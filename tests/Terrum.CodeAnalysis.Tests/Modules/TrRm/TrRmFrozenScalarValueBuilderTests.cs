// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Generic;
using NUnit.Framework;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Semantics;

namespace Terrum.CodeAnalysis.Tests.Modules.TrRm;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "NUnit instantiates this fixture through reflection.")]
internal sealed class TrRmFrozenScalarValueBuilderTests
{
    [Test]
    public void WritesAllRegisteredScalarsWithTypedInterningAndSharedBlobs()
    {
        var builder = new TrRmFrozenScalarValueBuilder();
        var zeroValues = new Dictionary<byte, TrRmFrozenScalarValueReference>();
        for (var candidate = 0; candidate <= 0x19; candidate++)
        {
            var typeCode = (byte)candidate;
            if (!TrRmConstantSourceValueValidator.TryGetBoundScalarType(
                typeCode, out var type))
            {
                continue;
            }

            var payload = new byte[PrimitiveTypeFacts.ScalarWidth(type)];
            var constant = new BoundConstant(ScalarBits.FromPayload(type, payload),
                type, default);
            zeroValues.Add(typeCode, builder.Add(constant));
        }

        var int32Payload = new byte[] { 42, 0, 0, 0 };
        var int32Constant = new BoundConstant(
            ScalarBits.FromPayload(PrimitiveType.Int32, int32Payload),
            PrimitiveType.Int32, default);
        var int32Reference = builder.Add(int32Constant);
        var duplicateInt32Reference = builder.Add(int32Constant);

        var char32Constant = new BoundConstant(
            ScalarBits.FromPayload(PrimitiveType.Char32, int32Payload),
            PrimitiveType.Char32, default);
        var char32Reference = builder.Add(char32Constant);

        var tables = builder.Encode(default);
        var blobs = new TrRmBlobTableReader(tables.BlobSection, tables.BlobCount);
        var typeSpecs = default(TrRmTypeSpecTableReader);
        var nodes = new TrRmFrozenValueNodeTableReader(tables.FrozenValueNodeSection,
            tables.FrozenValueNodeCount, typeSpecs, tables.BlobCount, 0, 0);
        var sources = new TrRmConstantSourceTableReader(tables.ConstantSourceSection,
            tables.ConstantSourceCount, tables.FrozenValueNodeCount, 0, 0);

        Assert.That(zeroValues, Has.Count.EqualTo(24));
        Assert.That(tables.FrozenValueNodeCount, Is.EqualTo(26));
        Assert.That(tables.ConstantSourceCount, Is.EqualTo(26));
        Assert.That(tables.BlobCount, Is.EqualTo(6));
        foreach (var (typeCode, reference) in zeroValues)
        {
            Assert.That(reference.TypeSpecRef, Is.EqualTo(0xF200_0000u | typeCode));
            Assert.That(sources.GetKind(checked((int)reference.ConstantSourceRow)),
                Is.EqualTo(1));
            var nodeRow = checked((int)sources.GetPayloadReference(
                checked((int)reference.ConstantSourceRow)));
            Assert.That(nodes.GetTypeSpecRef(nodeRow), Is.EqualTo(reference.TypeSpecRef));
            Assert.That(nodes.GetScalarBlobRef(nodeRow),
                Is.EqualTo(typeCode == 0 ? 0u : (uint)(typeCode switch
                {
                    0x00 => 0,
                    0x01 or 0x02 or 0x06 or 0x0B => 1,
                    0x03 or 0x07 or 0x0C or 0x12 or 0x13 => 2,
                    0x04 or 0x05 or 0x08 or 0x0D or 0x14 or 0x17 => 3,
                    0x09 or 0x0E or 0x15 or 0x18 => 4,
                    _ => 5,
                })));
        }

        Assert.That(duplicateInt32Reference, Is.EqualTo(int32Reference));
        Assert.That(char32Reference.ConstantSourceRow,
            Is.Not.EqualTo(int32Reference.ConstantSourceRow));
        var int32Node = checked((int)sources.GetPayloadReference(
            checked((int)int32Reference.ConstantSourceRow)));
        var char32Node = checked((int)sources.GetPayloadReference(
            checked((int)char32Reference.ConstantSourceRow)));
        Assert.That(nodes.GetTypeSpecRef(int32Node), Is.EqualTo(0xF200_0008u));
        Assert.That(nodes.GetTypeSpecRef(char32Node), Is.EqualTo(0xF200_0004u));
        Assert.That(nodes.GetScalarBlobRef(int32Node),
            Is.EqualTo(nodes.GetScalarBlobRef(char32Node)));
        Assert.That(blobs.GetRow(6).ToArray(), Is.EqualTo(int32Payload));
        Assert.That(nodes.GetScalarBlobRef(checked((int)sources.GetPayloadReference(
            checked((int)zeroValues[0].ConstantSourceRow)))), Is.Zero);
    }

    [Test]
    public void RejectsUnsupportedAndNoncanonicalBoundConstants()
    {
        var builder = new TrRmFrozenScalarValueBuilder();
        var unsupported = new BoundConstant(0,
            PrimitiveType.NativeUtf8ZPointer, default);
        _ = Assert.Throws<NotSupportedException>(() => builder.Add(unsupported));

        var noncanonical = new BoundConstant(new ScalarBits(0x100, 0),
            PrimitiveType.Int8, default);
        _ = Assert.Throws<ArgumentException>(() => builder.Add(noncanonical));
    }
}
