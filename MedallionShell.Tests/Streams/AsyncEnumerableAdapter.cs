using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Medallion.Shell.Tests.Streams;

public class AsyncEnumerableAdapter : IAsyncEnumerable<string>
{
    private readonly IEnumerable<string> strings;

    public AsyncEnumerableAdapter(IEnumerable<string> strings)
    {
        this.strings = strings;
    }

    public IAsyncEnumerator<string> GetAsyncEnumerator(CancellationToken cancellationToken = default) =>
        new AsyncEnumeratorAdapter(this.strings.GetEnumerator());

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