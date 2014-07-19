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
    public abstract partial class Command
    {
        // TODO task management & error handling
        // => if the process finishes normally, but the input task fails, is that an overall failure?
        // => output tasks should definitely be a cause for failure
        //
        // If you call > BEFORE a command has completed or before the task was observed, the error
        // should go as part of the task. Otherwise, this will FAIL because the content has already been wrapped
        // up in string form alternatively, this could run synchronously at that point

        // TODO should be in the ProcessCommand class. All operator overloads should call virtual command methods
        // that each subclass can override
        private Task inputTask;

        // prevent external inheritors
        internal Command() { }

        public abstract Process Process { get; }
        public abstract IReadOnlyList<Process> Processes { get; }

        public abstract StreamWriter StandardInput { get; }
        public abstract ProcessStreamReader StandardOutput { get; }
        public abstract ProcessStreamReader StandardError { get; }

        public abstract Task<CommandResult> Task { get; }

        public Command PipeTo(Command command)
        {
            return this | command;
        }

        // TODO put this in ProcessStreamWriter
        public Command PipeStandardInputFrom(Stream stream)
        {
            return this < stream;
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

            command.StandardOutput.PipeTo(stream);

            return command;
        }

        public static Command operator <(Command command, Stream stream)
        {
            Throw.IfNull(command, "command");
            Throw.IfNull(stream, "stream");

            // TODO don't allow this if this process has already exited?
            // TODO error handling
            command.inputTask = System.Threading.Tasks.Task.Run(async () =>
            {
                await stream.CopyToAsync(command.StandardInput.BaseStream).ConfigureAwait(false);
                Log.WriteLine("Stream input redirect: closing input to {0}", command.Processes[0].Id);
                command.StandardInput.Close();
            });

            return command;
        }

        public static Command operator >(Command command, FileInfo file)
        {
            Throw.IfNull(command, "command");
            Throw.IfNull(file, "file");

            // used over FileInfo.OpenWrite to get read file share, which seems potentially useful and
            // not that harmful
            var stream = new FileStream(file.FullName, FileMode.Create, FileAccess.Write, FileShare.Read);
            command.Task.ContinueWith(_ => stream.Close());
            return command > stream;
        }

        public static Command operator <(Command command, FileInfo file)
        {
            Throw.IfNull(command, "command");
            Throw.IfNull(file, "file");

            var stream = file.OpenRead();
            command.Task.ContinueWith(_ => stream.Close());
            return command < stream;
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
            Throw.IfNull(lines, "lines");

            var pipeLinesTask = command.PipeLinesFromEnumerableAsync(lines);
            // TODO error handling
            return command;
        }

        private async Task PipeLinesFromEnumerableAsync(IEnumerable<string> lines)
        {
            foreach (var line in lines)
            {
                await this.StandardInput.WriteLineAsync(line).ConfigureAwait(false);
            }
            this.StandardInput.Close();
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
