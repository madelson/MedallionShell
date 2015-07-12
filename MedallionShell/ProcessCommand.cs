using Medallion.Shell.Streams;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SystemTask = System.Threading.Tasks.Task;

namespace Medallion.Shell
{
    internal sealed class ProcessCommand : Command
    {
        private readonly bool disposeOnExit;

        internal ProcessCommand(ProcessStartInfo startInfo, bool throwOnError, bool disposeOnExit, TimeSpan timeout)
        {
            this.disposeOnExit = disposeOnExit;
            this.process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };

            var processTask = CreateProcessTask(this.process, throwOnError: throwOnError, timeout: timeout);

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
                this.standardInput = new ProcessStreamWriter(this.process.StandardInput);
            }

            this.task = this.CreateCombinedTask(processTask, ioTasks);
        }

        private async Task<CommandResult> CreateCombinedTask(Task processTask, List<Task> ioTasks)
        {
            int exitCode;
            try
            {
                await processTask.ConfigureAwait(false);
                exitCode = this.process.ExitCode;
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

        private static Task CreateProcessTask(Process process, bool throwOnError, TimeSpan timeout)
        {
            var taskCompletionSource = new TaskCompletionSource<bool>();
            process.Exited += (o, e) =>
            {
                Log.WriteLine("Received exited event from {0}", process.Id);

                if (throwOnError && process.ExitCode != 0)
                {
                    taskCompletionSource.SetException(new ErrorExitCodeException(process));
                }
                else
                {
                    taskCompletionSource.SetResult(true);
                }
            };
            return timeout != Timeout.InfiniteTimeSpan
                ? AddTimeout(taskCompletionSource.Task, process, timeout)
                : taskCompletionSource.Task;
        }

        private static async Task AddTimeout(Task task, Process process, TimeSpan timeout)
        { 
            // wait for either the given task or the timeout to complete
            // http://stackoverflow.com/questions/4238345/asynchronously-wait-for-taskt-to-complete-with-timeout
            var completed = await SystemTask.WhenAny(task, SystemTask.Delay(timeout)).ConfigureAwait(false);

            // Task.WhenAny() swallows errors: wait for the completed task to propagate any errors that occurred
            await completed.ConfigureAwait(false);

            // if we timed out, kill the process
            if (completed != task) 
            {
                Log.WriteLine("Process timed out");
                TryKillProcess(process);
                throw new TimeoutException("Process killed after exceeding timeout of " + timeout);
            }
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
