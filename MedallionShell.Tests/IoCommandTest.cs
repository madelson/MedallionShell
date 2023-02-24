using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Medallion.Shell.Tests;
using NUnit.Framework;

namespace MedallionShell.Tests
{
    using static Medallion.Shell.Tests.UnitTestHelpers;

    public class IOCommandTest
    {
        [Test]
        public void TestStandardOutCannotBeAccessedAfterRedirectingIt()
        {
            var output = new List<string>();
            var command = TestShell.Run(SampleCommand, "argecho", "a");
            var ioCommand = command.RedirectTo(output);

            var errorMessage = Assert.Throws<InvalidOperationException>(() => ioCommand.StandardOutput.GetHashCode())!.Message;
            errorMessage.ShouldEqual("StandardOutput is unavailable because it is already being piped to System.Collections.Generic.List`1[System.String]");

            Assert.DoesNotThrow(() => command.StandardOutput.GetHashCode());

            Assert.Throws<InvalidOperationException>(() => ioCommand.Result.StandardOutput.GetHashCode())!
                .Message
                .ShouldEqual(errorMessage);
            Assert.Throws<ObjectDisposedException>(() => command.Result.StandardOutput.GetHashCode());

            CollectionAssert.AreEqual(new[] { "a" }, output);
            ioCommand.Result.StandardError.ShouldEqual(command.Result.StandardError).ShouldEqual(string.Empty);
        }

        [Test]
        public void TestStandardErrorCannotBeAccessedAfterRedirectingIt()
        {
            var output = new List<string>();
            var command = TestShell.Run(SampleCommand, "argecho", "a");
            var ioCommand = command.RedirectStandardErrorTo(output);

            var errorMessage = Assert.Throws<InvalidOperationException>(() => ioCommand.StandardError.GetHashCode())!.Message;
            errorMessage.ShouldEqual("StandardError is unavailable because it is already being piped to System.Collections.Generic.List`1[System.String]");

            Assert.DoesNotThrow(() => command.StandardError.GetHashCode());

            Assert.Throws<InvalidOperationException>(() => ioCommand.Result.StandardError.GetHashCode())!
                .Message
                .ShouldEqual(errorMessage);
            Assert.Throws<ObjectDisposedException>(() => command.Result.StandardError.GetHashCode());

            Assert.IsEmpty(output);
            ioCommand.Result.StandardOutput.ShouldEqual(command.Result.StandardOutput).ShouldEqual($"a{Environment.NewLine}");
        }

        [Test]
        public void TestStandardInputCannotBeAccessedAfterRedirectingIt()
        {
            var command = TestShell.Run(SampleCommand, "echo");
            var ioCommand = command.RedirectFrom(new[] { "a" });

            var errorMessage = Assert.Throws<InvalidOperationException>(() => ioCommand.StandardInput.GetHashCode())!.Message;
            errorMessage.ShouldEqual("StandardInput is unavailable because it is already being piped from System.String[]");

            Assert.DoesNotThrow(() => command.StandardInput.GetHashCode());

            ioCommand.Result.StandardOutput.ShouldEqual(command.Result.StandardOutput).ShouldEqual($"a{Environment.NewLine}");
            ioCommand.Result.StandardError.ShouldEqual(command.Result.StandardError).ShouldEqual(string.Empty);
        }
    }
}
