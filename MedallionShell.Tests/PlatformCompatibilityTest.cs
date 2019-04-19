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
            Assert.DoesNotThrow(() => compiled(), "should run on .NET");

            var methodName = ((MethodCallExpression)testMethod.Body).Method.Name;
            var monoPath = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? @"C:\Program Files\Mono\bin\mono.exe" : "/usr/bin/mono";
            var command = Command.Run(monoPath, SampleCommand, nameof(PlatformCompatibilityTests), methodName);
            command.Result.Success.ShouldEqual(true, "should run on Mono. Got: " + command.Result.StandardError);
        }
    }
}
