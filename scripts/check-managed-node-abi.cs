// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System.Diagnostics;
using System.Runtime.InteropServices;

if (!OperatingSystem.IsWindows() || RuntimeInformation.ProcessArchitecture != Architecture.X64)
{
    throw new PlatformNotSupportedException("The managed-node ABI probe requires Windows x64.");
}

var artifacts = Path.GetFullPath("artifacts");
var binaries = Path.Combine(artifacts, "bin", "native-probes", "managed-node-abi");
var objects = Path.Combine(artifacts, "obj", "native-probes", "managed-node-abi");
var logs = Path.Combine(artifacts, "log", "native-probes", "managed-node-abi");
Directory.CreateDirectory(binaries);
Directory.CreateDirectory(objects);
Directory.CreateDirectory(logs);
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
var executable = Path.Combine(binaries, "managed-node-abi-probe.exe");
var objectFile = Path.Combine(objects, "managed-node-abi-probe.obj");
var source = Path.GetFullPath(Path.Combine("tests", "native", "managed-node-abi-probe.c"));
var nativeBuild = new ProcessStartInfo(Environment.GetEnvironmentVariable("ComSpec")
    ?? throw new InvalidOperationException("ComSpec is unset.")) {
    Arguments = $"/d /s /c \"call \"{environment}\" >nul && cl /nologo /W4 /WX /O2 /TC /Fo\"{objectFile}\" /Fe\"{executable}\" \"{source}\" /link /incremental:no\"",
};
var nativeOutput = await Run(nativeBuild, TimeSpan.FromMinutes(2)).ConfigureAwait(false);
await File.WriteAllTextAsync(Path.Combine(logs, "managed-node-abi-c-build.log"),
    nativeOutput).ConfigureAwait(false);
var cli = Path.Combine(artifacts, "bin", "sources", "terrumc", "Debug", "net11.0", "terrumc.dll");
foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
{
    var dll = Path.Combine(binaries, $"managed-node-abi-{policy}.dll");
    var compile = new ProcessStartInfo("dotnet");
    foreach (var argument in new[] { cli, "build-library",
        Path.GetFullPath(Path.Combine("tests", "native", "managed-node-abi.terrum")),
        "--export", "Cycles", "--export", "CollectWithScalar",
        "--export", "AllocationFailure", "--export", "RetainAcrossAllocationFailure",
        "-o", dll, "--runtime", policy })
    {
        compile.ArgumentList.Add(argument);
    }
    var buildOutput = await Run(compile, TimeSpan.FromMinutes(2)).ConfigureAwait(false);
    await File.WriteAllTextAsync(Path.Combine(logs, $"managed-node-abi-build-{policy}.log"),
        buildOutput).ConfigureAwait(false);
    var execute = new ProcessStartInfo(executable);
    execute.ArgumentList.Add(dll);
    var proof = await Run(execute, TimeSpan.FromSeconds(10)).ConfigureAwait(false);
    await File.WriteAllTextAsync(Path.Combine(logs, $"managed-node-abi-proof-{policy}.log"),
        proof).ConfigureAwait(false);
    if (proof.Trim() != "managed-node-abi roots=precise cycle-rounds=64 publication=checked failures=checked live=0")
    {
        throw new InvalidOperationException("The independent managed-node ABI proof did not complete.");
    }
    Console.Write($"{policy}: {proof}");

    var failedRelease = new ProcessStartInfo(executable);
    failedRelease.ArgumentList.Add(dll);
    failedRelease.ArgumentList.Add("reject-release");
    const uint FailedReleaseStatus = 0xE0540005;
    _ = await Run(failedRelease, TimeSpan.FromSeconds(10), unchecked((int)FailedReleaseStatus)).ConfigureAwait(false);
    var failureProof = FormattableString.Invariant(
        $"BOOL low32=false/high32!=0; collection release failure terminates with 0x{FailedReleaseStatus:X8}\n");
    await File.WriteAllTextAsync(Path.Combine(logs, $"managed-node-release-failure-{policy}.log"),
        failureProof).ConfigureAwait(false);
    Console.Write($"{policy}: {failureProof}");
}

var unwindDll = Path.Combine(binaries, "managed-unwind-abi.dll");
var compileUnwind = new ProcessStartInfo("dotnet");
foreach (var argument in new[] { cli, "build-library",
    Path.GetFullPath(Path.Combine("tests", "native", "managed-unwind-abi.terrum")),
    "--export", "SameFrame", "--export", "EscapingFrame", "--export", "LaterArgument",
    "--export", "OutputFailure", "--export", "ReturnAcrossCleanup", "--export", "EscapeToNative",
    "--export", "CollectWithScalar", "-o", unwindDll, "--runtime", "checked-exceptions" })
{
    compileUnwind.ArgumentList.Add(argument);
}
var unwindBuild = await Run(compileUnwind, TimeSpan.FromMinutes(2)).ConfigureAwait(false);
await File.WriteAllTextAsync(Path.Combine(logs, "managed-unwind-abi-build.log"),
    unwindBuild).ConfigureAwait(false);
var unwindExecute = new ProcessStartInfo(executable);
unwindExecute.ArgumentList.Add(unwindDll);
unwindExecute.ArgumentList.Add("unwind");
var unwindProof = await Run(unwindExecute, TimeSpan.FromSeconds(10)).ConfigureAwait(false);
await File.WriteAllTextAsync(Path.Combine(logs, "managed-unwind-abi-proof.log"),
    unwindProof).ConfigureAwait(false);
if (unwindProof.Trim() != "managed-node-unwind rounds=64 lexical=checked staging=checked native-catch=checked live=0")
{
    throw new InvalidOperationException("The independent managed-node unwind proof did not complete.");
}
Console.Write(unwindProof);

foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
{
    var classes = Path.Combine(binaries, $"managed-class-abi-{policy}.dll");
    var compileClasses = new ProcessStartInfo("dotnet");
    foreach (var argument in new[] { cli, "build-library",
        Path.GetFullPath(Path.Combine("tests", "native", "managed-class-abi.terrum")),
        "--export", "MixedEdges", "--export", "AliasUpdates", "--export", "DuringConstruction",
        "--export", "AssignmentAcrossCollection",
        "--export", "CreateLoose", "--export", "ScalarNonRoot", "--export", "AllocationFailure",
        "--export", "RetainAcrossAllocationFailure", "--export", "CollectWithScalar",
        "--export", "CopyExport", "--export", "CopySmall", "--export", "TryExport",
        "--export", "TryExportScalar",
        "-o", classes, "--runtime", policy })
    {
        compileClasses.ArgumentList.Add(argument);
    }
    var checkedUnwind = policy == "checked-exceptions";
    if (checkedUnwind)
    {
        compileClasses.ArgumentList.Add("--source");
        compileClasses.ArgumentList.Add(Path.GetFullPath(
            Path.Combine("tests", "native", "managed-class-unwind-abi.terrum")));
        compileClasses.ArgumentList.Add("--export");
        compileClasses.ArgumentList.Add("ConstructorFailure");
        compileClasses.ArgumentList.Add("--export");
        compileClasses.ArgumentList.Add("EscapeConstructorToNative");
        compileClasses.ArgumentList.Add("--export");
        compileClasses.ArgumentList.Add("AssignmentFailure");
        compileClasses.ArgumentList.Add("--export");
        compileClasses.ArgumentList.Add("FailExportValue");
    }
    var classBuild = await Run(compileClasses, TimeSpan.FromMinutes(2)).ConfigureAwait(false);
    await File.WriteAllTextAsync(Path.Combine(logs, $"managed-class-abi-build-{policy}.log"),
        classBuild).ConfigureAwait(false);
    var classExecute = new ProcessStartInfo(executable);
    classExecute.ArgumentList.Add(classes);
    classExecute.ArgumentList.Add(checkedUnwind ? "classes-checked" : "classes");
    var classProof = await Run(classExecute, TimeSpan.FromSeconds(10)).ConfigureAwait(false);
    await File.WriteAllTextAsync(Path.Combine(logs, $"managed-class-abi-proof-{policy}.log"),
        classProof).ConfigureAwait(false);
    var expected = checkedUnwind
        ? "managed-classes rounds=64 descriptors=checked construction=checked unwind=checked live=0"
        : "managed-classes rounds=64 descriptors=checked construction=checked unwind=disabled live=0";
    if (classProof.Trim() != expected)
    {
        throw new InvalidOperationException("The independent managed-class ABI proof did not complete.");
    }
    Console.Write($"{policy}: {classProof}");
    var valueExecute = new ProcessStartInfo(executable);
    valueExecute.ArgumentList.Add(classes);
    valueExecute.ArgumentList.Add(checkedUnwind ? "classes-values-checked" : "classes-values");
    var valueProof = await Run(valueExecute, TimeSpan.FromSeconds(10)).ConfigureAwait(false);
    await File.WriteAllTextAsync(Path.Combine(logs, $"managed-class-value-abi-proof-{policy}.log"),
        valueProof).ConfigureAwait(false);
    if (valueProof.Trim() != $"managed-class-values copy=16 direct=4 outputs=conditional unwind={(checkedUnwind ? "checked" : "disabled")} live=0")
    {
        throw new InvalidOperationException("The independent managed Copy-value ABI proof did not complete.");
    }
    Console.Write($"{policy}: {valueProof}");
    if (checkedUnwind)
    {
        var abandonFailure = new ProcessStartInfo(executable);
        abandonFailure.ArgumentList.Add(classes);
        abandonFailure.ArgumentList.Add("classes-reject-release");
        const uint FailedAbandonStatus = 0xE0540005;
        _ = await Run(abandonFailure, TimeSpan.FromSeconds(10),
            unchecked((int)FailedAbandonStatus)).ConfigureAwait(false);
        var abandonProof = FormattableString.Invariant(
            $"Failed constructor release terminates with 0x{FailedAbandonStatus:X8} before catch.\n");
        await File.WriteAllTextAsync(Path.Combine(logs, "managed-class-abandon-failure.log"),
            abandonProof).ConfigureAwait(false);
        Console.Write(abandonProof);
    }
}

foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
{
    var optional = Path.Combine(binaries, $"managed-optional-abi-{policy}.dll");
    var compileOptional = new ProcessStartInfo("dotnet");
    foreach (var argument in new[] { cli, "build-library",
        Path.GetFullPath(Path.Combine("tests", "native", "managed-optional-abi.terrum")),
        "--export", "OptionalCycles", "--export", "OptionalSnapshots", "--export", "OptionalNullOutput",
        "--export", "OptionalAllocationFailure", "--export", "OptionalRetainAcrossFailure",
        "--export", "CollectWithScalar", "-o", optional, "--runtime", policy })
    {
        compileOptional.ArgumentList.Add(argument);
    }
    var checkedUnwind = policy == "checked-exceptions";
    if (checkedUnwind)
    {
        compileOptional.ArgumentList.Add("--source");
        compileOptional.ArgumentList.Add(Path.GetFullPath(
            Path.Combine("tests", "native", "managed-optional-unwind-abi.terrum")));
        compileOptional.ArgumentList.Add("--export");
        compileOptional.ArgumentList.Add("OptionalRecovery");
        compileOptional.ArgumentList.Add("--export");
        compileOptional.ArgumentList.Add("OptionalEscapeToNative");
    }
    var optionalBuild = await Run(compileOptional, TimeSpan.FromMinutes(2)).ConfigureAwait(false);
    await File.WriteAllTextAsync(Path.Combine(logs, $"managed-optional-abi-build-{policy}.log"),
        optionalBuild).ConfigureAwait(false);
    var optionalExecute = new ProcessStartInfo(executable);
    optionalExecute.ArgumentList.Add(optional);
    optionalExecute.ArgumentList.Add(checkedUnwind ? "optional-checked" : "optional");
    var optionalProof = await Run(optionalExecute, TimeSpan.FromSeconds(10)).ConfigureAwait(false);
    await File.WriteAllTextAsync(Path.Combine(logs, $"managed-optional-abi-proof-{policy}.log"),
        optionalProof).ConfigureAwait(false);
    var expected = checkedUnwind
        ? "managed-optionals rounds=64 cycles=checked snapshots=checked unwind=checked live=0"
        : "managed-optionals rounds=64 cycles=checked snapshots=checked unwind=disabled live=0";
    if (optionalProof.Trim() != expected)
    {
        throw new InvalidOperationException("The independent managed-optional ABI proof did not complete.");
    }
    Console.Write($"{policy}: {optionalProof}");
}

foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
{
    var chain = Path.Combine(binaries, $"managed-token-chain-abi-{policy}.dll");
    var compileChain = new ProcessStartInfo("dotnet");
    foreach (var argument in new[] { cli, "build-library",
        Path.GetFullPath(Path.Combine("tests", "native", "managed-token-chain-abi.terrum")),
        "--source", Path.GetFullPath(Path.Combine("sources", "self-host", "ManagedTokenChain.terrum")),
        "--source", Path.GetFullPath(Path.Combine("sources", "self-host", "SourceRange.terrum")),
        "--source", Path.GetFullPath(Path.Combine("sources", "self-host", "Token.terrum")),
        "--export", "ChainReuse", "--export", "ChainAllocationFailure", "--export", "CollectWithScalar",
        "-o", chain, "--runtime", policy })
    {
        compileChain.ArgumentList.Add(argument);
    }
    var chainBuild = await Run(compileChain, TimeSpan.FromMinutes(2)).ConfigureAwait(false);
    await File.WriteAllTextAsync(Path.Combine(logs, $"managed-token-chain-abi-build-{policy}.log"),
        chainBuild).ConfigureAwait(false);
    var chainExecute = new ProcessStartInfo(executable);
    chainExecute.ArgumentList.Add(chain);
    chainExecute.ArgumentList.Add("chain");
    var chainProof = await Run(chainExecute, TimeSpan.FromSeconds(10)).ConfigureAwait(false);
    await File.WriteAllTextAsync(Path.Combine(logs, $"managed-token-chain-abi-proof-{policy}.log"),
        chainProof).ConfigureAwait(false);
    if (chainProof.Trim() != "managed-token-chain rounds=64 copy-values=checked reuse=checked allocation-failure=checked live=0")
    {
        throw new InvalidOperationException("The independent managed token-chain ABI proof did not complete.");
    }
    Console.Write($"{policy}: {chainProof}");
}

foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
{
    var capacity = Path.Combine(binaries, $"managed-class-capacity-abi-{policy}.dll");
    var compileCapacity = new ProcessStartInfo("dotnet");
    foreach (var argument in new[] { cli, "build-library",
        Path.GetFullPath(Path.Combine("tests", "native", "managed-class-capacity-abi.terrum")),
        "--export", "CapacityCycle", "--export", "CollectWithScalar",
        "-o", capacity, "--runtime", policy })
    {
        compileCapacity.ArgumentList.Add(argument);
    }
    var capacityBuild = await Run(compileCapacity, TimeSpan.FromMinutes(2)).ConfigureAwait(false);
    await File.WriteAllTextAsync(Path.Combine(logs, $"managed-class-capacity-abi-build-{policy}.log"),
        capacityBuild).ConfigureAwait(false);
    var capacityExecute = new ProcessStartInfo(executable);
    capacityExecute.ArgumentList.Add(capacity);
    capacityExecute.ArgumentList.Add("class-capacity");
    var capacityProof = await Run(capacityExecute, TimeSpan.FromSeconds(10)).ConfigureAwait(false);
    await File.WriteAllTextAsync(Path.Combine(logs, $"managed-class-capacity-abi-proof-{policy}.log"),
        capacityProof).ConfigureAwait(false);
    if (capacityProof.Trim() != "managed-class-capacity rounds=2 live-limit=4096 refusal=checked reuse=checked live=0")
    {
        throw new InvalidOperationException("The independent managed class-capacity ABI proof did not complete.");
    }
    Console.Write($"{policy}: {capacityProof}");
}

foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
{
    var transfers = Path.Combine(binaries, $"managed-loop-transfer-abi-{policy}.dll");
    var compileTransfers = new ProcessStartInfo("dotnet");
    foreach (var argument in new[] { cli, "build-library",
        Path.GetFullPath(Path.Combine("tests", "native", "managed-loop-transfer-abi.terrum")),
        "--export", "LoopTransfers", "--export", "CollectWithScalar",
        "-o", transfers, "--runtime", policy })
    {
        compileTransfers.ArgumentList.Add(argument);
    }
    var transferBuild = await Run(compileTransfers, TimeSpan.FromMinutes(2)).ConfigureAwait(false);
    await File.WriteAllTextAsync(Path.Combine(logs, $"managed-loop-transfer-abi-build-{policy}.log"),
        transferBuild).ConfigureAwait(false);
    var transferExecute = new ProcessStartInfo(executable);
    transferExecute.ArgumentList.Add(transfers);
    transferExecute.ArgumentList.Add("loop-transfers");
    var transferProof = await Run(transferExecute, TimeSpan.FromSeconds(10)).ConfigureAwait(false);
    await File.WriteAllTextAsync(Path.Combine(logs, $"managed-loop-transfer-abi-proof-{policy}.log"),
        transferProof).ConfigureAwait(false);
    if (transferProof.Trim() != "managed-loop-transfers rounds=64 iterator=checked cleanup=checked roots=checked live=0")
    {
        throw new InvalidOperationException("The independent managed loop-transfer ABI proof did not complete.");
    }
    Console.Write($"{policy}: {transferProof}");
}

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
