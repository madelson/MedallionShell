using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Medallion.Shell.Streams
{
    /// <summary>
    /// An implementation of <see cref="TextReader"/> with additional methods to control behavior. The 
    /// </summary>
    public abstract class ProcessStreamReader : TextReader
    {
        // prevents external inheritors
        internal ProcessStreamReader() { }

        /// <summary>
        /// Provides access to the underlying <see cref="Stream"/>. Equivalent to <see cref="StreamReader.BaseStream"/>
        /// </summary>
        public abstract Stream BaseStream { get; }

        /// <summary>
        /// Enumerates each remaining line of output. The enumerable cannot be re-used
        /// </summary>
        public IEnumerable<string> GetLines()
        {
            return new LinesEnumerable(this);
        }

        private class LinesEnumerable : IEnumerable<string>
        {
            private readonly TextReader reader;
            private int consumed;

            public LinesEnumerable(TextReader reader)
            {
                this.reader = reader;
            }

            IEnumerator<string> IEnumerable<string>.GetEnumerator()
            {
                Throw<InvalidOperationException>.If(
                    Interlocked.Exchange(ref this.consumed, 1) != 0, 
                    "The enumerable returned by GetLines() can only be enumerated once"
                );

                return this.GetEnumeratorInternal();
            }

            private IEnumerator<string> GetEnumeratorInternal()
            {
                string line;
                while ((line = this.reader.ReadLine()) != null)
                {
                    yield return line;
                }
            }

            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
            {
                return this.AsEnumerable().GetEnumerator();
            }
        }

        /// <summary>
        /// Discards all output from the underlying stream. This prevents the process from blocking because
        /// the output pipe's buffer is full without wasting any memory on buffering the output
        /// </summary>
        public abstract void Discard();

        /// <summary>
        /// By default, the underlying stream output is buffered to prevent the process from blocking
        /// because the output pipe's buffer is full. Calling this method disables this behavior. This is useful
        /// when it is desirable to have the process block while waiting for output to be read
        /// </summary>
        public abstract void StopBuffering();

        /// <summary>
        /// Pipes the output of the underlying stream to the given stream. This occurs asynchronously, so this
        /// method returns before all content has been written
        /// </summary>
        public Task PipeToAsync(Stream stream, bool leaveReaderOpen = false, bool leaveStreamOpen = false)
        {
            Throw.IfNull(stream, "stream");

            return this.PipeAsync(
                () => this.BaseStream.CopyToAsync(stream),
                leaveOpen: leaveReaderOpen,
                extraDisposeAction: leaveStreamOpen ? default(Action) : () => stream.Dispose()
            );
        }

        /// <summary>
        /// Pipes the output of the reader to the given writer. This occurs asynchronously, so this
        /// method returns before all content has been written
        /// </summary>
        public Task PipeToAsync(TextWriter writer, bool leaveReaderOpen = false, bool leaveWriterOpen = false)
        {
            Throw.IfNull(writer, "writer");

            return this.CopyToAsync(writer, leaveReaderOpen: leaveReaderOpen, leaveWriterOpen: leaveWriterOpen);
        }

        /// <summary>
        /// Asynchronously copies each line of output to the given collection
        /// </summary>
        public Task PipeToAsync(ICollection<string> lines, bool leaveReaderOpen = false)
        {
            Throw.IfNull(lines, "lines");

            return this.PipeAsync(
                async () =>
                {
                    string line;
                    while ((line = await this.ReadLineAsync().ConfigureAwait(false)) != null)
                    {
                        lines.Add(line);
                    }
                },
                leaveOpen: leaveReaderOpen
            );
        }

        /// <summary>
        /// Asynchronously writes all output to the given file
        /// </summary>
        public Task PipeToAsync(FileInfo file, bool leaveReaderOpen = false)
        {
            Throw.IfNull(file, "file");
            
            // used over FileInfo.OpenWrite to get read file share, which seems potentially useful and
            // not that harmful
            var stream = new FileStream(file.FullName, FileMode.Create, FileAccess.Write, FileShare.Read);
            return this.PipeToAsync(stream, leaveReaderOpen: leaveReaderOpen, leaveStreamOpen: false);
        }

        /// <summary>
        /// Asynchronously copies each charater to the given collection
        /// </summary>
        public Task PipeToAsync(ICollection<char> chars, bool leaveReaderOpen = false)
        {
            Throw.IfNull(chars, "chars");

            return this.PipeAsync(
                async () =>
                {
                    var buffer = new char[Constants.CharBufferSize];
                    int bytesRead;
                    while ((bytesRead = await this.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false)) != 0)
                    {
                        for (var i = 0; i < bytesRead; ++i) 
                        {
                            chars.Add(buffer[i]);
                        }
                    }
                },
                leaveOpen: leaveReaderOpen
            );
        }
    }
}
