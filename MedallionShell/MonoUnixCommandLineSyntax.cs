using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Medallion.Shell
{
    internal sealed class MonoUnixCommandLineSyntax : CommandLineSyntax
    {
        // note that fix recommended here https://bugzilla.xamarin.com/show_bug.cgi?id=19296 is not fully robust :-/

        // On unix, it seems that Mono calls g_shell_parse_argv:
        // https://github.com/mono/mono/blob/e8c660ce90efc7966ffc6fa9753ac237b2afadf2/mono/metadata/w32process-unix.c#L2174
        // something going on here: 
        // https://github.com/mono/mono/blob/e8c660ce90efc7966ffc6fa9753ac237b2afadf2/mono/metadata/w32process-unix.c#L1638
        // calls g_shell_parse_argv here:
        // https://github.com/mono/mono/blob/e8c660ce90efc7966ffc6fa9753ac237b2afadf2/mono/metadata/w32process-unix.c#L1786
        // def here: https://github.com/mono/mono/blob/c114ff59d96baba4479361b2679b7de602517877/mono/eglib/gshell.c

        public override string CreateArgumentString(IEnumerable<string> arguments)
        {
            Throw.IfNull(arguments, nameof(arguments));

            var builder = new StringBuilder();
            var isFirstArgument = true;
            foreach (var argument in arguments)
            {
                Throw.If(argument == null, nameof(arguments) + ": must not contain null");
                
                if (isFirstArgument) { isFirstArgument = false; }
                else { builder.Append(' '); }
                AppendArgument(argument);
            }

            return builder.ToString();

            void AppendArgument(string argument)
            {
                if (argument.Length > 0
                    && !argument.Any(IsSpecialCharacter))
                {
                    builder.Append(argument);
                    return;
                }

                builder.Append('"');
                for (var i = 0; i < argument.Length; ++i)
                {
                    var @char = argument[i];
                    switch (@char)
                    {
                        case '$':
                        case '`':
                        case '"':
                        case '\\':
                            builder.Append('\\');
                            break;
                    }
                    builder.Append(@char);
                }
                builder.Append('"');
            }
        }

        private static bool IsSpecialCharacter(char @char)
        {
            switch (@char)
            {
                case '\\':
                case '\'':
                case '"':
                    return true;
                default:
                    return char.IsWhiteSpace(@char);
            }
        }
    }
}
