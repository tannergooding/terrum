// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Terrum.CodeAnalysis;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Terrum.CodeAnalysis.Tests.Semantics;

namespace Terrum.CodeAnalysis.Tests.Modules.TrRm;

internal sealed class TrRmFieldPlaceNegativeTests
{
    private const string SchemaIdentity
        = "trrm/1-draft-local-aggregate-layout-execution-1";
    private const string ModuleIdentity = "l1-zero-count-field-place";
    private const string FieldPathSource = """
        [Blittable]
        [Layout(LayoutKind.Sequential, Packing: 1)]
        public struct Item {
            public mut uint8 Value;
            public mut uint32 Other;

            internal Item() {
                Value = uint8(0);
                Other = uint32(0);
            }
        }

        int32 Main() {
            mut [Item; 2] items = [new Item(), new Item()];
            mut [Item; 0] empty = [];
            items[0].Value = uint8(1);
            return 0;
        }
        """;
    private const string NestedFieldPathSource = """
        [Blittable]
        [Layout(LayoutKind.Sequential, Packing: 1)]
        public struct Leaf {
            public mut uint8 Value;

            internal Leaf() {
                Value = uint8(0);
            }
        }

        [Blittable]
        [Layout(LayoutKind.Sequential, Packing: 1)]
        public struct Item {
            public mut Leaf Nested;

            internal Item() {
                Nested = new Leaf();
            }
        }

        int32 Main() {
            mut Item seed = new Item();
            mut [Item; 2] items = [seed; 2];
            mut uint8 rhs = uint8(1);
            items[0].Nested.Value = rhs;
            return 0;
        }
        """;
    private const string MultiDimensionalFieldPathSource = """
        [Blittable]
        [Layout(LayoutKind.Sequential, Packing: 1, Alignment: 32)]
        public struct Leaf {
            public mut uint8 Value;
            public mut uint32 Other;

            internal Leaf() {
                Value = uint8(0);
                Other = uint32(0);
            }
        }

        [Blittable]
        [Layout(LayoutKind.Sequential, Packing: 1, Alignment: 32)]
        public struct Item {
            public mut Leaf Nested;

            internal Item() {
                Nested = new Leaf();
            }
        }

        int32 Main() {
            mut Item seed = new Item();
            mut [[Item; 2]; 2] rows = [[seed; 2]; 2];
            mut uint8 rhs = uint8(42);
            mut bool nonIndex = true;
            rows[0][1].Nested.Value = rhs;
            return int32(rows[0][1].Nested.Value);
        }
        """;
    [Test]
    public void RejectsSourceFieldPlaceSelectionIntoZeroCountArray()
    {
        const string source = """
            [Blittable]
            [Layout(LayoutKind.Sequential, Packing: 1)]
            public struct Item {
                public mut uint8 Value;

                internal Item() {
                    Value = uint8(0);
                }
            }

            [Blittable]
            [Layout(LayoutKind.Sequential, Packing: 1)]
            public struct Packet {
                public mut [Item; 0] Items;

                internal Packet() {
                    Items = [];
                }
            }

            int32 Main() {
                mut Packet packet = new Packet();
                packet.Items[0].Value = uint8(1);
                return 0;
            }
            """;
        var compilation = Compilation.Create(SyntaxTree.Parse(source),
            new CompilationOptions {
                LayoutCoreRegistration =
                    LayoutCoreRegistrationTestData.Create(),
            });

        Assert.That(compilation.Diagnostics.Select(static diagnostic
                => diagnostic.Code),
            Is.EqualTo(new[] { "TRM3030" }));
        Assert.That(compilation.Diagnostics.Single().Message,
            Does.Contain("Fixed-array element index must be in [0, 0)."));
    }

    [Test]
    public void RejectsSourceFreeFieldPlaceSelectionIntoZeroCountArray()
    {
        var artifact = EmitArtifactBytes(FieldPathSource);
        var index = TrRmArtifactIndex.ForLocalAggregateLayoutDraft(artifact);
        var tables
            = TrRmStructuralMetadataValidator
                .ValidateForeignInvocationStorageFixedArrayCopyReturnCoverageRepeatInitializationDraftAndGetExecutionTables(
                index);
        var mainMethodRow = FindMainMethodRow(tables);
        var bodyRow = tables.Methods.GetBodyRow(mainMethodRow);
        var body = tables.Bodies.GetRegisterBody(bodyRow,
            tables.Contracts.Count);
        var zeroArrayTypeSpecRow = 0;
        for (var layoutRow = 1;
            layoutRow <= tables.FixedArrayLayouts.Count; layoutRow++)
        {
            var candidate = tables.FixedArrayLayouts.GetRow(layoutRow);
            if (candidate.OwnerModuleRow == 1
                && tables.TypeSpecs.TryGetFixedInlineArray(
                    checked((uint)candidate.ArrayTypeSpecRow), out _,
                    out var elementCount)
                && elementCount == 0)
            {
                zeroArrayTypeSpecRow = candidate.ArrayTypeSpecRow;
                break;
            }
        }

        Assert.That(zeroArrayTypeSpecRow, Is.GreaterThan(0));
        var zeroArrayTypeMapIndex = FindBodyTypeMapIndex(body,
            checked((uint)zeroArrayTypeSpecRow));
        var zeroArrayHomeIndex = -1;
        for (var homeIndex = 0; homeIndex < body.HomeCount; homeIndex++)
        {
            var homeOffset = homeIndex * 8;
            if (body.HomeMap[homeOffset] == 3
                && BinaryPrimitives.ReadUInt16LittleEndian(
                    body.HomeMap[(homeOffset + 2)..])
                    == zeroArrayTypeMapIndex)
            {
                zeroArrayHomeIndex = homeIndex;
                break;
            }
        }

        Assert.That(zeroArrayHomeIndex, Is.GreaterThanOrEqualTo(0));
        var changedSelections = 0;
        var selectedRecordIndex = -1;
        for (var recordIndex = 0; recordIndex < body.RecordCount;
            recordIndex++)
        {
            var record = body.GetRecord(recordIndex).ToArray();
            if (changedSelections == 0 && record.Length == 24
                && record[0] == 0x04
                && record[4] == 3 && record[7] == 2)
            {
                var baseHomeIndex = BinaryPrimitives
                    .ReadUInt16LittleEndian(record.AsSpan(10));
                if (baseHomeIndex < body.HomeCount
                    && body.HomeMap[baseHomeIndex * 8] == 3)
                {
                    var baseTypeMapIndex = BinaryPrimitives
                        .ReadUInt16LittleEndian(
                            body.HomeMap[(baseHomeIndex * 8 + 2)..]);
                    var baseTypeSpecRef = BinaryPrimitives
                        .ReadUInt32LittleEndian(body.TypeMap[
                            (baseTypeMapIndex * sizeof(uint))..]);
                    if ((baseTypeSpecRef >> 24) == 0x11
                        && tables.TypeSpecs.TryGetFixedInlineArray(
                            baseTypeSpecRef & 0x00FF_FFFF, out _,
                            out var baseElementCount)
                        && baseElementCount > 0)
                    {
                        selectedRecordIndex = recordIndex;
                        changedSelections++;
                    }
                }
            }
        }

        Assert.That(changedSelections, Is.EqualTo(1));
        var bodyBytes = GetBodyRowBytes(artifact, bodyRow);
        var selectionOffset = GetRecordOffset(bodyBytes,
            selectedRecordIndex);
        Assert.That(bodyBytes.AsSpan(selectionOffset, 24).SequenceEqual(
            body.GetRecord(selectedRecordIndex)), Is.True);
        BinaryPrimitives.WriteUInt16LittleEndian(
            bodyBytes.AsSpan(selectionOffset + 10),
            checked((ushort)zeroArrayHomeIndex));
        var malformedArtifact = ReplaceBodyRow(artifact, bodyRow,
            bodyBytes);

        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.LoadStandard(
                new TrRmStandardArtifactInput(SchemaIdentity,
                    malformedArtifact), ModuleIdentity, []));
    }

    [Test]
    public void RejectsSourceFreeFieldPlaceWithMismatchedFieldType()
    {
        var artifact = EmitArtifactBytes(FieldPathSource);
        var index = TrRmArtifactIndex.ForLocalAggregateLayoutDraft(artifact);
        var tables
            = TrRmStructuralMetadataValidator
                .ValidateForeignInvocationStorageFixedArrayCopyReturnCoverageRepeatInitializationDraftAndGetExecutionTables(
                    index);
        var valueFieldRow = 0;
        var otherFieldRow = 0;
        for (var fieldRow = 1; fieldRow <= tables.Fields.Count;
            fieldRow++)
        {
            switch (tables.Fields.GetName(fieldRow))
            {
                case "Value":
                    valueFieldRow = fieldRow;
                    break;
                case "Other":
                    otherFieldRow = fieldRow;
                    break;
            }
        }

        Assert.That(valueFieldRow, Is.GreaterThan(0));
        Assert.That(otherFieldRow, Is.GreaterThan(0));
        Assert.That(tables.Fields.GetOwnerTypeDef(valueFieldRow),
            Is.EqualTo(tables.Fields.GetOwnerTypeDef(otherFieldRow)));
        var mainMethodRow = FindMainMethodRow(tables);
        var bodyRow = tables.Methods.GetBodyRow(mainMethodRow);
        var body = tables.Bodies.GetRegisterBody(bodyRow,
            tables.Contracts.Count);
        var selectedRecordIndex = -1;
        for (var recordIndex = 0; recordIndex < body.RecordCount;
            recordIndex++)
        {
            var record = body.GetRecord(recordIndex);
            if (record.Length == 24 && record[0] == 0x04
                && record[4] == 2 && record[7] == 1
                && record[13] == 1
                && BinaryPrimitives.ReadUInt32LittleEndian(
                    record[16..]) == (uint)valueFieldRow)
            {
                selectedRecordIndex = recordIndex;
                break;
            }
        }

        Assert.That(selectedRecordIndex, Is.GreaterThanOrEqualTo(0));
        var bodyBytes = GetBodyRowBytes(artifact, bodyRow);
        var selectionOffset = GetRecordOffset(bodyBytes,
            selectedRecordIndex);
        Assert.That(bodyBytes.AsSpan(selectionOffset, 24)
            .SequenceEqual(body.GetRecord(selectedRecordIndex)), Is.True);
        BinaryPrimitives.WriteUInt32LittleEndian(
            bodyBytes.AsSpan(selectionOffset + 16),
            checked((uint)otherFieldRow));
        var malformedArtifact = ReplaceBodyRow(artifact, bodyRow,
            bodyBytes);

        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.LoadStandard(
                new TrRmStandardArtifactInput(SchemaIdentity,
                    malformedArtifact), ModuleIdentity, []));
    }

    [Test]
    public void RejectsSourceFreeFieldPlaceWithOutOfRangeFieldDef()
    {
        var artifact = EmitArtifactBytes(FieldPathSource);
        var index = TrRmArtifactIndex.ForLocalAggregateLayoutDraft(artifact);
        var tables
            = TrRmStructuralMetadataValidator
                .ValidateForeignInvocationStorageFixedArrayCopyReturnCoverageRepeatInitializationDraftAndGetExecutionTables(
                    index);
        var valueFieldRow = 0;
        for (var fieldRow = 1; fieldRow <= tables.Fields.Count;
            fieldRow++)
        {
            if (StringComparer.Ordinal.Equals(
                    tables.Fields.GetName(fieldRow), "Value"))
            {
                valueFieldRow = fieldRow;
                break;
            }
        }

        Assert.That(valueFieldRow, Is.GreaterThan(0));
        var mainMethodRow = FindMainMethodRow(tables);
        var bodyRow = tables.Methods.GetBodyRow(mainMethodRow);
        var body = tables.Bodies.GetRegisterBody(bodyRow,
            tables.Contracts.Count);
        var selectedRecordIndex = -1;
        for (var recordIndex = 0; recordIndex < body.RecordCount;
            recordIndex++)
        {
            var record = body.GetRecord(recordIndex);
            if (record.Length == 24 && record[0] == 0x04
                && record[4] == 2 && record[7] == 1
                && record[13] == 1
                && BinaryPrimitives.ReadUInt32LittleEndian(
                    record[16..]) == (uint)valueFieldRow)
            {
                selectedRecordIndex = recordIndex;
                break;
            }
        }

        Assert.That(selectedRecordIndex, Is.GreaterThanOrEqualTo(0));
        var bodyBytes = GetBodyRowBytes(artifact, bodyRow);
        var selectionOffset = GetRecordOffset(bodyBytes,
            selectedRecordIndex);
        Assert.That(bodyBytes.AsSpan(selectionOffset, 24)
            .SequenceEqual(body.GetRecord(selectedRecordIndex)), Is.True);
        BinaryPrimitives.WriteUInt32LittleEndian(
            bodyBytes.AsSpan(selectionOffset + 16),
            checked((uint)(tables.Fields.Count + 1)));
        var malformedArtifact = ReplaceBodyRow(artifact, bodyRow,
            bodyBytes);

        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.LoadStandard(
                new TrRmStandardArtifactInput(SchemaIdentity,
                    malformedArtifact), ModuleIdentity, []));
    }

    [Test]
    public void RejectsSourceFreeFieldPlaceWithOutOfRangeElementIndex()
    {
        var artifact = EmitArtifactBytes(FieldPathSource);
        var index = TrRmArtifactIndex.ForLocalAggregateLayoutDraft(artifact);
        var tables
            = TrRmStructuralMetadataValidator
                .ValidateForeignInvocationStorageFixedArrayCopyReturnCoverageRepeatInitializationDraftAndGetExecutionTables(
                    index);
        var mainMethodRow = FindMainMethodRow(tables);
        var bodyRow = tables.Methods.GetBodyRow(mainMethodRow);
        var body = tables.Bodies.GetRegisterBody(bodyRow,
            tables.Contracts.Count);
        var selectedRecordIndex = -1;
        for (var recordIndex = 0; recordIndex < body.RecordCount;
            recordIndex++)
        {
            var record = body.GetRecord(recordIndex);
            if (record.Length != 24 || record[0] != 0x04
                || record[4] != 3 || record[7] != 2)
            {
                continue;
            }

            var baseHomeIndex = BinaryPrimitives
                .ReadUInt16LittleEndian(record[10..]);
            if (baseHomeIndex >= body.HomeCount
                || body.HomeMap[baseHomeIndex * 8] != 3)
            {
                continue;
            }
            var baseTypeMapIndex = BinaryPrimitives
                .ReadUInt16LittleEndian(
                    body.HomeMap[(baseHomeIndex * 8 + 2)..]);
            var baseTypeSpecRef = BinaryPrimitives.ReadUInt32LittleEndian(
                body.TypeMap[(baseTypeMapIndex * sizeof(uint))..]);
            if ((baseTypeSpecRef >> 24) == 0x11
                && tables.TypeSpecs.TryGetFixedInlineArray(
                    baseTypeSpecRef & 0x00FF_FFFF, out _,
                    out var elementCount)
                && elementCount > 0)
            {
                Assert.That(record[12], Is.AnyOf((byte)1, (byte)2));
                selectedRecordIndex = recordIndex;
                break;
            }
        }

        Assert.That(selectedRecordIndex, Is.GreaterThanOrEqualTo(0));
        var bodyBytes = GetBodyRowBytes(artifact, bodyRow);
        var selectionOffset = GetRecordOffset(bodyBytes,
            selectedRecordIndex);
        Assert.That(bodyBytes.AsSpan(selectionOffset, 24)
            .SequenceEqual(body.GetRecord(selectedRecordIndex)), Is.True);
        BinaryPrimitives.WriteUInt16LittleEndian(
            bodyBytes.AsSpan(selectionOffset + 14), ushort.MaxValue);
        var malformedArtifact = ReplaceBodyRow(artifact, bodyRow,
            bodyBytes);

        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.LoadStandard(
                new TrRmStandardArtifactInput(SchemaIdentity,
                    malformedArtifact), ModuleIdentity, []));
    }

    [Test]
    public void RejectsSourceFreeNestedArrayPlaceWithOutOfRangeIndexHome()
    {
        var artifact = EmitArtifactBytes(MultiDimensionalFieldPathSource);
        var index = TrRmArtifactIndex.ForLocalAggregateLayoutDraft(artifact);
        var tables
            = TrRmStructuralMetadataValidator
                .ValidateForeignInvocationStorageFixedArrayCopyReturnCoverageRepeatInitializationDraftAndGetExecutionTables(
                    index);
        var mainMethodRow = FindMainMethodRow(tables);
        var bodyRow = tables.Methods.GetBodyRow(mainMethodRow);
        var body = tables.Bodies.GetRegisterBody(bodyRow,
            tables.Contracts.Count);
        var nestedSelectionIndex = -1;
        var nestedSelectionCount = 0;
        for (var recordIndex = 0; recordIndex < body.RecordCount;
            recordIndex++)
        {
            var record = body.GetRecord(recordIndex);
            if (record.Length == 24 && record[0] == 0x04
                && record[4] == 3 && record[5] == 2 && record[7] == 1
                && record[12] == 2 && record[13] == 3)
            {
                nestedSelectionIndex = recordIndex;
                nestedSelectionCount++;
            }
        }

        Assert.That(nestedSelectionCount, Is.EqualTo(1));
        var bodyBytes = GetBodyRowBytes(artifact, bodyRow);
        var selectionOffset = GetRecordOffset(bodyBytes,
            nestedSelectionIndex);
        Assert.That(bodyBytes.AsSpan(selectionOffset, 24).SequenceEqual(
            body.GetRecord(nestedSelectionIndex)), Is.True);
        BinaryPrimitives.WriteUInt16LittleEndian(
            bodyBytes.AsSpan(selectionOffset + 14), ushort.MaxValue);
        var malformedArtifact = ReplaceBodyRow(artifact, bodyRow,
            bodyBytes);

        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.LoadStandard(
                new TrRmStandardArtifactInput(SchemaIdentity,
                    malformedArtifact), ModuleIdentity, []));
    }

    [Test]
    public void RejectsSourceFreeNestedArrayPlaceWithNonIndexHome()
    {
        var artifact = EmitArtifactBytes(MultiDimensionalFieldPathSource);
        var index = TrRmArtifactIndex.ForLocalAggregateLayoutDraft(artifact);
        var tables
            = TrRmStructuralMetadataValidator
                .ValidateForeignInvocationStorageFixedArrayCopyReturnCoverageRepeatInitializationDraftAndGetExecutionTables(
                    index);
        var mainMethodRow = FindMainMethodRow(tables);
        var bodyRow = tables.Methods.GetBodyRow(mainMethodRow);
        var body = tables.Bodies.GetRegisterBody(bodyRow,
            tables.Contracts.Count);
        var nonIndexHomeIndex = -1;
        for (var homeIndex = 0; homeIndex < body.HomeCount; homeIndex++)
        {
            var homeOffset = homeIndex * 8;
            if (body.HomeMap[homeOffset] != 3)
            {
                continue;
            }

            var typeMapIndex = BinaryPrimitives.ReadUInt16LittleEndian(
                body.HomeMap[(homeOffset + 2)..]);
            var typeMapToken = BinaryPrimitives.ReadUInt32LittleEndian(
                body.TypeMap[(typeMapIndex * sizeof(uint))..]);
            var typeSpecRef = (byte)(typeMapToken >> 24) switch {
                0x11 => typeMapToken & 0x00FF_FFFF,
                0xF2 or 0xF3 => typeMapToken,
                _ => 0u,
            };
            if (typeSpecRef != 0
                && TrRmNativeImportTypeContract.TryGetScalarType(
                    tables.TypeSpecs, typeSpecRef, out var scalarType)
                && !NativeFixedArrayContract.IsIndexType(scalarType))
            {
                nonIndexHomeIndex = homeIndex;
                break;
            }
        }

        Assert.That(nonIndexHomeIndex, Is.GreaterThanOrEqualTo(0));
        var nestedSelectionIndex = -1;
        var nestedSelectionCount = 0;
        for (var recordIndex = 0; recordIndex < body.RecordCount;
            recordIndex++)
        {
            var record = body.GetRecord(recordIndex);
            if (record.Length == 24 && record[0] == 0x04
                && record[4] == 3 && record[5] == 2 && record[7] == 1
                && record[12] == 2 && record[13] == 3)
            {
                nestedSelectionIndex = recordIndex;
                nestedSelectionCount++;
            }
        }

        Assert.That(nestedSelectionCount, Is.EqualTo(1));
        var bodyBytes = GetBodyRowBytes(artifact, bodyRow);
        var selectionOffset = GetRecordOffset(bodyBytes,
            nestedSelectionIndex);
        Assert.That(bodyBytes.AsSpan(selectionOffset, 24).SequenceEqual(
            body.GetRecord(nestedSelectionIndex)), Is.True);
        BinaryPrimitives.WriteUInt16LittleEndian(
            bodyBytes.AsSpan(selectionOffset + 14),
            checked((ushort)nonIndexHomeIndex));
        var malformedArtifact = ReplaceBodyRow(artifact, bodyRow,
            bodyBytes);

        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.LoadStandard(
                new TrRmStandardArtifactInput(SchemaIdentity,
                    malformedArtifact), ModuleIdentity, []));
    }

    [Test]
    public void RejectsSourceFreeNestedArrayPlaceWithWrongProjectionBase()
    {
        var artifact = EmitArtifactBytes(MultiDimensionalFieldPathSource);
        var index = TrRmArtifactIndex.ForLocalAggregateLayoutDraft(artifact);
        var tables
            = TrRmStructuralMetadataValidator
                .ValidateForeignInvocationStorageFixedArrayCopyReturnCoverageRepeatInitializationDraftAndGetExecutionTables(
                    index);
        var mainMethodRow = FindMainMethodRow(tables);
        var bodyRow = tables.Methods.GetBodyRow(mainMethodRow);
        var body = tables.Bodies.GetRegisterBody(bodyRow,
            tables.Contracts.Count);
        var nestedSelectionIndex = -1;
        var nestedSelectionCount = 0;
        for (var recordIndex = 0; recordIndex < body.RecordCount;
            recordIndex++)
        {
            var record = body.GetRecord(recordIndex);
            if (record.Length == 24 && record[0] == 0x04
                && record[4] == 3 && record[5] == 2 && record[7] == 1
                && record[12] == 2 && record[13] == 3)
            {
                nestedSelectionIndex = recordIndex;
                nestedSelectionCount++;
            }
        }

        Assert.That(nestedSelectionCount, Is.EqualTo(1));
        var bodyBytes = GetBodyRowBytes(artifact, bodyRow);
        var selectionOffset = GetRecordOffset(bodyBytes,
            nestedSelectionIndex);
        Assert.That(bodyBytes.AsSpan(selectionOffset, 24).SequenceEqual(
            body.GetRecord(nestedSelectionIndex)), Is.True);
        BinaryPrimitives.WriteUInt16LittleEndian(
            bodyBytes.AsSpan(selectionOffset + 10), 0);
        var malformedArtifact = ReplaceBodyRow(artifact, bodyRow,
            bodyBytes);

        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.LoadStandard(
                new TrRmStandardArtifactInput(SchemaIdentity,
                    malformedArtifact), ModuleIdentity, []));
    }

    [Test]
    public void RejectsSourceFreeNestedArrayPlaceWithMismatchedLeafFieldType()
    {
        var artifact = EmitArtifactBytes(MultiDimensionalFieldPathSource);
        var index = TrRmArtifactIndex.ForLocalAggregateLayoutDraft(artifact);
        var tables
            = TrRmStructuralMetadataValidator
                .ValidateForeignInvocationStorageFixedArrayCopyReturnCoverageRepeatInitializationDraftAndGetExecutionTables(
                    index);
        var valueFieldRow = 0;
        var otherFieldRow = 0;
        for (var fieldRow = 1; fieldRow <= tables.Fields.Count;
            fieldRow++)
        {
            switch (tables.Fields.GetName(fieldRow))
            {
                case "Value":
                    valueFieldRow = fieldRow;
                    break;
                case "Other":
                    otherFieldRow = fieldRow;
                    break;
            }
        }

        Assert.That(valueFieldRow, Is.GreaterThan(0));
        Assert.That(otherFieldRow, Is.GreaterThan(0));
        Assert.That(tables.Fields.GetOwnerTypeDef(valueFieldRow),
            Is.EqualTo(tables.Fields.GetOwnerTypeDef(otherFieldRow)));
        var mainMethodRow = FindMainMethodRow(tables);
        var bodyRow = tables.Methods.GetBodyRow(mainMethodRow);
        var body = tables.Bodies.GetRegisterBody(bodyRow,
            tables.Contracts.Count);
        var leafSelectionIndex = -1;
        var leafSelectionCount = 0;
        for (var recordIndex = 0; recordIndex < body.RecordCount;
            recordIndex++)
        {
            var record = body.GetRecord(recordIndex);
            if (record.Length == 24 && record[0] == 0x04
                && record[4] == 2 && record[5] == 2
                && record[13] == 1
                && BinaryPrimitives.ReadUInt32LittleEndian(
                    record[16..]) == (uint)valueFieldRow)
            {
                leafSelectionIndex = recordIndex;
                leafSelectionCount++;
            }
        }

        Assert.That(leafSelectionCount, Is.EqualTo(1));
        var bodyBytes = GetBodyRowBytes(artifact, bodyRow);
        var selectionOffset = GetRecordOffset(bodyBytes,
            leafSelectionIndex);
        Assert.That(bodyBytes.AsSpan(selectionOffset, 24).SequenceEqual(
            body.GetRecord(leafSelectionIndex)), Is.True);
        BinaryPrimitives.WriteUInt32LittleEndian(
            bodyBytes.AsSpan(selectionOffset + 16),
            checked((uint)otherFieldRow));
        var malformedArtifact = ReplaceBodyRow(artifact, bodyRow,
            bodyBytes);

        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.LoadStandard(
                new TrRmStandardArtifactInput(SchemaIdentity,
                    malformedArtifact), ModuleIdentity, []));
    }

    [Test]
    public void RejectsSourceFreeNestedArrayPlaceWithOutOfRangeLeafFieldDef()
    {
        var artifact = EmitArtifactBytes(MultiDimensionalFieldPathSource);
        var index = TrRmArtifactIndex.ForLocalAggregateLayoutDraft(artifact);
        var tables
            = TrRmStructuralMetadataValidator
                .ValidateForeignInvocationStorageFixedArrayCopyReturnCoverageRepeatInitializationDraftAndGetExecutionTables(
                    index);
        var valueFieldRow = 0;
        for (var fieldRow = 1; fieldRow <= tables.Fields.Count;
            fieldRow++)
        {
            if (StringComparer.Ordinal.Equals(
                    tables.Fields.GetName(fieldRow), "Value"))
            {
                valueFieldRow = fieldRow;
                break;
            }
        }

        Assert.That(valueFieldRow, Is.GreaterThan(0));
        var mainMethodRow = FindMainMethodRow(tables);
        var bodyRow = tables.Methods.GetBodyRow(mainMethodRow);
        var body = tables.Bodies.GetRegisterBody(bodyRow,
            tables.Contracts.Count);
        var leafSelectionIndex = -1;
        var leafSelectionCount = 0;
        for (var recordIndex = 0; recordIndex < body.RecordCount;
            recordIndex++)
        {
            var record = body.GetRecord(recordIndex);
            if (record.Length == 24 && record[0] == 0x04
                && record[4] == 2 && record[5] == 2 && record[7] == 1
                && record[13] == 1
                && BinaryPrimitives.ReadUInt32LittleEndian(
                    record[16..]) == (uint)valueFieldRow)
            {
                leafSelectionIndex = recordIndex;
                leafSelectionCount++;
            }
        }

        Assert.That(leafSelectionCount, Is.EqualTo(1));
        var bodyBytes = GetBodyRowBytes(artifact, bodyRow);
        var selectionOffset = GetRecordOffset(bodyBytes,
            leafSelectionIndex);
        BinaryPrimitives.WriteUInt32LittleEndian(
            bodyBytes.AsSpan(selectionOffset + 16),
            checked((uint)(tables.Fields.Count + 1)));
        var malformedArtifact = ReplaceBodyRow(artifact, bodyRow,
            bodyBytes);

        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.LoadStandard(
                new TrRmStandardArtifactInput(SchemaIdentity,
                    malformedArtifact), ModuleIdentity, []));
    }

    [Test]
    public void RejectsSourceFreeNestedArrayPlaceWithMismatchedFieldOwner()
    {
        var artifact = EmitArtifactBytes(MultiDimensionalFieldPathSource);
        var index = TrRmArtifactIndex.ForLocalAggregateLayoutDraft(artifact);
        var tables
            = TrRmStructuralMetadataValidator
                .ValidateForeignInvocationStorageFixedArrayCopyReturnCoverageRepeatInitializationDraftAndGetExecutionTables(
                    index);
        var valueFieldRow = 0;
        var nestedFieldRow = 0;
        for (var fieldRow = 1; fieldRow <= tables.Fields.Count;
            fieldRow++)
        {
            switch (tables.Fields.GetName(fieldRow))
            {
                case "Value":
                    valueFieldRow = fieldRow;
                    break;
                case "Nested":
                    nestedFieldRow = fieldRow;
                    break;
            }
        }

        Assert.That(valueFieldRow, Is.GreaterThan(0));
        Assert.That(nestedFieldRow, Is.GreaterThan(0));
        Assert.That(tables.Fields.GetOwnerTypeDef(valueFieldRow),
            Is.Not.EqualTo(tables.Fields.GetOwnerTypeDef(nestedFieldRow)));
        var mainMethodRow = FindMainMethodRow(tables);
        var bodyRow = tables.Methods.GetBodyRow(mainMethodRow);
        var body = tables.Bodies.GetRegisterBody(bodyRow,
            tables.Contracts.Count);
        var leafSelectionIndex = -1;
        var leafSelectionCount = 0;
        for (var recordIndex = 0; recordIndex < body.RecordCount;
            recordIndex++)
        {
            var record = body.GetRecord(recordIndex);
            if (record.Length == 24 && record[0] == 0x04
                && record[4] == 2 && record[5] == 2 && record[7] == 1
                && record[13] == 1
                && BinaryPrimitives.ReadUInt32LittleEndian(
                    record[16..]) == (uint)valueFieldRow)
            {
                leafSelectionIndex = recordIndex;
                leafSelectionCount++;
            }
        }

        Assert.That(leafSelectionCount, Is.EqualTo(1));
        var bodyBytes = GetBodyRowBytes(artifact, bodyRow);
        var selectionOffset = GetRecordOffset(bodyBytes,
            leafSelectionIndex);
        BinaryPrimitives.WriteUInt32LittleEndian(
            bodyBytes.AsSpan(selectionOffset + 16),
            checked((uint)nestedFieldRow));
        var malformedArtifact = ReplaceBodyRow(artifact, bodyRow,
            bodyBytes);

        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.LoadStandard(
                new TrRmStandardArtifactInput(SchemaIdentity,
                    malformedArtifact), ModuleIdentity, []));
    }

    [Test]
    public void RejectsSourceFreeNestedArrayPlaceWithMismatchedProjectionLayout()
    {
        var artifact = EmitArtifactBytes(MultiDimensionalFieldPathSource);
        var index = TrRmArtifactIndex.ForLocalAggregateLayoutDraft(artifact);
        var tables
            = TrRmStructuralMetadataValidator
                .ValidateForeignInvocationStorageFixedArrayCopyReturnCoverageRepeatInitializationDraftAndGetExecutionTables(
                    index);
        var mainMethodRow = FindMainMethodRow(tables);
        var bodyRow = tables.Methods.GetBodyRow(mainMethodRow);
        var body = tables.Bodies.GetRegisterBody(bodyRow,
            tables.Contracts.Count);
        var nestedSelectionIndex = -1;
        var nestedSelectionCount = 0;
        var selectedLayoutRow = 0;
        for (var recordIndex = 0; recordIndex < body.RecordCount;
            recordIndex++)
        {
            var record = body.GetRecord(recordIndex);
            if (record.Length == 24 && record[0] == 0x04
                && record[4] == 3 && record[5] == 2 && record[7] == 1
                && record[12] == 2 && record[13] == 3)
            {
                nestedSelectionIndex = recordIndex;
                nestedSelectionCount++;
                selectedLayoutRow = checked((int)
                    BinaryPrimitives.ReadUInt32LittleEndian(record[16..]));
            }
        }

        Assert.That(nestedSelectionCount, Is.EqualTo(1));
        Assert.That(selectedLayoutRow, Is.GreaterThan(0));
        var selectedLayout = tables.FixedArrayLayouts.GetRow(
            selectedLayoutRow);
        var wrongLayoutRow = 0;
        for (var layoutRow = 1;
            layoutRow <= tables.FixedArrayLayouts.Count; layoutRow++)
        {
            var candidate = tables.FixedArrayLayouts.GetRow(layoutRow);
            if (layoutRow != selectedLayoutRow
                && candidate.OwnerModuleRow
                    == selectedLayout.OwnerModuleRow
                && candidate.ArrayTypeSpecRow
                    != selectedLayout.ArrayTypeSpecRow)
            {
                wrongLayoutRow = layoutRow;
                break;
            }
        }

        Assert.That(wrongLayoutRow, Is.GreaterThan(0));
        var bodyBytes = GetBodyRowBytes(artifact, bodyRow);
        var selectionOffset = GetRecordOffset(bodyBytes,
            nestedSelectionIndex);
        Assert.That(bodyBytes.AsSpan(selectionOffset, 24).SequenceEqual(
            body.GetRecord(nestedSelectionIndex)), Is.True);
        BinaryPrimitives.WriteUInt32LittleEndian(
            bodyBytes.AsSpan(selectionOffset + 16),
            checked((uint)wrongLayoutRow));
        var malformedArtifact = ReplaceBodyRow(artifact, bodyRow,
            bodyBytes);

        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.LoadStandard(
                new TrRmStandardArtifactInput(SchemaIdentity,
                    malformedArtifact), ModuleIdentity, []));
    }

    [TestCase((byte)0xC2)]
    [TestCase((byte)0xC3)]
    public void RejectsSourceFreeMalformedComparisonOperations(byte opcode)
    {
        const string source = """
            int32 Main() {
                mut int32 left = int32(1);
                mut int32 right = int32(2);
                if (left < right) {
                    return 1;
                }
                return 0;
            }
            """;
        var artifact = EmitArtifactBytes(source);
        var index = TrRmArtifactIndex.ForLocalAggregateLayoutDraft(artifact);
        var tables
            = TrRmStructuralMetadataValidator
                .ValidateForeignInvocationStorageFixedArrayCopyReturnCoverageRepeatInitializationDraftAndGetExecutionTables(
                    index);
        var mainMethodRow = FindMainMethodRow(tables);
        var bodyRow = tables.Methods.GetBodyRow(mainMethodRow);
        var body = tables.Bodies.GetRegisterBody(bodyRow,
            tables.Contracts.Count);
        var codePosition = -1;
        var position = 0;
        while (position < body.Code.Length)
        {
            var instructionLength
                = TrRmRegisterBodyReader.InstructionLength(
                    body.Code[position], body.Schema);
            if (body.Code[position] == opcode)
            {
                codePosition = position;
                break;
            }
            position += instructionLength;
        }

        Assert.That(codePosition, Is.GreaterThanOrEqualTo(0));
        var bodyBytes = GetBodyRowBytes(artifact, bodyRow);
        var codeOffset = bodyBytes.Length - body.Code.Length;
        Assert.That(bodyBytes.AsSpan(codeOffset).SequenceEqual(body.Code),
            Is.True);
        if (opcode == 0xC2)
        {
            var payloadOffset = checked(codeOffset + codePosition + 2);
            var payload = BinaryPrimitives.ReadUInt16LittleEndian(
                bodyBytes.AsSpan(payloadOffset));
            BinaryPrimitives.WriteUInt16LittleEndian(
                bodyBytes.AsSpan(payloadOffset),
                checked((ushort)((payload & 0x0FFF) | 0xE000)));
        }
        else
        {
            bodyBytes[checked(codeOffset + codePosition + 1)] = 8;
        }

        var malformedArtifact = ReplaceBodyRow(artifact, bodyRow,
            bodyBytes);
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.LoadStandard(
                new TrRmStandardArtifactInput(SchemaIdentity,
                    malformedArtifact), ModuleIdentity, []));
    }

    [Test]
    public void LoadsNestedFieldPlaceSelectionAfterRepeatInitialization()
    {
        var artifact = EmitArtifactBytes(NestedFieldPathSource);
        var loaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(SchemaIdentity, artifact),
            ModuleIdentity, []);
        var entry = loaded.Functions[loaded.EntryPoint];

        Assert.That(entry.Body.Statements.Any(static statement =>
            statement is BoundExpressionStatement {
                Expression: BoundNativeFixedArrayPathWrite {
                    LeafFields.Length: 2,
                },
            }), Is.True);
    }

    [Test]
    public void LoadsMultiDimensionalNestedFieldPlaceSelection()
    {
        var compilation = Compilation.Create(
            SyntaxTree.Parse(MultiDimensionalFieldPathSource),
            new CompilationOptions {
                RuntimePolicy = BoundRuntimePolicy.FailFast,
                LayoutCoreRegistration =
                    LayoutCoreRegistrationTestData.Create(),
            });
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program
            ?? throw new InvalidOperationException(
                "A successful compilation must contain a bound program.");
        var artifact = TrRmExecutableCompiler.EmitStandard(program,
            ModuleIdentity, SchemaIdentity, 1);
        var loaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(SchemaIdentity, artifact),
            ModuleIdentity, []);
        var entry = loaded.Functions[loaded.EntryPoint];

        Assert.That(entry.Body.Statements.Any(static statement =>
            statement is BoundExpressionStatement {
                Expression: BoundNativeFixedArrayPathWrite {
                    Projections.Length: 2,
                    LeafFields.Length: 2,
                },
            }), Is.True);
    }

    private static byte[] EmitArtifactBytes(string source)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(source),
            new CompilationOptions {
                LayoutCoreRegistration =
                    LayoutCoreRegistrationTestData.Create(),
            });
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        return TrRmExecutableCompiler.EmitStandard(
            compilation.Program
                ?? throw new InvalidOperationException(
                    "A successful compilation must contain a bound program."),
            ModuleIdentity, SchemaIdentity, 1);
    }

    private static int FindMainMethodRow(
        TrRmStructuralMetadataValidator.ValidatedExecutionTables tables)
    {
        for (var methodRow = 1; methodRow <= tables.Methods.Count;
            methodRow++)
        {
            if (StringComparer.Ordinal.Equals(
                    tables.Methods.GetName(methodRow), "Main"))
            {
                return methodRow;
            }
        }

        throw new InvalidOperationException(
            "The artifact has no Main Method.");
    }

    private static ushort FindBodyTypeMapIndex(
        TrRmRegisterBodyReader body, uint typeSpecRef)
    {
        for (var typeMapIndex = 0; typeMapIndex < body.TypeCount;
            typeMapIndex++)
        {
            var token = BinaryPrimitives.ReadUInt32LittleEndian(
                body.TypeMap[(typeMapIndex * sizeof(uint))..]);
            if ((token >> 24) == 0x11
                && (token & 0x00FF_FFFF) == typeSpecRef)
            {
                return checked((ushort)typeMapIndex);
            }
        }

        throw new InvalidOperationException(
            "The Body has no TypeMap entry for the zero-count array.");
    }

    private static byte[] GetBodyRowBytes(byte[] artifact, int bodyRow)
    {
        var index = TrRmArtifactIndex.ForLocalAggregateLayoutDraft(artifact);
        if (!index.TryGetSection(0x0020, out var bodySection,
                out var bodyCount)
            || bodyRow <= 0 || bodyRow > bodyCount)
        {
            throw new InvalidOperationException(
                "The artifact has no selected Body row.");
        }

        var offsetBytes = checked((bodyCount + 1) * sizeof(uint));
        var start = checked((int)BinaryPrimitives
            .ReadUInt32LittleEndian(bodySection[((bodyRow - 1)
                * sizeof(uint))..]));
        var end = checked((int)BinaryPrimitives
            .ReadUInt32LittleEndian(bodySection[(bodyRow
                * sizeof(uint))..]));
        return bodySection.Slice(offsetBytes + start, end - start)
            .ToArray();
    }

    private static int GetRecordOffset(byte[] body, int recordIndex)
    {
        var mapCounts = new int[5];
        var position = 5;
        for (var bit = 0; bit < mapCounts.Length; bit++)
        {
            if ((body[2] & (1 << bit)) != 0)
            {
                mapCounts[bit] = checked((int)BinaryPrimitives
                    .ReadUInt32LittleEndian(body.AsSpan(position)));
                position += sizeof(uint);
            }
        }

        var blockCount = checked((int)BinaryPrimitives
            .ReadUInt32LittleEndian(body.AsSpan(position)));
        position += sizeof(uint);
        var recordCount = checked((int)BinaryPrimitives
            .ReadUInt32LittleEndian(body.AsSpan(position)));
        position += sizeof(uint);
        position += sizeof(uint);
        if ((uint)recordIndex >= (uint)recordCount)
        {
            throw new InvalidOperationException(
                "The Body record index is out of range.");
        }

        var mapByteCount = checked((mapCounts[0] * 8)
            + (mapCounts[1] * 4) + (mapCounts[2] * 4)
            + (mapCounts[3] * 4) + (mapCounts[4] * 8));
        var recordOffsetsStart = checked(position + mapByteCount
            + (blockCount * 12));
        var recordDataStart = checked(recordOffsetsStart
            + ((recordCount + 1) * sizeof(uint)));
        var recordDataOffset = checked((int)BinaryPrimitives
            .ReadUInt32LittleEndian(body.AsSpan(recordOffsetsStart
                + (recordIndex * sizeof(uint)))));
        return checked(recordDataStart + recordDataOffset);
    }

    private static byte[] ReplaceBodyRow(byte[] artifact, int bodyRow,
        byte[] replacementBody)
    {
        var index = TrRmArtifactIndex.ForLocalAggregateLayoutDraft(artifact);
        if (!index.TryGetSection(0x0020, out var bodySection,
                out var bodyCount)
            || bodyRow <= 0 || bodyRow > bodyCount)
        {
            throw new InvalidOperationException(
                "The artifact has no selected Body row.");
        }

        var offsetBytes = checked((bodyCount + 1) * sizeof(uint));
        var bodyRows = new byte[bodyCount][];
        var bodyDataLength = 0;
        for (var row = 1; row <= bodyCount; row++)
        {
            var start = checked((int)BinaryPrimitives
                .ReadUInt32LittleEndian(bodySection[((row - 1)
                    * sizeof(uint))..]));
            var end = checked((int)BinaryPrimitives
                .ReadUInt32LittleEndian(bodySection[(row
                    * sizeof(uint))..]));
            var rowBytes = row == bodyRow
                ? (byte[])replacementBody.Clone()
                : bodySection.Slice(offsetBytes + start,
                    end - start).ToArray();
            bodyRows[row - 1] = rowBytes;
            bodyDataLength = checked(bodyDataLength + rowBytes.Length);
        }

        var newBodySection = new byte[checked(offsetBytes
            + bodyDataLength)];
        var nextOffset = 0;
        for (var rowIndex = 0; rowIndex < bodyRows.Length; rowIndex++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(
                newBodySection.AsSpan(rowIndex * sizeof(uint)),
                checked((uint)nextOffset));
            var rowBytes = bodyRows[rowIndex];
            rowBytes.CopyTo(newBodySection, offsetBytes + nextOffset);
            nextOffset = checked(nextOffset + rowBytes.Length);
        }

        BinaryPrimitives.WriteUInt32LittleEndian(
            newBodySection.AsSpan(bodyCount * sizeof(uint)),
            checked((uint)nextOffset));
        var sections = new List<TrRmSection>(index.SectionCount);
        for (var sectionIndex = 0;
            sectionIndex < index.SectionCount; sectionIndex++)
        {
            index.GetSection(sectionIndex, out var kind, out var flags,
                out var section, out var recordCount);
            sections.Add(new TrRmSection(kind, flags,
                checked((uint)recordCount),
                kind == 0x0020 ? newBodySection : section.ToArray()));
        }

        return TrRmArtifactWriter.Encode(sections,
            TrRmArtifactSchema.LocalAggregateLayoutDraft);
    }
}
