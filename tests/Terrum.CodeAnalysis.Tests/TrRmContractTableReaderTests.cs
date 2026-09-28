// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.IO;
using Terrum.CodeAnalysis.Modules;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed class TrRmContractTableReaderTests
{
    [Fact]
    public void DistinctTypeAndMethodRangesOwnAssignedClauses()
    {
        Create(Section(Cleanup(), Effects()));
        Create([], recordCount: 0, declaredCount: 0,
            typeFirst: 0, typeCount: 0, methodFirst: 0, methodCount: 0);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(3, 0)]
    [InlineData(4, 0)]
    [InlineData(4, 6)]
    [InlineData(6, 1)]
    [InlineData(8, 0)]
    [InlineData(8, 2)]
    public void RejectsMalformedCleanupReferenceKindAndFlags(int offset, byte replacement)
    {
        var row = Cleanup();
        row[offset] = replacement;
        Assert.Throws<InvalidDataException>(() => Create(Section(row, Effects())));
    }

    [Fact]
    public void EffectsMaskRejectsUnassignedBitsAndDuplicateSingletons()
    {
        var effects = Effects();
        effects[8] = 8;
        Assert.Throws<InvalidDataException>(() => Create(Section(Cleanup(), effects)));

        Assert.Throws<InvalidDataException>(() => Create(Section(Effects(), Effects()),
            typeFirst: 0, typeCount: 0, methodFirst: 1, methodCount: 2));
    }

    [Fact]
    public void RejectsOverlapsOrphansAndMismatchedDeclaredCounts()
    {
        var rows = Section(Cleanup(), Effects());
        Assert.Throws<InvalidDataException>(() => Create(rows,
            typeFirst: 1, typeCount: 2, methodFirst: 2, methodCount: 1));
        Assert.Throws<InvalidDataException>(() => Create(Section(Cleanup(), Effects(), Effects()),
            recordCount: 3, declaredCount: 3));
        Assert.Throws<InvalidDataException>(() => Create(rows, declaredCount: 3));
        Assert.Throws<InvalidDataException>(() => Create(rows[..^1]));
    }

    [Fact]
    public void AssignedButNonexecutableClauseShapesAreBounded()
    {
        var output = Contract(0x1600_0001, 2, [0, 0, 0, 0, 1, 0, 0, 0]);
        var transfer = Contract(0x1600_0001, 3, [0, 0, 0, 0, 0, 0, 0, 0]);
        var dependency = Contract(0x1600_0001, 4,
            [0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0]);
        Create(Section(output), recordCount: 1, declaredCount: 1,
            typeFirst: 0, typeCount: 0, methodFirst: 1, methodCount: 1,
            hasOutputParam: true);
        Create(Section(transfer), recordCount: 1, declaredCount: 1,
            typeFirst: 0, typeCount: 0, methodFirst: 1, methodCount: 1);
        Create(Section(dependency), recordCount: 1, declaredCount: 1,
            typeFirst: 0, typeCount: 0, methodFirst: 1, methodCount: 1);

        output[12] = 2;
        Assert.Throws<InvalidDataException>(() => Create(Section(output),
            recordCount: 1, declaredCount: 1,
            typeFirst: 0, typeCount: 0, methodFirst: 1, methodCount: 1,
            hasOutputParam: true));
        transfer[15] = 1;
        Assert.Throws<InvalidDataException>(() => Create(Section(transfer),
            recordCount: 1, declaredCount: 1,
            typeFirst: 0, typeCount: 0, methodFirst: 1, methodCount: 1));
        Assert.Throws<InvalidDataException>(() => Create(Section(Contract(0x1600_0001, 6, [])),
            recordCount: 1, declaredCount: 1,
            typeFirst: 0, typeCount: 0, methodFirst: 1, methodCount: 1));
    }

    [Fact]
    public void OutputStateRequiresBooleanResultAndAnOutputParameterOrdinal()
    {
        var output = Contract(0x1600_0001, 2, [0, 0, 0, 0, 1, 0, 0, 0]);
        var section = Section(output);
        Create(section, recordCount: 1, declaredCount: 1,
            typeFirst: 0, typeCount: 0, methodFirst: 1, methodCount: 1,
            hasOutputParam: true, paramMode: 2);
        Assert.Throws<InvalidDataException>(() => Create(section,
            recordCount: 1, declaredCount: 1,
            typeFirst: 0, typeCount: 0, methodFirst: 1, methodCount: 1));
        Assert.Throws<InvalidDataException>(() => Create(section,
            recordCount: 1, declaredCount: 1,
            typeFirst: 0, typeCount: 0, methodFirst: 1, methodCount: 1,
            hasOutputParam: true, paramMode: 0));
        Assert.Throws<InvalidDataException>(() => Create(section,
            recordCount: 1, declaredCount: 1,
            typeFirst: 0, typeCount: 0, methodFirst: 1, methodCount: 1,
            hasOutputParam: true, nonBooleanResult: true));

        output[8] = 1;
        Assert.Throws<InvalidDataException>(() => Create(Section(output),
            recordCount: 1, declaredCount: 1,
            typeFirst: 0, typeCount: 0, methodFirst: 1, methodCount: 1,
            hasOutputParam: true));
    }

    private static void Create(byte[] section, int recordCount = 2, int declaredCount = 2,
        uint typeFirst = 1, uint typeCount = 1, uint methodFirst = 2, uint methodCount = 1,
        bool hasOutputParam = false, byte paramMode = 1, bool nonBooleanResult = false)
    {
        var names = new TrRmStringTableReader(TrRmStringTableWriter.EncodeTextRows(
            [("", 0), ("Module", 0), ("Widget", 0), ("Run", 0)]), 4);
        var module = new byte[44];
        Write(module, 0, 2);
        Write(module, 4, 1);
        Write(module, 28, 1);
        Write(module, 32, 1);
        Write(module, 36, 1);
        Write(module, 40, 1);
        var modules = new TrRmModuleTableReader(module, 1, names, 0, 1, 1, 0);
        var type = new byte[40];
        Write(type, 0, 1);
        Write(type, 4, 3);
        Write(type, 8, 4);
        Write(type, 32, typeFirst);
        Write(type, 36, typeCount);
        var types = new TrRmTypeDefTableReader(type, 1, names, modules,
            0, 0, 0, declaredCount);
        byte[] spec = [0, 0, 0, 0, 3, 0, 0, 0, 1, 0, 8];
        if (hasOutputParam && !nonBooleanResult)
        {
            spec[^1] = 1;
        }

        var typeSpecs = new TrRmTypeSpecTableReader(spec, 1, 1, 0, 0, 0);
        var signature = new byte[24];
        Write(signature, 0, 1);
        if (hasOutputParam)
        {
            Write(signature, 8, 1);
            Write(signature, 12, 1);
        }

        var signatures = new TrRmMethodSigTableReader(signature, 1, typeSpecs,
            hasOutputParam ? 1 : 0, 0);
        var param = hasOutputParam ? new byte[16] : [];
        if (hasOutputParam)
        {
            Write(param, 0, 1);
            Write(param, 8, 1);
            param[12] = paramMode;
        }

        var parameters = hasOutputParam
            ? new TrRmParamTableReader(param, 1, signatures, typeSpecs) : default;
        var method = new byte[28];
        Write(method, 0, 1);
        Write(method, 4, 4);
        Write(method, 8, 1);
        Write(method, 16, methodFirst);
        Write(method, 20, methodCount);
        var methods = new TrRmMethodDefTableReader(method, 1, names, modules,
            types, signatures, declaredCount, 0);
        _ = new TrRmContractTableReader(section, recordCount,
            types, methods, signatures, parameters, typeSpecs);
    }

    private static byte[] Cleanup()
    {
        var data = new byte[4];
        Write(data, 0, 1);
        return Contract(0x1300_0001, 5, data);
    }

    private static byte[] Effects()
    {
        var data = new byte[4];
        Write(data, 0, 1);
        return Contract(0x1600_0001, 1, data);
    }

    private static byte[] Contract(uint owner, ushort kind, byte[] data)
    {
        var row = new byte[8 + data.Length];
        Write(row, 0, owner);
        BinaryPrimitives.WriteUInt16LittleEndian(row.AsSpan(4), kind);
        data.CopyTo(row.AsSpan(8));
        return row;
    }

    private static byte[] Section(params byte[][] rows)
    {
        var indexBytes = (rows.Length + 1) * sizeof(uint);
        var dataBytes = 0;
        foreach (var row in rows)
        {
            dataBytes += row.Length;
        }

        var section = new byte[indexBytes + dataBytes];
        var offset = 0;
        for (var index = 0; index < rows.Length; index++)
        {
            Write(section, index * sizeof(uint), (uint)offset);
            rows[index].CopyTo(section.AsSpan(indexBytes + offset));
            offset += rows[index].Length;
        }

        Write(section, rows.Length * sizeof(uint), (uint)offset);
        return section;
    }

    private static void Write(byte[] bytes, int offset, uint value)
        => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);
}
