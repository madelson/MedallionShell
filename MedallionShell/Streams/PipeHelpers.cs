using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Medallion.Shell.Streams
{
    public static class PipeHelpers
    {
        public static async Task CopyToAsync(this TextReader reader, TextWriter writer, bool leaveReaderOpen, bool leaveWriterOpen)
        {
            try
            {
                var buffer = new char[Constants.CharBufferSize];
                int charsRead;
                while ((charsRead = await reader.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false)) > 0) 
                {
                    await writer.WriteAsync(buffer, 0, charsRead).ConfigureAwait(false);
                }
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
                if (!leaveReaderOpen)
                {
                    reader.Dispose();
                }
                if (!leaveWriterOpen)
                {
                    writer.Dispose();
                }
            }
        } 
    }
}
