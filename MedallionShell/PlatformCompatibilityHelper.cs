using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Medallion.Shell.Streams;

namespace Medallion.Shell
{
    internal static class PlatformCompatibilityHelper
    {
        // see http://www.mono-project.com/docs/faq/technical/
        public static readonly bool IsMono = Type.GetType("Mono.Runtime") != null;

        public static bool IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

        public static readonly CommandLineSyntax DefaultCommandLineSyntax =
            IsMono && !IsWindows
                ? new MonoUnixCommandLineSyntax()
                : new WindowsCommandLineSyntax();

        public static Stream WrapStandardInputStreamIfNeeded(Stream stream)
        {
            return IsMono || !IsWindows ? new CompatibilityStandardInputWrapperStream(stream) : stream;
        }

        public static int SafeGetExitCode(this Process process)
        {
            if (IsMono)
            {
                // an oddity of Mono is that it cannot return an exit code of -1; interpreting this as something
                // else. Unfortunately, this is what we get when calling Kill() on a .NET process on Windows. This
                // hack works around the issue. See https://github.com/mono/mono/blob/master/mcs/class/referencesource/System/services/monitoring/system/diagnosticts/Process.cs
                // for where this happens in the Mono source
                try { return process.ExitCode; }
                catch (InvalidOperationException ex)
                    when (ex.Message == "Cannot get the exit code from a non-child process on Unix")
                {
                    return -1;
                }
            }

            return process.ExitCode;
        }

        /// <summary>
        /// Starts the given <paramref name="process"/> and captures the standard IO streams. This method works around Mono.Android-specific
        /// issue https://github.com/madelson/MedallionShell/issues/22, where a process that exits quickly causes the initialization of
        /// the standard input writer to fail (since setting AutoFlush = true triggers a write which on Mono crashes for a closed process).
        ///
        /// If https://github.com/mono/mono/issues/8478 is ever addressed, we wouldn't need this any more.
        /// </summary>
        public static bool SafeStart(this Process process, out StreamWriter? standardInput, out StreamReader? standardOutput, out StreamReader? standardError)
        {
            var redirectStandardInput = process.StartInfo.RedirectStandardInput;
            var redirectStandardOutput = process.StartInfo.RedirectStandardOutput;
            var redirectStandardError = process.StartInfo.RedirectStandardError;

            try
            {
                process.Start();

                // adding this code allows for a sort-of replication of
                // https://github.com/madelson/MedallionShell/issues/22 on non-Android platforms
                // process.StandardInput.BaseStream.Write(new byte[1000], 0, 1000);
                // process.StandardInput.BaseStream.Flush();
            }
            catch (IOException ex)
                // note that AFAIK the exact type check here isn't necessary, but it seems more robust against
                // other types of IOExceptions (e. g. FileNotFoundException, PathTooLongException) that could in
                // theory be thrown here and trigger this
                when (IsMono && ex.GetType() == typeof(IOException))
            {
                standardInput = redirectStandardInput ? new StreamWriter(Stream.Null, Console.InputEncoding) { AutoFlush = true } : null;
                standardOutput = redirectStandardOutput ? new StreamReader(Stream.Null, Console.OutputEncoding) : null;
                standardError = redirectStandardError ? new StreamReader(Stream.Null, Console.OutputEncoding) : null;
                return false;
            }

            standardInput = redirectStandardInput ? process.StandardInput : null;
            standardOutput = redirectStandardOutput ? process.StandardOutput : null;
            standardError = redirectStandardError ? process.StandardError : null;
            return true;
        }
    }
}
