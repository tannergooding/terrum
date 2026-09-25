// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System.Diagnostics;
using System.Runtime.InteropServices;

if (!OperatingSystem.IsWindows() || RuntimeInformation.ProcessArchitecture != Architecture.X64)
{
    throw new PlatformNotSupportedException("The native SEH probe requires Windows x64.");
}

var vswhere = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
    "Microsoft Visual Studio", "Installer", "vswhere.exe");
if (!File.Exists(vswhere))
{
    throw new FileNotFoundException("Install Visual Studio C++ build tools before running the SEH probe.", vswhere);
}
var discovery = new ProcessStartInfo(vswhere);
foreach (var argument in new[] { "-latest", "-products", "*", "-requires", "Microsoft.VisualStudio.Component.VC.Tools.x86.x64",
    "-property", "installationPath" })
{
    discovery.ArgumentList.Add(argument);
}
var installation = (await Run(discovery, TimeSpan.FromSeconds(10)).ConfigureAwait(false)).Trim();
var environment = Path.Combine(installation, "VC", "Auxiliary", "Build", "vcvars64.bat");
if (installation.Length == 0 || !File.Exists(environment))
{
    throw new FileNotFoundException("No installed Visual Studio x64 C++ toolchain was found.", environment);
}
var artifacts = Path.GetFullPath("artifacts");
Directory.CreateDirectory(artifacts);
var source = Path.GetFullPath(Path.Combine("tests", "native", "seh-probe.c"));
var executable = Path.Combine(artifacts, "seh-probe.exe");
var listing = Path.Combine(artifacts, "seh-reference.asm");
var objectFile = Path.Combine(artifacts, "seh-reference.obj");
var build = new ProcessStartInfo(Environment.GetEnvironmentVariable("ComSpec") ?? throw new InvalidOperationException("ComSpec is unset.")) {
    Arguments = $"/d /s /c \"call \"{environment}\" >nul && cl /nologo /W4 /WX /Od /GS- /FAs /Fa\"{listing}\" /Fo\"{objectFile}\" /Fe\"{executable}\" \"{source}\" /link /incremental:no\"",
};
var buildOutput = await Run(build, TimeSpan.FromMinutes(2)).ConfigureAwait(false);
await File.WriteAllTextAsync(Path.Combine(artifacts, "native-seh-build.log"), buildOutput).ConfigureAwait(false);
var output = await Run(new ProcessStartInfo(executable), TimeSpan.FromSeconds(10)).ConfigureAwait(false);
await File.WriteAllTextAsync(Path.Combine(artifacts, "native-seh-proof.log"), output).ConfigureAwait(false);
if (output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length != 16)
{
    throw new InvalidOperationException("The probe did not report all sixteen C/dynamic execution cases.");
}
Console.Write(output);

static async Task<string> Run(ProcessStartInfo start, TimeSpan timeout)
{
    start.UseShellExecute = false;
    start.CreateNoWindow = true;
    start.RedirectStandardOutput = true;
    start.RedirectStandardError = true;
    using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start the SEH probe command.");
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
        throw new InvalidOperationException($"SEH probe command failed ({process.ExitCode}):\n{output}\n{error}");
    }
    return output;
}
