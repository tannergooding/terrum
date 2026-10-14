// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.IO;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests;

public sealed class TrRmDoForAuditTests
{
    // Entry defines zero/one/limit. The loop header increments before testing and
    // passes the replacement to itself or the exit: do { x++; } while (x < limit).
    internal static byte[] PostTestBody(byte limit) =>
    [
        8, 3, 7, 1, 2, 1, 0, 0, 0, 2, 0, 0, 0,
        1, 0, 2, 3, 0,
        1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 2, 1, 1, 1,
        0, 3, 6, 7,
        9, 13, 17, 255, 23, 28, 255,
        0, 25, 66, 75,
        21, 33, 7,
        0x18, 3, 1, 2, 3, 1, 1, 3, 3,
        1, 1, 1, 0, 1, 2, 1, 1, 1, 3, 1, limit,
        0x80, 2, 1, 1,
        0x1F, 1, 1, 4, 2, 2, 3, 3, 2, 3, 5,
        5, 2, 0, 3, 1, 4, 0, 5, 2, 6, 2, 2,
        0x15, 5, 1, 4, 2, 5, 6, 6, 5, 3,
        0x81, 6, 2, 1, 5, 3, 1, 5,
        0x12, 1, 7, 1, 7, 0, 0, 0x83, 7,
    ];

    // Pre-test header, body increment, distinct iterator increment, then backedge.
    internal static byte[] IteratorBody(byte limit) =>
    [
        8, 5, 9, 1, 2, 1, 0, 0, 0, 2, 0, 0, 0,
        1, 0, 2, 5, 0,
        1, 1, 1, 2, 1, 1, 1, 2, 2, 1, 1, 1, 1, 1, 1, 1, 1, 1,
        0, 3, 5, 6, 8, 9,
        9, 13, 17, 255, 19, 18, 255, 19, 255,
        0, 25, 56, 83, 111, 120,
        21, 24, 23, 24, 7,
        0x18, 3, 1, 2, 3, 1, 1, 3, 3,
        1, 1, 1, 0, 1, 2, 1, 1, 1, 3, 1, limit,
        0x80, 2, 1, 1,
        0x1F, 1, 1, 4, 2, 2, 3, 3, 2, 3, 4,
        3, 3, 0, 4, 1, 5, 1, 1,
        5, 5, 6, 4, 3, 0x81, 5, 3, 0, 5, 1, 4,
        0x1D, 1, 3, 2, 3, 4, 3, 2, 3, 6,
        3, 2, 0, 4, 0, 6, 1, 1,
        0x15, 6, 1, 4, 2, 0x80, 4, 1, 6,
        0x1F, 1, 1, 7, 2, 2, 3, 3, 2, 3, 8,
        3, 2, 0, 7, 0, 8, 1, 1,
        0x15, 8, 1, 7, 2, 0x80, 2, 1, 8,
        0x12, 1, 9, 1, 9, 0, 0, 0x83, 9,
    ];
    [TestCase((byte)0,0)]
    [TestCase((byte)3,4)]
    [TestCase((byte)4,4)]
    public void HandwrittenIteratorCarriesBodyAndIteratorReplacements(byte limit, int expected)
    {
        var artifact = TrRmExecutionAuditTests.Artifact(IteratorBody(limit));
        TrRmStructuralMetadataValidator.ValidateCompactIndexed(artifact);
        Assert.That(NativeJit.Execute(
            TrRmExecutableCompiler.Load(artifact), NativeRuntimePolicy.FailFast), Is.EqualTo(expected));
    }
    [TestCase(13,(byte)(2))]
    [TestCase(16,(byte)(4))]
    [TestCase(84,(byte)(4))]
    [TestCase(88,(byte)(0))]
    [TestCase(170,(byte)(3))]
    public void IteratorCannotBecomeASecondLoopEntryOrCycleHeader(int offset, byte value)
    {
        var body = IteratorBody(3);
        Assert.That(value, Is.Not.EqualTo(body[offset]));
        body[offset] = value;
        Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.Load(TrRmExecutionAuditTests.Artifact(body)));
    }
    [TestCase((byte)0,1)]
    [TestCase((byte)1,1)]
    [TestCase((byte)3,3)]
    public void HandwrittenBodyHeaderExecutesBeforeCondition(byte limit, int expected)
    {
        var artifact = TrRmExecutionAuditTests.Artifact(PostTestBody(limit));
        TrRmStructuralMetadataValidator.ValidateCompactIndexed(artifact);
        var program = TrRmExecutableCompiler.Load(artifact);
        Assert.That(NativeJit.Execute(program, NativeRuntimePolicy.FailFast), Is.EqualTo(expected));
        Assert.That(NativeEmitter.Emit(program, NativeRuntimePolicy.FailFast), Is.Not.Empty);
    }
    [TestCase(9,(byte)(1))]
    [TestCase(13,(byte)(2))]
    [TestCase(14,(byte)(1))]
    [TestCase(15,(byte)(3))]
    [TestCase(16,(byte)(2))]
    [TestCase(17,(byte)(1))]
    [TestCase(27,(byte)(2))]
    [TestCase(28,(byte)(1))]
    [TestCase(34,(byte)(5))]
    [TestCase(39,(byte)(0))]
    [TestCase(40,(byte)(24))]
    [TestCase(44,(byte)(24))]
    [TestCase(48,(byte)(32))]
    [TestCase(72,(byte)(3))]
    [TestCase(76,(byte)(0))]
    [TestCase(81,(byte)(4))]
    [TestCase(85,(byte)(4))]
    [TestCase(89,(byte)(0))]
    [TestCase(109,(byte)(4))]
    [TestCase(110,(byte)(3))]
    [TestCase(112,(byte)(6))]
    [TestCase(113,(byte)(1))]
    public void ForgedPostTestIndexesRegionsTypesAndInventoriesReject(int offset, byte value)
    {
        var body = PostTestBody(3);
        Assert.That(value, Is.Not.EqualTo(body[offset]));
        body[offset] = value;
        Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.Load(TrRmExecutionAuditTests.Artifact(body)));
    }

    [Test]
    public void ExitParameterCannotDominateTheLoopEvenWithMatchingUseInventories()
    {
        var body = PostTestBody(3);
        // Replace the loop's use of the dominating increment constant v2 with
        // exit parameter v7, updating its descriptor, live lists and last uses.
        body[21] = 0;
        body[31] = 2;
        body[53] = 3;
        body[54] = 7;
        body[80] = 3;
        body[81] = 7;
        body[83] = 3;
        body[84] = 5;
        body[85] = 7;
        byte[] uses = [3, 1, 4, 0, 5, 2, 6, 2, 7, 0];
        uses.CopyTo(body, 87);
        body[102] = 7;
        Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.Load(TrRmExecutionAuditTests.Artifact(body)));
    }

    [Test]
    public void PostTestOperationsCannotAcquireNoThrowFromTheLoopCondition()
    {
        var artifact = TrRmExecutionAuditTests.Artifact(PostTestBody(0), effect: 1);
        TrRmStructuralMetadataValidator.ValidateCompactIndexed(artifact);
        Assert.Throws<InvalidDataException>(() => TrRmExecutableCompiler.Load(artifact));
    }

    [Test]
    public void EveryTruncatedPostTestBodyRejects()
    {
        var body = PostTestBody(3);
        for (var length = 1; length < body.Length; length++)
        {
            Assert.Throws<InvalidDataException>(() =>
                TrRmExecutableCompiler.Load(TrRmExecutionAuditTests.Artifact(body[..length])));
        }
    }
}
