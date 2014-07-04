using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Medallion.Shell.Async
{
    // http://blogs.msdn.com/b/pfxteam/archive/2012/02/12/10266988.aspx
    internal sealed class AsyncLock
    {
        private readonly AsyncSemaphore semaphore = new AsyncSemaphore(initialCount: 1, maxCount: 1);
        private readonly Task<Releaser> cachedReleaserTask;

        public AsyncLock() 
        {
            this.cachedReleaserTask = Task.FromResult(new Releaser(this));
        }

        public Task<Releaser> AcquireAsync()
        {
            var wait = this.semaphore.WaitAsync();
            return wait.IsCompleted 
                ? this.cachedReleaserTask 
                : wait.ContinueWith(
                    CreateReleaserFunc,
                    this, 
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously, 
                    TaskScheduler.Default
                ); 
        }

        private static Releaser CreateReleaser(Task ignored, object asyncLock)
        {
            return new Releaser((AsyncLock)asyncLock);
        }
        private static Func<Task, object, Releaser> CreateReleaserFunc = CreateReleaser;

        public struct Releaser : IDisposable
        {
            private readonly AsyncLock @lock;

            internal Releaser(AsyncLock @lock) 
            {
                this.@lock = @lock;
            }

            public void Dispose()
            {
                this.@lock.semaphore.Release();
            }
        }
    }
}
