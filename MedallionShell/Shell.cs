﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
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
        internal Action<Options> Configuration { get; }

        /// <summary>
        /// Creates a shell whose commands will receive the given configuration options
        /// </summary>
        public Shell(Action<Options> options)
        {
            Throw.IfNull(options, nameof(options));
            this.Configuration = options;
        }

        #region ---- Instance API ----
        /// <summary>
        /// Executes the given <paramref name="executable"/> with the given <paramref name="arguments"/> and
        /// <paramref name="options"/>
        /// </summary>
        public Command Run(string executable, IEnumerable<object>? arguments = null, Action<Options>? options = null)
        {
            Throw.If(string.IsNullOrEmpty(executable), "executable is required");

            var executablePath = !executable.Contains(Path.DirectorySeparatorChar)
                && GetFullPathUsingSystemPathOrDefault(executable) is { } fullPath
                ? fullPath
                : executable;

            var finalOptions = this.GetOptions(options);

            var processStartInfo = new ProcessStartInfo
            {
                CreateNoWindow = true,
                FileName = executablePath,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
            };

            PopulateArguments(processStartInfo, arguments, finalOptions);

            if (finalOptions.ProcessStreamEncoding != null)
            {
                processStartInfo.StandardOutputEncoding = processStartInfo.StandardErrorEncoding = finalOptions.ProcessStreamEncoding;
#if NETCOREAPP2_1_OR_GREATER || NETSTANDARD2_1
                processStartInfo.StandardInputEncoding = finalOptions.ProcessStreamEncoding;
#endif
            }
            finalOptions.StartInfoInitializers.ForEach(a => a(processStartInfo));

            Command command = new ProcessCommand(
                processStartInfo,
                throwOnError: finalOptions.ThrowExceptionOnError,
                disposeOnExit: finalOptions.DisposeProcessOnExit,
                timeout: finalOptions.ProcessTimeout,
                cancellationToken: finalOptions.ProcessCancellationToken,
                standardInputEncoding: finalOptions.ProcessStreamEncoding
            );
            foreach (var initializer in finalOptions.CommandInitializers)
            {
                command = initializer(command);
                if (command == null) { throw new InvalidOperationException($"{nameof(Command)} initializer passed to {nameof(Options)}.{nameof(Options.Command)} must not return null!"); }
            }
            
            return command;
        }

        // internal for testing
        internal static string? GetFullPathUsingSystemPathOrDefault(string executable)
        {
            var paths = Environment.GetEnvironmentVariable("PATH")!.Split(Path.PathSeparator);

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var pathExtensions = Environment.GetEnvironmentVariable("PATHEXT")!
                    .Split(Path.PathSeparator)
                    .Concat(new[] { string.Empty })
                    .ToArray();
                return paths.SelectMany(path => pathExtensions.Select(pathExtension => Path.Combine(path, executable + pathExtension)))
                    .FirstOrDefault(File.Exists);
            }

            return paths.Select(path => Path.Combine(path, executable)).FirstOrDefault(File.Exists);
        }

        private static void PopulateArguments(ProcessStartInfo processStartInfo, IEnumerable<object?>? arguments, Options options)
        {
            if (arguments is null) { return; }

            var argumentStrings = arguments.Select(
                arg => Convert.ToString(arg, CultureInfo.InvariantCulture)
                    ?? throw new ArgumentException("must not contain null", nameof(arguments))
            );

#if NETCOREAPP2_1_OR_GREATER || NETSTANDARD2_1
            // If using the default command line syntax, prefer to use ArgumentsList rather than encoding ourselves (#95)
            if (options.CommandLineSyntax == PlatformCompatibilityHelper.DefaultCommandLineSyntax)
            {
                foreach (var argumentString in argumentStrings)
                {
                    processStartInfo.ArgumentList.Add(argumentString);
                }
                return;
            }
#endif

            processStartInfo.Arguments = options.CommandLineSyntax.CreateArgumentString(argumentStrings);
        }

        /// <summary>
        /// Tries to attach to an already running process, given its <paramref name="processId"/>,
        /// giving <paramref name="attachedCommand" /> representing the process and returning
        /// true if this succeeded, otherwise false.
        /// </summary>
        public bool TryAttachToProcess(int processId, [NotNullWhen(returnValue: true)] out Command? attachedCommand) =>
            this.TryAttachToProcess(processId, options: null, attachedCommand: out attachedCommand);

        /// <summary>
        /// Tries to attach to an already running process, given its <paramref name="processId"/>
        /// and <paramref name="options"/>,  giving <paramref name="attachedCommand" /> representing
        /// the process and returning true if this succeeded, otherwise false.
        /// </summary>
        public bool TryAttachToProcess(int processId, Action<Shell.Options>? options, [NotNullWhen(returnValue: true)] out Command? attachedCommand)
        {
            var finalOptions = this.GetOptions(options);
            if (finalOptions.ProcessStreamEncoding != null || finalOptions.StartInfoInitializers.Count != 0)
            {
                throw new InvalidOperationException(
                    "Setting encoding or using StartInfo initializers is not available when attaching to an already running process.");
            }

            attachedCommand = null;
            Process? process = null;
            try
            {
                process = Process.GetProcessById(processId);

                // Since simply getting (Safe)Handle from the process enables us to read
                // the exit code later, and handle itself is disposed when the whole class
                // is disposed, we do not need its value. Hence the bogus call to GetType().
#if NET45
                process.Handle.GetType();
#else
                process.SafeHandle.GetType();
#endif
            }
            catch (Exception e) when (IsIgnorableAttachingException(e))
            {
                process?.Dispose();
                return false;
            }
            catch (Exception e) when (e is Win32Exception || e is NotSupportedException)
            {
                throw new InvalidOperationException(
                    "Could not attach to the process from reasons other than it had already exited. See inner exception for details.",
                    e);
            }

            attachedCommand = new AttachedCommand(
                process,
                finalOptions.ThrowExceptionOnError,
                finalOptions.ProcessTimeout,
                finalOptions.ProcessCancellationToken,
                finalOptions.DisposeProcessOnExit);
            return true;
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

#region ---- Static API ----
        /// <summary>
        /// A <see cref="Shell"/> that uses default options
        /// </summary>
        public static Shell Default { get; } = new Shell(_ => { });
#endregion

        private Options GetOptions(Action<Options>? additionalConfiguration)
        {
            var builder = new Options();
            this.Configuration.Invoke(builder);
            additionalConfiguration?.Invoke(builder);
            return builder;
        }

#region ---- Options ----
        /// <summary>
        /// Provides a builder interface for configuring the options for creating and executing
        /// a <see cref="Medallion.Shell.Command"/>
        /// </summary>
        public sealed class Options
        {
            internal Options()
            {
                this.RestoreDefaults();
            }

            internal List<Action<ProcessStartInfo>> StartInfoInitializers { get; private set; } = default!; // assigned in RestoreDefaults
            internal List<Func<Command, Command>> CommandInitializers { get; private set; } = default!; // assigned in RestoreDefaults
            internal CommandLineSyntax CommandLineSyntax { get; private set; } = default!; // assigned in RestoreDefaults
            internal bool ThrowExceptionOnError { get; private set; }
            internal bool DisposeProcessOnExit { get; private set; }
            internal TimeSpan ProcessTimeout { get; private set; }
            internal Encoding? ProcessStreamEncoding { get; private set; }
            internal CancellationToken ProcessCancellationToken { get; private set; }

#region ---- Builder methods ----
            /// <summary>
            /// Restores all settings to the default value
            /// </summary>
            public Options RestoreDefaults()
            {
                this.StartInfoInitializers = new List<Action<ProcessStartInfo>>();
                this.CommandInitializers = new List<Func<Command, Command>>();
                this.CommandLineSyntax = PlatformCompatibilityHelper.DefaultCommandLineSyntax;
                this.ThrowExceptionOnError = false;
                this.DisposeProcessOnExit = true;
                this.ProcessTimeout = System.Threading.Timeout.InfiniteTimeSpan;
                this.ProcessStreamEncoding = null;
                this.ProcessCancellationToken = System.Threading.CancellationToken.None;
                return this;
            }

            /// <summary>
            /// Specifies a function which can modify the <see cref="ProcessStartInfo"/>. Multiple such functions
            /// can be specified this way
            /// </summary>
            public Options StartInfo(Action<ProcessStartInfo> initializer)
            {
                Throw.IfNull(initializer, nameof(initializer));

                this.StartInfoInitializers.Add(initializer);
                return this;
            }

            /// <summary>
            /// Specifies a function which can modify the <see cref="Medallion.Shell.Command"/>. Multiple such functions
            /// can be specified this way
            /// </summary>
            public Options Command(Action<Command> initializer)
            {
                Throw.IfNull(initializer, nameof(initializer));

                this.Command(c => 
                {
                    initializer(c);
                    return c;
                });
                return this;
            }

            /// <summary>
            /// Specifies a function which can project the <see cref="Medallion.Shell.Command"/> to a new <see cref="Medallion.Shell.Command"/>.
            /// Intended to be used with <see cref="Medallion.Shell.Command"/>-producing "pipe" functions like <see cref="Medallion.Shell.Command.RedirectTo(ICollection{char})"/>
            /// </summary>
            public Options Command(Func<Command, Command> initializer)
            {
                Throw.IfNull(initializer, nameof(initializer));

                this.CommandInitializers.Add(initializer);
                return this;
            }

            /// <summary>
            /// Sets the initial working directory of the <see cref="Medallion.Shell.Command"/> (defaults to the current working directory)
            /// </summary>.
            public Options WorkingDirectory(string path)
            {
                return this.StartInfo(psi => psi.WorkingDirectory = path);
            }

#if !NETSTANDARD1_3
            /// <summary>
            /// Adds or overwrites an environment variable to be passed to the <see cref="Medallion.Shell.Command"/>
            /// </summary>
            public Options EnvironmentVariable(string name, string value)
            {
                Throw.If(string.IsNullOrEmpty(name), "name is required");

                return this.StartInfo(psi => psi.EnvironmentVariables[name] = value);
            }

            /// <summary>
            /// Adds or overwrites a set of environmental variables to be passed to the <see cref="Medallion.Shell.Command"/>
            /// </summary>
            public Options EnvironmentVariables(IEnumerable<KeyValuePair<string, string>> environmentVariables)
            {
                Throw.IfNull(environmentVariables, "environmentVariables");

                var environmentVariablesList = environmentVariables.ToList();
                return this.StartInfo(psi => environmentVariablesList.ForEach(kvp => psi.EnvironmentVariables[kvp.Key] = kvp.Value));
            }
#endif

            /// <summary>
            /// If specified, a non-zero exit code will cause the <see cref="Medallion.Shell.Command"/>'s <see cref="Task"/> to fail
            /// with <see cref="ErrorExitCodeException"/>. Defaults to false
            /// </summary>
            public Options ThrowOnError(bool value = true)
            {
                this.ThrowExceptionOnError = value;
                return this;
            }

            /// <summary>
            /// If specified, the underlying <see cref="Process"/> object for the command will be disposed when the process exits.
            /// This means that there is no need to dispose of a <see cref="Medallion.Shell.Command"/>.
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
            /// Specifies the <see cref="CommandLineSyntax"/> to use for escaping arguments. Defaults to
            /// an appropriate value for the current platform
            /// </summary>
            [Obsolete("The default should work across platforms")]
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

            /// <summary>
            /// Specifies an <see cref="Encoding"/> to be used for StandardOutput, StandardError, and StandardInput.
            ///
            /// By default, <see cref="Process"/> will use <see cref="Console.OutputEncoding"/> for output streams and <see cref="Console.InputEncoding"/>
            /// for input streams.
            ///
            /// Note that the output encodings can be set individually via <see cref="ProcessStartInfo"/>. If you need to specify different encodings
            /// for different streams, use this method to set the StandardInput encoding and then use <see cref="StartInfo(Action{ProcessStartInfo})"/>
            /// to further override the two output encodings
            /// </summary>
            public Options Encoding(Encoding encoding)
            {
                Throw.IfNull(encoding, nameof(encoding));

                this.ProcessStreamEncoding = encoding;
                return this;
            }

            /// <summary>
            /// Specifies a <see cref="System.Threading.CancellationToken"/> which will abort the command when canceled.
            /// When a command is aborted, the underlying process will be killed as if using <see cref="Medallion.Shell.Command.Kill"/>
            /// </summary>
            public Options CancellationToken(CancellationToken cancellationToken)
            {
                this.ProcessCancellationToken = cancellationToken;
                return this;
            }
#endregion
        }
#endregion

        private static bool IsIgnorableAttachingException(Exception exception)
        {
            return exception is ArgumentException // process has already exited or ID is invalid
                || exception is InvalidOperationException; // process exited after its creation but before taking its handle
        }
    }
}
