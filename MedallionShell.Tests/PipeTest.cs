using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Medallion.Shell.Tests
{
    [TestClass]
    public class PipeTest
    {
        [TestMethod]
        public void TestPiping()
        {
            var shell = new Shell(o => o.ThrowOnError());

            var kinds = Enum.GetValues(typeof(Kind)).Cast<Kind>();
            foreach (var inKind in kinds)
            {
                foreach (var outKind in kinds.Where(k => k != Kind.String))
                {
                    dynamic input = this.CreateSinkOrSource(inKind, isOut: false);
                    dynamic output = this.CreateSinkOrSource(outKind, isOut: true);
                    var command = shell.Run("SampleCommand", "echo");
                    var tasks = new List<Task>();
                    if (input is TextReader)
                    {
                        tasks.Add(command.StandardInput.PipeFromAsync((TextReader)input));
                    }
                    else
                    {
                        command = command < input;
                    }
                    if (output is TextWriter)
                    {
                        tasks.Add(command.StandardOutput.PipeToAsync((TextWriter)output));
                    }
                    else
                    {
                        command = command > output;
                    }
                    tasks.Add(command.Task);
                    Task.WaitAll(tasks.ToArray());

                    string result = this.Read(outKind, output);
                    // MA: the output changes slightly if we are inputting as lines (adds a newline) and not outputting as lines
                    result.ShouldEqual(Content + (inKind == Kind.Lines && outKind != Kind.Lines ? Environment.NewLine : string.Empty), inKind + " => " + outKind);
                }
            }
        }

        private static readonly string Content = string.Join(Environment.NewLine, Enumerable.Range(1, 100).Select(i => string.Join(string.Empty, Enumerable.Repeat(i.As<object>(), i))));

        public enum Kind
        {
            Stream,
            File,
            ReaderWriter,
            String,
            Chars,
            Lines,
        }

        private object CreateSinkOrSource(Kind kind, bool isOut)
        { 
            switch (kind)
            {
                case Kind.Stream:
                    return isOut ? new MemoryStream() : new MemoryStream(Encoding.Default.GetBytes(Content));
                case Kind.File:
                    var path = GetPath(isOut);
                    if (!isOut)
                    {
                        File.WriteAllText(path, Content);
                    }
                    return new FileInfo(path);
                case Kind.ReaderWriter:
                    return isOut ? new StringWriter() : new StringReader(Content).As<object>();
                case Kind.String:
                    return Content;
                case Kind.Chars:
                    return isOut ? new List<char>() : Content.ToCharArray().As<object>();
                case Kind.Lines:
                    return isOut ? new List<string>() : Content.Split(new[] { Environment.NewLine }, StringSplitOptions.None).As<object>();
                default:
                    throw new InvalidOperationException();
            }
        }

        private string Read(Kind kind, object output)
        {
            switch (kind)
            {
                case Kind.Stream:
                    return Encoding.Default.GetString(((MemoryStream)output).ToArray());
                case Kind.File:
                    return File.ReadAllText(GetPath(isOut: true));
                case Kind.ReaderWriter:
                    return ((StringWriter)output).ToString();
                case Kind.Chars:
                    return string.Join(string.Empty, (IEnumerable<char>)output);
                case Kind.Lines:
                    return string.Join(Environment.NewLine, (IEnumerable<string>)output);
                default:
                    throw new InvalidOperationException();
            }
        }

        [ClassCleanup]
        public static void TearDown()
        {
            foreach (var isOut in new[] { false, true })
            {
                var path = GetPath(isOut);
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }

        private static string GetPath(bool isOut)
        {
            return Path.Combine(Path.GetTempPath(), "PipeTestFile_" + (isOut ? "IN" : "OUT") + ".txt");
        }
    }
}
