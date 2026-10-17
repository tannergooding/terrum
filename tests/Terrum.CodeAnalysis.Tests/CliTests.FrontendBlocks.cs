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
    [TestCase("int32 Main() { {} return 42; }")]
    [TestCase("int32 Main() { { { int32 x = 42; return x; } } }")]
    [TestCase("int32 Main() { { int32 x = 1; } { int32 x = 42; return x; } }")]
    [TestCase("int32 Main() { { int32 x = 1; } int32 x = 42; return x; }")]
    [TestCase("int32 Main() { mut int32 x = 40; { int32 y = 2; x += y; } return x; }")]
    [TestCase("int32 Main() { mut int32 x = 1; { x += x++; } return x + 40; }")]
    [TestCase("int32 Main() { { mut int32 x = 1; { int32 y = ++x; x += y; } return x + 38; } }")]
    [TestCase("int32 Main() { mut int32 n = 0; while (n < 3) { { n++; if (n == 2) { continue; } if (n == 3) { break; } } } return n + 39; }")]
    [TestCase("int32 Main() { mut int32 n = 0; do { { n++; continue; } } while (n < 3); return n + 39; }")]
    [TestCase("int32 Main() { mut int32 n = 0; for (; n < 3; n++) { { continue; } } return n + 39; }")]
    [TestCase("int32 Main() { do { { return 42; } } while (false); }")]
    [TestCase("int32 Main() { for (;;) { { return 42; } } }")]
    [TestCase("int32 Main() { { while (true) { { break; } } return 42; } }")]
    [TestCase("int32 Main() { { do { { break; } } while (true); return 42; } }")]
    [TestCase("int32 Main() { { for (;;) { { break; } } return 42; } }")]
    [TestCase("int32 Main() { { while (true) { { return 42; break; } } } }")]
    [TestCase("int32 Main() { { while (true) { while (false) { break; } return 42; } } }")]
    [TestCase("int32 Main() { { if (true) { { return 42; } } else { { return 0; } } } }")]
    [TestCase("int32 Main() { { int32 @const = 1; } { int32 const = 42; return @const; } }")]
    [TestCase("int32 Main() { { int32 @await = 1; } { int32 @await = 42; return @await; } }")]
    [TestCase("int32 Main() { mut int32 x = 40; { mut var y = x; y++; x = y + 1; } return x; }")]
    [TestCase("int32 Main() { mut int32 n = 0; while (n < 3) { { mut int32 t = 0; while (t < 2) { { t++; continue; } } n++; } } return n + 39; }")]
    [TestCase("int32 Main() { /*😀*/ { int32 名 = 1; } // close\n { int32 名 = 42; return 名; } }")]
    [TestCase("int32 Main() { { int32 int32 = 1; } { return int32(uint64(42)); } }")]
    [TestCase("int32 Main() { { uint64 x = 18446744073709551615; return x == 18446744073709551615 ? 42 : 0; } }")]
    [TestCase("int32 Main() { { int64 x = -9223372036854775808; return x == -9223372036854775808 ? 42 : 0; } }")]
    [TestCase("int32 Main() { ; ; { ; ; int32 x = 42; ; return x; ; } ; }")]
    [TestCase("int32 Main() { mut int32 n = 0; ; if (true) { ; ; n = 42; ; } else { ; n = 1; ; } ; return n; ; }")]
    [TestCase("int32 Main() { mut int32 n = 0; do { ; ; n++; ; } while (n < 2); ; for (; ; n++) { ; ; if (n == 42) { ; break; ; } ; } ; return n; ; }")]
    public async Task StandaloneBlocksMatchHostedScannerExecutableAndJitAsync(string source)
    {
        Assert.That(Lexer.Lex(SourceText.From(source)).Tokens.Length <= 64, Is.True);
        var root = RepositoryRoot();
        var frontend = Path.Combine(root, "self-host");
        var nativePath = CreateTestArtifactPath($"blocks-{Guid.NewGuid():N}.native.exe");
        var hostedPath = CreateTestArtifactPath($"blocks-{Guid.NewGuid():N}.hosted.exe");
        var scannerPath = CreateTestArtifactPath($"blocks-{Guid.NewGuid():N}.scanner.exe");
        try
        {
            foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
            {
                var input = Encoding.UTF8.GetBytes(source);
                var (componentExit, componentOutput, componentError) = await RunAsync(input,
                    "frontend-component", frontend, "-", "--runtime", policy, "--json");
                Assert.That(componentExit == 0, Is.True, $"{policy}: {componentOutput}{componentError}");
                using var document = JsonDocument.Parse(componentOutput);
                Assert.That(document.RootElement.GetProperty("result")
                    .GetProperty("projectionVersion").GetInt32(), Is.EqualTo(87));
                var (nativeExit, nativeOutput, nativeError) = await RunAsync(input,
                    "frontend-build", frontend, "-", "-o", nativePath, "--runtime", policy, "--json");
                var (hostedExit, hostedOutput, hostedError) = await RunAsync(input,
                    "build", "-", "-o", hostedPath, "--runtime", policy, "--json");
                Assert.That(nativeExit == 0, Is.True, $"{policy}: {nativeOutput}{nativeError}");
                Assert.That(hostedExit == 0, Is.True, $"{policy}: {hostedOutput}{hostedError}");
                Assert.That(await File.ReadAllBytesAsync(nativePath), Is.EqualTo(await File.ReadAllBytesAsync(hostedPath)));
                var (scannerExit, scannerOutput, scannerError) = await RunAsync(input,
                    "build-component", frontend, "-", "-o", scannerPath,
                    "--runtime", policy, "--json");
                Assert.That(scannerExit == 0, Is.True, $"{policy}: {scannerOutput}{scannerError}");
                Assert.That(await File.ReadAllBytesAsync(scannerPath), Is.EqualTo(await File.ReadAllBytesAsync(hostedPath)));
                Assert.That(await ExecuteNativeImageAsync(nativePath), Is.EqualTo(42));
                var (runExit, runOutput, runError) = await RunAsync(input,
                    "frontend-run", frontend, "-", "--runtime", policy, "--json");
                var (jitExit, jitOutput, jitError) = await RunAsync(input,
                    "jit", "-", "--runtime", policy, "--json");
                Assert.That(runExit == 42, Is.True, $"{policy}: {runOutput}{runError}");
                Assert.That(jitExit == 42, Is.True, $"{policy}: {jitOutput}{jitError}");
            }
        }
        finally
        {
            await TestFileCleanup.DeleteWithRetryAsync(nativePath).ConfigureAwait(true);
            File.Delete(hostedPath);
            File.Delete(scannerPath);
        }
    }
    [TestCase("int32 Main() { { int32 x = 1; } return x; }")]
    [TestCase("int32 Main() { { int32 x = 1; } { return x; } }")]
    [TestCase("int32 Main() { { mut int32 x = 1; } { x++; } return 42; }")]
    [TestCase("int32 Main() { { mut int32 x = 1; } x = 42; return 42; }")]
    [TestCase("int32 Main() { mut int32 x = 1; { int32 x = 42; } return x; }")]
    [TestCase("int32 Main(int32 x) { { int32 x = 42; } return x; }")]
    [TestCase("int32 Main() { { var x = x; } return 42; }")]
    [TestCase("int32 Main() { { var x = y; var y = 42; } return 42; }")]
    [TestCase("int32 Main() { { int32 x = 1; x++; } return 42; }")]
    [TestCase("int32 Main() { { int32 @const = 1; int32 const = 42; } return 42; }")]
    [TestCase("int32 Main() { { break; } return 42; }")]
    [TestCase("int32 Main() { { continue; } return 42; }")]
    [TestCase("int32 Main() { { while (true) { { break; } } } }")]
    [TestCase("int32 Main() { { } }")]
    [TestCase("int32 Main() { { return Missing(); } }")]
    public async Task StandaloneBlocksRejectInvalidOrUnsupportedSourceAtomicallyAsync(string source)
    {
        var root = RepositoryRoot();
        var path = CreateTestArtifactPath($"blocks-{Guid.NewGuid():N}.exe");
        try
        {
            foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
            {
                var (initial, _, _) = await RunAsync(Encoding.UTF8.GetBytes("int32 Main() { { return 42; } }"),
                    "frontend-build", Path.Combine(root, "self-host"), "-", "-o", path,
                    "--runtime", policy, "--json");
                Assert.That(initial, Is.EqualTo(0));
                var original = await File.ReadAllBytesAsync(path);
                var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(source),
                    "frontend-build", Path.Combine(root, "self-host"), "-", "-o", path,
                    "--runtime", policy, "--json");
                Assert.That(exit == 1, Is.True, $"{policy}: {exit}: {output}{error}");
                Assert.That(await File.ReadAllBytesAsync(path), Is.EqualTo(original));
            }
        }
        finally
        {
            File.Delete(path);
        }
    }
    [TestCase("int32 Main() { { int32 x = 1 / 0; return x; } }",unchecked((int)0xE0540002))]
    [TestCase("int32 Main() { { mut int32 x = 2147483647; x++; return x; } }",unchecked((int)0xE0540001))]
    public async Task StandaloneBlocksPreserveCheckedFailureStatusAsync(string source, int expected)
    {
        var root = RepositoryRoot();
        var nativePath = CreateTestArtifactPath($"blocks-{Guid.NewGuid():N}.native.exe");
        var hostedPath = CreateTestArtifactPath($"blocks-{Guid.NewGuid():N}.hosted.exe");
        try
        {
            foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
            {
                var input = Encoding.UTF8.GetBytes(source);
                var (nativeExit, nativeOutput, nativeError) = await RunAsync(input,
                    "frontend-build", Path.Combine(root, "self-host"), "-", "-o", nativePath,
                    "--runtime", policy, "--json");
                var (hostedExit, hostedOutput, hostedError) = await RunAsync(input,
                    "build", "-", "-o", hostedPath, "--runtime", policy, "--json");
                Assert.That(nativeExit == 0, Is.True, $"{policy}: {nativeOutput}{nativeError}");
                Assert.That(hostedExit == 0, Is.True, $"{policy}: {hostedOutput}{hostedError}");
                Assert.That(await File.ReadAllBytesAsync(nativePath), Is.EqualTo(await File.ReadAllBytesAsync(hostedPath)));
                Assert.That(await ExecuteNativeImageAsync(nativePath), Is.EqualTo(expected));
                Assert.That(await ExecuteNativeImageAsync(hostedPath), Is.EqualTo(expected));
            }
        }
        finally
        {
            await TestFileCleanup.DeleteWithRetryAsync(nativePath).ConfigureAwait(true);
            await TestFileCleanup.DeleteWithRetryAsync(hostedPath).ConfigureAwait(true);
        }
    }
    [TestCase("int32","42",42)]
    [TestCase("uint32","42",42)]
    [TestCase("int64","42",42)]
    [TestCase("uint64","42",42)]
    [TestCase("char16","42",42)]
    [TestCase("bool","true",1)]
    public async Task StandaloneBlocksExportAllSixScalarWidthsAsync(string type, string value, int expected)
    {
        var root = RepositoryRoot();
        var nativePath = CreateTestArtifactPath($"blocks-{Guid.NewGuid():N}.native.dll");
        var hostedPath = CreateTestArtifactPath($"blocks-{Guid.NewGuid():N}.hosted.dll");
        try
        {
            var input = Encoding.UTF8.GetBytes($"public {type} Value() {{ {{ {type} x = {value}; return x; }} }}");
            foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
            {
                var (nativeExit, nativeOutput, nativeError) = await RunAsync(input,
                    "frontend-build-library", Path.Combine(root, "self-host"), "-", "-o",
                    nativePath, "--runtime", policy, "--json");
                var (hostedExit, hostedOutput, hostedError) = await RunAsync(input,
                    "build-library", "-", "--export", "Value", "-o", hostedPath,
                    "--runtime", policy, "--json");
                Assert.That(nativeExit == 0, Is.True, $"{type}: {nativeOutput}{nativeError}");
                Assert.That(hostedExit == 0, Is.True, $"{type}: {hostedOutput}{hostedError}");
                Assert.That(await File.ReadAllBytesAsync(nativePath), Is.EqualTo(await File.ReadAllBytesAsync(hostedPath)));
                var handle = NativeLibrary.Load(nativePath);
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
            File.Delete(nativePath);
            File.Delete(hostedPath);
        }
    }
    [TestCase(6,true)]
    [TestCase(7,false)]
    public async Task StandaloneBlocksShareTheExistingSixLevelStructuralDepthAsync(int depth, bool valid)
        => await CheckBlockBoundaryAsync("int32 Main() { " + new string('{', depth)
            + " return 42; " + new string('}', depth) + " }", valid);
    [TestCase(11,true)]
    [TestCase(12,false)]
    public async Task StandaloneBlocksCountAgainstTwelveStatementRecordsAsync(int blocks, bool valid)
        => await CheckBlockBoundaryAsync("int32 Main() { "
            + string.Concat(Enumerable.Repeat("{} ", blocks)) + "return 42; }", valid);
    [TestCase(8,true)]
    [TestCase(9,false)]
    public async Task StandaloneBlocksCountEightTotalDeclarationIdentitiesAsync(int locals, bool valid)
    {
        var first = string.Concat(Enumerable.Range(0, 4).Select(i => $"int32 x{i} = {i}; "));
        var second = string.Concat(Enumerable.Range(0, locals - 4).Select(i => $"int32 x{i} = {i}; "));
        await CheckBlockBoundaryAsync($"int32 Main() {{ {{ {first} }} {{ {second} }} return 42; }}", valid);
    }
    [TestCase("for (;;) { { } }",0)]
    [TestCase("{ for (;;) { } }",0)]
    [TestCase("do { { continue; } } while (true);",1)]
    [TestCase("for (;;) { { continue; } }",0)]
    public async Task StandaloneBlocksNonFallthroughBuildWithoutExecutingAsync(string body, int nodes)
    {
        var root = RepositoryRoot();
        var nativePath = CreateTestArtifactPath($"blocks-{Guid.NewGuid():N}.native.exe");
        var hostedPath = CreateTestArtifactPath($"blocks-{Guid.NewGuid():N}.hosted.exe");
        try
        {
            var input = Encoding.UTF8.GetBytes($"int32 Main() {{ {body} }}");
            foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
            {
                var (exit, output, error) = await RunAsync(input, "frontend-build",
                    Path.Combine(root, "self-host"), "-", "-o", nativePath, "--runtime", policy, "--json");
                Assert.That(exit == 0, Is.True, $"{policy}: {output}{error}");
                using var document = JsonDocument.Parse(output);
                var result = document.RootElement.GetProperty("result");
                Assert.That(result.GetProperty("projectionVersion").GetInt32(), Is.EqualTo(87));
                Assert.That(result.GetProperty("nodes").GetArrayLength(), Is.EqualTo(nodes));
                Assert.That(global::System.Linq.Enumerable.Any(result.GetProperty("statements").EnumerateArray(),statement => statement.GetProperty("kind").GetInt32() == 8),Is.False);
                var (hostedExit, hostedOutput, hostedError) = await RunAsync(input,
                    "build", "-", "-o", hostedPath, "--runtime", policy, "--json");
                Assert.That(hostedExit == 0, Is.True, $"{policy}: {hostedOutput}{hostedError}");
                Assert.That(await File.ReadAllBytesAsync(nativePath), Is.EqualTo(await File.ReadAllBytesAsync(hostedPath)));
            }
        }
        finally
        {
            File.Delete(nativePath);
            File.Delete(hostedPath);
        }
    }
    [TestCase(64,false)]
    [TestCase(65,false)]
    [TestCase(82,false)]
    [TestCase(90,true)]
    public async Task StandaloneBlocksAtAndBeyondPreviousWindowMatchHostedExecutablesAndJitAsync(
        int count, bool mutableLocals)
    {
        var mutability = mutableLocals ? "mut " : string.Empty;
        var declarations = string.Concat(Enumerable.Range(0, 8)
            .Select(i => $"{mutability}int32 x{i} = {i}; "));
        var expression = count switch {
            64 => "((((((42))))))",
            65 => "+((((((42))))))",
            82 or 90 => string.Concat(new string('(', 15), "42", new string(')', 15)),
            _ => throw new ArgumentOutOfRangeException(nameof(count)),
        };
        var source = $"int32 Main() {{ {{ {declarations}return {expression}; }} }}";
        Assert.That(Lexer.Lex(SourceText.From(source)).Tokens.Length, Is.EqualTo(count));
        var root = RepositoryRoot();
        var frontend = Path.Combine(root, "self-host");
        var nativePath = CreateTestArtifactPath($"blocks-window-{Guid.NewGuid():N}.native.exe");
        var hostedPath = CreateTestArtifactPath($"blocks-window-{Guid.NewGuid():N}.hosted.exe");
        var scannerPath = CreateTestArtifactPath($"blocks-window-{Guid.NewGuid():N}.scanner.exe");
        try
        {
            foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
            {
                var input = Encoding.UTF8.GetBytes(source);
                var (componentExit, componentOutput, componentError) = await RunAsync(input,
                    "frontend-component", frontend, "-", "--runtime", policy, "--json");
                Assert.That(componentExit == 0, Is.True, $"{policy}: {componentOutput}{componentError}");
                using var document = JsonDocument.Parse(componentOutput);
                Assert.That(document.RootElement.GetProperty("result")
                    .GetProperty("projectionVersion").GetInt32(), Is.EqualTo(87));
                var (nativeExit, nativeOutput, nativeError) = await RunAsync(input,
                    "frontend-build", frontend, "-", "-o", nativePath, "--runtime", policy, "--json");
                var (hostedExit, hostedOutput, hostedError) = await RunAsync(input,
                    "build", "-", "-o", hostedPath, "--runtime", policy, "--json");
                Assert.That(nativeExit == 0, Is.True, $"{policy}: {nativeOutput}{nativeError}");
                Assert.That(hostedExit == 0, Is.True, $"{policy}: {hostedOutput}{hostedError}");
                Assert.That(await File.ReadAllBytesAsync(nativePath), Is.EqualTo(await File.ReadAllBytesAsync(hostedPath)));
                var (scannerExit, scannerOutput, scannerError) = await RunAsync(input,
                    "build-component", frontend, "-", "-o", scannerPath,
                    "--runtime", policy, "--json");
                Assert.That(scannerExit == 0, Is.True, $"{policy}: {scannerOutput}{scannerError}");
                Assert.That(await File.ReadAllBytesAsync(scannerPath), Is.EqualTo(await File.ReadAllBytesAsync(hostedPath)));
                Assert.That(await ExecuteNativeImageAsync(nativePath), Is.EqualTo(42));
                var (runExit, runOutput, runError) = await RunAsync(input,
                    "frontend-run", frontend, "-", "--runtime", policy, "--json");
                var (jitExit, jitOutput, jitError) = await RunAsync(input,
                    "jit", "-", "--runtime", policy, "--json");
                Assert.That(runExit == 42, Is.True, $"{policy}: {runOutput}{runError}");
                Assert.That(jitExit == 42, Is.True, $"{policy}: {jitOutput}{jitError}");
            }
        }
        finally
        {
            await TestFileCleanup.DeleteWithRetryAsync(nativePath).ConfigureAwait(true);
            File.Delete(hostedPath);
            File.Delete(scannerPath);
        }
    }
    [TestCase(32,true)]
    [TestCase(33,true)]
    public async Task StandaloneBlocksAcceptBeyondPreviousNodeLimitAsync(int nodes, bool valid)
    {
        var left = string.Join(" + ", Enumerable.Repeat("1", 7));
        var right = string.Join(" + ", Enumerable.Repeat("1", 6)) + " + 29";
        var parentheses = nodes - 29;
        var expression = new string('(', parentheses) + $"({left}) + ({right})" + new string(')', parentheses);
        await CheckBlockBoundaryAsync($"int32 Main() {{ {{ return {expression}; }} }}",
            valid, nodes: nodes);
    }
    [TestCase(16384,true)]
    [TestCase(16385,false)]
    public async Task StandaloneBlocksRetainTheExactSourceBudgetAsync(int length, bool valid)
    {
        const string prefix = "int32 Main() { { return 42; } } /*";
        var source = prefix + new string('x', length - prefix.Length - 2) + "*/";
        Assert.That(source.Length, Is.EqualTo(length));
        if (valid)
        {
            await CheckBlockBoundaryAsync(source, true);
        }
        else
        {
            foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
            {
                var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(source),
                    "frontend-component", Path.Combine(RepositoryRoot(), "self-host"), "-",
                    "--runtime", policy, "--json");
                Assert.That(exit, Is.EqualTo(2));
                Assert.That(error, Is.Empty);
                using var document = JsonDocument.Parse(output);
                Assert.That(document.RootElement.GetProperty("diagnostics")[0]
                    .GetProperty("code").GetString(), Is.EqualTo("TRM2001"));
            }
        }
    }

    [Test]
    public async Task StandaloneBlocksPreserveThreeParameterExportIdentityAsync()
    {
        var root = RepositoryRoot();
        var nativePath = CreateTestArtifactPath($"blocks-{Guid.NewGuid():N}.native.dll");
        var hostedPath = CreateTestArtifactPath($"blocks-{Guid.NewGuid():N}.hosted.dll");
        try
        {
            var input = Encoding.UTF8.GetBytes(
                "public int32 @await(int32 @const, int32 some, int32 c) { { int32 x = const + @some; return x + c; } }");
            foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
            {
                var (nativeExit, nativeOutput, nativeError) = await RunAsync(input,
                    "frontend-build-library", Path.Combine(root, "self-host"), "-", "-o",
                    nativePath, "--runtime", policy, "--json");
                var (hostedExit, hostedOutput, hostedError) = await RunAsync(input,
                    "build-library", "-", "--export", "await", "-o", hostedPath,
                    "--runtime", policy, "--json");
                Assert.That(nativeExit == 0, Is.True, $"{nativeOutput}{nativeError}");
                Assert.That(hostedExit == 0, Is.True, $"{hostedOutput}{hostedError}");
                Assert.That(await File.ReadAllBytesAsync(nativePath), Is.EqualTo(await File.ReadAllBytesAsync(hostedPath)));
                var handle = NativeLibrary.Load(nativePath);
                try
                {
                    var invoke = Marshal.GetDelegateForFunctionPointer<DoForThreeScalars>(
                        NativeLibrary.GetExport(handle, "await"));
                    Assert.That(invoke(1, 20, 21), Is.EqualTo(42));
                }
                finally
                {
                    NativeLibrary.Free(handle);
                }
            }
        }
        finally
        {
            File.Delete(nativePath);
            File.Delete(hostedPath);
        }
    }

    private static async Task CheckBlockBoundaryAsync(string source, bool valid, int? nodes = null)
    {
        Assert.That(Lexer.Lex(SourceText.From(source)).Tokens.Length <= 64, Is.True);
        foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
        {
            var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(source),
                "frontend-component", Path.Combine(RepositoryRoot(), "self-host"), "-",
                "--runtime", policy, "--json");
            Assert.That(exit == (valid ? 0 : 1), Is.True, $"{policy}: {output}{error}");
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
}
