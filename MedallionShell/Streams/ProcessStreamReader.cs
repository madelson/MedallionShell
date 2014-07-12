using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Medallion.Shell.Streams
{
    /// <summary>
    /// An implementation of <see cref="TextReader"/> with additional methods to control behavior. The 
    /// </summary>
    public abstract class ProcessStreamReader : TextReader
    {
        // prevents external inheritors
        internal ProcessStreamReader() { }

        /// <summary>
        /// Provides access to the underlying stream
        /// </summary>
        public abstract Stream BaseStream { get; }

        /// <summary>
        /// Returns the full content output by the process as a string. Unlike <see cref="TextReader.ReadToEnd"/>, This will fail with
        /// <see cref="InvalidOperationException"/> if the full content is not available (e. g. if the stream or
        /// reader have been read from via different methods).
        /// </summary>
        public abstract string Content { get; }

        /// <summary>
        /// Returns the full content output by the process as a byte array. Unlike <see cref="Stream.ReadToEnd"/>, This will fail with
        /// <see cref="InvalidOperationException"/> if the full content is not available (e. g. if the stream or
        /// reader have been read from via different methods).
        /// </summary>
        public abstract byte[] GetContentBytes();

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
