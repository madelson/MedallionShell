using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Medallion.Shell.Streams;

namespace Medallion.Shell
{
    internal sealed class PipedCommand : Command
    {
        private readonly Command first, second;
        private readonly Task<CommandResult> task;

        internal PipedCommand(Command first, Command second)
        {
            this.first = first;
            this.second = second;

            var pipeStreamsTask = PipeAsync(this.first.StandardOutput, this.second.StandardInput);
            this.task = this.CreateTask(pipeStreamsTask);
        }

        private async Task<CommandResult> CreateTask(Task pipeStreamsTask)
        {
            await pipeStreamsTask.ConfigureAwait(false);
            return await this.second.Task.ConfigureAwait(false);
        }

        public override Process Process
        {
            get { return this.second.Process; }
        }

        private IReadOnlyList<Process>? processes;
        public override IReadOnlyList<Process> Processes => this.processes ??= this.first.Processes.Concat(this.second.Processes).ToList().AsReadOnly();

        public override int ProcessId => this.second.ProcessId;

        private IReadOnlyList<int>? processIds;
        public override IReadOnlyList<int> ProcessIds => this.processIds ??= this.first.ProcessIds.Concat(this.second.ProcessIds).ToList().AsReadOnly();

        public override Task<CommandResult> Task => this.task;

        public override ProcessStreamWriter StandardInput => this.first.StandardInput;

        public override ProcessStreamReader StandardOutput => this.second.StandardOutput;

        public override ProcessStreamReader StandardError => this.second.StandardError;

        public override void Kill()
        {
            this.first.Kill();
            this.second.Kill();
        }

        public override string ToString() => $"{this.first} | {this.second}";

        protected override void DisposeInternal()
        {
            this.first.As<IDisposable>().Dispose();
            this.second.As<IDisposable>().Dispose();
        }

        private static async Task PipeAsync(ProcessStreamReader source, ProcessStreamWriter destination)
        {
            // NOTE: we use PipeFrom() since this will automatically flush any characters written to the
            // TextWriter APIs of destination first. However, we wrap with a using to ensure that source is
            // disposed rather than just source.BaseStream (which is all we pass to PipeFrom)
            using (source)
            {
                await destination.PipeFromAsync(source.BaseStream).ConfigureAwait(false);
            }
        }
    }
}
