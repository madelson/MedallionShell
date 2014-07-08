using Medallion.Shell.Async;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Medallion.Shell.Streams
{
    internal sealed class ProcessStreamHandler
    {
        public enum Mode
        {
            /// <summary>
            /// The contents of the stream is buffered internally so that the process will never be blocked because
            /// the pipe is full. This is the default mode.
            /// </summary>
            Buffer = 0,
            /// <summary>
            /// The contents of the stream can be accessed manually via <see cref="Stream"/>
            /// operations. However, internal buffering ensures that the process will never be blocked because
            /// the output stream internal buffer is full.
            /// </summary>
            BufferedManualRead,
            /// <summary>
            /// The contents of the stream can be accessed manually via <see cref="Stream"/>
            /// operations. If the process writes faster than the consumer reads, it may fill the buffer
            /// and block
            /// </summary>
            ManualRead,
            /// <summary>
            /// The contents of the stream is read and discarded
            /// </summary>
            DiscardContents,
            /// <summary>
            /// The contents of the stream is piped to another stream
            /// </summary>
            Piped,
        }

        /// <summary>
        /// The underlying output stream of the process
        /// </summary>
        private readonly Stream processStream;

        private readonly AsyncLock @lock = new AsyncLock();
        /// <summary>
        /// Acts as a buffer for data coming from <see cref="processStream"/>. 
        /// Access to this is protected by <see cref="lock"/>
        /// </summary>
        private MemoryStream memoryStream;
        
        private Mode mode;
        private Stream pipeStream;
        private volatile ProcessStreamReader reader; // TODO use Lazy<T> to avoid locking in SetMode()

        // TODO we should fire a finished event which can be used as a task completion source
        private volatile bool finished;

        public ProcessStreamHandler(Stream stream)
        {
            Throw.IfNull(stream, "stream");
            this.processStream = stream;
        }

        public ProcessStreamReader Reader
        {
            get
            {
                if (this.reader == null)
                {
                    this.SetMode(Mode.BufferedManualRead);
                }
                return this.reader;
            }
        }

        public void SetMode(Mode mode, Stream pipeStream = null)
        {
            Throw.If(mode == Mode.Piped != (pipeStream != null), "pipeStream: must be non-null if an only if switching to piped mode");

            using (this.@lock.AcquireAsync().Result)
            {
                // when just buffering, you can switch to any other mode (important since we start
                // in this mode)
                if (this.mode == Mode.Buffer
                    // when in manual read mode, you can always start buffering
                    || (this.mode == Mode.ManualRead && mode == Mode.BufferedManualRead)
                    // when in buffered read mode, you can always stop buffering
                    || (this.mode == Mode.BufferedManualRead && mode == Mode.ManualRead))
                {
                    // when we switch into manual read mode, create the manual read stream
                    if (mode == Mode.ManualRead || mode == Mode.BufferedManualRead)
                    {
                        this.reader = new InternalReader(this);
                    }

                    this.mode = mode;
                    this.pipeStream = pipeStream;
                }
                else if (this.mode != mode || pipeStream != this.pipeStream)
                {
                    string message;
                    switch (this.mode)
                    {
                        case Mode.DiscardContents:
                            message = "The stream has been set to discard its contents, so it cannot be used in another mode";
                            break;
                        case Mode.Piped:
                            message = pipeStream != this.pipeStream
                                ? "The stream is already being piped to a different stream"
                                : "The stream is being piped to another stream, so it cannot be used in another mode";
                            break;
                        default:
                            throw new NotImplementedException("Unexpected mode " + mode.ToString());
                    }

                    throw new InvalidOperationException(message);
                }
            }
        }

        private async Task ReadLoop()
        {
            Mode mode;
            var localBuffer = new byte[512];
            while (!this.finished)
            {
                using (await this.@lock.AcquireAsync().ConfigureAwait(false))
                {
                    mode = this.mode;

                    if (mode == Mode.Buffer || mode == Mode.BufferedManualRead)
                    {
                        var bytesRead = await this.processStream.ReadAsync(localBuffer, offset: 0, count: localBuffer.Length).ConfigureAwait(false);
                        if (bytesRead > 0)
                        {
                            if (this.memoryStream == null)
                            {
                                this.memoryStream = new MemoryStream();
                            }
                            this.memoryStream.Write(localBuffer, offset: 0, count: localBuffer.Length);
                        }
                        else if (bytesRead < 0)
                        {
                            this.finished = true;
                        }
                    }
                }

                if (mode == Mode.Piped)
                {
                    await this.memoryStream.CopyToAsync(this.pipeStream).ConfigureAwait(false);
                    await this.processStream.CopyToAsync(this.processStream).ConfigureAwait(false);
                    this.finished = true;
                }

                else if (mode == Mode.DiscardContents)
                {
                    using (await this.@lock.AcquireAsync().ConfigureAwait(false))
                    {
                        this.memoryStream = null;
                    }

                    int readResult;
                    do
                    {
                        readResult = await this.processStream.ReadAsync(localBuffer, offset: 0, count: localBuffer.Length);
                    }
                    while (readResult >= 0);
                }
            }
        }

        #region ---- Stream implementation ----
        private sealed class InternalStream : Stream
        {
            private readonly ProcessStreamHandler handler;
            private MemoryStream memoryStream;

            public InternalStream(ProcessStreamHandler handler)
            {
                this.handler = handler;
            }

            public override bool CanRead
            {
                get { return true; }
            }

            public override bool CanSeek
            {
                get { return false; }
            }

            public override bool CanWrite
            {
                get { return false; }
            }

            public override void Flush()
            {
                // no-op, since we don't write
            }

            public override long Length
            {
                get { throw new NotSupportedException(MethodBase.GetCurrentMethod().Name); }
            }

            public override long Position
            {
                get { throw new NotSupportedException(MethodBase.GetCurrentMethod().Name); }
                set { throw new NotSupportedException(MethodBase.GetCurrentMethod().Name); }
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                Throw.IfNull(buffer, "buffer");
                // offset is allowed to be buffer.Length. See http://referencesource.microsoft.com/#mscorlib/system/io/memorystream.cs
                Throw.IfOutOfRange(offset, "offset", min: 0, max: buffer.Length);
                Throw.IfOutOfRange(count, "count", min: 0, max: buffer.Length - offset);

                using (this.handler.@lock.AcquireAsync().Result)
                {
                    if (this.handler.finished)
                    {
                        return -1;
                    }

                    // first read from the memory streams
                    var bytesReadFromMemoryStreams = this.ReadFromMemoryStreams(buffer, offset, count);

                    // if there are still bytes to be read, read from the underlying stream
                    if (bytesReadFromMemoryStreams < count)
                    {
                        var bytesReadFromProcessStream = this.handler.processStream.Read(buffer, offset - bytesReadFromMemoryStreams, count - bytesReadFromMemoryStreams);
                        if (bytesReadFromProcessStream < 0)
                        {
                            this.handler.finished = true;
                            return -1;
                        }
                        return bytesReadFromMemoryStreams + bytesReadFromProcessStream;
                    }

                    // otherwise, just return the bytes from the memory stream
                    return bytesReadFromMemoryStreams;
                }
            }

            public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            {
                Throw.IfNull(buffer, "buffer");
                // offset is allowed to be buffer.Length. See http://referencesource.microsoft.com/#mscorlib/system/io/memorystream.cs
                Throw.IfOutOfRange(offset, "offset", min: 0, max: buffer.Length);
                Throw.IfOutOfRange(count, "count", min: 0, max: buffer.Length - offset);

                using (await this.handler.@lock.AcquireAsync().ConfigureAwait(false))
                {
                    if (this.handler.finished)
                    {
                        return -1;
                    }

                    // first read from the memory streams
                    var bytesReadFromMemoryStreams = this.ReadFromMemoryStreams(buffer, offset, count);

                    // if there are still bytes to be read, read from the underlying stream
                    if (bytesReadFromMemoryStreams < count)
                    {
                        var bytesReadFromProcessStream = await this.handler.processStream.ReadAsync(buffer, offset - bytesReadFromMemoryStreams, count - bytesReadFromMemoryStreams)
                            .ConfigureAwait(false);
                        if (bytesReadFromProcessStream < 0)
                        {
                            this.handler.finished = true;
                            return -1;
                        }
                        return bytesReadFromMemoryStreams + bytesReadFromProcessStream;
                    }

                    // otherwise, just return the bytes from the memory stream
                    return bytesReadFromMemoryStreams;
                }
            }

            /// <summary>
            /// Attempts to read from the memory stream buffers if the are available.
            /// Returns the number of bytes read (never -1)
            /// </summary>
            private int ReadFromMemoryStreams(byte[] buffer, int offset, int count)
            {
                // if we have memory stream, try to read from that
                if (this.memoryStream != null)
                {
                    var result = this.memoryStream.Read(buffer, offset, count);
                    if (result > 0)
                    {
                        return result;
                    }
                    else
                    {
                        this.memoryStream.Dispose();
                        this.memoryStream = null;
                        // fall through
                    }
                }

                // if the handler has a memory stream, copy it to our memory
                // stream and call again
                if (this.handler.memoryStream != null)
                {
                    this.memoryStream = this.handler.memoryStream;
                    this.handler.memoryStream = null;
                    this.memoryStream.Seek(0, SeekOrigin.Begin);
                    return this.ReadFromMemoryStreams(buffer, offset, count);
                }

                // otherwise, we've exhausted all memory streams so return false
                return 0;
            }

            public override long Seek(long offset, SeekOrigin origin)
            {
                throw new NotSupportedException(MethodBase.GetCurrentMethod().Name);
            }

            public override void SetLength(long value)
            {
                throw new NotSupportedException(MethodBase.GetCurrentMethod().Name);
            }

            public override void Write(byte[] buffer, int offset, int count)
            {
                throw new NotSupportedException(MethodBase.GetCurrentMethod().Name);
            }

            protected override void Dispose(bool disposing)
            {
                // no-op
            }
        }
        #endregion

        #region ---- ProcessStreamReader implementation ----
        private sealed class InternalReader : ProcessStreamReader
        {
            private readonly ProcessStreamHandler handler;

            public InternalReader(ProcessStreamHandler handler)
                : base(new InternalStream(handler))
            {
                this.handler = handler;
            }

            public override void Discard()
            {
                this.handler.SetMode(Mode.DiscardContents);
            }

            public override void StopBuffering()
            {
                this.handler.SetMode(Mode.ManualRead);
            }

            public override void PipeTo(Stream stream)
            {
                Throw.IfNull(stream, "stream");

                this.handler.SetMode(Mode.Piped, stream);
            }

            public override void PipeTo(TextWriter writer)
            {
                Throw.IfNull(writer, "writer");

                Task.Run(async () => await this.PipeToWriterAsync(writer).ConfigureAwait(false));
                // TODO do something with the exception?
            }

            private async Task PipeToWriterAsync(TextWriter writer)
            {
                var buffer = new char[512];
                while (true)
                {
                    var bytesRead = await this.ReadAsync(buffer, index: 0, count: buffer.Length).ConfigureAwait(false);
                    if (bytesRead < 0)
                    {
                        break;
                    }
                    await writer.WriteAsync(buffer, index: 0, count: buffer.Length);
                }
            }
        }
        #endregion
    }
}