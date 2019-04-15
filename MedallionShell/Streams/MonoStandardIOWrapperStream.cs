using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Medallion.Shell.Streams
{
    /// <summary>
    /// Unlike .NET, Mono throws an exception if you try to write to standard input after the process exits.
    /// To compensate for this, we wrap standard input with a stream that suppresses these errors.
    ///
    /// See https://github.com/madelson/MedallionShell/issues/6
    /// </summary>
    internal sealed class MonoStandardIOWrapperStream : Stream
    {
        private readonly Stream stream;

        public MonoStandardIOWrapperStream(Stream stream)
        {
            this.stream = stream;
        }

        public override bool CanRead => this.stream.CanRead;
        public override bool CanSeek => this.stream.CanSeek;
        public override bool CanWrite => this.stream.CanWrite;
        public override long Length => this.stream.Length;

        public override long Position
        {
            get => this.stream.Position;
            set => this.stream.Position = value;
        }

        public override bool CanTimeout => this.stream.CanTimeout;
        public override int ReadTimeout { get => this.stream.ReadTimeout; set => this.stream.ReadTimeout = value; }
        public override int WriteTimeout { get => this.stream.WriteTimeout; set => this.stream.WriteTimeout = value; }

        public override void Flush() => this.stream.Flush();

        public override Task FlushAsync(CancellationToken cancellationToken) => this.stream.FlushAsync(cancellationToken);

        public override int Read(byte[] buffer, int offset, int count)
        {
            return this.stream.Read(buffer, offset, count);
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            return this.stream.ReadAsync(buffer, offset, count, cancellationToken);
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            return this.stream.Seek(offset, origin);
        }

        public override void SetLength(long value)
        {
            this.stream.SetLength(value);
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            // this approach is a bit risky since we might end up suppressing other unrelated IO exceptions.
            // However, aside from trying to check the process (which brings its own risks if the process is in
            // a weird state), there is no really robust way to do this given that different OS's yield different
            // error messages

            try { this.stream.Write(buffer, offset, count); }
            catch (IOException) { }
        }

        public async override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            // see comment in Write()

            try { await this.stream.WriteAsync(buffer, offset, count, cancellationToken).ConfigureAwait(false); }
            catch (IOException) { }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                this.stream.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
