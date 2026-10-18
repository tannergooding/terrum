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
    [TestCase("[NoThrow] int32 Main() { return 40 + 2; }",42)]
    [TestCase("[NoThrow] int32 Main() => int32(84) / int32(2);",42)]
    [TestCase("[NoThrow] int32 Main() { return int32(21) << 1; }",42)]
    [TestCase("[NoThrow] int32 Main() { if (int32(1) == int32(1)) { return 42; } }",42)]
    [TestCase("[NoThrow] int32 Main() { if (false) { return 0; } else if (true) { return 42; } return 1; }",42)]
    [TestCase("[NoThrow] int32 Main() { int32 x = 40; return x | 2; }",42)]
    [TestCase("[NoThrow] int32 Main() { mut int32 x = 0; { x = 42; } while (false) {} return x; }",42)]
    [TestCase("[NoThrow] void Main() {}",0)]
    [TestCase("[NoThrow] void Main() { return (); }",0)]
    [TestCase("[NoThrow] void Main() => require int32(1) + int32(1) == 2;",0)]
    [TestCase("[NoThrow] void Main() => _ = int32(84) / int32(2);",0)]
    [TestCase("[NoThrow] void Main() { _ = uint64(0xFFFFFFFFFFFFFFFF); require true || false; }",0)]
    [TestCase("[NoThrow] void Main() { if (false) {} else if (true) { require true ? true : false; } }",0)]
    [TestCase("[NoThrow] int32 Main() { mut int32 x = 0; a: do { x = 42; break a; } while (true); return x; }",42)]
    [TestCase("[DoesNotReturn] void Main() { require false; }",unchecked((int)0xE0540003))]
    [TestCase("[DoesNotReturn] void Main() => require false;",unchecked((int)0xE0540003))]
    [TestCase("[DoesNotReturn] void Main() { require false; return; }",unchecked((int)0xE0540003))]
    [TestCase("[DoesNotReturn] void Main() { if (true) { require false; } else if (false) { return; } }",unchecked((int)0xE0540003))]
    [TestCase("[DoesNotReturn] void Main() { require int32(1) / int32(0) == 0; require false; }",unchecked((int)0xE0540002))]
    public async Task NativeFunctionEffectsMatchHostedScannerPeAndJitAsync(string text, int expected)
    {
        var root = RepositoryRoot();
        var native = CreateTestArtifactPath($"effects-{Guid.NewGuid():N}.native.exe");
        var hosted = CreateTestArtifactPath($"effects-{Guid.NewGuid():N}.hosted.exe");
        var scanner = CreateTestArtifactPath($"effects-{Guid.NewGuid():N}.scanner.exe");
        try
        {
            var source = Encoding.UTF8.GetBytes($"/*\U0001F600*/ {text} //tail");
            foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
            {
                var (exit, output, error) = await RunAsync(source, "frontend-build", TerrumProjectPaths.SourceDirectory(root),
                    "-", "-o", native, "--runtime", policy, "--json");
                Assert.That(exit == 0, Is.True, $"{output}{error}");
                using var document = JsonDocument.Parse(output);
                Assert.That(document.RootElement.GetProperty("result").GetProperty("projectionVersion").GetInt32(), Is.EqualTo(87));
                var (hostExit, hostOutput, hostError) = await RunAsync(source, "build", "-", "-o", hosted, "--runtime", policy, "--json");
                Assert.That(hostExit == 0, Is.True, $"{hostOutput}{hostError}");
                var (scanExit, scanOutput, scanError) = await RunAsync(source, "build-component", TerrumProjectPaths.SourceDirectory(root),
                    "-", "-o", scanner, "--runtime", policy, "--json");
                Assert.That(scanExit == 0, Is.True, $"{scanOutput}{scanError}");
                Assert.That(await File.ReadAllBytesAsync(native), Is.EqualTo(await File.ReadAllBytesAsync(hosted)));
                Assert.That(await File.ReadAllBytesAsync(scanner), Is.EqualTo(await File.ReadAllBytesAsync(hosted)));
                Assert.That(await ExecuteNativeImageAsync(native), Is.EqualTo(expected));
                var (runExit, runOutput, runError) = await RunAsync(source, "frontend-run", TerrumProjectPaths.SourceDirectory(root),
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
    [TestCase("[NoThrow] int32 Main() { mut int32 x = 1; return x + 1; }")]
    [TestCase("[NoThrow] void Main() { require false; }")]
    [TestCase("[NoThrow] void Main() => require false;")]
    [TestCase("[NoThrow] void Main() { _ = int32(2147483647) + int32(1); }")]
    [TestCase("[NoThrow] void Main() { _ = char16(65536); }")]
    [TestCase("[NoThrow] void Main() { mut int32 x = 0; x++; }")]
    [TestCase("[NoThrow] void Main() { require false && int32(1) / int32(0) == 0; }")]
    [TestCase("[NoThrow] void Main() { _ = true ? int32(42) : int32(1) / int32(0); }")]
    [TestCase("[DoesNotReturn] int32 Main() { return 42; }")]
    [TestCase("[DoesNotReturn] void Main() {}")]
    [TestCase("[DoesNotReturn] void Main() { return; }")]
    [TestCase("[DoesNotReturn] void Main() { return (); }")]
    [TestCase("[DoesNotReturn] void Main() => require true;")]
    [TestCase("[NoThrow][DoesNotReturn] void Main() { require false; }")]
    [TestCase("[NoThrow][NoThrow] void Main() {}")]
    [TestCase("[DoesNotReturn][DoesNotReturn] void Main() { require false; }")]
    [TestCase("[nothrow] void Main() {}")]
    [TestCase("[NoＴhrow] void Main() {}")]
    [TestCase("[@NoThrow] void Main() {}")]
    [TestCase("[NoThrow(value)] void Main() {}")]
    [TestCase("[NoThrow()] void Main() {}")]
    [TestCase("[Cleanup] void Main() {}")]
    [TestCase("[ConstEval] void Main() {}")]
    [TestCase("public [NoThrow] void Main() {}")]
    [TestCase("[NoThrow] void Main() { return; Missing(); }")]
    [TestCase("[DoesNotReturn] void Main() { require false; Missing(); }")]
    public async Task NativeFunctionEffectsRejectInvalidEffectsAndRetainPeAsync(string text)
    {
        var path = CreateTestArtifactPath($"effects-{Guid.NewGuid():N}.exe");
        try
        {
            foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
            {
                var (initial, initialOutput, initialError) = await RunAsync(
                    Encoding.UTF8.GetBytes("[NoThrow] int32 Main() => 42;"), "frontend-build",
                    TerrumProjectPaths.SourceDirectory(RepositoryRoot()), "-", "-o", path, "--runtime", policy, "--json");
                Assert.That(initial == 0, Is.True, $"{initialOutput}{initialError}");
                var bytes = await File.ReadAllBytesAsync(path);
                var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(text), "frontend-build",
                    TerrumProjectPaths.SourceDirectory(RepositoryRoot()), "-", "-o", path, "--runtime", policy, "--json");
                Assert.That(exit == 1, Is.True, $"{output}{error}");
                Assert.That(await File.ReadAllBytesAsync(path), Is.EqualTo(bytes));
            }
        }
        finally
        {
            File.Delete(path);
        }
    }
    [TestCase("int32","int32(40) + int32(2)",42)]
    [TestCase("uint32","uint32(84) / uint32(2)",42)]
    [TestCase("int64","int64(21) << 1",42)]
    [TestCase("uint64","uint64(40) | uint64(2)",42)]
    [TestCase("char16","char16(42)",42)]
    [TestCase("bool","true && !false",1)]
    [TestCase("void","require true",0)]
    public async Task NativeFunctionEffectsPreserveNormalizedSixScalarAndVoidExportsAsync(string type, string expression, int expected)
    {
        var root = RepositoryRoot();
        var native = CreateTestArtifactPath($"effects-{Guid.NewGuid():N}.native.dll");
        var hosted = CreateTestArtifactPath($"effects-{Guid.NewGuid():N}.hosted.dll");
        try
        {
            var source = Encoding.UTF8.GetBytes($"[NoThrow] public {type} @await() => {expression};");
            foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
            {
                var (exit, output, error) = await RunAsync(source, "frontend-build-library", TerrumProjectPaths.SourceDirectory(root),
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

    [Test]
    public async Task NativeNoThrowSupportsParameterizedScalarFunctionsAsync()
    {
        const string text = "[NoThrow] public int32 Identity(int32 value) { return value; }";
        var root = RepositoryRoot();
        var frontend = TerrumProjectPaths.SourceDirectory(root);
        var native = CreateTestArtifactPath($"effects-{Guid.NewGuid():N}.native.dll");
        var hosted = CreateTestArtifactPath($"effects-{Guid.NewGuid():N}.hosted.dll");
        try
        {
            foreach (var runtimeName in new[] { "fail-fast", "checked-exceptions" })
            {
                var (componentExit, componentOutput, componentError) = await RunAsync(
                    Encoding.UTF8.GetBytes(text), "frontend-component", frontend, "-",
                    "--runtime", runtimeName, "--json");
                Assert.That(componentExit, Is.EqualTo(0));
                Assert.That(componentError, Is.Empty);
                using (var document = JsonDocument.Parse(componentOutput))
                {
                    var result = document.RootElement.GetProperty("result");
                    Assert.That(result.GetProperty("valid").GetBoolean(), Is.True);
                    Assert.That(result.GetProperty("noThrow").GetBoolean(), Is.True);
                    Assert.That(result.GetProperty("projectionVersion").GetInt32(), Is.EqualTo(87));
                }

                var (buildExit, buildOutput, buildError) = await RunAsync(Encoding.UTF8.GetBytes(text),
                    "frontend-build-library", frontend, "-", "-o", native, "--runtime", runtimeName, "--json");
                Assert.That(buildExit == 0, Is.True, $"{runtimeName}: {buildOutput}{buildError}");
                var (hostExit, hostOutput, hostError) = await RunAsync(Encoding.UTF8.GetBytes(text),
                    "build-library", "-", "--export", "Identity", "-o", hosted, "--runtime", runtimeName, "--json");
                Assert.That(hostExit == 0, Is.True, $"{runtimeName}: {hostOutput}{hostError}");
                Assert.That(await File.ReadAllBytesAsync(native), Is.EqualTo(await File.ReadAllBytesAsync(hosted)));

                var handle = NativeLibrary.Load(native);
                try
                {
                    var invoke = Marshal.GetDelegateForFunctionPointer<ParameterizedIdentity>(
                        NativeLibrary.GetExport(handle, "Identity"));
                    Assert.That(invoke(42), Is.EqualTo(42));
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

    [Test]
    public async Task NativeDoesNotReturnSupportsParameterizedScalarFunctionsAsync()
    {
        const string text = "[DoesNotReturn] public void Stop(int32 value) { require false; }";
        var root = RepositoryRoot();
        var frontend = TerrumProjectPaths.SourceDirectory(root);
        var native = CreateTestArtifactPath($"effects-{Guid.NewGuid():N}.native.dll");
        var hosted = CreateTestArtifactPath($"effects-{Guid.NewGuid():N}.hosted.dll");
        try
        {
            foreach (var runtimeName in new[] { "fail-fast", "checked-exceptions" })
            {
                var (componentExit, componentOutput, componentError) = await RunAsync(
                    Encoding.UTF8.GetBytes(text), "frontend-component", frontend, "-",
                    "--runtime", runtimeName, "--json");
                Assert.That(componentExit, Is.EqualTo(0));
                Assert.That(componentError, Is.Empty);
                using (var document = JsonDocument.Parse(componentOutput))
                {
                    var result = document.RootElement.GetProperty("result");
                    Assert.That(result.GetProperty("valid").GetBoolean(), Is.True);
                    Assert.That(result.GetProperty("doesNotReturn").GetBoolean(), Is.True);
                    Assert.That(result.GetProperty("projectionVersion").GetInt32(), Is.EqualTo(87));
                }

                var (buildExit, buildOutput, buildError) = await RunAsync(Encoding.UTF8.GetBytes(text),
                    "frontend-build-library", frontend, "-", "-o", native, "--runtime", runtimeName, "--json");
                Assert.That(buildExit == 0, Is.True, $"{runtimeName}: {buildOutput}{buildError}");
                var (hostExit, hostOutput, hostError) = await RunAsync(Encoding.UTF8.GetBytes(text),
                    "build-library", "-", "--export", "Stop", "-o", hosted, "--runtime", runtimeName, "--json");
                Assert.That(hostExit == 0, Is.True, $"{runtimeName}: {hostOutput}{hostError}");
                Assert.That(await File.ReadAllBytesAsync(native), Is.EqualTo(await File.ReadAllBytesAsync(hosted)));
            }
        }
        finally
        {
            File.Delete(native);
            File.Delete(hosted);
        }
    }
    [TestCase("[DoesNotReturn] void Main() { while (true) {} }")]
    [TestCase("[NoThrow][DoesNotReturn] void Main() { for (;;) {} }")]
    [TestCase("[DoesNotReturn][NoThrow] void Main() { for (;;) {} }")]
    [TestCase("[NoThrow][DoesNotReturn] void Main() { if (false) { return; } else if (true) { for (;;) {} } }")]
    public async Task NativeFunctionEffectsNonFallthroughCompileOnlyAsync(string text)
    {
        var root = RepositoryRoot();
        var native = CreateTestArtifactPath($"effects-{Guid.NewGuid():N}.native.exe");
        var hosted = CreateTestArtifactPath($"effects-{Guid.NewGuid():N}.hosted.exe");
        try
        {
            foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
            {
                var source = Encoding.UTF8.GetBytes(text);
                var (exit, output, error) = await RunAsync(source, "frontend-build", TerrumProjectPaths.SourceDirectory(root),
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
    [TestCase(64,true)]
    [TestCase(65,true)]
    public async Task NativeFunctionEffectsAcceptAttributeTokensBeyondThePreviousWindowAsync(int count, bool valid)
    {
        var source = "[NoThrow] public void @await(bool a, bool b, bool c) { require "
            + new string('(', 15) + "true" + new string(')', 15)
            + "; require true; require true; require true; require "
            + (count == 64 ? "true" : "!false") + "; }";
        Assert.That(Lexer.Lex(SourceText.From(source)).Tokens.Length, Is.EqualTo(count));
        var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(source), "frontend-component",
            TerrumProjectPaths.SourceDirectory(RepositoryRoot()), "-", "--runtime", "fail-fast", "--json");
        Assert.That(exit == (valid ? 0 : 1), Is.True, $"{output}{error}");
    }
    [TestCase(false,0)]
    [TestCase(false,1)]
    [TestCase(false,2)]
    [TestCase(false,3)]
    [TestCase(true,0)]
    [TestCase(true,1)]
    [TestCase(true,2)]
    [TestCase(true,3)]
    public async Task NativeFunctionEffectsPreserveZeroThroughThreeParameterAbiAsync(bool isVoid, int count)
    {
        var root = RepositoryRoot();
        var native = CreateTestArtifactPath($"effects-{Guid.NewGuid():N}.native.dll");
        var hosted = CreateTestArtifactPath($"effects-{Guid.NewGuid():N}.hosted.dll");
        var parameters = isVoid ? count switch {
            0 => "",
            1 => "bool a",
            2 => "int32 a, uint64 b",
            _ => "char16 a, uint32 b, int64 c",
        } : string.Join(", ", Enumerable.Range(0, count).Select(index => $"int32 {(char)('a' + index)}"));
        var expression = isVoid ? "require true" : count switch {
            0 => "42",
            1 => "a",
            2 => "a | b",
            _ => "a | b | c",
        };
        try
        {
            var source = Encoding.UTF8.GetBytes($"[NoThrow] public {(isVoid ? "void" : "int32")} @await({parameters}) => {expression};");
            foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
            {
                var (exit, output, error) = await RunAsync(source, "frontend-build-library", TerrumProjectPaths.SourceDirectory(root),
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
                    if (isVoid)
                    {
                        switch (count)
                        {
                            case 0:
                                Marshal.GetDelegateForFunctionPointer<NativeVoidZero>(export)();
                                break;
                            case 1:
                                Marshal.GetDelegateForFunctionPointer<NativeVoidOne>(export)(true);
                                break;
                            case 2:
                                Marshal.GetDelegateForFunctionPointer<NativeVoidTwo>(export)(42, ulong.MaxValue);
                                break;
                            default:
                                Marshal.GetDelegateForFunctionPointer<NativeVoidThree>(export)(65535, uint.MaxValue, long.MinValue);
                                break;
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
    [TestCase("source",16384,true)]
    [TestCase("source",16385,false)]
    [TestCase("records",12,true)]
    [TestCase("records",13,false)]
    [TestCase("locals",8,true)]
    [TestCase("locals",9,false)]
    [TestCase("nodes",64,true)]
    [TestCase("nodes",65,false)]
    [TestCase("expression",16,true)]
    [TestCase("expression",17,false)]
    [TestCase("depth",6,true)]
    [TestCase("depth",7,false)]
    public async Task NativeFunctionEffectsRetainInclusiveResourceCapsAsync(string resource, int count, bool valid)
    {
        var body = resource switch {
            "records" => string.Concat(Enumerable.Repeat("require true;", count)),
            "locals" => string.Concat(Enumerable.Repeat("_ = true;", count)),
            "nodes" => "_ = !false || " + (valid ? "" : "!")
                + string.Join(" || ", Enumerable.Repeat("true", (count - 2) / 2)) + ";",
            "expression" => "require " + new string('(', count - 1) + "true" + new string(')', count - 1) + ";",
            "depth" => new string('{', count) + "require true;" + new string('}', count),
            _ => "require true;",
        };
        var text = $"[NoThrow] void Main() {{ {body} }}";
        if (resource == "source")
        {
            text += " /*" + new string('x', count - text.Length - 5) + "*/";
            Assert.That(text.Length, Is.EqualTo(count));
        }
        if (resource != "nodes")
        {
            Assert.That(Lexer.Lex(SourceText.From(text)).Tokens.Length <= 64, Is.True);
        }
        foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
        {
            var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(text), "frontend-component",
                TerrumProjectPaths.SourceDirectory(RepositoryRoot()), "-", "--runtime", policy, "--json");
            Assert.That(exit == (valid ? 0 : resource == "source" ? 2 : 1), Is.True, $"{output}{error}");
            if (valid)
            {
                using var document = JsonDocument.Parse(output);
                var result = document.RootElement.GetProperty("result");
                Assert.That(result.GetProperty("projectionVersion").GetInt32(), Is.EqualTo(87));
                if (resource == "nodes")
                { Assert.That(result.GetProperty("nodes").GetArrayLength(), Is.EqualTo(count)); }
                if (resource == "records")
                { Assert.That(result.GetProperty("statements").GetArrayLength(), Is.EqualTo(count)); }
                if (resource == "locals")
                { Assert.That(result.GetProperty("locals").GetArrayLength(), Is.EqualTo(count)); }
            }
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int ParameterizedIdentity(int value);
}
