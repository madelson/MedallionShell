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
        private enum Mode
        {
            /// <summary>
            /// As <see cref="Mode.Buffer"/>, but can transition to other modes
            /// </summary>
            Default = 0,
            /// <summary>
            /// The contents of the stream is buffered internally so that the process will never be blocked because
            /// the pipe is full. This is behaviorally equivalent to the default mode
            /// </summary>
            Buffer,
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
            /// The contents of the stream is discarded. This is implemented by closing the underlying stream
            /// </summary>
            DiscardContents,
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
        /// Exposes the underlying stream
        /// </summary>
        private readonly ProcessStreamReader reader;

        /// <summary>
        /// Used to track when the stream is fully consumed, as well as errors that may occur in various tasks
        /// </summary>
        private readonly TaskCompletionSource<bool> taskCompletionSource = new TaskCompletionSource<bool>();

        private readonly Task readLoopTask;

        public ProcessStreamHandler(Stream stream)
        {
            Throw.IfNull(stream, "stream");

            this.processStream = stream;
            this.reader = new InternalReader(this);
            // we don't need Task.Run here since this reaches an await very
            // quickly
            this.readLoopTask = this.ReadLoop()
                .ContinueWith(t => 
                {
                    Log.WriteLine("ReadLoop finished for {0} (success = {1})", stream.GetHashCode(), !t.IsFaulted);
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

        public Task Task { get { return this.taskCompletionSource.Task; } }

        private void SetMode(Mode mode)
        {
            lock (this.modeLock)
            {
                // when in the default mode, you can switch to any other mode (important since we start
                // in this mode)
                if (this.mode == Mode.Default
                    // can always go into discard mode
                    || mode == Mode.DiscardContents
                    // when in manual read mode, you can always start buffering
                    || (this.mode == Mode.ManualRead && mode == Mode.BufferedManualRead)
                    // when in buffered read mode, you can always stop buffering
                    || (this.mode == Mode.BufferedManualRead && mode == Mode.ManualRead))
                {
                    this.mode = mode;
                    if (mode == Mode.DiscardContents && this.mode != mode)
                    {
                        // we don't do this actively in order to prevent deadlock/blocking from taking the stream
                        // lock within the mode lock and to let any "current" stream operations finish first
                        this.readLoopTask.ContinueWith(_ => this.DiscardContent());
                    }
                }
                else if (this.mode != mode)
                {
                    string message;
                    switch (this.mode)
                    {
                        case Mode.DiscardContents:
                            message = "The stream has been set to discard its contents, so it cannot be used in another mode";
                            break;
                        case Mode.ManualRead:
                        case Mode.BufferedManualRead:
                            message = "The stream is already being read from, so it cannot be used in another mode";
                            break;
                        case Mode.Buffer:
                            message = "The entire contents of the stream are being buffered, so it cannot be used in another mode";
                            break;
                        default:
                            throw new NotImplementedException("Unexpected mode " + mode.ToString());
                    }

                    throw new InvalidOperationException(message);
                }
            }
        }

        /// <summary>
        /// asynchronously processes the stream depending on the current mode
        /// </summary>
        private async Task ReadLoop()
        {
            Log.WriteLine("ReadLoop started for {0}", this.processStream.GetHashCode());

            var localBuffer = new byte[Constants.ByteBufferSize];
            while (true)
            {
                // safely capture the current mode
                Mode mode;
                lock (this.modeLock)
                {
                    mode = this.mode;
                }

                const int delayTimeMillis = 100;
                switch (mode)
                {
                    case Mode.ManualRead:
                        // in manual read mode, we just delay so that we can periodically check to
                        // see whether the mode has switched
                        await Task.Delay(millisecondsDelay: delayTimeMillis).ConfigureAwait(false);
                        break;
                    case Mode.BufferedManualRead:
                        // in buffered manual read mode, we read from the buffer periodically
                        // to avoid the process blocking
                        await Task.Delay(millisecondsDelay: delayTimeMillis).ConfigureAwait(false);
                        goto case Mode.Buffer;
                    case Mode.Default:
                    case Mode.Buffer:
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
                            Log.WriteLine("ReadLoop about to read for buffering from {0}", this.processStream.GetHashCode());
                            var bytesRead = await this.processStream.ReadAsync(localBuffer, offset: 0, count: localBuffer.Length).ConfigureAwait(false);
                            Log.WriteLine("ReadLoop read {0} from {1} for buffering", bytesRead, this.processStream.GetHashCode());
                            if (bytesRead == 0)
                            {
                                return; // end of stream
                            }

                            // write to the buffer
                            this.memoryStream.Write(localBuffer, offset: 0, count: bytesRead);
                        }
                        break;
                    case Mode.DiscardContents:
                        return; // handled by a continuation
                }
            }
        }

        private void DiscardContent()
        {
            // grab the stream lock and close all streams
            using (this.streamLock.AcquireAsync().Result)
            {
                if (this.memoryStream != null)
                {
                    // free the memory stream
                    this.memoryStream.Dispose();
                    this.memoryStream = null;
                }

                this.processStream.Dispose();

                // we used to read to the end, but Dispose() seems to do the same thing
                //while (true)
                //{
                //    var bytesRead = await this.processStream.ReadAsync(localBuffer, offset: 0, count: localBuffer.Length).ConfigureAwait(false);
                //    if (bytesRead == 0)
                //    {
                //        return; // end of stream
                //    }
                //}
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

            #region ---- Stream flags ----
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
            #endregion

            #region ---- Read ----
            public override int Read(byte[] buffer, int offset, int count)
            {
                // TODO keep this in sync with the other Read method

                Throw.IfNull(buffer, "buffer");
                // offset is allowed to be buffer.Length. See http://referencesource.microsoft.com/#mscorlib/system/io/memorystream.cs
                Throw.IfOutOfRange(offset, "offset", min: 0, max: buffer.Length);
                Throw.IfOutOfRange(count, "count", min: 0, max: buffer.Length - offset);
                this.EnsureManualReadMode();

                using (this.handler.streamLock.AcquireAsync().Result)
                {
                    // first read from the memory streams
                    var bytesReadFromMemoryStreams = this.ReadFromMemoryStreams(buffer, offset, count);

                    // if there are still bytes to be read, read from the underlying stream
                    if (bytesReadFromMemoryStreams < count)
                    {
                        var bytesReadFromProcessStream = this.handler.processStream.Read(buffer, offset + bytesReadFromMemoryStreams, count - bytesReadFromMemoryStreams);
                        if (bytesReadFromProcessStream == 0)
                        {
                            this.handler.taskCompletionSource.TrySetResult(true);
                        }
                        return bytesReadFromMemoryStreams + bytesReadFromProcessStream;
                    }
                    
                    // otherwise, just return the bytes from the memory stream
                    return bytesReadFromMemoryStreams;
                }
            }
            
            public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            {
                // TODO keep this in sync with the other Read method

                Throw.IfNull(buffer, "buffer");
                // offset is allowed to be buffer.Length. See http://referencesource.microsoft.com/#mscorlib/system/io/memorystream.cs
                Throw.IfOutOfRange(offset, "offset", min: 0, max: buffer.Length);
                Throw.IfOutOfRange(count, "count", min: 0, max: buffer.Length - offset);
                this.EnsureManualReadMode();

                using (await this.handler.streamLock.AcquireAsync().ConfigureAwait(false))
                {
                    // first read from the memory streams
                    var bytesReadFromMemoryStreams = this.ReadFromMemoryStreams(buffer, offset, count);

                    // if there are still bytes to be read, read from the underlying stream
                    if (bytesReadFromMemoryStreams < count)
                    {
                        var bytesReadFromProcessStream = await this.handler.processStream.ReadAsync(buffer, offset + bytesReadFromMemoryStreams, count - bytesReadFromMemoryStreams)
                            .ConfigureAwait(false);
                        if (bytesReadFromProcessStream == 0)
                        {
                            // both the buffer and the process stream are exhausted: return false
                            this.handler.taskCompletionSource.TrySetResult(true);
                        }
                        return bytesReadFromMemoryStreams + bytesReadFromProcessStream;
                    }

                    // otherwise, just return the bytes from the memory stream
                    return bytesReadFromMemoryStreams;
                }
            }

            #region ---- Begin and End Read ----
            public override IAsyncResult BeginRead(byte[] buffer, int offset, int count, AsyncCallback callback, object state)
            {
                return new BeginReadResult(this.ReadAsync(buffer, offset, count), callback, state);
            }

            public override int EndRead(IAsyncResult asyncResult)
            {
                Throw.IfNull(asyncResult, "asyncResult");
                var beginReadResult = asyncResult as BeginReadResult;
                Throw.If(beginReadResult == null, "asyncResult: the provided result was not created from a stream of this type!");

                return beginReadResult.EndRead();
            }

            private class BeginReadResult : IAsyncResult
            {
                private readonly Task<int> readAsyncTask;
                private readonly object state;

                public BeginReadResult(Task<int> readAsyncTask, AsyncCallback callback, object state)
                {
                    this.readAsyncTask = readAsyncTask;
                    this.state = state;

                    if (callback != null)
                    {
                        this.readAsyncTask.ContinueWith(t => callback(this));
                    }
                }

                public int EndRead()
                {
                    return this.readAsyncTask.Result;
                }

                object IAsyncResult.AsyncState
                {
                    get { return this.state; }
                }

                WaitHandle IAsyncResult.AsyncWaitHandle
                {
                    get { return this.readAsyncTask.As<IAsyncResult>().AsyncWaitHandle; }
                }

                bool IAsyncResult.CompletedSynchronously
                {
                    get { return this.readAsyncTask.As<IAsyncResult>().CompletedSynchronously; }
                }

                bool IAsyncResult.IsCompleted
                {
                    get { return this.readAsyncTask.IsCompleted; }
                }
            }
            #endregion

            // this is an optimization to avoid some logic on each read
            private volatile bool modeEnsured;

            private void EnsureManualReadMode()
            {
                if (!this.modeEnsured)
                {
                    lock (this.handler.modeLock)
                    {
                        var mode = this.handler.mode;
                        if (mode != Mode.ManualRead && mode != Mode.BufferedManualRead)
                        {
                            this.handler.SetMode(Mode.BufferedManualRead);
                        }
                    }
                    this.modeEnsured = true;
                }
            }

            /// <summary>
            /// Attempts to read from the memory stream buffers if the are available.
            /// Returns the number of bytes read
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

                // otherwise, we've exhausted all memory streams so return 0 bytes read
                return 0;
            }
            #endregion

            protected override void Dispose(bool disposing)
            {
                this.handler.SetMode(Mode.DiscardContents);
            }

            #region ---- Non-supported Stream methods ----
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
            #endregion
        }
        #endregion

        #region ---- ProcessStreamReader implementation ----
        private sealed class InternalReader : ProcessStreamReader
        {
            private readonly ProcessStreamHandler handler;
            private readonly StreamReader reader;

            public InternalReader(ProcessStreamHandler handler)
            {
                this.handler = handler;
                this.reader = new StreamReader(new InternalStream(handler));
            }

            #region ---- ProcessStreamReader implementation ----
            public override Stream BaseStream
            {
                get { return this.reader.BaseStream; }
            }

            public override string GetContent()
            {
                this.handler.SetMode(Mode.Buffer);
                this.handler.Task.Wait();
                using (this.handler.streamLock.AcquireAsync().Result)
                {
                    if (this.handler.memoryStream == null)
                    {
                        this.ThrowDisposed();
                    }

                    this.handler.memoryStream.Seek(0, SeekOrigin.Begin);
                    using (var reader = new StreamReader(this.handler.memoryStream))
                    {
                        return reader.ReadToEnd();
                    }
                }
            }

            public override byte[] GetContentBytes()
            {
                this.handler.SetMode(Mode.Buffer);
                this.handler.Task.Wait();
                using (this.handler.streamLock.AcquireAsync().Result)
                {
                    if (this.handler.memoryStream == null)
                    {
                        this.ThrowDisposed();
                    }

                    return this.handler.memoryStream.ToArray();
                }
            }
 
            public override IEnumerable<string> GetLines()
            {
                Mode mode;
                lock (this.handler.modeLock)
                {
                    mode = this.handler.mode;
                }

                if (mode == Mode.Buffer)
                {
                    // we can still support this in buffer mode, but it's different; we grab the full
                    // contents and read lines from that. This is unlikely to cause extra blocking, since
                    // we already called one of the other methods that sets the mode to buffer and those
                    // methods are blocking
                    using (var reader = new StringReader(this.GetContent()))
                    {
                        string line;
                        while ((line = reader.ReadLine()) != null)
                        {
                            yield return line;
                        }
                    }
                }
                else
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        yield return line;
                    }
                }
            }

            public override void Discard()
            {
                this.handler.SetMode(Mode.DiscardContents);
            }

            public override void StopBuffering()
            {
                this.handler.SetMode(Mode.ManualRead);
            }

            public override Task PipeToAsync(Stream stream, bool leaveReaderOpen, bool leaveStreamOpen)
            {
                Throw.IfNull(stream, "stream");

                return this.PipeToAsyncInternal(stream, leaveReaderOpen: leaveReaderOpen, leaveStreamOpen: leaveStreamOpen);
            }

            private async Task PipeToAsyncInternal(Stream stream, bool leaveReaderOpen, bool leaveStreamOpen)
            {
                try
                {
                    await this.BaseStream.CopyToAsync(stream).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    if (!ex.IsExpectedPipeException())
                    {
                        throw;
                    }
                }
                finally
                {
                    if (!leaveReaderOpen)
                    {
                        this.Dispose();
                    }
                    if (!leaveStreamOpen)
                    {
                        stream.Dispose();
                    }
                }
            }

            public override Task PipeToAsync(TextWriter writer, bool leaveReaderOpen, bool leaveWriterOpen)
            {
                Throw.IfNull(writer, "writer");

                return this.CopyToAsync(writer, leaveReaderOpen: leaveReaderOpen, leaveWriterOpen: leaveWriterOpen);
            }

            public override Task PipeToAsync(ICollection<string> lines, bool leaveReaderOpen)
            {
                Throw.IfNull(lines, "lines");

                return this.PipeToAsyncInternal(lines, leaveReaderOpen: leaveReaderOpen);
            }

            private async Task PipeToAsyncInternal(ICollection<string> lines, bool leaveReaderOpen)
            {
                try
                {
                    string line;
                    while ((line = await this.reader.ReadLineAsync().ConfigureAwait(false)) != null) 
                    {
                        lines.Add(line);
                    }
                }
                catch (Exception ex)
                {
                    if (!ex.IsExpectedPipeException())
                    {
                        throw;
                    }
                }
                finally 
                {
                    if (!leaveReaderOpen) 
                    {
                        this.Dispose();
                    }
                }
            }
            #endregion

            #region ---- TextReader implementation ----
            // all reader methods are overriden to call the same method on the underlying StreamReader.
            // This approach is preferable to extending StreamReader directly, since many of the async methods
            // on StreamReader are conservative and fall back to threaded asynchrony when inheritance is in play
            // (this is done to respect any overriden Read() call). This way, we get the full benefit of asynchrony.

            public override int Peek()
            {
                return this.reader.Peek();
            }

            public override int Read()
            {
                return this.reader.Read();
            }

            public override int Read(char[] buffer, int index, int count)
            {
                return this.reader.Read(buffer, index, count);
            }

            public override Task<int> ReadAsync(char[] buffer, int index, int count)
            {
                return this.reader.ReadAsync(buffer, index, count);
            }

            public override int ReadBlock(char[] buffer, int index, int count)
            {
                return this.reader.ReadBlock(buffer, index, count);
            }

            public override Task<int> ReadBlockAsync(char[] buffer, int index, int count)
            {
                return this.reader.ReadBlockAsync(buffer, index, count);
            }

            public override string ReadLine()
            {
                return this.reader.ReadLine();
            }

            public override Task<string> ReadLineAsync()
            {
                return this.reader.ReadLineAsync();
            }

            public override string ReadToEnd()
            {
                return this.reader.ReadToEnd();
            }

            public override Task<string> ReadToEndAsync()
            {
                return this.reader.ReadToEndAsync();
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    this.reader.Dispose();
                }
            }
            #endregion
        }
        #endregion
    }
}