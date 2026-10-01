// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Lexing;
using Terrum.CodeAnalysis.Text;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed partial class CliTests
{
    [Theory]
    [InlineData("", 0)]
    [InlineData("return;", 0)]
    [InlineData("return ();", 0)]
    [InlineData("return ((()));", 0)]
    [InlineData("/*\U0001F600*/ return ( /*unit*/ ( /*inner*/ ) );", 0)]
    [InlineData("require true;", 0)]
    [InlineData("require false;", unchecked((int)0xE0540003))]
    [InlineData("mut int32 x = 0; require x++ == 0; require x == 1;", 0)]
    [InlineData("mut int32 x = 0; require true || ++x == 1; require x == 0;", 0)]
    [InlineData("mut int32 x = 0; require false ? ++x == 1 : ++x == 1; require x == 1;", 0)]
    [InlineData("_ = int32(1) / int32(0);", unchecked((int)0xE0540002))]
    [InlineData("_ = int32(2147483647) + int32(1);", unchecked((int)0xE0540001))]
    [InlineData("require int32(1) / int32(0) == int32(0);", unchecked((int)0xE0540002))]
    [InlineData("mut int32 @await = 1; @await += 41; require @await == 42; return;", 0)]
    [InlineData("{ _ = true; require true; } if (false) { require false; } else { return (); }", 0)]
    [InlineData("mut int32 x = 0; while (x < 2) { x++; } require x == 2;", 0)]
    [InlineData("mut int32 x = 0; @const: for (; x < 2; x++) { do { continue const; } while (false); } require x == 2;", 0)]
    [InlineData("do { return ((())); } while (true);", 0)]
    [InlineData("return; require false;", 0)]
    public async Task NativeVoidMatchesHostedScannerPeAndJitAsync(string body, int expected)
    {
        var root = RepositoryRoot();
        var native = Path.Combine(root, $"void-{Guid.NewGuid():N}.native.exe");
        var hosted = Path.Combine(root, $"void-{Guid.NewGuid():N}.hosted.exe");
        var scanner = Path.Combine(root, $"void-{Guid.NewGuid():N}.scanner.exe");
        try
        {
            var source = Encoding.UTF8.GetBytes($"void Main() {{ {body} }}");
            foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
            {
                var (exit, output, error) = await RunAsync(source, "frontend-build",
                    Path.Combine(root, "self-host"), "-", "-o", native, "--runtime", policy, "--json");
                Assert.True(exit == 0, $"{policy}: {output}{error}");
                using var document = JsonDocument.Parse(output);
                Assert.Equal(56, document.RootElement.GetProperty("result").GetProperty("projectionVersion").GetInt32());
                var (hostExit, hostOutput, hostError) = await RunAsync(source, "build",
                    "-", "-o", hosted, "--runtime", policy, "--json");
                Assert.True(hostExit == 0, $"{hostOutput}{hostError}");
                var (scanExit, scanOutput, scanError) = await RunAsync(source, "build-component",
                    Path.Combine(root, "self-host", "Scanner.terrum"), "-", "-o", scanner,
                    "--runtime", policy, "--json");
                Assert.True(scanExit == 0, $"{scanOutput}{scanError}");
                Assert.Equal(await File.ReadAllBytesAsync(hosted), await File.ReadAllBytesAsync(native));
                Assert.Equal(await File.ReadAllBytesAsync(hosted), await File.ReadAllBytesAsync(scanner));
                Assert.Equal(expected, await ExecuteNativeImageAsync(native));
                var (runExit, runOutput, runError) = await RunAsync(source, "frontend-run",
                    Path.Combine(root, "self-host"), "-", "--runtime", policy, "--json");
                Assert.True(runExit == expected, $"{runOutput}{runError}");
                var (jitExit, jitOutput, jitError) = await RunAsync(source, "jit", "-", "--runtime", policy, "--json");
                Assert.True(jitExit == expected, $"{jitOutput}{jitError}");
            }
        }
        finally
        {
            File.Delete(native);
            File.Delete(hosted);
            File.Delete(scanner);
        }
    }

    [Theory]
    [InlineData("void Main() { return 1; }")]
    [InlineData("void Main() { return int32(1); }")]
    [InlineData("void Main() { return true; }")]
    [InlineData("void Main() { return null; }")]
    [InlineData("void Main() { return (1); }")]
    [InlineData("void Main() { return Missing(); }")]
    [InlineData("void Main() { return () + (); }")]
    [InlineData("void Main() => ();")]
    [InlineData("void Main() { _ = (); }")]
    [InlineData("void Main() { require (); }")]
    [InlineData("void Main() { void x = (); }")]
    [InlineData("void Main() { var x = (); }")]
    [InlineData("void Main(void x) {}")]
    [InlineData("void Main(int32 x) {}")]
    [InlineData("void Other() {}")]
    [InlineData("void Main() { return; Missing(); }")]
    [InlineData("void Main() { require false; Missing(); }")]
    [InlineData("void Main() {} void Other() {}")]
    [InlineData("void Main() { { int32 x = 1; } require x == 1; }")]
    [InlineData("int32 Main() { return; }")]
    [InlineData("int32 Main() { return (); }")]
    [InlineData("int32 Main() {}")]
    public async Task NativeVoidRejectsUnsupportedSourceAndRetainsPeAsync(string text)
    {
        var path = Path.Combine(RepositoryRoot(), $"void-{Guid.NewGuid():N}.exe");
        try
        {
            foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
            {
                var (initial, initialOutput, initialError) = await RunAsync(Encoding.UTF8.GetBytes("void Main() {}"),
                    "frontend-build", Path.Combine(RepositoryRoot(), "self-host"), "-", "-o", path,
                    "--runtime", policy, "--json");
                Assert.True(initial == 0, $"{initialOutput}{initialError}");
                var bytes = await File.ReadAllBytesAsync(path);
                var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(text), "frontend-build",
                    Path.Combine(RepositoryRoot(), "self-host"), "-", "-o", path, "--runtime", policy, "--json");
                Assert.True(exit == 1, $"{exit}: {output}{error}");
                Assert.Equal(bytes, await File.ReadAllBytesAsync(path));
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("bool a", "require a;")]
    [InlineData("int32 a, uint64 b", "require a == 42 && b == uint64(42);")]
    [InlineData("char16 a, uint32 b, int64 c", "require a == char16(42) && b == uint32(42) && c == int64(42); return ();")]
    public async Task NativeVoidPreservesNormalizedExportAndZeroThroughThreeParameterAbiAsync(string parameters, string body)
    {
        var root = RepositoryRoot();
        var native = Path.Combine(root, $"void-{Guid.NewGuid():N}.native.dll");
        var hosted = Path.Combine(root, $"void-{Guid.NewGuid():N}.hosted.dll");
        try
        {
            var source = Encoding.UTF8.GetBytes($"public void @await({parameters}) {{ {body} }}");
            foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
            {
                var (exit, output, error) = await RunAsync(source, "frontend-build-library",
                    Path.Combine(root, "self-host"), "-", "-o", native, "--runtime", policy, "--json");
                Assert.True(exit == 0, $"{output}{error}");
                var (hostExit, hostOutput, hostError) = await RunAsync(source, "build-library",
                    "-", "--export", "await", "-o", hosted, "--runtime", policy, "--json");
                Assert.True(hostExit == 0, $"{hostOutput}{hostError}");
                Assert.Equal(await File.ReadAllBytesAsync(hosted), await File.ReadAllBytesAsync(native));
                var handle = NativeLibrary.Load(native);
                try
                {
                    var export = NativeLibrary.GetExport(handle, "await");
                    switch (parameters)
                    {
                        case "": Marshal.GetDelegateForFunctionPointer<NativeVoidZero>(export)(); break;
                        case "bool a": Marshal.GetDelegateForFunctionPointer<NativeVoidOne>(export)(true); break;
                        case "int32 a, uint64 b": Marshal.GetDelegateForFunctionPointer<NativeVoidTwo>(export)(42, 42); break;
                        default: Marshal.GetDelegateForFunctionPointer<NativeVoidThree>(export)(42, 42, 42); break;
                    }
                }
                finally
                {
                    NativeLibrary.Free(handle);
                }
            }
        }
        finally
        {
            File.Delete(native);
            File.Delete(hosted);
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate void NativeVoidZero();
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate void NativeVoidOne([MarshalAs(UnmanagedType.I1)] bool a);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate void NativeVoidTwo(int a, ulong b);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate void NativeVoidThree(ushort a, uint b, long c);

    [Theory]
    [InlineData("while (true) { continue; }")]
    [InlineData("for (;;) {}")]
    [InlineData("a: for (;;) { do { continue a; } while (false); }")]
    [InlineData("require false;")]
    public async Task NativeVoidNonFallthroughCompileOnlyAsync(string body)
    {
        var root = RepositoryRoot();
        var native = Path.Combine(root, $"void-{Guid.NewGuid():N}.native.exe");
        var hosted = Path.Combine(root, $"void-{Guid.NewGuid():N}.hosted.exe");
        try
        {
            var source = Encoding.UTF8.GetBytes($"void Main() {{ {body} }}");
            foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
            {
                var (exit, output, error) = await RunAsync(source, "frontend-build",
                    Path.Combine(root, "self-host"), "-", "-o", native, "--runtime", policy, "--json");
                Assert.True(exit == 0, $"{output}{error}");
                var (hostExit, hostOutput, hostError) = await RunAsync(source, "build",
                    "-", "-o", hosted, "--runtime", policy, "--json");
                Assert.True(hostExit == 0, $"{hostOutput}{hostError}");
                Assert.Equal(await File.ReadAllBytesAsync(hosted), await File.ReadAllBytesAsync(native));
            }
        }
        finally
        {
            File.Delete(native);
            File.Delete(hosted);
        }
    }

    [Theory]
    [InlineData("records", 12, true)]
    [InlineData("records", 13, false)]
    [InlineData("locals", 8, true)]
    [InlineData("locals", 9, false)]
    [InlineData("depth", 6, true)]
    [InlineData("depth", 7, false)]
    [InlineData("expression", 16, true)]
    [InlineData("expression", 17, false)]
    [InlineData("nodes", 32, true)]
    [InlineData("nodes", 33, false)]
    [InlineData("source", 16384, true)]
    [InlineData("source", 16385, false)]
    [InlineData("tokens", 64, true)]
    [InlineData("tokens", 65, false)]
    public async Task NativeVoidRetainsExactResourceCapsAsync(string resource, int count, bool valid)
    {
        var body = resource switch {
            "records" => string.Concat(Enumerable.Repeat("return; ", count)),
            "locals" => string.Concat(Enumerable.Repeat("_ = true; ", count)),
            "depth" => new string('{', count) + "return;" + new string('}', count),
            "expression" => "return " + new string('(', count) + new string(')', count) + ";",
            "nodes" => "require " + (count == 33 ? "!" : "")
                + string.Join(" && ", Enumerable.Repeat("true", 16)) + "; _ = true;",
            "tokens" => string.Concat(Enumerable.Range(0, 7).Select(i => $"int32 x{i} = {i}; "))
                + "require !true; return " + new string('(', 8) + new string(')', 8) + ";",
            _ => "",
        };
        var source = $"void Main() {{ {body} }}";
        if (resource == "source")
        {
            source += " /*" + new string('x', count - source.Length - 5) + "*/";
            Assert.Equal(count, source.Length);
        }
        if (resource == "tokens")
        {
            if (!valid) { source = source.Replace("x0 = 0", "x0 = +0", StringComparison.Ordinal); }
            Assert.Equal(count, Lexer.Lex(SourceText.From(source)).Tokens.Length);
        }
        foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
        {
            var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(source), "frontend-component",
                Path.Combine(RepositoryRoot(), "self-host"), "-", "--runtime", policy, "--json");
            Assert.True(exit == (valid ? 0 : resource == "source" ? 2 : 1), $"{output}{error}");
            if (valid)
            {
                using var document = JsonDocument.Parse(output);
                var result = document.RootElement.GetProperty("result");
                Assert.Equal(56, result.GetProperty("projectionVersion").GetInt32());
                if (resource == "locals") { Assert.Equal(count, result.GetProperty("locals").GetArrayLength()); }
                if (resource == "records") { Assert.Equal(count, result.GetProperty("statements").GetArrayLength()); }
                if (resource == "nodes") { Assert.Equal(count, result.GetProperty("nodes").GetArrayLength()); }
            }
        }
    }
}
