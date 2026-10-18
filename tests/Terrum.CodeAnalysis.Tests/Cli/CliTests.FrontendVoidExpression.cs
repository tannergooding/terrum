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

using Terrum.CodeAnalysis.Tests.Infrastructure;

namespace Terrum.CodeAnalysis.Tests.Cli;

public sealed partial class CliTests
{
    [TestCase("require true;",0)]
    [TestCase("require false;",unchecked((int)0xE0540003))]
    [TestCase("require true || int32(1) / int32(0) == 0;",0)]
    [TestCase("require false && int32(1) / int32(0) == 0;",unchecked((int)0xE0540003))]
    [TestCase("require int32(1) / int32(0) == 0;",unchecked((int)0xE0540002))]
    [TestCase("require true ? true : int32(1) / int32(0) == 0;",0)]
    [TestCase("require true ? false : true;",unchecked((int)0xE0540003))]
    [TestCase("_ = true;",0)]
    [TestCase("_ = int32(42);",0)]
    [TestCase("_ = uint32(0xFFFF_FFFF);",0)]
    [TestCase("_ = int64(-9223372036854775808);",0)]
    [TestCase("_ = uint64(18446744073709551615);",0)]
    [TestCase("_ = char16(65535);",0)]
    [TestCase("_ = int32(2147483647) + int32(1);",unchecked((int)0xE0540001))]
    [TestCase("_ = int32(1) / int32(0);",unchecked((int)0xE0540002))]
    [TestCase("_ = false && int32(1) / int32(0) == 0;",0)]
    [TestCase("_ = false ? int32(1) / int32(0) : int32(42);",0)]
    [TestCase("_ = int32(uint64(4294967296));",unchecked((int)0xE0540001))]
    [TestCase("/*\U0001F600*/ _ = (char16(0xFFFF));",0)]
    public async Task NativeVoidExpressionMatchesHostedScannerPeAndJitAsync(string statement, int expected)
    {
        var root = RepositoryRoot();
        var native = CreateTestArtifactPath($"void-expression-{Guid.NewGuid():N}.native.exe");
        var hosted = CreateTestArtifactPath($"void-expression-{Guid.NewGuid():N}.hosted.exe");
        var scanner = CreateTestArtifactPath($"void-expression-{Guid.NewGuid():N}.scanner.exe");
        try
        {
            var source = Encoding.UTF8.GetBytes($"void Main() => {statement} /*trailing*/");
            foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
            {
                var (exit, output, error) = await RunAsync(source, "frontend-build",
                    TerrumProjectPaths.SourceDirectory(root), "-", "-o", native, "--runtime", policy, "--json");
                Assert.That(exit == 0, Is.True, $"{policy}: {output}{error}");
                using var document = JsonDocument.Parse(output);
                var result = document.RootElement.GetProperty("result");
                Assert.That(result.GetProperty("projectionVersion").GetInt32(), Is.EqualTo(87));
                Assert.That(result.GetProperty("expressionBody").GetBoolean(), Is.True);
                var (hostExit, hostOutput, hostError) = await RunAsync(source, "build",
                    "-", "-o", hosted, "--runtime", policy, "--json");
                Assert.That(hostExit == 0, Is.True, $"{hostOutput}{hostError}");
                var (scanExit, scanOutput, scanError) = await RunAsync(source, "build-component",
                    TerrumProjectPaths.SourceDirectory(root), "-", "-o", scanner, "--runtime", policy, "--json");
                Assert.That(scanExit == 0, Is.True, $"{scanOutput}{scanError}");
                Assert.That(await File.ReadAllBytesAsync(native), Is.EqualTo(await File.ReadAllBytesAsync(hosted)));
                Assert.That(await File.ReadAllBytesAsync(scanner), Is.EqualTo(await File.ReadAllBytesAsync(hosted)));
                Assert.That(await ExecuteNativeImageAsync(native), Is.EqualTo(expected));
                var (runExit, runOutput, runError) = await RunAsync(source, "frontend-run",
                    TerrumProjectPaths.SourceDirectory(root), "-", "--runtime", policy, "--json");
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
    [TestCase("void Main() => ();")]
    [TestCase("void Main() => return;")]
    [TestCase("void Main() => return ();")]
    [TestCase("void Main() => 42;")]
    [TestCase("void Main() => Missing();")]
    [TestCase("void Main() => _ = Missing();")]
    [TestCase("void Main() => _ = ();")]
    [TestCase("void Main() => _ = 42;")]
    [TestCase("void Main() => _ = null;")]
    [TestCase("void Main() => require 1;")]
    [TestCase("void Main() => require ();")]
    [TestCase("void Main() => static require true;")]
    [TestCase("void Main() => require true, 1;")]
    [TestCase("void Main() => require true; require true;")]
    [TestCase("void Main() => _ = true; return;")]
    [TestCase("void Main() => require true")]
    [TestCase("void Main() => { require true; }")]
    [TestCase("void Main() => _ += int32(1);")]
    [TestCase("void Main() => _ = _;")]
    [TestCase("int32 Main() => require true;")]
    [TestCase("int32 Main() => _ = int32(42);")]
    [TestCase("void Main()=> require true;")]
    [TestCase("void Main() =>require true;")]
    [TestCase("void Main() => _= true;")]
    [TestCase("void Main() => _ =true;")]
    public async Task NativeVoidExpressionRejectsUnsupportedSourceAndRetainsPeAsync(string text)
    {
        var path = CreateTestArtifactPath($"void-expression-{Guid.NewGuid():N}.exe");
        try
        {
            foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
            {
                var (initial, initialOutput, initialError) = await RunAsync(Encoding.UTF8.GetBytes("void Main() => require true;"),
                    "frontend-build", TerrumProjectPaths.SourceDirectory(RepositoryRoot()), "-", "-o", path, "--runtime", policy, "--json");
                Assert.That(initial == 0, Is.True, $"{initialOutput}{initialError}");
                var bytes = await File.ReadAllBytesAsync(path);
                var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(text), "frontend-build",
                    TerrumProjectPaths.SourceDirectory(RepositoryRoot()), "-", "-o", path, "--runtime", policy, "--json");
                Assert.That(exit == 1, Is.True, $"{exit}: {output}{error}");
                Assert.That(await File.ReadAllBytesAsync(path), Is.EqualTo(bytes));
            }
        }
        finally
        {
            File.Delete(path);
        }
    }
    [TestCase("","_ = true;")]
    [TestCase("bool @const","require const;")]
    [TestCase("int32 a, uint64 b","require a > 0 && b == uint64(42);")]
    [TestCase("char16 a, uint32 b, int64 c","_ = a == char16(42) && b == uint32(42) && c == int64(42);")]
    [TestCase("int32 x","_ = x + 1;")]
    public async Task NativeVoidExpressionPreservesNormalizedExportAndPrimitiveParameterAbiAsync(string parameters, string statement)
    {
        var root = RepositoryRoot();
        var native = CreateTestArtifactPath($"void-expression-{Guid.NewGuid():N}.native.dll");
        var hosted = CreateTestArtifactPath($"void-expression-{Guid.NewGuid():N}.hosted.dll");
        try
        {
            var source = Encoding.UTF8.GetBytes($"public void @await({parameters}) => {statement}");
            foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
            {
                var (exit, output, error) = await RunAsync(source, "frontend-build-library",
                    TerrumProjectPaths.SourceDirectory(root), "-", "-o", native, "--runtime", policy, "--json");
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
                        case "bool @const":
                            Marshal.GetDelegateForFunctionPointer<NativeVoidOne>(export)(true);
                            break;
                        case "int32 x":
                            Marshal.GetDelegateForFunctionPointer<VoidExpressionInt32>(export)(42);
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

    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate void VoidExpressionInt32(int x);
    [TestCase("x = 1;")]
    [TestCase("x++;")]
    [TestCase("_ = x++;")]
    [TestCase("require ++x > 0;")]
    [TestCase("int32 y = x;")]
    public async Task NativeVoidExpressionRejectsUnsupportedReadonlyParameterActionsAsync(string statement)
    {
        foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
        {
            var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes($"public void F(int32 x) => {statement}"),
                "frontend-component", TerrumProjectPaths.SourceDirectory(RepositoryRoot()), "-", "--runtime", policy, "--json");
            Assert.That(exit == 1, Is.True, $"{output}{error}");
        }
    }
    [TestCase("require false;")]
    [TestCase("require !true;")]
    public async Task NativeVoidExpressionNonFallthroughCompileOnlyAsync(string statement)
    {
        var root = RepositoryRoot();
        var native = CreateTestArtifactPath($"void-expression-{Guid.NewGuid():N}.native.exe");
        var hosted = CreateTestArtifactPath($"void-expression-{Guid.NewGuid():N}.hosted.exe");
        try
        {
            foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
            {
                var source = Encoding.UTF8.GetBytes($"void Main() => {statement}");
                var (exit, output, error) = await RunAsync(source, "frontend-build",
                    TerrumProjectPaths.SourceDirectory(root), "-", "-o", native, "--runtime", policy, "--json");
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
    [TestCase("nodes",64,true)]
    [TestCase("nodes",65,false)]
    [TestCase("tokens",64,true)]
    [TestCase("tokens",65,true)]
    [TestCase("source",16384,true)]
    [TestCase("source",16385,false)]
    [TestCase("expression",16,true)]
    [TestCase("expression",17,false)]
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
        if (resource == "tokens")
        { Assert.That(Lexer.Lex(SourceText.From(source)).Tokens.Length, Is.EqualTo(count)); }
        if (resource == "source")
        {
            source += " /*" + new string('x', count - source.Length - 5) + "*/";
            Assert.That(source.Length, Is.EqualTo(count));
        }
        foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
        {
            var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(source), "frontend-component",
                TerrumProjectPaths.SourceDirectory(RepositoryRoot()), "-", "--runtime", policy, "--json");
            Assert.That(exit == (valid ? 0 : resource == "source" ? 2 : 1), Is.True, $"{output}{error}");
            if (valid)
            {
                using var document = JsonDocument.Parse(output);
                var result = document.RootElement.GetProperty("result");
                Assert.That(result.GetProperty("projectionVersion").GetInt32(), Is.EqualTo(87));
                Assert.That(result.GetProperty("statements").EnumerateArray(), Has.Exactly(1).Items);
                if (resource == "nodes")
                { Assert.That(result.GetProperty("nodes").GetArrayLength(), Is.EqualTo(count)); }
            }
        }
    }
}
