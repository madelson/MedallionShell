using Medallion.Shell.Streams;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Medallion.Shell
{
    public sealed class CommandResult
    { 
        internal CommandResult(Command command)
        {
            this.Command = command;
        }

        public Command Command { get; private set; }

        public int ExitCode { get { return this.Command.Process.ExitCode; } }

        /// <summary>
        /// Returns true iff the exit code is 0 (indicating success)
        /// </summary>
        public bool Success { get { return this.ExitCode == 0; } }

        public string StandardOutput { get { return this.Command.StandardOutput.GetContent(); } }

        public string StandardError { get { return this.Command.StandardError.GetContent(); } }
    }
}
