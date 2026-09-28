// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;

namespace Terrum.CodeAnalysis.Modules;

/// <summary>Validates retained dependency references and their local expected definitions.</summary>
/// <remarks>Does not bind the selected producer or compare full declared contracts.</remarks>
public readonly ref struct TrRmImportTableReader
{
    private const int RowSize = 12;
    private readonly HashSet<uint> _expected;

    public TrRmImportTableReader(ReadOnlySpan<byte> section, int recordCount,
        TrRmStringTableReader strings, TrRmModuleRefTableReader references,
        TrRmTypeDefTableReader types, TrRmMethodDefTableReader methods,
        TrRmPropertyDefTableReader properties)
    {
        if (recordCount is < 0 or > 0xFF_FFFF
            || (long)recordCount * RowSize != section.Length)
        {
            throw InvalidImport();
        }

        _expected = new HashSet<uint>();
        for (var row = 0; row < recordCount; row++)
        {
            var data = section.Slice(row * RowSize, RowSize);
            var moduleRef = Read(data, 0);
            var key = Read(data, 4);
            var expected = Read(data, 8);
            if (moduleRef == 0 || moduleRef > references.Count
                || key is 0 or > 0xFF_FFFF)
            {
                throw InvalidImport();
            }

            var (module, _, _, body) = TrRmDefinitionResolver.Resolve(expected,
                types, methods, properties);
            if (references.GetOwnerModule((int)moduleRef) != module
                || (expected >> 24 is 0x16 or 0x1E && body != 0)
                || !_expected.Add(expected))
            {
                throw InvalidImport();
            }

            _ = strings.DecodeName((int)key);
        }
    }

    public bool IsExpectedDefinition(uint reference)
        => _expected.Contains(reference);

    private static uint Read(ReadOnlySpan<byte> data, int offset)
        => BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, sizeof(uint)));

    private static InvalidDataException InvalidImport()
        => new("Invalid TRRM Import table.");
}
