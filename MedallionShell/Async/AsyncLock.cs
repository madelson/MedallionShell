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
        private readonly Queue<TaskCompletionSource<Releaser>> waiters = new Queue<TaskCompletionSource<Releaser>>(capacity: 2);

        public Task<Releaser> AcquireAsync()
        {
            return this.InternalAcquireAsync(blocking: true);
        }

        public Releaser Acquire()
        {
            return this.AcquireAsync().Result;
        }

        public TryReleaser TryAcquire()
        {
            var task = this.InternalAcquireAsync(blocking: false);
            return new TryReleaser(task != null ? this : null);
        }

        private Task<Releaser> InternalAcquireAsync(bool blocking)
        {
            lock (this.waiters)
            {
                if (this.waiters.Count == 0)
                {
                    var cachedSource = this.GetCachedReleaserSource();
                    this.waiters.Enqueue(cachedSource);
                    return cachedSource.Task;
                }
                else if (blocking)
                {
                    var source = new TaskCompletionSource<Releaser>();
                    waiters.Enqueue(source);
                    return source.Task;
                }
                else
                {
                    return null;
                }
            }
        }

        private TaskCompletionSource<Releaser> cachedReleaserSource;

        private TaskCompletionSource<Releaser> GetCachedReleaserSource()
        {
            if (this.cachedReleaserSource == null)
            {
                var source = new TaskCompletionSource<Releaser>();
                source.SetResult(new Releaser(this));
                this.cachedReleaserSource = source;
            }

            return this.cachedReleaserSource;
        }

        private void Release()
        {
            lock (this.waiters)
            {
                // dequeue the current waiter
                this.waiters.Dequeue();

                if (this.waiters.Count > 0)
                {
                    this.waiters.Peek().SetResult(new Releaser(this));
                }
            }
        }

        public struct Releaser : IDisposable
        {
            private readonly AsyncLock @lock;

            internal Releaser(AsyncLock @lock) 
            {
                this.@lock = @lock;
            }

            public void Dispose()
            {
                this.@lock.Release();
            }
        }

        public struct TryReleaser : IDisposable
        {
            private readonly AsyncLock @lock;

            internal TryReleaser(AsyncLock @lock)
            {
                this.@lock = @lock;
            }

            public bool HasLock { get { return this.@lock != null; } }

            public void Dispose()
            {
                if (this.@lock != null)
                {
                    this.@lock.Release();
                }
            }
        }
    }
}
