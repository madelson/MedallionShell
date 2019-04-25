using System;
using System.Collections.Generic;
using System.Text;
using Medallion.Shell.Signals;

namespace Medallion.Shell
{
    /// <summary>
    /// Represents a "signal" that can be sent to a <see cref="Command"/>
    /// </summary>
    public sealed class CommandSignal
    {
        private CommandSignal(int value)
        {
            this.Value = value;
        }

        internal int Value { get; }

        /// <summary>
        /// The input interupt signal (CTRL_C_EVENT on Windows, SIGINT on Unix).
        /// 
        /// By default, this will terminate the process, but processes may register a handle for the signal
        /// in order to do something else instead (e. g. graceful shutdown)
        /// </summary>
        public static CommandSignal ControlC { get; } =
            // values taken from https://docs.microsoft.com/en-us/windows/console/generateconsolectrlevent and 
            // http://people.cs.pitt.edu/~alanjawi/cs449/code/shell/UnixSignals.htm
            new CommandSignal(PlatformCompatibilityHelper.IsWindows ? (int)NativeMethods.CtrlType.CTRL_C_EVENT : 2);

        /// <summary>
        /// Creates a <see cref="CommandSignal"/> using an operating system-specific numeric value.
        /// The resulting <see cref="CommandSignal"/> will likely NOT be compatible across platforms
        /// </summary>
        public static CommandSignal FromSystemValue(int signal) => new CommandSignal(signal);
    }
}
