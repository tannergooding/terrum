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
    [InlineData("int32", "if (false) { return 0; } else if (true) { return 42; } else { return 1; }", 42)]
    [InlineData("int32", "if (false) { return 0; } else if (false) { return 1; } else if (true) { return 42; } return 2;", 42)]
    [InlineData("int32", "mut int32 x = 0; if (++x == 1) { x = 42; } else if (++x == 2) { x = 0; } return x;", 42)]
    [InlineData("int32", "mut int32 x = 40; if (x++ == 0) { return 0; } else if (++x == 42) { return x; } return 1;", 42)]
    [InlineData("int32", "if (true) { return 42; } else if (int32(1) / int32(0) == 0) { return 0; } return 1;", 42)]
    [InlineData("int32", "if (false) { return 0; } else if (true || int32(1) / int32(0) == 0) { return 42; } return 1;", 42)]
    [InlineData("int32", "if (false) { return 0; } else if (false && int32(1) / int32(0) == 0) { return 1; } return 42;", 42)]
    [InlineData("int32", "if (false) { return 0; } else if (true ? true : int32(1) / int32(0) == 0) { return 42; } return 1;", 42)]
    [InlineData("int32", "if (false) { return 0; } else if (int32(1) / int32(0) == 0) { return 1; } return 42;", unchecked((int)0xE0540002))]
    [InlineData("int32", "if (false) { return 0; } else if (int32(2147483647) + int32(1) == 0) { return 1; } return 42;", unchecked((int)0xE0540001))]
    [InlineData("int32", "{ if (false) { int32 x = 0; return x; } else if (true) { int32 x = 42; return x; } } return 0;", 42)]
    [InlineData("int32", "if (true) { if (false) { return 0; } else if (true) { return 42; } } else if (true) { return 1; } return 0;", 42)]
    [InlineData("int32", "mut int32 x = 0; @const: for (; x < 2; x++) { if (false) {} else if (true) { continue const; } } return x + 40;", 42)]
    [InlineData("int32", "mut int32 x = 0; a: do { while (true) { if (false) {} else if (true) { x = 42; break a; } } } while (++x == 0); return x;", 42)]
    [InlineData("int32", "mut int32 x = 0; a: while (x < 42) { for (;;) { if (false) {} else if (true) { x += 21; continue a; } } } return x;", 42)]
    [InlineData("int32", "mut int32 x = 0; do { if (false) {} else if (++x < 2) { continue; } } while (x < 2); return x + 40;", 42)]
    [InlineData("int32", "mut int32 x = 0; if (false) {} else if (true) { while (x < 2) { x++; } } return x + 40;", 42)]
    [InlineData("int32", "mut int32 x = 0; if (false) {} else if (true) { a: while (false) {} } a: do { x = 42; } while (false); return x;", 42)]
    [InlineData("int32", "if (false) { _ = int32(0); } else if (true) { _ = int32(1); require true; } return 42;", 42)]
    [InlineData("void", "if (false) { require false; } else if (true) { return (); }", 0)]
    [InlineData("void", "if (false) { require false; } else if (true) { _ = int32(1); }", 0)]
    [InlineData("void", "if (false) {} else if (true) { require false; }", unchecked((int)0xE0540003))]
    [InlineData("void", "if (false) {} else if (true) { require int32(1) / int32(0) == 0; }", unchecked((int)0xE0540002))]
    [InlineData("void", "if (false) {} else if (true) { _ = int32(1) / int32(0); }", unchecked((int)0xE0540002))]
    [InlineData("void", "if (false) {} else if (false) {} else if (true) { require true; }", 0)]
    public async Task NativeElseIfMatchesHostedScannerPeAndJitAsync(string type, string body, int expected)
    {
        var root = RepositoryRoot();
        var native = Path.Combine(root, $"else-if-{Guid.NewGuid():N}.native.exe");
        var hosted = Path.Combine(root, $"else-if-{Guid.NewGuid():N}.hosted.exe");
        var scanner = Path.Combine(root, $"else-if-{Guid.NewGuid():N}.scanner.exe");
        try
        {
            var text = $"/*\U0001F600*/ {type} Main() {{ {body} }} //tail";
            Assert.True(Lexer.Lex(SourceText.From(text)).Tokens.Length <= 64);
            var source = Encoding.UTF8.GetBytes(text);
            foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
            {
                var (exit, output, error) = await RunAsync(source, "frontend-build",
                    Path.Combine(root, "self-host"), "-", "-o", native, "--runtime", policy, "--json");
                Assert.True(exit == 0, $"{output}{error}");
                using var document = JsonDocument.Parse(output);
                Assert.Equal(85, document.RootElement.GetProperty("result").GetProperty("projectionVersion").GetInt32());
                var (hostExit, hostOutput, hostError) = await RunAsync(source, "build", "-", "-o", hosted, "--runtime", policy, "--json");
                Assert.True(hostExit == 0, $"{hostOutput}{hostError}");
                var (scanExit, scanOutput, scanError) = await RunAsync(source, "build-component",
                    Path.Combine(root, "self-host"), "-", "-o", scanner, "--runtime", policy, "--json");
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
    [InlineData("int32 Main() { if (false) { return 0; } else if (false) { return 1; } }")]
    [InlineData("int32 Main() { if (false) {} else if (true) { Missing(); } return 42; }")]
    [InlineData("int32 Main() { if (true) { return 42; } else if (false) { Missing(); } return 0; }")]
    [InlineData("void Main() { if (true) { return; } else if (false) { Missing(); } }")]
    [InlineData("void Main() { if (false) {} else if (1) {} }")]
    [InlineData("void Main() { if (false) {} else if (true) {} Missing(); }")]
    [InlineData("void Main() { if (false) {} else if (true) {} else if () {} }")]
    [InlineData("int32 Main() { if (true) { int32 x = 1; } else if (x == 1) {} return 42; }")]
    [InlineData("void Main() { if (false) {} else if (true) { _ = int32(1); } require _; }")]
    [InlineData("void Main() { a: while (false) { if (false) {} else if (true) { a: do {} while (false); } } }")]
    [InlineData("void Main() { if (false) {} else if (true) { continue missing; } }")]
    [InlineData("void Main() => if (false) {} else if (true) {}")]
    public async Task NativeElseIfRejectsUnsupportedInactiveSourceAndRetainsPeAsync(string text)
    {
        var path = Path.Combine(RepositoryRoot(), $"else-if-{Guid.NewGuid():N}.exe");
        try
        {
            foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
            {
                var (initial, initialOutput, initialError) = await RunAsync(
                    Encoding.UTF8.GetBytes("int32 Main() { if (false) {} else if (true) {} return 42; }"),
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
    [InlineData("int32", "int32(42)", 42)]
    [InlineData("uint32", "uint32(42)", 42)]
    [InlineData("int64", "int64(42)", 42)]
    [InlineData("uint64", "uint64(42)", 42)]
    [InlineData("char16", "char16(42)", 42)]
    [InlineData("bool", "true", 1)]
    public async Task NativeElseIfPreservesSixNormalizedScalarExportsAsync(string type, string value, int expected)
    {
        var root = RepositoryRoot();
        var native = Path.Combine(root, $"else-if-{Guid.NewGuid():N}.native.dll");
        var hosted = Path.Combine(root, $"else-if-{Guid.NewGuid():N}.hosted.dll");
        try
        {
            var source = Encoding.UTF8.GetBytes($"public {type} @await() {{ if (false) {{ return {value}; }} else if (true) {{ return {value}; }} else {{ return {value}; }} }}");
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

    [Theory]
    [InlineData("int32", "", "if (false) { return 0; } else if (true) { return 42; } return 1;", 0)]
    [InlineData("int32", "int32 a", "if (false) { return 0; } else if (a == 42) { return a; } return 1;", 1)]
    [InlineData("int32", "int32 a, int32 b", "if (false) { return 0; } else if (a + b == 42) { return a + b; } return 1;", 2)]
    [InlineData("int32", "int32 a, int32 b, int32 c", "if (false) { return 0; } else if (a + b + c == 42) { return a + b + c; } return 1;", 3)]
    [InlineData("void", "", "if (false) {} else if (true) { require true; }", 0)]
    public async Task NativeElseIfPreservesParameterAbiAndVoidExportAsync(string type, string parameters, string body, int count)
    {
        var root = RepositoryRoot();
        var native = Path.Combine(root, $"else-if-{Guid.NewGuid():N}.native.dll");
        var hosted = Path.Combine(root, $"else-if-{Guid.NewGuid():N}.hosted.dll");
        try
        {
            var source = Encoding.UTF8.GetBytes($"public {type} @await({parameters}) {{ {body} }}");
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
                        var actual = count switch {
                            0 => Marshal.GetDelegateForFunctionPointer<ScopedInt32>(export)(),
                            1 => Marshal.GetDelegateForFunctionPointer<ElseIfOne>(export)(42),
                            2 => Marshal.GetDelegateForFunctionPointer<ElseIfTwo>(export)(20, 22),
                            _ => Marshal.GetDelegateForFunctionPointer<DoForThreeScalars>(export)(10, 20, 12),
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

    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int ElseIfOne(int a);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int ElseIfTwo(int a, int b);

    [Theory]
    [InlineData("int32", "if (true) { require false; } else if (true) { require false; }")]
    [InlineData("void", "for (;;) { if (false) {} else if (true) { continue; } }")]
    [InlineData("int32", "a: for (;;) { if (false) {} else if (true) { continue a; } }")]
    public async Task NativeElseIfNonFallthroughCompileOnlyAsync(string type, string body)
    {
        var root = RepositoryRoot();
        var native = Path.Combine(root, $"else-if-{Guid.NewGuid():N}.native.exe");
        var hosted = Path.Combine(root, $"else-if-{Guid.NewGuid():N}.hosted.exe");
        try
        {
            foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
            {
                var source = Encoding.UTF8.GetBytes($"{type} Main() {{ {body} }}");
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
    [InlineData("depth", 6, true)]
    [InlineData("depth", 7, false)]
    [InlineData("records", 12, true)]
    [InlineData("records", 13, false)]
    [InlineData("nodes", 32, true)]
    [InlineData("nodes", 33, false)]
    [InlineData("locals", 8, true)]
    [InlineData("locals", 9, false)]
    [InlineData("source", 16384, true)]
    [InlineData("source", 16385, false)]
    [InlineData("expression", 16, true)]
    [InlineData("expression", 17, false)]
    [InlineData("tokens", 64, true)]
    [InlineData("tokens", 65, false)]
    public async Task NativeElseIfRetainsInclusiveResourceCapsAsync(string resource, int count, bool valid)
    {
        const string chain = "if (false) {} else if (true) {}";
        var body = resource switch {
            "depth" => new string('{', count - 2) + chain + new string('}', count - 2),
            "records" => chain + string.Concat(Enumerable.Repeat("require true;", count - 3)),
            "nodes" => "if (" + (valid ? "" : "!") + string.Join(" && ", Enumerable.Repeat("true", 16))
                + ") {} else if (true) {}",
            "locals" => "if (false) {" + string.Concat(Enumerable.Repeat("_ = true;", 4)) + "} else if (true) {"
                + string.Concat(Enumerable.Repeat("_ = true;", count - 4)) + "}",
            "expression" => "if (" + new string('(', count - 1) + "true" + new string(')', count - 1)
                + ") {} else if (true) {}",
            _ => chain,
        };
        var source = $"void Main() {{ {body} }}";
        if (resource == "tokens")
        {
            source = "public void Main(bool a, bool b, bool c) { if (" + new string('(', 15)
                + "true" + new string(')', 15) + ") {} else if (" + (valid ? "" : "!")
                + "true) {} _ = !true; }";
            Assert.Equal(count, Lexer.Lex(SourceText.From(source)).Tokens.Length);
        }
        if (resource == "source")
        {
            source += " /*" + new string('x', count - source.Length - 5) + "*/";
            Assert.Equal(count, source.Length);
        }
        if (resource != "tokens")
        {
            Assert.True(Lexer.Lex(SourceText.From(source)).Tokens.Length <= 64);
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
                Assert.Equal(85, result.GetProperty("projectionVersion").GetInt32());
                if (resource == "nodes") { Assert.Equal(count, result.GetProperty("nodes").GetArrayLength()); }
                if (resource == "locals") { Assert.Equal(count, result.GetProperty("locals").GetArrayLength()); }
                if (resource == "records") { Assert.Equal(count, result.GetProperty("statements").GetArrayLength()); }
            }
        }
    }
}
