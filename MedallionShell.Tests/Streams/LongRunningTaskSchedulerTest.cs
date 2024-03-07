using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Medallion.Shell.Streams;
using NUnit.Framework;

namespace Medallion.Shell.Tests.Streams;

[NonParallelizable] // shared LongRunningTaskSchedulerInstance
public class LongRunningTaskSchedulerTest
{
    [Test]
    public async Task TestDoesNotUseThreadPoolThreads()
    {
        var thread = await LongRunningTaskScheduler.StartNew(_ => Thread.CurrentThread, null, default);
        Assert.IsFalse(thread.IsThreadPoolThread);
    }

    [Test]
    public async Task TestAwaitsDoNotUseDedicatedThreads()
    {
        var thread1 = Thread.CurrentThread;
        var thread2 = await LongRunningTaskScheduler.StartNew(_ => Thread.CurrentThread, null, default);
        Assert.AreSame(TaskScheduler.Default, TaskScheduler.Current);
        var thread3 = Thread.CurrentThread;
        var thread4 = await LongRunningTaskScheduler.StartNew(_ => Thread.CurrentThread, null, default).ConfigureAwait(false);
        Assert.AreSame(TaskScheduler.Default, TaskScheduler.Current);
        var thread5 = Thread.CurrentThread;
        Assert.AreNotSame(thread1, thread2);
        Assert.AreNotSame(thread2, thread3);
        Assert.AreNotSame(thread3, thread4);
        Assert.AreNotSame(thread4, thread5);
        // note: threads 2 and 4 MIGHT be the same, but not if the pool of threads is large
    }

    [Test]
    public void TestCancellation()
    {
        using CancellationTokenSource cancellationTokenSource = new();
        cancellationTokenSource.Cancel();
        LongRunningTaskScheduler.StartNew(_ => 1, null, cancellationTokenSource.Token).Status.ShouldEqual(TaskStatus.Canceled);
    }

    [Test]
    public async Task FuzzTest()
    {
        const int Tasks = 10;

#if DEBUG
        var originalThreadsCreated = LongRunningTaskScheduler.ThreadsCreated;
#endif

        CancellationTokenSource cancellationTokenSource = new();
        try
        {
            BlockingCollection<int> collection = new();
            var tasks = Enumerable.Range(0, Tasks)
                .Select(_ => Task.Run(async () =>
                {
                    var results = new List<int>();
                    while (true)
                    {
                        var result = await LongRunningTaskScheduler.StartNew(
                            _ => collection.Take(cancellationTokenSource.Token),
                            null,
                            cancellationTokenSource.Token
                        );
                        if (result == -1) { break; }
                        results.Add(result);
                    }
                    return results;
                }))
                .ToArray();

            var next = 0;
            for (var i = 0; i < 10; ++i)
            {
                for (var j = 0; j < 100; ++j)
                {
                    collection.Add(next++);
                }
                await Task.Delay(1);
            }
            for (var i = 0; i < Tasks; ++i) { collection.Add(-1); }
            
            Assert.IsTrue(Task.WaitAll(tasks, TimeSpan.FromSeconds(30)));
            CollectionAssert.AreEquivalent(Enumerable.Range(0, next), tasks.SelectMany(t => t.Result));
        }
        finally { cancellationTokenSource.Cancel(); }

#if DEBUG
        var threadsCreated = LongRunningTaskScheduler.ThreadsCreated - originalThreadsCreated;
        if (!Debugger.IsAttached) // timing sensitive
        {
            Assert.LessOrEqual(threadsCreated, Tasks);
        }
#endif
    }
}
