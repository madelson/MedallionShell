using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using SampleCommand;

namespace Medallion.Shell.Tests
{
    using System.IO;
    using static UnitTestHelpers;

    public class PlatformCompatibilityTest
    {
        [Test]
        public void TestReadAfterExit() => RunTest(() => PlatformCompatibilityTests.TestReadAfterExit());

        [Test]
        public void TestWriteAfterExit() => RunTest(() => PlatformCompatibilityTests.TestWriteAfterExit());

        [Test]
        public void TestFlushAfterExit() => RunTest(() => PlatformCompatibilityTests.TestFlushAfterExit());

        [Test]
        public void TestExitWithMinusOne() => RunTest(() => PlatformCompatibilityTests.TestExitWithMinusOne());

        [Test]
        public void TestExitWithOne() => RunTest(() => PlatformCompatibilityTests.TestExitWithOne());

        [Test]
        public void TestBadProcessFile() => RunTest(() => PlatformCompatibilityTests.TestBadProcessFile());

        [Test]
        public void TestAttaching() => RunTest(() => PlatformCompatibilityTests.TestAttaching());

        [Test]
        public void TestWriteToStandardInput() => RunTest(() => PlatformCompatibilityTests.TestWriteToStandardInput());

        [Test]
        public void TestArgumentsRoundTrip() => RunTest(() => PlatformCompatibilityTests.TestArgumentsRoundTrip());

        [Test]
        public void TestKill() => RunTest(() => PlatformCompatibilityTests.TestKill());

        private static void RunTest(Expression<Action> testMethod)
        {
            var compiled = testMethod.Compile();
            Assert.DoesNotThrow(() => compiled(), "should run on current platform");

            // don't bother testing running Mono from .NET Core
#if !NETCOREAPP2_2
            var methodName = ((MethodCallExpression)testMethod.Body).Method.Name;

            var monoPath = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? @"C:\Program Files\Mono\bin\mono.exe" : "/usr/bin/mono";
            if (!File.Exists(monoPath))
            {
                // https://www.appveyor.com/docs/environment-variables/
                if (Environment.GetEnvironmentVariable("APPVEYOR")?.ToLowerInvariant() == "true")
                {
                    // not on VS2017 VM yet: https://www.appveyor.com/docs/windows-images-software/
                    Console.WriteLine("On APPVEYOR with no Mono installed; skipping mono test");
                    return;
                }

                Assert.Fail($"Could not find mono install at {monoPath}");
            }

            var command = Command.Run(monoPath, SampleCommand, nameof(PlatformCompatibilityTests), methodName);
            command.Result.Success.ShouldEqual(true, "should run on Mono. Got: " + command.Result.StandardError);
#endif
        }
    }
}
