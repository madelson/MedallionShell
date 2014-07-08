using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Medallion.Shell
{
    public sealed class CommandResult
    {
        internal CommandResult(int exitCode)
        {
            this.exitCode = exitCode;
        }

        private readonly int exitCode;
        public int ExitCode { get { return this.exitCode; } }

        /// <summary>
        /// Returns true iff the exit code is 0 (indicating success)
        /// </summary>
        public bool Success { get { return this.ExitCode == 0; } }
    }
}
