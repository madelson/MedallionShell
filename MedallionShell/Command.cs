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
    // TODO command should be disposable

    public abstract partial class Command
    {
        // TODO task management & error handling
        // => if the process finishes normally, but the input task fails, is that an overall failure?
        // => output tasks should definitely be a cause for failure
        //
        // If you call > BEFORE a command has completed or before the task was observed, the error
        // should go as part of the task. Otherwise, this will FAIL because the content has already been wrapped
        // up in string form alternatively, this could run synchronously at that point
        //
        // Another note: when piping TO a process like HEAD that will cut you off, 

        // prevent external inheritors
        internal Command() { }

        public abstract Process Process { get; }
        public abstract IReadOnlyList<Process> Processes { get; }

        public abstract ProcessStreamWriter StandardInput { get; }
        public abstract ProcessStreamReader StandardOutput { get; }
        public abstract ProcessStreamReader StandardError { get; }

        public abstract Task<CommandResult> Task { get; }

        public Task PipeToAsync(Command command)
        {
            Throw.IfNull(command, "command");

            return command.StandardInput.PipeFromAsync(this.StandardOutput.BaseStream);
        }

        #region ---- Operator overloads ----
        public static Command operator |(Command first, Command second)
        {
            return new PipedCommand(first, second);
        }

        #region ---- Standard input and output redirection ----
        public static Command operator >(Command command, Stream stream)
        {
            Throw.IfNull(command, "command");
            Throw.IfNull(stream, "stream");

            return new IoCommand(command, command.StandardOutput.PipeToAsync(stream, leaveStreamOpen: true));
        }

        public static Command operator <(Command command, Stream stream)
        {
            Throw.IfNull(command, "command");

            return new IoCommand(command, command.StandardInput.PipeFromAsync(stream, leaveStreamOpen: true));
        }

        public static Command operator >(Command command, FileInfo file)
        {
            Throw.IfNull(command, "command");

            return new IoCommand(command, command.StandardOutput.PipeToAsync(file));
        }

        public static Command operator <(Command command, FileInfo file)
        {
            Throw.IfNull(command, "command");

            return new IoCommand(command, command.StandardInput.PipeFromAsync(file));
        }

        public static Command operator >(Command command, IEnumerable<string> lines)
        {
            Throw.IfNull(command, "command");
            Throw.IfNull(lines, "lines");

            var linesCollection = lines as ICollection<string>;
            Throw.If(linesCollection == null, "lines: must implement ICollection<string> in order to recieve output");

            var pipeLinesTask = command.PipeLinesToCollectionAsync(linesCollection);
            // TODO error handling?
            return command;
        }

        private async Task PipeLinesToCollectionAsync(ICollection<string> lines)
        {
            string line;
            while ((line = await this.StandardOutput.ReadLineAsync().ConfigureAwait(false)) != null)
            {
                lines.Add(line);
            }
        }

        public static Command operator <(Command command, IEnumerable<string> lines)
        {
            Throw.IfNull(command, "command");

            return new IoCommand(command, command.StandardInput.PipeFromAsync(lines));
        }
        #endregion

        #region ---- && and || support ----
        public static bool operator true(Command command)
        {
            Throw.IfNull(command, "command");

            return command.Task.Result.Success;
        }

        public static bool operator false(Command command)
        {
            Throw.IfNull(command, "command");

            return !command.Task.Result.Success;
        }

        public static Command operator &(Command @this, Command that)
        {
            throw new NotSupportedException("Bitwise & is not supported. It exists only to enable '&&'");
        }
        #endregion
        #endregion

        #region ---- Static API ----
        public static Command Run(string executable, IEnumerable<string> arguments, Action<Shell.Options> options = null)
        {
            return Shell.Default.Run(executable, arguments, options);
        }

        public static Command Run(string executable, params string[] arguments)
        {
            return Shell.Default.Run(executable, arguments);
        }
        #endregion
    }
}
