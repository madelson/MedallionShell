MedallionShell
==============

MedallionShell is a lightweight library that vastly simplifies working with processes in .NET apps. 

[Download the NuGet package](https://www.nuget.org/packages/medallionshell)

Built on top of the powerful, yet [clunky](http://www.codeducky.org/process-handling-net/) [System.Diagnostics.Process API](http://msdn.microsoft.com/en-us/library/system.diagnostics.process(v=vs.110).aspx), the MedallionShell API streamlines common use-cases, removes [pitfalls](http://www.codeducky.org/process-handling-net/), and integrates Process handling with .NET [async/await](http://msdn.microsoft.com/en-us/library/hh191443.aspx) and [Tasks](http://msdn.microsoft.com/en-us/library/dd460717(v=vs.110).aspx).

```C#
// processes are created and interacted with using the Command class
var cmd = Command.Run("path_to_grep", "some REGEX");
cmd.StandardInput.PipeFromAsync(new FileInfo("some path"));
// by default, all output streams are buffered, so there's no need to worry
// about deadlock
var outputLines = cmd.StandardOutput.GetLines().ToList();
var errorText = cmd.StandardError.ReadToEnd();

// by default, the underlying Process is automatically disposed 
// so no using block is required

// and complex arguments are automatically escaped
var cmd = Command.Run("path_to_grep", "\\ some \"crazy\" REGEX \\");

// we can also do this inline using bash-style operator overloads
var lines = new List<string>();
var cmd = Command.Run("path_to_grep", "some REGEX") < new FileInfo("some path") > lines;
cmd.Wait();

// and we can even chain commands together with the pipe operator
var pipeline = Command.Run("path_to_grep", "some REGEX") | Command.Run("path_to_grep", "another REGEX");

// we can check a command's exit status using it's result
if (cmd.Result.Success) { ... }

// and perform async operations via its associated task
await cmd.Task;

// commands are also highly configurable
var cmd = Command.Run(
	"path_to_grep",
	new[] { "some REGEX" },	
	options: o => o
		// arbitrarily configure the ProcessStartInfo
		.StartInfo(si => si.RedirectStandardError = false)
		// option to turn a non-zero exit code into an exception
		.ThrowOnError()
		// option to kill the command and throw TimeoutException if it doesn't finish
		.Timeout(TimeSpan.FromMinutes(1))
		...
);

// and if we want to keep using a common set of configuration options, we 
// can package them up in a Shell object
var shell = new Shell(o => o.ThrowOnError()...);
shell.Run("path_to_grep", "some REGEX");
```

## Release Notes
- 1.0.3 Fixed bug with standard error (thanks <a href="https://github.com/nsdfxela">nsdfxela</a>!)
- 1.0.2 Fixed bug where timeout would suppress errors from ThrowOnError option
- 1.0.1 Allowed for argument ommission in Command.Run(), other minor fixes 
- 1.0.0 Initial release