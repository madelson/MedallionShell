using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Medallion.Shell
{
    /// <summary>
    /// Mono on unix uses a different, incompatible method of splitting the command line into arguments
    /// than is the default on Windows. This is referenced here: https://bugzilla.xamarin.com/show_bug.cgi?id=19296,
    /// although the "quick fix" it mentions does not fully account for the differences.
    /// </summary>
    internal sealed class MonoUnixCommandLineSyntax : CommandLineSyntax
    {
        // On unix, it seems that Mono calls g_shell_parse_argv:
        // https://github.com/mono/mono/blob/e8c660ce90efc7966ffc6fa9753ac237b2afadf2/mono/metadata/w32process-unix.c#L2174
        // follow that to here: 
        // https://github.com/mono/mono/blob/e8c660ce90efc7966ffc6fa9753ac237b2afadf2/mono/metadata/w32process-unix.c#L1638
        // calls g_shell_parse_argv here:
        // https://github.com/mono/mono/blob/e8c660ce90efc7966ffc6fa9753ac237b2afadf2/mono/metadata/w32process-unix.c#L1786
        // defined here: https://github.com/mono/mono/blob/c114ff59d96baba4479361b2679b7de602517877/mono/eglib/gshell.c

        public override string CreateArgumentString(IEnumerable<string> arguments) => CreateArgumentString(arguments, AppendArgument);

        private static void AppendArgument(string argument, StringBuilder builder)
        {
            // this method reverse-engineers the code Mono uses to split the command line.
            // A C# port of Mono's split method can be seen in the tests

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
