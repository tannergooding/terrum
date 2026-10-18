// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.IO;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests.Modules.TrRm;

public sealed class TrRmIndexedMutationTests
{
    private static BoundProgram Compile(string source, bool library = false)
    {
        var compilation = library ? Compilation.CreateLibrary(SyntaxTree.Parse(source))
            : Compilation.Create(SyntaxTree.Parse(source));
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        return ((global::System.Func<BoundProgram>)(() => { var __nunitActual0 = compilation.Program; Assert.That(__nunitActual0, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual0!; }))();
    }

    private static void RoundTrip(string source, int expected = 42)
    {
        var original = Compile(source);
        var artifact = TrRmExecutableCompiler.Emit(original, "indexed-mutations");
        TrRmStructuralMetadataValidator.ValidateCompact(artifact);
        var loaded = TrRmExecutableCompiler.Load(artifact);
        var index = new TrRmArtifactIndex(artifact);
        Assert.That(index.TryGetSection(0x20, out var bodyBytes, out var count), Is.True);
        Assert.That(new TrRmBodyTableReader(bodyBytes, count).GetRow(1)[0] & 0x0C, Is.EqualTo(0x08));
        Assert.That(NativeJit.Execute(original, NativeRuntimePolicy.FailFast), Is.EqualTo(expected));
        Assert.That(NativeJit.Execute(loaded, NativeRuntimePolicy.FailFast), Is.EqualTo(expected));
        Assert.That(NativeEmitter.Emit(loaded, NativeRuntimePolicy.FailFast), Is.Not.Empty);
    }

    private static byte[] IndependentConvertBody(byte policy) =>
    [
        4, 2, 1, 0, 0, 0, 4, 0, 0, 0, 2,
        1, 1, 1, 42, 6, 2, 2, policy, 1, 0x83, 1,
    ];
    [TestCase("int32")]
    [TestCase("uint32")]
    [TestCase("int64")]
    [TestCase("uint64")]
    public void AllIntegerCompoundsAndShiftsAreIndexed(string type)
    {
        (string Operator, int Result)[] cases =
        [
            ("+=", 22), ("-=", 18), ("*=", 40), ("/=", 10), ("%=", 0),
            ("&=", 0), ("|=", 22), ("^=", 22), ("<<=", 80), (">>=", 5), (">>>=", 5),
        ];
        foreach (var (operation, result) in cases)
        {
            var right = operation.Contains('<') || operation.Contains('>') ? "2" : $"{type}(2)";
            RoundTrip($"int32 Main() {{ mut {type} x = {type}(20); x {operation} {right}; return x == {type}({result}) ? 42 : 0; }}");
        }
        foreach (var op in new[] { "<<", ">>", ">>>" })
        {
            var result = op == "<<" ? 80 : 5;
            RoundTrip($"int32 Main() {{ {type} x = {type}(20); return (x {op} 2) == {type}({result}) ? 42 : 0; }}");
        }
    }
    [TestCase("int32")]
    [TestCase("uint32")]
    [TestCase("int64")]
    [TestCase("uint64")]
    public void PrefixPostfixAndNestedRhsUseOriginalSnapshot(string type)
    {
        RoundTrip($"int32 Main() {{ mut {type} x = {type}(20); {type} a = x++; {type} b = ++x; {type} c = x--; {type} d = --x; return a == {type}(20) && b == {type}(22) && c == {type}(22) && d == {type}(20) && x == {type}(20) ? 42 : 0; }}");
        RoundTrip($"int32 Main() {{ mut {type} x = {type}(20); x += x++; return x == {type}(40) ? 42 : 0; }}");
        RoundTrip($"int32 Main() {{ mut {type} x = {type}(20); x += ++x; return x == {type}(41) ? 42 : 0; }}");
        RoundTrip($"int32 Main() {{ mut {type} x = {type}(20); x += x++ + ++x; return x == {type}(62) ? 42 : 0; }}");
        RoundTrip($"int32 Main() {{ mut {type} x = {type}(20); x <<= int32(x++ - {type}(19)); return x == {type}(40) ? 42 : 0; }}");
    }
    [TestCase("int32")]
    [TestCase("uint32")]
    [TestCase("int64")]
    [TestCase("uint64")]
    public void ConditionMutationsAndLoopEdgesCarryNewMapping(string type)
    {
        RoundTrip($$"""
            int32 Main() {
                mut {{type}} x = {{type}}(0);
                mut int32 count = 0;
                while (x++ < {{type}}(4)) {
                    if (x == {{type}}(2) || (x++ == {{type}}(3) && ++count == 1)) { continue; }
                    if (x == {{type}}(5)) { break; }
                    ++count;
                }
                return x == {{type}}(5) && count == 3 ? 42 : 0;
            }
            """);
        RoundTrip($"int32 Main() {{ mut {type} x = {type}(20); if (false && x++ == {type}(20) || true) {{ }} return x == {type}(20) ? 42 : 0; }}");
    }
    [TestCase("int32","int32.MinValue","int32(1073741824)")]
    [TestCase("uint32","uint32.MaxValue","uint32(2147483647)")]
    [TestCase("int64","int64.MinValue","int64(4611686018427387904)")]
    [TestCase("uint64","uint64.MaxValue","uint64(9223372036854775807)")]
    public void HighBitsAndZeroCountPreserveRepresentation(string type, string high,
        string shifted)
    {
        RoundTrip($"int32 Main() {{ {type} x = {high}; return (x << 0) == {high} && (x >> 0) == {high} && (x >>> 0) == {high} ? 42 : 0; }}");
        RoundTrip($"int32 Main() {{ {type} x = {high}; return (x >>> 1) == {shifted} ? 42 : 0; }}");
        RoundTrip($"int32 Main() {{ mut {type} x = {high}; x >>>= 1; return x == {shifted} ? 42 : 0; }}");
    }
    [TestCase("int32","int32.MinValue")]
    [TestCase("int64","int64.MinValue")]
    public void SignedMinimumRemainderPreservesNativeSpecialCase(string type, string minimum)
        => RoundTrip($"int32 Main() {{ mut {type} x = {minimum}; x %= {type}(-1); return x == {type}(0) ? 42 : 0; }}");
    [TestCase("int32 Main() { mut bool x = true; x++; return 42; }")]
    [TestCase("int32 Main() { mut char16 x = char16(1); x += char16(1); return 42; }")]
    [TestCase("int32 Main() { int32 x = 1; x++; return 42; }")]
    public void InvalidSourcePlacesAndTypesStayExcluded(string source)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        Assert.That(compilation.HasErrors, Is.True);
    }
    [TestCase("[NoThrow] int32 Main() { mut int32 x = 1; x++; return 42; }")]
    [TestCase("[NoThrow] int32 Main() { mut uint32 x = 1; x >>= 0; return 42; }")]
    [TestCase("[NoThrow] int32 Main() { mut int32 x = 1; return false ? x << 1 : 42; }")]
    public void NoThrowRejectsRetainedCheckedOperations(string source)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        Assert.That(compilation.HasErrors, Is.True);
    }

    [Test]
    public void IndependentLinearPolicyThreeAndCheckedShiftsExecuteWithoutSource()
    {
        // Value 1 is signed high-bit payload; value 2 is signed zero count.
        // Values 3 and 5 reinterpret bits, 4 shifts unsigned, 6 shifts signed.
        byte[] body =
        [
            4, 2, 1, 0, 0, 0, 4, 0, 0, 0, 6,
            0xF0, 0x20, 1, 1, 1, 0, 0, 0, 0x80, 1, 2, 1, 0,
            6, 3, 2, 3, 1, 6, 4, 2, 3, 2,
            0x15, 5, 7, 3, 4, 6, 6, 1, 3, 5, 0x83, 6,
        ];
        var loaded = TrRmExecutableCompiler.Load(
            TrRmExecutionAuditTests.Artifact(body, entry: true));
        Assert.That(NativeJit.Execute(loaded, NativeRuntimePolicy.FailFast), Is.EqualTo(int.MinValue));
    }
    [TestCase((byte)(0))]
    [TestCase((byte)(4))]
    public void IndependentInvalidConvertPoliciesAreRejected(byte policy)
    {
        Assert.Throws<InvalidDataException>(() => TrRmExecutableCompiler.Load(
            TrRmExecutionAuditTests.Artifact(IndependentConvertBody(policy))));
    }
    [TestCase((byte)(1))]
    [TestCase((byte)(2))]
    public void IndependentConvertPoliciesExecute(byte policy)
    {
        var program = TrRmExecutableCompiler.Load(
            TrRmExecutionAuditTests.Artifact(IndependentConvertBody(policy)));
        Assert.That(NativeJit.Execute(program, NativeRuntimePolicy.FailFast), Is.EqualTo(42));
    }

    [Test]
    public void NoThrowAllowsOnlyTotalIndependentBitReinterpretation()
    {
        byte[] body = [4, 2, 1, 0, 0, 0, 4, 0, 0, 0, 3,
            1, 1, 1, 42, 6, 2, 2, 3, 1, 6, 3, 1, 3, 2, 0x83, 3];
        var artifact = TrRmExecutionAuditTests.Artifact(body, effect: 1);
        var program = TrRmExecutableCompiler.Load(artifact);
        Assert.That(program.Functions[0].IsNoThrow, Is.True);
        Assert.That(NativeJit.Execute(program, NativeRuntimePolicy.FailFast), Is.EqualTo(42));
        Assert.Throws<ModuleFormatException>(() => ModuleCompiler.CreateProducer(program, ["Main"]));
    }
    [TestCase((byte)(2),(byte)(4))]
    [TestCase((byte)(3),(byte)(4))]
    [TestCase((byte)(1),(byte)(6))]
    public void IndependentReinterpretRejectsBoolCharAndMismatchedWidths(
        byte sourceRow, byte destinationRow)
    {
        byte[] body = [4, 2, sourceRow, 0, 0, 0, destinationRow, 0, 0, 0, 2,
            1, 1, 1, 1, 6, 2, 2, 3, 1, 0x83, 1];
        Assert.Throws<InvalidDataException>(() => TrRmExecutableCompiler.Load(
            TrRmExecutionAuditTests.Artifact(body)));
    }
    [TestCase((byte)(6),(byte)(2),(byte)(1))]
    [TestCase((byte)(7),(byte)(1),(byte)(1))]
    [TestCase((byte)(8),(byte)(1),(byte)(2))]
    public void IndependentShiftRejectsWrongKindAndOperandTypes(
        byte kind, byte left, byte right)
    {
        byte[] body = [4, 2, 1, 0, 0, 0, 4, 0, 0, 0, 3,
            1, 1, 1, 1, 1, 2, 2, 1, 0x15, 3, kind, left, right, 0x83, 3];
        Assert.Throws<InvalidDataException>(() => TrRmExecutableCompiler.Load(
            TrRmExecutionAuditTests.Artifact(body)));
    }
    [TestCase(32u)]
    [TestCase(0x8000_0000u)]
    [TestCase(uint.MaxValue)]
    public void IndependentRawUnsignedCountRetainsTheCheckedFailureBoundary(uint count)
    {
        var body = RawCountShiftBody(count);
        TrRmStructuralMetadataValidator.ValidateCompactLinear(
            TrRmExecutionAuditTests.Artifact(body));
        var loaded = TrRmExecutableCompiler.Load(TrRmExecutionAuditTests.Artifact(body));
        Assert.That(NativeEmitter.Emit(loaded, NativeRuntimePolicy.FailFast), Is.Not.Empty);
        Assert.Throws<InvalidDataException>(() => TrRmExecutableCompiler.Load(
            TrRmExecutionAuditTests.Artifact(body, effect: 1)));
    }

    internal static byte[] RawCountShiftBody(uint count)
    {
        var bytes = new List<byte>
        {
            4, 2, 1, 0, 0, 0, 4, 0, 0, 0, 3,
            1, 1, 1, 1,
        };
        var width = count <= byte.MaxValue ? 1 : count <= ushort.MaxValue ? 2 : 4;
        if (width != 1)
        {
            bytes.AddRange([0xF0, (byte)(width == 2 ? 0x10 : 0x20)]);
        }
        bytes.AddRange([1, 2, 2]);
        for (var index = 0; index < width; index++)
        {
            bytes.Add((byte)((count >> (index * 8)) & byte.MaxValue));
        }
        bytes.AddRange([0x15, 3, 6, 1, 2, 0x83, 3]);
        return bytes.ToArray();
    }
}
