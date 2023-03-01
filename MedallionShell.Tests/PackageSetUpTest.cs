using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Medallion.Shell;
using Medallion.Shell.Tests;
using NUnit.Framework;
using NUnit.Framework.Interfaces;

namespace Medallion.Shell.Tests;

internal class PackageSetUpTest
{
    [Test]
    public void TestVersioning()
    {
        var version = typeof(Command).GetTypeInfo().Assembly.GetName().Version!.ToString();
        var informationalVersion = (AssemblyInformationalVersionAttribute)typeof(Command).GetTypeInfo().Assembly.GetCustomAttribute(typeof(AssemblyInformationalVersionAttribute))!;
        Assert.IsNotNull(informationalVersion);
        version.ShouldEqual(Regex.Replace(informationalVersion.InformationalVersion, "[-+].*$", string.Empty) + ".0");
    }

    [Test]
    
    public void TestLibrariesUseConfigureAwaitFalse()
    {
        var solutionDirectory = Path.GetDirectoryName(Path.GetDirectoryName(CurrentFilePath()))!;
        var projectDirectories = new[] { "MedallionShell", "MedallionShell.ProcessSignaler" }
            .Select(d => Path.Combine(solutionDirectory, d))
            .ToList();
        Assert.IsEmpty(projectDirectories.Where(d => !Directory.Exists(d)));

        var codeFiles = projectDirectories.SelectMany(d => Directory.EnumerateFiles(d, "*.cs", SearchOption.AllDirectories))
            .ToArray();
        Assert.IsNotEmpty(codeFiles);

        var awaitRegex = new Regex(@"//.*|(?<await>\bawait\s)");
        var configureAwaitRegex = new Regex(@"\.ConfigureAwait\(false\)|\.TryAwait\(\)");
        foreach (var codeFile in codeFiles)
        {
            var code = File.ReadAllText(codeFile);
            var awaitCount = awaitRegex.Matches(code).Cast<Match>().Count(m => m.Groups["await"].Success);
            var configureAwaitCount = configureAwaitRegex.Matches(code).Count;
            Assert.IsTrue(configureAwaitCount >= awaitCount, $"ConfigureAwait(false) count ({configureAwaitCount}) < await count ({awaitCount}) in {codeFile}");
        }
    }

    private static string CurrentFilePath([CallerFilePath] string filePath = "") => filePath;
}
