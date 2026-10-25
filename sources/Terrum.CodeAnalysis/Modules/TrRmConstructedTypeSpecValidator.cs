// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.IO;

namespace Terrum.CodeAnalysis.Modules;

internal static class TrRmConstructedTypeSpecValidator
{
    public static void Validate(TrRmTypeSpecTableReader typeSpecs,
        TrRmTypeDefTableReader typeDefs, TrRmGenericParamTableReader formals,
        TrRmImportTableReader imports, int constantSourceCount)
    {
        if (constantSourceCount is < 0 or > 0xFF_FFFF)
        {
            throw InvalidTypeSpec();
        }

        var hasForwardReference = false;
        for (var row = 1; row <= typeSpecs.Count; row++)
        {
            var bytes = typeSpecs.GetRow(row);
            if (bytes[0] == 0x03)
            {
                ValidateImportedTypeDef(Read(bytes, 2), imports);
            }
            else if (bytes[0] == 0x04)
            {
                var formalRow = checked((int)Read(bytes, 2));
                if (formals.GetFormalKind(formalRow) != 1)
                {
                    throw InvalidTypeSpec();
                }
            }
            else if (bytes[0] == 0x05)
            {
                var definition = GetConstructedDefinition(
                    bytes, typeSpecs, typeDefs, formals, imports);
                var argumentCount = (bytes.Length - 6) / sizeof(uint);
                if (argumentCount != definition.FormalCount)
                {
                    throw InvalidTypeSpec();
                }

                for (var ordinal = 0; ordinal < argumentCount; ordinal++)
                {
                    var formalRow = definition.FormalFirst + ordinal;
                    if (formals.GetOwnerDefId(formalRow) != definition.DefId
                        || formals.GetOrdinal(formalRow) != (uint)ordinal)
                    {
                        throw InvalidTypeSpec();
                    }

                    var argument = Read(bytes, 6 + (ordinal * sizeof(uint)));
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
            ValidateAcyclicReferences(typeSpecs, typeDefs, formals, imports);
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
        TrRmImportTableReader imports)
    {
        var genericTypeSpec = checked((int)Read(bytes, 2));
        var genericType = typeSpecs.GetRow(genericTypeSpec);
        var defId = genericType[0] switch
        {
            0x02 => 0x1300_0000u | Read(genericType, 2),
            0x03 => imports.GetExpectedDefinitionForImportRow(
                checked((int)Read(genericType, 2))),
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
        TrRmImportTableReader imports)
    {
        var indegree = new int[typeSpecs.Count + 1];
        for (var row = 1; row <= typeSpecs.Count; row++)
        {
            var referenceCount = GetTypeSpecReferenceCount(
                row, typeSpecs, typeDefs, formals, imports);
            for (var reference = 0; reference < referenceCount; reference++)
            {
                indegree[GetTypeSpecReference(row, reference,
                    typeSpecs, typeDefs, formals, imports)]++;
            }
        }

        var ready = new int[typeSpecs.Count];
        var head = 0;
        var tail = 0;
        for (var row = 1; row <= typeSpecs.Count; row++)
        {
            if (indegree[row] == 0)
            {
                ready[tail++] = row;
            }
        }

        var processed = 0;
        while (head < tail)
        {
            var row = ready[head++];
            processed++;
            var referenceCount = GetTypeSpecReferenceCount(
                row, typeSpecs, typeDefs, formals, imports);
            for (var reference = 0; reference < referenceCount; reference++)
            {
                var target = GetTypeSpecReference(row, reference,
                    typeSpecs, typeDefs, formals, imports);
                if (--indegree[target] == 0)
                {
                    ready[tail++] = target;
                }
            }
        }

        if (processed != typeSpecs.Count)
        {
            throw InvalidTypeSpec();
        }
    }

    private static int GetTypeSpecReferenceCount(int row,
        TrRmTypeSpecTableReader typeSpecs, TrRmTypeDefTableReader typeDefs,
        TrRmGenericParamTableReader formals, TrRmImportTableReader imports)
    {
        var bytes = typeSpecs.GetRow(row);
        if (bytes[0] == 0x05)
        {
            var definition = GetConstructedDefinition(
                bytes, typeSpecs, typeDefs, formals, imports);
            var count = 1;
            for (var ordinal = 0; ordinal < definition.FormalCount; ordinal++)
            {
                var formalRow = definition.FormalFirst + ordinal;
                if (formals.GetFormalKind(formalRow) == 1
                    && typeSpecs.GetTypeSpecRef(
                        Read(bytes, 6 + (ordinal * sizeof(uint)))).IsRow)
                {
                    count++;
                }
            }

            return count;
        }

        if (bytes[0] is (>= 0x06 and <= 0x0B) or 0x0E)
        {
            return typeSpecs.GetTypeSpecRef(Read(bytes, 2)).IsRow ? 1 : 0;
        }

        if (bytes[0] == 0x0C)
        {
            return typeSpecs.GetTypeSpecRef(Read(bytes, 3),
                allowInitQualifier: bytes[2] == 0x8A).IsRow ? 1 : 0;
        }

        return 0;
    }

    private static int GetTypeSpecReference(int row, int reference,
        TrRmTypeSpecTableReader typeSpecs, TrRmTypeDefTableReader typeDefs,
        TrRmGenericParamTableReader formals, TrRmImportTableReader imports)
    {
        var bytes = typeSpecs.GetRow(row);
        if (bytes[0] == 0x05)
        {
            if (reference == 0)
            {
                return checked((int)Read(bytes, 2));
            }

            var definition = GetConstructedDefinition(
                bytes, typeSpecs, typeDefs, formals, imports);
            for (var ordinal = 0; ordinal < definition.FormalCount; ordinal++)
            {
                var formalRow = definition.FormalFirst + ordinal;
                if (formals.GetFormalKind(formalRow) != 1)
                {
                    continue;
                }

                var typeArgument = typeSpecs.GetTypeSpecRef(
                    Read(bytes, 6 + (ordinal * sizeof(uint))));
                if (typeArgument.IsRow && --reference == 0)
                {
                    return typeArgument.Row;
                }
            }

            throw InvalidTypeSpec();
        }

        var offset = bytes[0] == 0x0C ? 3 : 2;
        var inner = typeSpecs.GetTypeSpecRef(Read(bytes, offset),
            allowInitQualifier: bytes[0] == 0x0C && bytes[2] == 0x8A);
        if (reference == 0 && inner.IsRow)
        {
            return inner.Row;
        }

        throw InvalidTypeSpec();
    }

    private static uint Read(ReadOnlySpan<byte> bytes, int offset)
        => BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(offset, sizeof(uint)));

    private static InvalidDataException InvalidTypeSpec()
        => new("Invalid TRRM TypeSpec binding.");

    private readonly record struct ConstructedDefinition(
        uint DefId, int FormalFirst, int FormalCount);
}
