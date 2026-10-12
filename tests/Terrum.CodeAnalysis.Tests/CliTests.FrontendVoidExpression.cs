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
    [InlineData("require true;", 0)]
    [InlineData("require false;", unchecked((int)0xE0540003))]
    [InlineData("require true || int32(1) / int32(0) == 0;", 0)]
    [InlineData("require false && int32(1) / int32(0) == 0;", unchecked((int)0xE0540003))]
    [InlineData("require int32(1) / int32(0) == 0;", unchecked((int)0xE0540002))]
    [InlineData("require true ? true : int32(1) / int32(0) == 0;", 0)]
    [InlineData("require true ? false : true;", unchecked((int)0xE0540003))]
    [InlineData("_ = true;", 0)]
    [InlineData("_ = int32(42);", 0)]
    [InlineData("_ = uint32(0xFFFF_FFFF);", 0)]
    [InlineData("_ = int64(-9223372036854775808);", 0)]
    [InlineData("_ = uint64(18446744073709551615);", 0)]
    [InlineData("_ = char16(65535);", 0)]
    [InlineData("_ = int32(2147483647) + int32(1);", unchecked((int)0xE0540001))]
    [InlineData("_ = int32(1) / int32(0);", unchecked((int)0xE0540002))]
    [InlineData("_ = false && int32(1) / int32(0) == 0;", 0)]
    [InlineData("_ = false ? int32(1) / int32(0) : int32(42);", 0)]
    [InlineData("_ = int32(uint64(4294967296));", unchecked((int)0xE0540001))]
    [InlineData("/*\U0001F600*/ _ = (char16(0xFFFF));", 0)]
    public async Task NativeVoidExpressionMatchesHostedScannerPeAndJitAsync(string statement, int expected)
    {
        var root = RepositoryRoot();
        var native = Path.Combine(root, $"void-expression-{Guid.NewGuid():N}.native.exe");
        var hosted = Path.Combine(root, $"void-expression-{Guid.NewGuid():N}.hosted.exe");
        var scanner = Path.Combine(root, $"void-expression-{Guid.NewGuid():N}.scanner.exe");
        try
        {
            var source = Encoding.UTF8.GetBytes($"void Main() => {statement} /*trailing*/");
            foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
            {
                var (exit, output, error) = await RunAsync(source, "frontend-build",
                    Path.Combine(root, "self-host"), "-", "-o", native, "--runtime", policy, "--json");
                Assert.True(exit == 0, $"{policy}: {output}{error}");
                using var document = JsonDocument.Parse(output);
                var result = document.RootElement.GetProperty("result");
                Assert.Equal(87, result.GetProperty("projectionVersion").GetInt32());
                Assert.True(result.GetProperty("expressionBody").GetBoolean());
                var (hostExit, hostOutput, hostError) = await RunAsync(source, "build",
                    "-", "-o", hosted, "--runtime", policy, "--json");
                Assert.True(hostExit == 0, $"{hostOutput}{hostError}");
                var (scanExit, scanOutput, scanError) = await RunAsync(source, "build-component",
                    Path.Combine(root, "self-host"), "-", "-o", scanner, "--runtime", policy, "--json");
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
    [InlineData("void Main() => ();")]
    [InlineData("void Main() => return;")]
    [InlineData("void Main() => return ();")]
    [InlineData("void Main() => 42;")]
    [InlineData("void Main() => Missing();")]
    [InlineData("void Main() => _ = Missing();")]
    [InlineData("void Main() => _ = ();")]
    [InlineData("void Main() => _ = 42;")]
    [InlineData("void Main() => _ = null;")]
    [InlineData("void Main() => require 1;")]
    [InlineData("void Main() => require ();")]
    [InlineData("void Main() => static require true;")]
    [InlineData("void Main() => require true, 1;")]
    [InlineData("void Main() => require true; require true;")]
    [InlineData("void Main() => _ = true; return;")]
    [InlineData("void Main() => require true")]
    [InlineData("void Main() => { require true; }")]
    [InlineData("void Main() => _ += int32(1);")]
    [InlineData("void Main() => _ = _;")]
    [InlineData("int32 Main() => require true;")]
    [InlineData("int32 Main() => _ = int32(42);")]
    [InlineData("void Main()=> require true;")]
    [InlineData("void Main() =>require true;")]
    [InlineData("void Main() => _= true;")]
    [InlineData("void Main() => _ =true;")]
    public async Task NativeVoidExpressionRejectsUnsupportedSourceAndRetainsPeAsync(string text)
    {
        var path = Path.Combine(RepositoryRoot(), $"void-expression-{Guid.NewGuid():N}.exe");
        try
        {
            foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
            {
                var (initial, initialOutput, initialError) = await RunAsync(Encoding.UTF8.GetBytes("void Main() => require true;"),
                    "frontend-build", Path.Combine(RepositoryRoot(), "self-host"), "-", "-o", path, "--runtime", policy, "--json");
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
    [InlineData("", "_ = true;")]
    [InlineData("bool @const", "require const;")]
    [InlineData("int32 a, uint64 b", "require a > 0 && b == uint64(42);")]
    [InlineData("char16 a, uint32 b, int64 c", "_ = a == char16(42) && b == uint32(42) && c == int64(42);")]
    [InlineData("int32 x", "_ = x + 1;")]
    public async Task NativeVoidExpressionPreservesNormalizedExportAndPrimitiveParameterAbiAsync(string parameters, string statement)
    {
        var root = RepositoryRoot();
        var native = Path.Combine(root, $"void-expression-{Guid.NewGuid():N}.native.dll");
        var hosted = Path.Combine(root, $"void-expression-{Guid.NewGuid():N}.hosted.dll");
        try
        {
            var source = Encoding.UTF8.GetBytes($"public void @await({parameters}) => {statement}");
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
                        case "bool @const": Marshal.GetDelegateForFunctionPointer<NativeVoidOne>(export)(true); break;
                        case "int32 x": Marshal.GetDelegateForFunctionPointer<VoidExpressionInt32>(export)(42); break;
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

    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate void VoidExpressionInt32(int x);

    [Theory]
    [InlineData("x = 1;")]
    [InlineData("x++;")]
    [InlineData("_ = x++;")]
    [InlineData("require ++x > 0;")]
    [InlineData("int32 y = x;")]
    public async Task NativeVoidExpressionRejectsUnsupportedReadonlyParameterActionsAsync(string statement)
    {
        foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
        {
            var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes($"public void F(int32 x) => {statement}"),
                "frontend-component", Path.Combine(RepositoryRoot(), "self-host"), "-", "--runtime", policy, "--json");
            Assert.True(exit == 1, $"{output}{error}");
        }
    }

    [Theory]
    [InlineData("require false;")]
    [InlineData("require !true;")]
    public async Task NativeVoidExpressionNonFallthroughCompileOnlyAsync(string statement)
    {
        var root = RepositoryRoot();
        var native = Path.Combine(root, $"void-expression-{Guid.NewGuid():N}.native.exe");
        var hosted = Path.Combine(root, $"void-expression-{Guid.NewGuid():N}.hosted.exe");
        try
        {
            foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
            {
                var source = Encoding.UTF8.GetBytes($"void Main() => {statement}");
                var (exit, output, error) = await RunAsync(source, "frontend-build",
                    Path.Combine(root, "self-host"), "-", "-o", native, "--runtime", policy, "--json");
                Assert.True(exit == 0, $"{output}{error}");
                var (hostExit, hostOutput, hostError) = await RunAsync(source, "build", "-", "-o", hosted, "--runtime", policy, "--json");
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
    [InlineData("nodes", 64, true)]
    [InlineData("nodes", 65, false)]
    [InlineData("tokens", 64, true)]
    [InlineData("tokens", 65, true)]
    [InlineData("source", 16384, true)]
    [InlineData("source", 16385, false)]
    [InlineData("expression", 16, true)]
    [InlineData("expression", 17, false)]
    public async Task NativeVoidExpressionRetainsOtherResourceCapsAsync(string resource, int count, bool valid)
    {
        var source = resource switch {
            "nodes" => "void Main() => require !true && " + (valid ? "" : "!")
                + string.Join(" && ", Enumerable.Repeat("true", (count - 2) / 2)) + ";",
            "tokens" => (count == 64 ? "" : "public ") + "void Main(bool a) => require "
                + string.Join(" + ", Enumerable.Repeat("int32(1)", 10)) + " == int32(10);",
            "expression" => "void Main() => require " + new string('(', count - 1)
                + "true" + new string(')', count - 1) + ";",
            _ => "void Main() => require true;",
        };
        if (resource == "tokens") { Assert.Equal(count, Lexer.Lex(SourceText.From(source)).Tokens.Length); }
        if (resource == "source")
        {
            source += " /*" + new string('x', count - source.Length - 5) + "*/";
            Assert.Equal(count, source.Length);
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
                Assert.Equal(87, result.GetProperty("projectionVersion").GetInt32());
                Assert.Single(result.GetProperty("statements").EnumerateArray());
                if (resource == "nodes") { Assert.Equal(count, result.GetProperty("nodes").GetArrayLength()); }
            }
        }
    }
}
