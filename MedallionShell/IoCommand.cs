using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Medallion.Shell
{
    internal sealed class IOCommand : Command
    {
        private readonly Command command;
        private readonly Task<CommandResult> task;
        private readonly StandardIOStream standardIOStream;
        // for toString
        private readonly object sourceOrSink;

        public IOCommand(Command command, Task ioTask, StandardIOStream standardIOStream, object sourceOrSink)
        {
            this.command = command;
            this.task = this.CreateTask(ioTask);
            this.standardIOStream = standardIOStream;
            this.sourceOrSink = sourceOrSink;
        }

        private async Task<CommandResult> CreateTask(Task ioTask)
        {
            await ioTask.ConfigureAwait(false);
            var innerResult = await this.command.Task.ConfigureAwait(false);

            // We wrap the inner command's result so that we can apply our stream availability error
            // checking (the Ignore() calls). However, we use the inner result's string values since
            // accessing those consumes the stream and we want both this result and the inner result
            // to have the value.
            return new CommandResult(
                innerResult.ExitCode,
                standardOutput: () =>
                {
                    Ignore(this.StandardOutput);
                    return innerResult.StandardOutput;
                },
                standardError: () =>
                {
                    Ignore(this.StandardError);
                    return innerResult.StandardError;
                }
            );

            void Ignore(object ignored) { }
        }

        public override System.Diagnostics.Process Process
        {
            get { return this.command.Process; }
        }

        public override IReadOnlyList<System.Diagnostics.Process> Processes
        {
            get { return this.command.Processes; }
        }

        public override int ProcessId => this.command.ProcessId;
        public override IReadOnlyList<int> ProcessIds => this.command.ProcessIds;

        public override Streams.ProcessStreamWriter StandardInput => this.standardIOStream != StandardIOStream.In
            ? this.command.StandardInput
            : throw new InvalidOperationException($"{nameof(this.StandardInput)} is unavailable because it is already being piped from {this.sourceOrSink}");

        public override Streams.ProcessStreamReader StandardOutput => this.standardIOStream != StandardIOStream.Out
            ? this.command.StandardOutput
            : throw new InvalidOperationException($"{nameof(this.StandardOutput)} is unavailable because it is already being piped to {this.sourceOrSink}");

        public override Streams.ProcessStreamReader StandardError => this.standardIOStream != StandardIOStream.Error
            ? this.command.StandardError
            : throw new InvalidOperationException($"{nameof(this.StandardError)} is unavailable because it is already being piped to {this.sourceOrSink}");

        public override Task<CommandResult> Task
        {
            get { return this.task; }
        }

        public override void Kill()
        {
            this.command.Kill();
        }

        public override string ToString() => $"{this.command} {ToString(this.standardIOStream)} {this.sourceOrSink}";

        protected override void DisposeInternal()
        {
            this.command.As<IDisposable>().Dispose();
        }

        private static string ToString(StandardIOStream standardIOStream) => standardIOStream switch
        {
            StandardIOStream.In => "<",
            StandardIOStream.Out => ">",
            StandardIOStream.Error => "2>",
            _ => throw new InvalidOperationException("should never get here"),
        };
    }

    internal enum StandardIOStream
    {
        In,
        Out,
        Error,
    }
}
