// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;

namespace Terrum.CodeAnalysis.Modules;

/// <summary>Validated index over one present TRRM Blob section.</summary>
/// <remarks>The borrowed section bytes must remain unchanged while this reader is in use.</remarks>
public readonly ref struct TrRmBlobTableReader
{
    private readonly TrRmVariableTableIndex _rows;
    private readonly TrRmA3LayoutResourceLedger? _resourceLedger;

    /// <summary>Gets the number of validated Blob rows.</summary>
    public int Count => _rows.Count;

    /// <summary>Validates and indexes the borrowed Blob section.</summary>
    /// <param name="section">The Blob section bytes, which must remain unchanged while this reader is in use.</param>
    /// <param name="recordCount">The number of Blob rows.</param>
    public TrRmBlobTableReader(ReadOnlySpan<byte> section, int recordCount)
        : this(section, recordCount, null)
    {
    }

    internal TrRmBlobTableReader(ReadOnlySpan<byte> section, int recordCount,
        TrRmA3LayoutResourceLedger? resourceLedger)
    {
        _rows = new TrRmVariableTableIndex(section, recordCount, 0, 1,
            resourceLedger);
        _resourceLedger = resourceLedger;
    }

    /// <summary>Gets the borrowed bytes of a one-based Blob row.</summary>
    /// <param name="row">The one-based row number.</param>
    /// <returns>The row's bytes.</returns>
    public ReadOnlySpan<byte> GetRow(int row)
    {
        var result = _rows.GetRow(row);
        _resourceLedger?.ConsumeWork(checked((ulong)result.Length));
        return result;
    }
}
