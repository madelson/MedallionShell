using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Medallion.Shell
{
    public sealed class Shell
    {
        private readonly Action<Options> configuration;

        public Shell()
        {
        }

        public Shell(Action<Options> configuration)
        {
            Throw.IfNull(configuration, "configuration");
            this.configuration = configuration;
        }

        #region ---- Instance API ----
        public Command Run(string executable, IEnumerable<object> arguments = null, Action<Options> options = null)
        {
            Throw.If(string.IsNullOrEmpty(executable), "executable is required");

            var finalOptions = this.GetOptions(options);

            var processStartInfo = new ProcessStartInfo
            {
                Arguments = arguments != null 
                    ? finalOptions.CommandLineSyntax.CreateArgumentString(arguments.Select(Convert.ToString))
                    : string.Empty,
                CreateNoWindow = true,
                FileName = executable,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
            };
            finalOptions.StartInfoInitializers.ForEach(a => a(processStartInfo));

            var command = new ProcessCommand(processStartInfo, throwOnError: finalOptions.ThrowExceptionOnError);
            finalOptions.CommandInitializers.ForEach(a => a(command));

            return command;
        }

        public Command Run(string executable, params object[] arguments)
        {
            Throw.IfNull(arguments, "arguments");

            return this.Run(executable, arguments.AsEnumerable());
        }
        #endregion

        // TODO Run static methods, change instance to Execute
        #region ---- Static API ----
        private static readonly Shell DefaultShell = new Shell();
        public static Shell Default { get { return DefaultShell; } }
        #endregion

        private Options GetOptions(Action<Options> additionalConfiguration)
        {
            var builder = new Options();
            if (this.configuration != null)
            {
                this.configuration(builder);
            }
            if (additionalConfiguration != null)
            {
                additionalConfiguration(builder);
            }
            return builder;
        }

        #region ---- Options ----
        public sealed class Options
        {
            public Options()
            {
                this.RestoreDefaults();
            }

            internal List<Action<ProcessStartInfo>> StartInfoInitializers { get; private set; }
            internal List<Action<Command>> CommandInitializers { get; private set; }
            internal CommandLineSyntax CommandLineSyntax { get; private set; }
            internal bool ThrowExceptionOnError { get; set; }

            #region ---- Builder methods ----
            /// <summary>
            /// Restores all settings to the default value
            /// </summary>
            public Options RestoreDefaults()
            {
                this.StartInfoInitializers = new List<Action<ProcessStartInfo>>();
                this.CommandInitializers = new List<Action<Command>>();
                this.CommandLineSyntax = new WindowsCommandLineSyntax();
                this.ThrowExceptionOnError = false;
                return this;
            }

            public Options StartInfo(Action<ProcessStartInfo> initializer)
            {
                Throw.IfNull(initializer, "initializer");

                this.StartInfoInitializers.Add(initializer);
                return this;
            }

            public Options Command(Action<Command> initializer)
            {
                Throw.IfNull(initializer, "initializer");

                this.CommandInitializers.Add(initializer);
                return this;
            }

            public Options WorkingDirectory(string path)
            {
                return this.StartInfo(psi => psi.WorkingDirectory = path);
            }

            public Options EnvironmentVariable(string name, string value)
            {
                Throw.If(string.IsNullOrEmpty(name), "name is required");

                return this.StartInfo(psi => psi.EnvironmentVariables[name] = value);
            }

            public Options EnvironmentVariables(IEnumerable<KeyValuePair<string, string>> environmentVariables)
            {
                Throw.IfNull(environmentVariables, "environmentVariables");

                var environmentVariablesList = environmentVariables.ToList();
                return this.StartInfo(psi => environmentVariablesList.ForEach(kvp => psi.EnvironmentVariables[kvp.Key] = kvp.Value));
            }

            public Options ThrowOnError(bool value = true)
            {
                this.ThrowExceptionOnError = value;
                return this;
            }

            public Options Syntax(CommandLineSyntax syntax)
            {
                Throw.IfNull(syntax, "syntax");

                this.CommandLineSyntax = syntax;
                return this;
            }
            #endregion
        }
        #endregion
    }
}
