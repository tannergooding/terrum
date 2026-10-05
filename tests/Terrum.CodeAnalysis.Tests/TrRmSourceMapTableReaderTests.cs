// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.IO;
using Terrum.CodeAnalysis.Modules;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed class TrRmSourceMapTableReaderTests
{
    [Fact]
    public void ChecksOneBodyInstructionSpanAgainstItsModulesSourceFile()
    {
        Create(Map());
        Assert.Throws<InvalidDataException>(() => Create(Map(), crossModule: true));
    }

    [Theory]
    [InlineData(4, 0)]
    [InlineData(6, 0)]
    [InlineData(8, 2)]
    [InlineData(12, 0)]
    [InlineData(20, 0)]
    [InlineData(24, 2)]
    [InlineData(36, 0)]
    [InlineData(36, 2)]
    [InlineData(40, 0)]
    [InlineData(40, 1)]
    public void RejectsInvalidSlotTargetsSelectorsAndRowReferences(int offset, byte value)
    {
        var bytes = Map();
        bytes[offset] = value;
        Assert.Throws<InvalidDataException>(() => Create(bytes));
    }

    [Fact]
    public void RejectsUnsortedRowsOverflowedSpansAndMissingPrelude()
    {
        var duplicate = new byte[68];
        Map().CopyTo(duplicate, 0);
        Map().AsSpan(36, 16).CopyTo(duplicate.AsSpan(52));
        Assert.Throws<InvalidDataException>(() => Create(duplicate, recordCount: 2));

        var overflow = Map();
        Write(overflow, 44, uint.MaxValue);
        Assert.Throws<InvalidDataException>(() => Create(overflow));
        Assert.Throws<InvalidDataException>(() => Create(Map()[..^1]));
    }

    [Fact]
    public void RejectsRowsThatReferenceSlotsOfTheWrongKind()
    {
        var bytes = Map();
        Write(bytes, 36, 2);
        Assert.Throws<InvalidDataException>(() => Create(bytes));
    }

    [Fact]
    public void MultipleMappedInstructionsMustFollowSlotOrder()
    {
        var section = new byte[84];
        Write(section, 0, 3);
        Map().AsSpan(4, 32).CopyTo(section.AsSpan(4));
        WriteU16(section, 36, 0x0020);
        WriteU16(section, 38, 1);
        Write(section, 40, 2);
        Write(section, 44, 1);
        Map().AsSpan(36, 16).CopyTo(section.AsSpan(52));
        Write(section, 68, 3);
        Write(section, 72, 2);
        Write(section, 80, 1);
        Create(section, recordCount: 2, twoMethods: true);

        Write(section, 52, 3);
        Write(section, 68, 1);
        Assert.Throws<InvalidDataException>(() =>
            Create(section, recordCount: 2, twoMethods: true));
    }

    [Fact]
    public void WriterMatchesWorkedBytesAndSharesSourceFileSlots()
    {
        var first = new TrRmSourceMapEntry(1, 1, 0, 1, 0, 2);
        Assert.Equal(Map(), Encode([first]));
        var second = first with { Ordinal = 1, StartUtf16 = 2, LengthUtf16 = 3 };
        var section = Encode([first, second]);
        Assert.Equal(84, section.Length);
        Assert.Equal((uint)3, BinaryPrimitives.ReadUInt32LittleEndian(section));
        Assert.Equal((uint)3, BinaryPrimitives.ReadUInt32LittleEndian(section.AsSpan(56)));
        Assert.Equal((uint)3, BinaryPrimitives.ReadUInt32LittleEndian(section.AsSpan(72)));
        Create(section, recordCount: 2);
    }

    [Fact]
    public void WriterRejectsUnboundDuplicateAndOverflowedMappings()
    {
        var entry = new TrRmSourceMapEntry(1, 1, 0, 1, 0, 2);
        Assert.Throws<ArgumentOutOfRangeException>(() => Encode([]));
        Assert.Throws<InvalidDataException>(() => Encode([entry, entry]));
        Assert.Throws<InvalidDataException>(() => Encode([entry with { BodyRow = 0 }]));
        Assert.Throws<InvalidDataException>(() => Encode([entry with { BodyRow = 2 }]));
        Assert.Throws<InvalidDataException>(() => Encode([entry with { Block = 0 }]));
        Assert.Throws<InvalidDataException>(() => Encode([entry with { Ordinal = -1 }]));
        Assert.Throws<InvalidDataException>(() => Encode([entry with { SourceFileRow = 0 }]));
        Assert.Throws<InvalidDataException>(() => Encode([entry with { SourceFileRow = 2 }]));
        Assert.Throws<InvalidDataException>(() =>
            Encode([entry with { StartUtf16 = uint.MaxValue }]));
        Assert.Throws<InvalidDataException>(() => Encode([entry], crossModule: true));
    }

    private static byte[] Encode(TrRmSourceMapEntry[] entries, bool crossModule = false)
    {
        var names = new TrRmStringTableReader(TrRmStringTableWriter.EncodeNames(
            [("Module", 0), ("Run", 0), ("src/file", 0), ("Other", 0)]), 4);
        var modules = new TrRmModuleTableReader(Modules(crossModule),
            crossModule ? 2 : 1, names, 0, 0, 1, 1);
        var types = new TrRmTypeDefTableReader([], 0, names, modules, 0, 0, 0, 0);
        byte[] specRow = [0, 0, 0, 0, 3, 0, 0, 0, 1, 0, 8];
        var typeSpecs = new TrRmTypeSpecTableReader(specRow, 1, 0, 0, 0, 0);
        var signature = new byte[24];
        Write(signature, 0, 1);
        var signatures = new TrRmMethodSigTableReader(signature, 1, typeSpecs, 0, 0);
        var method = new byte[28];
        Write(method, 4, 2);
        Write(method, 8, 1);
        Write(method, 24, 1);
        var methods = new TrRmMethodDefTableReader(method, 1,
            names, modules, types, signatures, 0, 1);
        var source = new byte[8];
        Write(source, 0, 3);
        var files = new TrRmSourceFileTableReader(source, 1, names, modules);
        return TrRmSourceMapTableWriter.Encode(entries, methods, files);
    }

    private static void Create(byte[] section, int recordCount = 1,
        bool crossModule = false, bool twoMethods = false)
    {
        var names = new TrRmStringTableReader(TrRmStringTableWriter.EncodeNames(
            [("Module", 0), ("Run", 0), ("src/file", 0), ("Other", 0)]), 4);
        var modules = new TrRmModuleTableReader(Modules(crossModule),
            crossModule ? 2 : 1, names, 0, 0, twoMethods ? 2 : 1, 1);
        var types = new TrRmTypeDefTableReader([], 0, names, modules, 0, 0, 0, 0);
        byte[] specRow = [0, 0, 0, 0, 3, 0, 0, 0, 1, 0, 8];
        var typeSpecs = new TrRmTypeSpecTableReader(specRow, 1, 0, 0, 0, 0);
        var signature = new byte[24];
        Write(signature, 0, 1);
        var signatures = new TrRmMethodSigTableReader(signature, 1, typeSpecs, 0, 0);
        var method = new byte[twoMethods ? 56 : 28];
        Write(method, 4, 2);
        Write(method, 8, 1);
        Write(method, 24, 1);
        if (twoMethods)
        {
            Write(method, 32, 2);
            Write(method, 36, 1);
            Write(method, 52, 2);
        }

        var methods = new TrRmMethodDefTableReader(method, twoMethods ? 2 : 1,
            names, modules, types, signatures, 0, twoMethods ? 2 : 1);
        var source = new byte[8];
        Write(source, 0, 3);
        var files = new TrRmSourceFileTableReader(source, 1, names, modules);
        _ = new TrRmSourceMapTableReader(section, recordCount, methods, files);
    }

    private static byte[] Modules(bool crossModule)
    {
        var rows = new byte[crossModule ? 88 : 44];
        Write(rows, 0, 1);
        Write(rows, 4, 1);
        Write(rows, 28, 1);
        Write(rows, 32, 1);
        Write(rows, 36, 1);
        Write(rows, 40, 1);
        if (crossModule)
        {
            Write(rows, 44, 4);
            Write(rows, 48, 1);
            Write(rows, 72, 1);
            Write(rows, 76, 1);
            Write(rows, 80, 2);
            Write(rows, 84, 1);
        }

        return rows;
    }

    private static byte[] Map()
    {
        var bytes = new byte[52];
        Write(bytes, 0, 2);
        WriteU16(bytes, 4, 0x0020);
        WriteU16(bytes, 6, 1);
        Write(bytes, 8, 1);
        Write(bytes, 12, 1);
        WriteU16(bytes, 20, 0x001F);
        Write(bytes, 24, 1);
        Write(bytes, 36, 1);
        Write(bytes, 40, 2);
        Write(bytes, 48, 2);
        return bytes;
    }

    private static void Write(byte[] bytes, int offset, uint value)
        => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);

    private static void WriteU16(byte[] bytes, int offset, ushort value)
        => BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(offset), value);
}
