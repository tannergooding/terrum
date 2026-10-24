// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.IO;

namespace Terrum.CodeAnalysis.Modules;

/// <summary>Validates formal ownership, ordinals, names and complete Constraint ranges.</summary>
/// <remarks>Structural checks do not establish executable generic constraints. Borrowed sections must remain unchanged.</remarks>
public readonly ref struct TrRmGenericParamTableReader
{
    private const int RowSize = 32;
    private readonly ReadOnlySpan<byte> _rows;
    private readonly int[] _constraintOwners;

    /// <summary>Gets the number of validated GenericParam rows.</summary>
    public int Count { get; }

    /// <summary>Gets the number of Constraint rows owned by these formals.</summary>
    public int ConstraintCount => _constraintOwners.Length - 1;

    /// <summary>Validates GenericParam ownership, ordinals, domains and Constraint ranges.</summary>
    /// <param name="section">The borrowed GenericParam section bytes.</param>
    /// <param name="recordCount">The number of 32-byte GenericParam rows.</param>
    /// <param name="strings">The validated String table.</param>
    /// <param name="types">The validated TypeDef table.</param>
    /// <param name="methods">The validated MethodDef table.</param>
    /// <param name="signatures">The validated MethodSig table.</param>
    /// <param name="constraintCount">The number of Constraint rows.</param>
    /// <param name="constantSourceCount">The number of ConstantSource rows, if available.</param>
    public TrRmGenericParamTableReader(ReadOnlySpan<byte> section, int recordCount,
        TrRmStringTableReader strings, TrRmTypeDefTableReader types,
        TrRmMethodDefTableReader methods, TrRmMethodSigTableReader signatures,
        int constraintCount, int constantSourceCount = 0)
    {
        if (recordCount is < 0 or > 0xFF_FFFF || constraintCount is < 0 or > 0xFF_FFFF
            || constantSourceCount is < 0 or > 0xFF_FFFF
            || (long)recordCount * RowSize != section.Length
            || (recordCount == 0 && constraintCount != 0))
        {
            throw InvalidFormal();
        }

        _rows = section;
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
            var formalKind = data[20];
            var ownershipDomain = data[21];
            var nullabilityDomain = data[22];
            var reserved = data[23];
            var first = Read(data, 12);
            var count = Read(data, 16);
            var constantTypeSpec = Read(data, 24);
            var defaultArgument = Read(data, 28);
            var invalidFormalShape = formalKind switch
            {
                1 => ownershipDomain is < 1 or > 3
                    || nullabilityDomain is < 1 or > 2
                    || constantTypeSpec != 0
                    || (defaultArgument != 0 && defaultArgument > (uint)types.Count),
                2 => ownershipDomain != 0 || nullabilityDomain != 0
                    || constantTypeSpec == 0
                    || constantTypeSpec > (uint)types.Count
                    || count != 0 || (defaultArgument != 0
                        && defaultArgument > (uint)constantSourceCount),
                _ => true,
            };
            if (formalCount == 0 || row < firstFormal || row >= firstFormal + formalCount
                || ordinal != (uint)(row - firstFormal)
                || name is 0 or > 0xFF_FFFF || reserved != 0
                || invalidFormalShape)
            {
                throw InvalidFormal();
            }

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

    /// <summary>Gets the one-based GenericParam row that owns a Constraint.</summary>
    public int GetConstraintOwner(int row)
    {
        if (row <= 0 || row >= _constraintOwners.Length)
        {
            throw InvalidFormal();
        }

        return _constraintOwners[row];
    }

    /// <summary>Gets the TypeDef or MethodDef identity that owns a formal.</summary>
    public uint GetOwnerDefId(int row)
        => ReadRow(row, 0);

    /// <summary>Gets the zero-based ordinal in the owning declaration's formal range.</summary>
    public uint GetOrdinal(int row)
        => ReadRow(row, 4);

    /// <summary>Gets the one-based String row containing the formal name.</summary>
    public int GetNameStringRow(int row)
        => checked((int)ReadRow(row, 8));

    /// <summary>Gets the one-based Constraint range owned by a formal.</summary>
    public (int First, int Count) GetConstraintRange(int row)
        => (checked((int)ReadRow(row, 12)), checked((int)ReadRow(row, 16)));

    /// <summary>Gets the assigned formal kind.</summary>
    public byte GetFormalKind(int row)
        => GetRow(row)[20];

    /// <summary>Gets the type formal's ownership domain, or zero for a constant formal.</summary>
    public byte GetOwnershipDomain(int row)
        => GetRow(row)[21];

    /// <summary>Gets the type formal's nullability domain, or zero for a constant formal.</summary>
    public byte GetNullabilityDomain(int row)
        => GetRow(row)[22];

    /// <summary>Gets the constant formal's type reference, or zero for a type formal.</summary>
    public uint GetConstantTypeSpecRef(int row)
        => ReadRow(row, 24);

    /// <summary>Gets the default TypeSpecRef or ConstantSource row, or zero when absent.</summary>
    public uint GetDefaultArgumentRef(int row)
        => ReadRow(row, 28);

    private ReadOnlySpan<byte> GetRow(int row)
    {
        if (row <= 0 || row > Count)
        {
            throw InvalidFormal();
        }

        return _rows.Slice((row - 1) * RowSize, RowSize);
    }

    private uint ReadRow(int row, int offset)
        => Read(GetRow(row), offset);

    private static uint Read(ReadOnlySpan<byte> data, int offset)
        => BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, sizeof(uint)));

    private static InvalidDataException InvalidFormal()
        => new("Invalid TRRM GenericParam table.");
}
