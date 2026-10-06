// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
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
    public async Task PrivateExtendedOperatorSpecializationReplacesUnchangedConsumerAsync(
        NativeRuntimePolicy policy)
    {
        const string Original = "public int32 LeafValue() { return 40; }";
        const string Replacement = """
            interface Exclusive<A, B, C> { static C operator ^(A left, B right); }
            struct Value : Exclusive<Value, Value, Value> {
                public int32 Number;
                public Value(int32 number) { Number = number; }
                public static Value operator ^(Value left, Value right) {
                    return new Value(left.Number ^ right.Number);
                }
            }
            T Mix<T>(T left, T right) where T : Exclusive<T, T, T>, [Copy] {
                return left ^ right;
            }
            U Forward<U>(U left, U right) where U : Exclusive<U, U, U>, [Copy] {
                return Mix(left, right);
            }
            public int32 LeafValue() { return Forward(new Value(40), new Value(2)).Number; }
            """;
        const string Consumer = """
            struct Value {
                public int32 Unrelated;
                public Value(int32 value) { Unrelated = value; }
            }
            int32 Main() { return LeafValue() + new Value(0).Unrelated; }
            """;
        var originalProgram = Compile(Original, policy);
        var original = ModuleCompiler.CreateProducer(originalProgram, ["LeafValue"]);
        var consumerProgram = Compile(Consumer, policy, ModuleCompiler.CreateReference(original), library: false);
        var consumer = ModuleCompiler.CreateConsumer(consumerProgram, original);
        var hash = SHA256.HashData(consumer);
        var replacementProgram = Compile(Replacement, policy);
        var replacement = ModuleCompiler.CreateProducer(replacementProgram, ["LeafValue"]);
        Assert.Equal(62,BinaryPrimitives.ReadInt32LittleEndian(replacement.AsSpan(4, 4)));
        Assert.Empty(ModuleCompiler.CreateReference(replacement).Structures);
        var linked = ModuleCompiler.Link(consumer, replacement);
        Assert.Contains(linked.Functions, function => function.Name == "$producer.Mix<Value>");
        Assert.Contains(linked.Functions, function => function.Name == "$producer.Forward<Value>");
        Assert.Contains(linked.Functions, function => function.Name == "$producer.Value.op_ExclusiveOr");
        Assert.Equal(42, await ExecuteAsync(NativeEmitter.Emit(linked, policy)).ConfigureAwait(true));
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
        Assert.Equal(42, await NativeModuleCompiler.RunGraphJitAsync(nativeConsumer, [nativeReplacement],
            Apphost(), TimeSpan.FromSeconds(30), policy).ConfigureAwait(true));
        Assert.Equal(nativeHash, SHA256.HashData(nativeConsumer));
    }
}
