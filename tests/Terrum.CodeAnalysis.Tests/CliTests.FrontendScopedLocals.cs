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
    [InlineData("int32 Main() { if (true) { int32 x = 42; return x; } return 0; }", 42)]
    [InlineData("int32 Main() { if (true) { int32 x = 42; return x; } }", 42)]
    [InlineData("int32 Main() { if (false) { int32 x = 1; return x; } else { int32 x = 42; return x; } }", 42)]
    [InlineData("int32 Main() { mut int32 n = 0; while (n < 3) { mut var x = n; n += 1; if (n == 2) { continue; } if (n == 3) { break; } } return n + 39; }", 42)]
    [InlineData("int32 Main() { mut int32 n = 0; while (n < 3) { mut var x = n; x += x++; n += 1; } return n + 39; }", 42)]
    [InlineData("int32 Main() { mut int32 n = 0; mut int32 s = 0; while (n < 3) { mut int32 x = 1; s += x; n++; } return s + 39; }", 42)]
    [InlineData("int32 Main() { mut int32 n = 0; mut int32 s = 0; while (n < 3) { mut int32 x = 1; s += x++; n++; } return s + 39; }", 42)]
    [InlineData("int32 Main() { if (false) { int32 x = 1; } mut int32 x = 41; x++; return x; }", 42)]
    [InlineData("int32 Main() { while (false) { int32 x = 1 / 0; } return 42; }", 42)]
    [InlineData("int32 Main() { mut int32 x = 0; while (x < 2) { int32 y = x; x = y + 1; } return x + 40; }", 42)]
    [InlineData("int32 Main() { mut int32 x = 0; while (x < 2) { if (true) { mut int32 y = x; x = y + 1; } } return x + 40; }", 42)]
    [InlineData("int32 Main() { mut int32 n = 0; mut int32 s = 0; while (n < 3) { int32 x = 1; n++; if (n == 2) { continue; } s += x; } return s + 40; }", 42)]
    [InlineData("int32 Main() { if (true) { int32 x = 42; if (x == 42) { var y = x; return y; } } return 0; }", 42)]
    [InlineData("int32 Main() { mut int32 x = 0; if (true) { mut var y = x; y = 42; x = y; } return x; }", 42)]
    [InlineData("int32 Main() { if (false) { int32 x = 0; return x; } else { int32 x = 42; return x; } }", 42)]
    [InlineData("int32 Main() { if (false) { int32 int32 = 1; } return int32(uint64(42)); }", 42)]
    [InlineData("int32 Main() { if (true) { if (true) { if (true) { if (true) { if (true) { if (true) { int32 x = 42; return x; } } } } } } return 0; }", 42)]
    [InlineData("int32 Main() { if (false) { int32 a = 1; } else { int32 a = 2; } if (true) { int32 a = 3; } else { int32 a = 4; } return 42; }", 42)]
    public async Task ScopedLocalsMatchHostedExecutableAndJitAsync(string source, int expected)
    {
        var root = RepositoryRoot();
        var frontend = Path.Combine(root, "self-host");
        var nativePath = Path.Combine(root, $"scoped-{Guid.NewGuid():N}.native.exe");
        var hostedPath = Path.Combine(root, $"scoped-{Guid.NewGuid():N}.hosted.exe");
        try
        {
            foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
            {
                var input = Encoding.UTF8.GetBytes(source);
                var (componentExit, componentOutput, componentError) = await RunAsync(input,
                    "frontend-component", frontend, "-", "--runtime", policy, "--json");
                Assert.True(componentExit == 0,
                    $"{policy}: {componentExit}: {componentOutput}{componentError}");
                using var document = JsonDocument.Parse(componentOutput);
                Assert.Equal(50, document.RootElement.GetProperty("result")
                    .GetProperty("projectionVersion").GetInt32());
                var (nativeExit, nativeOutput, nativeError) = await RunAsync(input,
                    "frontend-build", frontend, "-", "-o", nativePath, "--runtime", policy, "--json");
                var (hostedExit, hostedOutput, hostedError) = await RunAsync(input,
                    "build", "-", "-o", hostedPath, "--runtime", policy, "--json");
                Assert.True(nativeExit == 0, $"{policy}: {nativeOutput}{nativeError}");
                Assert.True(hostedExit == 0, $"{policy}: {hostedOutput}{hostedError}");
                Assert.Equal(await File.ReadAllBytesAsync(hostedPath), await File.ReadAllBytesAsync(nativePath));
                Assert.Equal(expected, await ExecuteNativeImageAsync(nativePath));
                var (runExit, runOutput, runError) = await RunAsync(input,
                    "frontend-run", frontend, "-", "--runtime", policy, "--json");
                var (jitExit, jitOutput, jitError) = await RunAsync(input,
                    "jit", "-", "--runtime", policy, "--json");
                Assert.True(runExit == expected,
                    $"{policy}: {runExit}: {runOutput}{runError}");
                Assert.Equal(jitExit, runExit);
            }
        }
        finally
        {
            File.Delete(nativePath);
            File.Delete(hostedPath);
        }
    }

    [Theory]
    [InlineData("int32 Main() { if (true) { int32 x = 1 / 0; return x; } return 42; }", unchecked((int)0xE0540002))]
    [InlineData("int32 Main() { while (true) { mut int32 x = 2147483647; x++; break; } return 42; }", unchecked((int)0xE0540001))]
    public async Task ScopedLocalsPreserveCheckedFailureStatusAsync(string source, int expected)
    {
        var root = RepositoryRoot();
        var nativePath = Path.Combine(root, $"scoped-{Guid.NewGuid():N}.native.exe");
        var hostedPath = Path.Combine(root, $"scoped-{Guid.NewGuid():N}.hosted.exe");
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
                Assert.True(nativeExit == 0, $"{policy}: {nativeOutput}{nativeError}");
                Assert.True(hostedExit == 0, $"{policy}: {hostedOutput}{hostedError}");
                Assert.Equal(await File.ReadAllBytesAsync(hostedPath), await File.ReadAllBytesAsync(nativePath));
                Assert.Equal(expected, await ExecuteNativeImageAsync(nativePath));
                Assert.Equal(expected, await ExecuteNativeImageAsync(hostedPath));
            }
        }
        finally
        {
            File.Delete(nativePath);
            File.Delete(hostedPath);
        }
    }

    [Theory]
    [InlineData("int32 Main() { if (true) { var x = 1; } return x; }")]
    [InlineData("int32 Main() { if (true) { var x = 1; } else { return x; } return 42; }")]
    [InlineData("int32 Main() { if (true) { var x = x; } return 42; }")]
    [InlineData("int32 Main() { if (true) { int32 x = x; } return 42; }")]
    [InlineData("int32 Main() { if (true) { var x = y; var y = 2; } return 42; }")]
    [InlineData("int32 Main() { if (true) { int32 x = 1; x = 2; } return 42; }")]
    [InlineData("int32 Main() { if (true) { int32 x = 1; x++; } return 42; }")]
    [InlineData("int32 Main() { int32 x = 1; if (true) { int32 x = 2; } return x; }")]
    [InlineData("int32 Main(int32 x) { if (true) { int32 x = 2; } return x; }")]
    [InlineData("int32 Main() { if (true) { int32 x = 1; } while (false) { return x; } return 42; }")]
    [InlineData("int32 Main() { if (true) { int32 x = 1; } else { int32 y = x; } return 42; }")]
    [InlineData("int32 Main() { if (true) { int32 int32 = 1; return int32(uint64(42)); } return 0; }")]
    public async Task ScopedLocalsRejectInactiveOrDuplicateNamesAtomicallyAsync(string source)
    {
        var root = RepositoryRoot();
        var frontend = Path.Combine(root, "self-host");
        var path = Path.Combine(root, $"scoped-{Guid.NewGuid():N}.exe");
        try
        {
            foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
            {
                var (initial, _, _) = await RunAsync(
                    Encoding.UTF8.GetBytes("int32 Main() { return 42; }"),
                    "frontend-build", frontend, "-", "-o", path, "--runtime", policy, "--json");
                Assert.Equal(0, initial);
                var original = await File.ReadAllBytesAsync(path);
                var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(source),
                    "frontend-build", frontend, "-", "-o", path, "--runtime", policy, "--json");
                Assert.True(exit == 1, $"{policy}: {exit}: {output}{error}");
                Assert.Equal(original, await File.ReadAllBytesAsync(path));
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ScopedLocalIdentityBudgetRemainsEightAcrossScopesAsync()
    {
        var frontend = Path.Combine(RepositoryRoot(), "self-host");
        var first = string.Concat(Enumerable.Range(0, 4).Select(i => $"int32 x{i} = {i}; "));
        var eight = first;
        var nine = first + "int32 x4 = 4; ";
        foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
        {
            foreach (var (body, expected) in new[] { (eight, 8), (nine, -1) })
            {
                var source = $"int32 Main() {{ if (false) {{ {first} }} else {{ {body} }} return 42; }}";
                Assert.True(Lexer.Lex(SourceText.From(source)).Tokens.Length <= 64);
                var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(source),
                    "frontend-component", frontend, "-", "--runtime", policy, "--json");
                Assert.Equal(expected == -1 ? 1 : 0, exit);
                Assert.Empty(error);
                using var document = JsonDocument.Parse(output);
                if (expected != -1)
                {
                    var result = document.RootElement.GetProperty("result");
                    Assert.Equal(50, result.GetProperty("projectionVersion").GetInt32());
                    Assert.Equal(expected, result.GetProperty("locals").GetArrayLength());
                }
            }
        }
    }

    [Theory]
    [InlineData("int32", "42", 42)]
    [InlineData("uint32", "42", 42)]
    [InlineData("int64", "42", 42)]
    [InlineData("uint64", "42", 42)]
    [InlineData("char16", "42", 42)]
    [InlineData("bool", "true", 1)]
    public async Task ScopedSixScalarsExportAndExecuteAsync(string type, string value, int expected)
    {
        var root = RepositoryRoot();
        var frontend = Path.Combine(root, "self-host");
        var nativePath = Path.Combine(root, $"scoped-{Guid.NewGuid():N}.native.dll");
        var hostedPath = Path.Combine(root, $"scoped-{Guid.NewGuid():N}.hosted.dll");
        try
        {
            foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
            {
                var input = Encoding.UTF8.GetBytes(
                    $"public {type} Value() {{ if (true) {{ {type} x = {value}; var y = x; return y; }} return {value}; }}");
                var (nativeExit, nativeOutput, nativeError) = await RunAsync(input,
                    "frontend-build-library", frontend, "-", "-o", nativePath,
                    "--runtime", policy, "--json");
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
                        _ => throw new InvalidOperationException("Unknown scalar type."),
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
    private delegate int ScopedInt32();
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate uint ScopedUInt32();
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate long ScopedInt64();
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate ulong ScopedUInt64();
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate ushort ScopedChar16();
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    [return: MarshalAs(UnmanagedType.I1)]
    private delegate bool ScopedBool();
}
