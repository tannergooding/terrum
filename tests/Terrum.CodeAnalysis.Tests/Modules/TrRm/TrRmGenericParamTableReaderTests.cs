// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.IO;
using Terrum.CodeAnalysis.Modules;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests.Modules.TrRm;

public sealed class TrRmGenericParamTableReaderTests
{
    [Test]
    public void TypeAndMethodFormalsHaveDistinctOwnersAndConstraintRanges()
    {
        Assert.That(GetConstraintOwner(Formals(), 1), Is.EqualTo(1));
        Assert.Throws<InvalidDataException>(() => GetConstraintOwner(Formals(), 0));
        Assert.Throws<InvalidDataException>(() => GetConstraintOwner(Formals(), 2));
    }

    [Test]
    public void MultipleFormalsOfOneTypeRequireIncreasingOrdinals()
    {
        var type = Type();
        Write(type, 24, 2);
        var signature = Signature();
        Write(signature, 16, 0);
        Write(signature, 20, 0);
        var formals = Formals();
        Write(formals, 20, 0x1300_0001);
        Write(formals, 24, 1);
        Assert.That(GetConstraintOwner(formals, 1, signature, type), Is.EqualTo(1));

        Write(formals, 24, 0);
        Assert.Throws<InvalidDataException>(() =>
            GetConstraintOwner(formals, 1, signature, type));
    }
    [TestCase(0,(byte)(0))]
    [TestCase(0,(byte)(2))]
    [TestCase(3,(byte)(0))]
    [TestCase(3,(byte)(0x16))]
    [TestCase(4,(byte)(1))]
    [TestCase(8,(byte)(0))]
    [TestCase(12,(byte)(0))]
    [TestCase(12,(byte)(2))]
    [TestCase(16,(byte)(0))]
    [TestCase(20,(byte)(0))]
    [TestCase(20,(byte)(2))]
    [TestCase(23,(byte)(0))]
    [TestCase(24,(byte)(1))]
    [TestCase(28,(byte)(0))]
    public void RejectsBadFormalOwnerOrdinalNameAndConstraintRange(int offset, byte replacement)
    {
        var bytes = Formals();
        bytes[offset] = replacement;
        Assert.Throws<InvalidDataException>(() => GetConstraintOwner(bytes, 1));
    }

    [Test]
    public void RejectsOverlappingOrUnownedFormalAndConstraintRows()
    {
        var expandedTypeRange = Type();
        Write(expandedTypeRange, 24, 2);
        Assert.Throws<InvalidDataException>(() =>
            GetConstraintOwner(Formals(), 1, type: expandedTypeRange));

        var sig = Signature();
        Write(sig, 16, 1);
        Assert.Throws<InvalidDataException>(() => GetConstraintOwner(Formals(), 1, signature: sig));

        var bytes = Formals();
        Write(bytes, 32, 1);
        Write(bytes, 36, 1);
        Assert.Throws<InvalidDataException>(() => GetConstraintOwner(bytes, 1));

        Assert.Throws<InvalidDataException>(() => GetConstraintOwner(Formals()[..^1], 1));
    }

    [Test]
    public void ConstraintRowChecksOwnershipWithoutAssigningKindsOrWitnesses()
    {
        var row = Constraint();
        Write(row, 12, 0x1600_0001);
        row[4] = 0xFF;
        row[5] = 0xFF;
        CreateConstraint(row);
    }
    [TestCase(0,(byte)(0))]
    [TestCase(0,(byte)(2))]
    [TestCase(6,(byte)(1))]
    [TestCase(7,(byte)(1))]
    [TestCase(8,(byte)(0))]
    [TestCase(8,(byte)(2))]
    public void RejectsInvalidConstraintOwnerFlagsAndTarget(int offset, byte replacement)
    {
        var row = Constraint();
        row[offset] = replacement;
        Assert.Throws<InvalidDataException>(() => CreateConstraint(row));
    }

    [Test]
    public void ConstraintSectionMustMatchFormalOwnedRowCount()
    {
        Assert.Throws<InvalidDataException>(() => CreateConstraint(Constraint()[..^1]));
        Assert.Throws<InvalidDataException>(() => CreateConstraint([], recordCount: 0));
    }

    private static void CreateConstraint(byte[] bytes, int recordCount = 1)
    {
        var names = new TrRmStringTableReader(TrRmStringTableWriter.EncodeTextRows(
            [("", 0), ("Module", 0), ("Widget", 0), ("Build", 0), ("T", 0), ("U", 0)]), 6);
        var modules = new TrRmModuleTableReader(Module(), 1, names, 0, 1, 1, 0);
        var types = new TrRmTypeDefTableReader(Type(), 1, names, modules, 0, 2, 0, 0);
        byte[] specRow = [0, 0, 0, 0, 3, 0, 0, 0, 1, 0, 8];
        var typeSpecs = new TrRmTypeSpecTableReader(specRow, 1, 1, 0, 2, 0);
        var signatures = new TrRmMethodSigTableReader(Signature(), 1, typeSpecs, 0, 2);
        var methods = new TrRmMethodDefTableReader(Method(), 1,
            names, modules, types, signatures, 0, 0);
        var formals = new TrRmGenericParamTableReader(Formals(), 2,
            names, types, methods, signatures, 1);
        _ = new TrRmConstraintTableReader(bytes, recordCount, formals, typeSpecs);
    }

    private static int GetConstraintOwner(byte[] formalRows, int row,
        byte[]? signature = null, byte[]? type = null)
    {
        var names = new TrRmStringTableReader(TrRmStringTableWriter.EncodeTextRows(
            [("", 0), ("Module", 0), ("Widget", 0), ("Build", 0), ("T", 0), ("U", 0)]), 6);
        var modules = new TrRmModuleTableReader(Module(), 1, names, 0, 1, 1, 0);
        var types = new TrRmTypeDefTableReader(type ?? Type(), 1, names, modules, 0, 2, 0, 0);
        byte[] specRow = [0, 0, 0, 0, 3, 0, 0, 0, 1, 0, 8];
        var typeSpecs = new TrRmTypeSpecTableReader(specRow, 1, 1, 0, 2, 0);
        var signatures = new TrRmMethodSigTableReader(signature ?? Signature(), 1,
            typeSpecs, 0, 2);
        var methods = new TrRmMethodDefTableReader(Method(), 1,
            names, modules, types, signatures, 0, 0);
        var formals = new TrRmGenericParamTableReader(formalRows, 2,
            names, types, methods, signatures, 1);
        return formals.GetConstraintOwner(row);
    }

    private static byte[] Module()
    {
        var row = new byte[44];
        Write(row, 0, 2);
        Write(row, 4, 1);
        Write(row, 28, 1);
        Write(row, 32, 1);
        Write(row, 36, 1);
        Write(row, 40, 1);
        return row;
    }

    private static byte[] Type()
    {
        var row = new byte[40];
        Write(row, 0, 1);
        Write(row, 4, 3);
        Write(row, 8, 4);
        Write(row, 20, 1);
        Write(row, 24, 1);
        return row;
    }

    private static byte[] Signature()
    {
        var row = new byte[24];
        Write(row, 0, 1);
        Write(row, 16, 2);
        Write(row, 20, 1);
        return row;
    }

    private static byte[] Method()
    {
        var row = new byte[28];
        Write(row, 4, 4);
        Write(row, 8, 1);
        return row;
    }

    private static byte[] Formals()
    {
        var rows = new byte[40];
        Write(rows, 0, 0x1300_0001);
        Write(rows, 8, 5);
        Write(rows, 12, 1);
        Write(rows, 16, 1);
        Write(rows, 20, 0x1600_0001);
        Write(rows, 28, 6);
        return rows;
    }

    private static byte[] Constraint()
    {
        var row = new byte[16];
        Write(row, 0, 1);
        Write(row, 8, 1);
        return row;
    }

    private static void Write(byte[] bytes, int offset, uint value)
        => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);
}
