using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Medallion.Shell
{
    internal sealed class PipedCommand : Command
    {
        private readonly Command first, second;

        internal PipedCommand(Command first, Command second)
        {
            Throw.IfNull(first, "first");
            Throw.IfNull(second, "second");

            this.first = first;
            this.second = second;

            this.first.StandardOutput.PipeTo(this.second.StandardInput.BaseStream);
            this.first.Task.ContinueWith(_ => this.second.StandardInput.Close());
        }

        public override Process Process
        {
            get { return this.second.Process; }
        }

        private IReadOnlyList<Process> processes;
        public override IReadOnlyList<Process> Processes
        {
            get { return this.processes ?? (this.processes = this.first.Processes.Concat(this.second.Processes).ToList().AsReadOnly()); }
        }

        public override Task<CommandResult> Task
        {
            get { return this.second.Task; }
        }

        public override StreamWriter StandardInput
        {
            get { return this.first.StandardInput; }
        }

        public override Streams.ProcessStreamReader StandardOutput
        {
            get { return this.second.StandardOutput; }
        }

        public override Streams.ProcessStreamReader StandardError
        {
            get { return this.second.StandardError; }
        }
    }
}
