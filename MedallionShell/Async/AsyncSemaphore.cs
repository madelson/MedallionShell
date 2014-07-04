using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Medallion.Shell.Async
{
    // based on http://blogs.msdn.com/b/pfxteam/archive/2012/02/12/10266983.aspx
    internal sealed class AsyncSemaphore
    {
        private static readonly Task CachedCompletedTask = Task.FromResult(true);
        private readonly Queue<TaskCompletionSource<bool>> waiters = new Queue<TaskCompletionSource<bool>>();
        private readonly int? maxCount;
        private int count; 

        public AsyncSemaphore(int initialCount, int? maxCount = null)
        {
            Throw.IfOutOfRange(initialCount, "initialCount", min: 0);
            if (maxCount.HasValue)
            {
                Throw.IfOutOfRange(maxCount.Value, "maxCount", min: 0);
                Throw.IfOutOfRange(initialCount, "initialCount", max: maxCount);
                this.maxCount = maxCount;
            }
            this.count = initialCount;
        }

        public Task WaitAsync()
        {
            lock (this.waiters)
            {
                if (this.count > 0)
                {
                    --this.count;
                    return CachedCompletedTask;
                }
                else
                {
                    var waiter = new TaskCompletionSource<bool>();
                    this.waiters.Enqueue(waiter);
                    return waiter.Task;
                }
            }
        }

        public void Release()
        {
            TaskCompletionSource<bool> toRelease = null;
            lock (this.waiters)
            {
                if (this.waiters.Count > 0)
                {
                    toRelease = this.waiters.Dequeue();
                }
                else if (this.maxCount.HasValue && this.count == this.maxCount)
                {
                    throw new InvalidOperationException("Max count value exceeded on the semaphore");
                }
                else
                {
                    ++this.count;
                }
            }
            if (toRelease != null)
            {
                toRelease.SetResult(true);
            }
        }
    }
}
