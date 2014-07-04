using Medallion.Shell.Async;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

        private readonly AsyncLock @lock;
        private readonly Stream stream;
        private volatile bool finished;
        private Mode mode;
        private Stream pipeStream;
        private MemoryStream buffer = new MemoryStream();

        public ProcessStreamHandler(Stream stream)
        {
            Throw.IfNull(stream, "stream");
            this.stream = stream;
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

                    if (mode == Mode.BufferedManualRead)
                    {
                        var bytesRead = await this.stream.ReadAsync(localBuffer, offset: 0, count: localBuffer.Length);
                        if (bytesRead > 0)
                        {

                        }
                        else if (bytesRead < 0)
                        {
                            this.finished = true;
                        }
                    }
                }

                if (mode == Mode.Piped)
                {
                    await this.buffer.CopyToAsync(this.pipeStream);
                    await this.stream.CopyToAsync(this.stream);
                    this.finished = true;
                }

                else if (mode == Mode.DiscardContents)
                {
                    // TODO should we do this in the piped case?
                    using (await this.@lock.AcquireAsync())
                    {
                        this.buffer.Dispose();
                        this.buffer = null;
                    }

                    int readResult;
                    do
                    {
                        readResult = await this.stream.ReadAsync(localBuffer, offset: 0, count: localBuffer.Length);
                    }
                    while (readResult >= 0);
                }
            }
        }
    }
}