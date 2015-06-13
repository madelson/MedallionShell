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
        private static readonly byte[] Empty = new byte[0];
        private static Task<int> CompletedZeroTask = Task.FromResult(0);

        private readonly SemaphoreSlim bytesAvailableSignal = new SemaphoreSlim(initialCount: 0, maxCount: 1);
        private readonly object @lock = new object();
        private readonly PipeInputStream input;
        private readonly PipeOutputStream output;
        
        private byte[] buffer = Empty;
        private int start, count;
        private bool writerClosed, readerClosed;
        private Task<int> readTask = CompletedZeroTask;

        public Pipe()
        {
            this.input = new PipeInputStream(this);
            this.output = new PipeOutputStream(this);
        }

        public Stream InputStream { get { return this.input; } }
        public Stream OutputStream { get { return this.output; } }

        #region ---- Writing ----
        private void Write(byte[] buffer, int offset, int count)
        {
            Throw.IfInvalidBuffer(buffer, offset, count);

            lock (this.@lock)
            {
                Throw<ObjectDisposedException>.If(this.writerClosed, "The write side of the pipe is closed");
                
                if (this.readerClosed)
                {
                    // if we can't read, just throw away the bytes since no one can observe them anyway
                    return;
                }

                // write the bytes
                this.WriteNoLock(buffer, offset, count);

                // if we actually wrote something, unblock any waiters
                if (count > 0 && this.bytesAvailableSignal.CurrentCount == 0)
                {
                    this.bytesAvailableSignal.Release();
                }
            }
        }

        private void WriteNoLock(byte[] buffer, int offset, int count)
        {
            this.EnsureCapacityNoLock(unchecked(this.count + count));

            var startToEnd = this.buffer.Length - start;
            var spaceAtEnd = startToEnd - this.count;
            if (spaceAtEnd > 0)
            {
                Buffer.BlockCopy(src: buffer, srcOffset: offset, dst: this.buffer, dstOffset: this.start + this.count, count: Math.Min(spaceAtEnd, count));
            }
            if (count > spaceAtEnd)
            {
                Buffer.BlockCopy(src: buffer, srcOffset: count - spaceAtEnd, dst: this.buffer, dstOffset: this.count - startToEnd, count: count - spaceAtEnd);
            }
            this.count += count;
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

            int newCapacity;
            if (currentCapacity < MinSize)
            {
                newCapacity = Math.Max(capacity, MinSize);
            }
            else
            {
                var doubleCapacity = 2L * currentCapacity;
                newCapacity = (int)Math.Min(doubleCapacity, int.MaxValue);
            }

            var newBuffer = new byte[newCapacity];
            var startToEndCount = this.buffer.Length - start;
            Buffer.BlockCopy(src: this.buffer, srcOffset: this.start, dst: newBuffer, dstOffset: 0, count: startToEndCount);
            Buffer.BlockCopy(src: this.buffer, srcOffset: 0, dst: newBuffer, dstOffset: startToEndCount, count: count - startToEndCount);
            this.buffer = newBuffer;
            this.start = 0;
        }

        private void CloseWriteSide()
        {
            lock (this.@lock)
            {
                if (this.writerClosed)
                {
                    return;
                }

                this.writerClosed = true;

                // unblock any waiters, since nothing more is coming
                if (this.bytesAvailableSignal.CurrentCount == 0)
                {
                    this.bytesAvailableSignal.Release();
                }

                if (this.readerClosed)
                {
                    this.CleanupNoLock();
                }
            }
        }
        #endregion

        #region ---- Reading ----
        private Task<int> ReadAsync(byte[] buffer, int offset, int count, TimeSpan timeout, CancellationToken cancellationToken)
        {
            Throw.IfInvalidBuffer(buffer, offset, count);

            // if we didn't want to write anything, return immediately
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
                    // respect cancellation in the sync flow by just returning a 
                    // canceled task when appropriate
                    if (cancellationToken.IsCancellationRequested)
                    {
                        var taskCompletionSource = new TaskCompletionSource<int>();
                        taskCompletionSource.SetCanceled();
                        return taskCompletionSource.Task;
                    }

                    var result = Task.FromResult(this.ReadNoLock(buffer, offset, count));
                    if (this.count == 0)
                    {
                        // if we consumed all the bytes, unset the signal
                        this.bytesAvailableSignal.Wait();
                    }
                    return result;
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
                var result = this.ReadNoLock(buffer, offset, count);

                // on the way out, signal if there are still bytes left
                if (this.count > 0 && this.bytesAvailableSignal.CurrentCount == 0)
                {
                    this.bytesAvailableSignal.Release();
                }

                return result;
            }
        }

        private int ReadNoLock(byte[] buffer, int offset, int count)
        {
            var countToRead = Math.Min(this.count, count);

            var startToEnd = this.buffer.Length - this.start;
            var newStart = this.start;
            if (startToEnd > 0)
            {
                int countToReadFromEnd;
                if (startToEnd > countToRead)
                {
                    countToReadFromEnd = countToRead;
                    newStart += countToRead;
                }
                else
                {
                    countToReadFromEnd = startToEnd;
                    newStart = 0;
                }
                Buffer.BlockCopy(src: this.buffer, srcOffset: this.start, dst: buffer, dstOffset: offset, count: countToReadFromEnd);
            }
            if (startToEnd < countToRead)
            {
                var countToReadFromBeginning = countToRead - startToEnd;
                newStart += countToReadFromBeginning;
                Buffer.BlockCopy(src: this.buffer, srcOffset: 0, dst: buffer, dstOffset: startToEnd, count: countToReadFromBeginning);
            }

            this.start = newStart;
            this.count -= countToRead;
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
                    }
                    , state: this
                );
            }
        }

        private void InternalCloseReadSideNoLock()
        {
            this.readerClosed = true;
            if (this.writerClosed)
            {
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
                // ok since we don't have true async write
                return base.BeginWrite(buffer, offset, count, callback, state);
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
                base.EndWrite(asyncResult); // no true async
            }

            public override void Flush()
            {
                // no-op, since we have no buffer
            }

            public override Task FlushAsync(CancellationToken cancellationToken)
            {
                // we have no true async support
                return base.FlushAsync(cancellationToken);
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
                this.pipe.Write(buffer, offset, count);
            }

            public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            {
                // no true async writes
                return base.WriteAsync(buffer, offset, count, cancellationToken);
            }

            public override void WriteByte(byte value)
            {
                // the base implementation is inefficient, but I don't think we care
                base.WriteByte(value);
            }

            public override int WriteTimeout
            {
                get { throw Throw.NotSupported(); }
                set { throw Throw.NotSupported(); }
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
    }
}
