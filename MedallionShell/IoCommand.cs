using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Medallion.Shell
{
    internal sealed class IoCommand : Command
    {
        private readonly Command command;
        private readonly Task<CommandResult> task;
        // for toString
        private readonly string @operator;
        private readonly object sourceOrSink;

        public IoCommand(Command command, Task ioTask, string @operator, object sourceOrSink)
        {
            this.command = command;
            this.task = this.CreateTask(ioTask);
            this.@operator = @operator;
            this.sourceOrSink = sourceOrSink;
        }

        private async Task<CommandResult> CreateTask(Task ioTask)
        {
            await ioTask.ConfigureAwait(false);
            return await this.command.Task.ConfigureAwait(false);
        }

        public override System.Diagnostics.Process Process
        {
            get { return this.command.Process; }
        }

        public override IReadOnlyList<System.Diagnostics.Process> Processes
        {
            get { return this.command.Processes; }
        }

        public override int ProcessId => this.command.ProcessId;
        public override IReadOnlyList<int> ProcessIds => this.command.ProcessIds;

        public override Streams.ProcessStreamWriter StandardInput
        {
            get { return this.command.StandardInput; }
        }

        public override Streams.ProcessStreamReader StandardOutput
        {
            get { return this.command.StandardOutput; }
        }

        public override Streams.ProcessStreamReader StandardError
        {
            get { return this.command.StandardError; }
        }

        public override Task<CommandResult> Task
        {
            get { return this.task; }
        }

        public override void Kill()
        {
            this.command.Kill();
        }

        public override string ToString() => $"{this.command} {this.@operator} {this.sourceOrSink}";

        protected override void DisposeInternal()
        {
            this.command.As<IDisposable>().Dispose();
        }
    }
}
