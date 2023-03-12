using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Medallion.Shell.Streams
{
    /// <summary>
    /// Provides functionality similar to a <see cref="StreamWriter"/> but with additional methods to simplify
    /// working with a process's standard input
    /// </summary>
    public sealed class ProcessStreamWriter : TextWriter
    {
        private readonly StreamWriter writer;

        internal ProcessStreamWriter(StreamWriter writer, Encoding encoding)
        {
            var stream = ProcessStreamWrapper.WrapIfNeeded(writer.BaseStream, isReadStream: false);

            this.writer = stream == writer.BaseStream && Equals(writer.Encoding, encoding)
                ? writer
                // Unfortunately, changing the encoding on older frameworks can't be done via ProcessStartInfo so
                // we have to do it manually here. See https://github.com/dotnet/corefx/issues/20497
                : new(stream, encoding);
            this.AutoFlush = true; // set the default
        }

        #region ---- Custom methods ----
        /// <summary>
        /// Provides access to the underlying <see cref="Stream"/>. Equivalent to <see cref="StreamWriter.BaseStream"/>
        /// </summary>
        public Stream BaseStream => this.writer.BaseStream;

        /// <summary>
        /// Determines whether writes are automatically flushed to the underlying <see cref="Stream"/> after each write.
        /// Equivalent to <see cref="StreamWriter.AutoFlush"/>. Defaults to TRUE
        /// </summary>
        public bool AutoFlush
        {
            get => this.writer.AutoFlush;
            set => this.writer.AutoFlush = value;
        }

        /// <summary>
        /// Asynchronously copies <paramref name="stream"/> to this stream
        /// </summary>
        public Task PipeFromAsync(Stream stream, bool leaveWriterOpen = false, bool leaveStreamOpen = false)
        {
            Throw.IfNull(stream, nameof(stream));

            return this.PipeAsync(
                async () =>
                {
                    // flush any content buffered in the writer, since we'll be using the raw stream
                    await this.writer.FlushAsync().ConfigureAwait(false);
                    if (this.AutoFlush)
                    {
                        // if the writer is configured to autoflush, we preserve that behavior when
                        // piping to the writer from a stream even though for performance we are bypassing
                        // this.writer in this case
                        await stream.CopyToAsyncWithAutoFlush(this.BaseStream).ConfigureAwait(false);
                    }
                    else
                    {
                        await stream.CopyToAsync(this.BaseStream).ConfigureAwait(false);
                    }
                },
                leaveOpen: leaveWriterOpen,
                extraDisposeAction: leaveStreamOpen ? null : stream.Dispose
            );
        }

        /// <summary>
        /// Asynchronously writes each item in <paramref name="lines"/> to this writer as a separate line
        /// </summary>
        public Task PipeFromAsync(IEnumerable<string> lines, bool leaveWriterOpen = false)
        {
            Throw.IfNull(lines, nameof(lines));

            return this.PipeAsync(
                // wrap in Task.Run since GetEnumerator() or MoveNext() might block
                () => Task.Run(async () =>
                {
                    foreach (var line in lines)
                    {
                        await this.WriteLineAsync(line).ConfigureAwait(false);
                    }
                }),
                leaveOpen: leaveWriterOpen
            );
        }

        /// <summary>
        /// Asynchronously writes all content from <paramref name="reader"/> to this writer
        /// </summary>
        public Task PipeFromAsync(TextReader reader, bool leaveWriterOpen = false, bool leaveReaderOpen = false)
        {
            Throw.IfNull(reader, nameof(reader));

            return reader.CopyToAsync(this.writer, leaveReaderOpen: leaveReaderOpen, leaveWriterOpen: leaveWriterOpen);
        }

        /// <summary>
        /// Asynchronously writes all content from <paramref name="file"/> to this stream
        /// </summary>
        public Task PipeFromAsync(FileInfo file, bool leaveWriterOpen = false)
        {
            Throw.IfNull(file, nameof(file));

            var stream = file.OpenRead();
            return this.PipeFromAsync(stream, leaveWriterOpen: leaveWriterOpen, leaveStreamOpen: false);
        }

        /// <summary>
        /// Asynchronously writes all content from <paramref name="chars"/> to this writer
        /// </summary>
        public Task PipeFromAsync(IEnumerable<char> chars, bool leaveWriterOpen = false)
        {
            Throw.IfNull(chars, nameof(chars));

            return this.PipeAsync(
                chars is string @string
                    // special-case string since we can use the built-in WriteAsync
                    ? new Func<Task>(() => this.WriteAsync(@string))
                    // when enumerating, layer on a Task.Run since GetEnumerator() or MoveNext() might block
                    : () => Task.Run(async () =>
                    {
                        var buffer = new char[Constants.CharBufferSize];
                        using var enumerator = chars.GetEnumerator();
                        while (true)
                        {
                            var i = 0;
                            while (i < buffer.Length && enumerator.MoveNext())
                            {
                                buffer[i++] = enumerator.Current;
                            }
                            if (i > 0)
                            {
                                await this.WriteAsync(buffer, 0, count: i).ConfigureAwait(false);
                            }
                            else
                            {
                                break;
                            }
                        }
                    }),
                leaveOpen: leaveWriterOpen
            );
        }
        #endregion

        #region ---- TextWriter methods ----
        /// <summary>
        /// Forwards to the implementation in the <see cref="StreamWriter"/> class
        /// </summary>
        protected override void Dispose(bool disposing)
        {
            if (disposing) { this.writer.Dispose(); }
        }

        /// <summary>
        /// Forwards to the implementation in the <see cref="StreamWriter"/> class
        /// </summary>
        public override Encoding Encoding => this.writer.Encoding;

        /// <summary>
        /// Forwards to the implementation in the <see cref="StreamWriter"/> class
        /// </summary>
        public override void Flush() => this.writer.Flush();

        /// <summary>
        /// Forwards to the implementation in the <see cref="StreamWriter"/> class
        /// </summary>
        public override Task FlushAsync() => this.writer.FlushAsync();

        /// <summary>
        /// Forwards to the implementation in the <see cref="StreamWriter"/> class
        /// </summary>
        public override IFormatProvider FormatProvider => this.writer.FormatProvider;

        /// <summary>
        /// Forwards to the implementation in the <see cref="StreamWriter"/> class
        /// </summary>
        [AllowNull]
        public override string NewLine { get => this.writer.NewLine; set => this.writer.NewLine = value; }

        /// <summary>
        /// Forwards to the implementation in the <see cref="StreamWriter"/> class
        /// </summary>
        public override void Write(bool value) => this.writer.Write(value);

        /// <summary>
        /// Forwards to the implementation in the <see cref="StreamWriter"/> class
        /// </summary>
        public override void Write(char value) => this.writer.Write(value);

        /// <summary>
        /// Forwards to the implementation in the <see cref="StreamWriter"/> class
        /// </summary>
        public override void Write(char[]? buffer) => this.writer.Write(buffer);

        /// <summary>
        /// Forwards to the implementation in the <see cref="StreamWriter"/> class
        /// </summary>
        public override void Write(char[] buffer, int index, int count) => this.writer.Write(buffer, index, count);

        /// <summary>
        /// Forwards to the implementation in the <see cref="StreamWriter"/> class
        /// </summary>
        public override void Write(decimal value) => this.writer.Write(value);

        /// <summary>
        /// Forwards to the implementation in the <see cref="StreamWriter"/> class
        /// </summary>
        public override void Write(double value) => this.writer.Write(value);

        /// <summary>
        /// Forwards to the implementation in the <see cref="StreamWriter"/> class
        /// </summary>
        public override void Write(float value) => this.writer.Write(value);

        /// <summary>
        /// Forwards to the implementation in the <see cref="StreamWriter"/> class
        /// </summary>
        public override void Write(int value) => this.writer.Write(value);

        /// <summary>
        /// Forwards to the implementation in the <see cref="StreamWriter"/> class
        /// </summary>
        public override void Write(long value) => this.writer.Write(value);

        /// <summary>
        /// Forwards to the implementation in the <see cref="StreamWriter"/> class
        /// </summary>
        public override void Write(object? value) => this.writer.Write(value);

        /// <summary>
        /// Forwards to the implementation in the <see cref="StreamWriter"/> class
        /// </summary>
        public override void Write(string format, object? arg0) => this.writer.Write(format, arg0);

        /// <summary>
        /// Forwards to the implementation in the <see cref="StreamWriter"/> class
        /// </summary>
        public override void Write(string format, object? arg0, object? arg1) => this.writer.Write(format, arg0, arg1);

        /// <summary>
        /// Forwards to the implementation in the <see cref="StreamWriter"/> class
        /// </summary>
        public override void Write(string format, object? arg0, object? arg1, object? arg2) => this.writer.Write(format, arg0, arg1, arg2);

        /// <summary>
        /// Forwards to the implementation in the <see cref="StreamWriter"/> class
        /// </summary>
        public override void Write(string format, params object?[] arg) => this.writer.Write(format, arg);

        /// <summary>
        /// Forwards to the implementation in the <see cref="StreamWriter"/> class
        /// </summary>
        public override void Write(string? value) => this.writer.Write(value);

        /// <summary>
        /// Forwards to the implementation in the <see cref="StreamWriter"/> class
        /// </summary>
        public override void Write(uint value) => this.writer.Write(value);

        /// <summary>
        /// Forwards to the implementation in the <see cref="StreamWriter"/> class
        /// </summary>
        public override void Write(ulong value) => this.writer.Write(value);

        /// <summary>
        /// Forwards to the implementation in the <see cref="StreamWriter"/> class
        /// </summary>
        public override Task WriteAsync(char value) => this.writer.WriteAsync(value);

        /// <summary>
        /// Forwards to the implementation in the <see cref="StreamWriter"/> class
        /// </summary>
        public override Task WriteAsync(char[] buffer, int index, int count) => this.writer.WriteAsync(buffer, index, count);

        /// <summary>
        /// Forwards to the implementation in the <see cref="StreamWriter"/> class
        /// </summary>
        public override Task WriteAsync(string? value) => this.writer.WriteAsync(value);

        /// <summary>
        /// Forwards to the implementation in the <see cref="StreamWriter"/> class
        /// </summary>
        public override void WriteLine() => this.writer.WriteLine();

        /// <summary>
        /// Forwards to the implementation in the <see cref="StreamWriter"/> class
        /// </summary>
        public override void WriteLine(bool value) => this.writer.WriteLine(value);

        /// <summary>
        /// Forwards to the implementation in the <see cref="StreamWriter"/> class
        /// </summary>
        public override void WriteLine(char value) => this.writer.WriteLine(value);

        /// <summary>
        /// Forwards to the implementation in the <see cref="StreamWriter"/> class
        /// </summary>
        public override void WriteLine(char[]? buffer) => this.writer.WriteLine(buffer);

        /// <summary>
        /// Forwards to the implementation in the <see cref="StreamWriter"/> class
        /// </summary>
        public override void WriteLine(char[] buffer, int index, int count) => this.writer.WriteLine(buffer, index, count);

        /// <summary>
        /// Forwards to the implementation in the <see cref="StreamWriter"/> class
        /// </summary>
        public override void WriteLine(decimal value) => this.writer.WriteLine(value);

        /// <summary>
        /// Forwards to the implementation in the <see cref="StreamWriter"/> class
        /// </summary>
        public override void WriteLine(double value) => this.writer.WriteLine(value);

        /// <summary>
        /// Forwards to the implementation in the <see cref="StreamWriter"/> class
        /// </summary>
        public override void WriteLine(float value) => this.writer.WriteLine(value);

        /// <summary>
        /// Forwards to the implementation in the <see cref="StreamWriter"/> class
        /// </summary>
        public override void WriteLine(int value) => this.writer.WriteLine(value);

        /// <summary>
        /// Forwards to the implementation in the <see cref="StreamWriter"/> class
        /// </summary>
        public override void WriteLine(long value) => this.writer.WriteLine(value);

        /// <summary>
        /// Forwards to the implementation in the <see cref="StreamWriter"/> class
        /// </summary>
        public override void WriteLine(object? value) => this.writer.WriteLine(value);

        /// <summary>
        /// Forwards to the implementation in the <see cref="StreamWriter"/> class
        /// </summary>
        public override void WriteLine(string format, object? arg0) => this.writer.WriteLine(format, arg0);

        /// <summary>
        /// Forwards to the implementation in the <see cref="StreamWriter"/> class
        /// </summary>
        public override void WriteLine(string format, object? arg0, object? arg1) => this.writer.WriteLine(format, arg0, arg1);

        /// <summary>
        /// Forwards to the implementation in the <see cref="StreamWriter"/> class
        /// </summary>
        public override void WriteLine(string format, object? arg0, object? arg1, object? arg2) => this.writer.WriteLine(format, arg0, arg1, arg2);

        /// <summary>
        /// Forwards to the implementation in the <see cref="StreamWriter"/> class
        /// </summary>
        public override void WriteLine(string format, params object?[] arg) => this.writer.WriteLine(format, arg);

        /// <summary>
        /// Forwards to the implementation in the <see cref="StreamWriter"/> class
        /// </summary>
        public override void WriteLine(string? value) => this.writer.WriteLine(value);

        /// <summary>
        /// Forwards to the implementation in the <see cref="StreamWriter"/> class
        /// </summary>
        public override void WriteLine(uint value) => this.writer.WriteLine(value);

        /// <summary>
        /// Forwards to the implementation in the <see cref="StreamWriter"/> class
        /// </summary>
        public override void WriteLine(ulong value) => this.writer.WriteLine(value);

        /// <summary>
        /// Forwards to the implementation in the <see cref="StreamWriter"/> class
        /// </summary>
        public override Task WriteLineAsync() => this.writer.WriteLineAsync();

        /// <summary>
        /// Forwards to the implementation in the <see cref="StreamWriter"/> class
        /// </summary>
        public override Task WriteLineAsync(char value) => this.writer.WriteLineAsync(value);

        /// <summary>
        /// Forwards to the implementation in the <see cref="StreamWriter"/> class
        /// </summary>
        public override Task WriteLineAsync(char[] buffer, int index, int count) => this.writer.WriteLineAsync(buffer, index, count);

        /// <summary>
        /// Forwards to the implementation in the <see cref="StreamWriter"/> class
        /// </summary>
        public override Task WriteLineAsync(string? value) => this.writer.WriteLineAsync(value);

#if NETCOREAPP2_1_OR_GREATER || NETSTANDARD2_1
        /// <summary>
        /// Forwards to the implementation in the <see cref="StreamWriter"/> class
        /// </summary>
        public override void Write(ReadOnlySpan<char> buffer) => this.writer.Write(buffer);

        /// <summary>
        /// Forwards to the implementation in the <see cref="StreamWriter"/> class
        /// </summary>
        public override void WriteLine(ReadOnlySpan<char> buffer) => this.writer.WriteLine(buffer);

        /// <summary>
        /// Forwards to the implementation in the <see cref="StreamWriter"/> class
        /// </summary>
        public override Task WriteAsync(ReadOnlyMemory<char> buffer, CancellationToken cancellationToken = default) => this.writer.WriteAsync(buffer);

        /// <summary>
        /// Forwards to the implementation in the <see cref="StreamWriter"/> class
        /// </summary>
        public override Task WriteLineAsync(ReadOnlyMemory<char> buffer, CancellationToken cancellationToken = default) => this.writer.WriteLineAsync(buffer, cancellationToken);
#endif
        #endregion
    }
}
