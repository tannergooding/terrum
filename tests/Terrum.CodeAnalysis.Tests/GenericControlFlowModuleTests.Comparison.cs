// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed partial class GenericControlFlowModuleTests
{
    [Theory]
    [InlineData(NativeRuntimePolicy.FailFast)]
    [InlineData(NativeRuntimePolicy.CheckedExceptions)]
    public async Task PrivateComparisonSpecializationsReplaceUnchangedConsumersAsync(NativeRuntimePolicy policy)
    {
        var source = await File.ReadAllTextAsync(Path.Combine(RepositoryRoot(), "samples", "native",
            "static-protocol-comparison.terrum")).ConfigureAwait(true);
        const string Original = "public int32 ComparisonProtocolProbe() { return 40; }";
        const string Consumer = """
            struct Rank {
                public int32 Different;
                public Rank(int32 value) { Different = value; }
            }
            int32 Main() { return ComparisonProtocolProbe() + new Rank(0).Different; }
            """;
        var originalProgram = Compile(Original, policy);
        var original = ModuleCompiler.CreateProducer(originalProgram, ["ComparisonProtocolProbe"]);
        var consumer = ModuleCompiler.CreateConsumer(Compile(Consumer, policy,
            ModuleCompiler.CreateReference(original), library: false), original);
        var hash = SHA256.HashData(consumer);
        var replacementProgram = Compile(source, policy);
        var replacement = ModuleCompiler.CreateProducer(replacementProgram, ["ComparisonProtocolProbe"]);
        _ = Assert.Single(ModuleCompiler.CreateReference(replacement).Functions);
        var linked = ModuleCompiler.Link(consumer, replacement);
        Assert.Equal(["Rank", "$producer.Rank"], linked.Structures.Select(static item => item.Name));
        Assert.Contains(linked.Functions, static function => function.Name == "$producer.Within<Rank>");
        Assert.Contains(linked.Functions, static function => function.Name == "$producer.Rank.op_Equality");
        Assert.Equal(40, await ExecuteAsync(NativeEmitter.Emit(ModuleCompiler.Link(consumer, original),
            policy)).ConfigureAwait(true));
        Assert.Equal(42, await ExecuteAsync(NativeEmitter.Emit(linked, policy)).ConfigureAwait(true));
        Assert.Equal(hash, SHA256.HashData(consumer));

        var nativeOriginal = NativeModuleCompiler.CreateProducer(originalProgram,
            ["ComparisonProtocolProbe"], "NodeB", policy);
        var nativeReplacement = NativeModuleCompiler.CreateProducer(replacementProgram,
            ["ComparisonProtocolProbe"], "NodeB", policy);
        var nativeConsumer = NativeModuleCompiler.CreateGraphConsumer(Compile(Consumer, policy,
            NativeModuleCompiler.CreateGraphReference([nativeOriginal]), library: false),
            [nativeOriginal], policy);
        var nativeHash = SHA256.HashData(nativeConsumer);
        Assert.Equal(40, await NativeModuleCompiler.RunGraphAsync(nativeConsumer, [nativeOriginal],
            TimeSpan.FromSeconds(10), policy).ConfigureAwait(true));
        Assert.Equal(42, await NativeModuleCompiler.RunGraphAsync(nativeConsumer, [nativeReplacement],
            TimeSpan.FromSeconds(10), policy).ConfigureAwait(true));
        Assert.Equal(42, await NativeModuleCompiler.RunGraphJitAsync(nativeConsumer, [nativeReplacement],
            Apphost(), TimeSpan.FromSeconds(30), policy).ConfigureAwait(true));
        Assert.Equal(nativeHash, SHA256.HashData(nativeConsumer));
    }
}
