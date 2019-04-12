using System;
using System.Threading;
using NUnit.Framework;

namespace Medallion.Shell.Tests
{
    using static UnitTestHelpers;

    public class AttachingTests
    {
        [Test]
        public void TestAttachingToExistingProcess()
        {
            var processCommand = Command.Run(SampleCommand, new[] { "sleep", "10000" });
            var processId = processCommand.ProcessId;
            Command.TryAttachToProcess(processId, out _)
                .ShouldEqual(true, "Attaching to process failed.");
            processCommand.Kill();
        }

        [Test]
        public void TestWaitingForAttachedProcessExit()
        {
            var processCommand = Command.Run(SampleCommand, new[] { "sleep", "100" });
            Command.TryAttachToProcess(processCommand.ProcessId, out var attachedCommand)
                .ShouldEqual(true, "Attaching to process failed.");
            var commandResult = attachedCommand.Task;
            commandResult.IsCompleted.ShouldEqual(false, "Task has finished too early.");
            Thread.Sleep(300);
            commandResult.IsCompleted.ShouldEqual(true, "Task has not finished on time.");
        }

        [Test]
        public void TestGettingExitCodeFromAttachedProcess()
        {
            var processCommand = Command.Run(SampleCommand, new[] { "exit", "16" });
            Command.TryAttachToProcess(processCommand.ProcessId, out var attachedCommand)
                .ShouldEqual(true, "Attaching to process failed.");
            var task = attachedCommand.Task;
            task.Wait(1000).ShouldEqual(true, "Task has not finished on time.");
            task.Result.ExitCode.ShouldEqual(16, "Exit code was not correct.");
        }

        [Test]
        public void TestAttachingToNonExistingProcess()
        {
            var processCommand = Command.Run(SampleCommand, new[] { "exit", "0" });
            var processId = processCommand.ProcessId;
            processCommand.Task.Wait(1000).ShouldEqual(true, "Process has not exited, test is inconclusive.");
            Command.TryAttachToProcess(processId, out _)
                .ShouldEqual(false, "Attaching succeeded although process has already exited.");
        }

        [Test]
        public void TestKillingAttachedProcess()
        {
            var processCommand = Command.Run(SampleCommand, new[] { "sleep", "10000" });
            var processId = processCommand.ProcessId;
            Command.TryAttachToProcess(
                    processId,
                    options => options.DisposeOnExit(false),
                    out var attachedCommand)
                .ShouldEqual(true, "Attaching to process failed.");
            
            attachedCommand.Kill();
            attachedCommand.Process.HasExited
                .ShouldEqual(true, "The process is still alive after Kill() has finished.");
        }

        [Test]
        public void TestAttachingWithAlreadyCancelledToken()
        {
            var cancellationTokenSource = new CancellationTokenSource();
            cancellationTokenSource.Cancel();
            var processCommand = Command.Run(SampleCommand, new[] { "sleep", "10000" });
            var processId = processCommand.ProcessId;
            Command.TryAttachToProcess(
                processId, 
                options => options.CancellationToken(cancellationTokenSource.Token).DisposeOnExit(false),
                out var attachedCommand);
            attachedCommand.Process.HasExited.ShouldEqual(true, "The process wasn't killed.");
        }

        [Test]
        public void TestTimeout()
        {
            const string expectedTimeoutexceptionDidNotOccur = "Expected TimeoutException did not occur.";

            var processCommand = Command.Run(SampleCommand, new[] { "sleep", "10000" });
            Thread.Sleep(200);
            var processId = processCommand.ProcessId;
            Command.TryAttachToProcess(
                processId,
                options => options.Timeout(TimeSpan.FromMilliseconds(150)),
                out var attachedCommand);

            // the timeout is counted from the moment we attached to the process so it shouldn't throw at this moment
            attachedCommand.Task.Wait(100); 

            // but should eventually throw
            var exception = Assert.Throws<AggregateException>(
                () => attachedCommand.Task.Wait(150),
                expectedTimeoutexceptionDidNotOccur);
            
            (exception.InnerException.InnerException is TimeoutException).ShouldEqual(true, expectedTimeoutexceptionDidNotOccur);
        }
    }
}
