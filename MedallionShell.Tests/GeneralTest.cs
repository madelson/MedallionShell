using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Medallion.Shell.Tests
{
    using System.Runtime.InteropServices;
    using static UnitTestHelpers;

    [NUnit.Framework.Timeout(10000)]
    public class GeneralTest
    {
        //[Test]
        //public void TestGrepLinux()
        //{
        //    Console.WriteLine($"{SampleCommand} {File.Exists(SampleCommand)} {RuntimeInformation.IsOSPlatform(OSPlatform.Linux)} {File.Exists("/usr/bin/mono")}");
        //    //Assert.Fail($"{SampleCommand} {File.Exists(SampleCommand)} {RuntimeInformation.IsOSPlatform(OSPlatform.Linux)} {File.Exists("/usr/bin/mono")}");

        //    var proc = new System.Diagnostics.Process
        //    {
        //        StartInfo =
        //        {
        //            FileName = "/usr/bin/mono",
        //            Arguments = $"{SampleCommand} exit 0",
        //            UseShellExecute = false,
        //            RedirectStandardInput = false,
        //            RedirectStandardOutput = true,
        //            RedirectStandardError = true,
        //            CreateNoWindow = true,
        //        },
        //    };
        //    proc.Start();
        //    var readStdOut = Task.Run(() => proc.StandardOutput.ReadToEndAsync());
        //    var readStdErr = Task.Run(() => proc.StandardError.ReadToEndAsync());
        //    if (!proc.WaitForExit(30000))
        //    {
        //        Console.WriteLine("Did not exit; killing");
        //        proc.Kill();
        //    }
        //    if (!readStdOut.Wait(1000)) { Console.WriteLine("STDOUT did not finish"); }
        //    else { Console.WriteLine("STDOUT: " + readStdOut.Result); }
        //    if (!readStdErr.Wait(1000)) { Console.WriteLine("STDERR did not finish"); }
        //    else { Console.WriteLine("STDERR: " + readStdErr.Result); }

        //    //var command = Shell.Default.Run("/usr/bin/mono", SampleCommand, "grep", "a+");
        //    //command.StandardInput.WriteLine("hi");
        //    //command.StandardInput.WriteLine("aa");
        //    //command.StandardInput.Dispose();
        //    //command.StandardOutput.ReadToEnd().ShouldEqual("aa\r\n");
        //}

        //[Test]
        //public void TestRawLinuxGrep()
        //{
        //    var proc = new System.Diagnostics.Process
        //    {
        //        StartInfo =
        //        {
        //            FileName = "/usr/bin/mono",
        //            Arguments = $"{SampleCommand} grep a+",
        //            UseShellExecute = false,
        //            RedirectStandardInput = true,
        //            RedirectStandardOutput = true,
        //            RedirectStandardError = true,
        //            CreateNoWindow = true,
        //        },
        //    };
        //    proc.Start();
        //    var readStdOut = Task.Run(() => proc.StandardOutput.ReadToEndAsync());
        //    var readStdErr = Task.Run(() => proc.StandardError.ReadToEndAsync());
        //    proc.StandardInput.WriteLine("hi");
        //    proc.StandardInput.WriteLine("aa");
        //    proc.StandardInput.Dispose();
        //    if (!proc.WaitForExit(30000))
        //    {
        //        Console.WriteLine("Did not exit; killing");
        //        proc.Kill();
        //    }
        //    if (!readStdOut.Wait(1000)) { Console.WriteLine("STDOUT did not finish"); }
        //    else { Console.WriteLine("STDOUT: " + readStdOut.Result); }
        //    if (!readStdErr.Wait(1000)) { Console.WriteLine("STDERR did not finish"); }
        //    else { Console.WriteLine("STDERR: " + readStdErr.Result); }
        //}

        //[Test]
        //public void TestExitLinux()
        //{
        //    var command = Shell.Default.Run("/usr/bin/mono", new object[] { SampleCommand, "exit", 0 });
        //    if (!command.Task.Wait(10000))
        //    {
        //        Console.WriteLine("Did not exit: killing");
        //        command.Kill();
        //    }
        //    Console.WriteLine(command.Task.Wait(1000));
        //}

        //[Test]
        //[Timeout(10000)]
        //public void TestErrorEchoLinux()
        //{
        //    var command = Command.Run("/usr/bin/mono", new[] { SampleCommand, "errecho" }, options: o => o.DisposeOnExit(false)) < "abc";
        //    Console.WriteLine("Created command");
        //    Console.WriteLine($"Process exited: {command.Process.WaitForExit(3000)}");
        //    command.Result.StandardError.ShouldEqual("abc");
        //}

        //[Test]
        //[Timeout(10000)]
        //public void TestRawErrorEchoLinux()
        //{
        //    var proc = new System.Diagnostics.Process
        //    {
        //        StartInfo =
        //        {
        //            FileName = "/usr/bin/mono",
        //            Arguments = $"{SampleCommand} errecho",
        //            UseShellExecute = false,
        //            RedirectStandardInput = true,
        //            RedirectStandardOutput = true,
        //            RedirectStandardError = true,
        //            CreateNoWindow = true,
        //        },
        //    };
        //    proc.Start();
        //    var readStdOut = Task.Run(() => proc.StandardOutput.ReadToEndAsync());
        //    var readStdErr = Task.Run(async () =>
        //    {
        //        var bytes = new MemoryStream();
        //        var buffer = new byte[Constants.ByteBufferSize];
        //        int bytesRead;
        //        while ((bytesRead = await proc.StandardError.BaseStream.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false)) > 0)
        //        {
        //            Console.WriteLine($"Read {bytesRead} bytes; writing to pipe");
        //            bytes.Write(buffer, 0, bytesRead);
        //        }
        //        bytes.Position = 0;
        //        return new StreamReader(bytes).ReadToEnd();
        //    });
        //    proc.StandardInput.Write("abc");
        //    proc.StandardInput.Dispose();
        //    if (!proc.WaitForExit(5000))
        //    {
        //        Console.WriteLine("Did not exit; killing");
        //        proc.Kill();
        //    }
        //    if (!readStdOut.Wait(1000)) { Console.WriteLine("STDOUT did not finish"); }
        //    else { Console.WriteLine("STDOUT: " + readStdOut.Result); }
        //    if (!readStdErr.Wait(1000)) { Console.WriteLine("STDERR did not finish"); }
        //    else { Console.WriteLine("STDERR: " + readStdErr.Result); }
        //}

        //[Test]
        //[Timeout(10000)]
        //public void TestArgEchoLinux()
        //{
        //    var command = Command.Run("/usr/bin/mono", SampleCommand, "argecho", "hello", "world");
        //    Console.WriteLine("Created command");
        //    Console.WriteLine("STDOUT: " + command.StandardOutput.ReadToEnd());
        //}

        [Test]
        public void TestGrep()
        {
            var command = Shell.Default.Run(SampleCommand, "grep", "a+");
            command.StandardInput.WriteLine("hi");
            command.StandardInput.WriteLine("aa");
            command.StandardInput.Dispose();
            command.StandardOutput.ReadToEnd().ShouldEqual("aa\r\n");
        }

        //[Test]
        //public void TestPipedGrep()
        //{
        //    Log.WriteLine("******** TestPipedGrep starting *********");

        //    var command = Command.Run(SampleCommand, "grep", "a") < new[] { "abcd", "a", "ab", "abc" }
        //        | Command.Run(SampleCommand, "grep", "b")
        //        | Command.Run(SampleCommand, "grep", "c");

        //    var results = command.StandardOutput.GetLines().ToArray();

        //    results.SequenceEqual(new[] { "abcd", "abc" }).ShouldEqual(true);
        //}

        //[Test]
        //public void TestLongWriteWithInfrequentReads()
        //{
        //    var lines = Enumerable.Range(0, 100).Select(i => i.ToString())
        //        .ToArray();

        //    var command = Command.Run(SampleCommand, "grep", ".") < lines;
        //    var outputLines = new List<string>();
        //    var readTask = Task.Run(() =>
        //    {
        //        var rand = new Random(12345);
        //        while (true)
        //        {
        //            if (rand.Next(10) == 0)
        //            {
        //                Thread.Sleep(200);
        //            }
        //            else
        //            {
        //                var line = command.StandardOutput.ReadLine();
        //                if (line == null)
        //                {
        //                    return;
        //                }
        //                else
        //                {
        //                    outputLines.Add(line);
        //                }
        //            }
        //        }
        //    });

        //    Task.WaitAll(command.Task, readTask);

        //    string.Join("/", outputLines).ShouldEqual(string.Join("/", lines));
        //}

        //[Test]
        //public void TestHead()
        //{
        //    var shell = new Shell(o => o.StartInfo(si => si.RedirectStandardError = false));
        //    var command = shell.Run(SampleCommand, "head", "10") < Enumerable.Range(0, 100).Select(i => i.ToString());
        //    command.Task.Result.StandardOutput.Trim().ShouldEqual(string.Join(Environment.NewLine, Enumerable.Range(0, 10)));
        //}

        //[Test]
        //public void TestCloseStandardOutput()
        //{
        //    var shell = new Shell(o => o.StartInfo(si => si.RedirectStandardError = false));
        //    var command = shell.Run(SampleCommand, "grep", "a") < Enumerable.Repeat(new string('a', 1000), 1000);
        //    command.StandardOutput.BaseStream.ReadByte();
        //    command.StandardOutput.BaseStream.Dispose();

        //    Assert.Throws<ObjectDisposedException>(() => command.StandardOutput.BaseStream.ReadByte());
        //    Assert.Throws<ObjectDisposedException>(() => command.StandardOutput.ReadToEnd());

        //    var command2 = shell.Run(SampleCommand, "grep", "a") < Enumerable.Repeat(new string('a', 1000), 1000);
        //    command2.Wait();
        //    command2.StandardOutput.Dispose();
        //    Assert.Throws<ObjectDisposedException>(() => command2.StandardOutput.Read());
        //}

        //[Test]
        //public void TestExitCode()
        //{
        //    Assert.IsTrue(Command.Run(SampleCommand, "exit", 0).Result.Success);
        //    Assert.IsFalse(Command.Run(SampleCommand, "exit", 1).Result.Success);

        //    var shell = new Shell(o => o.ThrowOnError());
        //    var ex = Assert.Throws<AggregateException>(() => shell.Run(SampleCommand, "exit", -1).Task.Wait());
        //    ex.InnerExceptions.Select(e => e.GetType()).SequenceEqual(new[] { typeof(ErrorExitCodeException) })
        //        .ShouldEqual(true);

        //    shell.Run(SampleCommand, "exit", 0).Task.Wait();
        //}

        //[Test]
        //public void TestThrowOnErrorWithTimeout()
        //{
        //    var command = Command.Run(SampleCommand, new object[] { "exit", 1 }, o => o.ThrowOnError().Timeout(TimeSpan.FromDays(1)));
        //    var ex = Assert.Throws<AggregateException>(() => command.Task.Wait());
        //    ex.InnerExceptions.Select(e => e.GetType()).SequenceEqual(new[] { typeof(ErrorExitCodeException) })
        //        .ShouldEqual(true);
        //}

        //[Test]
        //public void TestTimeout()
        //{
        //    var willTimeout = Command.Run(SampleCommand, new object[] { "sleep", 1000000 }, o => o.Timeout(TimeSpan.FromMilliseconds(200)));
        //    var ex = Assert.Throws<AggregateException>(() => willTimeout.Task.Wait());
        //    Assert.IsInstanceOf<TimeoutException>(ex.InnerException);
        //}

        //[Test]
        //public void TestZeroTimeout()
        //{
        //    var willTimeout = Command.Run(SampleCommand, new object[] { "sleep", 1000000 }, o => o.Timeout(TimeSpan.Zero));
        //    var ex = Assert.Throws<AggregateException>(() => willTimeout.Task.Wait());
        //    Assert.IsInstanceOf<TimeoutException>(ex.InnerException);
        //}

        //[Test]
        //public void TestCancellationAlreadyCanceled()
        //{
        //    using (var alreadyCanceled = new CancellationTokenSource(millisecondsDelay: 0))
        //    {
        //        var command = Command.Run(SampleCommand, new object[] { "sleep", 1000000 }, o => o.CancellationToken(alreadyCanceled.Token));
        //        Assert.Throws<TaskCanceledException>(() => command.Wait());
        //        Assert.Throws<TaskCanceledException>(() => command.Result.ToString());
        //        command.Task.Status.ShouldEqual(TaskStatus.Canceled);
        //        Assert.DoesNotThrow(() => command.ProcessId.ToString(), "still executes a command and gets a process ID");
        //    }
        //}

        //[Test]
        //public void TestCancellationNotCanceled()
        //{
        //    using (var notCanceled = new CancellationTokenSource())
        //    {
        //        var command = Command.Run(SampleCommand, new object[] { "sleep", 1000000 }, o => o.CancellationToken(notCanceled.Token));
        //        command.Task.Wait(50).ShouldEqual(false);
        //        command.Kill();
        //        command.Task.Wait(1000).ShouldEqual(true);
        //        command.Result.Success.ShouldEqual(false);
        //    }
        //}

        //[Test]
        //public void TestCancellationCanceledPartway()
        //{
        //    using (var cancellationTokenSource = new CancellationTokenSource())
        //    {
        //        var results = new SynchronizedCollection<string>();
        //        var command = Command.Run(SampleCommand, new object[] { "echo", "--per-char" }, o => o.CancellationToken(cancellationTokenSource.Token)) > results;
        //        command.StandardInput.WriteLine("hello");
        //        var timeout = Task.Delay(TimeSpan.FromSeconds(10));
        //        while (results.Count == 0 && !timeout.IsCompleted) { }
        //        results.Count.ShouldEqual(1);
        //        cancellationTokenSource.Cancel();
        //        var aggregateException = Assert.Throws<AggregateException>(() => command.Task.Wait(1000));
        //        Assert.IsInstanceOf<TaskCanceledException>(aggregateException.GetBaseException());
        //        CollectionAssert.AreEqual(results, new[] { "hello" });
        //    }
        //}

        //[Test]
        //public void TestCancellationCanceledAfterCompletion()
        //{
        //    using (var cancellationTokenSource = new CancellationTokenSource())
        //    {
        //        var results = new List<string>();
        //        var command = Command.Run(SampleCommand, new object[] { "echo" }, o => o.CancellationToken(cancellationTokenSource.Token)) > results;
        //        command.StandardInput.WriteLine("hello");
        //        command.StandardInput.Close();
        //        command.Task.Wait(1000).ShouldEqual(true);
        //        cancellationTokenSource.Cancel();
        //        command.Result.Success.ShouldEqual(true);
        //    }
        //}

        //[Test]
        //public void TestCancellationWithTimeoutTimeoutWins()
        //{
        //    var cancellationTokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        //    var command = Command.Run(
        //        SampleCommand,
        //        new object[] { "sleep", 1000000 },
        //        o => o.CancellationToken(cancellationTokenSource.Token)
        //            .Timeout(TimeSpan.FromMilliseconds(50))
        //    );
        //    Assert.Throws<TimeoutException>(() => command.Wait());
        //}

        //[Test]
        //public void TestCancellationWithTimeoutCancellationWins()
        //{
        //    var cancellationTokenSource = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        //    var command = Command.Run(
        //        SampleCommand,
        //        new object[] { "sleep", 1000000 },
        //        o => o.CancellationToken(cancellationTokenSource.Token)
        //            .Timeout(TimeSpan.FromSeconds(5))
        //    );
        //    Assert.Throws<TaskCanceledException>(() => command.Wait());
        //}

        //[Test]
        //public void TestErrorHandling()
        //{
        //    var command = Command.Run(SampleCommand, "echo") < "abc" > new char[0];
        //    Assert.Throws<NotSupportedException>(() => command.Wait());

        //    var command2 = Command.Run(SampleCommand, "echo") < this.ErrorLines();
        //    Assert.Throws<InvalidOperationException>(() => command2.Wait());
        //}

        //[Test]
        //public void TestStopBufferingAndDiscard()
        //{
        //    var command = Command.Run(SampleCommand, "pipe");
        //    command.StandardOutput.StopBuffering();
        //    var line = new string('a', 100);
        //    var state = 0;
        //    while (state < 2)
        //    {
        //        Log.WriteLine("Write to unbuffered command");
        //        var task = command.StandardInput.WriteLineAsync(line);
        //        if (!task.Wait(TimeSpan.FromSeconds(1)))
        //        {
        //            if (state == 0)
        //            {
        //                Log.WriteLine("Buffer full: read one line");
        //                var outLine = command.StandardOutput.ReadLine();
        //                outLine.ShouldEqual(line);
        //                // after this, we need to read more than one line to re-block since the readers buffer internally
        //            }
        //            else
        //            {
        //                Log.WriteLine("Buffer full: discard content");
        //                command.StandardOutput.Discard();
        //            }

        //            task.Wait(TimeSpan.FromSeconds(.5)).ShouldEqual(true, "can finish after read");
        //            if (state == 1)
        //            {
        //                command.StandardInput.Dispose();
        //            }
        //            state++;
        //        }
        //    }
        //}

        //[Test]
        //public void TestKill()
        //{
        //    var command = Command.Run(SampleCommand, "pipe");
        //    command.StandardInput.WriteLine("abc");
        //    command.StandardInput.Flush();
        //    Thread.Sleep(100);
        //    command.Task.IsCompleted.ShouldEqual(false);

        //    command.Kill();
        //    command.Result.Success.ShouldEqual(false);
        //    command.Result.ExitCode.ShouldEqual(-1);

        //    command.StandardOutput.ReadLine().ShouldEqual("abc");
        //}

        //[Test]
        //public void TestKillAfterFinished()
        //{
        //    var command = Command.Run(SampleCommand, "bool", true, "something");
        //    command.Task.Wait();
        //    command.Kill();
        //    command.Result.Success.ShouldEqual(true);
        //}

        //[Test]
        //public void TestNestedKill()
        //{
        //    var lines = new List<string>();
        //    var pipeline = Command.Run(SampleCommand, "pipe")
        //        | Command.Run(SampleCommand, "pipe")
        //        | Command.Run(SampleCommand, "pipe") > lines;

        //    // demonstrate that a single line can make it all the way through the pipeline
        //    // without getting caught in a buffer along the way
        //    pipeline.StandardInput.WriteLine("a line");
        //    var start = DateTime.UtcNow;
        //    while ((DateTime.UtcNow - start) < TimeSpan.FromSeconds(5))
        //    {
        //        if (lines.Count > 0) { break; }
        //        Thread.Sleep(10);
        //    }
        //    lines[0].ShouldEqual("a line");

        //    pipeline.Task.IsCompleted.ShouldEqual(false);
        //    pipeline.Kill();
        //    pipeline.Result.Success.ShouldEqual(false);
        //}

        //[Test]
        //public void TestVersioning()
        //{
        //    var version = typeof(Command).GetTypeInfo().Assembly.GetName().Version.ToString();
        //    var informationalVersion = (AssemblyInformationalVersionAttribute)typeof(Command).GetTypeInfo().Assembly.GetCustomAttribute(typeof(AssemblyInformationalVersionAttribute));
        //    Assert.IsNotNull(informationalVersion);
        //    version.ShouldEqual(informationalVersion.InformationalVersion + ".0");
        //}

        //[Test]
        //public void TestShortFlush()
        //{
        //    var command = Command.Run(SampleCommand, "shortflush", "a");
        //    var readCommand = command.StandardOutput.ReadBlockAsync(new char[1], 0, 1);
        //    readCommand.Wait(TimeSpan.FromSeconds(5)).ShouldEqual(true);

        //    command.StandardInput.Dispose();
        //    command.Task.Wait(TimeSpan.FromSeconds(5)).ShouldEqual(true);
        //}

        //[Test]
        //public void TestAutoFlush()
        //{
        //    var command = Command.Run(SampleCommand, "echo", "--per-char");
        //    command.StandardInput.AutoFlush.ShouldEqual(true);
        //    command.StandardInput.Write('a');

        //    var buffer = new char[1];
        //    var asyncRead = command.StandardOutput.ReadBlockAsync(buffer, 0, 1);
        //    asyncRead.Wait(TimeSpan.FromSeconds(3)).ShouldEqual(true);
        //    buffer[0].ShouldEqual('a');

        //    command.StandardInput.AutoFlush = false;
        //    command.StandardInput.Write('b');
        //    asyncRead = command.StandardOutput.ReadBlockAsync(buffer, 0, 1);
        //    asyncRead.Wait(TimeSpan.FromSeconds(.01)).ShouldEqual(false);
        //    command.StandardInput.Flush();
        //    asyncRead.Wait(TimeSpan.FromSeconds(3)).ShouldEqual(true);
        //    buffer[0].ShouldEqual('b');

        //    command.StandardInput.Dispose();
        //}

        //[Test]
        //public void TestErrorEcho()
        //{
        //    var command = Command.Run(SampleCommand, "errecho") < "abc";
        //    command.Result.StandardError.ShouldEqual("abc");
        //}

        //[Test]
        //public void TestEncoding()
        //{
        //    // pick a string that will be different in UTF8 vs the default to make sure we use the default
        //    var bytes = new byte[] { 255 };
        //    var inputEncoded = Console.InputEncoding.GetString(bytes);
        //    inputEncoded.ShouldEqual(Console.OutputEncoding.GetString(bytes));
        //    inputEncoded.ShouldNotEqual(Encoding.UTF8.GetString(bytes));
        //    var command = Command.Run(SampleCommand, "echo") < inputEncoded;
        //    command.Result.StandardOutput.ShouldEqual(inputEncoded);

        //    const string InternationalText = "漢字";
        //    command = Command.Run(SampleCommand, "echo") < InternationalText;
        //    command.Result.StandardOutput.ShouldEqual("??", "Default encoding does not support international chars");

        //    command = Command.Run(SampleCommand, new[] { "echo", "--utf8" }, options: o => o.Encoding(Encoding.UTF8)) < InternationalText;
        //    command.Result.StandardOutput.ShouldEqual(InternationalText);
        //}

        //[Test]
        //public void TestGetOutputAndErrorLines()
        //{
        //    // simple echo case
        //    var command = Command.Run(SampleCommand, "echo") < new[] { "a", "b", "c" };
        //    string.Join(", ", command.GetOutputAndErrorLines().ToList()).ShouldEqual("a, b, c");

        //    // failure case: stderr not redirected
        //    command = Command.Run(SampleCommand, new[] { "echo" }, options: o => o.StartInfo(s => s.RedirectStandardError = false)) < new[] { "a" };
        //    Assert.Throws<InvalidOperationException>(() => command.GetOutputAndErrorLines());

        //    // fuzz case
        //    var lines = Enumerable.Range(0, 5000).Select(_ => Guid.NewGuid().ToString()).ToArray();
        //    command = Command.Run(SampleCommand, "echoLinesToBothStreams") < lines;
        //    var outputLines = command.GetOutputAndErrorLines().ToList();
        //    CollectionAssert.AreEquivalent(lines, outputLines);
        //}

        //[Test]
        //public void TestProcessAndProcessId()
        //{
        //    void TestHelper(bool disposeOnExit)
        //    {
        //        var shell = new Shell(o => o.DisposeOnExit(disposeOnExit));
        //        var command1 = shell.Run(SampleCommand, "pipe", "--id1");
        //        var command2 = shell.Run(SampleCommand, "pipe", "--id2");
        //        var pipeCommand = command1.PipeTo(command2);
        //        try
        //        {
        //            if (disposeOnExit)
        //            {
        //                // invalid due to DisposeOnExit()
        //                Assert.Throws<InvalidOperationException>(() => command1.Process.ToString())
        //                    .Message.ShouldContain("dispose on exit");
        //                Assert.Throws<InvalidOperationException>(() => command2.Processes.Count())
        //                    .Message.ShouldContain("dispose on exit");
        //                Assert.Throws<InvalidOperationException>(() => pipeCommand.Processes.Count())
        //                    .Message.ShouldContain("dispose on exit");
        //            }
        //            else
        //            {
        //                command1.Process.StartInfo.Arguments.ShouldContain("--id1");
        //                command1.Processes.SequenceEqual(new[] { command1.Process });
        //                command2.Process.StartInfo.Arguments.ShouldContain("--id2");
        //                command2.Processes.SequenceEqual(new[] { command2.Process }).ShouldEqual(true);
        //                pipeCommand.Process.ShouldEqual(command2.Process);
        //                pipeCommand.Processes.SequenceEqual(new[] { command1.Process, command2.Process }).ShouldEqual(true);
        //            }

        //            // https://stackoverflow.com/questions/2633628/can-i-get-command-line-arguments-of-other-processes-from-net-c
        //            string GetCommandLine(int processId)
        //            {
        //                using (var searcher = new ManagementObjectSearcher("SELECT CommandLine FROM Win32_Process WHERE ProcessId = " + processId))
        //                {
        //                    return searcher.Get().Cast<ManagementBaseObject>().Single()["CommandLine"].ToString();
        //                }
        //            }

        //            GetCommandLine(command1.ProcessId).ShouldContain("--id1");
        //            command1.ProcessIds.SequenceEqual(new[] { command1.ProcessId }).ShouldEqual(true);
        //            GetCommandLine(command2.ProcessId).ShouldContain("--id2");
        //            command2.ProcessIds.SequenceEqual(new[] { command2.ProcessId }).ShouldEqual(true);
        //            pipeCommand.ProcessId.ShouldEqual(command2.ProcessId);
        //            pipeCommand.ProcessIds.SequenceEqual(new[] { command1.ProcessId, command2.ProcessId }).ShouldEqual(true);
        //        }
        //        finally
        //        {
        //            command1.RedirectFrom(new[] { "data" });
        //            pipeCommand.Wait();
        //        }
        //    }

        //    TestHelper(disposeOnExit: true);
        //    TestHelper(disposeOnExit: false);
        //}

        //[Test]
        //public void TestToString()
        //{
        //    var command0 = Command.Run(SampleCommand, new[] { "grep", "a+" }, options => options.DisposeOnExit(true));
        //    command0.ToString().ShouldEqual($"{SampleCommand} grep a+");

        //    var command1 = Command.Run(SampleCommand, "exit", 0);
        //    command1.ToString().ShouldEqual($"{SampleCommand} exit 0");

        //    var command2 = Command.Run(SampleCommand, "ex it", "0 0");
        //    command2.ToString().ShouldEqual($"{SampleCommand} \"ex it\" \"0 0\"");

        //    var command3 = command1 < new[] { "a" };
        //    command3.ToString().ShouldEqual($"{SampleCommand} exit 0 < System.String[]");

        //    var command4 = command3 | Command.Run(SampleCommand, "echo");
        //    command4.ToString().ShouldEqual($"{SampleCommand} exit 0 < System.String[] | {SampleCommand} echo");

        //    var command5 = command2.RedirectStandardErrorTo(Stream.Null);
        //    command5.ToString().ShouldEqual($"{SampleCommand} \"ex it\" \"0 0\" 2> {Stream.Null}");

        //    var command6 = command5.RedirectTo(new StringWriter());
        //    command6.Wait();
        //    command6.ToString().ShouldEqual($"{command5} > {new StringWriter()}");
        //}

        //[Test]
        //public void TestCommandOption()
        //{
        //    var command = Command.Run(SampleCommand, new[] { "echo" }, options: o => o.Command(c => c.StandardInput.Write("!!!")))
        //        .RedirectFrom("abc");
        //    command.Wait();
        //    command.Result.StandardOutput.ShouldEqual("!!!abc");

        //    var writer = new StringWriter();
        //    command = Command.Run(SampleCommand, new[] { "echo" }, options: o => o.Command(c => c.RedirectTo(writer)))
        //        .RedirectFrom("abc123");
        //    command.Wait();
        //    writer.ToString().ShouldEqual("abc123");
        //}

        private IEnumerable<string> ErrorLines()
        {
            yield return "1";
            throw new InvalidOperationException("Can't enumerate");
        }
    }
}
