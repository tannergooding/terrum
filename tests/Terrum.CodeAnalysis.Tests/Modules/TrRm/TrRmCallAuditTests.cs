// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests.Modules.TrRm;

public sealed class TrRmCallAuditTests
{
    internal static byte[] IndexedCallBody() =>
    [
        8, 1, 2, 0, 1, 1, 0, 0, 0,
        1, 1, 1, 1, 0, 2, 7, 11, 0, 18, 16,
        0x10, 2, 1, 1, 2, 2, 2,
        1, 1, 1, 41, 0x12, 2, 2, 1, 1, 0x83, 2,
    ];

    internal static byte[] IdentityBody() => [4, 1, 1, 0, 0, 0, 0, 0x83, 1];
    [TestCase(12,(byte)(0))] // wrong call result use count
    [TestCase(14,(byte)(1))] // missing call definition
    [TestCase(16,(byte)(12))] // wrong call definition offset
    [TestCase(18,(byte)(17))] // truncated block extent
    [TestCase(19,(byte)(15))] // wrong terminator boundary
    [TestCase(23,(byte)(0))] // argument's final use excludes the call
    [TestCase(25,(byte)(1))] // call result's last use is not its return
    [TestCase(32,(byte)(0))] // value-returning callee cannot have result zero
    [TestCase(33,(byte)(3))] // nonexistent module ordinal
    [TestCase(34,(byte)(0))] // missing argument
    [TestCase(35,(byte)(2))] // self-dependent call result
    public void IndexedCallInventoriesAndOperandsAreAudited(int offset, byte value)
    {
        var body = IndexedCallBody();
        body[offset] = value;
        Assert.Throws<InvalidDataException>(() => TrRmExecutableCompiler.Load(
            TrRmExecutionAuditTests.LocalCallArtifact(body, IdentityBody())));
    }

    [Test]
    public void IndexedCallFixtureIsIndependentlyValid()
    {
        var artifact = TrRmExecutionAuditTests.LocalCallArtifact(IndexedCallBody(), IdentityBody());
        TrRmStructuralMetadataValidator.ValidateCompact(artifact);
        Assert.That(NativeJit.Execute(TrRmExecutableCompiler.Load(artifact),
            NativeRuntimePolicy.FailFast), Is.EqualTo(41));
        var body = IndexedCallBody();
        for (var length = 1; length < body.Length; length++)
        {
            Assert.Throws<InvalidDataException>(() => TrRmExecutableCompiler.Load(
                TrRmExecutionAuditTests.LocalCallArtifact(body[..length], IdentityBody())));
        }
    }
    [TestCase((byte)(1))]
    [TestCase((byte)(4))]
    public void NonminimalCallReferenceOrArgumentCountWidthRejects(byte layout)
    {
        byte[] caller = layout == 1
            ? [4, 1, 1, 0, 0, 0, 2, 1, 1, 1, 41,
                0xF0, 1, 0x12, 2, 0, 2, 0, 1, 1, 0, 0x83, 2]
            : [4, 1, 1, 0, 0, 0, 2, 1, 1, 1, 41,
                0xF0, 4, 0x12, 2, 2, 1, 0, 1, 0x83, 2];
        var artifact = TrRmExecutionAuditTests.LocalCallArtifact(caller, IdentityBody());
        TrRmStructuralMetadataValidator.ValidateCompactLinear(artifact);
        Assert.Throws<InvalidDataException>(() => TrRmExecutableCompiler.Load(artifact));
    }
    [TestCase(255)]
    [TestCase(256)]
    public void VoidOperationCountAndParameterCountAreNotAddedForWidth(int operations)
    {
        var artifact = TrRmExecutionAuditTests.LocalCallArtifact(
            [4, 1, 1, 0, 0, 0, 1, 1, 1, 1, 42, 0x83, 1],
            RepeatedVoidCalls(operations), resultRow: 7);
        TrRmStructuralMetadataValidator.ValidateCompactLinear(artifact);
        var program = TrRmExecutableCompiler.Load(artifact);
        Assert.That(program.Functions[1].Variables, Has.Exactly(1).Items);
        // The recursive callee is deliberately uncalled; this proves decode and lowering,
        // not termination of a self-call with no base case.
        Assert.That(NativeJit.Execute(program, NativeRuntimePolicy.FailFast), Is.EqualTo(42));
    }

    [Test]
    public void ResultlessCallVisitsStillConsumeTheSharedVerificationBudget()
    {
        var artifact = TrRmExecutionAuditTests.LocalCallArtifact(
            [4, 1, 1, 0, 0, 0, 1, 1, 1, 1, 42, 0x83, 1],
            RepeatedVoidCalls(50_300), resultRow: 7);
        var exception = Assert.Throws<InvalidDataException>(() => TrRmExecutableCompiler.Load(artifact))!;
        Assert.That(exception.InnerException, Is.TypeOf<NotSupportedException>());
    }

    private static byte[] RepeatedVoidCalls(int count)
    {
        var width = count <= 255 ? 1 : 2;
        var bytes = new List<byte> { (byte)(width == 1 ? 4 : 5), 1 };
        if (width == 2)
        {
            bytes.Add(0);
        }
        bytes.AddRange([1, 0, 0, 0, (byte)(count & byte.MaxValue)]);
        if (width == 2)
        {
            bytes.Add((byte)(count >> 8));
        }
        for (var ordinal = 0; ordinal < count; ordinal++)
        {
            bytes.AddRange([0x12, 0, 2, 1, 1]);
        }
        bytes.AddRange([0x83, 0]);
        return bytes.ToArray();
    }

    [Test]
    public void DeclaredNoThrowCalleeCannotHideACheckedOperation()
    {
        byte[] caller = [4, 1, 1, 0, 0, 0, 2,
            1, 1, 1, 41, 0x12, 2, 2, 1, 1, 0x83, 2];
        byte[] callee = [4, 1, 1, 0, 0, 0, 2,
            1, 2, 1, 1, 0x15, 3, 1, 1, 2, 0x83, 3];
        var artifact = TrRmExecutionAuditTests.LocalCallArtifact(caller, callee,
            callerNoThrow: true, calleeNoThrow: true);
        TrRmStructuralMetadataValidator.ValidateCompactLinear(artifact);
        Assert.Throws<InvalidDataException>(() => TrRmExecutableCompiler.Load(artifact));
    }
    [TestCase("bool","true")]
    [TestCase("char16","char16(65535)")]
    [TestCase("int32","int32.MinValue")]
    [TestCase("uint32","uint32.MaxValue")]
    [TestCase("int64","int64.MinValue")]
    [TestCase("uint64","uint64.MaxValue")]
    public void ScalarCallResultsPreserveFullWidthAndSignedness(string type, string value)
        => RoundTrip($"int32 Main() {{ return Echo({value}) == {value} ? 42 : 0; }} "
            + $"[NoThrow] {type} Echo({type} value) {{ return value; }}", 42);

    [Test]
    public void ArgumentSnapshotsSurviveLazyContinuationChanges()
        => RoundTrip("""
            int32 Main() {
                mut int32 x = 1;
                x += F(x++, x++ == 2 ? ++x : --x, x);
                return x;
            }
            int32 F(int32 a, int32 b, int32 c) { return a * 100 + b * 10 + c; }
            """, 145);

    [Test]
    public void MaximumStackArgumentEnvelopeExecutes()
    {
        var parameters = string.Join(", ", Enumerable.Range(0, 64).Select(index => $"int64 p{index}"));
        var arguments = string.Join(", ", Enumerable.Range(1, 64).Select(index => $"int64({index})"));
        RoundTrip($"int32 Main() {{ return int32(Sum({arguments})); }} "
            + $"int64 Sum({parameters}) {{ return p0 + p63; }}", 65);
    }

    [Test]
    public void WideMethodOrdinalUsesTheExistingFunctionLimit()
    {
        var functions = string.Join(" ", Enumerable.Range(0, 1023)
            .Select(index => $"void F{index}() {{ }}"));
        RoundTrip($"int32 Main() {{ F1022(); return 42; }} {functions}", 42);
    }

    [Test]
    public void InactiveFailingCalleeAndLoopConditionCallsPreserveLaziness()
        => RoundTrip("""
            int32 Main() {
                mut int32 x = 0;
                while (Check(x++)) {
                    if (x == 2) { continue; }
                    if (x == 3) { break; }
                }
                return false && Fail() == 0 ? 0 : x;
            }
            bool Check(int32 x) { return x < 4; }
            int32 Fail() { return 1 / 0; }
            """, 3);

    private static void RoundTrip(string source, int expected)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var original = ((global::System.Func<BoundProgram>)(() => { var __nunitActual0 = compilation.Program; Assert.That(__nunitActual0, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual0!; }))();
        var loaded = TrRmExecutableCompiler.Load(TrRmExecutableCompiler.Emit(original, "calls-audit"));
        Assert.That(NativeJit.Execute(original, NativeRuntimePolicy.FailFast), Is.EqualTo(expected));
        Assert.That(NativeJit.Execute(loaded, NativeRuntimePolicy.FailFast), Is.EqualTo(expected));
    }
}
