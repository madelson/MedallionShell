using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Medallion.Shell.Streams
{
    internal sealed class MergedLinesEnumerable : IEnumerable<string>
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
            Throw<InvalidOperationException>.If(
                Interlocked.Exchange(ref this.consumed, 1) != 0,
                "The enumerable returned by GetOutputAndErrorLines() can only be enumerated once"
            );

            return this.GetEnumeratorInternal();
        }

        IEnumerator IEnumerable.GetEnumerator() => this.GetEnumerator();

        private IEnumerator<string> GetEnumeratorInternal()
        {
            var tasks = new List<ReaderAndTask>(capacity: 2);
            tasks.Add(new ReaderAndTask(this.standardOutput));
            tasks.Add(new ReaderAndTask(this.standardError));

            // phase 1: read both streams simultaneously, alternating between which is given priority.
            // Stop when one (or both) streams is exhausted

            TextReader remaining;
            while (true)
            {
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
                    var nextCompleted = Task.WhenAny(tasks.Select(t => t.Task)).GetAwaiter().GetResult();
                    next = tasks[0].Task == nextCompleted ? tasks[0] : tasks[1];
                }

                var nextLine = next.Task.GetAwaiter().GetResult();
                tasks.Remove(next);

                if (nextLine != null)
                {
                    yield return nextLine;
                    tasks.Add(new ReaderAndTask(next.Reader));
                }
                else
                {
                    var otherAsyncLine = tasks[0].Task.GetAwaiter().GetResult();
                    if (otherAsyncLine != null)
                    {
                        yield return otherAsyncLine;
                        remaining = tasks[0].Reader;
                        break;
                    }
                    else
                    {
                        yield break;
                    }
                }
            }

            // phase 2: finish reading the remaining stream

            string line;
            while ((line = remaining.ReadLine()) != null)
            {
                yield return line;
            }
        }

        private struct ReaderAndTask : IEquatable<ReaderAndTask>
        {
            public ReaderAndTask(TextReader reader)
            {
                this.Reader = reader;
                this.Task = reader.ReadLineAsync();
            }

            public TextReader Reader { get; }
            public Task<string> Task { get; }

            public bool Equals(ReaderAndTask that) => this.Reader == that.Reader && this.Task == that.Task;

            public override bool Equals(object obj) => obj is ReaderAndTask that && this.Equals(that);

            public override int GetHashCode() => this.Reader.GetHashCode() ^ this.Task.GetHashCode();
        }
    }
}
