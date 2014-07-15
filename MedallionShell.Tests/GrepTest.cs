using Medallion.Shell;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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
    }
}
