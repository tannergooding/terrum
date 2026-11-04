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

internal sealed class TrRmZeroCountFieldPlaceTests
{
    private const string SchemaIdentity
        = "trrm/1-draft-local-aggregate-layout-execution-1";
    private const string ModuleIdentity = "l1-zero-count-field-place";

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
        var artifact = EmitArtifactBytes("""
            [Blittable]
            [Layout(LayoutKind.Sequential, Packing: 1)]
            public struct Item {
                public mut uint8 Value;

                internal Item() {
                    Value = uint8(0);
                }
            }

            int32 Main() {
                mut [Item; 2] items = [new Item(), new Item()];
                mut [Item; 0] empty = [];
                items[0].Value = uint8(1);
                return 0;
            }
            """);
        var index = TrRmArtifactIndex.ForLocalAggregateLayoutDraft(artifact);
        var tables
            = TrRmStructuralMetadataValidator
                .ValidateForeignInvocationStorageFixedArrayCopyReturnCoverageRepeatInitializationDraftAndGetExecutionTables(
                    index);
        var mainMethodRow = 0;
        for (var methodRow = 1; methodRow <= tables.Methods.Count;
            methodRow++)
        {
            if (StringComparer.Ordinal.Equals(
                    tables.Methods.GetName(methodRow), "Main"))
            {
                mainMethodRow = methodRow;
                break;
            }
        }

        Assert.That(mainMethodRow, Is.GreaterThan(0));
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
