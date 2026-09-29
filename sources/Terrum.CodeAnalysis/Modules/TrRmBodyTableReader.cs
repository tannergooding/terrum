// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;

namespace Terrum.CodeAnalysis.Modules;

/// <summary>Validated index over one present TRRM Body section; instruction bytes remain opaque.</summary>
/// <remarks>The borrowed section bytes must remain unchanged while this reader is in use.</remarks>
public readonly ref struct TrRmBodyTableReader
{
    private readonly TrRmVariableTableIndex _rows;

    public TrRmBodyTableReader(ReadOnlySpan<byte> section, int recordCount)
    {
        _rows = new TrRmVariableTableIndex(section, recordCount, 1, 1);
    }

    public ReadOnlySpan<byte> GetRow(int row)
        => _rows.GetRow(row);
}
