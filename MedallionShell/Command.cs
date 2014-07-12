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

        public Command PipeStandardInputFrom(Stream stream)
        {
            return this < stream;
        }

        #region ---- Operator overloads ----
        public static Command operator |(Command first, Command second)
        {
            return new PipedCommand(first, second);
        }

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
            command.inputTask = System.Threading.Tasks.Task.Run(async () =>
            {
                await stream.CopyToAsync(command.StandardInput.BaseStream);
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
