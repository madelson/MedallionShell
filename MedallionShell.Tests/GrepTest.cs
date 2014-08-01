using Medallion.Shell;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Medallion.Shell.Tests
{
    [TestClass]
    public class GrepTest
    {
        [TestMethod]
        public void TestGrep()
        {
            var command = Shell.Default.Run("SampleCommand", "grep", "a+");
            command.StandardInput.WriteLine("hi");
            command.StandardInput.WriteLine("aa");
            command.StandardInput.Close();
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
            var command = shell.Run("SampleCommand", "grep", "a");
            command.Process.StandardOutput.BaseStream.Dispose();
            var ignored = command < Enumerable.Repeat(new string('a', 1000), 1000);
            command.Task.Wait();
            command.Task.Result.StandardOutput.ShouldEqual(string.Empty);
        }

        [TestMethod]
        public void TestExitCode()
        {
            if (!Command.Run("SampleCommand", "exit", 0))
            {
                Assert.Fail("Should have worked");
            }
            if (Command.Run("SampleCommand", "exit", 1))
            {
                Assert.Fail("Should have failed");
            }

            var shell = new Shell(o => o.ThrowOnError());
            var ex = UnitTestHelpers.AssertThrows<AggregateException>(() => shell.Run("SampleCommand", "exit", -1).Task.Wait());
            ex.InnerExceptions.Select(e => e.GetType()).SequenceEqual(new[] { typeof(ErrorExitCodeException) })
                .ShouldEqual(true);

            shell.Run("SampleCommand", "exit", 0).Task.Wait();
        }

        [TestMethod]
        public void TestTimeout()
        {
            var willTimeout = Command.Run("SampleCommand", new object[] { "sleep", 1000000 }, o => o.Timeout(TimeSpan.FromMilliseconds(200)));
            var ex = UnitTestHelpers.AssertThrows<AggregateException>(() => willTimeout.Task.Wait());
            Assert.IsInstanceOfType(ex.InnerException, typeof(TimeoutException));
        }

        // TODO error handling tests
    }
}
