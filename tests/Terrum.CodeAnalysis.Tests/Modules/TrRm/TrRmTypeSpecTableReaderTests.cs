// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.IO;
using Terrum.CodeAnalysis.Modules;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests.Modules.TrRm;

public sealed class TrRmTypeSpecTableReaderTests
{
    private const TrRmArtifactSchema s_a3Schema
        = TrRmArtifactSchema.ForeignInvocationStorageFixedArrayGenericCopyDraft;

    [Test]
    public void DecodesWorkedInt32RowWithDirectIndex()
    {
        byte[] section = [0, 0, 0, 0, 3, 0, 0, 0, 1, 0, 8];
        var reader = new TrRmTypeSpecTableReader(section, 1, 0, 0, 0, 0);
        Assert.That(reader.GetRow(1).ToArray(), Is.EqualTo(new byte[] { 1, 0, 8 }));
        Assert.Throws<InvalidDataException>(() => ReadRow(section, 0));
        Assert.Throws<InvalidDataException>(() => ReadRow(section, 2));
    }

    [Test]
    public void StandardFunctionPointerUsesConventionAndTypedMethodAnchor()
    {
        var standardRow = TrRmTypeSpecTableWriter.NativeFunctionPointer(3, 2);
        Assert.That(standardRow, Is.EqualTo(new byte[]
            { 0x0D, 0, 3, 2, 0, 0, 0 }));
        var section = TrRmTypeSpecTableWriter.Encode(
            [new ReadOnlyMemory<byte>(standardRow)], 0, 0, 0, 0,
            schema: TrRmArtifactSchema.Standard, methodDefCount: 2);
        var reader = new TrRmTypeSpecTableReader(section, 1, 0, 0, 0,
            nativeFunctionPointerSignatureCount: 0,
            schema: TrRmArtifactSchema.Standard, methodDefCount: 2);

        Assert.That(reader.TryGetNativeFunctionPointerAnchor(1,
            out var convention, out var methodDefRow), Is.True);
        Assert.That(convention, Is.EqualTo(3));
        Assert.That(methodDefRow, Is.EqualTo(2));
        Assert.That(reader.GetCompactValueTypeKey(1),
            Is.EqualTo(TrRmTypeSpecTableReader.NativeFunctionPointerTypeKeyStart));
        Assert.That(reader.TryGetNativeFunctionPointerTypeSpecRow(
            reader.GetCompactValueTypeKey(1), out var typeSpecRow), Is.True);
        Assert.That(typeSpecRow, Is.EqualTo(1));
    }

    [Test]
    public void StandardFunctionPointerAcceptsTerrumConventionTagZero()
    {
        var standardRow = TrRmTypeSpecTableWriter.NativeFunctionPointer(0, 2);
        Assert.That(standardRow, Is.EqualTo(new byte[]
            { 0x0D, 0, 0, 2, 0, 0, 0 }));
        var section = TrRmTypeSpecTableWriter.Encode(
            [new ReadOnlyMemory<byte>(standardRow)], 0, 0, 0, 0,
            schema: TrRmArtifactSchema.Standard, methodDefCount: 2);
        var reader = new TrRmTypeSpecTableReader(section, 1, 0, 0, 0,
            nativeFunctionPointerSignatureCount: 0,
            schema: TrRmArtifactSchema.Standard, methodDefCount: 2);

        Assert.That(reader.TryGetNativeFunctionPointerAnchor(1,
            out var convention, out var methodDefRow), Is.True);
        Assert.That(convention, Is.EqualTo(0));
        Assert.That(methodDefRow, Is.EqualTo(2));
    }

    [Test]
    public void A3FunctionPointerValueSearchChargesItsTraversalAtExactBoundary()
    {
        var section = CreateA3FunctionPointerChain(4);

        TrRmA3ResourceLedgerTestHelper.AssertExactWork(resourceLedger =>
        {
            var typeSpecs = new TrRmTypeSpecTableReader(section, 4,
                0, 0, 0, 0, s_a3Schema, methodDefCount: 1,
                resourceLedger: resourceLedger);
            Assert.That(typeSpecs.ContainsNativeFunctionPointerValue(4,
                default, default, default), Is.True);
        });
    }

    [Test]
    public void A3FunctionPointerValueSearchEnforcesLogicalDepth()
    {
        var maximumDepth = checked((int)TrRmA3LayoutResourceLedger.DepthLimit);
        var atLimitSection = CreateA3FunctionPointerChain(maximumDepth);
        var atLimitLedger = new TrRmA3LayoutResourceLedger();
        var atLimitTypeSpecs = new TrRmTypeSpecTableReader(atLimitSection,
            maximumDepth, 0, 0, 0, 0, s_a3Schema, methodDefCount: 1,
            resourceLedger: atLimitLedger);
        Assert.That(atLimitTypeSpecs.ContainsNativeFunctionPointerValue(
            (uint)maximumDepth, default, default, default), Is.True);

        var overLimitDepth = maximumDepth + 1;
        var overLimitSection = CreateA3FunctionPointerChain(overLimitDepth);
        var overLimitLedger = new TrRmA3LayoutResourceLedger();
        var overLimitTypeSpecs = new TrRmTypeSpecTableReader(overLimitSection,
            overLimitDepth, 0, 0, 0, 0, s_a3Schema, methodDefCount: 1,
            resourceLedger: overLimitLedger);
        TrRmResourceLimitException? exception = null;
        try
        {
            _ = overLimitTypeSpecs.ContainsNativeFunctionPointerValue(
                (uint)overLimitDepth, default, default, default);
        }
        catch (TrRmResourceLimitException caught)
        {
            exception = caught;
        }

        Assert.That(exception, Is.Not.Null);
        Assert.That(exception!.Dimension, Is.EqualTo("depth"));
        Assert.That(exception.Required,
            Is.EqualTo(new System.Numerics.BigInteger(
                TrRmA3LayoutResourceLedger.DepthLimit + 1)));
    }

    [Test]
    public void A3TypeSpecIdentityAndByteOperationsChargeAtExactBoundary()
    {
        var rows = new ReadOnlyMemory<byte>[]
        {
            TrRmTypeSpecTableWriter.NativeFunctionPointer(0, 1),
            new byte[] { 0x06, 0, 1, 0, 0, 0 },
            new byte[] { 0x06, 0, 2, 0, 0, 0 },
            new byte[] { 0x01, 0, 0x8C, 8 },
        };
        var section = TrRmTypeSpecTableWriter.Encode(rows, 0, 0, 0, 0,
            schema: s_a3Schema, methodDefCount: 1);

        TrRmA3ResourceLedgerTestHelper.AssertExactWork(resourceLedger =>
        {
            var typeSpecs = new TrRmTypeSpecTableReader(section, rows.Length,
                0, 0, 0, 0, s_a3Schema, methodDefCount: 1,
                resourceLedger: resourceLedger);
            Assert.That(typeSpecs.GetTypeSpecBytes(0xF200_0001).Length,
                Is.EqualTo(3));
            Assert.That(typeSpecs.AreEquivalentTypeSpecRefs(2, 2), Is.True);
            _ = typeSpecs.GetTypeSpecStructuralHash(3);
            var pointerKey
                = typeSpecs.GetCompactValueTypeKey(0xF300_8C08);
            Assert.That(pointerKey, Is.Not.Zero);
            Assert.That(typeSpecs.TryGetNativeFunctionPointerTypeSpecRow(
                typeSpecs.GetCompactValueTypeKey(1),
                out var functionPointerRow), Is.True);
            Assert.That(functionPointerRow, Is.EqualTo(1));
        });
    }

    [Test]
    public void A3StructuralHashMatchesEquivalentTypeSpecRows()
    {
        var section = Rows(
            [0x01, 0, 0x08],
            [0x06, 0, 0x08, 0, 0, 0xF2],
            [0x06, 0, 1, 0, 0, 0]);
        var typeSpecs = new TrRmTypeSpecTableReader(section, 3,
            0, 0, 0, 0, s_a3Schema);

        Assert.That(typeSpecs.AreEquivalentTypeSpecRefs(2, 3), Is.True);
        Assert.That(typeSpecs.GetTypeSpecStructuralHash(2),
            Is.EqualTo(typeSpecs.GetTypeSpecStructuralHash(3)));
    }

    [Test]
    public void A3TypeSpecIdentityWalksEnforceLogicalDepth()
    {
        var depth = checked((int)TrRmA3LayoutResourceLedger.DepthLimit + 1);
        var section = CreateA3FunctionPointerChain(depth);
        var typeSpecs = new TrRmTypeSpecTableReader(section, depth,
            0, 0, 0, 0, s_a3Schema, methodDefCount: 1,
            resourceLedger: new TrRmA3LayoutResourceLedger());

        TrRmResourceLimitException? comparisonException = null;
        try
        {
            _ = typeSpecs.AreEquivalentTypeSpecRefs(
                (uint)depth, (uint)depth);
        }
        catch (TrRmResourceLimitException caught)
        {
            comparisonException = caught;
        }

        Assert.That(comparisonException, Is.Not.Null);
        Assert.That(comparisonException!.Dimension, Is.EqualTo("depth"));
        Assert.That(comparisonException.Required,
            Is.EqualTo(new System.Numerics.BigInteger(
                TrRmA3LayoutResourceLedger.DepthLimit + 1)));

        var hashTypeSpecs = new TrRmTypeSpecTableReader(section, depth,
            0, 0, 0, 0, s_a3Schema, methodDefCount: 1,
            resourceLedger: new TrRmA3LayoutResourceLedger());
        TrRmResourceLimitException? hashException = null;
        try
        {
            _ = hashTypeSpecs.GetTypeSpecStructuralHash((uint)depth);
        }
        catch (TrRmResourceLimitException caught)
        {
            hashException = caught;
        }

        Assert.That(hashException, Is.Not.Null);
        Assert.That(hashException!.Dimension, Is.EqualTo("depth"));
        Assert.That(hashException.Required,
            Is.EqualTo(new System.Numerics.BigInteger(
                TrRmA3LayoutResourceLedger.DepthLimit + 1)));
    }

    [Test]
    public void DevelopmentSignatureRowsAndStandardAnchorsRemainDistinct()
    {
        var developmentRow =
            TrRmTypeSpecTableWriter.DevelopmentFunctionPointerSignatureReference(1);
        var developmentSection = TrRmTypeSpecTableWriter.Encode(
            [new ReadOnlyMemory<byte>(developmentRow)], 0, 0, 0, 1,
            TrRmArtifactSchema.Development);
        _ = new TrRmTypeSpecTableReader(developmentSection, 1, 0, 0, 0, 1,
            TrRmArtifactSchema.Development);

        Assert.Throws<InvalidDataException>(() => CreateForProfile(
            Rows(developmentRow), 1, 0, TrRmArtifactSchema.Standard, 1));

        var standardRow = TrRmTypeSpecTableWriter.NativeFunctionPointer(1, 1);
        Assert.Throws<InvalidDataException>(() => CreateForProfile(
            Rows(standardRow), 1, 1, TrRmArtifactSchema.Development, 1));
    }

    [TestCase(5, 1u)]
    [TestCase(1, 0u)]
    [TestCase(1, 2u)]
    public void StandardFunctionPointerRejectsUnknownConventionAndAnchor(
        byte convention, uint anchor)
    {
        var bytes = new byte[7];
        bytes[0] = 0x0D;
        bytes[2] = convention;
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(3), anchor);
        Assert.Throws<InvalidDataException>(() => CreateForProfile(
            Rows(bytes), 1, 0, TrRmArtifactSchema.Standard, 1));
    }

    [Test]
    public void ValidatesInlineAndIndexedFamiliesWithoutInventingExecutableSupport()
    {
        var section = Rows(
            [0x01, 0, 0x08],
            [0x01, 0, 0x81, 0x08],
            [0x01, 0, 0x8F, 0x08],
            [0x02, 0, 1, 0, 0, 0],
            [0x07, 0, 4, 0, 0, 0],
            [0x0C, 0, 0x8A, 4, 0, 0, 0],
            [0x06, 0, 1, 0, 0, 0],
            [0x05, 0, 4, 0, 0, 0, 8, 0, 0, 0xF2]);
        var reader = new TrRmTypeSpecTableReader(section, 8, 1, 0, 0);
        Assert.That(reader.GetRow(6)[0], Is.EqualTo((byte)0x0C));
        Assert.That(reader.GetRow(8)[0], Is.EqualTo((byte)0x05));
    }

    [Test]
    public void AllowsInitQualifierOnAtomicTypeOnlyAsSpanElement()
    {
        var section = Rows(
            [0x01, 0x04, 0x08],
            [0x0C, 0, 0x8A, 1, 0, 0, 0]);
        var reader = new TrRmTypeSpecTableReader(section, 2, 0, 0, 0, 0);

        Assert.That(reader.GetTypeSpecRef(1, allowInitQualifier: true)
            .Qualifiers, Is.EqualTo((byte)0x04));
        Assert.That(reader.GetTypeSpecRef(2).Form, Is.EqualTo((byte)0x0C));
        Assert.Throws<InvalidDataException>(() =>
            GetTypeSpecRef(section, 2, 1));
    }

    [Test]
    public void ValidatesExistentialAndNestedTypeSpecRefForms()
    {
        var section = Rows(
            [0x0F, 0],
            [0x06, 0, 0x08, 0, 0, 0xF2],
            [0x0C, 0, 0x8A, 0x08, 0x8B, 0x04, 0xF3],
            [0x0E, 0, 0x08, 0, 0, 0xF2],
            [0x01, 0x04, 0x8B, 0x08],
            [0x0C, 0, 0x8A, 0x05, 0, 0, 0],
            [0x06, 0, 0x08, 0, 0, 0xF2]);
        var reader = new TrRmTypeSpecTableReader(section, 7, 0, 0, 0, 0);
        Assert.That(reader.GetRow(1).ToArray(), Is.EqualTo(new byte[] { 0x0F, 0 }));
        Assert.That(reader.GetRow(2)[0], Is.EqualTo((byte)0x06));
        Assert.That(reader.GetRow(3)[0], Is.EqualTo((byte)0x0C));
        Assert.That(reader.GetRow(4)[0], Is.EqualTo((byte)0x0E));
        Assert.That(reader.AreEquivalentTypeSpecRefs(2, 7), Is.True);
        Assert.That(reader.AreEquivalentTypeSpecRefs(3, 6), Is.True);
        Assert.Throws<InvalidDataException>(() => GetTypeSpecRef(section, 7, 5));
        Assert.Throws<InvalidDataException>(() =>
            GetTypeSpecRef(section, 7, 0xF3048B08));
    }

    [Test]
    public void ValidatesTypeSpecRefNamespacesAndEquivalentInlineTypes()
    {
        var section = Rows([0x01, 0, 0x08], [0x01, 0, 0x8B, 0x08]);
        var reader = new TrRmTypeSpecTableReader(section, 2, 0, 0, 0, 0);

        Assert.That(reader.AreEquivalentTypeSpecRefs(1, 0xF2000008), Is.True);
        Assert.That(reader.AreEquivalentTypeSpecRefs(2, 0xF3008B08), Is.True);
        Assert.That(reader.GetTypeSpecRef(0xF200001A).Form, Is.EqualTo((byte)0x0F));
        Assert.That(reader.GetTypeSpecRef(0xF3008B08).TypeCode, Is.EqualTo((byte)0x8B));
        Assert.Throws<InvalidDataException>(() => GetTypeSpecRef(section, 2, 3));
        Assert.Throws<InvalidDataException>(() => GetTypeSpecRef(section, 2, 0xF2010008));
        Assert.Throws<InvalidDataException>(() => GetTypeSpecRef(section, 2, 0xF200001B));
        Assert.Throws<InvalidDataException>(() => GetTypeSpecRef(section, 2, 0xF3008508));
        Assert.Throws<InvalidDataException>(() => GetTypeSpecRef(section, 2, 0xF3008B1A));
        Assert.Throws<InvalidDataException>(() => GetTypeSpecRef(section, 2, 0xF3028C08));
    }

    [Test]
    public void ReusesCompactKeysForEquivalentNativePointerTypes()
    {
        var section = Rows(
            [0x01, 0, 0x0D],
            [0x01, 0, 0x8C, 0x0D],
            [0x01, 0, 0x8C, 0x0D],
            [0x01, 1, 0x8C, 0x0D],
            [0x01, 0, 0x8C, 0]);
        var reader = new TrRmTypeSpecTableReader(section, 5, 0, 0, 0, 0);

        Assert.That(reader.GetCompactValueTypeKey(2),
            Is.EqualTo(reader.GetCompactValueTypeKey(3)));
        Assert.That(reader.GetCompactValueTypeKey(2),
            Is.Not.EqualTo(reader.GetCompactValueTypeKey(4)));
        Assert.That(reader.GetCompactValueTypeKey(5), Is.EqualTo(0x8C));
    }

    [Test]
    public void RejectsNativePointerTypeKeyExhaustionAfterEquivalentPointers()
    {
        var signatureCount = ushort.MaxValue - TrRmTypeSpecTableReader.NativeFunctionPointerTypeKeyStart;
        var rows = new ReadOnlyMemory<byte>[signatureCount + 5];
        rows[0] = TrRmTypeSpecTableWriter.Builtin(8);
        rows[1] = TrRmTypeSpecTableWriter.Builtin(0x0D);
        for (var index = 0; index < signatureCount; index++)
        {
            rows[index + 2] = TrRmTypeSpecTableWriter
                .DevelopmentFunctionPointerSignatureReference((uint)(index + 1));
        }
        var firstPointerRow = signatureCount + 3;
        rows[firstPointerRow - 1] = TrRmTypeSpecTableWriter.InlineFamily(0x8C, 0x0D);
        rows[firstPointerRow] = TrRmTypeSpecTableWriter.InlineFamily(0x8C, 0x0D);
        rows[firstPointerRow + 1] = TrRmTypeSpecTableWriter.InlineFamily(0x8C, 8);

        var section = TrRmTypeSpecTableWriter.Encode(rows, 0, 0, 0,
            signatureCount, TrRmArtifactSchema.Development);
        var reader = new TrRmTypeSpecTableReader(section, rows.Length, 0, 0, 0,
            signatureCount, TrRmArtifactSchema.Development);

        Assert.That(reader.GetCompactValueTypeKey(firstPointerRow), Is.EqualTo(ushort.MaxValue));
        Assert.That(reader.GetCompactValueTypeKey(firstPointerRow + 1), Is.EqualTo(ushort.MaxValue));
        Assert.Throws<InvalidDataException>(() => GetCompactValueTypeKey(section,
            rows.Length, signatureCount, firstPointerRow + 2));
    }

    [TestCase(new byte[] { 0x00, 0, 8 })]
    [TestCase(new byte[] { 0x01, 2, 8 })]
    [TestCase(new byte[] { 0x01, 0, 0x1A })]
    [TestCase(new byte[] { 0x01, 0, 0xFF })]
    [TestCase(new byte[] { 0x01, 0, 0x80 })]
    [TestCase(new byte[] { 0x01, 0, 0x80, 0x80 })]
    [TestCase(new byte[] { 0x01, 0, 0x89, 0x08 })]
    [TestCase(new byte[] { 0x01, 0, 8, 0 })]
    [TestCase(new byte[] { 0x02, 0, 0, 0, 0, 0 })]
    [TestCase(new byte[] { 0x02, 0, 2, 0, 0, 0 })]
    [TestCase(new byte[] { 0x03, 0, 1, 0, 0, 0 })]
    [TestCase(new byte[] { 0x04, 0, 1, 0, 0, 0 })]
    [TestCase(new byte[] { 0x06, 0, 1, 0, 0, 0 })]
    [TestCase(new byte[] { 0x0C, 0, 0x8B, 1, 0, 0, 0 })]
    public void RejectsInvalidRowShapesAndReferences(byte[] row)
    {
        Assert.Throws<InvalidDataException>(() => Create(Rows(row)));
    }

    [Test]
    public void RejectsNoncanonicalIndexedAtomicArguments()
    {
        Assert.Throws<InvalidDataException>(() =>
            Create(Rows([1, 0, 8], [7, 0, 1, 0, 0, 0]), count: 2));
        Assert.Throws<InvalidDataException>(() =>
            Create(Rows([1, 0, 8], [0x0C, 0, 0x8A, 1, 0, 0, 0]), count: 2));
    }
    [TestCase(new byte[] { 5, 0, 1, 0, 0, 0, 1, 0, 0, 0, 2 })]
    [TestCase(new byte[] { 5, 0, 1, 0, 0, 0 })]
    [TestCase(new byte[] { 5, 0, 1, 0, 0, 0, 0, 0, 0, 0 })]
    public void RejectsMalformedConstructedArgumentPayloads(byte[] constructed)
    {
        Assert.Throws<InvalidDataException>(() =>
            Create(Rows([2, 0, 1, 0, 0, 0], constructed), count: 2));
    }

    [Test]
    public void ConstructedFormRequiresNominalGenericBase()
    {
        Assert.Throws<InvalidDataException>(() =>
            Create(Rows([1, 0, 8], [5, 0, 1, 0, 0, 0, 1, 0, 0, 0]),
                count: 2));
    }

    [Test]
    public void RejectsWrongIndexAndReferencedTableCounts()
    {
        Assert.Throws<InvalidDataException>(() => Create([0, 0, 0, 0, 4, 0, 0, 0, 1, 0, 8]));
        Assert.Throws<InvalidDataException>(() =>
            Create(Rows([2, 0, 1, 0, 0, 0]), typeDefCount: 0));
    }

    private static void Create(byte[] bytes, int count = 1, int typeDefCount = 1)
    {
        _ = new TrRmTypeSpecTableReader(bytes, count, typeDefCount, 0, 0);
    }

    private static void CreateForProfile(byte[] bytes, int count,
        int nativeFunctionPointerSignatureCount, TrRmArtifactSchema schema,
        int methodDefCount)
    {
        _ = new TrRmTypeSpecTableReader(bytes, count, 0, 0, 0,
            nativeFunctionPointerSignatureCount, schema, methodDefCount);
    }

    private static byte[] ReadRow(byte[] bytes, int row)
    {
        var reader = new TrRmTypeSpecTableReader(bytes, 1, 0, 0, 0);
        return reader.GetRow(row).ToArray();
    }

    private static ushort GetCompactValueTypeKey(byte[] bytes, int count,
        int nativeFunctionPointerSignatureCount, int row)
    {
        var reader = new TrRmTypeSpecTableReader(bytes, count, 0, 0, 0,
            nativeFunctionPointerSignatureCount, TrRmArtifactSchema.Development);
        return reader.GetCompactValueTypeKey(row);
    }

    private static byte[] CreateA3FunctionPointerChain(int depth)
    {
        var rows = new ReadOnlyMemory<byte>[depth];
        rows[0] = TrRmTypeSpecTableWriter.NativeFunctionPointer(0, 1);
        for (var index = 1; index < depth; index++)
        {
            var row = new byte[6];
            row[0] = 0x06;
            BinaryPrimitives.WriteUInt32LittleEndian(
                row.AsSpan(2), (uint)index);
            rows[index] = row;
        }

        return TrRmTypeSpecTableWriter.Encode(rows, 0, 0, 0, 0,
            schema: s_a3Schema, methodDefCount: 1);
    }

    private static TrRmTypeSpecRefInfo GetTypeSpecRef(byte[] bytes, int count, uint value)
    {
        var reader = new TrRmTypeSpecTableReader(bytes, count, 0, 0, 0);
        return reader.GetTypeSpecRef(value);
    }

    private static byte[] Rows(params byte[][] rows)
    {
        var dataLength = 0;
        foreach (var row in rows)
        {
            dataLength += row.Length;
        }

        var indexLength = (rows.Length + 1) * sizeof(uint);
        var bytes = new byte[indexLength + dataLength];
        var offset = 0;
        for (var index = 0; index < rows.Length; index++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(index * sizeof(uint)), (uint)offset);
            rows[index].CopyTo(bytes.AsSpan(indexLength + offset));
            offset += rows[index].Length;
        }

        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(rows.Length * sizeof(uint)), (uint)offset);
        return bytes;
    }
}
