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
    [InlineData("int32 Main() { mut int32 n = 40; do { n++; } while (false); return n + 1; }")]
    [InlineData("int32 Main() { mut int32 n = 38; do { n++; } while (++n < 42); return n; }")]
    [InlineData("int32 Main() { mut int32 n = 40; do { n++; continue; } while (++n < 42); return n; }")]
    [InlineData("int32 Main() { mut int32 n = 40; do { n++; break; } while (n++ < 50); return n + 1; }")]
    [InlineData("int32 Main() { mut int32 n = 40; do { return n + 2; } while (n++ < 50); }")]
    [InlineData("int32 Main() { mut int32 n = 39; for (mut int32 i = 0; i < 3; i++) { n++; } return n; }")]
    [InlineData("int32 Main() { mut int32 n = 39; for (n = 40; n < 42; n++) { } return n; }")]
    [InlineData("int32 Main() { mut int32 n = 39; for (n += 1; n < 42; n += 1) { } return n; }")]
    [InlineData("int32 Main() { mut int32 n = 40; for (n++; n < 42; ++n) { } return n; }")]
    [InlineData("int32 Main() { mut int32 n = 40; for (; n < 42; n++) { } return n; }")]
    [InlineData("int32 Main() { mut int32 n = 40; for (; ; n++) { if (n == 42) { break; } } return n; }")]
    [InlineData("int32 Main() { mut int32 n = 40; for (; n < 42;) { n++; } return n; }")]
    [InlineData("int32 Main() { for (;;) { return 42; } }")]
    [InlineData("int32 Main() { mut int32 n = 40; for (; n < 41; n++) { n++; continue; } return n; }")]
    [InlineData("int32 Main() { mut int32 n = 40; for (; n < 43; n++) { n++; break; } return n + 1; }")]
    [InlineData("int32 Main() { mut int32 n = 40; for (;; n++) { return n + 2; } }")]
    [InlineData("int32 Main() { mut int32 n = 40; for (mut int32 i = 0; i < 2; i++) { int32 x = 1; n += x; } return n; }")]
    [InlineData("int32 Main() { mut int32 n = 40; do { int32 x = 2; n += x; } while (false); return n; }")]
    [InlineData("int32 Main() { do { int32 x = 0; } while (false); int32 x = 42; return x; }")]
    [InlineData("int32 Main() { for (int32 x = 0; false;) { } int32 x = 42; return x; }")]
    [InlineData("int32 Main() { mut int32 n = 40; for (; n < 42; n++) { do { n++; continue; } while (false); } return n; }")]
    [InlineData("int32 Main() { mut int32 n = 40; do { for (;;) { n++; break; } n++; break; } while (false); return n; }")]
    [InlineData("int32 Main() { mut int32 n = 40; for (; n < 41; n++) { do { n++; break; } while (true); continue; } return n; }")]
    [InlineData("int32 Main() { mut int32 n = 39; for (; ++n < 42;) { } return n; }")]
    [InlineData("int32 Main() { for (mut int32 i = 0; i < 1; i++) { } for (mut int32 i = 0; i < 1; i++) { } return 42; }")]
    [InlineData("int32 Main() { int32 n = 1; for (; false && n / 0 == 0;) { return 0; } return 42; }")]
    [InlineData("int32 Main() { mut int32 n = 40; while (n < 41) { do { n++; continue; } while (false); } return n + 1; }")]
    [InlineData("int32 Main() { mut int32 n = 40; do { int32 x = 1; n += x; } while (n < 42); return n; }")]
    [InlineData("int32 Main() { int32 a = 1; int32 b = 1; mut int32 c = 40; do { c += a + b; } while (false); return c; }")]
    [InlineData("int32 Main() { do { if (false) { continue; } return 42; } while (false); }")]
    [InlineData("int32 Main() { mut int32 n = 0; for (mut var i = n; i < 3; i++) { mut int32 x = 1; n += x++; } return n + 39; }")]
    [InlineData("int32 Main() { mut int32 n = 42; for (; false; n /= 0) { n = 0; } return n; }")]
    [InlineData("int32 Main() { int32 n = 42; do { break; } while (n / 0 == 0); return n; }")]
    [InlineData("int32 Main() { int32 n = 42; do { return n; } while (n / 0 == 0); }")]
    [InlineData("int32 Main() { for (mut bool flag = true; flag; flag = false) { } return 42; }")]
    [InlineData("int32 Main() { for (char16 code = 97; false;) { } return 42; }")]
    [InlineData("int32 Main() { mut uint32 n = 4294967293; for (; n < 4294967295; n++) { } return n == 4294967295 ? 42 : 0; }")]
    [InlineData("int32 Main() { mut int64 n = -9223372036854775808; for (; n < -9223372036854775806; n++) { } return n == -9223372036854775806 ? 42 : 0; }")]
    [InlineData("int32 Main() { mut uint64 n = 18446744073709551613; for (; n < 18446744073709551615; n++) { } return n == 18446744073709551615 ? 42 : 0; }")]
    [InlineData("int32 Main() { mut int32 n = 4; for (n += n++; false;) { } return n + 34; }")]
    public async Task NativeDoForMatchesHostedAotAndJitAsync(string source)
    {
        var root = RepositoryRoot();
        var frontend = Path.Combine(root, "self-host");
        var nativePath = Path.Combine(root, $"do-for-{Guid.NewGuid():N}.native.exe");
        var hostedPath = Path.Combine(root, $"do-for-{Guid.NewGuid():N}.hosted.exe");
        try
        {
            var input = Encoding.UTF8.GetBytes(source);
            Assert.True(Lexer.Lex(SourceText.From(source)).Tokens.Length <= 64, source);
            foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
            {
                var (projectionExit, projectionOutput, projectionError) = await RunAsync(input,
                    "frontend-component", frontend, "-", "--runtime", policy, "--json");
                Assert.True(projectionExit == 0, $"{policy}: {source}: {projectionOutput}{projectionError}");
                using var projection = JsonDocument.Parse(projectionOutput);
                Assert.Equal(51, projection.RootElement.GetProperty("result")
                    .GetProperty("projectionVersion").GetInt32());
                var (nativeExit, nativeOutput, nativeError) = await RunAsync(input,
                    "frontend-build", frontend, "-", "-o", nativePath, "--runtime", policy, "--json");
                var (hostedExit, hostedOutput, hostedError) = await RunAsync(input,
                    "build", "-", "-o", hostedPath, "--runtime", policy, "--json");
                Assert.True(nativeExit == 0, $"{policy}: {source}: {nativeOutput}{nativeError}");
                Assert.True(hostedExit == 0, $"{policy}: {source}: {hostedOutput}{hostedError}");
                Assert.Equal(await File.ReadAllBytesAsync(hostedPath), await File.ReadAllBytesAsync(nativePath));
                Assert.Equal(42, await ExecuteNativeImageAsync(nativePath));
                var (jitExit, jitOutput, jitError) = await RunAsync(input,
                    "frontend-run", frontend, "-", "--runtime", policy, "--json");
                Assert.True(jitExit == 42, $"{policy}: {source}: {jitExit}: {jitOutput}{jitError}");
            }
        }
        finally
        {
            File.Delete(nativePath);
            File.Delete(hostedPath);
        }
    }

    [Theory]
    [InlineData("+=", 40, 2)]
    [InlineData("-=", 44, 2)]
    [InlineData("*=", 21, 2)]
    [InlineData("/=", 84, 2)]
    [InlineData("%=", 85, 43)]
    [InlineData("&=", 63, 42)]
    [InlineData("|=", 40, 2)]
    [InlineData("^=", 40, 2)]
    [InlineData("<<=", 21, 1)]
    [InlineData(">>=", 84, 1)]
    [InlineData(">>>=", 84, 1)]
    public async Task NativeDoForCompoundsWorkInBothHeaderClausesAsync(string operation, int initial, int operand)
    {
        await NativeDoForMatchesHostedAotAndJitAsync(
            $"int32 Main() {{ mut int32 n = {initial}; for (n {operation} {operand}; false;) {{ }} return n; }}");
        await NativeDoForMatchesHostedAotAndJitAsync(
            $"int32 Main() {{ mut int32 n = {initial}; for (; n != 42; n {operation} {operand}) {{ }} return n; }}");
    }

    [Theory]
    [InlineData("int32 Main() { do { int32 x = 1 / 0; return x; } while (false); }", unchecked((int)0xE0540002))]
    [InlineData("int32 Main() { for (mut int32 x = 2147483647; true; x++) { if (x < 0) { return 42; } continue; } }", unchecked((int)0xE0540001))]
    [InlineData("int32 Main() { mut int32 x = 0; for (x = 1 / 0; false;) { } return 42; }", unchecked((int)0xE0540002))]
    [InlineData("int32 Main() { int32 x = 1; do { continue; } while (x / 0 == 0); return 42; }", unchecked((int)0xE0540002))]
    public async Task NativeLoopsRetainCheckedFailureStatusAsync(string source, int expected)
    {
        var root = RepositoryRoot();
        var nativePath = Path.Combine(root, $"do-for-{Guid.NewGuid():N}.native.exe");
        var hostedPath = Path.Combine(root, $"do-for-{Guid.NewGuid():N}.hosted.exe");
        try
        {
            var input = Encoding.UTF8.GetBytes(source);
            foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
            {
                var (nativeExit, nativeOutput, nativeError) = await RunAsync(input,
                    "frontend-build", Path.Combine(root, "self-host"), "-", "-o", nativePath,
                    "--runtime", policy, "--json");
                var (hostedExit, hostedOutput, hostedError) = await RunAsync(input,
                    "build", "-", "-o", hostedPath, "--runtime", policy, "--json");
                Assert.True(nativeExit == 0, $"{policy}: {nativeOutput}{nativeError}");
                Assert.True(hostedExit == 0, $"{policy}: {hostedOutput}{hostedError}");
                Assert.Equal(await File.ReadAllBytesAsync(hostedPath), await File.ReadAllBytesAsync(nativePath));
                Assert.Equal(expected, await ExecuteNativeImageAsync(nativePath));
            }
        }
        finally
        {
            File.Delete(nativePath);
            File.Delete(hostedPath);
        }
    }

    [Theory]
    [InlineData("int32 Main() { for (int32 x = 0; false;) { } return x; }")]
    [InlineData("int32 Main() { do { int32 x = 1; } while (x == 1); return 42; }")]
    [InlineData("int32 Main() { for (; false; x++) { int32 x = 1; } return 42; }")]
    [InlineData("int32 Main() { for (int32 x = 1; x < 2; x++) { } return 42; }")]
    [InlineData("int32 Main() { for (mut int32 x = 1; x < 2; x++) { int32 x = 2; } return 42; }")]
    [InlineData("int32 Main() { mut int32 x = 1; for (int32 x = 1; false;) { } return 42; }")]
    [InlineData("int32 Main() { for (mut int32 x = 0; x < 2; x++) { int32 y = x; } return y; }")]
    [InlineData("int32 Main() { for (mut int32 x = 0; x < 2; x++) { } return x; }")]
    [InlineData("int32 Main() { for (var x = true; x; x++) { } return 42; }")]
    [InlineData("int32 Main() { do { break; } while (1); return 42; }")]
    [InlineData("int32 Main() { for (var x = 1;;) { break; } return x; }")]
    [InlineData("int32 Main() { mut int32 x = 0; for (;; (x++)) { break; } return 42; }")]
    [InlineData("int32 Main() { do { int32 x = 0; } while (false); return x; }")]
    [InlineData("int32 Main() { for (init var x; false;) { } return 42; }")]
    [InlineData("int32 Main() { do { break label; } while (false); return 42; }")]
    [InlineData("int32 Main() { do { } while (false); }")]
    [InlineData("int32 Main() { for (; false;) { } }")]
    [InlineData("int32 Main() { for (;; Value()) { return 42; } }")]
    [InlineData("int32 Main(bool flag) { do { if (flag) { continue; } else { return 42; } } while (false); }")]
    public async Task NativeLoopUnsupportedShapesRejectWithoutReplacingOutputAsync(string source)
    {
        var root = RepositoryRoot();
        var path = Path.Combine(root, $"do-for-{Guid.NewGuid():N}.exe");
        try
        {
            foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
            {
                var (initial, _, _) = await RunAsync(
                    Encoding.UTF8.GetBytes("int32 Main() { return 42; }"),
                    "frontend-build", Path.Combine(root, "self-host"), "-", "-o", path,
                    "--runtime", policy, "--json");
                Assert.Equal(0, initial);
                var original = await File.ReadAllBytesAsync(path);
                var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(source),
                    "frontend-build", Path.Combine(root, "self-host"), "-", "-o", path,
                    "--runtime", policy, "--json");
                Assert.True(exit == 1, $"{policy}: {source}: {exit}: {output}{error}");
                using var diagnostic = JsonDocument.Parse(output);
                Assert.InRange(diagnostic.RootElement.GetProperty("result")
                    .GetProperty("errorCode").GetInt32(), 1, 4);
                Assert.Equal(original, await File.ReadAllBytesAsync(path));
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData("int32", "42", 42)]
    [InlineData("uint32", "42", 42)]
    [InlineData("int64", "42", 42)]
    [InlineData("uint64", "42", 42)]
    [InlineData("char16", "42", 42)]
    [InlineData("bool", "true", 1)]
    public async Task NativeDoForExportAllSixScalarWidthsAsync(string type, string value, int expected)
    {
        var root = RepositoryRoot();
        var nativePath = Path.Combine(root, $"do-for-{Guid.NewGuid():N}.native.dll");
        var hostedPath = Path.Combine(root, $"do-for-{Guid.NewGuid():N}.hosted.dll");
        try
        {
            var source = $"public {type} Value() {{ for (;;) {{ do {{ return {value}; }} while (false); }} }}";
            var input = Encoding.UTF8.GetBytes(source);
            foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
            {
                var (nativeExit, nativeOutput, nativeError) = await RunAsync(input,
                    "frontend-build-library", Path.Combine(root, "self-host"), "-", "-o",
                    nativePath, "--runtime", policy, "--json");
                var (hostedExit, hostedOutput, hostedError) = await RunAsync(input,
                    "build-library", "-", "--export", "Value", "-o", hostedPath,
                    "--runtime", policy, "--json");
                Assert.True(nativeExit == 0, $"{type}: {nativeOutput}{nativeError}");
                Assert.True(hostedExit == 0, $"{type}: {hostedOutput}{hostedError}");
                Assert.Equal(await File.ReadAllBytesAsync(hostedPath), await File.ReadAllBytesAsync(nativePath));
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
            File.Delete(nativePath);
            File.Delete(hostedPath);
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int DoForThreeScalars(int a, int b, int c);

    [Theory]
    [InlineData("for (;;) { }", 0)]
    [InlineData("do { } while (true);", 1)]
    public async Task NativeDoForNonFallthroughNeedsNoSyntheticReturnAsync(string body, int nodes)
    {
        var root = RepositoryRoot();
        var nativePath = Path.Combine(root, $"do-for-{Guid.NewGuid():N}.native.exe");
        var hostedPath = Path.Combine(root, $"do-for-{Guid.NewGuid():N}.hosted.exe");
        try
        {
            var input = Encoding.UTF8.GetBytes($"int32 Main() {{ {body} }}");
            foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
            {
                var (exit, output, error) = await RunAsync(input, "frontend-build",
                    Path.Combine(root, "self-host"), "-", "-o", nativePath, "--runtime", policy, "--json");
                Assert.True(exit == 0, $"{policy}: {output}{error}");
                using var projection = JsonDocument.Parse(output);
                var result = projection.RootElement.GetProperty("result");
                Assert.Equal(51, result.GetProperty("projectionVersion").GetInt32());
                Assert.Equal(nodes, result.GetProperty("nodes").GetArrayLength());
                Assert.DoesNotContain(result.GetProperty("statements").EnumerateArray(),
                    statement => statement.GetProperty("kind").GetInt32() == 8);
                var (hostedExit, hostedOutput, hostedError) = await RunAsync(input,
                    "build", "-", "-o", hostedPath, "--runtime", policy, "--json");
                Assert.True(hostedExit == 0, $"{policy}: {hostedOutput}{hostedError}");
                Assert.Equal(await File.ReadAllBytesAsync(hostedPath), await File.ReadAllBytesAsync(nativePath));
            }
        }
        finally
        {
            File.Delete(nativePath);
            File.Delete(hostedPath);
        }
    }

    [Fact]
    public async Task NativeDoRetainsThreeParameterAbiAsync()
    {
        const string source = "public int32 Value(int32 a, int32 b, int32 c) { do { return a + b + c; } while (false); }";
        var root = RepositoryRoot();
        var output = Path.Combine(root, $"do-for-{Guid.NewGuid():N}.dll");
        try
        {
            foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
            {
                var (exit, json, error) = await RunAsync(Encoding.UTF8.GetBytes(source),
                    "frontend-build-library", Path.Combine(root, "self-host"), "-", "-o",
                    output, "--runtime", policy, "--json");
                Assert.True(exit == 0, $"{policy}: {json}{error}");
                var handle = NativeLibrary.Load(output);
                try
                {
                    var value = Marshal.GetDelegateForFunctionPointer<DoForThreeScalars>(
                        NativeLibrary.GetExport(handle, "Value"));
                    Assert.Equal(42, value(1, 2, 39));
                }
                finally
                {
                    NativeLibrary.Free(handle);
                }
            }
        }
        finally
        {
            File.Delete(output);
        }
    }

    [Fact]
    public async Task NativeLoopBoundsStayFixedAsync()
    {
        var frontend = Path.Combine(RepositoryRoot(), "self-host");
        var sixLevels = "int32 Main() { "
            + string.Concat(Enumerable.Repeat("do { ", 6))
            + "return 42; "
            + string.Concat(Enumerable.Repeat("} while (false); ", 6)) + "}";
        var sevenLevels = "int32 Main() { "
            + string.Concat(Enumerable.Repeat("while (true) { ", 7))
            + "return 42; " + string.Concat(Enumerable.Repeat("} ", 7)) + "}";
        var sixForLevels = "int32 Main() { for (;;) { for (;;) { "
            + string.Concat(Enumerable.Repeat("while (true) { ", 4))
            + "return 42; " + string.Concat(Enumerable.Repeat("} ", 6)) + "}";
        var sevenForLevels = "int32 Main() { for (;;) { for (;;) { "
            + string.Concat(Enumerable.Repeat("while (true) { ", 5))
            + "return 42; " + string.Concat(Enumerable.Repeat("} ", 7)) + "}";
        var eightLocals = "int32 Main() { do { "
            + string.Concat(Enumerable.Range(0, 8).Select(i => $"bool x{i} = true; "))
            + "} while (false); return 42; }";
        var nineLocals = eightLocals.Replace("} while (false);",
            "bool x8 = true; } while (false);", StringComparison.Ordinal);
        var thirtyThreeNodes = "int32 Main() { do { return "
            + "+" + string.Join(" + ", Enumerable.Repeat("1", 16)) + "; } while (false); }";
        var thirtyTwoNodes = thirtyThreeNodes.Replace("return +", "return ", StringComparison.Ordinal);
        var fifteenGroups = "int32 Main() { do { return "
            + new string('(', 15) + "42" + new string(')', 15)
            + "; } while (false); }";
        var sixteenGroups = fifteenGroups.Replace("42", "(42)", StringComparison.Ordinal);
        var thirteenStatements = "int32 Main() { "
            + string.Concat(Enumerable.Repeat("if (false) { return 0; } ", 6))
            + "return 42; }";
        var twelveStatements = "int32 Main() { "
            + string.Concat(Enumerable.Repeat("if (false) { return 0; } ", 5))
            + "do { } while (false); return 42; }";
        var twelveForStatements = "int32 Main() { "
            + string.Concat(Enumerable.Repeat("for (; false;) { } ", 3))
            + "do { } while (false); do { } while (false); return 42; }";
        var thirteenForStatements = "int32 Main() { "
            + string.Concat(Enumerable.Repeat("for (; false;) { } ", 4))
            + "return 42; }";
        var tooManyTokens = "int32 Main() { "
            + string.Concat(Enumerable.Repeat("do { } while (false); ", 10))
            + "return 42; }";
        Assert.True(Lexer.Lex(SourceText.From(sevenLevels)).Tokens.Length <= 64);
        Assert.True(Lexer.Lex(SourceText.From(nineLocals)).Tokens.Length <= 64);
        Assert.True(Lexer.Lex(SourceText.From(thirteenStatements)).Tokens.Length <= 64);
        Assert.True(Lexer.Lex(SourceText.From(twelveStatements)).Tokens.Length <= 64);
        Assert.True(Lexer.Lex(SourceText.From(sevenForLevels)).Tokens.Length <= 64);
        Assert.True(Lexer.Lex(SourceText.From(thirteenForStatements)).Tokens.Length <= 64);
        Assert.True(Lexer.Lex(SourceText.From(tooManyTokens)).Tokens.Length > 64);
        foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
        {
            foreach (var valid in new[] {
                sixLevels, sixForLevels, eightLocals, twelveStatements, twelveForStatements,
                fifteenGroups, thirtyTwoNodes,
            })
            {
                var (validExit, validOutput, _) = await RunAsync(Encoding.UTF8.GetBytes(valid),
                    "frontend-component", frontend, "-", "--runtime", policy, "--json");
                Assert.True(validExit == 0, $"{valid}: {validOutput}");
                if (valid == thirtyTwoNodes)
                {
                    using var projection = JsonDocument.Parse(validOutput);
                    Assert.Equal(32, projection.RootElement.GetProperty("result")
                        .GetProperty("nodes").GetArrayLength());
                }
                if (valid == twelveForStatements)
                {
                    using var projection = JsonDocument.Parse(validOutput);
                    Assert.Equal(12, projection.RootElement.GetProperty("result")
                        .GetProperty("statements").GetArrayLength());
                }
            }
            foreach (var source in new[] {
                sevenLevels, sevenForLevels, nineLocals, thirtyThreeNodes, sixteenGroups,
                thirteenStatements, thirteenForStatements, tooManyTokens,
            })
            {
                var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(source),
                    "frontend-component", frontend, "-", "--runtime", policy, "--json");
                Assert.True(exit == 1, $"{policy}: {source}: {output}{error}");
                using var result = JsonDocument.Parse(output);
                Assert.Equal(1, result.RootElement.GetProperty("result")
                    .GetProperty("errorCode").GetInt32());
            }
        }
    }
}
