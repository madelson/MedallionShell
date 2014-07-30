using Medallion.Shell.Streams;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Medallion.Shell
{
    // TODO rethink this class
    // TODO how will ExitCode work on Dispose()?

    /// <summary>
    /// The result of a <see cref="Command"/>
    /// </summary>
    public sealed class CommandResult
    { 
        internal CommandResult(Command command)
        {
            this.Command = command;
        }

        /// <summary>
        /// The command
        /// </summary>
        public Command Command { get; private set; }

        /// <summary>
        /// The exit code of the command's process
        /// </summary>
        public int ExitCode { get { return this.Command.Process.ExitCode; } }

        /// <summary>
        /// Returns true iff the exit code is 0 (indicating success)
        /// </summary>
        public bool Success { get { return this.ExitCode == 0; } }

        /// <summary>
        /// If available, the full standard output text of the command
        /// </summary>
        public string StandardOutput { get { return this.Command.StandardOutput.ReadContent(); } }

        /// <summary>
        /// If available, the full standard error text of the command
        /// </summary>
        public string StandardError { get { return this.Command.StandardError.ReadContent(); } }
    }
}
