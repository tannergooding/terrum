// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Terrum.CodeAnalysis.Lexing;

namespace Terrum.CodeAnalysis.Modules;

internal readonly ref struct TrRmMethodParameterLabelTableReader
{
    private const int RowSize = 12;
    private readonly Dictionary<ulong, uint>? _nameRows;
    private readonly TrRmA3LayoutResourceLedger? _resourceLedger;

    internal TrRmMethodParameterLabelTableReader(ReadOnlySpan<byte> section,
        int recordCount, TrRmStringTableReader strings,
        TrRmMethodDefTableReader methods,
        TrRmMethodSigTableReader signatures, TrRmArtifactSchema schema,
        TrRmA3LayoutResourceLedger? resourceLedger = null)
    {
        if (!schema.UsesMethodParameterLabels()
            || recordCount is < 1 or > 0xFF_FFFF
            || (long)recordCount * RowSize != section.Length)
        {
            throw InvalidTable();
        }

        _resourceLedger = resourceLedger;
        var nameRows = new Dictionary<ulong, uint>(recordCount);
        for (var row = 0; row < recordCount; row++)
        {
            resourceLedger?.ConsumeWork();
            var data = section.Slice(row * RowSize, RowSize);
            var owner = Read(data, 0);
            var ordinal = Read(data, 4);
            var nameRow = Read(data, 8);
            if (owner is 0 or > 0x00FF_FFFF
                || owner > (uint)methods.Count
                || nameRow is 0 or > 0x00FF_FFFF
                || nameRow > (uint)strings.Count)
            {
                throw InvalidTable();
            }

            var signature = methods.GetSignature(checked((int)owner));
            var (_, parameterCount) = signatures.GetParamRange(signature);
            if (ordinal >= (uint)parameterCount)
            {
                throw InvalidTable();
            }

            var name = strings.DecodeName(checked((int)nameRow));
            if (!IsIdentifier(name))
            {
                throw InvalidTable();
            }

            var key = ((ulong)owner << 32) | ordinal;
            if (!nameRows.TryAdd(key, nameRow))
            {
                throw InvalidTable();
            }
        }

        Count = recordCount;
        _nameRows = nameRows;
    }

    internal int Count { get; }

    internal bool TryGetNameStringRow(int methodDefRow, uint ordinal,
        out int nameStringRow)
    {
        _resourceLedger?.ConsumeWork();
        if (methodDefRow <= 0 || (uint)methodDefRow > 0x00FF_FFFF
            || _nameRows is null
            || !_nameRows.TryGetValue(
                ((ulong)(uint)methodDefRow << 32) | ordinal,
                out var row))
        {
            nameStringRow = 0;
            return false;
        }

        nameStringRow = checked((int)row);
        return true;
    }

    private static uint Read(ReadOnlySpan<byte> row, int offset)
        => BinaryPrimitives.ReadUInt32LittleEndian(row.Slice(offset, 4));

    private static bool IsIdentifier(string name)
    {
        if (name.Length == 0 || name == "_")
        {
            return false;
        }

        var first = true;
        foreach (var rune in name.EnumerateRunes())
        {
            if (rune.Value != '_' && !(first
                    ? UnicodeXid.IsStart(rune.Value)
                    : UnicodeXid.IsContinue(rune.Value)))
            {
                return false;
            }

            first = false;
        }

        return !first;
    }

    private static InvalidDataException InvalidTable()
        => new("Invalid TRRM MethodParameterLabel table.");
}
