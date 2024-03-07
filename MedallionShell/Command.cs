using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Medallion.Shell.Signals;
using Medallion.Shell.Streams;

namespace Medallion.Shell
{
    /// <summary>
    /// Represents an executing <see cref="Process"/> as well as related asynchronous activity (e. g. the piping of
    /// input and output streams)
    /// </summary>
    public abstract class Command : IDisposable
    {
        // prevent external inheritors
        internal Command() { }

        /// <summary>
        /// The <see cref="System.Diagnostics.Process"/> associated with this <see cref="Command"/>. In a multi-process command,
        /// this will be the final <see cref="System.Diagnostics.Process"/> in the chain. NOTE: this property cannot be accessed when using
        /// the <see cref="Shell.Options.DisposeOnExit(bool)"/> option
        /// </summary>
        public abstract Process Process { get; }
        /// <summary>
        /// All <see cref="System.Diagnostics.Process"/>es associated with this <see cref="Command"/>. NOTE: this property cannot be accessed when using
        /// the <see cref="Shell.Options.DisposeOnExit(bool)"/> option
        /// </summary>
        public abstract IReadOnlyList<Process> Processes { get; }

        /// <summary>
        /// The PID of the process associated with this <see cref="Command"/>. In a multi-process command,
        /// this will be the PID of the final <see cref="System.Diagnostics.Process"/> in the chain. NOTE: unlike
        /// the <see cref="Process"/> property, this property is compatible with the <see cref="Shell.Options.DisposeOnExit(bool)"/>
        /// option
        /// </summary>
        public abstract int ProcessId { get; }
        /// <summary>
        /// All PIDs of the <see cref="System.Diagnostics.Process"/>es associated with this <see cref="Command"/>. NOTE: unlike
        /// the <see cref="Processes"/> property, this property is compatible with the
        /// <see cref="Shell.Options.DisposeOnExit(bool)"/> option
        /// </summary>
        public abstract IReadOnlyList<int> ProcessIds { get; }

        /// <summary>
        /// Writes to the process's standard input
        /// </summary>
        public abstract ProcessStreamWriter StandardInput { get; }
        /// <summary>
        /// Reads from the process's standard output
        /// </summary>
        public abstract ProcessStreamReader StandardOutput { get; }
        /// <summary>
        /// Reads from the process's standard error
        /// </summary>
        public abstract ProcessStreamReader StandardError { get; }

        /// <summary>
        /// Kills the <see cref="Process"/> if it is still executing
        /// </summary>
        public abstract void Kill();

        /// <summary>
        /// Attempts to send the specified <see cref="CommandSignal"/> to each <see cref="System.Diagnostics.Process"/>
        /// underlying the current <see cref="Command"/>. Returns true if at least one <see cref="System.Diagnostics.Process"/>
        /// received the signal and false otherwise.
        /// 
        /// There are several reasons that signaling could fail, for example:
        /// * The provided signal number is not valid for the OS
        /// * The <see cref="System.Diagnostics.Process"/> has already exited
        /// * On Windows, signals can only be sent to console processes
        /// </summary>
        public Task<bool> TrySignalAsync(CommandSignal signal)
        {
            if (signal == null) { throw new ArgumentNullException(nameof(signal)); }

            this.ThrowIfDisposed();

            return TrySignalHelperAsync();

            async Task<bool> TrySignalHelperAsync()
            {
                var result = false;
                foreach (var processId in this.ProcessIds)
                {
                    result |= await ProcessSignaler.TrySignalAsync(processId, signal).ConfigureAwait(false);
                }
                return result;
            }
        }

        /// <summary>
        /// A convenience method for <code>command.Task.Wait()</code>. If the task faulted or was canceled,
        /// this will throw the faulting <see cref="Exception"/> or <see cref="TaskCanceledException"/> rather than
        /// the wrapped <see cref="AggregateException"/> thrown by <see cref="Task{TResult}.Result"/>
        /// </summary>
        public void Wait() => this.Task.GetAwaiter().GetResult();

        /// <summary>
        /// A convenience method for <code>command.Task.Result</code>. If the task faulted or was canceled,
        /// this will throw the faulting <see cref="Exception"/> or <see cref="TaskCanceledException"/> rather than
        /// the wrapped <see cref="AggregateException"/> thrown by <see cref="Task{TResult}.Result"/>
        /// </summary>
        public CommandResult Result => this.Task.GetAwaiter().GetResult();

        /// <summary>
        /// A <see cref="Task"/> representing the progress of this <see cref="Command"/>
        /// </summary>
        public abstract Task<CommandResult> Task { get; }

        #region ---- Fluent redirection ----
        /// <summary>
        /// Implements <see cref="Command"/> piping as in bash. The first <see cref="Command"/>'s standard output is piped
        /// to the second's standard input. Returns a new <see cref="Command"/> instance whose <see cref="Command.Task"/> tracks
        /// the progress of the entire chain
        /// </summary>
        public Command PipeTo(Command second)
        {
            Throw.IfNull(second, nameof(second));

            return new PipedCommand(this, second);
        }

        /// <summary>
        /// Standard output redirection as in bash. The <see cref="Command"/>'s standard output is written to the given
        /// <paramref name="stream"/>. Returns a new <see cref="Command"/> whose <see cref="Command.Task"/> tracks the progress
        /// of both this <see cref="Command"/> and the IO being performed
        /// </summary>
        public Command RedirectTo(Stream stream)
        {
            Throw.IfNull(stream, nameof(stream));

            return new IOCommand(this, this.StandardOutput.PipeToAsync(stream, leaveStreamOpen: true), StandardIOStream.Out, stream);
        }

        /// <summary>
        /// Standard error redirection as in bash. The <see cref="Command"/>'s standard error is written to the given
        /// <paramref name="stream"/>. Returns a new <see cref="Command"/> whose <see cref="Command.Task"/> tracks the progress
        /// of both this <see cref="Command"/> and the IO being performed
        /// </summary>
        public Command RedirectStandardErrorTo(Stream stream)
        {
            Throw.IfNull(stream, nameof(stream));

            return new IOCommand(this, this.StandardError.PipeToAsync(stream, leaveStreamOpen: true), StandardIOStream.Error, stream);
        }

        /// <summary>
        /// Standard input redirection as in bash. The given <paramref name="stream"/> is written to the <see cref="Command"/>'s
        /// standard output. Returns a new <see cref="Command"/> whose <see cref="Command.Task"/> tracks the progress
        /// of both this <see cref="Command"/> and the IO being performed
        /// </summary>
        public Command RedirectFrom(Stream stream)
        {
            Throw.IfNull(stream, nameof(stream));

            return new IOCommand(this, this.StandardInput.PipeFromAsync(stream, leaveStreamOpen: true), StandardIOStream.In, stream);
        }

        /// <summary>
        /// Standard output redirection as in bash. The <see cref="Command"/>'s standard output is written to the given
        /// <paramref name="file"/>. Returns a new <see cref="Command"/> whose <see cref="Command.Task"/> tracks the progress
        /// of both this <see cref="Command"/> and the IO being performed
        /// </summary>
        public Command RedirectTo(FileInfo file)
        {
            Throw.IfNull(file, nameof(file));

            return new IOCommand(this, this.StandardOutput.PipeToAsync(file), StandardIOStream.Out, file);
        }

        /// <summary>
        /// Standard error redirection as in bash. The <see cref="Command"/>'s standard error is written to the given
        /// <paramref name="file"/>. Returns a new <see cref="Command"/> whose <see cref="Command.Task"/> tracks the progress
        /// of both this <see cref="Command"/> and the IO being performed
        /// </summary>
        public Command RedirectStandardErrorTo(FileInfo file)
        {
            Throw.IfNull(file, nameof(file));

            return new IOCommand(this, this.StandardError.PipeToAsync(file), StandardIOStream.Error, file);
        }

        /// <summary>
        /// Standard input redirection as in bash. The given <paramref name="file"/> is written to the <see cref="Command"/>'s
        /// standard output. Returns a new <see cref="Command"/> whose <see cref="Command.Task"/> tracks the progress
        /// of both this <see cref="Command"/> and the IO being performed
        /// </summary>
        public Command RedirectFrom(FileInfo file)
        {
            Throw.IfNull(file, nameof(file));

            return new IOCommand(this, this.StandardInput.PipeFromAsync(file), StandardIOStream.In, file);
        }

        /// <summary>
        /// Standard output redirection as in bash. The lines of <see cref="Command"/>'s standard output are added to the given
        /// collection (<paramref name="lines"/> Returns a new <see cref="Command"/>  whose <see cref="Command.Task"/> tracks
        /// the progress of both this <see cref="Command"/> and the IO being performed
        /// </summary>
        public Command RedirectTo(ICollection<string> lines)
        {
            Throw.IfNull(lines, nameof(lines));

            return new IOCommand(this, this.StandardOutput.PipeToAsync(lines), StandardIOStream.Out, lines.GetType());
        }

        /// <summary>
        /// Standard error redirection as in bash. The lines of <see cref="Command"/>'s standard error are added to the given
        /// collection (<paramref name="lines"/> Returns a new <see cref="Command"/>  whose <see cref="Command.Task"/> tracks
        /// the progress of both this <see cref="Command"/> and the IO being performed
        /// </summary>
        public Command RedirectStandardErrorTo(ICollection<string> lines)
        {
            Throw.IfNull(lines, nameof(lines));

            return new IOCommand(this, this.StandardError.PipeToAsync(lines), StandardIOStream.Error, lines.GetType());
        }

        /// <summary>
        /// Standard input redirection as in bash. The items in <paramref name="lines"/> are written to the <see cref="Command"/>'s
        /// standard output as lines of text. Returns a new <see cref="Command"/> whose <see cref="Command.Task"/> tracks the
        /// progress of both this <see cref="Command"/> and the IO being performed
        /// </summary>
        public Command RedirectFrom(IEnumerable<string> lines)
        {
            Throw.IfNull(lines, nameof(lines));

            return new IOCommand(this, this.StandardInput.PipeFromAsync(lines), StandardIOStream.In, lines.GetType());
        }

        /// <summary>
        /// Standard output redirection as in bash. The chars of <see cref="Command"/>'s standard output are added to the given
        /// collection (<paramref name="chars"/> Returns a new <see cref="Command"/> whose <see cref="Command.Task"/> tracks
        /// the progress of both this <see cref="Command"/> and the IO being performed
        /// </summary>
        public Command RedirectTo(ICollection<char> chars)
        {
            Throw.IfNull(chars, nameof(chars));

            return new IOCommand(this, this.StandardOutput.PipeToAsync(chars), StandardIOStream.Out, chars.GetType());
        }

        /// <summary>
        /// Standard error redirection as in bash. The chars of <see cref="Command"/>'s standard error are added to the given
        /// collection (<paramref name="chars"/> Returns a new <see cref="Command"/> whose <see cref="Command.Task"/> tracks
        /// the progress of both this <see cref="Command"/> and the IO being performed
        /// </summary>
        public Command RedirectStandardErrorTo(ICollection<char> chars)
        {
            Throw.IfNull(chars, nameof(chars));

            return new IOCommand(this, this.StandardError.PipeToAsync(chars), StandardIOStream.Error, chars.GetType());
        }

        /// <summary>
        /// Standard input redirection as in bash. The items in <paramref name="chars"/> are written to the <see cref="Command"/>'s
        /// standard output. Returns a new <see cref="Command"/> whose <see cref="Command.Task"/> tracks the
        /// progress of both this <see cref="Command"/> and the IO being performed
        /// </summary>
        public Command RedirectFrom(IEnumerable<char> chars)
        {
            Throw.IfNull(chars, nameof(chars));

            return new IOCommand(this, this.StandardInput.PipeFromAsync(chars), StandardIOStream.In, chars.GetType());
        }

        /// <summary>
        /// Standard output redirection as in bash. The <see cref="Command"/>'s standard output is written to the given
        /// <paramref name="writer"/>. Returns a new <see cref="Command"/> whose <see cref="Command.Task"/> tracks the progress
        /// of both this <see cref="Command"/> and the IO being performed
        /// </summary>
        public Command RedirectTo(TextWriter writer)
        {
            Throw.IfNull(writer, nameof(writer));

            return new IOCommand(this, this.StandardOutput.PipeToAsync(writer, leaveWriterOpen: true), StandardIOStream.Out, writer);
        }

        /// <summary>
        /// Standard error redirection as in bash. The <see cref="Command"/>'s standard error is written to the given
        /// <paramref name="writer"/>. Returns a new <see cref="Command"/> whose <see cref="Command.Task"/> tracks the progress
        /// of both this <see cref="Command"/> and the IO being performed
        /// </summary>
        public Command RedirectStandardErrorTo(TextWriter writer)
        {
            Throw.IfNull(writer, nameof(writer));

            return new IOCommand(this, this.StandardError.PipeToAsync(writer, leaveWriterOpen: true), StandardIOStream.Error, writer);
        }

        /// <summary>
        /// Standard input redirection as in bash. The given <paramref name="reader"/> is written to the <see cref="Command"/>'s
        /// standard output. Returns a new <see cref="Command"/> whose <see cref="Command.Task"/> tracks the progress
        /// of both this <see cref="Command"/> and the IO being performed
        /// </summary>
        public Command RedirectFrom(TextReader reader)
        {
            Throw.IfNull(reader, nameof(reader));

            return new IOCommand(this, this.StandardInput.PipeFromAsync(reader, leaveReaderOpen: true), StandardIOStream.In, reader);
        }

        /// <summary>
        /// Returns a single streaming <see cref="IEnumerable{T}"/> which merges the outputs of
        /// <see cref="ProcessStreamReader.GetLines"/> on <see cref="StandardOutput"/> and
        /// <see cref="StandardError"/>. This is similar to doing 2>&amp;1 on the command line.
        ///
        /// Merging at the line level means that interleaving of the outputs cannot break up any single
        /// lines
        /// </summary>
        public IEnumerable<string> GetOutputAndErrorLines() => new MergedLinesEnumerable(standardOutput: this.StandardOutput, standardError: this.StandardError);
        #endregion

        #region ---- Operator overloads ----
        /// <summary>
        /// Implements <see cref="Command"/> piping as in bash. The first <see cref="Command"/>'s standard output is piped
        /// to the second's standard input. Returns a new <see cref="Command"/> instance whose <see cref="Command.Task"/> tracks
        /// the progress of the entire chain
        /// </summary>
        public static Command operator |(Command first, Command second)
        {
            Throw.IfNull(first, "first");
            return first.PipeTo(second);
        }

        #region ---- Standard input and output redirection ----
        /// <summary>
        /// Standard output redirection as in bash. The <see cref="Command"/>'s standard output is written to the given
        /// <paramref name="stream"/>. Returns a new <see cref="Command"/> whose <see cref="Command.Task"/> tracks the progress
        /// of both this <see cref="Command"/> and the IO being performed
        /// </summary>
        public static Command operator >(Command command, Stream stream)
        {
            Throw.IfNull(command, "command");
            return command.RedirectTo(stream);
        }

        /// <summary>
        /// Standard input redirection as in bash. The given <paramref name="stream"/> is written to the <see cref="Command"/>'s
        /// standard output. Returns a new <see cref="Command"/> whose <see cref="Command.Task"/> tracks the progress
        /// of both this <see cref="Command"/> and the IO being performed
        /// </summary>
        public static Command operator <(Command command, Stream stream)
        {
            Throw.IfNull(command, "command");
            return command.RedirectFrom(stream);
        }

        /// <summary>
        /// Standard output redirection as in bash. The <see cref="Command"/>'s standard output is written to the given
        /// <paramref name="file"/>. Returns a new <see cref="Command"/> whose <see cref="Command.Task"/> tracks the progress
        /// of both this <see cref="Command"/> and the IO being performed
        /// </summary>
        public static Command operator >(Command command, FileInfo file)
        {
            Throw.IfNull(command, "command");
            return command.RedirectTo(file);
        }

        /// <summary>
        /// Standard input redirection as in bash. The given <paramref name="file"/> is written to the <see cref="Command"/>'s
        /// standard output. Returns a new <see cref="Command"/> whose <see cref="Command.Task"/> tracks the progress
        /// of both this <see cref="Command"/> and the IO being performed
        /// </summary>
        public static Command operator <(Command command, FileInfo file)
        {
            Throw.IfNull(command, "command");
            return command.RedirectFrom(file);
        }

        /// <summary>
        /// Standard output redirection as in bash. The lines of <see cref="Command"/>'s standard output are added to the given
        /// collection (<paramref name="lines"/> MUST be an instance of <see cref="ICollection{String}"/>; the use of the <see cref="IEnumerable{String}"/>.
        /// type is to provide the required parity with the input redirection operator. Returns a new <see cref="Command"/>
        /// whose <see cref="Command.Task"/> tracks the progress of both this <see cref="Command"/> and the IO being performed
        /// </summary>
        public static Command operator >(Command command, IEnumerable<string> lines)
        {
            Throw.IfNull(command, nameof(command));
            Throw.IfNull(lines, nameof(lines));

            return lines is ICollection<string> linesCollection
                ? command.RedirectTo(linesCollection)
                : throw new ArgumentException("must implement ICollection<string> in order to recieve output", nameof(lines));
        }

        /// <summary>
        /// Standard input redirection as in bash. The items in <paramref name="lines"/> are written to the <see cref="Command"/>'s
        /// standard output as lines of text. Returns a new <see cref="Command"/> whose <see cref="Command.Task"/> tracks the
        /// progress of both this <see cref="Command"/> and the IO being performed
        /// </summary>
        public static Command operator <(Command command, IEnumerable<string> lines)
        {
            Throw.IfNull(command, "command");
            return command.RedirectFrom(lines);
        }

        /// <summary>
        /// Standard output redirection as in bash. The chars of <see cref="Command"/>'s standard output are added to the given
        /// collection (<paramref name="chars"/> MUST be an instance of <see cref="ICollection{Char}"/>; the use of the <see cref="IEnumerable{Character}"/>.
        /// type is to provide the required parity with the input redirection operator. Returns a new <see cref="Command"/>
        /// whose <see cref="Command.Task"/> tracks the progress of both this <see cref="Command"/> and the IO being performed
        /// </summary>
        public static Command operator >(Command command, IEnumerable<char> chars)
        {
            Throw.IfNull(command, nameof(command));
            Throw.IfNull(chars, nameof(chars));

            return chars is ICollection<char> charCollection
                ? command.RedirectTo(charCollection)
                : throw new ArgumentException("must implement ICollection<char> in order to receive output", nameof(chars));
        }

        /// <summary>
        /// Standard input redirection as in bash. The items in <paramref name="chars"/> are written to the <see cref="Command"/>'s
        /// standard output. Returns a new <see cref="Command"/> whose <see cref="Command.Task"/> tracks the
        /// progress of both this <see cref="Command"/> and the IO being performed
        /// </summary>
        public static Command operator <(Command command, IEnumerable<char> chars)
        {
            Throw.IfNull(command, "command");
            return command.RedirectFrom(chars);
        }
        #endregion
        #endregion

        #region ---- Static API ----
        /// <summary>
        /// A convenience method for calling <see cref="Shell.Run(string, IEnumerable{object}, Action{Shell.Options})"/> on <see cref="Shell.Default"/>
        /// </summary>
        public static Command Run(string executable, IEnumerable<object>? arguments = null, Action<Shell.Options>? options = null) =>
            Shell.Default.Run(executable, arguments, options);

        /// <summary>
        /// A convenience method for calling <see cref="Shell.TryAttachToProcess(int, Action{Shell.Options}, out Medallion.Shell.Command)"/> on <see cref="Shell.Default"/>
        /// </summary>
        public static bool TryAttachToProcess(int processId, Action<Shell.Options> options, [NotNullWhen(returnValue: true)] out Command? attachedCommand) =>
            Shell.Default.TryAttachToProcess(processId, options, out attachedCommand);

        /// <summary>
        /// A convenience method for calling <see cref="Shell.TryAttachToProcess(int, out Medallion.Shell.Command)"/> on <see cref="Shell.Default"/>
        /// </summary>
        public static bool TryAttachToProcess(int processId, [NotNullWhen(returnValue: true)] out Command? attachedCommand) =>
            Shell.Default.TryAttachToProcess(processId, out attachedCommand);

        /// <summary>
        /// A convenience method for calling <see cref="Shell.Run(string, object[])"/> on <see cref="Shell.Default"/>
        /// </summary>
        public static Command Run(string executable, params object[] arguments)
        {
            return Shell.Default.Run(executable, arguments);
        }
        #endregion

        // NOTE: we used to also override true, false, ! and & to support boolean conditions as in bash. The problem with
        // this is that a statement like a || b || c uses "|" to evaluate a || b before combining it with c. We already override
        // "|" to be the pipe operator, so this doesn't end up doing what we'd like. Rather than sacrifice piping (which is useful),
        // I'm choosing to sacrifice boolean overloads, which are cool but not particularly useful given that you can just do
        // .Result.Success

        #region ---- Dispose ----
        private int _disposed;

        /// <summary>
        /// Releases all resources associated with this <see cref="Command"/>. This is only required
        /// if the <see cref="Shell.Options.DisposeOnExit"/> has been set to false
        /// </summary>
        void IDisposable.Dispose()
        {
            if (Interlocked.Exchange(ref this._disposed, 1) == 0)
            {
                this.DisposeInternal();
            }
        }

        /// <summary>
        /// Subclass-specific implementation of <see cref="IDisposable.Dispose"/>
        /// </summary>
        protected abstract void DisposeInternal();

        /// <summary>
        /// Throws <see cref="ObjectDisposedException"/> if the <see cref="Command"/> has been disposed
        /// </summary>
        protected void ThrowIfDisposed()
        {
            Throw<ObjectDisposedException>.If(Volatile.Read(ref this._disposed) != 0, () => this.ToString()!);
        }
        #endregion
    }
}
