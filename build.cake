#tool "nuget:?package=xunit.runner.console"
var target = Argument("target", "Default");

Task("CompileSampleCommand")
  .Does(() => {
  MSBuild("SampleCommand/SampleCommand.csproj");
});


Task("CompileNetCore")
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

Task("Default")
  .IsDependentOn("TestNet45")
  .IsDependentOn("TestNetCore")
  .Does(() =>
{
  Information("Hello World!");
});

RunTarget(target);