// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.IO;
using System.Text;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests;

public sealed class TrRmMutationAuditTests
{
    // One indexed block: signed -1 and count 1, two uint32 reinterpretations,
    // unsigned right shift, signed result reinterpretation. All inventories are handwritten.
    internal static byte[] ShiftBody() =>
    [
        8, 1, 6, 0, 2, 1, 0, 0, 0, 4, 0, 0, 0,
        1, 1, 1, 1, 2, 1, 2, 1, 2, 1, 1, 1,
        0, 6, 15, 19, 23, 28, 33, 38, 0, 45, 43,
        16, 6, 1, 2, 2, 3, 3, 4, 4, 4, 5, 5, 6, 6, 6,
        2, 1, 1, 0xFF, 1, 2, 1, 1,
        6, 3, 2, 3, 1, 6, 4, 2, 3, 2,
        0x15, 5, 7, 3, 4, 6, 6, 1, 3, 5, 0x83, 6,
    ];

    [Test]
    public void HandwrittenIndexedRepresentationShiftExecutes()
    {
        var artifact = TrRmExecutionAuditTests.Artifact(ShiftBody());
        TrRmStructuralMetadataValidator.ValidateCompactIndexed(artifact);
        var program = TrRmExecutableCompiler.Load(artifact);
        Assert.That(NativeJit.Execute(program, NativeRuntimePolicy.FailFast), Is.EqualTo(int.MaxValue));
        Assert.Throws<ArgumentException>(() =>
            NativeEmitter.Emit(program, NativeRuntimePolicy.CheckedExceptions));
        Assert.Throws<ModuleFormatException>(() => ModuleCompiler.CreateProducer(program, ["Main"]));
        Assert.Throws<InvalidDataException>(() => TrRmExecutableCompiler.Load(
            TrRmExecutionAuditTests.Artifact(ShiftBody(), effect: 1)));
    }
    [TestCase(5,(byte)(2))]   // bool source mapping
    [TestCase(9,(byte)(6))]   // uint64 destination mapping
    [TestCase(18,(byte)(2))]  // forged use-block count
    [TestCase(26,(byte)(5))]  // incomplete definition range
    [TestCase(29,(byte)(24))] // conversion's definition offset
    [TestCase(34,(byte)(44))] // block extent
    [TestCase(35,(byte)(42))] // terminator offset
    [TestCase(39,(byte)(3))]  // false last-use ordinal
    [TestCase(62,(byte)(0))]  // unassigned conversion policy
    [TestCase(62,(byte)(4))]  // unassigned conversion policy
    [TestCase(63,(byte)(3))]  // self-dependent conversion definition
    [TestCase(71,(byte)(8))]  // no logical-right kind exists
    [TestCase(73,(byte)(2))]  // int32 count is not a TRRM shift operand
    [TestCase(76,(byte)(2))]  // conversion result disagrees with indexed type
    public void HandwrittenIndexedTamperingRejects(int offset, byte value)
    {
        var body = ShiftBody();
        body[offset] = value;
        Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.Load(TrRmExecutionAuditTests.Artifact(body)));
    }
    [TestCase((byte)(1),int.MaxValue)]
    [TestCase((byte)(2),0)]
    public void HandwrittenIndexedIntegerConvertPolicyExecutes(byte policy, int expected)
    {
        var body = ShiftBody();
        body[62] = policy;
        var program = TrRmExecutableCompiler.Load(TrRmExecutionAuditTests.Artifact(body));
        Assert.That(NativeJit.Execute(program, NativeRuntimePolicy.FailFast), Is.EqualTo(expected));
    }

    [Test]
    public void EveryTruncatedIndexedShiftRejects()
    {
        var body = ShiftBody();
        for (var length = 1; length < body.Length; length++)
        {
            Assert.Throws<InvalidDataException>(() => TrRmExecutableCompiler.Load(
                TrRmExecutionAuditTests.Artifact(body[..length])));
        }
    }
    [TestCase("int32")]
    [TestCase("uint32")]
    [TestCase("int64")]
    [TestCase("uint64")]
    public void LazyRhsAndNestedLoopTransfersPreserveSnapshots(string type)
    {
        var source = $$"""
            int32 Main() {
                mut {{type}} x = {{type}}(4);
                x += x++ == {{type}}(4) ? ++x : {{type}}(1) / {{type}}(0);
                mut int32 i = 0;
                mut int32 j = 0;
                while (i++ < 2) {
                    j = 0;
                    while (j++ < 3) {
                        if (j == 1) { x <<= 1; continue; }
                        x--;
                        break;
                    }
                }
                return x == {{type}}(37) && i == 3 && j == 2 ? 42 : 0;
            }
            """;
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var original = ((global::System.Func<BoundProgram>)(() => { var __nunitActual0 = compilation.Program; Assert.That(__nunitActual0, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual0!; }))();
        var loaded = TrRmExecutableCompiler.Load(TrRmExecutableCompiler.Emit(original, "audit"));
        Assert.That(NativeJit.Execute(original, NativeRuntimePolicy.FailFast), Is.EqualTo(42));
        Assert.That(NativeJit.Execute(loaded, NativeRuntimePolicy.FailFast), Is.EqualTo(42));
    }
    [TestCase(127,true)]
    [TestCase(128,false)]
    public void UpdatesCountEveryMaterializedValue(int updates, bool supported)
    {
        var source = new StringBuilder("int32 Main() { mut int32 x = 0; ");
        for (var index = 0; index < updates; index++)
        {
            source.Append("x++; ");
        }
        source.Append("return +x; }");
        var compilation = Compilation.Create(SyntaxTree.Parse(source.ToString()));
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var original = ((global::System.Func<BoundProgram>)(() => { var __nunitActual1 = compilation.Program; Assert.That(__nunitActual1, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual1!; }))();
        if (!supported)
        {
            Assert.Throws<NotSupportedException>(() => TrRmExecutableCompiler.Emit(original, "limit"));
            return;
        }
        var loaded = TrRmExecutableCompiler.Load(TrRmExecutableCompiler.Emit(original, "limit"));
        Assert.That(loaded.Functions[0].Variables.Length, Is.EqualTo(256));
        Assert.That(NativeJit.Execute(loaded, NativeRuntimePolicy.FailFast), Is.EqualTo(updates));
    }
}
