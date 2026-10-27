// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;

namespace Terrum.CodeAnalysis.Modules;

internal readonly record struct TrRmImportBinding(string ProducerIdentity, string ExportKey,
    uint TargetRequirement, uint RuntimePolicy);

/// <summary>Validates retained dependency references and their local expected definitions.</summary>
/// <remarks>Does not bind the selected producer or compare full declared contracts.</remarks>
public readonly ref struct TrRmImportTableReader
{
    private const int RowSize = 12;
    private readonly ReadOnlySpan<byte> _rows;
    private readonly Dictionary<uint, TrRmImportBinding> _expected;
    private readonly int _recordCount;

    internal int Count => _expected is null ? 0 : _expected.Count;

    /// <summary>Validates retained imports and their local expected definitions.</summary>
    /// <param name="section">The borrowed 12-byte Import rows.</param>
    /// <param name="recordCount">The number of Import rows.</param>
    /// <param name="strings">The validated String table.</param>
    /// <param name="references">The validated ModuleRef table.</param>
    /// <param name="types">The validated TypeDef table.</param>
    /// <param name="methods">The validated MethodDef table.</param>
    /// <param name="properties">The validated PropertyDef table.</param>
    /// <param name="interfaces">The validated InterfaceDef table, or default when unsupported.</param>
    /// <param name="associatedTypeDefs">The validated AssociatedTypeDef table, or default when unsupported.</param>
    /// <param name="fields">The validated FieldDef table, or default when unsupported.</param>
    public TrRmImportTableReader(ReadOnlySpan<byte> section, int recordCount,
        TrRmStringTableReader strings, TrRmModuleRefTableReader references,
        TrRmTypeDefTableReader types, TrRmMethodDefTableReader methods,
        TrRmPropertyDefTableReader properties,
        TrRmInterfaceDefTableReader interfaces = default,
        TrRmAssociatedTypeDefTableReader associatedTypeDefs = default,
        TrRmFieldDefTableReader fields = default)
        : this(section, recordCount, strings, references, types, methods,
            properties, interfaces, associatedTypeDefs, fields, default, default,
            TrRmArtifactSchema.Standard, profileAware: false)
    {
    }

    internal TrRmImportTableReader(ReadOnlySpan<byte> section, int recordCount,
        TrRmStringTableReader strings, TrRmModuleRefTableReader references,
        TrRmTypeDefTableReader types, TrRmMethodDefTableReader methods,
        TrRmPropertyDefTableReader properties,
        TrRmInterfaceDefTableReader interfaces,
        TrRmAssociatedTypeDefTableReader associatedTypeDefs,
        TrRmFieldDefTableReader fields,
        TrRmFeatureTableReader features, TrRmModuleTableReader modules,
        TrRmArtifactSchema schema)
        : this(section, recordCount, strings, references, types, methods,
            properties, interfaces, associatedTypeDefs, fields, features, modules,
            schema, profileAware: true)
    {
    }

    private TrRmImportTableReader(ReadOnlySpan<byte> section, int recordCount,
        TrRmStringTableReader strings, TrRmModuleRefTableReader references,
        TrRmTypeDefTableReader types, TrRmMethodDefTableReader methods,
        TrRmPropertyDefTableReader properties,
        TrRmInterfaceDefTableReader interfaces,
        TrRmAssociatedTypeDefTableReader associatedTypeDefs,
        TrRmFieldDefTableReader fields,
        TrRmFeatureTableReader features, TrRmModuleTableReader modules,
        TrRmArtifactSchema schema, bool profileAware)
    {
        if (recordCount is < 0 or > 0xFF_FFFF
            || (long)recordCount * RowSize != section.Length)
        {
            throw InvalidImport();
        }

        _rows = section;
        _recordCount = recordCount;
        _expected = new Dictionary<uint, TrRmImportBinding>(recordCount);
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

            var (module, visibility, owner, body) = TrRmDefinitionResolver.Resolve(expected,
                types, fields, methods, properties, interfaces, associatedTypeDefs);
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
            if (profileAware && schema == TrRmArtifactSchema.CopyOriginDraft)
            {
                var definitionKind = expected >> 24;
                var contractOnlyIdentity = definitionKind is 0x13 or 0x14
                    && (visibility != 2 || (owner != 0
                        && (types.GetFlags(owner) & 3) != 2));
                if ((definitionKind == 0x14
                    && !TrRmFeatureManifestValidator.Requires(features, modules,
                        module, TrRmFeatureTableReader.LayoutQueriesFeatureId, schema))
                    || (contractOnlyIdentity
                        && !TrRmFeatureManifestValidator.Requires(features, modules,
                            module, TrRmFeatureTableReader.CopyOriginContractsCandidateFeatureId,
                            schema)))
                {
                    throw InvalidImport();
                }
            }

            if (!_expected.TryAdd(expected, new TrRmImportBinding(
                references.GetProducerIdentity(moduleRefRow), strings.DecodeName((int)key),
                lastRequirements.TargetRequirement, lastRequirements.RuntimePolicy)))
            {
                throw InvalidImport();
            }
        }

        for (var row = 0; row < recordCount; row++)
        {
            var expected = Read(section.Slice(row * RowSize, RowSize), 8);
            var kind = expected >> 24;
            if (kind is 0x14 or 0x20)
            {
                var referenceRow = (int)(expected & 0xFF_FFFF);
                var ownerType = kind switch
                {
                    0x14 => fields.GetOwnerTypeDef(referenceRow),
                    0x20 => interfaces.GetOwnerTypeDef(
                        associatedTypeDefs.GetOwnerInterfaceDef(referenceRow)),
                    _ => throw InvalidImport(),
                };
                if (!_expected.ContainsKey(0x1300_0000u | (uint)ownerType))
                {
                    throw InvalidImport();
                }
            }
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

        if (!_expected.TryGetValue(reference, out var binding))
        {
            requirements = default;
            return false;
        }

        requirements = (binding.TargetRequirement, binding.RuntimePolicy);
        return true;
    }

    internal bool TryGetExpectedBinding(uint reference, out TrRmImportBinding binding)
    {
        if (_expected is not null && _expected.TryGetValue(reference, out binding))
        {
            return true;
        }

        binding = default;
        return false;
    }

    internal uint GetExpectedDefinitionForImportRow(int row)
    {
        if (row <= 0 || row > _recordCount)
        {
            throw InvalidImport();
        }

        return Read(_rows.Slice((row - 1) * RowSize, RowSize), 8);
    }

    internal int GetModuleRefForImportRow(int row)
    {
        if (row <= 0 || row > _recordCount)
        {
            throw InvalidImport();
        }

        return checked((int)Read(_rows.Slice((row - 1) * RowSize, RowSize), 0));
    }

    private static uint Read(ReadOnlySpan<byte> data, int offset)
        => BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, sizeof(uint)));

    private static InvalidDataException InvalidImport()
        => new("Invalid TRRM Import table.");
}
