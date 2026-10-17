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
    [TestCase("",0)]
    [TestCase("return;",0)]
    [TestCase("return ();",0)]
    [TestCase("return ((()));",0)]
    [TestCase("/*\U0001F600*/ return ( /*unit*/ ( /*inner*/ ) );",0)]
    [TestCase("require true;",0)]
    [TestCase("require false;",unchecked((int)0xE0540003))]
    [TestCase("mut int32 x = 0; require x++ == 0; require x == 1;",0)]
    [TestCase("mut int32 x = 0; require true || ++x == 1; require x == 0;",0)]
    [TestCase("mut int32 x = 0; require false ? ++x == 1 : ++x == 1; require x == 1;",0)]
    [TestCase("_ = int32(1) / int32(0);",unchecked((int)0xE0540002))]
    [TestCase("_ = int32(2147483647) + int32(1);",unchecked((int)0xE0540001))]
    [TestCase("require int32(1) / int32(0) == int32(0);",unchecked((int)0xE0540002))]
    [TestCase("mut int32 @await = 1; @await += 41; require @await == 42; return;",0)]
    [TestCase("{ _ = true; require true; } if (false) { require false; } else { return (); }",0)]
    [TestCase("mut int32 x = 0; while (x < 2) { x++; } require x == 2;",0)]
    [TestCase("mut int32 x = 0; @const: for (; x < 2; x++) { do { continue const; } while (false); } require x == 2;",0)]
    [TestCase("do { return ((())); } while (true);",0)]
    [TestCase("return; require false;",0)]
    public async Task NativeVoidMatchesHostedScannerPeAndJitAsync(string body, int expected)
    {
        var root = RepositoryRoot();
        var native = CreateTestArtifactPath($"void-{Guid.NewGuid():N}.native.exe");
        var hosted = CreateTestArtifactPath($"void-{Guid.NewGuid():N}.hosted.exe");
        var scanner = CreateTestArtifactPath($"void-{Guid.NewGuid():N}.scanner.exe");
        try
        {
            var source = Encoding.UTF8.GetBytes($"void Main() {{ {body} }}");
            foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
            {
                var (exit, output, error) = await RunAsync(source, "frontend-build",
                    SelfHostPaths.SourceDirectory(root), "-", "-o", native, "--runtime", policy, "--json");
                Assert.That(exit == 0, Is.True, $"{policy}: {output}{error}");
                using var document = JsonDocument.Parse(output);
                Assert.That(document.RootElement.GetProperty("result").GetProperty("projectionVersion").GetInt32(), Is.EqualTo(87));
                var (hostExit, hostOutput, hostError) = await RunAsync(source, "build",
                    "-", "-o", hosted, "--runtime", policy, "--json");
                Assert.That(hostExit == 0, Is.True, $"{hostOutput}{hostError}");
                var (scanExit, scanOutput, scanError) = await RunAsync(source, "build-component",
                    SelfHostPaths.SourceDirectory(root), "-", "-o", scanner,
                    "--runtime", policy, "--json");
                Assert.That(scanExit == 0, Is.True, $"{scanOutput}{scanError}");
                Assert.That(await File.ReadAllBytesAsync(native), Is.EqualTo(await File.ReadAllBytesAsync(hosted)));
                Assert.That(await File.ReadAllBytesAsync(scanner), Is.EqualTo(await File.ReadAllBytesAsync(hosted)));
                Assert.That(await ExecuteNativeImageAsync(native), Is.EqualTo(expected));
                var (runExit, runOutput, runError) = await RunAsync(source, "frontend-run",
                    SelfHostPaths.SourceDirectory(root), "-", "--runtime", policy, "--json");
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
    [TestCase("void Main() { return 1; }")]
    [TestCase("void Main() { return int32(1); }")]
    [TestCase("void Main() { return true; }")]
    [TestCase("void Main() { return null; }")]
    [TestCase("void Main() { return (1); }")]
    [TestCase("void Main() { return Missing(); }")]
    [TestCase("void Main() { return () + (); }")]
    [TestCase("void Main() => ();")]
    [TestCase("void Main() { _ = (); }")]
    [TestCase("void Main() { require (); }")]
    [TestCase("void Main() { void x = (); }")]
    [TestCase("void Main() { var x = (); }")]
    [TestCase("void Main(void x) {}")]
    [TestCase("void Main(int32 x) {}")]
    [TestCase("void Other() {}")]
    [TestCase("void Main() { return; Missing(); }")]
    [TestCase("void Main() { require false; Missing(); }")]
    [TestCase("void Main() { { int32 x = 1; } require x == 1; }")]
    [TestCase("int32 Main() { return; }")]
    [TestCase("int32 Main() { return (); }")]
    [TestCase("int32 Main() {}")]
    public async Task NativeVoidRejectsUnsupportedSourceAndRetainsPeAsync(string text)
    {
        var path = CreateTestArtifactPath($"void-{Guid.NewGuid():N}.exe");
        try
        {
            foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
            {
                var (initial, initialOutput, initialError) = await RunAsync(Encoding.UTF8.GetBytes("void Main() {}"),
                    "frontend-build", SelfHostPaths.SourceDirectory(RepositoryRoot()), "-", "-o", path,
                    "--runtime", policy, "--json");
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
    [TestCase("","")]
    [TestCase("bool a","require a;")]
    [TestCase("int32 a, uint64 b","require a == 42 && b == uint64(42);")]
    [TestCase("char16 a, uint32 b, int64 c","require a == char16(42) && b == uint32(42) && c == int64(42); return ();")]
    public async Task NativeVoidPreservesNormalizedExportAndZeroThroughThreeParameterAbiAsync(string parameters, string body)
    {
        var root = RepositoryRoot();
        var native = CreateTestArtifactPath($"void-{Guid.NewGuid():N}.native.dll");
        var hosted = CreateTestArtifactPath($"void-{Guid.NewGuid():N}.hosted.dll");
        try
        {
            var source = Encoding.UTF8.GetBytes($"public void @await({parameters}) {{ {body} }}");
            foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
            {
                var (exit, output, error) = await RunAsync(source, "frontend-build-library",
                    SelfHostPaths.SourceDirectory(root), "-", "-o", native, "--runtime", policy, "--json");
                Assert.That(exit == 0, Is.True, $"{output}{error}");
                var (hostExit, hostOutput, hostError) = await RunAsync(source, "build-library",
                    "-", "--export", "await", "-o", hosted, "--runtime", policy, "--json");
                Assert.That(hostExit == 0, Is.True, $"{hostOutput}{hostError}");
                Assert.That(await File.ReadAllBytesAsync(native), Is.EqualTo(await File.ReadAllBytesAsync(hosted)));
                var handle = NativeLibrary.Load(native);
                try
                {
                    var export = NativeLibrary.GetExport(handle, "await");
                    switch (parameters)
                    {
                        case "":
                            Marshal.GetDelegateForFunctionPointer<NativeVoidZero>(export)();
                            break;
                        case "bool a":
                            Marshal.GetDelegateForFunctionPointer<NativeVoidOne>(export)(true);
                            break;
                        case "int32 a, uint64 b":
                            Marshal.GetDelegateForFunctionPointer<NativeVoidTwo>(export)(42, 42);
                            break;
                        default:
                            Marshal.GetDelegateForFunctionPointer<NativeVoidThree>(export)(42, 42, 42);
                            break;
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

    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate void NativeVoidZero();
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate void NativeVoidOne([MarshalAs(UnmanagedType.I1)] bool a);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate void NativeVoidTwo(int a, ulong b);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate void NativeVoidThree(ushort a, uint b, long c);
    [TestCase("while (true) { continue; }")]
    [TestCase("for (;;) {}")]
    [TestCase("a: for (;;) { do { continue a; } while (false); }")]
    [TestCase("require false;")]
    public async Task NativeVoidNonFallthroughCompileOnlyAsync(string body)
    {
        var root = RepositoryRoot();
        var native = CreateTestArtifactPath($"void-{Guid.NewGuid():N}.native.exe");
        var hosted = CreateTestArtifactPath($"void-{Guid.NewGuid():N}.hosted.exe");
        try
        {
            var source = Encoding.UTF8.GetBytes($"void Main() {{ {body} }}");
            foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
            {
                var (exit, output, error) = await RunAsync(source, "frontend-build",
                    SelfHostPaths.SourceDirectory(root), "-", "-o", native, "--runtime", policy, "--json");
                Assert.That(exit == 0, Is.True, $"{output}{error}");
                var (hostExit, hostOutput, hostError) = await RunAsync(source, "build",
                    "-", "-o", hosted, "--runtime", policy, "--json");
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
    [TestCase("records",12,true)]
    [TestCase("records",13,false)]
    [TestCase("locals",8,true)]
    [TestCase("locals",9,false)]
    [TestCase("depth",6,true)]
    [TestCase("depth",7,false)]
    [TestCase("expression",16,true)]
    [TestCase("expression",17,false)]
    [TestCase("nodes",64,true)]
    [TestCase("nodes",65,false)]
    [TestCase("source",16384,true)]
    [TestCase("source",16385,false)]
    [TestCase("tokens",64,true)]
    [TestCase("tokens",65,true)]
    public async Task NativeVoidRetainsOtherResourceCapsAsync(string resource, int count, bool valid)
    {
        var body = resource switch {
            "records" => string.Concat(Enumerable.Repeat("return; ", count)),
            "locals" => string.Concat(Enumerable.Repeat("_ = true; ", count)),
            "depth" => new string('{', count) + "return;" + new string('}', count),
            "expression" => "return " + new string('(', count) + new string(')', count) + ";",
            "nodes" => "require " + (count == 65 ? "!" : "")
                + string.Join(" && ", Enumerable.Repeat("true", count / 2)) + "; _ = true;",
            "tokens" => string.Concat(Enumerable.Range(0, 7).Select(i => $"int32 x{i} = {i}; "))
                + "require !true; return " + new string('(', 8) + new string(')', 8) + ";",
            _ => "",
        };
        var source = $"void Main() {{ {body} }}";
        if (resource == "source")
        {
            source += " /*" + new string('x', count - source.Length - 5) + "*/";
            Assert.That(source.Length, Is.EqualTo(count));
        }
        if (resource == "tokens")
        {
            if (count == 65)
            { source = source.Replace("x0 = 0", "x0 = +0", StringComparison.Ordinal); }
            Assert.That(Lexer.Lex(SourceText.From(source)).Tokens.Length, Is.EqualTo(count));
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
                if (resource == "locals")
                { Assert.That(result.GetProperty("locals").GetArrayLength(), Is.EqualTo(count)); }
                if (resource == "records")
                { Assert.That(result.GetProperty("statements").GetArrayLength(), Is.EqualTo(count)); }
                if (resource == "nodes")
                {
                    Assert.That(Lexer.Lex(SourceText.From(source)).Tokens.Length > 64, Is.True);
                    Assert.That(result.GetProperty("nodes").GetArrayLength(), Is.EqualTo(count));
                }
            }
        }
    }

    [Test]
    public async Task NativeVoidParsesFunctionsBeyondThePreviousTokenWindowAsync()
    {
        var longCondition = "require " + new string('(', 15) + "true" + new string(')', 15) + "; ";
        var source = "void Main() { " + longCondition
            + string.Concat(Enumerable.Repeat("require true; ", 11)) + "}";
        Assert.That(Lexer.Lex(SourceText.From(source)).Tokens.Length > 64, Is.True);

        var directory = SelfHostPaths.SourceDirectory(RepositoryRoot());
        foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
        {
            var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(source),
                "frontend-component", directory, "-", "--runtime", policy, "--json");
            Assert.That(exit == 0, Is.True, $"{policy}: {output}{error}");
            using var document = JsonDocument.Parse(output);
            var result = document.RootElement.GetProperty("result");
            Assert.That(result.GetProperty("projectionVersion").GetInt32(), Is.EqualTo(87));
            Assert.That(result.GetProperty("statements").GetArrayLength(), Is.EqualTo(12));
        }
    }
}
