// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;

namespace Terrum.CodeAnalysis.Modules;

/// <summary>Checks exported definition visibility and unique keys per producer Module.</summary>
/// <remarks>Import binding, contract compatibility and implementation authenticity require separate validation.</remarks>
public readonly ref struct TrRmExportTableReader
{
    private const int RowSize = 8;

    public TrRmExportTableReader(ReadOnlySpan<byte> section, int recordCount,
        TrRmStringTableReader strings, TrRmTypeDefTableReader types,
        TrRmMethodDefTableReader methods, TrRmPropertyDefTableReader properties,
        TrRmImportTableReader imports)
    {
        if (recordCount is < 0 or > 0xFF_FFFF
            || (long)recordCount * RowSize != section.Length)
        {
            throw InvalidExport();
        }

        var keys = new HashSet<(int Module, string Key)>();
        for (var row = 0; row < recordCount; row++)
        {
            var data = section.Slice(row * RowSize, RowSize);
            var definition = Read(data, 0);
            var key = Read(data, 4);
            if (key is 0 or > 0xFF_FFFF)
            {
                throw InvalidExport();
            }

            var (module, visibility, owner, _) = TrRmDefinitionResolver.Resolve(
                definition, types, methods, properties);
            var text = strings.DecodeName((int)key);
            if (visibility != 2 || (owner != 0 && (types.GetFlags(owner) & 3) != 2)
                || imports.IsExpectedDefinition(definition) || !keys.Add((module, text)))
            {
                throw InvalidExport();
            }
        }
    }

    private static uint Read(ReadOnlySpan<byte> data, int offset)
        => BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, sizeof(uint)));

    private static InvalidDataException InvalidExport()
        => new("Invalid TRRM Export table.");
}
