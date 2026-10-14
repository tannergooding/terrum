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
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests;

public sealed partial class CliTests
{
    [TestCase("mut int32 x = 0; outer: while (x < 2) { while (true) { x++; break outer; } } return x + 41;")]
    [TestCase("mut int32 x = 0; outer: while (x < 2) { do { x++; continue outer; } while (x++ < 8); } return x + 40;")]
    [TestCase("mut int32 x = 0; outer: for (; x < 2; x++) { while (true) { continue outer; } x++; } return x + 40;")]
    [TestCase("mut int32 x = 0; outer: do { for (;;) { x++; continue outer; } } while (++x < 4); return x + 38;")]
    [TestCase("mut int32 x = 0; outer: do { for (;; x++) { x++; break outer; } } while (x++ < 8); return x + 41;")]
    [TestCase("mut int32 x = 0; outer: for (;; x++) { do { x++; break outer; } while (x++ < 8); } return x + 41;")]
    [TestCase("mut int32 x = 0; outer: while (true) { for (;;) { do { x++; break outer; } while (false); } } return x + 41;")]
    [TestCase("mut int32 x = 0; outer: for (; x < 2; x++) { middle: do { while (true) { continue middle; } } while (false); } return x + 40;")]
    [TestCase("mut int32 x = 0; @const: while (x < 2) { { _ = x++; do { continue const; } while (x++ < 8); } } return x + 40;")]
    [TestCase("mut int32 x = 0; 名: do { while (true) { x++; break 名; } } while (true); return x + 41; /*\U0001F600*/")]
    [TestCase("mut int32 x = 0; a: do { x++; break a; } while (false); a: do { x++; break a; } while (false); return x + 40;")]
    [TestCase("mut int32 x = 0; outer: while (x < 2 && ++x < 4) { for (;;) { continue outer; } } return x + 40;")]
    [TestCase("mut int32 x = 0; outer: while (x < 2) { do { x++; if (x < 2) { continue outer; } } while (false); } return x + 40;")]
    [TestCase("mut int32 x = 0; outer: do { { int32 y = 42; while (true) { x = y; break outer; } } } while (true); return x;")]
    [TestCase("mut int32 x = 0; outer: for (; x < 2; x++) { middle: while (true) { do { break middle; } while (true); } } return x + 40;")]
    public async Task NativeNamedTransfersMatchHostedScannerPeAndJitAsync(string body)
    {
        var source = $"int32 Main() {{ {body} }}";
        Assert.That(Lexer.Lex(SourceText.From(source)).Tokens.Length <= 64, Is.True);
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
                Assert.That(exit == 0, Is.True, $"{policy}: {output}{error}");
                using var document = JsonDocument.Parse(output);
                Assert.That(document.RootElement.GetProperty("result").GetProperty("projectionVersion").GetInt32(), Is.EqualTo(87));
                var (hostExit, hostOutput, hostError) = await RunAsync(input,
                    "build", "-", "-o", hosted, "--runtime", policy, "--json");
                Assert.That(hostExit == 0, Is.True, $"{policy}: {hostOutput}{hostError}");
                var (scanExit, scanOutput, scanError) = await RunAsync(input, "build-component",
                    Path.Combine(root, "self-host"), "-", "-o", scanner, "--runtime", policy, "--json");
                Assert.That(scanExit == 0, Is.True, $"{policy}: {scanOutput}{scanError}");
                Assert.That(await File.ReadAllBytesAsync(native), Is.EqualTo(await File.ReadAllBytesAsync(hosted)));
                Assert.That(await File.ReadAllBytesAsync(scanner), Is.EqualTo(await File.ReadAllBytesAsync(hosted)));
                Assert.That(await ExecuteNativeImageAsync(native), Is.EqualTo(42));
                var (runExit, runOutput, runError) = await RunAsync(input, "frontend-run",
                    Path.Combine(root, "self-host"), "-", "--runtime", policy, "--json");
                Assert.That(runExit == 42, Is.True, $"{policy}: {runOutput}{runError}");
                var (jitExit, jitOutput, jitError) = await RunAsync(input, "jit", "-", "--runtime", policy, "--json");
                Assert.That(jitExit == 42, Is.True, $"{policy}: {jitOutput}{jitError}");
            }
        }
        finally
        {
            await TestFileCleanup.DeleteWithRetryAsync(native).ConfigureAwait(true);
            File.Delete(hosted);
            File.Delete(scanner);
        }
    }
    [TestCase("a: while (true) { a: do { break a; } while (false); } return 42;")]
    [TestCase("@const: while (true) { const: do { break const; } while (false); } return 42;")]
    [TestCase("a: while (true) { break A; } return 42;")]
    [TestCase("while (true) { break later; } later: do {} while (false); return 42;")]
    [TestCase("a: do {} while (false); while (true) { continue a; } return 42;")]
    [TestCase("a: { while (true) { break a; } } return 42;")]
    [TestCase("a: if (true) { while (true) { continue a; } } return 42;")]
    [TestCase("a: while (true) { } break a; return 42;")]
    [TestCase("a : while (true) { break a; } return 42;")]
    [TestCase("a: while (true) { break @ordinary; } return 42;")]
    [TestCase("a: while (true) { while (true) { break a; } }")]
    [TestCase("a: do { while (true) { break a; } } while (true);")]
    [TestCase("a: for (;;) { while (true) { break a; } }")]
    [TestCase("a: do { { int32 y = 42; break a; } } while (false); return y;")]
    [TestCase("a: for (int32 y = 42;;) { break a; } return y;")]
    [TestCase("a: while (true) { break a; } return 42; } int32 Unused() { while (true) { break a; }")]
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
                Assert.That(initial == 0, Is.True, $"{initialOutput}{initialError}");
                var bytes = await File.ReadAllBytesAsync(path);
                var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes($"int32 Main() {{ {body} }}"),
                    "frontend-build", Path.Combine(RepositoryRoot(), "self-host"), "-", "-o", path,
                    "--runtime", policy, "--json");
                Assert.That(exit == 1, Is.True, $"{policy}: {exit}: {output}{error}");
                Assert.That(await File.ReadAllBytesAsync(path), Is.EqualTo(bytes));
            }
        }
        finally
        {
            File.Delete(path);
        }
    }
    [TestCase("a: for (;;) { do { continue a; } while (false); }")]
    [TestCase("a: do { while (true) { continue a; } } while (true);")]
    [TestCase("a: for (;;) { while (true) { break; break a; } }")]
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
                Assert.That(exit == 0, Is.True, $"{output}{error}");
                var (hostExit, hostOutput, hostError) = await RunAsync(input, "build", "-", "-o", hosted,
                    "--runtime", policy, "--json");
                Assert.That(hostExit == 0, Is.True, $"{hostOutput}{hostError}");
                Assert.That(await File.ReadAllBytesAsync(native), Is.EqualTo(await File.ReadAllBytesAsync(hosted)));
            }
        }
        finally
        {
            File.Delete(native);
            File.Delete(hosted);
        }
    }
    [TestCase("int32","int32(42)",42)]
    [TestCase("uint32","uint32(42)",42)]
    [TestCase("int64","int64(42)",42)]
    [TestCase("uint64","uint64(42)",42)]
    [TestCase("char16","char16(42)",42)]
    [TestCase("bool","true",1)]
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
                Assert.That(exit == 0, Is.True, $"{output}{error}");
                var (hostExit, hostOutput, hostError) = await RunAsync(source, "build-library",
                    "-", "--export", "await", "-o", hosted, "--runtime", policy, "--json");
                Assert.That(hostExit == 0, Is.True, $"{hostOutput}{hostError}");
                Assert.That(await File.ReadAllBytesAsync(native), Is.EqualTo(await File.ReadAllBytesAsync(hosted)));
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
                    Assert.That(actual, Is.EqualTo(expected));
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

    [Test]
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
                Assert.That(exit == 0, Is.True, $"{output}{error}");
                var (hostExit, hostOutput, hostError) = await RunAsync(source, "build-library",
                    "-", "--export", "await", "-o", hosted, "--runtime", policy, "--json");
                Assert.That(hostExit == 0, Is.True, $"{hostOutput}{hostError}");
                Assert.That(await File.ReadAllBytesAsync(native), Is.EqualTo(await File.ReadAllBytesAsync(hosted)));
                var handle = NativeLibrary.Load(native);
                try
                {
                    var export = Marshal.GetDelegateForFunctionPointer<DoForThreeScalars>(
                        NativeLibrary.GetExport(handle, "await"));
                    Assert.That(export(10, 20, 12), Is.EqualTo(42));
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
    [TestCase(8,true)]
    [TestCase(9,false)]
    public Task NativeNamedTransfersCountAllHiddenAndNamedIdentitiesAsync(int count, bool valid)
        => CheckNativeNamedBoundaryAsync("int32 Main() { int32 x = 42; "
            + string.Concat(Enumerable.Repeat("_ = true; ", count - 1))
            + "a: do { break a; } while (false); return x; }", valid, locals: count);
    [TestCase(6,true)]
    [TestCase(7,false)]
    public Task NativeNamedTransfersRetainSourceStructuralDepthAsync(int depth, bool valid)
        => CheckNativeNamedBoundaryAsync("int32 Main() { a: do { " + new string('{', depth - 1)
            + "break a;" + new string('}', depth - 1) + " } while (false); return 42; }", valid);
    [TestCase(12,true)]
    [TestCase(13,false)]
    public Task NativeNamedTransfersRetainStatementRecordLimitAsync(int count, bool valid)
        => CheckNativeNamedBoundaryAsync("int32 Main() { "
            + string.Concat(Enumerable.Repeat("{} ", count - 3))
            + "a: do { break a; } while (false); return 42; }", valid, statements: count);
    [TestCase(16,true)]
    [TestCase(17,false)]
    public Task NativeNamedTransfersRetainExpressionRecursionLimitAsync(int depth, bool valid)
        => CheckNativeNamedBoundaryAsync("int32 Main() { a: do { break a; } while (false); return "
            + new string('(', depth - 1) + "42" + new string(')', depth - 1) + "; }", valid);
    [TestCase(32,true)]
    [TestCase(33,true)]
    public Task NativeNamedTransfersAcceptProvisionalExpressionNodeExtensionAsync(int count, bool valid)
        => CheckNativeNamedBoundaryAsync("int32 Main() { a: do { break a; } while (false); return "
            + (count == 33 ? "+" : "") + string.Join(" + ", Enumerable.Repeat("1", 16)) + "; }", valid, nodes: count);
    [TestCase(64,true)]
    [TestCase(65,true)]
    public async Task NativeNamedTransfersAcceptTokensBeyondThePreviousWindowAsync(int count, bool valid)
    {
        var source = "int32 Main() { " + string.Concat(Enumerable.Repeat("{} ", 8))
            + "a: do { while (true) { break a; } } while (false); return +"
            + new string('(', 9) + (count == 64 ? "42" : "+42") + new string(')', 9) + "; }";
        Assert.That(Lexer.Lex(SourceText.From(source)).Tokens.Length, Is.EqualTo(count));
        await CheckNativeNamedBoundaryAsync(source, valid, checkTokens: false);
    }
    [TestCase(16384,true)]
    [TestCase(16385,false)]
    public async Task NativeNamedTransfersRetainExactUtf16SourceLimitAsync(int length, bool valid)
    {
        const string prefix = "int32 Main() { a: do { while (true) { break a; } } while (false); return 42; } /*";
        var source = prefix + new string('x', length - prefix.Length - 2) + "*/";
        Assert.That(source.Length, Is.EqualTo(length));
        foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
        {
            var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(source),
                "frontend-component", Path.Combine(RepositoryRoot(), "self-host"), "-",
                "--runtime", policy, "--json");
            Assert.That(exit == (valid ? 0 : 2), Is.True, $"{output}{error}");
        }
    }

    private static async Task CheckNativeNamedBoundaryAsync(string source, bool valid, bool checkTokens = true,
        int? locals = null, int? statements = null, int? nodes = null)
    {
        if (checkTokens)
        { Assert.That(Lexer.Lex(SourceText.From(source)).Tokens.Length <= 64, Is.True); }
        foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
        {
            var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(source),
                "frontend-component", Path.Combine(RepositoryRoot(), "self-host"), "-",
                "--runtime", policy, "--json");
            Assert.That(exit == (valid ? 0 : 1), Is.True, $"{output}{error}");
            if (valid)
            {
                using var document = JsonDocument.Parse(output);
                var result = document.RootElement.GetProperty("result");
                Assert.That(result.GetProperty("projectionVersion").GetInt32(), Is.EqualTo(87));
                if (locals is int localCount)
                { Assert.That(result.GetProperty("locals").GetArrayLength(), Is.EqualTo(localCount)); }
                if (statements is int statementCount)
                { Assert.That(result.GetProperty("statements").GetArrayLength(), Is.EqualTo(statementCount)); }
                if (nodes is int nodeCount)
                { Assert.That(result.GetProperty("nodes").GetArrayLength(), Is.EqualTo(nodeCount)); }
            }
        }
    }
}
