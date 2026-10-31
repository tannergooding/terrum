// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Linq;
using Terrum.CodeAnalysis.Semantics;

namespace Terrum.CodeAnalysis.Modules;

public static partial class TrRmExecutableCompiler
{
    private static byte[] EmitForeignInvocationStorageFixedArrayCopyDeclarations(
        BoundProgram program, string moduleIdentity)
    {
        const TrRmArtifactSchema schema
            = TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyDraft;
        if (program.EntryPoint != -1 || !program.Functions.IsEmpty
            || program.Structures.IsEmpty || !program.Classes.IsEmpty
            || !program.Buffers.IsEmpty
            || !program.NativeFunctionPointerSignatures.IsEmpty
            || !program.NativePointerTypes.IsEmpty
            || !program.NativeFixedArrayTypes.IsEmpty
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
                "A2 Copy declaration emission requires a fail-fast library of local struct declarations.");
        }

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
                || structure.Fields.IsDefaultOrEmpty
                || structure.Fields.Any(field =>
                    !DirectNativeImportContract.IsScalar(field.Type)
                    && ((int)field.Type - 100 is < 0
                        || (int)field.Type - 100 >= typeDefCount)))
            {
                throw Unsupported(
                    "A2 Copy declaration emission supports Blittable Sequential/Transparent structs with scalar or local struct fields.");
            }
        }

        _ = FixedLayoutStorage.Validate(program.Structures);
        var hasFloatingTypes = program.Structures.Any(static structure
            => structure.Fields.Any(static field => IsFloat(field.Type)));
        var typeRows = (hasFloatingTypes ? s_typeRows : s_nonFloatTypeRows)
            .ToList();
        var typeDefTypeSpecRows = new Dictionary<int, int>();
        var nativeFunctionPointerTypeRows
            = new Dictionary<PrimitiveType, int>();
        var nativePointerTypeRows = new Dictionary<PrimitiveType, int>();
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
                var fieldTypeSpecRow = NativeBlittableFieldTypeRow(typeRows,
                    typeDefTypeSpecRows, structureTypeDefRows,
                    fieldDefinition.Type, 0, nativeFunctionPointerTypeRows,
                    nativePointerTypeRows, []);
                var visibility = fieldDefinition.IsPrivate ? 0u
                    : fieldDefinition.IsInternal ? 1u : 2u;
                var fieldFlags = visibility
                    | (fieldDefinition.IsMutable ? 4u : 0u);
                structureFieldNameRows[index][field]
                    = GetStringRow(fieldDefinition.Name);
                fieldRows.Add(new TrRmFieldDefRow(index + 1,
                    structureFieldNameRows[index][field],
                    checked((uint)fieldTypeSpecRow), fieldFlags));
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

        var featureRows = new List<TrRmFeatureRow>();
        if (hasCopyEvidence)
        {
            featureRows.Add(TrRmFeatureRow.RootlessNativeValues);
        }
        var featureBytes = featureRows.Count == 0
            ? [] : TrRmFeatureTableWriter.Encode(featureRows, schema);
        var stringBytes = TrRmStringTableWriter.EncodeTextRows(stringRows);
        var strings = new TrRmStringTableReader(stringBytes,
            stringRows.Count);
        var moduleBytes = TrRmModuleTableWriter.Encode([
            new TrRmModuleRow(2,
                TrRmModuleTableReader.WindowsX64NativeTargetRequirement,
                0, 0, 1, 1, 1, 1,
                FeatureFirst: featureRows.Count == 0 ? 0 : 1,
                FeatureCount: featureRows.Count),
        ], strings, 0, typeDefCount, 0, 0, featureRows.Count);
        var modules = new TrRmModuleTableReader(moduleBytes, 1, strings,
            0, typeDefCount, 0, 0, featureRows.Count);
        var typeSpecBytes = TrRmTypeSpecTableWriter.Encode(typeRows,
            typeDefCount, 0, 0, 0, schema);
        var typeSpecs = new TrRmTypeSpecTableReader(typeSpecBytes,
            typeRows.Count, typeDefCount, 0, 0, 0, schema);
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
}
