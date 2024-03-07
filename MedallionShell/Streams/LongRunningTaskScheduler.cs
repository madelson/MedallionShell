using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Medallion.Shell.Streams;

/// <summary>
/// Implements a custom <see cref="TaskScheduler"/> which uses dedicated threads to perform
/// long-running tasks. Unlike with <see cref="Task.Factory"/>, the scheduler will re-use
/// threads if needed close together, which happens frequently when piping between process
/// streams using sync IO.
/// </summary>
internal sealed class LongRunningTaskScheduler : TaskScheduler
{
    /// <summary>
    /// How long a worker thread we've created will wait for a new task before spinning down.
    /// Intended to be long enough to smoothly handle work loops (e.g. when piping) while
    /// short enough to avoid bloating memory for too long if we have to create many threads.
    /// </summary>
    private static readonly TimeSpan IdleWorkerKeepalive = TimeSpan.FromSeconds(5);
    private static readonly LongRunningTaskScheduler Instance = new();

    /// <summary>
    /// Tracks all instances of <see cref="WorkerThreadState"/> that are currently awaiting
    /// a task assignment.
    /// </summary>
    private readonly HashSet<WorkerThreadState> _idleWorkers = new();

#if DEBUG
    private static long _threadsCreated;

    public static long ThreadsCreated => Interlocked.Read(ref _threadsCreated);
#endif

    private LongRunningTaskScheduler() { }

    private object Lock => this._idleWorkers;

    public static Task<TResult> StartNew<TResult>(
        Func<object?, TResult> func,
        object? state,
        CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Shims.CanceledTask<TResult>(cancellationToken);
        }

        return Task.Factory.StartNew(
            func,
            state,
            cancellationToken,
            TaskCreationOptions.LongRunning | TaskCreationOptions.DenyChildAttach,
            Instance);
    }

    protected override void QueueTask(Task task)
    {
        Debug.Assert(
            task.CreationOptions.HasFlag(TaskCreationOptions.LongRunning),
            "Only use this scheduler for long-running tasks");

        lock (this.Lock)
        {
            if (this._idleWorkers.Count != 0)
            {
                foreach (var worker in this._idleWorkers)
                {
                    worker.AssignNoLock(task);
                    return;
                }
            }
        }
             
        WorkerThreadState.StartNew(this, task);
    }

    protected override IEnumerable<Task> GetScheduledTasks() => Enumerable.Empty<Task>(); // all tasks run immediately

    protected override bool TryExecuteTaskInline(Task task, bool taskWasPreviouslyQueued) => false;
    
    private sealed class WorkerThreadState
    {
        private readonly AutoResetEvent _taskAssigned = new(initialState: false);
        private readonly LongRunningTaskScheduler _scheduler;
        private Task? _task;
        
        private WorkerThreadState(LongRunningTaskScheduler scheduler, Task task)
        {
            this._scheduler = scheduler;
            this._task = task;
            this.SetIdleOnTaskCompleted();
        }

        public static void StartNew(LongRunningTaskScheduler scheduler, Task task)
        {
            WorkerThreadState worker = new(scheduler, task);
            Task.Factory.StartNew(
                static state => ((WorkerThreadState)state!).Run(),
                state: worker,
                TaskCreationOptions.LongRunning | TaskCreationOptions.DenyChildAttach
            );
#if DEBUG
            Interlocked.Increment(ref _threadsCreated);
#endif
        }

        private void Run()
        {
#if !NETSTANDARD1_3
            Debug.Assert(!Thread.CurrentThread.IsThreadPoolThread, "Should be a dedicated thread");
#endif

            while (true)
            {
                Task? task;
                lock (this._scheduler.Lock)
                {
                    task = this._task;
                    if (task is null)
                    {
                        this.CleanUpNoLock();
                        return; // end thread
                    }
                }

                try { this._scheduler.TryExecuteTask(task); }
                catch { Debug.Fail($"{nameof(LongRunningTaskScheduler.TryExecuteTask)} should not throw"); }

                // We do not check the return value of WaitOne to avoid race conditions between
                // timeouts and a task being assigned to us.
                // NOTE: SetIdleOnTaskCompleted() will ensure we're idle by the time we get here
                this._taskAssigned.WaitOne(IdleWorkerKeepalive);
            }
        }

        public void AssignNoLock(Task task)
        {
            Debug.Assert(Monitor.IsEntered(this._scheduler.Lock), "NoLock method");
            Debug.Assert(this._task is null && this._scheduler._idleWorkers.Contains(this), "Must be idle");

            this._task = task;
            this._scheduler._idleWorkers.Remove(this);
            this.SetIdleOnTaskCompleted();
            this._taskAssigned.Set();
        }

        private void SetIdleOnTaskCompleted()
        {
            // When piping, we have an async loop which bounces between instances of
            // long-running sync IO tasks. To minimize thread creation, we need to ensure
            // that this worker gets returned to the idle pool BEFORE the await continuations
            // on the task run. To accomplish that, we set a synchronous continuation here.
            // NOTE that it is important that this method be called synchronously from QueueTask();
            // otherwise the caller might already have added their own continuations.
            this._task!.ContinueWith(
                static (_, state) => ((WorkerThreadState)state!).SetIdle(),
                this,
                TaskContinuationOptions.ExecuteSynchronously
            );
        }

        private void SetIdle()
        {
            lock (this._scheduler.Lock)
            {
                Debug.Assert(Monitor.IsEntered(this._scheduler.Lock), "NoLock method");
                Debug.Assert(this._task != null && !this._scheduler._idleWorkers.Contains(this), "Must not be idle");

                this._task = null;
                this._scheduler._idleWorkers.Add(this);
            }
        }

        private void CleanUpNoLock()
        {
            Debug.Assert(Monitor.IsEntered(this._scheduler.Lock), "NoLock method");
            Debug.Assert(this._task is null && this._scheduler._idleWorkers.Contains(this), "Must be idle");

            this._scheduler._idleWorkers.Remove(this);
            this._taskAssigned.Dispose();
        }
    }
}
