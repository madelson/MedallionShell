using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Medallion.Shell.Streams
{
    internal static class PipeHelpers
    {
        public static Task CopyToAsync(this TextReader reader, TextWriter writer, bool leaveReaderOpen, bool leaveWriterOpen)
        {
            return reader.PipeAsync(
                async () =>
                {
                    var buffer = new char[Constants.CharBufferSize];
                    int charsRead;
                    while ((charsRead = await reader.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false)) > 0)
                    {
                        await writer.WriteAsync(buffer, 0, charsRead).ConfigureAwait(false);
                    }
                },
                leaveOpen: leaveReaderOpen,
                extraDisposeAction: leaveWriterOpen ? default(Action) : () => writer.Dispose()
            );
        }

        /// <summary>
        /// Similar to <see cref="Stream.CopyToAsync(Stream)"/>, but performs a <see cref="Stream.FlushAsync()"/>
        /// following each <see cref="Stream.WriteAsync(byte[], int, int)"/>. The benefit of this is that data
        /// moves through the pipe instead of becoming caught in the write buffer of <paramref name="destination"/>
        /// </summary>
        public static async Task CopyToAsyncWithAutoFlush(this Stream source, Stream destination)
        {
            // note: this could be written more simply to just loop calling ReadAsync, WriteAsync, FlushAsync.
            // As an optimization, we instead flush in parallel with reading. This should hopefully help reduce
            // the overhead of flushing on each write call

            var buffer = new byte[Constants.ByteBufferSize];
            Task flushTask = null;
            try
            {
                while (true)
                {
                    // read
                    var bytesRead = await source.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false);

                    // complete any outstanding flush
                    if (flushTask != null)
                    {
                        // we null out flushTask before awaiting to avoid the case where this await fails
                        // and then we await the same flush task again in the catch block
                        var flushTaskToAwait = flushTask;
                        flushTask = null;
                        await flushTaskToAwait.ConfigureAwait(false);
                    }

                    // check if the source is exhausted. We've already finished the
                    // flush at this point so we can just return
                    if (bytesRead == 0) { return; }

                    // write
                    await destination.WriteAsync(buffer, 0, bytesRead).ConfigureAwait(false);

                    // begin flush. We allow this task to run in parallel with the read to minimize overhead
                    // as compared to the base stream implementation of CopyToAsync()
                    flushTask = destination.FlushAsync();
                }
            }
            catch (Exception ex)
            {
                // if an exception is thrown, make sure we don't leave behind a lingering flush task
                if (flushTask != null)
                {
                    try { await flushTask.ConfigureAwait(false); }
                    catch (Exception flushException)
                    {
                        throw new AggregateException(ex, flushException);
                    }
                }
                throw;
            }
        }

        public static async Task PipeAsync(this IDisposable @this, Func<Task> pipeTaskFactory, bool leaveOpen, Action extraDisposeAction = null)
        {
            Console.WriteLine("Beginning Pipe opp");
            try
            {
                await pipeTaskFactory().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Caught pipe exception " + ex);
                if (!ex.IsExpectedPipeException())
                {
                    throw;
                }
            }
            finally
            {
                if (!leaveOpen)
                {
                    Console.WriteLine("Closing pipe stream");
                    @this.Dispose();
                }
                extraDisposeAction?.Invoke();
                Console.WriteLine("Ending PipeAsync");
            }
        }
    }
}
