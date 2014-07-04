using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Medallion.Shell.Streams
{
    internal sealed class MultiStream : Stream
    {
        private readonly object @lock = new object();
        private readonly IReadOnlyList<Stream> streams;
        private readonly long[] 

        public MultiStream(IEnumerable<Stream> streams)
        {
            Throw.IfNull(streams, "streams");

            this.streams = streams.ToArray();
            Throw.If(this.streams.Any(s => s == null || !s.CanRead), "streams: must all be non-null and readable!");
        }

        public override bool CanRead
        {
            // allowed because we require all streams to be readable in the constructor
            get { return true; }
        }

        public override bool CanSeek
        {
            get { return this.streams.Count > 0 && this.streams.All(s => s.CanSeek); }
        }

        public override bool CanWrite
        {
            get { return false; }
        }

        public override void Flush()
        {
            // no-op (since we don't write)
        }

        public override long Length
        {
            // todo use counts of streams so far
            get { return this.streams.Sum(s => s.Length); }
        }

        public override long Position
        {
            get
            {
                lock (this.@lock)
                {
                    throw new NotImplementedException("todo sum lengths of non-completed streams + pos in last stream!");
                }
            }
            set
            {
                lock (this.@lock)
                {
                    throw new NotImplementedException("need to set position in underlying streams!");
                }
            }
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException("SetLength");
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException("Stream is read-only");
        }
    }
}
