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
    private readonly HashSet<uint> _exportedDefinitions;

    /// <summary>Validates exported local definitions, visibility and key uniqueness.</summary>
    /// <param name="section">The borrowed 8-byte Export rows.</param>
    /// <param name="recordCount">The number of Export rows.</param>
    /// <param name="strings">The validated String table.</param>
    /// <param name="types">The validated TypeDef table.</param>
    /// <param name="methods">The validated MethodDef table.</param>
    /// <param name="properties">The validated PropertyDef table.</param>
    /// <param name="imports">The validated Import table.</param>
    /// <param name="interfaces">The validated InterfaceDef table, or default when unsupported.</param>
    /// <param name="associatedTypeDefs">The validated AssociatedTypeDef table, or default when unsupported.</param>
    /// <param name="fields">The validated FieldDef table, or default when unsupported.</param>
    public TrRmExportTableReader(ReadOnlySpan<byte> section, int recordCount,
        TrRmStringTableReader strings, TrRmTypeDefTableReader types,
        TrRmMethodDefTableReader methods, TrRmPropertyDefTableReader properties,
        TrRmImportTableReader imports, TrRmInterfaceDefTableReader interfaces = default,
        TrRmAssociatedTypeDefTableReader associatedTypeDefs = default,
        TrRmFieldDefTableReader fields = default)
        : this(section, recordCount, strings, types, methods, properties,
            imports, interfaces, associatedTypeDefs, fields, default, default,
            TrRmArtifactSchema.Standard, profileAware: false)
    {
    }

    internal TrRmExportTableReader(ReadOnlySpan<byte> section, int recordCount,
        TrRmStringTableReader strings, TrRmTypeDefTableReader types,
        TrRmMethodDefTableReader methods, TrRmPropertyDefTableReader properties,
        TrRmImportTableReader imports, TrRmInterfaceDefTableReader interfaces,
        TrRmAssociatedTypeDefTableReader associatedTypeDefs,
        TrRmFieldDefTableReader fields,
        TrRmFeatureTableReader features, TrRmModuleTableReader modules,
        TrRmArtifactSchema schema)
        : this(section, recordCount, strings, types, methods, properties,
            imports, interfaces, associatedTypeDefs, fields, features, modules,
            schema, profileAware: true)
    {
    }

    private TrRmExportTableReader(ReadOnlySpan<byte> section, int recordCount,
        TrRmStringTableReader strings, TrRmTypeDefTableReader types,
        TrRmMethodDefTableReader methods, TrRmPropertyDefTableReader properties,
        TrRmImportTableReader imports, TrRmInterfaceDefTableReader interfaces,
        TrRmAssociatedTypeDefTableReader associatedTypeDefs,
        TrRmFieldDefTableReader fields,
        TrRmFeatureTableReader features, TrRmModuleTableReader modules,
        TrRmArtifactSchema schema, bool profileAware)
    {
        if (recordCount is < 0 or > 0xFF_FFFF
            || (long)recordCount * RowSize != section.Length)
        {
            throw InvalidExport();
        }

        var keys = new HashSet<(int Module, string Key)>(recordCount);
        _exportedDefinitions = new HashSet<uint>(recordCount);
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
                definition, types, fields, methods, properties, interfaces,
                associatedTypeDefs);
            var text = strings.DecodeName((int)key);
            var definitionKind = definition >> 24;
            var nonpublicDefinition = visibility != 2
                || (owner != 0 && (types.GetFlags(owner) & 3) != 2);
            var nonpublicContractIdentity = definitionKind is 0x13 or 0x14
                && nonpublicDefinition;
            var allowsContractOnlyIdentity = profileAware
                && schema == TrRmArtifactSchema.CopyOriginDraft
                && nonpublicContractIdentity
                && TrRmFeatureManifestValidator.Requires(features, modules,
                    module, TrRmFeatureTableReader.CopyOriginContractsCandidateFeatureId,
                    schema);
            var missingFieldPathFeature = profileAware
                && schema == TrRmArtifactSchema.CopyOriginDraft
                && definitionKind == 0x14
                && !TrRmFeatureManifestValidator.Requires(features, modules,
                    module, TrRmFeatureTableReader.LayoutQueriesFeatureId, schema);
            if ((nonpublicDefinition && !allowsContractOnlyIdentity)
                || missingFieldPathFeature
                || imports.IsExpectedDefinition(definition)
                || ((definitionKind is 0x14 or 0x20)
                    && imports.IsExpectedDefinition(0x1300_0000u | (uint)owner))
                || !keys.Add((module, text)))
            {
                throw InvalidExport();
            }

            _ = _exportedDefinitions.Add(definition);
        }
    }

    /// <summary>Determines whether a MethodDef has an ordinary Export row.</summary>
    /// <param name="methodDefRow">The one-based MethodDef row.</param>
    /// <returns><see langword="true"/> if the method is exported.</returns>
    public bool HasExportForMethod(int methodDefRow)
        => methodDefRow > 0
            && _exportedDefinitions.Contains(0x1600_0000u | (uint)methodDefRow);

    private static uint Read(ReadOnlySpan<byte> data, int offset)
        => BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, sizeof(uint)));

    private static InvalidDataException InvalidExport()
        => new("Invalid TRRM Export table.");
}
