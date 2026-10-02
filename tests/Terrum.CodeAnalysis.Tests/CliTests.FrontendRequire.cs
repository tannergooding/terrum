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
    [InlineData("require true; return 42;", 42)]
    [InlineData("require false; return 42;", unchecked((int)0xE0540003))]
    [InlineData("require int32(-1) < int32(42); return 42;", 42)]
    [InlineData("require uint32(4294967295) > uint32(42); return 42;", 42)]
    [InlineData("require int64(-9223372036854775808) < int64(42); return 42;", 42)]
    [InlineData("require uint64(18446744073709551615) > uint64(42); return 42;", 42)]
    [InlineData("require char16(65535) != char16(42); return 42;", 42)]
    [InlineData("bool b = true; require b == true; return 42;", 42)]
    [InlineData("mut int32 x = 40; require ++x == 41; return x + 1;", 42)]
    [InlineData("mut int32 x = 40; require x++ == 40; return x + 1;", 42)]
    [InlineData("mut int32 x = 40; require x++ + ++x == 82; return x;", 42)]
    [InlineData("mut int32 x = 41; require true || ++x == 0; return x + 1;", 42)]
    [InlineData("mut int32 x = 41; require !(false && ++x == 0); return x + 1;", 42)]
    [InlineData("mut int32 x = 40; require true ? ++x == 41 : ++x == 0; return x + 1;", 42)]
    [InlineData("mut int32 x = 40; require false ? ++x == 0 : ++x == 41; return x + 1;", 42)]
    [InlineData("if (true) { require true; return 42; } else { require false; return 0; }", 42)]
    [InlineData("mut int32 x = 0; while (x < 2) { require ++x <= 2; } return x + 40;", 42)]
    [InlineData("mut int32 x = 0; for (; x < 2; x++) { do { require true; } while (false); } return x + 40;", 42)]
    [InlineData("{ _ = int32(1); require true; } return 42;", 42)]
    [InlineData("mut int32 x = 0; @const: do { { _ = x++; require x == 1; while (true) { break const; } } } while (false); return x + 41;", 42)]
    [InlineData("/*\U0001F600*/ mut int32 名 = 41; require ++名 == 42; return 名;", 42)]
    [InlineData("mut int32 @require = 41; require ++@require == 42; return @require;", 42)]
    [InlineData("require false && int32(1) / int32(0) == int32(0); return 42;", unchecked((int)0xE0540003))]
    [InlineData("require int32(1) / int32(0) == int32(0); return 42;", unchecked((int)0xE0540002))]
    [InlineData("require int32(2147483647) + int32(1) == int32(0); return 42;", unchecked((int)0xE0540001))]
    [InlineData("require int32(uint64(4294967296)) == int32(0); return 42;", unchecked((int)0xE0540001))]
    public async Task NativeScalarRequireMatchesHostedScannerPeAndJitAsync(string body, int expected)
    {
        var source = $"int32 Main() {{ {body} }}";
        Assert.True(Lexer.Lex(SourceText.From(source)).Tokens.Length <= 64);
        var root = RepositoryRoot();
        var native = Path.Combine(root, $"require-{Guid.NewGuid():N}.native.exe");
        var hosted = Path.Combine(root, $"require-{Guid.NewGuid():N}.hosted.exe");
        var scanner = Path.Combine(root, $"require-{Guid.NewGuid():N}.scanner.exe");
        try
        {
            foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
            {
                var input = Encoding.UTF8.GetBytes(source);
                var (exit, output, error) = await RunAsync(input, "frontend-build",
                    Path.Combine(root, "self-host"), "-", "-o", native, "--runtime", policy, "--json");
                Assert.True(exit == 0, $"{policy}: {output}{error}");
                using var document = JsonDocument.Parse(output);
                Assert.Equal(55, document.RootElement.GetProperty("result").GetProperty("projectionVersion").GetInt32());
                var (hostExit, hostOutput, hostError) = await RunAsync(input, "build",
                    "-", "-o", hosted, "--runtime", policy, "--json");
                Assert.True(hostExit == 0, $"{hostOutput}{hostError}");
                var (scanExit, scanOutput, scanError) = await RunAsync(input, "build-component",
                    Path.Combine(root, "self-host", "Scanner.terrum"), "-", "-o", scanner,
                    "--runtime", policy, "--json");
                Assert.True(scanExit == 0, $"{scanOutput}{scanError}");
                Assert.Equal(await File.ReadAllBytesAsync(hosted), await File.ReadAllBytesAsync(native));
                Assert.Equal(await File.ReadAllBytesAsync(hosted), await File.ReadAllBytesAsync(scanner));
                Assert.Equal(expected, await ExecuteNativeImageAsync(native));
                var (runExit, runOutput, runError) = await RunAsync(input, "frontend-run",
                    Path.Combine(root, "self-host"), "-", "--runtime", policy, "--json");
                Assert.True(runExit == expected, $"{policy}: {runOutput}{runError}");
                var (jitExit, jitOutput, jitError) = await RunAsync(input, "jit",
                    "-", "--runtime", policy, "--json");
                Assert.True(jitExit == expected, $"{policy}: {jitOutput}{jitError}");
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
    [InlineData("static require true; return 42;")]
    [InlineData("const bool proven = true; static require proven; return 42;")]
    [InlineData("static require true || false; return 42;")]
    [InlineData("static require true ? true : false; return 42;")]
    [InlineData("static require true ? true : int32(1) / int32(0) == 0; return 42;")]
    [InlineData("bool value = false; static require true || value; return 42;")]
    [InlineData("static require true || int32(1) / int32(0) == 0; return 42;")]
    [InlineData("static ensure true && (int32(42) == int32(42)); return 42;")]
    [InlineData("static ensure false ? false : true; return 42;")]
    public async Task NativeScalarStaticProofsMatchHostedScannerPeAndJitAsync(string body)
    {
        var source = $"int32 Main() {{ {body} }}";
        var root = RepositoryRoot();
        var native = Path.Combine(root, $"static-proof-{Guid.NewGuid():N}.native.exe");
        var hosted = Path.Combine(root, $"static-proof-{Guid.NewGuid():N}.hosted.exe");
        var scanner = Path.Combine(root, $"static-proof-{Guid.NewGuid():N}.scanner.exe");
        try
        {
            foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
            {
                var input = Encoding.UTF8.GetBytes(source);
                var (exit, output, error) = await RunAsync(input, "frontend-build",
                    Path.Combine(root, "self-host"), "-", "-o", native, "--runtime", policy, "--json");
                Assert.True(exit == 0, $"{policy}: {output}{error}");
                using var document = JsonDocument.Parse(output);
                Assert.Equal(68, document.RootElement.GetProperty("result")
                    .GetProperty("projectionVersion").GetInt32());

                var (hostExit, hostOutput, hostError) = await RunAsync(input, "build",
                    "-", "-o", hosted, "--runtime", policy, "--json");
                Assert.True(hostExit == 0, $"{hostOutput}{hostError}");
                var (scanExit, scanOutput, scanError) = await RunAsync(input, "build-component",
                    Path.Combine(root, "self-host", "Scanner.terrum"), "-", "-o", scanner,
                    "--runtime", policy, "--json");
                Assert.True(scanExit == 0, $"{scanOutput}{scanError}");
                Assert.Equal(await File.ReadAllBytesAsync(hosted), await File.ReadAllBytesAsync(native));
                Assert.Equal(await File.ReadAllBytesAsync(hosted), await File.ReadAllBytesAsync(scanner));
                Assert.Equal(42, await ExecuteNativeImageAsync(native));

                var (jitExit, jitOutput, jitError) = await RunAsync(input, "jit",
                    "-", "--runtime", policy, "--json");
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
    [InlineData("require 1; return 42;")]
    [InlineData("require int32(1); return 42;")]
    [InlineData("require null; return 42;")]
    [InlineData("require (); return 42;")]
    [InlineData("require Missing(); return 42;")]
    [InlineData("require 1 == 1; return 42;")]
    [InlineData("require true, 7; return 42;")]
    [InlineData("require true else 7; return 42;")]
    [InlineData("static require false; return 42;")]
    [InlineData("static ensure false; return 42;")]
    [InlineData("bool value = true; static require value; return 42;")]
    [InlineData("bool value = true; static require false || value; return 42;")]
    [InlineData("bool value = false; static ensure true || value; return 42;")]
    [InlineData("const bool value = true; static ensure value; return 42;")]
    [InlineData("if (true) { static ensure true; } return 42;")]
    [InlineData("for (require true; false;) {} return 42;")]
    [InlineData("for (; false; require true) {} return 42;")]
    [InlineData("require true")]
    [InlineData("require true;")]
    [InlineData("bool b = false; require b;")]
    [InlineData("require int32(1) == int32(2);")]
    [InlineData("int32 x = 42; { require true; } return missing;")]
    [InlineData("{ int32 x = 42; } require x == 42; return 42;")]
    [InlineData("_ = true; require _; return 42;")]
    [InlineData("require false; BadUnsupported(); return 42;")]
    [InlineData("require true; return 42; } int32 Unused() { require 1;")]
    public async Task NativeScalarRequireRejectsUnsupportedTypingContextsAndRetainsPriorPeAsync(string body)
    {
        var path = Path.Combine(RepositoryRoot(), $"require-{Guid.NewGuid():N}.exe");
        try
        {
            foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
            {
                var (initial, initialOutput, initialError) = await RunAsync(
                    Encoding.UTF8.GetBytes("int32 Main() { require true; return 42; }"),
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
    [InlineData("require false;")]
    [InlineData("require !true;")]
    [InlineData("require false && int32(1) / int32(0) == int32(0);")]
    [InlineData("require true ? false : true;")]
    [InlineData("for (;;) { require true; }")]
    [InlineData("a: for (;;) { require true; do { continue a; } while (false); }")]
    public async Task NativeScalarRequireNonFallthroughCompileOnlyAsync(string body)
    {
        var root = RepositoryRoot();
        var native = Path.Combine(root, $"require-{Guid.NewGuid():N}.native.exe");
        var hosted = Path.Combine(root, $"require-{Guid.NewGuid():N}.hosted.exe");
        try
        {
            foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
            {
                var source = Encoding.UTF8.GetBytes($"int32 Main() {{ {body} }}");
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
    [InlineData("int32", "int32(42)", 42)]
    [InlineData("uint32", "uint32(42)", 42)]
    [InlineData("int64", "int64(42)", 42)]
    [InlineData("uint64", "uint64(42)", 42)]
    [InlineData("char16", "char16(42)", 42)]
    [InlineData("bool", "true", 1)]
    public async Task NativeScalarRequirePreservesAllSixNormalizedLibraryExportsAsync(string type, string value, int expected)
    {
        var root = RepositoryRoot();
        var native = Path.Combine(root, $"require-{Guid.NewGuid():N}.native.dll");
        var hosted = Path.Combine(root, $"require-{Guid.NewGuid():N}.hosted.dll");
        try
        {
            var source = Encoding.UTF8.GetBytes($"public {type} @await() {{ require true; return {value}; }}");
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
    public async Task NativeScalarRequirePreservesThreeParameterAbiAsync()
    {
        var native = Path.Combine(RepositoryRoot(), $"require-{Guid.NewGuid():N}.dll");
        var hosted = Path.Combine(RepositoryRoot(), $"require-{Guid.NewGuid():N}.hosted.dll");
        try
        {
            var source = Encoding.UTF8.GetBytes("public int32 @await(int32 a, int32 b, int32 c) { require a + b + c == 42; return a + b + c; }");
            foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
            {
                var (exit, output, error) = await RunAsync(source, "frontend-build-library",
                    Path.Combine(RepositoryRoot(), "self-host"), "-", "-o", native, "--runtime", policy, "--json");
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
    public Task NativeScalarRequireDoesNotConsumeHiddenOrNamedLocalIdentityAsync(int count, bool valid)
        => CheckRequireBoundaryAsync("int32 Main() { "
            + string.Concat(Enumerable.Repeat("_ = true; ", count - 1))
            + "int32 x = 42; require true; return x; }", valid, locals: count);

    [Theory]
    [InlineData(6, true)]
    [InlineData(7, false)]
    public Task NativeScalarRequireDoesNotIntroduceSourceStructuralDepthAsync(int depth, bool valid)
        => CheckRequireBoundaryAsync("int32 Main() { " + new string('{', depth)
            + "require true; return 42;" + new string('}', depth) + " }", valid);

    [Theory]
    [InlineData(12, true)]
    [InlineData(13, false)]
    public Task NativeScalarRequireCountsAgainstStatementRecordsAsync(int count, bool valid)
        => CheckRequireBoundaryAsync("int32 Main() { "
            + string.Concat(Enumerable.Repeat("require true; ", count - 1)) + "return 42; }",
            valid, statements: count);

    [Theory]
    [InlineData(16, true)]
    [InlineData(17, false)]
    public Task NativeScalarRequireRetainsExpressionRecursionLimitAsync(int depth, bool valid)
        => CheckRequireBoundaryAsync("int32 Main() { require " + new string('(', depth - 1)
            + "true" + new string(')', depth - 1) + "; return 42; }", valid);

    [Theory]
    [InlineData(32, true)]
    [InlineData(33, false)]
    public Task NativeScalarRequireRetainsExactNodeLimitAsync(int count, bool valid)
        => CheckRequireBoundaryAsync("int32 Main() { require " + (count == 33 ? "!" : "")
            + string.Join(" && ", Enumerable.Repeat("true", 16)) + "; return 42; }", valid, nodes: count);

    [Theory]
    [InlineData(16384, true)]
    [InlineData(16385, false)]
    public async Task NativeScalarRequireRetainsExactUtf16LimitAsync(int length, bool valid)
    {
        const string prefix = "int32 Main() { require true; return 42; } /*";
        var source = prefix + new string('x', length - prefix.Length - 2) + "*/";
        Assert.Equal(length, source.Length);
        foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
        {
            var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(source), "frontend-component",
                Path.Combine(RepositoryRoot(), "self-host"), "-", "--runtime", policy, "--json");
            Assert.True(exit == (valid ? 0 : 2), $"{output}{error}");
        }
    }

    [Theory]
    [InlineData(64, true)]
    [InlineData(65, false)]
    public async Task NativeScalarRequireRetainsExactTokenLimitAsync(int count, bool valid)
    {
        var declarations = string.Concat(Enumerable.Range(0, 7).Select(i => $"int32 x{i} = {i}; "));
        const string expression = "+(((((((+42)))))))";
        var source = $"int32 Main() {{ {declarations}require {(valid ? "true" : "!true")}; return {expression}; }}";
        Assert.Equal(count, Lexer.Lex(SourceText.From(source)).Tokens.Length);
        await CheckRequireBoundaryAsync(source, valid, checkTokens: false);
    }

    private static async Task CheckRequireBoundaryAsync(string source, bool valid, bool checkTokens = true,
        int? locals = null, int? statements = null, int? nodes = null)
    {
        if (checkTokens) { Assert.True(Lexer.Lex(SourceText.From(source)).Tokens.Length <= 64); }
        foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
        {
            var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(source), "frontend-component",
                Path.Combine(RepositoryRoot(), "self-host"), "-", "--runtime", policy, "--json");
            Assert.True(exit == (valid ? 0 : 1), $"{output}{error}");
            if (valid)
            {
                using var document = JsonDocument.Parse(output);
                var result = document.RootElement.GetProperty("result");
                Assert.Equal(55, result.GetProperty("projectionVersion").GetInt32());
                if (locals is int localCount) { Assert.Equal(localCount, result.GetProperty("locals").GetArrayLength()); }
                if (statements is int statementCount) { Assert.Equal(statementCount, result.GetProperty("statements").GetArrayLength()); }
                if (nodes is int nodeCount) { Assert.Equal(nodeCount, result.GetProperty("nodes").GetArrayLength()); }
            }
        }
    }
}
