using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Medallion.Shell;
using Medallion.Shell.Signals;
using NUnit.Framework;
using SampleCommand;

namespace MedallionShell.Tests
{
    using static Medallion.Shell.Tests.UnitTestHelpers;

    public class SignalTest
    {
        [Test]
        public async Task CanSendControlC()
        {
            var command = TestShell.Run(SampleCommand, "sleep", "10000");

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                WindowsProcessSignaler.HasSameConsole(command.ProcessId).ShouldEqual(false, "sanity check console setup");
            }

            (await command.TrySignalAsync(CommandSignal.ControlC)).ShouldEqual(true);

            command.Task.Wait(TimeSpan.FromSeconds(5)).ShouldEqual(true);
        }

        [Test]
        public async Task CanSendControlCToPipeline()
        {
            var command = TestShell.Run(SampleCommand, "sleep", "10000")
                | TestShell.Run(SampleCommand, "pipe") 
                | TestShell.Run(SampleCommand, "pipe");

            (await command.TrySignalAsync(CommandSignal.ControlC)).ShouldEqual(true);
            command.Task.Wait(TimeSpan.FromSeconds(5)).ShouldEqual(true);
        }

        [Test]
        public async Task HandlesEdgeCases()
        {
            var command = TestShell.Run(SampleCommand, "sleep", "10000");
            Assert.Throws<ArgumentNullException>(() => command.TrySignalAsync(null!).GetType());

            command.Kill();
            await command.Task;
            (await command.TrySignalAsync(CommandSignal.ControlC)).ShouldEqual(false, "exited");

            command.As<IDisposable>().Dispose();
            Assert.Throws<ObjectDisposedException>(() => command.TrySignalAsync(CommandSignal.ControlC).GetType());
        }

        [Test]
        public async Task CanSendUnixSignal([Values(3 /* SIGQUIT */, 6 /* SIGABORT */, 9 /* SIGKILL */)] int unixSignal)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Assert.Pass("tests unix-specific behavior");
            }

            var command = TestShell.Run(SampleCommand, "sleep", "10000");

            (await command.TrySignalAsync(CommandSignal.ControlC)).ShouldEqual(true);

            command.Task.Wait(TimeSpan.FromSeconds(5)).ShouldEqual(true);
        }

        [TestCaseSource(nameof(GetCtrlTypes))]
        public async Task CanSendSignalToSelf(int signal)
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Assert.Pass("Tests windows-specific behavior");
            }
            
            Command.TryAttachToProcess(ProcessHelper.CurrentProcessId, out var thisCommand).ShouldEqual(true);

            using (var manualResetEvent = new ManualResetEventSlim(initialState: false))
            {
                NativeMethods.ConsoleCtrlDelegate handler = receivedSignal =>
                {
                    if ((int)receivedSignal == signal)
                    {
                        manualResetEvent.Set();
                        return true; // handled
                    }
                    return false;
                };
                if (!NativeMethods.SetConsoleCtrlHandler(handler, add: true))
                {
                    Marshal.ThrowExceptionForHR(Marshal.GetHRForLastWin32Error());
                }

                try
                {
                    (await thisCommand!.TrySignalAsync(CommandSignal.FromSystemValue(signal))).ShouldEqual(true);
                    manualResetEvent.Wait(TimeSpan.FromSeconds(5)).ShouldEqual(true);
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

        public static IEnumerable<int> GetCtrlTypes() => Enum.GetValues(typeof(NativeMethods.CtrlType)).Cast<NativeMethods.CtrlType>().Select(t => (int)t);
    }
}
