using System;
using System.Collections.Generic;
using System.Text;

namespace Medallion.Shell
{
    internal sealed class MonoUnixCommandLineSyntax : CommandLineSyntax
    {
        private static readonly WindowsCommandLineSyntax WindowsSyntax = new WindowsCommandLineSyntax();

        public override string CreateArgumentString(IEnumerable<string> arguments)
        {
            var windowsResult = WindowsSyntax.CreateArgumentString(arguments);
            // recommended https://bugzilla.xamarin.com/show_bug.cgi?id=19296
            //return windowsResult.Replace("\\$", "\\\\$").Replace("\\`", "\\\\`");

            // from https://github.com/GNOME/glib/blob/master/glib/gshell.c

            var stringBuilder = new StringBuilder();
            foreach (var argument in arguments)
            {
                if (stringBuilder.Length > 0) { stringBuilder.Append(' '); }
                stringBuilder.Append('\'');
                foreach (var @char in argument)
                {
                    if (@char == '\'') { stringBuilder.Append(@"'\'"); }
                    else { stringBuilder.Append(@char); }
                }
                stringBuilder.Append('\'');
            }

            return stringBuilder.ToString();
        }
    }
}
