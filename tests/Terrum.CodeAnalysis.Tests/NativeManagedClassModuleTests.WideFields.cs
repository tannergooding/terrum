// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Linq;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed partial class NativeManagedClassModuleTests
{
    [Theory]
    [InlineData(NativeRuntimePolicy.FailFast, 16)]
    [InlineData(NativeRuntimePolicy.FailFast, 17)]
    [InlineData(NativeRuntimePolicy.FailFast, 32)]
    [InlineData(NativeRuntimePolicy.CheckedExceptions, 16)]
    [InlineData(NativeRuntimePolicy.CheckedExceptions, 17)]
    [InlineData(NativeRuntimePolicy.CheckedExceptions, 32)]
    public async Task WideClassTracksFinalFieldAndManagedEdgeAcrossModulesAsync(
        NativeRuntimePolicy policy, int fieldCount)
    {
        var producer = ModuleCompiler.CreateProducer(Compile(WideSource(fieldCount), policy), ["Read"]);
        Assert.Equal(65,BinaryPrimitives.ReadInt32LittleEndian(producer.AsSpan(4, 4)));
        if (fieldCount > 16)
        {
            var retiredRevision = producer.ToArray();
            BinaryPrimitives.WriteInt32LittleEndian(retiredRevision.AsSpan(4, 4), 46);
            _ = Assert.Throws<ModuleFormatException>(() => ModuleCompiler.CreateReference(retiredRevision));
        }

        var consumer = ModuleCompiler.CreateConsumer(Compile("int32 Main() { return Read(); }",
            policy, ModuleCompiler.CreateReference(producer), library: false), producer);
        var linked = ModuleCompiler.Link(consumer, producer);
        Assert.Equal(fieldCount, Assert.Single(linked.Classes).Fields.Length);
        Assert.Equal(42, await ExecuteAsync(NativeEmitter.Emit(linked, policy)).ConfigureAwait(true));
        Assert.Equal(42, await RunSemanticModuleJitAsync(consumer, producer, policy).ConfigureAwait(true));
    }

    [Theory]
    [InlineData(NativeRuntimePolicy.FailFast)]
    [InlineData(NativeRuntimePolicy.CheckedExceptions)]
    public async Task WideClassGraphLibraryRetainsPrivateLayoutAsync(NativeRuntimePolicy policy)
    {
        var leaf = NativeModuleCompiler.CreateProducer(Compile(
            "public int32 Leaf() { return 0; }", policy), ["Leaf"], "Leaf", policy);
        var middle = NativeModuleCompiler.CreateGraphProducer(Compile(
            WideSource(32, " + Leaf()"), policy, NativeModuleCompiler.CreateGraphReference([leaf])),
            ["Read"], "Wide", [leaf], policy);
        var consumer = NativeModuleCompiler.CreateGraphConsumer(Compile(
            "int32 Main() { return Read(); }", policy,
            NativeModuleCompiler.CreateGraphReference([middle]), library: false),
            [middle], policy);
        NativeModuleCompiler.ValidateGraph(consumer, [leaf, middle]);
        Assert.Equal(42, await NativeModuleCompiler.RunGraphAsync(consumer, [middle, leaf],
            TimeSpan.FromSeconds(10), policy).ConfigureAwait(true));
    }

    [Theory]
    [InlineData(NativeRuntimePolicy.FailFast)]
    [InlineData(NativeRuntimePolicy.CheckedExceptions)]
    public async Task WideClassRevisionRetainsTransparentStructuresAsync(NativeRuntimePolicy policy)
    {
        var source = """
            [Layout(Transparent)] public struct Number {
                public int32 Value;
                public Number(int32 value) { Value = value; }
            }
            """ + WideSource(32).Replace("return wide.Edge.Value;",
                "Number number = new Number(wide.Edge.Value); return number.Value;", StringComparison.Ordinal);
        var program = Compile(source, policy);
        Assert.True(Assert.Single(program.Structures).IsTransparent);
        var producer = ModuleCompiler.CreateProducer(program, ["Read"]);
        Assert.Equal(65,BinaryPrimitives.ReadInt32LittleEndian(producer.AsSpan(4, 4)));
        var consumer = ModuleCompiler.CreateConsumer(Compile("int32 Main() { return Read(); }",
            policy, ModuleCompiler.CreateReference(producer), library: false), producer);
        var linked = ModuleCompiler.Link(consumer, producer);
        Assert.True(Assert.Single(linked.Structures).IsTransparent);
        Assert.Equal(42, await ExecuteAsync(NativeEmitter.Emit(linked, policy)).ConfigureAwait(true));
    }

    [Theory]
    [InlineData(NativeRuntimePolicy.FailFast)]
    [InlineData(NativeRuntimePolicy.CheckedExceptions)]
    public async Task WideClassDescriptorsSurviveFullHeapCollectionAsync(NativeRuntimePolicy policy)
    {
        var source = WideSource(32) + """
            int32 Main() {
                require Make(out var keep);
                for (mut int32 i = 0; i < 2047; i = i + 1) {
                    { require Make(out var temporary); }
                }
                if (ManagedHeap.Collect() != 4094 || keep.Edge.Value != 42 || !keep.F30) {
                    return 1;
                }
                require Make(out var reused);
                if (ManagedHeap.Collect() != 0 || reused.Edge.Value != 42 || !reused.F30) {
                    return 2;
                }
                return keep.Edge.Value;
            }
            """;
        var compilation = Compilation.Create(SyntaxTree.Parse(source), (BoundRuntimePolicy)policy);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = Assert.IsType<BoundProgram>(compilation.Program);
        Assert.Equal(42, NativeJit.Execute(program, policy));
        Assert.Equal(42, await ExecuteAsync(NativeEmitter.Emit(program, policy)).ConfigureAwait(true));
    }

    [Fact]
    public async Task FailedWideClassConstructionReleasesLastFieldEdgeAsync()
    {
        var source = WideSource(32).Replace("Edge = edge; }",
            "Edge = edge; require edge.Value != 42; }", StringComparison.Ordinal) + """
            int32 Main() {
                unsafe {
                    try {
                        if (Make(out var abandoned)) { return 1; }
                        return 2;
                    } catch {
                        if (ManagedHeap.Collect() != 1) { return 3; }
                        require ManagedNode.TryCreate(42, out var reused);
                        return reused.Value;
                    }
                }
            }
            """;
        var compilation = Compilation.Create(SyntaxTree.Parse(source), BoundRuntimePolicy.CheckedExceptions);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = Assert.IsType<BoundProgram>(compilation.Program);
        Assert.Equal(42, NativeJit.Execute(program, NativeRuntimePolicy.CheckedExceptions));
        Assert.Equal(42, await ExecuteAsync(NativeEmitter.Emit(program,
            NativeRuntimePolicy.CheckedExceptions)).ConfigureAwait(true));
    }

    [Theory]
    [InlineData(NativeRuntimePolicy.FailFast)]
    [InlineData(NativeRuntimePolicy.CheckedExceptions)]
    public async Task ThirtyTwoFieldsFillMaximumClassPayloadAsync(NativeRuntimePolicy policy)
    {
        var fields = string.Join(" ", Enumerable.Range(0, 32).Select(index => $"public int32 F{index};"));
        var assignments = string.Join(" ", Enumerable.Range(0, 32).Select(index => $"F{index} = {index};"));
        var source = $"class Dense {{ {fields} public Dense() {{ {assignments} }} }}"
            + " public int32 Read() { require Dense.TryCreate(out var value);"
            + " return value.F31 + value.F11; }";
        var producer = ModuleCompiler.CreateProducer(Compile(source, policy), ["Read"]);
        Assert.Equal(65,BinaryPrimitives.ReadInt32LittleEndian(producer.AsSpan(4, 4)));
        var consumer = ModuleCompiler.CreateConsumer(Compile("int32 Main() { return Read(); }",
            policy, ModuleCompiler.CreateReference(producer), library: false), producer);
        var linked = ModuleCompiler.Link(consumer, producer);
        Assert.Equal(128, Assert.Single(linked.Classes).Size);
        Assert.Equal(42, NativeJit.Execute(linked, policy));
        Assert.Equal(42, await ExecuteAsync(NativeEmitter.Emit(linked, policy)).ConfigureAwait(true));
    }

    [Theory]
    [InlineData(NativeRuntimePolicy.FailFast)]
    [InlineData(NativeRuntimePolicy.CheckedExceptions)]
    public async Task WidePrivateConsumerLinksScalarProviderAsync(NativeRuntimePolicy policy)
    {
        var producer = ModuleCompiler.CreateProducer(Compile(
            "public int32 Bias() { return 0; }", policy), ["Bias"]);
        var source = WideSource(32) + " int32 Main() { return Read() + Bias(); }";
        var consumer = ModuleCompiler.CreateConsumer(Compile(source, policy,
            ModuleCompiler.CreateReference(producer), library: false), producer);
        Assert.Equal(65,BinaryPrimitives.ReadInt32LittleEndian(consumer.AsSpan(4, 4)));
        var linked = ModuleCompiler.Link(consumer, producer);
        Assert.Equal(32, Assert.Single(linked.Classes).Fields.Length);
        Assert.Equal(42, await ExecuteAsync(NativeEmitter.Emit(linked, policy)).ConfigureAwait(true));
        Assert.Equal(42, await RunSemanticModuleJitAsync(consumer, producer, policy).ConfigureAwait(true));
    }

    private static string WideSource(int fieldCount, string resultSuffix = "")
    {
        var fields = string.Join(" ", Enumerable.Range(0, fieldCount - 1).Select(index => $"public bool F{index};"));
        var assignments = string.Join(" ", Enumerable.Range(0, fieldCount - 1).Select(index => $"F{index} = true;"));
        return $$"""
            class Wide {
                {{fields}}
                public ManagedNode Edge;
                public Wide(ManagedNode edge) { {{assignments}} Edge = edge; }
            }
            bool Make(out Wide wide) {
                require ManagedNode.TryCreate(42, out var edge);
                return Wide.TryCreate(edge, out wide);
            }
            public int32 Read() {
                require Make(out var wide);
                require ManagedHeap.Collect() == 0;
                require wide.F{{fieldCount - 2}};
                return wide.Edge.Value{{resultSuffix}};
            }
            """;
    }
}
