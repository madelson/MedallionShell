using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.NetworkInformation;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Medallion.Shell.Streams;
using NUnit.Framework;

namespace Medallion.Shell.Tests;

[NonParallelizable] // performs global ThreadPool configuration
internal class ThreadUsageTest
{
    /// <summary>
    /// Tests the fix to https://github.com/madelson/MedallionShell/issues/94; prior to this change this test 
    /// would fail for small-ish thread pool values (both 2 and 8 on my machine, for example).
    /// </summary>
    [Test]
    public void TestPipeline([Values(2, 8)] int minThreads)
    {
        const int ProcessCount = 10;

        ThreadPool.GetMinThreads(out var originalMinWorkerThreads, out var originalMinCompletionPortThreads);
        ThreadPool.GetMaxThreads(out var originalMaxWorkerThreads, out var originalMaxCompletionPortThreads);
#if DEBUG
        var originalThreadsCreated = LongRunningTaskScheduler.ThreadsCreated;
#endif
        Command? pipeline = null;
        try
        {
            ThreadPool.SetMinThreads(minThreads, minThreads);
            ThreadPool.SetMaxThreads(minThreads, minThreads);

            var task = Task.Factory.StartNew(
                () =>
                {
                    pipeline = Enumerable.Range(0, ProcessCount)
                        .Select(_ => UnitTestHelpers.TestShell.Run(UnitTestHelpers.SampleCommand, "pipebytes"))
                        .Aggregate((first, second) => first | second);
                    for (var i = 0; i < 10; ++i)
                    {
                        var @char = (char)('a' + i);

                        pipeline.StandardInput.AutoFlush.ShouldEqual(true);
                        var writeTask = pipeline.StandardInput.WriteAsync(@char);
                        writeTask.Wait(TimeSpan.FromSeconds(30)).ShouldEqual(true, $"write {i} should complete");

                        var buffer = new char[10];
                        var readTask = pipeline.StandardOutput.ReadAsync(buffer, 0, buffer.Length);
                        readTask.Wait(TimeSpan.FromSeconds(30)).ShouldEqual(true, $"read {i} should complete");
                        readTask.Result.ShouldEqual(1);
                        buffer[0].ShouldEqual(@char);
                    }

                    pipeline.StandardInput.Dispose();
                    pipeline.Task.Wait(TimeSpan.FromSeconds(30)).ShouldEqual(true, "pipeline should exit");
                },
                TaskCreationOptions.LongRunning
            );
            Assert.IsTrue(task.Wait(TimeSpan.FromSeconds(10)));

#if DEBUG
            var threadsCreated = LongRunningTaskScheduler.ThreadsCreated - originalThreadsCreated;
            if (PlatformCompatibilityHelper.ProcessStreamsUseSyncIO)
            {
                if (!Debugger.IsAttached) // can lead to waiting threads timing out and thus more threads created
                {
                    // TODO https://github.com/madelson/MedallionShell/issues/102
                    // 1 std input thread for the first process
                    // 1 std output thread for the last process
                    // N-1 piping threads
                    // N std error threads
                    // Assert.LessOrEqual(threadsCreated, (2 * ProcessCount) + 1);

                    // 3 IO stream threads per process
                    Assert.LessOrEqual(threadsCreated, 3 * ProcessCount);
                }
            }
            else
            {
                threadsCreated.ShouldEqual(0);
            }
#endif
        }
        finally
        {
            ThreadPool.SetMinThreads(originalMinWorkerThreads, originalMinCompletionPortThreads);
            ThreadPool.SetMaxThreads(originalMaxWorkerThreads, originalMaxCompletionPortThreads);
            pipeline?.Kill();
        }
    }
}
