using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace Medallion.Shell.Tests
{
    public class SyntaxTest
    {
        [Fact]
        public void DirectTestWindowsSyntax()
        {
            this.TestSyntax(" ");
            this.TestSyntax(@"c:\temp", @"a\\b");
            this.TestSyntax("\" a \"", @"\\", @"\""", @"\\""");
        }

        private void TestSyntax(params string[] arguments)
        {
            var lines = Command.Run("SampleCommand", new[] { "argecho" }.Concat(arguments), o => o.ThrowOnError())
                .StandardOutput
                .GetLines()
                .ToArray();

            lines.SequenceEqual(arguments).ShouldEqual(true, "Got: '" + string.Join(Environment.NewLine, lines) + "'");
        }
    }
}
