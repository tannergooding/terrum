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
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed partial class CliTests
{
    [Theory]
    [InlineData("checked-exceptions", 0, "native-11")]
    [InlineData("fail-fast", 1, "native-10")]
    public async Task AnalyzeUsesTheExplicitRecoveryPolicyAsync(string policy, int expected, string profile)
    {
        const string Source = "int32 Main() { unsafe { try { require false; } catch { return 42; } } return 0; }";
        var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(Source),
            "analyze", "-", "--runtime", policy, "--json");
        Assert.Equal(expected, exit);
        Assert.Empty(error);
        using var document = JsonDocument.Parse(output);
        Assert.Equal(profile, document.RootElement.GetProperty("profile").GetString());
        Assert.Equal(expected == 0, document.RootElement.GetProperty("success").GetBoolean());
        Assert.False(document.RootElement.TryGetProperty("artifact", out _));
        if (expected != 0)
        {
            Assert.Contains(document.RootElement.GetProperty("diagnostics").EnumerateArray(),
                diagnostic => diagnostic.GetProperty("code").GetString() == "TRM3022");
        }
    }

    [Theory]
    [InlineData("fail-fast")]
    [InlineData("checked-exceptions")]
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
        Assert.Equal(0, exit);
        Assert.Empty(error);
        using var document = JsonDocument.Parse(output);
        Assert.Equal("native-12", document.RootElement.GetProperty("profile").GetString());
        Assert.Equal(policy, document.RootElement.GetProperty("runtimePolicy").GetString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ModulePolicyIsNotReadFromAnOptionLikeSourceOperandAsync(bool native)
    {
        var directory = Directory.CreateTempSubdirectory("terrum-policy-operand-").FullName;
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
            Assert.Equal(0, exit);
            Assert.Empty(error);
            using var document = JsonDocument.Parse(output);
            Assert.Equal("native-10", document.RootElement.GetProperty("profile").GetString());
            var bytes = await File.ReadAllBytesAsync(artifact);
            if (native)
            {
                Assert.Equal(NativeRuntimePolicy.FailFast, NativeModuleCompiler.Inspect(bytes).RuntimePolicy);
            }
            else
            {
                Assert.Equal(BoundRuntimePolicy.FailFast, ModuleCompiler.CreateReference(bytes).RuntimePolicy);
            }
        }
        finally
        {
            File.Delete(source);
            File.Delete(artifact);
            Directory.Delete(directory);
        }
    }

    [Fact]
    public async Task InvalidUnsafeRecoveryPreservesAnExistingArtifactAsync()
    {
        var path = Path.Combine(Path.GetTempPath(), $"terrum-checked-invalid-{Guid.NewGuid():N}.exe");
        byte[] original = [1, 2, 3];
        try
        {
            await File.WriteAllBytesAsync(path, original);
            var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(
                "int32 Main() { try { require false; } catch { return 42; } return 0; }"),
                "build", "-", "-o", path, "--runtime", "checked-exceptions", "--json");
            Assert.Equal(1, exit);
            Assert.Empty(error);
            using var document = JsonDocument.Parse(output);
            Assert.Equal("native-11", document.RootElement.GetProperty("profile").GetString());
            Assert.Contains(document.RootElement.GetProperty("diagnostics").EnumerateArray(),
                diagnostic => diagnostic.GetProperty("code").GetString() == "TRM3021");
            Assert.Equal(original, await File.ReadAllBytesAsync(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void MultipleSnapshotsAcceptExplicitCheckedBufferCapabilities()
    {
        var reader = SyntaxTree.Parse("int32 Read(int32 at) { return Host.Data[at]; }", "reader.terrum");
        var entry = SyntaxTree.Parse("""
            int32 Main() { unsafe { try { return Read(1); } catch { return 42; } } }
            """, "entry.terrum");
        var compilation = Compilation.Create([reader, entry],
            [new HostBuffer("Data", PrimitiveType.Int32, [7], false)], BoundRuntimePolicy.CheckedExceptions);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.Collection(compilation.SyntaxTrees, tree => Assert.Same(reader, tree), tree => Assert.Same(entry, tree));
        Assert.Equal(BoundRuntimePolicy.CheckedExceptions, Assert.IsType<BoundProgram>(compilation.Program).RuntimePolicy);
    }
}
