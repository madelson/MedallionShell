using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace Medallion.Shell.Signals
{
    internal static class NativeMethods
    {
        // from https://docs.microsoft.com/en-us/windows/console/generateconsolectrlevent
        public const uint AllProcessesWithCurrentConsoleGroup = 0;

        public enum CtrlType : uint
        {
            CTRL_C_EVENT = 0,
            CTRL_BREAK_EVENT = 1,
        }

        public delegate bool ConsoleCtrlDelegate(CtrlType ctrlType);

        // PC003 complains about methods that aren't supported on UWP. I looked into multi-targeting against
        // UWP with https://github.com/dotnet/sdk/issues/1408 and https://github.com/onovotny/MSBuildSdkExtras,
        // but since UWP doesn't even support Process this didn't feel worthwhile
#pragma warning disable PC003
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern uint GetConsoleProcessList(uint[] lpdwProcessList, uint dwProcessCount);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool SetConsoleCtrlHandler(ConsoleCtrlDelegate? handlerRoutine, bool add);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool GenerateConsoleCtrlEvent(CtrlType dwCtrlEvent, uint dwProcessGroupId);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool AttachConsole(uint dwProcessId);

        [DllImport("kernel32.dll")] // no SetLastError since we don't care if this fails
        public static extern bool FreeConsole();

        // http://man7.org/linux/man-pages/man2/kill.2.html
        // from https://developers.redhat.com/blog/2019/03/25/using-net-pinvoke-for-linux-system-functions/
        [DllImport("libc", SetLastError = true)]
#pragma warning disable SA1300
        public static extern int kill(int pid, int sig);
#pragma warning restore SA1300
#pragma warning restore PC003
    }
}
