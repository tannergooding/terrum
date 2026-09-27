// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.IO;
using System.Security.Cryptography;
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

    [Theory]
    [InlineData(NativeRuntimePolicy.FailFast)]
    [InlineData(NativeRuntimePolicy.CheckedExceptions)]
    public async Task NestedReceiverUsesVersion26WithUnchangedConsumerAndPrivateProducerReplacementAsync(
        NativeRuntimePolicy policy)
    {
        const string Replacement = """
            struct Inner {
                public mut int64 Value;
                public Inner(int64 value) { Value = value; }
                public mut void Add(int64 amount) { Value = Value + amount; }
            }
            struct Outer {
                public mut Inner Nested;
                public Outer(int64 value) { Nested = new Inner(value); }
                public mut void Forward(int64 amount) { Nested.Add(amount); }
            }
            public int32 Leaf() {
                mut Outer owner = new Outer(20);
                owner.Nested.Add(11);
                owner.Forward(11);
                return int32(owner.Nested.Value);
            }
            """;
        const string Original = "public int32 Leaf() { return 1; }";
        const string Consumer = "int32 Main() { return Leaf(); }";
        var original = ModuleCompiler.CreateProducer(Compile(Original, policy), ["Leaf"]);
        var consumer = ModuleCompiler.CreateConsumer(Compile(Consumer, policy,
            ModuleCompiler.CreateReference(original), library: false), original);
        var hash = SHA256.HashData(consumer);
        var producer = ModuleCompiler.CreateProducer(Compile(Replacement, policy), ["Leaf"]);
        Assert.Equal(26, BinaryPrimitives.ReadInt32LittleEndian(producer.AsSpan(4, 4)));
        Assert.Empty(ModuleCompiler.CreateReference(producer).Structures);
        var downgraded = (byte[])producer.Clone();
        BinaryPrimitives.WriteInt32LittleEndian(downgraded.AsSpan(4, 4), 25);
        _ = Assert.Throws<ModuleFormatException>(() => ModuleCompiler.CreateReference(downgraded));
        Assert.Equal(42, await ExecuteAsync(NativeEmitter.Emit(
            ModuleCompiler.Link(consumer, producer), policy)).ConfigureAwait(true));
        Assert.Equal(hash, SHA256.HashData(consumer));

        var nativeOriginal = NativeModuleCompiler.CreateProducer(Compile(Original, policy), ["Leaf"], "NodeB", policy);
        var nativeConsumer = NativeModuleCompiler.CreateGraphConsumer(Compile(Consumer, policy,
            NativeModuleCompiler.CreateGraphReference([nativeOriginal]), library: false),
            [nativeOriginal], policy);
        var nativeHash = SHA256.HashData(nativeConsumer);
        var nativeReplacement = NativeModuleCompiler.CreateProducer(Compile(Replacement, policy),
            ["Leaf"], "NodeB", policy);
        Assert.Equal(42, await NativeModuleCompiler.RunGraphAsync(nativeConsumer, [nativeReplacement],
            TimeSpan.FromSeconds(10), policy).ConfigureAwait(true));
        var framework = new DirectoryInfo(AppContext.BaseDirectory);
        var configuration = framework.Parent?.Name
            ?? throw new InvalidOperationException("Missing test configuration directory.");
        var apphost = Path.Combine(RepositoryRoot(), "artifacts", "bin", "sources", "terrumc",
            configuration, framework.Name, "terrumc.exe");
        Assert.Equal(42, await NativeModuleCompiler.RunGraphJitAsync(nativeConsumer, [nativeReplacement],
            apphost, TimeSpan.FromSeconds(30), policy).ConfigureAwait(true));
        Assert.Equal(nativeHash, SHA256.HashData(nativeConsumer));
    }
}
