// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System.Diagnostics;
using System.Runtime.InteropServices;

if (!OperatingSystem.IsWindows() || RuntimeInformation.ProcessArchitecture != Architecture.X64)
{
    throw new PlatformNotSupportedException("The native frame probe requires Windows x64.");
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
var binaries = Path.Combine(artifacts, "bin", "native-probes", "native-frame");
var objects = Path.Combine(artifacts, "obj", "native-probes", "native-frame");
var logs = Path.Combine(artifacts, "log", "native-probes", "native-frame");
Directory.CreateDirectory(binaries);
Directory.CreateDirectory(objects);
Directory.CreateDirectory(logs);
var cli = Path.GetFullPath(Path.Combine("artifacts", "bin", "sources", "terrumc", "Debug", "net11.0", "terrumc.dll"));
var dll = Path.Combine(binaries, "frame-unwind.dll");
var executable = Path.Combine(binaries, "frame-unwind-probe.exe");
var objectFile = Path.Combine(objects, "frame-unwind-probe.obj");
var buildLibrary = new ProcessStartInfo("dotnet");
foreach (var argument in new[] { cli, "build-library", Path.GetFullPath(Path.Combine("samples", "native", "frame-unwind.terrum")),
    "--export", "Mix", "--export", "TryWrite", "-o", dll, "--runtime", "fail-fast" })
{
    buildLibrary.ArgumentList.Add(argument);
}
var libraryOutput = await Run(buildLibrary, TimeSpan.FromMinutes(2)).ConfigureAwait(false);
var source = Path.GetFullPath(Path.Combine("tests", "native", "frame-unwind-probe.c"));
var build = new ProcessStartInfo(Environment.GetEnvironmentVariable("ComSpec")
    ?? throw new InvalidOperationException("ComSpec is unset.")) {
    Arguments = $"/d /s /c \"call \"{environment}\" >nul && cl /nologo /W4 /WX /Od /GS- /Fo\"{objectFile}\" /Fe\"{executable}\" \"{source}\" /link /incremental:no\"",
};
var nativeOutput = await Run(build, TimeSpan.FromMinutes(2)).ConfigureAwait(false);
await File.WriteAllTextAsync(Path.Combine(logs, "native-frame-build.log"), libraryOutput + nativeOutput).ConfigureAwait(false);
var execute = new ProcessStartInfo(executable);
execute.ArgumentList.Add(dll);
var proof = await Run(execute, TimeSpan.FromSeconds(10)).ConfigureAwait(false);
await File.WriteAllTextAsync(Path.Combine(logs, "native-frame-proof.log"), proof).ConfigureAwait(false);
if (proof.Trim() != "native-frame exports=42 unwound-rbp=1 funclets=1 unwound-funclet=1 prolog-states=8")
{
    throw new InvalidOperationException("The generated frame did not satisfy the independent native ABI proof.");
}
Console.Write(proof);

static async Task<string> Run(ProcessStartInfo start, TimeSpan timeout)
{
    start.UseShellExecute = false;
    start.CreateNoWindow = true;
    start.RedirectStandardOutput = true;
    start.RedirectStandardError = true;
    using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start native frame probe command.");
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
        throw new InvalidOperationException($"Native frame probe failed ({process.ExitCode}):\n{output}\n{error}");
    }
    return output;
}
