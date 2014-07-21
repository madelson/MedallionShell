using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Medallion.Shell.Streams
{
    public sealed class ProcessStreamWriter : TextWriter
    {
        private readonly StreamWriter writer;

        internal ProcessStreamWriter(StreamWriter writer) 
        {
            Throw.IfNull(writer, "writer");
            this.writer = writer;
        }

        #region ---- Custom methods ----
        public Stream BaseStream { get { return this.writer.BaseStream; } }

        public Task PipeFromAsync(Stream stream, bool leaveWriterOpen = false, bool leaveStreamOpen = false)
        {
            Throw.IfNull(stream, "stream");

            return this.PipeFromAsyncInternal(stream, leaveWriterOpen: leaveWriterOpen, leaveStreamOpen: leaveStreamOpen);
        }

        private async Task PipeFromAsyncInternal(Stream stream, bool leaveWriterOpen, bool leaveStreamOpen)
        {
            try
            {
                await this.writer.FlushAsync(); // flush any content buffered in the writer
                await stream.CopyToAsync(this.BaseStream);
            }
            finally
            {
                if (!leaveWriterOpen)
                {
                    this.Dispose();
                }
                if (!leaveStreamOpen)
                {
                    stream.Dispose();
                }
            }
        }

        public Task PipeFromAsync(IEnumerable<string> lines, bool leaveOpen = false)
        {
            Throw.IfNull(lines, "lines");

            return this.PipeFromAsyncInternal(lines, leaveOpen);
        }

        private async Task PipeFromAsyncInternal(IEnumerable<string> lines, bool leaveOpen)
        {
            try
            {
                foreach (var line in lines)
                {
                    await this.writer.WriteLineAsync(line);
                }
            }
            finally
            {
                if (!leaveOpen)
                {
                    this.Dispose();
                }
            }
        }
        #endregion

        #region ---- TextWriter methods ----
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                this.writer.Dispose();
            }
        }

        public override Encoding Encoding
        {
            get { return this.writer.Encoding; }
        }
        
        public override void Flush()
        {
            this.writer.Flush();
        }

        public override Task FlushAsync()
        {
            return this.writer.FlushAsync();
        }

        public override IFormatProvider FormatProvider
        {
            get { return this.writer.FormatProvider; }
        }

        public override string NewLine { get { return this.writer.NewLine; } set { this.writer.NewLine = value; } }

        public override void Write(bool value)
        {
            this.writer.Write(value);
        }

        public override void Write(char value)
        {
            this.writer.Write(value);
        }

        public override void Write(char[] buffer)
        {
            this.writer.Write(buffer);
        }

        public override void Write(char[] buffer, int index, int count)
        {
            this.writer.Write(buffer, index, count);
        }

        public override void Write(decimal value)
        {
            this.writer.Write(value);
        }

        public override void Write(double value)
        {
            this.writer.Write(value);
        }

        public override void Write(float value)
        {
            this.writer.Write(value);
        }

        public override void Write(int value)
        {
            this.writer.Write(value);
        }

        public override void Write(long value)
        {
            this.writer.Write(value);
        }

        public override void Write(object value)
        {
            this.writer.Write(value);
        }

        public override void Write(string format, object arg0)
        {
            this.writer.Write(format, arg0);
        }

        public override void Write(string format, object arg0, object arg1)
        {
            this.writer.Write(format, arg0, arg1);
        }

        public override void Write(string format, object arg0, object arg1, object arg2)
        {
            this.writer.Write(format, arg0, arg1, arg2);
        }

        public override void Write(string format, params object[] arg)
        {
            this.writer.Write(format, arg);
        }

        public override void Write(string value)
        {
            this.writer.Write(value);
        }

        public override void Write(uint value)
        {
            this.writer.Write(value);
        }

        public override void Write(ulong value)
        {
            this.writer.Write(value);
        }

        public override Task WriteAsync(char value)
        {
            return this.writer.WriteAsync(value);
        }

        public override Task WriteAsync(char[] buffer, int index, int count)
        {
            return this.writer.WriteAsync(buffer, index, count);
        }

        public override Task WriteAsync(string value)
        {
            return this.writer.WriteAsync(value);
        }

        public override void WriteLine()
        {
            this.writer.WriteLine();
        }

        public override void WriteLine(bool value)
        {
            this.writer.WriteLine(value);
        }

        public override void WriteLine(char value)
        {
            this.writer.WriteLine(value);
        }

        public override void WriteLine(char[] buffer)
        {
            this.writer.WriteLine(buffer);
        }

        public override void WriteLine(char[] buffer, int index, int count)
        {
            this.writer.WriteLine(buffer, index, count);
        }

        public override void WriteLine(decimal value)
        {
            this.writer.WriteLine(value);
        }

        public override void WriteLine(double value)
        {
            this.writer.WriteLine(value);
        }

        public override void WriteLine(float value)
        {
            this.writer.WriteLine(value);
        }

        public override void WriteLine(int value)
        {
            this.writer.WriteLine(value);
        }

        public override void WriteLine(long value)
        {
            this.writer.WriteLine(value);
        }

        public override void WriteLine(object value)
        {
            this.writer.WriteLine(value);
        }

        public override void WriteLine(string format, object arg0)
        {
            this.writer.WriteLine(format, arg0);
        }

        public override void WriteLine(string format, object arg0, object arg1)
        {
            this.writer.WriteLine(format, arg0, arg1);
        }

        public override void WriteLine(string format, object arg0, object arg1, object arg2)
        {
            this.writer.WriteLine(format, arg0, arg1, arg2);
        }

        public override void WriteLine(string format, params object[] arg)
        {
            this.writer.WriteLine(format, arg);
        }

        public override void WriteLine(string value)
        {
            this.writer.WriteLine(value);
        }

        public override void WriteLine(uint value)
        {
            this.writer.WriteLine(value);
        }

        public override void WriteLine(ulong value)
        {
            this.writer.WriteLine(value);
        }

        public override Task WriteLineAsync()
        {
            return this.writer.WriteLineAsync();
        }

        public override Task WriteLineAsync(char value)
        {
            return this.writer.WriteLineAsync(value);
        }

        public override Task WriteLineAsync(char[] buffer, int index, int count)
        {
            return this.writer.WriteLineAsync(buffer, index, count);
        }

        public override Task WriteLineAsync(string value)
        {
            return this.writer.WriteLineAsync(value);
        }
        #endregion
    }
}
