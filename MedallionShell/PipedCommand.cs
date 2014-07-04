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
        }

        public override Process Process
        {
            get { return this.second.Process; }
        }

        private IReadOnlyList<Process> processes;
        public override IReadOnlyList<Process> Processes
        {
            get { return this.processes ?? (this.processes = new[] { this.first.Process, this.second.Process }.Where(p => p != null).ToArray()); }
        }

        public override Stream StandardInputStream
        {
            get { return this.first.StandardInputStream; }
        }

        public override System.IO.Stream StandardOutputStream
        {
            get { return this.second.StandardOutputStream; }
        }

        public override System.IO.Stream StandardErrorStream
        {
            get { return this.second.StandardErrorStream; }
        }

        public override Task<CommandResult> Task
        {
            get { return this.second.Task; }
        }
    }
}
