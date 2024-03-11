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
        // this does not allow consuming the same IEnumerable twice
        new AsyncEnumeratorAdapter(this.strings.GetEnumerator());

    private class AsyncEnumeratorAdapter : IAsyncEnumerator<string>
    {
        private readonly IEnumerator<string> enumerator;

        public AsyncEnumeratorAdapter(IEnumerator<string> enumerator)
        {
            this.enumerator = enumerator;
        }

        public string Current => this.enumerator.Current;

        public ValueTask DisposeAsync()
        {
            this.enumerator.Dispose();
            return new(Task.CompletedTask);
        }

        public ValueTask<bool> MoveNextAsync() => new(this.enumerator.MoveNext());
    }
}
