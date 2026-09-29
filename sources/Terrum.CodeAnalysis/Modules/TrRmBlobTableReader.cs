// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;

namespace Terrum.CodeAnalysis.Modules;

/// <summary>Validated index over one present TRRM Blob section.</summary>
/// <remarks>The borrowed section bytes must remain unchanged while this reader is in use.</remarks>
public readonly ref struct TrRmBlobTableReader
{
    private readonly TrRmVariableTableIndex _rows;

    public int Count => _rows.Count;

    public TrRmBlobTableReader(ReadOnlySpan<byte> section, int recordCount)
    {
        _rows = new TrRmVariableTableIndex(section, recordCount, 0, 1);
    }

    public ReadOnlySpan<byte> GetRow(int row)
        => _rows.GetRow(row);
}
