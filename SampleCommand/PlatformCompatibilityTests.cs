using Medallion.Shell;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SampleCommand
{
    public static class PlatformCompatibilityTests
    {
        private static readonly string SampleCommandPath = typeof(Program).Assembly.Location;

        public static void TestWriteAfterExit()
        {
            var command = Command.Run(SampleCommandPath, "exit", 1);
            command.Wait();
            command.StandardInput.WriteLine(); // no-op
        }

        public static void TestReadAfterExit()
        {
            var command = Command.Run(SampleCommandPath, "exit", 1);
            command.Wait();
            if (command.StandardOutput.ReadLine() != null)
            {
                throw new InvalidOperationException("StdOut");
            }
            if (command.StandardError.ReadLine() != null)
            {
                throw new InvalidOperationException("StdErr");
            }
        }

        /// <summary>
        /// See SafeGetExitCode comment
        /// </summary>
        public static void TestExitWithMinusOne()
        {
            var command = Command.Run(SampleCommandPath, "exit", -1);
            if (command.Result.ExitCode != -1) { throw new InvalidOperationException($"Was: {command.Result.ExitCode}"); }
        }
    }
}
