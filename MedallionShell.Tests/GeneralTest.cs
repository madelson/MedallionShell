using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Medallion.Shell.Tests
{
    using System.Collections;
    using static UnitTestHelpers;

    public class GeneralTest
    {
        [Test]
        public void TestGrep()
        {
            var command = TestShell.Run(SampleCommand, "grep", "a+");
            command.StandardInput.WriteLine("hi");
            command.StandardInput.WriteLine("aa");
            command.StandardInput.Dispose();
            command.StandardOutput.ReadToEnd().ShouldEqual("aa" + Environment.NewLine, $"Exit code: {command.Result.ExitCode}, StdErr: '{command.Result.StandardError}'");
        }

        [Test]
        public void TestPipedGrep()
        {
            Log.WriteLine("******** TestPipedGrep starting *********");

            var command = TestShell.Run(SampleCommand, "grep", "a") < new[] { "abcd", "a", "ab", "abc" }
                | TestShell.Run(SampleCommand, "grep", "b")
                | TestShell.Run(SampleCommand, "grep", "c");

            var results = command.StandardOutput.GetLines().ToArray();

            results.SequenceEqual(new[] { "abcd", "abc" }).ShouldEqual(true);
        }

        [Test]
        public void TestLongWriteWithInfrequentReads()
        {
            var lines = Enumerable.Range(0, 100).Select(i => i.ToString())
                .ToArray();

            var command = TestShell.Run(SampleCommand, "grep", ".") < lines;
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

        [Test]
        public void TestHead()
        {
            var shell = MakeTestShell(o => o.StartInfo(si => si.RedirectStandardError = false));
            var command = shell.Run(SampleCommand, "head", "10") < Enumerable.Range(0, 100).Select(i => i.ToString());
            command.Task.Result.StandardOutput.Trim().ShouldEqual(string.Join(Environment.NewLine, Enumerable.Range(0, 10)));
        }

        [Test]
        public void TestCloseStandardOutput()
        {
            var shell = MakeTestShell(o => o.StartInfo(si => si.RedirectStandardError = false));
            var command = shell.Run(SampleCommand, "grep", "a") < Enumerable.Repeat(new string('a', 1000), 1000);
            command.StandardOutput.BaseStream.ReadByte();
            command.StandardOutput.BaseStream.Dispose();

            Assert.Throws<ObjectDisposedException>(() => command.StandardOutput.BaseStream.ReadByte());
            Assert.Throws<ObjectDisposedException>(() => command.StandardOutput.ReadToEnd());

            var command2 = shell.Run(SampleCommand, "grep", "a") < Enumerable.Repeat(new string('a', 1000), 1000);
            command2.Wait();
            command2.StandardOutput.Dispose();
            Assert.Throws<ObjectDisposedException>(() => command2.StandardOutput.Read());
        }

        [Test]
        public void TestExitCode()
        {
            Assert.IsTrue(TestShell.Run(SampleCommand, "exit", 0).Result.Success);
            Assert.IsFalse(TestShell.Run(SampleCommand, "exit", 1).Result.Success);

            var shell = MakeTestShell(o => o.ThrowOnError());
            var ex = Assert.Throws<AggregateException>(() => shell.Run(SampleCommand, "exit", -1).Task.Wait());
            ex.InnerExceptions.Select(e => e.GetType()).SequenceEqual(new[] { typeof(ErrorExitCodeException) })
                .ShouldEqual(true);

            shell.Run(SampleCommand, "exit", 0).Task.Wait();
        }

        [Test]
        public void TestThrowOnErrorWithTimeout()
        {
            var command = TestShell.Run(SampleCommand, new object[] { "exit", 1 }, o => o.ThrowOnError().Timeout(TimeSpan.FromDays(1)));
            var ex = Assert.Throws<AggregateException>(() => command.Task.Wait());
            ex.InnerExceptions.Select(e => e.GetType()).SequenceEqual(new[] { typeof(ErrorExitCodeException) })
                .ShouldEqual(true);
        }

        [Test]
        public void TestTimeout()
        {
            var willTimeout = TestShell.Run(SampleCommand, new object[] { "sleep", 1000000 }, o => o.Timeout(TimeSpan.FromMilliseconds(200)));
            var ex = Assert.Throws<AggregateException>(() => willTimeout.Task.Wait());
            Assert.IsInstanceOf<TimeoutException>(ex.InnerException);
        }

        [Test]
        public void TestZeroTimeout()
        {
            var willTimeout = TestShell.Run(SampleCommand, new object[] { "sleep", 1000000 }, o => o.Timeout(TimeSpan.Zero));
            var ex = Assert.Throws<AggregateException>(() => willTimeout.Task.Wait());
            Assert.IsInstanceOf<TimeoutException>(ex.InnerException);
        }

        [Test]
        public void TestCancellationAlreadyCanceled()
        {
            using (var alreadyCanceled = new CancellationTokenSource(millisecondsDelay: 0))
            {
                var command = TestShell.Run(SampleCommand, new object[] { "sleep", 1000000 }, o => o.CancellationToken(alreadyCanceled.Token));
                Assert.Throws<TaskCanceledException>(() => command.Wait());
                Assert.Throws<TaskCanceledException>(() => command.Result.ToString());
                command.Task.Status.ShouldEqual(TaskStatus.Canceled);
                Assert.DoesNotThrow(() => command.ProcessId.ToString(), "still executes a command and gets a process ID");
            }
        }

        [Test]
        public void TestCancellationNotCanceled()
        {
            using (var notCanceled = new CancellationTokenSource())
            {
                var command = TestShell.Run(SampleCommand, new object[] { "sleep", 1000000 }, o => o.CancellationToken(notCanceled.Token));
                command.Task.Wait(50).ShouldEqual(false);
                command.Kill();
                command.Task.Wait(1000).ShouldEqual(true);
                command.Result.Success.ShouldEqual(false);
            }
        }

        [Test]
        public void TestCancellationCanceledPartway()
        {
            using (var cancellationTokenSource = new CancellationTokenSource())
            {
                var results = new SyncCollection();
                var command = TestShell.Run(SampleCommand, new object[] { "echo", "--per-char" }, o => o.CancellationToken(cancellationTokenSource.Token)) > results;
                command.StandardInput.WriteLine("hello");
                var timeout = Task.Delay(TimeSpan.FromSeconds(10));
                while (results.Count == 0 && !timeout.IsCompleted) { }
                results.Count.ShouldEqual(1);
                cancellationTokenSource.Cancel();
                var aggregateException = Assert.Throws<AggregateException>(() => command.Task.Wait(1000));
                Assert.IsInstanceOf<TaskCanceledException>(aggregateException.GetBaseException());
                CollectionAssert.AreEqual(results, new[] { "hello" });
            }
        }

        private class SyncCollection : ICollection<string>
        {
            private readonly List<string> _list = new List<string>();

            private T WithLock<T>(Func<List<string>, T> func) { lock(this._list) { return func(this._list); } }
            private void WithLock(Action<List<string>> action) { lock(this._list) { action(this._list); } }

            public int Count => this.WithLock(l => l.Count);
            public bool IsReadOnly => false;
            public void Add(string item) => this.WithLock(l => l.Add(item));
            public void Clear() => this.WithLock(l => l.Clear());
            public bool Contains(string item) => this.WithLock(l => l.Contains(item));
            public void CopyTo(string[] array, int arrayIndex) => this.WithLock(l => l.CopyTo(array, arrayIndex));
            public IEnumerator<string> GetEnumerator() => this.WithLock(l => l.ToList()).GetEnumerator();
            public bool Remove(string item) => this.WithLock(l => l.Remove(item));
            IEnumerator IEnumerable.GetEnumerator() => this.GetEnumerator();
        }

        [Test]
        public void TestCancellationCanceledAfterCompletion()
        {
            using (var cancellationTokenSource = new CancellationTokenSource())
            {
                var results = new List<string>();
                var command = TestShell.Run(SampleCommand, new object[] { "echo" }, o => o.CancellationToken(cancellationTokenSource.Token)) > results;
                command.StandardInput.WriteLine("hello");
                command.StandardInput.Close();
                command.Task.Wait(1000).ShouldEqual(true);
                cancellationTokenSource.Cancel();
                command.Result.Success.ShouldEqual(true);
            }
        }

        [Test]
        public void TestCancellationWithTimeoutTimeoutWins()
        {
            var cancellationTokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var command = TestShell.Run(
                SampleCommand,
                new object[] { "sleep", 1000000 },
                o => o.CancellationToken(cancellationTokenSource.Token)
                    .Timeout(TimeSpan.FromMilliseconds(50))
            );
            Assert.Throws<TimeoutException>(() => command.Wait());
        }

        [Test]
        public void TestCancellationWithTimeoutCancellationWins()
        {
            var cancellationTokenSource = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
            var command = TestShell.Run(
                SampleCommand,
                new object[] { "sleep", 1000000 },
                o => o.CancellationToken(cancellationTokenSource.Token)
                    .Timeout(TimeSpan.FromSeconds(5))
            );
            Assert.Throws<TaskCanceledException>(() => command.Wait());
        }

        [Test]
        public void TestErrorHandling()
        {
            var command = TestShell.Run(SampleCommand, "echo") < "abc" > new char[0];
            Assert.Throws<NotSupportedException>(() => command.Wait());

            var command2 = TestShell.Run(SampleCommand, "echo") < this.ErrorLines();
            Assert.Throws<InvalidOperationException>(() => command2.Wait());
        }

        [Test]
        public void TestStopBufferingAndDiscard()
        {
            var command = TestShell.Run(SampleCommand, "pipe");
            command.StandardOutput.StopBuffering();
            var line = new string('a', 100);
            var state = 0;
            var linesWritten = 0;
            while (state < 2)
            {
                Log.WriteLine("Write to unbuffered command");
                var task = command.StandardInput.WriteLineAsync(line);
                if (!task.Wait(TimeSpan.FromSeconds(1)))
                {
                    if (state == 0)
                    {
                        Log.WriteLine("Buffer full: read");
                        // for whatever reason, on Unix I need to read a few lines to get things flowing again
                        var linesToRead = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? 1 : Math.Max((int)(.1 * linesWritten), 1);
                        for (var i = 0; i < linesToRead; ++i)
                        {
                            var outLine = command.StandardOutput.ReadLine();
                            outLine.ShouldEqual(line);
                        }
                        // after this, we may need to write more content than we read to re-block since the reader
                        // buffers internally
                    }
                    else
                    {
                        Log.WriteLine("Buffer full: discard content");
                        command.StandardOutput.Discard();
                    }

                    task.Wait(TimeSpan.FromSeconds(3)).ShouldEqual(true, $"can finish after read (state={state}, linesWritten={linesWritten})");
                    if (state == 1)
                    {
                        command.StandardInput.Dispose();
                    }
                    state++;
                }
                ++linesWritten;
            }
        }

        [Test]
        public void TestKill()
        {
            var command = TestShell.Run(SampleCommand, "pipe");
            command.StandardInput.WriteLine("abc");
            command.StandardInput.Flush();
            Thread.Sleep(300);
            command.Task.IsCompleted.ShouldEqual(false);

            command.Kill();
            command.Result.Success.ShouldEqual(false);
            // https://www.tldp.org/LDP/abs/html/exitcodes.html
            command.Result.ExitCode.ShouldEqual(RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? -1 : 137);

            command.StandardOutput.ReadLine().ShouldEqual("abc");
        }

        [Test]
        public void TestKillAfterFinished()
        {
            var command = TestShell.Run(SampleCommand, "bool", true, "something");
            command.Task.Wait();
            command.Kill();
            command.Result.Success.ShouldEqual(true);
        }

        [Test]
        public void TestNestedKill()
        {
            var lines = new SyncCollection();
            var pipeline = TestShell.Run(SampleCommand, "pipe")
                | TestShell.Run(SampleCommand, "pipe")
                | TestShell.Run(SampleCommand, "pipe") > lines;

            // demonstrate that a single line can make it all the way through the pipeline
            // without getting caught in a buffer along the way
            pipeline.StandardInput.AutoFlush.ShouldEqual(true);
            pipeline.StandardInput.WriteLine("a line");
            var start = DateTime.UtcNow;
            while ((DateTime.UtcNow - start) < TimeSpan.FromSeconds(10))
            {
                if (lines.Count > 0) { break; }
                Thread.Sleep(10);
            }
            pipeline.Task.IsCompleted.ShouldEqual(false);
            lines.FirstOrDefault().ShouldEqual("a line");

            pipeline.Task.IsCompleted.ShouldEqual(false);
            pipeline.Kill();
            pipeline.Result.Success.ShouldEqual(false);
        }

        [Test]
        public void TestVersioning()
        {
            var version = typeof(Command).GetTypeInfo().Assembly.GetName().Version.ToString();
            var informationalVersion = (AssemblyInformationalVersionAttribute)typeof(Command).GetTypeInfo().Assembly.GetCustomAttribute(typeof(AssemblyInformationalVersionAttribute));
            Assert.IsNotNull(informationalVersion);
            version.ShouldEqual(informationalVersion.InformationalVersion + ".0");
        }

        [Test]
        public void TestShortFlush()
        {
            var command = TestShell.Run(SampleCommand, "shortflush", "a");
            var readCommand = command.StandardOutput.ReadBlockAsync(new char[1], 0, 1);
            readCommand.Wait(TimeSpan.FromSeconds(5)).ShouldEqual(true);

            command.StandardInput.Dispose();
            command.Task.Wait(TimeSpan.FromSeconds(5)).ShouldEqual(true);
        }

        [Test]
        public void TestAutoFlush()
        {
            var command = TestShell.Run(SampleCommand, "echo", "--per-char");
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

        [Test]
        public void TestErrorEcho()
        {
            var command = TestShell.Run(SampleCommand, "errecho") < "abc";
            command.Result.StandardError.ShouldEqual("abc");
        }

        [Test]
        public void TestEncoding()
        {
            // pick a string that will be different in UTF8 vs the default to make sure we use the default
            var bytes = new byte[] { 255 };
            var inputEncoded = Console.InputEncoding.GetString(bytes);
            inputEncoded.ShouldEqual(Console.OutputEncoding.GetString(bytes)); // sanity check
            // mono and .NET Core will default to UTF8
            var defaultsToUtf8 = Console.InputEncoding.WebName == Encoding.UTF8.WebName;
            if (!defaultsToUtf8)
            {
                inputEncoded.ShouldNotEqual(Encoding.UTF8.GetString(bytes), $"Matched with {Console.InputEncoding.WebName}"); // sanity check
            }
            var command = TestShell.Run(SampleCommand, "echo") < inputEncoded;
            command.Result.StandardOutput.ShouldEqual(inputEncoded);

            const string InternationalText = "漢字";
            command = TestShell.Run(SampleCommand, "echo") < InternationalText;
            if (defaultsToUtf8)
            {
                command.Result.StandardOutput.ShouldEqual(InternationalText, $"Default encoding should support international chars");
            }
            else
            {
                command.Result.StandardOutput.ShouldEqual("??", "Default encoding does not support international chars");
            }

            var utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
            command = TestShell.Run(SampleCommand, new[] { "echo", "--utf8" }, options: o => o.Encoding(utf8NoBom)) < InternationalText;
            command.Result.StandardOutput.ShouldEqual(
                InternationalText, 
                $"UTF8 encoding should support international chars: Expected bytes [{string.Join(", ", utf8NoBom.GetBytes(InternationalText))}]. Received [{string.Join(", ", utf8NoBom.GetBytes(command.Result.StandardOutput))}]"
            );

            // since some platforms use UTF8 by default, also echo test with UTF16
            var unicodeNoBom = new UnicodeEncoding(bigEndian: false, byteOrderMark: false);
            command = TestShell.Run(SampleCommand, new[] { "echo", "--utf16" }, options: o => o.Encoding(unicodeNoBom));
            command.StandardInput.Encoding.ShouldEqual(unicodeNoBom);
            command.StandardOutput.Encoding.ShouldEqual(unicodeNoBom);
            command.StandardError.Encoding.ShouldEqual(unicodeNoBom);
            (command < InternationalText).Result.StandardOutput.ShouldEqual(InternationalText, "UTF16 should support international chars");
        }

        [Test]
        public void TestGetOutputAndErrorLines()
        {
            // simple echo case
            var command = TestShell.Run(SampleCommand, "echo") < new[] { "a", "b", "c" };
            string.Join(", ", command.GetOutputAndErrorLines().ToList()).ShouldEqual("a, b, c");

            // failure case: stderr not redirected
            command = TestShell.Run(SampleCommand, new[] { "echo" }, options: o => o.StartInfo(s => s.RedirectStandardError = false)) < new[] { "a" };
            Assert.Throws<InvalidOperationException>(() => command.GetOutputAndErrorLines());

            // fuzz case
            var lines = Enumerable.Range(0, 5000).Select(_ => Guid.NewGuid().ToString()).ToArray();
            command = TestShell.Run(SampleCommand, "echoLinesToBothStreams") < lines;
            var outputLines = command.GetOutputAndErrorLines().ToList();
            CollectionAssert.AreEquivalent(lines, outputLines);
        }

        [Test]
        public void TestProcessAndProcessId()
        {
            void TestHelper(bool disposeOnExit)
            {
                var shell = MakeTestShell(o => o.DisposeOnExit(disposeOnExit));
                var command1 = shell.Run(SampleCommand, "pipe", "--id1");
                var command2 = shell.Run(SampleCommand, "pipe", "--id2");
                var pipeCommand = command1.PipeTo(command2);
                try
                {
                    if (disposeOnExit)
                    {
                        // invalid due to DisposeOnExit()
                        Assert.Throws<InvalidOperationException>(() => command1.Process.ToString())
                            .Message.ShouldContain("dispose on exit");
                        Assert.Throws<InvalidOperationException>(() => command2.Processes.Count())
                            .Message.ShouldContain("dispose on exit");
                        Assert.Throws<InvalidOperationException>(() => pipeCommand.Processes.Count())
                            .Message.ShouldContain("dispose on exit");
                    }
                    else
                    {
                        command1.Process.StartInfo.Arguments.ShouldContain("--id1");
                        command1.Processes.SequenceEqual(new[] { command1.Process });
                        command2.Process.StartInfo.Arguments.ShouldContain("--id2");
                        command2.Processes.SequenceEqual(new[] { command2.Process }).ShouldEqual(true);
                        pipeCommand.Process.ShouldEqual(command2.Process);
                        pipeCommand.Processes.SequenceEqual(new[] { command1.Process, command2.Process }).ShouldEqual(true);
                    }

#if !NETCOREAPP2_2
                    // https://stackoverflow.com/questions/2633628/can-i-get-command-line-arguments-of-other-processes-from-net-c
                    string GetCommandLine(int processId)
                    {
                        using (var searcher = new System.Management.ManagementObjectSearcher("SELECT CommandLine FROM Win32_Process WHERE ProcessId = " + processId))
                        {
                            return searcher.Get().Cast<System.Management.ManagementBaseObject>().Single()["CommandLine"].ToString();
                        }
                    }
                    if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    {
                        GetCommandLine(command1.ProcessId).ShouldContain("--id1");
                        GetCommandLine(command2.ProcessId).ShouldContain("--id2");
                    }
#endif
                    command1.ProcessIds.SequenceEqual(new[] { command1.ProcessId }).ShouldEqual(true);
                    command2.ProcessIds.SequenceEqual(new[] { command2.ProcessId }).ShouldEqual(true);
                    pipeCommand.ProcessId.ShouldEqual(command2.ProcessId);
                    pipeCommand.ProcessIds.SequenceEqual(new[] { command1.ProcessId, command2.ProcessId }).ShouldEqual(true);
                }
                finally
                {
                    command1.RedirectFrom(new[] { "data" });
                    pipeCommand.Wait();
                }
            }

            TestHelper(disposeOnExit: true);
            TestHelper(disposeOnExit: false);
        }

        [Test]
        public void TestToString()
        {
            var sampleCommandString =
#if NETCOREAPP2_2
                $@"{DotNetPath} {SampleCommand}";
#else
                SampleCommand;
#endif

            var command0 = TestShell.Run(SampleCommand, new[] { "grep", "a+" }, options => options.DisposeOnExit(true));
            command0.ToString().ShouldEqual($"{sampleCommandString} grep a+");

            var command1 = TestShell.Run(SampleCommand, "exit", 0);
            command1.ToString().ShouldEqual($"{sampleCommandString} exit 0");

            var command2 = TestShell.Run(SampleCommand, "ex it", "0 0");
            command2.ToString().ShouldEqual($"{sampleCommandString} \"ex it\" \"0 0\"");

            var command3 = command1 < new[] { "a" };
            command3.ToString().ShouldEqual($"{sampleCommandString} exit 0 < System.String[]");

            var command4 = command3 | TestShell.Run(SampleCommand, "echo");
            command4.ToString().ShouldEqual($"{sampleCommandString} exit 0 < System.String[] | {sampleCommandString} echo");

            var command5 = command2.RedirectStandardErrorTo(Stream.Null);
            command5.ToString().ShouldEqual($"{sampleCommandString} \"ex it\" \"0 0\" 2> {Stream.Null}");

            var command6 = command5.RedirectTo(new StringWriter());
            command6.Wait();
            command6.ToString().ShouldEqual($"{command5} > {new StringWriter()}");
        }

        [Test]
        public void TestCommandOption()
        {
            var command = TestShell.Run(SampleCommand, new[] { "echo" }, options: o => o.Command(c => c.StandardInput.Write("!!!")))
                .RedirectFrom("abc");
            command.Wait();
            command.Result.StandardOutput.ShouldEqual("!!!abc");

            var writer = new StringWriter();
            command = TestShell.Run(SampleCommand, new[] { "echo" }, options: o => o.Command(c => c.RedirectTo(writer)))
                .RedirectFrom("abc123");
            command.Wait();
            writer.ToString().ShouldEqual("abc123");
        }

        [Test]
        public void TestProcessKeepsWritingAfterOutputIsClosed()
        {
            var command = TestShell.Run(SampleCommand, new[] { "pipe" });
            command.StandardOutput.Dispose();
            for (var i = 0; i < 100; ++i)
            {
                command.StandardInput.WriteLine(new string('a', i));
            }

            // workaround for https://github.com/mono/mono/issues/18279; so far
            // I've encountered this only on Mono Linux
            if (Type.GetType("Mono.Runtime") != null
                && !RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                command.StandardInput.Dispose();
                command.Task.Wait(TimeSpan.FromSeconds(1000)).ShouldEqual(true);
                command.Result.ExitCode.ShouldEqual(1);
                // SampleCommand fails because it's attempt to write to Console.Out fails hard
                Assert.That(command.Result.StandardError, Does.Contain("System.IO.IOException: Write fault"));
            }
            else
            {
                command.Task.IsCompleted.ShouldEqual(false);

                command.StandardInput.Dispose();
                command.Task.Wait(TimeSpan.FromSeconds(1000)).ShouldEqual(true);
                command.Result.Success.ShouldEqual(Type.GetType("Mono.Runtime") == null);
            }
        }

        private IEnumerable<string> ErrorLines()
        {
            yield return "1";
            throw new InvalidOperationException("Can't enumerate");
        }
    }
}
