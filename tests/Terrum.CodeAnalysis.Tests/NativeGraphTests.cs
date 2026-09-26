// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed class NativeGraphTests
{
    private static byte[] Leaf(string source, NativeRuntimePolicy policy = NativeRuntimePolicy.FailFast)
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source), (BoundRuntimePolicy)policy);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        return NativeModuleCompiler.CreateProducer(Assert.IsType<BoundProgram>(compilation.Program),
            ["Leaf"], "NodeB", policy);
    }

    private static byte[] Middle(string source, byte[] leaf,
        NativeRuntimePolicy policy = NativeRuntimePolicy.FailFast)
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source),
            NativeModuleCompiler.CreateGraphReference([leaf]), (BoundRuntimePolicy)policy);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        return NativeModuleCompiler.CreateGraphProducer(Assert.IsType<BoundProgram>(compilation.Program),
            ["Middle"], "NodeA", [leaf], policy);
    }

    private static byte[] Consumer(byte[] middle, NativeRuntimePolicy policy = NativeRuntimePolicy.FailFast)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("int32 Main() { return Middle() + 1; }"),
            NativeModuleCompiler.CreateGraphReference([middle]), (BoundRuntimePolicy)policy);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        return NativeModuleCompiler.CreateGraphConsumer(Assert.IsType<BoundProgram>(compilation.Program),
            [middle], policy);
    }

    private static byte[] WeakenRecordedImport(byte[] artifact, string name, string oldSuffix, string newSuffix)
    {
        var anchor = Encoding.ASCII.GetBytes(name + "|");
        var at = artifact.AsSpan().IndexOf(anchor);
        Assert.True(at >= 4);
        Assert.Equal(-1, artifact.AsSpan(at + 1).IndexOf(anchor));
        var length = BinaryPrimitives.ReadInt32LittleEndian(artifact.AsSpan(at - 4));
        var saved = Encoding.ASCII.GetString(artifact, at, length);
        Assert.EndsWith(oldSuffix, saved, StringComparison.Ordinal);
        var suffixStart = at + length - oldSuffix.Length;
        var difference = newSuffix.Length - oldSuffix.Length;
        var result = new byte[artifact.Length + difference];
        artifact.AsSpan(0, suffixStart).CopyTo(result);
        Encoding.ASCII.GetBytes(newSuffix).CopyTo(result, suffixStart);
        artifact.AsSpan(at + length).CopyTo(result.AsSpan(at + length + difference));
        BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(at - 4), checked(length + difference));
        BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(11),
            checked(BinaryPrimitives.ReadInt32LittleEndian(artifact.AsSpan(11)) + difference));
        SHA256.HashData(result.AsSpan(0, result.Length - 32)).CopyTo(result, result.Length - 32);
        return result;
    }

    [Theory]
    [InlineData(NativeRuntimePolicy.FailFast)]
    [InlineData(NativeRuntimePolicy.CheckedExceptions)]
    public async Task UnchangedRootAndMiddleExecuteCompatibleLeafReplacementAsync(NativeRuntimePolicy policy)
    {
        var original = Leaf("[NoThrow] public int32 Leaf() { return 40; }", policy);
        var middle = Middle("public int32 Middle() { return Leaf() + 1; }", original, policy);
        var root = Consumer(middle, policy);
        var originalRoot = root.ToArray();
        var originalMiddle = middle.ToArray();
        NativeModuleCompiler.ValidateGraph(root, [original, middle]);
        Assert.Equal(42, await NativeModuleCompiler.RunGraphAsync(root, [middle, original],
            TimeSpan.FromSeconds(10), policy));
        var replacement = Leaf("[NoThrow] public int32 Leaf() { return 41; }", policy);
        NativeModuleCompiler.ValidateGraph(root, [replacement, middle]);
        Assert.Equal(43, await NativeModuleCompiler.RunGraphAsync(root, [replacement, middle],
            TimeSpan.FromSeconds(10), policy));
        Assert.Equal(originalRoot, root);
        Assert.Equal(originalMiddle, middle);
        Assert.Equal(["NodeA"], NativeModuleCompiler.InspectGraphArtifact(root).Dependencies);
        Assert.Equal(["NodeB"], NativeModuleCompiler.InspectGraphArtifact(middle).Dependencies);
    }

    [Fact]
    public void RejectsMissingExtraIncompatiblePolicyAndCyclicGraphs()
    {
        var leaf = Leaf("[NoThrow] public int32 Leaf() { return 40; }");
        var middle = Middle("public int32 Middle() { return Leaf() + 1; }", leaf);
        var root = Consumer(middle);
        _ = Assert.Throws<ModuleContractException>(() => NativeModuleCompiler.ValidateGraph(root, [middle]));
        _ = Assert.Throws<ModuleContractException>(() => NativeModuleCompiler.ValidateGraph(root, [leaf, middle, leaf]));
        _ = Assert.Throws<ModuleContractException>(() => NativeModuleCompiler.ValidateGraph(root, [
            leaf, middle, NativeModuleCompiler.CreateProducer(
                Assert.IsType<BoundProgram>(Compilation.CreateLibrary(
                    SyntaxTree.Parse("public int32 Extra() { return 1; }")).Program),
                ["Extra"], "Extra", NativeRuntimePolicy.FailFast)]));
        _ = Assert.Throws<ModuleContractException>(() => NativeModuleCompiler.ValidateGraph(root, [
            Leaf("public int32 Leaf() { return 40; }"), middle]));
        _ = Assert.Throws<ModuleContractException>(() => NativeModuleCompiler.ValidateGraph(root, [
            Leaf("[NoThrow] public int32 Leaf() { return 40; }", NativeRuntimePolicy.CheckedExceptions), middle]));
        var damaged = middle.ToArray();
        damaged[^1] ^= 1;
        _ = Assert.Throws<ModuleFormatException>(() => NativeModuleCompiler.ValidateGraph(root, [leaf, damaged]));
        var tamperedImport = middle.ToArray();
        "NodeC"u8.CopyTo(tamperedImport.AsSpan(19 + "NodeA".Length + 2));
        SHA256.HashData(tamperedImport.AsSpan(0, tamperedImport.Length - 32))
            .CopyTo(tamperedImport, tamperedImport.Length - 32);
        _ = Assert.Throws<ModuleFormatException>(() => NativeModuleCompiler.ValidateGraph(root,
            [leaf, tamperedImport]));
        var badCount = middle.ToArray();
        badCount[8] = 0;
        SHA256.HashData(badCount.AsSpan(0, badCount.Length - 32))
            .CopyTo(badCount, badCount.Length - 32);
        _ = Assert.Throws<ModuleFormatException>(() => NativeModuleCompiler.ValidateGraph(root, [leaf, badCount]));

        var cycle = middle.ToArray();
        "NodeB"u8.CopyTo(cycle.AsSpan(19));
        SHA256.HashData(cycle.AsSpan(0, cycle.Length - 32)).CopyTo(cycle, cycle.Length - 32);
        var error = Assert.Throws<ModuleContractException>(() => NativeModuleCompiler.ValidateGraph(root,
            [middle, cycle]));
        Assert.Contains("cycle", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task OwnedTransferAcrossIntermediatePreservesReplacementContractsAsync()
    {
        const string LeafSource = """
            public unique struct Token {
                public int32 Value;
                public Token(int32 value) { Value = value; }
                [NoThrow] [Cleanup] public void Close() { }
            }
            public Token New() { return new Token(20); }
            [NoThrow] public int32 Take(Token value) { return value.Value; }
            """;
        static byte[] CompileLeaf(string source)
        {
            var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source));
            Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
            return NativeModuleCompiler.CreateProducer(Assert.IsType<BoundProgram>(compilation.Program),
                ["New", "Take", "Token.Close"], "NodeB", NativeRuntimePolicy.FailFast);
        }
        var leaf = CompileLeaf(LeafSource);
        var middleCompilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            public int32 Middle() {
                Token token = New();
                return Take(move token) + 22;
            }
            """), NativeModuleCompiler.CreateGraphReference([leaf]));
        Assert.False(middleCompilation.HasErrors, string.Join(Environment.NewLine, middleCompilation.Diagnostics));
        var middle = NativeModuleCompiler.CreateGraphProducer(
            Assert.IsType<BoundProgram>(middleCompilation.Program), ["Middle"], "NodeA",
            [leaf], NativeRuntimePolicy.FailFast);
        var rootCompilation = Compilation.Create(SyntaxTree.Parse("int32 Main() { return Middle(); }"),
            NativeModuleCompiler.CreateGraphReference([middle]));
        Assert.False(rootCompilation.HasErrors, string.Join(Environment.NewLine, rootCompilation.Diagnostics));
        var root = NativeModuleCompiler.CreateGraphConsumer(Assert.IsType<BoundProgram>(rootCompilation.Program),
            [middle], NativeRuntimePolicy.FailFast);
        Assert.Equal("native-13", NativeModuleCompiler.InspectGraphArtifact(middle).NativeProfileVersion);
        var replacement = CompileLeaf(LeafSource.Replace("new Token(20)", "new Token(21)", StringComparison.Ordinal));
        Assert.Equal(42, await NativeModuleCompiler.RunGraphAsync(root, [middle, leaf],
            TimeSpan.FromSeconds(10), NativeRuntimePolicy.FailFast));
        Assert.Equal(43, await NativeModuleCompiler.RunGraphAsync(root, [replacement, middle],
            TimeSpan.FromSeconds(10), NativeRuntimePolicy.FailFast));
        _ = Assert.Throws<ModuleContractException>(() => NativeModuleCompiler.ValidateGraph(root, [
            CompileLeaf(LeafSource.Replace("[NoThrow] public int32 Take", "public int32 Take",
                StringComparison.Ordinal)), middle]));
        _ = Assert.Throws<ModuleContractException>(() => NativeModuleCompiler.ValidateGraph(root, [
            CompileLeaf(LeafSource.Replace("public int32 Value;", "public int32 Other;", StringComparison.Ordinal)
                .Replace("Value = value;", "Other = value;", StringComparison.Ordinal)
                .Replace("value.Value", "value.Other", StringComparison.Ordinal)), middle]));
    }

    [Fact]
    public async Task SharedLeafDiamondIsAcyclicAndBindsEveryImportToItsOwnerAsync()
    {
        var leaf = Leaf("public int32 Leaf() { return 40; }");
        var middle = Middle("public int32 Middle() { return Leaf() + 1; }", leaf);
        var topCompilation = Compilation.CreateLibrary(SyntaxTree.Parse(
            "public int32 Top() { return Middle() + Leaf(); }"),
            NativeModuleCompiler.CreateGraphReference([middle, leaf]));
        Assert.False(topCompilation.HasErrors, string.Join(Environment.NewLine, topCompilation.Diagnostics));
        var top = NativeModuleCompiler.CreateGraphProducer(Assert.IsType<BoundProgram>(topCompilation.Program),
            ["Top"], "NodeC", [middle, leaf], NativeRuntimePolicy.FailFast);
        var rootCompilation = Compilation.Create(SyntaxTree.Parse("int32 Main() { return Top(); }"),
            NativeModuleCompiler.CreateGraphReference([top]));
        Assert.False(rootCompilation.HasErrors, string.Join(Environment.NewLine, rootCompilation.Diagnostics));
        var root = NativeModuleCompiler.CreateGraphConsumer(Assert.IsType<BoundProgram>(rootCompilation.Program),
            [top], NativeRuntimePolicy.FailFast);
        NativeModuleCompiler.ValidateGraph(root, [top, leaf, middle]);
        Assert.Equal(81, await NativeModuleCompiler.RunGraphAsync(root, [middle, leaf, top],
            TimeSpan.FromSeconds(10), NativeRuntimePolicy.FailFast));
        var replacement = Leaf("public int32 Leaf() { return 41; }");
        Assert.Equal(83, await NativeModuleCompiler.RunGraphAsync(root, [replacement, top, middle],
            TimeSpan.FromSeconds(10), NativeRuntimePolicy.FailFast));
    }

    [Fact]
    public void PersistedLibraryAndRootBodiesKeepRecordedNoThrowPromise()
    {
        var leaf = Leaf("[NoThrow] public int32 Leaf() { return 40; }");
        var middle = Middle("[NoThrow] public int32 Middle() { return Leaf(); }", leaf);
        var rootCompilation = Compilation.Create(SyntaxTree.Parse(
            "[NoThrow] int32 Main() { return Middle(); }"),
            NativeModuleCompiler.CreateGraphReference([middle]));
        Assert.False(rootCompilation.HasErrors, string.Join(Environment.NewLine, rootCompilation.Diagnostics));
        var root = NativeModuleCompiler.CreateGraphConsumer(Assert.IsType<BoundProgram>(rootCompilation.Program),
            [middle], NativeRuntimePolicy.FailFast);
        NativeModuleCompiler.ValidateGraph(root, [leaf, middle]);
        var weakenedMiddle = WeakenRecordedImport(middle, "Leaf", "|NoThrow|MayReturn", "|MayThrow|MayReturn");
        _ = Assert.Throws<ModuleFormatException>(() => NativeModuleCompiler.ValidateGraph(root,
            [leaf, weakenedMiddle]));
        var weakenedRoot = WeakenRecordedImport(root, "Middle", "|NoThrow|MayReturn", "|MayThrow|MayReturn");
        _ = Assert.Throws<ModuleFormatException>(() => NativeModuleCompiler.ValidateGraph(weakenedRoot,
            [leaf, middle]));
    }

    [Fact]
    public void PersistedLibraryCannotWeakenRequiredNoReturnPromise()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(
            "[DoesNotReturn] public void Halt() { while (true) { } }"));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var leaf = NativeModuleCompiler.CreateProducer(Assert.IsType<BoundProgram>(compilation.Program),
            ["Halt"], "NodeB", NativeRuntimePolicy.FailFast);
        var middleCompilation = Compilation.CreateLibrary(SyntaxTree.Parse(
            "public int32 Middle() { Halt(); }"), NativeModuleCompiler.CreateGraphReference([leaf]));
        Assert.False(middleCompilation.HasErrors, string.Join(Environment.NewLine, middleCompilation.Diagnostics));
        var middle = NativeModuleCompiler.CreateGraphProducer(Assert.IsType<BoundProgram>(middleCompilation.Program),
            ["Middle"], "NodeA", [leaf], NativeRuntimePolicy.FailFast);
        var root = Consumer(middle);
        NativeModuleCompiler.ValidateGraph(root, [leaf, middle]);
        var weakened = WeakenRecordedImport(middle, "Halt", "|MayThrow|DoesNotReturn", "|MayThrow|MayReturn");
        _ = Assert.Throws<ModuleFormatException>(() => NativeModuleCompiler.ValidateGraph(root,
            [leaf, weakened]));
    }

    [Fact]
    public void LibraryMayExportOrdinaryMainWithoutCreatingAnEntryPoint()
    {
        var leaf = Leaf("public int32 Leaf() { return 40; }");
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(
            "public int32 Main() { return Leaf(); }"),
            NativeModuleCompiler.CreateGraphReference([leaf]));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = Assert.IsType<BoundProgram>(compilation.Program);
        Assert.Equal(-1, program.EntryPoint);
        var graph = NativeModuleCompiler.CreateGraphProducer(program, ["Main"], "NodeA",
            [leaf], NativeRuntimePolicy.FailFast);
        Assert.Equal(["Main"], NativeModuleCompiler.InspectGraphArtifact(graph).Exports);
    }
}
