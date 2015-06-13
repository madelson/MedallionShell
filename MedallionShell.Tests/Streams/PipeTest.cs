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
            asyncRead.Wait(TimeSpan.FromSeconds(.01)).ShouldEqual(false);
            pipe.WriteText("x");
            asyncRead.Wait(TimeSpan.FromSeconds(.01)).ShouldEqual(false);
            pipe.WriteText(new string('y', 100));
            asyncRead.Wait(TimeSpan.FromSeconds(5)).ShouldEqual(true);
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
        }

        // TODO cancel, close (each side), overlapping reads
    }

    internal static class PipeExtensions
    {
        public static void WriteText(this Pipe @this, string text)
        {
            new StreamWriter(@this.InputStream) { AutoFlush = true }.Write(text);
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
