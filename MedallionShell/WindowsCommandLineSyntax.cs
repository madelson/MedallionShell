using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Medallion.Shell
{
    /// <summary>
    /// Provides <see cref="CommandLineSyntax"/> functionality for windows
    /// </summary>
    public sealed class WindowsCommandLineSyntax : CommandLineSyntax
    {
        /// <summary>
        /// Provides <see cref="CommandLineSyntax"/> functionality for windows
        /// </summary>
        public override string CreateArgumentString(IEnumerable<string> arguments)
        {
            Throw.IfNull(arguments, "arguments");

            var escapedArguments = arguments.Select(EscapeArgument);
            var result = string.Join(" ", escapedArguments);
            return result;
        }

        private static string EscapeArgument(string argument)
        {
            Throw.IfNull(argument, "argument");

            // this is super complex; I'm basing this on:
            // http://stackoverflow.com/questions/5510343/escape-command-line-arguments-in-c-sharp
            // Note: at the time of this writing, the posted answer didn't quite work as written. There was a comment
            // mentioning the correction, and I've submitted it as an edit (the groupings in the original post are wrong)
            
            // TODO the thread also mentions another method that only quotes when necessary. Should we use that?

            // find each substring of 0-or-more \ followed by " and replace it by twice-as-many \, followed by \".
            var singleQuotesEscaped = Regex.Replace(argument, @"(\\*)" + "\"", @"$1$1\" + "\"");

            // check if argument ends on \ and if so, double the number of backslashes at the end
            var trailingBackslashEscaped = Regex.Replace(singleQuotesEscaped, @"(\\+)$", @"$1$1");

            // add leading and trailing quotes
            var fullyEscaped = "\"" + trailingBackslashEscaped + "\"";
            return fullyEscaped;
        }
    }
}
