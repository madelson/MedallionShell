using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Medallion.Shell
{
    /// <summary>
    /// Provides <see cref="CommandLineSyntax"/> functionality for windows.
    /// 
    /// Note that while this class uses windows parsing rules, .NET Core actually follows the same rules when parsing
    /// <see cref="ProcessStartInfo.Arguments"/> into argv for unix-like systems. Therefore, this class is actually
    /// cross-platform compatible
    /// </summary>
    public sealed class WindowsCommandLineSyntax : CommandLineSyntax
    {
        /// <summary>
        /// Provides <see cref="CommandLineSyntax"/> functionality for windows
        /// </summary>
        public override string CreateArgumentString(IEnumerable<string> arguments) => CreateArgumentString(arguments, AppendArgument);

        private static void AppendArgument(string argument, StringBuilder builder)
        {
            // based on the logic from http://stackoverflow.com/questions/5510343/escape-command-line-arguments-in-c-sharp.
            // The method given there doesn't minimize the use of quotation. For that, I drew from
            // https://blogs.msdn.microsoft.com/twistylittlepassagesallalike/2011/04/23/everyone-quotes-command-line-arguments-the-wrong-way/

            // the essential encoding logic is:
            // (1) non-empty strings with no special characters require no encoding
            // (2) find each substring of 0-or-more \ followed by " and replace it by twice-as-many \, followed by \"
            // (3) check if argument ends on \ and if so, double the number of backslashes at the end
            // (4) add leading and trailing "

            if (argument.Length > 0
                && !argument.Any(IsSpecialCharacter))
            {
                builder.Append(argument);
                return;
            }

            builder.Append('"');

            var backSlashCount = 0;
            foreach (var ch in argument)
            {
                switch (ch)
                {
                    case '\\':
                        ++backSlashCount;
                        break;
                    case '"':
                        builder.Append('\\', repeatCount: (2 * backSlashCount) + 1);
                        backSlashCount = 0;
                        builder.Append(ch);
                        break;
                    default:
                        builder.Append('\\', repeatCount: backSlashCount);
                        backSlashCount = 0;
                        builder.Append(ch);
                        break;
                }
            }

            builder.Append('\\', repeatCount: 2 * backSlashCount)
                .Append('"');
        }

        private static bool IsSpecialCharacter(char ch) => char.IsWhiteSpace(ch) || ch == '"';
    }
}
