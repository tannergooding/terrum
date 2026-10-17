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
    [TestCase("int32","if (false) { return 0; } else if (true) { return 42; } else { return 1; }",42)]
    [TestCase("int32","if (false) { return 0; } else if (false) { return 1; } else if (true) { return 42; } return 2;",42)]
    [TestCase("int32","mut int32 x = 0; if (++x == 1) { x = 42; } else if (++x == 2) { x = 0; } return x;",42)]
    [TestCase("int32","mut int32 x = 40; if (x++ == 0) { return 0; } else if (++x == 42) { return x; } return 1;",42)]
    [TestCase("int32","if (true) { return 42; } else if (int32(1) / int32(0) == 0) { return 0; } return 1;",42)]
    [TestCase("int32","if (false) { return 0; } else if (true || int32(1) / int32(0) == 0) { return 42; } return 1;",42)]
    [TestCase("int32","if (false) { return 0; } else if (false && int32(1) / int32(0) == 0) { return 1; } return 42;",42)]
    [TestCase("int32","if (false) { return 0; } else if (true ? true : int32(1) / int32(0) == 0) { return 42; } return 1;",42)]
    [TestCase("int32","if (false) { return 0; } else if (int32(1) / int32(0) == 0) { return 1; } return 42;",unchecked((int)0xE0540002))]
    [TestCase("int32","if (false) { return 0; } else if (int32(2147483647) + int32(1) == 0) { return 1; } return 42;",unchecked((int)0xE0540001))]
    [TestCase("int32","{ if (false) { int32 x = 0; return x; } else if (true) { int32 x = 42; return x; } } return 0;",42)]
    [TestCase("int32","if (true) { if (false) { return 0; } else if (true) { return 42; } } else if (true) { return 1; } return 0;",42)]
    [TestCase("int32","mut int32 x = 0; @const: for (; x < 2; x++) { if (false) {} else if (true) { continue const; } } return x + 40;",42)]
    [TestCase("int32","mut int32 x = 0; a: do { while (true) { if (false) {} else if (true) { x = 42; break a; } } } while (++x == 0); return x;",42)]
    [TestCase("int32","mut int32 x = 0; a: while (x < 42) { for (;;) { if (false) {} else if (true) { x += 21; continue a; } } } return x;",42)]
    [TestCase("int32","mut int32 x = 0; do { if (false) {} else if (++x < 2) { continue; } } while (x < 2); return x + 40;",42)]
    [TestCase("int32","mut int32 x = 0; if (false) {} else if (true) { while (x < 2) { x++; } } return x + 40;",42)]
    [TestCase("int32","mut int32 x = 0; if (false) {} else if (true) { a: while (false) {} } a: do { x = 42; } while (false); return x;",42)]
    [TestCase("int32","if (false) { _ = int32(0); } else if (true) { _ = int32(1); require true; } return 42;",42)]
    [TestCase("void","if (false) { require false; } else if (true) { return (); }",0)]
    [TestCase("void","if (false) { require false; } else if (true) { _ = int32(1); }",0)]
    [TestCase("void","if (false) {} else if (true) { require false; }",unchecked((int)0xE0540003))]
    [TestCase("void","if (false) {} else if (true) { require int32(1) / int32(0) == 0; }",unchecked((int)0xE0540002))]
    [TestCase("void","if (false) {} else if (true) { _ = int32(1) / int32(0); }",unchecked((int)0xE0540002))]
    [TestCase("void","if (false) {} else if (false) {} else if (true) { require true; }",0)]
    public async Task NativeElseIfMatchesHostedScannerPeAndJitAsync(string type, string body, int expected)
    {
        var root = RepositoryRoot();
        var native = CreateTestArtifactPath($"else-if-{Guid.NewGuid():N}.native.exe");
        var hosted = CreateTestArtifactPath($"else-if-{Guid.NewGuid():N}.hosted.exe");
        var scanner = CreateTestArtifactPath($"else-if-{Guid.NewGuid():N}.scanner.exe");
        try
        {
            var text = $"/*\U0001F600*/ {type} Main() {{ {body} }} //tail";
            Assert.That(Lexer.Lex(SourceText.From(text)).Tokens.Length <= 64, Is.True);
            var source = Encoding.UTF8.GetBytes(text);
            foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
            {
                var (exit, output, error) = await RunAsync(source, "frontend-build",
                    SelfHostPaths.SourceDirectory(root), "-", "-o", native, "--runtime", policy, "--json");
                Assert.That(exit == 0, Is.True, $"{output}{error}");
                using var document = JsonDocument.Parse(output);
                Assert.That(document.RootElement.GetProperty("result").GetProperty("projectionVersion").GetInt32(), Is.EqualTo(87));
                var (hostExit, hostOutput, hostError) = await RunAsync(source, "build", "-", "-o", hosted, "--runtime", policy, "--json");
                Assert.That(hostExit == 0, Is.True, $"{hostOutput}{hostError}");
                var (scanExit, scanOutput, scanError) = await RunAsync(source, "build-component",
                    SelfHostPaths.SourceDirectory(root), "-", "-o", scanner, "--runtime", policy, "--json");
                Assert.That(scanExit == 0, Is.True, $"{scanOutput}{scanError}");
                Assert.That(await File.ReadAllBytesAsync(native), Is.EqualTo(await File.ReadAllBytesAsync(hosted)));
                Assert.That(await File.ReadAllBytesAsync(scanner), Is.EqualTo(await File.ReadAllBytesAsync(hosted)));
                Assert.That(await ExecuteNativeImageAsync(native), Is.EqualTo(expected));
                var (runExit, runOutput, runError) = await RunAsync(source, "frontend-run", SelfHostPaths.SourceDirectory(root),
                    "-", "--runtime", policy, "--json");
                Assert.That(runExit == expected, Is.True, $"{runOutput}{runError}");
                var (jitExit, jitOutput, jitError) = await RunAsync(source, "jit", "-", "--runtime", policy, "--json");
                Assert.That(jitExit == expected, Is.True, $"{jitOutput}{jitError}");
            }
        }
        finally
        {
            await TestFileCleanup.DeleteWithRetryAsync(native).ConfigureAwait(true);
            File.Delete(hosted);
            File.Delete(scanner);
        }
    }
    [TestCase("int32 Main() { if (false) { return 0; } else if (false) { return 1; } }")]
    [TestCase("int32 Main() { if (false) {} else if (true) { Missing(); } return 42; }")]
    [TestCase("int32 Main() { if (true) { return 42; } else if (false) { Missing(); } return 0; }")]
    [TestCase("void Main() { if (true) { return; } else if (false) { Missing(); } }")]
    [TestCase("void Main() { if (false) {} else if (1) {} }")]
    [TestCase("void Main() { if (false) {} else if (true) {} Missing(); }")]
    [TestCase("void Main() { if (false) {} else if (true) {} else if () {} }")]
    [TestCase("int32 Main() { if (true) { int32 x = 1; } else if (x == 1) {} return 42; }")]
    [TestCase("void Main() { if (false) {} else if (true) { _ = int32(1); } require _; }")]
    [TestCase("void Main() { a: while (false) { if (false) {} else if (true) { a: do {} while (false); } } }")]
    [TestCase("void Main() { if (false) {} else if (true) { continue missing; } }")]
    [TestCase("void Main() => if (false) {} else if (true) {}")]
    public async Task NativeElseIfRejectsUnsupportedInactiveSourceAndRetainsPeAsync(string text)
    {
        var path = CreateTestArtifactPath($"else-if-{Guid.NewGuid():N}.exe");
        try
        {
            foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
            {
                var (initial, initialOutput, initialError) = await RunAsync(
                    Encoding.UTF8.GetBytes("int32 Main() { if (false) {} else if (true) {} return 42; }"),
                    "frontend-build", SelfHostPaths.SourceDirectory(RepositoryRoot()), "-", "-o", path, "--runtime", policy, "--json");
                Assert.That(initial == 0, Is.True, $"{initialOutput}{initialError}");
                var bytes = await File.ReadAllBytesAsync(path);
                var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(text), "frontend-build",
                    SelfHostPaths.SourceDirectory(RepositoryRoot()), "-", "-o", path, "--runtime", policy, "--json");
                Assert.That(exit == 1, Is.True, $"{exit}: {output}{error}");
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
    public async Task NativeElseIfPreservesSixNormalizedScalarExportsAsync(string type, string value, int expected)
    {
        var root = RepositoryRoot();
        var native = CreateTestArtifactPath($"else-if-{Guid.NewGuid():N}.native.dll");
        var hosted = CreateTestArtifactPath($"else-if-{Guid.NewGuid():N}.hosted.dll");
        try
        {
            var source = Encoding.UTF8.GetBytes($"public {type} @await() {{ if (false) {{ return {value}; }} else if (true) {{ return {value}; }} else {{ return {value}; }} }}");
            foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
            {
                var (exit, output, error) = await RunAsync(source, "frontend-build-library", SelfHostPaths.SourceDirectory(root),
                    "-", "-o", native, "--runtime", policy, "--json");
                Assert.That(exit == 0, Is.True, $"{output}{error}");
                var (hostExit, hostOutput, hostError) = await RunAsync(source, "build-library", "-", "--export", "await",
                    "-o", hosted, "--runtime", policy, "--json");
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
    [TestCase("int32","","if (false) { return 0; } else if (true) { return 42; } return 1;",0)]
    [TestCase("int32","int32 a","if (false) { return 0; } else if (a == 42) { return a; } return 1;",1)]
    [TestCase("int32","int32 a, int32 b","if (false) { return 0; } else if (a + b == 42) { return a + b; } return 1;",2)]
    [TestCase("int32","int32 a, int32 b, int32 c","if (false) { return 0; } else if (a + b + c == 42) { return a + b + c; } return 1;",3)]
    [TestCase("void","","if (false) {} else if (true) { require true; }",0)]
    public async Task NativeElseIfPreservesParameterAbiAndVoidExportAsync(string type, string parameters, string body, int count)
    {
        var root = RepositoryRoot();
        var native = CreateTestArtifactPath($"else-if-{Guid.NewGuid():N}.native.dll");
        var hosted = CreateTestArtifactPath($"else-if-{Guid.NewGuid():N}.hosted.dll");
        try
        {
            var source = Encoding.UTF8.GetBytes($"public {type} @await({parameters}) {{ {body} }}");
            foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
            {
                var (exit, output, error) = await RunAsync(source, "frontend-build-library", SelfHostPaths.SourceDirectory(root),
                    "-", "-o", native, "--runtime", policy, "--json");
                Assert.That(exit == 0, Is.True, $"{output}{error}");
                var (hostExit, hostOutput, hostError) = await RunAsync(source, "build-library", "-", "--export", "await",
                    "-o", hosted, "--runtime", policy, "--json");
                Assert.That(hostExit == 0, Is.True, $"{hostOutput}{hostError}");
                Assert.That(await File.ReadAllBytesAsync(native), Is.EqualTo(await File.ReadAllBytesAsync(hosted)));
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
                        Assert.That(actual, Is.EqualTo(42));
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
    [TestCase("int32","if (true) { require false; } else if (true) { require false; }")]
    [TestCase("void","for (;;) { if (false) {} else if (true) { continue; } }")]
    [TestCase("int32","a: for (;;) { if (false) {} else if (true) { continue a; } }")]
    public async Task NativeElseIfNonFallthroughCompileOnlyAsync(string type, string body)
    {
        var root = RepositoryRoot();
        var native = CreateTestArtifactPath($"else-if-{Guid.NewGuid():N}.native.exe");
        var hosted = CreateTestArtifactPath($"else-if-{Guid.NewGuid():N}.hosted.exe");
        try
        {
            foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
            {
                var source = Encoding.UTF8.GetBytes($"{type} Main() {{ {body} }}");
                var (exit, output, error) = await RunAsync(source, "frontend-build", SelfHostPaths.SourceDirectory(root),
                    "-", "-o", native, "--runtime", policy, "--json");
                Assert.That(exit == 0, Is.True, $"{output}{error}");
                var (hostExit, hostOutput, hostError) = await RunAsync(source, "build", "-", "-o", hosted, "--runtime", policy, "--json");
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
    [TestCase("depth",6,true)]
    [TestCase("depth",7,false)]
    [TestCase("records",12,true)]
    [TestCase("records",13,false)]
    [TestCase("nodes",64,true)]
    [TestCase("nodes",65,false)]
    [TestCase("locals",8,true)]
    [TestCase("locals",9,false)]
    [TestCase("source",16384,true)]
    [TestCase("source",16385,false)]
    [TestCase("expression",16,true)]
    [TestCase("expression",17,false)]
    [TestCase("tokens",64,true)]
    [TestCase("tokens",65,true)]
    public async Task NativeElseIfRetainsInclusiveResourceCapsAsync(string resource, int count, bool valid)
    {
        const string chain = "if (false) {} else if (true) {}";
        var body = resource switch {
            "depth" => new string('{', count - 2) + chain + new string('}', count - 2),
            "records" => chain + string.Concat(Enumerable.Repeat("require true;", count - 3)),
            "nodes" => "if (" + (valid ? "" : "!")
                + string.Join(" && ", Enumerable.Repeat("true", count / 2))
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
                + "true" + new string(')', 15) + ") {} else if (" + (count == 64 ? "" : "!")
                + "true) {} _ = !true; }";
            Assert.That(Lexer.Lex(SourceText.From(source)).Tokens.Length, Is.EqualTo(count));
        }
        if (resource == "source")
        {
            source += " /*" + new string('x', count - source.Length - 5) + "*/";
            Assert.That(source.Length, Is.EqualTo(count));
        }
        if (resource is not ("tokens" or "nodes"))
        {
            Assert.That(Lexer.Lex(SourceText.From(source)).Tokens.Length <= 64, Is.True);
        }
        foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
        {
            var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(source), "frontend-component",
                SelfHostPaths.SourceDirectory(RepositoryRoot()), "-", "--runtime", policy, "--json");
            Assert.That(exit == (valid ? 0 : resource == "source" ? 2 : 1), Is.True, $"{output}{error}");
            if (valid)
            {
                using var document = JsonDocument.Parse(output);
                var result = document.RootElement.GetProperty("result");
                Assert.That(result.GetProperty("projectionVersion").GetInt32(), Is.EqualTo(87));
                if (resource == "nodes")
                { Assert.That(result.GetProperty("nodes").GetArrayLength(), Is.EqualTo(count)); }
                if (resource == "locals")
                { Assert.That(result.GetProperty("locals").GetArrayLength(), Is.EqualTo(count)); }
                if (resource == "records")
                { Assert.That(result.GetProperty("statements").GetArrayLength(), Is.EqualTo(count)); }
            }
        }
    }
}
