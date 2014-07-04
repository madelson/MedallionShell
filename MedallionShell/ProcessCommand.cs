using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Medallion.Shell
{
    internal sealed class ProcessCommand : Command
    {
        private readonly Task processTask;

        internal ProcessCommand(ProcessStartInfo startInfo)
        {
            this.process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
            this.processTask = CreateProcessTask(this.Process);
            this.Process.Start();
        }

        private readonly Process process;
        public override System.Diagnostics.Process Process
        {
            get { return this.process; }
        }

        private IReadOnlyList<Process> processes;
        public override IReadOnlyList<System.Diagnostics.Process> Processes
        {
            get { return this.processes ?? (this.processes = new[] { this.Process }); }
        }

        public override System.IO.Stream StandardInputStream
        {
            get { return this.Process.StandardInput.BaseStream; }
        }

        public override System.IO.Stream StandardOutputStream
        {
            get { return this.Process.StandardOutput.BaseStream; }
        }

        public override System.IO.Stream StandardErrorStream
        {
            get { return this.Process.StandardError.BaseStream; }
        }

        public override Task<CommandResult> Task
        {
            get { throw new NotImplementedException(); }
        }

        private async Task<CommandResult> CreateTask()
        {
            await this.processTask;

            return new CommandResult(this.Process.ExitCode);
        }

        private static Task CreateProcessTask(Process process)
        {
            var taskCompletionSource = new TaskCompletionSource<bool>();
            process.Exited += (o, e) => taskCompletionSource.SetResult(true);
            return taskCompletionSource.Task;
        }
    }
}
