// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using NUnit.Framework;

using Terrum.CodeAnalysis.Tests.Infrastructure;

namespace Terrum.CodeAnalysis.Tests.Cli;

public sealed partial class CliTests
{
    [TestCase("checked-exceptions",0,"native-11")]
    [TestCase("fail-fast",1,"native-10")]
    public async Task AnalyzeUsesTheExplicitRecoveryPolicyAsync(string policy, int expected, string profile)
    {
        const string Source = "int32 Main() { unsafe { try { require false; } catch { return 42; } } return 0; }";
        var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(Source),
            "analyze", "-", "--runtime", policy, "--json");
        Assert.That(exit, Is.EqualTo(expected));
        Assert.That(error, Is.Empty);
        using var document = JsonDocument.Parse(output);
        Assert.That(document.RootElement.GetProperty("profile").GetString(), Is.EqualTo(profile));
        Assert.That(document.RootElement.GetProperty("success").GetBoolean(), Is.EqualTo(expected == 0));
        Assert.That(document.RootElement.TryGetProperty("artifact", out _), Is.False);
        if (expected != 0)
        {
            Assert.That(global::System.Linq.Enumerable.Any(document.RootElement.GetProperty("diagnostics").EnumerateArray(),diagnostic => diagnostic.GetProperty("code").GetString() == "TRM3022"),Is.True);
        }
    }
    [TestCase("fail-fast")]
    [TestCase("checked-exceptions")]
    public async Task AnalyzeReportsCompactUniqueProfileAsync(string policy)
    {
        const string Source = """
            unique struct Token {
                public int32 Id;
                public Token() { Id = 42; }
                [NoThrow] [Cleanup] public void Close() {}
            }
            int32 Main() { Token value = new Token(); return value.Id; }
            """;
        var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(Source),
            "analyze", "-", "--runtime", policy, "--json");
        Assert.That(exit, Is.EqualTo(0));
        Assert.That(error, Is.Empty);
        using var document = JsonDocument.Parse(output);
        Assert.That(document.RootElement.GetProperty("profile").GetString(), Is.EqualTo("native-12"));
        Assert.That(document.RootElement.GetProperty("runtimePolicy").GetString(), Is.EqualTo(policy));
    }
    [TestCase(false)]
    [TestCase(true)]
    public async Task ModulePolicyIsNotReadFromAnOptionLikeSourceOperandAsync(bool native)
    {
        var directory = TestArtifactPaths.CreateDirectory("terrum-policy-operand-");
        var source = Path.Combine(directory, "--runtime");
        var artifact = Path.Combine(directory, native ? "provider.trmn" : "provider.trm");
        try
        {
            await File.WriteAllTextAsync(source, "public int32 Value() { return 42; }");
            string[] arguments = native
                ? ["compile-native", "producer", "--runtime", "Policy", "--export", "Value",
                    "-o", artifact, "--runtime", "fail-fast", "--json"]
                : ["compile-module", "producer", "--runtime", "Value",
                    "-o", artifact, "--runtime", "fail-fast", "--json"];
            var (exit, output, error) = await RunAsync([], arguments, directory);
            Assert.That(exit, Is.EqualTo(0));
            Assert.That(error, Is.Empty);
            using var document = JsonDocument.Parse(output);
            Assert.That(document.RootElement.GetProperty("profile").GetString(), Is.EqualTo("native-10"));
            var bytes = await File.ReadAllBytesAsync(artifact);
            if (native)
            {
                Assert.That(NativeModuleCompiler.Inspect(bytes).RuntimePolicy, Is.EqualTo(NativeRuntimePolicy.FailFast));
            }
            else
            {
                Assert.That(ModuleCompiler.CreateReference(bytes).RuntimePolicy, Is.EqualTo(BoundRuntimePolicy.FailFast));
            }
        }
        finally
        {
            File.Delete(source);
            File.Delete(artifact);
            Directory.Delete(directory);
        }
    }

    [Test]
    public async Task InvalidUnsafeRecoveryPreservesAnExistingArtifactAsync()
    {
        var path = Path.Combine(TestArtifactPaths.DirectoryPath, $"terrum-checked-invalid-{Guid.NewGuid():N}.exe");
        byte[] original = [1, 2, 3];
        try
        {
            await File.WriteAllBytesAsync(path, original);
            var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(
                "int32 Main() { try { require false; } catch { return 42; } return 0; }"),
                "build", "-", "-o", path, "--runtime", "checked-exceptions", "--json");
            Assert.That(exit, Is.EqualTo(1));
            Assert.That(error, Is.Empty);
            using var document = JsonDocument.Parse(output);
            Assert.That(document.RootElement.GetProperty("profile").GetString(), Is.EqualTo("native-11"));
            Assert.That(global::System.Linq.Enumerable.Any(document.RootElement.GetProperty("diagnostics").EnumerateArray(),diagnostic => diagnostic.GetProperty("code").GetString() == "TRM3021"),Is.True);
            Assert.That(await File.ReadAllBytesAsync(path), Is.EqualTo(original));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public void MultipleSnapshotsAcceptExplicitCheckedBufferCapabilities()
    {
        var reader = SyntaxTree.Parse("int32 Read(int32 at) { return Host.Data[at]; }", "reader.terrum");
        var entry = SyntaxTree.Parse("""
            int32 Main() { unsafe { try { return Read(1); } catch { return 42; } } }
            """, "entry.terrum");
        var compilation = Compilation.Create([reader, entry],
            [new HostBuffer("Data", PrimitiveType.Int32, [7], false)], BoundRuntimePolicy.CheckedExceptions);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        { var __nunitItems0 = global::System.Linq.Enumerable.ToArray(compilation.SyntaxTrees); Assert.That(__nunitItems0, Has.Length.EqualTo(2)); Assert.That(__nunitItems0[0], Is.SameAs(reader)); Assert.That(__nunitItems0[1], Is.SameAs(entry)); }
        Assert.That(((global::System.Func<BoundProgram>)(() => { var __nunitActual1 = compilation.Program; Assert.That(__nunitActual1, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual1!; }))().RuntimePolicy, Is.EqualTo(BoundRuntimePolicy.CheckedExceptions));
    }
}
