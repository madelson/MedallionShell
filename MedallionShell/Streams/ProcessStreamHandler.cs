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
        /// <summary>
        /// Acts as a buffer for data coming from <see cref="processStream"/>
        /// Volatile just to be safe, since this is mutable in async contexts
        /// </summary>
        private volatile MemoryStream memoryStream;
        /// <summary>
        /// Protects reads and writes to all streams
        /// </summary>
        private readonly AsyncLock streamLock = new AsyncLock();

        /// <summary>
        /// Protects access to <see cref="mode"/> and related variables
        /// </summary>
        private readonly object modeLock = new object();
        /// <summary>
        /// Represents the current mode of the handler
        /// </summary>
        private Mode mode;
        /// <summary>
        /// Represents an output stream for <see cref="Mode.Piped"/>
        /// </summary>
        private Stream pipeStream;

        /// <summary>
        /// Exposes the underlying stream
        /// </summary>
        private readonly ProcessStreamReader reader;

        /// <summary>
        /// Used to track when the stream is fully consumed, as well as errors that may occur in various tasks
        /// </summary>
        private readonly TaskCompletionSource<bool> taskCompletionSource = new TaskCompletionSource<bool>();

        public ProcessStreamHandler(Stream stream)
        {
            Throw.IfNull(stream, "stream");

            this.processStream = stream;
            this.reader = new InternalReader(this);
            Task.Run(async () => await this.ReadLoop().ConfigureAwait(false))
                .ContinueWith(t => {
                    if (t.IsFaulted)
                    {
                        this.taskCompletionSource.TrySetException(t.Exception);
                    }
                    else
                    {
                        this.taskCompletionSource.TrySetResult(true);
                    }
                });
        }

        public ProcessStreamReader Reader
        {
            get { return this.reader; }
        }

        public void SetMode(Mode mode, Stream pipeStream = null)
        {
            Throw.If(mode == Mode.Piped != (pipeStream != null), "pipeStream: must be non-null if an only if switching to piped mode");
            Throw.If(pipeStream != null && !pipeStream.CanWrite, "pipeStream: must be writable");

            lock (this.modeLock)
            {
                // when just buffering, you can switch to any other mode (important since we start
                // in this mode)
                if (this.mode == Mode.Buffer
                    // when in manual read mode, you can always start buffering
                    || (this.mode == Mode.ManualRead && mode == Mode.BufferedManualRead)
                    // when in buffered read mode, you can always stop buffering
                    || (this.mode == Mode.BufferedManualRead && mode == Mode.ManualRead))
                {
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
                        case Mode.ManualRead:
                        case Mode.BufferedManualRead:
                            message = "The stream is already being read from, so it cannot be used in another mode";
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
            var localBuffer = new byte[512];
            while (true)
            {
                // safely capture the current mode
                Mode mode;
                lock (this.modeLock)
                {
                    mode = this.mode;
                }

                switch (mode)
                {
                    case Mode.BufferedManualRead:
                        // in buffered manual read mode, we read from the buffer periodically
                        // to avoid the process blocking. Thus, we delay and then jump to the manual read
                        // case
                        await Task.Delay(millisecondsDelay: 100).ConfigureAwait(false);
                        goto case Mode.ManualRead;
                    case Mode.Buffer:
                    case Mode.ManualRead:
                        // grab the stream lock and read some bytes into the buffer
                        using (await this.streamLock.AcquireAsync().ConfigureAwait(false))
                        {
                            // initialized memory stream if necessary. Note that this can happen
                            // more than once due to InternalStream "grabbing" the memory stream
                            if (this.memoryStream == null)
                            {
                                this.memoryStream = new MemoryStream();
                            }

                            // read from the process
                            var bytesRead = await this.processStream.ReadAsync(localBuffer, offset: 0, count: localBuffer.Length).ConfigureAwait(false);
                            if (bytesRead < 0)
                            {
                                return; // end of stream
                            }

                            // write to the buffer
                            this.memoryStream.Write(localBuffer, offset: 0, count: bytesRead);
                        }
                        break;
                    case Mode.DiscardContents:
                        // grab the stream lock and just read to the end
                        using (await this.streamLock.AcquireAsync().ConfigureAwait(false))
                        {
                            if (this.memoryStream != null)
                            { 
                                // free the memory stream
                                this.memoryStream.Dispose();
                                this.memoryStream = null;
                            }
                            while (true)
                            {
                                var bytesRead = await this.processStream.ReadAsync(localBuffer, offset: 0, count: localBuffer.Length).ConfigureAwait(false);
                                if (bytesRead < 0)
                                {
                                    return; // end of stream
                                }
                            }
                        }
                    case Mode.Piped:
                        // grab the stream lock and copy the buffer + the stream to the output
                        using (await this.streamLock.AcquireAsync().ConfigureAwait(false))
                        {
                            if (this.memoryStream != null)
                            {
                                // copy & free any buffered content
                                this.memoryStream.CopyTo(this.pipeStream);
                                this.memoryStream.Dispose();
                                this.memoryStream = null;
                            }

                            await this.processStream.CopyToAsync(this.pipeStream).ConfigureAwait(false);
                        }
                        return; // end of stream
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

                using (this.handler.streamLock.AcquireAsync().Result)
                {
                    // TODO
                    //if (this.handler.finished)
                    //{
                    //    return -1;
                    //}

                    // first read from the memory streams
                    var bytesReadFromMemoryStreams = this.ReadFromMemoryStreams(buffer, offset, count);

                    // if there are still bytes to be read, read from the underlying stream
                    if (bytesReadFromMemoryStreams < count)
                    {
                        var bytesReadFromProcessStream = this.handler.processStream.Read(buffer, offset - bytesReadFromMemoryStreams, count - bytesReadFromMemoryStreams);
                        if (bytesReadFromProcessStream < 0)
                        {
                            // TODO
                            //this.handler.finished = true;
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

                using (await this.handler.streamLock.AcquireAsync().ConfigureAwait(false))
                {
                    // TODO
                    //if (this.handler.finished)
                    //{
                    //    return -1;
                    //}

                    // first read from the memory streams
                    var bytesReadFromMemoryStreams = this.ReadFromMemoryStreams(buffer, offset, count);

                    // if there are still bytes to be read, read from the underlying stream
                    if (bytesReadFromMemoryStreams < count)
                    {
                        var bytesReadFromProcessStream = await this.handler.processStream.ReadAsync(buffer, offset - bytesReadFromMemoryStreams, count - bytesReadFromMemoryStreams)
                            .ConfigureAwait(false);
                        if (bytesReadFromProcessStream < 0)
                        {
                            // TODO
                            //this.handler.finished = true;
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