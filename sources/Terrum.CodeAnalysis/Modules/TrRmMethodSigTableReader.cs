// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.IO;

namespace Terrum.CodeAnalysis.Modules;

/// <summary>Validates structural MethodSig references and complete, disjoint Param ranges.</summary>
/// <remarks>The borrowed MethodSig and TypeSpec sections must remain unchanged while this reader is in use.</remarks>
public readonly ref struct TrRmMethodSigTableReader
{
    private const int RowSize = 24;
    private readonly ReadOnlySpan<byte> _rows;
    private readonly int[] _paramOwners;
    private readonly TrRmA3LayoutResourceLedger? _resourceLedger;

    /// <summary>Gets the number of validated MethodSig rows.</summary>
    public int Count { get; }
    /// <summary>Gets the number of validated Param rows.</summary>
    public int ParamCount => _paramOwners.Length - 1;

    /// <summary>Validates MethodSig rows and their complete, disjoint Param ranges.</summary>
    /// <param name="section">The borrowed MethodSig section bytes.</param>
    /// <param name="recordCount">The number of MethodSig rows.</param>
    /// <param name="types">The validated TypeSpec table.</param>
    /// <param name="paramCount">The total number of Param rows.</param>
    /// <param name="genericParamCount">The total number of generic parameter rows.</param>
    public TrRmMethodSigTableReader(ReadOnlySpan<byte> section, int recordCount,
        TrRmTypeSpecTableReader types, int paramCount, int genericParamCount)
        : this(section, recordCount, types, paramCount, genericParamCount, null)
    {
    }

    internal TrRmMethodSigTableReader(ReadOnlySpan<byte> section,
        int recordCount, TrRmTypeSpecTableReader types, int paramCount,
        int genericParamCount, TrRmA3LayoutResourceLedger? resourceLedger)
    {
        resourceLedger?.ConsumeWork(5);
        if (recordCount <= 0 || recordCount > 0xFF_FFFF
            || paramCount is < 0 or > 0xFF_FFFF
            || genericParamCount is < 0 or > 0xFF_FFFF
            || (long)recordCount * RowSize != section.Length)
        {
            throw InvalidSignature();
        }

        Count = recordCount;
        _rows = section;
        _resourceLedger = resourceLedger;
        _resourceLedger?.ConsumeWork(checked((ulong)paramCount + 1UL));
        _paramOwners = new int[paramCount + 1];
        for (var row = 1; row <= recordCount; row++)
        {
            _resourceLedger?.ConsumeWork();
            var data = section.Slice((row - 1) * RowSize, RowSize);
            var result = Read(data, 0);
            var receiver = Read(data, 4);
            var first = Read(data, 8);
            var count = Read(data, 12);
            var genericFirst = Read(data, 16);
            var genericCount = Read(data, 20);
            _resourceLedger?.ConsumeWork(4);
            if (!ValidRange(first, count, paramCount)
                || !ValidRange(genericFirst, genericCount, genericParamCount))
            {
                throw InvalidSignature();
            }

            _ = types.GetTypeSpecRef(result);
            _ = types.GetTypeSpecRef(receiver, allowNull: true);

            for (var param = first; param < first + count; param++)
            {
                _resourceLedger?.ConsumeWork(2);
                if (_paramOwners[param] != 0)
                {
                    throw InvalidSignature();
                }

                _paramOwners[param] = row;
            }
        }

        for (var param = 1; param <= paramCount; param++)
        {
            _resourceLedger?.ConsumeWork();
            if (_paramOwners[param] == 0)
            {
                throw InvalidSignature();
            }
        }
    }

    /// <summary>Gets the MethodSig row that owns a one-based Param row.</summary>
    /// <param name="row">The one-based Param row.</param>
    /// <returns>The one-based owning MethodSig row.</returns>
    public int GetParamOwner(int row)
    {
        _resourceLedger?.ConsumeWork();
        if (row <= 0 || row >= _paramOwners.Length)
        {
            throw InvalidSignature();
        }

        _resourceLedger?.ConsumeWork();
        return _paramOwners[row];
    }

    /// <summary>Gets the receiver TypeSpec reference for a MethodSig row, or zero when absent.</summary>
    /// <param name="row">The one-based MethodSig row.</param>
    /// <returns>The receiver TypeSpec reference, or zero.</returns>
    public uint GetReceiverTypeSpec(int row)
    {
        _resourceLedger?.ConsumeWork();
        if (row <= 0 || row > Count)
        {
            throw InvalidSignature();
        }

        return Read(_rows.Slice((row - 1) * RowSize, RowSize), 4);
    }

    /// <summary>Gets the result TypeSpec reference for a MethodSig row.</summary>
    /// <param name="row">The one-based MethodSig row.</param>
    /// <returns>The result TypeSpec reference.</returns>
    public uint GetResultTypeSpec(int row)
    {
        _resourceLedger?.ConsumeWork();
        if (row <= 0 || row > Count)
        {
            throw InvalidSignature();
        }

        return Read(_rows.Slice((row - 1) * RowSize, RowSize), 0);
    }

    /// <summary>Gets the one-based Param range owned by a MethodSig row.</summary>
    /// <param name="row">The one-based MethodSig row.</param>
    /// <returns>The first Param row and number of rows.</returns>
    public (int Start, int Count) GetParamRange(int row)
    {
        _resourceLedger?.ConsumeWork();
        if (row <= 0 || row > Count)
        {
            throw InvalidSignature();
        }

        var data = _rows.Slice((row - 1) * RowSize, RowSize);
        return ((int)Read(data, 8), (int)Read(data, 12));
    }

    /// <summary>Gets the number of generic parameters in a MethodSig row's range.</summary>
    /// <param name="row">The one-based MethodSig row.</param>
    /// <returns>The number of generic parameter rows.</returns>
    public int GetGenericCount(int row)
    {
        _resourceLedger?.ConsumeWork();
        if (row <= 0 || row > Count)
        {
            throw InvalidSignature();
        }

        return (int)Read(_rows.Slice((row - 1) * RowSize, RowSize), 20);
    }

    /// <summary>Gets the one-based generic parameter range owned by a MethodSig row.</summary>
    /// <param name="row">The one-based MethodSig row.</param>
    /// <returns>The first generic parameter row and number of rows.</returns>
    public (int Start, int Count) GetGenericRange(int row)
    {
        _resourceLedger?.ConsumeWork();
        if (row <= 0 || row > Count)
        {
            throw InvalidSignature();
        }

        var data = _rows.Slice((row - 1) * RowSize, RowSize);
        return ((int)Read(data, 16), (int)Read(data, 20));
    }

    private uint Read(ReadOnlySpan<byte> data, int offset)
    {
        _resourceLedger?.ConsumeWork(sizeof(uint) + 1);
        return BinaryPrimitives.ReadUInt32LittleEndian(
            data.Slice(offset, sizeof(uint)));
    }

    private static bool ValidRange(uint first, uint count, int tableCount)
        => count == 0 ? first == 0
            : first != 0 && first <= tableCount && count <= (uint)tableCount - first + 1;

    private static InvalidDataException InvalidSignature()
        => new("Invalid TRRM MethodSig table.");
}
