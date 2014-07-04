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
        // TODO do we want this in the base class?
        public abstract Process Process { get; }
        public abstract IReadOnlyList<Process> Processes { get; }

        public abstract Stream StandardInputStream { get; }
        public abstract Stream StandardOutputStream { get; }
        public abstract Stream StandardErrorStream { get; }

        public abstract Task<CommandResult> Task { get; }

        public Command PipeTo(Command command)
        {
            return this | command;
        }

        public Command PipeStandardOutputTo(Stream stream)
        {
            return this > stream;
        }

        public Command PipeStandardErrorTo(Stream stream)
        {
            Throw.IfNull(stream, "stream");
            // TODO
            return this;
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

            // TODO

            return command;
        }

        public static Command operator <(Command command, Stream stream)
        {
            Throw.IfNull(command, "command");
            Throw.IfNull(stream, "stream");

            // TODO

            return command;
        }

        public static Command operator >(Command command, string path)
        {
            Throw.IfNull(command, "command");
            Throw.IfNull(path, "path");

            // used over File.OpenWrite to get read file share, which seems potentially useful and
            // not that harmful
            var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
            return command > stream;
        }

        public static Command operator <(Command command, string path)
        {
            Throw.IfNull(command, "command");
            Throw.IfNull(path, "path");

            return command > File.OpenRead(path);
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
            throw new NotSupportedException("Bitwise & is not supported. It exists only to enable '&&'")
        }
        #endregion
    }
}
