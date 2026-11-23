// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Text;

namespace Terrum.CodeAnalysis.Modules;

public static partial class TrRmExecutableCompiler
{
    private readonly record struct GenericMethodEmission(
        BoundGenericMethodDefinition Definition,
        BoundFunction ProofFunction,
        int MethodRow);

    private readonly record struct GenericMethodForwardingCall(
        BoundCall Call, int ResultVariable);

    private readonly record struct GenericMethodForwardingTarget(
        BoundCall Call, BoundFunction Function,
        BoundFunction SignatureFunction,
        BoundGenericMethodDefinition Definition,
        ImmutableArray<PrimitiveType> TypeArguments, uint MethodToken,
        int ResultVariable);

    private readonly record struct GenericInterfaceEmission(
        BoundInterfaceDefinition Definition, TrRmSourceTypeExport Export);

    private readonly record struct GenericImportedInterfaceEmission(
        BoundInterfaceDefinition Definition, BoundModuleImport Import,
        int ExpectedTypeDefRow = -1);

    private readonly record struct GenericImportedMethodEmission(
        BoundGenericMethodImport Source,
        BoundGenericMethodImport Expected,
        BoundModuleImport Import);

    private readonly record struct GenericImportedInterfaceMethodKey(
        string ProviderIdentity, string TypeExportKey,
        string QualifiedName, int Arity, int MemberOrdinal,
        string MethodExportKey, uint TargetRequirement,
        uint RuntimePolicy);

    private readonly record struct GenericImportedInterfaceAssociatedTypeKey(
        string ProviderIdentity, string TypeExportKey,
        string QualifiedName, int Arity, int MemberOrdinal,
        string AssociatedTypeExportKey, uint TargetRequirement,
        uint RuntimePolicy);

    private static byte[] EmitGenericMethodCallsDraftSource(
        BoundProgram program, string moduleIdentity,
        uint moduleTargetRequirement,
        SourceGenericMethodExports? sourceMethodExports,
        IReadOnlyList<TrRmSourceTypeExport>? sourceTypeExports,
        TrRmArtifactSchema schema = TrRmArtifactSchema.GenericMethodCallsDraft,
        TrRmArtifactSchema baseSchema = TrRmArtifactSchema.Standard,
        bool standardInitializationReferenceBodies = false,
        IReadOnlyList<BoundGenericMethodDefinition>?
            retainedMethodDefinitions = null,
        byte[]? baseArtifact = null,
        TrRmFrozenScalarValueBuilder? frozenScalarValueBuilder = null,
        SourceClassExports? sourceClassExports = null,
        IReadOnlyList<C1M1SourceClassMethodGenericFormalPlan>?
            sourceClassMethodGenericFormalPlans = null,
        string? standardSchemaIdentity = null,
        ImmutableArray<int> foreignNativeInterfaceStructureIndexes =
            default,
        IReadOnlyDictionary<BoundInterfaceMemberIdentity, uint>?
            foreignInterfaceMethodMapTokenPlaceholders = null)
    {
        if (program.GenericMethodDefinitions.IsDefault)
        {
            throw new ArgumentException(
                "The checked program has no validated generic method definition table.",
                nameof(program));
        }

        if (sourceMethodExports is not null
            && !ReferenceEquals(sourceMethodExports.Program, program))
        {
            throw new ArgumentException(
                "Source generic method mappings belong to a different checked program.",
                nameof(sourceMethodExports));
        }
        if (sourceClassExports is not null
            && !ReferenceEquals(sourceClassExports.Program, program))
        {
            throw new ArgumentException(
                "Source generic class-method mappings belong to a different checked program.",
                nameof(sourceClassExports));
        }
        if (sourceClassMethodGenericFormalPlans is not null
            && sourceClassMethodGenericFormalPlans.Count != 0
            && (baseArtifact is null
                || !schema.UsesGenericMethodCalls()
                || sourceClassExports is null))
        {
            throw new ArgumentException(
                "Inherited class MethodDef generic constraints require the exact CM1 source-class composition.",
                nameof(sourceClassMethodGenericFormalPlans));
        }

        if (baseArtifact is not null
            && ((!schema.UsesClassParameterDefaults()
                    && !schema.UsesForeignNativeInterfaceParameterSurface()
                    && !schema.UsesForeignNativeInterface()
                    && (sourceClassMethodGenericFormalPlans is null
                        || sourceClassMethodGenericFormalPlans.Count == 0))
                || baseSchema != schema
                || ((schema.UsesClassParameterDefaults()
                        || schema.UsesForeignNativeInterfaceParameterSurface())
                    && frozenScalarValueBuilder is null)))
        {
            throw new ArgumentException(
                "A composed CM1 class artifact requires its shared frozen-value builder and exact schema.",
                nameof(baseArtifact));
        }

        var methodParameterSurfaceValues
            = schema.UsesClassParameterDefaults()
                || schema.UsesForeignNativeInterfaceParameterSurface()
                ? frozenScalarValueBuilder
                    ?? new TrRmFrozenScalarValueBuilder()
                : null;

        if (sourceTypeExports is { Count: > 0 }
            && !schema.UsesGeneralMethodParameterLabels()
            && !schema.UsesForeignNativeInterface()
            && sourceTypeExports.Any(static export =>
                !export.InterfaceMethodExports.IsDefaultOrEmpty))
        {
            throw Unsupported(
                "G1 InterfaceMethod exports require the exact G1L1 schema for parameter labels.");
        }
        if (sourceTypeExports is { Count: > 0 }
            && !schema.UsesAssociatedRelationConstraints()
            && sourceTypeExports.Any(static export =>
                !export.InterfaceAssociatedTypeExports.IsDefaultOrEmpty))
        {
            throw Unsupported(
                "AssociatedTypeDef exports require the exact CM1AR1 schema.");
        }
        if (!program.GenericMethodDefinitions.IsEmpty
            && sourceMethodExports is null)
        {
            throw new NotSupportedException(
                "The G1 source producer requires explicit source generic-method mappings for retained open definitions.");
        }

        var emissionRows = new List<GenericMethodEmission>();
        if (sourceMethodExports is not null)
        {
            foreach (var definition in program.GenericMethodDefinitions)
            {
                var proofFunction = GetSupportedOpenGenericProofFunction(
                    definition);
                emissionRows.Add(new GenericMethodEmission(definition,
                    proofFunction, 0));
            }
        }
        if (retainedMethodDefinitions is not null)
        {
            foreach (var definition in retainedMethodDefinitions)
            {
                var proofFunction = GetSupportedOpenGenericProofFunction(
                    definition);
                emissionRows.Add(new GenericMethodEmission(definition,
                    proofFunction, 0));
            }
        }
        if (schema.UsesAssociatedRelationConstraints()
            && emissionRows.Any(static emission =>
                emission.Definition.Owner is null
                && emission.Definition.Formals.Any(static formal =>
                    !formal.AssociatedRelations.IsDefaultOrEmpty)))
        {
            throw Unsupported(
                "CM1AR1 associated-relation constraints are selected only for class MethodDefs on nongeneric classes.");
        }
        if (emissionRows.Any(static emission =>
                emission.Definition.Owner is not null)
            && (!schema.UsesClassParameterDefaults()
                || sourceClassExports is null))
        {
            throw Unsupported(
                "Generic class MethodDefs require the exact CM1 source-class composition.");
        }

        var sourceProgram = program;
        var addedSeed = baseArtifact is null
            && program.Functions.IsEmpty
            && emissionRows.Count != 0;
        if (addedSeed)
        {
            if (!program.NativeFunctionPointerSignatures.IsEmpty
                || !program.NativePointerTypes.IsEmpty
                || !program.NativeFixedArrayTypes.IsEmpty
                || !program.EnumDefinitions.IsEmpty)
            {
                throw Unsupported(
                    "The G1 source writer cannot seed a program with other retained declaration anchors.");
            }

            sourceProgram = program with {
                Functions = [CreateGenericMethodEmitterSeed()],
            };
        }

        var artifact = baseArtifact
            ?? EmitDevelopment(sourceProgram, moduleIdentity,
                baseSchema, moduleTargetRequirement,
                standardInitializationReferenceBodies:
                    standardInitializationReferenceBodies,
                allowGenericMethodNominalTypeTables: true);
        if (addedSeed)
        {
            artifact = RemoveGenericMethodEmitterSeed(artifact, baseSchema);
        }
        var hasGenericCallTargets = ArtifactHasGenericCallTargets(artifact,
            schema, standardSchemaIdentity);
        var exportedInterfaceTypeSpecRows
            = new Dictionary<(string QualifiedName, int Arity), uint>();
        var exportedInterfaceMethodDefRows
            = new Dictionary<(
                string QualifiedName, int Arity, int MemberOrdinal,
                string DeclarationKey), uint>();
        var exportedInterfaceAssociatedTypeDefRows
            = new Dictionary<(
                string QualifiedName, int Arity, int MemberOrdinal,
                string DeclarationKey), uint>();
        var importedInterfaceAssociatedTypeImportRows
            = new Dictionary<GenericImportedInterfaceAssociatedTypeKey,
                uint>();
        var importedGenericMethodTargetTokens
            = new Dictionary<BoundGenericMethodImport, uint>();
        var importedNominalTypeReferences
            = new Dictionary<BoundGenericNominalTypeIdentity, uint>();
        var importedNominalTypeSnapshots
            = Array.Empty<BoundSourceTypeProviderSnapshot>();
        var sourceClassMethodGenericPlans
            = sourceClassMethodGenericFormalPlans is null
                ? ImmutableArray<C1M1SourceClassMethodGenericFormalPlan>.Empty
                : ImmutableArray.CreateRange(
                    sourceClassMethodGenericFormalPlans);
        var interfaceMetadataProgram = program;
        if (!sourceClassMethodGenericPlans.IsEmpty)
        {
            (interfaceMetadataProgram, sourceClassMethodGenericPlans)
                = PrepareC1M1SourceClassMethodGenericRelationProgram(
                    program, sourceClassMethodGenericPlans);
        }
        if (sourceTypeExports is { Count: > 0 })
        {
            if (sourceMethodExports is null)
            {
                throw new ArgumentException(
                    "G1 interface exports require an explicit source mapping set.",
                    nameof(sourceTypeExports));
            }
            artifact = AddGenericMethodInterfaceMetadata(artifact, program,
                sourceTypeExports, sourceMethodExports, schema,
                methodParameterSurfaceValues,
                out exportedInterfaceTypeSpecRows,
                out exportedInterfaceMethodDefRows,
                out exportedInterfaceAssociatedTypeDefRows,
                foreignNativeInterfaceStructureIndexes);
        }
        if (emissionRows.Count != 0
            || !sourceClassMethodGenericPlans.IsEmpty
            || schema.UsesForeignNativeInterface())
        {
            artifact = AddImportedGenericInterfaceMetadata(artifact,
                interfaceMetadataProgram, schema,
                methodParameterSurfaceValues,
                out importedInterfaceAssociatedTypeImportRows,
                standardSchemaIdentity,
                foreignNativeInterfaceStructureIndexes);
        }
        if (emissionRows.Count != 0)
        {
            artifact = AddImportedGenericMethodMetadata(artifact,
                program, emissionRows, exportedInterfaceTypeSpecRows,
                exportedInterfaceAssociatedTypeDefRows,
                importedInterfaceAssociatedTypeImportRows, schema,
                methodParameterSurfaceValues,
                out importedGenericMethodTargetTokens,
                out importedNominalTypeReferences,
                out importedNominalTypeSnapshots);
        }
        if (emissionRows.Count != 0
            || sourceTypeExports is { Count: > 0 })
        {
            if (sourceMethodExports is null)
            {
                throw new ArgumentException(
                    "G1 metadata emission requires an explicit source mapping set.",
                    nameof(sourceMethodExports));
            }
            var standardIndex = new TrRmArtifactIndex(artifact, schema,
                standardSchemaIdentity: standardSchemaIdentity);
            _ = standardIndex.TryGetSection(0x0016, out _,
                out var baseMethodCount);
            for (var emissionIndex = 0;
                emissionIndex < emissionRows.Count; emissionIndex++)
            {
                emissionRows[emissionIndex] = emissionRows[
                    emissionIndex] with {
                    MethodRow = checked(baseMethodCount + emissionIndex + 1),
                };
            }
            artifact = AddGenericMethodMetadata(artifact, program,
                sourceMethodExports, emissionRows,
                exportedInterfaceTypeSpecRows,
                exportedInterfaceMethodDefRows,
                exportedInterfaceAssociatedTypeDefRows,
                importedInterfaceAssociatedTypeImportRows,
                importedGenericMethodTargetTokens,
                importedNominalTypeReferences,
                importedNominalTypeSnapshots, schema,
                methodParameterSurfaceValues, sourceClassExports);
        }
        else if (hasGenericCallTargets
            || ArtifactHasGenericWriteConstraints(artifact, schema,
                standardSchemaIdentity))
        {
            artifact = AddGenericMethodFeatures(artifact, schema,
                hasGenericCallTargets, standardSchemaIdentity);
        }

        if (!sourceClassMethodGenericPlans.IsEmpty)
        {
            artifact = AddGenericMethodFeatures(artifact, schema,
                includeGenericMethodCalls: true,
                standardSchemaIdentity: standardSchemaIdentity);
            artifact = EmitC1M1SourceClassMethodGenericConstraints(
                artifact, interfaceMetadataProgram,
                sourceClassMethodGenericPlans, schema,
                standardSchemaIdentity,
                exportedInterfaceTypeSpecRows,
                exportedInterfaceAssociatedTypeDefRows,
                importedInterfaceAssociatedTypeImportRows,
                importedNominalTypeReferences,
                importedNominalTypeSnapshots);
        }

        if (methodParameterSurfaceValues is not null
            && (schema.UsesForeignNativeInterfaceParameterSurface()
                ? methodParameterSurfaceValues.ConstantSourceCount != 0
                : baseArtifact is not null
                    || methodParameterSurfaceValues.ConstantSourceCount != 0))
        {
            if (baseArtifact is null)
            {
                var existingIndex = new TrRmArtifactIndex(artifact, schema,
                    standardSchemaIdentity: standardSchemaIdentity);
                var hasExistingValues
                    = existingIndex.TryGetSection(0x0030, out _,
                        out var existingSources)
                        && existingSources != 0
                    || existingIndex.TryGetSection(0x003F, out _,
                        out var existingNodes)
                        && existingNodes != 0
                    || existingIndex.TryGetSection(0x0032, out _,
                        out var existingBlobs)
                        && existingBlobs != 0;
                if (hasExistingValues)
                {
                    throw Unsupported(
                        "CM1 parameter defaults cannot replace unrelated frozen-value rows.");
                }
            }

            artifact = ReplaceFrozenScalarValueSections(artifact, schema,
                methodParameterSurfaceValues, standardSchemaIdentity);
        }

        if (schema.UsesForeignNativeInterface()
            && foreignInterfaceMethodMapTokenPlaceholders is
                { Count: > 0 })
        {
            var importedInterfaceMethodImportRows
                = ReadImportedInterfaceMethodImportRows(artifact, schema,
                    standardSchemaIdentity,
                    allowDuplicateForeignNativeInterfaceViews:
                        schema.UsesForeignNativeInterface());
            artifact = RemapForeignNativeInterfaceCallMethodMapTokens(
                artifact, schema, exportedInterfaceMethodDefRows,
                importedInterfaceMethodImportRows,
                foreignInterfaceMethodMapTokenPlaceholders);
        }

        var index = new TrRmArtifactIndex(artifact, schema,
            standardSchemaIdentity: standardSchemaIdentity);
        var tables = schema.UsesForeignNativeInterface()
            ? TrRmStructuralMetadataValidator
                .ValidateAndGetExecutionTables(index)
            : TrRmStructuralMetadataValidator
                .ValidateGenericMethodCallsDraftAndGetExecutionTables(index);
        for (var methodRow = 1; methodRow <= tables.Methods.Count;
            methodRow++)
        {
            var bodyRow = tables.Methods.GetBodyRow(methodRow);
            if (bodyRow != 0 && tables.Bodies.GetRow(bodyRow)[0] == 0x01)
            {
                tables.ValidateRegisterBodyMapsForMethod(methodRow);
            }
        }
        return artifact;
    }

    private static byte[] EmitC1M1SourceClassMethodGenericConstraints(
        byte[] artifact, BoundProgram program,
        ImmutableArray<C1M1SourceClassMethodGenericFormalPlan> plans,
        TrRmArtifactSchema schema, string? standardSchemaIdentity,
        IReadOnlyDictionary<(string QualifiedName, int Arity), uint>
            exportedInterfaceTypeSpecRows,
        IReadOnlyDictionary<(
            string QualifiedName, int Arity, int MemberOrdinal,
            string DeclarationKey), uint>
            exportedInterfaceAssociatedTypeDefRows,
        IReadOnlyDictionary<GenericImportedInterfaceAssociatedTypeKey,
            uint> importedInterfaceAssociatedTypeImportRows,
        IReadOnlyDictionary<BoundGenericNominalTypeIdentity, uint>
            importedNominalTypeReferences,
        IReadOnlyList<BoundSourceTypeProviderSnapshot>
            importedNominalTypeSnapshots)
    {
        if (plans.IsDefaultOrEmpty
            || !schema.UsesGenericMethodCalls()
            || (!schema.UsesAssociatedRelationConstraints()
                && plans.Any(static plan =>
                    plan.SourceFormal.Constraints.Any(
                        static constraint => constraint.Kind == 3))))
        {
            throw new ArgumentException(
                "The source class generic constraint plan is empty or unsupported by the selected schema.",
                nameof(plans));
        }

        var index = new TrRmArtifactIndex(artifact, schema,
            standardSchemaIdentity: standardSchemaIdentity);
        var sections = new List<TrRmSection>(index.SectionCount);
        for (var sectionIndex = 0; sectionIndex < index.SectionCount;
            sectionIndex++)
        {
            index.GetSection(sectionIndex, out var kind, out var flags,
                out var bytes, out var count);
            sections.Add(new TrRmSection(kind, flags,
                checked((uint)count), bytes.ToArray()));
        }

        var stringSection = GetSection(sections, 0x0002);
        var strings = new TrRmStringTableReader(stringSection.Bytes.Span,
            checked((int)stringSection.RecordCount));
        var names = new List<(string Text, int Encoding)>(strings.Count + 8);
        for (var stringRow = 1; stringRow <= strings.Count; stringRow++)
        {
            names.Add((strings.DecodeNamespace(stringRow), 1));
        }

        var typeSpecRows = ReadVariableRows(sections, 0x0011, 2, 1);
        var genericParameterRows = ReadGenericParamRows(sections);
        var constraintRows = ReadConstraintRows(sections);
        var associatedRelationRows = ReadAssociatedRelationRows(sections);
        var outputTables = TrRmStructuralMetadataValidator
            .ValidateC1AndGetExecutionTables(
                new TrRmArtifactIndex(artifact, schema,
                    standardSchemaIdentity: standardSchemaIdentity));
        var importedInterfaceImportRows
            = ReadImportedInterfaceImportRows(sections, strings);
        var nominalTypes = new GenericMethodNominalTypeWriter(sections,
            names, typeSpecRows, genericParameterRows, constraintRows,
            program, [], schema);
        nominalTypes.ImportDefinitionReferences(
            importedNominalTypeReferences, importedNominalTypeSnapshots);
        var emittedConstraintsByFormal
            = new Dictionary<int, List<TrRmConstraintRow>>();
        var interfaceConstraintVisits
            = new Dictionary<int, int>();

        foreach (var methodPlans in plans.GroupBy(
                     static plan => plan.MethodRow))
        {
            var orderedPlans = methodPlans.OrderBy(
                static plan => plan.FormalOrdinal).ToArray();
            var roots = orderedPlans.SelectMany(static plan => plan.Roots)
                .Distinct().ToArray();
            if (roots.Length == 0
                || roots.Any(root =>
                    root.Origin.MethodDefId != roots[0].Origin.MethodDefId
                    || !StringComparer.Ordinal.Equals(
                        root.Origin.ProviderIdentity,
                        roots[0].Origin.ProviderIdentity)
                    || !root.Origin.ProviderArtifactDigest.AsSpan()
                        .SequenceEqual(
                            roots[0].Origin.ProviderArtifactDigest.AsSpan())
                    || root.Origin.ClassOwner
                        != roots[0].Origin.ClassOwner
                    || !AreC1M1SourceClassMethodRootsEquivalent(
                        roots[0], root)))
            {
                throw new InvalidDataException(
                    "A class MethodDef has ambiguous typed generic constraint roots.");
            }

            var root = roots[0];
            var provider = orderedPlans[0].Provider;
            if (orderedPlans.Any(plan =>
                    !SameA2SourceTypeProviderSnapshot(
                        plan.Provider, provider))
                || !StringComparer.Ordinal.Equals(
                    root.Origin.ProviderIdentity, provider.ModuleIdentity)
                || root.Origin.ProviderArtifactDigest.IsDefault)
            {
                throw new InvalidDataException(
                    "A class MethodDef generic constraint lost its exact provider artifact context.");
            }

            var providerArtifact = provider.ArtifactSnapshot.ToArray();
            var providerIndex = new TrRmArtifactIndex(providerArtifact,
                GetC1M1ClassMetadataSchema(provider.SchemaIdentity),
                standardSchemaIdentity: provider.SchemaIdentity);
            var sourceTables = TrRmStructuralMetadataValidator
                .ValidateC1AndGetExecutionTables(providerIndex);
            if (provider.ModuleRow <= 0
                || provider.ModuleRow > sourceTables.Modules.Count
                || !StringComparer.Ordinal.Equals(
                    sourceTables.Modules.GetIdentity(provider.ModuleRow),
                    provider.ModuleIdentity)
                || !root.Origin.ProviderArtifactDigest.AsSpan()
                    .SequenceEqual(SHA256.HashData(
                        provider.ArtifactSnapshot.AsSpan())))
            {
                throw new InvalidDataException(
                    "A class MethodDef generic constraint has an invalid source Module snapshot.");
            }

            var sourceMethodRow = checked((int)(
                root.Origin.MethodDefId & 0x00FF_FFFF));
            if (sourceTables.Methods.GetOwnerType(sourceMethodRow)
                    != checked((int)(root.Origin.ClassOwner!
                        .Identity.DefinitionId & 0x00FF_FFFF))
                || outputTables.Methods.GetBodyRow(methodPlans.Key) != 0)
            {
                throw new InvalidDataException(
                    "A class MethodDef generic constraint changed its exact provider owner or Body role.");
            }

            var sourceSignature = sourceTables.Methods
                .GetSignature(sourceMethodRow);
            var (sourceGenericFirst, sourceGenericCount)
                = sourceTables.Signatures.GetGenericRange(sourceSignature);
            var outputSignature = outputTables.Methods
                .GetSignature(methodPlans.Key);
            var (outputGenericFirst, outputGenericCount)
                = outputTables.Signatures.GetGenericRange(outputSignature);
            if (sourceGenericCount != root.Formals.Length
                || sourceGenericCount != outputGenericCount
                || sourceGenericCount != orderedPlans.Length
                || orderedPlans.Where((plan, ordinal) =>
                    plan.FormalOrdinal != ordinal).Any())
            {
                throw new InvalidDataException(
                    "A class MethodDef generic constraint has an incomplete formal mapping.");
            }

            var genericParamRowMap = new Dictionary<int, int>();
            for (var ordinal = 0; ordinal < sourceGenericCount; ordinal++)
            {
                var sourceGenericParamRow = sourceGenericFirst + ordinal;
                var outputGenericParamRow = outputGenericFirst + ordinal;
                if (sourceTables.GenericParams.GetOwnerDefId(
                        sourceGenericParamRow)
                        != root.Origin.MethodDefId
                    || sourceTables.GenericParams.GetOrdinal(
                        sourceGenericParamRow) != (uint)ordinal
                    || outputTables.GenericParams.GetOwnerDefId(
                        outputGenericParamRow)
                        != (0x1600_0000u
                            | checked((uint)methodPlans.Key))
                    || outputTables.GenericParams.GetOrdinal(
                        outputGenericParamRow) != (uint)ordinal
                    || !genericParamRowMap.TryAdd(sourceGenericParamRow,
                        outputGenericParamRow))
                {
                    throw new InvalidDataException(
                        "A class MethodDef generic constraint has an invalid provider or destination GenericParam owner.");
                }
            }

            var typeContext = CreateC1M1GenericMethodTypeEmissionContext(
                program, root, sourceMethodRow, sourceTables,
                genericParamRowMap, typeSpecRows);
            interfaceConstraintVisits.Add(methodPlans.Key,
                SemanticVerifier.MaximumAnalysisVisits);
            foreach (var plan in orderedPlans)
            {
                if ((uint)plan.FormalOrdinal >= (uint)root.Formals.Length
                    || plan.SourceFormal.Ordinal
                        != (uint)plan.FormalOrdinal
                    || !StringComparer.Ordinal.Equals(
                        plan.SourceFormal.Name,
                        root.Formals[plan.FormalOrdinal].Name)
                    || plan.SourceFormal.Constraints.IsDefault
                    || !emittedConstraintsByFormal.TryAdd(
                        plan.GenericParamRow, []))
                {
                    throw new InvalidDataException(
                        "A class MethodDef generic formal has no exact typed provider constraint view.");
                }

                var typedFormal = root.Formals[plan.FormalOrdinal];
                var interfaceConstraintOrdinal = 0;
                var associatedRelationOrdinal = 0;
                var remainingVisits
                    = interfaceConstraintVisits[methodPlans.Key];
                var generatedConstraints
                    = emittedConstraintsByFormal[plan.GenericParamRow];
                foreach (var sourceConstraint
                    in plan.SourceFormal.Constraints)
                {
                    switch (sourceConstraint.Kind)
                    {
                        case 1:
                        {
                            if ((uint)interfaceConstraintOrdinal
                                >= (uint)typedFormal.InterfaceConstraints
                                    .Length)
                            {
                                throw new InvalidDataException(
                                    "A class MethodDef lost its typed interface constraint.");
                            }

                            var interfaceConstraint
                                = typedFormal.InterfaceConstraints[
                                    interfaceConstraintOrdinal++];
                            var payload
                                = GenericMethodInterfaceConstraintTypeSpecRef(
                                    interfaceConstraint,
                                    typeContext.Definition,
                                    exportedInterfaceTypeSpecRows,
                                    importedInterfaceImportRows,
                                    typeContext.FormalTypeSpecRows,
                                    typeContext.GenericParamRows,
                                    typeContext.BuiltInTypeSpecRows,
                                    typeSpecRows, depth: 0,
                                    ref remainingVisits);
                            generatedConstraints.Add(
                                new TrRmConstraintRow(
                                    plan.GenericParamRow, 1, payload));
                            break;
                        }

                        case 2 when sourceConstraint.Payload is >= 1 and <= 3:
                            generatedConstraints.Add(
                                new TrRmConstraintRow(
                                    plan.GenericParamRow, 2,
                                    sourceConstraint.Payload));
                            break;

                        case 3 when schema
                            .UsesAssociatedRelationConstraints():
                        {
                            if ((uint)associatedRelationOrdinal
                                    >= (uint)typedFormal
                                        .AssociatedRelations.Length)
                            {
                                throw new InvalidDataException(
                                    "A class MethodDef lost its typed associated-relation constraint.");
                            }

                            var relation = typedFormal.AssociatedRelations[
                                associatedRelationOrdinal++];
                            if (relation.FormalOrdinal
                                    != plan.FormalOrdinal
                                || !typedFormal.InterfaceConstraints.Any(
                                    constraint =>
                                        BoundInterfaceContract
                                            .AreEquivalentProviderInterfaceViews(
                                                constraint,
                                                relation.AssociatedType
                                                    .DeclaringInterface)))
                            {
                                throw new InvalidDataException(
                                    "A class MethodDef associated relation has no matching interface constraint.");
                            }

                            var sourceRelationRow
                                = relation.AssociatedRelationRow;
                            if (sourceRelationRow <= 0
                                || relation.DeclaringInterfaceTypeSpecReference
                                    != sourceTables.AssociatedRelations
                                        .GetDeclaringInterfaceTypeSpecRef(
                                            sourceRelationRow)
                                || relation.AssociatedTypeDefReference
                                    != sourceTables.AssociatedRelations
                                        .GetAssociatedTypeDefReference(
                                            sourceRelationRow)
                                || relation.RequiredTypeSpecReference
                                    != sourceTables.AssociatedRelations
                                        .GetRequiredTypeSpecRef(
                                            sourceRelationRow))
                            {
                                throw new InvalidDataException(
                                    "A class MethodDef associated relation differs from its exact provider row.");
                            }

                            var declaringInterfaceTypeSpec
                                = GenericMethodInterfaceConstraintTypeSpecRef(
                                    relation.AssociatedType
                                        .DeclaringInterface,
                                    typeContext.Definition,
                                    exportedInterfaceTypeSpecRows,
                                    importedInterfaceImportRows,
                                    typeContext.FormalTypeSpecRows,
                                    typeContext.GenericParamRows,
                                    typeContext.BuiltInTypeSpecRows,
                                    typeSpecRows, depth: 0,
                                    ref remainingVisits);
                            var associatedTypeDef
                                = GenericMethodAssociatedTypeDefReference(
                                    relation,
                                    exportedInterfaceAssociatedTypeDefRows,
                                    importedInterfaceAssociatedTypeImportRows);
                            var requiredTypeSpec
                                = nominalTypes.TypeTermRef(
                                    relation.RequiredType,
                                    typeContext.Definition,
                                    typeContext.FormalTypeSpecRows,
                                    typeContext.BuiltInTypeSpecRows,
                                    depth: 0);
                            var relationRow
                                = checked((uint)associatedRelationRows.Count
                                    + 1);
                            associatedRelationRows.Add(
                                new TrRmAssociatedRelationRow(
                                    declaringInterfaceTypeSpec,
                                    associatedTypeDef,
                                    requiredTypeSpec));
                            generatedConstraints.Add(
                                new TrRmConstraintRow(
                                    plan.GenericParamRow, 3,
                                    relationRow));
                            break;
                        }

                        default:
                            throw new InvalidDataException(
                                "A class MethodDef has an unsupported generic constraint kind or payload.");
                    }
                }

                if (interfaceConstraintOrdinal
                        != typedFormal.InterfaceConstraints.Length
                    || associatedRelationOrdinal
                        != typedFormal.AssociatedRelations.Length)
                {
                    throw new InvalidDataException(
                        "A class MethodDef lost part of its typed generic constraint closure.");
                }

                interfaceConstraintVisits[methodPlans.Key]
                    = remainingVisits;
            }
        }

        var temporaryArtifact = CompleteC1M1NominalTypeEmission(sections,
            names, typeSpecRows, genericParameterRows, constraintRows,
            nominalTypes, schema,
            standardSchemaIdentity
                ?? throw new InvalidDataException(
                    "CM1AR1 class method relation emission requires its exact Standard schema identity."));
        var temporaryIndex = new TrRmArtifactIndex(temporaryArtifact,
            schema, standardSchemaIdentity: standardSchemaIdentity);
        var temporaryTables = TrRmStructuralMetadataValidator
            .ValidateC1AndGetExecutionTables(temporaryIndex);
        var temporarySections = new List<TrRmSection>(
            temporaryIndex.SectionCount);
        for (var sectionIndex = 0;
            sectionIndex < temporaryIndex.SectionCount; sectionIndex++)
        {
            temporaryIndex.GetSection(sectionIndex, out var kind,
                out var flags, out var bytes, out var count);
            temporarySections.Add(new TrRmSection(kind, flags,
                checked((uint)count), bytes.ToArray()));
        }
        var rebuiltGenericParameters
            = ReadGenericParamRows(temporarySections);
        var rebuiltConstraints
            = new List<TrRmConstraintRow>();
        for (var genericParamRow = 1;
            genericParamRow <= temporaryTables.GenericParams.Count;
            genericParamRow++)
        {
            if (!emittedConstraintsByFormal.TryGetValue(genericParamRow,
                    out var formalConstraints))
            {
                formalConstraints = [];
                var (constraintFirst, constraintCount)
                    = temporaryTables.GenericParams
                        .GetConstraintRange(genericParamRow);
                for (var offset = 0; offset < constraintCount; offset++)
                {
                    var constraintRow = constraintFirst + offset;
                    formalConstraints.Add(new TrRmConstraintRow(
                        genericParamRow,
                        temporaryTables.Constraints.GetKind(constraintRow),
                        temporaryTables.Constraints.GetPayload(
                            constraintRow)));
                }
            }

            var constraintFirst = formalConstraints.Count == 0 ? 0
                : rebuiltConstraints.Count + 1;
            foreach (var constraint in formalConstraints)
            {
                if (constraint.OwnerGenericParamRow != genericParamRow)
                {
                    throw new InvalidDataException(
                        "A class MethodDef Constraint has an invalid GenericParam owner.");
                }

                rebuiltConstraints.Add(constraint);
            }

            rebuiltGenericParameters[genericParamRow - 1]
                = rebuiltGenericParameters[genericParamRow - 1] with {
                    ConstraintFirst = constraintFirst,
                    ConstraintCount = formalConstraints.Count,
                };
        }

        SetSection(temporarySections, 0x0019,
            checked((uint)rebuiltGenericParameters.Count),
            EncodeC1M1GenericParamRows(rebuiltGenericParameters));
        SetSection(temporarySections, 0x001A,
            checked((uint)rebuiltConstraints.Count),
            EncodeC1M1ConstraintRows(rebuiltConstraints));
        var associatedRelationBytes
            = TrRmAssociatedRelationTableWriter.Encode(
                associatedRelationRows, temporaryTables.TypeSpecs,
                temporaryTables.AssociatedTypeDefs.Count,
                temporaryTables.Imports);
        SetSection(temporarySections, 0x0036,
            checked((uint)associatedRelationRows.Count),
            associatedRelationBytes);
        temporarySections.Sort(static (left, right) =>
            left.Kind.CompareTo(right.Kind));
        var result = TrRmArtifactWriter.Encode(temporarySections, schema,
            standardSchemaIdentity: standardSchemaIdentity);
        _ = TrRmStructuralMetadataValidator
            .ValidateC1AndGetExecutionTables(
                new TrRmArtifactIndex(result, schema,
                    standardSchemaIdentity: standardSchemaIdentity));
        return result;
    }

    private static bool AreC1M1SourceClassMethodRootsEquivalent(
        BoundGenericMethodImport left, BoundGenericMethodImport right)
    {
        if (left.Formals.Length != right.Formals.Length)
        {
            return false;
        }

        for (var ordinal = 0; ordinal < left.Formals.Length; ordinal++)
        {
            var leftFormal = left.Formals[ordinal];
            var rightFormal = right.Formals[ordinal];
            if (leftFormal.Ordinal != rightFormal.Ordinal
                || !StringComparer.Ordinal.Equals(leftFormal.Name,
                    rightFormal.Name)
                || !leftFormal.InterfaceConstraints.SequenceEqual(
                    rightFormal.InterfaceConstraints)
                || !leftFormal.AssociatedRelations.SequenceEqual(
                    rightFormal.AssociatedRelations))
            {
                return false;
            }
        }

        return true;
    }

    private static byte[] ReplaceFrozenScalarValueSections(byte[] artifact,
        TrRmArtifactSchema schema,
        TrRmFrozenScalarValueBuilder frozenScalarValueBuilder,
        string? standardSchemaIdentity = null)
    {
        var index = new TrRmArtifactIndex(artifact, schema,
            standardSchemaIdentity: standardSchemaIdentity);
        var values = frozenScalarValueBuilder.Encode(default);
        var sections = new List<TrRmSection>(index.SectionCount + 3);
        for (var sectionIndex = 0; sectionIndex < index.SectionCount;
            sectionIndex++)
        {
            index.GetSection(sectionIndex, out var kind, out var flags,
                out var bytes, out var count);
            if (kind is 0x0030 or 0x0032 or 0x003F)
            {
                continue;
            }

            sections.Add(new TrRmSection(kind, flags,
                checked((uint)count), bytes.ToArray()));
        }

        if (values.BlobCount != 0)
        {
            sections.Add(new TrRmSection(0x0032, 0,
                checked((uint)values.BlobCount), values.BlobSection));
        }
        if (values.ConstantSourceCount != 0)
        {
            sections.Add(new TrRmSection(0x0030, 0,
                checked((uint)values.ConstantSourceCount),
                values.ConstantSourceSection));
            sections.Add(new TrRmSection(0x003F, 0,
                checked((uint)values.FrozenValueNodeCount),
                values.FrozenValueNodeSection));
        }

        sections.Sort(static (left, right) =>
            left.Kind.CompareTo(right.Kind));
        return TrRmArtifactWriter.Encode(sections, schema,
            standardSchemaIdentity: standardSchemaIdentity);
    }

    private static BoundFunction CreateGenericMethodEmitterSeed()
        => new("<trrm-generic-method-seed>", PrimitiveType.Int32, [], 0,
            new BoundBlock([
                new BoundReturn(new BoundConstant(0, PrimitiveType.Int32,
                    default), default),
            ], default), default) {
            IsInternal = true,
        };

    private static byte[] RemoveGenericMethodEmitterSeed(byte[] artifact,
        TrRmArtifactSchema schema)
    {
        var artifactIndex = new TrRmArtifactIndex(artifact, schema);
        var sections = new List<TrRmSection>(artifactIndex.SectionCount);
        for (var sectionIndex = 0;
            sectionIndex < artifactIndex.SectionCount;
            sectionIndex++)
        {
            artifactIndex.GetSection(sectionIndex, out var kind,
                out var flags, out var bytes, out var count);
            if (kind is 0x0016 or 0x0017 or 0x0018 or 0x001D or 0x0020)
            {
                var expectedCount = kind is 0x0018 or 0x001D ? 0 : 1;
                if (count != expectedCount)
                {
                    throw Unsupported(
                        "The G1 source writer seed did not emit its expected single-method tables.");
                }
                continue;
            }

            sections.Add(new TrRmSection(kind, flags,
                checked((uint)count), bytes.ToArray()));
        }
        return TrRmArtifactWriter.Encode(sections, schema);
    }

    private static BoundFunction GetSupportedOpenGenericProofFunction(
        BoundGenericMethodDefinition definition)
    {
        if (!IsSupportedSourceGenericClassMethodOwner(definition)
            || (!definition.IsStatic
                && (definition.Owner is null || definition.IsMutable))
            || definition.ConstantFormals.IsDefault
            || !definition.ConstantFormals.IsEmpty
            || definition.FormalOrder.IsDefault
            || definition.Formals.IsDefault
            || definition.Formals.IsEmpty
            || definition.FormalOrder.Length != definition.Formals.Length
            || definition.FunctionIndex < 0
            || definition.FunctionIndex
                >= definition.ProofProgram.Functions.Length)
        {
            throw Unsupported(
                $"Generic MethodDef '{definition.Name}' has an owner or formal shape outside the selected source writer.");
        }

        for (var ordinal = 0; ordinal < definition.Formals.Length; ordinal++)
        {
            var formal = definition.Formals[ordinal];
            var slot = definition.FormalOrder[ordinal];
            if (formal.Ordinal != ordinal
                || slot.Ordinal != ordinal
                || slot.Kind != BoundGenericMethodFormalKind.Type
                || slot.TypeFormalOrdinal != ordinal
                || !formal.Requirements.IsEmpty
                || formal.InterfaceConstraints.IsDefault
                || formal.OwnershipDomain is < 1 or > 3
                || formal.NullabilityDomain is < 1 or > 2)
            {
                throw Unsupported(
                    $"Generic MethodDef '{definition.Name}' has an unsupported formal constraint or default.");
            }
        }

        var function = definition.ProofProgram.Functions[
            definition.FunctionIndex];
        if (function.IsExternal
            || function.IsStatic != definition.IsStatic
            || function.GenericMethodProofDefinitionOrdinal
                != definition.DefinitionOrdinal
            || function.RequiresUnsafeCaller
                != definition.RequiresUnsafeCaller
            || function.ParameterCount != definition.RuntimeParameterCount
            || function.GenericMethodProofTypeParameters.Length
                != definition.Formals.Length
            || function.Variables.Take(function.ParameterCount).Any(
                static parameter => parameter.IsMutable || parameter.IsOut
                    || parameter.IsInit || parameter.IsImplicitRef)
            || function.NoFailureProof
            || function.NativeOutputContracts.Length != 0
            || function.DirectNativeImport is not null
            || function.NativeVTableMethod is not null
            || function.DeclarationContractClauses is { IsDefault: false,
                IsEmpty: false }
            || !function.CheckedOutputStatePredicates.IsDefaultOrEmpty)
        {
            throw Unsupported(
                $"Generic MethodDef '{definition.Name}' has a body or contract outside the current G1 source writer.");
        }

        for (var ordinal = 0; ordinal < definition.Formals.Length; ordinal++)
        {
            if (function.GenericMethodProofTypeParameters[ordinal]
                != definition.Formals[ordinal].PlaceholderType)
            {
                throw Unsupported(
                    $"Generic MethodDef '{definition.Name}' has inconsistent checked formal identities.");
            }
        }

        return function;
    }

    private static bool IsSupportedSourceGenericClassMethodOwner(
        BoundGenericMethodDefinition definition)
    {
        if (definition.Owner is not BoundGenericMethodOwner owner)
        {
            return true;
        }

        if (!ManagedClassTypes.IsClass(owner.ClosedType)
            || owner.SourceGenericTypeDefinitionIndex != -1
            || !owner.TypeArguments.IsDefaultOrEmpty
            || !owner.ConstantArguments.IsDefaultOrEmpty)
        {
            return false;
        }

        var classIndex = ManagedClassTypes.ClassIndex(owner.ClosedType);
        return (uint)classIndex
            < (uint)definition.ProofProgram.Classes.Length;
    }

    private static bool ContainsGenericMethodCall(BoundFunction function)
    {
        var found = false;
        if (!BoundTreeTraversal.VisitExpressions(function.Body,
                expression => found |= expression is BoundCall {
                    GenericMethodCall: not null,
                }))
        {
            throw Unsupported(
                "The G1 source writer could not traverse a checked proof Body.");
        }

        return found;
    }

    private static bool ArtifactHasGenericCallTargets(byte[] artifact,
        TrRmArtifactSchema schema,
        string? standardSchemaIdentity = null)
    {
        var index = new TrRmArtifactIndex(artifact, schema,
            standardSchemaIdentity: standardSchemaIdentity);
        if (!index.TryGetSection(0x0020, out var bodyBytes,
                out var bodyCount)
            || bodyCount == 0)
        {
            return false;
        }

        var contractCount = index.TryGetSection(0x001B, out _, out var count)
            ? count : 0;
        var bodies = new TrRmBodyTableReader(bodyBytes, bodyCount, schema);
        for (var bodyRow = 1; bodyRow <= bodies.Count; bodyRow++)
        {
            var body = bodies.GetRow(bodyRow);
            if (!body.IsEmpty && body[0] == 0x01
                && bodies.GetRegisterBody(bodyRow, contractCount)
                    .HasGenericCallTargets)
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasGenericCallTargets(
        ReadOnlyMemory<byte> body, int contractCount,
        TrRmArtifactSchema schema)
    {
        var bytes = body.Span;
        return !bytes.IsEmpty && bytes[0] == 0x01
            && new TrRmRegisterBodyReader(bytes, contractCount, schema)
                .HasGenericCallTargets;
    }

    private static bool ArtifactHasGenericWriteConstraints(byte[] artifact,
        TrRmArtifactSchema schema, string? standardSchemaIdentity = null)
    {
        var index = new TrRmArtifactIndex(artifact, schema,
            standardSchemaIdentity: standardSchemaIdentity);
        if (!index.TryGetSection(0x001A, out var constraints,
                out var count))
        {
            return false;
        }
        if (constraints.Length != (long)count * 12)
        {
            throw new InvalidDataException(
                "The Constraint table has an invalid fixed-width extent.");
        }

        for (var row = 0; row < count; row++)
        {
            var data = constraints.Slice(row * 12, 12);
            if (BinaryPrimitives.ReadUInt16LittleEndian(data[4..]) == 2
                && BinaryPrimitives.ReadUInt32LittleEndian(data[8..]) == 2)
            {
                return true;
            }
        }

        return false;
    }

    private static byte[] AddGenericMethodFeatures(byte[] artifact,
        TrRmArtifactSchema schema, bool includeGenericMethodCalls,
        string? standardSchemaIdentity = null)
    {
        var index = new TrRmArtifactIndex(artifact, schema,
            standardSchemaIdentity: standardSchemaIdentity);
        var sections = new List<TrRmSection>(index.SectionCount);
        for (var sectionIndex = 0; sectionIndex < index.SectionCount;
            sectionIndex++)
        {
            index.GetSection(sectionIndex, out var kind, out var flags,
                out var bytes, out var count);
            sections.Add(new TrRmSection(kind, flags,
                checked((uint)count), bytes.ToArray()));
        }

        var featureRows = ReadFeatureRows(sections);
        var hasWriteConstraint = ReadConstraintRows(sections).Any(
            static constraint => constraint.Kind == 2
                && constraint.Payload == 2);
        if (hasWriteConstraint && !schema.UsesGenericWriteRequirements())
        {
            throw Unsupported(
                "The selected TRRM schema does not admit generic Write requirements.");
        }
        if (includeGenericMethodCalls
            && !featureRows.Any(static feature => feature.FeatureId
                == TrRmFeatureTableReader.GenericMethodCallsFeatureId))
        {
            featureRows.Add(TrRmFeatureRow.GenericMethodCallsCandidate);
        }
        if (hasWriteConstraint
            && !featureRows.Any(static feature => feature.FeatureId
                == TrRmFeatureTableReader.GenericWriteRequirementsFeatureId))
        {
            featureRows.Add(
                TrRmFeatureRow.GenericWriteRequirementsCandidate);
        }
        var featureBytes = TrRmFeatureTableWriter.Encode(featureRows, schema);
        var featureCount = featureBytes.Length / 8;

        var moduleSection = GetSection(sections, 0x0001);
        var moduleBytes = moduleSection.Bytes.ToArray();
        if (moduleSection.RecordCount != 1)
        {
            throw Unsupported(
                "G1 source feature emission requires one output Module.");
        }
        var oldFeatureCount = ReadInt(moduleBytes, 28);
        if (oldFeatureCount == 0)
        {
            WriteUInt32(moduleBytes, 24, 1);
        }
        WriteUInt32(moduleBytes, 28, checked((uint)featureCount));

        SetSection(sections, 0x0001, 1, moduleBytes);
        SetSection(sections, 0x0004, checked((uint)featureCount),
            featureBytes);
        sections.Sort(static (left, right) =>
            left.Kind.CompareTo(right.Kind));
        return TrRmArtifactWriter.Encode(sections, schema,
            standardSchemaIdentity: standardSchemaIdentity);
    }

    private static bool ContainsInterfaceCall(BoundFunction function)
    {
        var found = false;
        if (!BoundTreeTraversal.VisitExpressions(function.Body,
                expression => found |= expression is BoundCall {
                    InterfaceCallIdentity: not null,
                }))
        {
            throw Unsupported(
                "The G1 source writer could not traverse a checked proof Body.");
        }

        return found;
    }

    private static bool IsDirectGenericMethodMoveReturn(
        BoundFunction function)
        => TryGetDirectGenericMethodMoveReturn(function, out _);

    private static bool TryGetDirectGenericMethodMoveReturn(
        BoundFunction function, out BoundMove? move)
    {
        move = null;
        if (!function.Body.RetiredManagedLocals.IsEmpty)
        {
            return false;
        }

        var body = function.Body;
        var cleanupVariables = new List<int>();
        while (body.Statements.Length == 1
            && body.Statements[0] is BoundTryFinally tryFinally)
        {
            if (!tryFinally.Finally.RetiredManagedLocals.IsEmpty
                || tryFinally.Finally.Statements.Length != 1
                || tryFinally.Finally.Statements[0]
                    is not BoundDrop drop || drop.IsOutput)
            {
                return false;
            }

            cleanupVariables.Add(drop.Variable);
            body = tryFinally.Body;
        }

        if (body.RetiredManagedLocals.IsEmpty is false
            || body.Statements.Length != 1
            || body.Statements[0] is not BoundReturn {
                Value: BoundMove returnedMove,
            }
            || (cleanupVariables.Count != 0
                && (cleanupVariables.Count != 1
                    || cleanupVariables[0] != returnedMove.Variable)))
        {
            return false;
        }

        move = returnedMove;
        return true;
    }

    private static bool TryGetDirectGenericCallReturn(
        BoundFunction function, out BoundCall? call,
        out int resultVariable)
    {
        call = null;
        resultVariable = -1;
        if (!function.Body.RetiredManagedLocals.IsEmpty)
        {
            return false;
        }

        var body = function.Body;
        var cleanupVariables = new List<int>();
        while (body.Statements.Length == 1
            && body.Statements[0] is BoundTryFinally tryFinally)
        {
            if (!tryFinally.Finally.RetiredManagedLocals.IsEmpty
                || tryFinally.Finally.Statements.Length != 1
                || tryFinally.Finally.Statements[0]
                    is not BoundDrop drop || drop.IsOutput)
            {
                return false;
            }

            cleanupVariables.Add(drop.Variable);
            body = tryFinally.Body;
        }

        if (body.RetiredManagedLocals.IsEmpty is false)
        {
            return false;
        }

        BoundCall? returnedCall = null;
        if (body.Statements.Length == 1
            && body.Statements[0] is BoundReturn {
                Value: BoundCall directCall,
            })
        {
            returnedCall = directCall;
        }
        else if (body.Statements.Length >= 2
            && body.Statements[^1] is BoundReturn {
                Value: BoundRead returnedRead,
            })
        {
            // Same-typed Copy aliases require no distinct serialized Home.
            var localCount = body.Statements.Length - 1;
            if (localCount > SemanticVerifier.MaximumVariables
                || function.Variables.Length
                    != function.ParameterCount + localCount)
            {
                return false;
            }

            var previousAlias = -1;
            for (var ordinal = 0; ordinal < localCount; ordinal++)
            {
                if (body.Statements[ordinal] is not BoundLocal local
                    || local.Variable
                        != function.ParameterCount + ordinal
                    || (uint)local.Variable
                        >= (uint)function.Variables.Length)
                {
                    return false;
                }

                var variable = function.Variables[local.Variable];
                if (variable.Type != local.Initializer.Type
                    || variable.IsMutable || variable.IsOut
                    || variable.IsInit || variable.IsImplicitRef
                    || variable.IsImplicitReferenceTemporary
                    || variable.IsInitializationLocal)
                {
                    return false;
                }

                if (ordinal == 0)
                {
                    if (local.Initializer is not BoundCall localCall
                        || localCall.Type == PrimitiveType.Void
                        || variable.Type != function.ReturnType)
                    {
                        return false;
                    }

                    returnedCall = localCall;
                }
                else if (local.Initializer is BoundRead read
                    && read.Variable == previousAlias
                    && read.Type == variable.Type)
                {
                    if (returnedCall is null
                        || variable.Type != returnedCall.Type)
                    {
                        return false;
                    }
                }
                else
                {
                    return false;
                }

                previousAlias = local.Variable;
            }

            if (returnedCall is null
                || returnedRead.Variable != previousAlias
                || returnedRead.Type != function.ReturnType)
            {
                return false;
            }

            resultVariable = previousAlias;
        }

        if (returnedCall is null)
        {
            return false;
        }

        var movedVariables = returnedCall.Arguments
            .OfType<BoundMove>()
            .Select(static move => move.Variable)
            .ToArray();
        if (cleanupVariables.Count != movedVariables.Length
            || movedVariables.Distinct().Count() != movedVariables.Length
            || cleanupVariables.Except(movedVariables).Any()
            || movedVariables.Except(cleanupVariables).Any())
        {
            return false;
        }

        call = returnedCall;
        return true;
    }

    private static bool TryGetDirectGenericUnitCall(
        BoundFunction function, out BoundCall? call)
    {
        call = null;
        var body = function.Body;
        if (function.ReturnType != PrimitiveType.Void
            || function.ParameterCount > 8
            || function.Variables.Length != function.ParameterCount
            || !body.RetiredManagedLocals.IsEmpty
            || body.Statements.Length is < 1 or > 2
            || body.Statements[0] is not BoundExpressionStatement {
                Expression: BoundCall directCall,
            }
            || directCall.Type != PrimitiveType.Void
            || (body.Statements.Length == 2
                && body.Statements[1] is not BoundReturn {
                    Value: null,
                }))
        {
            return false;
        }

        call = directCall;
        return true;
    }

    private static bool TryGetGenericMethodConditionalUnitCall(
        BoundFunction function, out BoundIf? conditional,
        out BoundCall? call)
    {
        conditional = null;
        call = null;
        var body = function.Body;
        if (function.ReturnType != PrimitiveType.Void
            || function.ParameterCount > 8
            || function.Variables.Length != function.ParameterCount
            || !body.RetiredManagedLocals.IsEmpty
            || body.Statements.Length is < 1 or > 2
            || (body.Statements.Length == 2
                && body.Statements[1] is not BoundReturn {
                    Value: null,
                })
            || body.Statements[0] is not BoundIf candidate
            || candidate.Else is not null
            || candidate.Then is not BoundBlock thenBlock
            || thenBlock.Statements.Length != 1
            || !thenBlock.RetiredManagedLocals.IsEmpty
            || thenBlock.Statements[0] is not BoundExpressionStatement {
                Expression: BoundCall candidateCall,
            }
            || candidateCall.Type != PrimitiveType.Void
            || candidateCall.InterfaceCallIdentity is not null
            || (candidateCall.GenericMethodCall is null
                && candidateCall.ImportedGenericMethodCall is null)
            || candidate.Condition is not BoundRead condition
            || condition.Type != PrimitiveType.Bool
            || (uint)condition.Variable >= (uint)function.ParameterCount)
        {
            return false;
        }

        var conditionVariable = function.Variables[condition.Variable];
        if (conditionVariable.Type != PrimitiveType.Bool
            || conditionVariable.IsMutable || conditionVariable.IsOut
            || conditionVariable.IsInit || conditionVariable.IsImplicitRef
            || conditionVariable.IsImplicitReferenceTemporary
            || conditionVariable.IsInitializationLocal)
        {
            return false;
        }

        conditional = candidate;
        call = candidateCall;
        return true;
    }

    private static bool IsSupportedGenericUnitArgument(
        BoundExpression argument, BoundFunction function)
    {
        if (argument is BoundConstant {
                Type: PrimitiveType.Void,
            } constant)
        {
            return constant.TryGetLegacyValue(out var value)
                && value == 0;
        }

        if (argument is not BoundRead read
            || (uint)read.Variable >= (uint)function.ParameterCount)
        {
            return false;
        }

        var variable = function.Variables[read.Variable];
        return read.Type == PrimitiveType.Void
            && variable.Type == PrimitiveType.Void
            && !variable.IsMutable && !variable.IsOut && !variable.IsInit
            && !variable.IsImplicitRef;
    }

    private static bool TryGetGenericMethodForwardingChain(
        BoundFunction function,
        out ImmutableArray<GenericMethodForwardingCall> calls)
    {
        calls = [];
        var body = function.Body;
        if (!body.RetiredManagedLocals.IsEmpty)
        {
            return false;
        }

        var hasUnitReturn = body.Statements.Length != 0
            && body.Statements[^1] is BoundReturn { Value: null };
        var unitCallIndex = body.Statements.Length
            - (hasUnitReturn ? 2 : 1);
        if (function.ReturnType == PrimitiveType.Void
            && unitCallIndex >= 1
            && unitCallIndex <= SemanticVerifier.MaximumVariables
            && function.ParameterCount + unitCallIndex
                <= SemanticVerifier.MaximumVariables
            && function.Variables.Length
                == function.ParameterCount + unitCallIndex
            && body.Statements[unitCallIndex]
                is BoundExpressionStatement {
                    Expression: BoundCall unitCall,
                }
            && unitCall.Type == PrimitiveType.Void)
        {
            var unitChain
                = ImmutableArray.CreateBuilder<GenericMethodForwardingCall>(
                    unitCallIndex + 1);
            for (var ordinal = 0; ordinal < unitCallIndex; ordinal++)
            {
                var resultVariable = function.ParameterCount + ordinal;
                if (body.Statements[ordinal] is not BoundLocal local
                    || local.Variable != resultVariable
                    || local.Initializer is not BoundCall call
                    || call.Type == PrimitiveType.Void
                    || function.Variables[resultVariable].Type != call.Type)
                {
                    return false;
                }

                var variable = function.Variables[resultVariable];
                if (variable.IsMutable || variable.IsOut || variable.IsInit
                    || variable.IsImplicitRef
                    || variable.IsImplicitReferenceTemporary
                    || variable.IsInitializationLocal)
                {
                    return false;
                }

                if (ordinal != 0
                    && !call.Arguments.Any(argument =>
                        argument is BoundRead read
                        && read.Variable >= function.ParameterCount
                        && read.Variable < resultVariable))
                {
                    return false;
                }

                unitChain.Add(new GenericMethodForwardingCall(call,
                    resultVariable));
            }

            if (!unitCall.Arguments.Any(argument =>
                    argument is BoundRead read
                    && read.Variable >= function.ParameterCount
                    && read.Variable < function.Variables.Length))
            {
                return false;
            }

            unitChain.Add(new GenericMethodForwardingCall(unitCall, -1));
            calls = unitChain.MoveToImmutable();
            return true;
        }

        var localCount = body.Statements.Length - 1;
        if (localCount < 2
            || localCount > SemanticVerifier.MaximumVariables
            || function.Variables.Length
                != function.ParameterCount + localCount
            || body.Statements[^1] is not BoundReturn {
                Value: BoundRead returnedRead,
            }
            || returnedRead.Variable
                != function.ParameterCount + localCount - 1
            || returnedRead.Type != function.ReturnType
            || function.Variables[returnedRead.Variable].Type
                != function.ReturnType)
        {
            return false;
        }

        var builder
            = ImmutableArray.CreateBuilder<GenericMethodForwardingCall>(
                localCount);
        for (var ordinal = 0; ordinal < localCount; ordinal++)
        {
            var resultVariable = function.ParameterCount + ordinal;
            if (body.Statements[ordinal] is not BoundLocal local
                || local.Variable != resultVariable
                || local.Initializer is not BoundCall call
                || call.Type == PrimitiveType.Void
                || function.Variables[resultVariable].Type != call.Type)
            {
                return false;
            }

            var variable = function.Variables[resultVariable];
            if (variable.IsMutable || variable.IsOut || variable.IsInit
                || variable.IsImplicitRef
                || variable.IsImplicitReferenceTemporary
                || variable.IsInitializationLocal)
            {
                return false;
            }

            if (ordinal != 0)
            {
                var consumesEarlierCallResult = false;
                foreach (var argument in call.Arguments)
                {
                    if (argument is BoundRead read
                        && read.Variable >= function.ParameterCount
                        && read.Variable < resultVariable)
                    {
                        consumesEarlierCallResult = true;
                        break;
                    }
                }

                if (!consumesEarlierCallResult)
                {
                    return false;
                }
            }

            builder.Add(new GenericMethodForwardingCall(call,
                resultVariable));
        }

        calls = builder.MoveToImmutable();
        return true;
    }

    private static byte[] EncodeGenericMethodMoveReturnBody(
        BoundFunction function, BoundGenericMethodDefinition definition,
        Dictionary<PrimitiveType, uint> formalTypeSpecRows,
        Dictionary<PrimitiveType, uint> builtInTypeSpecRows,
        List<ReadOnlyMemory<byte>> typeSpecRows,
        ImmutableArray<BoundSpanTypeDefinition> spanTypes,
        TrRmArtifactSchema schema,
        GenericMethodNominalTypeWriter nominalTypes)
    {
        if (!TryGetDirectGenericMethodMoveReturn(function,
                out var move)
            || move is null
            || (uint)move.Variable >= (uint)function.ParameterCount
            || function.Variables.Length != function.ParameterCount
            || function.ParameterCount > 8
            || move.Type != function.ReturnType
            || function.Variables[move.Variable].Type != move.Type
            || function.Variables.Take(function.ParameterCount).Any(
                static parameter => parameter.IsMutable || parameter.IsOut
                    || parameter.IsInit || parameter.IsImplicitRef))
        {
            throw Unsupported(
                $"Generic MethodDef '{definition.Name}' has a move Body outside the direct-parameter return subset.");
        }

        var typeMap = new TrRmRegisterBodyTokenMapBuilder();
        uint BodyTypeToken(PrimitiveType type)
        {
            return CreateStandardBodyTypeMapToken(
                GenericMethodTypeSpecRef(type, formalTypeSpecRows,
                    builtInTypeSpecRows, typeSpecRows, spanTypes,
                    definition.ProofProgram.CheckedReferenceTypes,
                    definition.DefinitionOrdinal, definition,
                    allowSpanType: true, nominalTypes: nominalTypes));
        }

        var homeMap = new byte[checked(function.ParameterCount * 8)];
        for (var parameter = 0; parameter < function.ParameterCount;
            parameter++)
        {
            var offset = parameter * 8;
            homeMap[offset] = 2;
            BinaryPrimitives.WriteUInt16LittleEndian(
                homeMap.AsSpan(offset + 2),
                typeMap.GetIndex(BodyTypeToken(
                    function.Variables[parameter].Type)));
            BinaryPrimitives.WriteUInt16LittleEndian(
                homeMap.AsSpan(offset + 4), checked((ushort)parameter));
        }

        var entryState = new byte[8];
        entryState[0] = 1;
        var code = new byte[] {
            0xB8,
            checked((byte)move.Variable),
        };
        return TrRmRegisterBodyWriter.Encode(homeMap, typeMap.Encode(),
            default, default, default,
            [new TrRmRegisterBodyBlock(0, checked((uint)code.Length), 0)],
            [entryState], code, schema: schema);
    }

    private static byte[] EncodeImportedGenericMethodForwardingBody(
        BoundFunction function, BoundGenericMethodDefinition definition,
        BoundProgram proofProgram, BoundCall genericCall, int resultVariable,
        bool isUnitCall, BoundGenericMethodImport importedTarget,
        IReadOnlyDictionary<BoundGenericMethodImport, uint>
            importedGenericMethodTargetTokens,
        Dictionary<PrimitiveType, uint> formalTypeSpecRows,
        Dictionary<PrimitiveType, uint> builtInTypeSpecRows,
        List<ReadOnlyMemory<byte>> typeSpecRows,
        ImmutableArray<BoundSpanTypeDefinition> spanTypes,
        TrRmArtifactSchema schema,
        GenericMethodNominalTypeWriter nominalTypes)
    {
        var expectedTarget = importedTarget.Binding?.Expected
            ?? importedTarget;
        var hasClassOwner
            = importedTarget.Origin.ClassOwner is not null;
        var targetReceiverCount
            = expectedTarget.Signature.IsStatic ? 0 : 1;
        var targetMethodParameterCount
            = expectedTarget.ProofFunction.ParameterCount
                - targetReceiverCount;
        var typeArguments
            = genericCall.ImportedGenericMethodTypeArguments;
        if (genericCall.GenericMethodCall is not null
            || function.ParameterCount > 8
            || (resultVariable < 0
                ? function.Variables.Length != function.ParameterCount
                : resultVariable < function.ParameterCount
                    || resultVariable
                        != function.Variables.Length - 1)
            || function.Body.Statements.Any(
                static statement => statement is BoundTryFinally)
            || importedTarget.Signature.Owner is not null
            || (!importedTarget.Signature.IsStatic && !hasClassOwner)
            || (importedTarget.Signature.IsStatic
                && importedTarget.Signature.IsMutable)
            || !importedTarget.Signature.ConstantFormals.IsDefaultOrEmpty
            || targetMethodParameterCount is < 0 or > 8
            || expectedTarget.ProofFunction.Variables.Length
                < expectedTarget.ProofFunction.ParameterCount
            || expectedTarget.ProofFunction.DoesNotReturn
            || expectedTarget.ProofFunction.RequiresUnsafeCaller
                != expectedTarget.Signature.RequiresUnsafeCaller
            || expectedTarget.Formals.IsDefaultOrEmpty
            || expectedTarget.Formals.Length
                != expectedTarget.Signature.Formals.Length
            || expectedTarget.Formals.Length > 8
            || typeArguments.IsDefault
            || typeArguments.Length != expectedTarget.Formals.Length
            || (uint)genericCall.Function
                >= (uint)proofProgram.Functions.Length
            || !importedGenericMethodTargetTokens.TryGetValue(
                importedTarget, out var targetToken))
        {
            throw Unsupported(
                $"Generic MethodDef '{definition.Name}' has an imported GenericCall outside the direct forwarding subset.");
        }

        var target = proofProgram.Functions[genericCall.Function];
        if (!ReferenceEquals(target.ImportedGenericMethod, importedTarget)
            || target.ImportedGenericMethodTypeArguments.IsDefault
            || !target.ImportedGenericMethodTypeArguments
                .SequenceEqual(typeArguments)
            || target.IsStatic != importedTarget.Signature.IsStatic
            || target.IsMutable != importedTarget.Signature.IsMutable
            || (!hasClassOwner && target.DeclaringStruct is not null)
            || (hasClassOwner && target.DeclaringStruct is null)
            || target.ParameterCount != genericCall.Arguments.Length
            || target.ParameterCount
                != expectedTarget.ProofFunction.ParameterCount
            || target.Variables.Length < target.ParameterCount
            || target.DoesNotReturn
            || genericCall.Type != target.ReturnType
            || function.ReturnType != target.ReturnType
            || ((target.ReturnType == PrimitiveType.Void) != isUnitCall)
            || (resultVariable < 0
                ? function.Variables.Length != function.ParameterCount
                : resultVariable < function.ParameterCount
                    || resultVariable
                        != function.Variables.Length - 1))
        {
            throw Unsupported(
                $"Generic MethodDef '{definition.Name}' does not retain the imported target specialization.");
        }

        var targetDefinition = new BoundGenericMethodDefinition(
            -1, expectedTarget.Signature.Name,
            expectedTarget.Signature.IsPublic,
            expectedTarget.Signature.RequiresUnsafeCaller,
            genericCall.Function, -1, default, expectedTarget.Formals,
            proofProgram) {
            FormalTypeOwnerOrdinal
                = expectedTarget.FormalTypeOwnerOrdinal,
            IsStatic = expectedTarget.Signature.IsStatic,
            FormalOrder = expectedTarget.Signature.FormalOrder,
            RuntimeParameterCount
                = expectedTarget.ProofFunction.ParameterCount,
        };
        var callerFormalOrdinalsByTargetFormal
            = new Dictionary<int, int>();
        for (var targetOrdinal = 0;
            targetOrdinal < expectedTarget.Formals.Length; targetOrdinal++)
        {
            var actualType = typeArguments[targetOrdinal];
            for (var callerOrdinal = 0;
                callerOrdinal < definition.Formals.Length; callerOrdinal++)
            {
                if (definition.Formals[callerOrdinal].PlaceholderType
                    != actualType)
                {
                    continue;
                }

                callerFormalOrdinalsByTargetFormal.Add(targetOrdinal,
                    callerOrdinal);
                break;
            }
        }

        var typeArgumentsInBody
            = new TrRmRegisterBodyGenericCallArgument[typeArguments.Length];
        for (var ordinal = 0; ordinal < typeArguments.Length; ordinal++)
        {
            var actual = typeArguments[ordinal];
            var formal = expectedTarget.Formals[ordinal];
            var formalSignature
                = expectedTarget.Signature.Formals[ordinal];
            if (formal.Ordinal != ordinal
                || formalSignature.Ordinal != ordinal
                || !formal.Requirements.IsDefaultOrEmpty
                || !formalSignature.Requirements.IsDefaultOrEmpty
                || formal.InterfaceConstraints.IsDefault
                || formalSignature.InterfaceConstraints.IsDefault
                || formal.InterfaceConstraints.Length
                    != formalSignature.InterfaceConstraints.Length
                || !formal.InterfaceConstraints.SequenceEqual(
                    formalSignature.InterfaceConstraints)
                || formal.OwnershipDomain != 1
                || formal.NullabilityDomain is < 1 or > 2)
            {
                throw Unsupported(
                    $"Imported generic MethodDef '{expectedTarget.Signature.Name}' has an unsupported formal obligation.");
            }

            var callerFormalOrdinal = -1;
            for (var callerOrdinal = 0;
                callerOrdinal < definition.Formals.Length;
                callerOrdinal++)
            {
                if (definition.Formals[callerOrdinal].PlaceholderType
                    == actual)
                {
                    callerFormalOrdinal = callerOrdinal;
                    break;
                }
            }

            if (callerFormalOrdinal >= 0)
            {
                var callerFormal = definition.Formals[
                    callerFormalOrdinal];
                if (!SemanticVerifier
                        .IsGenericMethodFormalContractSatisfied(
                            formal.OwnershipDomain,
                            formal.NullabilityDomain,
                            formal.RequiresWritable,
                            formal.RequiresBlittable,
                            callerFormal.OwnershipDomain,
                            callerFormal.NullabilityDomain,
                            callerFormal.RequiresWritable,
                            callerFormal.RequiresBlittable))
                {
                    throw Unsupported(
                        $"Generic MethodDef '{definition.Name}' forwards a caller formal without satisfying the imported target domain.");
                }
                foreach (var targetConstraint
                    in formal.InterfaceConstraints)
                {
                    if (!TrySubstituteGenericMethodInterfaceConstraint(
                            targetConstraint,
                            callerFormalOrdinalsByTargetFormal,
                            out var substitutedConstraint)
                        || !callerFormal.InterfaceConstraints.Any(
                            callerConstraint =>
                                BoundInterfaceContract
                                    .IsInInheritanceClosure(
                                        callerConstraint,
                                        substitutedConstraint,
                                        definition.ProofProgram.Interfaces)))
                    {
                        throw Unsupported(
                            $"Generic MethodDef '{definition.Name}' forwards a caller formal without satisfying the imported interface constraint.");
                    }
                }
            }
            else if (formal.OwnershipDomain == 2
                || formal.RequiresWritable
                || !formal.InterfaceConstraints.IsEmpty)
            {
                throw Unsupported(
                    "An imported G1 GenericCall has a TypeActual outside its supported domain.");
            }

            typeArgumentsInBody[ordinal]
                = new TrRmRegisterBodyGenericCallArgument(1,
                    GenericMethodTypeSpecRef(actual, formalTypeSpecRows,
                        builtInTypeSpecRows, typeSpecRows, spanTypes,
                        proofProgram.CheckedReferenceTypes,
                        definition.DefinitionOrdinal, definition,
                        allowSpanType: true, nominalTypes: nominalTypes));
        }

        var typeMap = new TrRmRegisterBodyTokenMapBuilder();
        uint GetBodyTypeToken(PrimitiveType type)
        {
            if (type == PrimitiveType.Void)
            {
                throw Unsupported(
                    "G1 imported GenericCall forwarding has a unit value Home.");
            }

            return CreateStandardBodyTypeMapToken(
                GenericMethodTypeSpecRef(type, formalTypeSpecRows,
                    builtInTypeSpecRows, typeSpecRows, spanTypes,
                    proofProgram.CheckedReferenceTypes,
                    definition.DefinitionOrdinal, definition,
                    allowSpanType: true, nominalTypes: nominalTypes));
        }

        var parameterHomeIndexes = new int[function.Variables.Length];
        Array.Fill(parameterHomeIndexes, -1);
        var homeRows = new List<byte[]>(function.ParameterCount + 1);
        for (var parameter = 0; parameter < function.ParameterCount;
            parameter++)
        {
            var parameterType = function.Variables[parameter].Type;
            if (parameterType == PrimitiveType.Void)
            {
                continue;
            }

            var home = new byte[8];
            home[0] = 2;
            BinaryPrimitives.WriteUInt16LittleEndian(home.AsSpan(2),
                typeMap.GetIndex(GetBodyTypeToken(parameterType)));
            BinaryPrimitives.WriteUInt16LittleEndian(home.AsSpan(4),
                checked((ushort)parameter));
            parameterHomeIndexes[parameter] = homeRows.Count;
            homeRows.Add(home);
        }

        var inputs = new TrRmRegisterBodyCallInput[
            target.ParameterCount];
        var assignedParameters = new bool[inputs.Length];
        for (var argumentIndex = 0;
            argumentIndex < genericCall.Arguments.Length; argumentIndex++)
        {
            var argument = genericCall.Arguments[argumentIndex];
            var formalIndex
                = genericCall.ArgumentParameterIndices.IsDefault
                    ? argumentIndex
                    : genericCall.ArgumentParameterIndices[argumentIndex];
            if ((uint)formalIndex >= (uint)inputs.Length
                || assignedParameters[formalIndex])
            {
                throw Unsupported(
                    $"Generic MethodDef '{definition.Name}' has a duplicate or invalid imported call input.");
            }

            var targetParameterType
                = expectedTarget.ProofFunction.Variables[formalIndex].Type;
            if (argument.Type == PrimitiveType.Void)
            {
                if (!GenericMethodTypeMatchesAfterSubstitution(
                        targetParameterType, argument.Type,
                        targetDefinition, definition, typeArguments, 0)
                    || target.Variables[formalIndex].Type
                        != PrimitiveType.Void
                    || !IsSupportedGenericUnitArgument(argument, function)
                    || target.Variables[formalIndex].IsMutable
                    || target.Variables[formalIndex].IsOut
                    || target.Variables[formalIndex].IsInit
                    || target.Variables[formalIndex].IsImplicitRef)
                {
                    throw Unsupported(
                        $"Generic MethodDef '{definition.Name}' has an imported unit input outside the exact None-selector subset.");
                }

                assignedParameters[formalIndex] = true;
                inputs[formalIndex] = new TrRmRegisterBodyCallInput(
                    0, 0, 0);
                continue;
            }

            if (argument is not BoundRead read
                || (uint)read.Variable >= (uint)function.ParameterCount
                || function.Variables[read.Variable].Type != argument.Type
                || !GenericMethodTypeMatchesAfterSubstitution(
                    targetParameterType, argument.Type,
                    targetDefinition, definition, typeArguments, 0)
                || target.Variables[formalIndex].Type != argument.Type
                || target.Variables[formalIndex].IsMutable
                || target.Variables[formalIndex].IsOut
                || target.Variables[formalIndex].IsInit
                || target.Variables[formalIndex].IsImplicitRef
                || function.Variables[read.Variable].IsMutable
                || function.Variables[read.Variable].IsOut
                || function.Variables[read.Variable].IsInit
                || function.Variables[read.Variable].IsImplicitRef
                || parameterHomeIndexes[read.Variable] < 0)
            {
                throw Unsupported(
                    $"Generic MethodDef '{definition.Name}' has an imported call input outside the Copy parameter-Home subset.");
            }

            var callerFormalOrdinal = -1;
            for (var callerOrdinal = 0;
                callerOrdinal < definition.Formals.Length;
                callerOrdinal++)
            {
                if (definition.Formals[callerOrdinal].PlaceholderType
                    == argument.Type)
                {
                    callerFormalOrdinal = callerOrdinal;
                    break;
                }
            }
            if (callerFormalOrdinal >= 0
                && definition.Formals[callerFormalOrdinal]
                    .OwnershipDomain != 1)
            {
                throw Unsupported(
                    $"Generic MethodDef '{definition.Name}' reads a non-Copy caller formal in an imported call.");
            }

            assignedParameters[formalIndex] = true;
            inputs[formalIndex] = new TrRmRegisterBodyCallInput(
                2, 1, checked((ushort)parameterHomeIndexes[read.Variable]));
        }
        if (assignedParameters.Any(static assigned => !assigned))
        {
            throw Unsupported(
                $"Generic MethodDef '{definition.Name}' has an incomplete imported call input list.");
        }
        if (!GenericMethodTypeMatchesAfterSubstitution(
                expectedTarget.ProofFunction.ReturnType,
                function.ReturnType, targetDefinition, definition,
                typeArguments, 0))
        {
            throw Unsupported(
                $"Generic MethodDef '{definition.Name}' has an imported result outside the checked direct-forwarding subset.");
        }

        ushort resultHome = 0;
        if (!isUnitCall)
        {
            if (homeRows.Count >= ushort.MaxValue)
            {
                throw Unsupported(
                    "A G1 imported GenericCall result exceeds its bounded Home map.");
            }

            resultHome = checked((ushort)homeRows.Count);
            var result = new byte[8];
            result[0] = 3;
            result[1] = 1;
            BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(2),
                typeMap.GetIndex(GetBodyTypeToken(function.ReturnType)));
            homeRows.Add(result);
        }

        var homeMap = new byte[checked(homeRows.Count * 8)];
        for (var homeIndex = 0; homeIndex < homeRows.Count; homeIndex++)
        {
            homeRows[homeIndex].CopyTo(homeMap, homeIndex * 8);
        }
        var methodMap = new TrRmRegisterBodyTokenMapBuilder();
        var methodMapIndex = methodMap.GetIndex(targetToken);
        var records = new List<ReadOnlyMemory<byte>>(3);
        var entryState = new byte[8];
        entryState[0] = 1;
        records.Add(entryState);
        records.Add(TrRmRegisterBodyRecordWriter.EncodeGenericCallTarget(
            1, 0, methodMapIndex, 0, 0, typeArgumentsInBody, schema));
        var callSiteIndex = checked((ushort)records.Count);
        records.Add(TrRmRegisterBodyRecordWriter.EncodeCallSite(
            4, isUnitCall ? (byte)0 : (byte)2, 1, resultHome,
            inputs, [], schema: schema));
        var code = isUnitCall
            ? new byte[] { 0x90, checked((byte)callSiteIndex) }
            : [
                0x90,
                checked((byte)callSiteIndex),
                0xB8,
                checked((byte)resultHome),
            ];
        return TrRmRegisterBodyWriter.Encode(homeMap, typeMap.Encode(),
            methodMap.Encode(), default, default,
            [new TrRmRegisterBodyBlock(0, checked((uint)code.Length), 0)],
            records, code, schema: schema);
    }

    private static byte[] EncodeGenericMethodConditionalUnitCallBody(
        BoundFunction function, BoundGenericMethodDefinition definition,
        IReadOnlyList<GenericMethodEmission> emissions, BoundIf conditional,
        BoundCall call,
        Dictionary<PrimitiveType, uint> formalTypeSpecRows,
        Dictionary<PrimitiveType, uint> builtInTypeSpecRows,
        List<ReadOnlyMemory<byte>> typeSpecRows,
        ImmutableArray<BoundSpanTypeDefinition> spanTypes,
        IReadOnlyDictionary<BoundGenericMethodImport, uint>
            importedGenericMethodTargetTokens,
        TrRmArtifactSchema schema,
        GenericMethodNominalTypeWriter nominalTypes)
    {
        var proofProgram = definition.ProofProgram;
        if ((uint)call.Function >= (uint)proofProgram.Functions.Length
            || proofProgram.Functions[call.Function].DoesNotReturn)
        {
            throw Unsupported(
                $"Generic MethodDef '{definition.Name}' has a conditional unit call to an unsupported or no-return target.");
        }

        var callBodyFunction = function with {
            Body = new BoundBlock([
                new BoundExpressionStatement(call, call.Span),
            ], call.Span),
        };
        var callBodyBytes = EncodeGenericMethodForwardingBody(
            callBodyFunction, definition, emissions, formalTypeSpecRows,
            builtInTypeSpecRows, typeSpecRows, spanTypes,
            importedGenericMethodTargetTokens, schema, nominalTypes);
        var callBody = new TrRmRegisterBodyReader(callBodyBytes,
            schema: schema);
        var callCode = callBody.GetBlockCode(0);
        if (callBody.BlockCount != 1 || callBody.RecordCount != 3
            || callCode.Length != 2 || callCode[0] != 0x90
            || callCode[1] != 2 || callBody.GetRecord(0)[0] != 0x01
            || callBody.GetRecord(1)[0] != 0x10
            || callBody.GetRecord(2)[0] != 0x02)
        {
            throw Unsupported(
                $"Generic MethodDef '{definition.Name}' has a conditional unit call outside the direct GenericCall subset.");
        }

        var condition = (BoundRead)conditional.Condition;
        var conditionHome = 0;
        for (var parameter = 0; parameter < condition.Variable;
            parameter++)
        {
            if (function.Variables[parameter].Type != PrimitiveType.Void)
            {
                conditionHome++;
            }
        }

        if ((uint)conditionHome >= (uint)callBody.HomeCount)
        {
            throw Unsupported(
                $"Generic MethodDef '{definition.Name}' has no Home for its conditional unit-call predicate.");
        }

        var conditionHomeEntry = callBody.HomeMap.Slice(
            conditionHome * 8, 8);
        if (conditionHomeEntry[0] != 2
            || BinaryPrimitives.ReadUInt16LittleEndian(
                conditionHomeEntry.Slice(4, 2)) != condition.Variable)
        {
            throw Unsupported(
                $"Generic MethodDef '{definition.Name}' has an invalid conditional unit-call Home.");
        }

        var records = new List<ReadOnlyMemory<byte>>(4);
        for (var recordIndex = 0; recordIndex < callBody.RecordCount;
            recordIndex++)
        {
            records.Add(callBody.GetRecord(recordIndex).ToArray());
        }

        var edgeRecordIndex = checked((ushort)records.Count);
        records.Add(TrRmRegisterBodyRecordWriter.EncodeConditionalEdge(
            1, 2));
        var conditionCode = new List<byte>(8) {
            0xD5,
            0,
            (byte)conditionHome,
            (byte)(conditionHome >> 8),
            0xE4,
            0,
            (byte)edgeRecordIndex,
            (byte)(edgeRecordIndex >> 8),
        };
        var thenCode = new List<byte>(callCode.ToArray());
        AppendBranch(thenCode, 3);
        var codeBlocks = new List<List<byte>>(4) {
            conditionCode,
            thenCode,
            new List<byte> { 0xE3, 0, 3, 0 },
            new List<byte> { 0x18, 0 },
        };
        var blocks = new List<TrRmRegisterBodyBlock>(codeBlocks.Count);
        var code = new List<byte>();
        foreach (var blockCode in codeBlocks)
        {
            blocks.Add(new TrRmRegisterBodyBlock(
                checked((uint)code.Count),
                checked((uint)blockCode.Count), 0));
            code.AddRange(blockCode);
        }

        return TrRmRegisterBodyWriter.Encode(callBody.HomeMap.ToArray(),
            callBody.TypeMap.ToArray(), callBody.MethodMap.ToArray(),
            callBody.FieldMap.ToArray(), callBody.ConstantMap.ToArray(),
            blocks, records, code.ToArray(), schema: schema);

        static void AppendBranch(List<byte> blockCode, int targetBlock)
        {
            blockCode.Add(0xE3);
            blockCode.Add(0);
            blockCode.Add(checked((byte)targetBlock));
            blockCode.Add(checked((byte)(targetBlock >> 8)));
        }
    }

    private static byte[] EncodeGenericMethodForwardingBody(
        BoundFunction function, BoundGenericMethodDefinition definition,
        IReadOnlyList<GenericMethodEmission> emissions,
        Dictionary<PrimitiveType, uint> formalTypeSpecRows,
        Dictionary<PrimitiveType, uint> builtInTypeSpecRows,
        List<ReadOnlyMemory<byte>> typeSpecRows,
        ImmutableArray<BoundSpanTypeDefinition> spanTypes,
        IReadOnlyDictionary<BoundGenericMethodImport, uint>
            importedGenericMethodTargetTokens,
        TrRmArtifactSchema schema,
        GenericMethodNominalTypeWriter nominalTypes)
    {
        if (TryGetGenericMethodConditionalUnitCall(function,
                out var conditional, out var conditionalCall))
        {
            return EncodeGenericMethodConditionalUnitCallBody(function,
                definition, emissions, conditional!, conditionalCall!,
                formalTypeSpecRows, builtInTypeSpecRows, typeSpecRows,
                spanTypes, importedGenericMethodTargetTokens, schema,
                nominalTypes);
        }

        if (TryGetGenericMethodForwardingChain(function, out var callChain))
        {
            return EncodeGenericMethodForwardingChainBody(function,
                definition, emissions, callChain,
                importedGenericMethodTargetTokens,
                formalTypeSpecRows, builtInTypeSpecRows, typeSpecRows,
                spanTypes, schema, nominalTypes);
        }

        var isUnitCall = TryGetDirectGenericUnitCall(function,
            out var genericCall);
        var resultVariable = -1;
        if (!isUnitCall && !TryGetDirectGenericCallReturn(function,
                out genericCall, out resultVariable))
        {
            throw Unsupported(
                $"Generic MethodDef '{definition.Name}' has a GenericCall Body outside the direct-return or unit-forwarding subset.");
        }

        if (genericCall is null)
        {
            throw Unsupported(
                $"Generic MethodDef '{definition.Name}' has a missing GenericCall Body target.");
        }

        if (genericCall.ImportedGenericMethodCall
            is { } importedGenericMethod)
        {
            return EncodeImportedGenericMethodForwardingBody(function,
                definition, definition.ProofProgram, genericCall,
                resultVariable, isUnitCall, importedGenericMethod,
                importedGenericMethodTargetTokens, formalTypeSpecRows,
                builtInTypeSpecRows, typeSpecRows, spanTypes, schema,
                nominalTypes);
        }

        if (genericCall.GenericMethodCall
                is not BoundGenericMethodInstantiation call
            || (isUnitCall
                ? genericCall.Type != PrimitiveType.Void
                    || function.ReturnType != PrimitiveType.Void
                : genericCall.Type == PrimitiveType.Void
                    || genericCall.Type != function.ReturnType)
            || (resultVariable < 0
                ? function.Variables.Length != function.ParameterCount
                : resultVariable < function.ParameterCount
                    || resultVariable
                        != function.Variables.Length - 1)
            || function.ParameterCount > 8)
        {
            throw Unsupported(
                $"Generic MethodDef '{definition.Name}' has a GenericCall Body outside the direct-return or unit-forwarding subset.");
        }

        var proofProgram = definition.ProofProgram;
        if ((uint)genericCall.Function >= (uint)proofProgram.Functions.Length)
        {
            throw Unsupported(
                $"Generic MethodDef '{definition.Name}' calls an unknown proof target.");
        }

        var target = proofProgram.Functions[genericCall.Function];
        var targetCall = target.GenericMethodProofCall;
        var targetEmissions = emissions.Where(candidate =>
            candidate.Definition.DefinitionOrdinal == call.DefinitionOrdinal)
            .ToArray();
        if (targetCall is null
            || targetEmissions.Length != 1
            || targetCall.DefinitionOrdinal != call.DefinitionOrdinal
            || !targetCall.TypeArguments.SequenceEqual(call.TypeArguments)
            || !targetCall.ConstantArguments.IsEmpty
            || !call.ConstantArguments.IsEmpty
            || target.ParameterCount != genericCall.Arguments.Length
            || target.ParameterCount != target.Variables.Length
            || target.ParameterCount > 8
            || (target.ReturnType == PrimitiveType.Void) != isUnitCall
            || call.TypeArguments.Length
                != targetEmissions[0].Definition.Formals.Length
            || (!genericCall.ArgumentParameterIndices.IsDefault
                && genericCall.ArgumentParameterIndices.Length
                    != genericCall.Arguments.Length))
        {
            throw Unsupported(
                $"Generic MethodDef '{definition.Name}' has a GenericCall target outside the local type-only forwarding subset.");
        }

        var targetDefinition = targetEmissions[0].Definition;
        var targetDoesNotReturn = target.DoesNotReturn;
        for (var ordinal = 0; ordinal < targetDefinition.Formals.Length;
            ordinal++)
        {
            var formal = targetDefinition.Formals[ordinal];
            if (!formal.Requirements.IsEmpty
                || formal.InterfaceConstraints.IsDefault
                || formal.OwnershipDomain is < 1 or > 3
                || formal.NullabilityDomain is < 1 or > 2)
            {
                throw Unsupported(
                    $"Generic MethodDef '{definition.Name}' forwards to a target with unsupported generic formal obligations.");
            }
        }

        var callerFormalOrdinalsByTargetFormal = new Dictionary<int, int>();
        for (var targetOrdinal = 0;
            targetOrdinal < targetDefinition.Formals.Length;
            targetOrdinal++)
        {
            var actualType = call.TypeArguments[targetOrdinal];
            for (var callerOrdinal = 0;
                callerOrdinal < definition.Formals.Length;
                callerOrdinal++)
            {
                if (definition.Formals[callerOrdinal].PlaceholderType
                    != actualType)
                {
                    continue;
                }

                callerFormalOrdinalsByTargetFormal.Add(
                    targetDefinition.Formals[targetOrdinal].Ordinal,
                    definition.Formals[callerOrdinal].Ordinal);
                break;
            }
        }

        bool SatisfiesInterfaceConstraints(int targetFormalOrdinal,
            BoundGenericMethodFormal callerFormal)
        {
            var targetFormal = targetDefinition.Formals[
                targetFormalOrdinal];
            foreach (var targetConstraint in targetFormal.InterfaceConstraints)
            {
                if (!TrySubstituteGenericMethodInterfaceConstraint(
                        targetConstraint,
                        callerFormalOrdinalsByTargetFormal,
                        out var substitutedConstraint)
                    || !callerFormal.InterfaceConstraints.Any(
                        callerConstraint =>
                            BoundInterfaceContract.IsInInheritanceClosure(
                                callerConstraint, substitutedConstraint,
                                definition.ProofProgram.Interfaces)))
                {
                    return false;
                }
            }

            return true;
        }

        var formalParameters = new bool[target.ParameterCount];
        var inputs = new TrRmRegisterBodyCallInput[target.ParameterCount];
        for (var argumentIndex = 0;
            argumentIndex < genericCall.Arguments.Length; argumentIndex++)
        {
            var argument = genericCall.Arguments[argumentIndex];
            var formalIndex = genericCall.ArgumentParameterIndices.IsDefault
                ? argumentIndex
                : genericCall.ArgumentParameterIndices[argumentIndex];
            if ((uint)formalIndex >= (uint)inputs.Length
                || formalParameters[formalIndex])
            {
                throw Unsupported(
                    $"Generic MethodDef '{definition.Name}' has a duplicate or invalid call input.");
            }

            if (argument.Type == PrimitiveType.Void)
            {
                if (!IsSupportedGenericUnitArgument(argument, function)
                    || !GenericMethodTypeMatchesAfterSubstitution(
                        target.Variables[formalIndex].Type, argument.Type,
                        targetDefinition, definition, call.TypeArguments, 0)
                    || target.Variables[formalIndex].IsMutable
                    || target.Variables[formalIndex].IsOut
                    || target.Variables[formalIndex].IsInit
                    || target.Variables[formalIndex].IsImplicitRef)
                {
                    throw Unsupported(
                        $"Generic MethodDef '{definition.Name}' has a unit call input outside the exact None-selector subset.");
                }

                formalParameters[formalIndex] = true;
                inputs[formalIndex] = new TrRmRegisterBodyCallInput(
                    0, 0, 0);
                continue;
            }

            var (argumentVariable, sourceUse) = argument switch
            {
                BoundRead read => (read.Variable, (byte)1),
                BoundMove move => (move.Variable, (byte)2),
                BoundReferenceReborrow { Descriptor: BoundRead read }
                    => (read.Variable, (byte)5),
                _ => (-1, (byte)0),
            };
            if (sourceUse == 0
                || (uint)argumentVariable >= (uint)function.ParameterCount
                || function.Variables[argumentVariable].Type != argument.Type
                || !GenericMethodTypeMatchesAfterSubstitution(
                    target.Variables[formalIndex].Type, argument.Type,
                    targetDefinition, definition, call.TypeArguments, 0)
                || target.Variables[formalIndex].IsMutable
                || target.Variables[formalIndex].IsOut
                || target.Variables[formalIndex].IsInit
                || target.Variables[formalIndex].IsImplicitRef
                || function.Variables[argumentVariable].IsMutable
                || function.Variables[argumentVariable].IsOut
                || function.Variables[argumentVariable].IsInit
                || function.Variables[argumentVariable].IsImplicitRef)
            {
                throw Unsupported(
                    $"Generic MethodDef '{definition.Name}' has an argument outside the parameter-home forwarding subset.");
            }

            // Transfers require a target with no failing operation before it returns the moved formal.
            if (sourceUse == 2
                && (!TryGetDirectGenericMethodMoveReturn(
                        targetEmissions[0].ProofFunction,
                        out var targetMove)
                    || targetMove is null
                    || targetMove.Variable != formalIndex))
            {
                throw Unsupported(
                    $"Generic MethodDef '{definition.Name}' transfers a value to a target without a matching direct move-return Body.");
            }

            var argumentFormalOrdinal = -1;
            for (var candidate = 0;
                candidate < definition.Formals.Length; candidate++)
            {
                if (definition.Formals[candidate].PlaceholderType
                    == argument.Type)
                {
                    argumentFormalOrdinal = candidate;
                    break;
                }
            }
            if (sourceUse == 5
                ? !IsGenericMethodInitializationReferenceType(
                    target.Variables[formalIndex].Type,
                    targetDefinition.ProofProgram.CheckedReferenceTypes)
                : argumentFormalOrdinal < 0
                    ? sourceUse != 1
                    : definition.Formals[argumentFormalOrdinal]
                            .OwnershipDomain switch {
                        1 => sourceUse != 1,
                        2 => sourceUse != 2,
                        _ => true,
                    })
            {
                throw Unsupported(
                    $"Generic MethodDef '{definition.Name}' uses a parameter Home without the required Copy or unique transfer mode.");
            }

            formalParameters[formalIndex] = true;
            var homeIndex = 0;
            for (var parameter = 0; parameter < argumentVariable;
                parameter++)
            {
                if (function.Variables[parameter].Type
                    != PrimitiveType.Void)
                {
                    homeIndex++;
                }
            }
            inputs[formalIndex] = new TrRmRegisterBodyCallInput(
                2, sourceUse, checked((ushort)homeIndex));
        }

        if (formalParameters.Any(static assigned => !assigned)
            || !GenericMethodTypeMatchesAfterSubstitution(
                target.ReturnType, function.ReturnType, targetDefinition,
                definition, call.TypeArguments, 0))
        {
            throw Unsupported(
                $"Generic MethodDef '{definition.Name}' has a GenericCall signature outside the checked forwarding subset.");
        }

        var typeMap = new TrRmRegisterBodyTokenMapBuilder();
        uint GetBodyTypeToken(PrimitiveType type)
        {
            if (type == PrimitiveType.Void)
            {
                throw Unsupported(
                    "G1 GenericCall forwarding requires non-unit values.");
            }

            return CreateStandardBodyTypeMapToken(
                GenericMethodTypeSpecRef(type, formalTypeSpecRows,
                    builtInTypeSpecRows, typeSpecRows, spanTypes,
                    definition.ProofProgram.CheckedReferenceTypes,
                    definition.DefinitionOrdinal, definition,
                    allowSpanType: true, nominalTypes: nominalTypes));
        }

        var homeRows = new List<byte[]>(function.ParameterCount + 1);
        for (var parameter = 0; parameter < function.ParameterCount;
            parameter++)
        {
            var parameterType = function.Variables[parameter].Type;
            if (parameterType == PrimitiveType.Void)
            {
                continue;
            }

            var home = new byte[8];
            home[0] = 2;
            BinaryPrimitives.WriteUInt16LittleEndian(
                home.AsSpan(2),
                typeMap.GetIndex(GetBodyTypeToken(parameterType)));
            BinaryPrimitives.WriteUInt16LittleEndian(
                home.AsSpan(4), checked((ushort)parameter));
            homeRows.Add(home);
        }

        ushort resultHome = 0;
        if (!targetDoesNotReturn && !isUnitCall)
        {
            resultHome = checked((ushort)homeRows.Count);
            var result = new byte[8];
            result[0] = 3;
            result[1] = 1;
            BinaryPrimitives.WriteUInt16LittleEndian(
                result.AsSpan(2),
                typeMap.GetIndex(GetBodyTypeToken(function.ReturnType)));
            homeRows.Add(result);
        }

        var homeMap = new byte[checked(homeRows.Count * 8)];
        for (var homeIndex = 0; homeIndex < homeRows.Count; homeIndex++)
        {
            homeRows[homeIndex].CopyTo(homeMap, homeIndex * 8);
        }

        var targetMethodMap = new TrRmRegisterBodyTokenMapBuilder();
        var targetMethodToken = 0x1600_0000u
            | checked((uint)targetEmissions[0].MethodRow);
        var targetMethodMapIndex = targetMethodMap.GetIndex(
            targetMethodToken);
        var targetArguments = new TrRmRegisterBodyGenericCallArgument[
            call.TypeArguments.Length];
        for (var ordinal = 0; ordinal < targetArguments.Length; ordinal++)
        {
            var actual = call.TypeArguments[ordinal];
            var targetFormal = targetDefinition.Formals[ordinal];
            var callerFormalOrdinal = -1;
            for (var candidate = 0;
                candidate < definition.Formals.Length; candidate++)
            {
                if (definition.Formals[candidate].PlaceholderType == actual)
                {
                    callerFormalOrdinal = candidate;
                    break;
                }
            }

            if (callerFormalOrdinal >= 0)
            {
                var callerFormal = definition.Formals[callerFormalOrdinal];
                if (!SemanticVerifier
                        .IsGenericMethodFormalContractSatisfied(
                            targetFormal.OwnershipDomain,
                            targetFormal.NullabilityDomain,
                            targetFormal.RequiresWritable,
                            targetFormal.RequiresBlittable,
                            callerFormal.OwnershipDomain,
                            callerFormal.NullabilityDomain,
                            callerFormal.RequiresWritable,
                            callerFormal.RequiresBlittable)
                    || !SatisfiesInterfaceConstraints(
                        ordinal, callerFormal))
                {
                    throw Unsupported(
                        $"Generic MethodDef '{definition.Name}' forwards a caller formal without the target domain or property obligations.");
                }
            }
            else if (targetFormal.OwnershipDomain == 2
                || targetFormal.RequiresWritable
                || !targetFormal.InterfaceConstraints.IsEmpty)
            {
                throw Unsupported(
                    "G1 GenericCall forwarding has a type actual that does not satisfy the target formal.");
            }

            targetArguments[ordinal]
                = new TrRmRegisterBodyGenericCallArgument(1,
                    GenericMethodTypeSpecRef(actual, formalTypeSpecRows,
                        builtInTypeSpecRows, typeSpecRows, spanTypes,
                        definition.ProofProgram.CheckedReferenceTypes,
                        definition.DefinitionOrdinal, definition,
                        allowSpanType: true, nominalTypes: nominalTypes));
        }

        var records = new List<ReadOnlyMemory<byte>>(3);
        var entryState = new byte[8];
        entryState[0] = 1;
        records.Add(entryState);
        records.Add(TrRmRegisterBodyRecordWriter.EncodeGenericCallTarget(
            1, 0, targetMethodMapIndex, 0, 0, targetArguments,
            schema));
        var callSiteIndex = checked((ushort)records.Count);
        records.Add(TrRmRegisterBodyRecordWriter.EncodeCallSite(
            4, targetDoesNotReturn || isUnitCall ? (byte)0 : (byte)2, 1,
            resultHome, inputs, [], schema: schema));

        var code = targetDoesNotReturn || isUnitCall
            ? new byte[] { 0x90, checked((byte)callSiteIndex) }
            : [
                0x90,
                checked((byte)callSiteIndex),
                0xB8,
                checked((byte)resultHome),
            ];
        return TrRmRegisterBodyWriter.Encode(homeMap, typeMap.Encode(),
            targetMethodMap.Encode(), default, default,
            [new TrRmRegisterBodyBlock(0, checked((uint)code.Length), 0)],
            records, code, schema: schema);
    }

    private static byte[] EncodeGenericMethodForwardingChainBody(
        BoundFunction function, BoundGenericMethodDefinition definition,
        IReadOnlyList<GenericMethodEmission> emissions,
        ImmutableArray<GenericMethodForwardingCall> callChain,
        IReadOnlyDictionary<BoundGenericMethodImport, uint>
            importedGenericMethodTargetTokens,
        Dictionary<PrimitiveType, uint> formalTypeSpecRows,
        Dictionary<PrimitiveType, uint> builtInTypeSpecRows,
        List<ReadOnlyMemory<byte>> typeSpecRows,
        ImmutableArray<BoundSpanTypeDefinition> spanTypes,
        TrRmArtifactSchema schema,
        GenericMethodNominalTypeWriter nominalTypes)
    {
        var hasUnitResult = callChain.Length != 0
            && callChain[^1].ResultVariable < 0;
        var resultCallCount = callChain.Length
            - (hasUnitResult ? 1 : 0);
        if (function.ParameterCount > 8
            || ((function.ReturnType == PrimitiveType.Void)
                != hasUnitResult)
            || callChain.Length < 2
            || callChain.Length > SemanticVerifier.MaximumVariables
            || resultCallCount < 1
            || function.ParameterCount + resultCallCount
                > ushort.MaxValue
            || (!hasUnitResult
                && function.ParameterCount + resultCallCount - 1
                    > byte.MaxValue)
            || callChain.Length > ushort.MaxValue / 2)
        {
            throw Unsupported(
                $"Generic MethodDef '{definition.Name}' has a call chain outside the bounded forwarding subset.");
        }

        var proofProgram = definition.ProofProgram;
        GenericMethodForwardingTarget PrepareTarget(
            GenericMethodForwardingCall forwardingCall)
        {
            var genericCall = forwardingCall.Call;
            if ((uint)genericCall.Function
                >= (uint)proofProgram.Functions.Length)
            {
                throw Unsupported(
                    $"Generic MethodDef '{definition.Name}' has an unknown call-chain target.");
            }

            if (genericCall.ImportedGenericMethodCall
                is { } importedTarget)
            {
                var expectedTarget = importedTarget.Binding?.Expected
                    ?? importedTarget;
                var typeArguments
                    = genericCall.ImportedGenericMethodTypeArguments;
                if (genericCall.GenericMethodCall is not null
                    || importedTarget.Signature.Owner is not null
                    || !importedTarget.Signature.IsStatic
                    || importedTarget.Signature.IsMutable
                    || !importedTarget.Signature.ConstantFormals
                        .IsDefaultOrEmpty
                    || !importedGenericMethodTargetTokens.TryGetValue(
                        importedTarget, out var methodToken)
                    || expectedTarget.ProofFunction.ParameterCount > 8
                    || expectedTarget.ProofFunction.Variables.Length
                        < expectedTarget.ProofFunction.ParameterCount
                    || expectedTarget.ProofFunction.DoesNotReturn
                    || expectedTarget.ProofFunction.RequiresUnsafeCaller
                        != expectedTarget.Signature.RequiresUnsafeCaller
                    || expectedTarget.Formals.IsDefaultOrEmpty
                    || expectedTarget.Formals.Length
                        != expectedTarget.Signature.Formals.Length
                    || expectedTarget.Formals.Length > 8
                    || typeArguments.IsDefault
                    || typeArguments.Length
                        != expectedTarget.Formals.Length
                    || (!genericCall.ArgumentParameterIndices.IsDefault
                        && genericCall.ArgumentParameterIndices.Length
                            != genericCall.Arguments.Length))
                {
                    throw Unsupported(
                        $"Generic MethodDef '{definition.Name}' has an imported call-chain target outside the bounded MethodDef Import subset.");
                }

                var target = proofProgram.Functions[genericCall.Function];
                if (!ReferenceEquals(target.ImportedGenericMethod,
                        importedTarget)
                    || target.ImportedGenericMethodTypeArguments.IsDefault
                    || !target.ImportedGenericMethodTypeArguments
                        .SequenceEqual(typeArguments)
                    || !target.IsStatic || target.DeclaringStruct is not null
                    || target.DoesNotReturn
                    || target.ParameterCount != genericCall.Arguments.Length
                    || target.ParameterCount
                        != expectedTarget.ProofFunction.ParameterCount
                    || target.Variables.Length < target.ParameterCount
                    || target.ParameterCount > 8
                    || genericCall.Type != target.ReturnType
                    || (target.ReturnType == PrimitiveType.Void)
                        != (forwardingCall.ResultVariable < 0))
                {
                    throw Unsupported(
                        $"Generic MethodDef '{definition.Name}' does not retain the imported call-chain specialization.");
                }

                var targetDefinition = new BoundGenericMethodDefinition(
                    -1, expectedTarget.Signature.Name,
                    expectedTarget.Signature.IsPublic,
                    expectedTarget.Signature.RequiresUnsafeCaller,
                    genericCall.Function, -1, default,
                    expectedTarget.Formals, proofProgram) {
                    FormalTypeOwnerOrdinal
                        = expectedTarget.FormalTypeOwnerOrdinal,
                    IsStatic = expectedTarget.Signature.IsStatic,
                    FormalOrder = expectedTarget.Signature.FormalOrder,
                    RuntimeParameterCount
                        = expectedTarget.ProofFunction.ParameterCount,
                };
                for (var ordinal = 0;
                    ordinal < expectedTarget.Formals.Length; ordinal++)
                {
                    var formal = expectedTarget.Formals[ordinal];
                    var formalSignature
                        = expectedTarget.Signature.Formals[ordinal];
                    if (formal.Ordinal != ordinal
                        || formalSignature.Ordinal != ordinal
                        || formal.OwnershipDomain != 1
                        || formal.NullabilityDomain is < 1 or > 2
                        || !formal.Requirements.IsDefaultOrEmpty
                        || !formalSignature.Requirements.IsDefaultOrEmpty
                        || formal.InterfaceConstraints.IsDefault
                        || formalSignature.InterfaceConstraints.IsDefault
                        || formal.InterfaceConstraints.Length
                            != formalSignature.InterfaceConstraints.Length
                        || !formal.InterfaceConstraints.SequenceEqual(
                            formalSignature.InterfaceConstraints))
                    {
                        throw Unsupported(
                            $"Imported generic MethodDef '{expectedTarget.Signature.Name}' has a call-chain formal outside the selected G1 contract subset.");
                    }
                }

                return new GenericMethodForwardingTarget(genericCall,
                    target, expectedTarget.ProofFunction, targetDefinition,
                    typeArguments, methodToken,
                    forwardingCall.ResultVariable);
            }

            if (genericCall.GenericMethodCall
                    is not BoundGenericMethodInstantiation call)
            {
                throw Unsupported(
                    $"Generic MethodDef '{definition.Name}' has a call-chain target outside the local type-only subset.");
            }

            var target = proofProgram.Functions[genericCall.Function];
            var targetCall = target.GenericMethodProofCall;
            var targetEmissions = emissions.Where(candidate =>
                candidate.Definition.DefinitionOrdinal
                    == call.DefinitionOrdinal).ToArray();
            if (targetCall is null || targetEmissions.Length != 1
                || targetCall.DefinitionOrdinal != call.DefinitionOrdinal
                || !targetCall.TypeArguments.SequenceEqual(
                    call.TypeArguments)
                || !targetCall.ConstantArguments.IsEmpty
                || target.DeclaringStruct is not null || !target.IsStatic
                || targetEmissions[0].Definition.Owner is not null
                || !targetEmissions[0].Definition.IsStatic
                || target.DoesNotReturn
                || target.ParameterCount != genericCall.Arguments.Length
                || target.ParameterCount != target.Variables.Length
                || target.ParameterCount > 8
                || (target.ReturnType == PrimitiveType.Void)
                    != (forwardingCall.ResultVariable < 0)
                || genericCall.Type != target.ReturnType
                || HasConstantFormals(targetEmissions[0].Definition)
                || call.TypeArguments.Length
                    != targetEmissions[0].Definition.Formals.Length
                || (!genericCall.ArgumentParameterIndices.IsDefault
                    && genericCall.ArgumentParameterIndices.Length
                        != genericCall.Arguments.Length))
            {
                throw Unsupported(
                    $"Generic MethodDef '{definition.Name}' targets an unsupported local GenericMethod.");
            }

            var targetDefinition = targetEmissions[0].Definition;
            for (var ordinal = 0;
                ordinal < targetDefinition.Formals.Length; ordinal++)
            {
                var formal = targetDefinition.Formals[ordinal];
                if (!formal.Requirements.IsEmpty
                    || formal.InterfaceConstraints.IsDefault
                    || !formal.InterfaceConstraints.IsEmpty
                    || formal.OwnershipDomain != 1
                    || formal.NullabilityDomain is < 1 or > 2)
                {
                    throw Unsupported(
                        $"Generic MethodDef '{definition.Name}' targets a method with unsupported formal obligations.");
                }
            }

            return new GenericMethodForwardingTarget(genericCall, target,
                target, targetDefinition, call.TypeArguments,
                0x1600_0000u
                    | checked((uint)targetEmissions[0].MethodRow),
                forwardingCall.ResultVariable);
        }

        static bool HasConstantFormals(
            BoundGenericMethodDefinition target)
            => !target.ConstantFormals.IsDefaultOrEmpty;

        var targets = new GenericMethodForwardingTarget[callChain.Length];
        for (var index = 0; index < targets.Length; index++)
        {
            targets[index] = PrepareTarget(callChain[index]);
        }
        var typeMap = new TrRmRegisterBodyTokenMapBuilder();
        uint GetBodyTypeToken(PrimitiveType type)
        {
            if (type == PrimitiveType.Void)
            {
                throw Unsupported(
                    "G1 GenericCall forwarding requires non-unit values.");
            }

            return CreateStandardBodyTypeMapToken(
                GenericMethodTypeSpecRef(type, formalTypeSpecRows,
                    builtInTypeSpecRows, typeSpecRows, spanTypes,
                    proofProgram.CheckedReferenceTypes,
                    definition.DefinitionOrdinal, definition,
                    allowSpanType: true, nominalTypes: nominalTypes));
        }

        var homeRows = new List<byte[]>(
            function.ParameterCount + resultCallCount);
        var homeIndicesByVariable = new int[function.Variables.Length];
        Array.Fill(homeIndicesByVariable, -1);
        for (var parameter = 0; parameter < function.ParameterCount;
            parameter++)
        {
            var parameterType = function.Variables[parameter].Type;
            if (parameterType == PrimitiveType.Void)
            {
                continue;
            }

            var home = new byte[8];
            home[0] = 2;
            BinaryPrimitives.WriteUInt16LittleEndian(
                home.AsSpan(2), typeMap.GetIndex(GetBodyTypeToken(
                    parameterType)));
            BinaryPrimitives.WriteUInt16LittleEndian(
                home.AsSpan(4), checked((ushort)parameter));
            homeIndicesByVariable[parameter] = homeRows.Count;
            homeRows.Add(home);
        }

        foreach (var target in targets)
        {
            var resultVariable = target.ResultVariable;
            if (resultVariable < 0)
            {
                continue;
            }

            if ((uint)resultVariable >= (uint)function.Variables.Length
                || homeIndicesByVariable[resultVariable] != -1
                || homeRows.Count >= ushort.MaxValue)
            {
                throw Unsupported(
                    "A G1 GenericCall result exceeds its bounded Home map.");
            }

            var home = new byte[8];
            home[0] = 3;
            home[1] = 1;
            BinaryPrimitives.WriteUInt16LittleEndian(
                home.AsSpan(2), typeMap.GetIndex(GetBodyTypeToken(
                    function.Variables[resultVariable].Type)));
            homeIndicesByVariable[resultVariable] = homeRows.Count;
            homeRows.Add(home);
        }

        var homeMap = new byte[checked(homeRows.Count * 8)];
        for (var homeIndex = 0; homeIndex < homeRows.Count; homeIndex++)
        {
            homeRows[homeIndex].CopyTo(homeMap, homeIndex * 8);
        }

        var methodMap = new TrRmRegisterBodyTokenMapBuilder();
        var records = new List<ReadOnlyMemory<byte>>(
            checked(1 + (callChain.Length * 2)));
        var entryState = new byte[8];
        entryState[0] = 1;
        records.Add(entryState);
        var code = new List<byte>(checked((callChain.Length * 4) + 2));
        foreach (var target in targets)
        {
            var genericCall = target.Call;
            var typeArguments = target.TypeArguments;
            var targetDefinition = target.Definition;
            var inputs = new TrRmRegisterBodyCallInput[
                target.Function.ParameterCount];
            var assignedParameters = new bool[inputs.Length];
            for (var argumentIndex = 0;
                argumentIndex < genericCall.Arguments.Length;
                argumentIndex++)
            {
                var argument = genericCall.Arguments[argumentIndex];
                var formalIndex
                    = genericCall.ArgumentParameterIndices.IsDefault
                        ? argumentIndex
                        : genericCall.ArgumentParameterIndices[
                            argumentIndex];
                if ((uint)formalIndex >= (uint)inputs.Length
                    || assignedParameters[formalIndex])
                {
                    throw Unsupported(
                        $"Generic MethodDef '{definition.Name}' has a duplicate or invalid call-chain input.");
                }

                var targetParameterType
                    = target.SignatureFunction.Variables[formalIndex].Type;
                if (argument.Type == PrimitiveType.Void)
                {
                    if (!IsSupportedGenericUnitArgument(argument, function)
                        || !GenericMethodTypeMatchesAfterSubstitution(
                            targetParameterType,
                            argument.Type, targetDefinition, definition,
                            typeArguments, 0)
                        || ((target.MethodToken >> 24) == 0x1C
                            && target.Function.Variables[formalIndex].Type
                                != argument.Type)
                        || target.Function.Variables[formalIndex].IsMutable
                        || target.Function.Variables[formalIndex].IsOut
                        || target.Function.Variables[formalIndex].IsInit
                        || target.Function.Variables[formalIndex]
                            .IsImplicitRef)
                    {
                        throw Unsupported(
                            $"Generic MethodDef '{definition.Name}' has a unit call-chain input outside the exact None-selector subset.");
                    }

                    assignedParameters[formalIndex] = true;
                    inputs[formalIndex] = new TrRmRegisterBodyCallInput(
                        0, 0, 0);
                    continue;
                }

                if (argument is not BoundRead read
                    || (uint)read.Variable
                        >= (uint)(target.ResultVariable < 0
                            ? function.Variables.Length
                            : target.ResultVariable)
                    || (uint)read.Variable
                        >= (uint)function.Variables.Length
                    || function.Variables[read.Variable].Type
                        != argument.Type
                    || !GenericMethodTypeMatchesAfterSubstitution(
                        targetParameterType,
                        argument.Type, targetDefinition, definition,
                        typeArguments, 0)
                    || ((target.MethodToken >> 24) == 0x1C
                        && target.Function.Variables[formalIndex].Type
                            != argument.Type)
                    || target.Function.Variables[formalIndex].IsMutable
                    || target.Function.Variables[formalIndex].IsOut
                    || target.Function.Variables[formalIndex].IsInit
                    || target.Function.Variables[formalIndex]
                        .IsImplicitRef
                    || function.Variables[read.Variable].IsMutable
                    || function.Variables[read.Variable].IsOut
                    || function.Variables[read.Variable].IsInit
                    || function.Variables[read.Variable].IsImplicitRef
                    || homeIndicesByVariable[read.Variable] < 0)
                {
                    throw Unsupported(
                        $"Generic MethodDef '{definition.Name}' has a call-chain input outside the Copy Home subset.");
                }

                var callerFormalOrdinal = -1;
                for (var ordinal = 0;
                    ordinal < definition.Formals.Length; ordinal++)
                {
                    if (definition.Formals[ordinal].PlaceholderType
                        == argument.Type)
                    {
                        callerFormalOrdinal = ordinal;
                        break;
                    }
                }
                if (callerFormalOrdinal >= 0
                    && definition.Formals[callerFormalOrdinal]
                        .OwnershipDomain != 1)
                {
                    throw Unsupported(
                        $"Generic MethodDef '{definition.Name}' reads a non-Copy caller formal in a call chain.");
                }

                assignedParameters[formalIndex] = true;
                inputs[formalIndex] = new TrRmRegisterBodyCallInput(
                    2, 1, checked((ushort)homeIndicesByVariable[
                        read.Variable]));
            }

            var resultType = target.ResultVariable < 0
                ? PrimitiveType.Void
                : function.Variables[
                    target.ResultVariable].Type;
            if (assignedParameters.Any(static assigned => !assigned)
                || !GenericMethodTypeMatchesAfterSubstitution(
                    target.SignatureFunction.ReturnType,
                    resultType,
                    targetDefinition, definition, typeArguments, 0)
                || ((target.MethodToken >> 24) == 0x1C
                    && target.Function.ReturnType != resultType))
            {
                throw Unsupported(
                    $"Generic MethodDef '{definition.Name}' has a call-chain signature outside the checked subset.");
            }

            var targetArguments
                = new TrRmRegisterBodyGenericCallArgument[
                    typeArguments.Length];
            var callerFormalOrdinalsByTargetFormal
                = new Dictionary<int, int>();
            for (var targetOrdinal = 0;
                targetOrdinal < typeArguments.Length; targetOrdinal++)
            {
                var actualType = typeArguments[targetOrdinal];
                for (var callerOrdinal = 0;
                    callerOrdinal < definition.Formals.Length;
                    callerOrdinal++)
                {
                    if (definition.Formals[callerOrdinal].PlaceholderType
                        != actualType)
                    {
                        continue;
                    }

                    callerFormalOrdinalsByTargetFormal.Add(targetOrdinal,
                        callerOrdinal);
                    break;
                }
            }

            for (var ordinal = 0; ordinal < targetArguments.Length;
                ordinal++)
            {
                var actual = typeArguments[ordinal];
                var formal = targetDefinition.Formals[ordinal];
                var callerFormalOrdinal = -1;
                for (var candidate = 0;
                    candidate < definition.Formals.Length; candidate++)
                {
                    if (definition.Formals[candidate].PlaceholderType
                        == actual)
                    {
                        callerFormalOrdinal = candidate;
                        break;
                    }
                }

                if (callerFormalOrdinal >= 0)
                {
                    var callerFormal
                        = definition.Formals[callerFormalOrdinal];
                    if (!SemanticVerifier
                            .IsGenericMethodFormalContractSatisfied(
                                formal.OwnershipDomain,
                                formal.NullabilityDomain,
                                formal.RequiresWritable,
                                formal.RequiresBlittable,
                                callerFormal.OwnershipDomain,
                                callerFormal.NullabilityDomain,
                                callerFormal.RequiresWritable,
                                callerFormal.RequiresBlittable))
                    {
                        throw Unsupported(
                            $"Generic MethodDef '{definition.Name}' forwards a caller formal without satisfying the target domain.");
                    }

                    foreach (var targetConstraint
                        in formal.InterfaceConstraints)
                    {
                        if (!TrySubstituteGenericMethodInterfaceConstraint(
                                targetConstraint,
                                callerFormalOrdinalsByTargetFormal,
                                out var substitutedConstraint)
                            || !callerFormal.InterfaceConstraints.Any(
                                callerConstraint =>
                                    BoundInterfaceContract
                                        .IsInInheritanceClosure(
                                            callerConstraint,
                                            substitutedConstraint,
                                            definition.ProofProgram
                                                .Interfaces)))
                        {
                            throw Unsupported(
                                $"Generic MethodDef '{definition.Name}' forwards a caller formal without satisfying the target interface constraint.");
                        }
                    }
                }
                else if (formal.OwnershipDomain == 2
                    || formal.RequiresWritable
                    || !formal.InterfaceConstraints.IsEmpty)
                {
                    throw Unsupported(
                        "A G1 GenericCall chain has a TypeActual outside its supported domain.");
                }

                targetArguments[ordinal]
                    = new TrRmRegisterBodyGenericCallArgument(1,
                        GenericMethodTypeSpecRef(actual,
                            formalTypeSpecRows, builtInTypeSpecRows,
                            typeSpecRows, spanTypes,
                            proofProgram.CheckedReferenceTypes,
                            definition.DefinitionOrdinal, definition,
                            allowSpanType: true,
                            nominalTypes: nominalTypes));
            }

            var methodMapIndex = methodMap.GetIndex(target.MethodToken);
            var targetRecordIndex = checked((ushort)records.Count);
            records.Add(
                TrRmRegisterBodyRecordWriter.EncodeGenericCallTarget(
                    1, 0, methodMapIndex, 0, 0, targetArguments,
                    schema));
            var callSiteIndex = checked((ushort)records.Count);
            var hasResultHome = target.ResultVariable >= 0;
            var resultHome = hasResultHome
                ? checked((ushort)homeIndicesByVariable[
                    target.ResultVariable]) : (ushort)0;
            records.Add(TrRmRegisterBodyRecordWriter.EncodeCallSite(
                4, hasResultHome ? (byte)2 : (byte)0,
                targetRecordIndex, resultHome, inputs, [], schema: schema));
            if (callSiteIndex <= byte.MaxValue)
            {
                code.Add(0x90);
                code.Add(checked((byte)callSiteIndex));
            }
            else
            {
                code.Add(0xE0);
                code.Add(0);
                code.Add((byte)callSiteIndex);
                code.Add((byte)(callSiteIndex >> 8));
            }
        }

        if (!hasUnitResult)
        {
            var returnHome = homeIndicesByVariable[
                callChain[^1].ResultVariable];
            if (returnHome > byte.MaxValue)
            {
                throw Unsupported(
                    "A G1 forwarding chain exceeds the compact return-Home index.");
            }
            code.Add(0xB8);
            code.Add(checked((byte)returnHome));
        }
        return TrRmRegisterBodyWriter.Encode(homeMap,
            typeMap.Encode(), methodMap.Encode(), default, default,
            [new TrRmRegisterBodyBlock(0,
                checked((uint)code.Count), 0)],
            records, code.ToArray(), schema: schema);
    }

    private static bool TrySubstituteGenericMethodInterfaceConstraint(
        BoundInterfaceType type,
        IReadOnlyDictionary<int, int> callerFormalOrdinals,
        out BoundInterfaceType substituted, int depth = 0)
    {
        substituted = null!;
        if (depth > SemanticVerifier.MaximumDepth
            || type.TypeArguments.IsDefault)
        {
            return false;
        }
        if (type.Kind == BoundInterfaceTypeKind.TypeParameter)
        {
            if (type.TypeParameterScope
                    != BoundInterfaceTypeParameterScope.Member
                || !callerFormalOrdinals.TryGetValue(
                    type.TypeParameterOrdinal, out var callerOrdinal))
            {
                return false;
            }

            substituted = type with {
                TypeParameterOrdinal = callerOrdinal,
            };
            return true;
        }

        var typeArguments
            = ImmutableArray.CreateBuilder<BoundInterfaceTypeArgument>(
                type.TypeArguments.Length);
        foreach (var argument in type.TypeArguments)
        {
            if (argument is null
                || argument.ConstantParameterScope
                    != BoundInterfaceTypeParameterScope.None)
            {
                return false;
            }

            BoundInterfaceType? argumentType = null;
            BoundInterfaceType? constantType = null;
            if (argument.Type is BoundInterfaceType nestedType
                && !TrySubstituteGenericMethodInterfaceConstraint(
                    nestedType, callerFormalOrdinals,
                    out argumentType, depth + 1))
            {
                return false;
            }
            if (argument.ConstantType is BoundInterfaceType nestedConstantType
                && !TrySubstituteGenericMethodInterfaceConstraint(
                    nestedConstantType, callerFormalOrdinals,
                    out constantType, depth + 1))
            {
                return false;
            }

            typeArguments.Add(argument with {
                Type = argumentType,
                ConstantType = constantType,
            });
        }

        substituted = type with {
            TypeArguments = typeArguments.MoveToImmutable(),
        };
        return true;
    }

    private static byte[] EncodeGenericMethodRequirementBody(
        BoundFunction function, BoundGenericMethodDefinition definition,
        BoundProgram program,
        ImmutableArray<BoundSpanTypeDefinition> spanTypes,
        IReadOnlyDictionary<(string QualifiedName, int Arity), uint>
            exportedInterfaceTypeSpecRows,
        IReadOnlyDictionary<(
            string QualifiedName, int Arity, int MemberOrdinal,
            string DeclarationKey), uint> exportedInterfaceMethodDefRows,
        IReadOnlyDictionary<GenericImportedInterfaceMethodKey, uint>
            importedInterfaceMethodImportRows,
        Dictionary<PrimitiveType, uint> formalTypeSpecRows,
        Dictionary<PrimitiveType, uint> builtInTypeSpecRows,
        List<ReadOnlyMemory<byte>> typeSpecRows,
        IReadOnlyDictionary<(int FormalOrdinal, int ConstraintOrdinal),
            (uint ConstraintRow, uint TypeSpecRef)>
                interfaceConstraintRowRefs,
        TrRmArtifactSchema schema,
        GenericMethodNominalTypeWriter nominalTypes)
    {
        if (!schema.UsesGeneralMethodParameterLabels()
            || !TryGetDirectGenericCallReturn(function, out var call,
                out var resultVariable)
            || call is null
            || call.InterfaceCallIdentity is not
                BoundInterfaceCallIdentity identity
            || !identity.IsConstraintCall
            || call.GenericMethodCall is not null
            || call.ImportedGenericMethodCall is not null
            || call.Type == PrimitiveType.Void
            || call.Type != function.ReturnType
            || identity.Requirement.AccessorOrdinal != -1
            || !BoundInterfaceContract.AreEquivalent(
                identity.WitnessInterface, identity.RequestedInterface)
            || !BoundInterfaceContract.AreEquivalent(
                identity.Requirement.DeclaringInterface,
                identity.RequestedInterface)
            || (resultVariable < 0
                ? function.Variables.Length != function.ParameterCount
                : resultVariable != function.ParameterCount
                    || function.Variables.Length
                        != function.ParameterCount + 1)
            || function.ParameterCount > 8)
        {
            throw Unsupported(
                $"Generic MethodDef '{definition.Name}' has an interface-requirement Body outside the direct-return subset.");
        }

        var proofProgram = definition.ProofProgram;
        if ((uint)call.Function >= (uint)proofProgram.Functions.Length)
        {
            throw Unsupported(
                $"Generic MethodDef '{definition.Name}' calls an unknown interface requirement.");
        }

        var requirementStub = proofProgram.Functions[call.Function];
        if (requirementStub.InterfaceRequirement
                is not BoundInterfaceMemberIdentity targetRequirement
            || !BoundInterfaceContract.AreEquivalent(targetRequirement,
                identity.Requirement)
            || !requirementStub.IsExternal || !requirementStub.IsPrivate
            || requirementStub.IsStatic
            || requirementStub.DeclaringStruct is not null
            || requirementStub.GenericMethodProofCall is not null
            || requirementStub.ParameterCount != call.Arguments.Length
            || requirementStub.Variables.Length
                != requirementStub.ParameterCount
            || requirementStub.ParameterCount is < 1 or > 9
            || requirementStub.Variables[0].Type
                != identity.ImplementingType
            || requirementStub.Variables[0].IsOut
            || requirementStub.Variables[0].IsInit
            || requirementStub.Variables[0].IsImplicitRef
            || requirementStub.ReturnType != call.Type
            || requirementStub.DoesNotReturn
            || requirementStub.RequiresUnsafeCaller)
        {
            throw Unsupported(
                $"Generic MethodDef '{definition.Name}' has an unsupported interface requirement target.");
        }

        var interfaceName
            = identity.Requirement.DeclaringInterface.NominalName;
        if (interfaceName is null)
        {
            throw Unsupported(
                "A G1 interface requirement has no declaring-interface identity.");
        }

        var arity
            = identity.Requirement.DeclaringInterface.TypeArguments.Length;
        var matchingInterfaces = program.Interfaces.Where(candidate =>
            candidate.ModuleImport
                == identity.Requirement.DeclaringInterface.ModuleImport
            && StringComparer.Ordinal.Equals(candidate.QualifiedName,
                interfaceName)
            && candidate.Formals.Length == arity).ToArray();
        if (matchingInterfaces.Length != 1
            || (uint)identity.Requirement.MemberOrdinal
                >= (uint)matchingInterfaces[0].Members.Length)
        {
            throw Unsupported(
                "A G1 interface requirement has no exact local or imported InterfaceMethod target.");
        }

        var matchingInterface = matchingInterfaces[0];
        var requirementMember = matchingInterface.Members[
            identity.Requirement.MemberOrdinal];
        if (!StringComparer.Ordinal.Equals(requirementMember.DeclarationKey,
                identity.Requirement.DeclarationKey)
            || (requirementMember.RequiresMutableReceiver
                && !schema.UsesMethodReceiverAccess())
            || requirementStub.Variables[0].IsMutable
                != requirementMember.RequiresMutableReceiver)
        {
            throw Unsupported(
                "A G1 interface requirement has no exact local or imported InterfaceMethod target.");
        }

        uint targetMethodToken;
        if (identity.Requirement.DeclaringInterface.ModuleImport
            is BoundModuleImport importedInterface)
        {
            if (!importedInterfaceMethodImportRows.TryGetValue(
                    new GenericImportedInterfaceMethodKey(
                        importedInterface.ProducerIdentity,
                        importedInterface.ExportKey, interfaceName, arity,
                        identity.Requirement.MemberOrdinal,
                        identity.Requirement.DeclarationKey,
                        importedInterface.TargetRequirement,
                        importedInterface.RuntimePolicy),
                    out var importedMethodImportRow))
            {
                throw Unsupported(
                    "A G1 interface requirement has no exact MethodDef Import.");
            }

            targetMethodToken = 0x1C00_0000u | importedMethodImportRow;
        }
        else
        {
            if (!exportedInterfaceTypeSpecRows.ContainsKey(
                    (interfaceName, arity))
                || !exportedInterfaceMethodDefRows.TryGetValue(
                    (interfaceName, arity,
                        identity.Requirement.MemberOrdinal,
                        identity.Requirement.DeclarationKey),
                    out var localRequirementMethodRow))
            {
                throw Unsupported(
                    "A G1 interface requirement has no directly exported InterfaceMethod MethodDef.");
            }

            targetMethodToken = 0x1600_0000u
                | localRequirementMethodRow;
        }

        var implementingFormalOrdinal = -1;
        for (var ordinal = 0; ordinal < definition.Formals.Length; ordinal++)
        {
            if (definition.Formals[ordinal].PlaceholderType
                != identity.ImplementingType)
            {
                continue;
            }

            if (implementingFormalOrdinal >= 0)
            {
                throw Unsupported(
                    "A G1 interface requirement receiver has an ambiguous formal identity.");
            }
            implementingFormalOrdinal = ordinal;
        }
        if (implementingFormalOrdinal < 0)
        {
            throw Unsupported(
                "A G1 interface requirement receiver is not an in-scope type formal.");
        }

        var callerFormal = definition.Formals[implementingFormalOrdinal];
        if (callerFormal.InterfaceConstraints.IsDefault
            || callerFormal.OwnershipDomain != 1
            || (requirementMember.RequiresMutableReceiver
                && !callerFormal.RequiresWritable))
        {
            throw Unsupported(
                "A G1 interface requirement receiver lacks a supported Copy formal contract.");
        }

        var matchingConstraintOrdinal = -1;
        for (var ordinal = 0;
            ordinal < callerFormal.InterfaceConstraints.Length; ordinal++)
        {
            if (!BoundInterfaceContract.AreEquivalent(
                    callerFormal.InterfaceConstraints[ordinal],
                    identity.RequestedInterface))
            {
                continue;
            }

            if (matchingConstraintOrdinal >= 0)
            {
                throw Unsupported(
                    "A G1 interface requirement receiver has duplicate Constraint evidence.");
            }
            matchingConstraintOrdinal = ordinal;
        }
        if (matchingConstraintOrdinal < 0
            || !interfaceConstraintRowRefs.TryGetValue(
                (implementingFormalOrdinal, matchingConstraintOrdinal),
                out var interfaceConstraint)
            || interfaceConstraint.ConstraintRow == 0
            || interfaceConstraint.TypeSpecRef == 0)
        {
            throw Unsupported(
                "A G1 interface requirement call has no exact caller Constraint evidence.");
        }

        var argumentIndices = new bool[requirementStub.ParameterCount];
        var inputs = new TrRmRegisterBodyCallInput[
            requirementStub.ParameterCount];
        for (var argumentIndex = 0;
            argumentIndex < call.Arguments.Length; argumentIndex++)
        {
            var argument = call.Arguments[argumentIndex];
            var parameterIndex = call.ArgumentParameterIndices.IsDefault
                ? argumentIndex
                : call.ArgumentParameterIndices[argumentIndex];
            var isReceiver = parameterIndex == 0;
            var inputVariable = argument switch {
                BoundRead read => read.Variable,
                BoundReceiverLoan loan when isReceiver
                    && !requirementMember.RequiresMutableReceiver
                    => loan.Variable,
                BoundMutableReceiverLoan loan when isReceiver
                    => loan.Variable,
                _ => -1,
            };
            if ((uint)parameterIndex >= (uint)inputs.Length
                || (argumentIndex == 0 && parameterIndex != 0)
                || argumentIndices[parameterIndex]
                || (isReceiver
                    ? argument is not (BoundRead or BoundReceiverLoan
                        or BoundMutableReceiverLoan)
                    : argument is not BoundRead)
                || (isReceiver
                    && requirementMember.RequiresMutableReceiver
                    && argument is not BoundMutableReceiverLoan)
                || (uint)inputVariable >= (uint)function.ParameterCount
                || function.Variables[inputVariable].Type != argument.Type
                || requirementStub.Variables[parameterIndex].Type
                    != argument.Type
                || (!isReceiver
                    && function.Variables[inputVariable].IsMutable)
                || function.Variables[inputVariable].IsOut
                || function.Variables[inputVariable].IsInit
                || function.Variables[inputVariable].IsImplicitRef
                || (parameterIndex != 0
                    && requirementStub.Variables[parameterIndex].IsMutable)
                || requirementStub.Variables[parameterIndex].IsOut
                || requirementStub.Variables[parameterIndex].IsInit
                || requirementStub.Variables[parameterIndex]
                    .IsImplicitRef)
            {
                throw Unsupported(
                    $"Generic MethodDef '{definition.Name}' has an interface-call argument outside the parameter-Home subset.");
            }

            var argumentFormalOrdinal = -1;
            for (var ordinal = 0; ordinal < definition.Formals.Length;
                ordinal++)
            {
                if (definition.Formals[ordinal].PlaceholderType
                    == argument.Type)
                {
                    argumentFormalOrdinal = ordinal;
                    break;
                }
            }
            if (argumentFormalOrdinal >= 0
                && definition.Formals[argumentFormalOrdinal]
                    .OwnershipDomain != 1)
            {
                throw Unsupported(
                    "A G1 interface-call input does not have the Copy domain required by its parameter Home.");
            }

            argumentIndices[parameterIndex] = true;
            var sourceUse = (byte)1;
            if (isReceiver && schema.UsesMethodReceiverAccess())
            {
                sourceUse = requirementMember.RequiresMutableReceiver
                    ? (byte)4 : (byte)3;
            }
            inputs[parameterIndex] = new TrRmRegisterBodyCallInput(
                2, sourceUse, checked((ushort)inputVariable));
        }
        if (argumentIndices.Any(static assigned => !assigned)
            || requirementStub.ParameterCount != call.Arguments.Length)
        {
            throw Unsupported(
                $"Generic MethodDef '{definition.Name}' has an incomplete interface-call input list.");
        }

        var typeMap = new TrRmRegisterBodyTokenMapBuilder();
        uint GetBodyTypeToken(PrimitiveType type)
        {
            if (type == PrimitiveType.Void)
            {
                throw Unsupported(
                    "G1 interface-requirement calls require a non-unit result.");
            }

            return CreateStandardBodyTypeMapToken(
                GenericMethodTypeSpecRef(type, formalTypeSpecRows,
                    builtInTypeSpecRows, typeSpecRows, spanTypes,
                    definition.ProofProgram.CheckedReferenceTypes,
                    definition.DefinitionOrdinal, definition,
                    allowSpanType: true, nominalTypes: nominalTypes));
        }

        var homeCount = function.ParameterCount + 1;
        var homeMap = new byte[checked(homeCount * 8)];
        for (var parameter = 0; parameter < function.ParameterCount;
            parameter++)
        {
            var offset = parameter * 8;
            homeMap[offset] = 2;
            BinaryPrimitives.WriteUInt16LittleEndian(
                homeMap.AsSpan(offset + 2),
                typeMap.GetIndex(GetBodyTypeToken(
                    function.Variables[parameter].Type)));
            BinaryPrimitives.WriteUInt16LittleEndian(
                homeMap.AsSpan(offset + 4), checked((ushort)parameter));
        }

        var resultHome = checked((ushort)function.ParameterCount);
        var resultOffset = resultHome * 8;
        homeMap[resultOffset] = 3;
        homeMap[resultOffset + 1] = 1;
        BinaryPrimitives.WriteUInt16LittleEndian(
            homeMap.AsSpan(resultOffset + 2),
            typeMap.GetIndex(GetBodyTypeToken(function.ReturnType)));

        var targetMethodMap = new TrRmRegisterBodyTokenMapBuilder();
        var targetMethodMapIndex = targetMethodMap.GetIndex(
            targetMethodToken);
        var records = new List<ReadOnlyMemory<byte>>(3);
        var entryState = new byte[8];
        entryState[0] = 1;
        records.Add(entryState);
        records.Add(TrRmRegisterBodyRecordWriter.EncodeGenericCallTarget(
            2, 1, targetMethodMapIndex,
            interfaceConstraint.TypeSpecRef,
            interfaceConstraint.ConstraintRow, [], schema));
        var callSiteIndex = checked((ushort)records.Count);
        records.Add(TrRmRegisterBodyRecordWriter.EncodeCallSite(
            4, 2, 1, resultHome, inputs, [], schema: schema));

        var code = new byte[] {
            0x90,
            checked((byte)callSiteIndex),
            0xB8,
            checked((byte)resultHome),
        };
        return TrRmRegisterBodyWriter.Encode(homeMap, typeMap.Encode(),
            targetMethodMap.Encode(), default, default,
            [new TrRmRegisterBodyBlock(0, checked((uint)code.Length), 0)],
            records, code, schema: schema);
    }

    private static List<GenericImportedMethodEmission>
        CaptureImportedGenericMethodEmissions(
            IReadOnlyList<GenericMethodEmission> emissions,
            out List<BoundGenericMethodImport> directMethodDefTargets)
    {
        var result = new List<GenericImportedMethodEmission>();
        var captured = new Dictionary<BoundGenericMethodImport, byte>();
        directMethodDefTargets = [];
        var retainedDefinitionOrigins = emissions
            .Select(static emission => emission.Definition.RetainedOrigin)
            .OfType<BoundGenericMethodImportOrigin>()
            .Select(static origin => GetRetainedGenericMethodOriginKey(
                origin))
            .ToHashSet();
        var remainingVisits = SemanticVerifier.MaximumAnalysisVisits;
        foreach (var emission in emissions)
        {
            if (!BoundTreeTraversal.VisitExpressions(
                    emission.ProofFunction.Body, expression =>
                    {
                        if (--remainingVisits < 0)
                        {
                            throw Unsupported(
                                "Generic MethodDef imported targets exceed the supported analysis limit.");
                        }
                        if (expression is not BoundCall {
                                InterfaceCallIdentity: null,
                                ImportedGenericMethodCall: { } source,
                            })
                        {
                            return;
                        }

                        if (source.Origin.IsMethodDefTarget
                            && source.Binding is null)
                        {
                            if (!retainedDefinitionOrigins.Contains(
                                    GetRetainedGenericMethodOriginKey(
                                        source.Origin)))
                            {
                                throw Unsupported(
                                    "A direct MethodDef target is outside the selected retained root's re-emitted Body closure.");
                            }
                            if (captured.TryAdd(source, 0))
                            {
                                directMethodDefTargets.Add(source);
                            }
                            return;
                        }

                        var expected = source.Binding?.Expected ?? source;
                        BoundModuleImport import;
                        if (source.Binding is { } binding)
                        {
                            if (string.IsNullOrEmpty(
                                    binding.ProviderIdentity)
                                || string.IsNullOrEmpty(binding.ExportKey))
                            {
                                throw Unsupported(
                                    "A bound GenericCall Import has no exact provider ExportKey.");
                            }

                            import = new BoundModuleImport(
                                binding.ProviderIdentity,
                                binding.ExportKey,
                                binding.TargetRequirement,
                                binding.RuntimePolicy);
                        }
                        else
                        {
                            var origin = expected.Origin;
                            if (origin.IsMethodDefTarget
                                || string.IsNullOrEmpty(
                                    origin.ProviderIdentity)
                                || string.IsNullOrEmpty(origin.ExportKey))
                            {
                                throw Unsupported(
                                    "An imported GenericCall target has no exact provider ExportKey.");
                            }

                            import = new BoundModuleImport(
                                origin.ProviderIdentity, origin.ExportKey,
                                origin.TargetRequirement,
                                origin.RuntimePolicy);
                        }

                        if (captured.TryAdd(source, 0))
                        {
                            result.Add(new GenericImportedMethodEmission(
                                source, expected, import));
                        }
                    }))
            {
                throw Unsupported(
                    "The G1 source writer cannot inspect an imported GenericCall Body.");
            }
        }

        return result;
    }

    private static byte[] AddImportedGenericMethodMetadata(
        byte[] artifact, BoundProgram program,
        IReadOnlyList<GenericMethodEmission> emissions,
        IReadOnlyDictionary<(string QualifiedName, int Arity), uint>
            exportedInterfaceTypeSpecRows,
        IReadOnlyDictionary<(
            string QualifiedName, int Arity, int MemberOrdinal,
            string DeclarationKey), uint>
            exportedInterfaceAssociatedTypeDefRows,
        IReadOnlyDictionary<GenericImportedInterfaceAssociatedTypeKey,
            uint> importedInterfaceAssociatedTypeImportRows,
        TrRmArtifactSchema schema,
        TrRmFrozenScalarValueBuilder? methodParameterSurfaceValues,
        out Dictionary<BoundGenericMethodImport, uint>
            importedGenericMethodTargetTokens,
        out Dictionary<BoundGenericNominalTypeIdentity, uint>
            importedNominalTypeReferences,
        out BoundSourceTypeProviderSnapshot[]
            importedNominalTypeSnapshots)
    {
        importedGenericMethodTargetTokens = [];
        importedNominalTypeReferences = [];
        importedNominalTypeSnapshots = [];
        var importedMethods
            = CaptureImportedGenericMethodEmissions(emissions,
                out var directMethodDefTargets);
        var retainedDefinitionIndices
            = new Dictionary<RetainedGenericMethodOriginKey, int>();
        for (var index = 0; index < emissions.Count; index++)
        {
            if (emissions[index].Definition.RetainedOrigin
                is not BoundGenericMethodImportOrigin origin)
            {
                continue;
            }

            _ = retainedDefinitionIndices.TryAdd(
                GetRetainedGenericMethodOriginKey(origin), index);
        }
        if (importedMethods.Count == 0
            && directMethodDefTargets.Count == 0)
        {
            return artifact;
        }

        var artifactIndex = new TrRmArtifactIndex(artifact, schema);
        var sections = new List<TrRmSection>(artifactIndex.SectionCount);
        for (var sectionIndex = 0;
            sectionIndex < artifactIndex.SectionCount; sectionIndex++)
        {
            artifactIndex.GetSection(sectionIndex, out var kind,
                out var flags, out var bytes, out var count);
            sections.Add(new TrRmSection(kind, flags,
                checked((uint)count), bytes.ToArray()));
        }

        var classOwnerTypeDefRows = new Dictionary<int, int>();
        if (emissions.Any(static emission =>
                emission.Definition.Owner is not null))
        {
            if (sourceClassExports is null
                || !ReferenceEquals(sourceClassExports.Program, program))
            {
                throw Unsupported(
                    "A generic class MethodDef requires the exact source-class export set.");
            }

            var baseTables = TrRmStructuralMetadataValidator
                .ValidateGenericMethodCallsDraftAndGetExecutionTables(
                    index);
            foreach (var emission in emissions)
            {
                if (emission.Definition.Owner
                    is not BoundGenericMethodOwner owner)
                {
                    continue;
                }

                var classIndex = ManagedClassTypes.ClassIndex(
                    owner.ClosedType);
                var classExports = sourceClassExports.Exports.Where(export =>
                    export.Declaration.ClassIndex == classIndex).ToArray();
                if (classExports.Length == 0)
                {
                    throw Unsupported(
                        $"Generic class MethodDef '{emission.Definition.Name}' has no source TypeDef ExportKey.");
                }

                var typeDefRows = new HashSet<int>();
                foreach (var classExport in classExports)
                {
                    if (!baseTables.Exports.TryGetDefinitionForKey(1,
                            classExport.ExportKey, out var typeDefId)
                        || (typeDefId >> 24) != 0x13)
                    {
                        throw new InvalidDataException(
                            "A generic class MethodDef owner ExportKey does not identify a TypeDef.");
                    }

                    var typeDefRow = checked((int)(
                        typeDefId & 0x00FF_FFFF));
                    if (baseTables.TypeDefs.GetOwnerModule(typeDefRow) != 1
                        || baseTables.TypeDefs.GetKind(typeDefRow) != 2)
                    {
                        throw new InvalidDataException(
                            "A generic class MethodDef owner is not a root class TypeDef.");
                    }

                    _ = typeDefRows.Add(typeDefRow);
                }

                if (typeDefRows.Count != 1
                    || !classOwnerTypeDefRows.TryAdd(
                        emission.Definition.DefinitionOrdinal,
                        typeDefRows.Single()))
                {
                    throw new InvalidDataException(
                        "A generic class MethodDef maps to conflicting source TypeDefs.");
                }
            }
        }

        var stringSection = GetSection(sections, 0x0002);
        var sourceStrings = new TrRmStringTableReader(
            stringSection.Bytes.Span,
            checked((int)stringSection.RecordCount));
        var names = new List<(string Text, int Encoding)>(
            sourceStrings.Count + (importedMethods.Count * 4));
        for (var row = 1; row <= sourceStrings.Count; row++)
        {
            names.Add((sourceStrings.DecodeNamespace(row), 1));
        }

        var moduleRefCount = GetCount(sections, 0x0010);
        var moduleRefRows = new List<TrRmModuleRefRow>(
            moduleRefCount + importedMethods.Count);
        var moduleRefRowsByProvider = new Dictionary<string, (
            int Row, uint TargetRequirement, uint RuntimePolicy)>(
                StringComparer.Ordinal);
        if (TryGetSection(sections, 0x0010, out var moduleRefSection))
        {
            if (moduleRefSection.Bytes.Length
                != checked(moduleRefCount * 12))
            {
                throw Invalid(
                    "The G1 MethodDef Import ModuleRef table has an invalid extent.");
            }

            for (var row = 1; row <= moduleRefCount; row++)
            {
                var data = moduleRefSection.Bytes.Span.Slice(
                    (row - 1) * 12, 12);
                var providerIdentity = sourceStrings.DecodeName(
                    ReadInt(data, 0));
                var targetRequirement
                    = BinaryPrimitives.ReadUInt32LittleEndian(data[4..]);
                var runtimePolicy
                    = BinaryPrimitives.ReadUInt32LittleEndian(data[8..]);
                if (moduleRefRowsByProvider.TryGetValue(providerIdentity,
                        out var existing)
                    && (existing.TargetRequirement != targetRequirement
                        || existing.RuntimePolicy != runtimePolicy))
                {
                    throw Invalid(
                        "The G1 MethodDef Import ModuleRef table has conflicting provider requirements.");
                }

                _ = moduleRefRowsByProvider.TryAdd(providerIdentity,
                    (row, targetRequirement, runtimePolicy));
                moduleRefRows.Add(new TrRmModuleRefRow(
                    ReadInt(data, 0), targetRequirement, runtimePolicy));
            }
        }

        var importCount = GetCount(sections, 0x001C);
        var importRows = new List<TrRmImportRow>(
            importCount + importedMethods.Count);
        var importTriples = new HashSet<(
            int ModuleRefRow, string ExportKey, uint ExpectedDefinition)>();
        if (TryGetSection(sections, 0x001C, out var importSection))
        {
            if (importSection.Bytes.Length != checked(importCount * 12))
            {
                throw Invalid(
                    "The G1 MethodDef Import table has an invalid extent.");
            }

            for (var row = 1; row <= importCount; row++)
            {
                var data = importSection.Bytes.Span.Slice(
                    (row - 1) * 12, 12);
                var moduleRefRow = ReadInt(data, 0);
                var exportKeyRow = ReadInt(data, 4);
                var expectedDefinition
                    = BinaryPrimitives.ReadUInt32LittleEndian(data[8..]);
                if (!importTriples.Add((moduleRefRow,
                        sourceStrings.DecodeName(exportKeyRow),
                        expectedDefinition)))
                {
                    throw Invalid(
                        "The G1 MethodDef Import table contains a duplicate expectation.");
                }

                importRows.Add(new TrRmImportRow(moduleRefRow,
                    exportKeyRow, expectedDefinition));
            }
        }

        int GetModuleRefRow(BoundModuleImport import)
        {
            if (moduleRefRowsByProvider.TryGetValue(
                    import.ProducerIdentity, out var existing))
            {
                if (existing.TargetRequirement != import.TargetRequirement
                    || existing.RuntimePolicy != import.RuntimePolicy)
                {
                    throw Unsupported(
                        "An imported GenericCall target disagrees with an existing ModuleRef.");
                }

                return existing.Row;
            }

            var providerStringRow = names.Count + 1;
            names.Add((import.ProducerIdentity, 1));
            var moduleRefRow = moduleRefRows.Count + 1;
            moduleRefRows.Add(new TrRmModuleRefRow(providerStringRow,
                import.TargetRequirement, import.RuntimePolicy));
            moduleRefRowsByProvider.Add(import.ProducerIdentity,
                (moduleRefRow, import.TargetRequirement,
                    import.RuntimePolicy));
            return moduleRefRow;
        }

        var typeDefCount = GetCount(sections, 0x0013);
        var methodCount = GetCount(sections, 0x0016);
        var signatureCount = GetCount(sections, 0x0017);
        var parameterCount = GetCount(sections, 0x0018);
        var featureCount = GetCount(sections, 0x0004);
        var sourceFileCount = GetCount(sections, 0x001F);
        var bodyCount = GetCount(sections, 0x0020);
        var nativeFunctionPointerSignatureCount
            = GetCount(sections, 0x0052);
        var methodBytes = TryGetSection(sections, 0x0016,
                out var methodSection)
            ? methodSection.Bytes.ToArray().ToList() : [];
        var signatureBytes = TryGetSection(sections, 0x0017,
                out var signatureSection)
            ? signatureSection.Bytes.ToArray().ToList() : [];
        var parameterBytes = TryGetSection(sections, 0x0018,
                out var parameterSection)
            ? parameterSection.Bytes.ToArray().ToList() : [];
        var typeSpecRows = ReadVariableRows(sections, 0x0011, 2, 1);
        var builtInTypeSpecRows = GetBuiltInTypeSpecRows(typeSpecRows);
        var genericParameterRows = ReadGenericParamRows(sections);
        var constraintRows = ReadConstraintRows(sections);
        var associatedRelationRows
            = ReadAssociatedRelationRows(sections);
        var contractRows = ReadVariableRows(sections, 0x001B, 8, 4);
        var importedInterfaceImportRows
            = ReadImportedInterfaceImportRows(sections, sourceStrings);
        var methodParameterLabelBytes = TryGetSection(sections, 0x0066,
                out var methodParameterLabelSection)
            ? methodParameterLabelSection.Bytes.ToArray().ToList() : [];
        var methodParameterLabelCount = GetCount(sections, 0x0066);
        if (methodParameterLabelBytes.Count
            != checked(methodParameterLabelCount
                * GetMethodParameterSurfaceRowSize(schema)))
        {
            throw Invalid(
                "The G1L1 MethodParameterLabel table has an invalid extent.");
        }

        var importedMethodRows = new int[importedMethods.Count];
        var importedMethodNameStringRows = new int[importedMethods.Count];
        var importedOwnerTypeTokens = new uint[importedMethods.Count];
        for (var importedOrdinal = 0;
            importedOrdinal < importedMethods.Count; importedOrdinal++)
        {
            var imported = importedMethods[importedOrdinal];
            var signature = imported.Expected.Signature;
            var proofFunction = imported.Expected.ProofFunction;
            var hasClassOwner
                = imported.Source.Origin.ClassOwner is not null;
            var receiverCount = signature.IsStatic ? 0 : 1;
            var methodParameterCount
                = proofFunction.ParameterCount - receiverCount;
            if (!signature.IsPublic
                || (!signature.IsStatic && !hasClassOwner)
                || (signature.IsStatic && signature.IsMutable)
                || signature.Owner is not null
                || !signature.ConstantFormals.IsDefaultOrEmpty
                || signature.FormalOrder.IsDefault
                || signature.FormalOrder.Length
                    != imported.Expected.Formals.Length
                || proofFunction.IsStatic != signature.IsStatic
                || proofFunction.IsMutable != signature.IsMutable
                || (!hasClassOwner
                    && proofFunction.DeclaringStruct is not null)
                || (hasClassOwner
                    && proofFunction.DeclaringStruct is null)
                || string.IsNullOrEmpty(proofFunction.Name)
                || methodParameterCount is < 0 or > 8
                || proofFunction.Variables.Length
                    < proofFunction.ParameterCount
                || (receiverCount != 0
                    && (proofFunction.Variables[0].Name != "this"
                        || !BoundGenericNominalTypeContract
                            .IsNominalClassType(
                                proofFunction.Variables[0].Type,
                                program.GenericNominalTypeInstances,
                                program.GenericNominalTypeDefinitions)
                        || program.GenericNominalTypeInstances[
                            BoundGenericNominalTypeTypes.Index(
                                proofFunction.Variables[0].Type)]
                            .Definition
                            != imported.Source.Origin.ClassOwner!
                                .Identity))
                || proofFunction.DoesNotReturn
                || proofFunction.RequiresUnsafeCaller
                    != signature.RequiresUnsafeCaller
                || imported.Expected.Formals.IsDefaultOrEmpty
                || imported.Expected.Formals.Length
                    != signature.Formals.Length
                || imported.Expected.Formals.Length > 8)
            {
                throw Unsupported(
                    $"Imported generic MethodDef '{proofFunction.Name}' is outside the bounded type-only Copy-value subset.");
            }

            var moduleRefRow = GetModuleRefRow(imported.Import);
            importedOwnerTypeTokens[importedOrdinal]
                = GetImportedGenericMethodOwnerTypeToken(
                    imported.Source, imported.Import, moduleRefRow);
            var exportKeyStringRow = names.Count + 1;
            names.Add((imported.Import.ExportKey, 1));
            importedMethodNameStringRows[importedOrdinal]
                = names.Count + 1;
            names.Add((proofFunction.Name, 1));
            var methodRow = checked(methodCount + importedOrdinal + 1);
            importedMethodRows[importedOrdinal] = methodRow;
            var methodDefId = 0x1600_0000u | checked((uint)methodRow);
            if (!importTriples.Add((moduleRefRow,
                    imported.Import.ExportKey, methodDefId)))
            {
                throw Invalid(
                    "An imported generic MethodDef duplicates an existing expectation.");
            }

            var importRow = checked(importRows.Count + 1);
            importRows.Add(new TrRmImportRow(moduleRefRow,
                exportKeyStringRow, methodDefId));
            importedGenericMethodTargetTokens.Add(imported.Source,
                0x1C00_0000u | checked((uint)importRow));
        }

        uint GetImportedGenericMethodOwnerTypeToken(
            BoundGenericMethodImport imported,
            BoundModuleImport methodImport, int methodModuleRefRow)
        {
            if (imported.Origin.ClassOwner
                is not BoundGenericMethodClassOwner owner)
            {
                return 0;
            }

            if (!StringComparer.Ordinal.Equals(
                    owner.Identity.ModuleIdentity,
                    methodImport.ProducerIdentity)
                || owner.TargetRequirement
                    != methodImport.TargetRequirement
                || owner.RuntimePolicy != methodImport.RuntimePolicy)
            {
                throw Invalid(
                    "A generic class MethodDef Import changed its selected owner provider.");
            }

            var ownerTypeDefToken = 0u;
            foreach (var import in importRows)
            {
                if (import.ModuleRefRow != methodModuleRefRow
                    || (import.ExpectedLocalDefId >> 24) != 0x13
                    || (import.ExpectedLocalDefId & 0x00FF_FFFF) == 0
                    || import.ExportKeyStringRow <= 0
                    || import.ExportKeyStringRow > names.Count
                    || !StringComparer.Ordinal.Equals(
                        names[import.ExportKeyStringRow - 1].Text,
                        owner.ExportKey))
                {
                    continue;
                }

                if (ownerTypeDefToken != 0
                    && ownerTypeDefToken != import.ExpectedLocalDefId)
                {
                    throw Invalid(
                        "A generic class MethodDef Import owner key resolves to conflicting TypeDefs.");
                }
                ownerTypeDefToken = import.ExpectedLocalDefId;
            }

            if (ownerTypeDefToken == 0)
            {
                throw Unsupported(
                    "A generic class MethodDef Import requires its exact owner TypeDef Import to be retained.");
            }

            return ownerTypeDefToken;
        }

        foreach (var directTarget in directMethodDefTargets)
        {
            var originKey = GetRetainedGenericMethodOriginKey(
                directTarget.Origin);
            if (!retainedDefinitionIndices.TryGetValue(originKey,
                    out var emissionIndex)
                || !IsSameRetainedGenericMethodOrigin(
                    emissions[emissionIndex].Definition.RetainedOrigin,
                    directTarget.Origin))
            {
                throw Unsupported(
                    "A direct MethodDef target has no emitted retained Body.");
            }

            var methodRow = checked(methodCount + importedMethods.Count
                + emissionIndex + 1);
            importedGenericMethodTargetTokens.Add(directTarget,
                0x1600_0000u | checked((uint)methodRow));
        }

        var preallocatedStrings = TrRmStringTableWriter.EncodeTextRows(names);
        var preallocatedStringTable = new TrRmStringTableReader(
            preallocatedStrings, names.Count);
        var preallocatedModuleBytes
            = GetSection(sections, 0x0001).Bytes.ToArray();
        var preallocatedModules = new TrRmModuleTableReader(
            preallocatedModuleBytes, 1, preallocatedStringTable,
            moduleRefRows.Count, typeDefCount, methodCount, sourceFileCount,
            featureCount);
        var preallocatedModuleRefBytes
            = TrRmModuleRefTableWriter.Encode(moduleRefRows,
                preallocatedStringTable, preallocatedModules);
        var preallocatedImportBytes
            = new byte[checked(importRows.Count * 12)];
        for (var row = 0; row < importRows.Count; row++)
        {
            var import = importRows[row];
            var offset = row * 12;
            BinaryPrimitives.WriteUInt32LittleEndian(
                preallocatedImportBytes.AsSpan(offset),
                checked((uint)import.ModuleRefRow));
            BinaryPrimitives.WriteUInt32LittleEndian(
                preallocatedImportBytes.AsSpan(offset + 4),
                checked((uint)import.ExportKeyStringRow));
            BinaryPrimitives.WriteUInt32LittleEndian(
                preallocatedImportBytes.AsSpan(offset + 8),
                import.ExpectedLocalDefId);
        }
        SetSection(sections, 0x0002, checked((uint)names.Count),
            preallocatedStrings);
        SetSection(sections, 0x0010,
            checked((uint)moduleRefRows.Count), preallocatedModuleRefBytes);
        SetSection(sections, 0x001C, checked((uint)importRows.Count),
            preallocatedImportBytes);

        var nominalTypes = new GenericMethodNominalTypeWriter(sections,
            names, typeSpecRows, genericParameterRows, constraintRows,
            program, emissions, schema);
        for (var importedOrdinal = 0;
            importedOrdinal < importedMethods.Count; importedOrdinal++)
        {
            var imported = importedMethods[importedOrdinal];
            var expected = imported.Expected;
            var signature = expected.Signature;
            var proofFunction = expected.ProofFunction;
            var methodRow = importedMethodRows[importedOrdinal];
            var methodDefId = 0x1600_0000u | checked((uint)methodRow);
            var methodNameStringRow
                = importedMethodNameStringRows[importedOrdinal];

            var importedTypeProgram
                = emissions[0].Definition.ProofProgram;
            var importedSpanTypes
                = ImmutableArray.CreateRange(expected.SpanTypes);
            var targetDefinition = new BoundGenericMethodDefinition(
                -1, signature.Name, signature.IsPublic,
                signature.RequiresUnsafeCaller, -1, -1, default,
                expected.Formals, importedTypeProgram) {
                FormalTypeOwnerOrdinal = expected.FormalTypeOwnerOrdinal,
                IsStatic = signature.IsStatic,
                IsMutable = signature.IsMutable,
                FormalOrder = signature.FormalOrder,
                RuntimeParameterCount = proofFunction.ParameterCount,
            };
            nominalTypes.Prepare(targetDefinition, proofFunction);
            var genericFirst = checked(genericParameterRows.Count + 1);
            var formalTypeSpecRows
                = new Dictionary<PrimitiveType, uint>(
                    expected.Formals.Length);
            var genericParamRows = new int[expected.Formals.Length];
            for (var formalOrdinal = 0;
                formalOrdinal < expected.Formals.Length; formalOrdinal++)
            {
                var formal = expected.Formals[formalOrdinal];
                var genericParamRow = checked(
                    (int)genericFirst + formalOrdinal);
                genericParamRows[formalOrdinal] = genericParamRow;
                var typeSpecRef = checked(
                    (uint)typeSpecRows.Count + 1);
                typeSpecRows.Add(TrRmTypeSpecTableWriter.Reference(0x04,
                    checked((uint)genericParamRow)));
                if (!formalTypeSpecRows.TryAdd(formal.PlaceholderType,
                        typeSpecRef))
                {
                    throw Unsupported(
                        $"Imported generic MethodDef '{proofFunction.Name}' has duplicate formal type identities.");
                }
            }

            uint GetExpectedTypeSpecRef(PrimitiveType type)
            {
                if (formalTypeSpecRows.TryGetValue(type,
                        out var formalTypeSpecRef))
                {
                    return formalTypeSpecRef;
                }

                return GenericMethodTypeSpecRef(type,
                    formalTypeSpecRows, builtInTypeSpecRows, typeSpecRows,
                    importedSpanTypes,
                    importedTypeProgram.CheckedReferenceTypes, -1,
                    targetDefinition, allowSpanType: true,
                    nominalTypes: nominalTypes);
            }

            uint GetExpectedTypeDefaultRef(
                BoundGenericMethodTypeDefault? typeDefault,
                int formalOrdinal)
                => typeDefault is null ? 0
                    : GenericMethodTypeDefaultRef(typeDefault,
                        formalOrdinal, targetDefinition, formalTypeSpecRows,
                        builtInTypeSpecRows, typeSpecRows,
                        nominalTypes: nominalTypes);

            if (!schema.UsesGeneralMethodParameterLabels()
                && expected.Formals.Any(static formal =>
                    !formal.InterfaceConstraints.IsEmpty))
            {
                throw Unsupported(
                    "Imported generic MethodDef interface constraints require the exact G1L1 schema.");
            }

            var interfaceConstraintVisits
                = SemanticVerifier.MaximumAnalysisVisits;
            for (var formalOrdinal = 0;
                formalOrdinal < expected.Formals.Length; formalOrdinal++)
            {
                var formal = expected.Formals[formalOrdinal];
                var formalSignature
                    = signature.Formals[formalOrdinal];
                var formalSlot = signature.FormalOrder[formalOrdinal];
                if (formal.Ordinal != formalOrdinal
                    || formalSignature.Ordinal != formalOrdinal
                    || formalSlot.Ordinal != formalOrdinal
                    || formalSlot.Kind
                        != BoundGenericMethodFormalKind.Type
                    || formalSlot.TypeFormalOrdinal != formalOrdinal
                    || formalSlot.ConstantFormalOrdinal != -1
                    || !StringComparer.Ordinal.Equals(formal.Name,
                        formalSignature.Name)
                    || formal.OwnershipDomain
                        != formalSignature.OwnershipDomain
                    || formal.NullabilityDomain
                        != formalSignature.NullabilityDomain
                    || formal.RequiresWritable
                        != formalSignature.RequiresWritable
                    || formal.RequiresBlittable
                        != formalSignature.RequiresBlittable
                    || formalSignature.HasTypeDefault
                        != (formal.TypeDefault is not null)
                    || !formalSignature
                        .DefaultTypeParameterDependencies.SequenceEqual(
                            formal.TypeDefault?.FormalDependencies ?? [])
                    || !formal.Requirements.IsDefaultOrEmpty
                    || !formalSignature.Requirements.IsDefaultOrEmpty
                    || formal.InterfaceConstraints.IsDefault
                    || formalSignature.InterfaceConstraints.IsDefault
                    || formal.AssociatedRelations.IsDefault
                    || formalSignature.AssociatedRelations.IsDefault
                    || formal.InterfaceConstraints.Length
                        != formalSignature.InterfaceConstraints.Length
                    || !formal.InterfaceConstraints.SequenceEqual(
                        formalSignature.InterfaceConstraints)
                    || formal.AssociatedRelations.Length
                        != formalSignature.AssociatedRelations.Length
                    || !formal.AssociatedRelations.SequenceEqual(
                        formalSignature.AssociatedRelations)
                    || formal.OwnershipDomain is < 1 or > 3
                    || formal.NullabilityDomain is < 1 or > 2)
                {
                    throw Unsupported(
                        $"Imported generic MethodDef '{proofFunction.Name}' has a formal outside the selected G1 contract subset.");
                }

                var genericParamRow = genericParamRows[formalOrdinal];
                var formalNameStringRow = names.Count + 1;
                names.Add((formal.Name, 1));
                var defaultArgumentRef
                    = GetExpectedTypeDefaultRef(formal.TypeDefault,
                        formalOrdinal);
                var constraintFirst = checked(constraintRows.Count + 1);
                if (formal.RequiresWritable)
                {
                    constraintRows.Add(new TrRmConstraintRow(
                        genericParamRow, 2, 2));
                }
                if (formal.RequiresBlittable)
                {
                    constraintRows.Add(new TrRmConstraintRow(
                        genericParamRow, 2, 3));
                }
                for (var constraintOrdinal = 0;
                    constraintOrdinal < formal.InterfaceConstraints.Length;
                    constraintOrdinal++)
                {
                    var constraintTypeSpec
                        = GenericMethodInterfaceConstraintTypeSpecRef(
                            formal.InterfaceConstraints[constraintOrdinal],
                            targetDefinition,
                            exportedInterfaceTypeSpecRows,
                            importedInterfaceImportRows,
                            formalTypeSpecRows, genericParamRows,
                            builtInTypeSpecRows, typeSpecRows, 0,
                            ref interfaceConstraintVisits);
                    constraintRows.Add(new TrRmConstraintRow(
                        genericParamRow, 1, constraintTypeSpec));
                }
                if (!formal.AssociatedRelations.IsEmpty
                    && !schema.UsesAssociatedRelationConstraints())
                {
                    throw Unsupported(
                        "Imported associated-relation constraints require the exact CM1AR1 schema.");
                }
                foreach (var relation in formal.AssociatedRelations)
                {
                    if (relation.FormalOrdinal != formalOrdinal
                        || !formal.InterfaceConstraints.Any(
                            constraint =>
                                BoundInterfaceContract
                                    .AreEquivalentProviderInterfaceViews(
                                        constraint,
                                        relation.AssociatedType
                                            .DeclaringInterface)))
                    {
                        throw Unsupported(
                            $"Imported generic MethodDef '{proofFunction.Name}' has an associated relation without its separate matching interface constraint.");
                    }

                    var declaringInterfaceTypeSpec
                        = GenericMethodInterfaceConstraintTypeSpecRef(
                            relation.AssociatedType.DeclaringInterface,
                            targetDefinition,
                            exportedInterfaceTypeSpecRows,
                            importedInterfaceImportRows,
                            formalTypeSpecRows, genericParamRows,
                            builtInTypeSpecRows, typeSpecRows, 0,
                            ref interfaceConstraintVisits);
                    var associatedTypeDefReference
                        = GenericMethodAssociatedTypeDefReference(
                            relation,
                            exportedInterfaceAssociatedTypeDefRows,
                            importedInterfaceAssociatedTypeImportRows);
                    if (!BoundGenericMethodTypeTerms
                            .TryGetFormalDependencies(
                                relation.RequiredType,
                                out var requiredTypeFormalDependencies)
                        || !BoundGenericMethodTypeTerms
                            .TryGetConstantFormalDependencies(
                                relation.RequiredType,
                                out var requiredTypeConstantDependencies))
                    {
                        throw Unsupported(
                            $"Imported generic MethodDef '{proofFunction.Name}' has an invalid associated required type.");
                    }

                    var requiredTypeSpec
                        = GenericMethodTypeDefaultRef(
                            new BoundGenericMethodTypeDefault(
                                relation.RequiredType,
                                requiredTypeFormalDependencies) {
                                ConstantFormalDependencies =
                                    requiredTypeConstantDependencies,
                            }, expected.Formals.Length, targetDefinition,
                            formalTypeSpecRows, builtInTypeSpecRows,
                            typeSpecRows, nominalTypes);
                    var associatedRelationRow
                        = checked((uint)associatedRelationRows.Count + 1);
                    associatedRelationRows.Add(
                        new TrRmAssociatedRelationRow(
                            declaringInterfaceTypeSpec,
                            associatedTypeDefReference,
                            requiredTypeSpec));
                    constraintRows.Add(new TrRmConstraintRow(
                        genericParamRow, 3, associatedRelationRow));
                }
                var constraintCount = constraintRows.Count
                    - constraintFirst + 1;
                if (constraintCount == 0)
                {
                    constraintFirst = 0;
                }

                genericParameterRows.Add(new TrRmGenericParamRow(
                    methodDefId, formalOrdinal, formalNameStringRow,
                    constraintFirst, constraintCount, 1,
                    formal.OwnershipDomain, formal.NullabilityDomain,
                    0, defaultArgumentRef));
            }

            var firstParameter = methodParameterCount == 0 ? 0
                : checked(parameterCount + 1);
            for (var parameterOrdinal = 0;
                parameterOrdinal < methodParameterCount;
                parameterOrdinal++)
            {
                var parameter
                    = proofFunction.Variables[
                        receiverCount + parameterOrdinal];
                if (parameter.IsMutable || parameter.IsOut || parameter.IsInit
                    || parameter.IsImplicitRef)
                {
                    throw Unsupported(
                        $"Imported generic MethodDef '{proofFunction.Name}' has a non-value parameter mode.");
                }

                var parameterTypeSpecRef
                    = GetExpectedTypeSpecRef(parameter.Type);
                var defaultConstantSourceRow = 0u;
                if (schema.UsesClassParameterDefaults())
                {
                    var defaults
                        = imported.Expected.OrdinaryParameterDefaults;
                    if (defaults.IsDefault
                        || defaults.Length != methodParameterCount)
                    {
                        throw Unsupported(
                            $"CM1 imported MethodDef '{proofFunction.Name}' has no complete default vector.");
                    }

                    var parameterDefault = defaults[parameterOrdinal];
                    if (parameterDefault is not null)
                    {
                        if (parameterDefault.ParameterIndex
                                != parameterOrdinal
                            || parameterDefault.ParameterType
                                != parameter.Type
                            || !StringComparer.Ordinal.Equals(
                                parameterDefault.ParameterName,
                                parameter.Name)
                            || !parameterDefault.FormalDependencies
                                .IsEmpty
                            || parameterDefault.Value
                                is not BoundConstant constant)
                        {
                            throw Unsupported(
                                $"CM1 imported MethodDef '{proofFunction.Name}' has an open or unsupported parameter default.");
                        }

                        defaultConstantSourceRow
                            = AddC1M1ParameterDefault(
                                methodParameterSurfaceValues, constant,
                                parameter.Type, parameterTypeSpecRef)
                                .ConstantSourceRow;
                    }
                }

                AppendUInt32(parameterBytes,
                    checked((uint)(signatureCount + importedOrdinal + 1)));
                AppendUInt32(parameterBytes,
                    checked((uint)parameterOrdinal));
                AppendUInt32(parameterBytes, parameterTypeSpecRef);
                parameterBytes.Add(0);
                parameterBytes.Add(0);
                parameterBytes.Add(0);
                parameterBytes.Add(0);

                if (schema.UsesGeneralMethodParameterLabels())
                {
                    var parameterNameStringRow = names.Count + 1;
                    names.Add((parameter.Name, 1));
                    AppendMethodParameterSurfaceRow(
                        methodParameterLabelBytes,
                        ref methodParameterLabelCount, schema,
                        checked((uint)methodRow),
                        checked((uint)parameterOrdinal),
                        checked((uint)parameterNameStringRow),
                        defaultConstantSourceRow);
                }
            }
            parameterCount = checked(parameterCount
                + methodParameterCount);

            var receiverTypeSpecRef = receiverCount == 0 ? 0
                : GetExpectedTypeSpecRef(proofFunction.Variables[0].Type);
            var resultTypeSpecRef
                = GetExpectedTypeSpecRef(proofFunction.ReturnType);
            AppendUInt32(signatureBytes, resultTypeSpecRef);
            AppendUInt32(signatureBytes, receiverTypeSpecRef);
            AppendUInt32(signatureBytes, checked((uint)firstParameter));
            AppendUInt32(signatureBytes,
                checked((uint)methodParameterCount));
            AppendUInt32(signatureBytes, checked((uint)genericFirst));
            AppendUInt32(signatureBytes,
                checked((uint)expected.Formals.Length));

            var contractFirst = 0;
            var contractCount = 0;
            if (signature.RequiresUnsafeCaller)
            {
                contractFirst = contractRows.Count + 1;
                contractRows.Add(EncodeGenericMethodEffectContract(
                    methodRow, s_effectPayloads[4]));
                contractCount++;
            }
            foreach (var dependencyContract
                in EncodeGenericMethodDependencyContracts(proofFunction,
                    methodRow))
            {
                if (contractFirst == 0)
                {
                    contractFirst = contractRows.Count + 1;
                }
                contractRows.Add(dependencyContract);
                contractCount++;
            }

            AppendUInt32(methodBytes,
                importedOwnerTypeTokens[importedOrdinal]);
            AppendUInt32(methodBytes,
                checked((uint)methodNameStringRow));
            AppendUInt32(methodBytes,
                checked((uint)(signatureCount + importedOrdinal + 1)));
            AppendUInt32(methodBytes,
                2u | (signature.IsMutable ? 4u : 0u));
            AppendUInt32(methodBytes, checked((uint)contractFirst));
            AppendUInt32(methodBytes, checked((uint)contractCount));
            AppendUInt32(methodBytes,
                classOwnerTypeDefRows.TryGetValue(
                    definition.DefinitionOrdinal,
                    out var classOwnerTypeDefRow)
                    ? checked((uint)classOwnerTypeDefRow) : 0);
        }

        nominalTypes.Complete();
        importedNominalTypeReferences
            = nominalTypes.GetDefinitionReferences();
        importedNominalTypeSnapshots = nominalTypes.GetUsedSnapshots();
        typeDefCount = GetCount(sections, 0x0013);
        var finalImportCount = GetCount(sections, 0x001C);
        var finalMethodCount = checked(methodCount + importedMethods.Count);
        var finalSignatureCount
            = checked(signatureCount + importedMethods.Count);
        var finalTypeSpecBytes = TrRmTypeSpecTableWriter.Encode(
            typeSpecRows, typeDefCount, finalImportCount,
            genericParameterRows.Count,
            nativeFunctionPointerSignatureCount, schema,
            finalMethodCount);
        var rewrittenStrings = TrRmStringTableWriter.EncodeTextRows(names);
        var stringTable = new TrRmStringTableReader(rewrittenStrings,
            names.Count);
        var moduleBytes = GetSection(sections, 0x0001).Bytes.ToArray();
        var finalModuleRefCount = GetCount(sections, 0x0010);
        var modules = new TrRmModuleTableReader(moduleBytes, 1,
            stringTable, finalModuleRefCount, typeDefCount,
            finalMethodCount, sourceFileCount, featureCount);
        var moduleRefBytes = GetSection(sections, 0x0010).Bytes.ToArray();
        _ = new TrRmModuleRefTableReader(moduleRefBytes,
            finalModuleRefCount, stringTable, modules);
        var (genericParameterBytes, constraintBytes,
            associatedRelationBytes)
            = CreateGenericMetadataBytes(rewrittenStrings, names.Count,
                moduleBytes, methodBytes.ToArray(),
                signatureBytes.ToArray(), finalTypeSpecBytes,
                typeSpecRows.Count, genericParameterRows, constraintRows,
                associatedRelationRows,
                sections, typeDefCount, finalImportCount,
                nativeFunctionPointerSignatureCount, featureCount,
                finalSignatureCount, parameterCount, contractRows.Count,
                bodyCount, finalModuleRefCount, schema);
        var importBytes = GetSection(sections, 0x001C).Bytes.ToArray();

        SetSection(sections, 0x0002, checked((uint)names.Count),
            rewrittenStrings);
        SetSection(sections, 0x0010,
            checked((uint)finalModuleRefCount), moduleRefBytes);
        SetSection(sections, 0x0011,
            checked((uint)typeSpecRows.Count), finalTypeSpecBytes);
        SetSection(sections, 0x0016, checked((uint)finalMethodCount),
            methodBytes.ToArray());
        SetSection(sections, 0x0017, checked((uint)finalSignatureCount),
            signatureBytes.ToArray());
        SetSection(sections, 0x0018, checked((uint)parameterCount),
            parameterBytes.ToArray());
        SetSection(sections, 0x0019,
            checked((uint)genericParameterRows.Count),
            genericParameterBytes);
        SetSection(sections, 0x001A,
            checked((uint)constraintRows.Count), constraintBytes);
        SetSection(sections, 0x0036,
            checked((uint)associatedRelationRows.Count),
            associatedRelationBytes);
        SetSection(sections, 0x001B, checked((uint)contractRows.Count),
            contractRows.Count == 0 ? []
                : TrRmVariableTableWriter.EncodeBlobs(contractRows));
        SetSection(sections, 0x001C, checked((uint)finalImportCount),
            importBytes);
        SetSection(sections, 0x0066,
            checked((uint)methodParameterLabelCount),
            methodParameterLabelBytes.Count == 0 ? []
                : [.. methodParameterLabelBytes]);

        sections.Sort(static (left, right) =>
            left.Kind.CompareTo(right.Kind));
        return TrRmArtifactWriter.Encode(sections, schema);
    }

    private static byte[] AddGenericMethodMetadata(byte[] artifact,
        BoundProgram program, SourceGenericMethodExports sourceMethodExports,
        IReadOnlyList<GenericMethodEmission> emissions,
        IReadOnlyDictionary<(string QualifiedName, int Arity), uint>
            exportedInterfaceTypeSpecRows,
        IReadOnlyDictionary<(
            string QualifiedName, int Arity, int MemberOrdinal,
            string DeclarationKey), uint> exportedInterfaceMethodDefRows,
        IReadOnlyDictionary<(
            string QualifiedName, int Arity, int MemberOrdinal,
            string DeclarationKey), uint>
            exportedInterfaceAssociatedTypeDefRows,
        IReadOnlyDictionary<GenericImportedInterfaceAssociatedTypeKey,
            uint> importedInterfaceAssociatedTypeImportRows,
        IReadOnlyDictionary<BoundGenericMethodImport, uint>
            importedGenericMethodTargetTokens,
        IReadOnlyDictionary<BoundGenericNominalTypeIdentity, uint>
            importedNominalTypeReferences,
        IReadOnlyList<BoundSourceTypeProviderSnapshot>
            importedNominalTypeSnapshots,
        TrRmArtifactSchema schema,
        TrRmFrozenScalarValueBuilder? methodParameterSurfaceValues,
        SourceClassExports? sourceClassExports)
    {
        var index = new TrRmArtifactIndex(artifact, schema);
        var sections = new List<TrRmSection>(index.SectionCount + 2);
        for (var sectionIndex = 0; sectionIndex < index.SectionCount;
            sectionIndex++)
        {
            index.GetSection(sectionIndex, out var kind, out var flags,
                out var bytes, out var count);
            sections.Add(new TrRmSection(kind, flags,
                checked((uint)count), bytes.ToArray()));
        }

        var stringSection = GetSection(sections, 0x0002);
        var stringRows = new TrRmStringTableReader(stringSection.Bytes.Span,
            checked((int)stringSection.RecordCount));
        var names = new List<(string Text, int Encoding)>(
            stringRows.Count + (emissions.Count * 2));
        for (var row = 1; row <= stringRows.Count; row++)
        {
            names.Add((stringRows.DecodeNamespace(row), 1));
        }

        var methodNameRows = new Dictionary<int, int>();
        var formalNameRows = new Dictionary<(int Definition, int Formal),
            int>();
        foreach (var emission in emissions)
        {
            methodNameRows.Add(emission.Definition.DefinitionOrdinal,
                names.Count + 1);
            names.Add((emission.Definition.Name, 1));
            for (var ordinal = 0;
                ordinal < emission.Definition.Formals.Length; ordinal++)
            {
                formalNameRows.Add((emission.Definition.DefinitionOrdinal,
                    ordinal), names.Count + 1);
                names.Add((emission.Definition.Formals[ordinal].Name, 1));
            }
        }

        var exportNameRows = new List<int>(sourceMethodExports.Exports.Length);
        foreach (var export in sourceMethodExports.Exports)
        {
            exportNameRows.Add(names.Count + 1);
            names.Add((export.ExportKey, 1));
        }
        var exportedDefinitionOrdinals = sourceMethodExports.Exports
            .Select(static export => export.Definition.DefinitionOrdinal)
            .ToHashSet();
        var labelSection = default(TrRmSection);
        var labelBearing = schema.UsesGeneralMethodParameterLabels();
        var hasLabelSection = labelBearing
            && TryGetSection(sections, 0x0066, out labelSection);
        var methodParameterLabelBytes = hasLabelSection
            ? labelSection.Bytes.ToArray().ToList() : [];
        var methodParameterLabelCount = hasLabelSection
            ? checked((int)labelSection.RecordCount) : 0;
        if (methodParameterLabelBytes.Count
            != checked(methodParameterLabelCount
                * GetMethodParameterSurfaceRowSize(schema)))
        {
            throw Invalid(
                "The MethodParameterSurface table has an invalid extent.");
        }

        var parameterSurfaceDefaultOffsets
            = new Dictionary<(int MethodRow, int Ordinal), int>();
        if (labelBearing)
        {
            foreach (var emission in emissions)
            {
                if (!exportedDefinitionOrdinals.Contains(
                        emission.Definition.DefinitionOrdinal))
                {
                    continue;
                }

                var function = emission.ProofFunction;
                var receiverCount = emission.Definition.IsStatic ? 0 : 1;
                var methodParameterCount
                    = function.ParameterCount - receiverCount;
                if (function.Variables.Length < function.ParameterCount)
                {
                    throw Unsupported(
                        $"Generic MethodDef '{emission.Definition.Name}' has no complete checked parameter-name view.");
                }

                for (var ordinal = 0; ordinal < methodParameterCount;
                    ordinal++)
                {
                    var nameRow = names.Count + 1;
                    names.Add((function.Variables[
                        ordinal + receiverCount].Name, 1));
                    var defaultOffset = schema.UsesClassParameterDefaults()
                        ? checked((methodParameterLabelCount
                            * GetMethodParameterSurfaceRowSize(schema)) + 12)
                        : -1;
                    AppendMethodParameterSurfaceRow(
                        methodParameterLabelBytes,
                        ref methodParameterLabelCount, schema,
                        checked((uint)emission.MethodRow),
                        checked((uint)ordinal),
                        checked((uint)nameRow));
                    if (defaultOffset >= 0)
                    {
                        parameterSurfaceDefaultOffsets.Add(
                            (emission.MethodRow, ordinal), defaultOffset);
                    }
                }
            }
        }

        var typeSpecSection = GetSection(sections, 0x0011);
        var typeDefCount = GetCount(sections, 0x0013);
        var importCount = GetCount(sections, 0x001C);
        var nativeFunctionPointerSignatureCount
            = GetCount(sections, 0x0052);
        var typeSpecRowsIndex = new TrRmVariableTableIndex(
            typeSpecSection.Bytes.Span,
            checked((int)typeSpecSection.RecordCount), 2, 1);
        var typeSpecRows = new List<ReadOnlyMemory<byte>>(
            checked(typeSpecRowsIndex.Count
                + emissions.Sum(static emission =>
                    emission.Definition.Formals.Length)));
        for (var row = 1; row <= typeSpecRowsIndex.Count; row++)
        {
            typeSpecRows.Add(typeSpecRowsIndex.GetRow(row).ToArray());
        }
        var importedInterfaceImportRows
            = ReadImportedInterfaceImportRows(sections, stringRows);
        var importedInterfaceMethodImportRows
            = ReadImportedInterfaceMethodImportRows(sections, stringRows,
                allowDuplicateForeignNativeInterfaceViews:
                    foreignNativeInterface);

        var methodSection = TryGetSection(sections, 0x0016,
            out var foundMethodSection) ? foundMethodSection : default;
        var methodBytes = methodSection.Bytes.ToArray().ToList();
        if (schema.UsesMethodReceiverAccess())
        {
            if (methodSection.RecordCount
                < checked((uint)program.Functions.Length))
            {
                throw Invalid(
                    "The G1 base MethodDef range does not cover its source functions.");
            }

            for (var functionIndex = 0;
                functionIndex < program.Functions.Length; functionIndex++)
            {
                var function = program.Functions[functionIndex];
                if (function.DeclaringStruct is null || function.IsStatic
                    || function.IsConstructor || function.IsClassConstructor
                    || function.IsCleanup || function.Variables.IsEmpty
                    || function.Variables[0].IsInit
                    || (!function.Variables[0].IsMutable
                        && !ManagedClassTypes.IsWritable(
                            function.Variables[0].Type)))
                {
                    continue;
                }

                var methodFlags = function.IsPrivate ? 0u
                    : function.IsInternal ? 1u : 2u;
                WriteUInt32(methodBytes,
                    checked((functionIndex * 28) + 12),
                    methodFlags | 4u);
            }
        }

        var signatureSection = TryGetSection(sections, 0x0017,
            out var foundSignatureSection) ? foundSignatureSection : default;
        var signatureBytes = signatureSection.Bytes.ToArray().ToList();
        var parameterSection = TryGetSection(sections, 0x0018,
            out var foundParameterSection)
            ? foundParameterSection : default;
        var parameterBytes = parameterSection.Bytes.ToArray().ToList();
        var bodySection = TryGetSection(sections, 0x0020,
            out var foundBodySection) ? foundBodySection : default;
        var bodyRows = new List<ReadOnlyMemory<byte>>(
            checked((int)bodySection.RecordCount) + emissions.Count);
        if (bodySection.RecordCount != 0)
        {
            var bodyIndex = new TrRmVariableTableIndex(bodySection.Bytes.Span,
                checked((int)bodySection.RecordCount), 1, 1);
            for (var row = 1; row <= bodyIndex.Count; row++)
            {
                bodyRows.Add(bodyIndex.GetRow(row).ToArray());
            }
        }

        var genericParameterRows = ReadGenericParamRows(sections);
        var constraintRows = ReadConstraintRows(sections);
        var associatedRelationRows
            = ReadAssociatedRelationRows(sections);
        var contractRows = ReadVariableRows(sections, 0x001B, 8, 4);
        var nominalTypes = new GenericMethodNominalTypeWriter(sections,
            names, typeSpecRows, genericParameterRows, constraintRows,
            program, emissions, schema);
        nominalTypes.ImportDefinitionReferences(
            importedNominalTypeReferences, importedNominalTypeSnapshots);
        var baseBuiltInTypeSpecRows = GetBuiltInTypeSpecRows(typeSpecRows);
        foreach (var emission in emissions)
        {
            if (emission.Definition.IsStatic)
            {
                continue;
            }

            if (emission.ProofFunction.Variables.IsEmpty
                || !ManagedClassTypes.IsClass(
                    emission.ProofFunction.Variables[0].Type)
                || ManagedClassTypes.IsWritable(
                    emission.ProofFunction.Variables[0].Type)
                || ManagedClassTypes.ClassIndex(
                    emission.ProofFunction.Variables[0].Type)
                    != ManagedClassTypes.ClassIndex(
                        emission.Definition.Owner!.ClosedType))
            {
                throw Unsupported(
                    $"Generic instance MethodDef '{emission.Definition.Name}' has an unsupported receiver TypeSpec.");
            }

            var receiverType = emission.ProofFunction.Variables[0].Type;
            if (!classOwnerTypeDefRows.TryGetValue(
                    emission.Definition.DefinitionOrdinal,
                    out var ownerTypeDefRow))
            {
                throw Unsupported(
                    $"Generic instance MethodDef '{emission.Definition.Name}' has no retained class TypeDef.");
            }

            if (!baseBuiltInTypeSpecRows.ContainsKey(receiverType))
            {
                var receiverTypeSpec = checked((uint)typeSpecRows.Count + 1);
                typeSpecRows.Add(TrRmTypeSpecTableWriter.Reference(0x02,
                    checked((uint)ownerTypeDefRow)));
                baseBuiltInTypeSpecRows.Add(receiverType,
                    receiverTypeSpec);
            }
        }

        var hasBlittableConstraint = false;
        var hasWriteConstraint = constraintRows.Any(
            static constraint => constraint.Kind == 2
                && constraint.Payload == 2);
        var emissionOrdinal = 0;
        foreach (var emission in emissions)
        {
            var builtInTypeSpecRows = new Dictionary<PrimitiveType, uint>(
                baseBuiltInTypeSpecRows);
            var definition = emission.Definition;
            var methodRow = emission.MethodRow;
            var signatureRow = checked((int)signatureSection.RecordCount
                + emissionOrdinal + 1);
            emissionOrdinal++;
            var function = emission.ProofFunction;
            var runtimeParameterCount = function.ParameterCount;
            var receiverCount = definition.IsStatic ? 0 : 1;
            var parameterCount = runtimeParameterCount - receiverCount;
            if (function.IsStatic != definition.IsStatic
                || runtimeParameterCount != definition.RuntimeParameterCount
                || runtimeParameterCount != function.Variables
                    .Take(runtimeParameterCount)
                    .Count(static parameter => !parameter.IsOut
                        && !parameter.IsInit)
                || parameterCount < 0
                || runtimeParameterCount > 8)
            {
                throw Unsupported(
                    $"Generic MethodDef '{definition.Name}' has an unsupported parameter shape.");
            }

            nominalTypes.Prepare(definition, function);
            var firstGenericParam = genericParameterRows.Count + 1;
            var formalTypeSpecRows = new Dictionary<PrimitiveType, uint>();
            var genericParamRows = new int[definition.Formals.Length];
            for (var ordinal = 0; ordinal < definition.Formals.Length;
                ordinal++)
            {
                var formal = definition.Formals[ordinal];
                var genericParamRow = checked(
                    genericParameterRows.Count + ordinal + 1);
                genericParamRows[ordinal] = genericParamRow;
                var typeSpecRow = checked((uint)typeSpecRows.Count + 1);
                typeSpecRows.Add(TrRmTypeSpecTableWriter.Reference(0x04,
                    checked((uint)genericParamRow)));
                if (!formalTypeSpecRows.TryAdd(formal.PlaceholderType,
                    typeSpecRow))
                {
                    throw Unsupported(
                        $"Generic MethodDef '{definition.Name}' has duplicate placeholder type identities.");
                }
            }

            var interfaceConstraintVisits
                = SemanticVerifier.MaximumAnalysisVisits;
            var interfaceConstraintRowRefs = new Dictionary<(
                int FormalOrdinal, int ConstraintOrdinal),
                (uint ConstraintRow, uint TypeSpecRef)>();
            for (var ordinal = 0; ordinal < definition.Formals.Length;
                ordinal++)
            {
                var formal = definition.Formals[ordinal];
                var genericParamRow = genericParamRows[ordinal];
                var constraintFirst = constraintRows.Count + 1;
                if (formal.RequiresWritable)
                {
                    constraintRows.Add(new TrRmConstraintRow(
                        genericParamRow, 2, 2));
                    hasWriteConstraint = true;
                }
                if (formal.RequiresBlittable)
                {
                    constraintRows.Add(new TrRmConstraintRow(
                        genericParamRow, 2, 3));
                    hasBlittableConstraint = true;
                }
                if (formal.InterfaceConstraints.IsDefault)
                {
                    throw Unsupported(
                        $"Generic MethodDef '{definition.Name}' has an invalid interface-constraint view.");
                }
                if (!formal.InterfaceConstraints.IsEmpty)
                {
                    if (!schema.UsesGeneralMethodParameterLabels())
                    {
                        throw Unsupported(
                            "G1 interface constraints require the exact G1L1 schema.");
                    }

                    for (var constraintOrdinal = 0;
                        constraintOrdinal
                            < formal.InterfaceConstraints.Length;
                        constraintOrdinal++)
                    {
                        var interfaceConstraint
                            = formal.InterfaceConstraints[
                                constraintOrdinal];
                        var constraintTypeSpec
                            = GenericMethodInterfaceConstraintTypeSpecRef(
                                interfaceConstraint, definition,
                                exportedInterfaceTypeSpecRows,
                                importedInterfaceImportRows,
                                formalTypeSpecRows, genericParamRows,
                                builtInTypeSpecRows, typeSpecRows, 0,
                                ref interfaceConstraintVisits);
                        var constraintRow
                            = checked((uint)constraintRows.Count + 1);
                        constraintRows.Add(new TrRmConstraintRow(
                            genericParamRow, 1, constraintTypeSpec));
                        if (!interfaceConstraintRowRefs.TryAdd(
                                (ordinal, constraintOrdinal),
                                (constraintRow, constraintTypeSpec)))
                        {
                            throw Invalid(
                                "A G1L1 GenericParam interface Constraint has an ambiguous source identity.");
                        }
                    }
                }
                if (formal.AssociatedRelations.IsDefault)
                {
                    throw Unsupported(
                        $"Generic MethodDef '{definition.Name}' has an invalid associated-relation view.");
                }
                if (!formal.AssociatedRelations.IsEmpty
                    && !schema.UsesAssociatedRelationConstraints())
                {
                    throw Unsupported(
                        "Associated-relation constraints require the exact CM1AR1 schema.");
                }
                foreach (var relation in formal.AssociatedRelations)
                {
                    if (relation.FormalOrdinal != ordinal
                        || !formal.InterfaceConstraints.Any(
                            constraint =>
                                BoundInterfaceContract
                                    .AreEquivalentProviderInterfaceViews(
                                        constraint,
                                        relation.AssociatedType
                                            .DeclaringInterface)))
                    {
                        throw Unsupported(
                            $"Generic MethodDef '{definition.Name}' has an associated relation without its separate matching interface constraint.");
                    }

                    var declaringInterfaceTypeSpec
                        = GenericMethodInterfaceConstraintTypeSpecRef(
                            relation.AssociatedType.DeclaringInterface,
                            definition, exportedInterfaceTypeSpecRows,
                            importedInterfaceImportRows,
                            formalTypeSpecRows, genericParamRows,
                            builtInTypeSpecRows, typeSpecRows, 0,
                            ref interfaceConstraintVisits);
                    var associatedTypeDefReference
                        = GenericMethodAssociatedTypeDefReference(
                            relation,
                            exportedInterfaceAssociatedTypeDefRows,
                            importedInterfaceAssociatedTypeImportRows);
                    if (!BoundGenericMethodTypeTerms
                            .TryGetFormalDependencies(
                                relation.RequiredType,
                                out var requiredTypeFormalDependencies)
                        || !BoundGenericMethodTypeTerms
                            .TryGetConstantFormalDependencies(
                                relation.RequiredType,
                                out var requiredTypeConstantDependencies))
                    {
                        throw Unsupported(
                            $"Generic MethodDef '{definition.Name}' has an invalid associated required type.");
                    }

                    var requiredTypeSpec
                        = GenericMethodTypeDefaultRef(
                            new BoundGenericMethodTypeDefault(
                                relation.RequiredType,
                                requiredTypeFormalDependencies) {
                                ConstantFormalDependencies =
                                    requiredTypeConstantDependencies,
                            }, definition.Formals.Length, definition,
                            formalTypeSpecRows, builtInTypeSpecRows,
                            typeSpecRows, nominalTypes);
                    var associatedRelationRow
                        = checked((uint)associatedRelationRows.Count + 1);
                    associatedRelationRows.Add(
                        new TrRmAssociatedRelationRow(
                            declaringInterfaceTypeSpec,
                            associatedTypeDefReference,
                            requiredTypeSpec));
                    constraintRows.Add(new TrRmConstraintRow(
                        genericParamRow, 3, associatedRelationRow));
                }
                var constraintCount = constraintRows.Count
                    - constraintFirst + 1;
                if (constraintCount == 0)
                {
                    constraintFirst = 0;
                }

                var defaultArgument = formal.TypeDefault is null ? 0u
                    : GenericMethodTypeDefaultRef(formal.TypeDefault,
                        ordinal, definition, formalTypeSpecRows,
                        builtInTypeSpecRows, typeSpecRows, nominalTypes);
                genericParameterRows.Add(new TrRmGenericParamRow(
                    0x1600_0000u | checked((uint)methodRow), ordinal,
                    formalNameRows[(definition.DefinitionOrdinal, ordinal)],
                    constraintFirst, constraintCount, 1,
                    formal.OwnershipDomain, formal.NullabilityDomain,
                    0, defaultArgument));
            }

            var firstParameter = parameterCount == 0
                ? 0 : (parameterBytes.Count / 16) + 1;
            for (var parameter = 0; parameter < parameterCount; parameter++)
            {
                var functionParameterIndex = parameter + receiverCount;
                var typeSpecRef = GenericMethodTypeSpecRef(
                    function.Variables[functionParameterIndex].Type,
                    formalTypeSpecRows, builtInTypeSpecRows, typeSpecRows,
                    definition.ProofProgram.SpanTypes,
                    definition.ProofProgram.CheckedReferenceTypes,
                    definition.DefinitionOrdinal, definition,
                    allowSpanType: true, nominalTypes: nominalTypes);
                if (schema.UsesClassParameterDefaults()
                    && exportedDefinitionOrdinals.Contains(
                        definition.DefinitionOrdinal))
                {
                    var defaults = definition.OrdinaryParameterDefaults;
                    if (defaults.IsDefault
                        || defaults.Length != parameterCount)
                    {
                        throw Unsupported(
                            $"CM1 generic MethodDef '{definition.Name}' has no complete checked default vector.");
                    }

                    var parameterDefault = defaults[parameter];
                    if (parameterDefault is not null)
                    {
                        if (parameterDefault.ParameterIndex != parameter
                            || parameterDefault.ParameterType
                                != function.Variables[
                                    functionParameterIndex].Type
                            || !StringComparer.Ordinal.Equals(
                                parameterDefault.ParameterName,
                                function.Variables[
                                    functionParameterIndex].Name)
                            || !parameterDefault.FormalDependencies
                                .IsEmpty
                            || parameterDefault.Value
                                is not BoundConstant constant
                            || !parameterSurfaceDefaultOffsets.TryGetValue(
                                (methodRow, parameter),
                                out var defaultOffset))
                        {
                            throw Unsupported(
                                $"CM1 generic MethodDef '{definition.Name}' has an open or unsupported parameter default.");
                        }

                        var frozenValue
                            = AddC1M1ParameterDefault(
                                methodParameterSurfaceValues, constant,
                                function.Variables[
                                    functionParameterIndex].Type,
                                typeSpecRef);
                        WriteUInt32(methodParameterLabelBytes,
                            defaultOffset,
                            frozenValue.ConstantSourceRow);
                    }
                }

                AppendUInt32(parameterBytes, checked((uint)signatureRow));
                AppendUInt32(parameterBytes, checked((uint)parameter));
                AppendUInt32(parameterBytes, typeSpecRef);
                parameterBytes.Add(0);
                parameterBytes.Add(0);
                parameterBytes.Add(0);
                parameterBytes.Add(0);
            }

            var resultTypeSpecRef = GenericMethodTypeSpecRef(
                function.ReturnType, formalTypeSpecRows,
                builtInTypeSpecRows, typeSpecRows,
                definition.ProofProgram.SpanTypes,
                definition.ProofProgram.CheckedReferenceTypes,
                definition.DefinitionOrdinal, definition,
                allowSpanType: true, nominalTypes: nominalTypes);
            AppendUInt32(signatureBytes, resultTypeSpecRef);
            var receiverTypeSpecRef = receiverCount == 0 ? 0
                : GenericMethodTypeSpecRef(
                    function.Variables[0].Type, formalTypeSpecRows,
                    builtInTypeSpecRows, typeSpecRows,
                    definition.ProofProgram.SpanTypes,
                    definition.ProofProgram.CheckedReferenceTypes,
                    definition.DefinitionOrdinal, definition,
                    allowSpanType: true, nominalTypes: nominalTypes);
            AppendUInt32(signatureBytes, receiverTypeSpecRef);
            AppendUInt32(signatureBytes, checked((uint)firstParameter));
            AppendUInt32(signatureBytes, checked((uint)parameterCount));
            AppendUInt32(signatureBytes,
                checked((uint)firstGenericParam));
            AppendUInt32(signatureBytes,
                checked((uint)definition.Formals.Length));

            var (hasFloatingTypes, _, _) = AnalyzeFunction(function);
            EnsureGenericBodyTypeMap(hasFloatingTypes,
                HasNativeWidthScalar(function),
                typeSpecRows);
            var containsGenericMethodCall
                = ContainsGenericMethodCall(function);
            var containsInterfaceCall = ContainsInterfaceCall(function);
            if (containsGenericMethodCall && containsInterfaceCall)
            {
                throw Unsupported(
                    $"Generic MethodDef '{definition.Name}' combines unsupported GenericCall target roles.");
            }

            bodyRows.Add(containsGenericMethodCall
                ? EncodeGenericMethodForwardingBody(function,
                    definition, emissions, formalTypeSpecRows,
                    builtInTypeSpecRows, typeSpecRows,
                    definition.ProofProgram.SpanTypes,
                    importedGenericMethodTargetTokens, schema,
                    nominalTypes)
                : containsInterfaceCall
                    ? EncodeGenericMethodRequirementBody(function,
                        definition, program,
                        definition.ProofProgram.SpanTypes,
                        exportedInterfaceTypeSpecRows,
                        exportedInterfaceMethodDefRows,
                        importedInterfaceMethodImportRows,
                        formalTypeSpecRows, builtInTypeSpecRows,
                        typeSpecRows, interfaceConstraintRowRefs, schema,
                        nominalTypes)
                : IsDirectGenericMethodMoveReturn(function)
                    ? EncodeGenericMethodMoveReturnBody(function,
                        definition, formalTypeSpecRows,
                        builtInTypeSpecRows, typeSpecRows,
                        definition.ProofProgram.SpanTypes, schema,
                        nominalTypes)
                    : EncodeBody(function, hasFloatingTypes, 0,
                        new Dictionary<PrimitiveType, int>(),
                        new Dictionary<PrimitiveType, int>()));

            var contractFirst = 0;
            var contractCount = 0;
            var effectMask = (function.DoesNotReturn ? 2u : 0u)
                | (definition.RequiresUnsafeCaller ? 4u : 0u);
            if (effectMask != 0)
            {
                contractFirst = contractRows.Count + 1;
                contractRows.Add(EncodeGenericMethodEffectContract(
                    methodRow, s_effectPayloads[
                        checked((int)effectMask)]));
                contractCount++;
            }
            foreach (var dependencyContract
                in EncodeGenericMethodDependencyContracts(function,
                    methodRow))
            {
                if (contractFirst == 0)
                {
                    contractFirst = contractRows.Count + 1;
                }
                contractRows.Add(dependencyContract);
                contractCount++;
            }

            AppendUInt32(methodBytes, 0);
            AppendUInt32(methodBytes, checked((uint)methodNameRows[
                definition.DefinitionOrdinal]));
            AppendUInt32(methodBytes, checked((uint)signatureRow));
            AppendUInt32(methodBytes, (definition.IsPublic ? 2u : 0u)
                | (schema.UsesMethodReceiverAccess()
                    && definition.IsMutable ? 4u : 0u));
            AppendUInt32(methodBytes, checked((uint)contractFirst));
            AppendUInt32(methodBytes, checked((uint)contractCount));
            AppendUInt32(methodBytes,
                checked((uint)bodyRows.Count));
        }

        nominalTypes.Complete();
        typeDefCount = GetCount(sections, 0x0013);
        importCount = GetCount(sections, 0x001C);
        hasWriteConstraint |= constraintRows.Any(
            static constraint => constraint.Kind == 2
                && constraint.Payload == 2);
        hasBlittableConstraint |= constraintRows.Any(
            static constraint => constraint.Kind == 2
                && constraint.Payload == 3);
        var rewrittenStrings = TrRmStringTableWriter.EncodeTextRows(names);
        var removedClosedExports = new HashSet<uint>();
        for (var functionIndex = 0;
            functionIndex < program.Functions.Length; functionIndex++)
        {
            if (program.Functions[functionIndex]
                .GenericMethodInstantiation is not null)
            {
                _ = removedClosedExports.Add(0x1600_0000u
                    | checked((uint)(functionIndex + 1)));
                WriteUInt32(methodBytes,
                    checked((functionIndex * 28) + 12), 0);
            }
        }

        var exportRows = ReadExportRows(sections, removedClosedExports);
        exportRows.AddRange(nominalTypes.Exports);
        for (var exportIndex = 0;
            exportIndex < sourceMethodExports.Exports.Length; exportIndex++)
        {
            var export = sourceMethodExports.Exports[exportIndex];
            var emission = emissions.First(candidate =>
                candidate.Definition.DefinitionOrdinal
                    == export.Definition.DefinitionOrdinal);
            exportRows.Add(new TrRmExportRow(
                0x1600_0000u | checked((uint)emission.MethodRow),
                exportNameRows[exportIndex]));
        }
        var exportBytes = EncodeExportRows(exportRows);

        var featureRows = ReadFeatureRows(sections);
        var hasGenericMethodFeature = featureRows.Any(static feature =>
            feature.FeatureId
                == TrRmFeatureTableReader.GenericMethodCallsFeatureId);
        if (featureRows.Any(static feature =>
                feature.FeatureId
                    == TrRmFeatureTableReader.GenericWriteRequirementsFeatureId)
            || (hasGenericMethodFeature
                && !schema.UsesAssociatedRelationConstraints()))
        {
            throw Unsupported("The source emitter cannot replace pre-existing G1 Features.");
        }
        if (!schema.UsesClassParameterDefaults()
            && featureRows.Any(static feature =>
                feature.FeatureId
                    == TrRmFeatureTableReader.MethodParameterLabelsFeatureId))
        {
            throw Unsupported(
                "The source emitter cannot replace pre-existing G1 label Features.");
        }
        if (hasWriteConstraint && !schema.UsesGenericWriteRequirements())
        {
            throw Unsupported(
                "The selected TRRM schema does not admit generic Write requirements.");
        }
        var hasGenericCallTargets = bodyRows.Any(body =>
            HasGenericCallTargets(body, contractRows.Count, schema));
        if (emissions.Count != 0 || hasGenericCallTargets
            || GetCount(sections, 0x0027) != 0
            || associatedRelationRows.Count != 0)
        {
            if (!hasGenericMethodFeature)
            {
                featureRows.Add(
                    TrRmFeatureRow.GenericMethodCallsCandidate);
            }
        }
        if (hasWriteConstraint
            && !featureRows.Any(static feature =>
                feature.FeatureId
                    == TrRmFeatureTableReader.GenericWriteRequirementsFeatureId))
        {
            featureRows.Add(
                TrRmFeatureRow.GenericWriteRequirementsCandidate);
        }
        if (labelBearing && (exportedDefinitionOrdinals.Count != 0
                || GetCount(sections, 0x0024) != 0
                || importedGenericMethodTargetTokens.Count != 0)
            && !featureRows.Any(static feature =>
                feature.FeatureId
                    == TrRmFeatureTableReader.MethodParameterLabelsFeatureId))
        {
            featureRows.Add(TrRmFeatureRow.MethodParameterLabelsCandidate);
        }
        if (methodParameterSurfaceValues is not null
            && methodParameterSurfaceValues.ConstantSourceCount > 0)
        {
            featureRows.Add(TrRmFeatureRow.TypedFrozenValues);
        }
        if (hasBlittableConstraint
            && !featureRows.Any(static feature =>
                feature.FeatureId
                    == TrRmFeatureTableReader.LayoutQueriesFeatureId))
        {
            featureRows.Add(TrRmFeatureRow.LayoutQueries);
        }
        nominalTypes.MergeFeatures(featureRows);
        var featureBytes = TrRmFeatureTableWriter.Encode(featureRows, schema);
        var featureCount = featureBytes.Length / 8;
        var moduleSection = GetSection(sections, 0x0001);
        var moduleBytes = moduleSection.Bytes.ToArray();
        if (moduleSection.RecordCount != 1)
        {
            throw Unsupported(
                "G1 source method emission requires one output Module.");
        }
        var oldFeatureCount = ReadInt(moduleBytes, 28);
        if (featureCount == 0)
        {
            WriteUInt32(moduleBytes, 24, 0);
        }
        else if (oldFeatureCount == 0)
        {
            WriteUInt32(moduleBytes, 24, 1);
        }
        WriteUInt32(moduleBytes, 28, checked((uint)featureCount));
        SetSection(sections, 0x0004, checked((uint)featureCount),
            featureBytes);

        var typeSpecBytes = TrRmTypeSpecTableWriter.Encode(typeSpecRows,
            typeDefCount, importCount, genericParameterRows.Count,
            nativeFunctionPointerSignatureCount,
            schema,
            checked((int)methodSection.RecordCount + emissions.Count));
        var finalMethodBytes = methodBytes.ToArray();
        var finalSignatureBytes = signatureBytes.ToArray();
        var finalParameterBytes = parameterBytes.ToArray();
        var (genericParameterBytes, constraintBytes,
            associatedRelationBytes) =
            CreateGenericMetadataBytes(
            rewrittenStrings, names.Count, moduleBytes, finalMethodBytes,
            finalSignatureBytes, typeSpecBytes,
            checked((int)typeSpecRows.Count),
            genericParameterRows, constraintRows, associatedRelationRows,
            sections, typeDefCount, importCount,
            nativeFunctionPointerSignatureCount, featureCount,
            checked((int)signatureSection.RecordCount
                + emissions.Count),
            finalParameterBytes.Length / 16, contractRows.Count,
            bodyRows.Count, GetCount(sections, 0x0010), schema);

        SetSection(sections, 0x0001, 1, moduleBytes);
        SetSection(sections, 0x0002, checked((uint)names.Count),
            rewrittenStrings);
        SetSection(sections, 0x0004, checked((uint)featureCount),
            featureBytes);
        SetSection(sections, 0x0011,
            checked((uint)typeSpecRows.Count), typeSpecBytes);
        SetSection(sections, 0x0016,
            checked(methodSection.RecordCount + (uint)emissions.Count),
            finalMethodBytes);
        SetSection(sections, 0x0017,
            checked(signatureSection.RecordCount
                + (uint)emissions.Count), finalSignatureBytes);
        SetSection(sections, 0x0018,
            checked((uint)(finalParameterBytes.Length / 16)),
            finalParameterBytes);
        SetSection(sections, 0x0019,
            checked((uint)genericParameterRows.Count),
            genericParameterBytes);
        SetSection(sections, 0x001A,
            checked((uint)constraintRows.Count), constraintBytes);
        SetSection(sections, 0x0036,
            checked((uint)associatedRelationRows.Count),
            associatedRelationBytes);
        SetSection(sections, 0x001B,
            checked((uint)contractRows.Count),
            contractRows.Count == 0 ? []
                : TrRmVariableTableWriter.EncodeBlobs(contractRows));
        SetSection(sections, 0x0020, checked((uint)bodyRows.Count),
            bodyRows.Count == 0 ? []
                : TrRmVariableTableWriter.EncodeBodies(bodyRows));
        SetSection(sections, 0x001D, checked((uint)exportRows.Count),
            exportBytes);
        SetSection(sections, 0x0066,
            checked((uint)methodParameterLabelCount),
            methodParameterLabelBytes.Count == 0 ? []
                : [.. methodParameterLabelBytes]);

        sections.Sort(static (left, right) =>
            left.Kind.CompareTo(right.Kind));
        return TrRmArtifactWriter.Encode(sections,
            schema);
    }

    private static byte[] AddGenericMethodInterfaceMetadata(byte[] artifact,
        BoundProgram program, IReadOnlyList<TrRmSourceTypeExport> typeExports,
        SourceGenericMethodExports sourceMethodExports,
        TrRmArtifactSchema schema,
        TrRmFrozenScalarValueBuilder? methodParameterSurfaceValues,
        out Dictionary<(string QualifiedName, int Arity), uint>
            exportedInterfaceTypeSpecRows,
        out Dictionary<(
            string QualifiedName, int Arity, int MemberOrdinal,
            string DeclarationKey), uint> exportedInterfaceMethodDefRows,
        out Dictionary<(
            string QualifiedName, int Arity, int MemberOrdinal,
            string DeclarationKey), uint>
            exportedInterfaceAssociatedTypeDefRows,
        ImmutableArray<int> foreignNativeInterfaceStructureIndexes =
            default)
    {
        exportedInterfaceTypeSpecRows = [];
        exportedInterfaceMethodDefRows = [];
        exportedInterfaceAssociatedTypeDefRows = [];
        var artifactIndex = new TrRmArtifactIndex(artifact, schema);
        var sections = new List<TrRmSection>(artifactIndex.SectionCount);
        for (var sectionIndex = 0;
            sectionIndex < artifactIndex.SectionCount;
            sectionIndex++)
        {
            artifactIndex.GetSection(sectionIndex, out var kind,
                out var flags, out var bytes, out var count);
            sections.Add(new TrRmSection(kind, flags,
                checked((uint)count), bytes.ToArray()));
        }

        if (GetCount(sections, 0x0024) != 0
            || GetCount(sections, 0x0025) != 0
            || GetCount(sections, 0x0026) != 0
            || GetCount(sections, 0x0027) != 0
            || GetCount(sections, 0x0028) != 0
            || GetCount(sections, 0x0029) != 0)
        {
            throw Unsupported(
                "The G1 source emitter cannot merge pre-existing interface or witness rows.");
        }

        var emissions = CaptureGenericInterfaceEmissions(program,
            typeExports, schema);
        if (emissions.Count == 0)
        {
            return artifact;
        }

        var stringSection = GetSection(sections, 0x0002);
        var strings = new TrRmStringTableReader(stringSection.Bytes.Span,
            checked((int)stringSection.RecordCount));
        var names = new List<(string Text, int Encoding)>(strings.Count
            + (emissions.Count * 2));
        for (var row = 1; row <= strings.Count; row++)
        {
            names.Add((strings.DecodeNamespace(row), 1));
        }

        var exportRows = ReadExportRows(sections, []);
        var exportKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var export in exportRows)
        {
            if (!exportKeys.Add(strings.DecodeName(
                    export.ExportKeyStringRow)))
            {
                throw new InvalidDataException(
                    "The base TRRM artifact contains duplicate ExportKeys.");
            }
        }
        foreach (var export in sourceMethodExports.Exports)
        {
            if (!exportKeys.Add(export.ExportKey))
            {
                throw new ArgumentException(
                    "G1 interface ExportKeys must be unique within the Module.",
                    nameof(typeExports));
            }
        }
        foreach (var emission in emissions)
        {
            if (!exportKeys.Add(emission.Export.ExportKey))
            {
                throw new ArgumentException(
                    "G1 interface ExportKeys must be unique within the Module.",
                    nameof(typeExports));
            }
            foreach (var memberExport in
                emission.Export.InterfaceMethodExports)
            {
                if (!exportKeys.Add(memberExport.ExportKey))
                {
                    throw new ArgumentException(
                        "G1 interface requirement ExportKeys must be unique within the Module.",
                        nameof(typeExports));
                }
            }
            if (!emission.Export.InterfaceAssociatedTypeExports
                    .IsDefaultOrEmpty)
            {
                foreach (var memberExport in
                    emission.Export.InterfaceAssociatedTypeExports)
                {
                    if (!exportKeys.Add(memberExport.ExportKey))
                    {
                        throw new ArgumentException(
                            "G1 associated-type ExportKeys must be unique within the Module.",
                            nameof(typeExports));
                    }
                }
            }
        }

        var typeDefCount = GetCount(sections, 0x0013);
        var interfaceDefCount = GetCount(sections, 0x0025);
        var interfaceMethodCount = GetCount(sections, 0x0024);
        var associatedTypeDefCount = GetCount(sections, 0x0027);
        var methodCount = GetCount(sections, 0x0016);
        var signatureCount = GetCount(sections, 0x0017);
        var parameterCount = GetCount(sections, 0x0018);
        var layoutCount = GetCount(sections, 0x0015);
        var fieldCount = GetCount(sections, 0x0014);
        var contractCount = GetCount(sections, 0x001B);
        var importCount = GetCount(sections, 0x001C);
        var moduleRefCount = GetCount(sections, 0x0010);
        var sourceFileCount = GetCount(sections, 0x001F);
        var bodyCount = GetCount(sections, 0x0020);
        var nativeFunctionPointerSignatureCount
            = GetCount(sections, 0x0052);
        var constantSourceCount = GetCount(sections, 0x0030);

        var typeDefBytes = TryGetSection(sections, 0x0013,
            out var typeDefSection)
            ? typeDefSection.Bytes.ToArray().ToList() : [];
        var layoutBytes = TryGetSection(sections, 0x0015,
            out var layoutSection)
            ? layoutSection.Bytes.ToArray().ToList() : [];
        var methodBytes = TryGetSection(sections, 0x0016,
            out var methodSection)
            ? methodSection.Bytes.ToArray().ToList() : [];
        var signatureBytes = TryGetSection(sections, 0x0017,
            out var signatureSection)
            ? signatureSection.Bytes.ToArray().ToList() : [];
        var parameterBytes = TryGetSection(sections, 0x0018,
            out var parameterSection)
            ? parameterSection.Bytes.ToArray().ToList() : [];

        var typeSpecRows = ReadVariableRows(sections, 0x0011, 2, 1);
        var builtInTypeSpecRows = GetBuiltInTypeSpecRows(typeSpecRows);
        var genericParameterRows = ReadGenericParamRows(sections);
        var constraintRows = ReadConstraintRows(sections);
        var interfaceDefRows = new List<TrRmInterfaceDefRow>(
            emissions.Count);
        var interfaceMethodRows = new List<TrRmInterfaceMethodRow>();
        var foreignNativeInterface = schema.UsesForeignNativeInterface();
        var nativeRawPointerTypeRow = 0;
        var nativeFunctionPointerTypeRows
            = new Dictionary<PrimitiveType, int>();
        var nativePointerTypeRows = new Dictionary<PrimitiveType, int>();
        var structureTypeDefRows = new Dictionary<int, int>();
        var typeDefTypeSpecRows = new Dictionary<int, int>();
        if (foreignNativeInterface && typeDefCount != 0)
        {
            MapForeignNativeInterfaceStructureTypeDefRows(program,
                foreignNativeInterfaceStructureIndexes, strings,
                GetSection(sections, 0x0001).Bytes.Span,
                moduleRefCount, typeDefBytes.ToArray(), typeDefCount,
                methodCount, sourceFileCount, fieldCount,
                genericParameterRows.Count, layoutCount, contractCount,
                GetCount(sections, 0x0004), schema,
                structureTypeDefRows);
        }
        var foreignFunctionPointerSignatureIndexes
            = foreignNativeInterface
                ? GetForeignNativeFunctionPointerSignatureIndexes(
                    emissions.Select(static emission =>
                        emission.Definition), program,
                    foreignNativeInterfaceStructureIndexes)
                : ImmutableArray<int>.Empty;
        var foreignFunctionPointerAnchorMethodRows
            = new Dictionary<int, int>(
                foreignFunctionPointerSignatureIndexes.Length);
        var nextForeignFunctionPointerAnchorMethodRow = checked(methodCount
            + emissions.Sum(static emission =>
                emission.Definition.Members.Length) + 1);
        foreach (var signatureIndex
            in foreignFunctionPointerSignatureIndexes)
        {
            foreignFunctionPointerAnchorMethodRows.Add(signatureIndex,
                nextForeignFunctionPointerAnchorMethodRow++);
        }
        var foreignReceiverTypeSpecRows = new Dictionary<bool, uint>();
        if (foreignNativeInterface)
        {
            nativeRawPointerTypeRow = typeSpecRows.Count + 1;
            typeSpecRows.Add(TrRmTypeSpecTableWriter.InlineFamily(0x8C, 0));
            foreignReceiverTypeSpecRows.Add(false,
                checked((uint)nativeRawPointerTypeRow));
        }

        uint GetForeignNativeInterfaceTypeSpecRef(BoundInterfaceType type)
        {
            if (type.Kind != BoundInterfaceTypeKind.Primitive
                || type.IsOptional || type.IsGarbageCollected
                || type.IsInitialization || type.IsGenericNominalConstantDomain
                || type.GenericNominalTypeIdentity is not null
                || type.SourceGenericTypeDefinitionIndex != -1)
            {
                throw Unsupported(
                    "F+I MethodSig types must be complete, closed native ABI values.");
            }

            if (type.PrimitiveType == PrimitiveType.Void)
            {
                return GetBuiltInTypeSpecRef(type.PrimitiveType,
                    builtInTypeSpecRows, typeSpecRows);
            }

            var row = NativeValueTypeRow(type.PrimitiveType,
                nativeRawPointerTypeRow, nativeFunctionPointerTypeRows,
                typeSpecRows, nativePointerTypeRows,
                program.NativePointerTypes, structureTypeDefRows,
                typeDefTypeSpecRows,
                primitive => checked((int)GetBuiltInTypeSpecRef(primitive,
                    builtInTypeSpecRows, typeSpecRows)));
            return checked((uint)row);
        }

        foreach (var signatureIndex
            in foreignFunctionPointerSignatureIndexes)
        {
            var signature = program.NativeFunctionPointerSignatures[
                signatureIndex];
            if (signature.CallingConvention is not (
                    NativeCallingConvention.Unmanaged
                    or NativeCallingConvention.Cdecl
                    or NativeCallingConvention.Stdcall
                    or NativeCallingConvention.Vectorcall))
            {
                throw Unsupported(
                    "Standard FNI function-pointer anchors require an explicit registered convention.");
            }

            var typeSpecRow = checked((uint)typeSpecRows.Count + 1);
            typeSpecRows.Add(TrRmTypeSpecTableWriter.NativeFunctionPointer(
                (byte)signature.CallingConvention,
                checked((uint)foreignFunctionPointerAnchorMethodRows[
                    signatureIndex])));
            nativeFunctionPointerTypeRows.Add(
                NativeFunctionPointerTypes.Type(signatureIndex),
                checked((int)typeSpecRow));
        }

        uint GetForeignNativeInterfaceReceiverTypeSpecRef(
            bool requiresMutableReceiver)
        {
            if (foreignReceiverTypeSpecRows.TryGetValue(
                    requiresMutableReceiver, out var existingRow))
            {
                return existingRow;
            }

            var row = checked((uint)typeSpecRows.Count + 1);
            typeSpecRows.Add(TrRmTypeSpecTableWriter.InlineFamily(0x8C, 0,
                requiresMutableReceiver ? (byte)1 : (byte)0));
            foreignReceiverTypeSpecRows.Add(requiresMutableReceiver, row);
            return row;
        }

        var associatedTypeDefRows
            = new List<TrRmAssociatedTypeDefRow>();
        var foreignInterfaceContractRows = new List<TrRmContractRow>();
        var foreignInterfacePredicateGraphs
            = new List<TrRmPredicateGraphRow>();
        var foreignInterfacePredicateFootprints
            = new List<TrRmPredicateReadFootprintRow>();
        var foreignInterfacePredicateRoots
            = new List<TrRmPredicateReadRootRow>();
        var foreignInterfacePredicateNodes
            = new List<TrRmPredicateNodeRow>();
        var foreignInterfacePredicateOperands
            = new List<TrRmPredicateNodeOperandRow>();
        var methodParameterLabelCount = GetCount(sections, 0x0066);
        var methodParameterLabelBytes
            = TryGetSection(sections, 0x0066, out var labelSection)
                ? labelSection.Bytes.ToArray().ToList() : [];
        if (methodParameterLabelBytes.Count
            != checked(methodParameterLabelCount
                * GetMethodParameterSurfaceRowSize(schema)))
        {
            throw Invalid(
                "The MethodParameterSurface table has an invalid extent.");
        }

        var interfaceTypeExportNameRows = new List<int>(
            emissions.Count);
        var interfaceMethodExportNameRows = new List<int>();
        var associatedTypeExportNameRows = new List<int>();
        var typeDefRowBase = typeDefCount;
        var methodRowBase = methodCount;
        var signatureRowBase = signatureCount;
        var parameterRowBase = parameterCount;
        var layoutRowBase = layoutCount;
        var interfaceDefRowBase = interfaceDefCount;
        var interfaceMethodRowBase = interfaceMethodCount;
        var emittedMethods = 0;
        var emittedSignatures = 0;
        var emittedParameters = 0;

        foreach (var emission in emissions)
        {
            var definition = emission.Definition;
            var export = emission.Export;
            var typeDefRow = checked(typeDefRowBase
                + interfaceDefRows.Count + 1);
            var interfaceDefRow = checked(interfaceDefRowBase
                + interfaceDefRows.Count + 1);
            var namespaceStringRow = names.Count + 1;
            var qualifiedNameSeparator
                = definition.QualifiedName.LastIndexOf('.',
                    StringComparison.Ordinal);
            names.Add((qualifiedNameSeparator < 0 ? ""
                : definition.QualifiedName[..qualifiedNameSeparator], 1));
            var nameStringRow = names.Count + 1;
            names.Add((qualifiedNameSeparator < 0
                ? definition.QualifiedName
                : definition.QualifiedName[(qualifiedNameSeparator + 1)..],
                1));
            interfaceTypeExportNameRows.Add(names.Count + 1);
            names.Add((export.ExportKey, 1));

            var genericFirst = definition.Formals.IsEmpty
                ? 0 : checked(genericParameterRows.Count + 1);
            var constraintsByFormal
                = new List<BoundInterfaceConstraint>?[
                    definition.Formals.Length];
            foreach (var constraint in definition.Constraints)
            {
                var formalConstraints
                    = constraintsByFormal[constraint.FormalOrdinal] ??= [];
                formalConstraints.Add(constraint);
            }
            var interfaceTypeSpecRow = checked(
                (uint)typeSpecRows.Count + 1);
            typeSpecRows.Add(TrRmTypeSpecTableWriter.Reference(0x02,
                checked((uint)typeDefRow)));
            if (!exportedInterfaceTypeSpecRows.TryAdd(
                    (definition.QualifiedName, definition.Formals.Length),
                    interfaceTypeSpecRow))
            {
                throw Invalid(
                    "A G1 source interface has an ambiguous TypeSpec identity.");
            }
            var formalTypeSpecRows = new Dictionary<int, uint>();
            var formalTypeSpecArguments = new uint[
                definition.Formals.Length];
            var selfFormalRow = 0;
            for (var ordinal = 0; ordinal < definition.Formals.Length;
                ordinal++)
            {
                var formal = definition.Formals[ordinal];
                var formalRow = checked(genericParameterRows.Count + 1);
                var formalNameStringRow = names.Count + 1;
                names.Add((formal.Name, 1));
                var formalConstraints = constraintsByFormal[ordinal];
                var constraintFirst = formalConstraints is null ? 0
                    : checked(constraintRows.Count + 1);
                if (formalConstraints is not null)
                {
                    foreach (var constraint in formalConstraints)
                    {
                        if (!IsGenericWriteConstraint(constraint))
                        {
                            throw Unsupported(
                                $"Interface '{definition.QualifiedName}' has an unsupported generic constraint.");
                        }

                        constraintRows.Add(new TrRmConstraintRow(
                            formalRow, 2, 2));
                    }
                }
                genericParameterRows.Add(new TrRmGenericParamRow(
                    0x1300_0000u | checked((uint)typeDefRow),
                    ordinal, formalNameStringRow, constraintFirst,
                    formalConstraints?.Count ?? 0, 1, 1, 1, 0, 0));
                var formalTypeSpecRow = checked(
                    (uint)typeSpecRows.Count + 1);
                typeSpecRows.Add(TrRmTypeSpecTableWriter.Reference(0x04,
                    checked((uint)formalRow)));
                formalTypeSpecRows.Add(ordinal, formalTypeSpecRow);
                formalTypeSpecArguments[ordinal] = formalTypeSpecRow;
                if (formal.IsSelf)
                {
                    if (selfFormalRow != 0)
                    {
                        throw Unsupported(
                            $"Interface '{definition.QualifiedName}' declares multiple self formals.");
                    }
                    selfFormalRow = formalRow;
                }
            }

            var receiverTypeSpecRef = foreignNativeInterface
                ? 0u : interfaceTypeSpecRow;
            if (!foreignNativeInterface
                && formalTypeSpecArguments.Length != 0)
            {
                receiverTypeSpecRef = checked(
                    (uint)typeSpecRows.Count + 1);
                typeSpecRows.Add(TrRmTypeSpecTableWriter.Constructed(
                    interfaceTypeSpecRow, formalTypeSpecArguments));
            }

            var methodMembers = definition.Members
                .Where(static member =>
                    member.Kind == BoundInterfaceMemberKind.Callable)
                .ToArray();
            var associatedTypeMembers = definition.Members
                .Where(static member =>
                    member.Kind == BoundInterfaceMemberKind.AssociatedType)
                .ToArray();
            var methodFirst = methodMembers.Length == 0 ? 0
                : checked(interfaceMethodRowBase
                    + interfaceMethodRows.Count + 1);
            var associatedTypeFirst = associatedTypeMembers.Length == 0 ? 0
                : checked(associatedTypeDefCount
                    + associatedTypeDefRows.Count + 1);
            for (var associatedOrdinal = 0;
                associatedOrdinal < associatedTypeMembers.Length;
                associatedOrdinal++)
            {
                var member = associatedTypeMembers[associatedOrdinal];
                var mapping = export.InterfaceAssociatedTypeExports[
                    associatedOrdinal];
                var associatedTypeDefRow = checked(
                    associatedTypeDefCount
                    + associatedTypeDefRows.Count + 1);
                var methodNameStringRow = names.Count + 1;
                names.Add((member.Name, 1));
                associatedTypeExportNameRows.Add(names.Count + 1);
                names.Add((mapping.ExportKey, 1));
                var defaultTypeSpecRef
                    = member.AssociatedDefaultType is null ? 0
                        : GenericInterfaceTypeSpecRef(
                            member.AssociatedDefaultType,
                            formalTypeSpecRows, builtInTypeSpecRows,
                            typeSpecRows);
                associatedTypeDefRows.Add(new TrRmAssociatedTypeDefRow(
                    interfaceDefRow, methodNameStringRow,
                    defaultTypeSpecRef));
                if (!exportedInterfaceAssociatedTypeDefRows.TryAdd(
                        (definition.QualifiedName,
                            definition.Formals.Length, member.Ordinal,
                            member.DeclarationKey),
                        checked((uint)associatedTypeDefRow)))
                {
                    throw Invalid(
                        "A G1L1 AssociatedTypeDef has an ambiguous source identity.");
                }
            }

            var methodExportOrdinal = 0;
            var interfaceMethodOrdinal = 0;
            foreach (var member in methodMembers)
            {
                var mapping = export.InterfaceMethodExports[
                    methodExportOrdinal++];
                var methodNameStringRow = names.Count + 1;
                names.Add((member.Name, 1));
                interfaceMethodExportNameRows.Add(names.Count + 1);
                names.Add((mapping.ExportKey, 1));
                var signatureRow = checked(signatureRowBase
                    + emittedSignatures + 1);
                var firstParameter = member.Parameters.IsEmpty ? 0
                    : checked(parameterRowBase + emittedParameters + 1);
                var methodRow = checked(methodRowBase + emittedMethods + 1);
                if (!exportedInterfaceMethodDefRows.TryAdd(
                        (definition.QualifiedName,
                            definition.Formals.Length, member.Ordinal,
                            member.DeclarationKey),
                        checked((uint)methodRow)))
                {
                    throw Invalid(
                        "A G1L1 InterfaceMethod has an ambiguous source identity.");
                }
                for (var parameterOrdinal = 0;
                    parameterOrdinal < member.Parameters.Length;
                    parameterOrdinal++)
                {
                    var parameter = member.Parameters[parameterOrdinal];
                    var parameterTypeSpecRef = foreignNativeInterface
                        ? GetForeignNativeInterfaceTypeSpecRef(
                            parameter.Type)
                        : GenericInterfaceTypeSpecRef(parameter.Type,
                            formalTypeSpecRows, builtInTypeSpecRows,
                            typeSpecRows);
                    var defaultConstantSourceRow = 0u;
                    if (schema.UsesClassParameterDefaults()
                        || schema.UsesForeignNativeInterfaceParameterSurface())
                    {
                        if (parameter.DefaultValue is BoundConstant value)
                        {
                            if (parameter.DefaultExpression is not null
                                || parameter.HasPendingDefault
                                || parameter.Mode
                                    != BoundInterfaceParameterMode.Value
                                || parameter.Type.Kind
                                    != BoundInterfaceTypeKind.Primitive
                                || parameter.Type.IsWritable
                                || parameter.Type.IsOptional
                                || parameter.Type.IsGarbageCollected
                                || parameter.Type.IsInitialization)
                            {
                                throw Unsupported(
                                    "CM1 interface parameter defaults require a checked closed scalar value.");
                            }

                            var frozenValue
                                = AddC1M1ParameterDefault(
                                    methodParameterSurfaceValues, value,
                                    parameter.Type.PrimitiveType,
                                    parameterTypeSpecRef);
                            defaultConstantSourceRow
                                = frozenValue.ConstantSourceRow;
                        }
                        else if (parameter.DefaultExpression is not null
                            || parameter.HasPendingDefault)
                        {
                            throw Unsupported(
                                "CM1 does not serialize open or unformed interface parameter defaults.");
                        }
                    }

                    if (schema.UsesMethodParameterLabels())
                    {
                        var parameterNameStringRow = names.Count + 1;
                        names.Add((parameter.Name, 1));
                        AppendMethodParameterSurfaceRow(
                            methodParameterLabelBytes,
                            ref methodParameterLabelCount, schema,
                            checked((uint)methodRow),
                            checked((uint)parameterOrdinal),
                            checked((uint)parameterNameStringRow),
                            defaultConstantSourceRow);
                    }
                    AppendUInt32(parameterBytes,
                        checked((uint)signatureRow));
                    AppendUInt32(parameterBytes,
                        checked((uint)parameterOrdinal));
                    AppendUInt32(parameterBytes, parameterTypeSpecRef);
                    parameterBytes.Add(0);
                    parameterBytes.Add(0);
                    parameterBytes.Add(0);
                    parameterBytes.Add(0);
                }
                emittedParameters += member.Parameters.Length;

                var resultTypeSpecRef = member.ResultType is null
                    ? GetBuiltInTypeSpecRef(PrimitiveType.Void,
                        builtInTypeSpecRows, typeSpecRows)
                    : foreignNativeInterface
                    ? GetForeignNativeInterfaceTypeSpecRef(member.ResultType)
                    : GenericInterfaceTypeSpecRef(member.ResultType,
                        formalTypeSpecRows, builtInTypeSpecRows,
                        typeSpecRows);
                AppendUInt32(signatureBytes, resultTypeSpecRef);
                AppendUInt32(signatureBytes,
                    foreignNativeInterface
                        ? GetForeignNativeInterfaceReceiverTypeSpecRef(
                            member.RequiresMutableReceiver)
                        : receiverTypeSpecRef);
                AppendUInt32(signatureBytes,
                    checked((uint)firstParameter));
                AppendUInt32(signatureBytes,
                    checked((uint)member.Parameters.Length));
                AppendUInt32(signatureBytes, 0);
                AppendUInt32(signatureBytes, 0);

                var contractFirst = 0;
                var methodContractCount = 0;
                if (foreignNativeInterface)
                {
                    (contractFirst, methodContractCount)
                        = AddForeignNativeInterfaceMethodContracts(member,
                            methodRow, contractCount,
                            foreignInterfaceContractRows,
                            foreignInterfacePredicateGraphs,
                            foreignInterfacePredicateFootprints,
                            foreignInterfacePredicateRoots,
                            foreignInterfacePredicateNodes,
                            foreignInterfacePredicateOperands,
                            GetCount(sections, 0x0037),
                            GetCount(sections, 0x0038),
                            GetCount(sections, 0x0039),
                            GetCount(sections, 0x003B),
                            GetCount(sections, 0x003C));
                }

                AppendUInt32(methodBytes, checked((uint)typeDefRow));
                AppendUInt32(methodBytes,
                    checked((uint)methodNameStringRow));
                AppendUInt32(methodBytes, checked((uint)signatureRow));
                AppendUInt32(methodBytes, 2u
                    | (schema.UsesMethodReceiverAccess()
                        && member.RequiresMutableReceiver ? 4u : 0u));
                AppendUInt32(methodBytes, contractFirst);
                AppendUInt32(methodBytes, methodContractCount);
                AppendUInt32(methodBytes, 0);
                interfaceMethodRows.Add(new TrRmInterfaceMethodRow(
                    interfaceDefRow, interfaceMethodOrdinal++, methodRow));
                emittedMethods++;
                emittedSignatures++;
            }

            var interfaceMethodCountForDefinition = methodMembers.Length;
            interfaceDefRows.Add(new TrRmInterfaceDefRow(typeDefRow,
                selfFormalRow, 0, 0, methodFirst,
                interfaceMethodCountForDefinition, associatedTypeFirst,
                associatedTypeMembers.Length, 0, 0, 0, 0));

            AppendUInt32(typeDefBytes, checked((uint)namespaceStringRow));
            AppendUInt32(typeDefBytes, checked((uint)nameStringRow));
            AppendUInt32(typeDefBytes, 2u | (3u << 2));
            AppendUInt32(typeDefBytes, 0);
            AppendUInt32(typeDefBytes, 0);
            AppendUInt32(typeDefBytes, checked((uint)genericFirst));
            AppendUInt32(typeDefBytes,
                checked((uint)definition.Formals.Length));
            AppendUInt32(typeDefBytes, foreignNativeInterface ? 0
                : checked((uint)(layoutRowBase + interfaceDefRows.Count)));
            AppendUInt32(typeDefBytes, foreignNativeInterface ? 0u : 1u);
            AppendUInt32(typeDefBytes, 0);
            AppendUInt32(typeDefBytes, 0);

            if (!foreignNativeInterface)
            {
                AppendUInt32(layoutBytes, checked((uint)typeDefRow));
                AppendUInt32(layoutBytes, 0);
                layoutBytes.Add(0);
                layoutBytes.Add(0);
                layoutBytes.Add(0);
                layoutBytes.Add(0);
                AppendUInt32(layoutBytes, 0);
                AppendUInt32(layoutBytes, 0);
                AppendUInt32(layoutBytes, 0);
                AppendUInt32(layoutBytes, 0);
            }
        }

        if (foreignNativeInterface
            && !foreignFunctionPointerSignatureIndexes.IsEmpty)
        {
            AddForeignNativeFunctionPointerAnchors(program,
                foreignFunctionPointerSignatureIndexes,
                foreignFunctionPointerAnchorMethodRows,
                type => type == PrimitiveType.Void
                    ? GetBuiltInTypeSpecRef(PrimitiveType.Void,
                        builtInTypeSpecRows, typeSpecRows)
                    : checked((uint)NativeValueTypeRow(type,
                        nativeRawPointerTypeRow,
                        nativeFunctionPointerTypeRows, typeSpecRows,
                        nativePointerTypeRows, program.NativePointerTypes,
                        structureTypeDefRows, typeDefTypeSpecRows,
                        primitive => checked((int)GetBuiltInTypeSpecRef(
                            primitive, builtInTypeSpecRows, typeSpecRows)))),
                methodCount, signatureCount, parameterCount,
                contractCount, ref emittedMethods, ref emittedSignatures,
                ref emittedParameters, names, methodBytes, signatureBytes,
                parameterBytes, foreignInterfaceContractRows,
                foreignInterfacePredicateGraphs,
                foreignInterfacePredicateFootprints,
                foreignInterfacePredicateRoots,
                foreignInterfacePredicateNodes,
                foreignInterfacePredicateOperands,
                GetCount(sections, 0x0037), GetCount(sections, 0x0038),
                GetCount(sections, 0x0039), GetCount(sections, 0x003B),
                GetCount(sections, 0x003C), schema);
        }

        var rewrittenStrings = TrRmStringTableWriter.EncodeTextRows(names);
        var stringTable = new TrRmStringTableReader(rewrittenStrings,
            names.Count);
        var finalTypeDefCount = checked(typeDefCount + emissions.Count);
        var finalMethodCount = checked(methodCount + emittedMethods);
        var finalSignatureCount = checked(signatureCount + emittedSignatures);
        var finalParameterCount = checked(parameterCount + emittedParameters);
        var finalContractCount = checked(contractCount
            + foreignInterfaceContractRows.Count);
        var finalLayoutCount = checked(layoutCount
            + (foreignNativeInterface ? 0 : emissions.Count));
        var finalGenericParamCount = genericParameterRows.Count;
        var typeSpecBytes = TrRmTypeSpecTableWriter.Encode(typeSpecRows,
            finalTypeDefCount, importCount, finalGenericParamCount,
            nativeFunctionPointerSignatureCount,
            schema, finalMethodCount);
        var moduleSection = GetSection(sections, 0x0001);
        var modules = new TrRmModuleTableReader(moduleSection.Bytes.Span,
            checked((int)moduleSection.RecordCount), stringTable,
            moduleRefCount, finalTypeDefCount, finalMethodCount,
            sourceFileCount, GetCount(sections, 0x0004));
        var featureCount = GetCount(sections, 0x0004);
        var features = featureCount == 0 ? default
            : new TrRmFeatureTableReader(GetSection(sections, 0x0004)
                .Bytes.Span, featureCount, schema);
        var typeDefs = new TrRmTypeDefTableReader(typeDefBytes.ToArray(),
            finalTypeDefCount, stringTable, modules, fieldCount,
            finalGenericParamCount, finalLayoutCount, finalContractCount,
            schema);
        var typeSpecs = new TrRmTypeSpecTableReader(typeSpecBytes,
            typeSpecRows.Count, finalTypeDefCount, importCount,
            finalGenericParamCount, nativeFunctionPointerSignatureCount,
            schema, finalMethodCount);
        var finalSignatureBytes = signatureBytes.ToArray();
        var signatures = finalSignatureCount == 0 ? default
            : new TrRmMethodSigTableReader(
                finalSignatureBytes, finalSignatureCount, typeSpecs,
                finalParameterCount, finalGenericParamCount);
        var finalParameterBytes = parameterBytes.ToArray();
        var parameters = finalParameterCount == 0 ? default
            : new TrRmParamTableReader(finalParameterBytes,
                finalParameterCount, signatures, typeSpecs, schema);
        var finalMethodBytes = methodBytes.ToArray();
        var methods = finalMethodCount == 0 ? default
            : new TrRmMethodDefTableReader(finalMethodBytes,
                finalMethodCount, stringTable, modules, typeDefs, signatures,
                finalContractCount, bodyCount, schema);

        var genericParamBytes = finalGenericParamCount == 0 ? []
            : TrRmGenericParamTableWriter.Encode(genericParameterRows,
                stringTable, typeDefs, methods, signatures, typeSpecs,
                constraintRows.Count, constantSourceCount);
        var genericParams = new TrRmGenericParamTableReader(
            genericParamBytes, finalGenericParamCount, stringTable,
            typeDefs, methods, signatures, typeSpecs,
            constraintRows.Count, constantSourceCount);
        var constraintBytes = constraintRows.Count == 0 ? []
            : TrRmConstraintTableWriter.Encode(constraintRows,
                genericParams, typeSpecs, default);
        var finalInterfaceDefBytes = TrRmInterfaceDefTableWriter.Encode(
            interfaceDefRows, typeDefs, genericParams, 0,
            interfaceMethodRows.Count, associatedTypeDefRows.Count,
            0, 0);
        var interfaces = new TrRmInterfaceDefTableReader(
            finalInterfaceDefBytes, interfaceDefRows.Count, typeDefs,
            genericParams, 0, interfaceMethodRows.Count,
            associatedTypeDefRows.Count, 0, 0);
        var associatedTypeDefBytes = associatedTypeDefRows.Count == 0 ? []
            : TrRmAssociatedTypeDefTableWriter.Encode(associatedTypeDefRows,
                stringTable, typeSpecs, interfaces);
        _ = new TrRmAssociatedTypeDefTableReader(associatedTypeDefBytes,
            associatedTypeDefRows.Count, stringTable, typeSpecs, interfaces);
        var interfaceMethodBytes = TrRmInterfaceMethodTableWriter.Encode(
            interfaceMethodRows, interfaces, methods, signatures);
        var targetProfileSection = TryGetSection(sections, 0x0048,
            out var targetProfilesSection) ? targetProfilesSection
            : default;
        var targetProfiles = new TrRmTargetProfileTableReader(
            targetProfileSection.Bytes.Span,
            checked((int)targetProfileSection.RecordCount),
            schema);
        var finalLayoutBytes = layoutBytes.ToArray();
        _ = new TrRmLayoutTableReader(finalLayoutBytes, finalLayoutCount,
            typeDefs, targetProfiles, GetCount(sections, 0x0023));
        if (foreignNativeInterface)
        {
            if (contractCount != 0)
            {
                throw Unsupported(
                    "The F+I source producer cannot merge pre-existing Contract rows.");
            }

            var contractBytes = TrRmContractTableWriter
                .EncodeForeignInvocationDraft(foreignInterfaceContractRows,
                    typeDefs, methods, signatures, parameters, typeSpecs,
                    features, modules,
                    checked(GetCount(sections, 0x0037)
                        + foreignInterfacePredicateGraphs.Count), schema);
            SetSection(sections, 0x001B,
                checked((uint)foreignInterfaceContractRows.Count),
                contractBytes);
            if (foreignInterfacePredicateGraphs.Count != 0)
            {
                var predicateGraphCount = checked(
                    GetCount(sections, 0x0037)
                    + foreignInterfacePredicateGraphs.Count);
                var predicateFootprintCount = checked(
                    GetCount(sections, 0x0038)
                    + foreignInterfacePredicateFootprints.Count);
                var predicateRootCount = checked(
                    GetCount(sections, 0x0039)
                    + foreignInterfacePredicateRoots.Count);
                var predicateNodeCount = checked(
                    GetCount(sections, 0x003B)
                    + foreignInterfacePredicateNodes.Count);
                var predicateOperandCount = checked(
                    GetCount(sections, 0x003C)
                    + foreignInterfacePredicateOperands.Count);
                var predicateGraphBytes
                    = TrRmPredicateGraphTableWriter.Encode(
                        foreignInterfacePredicateGraphs,
                        predicateNodeCount, predicateFootprintCount);
                var predicateFootprintBytes
                    = TrRmPredicateReadFootprintTableWriter.Encode(
                        foreignInterfacePredicateFootprints,
                        predicateRootCount, 0);
                var predicateRootBytes
                    = TrRmPredicateReadRootTableWriter.Encode(
                        foreignInterfacePredicateRoots, 0, 0, schema);
                var predicateNodeBytes
                    = TrRmPredicateNodeTableWriter.Encode(
                        foreignInterfacePredicateNodes,
                        predicateOperandCount, typeSpecs, schema);
                var operandCounts = new TrRmPredicateTableRowCounts(
                    predicateRootCount, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
                var predicateOperandBytes
                    = TrRmPredicateNodeOperandTableWriter.Encode(
                        foreignInterfacePredicateOperands, operandCounts,
                        schema);
                SetSection(sections, 0x0037,
                    checked((uint)predicateGraphCount), predicateGraphBytes);
                SetSection(sections, 0x0038,
                    checked((uint)predicateFootprintCount),
                    predicateFootprintBytes);
                SetSection(sections, 0x0039,
                    checked((uint)predicateRootCount), predicateRootBytes);
                SetSection(sections, 0x003B,
                    checked((uint)predicateNodeCount), predicateNodeBytes);
                SetSection(sections, 0x003C,
                    checked((uint)predicateOperandCount),
                    predicateOperandBytes);
            }
        }
        for (var index = 0; index < interfaceTypeExportNameRows.Count;
            index++)
        {
            exportRows.Add(new TrRmExportRow(
                0x1300_0000u
                    | checked((uint)(typeDefRowBase + index + 1)),
                interfaceTypeExportNameRows[index]));
        }
        for (var index = 0; index < interfaceMethodExportNameRows.Count;
            index++)
        {
            exportRows.Add(new TrRmExportRow(
                0x1600_0000u
                    | checked((uint)(methodRowBase + index + 1)),
                interfaceMethodExportNameRows[index]));
        }
        for (var index = 0; index < associatedTypeExportNameRows.Count;
            index++)
        {
            exportRows.Add(new TrRmExportRow(
                0x2000_0000u | checked((uint)(associatedTypeDefCount
                    + index + 1)),
                associatedTypeExportNameRows[index]));
        }

        SetSection(sections, 0x0002, checked((uint)names.Count),
            rewrittenStrings);
        SetSection(sections, 0x0011,
            checked((uint)typeSpecRows.Count), typeSpecBytes);
        SetSection(sections, 0x0013,
            checked((uint)finalTypeDefCount), [.. typeDefBytes]);
        if (!foreignNativeInterface || finalLayoutCount != 0)
        {
            SetSection(sections, 0x0015, checked((uint)finalLayoutCount),
                finalLayoutBytes);
        }
        SetSection(sections, 0x0016, checked((uint)finalMethodCount),
            finalMethodBytes);
        SetSection(sections, 0x0017, checked((uint)finalSignatureCount),
            finalSignatureBytes);
        SetSection(sections, 0x0018, checked((uint)finalParameterCount),
            [.. parameterBytes]);
        SetSection(sections, 0x0019,
            checked((uint)finalGenericParamCount), genericParamBytes);
        SetSection(sections, 0x001A,
            checked((uint)constraintRows.Count), constraintBytes);
        SetSection(sections, 0x0024,
            checked((uint)interfaceMethodRows.Count), interfaceMethodBytes);
        SetSection(sections, 0x0025,
            checked((uint)interfaceDefRows.Count), finalInterfaceDefBytes);
        if (associatedTypeDefRows.Count != 0)
        {
            SetSection(sections, 0x0027,
                checked((uint)(associatedTypeDefCount
                    + associatedTypeDefRows.Count)),
                associatedTypeDefBytes);
        }
        if (foreignNativeInterface && methodParameterLabelCount == 0)
        {
            sections.RemoveAll(static section => section.Kind == 0x0066);
        }
        else
        {
            SetSection(sections, 0x0066,
                checked((uint)methodParameterLabelCount),
                methodParameterLabelBytes.Count == 0 ? []
                    : [.. methodParameterLabelBytes]);
        }
        SetSection(sections, 0x001D, checked((uint)exportRows.Count),
            EncodeExportRows(exportRows));

        sections.Sort(static (left, right) =>
            left.Kind.CompareTo(right.Kind));
        var result = TrRmArtifactWriter.Encode(sections, schema);
        return result;
    }

    private static byte[] AddImportedGenericInterfaceMetadata(
        byte[] artifact, BoundProgram program, TrRmArtifactSchema schema,
        TrRmFrozenScalarValueBuilder? methodParameterSurfaceValues,
        out Dictionary<GenericImportedInterfaceAssociatedTypeKey, uint>
            importedInterfaceAssociatedTypeImportRows,
        string? standardSchemaIdentity = null,
        ImmutableArray<int> foreignNativeInterfaceStructureIndexes =
            default)
    {
        importedInterfaceAssociatedTypeImportRows = [];
        var emissions = CaptureImportedGenericInterfaceEmissions(program,
            schema);
        if (emissions.Count == 0)
        {
            return artifact;
        }
        var foreignNativeInterface
            = schema.UsesForeignNativeInterface();
        if (!schema.UsesGeneralMethodParameterLabels()
            && !schema.UsesForeignNativeInterfaceParameterSurface()
            && !foreignNativeInterface
            && emissions.Any(static emission =>
                emission.Definition.Members.Any(static member =>
                    member.Kind == BoundInterfaceMemberKind.Callable)))
        {
            throw Unsupported(
                "Imported G1 interface constraints require the exact G1L1 schema.");
        }
        if (foreignNativeInterface
            && !schema.UsesForeignNativeInterfaceParameterSurface()
            && emissions.Any(static emission =>
                emission.Definition.Members.Any(static member =>
                    member.Parameters.Length != 0)))
        {
            throw Unsupported(
                "Parameterized imported ForeignNativeInterface members require the exact PS1 profile.");
        }

        var artifactIndex = new TrRmArtifactIndex(artifact, schema,
            standardSchemaIdentity: standardSchemaIdentity);
        var sections = new List<TrRmSection>(artifactIndex.SectionCount);
        for (var sectionIndex = 0;
            sectionIndex < artifactIndex.SectionCount;
            sectionIndex++)
        {
            artifactIndex.GetSection(sectionIndex, out var kind,
                out var flags, out var bytes, out var count);
            sections.Add(new TrRmSection(kind, flags,
                checked((uint)count), bytes.ToArray()));
        }

        var stringSection = GetSection(sections, 0x0002);
        var strings = new TrRmStringTableReader(stringSection.Bytes.Span,
            checked((int)stringSection.RecordCount));
        var names = new List<(string Text, int Encoding)>(strings.Count
            + (emissions.Count * 4));
        for (var row = 1; row <= strings.Count; row++)
        {
            names.Add((strings.DecodeNamespace(row), 1));
        }

        var moduleRefRows = new List<TrRmModuleRefRow>(
            GetCount(sections, 0x0010) + emissions.Count);
        var moduleRefRowsByProvider = new Dictionary<string,
            (int Row, uint TargetRequirement, uint RuntimePolicy)>(
                StringComparer.Ordinal);
        if (TryGetSection(sections, 0x0010,
                out var moduleRefSection))
        {
            var count = checked((int)moduleRefSection.RecordCount);
            if (moduleRefSection.Bytes.Length != checked(count * 12))
            {
                throw Invalid(
                    "The G1L1 ModuleRef table has an invalid fixed-width extent.");
            }

            for (var row = 1; row <= count; row++)
            {
                var data = moduleRefSection.Bytes.Span.Slice(
                    (row - 1) * 12, 12);
                var providerIdentity = strings.DecodeName(
                    ReadInt(data, 0));
                var requirements = (
                    TargetRequirement: BinaryPrimitives
                        .ReadUInt32LittleEndian(data[4..]),
                    RuntimePolicy: BinaryPrimitives
                        .ReadUInt32LittleEndian(data[8..]));
                if (moduleRefRowsByProvider.TryGetValue(providerIdentity,
                        out var existing)
                    && (existing.TargetRequirement
                            != requirements.TargetRequirement
                        || existing.RuntimePolicy
                            != requirements.RuntimePolicy))
                {
                    throw Invalid(
                        "The G1L1 ModuleRef table has conflicting requirements for one provider.");
                }
                _ = moduleRefRowsByProvider.TryAdd(providerIdentity,
                    (row, requirements.TargetRequirement,
                        requirements.RuntimePolicy));

                moduleRefRows.Add(new TrRmModuleRefRow(
                    ReadInt(data, 0), requirements.TargetRequirement,
                    requirements.RuntimePolicy));
            }
        }

        var importRows = new List<TrRmImportRow>(
            GetCount(sections, 0x001C)
                + emissions.Sum(static emission =>
                    1 + emission.Definition.Members.Length));
        var importTriples = new HashSet<(
            int ModuleRefRow, string ExportKey, uint ExpectedDefinition)>();
        if (TryGetSection(sections, 0x001C, out var importSection))
        {
            var count = checked((int)importSection.RecordCount);
            if (importSection.Bytes.Length != checked(count * 12))
            {
                throw Invalid(
                    "The G1L1 Import table has an invalid fixed-width extent.");
            }

            for (var row = 1; row <= count; row++)
            {
                var data = importSection.Bytes.Span.Slice(
                    (row - 1) * 12, 12);
                var moduleRefRow = ReadInt(data, 0);
                var exportKey = strings.DecodeName(ReadInt(data, 4));
                var expectedDefinition
                    = BinaryPrimitives.ReadUInt32LittleEndian(data[8..]);
                if (!importTriples.Add((moduleRefRow, exportKey,
                        expectedDefinition)))
                {
                    throw Invalid(
                        "The G1L1 Import table contains a duplicate expectation.");
                }

                importRows.Add(new TrRmImportRow(moduleRefRow,
                    ReadInt(data, 4), expectedDefinition));
            }
        }

        int GetOrAddModuleRefRow(string producerIdentity,
            uint targetRequirement, uint runtimePolicy)
        {
            if (moduleRefRowsByProvider.TryGetValue(
                    producerIdentity, out var existing))
            {
                if (existing.TargetRequirement != targetRequirement
                    || existing.RuntimePolicy != runtimePolicy)
                {
                    throw Unsupported(
                        "An imported FNI declaration and an existing ModuleRef disagree on provider requirements.");
                }

                return existing.Row;
            }

            var providerStringRow = names.Count + 1;
            names.Add((producerIdentity, 1));
            var moduleRefRow = moduleRefRows.Count + 1;
            moduleRefRows.Add(new TrRmModuleRefRow(providerStringRow,
                targetRequirement, runtimePolicy));
            moduleRefRowsByProvider.Add(producerIdentity,
                (moduleRefRow, targetRequirement, runtimePolicy));
            return moduleRefRow;
        }

        int GetModuleRefRow(BoundModuleImport import)
            => GetOrAddModuleRefRow(import.ProducerIdentity,
                import.TargetRequirement, import.RuntimePolicy);

        var typeDefCount = GetCount(sections, 0x0013);
        var interfaceDefCount = GetCount(sections, 0x0025);
        var interfaceMethodCount = GetCount(sections, 0x0024);
        var associatedTypeDefCount = GetCount(sections, 0x0027);
        var methodCount = GetCount(sections, 0x0016);
        var signatureCount = GetCount(sections, 0x0017);
        var parameterCount = GetCount(sections, 0x0018);
        var layoutCount = GetCount(sections, 0x0015);
        var fieldCount = GetCount(sections, 0x0014);
        var contractCount = GetCount(sections, 0x001B);
        var sourceFileCount = GetCount(sections, 0x001F);
        var featureCount = GetCount(sections, 0x0004);
        var nativeFunctionPointerSignatureCount
            = GetCount(sections, 0x0052);

        var typeDefBytes = TryGetSection(sections, 0x0013,
            out var typeDefSection)
            ? typeDefSection.Bytes.ToArray().ToList() : [];
        var layoutBytes = TryGetSection(sections, 0x0015,
            out var layoutSection)
            ? layoutSection.Bytes.ToArray().ToList() : [];
        var methodBytes = TryGetSection(sections, 0x0016,
            out var methodSection)
            ? methodSection.Bytes.ToArray().ToList() : [];
        var signatureBytes = TryGetSection(sections, 0x0017,
            out var signatureSection)
            ? signatureSection.Bytes.ToArray().ToList() : [];
        var parameterBytes = TryGetSection(sections, 0x0018,
            out var parameterSection)
            ? parameterSection.Bytes.ToArray().ToList() : [];
        var methodParameterLabelCount = GetCount(sections, 0x0066);
        var methodParameterLabelBytes = TryGetSection(sections, 0x0066,
            out var methodParameterLabelSection)
            ? methodParameterLabelSection.Bytes.ToArray().ToList() : [];
        if (methodParameterLabelBytes.Count
            != checked(methodParameterLabelCount
                * GetMethodParameterSurfaceRowSize(schema)))
        {
            throw Invalid(
                "The G1L1 MethodParameterLabel table has an invalid fixed-width extent.");
        }

        var typeSpecRows = ReadVariableRows(sections, 0x0011, 2, 1);
        var builtInTypeSpecRows = GetBuiltInTypeSpecRows(typeSpecRows);
        var foreignNativePointerTypeRows
            = new Dictionary<PrimitiveType, int>();
        var foreignNativeFunctionPointerTypeRows
            = new Dictionary<PrimitiveType, int>();
        var foreignNativeStructureTypeDefRows
            = new Dictionary<int, int>();
        var foreignNativeTypeDefTypeSpecRows
            = new Dictionary<int, int>();
        if (foreignNativeInterface && typeDefCount != 0)
        {
            MapForeignNativeInterfaceStructureTypeDefRows(program,
                foreignNativeInterfaceStructureIndexes, strings,
                GetSection(sections, 0x0001).Bytes.Span,
                moduleRefRows.Count, typeDefBytes.ToArray(), typeDefCount,
                methodCount, sourceFileCount, fieldCount,
                GetCount(sections, 0x0019), layoutCount, contractCount,
                featureCount, schema,
                foreignNativeStructureTypeDefRows);
        }
        if (foreignNativeInterface
            && !foreignNativeInterfaceStructureIndexes.IsDefaultOrEmpty)
        {
            foreach (var structureIndex
                in foreignNativeInterfaceStructureIndexes)
            {
                var structure = program.Structures[structureIndex];
                if (structure.StandardTypeOrigin
                    is not BoundStandardTypeOrigin origin)
                {
                    continue;
                }
                if (standardSchemaIdentity is null
                    || !IsSelectedFniAggregateOriginSchemaIdentity(
                        standardSchemaIdentity,
                        origin.Provider.SchemaIdentity)
                    || origin.ExportKeys.IsDefaultOrEmpty
                    || !foreignNativeStructureTypeDefRows.TryGetValue(
                        structureIndex, out var typeDefRow))
                {
                    throw Unsupported(
                        "An imported FNI aggregate TypeDef has no exact provider view or expected local row.");
                }

                var moduleRefRow = GetOrAddModuleRefRow(
                    origin.Provider.ModuleIdentity,
                    origin.Provider.TargetRequirement,
                    origin.Provider.RuntimePolicy);
                var expectedDefinitionId = 0x1300_0000u
                    | checked((uint)typeDefRow);
                var exportKey = GetSelectedFniAggregateOriginExportKey(
                    origin);
                if (string.IsNullOrEmpty(exportKey)
                    || exportKey.Contains('\0', StringComparison.Ordinal)
                    || !importTriples.Add((moduleRefRow, exportKey,
                        expectedDefinitionId)))
                {
                    throw Invalid(
                        "An FNI aggregate TypeDef Import has an invalid or duplicate ExportKey.");
                }

                var exportKeyStringRow = names.Count + 1;
                names.Add((exportKey, 1));
                importRows.Add(new TrRmImportRow(moduleRefRow,
                    exportKeyStringRow, expectedDefinitionId));
            }
        }

        var foreignFunctionPointerSignatureIndexes
            = foreignNativeInterface
                ? GetForeignNativeFunctionPointerSignatureIndexes(
                    emissions.Select(static emission =>
                        emission.Definition), program,
                    foreignNativeInterfaceStructureIndexes)
                : ImmutableArray<int>.Empty;
        var foreignFunctionPointerAnchorMethodRows
            = new Dictionary<int, int>(
                foreignFunctionPointerSignatureIndexes.Length);
        var nextForeignFunctionPointerAnchorMethodRow = checked(methodCount
            + emissions.Sum(static emission =>
                emission.Definition.Members.Length) + 1);
        foreach (var signatureIndex
            in foreignFunctionPointerSignatureIndexes)
        {
            foreignFunctionPointerAnchorMethodRows.Add(signatureIndex,
                nextForeignFunctionPointerAnchorMethodRow++);
        }
        var foreignNativeRawPointerTypeRow = 0;
        var hasForeignNativeRawPointerSignatureType
            = foreignNativeInterface && emissions.Any(static emission =>
                emission.Definition.Members.Any(static member =>
                    member.ResultType?.PrimitiveType
                        == PrimitiveType.NativeRawPointer
                    || member.Parameters.Any(static parameter =>
                        parameter.Type.PrimitiveType
                            == PrimitiveType.NativeRawPointer)))
                || foreignNativeInterface
                    && foreignFunctionPointerSignatureIndexes.Any(
                        signatureIndex =>
                        {
                            var signature
                                = program.NativeFunctionPointerSignatures[
                                    signatureIndex];
                            return signature.ReturnType
                                    == PrimitiveType.NativeRawPointer
                                || signature.ParameterTypes.Contains(
                                    PrimitiveType.NativeRawPointer);
                        });
        if (foreignNativeInterface
            && (hasForeignNativeRawPointerSignatureType
                || program.NativePointerTypes.Any(static pointer =>
                    pointer.PointeeType
                        == PrimitiveType.NativeRawPointer)))
        {
            foreignNativeRawPointerTypeRow = typeSpecRows.Count + 1;
            typeSpecRows.Add(
                TrRmTypeSpecTableWriter.InlineFamily(0x8C, 0));
        }
        var foreignReceiverTypeSpecRows = new Dictionary<bool, uint>();
        uint GetForeignNativeInterfaceTypeSpecRef(BoundInterfaceType type)
        {
            if (type.Kind != BoundInterfaceTypeKind.Primitive
                || type.IsOptional || type.IsGarbageCollected
                || type.IsInitialization || type.IsGenericNominalConstantDomain
                || type.GenericNominalTypeIdentity is not null
                || type.SourceGenericTypeDefinitionIndex != -1)
            {
                throw Unsupported(
                    "Imported F+I MethodSig types must be complete, closed native ABI values.");
            }

            if (type.PrimitiveType == PrimitiveType.Void)
            {
                return GetBuiltInTypeSpecRef(PrimitiveType.Void,
                    builtInTypeSpecRows, typeSpecRows);
            }

            var row = NativeValueTypeRow(type.PrimitiveType,
                foreignNativeRawPointerTypeRow,
                foreignNativeFunctionPointerTypeRows, typeSpecRows,
                foreignNativePointerTypeRows, program.NativePointerTypes,
                foreignNativeStructureTypeDefRows,
                foreignNativeTypeDefTypeSpecRows,
                primitive => checked((int)GetBuiltInTypeSpecRef(primitive,
                    builtInTypeSpecRows, typeSpecRows)));
            return checked((uint)row);
        }

        foreach (var signatureIndex
            in foreignFunctionPointerSignatureIndexes)
        {
            var signature = program.NativeFunctionPointerSignatures[
                signatureIndex];
            if (signature.CallingConvention is not (
                    NativeCallingConvention.Unmanaged
                    or NativeCallingConvention.Cdecl
                    or NativeCallingConvention.Stdcall
                    or NativeCallingConvention.Vectorcall))
            {
                throw Unsupported(
                    "Standard FNI function-pointer anchors require an explicit registered convention.");
            }

            var typeSpecRow = checked((uint)typeSpecRows.Count + 1);
            typeSpecRows.Add(TrRmTypeSpecTableWriter.NativeFunctionPointer(
                (byte)signature.CallingConvention,
                checked((uint)foreignFunctionPointerAnchorMethodRows[
                    signatureIndex])));
            foreignNativeFunctionPointerTypeRows.Add(
                NativeFunctionPointerTypes.Type(signatureIndex),
                checked((int)typeSpecRow));
        }

        uint GetForeignNativeInterfaceReceiverTypeSpecRef(
            bool requiresMutableReceiver)
        {
            if (foreignReceiverTypeSpecRows.TryGetValue(
                    requiresMutableReceiver, out var existing))
            {
                return existing;
            }

            var row = checked((uint)typeSpecRows.Count + 1);
            typeSpecRows.Add(TrRmTypeSpecTableWriter.InlineFamily(0x8C, 0,
                requiresMutableReceiver ? (byte)1 : (byte)0));
            foreignReceiverTypeSpecRows.Add(requiresMutableReceiver, row);
            return row;
        }

        var genericParameterRows = ReadGenericParamRows(sections);
        var constraintRows = ReadConstraintRows(sections);
        var associatedTypeDefRows
            = ReadAssociatedTypeDefRows(sections);
        if (associatedTypeDefRows.Count != associatedTypeDefCount)
        {
            throw Invalid(
                "The AssociatedTypeDef table count changed while reading imported interface metadata.");
        }
        var interfaceDefRows = new List<TrRmInterfaceDefRow>(
            emissions.Count);
        var interfaceMethodRows = new List<TrRmInterfaceMethodRow>();
        var foreignInterfaceRows
            = new List<TrRmForeignNativeInterfaceRow>(emissions.Count);
        var foreignInterfaceContractRows = new List<TrRmContractRow>();
        var foreignInterfacePredicateGraphs
            = new List<TrRmPredicateGraphRow>();
        var foreignInterfacePredicateFootprints
            = new List<TrRmPredicateReadFootprintRow>();
        var foreignInterfacePredicateRoots
            = new List<TrRmPredicateReadRootRow>();
        var foreignInterfacePredicateNodes
            = new List<TrRmPredicateNodeRow>();
        var foreignInterfacePredicateOperands
            = new List<TrRmPredicateNodeOperandRow>();
        var emissionOrdinal = 0;
        var emittedMethods = 0;
        var emittedSignatures = 0;
        var emittedParameters = 0;

        foreach (var emission in emissions)
        {
            var definition = emission.Definition;
            var typeDefRow = checked(typeDefCount + emissionOrdinal + 1);
            var interfaceDefRow = checked(interfaceDefCount
                + emissionOrdinal + 1);
            if (foreignNativeInterface)
            {
                if (definition.ForeignNativeInterface
                    is not BoundForeignNativeInterfaceLayout layout)
                {
                    throw Unsupported(
                        "An imported F+I interface has no retained vtable layout.");
                }

                foreignInterfaceRows.Add(new TrRmForeignNativeInterfaceRow(
                    checked((uint)interfaceDefRow), 1,
                    layout.VTableByteOffset));
            }
            var moduleRefRow = GetModuleRefRow(emission.Import);
            var namespaceStringRow = names.Count + 1;
            var separator = definition.QualifiedName.LastIndexOf('.',
                StringComparison.Ordinal);
            names.Add((separator < 0 ? ""
                : definition.QualifiedName[..separator], 1));
            var nameStringRow = names.Count + 1;
            names.Add((separator < 0 ? definition.QualifiedName
                : definition.QualifiedName[(separator + 1)..], 1));
            var typeExportKeyStringRow = names.Count + 1;
            names.Add((emission.Import.ExportKey, 1));
            var typeDefExpectedId = 0x1300_0000u
                | checked((uint)typeDefRow);
            if (!importTriples.Add((moduleRefRow,
                    emission.Import.ExportKey, typeDefExpectedId)))
            {
                throw Invalid(
                    "The G1L1 imported interface TypeDef duplicates an existing Import expectation.");
            }

            importRows.Add(new TrRmImportRow(moduleRefRow,
                typeExportKeyStringRow, typeDefExpectedId));

            var genericFirst = definition.Formals.IsEmpty ? 0
                : checked(genericParameterRows.Count + 1);
            var constraintsByFormal
                = new List<BoundInterfaceConstraint>?[
                    definition.Formals.Length];
            foreach (var constraint in definition.Constraints)
            {
                var formalConstraints
                    = constraintsByFormal[constraint.FormalOrdinal] ??= [];
                formalConstraints.Add(constraint);
            }

            var interfaceTypeSpecRow = 0u;
            if (!foreignNativeInterface)
            {
                interfaceTypeSpecRow = checked(
                    (uint)typeSpecRows.Count + 1);
                typeSpecRows.Add(TrRmTypeSpecTableWriter.Reference(0x02,
                    checked((uint)typeDefRow)));
            }
            var formalTypeSpecRows = new Dictionary<int, uint>();
            var formalTypeSpecArguments = new uint[
                definition.Formals.Length];
            var selfFormalRow = 0;
            for (var ordinal = 0; ordinal < definition.Formals.Length;
                ordinal++)
            {
                var formal = definition.Formals[ordinal];
                var formalRow = checked(genericParameterRows.Count + 1);
                var formalNameStringRow = names.Count + 1;
                names.Add((formal.Name, 1));
                var formalConstraints = constraintsByFormal[ordinal];
                var constraintFirst = formalConstraints is null ? 0
                    : checked(constraintRows.Count + 1);
                if (formalConstraints is not null)
                {
                    foreach (var constraint in formalConstraints)
                    {
                        if (!IsGenericWriteConstraint(constraint))
                        {
                            throw Unsupported(
                                $"Imported interface '{definition.QualifiedName}' has an unsupported generic constraint.");
                        }

                        constraintRows.Add(new TrRmConstraintRow(
                            formalRow, 2, 2));
                    }
                }
                genericParameterRows.Add(new TrRmGenericParamRow(
                    0x1300_0000u | checked((uint)typeDefRow),
                    ordinal, formalNameStringRow, constraintFirst,
                    formalConstraints?.Count ?? 0, 1, 1, 1, 0, 0));
                var formalTypeSpecRow = checked(
                    (uint)typeSpecRows.Count + 1);
                typeSpecRows.Add(TrRmTypeSpecTableWriter.Reference(0x04,
                    checked((uint)formalRow)));
                formalTypeSpecRows.Add(ordinal, formalTypeSpecRow);
                formalTypeSpecArguments[ordinal] = formalTypeSpecRow;
                if (formal.IsSelf)
                {
                    if (selfFormalRow != 0)
                    {
                        throw Unsupported(
                            $"Imported interface '{definition.QualifiedName}' declares multiple self formals.");
                    }
                    selfFormalRow = formalRow;
                }
            }

            var receiverTypeSpecRef = interfaceTypeSpecRow;
            if (formalTypeSpecArguments.Length != 0)
            {
                receiverTypeSpecRef = checked(
                    (uint)typeSpecRows.Count + 1);
                typeSpecRows.Add(TrRmTypeSpecTableWriter.Constructed(
                    interfaceTypeSpecRow, formalTypeSpecArguments));
            }

            var methodMembers = definition.Members
                .Where(static member =>
                    member.Kind == BoundInterfaceMemberKind.Callable)
                .ToArray();
            var associatedTypeMembers = definition.Members
                .Where(static member =>
                    member.Kind == BoundInterfaceMemberKind.AssociatedType)
                .ToArray();
            var methodFirst = methodMembers.Length == 0 ? 0
                : checked(interfaceMethodCount
                    + interfaceMethodRows.Count + 1);
            var interfaceMethodOrdinal = 0;
            foreach (var member in methodMembers)
            {
                var methodNameStringRow = names.Count + 1;
                names.Add((member.Name, 1));
                var signatureRow = checked(signatureCount
                    + emittedSignatures + 1);
                var firstParameter = member.Parameters.IsEmpty ? 0
                    : checked(parameterCount + emittedParameters + 1);
                var methodRow = checked(methodCount + emittedMethods + 1);
                var methodExpectedId = 0x1600_0000u
                    | checked((uint)methodRow);
                var methodExportKeys
                    = ImmutableArray.CreateBuilder<string>(
                        member.AdditionalDeclarationKeys.Length + 1);
                methodExportKeys.Add(member.DeclarationKey);
                methodExportKeys.AddRange(
                    member.AdditionalDeclarationKeys);
                foreach (var methodExportKey in methodExportKeys)
                {
                    var methodExportKeyStringRow = names.Count + 1;
                    names.Add((methodExportKey, 1));
                    if (!importTriples.Add((moduleRefRow,
                            methodExportKey, methodExpectedId)))
                    {
                        throw Invalid(
                            "The G1L1 imported InterfaceMethod duplicates an existing Import expectation.");
                    }

                    importRows.Add(new TrRmImportRow(moduleRefRow,
                        methodExportKeyStringRow, methodExpectedId));
                }
                for (var parameterOrdinal = 0;
                    parameterOrdinal < member.Parameters.Length;
                    parameterOrdinal++)
                {
                    var parameter = member.Parameters[parameterOrdinal];
                    var parameterTypeSpecRef
                        = foreignNativeInterface
                            ? GetForeignNativeInterfaceTypeSpecRef(
                                parameter.Type)
                            : GenericInterfaceTypeSpecRef(parameter.Type,
                                formalTypeSpecRows, builtInTypeSpecRows,
                                typeSpecRows);
                    var defaultConstantSourceRow = 0u;
                    if (schema.UsesClassParameterDefaults()
                        || schema
                            .UsesForeignNativeInterfaceParameterSurface())
                    {
                        if (parameter.DefaultValue is BoundConstant value)
                        {
                            if (parameter.DefaultExpression is not null
                                || parameter.HasPendingDefault
                                || parameter.Mode
                                    != BoundInterfaceParameterMode.Value
                                || parameter.Type.Kind
                                    != BoundInterfaceTypeKind.Primitive
                                || parameter.Type.IsWritable
                                || parameter.Type.IsOptional
                                || parameter.Type.IsGarbageCollected
                                || parameter.Type.IsInitialization)
                            {
                                throw Unsupported(
                                    "CM1 imported interface defaults require a checked closed scalar value.");
                            }

                            var frozenValue
                                = AddC1M1ParameterDefault(
                                    methodParameterSurfaceValues, value,
                                    parameter.Type.PrimitiveType,
                                    parameterTypeSpecRef);
                            defaultConstantSourceRow
                                = frozenValue.ConstantSourceRow;
                        }
                        else if (parameter.DefaultExpression is not null
                            || parameter.HasPendingDefault)
                        {
                            throw Unsupported(
                                "CM1 imported interface defaults cannot be open or unformed.");
                        }
                    }

                    var parameterNameStringRow = names.Count + 1;
                    names.Add((parameter.Name, 1));
                    AppendMethodParameterSurfaceRow(
                        methodParameterLabelBytes,
                        ref methodParameterLabelCount, schema,
                        checked((uint)methodRow),
                        checked((uint)parameterOrdinal),
                        checked((uint)parameterNameStringRow),
                        defaultConstantSourceRow);
                    AppendUInt32(parameterBytes,
                        checked((uint)signatureRow));
                    AppendUInt32(parameterBytes,
                        checked((uint)parameterOrdinal));
                    AppendUInt32(parameterBytes, parameterTypeSpecRef);
                    parameterBytes.Add(0);
                    parameterBytes.Add(0);
                    parameterBytes.Add(0);
                    parameterBytes.Add(0);
                }
                emittedParameters += member.Parameters.Length;

                var resultTypeSpecRef = member.ResultType is null
                    ? GetBuiltInTypeSpecRef(PrimitiveType.Void,
                        builtInTypeSpecRows, typeSpecRows)
                    : foreignNativeInterface
                        ? GetForeignNativeInterfaceTypeSpecRef(
                            member.ResultType)
                        : GenericInterfaceTypeSpecRef(member.ResultType,
                            formalTypeSpecRows, builtInTypeSpecRows,
                            typeSpecRows);
                AppendUInt32(signatureBytes, resultTypeSpecRef);
                AppendUInt32(signatureBytes, foreignNativeInterface
                    ? GetForeignNativeInterfaceReceiverTypeSpecRef(
                        member.RequiresMutableReceiver)
                    : receiverTypeSpecRef);
                AppendUInt32(signatureBytes,
                    checked((uint)firstParameter));
                AppendUInt32(signatureBytes,
                    checked((uint)member.Parameters.Length));
                AppendUInt32(signatureBytes, 0);
                AppendUInt32(signatureBytes, 0);

                var methodContractFirst = 0;
                var methodContractCount = 0;
                if (foreignNativeInterface)
                {
                    (methodContractFirst, methodContractCount)
                        = AddForeignNativeInterfaceMethodContracts(member,
                            methodRow, contractCount,
                            foreignInterfaceContractRows,
                            foreignInterfacePredicateGraphs,
                            foreignInterfacePredicateFootprints,
                            foreignInterfacePredicateRoots,
                            foreignInterfacePredicateNodes,
                            foreignInterfacePredicateOperands,
                            GetCount(sections, 0x0037),
                            GetCount(sections, 0x0038),
                            GetCount(sections, 0x0039),
                            GetCount(sections, 0x003B),
                            GetCount(sections, 0x003C));
                }

                AppendUInt32(methodBytes, checked((uint)typeDefRow));
                AppendUInt32(methodBytes,
                    checked((uint)methodNameStringRow));
                AppendUInt32(methodBytes, checked((uint)signatureRow));
                AppendUInt32(methodBytes, 2u
                    | (schema.UsesMethodReceiverAccess()
                        && member.RequiresMutableReceiver ? 4u : 0u));
                AppendUInt32(methodBytes,
                    checked((uint)methodContractFirst));
                AppendUInt32(methodBytes,
                    checked((uint)methodContractCount));
                AppendUInt32(methodBytes, 0);
                interfaceMethodRows.Add(new TrRmInterfaceMethodRow(
                    interfaceDefRow, interfaceMethodOrdinal++, methodRow));
                emittedMethods++;
                emittedSignatures++;
            }

            var associatedTypeFirst = associatedTypeMembers.Length == 0 ? 0
                : checked(associatedTypeDefRows.Count + 1);
            foreach (var member in associatedTypeMembers)
            {
                var nameStringRow = names.Count + 1;
                names.Add((member.Name, 1));
                var associatedTypeDefRow
                    = checked(associatedTypeDefRows.Count + 1);
                var expectedDefinition = 0x2000_0000u
                    | checked((uint)associatedTypeDefRow);
                var associatedExportKeys
                    = ImmutableArray.CreateBuilder<string>(
                        member.AdditionalDeclarationKeys.Length + 1);
                associatedExportKeys.Add(member.DeclarationKey);
                associatedExportKeys.AddRange(
                    member.AdditionalDeclarationKeys);
                foreach (var associatedExportKey in associatedExportKeys)
                {
                    var exportKeyStringRow = names.Count + 1;
                    names.Add((associatedExportKey, 1));
                    if (!importTriples.Add((moduleRefRow,
                            associatedExportKey, expectedDefinition)))
                    {
                        throw Invalid(
                            "A G1L1 imported AssociatedTypeDef duplicates an existing expectation.");
                    }

                    var importRow = checked(importRows.Count + 1);
                    importRows.Add(new TrRmImportRow(moduleRefRow,
                        exportKeyStringRow, expectedDefinition));
                    if (!importedInterfaceAssociatedTypeImportRows.TryAdd(
                            new GenericImportedInterfaceAssociatedTypeKey(
                                emission.Import.ProducerIdentity,
                                emission.Import.ExportKey,
                                definition.QualifiedName,
                                definition.Formals.Length, member.Ordinal,
                                associatedExportKey,
                                emission.Import.TargetRequirement,
                                emission.Import.RuntimePolicy),
                            0x1C00_0000u | checked((uint)importRow)))
                    {
                        throw Invalid(
                            "A G1L1 imported AssociatedTypeDef has an ambiguous retained identity.");
                    }
                }

                var defaultTypeSpecRef
                    = member.AssociatedDefaultType is null ? 0
                        : GenericInterfaceTypeSpecRef(
                            member.AssociatedDefaultType,
                            formalTypeSpecRows, builtInTypeSpecRows,
                            typeSpecRows);
                associatedTypeDefRows.Add(
                    new TrRmAssociatedTypeDefRow(
                        interfaceDefRow, nameStringRow,
                        defaultTypeSpecRef));
            }

            interfaceDefRows.Add(new TrRmInterfaceDefRow(typeDefRow,
                selfFormalRow, 0, 0, methodFirst,
                methodMembers.Length, associatedTypeFirst,
                associatedTypeMembers.Length, 0, 0, 0, 0));

            AppendUInt32(typeDefBytes, checked((uint)namespaceStringRow));
            AppendUInt32(typeDefBytes, checked((uint)nameStringRow));
            AppendUInt32(typeDefBytes, 2u | (3u << 2));
            AppendUInt32(typeDefBytes, 0);
            AppendUInt32(typeDefBytes, 0);
            AppendUInt32(typeDefBytes, checked((uint)genericFirst));
            AppendUInt32(typeDefBytes,
                checked((uint)definition.Formals.Length));
            AppendUInt32(typeDefBytes, foreignNativeInterface ? 0
                : checked((uint)(layoutCount + emissionOrdinal + 1)));
            AppendUInt32(typeDefBytes, foreignNativeInterface ? 0u : 1u);
            AppendUInt32(typeDefBytes, 0);
            AppendUInt32(typeDefBytes, 0);

            if (!foreignNativeInterface)
            {
                AppendUInt32(layoutBytes, checked((uint)typeDefRow));
                AppendUInt32(layoutBytes, 0);
                layoutBytes.Add(0);
                layoutBytes.Add(0);
                layoutBytes.Add(0);
                layoutBytes.Add(0);
                AppendUInt32(layoutBytes, 0);
                AppendUInt32(layoutBytes, 0);
                AppendUInt32(layoutBytes, 0);
                AppendUInt32(layoutBytes, 0);
            }

            emissionOrdinal++;
        }

        if (foreignNativeInterface
            && !foreignFunctionPointerSignatureIndexes.IsEmpty)
        {
            AddForeignNativeFunctionPointerAnchors(program,
                foreignFunctionPointerSignatureIndexes,
                foreignFunctionPointerAnchorMethodRows,
                type => type == PrimitiveType.Void
                    ? GetBuiltInTypeSpecRef(PrimitiveType.Void,
                        builtInTypeSpecRows, typeSpecRows)
                    : checked((uint)NativeValueTypeRow(type,
                        foreignNativeRawPointerTypeRow,
                        foreignNativeFunctionPointerTypeRows, typeSpecRows,
                        foreignNativePointerTypeRows,
                        program.NativePointerTypes,
                        foreignNativeStructureTypeDefRows,
                        foreignNativeTypeDefTypeSpecRows,
                        primitive => checked((int)GetBuiltInTypeSpecRef(
                            primitive, builtInTypeSpecRows, typeSpecRows)))),
                methodCount, signatureCount, parameterCount,
                contractCount, ref emittedMethods, ref emittedSignatures,
                ref emittedParameters, names, methodBytes, signatureBytes,
                parameterBytes, foreignInterfaceContractRows,
                foreignInterfacePredicateGraphs,
                foreignInterfacePredicateFootprints,
                foreignInterfacePredicateRoots,
                foreignInterfacePredicateNodes,
                foreignInterfacePredicateOperands,
                GetCount(sections, 0x0037), GetCount(sections, 0x0038),
                GetCount(sections, 0x0039), GetCount(sections, 0x003B),
                GetCount(sections, 0x003C), schema);
        }

        var rewrittenStrings = TrRmStringTableWriter.EncodeTextRows(names);
        var stringTable = new TrRmStringTableReader(rewrittenStrings,
            names.Count);
        var finalTypeDefCount = checked(typeDefCount + emissions.Count);
        var finalMethodCount = checked(methodCount + emittedMethods);
        var finalSignatureCount = checked(signatureCount + emittedSignatures);
        var finalParameterCount = checked(parameterCount + emittedParameters);
        var finalContractCount = checked(contractCount
            + foreignInterfaceContractRows.Count);
        var finalLayoutCount = checked(layoutCount
            + (foreignNativeInterface ? 0 : emissions.Count));
        var finalGenericParameterCount = genericParameterRows.Count;
        var moduleSection = GetSection(sections, 0x0001);
        var modules = new TrRmModuleTableReader(moduleSection.Bytes.Span,
            checked((int)moduleSection.RecordCount), stringTable,
            moduleRefRows.Count, finalTypeDefCount, finalMethodCount,
            sourceFileCount, featureCount);
        var moduleRefBytes = TrRmModuleRefTableWriter.Encode(moduleRefRows,
            stringTable, modules);
        var moduleRefs = new TrRmModuleRefTableReader(moduleRefBytes,
            moduleRefRows.Count, stringTable, modules);
        var finalTypeDefBytes = typeDefBytes.ToArray();
        var typeDefs = new TrRmTypeDefTableReader(finalTypeDefBytes,
            finalTypeDefCount, stringTable, modules, fieldCount,
            finalGenericParameterCount, finalLayoutCount, finalContractCount,
            schema);
        var typeSpecBytes = TrRmTypeSpecTableWriter.Encode(typeSpecRows,
            finalTypeDefCount, importRows.Count,
            finalGenericParameterCount,
            nativeFunctionPointerSignatureCount, schema, finalMethodCount);
        var typeSpecs = new TrRmTypeSpecTableReader(typeSpecBytes,
            typeSpecRows.Count, finalTypeDefCount, importRows.Count,
            finalGenericParameterCount,
            nativeFunctionPointerSignatureCount, schema, finalMethodCount);
        var finalSignatureBytes = signatureBytes.ToArray();
        var signatures = finalSignatureCount == 0 ? default
            : new TrRmMethodSigTableReader(finalSignatureBytes,
                finalSignatureCount, typeSpecs, finalParameterCount,
                finalGenericParameterCount);
        var finalMethodBytes = methodBytes.ToArray();
        var methods = finalMethodCount == 0 ? default
            : new TrRmMethodDefTableReader(finalMethodBytes,
                finalMethodCount, stringTable, modules, typeDefs, signatures,
                finalContractCount, GetCount(sections, 0x0020), schema);
        var genericParameterBytes = genericParameterRows.Count == 0 ? []
            : TrRmGenericParamTableWriter.Encode(genericParameterRows,
                stringTable, typeDefs, methods, signatures, typeSpecs,
                constraintRows.Count, GetCount(sections, 0x0030));
        var genericParams = new TrRmGenericParamTableReader(
            genericParameterBytes, genericParameterRows.Count,
            stringTable, typeDefs, methods, signatures, typeSpecs,
            constraintRows.Count, GetCount(sections, 0x0030));
        var constraintBytes = constraintRows.Count == 0 ? []
            : TrRmConstraintTableWriter.Encode(constraintRows,
                genericParams, typeSpecs, default);
        var finalInterfaceMethodCount = checked(interfaceMethodCount
            + interfaceMethodRows.Count);
        var interfaceBaseCount = GetCount(sections, 0x0026);
        var finalAssociatedTypeCount = associatedTypeDefRows.Count;
        var associatedConstCount = GetCount(sections, 0x0028);
        var interfaceConstCount = GetCount(sections, 0x0029);
        var newInterfaceDefBytes = TrRmInterfaceDefTableWriter.Encode(
            interfaceDefRows, typeDefs, genericParams, interfaceBaseCount,
            finalInterfaceMethodCount, finalAssociatedTypeCount,
            associatedConstCount, interfaceConstCount);
        var existingInterfaceDefBytes = TryGetSection(sections, 0x0025,
            out var existingInterfaceDefSection)
            ? existingInterfaceDefSection.Bytes.ToArray() : [];
        var finalInterfaceDefBytes = new byte[
            existingInterfaceDefBytes.Length + newInterfaceDefBytes.Length];
        existingInterfaceDefBytes.CopyTo(finalInterfaceDefBytes, 0);
        newInterfaceDefBytes.CopyTo(finalInterfaceDefBytes,
            existingInterfaceDefBytes.Length);
        var interfaces = new TrRmInterfaceDefTableReader(
            finalInterfaceDefBytes,
            checked(interfaceDefCount + interfaceDefRows.Count),
            typeDefs, genericParams, interfaceBaseCount,
            finalInterfaceMethodCount, finalAssociatedTypeCount,
            associatedConstCount, interfaceConstCount);
        var newInterfaceMethodBytes
            = interfaceMethodRows.Count == 0 ? []
                : TrRmInterfaceMethodTableWriter.Encode(
                    interfaceMethodRows, interfaces, methods, signatures);
        var existingInterfaceMethodBytes = TryGetSection(sections, 0x0024,
            out var existingInterfaceMethodSection)
            ? existingInterfaceMethodSection.Bytes.ToArray() : [];
        var finalInterfaceMethodBytes = new byte[
            existingInterfaceMethodBytes.Length
                + newInterfaceMethodBytes.Length];
        existingInterfaceMethodBytes.CopyTo(finalInterfaceMethodBytes, 0);
        newInterfaceMethodBytes.CopyTo(finalInterfaceMethodBytes,
            existingInterfaceMethodBytes.Length);
        var fields = fieldCount == 0 ? default
            : new TrRmFieldDefTableReader(
                GetSection(sections, 0x0014).Bytes.Span, fieldCount,
                stringTable, typeDefs, typeSpecs);
        var finalAssociatedTypeDefBytes
            = finalAssociatedTypeCount == 0 ? []
                : TrRmAssociatedTypeDefTableWriter.Encode(
                    associatedTypeDefRows, stringTable, typeSpecs,
                    interfaces);
        var associatedTypeDefs = finalAssociatedTypeCount == 0 ? default
            : new TrRmAssociatedTypeDefTableReader(
                finalAssociatedTypeDefBytes, finalAssociatedTypeCount,
                stringTable, typeSpecs, interfaces);
        var features = featureCount == 0 ? default
            : new TrRmFeatureTableReader(
                GetSection(sections, 0x0004).Bytes.Span,
                featureCount, schema);
        if (foreignNativeInterface)
        {
            var finalParameterBytes = parameterBytes.ToArray();
            var parameters = finalParameterCount == 0 ? default
                : new TrRmParamTableReader(finalParameterBytes,
                    finalParameterCount, signatures, typeSpecs, schema);
            var targetProfileSection = GetSection(sections, 0x0048);
            var targetProfiles = new TrRmTargetProfileTableReader(
                targetProfileSection.Bytes.Span,
                checked((int)targetProfileSection.RecordCount), schema);
            var targetProfileRow = 0;
            for (var row = 1; row <= targetProfiles.Count; row++)
            {
                var profile = targetProfiles.GetProfile(row);
                if (profile.ProfileId
                        != TrRmTargetProfileTableReader
                            .WindowsX64NativeProfileId
                    || profile.RuleRevision
                        != TrRmTargetProfileTableReader
                            .WindowsX64NativeProfileRuleRevision)
                {
                    continue;
                }

                if (targetProfileRow != 0)
                {
                    throw Invalid(
                        "The F+I TargetProfile table has duplicate Windows x64 native profiles.");
                }

                targetProfileRow = row;
            }
            if (targetProfileRow == 0)
            {
                throw Invalid(
                    "Imported F+I declarations have no Windows x64 native TargetProfile.");
            }
            if (foreignInterfaceRows.Count != emissions.Count)
            {
                throw Invalid(
                    "Imported F+I declarations have an incomplete layout-row set.");
            }
            foreignInterfaceRows = [.. foreignInterfaceRows.Select(
                row => row with {
                    TargetProfileRow = checked((uint)targetProfileRow),
                })];
            var foreignInterfaceBytes
                = TrRmForeignNativeInterfaceTableWriter.Encode(
                    foreignInterfaceRows, interfaces, targetProfiles);
            SetSection(sections, 0x004C,
                checked((uint)foreignInterfaceRows.Count),
                foreignInterfaceBytes);

            if (foreignInterfaceContractRows.Count != 0)
            {
                var existingContractRows = ReadVariableRows(sections,
                    0x001B, 8, 4);
                var contractRows = new List<TrRmContractRow>(
                    existingContractRows.Count
                        + foreignInterfaceContractRows.Count);
                foreach (var existingRow in existingContractRows)
                {
                    var data = existingRow.Span;
                    contractRows.Add(new TrRmContractRow(
                        BinaryPrimitives.ReadUInt32LittleEndian(data),
                        BinaryPrimitives.ReadUInt16LittleEndian(
                            data[4..]),
                        data[8..].ToArray()));
                }
                contractRows.AddRange(foreignInterfaceContractRows);
                var predicateGraphCount = checked(
                    GetCount(sections, 0x0037)
                    + foreignInterfacePredicateGraphs.Count);
                var contractBytes
                    = TrRmContractTableWriter
                        .EncodeForeignInvocationDraft(contractRows,
                            typeDefs, methods, signatures, parameters,
                            typeSpecs, features, modules,
                            predicateGraphCount, schema);
                SetSection(sections, 0x001B,
                    checked((uint)finalContractCount), contractBytes);

                if (foreignInterfacePredicateGraphs.Count != 0)
                {
                    var predicateGraphRows = checked(
                        GetCount(sections, 0x0037)
                        + foreignInterfacePredicateGraphs.Count);
                    var predicateFootprintRows = checked(
                        GetCount(sections, 0x0038)
                        + foreignInterfacePredicateFootprints.Count);
                    var predicateRootRows = checked(
                        GetCount(sections, 0x0039)
                        + foreignInterfacePredicateRoots.Count);
                    var predicateNodeRows = checked(
                        GetCount(sections, 0x003B)
                        + foreignInterfacePredicateNodes.Count);
                    var predicateOperandRows = checked(
                        GetCount(sections, 0x003C)
                        + foreignInterfacePredicateOperands.Count);
                    var predicateGraphBytes
                        = TrRmPredicateGraphTableWriter.Encode(
                            foreignInterfacePredicateGraphs,
                            predicateNodeRows, predicateFootprintRows);
                    var predicateFootprintBytes
                        = TrRmPredicateReadFootprintTableWriter.Encode(
                            foreignInterfacePredicateFootprints,
                            predicateRootRows, 0);
                    var predicateRootBytes
                        = TrRmPredicateReadRootTableWriter.Encode(
                            foreignInterfacePredicateRoots, 0, 0, schema);
                    var predicateNodeBytes
                        = TrRmPredicateNodeTableWriter.Encode(
                            foreignInterfacePredicateNodes,
                            predicateOperandRows, typeSpecs, schema);
                    var operandCounts = new TrRmPredicateTableRowCounts(
                        predicateRootRows, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
                    var predicateOperandBytes
                        = TrRmPredicateNodeOperandTableWriter.Encode(
                            foreignInterfacePredicateOperands,
                            operandCounts, schema);
                    AppendRowsSection(sections, 0x0037,
                        checked((uint)predicateGraphRows),
                        predicateGraphBytes);
                    AppendRowsSection(sections, 0x0038,
                        checked((uint)predicateFootprintRows),
                        predicateFootprintBytes);
                    AppendRowsSection(sections, 0x0039,
                        checked((uint)predicateRootRows),
                        predicateRootBytes);
                    AppendRowsSection(sections, 0x003B,
                        checked((uint)predicateNodeRows),
                        predicateNodeBytes);
                    AppendRowsSection(sections, 0x003C,
                        checked((uint)predicateOperandRows),
                        predicateOperandBytes);
                }
            }
        }

        var finalImportBytes = TrRmImportTableWriter.Encode(importRows,
            stringTable, moduleRefs, typeDefs, methods, default, interfaces,
            associatedTypeDefs, fields, features, modules, schema);

        SetSection(sections, 0x0002, checked((uint)names.Count),
            rewrittenStrings);
        SetSection(sections, 0x0010, checked((uint)moduleRefRows.Count),
            moduleRefBytes);
        SetSection(sections, 0x0011,
            checked((uint)typeSpecRows.Count), typeSpecBytes);
        SetSection(sections, 0x0013, checked((uint)finalTypeDefCount),
            finalTypeDefBytes);
        if (!foreignNativeInterface || finalLayoutCount != 0)
        {
            SetSection(sections, 0x0015, checked((uint)finalLayoutCount),
                layoutBytes.ToArray());
        }
        SetSection(sections, 0x0016, checked((uint)finalMethodCount),
            finalMethodBytes);
        SetSection(sections, 0x0017, checked((uint)finalSignatureCount),
            finalSignatureBytes);
        SetSection(sections, 0x0018, checked((uint)finalParameterCount),
            parameterBytes.ToArray());
        SetSection(sections, 0x0019,
            checked((uint)finalGenericParameterCount),
            genericParameterBytes);
        SetSection(sections, 0x001A,
            checked((uint)constraintRows.Count), constraintBytes);
        SetSection(sections, 0x001C, checked((uint)importRows.Count),
            finalImportBytes);
        SetSection(sections, 0x0024,
            checked((uint)finalInterfaceMethodCount),
            finalInterfaceMethodBytes);
        SetSection(sections, 0x0025,
            checked((uint)(interfaceDefCount + interfaceDefRows.Count)),
            finalInterfaceDefBytes);
        SetSection(sections, 0x0027,
            checked((uint)finalAssociatedTypeCount),
            finalAssociatedTypeDefBytes);
        SetSection(sections, 0x0066,
            checked((uint)methodParameterLabelCount),
            methodParameterLabelBytes.Count == 0 ? []
                : [.. methodParameterLabelBytes]);

        sections.Sort(static (left, right) =>
            left.Kind.CompareTo(right.Kind));
        return TrRmArtifactWriter.Encode(sections, schema,
            standardSchemaIdentity: standardSchemaIdentity);
    }

    private static List<GenericImportedInterfaceEmission>
        CaptureImportedGenericInterfaceEmissions(BoundProgram program,
            TrRmArtifactSchema schema)
    {
        if (schema.UsesForeignNativeInterface())
        {
            var importedFni = new List<GenericImportedInterfaceEmission>();
            var selectors = new HashSet<(string ProducerIdentity,
                string ExportKey, string QualifiedName, uint TargetRequirement,
                uint RuntimePolicy, int ExpectedTypeDefRow)>();
            foreach (var definition in program.Interfaces)
            {
                if (definition.ForeignNativeInterface is null)
                {
                    throw Unsupported(
                        "F+I imported references cannot retain ordinary interface declarations.");
                }

                if (definition.ModuleImport is not BoundModuleImport import)
                {
                    continue;
                }

                ValidateImportedGenericInterface(definition, schema,
                    program);

                var expectedViews
                    = definition.ForeignNativeInterfaceExpectedViews;
                if (expectedViews.IsDefaultOrEmpty)
                {
                    if (!selectors.Add((import.ProducerIdentity,
                            import.ExportKey, definition.QualifiedName,
                            import.TargetRequirement, import.RuntimePolicy,
                            -1)))
                    {
                        throw Unsupported(
                            "F+I imported interface references contain a duplicate owner identity.");
                    }

                    importedFni.Add(new GenericImportedInterfaceEmission(
                        definition, import));
                    continue;
                }

                var providerMembersByKey = new Dictionary<string,
                    BoundInterfaceMemberDefinition>(
                        definition.Members.Length, StringComparer.Ordinal);
                foreach (var member in definition.Members)
                {
                    if (!providerMembersByKey.TryAdd(member.DeclarationKey,
                            member))
                    {
                        throw Invalid(
                            "An imported FNI provider has duplicate method ExportKeys.");
                    }

                    foreach (var alias in member.AdditionalDeclarationKeys)
                    {
                        if (!providerMembersByKey.TryAdd(alias, member))
                        {
                            throw Invalid(
                                "An imported FNI provider has conflicting method aliases.");
                        }
                    }
                }

                foreach (var expectedView in expectedViews)
                {
                    var expectedDefinition = expectedView.Definition;
                    if (expectedView.SourceTypeDefRow <= 0
                        || expectedDefinition.ModuleImport != import
                        || expectedDefinition.ForeignNativeInterface
                            is null)
                    {
                        throw Invalid(
                            "An imported FNI expected surface has an invalid owner identity.");
                    }

                    ValidateImportedGenericInterface(expectedDefinition,
                        schema, program);
                    ValidateForeignNativeInterfaceProviderContract(
                        expectedDefinition, definition,
                        providerMembersByKey);
                    if (!selectors.Add((import.ProducerIdentity,
                            import.ExportKey,
                            expectedDefinition.QualifiedName,
                            import.TargetRequirement, import.RuntimePolicy,
                            expectedView.SourceTypeDefRow)))
                    {
                        throw Invalid(
                            "An imported FNI expected surface is retained more than once.");
                    }

                    importedFni.Add(new GenericImportedInterfaceEmission(
                        expectedDefinition, import,
                        expectedView.SourceTypeDefRow));
                }

                if (!expectedViews.Any(expectedView =>
                        StringComparer.Ordinal.Equals(
                            expectedView.Definition.QualifiedName,
                            definition.QualifiedName)))
                {
                    if (!selectors.Add((import.ProducerIdentity,
                            import.ExportKey, definition.QualifiedName,
                            import.TargetRequirement, import.RuntimePolicy,
                            -1)))
                    {
                        throw Invalid(
                            "An imported FNI provider call target is retained more than once.");
                    }

                    importedFni.Add(new GenericImportedInterfaceEmission(
                        definition, import));
                }

                if (importedFni.Count
                    > SemanticVerifier.MaximumAnalysisVisits)
                {
                    throw Unsupported(
                        "Imported FNI expected views exceed the supported declaration limit.");
                }
            }

            return [.. importedFni.OrderBy(
                    static emission => emission.Import.ProducerIdentity,
                    StringComparer.Ordinal)
                .ThenBy(static emission => emission.Import.ExportKey,
                    StringComparer.Ordinal)
                .ThenBy(static emission => emission.Definition.QualifiedName,
                    StringComparer.Ordinal)
                .ThenBy(static emission => emission.ExpectedTypeDefRow)];
        }

        var required = new Dictionary<(
            string ProviderIdentity, string ExportKey, string QualifiedName,
            int Arity, uint TargetRequirement, uint RuntimePolicy),
            BoundModuleImport>();
        var remainingVisits = SemanticVerifier.MaximumAnalysisVisits;
        void Capture(BoundInterfaceType type, int depth)
        {
            if (type is null || depth > SemanticVerifier.MaximumDepth
                || --remainingVisits < 0)
            {
                throw Unsupported(
                    "Imported G1 interface constraints exceed the supported analysis limit.");
            }

            if (type.ModuleImport is BoundModuleImport moduleImport)
            {
                if (type.Kind != BoundInterfaceTypeKind.Nominal
                    || string.IsNullOrEmpty(type.NominalName)
                    || string.IsNullOrEmpty(moduleImport.ProducerIdentity)
                    || string.IsNullOrEmpty(moduleImport.ExportKey)
                    || moduleImport.ExportKey.Contains('\0',
                        StringComparison.Ordinal)
                    || !IsValidSourceGenericMethodExportKey(
                        moduleImport.ExportKey))
                {
                    throw Unsupported(
                        "An imported G1 interface constraint has no nominal provider identity.");
                }

                var arity = type.TypeArguments.IsDefault
                    ? 0 : type.TypeArguments.Length;
                _ = required.TryAdd((moduleImport.ProducerIdentity,
                    moduleImport.ExportKey, type.NominalName, arity,
                    moduleImport.TargetRequirement,
                    moduleImport.RuntimePolicy), moduleImport);
            }

            if (type.TypeArguments.IsDefaultOrEmpty)
            {
                return;
            }

            foreach (var argument in type.TypeArguments)
            {
                if (argument is null)
                {
                    throw Unsupported(
                        "An imported G1 interface constraint has an invalid type argument.");
                }

                if (argument.Kind == BoundInterfaceTypeArgumentKind.Type
                    && argument.Type is BoundInterfaceType argumentType)
                {
                    Capture(argumentType, depth + 1);
                }
            }
        }

        foreach (var definition in program.GenericMethodDefinitions)
        {
            if (definition.Formals.IsDefault)
            {
                throw Unsupported(
                    "A generic method has an invalid interface-constraint view.");
            }

            foreach (var formal in definition.Formals)
            {
                if (formal.InterfaceConstraints.IsDefault)
                {
                    throw Unsupported(
                        "A generic method has an invalid interface-constraint view.");
                }

                foreach (var constraint in formal.InterfaceConstraints)
                {
                    if (constraint is null)
                    {
                        throw Unsupported(
                            "A generic method has an invalid interface-constraint view.");
                    }

                    Capture(constraint, 0);
                }
                if (formal.AssociatedRelations.IsDefault)
                {
                    throw Unsupported(
                        "A generic method has an invalid associated-relation view.");
                }

                foreach (var relation in formal.AssociatedRelations)
                {
                    if (relation is null)
                    {
                        throw Unsupported(
                            "A generic method has an invalid associated-relation view.");
                    }

                    Capture(relation.AssociatedType.DeclaringInterface, 0);
                }
            }
        }
        void CaptureFormals(
            ImmutableArray<BoundGenericMethodFormal> formals)
        {
            if (formals.IsDefault)
            {
                throw Unsupported(
                    "A retained generic MethodDef has an invalid interface-constraint view.");
            }

            foreach (var formal in formals)
            {
                if (formal.InterfaceConstraints.IsDefault
                    || formal.AssociatedRelations.IsDefault)
                {
                    throw Unsupported(
                        "A retained generic MethodDef has an invalid interface-constraint view.");
                }

                foreach (var constraint in formal.InterfaceConstraints)
                {
                    if (constraint is null)
                    {
                        throw Unsupported(
                            "A retained generic MethodDef has an invalid interface-constraint view.");
                    }

                    Capture(constraint, 0);
                }

                foreach (var relation in formal.AssociatedRelations)
                {
                    if (relation is null)
                    {
                        throw Unsupported(
                            "A retained generic MethodDef has an invalid associated-relation view.");
                    }

                    Capture(relation.AssociatedType.DeclaringInterface, 0);
                }
            }
        }
        foreach (var root in program.RetainedGenericMethodRoots)
        {
            CaptureFormals(root.Formals);
            if (root.Binding is BoundGenericMethodImportBinding binding)
            {
                CaptureFormals(binding.Expected.Formals);
                CaptureFormals(binding.Provider.Formals);
            }
        }

        var result = new List<GenericImportedInterfaceEmission>(
            required.Count);
        foreach (var (key, import) in required.OrderBy(
                     static pair => pair.Key.ProviderIdentity,
                     StringComparer.Ordinal).ThenBy(
                     static pair => pair.Key.ExportKey,
                     StringComparer.Ordinal).ThenBy(
                     static pair => pair.Key.QualifiedName,
                     StringComparer.Ordinal).ThenBy(
                     static pair => pair.Key.Arity))
        {
            var matches = program.Interfaces.Where(definition =>
                definition.ModuleImport == import
                && StringComparer.Ordinal.Equals(definition.QualifiedName,
                    key.QualifiedName)
                && definition.Formals.Length == key.Arity).ToArray();
            if (matches.Length != 1)
            {
                throw Unsupported(
                    "An imported G1 interface constraint has no unique retained provider declaration.");
            }

            ValidateImportedGenericInterface(matches[0], schema, program);
            var definitionVisits = checked(1
                + matches[0].Formals.Length
                + matches[0].Members.Length
                + matches[0].Members.Sum(
                    static member => member.Parameters.Length));
            if (definitionVisits > remainingVisits)
            {
                throw Unsupported(
                    "Imported G1 interface constraints exceed the supported analysis limit.");
            }
            remainingVisits -= definitionVisits;
            result.Add(new GenericImportedInterfaceEmission(matches[0],
                import));
        }

        if (result.Count > SemanticVerifier.MaximumAnalysisVisits)
        {
            throw Unsupported(
                "Imported G1 interface constraints exceed the supported analysis limit.");
        }

        return result;
    }

    private static void ValidateImportedGenericInterface(
        BoundInterfaceDefinition definition, TrRmArtifactSchema schema,
        BoundProgram program)
    {
        if (schema.UsesForeignNativeInterface())
        {
            if (!definition.IsPublic
                || definition.Accessibility
                    != BoundInterfaceAccessibility.Public
                || definition.IsPartial
                || definition.ModuleImport is not BoundModuleImport import
                || string.IsNullOrEmpty(import.ProducerIdentity)
                || string.IsNullOrEmpty(import.ExportKey)
                || !IsValidSourceGenericMethodExportKey(import.ExportKey)
                || import.ExportKey.Contains('\0',
                    StringComparison.Ordinal)
                || !definition.Bases.IsEmpty
                || !definition.Formals.IsEmpty
                || !definition.Constraints.IsEmpty
                || definition.Members.IsDefaultOrEmpty
                || !ForeignNativeInterfaceContract.IsValidTable(
                    [definition], program.NativePointerTypes,
                    program.NativeFunctionPointerSignatures,
                    program.NativeCoreRegistration)
                || !NativeOutputContractValidator.IsValidInterfaceTable(
                    [definition], program.Structures,
                    program.NativePointerTypes,
                    program.NativeFunctionPointerSignatures,
                    program.EnumDefinitions, program.NativeCoreRegistration,
                    program.NativeOutputCoreRegistration))
            {
                throw Unsupported(
                    $"Imported ForeignNativeInterface '{definition.QualifiedName}' has an invalid retained contract.");
            }

            return;
        }

        if (!definition.IsPublic
            || definition.Accessibility
                != BoundInterfaceAccessibility.Public
            || definition.ModuleImport is not BoundModuleImport import
            || string.IsNullOrEmpty(definition.QualifiedName)
            || !definition.Bases.IsDefaultOrEmpty
            || !definition.AttributeTokens.IsDefaultOrEmpty
            || definition.ForeignNativeInterface is not null
            || definition.Formals.IsDefault
            || definition.Formals.Length > SemanticVerifier.MaximumArguments
            || definition.Constraints.IsDefault
            || definition.Members.IsDefault
            || definition.Members.Length
                > SemanticVerifier.MaximumAnalysisVisits)
        {
            throw Unsupported(
                $"Imported interface '{definition.QualifiedName}' is outside the G1L1 declaration subset.");
        }
        if (string.IsNullOrEmpty(import.ProducerIdentity)
            || string.IsNullOrEmpty(import.ExportKey)
            || import.ExportKey.Contains('\0', StringComparison.Ordinal)
            || !IsValidSourceGenericMethodExportKey(import.ExportKey))
        {
            throw Unsupported(
                $"Imported interface '{definition.QualifiedName}' has an invalid provider identity.");
        }

        var constraintsByFormal
            = new List<BoundInterfaceConstraint>?[definition.Formals.Length];
        foreach (var constraint in definition.Constraints)
        {
            if (constraint is null
                || !IsGenericWriteConstraint(constraint)
                || (uint)constraint.FormalOrdinal
                    >= (uint)definition.Formals.Length)
            {
                throw Unsupported(
                    $"Imported interface '{definition.QualifiedName}' has an unsupported generic constraint.");
            }

            var formalConstraints
                = constraintsByFormal[constraint.FormalOrdinal] ??= [];
            formalConstraints.Add(constraint);
        }

        var selfFormalCount = 0;
        for (var ordinal = 0; ordinal < definition.Formals.Length; ordinal++)
        {
            var formal = definition.Formals[ordinal];
            var formalConstraints = constraintsByFormal[ordinal];
            if (formal is null
                || formal.Ordinal != ordinal
                || formal.Kind != BoundInterfaceFormalKind.Type
                || string.IsNullOrEmpty(formal.Name)
                || formal.ConstantType is not null
                || formal.TypeDefault is not null
                || formal.ConstantDefault is not null
                || !formal.Attributes.IsDefaultOrEmpty
                || !formal.AttributeTokens.IsDefaultOrEmpty
                || formal.RequiresWritable
                    != (formalConstraints is { Count: > 0 })
                || formalConstraints is { Count: > 1 })
            {
                throw Unsupported(
                    $"Imported interface '{definition.QualifiedName}' has an unsupported owner formal.");
            }

            if (formal.IsSelf)
            {
                selfFormalCount++;
            }
        }
        if (selfFormalCount > 1)
        {
            throw Unsupported(
                $"Imported interface '{definition.QualifiedName}' declares multiple self formals.");
        }

        var memberExportKeys = new HashSet<string>(StringComparer.Ordinal);
        var memberNames = new HashSet<string>(StringComparer.Ordinal);
        var typeExportKey = definition.ModuleImport.ExportKey;
        for (var ordinal = 0; ordinal < definition.Members.Length; ordinal++)
        {
            var member = definition.Members[ordinal];
            if (member is null)
            {
                throw Unsupported(
                    $"Imported interface '{definition.QualifiedName}' has an invalid member.");
            }

            if (member.Ordinal != ordinal
                || member.Accessibility
                    != BoundInterfaceAccessibility.Public
                || string.IsNullOrEmpty(member.Name)
                || !memberNames.Add(member.Name)
                || string.IsNullOrEmpty(member.DeclarationKey)
                || !IsValidSourceGenericMethodExportKey(
                    member.DeclarationKey)
                || member.DeclarationKey.Contains('\0',
                    StringComparison.Ordinal)
                || StringComparer.Ordinal.Equals(member.DeclarationKey,
                    typeExportKey)
                || !memberExportKeys.Add(member.DeclarationKey))
            {
                throw Unsupported(
                    $"Imported interface '{definition.QualifiedName}' has an invalid or ambiguous member identity.");
            }
            foreach (var additionalKey
                in member.AdditionalDeclarationKeys)
            {
                if (string.IsNullOrEmpty(additionalKey)
                    || additionalKey.Contains('\0',
                        StringComparison.Ordinal)
                    || !IsValidSourceGenericMethodExportKey(additionalKey)
                    || StringComparer.Ordinal.Equals(additionalKey,
                        typeExportKey)
                    || !memberExportKeys.Add(additionalKey))
                {
                    throw Unsupported(
                        $"Imported interface '{definition.QualifiedName}' has an invalid or ambiguous member alias.");
                }
            }

            if (member.Kind == BoundInterfaceMemberKind.Callable)
            {
                if (member.IsStatic || member.IsExternal
                    || (member.RequiresMutableReceiver
                        && !schema.UsesMethodReceiverAccess())
                    || member.IsPartial || member.RequiresUnsafeCaller
                    || member.DeclaredRequiresUnsafeCaller
                    || member.DoesNotReturn || member.IsAsync
                    || member.HasBody
                    || member.NativeCallableConvention is not null
                    || !member.Attributes.IsDefaultOrEmpty
                    || !member.AttributeTokens.IsDefaultOrEmpty
                    || !member.ContractTokens.IsDefaultOrEmpty
                    || member.AdditionalDeclarationKeys.IsDefault
                    || !member.TypeParameters.IsDefaultOrEmpty
                    || !member.Constraints.IsDefaultOrEmpty
                    || !member.Dependencies.IsDefaultOrEmpty
                    || !member.Accessors.IsDefaultOrEmpty
                    || member.AssociatedDefaultType is not null
                    || member.AssociatedConstantType is not null
                    || member.AssociatedConstantDefault is not null
                    || member.Parameters.IsDefault
                    || member.Parameters.Length
                        > SemanticVerifier.MaximumArguments
                    || member.Parameters.Any(parameter =>
                        parameter is null
                        || string.IsNullOrEmpty(parameter.Name)
                        || parameter.Mode != BoundInterfaceParameterMode.Value
                        || !parameter.Attributes.IsDefaultOrEmpty
                        || !parameter.AttributeTokens.IsDefaultOrEmpty
                        || parameter.DefaultValue is not null
                        || parameter.DefaultExpression is not null
                        || parameter.HasPendingDefault
                        || !IsSupportedGenericInterfaceSignatureType(
                            parameter.Type, definition.Formals.Length,
                            isReturn: false))
                    || (member.ResultType is not null
                        && !IsSupportedGenericInterfaceSignatureType(
                            member.ResultType, definition.Formals.Length,
                            isReturn: true)))
                {
                    throw Unsupported(
                        $"Imported interface requirement '{definition.QualifiedName}.{member.Name}' is outside the G1L1 scalar/value signature subset.");
                }

                continue;
            }

            if (member.Kind != BoundInterfaceMemberKind.AssociatedType
                || !schema.UsesAssociatedRelationConstraints()
                || member.IsStatic || member.IsExternal || member.IsPartial
                || member.RequiresMutableReceiver
                || member.RequiresUnsafeCaller
                || member.DeclaredRequiresUnsafeCaller
                || member.DoesNotReturn || member.IsAsync || member.HasBody
                || member.NativeCallableConvention is not null
                || !member.Attributes.IsDefaultOrEmpty
                || !member.AttributeTokens.IsDefaultOrEmpty
                || !member.ContractTokens.IsDefaultOrEmpty
                || !member.TypeParameters.IsDefaultOrEmpty
                || !member.Constraints.IsDefaultOrEmpty
                || !member.Dependencies.IsDefaultOrEmpty
                || !member.Accessors.IsDefaultOrEmpty
                || !member.Parameters.IsEmpty
                || member.Parameters.IsDefault
                || member.ResultType is not null
                || member.AssociatedConstantType is not null
                || member.AssociatedConstantDefault is not null
                || (member.AssociatedDefaultType is not null
                    && !IsSupportedGenericInterfaceSignatureType(
                        member.AssociatedDefaultType,
                        definition.Formals.Length, isReturn: true)))
            {
                throw Unsupported(
                    $"Imported associated type '{definition.QualifiedName}.{member.Name}' is outside the CM1AR1 declaration subset.");
            }
        }
    }

    private static (int First, int Count)
        AddForeignNativeInterfaceMethodContracts(
        BoundInterfaceMemberDefinition member, int methodRow,
        int contractBaseCount, List<TrRmContractRow> contractRows,
        List<TrRmPredicateGraphRow> predicateGraphs,
        List<TrRmPredicateReadFootprintRow> predicateFootprints,
        List<TrRmPredicateReadRootRow> predicateRoots,
        List<TrRmPredicateNodeRow> predicateNodes,
        List<TrRmPredicateNodeOperandRow> predicateOperands,
        int predicateGraphBaseCount, int predicateFootprintBaseCount,
        int predicateRootBaseCount, int predicateNodeBaseCount,
        int predicateOperandBaseCount)
    {
        if (member.NativeCallableConvention
                is not NativeCallingConvention convention)
        {
            throw Unsupported(
                "F+I member contracts require a normalized convention.");
        }

        var firstContract = checked(contractBaseCount + contractRows.Count
            + 1);
        var firstContractIndex = contractRows.Count;
        var methodOwner = 0x1600_0000u | checked((uint)methodRow);
        var effectsPayload = new byte[sizeof(uint)];
        var effectsMask = 4u | (member.DoesNotReturn ? 2u : 0u);
        BinaryPrimitives.WriteUInt32LittleEndian(effectsPayload,
            effectsMask);
        contractRows.Add(new TrRmContractRow(methodOwner, 1,
            effectsPayload));
        contractRows.Add(new TrRmContractRow(methodOwner, 0x0C,
            StandardNativeCallableContractPayload(convention)));
        _ = AddForeignNativeInterfaceDependencyContracts(
            member.Dependencies, methodOwner, contractRows);

        if (!member.NativeOutputContracts.IsDefaultOrEmpty)
        {
            var selectors = new SortedSet<byte>();
            foreach (var output in member.NativeOutputContracts)
            {
                _ = selectors.Add((byte)output.SuccessSelector);
                _ = selectors.Add((byte)output.ComplementSelector);
            }

            var graphBySelector = new Dictionary<byte, int>();
            uint phaseOrdinal = 0;
            foreach (var selector in selectors)
            {
                var graphRow = checked(predicateGraphBaseCount
                    + predicateGraphs.Count + 1);
                var footprintRow = checked(predicateFootprintBaseCount
                    + predicateFootprints.Count + 1);
                var rootRow = checked(predicateRootBaseCount
                    + predicateRoots.Count + 1);
                var nodeRow = checked(predicateNodeBaseCount
                    + predicateNodes.Count + 1);
                var operandRow = checked(predicateOperandBaseCount
                    + predicateOperands.Count + 1);
                predicateGraphs.Add(new TrRmPredicateGraphRow(nodeRow,
                    footprintRow));
                predicateFootprints.Add(
                    new TrRmPredicateReadFootprintRow(rootRow, 1, 0, 0));
                predicateRoots.Add(new TrRmPredicateReadRootRow(5, 0));
                predicateNodes.Add(new TrRmPredicateNodeRow(selector,
                    0xF200_0001, checked((uint)operandRow), 1));
                predicateOperands.Add(new TrRmPredicateNodeOperandRow(1,
                    checked((uint)rootRow)));
                var predicatePayload = new byte[12];
                predicatePayload[0] = 3;
                BinaryPrimitives.WriteUInt32LittleEndian(
                    predicatePayload.AsSpan(4), phaseOrdinal++);
                BinaryPrimitives.WriteUInt32LittleEndian(
                    predicatePayload.AsSpan(8), checked((uint)graphRow));
                contractRows.Add(new TrRmContractRow(methodOwner, 0x0B,
                    predicatePayload));
                graphBySelector.Add(selector, graphRow);
            }

            foreach (var output in member.NativeOutputContracts)
            {
                var resultType = member.ResultType
                    is { Kind: BoundInterfaceTypeKind.Primitive }
                        ? member.ResultType.PrimitiveType
                        : throw Unsupported(
                            "F+I NativeOutput requires a scalar result.");
                var resultCondition
                    = (output.SuccessSelector, resultType) switch {
                        (NativeOutputSelector.NonZero,
                            PrimitiveType.UInt32)
                            => DirectNativeImportContract
                                .UInt32NonZeroResultCondition,
                        (NativeOutputSelector.NonZero,
                            PrimitiveType.Int32)
                            => DirectNativeImportContract
                                .Int32NonZeroResultCondition,
                        (NativeOutputSelector.NonNegative,
                            PrimitiveType.Int32)
                            => DirectNativeImportContract
                                .Int32NonNegativeResultCondition,
                        _ => throw Unsupported(
                            "F+I NativeOutput has an unsupported result selector."),
                    };
                contractRows.Add(new TrRmContractRow(methodOwner, 0x12,
                    NativeOutputPayload(output.FormalOrdinal,
                        resultCondition, 1)));
                AddOutputState((byte)output.SuccessState,
                    output.FormalOrdinal,
                    graphBySelector[(byte)output.SuccessSelector]);
                AddOutputState((byte)output.ComplementState,
                    output.FormalOrdinal,
                    graphBySelector[(byte)output.ComplementSelector]);
                foreach (var dependency in output.Dependencies)
                {
                    var dependencyPayload = new byte[12];
                    dependencyPayload[0] = dependency.DestinationKind;
                    dependencyPayload[1] = (byte)dependency.SourceKind;
                    dependencyPayload[2] = (byte)dependency.Relationship;
                    dependencyPayload[3] = (byte)dependency.Phase;
                    BinaryPrimitives.WriteUInt32LittleEndian(
                        dependencyPayload.AsSpan(4),
                        checked((uint)dependency.DestinationOrdinal));
                    BinaryPrimitives.WriteUInt32LittleEndian(
                        dependencyPayload.AsSpan(8),
                        checked((uint)dependency.SourceOrdinal));
                    contractRows.Add(new TrRmContractRow(methodOwner, 4,
                        dependencyPayload));
                }
            }
        }

        return (firstContract,
            checked(contractRows.Count - firstContractIndex));

        void AddOutputState(byte state, int formalOrdinal, int graphRow)
        {
            var payload = new byte[12];
            BinaryPrimitives.WriteUInt32LittleEndian(payload,
                checked((uint)formalOrdinal));
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(4),
                checked((uint)graphRow));
            payload[8] = state;
            contractRows.Add(new TrRmContractRow(methodOwner, 2, payload));
        }
    }

    private static ImmutableArray<int>
        GetForeignNativeFunctionPointerSignatureIndexes(
        IEnumerable<BoundInterfaceDefinition> definitions,
        BoundProgram program, ImmutableArray<int> structureIndexes)
    {
        var signatures = program.NativeFunctionPointerSignatures;
        var pointerTypes = program.NativePointerTypes;
        var states = new byte[signatures.Length];
        var retained = new SortedSet<int>();

        void Visit(PrimitiveType type, int depth)
        {
            if (depth > SemanticVerifier.MaximumDepth)
            {
                throw Unsupported(
                    "The ForeignNativeInterface function-pointer type closure exceeds the supported depth.");
            }

            if (NativePointerTypes.IsType(type))
            {
                var pointerIndex = NativePointerTypes.Index(type);
                if ((uint)pointerIndex >= (uint)pointerTypes.Length)
                {
                    throw Unsupported(
                        "A ForeignNativeInterface signature references an invalid native pointer type.");
                }

                Visit(pointerTypes[pointerIndex].PointeeType, depth + 1);
                return;
            }

            if (!NativeFunctionPointerTypes.IsType(type))
            {
                return;
            }

            var signatureIndex = NativeFunctionPointerTypes.Index(type);
            if ((uint)signatureIndex >= (uint)signatures.Length)
            {
                throw Unsupported(
                    "A ForeignNativeInterface signature references an invalid function-pointer anchor.");
            }

            if (states[signatureIndex] == 1)
            {
                throw Unsupported(
                    "A ForeignNativeInterface function-pointer signature contains a cycle.");
            }
            if (states[signatureIndex] == 2)
            {
                return;
            }

            states[signatureIndex] = 1;
            var signature = signatures[signatureIndex];
            Visit(signature.ReturnType, depth + 1);
            foreach (var parameterType in signature.ParameterTypes)
            {
                Visit(parameterType, depth + 1);
            }
            states[signatureIndex] = 2;
            _ = retained.Add(signatureIndex);
        }

        foreach (var definition in definitions)
        {
            foreach (var member in definition.Members)
            {
                if (member.ResultType is BoundInterfaceType resultType)
                {
                    Visit(resultType.PrimitiveType, 0);
                }
                foreach (var parameter in member.Parameters)
                {
                    Visit(parameter.Type.PrimitiveType, 0);
                }
            }
        }

        foreach (var structureIndex in structureIndexes.IsDefault
                     ? ImmutableArray<int>.Empty : structureIndexes)
        {
            if ((uint)structureIndex >= (uint)program.Structures.Length)
            {
                throw Unsupported(
                    "A ForeignNativeInterface aggregate references an invalid structure.");
            }

            foreach (var field in program.Structures[structureIndex].Fields)
            {
                if (field is null)
                {
                    throw Unsupported(
                        "A ForeignNativeInterface aggregate contains a missing field.");
                }

                Visit(field.Type, 0);
            }
        }

        return [.. retained];
    }

    private static void MapForeignNativeInterfaceStructureTypeDefRows(
        BoundProgram program,
        ImmutableArray<int> structureIndexes,
        TrRmStringTableReader strings,
        ReadOnlySpan<byte> moduleBytes, int moduleRefCount,
        byte[] typeDefBytes, int typeDefCount, int methodCount,
        int sourceFileCount, int fieldCount, int genericParameterCount,
        int layoutCount, int contractCount, int featureCount,
        TrRmArtifactSchema schema,
        Dictionary<int, int> structureTypeDefRows)
    {
        if (structureIndexes.IsDefaultOrEmpty)
        {
            return;
        }
        if (structureIndexes.Length > typeDefCount)
        {
            throw Invalid(
                "The F+I structure TypeDef range is incomplete.");
        }

        var modules = new TrRmModuleTableReader(moduleBytes, 1, strings,
            moduleRefCount, typeDefCount, methodCount, sourceFileCount,
            featureCount);
        var typeDefs = new TrRmTypeDefTableReader(typeDefBytes,
            typeDefCount, strings, modules, fieldCount,
            genericParameterCount, layoutCount, contractCount, schema);
        for (var structureOrdinal = 0;
            structureOrdinal < structureIndexes.Length; structureOrdinal++)
        {
            var structureIndex = structureIndexes[structureOrdinal];
            if ((uint)structureIndex >= (uint)program.Structures.Length)
            {
                throw Invalid(
                    "The F+I structure index is outside the checked program.");
            }

            var typeDefRow = structureOrdinal + 1;
            var structure = program.Structures[structureIndex];
            var namespaceSeparator = structure.Name.LastIndexOf('.',
                StringComparison.Ordinal);
            var namespaceName = namespaceSeparator < 0
                ? string.Empty
                : structure.Name[..namespaceSeparator];
            var name = namespaceSeparator < 0
                ? structure.Name
                : structure.Name[(namespaceSeparator + 1)..];
            if (typeDefs.GetKind(typeDefRow) != 1
                || !StringComparer.Ordinal.Equals(
                    typeDefs.GetNamespace(typeDefRow), namespaceName)
                || !StringComparer.Ordinal.Equals(
                    typeDefs.GetName(typeDefRow), name)
                || !structureTypeDefRows.TryAdd(structureIndex,
                    typeDefRow))
            {
                throw Invalid(
                    "The F+I structure TypeDef map differs from its ordered closure.");
            }
        }
    }

    private static void AddForeignNativeFunctionPointerAnchors(
        BoundProgram program, ImmutableArray<int> signatureIndexes,
        IReadOnlyDictionary<int, int> anchorMethodRows,
        Func<PrimitiveType, uint> typeSpecRef,
        int methodCount, int signatureCount, int parameterCount,
        int contractCount, ref int emittedMethods, ref int emittedSignatures,
        ref int emittedParameters, List<(string Text, int Encoding)> names,
        List<byte> methodBytes, List<byte> signatureBytes,
        List<byte> parameterBytes, List<TrRmContractRow> contractRows,
        List<TrRmPredicateGraphRow> predicateGraphs,
        List<TrRmPredicateReadFootprintRow> predicateFootprints,
        List<TrRmPredicateReadRootRow> predicateRoots,
        List<TrRmPredicateNodeRow> predicateNodes,
        List<TrRmPredicateNodeOperandRow> predicateOperands,
        int predicateGraphBaseCount, int predicateFootprintBaseCount,
        int predicateRootBaseCount, int predicateNodeBaseCount,
        int predicateOperandBaseCount, TrRmArtifactSchema schema)
    {
        var usedNames = names.Select(static name => name.Text)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var signatureIndex in signatureIndexes)
        {
            var methodRow = checked(methodCount + emittedMethods + 1);
            if (!anchorMethodRows.TryGetValue(signatureIndex,
                    out var expectedMethodRow)
                || methodRow != expectedMethodRow)
            {
                throw Invalid(
                    "The Standard FNI function-pointer anchor row order changed during emission.");
            }

            var signature
                = program.NativeFunctionPointerSignatures[signatureIndex];
            if (!signature.NativeParameters.IsDefaultOrEmpty
                || signature.EffectsMask is not (0 or 2 or 4 or 6)
                || signature.Selectors.IsDefault
                || signature.OutputStates.IsDefault
                || signature.Dependencies.IsDefault
                || signature.Selectors.Any(static selector =>
                    selector.PhaseTag != 3 || selector.NodeKind is < 1 or > 6))
            {
                throw Unsupported(
                    "A Standard FNI function-pointer anchor contains an unselected contract.");
            }

            var signatureRow
                = checked(signatureCount + emittedSignatures + 1);
            var firstParameter = signature.ParameterTypes.IsEmpty
                ? 0
                : checked(parameterCount + emittedParameters + 1);
            for (var ordinal = 0;
                ordinal < signature.ParameterTypes.Length; ordinal++)
            {
                AppendUInt32(parameterBytes, checked((uint)signatureRow));
                AppendUInt32(parameterBytes, checked((uint)ordinal));
                AppendUInt32(parameterBytes,
                    typeSpecRef(signature.ParameterTypes[ordinal]));
                AppendUInt32(parameterBytes, 0);
                emittedParameters++;
            }

            AppendUInt32(signatureBytes,
                typeSpecRef(signature.ReturnType));
            AppendUInt32(signatureBytes, 0);
            AppendUInt32(signatureBytes, checked((uint)firstParameter));
            AppendUInt32(signatureBytes,
                checked((uint)signature.ParameterTypes.Length));
            AppendUInt32(signatureBytes, 0);
            AppendUInt32(signatureBytes, 0);

            var owner = 0x1600_0000u | checked((uint)methodRow);
            var firstContractIndex = contractRows.Count;
            if (signature.EffectsMask != 0)
            {
                var effectsPayload = new byte[sizeof(uint)];
                BinaryPrimitives.WriteUInt32LittleEndian(effectsPayload,
                    signature.EffectsMask);
                contractRows.Add(new TrRmContractRow(owner, 1,
                    effectsPayload));
            }

            var graphRowsByOrdinal = new Dictionary<uint, int>(
                signature.Selectors.Length);
            foreach (var selector in signature.Selectors.OrderBy(
                         static selector => selector.PhaseLocalOrdinal))
            {
                var graphRow = checked(predicateGraphBaseCount
                    + predicateGraphs.Count + 1);
                var footprintRow = checked(predicateFootprintBaseCount
                    + predicateFootprints.Count + 1);
                var rootRow = checked(predicateRootBaseCount
                    + predicateRoots.Count + 1);
                var nodeRow = checked(predicateNodeBaseCount
                    + predicateNodes.Count + 1);
                var operandRow = checked(predicateOperandBaseCount
                    + predicateOperands.Count + 1);
                predicateGraphs.Add(new TrRmPredicateGraphRow(nodeRow,
                    footprintRow));
                predicateFootprints.Add(
                    new TrRmPredicateReadFootprintRow(rootRow, 1, 0, 0));
                predicateRoots.Add(new TrRmPredicateReadRootRow(5, 0));
                predicateNodes.Add(new TrRmPredicateNodeRow(
                    selector.NodeKind, 0xF200_0001,
                    checked((uint)operandRow), 1));
                predicateOperands.Add(new TrRmPredicateNodeOperandRow(1,
                    checked((uint)rootRow)));
                var payload = new byte[12];
                payload[0] = 3;
                BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(4),
                    selector.PhaseLocalOrdinal);
                BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(8),
                    checked((uint)graphRow));
                contractRows.Add(new TrRmContractRow(owner, 0x0B,
                    payload));
                graphRowsByOrdinal.Add(selector.PhaseLocalOrdinal,
                    graphRow);
            }

            contractRows.Add(new TrRmContractRow(owner, 0x10,
                NativeAbiPayload(3)));
            foreach (var outputState in signature.OutputStates)
            {
                if (!graphRowsByOrdinal.TryGetValue(
                        outputState.SelectorOrdinal, out var graphRow))
                {
                    throw Invalid(
                        "A Standard FNI function-pointer output state has no selector graph.");
                }

                var payload = new byte[12];
                BinaryPrimitives.WriteUInt32LittleEndian(payload,
                    outputState.FormalOrdinal);
                BinaryPrimitives.WriteUInt32LittleEndian(
                    payload.AsSpan(4), checked((uint)graphRow));
                payload[8] = outputState.State;
                contractRows.Add(new TrRmContractRow(owner, 2, payload));
            }

            foreach (var dependency in signature.Dependencies)
            {
                var payload = new byte[12];
                payload[0] = dependency.DestinationKind;
                payload[1] = dependency.SourceKind;
                payload[2] = dependency.Relationship;
                payload[3] = dependency.Phase;
                BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(4),
                    dependency.DestinationOrdinal);
                BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(8),
                    dependency.SourceOrdinal);
                contractRows.Add(new TrRmContractRow(owner, 4, payload));
            }

            var contractFirst = checked(contractCount
                + firstContractIndex + 1);
            var contractCountForMethod
                = contractRows.Count - firstContractIndex;
            var nameOrdinal = signatureIndex + 1;
            var name = $"__trrm_fni_fnptr_anchor_{nameOrdinal}";
            while (!usedNames.Add(name))
            {
                name = $"__trrm_fni_fnptr_anchor_{++nameOrdinal}";
            }

            var nameStringRow = names.Count + 1;
            names.Add((name, 1));
            AppendUInt32(methodBytes, 0);
            AppendUInt32(methodBytes, checked((uint)nameStringRow));
            AppendUInt32(methodBytes, checked((uint)signatureRow));
            AppendUInt32(methodBytes, 0);
            AppendUInt32(methodBytes, checked((uint)contractFirst));
            AppendUInt32(methodBytes,
                checked((uint)contractCountForMethod));
            AppendUInt32(methodBytes, 0);
            emittedMethods++;
            emittedSignatures++;
        }

        _ = schema;
    }

    private static int AddForeignNativeInterfaceDependencyContracts(
        ImmutableArray<BoundDependencyContract> dependencies,
        uint methodOwner, List<TrRmContractRow> contractRows)
    {
        if (dependencies.IsDefault)
        {
            throw Unsupported(
                "F+I member Dependency contracts are uninitialized.");
        }

        var rows = new List<TrRmDependencyContract>();
        var refinements = new List<TrRmDependencyContract>();
        var basePhasesBySource = new Dictionary<(
            BoundDependencyDestination Destination, int? DestinationOrdinal,
            BoundDependencySource Source), List<byte>>();
        foreach (var dependency in dependencies)
        {
            if (dependency is null || dependency.Sources.IsDefault)
            {
                throw Unsupported(
                    "F+I member Dependency contracts are uninitialized.");
            }

            if (dependency.Destination
                    == BoundDependencyDestination.InitializedOutput
                || dependency.Relation is not (
                    BoundDependencyRelation.DependsOn
                    or BoundDependencyRelation.Captures))
            {
                continue;
            }

            var phase = dependency.Relation switch {
                BoundDependencyRelation.DependsOn
                    when dependency.Destination
                        == BoundDependencyDestination.Result => (byte)1,
                BoundDependencyRelation.Captures => (byte)2,
                _ => throw Unsupported(
                    "F+I member Dependency destination/phase is not representable."),
            };
            foreach (var source in dependency.Sources)
            {
                var key = (dependency.Destination,
                    dependency.DestinationOrdinal, source);
                if (!basePhasesBySource.TryGetValue(key, out var phases))
                {
                    phases = [];
                    basePhasesBySource.Add(key, phases);
                }

                phases.Add(phase);
            }
        }

        foreach (var dependency in dependencies)
        {
            if (dependency is null || dependency.Sources.IsDefault)
            {
                throw Unsupported(
                    "F+I member Dependency contracts are uninitialized.");
            }

            var (destinationKind, destinationOrdinal) =
                dependency.Destination switch {
                    BoundDependencyDestination.Result
                        when dependency.DestinationOrdinal is null
                        => ((byte)1, 0u),
                    BoundDependencyDestination.InitializedOutput
                        when dependency.DestinationOrdinal is int output
                            && output >= 0
                        => ((byte)2, checked((uint)output)),
                    BoundDependencyDestination.RetainedFormal
                        when dependency.DestinationOrdinal is int formal
                            && formal >= 0
                        => ((byte)3, checked((uint)formal)),
                    BoundDependencyDestination.Receiver
                        when dependency.DestinationOrdinal is null
                        => ((byte)4, 0u),
                    _ => throw Unsupported(
                        "F+I member Dependency destination is not representable."),
                };
            if (destinationKind == 2)
            {
                continue;
            }

            if (dependency.Relation is BoundDependencyRelation.DependsOn
                or BoundDependencyRelation.Captures)
            {
                var phase = dependency.Relation switch {
                    BoundDependencyRelation.DependsOn
                        when destinationKind is 1 => (byte)1,
                    BoundDependencyRelation.Captures => (byte)2,
                    _ => throw Unsupported(
                        "F+I member Dependency destination/phase is not representable."),
                };
                if (dependency.Sources.IsEmpty)
                {
                    rows.Add(new TrRmDependencyContract(destinationKind, 0,
                        0, phase, destinationOrdinal, 0));
                    continue;
                }

                foreach (var source in dependency.Sources)
                {
                    var (sourceKind, sourceOrdinal)
                        = GetForeignNativeInterfaceDependencySource(source);
                    rows.Add(new TrRmDependencyContract(destinationKind,
                        sourceKind, 3, phase, destinationOrdinal,
                        sourceOrdinal));
                }

                continue;
            }

            var relationship = dependency.Relation switch {
                BoundDependencyRelation.BorrowSourceStorage => (byte)1,
                BoundDependencyRelation.ForwardsBacking => (byte)2,
                _ => throw Unsupported(
                    "F+I member Dependency relation is not representable."),
            };
            if (dependency.Sources.IsEmpty)
            {
                throw Unsupported(
                    "F+I member Dependency refinements require sources.");
            }

            foreach (var source in dependency.Sources)
            {
                var (sourceKind, sourceOrdinal)
                    = GetForeignNativeInterfaceDependencySource(source);
                if (!basePhasesBySource.TryGetValue((
                        dependency.Destination,
                        dependency.DestinationOrdinal, source),
                        out var phases) || phases.Count == 0)
                {
                    throw Unsupported(
                        "F+I member Dependency refinement does not refine a declared source.");
                }

                foreach (var phase in phases)
                {
                    refinements.Add(new TrRmDependencyContract(
                        destinationKind, sourceKind, relationship, phase,
                        destinationOrdinal, sourceOrdinal));
                }
            }
        }

        var rowIndexes = new Dictionary<(byte DestinationKind,
            uint DestinationOrdinal, byte Phase, byte SourceKind,
            uint SourceOrdinal), int>();
        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            var key = (row.DestinationKind, row.DestinationOrdinal,
                row.Phase, row.SourceKind, row.SourceOrdinal);
            if (!rowIndexes.TryAdd(key, index))
            {
                throw Unsupported(
                    "F+I member Dependency contracts contain a duplicate occurrence key.");
            }
        }

        foreach (var refinement in refinements)
        {
            var key = (refinement.DestinationKind,
                refinement.DestinationOrdinal, refinement.Phase,
                refinement.SourceKind, refinement.SourceOrdinal);
            if (!rowIndexes.TryGetValue(key, out var rowIndex)
                || rows[rowIndex].Relationship != 3)
            {
                throw Unsupported(
                    "F+I member Dependency refinements conflict or do not refine a declared source.");
            }

            rows[rowIndex] = rows[rowIndex] with {
                Relationship = refinement.Relationship,
            };
        }

        rows.Sort(static (left, right) => {
            var comparison = left.DestinationKind.CompareTo(
                right.DestinationKind);
            if (comparison != 0)
            {
                return comparison;
            }
            comparison = left.DestinationOrdinal.CompareTo(
                right.DestinationOrdinal);
            if (comparison != 0)
            {
                return comparison;
            }
            comparison = left.Phase.CompareTo(right.Phase);
            if (comparison != 0)
            {
                return comparison;
            }
            comparison = left.SourceKind.CompareTo(right.SourceKind);
            return comparison != 0
                ? comparison
                : left.SourceOrdinal.CompareTo(right.SourceOrdinal);
        });

        var keys = new HashSet<(byte DestinationKind,
            uint DestinationOrdinal, byte Phase, byte SourceKind,
            uint SourceOrdinal)>();
        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            if (!keys.Add((row.DestinationKind, row.DestinationOrdinal,
                    row.Phase, row.SourceKind, row.SourceOrdinal)))
            {
                throw Unsupported(
                    "F+I member Dependency contracts contain a duplicate occurrence key.");
            }

            if (row.SourceKind == 0
                && index + 1 < rows.Count
                && rows[index + 1].DestinationKind == row.DestinationKind
                && rows[index + 1].DestinationOrdinal
                    == row.DestinationOrdinal
                && rows[index + 1].Phase == row.Phase)
            {
                throw Unsupported(
                    "F+I member Dependency empty-source rows cannot coexist with sources.");
            }
        }

        foreach (var row in rows)
        {
            var payload = new byte[12];
            payload[0] = row.DestinationKind;
            payload[1] = row.SourceKind;
            payload[2] = row.Relationship;
            payload[3] = row.Phase;
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(4),
                row.DestinationOrdinal);
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(8),
                row.SourceOrdinal);
            contractRows.Add(new TrRmContractRow(methodOwner, 4, payload));
        }

        return rows.Count;

        static (byte Kind, uint Ordinal)
            GetForeignNativeInterfaceDependencySource(
            BoundDependencySource source)
            => source.Kind switch {
                BoundDependencySourceKind.Formal
                    when source.FormalOrdinal is int formalOrdinal
                        && formalOrdinal >= 0
                    => (1, checked((uint)formalOrdinal)),
                BoundDependencySourceKind.Receiver
                    when source.FormalOrdinal is null => (2, 0),
                _ => throw Unsupported(
                    "F+I member Dependency source is not representable."),
            };
    }

    private static List<GenericInterfaceEmission>
        CaptureGenericInterfaceEmissions(BoundProgram program,
            IReadOnlyList<TrRmSourceTypeExport> typeExports,
            TrRmArtifactSchema schema)
    {
        var foreignNativeInterface = schema.UsesForeignNativeInterface();
        var result = new List<GenericInterfaceEmission>(typeExports.Count);
        var selectors = new HashSet<(string Namespace, string Name, int Arity)>();
        foreach (var export in typeExports)
        {
            if (export.GenericArity < 0
                || !export.EnumMemberDeclarationKeys.IsDefault
                || !export.FieldExports.IsDefaultOrEmpty
                || export.InterfaceMethodExports.IsDefault
                    || export.Namespace is null
                    || export.Namespace.Contains('\0',
                        StringComparison.Ordinal)
                || string.IsNullOrEmpty(export.Name)
                || string.IsNullOrEmpty(export.ExportKey)
                || !IsValidSourceGenericMethodExportKey(export.ExportKey)
                || export.ExportKey.Contains('\0', StringComparison.Ordinal)
                || !selectors.Add((export.Namespace, export.Name,
                    export.GenericArity)))
            {
                throw new ArgumentException(
                    "G1 TypeDef mappings require unique interface selectors, nonnegative arity, and valid interface ExportKeys.",
                    nameof(typeExports));
            }

            var matching = program.Interfaces.Where(definition =>
                StringComparer.Ordinal.Equals(
                    GetQualifiedInterfaceNamespace(definition.QualifiedName),
                    export.Namespace)
                && StringComparer.Ordinal.Equals(
                    GetQualifiedInterfaceName(definition.QualifiedName),
                    export.Name)
                && definition.Formals.Length == export.GenericArity
                && definition.ModuleImport is null).ToArray();
            if (matching.Length != 1 || !matching[0].IsPublic)
            {
                throw new ArgumentException(
                    "A G1 TypeDef mapping must select one public local source interface of the exact name and arity.",
                    nameof(typeExports));
            }

            var definition = matching[0];
            var methodMembers = definition.Members
                .Where(static member =>
                    member.Kind == BoundInterfaceMemberKind.Callable)
                .ToArray();
            var associatedTypeMembers = definition.Members
                .Where(static member =>
                    member.Kind == BoundInterfaceMemberKind.AssociatedType)
                .ToArray();
            if (!definition.Bases.IsEmpty
                || definition.Constraints.IsDefault
                || definition.Constraints.Any(constraint =>
                    constraint is null
                    || !IsGenericWriteConstraint(constraint)
                    || !schema.UsesGenericWriteRequirements()
                    || (uint)constraint.FormalOrdinal
                        >= (uint)definition.Formals.Length
                    || definition.Formals[constraint.FormalOrdinal].Kind
                        != BoundInterfaceFormalKind.Type)
                || (!foreignNativeInterface
                    && !definition.AttributeTokens.IsDefaultOrEmpty)
                || (foreignNativeInterface
                    ? definition.ForeignNativeInterface is null
                        || definition.Accessibility
                            != BoundInterfaceAccessibility.Public
                        || definition.IsPartial
                    : definition.ForeignNativeInterface is not null)
                || definition.Formals.IsDefault
                || definition.Formals.Length
                    > SemanticVerifier.MaximumArguments
                || (foreignNativeInterface
                    && definition.Formals.Length != 0)
                || definition.Members.IsDefault
                || methodMembers.Length + associatedTypeMembers.Length
                    != definition.Members.Length
                || (foreignNativeInterface
                    && associatedTypeMembers.Length != 0)
                || definition.Formals.Any(static formal =>
                    formal.Kind != BoundInterfaceFormalKind.Type
                    || formal.ConstantType is not null
                    || formal.TypeDefault is not null
                    || formal.ConstantDefault is not null
                    || !formal.Attributes.IsDefaultOrEmpty
                    || !formal.AttributeTokens.IsDefaultOrEmpty))
            {
                throw Unsupported(
                    $"Interface '{definition.QualifiedName}' is outside the G1 type-only, unconstrained declaration subset.");
            }
            for (var ordinal = 0; ordinal < definition.Formals.Length;
                ordinal++)
            {
                if (definition.Formals[ordinal].Ordinal != ordinal)
                {
                    throw new InvalidDataException(
                        "A bound interface formal range is not contiguous.");
                }
            }
            if (export.InterfaceMethodExports.Length
                != methodMembers.Length)
            {
                throw new ArgumentException(
                    "Every directly declared InterfaceMethod needs one source-anchored ExportKey.",
                    nameof(typeExports));
            }
            var associatedTypeExports
                = export.InterfaceAssociatedTypeExports.IsDefault
                    ? [] : export.InterfaceAssociatedTypeExports;
            if (associatedTypeExports.Length
                != associatedTypeMembers.Length)
            {
                throw new ArgumentException(
                    "Each direct AssociatedTypeDef requires one source-anchored ExportKey under CM1AR1.",
                    nameof(typeExports));
            }

            var methodExportOrdinal = 0;
            var associatedTypeExportOrdinal = 0;
            for (var ordinal = 0; ordinal < definition.Members.Length;
                ordinal++)
            {
                var member = definition.Members[ordinal];
                if (member.Ordinal != ordinal)
                {
                    throw new InvalidDataException(
                        "A bound interface member range is not declaration-ordered.");
                }

                if (member.Kind == BoundInterfaceMemberKind.Callable)
                {
                    var memberExport
                        = export.InterfaceMethodExports[
                            methodExportOrdinal++];
                    if (member.SourceInputOrdinal
                            != memberExport.SourceInputOrdinal
                        || member.SourceDeclarationSpan
                            != memberExport.DeclarationSpan
                        || !IsValidInterfaceMemberExport(
                            memberExport.SourceInputOrdinal,
                            memberExport.DeclarationSpan,
                            memberExport.ExportKey, program))
                    {
                        throw new ArgumentException(
                            "An InterfaceMethod mapping must select the exact directly declared requirement by source input and full declaration span.",
                            nameof(typeExports));
                    }

                    if (member.Accessibility
                        != BoundInterfaceAccessibility.Public
                        || member.IsStatic || member.IsExternal
                        || (member.RequiresMutableReceiver
                            && !foreignNativeInterface
                            && !schema.UsesMethodReceiverAccess())
                        || member.IsPartial || member.IsAsync
                        || member.HasBody
                        || (!foreignNativeInterface
                            && (member.RequiresUnsafeCaller
                                || member.DeclaredRequiresUnsafeCaller
                                || member.NativeCallableConvention is not null
                                || member.DoesNotReturn))
                        || (foreignNativeInterface
                            && (!member.RequiresUnsafeCaller
                                || !member.DeclaredRequiresUnsafeCaller
                                || member.NativeCallableConvention
                                    is not (NativeCallingConvention.Unmanaged
                                        or NativeCallingConvention.Cdecl
                                        or NativeCallingConvention.Stdcall)
                                || !ForeignNativeInterfaceContract
                                    .IsValidMethodSignature(member,
                                        program.NativePointerTypes,
                                        program.NativeFunctionPointerSignatures)))
                        || (!foreignNativeInterface
                            && (!member.Attributes.IsDefaultOrEmpty
                                || !member.AttributeTokens.IsDefaultOrEmpty
                                || !member.ContractTokens.IsDefaultOrEmpty))
                        || !member.AdditionalDeclarationKeys.IsDefaultOrEmpty
                        || !member.TypeParameters.IsDefaultOrEmpty
                        || !member.Constraints.IsDefaultOrEmpty
                        || (!foreignNativeInterface
                            && !member.Dependencies.IsDefaultOrEmpty)
                        || !member.Accessors.IsDefaultOrEmpty
                        || member.AssociatedDefaultType is not null
                        || member.AssociatedConstantType is not null
                        || member.AssociatedConstantDefault is not null
                        || member.Parameters.IsDefault
                        || member.Parameters.Length
                            > SemanticVerifier.MaximumArguments
                        || member.Parameters.Select((parameter,
                                parameterOrdinal) =>
                                (Parameter: parameter,
                                    Ordinal: parameterOrdinal))
                            .Any(parameter =>
                            parameter.Parameter.Mode
                                != BoundInterfaceParameterMode.Value
                            || (!foreignNativeInterface
                                && (!parameter.Parameter.Attributes.IsDefaultOrEmpty
                                    || !parameter.Parameter.AttributeTokens.IsDefaultOrEmpty
                                    || !IsSupportedGenericInterfaceSignatureType(
                                        parameter.Parameter.Type,
                                        definition.Formals.Length,
                                        isReturn: false)))
                            || (foreignNativeInterface
                                && ((!member.NativeOutputContracts.Any(
                                        output => output.FormalOrdinal
                                            == parameter.Ordinal)
                                    && (!parameter.Parameter.Attributes.IsDefaultOrEmpty
                                        || !parameter.Parameter.AttributeTokens.IsDefaultOrEmpty))
                                    || parameter.Parameter.Type.Kind
                                        != BoundInterfaceTypeKind.Primitive)))
                        || (member.ResultType is not null
                            && (foreignNativeInterface
                                ? member.ResultType.Kind
                                    != BoundInterfaceTypeKind.Primitive
                                : !IsSupportedGenericInterfaceSignatureType(
                                    member.ResultType,
                                    definition.Formals.Length,
                                    isReturn: true))))
                    {
                        throw Unsupported(
                            $"Interface requirement '{definition.QualifiedName}.{member.Name}' is outside the G1 bodyless scalar/value signature subset.");
                    }

                    continue;
                }

                var associatedTypeExport
                    = associatedTypeExports[
                        associatedTypeExportOrdinal++];
                if (!schema.UsesAssociatedRelationConstraints()
                    || member.Accessibility
                        != BoundInterfaceAccessibility.Public
                    || member.SourceInputOrdinal
                        != associatedTypeExport.SourceInputOrdinal
                    || member.SourceDeclarationSpan
                        != associatedTypeExport.DeclarationSpan
                    || !IsValidInterfaceMemberExport(
                        associatedTypeExport.SourceInputOrdinal,
                        associatedTypeExport.DeclarationSpan,
                        associatedTypeExport.ExportKey, program)
                    || member.IsStatic || member.IsExternal || member.IsPartial
                    || member.RequiresUnsafeCaller
                    || member.DeclaredRequiresUnsafeCaller
                    || member.DoesNotReturn || member.IsAsync
                    || member.HasBody
                    || member.NativeCallableConvention is not null
                    || !member.Attributes.IsDefaultOrEmpty
                    || !member.AttributeTokens.IsDefaultOrEmpty
                    || !member.ContractTokens.IsDefaultOrEmpty
                    || !member.AdditionalDeclarationKeys.IsDefaultOrEmpty
                    || !member.TypeParameters.IsDefaultOrEmpty
                    || !member.Constraints.IsDefaultOrEmpty
                    || !member.Parameters.IsEmpty
                    || member.ResultType is not null
                    || !member.Accessors.IsDefaultOrEmpty
                    || member.AssociatedConstantType is not null
                    || member.AssociatedConstantDefault is not null
                    || (member.AssociatedDefaultType is not null
                        && !IsSupportedGenericInterfaceSignatureType(
                            member.AssociatedDefaultType,
                            definition.Formals.Length,
                            isReturn: true)))
                {
                    throw Unsupported(
                        $"Associated type '{definition.QualifiedName}.{member.Name}' is outside the CM1AR1 type-only declaration subset.");
                }
            }

            result.Add(new GenericInterfaceEmission(definition, export));
        }

        return result;
    }

    private static bool IsValidInterfaceMemberExport(int sourceInputOrdinal,
        TextSpan span, string exportKey, BoundProgram program)
        => sourceInputOrdinal >= 0
            && sourceInputOrdinal < program.SourceInputCount
            && span.Start >= 0 && span.Length > 0
            && (long)span.Start + span.Length <= int.MaxValue
            && !string.IsNullOrEmpty(exportKey)
            && IsValidSourceGenericMethodExportKey(exportKey)
            && !exportKey.Contains('\0', StringComparison.Ordinal);

    private static bool IsGenericWriteConstraint(
        BoundInterfaceConstraint constraint) =>
        constraint.Kind == BoundInterfaceConstraintKind.Write
            && constraint.Type is null
            && constraint.AttributeName is null
            && constraint.FormalScope
                == BoundInterfaceTypeParameterScope.Interface;

    private static string GetQualifiedInterfaceNamespace(
        string qualifiedName)
    {
        var separator = qualifiedName.LastIndexOf('.',
            StringComparison.Ordinal);
        return separator < 0 ? "" : qualifiedName[..separator];
    }

    private static string GetQualifiedInterfaceName(string qualifiedName)
    {
        var separator = qualifiedName.LastIndexOf('.',
            StringComparison.Ordinal);
        return separator < 0 ? qualifiedName
            : qualifiedName[(separator + 1)..];
    }

    private static bool IsSupportedGenericInterfaceSignatureType(
        BoundInterfaceType type, int formalCount, bool isReturn)
    {
        if (type.IsWritable || type.IsOptional
            || type.IsGarbageCollected || type.IsInitialization
            || type.ModuleImport is not null
            || !type.TypeArguments.IsDefaultOrEmpty)
        {
            return false;
        }

        return type.Kind switch {
            BoundInterfaceTypeKind.Primitive
                => Array.IndexOf(s_types, type.PrimitiveType) >= 0
                    && (isReturn
                        || type.PrimitiveType != PrimitiveType.Void),
            BoundInterfaceTypeKind.TypeParameter
                => type.TypeParameterScope
                        == BoundInterfaceTypeParameterScope.Interface
                    && type.TypeParameterOrdinal >= 0
                    && type.TypeParameterOrdinal < formalCount,
            _ => false,
        };
    }

    private static uint GenericInterfaceTypeSpecRef(
        BoundInterfaceType type,
        Dictionary<int, uint> formalTypeSpecRows,
        Dictionary<PrimitiveType, uint> builtInTypeSpecRows,
        List<ReadOnlyMemory<byte>> typeSpecRows)
        => type.Kind switch {
            BoundInterfaceTypeKind.Primitive
                => GetBuiltInTypeSpecRef(type.PrimitiveType,
                    builtInTypeSpecRows, typeSpecRows),
            BoundInterfaceTypeKind.TypeParameter
                when type.TypeParameterScope
                    == BoundInterfaceTypeParameterScope.Interface
                    && formalTypeSpecRows.TryGetValue(
                        type.TypeParameterOrdinal, out var formalTypeSpec)
                    => formalTypeSpec,
            _ => throw Unsupported(
                "A G1 interface requirement TypeSpec is outside the selected scalar and owner-formal subset."),
        };

    private static uint GenericMethodInterfaceConstraintTypeSpecRef(
        BoundInterfaceType type, BoundGenericMethodDefinition method,
        IReadOnlyDictionary<(string QualifiedName, int Arity), uint>
            exportedInterfaceTypeSpecRows,
        IReadOnlyDictionary<(string ProviderIdentity, string ExportKey,
            string QualifiedName, int Arity, uint TargetRequirement,
            uint RuntimePolicy), uint>
            importedInterfaceImportRows,
        Dictionary<PrimitiveType, uint> formalTypeSpecRows,
        int[] genericParamRows,
        Dictionary<PrimitiveType, uint> builtInTypeSpecRows,
        List<ReadOnlyMemory<byte>> typeSpecRows, int depth,
        ref int remainingVisits)
    {
        if (depth > SemanticVerifier.MaximumDepth
            || --remainingVisits < 0
            || (type.IsWritable
                && type.Kind != BoundInterfaceTypeKind.TypeParameter)
            || type.IsOptional
            || type.IsGarbageCollected || type.IsInitialization
            || type.IsGenericNominalConstantDomain
            || type.GenericNominalTypeIdentity is not null
            || type.SourceGenericTypeDefinitionIndex != -1)
        {
            throw Unsupported(
                "A G1 interface constraint exceeds the supported local type subset.");
        }

        switch (type.Kind)
        {
            case BoundInterfaceTypeKind.Primitive:
                if (type.PrimitiveType == PrimitiveType.Void
                    || Array.IndexOf(s_types, type.PrimitiveType) < 0
                    || type.NominalName is not null
                    || type.TypeParameterOrdinal != -1
                    || type.TypeParameterScope
                        != BoundInterfaceTypeParameterScope.None
                    || !type.TypeArguments.IsDefaultOrEmpty)
                {
                    throw Unsupported(
                        "A G1 interface constraint uses an unsupported primitive type.");
                }

                return GetBuiltInTypeSpecRef(type.PrimitiveType,
                    builtInTypeSpecRows, typeSpecRows);

            case BoundInterfaceTypeKind.TypeParameter:
                if (type.TypeParameterScope
                        != BoundInterfaceTypeParameterScope.Member
                    || (uint)type.TypeParameterOrdinal
                        >= (uint)method.Formals.Length
                    || type.PrimitiveType != PrimitiveType.Void
                    || type.NominalName is not null
                    || !type.TypeArguments.IsDefaultOrEmpty
                    || !formalTypeSpecRows.TryGetValue(
                        method.Formals[type.TypeParameterOrdinal]
                            .PlaceholderType, out var formalTypeSpecRow))
                {
                    throw Unsupported(
                        "A G1 interface constraint references an unsupported method formal.");
                }

                if (!type.IsWritable)
                {
                    return formalTypeSpecRow;
                }

                var writableFormalTypeSpecRow
                    = checked((uint)typeSpecRows.Count + 1);
                typeSpecRows.Add(TrRmTypeSpecTableWriter.Reference(0x04,
                    checked((uint)genericParamRows[
                        type.TypeParameterOrdinal]), 0x01));
                return writableFormalTypeSpecRow;

            case BoundInterfaceTypeKind.Nominal:
                if (string.IsNullOrEmpty(type.NominalName)
                    || type.TypeParameterOrdinal != -1
                    || type.PrimitiveType != PrimitiveType.Void
                    || type.TypeParameterScope
                        != BoundInterfaceTypeParameterScope.None)
                {
                    throw Unsupported(
                        "A G1 interface constraint has no selected local interface declaration.");
                }

                var argumentCount = type.TypeArguments.IsDefault
                    ? 0 : type.TypeArguments.Length;
                uint interfaceTypeSpecRow;
                if (type.ModuleImport is BoundModuleImport moduleImport)
                {
                    if (!importedInterfaceImportRows.TryGetValue(
                            (moduleImport.ProducerIdentity,
                                moduleImport.ExportKey, type.NominalName,
                                argumentCount, moduleImport.TargetRequirement,
                                moduleImport.RuntimePolicy),
                            out var importRow))
                    {
                        throw Unsupported(
                            "A G1 interface constraint has no exact expected interface Import.");
                    }

                    interfaceTypeSpecRow
                        = checked((uint)typeSpecRows.Count + 1);
                    typeSpecRows.Add(TrRmTypeSpecTableWriter.Reference(0x03,
                        importRow));
                }
                else if (!exportedInterfaceTypeSpecRows.TryGetValue(
                             (type.NominalName, argumentCount),
                             out interfaceTypeSpecRow))
                {
                    throw Unsupported(
                        "A G1 interface constraint has no selected local interface declaration.");
                }

                if (argumentCount == 0)
                {
                    return interfaceTypeSpecRow;
                }

                var arguments = new uint[argumentCount];
                for (var ordinal = 0; ordinal < argumentCount; ordinal++)
                {
                    var argument = type.TypeArguments[ordinal];
                    if (argument.Kind != BoundInterfaceTypeArgumentKind.Type
                        || argument.Type is not BoundInterfaceType
                            argumentType
                        || argument.ConstantType is not null
                        || argument.ConstantValue is not null
                        || argument.ConstantParameterScope
                            != BoundInterfaceTypeParameterScope.None
                        || argument.ConstantParameterOrdinal != -1)
                    {
                        throw Unsupported(
                            "A G1 interface constraint has an unsupported interface argument.");
                    }

                    arguments[ordinal]
                        = GenericMethodInterfaceConstraintTypeSpecRef(
                            argumentType, method,
                            exportedInterfaceTypeSpecRows,
                            importedInterfaceImportRows, formalTypeSpecRows,
                            genericParamRows, builtInTypeSpecRows, typeSpecRows,
                            depth + 1,
                            ref remainingVisits);
                }

                var constructedTypeSpecRow
                    = checked((uint)typeSpecRows.Count + 1);
                typeSpecRows.Add(TrRmTypeSpecTableWriter.Constructed(
                    interfaceTypeSpecRow, arguments));
                return constructedTypeSpecRow;

            default:
                throw Unsupported(
                    "A G1 interface constraint uses an unsupported type constructor.");
        }
    }

    private static uint GenericMethodAssociatedTypeDefReference(
        BoundGenericMethodAssociatedRelation relation,
        IReadOnlyDictionary<(
            string QualifiedName, int Arity, int MemberOrdinal,
            string DeclarationKey), uint>
            exportedInterfaceAssociatedTypeDefRows,
        IReadOnlyDictionary<GenericImportedInterfaceAssociatedTypeKey,
            uint> importedInterfaceAssociatedTypeImportRows)
    {
        var identity = relation.AssociatedType;
        var declaringInterface = identity.DeclaringInterface;
        if (declaringInterface.Kind != BoundInterfaceTypeKind.Nominal
            || string.IsNullOrEmpty(declaringInterface.NominalName)
            || declaringInterface.TypeArguments.IsDefault
            || string.IsNullOrEmpty(identity.DeclarationKey))
        {
            throw Unsupported(
                "A G1 associated-relation has no complete interface/member identity.");
        }

        var arity = declaringInterface.TypeArguments.Length;
        if (declaringInterface.ModuleImport is BoundModuleImport import)
        {
            var key = new GenericImportedInterfaceAssociatedTypeKey(
                import.ProducerIdentity, import.ExportKey,
                declaringInterface.NominalName, arity,
                identity.MemberOrdinal, identity.DeclarationKey,
                import.TargetRequirement, import.RuntimePolicy);
            return importedInterfaceAssociatedTypeImportRows.TryGetValue(
                key, out var importToken)
                ? importToken
                : throw Unsupported(
                    "A G1 associated-relation has no exact imported AssociatedTypeDef.");
        }

        return exportedInterfaceAssociatedTypeDefRows.TryGetValue(
                (declaringInterface.NominalName, arity,
                    identity.MemberOrdinal, identity.DeclarationKey),
                out var associatedTypeDefRow)
            ? 0x2000_0000u | associatedTypeDefRow
            : throw Unsupported(
                "A G1 associated-relation has no directly exported AssociatedTypeDef.");
    }

    private static Dictionary<(string ProviderIdentity, string ExportKey,
        string QualifiedName, int Arity, uint TargetRequirement,
        uint RuntimePolicy), uint> ReadImportedInterfaceImportRows(
        IReadOnlyList<TrRmSection> sections,
        TrRmStringTableReader strings)
    {
        var result = new Dictionary<(string ProviderIdentity,
            string ExportKey, string QualifiedName, int Arity,
            uint TargetRequirement, uint RuntimePolicy), uint>();
        if (!TryGetSection(sections, 0x001C, out var importSection))
        {
            return result;
        }

        if (!TryGetSection(sections, 0x0010, out var moduleRefSection)
            || !TryGetSection(sections, 0x0013, out var typeDefSection))
        {
            throw Invalid(
                "The generic method source artifact has Imports without ModuleRefs or TypeDefs.");
        }

        var importCount = checked((int)importSection.RecordCount);
        var moduleRefCount = checked((int)moduleRefSection.RecordCount);
        var typeDefCount = checked((int)typeDefSection.RecordCount);
        if (importSection.Bytes.Length != importCount * 12
            || moduleRefSection.Bytes.Length != moduleRefCount * 12
            || typeDefSection.Bytes.Length != typeDefCount * 44)
        {
            throw Invalid(
                "A G1 interface Import source table has an invalid extent.");
        }

        for (var importRow = 1; importRow <= importCount; importRow++)
        {
            var import = importSection.Bytes.Span.Slice(
                (importRow - 1) * 12, 12);
            var moduleRefRow = checked((int)
                BinaryPrimitives.ReadUInt32LittleEndian(import));
            var exportKeyRow = checked((int)
                BinaryPrimitives.ReadUInt32LittleEndian(import[4..]));
            var expectedDefinition
                = BinaryPrimitives.ReadUInt32LittleEndian(import[8..]);
            if ((expectedDefinition >> 24) != 0x13)
            {
                continue;
            }

            var typeDefRow = checked((int)(expectedDefinition
                & 0x00FF_FFFF));
            if (typeDefRow <= 0 || typeDefRow > typeDefCount
                || moduleRefRow <= 0 || moduleRefRow > moduleRefCount)
            {
                throw Invalid(
                    "A G1 interface Import has an invalid expected TypeDef or ModuleRef.");
            }

            var typeDef = typeDefSection.Bytes.Span.Slice(
                (typeDefRow - 1) * 44, 44);
            var typeDefFlags = BinaryPrimitives.ReadUInt32LittleEndian(
                typeDef[8..]);
            if (((typeDefFlags >> 2) & 7) != 3)
            {
                continue;
            }

            var namespaceRow = checked((int)
                BinaryPrimitives.ReadUInt32LittleEndian(typeDef));
            var nameRow = checked((int)
                BinaryPrimitives.ReadUInt32LittleEndian(typeDef[4..]));
            var genericArity = checked((int)
                BinaryPrimitives.ReadUInt32LittleEndian(typeDef[24..]));
            var namespaceName = strings.DecodeNamespace(namespaceRow);
            var name = strings.DecodeName(nameRow);
            var qualifiedName = namespaceName.Length == 0 ? name
                : namespaceName + "." + name;

            var moduleRef = moduleRefSection.Bytes.Span.Slice(
                (moduleRefRow - 1) * 12, 12);
            var providerStringRow = checked((int)
                BinaryPrimitives.ReadUInt32LittleEndian(moduleRef));
            var providerIdentity = strings.DecodeName(providerStringRow);
            var targetRequirement = BinaryPrimitives
                .ReadUInt32LittleEndian(moduleRef[4..]);
            var runtimePolicy = BinaryPrimitives
                .ReadUInt32LittleEndian(moduleRef[8..]);
            var exportKey = strings.DecodeName(exportKeyRow);
            var key = (providerIdentity, exportKey, qualifiedName,
                genericArity, targetRequirement, runtimePolicy);
            if (!result.TryAdd(key, checked((uint)importRow)))
            {
                throw Unsupported(
                    "A G1 interface constraint has ambiguous expected Import views.");
            }
        }

        return result;
    }

    private static Dictionary<GenericImportedInterfaceMethodKey, uint>
        ReadImportedInterfaceMethodImportRows(byte[] artifact,
            TrRmArtifactSchema schema, string? standardSchemaIdentity,
            bool allowDuplicateForeignNativeInterfaceViews = false)
    {
        var index = new TrRmArtifactIndex(artifact, schema,
            standardSchemaIdentity: standardSchemaIdentity);
        var sections = new List<TrRmSection>(index.SectionCount);
        for (var sectionIndex = 0;
            sectionIndex < index.SectionCount;
            sectionIndex++)
        {
            index.GetSection(sectionIndex, out var kind, out var flags,
                out var bytes, out var count);
            sections.Add(new TrRmSection(kind, flags,
                checked((uint)count), bytes.ToArray()));
        }

        var stringSection = GetSection(sections, 0x0002);
        var strings = new TrRmStringTableReader(stringSection.Bytes.Span,
            checked((int)stringSection.RecordCount));
        return ReadImportedInterfaceMethodImportRows(sections, strings,
            allowDuplicateForeignNativeInterfaceViews);
    }

    private static byte[] RemapForeignNativeInterfaceCallMethodMapTokens(
        byte[] artifact, TrRmArtifactSchema schema,
        IReadOnlyDictionary<(
            string QualifiedName, int Arity, int MemberOrdinal,
            string DeclarationKey), uint> localMethodDefRows,
        IReadOnlyDictionary<GenericImportedInterfaceMethodKey, uint>
            importedMethodImportRows,
        IReadOnlyDictionary<BoundInterfaceMemberIdentity, uint>
            placeholders)
    {
        var targetTokens = new Dictionary<uint, uint>(placeholders.Count);
        foreach (var (member, placeholder) in placeholders)
        {
            if ((placeholder >> 24) != 0xFE
                || (placeholder & 0x00FF_FFFF) == 0
                || member.DeclaringInterface.NominalName is not
                    string qualifiedName)
            {
                throw Invalid(
                    "A Standard F+I caller Body has an invalid InterfaceMethod placeholder.");
            }

            var arity = member.DeclaringInterface.TypeArguments.Length;
            uint targetToken;
            if (member.DeclaringInterface.ModuleImport
                is BoundModuleImport importedInterface)
            {
                var key = new GenericImportedInterfaceMethodKey(
                    importedInterface.ProducerIdentity,
                    importedInterface.ExportKey, qualifiedName, arity,
                    member.MemberOrdinal, member.DeclarationKey,
                    importedInterface.TargetRequirement,
                    importedInterface.RuntimePolicy);
                if (!importedMethodImportRows.TryGetValue(key,
                        out var importRow))
                {
                    throw Unsupported(
                        "A Standard F+I caller Body has no exact imported InterfaceMethod Import.");
                }

                targetToken = 0x1C00_0000u | importRow;
            }
            else
            {
                var key = (qualifiedName, arity, member.MemberOrdinal,
                    member.DeclarationKey);
                if (!localMethodDefRows.TryGetValue(key,
                        out var methodRow))
                {
                    throw Unsupported(
                        "A Standard F+I caller Body has no exact local InterfaceMethod MethodDef.");
                }

                targetToken = 0x1600_0000u | methodRow;
            }

            if (!targetTokens.TryAdd(placeholder, targetToken))
            {
                throw Invalid(
                    "A Standard F+I caller Body reuses an InterfaceMethod placeholder.");
            }
        }

        var index = new TrRmArtifactIndex(artifact, schema);
        if (!index.TryGetSection(0x0020, out var bodyBytes,
                out var bodyCount)
            || bodyCount == 0)
        {
            throw Invalid(
                "A Standard F+I caller Body placeholder has no Body table.");
        }

        var bodySection = bodyBytes.ToArray();
        var bodyTable = new TrRmBodyTableReader(bodySection, bodyCount,
            schema);
        _ = index.TryGetSection(0x001B, out _, out var contractCount);
        var bodyDataOffset = checked((bodyCount + 1) * sizeof(uint));
        var replaced = new HashSet<uint>();
        for (var bodyRow = 1; bodyRow <= bodyCount; bodyRow++)
        {
            var body = bodyTable.GetRow(bodyRow);
            if (body[0] != 0x01 || body[1] != 0x01)
            {
                continue;
            }

            var registerBody = bodyTable.GetRegisterBody(bodyRow,
                contractCount);
            var methodMap = registerBody.MethodMap;
            var previousOffset
                = BinaryPrimitives.ReadUInt32LittleEndian(
                    bodySection.AsSpan((bodyRow - 1) * sizeof(uint), 4));
            var rowStart = checked(bodyDataOffset
                + (int)previousOffset);
            for (var methodMapIndex = 0;
                methodMapIndex < registerBody.MethodCount;
                methodMapIndex++)
            {
                var mapOffset = methodMapIndex * sizeof(uint);
                var token = BinaryPrimitives.ReadUInt32LittleEndian(
                    methodMap[mapOffset..]);
                if ((token >> 24) != 0xFE)
                {
                    continue;
                }
                if (!targetTokens.TryGetValue(token,
                        out var targetToken))
                {
                    throw Invalid(
                        "A Standard F+I caller Body has an unknown InterfaceMethod placeholder.");
                }

                BinaryPrimitives.WriteUInt32LittleEndian(
                    bodySection.AsSpan(checked(rowStart
                        + registerBody.MethodMapOffset + mapOffset), 4),
                    targetToken);
                _ = replaced.Add(token);
            }
        }

        if (replaced.Count != targetTokens.Count)
        {
            throw Invalid(
                "A Standard F+I InterfaceMethod placeholder has no Body MethodMap entry.");
        }

        var sections = new List<TrRmSection>(index.SectionCount);
        for (var sectionIndex = 0;
            sectionIndex < index.SectionCount;
            sectionIndex++)
        {
            index.GetSection(sectionIndex, out var kind, out var flags,
                out var bytes, out var count);
            sections.Add(new TrRmSection(kind, flags,
                checked((uint)count),
                kind == 0x0020 ? bodySection : bytes.ToArray()));
        }

        return TrRmArtifactWriter.Encode(sections, schema);
    }

    private static Dictionary<GenericImportedInterfaceMethodKey, uint>
        ReadImportedInterfaceMethodImportRows(
            IReadOnlyList<TrRmSection> sections,
            TrRmStringTableReader strings,
            bool allowDuplicateForeignNativeInterfaceViews = false)
    {
        var result
            = new Dictionary<GenericImportedInterfaceMethodKey, uint>();
        if (!TryGetSection(sections, 0x001C, out var importSection)
            || !TryGetSection(sections, 0x0025, out var interfaceDefSection)
            || !TryGetSection(sections, 0x0024,
                out var interfaceMethodSection))
        {
            return result;
        }

        if (!TryGetSection(sections, 0x0010, out var moduleRefSection)
            || !TryGetSection(sections, 0x0013, out var typeDefSection)
            || !TryGetSection(sections, 0x0016, out var methodSection))
        {
            throw Invalid(
                "The G1L1 interface MethodDef Imports have incomplete declaration tables.");
        }

        var importCount = checked((int)importSection.RecordCount);
        var moduleRefCount = checked((int)moduleRefSection.RecordCount);
        var typeDefCount = checked((int)typeDefSection.RecordCount);
        var methodCount = checked((int)methodSection.RecordCount);
        var interfaceDefCount
            = checked((int)interfaceDefSection.RecordCount);
        var interfaceMethodCount
            = checked((int)interfaceMethodSection.RecordCount);
        if (importSection.Bytes.Length != checked(importCount * 12)
            || moduleRefSection.Bytes.Length
                != checked(moduleRefCount * 12)
            || typeDefSection.Bytes.Length != checked(typeDefCount * 44)
            || methodSection.Bytes.Length != checked(methodCount * 28)
            || interfaceDefSection.Bytes.Length
                != checked(interfaceDefCount * 48)
            || interfaceMethodSection.Bytes.Length
                != checked(interfaceMethodCount * 12))
        {
            throw Invalid(
                "A G1L1 interface MethodDef Import source table has an invalid extent.");
        }

        var typeImports = new Dictionary<int, (
            string ProviderIdentity, string ExportKey, string QualifiedName,
            int Arity, uint TargetRequirement, uint RuntimePolicy)>();
        for (var importRow = 1; importRow <= importCount; importRow++)
        {
            var import = importSection.Bytes.Span.Slice(
                (importRow - 1) * 12, 12);
            var expectedDefinition
                = BinaryPrimitives.ReadUInt32LittleEndian(import[8..]);
            if ((expectedDefinition >> 24) != 0x13)
            {
                continue;
            }

            var typeDefRow = checked((int)(expectedDefinition
                & 0x00FF_FFFF));
            if (typeDefRow <= 0 || typeDefRow > typeDefCount)
            {
                throw Invalid(
                    "A G1L1 interface MethodDef Import has an invalid expected TypeDef.");
            }

            var typeDef = typeDefSection.Bytes.Span.Slice(
                (typeDefRow - 1) * 44, 44);
            var flags = BinaryPrimitives.ReadUInt32LittleEndian(
                typeDef[8..]);
            if (((flags >> 2) & 7) != 3)
            {
                continue;
            }

            var moduleRefRow = ReadInt(import, 0);
            if (moduleRefRow <= 0 || moduleRefRow > moduleRefCount)
            {
                throw Invalid(
                    "A G1L1 interface TypeDef Import has an invalid ModuleRef.");
            }

            var moduleRef = moduleRefSection.Bytes.Span.Slice(
                (moduleRefRow - 1) * 12, 12);
            var providerIdentity = strings.DecodeName(
                ReadInt(moduleRef, 0));
            var namespaceName = strings.DecodeNamespace(
                ReadInt(typeDef, 0));
            var name = strings.DecodeName(ReadInt(typeDef, 4));
            var qualifiedName = namespaceName.Length == 0 ? name
                : namespaceName + "." + name;
            var typeImport = (
                ProviderIdentity: providerIdentity,
                ExportKey: strings.DecodeName(ReadInt(import, 4)),
                QualifiedName: qualifiedName,
                Arity: ReadInt(typeDef, 24),
                TargetRequirement: BinaryPrimitives
                    .ReadUInt32LittleEndian(moduleRef[4..]),
                RuntimePolicy: BinaryPrimitives
                    .ReadUInt32LittleEndian(moduleRef[8..]));
            if (!typeImports.TryAdd(typeDefRow, typeImport))
            {
                throw Unsupported(
                    "A G1L1 imported interface TypeDef has ambiguous expected Import views.");
            }
        }

        var interfaceDefByTypeDef = new Dictionary<int, int>(
            interfaceDefCount);
        for (var interfaceDefRow = 1;
            interfaceDefRow <= interfaceDefCount; interfaceDefRow++)
        {
            var interfaceDef = interfaceDefSection.Bytes.Span.Slice(
                (interfaceDefRow - 1) * 48, 48);
            var typeDefRow = ReadInt(interfaceDef, 0);
            if (!interfaceDefByTypeDef.TryAdd(typeDefRow,
                    interfaceDefRow))
            {
                throw Invalid(
                    "A G1L1 InterfaceDef has an ambiguous TypeDef owner.");
            }
        }

        var interfaceMethodByMethod = new Dictionary<int,
            (int InterfaceDefRow, int Ordinal)>(interfaceMethodCount);
        for (var interfaceMethodRow = 1;
            interfaceMethodRow <= interfaceMethodCount;
            interfaceMethodRow++)
        {
            var interfaceMethod = interfaceMethodSection.Bytes.Span.Slice(
                (interfaceMethodRow - 1) * 12, 12);
            var methodRow = ReadInt(interfaceMethod, 8);
            if (!interfaceMethodByMethod.TryAdd(methodRow,
                    (ReadInt(interfaceMethod, 0),
                        ReadInt(interfaceMethod, 4))))
            {
                throw Invalid(
                    "An InterfaceMethod MethodDef has an ambiguous imported member identity.");
            }
        }

        for (var importRow = 1; importRow <= importCount; importRow++)
        {
            var import = importSection.Bytes.Span.Slice(
                (importRow - 1) * 12, 12);
            var expectedDefinition
                = BinaryPrimitives.ReadUInt32LittleEndian(import[8..]);
            if ((expectedDefinition >> 24) != 0x16)
            {
                continue;
            }

            var methodRow = checked((int)(expectedDefinition
                & 0x00FF_FFFF));
            if (methodRow <= 0 || methodRow > methodCount)
            {
                throw Invalid(
                    "A G1L1 interface MethodDef Import has an invalid expected MethodDef.");
            }

            var method = methodSection.Bytes.Span.Slice(
                (methodRow - 1) * 28, 28);
            var typeDefRow = ReadInt(method, 0);
            if (!typeImports.TryGetValue(typeDefRow,
                    out var typeImport))
            {
                continue;
            }
            if (!interfaceDefByTypeDef.TryGetValue(typeDefRow,
                    out var interfaceDefRow)
                || !interfaceMethodByMethod.TryGetValue(methodRow,
                    out var interfaceMethod)
                || interfaceMethod.InterfaceDefRow != interfaceDefRow)
            {
                throw Invalid(
                    "An imported interface MethodDef has no direct InterfaceMethod owner.");
            }

            var moduleRefRow = ReadInt(import, 0);
            if (moduleRefRow <= 0 || moduleRefRow > moduleRefCount)
            {
                throw Invalid(
                    "An imported interface MethodDef has an invalid ModuleRef.");
            }
            var moduleRef = moduleRefSection.Bytes.Span.Slice(
                (moduleRefRow - 1) * 12, 12);
            var providerIdentity
                = strings.DecodeName(ReadInt(moduleRef, 0));
            var targetRequirement
                = BinaryPrimitives.ReadUInt32LittleEndian(moduleRef[4..]);
            var runtimePolicy
                = BinaryPrimitives.ReadUInt32LittleEndian(moduleRef[8..]);
            if (!StringComparer.Ordinal.Equals(providerIdentity,
                    typeImport.ProviderIdentity)
                || targetRequirement != typeImport.TargetRequirement
                || runtimePolicy != typeImport.RuntimePolicy)
            {
                throw Invalid(
                    "An imported interface MethodDef selects a different provider than its TypeDef.");
            }

            var key = new GenericImportedInterfaceMethodKey(
                typeImport.ProviderIdentity, typeImport.ExportKey,
                typeImport.QualifiedName, typeImport.Arity,
                interfaceMethod.Ordinal,
                strings.DecodeName(ReadInt(import, 4)),
                typeImport.TargetRequirement, typeImport.RuntimePolicy);
            // A completed FNI call is provider-keyed; identical expected views
            // may therefore share one deterministic MethodMap target.
            if (!result.TryAdd(key, checked((uint)importRow))
                && !allowDuplicateForeignNativeInterfaceViews)
            {
                throw Unsupported(
                    "A G1L1 imported InterfaceMethod has an ambiguous expected Import.");
            }
        }

        return result;
    }

    private static (byte[] GenericParams, byte[] Constraints,
        byte[] AssociatedRelations)
        CreateGenericMetadataBytes(byte[] stringBytes,
        int stringCount, byte[] moduleBytes, byte[] methodBytes,
        byte[] signatureBytes, byte[] typeSpecBytes, int typeSpecCount,
        List<TrRmGenericParamRow> genericParameterRows,
        List<TrRmConstraintRow> constraintRows,
        List<TrRmAssociatedRelationRow> associatedRelationRows,
        IReadOnlyList<TrRmSection> sections, int typeDefCount, int importCount,
        int nativeFunctionPointerSignatureCount, int featureCount,
        int signatureCount, int parameterCount, int contractCount,
        int bodyCount, int moduleRefCount, TrRmArtifactSchema schema)
    {
        var strings = new TrRmStringTableReader(stringBytes, stringCount);
        var modules = new TrRmModuleTableReader(moduleBytes, 1, strings,
            moduleRefCount, typeDefCount, methodBytes.Length / 28,
            GetCount(sections, 0x001F), 0);
        TrRmTypeDefTableReader types = default;
        if (typeDefCount != 0)
        {
            var typeDefSection = GetSection(sections, 0x0013);
            types = new TrRmTypeDefTableReader(typeDefSection.Bytes.Span,
                typeDefCount, strings, modules, GetCount(sections, 0x0014),
                genericParameterRows.Count, GetCount(sections, 0x0015),
                contractCount, schema);
        }
        var typeSpecs = new TrRmTypeSpecTableReader(typeSpecBytes,
            typeSpecCount, typeDefCount, importCount,
            genericParameterRows.Count,
            nativeFunctionPointerSignatureCount,
            schema,
            methodBytes.Length / 28);
        var signatures = signatureCount == 0 ? default
            : new TrRmMethodSigTableReader(signatureBytes,
                signatureCount, typeSpecs, parameterCount,
                genericParameterRows.Count);
        var methodCount = methodBytes.Length / 28;
        var methods = methodCount == 0 ? default
            : new TrRmMethodDefTableReader(methodBytes, methodCount, strings,
                modules, types, signatures, contractCount, bodyCount, schema);
        var genericParamBytes = genericParameterRows.Count == 0 ? []
            : TrRmGenericParamTableWriter.Encode(genericParameterRows,
                strings, types, methods, signatures, typeSpecs,
                constraintRows.Count, GetCount(sections, 0x0030));
        var genericParams = new TrRmGenericParamTableReader(
            genericParamBytes, genericParameterRows.Count, strings, types,
            methods, signatures, typeSpecs, constraintRows.Count,
            GetCount(sections, 0x0030));
        var moduleRefBytes = TryGetSection(sections, 0x0010,
            out var moduleRefSection) ? moduleRefSection.Bytes
            : ReadOnlyMemory<byte>.Empty;
        var moduleRefs = new TrRmModuleRefTableReader(moduleRefBytes.Span,
            moduleRefCount, strings, modules);
        var interfaceDefCount = GetCount(sections, 0x0025);
        var interfaceMethodCount = GetCount(sections, 0x0024);
        var interfaceBaseCount = GetCount(sections, 0x0026);
        var associatedTypeDefCount = GetCount(sections, 0x0027);
        var associatedConstCount = GetCount(sections, 0x0028);
        var interfaceConstCount = GetCount(sections, 0x0029);
        var interfaces = interfaceDefCount == 0 ? default
            : new TrRmInterfaceDefTableReader(
                GetSection(sections, 0x0025).Bytes.Span,
                interfaceDefCount, types, genericParams,
                interfaceBaseCount, interfaceMethodCount,
                associatedTypeDefCount, associatedConstCount,
                interfaceConstCount);
        var associatedTypeDefs = associatedTypeDefCount == 0 ? default
            : new TrRmAssociatedTypeDefTableReader(
                GetSection(sections, 0x0027).Bytes.Span,
                associatedTypeDefCount, strings, typeSpecs, interfaces);
        var fields = GetCount(sections, 0x0014) == 0 ? default
            : new TrRmFieldDefTableReader(
                GetSection(sections, 0x0014).Bytes.Span,
                GetCount(sections, 0x0014), strings, types, typeSpecs);
        var properties = GetCount(sections, 0x001E) == 0 ? default
            : new TrRmPropertyDefTableReader(
                GetSection(sections, 0x001E).Bytes.Span,
                GetCount(sections, 0x001E), strings, types, typeSpecs,
                methods, signatures);
        var features = featureCount == 0 ? default
            : new TrRmFeatureTableReader(
                GetSection(sections, 0x0004).Bytes.Span,
                featureCount, schema);
        var importBytes = TryGetSection(sections, 0x001C,
            out var importSection) ? importSection.Bytes.Span
            : ReadOnlySpan<byte>.Empty;
        var imports = new TrRmImportTableReader(importBytes, importCount,
            strings, moduleRefs, types, methods, properties, interfaces,
            associatedTypeDefs, fields, features, modules, schema);
        var associatedRelationBytes
            = TrRmAssociatedRelationTableWriter.Encode(
                associatedRelationRows, typeSpecs, associatedTypeDefCount,
                imports);
        var associatedRelations
            = new TrRmAssociatedRelationTableReader(
                associatedRelationBytes, associatedRelationRows.Count,
                typeSpecs, associatedTypeDefCount, imports);
        var constraintBytes = constraintRows.Count == 0 ? []
            : TrRmConstraintTableWriter.Encode(constraintRows,
                genericParams, typeSpecs, associatedRelations);
        return (genericParamBytes, constraintBytes,
            associatedRelationBytes);
    }

    private static uint GenericMethodTypeDefaultRef(
        BoundGenericMethodTypeDefault typeDefault, int formalOrdinal,
        BoundGenericMethodDefinition definition,
        Dictionary<PrimitiveType, uint> formalTypeSpecRows,
        Dictionary<PrimitiveType, uint> builtInTypeSpecRows,
        List<ReadOnlyMemory<byte>> typeSpecRows,
        GenericMethodNominalTypeWriter? nominalTypes)
    {
        if (BoundGenericMethodTypeTerms.ContainsKind(typeDefault.Term,
                BoundGenericMethodTypeTermKind.ImportedGeneric))
        {
            if (!typeDefault.ConstantFormalDependencies.IsEmpty
                || !BoundGenericMethodTypeTerms.TryGetFormalDependencies(
                    typeDefault.Term, out var dependencies)
                || !dependencies.SequenceEqual(typeDefault.FormalDependencies)
                || dependencies.Any(dependency =>
                    dependency < 0 || dependency >= formalOrdinal))
            {
                throw Unsupported(
                    "A G1 nominal type default has inconsistent earlier-formal dependencies.");
            }
            return nominalTypes is null
                ? throw Unsupported(
                    "This G1 type-default position does not select nominal TypeDef emission.")
                : nominalTypes.TypeTermRef(typeDefault.Term, definition,
                    formalTypeSpecRows, builtInTypeSpecRows, 0);
        }
        if (typeDefault.Term.Kind
            == BoundGenericMethodTypeTermKind.Span)
        {
            if (!typeDefault.ConstantFormalDependencies.IsEmpty
                || !typeDefault.Term.ConstantArguments.IsEmpty
                || typeDefault.Term.IsGarbageCollected
                || typeDefault.Term.IsOptional
                || typeDefault.Term.AllowsVoid
                || typeDefault.Term.ElementCount != 0
                || typeDefault.Term.CallingConvention
                    != NativeCallingConvention.Native
                || typeDefault.Term.ParameterCount != 0
                || typeDefault.Term.NominalTypeIdentity is not null
                || !BoundGenericMethodTypeTerms.TryGetFormalDependencies(
                    typeDefault.Term, out var dependencies)
                || !dependencies.SequenceEqual(
                    typeDefault.FormalDependencies)
                || dependencies.Any(dependency =>
                    dependency < 0 || dependency >= formalOrdinal))
            {
                throw Unsupported(
                    $"Generic MethodDef '{definition.Name}' has a Span type default outside the earlier-formal subset.");
            }

            return GenericMethodTypeDefaultSpecRef(typeDefault.Term,
                formalOrdinal, definition, formalTypeSpecRows,
                builtInTypeSpecRows, typeSpecRows);
        }

        if (typeDefault.Term.Kind
            == BoundGenericMethodTypeTermKind.CheckedReference)
        {
            if (!typeDefault.ConstantFormalDependencies.IsEmpty
                || typeDefault.Term.TypeArguments.Length != 1
                || (typeDefault.Term.IsGarbageCollected
                    && typeDefault.Term.IsInitialization)
                || (typeDefault.Term.IsOptional
                    && typeDefault.Term.IsInitialization)
                || typeDefault.Term.AllowsVoid
                || typeDefault.Term.IsElementWritable
                || typeDefault.Term.ElementCount != 0
                || typeDefault.Term.CallingConvention
                    != NativeCallingConvention.Native
                || typeDefault.Term.ParameterCount != 0
                || typeDefault.Term.NominalTypeIdentity is not null
                || !BoundGenericMethodTypeTerms.TryGetFormalDependencies(
                    typeDefault.Term, out var dependencies)
                || !dependencies.SequenceEqual(
                    typeDefault.FormalDependencies)
                || dependencies.Any(dependency =>
                    dependency < 0 || dependency >= formalOrdinal))
            {
                throw Unsupported(
                    $"Generic MethodDef '{definition.Name}' has a checked-reference type default outside the earlier-formal subset.");
            }

            return GenericMethodTypeDefaultSpecRef(typeDefault.Term,
                formalOrdinal, definition, formalTypeSpecRows,
                builtInTypeSpecRows, typeSpecRows);
        }

        var redundantWritableFormal
            = typeDefault.Term.Kind
                    == BoundGenericMethodTypeTermKind.Formal
                && (uint)typeDefault.Term.FormalOrdinal
                    < (uint)definition.Formals.Length
                && definition.Formals[typeDefault.Term.FormalOrdinal]
                    .RequiresWritable;
        if (!typeDefault.ConstantFormalDependencies.IsEmpty
            || typeDefault.Term.TypeArguments.IsDefaultOrEmpty
                == false
            || !typeDefault.Term.ConstantArguments.IsEmpty
            || (typeDefault.Term.IsWritable
                && !redundantWritableFormal)
            || typeDefault.Term.IsGarbageCollected
            || typeDefault.Term.IsInitialization
            || typeDefault.Term.IsOptional
            || typeDefault.Term.ElementCount != 0
            || typeDefault.Term.CallingConvention
                != NativeCallingConvention.Native
            || typeDefault.Term.ParameterCount != 0
            || typeDefault.Term.AllowsVoid
            || typeDefault.Term.IsElementWritable
            || typeDefault.Term.NominalTypeIdentity is not null)
        {
            throw Unsupported(
                $"Generic MethodDef '{definition.Name}' has a type default outside the G1 direct-type subset.");
        }

        if (typeDefault.Term.Kind
            == BoundGenericMethodTypeTermKind.Formal)
        {
            var dependency = typeDefault.Term.FormalOrdinal;
            if (dependency < 0 || dependency >= formalOrdinal
                || !typeDefault.FormalDependencies.SequenceEqual(
                    [dependency]))
            {
                throw Unsupported(
                    $"Generic MethodDef '{definition.Name}' has a non-earlier or inconsistent type default.");
            }

            var dependencyType = definition.Formals[dependency]
                .PlaceholderType;
            return formalTypeSpecRows.TryGetValue(dependencyType,
                    out var typeSpecRef)
                ? typeSpecRef
                : throw Unsupported(
                    $"Generic MethodDef '{definition.Name}' has a type default before its formal.");
        }

        if (typeDefault.Term.Kind
            == BoundGenericMethodTypeTermKind.Concrete
            && typeDefault.FormalDependencies.IsEmpty)
        {
            return GetBuiltInTypeSpecRef(typeDefault.Term.ConcreteType,
                builtInTypeSpecRows, typeSpecRows);
        }

        throw Unsupported(
            $"Generic MethodDef '{definition.Name}' has a type default outside the G1 direct-type subset.");
    }

    private static uint GenericMethodTypeDefaultSpecRef(
        BoundGenericMethodTypeTerm term, int formalOrdinal,
        BoundGenericMethodDefinition definition,
        Dictionary<PrimitiveType, uint> formalTypeSpecRows,
        Dictionary<PrimitiveType, uint> builtInTypeSpecRows,
        List<ReadOnlyMemory<byte>> typeSpecRows)
    {
        if (term.Kind == BoundGenericMethodTypeTermKind.Formal)
        {
            if (term.FormalOrdinal < 0
                || term.IsElementWritable
                || term.FormalOrdinal >= formalOrdinal
                || (uint)term.FormalOrdinal
                    >= (uint)definition.Formals.Length
                || (term.IsWritable
                    && !definition.Formals[term.FormalOrdinal]
                        .RequiresWritable)
                || !formalTypeSpecRows.TryGetValue(
                    definition.Formals[term.FormalOrdinal]
                        .PlaceholderType, out var formalTypeSpecRef))
            {
                throw Unsupported(
                    $"Generic MethodDef '{definition.Name}' has a Span default that does not reference an earlier formal.");
            }
            return formalTypeSpecRef;
        }

        if (term.Kind == BoundGenericMethodTypeTermKind.CheckedReference)
        {
            if (term.TypeArguments.Length != 1
                || (term.IsGarbageCollected && term.IsInitialization)
                || (term.IsOptional && term.IsInitialization)
                || term.AllowsVoid || term.IsElementWritable
                || term.ElementCount != 0
                || term.CallingConvention
                    != NativeCallingConvention.Native
                || term.ParameterCount != 0
                || term.NominalTypeIdentity is not null
                || term.TypeArguments[0].AllowsVoid
                || term.TypeArguments[0].IsOptional)
            {
                throw Unsupported(
                    $"Generic MethodDef '{definition.Name}' has a checked-reference type default outside the selected TypeSpec family.");
            }

            var referentTerm = term.TypeArguments[0];
            uint referentTypeSpecRef;
            if (referentTerm.Kind
                == BoundGenericMethodTypeTermKind.Formal)
            {
                var dependency = referentTerm.FormalOrdinal;
                if (dependency < 0 || dependency >= formalOrdinal
                    || (uint)dependency
                        >= (uint)definition.Formals.Length
                    || !formalTypeSpecRows.TryGetValue(
                        definition.Formals[dependency].PlaceholderType,
                        out referentTypeSpecRef))
                {
                    throw Unsupported(
                        $"Generic MethodDef '{definition.Name}' has a checked-reference default that does not reference an earlier formal.");
                }
                if (referentTerm.IsWritable)
                {
                    if (!definition.Formals[dependency]
                            .RequiresWritable)
                    {
                        throw Unsupported(
                            $"Generic MethodDef '{definition.Name}' has a write-qualified referent without the formal's write requirement.");
                    }
                }
            }
            else if (referentTerm.Kind
                == BoundGenericMethodTypeTermKind.Concrete)
            {
                if (referentTerm.ConcreteType == PrimitiveType.Void)
                {
                    throw Unsupported(
                        "A G1 checked-reference referent cannot be void.");
                }
                referentTypeSpecRef = GenericMethodTypeSpecRef(
                    referentTerm.ConcreteType, formalTypeSpecRows,
                    builtInTypeSpecRows, typeSpecRows,
                    definition.ProofProgram.SpanTypes,
                    definition.ProofProgram.CheckedReferenceTypes,
                    definition.DefinitionOrdinal, definition,
                    allowSpanType: true,
                    allowFormal: false);
            }
            else
            {
                referentTypeSpecRef = GenericMethodTypeDefaultSpecRef(
                    referentTerm, formalOrdinal, definition,
                    formalTypeSpecRows, builtInTypeSpecRows, typeSpecRows);
            }

            var referenceTypeSpecRef = checked(
                (uint)typeSpecRows.Count + 1);
            var qualifiers = (byte)((term.IsWritable ? 0x01 : 0)
                | (term.IsGarbageCollected ? 0x02 : 0));
            typeSpecRows.Add(term.IsInitialization
                ? TrRmTypeSpecTableWriter.InitializationReference(
                    referentTypeSpecRef, term.IsWritable)
                : TrRmTypeSpecTableWriter.Wrapped(0x07, referentTypeSpecRef,
                    qualifiers));
            if (!term.IsOptional)
            {
                return referenceTypeSpecRef;
            }

            var optionalTypeSpecRef = checked(
                (uint)typeSpecRows.Count + 1);
            typeSpecRows.Add(TrRmTypeSpecTableWriter.Wrapped(0x06,
                referenceTypeSpecRef));
            return optionalTypeSpecRef;
        }

        if (term.Kind == BoundGenericMethodTypeTermKind.Concrete)
        {
            if (term.AllowsVoid || term.IsElementWritable)
            {
                throw Unsupported(
                    "A G1 Span element type cannot be void.");
            }
            return GenericMethodTypeSpecRef(term.ConcreteType,
                formalTypeSpecRows, builtInTypeSpecRows, typeSpecRows,
                definition.ProofProgram.SpanTypes,
                definition.ProofProgram.CheckedReferenceTypes,
                definition.DefinitionOrdinal, definition,
                allowSpanType: true);
        }

        if (term.Kind != BoundGenericMethodTypeTermKind.Span
            || term.TypeArguments.Length != 1
            || term.IsGarbageCollected || term.IsOptional
            || term.AllowsVoid || term.ElementCount != 0
            || term.CallingConvention != NativeCallingConvention.Native
            || term.ParameterCount != 0
            || term.NominalTypeIdentity is not null
            || (term.TypeArguments[0].Kind
                    == BoundGenericMethodTypeTermKind.Concrete
                && (term.TypeArguments[0].ConcreteType
                        == PrimitiveType.Void
                    || term.TypeArguments[0].AllowsVoid))
            || (term.IsElementWritable
                && term.TypeArguments[0].Kind
                    != BoundGenericMethodTypeTermKind.Formal))
        {
            throw Unsupported(
                $"Generic MethodDef '{definition.Name}' has a nested Span default outside the selected TypeSpec family.");
        }

        var elementQualifiers = (byte)(
            (term.IsElementWritable ? 0x01 : 0)
            | (term.IsInitialization ? 0x04 : 0));
        var elementTypeSpecRef = GenericMethodSpanArgumentTypeSpecRef(
            term.TypeArguments[0], elementQualifiers, formalOrdinal,
            definition, formalTypeSpecRows, builtInTypeSpecRows,
            typeSpecRows);
        var typeSpecRef = checked((uint)typeSpecRows.Count + 1);
        typeSpecRows.Add(TrRmTypeSpecTableWriter.IndexedFamily(0x8A,
            elementTypeSpecRef, term.IsWritable ? (byte)0x01 : (byte)0));
        return typeSpecRef;
    }

    private static uint GenericMethodSpanArgumentTypeSpecRef(
        BoundGenericMethodTypeTerm elementTerm, byte qualifiers,
        int formalOrdinal, BoundGenericMethodDefinition definition,
        Dictionary<PrimitiveType, uint> formalTypeSpecRows,
        Dictionary<PrimitiveType, uint> builtInTypeSpecRows,
        List<ReadOnlyMemory<byte>> typeSpecRows)
    {
        if ((qualifiers & 0x01) != 0)
        {
            if (elementTerm.Kind
                    != BoundGenericMethodTypeTermKind.Formal
                || (uint)elementTerm.FormalOrdinal
                    >= (uint)definition.Formals.Length
                || !definition.Formals[elementTerm.FormalOrdinal]
                    .RequiresWritable)
            {
                throw Unsupported(
                    $"Generic MethodDef '{definition.Name}' has a writable Span element without the formal's write requirement.");
            }

        }

        if (qualifiers == 0
            && elementTerm.Kind
                == BoundGenericMethodTypeTermKind.Concrete
            && !formalTypeSpecRows.ContainsKey(elementTerm.ConcreteType)
            && !BoundSpanTypeTypes.IsValidType(
                elementTerm.ConcreteType,
                definition.ProofProgram.SpanTypes))
        {
            var builtinIndex = Array.IndexOf(s_types,
                elementTerm.ConcreteType);
            if (builtinIndex >= 0)
            {
                return 0xF200_0000u | s_codes[builtinIndex];
            }
        }

        uint typeSpecRef;
        if (elementTerm.Kind == BoundGenericMethodTypeTermKind.Formal)
        {
            typeSpecRef = GenericMethodTypeDefaultSpecRef(elementTerm,
                formalOrdinal, definition, formalTypeSpecRows,
                builtInTypeSpecRows, typeSpecRows);
        }
        else if (elementTerm.Kind == BoundGenericMethodTypeTermKind.Concrete)
        {
            if (elementTerm.AllowsVoid
                || elementTerm.ConcreteType == PrimitiveType.Void)
            {
                throw Unsupported("A G1 Span element cannot be void.");
            }
            typeSpecRef = GenericMethodTypeSpecRef(
                elementTerm.ConcreteType, formalTypeSpecRows,
                builtInTypeSpecRows, typeSpecRows,
                definition.ProofProgram.SpanTypes,
                definition.ProofProgram.CheckedReferenceTypes,
                definition.DefinitionOrdinal, definition,
                allowSpanType: true,
                allowFormal: false);
        }
        else if (elementTerm.Kind is
            BoundGenericMethodTypeTermKind.Span
                or BoundGenericMethodTypeTermKind.CheckedReference)
        {
            typeSpecRef = GenericMethodTypeDefaultSpecRef(elementTerm,
                formalOrdinal, definition, formalTypeSpecRows,
                builtInTypeSpecRows, typeSpecRows);
        }
        else
        {
            throw Unsupported(
                $"Generic MethodDef '{definition.Name}' has a Span default element outside the selected type terms.");
        }

        return qualifiers == 0
            ? typeSpecRef
            : AddGenericTypeSpecQualifiers(typeSpecRef, qualifiers,
                typeSpecRows);
    }

    private static List<TrRmExportRow> ReadExportRows(
        IReadOnlyList<TrRmSection> sections,
        HashSet<uint> removedDefinitions)
    {
        var rows = new List<TrRmExportRow>();
        if (!TryGetSection(sections, 0x001D, out var section))
        {
            return rows;
        }

        for (var index = 0; index < section.Bytes.Length / 8; index++)
        {
            var offset = index * 8;
            var localDefId = BinaryPrimitives.ReadUInt32LittleEndian(
                section.Bytes.Span.Slice(offset, 4));
            if (!removedDefinitions.Contains(localDefId))
            {
                rows.Add(new TrRmExportRow(localDefId,
                    ReadInt(section.Bytes.Span, offset + 4)));
            }
        }

        return rows;
    }

    private static List<TrRmGenericParamRow> ReadGenericParamRows(
        IReadOnlyList<TrRmSection> sections)
    {
        var count = GetCount(sections, 0x0019);
        if (count == 0)
        {
            return [];
        }

        var section = GetSection(sections, 0x0019);
        if (section.Bytes.Length != checked(count * 32))
        {
            throw new InvalidDataException(
                "The GenericParam table has an invalid fixed-width extent.");
        }

        var rows = new List<TrRmGenericParamRow>(count);
        for (var index = 0; index < count; index++)
        {
            var data = section.Bytes.Span.Slice(index * 32, 32);
            if (data[23] != 0)
            {
                throw new InvalidDataException(
                    "The GenericParam table contains a nonzero reserved byte.");
            }

            rows.Add(new TrRmGenericParamRow(
                BinaryPrimitives.ReadUInt32LittleEndian(data),
                ReadInt(data, 4), ReadInt(data, 8), ReadInt(data, 12),
                ReadInt(data, 16), data[20], data[21], data[22],
                BinaryPrimitives.ReadUInt32LittleEndian(data[24..]),
                BinaryPrimitives.ReadUInt32LittleEndian(data[28..])));
        }

        return rows;
    }

    private static List<TrRmConstraintRow> ReadConstraintRows(
        IReadOnlyList<TrRmSection> sections)
    {
        var count = GetCount(sections, 0x001A);
        if (count == 0)
        {
            return [];
        }

        var section = GetSection(sections, 0x001A);
        if (section.Bytes.Length != checked(count * 12))
        {
            throw new InvalidDataException(
                "The Constraint table has an invalid fixed-width extent.");
        }

        var rows = new List<TrRmConstraintRow>(count);
        for (var index = 0; index < count; index++)
        {
            var data = section.Bytes.Span.Slice(index * 12, 12);
            if (BinaryPrimitives.ReadUInt16LittleEndian(data[6..]) != 0)
            {
                throw new InvalidDataException(
                    "The Constraint table contains nonzero reserved flags.");
            }

            rows.Add(new TrRmConstraintRow(ReadInt(data, 0),
                BinaryPrimitives.ReadUInt16LittleEndian(data[4..]),
                BinaryPrimitives.ReadUInt32LittleEndian(data[8..])));
        }

        return rows;
    }

    private static List<TrRmAssociatedTypeDefRow>
        ReadAssociatedTypeDefRows(IReadOnlyList<TrRmSection> sections)
    {
        var count = GetCount(sections, 0x0027);
        if (count == 0)
        {
            return [];
        }

        var section = GetSection(sections, 0x0027);
        if (section.Bytes.Length != checked(count * 12))
        {
            throw new InvalidDataException(
                "The AssociatedTypeDef table has an invalid fixed-width extent.");
        }

        var rows = new List<TrRmAssociatedTypeDefRow>(count);
        for (var index = 0; index < count; index++)
        {
            var data = section.Bytes.Span.Slice(index * 12, 12);
            rows.Add(new TrRmAssociatedTypeDefRow(ReadInt(data, 0),
                ReadInt(data, 4),
                BinaryPrimitives.ReadUInt32LittleEndian(data[8..])));
        }

        return rows;
    }

    private static List<TrRmAssociatedRelationRow>
        ReadAssociatedRelationRows(IReadOnlyList<TrRmSection> sections)
    {
        var count = GetCount(sections, 0x0036);
        if (count == 0)
        {
            return [];
        }

        var section = GetSection(sections, 0x0036);
        if (section.Bytes.Length != checked(count * 12))
        {
            throw new InvalidDataException(
                "The AssociatedRelation table has an invalid fixed-width extent.");
        }

        var rows = new List<TrRmAssociatedRelationRow>(count);
        for (var index = 0; index < count; index++)
        {
            var data = section.Bytes.Span.Slice(index * 12, 12);
            rows.Add(new TrRmAssociatedRelationRow(
                BinaryPrimitives.ReadUInt32LittleEndian(data),
                BinaryPrimitives.ReadUInt32LittleEndian(data[4..]),
                BinaryPrimitives.ReadUInt32LittleEndian(data[8..])));
        }

        return rows;
    }

    private static List<ReadOnlyMemory<byte>> ReadVariableRows(
        IReadOnlyList<TrRmSection> sections, ushort kind,
        int minimumRowBytes, int rowAlignment)
    {
        if (!TryGetSection(sections, kind, out var section))
        {
            return [];
        }

        var index = new TrRmVariableTableIndex(section.Bytes.Span,
            checked((int)section.RecordCount), minimumRowBytes,
            rowAlignment);
        var rows = new List<ReadOnlyMemory<byte>>(index.Count);
        for (var row = 1; row <= index.Count; row++)
        {
            rows.Add(index.GetRow(row).ToArray());
        }
        return rows;
    }

    private static ReadOnlyMemory<byte> EncodeGenericMethodEffectContract(
        int methodRow, ReadOnlyMemory<byte> effectMask)
    {
        if (effectMask.Length != sizeof(uint))
        {
            throw new InvalidOperationException(
                "The generic method effect payload has an invalid extent.");
        }

        var row = new byte[8 + effectMask.Length];
        WriteUInt32(row, 0, 0x1600_0000u | checked((uint)methodRow));
        BinaryPrimitives.WriteUInt16LittleEndian(row.AsSpan(4, 2), 1);
        effectMask.Span.CopyTo(row.AsSpan(8));
        return row;
    }

    private static ImmutableArray<ReadOnlyMemory<byte>>
        EncodeGenericMethodDependencyContracts(
            BoundFunction function, int methodRow)
    {
        var dependencies = function.DependencyContracts;
        if (dependencies.IsDefault)
        {
            throw Unsupported(
                "A G1 Generic MethodDef has an invalid dependency-contract set.");
        }
        if (dependencies.IsEmpty)
        {
            return [];
        }

        BoundDependencyContract? dependsOn = null;
        BoundDependencyContract? forwardsBacking = null;
        BoundDependencyContract? borrowSourceStorage = null;
        foreach (var dependency in dependencies)
        {
            if (dependency.Destination
                    != BoundDependencyDestination.Result
                || dependency.DestinationOrdinal is not null
                || dependency.Sources.IsDefault
                || dependency.Relation is not (
                    BoundDependencyRelation.DependsOn
                    or BoundDependencyRelation.ForwardsBacking
                    or BoundDependencyRelation.BorrowSourceStorage))
            {
                throw Unsupported(
                    "G1 generic MethodDefs currently support only ordinary result dependency contracts.");
            }

            if (dependency.Relation == BoundDependencyRelation.DependsOn)
            {
                if (dependsOn is not null)
                {
                    throw Unsupported(
                        "A G1 generic MethodDef has multiple ordinary result DependsOn contracts.");
                }
                dependsOn = dependency;
            }
            else if (dependency.Relation
                == BoundDependencyRelation.ForwardsBacking)
            {
                if (forwardsBacking is not null)
                {
                    throw Unsupported(
                        "A G1 generic MethodDef has multiple ordinary result ForwardsBacking contracts.");
                }
                forwardsBacking = dependency;
            }
            else
            {
                if (borrowSourceStorage is not null)
                {
                    throw Unsupported(
                        "A G1 generic MethodDef has multiple ordinary result BorrowSourceStorage contracts.");
                }
                borrowSourceStorage = dependency;
            }
        }

        if (dependsOn is not BoundDependencyContract declared
            || (forwardsBacking is BoundDependencyContract forwarded
                && (declared.Sources.IsEmpty
                    || forwarded.Sources.Distinct().Count()
                        != forwarded.Sources.Length
                    || !forwarded.Sources.All(declared.Sources.Contains)))
            || (borrowSourceStorage is BoundDependencyContract borrowed
                && (declared.Sources.IsEmpty
                    || borrowed.Sources.Distinct().Count()
                        != borrowed.Sources.Length
                    || !borrowed.Sources.All(declared.Sources.Contains))))
        {
            throw Unsupported(
                "A G1 generic MethodDef has a result refinement source outside its declared DependsOn set.");
        }

        var orderedSources = declared.Sources
            .OrderBy(static source => source.Kind)
            .ThenBy(static source => source.FormalOrdinal ?? -1)
            .ToArray();
        if (orderedSources.Distinct().Count() != orderedSources.Length
            || orderedSources.Any(source =>
                source.Kind != BoundDependencySourceKind.Formal
                || source.FormalOrdinal is not int ordinal
                || (uint)ordinal >= (uint)function.ParameterCount))
        {
            throw Unsupported(
                "G1 generic MethodDef result dependencies must name unique method formals.");
        }

        var forwardedSources = new HashSet<BoundDependencySource>();
        if (forwardsBacking is BoundDependencyContract backing)
        {
            forwardedSources.UnionWith(backing.Sources);
        }
        var storageSources = borrowSourceStorage
            is BoundDependencyContract storage
                ? storage.Sources.ToHashSet()
                : new HashSet<BoundDependencySource>();
        if ((orderedSources.Length == 0
                && (!forwardedSources.IsEmpty || !storageSources.IsEmpty))
            || storageSources.Overlaps(forwardedSources))
        {
            throw Unsupported(
                "G1 generic MethodDef result dependency refinements must be nonempty and disjoint.");
        }

        var rows = ImmutableArray.CreateBuilder<ReadOnlyMemory<byte>>(
            Math.Max(1, orderedSources.Length));
        if (orderedSources.Length == 0)
        {
            rows.Add(EncodeGenericMethodDependencyRow(methodRow,
                sourceKind: 0, relationship: 0, sourceOrdinal: 0));
            return rows.MoveToImmutable();
        }

        foreach (var source in orderedSources)
        {
            var ordinal = source.FormalOrdinal!.Value;
            rows.Add(EncodeGenericMethodDependencyRow(methodRow,
                sourceKind: 1,
                relationship: storageSources.Contains(source)
                    ? (byte)1
                    : forwardedSources.Contains(source) ? (byte)2 : (byte)3,
                checked((uint)ordinal)));
        }
        return rows.MoveToImmutable();
    }

    private static ReadOnlyMemory<byte> EncodeGenericMethodDependencyRow(
        int methodRow, byte sourceKind, byte relationship,
        uint sourceOrdinal)
    {
        var row = new byte[20];
        WriteUInt32(row, 0, 0x1600_0000u | checked((uint)methodRow));
        BinaryPrimitives.WriteUInt16LittleEndian(row.AsSpan(4, 2), 4);
        row[8] = 1;
        row[9] = sourceKind;
        row[10] = relationship;
        row[11] = 1;
        BinaryPrimitives.WriteUInt32LittleEndian(row.AsSpan(16),
            sourceOrdinal);
        return row;
    }

    private static byte[] EncodeExportRows(
        List<TrRmExportRow> rows)
    {
        if (rows.Count == 0)
        {
            return [];
        }

        var bytes = new byte[checked(rows.Count * 8)];
        for (var index = 0; index < rows.Count; index++)
        {
            var offset = index * 8;
            WriteUInt32(bytes, offset, rows[index].LocalDefId);
            WriteUInt32(bytes, offset + 4,
                checked((uint)rows[index].ExportKeyStringRow));
        }
        return bytes;
    }

    private static List<TrRmFeatureRow> ReadFeatureRows(
        IReadOnlyList<TrRmSection> sections)
    {
        var rows = new List<TrRmFeatureRow>();
        if (!TryGetSection(sections, 0x0004, out var section))
        {
            return rows;
        }

        for (var index = 0; index < section.Bytes.Length / 8; index++)
        {
            rows.Add(new TrRmFeatureRow(
                BinaryPrimitives.ReadUInt32LittleEndian(
                    section.Bytes.Span.Slice(index * 8, 4)),
                BinaryPrimitives.ReadUInt32LittleEndian(
                    section.Bytes.Span.Slice((index * 8) + 4, 4))));
        }
        return rows;
    }

    private static uint GenericMethodTypeSpecRef(PrimitiveType type,
        Dictionary<PrimitiveType, uint> formalTypeSpecRows,
        Dictionary<PrimitiveType, uint> builtInTypeSpecRows,
        List<ReadOnlyMemory<byte>> typeSpecRows,
        ImmutableArray<BoundSpanTypeDefinition> spanTypes,
        ImmutableArray<CheckedReferenceTypeDefinition>
            checkedReferenceTypes,
        int definitionOrdinal, BoundGenericMethodDefinition definition,
        bool allowSpanType = false,
        bool allowFormal = true,
        GenericMethodNominalTypeWriter? nominalTypes = null,
        int depth = 0)
    {
        if (depth > SemanticVerifier.MaximumDepth)
        {
            throw Unsupported(
                "A G1 source type exceeds the supported nesting depth.");
        }
        if (BoundGenericNominalTypeTypes.IsType(type))
        {
            return nominalTypes is null
                ? throw Unsupported(
                    "This G1 type position does not select nominal TypeSpec emission.")
                : nominalTypes.TypeSpecRef(type, definition,
                    formalTypeSpecRows, builtInTypeSpecRows, depth + 1);
        }
        if (BoundSpanTypeTypes.IsValidType(type, spanTypes))
        {
            if (!allowSpanType)
            {
                throw Unsupported(
                    "The selected G1 profile does not admit Span values in MethodSig or Body value types.");
            }
            if (builtInTypeSpecRows.TryGetValue(type,
                    out var existingSpanTypeSpecRef))
            {
                return existingSpanTypeSpecRef;
            }

            var spanType = spanTypes[BoundSpanTypeTypes.Index(type)];
            var isImportedFormalOwner
                = definition.FormalTypeOwnerOrdinal >= 0
                    && spanType.ElementFormalDefinitionOrdinal
                        == BoundSpanTypeContract
                            .ImportedFormalDefinitionOrdinal
                            - definition.FormalTypeOwnerOrdinal;
            if ((spanType.ElementFormalDefinitionOrdinal >= 0
                    && spanType.ElementFormalDefinitionOrdinal
                        != definitionOrdinal)
                || (spanType.ElementFormalDefinitionOrdinal < -1
                    && !isImportedFormalOwner))
            {
                throw Unsupported(
                    "A G1 Span element formal belongs to a different Generic MethodDef.");
            }
            var hasFormalElementOwner
                = (definitionOrdinal >= 0
                    && spanType.ElementFormalDefinitionOrdinal
                        == definitionOrdinal)
                    || isImportedFormalOwner;
            if (hasFormalElementOwner
                && !formalTypeSpecRows.ContainsKey(spanType.ElementType))
            {
                throw Unsupported(
                    "A G1 Span element formal is not owned by the emitted Generic MethodDef.");
            }

            BoundGenericMethodTypeTerm elementTerm;
            if (hasFormalElementOwner)
            {
                if ((uint)spanType.ElementFormalOrdinal
                    >= (uint)formalTypeSpecRows.Count)
                {
                    throw Unsupported(
                        "A G1 Span element formal is outside the emitted Generic MethodDef.");
                }
                elementTerm = new BoundGenericMethodTypeTerm(
                    BoundGenericMethodTypeTermKind.Formal,
                    PrimitiveType.Void, spanType.ElementFormalOrdinal, -1,
                    [], false, false, false, false);
            }
            else
            {
                if (spanType.ElementFormalDefinitionOrdinal
                        != -1
                    || spanType.IsElementWritable)
                {
                    throw Unsupported(
                        "A G1 Span element formal belongs to an unsupported owner.");
                }
                elementTerm = new BoundGenericMethodTypeTerm(
                    BoundGenericMethodTypeTermKind.Concrete,
                    spanType.ElementType, -1, -1, [],
                    false, false, false, false) {
                    AllowsVoid = spanType.ElementType == PrimitiveType.Void,
                };
            }

            var isElementWritable = spanType.IsElementWritable;
            if (isElementWritable)
            {
                if ((uint)spanType.ElementFormalOrdinal
                        >= (uint)definition.Formals.Length
                    || !definition.Formals[
                        spanType.ElementFormalOrdinal].RequiresWritable)
                {
                    throw Unsupported(
                        "A G1 writable Span element formal requires its declared write property.");
                }
            }

            var elementQualifiers = (byte)(
                (isElementWritable ? 0x01 : 0)
                | (spanType.IsInitialization ? 0x04 : 0));
            var elementTypeSpecRef = nominalTypes is null
                ? GenericMethodSpanArgumentTypeSpecRef(
                    elementTerm, elementQualifiers,
                    definition.Formals.Length,
                    definition, formalTypeSpecRows, builtInTypeSpecRows,
                    typeSpecRows)
                : GenericMethodTypeSpecRef(spanType.ElementType,
                    formalTypeSpecRows, builtInTypeSpecRows, typeSpecRows,
                    spanTypes, checkedReferenceTypes, definitionOrdinal,
                    definition, allowSpanType: true, allowFormal,
                    nominalTypes, depth + 1);
            if (nominalTypes is not null && elementQualifiers != 0)
            {
                elementTypeSpecRef = AddGenericTypeSpecQualifiers(
                    elementTypeSpecRef, elementQualifiers, typeSpecRows);
            }

            var typeSpecRef = checked((uint)typeSpecRows.Count + 1);
            typeSpecRows.Add(TrRmTypeSpecTableWriter.IndexedFamily(0x8A,
                elementTypeSpecRef, spanType.IsWritable ? (byte)0x01 : (byte)0));
            builtInTypeSpecRows.Add(type, typeSpecRef);
            return typeSpecRef;
        }

        if (CheckedReferenceTypeContract.IsValidCompleteType(type,
                checkedReferenceTypes))
        {
            var referenceType = checkedReferenceTypes[
                CheckedReferenceTypes.Index(type)];
            if (referenceType.IsOptionalValue
                || (referenceType.IsGarbageCollected
                    && referenceType.IsInitialization)
                || (referenceType.IsOptional
                    && referenceType.IsInitialization))
            {
                throw Unsupported(
                    "The selected G1 profile does not support this checked-reference qualifier combination.");
            }
            var referentTypeSpecRef = GenericMethodTypeSpecRef(
                referenceType.ReferentType, formalTypeSpecRows,
                builtInTypeSpecRows, typeSpecRows, spanTypes,
                checkedReferenceTypes, definitionOrdinal, definition,
                allowSpanType, allowFormal, nominalTypes, depth + 1);
            if (referenceType.IsReferentWritable)
            {
                if (!formalTypeSpecRows.ContainsKey(
                        referenceType.ReferentType))
                {
                    throw Unsupported(
                        "A G1 initialization-reference write referent must be a formal owned by the emitted Generic MethodDef.");
                }
                var hasWritableFormal = false;
                foreach (var formal in definition.Formals)
                {
                    if (formal.PlaceholderType
                        != referenceType.ReferentType)
                    {
                        continue;
                    }
                    hasWritableFormal = formal.RequiresWritable;
                    break;
                }
                if (!hasWritableFormal)
                {
                    throw Unsupported(
                        "A G1 write-qualified reference referent requires its formal's write property.");
                }
            }
            var referenceTypeSpecRef = checked(
                (uint)typeSpecRows.Count + 1);
            var qualifiers = (byte)((referenceType.IsWritable ? 0x01 : 0)
                | (referenceType.IsGarbageCollected ? 0x02 : 0));
            typeSpecRows.Add(referenceType.IsInitialization
                ? TrRmTypeSpecTableWriter.InitializationReference(
                    referentTypeSpecRef, referenceType.IsWritable)
                : TrRmTypeSpecTableWriter.Wrapped(0x07,
                    referentTypeSpecRef, qualifiers));
            if (!referenceType.IsOptional)
            {
                return referenceTypeSpecRef;
            }

            var optionalTypeSpecRef = checked(
                (uint)typeSpecRows.Count + 1);
            typeSpecRows.Add(TrRmTypeSpecTableWriter.Wrapped(0x06,
                referenceTypeSpecRef));
            return optionalTypeSpecRef;
        }

        return allowFormal
            && formalTypeSpecRows.TryGetValue(type, out var formalTypeSpec)
            ? formalTypeSpec
            : GetBuiltInTypeSpecRef(type, builtInTypeSpecRows,
                typeSpecRows);
    }

    private static uint AddGenericTypeSpecQualifiers(uint typeSpecRef,
        byte qualifiers, List<ReadOnlyMemory<byte>> typeSpecRows)
    {
        if (typeSpecRef == 0
            || typeSpecRef > (uint)typeSpecRows.Count)
        {
            throw Unsupported(
                "A G1 qualified type has no row-backed TypeSpec reference.");
        }

        var typeSpec = typeSpecRows[checked((int)typeSpecRef - 1)].Span;
        if (typeSpec.Length < 2)
        {
            throw Unsupported(
                "A G1 qualified type has an invalid TypeSpec row.");
        }

        var qualifiedTypeSpec = typeSpec.ToArray();
        qualifiedTypeSpec[1] |= qualifiers;
        var qualifiedTypeSpecRef = checked((uint)typeSpecRows.Count + 1);
        typeSpecRows.Add(qualifiedTypeSpec);
        return qualifiedTypeSpecRef;
    }

    private static void EnsureGenericBodyTypeMap(bool hasFloatingTypes,
        bool hasNativeWidthTypes, List<ReadOnlyMemory<byte>> typeSpecRows)
    {
        foreach (var type in CompactBuiltinTypeMap(hasFloatingTypes,
            hasNativeWidthTypes))
        {
            var typeRow = TypeRow(type);
            if (typeRow > typeSpecRows.Count
                || !typeSpecRows[typeRow - 1].Span.SequenceEqual(
                    TrRmTypeSpecTableWriter.Builtin(
                        s_codes[Array.IndexOf(s_types, type)])))
            {
                throw Unsupported(
                    $"Generic method Body type map for '{type}' is not representable in the selected Standard type table.");
            }
        }
    }

    private static Dictionary<PrimitiveType, uint> GetBuiltInTypeSpecRows(
        List<ReadOnlyMemory<byte>> typeSpecRows)
    {
        var result = new Dictionary<PrimitiveType, uint>();
        for (var index = 0; index < typeSpecRows.Count; index++)
        {
            var row = typeSpecRows[index].Span;
            if (row.Length != 3 || row[0] != 0x01 || row[1] != 0)
            {
                continue;
            }

            var typeIndex = Array.IndexOf(s_codes, row[2]);
            if (typeIndex >= 0)
            {
                _ = result.TryAdd(s_types[typeIndex],
                    checked((uint)index + 1));
            }
        }

        return result;
    }

    private static uint GetBuiltInTypeSpecRef(PrimitiveType type,
        Dictionary<PrimitiveType, uint> builtInTypeSpecRows,
        List<ReadOnlyMemory<byte>> typeSpecRows)
    {
        if (builtInTypeSpecRows.TryGetValue(type, out var typeSpecRef))
        {
            return typeSpecRef;
        }

        var typeIndex = Array.IndexOf(s_types, type);
        if (typeIndex < 0)
        {
            throw Unsupported(
                $"Generic MethodDef type '{type}' has no G1 source TypeSpec.");
        }

        typeSpecRef = checked((uint)typeSpecRows.Count + 1);
        typeSpecRows.Add(TrRmTypeSpecTableWriter.Builtin(
            s_codes[typeIndex]));
        builtInTypeSpecRows.Add(type, typeSpecRef);
        return typeSpecRef;
    }

    private static bool IsGenericMethodInitializationReferenceType(
        PrimitiveType type,
        ImmutableArray<CheckedReferenceTypeDefinition> checkedReferenceTypes)
    {
        if (!CheckedReferenceTypeContract.IsValidType(type,
                checkedReferenceTypes))
        {
            return false;
        }

        var reference = checkedReferenceTypes[
            CheckedReferenceTypes.Index(type)];
        return reference.IsInitialization
            && !reference.IsGarbageCollected
            && !reference.IsOptional
            && !reference.IsOptionalValue;
    }

    private static bool GenericMethodTypeMatchesAfterSubstitution(
        PrimitiveType targetType, PrimitiveType callerType,
        BoundGenericMethodDefinition targetDefinition,
        BoundGenericMethodDefinition callerDefinition,
        ImmutableArray<PrimitiveType> typeArguments, int depth)
    {
        if (depth > SemanticVerifier.MaximumDepth
            || typeArguments.Length != targetDefinition.Formals.Length)
        {
            return false;
        }

        for (var ordinal = 0; ordinal < targetDefinition.Formals.Length;
            ordinal++)
        {
            if (targetDefinition.Formals[ordinal].PlaceholderType
                == targetType)
            {
                return typeArguments[ordinal] == callerType;
            }
        }

        if (BoundGenericNominalTypeTypes.IsType(targetType)
            || BoundGenericNominalTypeTypes.IsType(callerType))
        {
            var targetProgram = targetDefinition.ProofProgram;
            var callerProgram = callerDefinition.ProofProgram;
            var targetFunction = targetProgram.Functions[
                targetDefinition.FunctionIndex];
            var callerFunction = callerProgram.Functions[
                callerDefinition.FunctionIndex];
            if (!SemanticVerifier.TryGetGenericMethodActualTypeTerm(
                    targetType, targetFunction, targetProgram, depth + 1,
                    out var targetTerm)
                || !SemanticVerifier.TryGetGenericMethodActualTypeTerm(
                    callerType, callerFunction, callerProgram, depth + 1,
                    out var callerTerm))
            {
                return false;
            }
            var actualTerms = ImmutableArray.CreateBuilder<
                BoundGenericMethodTypeTerm>(typeArguments.Length);
            foreach (var argument in typeArguments)
            {
                if (!SemanticVerifier.TryGetGenericMethodActualTypeTerm(
                        argument, callerFunction, callerProgram, depth + 1,
                        out var argumentTerm))
                {
                    return false;
                }
                actualTerms.Add(argumentTerm);
            }
            return BoundGenericMethodTypeTerms.TrySubstitute(targetTerm,
                    actualTerms.MoveToImmutable(), out var substituted)
                && BoundGenericMethodTypeTerms.AreEquivalent(substituted,
                    callerTerm, depth + 1);
        }

        var targetReferenceIsValid
            = CheckedReferenceTypeContract.IsValidCompleteType(targetType,
                targetDefinition.ProofProgram.CheckedReferenceTypes);
        var callerReferenceIsValid
            = CheckedReferenceTypeContract.IsValidCompleteType(callerType,
                callerDefinition.ProofProgram.CheckedReferenceTypes);
        if (targetReferenceIsValid || callerReferenceIsValid)
        {
            if (!targetReferenceIsValid || !callerReferenceIsValid)
            {
                return false;
            }

            var targetReference = targetDefinition.ProofProgram
                .CheckedReferenceTypes[CheckedReferenceTypes.Index(
                    targetType)];
            var callerReference = callerDefinition.ProofProgram
                .CheckedReferenceTypes[CheckedReferenceTypes.Index(
                    callerType)];
            return targetReference.IsWritable
                    == callerReference.IsWritable
                && targetReference.IsGarbageCollected
                    == callerReference.IsGarbageCollected
                && targetReference.IsInitialization
                    == callerReference.IsInitialization
                && targetReference.IsOptional
                    == callerReference.IsOptional
                && targetReference.IsOptionalValue
                    == callerReference.IsOptionalValue
                && targetReference.IsReferentWritable
                    == callerReference.IsReferentWritable
                && GenericMethodTypeMatchesAfterSubstitution(
                    targetReference.ReferentType,
                    callerReference.ReferentType, targetDefinition,
                    callerDefinition, typeArguments, depth + 1);
        }

        var targetSpanIsValid = BoundSpanTypeTypes.IsValidType(targetType,
            targetDefinition.ProofProgram.SpanTypes);
        var callerSpanIsValid = BoundSpanTypeTypes.IsValidType(callerType,
            callerDefinition.ProofProgram.SpanTypes);
        if (targetSpanIsValid || callerSpanIsValid)
        {
            if (!targetSpanIsValid || !callerSpanIsValid)
            {
                return false;
            }

            var targetSpan = targetDefinition.ProofProgram.SpanTypes[
                BoundSpanTypeTypes.Index(targetType)];
            var callerSpan = callerDefinition.ProofProgram.SpanTypes[
                BoundSpanTypeTypes.Index(callerType)];
            if (targetSpan.IsElementWritable
                    != callerSpan.IsElementWritable
                || targetSpan.IsWritable != callerSpan.IsWritable
                || targetSpan.IsInitialization
                    != callerSpan.IsInitialization)
            {
                return false;
            }

            var targetHasFormalElement
                = (targetDefinition.DefinitionOrdinal >= 0
                    && targetSpan.ElementFormalDefinitionOrdinal
                        == targetDefinition.DefinitionOrdinal)
                || (targetDefinition.FormalTypeOwnerOrdinal >= 0
                    && targetSpan.ElementFormalDefinitionOrdinal
                        == BoundSpanTypeContract
                            .ImportedFormalDefinitionOrdinal
                            - targetDefinition.FormalTypeOwnerOrdinal);
            if (targetHasFormalElement)
            {
                var targetFormalOrdinal
                    = targetSpan.ElementFormalOrdinal;
                if ((uint)targetFormalOrdinal
                        >= (uint)typeArguments.Length
                    || targetDefinition.Formals[targetFormalOrdinal]
                        .PlaceholderType != targetSpan.ElementType
                    || callerSpan.ElementType
                        != typeArguments[targetFormalOrdinal])
                {
                    return false;
                }

                var callerFormalOrdinal = -1;
                for (var ordinal = 0;
                    ordinal < callerDefinition.Formals.Length; ordinal++)
                {
                    if (callerDefinition.Formals[ordinal].PlaceholderType
                        == callerSpan.ElementType)
                    {
                        callerFormalOrdinal = ordinal;
                        break;
                    }
                }

                return callerFormalOrdinal >= 0
                    ? callerSpan.ElementFormalDefinitionOrdinal
                        == callerDefinition.DefinitionOrdinal
                        && callerSpan.ElementFormalOrdinal
                            == callerFormalOrdinal
                    : callerSpan.ElementFormalDefinitionOrdinal == -1
                        && !targetSpan.IsElementWritable;
            }

            return targetSpan.ElementFormalDefinitionOrdinal == -1
                && callerSpan.ElementFormalDefinitionOrdinal == -1
                && GenericMethodTypeMatchesAfterSubstitution(
                    targetSpan.ElementType, callerSpan.ElementType,
                    targetDefinition, callerDefinition, typeArguments,
                    depth + 1);
        }

        return targetType == callerType;
    }

    private static TrRmSection GetSection(
        IReadOnlyList<TrRmSection> sections, ushort kind)
        => TryGetSection(sections, kind, out var section)
            ? section
            : throw new InvalidDataException(
                $"TRRM section 0x{kind:X4} is required.");

    private static bool TryGetSection(IReadOnlyList<TrRmSection> sections,
        ushort kind, out TrRmSection section)
    {
        foreach (var candidate in sections)
        {
            if (candidate.Kind == kind)
            {
                section = candidate;
                return true;
            }
        }
        section = default;
        return false;
    }

    private static int GetCount(IReadOnlyList<TrRmSection> sections,
        ushort kind)
        => TryGetSection(sections, kind, out var section)
            ? checked((int)section.RecordCount) : 0;

    private static void SetSection(List<TrRmSection> sections, ushort kind,
        uint recordCount, byte[] bytes)
    {
        var index = sections.FindIndex(section => section.Kind == kind);
        if (recordCount == 0)
        {
            if (index >= 0)
            {
                sections.RemoveAt(index);
            }
            return;
        }

        var flags = index >= 0 ? sections[index].Flags : (ushort)0;
        var replacement = new TrRmSection(kind, flags, recordCount, bytes);
        if (index >= 0)
        {
            sections[index] = replacement;
        }
        else
        {
            sections.Add(replacement);
        }
    }

    private static void AppendRowsSection(List<TrRmSection> sections,
        ushort kind, uint recordCount, byte[] appendedBytes)
    {
        var existingBytes = TryGetSection(sections, kind, out var existing)
            ? existing.Bytes.ToArray() : [];
        var combined = new byte[checked(existingBytes.Length
            + appendedBytes.Length)];
        existingBytes.CopyTo(combined, 0);
        appendedBytes.CopyTo(combined, existingBytes.Length);
        SetSection(sections, kind, recordCount, combined);
    }

    private static int ReadInt(ReadOnlySpan<byte> bytes, int offset)
        => checked((int)BinaryPrimitives.ReadUInt32LittleEndian(
            bytes.Slice(offset, sizeof(uint))));

    private static void AppendUInt32(List<byte> bytes, uint value)
    {
        bytes.Add((byte)value);
        bytes.Add((byte)(value >> 8));
        bytes.Add((byte)(value >> 16));
        bytes.Add((byte)(value >> 24));
    }

    private static void AppendMethodParameterSurfaceRow(
        List<byte> bytes, ref int count, TrRmArtifactSchema schema,
        uint methodDefRow, uint ordinal, uint nameStringRow,
        uint defaultConstantSourceRow = 0)
    {
        if (count >= 0xFF_FFFF
            || (!schema.UsesClassParameterDefaults()
                && !schema.UsesForeignNativeInterfaceParameterSurface()
                && defaultConstantSourceRow != 0))
        {
            throw Unsupported(
                "The MethodParameterSurface exceeds its selected row contract.");
        }

        if (schema.UsesForeignNativeInterfaceParameterSurface())
        {
            TrRmForeignInterfaceParameterSurfaceTableWriter.AppendRow(bytes,
                new TrRmForeignInterfaceParameterSurfaceRow(
                    methodDefRow, ordinal, nameStringRow,
                    defaultConstantSourceRow));
            count = checked(count + 1);
            return;
        }

        AppendUInt32(bytes, methodDefRow);
        AppendUInt32(bytes, ordinal);
        AppendUInt32(bytes, nameStringRow);
        if (schema.UsesClassParameterDefaults()
            || schema.UsesForeignNativeInterfaceParameterSurface())
        {
            AppendUInt32(bytes, defaultConstantSourceRow);
        }

        count = checked(count + 1);
    }

    private static TrRmFrozenScalarValueReference
        AddC1M1ParameterDefault(
            TrRmFrozenScalarValueBuilder? frozenValues,
            BoundConstant value, PrimitiveType parameterType,
            uint parameterTypeSpecRef)
    {
        if (frozenValues is null
            || value.Type != parameterType
            || value.RepresentationType != parameterType
            || (parameterType is PrimitiveType.NativeInt
                or PrimitiveType.NativeUInt)
            || !value.Bits.IsCanonicalFor(parameterType))
        {
            throw Unsupported(
                "CM1 parameter defaults require exact fixed-width SF1 scalar values.");
        }

        var frozenValue = frozenValues.AddScalarParameterDefault(value,
            parameterTypeSpecRef, parameterType);
        if (frozenValue.TypeSpecRef != parameterTypeSpecRef)
        {
            throw Unsupported(
                "CM1 parameter defaults must match the complete parameter TypeSpec.");
        }

        return frozenValue;
    }

    private static int GetMethodParameterSurfaceRowSize(
        TrRmArtifactSchema schema)
        => schema.UsesClassParameterDefaults()
            || schema.UsesForeignNativeInterfaceParameterSurface()
                ? 16 : 12;

    private static void WriteUInt32(List<byte> bytes, int offset,
        uint value)
    {
        bytes[offset] = (byte)value;
        bytes[offset + 1] = (byte)(value >> 8);
        bytes[offset + 2] = (byte)(value >> 16);
        bytes[offset + 3] = (byte)(value >> 24);
    }

    private static void WriteUInt32(byte[] bytes, int offset, uint value)
        => BinaryPrimitives.WriteUInt32LittleEndian(
            bytes.AsSpan(offset, sizeof(uint)), value);

}
