using System.Collections.Generic;
using System.Threading.Tasks;

namespace Medallion.Shell.Tests.Streams;

internal static class EnumerableExtensions
{
    public static async IAsyncEnumerable<T> AsAsyncEnumerable<T>(this IEnumerable<T> items)
    {
        await Task.CompletedTask; // make compiler happy
        foreach (var item in items) { yield return item; }
    }
}