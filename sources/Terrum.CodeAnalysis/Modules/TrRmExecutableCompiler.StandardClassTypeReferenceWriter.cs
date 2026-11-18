// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using Terrum.CodeAnalysis.Semantics;

namespace Terrum.CodeAnalysis.Modules;

public static partial class TrRmExecutableCompiler
{
    private sealed record C1M1TypeDefEmissionView(
        byte Kind, string Namespace, string Name, uint TypeDefFlags,
        ImmutableArray<BoundStandardClassFieldDeclaration> Fields,
        ImmutableArray<BoundStandardClassLayoutVariant> Layouts);

    private static byte[] EmitC1M1SourceClassDeclarations(
        BoundProgram program, string moduleIdentity,
        uint moduleTargetRequirement, SourceClassExports sourceClassExports)
    {
        if (!ReferenceEquals(sourceClassExports.Program, program)
            || program.EntryPoint != -1
            || !program.StandardClassDeclarations.IsEmpty
            || !program.GenericNominalTypeDefinitions.IsEmpty
            || !program.GenericNominalTypeInstances.IsEmpty
            || !program.Interfaces.IsEmpty
            || !program.InterfaceWitnesses.IsEmpty
            || !program.GenericMethodDefinitions.IsEmpty
            || !program.GenericMethodSignatures.IsEmpty
            || !program.GenericMethodOperatorProtocols.IsEmpty)
        {
            throw Unsupported(
                "C1M1 source declaration emission requires captured local non-generic classes without imported class, generic or interface closure.");
        }
        if (sourceClassExports.Exports.Any(static export =>
                export.MemberExports.Any(static member =>
                    member.Kind != SourceClassMemberKind.Field)))
        {
            throw Unsupported(
                "C1M1 source class declarations do not publish constructor, method, or property ExportKeys; use the exact construction successor.");
        }
        if (program.Classes.Any(static @class =>
                @class.HasCleanup is not false))
        {
            throw Unsupported(
                "The selected C1M1 class TypeDef view does not preserve class Cleanup selection.");
        }
        if (program.Classes.Any(static @class =>
                @class.Fields.Any(static field =>
                    field.BackingPropertyName is not null)))
        {
            throw Unsupported(
                "Property backing fields require the exact PI1 schema and cannot be emitted as named FieldDefs.");
        }

        SemanticVerifier.VerifyLibraryReferences(program);
        var exportKeysByClass = new SortedDictionary<int, List<string>>();
        foreach (var export in sourceClassExports.Exports)
        {
            var classIndex = export.Declaration.ClassIndex;
            if (classIndex < 0 || classIndex >= program.Classes.Length
                || !ReferenceEquals(program.Classes[classIndex],
                    export.Declaration.DeclarationSnapshot))
            {
                throw new InvalidDataException(
                    "A captured source class no longer identifies its checked local declaration.");
            }

            if (!exportKeysByClass.TryGetValue(classIndex,
                    out var exportKeys))
            {
                exportKeys = [];
                exportKeysByClass.Add(classIndex, exportKeys);
            }

            exportKeys.Add(export.ExportKey);
        }

        if (exportKeysByClass.Count == 0
            || exportKeysByClass.Count > ManagedClassTypes.OptionalFirst
                - ManagedClassTypes.First)
        {
            throw Unsupported(
                "C1M1 source declaration emission requires at least one captured class within the local class limit.");
        }

        var sourceClassProgram = program.SourceClassProgramAssociation;
        if (sourceClassProgram is null)
        {
            throw new InvalidDataException(
                "Captured C1M1 class exports have no compiler-owned source class association.");
        }

        var localClassIndexes = sourceClassProgram.Declarations
            .Select(static declaration => declaration.ClassIndex)
            .ToHashSet();
        var sourceStructDeclarations = program.SourceTypeDeclarations
            .ToDictionary(static declaration => declaration.StructureIndex);
        var retainedStructureIndexes = new HashSet<int>();
        var pendingTypeDefinitions = new Queue<(bool IsClass, int Index)>(
            exportKeysByClass.Keys.Select(static classIndex =>
                (IsClass: true, classIndex)));
        while (pendingTypeDefinitions.TryDequeue(out var pendingDefinition))
        {
            ImmutableArray<BoundStructField> pendingFields;
            if (pendingDefinition.IsClass)
            {
                pendingFields
                    = program.Classes[pendingDefinition.Index].Fields;
            }
            else
            {
                var structure = program.Structures[pendingDefinition.Index];
                if (!IsC1M1SourceStructLayoutSupported(structure))
                {
                    throw Unsupported(
                        "C1M1 class declaration closure supports only non-generic local structs with auto layout and no ownership or cleanup contract.");
                }

                pendingFields = structure.Fields;
            }

            foreach (var field in pendingFields)
            {
                if (TryGetC1M1SourceClassFieldType(field.Type,
                        out var dependencyClassIndex, out _, out _))
                {
                    if (!localClassIndexes.Contains(dependencyClassIndex))
                    {
                        throw Unsupported(
                            "C1M1 source class declarations do not capture imported or private class TypeDef dependencies.");
                    }

                    if (exportKeysByClass.TryAdd(dependencyClassIndex, []))
                    {
                        if (exportKeysByClass.Count
                            > ManagedClassTypes.OptionalFirst
                                - ManagedClassTypes.First)
                        {
                            throw Unsupported(
                                "C1M1 source class dependency closure exceeds the local class limit.");
                        }

                        pendingTypeDefinitions.Enqueue((true,
                            dependencyClassIndex));
                    }

                    continue;
                }

                if (TryGetC1M1SourceStructFieldType(field.Type,
                        sourceStructDeclarations,
                        out var dependencyStructureIndex))
                {
                    if (retainedStructureIndexes.Add(
                            dependencyStructureIndex))
                    {
                        if (retainedStructureIndexes.Count
                            > SemanticVerifier.MaximumVariables)
                        {
                            throw Unsupported(
                                "C1M1 source class struct dependency closure exceeds the local structure limit.");
                        }

                        pendingTypeDefinitions.Enqueue((false,
                            dependencyStructureIndex));
                    }
                    continue;
                }

                _ = GetC1M1SourceScalarTypeSpecReference(field.Type);
            }
        }

        var typeDefRows = new List<TrRmTypeDefRow>(
            exportKeysByClass.Count);
        var typeDefNames = new List<(string Namespace, string Name)>(
            exportKeysByClass.Count);
        var fieldRows = new List<TrRmFieldDefRow>();
        var fieldNames = new List<string>();
        var layoutRows = new List<TrRmLayoutRow>(
            exportKeysByClass.Count + retainedStructureIndexes.Count);
        var layoutFieldRows = new List<TrRmLayoutFieldRow>();
        var typeSpecRows = new List<ReadOnlyMemory<byte>>();
        var typeSpecRowByClassType = new Dictionary<
            (int ClassIndex, bool IsWritable, bool IsOptional), uint>();
        var typeSpecRowByStructure = new Dictionary<int, uint>();
        var exportRows = new List<(uint DefinitionId, string Key)>();
        var sourceNames = new HashSet<(string Namespace, string Name)>();
        var typeDefRowByClass = exportKeysByClass.Keys
            .Select(static (classIndex, ordinal) =>
                (classIndex, typeDefRow: ordinal + 1))
            .ToDictionary(static item => item.classIndex,
                static item => item.typeDefRow);
        var fieldDefRowByClassAndIndex = new Dictionary<
            (int ClassIndex, int FieldIndex), int>();
        var typeDefRowByStructure = retainedStructureIndexes
            .Order()
            .Select(static (structureIndex, ordinal) =>
                (structureIndex, typeDefRow: ordinal + 1))
            .ToDictionary(static item => item.structureIndex,
                item => item.typeDefRow + exportKeysByClass.Count);
        var typeDefOrder = exportKeysByClass.Keys
            .Select(static classIndex => (IsClass: true, Index: classIndex))
            .Concat(retainedStructureIndexes.Order().Select(
                static structureIndex => (IsClass: false,
                    Index: structureIndex)));

        uint FieldTypeSpecReference(PrimitiveType type)
        {
            if (TryGetC1M1SourceClassFieldType(type,
                    out var classIndex, out var isWritable,
                    out var isOptional))
            {
                if (!typeDefRowByClass.TryGetValue(classIndex,
                        out var typeDefRow))
                {
                    throw new InvalidDataException(
                        "A captured source class FieldDef refers to an unretained local class.");
                }

                var key = (classIndex, isWritable, isOptional);
                if (!typeSpecRowByClassType.TryGetValue(key,
                        out var reference))
                {
                    var classTypeSpecRow
                        = checked((uint)typeSpecRows.Count + 1);
                    var qualifiers = (byte)(0x02
                        | (isWritable ? 0x01 : 0));
                    typeSpecRows.Add(TrRmTypeSpecTableWriter.Reference(
                        0x02, checked((uint)typeDefRow), qualifiers));
                    reference = classTypeSpecRow;
                    if (isOptional)
                    {
                        reference = checked((uint)typeSpecRows.Count + 1);
                        typeSpecRows.Add(TrRmTypeSpecTableWriter.Wrapped(
                            0x06, classTypeSpecRow));
                    }

                    typeSpecRowByClassType.Add(key, reference);
                }

                return reference;
            }

            if (TryGetC1M1SourceStructFieldType(type,
                    sourceStructDeclarations,
                    out var structureIndex))
            {
                if (!typeDefRowByStructure.TryGetValue(structureIndex,
                        out var typeDefRow))
                {
                    throw new InvalidDataException(
                        "A captured source class FieldDef refers to an unretained local struct.");
                }

                if (!typeSpecRowByStructure.TryGetValue(structureIndex,
                        out var typeSpecRow))
                {
                    typeSpecRow
                        = checked((uint)typeSpecRows.Count + 1);
                    typeSpecRows.Add(TrRmTypeSpecTableWriter.Reference(0x02,
                        checked((uint)typeDefRow)));
                    typeSpecRowByStructure.Add(structureIndex, typeSpecRow);
                }

                return typeSpecRow;
            }

            return GetC1M1SourceScalarTypeSpecReference(type);
        }

        foreach (var (isClass, sourceIndex) in typeDefOrder)
        {
            if (isClass
                && program.Classes[sourceIndex].IsUnique is not false)
            {
                throw Unsupported(
                    "C1M1 class TypeDefs require known aliasable handle ownership.");
            }

            var declarationName = isClass
                ? program.Classes[sourceIndex].Name
                : program.Structures[sourceIndex].Name;
            var exportKeys = isClass
                ? exportKeysByClass[sourceIndex] : [];
            var typeFields = isClass
                ? program.Classes[sourceIndex].Fields
                : program.Structures[sourceIndex].Fields;
            var typeDefFlags = isClass
                ? (exportKeys.Count != 0 ? 0x02u
                    : program.Classes[sourceIndex].IsInternal ? 0x01u : 0x02u)
                    | 0x08u
                    | (program.Classes[sourceIndex].IsMutable ? 0x80u : 0u)
                : (program.Structures[sourceIndex].IsPrivate ? 0u
                    : program.Structures[sourceIndex].IsInternal ? 1u : 2u)
                    | 0x04u;
            var typeDefRow = typeDefRows.Count + 1;
            if (typeFields.IsDefault
                || (isClass && typeFields.IsEmpty)
                || (isClass && exportKeys.Count != 0
                    && program.Classes[sourceIndex].IsInternal))
            {
                throw Unsupported(
                    "C1M1 source class declarations require complete instance fields and public roots.");
            }
            var separator = declarationName.LastIndexOf('.');
            var namespaceName = !isClass
                ? sourceStructDeclarations[sourceIndex].Namespace
                : separator < 0 ? string.Empty
                    : declarationName[..separator];
            var typeName = !isClass
                ? sourceStructDeclarations[sourceIndex].Name
                : separator < 0 ? declarationName
                    : declarationName[(separator + 1)..];
            if (typeName.Length == 0
                || !sourceNames.Add((namespaceName, typeName)))
            {
                throw new InvalidDataException(
                    "Captured source class names are empty or duplicated.");
            }

            var fieldFirst = fieldRows.Count + 1;
            var layoutFieldFirst = layoutFieldRows.Count + 1;
            for (var fieldOrdinal = 0; fieldOrdinal < typeFields.Length;
                fieldOrdinal++)
            {
                var field = typeFields[fieldOrdinal];
                if (field is null || (field.IsPrivate && field.IsInternal)
                    || field.RequiresUnsafeCaller)
                {
                    throw Unsupported(
                        "C1M1 source class FieldDefs cannot discard conflicting access or unsafe-caller facts.");
                }

                var visibility = field.IsPrivate ? 0u
                    : field.IsInternal ? 1u : 2u;
                var fieldFlags = visibility
                    | (field.IsMutable ? 0x04u : 0u);
                var fieldDefRow = fieldRows.Count + 1;
                fieldRows.Add(new TrRmFieldDefRow(typeDefRow, 0,
                    FieldTypeSpecReference(field.Type), fieldFlags));
                fieldNames.Add(field.Name);
                if (isClass)
                {
                    fieldDefRowByClassAndIndex.Add(
                        (sourceIndex, fieldOrdinal), fieldDefRow);
                }

                layoutFieldRows.Add(new TrRmLayoutFieldRow(
                    layoutRows.Count + 1, fieldOrdinal, fieldDefRow));
            }

            if (isClass)
            {
                var exportDefinitionId = 0x1300_0000u
                    | checked((uint)typeDefRow);
                foreach (var exportKey in exportKeys)
                {
                    exportRows.Add((exportDefinitionId, exportKey));
                }
            }

            typeDefRows.Add(new TrRmTypeDefRow(0, 0, typeDefFlags,
                typeFields.IsEmpty ? 0 : fieldFirst, typeFields.Length, 0, 0,
                layoutRows.Count + 1, 1, 0, 0));
            typeDefNames.Add((namespaceName, typeName));
            layoutRows.Add(new TrRmLayoutRow(typeDefRow, 0, 0, 0, 0, 0,
                typeFields.IsEmpty ? 0 : layoutFieldFirst,
                typeFields.Length));
        }

        foreach (var classExport in sourceClassExports.Exports)
        {
            foreach (var memberExport in classExport.MemberExports)
            {
                if (memberExport.Kind != SourceClassMemberKind.Field)
                {
                    continue;
                }

                if (!fieldDefRowByClassAndIndex.TryGetValue((
                        classExport.Declaration.ClassIndex,
                        memberExport.FieldIndex), out var fieldDefRow))
                {
                    throw new InvalidDataException(
                        "A captured source class field ExportKey has no emitted FieldDef.");
                }

                exportRows.Add((
                    0x1400_0000u | checked((uint)fieldDefRow),
                    memberExport.ExportKey));
            }
        }

        var totalRows = (long)typeDefRows.Count + fieldRows.Count
            + layoutRows.Count + layoutFieldRows.Count
            + typeSpecRows.Count + exportRows.Count;
        if (typeDefRows.Count > 0xFF_FFFF
            || fieldRows.Count > 0xFF_FFFF
            || layoutRows.Count > 0xFF_FFFF
            || layoutFieldRows.Count > 0xFF_FFFF
            || typeSpecRows.Count > 0xFF_FFFF
            || exportRows.Count > 0xFF_FFFF
            || totalRows > SemanticVerifier.MaximumAnalysisVisits)
        {
            throw Unsupported(
                "The C1M1 source class declaration closure exceeds the checked metadata row limits.");
        }

        var textRows = new List<(string Text, int Encoding)>();
        var stringRows = new Dictionary<string, int>(StringComparer.Ordinal);
        int AddString(string text)
        {
            if (!stringRows.TryGetValue(text, out var row))
            {
                textRows.Add((text, 1));
                row = textRows.Count;
                stringRows.Add(text, row);
            }

            return row;
        }

        var moduleIdentityStringRow = AddString(moduleIdentity);
        foreach (var (namespaceName, typeName) in typeDefNames)
        {
            _ = AddString(namespaceName);
            _ = AddString(typeName);
        }

        foreach (var fieldName in fieldNames)
        {
            _ = AddString(fieldName);
        }

        foreach (var (_, exportKey) in exportRows)
        {
            _ = AddString(exportKey);
        }

        var stringBytes = TrRmStringTableWriter.EncodeTextRows(textRows);
        var strings = new TrRmStringTableReader(stringBytes,
            textRows.Count);
        for (var typeIndex = 0; typeIndex < typeDefRows.Count; typeIndex++)
        {
            typeDefRows[typeIndex] = typeDefRows[typeIndex] with {
                NamespaceStringRow
                    = stringRows[typeDefNames[typeIndex].Namespace],
                NameStringRow = stringRows[typeDefNames[typeIndex].Name],
            };
        }

        for (var fieldIndex = 0; fieldIndex < fieldRows.Count; fieldIndex++)
        {
            fieldRows[fieldIndex] = fieldRows[fieldIndex] with {
                NameStringRow = stringRows[fieldNames[fieldIndex]],
            };
        }

        var moduleBytes = TrRmModuleTableWriter.Encode([
            new TrRmModuleRow(moduleIdentityStringRow,
                moduleTargetRequirement, (uint)program.RuntimePolicy, 0,
                1, 1, 1, 1, 0, 0),
        ], strings, 0, typeDefRows.Count, 0, 0, 0);
        var modules = new TrRmModuleTableReader(moduleBytes, 1, strings,
            0, typeDefRows.Count, 0, 0, 0);
        var typeDefBytes = TrRmTypeDefTableWriter.Encode(typeDefRows,
            strings, modules, fieldRows.Count, 0, layoutRows.Count, 0,
            TrRmArtifactSchema.C1M1Draft);
        var typeDefs = new TrRmTypeDefTableReader(typeDefBytes,
            typeDefRows.Count, strings, modules, fieldRows.Count, 0,
            layoutRows.Count, 0, TrRmArtifactSchema.C1M1Draft);
        var typeSpecBytes = typeSpecRows.Count == 0 ? [] :
            TrRmTypeSpecTableWriter.Encode(typeSpecRows, typeDefRows.Count,
                0, 0, 0, TrRmArtifactSchema.C1M1Draft);
        var typeSpecs = new TrRmTypeSpecTableReader(
            typeSpecRows.Count == 0
                ? new byte[sizeof(uint)] : typeSpecBytes,
            typeSpecRows.Count, typeDefRows.Count, 0, 0, 0,
            TrRmArtifactSchema.C1M1Draft);
        var fieldBytes = TrRmFieldDefTableWriter.Encode(fieldRows, strings,
            typeDefs, typeSpecs);
        var fields = new TrRmFieldDefTableReader(fieldBytes,
            fieldRows.Count, strings, typeDefs, typeSpecs);
        var targetProfiles = new TrRmTargetProfileTableReader([], 0);
        var layoutBytes = TrRmLayoutTableWriter.Encode(layoutRows, typeDefs,
            targetProfiles, layoutFieldRows.Count);
        var layouts = new TrRmLayoutTableReader(layoutBytes,
            layoutRows.Count, typeDefs, targetProfiles,
            layoutFieldRows.Count);
        var layoutFieldBytes = TrRmLayoutFieldTableWriter.Encode(
            layoutFieldRows, layouts, typeDefs, fields);
        var exportBytes = EncodeC1M1Exports(exportRows,
            stringRows);

        var sections = new List<TrRmSection> {
            new TrRmSection(0x0001, 0, 1, moduleBytes),
            new TrRmSection(0x0002, 0, checked((uint)textRows.Count),
                stringBytes),
            new TrRmSection(0x0013, 0,
                checked((uint)typeDefRows.Count), typeDefBytes),
            new TrRmSection(0x0014, 0, checked((uint)fieldRows.Count),
                fieldBytes),
            new TrRmSection(0x0015, 0, checked((uint)layoutRows.Count),
                layoutBytes),
            new TrRmSection(0x001D, 0,
                checked((uint)exportRows.Count), exportBytes),
            new TrRmSection(0x0023, 0,
                checked((uint)layoutFieldRows.Count), layoutFieldBytes),
        };
        if (typeSpecRows.Count != 0)
        {
            sections.Add(new TrRmSection(0x0011, 0,
                checked((uint)typeSpecRows.Count), typeSpecBytes));
        }

        sections.Sort(static (left, right) =>
            left.Kind.CompareTo(right.Kind));
        var artifact = TrRmArtifactWriter.Encode(sections,
            TrRmArtifactSchema.C1M1Draft);
        _ = TrRmStructuralMetadataValidator
            .ValidateC1AndGetExecutionTables(
                TrRmArtifactIndex.ForC1M1Draft(artifact));
        return artifact;
    }

    private static uint GetC1M1SourceScalarTypeSpecReference(
        PrimitiveType type)
    {
        var typeCode = type switch {
            PrimitiveType.Bool => 0x01,
            PrimitiveType.Char8 => 0x02,
            PrimitiveType.Char16 => 0x03,
            PrimitiveType.Char32 => 0x04,
            PrimitiveType.Rune => 0x05,
            PrimitiveType.Int8 => 0x06,
            PrimitiveType.Int16 => 0x07,
            PrimitiveType.Int32 => 0x08,
            PrimitiveType.Int64 => 0x09,
            PrimitiveType.Int128 => 0x0A,
            PrimitiveType.UInt8 => 0x0B,
            PrimitiveType.UInt16 => 0x0C,
            PrimitiveType.UInt32 => 0x0D,
            PrimitiveType.UInt64 => 0x0E,
            PrimitiveType.UInt128 => 0x0F,
            PrimitiveType.NativeInt => 0x10,
            PrimitiveType.NativeUInt => 0x11,
            PrimitiveType.Float16 => 0x12,
            PrimitiveType.BFloat16 => 0x13,
            PrimitiveType.Float32 => 0x14,
            PrimitiveType.Float64 => 0x15,
            PrimitiveType.Float128 => 0x16,
            PrimitiveType.Decimal32 => 0x17,
            PrimitiveType.Decimal64 => 0x18,
            PrimitiveType.Decimal128 => 0x19,
            _ => throw Unsupported(
                $"C1M1 source class field type '{type}' has no supported local scalar TypeSpec."),
        };
        return 0xF200_0000u | checked((uint)typeCode);
    }

    private static bool TryGetC1M1SourceClassFieldType(
        PrimitiveType type, out int classIndex, out bool isWritable,
        out bool isOptional)
    {
        isOptional = ManagedClassTypes.IsOptional(type);
        var requiredType = isOptional
            ? ManagedClassTypes.Required(type) : type;
        if (!ManagedClassTypes.IsClass(requiredType))
        {
            classIndex = -1;
            isWritable = false;
            return false;
        }

        classIndex = ManagedClassTypes.ClassIndex(requiredType);
        isWritable = ManagedClassTypes.IsWritable(type);
        return true;
    }

    private static bool TryGetC1M1SourceStructFieldType(
        PrimitiveType type,
        Dictionary<int, BoundSourceTypeDeclaration> declarations,
        out int structureIndex)
    {
        var typeValue = (int)type;
        if (typeValue is >= 100 and < BoundEnumTypes.First)
        {
            structureIndex = typeValue - 100;
            return declarations.ContainsKey(structureIndex);
        }

        structureIndex = -1;
        return false;
    }

    private static bool IsC1M1SourceStructLayoutSupported(
        BoundStruct structure)
        => structure is not null
            && !structure.Fields.IsDefault
            && structure.LayoutKind == 0
            && structure.BlittableTargetRequirement == 0
            && structure.Packing == 0
            && structure.DeclaredAlignment is null
            && !structure.IsUnique
            && !(structure.IsPrivate && structure.IsInternal)
            && structure.CleanupMethod is null
            && structure.StandardTypeOrigin is null
            && structure.SourceGenericTypeDefinitionIndex < 0;

    private static byte[] EmitC1M1MetadataClassTypeReference(
        BoundProgram program, string moduleIdentity,
        uint moduleTargetRequirement)
    {
        if (program.EntryPoint != -1 || !program.Functions.IsEmpty
            || !program.Classes.IsEmpty || !program.Interfaces.IsEmpty
            || !program.InterfaceWitnesses.IsEmpty
            || !program.GenericMethodDefinitions.IsEmpty
            || !program.GenericMethodSignatures.IsEmpty
            || !program.SourceTypeDeclarations.IsEmpty
            || !program.SourceGenericTypeDefinitions.IsEmpty
            || !program.GenericNominalTypeInstances.IsEmpty
            || !program.EnumDefinitions.IsEmpty
            || !program.SourceGenericTypeInstances.IsEmpty
            || !program.NativeFunctionPointerSignatures.IsEmpty
            || !program.NativePointerTypes.IsEmpty
            || !program.NativeFixedArrayTypes.IsEmpty
            || !program.CheckedReferenceTypes.IsEmpty
            || !program.PrivateNominalTypeOrigins.IsEmpty
            || !program.PrivateClassReferences.IsEmpty
            || !program.StaticFields.IsEmpty
            || !program.StaticInitializationGroups.IsEmpty
            || !program.NativeUtf16ZLiterals.IsEmpty
            || !program.NativeUtf8ZLiterals.IsEmpty
            || !program.Buffers.IsEmpty
            || program.StandardMethodProviderClosure is not null
            || program.StandardFieldProviderClosure is not null
            || program.StandardQuerySourceAssociation is not null
            || program.NativeCoreRegistration is not null
            || program.NativeOutputCoreRegistration is not null
            || program.LayoutCoreRegistration is not null
            || program.StaticInitializationCoreRegistration is not null
            || program.LazyInitializationCoreRegistration is not null
            || program.CoreTaskType is not null
            || !program.CoreExceptionIdentitySelections.IsEmpty
            || !program.CoreContractFailureIdentitySelections.IsEmpty
            || program.CoreContractFailureIdentitySelection is not null
            || program.CoreEvaluationFormIdentitySelection is not null
            || !program.CoreEvaluationFormIdentitySelections.IsEmpty
            || !program.CoreEvaluationFormSchemaProviders.IsEmpty
            || program.RequiresSourceGrant
            || program.InvocationProfileId is not null
            || program.NativeLoweringAgreement is not null
            || !BoundGenericNominalTypeContract
                .IsValidDefinitionTable(
                    program.GenericNominalTypeDefinitions)
            || !BoundStandardClassDeclarationContract.IsValidTable(
                program.StandardClassDeclarations,
                program.GenericNominalTypeDefinitions))
        {
            throw Unsupported(
                "C1M1 metadata-only class re-emission requires a type-only class reference with complete checked carriers.");
        }

        SemanticVerifier.VerifyLibraryReferences(program);
        var rootDeclarations = program.StandardClassDeclarations
            .Where(static declaration =>
                declaration.Expectations.IsEmpty
                && !declaration.SelectedExportKeys.IsEmpty)
            .ToArray();
        if (rootDeclarations.Length == 0)
        {
            throw Unsupported(
                "C1M1 metadata-only class re-emission requires an explicitly exported class root.");
        }
        var classIdentities = program.StandardClassDeclarations
            .Select(static declaration => declaration.Declaration.Identity)
            .ToHashSet();
        if (program.StandardClassDeclarations.Any(declaration =>
                !program.GenericNominalTypeDefinitions.Any(definition =>
                    definition.Identity
                        == declaration.Declaration.Identity
                    && definition.NominalKind == 2))
            || program.GenericNominalTypeDefinitions.Any(definition =>
                definition.NominalKind == 1
                    ? definition.ProviderExpectations.IsEmpty
                    : definition.NominalKind != 2
                        || !classIdentities.Contains(definition.Identity)))
        {
            throw Unsupported(
                "C1M1 class re-emission cannot discard or invent a nominal definition.");
        }

        var rootSnapshot = rootDeclarations[0].Declaration.Provider;
        if (!StringComparer.Ordinal.Equals(rootSnapshot.SchemaIdentity,
                StandardC1M1SchemaIdentity)
            || rootSnapshot.ModuleRow <= 0
            || !StringComparer.Ordinal.Equals(rootSnapshot.ModuleIdentity,
                rootDeclarations[0].Declaration.Identity.ModuleIdentity)
            || rootSnapshot.TargetRequirement != moduleTargetRequirement
            || rootSnapshot.RuntimePolicy != (uint)program.RuntimePolicy)
        {
            throw Unsupported(
                "C1M1 metadata-only class re-emission must retain one exact root Module contract.");
        }

        foreach (var declaration in rootDeclarations)
        {
            if (!SourceTypeSnapshotsMatch(rootSnapshot,
                    declaration.Declaration.Provider))
            {
                throw Unsupported(
                    "C1M1 class roots from multiple source Modules cannot be combined into one output Module.");
            }
        }

        var providerInputs = new List<TrRmStandardArtifactInput>();
        void AddProviderInput(BoundSourceTypeProviderSnapshot provider)
        {
            if (!StringComparer.Ordinal.Equals(provider.SchemaIdentity,
                    StandardC1M1SchemaIdentity)
                || provider.ArtifactSnapshot.IsDefaultOrEmpty)
            {
                throw new InvalidDataException(
                    "A retained C1M1 type expectation has an invalid provider snapshot.");
            }

            if (provider.ArtifactSnapshot.AsSpan().SequenceEqual(
                    rootSnapshot.ArtifactSnapshot.AsSpan()))
            {
                return;
            }

            foreach (var providerInput in providerInputs)
            {
                if (providerInput.Artifact.Span.SequenceEqual(
                        provider.ArtifactSnapshot.AsSpan()))
                {
                    return;
                }
            }

            providerInputs.Add(new TrRmStandardArtifactInput(
                StandardC1M1SchemaIdentity,
                provider.ArtifactSnapshot.ToArray()));
        }

        foreach (var declaration in program.StandardClassDeclarations)
        {
            foreach (var expectation in declaration.Expectations)
            {
                AddProviderInput(expectation.Binding.Provider);
            }
        }

        foreach (var definition in program.GenericNominalTypeDefinitions)
        {
            foreach (var expectation in definition.ProviderExpectations)
            {
                AddProviderInput(expectation.Provider);
            }
        }

        var artifacts = new byte[providerInputs.Count + 1][];
        var schemaIdentities = new string[artifacts.Length];
        artifacts[0] = rootSnapshot.ArtifactSnapshot.ToArray();
        schemaIdentities[0] = StandardC1M1SchemaIdentity;
        for (var index = 0; index < providerInputs.Count; index++)
        {
            artifacts[index + 1] = providerInputs[index].Artifact.ToArray();
            schemaIdentities[index + 1] = providerInputs[index].SchemaIdentity;
        }

        var moduleValidation = ValidateStandardInputModules(artifacts,
            schemaIdentities, rootSnapshot.ModuleIdentity,
            a3LayoutResourceLedger: null);
        var rootModuleRow = moduleValidation.RootModuleRow;
        if (rootModuleRow != rootSnapshot.ModuleRow)
        {
            throw new InvalidDataException(
                "The retained C1M1 class root Module row changed.");
        }
        var rootIndex
            = TrRmArtifactIndex.ForC1M1Draft(artifacts[0]);
        var rootTables = TrRmStructuralMetadataValidator
            .ValidateC1AndGetExecutionTables(rootIndex);
        EnsureC1ClassDeclarationTablesSupported(rootTables);
        var typeProviderBindings
            = TrRmFixedArrayProviderBindingValidator
                .ValidateC1M1TypeDefImports(rootTables, artifacts,
                    schemaIdentities, moduleValidation.Modules,
                    rootModuleRow);
        ValidateC1M1ClassCarrierBindings(program, moduleValidation.Modules,
            artifacts, typeProviderBindings);
        ValidateC1M1StructCarrierBindings(program, moduleValidation.Modules,
            artifacts, typeProviderBindings);
        if (!StringComparer.Ordinal.Equals(
                rootTables.Modules.GetIdentity(rootModuleRow),
                rootSnapshot.ModuleIdentity)
            || rootTables.Modules.GetRequirements(rootModuleRow)
                != (rootSnapshot.TargetRequirement,
                    rootSnapshot.RuntimePolicy)
            || rootTables.Modules.GetGoverningRuleEdition(rootModuleRow)
                != rootSnapshot.GoverningRuleEdition
            || rootTables.Modules.GetEntryMethod(rootModuleRow) != 0
            || rootTables.Modules.GetMethodDefRange(rootModuleRow).Start
                != rootTables.Modules.GetMethodDefRange(rootModuleRow).End)
        {
            throw new InvalidDataException(
                "The retained C1M1 class root snapshot is inconsistent.");
        }

        var typeViewsByOldRow
            = new SortedDictionary<int, BoundStandardClassTypeView>();
        var exportsByOldRow
            = new Dictionary<int, ImmutableArray<string>>();
        foreach (var declaration in rootDeclarations)
        {
            var view = declaration.Declaration;
            var typeDefRow = GetTypeDefRow(view.Identity.DefinitionId);
            if (rootTables.TypeDefs.GetOwnerModule(typeDefRow)
                    != rootModuleRow
                || rootTables.TypeDefs.GetKind(typeDefRow) != 2
                || rootTables.TypeDefs.GetGenericRange(typeDefRow).Count != 0
                || rootTables.TypeDefs.GetContractRange(typeDefRow).Count
                    != 0)
            {
                throw Unsupported(
                    "C1M1 metadata-only class re-emission supports nongeneric class roots without TypeDef contracts.");
            }

            var parsedView = CreateStandardClassTypeView(rootTables,
                view.Identity, rootSnapshot, typeDefRow);
            if (!StandardClassViewsMatch(view, parsedView)
                || !GetStandardTypeExportKeys(rootTables, rootModuleRow,
                        typeDefRow).AsSpan()
                    .SequenceEqual(declaration.ExportKeys.AsSpan()))
            {
                throw new InvalidDataException(
                    "The retained C1M1 class root does not match its checked snapshot.");
            }

            if (!typeViewsByOldRow.TryAdd(typeDefRow, parsedView)
                || !exportsByOldRow.TryAdd(typeDefRow,
                    declaration.ExportKeys))
            {
                throw new InvalidDataException(
                    "The C1M1 class reference contains duplicate root TypeDefs.");
            }
        }

        var bindingsByImportRow = new SortedDictionary<int,
            BoundSourceTypeExpectation>();
        foreach (var declaration in program.StandardClassDeclarations)
        {
            foreach (var expectation in declaration.Expectations)
            {
                var binding = expectation.Binding;
                if (!SourceTypeSnapshotsMatch(binding.UsingModule,
                        rootSnapshot))
                {
                    continue;
                }

                var expectedTypeDefRow = GetTypeDefRow(
                    binding.ExpectedDefinitionId);
                var parsedExpectedView = CreateStandardClassTypeView(
                    rootTables, expectation.ExpectedDeclaration.Identity,
                    rootSnapshot, expectedTypeDefRow);
                if (rootTables.TypeDefs.GetOwnerModule(expectedTypeDefRow)
                        != rootModuleRow
                    || rootTables.TypeDefs.GetKind(expectedTypeDefRow) != 2
                    || rootTables.TypeDefs.GetGenericRange(
                        expectedTypeDefRow).Count != 0
                    || rootTables.TypeDefs.GetContractRange(
                        expectedTypeDefRow).Count != 0
                    || !StandardClassViewsMatch(
                        expectation.ExpectedDeclaration,
                        parsedExpectedView)
                    || !bindingsByImportRow.TryAdd(binding.ImportRow,
                        binding))
                {
                    throw new InvalidDataException(
                        "A retained C1M1 class Import expectation does not match its root snapshot.");
                }

                if (typeViewsByOldRow.TryGetValue(expectedTypeDefRow,
                        out var existingView))
                {
                    if (!StandardClassViewsMatch(existingView,
                            parsedExpectedView))
                    {
                        throw new InvalidDataException(
                            "One C1M1 expected TypeDef has conflicting class views.");
                    }
                }
                else
                {
                    typeViewsByOldRow.Add(expectedTypeDefRow,
                        parsedExpectedView);
                    exportsByOldRow.Add(expectedTypeDefRow, []);
                }
            }
        }

        var typeDefViewsByOldRow
            = new SortedDictionary<int, C1M1TypeDefEmissionView>();
        foreach (var (typeDefRow, view) in typeViewsByOldRow)
        {
            typeDefViewsByOldRow.Add(typeDefRow,
                ToC1M1TypeDefEmissionView(rootTables, typeDefRow, view));
        }
        foreach (var definition in program.GenericNominalTypeDefinitions)
        {
            if (definition.NominalKind != 1)
            {
                continue;
            }

            foreach (var expectation in definition.ProviderExpectations)
            {
                if (!SourceTypeSnapshotsMatch(expectation.UsingModule,
                        rootSnapshot))
                {
                    continue;
                }

                var expectedTypeDefRow = GetTypeDefRow(
                    expectation.ExpectedDefinitionId);
                if (rootTables.TypeDefs.GetOwnerModule(expectedTypeDefRow)
                        != rootModuleRow
                    || rootTables.TypeDefs.GetKind(expectedTypeDefRow) != 1
                    || rootTables.TypeDefs.GetGenericRange(
                        expectedTypeDefRow).Count != 0
                    || rootTables.TypeDefs.GetContractRange(
                        expectedTypeDefRow).Count != 0
                    || !bindingsByImportRow.TryAdd(expectation.ImportRow,
                        expectation))
                {
                    throw new InvalidDataException(
                        "A retained C1M1 struct Import expectation does not match its root snapshot.");
                }

                var typeDefView = CreateC1M1TypeDefEmissionView(
                    rootTables, rootSnapshot, expectedTypeDefRow);
                if (typeDefView.Kind != 1
                    || !typeDefViewsByOldRow.TryAdd(expectedTypeDefRow,
                        typeDefView)
                    || !exportsByOldRow.TryAdd(expectedTypeDefRow, []))
                {
                    throw new InvalidDataException(
                        "A retained C1M1 struct Import TypeDef is duplicated.");
                }
            }
        }
        AddC1M1LocalNonGenericTypeClosure(rootTables, rootModuleRow,
            rootSnapshot, typeDefViewsByOldRow, exportsByOldRow);

        var rootImportCount = 0;
        for (var importRow = 1; importRow <= rootTables.Imports.Count;
            importRow++)
        {
            var moduleRefRow
                = rootTables.Imports.GetModuleRefForImportRow(importRow);
            if (rootTables.ModuleRefs.GetOwnerModule(moduleRefRow)
                != rootModuleRow)
            {
                continue;
            }

            rootImportCount++;
            if ((rootTables.Imports.GetExpectedDefinitionForImportRow(
                    importRow) >> 24) != 0x13)
            {
                throw new NotSupportedException(
                    "C1M1 class re-emission only retains TypeDef Imports.");
            }
        }

        if (typeDefViewsByOldRow.Count == 0
            || bindingsByImportRow.Count != rootImportCount)
        {
            throw new InvalidDataException(
                "The retained C1M1 class provider closure is incomplete.");
        }

        var moduleRefStartEnd
            = rootTables.Modules.GetModuleRefRange(rootModuleRow);
        var moduleRefRowsByOldRow = new SortedSet<int>();
        foreach (var (importRow, binding) in bindingsByImportRow)
        {
            if (importRow <= 0 || importRow > rootTables.Imports.Count
                || (rootTables.Imports
                        .GetExpectedDefinitionForImportRow(importRow) >> 24)
                    != 0x13
                || rootTables.Imports.GetExpectedDefinitionForImportRow(
                    importRow) != binding.ExpectedDefinitionId
                || !StringComparer.Ordinal.Equals(
                    rootTables.Imports.GetBindingForImportRow(importRow)
                        .ExportKey, binding.ExportKey)
                || !StringComparer.Ordinal.Equals(binding.ProducerIdentity,
                    rootTables.ModuleRefs.GetProducerIdentity(
                        rootTables.Imports.GetModuleRefForImportRow(
                            importRow)))
                || binding.TargetRequirement
                    != rootTables.ModuleRefs.GetRequirements(
                        rootTables.Imports.GetModuleRefForImportRow(
                            importRow)).TargetRequirement
                || binding.RuntimePolicy
                    != rootTables.ModuleRefs.GetRequirements(
                        rootTables.Imports.GetModuleRefForImportRow(
                            importRow)).RuntimePolicy)
            {
                throw new InvalidDataException(
                    "A retained C1M1 class Import no longer matches its selected provider.");
            }

            var moduleRefRow
                = rootTables.Imports.GetModuleRefForImportRow(importRow);
            _ = moduleRefRowsByOldRow.Add(moduleRefRow);
        }

        if (moduleRefRowsByOldRow.Count
                != moduleRefStartEnd.End - moduleRefStartEnd.Start
            || moduleRefRowsByOldRow.Any(moduleRefRow =>
                moduleRefRow < moduleRefStartEnd.Start
                || moduleRefRow >= moduleRefStartEnd.End))
        {
            throw new InvalidDataException(
                "The retained C1M1 class ModuleRef range is incomplete.");
        }

        var typeDefRowMap = new Dictionary<int, int>();
        var nextTypeDefRow = 0;
        foreach (var oldTypeDefRow in typeDefViewsByOldRow.Keys)
        {
            typeDefRowMap.Add(oldTypeDefRow, ++nextTypeDefRow);
        }

        var importRowMap = new Dictionary<int, int>();
        var nextImportRow = 0;
        foreach (var oldImportRow in bindingsByImportRow.Keys)
        {
            importRowMap.Add(oldImportRow, ++nextImportRow);
        }

        var moduleRefRowMap = new Dictionary<int, int>();
        var nextModuleRefRow = 0;
        foreach (var oldModuleRefRow in moduleRefRowsByOldRow)
        {
            moduleRefRowMap.Add(oldModuleRefRow, ++nextModuleRefRow);
        }

        var outputTypeSpecRows
            = new List<ReadOnlyMemory<byte>>();
        var typeSpecRowMap = new Dictionary<int, uint>();
        var activeTypeSpecRows = new HashSet<int>();
        var fieldRows = new List<TrRmFieldDefRow>();
        var layoutRows = new List<TrRmLayoutRow>();
        var layoutFieldRows = new List<TrRmLayoutFieldRow>();
        var outputTypeDefRows = new List<TrRmTypeDefRow>();
        var typeDefNames = new List<(string Namespace, string Name)>();
        var fieldRowMap = new Dictionary<int, int>();
        var fieldNames = new List<string>();
        var exportRows = new List<(uint DefinitionId, string Key)>();
        var exportKeys = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (oldTypeDefRow, view) in typeDefViewsByOldRow)
        {
            if (view.Fields.Length > 0xFF_FFFF
                || view.Layouts.IsEmpty
                || view.Layouts.Length > 0xFF_FFFF)
            {
                throw Unsupported(
                    "The C1M1 TypeDef closure exceeds the metadata row limits.");
            }

            var newTypeDefRow = typeDefRowMap[oldTypeDefRow];
            var fieldFirst = view.Fields.IsEmpty ? 0
                : fieldRows.Count + 1;
            foreach (var field in view.Fields)
            {
                if (GetTypeDefRow(field.OwnerDefinitionId)
                        != oldTypeDefRow
                    || (field.DefinitionId >> 24) != 0x14)
                {
                    throw new InvalidDataException(
                        "A retained C1M1 TypeDef FieldDef has an invalid owner.");
                }

                var oldFieldRow = GetFieldDefRow(field.DefinitionId);
                if (!fieldRowMap.TryAdd(oldFieldRow,
                        fieldRows.Count + 1))
                {
                    throw new InvalidDataException(
                        "A retained C1M1 TypeDef FieldDef is assigned more than once.");
                }

                var remappedTypeSpec
                    = RemapC1M1ClassTypeSpec(rootTables,
                        field.TypeSpecReference, typeDefRowMap,
                        importRowMap, outputTypeSpecRows, typeSpecRowMap,
                        activeTypeSpecRows, depth: 0);
                fieldRows.Add(new TrRmFieldDefRow(newTypeDefRow,
                    0, remappedTypeSpec, field.Flags));
                fieldNames.Add(field.Name);
            }

            var layoutFirst = layoutRows.Count + 1;
            foreach (var layout in view.Layouts)
            {
                var newLayoutRow = layoutRows.Count + 1;
                var layoutFieldFirst = layout.FieldDefinitionIds.IsEmpty
                    ? 0 : layoutFieldRows.Count + 1;
                for (var ordinal = 0;
                    ordinal < layout.FieldDefinitionIds.Length; ordinal++)
                {
                    var oldFieldRow = GetFieldDefRow(
                        layout.FieldDefinitionIds[ordinal]);
                    if (!fieldRowMap.TryGetValue(oldFieldRow,
                            out var newFieldRow))
                    {
                        throw new InvalidDataException(
                            "A retained C1M1 Layout refers to an unowned FieldDef.");
                    }

                    layoutFieldRows.Add(new TrRmLayoutFieldRow(
                        newLayoutRow, ordinal, newFieldRow));
                }

                layoutRows.Add(new TrRmLayoutRow(newTypeDefRow,
                    layout.TargetProfileReference, layout.Kind,
                    layout.Flags, layout.Alignment, layout.Pack,
                    layoutFieldFirst,
                    layout.FieldDefinitionIds.Length));
            }

            var exportKeysForType = exportsByOldRow[oldTypeDefRow];
            foreach (var exportKey in exportKeysForType)
            {
                if (!exportKeys.Add(exportKey))
                {
                    throw new InvalidDataException(
                        "C1M1 class ExportKeys must be unique within the Module.");
                }

                exportRows.Add((
                    0x1300_0000u | checked((uint)newTypeDefRow),
                    exportKey));
            }

            outputTypeDefRows.Add(new TrRmTypeDefRow(0, 0, view.TypeDefFlags,
                fieldFirst, view.Fields.Length,
                0, 0, layoutFirst, view.Layouts.Length,
                0, 0));
            typeDefNames.Add((view.Namespace, view.Name));
        }

        foreach (var (definitionId, exportKey) in
            rootTables.Exports.GetExportsForModule(rootModuleRow))
        {
            if ((definitionId >> 24) != 0x14)
            {
                continue;
            }

            var oldFieldRow = GetFieldDefRow(definitionId);
            var oldOwnerTypeDefRow = rootTables.Fields
                .GetOwnerTypeDef(oldFieldRow);
            if (!exportsByOldRow.TryGetValue(oldOwnerTypeDefRow,
                    out var ownerExportKeys)
                || ownerExportKeys.IsEmpty)
            {
                continue;
            }

            if (!fieldRowMap.TryGetValue(oldFieldRow,
                    out var newFieldRow)
                || !exportKeys.Add(exportKey))
            {
                throw new InvalidDataException(
                    "A retained C1M1 class FieldDef ExportKey cannot be remapped.");
            }

            exportRows.Add((
                0x1400_0000u | checked((uint)newFieldRow), exportKey));
        }

        var featureRows = rootSnapshot.Features
            .Select(static feature => new TrRmFeatureRow(feature.FeatureId,
                feature.RuleRevision))
            .ToArray();

        var textRows = new List<(string Text, int Encoding)>();
        var stringRows = new Dictionary<string, int>(StringComparer.Ordinal);
        int AddString(string text)
        {
            if (!stringRows.TryGetValue(text, out var row))
            {
                textRows.Add((text, 1));
                row = textRows.Count;
                stringRows.Add(text, row);
            }

            return row;
        }

        var moduleIdentityStringRow = AddString(moduleIdentity);
        foreach (var (oldTypeDefRow, view) in typeDefViewsByOldRow)
        {
            _ = oldTypeDefRow;
            AddString(view.Namespace);
            AddString(view.Name);
        }

        foreach (var fieldName in fieldNames)
        {
            AddString(fieldName);
        }
        foreach (var moduleRefRow in moduleRefRowsByOldRow)
        {
            AddString(rootTables.ModuleRefs.GetProducerIdentity(moduleRefRow));
        }
        foreach (var binding in bindingsByImportRow.Values)
        {
            AddString(binding.ExportKey);
        }
        foreach (var exportRow in exportRows)
        {
            AddString(exportRow.Key);
        }

        var stringBytes = TrRmStringTableWriter.EncodeTextRows(textRows);
        var strings = new TrRmStringTableReader(stringBytes,
            textRows.Count);
        for (var typeIndex = 0;
            typeIndex < outputTypeDefRows.Count; typeIndex++)
        {
            outputTypeDefRows[typeIndex] = outputTypeDefRows[typeIndex] with {
                NamespaceStringRow
                    = stringRows[typeDefNames[typeIndex].Namespace],
                NameStringRow = stringRows[typeDefNames[typeIndex].Name],
            };
        }
        for (var fieldIndex = 0; fieldIndex < fieldRows.Count; fieldIndex++)
        {
            fieldRows[fieldIndex] = fieldRows[fieldIndex] with {
                NameStringRow = stringRows[fieldNames[fieldIndex]],
            };
        }

        var outputTypeSpecBytes = outputTypeSpecRows.Count == 0
            ? []
            : TrRmTypeSpecTableWriter.Encode(outputTypeSpecRows,
                outputTypeDefRows.Count, importRowMap.Count, 0, 0,
                TrRmArtifactSchema.C1M1Draft);
        var typeSpecReaderBytes = outputTypeSpecRows.Count == 0
            ? new byte[sizeof(uint)] : outputTypeSpecBytes;
        var typeSpecs = new TrRmTypeSpecTableReader(typeSpecReaderBytes,
            outputTypeSpecRows.Count, outputTypeDefRows.Count,
            importRowMap.Count, 0, 0, TrRmArtifactSchema.C1M1Draft);

        var targetProfileBytes = GetTargetProfileSection(rootIndex,
            out var targetProfileCount);
        var targetProfiles = targetProfileBytes.Length == 0
            ? new TrRmTargetProfileTableReader([], 0)
            : new TrRmTargetProfileTableReader(targetProfileBytes,
                targetProfileCount);
        var featureBytes = TrRmFeatureTableWriter.Encode(featureRows,
            TrRmArtifactSchema.C1M1Draft);
        var moduleBytes = TrRmModuleTableWriter.Encode([
            new TrRmModuleRow(moduleIdentityStringRow,
                moduleTargetRequirement, rootSnapshot.RuntimePolicy, 0,
                1, 1, 1, 1,
                featureRows.Length == 0 ? 0 : 1,
                featureRows.Length, rootSnapshot.GoverningRuleEdition),
        ], strings, moduleRefRowMap.Count, outputTypeDefRows.Count, 0, 0,
            featureRows.Length);
        var moduleTables = new TrRmModuleTableReader(moduleBytes, 1,
            strings, moduleRefRowMap.Count, outputTypeDefRows.Count, 0, 0,
            featureRows.Length);
        var typeDefBytes = TrRmTypeDefTableWriter.Encode(outputTypeDefRows,
            strings, moduleTables, fieldRows.Count, 0, layoutRows.Count, 0,
            TrRmArtifactSchema.C1M1Draft);
        var typeDefs = new TrRmTypeDefTableReader(typeDefBytes,
            outputTypeDefRows.Count, strings, moduleTables, fieldRows.Count,
            0, layoutRows.Count, 0, TrRmArtifactSchema.C1M1Draft);

        var layoutFieldCount = layoutFieldRows.Count;
        var fieldBytes = fieldRows.Count == 0 ? []
            : TrRmFieldDefTableWriter.Encode(fieldRows, strings, typeDefs,
                typeSpecs);
        var fields = new TrRmFieldDefTableReader(fieldBytes,
            fieldRows.Count, strings, typeDefs, typeSpecs);
        var layoutBytes = TrRmLayoutTableWriter.Encode(layoutRows,
            typeDefs, targetProfiles, layoutFieldCount);
        var layouts = new TrRmLayoutTableReader(layoutBytes,
            layoutRows.Count, typeDefs, targetProfiles, layoutFieldCount);
        var layoutFieldBytes = layoutFieldCount == 0 ? []
            : TrRmLayoutFieldTableWriter.Encode(layoutFieldRows, layouts,
                typeDefs, fields);

        var moduleRefRows = new TrRmModuleRefRow[moduleRefRowMap.Count];
        foreach (var (oldModuleRefRow, newModuleRefRow)
            in moduleRefRowMap)
        {
            var producerIdentity = rootTables.ModuleRefs
                .GetProducerIdentity(oldModuleRefRow);
            var requirements = rootTables.ModuleRefs.GetRequirements(
                oldModuleRefRow);
            moduleRefRows[newModuleRefRow - 1] = new TrRmModuleRefRow(
                stringRows[producerIdentity], requirements.TargetRequirement,
                requirements.RuntimePolicy);
        }
        var moduleRefBytes = moduleRefRows.Length == 0 ? []
            : TrRmModuleRefTableWriter.Encode(moduleRefRows, strings,
                moduleTables);
        var importBytes = bindingsByImportRow.Count == 0 ? []
            : EncodeC1M1ClassImports(bindingsByImportRow, importRowMap,
                moduleRefRowMap, typeDefRowMap, stringRows,
                rootTables);
        var exportBytes = exportRows.Count == 0 ? []
            : EncodeC1M1Exports(exportRows, stringRows);

        var sections = new List<TrRmSection> {
            new(0x0001, 0, 1, moduleBytes),
            new(0x0002, 0, checked((uint)textRows.Count), stringBytes),
            new(0x0013, 0, checked((uint)outputTypeDefRows.Count),
                typeDefBytes),
            new(0x0015, 0, checked((uint)layoutRows.Count), layoutBytes),
        };
        if (featureRows.Length != 0)
        {
            sections.Add(new TrRmSection(0x0004, 0,
                checked((uint)featureRows.Length), featureBytes));
        }
        if (targetProfileBytes.Length != 0)
        {
            sections.Add(new TrRmSection(0x0003, 0,
                checked((uint)targetProfileCount), targetProfileBytes));
        }
        if (moduleRefRows.Length != 0)
        {
            sections.Add(new TrRmSection(0x0010, 0,
                checked((uint)moduleRefRows.Length), moduleRefBytes));
        }
        if (outputTypeSpecRows.Count != 0)
        {
            sections.Add(new TrRmSection(0x0011, 0,
                checked((uint)outputTypeSpecRows.Count),
                outputTypeSpecBytes));
        }
        if (fieldRows.Count != 0)
        {
            sections.Add(new TrRmSection(0x0014, 0,
                checked((uint)fieldRows.Count), fieldBytes));
        }
        if (layoutFieldCount != 0)
        {
            sections.Add(new TrRmSection(0x0023, 0,
                checked((uint)layoutFieldCount), layoutFieldBytes));
        }
        if (importBytes.Length != 0)
        {
            sections.Add(new TrRmSection(0x001C, 0,
                checked((uint)bindingsByImportRow.Count), importBytes));
        }
        if (exportBytes.Length != 0)
        {
            sections.Add(new TrRmSection(0x001D, 0,
                checked((uint)exportRows.Count), exportBytes));
        }

        sections.Sort(static (left, right) =>
            left.Kind.CompareTo(right.Kind));
        var artifact = TrRmArtifactWriter.Encode(sections,
            TrRmArtifactSchema.C1M1Draft);
        _ = TrRmStructuralMetadataValidator
            .ValidateC1AndGetExecutionTables(
                TrRmArtifactIndex.ForC1M1Draft(artifact));
        return artifact;
    }

    private static void ValidateC1M1ClassCarrierBindings(
        BoundProgram program,
        ImmutableArray<TrRmStandardModuleSelection> modules,
        byte[][] artifacts,
        ImmutableArray<TrRmStandardTypeProviderBinding> typeProviderBindings)
    {
        var expectationsByImport = new Dictionary<
            (string ModuleIdentity, int ModuleRow, int ImportRow),
            BoundStandardClassExpectation>();
        foreach (var declaration in program.StandardClassDeclarations)
        {
            foreach (var expectation in declaration.Expectations)
            {
                var binding = expectation.Binding;
                var usingModule = modules.Single(module =>
                    StringComparer.Ordinal.Equals(module.Identity,
                        binding.UsingModule.ModuleIdentity)
                    && module.ModuleRow == binding.UsingModule.ModuleRow);
                var usingTables = GetC1M1Tables(artifacts,
                    usingModule.ArtifactIndex);
                EnsureC1ClassDeclarationTablesSupported(usingTables);
                var usingSnapshot = CreateA2SourceTypeProviderSnapshot(
                    usingTables, artifacts[usingModule.ArtifactIndex],
                    StandardC1M1SchemaIdentity, usingModule.ModuleRow);
                var expectedTypeDefRow
                    = GetTypeDefRow(binding.ExpectedDefinitionId);
                var expectedModuleRefRow = usingTables.Imports
                    .GetModuleRefForImportRow(binding.ImportRow);
                var importBinding = usingTables.Imports
                    .GetBindingForImportRow(binding.ImportRow);
                var moduleRefRequirements = usingTables.ModuleRefs
                    .GetRequirements(expectedModuleRefRow);
                if (!SourceTypeSnapshotsMatch(binding.UsingModule,
                        usingSnapshot)
                    || usingTables.TypeDefs.GetOwnerModule(
                        expectedTypeDefRow) != usingModule.ModuleRow
                    || usingTables.TypeDefs.GetKind(expectedTypeDefRow) != 2
                    || usingTables.Imports
                        .GetExpectedDefinitionForImportRow(binding.ImportRow)
                        != binding.ExpectedDefinitionId
                    || !StringComparer.Ordinal.Equals(
                        importBinding.ProducerIdentity,
                        binding.ProducerIdentity)
                    || !StringComparer.Ordinal.Equals(importBinding.ExportKey,
                        binding.ExportKey)
                    || importBinding.TargetRequirement
                        != binding.TargetRequirement
                    || importBinding.RuntimePolicy != binding.RuntimePolicy
                    || moduleRefRequirements
                        != (binding.TargetRequirement, binding.RuntimePolicy)
                    || !expectationsByImport.TryAdd((
                        binding.UsingModule.ModuleIdentity,
                        binding.UsingModule.ModuleRow, binding.ImportRow),
                        expectation))
                {
                    throw new InvalidDataException(
                        "A retained C1M1 class Import view no longer matches its using Module.");
                }

                var expectedView = CreateStandardClassTypeView(usingTables,
                    expectation.ExpectedDeclaration.Identity,
                    usingSnapshot, expectedTypeDefRow);
                if (!StandardClassViewsMatch(
                        expectation.ExpectedDeclaration, expectedView))
                {
                    throw new InvalidDataException(
                        "A retained C1M1 class expectation no longer matches its TypeDef.");
                }

                var providerModule = modules.Single(module =>
                    StringComparer.Ordinal.Equals(module.Identity,
                        binding.ProducerIdentity)
                    && module.ModuleRow == binding.Provider.ModuleRow);
                var providerTables = GetC1M1Tables(artifacts,
                    providerModule.ArtifactIndex);
                EnsureC1ClassDeclarationTablesSupported(providerTables);
                var providerSnapshot = CreateA2SourceTypeProviderSnapshot(
                    providerTables, artifacts[providerModule.ArtifactIndex],
                    StandardC1M1SchemaIdentity, providerModule.ModuleRow);
                var providerTypeDefRow
                    = GetTypeDefRow(binding.ProviderDefinitionId);
                if (!SourceTypeSnapshotsMatch(binding.Provider,
                        providerSnapshot)
                    || providerTables.TypeDefs.GetOwnerModule(
                        providerTypeDefRow) != providerModule.ModuleRow
                    || providerTables.TypeDefs.GetKind(providerTypeDefRow) != 2
                    || !providerTables.Exports.TryGetDefinitionForKey(
                        providerModule.ModuleRow, binding.ExportKey,
                        out var providerDefinition)
                    || providerDefinition != binding.ProviderDefinitionId)
                {
                    throw new InvalidDataException(
                        "A retained C1M1 class provider selection no longer matches its Module and ExportKey.");
                }

                var providerIdentity = new BoundGenericNominalTypeIdentity(
                    providerModule.Identity, providerModule.Identity,
                    binding.ProviderDefinitionId);
                var actualDeclaration = program.StandardClassDeclarations
                    .SingleOrDefault(candidate =>
                        candidate.Declaration.Identity == providerIdentity);
                if (actualDeclaration is null
                    || !SourceTypeSnapshotsMatch(
                        actualDeclaration.Declaration.Provider,
                        providerSnapshot))
                {
                    throw new InvalidDataException(
                        "A retained C1M1 class provider declaration is missing.");
                }

                var actualView = CreateStandardClassTypeView(providerTables,
                    providerIdentity, providerSnapshot,
                    providerTypeDefRow);
                if (!StandardClassViewsMatch(
                        actualDeclaration.Declaration, actualView)
                    || !GetStandardTypeExportKeys(providerTables,
                            providerModule.ModuleRow, providerTypeDefRow)
                        .AsSpan().SequenceEqual(
                            actualDeclaration.ExportKeys.AsSpan()))
                {
                    throw new InvalidDataException(
                        "A retained C1M1 class provider view no longer matches its TypeDef.");
                }
            }
        }

        var boundImports = new HashSet<(
            string ModuleIdentity, int ModuleRow, int ImportRow)>();
        foreach (var binding in typeProviderBindings)
        {
            var consumerModule = modules.Single(module =>
                module.ArtifactIndex == binding.ConsumerArtifactIndex
                && module.ModuleRow == binding.ConsumerModuleRow);
            var consumerTables = GetC1M1Tables(artifacts,
                consumerModule.ArtifactIndex);
            if (consumerTables.TypeDefs.GetKind(
                    binding.ExpectedTypeDefRow) != 2)
            {
                continue;
            }

            var providerModule = modules.Single(module =>
                module.ArtifactIndex == binding.ProviderArtifactIndex
                && module.ModuleRow == binding.ProviderModuleRow);
            var key = (consumerModule.Identity, binding.ConsumerModuleRow,
                binding.ImportRow);
            if (!expectationsByImport.TryGetValue(key, out var expectation)
                || !boundImports.Add(key)
                || binding.ExpectedTypeDefRow
                    != GetTypeDefRow(
                        expectation.Binding.ExpectedDefinitionId)
                || binding.ProviderTypeDefRow
                    != GetTypeDefRow(
                        expectation.Binding.ProviderDefinitionId)
                || binding.ProviderArtifactIndex
                    != providerModule.ArtifactIndex
                || !StringComparer.Ordinal.Equals(binding.ProducerIdentity,
                    expectation.Binding.ProducerIdentity)
                || !StringComparer.Ordinal.Equals(binding.ExportKey,
                    expectation.Binding.ExportKey)
                || binding.TargetRequirement
                    != expectation.Binding.TargetRequirement
                || binding.RuntimePolicy
                    != expectation.Binding.RuntimePolicy)
            {
                throw new InvalidDataException(
                    "C1M1 class provider imports do not match every retained expectation.");
            }
        }

        if (boundImports.Count != expectationsByImport.Count)
        {
            throw new InvalidDataException(
                "A retained C1M1 class Import expectation has no selected provider binding.");
        }
    }

    private static void ValidateC1M1StructCarrierBindings(
        BoundProgram program,
        ImmutableArray<TrRmStandardModuleSelection> modules,
        byte[][] artifacts,
        ImmutableArray<TrRmStandardTypeProviderBinding> typeProviderBindings)
    {
        var expectationsByImport = new Dictionary<(
            string ModuleIdentity, int ModuleRow, int ImportRow),
            BoundSourceTypeExpectation>();
        foreach (var definition in program.GenericNominalTypeDefinitions)
        {
            if (definition.NominalKind != 1)
            {
                continue;
            }

            foreach (var expectation in definition.ProviderExpectations)
            {
                var binding = expectation;
                var key = (binding.UsingModule.ModuleIdentity,
                    binding.UsingModule.ModuleRow, binding.ImportRow);
                if (!expectationsByImport.TryAdd(key, binding))
                {
                    throw new InvalidDataException(
                        "A C1M1 struct Import has more than one retained expectation.");
                }
            }
        }

        var boundImports = new HashSet<(
            string ModuleIdentity, int ModuleRow, int ImportRow)>();
        foreach (var binding in typeProviderBindings)
        {
            var consumerModule = modules.Single(module =>
                module.ArtifactIndex == binding.ConsumerArtifactIndex
                && module.ModuleRow == binding.ConsumerModuleRow);
            var consumerTables = GetC1M1Tables(artifacts,
                consumerModule.ArtifactIndex);
            if (consumerTables.TypeDefs.GetKind(
                    binding.ExpectedTypeDefRow) != 1)
            {
                continue;
            }

            var key = (consumerModule.Identity,
                binding.ConsumerModuleRow, binding.ImportRow);
            if (!expectationsByImport.TryGetValue(key, out var expectation)
                || !boundImports.Add(key)
                || binding.ExpectedTypeDefRow
                    != GetTypeDefRow(expectation.ExpectedDefinitionId)
                || binding.ProviderTypeDefRow
                    != GetTypeDefRow(expectation.ProviderDefinitionId)
                || !StringComparer.Ordinal.Equals(
                    binding.ProducerIdentity, expectation.ProducerIdentity)
                || !StringComparer.Ordinal.Equals(binding.ExportKey,
                    expectation.ExportKey)
                || binding.TargetRequirement
                    != expectation.TargetRequirement
                || binding.RuntimePolicy != expectation.RuntimePolicy)
            {
                throw new InvalidDataException(
                    "C1M1 struct provider imports do not match every retained expectation.");
            }

            var usingSnapshot = CreateA2SourceTypeProviderSnapshot(
                consumerTables, artifacts[consumerModule.ArtifactIndex],
                StandardC1M1SchemaIdentity, consumerModule.ModuleRow);
            var providerModule = modules.Single(module =>
                StringComparer.Ordinal.Equals(module.Identity,
                    expectation.ProducerIdentity)
                && module.ModuleRow == expectation.Provider.ModuleRow);
            var providerTables = GetC1M1Tables(artifacts,
                providerModule.ArtifactIndex);
            var providerSnapshot = CreateA2SourceTypeProviderSnapshot(
                providerTables, artifacts[providerModule.ArtifactIndex],
                StandardC1M1SchemaIdentity, providerModule.ModuleRow);
            var expectedTypeDefRow
                = GetTypeDefRow(expectation.ExpectedDefinitionId);
            var providerTypeDefRow
                = GetTypeDefRow(expectation.ProviderDefinitionId);
            var importModuleRefRow = consumerTables.Imports
                .GetModuleRefForImportRow(expectation.ImportRow);
            var importBinding = consumerTables.Imports
                .GetBindingForImportRow(expectation.ImportRow);
            if (!SourceTypeSnapshotsMatch(expectation.UsingModule,
                    usingSnapshot)
                || !SourceTypeSnapshotsMatch(expectation.Provider,
                    providerSnapshot)
                || consumerTables.TypeDefs.GetOwnerModule(
                    expectedTypeDefRow) != consumerModule.ModuleRow
                || consumerTables.TypeDefs.GetKind(expectedTypeDefRow) != 1
                || providerTables.TypeDefs.GetOwnerModule(
                    providerTypeDefRow) != providerModule.ModuleRow
                || providerTables.TypeDefs.GetKind(providerTypeDefRow) != 1
                || consumerTables.Imports
                    .GetExpectedDefinitionForImportRow(expectation.ImportRow)
                    != expectation.ExpectedDefinitionId
                || !StringComparer.Ordinal.Equals(
                    importBinding.ProducerIdentity,
                    expectation.ProducerIdentity)
                || !StringComparer.Ordinal.Equals(importBinding.ExportKey,
                    expectation.ExportKey)
                || importBinding.TargetRequirement
                    != expectation.TargetRequirement
                || importBinding.RuntimePolicy
                    != expectation.RuntimePolicy
                || consumerTables.ModuleRefs.GetRequirements(
                    importModuleRefRow)
                    != (expectation.TargetRequirement,
                        expectation.RuntimePolicy)
                || !providerTables.Exports.TryGetDefinitionForKey(
                    providerModule.ModuleRow, expectation.ExportKey,
                    out var actualDefinition)
                || actualDefinition != expectation.ProviderDefinitionId)
            {
                throw new InvalidDataException(
                    "A retained C1M1 struct Import no longer matches its using and provider Modules.");
            }
        }

        if (boundImports.Count != expectationsByImport.Count)
        {
            throw new InvalidDataException(
                "A retained C1M1 struct Import has no selected provider binding.");
        }
    }

    private static TrRmStructuralMetadataValidator.ValidatedExecutionTables
        GetC1M1Tables(byte[][] artifacts, int artifactIndex)
    {
        if ((uint)artifactIndex >= (uint)artifacts.Length)
        {
            throw new InvalidDataException(
                "A C1M1 class provider artifact index is out of range.");
        }

        return TrRmStructuralMetadataValidator
            .ValidateC1AndGetExecutionTables(
                TrRmArtifactIndex.ForC1M1Draft(
                    artifacts[artifactIndex]));
    }

    private static byte[] GetTargetProfileSection(TrRmArtifactIndex index,
        out int recordCount)
    {
        if (index.TryGetSection(0x0003, out var bytes, out recordCount))
        {
            return bytes.ToArray();
        }

        recordCount = 0;
        return [];
    }

    private static uint RemapC1M1ClassTypeSpec(
        TrRmStructuralMetadataValidator.ValidatedExecutionTables sourceTables,
        uint typeSpecReference, Dictionary<int, int> typeDefRowMap,
        Dictionary<int, int> importRowMap,
        List<ReadOnlyMemory<byte>> outputRows,
        Dictionary<int, uint> rowMap, HashSet<int> activeRows, int depth)
    {
        if (depth > SemanticVerifier.MaximumDepth)
        {
            throw Unsupported(
                "C1M1 class TypeSpec remapping exceeds the type nesting limit.");
        }

        var reference = sourceTables.TypeSpecs.GetTypeSpecRef(
            typeSpecReference);
        if (!reference.IsRow)
        {
            return typeSpecReference;
        }
        if (rowMap.TryGetValue(reference.Row, out var existingReference))
        {
            return existingReference;
        }
        if (!activeRows.Add(reference.Row))
        {
            throw new InvalidDataException(
                "C1M1 type dependency TypeSpec rows contain a cycle.");
        }

        try
        {
            var row = sourceTables.TypeSpecs.GetRow(reference.Row).ToArray();
            switch (reference.Form)
            {
                case 0x01:
                    break;

                case 0x02:
                {
                    var oldTypeDefRow = checked((int)
                        BinaryPrimitives.ReadUInt32LittleEndian(
                            row.AsSpan(2)));
                    if (!typeDefRowMap.TryGetValue(oldTypeDefRow,
                            out var newTypeDefRow))
                    {
                        throw Unsupported(
                            "A class field refers to an unretained local TypeDef.");
                    }

                    Write(row, 2, checked((uint)newTypeDefRow));
                    break;
                }

                case 0x03:
                {
                    var oldImportRow = checked((int)
                        BinaryPrimitives.ReadUInt32LittleEndian(
                            row.AsSpan(2)));
                    if (!importRowMap.TryGetValue(oldImportRow,
                            out var newImportRow))
                    {
                        throw Invalid(
                            "A class field TypeSpec refers to an unretained Import.");
                    }

                    Write(row, 2, checked((uint)newImportRow));
                    break;
                }

                case >= 0x06 and <= 0x0B:
                case 0x0E:
                {
                    var child = BinaryPrimitives.ReadUInt32LittleEndian(
                        row.AsSpan(2));
                    var remappedChild = RemapC1M1ClassTypeSpec(
                        sourceTables, child, typeDefRowMap, importRowMap,
                        outputRows, rowMap, activeRows, depth + 1);
                    Write(row, 2, remappedChild);
                    break;
                }

                case 0x0C:
                {
                    var child = BinaryPrimitives.ReadUInt32LittleEndian(
                        row.AsSpan(3));
                    var remappedChild = RemapC1M1ClassTypeSpec(
                        sourceTables, child, typeDefRowMap, importRowMap,
                        outputRows, rowMap, activeRows, depth + 1);
                    Write(row, 3, remappedChild);
                    break;
                }

                default:
                    throw Unsupported(
                        "C1M1 type dependency TypeSpec remapping does not support this TypeSpec form.");
            }

            outputRows.Add(row);
            var newReference = checked((uint)outputRows.Count);
            rowMap.Add(reference.Row, newReference);
            return newReference;
        }
        finally
        {
            _ = activeRows.Remove(reference.Row);
        }
    }

    private static C1M1TypeDefEmissionView ToC1M1TypeDefEmissionView(
        TrRmStructuralMetadataValidator.ValidatedExecutionTables tables,
        int typeDefRow, BoundStandardClassTypeView view)
        => new(tables.TypeDefs.GetKind(typeDefRow), view.Namespace,
            view.Name, view.TypeDefFlags, view.Fields, view.Layouts);

    private static C1M1TypeDefEmissionView CreateC1M1TypeDefEmissionView(
        TrRmStructuralMetadataValidator.ValidatedExecutionTables tables,
        BoundSourceTypeProviderSnapshot provider, int typeDefRow)
    {
        var identity = new BoundGenericNominalTypeIdentity(
            provider.ModuleIdentity, provider.ModuleIdentity,
            0x1300_0000u | checked((uint)typeDefRow));
        var classView = CreateStandardClassTypeView(tables, identity,
            provider, typeDefRow);
        return ToC1M1TypeDefEmissionView(tables, typeDefRow, classView);
    }

    private static void AddC1M1LocalNonGenericTypeClosure(
        TrRmStructuralMetadataValidator.ValidatedExecutionTables rootTables,
        int rootModuleRow, BoundSourceTypeProviderSnapshot rootSnapshot,
        SortedDictionary<int, C1M1TypeDefEmissionView>
            typeDefViewsByOldRow,
        Dictionary<int, ImmutableArray<string>> exportsByOldRow)
    {
        var pendingTypeDefRows = new Queue<int>(typeDefViewsByOldRow.Keys);
        var pendingTypeSpecs = new Stack<(uint Reference, int Depth)>();
        var visitedTypeSpecs = new HashSet<int>();
        var remaining = SemanticVerifier.MaximumAnalysisVisits;
        while (pendingTypeDefRows.TryDequeue(out var typeDefRow))
        {
            if (--remaining < 0)
            {
                throw Unsupported(
                    "C1M1 class dependency closure exceeds the metadata traversal limit.");
            }

            var view = typeDefViewsByOldRow[typeDefRow];
            foreach (var field in view.Fields)
            {
                if (--remaining < 0)
                {
                    throw Unsupported(
                        "C1M1 class dependency closure exceeds the metadata traversal limit.");
                }

                pendingTypeSpecs.Push((field.TypeSpecReference, 1));
                while (pendingTypeSpecs.TryPop(out var pendingTypeSpec))
                {
                    if (--remaining < 0
                        || pendingTypeSpec.Depth
                            > SemanticVerifier.MaximumDepth)
                    {
                        throw Unsupported(
                            "C1M1 class TypeSpec dependency closure exceeds its traversal limit.");
                    }

                    var reference = rootTables.TypeSpecs.GetTypeSpecRef(
                        pendingTypeSpec.Reference);
                    if (!reference.IsRow
                        || !visitedTypeSpecs.Add(reference.Row))
                    {
                        continue;
                    }

                    var row = rootTables.TypeSpecs.GetRow(reference.Row);
                    switch (reference.Form)
                    {
                        case 0x01:
                        case 0x03:
                            break;

                        case 0x02:
                        {
                            var referencedTypeDefRow = checked((int)
                                BinaryPrimitives.ReadUInt32LittleEndian(
                                    row[2..]));
                            if (rootTables.TypeDefs.GetOwnerModule(
                                    referencedTypeDefRow)
                                != rootModuleRow)
                            {
                                throw new InvalidDataException(
                                    "A C1M1 class TypeSpec directly references a TypeDef owned by another Module.");
                            }
                            var kind = rootTables.TypeDefs.GetKind(
                                referencedTypeDefRow);
                            if (kind is not (1 or 2))
                            {
                                throw Unsupported(
                                    "C1M1 class re-emission does not yet retain local interface TypeDef dependencies.");
                            }

                            if (rootTables.TypeDefs.GetGenericRange(
                                    referencedTypeDefRow).Count != 0
                                || rootTables.TypeDefs.GetContractRange(
                                    referencedTypeDefRow).Count != 0)
                            {
                                throw Unsupported(
                                    "C1M1 class re-emission does not yet retain generic or contracted local TypeDef dependencies.");
                            }

                            if (!typeDefViewsByOldRow.ContainsKey(
                                    referencedTypeDefRow))
                            {
                                var dependencyView
                                    = CreateC1M1TypeDefEmissionView(
                                        rootTables, rootSnapshot,
                                        referencedTypeDefRow);
                                if (dependencyView.Kind != kind
                                    || !typeDefViewsByOldRow.TryAdd(
                                        referencedTypeDefRow,
                                        dependencyView)
                                    || !exportsByOldRow.TryAdd(
                                        referencedTypeDefRow, []))
                                {
                                    throw new InvalidDataException(
                                        "A C1M1 local TypeDef dependency was retained more than once.");
                                }

                                pendingTypeDefRows.Enqueue(
                                    referencedTypeDefRow);
                            }

                            break;
                        }

                        case >= 0x06 and <= 0x0B:
                        case 0x0C:
                        case 0x0E:
                        {
                            var childOffset = reference.Form == 0x0C
                                ? 3 : 2;
                            var child = BinaryPrimitives
                                .ReadUInt32LittleEndian(
                                    row[childOffset..]);
                            pendingTypeSpecs.Push((child,
                                checked(pendingTypeSpec.Depth + 1)));
                            break;
                        }

                        default:
                            throw Unsupported(
                                "C1M1 class TypeSpec dependency closure does not support this TypeSpec form.");
                    }
                }
            }
        }
    }

    private static byte[] EncodeC1M1ClassImports(
        SortedDictionary<int, BoundSourceTypeExpectation> bindingsByImportRow,
        Dictionary<int, int> importRowMap,
        Dictionary<int, int> moduleRefRowMap,
        Dictionary<int, int> typeDefRowMap,
        Dictionary<string, int> stringRows,
        TrRmStructuralMetadataValidator.ValidatedExecutionTables sourceTables)
    {
        var bytes = new byte[checked(bindingsByImportRow.Count * 12)];
        foreach (var (oldImportRow, binding) in bindingsByImportRow)
        {
            var newImportRow = importRowMap[oldImportRow];
            var oldModuleRefRow = sourceTables.Imports
                .GetModuleRefForImportRow(oldImportRow);
            var newModuleRefRow = moduleRefRowMap[oldModuleRefRow];
            var expectedOldTypeDefRow
                = GetTypeDefRow(binding.ExpectedDefinitionId);
            var expectedNewTypeDefRow
                = typeDefRowMap[expectedOldTypeDefRow];
            var offset = (newImportRow - 1) * 12;
            Write(bytes, offset, checked((uint)newModuleRefRow));
            Write(bytes, offset + 4,
                checked((uint)stringRows[binding.ExportKey]));
            Write(bytes, offset + 8,
                0x1300_0000u | checked((uint)expectedNewTypeDefRow));
        }

        return bytes;
    }

    private static byte[] EncodeC1M1Exports(
        IReadOnlyList<(uint DefinitionId, string Key)> exports,
        Dictionary<string, int> stringRows)
    {
        var bytes = new byte[checked(exports.Count * 8)];
        for (var index = 0; index < exports.Count; index++)
        {
            var export = exports[index];
            BinaryPrimitives.WriteUInt32LittleEndian(
                bytes.AsSpan(index * 8), export.DefinitionId);
            Write(bytes, (index * 8) + 4,
                checked((uint)stringRows[export.Key]));
        }

        return bytes;
    }

    private static bool SourceTypeSnapshotsMatch(
        BoundSourceTypeProviderSnapshot left,
        BoundSourceTypeProviderSnapshot right)
        => StringComparer.Ordinal.Equals(left.SchemaIdentity,
                right.SchemaIdentity)
            && left.ModuleRow == right.ModuleRow
            && StringComparer.Ordinal.Equals(left.ModuleIdentity,
                right.ModuleIdentity)
            && left.GoverningRuleEdition == right.GoverningRuleEdition
            && left.TargetRequirement == right.TargetRequirement
            && left.RuntimePolicy == right.RuntimePolicy
            && left.Features.AsSpan().SequenceEqual(right.Features.AsSpan())
            && left.TargetProfiles.AsSpan().SequenceEqual(
                right.TargetProfiles.AsSpan())
            && left.ArtifactSnapshot.AsSpan().SequenceEqual(
                right.ArtifactSnapshot.AsSpan());

    private static bool StandardClassViewsMatch(
        BoundStandardClassTypeView left,
        BoundStandardClassTypeView right)
    {
        if (left.Identity != right.Identity
            || !StringComparer.Ordinal.Equals(left.Namespace,
                right.Namespace)
            || !StringComparer.Ordinal.Equals(left.Name, right.Name)
            || left.TypeDefFlags != right.TypeDefFlags
            || !SourceTypeSnapshotsMatch(left.Provider, right.Provider)
            || left.Fields.Length != right.Fields.Length
            || left.Layouts.Length != right.Layouts.Length)
        {
            return false;
        }

        for (var index = 0; index < left.Fields.Length; index++)
        {
            if (left.Fields[index] != right.Fields[index])
            {
                return false;
            }
        }

        for (var index = 0; index < left.Layouts.Length; index++)
        {
            var leftLayout = left.Layouts[index];
            var rightLayout = right.Layouts[index];
            if (leftLayout.Row != rightLayout.Row
                || leftLayout.OwnerDefinitionId
                    != rightLayout.OwnerDefinitionId
                || leftLayout.TargetProfileReference
                    != rightLayout.TargetProfileReference
                || leftLayout.TargetProfileId
                    != rightLayout.TargetProfileId
                || leftLayout.TargetProfileRuleRevision
                    != rightLayout.TargetProfileRuleRevision
                || leftLayout.Kind != rightLayout.Kind
                || leftLayout.Flags != rightLayout.Flags
                || leftLayout.Alignment != rightLayout.Alignment
                || leftLayout.Pack != rightLayout.Pack
                || !leftLayout.FieldDefinitionIds.AsSpan()
                    .SequenceEqual(
                        rightLayout.FieldDefinitionIds.AsSpan()))
            {
                return false;
            }
        }

        return true;
    }

    private static int GetTypeDefRow(uint definitionId)
    {
        if ((definitionId >> 24) != 0x13
            || (definitionId & 0x00FF_FFFF) == 0)
        {
            throw Invalid("A C1M1 type reference has an invalid TypeDef identity.");
        }

        return checked((int)(definitionId & 0x00FF_FFFF));
    }

    private static int GetFieldDefRow(uint definitionId)
    {
        if ((definitionId >> 24) != 0x14
            || (definitionId & 0x00FF_FFFF) == 0)
        {
            throw Invalid("A C1M1 type reference has an invalid FieldDef identity.");
        }

        return checked((int)(definitionId & 0x00FF_FFFF));
    }

    private static void Write(Span<byte> bytes, int offset, uint value)
        => BinaryPrimitives.WriteUInt32LittleEndian(bytes[offset..], value);
}
