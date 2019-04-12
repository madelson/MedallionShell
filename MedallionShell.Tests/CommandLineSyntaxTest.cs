using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Medallion.Shell.Tests
{
    using static UnitTestHelpers;

    public class CommandLineSyntaxTest
    {
        [Test]
        public void TestArgumentsRoundTrip()
        {
            this.TestRoundTrip(" ");
            this.TestRoundTrip(@"c:\temp", @"a\\b");
            this.TestRoundTrip("\" a \"", @"\\", @"\""", @"\\""");
            this.TestRoundTrip("a\"b");
            this.TestRoundTrip("a\"b\"");
            this.TestRoundTrip("a\"b", "c\"d");
            this.TestRoundTrip("\v", "\t");
            this.TestRoundTrip("\r", "\n", "\r\n");
            this.TestRoundTrip(string.Empty, "\"", "\\", string.Empty);
            this.TestRoundTrip("abc", "a\\b", "a\\ b\"");

            // cases from https://docs.microsoft.com/en-us/cpp/cpp/parsing-cpp-command-line-arguments?view=vs-2019
            this.TestRoundTrip("abc", "d", "e");
            this.TestRoundTrip(@"a\\b", "de fg", "h");
            this.TestRoundTrip(@"a\""b", "c", "d");
            this.TestRoundTrip(@"a\\b c", "d", "e");
        }

        [Test]
        public void TestArgumentValidation()
        {
            var syntax = new WindowsCommandLineSyntax();
            Assert.Throws<ArgumentNullException>(() => syntax.CreateArgumentString(null));
            Assert.Throws<ArgumentException>(() => syntax.CreateArgumentString(new[] { "a", null, "b" }));
        }
        
        private void TestRoundTrip(params string[] arguments)
        {
            this.TestRealRoundTrip(arguments);
            this.TestAgainstNetCoreArgumentParser(arguments);
        }

        private void TestRealRoundTrip(string[] arguments)
        {
            var output = Command.Run(SampleCommand, new[] { "argecho" }.Concat(arguments), o => o.ThrowOnError()).Result.StandardOutput;
            var expected = string.Join(string.Empty, arguments.Select(a => a + Environment.NewLine));
            output.ShouldEqual(expected);
        }

        private void TestAgainstNetCoreArgumentParser(string[] arguments)
        {
            var argumentString = new WindowsCommandLineSyntax().CreateArgumentString(arguments);
            var result = new List<string>();
            ParseArgumentsIntoList(argumentString, result);
            CollectionAssert.AreEqual(actual: result, expected: arguments);
        }

        #region ---- Arguments Parser ----
        // copied from https://github.com/dotnet/corefx/blob/ccb68c0602656cea9a2a33f35f54dccba9eef784/src/System.Diagnostics.Process/src/System/Diagnostics/Process.Unix.cs#L785

        /// <summary>Parses a command-line argument string into a list of arguments.</summary>
        /// <param name="arguments">The argument string.</param>
        /// <param name="results">The list into which the component arguments should be stored.</param>
        /// <remarks>
        /// This follows the rules outlined in "Parsing C++ Command-Line Arguments" at 
        /// https://msdn.microsoft.com/en-us/library/17w5ykft.aspx.
        /// </remarks>
        private static void ParseArgumentsIntoList(string arguments, List<string> results)
        {
            // Iterate through all of the characters in the argument string.
            for (int i = 0; i < arguments.Length; i++)
            {
                while (i < arguments.Length && (arguments[i] == ' ' || arguments[i] == '\t'))
                {
                    i++;
                }

                if (i == arguments.Length)
                {
                    break;
                }

                results.Add(GetNextArgument(arguments, ref i));
            }
        }

        private static string GetNextArgument(string arguments, ref int i)
        {
            var currentArgument = new StringBuilder();
            bool inQuotes = false;

            while (i < arguments.Length)
            {
                // From the current position, iterate through contiguous backslashes.
                int backslashCount = 0;
                while (i < arguments.Length && arguments[i] == '\\')
                {
                    i++;
                    backslashCount++;
                }

                if (backslashCount > 0)
                {
                    if (i >= arguments.Length || arguments[i] != '"')
                    {
                        // Backslashes not followed by a double quote:
                        // they should all be treated as literal backslashes.
                        currentArgument.Append('\\', backslashCount);
                    }
                    else
                    {
                        // Backslashes followed by a double quote:
                        // - Output a literal slash for each complete pair of slashes
                        // - If one remains, use it to make the subsequent quote a literal.
                        currentArgument.Append('\\', backslashCount / 2);
                        if (backslashCount % 2 != 0)
                        {
                            currentArgument.Append('"');
                            i++;
                        }
                    }

                    continue;
                }

                char c = arguments[i];

                // If this is a double quote, track whether we're inside of quotes or not.
                // Anything within quotes will be treated as a single argument, even if
                // it contains spaces.
                if (c == '"')
                {
                    if (inQuotes && i < arguments.Length - 1 && arguments[i + 1] == '"')
                    {
                        // Two consecutive double quotes inside an inQuotes region should result in a literal double quote 
                        // (the parser is left in the inQuotes region).
                        // This behavior is not part of the spec of code:ParseArgumentsIntoList, but is compatible with CRT 
                        // and .NET Framework.
                        currentArgument.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = !inQuotes;
                    }

                    i++;
                    continue;
                }

                // If this is a space/tab and we're not in quotes, we're done with the current
                // argument, it should be added to the results and then reset for the next one.
                if ((c == ' ' || c == '\t') && !inQuotes)
                {
                    break;
                }

                // Nothing special; add the character to the current argument.
                currentArgument.Append(c);
                i++;
            }

            return currentArgument.ToString();
        }
        #endregion
    }
}
