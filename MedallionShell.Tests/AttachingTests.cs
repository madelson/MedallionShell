using System;
using System.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Medallion.Shell.Tests
{
    [TestClass]
    public class AttachingTests
    {
        [TestMethod]
        public void TestAttachingToExistingProcess()
        {
            var processCommand = Command.Run("SampleCommand", new[] { "sleep", "10000" });
            var processId = processCommand.ProcessId;
            Command.TryAttachToProcess(processId, out _)
                .ShouldEqual(true, "Attaching to process failed.");
            processCommand.Kill();
        }

        [TestMethod]
        public void TestWaitingForAttachedProcessExit()
        {
            var processCommand = Command.Run("SampleCommand", new[] { "sleep", "100" });
            Command.TryAttachToProcess(processCommand.ProcessId, out var attachedCommand)
                .ShouldEqual(true, "Attaching to process failed.");
            var commandResult = attachedCommand.Task;
            commandResult.IsCompleted.ShouldEqual(false, "Task has finished too early.");
            Thread.Sleep(300);
            commandResult.IsCompleted.ShouldEqual(true, "Task has not finished on time.");
        }

        [TestMethod]
        public void TestGettingExitCodeFromAttachedProcess()
        {
            var processCommand = Command.Run("SampleCommand", new[] { "exit", "16" });
            Command.TryAttachToProcess(processCommand.ProcessId, out var attachedCommand)
                .ShouldEqual(true, "Attaching to process failed.");
            var task = attachedCommand.Task;
            task.Wait(1000).ShouldEqual(true, "Task has not finished on time.");
            task.Result.ExitCode.ShouldEqual(16, "Exit code was not correct.");
        }

        [TestMethod]
        public void TestAttachingToNonExistingProcess()
        {
            var processCommand = Command.Run("SampleCommand", new[] { "exit", "0" });
            var processId = processCommand.ProcessId;
            processCommand.Task.Wait(1000).ShouldEqual(true, "Process has not exited, test is inconclusive.");
            Command.TryAttachToProcess(processId, out _)
                .ShouldEqual(false, "Attaching succeeded although process has already exited.");
        }

        [TestMethod]
        public void TestKillingAttachedProcess()
        {
            var processCommand = Command.Run("SampleCommand", new[] { "sleep", "10000" });
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

        [TestMethod]
        public void TestAttachingWithAlreadyCancelledToken()
        {
            var cancellationTokenSource = new CancellationTokenSource();
            cancellationTokenSource.Cancel();
            var processCommand = Command.Run("SampleCommand", new[] { "sleep", "10000" });
            var processId = processCommand.ProcessId;
            Command.TryAttachToProcess(
                processId, 
                options => options.CancellationToken(cancellationTokenSource.Token).DisposeOnExit(false),
                out var attachedCommand);
            attachedCommand.Process.HasExited.ShouldEqual(true, "The process wasn't killed.");
        }

        [TestMethod]
        public void TestTimeout()
        {
            const string expectedTimeoutexceptionDidNotOccur = "Expected TimeoutException did not occur.";

            var processCommand = Command.Run("SampleCommand", new[] { "sleep", "10000" });
            Thread.Sleep(200);
            var processId = processCommand.ProcessId;
            Command.TryAttachToProcess(
                processId,
                options => options.Timeout(TimeSpan.FromMilliseconds(150)),
                out var attachedCommand);

            // the timeout is counted from the moment we attached to the process so it shouldn't throw at this moment
            attachedCommand.Task.Wait(100); 

            // but should eventually throw
            var exception = UnitTestHelpers.AssertThrows<AggregateException>(
                () => attachedCommand.Task.Wait(150),
                expectedTimeoutexceptionDidNotOccur);
            
            (exception.InnerException.InnerException is TimeoutException).ShouldEqual(true, expectedTimeoutexceptionDidNotOccur);
        }
    }
}
