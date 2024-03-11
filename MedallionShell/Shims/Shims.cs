using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Medallion.Shell;

internal static class Shims
{
    public static T[] EmptyArray<T>() =>
#if NET45 || (NETFRAMEWORK && DEBUG) // for test coverage
        Empty<T>.Array;

    private static class Empty<T>
    {
        public static readonly T[] Array = [];
    }
#else
        [];
#endif

    public static Task<T> CanceledTask<T>(CancellationToken cancellationToken)
    {
#if NET45 || (NETFRAMEWORK && DEBUG) // for test coverage
        TaskCompletionSource<T> taskCompletionSource = new();
        taskCompletionSource.SetCanceled();
        return taskCompletionSource.Task;
#else
        return Task.FromCanceled<T>(cancellationToken);
#endif
    }
}