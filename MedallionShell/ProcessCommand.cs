using Medallion.Shell.Streams;
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
using SystemTask = System.Threading.Tasks.Task;

namespace Medallion.Shell
{
    internal sealed class ProcessCommand : Command
    {
        private readonly bool disposeOnExit;
        
        internal ProcessCommand(
            ProcessStartInfo startInfo, 
            bool throwOnError, 
            bool disposeOnExit, 
            TimeSpan timeout,
            CancellationToken cancellationToken,
            Encoding standardInputEncoding)
        {
            this.disposeOnExit = disposeOnExit;
            this.process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };

            var processTask = CreateProcessTask(this.process, throwOnError: throwOnError);

            this.process.Start();

            var ioTasks = new List<Task>(capacity: 2);
            if (startInfo.RedirectStandardOutput)
            {
                this.standardOutputReader = new InternalProcessStreamReader(this.process.StandardOutput);
                ioTasks.Add(this.standardOutputReader.Task);
            }
            if (startInfo.RedirectStandardError)
            {
                this.standardErrorReader = new InternalProcessStreamReader(this.process.StandardError);
                ioTasks.Add(this.standardErrorReader.Task);
            }
            if (startInfo.RedirectStandardInput)
            {
                // unfortunately, this can't be done via ProcessStartInfo so we have to do it manually here.
                // See https://github.com/dotnet/corefx/issues/20497
                var streamWriter = standardInputEncoding == null || Equals(this.process.StandardInput.Encoding, standardInputEncoding)
                    ? this.process.StandardInput
                    : new StreamWriter(this.process.StandardInput.BaseStream, standardInputEncoding);
                this.standardInput = new ProcessStreamWriter(streamWriter);
            }

            // according to https://docs.microsoft.com/en-us/dotnet/api/system.diagnostics.process.id?view=netcore-1.1#System_Diagnostics_Process_Id,
            // this can throw PlatformNotSupportedException on some older windows systems in some StartInfo configurations. To be as
            // robust as possible, we thus make this a best-effort attempt
            try { this.processIdOrExceptionDispatchInfo = process.Id; }
            catch (PlatformNotSupportedException processIdException)
            {
                this.processIdOrExceptionDispatchInfo = ExceptionDispatchInfo.Capture(processIdException);
            }
            
            this.task = this.CreateCombinedTask(processTask.Task, timeout, cancellationToken, ioTasks);
        }

        private async Task<CommandResult> CreateCombinedTask(
            Task<int> processTask, 
            TimeSpan timeout,
            CancellationToken cancellationToken, 
            List<Task> ioTasks)
        {
            int exitCode;
            try
            {
                // we only set up timeout and cancellation AFTER starting the process. This prevents a race
                // condition where we immediately try to kill the process before having started it and then proceed to start it.
                // While we could avoid starting at all in such cases, that would leave the command in a weird state (no PID, no streams, etc)
                await this.HandleCancellationAndTimeout(processTask, cancellationToken, timeout).ConfigureAwait(false);

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

        private async Task HandleCancellationAndTimeout(Task<int> processTask, CancellationToken cancellationToken, TimeSpan timeout)
        {
            using (var cancellationOrTimeout = CancellationOrTimeout.TryCreate(cancellationToken, timeout))
            {
                if (cancellationOrTimeout != null)
                {
                    // wait for either cancellation/timeout or the process to finish
                    var completed = await SystemTask.WhenAny(cancellationOrTimeout.Task, processTask).ConfigureAwait(false);
                    if (completed == cancellationOrTimeout.Task)
                    {
                        // if cancellation/timeout finishes first, kill the process
                        TryKillProcess(this.process);
                        // propagate cancellation or timeout exception
                        await completed.ConfigureAwait(false);
                    }
                }
            }
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

        public override void Kill()
        {
            this.ThrowIfDisposed();

            TryKillProcess(this.process);
        }
        
        private static TaskCompletionSource<int> CreateProcessTask(Process process, bool throwOnError)
        {
            var taskCompletionSource = new TaskCompletionSource<int>();
            process.Exited += (o, e) =>
            {
                Log.WriteLine("Received exited event from {0}", process.Id);

                if (throwOnError && process.ExitCode != 0)
                {
                    taskCompletionSource.SetException(new ErrorExitCodeException(process));
                }
                else
                {
                    taskCompletionSource.SetResult(process.ExitCode);
                }
            };

            return taskCompletionSource;
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
