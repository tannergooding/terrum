// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.IO;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed partial class NativeManagedClassModuleTests
{
    [Theory]
    [InlineData(18, 43)]
    [InlineData(19, 42)]
    public async Task LegacyManagedLeavesPreserveWritableAuthorityWhenLinkedAsync(
        int version, int expected)
    {
        const NativeRuntimePolicy Policy = NativeRuntimePolicy.FailFast;
        var producer = await File.ReadAllBytesAsync(Path.Combine(RepositoryRoot(), "tests",
            "Terrum.CodeAnalysis.Tests", "Fixtures", "Native", $"managed-access-legacy-v{version}.trm"))
            .ConfigureAwait(true);
        Assert.Equal(version, BinaryPrimitives.ReadInt32LittleEndian(producer.AsSpan(4, 4)));
        var reference = ModuleCompiler.CreateReference(producer);
        Assert.Empty(reference.Structures);
        Assert.Equal("Leaf", Assert.Single(reference.Functions).Name);
        var consumer = ModuleCompiler.CreateConsumer(Compile("int32 Main() { return Leaf(); }",
            Policy, reference, library: false), producer);
        var linked = ModuleCompiler.Link(consumer, producer);
        if (version == 18)
        {
            var setValue = Assert.Single(linked.Functions,
                function => function.Intrinsic == BoundIntrinsic.NodeSetValue);
            Assert.Equal(PrimitiveType.WriteManagedNode, setValue.Variables[0].Type);
            var getNext = Assert.Single(linked.Functions,
                function => function.Intrinsic == BoundIntrinsic.NodeTryGetNext);
            Assert.Equal(PrimitiveType.ManagedNode, getNext.Variables[0].Type);
            Assert.Equal(PrimitiveType.WriteManagedNode, getNext.Variables[1].Type);
        }
        else
        {
            var @class = Assert.Single(linked.Classes);
            Assert.True(@class.IsMutable);
            var increment = Assert.Single(linked.Functions,
                function => function.Name == "$producer.Counter.Increment");
            Assert.Equal(ManagedClassTypes.Writable((PrimitiveType)ManagedClassTypes.First),
                increment.Variables[0].Type);
        }
        Assert.Equal(expected, await ExecuteAsync(NativeEmitter.Emit(linked, Policy)).ConfigureAwait(true));
    }

    [Fact]
    public async Task LegacyVersion23ManagedLeafRetainsWritableAuthorityAfterNormalizationAsync()
    {
        const NativeRuntimePolicy Policy = NativeRuntimePolicy.FailFast;
        var producer = await File.ReadAllBytesAsync(Path.Combine(RepositoryRoot(), "tests",
            "Terrum.CodeAnalysis.Tests", "Fixtures", "Native", "managed-access-legacy-v23.trm"))
            .ConfigureAwait(true);
        Assert.Equal(23, BinaryPrimitives.ReadInt32LittleEndian(producer.AsSpan(4, 4)));
        var reference = ModuleCompiler.CreateReference(producer);
        Assert.Empty(reference.Structures);
        Assert.Equal("Leaf", Assert.Single(reference.Functions).Name);
        var consumer = ModuleCompiler.CreateConsumer(Compile("int32 Main() { return Leaf(); }",
            Policy, reference, library: false), producer);
        var linked = ModuleCompiler.Link(consumer, producer);
        var @class = Assert.Single(linked.Classes);
        Assert.True(@class.IsMutable);
        Assert.Equal(ManagedClassTypes.Optional(ManagedClassTypes.Writable(
            (PrimitiveType)ManagedClassTypes.First)), @class.Fields[1].Type);
        var bump = Assert.Single(linked.Functions, function => function.Name == "$producer.Cycle.Bump");
        Assert.Equal(ManagedClassTypes.Writable((PrimitiveType)ManagedClassTypes.First),
            bump.Variables[0].Type);
        Assert.Equal(42, await ExecuteAsync(NativeEmitter.Emit(linked, Policy)).ConfigureAwait(true));
    }

    [Fact]
    public void LegacyVersion23CannotSmuggleWritableTypeIdentifiers()
    {
        var producer = File.ReadAllBytes(Path.Combine(RepositoryRoot(), "tests",
            "Terrum.CodeAnalysis.Tests", "Fixtures", "Native", "managed-access-legacy-v23.trm"));
        var edgeName = Find(producer, [4, 0, 0, 0, (byte)'N', (byte)'e', (byte)'x', (byte)'t',
            0, 5, 0, 0]);
        var edgeType = edgeName + 8;
        Assert.Equal((int)ManagedClassTypes.Optional((PrimitiveType)ManagedClassTypes.First),
            BinaryPrimitives.ReadInt32LittleEndian(producer.AsSpan(edgeType, 4)));
        var forged = (byte[])producer.Clone();
        BinaryPrimitives.WriteInt32LittleEndian(forged.AsSpan(edgeType, 4),
            ManagedClassTypes.OptionalWriteFirst);
        _ = Assert.Throws<ModuleFormatException>(() => ModuleCompiler.CreateReference(forged));
    }
}
