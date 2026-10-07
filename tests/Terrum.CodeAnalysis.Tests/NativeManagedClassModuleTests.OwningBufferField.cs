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
    public async Task MutableBufferFieldUsesPrivateProducerReplacementAsync(
        NativeRuntimePolicy policy)
    {
        const string Original = """
            public int32 LeafValue() {
                require OwnedInt32Buffer.TryCreate(1, out var raw);
                mut OwnedInt32Buffer words = move raw;
                require words.TryAppend(1);
                words.Set(0, 1);
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
                [NoThrow] [Cleanup] public void Close() {}
            }
            public int32 LeafValue() {
                require OwnedInt32Buffer.TryCreate(1, out var raw);
                mut Owner owner = new Owner(move raw);
                require owner.Words.TryAppend(1);
                owner.Words.Set(0, 41);
                owner.Words.Clear();
                require owner.Words.TryAppend(42);
                return owner.Words.Read(0);
            }
            """;
        const string Consumer = "int32 Main() { return LeafValue(); }";
        var originalProgram = Compile(Original, policy);
        var replacementProgram = Compile(Replacement, policy);
        Assert.Equal("Unused", replacementProgram.Structures[0].Name);
        var original = ModuleCompiler.CreateProducer(originalProgram, ["LeafValue"]);
        var consumer = ModuleCompiler.CreateConsumer(Compile(Consumer, policy,
            ModuleCompiler.CreateReference(original), library: false), original);
        var hash = SHA256.HashData(consumer);
        var producer = ModuleCompiler.CreateProducer(replacementProgram, ["LeafValue"]);
        Assert.Equal(69,BinaryPrimitives.ReadInt32LittleEndian(producer.AsSpan(4, 4)));
        Assert.Empty(ModuleCompiler.CreateReference(producer).Structures);
        var downgraded = (byte[])producer.Clone();
        BinaryPrimitives.WriteInt32LittleEndian(downgraded.AsSpan(4, 4), 28);
        _ = Assert.Throws<ModuleFormatException>(() => ModuleCompiler.CreateReference(downgraded));
        Assert.Equal(42, await ExecuteAsync(NativeEmitter.Emit(
            ModuleCompiler.Link(consumer, producer), policy)).ConfigureAwait(true));
        Assert.Equal(hash, SHA256.HashData(consumer));

        var nativeOriginal = NativeModuleCompiler.CreateProducer(originalProgram, ["LeafValue"], "NodeB", policy);
        var nativeConsumer = NativeModuleCompiler.CreateGraphConsumer(Compile(Consumer, policy,
            NativeModuleCompiler.CreateGraphReference([nativeOriginal]), library: false),
            [nativeOriginal], policy);
        var nativeHash = SHA256.HashData(nativeConsumer);
        var nativeReplacement = NativeModuleCompiler.CreateProducer(replacementProgram,
            ["LeafValue"], "NodeB", policy);
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
