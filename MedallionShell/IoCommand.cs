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

        public IoCommand(Command command, Task ioTask)
        {
            this.command = command;
            this.task = this.CreateTask(ioTask);
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

        protected override void DisposeInternal()
        {
            this.command.As<IDisposable>().Dispose();
        }
    }
}
