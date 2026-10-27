// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.IO;

namespace Terrum.CodeAnalysis.Modules;

/// <summary>Validates Param types, modes, ordinals and MethodSig ownership.</summary>
/// <remarks>The borrowed Param, MethodSig and TypeSpec sections must remain unchanged while this reader is in use.</remarks>
public readonly ref struct TrRmParamTableReader
{
    private const int RowSize = 16;
    private readonly ReadOnlySpan<byte> _rows;

    /// <summary>Validates Param rows using the Standard schema.</summary>
    /// <param name="section">The fixed-width Param row section.</param>
    /// <param name="recordCount">The number of Param rows.</param>
    /// <param name="signatures">The validated MethodSig table.</param>
    /// <param name="types">The validated TypeSpec table.</param>
    public TrRmParamTableReader(ReadOnlySpan<byte> section, int recordCount,
        TrRmMethodSigTableReader signatures, TrRmTypeSpecTableReader types)
        : this(section, recordCount, signatures, types, TrRmArtifactSchema.Standard)
    {
    }

    internal TrRmParamTableReader(ReadOnlySpan<byte> section, int recordCount,
        TrRmMethodSigTableReader signatures, TrRmTypeSpecTableReader types,
        TrRmArtifactSchema schema)
    {
        if (recordCount <= 0 || recordCount > 0xFF_FFFF
            || recordCount != signatures.ParamCount
            || (long)recordCount * RowSize != section.Length
            || schema is not (TrRmArtifactSchema.Standard
                or TrRmArtifactSchema.Development
                or TrRmArtifactSchema.CopyOriginDraft
                or TrRmArtifactSchema.ForeignInvocationDraft))
        {
            throw InvalidParam();
        }

        _rows = section;
        var previousOwner = 0;
        var ordinal = 0;
        for (var row = 1; row <= recordCount; row++)
        {
            var data = section.Slice((row - 1) * RowSize, RowSize);
            var owner = Read(data, 0);
            var expectedOwner = signatures.GetParamOwner(row);
            ordinal = expectedOwner == previousOwner ? ordinal + 1 : 0;
            var type = Read(data, 8);
            if (owner != expectedOwner || Read(data, 4) != ordinal
                || !ValidModeAndFlags(data, type, types, schema))
            {
                throw InvalidParam();
            }

            _ = types.GetTypeSpecRef(type);
            previousOwner = expectedOwner;
        }
    }

    /// <summary>Gets the assigned passing mode for a Param row.</summary>
    /// <param name="row">The one-based Param row.</param>
    public byte GetMode(int row)
    {
        if (row <= 0 || (long)row * RowSize > _rows.Length)
        {
            throw InvalidParam();
        }

        return _rows[((row - 1) * RowSize) + 12];
    }

    internal byte GetFlags(int row)
    {
        if (row <= 0 || (long)row * RowSize > _rows.Length)
        {
            throw InvalidParam();
        }

        return _rows[((row - 1) * RowSize) + 13];
    }

    /// <summary>Gets the complete TypeSpecRef for a Param row.</summary>
    /// <param name="row">The one-based Param row.</param>
    public uint GetTypeSpec(int row)
    {
        if (row <= 0 || (long)row * RowSize > _rows.Length)
        {
            throw InvalidParam();
        }

        return Read(_rows.Slice((row - 1) * RowSize, RowSize), 8);
    }

    private static uint Read(ReadOnlySpan<byte> data, int offset)
        => BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, sizeof(uint)));

    private static bool ValidModeAndFlags(ReadOnlySpan<byte> data, uint typeSpecRef,
        TrRmTypeSpecTableReader types, TrRmArtifactSchema schema)
    {
        if (data[14] != 0 || data[15] != 0)
        {
            return false;
        }

        if (schema == TrRmArtifactSchema.Development)
        {
            return data[12] <= 4 && data[13] == 0;
        }

        return data[12] == 0 && (data[13] & ~1) == 0
            && ((data[13] & 1) == 0 || IsImplicitReadonlyReference(typeSpecRef, types));
    }

    private static bool IsImplicitReadonlyReference(uint typeSpecRef,
        TrRmTypeSpecTableReader types)
    {
        var type = types.GetTypeSpecRef(typeSpecRef);
        return type.Qualifiers == 0
            && (type.Form == 0x07
                || (type.Form == 0x0C && type.TypeCode == 0x8B)
                || (type.Form == 0x01 && type.TypeCode == 0x8B));
    }

    private static InvalidDataException InvalidParam()
        => new("Invalid TRRM Param table.");
}
