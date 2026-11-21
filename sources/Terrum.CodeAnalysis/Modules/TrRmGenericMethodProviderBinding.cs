// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Security.Cryptography;
using Terrum.CodeAnalysis.Semantics;

namespace Terrum.CodeAnalysis.Modules;

public static partial class TrRmExecutableCompiler
{
    private sealed class GenericMethodReadContext
    {
        internal byte[][] Artifacts { get; }
        internal ImmutableArray<byte>[] Digests { get; }
        internal string SchemaIdentity { get; }
        internal TrRmArtifactSchema Schema { get; }
        internal ImmutableArray<TrRmStandardModuleSelection>
            ModuleSelections { get; }
        internal Dictionary<string, (int ArtifactIndex, int ModuleRow)>
            Modules { get; } = new(StringComparer.Ordinal);
        internal Dictionary<(int ArtifactIndex, int ModuleRow),
            (uint TargetRequirement, uint RuntimePolicy)>
            ModuleRequirements { get; } = [];
        internal Dictionary<(int ArtifactIndex, int MethodRow),
            BoundGenericMethodImport> MethodTargets { get; } = [];
        internal Dictionary<(int ArtifactIndex, int ImportRow),
            BoundGenericMethodImport> ImportTargets { get; } = [];
        internal Dictionary<(int ArtifactIndex, int MethodRow),
            (int ArtifactIndex, uint DefinitionId)> ExpectedProviders
                { get; } = [];
        internal Dictionary<(int ArtifactIndex, int MethodRow, string ExportKey),
            BoundGenericMethodImport> ExportedMethods { get; } = [];
        internal Dictionary<(int ArtifactIndex, int ModuleRow),
            ImmutableArray<BoundInterfaceDefinition>> ExportedInterfaceDefinitions
                { get; } = [];
        internal Dictionary<(int ArtifactIndex, int ModuleRow, string ExportKey),
            BoundInterfaceDefinition> ExportedInterfaceDefinitionsByKey
                { get; } = [];
        internal Dictionary<(int ArtifactIndex, int ModuleRow, string ExportKey),
            Dictionary<string, BoundInterfaceMemberDefinition>>
            ForeignNativeInterfaceMembersByExportKey { get; } = [];
        internal Dictionary<(int ArtifactIndex, int ModuleRow),
            ImmutableArray<BoundInterfaceDefinition>> InterfaceDefinitions
                { get; } = [];
        internal Dictionary<(int ArtifactIndex, int ModuleRow, int TypeDefRow),
            BoundInterfaceDefinition> InterfaceDefinitionsByTypeDef
                { get; } = [];
        internal HashSet<(int ArtifactIndex, int ModuleRow)>
            ModulesWithValidatedG1InterfaceMethodClosure { get; } = [];
        internal HashSet<(int ArtifactIndex, int ModuleRow)>
            ForeignNativeInterfaceModulesBeingLoaded { get; } = [];
        internal Dictionary<(int ArtifactIndex, int MethodRow),
            BoundFunction> DecodedMethods { get; } = [];
        internal Dictionary<(int ArtifactIndex, int MethodRow),
            ImmutableArray<BoundFunction>> DecodedMethodSupportFunctions
                { get; } = [];
        internal List<((int ArtifactIndex, int MethodRow) Method,
            BoundGenericMethodImport Import)> MethodViews { get; } = [];
        internal HashSet<(int ArtifactIndex, int MethodRow)>
            MethodsBeingDecoded { get; } = [];
        internal HashSet<(int ArtifactIndex, int ImportRow)>
            ImportsBeingDecoded { get; } = [];
        internal HashSet<(int ArtifactIndex, int ModuleRow)>
            ModulesWithValidatedImports { get; } = [];
        internal List<BoundGenericMethodImport> ExpectedTargets { get; } = [];
        internal List<BoundSpanTypeDefinition> SpanTypes { get; } = [];
        internal List<NativePointerTypeDefinition>
            ForeignNativeInterfacePointerTypes { get; } = [];
        internal List<BoundStruct> ForeignNativeInterfaceStructures
            { get; } = [];
        internal List<NativeFunctionPointerSignatureDefinition>
            ForeignNativeInterfaceFunctionPointerSignatures { get; } = [];
        internal List<CheckedReferenceTypeDefinition>
            CheckedReferenceTypeDefinitions { get; } = [];
        internal ImmutableArray<BoundGenericNominalTypeDefinition>.Builder
            GenericNominalTypeDefinitions { get; }
                = ImmutableArray.CreateBuilder<
                    BoundGenericNominalTypeDefinition>();
        internal ImmutableArray<BoundGenericNominalTypeInstance>.Builder
            GenericNominalTypeInstances { get; }
                = ImmutableArray.CreateBuilder<
                    BoundGenericNominalTypeInstance>();
        internal ImmutableArray<NativePointerTypeDefinition>.Builder
            GenericNominalPointerTypes { get; }
                = ImmutableArray.CreateBuilder<
                    NativePointerTypeDefinition>();
        internal Dictionary<(PrimitiveType PointeeType, bool IsWritable),
            PrimitiveType> GenericNominalPointerTypeIndexes { get; } = [];
        private readonly Dictionary<BoundGenericNominalTypeIdentity, int>
            _genericNominalDefinitionIndexes = [];
        private readonly Dictionary<string, PrimitiveType>
            _genericNominalTypeInstanceIndexes = new(StringComparer.Ordinal);
        private readonly HashSet<(int ArtifactIndex, int ModuleRow,
            int TypeDefRow)> _genericNominalDefinitionsLoaded = [];
        internal HashSet<(int ArtifactIndex, int ImportRow)>
            GenericNominalTypeImports { get; } = [];
        private readonly Dictionary<(int ArtifactIndex, int MethodRow), int>
            _spanFormalOwnerOrdinals = [];
        private readonly Dictionary<(int ArtifactIndex, int MethodRow,
            PrimitiveType ReferentType, bool IsWritable,
            bool IsGarbageCollected, bool IsInitialization,
            bool IsOptional, bool IsReferentWritable), PrimitiveType>
                _checkedReferenceTypeIndexes = [];
        internal Dictionary<(PrimitiveType ElementType,
            int ElementFormalDefinitionOrdinal, int ElementFormalOrdinal,
            bool IsElementWritable, bool IsWritable, bool IsInitialization),
            int> SpanTypeIndexes { get; } = [];

        internal int GetFormalTypeOwnerOrdinal(int artifactIndex,
            int methodRow)
        {
            var key = (artifactIndex, methodRow);
            if (_spanFormalOwnerOrdinals.TryGetValue(key,
                    out var existingOrdinal))
            {
                return existingOrdinal;
            }

            var ordinal = _spanFormalOwnerOrdinals.Count;
            if (ordinal >= BoundSpanTypeTypes.MaximumCount)
            {
                throw Unsupported(
                    "The selected G1 artifact exceeds the supported generic Span formal-owner count.");
            }
            _spanFormalOwnerOrdinals.Add(key, ordinal);
            return ordinal;
        }

        internal PrimitiveType GetSpanType(PrimitiveType elementType,
            int formalOwnerOrdinal, int formalOrdinal,
            bool isElementWritable, bool isWritable, bool isInitialization)
        {
            var formalDefinitionOrdinal = formalOrdinal < 0
                ? -1
                : checked(BoundSpanTypeContract
                    .ImportedFormalDefinitionOrdinal - formalOwnerOrdinal);
            if (elementType == PrimitiveType.Void
                || (formalOrdinal >= 0 && formalOwnerOrdinal < 0)
                || (isElementWritable && formalOrdinal < 0))
            {
                throw Invalid(
                    "The G1 Span type has an invalid element formal or qualifier.");
            }

            var key = (elementType, formalDefinitionOrdinal, formalOrdinal,
                isElementWritable, isWritable, isInitialization);
            if (SpanTypeIndexes.TryGetValue(key, out var existingIndex))
            {
                return BoundSpanTypeTypes.Type(existingIndex);
            }
            if (SpanTypes.Count >= BoundSpanTypeTypes.MaximumCount)
            {
                throw Unsupported(
                    "The selected G1 artifact exceeds the supported Span type count.");
            }

            var index = SpanTypes.Count;
            SpanTypes.Add(new BoundSpanTypeDefinition(elementType,
                formalDefinitionOrdinal, formalOrdinal, isElementWritable,
                isWritable, isInitialization));
            SpanTypeIndexes.Add(key, index);
            return BoundSpanTypeTypes.Type(index);
        }

        internal PrimitiveType GetCheckedReferenceType(
            int artifactIndex, int methodRow, PrimitiveType referentType,
            bool isWritable, bool isGarbageCollected, bool isInitialization,
            bool isReferentWritable, bool isOptional = false)
        {
            var key = (artifactIndex, methodRow, referentType, isWritable,
                isGarbageCollected, isInitialization,
                isOptional, isReferentWritable);
            if (_checkedReferenceTypeIndexes.TryGetValue(key,
                    out var existingType))
            {
                return existingType;
            }
            if (referentType == PrimitiveType.Void
                || (isGarbageCollected && isInitialization)
                || (isOptional && isInitialization)
                || CheckedReferenceTypeDefinitions.Count
                    >= SemanticVerifier.MaximumAnalysisVisits)
            {
                throw Unsupported(
                    "The selected G1 artifact exceeds the supported checked-reference type count.");
            }

            var index = CheckedReferenceTypeDefinitions.Count;
            CheckedReferenceTypeDefinitions.Add(
                new CheckedReferenceTypeDefinition(referentType,
                    isWritable, isGarbageCollected, isInitialization,
                    IsOptional: isOptional,
                    IsReferentWritable: isReferentWritable));
            var type = CheckedReferenceTypes.Type(index);
            _checkedReferenceTypeIndexes.Add(key, type);
            return type;
        }

        internal PrimitiveType ReadGenericNominalTypeSpec(
            TrRmStructuralMetadataValidator.ValidatedExecutionTables tables,
            int artifactIndex, int moduleRow, uint typeSpecRef,
            int methodRow, ImmutableArray<PrimitiveType> methodFormalTypes,
            TrRmGenericMethodFormalImport[] formalRecords)
        {
            EnsureGenericNominalTypeSpecs(tables, artifactIndex, moduleRow,
                typeSpecRef, methodRow, 0);
            return TrRmExecutableCompiler.ReadGenericNominalTypeSpec(
                typeSpecRef, tables, GenericNominalTypeDefinitions.ToImmutable(),
                tables.Modules.GetIdentity(moduleRow), GenericNominalTypeInstances,
                _genericNominalTypeInstanceIndexes, GenericNominalPointerTypes,
                GenericNominalPointerTypeIndexes, 0, methodRow,
                methodFormalTypes,
                GetFormalTypeOwnerOrdinal(artifactIndex, methodRow), this,
                artifactIndex, moduleRow, formalRecords);
        }

        internal BoundGenericNominalTypeArgument
            ReadGenericNominalTypeArgument(
                TrRmStructuralMetadataValidator.ValidatedExecutionTables tables,
                int artifactIndex, int moduleRow, uint typeSpecRef,
                int methodRow,
                ImmutableArray<PrimitiveType> methodFormalTypes,
                TrRmGenericMethodFormalImport[] formalRecords,
                PrimitiveType resolvedType)
        {
            EnsureGenericNominalTypeSpecs(tables, artifactIndex, moduleRow,
                typeSpecRef, methodRow, 0);
            var reference = tables.TypeSpecs.GetTypeSpecRef(typeSpecRef,
                allowInitQualifier: true);
            if (reference.Form == 0x04)
            {
                return TrRmExecutableCompiler
                    .ReadGenericNominalTypeArgument(typeSpecRef, tables,
                        GenericNominalTypeDefinitions.ToImmutable(),
                        tables.Modules.GetIdentity(moduleRow),
                        GenericNominalTypeInstances,
                        _genericNominalTypeInstanceIndexes,
                        GenericNominalPointerTypes,
                        GenericNominalPointerTypeIndexes, 0, methodRow,
                        methodFormalTypes,
                        GetFormalTypeOwnerOrdinal(artifactIndex,
                            methodRow), this, artifactIndex, moduleRow,
                        formalRecords);
            }

            var typeSpecForm = reference.Form == 0x05
                ? (byte)0 : reference.Form;
            var argument = BoundGenericNominalTypeArgument.ForMetadataType(
                resolvedType, reference.Qualifiers, typeSpecRef,
                tables.Modules.GetIdentity(moduleRow), typeSpecForm,
                reference.TypeCode, reference.ArgumentCode);
            if (resolvedType != PrimitiveType.Void
                && TryCreateG1GenericMethodTypeTerm(resolvedType,
                    methodFormalTypes, formalRecords,
                    GetFormalTypeOwnerOrdinal(artifactIndex, methodRow),
                    SpanTypes, CheckedReferenceTypeDefinitions,
                    GenericNominalTypeDefinitions.ToImmutable(),
                    GenericNominalTypeInstances, 0, -1,
                    out var typeArgumentTerm)
                && typeArgumentTerm.Kind
                    != BoundGenericMethodTypeTermKind.Concrete
                && BoundGenericMethodTypeTerms.TryGetFormalDependencies(
                    typeArgumentTerm, out var formalDependencies)
                && !formalDependencies.IsEmpty)
            {
                argument = argument with {
                    TypeArgumentTerm = typeArgumentTerm,
                };
                argument = argument with {
                    TypeFormalDefinitionOrdinal =
                        GetFormalTypeOwnerOrdinal(artifactIndex,
                            methodRow),
                    TypeFormalOrdinal =
                        typeArgumentTerm.Kind
                            == BoundGenericMethodTypeTermKind.Formal
                            ? formalDependencies[0] : -1,
                    TypeFormalOwnerKind =
                        BoundGenericTypeFormalOwnerKind.GenericMethod,
                };
            }

            return argument;
        }

        internal PrimitiveType GetGenericNominalTypeInstance(
            BoundGenericNominalTypeIdentity identity,
            ImmutableArray<BoundGenericNominalTypeArgument> arguments,
            byte qualifiers, byte typeSpecForm = 0)
        {
            var key = BoundGenericNominalTypeContract.CreateUseKey(
                identity, arguments, qualifiers, typeSpecForm);
            if (_genericNominalTypeInstanceIndexes.TryGetValue(key,
                    out var existingType))
            {
                return existingType;
            }
            if (GenericNominalTypeInstances.Count
                >= BoundGenericNominalTypeTypes.MaximumCount)
            {
                throw Unsupported(
                    "G1 generic nominal substitution exceeds the supported instance count.");
            }

            var type = BoundGenericNominalTypeTypes.Type(
                GenericNominalTypeInstances.Count);
            GenericNominalTypeInstances.Add(
                new BoundGenericNominalTypeInstance(identity, arguments,
                    qualifiers));
            _genericNominalTypeInstanceIndexes.Add(key, type);
            return type;
        }

        private void EnsureGenericNominalTypeSpecs(
            TrRmStructuralMetadataValidator.ValidatedExecutionTables tables,
            int artifactIndex, int moduleRow, uint typeSpecRef,
            int methodRow, int depth)
        {
            if (depth > SemanticVerifier.MaximumDepth)
            {
                throw Unsupported(
                    "A G1 constructed nominal TypeSpec exceeds the supported nesting depth.");
            }

            var typeSpec = tables.TypeSpecs.GetTypeSpecRef(typeSpecRef,
                allowInitQualifier: true);
            if (!typeSpec.IsRow)
            {
                return;
            }

            var row = tables.TypeSpecs.GetRow(typeSpec.Row);
            switch (typeSpec.Form)
            {
                case 0x02:
                case 0x03:
                    EnsureGenericNominalTypeDefinition(tables,
                        artifactIndex, moduleRow,
                        tables.TypeSpecs.GetNominalTypeDefRow(typeSpecRef,
                            tables.TypeDefs, tables.Imports),
                        typeSpec.Form == 0x03
                            ? checked((int)BinaryPrimitives.ReadUInt32LittleEndian(
                                row.Slice(2, sizeof(uint)))) : 0);
                    break;

                case 0x05:
                    var genericTypeSpecRef = BinaryPrimitives
                        .ReadUInt32LittleEndian(row.Slice(2, sizeof(uint)));
                    var genericTypeSpec
                        = tables.TypeSpecs.GetTypeSpecRef(
                            genericTypeSpecRef);
                    var typeDefRow = tables.TypeSpecs.GetNominalTypeDefRow(
                        genericTypeSpecRef, tables.TypeDefs, tables.Imports);
                    EnsureGenericNominalTypeDefinition(tables,
                        artifactIndex, moduleRow, typeDefRow,
                        genericTypeSpec.Form == 0x03
                            ? checked((int)BinaryPrimitives
                                .ReadUInt32LittleEndian(tables.TypeSpecs
                                    .GetRow(genericTypeSpec.Row)
                                    .Slice(2, sizeof(uint)))) : 0);
                    var (firstFormal, formalCount)
                        = tables.TypeDefs.GetGenericRange(typeDefRow);
                    if (formalCount != tables.TypeSpecs
                            .GetConstructedArgumentCount(typeSpec.Row))
                    {
                        throw Invalid(
                            "A G1 constructed nominal TypeSpec has the wrong argument count.");
                    }

                    for (var ordinal = 0; ordinal < formalCount; ordinal++)
                    {
                        if (tables.GenericParams.GetFormalKind(
                                firstFormal + ordinal) == 1)
                        {
                            EnsureGenericNominalTypeSpecs(tables,
                                artifactIndex, moduleRow,
                                tables.TypeSpecs.GetConstructedArgument(
                                    typeSpec.Row, ordinal),
                                methodRow, depth + 1);
                        }
                    }
                    break;

                case >= 0x06 and <= 0x0B:
                case 0x0E:
                    EnsureGenericNominalTypeSpecs(tables, artifactIndex,
                        moduleRow, BinaryPrimitives.ReadUInt32LittleEndian(
                            row.Slice(2, sizeof(uint))), methodRow,
                        depth + 1);
                    break;

                case 0x0C:
                    EnsureGenericNominalTypeSpecs(tables, artifactIndex,
                        moduleRow, BinaryPrimitives.ReadUInt32LittleEndian(
                            row.Slice(3, sizeof(uint))), methodRow,
                        depth + 1);
                    break;
            }
        }

        private void EnsureGenericNominalTypeDefinition(
            TrRmStructuralMetadataValidator.ValidatedExecutionTables tables,
            int artifactIndex, int moduleRow, int typeDefRow,
            int importRow = 0)
        {
            var key = (artifactIndex, moduleRow, typeDefRow);
            if (!_genericNominalDefinitionsLoaded.Add(key)
                && importRow == 0)
            {
                return;
            }

            var definitionId = 0x1300_0000u | checked((uint)typeDefRow);
            if (tables.TypeDefs.GetOwnerModule(typeDefRow) != moduleRow)
            {
                throw Invalid(
                    "A G1 constructed nominal TypeDef is owned by a different Module.");
            }

            BoundGenericNominalTypeDefinition? definition;
            if (importRow != 0
                || tables.Imports.TryGetExpectedBinding(definitionId, out _))
            {
                if (importRow <= 0
                    || tables.Imports.GetExpectedDefinitionForImportRow(
                        importRow) != definitionId)
                {
                    throw Invalid(
                        "A G1 constructed nominal TypeDef has no exact Import row.");
                }

                var moduleRefRow = tables.Imports.GetModuleRefForImportRow(
                    importRow);
                var binding = tables.Imports.GetBindingForImportRow(importRow);
                if (tables.ModuleRefs.GetOwnerModule(moduleRefRow)
                        != moduleRow
                    || tables.ModuleRefs.GetRequirements(moduleRefRow)
                        != (binding.TargetRequirement,
                            binding.RuntimePolicy)
                    || !Modules.TryGetValue(binding.ProducerIdentity,
                        out var providerSelection))
                {
                    throw Invalid(
                        "A G1 constructed nominal TypeDef Import has no exact selected provider.");
                }

                var providerTables
                    = TrRmStructuralMetadataValidator
                        .ValidateGenericMethodCallsDraftAndGetExecutionTables(
                            new TrRmArtifactIndex(
                                Artifacts[providerSelection.ArtifactIndex],
                                Schema));
                if (providerTables.Modules.GetRequirements(
                        providerSelection.ModuleRow)
                    != (binding.TargetRequirement,
                        binding.RuntimePolicy)
                    || !providerTables.Exports.TryGetDefinitionForKey(
                        providerSelection.ModuleRow, binding.ExportKey,
                        out var providerDefinitionId)
                    || (providerDefinitionId >> 24) != 0x13)
                {
                    throw Invalid(
                        "A G1 constructed nominal TypeDef Import does not select an exported provider TypeDef.");
                }

                var providerTypeDefRow = checked((int)(
                    providerDefinitionId & 0x00FF_FFFF));
                var providerDefinitions =
                    TrRmExecutableCompiler.CreateGenericNominalTypeDefinitions(
                        providerTables, binding.ProducerIdentity,
                        TrRmExecutableCompiler
                            .LoadGenericNominalEnumDefinitions(
                                providerTables),
                        moduleRow: providerSelection.ModuleRow,
                        onlyTypeDefRow: providerTypeDefRow);
                if (providerDefinitions.Length != 1)
                {
                    throw Invalid(
                        "A G1 constructed nominal TypeDef provider is not a supported public definition.");
                }

                var usingModule = TrRmExecutableCompiler
                    .CreateA2SourceTypeProviderSnapshot(tables,
                        Artifacts[artifactIndex], SchemaIdentity, moduleRow);
                var providerModule = TrRmExecutableCompiler
                    .CreateA2SourceTypeProviderSnapshot(providerTables,
                        Artifacts[providerSelection.ArtifactIndex],
                        SchemaIdentity, providerSelection.ModuleRow);
                var expectation = new BoundSourceTypeExpectation(
                    usingModule, importRow, definitionId,
                    binding.ProducerIdentity, binding.ExportKey,
                    binding.TargetRequirement, binding.RuntimePolicy,
                    providerModule, providerDefinitionId);
                definition = providerDefinitions[0] with {
                    ProviderExpectations = [expectation],
                    Provider = providerModule,
                    ExportKeys = GetGenericNominalTypeExportKeys(
                        providerTables, providerSelection.ModuleRow,
                        providerTypeDefRow),
                };
                if (GenericNominalTypeImports.Add(
                        (artifactIndex, importRow)))
                {
                    var rootTables
                        = TrRmStructuralMetadataValidator
                            .ValidateGenericMethodCallsDraftAndGetExecutionTables(
                                new TrRmArtifactIndex(Artifacts[0], Schema));
                    var schemaIdentities = new string[Artifacts.Length];
                    Array.Fill(schemaIdentities, SchemaIdentity);
                    var providerBinding
                        = TrRmFixedArrayProviderBindingValidator
                            .ValidateGenericMethodTypeDefImport(rootTables,
                                Artifacts, schemaIdentities, ModuleSelections,
                                artifactIndex, moduleRow, importRow);
                    if (providerBinding.ExpectedTypeDefRow != typeDefRow
                        || providerBinding.ProviderArtifactIndex
                            != providerSelection.ArtifactIndex
                        || providerBinding.ProviderModuleRow
                            != providerSelection.ModuleRow
                        || providerBinding.ProviderTypeDefRow
                            != providerTypeDefRow
                        || providerBinding.ActualDefinitionId
                            != providerDefinitionId)
                    {
                        throw Invalid(
                            "A G1 constructed nominal TypeDef binding changed during structural provider validation.");
                    }
                }
            }
            else
            {
                if (importRow != 0)
                {
                    throw Invalid(
                        "A G1 constructed nominal TypeSpec has an Import row that does not own its TypeDef.");
                }

                var providerIdentity = tables.Modules.GetIdentity(moduleRow);
                definition = TrRmExecutableCompiler
                    .CreateGenericNominalTypeDefinitions(tables,
                        providerIdentity,
                        TrRmExecutableCompiler
                            .LoadGenericNominalEnumDefinitions(tables),
                        moduleRow: moduleRow,
                        onlyTypeDefRow: typeDefRow)
                    .SingleOrDefault();
                if (definition is null)
                {
                    throw Unsupported(
                        "A G1 constructed nominal TypeSpec has no selected public TypeDef declaration.");
                }
                definition = definition with {
                    Provider = TrRmExecutableCompiler
                        .CreateA2SourceTypeProviderSnapshot(tables,
                            Artifacts[artifactIndex], SchemaIdentity,
                            moduleRow),
                    ExportKeys = GetGenericNominalTypeExportKeys(tables,
                        moduleRow, typeDefRow),
                };
            }

            if (_genericNominalDefinitionIndexes.TryGetValue(
                    definition.Identity, out var existingIndex))
            {
                if (!BoundGenericNominalTypeContract
                        .AreEquivalentDefinitions(
                        GenericNominalTypeDefinitions[existingIndex],
                        definition))
                {
                    throw Invalid(
                        "A G1 generic nominal TypeDef identity has conflicting declarations.");
                }
                if (!definition.ProviderExpectations.IsDefaultOrEmpty
                    || !definition.TypeDefBindingEqualityGroups.IsDefaultOrEmpty
                    || !definition.ExportKeys.IsDefaultOrEmpty
                    || !definition.SelectedExportKeys.IsDefaultOrEmpty)
                {
                    var existingDefinition
                        = GenericNominalTypeDefinitions[existingIndex];
                    GenericNominalTypeDefinitions[existingIndex]
                        = existingDefinition with {
                            ProviderExpectations = [
                                .. existingDefinition.ProviderExpectations,
                                .. definition.ProviderExpectations.Where(
                                    expectation =>
                                        !existingDefinition
                                            .ProviderExpectations.Any(
                                                existingExpectation =>
                                                    BoundGenericNominalTypeContract
                                                        .AreEquivalentProviderExpectations(
                                                            existingExpectation,
                                                            expectation))),
                            ],
                            TypeDefBindingEqualityGroups =
                                BoundGenericNominalTypeContract
                                    .MergeTypeDefBindingEqualityGroups(
                                        existingDefinition
                                            .TypeDefBindingEqualityGroups,
                                        definition
                                            .TypeDefBindingEqualityGroups),
                            Provider = existingDefinition.Provider
                                ?? definition.Provider,
                            ExportKeys = [
                                .. existingDefinition.ExportKeys
                                    .Concat(definition.ExportKeys)
                                    .Distinct(StringComparer.Ordinal),
                            ],
                            SelectedExportKeys = [
                                .. existingDefinition.SelectedExportKeys
                                    .Concat(definition.SelectedExportKeys)
                                    .Distinct(StringComparer.Ordinal),
                            ],
                        };
                }
                return;
            }

            if (GenericNominalTypeDefinitions.Count
                >= BoundGenericNominalTypeTypes.MaximumCount)
            {
                throw Unsupported(
                    "The G1 generic nominal TypeDef count exceeds the supported limit.");
            }
            _genericNominalDefinitionIndexes.Add(definition.Identity,
                GenericNominalTypeDefinitions.Count);
            GenericNominalTypeDefinitions.Add(definition);
        }

        private static ImmutableArray<string>
            GetGenericNominalTypeExportKeys(
            TrRmStructuralMetadataValidator.ValidatedExecutionTables tables,
            int moduleRow, int typeDefRow)
        {
            var definitionId = 0x1300_0000u | checked((uint)typeDefRow);
            return [.. tables.Exports.GetExportsForModule(moduleRow)
                .Where(export => export.Definition == definitionId)
                .Select(static export => export.ExportKey)
                .OrderBy(static exportKey => exportKey,
                    StringComparer.Ordinal)];
        }

        internal GenericMethodReadContext(TrRmStandardArtifactInput input,
            IReadOnlyList<TrRmStandardArtifactInput> providers)
        {
            ArgumentNullException.ThrowIfNull(input);
            ArgumentNullException.ThrowIfNull(providers);
            SchemaIdentity = input.SchemaIdentity;
            Schema = SchemaIdentity switch
            {
                StandardGenericMethodCallsSchemaIdentity
                    => TrRmArtifactSchema.GenericMethodCallsDraft,
                StandardGenericMethodCallsParameterLabelsSchemaIdentity
                    => TrRmArtifactSchema
                        .GenericMethodCallsParameterLabelsDraft,
                StandardGenericMethodCallsParameterLabelsReceiverAccessSchemaIdentity
                    => TrRmArtifactSchema
                        .GenericMethodCallsParameterLabelsReceiverAccessDraft,
                StandardForeignNativeInterfaceSchemaIdentity
                    => TrRmArtifactSchema.ForeignNativeInterfaceDraft,
                StandardForeignNativeInterfaceParameterSurfaceSchemaIdentity
                    => TrRmArtifactSchema
                        .ForeignNativeInterfaceParameterSurfaceDraft,
                StandardC1L1R1SchemaIdentity
                    => TrRmArtifactSchema.C1ParameterLabelsReceiverAccessDraft,
                StandardC1M1L1R1SchemaIdentity
                    => TrRmArtifactSchema
                        .C1M1ParameterLabelsReceiverAccessDraft,
                StandardC1M1ConstructionL1R1SchemaIdentity
                    => TrRmArtifactSchema
                        .C1M1ConstructionParameterLabelsReceiverAccessDraft,
                StandardC1M1Construction2SchemaIdentity
                    => TrRmArtifactSchema.C1M1Construction2Draft,
                StandardC1M1Construction2InitializerCopyEvidenceSchemaIdentity
                    => TrRmArtifactSchema.C1M1Construction2InitializerCopyEvidenceDraft,
                StandardC1M1Construction2TypeBindingViewsSchemaIdentity
                    => TrRmArtifactSchema.C1M1Construction2InitializerCopyEvidenceDraft,
                StandardGenericMethodSchema
                    .C1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsClassTypeBindingViews
                    => TrRmArtifactSchema
                        .C1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsInitializerCopyEvidenceDraft,
                StandardC1M1Construction2L1R1SchemaIdentity
                    => TrRmArtifactSchema
                        .C1M1Construction2ParameterLabelsReceiverAccessDraft,
                StandardC1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsSchemaIdentity
                    => TrRmArtifactSchema
                        .C1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsDraft,
                StandardC1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsInitializerCopyEvidenceSchemaIdentity
                    => TrRmArtifactSchema
                        .C1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsInitializerCopyEvidenceDraft,
                StandardC1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsTypeBindingViewsSchemaIdentity
                    => TrRmArtifactSchema
                        .C1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsInitializerCopyEvidenceDraft,
                StandardCM1AR1SchemaIdentity
                    => TrRmArtifactSchema
                        .C1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsAssociatedRelationConstraintsDraft,
                StandardC1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsAssociatedRelationConstraintsInitializerCopyEvidenceSchemaIdentity
                    => TrRmArtifactSchema
                        .C1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsAssociatedRelationConstraintsInitializerCopyEvidenceDraft,
                StandardC1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsAssociatedRelationConstraintsTypeBindingViewsSchemaIdentity
                    => TrRmArtifactSchema
                        .C1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsAssociatedRelationConstraintsInitializerCopyEvidenceDraft,
                StandardGenericMethodSchema
                    .C1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsAssociatedRelationConstraintsClassTypeBindingViews
                    => TrRmArtifactSchema
                        .C1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsAssociatedRelationConstraintsInitializerCopyEvidenceDraft,
                StandardC1M1ConstructionL1R1PropertyInitializedStateSchemaIdentity
                    => TrRmArtifactSchema
                        .C1M1ConstructionL1R1PropertyInitializedStateDraft,
                StandardC1M1Construction2L1R1PropertyInitializedStateSchemaIdentity
                    => TrRmArtifactSchema
                        .C1M1Construction2L1R1PropertyInitializedStateDraft,
                StandardC1M1ConstructionL1R1EnumClosureSchemaIdentity
                    => TrRmArtifactSchema.C1M1ConstructionL1R1EnumClosureDraft,
                StandardC1M1Construction2L1R1EnumClosureSchemaIdentity
                    => TrRmArtifactSchema.C1M1Construction2L1R1EnumClosureDraft,
                StandardC1M1ConstructionL1R1PropertyInitializedStateEnumClosureSchemaIdentity
                    => TrRmArtifactSchema.C1M1ConstructionL1R1PropertyInitializedStateEnumClosureDraft,
                StandardC1M1Construction2L1R1PropertyInitializedStateEnumClosureSchemaIdentity
                    => TrRmArtifactSchema.C1M1Construction2L1R1PropertyInitializedStateEnumClosureDraft,
                StandardC1M1ConstructionL1R1EnumClosureInitializerCopyEvidenceSchemaIdentity
                    => TrRmArtifactSchema.C1M1ConstructionL1R1EnumClosureInitializerCopyEvidenceDraft,
                StandardC1M1Construction2L1R1EnumClosureInitializerCopyEvidenceSchemaIdentity
                    => TrRmArtifactSchema.C1M1Construction2L1R1EnumClosureInitializerCopyEvidenceDraft,
                StandardC1M1ConstructionL1R1ClassInstanceLayoutInitializerCopyEvidenceSchemaIdentity
                    => TrRmArtifactSchema.C1M1ConstructionL1R1EnumClosureClassInstanceLayoutInitializerCopyEvidenceDraft,
                StandardC1M1Construction2L1R1ClassInstanceLayoutInitializerCopyEvidenceSchemaIdentity
                    => TrRmArtifactSchema.C1M1Construction2L1R1EnumClosureClassInstanceLayoutInitializerCopyEvidenceDraft,
                StandardC1M1ConstructionL1R1ClassInstanceLayoutSchemaIdentity
                    => TrRmArtifactSchema.C1M1ConstructionL1R1EnumClosureDraft,
                StandardC1M1Construction2L1R1ClassInstanceLayoutSchemaIdentity
                    => TrRmArtifactSchema.C1M1Construction2L1R1EnumClosureDraft,
                StandardC1M1ConstructionL1R1PropertyInitializedStateClassInstanceLayoutSchemaIdentity
                    => TrRmArtifactSchema.C1M1ConstructionL1R1PropertyInitializedStateEnumClosureDraft,
                StandardC1M1Construction2L1R1PropertyInitializedStateClassInstanceLayoutSchemaIdentity
                    => TrRmArtifactSchema.C1M1Construction2L1R1PropertyInitializedStateEnumClosureDraft,
                _ => throw Unsupported(
                    "The generic-method source reference requires an exact label-bearing G1/C1 schema selection."),
            };
            var inputs = new TrRmStandardArtifactInput[
                checked(providers.Count + 1)];
            inputs[0] = input;
            for (var index = 1; index < inputs.Length; index++)
            {
                inputs[index] = providers[index - 1]
                    ?? throw new ArgumentNullException(
                        $"{nameof(providers)}[{index - 1}]");
            }

            Artifacts = new byte[inputs.Length][];
            Digests = new ImmutableArray<byte>[inputs.Length];
            for (var index = 0; index < inputs.Length; index++)
            {
                if (!StringComparer.Ordinal.Equals(
                        inputs[index].SchemaIdentity,
                        SchemaIdentity))
                {
                    throw Unsupported(
                        "The generic-method source reference requires the selected exact schema for every supplied artifact.");
                }

                Artifacts[index] = inputs[index].Artifact.ToArray();
                Digests[index] = [.. SHA256.HashData(Artifacts[index])];
            }

            var moduleSelections = ImmutableArray.CreateBuilder<
                TrRmStandardModuleSelection>();
            for (var index = 0; index < Artifacts.Length; index++)
            {
                var artifactIndex = new TrRmArtifactIndex(Artifacts[index],
                    Schema);
                var tables = Schema.UsesForeignNativeInterface()
                    ? TrRmStructuralMetadataValidator
                        .ValidateAndGetExecutionTables(artifactIndex)
                    : TrRmStructuralMetadataValidator
                        .ValidateGenericMethodCallsDraftAndGetExecutionTables(
                            artifactIndex);
                if (!Schema.UsesForeignNativeInterface())
                {
                    ValidateGenericMethodCallBodies(tables);
                }
                for (var moduleRow = 1; moduleRow <= tables.Modules.Count;
                    moduleRow++)
                {
                    var identity = tables.Modules.GetIdentity(moduleRow);
                    if (!Modules.TryAdd(identity, (index, moduleRow)))
                    {
                        throw Invalid(
                            "The supplied G1 artifacts contain an ambiguous Module identity.");
                    }

                    var requirements
                        = tables.Modules.GetRequirements(moduleRow);
                    ModuleRequirements.Add((index, moduleRow),
                        requirements);
                    var (methodStart, methodEnd)
                        = tables.Modules.GetMethodDefRange(moduleRow);
                    moduleSelections.Add(new TrRmStandardModuleSelection(
                        index, moduleRow, identity,
                        requirements.TargetRequirement,
                        requirements.RuntimePolicy,
                        tables.Modules.GetEntryMethod(moduleRow),
                        methodStart, methodEnd) {
                        GoverningRuleEdition =
                            tables.Modules.GetGoverningRuleEdition(
                                moduleRow),
                    });
                }
            }
            ModuleSelections = moduleSelections.MoveToImmutable();
        }

        internal void CompleteBindings()
        {
            foreach (var (method, import) in MethodViews)
            {
                if (!DecodedMethods.TryGetValue(method, out var function))
                {
                    throw Invalid(
                        "A G1 MethodDef view has no reconstructed open Body.");
                }

                if (!DecodedMethodSupportFunctions.TryGetValue(method,
                        out var supportFunctions))
                {
                    throw Invalid(
                        "A G1 MethodDef view has no reconstructed proof support functions.");
                }

                import.CompleteProofFunction(function, supportFunctions);
            }

            foreach (var target in ExpectedTargets)
            {
                var binding = target.Binding!;
                if (binding.Provider.ProofFunction.IsExternal)
                {
                    throw Invalid(
                        "The selected G1 provider has no reconstructed open Body.");
                }

                CompleteExpectedGenericMethodBody(target, binding,
                    Schema.UsesGeneralMethodParameterLabels());
            }
        }
    }

    private static void ValidateGenericMethodModuleImports(
        TrRmStructuralMetadataValidator.ValidatedExecutionTables tables,
        GenericMethodReadContext context, int artifactIndex, int moduleRow)
    {
        if (!context.ModulesWithValidatedImports.Add(
                (artifactIndex, moduleRow)))
        {
            return;
        }

        for (var importRow = 1; importRow <= tables.Imports.Count;
            importRow++)
        {
            var moduleRefRow = tables.Imports.GetModuleRefForImportRow(
                importRow);
            if (tables.ModuleRefs.GetOwnerModule(moduleRefRow) != moduleRow)
            {
                continue;
            }

            var definition = tables.Imports
                .GetExpectedDefinitionForImportRow(importRow);
            if ((definition >> 24) != 0x16)
            {
                continue;
            }

            var methodRow = checked((int)(definition & 0x00FF_FFFF));
            if (tables.Signatures.GetGenericCount(
                    tables.Methods.GetSignature(methodRow)) == 0)
            {
                continue;
            }

            _ = CreateExpectedGenericMethodImport(tables, context,
                artifactIndex, moduleRow, importRow);
        }
    }

    private static BoundGenericMethodImport CreateExpectedGenericMethodImport(
        TrRmStructuralMetadataValidator.ValidatedExecutionTables tables,
        GenericMethodReadContext context, int artifactIndex, int moduleRow,
        int importRow)
    {
        var importKey = (artifactIndex, importRow);
        if (context.ImportTargets.TryGetValue(importKey, out var cachedImport))
        {
            return cachedImport;
        }
        if (context.MethodsBeingDecoded.Count
                + context.ImportsBeingDecoded.Count
            >= SemanticVerifier.MaximumDepth)
        {
            throw Unsupported(
                "The G1 generic-method provider closure exceeds the supported nesting limit.");
        }

        _ = context.ImportsBeingDecoded.Add(importKey);
        try
        {
            return CreateExpectedGenericMethodImportCore(tables, context,
                artifactIndex, moduleRow, importRow);
        }
        finally
        {
            _ = context.ImportsBeingDecoded.Remove(importKey);
        }
    }

    private static BoundGenericMethodImport CreateExpectedGenericMethodImportCore(
        TrRmStructuralMetadataValidator.ValidatedExecutionTables tables,
        GenericMethodReadContext context, int artifactIndex, int moduleRow,
        int importRow)
    {
        var definitionId = tables.Imports
            .GetExpectedDefinitionForImportRow(importRow);
        if ((definitionId >> 24) != 0x16)
        {
            throw Invalid(
                "The selected GenericCall Import does not expect a MethodDef.");
        }

        var methodRow = checked((int)(definitionId & 0x00FF_FFFF));
        var expected = CreateGenericMethodImportCore(tables, context,
            artifactIndex, moduleRow, definitionId, "",
            isMethodDefTarget: true, decodeBody: false);
        var target = new BoundGenericMethodImport(expected.Origin,
            expected.Signature, expected.Formals, expected.ProofFunction,
            expected.FormalTypeOwnerOrdinal, expected.SpanTypes,
            expected.OrdinaryParameterDefaults);
        context.ImportTargets.Add((artifactIndex, importRow), target);
        var moduleRefRow = tables.Imports.GetModuleRefForImportRow(
            importRow);
        var selected = tables.Imports.GetBindingForImportRow(importRow);
        if (tables.ModuleRefs.GetOwnerModule(moduleRefRow) != moduleRow
            || tables.Methods.GetBodyRow(methodRow) != 0
            || !context.Modules.TryGetValue(selected.ProducerIdentity,
                out var providerSelection))
        {
            throw Invalid(
                "The G1 expected MethodDef has no exact selected ModuleRef provider.");
        }

        var provider = TrRmStructuralMetadataValidator
            .ValidateGenericMethodCallsDraftAndGetExecutionTables(
                new TrRmArtifactIndex(
                    context.Artifacts[providerSelection.ArtifactIndex],
                    context.Schema));
        var providerModuleRow = providerSelection.ModuleRow;
        var (targetRequirement, runtimePolicy)
            = provider.Modules.GetRequirements(providerModuleRow);
        if (targetRequirement != selected.TargetRequirement
            || runtimePolicy != selected.RuntimePolicy
            || !provider.Exports.TryGetDefinitionForKey(providerModuleRow,
                selected.ExportKey, out var providerDefinition)
            || (providerDefinition >> 24) != 0x16)
        {
            throw Invalid(
                "The selected G1 ModuleRef requirements or MethodDef ExportKey do not match the retained Import.");
        }

        var expectedKey = (artifactIndex, methodRow);
        var providerKey = (providerSelection.ArtifactIndex, providerDefinition);
        if (context.ExpectedProviders.TryGetValue(expectedKey,
                out var previousProvider)
            && previousProvider != providerKey)
        {
            throw Invalid(
                "One G1 expected MethodDef cannot select conflicting provider definitions.");
        }
        context.ExpectedProviders[expectedKey] = providerKey;

        ValidateGenericMethodModuleImports(provider, context,
            providerSelection.ArtifactIndex, providerModuleRow);
        var imported = CreateGenericMethodImport(provider, context,
            providerSelection.ArtifactIndex, providerModuleRow,
            providerDefinition, selected.ExportKey,
            isMethodDefTarget: false);
        ValidateGenericMethodProviderContract(expected, imported, context);
        var expectedInterfaceDefinitions = CreateGenericInterfaceDefinitions(
            tables, context, artifactIndex, moduleRow);
        var binding = new BoundGenericMethodImportBinding(importRow,
            moduleRefRow, selected.ProducerIdentity, selected.ExportKey,
            selected.TargetRequirement, selected.RuntimePolicy,
            expected, imported, expectedInterfaceDefinitions);
        target.CompleteBinding(binding);
        CompleteExpectedGenericMethodBody(target, binding,
            context.Schema.UsesGeneralMethodParameterLabels());
        context.ExpectedTargets.Add(target);
        return target;
    }

    private static void CompleteExpectedGenericMethodBody(
        BoundGenericMethodImport target,
        BoundGenericMethodImportBinding binding,
        bool preserveExpectedParameterNames)
    {
        var expected = binding.Expected.ProofFunction;
        var provider = binding.Provider.ProofFunction;
        if (preserveExpectedParameterNames)
        {
            if (expected.Variables.Length < expected.ParameterCount
                || provider.Variables.Length < expected.ParameterCount)
            {
                throw Invalid(
                    "The selected G1L1 provider cannot preserve the expected parameter-label view.");
            }

            var variables = provider.Variables.ToBuilder();
            for (var ordinal = 0; ordinal < expected.ParameterCount;
                ordinal++)
            {
                variables[ordinal] = variables[ordinal] with {
                    Name = expected.Variables[ordinal].Name,
                };
            }

            provider = provider with {
                Variables = variables.MoveToImmutable(),
            };
        }

        target.CompleteProofFunction(provider with {
            Name = expected.Name,
            RequiresUnsafeCaller = expected.RequiresUnsafeCaller,
            DoesNotReturn = expected.DoesNotReturn,
        }, binding.Provider.ProofSupportFunctions,
            binding.Provider.FormalTypeOwnerOrdinal);
    }

    private static void ValidateGenericMethodProviderContract(
        BoundGenericMethodImport expected, BoundGenericMethodImport provider,
        GenericMethodReadContext context)
    {
        var expectedFunction = expected.ProofFunction;
        var providerFunction = provider.ProofFunction;
        var expectedReceiverCount = expected.Signature.IsStatic ? 0 : 1;
        var providerReceiverCount = provider.Signature.IsStatic ? 0 : 1;
        var expectedParameterCount
            = expectedFunction.ParameterCount - expectedReceiverCount;
        var providerParameterCount
            = providerFunction.ParameterCount - providerReceiverCount;
        if (expected.Formals.Length != provider.Formals.Length
            || expected.Signature.IsStatic != provider.Signature.IsStatic
            || expected.Signature.IsMutable
                != provider.Signature.IsMutable
            || expectedFunction.IsStatic != expected.Signature.IsStatic
            || providerFunction.IsStatic != provider.Signature.IsStatic
            || expectedFunction.IsMutable != expected.Signature.IsMutable
            || providerFunction.IsMutable != provider.Signature.IsMutable
            || (expected.Signature.IsStatic
                && expected.Signature.IsMutable)
            || expectedFunction.ParameterCount < expectedReceiverCount
            || providerFunction.ParameterCount < providerReceiverCount
            || expectedFunction.Variables.Length
                < expectedFunction.ParameterCount
            || providerFunction.Variables.Length
                < providerFunction.ParameterCount
            || expectedParameterCount < 0 || providerParameterCount < 0
            || expectedParameterCount != providerParameterCount
            || !AreEquivalentGenericMethodImportTypes(
                expectedFunction.ReturnType, providerFunction.ReturnType,
                expected, provider, context)
            || (providerFunction.RequiresUnsafeCaller
                && !expectedFunction.RequiresUnsafeCaller)
            || (expectedFunction.DoesNotReturn
                && !providerFunction.DoesNotReturn)
            || !GenericMethodDependencyContractsSatisfy(
                expectedFunction.DependencyContracts,
                providerFunction.DependencyContracts))
        {
            throw Invalid(
                "The selected G1 provider has an incompatible generic signature or Effects contract.");
        }

        if (expectedReceiverCount != 0
            && (!AreEquivalentGenericMethodImportTypes(
                    expectedFunction.Variables[0].Type,
                    providerFunction.Variables[0].Type,
                    expected, provider, context)
                || expectedFunction.Variables[0].IsMutable
                    != providerFunction.Variables[0].IsMutable))
        {
            throw Invalid(
                "The selected G1 provider has an incompatible class receiver.");
        }

        for (var ordinal = 0; ordinal < expected.Formals.Length; ordinal++)
        {
            var expectedFormal = expected.Formals[ordinal];
            var providerFormal = provider.Formals[ordinal];
            if ((providerFormal.OwnershipDomain != 3
                    && providerFormal.OwnershipDomain
                        != expectedFormal.OwnershipDomain)
                || providerFormal.NullabilityDomain
                    < expectedFormal.NullabilityDomain
                || (providerFormal.RequiresWritable
                    && !expectedFormal.RequiresWritable)
                || (providerFormal.RequiresBlittable
                    && !expectedFormal.RequiresBlittable)
                || expectedFormal.InterfaceConstraints.IsDefault
                || providerFormal.InterfaceConstraints.IsDefault
                || expectedFormal.AssociatedRelations.IsDefault
                || providerFormal.AssociatedRelations.IsDefault)
            {
                throw Invalid(
                    "The selected G1 provider changes a retained generic default or strengthens a caller obligation.");
            }
            if (providerFormal.InterfaceConstraints.Any(
                    providerConstraint =>
                        !expectedFormal.InterfaceConstraints.Any(
                            expectedConstraint =>
                                BoundInterfaceContract
                                    .AreEquivalentProviderInterfaceViews(
                                    expectedConstraint,
                                    providerConstraint))))
            {
                throw Invalid(
                    "The selected G1 provider changes a retained interface constraint identity or strengthens an interface obligation.");
            }
            if (providerFormal.AssociatedRelations.Any(
                    providerRelation =>
                        !expectedFormal.AssociatedRelations.Any(
                            expectedRelation =>
                                expectedRelation.FormalOrdinal
                                    == providerRelation.FormalOrdinal
                                && BoundInterfaceContract
                                    .AreEquivalentAssociatedTypeIdentities(
                                        expectedRelation.AssociatedType,
                                        providerRelation.AssociatedType)
                                && AreEquivalentGenericMethodImportTypeTerms(
                                    expectedRelation.RequiredType,
                                    providerRelation.RequiredType,
                                    expected, provider, context, 0))))
            {
                throw Invalid(
                    "The selected G1 provider adds or changes an associated-relation obligation.");
            }
            if (!PreservesGenericMethodTypeDefault(
                    expectedFormal.TypeDefault,
                    providerFormal.TypeDefault, expected, provider,
                    context))
            {
                throw Invalid(
                    "The selected G1 provider changes a retained generic type default.");
            }
        }

        for (var ordinal = 0; ordinal < expectedParameterCount; ordinal++)
        {
            if (!AreEquivalentGenericMethodImportTypes(
                    expectedFunction.Variables[
                        expectedReceiverCount + ordinal].Type,
                    providerFunction.Variables[
                        providerReceiverCount + ordinal].Type,
                    expected, provider, context))
            {
                throw Invalid(
                    "The selected G1 provider has an incompatible ordered parameter type.");
            }
        }
    }

    internal static bool GenericMethodDependencyContractsSatisfy(
        ImmutableArray<BoundDependencyContract> expected,
        ImmutableArray<BoundDependencyContract> provider)
    {
        if (expected.IsDefault || provider.IsDefault)
        {
            return false;
        }

        static bool TryGetResultDependencies(
            ImmutableArray<BoundDependencyContract> contracts,
            out BoundDependencyContract? dependsOn,
            out BoundDependencyContract? borrowSourceStorage,
            out BoundDependencyContract? forwardsBacking)
        {
            dependsOn = null;
            borrowSourceStorage = null;
            forwardsBacking = null;
            foreach (var contract in contracts)
            {
                if (contract.Destination
                        != BoundDependencyDestination.Result
                    || contract.DestinationOrdinal is not null
                    || contract.Sources.IsDefault
                    || contract.Relation is not (
                        BoundDependencyRelation.DependsOn
                        or BoundDependencyRelation.ForwardsBacking
                        or BoundDependencyRelation.BorrowSourceStorage))
                {
                    return false;
                }

                if (contract.Relation == BoundDependencyRelation.DependsOn)
                {
                    if (dependsOn is not null)
                    {
                        return false;
                    }
                    dependsOn = contract;
                }
                else if (contract.Relation
                    == BoundDependencyRelation.ForwardsBacking)
                {
                    if (forwardsBacking is not null)
                    {
                        return false;
                    }
                    forwardsBacking = contract;
                }
                else
                {
                    if (borrowSourceStorage is not null)
                    {
                        return false;
                    }
                    borrowSourceStorage = contract;
                }
            }

            if ((borrowSourceStorage is not null
                    && borrowSourceStorage.Sources.IsEmpty)
                || (forwardsBacking is not null
                    && forwardsBacking.Sources.IsEmpty))
            {
                return false;
            }
            if (dependsOn is null)
            {
                return borrowSourceStorage is null && forwardsBacking is null;
            }

            var sources = dependsOn.Sources.ToHashSet();
            var borrowed = borrowSourceStorage?.Sources.ToHashSet()
                ?? new HashSet<BoundDependencySource>();
            var forwarded = forwardsBacking?.Sources.ToHashSet()
                ?? new HashSet<BoundDependencySource>();
            var borrowedCount = borrowSourceStorage?.Sources.Length ?? 0;
            var forwardedCount = forwardsBacking?.Sources.Length ?? 0;
            return sources.Count == dependsOn.Sources.Length
                && borrowed.Count == borrowedCount
                && forwarded.Count == forwardedCount
                && borrowed.IsSubsetOf(sources)
                && forwarded.IsSubsetOf(sources)
                && !borrowed.Overlaps(forwarded);
        }

        if (!TryGetResultDependencies(expected, out var expectedDependsOn,
                out var expectedStorage, out var expectedForwarding)
            || !TryGetResultDependencies(provider, out var providerDependsOn,
                out var providerStorage, out var providerForwarding)
            || (expectedDependsOn is null) != (providerDependsOn is null))
        {
            return false;
        }
        if (expectedDependsOn is null)
        {
            return true;
        }

        var expectedSources = expectedDependsOn.Sources.ToHashSet();
        var providerSources = providerDependsOn!.Sources.ToHashSet();
        if (!providerSources.IsSubsetOf(expectedSources))
        {
            return false;
        }

        var expectedStorageSources = expectedStorage?.Sources.ToHashSet()
            ?? new HashSet<BoundDependencySource>();
        var expectedForwarded
            = expectedForwarding?.Sources.ToHashSet()
                ?? new HashSet<BoundDependencySource>();
        var providerStorageSources = providerStorage?.Sources.ToHashSet()
            ?? new HashSet<BoundDependencySource>();
        var providerForwarded
            = providerForwarding?.Sources.ToHashSet()
                ?? new HashSet<BoundDependencySource>();
        foreach (var source in expectedSources)
        {
            var expectedPermission = expectedStorageSources.Contains(source)
                ? 1 : expectedForwarded.Contains(source) ? 2 : 3;
            var providerPermission = !providerSources.Contains(source)
                ? 0
                : providerStorageSources.Contains(source)
                    ? 1
                    : providerForwarded.Contains(source) ? 2 : 3;
            if (((providerPermission & ~expectedPermission) & 0x03) != 0)
            {
                return false;
            }
        }
        return true;
    }

    private static bool AreEquivalentGenericMethodImportTypes(
        PrimitiveType expectedType, PrimitiveType providerType,
        BoundGenericMethodImport expected, BoundGenericMethodImport provider,
        GenericMethodReadContext context,
        int depth = 0, bool allowFormal = true)
    {
        if (depth > SemanticVerifier.MaximumDepth)
        {
            return false;
        }

        for (var ordinal = 0; allowFormal
            && ordinal < expected.Formals.Length; ordinal++)
        {
            if (expected.Formals[ordinal].PlaceholderType == expectedType)
            {
                return provider.Formals[ordinal].PlaceholderType
                    == providerType;
            }
        }

        var expectedIsSpan = BoundSpanTypeTypes.IsValidType(expectedType,
            expected.SpanTypes);
        var providerIsSpan = BoundSpanTypeTypes.IsValidType(providerType,
            provider.SpanTypes);
        if (expectedIsSpan || providerIsSpan)
        {
            if (!expectedIsSpan || !providerIsSpan)
            {
                return false;
            }

            var expectedSpan = expected.SpanTypes[
                BoundSpanTypeTypes.Index(expectedType)];
            var providerSpan = provider.SpanTypes[
                BoundSpanTypeTypes.Index(providerType)];
            if (expectedSpan.IsWritable != providerSpan.IsWritable
                || expectedSpan.IsInitialization
                    != providerSpan.IsInitialization
                || expectedSpan.IsElementWritable
                    != providerSpan.IsElementWritable
                || expectedSpan.ElementFormalOrdinal
                    != providerSpan.ElementFormalOrdinal)
            {
                return false;
            }

            if (expectedSpan.ElementFormalDefinitionOrdinal
                == BoundSpanTypeContract
                    .ImportedFormalDefinitionOrdinal
                    - expected.FormalTypeOwnerOrdinal
                || providerSpan.ElementFormalDefinitionOrdinal
                    == BoundSpanTypeContract
                        .ImportedFormalDefinitionOrdinal
                        - provider.FormalTypeOwnerOrdinal)
            {
                return expectedSpan.ElementFormalDefinitionOrdinal
                        == BoundSpanTypeContract
                            .ImportedFormalDefinitionOrdinal
                            - expected.FormalTypeOwnerOrdinal
                    && providerSpan.ElementFormalDefinitionOrdinal
                        == BoundSpanTypeContract
                            .ImportedFormalDefinitionOrdinal
                            - provider.FormalTypeOwnerOrdinal
                    && (uint)expectedSpan.ElementFormalOrdinal
                        < (uint)expected.Formals.Length
                    && (uint)providerSpan.ElementFormalOrdinal
                        < (uint)provider.Formals.Length
                    && expected.Formals[
                        expectedSpan.ElementFormalOrdinal].PlaceholderType
                        == expectedSpan.ElementType
                    && provider.Formals[
                        providerSpan.ElementFormalOrdinal].PlaceholderType
                        == providerSpan.ElementType
                    && expectedSpan.ElementFormalOrdinal
                        == providerSpan.ElementFormalOrdinal;
            }

            return expectedSpan.ElementFormalDefinitionOrdinal == -1
                && providerSpan.ElementFormalDefinitionOrdinal == -1
                && AreEquivalentGenericMethodImportTypes(
                    expectedSpan.ElementType, providerSpan.ElementType,
                    expected, provider, context, depth + 1,
                    allowFormal: false);
        }

        var expectedIsGenericNominal
            = BoundGenericNominalTypeTypes.IsType(expectedType);
        var providerIsGenericNominal
            = BoundGenericNominalTypeTypes.IsType(providerType);
        if (expectedIsGenericNominal || providerIsGenericNominal)
        {
            if (!expectedIsGenericNominal || !providerIsGenericNominal)
            {
                return false;
            }

            var genericNominalDefinitions
                = context.GenericNominalTypeDefinitions.ToImmutable();
            var expectedFormalTypes = expected.Formals.Select(
                static formal => formal.PlaceholderType).ToImmutableArray();
            var providerFormalTypes = provider.Formals.Select(
                static formal => formal.PlaceholderType).ToImmutableArray();
            var expectedFormalRecords
                = CreateGenericMethodFormalImportRecords(expected);
            var providerFormalRecords
                = CreateGenericMethodFormalImportRecords(provider);
            return TryCreateG1GenericMethodTypeTerm(expectedType,
                    expectedFormalTypes, expectedFormalRecords,
                    expected.FormalTypeOwnerOrdinal, context.SpanTypes,
                    context.CheckedReferenceTypeDefinitions,
                    genericNominalDefinitions,
                    context.GenericNominalTypeInstances, 0,
                    expectedFormalTypes.IndexOf(expectedType),
                    out var expectedTerm)
                && TryCreateG1GenericMethodTypeTerm(providerType,
                    providerFormalTypes, providerFormalRecords,
                    provider.FormalTypeOwnerOrdinal, context.SpanTypes,
                    context.CheckedReferenceTypeDefinitions,
                    genericNominalDefinitions,
                    context.GenericNominalTypeInstances, 0,
                    providerFormalTypes.IndexOf(providerType),
                    out var providerTerm)
                && AreEquivalentGenericMethodImportTypeTerms(
                    expectedTerm, providerTerm, expected, provider, context,
                    depth + 1);
        }

        return expectedType == providerType;
    }

    private static TrRmGenericMethodFormalImport[]
        CreateGenericMethodFormalImportRecords(
            BoundGenericMethodImport import)
    {
        var records = new TrRmGenericMethodFormalImport[import.Formals.Length];
        for (var ordinal = 0; ordinal < records.Length; ordinal++)
        {
            var formal = import.Formals[ordinal];
            records[ordinal] = new TrRmGenericMethodFormalImport(
                formal.PlaceholderType, formal.Name,
                formal.OwnershipDomain, formal.NullabilityDomain,
                formal.RequiresWritable, formal.RequiresBlittable,
                formal.InterfaceConstraints, formal.TypeDefault) {
                AssociatedRelations = formal.AssociatedRelations,
            };
        }

        return records;
    }

    private static bool PreservesGenericMethodTypeDefault(
        BoundGenericMethodTypeDefault? expectedDefault,
        BoundGenericMethodTypeDefault? providerDefault,
        BoundGenericMethodImport expected, BoundGenericMethodImport provider,
        GenericMethodReadContext context)
    {
        if (expectedDefault is null)
        {
            return true;
        }
        if (providerDefault is null
            || expectedDefault.Term.Kind != providerDefault.Term.Kind
            || !expectedDefault.FormalDependencies.SequenceEqual(
                providerDefault.FormalDependencies))
        {
            return false;
        }

        return expectedDefault.Term.Kind switch {
            BoundGenericMethodTypeTermKind.Formal
                => expectedDefault.Term.FormalOrdinal
                    == providerDefault.Term.FormalOrdinal,
            BoundGenericMethodTypeTermKind.Concrete
                => AreEquivalentGenericMethodImportTypes(
                    expectedDefault.Term.ConcreteType,
                    providerDefault.Term.ConcreteType, expected, provider,
                    context, allowFormal: false),
            BoundGenericMethodTypeTermKind.CheckedReference
                => AreEquivalentGenericMethodImportTypeTerms(
                    expectedDefault.Term, providerDefault.Term,
                    expected, provider, context, 0),
            BoundGenericMethodTypeTermKind.Span
                => AreEquivalentGenericMethodImportTypeTerms(
                    expectedDefault.Term, providerDefault.Term,
                    expected, provider, context, 0),
            BoundGenericMethodTypeTermKind.ImportedGeneric
                => AreEquivalentGenericMethodImportTypeTerms(
                    expectedDefault.Term, providerDefault.Term,
                    expected, provider, context, 0),
            _ => false,
        };
    }

    private static bool AreEquivalentGenericMethodImportTypeTerms(
        BoundGenericMethodTypeTerm expectedTerm,
        BoundGenericMethodTypeTerm providerTerm,
        BoundGenericMethodImport expected,
        BoundGenericMethodImport provider, GenericMethodReadContext context,
        int depth)
    {
        if (depth > SemanticVerifier.MaximumDepth
            || expectedTerm.Kind != providerTerm.Kind
            || expectedTerm.FormalOrdinal != providerTerm.FormalOrdinal
            || expectedTerm.IsWritable != providerTerm.IsWritable
            || expectedTerm.IsInitialization
                != providerTerm.IsInitialization
            || expectedTerm.IsElementWritable
                != providerTerm.IsElementWritable
            || expectedTerm.IsGarbageCollected
                != providerTerm.IsGarbageCollected
            || expectedTerm.IsOptional != providerTerm.IsOptional
            || expectedTerm.AllowsVoid != providerTerm.AllowsVoid
            || expectedTerm.ElementCount != providerTerm.ElementCount
            || expectedTerm.CallingConvention
                != providerTerm.CallingConvention
            || expectedTerm.ParameterCount != providerTerm.ParameterCount
            || expectedTerm.NominalTypeIdentity
                != providerTerm.NominalTypeIdentity
            || expectedTerm.TypeArguments.IsDefault
            || providerTerm.TypeArguments.IsDefault
            || expectedTerm.TypeArguments.Length
                != providerTerm.TypeArguments.Length
            || expectedTerm.ConstantArguments.IsDefault
            || providerTerm.ConstantArguments.IsDefault
            || expectedTerm.ConstantArguments.Length
                != providerTerm.ConstantArguments.Length)
        {
            return false;
        }

        if (expectedTerm.Kind == BoundGenericMethodTypeTermKind.Formal)
        {
            return expectedTerm.FormalOrdinal >= 0
                && (uint)expectedTerm.FormalOrdinal
                    < (uint)expected.Formals.Length
                && (uint)providerTerm.FormalOrdinal
                    < (uint)provider.Formals.Length;
        }
        if (expectedTerm.Kind == BoundGenericMethodTypeTermKind.Concrete)
        {
            return AreEquivalentGenericMethodImportTypes(
                expectedTerm.ConcreteType, providerTerm.ConcreteType,
                expected, provider, context, depth + 1,
                allowFormal: false);
        }
        if (expectedTerm.Kind is not (
                BoundGenericMethodTypeTermKind.CheckedReference
                or BoundGenericMethodTypeTermKind.Span
                or BoundGenericMethodTypeTermKind.ImportedGeneric)
            || ((expectedTerm.Kind is
                    BoundGenericMethodTypeTermKind.CheckedReference
                    or BoundGenericMethodTypeTermKind.Span)
                && expectedTerm.TypeArguments.Length != 1))
        {
            return false;
        }

        for (var index = 0; index < expectedTerm.TypeArguments.Length;
            index++)
        {
            if (!AreEquivalentGenericMethodImportTypeTerms(
                    expectedTerm.TypeArguments[index],
                    providerTerm.TypeArguments[index], expected, provider,
                    context, depth + 1))
            {
                return false;
            }
        }

        for (var index = 0; index < expectedTerm.ConstantArguments.Length;
            index++)
        {
            var expectedConstant = expectedTerm.ConstantArguments[index];
            var providerConstant = providerTerm.ConstantArguments[index];
            if (expectedConstant.Kind != providerConstant.Kind)
            {
                return false;
            }
            if (expectedConstant.Kind
                == BoundGenericMethodTypeConstantArgumentKind.Value)
            {
                if (expectedConstant.DomainType != providerConstant.DomainType
                    || expectedConstant.CarrierType
                        != providerConstant.CarrierType
                    || expectedConstant.Bits != providerConstant.Bits)
                {
                    return false;
                }
            }
            else if (expectedConstant.FormalOrdinal
                    != providerConstant.FormalOrdinal
                || expectedConstant.FormalDefinitionOrdinal
                    != providerConstant.FormalDefinitionOrdinal
                || expectedConstant.FormalOwnerKind
                    != providerConstant.FormalOwnerKind)
            {
                return false;
            }
        }

        return true;
    }
}
