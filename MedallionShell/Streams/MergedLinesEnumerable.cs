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
    internal sealed class MergedLinesEnumerable :
#if NETCOREAPP3_0_OR_GREATER || NETSTANDARD2_1_OR_GREATER
        IEnumerable<string>, IAsyncEnumerable<string>
#else
        IEnumerable<string>
#endif
    {
        private readonly TextReader standardOutput, standardError;
        private int consumed;

        public MergedLinesEnumerable(TextReader standardOutput, TextReader standardError)
        {
            this.standardOutput = standardOutput;
            this.standardError = standardError;
        }

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
            List<ReaderAndTask> tasks = [new(this.standardOutput), new(this.standardError)];

            // phase 1: read both streams simultaneously, alternating between which is given priority.
            // Stop when both streams are exhausted
            do
            {
                var nextLine = this.GetNextLineOrDefaultAsync(tasks).GetAwaiter().GetResult();
                if (nextLine is null) { yield break; } // both readers done
                yield return nextLine;
            }
            while (tasks.Count != 1);

            // phase 2: finish reading the remaining stream
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
                tasks = [new(this.standardOutput, cancellationToken), new(this.standardError, cancellationToken)];
            }

            // Figure out which of the 2 tasks is completed. Remove that task and, if the result is not null, replace it
            // by queueing up the next read. 
            // If the result is not null, return the result. If the result is null instead await the other task and return its result.

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
            else
            {
                return null;
            }
        }

#if NETCOREAPP3_0_OR_GREATER || NETSTANDARD2_1_OR_GREATER
        public IAsyncEnumerator<string> GetAsyncEnumerator(CancellationToken cancellationToken = default)
        {
            this.AssertNoMultipleEnumeration();

            return this.GetAsyncEnumeratorInternal(cancellationToken);
        }

        private async IAsyncEnumerator<string> GetAsyncEnumeratorInternal(CancellationToken cancellationToken)
        {
            var tasks = new List<ReaderAndTask>(capacity: 2) { new(this.standardOutput, cancellationToken), new(this.standardError, cancellationToken) };
            do
            {
                var nextLine = await this.GetNextLineOrDefaultAsync(tasks, cancellationToken).ConfigureAwait(false);
                if (nextLine is null) { yield break; } // both readers done
                yield return nextLine;
            }
            while (tasks.Count != 1); // both readers not done

            // phase 2: finish reading the remaining stream
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

        private struct ReaderAndTask : IEquatable<ReaderAndTask>
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
