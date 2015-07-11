using Medallion.Shell.Streams;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Medallion.Shell.Tests.Streams
{
    [TestClass]
    public class PipeTest
    {
        [TestMethod]
        public void SimpleTest()
        {
            var pipe = new Pipe();

            pipe.WriteText("abc");
            pipe.ReadTextAsync(3).Result.ShouldEqual("abc");

            pipe.WriteText("1");
            pipe.WriteText("2");
            pipe.WriteText("3");
            pipe.ReadTextAsync(3).Result.ShouldEqual("123");

            var asyncRead = pipe.ReadTextAsync(100);
            asyncRead.Wait(TimeSpan.FromSeconds(.01)).ShouldEqual(false, asyncRead.IsCompleted ? "Found: " + (asyncRead.Result ?? "null") : "not complete");
            pipe.WriteText("x");
            asyncRead.Wait(TimeSpan.FromSeconds(.01)).ShouldEqual(false, asyncRead.IsCompleted ? "Found: " + (asyncRead.Result ?? "null") : "not complete");
            pipe.WriteText(new string('y', 100));
            asyncRead.Wait(TimeSpan.FromSeconds(5)).ShouldEqual(true, asyncRead.IsCompleted ? "Found: " + (asyncRead.Result ?? "null") : "not complete");
            asyncRead.Result.ShouldEqual("x" + new string('y', 99));
        }

        [TestMethod]
        public void TimeoutTest()
        {
            var pipe = new Pipe { OutputStream = { ReadTimeout = 0 } };
            UnitTestHelpers.AssertThrows<TimeoutException>(() => pipe.OutputStream.ReadByte());

            pipe.WriteText(new string('a', 2048));
            pipe.ReadTextAsync(2048).Result.ShouldEqual(new string('a', 2048));
        }

        [TestMethod]
        public void TestCancel()
        {
            var pipe = new Pipe();
            var cancellationTokenSource = new CancellationTokenSource();

            var asyncRead = pipe.ReadTextAsync(1, cancellationTokenSource.Token);
            asyncRead.Wait(TimeSpan.FromSeconds(.01)).ShouldEqual(false);
            cancellationTokenSource.Cancel();
            asyncRead.ContinueWith(_ => { }).Wait(TimeSpan.FromSeconds(5)).ShouldEqual(true);
            asyncRead.IsCanceled.ShouldEqual(true);

            pipe.WriteText("aaa");
            pipe.ReadTextAsync(2).Result.ShouldEqual("aa");

            asyncRead = pipe.ReadTextAsync(1, cancellationTokenSource.Token);
            asyncRead.ContinueWith(_ => { }).Wait(TimeSpan.FromSeconds(5)).ShouldEqual(true);
            asyncRead.IsCanceled.ShouldEqual(true);
        }

        [TestMethod]
        public void TestCloseWriteSide()
        {
            var pipe = new Pipe();
            pipe.WriteText("123456");
            pipe.InputStream.Close();
            UnitTestHelpers.AssertThrows<ObjectDisposedException>(() => pipe.InputStream.WriteByte(1));

            pipe.ReadTextAsync(5).Result.ShouldEqual("12345");
            pipe.ReadTextAsync(2).Result.ShouldEqual(null);

            pipe = new Pipe();
            var asyncRead = pipe.ReadTextAsync(1);
            asyncRead.Wait(TimeSpan.FromSeconds(.01)).ShouldEqual(false);
            pipe.InputStream.Close();
            asyncRead.Wait(TimeSpan.FromSeconds(5)).ShouldEqual(true);
        }

        [TestMethod]
        public void TestCloseReadSide()
        {
            var pipe = new Pipe();
            pipe.WriteText("abc");
            pipe.ReadTextAsync(2).Result.ShouldEqual("ab");
            pipe.OutputStream.Close();
            UnitTestHelpers.AssertThrows<ObjectDisposedException>(() => pipe.OutputStream.ReadByte());

            var largeBytes = new byte[10 * 1024];
            var initialMemory = GC.GetTotalMemory(forceFullCollection: true);
            for (var i = 0; i < int.MaxValue / 1024; ++i)
            {
                pipe.InputStream.Write(largeBytes, 0, largeBytes.Length);
            }
            var finalMemory = GC.GetTotalMemory(forceFullCollection: true);

            Assert.IsTrue(finalMemory - initialMemory < 10 * largeBytes.Length, "final = " + finalMemory + " initial = " + initialMemory);

            UnitTestHelpers.AssertThrows<ObjectDisposedException>(() => pipe.OutputStream.ReadByte());

            pipe.InputStream.Close();
        }

        [TestMethod]
        public void TestConcurrentReads()
        {
            var pipe = new Pipe();

            var asyncRead = pipe.ReadTextAsync(1);
            UnitTestHelpers.AssertThrows<InvalidOperationException>(() => pipe.OutputStream.ReadByte());
            pipe.InputStream.Close();
            asyncRead.Wait(TimeSpan.FromSeconds(5)).ShouldEqual(true);
        }

        [TestMethod]
        public void TestConcurrentWrites()
        {
            var pipe = new Pipe();
            pipe.SetFixedLength();

            var longText = new string('x', (2 * Constants.ByteBufferSize) + 1);
            var asyncWrite = pipe.WriteTextAsync(longText);
            asyncWrite.Wait(TimeSpan.FromSeconds(.01)).ShouldEqual(false);
            UnitTestHelpers.AssertThrows<InvalidOperationException>(() => pipe.InputStream.WriteByte(101));
            pipe.OutputStream.Close();
            asyncWrite.Wait(TimeSpan.FromSeconds(5)).ShouldEqual(true);
        }

        [TestMethod]
        public void TestChainedPipes()
        {
            var pipes = CreatePipeChain(100);

            // short write
            pipes[0].InputStream.WriteByte(100);
            var buffer = new byte[1];
            pipes.Last().OutputStream.ReadAsync(buffer, 0, buffer.Length)
                .Wait(TimeSpan.FromSeconds(5))
                .ShouldEqual(true);

            buffer[0].ShouldEqual((byte)100);

            // long write
            var longText = new string('y', 3 * Constants.CharBufferSize);
            var asyncWrite = pipes[0].WriteTextAsync(longText);
            asyncWrite.Wait(TimeSpan.FromSeconds(5)).ShouldEqual(true);
            var asyncRead = pipes.Last().ReadTextAsync(longText.Length);
            asyncRead.Wait(TimeSpan.FromSeconds(5)).ShouldEqual(true);
            asyncRead.Result.ShouldEqual(longText);
        }

        [TestMethod]
        public void TestPipeChainWithFixedLengthPipes()
        {
            try
            {
                var pipes = CreatePipeChain(2);
                var longText = new string('z', 8 * Constants.ByteBufferSize + 1);
                pipes.ForEach(p => p.SetFixedLength());
                var asyncWrite = pipes[0].WriteTextAsync(longText);
                asyncWrite.Wait(TimeSpan.FromSeconds(5)).ShouldEqual(false);
                var asyncRead = pipes.Last().ReadTextAsync(longText.Length);
                asyncWrite.Wait(TimeSpan.FromSeconds(5)).ShouldEqual(true);
                asyncRead.Wait(TimeSpan.FromSeconds(5)).ShouldEqual(true);
                asyncRead.Result.ShouldEqual(longText);
            }
            finally
            {
                Pipe.PrintLog();
            }
        }

        private static List<Pipe> CreatePipeChain(int length)
        {
            var pipes = Enumerable.Range(0, length).Select(_ => new Pipe())
                .ToList();
            for (var i = 0; i < pipes.Count - 1; ++i)
            {
                var fromPipe = pipes[i];
                var toPipe = pipes[i + 1];
                fromPipe.OutputStream.CopyToAsync(toPipe.InputStream)
                    .ContinueWith(_ => {
                        fromPipe.OutputStream.Close();
                        toPipe.InputStream.Close();
                    });
            }

            return pipes;
        }
    }

    internal static class PipeExtensions
    {
        public static void WriteText(this Pipe @this, string text)
        {
            new StreamWriter(@this.InputStream) { AutoFlush = true }.Write(text);
        }

        public static Task WriteTextAsync(this Pipe @this, string text)
        {
            return new StreamWriter(@this.InputStream) { AutoFlush = true }.WriteAsync(text);
        }

        public static async Task<string> ReadTextAsync(this Pipe @this, int count, CancellationToken token = default(CancellationToken))
        {
            var bytes = new byte[count];
            var bytesRead = 0;
            while (bytesRead < count)
            {
                var result = await @this.OutputStream.ReadAsync(bytes, offset: bytesRead, count: count - bytesRead, cancellationToken: token);
                if (result == 0) { return null; }
                bytesRead += result;
            }

            return new string(Encoding.Default.GetChars(bytes));
        }
    }
}
