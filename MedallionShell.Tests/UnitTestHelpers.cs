using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Medallion.Shell.Tests
{
    public static class UnitTestHelpers
    {
        public static string SampleCommand => SampleCommandPath.Value;

        private static readonly Lazy<string> SampleCommandPath = new Lazy<string>(() =>
        {
            var binDirectory = Path.GetDirectoryName(typeof(UnitTestHelpers).Assembly.Location);
            var sampleCommandExePath = Path.Combine(binDirectory, "SampleCommand.exe");
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return sampleCommandExePath;
            }

            const string MonoPath = "/usr/bin/mono";
            if (PlatformCompatibilityHelper.IsMono)
            {
                var shellScriptPath = Path.Combine(binDirectory, "SampleCommand.sh");
                // from https://www.mono-project.com/archived/guiderunning_mono_applications/
                File.WriteAllText(
                    Path.Combine(binDirectory, shellScriptPath),
                    $"!/bin/sh{Environment.NewLine}{MonoPath} {sampleCommandExePath} \"$@\""
                );

                Command.Run("chmod", new[] { "u+x", shellScriptPath }, options: o => o.ThrowOnError()).Wait();
                return shellScriptPath;
            }

            throw new NotSupportedException();
        });

        public static T ShouldEqual<T>(this T @this, T that, string message = null)
        {
            Assert.AreEqual(that, @this, message);
            return @this;
        }

        public static T ShouldNotEqual<T>(this T @this, T that, string message = null)
        {
            Assert.AreNotEqual(that, @this, message);
            return @this;
        }

        public static string ShouldContain(this string haystack, string needle, string message = null)
        {
            Assert.IsNotNull(haystack, $"Expected: contains '{needle}'. Was: NULL{(message != null ? $" ({message})" : string.Empty)}");
            if (!haystack.Contains(needle))
            {
                Assert.Fail($"Expected: contains '{needle}'. Was: '{haystack}'{(message != null ? $" ({message})" : string.Empty)}");
            }

            return haystack;
        }
    }
}
