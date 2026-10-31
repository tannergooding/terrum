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
/// <remarks>A2 TypeDef aliases retain distinct Import rows; callers must use row-scoped binding when one expected
/// DefId has multiple views.</remarks>
public readonly ref struct TrRmImportTableReader
{
    private const int RowSize = 12;
    private readonly ReadOnlySpan<byte> _rows;
    private readonly Dictionary<uint, TrRmImportBinding> _expected;
    private readonly HashSet<uint>? _ambiguousExpected;
    private readonly HashSet<(int ModuleRefRow, string ExportKey, uint ExpectedDefinition)>?
        _importTriples;
    private readonly TrRmImportBinding[]? _bindingsByImportRow;
    private readonly int _recordCount;
    private readonly TrRmA3LayoutResourceLedger? _resourceLedger;

    internal int Count
    {
        get
        {
            _resourceLedger?.ConsumeWork();
            return _expected is null ? 0 : _recordCount;
        }
    }

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
    /// <param name="associatedConstDefs">The validated AssociatedConstDef table, or default when unsupported.</param>
    public TrRmImportTableReader(ReadOnlySpan<byte> section, int recordCount,
        TrRmStringTableReader strings, TrRmModuleRefTableReader references,
        TrRmTypeDefTableReader types, TrRmMethodDefTableReader methods,
        TrRmPropertyDefTableReader properties,
        TrRmInterfaceDefTableReader interfaces = default,
        TrRmAssociatedTypeDefTableReader associatedTypeDefs = default,
        TrRmFieldDefTableReader fields = default,
        TrRmAssociatedConstDefTableReader associatedConstDefs = default)
        : this(section, recordCount, strings, references, types, methods,
            properties, interfaces, associatedTypeDefs, fields, associatedConstDefs,
            default, default, TrRmArtifactSchema.Standard,
            default, default, default,
            resourceLedger: null, profileAware: false)
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
        TrRmArtifactSchema schema,
        TrRmAssociatedConstDefTableReader associatedConstDefs = default,
        TrRmStaticFieldTableReader staticFields = default,
        TrRmStaticFieldContractTableReader staticFieldContracts = default,
        TrRmIntrinsicRegistrationTableReader intrinsicRegistrations = default,
        TrRmA3LayoutResourceLedger? resourceLedger = null)
        : this(section, recordCount, strings, references, types, methods,
            properties, interfaces, associatedTypeDefs, fields, associatedConstDefs,
            features, modules, schema, staticFields, staticFieldContracts,
            intrinsicRegistrations, resourceLedger,
            profileAware: true)
    {
    }

    private TrRmImportTableReader(ReadOnlySpan<byte> section, int recordCount,
        TrRmStringTableReader strings, TrRmModuleRefTableReader references,
        TrRmTypeDefTableReader types, TrRmMethodDefTableReader methods,
        TrRmPropertyDefTableReader properties,
        TrRmInterfaceDefTableReader interfaces,
        TrRmAssociatedTypeDefTableReader associatedTypeDefs,
        TrRmFieldDefTableReader fields,
        TrRmAssociatedConstDefTableReader associatedConstDefs,
        TrRmFeatureTableReader features, TrRmModuleTableReader modules,
        TrRmArtifactSchema schema,
        TrRmStaticFieldTableReader staticFields,
        TrRmStaticFieldContractTableReader staticFieldContracts,
        TrRmIntrinsicRegistrationTableReader intrinsicRegistrations,
        TrRmA3LayoutResourceLedger? resourceLedger,
        bool profileAware)
    {
        resourceLedger?.ConsumeWork(3);
        if (recordCount is < 0 or > 0xFF_FFFF
            || (long)recordCount * RowSize != section.Length)
        {
            throw InvalidImport();
        }

        _rows = section;
        _recordCount = recordCount;
        _resourceLedger = resourceLedger;
        _resourceLedger?.ConsumeWork((ulong)recordCount);
        var allowsTypeDefAliases = profileAware
            && (schema
                == TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyDraft);
        if (allowsTypeDefAliases)
        {
            resourceLedger?.ConsumeWork(
                checked(((ulong)recordCount * 3UL) + 3UL));
            _ambiguousExpected = new HashSet<uint>(recordCount);
            _importTriples = new HashSet<(
                int ModuleRefRow, string ExportKey, uint ExpectedDefinition)>(
                    recordCount);
            _bindingsByImportRow = new TrRmImportBinding[recordCount];
        }
        else
        {
            _ambiguousExpected = null;
            _importTriples = null;
            _bindingsByImportRow = null;
        }
        _expected = new Dictionary<uint, TrRmImportBinding>(recordCount);
        var lastModuleRefRow = 0;
        var lastRequirements = default((int OwnerModule,
            uint TargetRequirement, uint RuntimePolicy));
        for (var row = 0; row < recordCount; row++)
        {
            _resourceLedger?.ConsumeWork();
            var data = section.Slice(row * RowSize, RowSize);
            var moduleRef = Read(data, 0);
            var key = Read(data, 4);
            var expected = Read(data, 8);
            _resourceLedger?.ConsumeWork(3);
            if (moduleRef == 0 || moduleRef > references.Count
                || key is 0 or > 0xFF_FFFF)
            {
                throw InvalidImport();
            }

            var (module, visibility, owner, body) = TrRmDefinitionResolver.Resolve(expected,
                types, fields, methods, properties, interfaces, associatedTypeDefs,
                associatedConstDefs, schema, staticFields, staticFieldContracts,
                intrinsicRegistrations);
            var moduleRefRow = (int)moduleRef;
            if (moduleRefRow != lastModuleRefRow)
            {
                _resourceLedger?.ConsumeWork();
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
            _resourceLedger?.ConsumeWork(2);
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

            _resourceLedger?.ConsumeWork(2);
            var exportKey = strings.DecodeName((int)key);
            var binding = new TrRmImportBinding(
                references.GetProducerIdentity(moduleRefRow), exportKey,
                lastRequirements.TargetRequirement, lastRequirements.RuntimePolicy);
            if (allowsTypeDefAliases)
            {
                if (!_importTriples!.Add((moduleRefRow, exportKey, expected)))
                {
                    throw InvalidImport();
                }

                _bindingsByImportRow![row] = binding;
            }

            if (!_expected.TryAdd(expected, binding))
            {
                if (!allowsTypeDefAliases || expected >> 24 != 0x13)
                {
                    throw InvalidImport();
                }

                _ = _ambiguousExpected!.Add(expected);
            }
        }

        for (var row = 0; row < recordCount; row++)
        {
            _resourceLedger?.ConsumeWork();
            var expected = Read(section.Slice(row * RowSize, RowSize), 8);
            var kind = expected >> 24;
            if (kind == 0x26)
            {
                var registrationRow = checked((int)(expected & 0x00FF_FFFF));
                if (intrinsicRegistrations.GetDeclarationRole(registrationRow) != 2)
                {
                    throw InvalidImport();
                }

                continue;
            }

            if (kind is 0x14 or 0x20 or 0x24 or 0x25)
            {
                var referenceRow = (int)(expected & 0xFF_FFFF);
                if (kind == 0x24
                    && staticFieldContracts.GetDeclarationRole(
                        staticFieldContracts.GetRowForStaticField(referenceRow)) != 2)
                {
                    throw InvalidImport();
                }

                var ownerType = kind switch
                {
                    0x14 => fields.GetOwnerTypeDef(referenceRow),
                    0x20 => interfaces.GetOwnerTypeDef(
                        associatedTypeDefs.GetOwnerInterfaceDef(referenceRow)),
                    0x24 => staticFields.GetOwnerTypeDefRow(referenceRow),
                    0x25 => interfaces.GetOwnerTypeDef(
                        associatedConstDefs.GetOwnerInterfaceDef(referenceRow)),
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
    {
        _resourceLedger?.ConsumeWork();
        return _expected is not null && _expected.ContainsKey(reference);
    }

    public bool TryGetExpectedRequirements(uint reference,
        out (uint TargetRequirement, uint RuntimePolicy) requirements)
    {
        _resourceLedger?.ConsumeWork();
        if (_expected is null
            || (_ambiguousExpected?.Contains(reference) ?? false))
        {
            requirements = default;
            return false;
        }

        _resourceLedger?.ConsumeWork();
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
        _resourceLedger?.ConsumeWork();
        if (_expected is not null
            && !(_ambiguousExpected?.Contains(reference) ?? false)
            && _expected.TryGetValue(reference, out binding))
        {
            return true;
        }

        binding = default;
        return false;
    }

    internal uint GetExpectedDefinitionForImportRow(int row)
    {
        _resourceLedger?.ConsumeWork();
        if (row <= 0 || row > _recordCount)
        {
            throw InvalidImport();
        }

        return Read(_rows.Slice((row - 1) * RowSize, RowSize), 8);
    }

    internal int GetModuleRefForImportRow(int row)
    {
        _resourceLedger?.ConsumeWork();
        if (row <= 0 || row > _recordCount)
        {
            throw InvalidImport();
        }

        return checked((int)Read(_rows.Slice((row - 1) * RowSize, RowSize), 0));
    }

    internal TrRmImportBinding GetBindingForImportRow(int row)
    {
        _resourceLedger?.ConsumeWork();
        if (row <= 0 || row > _recordCount)
        {
            throw InvalidImport();
        }

        if (_bindingsByImportRow is not null)
        {
            return _bindingsByImportRow[row - 1];
        }

        var expected = GetExpectedDefinitionForImportRow(row);
        return _expected is not null && _expected.TryGetValue(expected,
                out var binding)
            ? binding
            : throw InvalidImport();
    }

    private uint Read(ReadOnlySpan<byte> data, int offset)
    {
        _resourceLedger?.ConsumeWork(sizeof(uint) + 1);
        return BinaryPrimitives.ReadUInt32LittleEndian(
            data.Slice(offset, sizeof(uint)));
    }

    private static InvalidDataException InvalidImport()
        => new("Invalid TRRM Import table.");
}
