using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Medallion.Shell.Tests
{
    [TestClass]
    public class SyntaxTest
    {
        [TestMethod]
        public void DirectTestWindowsSyntax()
        {
            this.TestSyntax(" ");
            this.TestSyntax(@"c:\temp", @"a\\b");
            this.TestSyntax("\" a \"", @"\\", @"\""", @"\\""");
            this.TestSyntax("a\"b");
            this.TestSyntax("a\"b\"");
            this.TestSyntax("a\"b", "c\"d");
            this.TestSyntax("\v", "\t");
            this.TestSyntax("\r", "\n", "\r\n");
            this.TestSyntax(string.Empty, "\"", "\\", string.Empty);
            this.TestSyntax("abc", "a\\b", "a\\ b\"");
        }

        private void TestSyntax(params string[] arguments)
        {
            var output = Command.Run("SampleCommand", new[] { "argecho" }.Concat(arguments), o => o.ThrowOnError()).Result.StandardOutput;

            var expected = string.Join(string.Empty, arguments.Select(a => a + Environment.NewLine));
            output.ShouldEqual(expected);
        }
    }
}
