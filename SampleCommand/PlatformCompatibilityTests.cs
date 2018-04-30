using Medallion.Shell;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
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
            command.StandardInput.BaseStream.WriteAsync(new byte[1], 0, 1).Wait(); // no-op
        }

        public static void TestFlushAfterExit()
        {
            var command = Command.Run(SampleCommandPath, "exit", 1);
            command.Wait();
            command.StandardInput.Flush();
            command.StandardInput.BaseStream.Flush();
            command.StandardInput.BaseStream.FlushAsync();
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

        /// <summary>
        /// See PlatformCompatibilityHelper.SafeStart comment
        /// </summary>
        public static void TestExitWithOne()
        {
            var command = Command.Run(SampleCommandPath, "exit", 1);
            if (command.Result.ExitCode != 1) { throw new InvalidOperationException($"Was: {command.Result.ExitCode}"); }
        }

        public static void TestBadProcessFile()
        {
            var baseDirectory = Path.GetDirectoryName(SampleCommandPath);

            AssertThrows<Win32Exception>(() => Command.Run(baseDirectory));
            AssertThrows<Win32Exception>(() => Command.Run(Path.Combine(baseDirectory, "DOES_NOT_EXIST.exe")));
        }

        private static void AssertThrows<TException>(Action action) where TException : Exception
        {
            try { action(); }
            catch (Exception ex)
            {
                if (ex.GetType() != typeof(TException)) { throw new InvalidOperationException($"Expected {typeof(TException)} but got {ex.GetType()}"); }
                return;
            }

            throw new InvalidOperationException($"Expected {typeof(TException)}, but no exception was thrown");
        }
    }
}
