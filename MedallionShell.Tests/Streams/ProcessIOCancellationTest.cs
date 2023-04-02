using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;
using NUnit.Framework;
using SampleCommand;

namespace Medallion.Shell.Tests.Streams;

internal class ProcessIOCancellationTest
{
    [Test]
    public void TestProcessIOIsCancellable()
    {
        using Process process = new()
        {
            StartInfo =
            {
#if NETFRAMEWORK
                FileName = PlatformCompatibilityTests.SampleCommandPath,
                Arguments = "echo",
#else
                FileName = PlatformCompatibilityTests.DotNetPath,
                Arguments = PlatformCompatibilityHelper.DefaultCommandLineSyntax.CreateArgumentString(new[] { PlatformCompatibilityTests.SampleCommandPath, "echo" }),
#endif
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
            },
        };
        process.Start();
        try
        {
            using CancellationTokenSource cancellationTokenSource = new();
            var readTask = DoIOWithCancellationAsync(process.StandardOutput.BaseStream, cancellationTokenSource.Token);
            var writeTask = Task.Run(async () => 
            {
                // For write, loop to fill up the buffer; eventually we'll block
                while (true) { await DoIOWithCancellationAsync(process.StandardInput.BaseStream, cancellationTokenSource.Token); }
            });
            Task.WaitAny(new[] { readTask, writeTask }, TimeSpan.FromSeconds(0.5)).ShouldEqual(-1);
            
            cancellationTokenSource.Cancel();

            Assert.IsTrue(Task.WhenAll(readTask, writeTask).ContinueWith(_ => { }).Wait(TimeSpan.FromSeconds(10)));
            Assert.True(readTask.IsCanceled || readTask.Exception?.InnerException is OperationCanceledException);
            Assert.True(writeTask.IsCanceled || writeTask.Exception?.InnerException is OperationCanceledException);
        }
        finally
        {
            process.Kill();
        }
    }

    private static Task DoIOWithCancellationAsync(Stream processStream, CancellationToken cancellationToken)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return processStream.CanRead
                ? processStream.ReadAsync(new byte[Constants.ByteBufferSize], 0, Constants.ByteBufferSize, cancellationToken)
                : processStream.WriteAsync(new byte[Constants.ByteBufferSize], 0, Constants.ByteBufferSize, cancellationToken);
        }

        return Task.Factory.StartNew(
            () =>
            {
                var threadId = NativeMethods.GetCurrentThreadId();
                using var threadHandle = NativeMethods.OpenThread(
                    dwDesiredAccess: NativeMethods.ThreadTerminateAccess,
                    bInheritHandle: false,
                    dwThreadId: threadId);
                object completedSentinel = new(), syncCancelSentinel = new();
                object? marker = null;
                using var registration = cancellationToken.Register(() => 
                {
                    if (NativeMethods.GetCurrentThreadId() == threadId)
                    {
                        Interlocked.Exchange(ref marker, syncCancelSentinel);
                        return;
                    }

                    ManualResetEventSlim @event = new(initialState: false);
                    if (Interlocked.CompareExchange(ref marker, @event, comparand: null) == completedSentinel)
                    {
                        // too late to cancel: op has already finished
                        @event.Dispose();
                        return;
                    }

                    SpinWait spinWait = default;
                    while (!NativeMethods.CancelSynchronousIo(threadHandle)
                        && Interlocked.CompareExchange(ref marker, null, comparand: completedSentinel) != completedSentinel)
                    {
                        spinWait.SpinOnce();
                    }

                    @event.Set();
                });
                if (Interlocked.Exchange(ref marker, null) == syncCancelSentinel)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }
                try
                {
                    if (processStream.CanRead)
                    {
                        processStream.Read(new byte[Constants.ByteBufferSize], 0, Constants.ByteBufferSize);
                    }
                    else
                    {
                        processStream.Write(new byte[Constants.ByteBufferSize], 0, Constants.ByteBufferSize);
                    }
                }
                finally
                {
                    if (Interlocked.Exchange(ref marker, completedSentinel) is ManualResetEventSlim @event)
                    {
                        @event.Wait();
                        @event.Dispose();
                    }
                }
            }, 
            TaskCreationOptions.LongRunning);
    }

    private static class NativeMethods
    {
        // From https://learn.microsoft.com/en-us/windows/win32/procthread/thread-security-and-access-rights
        public const int ThreadTerminateAccess = 1;

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern uint GetCurrentThreadId();

        [DllImport("kernel32", SetLastError = true)]
        public static extern bool CloseHandle(IntPtr handle);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool CancelSynchronousIo(ThreadHandle hThread);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern ThreadHandle OpenThread(uint dwDesiredAccess, bool bInheritHandle, uint dwThreadId);

        internal sealed class ThreadHandle : SafeHandleZeroOrMinusOneIsInvalid
        {
            private ThreadHandle()
                : base(ownsHandle: true) { }

            protected override bool ReleaseHandle() => CloseHandle(this.handle);
        }
    }
}
