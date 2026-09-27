// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed partial class NativeManagedClassModuleTests
{
    [Theory]
    [InlineData(NativeRuntimePolicy.FailFast)]
    [InlineData(NativeRuntimePolicy.CheckedExceptions)]
    public async Task MutableStructReceiverUsesVersion25AndLinksThroughScalarExportAsync(
        NativeRuntimePolicy policy)
    {
        var program = Compile("""
            public struct Counter {
                public mut int32 Value;
                public Counter(int32 value) { Value = value; }
                public mut void Increment() { Value = Value + 1; }
            }
            public int32 Leaf() {
                mut Counter value = new Counter(41);
                value.Increment();
                return value.Value;
            }
            """, policy);
        _ = Assert.Throws<ModuleContractException>(() =>
            ModuleCompiler.CreateProducer(program, ["Counter.Increment"]));
        var producer = ModuleCompiler.CreateProducer(program, ["Leaf"]);
        Assert.Equal(25, BinaryPrimitives.ReadInt32LittleEndian(producer.AsSpan(4, 4)));
        var reference = ModuleCompiler.CreateReference(producer);
        Assert.Equal("Leaf", Assert.Single(reference.Functions).Name);
        var consumer = ModuleCompiler.CreateConsumer(Compile("int32 Main() { return Leaf(); }",
            policy, reference, library: false), producer);
        var linked = ModuleCompiler.Link(consumer, producer);
        Assert.Equal(42, await ExecuteAsync(NativeEmitter.Emit(linked, policy)).ConfigureAwait(true));
        var downgraded = (byte[])producer.Clone();
        BinaryPrimitives.WriteInt32LittleEndian(downgraded.AsSpan(4, 4), 24);
        _ = Assert.Throws<ModuleFormatException>(() => ModuleCompiler.CreateReference(downgraded));
    }
}
