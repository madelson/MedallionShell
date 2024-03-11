#if NETCOREAPP3_0_OR_GREATER || NETSTANDARD2_0_OR_GREATER
using System.Collections.Generic;
using System.IO;
using Medallion.Shell.Streams;

namespace Medallion.Shell.Tests.Streams;

public class MergedLinesEnumerableTestAsync : MergedLinesEnumerableTestBase
{
    protected override IAsyncEnumerable<string> Create(TextReader reader1, TextReader reader2) =>
        new MergedLinesEnumerable(reader1, reader2);
}
#endif