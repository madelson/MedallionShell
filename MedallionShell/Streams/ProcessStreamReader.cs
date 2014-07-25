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
        public abstract string GetContent();

        /// <summary>
        /// Returns the full content output by the process as a byte array. Unlike <see cref="Stream.ReadToEnd"/>, This will fail with
        /// <see cref="InvalidOperationException"/> if the full content is not available (e. g. if the stream or
        /// reader have been read from via different methods).
        /// </summary>
        public abstract byte[] GetContentBytes();

        /// <summary>
        /// Returns the lines of output as an <see cref="IEnumerable{string}"/>
        /// </summary>
        public abstract IEnumerable<string> GetLines();

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
        public abstract Task PipeToAsync(Stream stream, bool leaveReaderOpen = false, bool leaveStreamOpen = false);

        /// <summary>
        /// Pipes the output of the reader to the given writer. This occurs asynchronously, so this
        /// method returns before all content has been written
        /// </summary>
        public abstract Task PipeToAsync(TextWriter writer, bool leaveReaderOpen = false, bool leaveWriterOpen = false);

        public abstract Task PipeToAsync(ICollection<string> lines, bool leaveReaderOpen = false);

        public Task PipeToAsync(FileInfo file, bool leaveReaderOpen = false)
        {
            Throw.IfNull(file, "file");

            // used over FileInfo.OpenWrite to get read file share, which seems potentially useful and
            // not that harmful
            var stream = new FileStream(file.FullName, FileMode.Create, FileAccess.Write, FileShare.Read);
            return this.PipeToAsync(stream, leaveReaderOpen: leaveReaderOpen, leaveStreamOpen: false);
        }
    }
}
