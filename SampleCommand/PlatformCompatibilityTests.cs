using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Medallion.Shell;

namespace SampleCommand
{
    public static class PlatformCompatibilityTests
    {
        public static readonly string SampleCommandPath = typeof(Program).Assembly.Location;

        public static readonly Shell TestShell = !RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? new Shell(options: o => o.StartInfo(si =>
            {
                // on linux, you can't run mono exes directly so instead we invoke them through Mono
                if (si.FileName == SampleCommandPath)
                {
                    si.Arguments = !string.IsNullOrEmpty(si.Arguments) ? $"{si.FileName} {si.Arguments}" : si.FileName;
                    si.FileName = "/usr/bin/mono";
                }
            }))
            : Shell.Default;

        public static void TestWriteAfterExit()
        {
            var command = TestShell.Run(SampleCommandPath, "exit", 1);
            command.Wait();
            command.StandardInput.WriteLine(); // no-op
            command.StandardInput.BaseStream.WriteAsync(new byte[1], 0, 1).Wait(); // no-op
        }

        public static void TestFlushAfterExit()
        {
            var command = TestShell.Run(SampleCommandPath, "exit", 1);
            command.Wait();
            command.StandardInput.Flush();
            command.StandardInput.BaseStream.Flush();
            command.StandardInput.BaseStream.FlushAsync().Wait();
        }

        public static void TestReadAfterExit()
        {
            var command = TestShell.Run(SampleCommandPath, "exit", 1);
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
            var command = TestShell.Run(SampleCommandPath, "exit", -1);
            var exitCode = command.Result.ExitCode;
            var isExpectedExitCode = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? exitCode == -1
                // Linux only returns the lower 8 bits of the exit code. Sounds like this may change in the future so we're just asserting that the lower 8 bits match
                // https://unix.stackexchange.com/questions/418784/what-is-the-min-and-max-values-of-exit-codes-in-linux/418802#418802?newreg=5f906406f0f04a1980a77192e3c64a6b
                : (exitCode & 0xff) == (-1 & 0xff);
            if (!isExpectedExitCode) { throw new InvalidOperationException($"Was: {command.Result.ExitCode}"); }
        }

        /// <summary>
        /// See PlatformCompatibilityHelper.SafeStart comment
        /// </summary>
        public static void TestExitWithOne()
        {
            var command = TestShell.Run(SampleCommandPath, "exit", 1);
            if (command.Result.ExitCode != 1) { throw new InvalidOperationException($"Was: {command.Result.ExitCode}"); }
        }

        public static void TestBadProcessFile()
        {
            var baseDirectory = Path.GetDirectoryName(SampleCommandPath);

            AssertThrows<Win32Exception>(() => Command.Run(baseDirectory));
            AssertThrows<Win32Exception>(() => Command.Run(Path.Combine(baseDirectory, "DOES_NOT_EXIST.exe")));
        }

        public static void TestAttaching()
        {
            var processCommand = TestShell.Run(SampleCommandPath, new[] { "sleep", "10000" });
            try
            {
                var processId = processCommand.ProcessId;
                if (!Command.TryAttachToProcess(processId, out _))
                {
                    throw new InvalidOperationException("Wasn't able to attach to the running process.");
                }
            }
            finally
            {
                processCommand.Kill();
            }
        }

        public static void TestWriteToStandardInput()
        {
            var command = TestShell.Run(SampleCommandPath, new[] { "echo" }, options: o => o.Timeout(TimeSpan.FromSeconds(5)));
            command.StandardInput.WriteLine("abcd");
            command.StandardInput.Dispose();
            if (command.Result.StandardOutput != ("abcd" + Environment.NewLine)) { throw new InvalidOperationException($"Was '{command.StandardOutput}'"); }
        }
        
        public static void TestArgumentRoundTrip()
        {
            var arguments = new[]
            {
                @"c:\temp",
                @"a\\b",
                @"\\\",
                @"``\`\\",
                @"C:\temp\blah",
                " leading and trailing\twhitespace!  ",
            };
            var command = TestShell.Run(SampleCommandPath, new[] { "argecho" }.Concat(arguments), o => o.ThrowOnError());
            var outputLines = command.StandardOutput.GetLines().ToArray();
            command.Wait();
            if (!outputLines.SequenceEqual(arguments))
            {
                throw new InvalidOperationException($"Was {string.Join(" ", outputLines.Select((l, index) => $"'{l}' ({(index >= arguments.Length ? "EXTRA" : (l == arguments[index]).ToString())})"))}");
            }
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
