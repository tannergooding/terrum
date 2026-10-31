// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Terrum.CodeAnalysis.Semantics;

namespace Terrum.CodeAnalysis.Modules;

public static partial class TrRmExecutableCompiler
{
    private static readonly UTF8Encoding s_sourceTypeExportKeyEncoding
        = new(false, true);

    private sealed record A2SourceTypeImportPlan(
        List<TrRmModuleRefRow> ModuleRefs,
        List<TrRmImportRow> Imports,
        Dictionary<int, int> FirstImportRowByStructure);

    private static byte[] EmitForeignInvocationStorageFixedArrayCopyDeclarations(
        BoundProgram program, string moduleIdentity,
        IReadOnlyList<TrRmSourceTypeExport>? sourceTypeExports = null)
    {
        const TrRmArtifactSchema schema
            = TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyDraft;
        if (program.EntryPoint != -1 || !program.Functions.IsEmpty
            || program.Structures.IsEmpty             || !program.Classes.IsEmpty
            || !program.Buffers.IsEmpty
            || !program.NativeFunctionPointerSignatures.IsEmpty
            || !program.NativePointerTypes.IsEmpty
            || !program.CheckedReferenceTypes.IsEmpty
            || !program.Interfaces.IsEmpty
            || !program.EnumDefinitions.IsEmpty
            || !program.GenericNominalTypeDefinitions.IsEmpty
            || !program.GenericNominalTypeInstances.IsEmpty
            || program.CoreTaskType is not null
            || program.NativeCoreRegistration is not null
            || program.NativeLoweringAgreement is not null
            || program.RequiresSourceGrant
            || program.InvocationProfileId is not null
            || program.IsInvocationSourceBound
            || program.RuntimePolicy != BoundRuntimePolicy.FailFast)
        {
            throw Unsupported(
                "A2 Copy declaration emission requires a fail-fast library of supported struct declarations.");
        }

        ValidateA2SourceTypeDeclarationMap(program);
        var typeDefCount = program.Structures.Length;
        var structureTypeDefRows = new Dictionary<int, int>(typeDefCount);
        for (var index = 0; index < typeDefCount; index++)
        {
            structureTypeDefRows.Add(index, index + 1);
        }

        foreach (var structure in program.Structures)
        {
            if (structure.BlittableTargetRequirement
                    != FixedLayoutStorage.WindowsX64TargetRequirement
                || structure.IsSequential == structure.IsTransparent
                || structure.CleanupMethod is not null
                || structure.Fields.IsDefaultOrEmpty)
            {
                throw Unsupported(
                    "A2 Copy declaration emission supports Blittable Sequential/Transparent structs without Cleanup.");
            }

            foreach (var field in structure.Fields)
            {
                var structureIndex = (int)field.Type - 100;
                var isStructure = !ManagedClassTypes.IsClass(field.Type)
                    && !BoundEnumTypes.IsType(field.Type)
                    && structureIndex >= 0
                    && structureIndex < typeDefCount;
                var isArray = NativeFixedArrayTypes.IsType(field.Type);
                var isValidArray = isArray
                    && (uint)NativeFixedArrayTypes.Index(field.Type)
                        < (uint)program.NativeFixedArrayTypes.Length;
                if (!DirectNativeImportContract.IsScalar(field.Type)
                    && !isStructure && !isValidArray)
                {
                    throw Unsupported(
                        "A2 Copy declaration emission supports scalar, local struct, and fixed-array fields.");
                }
            }
        }

        _ = FixedLayoutStorage.Validate(program.Structures,
            program.NativeFixedArrayTypes);
        var hasFloatingTypes = program.Structures.Any(static structure
            => structure.Fields.Any(static field => IsFloat(field.Type)));
        var typeRows = (hasFloatingTypes ? s_typeRows : s_nonFloatTypeRows)
            .ToList();
        var typeDefTypeSpecRows = new Dictionary<int, int>();
        var nativeFunctionPointerTypeRows
            = new Dictionary<PrimitiveType, int>();
        var nativePointerTypeRows = new Dictionary<PrimitiveType, int>();
        var arrayTypeSpecRows
            = new Dictionary<int, uint>(program.NativeFixedArrayTypes.Length);
        var arrayTypeStates = new byte[program.NativeFixedArrayTypes.Length];
        var fixedArrayLayoutRows = new List<TrRmFixedArrayLayoutRow>(
            program.NativeFixedArrayTypes.Length);
        var structureNamespaceRows = new int[typeDefCount];
        var structureNameRows = new int[typeDefCount];
        var structureFieldNameRows = new int[typeDefCount][];
        var fieldFirstByStructure = new int[typeDefCount];
        var layoutFirstByStructure = new int[typeDefCount];
        var fieldRows = new List<TrRmFieldDefRow>();
        var layoutRows = new List<TrRmLayoutRow>(typeDefCount * 2);
        var layoutFieldRows = new List<TrRmLayoutFieldRow>();
        var stringRows = new List<(string Text, int Encoding)> {
            ("", 0), (moduleIdentity, 0),
        };
        var stringRowsByText = new Dictionary<string, int>(
            StringComparer.Ordinal) {
            [""] = 1,
            [moduleIdentity] = 2,
        };

        int GetStringRow(string text)
        {
            if (!stringRowsByText.TryGetValue(text, out var row))
            {
                row = stringRows.Count + 1;
                stringRows.Add((text, 0));
                stringRowsByText.Add(text, row);
            }

            return row;
        }

        var exportRows = CreateA2SourceTypeExportRows(program,
            sourceTypeExports, structureTypeDefRows, GetStringRow);
        var typeImportPlan = CreateA2SourceTypeImportPlan(program,
            GetStringRow);
        var importedTypeSpecRows = new Dictionary<int, int>();

        uint GetFieldTypeSpecRef(PrimitiveType type)
        {
            if (NativeFixedArrayTypes.IsType(type))
            {
                return GetArrayTypeSpecRow(NativeFixedArrayTypes.Index(type));
            }

            var structureIndex = (int)type - 100;
            if (structureIndex >= 0
                && structureIndex < typeDefCount
                && typeImportPlan.FirstImportRowByStructure.TryGetValue(
                    structureIndex, out var importRow))
            {
                if (!importedTypeSpecRows.TryGetValue(structureIndex,
                        out var importedRow))
                {
                    importedRow = typeRows.Count + 1;
                    typeRows.Add(TrRmTypeSpecTableWriter.Reference(0x03,
                        checked((uint)importRow)));
                    importedTypeSpecRows.Add(structureIndex, importedRow);
                }

                return checked((uint)importedRow);
            }

            return checked((uint)NativeBlittableFieldTypeRow(typeRows,
                typeDefTypeSpecRows, structureTypeDefRows, type, 0,
                nativeFunctionPointerTypeRows, nativePointerTypeRows, []));
        }

        uint GetArrayTypeSpecRow(int arrayIndex)
        {
            if ((uint)arrayIndex >= (uint)program.NativeFixedArrayTypes.Length)
            {
                throw Unsupported("A fixed-array type is outside the checked program table.");
            }
            if (arrayTypeSpecRows.TryGetValue(arrayIndex,
                    out var existingRow))
            {
                return existingRow;
            }
            if (arrayTypeStates[arrayIndex] != 0)
            {
                throw Unsupported("A fixed-array type identity is recursive.");
            }

            arrayTypeStates[arrayIndex] = 1;
            var definition = program.NativeFixedArrayTypes[arrayIndex];
            var elementTypeSpecRef
                = GetFieldTypeSpecRef(definition.ElementType);
            var row = checked((uint)typeRows.Count + 1);
            typeRows.Add(TrRmTypeSpecTableWriter.FixedInlineArray(
                elementTypeSpecRef, definition.ElementCount));
            arrayTypeSpecRows.Add(arrayIndex, row);
            arrayTypeStates[arrayIndex] = 2;
            fixedArrayLayoutRows.Add(new TrRmFixedArrayLayoutRow(1,
                checked((int)row), 1, definition.ElementStride,
                definition.ByteExtent, definition.Alignment));
            return row;
        }

        var hasCopyEvidence = false;
        var typeDefRows = new TrRmTypeDefRow[typeDefCount];
        for (var index = 0; index < typeDefCount; index++)
        {
            var structure = program.Structures[index];
            var separator = structure.Name.LastIndexOf('.',
                StringComparison.Ordinal);
            var typeNamespace = separator < 0 ? "" : structure.Name[..separator];
            var typeName = separator < 0
                ? structure.Name : structure.Name[(separator + 1)..];
            if (typeName.Length == 0)
            {
                throw Unsupported(
                    $"A2 Copy declaration identity '{structure.Name}' is invalid.");
            }

            structureNamespaceRows[index] = GetStringRow(typeNamespace);
            structureNameRows[index] = GetStringRow(typeName);
            structureFieldNameRows[index] = new int[structure.Fields.Length];
            fieldFirstByStructure[index] = fieldRows.Count + 1;
            for (var field = 0; field < structure.Fields.Length; field++)
            {
                var fieldDefinition = structure.Fields[field];
                var fieldTypeSpecRef = GetFieldTypeSpecRef(
                    fieldDefinition.Type);
                var visibility = fieldDefinition.IsPrivate ? 0u
                    : fieldDefinition.IsInternal ? 1u : 2u;
                var fieldFlags = visibility
                    | (fieldDefinition.IsMutable ? 4u : 0u);
                structureFieldNameRows[index][field]
                    = GetStringRow(fieldDefinition.Name);
                fieldRows.Add(new TrRmFieldDefRow(index + 1,
                    structureFieldNameRows[index][field],
                    fieldTypeSpecRef, fieldFlags));
            }

            layoutFirstByStructure[index] = layoutRows.Count + 1;
            var layoutKind = structure.IsSequential ? (byte)1 : (byte)3;
            var pack = structure.IsSequential ? 16u : 0u;
            for (var variant = 0; variant < 2; variant++)
            {
                var layoutRow = layoutRows.Count + 1;
                var layoutFieldFirst = layoutFieldRows.Count + 1;
                layoutRows.Add(new TrRmLayoutRow(index + 1,
                    variant == 0 ? 0u : 1u, layoutKind,
                    variant == 0 ? (byte)0 : (byte)1,
                    0, pack, layoutFieldFirst, structure.Fields.Length));
                for (var field = 0; field < structure.Fields.Length; field++)
                {
                    layoutFieldRows.Add(new TrRmLayoutFieldRow(layoutRow,
                        field, fieldFirstByStructure[index] + field));
                }
            }

            var visibilityFlags = structure.IsPrivate ? 0u
                : structure.IsInternal ? 1u : 2u;
            var declaredCopy = !structure.IsUnique;
            if (declaredCopy)
            {
                hasCopyEvidence = true;
            }
            typeDefRows[index] = new TrRmTypeDefRow(
                structureNamespaceRows[index], structureNameRows[index],
                visibilityFlags | 4u
                    | (declaredCopy
                        ? TrRmTypeDefTableReader.DeclaredCopyFlag : 0u),
                fieldFirstByStructure[index], structure.Fields.Length,
                0, 0, layoutFirstByStructure[index], 2, 0, 0);
        }

        for (var arrayIndex = 0;
            arrayIndex < program.NativeFixedArrayTypes.Length; arrayIndex++)
        {
            _ = GetArrayTypeSpecRow(arrayIndex);
        }

        var featureRows = new List<TrRmFeatureRow>();
        if (hasCopyEvidence)
        {
            featureRows.Add(TrRmFeatureRow.RootlessNativeValues);
        }
        if (fixedArrayLayoutRows.Count != 0)
        {
            featureRows.Add(TrRmFeatureRow.FixedInlineArrayStorageCandidate);
            if (!hasCopyEvidence)
            {
                featureRows.Add(TrRmFeatureRow.RootlessNativeValues);
            }
        }
        var featureBytes = featureRows.Count == 0
            ? [] : TrRmFeatureTableWriter.Encode(featureRows, schema);
        var features = new TrRmFeatureTableReader(featureBytes,
            featureRows.Count, schema);
        var stringBytes = TrRmStringTableWriter.EncodeTextRows(stringRows);
        var strings = new TrRmStringTableReader(stringBytes,
            stringRows.Count);
        var moduleBytes = TrRmModuleTableWriter.Encode([
            new TrRmModuleRow(2,
                TrRmModuleTableReader.WindowsX64NativeTargetRequirement,
                0, 0, 1, 1, 1, 1,
                FeatureFirst: featureRows.Count == 0 ? 0 : 1,
                FeatureCount: featureRows.Count),
        ], strings, typeImportPlan.ModuleRefs.Count, typeDefCount, 0, 0,
            featureRows.Count);
        var modules = new TrRmModuleTableReader(moduleBytes, 1, strings,
            typeImportPlan.ModuleRefs.Count, typeDefCount, 0, 0,
            featureRows.Count);
        var moduleRefBytes = typeImportPlan.ModuleRefs.Count == 0 ? []
            : TrRmModuleRefTableWriter.Encode(typeImportPlan.ModuleRefs,
                strings, modules);
        var moduleRefs = new TrRmModuleRefTableReader(moduleRefBytes,
            typeImportPlan.ModuleRefs.Count, strings, modules);
        var typeSpecBytes = TrRmTypeSpecTableWriter.Encode(typeRows,
            typeDefCount, typeImportPlan.Imports.Count, 0, 0, schema);
        var typeSpecs = new TrRmTypeSpecTableReader(typeSpecBytes,
            typeRows.Count, typeDefCount, typeImportPlan.Imports.Count, 0, 0,
            schema);
        var typeDefBytes = TrRmTypeDefTableWriter.Encode(typeDefRows,
            strings, modules, fieldRows.Count, 0, layoutRows.Count, 0,
            schema);
        var typeDefs = new TrRmTypeDefTableReader(typeDefBytes, typeDefCount,
            strings, modules, fieldRows.Count, 0, layoutRows.Count, 0,
            schema);
        var fieldBytes = TrRmFieldDefTableWriter.Encode(fieldRows, strings,
            typeDefs, typeSpecs);
        var fields = new TrRmFieldDefTableReader(fieldBytes, fieldRows.Count,
            strings, typeDefs, typeSpecs);
        var importBytes = typeImportPlan.Imports.Count == 0 ? []
            : TrRmImportTableWriter.Encode(typeImportPlan.Imports,
                strings, moduleRefs, typeDefs, default, default, default,
                default, fields, features, modules, schema);
        var imports = new TrRmImportTableReader(importBytes,
            typeImportPlan.Imports.Count, strings, moduleRefs, typeDefs,
            default, default, default, default, fields, features, modules,
            schema);
        var targetProfileBytes = TrRmTargetProfileTableWriter.Encode([
            new TrRmTargetProfileRow(
                TrRmTargetProfileTableReader.WindowsX64NativeProfileId,
                TrRmTargetProfileTableReader
                    .WindowsX64NativeProfileRuleRevision),
        ], schema);
        var targetProfiles = new TrRmTargetProfileTableReader(
            targetProfileBytes, 1, schema);
        var layoutBytes = TrRmLayoutTableWriter.Encode(layoutRows, typeDefs,
            targetProfiles, layoutFieldRows.Count);
        var layouts = new TrRmLayoutTableReader(layoutBytes, layoutRows.Count,
            typeDefs, targetProfiles, layoutFieldRows.Count);
        var layoutFieldBytes = TrRmLayoutFieldTableWriter.Encode(
            layoutFieldRows, layouts, typeDefs, fields);
        var exportBytes = exportRows.Count == 0 ? []
            : TrRmExportTableWriter.Encode(exportRows, strings, typeDefs,
                default, default, imports, default, default, default,
                features, modules, schema);
        var sections = new List<TrRmSection>
        {
            new(0x0001, 0, 1, moduleBytes),
            new(0x0002, 0, (uint)stringRows.Count, stringBytes),
            new(0x0011, 0, (uint)typeRows.Count, typeSpecBytes),
            new(0x0013, 0, (uint)typeDefCount, typeDefBytes),
            new(0x0014, 0, (uint)fieldRows.Count, fieldBytes),
            new(0x0015, 0, (uint)layoutRows.Count, layoutBytes),
            new(0x0023, 0, (uint)layoutFieldRows.Count,
                layoutFieldBytes),
            new(0x0048, 0, 1, targetProfileBytes),
        };
        if (exportRows.Count != 0)
        {
            sections.Add(new TrRmSection(0x001D, 0,
                (uint)exportRows.Count, exportBytes));
        }
        if (typeImportPlan.ModuleRefs.Count != 0)
        {
            sections.Add(new TrRmSection(0x0010, 0,
                (uint)typeImportPlan.ModuleRefs.Count, moduleRefBytes));
        }
        if (typeImportPlan.Imports.Count != 0)
        {
            sections.Add(new TrRmSection(0x001C, 0,
                (uint)typeImportPlan.Imports.Count, importBytes));
        }
        if (fixedArrayLayoutRows.Count != 0)
        {
            var fixedArrayLayoutBytes
                = TrRmFixedArrayLayoutTableWriter.Encode(
                    fixedArrayLayoutRows, typeSpecs, targetProfiles, features,
                    modules, schema);
            sections.Add(new TrRmSection(0x0012, 0,
                (uint)fixedArrayLayoutRows.Count, fixedArrayLayoutBytes));
        }
        if (featureRows.Count != 0)
        {
            sections.Add(new TrRmSection(0x0004, 0,
                (uint)featureRows.Count, featureBytes));
        }

        sections.Sort(static (left, right)
            => left.Kind.CompareTo(right.Kind));
        var artifact = TrRmArtifactWriter.Encode(sections, schema);
        _ = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationStorageFixedArrayCopyDraftAndGetExecutionTables(
                TrRmArtifactIndex
                    .ForForeignInvocationStorageFixedArrayCopyDraft(artifact));
        return artifact;
    }

    private static List<TrRmExportRow> CreateA2SourceTypeExportRows(
        BoundProgram program,
        IReadOnlyList<TrRmSourceTypeExport>? typeExports,
        Dictionary<int, int> structureTypeDefRows,
        Func<string, int> getStringRow)
    {
        var rows = new List<TrRmExportRow>(
            typeExports?.Count ?? 0);
        if (typeExports is null || typeExports.Count == 0)
        {
            return rows;
        }

        var sourceDeclarations = program.SourceTypeDeclarations;
        if (sourceDeclarations.IsDefault)
        {
            throw Unsupported(
                "Explicit A2 TypeDef exports require a source-bound local declaration set.");
        }

        var declarationByName
            = new Dictionary<(string Namespace, string Name),
                BoundSourceTypeDeclaration>(sourceDeclarations.Length);
        var structureIndexes = new bool[program.Structures.Length];
        foreach (var declaration in sourceDeclarations)
        {
            if ((uint)declaration.StructureIndex
                    >= (uint)program.Structures.Length
                || structureIndexes[declaration.StructureIndex])
            {
                throw new ArgumentException(
                    "The source TypeDef declaration map is inconsistent.",
                    nameof(program));
            }

            var structure = program.Structures[declaration.StructureIndex];
            var qualifiedName = declaration.Namespace.Length == 0
                ? declaration.Name
                : $"{declaration.Namespace}.{declaration.Name}";
            if (!StringComparer.Ordinal.Equals(structure.Name, qualifiedName)
                || declaration.DeclarationSnapshot != structure
                || structure.StandardTypeOrigin is not null
                || !declarationByName.TryAdd(
                    (declaration.Namespace, declaration.Name), declaration))
            {
                throw new ArgumentException(
                    "The source TypeDef declaration map no longer matches the checked program.",
                    nameof(program));
            }

            structureIndexes[declaration.StructureIndex] = true;
        }

        var selectedStructures = new HashSet<int>();
        var exportKeys = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < typeExports.Count; index++)
        {
            var export = typeExports[index];
            if (export is null || export.Namespace is null
                || string.IsNullOrEmpty(export.Name)
                || string.IsNullOrEmpty(export.ExportKey)
                || export.Namespace.Contains('\0',
                    StringComparison.Ordinal)
                || export.Name.Contains('\0', StringComparison.Ordinal)
                || export.ExportKey.Contains('\0', StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "A source TypeDef export mapping contains invalid text.",
                    nameof(typeExports));
            }

            try
            {
                _ = s_sourceTypeExportKeyEncoding.GetByteCount(
                    export.ExportKey);
            }
            catch (EncoderFallbackException exception)
            {
                throw new ArgumentException(
                    "A source TypeDef ExportKey must be well-formed scalar text.",
                    nameof(typeExports), exception);
            }

            if (!declarationByName.TryGetValue(
                    (export.Namespace, export.Name),
                    out var declaration))
            {
                throw new ArgumentException(
                    "A source TypeDef export mapping does not select one source declaration.",
                    nameof(typeExports));
            }

            var structure = program.Structures[declaration.StructureIndex];
            if (structure.IsPrivate || structure.IsInternal
                || structure.IsUnique || structure.CleanupMethod is not null)
            {
                throw new ArgumentException(
                    "A source TypeDef export must select a public ordinary Copy struct.",
                    nameof(typeExports));
            }
            if (!selectedStructures.Add(declaration.StructureIndex)
                || !exportKeys.Add(export.ExportKey))
            {
                throw new ArgumentException(
                    "Source TypeDef declarations and ExportKeys must be unique.",
                    nameof(typeExports));
            }

            var typeDefRow = structureTypeDefRows[
                declaration.StructureIndex];
            rows.Add(new TrRmExportRow(
                0x1300_0000u | checked((uint)typeDefRow),
                getStringRow(export.ExportKey)));
        }

        var pendingStructures = new Stack<int>(selectedStructures);
        var checkedStructures = new HashSet<int>();
        while (pendingStructures.TryPop(out var structureIndex))
        {
            if (!checkedStructures.Add(structureIndex))
            {
                continue;
            }

            foreach (var field in program.Structures[structureIndex].Fields)
            {
                AddNominalDependencies(field.Type);
            }
        }

        return rows;

        void AddNominalDependencies(PrimitiveType type)
        {
            if (NativeFixedArrayTypes.IsType(type))
            {
                var arrayIndex = NativeFixedArrayTypes.Index(type);
                if ((uint)arrayIndex
                    >= (uint)program.NativeFixedArrayTypes.Length)
                {
                    throw Unsupported(
                        "A source TypeDef export has an invalid fixed-array closure.");
                }

                AddNominalDependencies(
                    program.NativeFixedArrayTypes[arrayIndex].ElementType);
                return;
            }

            var structureIndex = (int)type - 100;
            if (ManagedClassTypes.IsClass(type)
                || BoundEnumTypes.IsType(type)
                || structureIndex < 0
                || structureIndex >= program.Structures.Length)
            {
                return;
            }

            if (program.Structures[structureIndex].StandardTypeOrigin
                is not null)
            {
                return;
            }

            if (!selectedStructures.Contains(structureIndex))
            {
                throw Unsupported(
                    "Every local nominal type in an exported A2 storage closure requires an explicit ExportKey.");
            }

            var structure = program.Structures[structureIndex];
            if (structure.IsPrivate || structure.IsInternal
                || structure.IsUnique || structure.CleanupMethod is not null)
            {
                throw Unsupported(
                    "An exported A2 storage closure contains an ineligible nominal type.");
            }

            if (!checkedStructures.Contains(structureIndex))
            {
                pendingStructures.Push(structureIndex);
            }
        }
    }

    private static void ValidateA2SourceTypeDeclarationMap(
        BoundProgram program)
    {
        var sourceDeclarations = program.SourceTypeDeclarations;
        if (sourceDeclarations.IsDefault)
        {
            return;
        }

        var sourceIndexes = new bool[program.Structures.Length];
        foreach (var declaration in sourceDeclarations)
        {
            if ((uint)declaration.StructureIndex
                    >= (uint)program.Structures.Length
                || sourceIndexes[declaration.StructureIndex])
            {
                throw new ArgumentException(
                    "The source TypeDef declaration map is inconsistent.",
                    nameof(program));
            }

            var structure = program.Structures[declaration.StructureIndex];
            var qualifiedName = declaration.Namespace.Length == 0
                ? declaration.Name
                : $"{declaration.Namespace}.{declaration.Name}";
            if (!StringComparer.Ordinal.Equals(structure.Name,
                    qualifiedName)
                || declaration.DeclarationSnapshot != structure
                || structure.StandardTypeOrigin is not null)
            {
                throw new ArgumentException(
                    "The source TypeDef declaration map no longer matches the checked program.",
                    nameof(program));
            }

            sourceIndexes[declaration.StructureIndex] = true;
        }

        for (var index = 0; index < program.Structures.Length; index++)
        {
            if (!sourceIndexes[index]
                && program.Structures[index].StandardTypeOrigin is null)
            {
                throw Unsupported(
                    "A2 source emission cannot preserve a referenced type without its Standard TypeDef origin.");
            }
        }
    }

    private static A2SourceTypeImportPlan CreateA2SourceTypeImportPlan(
        BoundProgram program, Func<string, int> getStringRow)
    {
        var moduleRefs = new List<TrRmModuleRefRow>();
        var imports = new List<TrRmImportRow>();
        var firstImportRowByStructure = new Dictionary<int, int>();
        var moduleRefsByIdentity = new Dictionary<string,
            (uint TargetRequirement, uint RuntimePolicy, int Row)>(
            StringComparer.Ordinal);
        var providerSnapshotsByIdentity = new Dictionary<string,
            BoundSourceTypeProviderSnapshot>(StringComparer.Ordinal);
        var structuresByActualDefinition = new Dictionary<
            (string ModuleIdentity, uint DefinitionId), List<int>>();
        var validatedReferenceRoots
            = new HashSet<BoundSourceTypeProviderSnapshot>();
        var rootAliasKeysByDefinition = new Dictionary<(
            BoundSourceTypeProviderSnapshot ReferenceRoot,
            string ModuleIdentity, uint DefinitionId),
            List<(string ExportKey, uint TargetRequirement,
                uint RuntimePolicy)>>();
        var rootAliasViewsByDefinition = new Dictionary<(
            BoundSourceTypeProviderSnapshot ReferenceRoot,
            string ModuleIdentity, uint DefinitionId), List<int>>();
        var writtenExpectations = new HashSet<(
            BoundSourceTypeProviderSnapshot UsingModule, int ImportRow,
            uint ExpectedDefinitionId)>();
        var importTriples = new HashSet<(
            int ModuleRefRow, string ExportKey, uint ExpectedDefinition)>();

        for (var structureIndex = 0;
            structureIndex < program.Structures.Length; structureIndex++)
        {
            var origin = program.Structures[structureIndex].StandardTypeOrigin;
            if (origin is null)
            {
                continue;
            }

            var provider = origin.Provider;
            var expectedDeclaration = origin.ExpectedDeclaration
                ?? throw Unsupported(
                    "An A2 source type origin does not retain its checked declaration snapshot.");
            if (!SameA2SourceTypeDeclaration(
                    program.Structures[structureIndex],
                    expectedDeclaration))
            {
                throw Unsupported(
                    "An A2 source type declaration changed after its provider expectation was checked.");
            }
            var referenceRoot = origin.ReferenceRoot
                ?? throw Unsupported(
                    "An A2 source type origin does not retain its reference root.");
            if (!StringComparer.Ordinal.Equals(provider.SchemaIdentity,
                    StandardForeignInvocationStorageFixedArraySchemaIdentity)
                || !StringComparer.Ordinal.Equals(referenceRoot.SchemaIdentity,
                    StandardForeignInvocationStorageFixedArraySchemaIdentity)
                || string.IsNullOrEmpty(provider.ModuleIdentity)
                || origin.DefinitionId >> 24 != 0x13
                || origin.ExportKeys.IsDefaultOrEmpty)
            {
                throw Unsupported(
                    "An A2 source type origin lacks a complete selected provider identity.");
            }
            if (validatedReferenceRoots.Add(referenceRoot)
                && (origin.ExpectedFixedArrayTypes.IsDefault
                    || origin.ExpectedNativePointerTypes.IsDefault
                    || program.NativeFixedArrayTypes.Length
                        < origin.ExpectedFixedArrayTypes.Length
                    || !program.NativeFixedArrayTypes.AsSpan()
                        .Slice(0, origin.ExpectedFixedArrayTypes.Length)
                        .SequenceEqual(
                            origin.ExpectedFixedArrayTypes.AsSpan())
                    || program.NativePointerTypes.Length
                        < origin.ExpectedNativePointerTypes.Length
                    || !program.NativePointerTypes.AsSpan()
                        .Slice(0,
                            origin.ExpectedNativePointerTypes.Length)
                        .SequenceEqual(
                            origin.ExpectedNativePointerTypes.AsSpan())))
            {
                throw Unsupported(
                    "An A2 source type array or pointer table changed after its provider expectation was checked.");
            }

            if (providerSnapshotsByIdentity.TryGetValue(
                    provider.ModuleIdentity, out var existingSnapshot))
            {
                if (!SameA2SourceTypeProviderSnapshot(existingSnapshot,
                        provider))
                {
                    throw Unsupported(
                        "A2 source type origins select conflicting snapshots for one provider identity.");
                }
            }
            else
            {
                providerSnapshotsByIdentity.Add(provider.ModuleIdentity,
                    provider);
            }
            var actualDefinitionKey = (provider.ModuleIdentity,
                origin.DefinitionId);
            if (!structuresByActualDefinition.TryGetValue(
                    actualDefinitionKey, out var actualStructures))
            {
                actualStructures = [];
                structuresByActualDefinition.Add(actualDefinitionKey,
                    actualStructures);
            }
            actualStructures.Add(structureIndex);

            var expectedDefinition = 0x1300_0000u
                | checked((uint)(structureIndex + 1));
            var rootAliasKey = (referenceRoot, provider.ModuleIdentity,
                origin.DefinitionId);
            if (!rootAliasKeysByDefinition.TryGetValue(rootAliasKey,
                    out var rootAliasKeys))
            {
                rootAliasKeys = [];
                rootAliasKeysByDefinition.Add(rootAliasKey, rootAliasKeys);
            }
            if (!rootAliasViewsByDefinition.TryGetValue(rootAliasKey,
                    out var rootAliasViews))
            {
                rootAliasViews = [];
                rootAliasViewsByDefinition.Add(rootAliasKey, rootAliasViews);
            }
            rootAliasViews.Add(structureIndex);

            if (origin.IsLookupRoot)
            {
                if (!ReferenceEquals(provider, referenceRoot))
                {
                    throw Unsupported(
                        "An A2 lookup root does not belong to its retained reference Module.");
                }

                foreach (var exportKey in origin.ExportKeys)
                {
                    AddRootAliasKey(rootAliasKey, exportKey,
                        provider.TargetRequirement, provider.RuntimePolicy);
                    AddImport(provider.ModuleIdentity, exportKey,
                        provider.TargetRequirement, provider.RuntimePolicy,
                        expectedDefinition, structureIndex);
                }
            }
            else if (ReferenceEquals(provider, referenceRoot)
                && origin.Expectations.IsDefaultOrEmpty)
            {
                foreach (var exportKey in origin.ExportKeys)
                {
                    AddRootAliasKey(rootAliasKey, exportKey,
                        provider.TargetRequirement, provider.RuntimePolicy);
                    AddImport(provider.ModuleIdentity, exportKey,
                        provider.TargetRequirement, provider.RuntimePolicy,
                        expectedDefinition, structureIndex);
                }
            }
            else
            {
                if (origin.Expectations.IsDefaultOrEmpty)
                {
                    throw Unsupported(
                        "An imported A2 source type has no retained Import expectations.");
                }

                var rootExpectations = origin.Expectations.Where(expectation
                    => ReferenceEquals(expectation.UsingModule,
                        referenceRoot)).ToArray();
                if (rootExpectations.Length == 0
                    || rootExpectations.Any(expectation =>
                        (expectation.ExpectedDefinitionId >> 24) != 0x13
                        || expectation.ExpectedDefinitionId
                            != rootExpectations[0].ExpectedDefinitionId
                        || !StringComparer.Ordinal.Equals(
                            expectation.ProducerIdentity,
                            provider.ModuleIdentity)
                        || expectation.TargetRequirement
                            != provider.TargetRequirement
                        || expectation.RuntimePolicy != provider.RuntimePolicy))
                {
                    throw Unsupported(
                        "An imported A2 source type lacks a complete expectation from its retained reference Module.");
                }

                var rootKeys = rootExpectations.Select(
                        static expectation => expectation.ExportKey)
                    .Distinct(StringComparer.Ordinal);
                if (!origin.ExportKeys.SequenceEqual(rootKeys,
                        StringComparer.Ordinal))
                {
                    throw Unsupported(
                        "An imported A2 source type's retained alias keys do not match its reference Module expectations.");
                }

                foreach (var expectation in rootExpectations)
                {
                    AddRootAliasKey(rootAliasKey, expectation.ExportKey,
                        expectation.TargetRequirement,
                        expectation.RuntimePolicy);
                    AddImport(expectation.ProducerIdentity,
                        expectation.ExportKey,
                        expectation.TargetRequirement,
                        expectation.RuntimePolicy, expectedDefinition,
                        structureIndex);
                }
            }
        }

        foreach (var (aliasClass, structures) in rootAliasViewsByDefinition)
        {
            foreach (var structureIndex in structures)
            {
                var expectedDefinition = 0x1300_0000u
                    | checked((uint)(structureIndex + 1));
                foreach (var (exportKey, targetRequirement, runtimePolicy)
                    in rootAliasKeysByDefinition[aliasClass])
                {
                    AddImport(aliasClass.ModuleIdentity, exportKey,
                        targetRequirement, runtimePolicy,
                        expectedDefinition, structureIndex);
                }
            }
        }

        foreach (var ownerStructure in program.Structures)
        {
            var origin = ownerStructure.StandardTypeOrigin;
            if (origin is null || origin.Expectations.IsDefaultOrEmpty)
            {
                continue;
            }

            var referenceRoot = origin.ReferenceRoot
                ?? throw Unsupported(
                    "An A2 source type origin does not retain its reference root.");
            foreach (var expectation in origin.Expectations)
            {
                if (ReferenceEquals(expectation.UsingModule, referenceRoot)
                    || !writtenExpectations.Add((
                        expectation.UsingModule, expectation.ImportRow,
                        expectation.ExpectedDefinitionId)))
                {
                    continue;
                }

                if ((expectation.ProviderDefinitionId >> 24) != 0x13
                    || !StringComparer.Ordinal.Equals(
                        expectation.ProducerIdentity,
                        expectation.Provider.ModuleIdentity)
                    || expectation.TargetRequirement
                        != expectation.Provider.TargetRequirement
                    || expectation.RuntimePolicy
                        != expectation.Provider.RuntimePolicy)
                {
                    throw Unsupported(
                        "A provider-owned A2 expectation does not retain its actual TypeDef origin.");
                }

                var targetDefinitionKey
                    = (expectation.Provider.ModuleIdentity,
                        expectation.ProviderDefinitionId);
                if (!structuresByActualDefinition.TryGetValue(
                        targetDefinitionKey, out var targetStructures))
                {
                    continue;
                }

                foreach (var structureIndex in targetStructures)
                {
                    var targetOrigin
                        = program.Structures[structureIndex]
                            .StandardTypeOrigin;
                    if (targetOrigin is not null
                        && SameA2SourceTypeProviderSnapshot(
                            targetOrigin.Provider, expectation.Provider))
                    {
                        AddImport(expectation.ProducerIdentity,
                            expectation.ExportKey,
                            expectation.TargetRequirement,
                            expectation.RuntimePolicy,
                            0x1300_0000u
                                | checked((uint)(structureIndex + 1)),
                            structureIndex);
                    }
                }
            }
        }

        return new A2SourceTypeImportPlan(moduleRefs, imports,
            firstImportRowByStructure);

        void AddRootAliasKey(
            (BoundSourceTypeProviderSnapshot ReferenceRoot,
                string ModuleIdentity, uint DefinitionId) aliasClass,
            string exportKey, uint targetRequirement, uint runtimePolicy)
        {
            var alias = (ExportKey: exportKey,
                TargetRequirement: targetRequirement, RuntimePolicy: runtimePolicy);
            if (!rootAliasKeysByDefinition[aliasClass]
                .Any(existing => StringComparer.Ordinal.Equals(
                    existing.ExportKey, alias.ExportKey)
                    && existing.TargetRequirement == alias.TargetRequirement
                    && existing.RuntimePolicy == alias.RuntimePolicy))
            {
                rootAliasKeysByDefinition[aliasClass].Add(alias);
            }
        }

        void AddImport(string producerIdentity, string exportKey,
            uint targetRequirement, uint runtimePolicy,
            uint expectedDefinition, int structureIndex)
        {
            if (string.IsNullOrEmpty(producerIdentity)
                || string.IsNullOrEmpty(exportKey))
            {
                throw Unsupported(
                    "An A2 source type Import has an empty provider identity or ExportKey.");
            }

            if (!moduleRefsByIdentity.TryGetValue(producerIdentity,
                    out var moduleRef))
            {
                var moduleRefRow = moduleRefs.Count + 1;
                moduleRefs.Add(new TrRmModuleRefRow(
                    getStringRow(producerIdentity), targetRequirement,
                    runtimePolicy));
                moduleRef = (targetRequirement, runtimePolicy,
                    moduleRefRow);
                moduleRefsByIdentity.Add(producerIdentity, moduleRef);
            }
            else if (moduleRef.TargetRequirement != targetRequirement
                || moduleRef.RuntimePolicy != runtimePolicy)
            {
                throw Unsupported(
                    "A2 source type origins retain conflicting Module requirements for one provider identity.");
            }

            if (importTriples.Add((moduleRef.Row, exportKey,
                    expectedDefinition)))
            {
                imports.Add(new TrRmImportRow(moduleRef.Row,
                    getStringRow(exportKey), expectedDefinition));
                _ = firstImportRowByStructure.TryAdd(structureIndex,
                    imports.Count);
            }
        }
    }

    private static bool SameA2SourceTypeProviderSnapshot(
        BoundSourceTypeProviderSnapshot left,
        BoundSourceTypeProviderSnapshot right)
        => StringComparer.Ordinal.Equals(left.SchemaIdentity,
                right.SchemaIdentity)
            && left.ArtifactSnapshot.AsSpan().SequenceEqual(
                right.ArtifactSnapshot.AsSpan())
            && left.ModuleRow == right.ModuleRow
            && StringComparer.Ordinal.Equals(left.ModuleIdentity,
                right.ModuleIdentity)
            && left.GoverningRuleEdition == right.GoverningRuleEdition
            && left.TargetRequirement == right.TargetRequirement
            && left.RuntimePolicy == right.RuntimePolicy
            && left.Features.AsSpan().SequenceEqual(right.Features.AsSpan())
            && left.TargetProfiles.AsSpan().SequenceEqual(
                right.TargetProfiles.AsSpan());

    private static bool SameA2SourceTypeDeclaration(
        BoundStruct actual, BoundStruct expected)
        => StringComparer.Ordinal.Equals(actual.Name, expected.Name)
            && actual.Fields.AsSpan().SequenceEqual(expected.Fields.AsSpan())
            && actual.Size == expected.Size
            && actual.TokenSpanRole == expected.TokenSpanRole
            && actual.BlittableTargetRequirement
                == expected.BlittableTargetRequirement
            && actual.IsSequential == expected.IsSequential
            && actual.IsTransparent == expected.IsTransparent
            && actual.IsUnique == expected.IsUnique
            && StringComparer.Ordinal.Equals(actual.CleanupMethod,
                expected.CleanupMethod)
            && actual.IsPrivate == expected.IsPrivate
            && actual.IsInternal == expected.IsInternal
            && expected.StandardTypeOrigin is null;
}
