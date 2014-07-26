using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Diagnostics;

namespace Medallion.Shell
{
    /// <summary>
    /// Acts as an abstract class for handling the escaping of arguments when passed to a <see cref="Process"/>.
    /// </summary>
    public abstract class CommandLineSyntax
    {
        /// <summary>
        /// Creates a combined argument string that can be used with <see cref="ProcessStartInfo.Arguments"/>. The combined
        /// string should escape all arguments appropriately so that the process receives the same strings as passed into this method
        /// </summary>
        /// <param name="arguments">the arguments to the process</param>
        /// <returns>the argument string</returns>
        public abstract string CreateArgumentString(IEnumerable<string> arguments);
    }
}
