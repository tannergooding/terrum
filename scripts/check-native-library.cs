// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.
#:project ../sources/Terrum.CodeAnalysis/Terrum.CodeAnalysis.csproj

using System.Diagnostics;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;

if (!OperatingSystem.IsWindows())
{
    throw new PlatformNotSupportedException("The independent DLL loader probe requires Windows x64.");
}

const string Source = """
    public struct Twelve {
        public int32 First; public int32 Second; public int32 Third;
        public Twelve(int32 first, int32 second, int32 third) {
            First = first; Second = second; Third = third;
        }
    }
    public struct Wide {
        public int64 Signed; public uint64 Unsigned;
        public Wide(int64 signed, uint64 unsigned) { Signed = signed; Unsigned = unsigned; }
    }
    public Twelve MakeTwelve(int32 first, int32 second, int32 third) {
        return new Twelve(first, second, third);
    }
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
var probe = Path.GetFullPath(Path.Combine("artifacts", "library-probe.exe"));
if (!File.Exists(probe))
{
    throw new FileNotFoundException("Build tests\\native\\library-probe.c with the MSVC x64 compiler before running the DLL check.", probe);
}
var first = Path.GetFullPath(Path.Combine("artifacts", "native-library-first.dll"));
var second = Path.GetFullPath(Path.Combine("artifacts", "native-library-second.dll"));
try
{
    await File.WriteAllBytesAsync(first, Compile(Source)).ConfigureAwait(false);
    await File.WriteAllBytesAsync(second, Compile(Source.Replace("Hidden() { return 5;", "Hidden() { return 6;",
        StringComparison.Ordinal))).ConfigureAwait(false);
    var start = new ProcessStartInfo(probe) {
        UseShellExecute = false,
        CreateNoWindow = true,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
    };
    start.ArgumentList.Add(first);
    start.ArgumentList.Add(second);
    using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start the DLL probe.");
    var output = process.StandardOutput.ReadToEndAsync();
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
    if (process.ExitCode != 0)
    {
        throw new InvalidOperationException($"DLL probe exited {process.ExitCode}: {await error.ConfigureAwait(false)}");
    }
    Console.Write(await output.ConfigureAwait(false));
    var failStart = new ProcessStartInfo(probe) {
        UseShellExecute = false,
        CreateNoWindow = true,
        RedirectStandardError = true,
    };
    failStart.ArgumentList.Add("--failure");
    failStart.ArgumentList.Add(first);
    using var failing = Process.Start(failStart) ?? throw new InvalidOperationException("Could not start failure probe.");
    var failureError = failing.StandardError.ReadToEndAsync();
    try
    {
        await failing.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
    }
    catch (TimeoutException)
    {
        failing.Kill(entireProcessTree: true);
        await failing.WaitForExitAsync().ConfigureAwait(false);
        throw;
    }
    if (failing.ExitCode != unchecked((int)0xE0540003))
    {
        throw new InvalidOperationException($"Unexpected DLL fail-fast exit {failing.ExitCode}: {await failureError.ConfigureAwait(false)}");
    }
    Console.WriteLine($"DLL failure exited 0x{failing.ExitCode:X8}");
}
finally
{
    File.Delete(first);
    File.Delete(second);
}

static byte[] Compile(string source)
{
    var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source));
    if (compilation.HasErrors || compilation.Program is not BoundProgram program)
    {
        throw new InvalidOperationException(string.Join(Environment.NewLine, compilation.Diagnostics));
    }
    return NativeEmitter.EmitLibrary(program, ["TryWide", "Evaluate", "MakeWide", "MakeTwelve"],
        NativeRuntimePolicy.FailFast);
}
