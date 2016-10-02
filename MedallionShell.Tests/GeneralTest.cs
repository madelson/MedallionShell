using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Medallion.Shell.Tests
{
    public class GeneralTest
    {
        [Fact]
        public void TestGrep()
        {
            var command = Shell.Default.Run("SampleCommand", "grep", "a+");
            command.StandardInput.WriteLine("hi");
            command.StandardInput.WriteLine("aa");
            command.StandardInput.Dispose();
            command.StandardOutput.ReadToEnd().ShouldEqual("aa\r\n");
        }

        [Fact]
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

        [Fact]
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

        [Fact]
        public void TestHead()
        {
            var shell = new Shell(o => o.StartInfo(si => si.RedirectStandardError = false));
            var command = shell.Run("SampleCommand", "head", "10") < Enumerable.Range(0, 100).Select(i => i.ToString());
            command.Task.Result.StandardOutput.Trim().ShouldEqual(string.Join(Environment.NewLine, Enumerable.Range(0, 10)));
        }

        [Fact]
        public void TestCloseStandardOutput()
        {
            var shell = new Shell(o => o.StartInfo(si => si.RedirectStandardError = false));
            var command = shell.Run("SampleCommand", "grep", "a") < Enumerable.Repeat(new string('a', 1000), 1000);
            command.StandardOutput.BaseStream.ReadByte();
            command.StandardOutput.BaseStream.Dispose();
            
            Assert.Throws<ObjectDisposedException>(() => command.StandardOutput.BaseStream.ReadByte());
            Assert.Throws<ObjectDisposedException>(() => command.StandardOutput.ReadToEnd());

            var command2 = shell.Run("SampleCommand", "grep", "a") < Enumerable.Repeat(new string('a', 1000), 1000);
            command2.Wait();
            command2.StandardOutput.Dispose();
            Assert.Throws<ObjectDisposedException>(() => command2.StandardOutput.Read());
        }

        [Fact]
        public void TestExitCode()
        {
            Assert.True(Command.Run("SampleCommand", "exit", 0).Result.Success);
            Assert.False(Command.Run("SampleCommand", "exit", 1).Result.Success);

            var shell = new Shell(o => o.ThrowOnError());
            var ex = Assert.Throws<AggregateException>(() => shell.Run("SampleCommand", "exit", -1).Task.Wait());
            ex.InnerExceptions.Select(e => e.GetType()).SequenceEqual(new[] { typeof(ErrorExitCodeException) })
                .ShouldEqual(true);

            shell.Run("SampleCommand", "exit", 0).Task.Wait();
        }

        [Fact]
        public void TestThrowOnErrorWithTimeout()
        {
            var command = Command.Run("SampleCommand", new object[] { "exit", 1 }, o => o.ThrowOnError().Timeout(TimeSpan.FromDays(1)));
            var ex = Assert.Throws<AggregateException>(() => command.Task.Wait());
            ex.InnerExceptions.Select(e => e.GetType()).SequenceEqual(new[] { typeof(ErrorExitCodeException) })
                .ShouldEqual(true);
        }

        [Fact]
        public void TestTimeout()
        {
            var willTimeout = Command.Run("SampleCommand", new object[] { "sleep", 1000000 }, o => o.Timeout(TimeSpan.FromMilliseconds(200)));
            var ex = Assert.Throws<AggregateException>(() => willTimeout.Task.Wait());
            Assert.IsType<TimeoutException>(ex.InnerException);
        }

        [Fact]
        public void TestErrorHandling()
        {
            var command = Command.Run("SampleCommand", "echo") < "abc" > new char[0];
            Assert.Throws<AggregateException>(() => command.Wait());

            var command2 = Command.Run("SampleCommand", "echo") < this.ErrorLines();
            Assert.Throws<AggregateException>(() => command.Wait());
        }

        [Fact]
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

        [Fact]
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

        [Fact]
        public void TestKillAfterFinished()
        {
            var command = Command.Run("SampleCommand", "bool", true, "something");
            command.Task.Wait();
            command.Kill();
            command.Result.Success.ShouldEqual(true);
        }

        [Fact]
        public void TestNestedKill()
        {
            var lines = new List<string>();
            var pipeline = Command.Run("SampleCommand", "pipe")
                | Command.Run("SampleCommand", "pipe")
                | Command.Run("SampleCommand", "pipe") > lines;
            pipeline.StandardInput.WriteLine("a line");
            pipeline.StandardInput.Flush();
            Thread.Sleep(100);
            pipeline.Task.IsCompleted.ShouldEqual(false);
            
            pipeline.Kill();
            pipeline.Result.Success.ShouldEqual(false);
            // This doesn't work right now due to our lack of flushing. There's not enought data so the single line gets caught
            // between pipes
            Assert.Throws<ArgumentOutOfRangeException>(() => lines[0].ShouldEqual("a line"));
        }

        [Fact]
        public void TestVersioning()
        {
            var version = typeof(Command).GetTypeInfo().Assembly.GetName().Version.ToString();
            var informationalVersion = (AssemblyInformationalVersionAttribute)typeof(Command).GetTypeInfo().Assembly.GetCustomAttribute(typeof(AssemblyInformationalVersionAttribute));
            Assert.NotNull(informationalVersion);
            version.ShouldEqual(informationalVersion.InformationalVersion + ".0");
        }

        [Fact]
        public void TestShortFlush()
        {
            var command = Command.Run("SampleCommand", "shortflush", "a");
            var readCommand = command.StandardOutput.ReadBlockAsync(new char[1], 0, 1);
            //var readCommand = command.StandardOutput.BaseStream.ReadAsync(new byte[1], 0, 1);
            readCommand.Wait(TimeSpan.FromSeconds(5)).ShouldEqual(true);

            command.StandardInput.Dispose();
            command.Task.Wait(TimeSpan.FromSeconds(5)).ShouldEqual(true);
        }

        [Fact]
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

        [Fact]
        public void TestErrorEcho()
        {
            var command = Command.Run("SampleCommand", "errecho") < "abc";
            command.Result.StandardError.ShouldEqual("abc");
        }

        private IEnumerable<string> ErrorLines()
        {
            yield return "1";
            throw new InvalidOperationException("Can't enumerate");
        }
    }
}
