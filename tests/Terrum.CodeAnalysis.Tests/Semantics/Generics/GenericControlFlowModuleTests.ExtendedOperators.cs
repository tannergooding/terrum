// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests.Semantics.Generics;

public sealed partial class GenericControlFlowModuleTests
{
    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
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
        Assert.That(BinaryPrimitives.ReadInt32LittleEndian(replacement.AsSpan(4, 4)), Is.EqualTo(76));
        Assert.That(ModuleCompiler.CreateReference(replacement).Structures, Is.Empty);
        var linked = ModuleCompiler.Link(consumer, replacement);
        Assert.That(global::System.Linq.Enumerable.Any(linked.Functions,function => function.Name == "$producer.Mix<Value>"),Is.True);
        Assert.That(global::System.Linq.Enumerable.Any(linked.Functions,function => function.Name == "$producer.Forward<Value>"),Is.True);
        Assert.That(global::System.Linq.Enumerable.Any(linked.Functions,function => function.Name == "$producer.Value.op_ExclusiveOr"),Is.True);
        Assert.That(await ExecuteAsync(NativeEmitter.Emit(linked, policy)).ConfigureAwait(true), Is.EqualTo(42));
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
        Assert.That(await NativeModuleCompiler.RunGraphJitAsync(nativeConsumer, [nativeReplacement],
            Apphost(), TimeSpan.FromSeconds(30), policy).ConfigureAwait(true), Is.EqualTo(42));
        Assert.That(SHA256.HashData(nativeConsumer), Is.EqualTo(nativeHash));
    }
}
