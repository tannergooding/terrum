// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests;

public sealed class TrRmNamedTransferTests
{
    [TestCase("mut int32 x = 0; outer: while (x < 3) { mut int32 y = 0; while (y++ < 2) { x++; if (x == 2) { break outer; } } } return x == 2 ? 42 : 0;")]
    [TestCase("mut int32 x = 0; mut int32 skip = 0; outer: while (x < 3) { mut int32 y = 0; while (y++ < 2) { x++; if (x == 3) { continue outer; } } skip++; } return x == 3 && skip == 1 ? 42 : 0;")]
    [TestCase("mut int32 x = 0; mut int32 sum = 0; outer: for (; x < 3; x++) { mut int32 y = 0; while (y++ < 2) { if (x == 1) { continue outer; } } sum += x; } return x == 3 && sum == 2 ? 42 : 0;")]
    [TestCase("mut int32 x = 0; mut int32 seen = 0; outer: do { mut int32 y = 0; while (y++ < 2) { x++; if (x == 3) { continue outer; } } seen++; } while (x < 3); return x == 3 && seen == 1 ? 42 : 0;")]
    [TestCase("mut int32 x = 0; mut int32 effects = 0; outer: for (; x < 3; effects++) { mut int32 y = 0; do { x++; if (x == 2) { break outer; } } while (y++ < 1); effects += 10; } return x == 2 && effects == 0 ? 42 : 0;")]
    [TestCase("mut int32 x = 0; mut int32 effects = 0; outer: do { mut int32 y = 0; for (; y < 2; effects++) { x++; if (x == 2) { break outer; } y++; } } while (effects++ < 3); return x == 2 && effects == 1 ? 42 : 0;")]
    [TestCase("mut int32 x = 0; mut int32 skipped = 0; outer: while (x < 3) { mut int32 y = 0; while (y++ < 2) { mut int32 z = 0; while (z++ < 2) { x++; if (x == 2) { break outer; } } skipped++; } } return x == 2 && skipped == 0 ? 42 : 0;")]
    [TestCase("mut int32 x = 0; mut int32 skipped = 0; outer: for (; x < 3; x++) { mut int32 y = 0; do { mut int32 z = 0; while (z++ < 2) { if (x == 1) { continue outer; } } y++; } while (y < 1); skipped++; } return x == 3 && skipped == 2 ? 42 : 0;")]
    [TestCase("mut int32 x = 0; @const: while (x < 2) { mut int32 y = 0; while (y++ < 2) { x++; if (x == 1) { continue const; } } } return x == 3 ? 42 : 0;")]
    [TestCase("mut int32 x = 0; mut int32 effects = 0; outer: do { mut int32 y = 0; while (y++ < 2) { if (x++ == 0) { continue outer; } } } while (++effects < 2); return effects == 2 && x == 3 ? 42 : 0;")]
    [TestCase("mut int32 x = 0; outer: while (x < 1) { mut int32 y = 0; while (y++ < 2) { x++; if (x == 1) { break outer; } } } outer: while (x < 2) { mut int32 y = 0; while (y++ < 2) { x++; if (x == 2) { break outer; } } } return x == 2 ? 42 : 0;")]
    [TestCase("mut uint64 x = 18446744073709551613; outer: while (x < 18446744073709551615) { mut int32 y = 0; while (y++ < 2) { x++; if (x == 18446744073709551614) { continue outer; } if (x == 18446744073709551615) { break outer; } } } return x == 18446744073709551615 ? 42 : 0;")]
    [TestCase("mut int32 x = 0; mut int32 skipped = 0; outer: while (x < 3) { mut int32 y = 0; middle: while (y++ < 2) { mut int32 z = 0; while (z++ < 2) { x++; if (x == 1) { continue middle; } if (x == 3) { break outer; } } skipped++; } } return x == 3 && skipped == 0 ? 42 : 0;")]
    [TestCase("mut int32 x = 0; outer: for (; x < 3; x++) { while (true) { if (x < 3) { continue outer; } continue; } } return x == 3 ? 42 : 0;")]
    public void NamedTransfersRoundTripWithoutRegionBypass(string body)
    {
        var source = "int32 Main() { " + body + " }";
        var original = Compile(source, BoundRuntimePolicy.FailFast);
        Assert.That(NativeJit.Execute(original, NativeRuntimePolicy.FailFast), Is.EqualTo(42));
        var bytes = TrRmExecutableCompiler.Emit(original, "named-transfers");
        TrRmStructuralMetadataValidator.ValidateCompact(bytes);
        var loaded = TrRmExecutableCompiler.Load(bytes);
        Assert.That(NativeJit.Execute(loaded, NativeRuntimePolicy.FailFast), Is.EqualTo(42));
        Assert.That(NativeEmitter.Emit(loaded, NativeRuntimePolicy.FailFast), Is.Not.Empty);
        var checkedSource = Compile(source, BoundRuntimePolicy.CheckedExceptions);
        Assert.That(NativeJit.Execute(checkedSource, NativeRuntimePolicy.CheckedExceptions), Is.EqualTo(42));
        Assert.Throws<NotSupportedException>(() => TrRmExecutableCompiler.Emit(checkedSource, "checked"));
    }
    [TestCase("outer: while (true) { while (true) { break outer; } } return 42;")]
    [TestCase("outer: for (;;) { do { continue outer; } while (false); }")]
    [TestCase("outer: do { while (true) { break outer; } } while (false); return 42;")]
    [TestCase("outer: while (true) { for (;;) { continue outer; } }")]
    [TestCase("mut bool choose = false; outer: do { for (;;) { if (choose) { break outer; } continue; } } while (false); return 42;")]
    [TestCase("mut bool choose = false; outer: for (;;) { while (true) { if (choose) { break outer; } continue; } } return 42;")]
    public void NonNearestOnlyAcyclicLoopsCannotGainSyntheticBackedges(string body)
        => Assert.Throws<NotSupportedException>(() => TrRmExecutableCompiler.Emit(
            Compile("int32 Main() { " + body + " }", BoundRuntimePolicy.FailFast), "acyclic-named"));

    [Test]
    public void InactiveUnsupportedDeclarationsRemainAudited()
        => Assert.Throws<NotSupportedException>(() => TrRmExecutableCompiler.Emit(
            Compile("int32 Main() { mut int32 x = 0; outer: while (x < 2) { while (false) { if (false) { init int32 forbidden; } x++; if (x == 1) { continue outer; } } x++; } return 42; }",
                BoundRuntimePolicy.FailFast), "inactive-named"));

    private static BoundProgram Compile(string source, BoundRuntimePolicy policy)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(source), policy);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        return ((global::System.Func<BoundProgram>)(() => { var __nunitActual0 = compilation.Program; Assert.That(__nunitActual0, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual0!; }))();
    }
    [TestCase(true)]
    [TestCase(false)]
    public void RoutingValuesCountAgainstTheExactMaterializedValueLimit(bool supported)
    {
        var baseline = TrRmExecutableCompiler.Load(TrRmExecutableCompiler.Emit(
            Compile(Source(0, 0), BoundRuntimePolicy.FailFast), "routing-budget")).Functions[0].Variables.Length;
        var scopes = (256 - baseline) / 3;
        var unary = (256 - baseline) % 3 + (supported ? 0 : 1);
        var original = Compile(Source(scopes, unary), BoundRuntimePolicy.FailFast);
        if (!supported)
        {
            Assert.Throws<NotSupportedException>(() => TrRmExecutableCompiler.Emit(original, "257-routing-values"));
            return;
        }
        var loaded = TrRmExecutableCompiler.Load(TrRmExecutableCompiler.Emit(original, "256-routing-values"));
        Assert.That(loaded.Functions[0].Variables.Length, Is.EqualTo(256));
        Assert.That(NativeJit.Execute(loaded, NativeRuntimePolicy.FailFast), Is.EqualTo(scopes + 1));

        static string Source(int scopes, int unary)
        {
            var source = new StringBuilder("int32 Main() { mut int32 x = 0; outer: while (x < 1) { ");
            for (var index = 0; index < scopes; index++)
            {
                source.Append($"{{ int32 local = {index}; x = x + 1; }} ");
            }
            source.Append("mut int32 y = 0; while (y < 2) { y++; if (y == 1) { x = x + 1; continue outer; } } } return ");
            source.Append(string.Concat(Enumerable.Repeat("+(", unary)));
            source.Append('x').Append(')', unary).Append("; }");
            return source.ToString();
        }
    }

    [Test]
    public void NearestNamedAndUnnamedTransfersKeepIdenticalArtifacts()
    {
        const string body = "int32 Main() { mut int32 x = 0; LABEL while (x < 3) { x++; if (x == 2) { continue TARGET; } if (x == 3) { break TARGET; } } return x; }";
        var unnamed = Compile(body.Replace("LABEL ", "", StringComparison.Ordinal)
            .Replace(" TARGET", "", StringComparison.Ordinal), BoundRuntimePolicy.FailFast);
        var named = Compile(body.Replace("LABEL", "@const:", StringComparison.Ordinal)
            .Replace("TARGET", "const", StringComparison.Ordinal), BoundRuntimePolicy.FailFast);
        Assert.That(TrRmExecutableCompiler.Emit(named, "nearest"), Is.EqualTo(TrRmExecutableCompiler.Emit(unnamed, "nearest")));
    }
    [TestCase(-1)]
    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    [TestCase(4)]
    public void IndependentNestedGraphStillRejectsRegionBypassAndInvalidSsa(int mutation)
    {
        byte[][] blocks = [
            [8, 2, 1, 2, 2, 1, 1, 2, 0, 1, 2, 1, 42, 0x80, 2, 0],
            [29, 1, 2, 1, 2, 2, 1, 2, 1, 1, 0, 0, 0x81, 1, 3, 0, 7, 0],
            [29, 2, 2, 1, 2, 2, 1, 2, 1, 1, 0, 0, 0x81, 1, 4, 0, 5, 0],
            [13, 2, 2, 1, 2, 2, 1, 2, 0, 0x80, 3, 0],
            [29, 1, 2, 1, 2, 2, 1, 2, 1, 1, 0, 0, 0x81, 1, 2, 0, 6, 0],
            [13, 1, 2, 1, 2, 2, 1, 2, 0, 0x80, 2, 0],
            [20, 1, 2, 1, 2, 0, 0, 0x83, 2],
        ];
        if (mutation == 0)
        { blocks[3][10] = 7; }
        if (mutation == 1)
        { blocks[3][10] = 6; }
        if (mutation == 2)
        { blocks[1][14] = 4; }
        var bytes = new List<byte> {
            8, 7, 2, 2, 2, 1, 0, 0, 0, 2, 0, 0, 0,
            1, 0, 2, 7, 0, 1, 1, 3, 5, 0,
            2, 3, 1, 1,
            0, 2, 2, 2, 2, 2, 2, 2,
            5, 9,
        };
        var offset = 0;
        foreach (var block in blocks)
        {
            bytes.Add((byte)offset);
            offset += block.Length;
        }
        bytes.Add((byte)offset);
        bytes.AddRange([13, 12, 12, 9, 12, 9, 7]);
        foreach (var block in blocks)
        { bytes.AddRange(block); }
        if (mutation == 3)
        { bytes[19] = 0; }
        if (mutation == 4)
        { bytes[35] = 9; }
        var artifact = TrRmExecutionAuditTests.Artifact(bytes.ToArray());
        if (mutation == -1)
        {
            TrRmStructuralMetadataValidator.ValidateCompactIndexed(artifact);
            Assert.That(NativeJit.Execute(TrRmExecutableCompiler.Load(artifact), NativeRuntimePolicy.FailFast), Is.EqualTo(42));
        }
        else
        {
            Assert.Throws<InvalidDataException>(() => TrRmExecutableCompiler.Load(artifact));
        }
    }
}
