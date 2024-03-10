using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Medallion.Shell.Streams
{
    internal sealed class MergedLinesEnumerable(TextReader standardOutput, TextReader standardError) :
#if NETCOREAPP3_0_OR_GREATER || NETSTANDARD2_1_OR_GREATER
        IEnumerable<string>, IAsyncEnumerable<string>
#else
        IEnumerable<string>
#endif
    {
        private int consumed;

        public IEnumerator<string> GetEnumerator()
        {
            this.AssertNoMultipleEnumeration();

            return this.GetEnumeratorInternal();
        }

        private void AssertNoMultipleEnumeration() =>
            Throw<InvalidOperationException>.If(
                Interlocked.Exchange(ref this.consumed, 1) != 0,
                "The enumerable returned by GetOutputAndErrorLines() can only be enumerated once"
            );

        IEnumerator IEnumerable.GetEnumerator() => this.GetEnumerator();

        private IEnumerator<string> GetEnumeratorInternal()
        {
            List<ReaderAndTask> tasks = [new(standardOutput), new(standardError)];

            do
            {
                var nextLine = this.GetNextLineOrDefaultAsync(tasks).GetAwaiter().GetResult();
                if (nextLine is null) { yield break; }
                yield return nextLine;
            }
            while (tasks.Count != 1);

            var remaining = tasks[0].Reader;
            while (remaining.ReadLine() is { } line)
            {
                yield return line;
            }
        }

        private async Task<string?> GetNextLineOrDefaultAsync(List<ReaderAndTask> tasks, CancellationToken cancellationToken = default)
        {
            Debug.Assert(tasks.Count is 0 or 2, "There should be EITHER nothing OR both stdout and stderr.");

            if (tasks.Count == 0)
            {
                tasks = [new(standardOutput, cancellationToken), new(standardError, cancellationToken)];
            }

            ReaderAndTask next;
            if (tasks[0].Task.IsCompleted)
            {
                next = tasks[0];
            }
            else if (tasks[1].Task.IsCompleted)
            {
                next = tasks[1];
            }
            else
            {
                var nextCompleted = await Task.WhenAny(tasks.Select(t => t.Task)).ConfigureAwait(false);
                next = tasks[0].Task == nextCompleted ? tasks[0] : tasks[1];
            }

            var nextLine = await next.Task.ConfigureAwait(false);
            tasks.Remove(next);

            if (nextLine is { })
            {
                tasks.Add(new ReaderAndTask(next.Reader, cancellationToken));
                return nextLine;
            }
            else if (await tasks[0].Task.ConfigureAwait(false) is { } otherAsyncLine)
            {
                return otherAsyncLine;
            }
            return null;
        }

#if NETCOREAPP3_0_OR_GREATER || NETSTANDARD2_1_OR_GREATER
        public IAsyncEnumerator<string> GetAsyncEnumerator(CancellationToken cancellationToken = default)
        {
            this.AssertNoMultipleEnumeration();

            return this.GetAsyncEnumeratorInternal(cancellationToken);
        }

        private async IAsyncEnumerator<string> GetAsyncEnumeratorInternal(CancellationToken cancellationToken)
        {
            List<ReaderAndTask> tasks = [new(standardOutput, cancellationToken), new(standardError, cancellationToken)];

            do
            {
                var nextLine = await this.GetNextLineOrDefaultAsync(tasks, cancellationToken).ConfigureAwait(false);
                if (nextLine is null) { yield break; }
                yield return nextLine;
            }
            while (tasks.Count != 1);

            var remaining = tasks[0].Reader;
#if NET7_0_OR_GREATER
            while (await remaining.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
#else
            while (remaining.ReadLine() is { } line)
#endif
            {
                yield return line;
            }
        }
#endif

        private readonly struct ReaderAndTask : IEquatable<ReaderAndTask>
        {
            public ReaderAndTask(TextReader reader, CancellationToken cancellationToken = default)
            {
                this.Reader = reader;
#if NET7_0_OR_GREATER
                this.Task = reader.ReadLineAsync(cancellationToken);
#else
                this.Task = reader.ReadLineAsync();
#endif
            }

            public TextReader Reader { get; }
            public Task<string?> Task { get; }

            public bool Equals(ReaderAndTask that) => this.Reader == that.Reader && this.Task == that.Task;

            public override bool Equals(object? obj) => obj is ReaderAndTask that && this.Equals(that);

            public override int GetHashCode() => this.Reader.GetHashCode() ^ this.Task.GetHashCode();
        }
    }
}
