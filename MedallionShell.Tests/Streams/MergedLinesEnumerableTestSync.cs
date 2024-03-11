using System.Collections.Generic;
using System.IO;
using Medallion.Shell.Streams;

namespace Medallion.Shell.Tests.Streams;

public class MergedLinesEnumerableTestSync : MergedLinesEnumerableTestBase
{
    protected override IAsyncEnumerable<string> Create(TextReader reader1, TextReader reader2) =>
        new MergedLinesEnumerable(reader1, reader2).AsAsyncEnumerable<string>();
}