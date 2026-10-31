// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.IO;

namespace Terrum.CodeAnalysis.Modules;

internal static class TrRmConstructedTypeSpecValidator
{
    public static void Validate(TrRmTypeSpecTableReader typeSpecs,
        TrRmTypeDefTableReader typeDefs, TrRmGenericParamTableReader formals,
        TrRmImportTableReader imports, int constantSourceCount,
        TrRmA3LayoutResourceLedger? resourceLedger = null)
    {
        resourceLedger?.ConsumeWork();
        if (constantSourceCount is < 0 or > 0xFF_FFFF)
        {
            throw InvalidTypeSpec();
        }

        var hasForwardReference = false;
        var typeSpecCount = typeSpecs.Count;
        for (var row = 1; row <= typeSpecCount; row++)
        {
            resourceLedger?.ConsumeWork();
            var bytes = typeSpecs.GetRow(row);
            var form = ReadByte(bytes, 0, resourceLedger);
            if (form == 0x03)
            {
                ValidateImportedTypeDef(Read(bytes, 2, resourceLedger),
                    imports);
            }
            else if (form == 0x04)
            {
                var formalRow = checked((int)Read(bytes, 2, resourceLedger));
                if (formals.GetFormalKind(formalRow) != 1)
                {
                    throw InvalidTypeSpec();
                }
            }
            else if (form == 0x05)
            {
                var definition = GetConstructedDefinition(
                    bytes, typeSpecs, typeDefs, formals, imports,
                    resourceLedger);
                resourceLedger?.ConsumeWork(3);
                var argumentCount = (bytes.Length - 6) / sizeof(uint);
                if (argumentCount != definition.FormalCount)
                {
                    throw InvalidTypeSpec();
                }

                for (var ordinal = 0; ordinal < argumentCount; ordinal++)
                {
                    resourceLedger?.ConsumeWork();
                    var formalRow = definition.FormalFirst + ordinal;
                    if (formals.GetOwnerDefId(formalRow) != definition.DefId
                        || formals.GetOrdinal(formalRow) != (uint)ordinal)
                    {
                        throw InvalidTypeSpec();
                    }

                    var argument = Read(bytes,
                        6 + (ordinal * sizeof(uint)), resourceLedger);
                    switch (formals.GetFormalKind(formalRow))
                    {
                        case 1:
                            var typeArgument = typeSpecs.GetTypeSpecRef(argument);
                            hasForwardReference |= typeArgument.IsRow
                                && typeArgument.Row >= row;
                            break;

                        case 2:
                            if (argument is 0 or > 0xFF_FFFF
                                || argument > (uint)constantSourceCount)
                            {
                                throw InvalidTypeSpec();
                            }
                            break;

                        default:
                            throw InvalidTypeSpec();
                    }
                }
            }
        }

        // All other TypeSpec row edges already point backward; only constructed type arguments can point forward.
        if (hasForwardReference)
        {
            ValidateAcyclicReferences(typeSpecs, typeDefs, formals, imports,
                resourceLedger);
        }
    }

    private static void ValidateImportedTypeDef(uint importRow,
        TrRmImportTableReader imports)
    {
        var expected = imports.GetExpectedDefinitionForImportRow(checked((int)importRow));
        if ((expected >> 24) != 0x13)
        {
            throw InvalidTypeSpec();
        }
    }

    private static ConstructedDefinition GetConstructedDefinition(
        ReadOnlySpan<byte> bytes, TrRmTypeSpecTableReader typeSpecs,
        TrRmTypeDefTableReader typeDefs, TrRmGenericParamTableReader formals,
        TrRmImportTableReader imports,
        TrRmA3LayoutResourceLedger? resourceLedger)
    {
        var genericTypeSpec = checked((int)Read(bytes, 2, resourceLedger));
        var genericType = typeSpecs.GetRow(genericTypeSpec);
        var genericForm = ReadByte(genericType, 0, resourceLedger);
        var defId = genericForm switch
        {
            0x02 => 0x1300_0000u | Read(genericType, 2, resourceLedger),
            0x03 => imports.GetExpectedDefinitionForImportRow(
                checked((int)Read(genericType, 2, resourceLedger))),
            _ => throw InvalidTypeSpec(),
        };
        if ((defId >> 24) != 0x13)
        {
            throw InvalidTypeSpec();
        }

        var typeDef = checked((int)(defId & 0xFF_FFFF));
        if (typeDef <= 0 || typeDef > typeDefs.Count)
        {
            throw InvalidTypeSpec();
        }

        var (formalFirst, formalCount) = typeDefs.GetGenericRange(typeDef);
        for (var ordinal = 0; ordinal < formalCount; ordinal++)
        {
            resourceLedger?.ConsumeWork();
            var formalRow = formalFirst + ordinal;
            if (formals.GetOwnerDefId(formalRow) != defId
                || formals.GetOrdinal(formalRow) != (uint)ordinal)
            {
                throw InvalidTypeSpec();
            }
        }

        return new ConstructedDefinition(defId, formalFirst, formalCount);
    }

    private static void ValidateAcyclicReferences(TrRmTypeSpecTableReader typeSpecs,
        TrRmTypeDefTableReader typeDefs, TrRmGenericParamTableReader formals,
        TrRmImportTableReader imports,
        TrRmA3LayoutResourceLedger? resourceLedger)
    {
        var typeSpecCount = typeSpecs.Count;
        resourceLedger?.ConsumeWork(checked((2UL * (ulong)typeSpecCount) + 3UL));
        var indegree = new int[typeSpecCount + 1];
        var ready = new int[typeSpecCount];
        for (var row = 1; row <= typeSpecCount; row++)
        {
            resourceLedger?.ConsumeWork();
            var referenceCount = GetTypeSpecReferenceCount(
                row, typeSpecs, typeDefs, formals, imports, resourceLedger);
            for (var reference = 0; reference < referenceCount; reference++)
            {
                resourceLedger?.ConsumeWork();
                indegree[GetTypeSpecReference(row, reference,
                    typeSpecs, typeDefs, formals, imports, resourceLedger)]++;
            }
        }

        var head = 0;
        var tail = 0;
        for (var row = 1; row <= typeSpecCount; row++)
        {
            resourceLedger?.ConsumeWork();
            if (indegree[row] == 0)
            {
                resourceLedger?.ConsumeWork();
                ready[tail++] = row;
            }
        }

        var processed = 0;
        while (head < tail)
        {
            resourceLedger?.ConsumeWork();
            var row = ready[head++];
            processed++;
            var referenceCount = GetTypeSpecReferenceCount(
                row, typeSpecs, typeDefs, formals, imports, resourceLedger);
            for (var reference = 0; reference < referenceCount; reference++)
            {
                resourceLedger?.ConsumeWork();
                var target = GetTypeSpecReference(row, reference,
                    typeSpecs, typeDefs, formals, imports, resourceLedger);
                resourceLedger?.ConsumeWork();
                if (--indegree[target] == 0)
                {
                    resourceLedger?.ConsumeWork();
                    ready[tail++] = target;
                }
            }
        }

        resourceLedger?.ConsumeWork();
        if (processed != typeSpecCount)
        {
            throw InvalidTypeSpec();
        }
    }

    private static int GetTypeSpecReferenceCount(int row,
        TrRmTypeSpecTableReader typeSpecs, TrRmTypeDefTableReader typeDefs,
        TrRmGenericParamTableReader formals, TrRmImportTableReader imports,
        TrRmA3LayoutResourceLedger? resourceLedger)
    {
        var bytes = typeSpecs.GetRow(row);
        var form = ReadByte(bytes, 0, resourceLedger);
        if (form == 0x05)
        {
            var definition = GetConstructedDefinition(
                bytes, typeSpecs, typeDefs, formals, imports, resourceLedger);
            var count = 1;
            for (var ordinal = 0; ordinal < definition.FormalCount; ordinal++)
            {
                resourceLedger?.ConsumeWork();
                var formalRow = definition.FormalFirst + ordinal;
                if (formals.GetFormalKind(formalRow) == 1
                    && typeSpecs.GetTypeSpecRef(
                        Read(bytes, 6 + (ordinal * sizeof(uint)),
                            resourceLedger)).IsRow)
                {
                    count++;
                }
            }

            return count;
        }

        if (form is (>= 0x06 and <= 0x0B) or 0x0E)
        {
            return typeSpecs.GetTypeSpecRef(
                Read(bytes, 2, resourceLedger)).IsRow ? 1 : 0;
        }

        if (form == 0x0C)
        {
            var allowInitQualifier
                = ReadByte(bytes, 2, resourceLedger) == 0x8A;
            return typeSpecs.GetTypeSpecRef(Read(bytes, 3, resourceLedger),
                allowInitQualifier: allowInitQualifier).IsRow ? 1 : 0;
        }

        if (form == 0x11)
        {
            return typeSpecs.GetTypeSpecRef(
                Read(bytes, 2, resourceLedger)).IsRow ? 1 : 0;
        }

        return 0;
    }

    private static int GetTypeSpecReference(int row, int reference,
        TrRmTypeSpecTableReader typeSpecs, TrRmTypeDefTableReader typeDefs,
        TrRmGenericParamTableReader formals, TrRmImportTableReader imports,
        TrRmA3LayoutResourceLedger? resourceLedger)
    {
        var bytes = typeSpecs.GetRow(row);
        var form = ReadByte(bytes, 0, resourceLedger);
        if (form == 0x05)
        {
            if (reference == 0)
            {
                return checked((int)Read(bytes, 2, resourceLedger));
            }

            var definition = GetConstructedDefinition(
                bytes, typeSpecs, typeDefs, formals, imports, resourceLedger);
            for (var ordinal = 0; ordinal < definition.FormalCount; ordinal++)
            {
                resourceLedger?.ConsumeWork();
                var formalRow = definition.FormalFirst + ordinal;
                if (formals.GetFormalKind(formalRow) != 1)
                {
                    continue;
                }

                var typeArgument = typeSpecs.GetTypeSpecRef(
                    Read(bytes, 6 + (ordinal * sizeof(uint)),
                        resourceLedger));
                if (typeArgument.IsRow && --reference == 0)
                {
                    return typeArgument.Row;
                }
            }

            throw InvalidTypeSpec();
        }

        var offset = form == 0x0C ? 3 : 2;
        var allowInitQualifier = form == 0x0C
            && ReadByte(bytes, 2, resourceLedger) == 0x8A;
        var inner = typeSpecs.GetTypeSpecRef(Read(bytes, offset,
                resourceLedger),
            allowInitQualifier: allowInitQualifier);
        if (reference == 0 && inner.IsRow)
        {
            return inner.Row;
        }

        throw InvalidTypeSpec();
    }

    private static uint Read(ReadOnlySpan<byte> bytes, int offset,
        TrRmA3LayoutResourceLedger? resourceLedger)
    {
        resourceLedger?.ConsumeWork(sizeof(uint) + 1);
        return BinaryPrimitives.ReadUInt32LittleEndian(
            bytes.Slice(offset, sizeof(uint)));
    }

    private static byte ReadByte(ReadOnlySpan<byte> bytes, int offset,
        TrRmA3LayoutResourceLedger? resourceLedger)
    {
        resourceLedger?.ConsumeWork(sizeof(byte) + 1);
        return bytes[offset];
    }

    private static InvalidDataException InvalidTypeSpec()
        => new("Invalid TRRM TypeSpec binding.");

    private readonly record struct ConstructedDefinition(
        uint DefId, int FormalFirst, int FormalCount);
}
