MedallionShell

==============

MedallionShell vastly simplifies working with processes in .NET. 

[Download the NuGet package](https://www.nuget.org/packages/medallionshell) [![NuGet Status](http://img.shields.io/nuget/v/MedallionShell.svg?style=flat)](https://www.nuget.org/packages/MedallionShell/) ([Release notes](#release-notes)). There is also a [strong-named release](https://www.nuget.org/packages/medallionshell.strongname) [![NuGet Status](http://img.shields.io/nuget/v/MedallionShell.StrongName.svg?style=flat)](https://www.nuget.org/packages/MedallionShell.StrongName/)

With MedallionShell, running a process is as simple as:
```C#
Command.Run("git", "commit", "-m", "critical bugfix").Wait();
```

## Why MedallionShell?

.NET ships with the powerful `System.Diagnostics.Process` class built in. However, the `Process` API is clunky to use and there are [many pitfalls which must be accounted for even in basic scenarios](https://github.com/steaks/codeducky/blob/master/blogs/Processes.md). MedallionShell is built on top of `Process` and focuses on streamlining common use-cases while eliminating or containing traps so that things "just work" as much as possible.

Here are some of the things the library takes care of for you:
* Clean integration with async/await and `Task`
* Piping standard IO streams to and from various sources without creating deadlocks or race conditions
* Properly escaping process arguments (a common source of security vulnerabilities)
* Being able to recover from hangs through timeout, `CancellationToken`, and safe kill, and signal support
* Cross-platform support (e. g. signals and workarounds for Mono oddities [#6](https://github.com/madelson/MedallionShell/issues/6), [#22](https://github.com/madelson/MedallionShell/issues/22), [#43](https://github.com/madelson/MedallionShell/issues/43), and [#44](https://github.com/madelson/MedallionShell/issues/44))

## API Overview

### Commands

The `Command` class represents an executing process:
```C#
// create a command via Command.Run
var command = Command.Run("executable", "arg1", "arg2", ...);

// wait for it to finish
command.Wait(); // or...
var result = command.Result; // or...
result = await command.Task;

// inspect the result
if (!result.Success)
{
	Console.Error.WriteLine($"command failed with exit code {result.ExitCode}: {result.StandardError}");
}
```
The `Command.Task` property means that you can easily compose the `Command`'s execution with other `Task`-based async operations. You can terminate a `Command` by invoking its `Kill()` method.

Most APIs create a `Command` instance by starting a new process. However, you can also create a `Command` from an existing process via the `Command.TryAttachToProcess` API.

### Standard IO

One of the main ways to interact with a process is via its [standard IO streams](https://en.wikipedia.org/wiki/Standard_streams) (in, out and error). By default, MedallionShell configures the process to enable these streams and captures standard error and standard output in the `Command`'s result.

Additionally/alternatively, you can interact with these streams directly via the `Command.StandardInput`, `Command.StandardOutput`, and `Command.StandardError` properties. As with `Process`, these are `TextWriter`/`TextReader` objects that also expose the underlying `Stream`, giving you the option of writing/reading either text or raw bytes.

The standard IO streams also contain methods for piping to and from common sinks and sources, including `Stream`s, `TextReader/Writer`s, files, and collections. For example:
```C#
command.StandardInput.PipeFromAsync(new FileInfo("input.csv")); // pipes in all bytes from input.csv
var outputLines = new List<string>();
command.StandardOutput.PipeToAsync(outputLines); // pipe output text to a collection
```

You can also express piping directly on the `Command` object. This returns a new `Command` instance which represents both the underlying process execution and the IO piping operation, providing one thing you can await to know when everything has completed. You can even use this feature to chain together commands (like the `|` operator on the command line).
```C#
await Command.Run("processingStep1.exe")
	.RedirectFrom(new FileInfo("input.txt"))
	.PipeTo(Command.Run("processingStep2.exe"))
	.RedirectTo(new FileInfo("output.txt"));
	
// alternatively, this can be expressed with operators as on the command line
await Command.Run("ProcssingStep1.exe") < new FileInfo("input.txt")
	| Command.Run("processingStep2.exe") > new FileInfo("output.text");
```

### Stopping a Command

You can immediately terminate a command with the `Kill()` API. You can also use the `TrySignalAsync` API to send other types of signals which can allow for graceful shutdown if the target process handles them. `CommandSignal.ControlC` works across platforms, while other signals are OS-specific.

### Command Options

When constructing a `Command`, you can specify various options to provide additional configuration:
```C#
Command.Run("foo.exe", new[] { "arg1" }, options => options.ThrowOnError()...);
```

The supported options are:

|Option|Description|Default|
| --- | --- | --- |
|**ThrowOnError**|If true, the command will throw an exception if the underlying process returns a non-zero exit code rather than returning a failed result|`false`|
|**WorkingDirectory**|Sets the initial working directory for the process|`Environment.CurrentDirectory`|
|**CancellationToken**|Specifies a `CancellationToken` which will kill the process if canceled|`CancellationToken.None`|
|**Timeout**|Specifies a time period after which the process will be killed|`Timeout.Infinite`|
|**StartInfo**|Specifies arbitrary additional configuration of the `ProcessStartInfo` object| |
|**DisposeOnExit**|If true, the underlying `Process` object will be disposed when the process exits, removing the need to call `Command.Dispose()`|`true`|
|**EnvironmentVariable(s)**|Specifies environment variable overrides for the process|`Environment.GetEnvironmentVariables()`|
|**Encoding**|Specifies an `Encoding` to be used on all standard IO streams|`Console.OutputEncoding`/`Console.InputEncoding`: note that what this is varies by platform!|
|**Command**|Specifies arbitrary additional configuration of the `Command` object after it is created (generally only useful with `Shell`s, which are described below) | |

### Shells
It is frequently the case that within the context of a single application all the `Command`s you invoke will want the same or very similar options. To simplify this, you can package up a set of options in a `Shell` object for convenient re-use:
```C#
private static readonly Shell MyShell = new Shell(options => options.ThrowOnError().Timeout(...)...);

...

var command = MyShell.Run("foo.exe", new[] { "arg1", ... }, options => /* can still override/specify further options */);
```

### Strong naming
MedallionShell 1.x is not strong-named. In 1.x, a parallel strong-named package [MedallionShell.StrongName](https://www.nuget.org/packages/medallionshell.strongname) is maintained alongside with identical contents.

This package is published from the [strong-name](https://github.com/madelson/MedallionShell/tree/strong-name) branch.

## Contributing
Contributions are welcome! Please report any issues you encounter or ideas for enhancements. If you would like to contribute code, I ask that you file an issue first so that we can work out the details before you start coding and avoid wasted effort on your part.

**To build the code**, you will need VisualStudio 2019 or higher (community edition is fine) [download](https://www.visualstudio.com/vs/community/). Running all tests will require that you have installed Mono (for the Mono compat tests only).

Windows: [![Build status](https://ci.appveyor.com/api/projects/status/9idbmymiatbd8ncx/branch/master?svg=true)](https://ci.appveyor.com/project/madelson/medallionshell/branch/master) 
Linux: [![Build Status](https://travis-ci.com/madelson/MedallionShell.svg?branch=master)](https://travis-ci.com/madelson/MedallionShell)

## Release Notes
- 1.6.2
	- Add net471 build as workaround for [#75](https://github.com/madelson/MedallionShell/issues/75). Thanks [Cloudmersive](https://github.com/Cloudmersive) for reporting the issue and testing the fix!
- 1.6.1 
	- Strong-named release [MedallionShell.StrongName](https://www.nuget.org/packages/medallionshell.strongname) ([#65](https://github.com/madelson/MedallionShell/issues/65)). Thanks [ldennington](https://github.com/ldennington)!
	- Fixes transient error in signaling on Windows machines with slow disks ([#61](https://github.com/madelson/MedallionShell/issues/61))
	- Reduces dependency footprint for .NET Standard 2.0 ([#56](https://github.com/madelson/MedallionShell/issues/56))
	- Improves error messaging when trying to access the standard IO streams of Commands when those streams have been piped elsewhere ([#59](https://github.com/madelson/MedallionShell/issues/59), [#41](https://github.com/madelson/MedallionShell/issues/41))
	- Adds C#8 nullable reference type annotations
- 1.6.0
	- Adds `Command.TryAttachToProcess` API for creating a `Command` attached to an already-running process ([#30](https://github.com/madelson/MedallionShell/issues/30)). Thanks [konrad-kruczynski](konrad-kruczynski) for coming up with the idea and implementing!	
	- Adds `Command.TrySignal` API which provides cross-platform support for the CTRL+C (SIGINT) signal as well as support for OS-specific signals ([#35](https://github.com/madelson/MedallionShell/issues/35))
	- Properly escape command line arguments when running under Mono on Unix. With this change, the default behavior should work across all platforms ([#44](https://github.com/madelson/MedallionShell/issues/44))
	- Make `StandardInput.Dispose()` work properly when running under Mono on Unix ([#43](https://github.com/madelson/MedallionShell/issues/43))
	- Add .NET Standard 2.0 and .NET 4.6 build targets so that users of more modern frameworks can take advantage of more modern APIs. The .NET Standard 1.3 and .NET 4.5 targets will likely be retired in the event of a 2.0 release.
	- Allow for setting piping and redirection via a `Shell` option with the new `Command(Func<Command, Command>)` option ([#39](https://github.com/madelson/MedallionShell/issues/39))
	- Add CI testing for Mono and .NET Core on Linux
- 1.5.1 Improves Mono.Android compatibility ([#22](https://github.com/madelson/MedallionShell/issues/22)). Thanks [sushihangover](https://github.com/sushihangover) for reporting the issue and testing the fix!
- 1.5.0
	- Command overrides `ToString()` to simplify debugging ([#19](https://github.com/madelson/MedallionShell/issues/19)). Thanks [Stephanvs](https://github.com/Stephanvs)!
	- WindowsCommandLineSyntax no longer quotes arguments that don't require it
- 1.4.0 
	- Adds cancellation support ([#18](https://github.com/madelson/MedallionShell/issues/18))
	- Adds API for getting the underlying process ID for a command even with the DisposeOnExit option ([#16](https://github.com/madelson/MedallionShell/issues/16))
	- Adds API for consuming standard out and standard error lines together as a single stream ([#14](https://github.com/madelson/MedallionShell/issues/14))
	- Improves Mono compatibility ([#6](https://github.com/madelson/MedallionShell/issues/6))
	- Changes `Command.Result` and `Command.Wait()` to throw unwrapped exceptions instead of `AggregateException`
- 1.3.0 Fixes default standard IO stream encodings (thanks [xjfnet](https://github.com/xjfnet)!) and added support for specifying a custom encoding
- 1.2.1 Adds .NET Core support (thanks [kal](https://github.com/kal)!), adds new fluent APIs for each of the piping/redirection operators, and now respects StandardInput.AutoFlush when piping between commands
- 1.1.0 Adds AutoFlush support to StandardInput, and fixed bug where small amounts of flushed data became "stuck" in the StandardOutput buffer
- 1.0.3 Fixes bug with standard error (thanks <a href="https://github.com/nsdfxela">nsdfxela</a>!)
- 1.0.2 Fixes bug where timeout would suppress errors from ThrowOnError option
- 1.0.1 Allows for argument ommission in Command.Run(), other minor fixes 
- 1.0.0 Initial release
