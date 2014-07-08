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
        private readonly Action<Builder> configuration;

        public Shell()
        {
        }

        public Shell(Action<Builder> configuration)
        {
            Throw.IfNull(configuration, "configuration");
            this.configuration = configuration;
        }

        #region ---- Instance API ----
        public Command Run(string executable, IEnumerable<string> arguments = null, Action<Builder> options = null)
        {
            Throw.If(string.IsNullOrEmpty(executable), "executable is required");

            var finalOptions = this.GetOptions(options);

            var processStartInfo = new ProcessStartInfo
            {
                // TODO syntax
                Arguments = arguments != null ? string.Join(" ", arguments) : string.Empty,
                CreateNoWindow = true,
                FileName = executable,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
            };
            finalOptions.StartInfoInitializers.ForEach(a => a(processStartInfo));

            var command = new ProcessCommand(processStartInfo);
            finalOptions.CommandInitializers.ForEach(a => a(command));

            return command;
        }

        public Command Run(string executable, params string[] arguments)
        {
            Throw.IfNull(arguments, "arguments");

            return this.Run(executable, arguments.AsEnumerable());
        }
        #endregion

        #region ---- Static API ----
        private static readonly Shell DefaultShell = new Shell();
        public static Shell Default { get { return DefaultShell; } }
        #endregion

        private Builder GetOptions(Action<Builder> additionalConfiguration)
        {
            var builder = new Builder();
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

        #region ---- Builder ----
        public sealed class Builder
        {
            public Builder()
            {
                this.StartInfoInitializers = new List<Action<ProcessStartInfo>>();
                this.CommandInitializers = new List<Action<Command>>();
                this.RestoreDefaults();
            }

            internal List<Action<ProcessStartInfo>> StartInfoInitializers { get; private set; }
            internal List<Action<Command>> CommandInitializers { get; private set; }

            #region ---- Builder methods ----
            /// <summary>
            /// Restores all settings to the default value
            /// </summary>
            public Builder RestoreDefaults()
            {
                this.StartInfoInitializers.Clear();
                this.CommandInitializers.Clear();
                return this;
            }

            public Builder StartInfo(Action<ProcessStartInfo> initializer)
            {
                Throw.IfNull(initializer, "initializer");

                this.StartInfoInitializers.Add(initializer);
                return this;
            }

            public Builder Command(Action<Command> initializer)
            {
                Throw.IfNull(initializer, "initializer");

                this.CommandInitializers.Add(initializer);
                return this;
            }

            public Builder WorkingDirectory(string path)
            {
                return this.StartInfo(psi => psi.WorkingDirectory = path);
            }

            public Builder EnvironmentVariable(string name, string value)
            {
                Throw.If(string.IsNullOrEmpty(name), "name is required");

                return this.StartInfo(psi => psi.EnvironmentVariables[name] = value);
            }

            public Builder EnvironmentVariables(IEnumerable<KeyValuePair<string, string>> environmentVariables)
            {
                Throw.IfNull(environmentVariables, "environmentVariables");

                var environmentVariablesList = environmentVariables.ToList();
                return this.StartInfo(psi => environmentVariablesList.ForEach(kvp => psi.EnvironmentVariables[kvp.Key] = kvp.Value));
            }
            #endregion
        }
        #endregion
    }
}
