// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System.Diagnostics;
using System.Runtime.InteropServices;

if (!OperatingSystem.IsWindows() || RuntimeInformation.ProcessArchitecture != Architecture.X64)
{
    throw new PlatformNotSupportedException("The managed-node ABI probe requires Windows x64.");
}

var artifacts = Path.GetFullPath("artifacts");
Directory.CreateDirectory(artifacts);
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
var executable = Path.Combine(artifacts, "managed-node-abi-probe.exe");
var objectFile = Path.Combine(artifacts, "managed-node-abi-probe.obj");
var source = Path.GetFullPath(Path.Combine("tests", "native", "managed-node-abi-probe.c"));
var nativeBuild = new ProcessStartInfo(Environment.GetEnvironmentVariable("ComSpec")
    ?? throw new InvalidOperationException("ComSpec is unset.")) {
    Arguments = $"/d /s /c \"call \"{environment}\" >nul && cl /nologo /W4 /WX /O2 /TC /Fo\"{objectFile}\" /Fe\"{executable}\" \"{source}\" /link /incremental:no\"",
};
var nativeOutput = await Run(nativeBuild, TimeSpan.FromMinutes(2)).ConfigureAwait(false);
await File.WriteAllTextAsync(Path.Combine(artifacts, "managed-node-abi-c-build.log"),
    nativeOutput).ConfigureAwait(false);
var cli = Path.Combine(artifacts, "bin", "sources", "terrumc", "Debug", "net11.0", "terrumc.dll");
var dll = Path.Combine(artifacts, "managed-node-abi.dll");
var compile = new ProcessStartInfo("dotnet");
foreach (var argument in new[] { cli, "build-library",
    Path.GetFullPath(Path.Combine("tests", "native", "managed-node-abi.terrum")),
    "--export", "Cycles", "--export", "CollectWithScalar",
    "--export", "AllocationFailure", "--export", "RetainAcrossAllocationFailure",
    "-o", dll, "--runtime", "fail-fast" })
{
    compile.ArgumentList.Add(argument);
}
var buildOutput = await Run(compile, TimeSpan.FromMinutes(2)).ConfigureAwait(false);
await File.WriteAllTextAsync(Path.Combine(artifacts, "managed-node-abi-build.log"),
    buildOutput).ConfigureAwait(false);
var execute = new ProcessStartInfo(executable);
execute.ArgumentList.Add(dll);
var proof = await Run(execute, TimeSpan.FromSeconds(10)).ConfigureAwait(false);
await File.WriteAllTextAsync(Path.Combine(artifacts, "managed-node-abi-proof.log"),
    proof).ConfigureAwait(false);
if (proof.Trim() != "managed-node-abi roots=precise cycle-rounds=64 publication=checked failures=checked live=0")
{
    throw new InvalidOperationException("The independent managed-node ABI proof did not complete.");
}
Console.Write(proof);

var failedRelease = new ProcessStartInfo(executable);
failedRelease.ArgumentList.Add(dll);
failedRelease.ArgumentList.Add("reject-release");
const uint FailedReleaseStatus = 0xE0540005;
_ = await Run(failedRelease, TimeSpan.FromSeconds(10), unchecked((int)FailedReleaseStatus)).ConfigureAwait(false);
var failureProof = FormattableString.Invariant(
    $"BOOL low32=false/high32!=0; collection release failure terminates with 0x{FailedReleaseStatus:X8}\n");
await File.WriteAllTextAsync(Path.Combine(artifacts, "managed-node-release-failure.log"),
    failureProof).ConfigureAwait(false);
Console.Write(failureProof);

static async Task<string> Run(ProcessStartInfo start, TimeSpan timeout, int expectedExitCode = 0)
{
    start.UseShellExecute = false;
    start.CreateNoWindow = true;
    start.RedirectStandardOutput = true;
    start.RedirectStandardError = true;
    using var process = Process.Start(start)
        ?? throw new InvalidOperationException("Could not start managed-node ABI command.");
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
    if (process.ExitCode != expectedExitCode)
    {
        throw new InvalidOperationException(
            $"Managed-node ABI command exited {process.ExitCode}, expected {expectedExitCode}:\n{output}\n{error}");
    }
    return output;
}
