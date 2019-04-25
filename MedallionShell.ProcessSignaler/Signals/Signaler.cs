using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace Medallion.Shell.Signals
{
    internal static class Signaler
    {
        // implementation based on https://stackoverflow.com/questions/813086/can-i-send-a-ctrl-c-sigint-to-an-application-on-windows
        // + some additional research / experimentation
        
        public static int Signal(int processId, NativeMethods.CtrlType ctrlType)
        {
            // first detach from the current console if one exists. We don't check the exit
            // code since the only documented fail case is not having a console
            NativeMethods.FreeConsole();

            // attach to the child's console
            return NativeMethods.AttachConsole(checked((uint)processId))
                // disable signal handling for our program
                // from https://docs.microsoft.com/en-us/windows/console/setconsolectrlhandler:
                // "Calling SetConsoleCtrlHandler with the NULL and TRUE arguments causes the calling process to ignore CTRL+C signals"
                && NativeMethods.SetConsoleCtrlHandler(null, true)
                // send the signal
                && NativeMethods.GenerateConsoleCtrlEvent(ctrlType, NativeMethods.AllProcessesWithCurrentConsoleGroup)
                ? 0
                : Marshal.GetLastWin32Error();
        }
    }
}
