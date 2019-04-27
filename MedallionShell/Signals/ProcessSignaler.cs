using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace Medallion.Shell.Signals
{
    internal static class ProcessSignaler
    {
        public static Task<bool> TrySignalAsync(int processId, CommandSignal signal)
        {
            return PlatformCompatibilityHelper.IsWindows
                ? WindowsProcessSignaler.TrySignalAsync(processId, (NativeMethods.CtrlType)signal.Value)
                : Task.FromResult(UnixProcessSignaler.TrySignal(processId, signal.Value));
        }
    }
}
