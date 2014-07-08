using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Medallion.Shell.Streams
{
    // TODO should probably wrap StreamReader instead of extending, since this way we get worse perf
    // on async methods

    /// <summary>
    /// An implementation of <see cref="StreamReader"/> with additional methods to control behavior. The 
    /// </summary>
    public abstract class ProcessStreamReader : StreamReader
    {
        // prevents external inheritors
        internal ProcessStreamReader(Stream stream) : base(stream) { }

        /// <summary>
        /// Discards all output from the underlying stream. This prevents the process from blocking because
        /// the output pipe's buffer is full without wasting any memory on buffering the output
        /// </summary>
        public abstract void Discard();

        /// <summary>
        /// By default, the underlying stream output is buffered to prevent the process from blocking
        /// because the output pipe's buffer is full. Calling this method disables this behavior. This is useful
        /// when it is desirable to have the process block while waiting for output to be read
        /// </summary>
        public abstract void StopBuffering();

        /// <summary>
        /// Pipes the output of the underlying stream to the given stream. This occurs asynchronously, so this
        /// method returns before all content has been written
        /// </summary>
        public abstract void PipeTo(Stream stream);

        /// <summary>
        /// Pipes the output of the reader to the given writer. This occurs asynchronously, so this
        /// method returns before all content has been written
        /// </summary>
        public abstract void PipeTo(TextWriter writer);
    }
}
