// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

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

    [Theory]
    [InlineData(12, 0)] // wrong call result use count
    [InlineData(14, 1)] // missing call definition
    [InlineData(16, 12)] // wrong call definition offset
    [InlineData(18, 17)] // truncated block extent
    [InlineData(19, 15)] // wrong terminator boundary
    [InlineData(23, 0)] // argument's final use excludes the call
    [InlineData(25, 1)] // call result's last use is not its return
    [InlineData(32, 0)] // value-returning callee cannot have result zero
    [InlineData(33, 3)] // nonexistent module ordinal
    [InlineData(34, 0)] // missing argument
    [InlineData(35, 2)] // self-dependent call result
    public void IndexedCallInventoriesAndOperandsAreAudited(int offset, byte value)
    {
        var body = IndexedCallBody();
        body[offset] = value;
        Assert.Throws<InvalidDataException>(() => TrRmExecutableCompiler.Load(
            TrRmExecutionAuditTests.LocalCallArtifact(body, IdentityBody())));
    }

    [Fact]
    public void IndexedCallFixtureIsIndependentlyValid()
    {
        var artifact = TrRmExecutionAuditTests.LocalCallArtifact(IndexedCallBody(), IdentityBody());
        TrRmStructuralMetadataValidator.ValidateCompact(artifact);
        Assert.Equal(41, NativeJit.Execute(TrRmExecutableCompiler.Load(artifact),
            NativeRuntimePolicy.FailFast));
        var body = IndexedCallBody();
        for (var length = 1; length < body.Length; length++)
        {
            Assert.Throws<InvalidDataException>(() => TrRmExecutableCompiler.Load(
                TrRmExecutionAuditTests.LocalCallArtifact(body[..length], IdentityBody())));
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
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

    [Theory]
    [InlineData(255)]
    [InlineData(256)]
    public void VoidOperationCountAndParameterCountAreNotAddedForWidth(int operations)
    {
        var artifact = TrRmExecutionAuditTests.LocalCallArtifact(
            [4, 1, 1, 0, 0, 0, 1, 1, 1, 1, 42, 0x83, 1],
            RepeatedVoidCalls(operations), resultRow: 7);
        TrRmStructuralMetadataValidator.ValidateCompactLinear(artifact);
        var program = TrRmExecutableCompiler.Load(artifact);
        Assert.Single(program.Functions[1].Variables);
        // The recursive callee is deliberately uncalled; this proves decode and lowering,
        // not termination of a self-call with no base case.
        Assert.Equal(42, NativeJit.Execute(program, NativeRuntimePolicy.FailFast));
    }

    [Fact]
    public void ResultlessCallVisitsStillConsumeTheSharedVerificationBudget()
    {
        var artifact = TrRmExecutionAuditTests.LocalCallArtifact(
            [4, 1, 1, 0, 0, 0, 1, 1, 1, 1, 42, 0x83, 1],
            RepeatedVoidCalls(50_300), resultRow: 7);
        var exception = Assert.Throws<InvalidDataException>(() => TrRmExecutableCompiler.Load(artifact));
        Assert.IsType<NotSupportedException>(exception.InnerException);
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

    [Fact]
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

    [Theory]
    [InlineData("bool", "true")]
    [InlineData("char16", "char16(65535)")]
    [InlineData("int32", "int32.MinValue")]
    [InlineData("uint32", "uint32.MaxValue")]
    [InlineData("int64", "int64.MinValue")]
    [InlineData("uint64", "uint64.MaxValue")]
    public void ScalarCallResultsPreserveFullWidthAndSignedness(string type, string value)
        => RoundTrip($"int32 Main() {{ return Echo({value}) == {value} ? 42 : 0; }} "
            + $"[NoThrow] {type} Echo({type} value) {{ return value; }}", 42);

    [Fact]
    public void ArgumentSnapshotsSurviveLazyContinuationChanges()
        => RoundTrip("""
            int32 Main() {
                mut int32 x = 1;
                x += F(x++, x++ == 2 ? ++x : --x, x);
                return x;
            }
            int32 F(int32 a, int32 b, int32 c) { return a * 100 + b * 10 + c; }
            """, 145);

    [Fact]
    public void MaximumStackArgumentEnvelopeExecutes()
    {
        var parameters = string.Join(", ", Enumerable.Range(0, 64).Select(index => $"int64 p{index}"));
        var arguments = string.Join(", ", Enumerable.Range(1, 64).Select(index => $"int64({index})"));
        RoundTrip($"int32 Main() {{ return int32(Sum({arguments})); }} "
            + $"int64 Sum({parameters}) {{ return p0 + p63; }}", 65);
    }

    [Fact]
    public void WideMethodOrdinalUsesTheExistingFunctionLimit()
    {
        var functions = string.Join(" ", Enumerable.Range(0, 1023)
            .Select(index => $"void F{index}() {{ }}"));
        RoundTrip($"int32 Main() {{ F1022(); return 42; }} {functions}", 42);
    }

    [Fact]
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
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var original = Assert.IsType<BoundProgram>(compilation.Program);
        var loaded = TrRmExecutableCompiler.Load(TrRmExecutableCompiler.Emit(original, "calls-audit"));
        Assert.Equal(expected, NativeJit.Execute(original, NativeRuntimePolicy.FailFast));
        Assert.Equal(expected, NativeJit.Execute(loaded, NativeRuntimePolicy.FailFast));
    }
}
