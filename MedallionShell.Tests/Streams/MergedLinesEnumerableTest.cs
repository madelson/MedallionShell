using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Medallion.Shell.Streams;
using Moq;
using NUnit.Framework;

namespace Medallion.Shell.Tests.Streams
{
    public class MergedLinesEnumerableTest
    {
        [Test]
        public void TestOneIsEmpty()
        {
            var empty1 = new StringReader(string.Empty);
            var nonEmpty1 = new StringReader("abc\r\ndef\r\nghi\r\njkl");

            var enumerable1 = new MergedLinesEnumerable(empty1, nonEmpty1);
            var list1 = enumerable1.ToList();
            list1.SequenceEqual(new[] { "abc", "def", "ghi", "jkl" })
                .ShouldEqual(true, string.Join(", ", list1));

            var empty2 = new StringReader(string.Empty);
            var nonEmpty2 = new StringReader("a\nbb\nccc\n");
            var enumerable2 = new MergedLinesEnumerable(nonEmpty2, empty2);
            var list2 = enumerable2.ToList();
            list2.SequenceEqual(new[] { "a", "bb", "ccc" })
                .ShouldEqual(true, string.Join(", ", list2));
        }

        [Test]
        public void TestBothAreEmpty()
        {
            var list = new MergedLinesEnumerable(new StringReader(string.Empty), new StringReader(string.Empty)).ToList();
            list.Count.ShouldEqual(0, string.Join(", ", list));
        }

        [Test]
        public void TestBothArePopulatedEqualSizes()
        {
            var list = new MergedLinesEnumerable(
                    new StringReader("a\nbb\nccc"),
                    new StringReader("1\r\n22\r\n333")
                )
                .ToList();
            string.Join(", ", list).ShouldEqual("a, 1, bb, 22, ccc, 333");
        }

        [Test]
        public void TestBothArePopulatedDifferenceSizes()
        {
            var lines1 = string.Join("\n", new[] { "x", "y", "z" });
            var lines2 = string.Join("\n", new[] { "1", "2", "3", "4", "5" });

            var list1 = new MergedLinesEnumerable(new StringReader(lines1), new StringReader(lines2))
                .ToList();
            string.Join(", ", list1).ShouldEqual("x, 1, y, 2, z, 3, 4, 5");

            var list2 = new MergedLinesEnumerable(new StringReader(lines2), new StringReader(lines1))
                .ToList();
            string.Join(", ", list2).ShouldEqual("1, x, 2, y, 3, z, 4, 5");
        }

        [Test]
        public void TestConsumeTwice()
        {
            var enumerable = new MergedLinesEnumerable(new StringReader("a"), new StringReader("b"));
            enumerable.GetEnumerator();
            Assert.Throws<InvalidOperationException>(() => enumerable.GetEnumerator());
        }

        [Test]
        public void TestOneThrows()
        {
            void TestOneThrows(bool reverse)
            {
                var reader1 = new StringReader("a\nb\nc");
                var count = 0;
                var mockReader = new Mock<TextReader>(MockBehavior.Strict);
                mockReader.Setup(r => r.ReadLineAsync())
                    .ReturnsAsync(() => ++count < 3 ? "LINE" : throw new TimeZoneNotFoundException());

                Assert.Throws<TimeZoneNotFoundException>(
                    () => new MergedLinesEnumerable(
                            reverse ? mockReader.Object : reader1,
                            reverse ? reader1 : mockReader.Object
                        )
                        .ToList()
                );
            }

            TestOneThrows(reverse: false);
            TestOneThrows(reverse: true);
        }

        [Test]
        public void FuzzTest()
        {
            var pipe1 = new Pipe();
            var pipe2 = new Pipe();

            var enumerable = new MergedLinesEnumerable(new StreamReader(pipe1.OutputStream), new StreamReader(pipe2.OutputStream));

            var strings1 = Enumerable.Range(0, 2000).Select(_ => Guid.NewGuid().ToString()).ToArray();
            var strings2 = Enumerable.Range(0, 2300).Select(_ => Guid.NewGuid().ToString()).ToArray();

            void WriteStrings(IReadOnlyList<string> strings, Pipe pipe)
            {
                var spinWait = default(SpinWait);
                var random = new Random(Guid.NewGuid().GetHashCode());
                using (var writer = new StreamWriter(pipe.InputStream))
                {
                    foreach (var line in strings)
                    {
                        if (random.Next(4) == 1)
                        {
                            spinWait.SpinOnce();
                        }

                        writer.WriteLine(line);
                    }
                }
            }

            var task1 = Task.Run(() => WriteStrings(strings1, pipe1));
            var task2 = Task.Run(() => WriteStrings(strings2, pipe2));
            var consumeTask = Task.Run(() => enumerable.ToList());
            Task.WaitAll(task1, task2, consumeTask);

            CollectionAssert.AreEquivalent(strings1.Concat(strings2).ToList(), consumeTask.Result);
        }
    }
}
