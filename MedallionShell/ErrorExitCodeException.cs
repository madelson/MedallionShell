using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Medallion.Shell
{
    public sealed class ErrorExitCodeException : Exception
    {
        internal ErrorExitCodeException(Process process)
            : base(string.Format("Process {0} ({1} {2}) exited with non-zero value {3}", process.Id, process.StartInfo.FileName, process.StartInfo.Arguments, process.ExitCode))
        {
            this.ExitCode = process.ExitCode;
        }

        public int ExitCode { get; private set; }
    }
}
