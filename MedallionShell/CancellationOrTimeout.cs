using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Medallion.Shell
{
    /// <summary>
    /// Provides a <see cref="Task"/> which combines the logic of watching for cancellation or a timeout
    /// </summary>
    internal sealed class CancellationOrTimeout : IDisposable
    {
        private List<IDisposable> resources;

        private CancellationOrTimeout(Task task, List<IDisposable> resources)
        {
            this.Task = task;
            this.resources = resources;
        }

        public static CancellationOrTimeout TryCreate(CancellationToken cancellationToken, TimeSpan timeout)
        {
            var hasCancellation = cancellationToken.CanBeCanceled;
            var hasTimeout = timeout != Timeout.InfiniteTimeSpan;

            if (!hasCancellation && !hasTimeout)
            {
                // originally, I designed this to return a static task which never completes. However, this can cause
                // memory leaks from the continuations that build up on the task
                return null;
            }

            var resources = new List<IDisposable>();
            var taskBuilder = new TaskCompletionSource<bool>();

            if (hasCancellation)
            {
                resources.Add(cancellationToken.Register(
                    state => ((TaskCompletionSource<bool>)state).TrySetCanceled(),
                    state: taskBuilder
                ));
            }

            if (hasTimeout)
            {
                var timeoutSource = new CancellationTokenSource(timeout);
                resources.Add(timeoutSource);

                resources.Add(timeoutSource.Token.Register(
                    state =>
                    {
                        var tupleState = (Tuple<TaskCompletionSource<bool>, TimeSpan>)state;
                        tupleState.Item1.TrySetException(new TimeoutException("Process killed after exceeding timeout of " + tupleState.Item2));
                    },
                    state: Tuple.Create(taskBuilder, timeout)
                ));
            }

            return new CancellationOrTimeout(taskBuilder.Task, resources);
        }

        public Task Task { get; }

        public void Dispose()
        {
            this.resources?.ForEach(d => d.Dispose());
            this.resources = null;
        }
    }
}
