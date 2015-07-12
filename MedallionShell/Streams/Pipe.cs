using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Medallion.Shell.Streams
{
    internal sealed class Pipe
    {
        // From MemoryStream (see http://referencesource.microsoft.com/#mscorlib/system/io/memorystream.cs,1416df83d2368912)
        private const int MinSize = 256;
        /// <summary>
        /// The maximum size at which the pipe will be left empty. Using 2 * <see cref="Constants.ByteBufferSize"/>
        /// helps prevent thrashing if data is being pushed through the pipe at that rate
        /// </summary>
        private const int MaxStableSize = 2 * Constants.ByteBufferSize;
        private static readonly byte[] Empty = new byte[0];
        private static Task<int> CompletedZeroTask = Task.FromResult(0);

        private readonly SemaphoreSlim bytesAvailableSignal = new SemaphoreSlim(initialCount: 0, maxCount: 1);
        private readonly object @lock = new object();
        private readonly PipeInputStream input;
        private readonly PipeOutputStream output;
        
        private byte[] buffer = Empty;
        private int start, count;
        private bool writerClosed, readerClosed;
        private SemaphoreSlim spaceAvailableSignal;
        private Task<int> readTask = CompletedZeroTask;
        private Task writeTask = CompletedZeroTask;

        public Pipe()
        {
            this.input = new PipeInputStream(this);
            this.output = new PipeOutputStream(this);
        }

        public Stream InputStream { get { return this.input; } }
        public Stream OutputStream { get { return this.output; } }

        #region ---- Signals ----
        public void SetFixedLength()
        {
            lock (this.@lock)
            {
                if (this.spaceAvailableSignal == null
                    && !this.readerClosed
                    && !this.writerClosed)
                {
                    this.spaceAvailableSignal = new SemaphoreSlim(
                        initialCount: this.GetSpaceAvailableNoLock() > 0 ? 1 : 0,
                        maxCount: 1
                    );
                }
            }
        }

        private int GetSpaceAvailableNoLock()
        {
            return Math.Max(this.buffer.Length, MaxStableSize) - this.count;
        }

        /// <summary>
        /// MA: I used to have the signals updated in various ways and in various places
        /// throughout the code. Now I have just one function that sets both signals to the correct
        /// values. This is called from <see cref="ReadNoLock"/>, <see cref="WriteNoLock"/>, 
        /// <see cref="InternalCloseReadSideNoLock"/>, and <see cref="InternalCloseWriteSideNoLock"/>.
        /// 
        /// While it may seem like this does extra work, nearly all cases are necessary. For example, we used
        /// to say "signal bytes available if count > 0" at the end of <see cref="WriteNoLock"/>. The problem is
        /// that we could have the following sequence of operations:
        /// 1. <see cref="ReadNoLockAsync"/> blocks on <see cref="bytesAvailableSignal"/>
        /// 2. <see cref="WriteNoLock"/> writes and signals
        /// 3. <see cref="ReadNoLockAsync"/> wakes up
        /// 4. Another <see cref="WriteNoLock"/> call writes and re-signals
        /// 5. <see cref="ReadNoLockAsync"/> reads ALL content and returns, leaving <see cref="bytesAvailableSignal"/> signaled (invalid)
        /// 
        /// This new implementation avoids this because the <see cref="ReadNoLock"/> call inside <see cref="ReadNoLockAsync"/> will
        /// properly unsignal after it consumes ALL the contents
        /// </summary>
        private void UpdateSignalsNoLock()
        {
            // update bytes available
            switch (this.bytesAvailableSignal.CurrentCount) 
            {
                case 0:
                    if (this.count > 0 || this.writerClosed)
                    {
                        this.bytesAvailableSignal.Release();
                    }
                    break;
                case 1:
                    if (this.count == 0 && !this.writerClosed)
                    {
                        this.bytesAvailableSignal.Wait();
                    }
                    break;
                default:
                    throw new InvalidOperationException("Should never get here");
            }

            // update space available
            if (this.spaceAvailableSignal != null)
            {
                switch (this.spaceAvailableSignal.CurrentCount) 
                {
                    case 0:
                        if (this.readerClosed || this.GetSpaceAvailableNoLock() > 0)
                        {
                            this.spaceAvailableSignal.Release();
                        }
                        break;
                    case 1:
                        if (!this.readerClosed && this.GetSpaceAvailableNoLock() == 0)
                        {
                            this.spaceAvailableSignal.Wait();
                        }
                        break;
                    default:
                        throw new InvalidOperationException("Should never get here");
                }
            }
        }
        #endregion

        #region ---- Writing ----
        private Task WriteAsync(byte[] buffer, int offset, int count, TimeSpan timeout, CancellationToken cancellationToken)
        {
            Throw.IfInvalidBuffer(buffer, offset, count);

            // always respect cancellation, even in the sync flow
            if (cancellationToken.IsCancellationRequested)
            {
                return CreateCanceledTask();
            }

            if (count == 0)
            {
                // if we didn't want to write anything, return immediately
                return CompletedZeroTask;
            }

            lock (this.@lock)
            {
                Throw<ObjectDisposedException>.If(this.writerClosed, "The write side of the pipe is closed");
                Throw<InvalidOperationException>.If(!this.writeTask.IsCompleted, "Concurrent writes are not allowed");

                if (this.readerClosed)
                {
                    // if we can't read, just throw away the bytes since no one can observe them anyway
                    return CompletedZeroTask;
                }

                if (this.spaceAvailableSignal == null
                    || this.GetSpaceAvailableNoLock() >= count)
                {
                    // if we're not limited by space, just write and return
                    this.WriteNoLock(buffer, offset, count);

                    return CompletedZeroTask;
                }

                // otherwise, create and return an async write task
                return this.writeTask = this.WriteNoLockAsync(buffer, offset, count, timeout, cancellationToken);
            }
        }

        private async Task WriteNoLockAsync(byte[] buffer, int offset, int count, TimeSpan timeout, CancellationToken cancellationToken)
        {
            var remainingCount = count;
            do
            {
                // MA: we only use the timeout/token on the first time through, to avoid doing part of the write. This way, it's all or nothing
                CancellationToken cancellationTokenToUse;
                TimeSpan timeoutToUse;
                if (remainingCount == count)
                {
                    timeoutToUse = timeout;
                    cancellationTokenToUse = cancellationToken;
                }
                else
                {
                    timeoutToUse = Timeout.InfiniteTimeSpan;
                    cancellationTokenToUse = CancellationToken.None;
                }

                // acquire the semaphore
                var acquired = await this.spaceAvailableSignal.WaitAsync(timeoutToUse, cancellationTokenToUse).ConfigureAwait(false);
                if (!acquired)
                {
                    throw new TimeoutException("Timed out writing to the pipe");
                }

                // we need to reacquire the lock after the await since we might have switched threads
                lock (this.@lock)
                {
                    if (this.readerClosed)
                    {
                        // if the read side is gone, we're instantly done
                        remainingCount = 0;
                    }
                    else
                    {
                        var countToWrite = Math.Min(this.GetSpaceAvailableNoLock(), remainingCount);
                        this.WriteNoLock(buffer, offset + (count - remainingCount), countToWrite);

                        remainingCount -= countToWrite;
                    }
                }
            } while (remainingCount > 0);
        }

        private void WriteNoLock(byte[] buffer, int offset, int count)
        {
            if (count <= 0)
            {
                throw new InvalidOperationException("Sanity check: WriteNoLock requires positive count");
            }

            this.EnsureCapacityNoLock(unchecked(this.count + count));

            var writeStart = (this.start + this.count) % this.buffer.Length;
            var writeStartToEndCount = Math.Min(this.buffer.Length - writeStart, count);
            Buffer.BlockCopy(src: buffer, srcOffset: offset, dst: this.buffer, dstOffset: writeStart, count: writeStartToEndCount);
            Buffer.BlockCopy(src: buffer, srcOffset: offset + writeStartToEndCount, dst: this.buffer, dstOffset: 0, count: count - writeStartToEndCount);
            this.count += count;

            this.UpdateSignalsNoLock();
        }

        private void EnsureCapacityNoLock(int capacity)
        {
            if (capacity < 0)
            {
                throw new IOException("Pipe stream is too long");
            }

            var currentCapacity = this.buffer.Length;
            if (capacity <= currentCapacity)
            {
                return;
            }

            if (this.spaceAvailableSignal != null
                && capacity > MaxStableSize)
            {
                throw new InvalidOperationException("Sanity check: pipe should not attempt to expand beyond stable size in fixed length mode");
            }

            int newCapacity;
            if (currentCapacity < MinSize)
            {
                newCapacity = Math.Max(capacity, MinSize);
            }
            else
            {
                var doubleCapacity = 2L * currentCapacity;
                newCapacity = capacity >= doubleCapacity 
                    ? capacity
                    : (int)Math.Min(doubleCapacity, int.MaxValue);
            }

            var newBuffer = new byte[newCapacity];
            var startToEndCount = Math.Min(this.buffer.Length - this.start, this.count);
            Buffer.BlockCopy(src: this.buffer, srcOffset: this.start, dst: newBuffer, dstOffset: 0, count: startToEndCount);
            Buffer.BlockCopy(src: this.buffer, srcOffset: 0, dst: newBuffer, dstOffset: startToEndCount, count: this.count - startToEndCount);
            this.buffer = newBuffer;
            this.start = 0;
        }

        private void CloseWriteSide()
        {
            lock (this.@lock)
            {
                // no-op if we're already closed
                if (this.writerClosed)
                {
                    return;
                }

                // if we don't have an active write task, close now
                if (this.writeTask.IsCompleted)
                {
                    this.InternalCloseWriteSideNoLock();
                    return;
                }

                // otherwise, close as a continuation on the write task
                this.writeTask = this.writeTask.ContinueWith(
                    (t, state) =>
                    {
                        var @this = (Pipe)state;
                        lock (@this.@lock)
                        {
                            @this.InternalCloseWriteSideNoLock();
                        }
                    }
                    , state: this
                );
            }
        }

        private void InternalCloseWriteSideNoLock()
        {
            this.writerClosed = true;
            this.UpdateSignalsNoLock();
            if (this.readerClosed)
            {
                // if both sides are now closed, cleanup
                this.CleanupNoLock();
            }
        }
        #endregion

        #region ---- Reading ----
        private Task<int> ReadAsync(byte[] buffer, int offset, int count, TimeSpan timeout, CancellationToken cancellationToken)
        {
            Throw.IfInvalidBuffer(buffer, offset, count);

            // always respect cancellation, even in the sync flow
            if (cancellationToken.IsCancellationRequested)
            {
                return CreateCanceledTask();
            }

            // if we didn't want to read anything, return immediately
            if (count == 0) 
            { 
                return CompletedZeroTask; 
            }

            lock (this.@lock)
            {
                Throw<ObjectDisposedException>.If(this.readerClosed, "The read side of the pipe is closed");
                Throw<InvalidOperationException>.If(!this.readTask.IsCompleted, "Concurrent reads are not allowed");

                // if we have bytes, read them and return synchronously
                if (this.count > 0)
                {
                    return Task.FromResult(this.ReadNoLock(buffer, offset, count));
                }

                // if we don't have bytes and no more are coming, return 0
                if (this.writerClosed)
                {
                    return CompletedZeroTask;
                }

                // otherwise, create and return an async read task
                return this.readTask = this.ReadNoLockAsync(buffer, offset, count, timeout, cancellationToken);
            }
        }

        private async Task<int> ReadNoLockAsync(byte[] buffer, int offset, int count, TimeSpan timeout, CancellationToken cancellationToken)
        {
            var acquired = await this.bytesAvailableSignal.WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
            if (!acquired)
            {
                throw new TimeoutException("Timed out reading from the pipe");
            }

            // we need to reacquire the lock after the await since we might have switched threads
            lock (this.@lock)
            {
                return this.ReadNoLock(buffer, offset, count);
            }
        }

        private int ReadNoLock(byte[] buffer, int offset, int count)
        {
            var countToRead = Math.Min(this.count, count);

            var bytesRead = 0;
            while (bytesRead < countToRead)
            {
                var bytesToRead = Math.Min(countToRead - bytesRead, this.buffer.Length - this.start);
                Buffer.BlockCopy(src: this.buffer, srcOffset: this.start, dst: buffer, dstOffset: offset + bytesRead, count: bytesToRead);
                bytesRead += bytesToRead;
                this.start = (this.start + bytesToRead) % this.buffer.Length;
            }
            this.count -= countToRead;

            // ensure that an empty pipe never stays above the max stable size
            if (this.count == 0
                && this.buffer.Length > MaxStableSize)
            {
                this.start = 0;
                this.buffer = new byte[MaxStableSize];
            }

            this.UpdateSignalsNoLock();

            return countToRead;
        }

        private void CloseReadSide()
        {
            lock (this.@lock)
            {
                // no-op if we're already closed
                if (this.readerClosed)
                {
                    return;
                }

                // if we don't have an active read task, close now
                if (this.readTask.IsCompleted)
                {
                    this.InternalCloseReadSideNoLock();
                    return;
                }

                // otherwise, close as a continuation on the read task
                this.readTask = this.readTask.ContinueWith(
                    (t, state) =>
                    {
                        var @this = (Pipe)state;
                        lock (@this.@lock)
                        {
                            @this.InternalCloseReadSideNoLock();
                        }
                        return -1;
                    }, 
                    state: this
                );
            }
        }

        private void InternalCloseReadSideNoLock()
        {
            this.readerClosed = true;
            this.UpdateSignalsNoLock();
            if (this.writerClosed)
            {
                // if both sides are now closed, cleanup
                this.CleanupNoLock();
            }
        }
        #endregion

        #region ---- Dispose ----
        private void CleanupNoLock()
        {
            this.buffer = null;
            this.readTask = null;
            this.bytesAvailableSignal.Dispose();
            if (this.spaceAvailableSignal != null) 
            {
                this.spaceAvailableSignal.Dispose();
            }
        }
        #endregion

        #region ---- Cancellation ----
        private static Task<int> CreateCanceledTask()
        {
            var taskCompletionSource = new TaskCompletionSource<int>();
            taskCompletionSource.SetCanceled();
            return taskCompletionSource.Task;
        }
        #endregion

        #region ---- Input Stream ----
        private sealed class PipeInputStream : Stream
        {
            private readonly Pipe pipe;

            public PipeInputStream(Pipe pipe) { this.pipe = pipe; }

            public override IAsyncResult BeginRead(byte[] buffer, int offset, int count, AsyncCallback callback, object state)
            {
                throw WriteOnly();
            }

            public override IAsyncResult BeginWrite(byte[] buffer, int offset, int count, AsyncCallback callback, object state)
            {
                // according to the docs, the callback is optional
                var writeTask = this.WriteAsync(buffer, offset, count, CancellationToken.None);
                var writeResult = new AsyncWriteResult(state, writeTask, this);
                if (callback != null)
                {
                    writeTask.ContinueWith(_ => callback(writeResult));
                }
                return writeResult;
            }

            private sealed class AsyncWriteResult : IAsyncResult
            {
                private readonly object state;
                private readonly Task writeTask;
                private readonly PipeInputStream stream;

                public AsyncWriteResult(object state, Task writeTask, PipeInputStream stream)
                {
                    this.state = state;
                    this.writeTask = writeTask;
                    this.stream = stream;
                }

                public Task WriteTask { get { return this.writeTask; } }

                public Stream Stream { get { return this.stream; } }

                object IAsyncResult.AsyncState { get { return this.state; } }

                WaitHandle IAsyncResult.AsyncWaitHandle { get { return this.writeTask.As<IAsyncResult>().AsyncWaitHandle; } }

                bool IAsyncResult.CompletedSynchronously { get { return this.writeTask.As<IAsyncResult>().CompletedSynchronously; } }

                bool IAsyncResult.IsCompleted { get { return this.writeTask.IsCompleted; } }
            }

            public override bool CanRead { get { return false; } }

            public override bool CanSeek { get { return false; } }

            public override bool CanTimeout { get { return false; } }

            public override bool CanWrite { get { return true; } }

            public override void Close()
            {
                base.Close(); // calls Dispose(true)
            }

            public override Task CopyToAsync(Stream destination, int bufferSize, CancellationToken cancellationToken)
            {
                throw WriteOnly();
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    this.pipe.CloseWriteSide();
                }
            }

            public override int EndRead(IAsyncResult asyncResult)
            {
                throw WriteOnly();
            }

            public override void EndWrite(IAsyncResult asyncResult)
            {
                Throw.IfNull(asyncResult, "asyncResult");
                var writeResult = asyncResult as AsyncWriteResult;
                Throw.If(writeResult == null || writeResult.Stream != this, "asyncResult: must be created by this stream's BeginWrite method");
                writeResult.WriteTask.Wait();
            }

            public override void Flush()
            {
                // no-op, since we are just a buffer
            }

            public override Task FlushAsync(CancellationToken cancellationToken)
            {
                // no-op since we are just a buffer
                return CompletedZeroTask;
            }

            public override long Length { get { throw Throw.NotSupported(); } }

            public override long Position
            {
                get { throw Throw.NotSupported(); }
                set { throw Throw.NotSupported(); }
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                throw WriteOnly();
            }

            public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            {
                throw WriteOnly();
            }

            public override int ReadByte()
            {
                throw WriteOnly();
            }

            public override int ReadTimeout
            {
                get { throw WriteOnly(); }
                set { throw WriteOnly(); }
            }

            public override long Seek(long offset, SeekOrigin origin)
            {
                throw Throw.NotSupported();
            }

            public override void SetLength(long value)
            {
                throw Throw.NotSupported();
            }

            public override void Write(byte[] buffer, int offset, int count)
            {
                try
                {
                    this.pipe.WriteAsync(buffer, offset, count, TimeSpan.FromMilliseconds(this.WriteTimeout), CancellationToken.None).Wait();
                }
                catch (AggregateException ex)
                {
                    // unwrap aggregate if we can
                    if (ex.InnerExceptions.Count == 1)
                    {
                        ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
                    }

                    throw;
                }
            }

            public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            {
                return this.pipe.WriteAsync(buffer, offset, count, TimeSpan.FromMilliseconds(this.WriteTimeout), cancellationToken);
            }

            public override void WriteByte(byte value)
            {
                // the base implementation is inefficient, but I don't think we care
                base.WriteByte(value);
            }

            private int writeTimeout = Timeout.Infinite;

            public override int WriteTimeout
            {
                get { return this.writeTimeout; }
                set 
                {
                    if (value != Timeout.Infinite)
                    {
                        Throw.IfOutOfRange(value, "WriteTimeout", min: 0);
                    }
                    this.writeTimeout = value;
                }
            }

            private static NotSupportedException WriteOnly([CallerMemberName] string memberName = null)
            {
                throw new NotSupportedException(memberName + ": the stream is write only");
            }
        }
        #endregion

        #region ---- Output Stream ----
        private sealed class PipeOutputStream : Stream
        {
            private readonly Pipe pipe;

            public PipeOutputStream(Pipe pipe) { this.pipe = pipe; }

            public override IAsyncResult BeginRead(byte[] buffer, int offset, int count, AsyncCallback callback, object state)
            {
                // according to the docs, the callback is optional
                var readTask = this.ReadAsync(buffer, offset, count, CancellationToken.None);
                var readResult = new AsyncReadResult(state, readTask, this);
                if (callback != null)
                {
                    readTask.ContinueWith(_ => callback(readResult));
                }
                return readResult;
            }

            private sealed class AsyncReadResult : IAsyncResult
            {
                private readonly object state;
                private readonly Task<int> readTask;
                private readonly PipeOutputStream stream;

                public AsyncReadResult(object state, Task<int> readTask, PipeOutputStream stream)
                {
                    this.state = state;
                    this.readTask = readTask;
                    this.stream = stream;
                }

                public Task<int> ReadTask { get { return this.readTask; } }

                public Stream Stream { get { return this.stream; } }

                object IAsyncResult.AsyncState { get { return this.state; } }

                WaitHandle IAsyncResult.AsyncWaitHandle { get { return this.readTask.As<IAsyncResult>().AsyncWaitHandle; } }

                bool IAsyncResult.CompletedSynchronously { get { return this.readTask.As<IAsyncResult>().CompletedSynchronously; } }

                bool IAsyncResult.IsCompleted { get { return this.readTask.IsCompleted; } }
            }

            public override IAsyncResult BeginWrite(byte[] buffer, int offset, int count, AsyncCallback callback, object state)
            {
                throw ReadOnly();
            }

            public override bool CanRead { get { return true; } }

            public override bool CanSeek { get { return false; } }

            public override bool CanTimeout { get { return true; } }

            public override bool CanWrite { get { return false; } }

            public override void Close()
            {
                base.Close(); // calls Dispose(true)
            }

            public override Task CopyToAsync(Stream destination, int bufferSize, CancellationToken cancellationToken)
            {
                // the base implementation is reasonable
                return base.CopyToAsync(destination, bufferSize, cancellationToken);
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    this.pipe.CloseReadSide();
                }
            }

            public override int EndRead(IAsyncResult asyncResult)
            {
                Throw.IfNull(asyncResult, "asyncResult");
                var readResult = asyncResult as AsyncReadResult;
                Throw.If(readResult == null || readResult.Stream != this, "asyncResult: must be created by this stream's BeginRead method");

                return readResult.ReadTask.Result;
            }

            public override void EndWrite(IAsyncResult asyncResult)
            {
                throw ReadOnly();
            }

            public override void Flush()
            {
                throw ReadOnly();
            }

            public override Task FlushAsync(CancellationToken cancellationToken)
            {
                throw ReadOnly();
            }

            public override long Length { get { throw Throw.NotSupported(); } }

            public override long Position
            {
                get { throw Throw.NotSupported(); }
                set { throw Throw.NotSupported(); }
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                try
                {
                    return this.pipe.ReadAsync(buffer, offset, count, TimeSpan.FromMilliseconds(this.ReadTimeout), CancellationToken.None).Result;
                }
                catch (AggregateException ex)
                {
                    // unwrap aggregate if we can
                    if (ex.InnerExceptions.Count == 1)
                    {
                        ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
                    }

                    throw;
                }
            }

            public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            {
                return this.pipe.ReadAsync(buffer, offset, count, TimeSpan.FromMilliseconds(this.ReadTimeout), cancellationToken);
            }

            public override int ReadByte()
            {
                // this is inefficient, but I think that's ok
                return base.ReadByte();
            }

            private int readTimeout = Timeout.Infinite;

            public override int ReadTimeout
            {
                get { return this.readTimeout; }
                set
                {
                    if (value != Timeout.Infinite)
                    {
                        Throw.IfOutOfRange(value, "ReadTimeout", min: 0);
                    }
                    this.readTimeout = value;
                }
            }

            public override long Seek(long offset, SeekOrigin origin)
            {
                throw Throw.NotSupported();
            }

            public override void SetLength(long value)
            {
                throw Throw.NotSupported();
            }

            public override void Write(byte[] buffer, int offset, int count)
            {
                throw ReadOnly();
            }

            public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            {
                throw ReadOnly();
            }

            public override void WriteByte(byte value)
            {
                throw ReadOnly();
            }

            public override int WriteTimeout
            {
                get { throw ReadOnly(); }
                set { throw ReadOnly(); }
            }

            private static NotSupportedException ReadOnly([CallerMemberName] string memberName = null)
            {
                throw new NotSupportedException(memberName + ": the stream is read only");
            }
        }
        #endregion

        //private static readonly StringBuilder log = new StringBuilder();
        //private static int nextId;
        //private int id = Interlocked.Increment(ref nextId);
        //private void Log(string format, params object[] args)
        //{
        //    var formatted = this.id + ": " + string.Format(format, args);
        //    lock (log)
        //    {
        //        log.AppendLine(formatted);
        //    }
        //}
        //public static void PrintLog()
        //{
        //    Console.WriteLine("**** LOG ****");
        //    lock (log)
        //    {
        //        Console.WriteLine(log.ToString());
        //    }
        //}
    }
}
