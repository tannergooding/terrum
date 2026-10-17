// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.
#:project ..\sources\Terrum.CodeAnalysis\Terrum.CodeAnalysis.csproj

using System.Diagnostics;
using System.Runtime.InteropServices;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;

if (!OperatingSystem.IsWindows() || RuntimeInformation.ProcessArchitecture != Architecture.X64)
{
    throw new PlatformNotSupportedException("The cleanup copy ABI probe requires Windows x64.");
}

var source = """
    public unique struct Child {
        internal int32 Tag;
        public Child() { Tag = 42; }
        [NoThrow] public int32 Read() { return Tag; }
        [NoThrow] [Cleanup] public void Close() {}
    }
    public unique struct Parent {
        internal Child Value;
        internal int64 Marker;
        public Parent() { Value = new Child(); Marker = int64.MinValue; }
        [NoThrow] [Cleanup] public void Close() {}
    }
    public unique struct Envelope {
        internal Parent Value;
        public Envelope() { Value = new Parent(); }
    }
    public int32 Check() {
        { Parent value = new Parent(); }
        return 42;
    }
    public int32 CheckReplacement() {
        mut Parent value = new Parent();
        value = new Parent();
        require value.Marker == int64.MinValue;
        return value.Value.Read();
    }
    public bool Replace(Parent input, out Parent output) {
        require input.Value.Read() == 42;
        output = move input;
        return true;
    }
    public int32 CheckStaged() {
        mut Parent value = new Parent();
        require Replace(new Parent(), out value);
        require value.Marker == int64.MinValue;
        return value.Value.Read();
    }
    public Parent ReturnOwner() {
        Parent old = new Parent();
        return new Parent();
    }
    public int32 CheckPendingReturn() {
        Parent value = ReturnOwner();
        require value.Marker == int64.MinValue;
        return value.Value.Read();
    }
    public int32 CheckNested() {
        Envelope value = new Envelope();
        return value.Value.Value.Read();
    }
    """;
var artifacts = Path.GetFullPath("artifacts");
var binaries = Path.Combine(artifacts, "bin", "native-probes", "cleanup-copy-abi");
var objects = Path.Combine(artifacts, "obj", "native-probes", "cleanup-copy-abi");
var logs = Path.Combine(artifacts, "log", "native-probes", "cleanup-copy-abi");
Directory.CreateDirectory(binaries);
Directory.CreateDirectory(objects);
Directory.CreateDirectory(logs);

var vswhere = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
    "Microsoft Visual Studio", "Installer", "vswhere.exe");
var discover = new ProcessStartInfo(vswhere);
foreach (var argument in new[] { "-latest", "-products", "*",
    "-requires", "Microsoft.VisualStudio.Component.VC.Tools.x86.x64", "-property", "installationPath" })
{
    discover.ArgumentList.Add(argument);
}
var installation = (await Run(discover, TimeSpan.FromSeconds(10)).ConfigureAwait(false)).Trim();
var environment = Path.Combine(installation, "VC", "Auxiliary", "Build", "vcvars64.bat");
if (installation.Length == 0 || !File.Exists(environment))
{
    throw new FileNotFoundException("Visual Studio x64 C++ build tools are required.", environment);
}
var provider = Path.Combine(binaries, "cleanup-copy-provider.dll");
var worker = Path.Combine(binaries, "cleanup-copy-worker.exe");
var providerSource = Path.GetFullPath(Path.Combine("tests", "native", "cleanup-copy-provider.c"));
var workerSource = Path.GetFullPath(Path.Combine("tests", "native", "cleanup-copy-worker.c"));
var build = new ProcessStartInfo(Environment.GetEnvironmentVariable("ComSpec")
    ?? throw new InvalidOperationException("ComSpec is unset.")) {
    Arguments = $"/d /s /c \"call \"{environment}\" >nul && cl /nologo /W4 /WX /O2 /TC /LD /Fo\"{Path.Combine(objects, "cleanup-copy-provider.obj")}\" /Fe\"{provider}\" \"{providerSource}\" /link /incremental:no /implib:\"{Path.Combine(objects, "cleanup-copy-provider.lib")}\" && cl /nologo /W4 /WX /O2 /TC /Fo\"{Path.Combine(objects, "cleanup-copy-worker.obj")}\" /Fe\"{worker}\" \"{workerSource}\" /link /incremental:no\"",
};
var buildOutput = await Run(build, TimeSpan.FromMinutes(2)).ConfigureAwait(false);
await File.WriteAllTextAsync(Path.Combine(logs, "cleanup-copy-build.log"), buildOutput).ConfigureAwait(false);
foreach (var policy in new[] { NativeRuntimePolicy.FailFast, NativeRuntimePolicy.CheckedExceptions })
{
    var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source), (BoundRuntimePolicy)policy);
    if (compilation.HasErrors || compilation.Program is not BoundProgram program)
    {
        throw new InvalidOperationException(string.Join(Environment.NewLine, compilation.Diagnostics));
    }
    // Substitute real C cleanup callees at the raw verified-IR import boundary.
    // Their by-value argument storage may be overwritten without changing the caller's value.
    var functions = program.Functions.ToBuilder();
    for (var index = 0; index < functions.Count; index++)
    {
        if (functions[index].Name is "Parent.Close" or "Child.Close")
        {
            functions[index] = functions[index] with {
                IsExternal = true,
                Body = new BoundBlock([], default),
            };
        }
    }
    program = program with { Functions = functions.ToImmutable() };
    var image = NativeEmitter.EmitLibraryWithImports(program,
        ["Check", "CheckReplacement", "CheckStaged", "CheckPendingReturn", "CheckNested"], [
            new NativeImportBinding("Parent.Close", "cleanup-copy-provider.dll", "ClobberParent"),
            new NativeImportBinding("Child.Close", "cleanup-copy-provider.dll", "RecordChild"),
        ], policy);
    var target = Path.Combine(binaries, $"cleanup-copy-target-{policy}.dll");
    await File.WriteAllBytesAsync(target, image).ConfigureAwait(false);
    var execute = new ProcessStartInfo(worker) { WorkingDirectory = binaries };
    execute.ArgumentList.Add(provider);
    execute.ArgumentList.Add(target);
    var proof = await Run(execute, TimeSpan.FromSeconds(10)).ConfigureAwait(false);
    await File.WriteAllTextAsync(Path.Combine(logs, $"cleanup-copy-proof-{policy}.log"), proof).ConfigureAwait(false);
    if (proof.Trim() != "cleanup-copy-abi parent-scratch=clobbered child=42 cases=5")
    {
        throw new InvalidOperationException("The independent cleanup copy ABI proof did not complete.");
    }
    Console.Write($"{policy}: {proof}");
}

static async Task<string> Run(ProcessStartInfo start, TimeSpan timeout)
{
    start.UseShellExecute = false;
    start.CreateNoWindow = true;
    start.RedirectStandardOutput = true;
    start.RedirectStandardError = true;
    using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start cleanup ABI command.");
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
        throw new InvalidOperationException($"Cleanup ABI command failed ({process.ExitCode}):\n{output}\n{error}");
    }
    return output;
}
