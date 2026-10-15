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

    /// <summary>The provisional Windows named native import assignment.</summary>
    public static TrRmFeatureRow WindowsNamedNativeImports
        => new(TrRmFeatureTableReader.WindowsNamedNativeImportsFeatureId,
            TrRmFeatureTableReader.WindowsNamedNativeImportsRuleRevision);

    /// <summary>The provisional native COM contract assignment.</summary>
    public static TrRmFeatureRow NativeComContracts
        => new(TrRmFeatureTableReader.NativeComContractsFeatureId,
            TrRmFeatureTableReader.NativeComContractsRuleRevision);

    /// <summary>The provisional explicit native Blittable-layout assignment.</summary>
    public static TrRmFeatureRow NativeBlittableLayouts
        => new(TrRmFeatureTableReader.NativeBlittableLayoutsFeatureId,
            TrRmFeatureTableReader.NativeBlittableLayoutsRuleRevision);
}

/// <summary>Validates the required feature assignments supported by provisional TRRM (1, 0).</summary>
/// <remarks>The borrowed Feature section must remain unchanged while this reader is in use.</remarks>
public readonly ref struct TrRmFeatureTableReader
{
    /// <summary>The provisional feature identifier assigned to ScalarInitOutput.</summary>
    public const uint ScalarInitOutputFeatureId = 1;

    /// <summary>The provisional feature identifier assigned to Windows named native imports.</summary>
    public const uint WindowsNamedNativeImportsFeatureId = 2;

    /// <summary>The provisional feature identifier assigned to native COM contracts.</summary>
    public const uint NativeComContractsFeatureId = 3;

    /// <summary>The provisional feature identifier assigned to native Blittable layouts.</summary>
    public const uint NativeBlittableLayoutsFeatureId = 4;

    /// <summary>The initial ScalarInitOutput rule revision supported by this implementation.</summary>
    public const uint ScalarInitOutputInitialRuleRevision = 1;

    /// <summary>The current ScalarInitOutput rule revision supported by this implementation.</summary>
    public const uint ScalarInitOutputRuleRevision = 6;

    /// <summary>The initial Windows named native imports rule revision supported by this implementation.</summary>
    public const uint WindowsNamedNativeImportsRuleRevision = 1;

    /// <summary>The provisional native COM contract rule revision supported by this implementation.</summary>
    public const uint NativeComContractsRuleRevision = 1;

    /// <summary>The provisional native Blittable-layout rule revision supported by this implementation.</summary>
    public const uint NativeBlittableLayoutsRuleRevision = 1;

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
                WindowsNamedNativeImportsFeatureId => WindowsNamedNativeImportsRuleRevision,
                NativeComContractsFeatureId => NativeComContractsRuleRevision,
                NativeBlittableLayoutsFeatureId => NativeBlittableLayoutsRuleRevision,
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
