using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Medallion.Shell
{
    /// <summary>
    /// Represents an object which can be used to dispatch <see cref="Command"/>s
    /// </summary>
    public sealed class Shell
    {
        private readonly Action<Options> configuration;

        private Shell()
        {
        }

        /// <summary>
        /// Creates a shell whose commands will receive the given configuration options
        /// </summary>
        public Shell(Action<Options> options)
        {
            Throw.IfNull(options, "configuration");
            this.configuration = options;
        }

        #region ---- Instance API ----
        /// <summary>
        /// Executes the given <paramref name="executable"/> with the given <paramref name="arguments"/> and
        /// <paramref name="options"/>
        /// </summary>
        public Command Run(string executable, IEnumerable<object> arguments = null, Action<Options> options = null)
        {
            Throw.If(string.IsNullOrEmpty(executable), "executable is required");

            var finalOptions = this.GetOptions(options);

            var processStartInfo = new ProcessStartInfo
            {
                Arguments = arguments != null 
                    ? finalOptions.CommandLineSyntax.CreateArgumentString(arguments.Select(arg => Convert.ToString(arg, CultureInfo.InvariantCulture)))
                    : string.Empty,
                CreateNoWindow = true,
                FileName = executable,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
            };
            finalOptions.StartInfoInitializers.ForEach(a => a(processStartInfo));

            var command = new ProcessCommand(
                processStartInfo, 
                throwOnError: finalOptions.ThrowExceptionOnError,
                disposeOnExit: finalOptions.DisposeProcessOnExit,
                timeout: finalOptions.ProcessTimeout
            );
            finalOptions.CommandInitializers.ForEach(a => a(command));

            return command;
        }

        /// <summary>
        /// Executes the given <paramref name="executable"/> with the given <paramref name="arguments"/> 
        /// </summary>
        public Command Run(string executable, params object[] arguments)
        {
            Throw.IfNull(arguments, "arguments");

            return this.Run(executable, arguments.AsEnumerable());
        }
        #endregion

        // TODO do we want to support static Run-type methods here as well?
        #region ---- Static API ----
        private static readonly Shell DefaultShell = new Shell();
        /// <summary>
        /// A <see cref="Shell"/> that uses default options
        /// </summary>
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
        /// <summary>
        /// Provides a builder interface for configuring the options for creating and executing
        /// a <see cref="Command"/>
        /// </summary>
        public sealed class Options
        {
            internal Options()
            {
                this.RestoreDefaults();
            }

            internal List<Action<ProcessStartInfo>> StartInfoInitializers { get; private set; }
            internal List<Action<Command>> CommandInitializers { get; private set; }
            internal CommandLineSyntax CommandLineSyntax { get; private set; }
            internal bool ThrowExceptionOnError { get; set; }
            internal bool DisposeProcessOnExit { get; set; }
            internal TimeSpan ProcessTimeout { get; set; }

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
                this.DisposeProcessOnExit = true;
                this.ProcessTimeout = System.Threading.Timeout.InfiniteTimeSpan;
                return this;
            }

            /// <summary>
            /// Specifies a function which can modify the <see cref="ProcessStartInfo"/>. Multiple such functions
            /// can be specified this way
            /// </summary>
            public Options StartInfo(Action<ProcessStartInfo> initializer)
            {
                Throw.IfNull(initializer, "initializer");

                this.StartInfoInitializers.Add(initializer);
                return this;
            }

            /// <summary>
            /// Specifies a function which can modify the <see cref="Command"/>. Multiple such functions
            /// can be specified this way
            /// </summary>
            public Options Command(Action<Command> initializer)
            {
                Throw.IfNull(initializer, "initializer");

                this.CommandInitializers.Add(initializer);
                return this;
            }

            /// <summary>
            /// Sets the initial working directory of the <see cref="Command"/> (defaults to the current working directory)
            /// </summary>.
            public Options WorkingDirectory(string path)
            {
                return this.StartInfo(psi => psi.WorkingDirectory = path);
            }

            /// <summary>
            /// Adds or overwrites an environment variable to be passed to the <see cref="Command"/>
            /// </summary>
            public Options EnvironmentVariable(string name, string value)
            {
                Throw.If(string.IsNullOrEmpty(name), "name is required");

                return this.StartInfo(psi => psi.EnvironmentVariables[name] = value);
            }

            /// <summary>
            /// Adds or overwrites a set of environmental variables to be passed to the <see cref="Command"/>
            /// </summary>
            public Options EnvironmentVariables(IEnumerable<KeyValuePair<string, string>> environmentVariables)
            {
                Throw.IfNull(environmentVariables, "environmentVariables");

                var environmentVariablesList = environmentVariables.ToList();
                return this.StartInfo(psi => environmentVariablesList.ForEach(kvp => psi.EnvironmentVariables[kvp.Key] = kvp.Value));
            }

            /// <summary>
            /// If specified, a non-zero exit code will cause the <see cref="Command"/>'s <see cref="Task"/> to fail
            /// with <see cref="ErrorExitCodeException"/>. Defaults to false
            /// </summary>
            public Options ThrowOnError(bool value = true)
            {
                this.ThrowExceptionOnError = value;
                return this;
            }

            /// <summary>
            /// If specified, the underlying <see cref="Process"/> object for the command will be disposed when the process exits.
            /// This means that there is no need to dispose of a <see cref="Command"/>. 
            /// 
            /// This also means that <see cref="Medallion.Shell.Command.Process"/> cannot be reliably accessed, 
            /// since it may exit at any time. 
            /// 
            /// Defaults to true
            /// </summary>
            public Options DisposeOnExit(bool value = true)
            {
                this.DisposeProcessOnExit = value;
                return this;
            }

            /// <summary>
            /// Specifies the <see cref="CommandLineSyntax"/> to use for escaping arguments. Defaults to an instance of
            /// <see cref="WindowsCommandLineSyntax"/>
            /// </summary>
            public Options Syntax(CommandLineSyntax syntax)
            {
                Throw.IfNull(syntax, "syntax");

                this.CommandLineSyntax = syntax;
                return this;
            }

            /// <summary>
            /// Specifies a timeout after which the process should be killed. Defaults to <see cref="System.Threading.Timeout.InfiniteTimeSpan"/>
            /// </summary>
            public Options Timeout(TimeSpan timeout)
            {
                Throw<ArgumentOutOfRangeException>.If(timeout < TimeSpan.Zero && timeout != System.Threading.Timeout.InfiniteTimeSpan, "timeout");

                this.ProcessTimeout = timeout;
                return this;
            }
            #endregion
        }
        #endregion
    }
}
