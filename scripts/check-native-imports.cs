// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.
#:project ../sources/Terrum.CodeAnalysis/Terrum.CodeAnalysis.csproj

using System.Diagnostics;
using System.Security.Cryptography;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;

if (!OperatingSystem.IsWindows())
{
    throw new PlatformNotSupportedException("The native import proof requires Windows x64.");
}

const string Provider = """
    public struct Twelve {
        public int32 First; public int32 Second; public int32 Third;
        public Twelve(int32 a, int32 b, int32 c) { First = a; Second = b; Third = c; }
    }
    public struct Wide {
        public int64 Signed; public uint64 Unsigned;
        public Wide(int64 signed, uint64 unsigned) { Signed = signed; Unsigned = unsigned; }
    }
    public Twelve MakeTwelve(int32 a, int32 b, int32 c) { return new Twelve(a, b, c); }
    public Wide MakeWide(int64 signed, uint64 unsigned) { return new Wide(signed, unsigned); }
    int32 Hidden() { return 5; }
    public int32 Evaluate(Twelve value, Wide wide, int64 n, int32 d, int32 e) {
        require wide.Signed == int64.MinValue && wide.Unsigned == uint64.MaxValue;
        return value.First + value.Second + value.Third + int32(n / 2147483648)
            + d + e + Hidden();
    }
    public bool TryWide(bool success, out Wide result) {
        if (!success) { return false; }
        result = MakeWide(int64.MinValue, uint64.MaxValue);
        return true;
    }
    """;
const string Consumer = """
    bool Forward(bool success, out Wide result) { return TryWide(success, out result); }
    int32 Recurse(int32 count) {
        if (count == 0) {
            return Evaluate(MakeTwelve(10, 11, 12),
                MakeWide(int64.MinValue, uint64.MaxValue), 2147483648, 1, 2);
        }
        return Recurse(count - 1);
    }
    int32 Main() {
        require Forward(true, out var first);
        if (Forward(false, out var ignored)) { return -1; }
        require Forward(true, out var second);
        require first.Signed == int64.MinValue && second.Unsigned == uint64.MaxValue;
        return Recurse(3);
    }
    """;
string[] exports = ["Evaluate", "MakeTwelve", "MakeWide", "TryWide"];
const string DllName = "native-import-provider.dll";
var library = Compilation.CreateLibrary(SyntaxTree.Parse(Provider));
var provider = ProgramOf(library);
var artifact = ModuleCompiler.CreateProducer(provider, exports);
var consumer = ProgramOf(Compilation.Create(SyntaxTree.Parse(Consumer), ModuleCompiler.CreateReference(artifact)));
var bindings = exports.Select(name => new NativeImportBinding(name, DllName, name)).ToArray();
var executable = NativeEmitter.EmitWithImports(consumer, bindings, NativeRuntimePolicy.FailFast);
var first = NativeEmitter.EmitLibrary(provider, exports, NativeRuntimePolicy.FailFast);
var replacement = ProgramOf(Compilation.CreateLibrary(SyntaxTree.Parse(
    Provider.Replace("Hidden() { return 5;", "Hidden() { return 6;", StringComparison.Ordinal))));
var second = NativeEmitter.EmitLibrary(replacement, exports, NativeRuntimePolicy.FailFast);
var root = Path.GetFullPath(Path.Combine("artifacts", "bin", "native-probes", "native-imports"));
Directory.CreateDirectory(root);
var exePath = Path.Combine(root, "native-import-consumer.exe");
var dllPath = Path.Combine(root, DllName);
var failurePath = Path.Combine(root, "native-import-failure.exe");
try
{
    await File.WriteAllBytesAsync(exePath, executable).ConfigureAwait(false);
    var hash = Convert.ToHexString(SHA256.HashData(executable));
    await File.WriteAllBytesAsync(dllPath, first).ConfigureAwait(false);
    await Run(exePath, 42).ConfigureAwait(false);
    await File.WriteAllBytesAsync(dllPath, second).ConfigureAwait(false);
    await Run(exePath, 43).ConfigureAwait(false);
    if (Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(exePath).ConfigureAwait(false))) != hash)
    {
        throw new InvalidOperationException("Native consumer image changed during provider replacement.");
    }
    var failed = ProgramOf(Compilation.Create(SyntaxTree.Parse(Consumer.Replace("return Recurse(3);",
        "return Evaluate(MakeTwelve(10, 11, 12), MakeWide(0, 0), 2147483648, 1, 2);",
        StringComparison.Ordinal)), ModuleCompiler.CreateReference(artifact)));
    await File.WriteAllBytesAsync(failurePath, NativeEmitter.EmitWithImports(failed, bindings,
        NativeRuntimePolicy.FailFast)).ConfigureAwait(false);
    await Run(failurePath, unchecked((int)0xE0540003)).ConfigureAwait(false);
    Console.WriteLine($"Native consumer SHA-256 {hash}; provider results 42/43; checked failure 0xE0540003.");
}
finally
{
    File.Delete(exePath);
    File.Delete(dllPath);
    File.Delete(failurePath);
}

static BoundProgram ProgramOf(Compilation compilation)
{
    if (compilation.HasErrors || compilation.Program is not BoundProgram program)
    {
        throw new InvalidOperationException(string.Join(Environment.NewLine, compilation.Diagnostics));
    }
    return program;
}

static async Task Run(string image, int expected)
{
    var start = new ProcessStartInfo(image) {
        UseShellExecute = false,
        CreateNoWindow = true,
        RedirectStandardError = true,
    };
    using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start native consumer.");
    var error = process.StandardError.ReadToEndAsync();
    try
    {
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
    }
    catch (TimeoutException)
    {
        process.Kill(entireProcessTree: true);
        await process.WaitForExitAsync().ConfigureAwait(false);
        throw;
    }
    if (process.ExitCode != expected)
    {
        throw new InvalidOperationException($"Native consumer exited {process.ExitCode} instead of {expected}: {await error.ConfigureAwait(false)}");
    }
}
