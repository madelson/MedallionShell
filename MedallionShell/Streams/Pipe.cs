﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
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
        private static readonly Task<int> CompletedZeroTask = Task.FromResult(0);

        private readonly AsyncManualResetEvent bytesAvailableSignal = new(initialState: false);
        private readonly object @lock = new();
        private readonly PipeInputStream input;
        private readonly PipeOutputStream output;

        private byte[] buffer = Shims.EmptyArray<byte>();
        private int start, count;
        private bool writerClosed, readerClosed;
        private AsyncManualResetEvent? spaceAvailableSignal;
        private Task<int> readTask = CompletedZeroTask;
        private Task writeTask = CompletedZeroTask;

        public Pipe()
        {
            this.input = new PipeInputStream(this);
            this.output = new PipeOutputStream(this);
        }

        public Stream InputStream => this.input;
        public Stream OutputStream => this.output;

        #region ---- Signals ----
        public void SetFixedLength()
        {
            lock (this.@lock)
            {
                if (this.spaceAvailableSignal is null
                    && !this.readerClosed
                    && !this.writerClosed)
                {
                    this.spaceAvailableSignal = new(initialState: this.GetSpaceAvailableNoLock() > 0);
                }
            }
        }

        private int GetSpaceAvailableNoLock()
        {
            Debug.Assert(Monitor.IsEntered(this.@lock), "NoLock method");

            return Math.Max(this.buffer.Length, MaxStableSize) - this.count;
        }

        private void AssertSignalInvariantsNoLock()
        {
            Debug.Assert(Monitor.IsEntered(this.@lock), "NoLock method");
            Debug.Assert(this.bytesAvailableSignal.IsSet == (this.writerClosed || this.count > 0), nameof(this.bytesAvailableSignal));
            Debug.Assert(
                this.spaceAvailableSignal is null
                    || (this.spaceAvailableSignal.IsSet == (this.readerClosed || this.GetSpaceAvailableNoLock() > 0)),
                nameof(this.spaceAvailableSignal)
            );
        }

        private static void SetSignal(AsyncManualResetEvent signal)
        {
            Debug.Assert(!signal.IsSet, "should be unset");
            signal.Set();
        }

        private static void ResetSignal(AsyncManualResetEvent signal)
        {
            Debug.Assert(signal.IsSet, "should be set");
            signal.Reset();
        }
        #endregion

        #region ---- Writing ----
        private Task WriteAsync(byte[] buffer, int offset, int count, TimeSpan timeout, CancellationToken cancellationToken)
        {
            Throw.IfInvalidBuffer(buffer, offset, count);

            // always respect cancellation, even in the sync flow
            if (cancellationToken.IsCancellationRequested)
            {
                return Shims.CanceledTask<bool>(cancellationToken);
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

                if (this.spaceAvailableSignal is null
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
            Debug.Assert(Monitor.IsEntered(this.@lock), "NoLock method");

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
                var acquired = await this.spaceAvailableSignal!.WaitAsync(timeoutToUse, cancellationTokenToUse).ConfigureAwait(false);
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
            }
            while (remainingCount > 0);
        }

        private void WriteNoLock(byte[] buffer, int offset, int count)
        {
            Debug.Assert(Monitor.IsEntered(this.@lock), "NoLock method");
            Debug.Assert(count > 0, "WriteNoLock requires positive count");
            Debug.Assert(this.spaceAvailableSignal is null || this.GetSpaceAvailableNoLock() >= count, "Must have space available"); 

            var hadNoBytesAvailable = this.count == 0;
            
            this.EnsureCapacityNoLock(unchecked(this.count + count));

            var writeStart = (this.start + this.count) % this.buffer.Length;
            var writeStartToEndCount = Math.Min(this.buffer.Length - writeStart, count);
            Buffer.BlockCopy(src: buffer, srcOffset: offset, dst: this.buffer, dstOffset: writeStart, count: writeStartToEndCount);
            Buffer.BlockCopy(src: buffer, srcOffset: offset + writeStartToEndCount, dst: this.buffer, dstOffset: 0, count: count - writeStartToEndCount);
            this.count += count;

            if (hadNoBytesAvailable) { SetSignal(this.bytesAvailableSignal); }
            if (this.spaceAvailableSignal != null && this.GetSpaceAvailableNoLock() <= 0) { ResetSignal(this.spaceAvailableSignal); }
            this.AssertSignalInvariantsNoLock();
        }

        private void EnsureCapacityNoLock(int capacity)
        {
            Debug.Assert(Monitor.IsEntered(this.@lock), "NoLock method");

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
                    static (_, state) =>
                    {
                        var @this = (Pipe)state!;
                        lock (@this.@lock)
                        {
                            @this.InternalCloseWriteSideNoLock();
                        }
                    },
                    state: this
                );
            }
        }

        private void InternalCloseWriteSideNoLock()
        {
            Debug.Assert(Monitor.IsEntered(this.@lock), "NoLock method");
            
            this.writerClosed = true;
            
            // closed writer means reads won't block, so we effectively always have bytes
            if (this.count == 0) { SetSignal(this.bytesAvailableSignal); }
            this.AssertSignalInvariantsNoLock();

            if (this.readerClosed)
            {
                // if both sides are now closed, cleanup
                this.CleanUpNoLock();
            }
        }
        #endregion

        #region ---- Reading ----
        private int Read(Span<byte> buffer, TimeSpan timeout)
        {
            // if we didn't want to read anything, return immediately
            if (buffer.Length == 0)
            {
                return 0;
            }

            TaskCompletionSource<int>? readTaskCompletionSource = null;
            int bytesRead;
            lock (this.@lock)
            {
                if (this.TryReadBeforeWaitNoLock(buffer, out bytesRead))
                {
                    return bytesRead;
                }

                // block concurrent reads
                this.readTask = (readTaskCompletionSource = new()).Task;
            }
            try
            {
                if (!this.bytesAvailableSignal.Wait(timeout, CancellationToken.None)) { ThrowReadTimeout(); }

                lock (this.@lock)
                {
                    return bytesRead = this.PerformReadNoLock(buffer);
                }
            }
            finally // always complete readTask
            {
                readTaskCompletionSource.TrySetResult(bytesRead);
            }
        }

        private Task<int> ReadAsync(Memory<byte> buffer, TimeSpan timeout, CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return Shims.CanceledTask<int>(cancellationToken);
            }

            // if we didn't want to read anything, return immediately
            if (buffer.Length == 0)
            {
                return CompletedZeroTask;
            }

            lock (this.@lock)
            {
                if (this.TryReadBeforeWaitNoLock(buffer.Span, out var bytesRead))
                {
                    return Task.FromResult(bytesRead);
                }

                return this.readTask = this.WaitAndReadNoLockAsync(buffer, timeout, cancellationToken);
            }
        }

        private async Task<int> WaitAndReadNoLockAsync(Memory<byte> buffer, TimeSpan timeout, CancellationToken cancellationToken)
        {
            Debug.Assert(Monitor.IsEntered(this.@lock), "NoLock method");

            if (!await this.bytesAvailableSignal.WaitAsync(timeout, cancellationToken).ConfigureAwait(false))
            {
                ThrowReadTimeout();
            }

            // we need to reacquire the lock after the await since we might have switched threads
            lock (this.@lock)
            {
                return this.PerformReadNoLock(buffer.Span);
            }
        }

        private static void ThrowReadTimeout() => throw new TimeoutException("Timed out reading from the pipe");

        private bool TryReadBeforeWaitNoLock(Span<byte> buffer, out int bytesRead)
        {
            Debug.Assert(Monitor.IsEntered(this.@lock), "NoLock method");
            Throw<ObjectDisposedException>.If(this.readerClosed, "The read side of the pipe is closed");
            Throw<InvalidOperationException>.If(!this.readTask.IsCompleted, "Concurrent reads are not allowed");

            if (this.count == 0 && !this.writerClosed)
            {
                bytesRead = -1;
                return false;
            }

            bytesRead = this.PerformReadNoLock(buffer);
            return true;
        }

        private int PerformReadNoLock(Span<byte> buffer)
        {
            Debug.Assert(Monitor.IsEntered(this.@lock), "NoLock method");
            Debug.Assert(this.count > 0 || this.writerClosed, "Must be called in readable state");
            
            var bytesToRead = Math.Min(this.count, buffer.Length);

            // read from end of buffer
            var bytesToReadFromEnd = Math.Min(bytesToRead, this.buffer.Length - this.start);
            this.buffer.AsSpan(this.start, bytesToReadFromEnd).CopyTo(buffer);
            
            if (bytesToReadFromEnd == bytesToRead)
            {
                this.start += bytesToRead;
            }
            else
            {
                // read from start of buffer
                var bytesToReadFromStart = bytesToRead - bytesToReadFromEnd;
                this.buffer.AsSpan(0, bytesToReadFromStart).CopyTo(buffer.Slice(bytesToReadFromEnd));
                this.start = bytesToReadFromStart;
            }
            this.count -= bytesToRead;

            // ensure that an empty pipe never stays above the max stable size
            if (this.count == 0)
            {
                if (this.buffer.Length > MaxStableSize)
                {
                    this.start = 0;
                    this.buffer = new byte[MaxStableSize];
                }

                if (!this.writerClosed) { ResetSignal(this.bytesAvailableSignal); }
            }

            if (this.spaceAvailableSignal?.IsSet == false
                && this.GetSpaceAvailableNoLock() > 0)
            {
                SetSignal(this.spaceAvailableSignal);
            }
            this.AssertSignalInvariantsNoLock();

            return bytesToRead;
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
                    static (_, state) =>
                    {
                        var @this = (Pipe)state!;
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
            Debug.Assert(Monitor.IsEntered(this.@lock), "NoLock method");

            this.readerClosed = true;

            // closed reader means new written content gets dropped, so we always have space
            if (this.spaceAvailableSignal?.IsSet == false) { SetSignal(this.spaceAvailableSignal); }
            this.AssertSignalInvariantsNoLock();
            
            if (this.writerClosed)
            {
                // if both sides are now closed, cleanup
                this.CleanUpNoLock();
            }
        }
        #endregion

        #region ---- Dispose ----
        private void CleanUpNoLock()
        {
            Debug.Assert(Monitor.IsEntered(this.@lock), "NoLock method");

            this.buffer = Shims.EmptyArray<byte>();
            this.writeTask = this.readTask = CompletedZeroTask;
        }
        #endregion

        #region ---- Input Stream ----
        private sealed class PipeInputStream(Pipe pipe) : Stream
        {
#if !NETSTANDARD1_3
            public override IAsyncResult BeginRead(byte[] buffer, int offset, int count, AsyncCallback? callback, object? state)
            {
                throw WriteOnly();
            }

            public override IAsyncResult BeginWrite(byte[] buffer, int offset, int count, AsyncCallback? callback, object? state)
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
#endif

            private sealed class AsyncWriteResult(object? state, Task writeTask, Pipe.PipeInputStream stream) : IAsyncResult
            {
                public Task WriteTask { get; } = writeTask;

                public Stream Stream { get; } = stream;

                object? IAsyncResult.AsyncState => state;

                WaitHandle IAsyncResult.AsyncWaitHandle => this.WriteTask.As<IAsyncResult>().AsyncWaitHandle;

                bool IAsyncResult.CompletedSynchronously => this.WriteTask.As<IAsyncResult>().CompletedSynchronously;

                bool IAsyncResult.IsCompleted => this.WriteTask.IsCompleted;
            }

            public override bool CanRead => false;
            public override bool CanSeek => false;
            public override bool CanTimeout => true;
            public override bool CanWrite => true;

#if !NETSTANDARD1_3
            public override void Close()
            {
                base.Close(); // calls Dispose(true)
            }
#endif

            public override Task CopyToAsync(Stream destination, int bufferSize, CancellationToken cancellationToken)
            {
                throw WriteOnly();
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    pipe.CloseWriteSide();
                }
            }

#if !NETSTANDARD1_3
            public override int EndRead(IAsyncResult asyncResult)
            {
                throw WriteOnly();
            }

            public override void EndWrite(IAsyncResult asyncResult)
            {
                Throw.IfNull(asyncResult, nameof(asyncResult));
                var writeResult = asyncResult as AsyncWriteResult 
                    ?? throw new ArgumentException("must be created by this stream's BeginWrite method", nameof(asyncResult));
                writeResult.WriteTask.Wait();
            }
#endif

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

            public override void Write(byte[] buffer, int offset, int count) => 
                // Since Pipes are only written to internally and outside of tests we'll always use async IO,
                // We don't offer an optimized implementation of sync Write()
                this.WriteAsync(buffer, offset, count).GetAwaiter().GetResult();

            public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
                pipe.WriteAsync(buffer, offset, count, TimeSpan.FromMilliseconds(this.WriteTimeout), cancellationToken);

            public override void WriteByte(byte value) => base.WriteByte(value);

#if NETCOREAPP2_1_OR_GREATER || NETSTANDARD2_1
            public override int Read(Span<byte> buffer) => throw WriteOnly();

            public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken) => throw WriteOnly();

            public override void Write(ReadOnlySpan<byte> buffer) => 
                // similar to Write(byte[], int, int), we won't call this so we don't bother to optimize
                base.Write(buffer);

            public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken) =>
                // see comment in Write(ReadOnlySpan<byte>)
                base.WriteAsync(buffer, cancellationToken);
#endif

            private int writeTimeout = Timeout.Infinite;

            public override int WriteTimeout
            {
                get => this.writeTimeout;
                set
                {
                    if (value != Timeout.Infinite)
                    {
                        Throw.IfOutOfRange(value, "WriteTimeout", min: 0);
                    }
                    this.writeTimeout = value;
                }
            }

            private static NotSupportedException WriteOnly([CallerMemberName] string memberName = "") =>
                throw new NotSupportedException(memberName + ": the stream is write only");
        }
        #endregion

        #region ---- Output Stream ----
        private sealed class PipeOutputStream(Pipe pipe) : Stream
        {
#if !NETSTANDARD1_3
            public override IAsyncResult BeginRead(byte[] buffer, int offset, int count, AsyncCallback? callback, object? state)
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
#endif

            private sealed class AsyncReadResult(object? state, Task<int> readTask, Pipe.PipeOutputStream stream) : IAsyncResult
            {
                public Task<int> ReadTask { get; } = readTask;

                public Stream Stream { get; } = stream;

                object? IAsyncResult.AsyncState => state;

                WaitHandle IAsyncResult.AsyncWaitHandle => this.ReadTask.As<IAsyncResult>().AsyncWaitHandle;

                bool IAsyncResult.CompletedSynchronously => this.ReadTask.As<IAsyncResult>().CompletedSynchronously;

                bool IAsyncResult.IsCompleted => this.ReadTask.IsCompleted;
            }

#if !NETSTANDARD1_3
            public override IAsyncResult BeginWrite(byte[] buffer, int offset, int count, AsyncCallback? callback, object? state)
            {
                throw ReadOnly();
            }
#endif

            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanTimeout => true;
            public override bool CanWrite => false;

#if !NETSTANDARD1_3
            public override void Close()
            {
                base.Close(); // calls Dispose(true)
            }
#endif

            public override Task CopyToAsync(Stream destination, int bufferSize, CancellationToken cancellationToken)
            {
                // the base implementation is reasonable
                return base.CopyToAsync(destination, bufferSize, cancellationToken);
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    pipe.CloseReadSide();
                }
            }

#if !NETSTANDARD1_3
            public override int EndRead(IAsyncResult asyncResult)
            {
                Throw.IfNull(asyncResult, nameof(asyncResult));
                var readResult = asyncResult as AsyncReadResult
                    ?? throw new ArgumentException("must be created by this stream's BeginRead method", nameof(asyncResult));
                return readResult.ReadTask.Result;
            }

            public override void EndWrite(IAsyncResult asyncResult)
            {
                throw ReadOnly();
            }
#endif

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
                Throw.IfInvalidBuffer(buffer, offset, count);

                return pipe.Read(buffer.AsSpan(offset, count), TimeSpan.FromMilliseconds(this.readTimeout));
            }

            public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            {
                Throw.IfInvalidBuffer(buffer, offset, count);

                return pipe.ReadAsync(new(buffer, offset, count), TimeSpan.FromMilliseconds(this.readTimeout), cancellationToken);
            }

#if NETCOREAPP2_1_OR_GREATER || NETSTANDARD2_1
            public override void Write(ReadOnlySpan<byte> buffer) => throw ReadOnly();

            public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken) =>
                throw ReadOnly();

            public override int Read(Span<byte> buffer) =>
                pipe.Read(buffer, TimeSpan.FromMilliseconds(this.readTimeout));

            public override int ReadByte()
            {
                byte b = 0;
                int result = this.Read(MemoryMarshal.CreateSpan(ref b, length: 1));
                return result != 0 ? b : -1;
            }

            public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken) =>
                new(pipe.ReadAsync(buffer, TimeSpan.FromMilliseconds(this.readTimeout), cancellationToken)); 
#else
            public override int ReadByte() => base.ReadByte();
#endif

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

            private static NotSupportedException ReadOnly([CallerMemberName] string memberName = "") =>
                throw new NotSupportedException(memberName + ": the stream is read only");
        }
#endregion
    }
}
