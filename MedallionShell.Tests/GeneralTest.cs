using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Medallion.Shell.Tests
{
    [TestClass]
    public class GeneralTest
    {
        [TestMethod]
        public void TestGrep()
        {
            var command = Shell.Default.Run("SampleCommand", "grep", "a+");
            command.StandardInput.WriteLine("hi");
            command.StandardInput.WriteLine("aa");
            command.StandardInput.Dispose();
            command.StandardOutput.ReadToEnd().ShouldEqual("aa\r\n");
        }

        [TestMethod]
        public void TestPipedGrep()
        {
            Log.WriteLine("******** TestPipedGrep starting *********");

            var command = (
                Command.Run("SampleCommand", "grep", "a") < new[] { "abcd", "a", "ab", "abc" }
                | Command.Run("SampleCommand", "grep", "b")
                | Command.Run("SampleCommand", "grep", "c") 
            );

            var results = command.StandardOutput.GetLines().ToArray();

            results.SequenceEqual(new[] { "abcd", "abc" }).ShouldEqual(true);
        }

        [TestMethod]
        public void TestLongWriteWithInfrequentReads()
        {
            var lines = Enumerable.Range(0, 100).Select(i => i.ToString())
                .ToArray();

            var command = Command.Run("SampleCommand", "grep", ".") < lines;
            var outputLines = new List<string>();
            var readTask = Task.Run(() =>
            {
                var rand = new Random(12345);
                while (true)
                {
                    if (rand.Next(10) == 0)
                    {
                        Thread.Sleep(200);
                    }
                    else
                    {
                        var line = command.StandardOutput.ReadLine();
                        if (line == null)
                        {
                            return;
                        }
                        else
                        {
                            outputLines.Add(line);
                        }
                    }
                }
            });

            Task.WaitAll(command.Task, readTask);

            string.Join("/", outputLines).ShouldEqual(string.Join("/", lines));
        }

        [TestMethod]
        public void TestHead()
        {
            var shell = new Shell(o => o.StartInfo(si => si.RedirectStandardError = false));
            var command = shell.Run("SampleCommand", "head", "10") < Enumerable.Range(0, 100).Select(i => i.ToString());
            command.Task.Result.StandardOutput.Trim().ShouldEqual(string.Join(Environment.NewLine, Enumerable.Range(0, 10)));
        }

        [TestMethod]
        public void TestCloseStandardOutput()
        {
            var shell = new Shell(o => o.StartInfo(si => si.RedirectStandardError = false));
            var command = shell.Run("SampleCommand", "grep", "a") < Enumerable.Repeat(new string('a', 1000), 1000);
            command.StandardOutput.BaseStream.ReadByte();
            command.StandardOutput.BaseStream.Dispose();
            
            UnitTestHelpers.AssertThrows<ObjectDisposedException>(() => command.StandardOutput.BaseStream.ReadByte());
            UnitTestHelpers.AssertThrows<ObjectDisposedException>(() => command.StandardOutput.ReadToEnd());

            var command2 = shell.Run("SampleCommand", "grep", "a") < Enumerable.Repeat(new string('a', 1000), 1000);
            command2.Wait();
            command2.StandardOutput.Dispose();
            UnitTestHelpers.AssertThrows<ObjectDisposedException>(() => command2.StandardOutput.Read());
        }

        [TestMethod]
        public void TestExitCode()
        {
            Assert.IsTrue(Command.Run("SampleCommand", "exit", 0).Result.Success);
            Assert.IsFalse(Command.Run("SampleCommand", "exit", 1).Result.Success);

            var shell = new Shell(o => o.ThrowOnError());
            var ex = UnitTestHelpers.AssertThrows<AggregateException>(() => shell.Run("SampleCommand", "exit", -1).Task.Wait());
            ex.InnerExceptions.Select(e => e.GetType()).SequenceEqual(new[] { typeof(ErrorExitCodeException) })
                .ShouldEqual(true);

            shell.Run("SampleCommand", "exit", 0).Task.Wait();
        }

        [TestMethod]
        public void TestThrowOnErrorWithTimeout()
        {
            var command = Command.Run("SampleCommand", new object[] { "exit", 1 }, o => o.ThrowOnError().Timeout(TimeSpan.FromDays(1)));
            var ex = UnitTestHelpers.AssertThrows<AggregateException>(() => command.Task.Wait());
            ex.InnerExceptions.Select(e => e.GetType()).SequenceEqual(new[] { typeof(ErrorExitCodeException) })
                .ShouldEqual(true);
        }

        [TestMethod]
        public void TestTimeout()
        {
            var willTimeout = Command.Run("SampleCommand", new object[] { "sleep", 1000000 }, o => o.Timeout(TimeSpan.FromMilliseconds(200)));
            var ex = UnitTestHelpers.AssertThrows<AggregateException>(() => willTimeout.Task.Wait());
            Assert.IsInstanceOfType(ex.InnerException, typeof(TimeoutException));
        }

        [TestMethod]
        public void TestErrorHandling()
        {
            var command = Command.Run("SampleCommand", "echo") < "abc" > new char[0];
            UnitTestHelpers.AssertThrows<AggregateException>(() => command.Wait());

            var command2 = Command.Run("SampleCommand", "echo") < this.ErrorLines();
            UnitTestHelpers.AssertThrows<AggregateException>(() => command.Wait());
        }

        [TestMethod]
        public void TestStopBufferingAndDiscard()
        {
            var command = Command.Run("SampleCommand", "pipe");
            command.StandardOutput.StopBuffering();
            var line = new string('a', 100);
            var state = 0;
            while (state < 2)
            {
                Log.WriteLine("Write to unbuffered command");
                var task = command.StandardInput.WriteLineAsync(line);
                if (!task.Wait(TimeSpan.FromSeconds(1)))
                {
                    if (state == 0)
                    {
                        Log.WriteLine("Buffer full: read one line");
                        var outLine = command.StandardOutput.ReadLine();
                        outLine.ShouldEqual(line);
                        // after this, we need to read more than one line to re-block since the readers buffer internally
                    }
                    else
                    {
                        Log.WriteLine("Buffer full: discard content");
                        command.StandardOutput.Discard();
                    }
                    
                    task.Wait(TimeSpan.FromSeconds(.5)).ShouldEqual(true, "can finish after read");
                    if (state == 1)
                    {
                        command.StandardInput.Dispose();
                    }
                    state++;
                }
            }
        }

        [TestMethod]
        public void TestKill()
        {
            var command = Command.Run("SampleCommand", "pipe");
            command.StandardInput.WriteLine("abc");
            command.StandardInput.Flush();
            Thread.Sleep(100);
            command.Task.IsCompleted.ShouldEqual(false);

            command.Kill();
            command.Result.Success.ShouldEqual(false);
            command.Result.ExitCode.ShouldEqual(-1);

            command.StandardOutput.ReadLine().ShouldEqual("abc");
        }

        [TestMethod]
        public void TestKillAfterFinished()
        {
            var command = Command.Run("SampleCommand", "bool", true, "something");
            command.Task.Wait();
            command.Kill();
            command.Result.Success.ShouldEqual(true);
        }

        [TestMethod]
        public void TestNestedKill()
        {
            var lines = new List<string>();
            var pipeline = Command.Run("SampleCommand", "pipe")
                | Command.Run("SampleCommand", "pipe")
                | Command.Run("SampleCommand", "pipe") > lines;

            // demonstrate that a single line can make it all the way through the pipeline
            // without getting caught in a buffer along the way
            pipeline.StandardInput.WriteLine("a line");
            var start = DateTime.UtcNow;
            while ((DateTime.UtcNow - start) < TimeSpan.FromSeconds(5))
            {
                if (lines.Count > 0) { break; }
                Thread.Sleep(10);
            }
            lines[0].ShouldEqual("a line");

            pipeline.Task.IsCompleted.ShouldEqual(false);
            pipeline.Kill();
            pipeline.Result.Success.ShouldEqual(false);
        }

        [TestMethod]
        public void TestVersioning()
        {
            var version = typeof(Command).GetTypeInfo().Assembly.GetName().Version.ToString();
            var informationalVersion = (AssemblyInformationalVersionAttribute)typeof(Command).GetTypeInfo().Assembly.GetCustomAttribute(typeof(AssemblyInformationalVersionAttribute));
            Assert.IsNotNull(informationalVersion);
            version.ShouldEqual(informationalVersion.InformationalVersion + ".0");
        }

        [TestMethod]
        public void TestShortFlush()
        {
            var command = Command.Run("SampleCommand", "shortflush", "a");
            var readCommand = command.StandardOutput.ReadBlockAsync(new char[1], 0, 1);
            //var readCommand = command.StandardOutput.BaseStream.ReadAsync(new byte[1], 0, 1);
            readCommand.Wait(TimeSpan.FromSeconds(5)).ShouldEqual(true);

            command.StandardInput.Dispose();
            command.Task.Wait(TimeSpan.FromSeconds(5)).ShouldEqual(true);
        }

        [TestMethod]
        public void TestAutoFlush()
        {
            var command = Command.Run("SampleCommand", "echo", "--per-char");
            command.StandardInput.AutoFlush.ShouldEqual(true);
            command.StandardInput.Write('a');

            var buffer = new char[1];
            var asyncRead = command.StandardOutput.ReadBlockAsync(buffer, 0, 1);
            asyncRead.Wait(TimeSpan.FromSeconds(3)).ShouldEqual(true);
            buffer[0].ShouldEqual('a');

            command.StandardInput.AutoFlush = false;
            command.StandardInput.Write('b');
            asyncRead = command.StandardOutput.ReadBlockAsync(buffer, 0, 1);
            asyncRead.Wait(TimeSpan.FromSeconds(.01)).ShouldEqual(false);
            command.StandardInput.Flush();
            asyncRead.Wait(TimeSpan.FromSeconds(3)).ShouldEqual(true);
            buffer[0].ShouldEqual('b');

            command.StandardInput.Dispose();
        }

        [TestMethod]
        public void TestErrorEcho()
        {
            var command = Command.Run("SampleCommand", "errecho") < "abc";
            command.Result.StandardError.ShouldEqual("abc");
        }

        [TestMethod]
        public void TestEncoding()
        {
            // pick a string that will be different in UTF8 vs the default to make sure we use the default
            var bytes = new byte[] { 255 };
            var inputEncoded = Console.InputEncoding.GetString(bytes);
            inputEncoded.ShouldEqual(Console.OutputEncoding.GetString(bytes));
            inputEncoded.ShouldNotEqual(Encoding.UTF8.GetString(bytes));
            var command = Command.Run("SampleCommand", "echo") < inputEncoded;
            command.Result.StandardOutput.ShouldEqual(inputEncoded);

            const string InternationalText = "漢字";
            command = Command.Run("SampleCommand", "echo") < InternationalText;
            command.Result.StandardOutput.ShouldEqual("??", "Default encoding does not support international chars");

            command = Command.Run("SampleCommand", new[] { "echo", "--utf8" }, options: o => o.Encoding(Encoding.UTF8)) < InternationalText;
            command.Result.StandardOutput.ShouldEqual(InternationalText);
        }

        private IEnumerable<string> ErrorLines()
        {
            yield return "1";
            throw new InvalidOperationException("Can't enumerate");
        }
    }
}
