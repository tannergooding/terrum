// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;

namespace Terrum.CodeAnalysis.Modules;

internal readonly record struct TrRmIntrinsicRegistrationRow(
    int OwnerModuleRow, uint RegistrationId, uint RuleRevision,
    byte DeclarationRole, byte Visibility);

internal readonly ref struct TrRmIntrinsicRegistrationTableReader
{
    private const int RowSize = 16;

    private readonly ReadOnlySpan<byte> _rows;
    private readonly int _recordCount;

    internal int Count => _recordCount;

    internal TrRmIntrinsicRegistrationTableReader(ReadOnlySpan<byte> section,
        int recordCount, TrRmModuleTableReader modules, TrRmArtifactSchema schema)
    {
        if (!TrRmStaticFieldTableReader.ValidCount(recordCount)
            || (long)recordCount * RowSize != section.Length
            || schema != TrRmArtifactSchema.GeneralPredicateGraphsDraft)
        {
            throw InvalidTable();
        }

        _rows = section;
        _recordCount = recordCount;
        var ownedIdentities = new HashSet<(int Module, uint Id, uint Revision)>();
        for (var row = 1; row <= recordCount; row++)
        {
            var data = GetRow(row);
            var ownerModule = Read(data, 0);
            var registrationId = Read(data, 4);
            var ruleRevision = Read(data, 8);
            var role = data[12];
            var visibility = data[13];
            if (!TrRmStaticFieldTableReader.IsRowReference(
                    ownerModule, modules.Count)
                || registrationId == 0 || ruleRevision == 0
                || role is not (1 or 2) || visibility > 2
                || (role == 2 && visibility != 2)
                || BinaryPrimitives.ReadUInt16LittleEndian(data[14..]) != 0
                || (role == 1 && !ownedIdentities.Add(
                    (checked((int)ownerModule), registrationId, ruleRevision))))
            {
                throw InvalidTable();
            }
        }
    }

    internal int GetOwnerModuleRow(int row)
        => checked((int)Read(GetRow(row), 0));
    internal uint GetRegistrationId(int row) => Read(GetRow(row), 4);
    internal uint GetRuleRevision(int row) => Read(GetRow(row), 8);
    internal byte GetDeclarationRole(int row) => GetRow(row)[12];
    internal byte GetVisibility(int row) => GetRow(row)[13];

    private ReadOnlySpan<byte> GetRow(int row)
    {
        if (row <= 0 || row > _recordCount)
        {
            throw InvalidTable();
        }

        return _rows.Slice((row - 1) * RowSize, RowSize);
    }

    private static uint Read(ReadOnlySpan<byte> data, int offset)
        => BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, sizeof(uint)));

    private static InvalidDataException InvalidTable()
        => new("Invalid TRRM IntrinsicRegistration table.");
}

internal static class TrRmIntrinsicRegistrationTableWriter
{
    private const int RowSize = 16;

    internal static byte[] Encode(
        IReadOnlyList<TrRmIntrinsicRegistrationRow> rows,
        TrRmModuleTableReader modules, TrRmArtifactSchema schema)
    {
        ArgumentNullException.ThrowIfNull(rows);
        if (rows.Count > 0xFF_FFFF)
        {
            throw new ArgumentOutOfRangeException(nameof(rows));
        }

        var result = new byte[rows.Count * RowSize];
        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            var offset = index * RowSize;
            Write(result, offset, unchecked((uint)row.OwnerModuleRow));
            Write(result, offset + 4, row.RegistrationId);
            Write(result, offset + 8, row.RuleRevision);
            result[offset + 12] = row.DeclarationRole;
            result[offset + 13] = row.Visibility;
        }

        _ = new TrRmIntrinsicRegistrationTableReader(
            result, rows.Count, modules, schema);
        return result;
    }

    private static void Write(byte[] data, int offset, uint value)
        => BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(offset), value);
}
