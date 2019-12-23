using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Medallion.Shell
{
    internal static class ProcessHelper
    {
        public const string ProcessNotAccessibleWithDisposeOnExitEnabled =
            "Process can only be accessed when the command is not set to dispose on exit. This is to prevent non-deterministic code which may access the process before or after it exits";

        private static object? _boxedCurrentProcessId;

        public static int CurrentProcessId => (int)LazyInitializer.EnsureInitialized(
            ref _boxedCurrentProcessId,
            () => { using (var process = Process.GetCurrentProcess()) { return (object)process.Id; } }
        )!;

        public static void TryKillProcess(Process process)
        {
            try
            {
                // the try-catch is because Kill() will throw if the process is disposed
                process.Kill();
            }
            catch (Exception ex)
            {
                Log.WriteLine("Exception killing process: " + ex);
            }
        }

        /// <summary>
        /// Creates a task which will either return the exit code for the <paramref name="process"/>, throw
        /// <see cref="ErrorExitCodeException"/>, throw <see cref="TimeoutException"/> or be canceled. When
        /// the returned <see cref="Task"/> completes, the <paramref name="process"/> is guaranteed to have
        /// exited
        /// </summary>
        public static Task<int> CreateProcessTask(
            Process process,
            Task processMonitoringTask,
            bool throwOnError,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            // the implementation here is somewhat tricky. We want to guarantee that the process has exited when the resulting
            // task returns. That means that we can't trigger task completion on timeout or cancellation. At the same time, we
            // want the task result to match the exit condition. The approach is to use a TCS for the returned task that gets completed
            // in a continuation on the monitoring task. We can't use the continuation itself as the result because it won't be canceled
            // even from throwing an OCE. To determine the result of the TCS, we set off a "race" between timeout, cancellation, and
            // processExit to set a resultObject, which is done thread-safely using Interlocked. When timeout or cancellation win, they
            // also kill the process to propagate the continuation execution

            var taskBuilder = new TaskCompletionSource<int>();
            var disposables = new List<IDisposable>();
            object? resultObject = null;

            if (cancellationToken.CanBeCanceled)
            {
                disposables.Add(cancellationToken.Register(() =>
                {
                    if (Interlocked.CompareExchange(ref resultObject, CanceledSentinel, null) == null)
                    {
                        TryKillProcess(process); // if cancellation wins the race, kill the process
                    }
                }));
            }

            if (timeout != Timeout.InfiniteTimeSpan)
            {
                var timeoutSource = new CancellationTokenSource(timeout);
                disposables.Add(timeoutSource.Token.Register(() =>
                {
                    var timeoutException = new TimeoutException("Process killed after exceeding timeout of " + timeout);
                    if (Interlocked.CompareExchange(ref resultObject, timeoutException, null) == null)
                    {
                        TryKillProcess(process); // if timeout wins the race, kill the process
                    }
                }));
                disposables.Add(timeoutSource);
            }

            processMonitoringTask.ContinueWith(
                _ =>
                {
                    var resultObjectValue = Interlocked.CompareExchange(ref resultObject, CompletedSentinel, null);
                    if (resultObjectValue == null) // process completed naturally
                    {
                        // try-catch because in theory any process property access could fail if someone
                        // disposes out from under us
                        try
                        {
                            var exitCode = process.SafeGetExitCode();
                            if (throwOnError && exitCode != 0)
                            {
                                taskBuilder.SetException(new ErrorExitCodeException(process));
                            }
                            else
                            {
                                taskBuilder.SetResult(exitCode);
                            }
                        }
                        catch (Exception ex)
                        {
                            taskBuilder.SetException(ex);
                        }
                    }
                    else if (resultObjectValue == CanceledSentinel)
                    {
                        taskBuilder.SetCanceled();
                    }
                    else
                    {
                        taskBuilder.SetException((Exception)resultObjectValue);
                    }

                    // perform cleanup
                    disposables.ForEach(d => d.Dispose());
                },
                TaskContinuationOptions.ExecuteSynchronously
            );

            return taskBuilder.Task;
        }

        private static readonly object CompletedSentinel = new object(),
            CanceledSentinel = new object();
    }
}
