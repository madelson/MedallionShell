using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Medallion.Shell.Signals
{
    internal static class WindowsProcessSignaler
    {
        // This class forks its signaling approach based on whether or not the target process shares a console with the current
        // process. This is needed because Windows signals hit all processes on the same console. Signaling processes
        // requires mucking with global state, so signaling processes on a different console is safer since we can do it from our 
        // embedded ProcessSignaler exe and thus isolate those modifications

        /// <summary>
        /// Since signaling from the current process requires mucking with global state (CTRL handlers), we limit to one
        /// concurrent access.
        /// </summary>
        private static readonly SemaphoreSlim SignalFromCurrentProcessLock = new SemaphoreSlim(initialCount: 1, maxCount: 1);

        public static async Task<bool> TrySignalAsync(int processId, NativeMethods.CtrlType signal)
        {
            if (HasSameConsole(processId))
            {
                return await SendSignalFromCurrentProcess(processId, signal).ConfigureAwait(false);
            }

            using (var file = await DeploySignalerExeAsync().ConfigureAwait(false))
            {
                var command = Command.Run(file.Path, new object[] { processId, (int)signal });
                return (await command.Task.ConfigureAwait(false)).Success;
            }
        }

        internal static bool HasSameConsole(int processId)
        {
            // see https://docs.microsoft.com/en-us/windows/console/getconsoleprocesslist
            // for instructions on calling this method

            uint processListCount = 1;
            uint[] processIdListBuffer;
            do
            {
                processIdListBuffer = new uint[processListCount];
                processListCount = NativeMethods.GetConsoleProcessList(processIdListBuffer, processListCount);
            }
            while (processListCount > processIdListBuffer.Length);

            checked
            {
                return processIdListBuffer.Take((int)processListCount)
                    .Contains(checked((uint)processId));
            }
        }

        private static async Task<bool> SendSignalFromCurrentProcess(int processId, NativeMethods.CtrlType signal)
        {
            await SignalFromCurrentProcessLock.WaitAsync().ConfigureAwait(false);
            try
            {
                using (var waitForSignalSemaphore = new SemaphoreSlim(initialCount: 0, maxCount: 1))
                {
                    NativeMethods.ConsoleCtrlDelegate handler = receivedSignal =>
                    {
                        if (receivedSignal == signal)
                        {
                            waitForSignalSemaphore.Release();
                            // if we're signaling another process on the same console, we return true
                            // to prevent the signal from bubbling. If we're signaling ourselves, we
                            // allow it to bubble since presumably that's what the caller wanted
                            return processId != ProcessHelper.CurrentProcessId;
                        }
                        return false;
                    };
                    if (!NativeMethods.SetConsoleCtrlHandler(handler, add: true))
                    {
                        Marshal.ThrowExceptionForHR(Marshal.GetHRForLastWin32Error());
                    }
                    try
                    {
                        if (!NativeMethods.GenerateConsoleCtrlEvent(signal, NativeMethods.AllProcessesWithCurrentConsoleGroup))
                        {
                            return false;
                        }
                        
                        // Wait until the signal has reached our handler and been handled to know that it is safe to
                        // remove the handler.
                        // Timeout here just to ensure we don't hang forever if something weird happens (e. g. someone
                        // else registers a handler concurrently with us).
                        return await waitForSignalSemaphore.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
                    }
                    finally
                    {
                        if (!NativeMethods.SetConsoleCtrlHandler(handler, add: false))
                        {
                            Marshal.ThrowExceptionForHR(Marshal.GetHRForLastWin32Error());
                        }
                    }
                }
            }
            finally
            {
                SignalFromCurrentProcessLock.Release();
            }
        }

        private static async Task<TemporaryExeFile> DeploySignalerExeAsync()
        {
            var tempDirectoryName = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDirectoryName);
            const string SignalerExeName = "MedallionShell.ProcessSignaler.exe";
            var exePath = Path.Combine(tempDirectoryName, SignalerExeName);
            using (var resourceStream = Helpers.GetMedallionShellAssembly().GetManifestResourceStream(SignalerExeName))
            using (var fileStream = new FileStream(exePath, FileMode.CreateNew, FileAccess.Write, FileShare.None, Constants.ByteBufferSize, useAsync: true))
            {
                await resourceStream.CopyToAsync(fileStream).ConfigureAwait(false);
            }
            
            return new TemporaryExeFile(exePath);
        }

        private class TemporaryExeFile : IDisposable
        {
            private string _path;

            public TemporaryExeFile(string path)
            {
                this._path = path;
            }

            public string Path => this._path;

            public void Dispose()
            {
                var toDelete = Interlocked.Exchange(ref this._path, null);
                if (toDelete != null)
                {
                    File.Delete(toDelete);
                    Directory.Delete(System.IO.Path.GetDirectoryName(toDelete));
                }
            }
        }
    }
}
