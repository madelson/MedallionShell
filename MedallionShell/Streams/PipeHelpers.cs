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
 
        public static async Task PipeAsync(this IDisposable @this, Func<Task> pipeTaskFactory, bool leaveOpen, Action extraDisposeAction = null)
        {
            try
            {
                await pipeTaskFactory().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                if (!ex.IsExpectedPipeException())
                {
                    throw;
                }
            }
            finally
            {
                if (!leaveOpen)
                {
                    @this.Dispose();
                }
                if (extraDisposeAction != null)
                {
                    extraDisposeAction();
                }
            }
        }
    }
}
