using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using SampleCommand;

#if !NETCOREAPP2_2
// don't allow things to hang when running on a CI server
[assembly: Timeout(60000)]
#endif

namespace Medallion.Shell.Tests
{
    public static class UnitTestHelpers
    {
        public static string SampleCommand => PlatformCompatibilityTests.SampleCommandPath;
        public static Shell TestShell => PlatformCompatibilityTests.TestShell;
        public static string DotNetPath => PlatformCompatibilityTests.DotNetPath;

        public static Shell MakeTestShell(Action<Shell.Options> options) => new Shell(TestShell.Configuration + options);

        public static T ShouldEqual<T>(this T @this, T that, string? message = null)
        {
            Assert.AreEqual(that, @this, message);
            return @this;
        }

        public static T ShouldNotEqual<T>(this T @this, T that, string? message = null)
        {
            Assert.AreNotEqual(that, @this, message);
            return @this;
        }

        public static string ShouldContain(this string haystack, string needle, string? message = null)
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
