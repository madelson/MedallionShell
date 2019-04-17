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
            return windowsResult.Replace("\\$", "\\\\$").Replace("\\`", "\\\\`");
        }
    }
}
