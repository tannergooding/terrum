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
    [InlineData("[NoThrow] int32 Main() { return 40 + 2; }", 42)]
    [InlineData("[NoThrow] int32 Main() => int32(84) / int32(2);", 42)]
    [InlineData("[NoThrow] int32 Main() { return int32(21) << 1; }", 42)]
    [InlineData("[NoThrow] int32 Main() { if (int32(1) == int32(1)) { return 42; } }", 42)]
    [InlineData("[NoThrow] int32 Main() { if (false) { return 0; } else if (true) { return 42; } return 1; }", 42)]
    [InlineData("[NoThrow] int32 Main() { int32 x = 40; return x | 2; }", 42)]
    [InlineData("[NoThrow] int32 Main() { mut int32 x = 0; { x = 42; } while (false) {} return x; }", 42)]
    [InlineData("[NoThrow] void Main() {}", 0)]
    [InlineData("[NoThrow] void Main() { return (); }", 0)]
    [InlineData("[NoThrow] void Main() => require int32(1) + int32(1) == 2;", 0)]
    [InlineData("[NoThrow] void Main() => _ = int32(84) / int32(2);", 0)]
    [InlineData("[NoThrow] void Main() { _ = uint64(0xFFFFFFFFFFFFFFFF); require true || false; }", 0)]
    [InlineData("[NoThrow] void Main() { if (false) {} else if (true) { require true ? true : false; } }", 0)]
    [InlineData("[NoThrow] int32 Main() { mut int32 x = 0; a: do { x = 42; break a; } while (true); return x; }", 42)]
    [InlineData("[DoesNotReturn] void Main() { require false; }", unchecked((int)0xE0540003))]
    [InlineData("[DoesNotReturn] void Main() => require false;", unchecked((int)0xE0540003))]
    [InlineData("[DoesNotReturn] void Main() { require false; return; }", unchecked((int)0xE0540003))]
    [InlineData("[DoesNotReturn] void Main() { if (true) { require false; } else if (false) { return; } }", unchecked((int)0xE0540003))]
    [InlineData("[DoesNotReturn] void Main() { require int32(1) / int32(0) == 0; require false; }", unchecked((int)0xE0540002))]
    public async Task NativeFunctionEffectsMatchHostedScannerPeAndJitAsync(string text, int expected)
    {
        var root = RepositoryRoot();
        var native = Path.Combine(root, $"effects-{Guid.NewGuid():N}.native.exe");
        var hosted = Path.Combine(root, $"effects-{Guid.NewGuid():N}.hosted.exe");
        var scanner = Path.Combine(root, $"effects-{Guid.NewGuid():N}.scanner.exe");
        try
        {
            var source = Encoding.UTF8.GetBytes($"/*\U0001F600*/ {text} //tail");
            foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
            {
                var (exit, output, error) = await RunAsync(source, "frontend-build", Path.Combine(root, "self-host"),
                    "-", "-o", native, "--runtime", policy, "--json");
                Assert.True(exit == 0, $"{output}{error}");
                using var document = JsonDocument.Parse(output);
                Assert.Equal(59, document.RootElement.GetProperty("result").GetProperty("projectionVersion").GetInt32());
                var (hostExit, hostOutput, hostError) = await RunAsync(source, "build", "-", "-o", hosted, "--runtime", policy, "--json");
                Assert.True(hostExit == 0, $"{hostOutput}{hostError}");
                var (scanExit, scanOutput, scanError) = await RunAsync(source, "build-component", Path.Combine(root, "self-host", "Scanner.terrum"),
                    "-", "-o", scanner, "--runtime", policy, "--json");
                Assert.True(scanExit == 0, $"{scanOutput}{scanError}");
                Assert.Equal(await File.ReadAllBytesAsync(hosted), await File.ReadAllBytesAsync(native));
                Assert.Equal(await File.ReadAllBytesAsync(hosted), await File.ReadAllBytesAsync(scanner));
                Assert.Equal(expected, await ExecuteNativeImageAsync(native));
                var (runExit, runOutput, runError) = await RunAsync(source, "frontend-run", Path.Combine(root, "self-host"),
                    "-", "--runtime", policy, "--json");
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
    [InlineData("[NoThrow] int32 Main() { mut int32 x = 1; return x + 1; }")]
    [InlineData("[NoThrow] void Main() { require false; }")]
    [InlineData("[NoThrow] void Main() => require false;")]
    [InlineData("[NoThrow] void Main() { _ = int32(2147483647) + int32(1); }")]
    [InlineData("[NoThrow] void Main() { _ = char16(65536); }")]
    [InlineData("[NoThrow] void Main() { mut int32 x = 0; x++; }")]
    [InlineData("[NoThrow] void Main() { require false && int32(1) / int32(0) == 0; }")]
    [InlineData("[NoThrow] void Main() { _ = true ? int32(42) : int32(1) / int32(0); }")]
    [InlineData("[DoesNotReturn] int32 Main() { return 42; }")]
    [InlineData("[DoesNotReturn] void Main() {}")]
    [InlineData("[DoesNotReturn] void Main() { return; }")]
    [InlineData("[DoesNotReturn] void Main() { return (); }")]
    [InlineData("[DoesNotReturn] void Main() => require true;")]
    [InlineData("[NoThrow][DoesNotReturn] void Main() { require false; }")]
    [InlineData("[NoThrow][NoThrow] void Main() {}")]
    [InlineData("[DoesNotReturn][DoesNotReturn] void Main() { require false; }")]
    [InlineData("[nothrow] void Main() {}")]
    [InlineData("[NoＴhrow] void Main() {}")]
    [InlineData("[@NoThrow] void Main() {}")]
    [InlineData("[NoThrow(value)] void Main() {}")]
    [InlineData("[NoThrow()] void Main() {}")]
    [InlineData("[Cleanup] void Main() {}")]
    [InlineData("[ConstEval] void Main() {}")]
    [InlineData("public [NoThrow] void Main() {}")]
    [InlineData("[NoThrow] void Main() { return; Missing(); }")]
    [InlineData("[DoesNotReturn] void Main() { require false; Missing(); }")]
    public async Task NativeFunctionEffectsRejectInvalidEffectsAndRetainPeAsync(string text)
    {
        var path = Path.Combine(RepositoryRoot(), $"effects-{Guid.NewGuid():N}.exe");
        try
        {
            foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
            {
                var (initial, initialOutput, initialError) = await RunAsync(
                    Encoding.UTF8.GetBytes("[NoThrow] int32 Main() => 42;"), "frontend-build",
                    Path.Combine(RepositoryRoot(), "self-host"), "-", "-o", path, "--runtime", policy, "--json");
                Assert.True(initial == 0, $"{initialOutput}{initialError}");
                var bytes = await File.ReadAllBytesAsync(path);
                var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(text), "frontend-build",
                    Path.Combine(RepositoryRoot(), "self-host"), "-", "-o", path, "--runtime", policy, "--json");
                Assert.True(exit == 1, $"{output}{error}");
                Assert.Equal(bytes, await File.ReadAllBytesAsync(path));
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData("int32", "int32(40) + int32(2)", 42)]
    [InlineData("uint32", "uint32(84) / uint32(2)", 42)]
    [InlineData("int64", "int64(21) << 1", 42)]
    [InlineData("uint64", "uint64(40) | uint64(2)", 42)]
    [InlineData("char16", "char16(42)", 42)]
    [InlineData("bool", "true && !false", 1)]
    [InlineData("void", "require true", 0)]
    public async Task NativeFunctionEffectsPreserveNormalizedSixScalarAndVoidExportsAsync(string type, string expression, int expected)
    {
        var root = RepositoryRoot();
        var native = Path.Combine(root, $"effects-{Guid.NewGuid():N}.native.dll");
        var hosted = Path.Combine(root, $"effects-{Guid.NewGuid():N}.hosted.dll");
        try
        {
            var source = Encoding.UTF8.GetBytes($"[NoThrow] public {type} @await() => {expression};");
            foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
            {
                var (exit, output, error) = await RunAsync(source, "frontend-build-library", Path.Combine(root, "self-host"),
                    "-", "-o", native, "--runtime", policy, "--json");
                Assert.True(exit == 0, $"{output}{error}");
                var (hostExit, hostOutput, hostError) = await RunAsync(source, "build-library", "-", "--export", "await",
                    "-o", hosted, "--runtime", policy, "--json");
                Assert.True(hostExit == 0, $"{hostOutput}{hostError}");
                Assert.Equal(await File.ReadAllBytesAsync(hosted), await File.ReadAllBytesAsync(native));
                var handle = NativeLibrary.Load(native);
                try
                {
                    var export = NativeLibrary.GetExport(handle, "await");
                    if (type == "void")
                    {
                        Marshal.GetDelegateForFunctionPointer<NativeVoidZero>(export)();
                    }
                    else
                    {
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
    [InlineData("[DoesNotReturn] void Main() { while (true) {} }")]
    [InlineData("[NoThrow][DoesNotReturn] void Main() { for (;;) {} }")]
    [InlineData("[DoesNotReturn][NoThrow] void Main() { for (;;) {} }")]
    [InlineData("[NoThrow][DoesNotReturn] void Main() { if (false) { return; } else if (true) { for (;;) {} } }")]
    public async Task NativeFunctionEffectsNonFallthroughCompileOnlyAsync(string text)
    {
        var root = RepositoryRoot();
        var native = Path.Combine(root, $"effects-{Guid.NewGuid():N}.native.exe");
        var hosted = Path.Combine(root, $"effects-{Guid.NewGuid():N}.hosted.exe");
        try
        {
            foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
            {
                var source = Encoding.UTF8.GetBytes(text);
                var (exit, output, error) = await RunAsync(source, "frontend-build", Path.Combine(root, "self-host"),
                    "-", "-o", native, "--runtime", policy, "--json");
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
    [InlineData(64, true)]
    [InlineData(65, false)]
    public async Task NativeFunctionEffectsAttributeTokensCountAgainstExistingLimitAsync(int count, bool valid)
    {
        var source = "[NoThrow] public void @await(bool a, bool b, bool c) { require "
            + new string('(', 15) + "true" + new string(')', 15)
            + "; require true; require true; require true; require " + (valid ? "true" : "!false") + "; }";
        Assert.Equal(count, Lexer.Lex(SourceText.From(source)).Tokens.Length);
        var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(source), "frontend-component",
            Path.Combine(RepositoryRoot(), "self-host"), "-", "--runtime", "fail-fast", "--json");
        Assert.True(exit == (valid ? 0 : 1), $"{output}{error}");
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(false, 1)]
    [InlineData(false, 2)]
    [InlineData(false, 3)]
    [InlineData(true, 0)]
    [InlineData(true, 1)]
    [InlineData(true, 2)]
    [InlineData(true, 3)]
    public async Task NativeFunctionEffectsPreserveZeroThroughThreeParameterAbiAsync(bool isVoid, int count)
    {
        var root = RepositoryRoot();
        var native = Path.Combine(root, $"effects-{Guid.NewGuid():N}.native.dll");
        var hosted = Path.Combine(root, $"effects-{Guid.NewGuid():N}.hosted.dll");
        var parameters = isVoid ? count switch {
            0 => "", 1 => "bool a", 2 => "int32 a, uint64 b", _ => "char16 a, uint32 b, int64 c",
        } : string.Join(", ", Enumerable.Range(0, count).Select(index => $"int32 {(char)('a' + index)}"));
        var expression = isVoid ? "require true" : count switch {
            0 => "42", 1 => "a", 2 => "a | b", _ => "a | b | c",
        };
        try
        {
            var source = Encoding.UTF8.GetBytes($"[NoThrow] public {(isVoid ? "void" : "int32")} @await({parameters}) => {expression};");
            foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
            {
                var (exit, output, error) = await RunAsync(source, "frontend-build-library", Path.Combine(root, "self-host"),
                    "-", "-o", native, "--runtime", policy, "--json");
                Assert.True(exit == 0, $"{output}{error}");
                var (hostExit, hostOutput, hostError) = await RunAsync(source, "build-library", "-", "--export", "await",
                    "-o", hosted, "--runtime", policy, "--json");
                Assert.True(hostExit == 0, $"{hostOutput}{hostError}");
                Assert.Equal(await File.ReadAllBytesAsync(hosted), await File.ReadAllBytesAsync(native));
                var handle = NativeLibrary.Load(native);
                try
                {
                    var export = NativeLibrary.GetExport(handle, "await");
                    if (isVoid)
                    {
                        switch (count)
                        {
                            case 0: Marshal.GetDelegateForFunctionPointer<NativeVoidZero>(export)(); break;
                            case 1: Marshal.GetDelegateForFunctionPointer<NativeVoidOne>(export)(true); break;
                            case 2: Marshal.GetDelegateForFunctionPointer<NativeVoidTwo>(export)(42, ulong.MaxValue); break;
                            default: Marshal.GetDelegateForFunctionPointer<NativeVoidThree>(export)(65535, uint.MaxValue, long.MinValue); break;
                        }
                    }
                    else
                    {
                        var actual = count switch {
                            0 => Marshal.GetDelegateForFunctionPointer<ScopedInt32>(export)(),
                            1 => Marshal.GetDelegateForFunctionPointer<ElseIfOne>(export)(42),
                            2 => Marshal.GetDelegateForFunctionPointer<ElseIfTwo>(export)(40, 2),
                            _ => Marshal.GetDelegateForFunctionPointer<DoForThreeScalars>(export)(32, 8, 2),
                        };
                        Assert.Equal(42, actual);
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

    [Theory]
    [InlineData("source", 16384, true)]
    [InlineData("source", 16385, false)]
    [InlineData("records", 12, true)]
    [InlineData("records", 13, false)]
    [InlineData("locals", 8, true)]
    [InlineData("locals", 9, false)]
    [InlineData("nodes", 32, true)]
    [InlineData("nodes", 33, false)]
    [InlineData("expression", 16, true)]
    [InlineData("expression", 17, false)]
    [InlineData("depth", 6, true)]
    [InlineData("depth", 7, false)]
    public async Task NativeFunctionEffectsRetainInclusiveResourceCapsAsync(string resource, int count, bool valid)
    {
        var body = resource switch {
            "records" => string.Concat(Enumerable.Repeat("require true;", count)),
            "locals" => string.Concat(Enumerable.Repeat("_ = true;", count)),
            "nodes" => "_ = !false || " + (valid ? "" : "!") + string.Join(" || ", Enumerable.Repeat("true", 15)) + ";",
            "expression" => "require " + new string('(', count - 1) + "true" + new string(')', count - 1) + ";",
            "depth" => new string('{', count) + "require true;" + new string('}', count),
            _ => "require true;",
        };
        var text = $"[NoThrow] void Main() {{ {body} }}";
        if (resource == "source")
        {
            text += " /*" + new string('x', count - text.Length - 5) + "*/";
            Assert.Equal(count, text.Length);
        }
        Assert.True(Lexer.Lex(SourceText.From(text)).Tokens.Length <= 64);
        foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
        {
            var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(text), "frontend-component",
                Path.Combine(RepositoryRoot(), "self-host"), "-", "--runtime", policy, "--json");
            Assert.True(exit == (valid ? 0 : resource == "source" ? 2 : 1), $"{output}{error}");
            if (valid)
            {
                using var document = JsonDocument.Parse(output);
                var result = document.RootElement.GetProperty("result");
                Assert.Equal(59, result.GetProperty("projectionVersion").GetInt32());
                if (resource == "nodes") { Assert.Equal(count, result.GetProperty("nodes").GetArrayLength()); }
                if (resource == "records") { Assert.Equal(count, result.GetProperty("statements").GetArrayLength()); }
                if (resource == "locals") { Assert.Equal(count, result.GetProperty("locals").GetArrayLength()); }
            }
        }
    }
}
