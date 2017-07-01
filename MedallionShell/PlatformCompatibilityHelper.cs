using System;
using System.Collections.Generic;
using System.Text;
using System.IO;
using Medallion.Shell.Streams;
using System.Diagnostics;

namespace Medallion.Shell
{
    internal static class PlatformCompatibilityHelper
    {
        // see http://www.mono-project.com/docs/faq/technical/
        private static readonly bool IsMono = Type.GetType("Mono.Runtime") != null;

        public static Stream WrapStandardInputStreamIfNeeded(Stream stream)
        {
            return IsMono ? new MonoStandardIOWrapperStream(stream) : stream;
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
    }
}
