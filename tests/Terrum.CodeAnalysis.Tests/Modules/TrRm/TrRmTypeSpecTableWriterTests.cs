// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.IO;
using Terrum.CodeAnalysis.Modules;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests.Modules.TrRm;

public sealed class TrRmTypeSpecTableWriterTests
{
    [Test]
    public void EncodesDependentRowsAndOwnsTheirInput()
    {
        byte[] primitive = [1, 0, 8];
        var bytes = TrRmTypeSpecTableWriter.Encode([
            primitive,
            new byte[] { 2, 0, 1, 0, 0, 0 },
            new byte[] { 5, 0, 2, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0 },
        ], 1, 0, 0, 1);
        primitive[2] = 9;

        var reader = new TrRmTypeSpecTableReader(bytes, 3, 1, 0, 0, 1);
        Assert.That(reader.GetRow(1).ToArray(), Is.EqualTo(new byte[] { 1, 0, 8 }));
        Assert.That(reader.GetRow(3)[0], Is.EqualTo((byte)5));
        Assert.That(bytes.Length, Is.EqualTo(39));
    }

    [Test]
    public void RejectsMalformedRowsAndMissingReferencedTables()
    {
        Assert.Throws<ArgumentNullException>(() =>
            TrRmTypeSpecTableWriter.Encode(null!, 0, 0, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            TrRmTypeSpecTableWriter.Encode([], 0, 0, 0, 0));
        Assert.Throws<InvalidDataException>(() =>
            TrRmTypeSpecTableWriter.Encode([ReadOnlyMemory<byte>.Empty], 0, 0, 0, 0));
        Assert.Throws<InvalidDataException>(() =>
            TrRmTypeSpecTableWriter.Encode([new byte[] { 1, 0, 0x1A }], 0, 0, 0, 0));
        Assert.Throws<InvalidDataException>(() =>
            TrRmTypeSpecTableWriter.Encode([new byte[] { 2, 0, 1, 0, 0, 0 }], 0, 0, 0, 0));
        Assert.Throws<InvalidDataException>(() =>
            TrRmTypeSpecTableWriter.Encode([new byte[] { 7, 0, 1, 0, 0, 0 }], 0, 0, 0, 0));
    }

    [Test]
    public void TypedConstructorsCoverAssignedTypeSpecForms()
    {
        ReadOnlyMemory<byte>[] rows = [
            TrRmTypeSpecTableWriter.Builtin(8),
            TrRmTypeSpecTableWriter.InlineFamily(0x80, 8),
            TrRmTypeSpecTableWriter.Reference(0x02, 1),
            TrRmTypeSpecTableWriter.Reference(0x03, 1),
            TrRmTypeSpecTableWriter.Reference(0x04, 1),
            TrRmTypeSpecTableWriter.Constructed(3, 1, 1),
            TrRmTypeSpecTableWriter.Wrapped(0x06, 1),
            TrRmTypeSpecTableWriter.Wrapped(0x07, 3),
            TrRmTypeSpecTableWriter.Wrapped(0x08, 3),
            TrRmTypeSpecTableWriter.Wrapped(0x09, 3),
            TrRmTypeSpecTableWriter.Wrapped(0x0A, 3),
            TrRmTypeSpecTableWriter.Wrapped(0x0B, 3),
            TrRmTypeSpecTableWriter.IndexedFamily(0x80, 3),
        ];
        var bytes = TrRmTypeSpecTableWriter.Encode(rows, 1, 1, 1, 1);
        var reader = new TrRmTypeSpecTableReader(bytes, rows.Length, 1, 1, 1, 1);
        for (var index = 0; index < rows.Length; index++)
        {
            Assert.That(reader.GetRow(index + 1).ToArray(), Is.EqualTo(rows[index].ToArray()));
        }

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            TrRmTypeSpecTableWriter.Reference(0x05, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            TrRmTypeSpecTableWriter.Wrapped(0x05, 1));
        Assert.Throws<InvalidDataException>(() => TrRmTypeSpecTableWriter.Encode([
            TrRmTypeSpecTableWriter.Builtin(8),
            TrRmTypeSpecTableWriter.Wrapped(0x07, 1),
        ], 0, 0, 0, 0));
        Assert.Throws<InvalidDataException>(() => TrRmTypeSpecTableWriter.Encode([
            TrRmTypeSpecTableWriter.InlineFamily(0x80, 0x1A),
        ], 0, 0, 0, 0));
        Assert.Throws<InvalidDataException>(() => TrRmTypeSpecTableWriter.Encode([
            TrRmTypeSpecTableWriter.Reference(0x02, 1),
            TrRmTypeSpecTableWriter.Constructed(1, 1, 2),
        ], 1, 0, 0, 1));
    }

    [Test]
    public void TypedPayloadsUseTheSpecifiedLittleEndianFieldOrder()
    {
        Assert.That(TrRmTypeSpecTableWriter.Builtin(8, 1), Is.EqualTo(new byte[] { 1, 1, 8 }));
        Assert.That(TrRmTypeSpecTableWriter.InlineFamily(0x80, 8), Is.EqualTo(new byte[] { 1, 0, 0x80, 8 }));
        Assert.That(TrRmTypeSpecTableWriter.Reference(2, 0x00A1_B2C3, 1), Is.EqualTo(new byte[] { 2, 1, 0xC3, 0xB2, 0xA1, 0 }));
        Assert.That(TrRmTypeSpecTableWriter.Constructed(3, 1, 2), Is.EqualTo(new byte[] {
            5, 0, 3, 0, 0, 0, 1, 0, 0, 0, 2, 0, 0, 0,
        }));
        Assert.That(TrRmTypeSpecTableWriter.Wrapped(6, 1), Is.EqualTo(new byte[] { 6, 0, 1, 0, 0, 0 }));
        Assert.That(TrRmTypeSpecTableWriter.IndexedFamily(0x80, 1), Is.EqualTo(new byte[] { 12, 0, 0x80, 1, 0, 0, 0 }));
    }
}
