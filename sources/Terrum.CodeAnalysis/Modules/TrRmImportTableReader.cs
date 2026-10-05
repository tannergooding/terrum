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
    private readonly Dictionary<uint, (uint TargetRequirement, uint RuntimePolicy)> _expected;

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

        _expected = new Dictionary<uint, (uint TargetRequirement, uint RuntimePolicy)>(recordCount);
        var lastModuleRefRow = 0;
        var lastRequirements = default((int OwnerModule,
            uint TargetRequirement, uint RuntimePolicy));
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
            var moduleRefRow = (int)moduleRef;
            if (moduleRefRow != lastModuleRefRow)
            {
                var ownerModule = references.GetOwnerModule(moduleRefRow);
                if (ownerModule != module
                    || (expected >> 24 is 0x16 or 0x1E && body != 0))
                {
                    throw InvalidImport();
                }

                var (targetRequirement, runtimePolicy) =
                    references.GetRequirements(moduleRefRow);
                lastRequirements = (ownerModule, targetRequirement, runtimePolicy);
                lastModuleRefRow = moduleRefRow;
            }
            else if (lastRequirements.OwnerModule != module
                || (expected >> 24 is 0x16 or 0x1E && body != 0))
            {
                throw InvalidImport();
            }

            if (!_expected.TryAdd(expected,
                (lastRequirements.TargetRequirement, lastRequirements.RuntimePolicy)))
            {
                throw InvalidImport();
            }

            _ = strings.DecodeName((int)key);
        }
    }

    public bool IsExpectedDefinition(uint reference)
        => _expected is not null && _expected.ContainsKey(reference);

    public bool TryGetExpectedRequirements(uint reference,
        out (uint TargetRequirement, uint RuntimePolicy) requirements)
    {
        if (_expected is null)
        {
            requirements = default;
            return false;
        }

        return _expected.TryGetValue(reference, out requirements);
    }

    private static uint Read(ReadOnlySpan<byte> data, int offset)
        => BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, sizeof(uint)));

    private static InvalidDataException InvalidImport()
        => new("Invalid TRRM Import table.");
}
