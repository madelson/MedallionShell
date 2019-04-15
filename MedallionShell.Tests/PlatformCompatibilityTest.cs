using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using SampleCommand;

namespace Medallion.Shell.Tests
{
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

        [TestMethod]
        public void TestWriteToStandardInput() => RunTest(() => PlatformCompatibilityTests.TestWriteToStandardInput());

        private static void RunTest(Expression<Action> testMethod)
        {
            var compiled = testMethod.Compile();
            Assert.DoesNotThrow(() => compiled(), "should run on .NET");

            var methodName = ((MethodCallExpression)testMethod.Body).Method.Name;
            const string MonoPath = @"C:\Program Files\Mono\bin\mono.exe";
            var command = Command.Run(MonoPath, UnitTestHelpers.SampleCommand, nameof(PlatformCompatibilityTests), methodName);
            command.Result.Success.ShouldEqual(true, "should run on Mono. Got: " + command.Result.StandardError);
        }
    }
}
