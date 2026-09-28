// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;

namespace Terrum.CodeAnalysis.Modules;

/// <summary>Validated index over one present TRRM String section.</summary>
/// <remarks>The borrowed section bytes must remain unchanged while this reader is in use.</remarks>
public readonly ref struct TrRmStringTableReader
{
    private readonly TrRmVariableTableIndex _rows;

    public TrRmStringTableReader(ReadOnlySpan<byte> section, int recordCount)
    {
        _rows = new TrRmVariableTableIndex(section, recordCount, 8, 4);
    }

    public string DecodeName(int row)
        => TrRmStringRowReader.DecodeName(_rows.GetRow(row));

    public string DecodeNamespace(int row)
        => TrRmStringRowReader.DecodeNamespace(_rows.GetRow(row));
}
