using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Medallion.Shell.Streams;

namespace Medallion.Shell
{
    internal sealed class AttachedCommand : Command
    {
        private const string StreamPropertyExceptionMessage =
            "This property cannot be used when attaching to already running process.";
        private readonly Process process;
        private readonly Task<CommandResult> commandResultTask;
        private readonly bool disposeOnExit;
        private readonly Lazy<ReadOnlyCollection<Process>> processes;
        
        internal AttachedCommand(
            Process process,
            bool throwOnError,
            TimeSpan timeout,
            CancellationToken cancellationToken,
            bool disposeOnExit)
        {
            this.process = process;
            this.disposeOnExit = disposeOnExit;
            var processMonitoringTask = CreateProcessMonitoringTask(process);
            var processTask = ProcessHelper.CreateProcessTask(this.process, processMonitoringTask, throwOnError, timeout, cancellationToken);

            this.commandResultTask = processTask.ContinueWith(
                continuedTask =>
                {
                    if (disposeOnExit)
                    {
                        this.process.Dispose();
                    }
                    return new CommandResult(continuedTask.Result, this);
                },
                TaskContinuationOptions.ExecuteSynchronously
            );

            this.processes = new Lazy<ReadOnlyCollection<Process>>(() => new ReadOnlyCollection<Process>([this.process]));
        }

        public override Process Process
        {
            get
            {
                this.ThrowIfDisposed();
                Throw<InvalidOperationException>.If(
                    this.disposeOnExit,
                    ProcessHelper.ProcessNotAccessibleWithDisposeOnExitEnabled
                );
                return this.process;
            }
        }

        public override IReadOnlyList<Process> Processes
        {
            get
            {
                this.ThrowIfDisposed();
                return this.processes.Value;
            }
        }

        public override int ProcessId
        {
            get
            {
                this.ThrowIfDisposed();
                
                return this.process.Id;
            }
        }

        public override IReadOnlyList<int> ProcessIds => new ReadOnlyCollection<int>([this.ProcessId]);

        public override ProcessStreamWriter StandardInput => throw new InvalidOperationException(StreamPropertyExceptionMessage);

        public override ProcessStreamReader StandardOutput => throw new InvalidOperationException(StreamPropertyExceptionMessage);

        public override ProcessStreamReader StandardError => throw new InvalidOperationException(StreamPropertyExceptionMessage);

        public override void Kill()
        {
            this.ThrowIfDisposed();

            ProcessHelper.TryKillProcess(this.process);
        }

        public override Task<CommandResult> Task => this.commandResultTask;

        protected override void DisposeInternal()
        {
            this.process.Dispose();
        }

        private static Task CreateProcessMonitoringTask(Process process)
        {
            var taskBuilder = new TaskCompletionSource<bool>();
            
            try
            {
                process.EnableRaisingEvents = true;
            }
            // EnableRaisingEvents will throw if the process has already exited; to account for
            // that race condition we return a completed task in that case
            catch (InvalidOperationException) when (process.HasExited)
            {
                taskBuilder.SetResult(false);
                return taskBuilder.Task;
            }

            process.Exited += (sender, e) => taskBuilder.TrySetResult(false);

            // we must account for the race condition where the process exits between enabling events and
            // subscribing to Exited. Therefore, we do exit check after the subscription to account
            // for this
            if (process.HasExited)
            {
                taskBuilder.TrySetResult(false);
            }

            return taskBuilder.Task;
        }
    }
}
