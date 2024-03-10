using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Medallion.Shell.Tests.Streams;

public class AsyncEnumerableAdapter(IEnumerable<string> enumerable) : IAsyncEnumerable<string>
{
    public IAsyncEnumerator<string> GetAsyncEnumerator(CancellationToken cancellationToken = default) =>
        new AsyncEnumeratorAdapter(enumerable.GetEnumerator());

    private class AsyncEnumeratorAdapter(IEnumerator<string> enumerator) : IAsyncEnumerator<string>
    {
        public string Current => enumerator.Current;

        public ValueTask DisposeAsync()
        {
            enumerator.Dispose();
            return new(Task.CompletedTask);
        }

        public ValueTask<bool> MoveNextAsync() => new(enumerator.MoveNext());
    }
}