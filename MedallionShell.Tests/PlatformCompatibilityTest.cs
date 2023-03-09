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
        public Task TestReadAfterExit() => RunTestAsync(() => PlatformCompatibilityTests.TestReadAfterExit());

        [Test]
        public Task TestWriteAfterExit() => RunTestAsync(() => PlatformCompatibilityTests.TestWriteAfterExit());

        [Test]
        public Task TestFlushAfterExit() => RunTestAsync(() => PlatformCompatibilityTests.TestFlushAfterExit());

        [Test]
        public Task TestExitWithMinusOne() => RunTestAsync(() => PlatformCompatibilityTests.TestExitWithMinusOne());

        [Test]
        public Task TestExitWithOne() => RunTestAsync(() => PlatformCompatibilityTests.TestExitWithOne());

        [Test]
        public Task TestBadProcessFile() => RunTestAsync(() => PlatformCompatibilityTests.TestBadProcessFile());

        [Test]
        public Task TestAttaching() => RunTestAsync(() => PlatformCompatibilityTests.TestAttaching());

        [Test]
        public Task TestWriteToStandardInput() => RunTestAsync(() => PlatformCompatibilityTests.TestWriteToStandardInput());

        [Test]
        public Task TestArgumentsRoundTrip() => RunTestAsync(() => PlatformCompatibilityTests.TestArgumentsRoundTrip());

        [Test]
        public Task TestKill() => RunTestAsync(() => PlatformCompatibilityTests.TestKill());

        private static async Task RunTestAsync(Expression<Action> testMethod)
        {
            var compiled = testMethod.Compile();
            Assert.DoesNotThrow(() => compiled(), "should run on current platform");

            // don't bother testing running Mono from .NET Core or Mono itself
#if NETFRAMEWORK
            if (!PlatformCompatibilityHelper.IsMono)
            {
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
                await command.Task;
                command.Result.Success.ShouldEqual(true, "should run on Mono. Got: " + command.Result.StandardError);
            }
#endif
        }
    }
}
