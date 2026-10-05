// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.IO;

namespace Terrum.CodeAnalysis.Modules;

/// <summary>Validates formal ownership, ordinals, names and complete Constraint ranges.</summary>
/// <remarks>Structural checks do not establish executable generic constraints. Borrowed sections must remain unchanged.</remarks>
public readonly ref struct TrRmGenericParamTableReader
{
    private const int RowSize = 20;
    private readonly int[] _constraintOwners;

    public int Count { get; }
    public int ConstraintCount => _constraintOwners.Length - 1;

    public TrRmGenericParamTableReader(ReadOnlySpan<byte> section, int recordCount,
        TrRmStringTableReader strings, TrRmTypeDefTableReader types,
        TrRmMethodDefTableReader methods, TrRmMethodSigTableReader signatures,
        int constraintCount)
    {
        if (recordCount is < 0 or > 0xFF_FFFF || constraintCount is < 0 or > 0xFF_FFFF
            || (long)recordCount * RowSize != section.Length
            || (recordCount == 0 && constraintCount != 0))
        {
            throw InvalidFormal();
        }

        Count = recordCount;
        _constraintOwners = new int[constraintCount + 1];
        long declaredFormalCount = 0;
        for (var type = 1; type <= types.Count; type++)
        {
            declaredFormalCount += types.GetGenericRange(type).Count;
        }

        for (var method = 1; method <= methods.Count; method++)
        {
            declaredFormalCount += signatures.GetGenericRange(methods.GetSignature(method)).Count;
        }

        if (declaredFormalCount != recordCount)
        {
            throw InvalidFormal();
        }

        // Per-row range membership plus the equal total proves an exact partition.
        for (var row = 1; row <= recordCount; row++)
        {
            var data = section.Slice((row - 1) * RowSize, RowSize);
            var owner = Read(data, 0);
            var ownerRow = (int)(owner & 0xFF_FFFF);
            var (firstFormal, formalCount) = (owner >> 24) switch
            {
                0x13u when ownerRow > 0 && ownerRow <= types.Count
                    => types.GetGenericRange(ownerRow),
                0x16u when ownerRow > 0 && ownerRow <= methods.Count
                    => signatures.GetGenericRange(methods.GetSignature(ownerRow)),
                _ => default,
            };
            var name = Read(data, 8);
            var ordinal = Read(data, 4);
            if (formalCount == 0 || row < firstFormal || row >= firstFormal + formalCount
                || ordinal != (uint)(row - firstFormal)
                || name is 0 or > 0xFF_FFFF)
            {
                throw InvalidFormal();
            }

            var first = Read(data, 12);
            var count = Read(data, 16);
            if (count == 0 ? first != 0
                : first == 0 || first > constraintCount
                    || count > (uint)constraintCount - first + 1)
            {
                throw InvalidFormal();
            }

            for (var constraint = first; constraint < first + count; constraint++)
            {
                if (_constraintOwners[constraint] != 0)
                {
                    throw InvalidFormal();
                }

                _constraintOwners[constraint] = row;
            }

            _ = strings.DecodeName((int)name);
        }

        for (var row = 1; row <= constraintCount; row++)
        {
            if (_constraintOwners[row] == 0)
            {
                throw InvalidFormal();
            }
        }
    }

    public int GetConstraintOwner(int row)
    {
        if (row <= 0 || row >= _constraintOwners.Length)
        {
            throw InvalidFormal();
        }

        return _constraintOwners[row];
    }

    private static uint Read(ReadOnlySpan<byte> data, int offset)
        => BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, sizeof(uint)));

    private static InvalidDataException InvalidFormal()
        => new("Invalid TRRM GenericParam table.");
}
