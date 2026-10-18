// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests.Native.Modules;

public sealed partial class NativeManagedClassModuleTests
{
    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public async Task MutableBufferFieldViewUsesPrivateProducerReplacementAsync(
        NativeRuntimePolicy policy)
    {
        const string Original = """
            public int32 LeafValue() {
                require OwnedInt32Buffer.TryCreate(2, out var raw);
                mut OwnedInt32Buffer words = move raw;
                require words.TryAppend(40);
                require words.TryAppend(1);
                {
                    mut MutableInt32BufferView view = words.SliceMutable(1, 1);
                    view.Set(0, 2);
                }
                words.Clear();
                require words.TryAppend(1);
                return words.Read(0);
            }
            """;
        const string Replacement = """
            struct Unused { public int32 Value; public Unused() { Value = 0; } }
            unique struct Owner {
                public mut OwnedInt32Buffer Words;
                public Owner(OwnedInt32Buffer words) { Words = move words; }
            }
            public int32 LeafValue() {
                require OwnedInt32Buffer.TryCreate(2, out var raw);
                mut Owner owner = new Owner(move raw);
                require owner.Words.TryAppend(40);
                require owner.Words.TryAppend(1);
                {
                    mut MutableInt32BufferView view = owner.Words.SliceMutable(1, 1);
                    view.Set(0, 2);
                }
                owner.Words.Clear();
                require owner.Words.TryAppend(42);
                return owner.Words.Read(0);
            }
            """;
        const string Consumer = "int32 Main() { return LeafValue(); }";
        var originalProgram = Compile(Original, policy);
        var replacementProgram = Compile(Replacement, policy);
        Assert.That(replacementProgram.Structures[0].Name, Is.EqualTo("Unused"));
        var original = ModuleCompiler.CreateProducer(originalProgram, ["LeafValue"]);
        var consumer = ModuleCompiler.CreateConsumer(Compile(Consumer, policy,
            ModuleCompiler.CreateReference(original), library: false), original);
        var hash = SHA256.HashData(consumer);
        var producer = ModuleCompiler.CreateProducer(replacementProgram, ["LeafValue"]);
        Assert.That(BinaryPrimitives.ReadInt32LittleEndian(producer.AsSpan(4, 4)), Is.EqualTo(76));
        Assert.That(ModuleCompiler.CreateReference(producer).Structures, Is.Empty);
        var downgraded = (byte[])producer.Clone();
        BinaryPrimitives.WriteInt32LittleEndian(downgraded.AsSpan(4, 4), 29);
        _ = Assert.Throws<ModuleFormatException>(() => ModuleCompiler.CreateReference(downgraded))!;
        Assert.That(await ExecuteAsync(NativeEmitter.Emit(
            ModuleCompiler.Link(consumer, producer), policy)).ConfigureAwait(true), Is.EqualTo(42));
        Assert.That(SHA256.HashData(consumer), Is.EqualTo(hash));

        var nativeOriginal = NativeModuleCompiler.CreateProducer(originalProgram, ["LeafValue"], "NodeB", policy);
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
}
