// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed partial class NativeManagedClassModuleTests
{
    [Theory]
    [InlineData(NativeRuntimePolicy.FailFast, false)]
    [InlineData(NativeRuntimePolicy.FailFast, true)]
    [InlineData(NativeRuntimePolicy.CheckedExceptions, false)]
    [InlineData(NativeRuntimePolicy.CheckedExceptions, true)]
    public void CopyFactoryArgumentsRetainVersionedManagedAccessEvenWithoutCopyFields(
        NativeRuntimePolicy policy, bool storeCopy)
    {
        var field = storeCopy ? "public Payload Value;" : "public int64 Value;";
        var assignment = storeCopy ? "Value = value;" : "Value = value.First;";
        var access = storeCopy ? "box.Value.First" : "box.Value";
        var program = Compile($$"""
            struct Payload {
                public int64 First;
                public int64 Second;
                public Payload(int64 first) { First = first; Second = 2; }
            }
            class Box {
                {{field}}
                public Box(Payload value) { {{assignment}} }
            }
            public int32 Proof() {
                require Box.TryCreate(new Payload(42), out var box);
                return int32({{access}});
            }
            """, policy);
        var producer = ModuleCompiler.CreateProducer(program, ["Proof"]);
        Assert.Equal(50, BinaryPrimitives.ReadInt32LittleEndian(producer.AsSpan(4, 4)));
        Assert.Equal("native-19", ModuleCompiler.GetNativeProfileVersion(producer, false));
        var reference = ModuleCompiler.CreateReference(producer);
        Assert.Empty(reference.Structures);
        Assert.Equal("Proof", Assert.Single(reference.Functions).Name);
        foreach (var version in new[] { 19, 20, 21, 22, 23 })
        {
            var downgraded = (byte[])producer.Clone();
            BinaryPrimitives.WriteInt32LittleEndian(downgraded.AsSpan(4, 4), version);
            _ = Assert.Throws<ModuleFormatException>(() => ModuleCompiler.CreateReference(downgraded));
        }
    }

    [Theory]
    [InlineData(NativeRuntimePolicy.FailFast)]
    [InlineData(NativeRuntimePolicy.CheckedExceptions)]
    public async Task CopyFactorySemanticReplacementRemapsPrivateArgumentsAsync(NativeRuntimePolicy policy)
    {
        var original = ModuleCompiler.CreateProducer(
            Compile("public int32 Leaf() { return 40; }", policy), ["Leaf"]);
        var consumer = ModuleCompiler.CreateConsumer(Compile("""
            struct Cell {
                public int64 Other;
                public Cell(int64 value) { Other = value; }
            }
            struct Stamp {
                public int32 Other;
                public Stamp(int32 value) { Other = value; }
            }
            int32 Main() {
                Cell cell = new Cell(1);
                Stamp stamp = new Stamp(2);
                return Leaf() + int32(cell.Other) + stamp.Other - 3;
            }
            """, policy, ModuleCompiler.CreateReference(original), library: false), original);
        var hash = SHA256.HashData(consumer);
        var replacement = ModuleCompiler.CreateProducer(Compile(CopyArgumentLeaf(), policy), ["Leaf"]);
        Assert.Equal(50, BinaryPrimitives.ReadInt32LittleEndian(replacement.AsSpan(4, 4)));
        var program = ModuleCompiler.Link(consumer, replacement);
        Assert.Equal(["Cell", "Stamp", "$producer.Cell", "$producer.Stamp"],
            program.Structures.Select(static structure => structure.Name));
        var factory = Assert.Single(program.Functions,
            static function => function.Intrinsic == BoundIntrinsic.ClassTryCreate);
        Assert.Equal((PrimitiveType)103, factory.Variables[0].Type);
        Assert.Equal((PrimitiveType)103, program.Functions[factory.ClassConstructor].Variables[1].Type);
        Assert.Equal(42, await ExecuteAsync(NativeEmitter.Emit(program, policy)).ConfigureAwait(true));
        Assert.Equal(hash, SHA256.HashData(consumer));
    }

    [Theory]
    [InlineData(NativeRuntimePolicy.FailFast)]
    [InlineData(NativeRuntimePolicy.CheckedExceptions)]
    public async Task CopyFactoryLeafReplacesUnchangedNativeGraphAsync(NativeRuntimePolicy policy)
    {
        var original = NativeModuleCompiler.CreateProducer(
            Compile(Sample("managed-class-leaf-original"), policy), ["Leaf"], "NodeB", policy);
        var middle = NativeModuleCompiler.CreateGraphProducer(Compile(Middle, policy,
            NativeModuleCompiler.CreateGraphReference([original])), ["Middle"], "NodeA", [original], policy);
        var root = NativeModuleCompiler.CreateGraphConsumer(Compile(Root, policy,
            NativeModuleCompiler.CreateGraphReference([middle]), library: false), [middle], policy);
        var rootHash = SHA256.HashData(root);
        var middleHash = SHA256.HashData(middle);
        var replacement = NativeModuleCompiler.CreateProducer(
            Compile(CopyArgumentLeaf(), policy), ["Leaf"], "NodeB", policy);
        Assert.Equal("native-20", NativeModuleCompiler.Inspect(replacement).NativeProfileVersion);
        NativeModuleCompiler.ValidateGraph(root, [replacement, middle]);
        Assert.Equal(44, await NativeModuleCompiler.RunGraphAsync(root, [middle, replacement],
            TimeSpan.FromSeconds(10), policy).ConfigureAwait(true));
        var framework = new DirectoryInfo(AppContext.BaseDirectory);
        var configuration = framework.Parent?.Name
            ?? throw new InvalidOperationException("Missing test configuration directory.");
        var apphost = Path.Combine(RepositoryRoot(), "artifacts", "bin", "sources", "terrumc",
            configuration, framework.Name, "terrumc.exe");
        Assert.Equal(44, await NativeModuleCompiler.RunGraphJitAsync(root, [replacement, middle], apphost,
            TimeSpan.FromSeconds(30), policy).ConfigureAwait(true));
        Assert.Equal(rootHash, SHA256.HashData(root));
        Assert.Equal(middleHash, SHA256.HashData(middle));
    }

    private static string CopyArgumentLeaf() =>
        Sample("managed-copy-class-leaf")
            .Replace("public Cycle(int32 value)", "public Cycle(Stamp value)", StringComparison.Ordinal)
            .Replace("Data = new Stamp(value);", "Data = value;", StringComparison.Ordinal)
            .Replace("Cycle.TryCreate(20,", "Cycle.TryCreate(new Stamp(20),", StringComparison.Ordinal)
            .Replace("Cycle.TryCreate(22,", "Cycle.TryCreate(new Stamp(22),", StringComparison.Ordinal);
}
