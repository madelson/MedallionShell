using Medallion.Shell.Streams;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Medallion.Shell
{
    /// <summary>
    /// The result of a <see cref="Command"/>
    /// </summary>
    public sealed class CommandResult
    { 
        private readonly Lazy<string> standardOutput, standardError;

        internal CommandResult(int exitCode, Command command)
        {
            this.ExitCode = exitCode;
            this.standardOutput = new Lazy<string>(() => command.StandardOutput.ReadToEnd());
            this.standardError = new Lazy<string>(() => command.StandardError.ReadToEnd());
        }

        /// <summary>
        /// The exit code of the command's process
        /// </summary>
        public int ExitCode { get; private set; }

        /// <summary>
        /// Returns true iff the exit code is 0 (indicating success)
        /// </summary>
        public bool Success { get { return this.ExitCode == 0; } }

        /// <summary>
        /// If available, the full standard output text of the command
        /// </summary>
        public string StandardOutput { get { return this.standardOutput.Value; } }

        /// <summary>
        /// If available, the full standard error text of the command
        /// </summary>
        public string StandardError { get { return this.standardError.Value; } }
    }
}
