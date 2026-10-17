// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.IO;

namespace Terrum.CodeAnalysis.Modules;

/// <summary>A required feature assignment in a TRRM Feature table.</summary>
/// <param name="FeatureId">The assigned feature identifier.</param>
/// <param name="MinimumRuleRevision">The minimum rule revision required by the artifact.</param>
public readonly record struct TrRmFeatureRow(uint FeatureId, uint MinimumRuleRevision)
{
    /// <summary>The provisional ScalarInitOutput assignment supported by this implementation.</summary>
    public static TrRmFeatureRow ScalarInitOutput
        => new(TrRmFeatureTableReader.ScalarInitOutputFeatureId,
            TrRmFeatureTableReader.ScalarInitOutputRuleRevision);

    /// <summary>The provisional native boundary contract assignment.</summary>
    public static TrRmFeatureRow NativeBoundaryContracts
        => new(TrRmFeatureTableReader.NativeBoundaryContractsFeatureId,
            TrRmFeatureTableReader.NativeBoundaryContractsRuleRevision);

    /// <summary>The provisional native rootless-value assignment.</summary>
    public static TrRmFeatureRow RootlessNativeValues
        => new(TrRmFeatureTableReader.RootlessNativeValuesFeatureId,
            TrRmFeatureTableReader.RootlessNativeValuesRuleRevision);

    /// <summary>The provisional direct native-call assignment.</summary>
    public static TrRmFeatureRow DirectNativeCalls
        => new(TrRmFeatureTableReader.DirectNativeCallsFeatureId,
            TrRmFeatureTableReader.DirectNativeCallsRuleRevision);

    /// <summary>The provisional native callable-entry assignment.</summary>
    public static TrRmFeatureRow NativeCallableEntries
        => new(TrRmFeatureTableReader.NativeCallableEntriesFeatureId,
            TrRmFeatureTableReader.NativeCallableEntriesRuleRevision);

    /// <summary>The provisional native vtable-call assignment.</summary>
    public static TrRmFeatureRow NativeVTableCalls
        => new(TrRmFeatureTableReader.NativeVTableCallsFeatureId,
            TrRmFeatureTableReader.NativeVTableCallsRuleRevision);
}

/// <summary>Validates the required feature assignments supported by provisional TRRM (1, 0).</summary>
/// <remarks>The borrowed Feature section must remain unchanged while this reader is in use.</remarks>
public readonly ref struct TrRmFeatureTableReader
{
    /// <summary>The provisional feature identifier assigned to ScalarInitOutput.</summary>
    public const uint ScalarInitOutputFeatureId = 1;

    /// <summary>The provisional candidate identifier for native boundary contracts.</summary>
    public const uint NativeBoundaryContractsFeatureId = 0x0100;

    /// <summary>The provisional candidate identifier for rootless native values.</summary>
    public const uint RootlessNativeValuesFeatureId = 0x0101;

    /// <summary>The provisional candidate identifier for direct native calls.</summary>
    public const uint DirectNativeCallsFeatureId = 0x0102;

    /// <summary>The provisional candidate identifier for native callable entries.</summary>
    public const uint NativeCallableEntriesFeatureId = 0x0103;

    /// <summary>The provisional candidate identifier for native vtable calls.</summary>
    public const uint NativeVTableCallsFeatureId = 0x0104;

    /// <summary>The initial ScalarInitOutput rule revision supported by this implementation.</summary>
    public const uint ScalarInitOutputInitialRuleRevision = 1;

    /// <summary>The current ScalarInitOutput rule revision supported by this implementation.</summary>
    public const uint ScalarInitOutputRuleRevision = 8;

    /// <summary>The provisional native boundary contract rule revision supported by this implementation.</summary>
    public const uint NativeBoundaryContractsRuleRevision = 1;

    /// <summary>The provisional rootless native value rule revision supported by this implementation.</summary>
    public const uint RootlessNativeValuesRuleRevision = 1;

    /// <summary>The provisional direct native-call rule revision supported by this implementation.</summary>
    public const uint DirectNativeCallsRuleRevision = 1;

    /// <summary>The provisional native callable-entry rule revision supported by this implementation.</summary>
    public const uint NativeCallableEntriesRuleRevision = 1;

    /// <summary>The provisional native vtable-call rule revision supported by this implementation.</summary>
    public const uint NativeVTableCallsRuleRevision = 1;

    private const int RowSize = 8;
    private readonly ReadOnlySpan<byte> _rows;
    private readonly int _recordCount;

    /// <summary>Gets the number of validated feature rows.</summary>
    public int Count => _recordCount;

    /// <summary>Validates the fixed-width Feature rows and supported assignments.</summary>
    /// <param name="section">The borrowed Feature section bytes.</param>
    /// <param name="recordCount">The number of Feature rows.</param>
    public TrRmFeatureTableReader(ReadOnlySpan<byte> section, int recordCount)
    {
        if (recordCount < 0 || recordCount > 0xFF_FFFF
            || (long)recordCount * RowSize != section.Length)
        {
            throw InvalidFeature();
        }

        _rows = section;
        _recordCount = recordCount;
        uint previousFeatureId = 0;
        for (var row = 0; row < recordCount; row++)
        {
            var offset = row * RowSize;
            var featureId = Read(offset);
            var minimumRuleRevision = Read(offset + 4);
            var maximumRevision = featureId switch
            {
                ScalarInitOutputFeatureId => ScalarInitOutputRuleRevision,
                NativeBoundaryContractsFeatureId => NativeBoundaryContractsRuleRevision,
                RootlessNativeValuesFeatureId => RootlessNativeValuesRuleRevision,
                DirectNativeCallsFeatureId => DirectNativeCallsRuleRevision,
                NativeCallableEntriesFeatureId => NativeCallableEntriesRuleRevision,
                NativeVTableCallsFeatureId => NativeVTableCallsRuleRevision,
                _ => 0u,
            };
            if (featureId <= previousFeatureId || minimumRuleRevision == 0
                || minimumRuleRevision > maximumRevision)
            {
                throw InvalidFeature();
            }

            previousFeatureId = featureId;
        }
    }

    /// <summary>Gets a validated Feature row by its one-based row number.</summary>
    /// <param name="row">The one-based Feature row number.</param>
    public TrRmFeatureRow GetFeature(int row)
    {
        if (row <= 0 || row > _recordCount)
        {
            throw InvalidFeature();
        }

        var offset = (row - 1) * RowSize;
        return new TrRmFeatureRow(Read(offset), Read(offset + 4));
    }

    /// <summary>Finds a validated Feature row by feature identifier.</summary>
    /// <param name="featureId">The feature identifier to locate.</param>
    /// <param name="feature">Receives the matching feature row, if present.</param>
    /// <returns><see langword="true"/> if a matching row exists; otherwise, <see langword="false"/>.</returns>
    public bool TryGetFeature(uint featureId, out TrRmFeatureRow feature)
    {
        var low = 0;
        var high = _recordCount;
        while (low < high)
        {
            var middle = low + (high - low) / 2;
            var currentFeatureId = Read(middle * RowSize);
            if (currentFeatureId < featureId)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        if (low < _recordCount && Read((low * RowSize)) == featureId)
        {
            feature = new TrRmFeatureRow(Read((low * RowSize)), Read((low * RowSize) + 4));
            return true;
        }

        feature = default;
        return false;
    }

    private uint Read(int offset)
        => BinaryPrimitives.ReadUInt32LittleEndian(_rows.Slice(offset, sizeof(uint)));

    private static InvalidDataException InvalidFeature()
        => new("Invalid TRRM Feature table.");
}
