using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Medallion.Shell.Streams;
using SystemTask = System.Threading.Tasks.Task;

namespace Medallion.Shell
{
    internal sealed class ProcessCommand : Command
    {
        private readonly bool disposeOnExit;
        /// <summary>
        /// Used for <see cref="ToString"/>
        /// </summary>
        private readonly string fileName, arguments;

        internal ProcessCommand(
            ProcessStartInfo startInfo,
            bool throwOnError,
            bool disposeOnExit,
            TimeSpan timeout,
            CancellationToken cancellationToken,
            Encoding standardInputEncoding)
        {
            this.disposeOnExit = disposeOnExit;
            this.fileName = startInfo.FileName;
            this.arguments = startInfo.Arguments;
            this.process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };

            var processMonitoringTask = CreateProcessMonitoringTask(this.process);

            this.process.SafeStart(out var processStandardInput, out var processStandardOutput, out var processStandardError);

            var ioTasks = new List<Task>(capacity: 2);
            if (startInfo.RedirectStandardOutput)
            {
                this.standardOutputReader = new InternalProcessStreamReader(processStandardOutput);
                ioTasks.Add(this.standardOutputReader.Task);
            }
            if (startInfo.RedirectStandardError)
            {
                this.standardErrorReader = new InternalProcessStreamReader(processStandardError);
                ioTasks.Add(this.standardErrorReader.Task);
            }
            if (startInfo.RedirectStandardInput)
            {
                // unfortunately, changing the encoding can't be done via ProcessStartInfo so we have to do it manually here.
                // See https://github.com/dotnet/corefx/issues/20497

                var wrappedStream = PlatformCompatibilityHelper.WrapStandardInputStreamIfNeeded(processStandardInput.BaseStream);
                var standardInputEncodingToUse = standardInputEncoding ?? processStandardInput.Encoding;
                var streamWriter = wrappedStream == processStandardInput.BaseStream && Equals(standardInputEncodingToUse, processStandardInput.Encoding)
                    ? processStandardInput
                    : new StreamWriter(wrappedStream, standardInputEncodingToUse);
                this.standardInput = new ProcessStreamWriter(streamWriter);
            }

            // according to https://docs.microsoft.com/en-us/dotnet/api/system.diagnostics.process.id?view=netcore-1.1#System_Diagnostics_Process_Id,
            // this can throw PlatformNotSupportedException on some older windows systems in some StartInfo configurations. To be as
            // robust as possible, we thus make this a best-effort attempt
            try { this.processIdOrExceptionDispatchInfo = this.process.Id; }
            catch (PlatformNotSupportedException processIdException)
            {
                this.processIdOrExceptionDispatchInfo = ExceptionDispatchInfo.Capture(processIdException);
            }

            // we only set up timeout and cancellation AFTER starting the process. This prevents a race
            // condition where we immediately try to kill the process before having started it and then proceed to start it.
            // While we could avoid starting at all in such cases, that would leave the command in a weird state (no PID, no streams, etc)
            var processTask = CreateProcessTask(this.process, processMonitoringTask, throwOnError, timeout, cancellationToken);
            this.task = this.CreateCombinedTask(processTask, ioTasks);
        }

        private async Task<CommandResult> CreateCombinedTask(Task<int> processTask, IReadOnlyList<Task> ioTasks)
        {
            int exitCode;
            try
            {
                // first, wait for the process to exit. This can throw
                exitCode = await processTask.ConfigureAwait(false);
            }
            finally
            {
                if (this.disposeOnExit)
                {
                    // clean up the process AFTER we capture the exit code
                    this.process.Dispose();
                }
            }

            await SystemTask.WhenAll(ioTasks).ConfigureAwait(false);
            return new CommandResult(exitCode, this);
        }

        private readonly Process process;
        public override System.Diagnostics.Process Process
        {
            get
            {
                this.ThrowIfDisposed();
                Throw<InvalidOperationException>.If(
                    this.disposeOnExit,
                    "Process can only be accessed when the command is not set to dispose on exit. This is to prevent non-deterministic code which may access the process before or after it exits"
                );
                return this.process;
            }
        }

        private IReadOnlyList<Process> processes;
        public override IReadOnlyList<Process> Processes
        {
            get { return this.processes ?? (this.processes = new ReadOnlyCollection<Process>(new[] { this.Process })); }
        }

        private readonly object processIdOrExceptionDispatchInfo;
        public override int ProcessId
        {
            get
            {
                this.ThrowIfDisposed();

                if (this.processIdOrExceptionDispatchInfo is ExceptionDispatchInfo exceptionDispatchInfo)
                {
                    exceptionDispatchInfo.Throw();
                }

                return (int)this.processIdOrExceptionDispatchInfo;
            }
        }

        private IReadOnlyList<int> processIds;
        public override IReadOnlyList<int> ProcessIds
        {
            get { return this.processIds ?? (this.processIds = new ReadOnlyCollection<int>(new[] { this.ProcessId })); }
        }

        private readonly ProcessStreamWriter standardInput;
        public override ProcessStreamWriter StandardInput
        {
            get
            {
                Throw<InvalidOperationException>.If(this.standardInput == null, "Standard input is not redirected");
                return this.standardInput;
            }
        }

        private readonly InternalProcessStreamReader standardOutputReader;
        public override ProcessStreamReader StandardOutput
        {
            get
            {
                Throw<InvalidOperationException>.If(this.standardOutputReader == null, "Standard output is not redirected");
                return this.standardOutputReader;
            }
        }

        private readonly InternalProcessStreamReader standardErrorReader;
        public override Streams.ProcessStreamReader StandardError
        {
            get
            {
                Throw<InvalidOperationException>.If(this.standardErrorReader == null, "Standard error is not redirected");
                return this.standardErrorReader;
            }
        }

        private readonly Task<CommandResult> task;
        public override Task<CommandResult> Task { get { return this.task; } }

        public override string ToString() => this.fileName + " " + this.arguments;

        public override void Kill()
        {
            this.ThrowIfDisposed();

            TryKillProcess(this.process);
        }

        /// <summary>
        /// Creates a <see cref="SystemTask"/> which watches for the given <paramref name="process"/> to exit.
        /// This must be configured BEFORE starting the process since otherwise there is a race between subscribing
        /// to the exited event and the event firing.
        /// </summary>
        private static Task CreateProcessMonitoringTask(Process process)
        {
            var taskBuilder = new TaskCompletionSource<bool>();
            // note: calling TrySetResult here on the off chance that a bug causes this event to fire twice.
            // Apparently old versions of mono had such a bug. The issue is that any exception in this event
            // can down the process since it fires on an unprotected threadpool thread
            process.Exited += (o, e) => taskBuilder.TrySetResult(false);

            return taskBuilder.Task;
        }

        private static readonly object CompletedSentinel = new object(),
            CanceledSentinel = new object();

        /// <summary>
        /// Creates a task which will either return the exit code for the <paramref name="process"/>, throw
        /// <see cref="ErrorExitCodeException"/>, throw <see cref="TimeoutException"/> or be canceled. When
        /// the returned <see cref="Task"/> completes, the <paramref name="process"/> is guaranteed to have
        /// exited
        /// </summary>
        private static Task<int> CreateProcessTask(
            Process process,
            Task processMonitoringTask,
            bool throwOnError,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            // the implementation here is somewhat tricky. We want to guarantee that the process has exited when the resulting
            // task returns. That means that we can't trigger task completion on timeout or cancellation. At the same time, we
            // want the task result to match the exit condition. The approach is to use a TCS for the returned task that gets completed
            // in a continuation on the monitoring task. We can't use the continuation itself as the result because it won't be canceled
            // even from throwing an OCE. To determine the result of the TCS, we set off a "race" between timeout, cancellation, and
            // processExit to set a resultObject, which is done thread-safely using Interlocked. When timeout or cancellation win, they
            // also kill the process to propagate the continuation execution

            var taskBuilder = new TaskCompletionSource<int>();
            var disposables = new List<IDisposable>();
            object resultObject = null;

            if (cancellationToken.CanBeCanceled)
            {
                disposables.Add(cancellationToken.Register(() =>
                {
                    if (Interlocked.CompareExchange(ref resultObject, CanceledSentinel, null) == null)
                    {
                        TryKillProcess(process); // if cancellation wins the race, kill the process
                    }
                }));
            }

            if (timeout != Timeout.InfiniteTimeSpan)
            {
                var timeoutSource = new CancellationTokenSource(timeout);
                disposables.Add(timeoutSource.Token.Register(() =>
                {
                    var timeoutException = new TimeoutException("Process killed after exceeding timeout of " + timeout);
                    if (Interlocked.CompareExchange(ref resultObject, timeoutException, null) == null)
                    {
                        TryKillProcess(process); // if timeout wins the race, kill the process
                    }
                }));
                disposables.Add(timeoutSource);
            }

            processMonitoringTask.ContinueWith(
                _ =>
                {
                    var resultObjectValue = Interlocked.CompareExchange(ref resultObject, CompletedSentinel, null);
                    if (resultObjectValue == null) // process completed naturally
                    {
                        // try-catch because in theory any process property access could fail if someone
                        // disposes out from under us
                        try
                        {
                            var exitCode = process.SafeGetExitCode();
                            if (throwOnError && exitCode != 0)
                            {
                                taskBuilder.SetException(new ErrorExitCodeException(process));
                            }
                            else
                            {
                                taskBuilder.SetResult(exitCode);
                            }
                        }
                        catch (Exception ex)
                        {
                            taskBuilder.SetException(ex);
                        }
                    }
                    else if (resultObjectValue == CanceledSentinel)
                    {
                        taskBuilder.SetCanceled();
                    }
                    else
                    {
                        taskBuilder.SetException((Exception)resultObjectValue);
                    }

                    // perform cleanup
                    disposables.ForEach(d => d.Dispose());
                },
                TaskContinuationOptions.ExecuteSynchronously
            );

            return taskBuilder.Task;
        }

        private static void TryKillProcess(Process process)
        {
            try
            {
                // the try-catch is because Kill() will throw if the process is disposed
                process.Kill();
            }
            catch (Exception ex)
            {
                Log.WriteLine("Exception killing process: " + ex);
            }
        }

        protected override void DisposeInternal()
        {
            this.process.Dispose();
        }
    }
}
