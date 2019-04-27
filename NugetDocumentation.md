MedallionShell vastly simplifies working with processes in .NET. 

With MedallionShell, running a process is as simple as:
```C#
Command.Run("git", "commit", "-m", "critical bugfix").Wait();
```

.NET ships with the powerful `System.Diagnostics.Process` class built in. However, the `Process` API is clunky to use and there are [many pitfalls which must be accounted for even in basic scenarios](https://github.com/steaks/codeducky/blob/master/blogs/Processes.md). MedallionShell is built on top of `Process` and focuses on streamlining common use-cases while eliminating or containing traps so that things "just work" as much as possible.

Here are some of the things the library takes care of for you:
* Clean integration with async/await and `Task`
* Piping standard IO streams to and from various sources without creating deadlocks or race conditions
* Properly escaping process arguments (a common source of security vulnerabilities)
* Being able to recover from hangs through timeout, `CancellationToken`, and safe kill, and signal support
* Cross-platform support (e. g. signals and workarounds for Mono oddities)

**To learn more**, check out the [full documentation](https://github.com/madelson/MedallionShell/blob/master/README.md).