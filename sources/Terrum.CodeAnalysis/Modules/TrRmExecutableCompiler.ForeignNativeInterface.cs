// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Terrum.CodeAnalysis.Semantics;

namespace Terrum.CodeAnalysis.Modules;

public static partial class TrRmExecutableCompiler
{
    private static byte[] EmitForeignNativeInterfaceDeclarations(
        BoundProgram program, string moduleIdentity, string schemaIdentity,
        uint moduleTargetRequirement,
        IReadOnlyList<TrRmSourceTypeExport>? sourceTypeExports)
    {
        ArgumentNullException.ThrowIfNull(program);
        ArgumentException.ThrowIfNullOrEmpty(moduleIdentity);
        var schema = GetForeignInvocationSchema(schemaIdentity);
        if (!schema.UsesForeignNativeInterface()
            || moduleTargetRequirement
                != TrRmModuleTableReader.WindowsX64NativeTargetRequirement
            || program.EntryPoint >= 0
            || !program.Functions.IsEmpty
            || !program.Classes.IsEmpty
            || !program.Buffers.IsEmpty
            || !program.NativeFixedArrayTypes.IsEmpty
            || !program.GenericMethodDefinitions.IsEmpty
            || !program.GenericMethodSignatures.IsEmpty
            || !program.GenericMethodOperatorProtocols.IsEmpty
            || !program.GenericMethodTypeFormalOwners.IsEmpty
            || !program.RetainedGenericMethodRoots.IsEmpty
            || !program.SourceGenericTypeDefinitions.IsEmpty
            || !program.SourceGenericTypeInstances.IsEmpty
            || !program.GenericNominalTypeDefinitions.IsEmpty
            || !program.GenericNominalTypeInstances.IsEmpty
            || !program.SpanTypes.IsEmpty
            || !program.CheckedReferenceTypes.IsEmpty
            || !program.PrivateNominalTypeOrigins.IsEmpty
            || !program.PrivateClassReferences.IsEmpty
            || !program.StandardClassDeclarations.IsEmpty
            || !program.InterfaceWitnesses.IsEmpty
            || !program.EnumDefinitions.IsEmpty
            || !ForeignNativeInterfaceContract.IsValidTable(program))
        {
            throw Unsupported(
                "The F+I source producer requires a declaration-only library with supported native interface and aggregate metadata.");
        }

        var localInterfaces = program.Interfaces.Where(
                static definition => definition.ModuleImport is null)
            .ToArray();
        if (program.Interfaces.IsDefaultOrEmpty
            || program.Interfaces.Any(static definition =>
                definition.ForeignNativeInterface is null))
        {
            throw Unsupported(
                "The F+I library must contain only ForeignNativeInterface declarations and their selected local Blittable aggregate closure.");
        }

        var localInterfaceNames = localInterfaces.Select(
                static definition => definition.QualifiedName)
            .ToHashSet(StringComparer.Ordinal);
        var localStructureIndexesByName = new Dictionary<string, int>(
            StringComparer.Ordinal);
        for (var structureIndex = 0;
            structureIndex < program.Structures.Length; structureIndex++)
        {
            if (program.Structures[structureIndex].StandardTypeOrigin is null
                && !localStructureIndexesByName.TryAdd(
                    program.Structures[structureIndex].Name,
                    structureIndex))
            {
                throw Invalid(
                    "The F+I source program contains duplicate local structure names.");
            }
        }

        var interfaceTypeExports = new List<TrRmSourceTypeExport>();
        var structureTypeExports = new List<TrRmSourceTypeExport>();
        foreach (var typeExport in sourceTypeExports
                     ?? Array.Empty<TrRmSourceTypeExport>())
        {
            if (typeExport is null || typeExport.Namespace is null
                || string.IsNullOrEmpty(typeExport.Name)
                || string.IsNullOrEmpty(typeExport.ExportKey))
            {
                throw new ArgumentException(
                    "An F+I source TypeDef mapping has invalid identity text.",
                    nameof(sourceTypeExports));
            }

            var qualifiedName = string.IsNullOrEmpty(typeExport.Namespace)
                ? typeExport.Name
                : string.Concat(typeExport.Namespace, ".", typeExport.Name);
            if (localInterfaceNames.Contains(qualifiedName))
            {
                interfaceTypeExports.Add(typeExport);
            }
            else if (localStructureIndexesByName.ContainsKey(qualifiedName))
            {
                structureTypeExports.Add(typeExport);
            }
            else
            {
                throw new ArgumentException(
                    "An F+I source TypeDef mapping does not select a local interface or structure.",
                    nameof(sourceTypeExports));
            }
        }

        if (interfaceTypeExports.Count != localInterfaces.Length)
        {
            throw new ArgumentException(
                "F+I source interface exports must identify every local ForeignNativeInterface TypeDef exactly once.",
                nameof(sourceTypeExports));
        }

        var requiredStructureIndexes
            = GetForeignNativeInterfaceStructureIndexes(
                localInterfaces, program, out var exportedStructureIndexes);
        var structureTypeExportNames = structureTypeExports.Select(
                static export => string.IsNullOrEmpty(export.Namespace)
                    ? export.Name
                    : string.Concat(export.Namespace, ".", export.Name))
            .ToHashSet(StringComparer.Ordinal);
        foreach (var structureIndex in exportedStructureIndexes)
        {
            if (program.Structures[structureIndex].StandardTypeOrigin
                    is not null)
            {
                throw Unsupported(
                    "F+I source emission does not yet compose imported aggregate pointee Types.");
            }
            if (!structureTypeExportNames.Contains(
                    program.Structures[structureIndex].Name))
            {
                throw new ArgumentException(
                    "An F+I aggregate pointee requires an explicit source TypeDef ExportKey.",
                    nameof(sourceTypeExports));
            }
        }

        var structureIndexes = requiredStructureIndexes
            .Concat(structureTypeExports.Select(export =>
                localStructureIndexesByName[
                    string.IsNullOrEmpty(export.Namespace)
                        ? export.Name
                        : string.Concat(export.Namespace, ".",
                            export.Name)]))
            .Distinct()
            .Order()
            .ToImmutableArray();
        if (!structureIndexes.IsEmpty)
        {
            ValidateForeignNativeInterfaceStructureSlice(program,
                structureIndexes);
        }

        if (!schema.UsesForeignNativeInterfaceParameterSurface()
            && program.Interfaces.Any(static definition =>
                definition.Members.Any(static member =>
                    !member.Parameters.IsEmpty)))
        {
            throw Unsupported(
                "Parameterized ForeignNativeInterface members require the exact F+I+SF1+PS1 schema.");
        }

        SemanticVerifier.Verify(program);
        var hasNativeOutputs = program.Interfaces.Any(static definition =>
            definition.Members.Any(static member =>
                !member.NativeOutputContracts.IsDefaultOrEmpty));
        var hasFunctionPointerPredicates
            = GetForeignNativeFunctionPointerSignatureIndexes(
                program.Interfaces, program).Any(index =>
                !program.NativeFunctionPointerSignatures[index]
                    .Selectors.IsEmpty);
        var hasScalarDefaults = schema
            .UsesForeignNativeInterfaceParameterSurface()
            && program.Interfaces.Any(static definition =>
                definition.Members.Any(static member =>
                    member.Parameters.Any(static parameter =>
                        parameter.DefaultValue is not null)));
        var featureRows = new List<TrRmFeatureRow> {
            TrRmFeatureRow.NativeBoundaryContracts,
            TrRmFeatureRow.RootlessNativeValues,
            TrRmFeatureRow.NativeInterfaceCalls,
        };
        if (hasNativeOutputs)
        {
            featureRows.Add(TrRmFeatureRow.NativeOutputCellsCandidate);
            featureRows.Add(TrRmFeatureRow.PublishedPredicates);
        }
        else if (hasFunctionPointerPredicates)
        {
            featureRows.Add(TrRmFeatureRow.PublishedPredicates);
        }
        if (hasScalarDefaults)
        {
            featureRows.Add(TrRmFeatureRow.TypedFrozenValues);
        }
        if (!structureIndexes.IsEmpty)
        {
            featureRows.Add(TrRmFeatureRow.LayoutQueries);
        }
        if (schema.UsesForeignNativeInterfaceParameterSurface())
        {
            featureRows.Add(TrRmFeatureRow.MethodParameterLabelsCandidate);
        }

        var moduleNameRows = TrRmStringTableWriter.EncodeTextRows([
            (moduleIdentity, 0),
        ]);
        var strings = new TrRmStringTableReader(moduleNameRows, 1);
        var featureBytes = TrRmFeatureTableWriter.Encode(featureRows, schema);
        var moduleBytes = TrRmModuleTableWriter.Encode([
            new TrRmModuleRow(1,
                moduleTargetRequirement, 0, 0, 1, 1, 1, 1, 1,
                featureRows.Count),
        ], strings, 0, 0, 0, 0, featureRows.Count);
        var targetProfileBytes = TrRmTargetProfileTableWriter.Encode([
            new TrRmTargetProfileRow(
                TrRmTargetProfileTableReader.WindowsX64NativeProfileId,
                TrRmTargetProfileTableReader
                    .WindowsX64NativeProfileRuleRevision),
        ]);
        var sections = new List<TrRmSection> {
            new(0x0001, 0, 1, moduleBytes),
            new(0x0002, 0, 1, moduleNameRows),
            new(0x0004, 0, checked((uint)featureRows.Count), featureBytes),
            new(0x0048, 0, 1, targetProfileBytes),
        };
        sections.Sort(static (left, right) =>
            left.Kind.CompareTo(right.Kind));
        var shell = TrRmArtifactWriter.Encode(sections, schema);
        if (!structureIndexes.IsEmpty)
        {
            var structureArtifact
                = EmitForeignNativeInterfaceStructureArtifact(program,
                    moduleIdentity, moduleTargetRequirement,
                    structureTypeExports, structureIndexes);
            shell = MergeForeignNativeInterfaceStructureArtifact(shell,
                structureArtifact, schema);
        }
        var sourceMethodExports = SourceGenericMethodExports.Capture(
            program, moduleIdentity, []);
        var artifact = EmitGenericMethodCallsDraftSource(program,
            moduleIdentity, moduleTargetRequirement, sourceMethodExports,
            interfaceTypeExports, schema, baseSchema: schema,
            baseArtifact: shell,
            frozenScalarValueBuilder: schema
                .UsesForeignNativeInterfaceParameterSurface()
                    ? new TrRmFrozenScalarValueBuilder() : null);
        artifact = AddForeignNativeInterfaceRows(artifact, program,
            interfaceTypeExports, schema);
        _ = TrRmStructuralMetadataValidator.ValidateAndGetExecutionTables(
            new TrRmArtifactIndex(artifact, schema));
        return artifact;
    }

    private static ImmutableArray<int>
        GetForeignNativeInterfaceStructureIndexes(
        IReadOnlyList<BoundInterfaceDefinition> definitions,
        BoundProgram program, out ImmutableArray<int> exportedIndexes)
    {
        var structureStates = new byte[program.Structures.Length];
        var pointerStates = new byte[program.NativePointerTypes.Length];
        var functionPointerStates
            = new byte[program.NativeFunctionPointerSignatures.Length];
        var structureIndexes = new SortedSet<int>();
        var exposedStructureIndexes = new SortedSet<int>();

        void Visit(PrimitiveType type, bool throughPointer, int depth)
        {
            if (depth > SemanticVerifier.MaximumDepth)
            {
                throw Unsupported(
                    "The F+I aggregate pointee TypeSpec closure exceeds the supported depth.");
            }

            if (NativePointerTypes.IsType(type))
            {
                var pointerIndex = NativePointerTypes.Index(type);
                if ((uint)pointerIndex >= (uint)program.NativePointerTypes.Length)
                {
                    throw Unsupported(
                        "A ForeignNativeInterface signature references an invalid pointer type.");
                }
                if (pointerStates[pointerIndex] != 2)
                {
                    if (pointerStates[pointerIndex] == 1)
                    {
                        throw Unsupported(
                            "A ForeignNativeInterface pointer TypeSpec closure is cyclic.");
                    }

                    pointerStates[pointerIndex] = 1;
                    Visit(program.NativePointerTypes[pointerIndex].PointeeType,
                        true, depth + 1);
                    pointerStates[pointerIndex] = 2;
                }
                return;
            }

            if (NativeFunctionPointerTypes.IsType(type))
            {
                var signatureIndex = NativeFunctionPointerTypes.Index(type);
                if ((uint)signatureIndex
                    >= (uint)program.NativeFunctionPointerSignatures.Length)
                {
                    throw Unsupported(
                        "A ForeignNativeInterface signature references an invalid function-pointer anchor.");
                }
                if (functionPointerStates[signatureIndex] != 2)
                {
                    if (functionPointerStates[signatureIndex] == 1)
                    {
                        throw Unsupported(
                            "A ForeignNativeInterface function-pointer closure is cyclic.");
                    }

                    functionPointerStates[signatureIndex] = 1;
                    var signature
                        = program.NativeFunctionPointerSignatures[
                            signatureIndex];
                    Visit(signature.ReturnType, true, depth + 1);
                    foreach (var parameterType in signature.ParameterTypes)
                    {
                        Visit(parameterType, true, depth + 1);
                    }
                    functionPointerStates[signatureIndex] = 2;
                }
                return;
            }

            if ((int)type is < 100
                || (int)type >= BoundEnumTypes.First)
            {
                if (BoundEnumTypes.IsType(type))
                {
                    throw Unsupported(
                        "F+I aggregate pointee signatures do not yet admit nominal enum TypeSpecs.");
                }
                return;
            }

            VisitStructure(checked((int)type - 100), throughPointer,
                depth + 1);
        }

        void VisitStructure(int structureIndex, bool throughPointer,
            int depth)
        {
            if ((uint)structureIndex >= (uint)program.Structures.Length)
            {
                throw Unsupported(
                    "A ForeignNativeInterface signature references an invalid aggregate type.");
            }
            if (throughPointer)
            {
                _ = exposedStructureIndexes.Add(structureIndex);
            }
            if (structureStates[structureIndex] == 2)
            {
                return;
            }
            if (structureStates[structureIndex] == 1)
            {
                if (throughPointer)
                {
                    return;
                }
                throw Unsupported(
                    "A ForeignNativeInterface inline aggregate closure is cyclic.");
            }

            var structure = program.Structures[structureIndex];
            if (structure.BlittableTargetRequirement
                    != FixedLayoutStorage.WindowsX64TargetRequirement
                || structure.LayoutKind is not (1 or 2 or 3)
                || structure.IsUnique || structure.CleanupMethod is not null
                || structure.Fields.IsDefaultOrEmpty)
            {
                throw Unsupported(
                    $"ForeignNativeInterface aggregate '{structure.Name}' has no supported Windows x64 Blittable layout.");
            }

            structureStates[structureIndex] = 1;
            foreach (var field in structure.Fields)
            {
                if (field is null
                    || NativePointerTypes.IsType(field.Type)
                    || NativeFunctionPointerTypes.IsType(field.Type)
                    || NativeFixedArrayTypes.IsType(field.Type)
                    || BoundEnumTypes.IsType(field.Type))
                {
                    throw Unsupported(
                        "The current F+I aggregate layout slice admits scalar, raw-pointer and inline-struct fields only.");
                }

                Visit(field.Type, false, depth + 1);
            }

            structureStates[structureIndex] = 2;
            _ = structureIndexes.Add(structureIndex);
        }

        foreach (var definition in definitions)
        {
            foreach (var member in definition.Members)
            {
                if (member.ResultType is BoundInterfaceType resultType)
                {
                    Visit(resultType.PrimitiveType, false, 0);
                }
                foreach (var parameter in member.Parameters)
                {
                    Visit(parameter.Type.PrimitiveType, false, 0);
                }
            }
        }

        exportedIndexes = [.. exposedStructureIndexes];
        return [.. structureIndexes];
    }

    private static void ValidateForeignNativeInterfaceStructureSlice(
        BoundProgram program, ImmutableArray<int> structureIndexes)
    {
        foreach (var structureIndex in structureIndexes)
        {
            if ((uint)structureIndex >= (uint)program.Structures.Length
                || program.Structures[structureIndex].StandardTypeOrigin
                    is not null)
            {
                throw Unsupported(
                    "The F+I structure producer currently requires local aggregate TypeDefs.");
            }
        }
    }

    private static byte[] EmitForeignNativeInterfaceStructureArtifact(
        BoundProgram program, string moduleIdentity,
        uint moduleTargetRequirement,
        IReadOnlyList<TrRmSourceTypeExport> structureTypeExports,
        ImmutableArray<int> structureIndexes)
    {
        var structureIndexMap = new int[program.Structures.Length];
        Array.Fill(structureIndexMap, -1);
        for (var index = 0; index < structureIndexes.Length; index++)
        {
            structureIndexMap[structureIndexes[index]] = index;
        }

        PrimitiveType RemapStructureFieldType(PrimitiveType type)
        {
            if (NativePointerTypes.IsType(type)
                || NativeFunctionPointerTypes.IsType(type)
                || NativeFixedArrayTypes.IsType(type)
                || BoundEnumTypes.IsType(type))
            {
                throw Unsupported(
                    "The current F+I aggregate layout slice admits scalar, raw-pointer and inline-struct fields only.");
            }

            if ((int)type is >= 100 and < BoundEnumTypes.First)
            {
                var structureIndex = (int)type - 100;
                if ((uint)structureIndex >= (uint)structureIndexMap.Length
                    || structureIndexMap[structureIndex] < 0)
                {
                    throw Unsupported(
                        "A ForeignNativeInterface aggregate field has no selected TypeDef closure.");
                }

                return (PrimitiveType)(100
                    + structureIndexMap[structureIndex]);
            }

            return type;
        }

        var structures = ImmutableArray.CreateBuilder<BoundStruct>(
            structureIndexes.Length);
        foreach (var structureIndex in structureIndexes)
        {
            var structure = program.Structures[structureIndex];
            var fields = structure.Fields.Select(field => field with {
                Type = RemapStructureFieldType(field.Type),
            }).ToImmutableArray();
            structures.Add(structure with { Fields = fields });
        }
        var selectedStructures = structures.MoveToImmutable();
        var sourceTypeDeclarations = program.SourceTypeDeclarations.IsDefault
            ? ImmutableArray<BoundSourceTypeDeclaration>.Empty
            : program.SourceTypeDeclarations
                .Where(declaration =>
                    (uint)declaration.StructureIndex
                        < (uint)structureIndexMap.Length
                    && structureIndexMap[declaration.StructureIndex] >= 0)
                .Select(declaration => declaration with {
                    StructureIndex = structureIndexMap[
                        declaration.StructureIndex],
                    DeclarationSnapshot = selectedStructures[
                        structureIndexMap[declaration.StructureIndex]],
                }).ToImmutableArray();
        var typePointers
            = ImmutableArray.CreateBuilder<NativePointerTypeDefinition>(
                selectedStructures.Length);
        for (var structureIndex = 0;
            structureIndex < selectedStructures.Length; structureIndex++)
        {
            typePointers.Add(new NativePointerTypeDefinition(
                (PrimitiveType)(100 + structureIndex), false));
        }

        if (!program.SourceTypePublications.IsDefault)
        {
            throw Unsupported(
                "F+I structure emission cannot replace a retained source publication map.");
        }

        var structureProgram = program with {
            Functions = [],
            EntryPoint = -1,
            Interfaces = [],
            Structures = selectedStructures,
            NativeFunctionPointerSignatures = [],
            NativePointerTypes = typePointers.MoveToImmutable(),
            NativeFixedArrayTypes = [],
            EnumDefinitions = [],
            SourceTypeDeclarations = sourceTypeDeclarations,
            StandardQuerySourceAssociation = null,
        };
        return EmitDevelopment(structureProgram, moduleIdentity,
            TrRmArtifactSchema.Standard, moduleTargetRequirement,
            structureTypeExports);
    }

    private static byte[] MergeForeignNativeInterfaceStructureArtifact(
        byte[] shell, byte[] structureArtifact,
        TrRmArtifactSchema schema)
    {
        var shellIndex = new TrRmArtifactIndex(shell, schema);
        var structuresIndex = new TrRmArtifactIndex(structureArtifact,
            TrRmArtifactSchema.Standard);
        var sections = new List<TrRmSection>(
            shellIndex.SectionCount + structuresIndex.SectionCount);
        for (var sectionIndex = 0;
            sectionIndex < structuresIndex.SectionCount; sectionIndex++)
        {
            structuresIndex.GetSection(sectionIndex, out var kind, out var flags,
                out var bytes, out var count);
            if (kind is 0x0001 or 0x0004)
            {
                continue;
            }

            sections.Add(new TrRmSection(kind, flags,
                checked((uint)count), bytes.ToArray()));
        }

        for (var sectionIndex = 0; sectionIndex < shellIndex.SectionCount;
            sectionIndex++)
        {
            shellIndex.GetSection(sectionIndex, out var kind, out var flags,
                out var bytes, out var count);
            if (kind is not (0x0002 or 0x0048))
            {
                sections.Add(new TrRmSection(kind, flags,
                    checked((uint)count), bytes.ToArray()));
            }
        }

        sections.Sort(static (left, right) =>
            left.Kind.CompareTo(right.Kind));
        return TrRmArtifactWriter.Encode(sections, schema);
    }

    private static byte[] AddForeignNativeInterfaceRows(byte[] artifact,
        BoundProgram program, IReadOnlyList<TrRmSourceTypeExport> typeExports,
        TrRmArtifactSchema schema)
    {
        var index = new TrRmArtifactIndex(artifact, schema);
        _ = index.TryGetSection(0x0001, out var moduleBytes,
            out var moduleCount);
        _ = index.TryGetSection(0x0002, out var stringBytes,
            out var stringCount);
        _ = index.TryGetSection(0x0004, out var featureBytes,
            out var featureCount);
        _ = index.TryGetSection(0x0010, out var moduleRefBytes,
            out var moduleRefCount);
        _ = index.TryGetSection(0x0011, out var typeSpecBytes,
            out var typeSpecCount);
        _ = index.TryGetSection(0x0013, out var typeDefBytes,
            out var typeDefCount);
        _ = index.TryGetSection(0x0014, out var fieldBytes,
            out var fieldCount);
        _ = index.TryGetSection(0x0015, out var layoutBytes,
            out var layoutCount);
        _ = index.TryGetSection(0x0016, out var methodBytes,
            out var methodCount);
        _ = index.TryGetSection(0x0017, out var signatureBytes,
            out var signatureCount);
        _ = index.TryGetSection(0x0018, out _,
            out var parameterCount);
        _ = index.TryGetSection(0x0019, out var genericParamBytes,
            out var genericParamCount);
        _ = index.TryGetSection(0x001B, out var contractBytes,
            out var contractCount);
        _ = index.TryGetSection(0x0024, out var interfaceMethodBytes,
            out var interfaceMethodCount);
        _ = index.TryGetSection(0x0025, out var interfaceBytes,
            out var interfaceCount);
        _ = index.TryGetSection(0x004C,
            out var foreignNativeInterfaceBytes,
            out var foreignNativeInterfaceCount);
        _ = index.TryGetSection(0x0048, out var targetProfileBytes,
            out var targetProfileCount);
        var strings = new TrRmStringTableReader(stringBytes, stringCount);
        var features = new TrRmFeatureTableReader(featureBytes,
            featureCount, schema);
        var modules = new TrRmModuleTableReader(moduleBytes, moduleCount,
            strings, moduleRefCount, typeDefCount, methodCount, 0,
            featureCount);
        var typeSpecs = typeSpecCount == 0 ? default
            : new TrRmTypeSpecTableReader(typeSpecBytes, typeSpecCount,
                typeDefCount, 0, genericParamCount, 0, schema, methodCount);
        var typeDefs = new TrRmTypeDefTableReader(typeDefBytes, typeDefCount,
            strings, modules, fieldCount, genericParamCount, layoutCount,
            contractCount, schema);
        var signatures = new TrRmMethodSigTableReader(signatureBytes,
            signatureCount, typeSpecs, parameterCount, genericParamCount);
        var methods = new TrRmMethodDefTableReader(methodBytes, methodCount,
            strings, modules, typeDefs, signatures, contractCount, 0, schema);
        var genericParams = new TrRmGenericParamTableReader(
            genericParamBytes, genericParamCount, strings, typeDefs,
            methods, signatures, typeSpecs, 0);
        var interfaces = new TrRmInterfaceDefTableReader(interfaceBytes,
            interfaceCount, typeDefs, genericParams, 0,
            interfaceMethodCount, 0, 0, 0);
        var targetProfiles = new TrRmTargetProfileTableReader(
            targetProfileBytes, targetProfileCount, schema);
        var foreignInterfaceRows = new List<TrRmForeignNativeInterfaceRow>(
            checked(typeExports.Count + foreignNativeInterfaceCount));
        if (foreignNativeInterfaceCount != 0)
        {
            var importedForeignInterfaces
                = new TrRmForeignNativeInterfaceTableReader(
                    foreignNativeInterfaceBytes,
                    foreignNativeInterfaceCount, interfaces,
                    targetProfiles, schema);
            for (var row = 1;
                row <= importedForeignInterfaces.Count; row++)
            {
                foreignInterfaceRows.Add(
                    new TrRmForeignNativeInterfaceRow(
                        importedForeignInterfaces.GetOwnerInterfaceDef(row),
                        importedForeignInterfaces.GetTargetProfile(row),
                        importedForeignInterfaces.GetVTableByteOffset(row)));
            }
        }

        for (var exportIndex = 0; exportIndex < typeExports.Count;
            exportIndex++)
        {
            var export = typeExports[exportIndex];
            var qualifiedName = string.IsNullOrEmpty(export.Namespace)
                ? export.Name : $"{export.Namespace}.{export.Name}";
            var definition = program.Interfaces.SingleOrDefault(candidate =>
                StringComparer.Ordinal.Equals(candidate.QualifiedName,
                    qualifiedName)
                && candidate.ModuleImport is null);
            if (definition?.ForeignNativeInterface is not
                    BoundForeignNativeInterfaceLayout layout
                || exportIndex >= interfaces.Count
                || !StringComparer.Ordinal.Equals(
                    typeDefs.GetNamespace(
                        interfaces.GetOwnerTypeDef(exportIndex + 1)),
                    export.Namespace)
                || !StringComparer.Ordinal.Equals(
                    typeDefs.GetName(
                        interfaces.GetOwnerTypeDef(exportIndex + 1)),
                    export.Name))
            {
                throw Invalid(
                    $"ForeignNativeInterface '{qualifiedName}' has no emitted InterfaceDef row.");
            }

            foreignInterfaceRows.Add(new TrRmForeignNativeInterfaceRow(
                checked((uint)(exportIndex + 1)), 1,
                layout.VTableByteOffset));
        }

        var bytes = TrRmForeignNativeInterfaceTableWriter.Encode(
            foreignInterfaceRows, interfaces, targetProfiles);
        var sections = new List<TrRmSection>(index.SectionCount);
        for (var sectionIndex = 0; sectionIndex < index.SectionCount;
            sectionIndex++)
        {
            index.GetSection(sectionIndex, out var kind, out var flags,
                out var sectionBytes, out var count);
            if (kind != 0x004C)
            {
                sections.Add(new TrRmSection(kind, flags,
                    checked((uint)count), sectionBytes.ToArray()));
            }
        }

        sections.Add(new TrRmSection(0x004C, 0,
            checked((uint)foreignInterfaceRows.Count), bytes));
        sections.Sort(static (left, right) =>
            left.Kind.CompareTo(right.Kind));
        return TrRmArtifactWriter.Encode(sections, schema);
    }

}
