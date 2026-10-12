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
    [InlineData("mut int32 x = 0; outer: while (x < 2) { while (true) { x++; break outer; } } return x + 41;")]
    [InlineData("mut int32 x = 0; outer: while (x < 2) { do { x++; continue outer; } while (x++ < 8); } return x + 40;")]
    [InlineData("mut int32 x = 0; outer: for (; x < 2; x++) { while (true) { continue outer; } x++; } return x + 40;")]
    [InlineData("mut int32 x = 0; outer: do { for (;;) { x++; continue outer; } } while (++x < 4); return x + 38;")]
    [InlineData("mut int32 x = 0; outer: do { for (;; x++) { x++; break outer; } } while (x++ < 8); return x + 41;")]
    [InlineData("mut int32 x = 0; outer: for (;; x++) { do { x++; break outer; } while (x++ < 8); } return x + 41;")]
    [InlineData("mut int32 x = 0; outer: while (true) { for (;;) { do { x++; break outer; } while (false); } } return x + 41;")]
    [InlineData("mut int32 x = 0; outer: for (; x < 2; x++) { middle: do { while (true) { continue middle; } } while (false); } return x + 40;")]
    [InlineData("mut int32 x = 0; @const: while (x < 2) { { _ = x++; do { continue const; } while (x++ < 8); } } return x + 40;")]
    [InlineData("mut int32 x = 0; 名: do { while (true) { x++; break 名; } } while (true); return x + 41; /*\U0001F600*/")]
    [InlineData("mut int32 x = 0; a: do { x++; break a; } while (false); a: do { x++; break a; } while (false); return x + 40;")]
    [InlineData("mut int32 x = 0; outer: while (x < 2 && ++x < 4) { for (;;) { continue outer; } } return x + 40;")]
    [InlineData("mut int32 x = 0; outer: while (x < 2) { do { x++; if (x < 2) { continue outer; } } while (false); } return x + 40;")]
    [InlineData("mut int32 x = 0; outer: do { { int32 y = 42; while (true) { x = y; break outer; } } } while (true); return x;")]
    [InlineData("mut int32 x = 0; outer: for (; x < 2; x++) { middle: while (true) { do { break middle; } while (true); } } return x + 40;")]
    public async Task NativeNamedTransfersMatchHostedScannerPeAndJitAsync(string body)
    {
        var source = $"int32 Main() {{ {body} }}";
        Assert.True(Lexer.Lex(SourceText.From(source)).Tokens.Length <= 64);
        var root = RepositoryRoot();
        var native = Path.Combine(root, $"native-named-{Guid.NewGuid():N}.native.exe");
        var hosted = Path.Combine(root, $"native-named-{Guid.NewGuid():N}.hosted.exe");
        var scanner = Path.Combine(root, $"native-named-{Guid.NewGuid():N}.scanner.exe");
        try
        {
            foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
            {
                var input = Encoding.UTF8.GetBytes(source);
                var (exit, output, error) = await RunAsync(input, "frontend-build",
                    Path.Combine(root, "self-host"), "-", "-o", native, "--runtime", policy, "--json");
                Assert.True(exit == 0, $"{policy}: {output}{error}");
                using var document = JsonDocument.Parse(output);
                Assert.Equal(87, document.RootElement.GetProperty("result").GetProperty("projectionVersion").GetInt32());
                var (hostExit, hostOutput, hostError) = await RunAsync(input,
                    "build", "-", "-o", hosted, "--runtime", policy, "--json");
                Assert.True(hostExit == 0, $"{policy}: {hostOutput}{hostError}");
                var (scanExit, scanOutput, scanError) = await RunAsync(input, "build-component",
                    Path.Combine(root, "self-host"), "-", "-o", scanner, "--runtime", policy, "--json");
                Assert.True(scanExit == 0, $"{policy}: {scanOutput}{scanError}");
                Assert.Equal(await File.ReadAllBytesAsync(hosted), await File.ReadAllBytesAsync(native));
                Assert.Equal(await File.ReadAllBytesAsync(hosted), await File.ReadAllBytesAsync(scanner));
                Assert.Equal(42, await ExecuteNativeImageAsync(native));
                var (runExit, runOutput, runError) = await RunAsync(input, "frontend-run",
                    Path.Combine(root, "self-host"), "-", "--runtime", policy, "--json");
                Assert.True(runExit == 42, $"{policy}: {runOutput}{runError}");
                var (jitExit, jitOutput, jitError) = await RunAsync(input, "jit", "-", "--runtime", policy, "--json");
                Assert.True(jitExit == 42, $"{policy}: {jitOutput}{jitError}");
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
    [InlineData("a: while (true) { a: do { break a; } while (false); } return 42;")]
    [InlineData("@const: while (true) { const: do { break const; } while (false); } return 42;")]
    [InlineData("a: while (true) { break A; } return 42;")]
    [InlineData("while (true) { break later; } later: do {} while (false); return 42;")]
    [InlineData("a: do {} while (false); while (true) { continue a; } return 42;")]
    [InlineData("a: { while (true) { break a; } } return 42;")]
    [InlineData("a: if (true) { while (true) { continue a; } } return 42;")]
    [InlineData("a: while (true) { } break a; return 42;")]
    [InlineData("a : while (true) { break a; } return 42;")]
    [InlineData("a: while (true) { break @ordinary; } return 42;")]
    [InlineData("a: while (true) { while (true) { break a; } }")]
    [InlineData("a: do { while (true) { break a; } } while (true);")]
    [InlineData("a: for (;;) { while (true) { break a; } }")]
    [InlineData("a: do { { int32 y = 42; break a; } } while (false); return y;")]
    [InlineData("a: for (int32 y = 42;;) { break a; } return y;")]
    [InlineData("a: while (true) { break a; } return 42; } int32 Unused() { while (true) { break a; }")]
    public async Task NativeNamedTransfersRejectInvalidTargetsAndPreservePriorPeAsync(string body)
    {
        var path = Path.Combine(RepositoryRoot(), $"native-named-{Guid.NewGuid():N}.exe");
        try
        {
            foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
            {
                var (initial, initialOutput, initialError) = await RunAsync(
                    Encoding.UTF8.GetBytes("int32 Main() { a: do { break a; } while (false); return 42; }"),
                    "frontend-build", Path.Combine(RepositoryRoot(), "self-host"), "-", "-o", path,
                    "--runtime", policy, "--json");
                Assert.True(initial == 0, $"{initialOutput}{initialError}");
                var bytes = await File.ReadAllBytesAsync(path);
                var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes($"int32 Main() {{ {body} }}"),
                    "frontend-build", Path.Combine(RepositoryRoot(), "self-host"), "-", "-o", path,
                    "--runtime", policy, "--json");
                Assert.True(exit == 1, $"{policy}: {exit}: {output}{error}");
                Assert.Equal(bytes, await File.ReadAllBytesAsync(path));
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData("a: for (;;) { do { continue a; } while (false); }")]
    [InlineData("a: do { while (true) { continue a; } } while (true);")]
    [InlineData("a: for (;;) { while (true) { break; break a; } }")]
    public async Task NativeNamedNonFallthroughLoopsCompileOnlyAsync(string body)
    {
        var root = RepositoryRoot();
        var native = Path.Combine(root, $"native-named-{Guid.NewGuid():N}.native.exe");
        var hosted = Path.Combine(root, $"native-named-{Guid.NewGuid():N}.hosted.exe");
        try
        {
            foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
            {
                var input = Encoding.UTF8.GetBytes($"int32 Main() {{ {body} }}");
                var (exit, output, error) = await RunAsync(input, "frontend-build",
                    Path.Combine(root, "self-host"), "-", "-o", native, "--runtime", policy, "--json");
                Assert.True(exit == 0, $"{output}{error}");
                var (hostExit, hostOutput, hostError) = await RunAsync(input, "build", "-", "-o", hosted,
                    "--runtime", policy, "--json");
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
    [InlineData("int32", "int32(42)", 42)]
    [InlineData("uint32", "uint32(42)", 42)]
    [InlineData("int64", "int64(42)", 42)]
    [InlineData("uint64", "uint64(42)", 42)]
    [InlineData("char16", "char16(42)", 42)]
    [InlineData("bool", "true", 1)]
    public async Task NativeNamedTransfersPreserveAllSixNormalizedLibraryExportsAsync(string type, string value, int expected)
    {
        var root = RepositoryRoot();
        var native = Path.Combine(root, $"native-named-{Guid.NewGuid():N}.native.dll");
        var hosted = Path.Combine(root, $"native-named-{Guid.NewGuid():N}.hosted.dll");
        try
        {
            var source = Encoding.UTF8.GetBytes($"public {type} @await() {{ a: do {{ while (true) {{ break a; }} }} while (false); return {value}; }}");
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
                    var actual = type switch {
                        "int32" => Marshal.GetDelegateForFunctionPointer<ScopedInt32>(export)(),
                        "uint32" => (long)Marshal.GetDelegateForFunctionPointer<ScopedUInt32>(export)(),
                        "int64" => Marshal.GetDelegateForFunctionPointer<ScopedInt64>(export)(),
                        "uint64" => (long)Marshal.GetDelegateForFunctionPointer<ScopedUInt64>(export)(),
                        "char16" => Marshal.GetDelegateForFunctionPointer<ScopedChar16>(export)(),
                        "bool" => Marshal.GetDelegateForFunctionPointer<ScopedBool>(export)() ? 1 : 0,
                        _ => throw new InvalidOperationException(type),
                    };
                    Assert.Equal(expected, actual);
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

    [Fact]
    public async Task NativeNamedTransfersPreserveThreeParameterLibraryAbiAsync()
    {
        var root = RepositoryRoot();
        var native = Path.Combine(root, $"native-named-{Guid.NewGuid():N}.native.dll");
        var hosted = Path.Combine(root, $"native-named-{Guid.NewGuid():N}.hosted.dll");
        try
        {
            var source = Encoding.UTF8.GetBytes("public int32 @await(int32 a, int32 b, int32 c) { @const: do { while (true) { break const; } } while (false); return a + b + c; }");
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
                    var export = Marshal.GetDelegateForFunctionPointer<DoForThreeScalars>(
                        NativeLibrary.GetExport(handle, "await"));
                    Assert.Equal(42, export(10, 20, 12));
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

    [Theory]
    [InlineData(8, true)]
    [InlineData(9, false)]
    public Task NativeNamedTransfersCountAllHiddenAndNamedIdentitiesAsync(int count, bool valid)
        => CheckNativeNamedBoundaryAsync("int32 Main() { int32 x = 42; "
            + string.Concat(Enumerable.Repeat("_ = true; ", count - 1))
            + "a: do { break a; } while (false); return x; }", valid, locals: count);

    [Theory]
    [InlineData(6, true)]
    [InlineData(7, false)]
    public Task NativeNamedTransfersRetainSourceStructuralDepthAsync(int depth, bool valid)
        => CheckNativeNamedBoundaryAsync("int32 Main() { a: do { " + new string('{', depth - 1)
            + "break a;" + new string('}', depth - 1) + " } while (false); return 42; }", valid);

    [Theory]
    [InlineData(12, true)]
    [InlineData(13, false)]
    public Task NativeNamedTransfersRetainStatementRecordLimitAsync(int count, bool valid)
        => CheckNativeNamedBoundaryAsync("int32 Main() { "
            + string.Concat(Enumerable.Repeat("{} ", count - 3))
            + "a: do { break a; } while (false); return 42; }", valid, statements: count);

    [Theory]
    [InlineData(16, true)]
    [InlineData(17, false)]
    public Task NativeNamedTransfersRetainExpressionRecursionLimitAsync(int depth, bool valid)
        => CheckNativeNamedBoundaryAsync("int32 Main() { a: do { break a; } while (false); return "
            + new string('(', depth - 1) + "42" + new string(')', depth - 1) + "; }", valid);

    [Theory]
    [InlineData(32, true)]
    [InlineData(33, true)]
    public Task NativeNamedTransfersAcceptProvisionalExpressionNodeExtensionAsync(int count, bool valid)
        => CheckNativeNamedBoundaryAsync("int32 Main() { a: do { break a; } while (false); return "
            + (count == 33 ? "+" : "") + string.Join(" + ", Enumerable.Repeat("1", 16)) + "; }", valid, nodes: count);

    [Theory]
    [InlineData(64, true)]
    [InlineData(65, true)]
    public async Task NativeNamedTransfersAcceptTokensBeyondThePreviousWindowAsync(int count, bool valid)
    {
        var source = "int32 Main() { " + string.Concat(Enumerable.Repeat("{} ", 8))
            + "a: do { while (true) { break a; } } while (false); return +"
            + new string('(', 9) + (count == 64 ? "42" : "+42") + new string(')', 9) + "; }";
        Assert.Equal(count, Lexer.Lex(SourceText.From(source)).Tokens.Length);
        await CheckNativeNamedBoundaryAsync(source, valid, checkTokens: false);
    }

    [Theory]
    [InlineData(16384, true)]
    [InlineData(16385, false)]
    public async Task NativeNamedTransfersRetainExactUtf16SourceLimitAsync(int length, bool valid)
    {
        const string prefix = "int32 Main() { a: do { while (true) { break a; } } while (false); return 42; } /*";
        var source = prefix + new string('x', length - prefix.Length - 2) + "*/";
        Assert.Equal(length, source.Length);
        foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
        {
            var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(source),
                "frontend-component", Path.Combine(RepositoryRoot(), "self-host"), "-",
                "--runtime", policy, "--json");
            Assert.True(exit == (valid ? 0 : 2), $"{output}{error}");
        }
    }

    private static async Task CheckNativeNamedBoundaryAsync(string source, bool valid, bool checkTokens = true,
        int? locals = null, int? statements = null, int? nodes = null)
    {
        if (checkTokens) { Assert.True(Lexer.Lex(SourceText.From(source)).Tokens.Length <= 64); }
        foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
        {
            var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(source),
                "frontend-component", Path.Combine(RepositoryRoot(), "self-host"), "-",
                "--runtime", policy, "--json");
            Assert.True(exit == (valid ? 0 : 1), $"{output}{error}");
            if (valid)
            {
                using var document = JsonDocument.Parse(output);
                var result = document.RootElement.GetProperty("result");
                Assert.Equal(87, result.GetProperty("projectionVersion").GetInt32());
                if (locals is int localCount) { Assert.Equal(localCount, result.GetProperty("locals").GetArrayLength()); }
                if (statements is int statementCount) { Assert.Equal(statementCount, result.GetProperty("statements").GetArrayLength()); }
                if (nodes is int nodeCount) { Assert.Equal(nodeCount, result.GetProperty("nodes").GetArrayLength()); }
            }
        }
    }
}
