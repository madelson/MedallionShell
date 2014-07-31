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
                this.standardOutputHandler = new ProcessStreamHandler(this.process.StandardOutput);
                ioTasks.Add(this.standardOutputHandler.Task);
            }
            if (startInfo.RedirectStandardError)
            {
                this.standardErrorHandler = new ProcessStreamHandler(this.process.StandardError);
                ioTasks.Add(this.standardErrorHandler.Task);
            }
            if (startInfo.RedirectStandardInput)
            {
                this.standardInput = new ProcessStreamWriter(this.process.StandardInput);
            }

            this.task = this.CreateCombinedTask(processTask, ioTasks);
        }

        private async Task<CommandResult> CreateCombinedTask(Task processTask, List<Task> ioTasks)
        {
            await processTask.ConfigureAwait(false);
            var exitCode = this.process.ExitCode;
            if (this.disposeOnExit)
            {
                // clean up the process AFTER we capture the exit code
                this.process.Dispose();
            }

            await SystemTask.WhenAll(ioTasks).ConfigureAwait(false);
            return new CommandResult(exitCode, this);
        }

        private readonly Process process;
        public override System.Diagnostics.Process Process
        {
            get 
            {
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

        private readonly ProcessStreamHandler standardOutputHandler;
        public override ProcessStreamReader StandardOutput
        {
            get 
            {
                Throw<InvalidOperationException>.If(this.standardOutputHandler == null, "Standard output is not redirected");
                return this.standardOutputHandler.Reader;
            }
        }

        private readonly ProcessStreamHandler standardErrorHandler;
        public override Streams.ProcessStreamReader StandardError
        {
            get 
            {
                Throw<InvalidOperationException>.If(this.standardErrorHandler == null, "Standard error is not redirected");
                return this.standardOutputHandler.Reader;
            }
        }

        private readonly Task<CommandResult> task;
        public override Task<CommandResult> Task { get { return this.task; } }

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
            // http://stackoverflow.com/questions/4238345/asynchronously-wait-for-taskt-to-complete-with-timeout
            if (await SystemTask.WhenAny(task, SystemTask.Delay(timeout)).ConfigureAwait(false) != task) 
            {
                Log.WriteLine("Process timed out");
                try 
                {
                    process.Kill();
                }
                catch (Exception ex) 
                {
                    Log.WriteLine("Exception killing process: " + ex);
                }
                throw new TimeoutException("Process killed after exceeding timeout of " + timeout);
            }
        }

        protected override void DisposeInternal()
        {
            this.process.Dispose();
        }
    }
}
