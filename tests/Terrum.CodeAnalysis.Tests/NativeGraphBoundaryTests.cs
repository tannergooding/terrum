// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed class NativeGraphBoundaryTests
{
    [Theory]
    [InlineData(NativeRuntimePolicy.FailFast)]
    [InlineData(NativeRuntimePolicy.CheckedExceptions)]
    public async Task ExecutesSixteenProducerChainAndRejectsSeventeenthAsync(NativeRuntimePolicy policy)
    {
        var providers = new byte[16][];
        providers[0] = Leaf("public int32 Value0() { return 1; }", "Value0", "Chain0", policy);
        for (var index = 1; index < providers.Length; index++)
        {
            var name = FormattableString.Invariant($"Value{index}");
            var source = FormattableString.Invariant(
                $"public int32 {name}() {{ return Value{index - 1}() + 1; }}");
            providers[index] = Library(source, name, FormattableString.Invariant($"Chain{index}"),
                [providers[index - 1]], policy);
        }
        var root = Root("int32 Main() { return Value15(); }", [providers[^1]], policy);
        NativeModuleCompiler.ValidateGraph(root, providers);
        Assert.Equal(16, await NativeModuleCompiler.RunGraphAsync(root, providers,
            TimeSpan.FromSeconds(10), policy));

        var extra = Leaf("public int32 Extra() { return 0; }", "Extra", "Extra", policy);
        _ = Assert.Throws<ModuleContractException>(() =>
            NativeModuleCompiler.ValidateGraph(root, [.. providers, extra]));
    }

    [Fact]
    public async Task DistinctLeafIdentitiesWithEqualExportNamesRemainIsolatedAsync()
    {
        const NativeRuntimePolicy Policy = NativeRuntimePolicy.FailFast;
        var first = Leaf("public int32 GetVersion() { return 20; }", "GetVersion", "LeafV1", Policy);
        var second = Leaf("public int32 GetVersion() { return 22; }", "GetVersion", "LeafV2", Policy);
        var firstReader = Library("public int32 ReadV1() { return GetVersion(); }",
            "ReadV1", "ReaderV1", [first], Policy);
        var secondReader = Library("public int32 ReadV2() { return GetVersion(); }",
            "ReadV2", "ReaderV2", [second], Policy);
        var root = Root("int32 Main() { return ReadV1() + ReadV2(); }",
            [firstReader, secondReader], Policy);
        var originalRoot = (byte[])root.Clone();
        var originalFirstReader = (byte[])firstReader.Clone();
        var originalSecondReader = (byte[])secondReader.Clone();
        Assert.Equal(42, await NativeModuleCompiler.RunGraphAsync(root,
            [second, firstReader, first, secondReader], TimeSpan.FromSeconds(10), Policy));

        var replacement = Leaf("public int32 GetVersion() { return 21; }", "GetVersion", "LeafV1", Policy);
        Assert.Equal(43, await NativeModuleCompiler.RunGraphAsync(root,
            [firstReader, secondReader, second, replacement], TimeSpan.FromSeconds(10), Policy));
        Assert.Equal(originalRoot, root);
        Assert.Equal(originalFirstReader, firstReader);
        Assert.Equal(originalSecondReader, secondReader);
    }

    private static byte[] Leaf(string source, string export, string identity, NativeRuntimePolicy policy)
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source), (BoundRuntimePolicy)policy);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        return NativeModuleCompiler.CreateProducer(Assert.IsType<BoundProgram>(compilation.Program),
            [export], identity, policy);
    }

    private static byte[] Library(string source, string export, string identity, byte[][] dependencies,
        NativeRuntimePolicy policy)
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source),
            NativeModuleCompiler.CreateGraphReference(dependencies), (BoundRuntimePolicy)policy);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        return NativeModuleCompiler.CreateGraphProducer(Assert.IsType<BoundProgram>(compilation.Program),
            [export], identity, dependencies, policy);
    }

    private static byte[] Root(string source, byte[][] dependencies, NativeRuntimePolicy policy)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(source),
            NativeModuleCompiler.CreateGraphReference(dependencies), (BoundRuntimePolicy)policy);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        return NativeModuleCompiler.CreateGraphConsumer(Assert.IsType<BoundProgram>(compilation.Program),
            dependencies, policy);
    }
}
