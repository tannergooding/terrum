// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.IO;

namespace Terrum.CodeAnalysis.Modules;

/// <summary>Validates Constraint row framing, owner, flags and required TypeSpec reference.</summary>
/// <remarks>Constraint kinds and witness references lack assigned executable semantics. Borrowed sections must remain unchanged.</remarks>
public readonly ref struct TrRmConstraintTableReader
{
    private const int RowSize = 16;

    public TrRmConstraintTableReader(ReadOnlySpan<byte> section, int recordCount,
        TrRmGenericParamTableReader formals, TrRmTypeSpecTableReader types)
    {
        if (recordCount is < 0 or > 0xFF_FFFF
            || recordCount != formals.ConstraintCount
            || (long)recordCount * RowSize != section.Length)
        {
            throw InvalidConstraint();
        }

        for (var row = 1; row <= recordCount; row++)
        {
            var data = section.Slice((row - 1) * RowSize, RowSize);
            var owner = Read(data, 0);
            var target = Read(data, 8);
            if (owner != formals.GetConstraintOwner(row)
                || BinaryPrimitives.ReadUInt16LittleEndian(data[6..]) != 0
                || target == 0 || target > types.Count)
            {
                throw InvalidConstraint();
            }
        }
    }

    private static uint Read(ReadOnlySpan<byte> data, int offset)
        => BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, sizeof(uint)));

    private static InvalidDataException InvalidConstraint()
        => new("Invalid TRRM Constraint table.");
}
