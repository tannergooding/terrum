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
    [TestCase("_ = int32(1); return 42;")]
    [TestCase("_ = uint32(4294967295); return 42;")]
    [TestCase("_ = int64(-9223372036854775808); return 42;")]
    [TestCase("_ = uint64(18446744073709551615); return 42;")]
    [TestCase("_ = char16(65535); return 42;")]
    [TestCase("_ = true; return 42;")]
    [TestCase("_ = int32(uint64(42)); return 42;")]
    [TestCase("int32 _name = 42; _ = _name; return _name;")]
    [TestCase("mut int32 x = 40; _ = ++x; _ = x++; return x;")]
    [TestCase("mut int32 x = 40; _ = x++ + ++x; return x;")]
    [TestCase("mut int32 x = 40; x += 1; _ = x++; return x;")]
    [TestCase("mut int32 x = 0; _ = false && ++x == 1; _ = true || ++x == 1; return x + 42;")]
    [TestCase("mut int32 x = 40; _ = true ? ++x : x++; return x + 1;")]
    [TestCase("int32 x = 1; _ = 2 + x; _ = x < 2; _ = !(x == 2); return 42;")]
    [TestCase("{ _ = int32(7); } { _ = false; } return 42;")]
    [TestCase("if (true) { _ = int32(7); return 42; } else { _ = false; return 0; }")]
    [TestCase("mut int32 n = 0; while (n < 2) { _ = n++; } return n + 40;")]
    [TestCase("mut int32 n = 0; do { _ = ++n; } while (n < 2); return n + 40;")]
    [TestCase("mut int32 n = 0; for (; n < 2; n++) { _ = n; } return n + 40;")]
    [TestCase("mut int32 @const = 40; _ = const++; _ = ++@const; return const;")]
    [TestCase("/*\U0001F600*/ mut int32 名 = 40; _ = 名++; _ = ++名; return 名;")]
    [TestCase("_/*c*/=/*c*/int32(7); return 42;")]
    public async Task ScalarDiscardsMatchHostedScannerPeAndJitAsync(string body)
    {
        var source = $"int32 Main() {{ {body} }}";
        Assert.That(Lexer.Lex(SourceText.From(source)).Tokens.Length <= 64, Is.True);
        var root = RepositoryRoot();
        var native = Path.Combine(root, $"discard-{Guid.NewGuid():N}.native.exe");
        var hosted = Path.Combine(root, $"discard-{Guid.NewGuid():N}.hosted.exe");
        var scanner = Path.Combine(root, $"discard-{Guid.NewGuid():N}.scanner.exe");
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
                    Path.Combine(root, "self-host"), "-", "-o", scanner,
                    "--runtime", policy, "--json");
                Assert.That(scanExit == 0, Is.True, $"{policy}: {scanOutput}{scanError}");
                Assert.That(await File.ReadAllBytesAsync(native), Is.EqualTo(await File.ReadAllBytesAsync(hosted)));
                Assert.That(await File.ReadAllBytesAsync(scanner), Is.EqualTo(await File.ReadAllBytesAsync(hosted)));
                Assert.That(await ExecuteNativeImageAsync(native), Is.EqualTo(42));
                var (runExit, runOutput, runError) = await RunAsync(input, "frontend-run",
                    Path.Combine(root, "self-host"), "-", "--runtime", policy, "--json");
                Assert.That(runExit == 42, Is.True, $"{policy}: {runOutput}{runError}");
                var (jitExit, jitOutput, jitError) = await RunAsync(input, "jit",
                    "-", "--runtime", policy, "--json");
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
    [TestCase("_ = 1; return 42;")]
    [TestCase("_ = -1; return 42;")]
    [TestCase("_ = 1 + 2; return 42;")]
    [TestCase("_ = true ? 1 : 2; return 42;")]
    [TestCase("_ = (); return 42;")]
    [TestCase("_ = bool(true); return 42;")]
    [TestCase("_ = Missing(); return 42;")]
    [TestCase("_ += int32(1); return 42;")]
    [TestCase("_= int32(1); return 42;")]
    [TestCase("_ =int32(1); return 42;")]
    [TestCase("_ = _; return 42;")]
    [TestCase("_ = int32(1); return _;")]
    [TestCase("int32 _ = 1; return 42;")]
    [TestCase("for (_ = int32(1); false;) {} return 42;")]
    [TestCase("for (; false; _ = int32(1)) {} return 42;")]
    [TestCase("mut int32 x = 1; _ = (x += 1); return 42;")]
    [TestCase("_ = int32(1);")]
    public async Task ScalarDiscardsRejectInvalidOrUnsupportedSourceAtomicallyAsync(string body)
    {
        var source = $"int32 Main() {{ {body} }}";
        var path = Path.Combine(RepositoryRoot(), $"discard-{Guid.NewGuid():N}.exe");
        try
        {
            foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
            {
                var (initial, initialOutput, initialError) = await RunAsync(
                    Encoding.UTF8.GetBytes("int32 Main() { _ = int32(1); return 42; }"),
                    "frontend-build", Path.Combine(RepositoryRoot(), "self-host"), "-",
                    "-o", path, "--runtime", policy, "--json");
                Assert.That(initial == 0, Is.True, $"{initialOutput}{initialError}");
                var bytes = await File.ReadAllBytesAsync(path);
                var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(source),
                    "frontend-build", Path.Combine(RepositoryRoot(), "self-host"), "-",
                    "-o", path, "--runtime", policy, "--json");
                Assert.That(exit == 1, Is.True, $"{policy}: {exit}: {output}{error}");
                Assert.That(await File.ReadAllBytesAsync(path), Is.EqualTo(bytes));
            }
        }
        finally
        {
            File.Delete(path);
        }
    }
    [TestCase("int32","int32(42)",42)]
    [TestCase("uint32","uint32(42)",42)]
    [TestCase("int64","int64(42)",42)]
    [TestCase("uint64","uint64(42)",42)]
    [TestCase("char16","char16(42)",42)]
    [TestCase("bool","true",1)]
    public async Task ScalarDiscardsPreserveAllSixLibraryExportsAsync(string type, string value, int expected)
    {
        var root = RepositoryRoot();
        var native = Path.Combine(root, $"discard-{Guid.NewGuid():N}.native.dll");
        var hosted = Path.Combine(root, $"discard-{Guid.NewGuid():N}.hosted.dll");
        try
        {
            var source = Encoding.UTF8.GetBytes($"public {type} Value() {{ _ = {value}; return {value}; }}");
            foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
            {
                var (exit, output, error) = await RunAsync(source, "frontend-build-library",
                    Path.Combine(root, "self-host"), "-", "-o", native, "--runtime", policy, "--json");
                Assert.That(exit == 0, Is.True, $"{output}{error}");
                var (hostExit, hostOutput, hostError) = await RunAsync(source, "build-library",
                    "-", "--export", "Value", "-o", hosted, "--runtime", policy, "--json");
                Assert.That(hostExit == 0, Is.True, $"{hostOutput}{hostError}");
                Assert.That(await File.ReadAllBytesAsync(native), Is.EqualTo(await File.ReadAllBytesAsync(hosted)));
                var handle = NativeLibrary.Load(native);
                try
                {
                    var export = NativeLibrary.GetExport(handle, "Value");
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
    [TestCase("int32(1) / int32(0)",unchecked((int)0xE0540002))]
    [TestCase("int32(2147483647) + int32(1)",unchecked((int)0xE0540001))]
    [TestCase("int32(uint64(4294967296))",unchecked((int)0xE0540001))]
    public async Task ScalarDiscardsDoNotEliminateCheckedFailuresAsync(string expression, int expected)
    {
        var source = Encoding.UTF8.GetBytes($"int32 Main() {{ _ = {expression}; return 42; }}");
        var path = Path.Combine(RepositoryRoot(), $"discard-{Guid.NewGuid():N}.exe");
        try
        {
            foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
            {
                var (exit, output, error) = await RunAsync(source, "frontend-build",
                    Path.Combine(RepositoryRoot(), "self-host"), "-", "-o", path, "--runtime", policy, "--json");
                Assert.That(exit == 0, Is.True, $"{output}{error}");
                Assert.That(await ExecuteNativeImageAsync(path), Is.EqualTo(expected));
                var (jitExit, jitOutput, jitError) = await RunAsync(source, "frontend-run",
                    Path.Combine(RepositoryRoot(), "self-host"), "-", "--runtime", policy, "--json");
                Assert.That(jitExit == expected, Is.True, $"{jitExit}: {jitOutput}{jitError}");
            }
        }
        finally
        {
            await TestFileCleanup.DeleteWithRetryAsync(path).ConfigureAwait(true);
        }
    }
    [TestCase(8,true)]
    [TestCase(9,false)]
    public Task ScalarDiscardsConsumeEightTotalHiddenAndNamedLocalIdentitiesAsync(int count, bool valid)
        => CheckDiscardBoundaryAsync("int32 Main() { "
            + string.Concat(Enumerable.Repeat("_ = true; ", count - 1))
            + "int32 x = 42; return x; }", valid);
    [TestCase(6,true)]
    [TestCase(7,false)]
    public Task ScalarDiscardsGeneratedBlocksDoNotConsumeSourceDepthAsync(int depth, bool valid)
        => CheckDiscardBoundaryAsync("int32 Main() { " + new string('{', depth)
            + "_ = true; return 42;" + new string('}', depth) + " }", valid);
    [TestCase(12,true)]
    [TestCase(13,false)]
    public Task ScalarDiscardsCountAgainstStatementRecordsAsync(int count, bool valid)
        => CheckDiscardBoundaryAsync("int32 Main() { "
            + string.Concat(Enumerable.Repeat("{} ", count - 3))
            + "_ = true; _ = false; return 42; }", valid);
    [TestCase(16384,true)]
    [TestCase(16385,false)]
    public async Task ScalarDiscardsRetainExactSourceLimitAsync(int length, bool valid)
    {
        const string prefix = "int32 Main() { _ = true; return 42; } /*";
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

    private static async Task CheckDiscardBoundaryAsync(string source, bool valid, int? nodes = null)
    {
        Assert.That(Lexer.Lex(SourceText.From(source)).Tokens.Length <= 64, Is.True);
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
                if (nodes is int nodeCount)
                {
                    Assert.That(result.GetProperty("nodes").GetArrayLength(), Is.EqualTo(nodeCount));
                }
            }
        }
    }
    [TestCase(64,true)]
    [TestCase(65,true)]
    public async Task ScalarDiscardsAcceptTokensBeyondThePreviousWindowAsync(int tokens, bool valid)
    {
        var declarations = string.Concat(Enumerable.Range(0, 7).Select(i => $"int32 x{i} = {i}; "));
        var expression = tokens == 64 ? "+(((((((42)))))))" : "+(((((((+42)))))))";
        var source = $"int32 Main() {{ {declarations}_ = true; return {expression}; }}";
        Assert.That(Lexer.Lex(SourceText.From(source)).Tokens.Length, Is.EqualTo(tokens));
        foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
        {
            var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(source),
                "frontend-component", Path.Combine(RepositoryRoot(), "self-host"), "-",
                "--runtime", policy, "--json");
            Assert.That(exit == (valid ? 0 : 1), Is.True, $"{output}{error}");
        }
    }
    [TestCase(32,true)]
    [TestCase(33,true)]
    public async Task ScalarDiscardsAcceptBeyondPreviousNodeLimitAsync(int nodes, bool valid)
    {
        var left = string.Join(" + ", Enumerable.Repeat("1", 7));
        var right = string.Join(" + ", Enumerable.Repeat("1", 6)) + " + 29";
        var groups = nodes - 30;
        var expression = new string('(', groups) + $"({left}) + ({right})" + new string(')', groups);
        await CheckDiscardBoundaryAsync($"int32 Main() {{ _ = true; return {expression}; }}",
            valid, nodes: nodes);
    }
    [TestCase(15,true)]
    [TestCase(16,false)]
    public Task ScalarDiscardsRetainSixteenExpressionRecursionLevelsAsync(int groups, bool valid)
        => CheckDiscardBoundaryAsync("int32 Main() { _ = " + new string('(', groups)
            + "true" + new string(')', groups) + "; return 42; }", valid);

    [Test]
    public async Task ScalarDiscardsPreserveNormalizedThreeParameterExportAsync()
    {
        var path = Path.Combine(RepositoryRoot(), $"discard-{Guid.NewGuid():N}.dll");
        try
        {
            var input = Encoding.UTF8.GetBytes(
                "public int32 @await(int32 @const, int32 some, int32 c) { _ = const + @some; return c; }");
            foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
            {
                var (exit, output, error) = await RunAsync(input, "frontend-build-library",
                    Path.Combine(RepositoryRoot(), "self-host"), "-", "-o", path, "--runtime", policy, "--json");
                Assert.That(exit == 0, Is.True, $"{output}{error}");
                var handle = NativeLibrary.Load(path);
                try
                {
                    var invoke = Marshal.GetDelegateForFunctionPointer<DoForThreeScalars>(
                        NativeLibrary.GetExport(handle, "await"));
                    Assert.That(invoke(1, 20, 42), Is.EqualTo(42));
                }
                finally
                {
                    NativeLibrary.Free(handle);
                }
            }
        }
        finally
        {
            File.Delete(path);
        }
    }
    [TestCase("for (;;) { _ = true; }")]
    [TestCase("for (;;) { _ = int32(1); continue; }")]
    [TestCase("do { _ = true; continue; } while (true);")]
    public async Task ScalarDiscardsNonFallthroughCompileWithoutExecutingAsync(string body)
    {
        var path = Path.Combine(RepositoryRoot(), $"discard-{Guid.NewGuid():N}.exe");
        try
        {
            foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
            {
                var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes($"int32 Main() {{ {body} }}"),
                    "frontend-build", Path.Combine(RepositoryRoot(), "self-host"), "-", "-o", path,
                    "--runtime", policy, "--json");
                Assert.That(exit == 0, Is.True, $"{output}{error}");
            }
        }
        finally
        {
            File.Delete(path);
        }
    }
}
