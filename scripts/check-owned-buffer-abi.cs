// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System.Diagnostics;
using System.Runtime.InteropServices;

if (!OperatingSystem.IsWindows() || RuntimeInformation.ProcessArchitecture != Architecture.X64)
{
    throw new PlatformNotSupportedException("The owned-buffer ABI probe requires Windows x64.");
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
var executable = Path.Combine(artifacts, "owned-buffer-abi-probe.exe");
var objectFile = Path.Combine(artifacts, "owned-buffer-abi-probe.obj");
var source = Path.GetFullPath(Path.Combine("tests", "native", "owned-buffer-abi-probe.c"));
var nativeBuild = new ProcessStartInfo(Environment.GetEnvironmentVariable("ComSpec")
    ?? throw new InvalidOperationException("ComSpec is unset.")) {
    Arguments = $"/d /s /c \"call \"{environment}\" >nul && cl /nologo /W4 /WX /O2 /TC /Fo\"{objectFile}\" /Fe\"{executable}\" \"{source}\" /link /incremental:no\"",
};
var nativeOutput = await Run(nativeBuild, TimeSpan.FromMinutes(2)).ConfigureAwait(false);
await File.WriteAllTextAsync(Path.Combine(artifacts, "owned-buffer-abi-c-build.log"), nativeOutput).ConfigureAwait(false);
var cli = Path.Combine(artifacts, "bin", "sources", "terrumc", "Debug", "net11.0", "terrumc.dll");
foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
{
    var dll = Path.Combine(artifacts, $"owned-buffer-abi-{policy}.dll");
    var compile = new ProcessStartInfo("dotnet");
    foreach (var argument in new[] { cli, "build-library",
        Path.GetFullPath(Path.Combine("tests", "native", "owned-buffer-abi.terrum")),
        "--export", "TryAcquire", "--export", "Build", "--export", "Relay", "--export", "Consume",
        "--export", "Rebuild", "--export", "TryRebuild", "--export", "Composite",
        "--export", "FieldMutation",
        "--export", "FormattingWorkspace", "--export", "FormatDigits", "--export", "FormatSignedDigits",
        "--source", Path.GetFullPath(Path.Combine("self-host", "IntegerFormatting.terrum")),
        "-o", dll, "--runtime", policy })
    {
        compile.ArgumentList.Add(argument);
    }
    if (policy == "checked-exceptions")
    {
        compile.ArgumentList.Add("--source");
        compile.ArgumentList.Add(Path.GetFullPath(Path.Combine("tests", "native", "owned-buffer-abi.checked.terrum")));
        compile.ArgumentList.Add("--export");
        compile.ArgumentList.Add("Recover");
        compile.ArgumentList.Add("--export");
        compile.ArgumentList.Add("RecoverFormatting");
        compile.ArgumentList.Add("--export");
        compile.ArgumentList.Add("RecoverField");
    }
    var buildOutput = await Run(compile, TimeSpan.FromMinutes(2)).ConfigureAwait(false);
    await File.WriteAllTextAsync(Path.Combine(artifacts, $"owned-buffer-abi-build-{policy}.log"), buildOutput).ConfigureAwait(false);
    var execute = new ProcessStartInfo(executable);
    execute.ArgumentList.Add(dll);
    execute.ArgumentList.Add(policy == "checked-exceptions" ? "checked" : "fail-fast");
    var proof = await Run(execute, TimeSpan.FromSeconds(10)).ConfigureAwait(false);
    await File.WriteAllTextAsync(Path.Combine(artifacts, $"owned-buffer-abi-proof-{policy}.log"), proof).ConfigureAwait(false);
    if (proof.Trim() != "owned-buffer-abi opaque=8 publication=checked views=scoped reuse=checked field=tracked heap=tracked live=0")
    {
        throw new InvalidOperationException("The independent owned-buffer ABI proof did not complete.");
    }
    Console.Write($"{policy}: {proof}");
    var failedRelease = new ProcessStartInfo(executable);
    failedRelease.ArgumentList.Add(dll);
    failedRelease.ArgumentList.Add(policy == "checked-exceptions" ? "checked" : "fail-fast");
    failedRelease.ArgumentList.Add("reject-release");
    _ = await Run(failedRelease, TimeSpan.FromSeconds(10), unchecked((int)0xE0540005u)).ConfigureAwait(false);
    var failureProof = $"{policy}: BOOL low32=false/high32!=0; release failure terminates with 0xE0540005\n";
    await File.WriteAllTextAsync(Path.Combine(artifacts, $"owned-buffer-release-failure-{policy}.log"),
        failureProof).ConfigureAwait(false);
    Console.Write(failureProof);
}

static async Task<string> Run(ProcessStartInfo start, TimeSpan timeout, int expectedExitCode = 0)
{
    start.UseShellExecute = false;
    start.CreateNoWindow = true;
    start.RedirectStandardOutput = true;
    start.RedirectStandardError = true;
    using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start owned-buffer ABI command.");
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
            $"Owned-buffer ABI command exited {process.ExitCode}, expected {expectedExitCode}:\n{output}\n{error}");
    }
    return output;
}
