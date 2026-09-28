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
        var owners = new uint[recordCount + 1];
        _constraintOwners = new int[constraintCount + 1];
        for (var type = 1; type <= types.Count; type++)
        {
            var (start, count) = types.GetGenericRange(type);
            Fill(owners, start, count, 0x1300_0000u | (uint)type);
        }

        for (var method = 1; method <= methods.Count; method++)
        {
            var (start, count) = signatures.GetGenericRange(methods.GetSignature(method));
            Fill(owners, start, count, 0x1600_0000u | (uint)method);
        }

        var previousOwner = 0u;
        var ordinal = 0;
        for (var row = 1; row <= recordCount; row++)
        {
            var data = section.Slice((row - 1) * RowSize, RowSize);
            var name = Read(data, 8);
            if (owners[row] == 0 || Read(data, 0) != owners[row]
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

            ordinal = owners[row] == previousOwner ? ordinal + 1 : 0;
            if (Read(data, 4) != ordinal)
            {
                throw InvalidFormal();
            }

            previousOwner = owners[row];
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

    private static void Fill(uint[] owners, int start, int count, uint owner)
    {
        for (var row = start; row < start + count; row++)
        {
            if (owners[row] != 0)
            {
                throw InvalidFormal();
            }

            owners[row] = owner;
        }
    }

    private static uint Read(ReadOnlySpan<byte> data, int offset)
        => BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, sizeof(uint)));

    private static InvalidDataException InvalidFormal()
        => new("Invalid TRRM GenericParam table.");
}
