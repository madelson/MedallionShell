using Medallion.Shell.Streams;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Medallion.Shell
{
    // TODO kill
    // TODO dispose on exit (defaults to true)

    /// <summary>
    /// Represents an executing <see cref="Process"/> as well as related asynchronous activity (e. g. the piping of
    /// input and output streams)
    /// </summary>
    public abstract partial class Command : IDisposable
    {
        // prevent external inheritors
        internal Command() { }

        /// <summary>
        /// The <see cref="Process"/> associated with this <see cref="Command"/>. In a multi-process command,
        /// this will be the final <see cref="Process"/> in the chain
        /// </summary>
        public abstract Process Process { get; }
        /// <summary>
        /// All <see cref="Process"/>es associated with this <see cref="Command"/>
        /// </summary>
        public abstract IReadOnlyList<Process> Processes { get; }

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
        /// A <see cref="Task"/> representing the progress of this <see cref="Command"/>
        /// </summary>
        public abstract Task<CommandResult> Task { get; }

        #region ---- Operator overloads ----
        /// <summary>
        /// Implements <see cref="Command"/> piping as in bash. The first <see cref="Command"/>'s standard output is piped
        /// to the second's standard input. Returns a new <see cref="Command"/> instance whose <see cref="Command.Task"/> tracks
        /// the progress of the entire chain
        /// </summary>
        public static Command operator |(Command first, Command second)
        {
            return new PipedCommand(first, second);
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
            Throw.IfNull(stream, "stream");

            return new IoCommand(command, command.StandardOutput.PipeToAsync(stream, leaveStreamOpen: true));
        }

        /// <summary>
        /// Standard input redirection as in bash. The given <paramref name="stream"/> is written to the <see cref="Command"/>'s 
        /// standard output. Returns a new <see cref="Command"/> whose <see cref="Command.Task"/> tracks the progress
        /// of both this <see cref="Command"/> and the IO being performed
        /// </summary>
        public static Command operator <(Command command, Stream stream)
        {
            Throw.IfNull(command, "command");

            return new IoCommand(command, command.StandardInput.PipeFromAsync(stream, leaveStreamOpen: true));
        }

        /// <summary>
        /// Standard output redirection as in bash. The <see cref="Command"/>'s standard output is written to the given
        /// <paramref name="file"/>. Returns a new <see cref="Command"/> whose <see cref="Command.Task"/> tracks the progress
        /// of both this <see cref="Command"/> and the IO being performed
        /// </summary>
        public static Command operator >(Command command, FileInfo file)
        {
            Throw.IfNull(command, "command");

            return new IoCommand(command, command.StandardOutput.PipeToAsync(file));
        }

        /// <summary>
        /// Standard input redirection as in bash. The given <paramref name="file"/> is written to the <see cref="Command"/>'s 
        /// standard output. Returns a new <see cref="Command"/> whose <see cref="Command.Task"/> tracks the progress
        /// of both this <see cref="Command"/> and the IO being performed
        /// </summary>
        public static Command operator <(Command command, FileInfo file)
        {
            Throw.IfNull(command, "command");

            return new IoCommand(command, command.StandardInput.PipeFromAsync(file));
        }

        /// <summary>
        /// Standard output redirection as in bash. The lines of <see cref="Command"/>'s standard output are added to the given
        /// collection (<paramref name="lines"/> MUST be an instance of <see cref="ICollection{String}"/>; the use of the <see cref="IEnumerable{String}"/>. 
        /// type is to provide the required parity with the input redirection operator. Returns a new <see cref="Command"/> 
        /// whose <see cref="Command.Task"/> tracks the progress of both this <see cref="Command"/> and the IO being performed
        /// </summary>
        public static Command operator >(Command command, IEnumerable<string> lines)
        {
            Throw.IfNull(command, "command");
            Throw.IfNull(lines, "lines");

            var linesCollection = lines as ICollection<string>;
            Throw.If(linesCollection == null, "lines: must implement ICollection<string> in order to recieve output");

            return new IoCommand(command, command.StandardOutput.PipeToAsync(linesCollection));
        }

        /// <summary>
        /// Standard input redirection as in bash. The items in <paramref name="lines"/> are written to the <see cref="Command"/>'s 
        /// standard output as lines of text. Returns a new <see cref="Command"/> whose <see cref="Command.Task"/> tracks the 
        /// progress of both this <see cref="Command"/> and the IO being performed
        /// </summary>
        public static Command operator <(Command command, IEnumerable<string> lines)
        {
            Throw.IfNull(command, "command");

            return new IoCommand(command, command.StandardInput.PipeFromAsync(lines));
        }
        #endregion

        #region ---- && and || support ----
        /// <summary>
        /// Provides support for use of boolean operators with processes as in bash.
        /// The boolean value of the command is based on the process exit code
        /// </summary>
        public static bool operator true(Command command)
        {
            Throw.IfNull(command, "command");

            return command.Task.Result.Success;
        }

        /// <summary>
        /// Provides support for use of boolean operators with processes as in bash.
        /// The boolean value of the command is based on the process exit code
        /// </summary>
        public static bool operator false(Command command)
        {
            Throw.IfNull(command, "command");

            return !command.Task.Result.Success;
        }

        /// <summary>
        /// This is required to support boolean AND but should never be called. This will always
        /// throw a <see cref="NotSupportedException"/>
        /// </summary>
        public static Command operator &(Command @this, Command that)
        {
            throw new NotSupportedException("Bitwise & is not supported. It exists only to enable '&&'");
        }

        /// <summary>
        /// Provides support for use of boolean operators with processes as in bash.
        /// The boolean value of the command is based on the process exit code
        /// </summary>
        public static bool operator !(Command command)
        {
            Throw.IfNull(command, "command");

            return command ? false : true;
        }
        #endregion
        #endregion

        #region ---- Static API ----
        /// <summary>
        /// A convenience method for calling <see cref="Shell.Run(String, IEnumerable{Object}, Action{Shell.Options})"/> on <see cref="Shell.Default"/>
        /// </summary>
        public static Command Run(string executable, IEnumerable<object> arguments, Action<Shell.Options> options = null)
        {
            return Shell.Default.Run(executable, arguments, options);
        }

        /// <summary>
        /// A convenience method for calling <see cref="Shell.Run(String, Object[])"/> on <see cref="Shell.Default"/>
        /// </summary>
        public static Command Run(string executable, params object[] arguments)
        {
            return Shell.Default.Run(executable, arguments);
        }
        #endregion

        #region ---- Dispose ----
        /// <summary>
        /// Releases all resources associated with this <see cref="Command"/>. This is only required
        /// if the <see cref="Shell.Options.DisposeOnExit"/> has been set to false
        /// </summary>
        void IDisposable.Dispose()
        {
            this.DisposeInternal();
        }

        /// <summary>
        /// Subclass-specific implementation of <see cref="IDisposable.Dispose"/>
        /// </summary>
        protected abstract void DisposeInternal();
        #endregion
    }
}
