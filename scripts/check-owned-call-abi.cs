// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System.Diagnostics;
using System.Runtime.InteropServices;

if (!OperatingSystem.IsWindows() || RuntimeInformation.ProcessArchitecture != Architecture.X64)
{
    throw new PlatformNotSupportedException("The owned-call ABI probe requires Windows x64.");
}

var vswhere = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
    "Microsoft Visual Studio", "Installer", "vswhere.exe");
var discover = new ProcessStartInfo(vswhere);
foreach (var argument in new[] { "-latest", "-products", "*", "-requires", "Microsoft.VisualStudio.Component.VC.Tools.x86.x64",
    "-property", "installationPath" })
{
    discover.ArgumentList.Add(argument);
}
var installation = (await Run(discover, TimeSpan.FromSeconds(10)).ConfigureAwait(false)).Trim();
var environment = Path.Combine(installation, "VC", "Auxiliary", "Build", "vcvars64.bat");
if (installation.Length == 0 || !File.Exists(environment))
{
    throw new FileNotFoundException("Visual Studio x64 C++ build tools are required.", environment);
}

var artifacts = Path.GetFullPath("artifacts");
Directory.CreateDirectory(artifacts);
var cli = Path.GetFullPath(Path.Combine("artifacts", "bin", "sources", "terrumc", "Debug", "net11.0", "terrumc.dll"));
var dll = Path.Combine(artifacts, "owned-call-abi.dll");
var executable = Path.Combine(artifacts, "owned-call-abi-probe.exe");
var objectFile = Path.Combine(artifacts, "owned-call-abi-probe.obj");
var buildLibrary = new ProcessStartInfo("dotnet");
foreach (var argument in new[] { cli, "build-library",
    Path.GetFullPath(Path.Combine("tests", "native", "owned-call-abi.terrum")),
    "--export", "Consume", "--export", "Relay", "--export", "RelayWide", "--export", "ConsumeWide",
    "--export", "RelayLast", "--export", "TryTicket", "--export", "TryWide", "--export", "TryPartial",
    "--export", "CatchOutputFailure", "--export", "LaterArgumentFailure",
    "-o", dll, "--runtime", "checked-exceptions" })
{
    buildLibrary.ArgumentList.Add(argument);
}
var libraryOutput = await Run(buildLibrary, TimeSpan.FromMinutes(2)).ConfigureAwait(false);
var source = Path.GetFullPath(Path.Combine("tests", "native", "owned-call-abi-probe.c"));
var build = new ProcessStartInfo(Environment.GetEnvironmentVariable("ComSpec")
    ?? throw new InvalidOperationException("ComSpec is unset.")) {
    Arguments = $"/d /s /c \"call \"{environment}\" >nul && cl /nologo /W4 /WX /O2 /TC /Fo\"{objectFile}\" /Fe\"{executable}\" \"{source}\" /link /incremental:no\"",
};
var nativeOutput = await Run(build, TimeSpan.FromMinutes(2)).ConfigureAwait(false);
await File.WriteAllTextAsync(Path.Combine(artifacts, "owned-call-abi-build.log"), libraryOutput + nativeOutput).ConfigureAwait(false);
var execute = new ProcessStartInfo(executable);
execute.ArgumentList.Add(dll);
var proof = await Run(execute, TimeSpan.FromSeconds(10)).ConfigureAwait(false);
await File.WriteAllTextAsync(Path.Combine(artifacts, "owned-call-abi-proof.log"), proof).ConfigureAwait(false);
if (proof.Trim() != "owned-call-abi direct=4 indirect=16 stack=6 publication=checked recovery=42")
{
    throw new InvalidOperationException("The independent native owned-call ABI proof did not complete.");
}
Console.Write(proof);

static async Task<string> Run(ProcessStartInfo start, TimeSpan timeout)
{
    start.UseShellExecute = false;
    start.CreateNoWindow = true;
    start.RedirectStandardOutput = true;
    start.RedirectStandardError = true;
    using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start owned-call ABI probe command.");
    var stdout = process.StandardOutput.ReadToEndAsync();
    var stderr = process.StandardError.ReadToEndAsync();
    try
    {
        await process.WaitForExitAsync().WaitAsync(timeout).ConfigureAwait(false);
    }
    catch (TimeoutException)
    {
        if (!process.HasExited)
        {
            process.Kill(entireProcessTree: true);
        }
        await process.WaitForExitAsync().ConfigureAwait(false);
        throw;
    }
    var output = await stdout.ConfigureAwait(false);
    var error = await stderr.ConfigureAwait(false);
    if (process.ExitCode != 0)
    {
        throw new InvalidOperationException($"Owned-call ABI probe failed ({process.ExitCode}):\n{output}\n{error}");
    }
    return output;
}
