using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Medallion.Shell.Signals;

namespace Medallion.Shell
{
    internal static class Program
    {
        public static int Main(string[] args)
        {
            var processId = int.Parse(args[0]);
            var signal = (NativeMethods.CtrlType)int.Parse(args[1]);
            return Signaler.Signal(processId, signal);
        }
    }
}
