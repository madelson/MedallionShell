using Medallion.Shell.Streams;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Medallion.Shell
{
    internal sealed class ProcessCommand : Command
    {
        internal ProcessCommand(ProcessStartInfo startInfo, bool throwOnError)
        {
            this.process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };

            var tasks = new List<Task>(capacity: 3) { CreateProcessTask(this.Process, throwOnError: throwOnError) };
            this.Process.Start();

            if (startInfo.RedirectStandardOutput)
            {
                this.standardOutputHandler = new ProcessStreamHandler(this.Process.StandardOutput.BaseStream);
                tasks.Add(this.standardOutputHandler.Task);
            }
            if (startInfo.RedirectStandardError)
            {
                this.standardErrorHandler = new ProcessStreamHandler(this.process.StandardError.BaseStream);
                tasks.Add(this.standardErrorHandler.Task);
            }
            if (startInfo.RedirectStandardInput)
            {
                this.standardInput = new ProcessStreamWriter(this.process.StandardInput);
            }

            this.task = this.CreateCombinedTask(tasks);
        }

        private async Task<CommandResult> CreateCombinedTask(List<Task> tasks)
        {
            await System.Threading.Tasks.Task.WhenAll(tasks).ConfigureAwait(false);
            return new CommandResult(this);
        }

        private readonly Process process;
        public override System.Diagnostics.Process Process
        {
            get { return this.process; }
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
            get { throw new NotImplementedException(); }
        }

        private readonly Task<CommandResult> task;
        public override Task<CommandResult> Task { get { return this.task; } }

        private static Task CreateProcessTask(Process process, bool throwOnError)
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
            return taskCompletionSource.Task;
        }

        protected override void DisposeInternal()
        {
            this.process.Dispose();
            this.task.Dispose();
        }
    }
}
