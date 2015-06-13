using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
        
        private byte[] buffer = Empty;
        private int start, count;
        private bool writerClosed, readerClosed;
        private Task<int> readTask = CompletedZeroTask;

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
            var spaceAtEnd = startToEnd - count;
            if (spaceAtEnd > 0)
            {
                Buffer.BlockCopy(src: buffer, srcOffset: offset, dst: this.buffer, dstOffset: startToEnd + this.count, count: Math.Min(spaceAtEnd, count));
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
                this.writerClosed = true;
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
            if (startToEnd > 0)
            {
                int countToReadFromEnd;
                if (startToEnd > countToRead)
                {
                    countToReadFromEnd = countToRead;
                    this.start += countToRead;
                }
                else
                {
                    countToReadFromEnd = startToEnd;
                    this.start = 0;
                }
                Buffer.BlockCopy(src: this.buffer, srcOffset: this.start, dst: buffer, dstOffset: offset, count: countToReadFromEnd);

            }
            if (startToEnd < countToRead)
            {
                var countToReadFromBeginning = countToRead - startToEnd;
                this.start += countToReadFromBeginning;
                Buffer.BlockCopy(src: this.buffer, srcOffset: 0, dst: buffer, dstOffset: startToEnd, count: countToReadFromBeginning);
            }

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
        private sealed class InputStream : Stream
        {
            public override IAsyncResult BeginRead(byte[] buffer, int offset, int count, AsyncCallback callback, object state)
            {
                return base.BeginRead(buffer, offset, count, callback, state);
            }

            public override IAsyncResult BeginWrite(byte[] buffer, int offset, int count, AsyncCallback callback, object state)
            {
                return base.BeginWrite(buffer, offset, count, callback, state);
            }

            public override bool CanRead
            {
                get { throw new NotImplementedException(); }
            }

            public override bool CanSeek
            {
                get { throw new NotImplementedException(); }
            }

            public override bool CanTimeout
            {
                get
                {
                    return base.CanTimeout;
                }
            }

            public override bool CanWrite
            {
                get { throw new NotImplementedException(); }
            }

            public override void Close()
            {
                base.Close();
            }

            public override Task CopyToAsync(Stream destination, int bufferSize, CancellationToken cancellationToken)
            {
                return base.CopyToAsync(destination, bufferSize, cancellationToken);
            }

            protected override void Dispose(bool disposing)
            {
                base.Dispose(disposing);
            }

            public override int EndRead(IAsyncResult asyncResult)
            {
                return base.EndRead(asyncResult);
            }

            public override void EndWrite(IAsyncResult asyncResult)
            {
                base.EndWrite(asyncResult);
            }

            public override void Flush()
            {
                throw new NotImplementedException();
            }

            public override Task FlushAsync(CancellationToken cancellationToken)
            {
                return base.FlushAsync(cancellationToken);
            }

            public override long Length
            {
                get { throw new NotImplementedException(); }
            }

            public override long Position
            {
                get
                {
                    throw new NotImplementedException();
                }
                set
                {
                    throw new NotImplementedException();
                }
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                throw new NotImplementedException();
            }

            public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            {
                return base.ReadAsync(buffer, offset, count, cancellationToken);
            }

            public override int ReadByte()
            {
                return base.ReadByte();
            }

            public override int ReadTimeout
            {
                get
                {
                    return base.ReadTimeout;
                }
                set
                {
                    base.ReadTimeout = value;
                }
            }

            public override long Seek(long offset, SeekOrigin origin)
            {
                throw new NotImplementedException();
            }

            public override void SetLength(long value)
            {
                throw new NotImplementedException();
            }

            public override void Write(byte[] buffer, int offset, int count)
            {
                throw new NotImplementedException();
            }

            public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            {
                return base.WriteAsync(buffer, offset, count, cancellationToken);
            }

            public override void WriteByte(byte value)
            {
                base.WriteByte(value);
            }

            public override int WriteTimeout
            {
                get
                {
                    return base.WriteTimeout;
                }
                set
                {
                    base.WriteTimeout = value;
                }
            }
        }
        #endregion

        #region ---- Output Stream ----
        private sealed class OutputSteram : Stream
        {
            public override IAsyncResult BeginRead(byte[] buffer, int offset, int count, AsyncCallback callback, object state)
            {
                return base.BeginRead(buffer, offset, count, callback, state);
            }

            public override IAsyncResult BeginWrite(byte[] buffer, int offset, int count, AsyncCallback callback, object state)
            {
                return base.BeginWrite(buffer, offset, count, callback, state);
            }

            public override bool CanRead
            {
                get { throw new NotImplementedException(); }
            }

            public override bool CanSeek
            {
                get { throw new NotImplementedException(); }
            }

            public override bool CanTimeout
            {
                get
                {
                    return base.CanTimeout;
                }
            }

            public override bool CanWrite
            {
                get { throw new NotImplementedException(); }
            }

            public override void Close()
            {
                base.Close();
            }

            public override Task CopyToAsync(Stream destination, int bufferSize, CancellationToken cancellationToken)
            {
                return base.CopyToAsync(destination, bufferSize, cancellationToken);
            }

            protected override void Dispose(bool disposing)
            {
                base.Dispose(disposing);
            }

            public override int EndRead(IAsyncResult asyncResult)
            {
                return base.EndRead(asyncResult);
            }

            public override void EndWrite(IAsyncResult asyncResult)
            {
                base.EndWrite(asyncResult);
            }

            public override void Flush()
            {
                throw new NotImplementedException();
            }

            public override Task FlushAsync(CancellationToken cancellationToken)
            {
                return base.FlushAsync(cancellationToken);
            }

            public override long Length
            {
                get { throw new NotImplementedException(); }
            }

            public override long Position
            {
                get
                {
                    throw new NotImplementedException();
                }
                set
                {
                    throw new NotImplementedException();
                }
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                throw new NotImplementedException();
            }

            public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            {
                return base.ReadAsync(buffer, offset, count, cancellationToken);
            }

            public override int ReadByte()
            {
                return base.ReadByte();
            }

            public override int ReadTimeout
            {
                get
                {
                    return base.ReadTimeout;
                }
                set
                {
                    base.ReadTimeout = value;
                }
            }

            public override long Seek(long offset, SeekOrigin origin)
            {
                throw new NotImplementedException();
            }

            public override void SetLength(long value)
            {
                throw new NotImplementedException();
            }

            public override void Write(byte[] buffer, int offset, int count)
            {
                throw new NotImplementedException();
            }

            public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            {
                return base.WriteAsync(buffer, offset, count, cancellationToken);
            }

            public override void WriteByte(byte value)
            {
                base.WriteByte(value);
            }

            public override int WriteTimeout
            {
                get
                {
                    return base.WriteTimeout;
                }
                set
                {
                    base.WriteTimeout = value;
                }
            }
        }
        #endregion
    }
}
