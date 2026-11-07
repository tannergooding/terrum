// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Terrum.CodeAnalysis.Semantics;

namespace Terrum.CodeAnalysis.Modules;

public static partial class TrRmExecutableCompiler
{
    private static readonly UTF8Encoding s_sourceTypeExportKeyEncoding
        = new(false, true);

    private sealed record SourceTypeImportPlan(
        List<TrRmModuleRefRow> ModuleRefs,
        List<TrRmImportRow> Imports,
        Dictionary<int, int> FirstImportRowByStructure);

    private static byte[] EmitForeignInvocationStorageFixedArrayCopyDeclarations(
        BoundProgram program, string moduleIdentity,
        TrRmArtifactSchema schema,
        IReadOnlyList<TrRmSourceTypeExport>? sourceTypeExports = null)
    {
        if (schema is not (
            TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyDraft
            or TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyEmptyInitializationDraft
            or TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyEmptyUnitInitializationDraft
            or TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyEmptyUnitScalarFrozenValuesDraft
            or TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyReturnCoverageDraft
            or TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyReturnCoverageRepeatInitializationDraft
            or TrRmArtifactSchema.LocalAggregateLayoutDraft))
        {
            throw new ArgumentOutOfRangeException(nameof(schema));
        }
        if (program.EntryPoint != -1
            || !program.Classes.IsEmpty
            || !program.Buffers.IsEmpty
            || !program.NativeFunctionPointerSignatures.IsEmpty
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
                "Standard source-type declaration emission requires a fail-fast library of supported struct declarations.");
        }
        if (!program.Functions.IsEmpty
            && schema is not (
                TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyReturnCoverageDraft
                or TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyReturnCoverageRepeatInitializationDraft))
        {
            throw Unsupported(
                "Only the exact A2R1 schema publishes verified function return coverage.");
        }

        ValidateA2SourceTypeDeclarationMap(program);
        if (!NativePointerTypeContract.IsValidTable(
                program.NativePointerTypes, program.Structures,
                program.NativeFunctionPointerSignatures,
                program.NativeFixedArrayTypes))
        {
            throw Unsupported(
                "Standard source-type declaration emission received an invalid native pointer type table.");
        }

        var typeDefCount = program.Structures.Length;
        var structureTypeDefRows = new Dictionary<int, int>(typeDefCount);
        for (var structureOrdinal = 0;
            structureOrdinal < typeDefCount; structureOrdinal++)
        {
            structureTypeDefRows.Add(structureOrdinal,
                structureOrdinal + 1);
        }

        foreach (var structure in program.Structures)
        {
            if (structure.BlittableTargetRequirement
                    != FixedLayoutStorage.WindowsX64TargetRequirement
                || structure.LayoutKind is not (1 or 2 or 3)
                || structure.CleanupMethod is not null
                || structure.Fields.IsDefaultOrEmpty)
            {
                throw Unsupported(
                    "Standard source-type declaration emission supports Blittable fixed-layout structs without Cleanup.");
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
                var isPointer = NativePointerTypes.IsType(field.Type)
                    && (uint)NativePointerTypes.Index(field.Type)
                        < (uint)program.NativePointerTypes.Length;
                if (field.RequiresUnsafeCaller
                    || (!DirectNativeImportContract.IsScalar(field.Type)
                        && !isStructure && !isValidArray && !isPointer))
                {
                    throw Unsupported(
                        "Standard FieldDef declarations cannot discard an independent field-level unsafe obligation or an unsupported field type.");
                }
            }
        }

        _ = FixedLayoutStorage.Validate(program.Structures,
            program.NativeFixedArrayTypes);
        var hasFloatingTypes = program.Structures.Any(static structure
                => structure.Fields.Any(static field => IsFloat(field.Type)))
            || program.NativeFixedArrayTypes.Any(static fixedArray
                => IsFloat(fixedArray.ElementType));
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
        var typeImportPlan = CreateSourceTypeImportPlan(program, schema,
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
                nativeFunctionPointerTypeRows, nativePointerTypeRows,
                program.NativePointerTypes,
                pointeeType => checked((int)GetFieldTypeSpecRef(
                    pointeeType))));
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
        for (var typeDefOrdinal = 0;
            typeDefOrdinal < typeDefCount; typeDefOrdinal++)
        {
            var structure = program.Structures[typeDefOrdinal];
            var separator = structure.Name.LastIndexOf('.',
                StringComparison.Ordinal);
            var typeNamespace = separator < 0 ? "" : structure.Name[..separator];
            var typeName = separator < 0
                ? structure.Name : structure.Name[(separator + 1)..];
            if (typeName.Length == 0)
            {
                throw Unsupported(
                    $"Standard source TypeDef identity '{structure.Name}' is invalid.");
            }

            structureNamespaceRows[typeDefOrdinal]
                = GetStringRow(typeNamespace);
            structureNameRows[typeDefOrdinal] = GetStringRow(typeName);
            structureFieldNameRows[typeDefOrdinal]
                = new int[structure.Fields.Length];
            fieldFirstByStructure[typeDefOrdinal] = fieldRows.Count + 1;
            for (var field = 0; field < structure.Fields.Length; field++)
            {
                var fieldDefinition = structure.Fields[field];
                var fieldTypeSpecRef = GetFieldTypeSpecRef(
                    fieldDefinition.Type);
                var visibility = fieldDefinition.IsPrivate ? 0u
                    : fieldDefinition.IsInternal ? 1u : 2u;
                var fieldFlags = visibility
                    | (fieldDefinition.IsMutable ? 4u : 0u);
                structureFieldNameRows[typeDefOrdinal][field]
                    = GetStringRow(fieldDefinition.Name);
                fieldRows.Add(new TrRmFieldDefRow(typeDefOrdinal + 1,
                    structureFieldNameRows[typeDefOrdinal][field],
                    fieldTypeSpecRef, fieldFlags));
            }

            layoutFirstByStructure[typeDefOrdinal] = layoutRows.Count + 1;
            var layoutKind = structure.LayoutKind;
            var declaredAlignment = structure.DeclaredAlignment is int alignment
                ? checked((uint)alignment) : 0u;
            var pack = checked((uint)structure.Packing);
            for (var variant = 0; variant < 2; variant++)
            {
                var layoutRow = layoutRows.Count + 1;
                var layoutFieldFirst = layoutFieldRows.Count + 1;
                layoutRows.Add(new TrRmLayoutRow(typeDefOrdinal + 1,
                    variant == 0 ? 0u : 1u, layoutKind,
                    variant == 0 ? (byte)0 : (byte)1,
                    declaredAlignment, pack, layoutFieldFirst,
                    structure.Fields.Length));
                for (var field = 0; field < structure.Fields.Length; field++)
                {
                    layoutFieldRows.Add(new TrRmLayoutFieldRow(layoutRow,
                        field, fieldFirstByStructure[typeDefOrdinal] + field));
                }
            }

            var visibilityFlags = structure.IsPrivate ? 0u
                : structure.IsInternal ? 1u : 2u;
            var declaredCopy = !structure.IsUnique;
            if (declaredCopy)
            {
                hasCopyEvidence = true;
            }
            typeDefRows[typeDefOrdinal] = new TrRmTypeDefRow(
                structureNamespaceRows[typeDefOrdinal],
                structureNameRows[typeDefOrdinal],
                visibilityFlags | 4u
                    | (declaredCopy
                        ? TrRmTypeDefTableReader.DeclaredCopyFlag : 0u),
                fieldFirstByStructure[typeDefOrdinal],
                structure.Fields.Length,
                0, 0, layoutFirstByStructure[typeDefOrdinal], 2, 0, 0);
        }

        for (var arrayIndex = 0;
            arrayIndex < program.NativeFixedArrayTypes.Length; arrayIndex++)
        {
            _ = GetArrayTypeSpecRow(arrayIndex);
        }

        var methodSignatureRows
            = new List<TrRmMethodSigRow>(program.Functions.Length);
        var parameterRows = new List<TrRmParamRow>();
        var methodNameRows = new int[program.Functions.Length];
        var methodContractFirst = new int[program.Functions.Length];
        var methodContractCount = new int[program.Functions.Length];
        var contractRows = new List<TrRmContractRow>();
        var hasRawUnionReturnCoverage = false;
        for (var functionIndex = 0;
            functionIndex < program.Functions.Length;
            functionIndex++)
        {
            var function = program.Functions[functionIndex];
            var methodSigRow = functionIndex + 1;
            var parameterFirst = function.ParameterCount == 0
                ? 0 : parameterRows.Count + 1;
            for (var ordinal = 0; ordinal < function.ParameterCount; ordinal++)
            {
                var parameter = function.Variables[ordinal];
                parameterRows.Add(new TrRmParamRow(methodSigRow, ordinal,
                    GetFieldTypeSpecRef(parameter.Type), 0));
            }

            methodSignatureRows.Add(new TrRmMethodSigRow(
                GetFieldTypeSpecRef(function.ReturnType), 0,
                parameterFirst, function.ParameterCount, 0, 0));
            methodNameRows[functionIndex] = GetStringRow(function.Name);
            if (function.RawUnionReturnCoverage.IsDefaultOrEmpty)
            {
                continue;
            }
            if (!SemanticVerifier.IsRawUnionType(program,
                    function.ReturnType))
            {
                throw Unsupported(
                    "A Raw Union ReturnCoverage summary is attached to a non-Union result.");
            }

            var initializedBytes = function.RawUnionReturnCoverage;
            var guaranteedBytes
                = SemanticVerifier.RawUnionGuaranteedInitializedPayloadBytes(
                    function.ReturnType, program);
            if (initializedBytes.Length != guaranteedBytes.Length)
            {
                throw Unsupported(
                    "A Raw Union ReturnCoverage summary does not match the result layout.");
            }

            var strongerThanGuarantee = false;
            for (var offset = 0; offset < initializedBytes.Length; offset++)
            {
                strongerThanGuarantee |= initializedBytes[offset] != 0
                    && guaranteedBytes[offset] == 0;
            }
            if (!strongerThanGuarantee)
            {
                continue;
            }

            methodContractFirst[functionIndex] = contractRows.Count + 1;
            var owner = 0x1600_0000u
                | checked((uint)(functionIndex + 1));
            contractRows.Add(new TrRmContractRow(owner, 0x0F,
                CreateReturnCoveragePayload(initializedBytes.AsSpan())));
            methodContractCount[functionIndex] = 1;
            hasRawUnionReturnCoverage = true;
        }

        var methodRows = new List<TrRmMethodDefRow>(
            program.Functions.Length);
        var exportKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var export in exportRows)
        {
            _ = exportKeys.Add(stringRows[export.ExportKeyStringRow - 1].Text);
        }
        for (var functionIndex = 0;
            functionIndex < program.Functions.Length;
            functionIndex++)
        {
            var function = program.Functions[functionIndex];
            var methodRow = functionIndex + 1;
            var visibility = function.IsPrivate ? 0u
                : function.IsInternal ? 1u : 2u;
            methodRows.Add(new TrRmMethodDefRow(0,
                methodNameRows[functionIndex], functionIndex + 1,
                visibility, methodContractFirst[functionIndex],
                methodContractCount[functionIndex], BodyRow: 0));
            if (visibility == 2)
            {
                if (!exportKeys.Add(function.Name))
                {
                    throw Unsupported(
                        "A2R1 declaration emission requires unique public MethodDef export names.");
                }

                exportRows.Add(new TrRmExportRow(
                    0x1600_0000u | checked((uint)methodRow),
                    methodNameRows[functionIndex]));
            }
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
        if (hasRawUnionReturnCoverage)
        {
            if (!featureRows.Contains(TrRmFeatureRow.RootlessNativeValues))
            {
                featureRows.Add(TrRmFeatureRow.RootlessNativeValues);
            }
            featureRows.Add(TrRmFeatureRow.RawUnionValueCoverageCandidate);
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
        ], strings, typeImportPlan.ModuleRefs.Count, typeDefCount,
            methodRows.Count, 0, featureRows.Count);
        var modules = new TrRmModuleTableReader(moduleBytes, 1, strings,
            typeImportPlan.ModuleRefs.Count, typeDefCount,
            methodRows.Count, 0,
            featureRows.Count);
        var moduleRefBytes = typeImportPlan.ModuleRefs.Count == 0 ? []
            : TrRmModuleRefTableWriter.Encode(typeImportPlan.ModuleRefs,
                strings, modules);
        var moduleRefs = new TrRmModuleRefTableReader(moduleRefBytes,
            typeImportPlan.ModuleRefs.Count, strings, modules);
        var typeSpecBytes = TrRmTypeSpecTableWriter.Encode(typeRows,
            typeDefCount, typeImportPlan.Imports.Count, 0, 0, schema,
            methodDefCount: methodRows.Count);
        var typeSpecs = new TrRmTypeSpecTableReader(typeSpecBytes,
            typeRows.Count, typeDefCount, typeImportPlan.Imports.Count, 0, 0,
            schema, methodDefCount: methodRows.Count);
        var typeDefBytes = typeDefCount == 0 ? []
            : TrRmTypeDefTableWriter.Encode(typeDefRows,
                strings, modules, fieldRows.Count, 0, layoutRows.Count,
                contractRows.Count, schema);
        var typeDefs = new TrRmTypeDefTableReader(typeDefBytes, typeDefCount,
            strings, modules, fieldRows.Count, 0, layoutRows.Count,
            contractRows.Count,
            schema);
        var fieldBytes = fieldRows.Count == 0 ? []
            : TrRmFieldDefTableWriter.Encode(fieldRows, strings,
                typeDefs, typeSpecs);
        var fields = fieldRows.Count == 0 ? default
            : new TrRmFieldDefTableReader(fieldBytes, fieldRows.Count,
                strings, typeDefs, typeSpecs);
        var importBytes = typeImportPlan.Imports.Count == 0 ? []
            : TrRmImportTableWriter.Encode(typeImportPlan.Imports,
                strings, moduleRefs, typeDefs, default, default, default,
                default, fields, features, modules, schema);
        var imports = new TrRmImportTableReader(importBytes,
            typeImportPlan.Imports.Count, strings, moduleRefs, typeDefs,
            default, default, default, default, fields, features, modules,
            schema);
        var methodSignatureBytes = methodSignatureRows.Count == 0 ? []
            : TrRmMethodSigTableWriter.Encode(methodSignatureRows, typeSpecs,
                parameterRows.Count, 0);
        var signatures = methodSignatureRows.Count == 0
            ? default
            : new TrRmMethodSigTableReader(methodSignatureBytes,
                methodSignatureRows.Count, typeSpecs, parameterRows.Count, 0);
        var parameterBytes = parameterRows.Count == 0 ? []
            : TrRmParamTableWriter.Encode(parameterRows, signatures, typeSpecs,
                schema);
        var parameters = parameterRows.Count == 0
            ? default
            : new TrRmParamTableReader(parameterBytes, parameterRows.Count,
                signatures, typeSpecs, schema);
        var methodBytes = methodRows.Count == 0 ? []
            : TrRmMethodDefTableWriter.Encode(methodRows, strings, modules,
                typeDefs, signatures, contractRows.Count, bodyCount: 0);
        var methods = methodRows.Count == 0
            ? default
            : new TrRmMethodDefTableReader(methodBytes, methodRows.Count,
                strings, modules, typeDefs, signatures, contractRows.Count, 0);
        var contractBytes = contractRows.Count == 0 ? []
            : TrRmContractTableWriter.EncodeForeignInvocationDraft(
                contractRows, typeDefs, methods, signatures, parameters,
                typeSpecs, features, modules, predicateGraphCount: 0,
                schema: schema);
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
                methods, default, imports, default, default, fields,
                features, modules, schema);
        var sections = new List<TrRmSection>
        {
            new(0x0001, 0, 1, moduleBytes),
            new(0x0002, 0, (uint)stringRows.Count, stringBytes),
            new(0x0011, 0, (uint)typeRows.Count, typeSpecBytes),
            new(0x0048, 0, 1, targetProfileBytes),
        };
        if (typeDefCount != 0)
        {
            sections.Add(new TrRmSection(0x0013, 0,
                (uint)typeDefCount, typeDefBytes));
        }
        if (fieldRows.Count != 0)
        {
            sections.Add(new TrRmSection(0x0014, 0,
                (uint)fieldRows.Count, fieldBytes));
        }
        if (layoutRows.Count != 0)
        {
            sections.Add(new TrRmSection(0x0015, 0,
                (uint)layoutRows.Count, layoutBytes));
        }
        if (layoutFieldRows.Count != 0)
        {
            sections.Add(new TrRmSection(0x0023, 0,
                (uint)layoutFieldRows.Count, layoutFieldBytes));
        }
        if (methodRows.Count != 0)
        {
            sections.Add(new TrRmSection(0x0016, 0,
                (uint)methodRows.Count, methodBytes));
            sections.Add(new TrRmSection(0x0017, 0,
                (uint)methodSignatureRows.Count, methodSignatureBytes));
        }
        if (parameterRows.Count != 0)
        {
            sections.Add(new TrRmSection(0x0018, 0,
                (uint)parameterRows.Count, parameterBytes));
        }
        if (contractRows.Count != 0)
        {
            sections.Add(new TrRmSection(0x001B, 0,
                (uint)contractRows.Count, contractBytes));
        }
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
        var index = schema == TrRmArtifactSchema.LocalAggregateLayoutDraft
            ? TrRmArtifactIndex.ForLocalAggregateLayoutDraft(artifact)
            : schema
                .UsesForeignInvocationStorageFixedArrayRepeatInitialization()
                ? TrRmArtifactIndex
                .ForForeignInvocationStorageFixedArrayCopyReturnCoverageRepeatInitializationDraft(
                    artifact)
            : schema.UsesRawUnionValueCoverage()
            ? TrRmArtifactIndex
                .ForForeignInvocationStorageFixedArrayCopyReturnCoverageDraft(
                    artifact)
            : schema.UsesScalarFrozenValues()
            ? TrRmArtifactIndex
                .ForForeignInvocationStorageFixedArrayCopyEmptyUnitScalarFrozenValuesDraft(
                    artifact)
            : schema.UsesForeignInvocationStorageFixedArrayUnitInitialization()
            ? TrRmArtifactIndex
                .ForForeignInvocationStorageFixedArrayCopyEmptyUnitInitializationDraft(
                    artifact)
            : schema.UsesForeignInvocationStorageFixedArrayEmptyInitialization()
                ? TrRmArtifactIndex
                    .ForForeignInvocationStorageFixedArrayCopyEmptyInitializationDraft(
                        artifact)
                : TrRmArtifactIndex
                    .ForForeignInvocationStorageFixedArrayCopyDraft(artifact);
        if (schema
            .UsesForeignInvocationStorageFixedArrayRepeatInitialization())
        {
            _ = TrRmStructuralMetadataValidator
                .ValidateForeignInvocationStorageFixedArrayCopyReturnCoverageRepeatInitializationDraftAndGetExecutionTables(
                    index);
        }
        else if (schema.UsesRawUnionValueCoverage())
        {
            _ = TrRmStructuralMetadataValidator
                .ValidateForeignInvocationStorageFixedArrayCopyReturnCoverageDraftAndGetExecutionTables(
                    index);
        }
        else if (schema.UsesScalarFrozenValues())
        {
            _ = TrRmStructuralMetadataValidator
                .ValidateForeignInvocationStorageFixedArrayCopyEmptyUnitScalarFrozenValuesDraftAndGetExecutionTables(
                    index);
        }
        else if (schema.UsesForeignInvocationStorageFixedArrayUnitInitialization())
        {
            _ = TrRmStructuralMetadataValidator
                .ValidateForeignInvocationStorageFixedArrayCopyEmptyUnitInitializationDraftAndGetExecutionTables(
                    index);
        }
        else if (schema.UsesForeignInvocationStorageFixedArrayEmptyInitialization())
        {
            _ = TrRmStructuralMetadataValidator
                .ValidateForeignInvocationStorageFixedArrayCopyEmptyInitializationDraftAndGetExecutionTables(
                    index);
        }
        else
        {
            _ = TrRmStructuralMetadataValidator
                .ValidateForeignInvocationStorageFixedArrayCopyDraftAndGetExecutionTables(
                    index);
        }
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
            return program.SourceTypePublications.IsDefault
                ? rows
                : CreateA2RetainedSourceTypeExportRows(program,
                    structureTypeDefRows, getStringRow);
        }
        if (!program.SourceTypePublications.IsDefault)
        {
            throw new ArgumentException(
                "A new source TypeDef export map cannot replace a retained Standard publication map.",
                nameof(typeExports));
        }

        var sourceDeclarations = program.SourceTypeDeclarations;
        if (sourceDeclarations.IsDefault)
        {
            throw Unsupported(
                "Explicit Standard TypeDef exports require a source-bound local declaration set.");
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

        ValidateA2SourceTypeExportClosure(program, selectedStructures);
        return rows;
    }

    private static byte[] CreateReturnCoveragePayload(
        ReadOnlySpan<byte> initializedBytes)
    {
        var ranges = new List<(int Offset, int Length)>();
        var offset = 0;
        while (offset < initializedBytes.Length)
        {
            if (initializedBytes[offset] == 0)
            {
                offset++;
                continue;
            }

            var start = offset;
            do
            {
                offset++;
            }
            while (offset < initializedBytes.Length
                && initializedBytes[offset] != 0);
            ranges.Add((start, offset - start));
        }

        if (ranges.Count == 0)
        {
            throw new InvalidDataException(
                "A Raw Union ReturnCoverage summary contains no initialized bytes.");
        }

        var result = new byte[checked(8 + (ranges.Count * 20))];
        BinaryPrimitives.WriteUInt32LittleEndian(result, 1);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4),
            checked((uint)ranges.Count));
        var rowOffset = 8;
        foreach (var (rangeOffset, rangeLength) in ranges)
        {
            BinaryPrimitives.WriteUInt64LittleEndian(
                result.AsSpan(rowOffset), checked((ulong)rangeOffset));
            BinaryPrimitives.WriteUInt64LittleEndian(
                result.AsSpan(rowOffset + sizeof(ulong)),
                checked((ulong)rangeLength));
            rowOffset += 20;
        }

        return result;
    }

    private static List<TrRmExportRow> CreateA2RetainedSourceTypeExportRows(
        BoundProgram program,
        Dictionary<int, int> structureTypeDefRows,
        Func<string, int> getStringRow)
    {
        var rows = new List<TrRmExportRow>();
        var selectedStructures = new HashSet<int>();
        var retainedStructures = new HashSet<int>();
        var exportKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var publication in program.SourceTypePublications)
        {
            if ((uint)publication.StructureIndex
                    >= (uint)program.Structures.Length
                || !retainedStructures.Add(publication.StructureIndex)
                || publication.DeclarationSnapshot is null)
            {
                throw new ArgumentException(
                    "The retained source TypeDef publication map is inconsistent.",
                    nameof(program));
            }

            var structure = program.Structures[publication.StructureIndex];
            if (structure.StandardTypeOrigin is not null
                || !SameA2SourceTypeDeclaration(structure,
                    publication.DeclarationSnapshot)
                || publication.ExportKeys.IsDefault
                || publication.ExpectedFixedArrayTypes.IsDefault
                || !program.NativeFixedArrayTypes.AsSpan().SequenceEqual(
                    publication.ExpectedFixedArrayTypes.AsSpan())
                || publication.ExpectedNativePointerTypes.IsDefault
                || !program.NativePointerTypes.AsSpan().SequenceEqual(
                    publication.ExpectedNativePointerTypes.AsSpan()))
            {
                throw Unsupported(
                    "A retained Standard TypeDef publication no longer matches its checked declaration and type tables.");
            }

            if (publication.ExportKeys.IsEmpty)
            {
                continue;
            }

            if (structure.IsPrivate || structure.IsInternal
                || structure.IsUnique || structure.CleanupMethod is not null)
            {
                throw Unsupported(
                    "A retained Standard TypeDef export no longer names a public ordinary Copy declaration.");
            }

            _ = selectedStructures.Add(publication.StructureIndex);
            foreach (var exportKey in publication.ExportKeys)
            {
                if (string.IsNullOrEmpty(exportKey)
                    || exportKey.Contains('\0', StringComparison.Ordinal))
                {
                    throw Unsupported(
                        "A retained Standard TypeDef publication contains an invalid ExportKey.");
                }

                try
                {
                    _ = s_sourceTypeExportKeyEncoding.GetByteCount(exportKey);
                }
                catch (EncoderFallbackException exception)
                {
                    throw new NotSupportedException(
                        "A retained Standard TypeDef ExportKey is not well-formed scalar text.",
                        exception);
                }

                if (!exportKeys.Add(exportKey))
                {
                    throw Unsupported(
                        "Retained Standard TypeDef ExportKeys are not unique.");
                }

                var typeDefRow = structureTypeDefRows[
                    publication.StructureIndex];
                rows.Add(new TrRmExportRow(
                    0x1300_0000u | checked((uint)typeDefRow),
                    getStringRow(exportKey)));
            }
        }

        ValidateA2SourceTypeExportClosure(program, selectedStructures);
        return rows;
    }

    private static void ValidateA2SourceTypeExportClosure(
        BoundProgram program, HashSet<int> selectedStructures)
    {
        var pendingStructures = new Stack<int>(selectedStructures);
        var checkedStructures = new HashSet<int>();
        var checkedArrayTypes = new HashSet<int>();
        var checkedPointerTypes = new HashSet<int>();
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

                if (checkedArrayTypes.Add(arrayIndex))
                {
                    AddNominalDependencies(
                        program.NativeFixedArrayTypes[arrayIndex].ElementType);
                }
                return;
            }

            if (NativePointerTypes.IsType(type))
            {
                var pointerIndex = NativePointerTypes.Index(type);
                if ((uint)pointerIndex
                    >= (uint)program.NativePointerTypes.Length)
                {
                    throw Unsupported(
                        "A source TypeDef export has an invalid native-pointer closure.");
                }

                if (checkedPointerTypes.Add(pointerIndex))
                {
                    AddNominalDependencies(
                        program.NativePointerTypes[pointerIndex].PointeeType);
                }
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
                    "Every local nominal type in an exported Standard TypeDef storage closure requires an explicit ExportKey.");
            }

            var structure = program.Structures[structureIndex];
            if (structure.IsPrivate || structure.IsInternal
                || structure.IsUnique || structure.CleanupMethod is not null)
            {
                throw Unsupported(
                    "An exported Standard TypeDef storage closure contains an ineligible nominal type.");
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
        var sourcePublications = program.SourceTypePublications;
        if (sourceDeclarations.IsDefault && sourcePublications.IsDefault)
        {
            return;
        }

        var sourceIndexes = new bool[program.Structures.Length];
        if (!sourceDeclarations.IsDefault)
        {
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
        }

        if (!sourcePublications.IsDefault)
        {
            foreach (var publication in sourcePublications)
            {
                if ((uint)publication.StructureIndex
                        >= (uint)program.Structures.Length
                    || sourceIndexes[publication.StructureIndex])
                {
                    throw new ArgumentException(
                        "The retained source TypeDef publication map is inconsistent.",
                        nameof(program));
                }

                var structure = program.Structures[publication.StructureIndex];
                if (structure.StandardTypeOrigin is not null
                    || publication.DeclarationSnapshot is null
                    || !SameA2SourceTypeDeclaration(structure,
                        publication.DeclarationSnapshot))
                {
                    throw Unsupported(
                        "A retained Standard TypeDef identity no longer matches its checked local declaration.");
                }

                sourceIndexes[publication.StructureIndex] = true;
            }
        }

        for (var index = 0; index < program.Structures.Length; index++)
        {
            if (!sourceIndexes[index]
                && program.Structures[index].StandardTypeOrigin is null)
            {
                throw Unsupported(
                    "Standard source emission cannot preserve a referenced type without its Standard TypeDef origin.");
            }
        }
    }

    private static SourceTypeImportPlan CreateSourceTypeImportPlan(
        BoundProgram program, TrRmArtifactSchema schema,
        Func<string, int> getStringRow)
    {
        var expectedProviderSchemaIdentity = schema switch {
            TrRmArtifactSchema.LocalAggregateLayoutDraft
                => StandardLocalAggregateLayoutSchemaIdentity,
            TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyDraft
            or TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyEmptyInitializationDraft
            or TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyEmptyUnitInitializationDraft
            or TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyEmptyUnitScalarFrozenValuesDraft
            or TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyReturnCoverageDraft
            or TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyReturnCoverageRepeatInitializationDraft
                => StandardForeignInvocationStorageFixedArraySchemaIdentity,
            _ => null,
        };
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

            var expectedProviderSchema = expectedProviderSchemaIdentity
                ?? throw new ArgumentOutOfRangeException(nameof(schema));
            var provider = origin.Provider;
            var expectedDeclaration = origin.ExpectedDeclaration
                ?? throw Unsupported(
                    "A Standard source type origin does not retain its checked declaration snapshot.");
            if (!SameA2SourceTypeDeclaration(
                    program.Structures[structureIndex],
                    expectedDeclaration))
            {
                throw Unsupported(
                    "A Standard source type declaration changed after its provider expectation was checked.");
            }
            var referenceRoot = origin.ReferenceRoot
                ?? throw Unsupported(
                    "A Standard source type origin does not retain its reference root.");
            if (!StringComparer.Ordinal.Equals(provider.SchemaIdentity,
                    expectedProviderSchema)
                || !StringComparer.Ordinal.Equals(referenceRoot.SchemaIdentity,
                    expectedProviderSchema)
                || string.IsNullOrEmpty(provider.ModuleIdentity)
                || origin.DefinitionId >> 24 != 0x13
                || origin.ExportKeys.IsDefaultOrEmpty)
            {
                throw Unsupported(
                    "A source type origin lacks a complete identity for the selected provider schema.");
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
                    "A Standard source type array or pointer table changed after its provider expectation was checked.");
            }

            if (providerSnapshotsByIdentity.TryGetValue(
                    provider.ModuleIdentity, out var existingSnapshot))
            {
                if (!SameA2SourceTypeProviderSnapshot(existingSnapshot,
                        provider))
                {
                    throw Unsupported(
                        "Standard source type origins select conflicting snapshots for one provider identity.");
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
                        "A Standard type lookup root does not belong to its retained reference Module.");
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
                        "An imported Standard source type has no retained Import expectations.");
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
                        "An imported Standard source type lacks a complete expectation from its retained reference Module.");
                }

                var rootKeys = rootExpectations.Select(
                        static expectation => expectation.ExportKey)
                    .Distinct(StringComparer.Ordinal);
                if (!origin.ExportKeys.SequenceEqual(rootKeys,
                        StringComparer.Ordinal))
                {
                    throw Unsupported(
                        "An imported Standard source type's retained alias keys do not match its reference Module expectations.");
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
                    "A Standard source type origin does not retain its reference root.");
            foreach (var expectation in origin.Expectations)
            {
                if (schema == TrRmArtifactSchema.LocalAggregateLayoutDraft
                    && (!StringComparer.Ordinal.Equals(
                            expectation.UsingModule.SchemaIdentity,
                            StandardLocalAggregateLayoutSchemaIdentity)
                        || !StringComparer.Ordinal.Equals(
                            expectation.Provider.SchemaIdentity,
                            StandardLocalAggregateLayoutSchemaIdentity)))
                {
                    throw Unsupported(
                        "A source type Import edge does not retain the exact provider schema selected for this artifact.");
                }

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
                        "A provider-owned Standard source-type expectation does not retain its actual TypeDef origin.");
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

        return new SourceTypeImportPlan(moduleRefs, imports,
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
                    "A Standard source type Import has an empty provider identity or ExportKey.");
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
                    "Standard source type origins retain conflicting Module requirements for one provider identity.");
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
