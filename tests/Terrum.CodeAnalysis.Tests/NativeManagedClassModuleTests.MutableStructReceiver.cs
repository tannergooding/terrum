// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests;

public sealed partial class NativeManagedClassModuleTests
{
    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public async Task MutableStructReceiverLinksThroughScalarExportAsync(
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
            ModuleCompiler.CreateProducer(program, ["Counter.Increment"]))!;
        var producer = ModuleCompiler.CreateProducer(program, ["Leaf"]);
        Assert.That(BinaryPrimitives.ReadInt32LittleEndian(producer.AsSpan(4, 4)), Is.EqualTo(76));
        var reference = ModuleCompiler.CreateReference(producer);
        Assert.That(global::System.Linq.Enumerable.Single(reference.Functions).Name, Is.EqualTo("Leaf"));
        var consumer = ModuleCompiler.CreateConsumer(Compile("int32 Main() { return Leaf(); }",
            policy, reference, library: false), producer);
        var linked = ModuleCompiler.Link(consumer, producer);
        Assert.That(await ExecuteAsync(NativeEmitter.Emit(linked, policy)).ConfigureAwait(true), Is.EqualTo(42));
        var downgraded = (byte[])producer.Clone();
        BinaryPrimitives.WriteInt32LittleEndian(downgraded.AsSpan(4, 4), 46);
        _ = Assert.Throws<ModuleFormatException>(() => ModuleCompiler.CreateReference(downgraded))!;
    }
    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public async Task NestedReceiverUsesUnchangedConsumerAndPrivateProducerReplacementAsync(
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
        Assert.That(BinaryPrimitives.ReadInt32LittleEndian(producer.AsSpan(4, 4)), Is.EqualTo(76));
        Assert.That(ModuleCompiler.CreateReference(producer).Structures, Is.Empty);
        var downgraded = (byte[])producer.Clone();
        BinaryPrimitives.WriteInt32LittleEndian(downgraded.AsSpan(4, 4), 25);
        _ = Assert.Throws<ModuleFormatException>(() => ModuleCompiler.CreateReference(downgraded))!;
        Assert.That(await ExecuteAsync(NativeEmitter.Emit(
            ModuleCompiler.Link(consumer, producer), policy)).ConfigureAwait(true), Is.EqualTo(42));
        Assert.That(SHA256.HashData(consumer), Is.EqualTo(hash));

        var nativeOriginal = NativeModuleCompiler.CreateProducer(Compile(Original, policy), ["Leaf"], "NodeB", policy);
        var nativeConsumer = NativeModuleCompiler.CreateGraphConsumer(Compile(Consumer, policy,
            NativeModuleCompiler.CreateGraphReference([nativeOriginal]), library: false),
            [nativeOriginal], policy);
        var nativeHash = SHA256.HashData(nativeConsumer);
        var nativeReplacement = NativeModuleCompiler.CreateProducer(Compile(Replacement, policy),
            ["Leaf"], "NodeB", policy);
        Assert.That(await NativeModuleCompiler.RunGraphAsync(nativeConsumer, [nativeReplacement],
            TimeSpan.FromSeconds(10), policy).ConfigureAwait(true), Is.EqualTo(42));
        var framework = new DirectoryInfo(AppContext.BaseDirectory);
        var configuration = framework.Parent?.Name
            ?? throw new InvalidOperationException("Missing test configuration directory.");
        var apphost = Path.Combine(RepositoryRoot(), "artifacts", "bin", "sources", "terrumc",
            configuration, framework.Name, "terrumc.exe");
        Assert.That(await NativeModuleCompiler.RunGraphJitAsync(nativeConsumer, [nativeReplacement],
            apphost, TimeSpan.FromSeconds(30), policy).ConfigureAwait(true), Is.EqualTo(42));
        Assert.That(SHA256.HashData(nativeConsumer), Is.EqualTo(nativeHash));
    }
    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public async Task UniqueMutableReceiverUsesPrivateProducerReplacementAsync(
        NativeRuntimePolicy policy)
    {
        const string Replacement = """
            unique struct Owner {
                public mut int32 Value;
                public Owner(int32 value) { Value = value; }
                public mut void Add(int32 amount) { Value = Value + amount; }
                [NoThrow] [Cleanup] public void Close() {}
            }
            public int32 Leaf() {
                mut Owner owner = new Owner(40);
                owner.Add(2);
                return owner.Value;
            }
            """;
        const string Original = "public int32 Leaf() { return 1; }";
        const string Consumer = "int32 Main() { return Leaf(); }";
        var replacementProgram = Compile(Replacement, policy);
        _ = Assert.Throws<ModuleContractException>(() =>
            ModuleCompiler.CreateProducer(replacementProgram, ["Owner.Add"]))!;
        var original = ModuleCompiler.CreateProducer(Compile(Original, policy), ["Leaf"]);
        var consumer = ModuleCompiler.CreateConsumer(Compile(Consumer, policy,
            ModuleCompiler.CreateReference(original), library: false), original);
        var hash = SHA256.HashData(consumer);
        var producer = ModuleCompiler.CreateProducer(replacementProgram, ["Leaf"]);
        Assert.That(BinaryPrimitives.ReadInt32LittleEndian(producer.AsSpan(4, 4)), Is.EqualTo(76));
        Assert.That(ModuleCompiler.CreateReference(producer).Structures, Is.Empty);
        var downgraded = (byte[])producer.Clone();
        BinaryPrimitives.WriteInt32LittleEndian(downgraded.AsSpan(4, 4), 26);
        _ = Assert.Throws<ModuleFormatException>(() => ModuleCompiler.CreateReference(downgraded))!;
        Assert.That(await ExecuteAsync(NativeEmitter.Emit(
            ModuleCompiler.Link(consumer, producer), policy)).ConfigureAwait(true), Is.EqualTo(42));
        Assert.That(SHA256.HashData(consumer), Is.EqualTo(hash));

        var nativeOriginal = NativeModuleCompiler.CreateProducer(Compile(Original, policy), ["Leaf"], "NodeB", policy);
        var nativeConsumer = NativeModuleCompiler.CreateGraphConsumer(Compile(Consumer, policy,
            NativeModuleCompiler.CreateGraphReference([nativeOriginal]), library: false),
            [nativeOriginal], policy);
        var nativeHash = SHA256.HashData(nativeConsumer);
        var nativeReplacement = NativeModuleCompiler.CreateProducer(replacementProgram, ["Leaf"], "NodeB", policy);
        Assert.That(await NativeModuleCompiler.RunGraphAsync(nativeConsumer, [nativeReplacement],
            TimeSpan.FromSeconds(10), policy).ConfigureAwait(true), Is.EqualTo(42));
        var framework = new DirectoryInfo(AppContext.BaseDirectory);
        var configuration = framework.Parent?.Name
            ?? throw new InvalidOperationException("Missing test configuration directory.");
        var apphost = Path.Combine(RepositoryRoot(), "artifacts", "bin", "sources", "terrumc",
            configuration, framework.Name, "terrumc.exe");
        Assert.That(await NativeModuleCompiler.RunGraphJitAsync(nativeConsumer, [nativeReplacement],
            apphost, TimeSpan.FromSeconds(30), policy).ConfigureAwait(true), Is.EqualTo(42));
        Assert.That(SHA256.HashData(nativeConsumer), Is.EqualTo(nativeHash));
    }
    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public async Task NestedUniqueFieldReceiverUsesPrivateProducerReplacementAsync(
        NativeRuntimePolicy policy)
    {
        const string Replacement = """
            struct Leaf {
                public mut int32 Value;
                public Leaf(int32 value) { Value = value; }
                public mut void Add(int32 amount) { Value = Value + amount; }
            }
            unique struct Child {
                public mut Leaf Data;
                public Child(int32 value) { Data = new Leaf(value); }
                [NoThrow] [Cleanup] public void Close() {}
            }
            unique struct Owner {
                public mut Child Nested;
                public Owner(int32 value) { Nested = new Child(value); }
                [NoThrow] [Cleanup] public void Close() {}
            }
            public int32 LeafValue() {
                mut Owner owner = new Owner(40);
                owner.Nested.Data.Add(2);
                return owner.Nested.Data.Value;
            }
            """;
        const string Original = "public int32 LeafValue() { return 1; }";
        const string Consumer = "int32 Main() { return LeafValue(); }";
        var replacementProgram = Compile(Replacement, policy);
        var original = ModuleCompiler.CreateProducer(Compile(Original, policy), ["LeafValue"]);
        var consumer = ModuleCompiler.CreateConsumer(Compile(Consumer, policy,
            ModuleCompiler.CreateReference(original), library: false), original);
        var hash = SHA256.HashData(consumer);
        var producer = ModuleCompiler.CreateProducer(replacementProgram, ["LeafValue"]);
        Assert.That(BinaryPrimitives.ReadInt32LittleEndian(producer.AsSpan(4, 4)), Is.EqualTo(76));
        Assert.That(ModuleCompiler.CreateReference(producer).Structures, Is.Empty);
        var downgraded = (byte[])producer.Clone();
        BinaryPrimitives.WriteInt32LittleEndian(downgraded.AsSpan(4, 4), 27);
        _ = Assert.Throws<ModuleFormatException>(() => ModuleCompiler.CreateReference(downgraded))!;
        Assert.That(await ExecuteAsync(NativeEmitter.Emit(
            ModuleCompiler.Link(consumer, producer), policy)).ConfigureAwait(true), Is.EqualTo(42));
        Assert.That(SHA256.HashData(consumer), Is.EqualTo(hash));

        var nativeOriginal = NativeModuleCompiler.CreateProducer(Compile(Original, policy),
            ["LeafValue"], "NodeB", policy);
        var nativeConsumer = NativeModuleCompiler.CreateGraphConsumer(Compile(Consumer, policy,
            NativeModuleCompiler.CreateGraphReference([nativeOriginal]), library: false),
            [nativeOriginal], policy);
        var nativeHash = SHA256.HashData(nativeConsumer);
        var nativeReplacement = NativeModuleCompiler.CreateProducer(replacementProgram,
            ["LeafValue"], "NodeB", policy);
        Assert.That(await NativeModuleCompiler.RunGraphAsync(nativeConsumer, [nativeReplacement],
            TimeSpan.FromSeconds(10), policy).ConfigureAwait(true), Is.EqualTo(42));
        var framework = new DirectoryInfo(AppContext.BaseDirectory);
        var configuration = framework.Parent?.Name
            ?? throw new InvalidOperationException("Missing test configuration directory.");
        var apphost = Path.Combine(RepositoryRoot(), "artifacts", "bin", "sources", "terrumc",
            configuration, framework.Name, "terrumc.exe");
        Assert.That(await NativeModuleCompiler.RunGraphJitAsync(nativeConsumer, [nativeReplacement],
            apphost, TimeSpan.FromSeconds(30), policy).ConfigureAwait(true), Is.EqualTo(42));
        Assert.That(SHA256.HashData(nativeConsumer), Is.EqualTo(nativeHash));
    }
    [TestCase(false)]
    [TestCase(true)]
    public void PrivateUniqueReceiversUseRemappedStructureIndexes(bool nestedField)
    {
        const string Unused = """
            struct Unused {
                public int32 Value;
                public Unused(int32 value) { Value = value; }
            }
            """;
        const string NamedOwner = """
            unique struct Owner {
                public mut int32 Value;
                public Owner(int32 value) { Value = value; }
                public mut void Add(int32 amount) { Value = Value + amount; }
                [NoThrow] [Cleanup] public void Close() {}
            }
            public int32 LeafValue() {
                mut Owner owner = new Owner(40);
                owner.Add(2);
                return owner.Value;
            }
            """;
        const string NestedField = """
            struct Leaf {
                public mut int32 Value;
                public Leaf(int32 value) { Value = value; }
                public mut void Add(int32 amount) { Value = Value + amount; }
            }
            unique struct Owner {
                public mut Leaf Nested;
                public Owner(int32 value) { Nested = new Leaf(value); }
                [NoThrow] [Cleanup] public void Close() {}
            }
            public int32 LeafValue() {
                mut Owner owner = new Owner(40);
                owner.Nested.Add(2);
                return owner.Nested.Value;
            }
            """;
        var program = Compile(Unused + (nestedField ? NestedField : NamedOwner),
            NativeRuntimePolicy.CheckedExceptions);
        Assert.That(program.Structures[0].Name, Is.EqualTo("Unused"));
        var producer = ModuleCompiler.CreateProducer(program, ["LeafValue"]);
        Assert.That(BinaryPrimitives.ReadInt32LittleEndian(producer.AsSpan(4, 4)), Is.EqualTo(76));
        var reference = ModuleCompiler.CreateReference(producer);
        Assert.That(global::System.Linq.Enumerable.Single(reference.Functions).Name, Is.EqualTo("LeafValue"));
        Assert.That(reference.Structures, Is.Empty);
        var downgraded = (byte[])producer.Clone();
        BinaryPrimitives.WriteInt32LittleEndian(downgraded.AsSpan(4, 4), 46);
        _ = Assert.Throws<ModuleFormatException>(() => ModuleCompiler.CreateReference(downgraded))!;
    }
}
