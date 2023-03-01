using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SystemTask = System.Threading.Tasks.Task;

namespace Medallion.Shell.Streams;

/// <summary>
/// Simple async-friendly version of <see cref="ManualResetEvent"/>
/// </summary>
internal sealed class AsyncManualResetEvent
{
    private static readonly Task<bool> TrueTask = SystemTask.FromResult(true);

    /// <summary>
    /// When set, this is null. When unset, this is an incomplete <see cref="TaskCompletionSource{TResult}"/>.
    /// </summary>
    private TaskCompletionSource<bool>? _taskCompletionSource;

    public AsyncManualResetEvent(bool initialState)
    {
        if (!initialState) { this.Reset(); }
    }

    private Task<bool> Task => Volatile.Read(ref this._taskCompletionSource)?.Task ?? TrueTask;

    public bool IsSet => this.Task.IsCompleted;

    public void Set() => Interlocked.Exchange(ref this._taskCompletionSource, null)?.SetResult(true);

    public void Reset() => Interlocked.CompareExchange(ref this._taskCompletionSource, new(), comparand: null);

    public bool Wait(TimeSpan timeout, CancellationToken cancellationToken) => 
        this.Task.Wait((int)timeout.TotalMilliseconds, cancellationToken);

    public Task<bool> WaitAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        var task = this.Task;
        return task.IsCompleted || (timeout == Timeout.InfiniteTimeSpan && !cancellationToken.CanBeCanceled)
            ? task
            : WaitAsyncHelper();

        async Task<bool> WaitAsyncHelper()
        {
            using var linkedTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var completed = await SystemTask.WhenAny(task, SystemTask.Delay(timeout, linkedTokenSource.Token)).ConfigureAwait(false);
            if (completed == task)
            {
                Debug.Assert(task.Status == TaskStatus.RanToCompletion && task.Result, "Task should always finish with true");
                linkedTokenSource.Cancel(); // clean up unfinished Delay task
                return true;
            }

            cancellationToken.ThrowIfCancellationRequested();
            return false;
        }
    }
}
