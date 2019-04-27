using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace Medallion.Shell.Signals
{
    internal static class UnixProcessSignaler
    {
        public static bool TrySignal(int processId, int signal)
        {
            return NativeMethods.kill(pid: processId, sig: signal) == 0;
        }
    }
}
