using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Medallion.Shell
{
    /// <summary>
    /// Represents a process that failed with a non-zero exit code. This will be thrown by a <see cref="Command"/>
    /// created with <see cref="Shell.Options.ThrowOnError"/> called
    /// </summary>
    public sealed class ErrorExitCodeException : Exception
    {
        internal ErrorExitCodeException(Process process)
            : base(string.Format("Process {0} ({1} {2}) exited with non-zero value {3}", process.Id, process.StartInfo.FileName, process.StartInfo.Arguments, process.ExitCode))
        {
            this.ExitCode = process.ExitCode;
        }

        /// <summary>
        /// The exit code of the process
        /// </summary>
        public int ExitCode { get; private set; }
    }
}
