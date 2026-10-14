// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests;

public sealed partial class GenericControlFlowModuleTests
{
    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
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
        Assert.That(BinaryPrimitives.ReadInt32LittleEndian(original.AsSpan(4, 4)), Is.EqualTo(76));
        Assert.That(BinaryPrimitives.ReadInt32LittleEndian(replacement.AsSpan(4, 4)), Is.EqualTo(76));
        Assert.That(ModuleCompiler.GetNativeProfileVersion(replacement, false), Is.EqualTo(ModuleCompiler.GetNativeProfileVersion(original, false)));
        Assert.That(global::System.Linq.Enumerable.Single(ModuleCompiler.CreateReference(replacement).Functions).Name, Is.EqualTo("Read"));

        var consumer = ModuleCompiler.CreateConsumer(Compile(Consumer, policy,
            ModuleCompiler.CreateReference(original), library: false), original);
        var hash = SHA256.HashData(consumer);
        var linked = ModuleCompiler.Link(consumer, replacement);
        var condition = global::System.Linq.Enumerable.Single(linked.Functions, function => function.Name == "$producer.Condition");
        var conditionResult = ((global::System.Func<BoundReturn>)(() => { var __nunitActual0 = global::System.Linq.Enumerable.Single(condition.Body.Statements); Assert.That(__nunitActual0, Is.TypeOf<BoundReturn>()); return (BoundReturn)__nunitActual0!; }))();
        Assert.That(((global::System.Func<BoundConstant>)(() => { var __nunitActual1 = conditionResult.Value; Assert.That(__nunitActual1, Is.TypeOf<BoundConstant>()); return (BoundConstant)__nunitActual1!; }))().Value, Is.EqualTo(1L));
        var secret = global::System.Linq.Enumerable.Single(linked.Functions, function => function.Name == "$producer.Secret");
        var requirement = ((global::System.Func<BoundRequire>)(() => { var __nunitActual2 = secret.Body.Statements[0]; Assert.That(__nunitActual2, Is.TypeOf<BoundRequire>()); return (BoundRequire)__nunitActual2!; }))();
        Assert.That(((global::System.Func<BoundConstant>)(() => { var __nunitActual3 = requirement.Condition; Assert.That(__nunitActual3, Is.TypeOf<BoundConstant>()); return (BoundConstant)__nunitActual3!; }))().Value, Is.EqualTo(1L));
        var result = ((global::System.Func<BoundReturn>)(() => { var __nunitActual4 = secret.Body.Statements[1]; Assert.That(__nunitActual4, Is.TypeOf<BoundReturn>()); return (BoundReturn)__nunitActual4!; }))();
        Assert.That(((global::System.Func<BoundConstant>)(() => { var __nunitActual5 = result.Value; Assert.That(__nunitActual5, Is.TypeOf<BoundConstant>()); return (BoundConstant)__nunitActual5!; }))().Value, Is.EqualTo(42L));
        Assert.That(await ExecuteAsync(NativeEmitter.Emit(linked, policy)).ConfigureAwait(true), Is.EqualTo(42));
        Assert.That(NativeJit.Execute(linked, policy), Is.EqualTo(42));
        Assert.That(SHA256.HashData(consumer), Is.EqualTo(hash));

        var nativeOriginal = NativeModuleCompiler.CreateProducer(originalProgram, ["Read"], "Leaf", policy);
        var nativeReplacement = NativeModuleCompiler.CreateProducer(replacementProgram, ["Read"], "Leaf", policy);
        Assert.That(NativeModuleCompiler.Inspect(nativeReplacement).NativeProfileVersion, Is.EqualTo(NativeModuleCompiler.Inspect(nativeOriginal).NativeProfileVersion));
        var nativeConsumer = NativeModuleCompiler.CreateConsumer(Compile(Consumer, policy,
            NativeModuleCompiler.CreateReference(nativeOriginal), library: false), nativeOriginal, policy);
        var nativeHash = SHA256.HashData(nativeConsumer);
        Assert.That(await NativeModuleCompiler.RunAsync(nativeConsumer, nativeOriginal,
            TimeSpan.FromSeconds(10)).ConfigureAwait(true), Is.EqualTo(40));
        Assert.That(await NativeModuleCompiler.RunAsync(nativeConsumer, nativeReplacement,
            TimeSpan.FromSeconds(10)).ConfigureAwait(true), Is.EqualTo(42));
        Assert.That(SHA256.HashData(nativeConsumer), Is.EqualTo(nativeHash));
    }
}
