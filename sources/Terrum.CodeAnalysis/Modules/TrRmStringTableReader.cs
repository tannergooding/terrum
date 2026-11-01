// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;

namespace Terrum.CodeAnalysis.Modules;

/// <summary>Validated index over one present TRRM String section.</summary>
/// <remarks>The borrowed section bytes must remain unchanged while this reader is in use.</remarks>
public readonly ref struct TrRmStringTableReader
{
    private readonly TrRmVariableTableIndex _rows;
    private readonly TrRmA3LayoutResourceLedger? _resourceLedger;

    public TrRmStringTableReader(ReadOnlySpan<byte> section, int recordCount)
        : this(section, recordCount, null)
    {
    }

    internal TrRmStringTableReader(ReadOnlySpan<byte> section, int recordCount,
        TrRmA3LayoutResourceLedger? resourceLedger)
    {
        resourceLedger?.ConsumeWork();
        _rows = new TrRmVariableTableIndex(section, recordCount, 8, 4,
            resourceLedger);
        _resourceLedger = resourceLedger;
        Count = recordCount;
    }

    /// <summary>Gets the number of validated string rows.</summary>
    public int Count { get; }

    public string DecodeName(int row)
        => DecodeText(row, allowEmpty: false);

    public string DecodeNamespace(int row)
        => DecodeText(row, allowEmpty: true);

    internal void ValidateShapes(int recordCount)
    {
        for (var row = 1; row <= recordCount; row++)
        {
            _resourceLedger?.ConsumeWork();
            var data = _rows.GetRow(row);
            _resourceLedger?.ConsumeWork(checked((ulong)data.Length));
            TrRmStringRowReader.ValidateShape(data);
        }
    }

    private string DecodeText(int row, bool allowEmpty)
    {
        _resourceLedger?.ConsumeWork();
        var data = _rows.GetRow(row);
        var inputBytes = checked((ulong)data.Length);
        _resourceLedger?.ConsumeWork(checked(inputBytes + 1UL));
        var characterCount
            = TrRmStringRowReader.GetDecodedCharacterCount(data);
        _resourceLedger?.ConsumeWork(checked(inputBytes + 1UL
            + ((ulong)characterCount * sizeof(char))));
        return allowEmpty
            ? TrRmStringRowReader.DecodeNamespace(data)
            : TrRmStringRowReader.DecodeName(data);
    }
}
