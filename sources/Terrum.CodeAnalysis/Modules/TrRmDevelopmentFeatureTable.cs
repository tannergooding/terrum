// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;

namespace Terrum.CodeAnalysis.Modules;

internal readonly record struct TrRmDevelopmentFeatureRow(uint FeatureId,
    uint MinimumRuleRevision)
{
    internal static TrRmDevelopmentFeatureRow ScalarInitOutput
        => new(TrRmDevelopmentFeatureTableReader.ScalarInitOutputFeatureId,
            TrRmDevelopmentFeatureTableReader.ScalarInitOutputRuleRevision);

    internal static TrRmDevelopmentFeatureRow NativeBoundaryContracts
        => new(TrRmDevelopmentFeatureTableReader.NativeBoundaryContractsFeatureId, 1);

    internal static TrRmDevelopmentFeatureRow RootlessNativeValues
        => new(TrRmDevelopmentFeatureTableReader.RootlessNativeValuesFeatureId, 1);

    internal static TrRmDevelopmentFeatureRow DirectNativeCalls
        => new(TrRmDevelopmentFeatureTableReader.DirectNativeCallsFeatureId, 1);

    internal static TrRmDevelopmentFeatureRow NativeCallableEntries
        => new(TrRmDevelopmentFeatureTableReader.NativeCallableEntriesFeatureId, 1);

    internal static TrRmDevelopmentFeatureRow NativeVTableCalls
        => new(TrRmDevelopmentFeatureTableReader.NativeVTableCallsFeatureId, 1);
}

internal readonly ref struct TrRmDevelopmentFeatureTableReader
{
    internal const uint ScalarInitOutputFeatureId = 1;
    internal const uint NativeBoundaryContractsFeatureId = 0x0100;
    internal const uint RootlessNativeValuesFeatureId = 0x0101;
    internal const uint DirectNativeCallsFeatureId = 0x0102;
    internal const uint NativeCallableEntriesFeatureId = 0x0103;
    internal const uint NativeVTableCallsFeatureId = 0x0104;
    internal const uint ScalarInitOutputInitialRuleRevision = 1;
    internal const uint ScalarInitOutputRuleRevision = 8;

    private readonly ReadOnlySpan<byte> _rows;
    private readonly int _recordCount;

    internal TrRmDevelopmentFeatureTableReader(ReadOnlySpan<byte> section,
        int recordCount)
    {
        if (recordCount < 0 || recordCount > 0xFF_FFFF
            || (long)recordCount * sizeof(ulong) != section.Length)
        {
            throw InvalidFeature();
        }

        _rows = section;
        _recordCount = recordCount;
        uint previousFeatureId = 0;
        for (var row = 0; row < recordCount; row++)
        {
            var offset = row * sizeof(ulong);
            var featureId = Read(offset);
            var ruleRevision = Read(offset + sizeof(uint));
            var maximumRevision = featureId switch
            {
                ScalarInitOutputFeatureId => ScalarInitOutputRuleRevision,
                NativeBoundaryContractsFeatureId or RootlessNativeValuesFeatureId
                    or DirectNativeCallsFeatureId or NativeCallableEntriesFeatureId
                    or NativeVTableCallsFeatureId => 1u,
                _ => 0u,
            };
            if (featureId <= previousFeatureId || ruleRevision == 0
                || ruleRevision > maximumRevision)
            {
                throw InvalidFeature();
            }

            previousFeatureId = featureId;
        }
    }

    internal int Count => _recordCount;

    internal TrRmDevelopmentFeatureRow GetFeature(int row)
    {
        if (row <= 0 || row > _recordCount)
        {
            throw InvalidFeature();
        }

        var offset = (row - 1) * sizeof(ulong);
        return new TrRmDevelopmentFeatureRow(Read(offset), Read(offset + sizeof(uint)));
    }

    internal bool TryGetFeature(uint featureId, out TrRmDevelopmentFeatureRow feature)
    {
        var low = 0;
        var high = _recordCount;
        while (low < high)
        {
            var middle = low + ((high - low) / 2);
            var currentFeatureId = Read(middle * sizeof(ulong));
            if (currentFeatureId < featureId)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        if (low < _recordCount && Read(low * sizeof(ulong)) == featureId)
        {
            var offset = low * sizeof(ulong);
            feature = new TrRmDevelopmentFeatureRow(Read(offset),
                Read(offset + sizeof(uint)));
            return true;
        }

        feature = default;
        return false;
    }

    private uint Read(int offset)
        => BinaryPrimitives.ReadUInt32LittleEndian(_rows.Slice(offset, sizeof(uint)));

    private static InvalidDataException InvalidFeature()
        => new("Invalid development TRRM Feature table.");
}

internal static class TrRmDevelopmentFeatureTableWriter
{
    internal static byte[] Encode(IReadOnlyList<TrRmDevelopmentFeatureRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        if (rows.Count > 0xFF_FFFF)
        {
            throw new ArgumentOutOfRangeException(nameof(rows));
        }

        var result = new byte[rows.Count * sizeof(ulong)];
        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            BinaryPrimitives.WriteUInt32LittleEndian(
                result.AsSpan(index * sizeof(ulong)), row.FeatureId);
            BinaryPrimitives.WriteUInt32LittleEndian(
                result.AsSpan((index * sizeof(ulong)) + sizeof(uint)),
                row.MinimumRuleRevision);
        }

        _ = new TrRmDevelopmentFeatureTableReader(result, rows.Count);
        return result;
    }
}
