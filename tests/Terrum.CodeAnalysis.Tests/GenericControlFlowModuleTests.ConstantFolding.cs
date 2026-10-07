// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed partial class GenericControlFlowModuleTests
{
    [Theory]
    [InlineData(NativeRuntimePolicy.FailFast)]
    [InlineData(NativeRuntimePolicy.CheckedExceptions)]
    public async Task FoldedPrivateScalarConstantReplacesUnchangedConsumerAsync(NativeRuntimePolicy policy)
    {
        const string Original = "public int32 Read() { return 40; }";
        const string Replacement = """
            [NoThrow] bool Condition() {
                return (uint64.MaxValue > uint64(int64.MaxValue)) && !false;
            }
            [NoThrow] int32 Secret() {
                require (true & (uint64.MaxValue > uint64(int64.MaxValue))) == true;
                return int32(uint32(6 * 7));
            }
            public int32 Read() { if (Condition()) { return Secret(); } return 0; }
            """;
        const string Consumer = "int32 Main() { return Read(); }";
        var originalProgram = Compile(Original, policy);
        var replacementProgram = Compile(Replacement, policy);
        var original = ModuleCompiler.CreateProducer(originalProgram, ["Read"]);
        var replacement = ModuleCompiler.CreateProducer(replacementProgram, ["Read"]);
        Assert.Equal(65,BinaryPrimitives.ReadInt32LittleEndian(original.AsSpan(4, 4)));
        Assert.Equal(65,BinaryPrimitives.ReadInt32LittleEndian(replacement.AsSpan(4, 4)));
        Assert.Equal(ModuleCompiler.GetNativeProfileVersion(original, false),
            ModuleCompiler.GetNativeProfileVersion(replacement, false));
        Assert.Equal("Read", Assert.Single(ModuleCompiler.CreateReference(replacement).Functions).Name);

        var consumer = ModuleCompiler.CreateConsumer(Compile(Consumer, policy,
            ModuleCompiler.CreateReference(original), library: false), original);
        var hash = SHA256.HashData(consumer);
        var linked = ModuleCompiler.Link(consumer, replacement);
        var condition = Assert.Single(linked.Functions, function => function.Name == "$producer.Condition");
        var conditionResult = Assert.IsType<BoundReturn>(Assert.Single(condition.Body.Statements));
        Assert.Equal(1L, Assert.IsType<BoundConstant>(conditionResult.Value).Value);
        var secret = Assert.Single(linked.Functions, function => function.Name == "$producer.Secret");
        var requirement = Assert.IsType<BoundRequire>(secret.Body.Statements[0]);
        Assert.Equal(1L, Assert.IsType<BoundConstant>(requirement.Condition).Value);
        var result = Assert.IsType<BoundReturn>(secret.Body.Statements[1]);
        Assert.Equal(42L, Assert.IsType<BoundConstant>(result.Value).Value);
        Assert.Equal(42, await ExecuteAsync(NativeEmitter.Emit(linked, policy)).ConfigureAwait(true));
        Assert.Equal(42, NativeJit.Execute(linked, policy));
        Assert.Equal(hash, SHA256.HashData(consumer));

        var nativeOriginal = NativeModuleCompiler.CreateProducer(originalProgram, ["Read"], "Leaf", policy);
        var nativeReplacement = NativeModuleCompiler.CreateProducer(replacementProgram, ["Read"], "Leaf", policy);
        Assert.Equal(NativeModuleCompiler.Inspect(nativeOriginal).NativeProfileVersion,
            NativeModuleCompiler.Inspect(nativeReplacement).NativeProfileVersion);
        var nativeConsumer = NativeModuleCompiler.CreateConsumer(Compile(Consumer, policy,
            NativeModuleCompiler.CreateReference(nativeOriginal), library: false), nativeOriginal, policy);
        var nativeHash = SHA256.HashData(nativeConsumer);
        Assert.Equal(40, await NativeModuleCompiler.RunAsync(nativeConsumer, nativeOriginal,
            TimeSpan.FromSeconds(10)).ConfigureAwait(true));
        Assert.Equal(42, await NativeModuleCompiler.RunAsync(nativeConsumer, nativeReplacement,
            TimeSpan.FromSeconds(10)).ConfigureAwait(true));
        Assert.Equal(nativeHash, SHA256.HashData(nativeConsumer));
    }
}
