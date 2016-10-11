#tool "nuget:?package=xunit.runner.console"
var target = Argument("target", "Default");

Task("RestoreNuGetPackages")
    .Does(() =>
{
    NuGetRestore("MedallionShell.sln");
});

Task("CompileSampleCommand")
	.IsDependentOn("RestoreNuGetPackages")
	.Does(() => {
		MSBuild("SampleCommand/SampleCommand.csproj");
	});

Task("CompileNetCore")
	.IsDependentOn("RestoreNuGetPackages")
	.Does(() =>
	{
		DotNetCoreBuild("MedallionShell.NetCore");
	});

Task("TestNetCore")
  .IsDependentOn("CompileNetCore")
  .IsDependentOn("CompileSampleCommand")
  .Does(() =>
	{
		CopyFile("SampleCommand/bin/Debug/SampleCommand.exe", "SampleCommand.exe");
		try {
			DotNetCoreTest("MedallionShell.NetCore.Tests");
		} finally {
			DeleteFile("SampleCommand.exe");
		}
	});

Task("CompileNet45")
	.IsDependentOn("RestoreNuGetPackages")
	.Does(()=>{
		MSBuild("MedallionShell/MedallionShell.csproj");
	});

Task("CompileNet45Tests")
  .IsDependentOn("CompileNet45")
  .Does(() => {
  MSBuild("MedallionShell.Tests/MedallionShell.Tests.csproj");
});

Task("TestNet45")
  .IsDependentOn("CompileNet45")
  .IsDependentOn("CompileNet45Tests")
  .Does(()=>{
  XUnit2("MedallionShell.Tests/bin/Debug/MedallionShell.Tests.dll");
});

// based on http://stackoverflow.com/questions/37814627/cake-nugetpack-configuration
Task("NuGetPack")
	.IsDependentOn("TestNet45")
	.IsDependentOn("TestNetCore")
	.Does(() => NuGetPack(
		GetFiles("MedallionShell.nuspec"), 
		new NuGetPackSettings { Symbols = true }
	));

Task("Default")
  .IsDependentOn("NuGetPack")
  .Does(() =>
{
  Information("Hello World!");
});

RunTarget(target);