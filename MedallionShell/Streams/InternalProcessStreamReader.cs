using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Diagnostics;

namespace Medallion.Shell.Streams
{
    internal sealed class InternalProcessStreamReader : ProcessStreamReader
    {
        /// <summary>
        /// The underlying <see cref="Stream"/> from the <see cref="Process"/>
        /// </summary>
        private readonly Stream processStream;
        private readonly Pipe pipe;
        private readonly StreamReader reader;
        private readonly Task task;
        private volatile bool discardContents;

        public InternalProcessStreamReader(StreamReader processStreamReader)
        {
            this.processStream = processStreamReader.BaseStream;
            this.pipe = new Pipe();
            this.reader = new StreamReader(this.pipe.OutputStream);
            this.task = Task.Run(() => this.BufferLoop());
        }

        public Task Task { get { return this.task; } }

        private async Task BufferLoop()
        {
            try
            {
                var buffer = new byte[Constants.ByteBufferSize];
                int bytesRead;
                while (
                    !this.discardContents
                    && (bytesRead = await this.processStream.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false)) > 0
                )
                {
                    await this.pipe.InputStream.WriteAsync(buffer, 0, bytesRead).ConfigureAwait(false);
                }
            }
            finally
            {
                this.processStream.Close();
                this.pipe.InputStream.Close();
            }
        }

        #region ---- ProcessStreamReader implementation ----
        public override Stream BaseStream
        {
            get { return this.reader.BaseStream; }
        }

        public override void Discard()
        {
            this.discardContents = true;
            this.reader.Dispose();
        }

        public override void StopBuffering()
        {
            // this causes writes to the pipe to block, thus
            // preventing unbounded buffering (although some more content
            // may still be buffered)
            this.pipe.SetFixedLength();
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
                this.Discard();
            }
        }
        #endregion
    }
}
