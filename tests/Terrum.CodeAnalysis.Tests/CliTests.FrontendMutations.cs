// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed partial class CliTests
{
    [Theory]
    [InlineData("int32")]
    [InlineData("uint32")]
    [InlineData("int64")]
    [InlineData("uint64")]
    public async Task FrontendAllIntegerCompoundsAndShiftsExecuteAcrossPoliciesAsync(string type)
    {
        var frontend = Path.Combine(RepositoryRoot(), "self-host");
        var cases = new (string Operator, int Operand, int Expected)[] {
            ("+=", 4, 44), ("-=", 2, 38), ("*=", 2, 80),
            ("/=", 2, 20), ("%=", 6, 4), ("&=", 7, 0),
            ("|=", 3, 43), ("^=", 3, 43), ("<<=", 1, 80),
            (">>=", 1, 20), (">>>=", 1, 20),
            ("<<", 1, 80), (">>", 1, 20), (">>>", 1, 20),
        };
        foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
        {
            foreach (var (operation, operand, expected) in cases)
            {
                var compound = operation.EndsWith('=');
                var body = compound
                    ? $"x {operation} {operand}; return x == {expected} ? 42 : 1;"
                    : $"return (x {operation} {operand}) == {expected} ? 42 : 1;";
                var input = Encoding.UTF8.GetBytes($"int32 Main() {{ mut {type} x = 40; {body} }}");
                var (projectionExit, projectionOutput, projectionError) = await RunAsync(input,
                    "frontend-component", frontend, "-", "--runtime", policy, "--json");
                Assert.True(projectionExit == 0,
                    $"{type} {operation} {policy}: {projectionExit}: {projectionOutput}{projectionError}");
                Assert.Empty(projectionError);
                using var document = JsonDocument.Parse(projectionOutput);
                Assert.Equal(49, document.RootElement.GetProperty("result")
                    .GetProperty("projectionVersion").GetInt32());
                var (runExit, runOutput, runError) = await RunAsync(input,
                    "frontend-run", frontend, "-", "--runtime", policy, "--json");
                Assert.True(runExit == 42,
                    $"{type} {operation} {policy}: {runExit}: {runOutput}{runError}");
                Assert.Empty(runOutput);
                Assert.Empty(runError);
            }
        }
    }

    [Theory]
    [InlineData("int32")]
    [InlineData("uint32")]
    [InlineData("int64")]
    [InlineData("uint64")]
    public async Task FrontendEveryIntegerUpdateFormMatchesHostedLibraryAsync(string type)
    {
        var root = RepositoryRoot();
        var nativePath = Path.Combine(root, $"mutations-{Guid.NewGuid():N}.native.dll");
        var hostedPath = Path.Combine(root, $"mutations-{Guid.NewGuid():N}.hosted.dll");
        try
        {
            foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
            {
                foreach (var (expression, old, current) in new[] {
                    ("x++", 4, 5), ("++x", 5, 5),
                    ("x--", 4, 3), ("--x", 3, 3),
                })
                {
                    var input = Encoding.UTF8.GetBytes(
                        $"public {type} Value() {{ mut {type} x = 4; var old = {expression};"
                        + $" return old == {old} && x == {current} ? x : 0; }}");
                    var (nativeExit, nativeOutput, nativeError) = await RunAsync(input,
                        "frontend-build-library", Path.Combine(root, "self-host"), "-", "-o",
                        nativePath, "--runtime", policy, "--json");
                    var (hostedExit, hostedOutput, hostedError) = await RunAsync(input,
                        "build-library", "-", "--export", "Value", "-o",
                        hostedPath, "--runtime", policy, "--json");
                    Assert.True(nativeExit == 0,
                        $"{type} {expression} {policy}: {nativeExit}: {nativeOutput}{nativeError}");
                    Assert.True(hostedExit == 0,
                        $"{type} {expression} {policy}: {hostedExit}: {hostedOutput}{hostedError}");
                    Assert.Empty(nativeError);
                    Assert.Empty(hostedError);
                    Assert.Equal(await File.ReadAllBytesAsync(hostedPath),
                        await File.ReadAllBytesAsync(nativePath));
                    var handle = NativeLibrary.Load(nativePath);
                    try
                    {
                        var address = NativeLibrary.GetExport(handle, "Value");
                        var actual = type switch {
                            "int32" => Marshal.GetDelegateForFunctionPointer<MutationInt32>(address)(),
                            "uint32" => Marshal.GetDelegateForFunctionPointer<MutationUInt32>(address)(),
                            "int64" => Marshal.GetDelegateForFunctionPointer<MutationInt64>(address)(),
                            "uint64" => (long)Marshal.GetDelegateForFunctionPointer<MutationUInt64>(address)(),
                            _ => throw new InvalidOperationException("Unexpected scalar type."),
                        };
                        Assert.Equal(current, actual);
                    }
                    finally
                    {
                        NativeLibrary.Free(handle);
                    }
                }
            }
        }
        finally
        {
            File.Delete(nativePath);
            File.Delete(hostedPath);
        }
    }

    [Theory]
    [InlineData("int32")]
    [InlineData("uint32")]
    [InlineData("int64")]
    [InlineData("uint64")]
    public async Task FrontendShiftExpressionBodyMatchesHostedLibraryAsync(string type)
    {
        var root = RepositoryRoot();
        var nativePath = Path.Combine(root, $"mutations-{Guid.NewGuid():N}.native.dll");
        var hostedPath = Path.Combine(root, $"mutations-{Guid.NewGuid():N}.hosted.dll");
        try
        {
            foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
            {
                var input = Encoding.UTF8.GetBytes(
                    $"public {type} Value({type} x, int32 count) => x >>> count;");
                var (projectionExit, projectionOutput, projectionError) = await RunAsync(input,
                    "frontend-component", Path.Combine(root, "self-host"), "-",
                    "--runtime", policy, "--json");
                Assert.True(projectionExit == 0,
                    $"{type} {policy}: {projectionExit}: {projectionOutput}{projectionError}");
                Assert.Empty(projectionError);
                using var document = JsonDocument.Parse(projectionOutput);
                var result = document.RootElement.GetProperty("result");
                Assert.Equal(49, result.GetProperty("projectionVersion").GetInt32());
                Assert.True(result.GetProperty("expressionBody").GetBoolean());
                var (nativeExit, nativeOutput, nativeError) = await RunAsync(input,
                    "frontend-build-library", Path.Combine(root, "self-host"), "-", "-o",
                    nativePath, "--runtime", policy, "--json");
                var (hostedExit, hostedOutput, hostedError) = await RunAsync(input,
                    "build-library", "-", "--export", "Value", "-o",
                    hostedPath, "--runtime", policy, "--json");
                Assert.True(nativeExit == 0, $"{type} {policy}: {nativeOutput}{nativeError}");
                Assert.True(hostedExit == 0, $"{type} {policy}: {hostedOutput}{hostedError}");
                Assert.Empty(nativeError);
                Assert.Empty(hostedError);
                Assert.Equal(await File.ReadAllBytesAsync(hostedPath),
                    await File.ReadAllBytesAsync(nativePath));
            }
        }
        finally
        {
            File.Delete(nativePath);
            File.Delete(hostedPath);
        }
    }

    [Theory]
    [InlineData("int32 Main() { mut int32 x = 4; return ++x + x--; }", 10)]
    [InlineData("int32 Main() { mut int32 x = 4; return x++ + --x; }", 8)]
    [InlineData("int32 Main() { mut int32 x = 4; x += x++; return x; }", 8)]
    [InlineData("int32 Main() { mut int32 x = 4; (x) += x++; return x; }", 8)]
    [InlineData("int32 Main() { mut int32 x = 2; ((x)) <<= x++; return x; }", 8)]
    [InlineData("int32 Main() { mut int32 x = 0; while (x < 2) { (x) += 1; } return x; }", 2)]
    [InlineData("int32 Main() { mut int32 x = 4; x += (x++ + x); return x; }", 13)]
    [InlineData("int32 Main() { mut int32 x = 4; x += ++x * 2; return x; }", 14)]
    [InlineData("int32 Main() { mut int32 x = 4; x += (true ? x++ : x / 0); return x; }", 8)]
    [InlineData("int32 Main() { mut int32 x = 4; x += (false ? x / 0 : x++); return x; }", 8)]
    [InlineData("int32 Main() { mut int32 x = 2; x <<= x++; return x; }", 8)]
    [InlineData("int32 Main() { mut int32 x = 4; x -= x--; return x; }", 0)]
    [InlineData("int32 Main() { mut var x = int32(4); x++; return x; }", 5)]
    [InlineData("int32 Main() { mut int32 x = 4; ++x; x--; return x; }", 4)]
    [InlineData("int32 Main() { mut int32 x = 4; (x)++; return ++(x); }", 6)]
    [InlineData("int32 Main() { mut int32 x = 4; return true ? x++ + x : 1 / 0; }", 9)]
    [InlineData("int32 Main() { mut int32 x = 4; return false && x++ == 4 ? 0 : x; }", 4)]
    [InlineData("int32 Main() { mut int32 x = 4; return false ? x / 0 : x++ + x; }", 9)]
    [InlineData("int32 Main() { mut int32 x = 4; return true || x++ == 0 ? x : 0; }", 4)]
    [InlineData("int32 Main() { mut int32 x = 1; x <<= 31; return x >> 31 == -1 && x >>> 31 == 1 ? 42 : 0; }", 42)]
    [InlineData("int32 Main() { mut int32 x = 1; return x << 31 == -2147483648 ? 42 : 0; }", 42)]
    [InlineData("int32 Main() { mut int64 x = -1; return x >>> 63 == 1 ? 42 : 0; }", 42)]
    [InlineData("int32 Main() { mut int64 x = 1; x <<= 63; return x < 0 ? 42 : 0; }", 42)]
    [InlineData("int32 Main() { mut uint64 x = 1; return x << 63 == 9223372036854775808 ? 42 : 0; }", 42)]
    [InlineData("int32 Main() { mut uint64 x = 1; return x << 32 == 4294967296 ? 42 : 0; }", 42)]
    [InlineData("int32 Main() { mut uint32 x = 4294967295; return x >> 31 == 1 && x >>> 31 == 1 ? 42 : 0; }", 42)]
    [InlineData("int32 Main() { mut uint64 x = 18446744073709551615; return x >> 63 == 1 && x >>> 63 == 1 ? 42 : 0; }", 42)]
    [InlineData("int32 Main() { mut int32 x = 0; while (x < 2) { x += 1; if (x == 1) { continue; } return x + 40; } return 0; }", 42)]
    [InlineData("int32 Main() { mut int32 x = 0; while (x < 3) { while (true) { x++; break; } if (x == 2) { continue; } if (x == 3) { return 42; } } return 0; }", 42)]
    public async Task FrontendUpdateSnapshotsAndControlFlowMatchHostedAsync(string source, int expected)
    {
        var root = RepositoryRoot();
        var frontend = Path.Combine(root, "self-host");
        var nativePath = Path.Combine(root, $"mutations-{Guid.NewGuid():N}.native.exe");
        var hostedPath = Path.Combine(root, $"mutations-{Guid.NewGuid():N}.hosted.exe");
        try
        {
            foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
            {
                var input = Encoding.UTF8.GetBytes(source);
                var (nativeExit, nativeOutput, nativeError) = await RunAsync(input,
                    "frontend-build", frontend, "-", "-o", nativePath, "--runtime", policy, "--json");
                var (hostedExit, hostedOutput, hostedError) = await RunAsync(input,
                    "build", "-", "-o", hostedPath, "--runtime", policy, "--json");
                Assert.True(nativeExit == 0,
                    $"frontend-build {policy}: {nativeExit}: {nativeOutput}{nativeError}");
                Assert.True(hostedExit == 0,
                    $"build {policy}: {hostedExit}: {hostedOutput}{hostedError}");
                Assert.Empty(nativeError);
                Assert.Empty(hostedError);
                Assert.Equal(await File.ReadAllBytesAsync(hostedPath), await File.ReadAllBytesAsync(nativePath));
                Assert.Equal(expected, await ExecuteNativeImageAsync(nativePath));
                var (runExit, runOutput, runError) = await RunAsync(input,
                    "frontend-run", frontend, "-", "--runtime", policy, "--json");
                Assert.True(runExit == expected,
                    $"frontend-run {policy}: {runExit}: {runOutput}{runError}");
                Assert.Empty(runOutput);
                Assert.Empty(runError);
            }
        }
        finally
        {
            File.Delete(nativePath);
            File.Delete(hostedPath);
        }
    }

    [Theory]
    [InlineData("int32", "2147483647", "x++", unchecked((int)0xE0540001))]
    [InlineData("int32", "-2147483648", "--x", unchecked((int)0xE0540001))]
    [InlineData("uint32", "0", "x--", unchecked((int)0xE0540001))]
    [InlineData("uint64", "0", "--x", unchecked((int)0xE0540001))]
    [InlineData("uint32", "4294967295", "x += 1;", unchecked((int)0xE0540001))]
    [InlineData("int64", "9223372036854775807", "++x", unchecked((int)0xE0540001))]
    [InlineData("uint64", "18446744073709551615", "x *= 2;", unchecked((int)0xE0540001))]
    [InlineData("int32", "1", "x /= 0;", unchecked((int)0xE0540002))]
    [InlineData("int64", "-9223372036854775808", "x /= -1;", unchecked((int)0xE0540001))]
    [InlineData("int64", "-9223372036854775808", "x %= -1;", 42)]
    [InlineData("int32", "-2147483648", "x %= -1;", 42)]
    [InlineData("int32", "1", "x <<= -1;", unchecked((int)0xE0540001))]
    [InlineData("int32", "1", "x >>>= 32;", unchecked((int)0xE0540001))]
    [InlineData("uint32", "1", "x >>= -1;", unchecked((int)0xE0540001))]
    [InlineData("int64", "1", "x >>= 64;", unchecked((int)0xE0540001))]
    [InlineData("uint64", "1", "x >>>= 64;", unchecked((int)0xE0540001))]
    [InlineData("int32", "1", "return x << -1;", unchecked((int)0xE0540001))]
    [InlineData("int64", "1", "return int32(x >>> 64);", unchecked((int)0xE0540001))]
    public async Task FrontendMutationsPreserveExactCheckedStatusesAsync(
        string type, string initial, string mutation, int expected)
    {
        var root = RepositoryRoot();
        var path = Path.Combine(root, $"mutations-{Guid.NewGuid():N}.exe");
        try
        {
            foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
            {
                var statement = mutation.EndsWith(';')
                    ? mutation : $"return {mutation} == {initial} ? 1 : 42;";
                if (mutation.EndsWith(';'))
                {
                    statement += " return 42;";
                }
                var input = Encoding.UTF8.GetBytes(
                    $"int32 Main() {{ mut {type} x = {initial}; {statement} }}");
                var (exit, output, error) = await RunAsync(input,
                    "frontend-build", Path.Combine(root, "self-host"), "-", "-o", path,
                    "--runtime", policy, "--json");
                Assert.True(exit == 0, $"{type} {mutation} {policy}: {exit}: {output}{error}");
                Assert.Empty(error);
                Assert.Equal(expected, await ExecuteNativeImageAsync(path));
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData("int32 Main() { int32 x = 1; x++; return x; }")]
    [InlineData("int32 Main(int32 x) { ++x; return x; }")]
    [InlineData("int32 Main() { mut bool x = true; x++; return 42; }")]
    [InlineData("int32 Main() { mut char16 x = 1; x += 1; return 42; }")]
    [InlineData("int32 Main() { mut int32 x = 1; x <<= int64(1); return x; }")]
    [InlineData("int32 Main() { mut int64 x = 1; return int32(x << int64(1)); }")]
    [InlineData("int32 Main() { mut int32 x = 1; x += uint32(1); return x; }")]
    [InlineData("int32 Main() { mut int32 x = 1; return x++ + Missing(); }")]
    [InlineData("int32 Main() { mut int32 x = 1; return x.Value++; }")]
    [InlineData("int32 Main() { mut int32 x = 1; return x / 0 + unknown++; }")]
    [InlineData("int32 Main() { mut int32 x = 1; x += 1.5; return x; }")]
    [InlineData("int32 Main() { mut int32 x = 1; return (x + 1)++; }")]
    [InlineData("int32 Main() { mut int32 x = 1; x += int64(2); return x; }")]
    [InlineData("int32 Main() { mut int32 x = 1; return x >>> uint32(1); }")]
    [InlineData("int32 Main() { var x = 1 << 2; return 42; }")]
    [InlineData("int32 Main() { mut int32 count = 2; var x = 1 << count; return x; }")]
    [InlineData("int32 Main() { mut int32 x = 1; (x + 1) += 2; return x; }")]
    [InlineData("int32 Main() { int32 x = 1; (x) += 2; return x; }")]
    [InlineData("int32 Main() { mut int32 x = 4; ((x++)); (++x); return x; }")]
    public async Task FrontendInvalidMutationRetainsOutputAsync(string source)
    {
        var root = RepositoryRoot();
        var path = Path.Combine(root, $"mutations-{Guid.NewGuid():N}.exe");
        try
        {
            foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
            {
                var (initial, initialOutput, initialError) = await RunAsync(
                    Encoding.UTF8.GetBytes("int32 Main() { mut int32 x = 1; return x++; }"),
                    "frontend-build", Path.Combine(root, "self-host"), "-", "-o", path,
                    "--runtime", policy, "--json");
                Assert.True(initial == 0, $"{initial}: {initialOutput}{initialError}");
                Assert.Empty(initialError);
                var original = await File.ReadAllBytesAsync(path);
                var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(source),
                    "frontend-build", Path.Combine(root, "self-host"), "-", "-o", path,
                    "--runtime", policy, "--json");
                Assert.True(exit == 1, $"{source}: {exit}: {output}{error}");
                Assert.Empty(error);
                using var document = JsonDocument.Parse(output);
                Assert.False(document.RootElement.GetProperty("success").GetBoolean());
                Assert.Equal(original, await File.ReadAllBytesAsync(path));
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData("int32 Main() { int32 x = 1; return x++; }", 2, "x++")]
    [InlineData("int32 Main() { mut int32 x = 1; x += uint32(1); return x; }", 2, "uint32(1)")]
    [InlineData("int32 Main() { mut int32 x = 1; x <<= int64(1); return x; }", 2, "int64(1)")]
    [InlineData("int32 Main() { mut int32 x = 1; x += Missing; return x; }", 3, "Missing")]
    public async Task FrontendMutationErrorsRetainDiagnosticKindAndSpanAsync(
        string source, int expectedCode, string offending)
    {
        var frontend = Path.Combine(RepositoryRoot(), "self-host");
        foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
        {
            var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(source),
                "frontend-component", frontend, "-", "--runtime", policy, "--json");
            Assert.True(exit == 1, $"{source} {policy}: {exit}: {output}{error}");
            Assert.Empty(error);
            using var document = JsonDocument.Parse(output);
            var result = document.RootElement.GetProperty("result");
            Assert.False(result.GetProperty("valid").GetBoolean());
            Assert.Equal(expectedCode, result.GetProperty("errorCode").GetInt32());
            var span = result.GetProperty("errorSpan");
            var start = span.GetProperty("start").GetInt32();
            Assert.Equal(source.LastIndexOf(offending, StringComparison.Ordinal), start);
            Assert.True(span.GetProperty("length").GetInt32() > 0);
        }
    }

    [Fact]
    public async Task FrontendMutationsKeepStatementAndNodeEnvelopesAsync()
    {
        var frontend = Path.Combine(RepositoryRoot(), "self-host");
        foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
        {
            var withinStatements = "int32 Main() { mut int32 x = 0; "
                + string.Concat(Enumerable.Repeat("x++; ", 11)) + "return x; }";
            var beyondStatements = "int32 Main() { mut int32 x = 0; "
                + string.Concat(Enumerable.Repeat("x++; ", 12)) + "return x; }";
            var withinNodes = "int32 Main() { mut int32 x = 0; return +("
                + string.Join(" + ", Enumerable.Repeat("x++", 10)) + "); }";
            var beyondNodes = "int32 Main() { mut int32 x = 0; return "
                + string.Join(" + ", Enumerable.Repeat("x++", 11)) + "; }";
            foreach (var source in new[] { withinStatements, withinNodes })
            {
                var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(source),
                    "frontend-component", frontend, "-", "--runtime", policy, "--json");
                Assert.True(exit == 0, $"{source}: {exit}: {output}{error}");
                Assert.Empty(error);
                using var document = JsonDocument.Parse(output);
                var result = document.RootElement.GetProperty("result");
                Assert.Equal(49, result.GetProperty("projectionVersion").GetInt32());
                Assert.Equal(source == withinNodes ? 32 : 12,
                    result.GetProperty(source == withinNodes ? "nodes" : "statements").GetArrayLength());
            }
            foreach (var source in new[] { beyondStatements, beyondNodes })
            {
                var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(source),
                    "frontend-component", frontend, "-", "--runtime", policy, "--json");
                Assert.True(exit == 1, $"{source}: {exit}: {output}{error}");
                Assert.Empty(error);
                using var document = JsonDocument.Parse(output);
                Assert.False(document.RootElement.GetProperty("success").GetBoolean());
            }
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int MutationInt32();

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate uint MutationUInt32();

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate long MutationInt64();

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate ulong MutationUInt64();

    private static string RepositoryRoot() => Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
}
