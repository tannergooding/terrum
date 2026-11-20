// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Terrum.CodeAnalysis.Lexing;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;

namespace Terrum.CodeAnalysis.Modules;

public static partial class TrRmExecutableCompiler
{
    private static TrRmArtifactSchema GetC1M1ClassMetadataSchema(
        string schemaIdentity)
        => schemaIdentity switch {
            StandardC1M1SchemaIdentity
                => TrRmArtifactSchema.C1M1Draft,
            StandardC1M1Construction2SchemaIdentity
                => TrRmArtifactSchema.C1M1Construction2Draft,
            StandardC1M1Construction2InitializerCopyEvidenceSchemaIdentity
                => TrRmArtifactSchema.C1M1Construction2InitializerCopyEvidenceDraft,
            StandardC1M1Construction2TypeBindingViewsSchemaIdentity
                => TrRmArtifactSchema.C1M1Construction2InitializerCopyEvidenceDraft,
            StandardC1M1Construction2ClassTypeBindingViewsSchemaIdentity
                => TrRmArtifactSchema.C1M1Construction2InitializerCopyEvidenceDraft,
            StandardC1M1ConstructionL1R1SchemaIdentity
                => TrRmArtifactSchema.C1M1ConstructionParameterLabelsReceiverAccessDraft,
            StandardC1M1Construction2L1R1SchemaIdentity
                => TrRmArtifactSchema.C1M1Construction2ParameterLabelsReceiverAccessDraft,
            StandardC1M1ConstructionL1R1EnumClosureInitializerCopyEvidenceSchemaIdentity
                => TrRmArtifactSchema.C1M1ConstructionL1R1EnumClosureInitializerCopyEvidenceDraft,
            StandardC1M1Construction2L1R1EnumClosureInitializerCopyEvidenceSchemaIdentity
                => TrRmArtifactSchema.C1M1Construction2L1R1EnumClosureInitializerCopyEvidenceDraft,
            StandardC1M1ConstructionL1R1ClassInstanceLayoutInitializerCopyEvidenceSchemaIdentity
                => TrRmArtifactSchema.C1M1ConstructionL1R1EnumClosureClassInstanceLayoutInitializerCopyEvidenceDraft,
            StandardC1M1Construction2L1R1ClassInstanceLayoutInitializerCopyEvidenceSchemaIdentity
                => TrRmArtifactSchema.C1M1Construction2L1R1EnumClosureClassInstanceLayoutInitializerCopyEvidenceDraft,
            StandardC1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsSchemaIdentity
                => TrRmArtifactSchema
                    .C1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsDraft,
            StandardC1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsInitializerCopyEvidenceSchemaIdentity
                => TrRmArtifactSchema
                    .C1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsInitializerCopyEvidenceDraft,
            StandardC1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsTypeBindingViewsSchemaIdentity
                => TrRmArtifactSchema
                    .C1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsInitializerCopyEvidenceDraft,
            StandardC1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsClassTypeBindingViewsSchemaIdentity
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
            StandardC1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsAssociatedRelationConstraintsClassTypeBindingViewsSchemaIdentity
                => TrRmArtifactSchema
                    .C1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsAssociatedRelationConstraintsInitializerCopyEvidenceDraft,
            _ => throw Unsupported(
                "Source-free class MethodDef re-emission requires an exact supported C1M1 schema selection."),
        };

    private sealed record C1M1TypeDefEmissionView(
        byte Kind, string Namespace, string Name, uint TypeDefFlags,
        ImmutableArray<BoundStandardClassFieldDeclaration> Fields,
        ImmutableArray<BoundStandardClassLayoutVariant> Layouts);

    private sealed record C1M1MethodEmissionPlan(
        BoundStandardClassTypeView Owner,
        BoundStandardClassMethodDeclaration Method,
        ImmutableArray<BoundStandardClassMethodExpectation> Imports,
        ImmutableArray<string> ExportKeys,
        ReadOnlyMemory<byte> Body);

    private sealed record C1M1ModuleFunctionEmissionPlan(
        int MethodDefRow, BoundFunction? Function,
        ImmutableArray<string> ExportKeys,
        ImmutableArray<int> ImportRows);

    private sealed record C1M1PrivateModuleFunctionEmissionPlan(
        int MethodDefRow);

    private readonly record struct C1M1GenericConstraintPlan(
        int MethodRow, int FormalOrdinal, int GenericParamRow,
        int Kind, uint Payload, int InterfaceConstraintOrdinal);

    private readonly record struct C1M1GenericMethodTypeSpecPlan(
        int MethodRow, int MethodSigRow, int ParameterRow,
        int ParameterOrdinal, bool IsResult);

    private readonly record struct C1M1GenericMethodDefaultTypeSpecPlan(
        int MethodRow, int GenericParamRow, int FormalOrdinal);

    private sealed record C1M1GenericMethodTypeEmissionContext(
        BoundGenericMethodImport Root,
        BoundGenericMethodDefinition Definition,
        Dictionary<PrimitiveType, uint> FormalTypeSpecRows,
        int[] GenericParamRows,
        Dictionary<PrimitiveType, uint> BuiltInTypeSpecRows);

    private sealed record C1M1Construction2SourceInitializer(
        int ClassIndex, BoundFunction Function, uint ClassTypeSpecReference,
        ImmutableArray<BoundFieldAssignment> FieldAssignments,
        int InitializerMethodDefRow, int ProjectionMethodDefRow);

    private sealed record C1M1Construction2SourceMethod(
        BoundFunction Function, int MethodDefRow,
        int DirectReturnHome = -1,
        int IdentityForwarderTargetFunctionIndex = -1,
        byte[]? EncodedBody = null,
        BoundBinary? CallBinaryExpression = null);

    private sealed record C1M1SourceImport(
        int ModuleRefRow, string ExportKey, uint ExpectedDefinitionId);

    private sealed record C1M1ImportedStructImportPlan(
        BoundSourceTypeProviderSnapshot Provider,
        ImmutableArray<string> ExportKeys);

    private sealed record C1M1ImportedClassImportPlan(
        BoundSourceTypeProviderSnapshot Provider,
        ImmutableArray<string> ExportKeys);

    private sealed record C1M1ImportedClassTypeDefViewPlan(
        int StandardClassIndex, BoundStandardClassTypeView Declaration,
        BoundSourceTypeProviderSnapshot Provider,
        ImmutableArray<string> ExportKeys,
        ImmutableArray<BoundStandardClassExpectation> Expectations,
        bool IsSelectedView);

    private sealed record C1M1SourceClassMethodImportPlan(
        BoundSourceTypeProviderSnapshot Provider, string ExportKey);

    private sealed record C1M1SourceClassMethodPlan(
        int StandardClassViewIndex,
        BoundStandardClassTypeView OwnerView,
        BoundStandardClassMethodDeclaration Method,
        int MethodRow,
        ImmutableArray<C1M1SourceClassMethodImportPlan> Imports,
        ImmutableArray<BoundGenericMethodImport> GenericMethodRoots,
        BoundSourceTypeProviderSnapshot? GenericMethodProvider);

    private sealed record C1M1SourceClassMethodGenericFormalPlan(
        int MethodRow, int GenericParamRow, int FormalOrdinal,
        BoundStandardClassMethodGenericParameter SourceFormal,
        ImmutableArray<BoundGenericMethodImport> Roots,
        BoundSourceTypeProviderSnapshot Provider);

    private static ImmutableArray<BoundGenericMethodImport>
        GetC1M1SourceClassMethodGenericRoots(
            BoundProgram program,
            BoundStandardClassMethodDeclaration method,
            BoundStandardClassTypeView methodView,
            BoundStandardClassExpectation? typeExpectation,
            BoundSourceTypeProviderSnapshot provider)
    {
        var digest = SHA256.HashData(provider.ArtifactSnapshot.AsSpan());
        var methodExpectations = typeExpectation is null
            ? Array.Empty<BoundStandardClassMethodExpectation>()
            : typeExpectation.MethodExpectations.Where(expectation =>
                expectation.ExpectedDefinitionId == method.DefinitionId)
                .ToArray();
        if (typeExpectation is not null && methodExpectations.Length == 0)
        {
            throw new InvalidDataException(
                "A retained class MethodDef with generic constraints has no exact method Import expectation.");
        }

        var roots = ImmutableArray.CreateBuilder<BoundGenericMethodImport>();
        foreach (var root in program.RetainedGenericMethodRoots)
        {
            var origin = root.Origin;
            if (origin.IsMethodDefTarget
                || !StringComparer.Ordinal.Equals(origin.ProviderIdentity,
                    provider.ModuleIdentity)
                || origin.TargetRequirement != provider.TargetRequirement
                || origin.RuntimePolicy != provider.RuntimePolicy
                || origin.ProviderArtifactDigest.IsDefault
                || !origin.ProviderArtifactDigest.AsSpan()
                    .SequenceEqual(digest)
                || root.Binding is not null)
            {
                continue;
            }

            var owner = origin.ClassOwner;
            if (owner is null
                || owner.Identity.DefinitionId >> 24 != 0x13)
            {
                continue;
            }

            if (methodExpectations.Length == 0)
            {
                if (origin.MethodDefId != method.DefinitionId
                    || owner.Identity.DefinitionId
                        != methodView.Identity.DefinitionId)
                {
                    continue;
                }

                roots.Add(root);
                continue;
            }

            foreach (var expectation in methodExpectations)
            {
                if (origin.MethodDefId
                        != expectation.ProviderDefinitionId
                    || !StringComparer.Ordinal.Equals(origin.ExportKey,
                        expectation.ExportKey)
                    || owner.Identity.DefinitionId
                        != expectation.ProviderOwnerDefinitionId
                    || owner.ExpectedDefinitionId
                        != expectation.ExpectedOwnerDefinitionId
                    || !StringComparer.Ordinal.Equals(owner.ExportKey,
                        typeExpectation!.Binding.ExportKey))
                {
                    continue;
                }

                roots.Add(root);
                break;
            }
        }

        if (roots.Count == 0)
        {
            throw new InvalidDataException(
                "A retained class MethodDef generic constraint has no exact typed provider root.");
        }

        return roots.Distinct().ToImmutableArray();
    }

    private static (BoundProgram Program,
        ImmutableArray<C1M1SourceClassMethodGenericFormalPlan> Plans)
        PrepareC1M1SourceClassMethodGenericRelationProgram(
            BoundProgram program,
            IReadOnlyList<C1M1SourceClassMethodGenericFormalPlan> plans)
    {
        var rootsBySource = plans.SelectMany(plan =>
                plan.Roots.Select(root => (Plan: plan, Root: root)))
            .GroupBy(static item => item.Root)
            .ToArray();
        var rewrittenRoots
            = new Dictionary<BoundGenericMethodImport,
                BoundGenericMethodImport>();
        var localInterfaceMappings
            = new List<(string Name, int Arity, BoundModuleImport Import,
                HashSet<(int Ordinal, string DeclarationKey)>
                    AssociatedMembers)>();
        foreach (var rootGroup in rootsBySource)
        {
            var sourceRoot = rootGroup.Key;
            var sourcePlan = rootGroup.First().Plan;
            if (rootGroup.Any(item =>
                    !SameA2SourceTypeProviderSnapshot(
                        item.Plan.Provider, sourcePlan.Provider)))
            {
                throw new InvalidDataException(
                    "A retained class MethodDef generic root has conflicting source Module snapshots.");
            }

            var provider = sourcePlan.Provider;
            var sourceArtifact = provider.ArtifactSnapshot.ToArray();
            var sourceIndex = new TrRmArtifactIndex(sourceArtifact,
                GetC1M1ClassMetadataSchema(provider.SchemaIdentity),
                standardSchemaIdentity: provider.SchemaIdentity);
            var sourceTables = TrRmStructuralMetadataValidator
                .ValidateC1AndGetExecutionTables(sourceIndex);
            if (provider.ModuleRow <= 0
                || provider.ModuleRow > sourceTables.Modules.Count
                || sourceRoot.Origin.MethodDefId >> 24 != 0x16
                || sourceRoot.Formals.IsDefault
                || sourceRoot.Formals.Any(static formal =>
                    formal is null
                    || formal.InterfaceConstraints.IsDefault
                    || formal.AssociatedRelations.IsDefault)
                || !StringComparer.Ordinal.Equals(
                    sourceTables.Modules.GetIdentity(provider.ModuleRow),
                    provider.ModuleIdentity))
            {
                throw new InvalidDataException(
                    "A retained class MethodDef generic root has an invalid source Module or MethodDef identity.");
            }

            var localImports
                = new Dictionary<(string Name, int Arity),
                    BoundModuleImport>();
            foreach (var formal in sourceRoot.Formals)
            {
                foreach (var constraint in formal.InterfaceConstraints)
                {
                    if (constraint.ModuleImport is null)
                    {
                        _ = GetC1M1AssociatedInterfaceModuleImport(
                            constraint, sourceTables, provider.ModuleRow,
                            provider, localImports);
                    }
                }

                foreach (var relation in formal.AssociatedRelations)
                {
                    var declaringInterface
                        = relation.AssociatedType.DeclaringInterface;
                    if (declaringInterface.ModuleImport is null)
                    {
                        _ = GetC1M1AssociatedInterfaceModuleImport(
                            declaringInterface, sourceTables,
                            provider.ModuleRow, provider, localImports);
                    }
                }
            }

            var rewrittenRoot = localImports.Count == 0 ? sourceRoot
                : RewriteC1M1AssociatedMethodImport(sourceRoot,
                    sourceTables, provider.ModuleRow, provider,
                    localImports);
            rewrittenRoots.Add(sourceRoot, rewrittenRoot);
            foreach (var (key, import) in localImports)
            {
                var associatedMembers = new HashSet<(
                    int Ordinal, string DeclarationKey)>();
                foreach (var relation in sourceRoot.Formals
                    .SelectMany(static formal => formal.AssociatedRelations))
                {
                    var declaringInterface
                        = relation.AssociatedType.DeclaringInterface;
                    if (declaringInterface.Kind
                            == BoundInterfaceTypeKind.Nominal
                        && StringComparer.Ordinal.Equals(
                            declaringInterface.NominalName, key.Name)
                        && (declaringInterface.TypeArguments.IsDefault
                                ? 0
                                : declaringInterface.TypeArguments.Length)
                            == key.Arity)
                    {
                        _ = associatedMembers.Add((
                            relation.AssociatedType.MemberOrdinal,
                            relation.AssociatedType.DeclarationKey));
                    }
                }

                if (!localInterfaceMappings.Any(candidate =>
                        candidate.Name == key.Name
                        && candidate.Arity == key.Arity
                        && candidate.Import == import))
                {
                    localInterfaceMappings.Add((key.Name, key.Arity,
                        import, associatedMembers));
                }
                else
                {
                    var mappingIndex = localInterfaceMappings.FindIndex(
                        candidate => candidate.Name == key.Name
                            && candidate.Arity == key.Arity
                            && candidate.Import == import);
                    _ = localInterfaceMappings[mappingIndex]
                        .AssociatedMembers.UnionWith(associatedMembers);
                }
            }
        }

        var matchedInterfaceMappings
            = new HashSet<(string Name, int Arity,
                BoundModuleImport Import)>();
        var interfaces
            = ImmutableArray.CreateBuilder<BoundInterfaceDefinition>(
                program.Interfaces.Length);
        foreach (var definition in program.Interfaces)
        {
            var key = (definition.QualifiedName,
                definition.Formals.Length);
            if (definition.ModuleImport is BoundModuleImport existingImport)
            {
                foreach (var mapping in localInterfaceMappings.Where(
                    mapping =>
                        StringComparer.Ordinal.Equals(mapping.Name,
                            key.Item1)
                        && mapping.Arity == key.Item2
                        && mapping.Import == existingImport))
                {
                    _ = matchedInterfaceMappings.Add((mapping.Name,
                        mapping.Arity, mapping.Import));
                }

                interfaces.Add(definition);
                continue;
            }

            var candidates = localInterfaceMappings.Where(mapping =>
                StringComparer.Ordinal.Equals(mapping.Name, key.Item1)
                && mapping.Arity == key.Item2
                && (mapping.AssociatedMembers.Count == 0
                    || mapping.AssociatedMembers.All(member =>
                        definition.Members.Any(candidate =>
                            candidate.Ordinal == member.Ordinal
                            && (StringComparer.Ordinal.Equals(
                                    candidate.DeclarationKey,
                                    member.DeclarationKey)
                                || candidate.AdditionalDeclarationKeys
                                    .Contains(member.DeclarationKey,
                                        StringComparer.Ordinal)))))
            ).ToArray();
            if (candidates.Length == 0)
            {
                interfaces.Add(definition);
                continue;
            }

            if (candidates.Length != 1)
            {
                throw new InvalidDataException(
                    "A retained class MethodDef relation has no unique exact local InterfaceDef declaration.");
            }

            var mapping = candidates[0];
            _ = matchedInterfaceMappings.Add((mapping.Name, mapping.Arity,
                mapping.Import));
            interfaces.Add(definition with {
                ModuleImport = mapping.Import,
            });
        }

        if (matchedInterfaceMappings.Count != localInterfaceMappings.Count)
        {
            throw new InvalidDataException(
                "A retained class MethodDef relation has no exact local InterfaceDef import/export closure.");
        }

        var retainedRoots
            = ImmutableArray.CreateBuilder<BoundGenericMethodImport>(
                program.RetainedGenericMethodRoots.Length);
        foreach (var root in program.RetainedGenericMethodRoots)
        {
            retainedRoots.Add(rewrittenRoots.TryGetValue(root,
                out var rewritten) ? rewritten : root);
        }

        var rewrittenProgram = program with {
            Interfaces = interfaces.MoveToImmutable(),
            RetainedGenericMethodRoots = retainedRoots.MoveToImmutable(),
        };
        var rewrittenPlans
            = ImmutableArray.CreateBuilder<
                C1M1SourceClassMethodGenericFormalPlan>(plans.Count);
        foreach (var plan in plans)
        {
            rewrittenPlans.Add(plan with {
                Roots = [.. plan.Roots.Select(root =>
                    rewrittenRoots.TryGetValue(root, out var rewritten)
                        ? rewritten : root)],
            });
        }

        return (rewrittenProgram, rewrittenPlans.MoveToImmutable());
    }

    private readonly record struct C1M1Construction2ConditionalReturn(
        BoundIf Root);

    private readonly record struct C1M1Construction2NestedFieldRead(
        ImmutableArray<int> FieldDefRows,
        ImmutableArray<PrimitiveType> FieldTypes,
        int BaseVariableIndex);

    private readonly record struct C1M1Construction2FieldReference(
        uint FieldToken);

    private readonly record struct C1M1Construction2NestedFieldWrite(
        ImmutableArray<int> FieldDefRows,
        ImmutableArray<PrimitiveType> FieldTypes,
        int BaseVariableIndex,
        BoundExpression Value);

    private readonly record struct C1M1ParameterSurfaceRow(
        int MethodDefRow, int Ordinal, string Name,
        uint DefaultConstantSourceRow);

    private readonly struct C1M1FieldPathIdentity
        : IEquatable<C1M1FieldPathIdentity>
    {
        private readonly uint _rootTypeSpecRef;
        private readonly ImmutableArray<uint> _fieldReferences;

        internal C1M1FieldPathIdentity(uint rootTypeSpecRef,
            ImmutableArray<uint> fieldReferences)
        {
            _rootTypeSpecRef = rootTypeSpecRef;
            _fieldReferences = fieldReferences;
        }

        public bool Equals(C1M1FieldPathIdentity other)
            => _rootTypeSpecRef == other._rootTypeSpecRef
                && _fieldReferences.AsSpan().SequenceEqual(
                    other._fieldReferences.AsSpan());

        public override bool Equals(object? obj)
            => obj is C1M1FieldPathIdentity other && Equals(other);

        public override int GetHashCode()
        {
            var hashCode = new HashCode();
            hashCode.Add(_rootTypeSpecRef);
            foreach (var fieldReference in _fieldReferences)
            {
                hashCode.Add(fieldReference);
            }

            return hashCode.ToHashCode();
        }
    }

    private static byte[] EmitC1M1SourceClassDeclarations(
        BoundProgram program, string moduleIdentity,
        uint moduleTargetRequirement, SourceClassExports sourceClassExports,
        out TrRmFrozenScalarValueBuilder frozenScalarValueBuilder,
        out ImmutableArray<C1M1SourceClassMethodGenericFormalPlan>
            sourceClassMethodGenericFormalPlans,
        TrRmArtifactSchema schema = TrRmArtifactSchema.C1M1Draft,
        string? standardSchemaIdentity = null)
    {
        sourceClassMethodGenericFormalPlans = [];
        var emitsConstruction2 = schema.UsesObjectConstruction();
        if (schema != TrRmArtifactSchema.C1M1Draft
            && !emitsConstruction2)
        {
            throw new ArgumentOutOfRangeException(nameof(schema));
        }

        var typeViews = program.StandardQuerySourceAssociation
            is BoundStandardQuerySourceAssociation typeViewAssociation
            && !typeViewAssociation.TypeViews.IsDefault
                ? typeViewAssociation.TypeViews
                : ImmutableArray<BoundStandardTypeView>.Empty;
        var hasTypeBindingEqualityGroups = program.Structures.Any(
            static structure =>
                structure.StandardTypeOrigin is
                    BoundStandardTypeOrigin origin
                && origin.ContextualViewIndexes.Length > 1);
        var hasClassContextualTypeViews
            = program.StandardClassDeclarations.Any(
                static declaration =>
                    declaration.Expectations.Length > 1
                    || !declaration.TypeDefBindingEqualityGroups.IsEmpty);
        var supportsClassTypeBindingViews
            = IsSelectedClassTypeDefBindingEqualitySchemaIdentity(
                standardSchemaIdentity);
        var hasTypeOnlyContext = typeViews.Length != 0
            && program.Classes.IsEmpty
            && sourceClassExports.Exports.IsEmpty;
        if (hasTypeBindingEqualityGroups
            && !IsSelectedTypeDefBindingEqualitySchemaIdentity(
                standardSchemaIdentity))
        {
            throw Unsupported(
                "Distinct contextual TypeDef expectations require an exact TypeBindingViews schema.");
        }
        if (hasTypeBindingEqualityGroups
            && typeViews.IsEmpty)
        {
            throw new InvalidDataException(
                "A contextual TypeDef equality group has no retained view graph.");
        }
        if (hasClassContextualTypeViews
            && !IsSelectedClassTypeDefBindingEqualitySchemaIdentity(
                standardSchemaIdentity))
        {
            throw Unsupported(
                "Distinct class TypeDef expectations require an exact class TypeBindingViews schema.");
        }

        SemanticVerifier.VerifyLibraryReferences(program);
        var standardClassIndexByIdentity
            = CreateC1M1StandardClassIndexByIdentity(program);
        var sourceStructDeclarations
            = CreateC1M1SourceStructDeclarations(program);
        if (!ReferenceEquals(sourceClassExports.Program, program)
            || program.EntryPoint != -1
            || !program.InterfaceWitnesses.IsEmpty
            || !program.GenericMethodSignatures.IsEmpty
            || !program.GenericMethodOperatorProtocols.IsEmpty
            || !HasSupportedC1M1ImportedNominalClosure(program,
                sourceStructDeclarations, standardClassIndexByIdentity)
            || (!schema.UsesClassParameterDefaults()
                && (!program.Interfaces.IsEmpty
                    || !program.GenericMethodDefinitions.IsEmpty)))
        {
            throw Unsupported(
                "C1M1 source declaration emission requires captured local non-generic classes and the bounded imported nominal closure without generic or interface definitions.");
        }
        if (!emitsConstruction2
            && sourceClassExports.Exports.Any(static export =>
                export.MemberExports.Any(static member =>
                    member.Kind != SourceClassMemberKind.Field)))
        {
            throw Unsupported(
                "C1M1 source class declarations do not publish constructor, method, or property ExportKeys; use the exact construction successor.");
        }
        if (emitsConstruction2
            && sourceClassExports.Exports.Any(static export =>
                export.MemberExports.Any(static member =>
                    member.Kind == SourceClassMemberKind.Property)))
        {
            throw Unsupported(
                "The bounded Construction-2 class source emitter does not yet retain property Bodies.");
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

        if ((exportKeysByClass.Count == 0 && !hasTypeOnlyContext)
            || exportKeysByClass.Count > ManagedClassTypes.OptionalFirst
                - ManagedClassTypes.First)
        {
            throw Unsupported(
                "C1M1 source declaration emission requires captured class declarations or an exact contextual type-only reference.");
        }

        var sourceClassProgram = program.SourceClassProgramAssociation;
        if (sourceClassProgram is null
            && (!program.Classes.IsEmpty
                || !sourceClassExports.Exports.IsEmpty))
        {
            throw new InvalidDataException(
                "Captured C1M1 class exports have no compiler-owned source class association.");
        }

        var sourceClassDeclarations = sourceClassProgram?.Declarations ?? [];
        var localClassIndexes = sourceClassDeclarations
            .Select(static declaration => declaration.ClassIndex)
            .ToHashSet();
        var pendingTypeDefinitions = new Queue<(bool IsClass, int Index)>(
            exportKeysByClass.Keys.Select(static classIndex =>
                (IsClass: true, classIndex)));
        foreach (var declaration in sourceClassDeclarations)
        {
            if (exportKeysByClass.TryAdd(declaration.ClassIndex, []))
            {
                if (exportKeysByClass.Count
                    > ManagedClassTypes.OptionalFirst
                        - ManagedClassTypes.First)
                {
                    throw Unsupported(
                        "C1M1 source class declarations exceed the local class limit.");
                }

                pendingTypeDefinitions.Enqueue((true,
                    declaration.ClassIndex));
            }
        }

        var retainedStructureIndexes = new HashSet<int>();
        if (hasTypeOnlyContext)
        {
            for (var structureIndex = 0;
                structureIndex < program.Structures.Length;
                structureIndex++)
            {
                if (retainedStructureIndexes.Add(structureIndex))
                {
                    pendingTypeDefinitions.Enqueue((false,
                        structureIndex));
                }
            }
        }
        if (exportKeysByClass.Count
            > ManagedClassTypes.OptionalFirst - ManagedClassTypes.First)
        {
            throw Unsupported(
                "C1M1 source class declarations exceed the local class limit.");
        }

        foreach (var declaration in program.StandardClassDeclarations)
        {
            foreach (var field in declaration.Declaration.Fields)
            {
                if (TryGetC1M1ImportedClassFieldType(field.Type, program,
                        standardClassIndexByIdentity, out _, out _,
                        allowOptionalWrapper: true))
                {
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
                                "C1M1 imported class struct dependency closure exceeds the local structure limit.");
                        }

                        pendingTypeDefinitions.Enqueue((false,
                            dependencyStructureIndex));
                    }

                    continue;
                }

                _ = GetC1M1SourceScalarTypeSpecReference(field.Type);
            }
        }

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
                if (!IsC1M1SourceStructLayoutSupported(structure,
                        sourceStructDeclarations[pendingDefinition.Index]))
                {
                    throw Unsupported(
                        "C1M1 class declaration closure supports only non-generic local or imported structs with auto layout and no ownership or cleanup contract.");
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

                if (TryGetC1M1ImportedClassFieldType(field.Type, program,
                        standardClassIndexByIdentity, out _, out _,
                        allowOptionalWrapper: true))
                {
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

        var standardClassTypeDefViews
            = new List<C1M1ImportedClassTypeDefViewPlan>();
        var standardClassTypeDefViewIndexesByClass
            = new Dictionary<int, List<int>>();
        for (var standardClassIndex = 0;
            standardClassIndex < program.StandardClassDeclarations.Length;
            standardClassIndex++)
        {
            var declaration
                = program.StandardClassDeclarations[standardClassIndex];
            var importPlan = GetC1M1ImportedClassImportPlan(program,
                declaration);
            var viewIndexes = new List<int>();
            standardClassTypeDefViewIndexesByClass.Add(standardClassIndex,
                viewIndexes);

            if (!supportsClassTypeBindingViews)
            {
                AddView(declaration.Declaration, importPlan.Provider,
                    importPlan.ExportKeys, declaration.Expectations, false);
            }
            else
            {
                foreach (var expectation in declaration.Expectations)
                {
                    var binding = expectation.Binding;
                    AddView(expectation.ExpectedDeclaration,
                        binding.Provider, [binding.ExportKey], [expectation],
                        isSelectedView: false);
                }

                var hasDirectEqualityEndpoint
                    = declaration.TypeDefBindingEqualityGroups.Any(group =>
                        group.Endpoints.Any(endpoint =>
                            endpoint.ImportRow == 0
                            && endpoint.TypeDefDefinitionId
                                == declaration.Declaration.Identity
                                    .DefinitionId
                            && SameA2SourceTypeProviderSnapshot(
                                endpoint.OwnerModule,
                                declaration.Declaration.Provider)));
                foreach (var exportKey in declaration.SelectedExportKeys)
                {
                    var isAlreadyAnExpectation
                        = declaration.Expectations.Any(expectation =>
                            StringComparer.Ordinal.Equals(
                                expectation.Binding.ExportKey, exportKey));
                    if (isAlreadyAnExpectation
                        && !hasDirectEqualityEndpoint)
                    {
                        continue;
                    }

                    AddView(declaration.Declaration,
                        declaration.Declaration.Provider, [exportKey], [],
                        isSelectedView: true);
                }
            }

            if (viewIndexes.Count == 0)
            {
                throw Unsupported(
                    "C1M1 class source emission requires an exact TypeDef view for every retained class declaration.");
            }

            void AddView(BoundStandardClassTypeView view,
                BoundSourceTypeProviderSnapshot provider,
                ImmutableArray<string> exportKeys,
                ImmutableArray<BoundStandardClassExpectation> expectations,
                bool isSelectedView)
            {
                if (!SameA2SourceTypeProviderSnapshot(provider,
                        declaration.Declaration.Provider)
                    || exportKeys.IsDefaultOrEmpty
                    || exportKeys.Distinct(StringComparer.Ordinal).Count()
                        != exportKeys.Length
                    || exportKeys.Any(exportKey =>
                        !BoundEnumTypes.IsValidModuleDeclarationKey(exportKey)
                        || !declaration.ExportKeys.Contains(exportKey,
                            StringComparer.Ordinal)))
                {
                    throw new InvalidDataException(
                        "A retained class TypeDef view lost its exact provider ExportKey.");
                }

                var viewIndex = standardClassTypeDefViews.Count;
                standardClassTypeDefViews.Add(
                    new C1M1ImportedClassTypeDefViewPlan(
                        standardClassIndex, view, provider, exportKeys,
                        expectations, isSelectedView));
                viewIndexes.Add(viewIndex);
            }
        }
        int[][] sourceClassEqualityViewGroups
            = supportsClassTypeBindingViews
                ? standardClassTypeDefViewIndexesByClass.Values
                .Select(viewIndexes => viewIndexes.Where(viewIndex =>
                        standardClassTypeDefViews[viewIndex].IsSelectedView)
                    .ToArray())
                .Where(static viewIndexes => viewIndexes.Length > 1)
                .ToArray()
                : [];

        var typeDefRows = new List<TrRmTypeDefRow>(
            exportKeysByClass.Count
                + standardClassTypeDefViews.Count);
        var typeDefNames = new List<(string Namespace, string Name)>(
            exportKeysByClass.Count
                + standardClassTypeDefViews.Count);
        var fieldRows = new List<TrRmFieldDefRow>();
        var fieldNames = new List<string>();
        var layoutRows = new List<TrRmLayoutRow>(
            exportKeysByClass.Count
                + standardClassTypeDefViews.Count
                + structureTypeDefViews.Count);
        var layoutFieldRows = new List<TrRmLayoutFieldRow>();
        var typeSpecRows = new List<ReadOnlyMemory<byte>>();
        var typeSpecRowByClassType = new Dictionary<
            (int ClassIndex, bool IsWritable, bool IsOptional), uint>();
        var typeSpecRowByStructure = new Dictionary<int, uint>();
        var typeSpecRowByTypeView = new Dictionary<int, uint>();
        var exportRows = new List<(uint DefinitionId, string Key)>();
        var methodRows = new List<TrRmMethodDefRow>();
        var methodNames = new List<string>();
        var methodSignatureRows = new List<TrRmMethodSigRow>();
        var parameterRows = new List<TrRmParamRow>();
        var genericParamRows = new List<TrRmGenericParamRow>();
        var genericParamNames = new List<string>();
        var genericConstraintRows = new List<TrRmConstraintRow>();
        var parameterSurfaceRows
            = new List<C1M1ParameterSurfaceRow>();
        var contractRows = new List<TrRmContractRow>();
        var bodyRows = new List<ReadOnlyMemory<byte>>();
        var featureRows = new List<TrRmFeatureRow>();
        frozenScalarValueBuilder = new TrRmFrozenScalarValueBuilder();
        var constructionInitializers
            = new List<C1M1Construction2SourceInitializer>();
        var construction2SourceMethods
            = new List<C1M1Construction2SourceMethod>();
        var methodDefRowByFunctionIndex = new Dictionary<int, int>();
        var sourceNames = new Dictionary<
            (string Namespace, string Name), (int Kind, int Index)>();
        var typeDefRowByClass = exportKeysByClass.Keys
            .Select(static (classIndex, ordinal) =>
                (classIndex, typeDefRow: ordinal + 1))
            .ToDictionary(static item => item.classIndex,
                static item => item.typeDefRow);
        var fieldDefRowByClassAndIndex = new Dictionary<
            (int ClassIndex, int FieldIndex), int>();
        var fieldDefRowByStandardClassViewAndIndex = new Dictionary<
            (int StandardClassViewIndex, int FieldIndex), int>();
        var fieldImportRowByStandardClassViewAndIndex = new Dictionary<
            (int StandardClassViewIndex, int FieldIndex), int>();
        var methodDefRowByStandardClassViewAndId = new Dictionary<
            (int StandardClassViewIndex, uint MethodDefinitionId), int>();
        var fieldDefRowByStructureAndIndex = new Dictionary<
            (int StructureIndex, int FieldIndex), int>();
        var fieldDefRowByTypeViewAndIndex = new Dictionary<
            (int TypeViewIndex, int FieldIndex), int>();
        var typeDefRowByStandardClassView
            = new Dictionary<int, int>(standardClassTypeDefViews.Count);
        for (var classViewIndex = 0;
            classViewIndex < standardClassTypeDefViews.Count;
            classViewIndex++)
        {
            var typeDefRow = exportKeysByClass.Count + classViewIndex + 1;
            typeDefRowByStandardClassView.Add(classViewIndex, typeDefRow);
        }
        var contextualTypeViews = typeViews;
        var structureTypeDefViews = new List<(int StructureIndex,
            int TypeViewIndex)>();
        foreach (var structureIndex in retainedStructureIndexes.Order())
        {
            var origin = program.Structures[structureIndex]
                .StandardTypeOrigin;
            if (origin is null
                || origin.ContextualViewIndexes.IsDefaultOrEmpty)
            {
                structureTypeDefViews.Add((structureIndex, -1));
                continue;
            }

            foreach (var typeViewIndex in origin.ContextualViewIndexes)
            {
                if ((uint)typeViewIndex >= (uint)contextualTypeViews.Length
                    || contextualTypeViews[typeViewIndex].StructureIndex
                        != structureIndex)
                {
                    throw new InvalidDataException(
                        "A retained struct has an invalid contextual TypeDef view map.");
                }

                structureTypeDefViews.Add((structureIndex, typeViewIndex));
            }
        }

        var typeDefRowByStructure
            = new Dictionary<int, int>(retainedStructureIndexes.Count);
        var typeDefRowByTypeView = new Dictionary<int, int>(
            contextualTypeViews.Length);
        for (var index = 0; index < structureTypeDefViews.Count; index++)
        {
            var (structureIndex, typeViewIndex)
                = structureTypeDefViews[index];
            var typeDefRow = exportKeysByClass.Count
                + standardClassTypeDefViews.Count + index + 1;
            _ = typeDefRowByStructure.TryAdd(structureIndex, typeDefRow);
            if (typeViewIndex >= 0)
            {
                if (!typeDefRowByTypeView.TryAdd(typeViewIndex, typeDefRow))
                {
                    throw new InvalidDataException(
                        "A contextual TypeDef view was emitted more than once.");
                }
            }
        }
        var typeImportRowByStandardClassView
            = new Dictionary<int, int>(standardClassTypeDefViews.Count);
        var typeImportPlanByStandardClassView
            = new Dictionary<int, C1M1ImportedClassImportPlan>(
                standardClassTypeDefViews.Count);
        var typeImportRowByStructure = new Dictionary<int, int>();
        var typeImportPlanByStructure
            = new Dictionary<int, C1M1ImportedStructImportPlan>();
        var typeImportRowByTypeView = new Dictionary<int, int>();
        var typeImportPlanByTypeView
            = new Dictionary<int, C1M1ImportedStructImportPlan>();
        var typeSpecRowByStandardClassType = new Dictionary<int, uint>();
        var moduleReferences = new List<(
            string ProducerIdentity, uint TargetRequirement,
            uint RuntimePolicy)>();
        var moduleReferenceRows = new Dictionary<(
            string ProducerIdentity, uint TargetRequirement,
            uint RuntimePolicy), int>();
        var imports = new List<C1M1SourceImport>();
        var importRowsByIdentity = new Dictionary<(
            int ModuleRefRow, string ExportKey, uint ExpectedDefinitionId),
            int>();

        int GetModuleRefRow(string producerIdentity,
            uint targetRequirement, uint runtimePolicy)
        {
            var key = (producerIdentity, targetRequirement, runtimePolicy);
            if (!moduleReferenceRows.TryGetValue(key, out var moduleRefRow))
            {
                moduleRefRow = moduleReferences.Count + 1;
                moduleReferences.Add(key);
                moduleReferenceRows.Add(key, moduleRefRow);
            }

            return moduleRefRow;
        }

        int AddImport(string producerIdentity, uint targetRequirement,
            uint runtimePolicy, string exportKey, uint expectedDefinitionId)
        {
            var moduleRefRow = GetModuleRefRow(producerIdentity,
                targetRequirement, runtimePolicy);
            var identity = (moduleRefRow, exportKey, expectedDefinitionId);
            if (importRowsByIdentity.TryGetValue(identity,
                    out var existingRow))
            {
                return existingRow;
            }

            var importRow = imports.Count + 1;
            imports.Add(new C1M1SourceImport(moduleRefRow, exportKey,
                expectedDefinitionId));
            importRowsByIdentity.Add(identity, importRow);
            return importRow;
        }

        C1M1ImportedStructImportPlan GetContextualStructImportPlan(
            int structureIndex, int typeViewIndex)
        {
            var origin = program.Structures[structureIndex]
                .StandardTypeOrigin
                ?? throw new InvalidDataException(
                    "A contextual struct view has no canonical provider origin.");
            var view = contextualTypeViews[typeViewIndex];
            if (view.StructureIndex != structureIndex
                || !SameA2SourceTypeProviderSnapshot(view.Provider,
                    origin.Provider)
                || view.ProviderDefinitionId != origin.DefinitionId
                || string.IsNullOrEmpty(view.ExportKey)
                || !BoundEnumTypes.IsValidModuleDeclarationKey(
                    view.ExportKey)
                || !origin.ExportKeys.Contains(view.ExportKey,
                    StringComparer.Ordinal))
            {
                throw new InvalidDataException(
                    "A contextual struct view has no exact provider ExportKey.");
            }

            return new C1M1ImportedStructImportPlan(view.Provider, [
                view.ExportKey,
            ]);
        }

        for (var classViewIndex = 0;
            classViewIndex < standardClassTypeDefViews.Count;
            classViewIndex++)
        {
            var view = standardClassTypeDefViews[classViewIndex];
            var importPlan = new C1M1ImportedClassImportPlan(
                view.Provider, view.ExportKeys);
            var typeDefRow
                = typeDefRowByStandardClassView[classViewIndex];
            var importRow = 0;
            foreach (var exportKey in importPlan.ExportKeys)
            {
                var addedImportRow = AddImport(
                    importPlan.Provider.ModuleIdentity,
                    importPlan.Provider.TargetRequirement,
                    importPlan.Provider.RuntimePolicy, exportKey,
                    0x1300_0000u | checked((uint)typeDefRow));
                if (importRow == 0)
                {
                    importRow = addedImportRow;
                }
            }

            typeImportPlanByStandardClassView.Add(classViewIndex,
                importPlan);
            typeImportRowByStandardClassView.Add(classViewIndex, importRow);
        }

        foreach (var (structureIndex, typeViewIndex)
                 in structureTypeDefViews)
        {
            var structure = program.Structures[structureIndex];
            if (structure.StandardTypeOrigin is not
                BoundStandardTypeOrigin origin)
            {
                continue;
            }

            var typeDefRow = typeViewIndex < 0
                ? typeDefRowByStructure[structureIndex]
                : typeDefRowByTypeView[typeViewIndex];
            var importPlan = typeViewIndex < 0
                ? GetC1M1ImportedStructImportPlan(origin)
                : GetContextualStructImportPlan(structureIndex,
                    typeViewIndex);
            if (typeViewIndex < 0)
            {
                typeImportPlanByStructure.Add(structureIndex, importPlan);
                foreach (var exportKey in importPlan.ExportKeys)
                {
                    AddImport(importPlan.Provider.ModuleIdentity,
                        importPlan.Provider.TargetRequirement,
                        importPlan.Provider.RuntimePolicy, exportKey,
                        0x1300_0000u | checked((uint)typeDefRow));
                }

                typeImportRowByStructure.Add(structureIndex,
                    imports.Count - importPlan.ExportKeys.Length + 1);
                continue;
            }

            var typeView = contextualTypeViews[typeViewIndex];
            var importRow = AddImport(importPlan.Provider.ModuleIdentity,
                importPlan.Provider.TargetRequirement,
                importPlan.Provider.RuntimePolicy,
                importPlan.ExportKeys[0],
                0x1300_0000u | checked((uint)typeDefRow));
            typeImportPlanByTypeView.Add(typeViewIndex, importPlan);
            typeImportRowByTypeView.Add(typeViewIndex, importRow);
            if (typeDefRowByStructure[structureIndex] == typeDefRow)
            {
                typeImportPlanByStructure.Add(structureIndex, importPlan);
                typeImportRowByStructure.Add(structureIndex, importRow);
            }
        }

        var typeDefOrder = exportKeysByClass.Keys
            .Select(static classIndex =>
                (Kind: 0, Index: classIndex, TypeViewIndex: -1))
            .Concat(Enumerable.Range(0,
                    standardClassTypeDefViews.Count)
                .Select(static classViewIndex => (Kind: 1,
                    Index: classViewIndex, TypeViewIndex: -1)))
            .Concat(structureTypeDefViews.Select(
                static view => (Kind: 2,
                    Index: view.StructureIndex,
                    TypeViewIndex: view.TypeViewIndex)));

        int GetStandardClassTypeDefViewIndex(int standardClassIndex,
            int instanceIndex)
        {
            if ((uint)instanceIndex
                    >= (uint)program.GenericNominalTypeInstances.Length
                || !standardClassTypeDefViewIndexesByClass.TryGetValue(
                    standardClassIndex, out var viewIndexes))
            {
                throw new InvalidDataException(
                    "A captured source type refers to an unretained Standard class TypeDef view.");
            }

            var instance = program.GenericNominalTypeInstances[instanceIndex];
            var matches = viewIndexes.Where(viewIndex =>
            {
                var view = standardClassTypeDefViews[viewIndex];
                if (instance.TypeSpecImportRow > 0)
                {
                    return view.Expectations.Any(expectation =>
                        expectation.Binding.ImportRow
                            == instance.TypeSpecImportRow
                        && StringComparer.Ordinal.Equals(
                            expectation.Binding.UsingModule.ModuleIdentity,
                            instance.TypeSpecProviderIdentity));
                }

                if (view.IsSelectedView)
                {
                    return instance.TypeSpecProviderIdentity is null
                        || StringComparer.Ordinal.Equals(
                            view.Provider.ModuleIdentity,
                            instance.TypeSpecProviderIdentity);
                }

                return false;
            }).ToArray();
            if (matches.Length == 0 && viewIndexes.Count == 1
                && instance.TypeSpecImportRow == 0)
            {
                return viewIndexes[0];
            }
            if (matches.Length == 1)
            {
                return matches[0];
            }
            if (matches.Length > 1
                && matches.All(viewIndex =>
                    standardClassTypeDefViews[viewIndex].IsSelectedView))
            {
                return matches.OrderBy(viewIndex =>
                        standardClassTypeDefViews[viewIndex].ExportKeys[0],
                        StringComparer.Ordinal)
                    .First();
            }

            throw new InvalidDataException(
                "A class TypeSpec does not identify one exact retained TypeDef view.");
        }

        uint ImportedClassTypeSpecReference(
            int standardClassIndex, int instanceIndex)
        {
            var classViewIndex = GetStandardClassTypeDefViewIndex(
                standardClassIndex, instanceIndex);
            if (!typeImportRowByStandardClassView.TryGetValue(
                    classViewIndex, out var importRow))
            {
                throw new InvalidDataException(
                    "A captured source type refers to an unretained Standard class TypeDef.");
            }

            if (!typeSpecRowByStandardClassType.TryGetValue(instanceIndex,
                    out var typeSpecRow))
            {
                var instance = program.GenericNominalTypeInstances[
                    instanceIndex];
                var importedTypeSpecRow
                    = checked((uint)typeSpecRows.Count + 1);
                typeSpecRows.Add(TrRmTypeSpecTableWriter.Reference(0x03,
                    checked((uint)importRow), instance.Qualifiers));
                typeSpecRow = importedTypeSpecRow;
                if (instance.TypeSpecForm == 0x06)
                {
                    typeSpecRow = checked((uint)typeSpecRows.Count + 1);
                    typeSpecRows.Add(TrRmTypeSpecTableWriter.Wrapped(0x06,
                        importedTypeSpecRow));
                }

                typeSpecRowByStandardClassType.Add(instanceIndex,
                    typeSpecRow);
            }

            return typeSpecRow;
        }

        uint FieldTypeSpecReference(PrimitiveType type)
        {
            if (TryGetC1M1ImportedClassFieldType(type, program,
                    standardClassIndexByIdentity,
                    out var standardClassIndex, out var instanceIndex,
                    allowOptionalWrapper: true))
            {
                return ImportedClassTypeSpecReference(
                    standardClassIndex, instanceIndex);
            }

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
                    var qualifiers = isWritable ? (byte)0x01 : (byte)0;
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
                    var typeSpecForm = typeImportRowByStructure.TryGetValue(
                        structureIndex, out var typeImportRow) ? (byte)0x03
                        : (byte)0x02;
                    typeSpecRows.Add(TrRmTypeSpecTableWriter.Reference(
                        typeSpecForm,
                        checked((uint)(typeSpecForm == 0x03
                            ? typeImportRow : typeDefRow))));
                    typeSpecRowByStructure.Add(structureIndex, typeSpecRow);
                }

                return typeSpecRow;
            }

            return GetC1M1SourceScalarTypeSpecReference(type);
        }

        uint ContextualFieldTypeSpecReference(PrimitiveType type,
            int typeViewIndex)
        {
            if (!TryGetC1M1SourceStructFieldType(type,
                    sourceStructDeclarations, out var structureIndex))
            {
                return FieldTypeSpecReference(type);
            }

            if (typeViewIndex < 0
                || (uint)typeViewIndex
                    >= (uint)contextualTypeViews.Length
                || !typeDefRowByTypeView.ContainsKey(typeViewIndex)
                || !typeImportRowByTypeView.TryGetValue(typeViewIndex,
                    out var importRow)
                || contextualTypeViews[typeViewIndex].StructureIndex
                    != structureIndex)
            {
                throw new InvalidDataException(
                    "A contextual FieldDef type has no matching retained TypeDef view.");
            }

            if (!typeSpecRowByTypeView.TryGetValue(typeViewIndex,
                    out var typeSpecRow))
            {
                typeSpecRow = checked((uint)typeSpecRows.Count + 1);
                typeSpecRows.Add(TrRmTypeSpecTableWriter.Reference(0x03,
                    checked((uint)importRow)));
                typeSpecRowByTypeView.Add(typeViewIndex, typeSpecRow);
            }

            return typeSpecRow;
        }

        int GetMethodSignatureTypeViewIndex(BoundFunction function,
            int parameterOrdinal, PrimitiveType type)
        {
            if (function.ModuleImport is not BoundModuleImport methodImport
                || program.StandardQuerySourceAssociation
                    is not BoundStandardQuerySourceAssociation association
                || association.MethodTypeViews.IsDefaultOrEmpty)
            {
                return -1;
            }

            var matches = association.MethodTypeViews.Where(view =>
                    StringComparer.Ordinal.Equals(
                        view.MethodExportKey, methodImport.ExportKey)
                    && view.SignaturePosition == parameterOrdinal)
                .ToArray();
            if (matches.Length == 0)
            {
                return -1;
            }
            if (matches.Length != 1
                || (uint)matches[0].TypeViewIndex
                    >= (uint)contextualTypeViews.Length
                || contextualTypeViews[matches[0].TypeViewIndex]
                    .StructureIndex != (int)type - 100)
            {
                throw new InvalidDataException(
                    "A MethodDef signature has an invalid contextual TypeDef view.");
            }

            return matches[0].TypeViewIndex;
        }

        uint MethodTypeSpecReference(PrimitiveType type)
        {
            if (TryGetC1M1ImportedClassFieldType(type, program,
                    standardClassIndexByIdentity,
                    out var standardClassIndex, out var instanceIndex,
                    allowOptionalWrapper: true))
            {
                return ImportedClassTypeSpecReference(
                    standardClassIndex, instanceIndex);
            }

            return FieldTypeSpecReference(type);
        }

        uint Construction2ValueTypeSpecReference(PrimitiveType type)
            => type == PrimitiveType.Void
                ? 0xF200_0000u : MethodTypeSpecReference(type);

        var noOpMethodReferenceTypeSpecRows
            = new Dictionary<PrimitiveType, uint>();
        uint NoOpMethodTypeSpecReference(PrimitiveType type)
        {
            if (!CheckedReferenceTypes.IsType(type))
            {
                return type == PrimitiveType.Void
                    ? 0xF200_0000u : MethodTypeSpecReference(type);
            }

            if (!IsC1M1NoOpReadonlyScalarReferenceType(type,
                    program.CheckedReferenceTypes))
            {
                throw Unsupported(
                    "A no-op Construction-2 class Method supports only ordinary readonly references to scalar values.");
            }

            if (noOpMethodReferenceTypeSpecRows.TryGetValue(type,
                    out var existingReference))
            {
                return existingReference;
            }

            var reference = program.CheckedReferenceTypes[
                CheckedReferenceTypes.Index(type)];
            var referentTypeSpecReference
                = Construction2ValueTypeSpecReference(
                    reference.ReferentType);
            if (typeSpecRows.Count >= 0xFF_FFFF)
            {
                throw Unsupported(
                    "Construction-2 class Method TypeSpecs exceed the metadata row limit.");
            }

            typeSpecRows.Add(TrRmTypeSpecTableWriter.Wrapped(0x07,
                referentTypeSpecReference));
            existingReference = checked((uint)typeSpecRows.Count);
            noOpMethodReferenceTypeSpecRows.Add(type, existingReference);
            return existingReference;
        }

        int AppendC1M1Construction2Parameters(
            int signatureRow, BoundFunction function, int firstVariable,
            bool allowNoOpReadonlyScalarReferences = false)
        {
            var parameterCount = function.ParameterCount - firstVariable;
            if (parameterCount < 0
                || parameterCount > SemanticVerifier.MaximumArguments)
            {
                throw Unsupported(
                    "A Construction-2 constructor exceeds the checked logical parameter limit.");
            }

            var firstParam = parameterCount == 0
                ? 0 : parameterRows.Count + 1;
            for (var ordinal = 0; ordinal < parameterCount; ordinal++)
            {
                var variable = function.Variables[firstVariable + ordinal];
                if (variable.IsMutable || variable.IsOut || variable.IsInit
                    || (variable.IsImplicitRef
                        && !allowNoOpReadonlyScalarReferences)
                    || variable.IsInitializationLocal)
                {
                    throw Unsupported(
                        "The bounded Construction-2 constructor projection supports only immutable by-value parameters.");
                }

                var typeSpecReference
                    = allowNoOpReadonlyScalarReferences
                        ? NoOpMethodTypeSpecReference(variable.Type)
                        : ParameterTypeSpecReference(variable.Type);
                parameterRows.Add(new TrRmParamRow(signatureRow, ordinal,
                    typeSpecReference, 0,
                    allowNoOpReadonlyScalarReferences
                        && variable.IsImplicitRef ? (byte)1 : (byte)0));
            }

            return firstParam;
        }

        uint ParameterTypeSpecReference(PrimitiveType type)
            => Construction2ValueTypeSpecReference(type);

        void AppendC1M1Construction2ParameterSurface(
            int methodDefRow, BoundFunction function, int firstVariable)
        {
            if (!schema.UsesClassParameterDefaults())
            {
                return;
            }

            var parameterCount = function.ParameterCount - firstVariable;
            var defaultsByOrdinal
                = new Dictionary<int, BoundOrdinaryParameterDefault>();
            var previousOrdinal = -1;
            if (!function.HasOrdinaryParameterDefaultSurface
                || function.SourceDeclarationInputOrdinal < 0
                || function.OrdinaryParameterDefaults.IsDefault)
            {
                throw Unsupported(
                    "CM1 source publication requires the checked primary parameter surface for every retained class method.");
            }

            foreach (var parameterDefault
                in function.OrdinaryParameterDefaults)
            {
                if (parameterDefault is null
                    || parameterDefault.ParameterOrdinal <= previousOrdinal
                    || parameterDefault.ParameterOrdinal >= parameterCount
                    || parameterDefault.SourceInputOrdinal
                        != function.SourceDeclarationInputOrdinal
                    || parameterDefault.IsPending
                    || parameterDefault.Value is not BoundConstant
                        || parameterDefault.Expression is not null
                    || !defaultsByOrdinal.TryAdd(
                        parameterDefault.ParameterOrdinal,
                        parameterDefault))
                {
                    throw Unsupported(
                        "CM1 source defaults require one checked, closed scalar value per authored parameter default.");
                }

                previousOrdinal = parameterDefault.ParameterOrdinal;
            }

            for (var ordinal = 0; ordinal < parameterCount; ordinal++)
            {
                var variable = function.Variables[
                    firstVariable + ordinal];
                var defaultConstantSourceRow = 0u;
                if (defaultsByOrdinal.TryGetValue(ordinal,
                        out var parameterDefault))
                {
                    var value = (BoundConstant)parameterDefault.Value!;
                    if (value.Type != variable.Type
                        || value.RepresentationType != variable.Type
                        || (value.Type is PrimitiveType.NativeInt
                            or PrimitiveType.NativeUInt)
                        || !value.Bits.IsCanonicalFor(variable.Type))
                    {
                        throw Unsupported(
                            "CM1 parameter defaults require exact fixed-width SF1 scalar values.");
                    }

                    var expectedTypeSpecRef
                        = ParameterTypeSpecReference(variable.Type);
                    var frozenValue
                        = frozenScalarValueBuilder.Add(value);
                    if (frozenValue.TypeSpecRef != expectedTypeSpecRef)
                    {
                        throw Unsupported(
                            "CM1 parameter default values must retain the exact parameter TypeSpec.");
                    }

                    defaultConstantSourceRow
                        = frozenValue.ConstantSourceRow;
                }

                if (parameterSurfaceRows.Count >= 0xFF_FFFF)
                {
                    throw Unsupported(
                        "The CM1 MethodParameterSurface table exceeds its row limit.");
                }

                parameterSurfaceRows.Add(new C1M1ParameterSurfaceRow(
                    methodDefRow, ordinal, variable.Name,
                    defaultConstantSourceRow));
            }
        }

        foreach (var (typeKind, sourceIndex, typeViewIndex) in typeDefOrder)
        {
            var isClass = typeKind == 0;
            var isStandardClass = typeKind == 1;
            if (typeKind is < 0 or > 2)
            {
                throw new InvalidDataException(
                    "A retained class TypeDef has an unknown emission kind.");
            }

            if (isClass
                && (schema.UsesConstructionInitializerCopyEvidence()
                    ? program.Classes[sourceIndex].IsUnique is not bool
                    : program.Classes[sourceIndex].IsUnique is not false))
            {
                throw Unsupported(
                    "C1M1 class TypeDefs require ownership evidence selected by the exact schema.");
            }

            var standardClassViewPlan = isStandardClass
                ? standardClassTypeDefViews[sourceIndex]
                : null;
            var standardClassView = standardClassViewPlan?.Declaration;
            var isContextualTypeView = typeKind == 2 && typeViewIndex >= 0;
            var sourceStructure = typeKind == 2
                ? isContextualTypeView
                    ? contextualTypeViews[typeViewIndex].ViewDeclaration
                    : program.Structures[sourceIndex]
                : null;
            var declarationName = isClass
                ? program.Classes[sourceIndex].Name
                : isStandardClass
                ? standardClassView!.Name
                : sourceStructure!.Name;
            var exportKeys = isClass
                ? exportKeysByClass[sourceIndex] : [];
            var typeDefFlags = isClass
                ? (exportKeys.Count != 0 ? 0x02u
                    : program.Classes[sourceIndex].IsInternal ? 0x01u : 0x02u)
                    | 0x08u
                    | (schema.UsesConstructionInitializerCopyEvidence()
                        && program.Classes[sourceIndex].IsUnique == true
                            ? TrRmTypeDefTableReader.DeclaredUniqueFlag : 0u)
                    | (program.Classes[sourceIndex].IsMutable ? 0x80u : 0u)
                : isStandardClass
                ? standardClassView!.TypeDefFlags
                : (sourceStructure!.IsPrivate ? 0u
                    : sourceStructure.IsInternal ? 1u : 2u)
                    | 0x04u
                    | (schema.UsesConstructionInitializerCopyEvidence()
                        && (sourceStructure.StandardTypeOrigin is null
                            ? !sourceStructure.IsUnique
                            : sourceStructure.DeclaredCopyEvidence == true)
                            ? TrRmTypeDefTableReader.DeclaredCopyFlag : 0u);
            var typeFieldCount = isClass
                ? program.Classes[sourceIndex].Fields.Length
                : isStandardClass
                ? standardClassView!.Fields.Length
                : sourceStructure!.Fields.Length;
            var typeFieldsAreDefault = isClass
                ? program.Classes[sourceIndex].Fields.IsDefault
                : isStandardClass
                ? standardClassView!.Fields.IsDefault
                : sourceStructure!.Fields.IsDefault;
            var typeDefRow = isClass
                ? typeDefRowByClass[sourceIndex]
                : isStandardClass
                ? typeDefRowByStandardClassView[sourceIndex]
                : isContextualTypeView
                    ? typeDefRowByTypeView[typeViewIndex]
                    : typeDefRowByStructure[sourceIndex];
            if (typeDefRow != typeDefRows.Count + 1
                || typeFieldsAreDefault
                || (isContextualTypeView
                    && contextualTypeViews[typeViewIndex]
                        .FieldTypeViewIndexes.Length != typeFieldCount)
                || (isClass && typeFieldCount == 0)
                || (isClass && exportKeys.Count != 0
                    && program.Classes[sourceIndex].IsInternal))
            {
                throw Unsupported(
                    "C1M1 source class declarations require complete instance fields and public roots.");
            }
            var separator = declarationName.LastIndexOf('.');
            var namespaceName = isStandardClass
                ? standardClassView!.Namespace
                : !isClass
                ? isContextualTypeView
                    ? separator < 0 ? string.Empty
                        : declarationName[..separator]
                    : sourceStructDeclarations[sourceIndex].Namespace
                : separator < 0 ? string.Empty
                    : declarationName[..separator];
            var typeName = isStandardClass
                ? standardClassView!.Name
                : !isClass
                ? isContextualTypeView
                    ? separator < 0 ? declarationName
                        : declarationName[(separator + 1)..]
                    : sourceStructDeclarations[sourceIndex].Name
                : separator < 0 ? declarationName
                    : declarationName[(separator + 1)..];
            var sourceName = (namespaceName, typeName);
            var sourceNameIdentity = isStandardClass
                ? (typeKind, standardClassViewPlan!.StandardClassIndex)
                : (typeKind, sourceIndex);
            if (typeName.Length == 0
                || sourceNames.TryGetValue(sourceName,
                    out var existingSourceName)
                    && existingSourceName != sourceNameIdentity)
            {
                throw new InvalidDataException(
                    "Captured source class names are empty or duplicated.");
            }
            sourceNames[sourceName] = sourceNameIdentity;

            var fieldFirst = fieldRows.Count + 1;
            var layoutFieldFirst = layoutFieldRows.Count + 1;
            for (var fieldOrdinal = 0; fieldOrdinal < typeFieldCount;
                fieldOrdinal++)
            {
                string fieldName;
                PrimitiveType fieldType;
                uint fieldFlags;
                if (isStandardClass)
                {
                    var field = standardClassView!.Fields[fieldOrdinal];
                    var layout = standardClassView.Layouts[0];
                    if (field.OwnerDefinitionId
                            != standardClassView.Identity.DefinitionId
                        || field.DefinitionId >> 24 != 0x14
                        || layout.FieldDefinitionIds[fieldOrdinal]
                            != field.DefinitionId)
                    {
                        throw new InvalidDataException(
                            "An imported class FieldDef or layout changed its checked identity.");
                    }

                    fieldName = field.Name;
                    fieldType = field.Type;
                    fieldFlags = field.Flags;
                }
                else
                {
                    var field = isClass
                        ? program.Classes[sourceIndex].Fields[fieldOrdinal]
                        : sourceStructure!.Fields[fieldOrdinal];
                    if (field is null || (field.IsPrivate && field.IsInternal)
                        || field.RequiresUnsafeCaller)
                    {
                        throw Unsupported(
                            "C1M1 source class FieldDefs cannot discard conflicting access or unsafe-caller facts.");
                    }

                    var visibility = field.IsPrivate ? 0u
                        : field.IsInternal ? 1u : 2u;
                    fieldFlags = visibility
                        | (field.IsMutable ? 0x04u : 0u);
                    fieldName = field.Name;
                    fieldType = field.Type;
                }

                var fieldDefRow = fieldRows.Count + 1;
                fieldRows.Add(new TrRmFieldDefRow(typeDefRow, 0,
                    isContextualTypeView
                        ? ContextualFieldTypeSpecReference(fieldType,
                            contextualTypeViews[typeViewIndex]
                                .FieldTypeViewIndexes[fieldOrdinal])
                        : FieldTypeSpecReference(fieldType),
                    fieldFlags));
                fieldNames.Add(fieldName);
                if (isClass)
                {
                    fieldDefRowByClassAndIndex.Add(
                        (sourceIndex, fieldOrdinal), fieldDefRow);
                }
                else if (isStandardClass)
                {
                    fieldDefRowByStandardClassViewAndIndex.Add(
                        (sourceIndex, fieldOrdinal), fieldDefRow);
                }
                else
                {
                    if (isContextualTypeView)
                    {
                        fieldDefRowByTypeViewAndIndex.Add(
                            (typeViewIndex, fieldOrdinal), fieldDefRow);
                        if (typeDefRowByStructure[sourceIndex]
                            == typeDefRow)
                        {
                            fieldDefRowByStructureAndIndex.Add(
                                (sourceIndex, fieldOrdinal), fieldDefRow);
                        }
                    }
                    else
                    {
                        fieldDefRowByStructureAndIndex.Add(
                            (sourceIndex, fieldOrdinal), fieldDefRow);
                    }
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
                typeFieldCount == 0 ? 0 : fieldFirst, typeFieldCount, 0, 0,
                layoutRows.Count + 1, 1, 0, 0));
            typeDefNames.Add((namespaceName, typeName));
            layoutRows.Add(new TrRmLayoutRow(typeDefRow, 0, 0, 0, 0, 0,
                typeFieldCount == 0 ? 0 : layoutFieldFirst,
                typeFieldCount));
        }

        foreach (var (structureIndex, typeViewIndex)
                 in structureTypeDefViews)
        {
            var structure = program.Structures[structureIndex];
            if (structure.StandardTypeOrigin is not
                BoundStandardTypeOrigin)
            {
                continue;
            }
            var viewStructure = typeViewIndex < 0
                ? structure
                : contextualTypeViews[typeViewIndex].ViewDeclaration;
            var importPlan = typeViewIndex < 0
                ? typeImportPlanByStructure[structureIndex]
                : typeImportPlanByTypeView[typeViewIndex];

            for (var fieldIndex = 0;
                fieldIndex < viewStructure.Fields.Length; fieldIndex++)
            {
                var field = viewStructure.Fields[fieldIndex];
                if (field.StandardFieldOrigin is not
                    BoundStandardFieldOrigin fieldOrigin)
                {
                    continue;
                }

                var fieldEdge = typeViewIndex >= 0
                    ? contextualTypeViews[typeViewIndex]
                        .FieldEdges[fieldIndex]
                    : null;
                if (fieldEdge is not null
                    || !fieldOrigin.SelectedExportKeys.IsDefaultOrEmpty
                    || typeViewIndex < 0
                        && !fieldOrigin.ExportKeys.IsDefaultOrEmpty)
                {
                    if (!IsC1M1ImportedStructFieldImportable(structure,
                            field, importPlan))
                    {
                        throw Unsupported(
                            "C1M1 class source emission requires selected public FieldDef exports from the exact imported struct provider.");
                    }

                    var fieldDefRow = 0;
                    var hasFieldRow = typeViewIndex < 0
                        ? fieldDefRowByStructureAndIndex.TryGetValue((
                            structureIndex, fieldIndex),
                            out fieldDefRow)
                        : fieldDefRowByTypeViewAndIndex.TryGetValue((
                            typeViewIndex, fieldIndex),
                            out fieldDefRow);
                    if (!hasFieldRow)
                    {
                        throw new InvalidDataException(
                            "A selected imported struct FieldDef has no local expected stub.");
                    }

                    if (fieldEdge is not null)
                    {
                        if (!StringComparer.Ordinal.Equals(
                                fieldOrigin.Provider.ModuleIdentity,
                                fieldEdge.ProducerIdentity)
                            || fieldOrigin.DefinitionId
                                != fieldEdge.ActualDefinitionId)
                        {
                            throw new InvalidDataException(
                                "A contextual FieldDef Import changed its provider edge.");
                        }

                        AddImport(fieldEdge.ProducerIdentity,
                            fieldEdge.TargetRequirement,
                            fieldEdge.RuntimePolicy, fieldEdge.ExportKey,
                            0x1400_0000u
                                | checked((uint)fieldDefRow));
                    }
                    else
                    {
                        var importKeys
                            = fieldOrigin.SelectedExportKeys.IsDefaultOrEmpty
                                ? fieldOrigin.ExportKeys
                                : fieldOrigin.SelectedExportKeys;
                        foreach (var exportKey in importKeys)
                        {
                            AddImport(fieldOrigin.Provider.ModuleIdentity,
                                fieldOrigin.Provider.TargetRequirement,
                                fieldOrigin.Provider.RuntimePolicy, exportKey,
                                0x1400_0000u
                                    | checked((uint)fieldDefRow));
                        }
                    }
                }
            }
        }

        if (schema.UsesGenericMethodCalls()
            && classMethodPlans.Any(static plan =>
                plan.Method.GenericParameterCount != 0)
            && !featureRows.Contains(
                TrRmFeatureRow.GenericMethodCallsCandidate))
        {
            featureRows.Add(TrRmFeatureRow.GenericMethodCallsCandidate);
        }

        (Dictionary<int, int> TypeDefRows,
            Dictionary<int, int> ImportRows,
            HashSet<int> AmbiguousTypeDefRows)
            CreateMethodTypeMaps(
                BoundSourceTypeProviderSnapshot sourceSnapshot)
        {
            var typeDefRows = new Dictionary<int, int>();
            var importRows = new Dictionary<int, int>();
            var ambiguousTypeDefRows = new HashSet<int>();
            void AddTypeDefMapping(uint definitionId,
                int destinationTypeDefRow)
            {
                if (definitionId >> 24 != 0x13
                    || (definitionId & 0x00FF_FFFF) == 0)
                {
                    throw new InvalidDataException(
                        "A class MethodSig has an invalid TypeDef identity.");
                }

                var sourceTypeDefRow = checked((int)
                    (definitionId & 0x00FF_FFFF));
                if (ambiguousTypeDefRows.Contains(sourceTypeDefRow))
                {
                    return;
                }

                if (!typeDefRows.TryGetValue(sourceTypeDefRow,
                        out var existingTypeDefRow))
                {
                    typeDefRows.Add(sourceTypeDefRow,
                        destinationTypeDefRow);
                    return;
                }

                if (existingTypeDefRow != destinationTypeDefRow)
                {
                    _ = typeDefRows.Remove(sourceTypeDefRow);
                    _ = ambiguousTypeDefRows.Add(sourceTypeDefRow);
                }
            }

            for (var mappedViewIndex = 0;
                mappedViewIndex < standardClassTypeDefViews.Count;
                mappedViewIndex++)
            {
                var mappedView = standardClassTypeDefViews[
                    mappedViewIndex];
                var destinationTypeDefRow
                    = typeDefRowByStandardClassView[mappedViewIndex];
                if (SameA2SourceTypeProviderSnapshot(
                        mappedView.Declaration.Provider, sourceSnapshot))
                {
                    AddTypeDefMapping(
                        mappedView.Declaration.Identity.DefinitionId,
                        destinationTypeDefRow);
                }

                foreach (var expectation in mappedView.Expectations)
                {
                    if (!SameA2SourceTypeProviderSnapshot(
                            expectation.Binding.UsingModule,
                            sourceSnapshot))
                    {
                        continue;
                    }

                    AddTypeDefMapping(
                        expectation.Binding.ExpectedDefinitionId,
                        destinationTypeDefRow);
                    var sourceImportRow
                        = expectation.Binding.ImportRow;
                    var destinationImportRow
                        = typeImportRowByStandardClassView[
                            mappedViewIndex];
                    if (!importRows.TryAdd(sourceImportRow,
                            destinationImportRow)
                        && importRows[sourceImportRow]
                            != destinationImportRow)
                    {
                        throw new InvalidDataException(
                            "A class MethodSig Import has multiple destination view mappings.");
                    }
                }
            }

            for (var structureViewIndex = 0;
                structureViewIndex < structureTypeDefViews.Count;
                structureViewIndex++)
            {
                var (structureIndex, typeViewIndex)
                    = structureTypeDefViews[structureViewIndex];
                if (typeViewIndex < 0)
                {
                    if (program.Structures[structureIndex]
                            .StandardTypeOrigin
                        is not BoundStandardTypeOrigin origin)
                    {
                        continue;
                    }

                    var destinationTypeDefRow
                        = typeDefRowByStructure[structureIndex];
                    if (SameA2SourceTypeProviderSnapshot(origin.Provider,
                            sourceSnapshot))
                    {
                        AddTypeDefMapping(origin.DefinitionId,
                            destinationTypeDefRow);
                    }

                    foreach (var expectation in origin.Expectations)
                    {
                        if (!SameA2SourceTypeProviderSnapshot(
                                expectation.UsingModule, sourceSnapshot))
                        {
                            continue;
                        }

                        AddTypeDefMapping(
                            expectation.ExpectedDefinitionId,
                            destinationTypeDefRow);
                        if (typeImportRowByStructure.TryGetValue(
                                structureIndex,
                                out var destinationImportRow))
                        {
                            if (!importRows.TryAdd(expectation.ImportRow,
                                    destinationImportRow)
                                && importRows[expectation.ImportRow]
                                    != destinationImportRow)
                            {
                                throw new InvalidDataException(
                                    "A class MethodSig Import has multiple destination TypeDef views.");
                            }
                        }
                    }

                    continue;
                }

                var typeView = contextualTypeViews[typeViewIndex];
                var contextualTypeDefRow
                    = typeDefRowByTypeView[typeViewIndex];
                if (SameA2SourceTypeProviderSnapshot(typeView.Provider,
                        sourceSnapshot))
                {
                    AddTypeDefMapping(typeView.ProviderDefinitionId,
                        contextualTypeDefRow);
                }

                if (typeView.Expectation is BoundSourceTypeExpectation
                        typeExpectation
                    && SameA2SourceTypeProviderSnapshot(
                        typeExpectation.UsingModule, sourceSnapshot))
                {
                    AddTypeDefMapping(
                        typeExpectation.ExpectedDefinitionId,
                        contextualTypeDefRow);
                    if (typeImportRowByTypeView.TryGetValue(typeViewIndex,
                            out var destinationImportRow))
                    {
                        if (!importRows.TryAdd(typeExpectation.ImportRow,
                                destinationImportRow)
                            && importRows[typeExpectation.ImportRow]
                                != destinationImportRow)
                        {
                            throw new InvalidDataException(
                                "A class MethodSig Import has multiple destination TypeDef views.");
                        }
                    }
                }
            }

            return (typeDefRows, importRows, ambiguousTypeDefRows);
        }

        for (var classViewIndex = 0;
            classViewIndex < standardClassTypeDefViews.Count;
            classViewIndex++)
        {
            var viewPlan = standardClassTypeDefViews[classViewIndex];
            var classView = viewPlan.Declaration;
            var importPlan
                = typeImportPlanByStandardClassView[classViewIndex];
            var providerClassView = program.StandardClassDeclarations[
                viewPlan.StandardClassIndex].Declaration;
            var expectationFieldImports = supportsClassTypeBindingViews
                ? viewPlan.Expectations.SelectMany(static expectation =>
                    expectation.FieldExpectations).ToArray()
                : [];

            for (var fieldIndex = 0;
                fieldIndex < classView.Fields.Length; fieldIndex++)
            {
                var field = classView.Fields[fieldIndex];
                var matchingExpectations = expectationFieldImports
                    .Where(fieldExpectation =>
                        fieldExpectation.ExpectedDefinitionId
                            == field.DefinitionId
                        && fieldExpectation.ExpectedOwnerDefinitionId
                            == classView.Identity.DefinitionId)
                    .ToArray();
                if (matchingExpectations.Length == 0
                    && field.SelectedExportKeys.IsDefaultOrEmpty)
                {
                    continue;
                }

                var importableField = supportsClassTypeBindingViews
                    && !viewPlan.Expectations.IsEmpty
                    ? providerClassView.Fields[fieldIndex]
                    : field;
                var importableOwner = supportsClassTypeBindingViews
                    && !viewPlan.Expectations.IsEmpty
                    ? providerClassView : classView;
                if (!IsC1M1ImportedClassFieldImportable(importableOwner,
                        importableField, importPlan)
                    || !fieldDefRowByStandardClassViewAndIndex.TryGetValue((
                        classViewIndex, fieldIndex), out var fieldDefRow))
                {
                    throw Unsupported(
                        "C1M1 class source emission requires public instance FieldDef exports from the exact imported class provider.");
                }

                var fieldImportRow = 0;
                var fieldImportKeys = matchingExpectations.Length != 0
                    ? matchingExpectations.Select(
                        static fieldExpectation =>
                            fieldExpectation.ExportKey)
                        .Distinct(StringComparer.Ordinal)
                        .ToArray()
                    : field.SelectedExportKeys.ToArray();
                foreach (var fieldExpectation in matchingExpectations)
                {
                    var ownerExpectation = viewPlan.Expectations.SingleOrDefault(
                        expectation =>
                            expectation.Binding.ImportRow
                                == fieldExpectation.OwnerImportRow);
                    if (fieldExpectation.ImportRow <= 0
                        || ownerExpectation is null
                        || !SameA2SourceTypeProviderSnapshot(
                            fieldExpectation.Provider,
                            importPlan.Provider)
                        || !fieldImportKeys.Contains(
                            fieldExpectation.ExportKey,
                            StringComparer.Ordinal))
                    {
                        throw new InvalidDataException(
                            "A retained class FieldDef Import lost its exact owner view or ExportKey.");
                    }

                    var importRow = AddImport(
                        fieldExpectation.Provider.ModuleIdentity,
                        fieldExpectation.Provider.TargetRequirement,
                        fieldExpectation.Provider.RuntimePolicy,
                        fieldExpectation.ExportKey,
                        0x1400_0000u | checked((uint)fieldDefRow));
                    if (fieldImportRow == 0)
                    {
                        fieldImportRow = importRow;
                    }
                }

                foreach (var exportKey in field.SelectedExportKeys)
                {
                    if (matchingExpectations.Any(fieldExpectation =>
                            StringComparer.Ordinal.Equals(
                                fieldExpectation.ExportKey, exportKey)))
                    {
                        continue;
                    }

                    var importRow = AddImport(
                        importPlan.Provider.ModuleIdentity,
                        importPlan.Provider.TargetRequirement,
                        importPlan.Provider.RuntimePolicy, exportKey,
                        0x1400_0000u | checked((uint)fieldDefRow));
                    if (fieldImportRow == 0)
                    {
                        fieldImportRow = importRow;
                    }
                }

                fieldImportRowByStandardClassViewAndIndex.Add(
                    (classViewIndex, fieldIndex), fieldImportRow);
            }
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

        if (emitsConstruction2)
        {
            var sourceClassDeclarationsByIndex
                = sourceClassDeclarations.ToDictionary(
                    static declaration => declaration.ClassIndex);
            var constructorFunctionsBySourceDeclaration
                = program.Functions
                    .Select(static (function, functionIndex) =>
                        (function, functionIndex))
                    .Where(static candidate =>
                        candidate.function.IsClassConstructor)
                    .GroupBy(static candidate => (
                        candidate.function.SourceDeclarationInputOrdinal,
                        candidate.function.SourceDeclarationSpan))
                    .ToDictionary(static group => group.Key,
                        static group => group.ToArray());
            var classMethodsBySourceDeclaration = program.Functions
                .Select(static (function, functionIndex) =>
                    (function, functionIndex))
                .Where(static candidate =>
                    candidate.function.DeclaringStruct is not null
                    && !candidate.function.IsClassConstructor
                    && candidate.function.PropertyName is null
                    && candidate.function.SourceDeclarationInputOrdinal
                        >= 0)
                .GroupBy(static candidate => (
                    candidate.function.SourceDeclarationInputOrdinal,
                    candidate.function.SourceDeclarationSpan))
                .ToDictionary(static group => group.Key,
                    static group => group.ToArray());
            var memberExportsByClassAndFunction
                = sourceClassExports.Exports
                    .SelectMany(static export =>
                        export.MemberExports.Select(member => (
                            export.Declaration.ClassIndex, Member: member)))
                    .GroupBy(static candidate => (
                        candidate.ClassIndex,
                        candidate.Member.FunctionIndex))
                    .ToDictionary(static group => group.Key,
                        static group => group
                            .Select(static candidate => candidate.Member)
                            .ToArray());
            foreach (var classIndex in exportKeysByClass.Keys)
            {
                if (!sourceClassDeclarationsByIndex.TryGetValue(classIndex,
                        out var declaration))
                {
                    throw new InvalidDataException(
                        "A retained class TypeDef has no exact source declaration.");
                }

                var constructorDeclarations = new List<(
                    BoundSourceClassPart Part, SyntaxNode Declaration)>();
                var methodDeclarations = new List<(
                    BoundSourceClassPart Part, SyntaxNode Declaration)>();
                foreach (var part in declaration.Parts)
                {
                    foreach (var member in part.Declaration.Children
                        .OfType<SyntaxNode>())
                    {
                        if (member.Kind == SyntaxKind.FieldDeclaration)
                        {
                            if (member.Children
                                .OfType<SyntaxTokenElement>()
                                .Any(static token => token.Token.Kind
                                    == TokenKind.StaticKeyword))
                            {
                                throw Unsupported(
                                    "The bounded Construction-2 class source emitter does not preserve static field initialization.");
                            }

                            continue;
                        }

                        if (member.Kind == SyntaxKind.FunctionDeclaration)
                        {
                            if (!schema.UsesClassParameterDefaults()
                                && HasC1M1Construction2ParameterDefaults(
                                    member))
                            {
                                throw Unsupported(
                                    "The bounded Construction-2 source emitter does not yet retain ordinary member parameter defaults.");
                            }

                            methodDeclarations.Add((part, member));
                            continue;
                        }

                        if (member.Kind != SyntaxKind.ConstructorDeclaration)
                        {
                            throw Unsupported(
                                "The bounded Construction-2 class source emitter does not yet retain properties or other class member declarations.");
                        }

                        if (!schema.UsesClassParameterDefaults()
                            && HasC1M1Construction2ParameterDefaults(member))
                        {
                            throw Unsupported(
                                "The bounded Construction-2 source emitter does not yet retain ordinary member parameter defaults.");
                        }

                        if (IsC1M1Construction2PartialSecondary(member))
                        {
                            continue;
                        }

                        constructorDeclarations.Add((part, member));
                    }
                }

                if (constructorDeclarations.Count == 0)
                {
                    throw Unsupported(
                        "The bounded Construction-2 class source emitter requires at least one complete source constructor per retained class.");
                }

                foreach (var (constructorPart, constructorDeclaration)
                    in constructorDeclarations)
                {
                    if (!constructorFunctionsBySourceDeclaration.TryGetValue((
                            constructorPart.SourceInputOrdinal,
                            constructorDeclaration.Span),
                            out var constructorFunctions)
                        || constructorFunctions.Length != 1)
                    {
                        throw new InvalidDataException(
                            "A retained source class constructor has no unique checked initializer.");
                    }

                    var (function, functionIndex) = constructorFunctions[0];
                    if (function.IsPartialConstructor
                            != IsC1M1Construction2PartialPrimary(
                                constructorDeclaration)
                        || (function.IsPartialConstructor
                            && (function.SourceDeclarationInputOrdinal
                                    != constructorPart.SourceInputOrdinal
                                || function.SourceDeclarationSpan
                                    != constructorDeclaration.Span
                                || !HasC1M1Construction2PartialSecondary(
                                    declaration.Parts, function)))
                    {
                        throw new InvalidDataException(
                            "A partial source class constructor does not map its primary to one checked implementing secondary.");
                    }

                    var classDeclaration
                        = program.Classes[classIndex];
                    var receiverType = function.Variables.IsEmpty
                        ? PrimitiveType.Void : function.Variables[0].Type;
                    if (!function.IsClassConstructor
                        || function.IsConstructor || function.IsStatic
                        || function.IsExternal
                        || function.Intrinsic != BoundIntrinsic.None
                        || function.ClassConstructor != -1
                        || function.DeclaringStruct != classDeclaration.Name
                        || function.ReturnType != PrimitiveType.Void
                        || function.ParameterCount < 1
                        || function.ParameterCount != function.Variables.Length
                        || function.RequiresUnsafeCaller
                        || function.DeclaredRequiresUnsafeCaller
                        || function.DoesNotReturn || function.NoFailureProof
                        || !function.CapturedParameters.IsEmpty
                        || !function.DeclarationContractClauses.IsEmpty
                        || function.ModuleImport is not null
                        || function.GenericMethodInstantiation is not null
                        || ManagedClassTypes.IsOptional(receiverType)
                        || !ManagedClassTypes.IsClass(receiverType)
                        || ManagedClassTypes.ClassIndex(receiverType) != classIndex
                        || ManagedClassTypes.IsWritable(receiverType)
                            != classDeclaration.IsMutable
                        || (function.IsPrivate && function.IsInternal))
                    {
                        throw Unsupported(
                            "The bounded Construction-2 class source emitter cannot discard constructor ownership, signature, access, effect, or completion facts.");
                    }

                    var fieldAssignments
                        = GetC1M1Construction2FieldAssignments(program,
                            function, classDeclaration,
                            sourceStructDeclarations, schema,
                            schemaIdentity);
                    foreach (var field in classDeclaration.Fields)
                    {
                        _ = FieldTypeSpecReference(field.Type);
                    }

                    var memberExports
                        = memberExportsByClassAndFunction.TryGetValue((
                            classIndex, functionIndex), out var mappedExports)
                            ? mappedExports
                            : Array.Empty<SourceClassMemberExport>();
                    if (memberExports.Any(static member =>
                            member.Kind != SourceClassMemberKind.Constructor))
                    {
                        throw new InvalidDataException(
                            "A class constructor mapping contains a non-constructor member.");
                    }
                    var initializerExportKeys = memberExports
                        .Select(static member => member.ExportKey)
                        .ToImmutableArray();
                    var projectionExportKeys = memberExports
                        .Where(static member =>
                            member.ConstructionProjectionExportKey is not null)
                        .Select(static member =>
                            member.ConstructionProjectionExportKey!)
                        .ToImmutableArray();
                    var hasCompleteConstructionRecipe
                        = fieldAssignments.Length
                            == classDeclaration.Fields.Length;
                    if (!hasCompleteConstructionRecipe
                        && !projectionExportKeys.IsEmpty)
                    {
                        throw Unsupported(
                            "A Construction-2 projection ExportKey requires a constructor that initializes every class field.");
                    }

                    var classTypeSpecReference
                        = FieldTypeSpecReference(receiverType);
                    var initializerMethodDefRow = methodRows.Count + 1;
                    var projectionMethodDefRow = hasCompleteConstructionRecipe
                        ? initializerMethodDefRow + 1 : 0;
                    var initializerSignatureRow
                        = methodSignatureRows.Count + 1;
                    var initializerParamFirst
                        = AppendC1M1Construction2Parameters(
                            initializerSignatureRow, function, 1);
                    methodSignatureRows.Add(new TrRmMethodSigRow(
                        0xF200_0000u, classTypeSpecReference,
                        initializerParamFirst,
                        function.ParameterCount - 1, 0, 0));
                    var projectionSignatureRow = 0;
                    if (hasCompleteConstructionRecipe)
                    {
                        projectionSignatureRow
                            = methodSignatureRows.Count + 1;
                        var projectionParamFirst
                            = AppendC1M1Construction2Parameters(
                                projectionSignatureRow, function, 1);
                        methodSignatureRows.Add(new TrRmMethodSigRow(
                            classTypeSpecReference, 0,
                            projectionParamFirst,
                            function.ParameterCount - 1, 0, 0));
                    }

                    var initializerContractFirst
                        = contractRows.Count + 1;
                    contractRows.Add(new TrRmContractRow(
                        0x1600_0000u
                            | checked((uint)initializerMethodDefRow),
                        0x0003,
                        EncodeC1M1Construction2InitializerContract(
                            fieldAssignments, fieldDefRowByClassAndIndex,
                            classIndex)));
                    var projectionContractFirst = 0;
                    if (hasCompleteConstructionRecipe)
                    {
                        projectionContractFirst = contractRows.Count + 1;
                        contractRows.Add(new TrRmContractRow(
                            0x1600_0000u
                                | checked((uint)projectionMethodDefRow),
                            0x0003,
                            EncodeC1M1Construction2ProjectionContract(
                                initializerMethodDefRow)));
                    }

                    var visibility = function.IsPrivate ? 0u
                        : function.IsInternal ? 1u : 2u;
                    methodRows.Add(new TrRmMethodDefRow(
                        typeDefRowByClass[classIndex], 0,
                        initializerSignatureRow, visibility,
                        initializerContractFirst, 1, 0));
                    methodNames.Add(function.Name);
                    if (hasCompleteConstructionRecipe)
                    {
                        methodRows.Add(new TrRmMethodDefRow(
                            typeDefRowByClass[classIndex], 0,
                            projectionSignatureRow,
                            projectionExportKeys.IsEmpty ? 0u : 2u,
                            projectionContractFirst, 1, 0));
                        methodNames.Add(function.Name);
                    }
                    AppendC1M1Construction2ParameterSurface(
                        initializerMethodDefRow, function, 1);
                    if (hasCompleteConstructionRecipe)
                    {
                        AppendC1M1Construction2ParameterSurface(
                            projectionMethodDefRow, function, 1);
                    }

                    foreach (var exportKey in initializerExportKeys)
                    {
                        exportRows.Add((
                            0x1600_0000u
                                | checked((uint)initializerMethodDefRow),
                            exportKey));
                    }

                    foreach (var exportKey in projectionExportKeys)
                    {
                        exportRows.Add((
                            0x1600_0000u
                                | checked((uint)projectionMethodDefRow),
                            exportKey));
                    }

                    constructionInitializers.Add(
                        new C1M1Construction2SourceInitializer(
                            classIndex, function, classTypeSpecReference,
                            fieldAssignments, initializerMethodDefRow,
                            projectionMethodDefRow));
                }

                foreach (var (methodPart, methodDeclaration)
                    in methodDeclarations)
                {
                    if (!classMethodsBySourceDeclaration.TryGetValue((
                            methodPart.SourceInputOrdinal,
                            methodDeclaration.Span),
                            out var methodFunctions)
                        || methodFunctions.Length != 1)
                    {
                        throw new InvalidDataException(
                            "A retained source class method has no unique checked MethodDef.");
                    }

                    var (methodFunction, methodFunctionIndex)
                        = methodFunctions[0];
                    var isSupportedNoOpInstanceMethod
                        = !methodFunction.IsStatic
                            && methodFunction.ParameterCount
                                == methodFunction.Variables.Length
                            && methodFunction.ParameterCount >= 1
                            && methodFunction.ReturnType
                                == PrimitiveType.Void;
                    var directReturnHome = -1;
                    var isSupportedDirectReturnInstanceMethod
                        = !methodFunction.IsStatic
                            && methodFunction.ReturnType
                                != PrimitiveType.Void
                            && methodFunction.ParameterCount
                                == methodFunction.Variables.Length
                            && TryGetC1M1Construction2DirectParameterReturn(
                                methodFunction, out directReturnHome);
                    var directFieldIndex = -1;
                    var isSupportedDirectFieldReadInstanceMethod
                        = !methodFunction.IsStatic
                            && methodFunction.ReturnType
                                != PrimitiveType.Void
                            && TryGetC1M1Construction2DirectFieldRead(
                                methodFunction, out directFieldIndex);
                    var fieldReference
                        = TryGetC1M1Construction2FieldReference(
                            program, methodFunction, classIndex,
                            standardClassIndexByIdentity,
                            typeImportPlanByStandardClassView,
                            fieldDefRowByClassAndIndex,
                            fieldDefRowByStandardClassViewAndIndex,
                            fieldImportRowByStandardClassViewAndIndex,
                            standardClassTypeDefViews,
                            GetStandardClassTypeDefViewIndex);
                    var isSupportedFieldReferenceBody
                        = fieldReference.HasValue;
                    var nestedFieldRead
                        = !isSupportedDirectFieldReadInstanceMethod
                            && !isSupportedFieldReferenceBody
                            ? TryGetC1M1Construction2NestedFieldRead(
                                program, methodFunction, classIndex,
                                sourceStructDeclarations,
                                typeImportPlanByStructure,
                                standardClassIndexByIdentity,
                                typeImportPlanByStandardClassView,
                                fieldDefRowByClassAndIndex,
                                fieldDefRowByStandardClassViewAndIndex,
                                fieldDefRowByStructureAndIndex,
                                standardClassTypeDefViews,
                                GetStandardClassTypeDefViewIndex)
                            : null;
                    var directFieldWriteIndex = -1;
                    var isSupportedDirectFieldWriteInstanceMethod
                        = !methodFunction.IsStatic
                            && methodFunction.ReturnType
                                == PrimitiveType.Void
                            && TryGetC1M1Construction2DirectFieldWrite(
                                methodFunction,
                                out directFieldWriteIndex);
                    C1M1Construction2NestedFieldWrite? nestedFieldWrite
                        = !isSupportedDirectFieldWriteInstanceMethod
                            ? TryGetC1M1Construction2ImportedClassFieldWrite(
                                program, methodFunction,
                                standardClassIndexByIdentity,
                                typeImportPlanByStandardClassView,
                                fieldDefRowByStandardClassViewAndIndex,
                                fieldImportRowByStandardClassViewAndIndex,
                                standardClassTypeDefViews,
                                GetStandardClassTypeDefViewIndex)
                            : null;
                    nestedFieldWrite ??=
                        !isSupportedDirectFieldWriteInstanceMethod
                            ? TryGetC1M1Construction2NestedFieldWrite(
                                program, methodFunction, classIndex,
                                sourceStructDeclarations,
                                typeImportPlanByStructure,
                                fieldDefRowByClassAndIndex,
                                fieldDefRowByStructureAndIndex)
                            : null;
                    var conditionalReturn
                        = TryGetC1M1Construction2ConditionalReturn(
                            methodFunction);
                    var identityForwarderTargetFunctionIndex = -1;
                    var isSupportedIdentityForwarder
                        = TryGetC1M1Construction2IdentityForwarder(
                            methodFunction, program.Functions,
                            out identityForwarderTargetFunctionIndex);
                    var isSupportedUnitIdentityForwarder
                        = !isSupportedIdentityForwarder
                            && TryGetC1M1Construction2UnitIdentityForwarder(
                                methodFunction, program.Functions,
                                out identityForwarderTargetFunctionIndex);
                    var directNumericBinary
                        = TryGetC1M1Construction2DirectNumericBinary(
                            methodFunction);
                    var directNumericUnary
                        = TryGetC1M1Construction2DirectNumericUnary(
                            methodFunction);
                    var directBooleanComparison
                        = TryGetC1M1Construction2DirectBooleanComparison(
                            methodFunction);
                    var callNumericBinary
                        = TryGetC1M1Construction2DirectCallNumericBinary(
                            methodFunction, program.Functions, program);
                    var localCopyReturn
                        = TryGetC1M1Construction2LocalCopyReturn(
                            methodFunction);
                    var localNumericUnaryReturn
                        = TryGetC1M1Construction2LocalNumericUnaryReturn(
                            methodFunction);
                    var localNumericBinaryReturn
                        = TryGetC1M1Construction2LocalNumericBinaryReturn(
                            methodFunction);
                    if ((!methodFunction.IsStatic
                            && !isSupportedNoOpInstanceMethod
                            && !isSupportedDirectReturnInstanceMethod
                            && !isSupportedDirectFieldReadInstanceMethod
                            && !isSupportedFieldReferenceBody
                            && nestedFieldRead is null
                            && !isSupportedDirectFieldWriteInstanceMethod
                            && nestedFieldWrite is null
                            && !isSupportedIdentityForwarder
                            && !isSupportedUnitIdentityForwarder
                            && directNumericBinary is null
                            && directNumericUnary is null
                            && directBooleanComparison is null
                            && conditionalReturn is null
                            && callNumericBinary is null
                            && !localCopyReturn
                            && localNumericUnaryReturn is null
                            && !localNumericBinaryReturn)
                        || methodFunction.IsClassConstructor
                        || methodFunction.IsConstructor
                        || methodFunction.IsExternal
                        || methodFunction.IsCleanup
                        || methodFunction.IsOperator
                        || methodFunction.Intrinsic != BoundIntrinsic.None
                        || methodFunction.ClassConstructor != -1
                        || methodFunction.DeclaringStruct
                            != classDeclaration.Name
                        || ((methodFunction.RequiresUnsafeCaller
                                || methodFunction.DeclaredRequiresUnsafeCaller)
                            && !isSupportedFieldReferenceBody)
                        || methodFunction.DoesNotReturn
                        || methodFunction.NoFailureProof
                        || methodFunction.HasPureEvaluationForm
                        || methodFunction.DirectNativeImport is not null
                        || methodFunction.NativeVTableMethod is not null
                        || methodFunction.NativeFunctionPointerType
                            is not null
                        || methodFunction.NativeCallableConvention
                            is not null
                        || methodFunction.NativeCallableEntryName
                            is not null
                        || methodFunction.HasNativeExport
                        || methodFunction.NativeExportEntryName
                            is not null
                        || methodFunction.StaticConstructorIdentity
                            is not null
                        || methodFunction.StaticFieldInitializerIndex
                            >= 0
                        || methodFunction.ModuleImport is not null
                        || methodFunction.GenericMethodInstantiation
                            is not null
                        || methodFunction.PropertyName is not null
                        || methodFunction.IndexedBody is not null
                        || !methodFunction.CapturedParameters.IsEmpty
                        || !methodFunction.ResultDependencyParameters
                            .IsEmpty
                        || methodFunction.ResultDependencyIncludesReceiver
                        || methodFunction.ReturnsForwardedBacking
                        || methodFunction.NativeOutputRole is not null
                        || !methodFunction.NativeOutputContracts.IsEmpty
                        || (!methodFunction.DependencyContracts.IsEmpty
                            && !isSupportedFieldReferenceBody)
                        || methodFunction.CopyOriginSummary is not null
                        || methodFunction.HasRawUnionReturnCoverage
                        || !methodFunction.RawUnionReturnCoverage.IsEmpty
                        || !methodFunction.FriendTargets.IsEmpty
                        || !methodFunction.DeclarationContractClauses
                            .IsEmpty
                        || (methodFunction.IsPrivate
                            && methodFunction.IsInternal))
                    {
                        throw Unsupported(
                            "The bounded Construction-2 class method producer supports scalar static methods, exact identity forwarders, readonly instance int32/Boolean operations and same-class call-binary methods, scalar local-copy/unary/binary returns, empty unit instance methods, readonly instance methods returning one scalar value parameter, direct scalar field reads and normal-result checked-reference returns to direct local or imported scalar fields, and direct or nested inline-struct scalar field writes.");
                    }

                    if (methodFunction.ReturnType != PrimitiveType.Void)
                    {
                        if (!methodFunction.IsStatic)
                        {
                            if (!isSupportedDirectReturnInstanceMethod
                                && !isSupportedDirectFieldReadInstanceMethod
                                && !isSupportedFieldReferenceBody
                                && nestedFieldRead is null
                                && !isSupportedDirectFieldWriteInstanceMethod
                                && !isSupportedIdentityForwarder
                                && !isSupportedUnitIdentityForwarder
                                && directNumericBinary is null
                                && directNumericUnary is null
                                && directBooleanComparison is null
                                && conditionalReturn is null
                                && callNumericBinary is null
                                && !localCopyReturn
                                && localNumericUnaryReturn is null
                                && !localNumericBinaryReturn)
                            {
                                throw Unsupported(
                                    "The bounded Construction-2 instance method producer supports only unit, direct scalar-parameter, direct numeric/Boolean operations, same-class fixed-integer call-binary, local-copy/unary/binary returns, direct scalar-field reads, normal-result checked-reference field returns and direct scalar-field writes.");
                            }
                        }

                        if (isSupportedFieldReferenceBody)
                        {
                            _ = NoOpMethodTypeSpecReference(
                                methodFunction.ReturnType);
                        }
                        else
                        {
                            _ = GetC1M1SourceScalarTypeSpecReference(
                                methodFunction.ReturnType);
                        }
                    }

                    if (methodFunction.IsStatic)
                    {
                        for (var variableIndex = 0;
                            variableIndex < methodFunction.Variables.Length;
                            variableIndex++)
                        {
                            var variable
                                = methodFunction.Variables[variableIndex];
                            var isUnitParameter
                                = variable.Type == PrimitiveType.Void
                                    && variableIndex
                                        < methodFunction.ParameterCount;
                            if ((variable.Type == PrimitiveType.Void
                                    && !isUnitParameter)
                                || variable.IsOut || variable.IsInit
                                || variable.IsImplicitRef
                                || variable.IsInitializationLocal)
                            {
                                throw Unsupported(
                                    "The bounded Construction-2 static method producer supports only scalar local storage.");
                            }

                            if (!isUnitParameter)
                            {
                                if (!TryGetC1M1SourceScalarTypeSpecReference(
                                        variable.Type, out _)
                                    && !TryGetC1M1ImportedClassFieldType(
                                        variable.Type, program,
                                        standardClassIndexByIdentity,
                                        out _, out _,
                                        allowOptionalWrapper: true))
                                {
                                    throw Unsupported(
                                        "The bounded Construction-2 static method producer supports scalar or selected imported class-handle value parameters.");
                                }
                            }
                        }
                    }
                    else
                    {
                        var receiverType = methodFunction.Variables[0].Type;
                        if (ManagedClassTypes.IsOptional(receiverType)
                            || !ManagedClassTypes.IsClass(receiverType)
                            || ManagedClassTypes.ClassIndex(receiverType)
                                != classIndex)
                        {
                            throw Unsupported(
                                "The bounded Construction-2 instance method producer requires its exact nominal class receiver.");
                        }

                        for (var variableIndex = 1;
                            variableIndex < methodFunction.Variables.Length;
                            variableIndex++)
                        {
                            var variable
                                = methodFunction.Variables[variableIndex];
                            var isUnitParameter
                                = variable.Type == PrimitiveType.Void
                                    && variableIndex
                                        < methodFunction.ParameterCount;
                            if ((variable.Type == PrimitiveType.Void
                                    && !isUnitParameter)
                                || variable.IsOut || variable.IsInit
                                || (variable.IsImplicitRef
                                    && (!isSupportedNoOpInstanceMethod
                                        || !IsC1M1NoOpReadonlyScalarReferenceType(
                                            variable.Type,
                                            program.CheckedReferenceTypes)))
                                || variable.IsInitializationLocal)
                            {
                                throw Unsupported(
                                    "The bounded Construction-2 instance method producer supports only scalar value parameters.");
                            }

                            if (!isUnitParameter)
                            {
                                var isNoOpReferenceParameter
                                    = isSupportedNoOpInstanceMethod
                                        && IsC1M1NoOpReadonlyScalarReferenceType(
                                            variable.Type,
                                            program.CheckedReferenceTypes);
                                if (!IsC1M1Construction2SourceCopyType(
                                        variable.Type, program, schema,
                                        schemaIdentity,
                                        sourceStructDeclarations)
                                    && !TryGetC1M1ImportedClassFieldType(
                                        variable.Type, program,
                                        standardClassIndexByIdentity,
                                        out _, out _,
                                        allowOptionalWrapper: true)
                                    && !isNoOpReferenceParameter)
                                {
                                    throw Unsupported(
                                        "The bounded Construction-2 instance method producer supports scalar, Copy-struct, selected imported class-handle or no-op readonly scalar-reference parameters.");
                                }
                            }
                        }
                    }

                    var methodExports
                        = memberExportsByClassAndFunction.TryGetValue((
                            classIndex, methodFunctionIndex),
                            out var mappedMethodExports)
                            ? mappedMethodExports
                            : Array.Empty<SourceClassMemberExport>();
                    if (methodExports.Any(static member =>
                            member.Kind != SourceClassMemberKind.Method))
                    {
                        throw new InvalidDataException(
                            "A class method mapping contains a non-method member.");
                    }

                    var methodExportKeys = methodExports
                        .Select(static member => member.ExportKey)
                        .ToArray();
                    var methodRow = methodRows.Count + 1;
                    if (!methodDefRowByFunctionIndex.TryAdd(
                            methodFunctionIndex, methodRow))
                    {
                        throw new InvalidDataException(
                            "A checked class method was assigned more than one MethodDef row.");
                    }

                    byte[]? encodedBody = null;
                    if (isSupportedDirectFieldReadInstanceMethod)
                    {
                        if (!fieldDefRowByClassAndIndex.TryGetValue((
                                classIndex, directFieldIndex),
                                out var fieldDefRow))
                        {
                            throw new InvalidDataException(
                                "A class instance method reads a FieldDef outside its retained owner.");
                        }

                        encodedBody
                            = EncodeC1M1Construction2FieldReadBody(
                                fieldDefRow);
                    }
                    else if (nestedFieldRead is not null)
                    {
                        encodedBody
                            = EncodeC1M1Construction2NestedFieldReadBody(
                                methodFunction, nestedFieldRead,
                                Construction2ValueTypeSpecReference,
                                contractRows.Count, schema);
                    }
                    else if (isSupportedDirectFieldWriteInstanceMethod)
                    {
                        if (!fieldDefRowByClassAndIndex.TryGetValue((
                                classIndex, directFieldWriteIndex),
                                out var fieldDefRow))
                        {
                            throw new InvalidDataException(
                                "A class instance method writes a FieldDef outside its retained owner.");
                        }

                        var field = program.Classes[classIndex]
                            .Fields[directFieldWriteIndex];
                        if (!field.IsMutable
                            || field.Type
                                != methodFunction.Variables[1].Type)
                        {
                            throw new InvalidDataException(
                                "A compact class FieldWrite requires a mutable FieldDef with the exact formal type.");
                        }

                        _ = GetC1M1SourceScalarTypeSpecReference(field.Type);
                        encodedBody
                            = EncodeC1M1Construction2FieldWriteBody(
                                fieldDefRow, 0, sourceUse: 1);
                    }
                    else if (nestedFieldWrite is not null)
                    {
                        encodedBody
                            = EncodeC1M1Construction2NestedFieldWriteBody(
                                methodFunction, nestedFieldWrite,
                                frozenScalarValueBuilder,
                                Construction2ValueTypeSpecReference,
                                contractRows.Count, schema);
                    }
                    else if (directNumericBinary is not null)
                    {
                        encodedBody
                            = EncodeC1M1Construction2DirectNumericBinaryBody(
                                methodFunction, directNumericBinary,
                                frozenScalarValueBuilder,
                                Construction2ValueTypeSpecReference,
                                contractRows.Count, schema);
                    }
                    else if (directBooleanComparison is not null)
                    {
                        encodedBody
                            = EncodeC1M1Construction2DirectBooleanComparisonBody(
                                methodFunction, directBooleanComparison,
                                Construction2ValueTypeSpecReference,
                                contractRows.Count, schema);
                    }
                    else if (directNumericUnary is not null)
                    {
                        encodedBody
                            = EncodeC1M1Construction2DirectNumericUnaryBody(
                                methodFunction, directNumericUnary,
                                Construction2ValueTypeSpecReference,
                                contractRows.Count, schema);
                    }
                    else if (localNumericUnaryReturn is not null)
                    {
                        encodedBody
                            = EncodeC1M1Construction2LocalNumericUnaryReturnBody(
                                methodFunction, localNumericUnaryReturn,
                                Construction2ValueTypeSpecReference,
                                contractRows.Count, schema);
                    }
                    else if (conditionalReturn is not null)
                    {
                        encodedBody
                            = EncodeC1M1Construction2ConditionalReturnBody(
                                methodFunction, conditionalReturn.Value,
                                frozenScalarValueBuilder,
                                Construction2ValueTypeSpecReference,
                                contractRows.Count, schema);
                    }
                    else if (localCopyReturn)
                    {
                        encodedBody
                            = EncodeC1M1Construction2LocalCopyReturnBody(
                                methodFunction,
                                Construction2ValueTypeSpecReference,
                                contractRows.Count, schema);
                    }
                    else if (localNumericBinaryReturn)
                    {
                        encodedBody
                            = EncodeC1M1Construction2LocalNumericBinaryReturnBody(
                                methodFunction,
                                frozenScalarValueBuilder,
                                Construction2ValueTypeSpecReference,
                                contractRows.Count, schema);
                    }

                    var methodSignatureRow
                        = methodSignatureRows.Count + 1;
                    var methodParamFirst
                        = AppendC1M1Construction2Parameters(
                            methodSignatureRow, methodFunction,
                            methodFunction.IsStatic ? 0 : 1,
                            allowNoOpReadonlyScalarReferences:
                                isSupportedNoOpInstanceMethod);
                    var methodResultTypeSpecReference
                        = methodFunction.ReturnType == PrimitiveType.Void
                            ? 0xF200_0000u
                            : isSupportedFieldReferenceBody
                                ? NoOpMethodTypeSpecReference(
                                    methodFunction.ReturnType)
                                : MethodTypeSpecReference(
                                    methodFunction.ReturnType);
                    var methodReceiverTypeSpecReference
                        = methodFunction.IsStatic ? 0u
                            : MethodTypeSpecReference(
                                methodFunction.Variables[0].Type);
                    methodSignatureRows.Add(new TrRmMethodSigRow(
                        methodResultTypeSpecReference,
                        methodReceiverTypeSpecReference,
                        methodParamFirst,
                        methodFunction.ParameterCount
                            - (methodFunction.IsStatic ? 0 : 1),
                        0, 0));
                    var methodEffectsMask
                        = (methodFunction.DoesNotReturn ? 2u : 0u)
                            | (methodFunction.RequiresUnsafeCaller ? 4u : 0u);
                    var methodContractFirst = 0;
                    var methodContractCount = 0;
                    if (methodEffectsMask != 0)
                    {
                        methodContractFirst = contractRows.Count + 1;
                        contractRows.Add(new TrRmContractRow(
                            0x1600_0000u | checked((uint)methodRow), 1,
                            s_effectPayloads[methodEffectsMask]));
                        methodContractCount++;
                    }

                    if (isSupportedFieldReferenceBody)
                    {
                        if (fieldReference is not
                            C1M1Construction2FieldReference selectedFieldReference)
                        {
                            throw new InvalidDataException(
                                "A class field-reference method has no retained FieldDef or FieldDef Import.");
                        }

                        var source = methodFunction.DependencyContracts[0]
                            .Sources[0];
                        byte sourceKind;
                        uint sourceOrdinal;
                        switch (source.Kind)
                        {
                            case BoundDependencySourceKind.Formal
                                when source.FormalOrdinal is int ordinal
                                    && ordinal >= 0:
                                sourceKind = 1;
                                sourceOrdinal = checked((uint)ordinal);
                                break;
                            case BoundDependencySourceKind.Receiver
                                when source.FormalOrdinal is null:
                                sourceKind = 2;
                                sourceOrdinal = 0;
                                break;
                            default:
                                throw new InvalidDataException(
                                    "A class field-reference method has an unsupported Dependency source.");
                        }

                        var dependencyContractRow
                            = contractRows.Count + 1;
                        if (methodContractFirst == 0)
                        {
                            methodContractFirst
                                = dependencyContractRow;
                        }

                        var payload = new byte[12];
                        payload[0] = 1;
                        payload[1] = sourceKind;
                        payload[2] = 3;
                        payload[3] = 1;
                        BinaryPrimitives.WriteUInt32LittleEndian(
                            payload.AsSpan(8), sourceOrdinal);
                        contractRows.Add(new TrRmContractRow(
                            0x1600_0000u
                                | checked((uint)methodRow),
                            4, payload));
                        methodContractCount++;
                        encodedBody
                            = EncodeC1M1Construction2FieldReferenceBody(
                                selectedFieldReference.FieldToken,
                                dependencyContractRow);
                    }

                    var methodVisibility = methodFunction.IsPrivate ? 0u
                        : methodFunction.IsInternal ? 1u : 2u;
                    if (!methodFunction.IsStatic
                        && methodFunction.Variables[0].IsMutable)
                    {
                        if (schema.UsesMethodReceiverAccess())
                        {
                            methodVisibility |= 4u;
                        }
                        else if (schema
                            != TrRmArtifactSchema.C1M1Construction2Draft)
                        {
                            throw Unsupported(
                                "A mutable class method receiver requires Construction-2 TypeSpec access or the exact receiver-access successor.");
                        }
                    }

                    methodRows.Add(new TrRmMethodDefRow(
                        typeDefRowByClass[classIndex], 0,
                        methodSignatureRow, methodVisibility,
                        methodContractFirst,
                        methodContractCount, 0));
                    methodNames.Add(methodFunction.Name);
                    AppendC1M1Construction2ParameterSurface(methodRow,
                        methodFunction,
                        methodFunction.IsStatic ? 0 : 1);
                    foreach (var exportKey in methodExportKeys)
                    {
                        exportRows.Add((
                            0x1600_0000u | checked((uint)methodRow),
                            exportKey));
                    }

                    construction2SourceMethods.Add(
                        new C1M1Construction2SourceMethod(
                            methodFunction, methodRow,
                            isSupportedDirectReturnInstanceMethod
                                ? directReturnHome : -1,
                            isSupportedIdentityForwarder
                                || isSupportedUnitIdentityForwarder
                                ? identityForwarderTargetFunctionIndex : -1,
                            encodedBody, callNumericBinary));
                }
            }

            featureRows.Add(
                TrRmFeatureRow.ClassConstructionBodiesCandidate);
            if (schema.UsesClassParameterDefaults())
            {
                featureRows.Add(
                    TrRmFeatureRow.MethodParameterLabelsCandidate);
            }

            foreach (var initializer in constructionInitializers)
            {
                bodyRows.Add(EncodeC1M1Construction2InitializerBody(
                    program, initializer, fieldDefRowByClassAndIndex,
                    Construction2ValueTypeSpecReference,
                    frozenScalarValueBuilder, contractRows.Count, schema));
                methodRows[initializer.InitializerMethodDefRow - 1]
                    = methodRows[initializer.InitializerMethodDefRow - 1]
                        with { BodyRow = bodyRows.Count };
                if (initializer.ProjectionMethodDefRow != 0)
                {
                    bodyRows.Add(EncodeC1M1Construction2ProjectionBody(
                        initializer, Construction2ValueTypeSpecReference,
                        contractRows.Count, schema));
                    methodRows[initializer.ProjectionMethodDefRow - 1]
                        = methodRows[initializer.ProjectionMethodDefRow - 1]
                            with { BodyRow = bodyRows.Count };
                }
            }

            foreach (var method in construction2SourceMethods)
            {
                var body = method.EncodedBody;
                if (body is null
                    && method.IdentityForwarderTargetFunctionIndex >= 0)
                {
                    if (!methodDefRowByFunctionIndex.TryGetValue(
                            method.IdentityForwarderTargetFunctionIndex,
                            out var targetMethodDefRow))
                    {
                        throw new InvalidDataException(
                            "A class identity forwarder targets a MethodDef outside its retained class closure.");
                    }

                    body = EncodeC1M1Construction2IdentityForwarderBody(
                        targetMethodDefRow);
                }

                if (body is null && method.CallBinaryExpression is not null)
                {
                    if (method.CallBinaryExpression
                            is BoundBinary callBinaryExpression
                        && callBinaryExpression.Left is BoundCall targetCall)
                    {
                        if (!methodDefRowByFunctionIndex.TryGetValue(
                                targetCall.Function,
                                out var targetMethodDefRow))
                        {
                            throw new InvalidDataException(
                                "A class call Body targets a MethodDef outside its retained class closure.");
                        }

                        body = EncodeC1M1Construction2CallNumericBinaryBody(
                            method.Function, callBinaryExpression,
                            targetMethodDefRow,
                            program.Functions[targetCall.Function], program,
                            frozenScalarValueBuilder,
                            Construction2ValueTypeSpecReference,
                            contractRows.Count, schema);
                    }
                    else
                    {
                        throw new InvalidDataException(
                            "A retained class call Body lost its checked direct target.");
                    }
                }

                if (body is null)
                {
                    body = method.Function.IsStatic
                        ? EncodeC1M1Construction2StaticMethodBody(
                            method.Function, Construction2ValueTypeSpecReference,
                            frozenScalarValueBuilder, contractRows.Count,
                            schema)
                        : method.DirectReturnHome >= 0
                        ? EncodeC1M1Construction2InstanceMethodBody(
                            method.Function, method.DirectReturnHome,
                            Construction2ValueTypeSpecReference,
                            contractRows.Count, schema)
                        : EncodeC1M1Construction2NoOpInstanceMethodBody(
                            method.Function,
                            NoOpMethodTypeSpecReference,
                            contractRows.Count, schema);
                }

                bodyRows.Add(body);
                methodRows[method.MethodDefRow - 1]
                    = methodRows[method.MethodDefRow - 1]
                        with { BodyRow = bodyRows.Count };
            }

            if (frozenScalarValueBuilder.ConstantSourceCount != 0
                && !featureRows.Contains(TrRmFeatureRow.TypedFrozenValues))
            {
                featureRows.Add(TrRmFeatureRow.TypedFrozenValues);
            }

        }

        var classMethodPlans
            = new List<C1M1SourceClassMethodPlan>();
        var classMethodGenericFormalPlans
            = new List<C1M1SourceClassMethodGenericFormalPlan>();
        for (var classViewIndex = 0;
            classViewIndex < standardClassTypeDefViews.Count;
            classViewIndex++)
        {
            var viewPlan = standardClassTypeDefViews[classViewIndex];
            var providerView = program.StandardClassDeclarations[
                viewPlan.StandardClassIndex].Declaration;
            var methodViews = viewPlan.Expectations.IsEmpty
                ? new[] { (viewPlan.Declaration,
                    (BoundStandardClassExpectation?)null) }
                : viewPlan.Expectations.Select(static expectation =>
                    (expectation.ExpectedDeclaration,
                    (BoundStandardClassExpectation?)expectation))
                    .ToArray();
            foreach (var (methodView, viewExpectation) in methodViews)
            {
                if (viewExpectation is BoundStandardClassExpectation
                        expectedView
                    && (expectedView.Binding.ExpectedDefinitionId
                            != methodView.Identity.DefinitionId
                        || !SameA2SourceTypeProviderSnapshot(
                            expectedView.Binding.UsingModule,
                            methodView.Provider)))
                {
                    throw new InvalidDataException(
                        "A retained class MethodDef view lost its exact owning TypeDef context.");
                }

                foreach (var method in methodView.Methods)
                {
                    if (method.DefinitionId >> 24 != 0x16
                        || (method.DefinitionId & 0x00FF_FFFF) == 0
                        || method.Parameters.IsDefault
                        || method.ExportKeys.IsDefault
                        || method.SelectedExportKeys.IsDefault
                        || method.GenericParameters.IsDefault
                        || method.DependencyClauses.IsDefault)
                    {
                        throw new InvalidDataException(
                            "A retained class MethodDef view has invalid declaration rows.");
                    }

                    if (schema.UsesGenericMethodCalls()
                        && method.GenericParameters.Any(formal =>
                            formal is null || formal.Constraints.IsDefault))
                    {
                        throw new InvalidDataException(
                            "A retained class MethodDef has an invalid generic constraint view.");
                    }

                    var hasRetainedGenericConstraints
                        = schema.UsesGenericMethodCalls()
                            && method.GenericParameters.Any(formal =>
                                formal.Constraints.Any(constraint =>
                                    constraint.Kind == 1
                                    || (schema
                                        .UsesAssociatedRelationConstraints()
                                        && constraint.Kind == 3)));
                    var genericMethodRoots
                        = hasRetainedGenericConstraints
                            ? GetC1M1SourceClassMethodGenericRoots(program,
                                method, methodView, viewExpectation,
                                viewPlan.Provider)
                            : ImmutableArray<BoundGenericMethodImport>.Empty;

                    var imports
                        = ImmutableArray.CreateBuilder<
                            C1M1SourceClassMethodImportPlan>();
                    var importKeys = new HashSet<(string ModuleIdentity,
                        string ExportKey)>();
                    if (viewExpectation is BoundStandardClassExpectation
                        expectation)
                    {
                        foreach (var methodExpectation
                            in expectation.MethodExpectations.Where(
                                candidate => candidate.ExpectedDefinitionId
                                    == method.DefinitionId))
                        {
                            if (methodExpectation.OwnerImportRow
                                    != expectation.Binding.ImportRow
                                || methodExpectation.ExpectedOwnerDefinitionId
                                    != expectation.Binding.ExpectedDefinitionId
                                || !SameA2SourceTypeProviderSnapshot(
                                    methodExpectation.Provider,
                                    viewPlan.Provider)
                                || providerView.Identity.DefinitionId
                                    != methodExpectation
                                        .ProviderOwnerDefinitionId
                                || !providerView.Methods.Any(
                                    providerMethod =>
                                        providerMethod.DefinitionId
                                            == methodExpectation
                                                .ProviderDefinitionId
                                        && providerMethod.ExportKeys.Contains(
                                            methodExpectation.ExportKey,
                                            StringComparer.Ordinal)))
                            {
                                throw new InvalidDataException(
                                    "A retained class MethodDef Import lost its exact provider, owner, or ExportKey.");
                            }

                            if (importKeys.Add((
                                    methodExpectation.Provider.ModuleIdentity,
                                    methodExpectation.ExportKey)))
                            {
                                imports.Add(
                                    new C1M1SourceClassMethodImportPlan(
                                        methodExpectation.Provider,
                                        methodExpectation.ExportKey));
                            }
                        }
                    }

                    foreach (var exportKey in method.SelectedExportKeys)
                    {
                        var providerMethod = providerView.Methods
                            .SingleOrDefault(candidate =>
                                StringComparer.Ordinal.Equals(
                                    candidate.Name, method.Name)
                                && candidate.ExportKeys.Contains(exportKey,
                                    StringComparer.Ordinal));
                        if (providerMethod is null)
                        {
                            throw new InvalidDataException(
                                "A selected class MethodDef key is absent from its checked provider owner.");
                        }

                        if (importKeys.Add((
                                viewPlan.Provider.ModuleIdentity, exportKey)))
                        {
                            imports.Add(
                                new C1M1SourceClassMethodImportPlan(
                                    viewPlan.Provider, exportKey));
                        }
                    }

                    if (classMethodPlans.Count >= 0xFF_FFFF
                        || methodRows.Count + classMethodPlans.Count
                            >= 0xFF_FFFF)
                    {
                        throw Unsupported(
                            "C1M1 class MethodDef views exceed the metadata row limit.");
                    }

                    var methodRow
                        = methodRows.Count + classMethodPlans.Count + 1;
                    if (!methodDefRowByStandardClassViewAndId.TryAdd(
                            (classViewIndex, method.DefinitionId),
                            methodRow))
                    {
                        throw new InvalidDataException(
                            "A class TypeDef view contains a duplicate MethodDef identity.");
                    }

                    classMethodPlans.Add(new C1M1SourceClassMethodPlan(
                        classViewIndex, methodView, method, methodRow,
                        imports.MoveToImmutable(), genericMethodRoots,
                        hasRetainedGenericConstraints
                            ? viewPlan.Provider : null));
                }
            }
        }

        for (var classViewIndex = 0;
            classViewIndex < standardClassTypeDefViews.Count;
            classViewIndex++)
        {
            var viewPlan = standardClassTypeDefViews[classViewIndex];
            var classView = viewPlan.Declaration;
            var sourceSnapshot = classView.Provider;
            var sourceArtifact = sourceSnapshot.ArtifactSnapshot.ToArray();
            var sourceIndex = new TrRmArtifactIndex(sourceArtifact,
                GetC1M1ClassMetadataSchema(sourceSnapshot.SchemaIdentity),
                standardSchemaIdentity: sourceSnapshot.SchemaIdentity);
            var sourceTables = TrRmStructuralMetadataValidator
                .ValidateC1AndGetExecutionTables(sourceIndex);
            if (sourceSnapshot.ModuleRow <= 0
                || sourceSnapshot.ModuleRow > sourceTables.Modules.Count
                || !StringComparer.Ordinal.Equals(
                    sourceTables.Modules.GetIdentity(
                        sourceSnapshot.ModuleRow),
                    sourceSnapshot.ModuleIdentity))
            {
                throw new InvalidDataException(
                    "A retained class MethodDef view has an invalid owner Module snapshot.");
            }

            var viewMethodPlans = classMethodPlans.Where(plan =>
                plan.StandardClassViewIndex == classViewIndex).ToArray();
            foreach (var plan in viewMethodPlans)
            {
                var method = plan.Method;
                var methodSourceSnapshot = plan.OwnerView.Provider;
                var methodSourceArtifact
                    = methodSourceSnapshot.ArtifactSnapshot.ToArray();
                var methodSourceIndex = new TrRmArtifactIndex(
                    methodSourceArtifact,
                    GetC1M1ClassMetadataSchema(
                        methodSourceSnapshot.SchemaIdentity),
                    standardSchemaIdentity:
                        methodSourceSnapshot.SchemaIdentity);
                var methodSourceTables
                    = TrRmStructuralMetadataValidator
                        .ValidateC1AndGetExecutionTables(
                            methodSourceIndex);
                if (methodSourceSnapshot.ModuleRow <= 0
                    || methodSourceSnapshot.ModuleRow
                        > methodSourceTables.Modules.Count
                    || !StringComparer.Ordinal.Equals(
                        methodSourceTables.Modules.GetIdentity(
                            methodSourceSnapshot.ModuleRow),
                        methodSourceSnapshot.ModuleIdentity))
                {
                    throw new InvalidDataException(
                        "A retained class MethodDef view has an invalid owner Module snapshot.");
                }

                var (methodTypeDefRowMap, methodTypeImportRowMap,
                    ambiguousMethodTypeDefRows)
                    = CreateMethodTypeMaps(methodSourceSnapshot);
                var sourceMethodRow = checked((int)
                    (method.DefinitionId & 0x00FF_FFFF));
                var sourceOwnerTypeDefRow = checked((int)
                    (plan.OwnerView.Identity.DefinitionId & 0x00FF_FFFF));
                var exactMethodOwnerTypeDefRows
                    = new Dictionary<int, int> {
                        [sourceOwnerTypeDefRow]
                            = typeDefRowByStandardClassView[
                                classViewIndex],
                    };
                if (methodSourceTables.Methods.GetOwnerType(sourceMethodRow)
                        != sourceOwnerTypeDefRow
                    || methodSourceTables.Methods.GetBodyRow(sourceMethodRow)
                        != checked((int)method.BodyRow))
                {
                    throw new InvalidDataException(
                        "A retained class MethodDef changed its source owner or Body role.");
                }

                var outputMethodRow = plan.MethodRow;
                var genericParamFirst = method.GenericParameters.IsEmpty
                    ? 0 : genericParamRows.Count + 1;
                var genericParamRowMap = new Dictionary<int, int>();
                if (method.GenericParameterCount
                        != method.GenericParameters.Length
                    || method.GenericParameterCount > 8)
                {
                    throw Unsupported(
                        "Class MethodDef GenericParam views exceed the selected source-class writer contract.");
                }

                for (var ordinal = 0;
                    ordinal < method.GenericParameters.Length;
                    ordinal++)
                {
                    var formal = method.GenericParameters[ordinal];
                    if (formal is null || formal.Row <= 0
                        || formal.OwnerDefinitionId != method.DefinitionId
                        || formal.Ordinal != (uint)ordinal
                        || formal.FormalKind != 1
                        || formal.ConstantTypeSpecReference != 0
                        || formal.Constraints.IsDefault
                        || string.IsNullOrEmpty(formal.Name))
                    {
                        throw new InvalidDataException(
                            "A class MethodDef GenericParam has an invalid owner, kind, or ordinal.");
                    }

                    var outputGenericParamRow
                        = genericParamRows.Count + 1;
                    if (!genericParamRowMap.TryAdd(formal.Row,
                            outputGenericParamRow))
                    {
                        throw new InvalidDataException(
                            "A class MethodDef GenericParam row is duplicated.");
                    }

                    genericParamNames.Add(formal.Name);
                    genericParamRows.Add(default);
                    if (schema.UsesGenericMethodCalls()
                        && formal.Constraints.Any(constraint =>
                            constraint.Kind == 1
                            || (schema.UsesAssociatedRelationConstraints()
                                && constraint.Kind == 3)))
                    {
                        if (plan.GenericMethodRoots.IsEmpty
                            || plan.GenericMethodProvider is not
                                BoundSourceTypeProviderSnapshot provider)
                        {
                            throw new InvalidDataException(
                                "A class MethodDef generic constraint has no exact typed provider root.");
                        }

                        classMethodGenericFormalPlans.Add(
                            new C1M1SourceClassMethodGenericFormalPlan(
                                outputMethodRow, outputGenericParamRow,
                                ordinal, formal,
                                plan.GenericMethodRoots, provider));
                    }
                }

                var sourceTypeSpecRows = new Dictionary<int, uint>();
                var activeTypeSpecRows = new HashSet<int>();

                var signatureRow = methodSignatureRows.Count + 1;
                var resultTypeSpec = RemapC1M1ClassTypeSpec(
                    methodSourceTables,
                    method.ReturnTypeSpecReference, methodTypeDefRowMap,
                    methodTypeImportRowMap, typeSpecRows,
                    sourceTypeSpecRows, activeTypeSpecRows, depth: 0,
                    genericParamRowMap: genericParamRowMap,
                    ambiguousTypeDefRows: ambiguousMethodTypeDefRows,
                    exactTypeDefRowMap: exactMethodOwnerTypeDefRows);
                var receiverTypeSpec = method.ReceiverTypeSpecReference == 0
                    ? 0 : RemapC1M1ClassTypeSpec(methodSourceTables,
                        method.ReceiverTypeSpecReference,
                        methodTypeDefRowMap, methodTypeImportRowMap,
                        typeSpecRows, sourceTypeSpecRows,
                        activeTypeSpecRows, depth: 0,
                        genericParamRowMap: genericParamRowMap,
                        ambiguousTypeDefRows: ambiguousMethodTypeDefRows,
                        exactTypeDefRowMap:
                            exactMethodOwnerTypeDefRows);
                var parameterFirst = method.Parameters.IsEmpty
                    ? 0 : parameterRows.Count + 1;
                for (var ordinal = 0; ordinal < method.Parameters.Length;
                    ordinal++)
                {
                    var parameter = method.Parameters[ordinal];
                    var typeSpecReference = RemapC1M1ClassTypeSpec(
                        methodSourceTables, parameter.TypeSpecReference,
                        methodTypeDefRowMap, methodTypeImportRowMap,
                        typeSpecRows, sourceTypeSpecRows,
                        activeTypeSpecRows, depth: 0,
                        genericParamRowMap: genericParamRowMap,
                        ambiguousTypeDefRows: ambiguousMethodTypeDefRows,
                        exactTypeDefRowMap:
                            exactMethodOwnerTypeDefRows);
                    parameterRows.Add(new TrRmParamRow(signatureRow, ordinal,
                        typeSpecReference, parameter.Mode, parameter.Flags));

                    if (schema.UsesClassParameterDefaults())
                    {
                        uint defaultConstantSourceRow = 0;
                        if (parameter.DefaultValue is BoundConstant value)
                        {
                            if (!TryGetC1M1ClassScalarType(
                                    methodSourceTables,
                                    parameter.TypeSpecReference,
                                    out var parameterType)
                                || parameterType
                                    is PrimitiveType.NativeInt
                                        or PrimitiveType.NativeUInt
                                || value.Type != parameterType
                                || value.RepresentationType != parameterType
                                || !value.Bits.IsCanonicalFor(parameterType))
                            {
                                throw Unsupported(
                                    "A retained class MethodDef default must be an exact fixed-width scalar value.");
                            }

                            var frozenValue
                                = frozenScalarValueBuilder.Add(value);
                            if (frozenValue.TypeSpecRef != typeSpecReference)
                            {
                                throw Unsupported(
                                    "A retained class MethodDef default changed its exact parameter TypeSpec.");
                            }

                            defaultConstantSourceRow
                                = frozenValue.ConstantSourceRow;
                        }

                        parameterSurfaceRows.Add(
                            new C1M1ParameterSurfaceRow(outputMethodRow,
                                ordinal, parameter.Name,
                                defaultConstantSourceRow));
                    }
                    else if (parameter.DefaultValue is not null)
                    {
                        throw Unsupported(
                            "The selected source-class schema does not retain class MethodDef parameter defaults.");
                    }
                }

                methodSignatureRows.Add(new TrRmMethodSigRow(
                    resultTypeSpec, receiverTypeSpec, parameterFirst,
                    method.Parameters.Length, genericParamFirst,
                    method.GenericParameterCount));

                var contractFirst = 0;
                var methodContractCount = 0;
                void AddMethodContract(ushort kind, byte[] payload)
                {
                    if (contractFirst == 0)
                    {
                        contractFirst = contractRows.Count + 1;
                    }

                    contractRows.Add(new TrRmContractRow(
                        0x1600_0000u | checked((uint)outputMethodRow),
                        kind, payload));
                    methodContractCount++;
                }

                if (method.EffectsMask != 0)
                {
                    if ((method.EffectsMask & ~6u) != 0
                        || !s_effectPayloads.TryGetValue(
                            method.EffectsMask, out var effectPayload))
                    {
                        throw Unsupported(
                            "A retained class MethodDef has an unselected effect contract.");
                    }

                    AddMethodContract(1, effectPayload);
                }

                if (method.ConstructionRole is 1 or 2)
                {
                    if (!schema.UsesObjectConstruction()
                        || method.ConstructionPropertyDefinitionId != 0)
                    {
                        throw Unsupported(
                            "A retained class MethodDef construction contract is not selected by this source schema.");
                    }

                    byte[] constructionPayload;
                    if (method.ConstructionRole == 1)
                    {
                        if (method.InitializerMethodDefinitionId != 0
                            || method.InitializedMemberDefinitionIds.IsDefault)
                        {
                            throw new InvalidDataException(
                                "A class initializer MethodDef has an invalid construction role.");
                        }

                        var initializedFields = new uint[
                            method.InitializedMemberDefinitionIds.Length];
                        for (var memberIndex = 0;
                            memberIndex < initializedFields.Length;
                            memberIndex++)
                        {
                            var fieldDefinitionId
                                = method.InitializedMemberDefinitionIds[
                                    memberIndex];
                            var fieldIndex = -1;
                            for (var candidateIndex = 0;
                                candidateIndex < classView.Fields.Length;
                                candidateIndex++)
                            {
                                if (classView.Fields[candidateIndex]
                                        .DefinitionId == fieldDefinitionId)
                                {
                                    fieldIndex = candidateIndex;
                                    break;
                                }
                            }
                            if (fieldIndex < 0)
                            {
                                foreach (var expectation
                                    in viewPlan.Expectations)
                                {
                                    var fieldExpectation
                                        = expectation.FieldExpectations
                                            .SingleOrDefault(candidate =>
                                                candidate.ExpectedDefinitionId
                                                    == fieldDefinitionId
                                                && candidate
                                                    .ExpectedOwnerDefinitionId
                                                    == plan.OwnerView.Identity
                                                        .DefinitionId
                                                && candidate
                                                    .ProviderOwnerDefinitionId
                                                    == classView.Identity
                                                        .DefinitionId);
                                    if (fieldExpectation is null)
                                    {
                                        continue;
                                    }

                                    for (var candidateIndex = 0;
                                        candidateIndex
                                            < classView.Fields.Length;
                                        candidateIndex++)
                                    {
                                        if (classView.Fields[candidateIndex]
                                                .DefinitionId
                                            == fieldExpectation
                                                .ProviderDefinitionId)
                                        {
                                            if (fieldIndex >= 0)
                                            {
                                                throw new InvalidDataException(
                                                    "A class initializer FieldDef expectation has multiple destination rows.");
                                            }

                                            fieldIndex = candidateIndex;
                                        }
                                    }
                                }
                            }
                            if (fieldIndex < 0
                                || !fieldDefRowByStandardClassViewAndIndex
                                    .TryGetValue((classViewIndex, fieldIndex),
                                        out var fieldDefRow))
                            {
                                throw new InvalidDataException(
                                    "A class initializer MethodDef refers to an unretained FieldDef.");
                            }

                            initializedFields[memberIndex]
                                = 0x1400_0000u
                                    | checked((uint)fieldDefRow);
                        }

                        constructionPayload = new byte[checked(
                            8 + (initializedFields.Length * sizeof(uint)))];
                        constructionPayload[0] = 1;
                        BinaryPrimitives.WriteUInt32LittleEndian(
                            constructionPayload.AsSpan(4),
                            checked((uint)initializedFields.Length));
                        for (var memberIndex = 0;
                            memberIndex < initializedFields.Length;
                            memberIndex++)
                        {
                            BinaryPrimitives.WriteUInt32LittleEndian(
                                constructionPayload.AsSpan(
                                    8 + (memberIndex * sizeof(uint))),
                                initializedFields[memberIndex]);
                        }
                    }
                    else
                    {
                        if ((method.InitializerMethodDefinitionId >> 24)
                                != 0x16
                            || !methodDefRowByStandardClassViewAndId.TryGetValue(
                                (classViewIndex,
                                    method.InitializerMethodDefinitionId),
                                out var initializerMethodRow))
                        {
                            throw new InvalidDataException(
                                "A class construction projection lost its exact owner initializer MethodDef.");
                        }

                        constructionPayload
                            = EncodeC1M1Construction2ProjectionContract(
                                initializerMethodRow);
                    }

                    AddMethodContract(0x0003, constructionPayload);
                }
                else if (method.ConstructionRole != 0
                    || method.InitializerMethodDefinitionId != 0
                    || method.ConstructionPropertyDefinitionId != 0
                    || !method.InitializedMemberDefinitionIds
                        .IsDefaultOrEmpty)
                {
                    throw new InvalidDataException(
                        "A class MethodDef has an unsupported construction role.");
                }

                foreach (var dependency
                    in method.DependencyClauses)
                {
                    var payload = new byte[12];
                    payload[0] = dependency.DestinationKind;
                    payload[1] = dependency.SourceKind;
                    payload[2] = dependency.Relationship;
                    payload[3] = dependency.Phase;
                    BinaryPrimitives.WriteUInt32LittleEndian(
                        payload.AsSpan(4),
                        dependency.DestinationOrdinal);
                    BinaryPrimitives.WriteUInt32LittleEndian(
                        payload.AsSpan(8), dependency.SourceOrdinal);
                    AddMethodContract(4, payload);
                }

                if (method.GenericParameters.IsEmpty
                    && method.GenericParameterCount != 0)
                {
                    throw new InvalidDataException(
                        "A class MethodDef lost its GenericParam rows.");
                }
                for (var formalOrdinal = 0;
                    formalOrdinal < method.GenericParameters.Length;
                    formalOrdinal++)
                {
                    var formal = method.GenericParameters[formalOrdinal];
                    var genericParamRow = genericParamRowMap[formal.Row];
                    var constraintFirst = genericConstraintRows.Count + 1;
                    foreach (var constraint in formal.Constraints)
                    {
                        if (constraint.OwnerGenericParamRow != formal.Row)
                        {
                            throw new InvalidDataException(
                                "A class MethodDef Constraint has an invalid owner.");
                        }

                        uint payload;
                        switch (constraint.Kind)
                        {
                            case 1 when schema.UsesGenericMethodCalls():
                            case 3 when schema
                                .UsesAssociatedRelationConstraints():
                                continue;
                            case 1:
                                payload = RemapC1M1ClassTypeSpec(
                                    methodSourceTables, constraint.Payload,
                                    methodTypeDefRowMap,
                                    methodTypeImportRowMap, typeSpecRows,
                                    sourceTypeSpecRows, activeTypeSpecRows,
                                    depth: 0,
                                    genericParamRowMap:
                                        genericParamRowMap,
                                    ambiguousTypeDefRows:
                                        ambiguousMethodTypeDefRows,
                                    exactTypeDefRowMap:
                                        exactMethodOwnerTypeDefRows);
                                break;
                            case 2 when constraint.Payload is >= 1 and <= 3:
                                payload = constraint.Payload;
                                break;
                            default:
                                throw new InvalidDataException(
                                    "A class MethodDef Constraint has an unsupported kind or payload.");
                        }

                        genericConstraintRows.Add(new TrRmConstraintRow(
                            genericParamRow, constraint.Kind, payload));
                    }

                    var constraintCount = genericConstraintRows.Count
                        - constraintFirst;
                    if (constraintCount == 0)
                    {
                        constraintFirst = 0;
                    }

                    genericParamRows[genericParamRow - 1]
                        = new TrRmGenericParamRow(
                            0x1600_0000u
                                | checked((uint)outputMethodRow),
                            formalOrdinal, 0, constraintFirst,
                            constraintCount, formal.FormalKind,
                            formal.OwnershipDomain,
                            formal.NullabilityDomain, 0,
                            formal.DefaultArgumentReference == 0 ? 0
                                : RemapC1M1ClassTypeSpec(methodSourceTables,
                                    formal.DefaultArgumentReference,
                                    methodTypeDefRowMap,
                                    methodTypeImportRowMap, typeSpecRows,
                                    sourceTypeSpecRows,
                                    activeTypeSpecRows, depth: 0,
                                    genericParamRowMap:
                                        genericParamRowMap,
                                    ambiguousTypeDefRows:
                                        ambiguousMethodTypeDefRows,
                                    exactTypeDefRowMap:
                                        exactMethodOwnerTypeDefRows));
                }

                var ownerTypeDefRow
                    = typeDefRowByStandardClassView[classViewIndex];
                methodRows.Add(new TrRmMethodDefRow(ownerTypeDefRow,
                    0, signatureRow, method.Flags,
                    contractFirst, methodContractCount, 0));
                methodNames.Add(method.Name);

                foreach (var methodImport in plan.Imports)
                {
                    _ = AddImport(methodImport.Provider.ModuleIdentity,
                        methodImport.Provider.TargetRequirement,
                        methodImport.Provider.RuntimePolicy,
                        methodImport.ExportKey,
                        0x1600_0000u | checked((uint)outputMethodRow));
                }
            }
        }

        if (frozenScalarValueBuilder.ConstantSourceCount != 0
            && !featureRows.Contains(TrRmFeatureRow.TypedFrozenValues))
        {
            featureRows.Add(TrRmFeatureRow.TypedFrozenValues);
        }

        if (hasTypeOnlyContext)
        {
            if (program.Functions.Any(static function =>
                    function.ModuleImport is null
                    || !function.IsExternal
                    || !function.IsModuleReference
                    || function.IsPrivate || function.IsInternal
                    || function.IsClassConstructor
                    || function.IsConstructor
                    || function.DirectNativeImport is not null
                    || function.NativeVTableMethod is not null
                    || function.NativeFunctionPointerType is not null
                    || function.NativeCallableConvention is not null
                    || function.ParameterCount != function.Variables.Length
                    || function.ParameterCount
                        > SemanticVerifier.MaximumArguments))
            {
                throw Unsupported(
                    "Contextual method/type references support only bodyless imported top-level MethodDefs with bounded by-value signatures.");
            }

            for (var functionIndex = 0;
                functionIndex < program.Functions.Length;
                functionIndex++)
            {
                var function = program.Functions[functionIndex];
                var moduleImport = function.ModuleImport!;
                if (moduleImport.ExportKey != function.Name
                    || moduleImport.TargetRequirement
                        != TrRmModuleTableReader
                            .WindowsX64NativeTargetRequirement
                    || moduleImport.RuntimePolicy != 0
                    || function.ReturnType != PrimitiveType.Void
                        && !TryGetC1M1SourceScalarTypeSpecReference(
                            function.ReturnType, out _))
                {
                    throw Unsupported(
                        "A contextual method Import has an unsupported identity, requirement or result type.");
                }

                var methodDefRow = methodRows.Count + 1;
                var signatureRow = methodSignatureRows.Count + 1;
                var firstParameter = parameterRows.Count + 1;
                for (var ordinal = 0;
                    ordinal < function.ParameterCount; ordinal++)
                {
                    var variable = function.Variables[ordinal];
                    if (variable.IsOut || variable.IsInit
                        || variable.IsMutable || variable.IsImplicitRef
                        || variable.IsCanonicalImplicitReference
                        || variable.IsInitializationLocal)
                    {
                        throw Unsupported(
                            "Contextual method Imports support only ordinary immutable by-value parameters.");
                    }

                    uint typeSpecReference;
                    if (TryGetC1M1SourceStructFieldType(variable.Type,
                            sourceStructDeclarations, out _))
                    {
                        var typeViewIndex
                            = GetMethodSignatureTypeViewIndex(function,
                                ordinal, variable.Type);
                        typeSpecReference
                            = ContextualFieldTypeSpecReference(
                                variable.Type, typeViewIndex);
                    }
                    else
                    {
                        if (variable.Type != PrimitiveType.Void
                            && !TryGetC1M1SourceScalarTypeSpecReference(
                                variable.Type, out _))
                        {
                            throw Unsupported(
                                "Contextual method Imports support only scalar, unit or selected struct parameters.");
                        }

                        typeSpecReference
                            = Construction2ValueTypeSpecReference(
                                variable.Type);
                    }

                    parameterRows.Add(new TrRmParamRow(signatureRow,
                        ordinal, typeSpecReference, 0, 0));
                }

                var returnTypeSpecReference
                    = function.ReturnType == PrimitiveType.Void
                        ? 0xF200_0000u
                        : Construction2ValueTypeSpecReference(
                            function.ReturnType);
                methodSignatureRows.Add(new TrRmMethodSigRow(
                    returnTypeSpecReference, 0,
                    function.ParameterCount == 0 ? 0 : firstParameter,
                    function.ParameterCount, 0, 0));
                methodRows.Add(new TrRmMethodDefRow(0, 0, signatureRow,
                    2, 0, 0, 0));
                methodNames.Add(function.Name);
                methodDefRowByFunctionIndex.Add(functionIndex,
                    methodDefRow);
                _ = AddImport(moduleImport.ProducerIdentity,
                    moduleImport.TargetRequirement,
                    moduleImport.RuntimePolicy, moduleImport.ExportKey,
                    0x1600_0000u | checked((uint)methodDefRow));
            }
        }

        var totalRows = (long)typeDefRows.Count + fieldRows.Count
            + layoutRows.Count + layoutFieldRows.Count
            + typeSpecRows.Count + exportRows.Count
            + moduleReferences.Count + imports.Count + methodRows.Count
            + methodSignatureRows.Count + parameterRows.Count
            + genericParamRows.Count + genericConstraintRows.Count
            + parameterSurfaceRows.Count + contractRows.Count
            + bodyRows.Count + featureRows.Count
            + frozenScalarValueBuilder.BlobCount
            + frozenScalarValueBuilder.FrozenValueNodeCount
            + frozenScalarValueBuilder.ConstantSourceCount;
        if (typeDefRows.Count > 0xFF_FFFF
            || fieldRows.Count > 0xFF_FFFF
            || layoutRows.Count > 0xFF_FFFF
            || layoutFieldRows.Count > 0xFF_FFFF
            || typeSpecRows.Count > 0xFF_FFFF
            || exportRows.Count > 0xFF_FFFF
            || moduleReferences.Count > 0xFF_FFFF
            || imports.Count > 0xFF_FFFF
            || methodRows.Count > 0xFF_FFFF
            || methodSignatureRows.Count > 0xFF_FFFF
            || parameterRows.Count > 0xFF_FFFF
            || genericParamRows.Count > 0xFF_FFFF
            || genericConstraintRows.Count > 0xFF_FFFF
            || parameterSurfaceRows.Count > 0xFF_FFFF
            || contractRows.Count > 0xFF_FFFF
            || bodyRows.Count > 0xFF_FFFF
            || featureRows.Count > 0xFF_FFFF
            || frozenScalarValueBuilder.BlobCount > 0xFF_FFFF
            || frozenScalarValueBuilder.FrozenValueNodeCount > 0xFF_FFFF
            || frozenScalarValueBuilder.ConstantSourceCount > 0xFF_FFFF
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

        foreach (var methodName in methodNames)
        {
            _ = AddString(methodName);
        }

        foreach (var genericParamName in genericParamNames)
        {
            _ = AddString(genericParamName);
        }

        foreach (var parameterSurface in parameterSurfaceRows)
        {
            _ = AddString(parameterSurface.Name);
        }

        foreach (var (_, exportKey) in exportRows)
        {
            _ = AddString(exportKey);
        }

        foreach (var (producerIdentity, _, _) in moduleReferences)
        {
            _ = AddString(producerIdentity);
        }

        foreach (var import in imports)
        {
            _ = AddString(import.ExportKey);
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

        for (var methodIndex = 0;
            methodIndex < methodRows.Count; methodIndex++)
        {
            methodRows[methodIndex] = methodRows[methodIndex] with {
                NameStringRow = stringRows[methodNames[methodIndex]],
            };
        }

        for (var genericParamIndex = 0;
            genericParamIndex < genericParamRows.Count;
            genericParamIndex++)
        {
            genericParamRows[genericParamIndex]
                = genericParamRows[genericParamIndex] with {
                    NameStringRow = stringRows[
                        genericParamNames[genericParamIndex]],
                };
        }

        var featureBytes = featureRows.Count == 0 ? []
            : TrRmFeatureTableWriter.Encode(featureRows, schema);
        var features = featureRows.Count == 0 ? default
            : new TrRmFeatureTableReader(featureBytes,
                featureRows.Count, schema);
        var moduleRefRows = moduleReferences.Select(reference =>
            new TrRmModuleRefRow(
                stringRows[reference.ProducerIdentity],
                reference.TargetRequirement, reference.RuntimePolicy))
            .ToArray();
        var moduleBytes = TrRmModuleTableWriter.Encode([
            new TrRmModuleRow(moduleIdentityStringRow,
                moduleTargetRequirement, (uint)program.RuntimePolicy, 0,
                1, 1, 1, 1,
                featureRows.Count == 0 ? 0u : 1u,
                checked((uint)featureRows.Count)),
        ], strings, moduleRefRows.Length, typeDefRows.Count,
            methodRows.Count, 0,
            featureRows.Count);
        var modules = new TrRmModuleTableReader(moduleBytes, 1, strings,
            moduleRefRows.Length, typeDefRows.Count, methodRows.Count, 0,
            featureRows.Count);
        var moduleRefBytes = moduleRefRows.Length == 0 ? []
            : TrRmModuleRefTableWriter.Encode(moduleRefRows, strings,
                modules);
        var moduleRefs = moduleRefRows.Length == 0 ? default
            : new TrRmModuleRefTableReader(moduleRefBytes,
                moduleRefRows.Length, strings, modules);
        var typeDefBytes = TrRmTypeDefTableWriter.Encode(typeDefRows,
            strings, modules, fieldRows.Count, genericParamRows.Count,
            layoutRows.Count, contractRows.Count, schema);
        var typeDefs = new TrRmTypeDefTableReader(typeDefBytes,
            typeDefRows.Count, strings, modules, fieldRows.Count,
            genericParamRows.Count, layoutRows.Count, contractRows.Count,
            schema);
        var typeSpecBytes = typeSpecRows.Count == 0 ? [] :
            TrRmTypeSpecTableWriter.Encode(typeSpecRows, typeDefRows.Count,
                imports.Count, genericParamRows.Count, 0, schema,
                methodDefCount: methodRows.Count);
        var typeSpecs = new TrRmTypeSpecTableReader(
            typeSpecRows.Count == 0
                ? new byte[sizeof(uint)] : typeSpecBytes,
            typeSpecRows.Count, typeDefRows.Count, imports.Count,
            genericParamRows.Count, 0, schema,
            methodDefCount: methodRows.Count);
        var frozenScalarValueTables
            = frozenScalarValueBuilder.Encode(typeSpecs);
        var parameterSurfaceBytes
            = new byte[checked(parameterSurfaceRows.Count * 16)];
        for (var index = 0; index < parameterSurfaceRows.Count; index++)
        {
            var parameterSurface = parameterSurfaceRows[index];
            var offset = index * 16;
            BinaryPrimitives.WriteUInt32LittleEndian(
                parameterSurfaceBytes.AsSpan(offset, 4),
                checked((uint)parameterSurface.MethodDefRow));
            BinaryPrimitives.WriteUInt32LittleEndian(
                parameterSurfaceBytes.AsSpan(offset + 4, 4),
                checked((uint)parameterSurface.Ordinal));
            BinaryPrimitives.WriteUInt32LittleEndian(
                parameterSurfaceBytes.AsSpan(offset + 8, 4),
                checked((uint)stringRows[parameterSurface.Name]));
            BinaryPrimitives.WriteUInt32LittleEndian(
                parameterSurfaceBytes.AsSpan(offset + 12, 4),
                parameterSurface.DefaultConstantSourceRow);
        }
        var fieldBytes = TrRmFieldDefTableWriter.Encode(fieldRows, strings,
            typeDefs, typeSpecs, schema);
        var fields = new TrRmFieldDefTableReader(fieldBytes,
            fieldRows.Count, strings, typeDefs, typeSpecs);
        var importRows = imports.Select(import =>
            new TrRmImportRow(import.ModuleRefRow,
                stringRows[import.ExportKey],
                import.ExpectedDefinitionId)).ToArray();
        var signatureBytes = methodSignatureRows.Count == 0 ? []
            : TrRmMethodSigTableWriter.Encode(methodSignatureRows,
                typeSpecs, parameterRows.Count, genericParamRows.Count);
        var signatures = methodSignatureRows.Count == 0 ? default
            : new TrRmMethodSigTableReader(signatureBytes,
                methodSignatureRows.Count, typeSpecs,
                parameterRows.Count, genericParamRows.Count);
        var parameterBytes = parameterRows.Count == 0 ? []
            : TrRmParamTableWriter.Encode(parameterRows, signatures,
                typeSpecs, schema);
        var parameters = parameterRows.Count == 0 ? default
            : new TrRmParamTableReader(parameterBytes,
                parameterRows.Count, signatures, typeSpecs, schema);
        var methodBytes = methodRows.Count == 0 ? []
            : TrRmMethodDefTableWriter.Encode(methodRows, strings, modules,
                typeDefs, signatures, contractRows.Count, bodyRows.Count,
                schema);
        var methods = methodRows.Count == 0 ? default
            : new TrRmMethodDefTableReader(methodBytes, methodRows.Count,
                strings, modules, typeDefs, signatures, contractRows.Count,
                bodyRows.Count, schema);
        var importBytes = importRows.Length == 0 ? []
            : TrRmImportTableWriter.Encode(importRows, strings, moduleRefs,
                typeDefs, methods, default, default, default, fields,
                features, modules, schema);
        var imports = importRows.Length == 0 ? default
            : new TrRmImportTableReader(importBytes, importRows.Length,
                strings, moduleRefs, typeDefs, methods, default, default,
                default, fields, features, modules, schema);
        var genericParamBytes = genericParamRows.Count == 0 ? []
            : TrRmGenericParamTableWriter.Encode(genericParamRows, strings,
                typeDefs, methods, signatures, typeSpecs,
                genericConstraintRows.Count);
        var genericParams = genericParamRows.Count == 0 ? default
            : new TrRmGenericParamTableReader(genericParamBytes,
                genericParamRows.Count, strings, typeDefs, methods,
                signatures, typeSpecs, genericConstraintRows.Count);
        var associatedRelations
            = new TrRmAssociatedRelationTableReader([], 0, typeSpecs, 0,
                imports);
        var constraintBytes = genericConstraintRows.Count == 0 ? []
            : TrRmConstraintTableWriter.Encode(genericConstraintRows,
                genericParams, typeSpecs, associatedRelations);
        var contractBytes = contractRows.Count == 0 ? []
            : TrRmContractTableWriter.EncodeC1M1Construction2Draft(
                contractRows, typeDefs, methods, signatures, parameters,
                typeSpecs, fields, default, default, features, modules,
                predicateGraphCount: 0);
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
        var typeDefBindingEqualityRows
            = new List<TrRmTypeDefBindingEqualityRow>();
        if (IsSelectedTypeDefBindingEqualitySchemaIdentity(
                standardSchemaIdentity))
        {
            foreach (var structureIndex in retainedStructureIndexes.Order())
            {
                var origin = program.Structures[structureIndex]
                    .StandardTypeOrigin;
                if (origin is null
                    || origin.ContextualViewIndexes.Length < 2)
                {
                    continue;
                }

                var typeDefRowsForViews
                    = origin.ContextualViewIndexes
                        .Select(typeViewIndex =>
                            typeDefRowByTypeView.TryGetValue(
                                typeViewIndex, out var typeDefRow)
                                ? typeDefRow
                                : throw new InvalidDataException(
                                    "A contextual TypeDef equality endpoint was not emitted."))
                        .Order().ToArray();
                if (typeDefRowsForViews.Distinct().Count()
                    != typeDefRowsForViews.Length)
                {
                    throw new InvalidDataException(
                        "A contextual TypeDef equality group contains duplicate destination rows.");
                }

                var leftTypeDef = checked((uint)typeDefRowsForViews[0]);
                for (var index = 1;
                    index < typeDefRowsForViews.Length; index++)
                {
                    typeDefBindingEqualityRows.Add(
                        new TrRmTypeDefBindingEqualityRow(1,
                            leftTypeDef,
                            checked((uint)typeDefRowsForViews[index])));
                }
            }
        }
        if (IsSelectedClassTypeDefBindingEqualitySchemaIdentity(
                standardSchemaIdentity))
        {
            var parentByView = Enumerable.Range(0,
                standardClassTypeDefViews.Count).ToArray();

            int FindViewRoot(int viewIndex)
            {
                var root = viewIndex;
                while (parentByView[root] != root)
                {
                    root = parentByView[root];
                }

                while (parentByView[viewIndex] != viewIndex)
                {
                    var next = parentByView[viewIndex];
                    parentByView[viewIndex] = root;
                    viewIndex = next;
                }

                return root;
            }

            void JoinViews(int leftView, int rightView)
            {
                var leftRoot = FindViewRoot(leftView);
                var rightRoot = FindViewRoot(rightView);
                if (leftRoot != rightRoot)
                {
                    parentByView[Math.Max(leftRoot, rightRoot)]
                        = Math.Min(leftRoot, rightRoot);
                }
            }

            foreach (var group in sourceClassEqualityViewGroups)
            {
                var firstView = group[0];
                for (var index = 1; index < group.Length; index++)
                {
                    JoinViews(firstView, group[index]);
                }
            }

            foreach (var (standardClassIndex, viewIndexes)
                     in standardClassTypeDefViewIndexesByClass)
            {
                var declaration = program.StandardClassDeclarations[
                    standardClassIndex];
                foreach (var group in declaration
                             .TypeDefBindingEqualityGroups)
                {
                    if (group.OwnerModule is
                            BoundSourceTypeProviderSnapshot ownerModule
                        && !viewIndexes.Any(viewIndex =>
                            standardClassTypeDefViews[viewIndex]
                                .Expectations.Any(expectation =>
                                    SameA2SourceTypeProviderSnapshot(
                                        ownerModule,
                                        expectation.Binding.UsingModule))))
                    {
                        continue;
                    }

                    var endpointViews = new HashSet<int>();
                    foreach (var endpoint in group.Endpoints)
                    {
                        var matches = viewIndexes.Where(viewIndex =>
                        {
                            var view
                                = standardClassTypeDefViews[viewIndex];
                            if (endpoint.ImportRow == 0)
                            {
                                return view.IsSelectedView
                                    && endpoint.TypeDefDefinitionId
                                        == declaration.Declaration.Identity
                                            .DefinitionId
                                    && SameA2SourceTypeProviderSnapshot(
                                        endpoint.OwnerModule,
                                        view.Provider);
                            }

                            return view.Expectations.Any(expectation =>
                                expectation.Binding.ImportRow
                                    == endpoint.ImportRow
                                && expectation.Binding.ExpectedDefinitionId
                                    == endpoint.TypeDefDefinitionId
                                && StringComparer.Ordinal.Equals(
                                    expectation.Binding.ExportKey,
                                    endpoint.ExportKey)
                                && SameA2SourceTypeProviderSnapshot(
                                    endpoint.OwnerModule,
                                    expectation.Binding.UsingModule));
                        }).ToArray();
                        if (matches.Length == 0)
                        {
                            throw new InvalidDataException(
                                "A class TypeDef equality endpoint has no exact emitted view.");
                        }

                        foreach (var viewIndex in matches)
                        {
                            _ = endpointViews.Add(viewIndex);
                        }
                    }

                    if (endpointViews.Count < 2)
                    {
                        throw new InvalidDataException(
                            "A class TypeDef equality group has fewer than two emitted views.");
                    }

                    var firstView = endpointViews.Min();
                    foreach (var viewIndex in endpointViews)
                    {
                        JoinViews(firstView, viewIndex);
                    }
                }
            }

            var equalityComponents = Enumerable.Range(0,
                    standardClassTypeDefViews.Count)
                .GroupBy(FindViewRoot)
                .Where(static component => component.Count() > 1)
                .Select(component => component.Select(viewIndex =>
                        typeDefRowByStandardClassView[viewIndex])
                    .Order().ToArray())
                .OrderBy(static rows => rows[0])
                .ToArray();
            foreach (var component in equalityComponents)
            {
                var leftTypeDef = checked((uint)component[0]);
                for (var index = 1; index < component.Length; index++)
                {
                    typeDefBindingEqualityRows.Add(
                        new TrRmTypeDefBindingEqualityRow(1, leftTypeDef,
                            checked((uint)component[index])));
                }
            }

            typeDefBindingEqualityRows = [.. typeDefBindingEqualityRows
                .Distinct()
                .OrderBy(static row => row.OwnerModule)
                .ThenBy(static row => row.LeftTypeDef)
                .ThenBy(static row => row.RightTypeDef)];
        }

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
        if (featureRows.Count != 0)
        {
            sections.Add(new TrRmSection(0x0004, 0,
                checked((uint)featureRows.Count), featureBytes));
        }
        if (typeSpecRows.Count != 0)
        {
            sections.Add(new TrRmSection(0x0011, 0,
                checked((uint)typeSpecRows.Count), typeSpecBytes));
        }
        if (moduleRefRows.Length != 0)
        {
            sections.Add(new TrRmSection(0x0010, 0,
                checked((uint)moduleRefRows.Length), moduleRefBytes));
        }
        if (importRows.Length != 0)
        {
            sections.Add(new TrRmSection(0x001C, 0,
                checked((uint)importRows.Length), importBytes));
        }
        if (methodRows.Count != 0)
        {
            sections.Add(new TrRmSection(0x0016, 0,
                checked((uint)methodRows.Count), methodBytes));
            sections.Add(new TrRmSection(0x0017, 0,
                checked((uint)methodSignatureRows.Count),
                signatureBytes));
        }
        if (parameterRows.Count != 0)
        {
            sections.Add(new TrRmSection(0x0018, 0,
                checked((uint)parameterRows.Count), parameterBytes));
        }
        if (genericParamRows.Count != 0)
        {
            sections.Add(new TrRmSection(0x0019, 0,
                checked((uint)genericParamRows.Count), genericParamBytes));
        }
        if (genericConstraintRows.Count != 0)
        {
            sections.Add(new TrRmSection(0x001A, 0,
                checked((uint)genericConstraintRows.Count),
                constraintBytes));
        }
        if (contractRows.Count != 0)
        {
            sections.Add(new TrRmSection(0x001B, 0,
                checked((uint)contractRows.Count), contractBytes));
        }
        if (bodyRows.Count != 0)
        {
            sections.Add(new TrRmSection(0x0020, 0,
                checked((uint)bodyRows.Count),
                TrRmVariableTableWriter.EncodeBodies(bodyRows)));
        }
        if (frozenScalarValueTables.BlobCount != 0)
        {
            sections.Add(new TrRmSection(0x0003, 0,
                checked((uint)frozenScalarValueTables.BlobCount),
                frozenScalarValueTables.BlobSection));
        }
        if (frozenScalarValueTables.ConstantSourceCount != 0)
        {
            sections.Add(new TrRmSection(0x0030, 0,
                checked((uint)frozenScalarValueTables.ConstantSourceCount),
                frozenScalarValueTables.ConstantSourceSection));
            sections.Add(new TrRmSection(0x003F, 0,
                checked((uint)frozenScalarValueTables.FrozenValueNodeCount),
                frozenScalarValueTables.FrozenValueNodeSection));
        }
        if (parameterSurfaceRows.Count != 0)
        {
            sections.Add(new TrRmSection(0x0066, 0,
                checked((uint)parameterSurfaceRows.Count),
                parameterSurfaceBytes));
        }
        if (typeDefBindingEqualityRows.Count != 0)
        {
            sections.Add(new TrRmSection(0x0067, 0,
                checked((uint)typeDefBindingEqualityRows.Count),
                TrRmTypeDefBindingEqualityTableWriter.Encode(
                    typeDefBindingEqualityRows, modules, typeDefs,
                    IsSelectedClassTypeDefBindingEqualitySchemaIdentity(
                        standardSchemaIdentity)));
        }

        sections.Sort(static (left, right) =>
            left.Kind.CompareTo(right.Kind));
        var artifact = TrRmArtifactWriter.Encode(sections,
            schema, standardSchemaIdentity: standardSchemaIdentity);
        if (!schema.UsesClassParameterDefaults()
            && classMethodGenericFormalPlans.Count == 0)
        {
            _ = TrRmStructuralMetadataValidator
                .ValidateC1AndGetExecutionTables(
                    new TrRmArtifactIndex(artifact, schema,
                        standardSchemaIdentity:
                            standardSchemaIdentity));
        }

        sourceClassMethodGenericFormalPlans
            = [.. classMethodGenericFormalPlans];
        return artifact;
    }

    private static uint GetC1M1SourceScalarTypeSpecReference(
        PrimitiveType type)
    {
        if (!TryGetC1M1SourceScalarTypeSpecReference(type,
                out var typeSpecReference))
        {
            throw Unsupported(
                $"C1M1 source class field type '{type}' has no supported local scalar TypeSpec.");
        }

        return typeSpecReference;
    }

    private static bool TryGetC1M1SourceScalarTypeSpecReference(
        PrimitiveType type, out uint typeSpecReference)
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
            _ => -1,
        };
        if (typeCode < 0)
        {
            typeSpecReference = 0;
            return false;
        }

        typeSpecReference = 0xF200_0000u | checked((uint)typeCode);
        return true;
    }

    private static bool IsC1M1NoOpReadonlyScalarReferenceType(
        PrimitiveType type,
        ImmutableArray<CheckedReferenceTypeDefinition> referenceTypes)
    {
        if (!CheckedReferenceTypes.IsType(type)
            || !CheckedReferenceTypeContract.IsValidType(type,
                referenceTypes))
        {
            return false;
        }

        var reference = referenceTypes[CheckedReferenceTypes.Index(type)];
        return !reference.IsWritable
            && !reference.IsGarbageCollected
            && !reference.IsInitialization
            && !reference.IsOptional
            && !reference.IsOptionalValue
            && !reference.IsReferentWritable
            && !CheckedReferenceTypes.IsType(reference.ReferentType)
            && TryGetC1M1SourceScalarTypeSpecReference(
                reference.ReferentType, out _);
    }

    private static bool IsC1M1NoOpReadonlyScalarReferenceTypeSpec(
        TrRmTypeSpecTableReader typeSpecs, uint typeSpecReference)
    {
        var type = typeSpecs.GetTypeSpecRef(typeSpecReference);
        if (!type.IsRow || type.Form != 0x07 || type.Qualifiers != 0)
        {
            return false;
        }

        var row = typeSpecs.GetRow(type.Row);
        var referentTypeSpecRef
            = BinaryPrimitives.ReadUInt32LittleEndian(row.Slice(2));
        var referent = typeSpecs.GetTypeSpecRef(referentTypeSpecRef);
        return !referent.IsRow && referent.Form == 0x01
            && referent.Qualifiers == 0
            && TrRmConstantSourceValueValidator.TryGetBoundScalarType(
                referent.TypeCode, out _);
    }

    private static bool AreC1M1ClassCheckedReferenceTypesSupported(
        ImmutableArray<CheckedReferenceTypeDefinition> referenceTypes)
    {
        if (referenceTypes.IsDefaultOrEmpty)
        {
            return true;
        }

        if (!CheckedReferenceTypeContract.IsValidTable(referenceTypes))
        {
            return false;
        }

        for (var index = 0; index < referenceTypes.Length; index++)
        {
            if (!IsC1M1NoOpReadonlyScalarReferenceType(
                    CheckedReferenceTypes.Type(index), referenceTypes))
            {
                return false;
            }
        }

        return true;
    }

    private static bool HasC1M1Construction2ParameterDefaults(
        SyntaxNode declaration)
    {
        var parameterList = declaration.Children
            .OfType<SyntaxNode>()
            .SingleOrDefault(static child =>
                child.Kind == SyntaxKind.ParameterList);
        return parameterList is not null
            && ContainsTokenKind(parameterList, TokenKind.Equals);

        static bool ContainsTokenKind(SyntaxNode node, TokenKind kind)
        {
            foreach (var child in node.Children)
            {
                if (child is SyntaxTokenElement token
                    && token.Token.Kind == kind)
                {
                    return true;
                }

                if (child is SyntaxNode nested
                    && ContainsTokenKind(nested, kind))
                {
                    return true;
                }
            }

            return false;
        }
    }

    private static bool IsC1M1Construction2PartialPrimary(
        SyntaxNode declaration)
        => HasPartialConstructorModifier(declaration)
            && declaration.Children.OfType<SyntaxNode>().Any(
                static child => child.Kind == SyntaxKind.ExternalBody);

    private static bool IsC1M1Construction2PartialSecondary(
        SyntaxNode declaration)
        => HasPartialConstructorModifier(declaration)
            && declaration.Children.OfType<SyntaxNode>().All(
                static child => child.Kind != SyntaxKind.ExternalBody);

    private static bool HasC1M1Construction2PartialSecondary(
        ImmutableArray<BoundSourceClassPart> parts,
        BoundFunction function)
    {
        var matches = 0;
        foreach (var part in parts)
        {
            if (part.SourceInputOrdinal != function.SourceInputOrdinal)
            {
                continue;
            }

            foreach (var declaration in part.Declaration.Children
                .OfType<SyntaxNode>())
            {
                if (declaration.Span == function.Span
                    && IsC1M1Construction2PartialSecondary(declaration))
                {
                    matches++;
                }
            }
        }

        return matches == 1;
    }

    private static bool HasPartialConstructorModifier(
        SyntaxNode declaration)
        => declaration.Children.OfType<SyntaxTokenElement>().Any(
            static token => token.Token.Kind == TokenKind.PartialKeyword);

    private static ImmutableArray<BoundFieldAssignment>
        GetC1M1Construction2FieldAssignments(
            BoundProgram program, BoundFunction function, BoundClass @class,
            IReadOnlyDictionary<int, BoundSourceTypeDeclaration>
                sourceStructDeclarations,
            TrRmArtifactSchema schema, string schemaIdentity)
    {
        if (function.Variables.Length != function.ParameterCount
            || function.Body.Statements.IsDefault)
        {
            throw Unsupported(
                "The bounded Construction-2 initializer requires a checked body without untracked local storage.");
        }

        var assignments
            = ImmutableArray.CreateBuilder<BoundFieldAssignment>();
        var assignedFields = new HashSet<int>();
        var returned = false;
        Collect(function.Body);

        return assignments.ToImmutable();

        void Collect(BoundBlock block)
        {
            foreach (var statement in block.Statements)
            {
                if (returned)
                {
                    throw Unsupported(
                        "A Construction-2 initializer cannot continue after its unit return.");
                }

                if (statement is BoundBlock nested)
                {
                    Collect(nested);
                    continue;
                }

                if (statement is BoundReturn { Value: null })
                {
                    returned = true;
                    continue;
                }

                if (statement is not BoundFieldAssignment assignment
                    || assignment.Variable != 0
                    || !assignment.NestedFields.IsEmpty
                    || assignment.IsUnsafeAuthorized
                    || assignment.Field < 0
                    || assignment.Field >= @class.Fields.Length
                    || !assignedFields.Add(assignment.Field))
                {
                    throw Unsupported(
                        "The bounded Construction-2 initializer supports only direct field stores with each field initialized once.");
                }

                var fieldType = @class.Fields[assignment.Field].Type;
                if (assignment.Value is BoundRead read)
                {
                    if (read.Variable <= 0
                        || read.Variable >= function.ParameterCount
                        || read.Type != fieldType
                        || function.Variables[read.Variable].Type
                            != read.Type
                        || !IsC1M1Construction2SourceCopyType(read.Type,
                            program, schema, schemaIdentity,
                            sourceStructDeclarations))
                    {
                        throw Unsupported(
                            "A Construction-2 initializer parameter store must copy a matching checked value formal.");
                    }
                }
                else if (assignment.Value is BoundConstant constant)
                {
                    if (constant.Type != fieldType
                        || (!TryGetC1M1SourceScalarTypeSpecReference(
                                constant.Type, out _)
                            && (!schema.UsesC1EnumClosure()
                                || !BoundEnumTypes.IsValidType(
                                    constant.Type,
                                    program.EnumDefinitions.Length))))
                    {
                        throw Unsupported(
                            "A Construction-2 initializer constant store must match a supported scalar or nominal enum field type.");
                    }
                }
                else
                {
                    throw Unsupported(
                        "A Construction-2 initializer store must use a direct formal or scalar constant.");
                }

                assignments.Add(assignment);
            }
        }
    }

    private static bool IsC1M1Construction2SourceCopyType(
        PrimitiveType type, BoundProgram program, TrRmArtifactSchema schema,
        string schemaIdentity,
        IReadOnlyDictionary<int, BoundSourceTypeDeclaration>
            sourceStructDeclarations)
    {
        var pending = new Stack<(PrimitiveType Type, bool Exit)>();
        var structStates = new Dictionary<int, byte>();
        pending.Push((type, false));
        var work = 0;
        while (pending.Count != 0)
        {
            if (++work > SemanticVerifier.MaximumAnalysisVisits)
            {
                return false;
            }

            var (current, exit) = pending.Pop();
            if (TryGetC1M1SourceScalarTypeSpecReference(current, out _))
            {
                continue;
            }

            if (BoundEnumTypes.IsValidType(current,
                    program.EnumDefinitions.Length))
            {
                if (!schema.UsesC1EnumClosure())
                {
                    return false;
                }

                continue;
            }

            if (NativePointerTypes.IsType(current))
            {
                if ((uint)NativePointerTypes.Index(current)
                    >= (uint)program.NativePointerTypes.Length)
                {
                    return false;
                }

                continue;
            }

            if (NativeFunctionPointerTypes.IsType(current))
            {
                if ((uint)NativeFunctionPointerTypes.Index(current)
                    >= (uint)program.NativeFunctionPointerSignatures.Length)
                {
                    return false;
                }

                continue;
            }

            if (IsC1M1Construction2AliasableClassHandleType(current,
                    program, schemaIdentity))
            {
                continue;
            }

            var structureIndex = (int)current - 100;
            if (structureIndex < 0
                || (uint)structureIndex >= (uint)program.Structures.Length)
            {
                return false;
            }

            if (exit)
            {
                structStates[structureIndex] = 2;
                continue;
            }

            if (structStates.TryGetValue(structureIndex,
                    out var state))
            {
                if (state == 1)
                {
                    return false;
                }
                continue;
            }

            var structure = program.Structures[structureIndex];
            if (!schema.SupportsDeclaredCopyEvidence())
            {
                return false;
            }

            if (structure.StandardTypeOrigin is not null)
            {
                if (!schema.UsesConstructionInitializerCopyEvidence()
                    || structure.DeclaredCopyEvidence != true
                    || structure.IsUnique
                    || structure.CleanupMethod is not null
                    || !structure.StandardLayoutOnly
                    || structure.StandardTypeOrigin.Provider is null
                    || !StringComparer.Ordinal.Equals(
                        structure.StandardTypeOrigin.Provider.SchemaIdentity,
                        schemaIdentity)
                    || !GetForeignInvocationSchema(
                        structure.StandardTypeOrigin.Provider.SchemaIdentity)
                        .UsesConstructionInitializerCopyEvidence())
                {
                    return false;
                }

                continue;
            }

            if (!sourceStructDeclarations.TryGetValue(structureIndex,
                    out var declaration)
                || !IsC1M1SourceStructLayoutSupported(structure,
                    declaration))
            {
                return false;
            }

            structStates.Add(structureIndex, 1);
            pending.Push((current, true));
            for (var fieldIndex = structure.Fields.Length - 1;
                fieldIndex >= 0; fieldIndex--)
            {
                var field = structure.Fields[fieldIndex];
                if (field is null || field.RequiresUnsafeCaller)
                {
                    return false;
                }

                pending.Push((field.Type, false));
            }
        }

        return true;
    }

    private static bool IsC1M1Construction2AliasableClassHandleType(
        PrimitiveType type, BoundProgram program, string schemaIdentity)
    {
        if (ManagedClassTypes.IsClass(type))
        {
            var classIndex = ManagedClassTypes.ClassIndex(type);
            return (uint)classIndex < (uint)program.Classes.Length
                && program.Classes[classIndex].IsUnique == false
                && program.Classes[classIndex].HasCleanup == false
                && program.Classes[classIndex].CleanupMethod is null;
        }

        if (!BoundGenericNominalTypeContract.IsNominalClassType(type,
                program.GenericNominalTypeInstances,
                program.GenericNominalTypeDefinitions))
        {
            return false;
        }

        var instanceIndex = BoundGenericNominalTypeTypes.Index(type);
        if ((uint)instanceIndex
                >= (uint)program.GenericNominalTypeInstances.Length
            || program.GenericNominalTypeInstances[instanceIndex] is not
                {
                    Arguments.IsEmpty: true,
                    Qualifiers: <= 1,
                    TypeSpecForm: 0 or 0x02 or 0x03,
                } instance)
        {
            return false;
        }

        foreach (var declaration in program.StandardClassDeclarations)
        {
            if (declaration.Declaration.Identity == instance.Definition
                && (declaration.Declaration.TypeDefFlags
                    & TrRmTypeDefTableReader.DeclaredUniqueFlag) == 0
                && StringComparer.Ordinal.Equals(
                    declaration.Declaration.Provider.SchemaIdentity,
                    schemaIdentity)
                && GetForeignInvocationSchema(
                    declaration.Declaration.Provider.SchemaIdentity)
                    .UsesConstructionInitializerCopyEvidence())
            {
                return true;
            }

            foreach (var expectation in declaration.Expectations)
            {
                var expected = expectation.ExpectedDeclaration;
                if (expected.Identity == instance.Definition
                    && (expected.TypeDefFlags
                        & TrRmTypeDefTableReader.DeclaredUniqueFlag) == 0
                    && StringComparer.Ordinal.Equals(
                        expected.Provider.SchemaIdentity, schemaIdentity)
                    && GetForeignInvocationSchema(
                        expected.Provider.SchemaIdentity)
                        .UsesConstructionInitializerCopyEvidence())
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static byte[] EncodeC1M1Construction2InitializerContract(
        ImmutableArray<BoundFieldAssignment> assignments,
        Dictionary<(int ClassIndex, int FieldIndex), int>
            fieldDefRowByClassAndIndex,
        int classIndex)
    {
        var payload = new byte[checked(8 + (assignments.Length * sizeof(uint)))];
        payload[0] = 1;
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(4),
            checked((uint)assignments.Length));
        for (var index = 0; index < assignments.Length; index++)
        {
            if (!fieldDefRowByClassAndIndex.TryGetValue(
                    (classIndex, assignments[index].Field),
                    out var fieldDefRow))
            {
                throw new InvalidDataException(
                    "A Construction-2 initializer field has no emitted FieldDef.");
            }

            BinaryPrimitives.WriteUInt32LittleEndian(
                payload.AsSpan(8 + (index * sizeof(uint))),
                0x1400_0000u | checked((uint)fieldDefRow));
        }

        return payload;
    }

    private static byte[] EncodeC1M1Construction2ProjectionContract(
        int initializerMethodDefRow)
    {
        var payload = new byte[8];
        payload[0] = 2;
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(4),
            checked((uint)initializerMethodDefRow));
        return payload;
    }

    private static byte[] EncodeC1M1Construction2InitializerBody(
        BoundProgram program, C1M1Construction2SourceInitializer initializer,
        Dictionary<(int ClassIndex, int FieldIndex), int>
            fieldDefRowByClassAndIndex,
        Func<PrimitiveType, uint> typeSpecReferenceFor,
        TrRmFrozenScalarValueBuilder frozenScalarValueBuilder,
        int contractCount, TrRmArtifactSchema schema)
    {
        var function = initializer.Function;
        var typeTokens = function.Variables
            .Select(variable => CreateStandardBodyTypeMapToken(
                typeSpecReferenceFor(variable.Type)))
            .Concat(initializer.FieldAssignments.Select(assignment =>
                CreateStandardBodyTypeMapToken(
                    typeSpecReferenceFor(assignment.Value.Type))))
            .ToArray();
        var maps = TrRmRegisterBodyMapBuilder.Create(typeTokens, []);
        var homeMap = new byte[checked(function.Variables.Length * 8)];
        for (var variableIndex = 0;
            variableIndex < function.Variables.Length; variableIndex++)
        {
            var home = homeMap.AsSpan(variableIndex * 8, 8);
            home[0] = variableIndex == 0 ? (byte)1 : (byte)2;
            BinaryPrimitives.WriteUInt16LittleEndian(home[2..],
                checked((ushort)maps.GetTypeMapIndex(
                    typeTokens[variableIndex])));
            if (variableIndex != 0)
            {
                BinaryPrimitives.WriteUInt16LittleEndian(home[4..],
                    checked((ushort)(variableIndex - 1)));
            }
        }

        var records = new List<ReadOnlyMemory<byte>> {
            new(new byte[] { 1, 0, 0, 0, 0, 0, 0, 0 }),
        };
        var code = new List<byte>();
        var constantRows = new List<TrRmFrozenScalarValueReference>();
        foreach (var assignment in initializer.FieldAssignments)
        {
            if (!fieldDefRowByClassAndIndex.TryGetValue((
                    initializer.ClassIndex, assignment.Field),
                    out var fieldDefRow))
            {
                throw new InvalidDataException(
                    "A Construction-2 initializer Body cannot bind its checked field assignment.");
            }

            var fieldTypeToken = CreateStandardBodyTypeMapToken(
                typeSpecReferenceFor(assignment.Value.Type));
            var selectionRecord = records.Count;
            records.Add(TrRmRegisterBodyRecordWriter.EncodePlaceSelection(
                targetKind: 2, accessKind: 3, destinationKind: 1,
                baseKind: 2, destinationIndex: 0, baseIndex: 0,
                indexKind: 0, targetRefKind: 1, indexIndex: 0,
                targetRef: checked((uint)fieldDefRow),
                referentTypeMapIndex: checked((ushort)
                    maps.GetTypeMapIndex(fieldTypeToken)),
                schema));
            AppendRecordReference(0xD8, selectionRecord);

            byte sourceKind;
            ushort sourceIndex;
            if (assignment.Value is BoundRead read)
            {
                sourceKind = 2;
                sourceIndex = checked((ushort)read.Variable);
            }
            else if (assignment.Value is BoundConstant constant)
            {
                sourceKind = 1;
                sourceIndex = 1;
                var constantIndex = checked((ushort)
                    GetConstantMapIndex(constant));
                code.Add(0xD4);
                code.Add(1);
                code.Add((byte)constantIndex);
                code.Add((byte)(constantIndex >> 8));
            }
            else
            {
                throw new InvalidDataException(
                    "A Construction-2 initializer Body contains an unsupported field value.");
            }

            var storeRecord = records.Count;
            records.Add(TrRmRegisterBodyRecordWriter.EncodeSemanticStore(
                placeKind: 1, sourceKind: sourceKind, transition: 1,
                sourceUse: 1, placeIndex: 0, sourceIndex: sourceIndex,
                schema: schema));
            AppendRecordReference(0xD9, storeRecord);
        }

        code.Add(0x18);
        code.Add(0);
        var codeBytes = code.ToArray();
        var constantMap = new byte[checked(constantRows.Count * 8)];
        for (var index = 0; index < constantRows.Count; index++)
        {
            var offset = index * 8;
            BinaryPrimitives.WriteUInt32LittleEndian(
                constantMap.AsSpan(offset), constantRows[index].TypeSpecRef);
            BinaryPrimitives.WriteUInt32LittleEndian(
                constantMap.AsSpan(offset + sizeof(uint)),
                constantRows[index].ConstantSourceRow);
        }

        return TrRmRegisterBodyWriter.Encode(homeMap, maps.TypeMap,
            default, default, constantMap,
            [new TrRmRegisterBodyBlock(0,
                checked((uint)codeBytes.Length), 0)],
            records, codeBytes, contractCount: contractCount,
            schema: schema,
            generalPredicateGraphsInitializationReferenceBodies
                : schema.UsesGeneralPredicateGraphs());

        int GetConstantMapIndex(BoundConstant constant)
        {
            var typeSpecReference
                = typeSpecReferenceFor(constant.Type);
            TrRmFrozenScalarValueReference frozenValue;
            if (BoundEnumTypes.IsType(constant.Type))
            {
                if (!schema.UsesC1EnumClosure()
                    || !BoundEnumTypes.IsValidType(constant.Type,
                        program.EnumDefinitions.Length))
                {
                    throw Unsupported(
                        "A Construction-2 initializer enum constant has no checked nominal definition.");
                }

                var enumDefinition = program.EnumDefinitions[
                    BoundEnumTypes.Index(constant.Type)];
                frozenValue
                    = frozenScalarValueBuilder.AddC1EnumClosureMember(
                        constant, typeSpecReference,
                        enumDefinition.UnderlyingType, schema);
            }
            else
            {
                frozenValue = frozenScalarValueBuilder.Add(constant);
            }

            if (frozenValue.TypeSpecRef
                != typeSpecReference)
            {
                throw Unsupported(
                    "A Construction-2 initializer ConstantMap requires the exact field TypeSpecRef.");
            }

            var constantIndex = constantRows.IndexOf(frozenValue);
            if (constantIndex < 0)
            {
                if (constantRows.Count > ushort.MaxValue)
                {
                    throw Unsupported(
                        "A Construction-2 initializer Body exceeds the ConstantMap index limit.");
                }

                constantIndex = constantRows.Count;
                constantRows.Add(frozenValue);
            }

            return constantIndex;
        }

        void AppendRecordReference(byte opcode, int recordIndex)
        {
            code.Add(opcode);
            code.Add(0);
            var index = checked((ushort)recordIndex);
            code.Add((byte)index);
            code.Add((byte)(index >> 8));
        }
    }

    private static byte[] EncodeC1M1Construction2ProjectionBody(
        C1M1Construction2SourceInitializer initializer,
        Func<PrimitiveType, uint> typeSpecReferenceFor,
        int contractCount, TrRmArtifactSchema schema)
    {
        var function = initializer.Function;
        var typeTokens = new uint[function.ParameterCount];
        typeTokens[0] = CreateStandardBodyTypeMapToken(
            initializer.ClassTypeSpecReference);
        for (var parameterIndex = 1;
            parameterIndex < function.ParameterCount; parameterIndex++)
        {
            typeTokens[parameterIndex]
                = CreateStandardBodyTypeMapToken(
                    typeSpecReferenceFor(
                        function.Variables[parameterIndex].Type));
        }

        var methodMapToken = 0x1600_0000u
            | checked((uint)initializer.InitializerMethodDefRow);
        var maps = TrRmRegisterBodyMapBuilder.Create(typeTokens,
            [methodMapToken]);
        var homeMap = new byte[checked(
            (function.ParameterCount + 1) * 8)];
        homeMap[0] = 4;
        BinaryPrimitives.WriteUInt16LittleEndian(homeMap.AsSpan(2),
            checked((ushort)maps.GetTypeMapIndex(typeTokens[0])));
        homeMap[8] = 3;
        BinaryPrimitives.WriteUInt16LittleEndian(homeMap.AsSpan(10),
            checked((ushort)maps.GetTypeMapIndex(typeTokens[0])));
        var inputs = new List<TrRmRegisterBodyCallInput>(
            function.ParameterCount) {
            new(2, 5, 0),
        };
        for (var parameterIndex = 1;
            parameterIndex < function.ParameterCount; parameterIndex++)
        {
            var homeIndex = parameterIndex + 1;
            var home = homeMap.AsSpan(homeIndex * 8, 8);
            home[0] = 2;
            BinaryPrimitives.WriteUInt16LittleEndian(home[2..],
                checked((ushort)maps.GetTypeMapIndex(
                    typeTokens[parameterIndex])));
            BinaryPrimitives.WriteUInt16LittleEndian(home[4..],
                checked((ushort)(parameterIndex - 1)));
            if (function.Variables[parameterIndex].Type
                == PrimitiveType.Void)
            {
                inputs.Add(new TrRmRegisterBodyCallInput(0, 0, 0));
                continue;
            }

            inputs.Add(new TrRmRegisterBodyCallInput(2, 1,
                checked((ushort)homeIndex)));
        }

        var records = new ReadOnlyMemory<byte>[] {
            new(new byte[] { 1, 0, 0, 0, 0, 0, 0, 0 }),
            TrRmRegisterBodyRecordWriter.EncodeObjectConstruction(1, 0,
                0, 0, schema),
            TrRmRegisterBodyRecordWriter.EncodeCallSite(1, 0, 1, 0,
                inputs, [], schema: schema),
            TrRmRegisterBodyRecordWriter.EncodeObjectConstruction(2, 2,
                0, 1, schema),
        };
        var code = new byte[] {
            0xED, 0, 1, 0,
            0xE0, 0, 2, 0,
            0xED, 0, 3, 0,
            0xB8, 1,
        };
        return TrRmRegisterBodyWriter.Encode(homeMap, maps.TypeMap,
            maps.MethodMap, default, default,
            [new TrRmRegisterBodyBlock(0,
                checked((uint)code.Length), 0)],
            records, code, contractCount: contractCount,
            schema: schema,
            generalPredicateGraphsInitializationReferenceBodies
                : schema.UsesGeneralPredicateGraphs());
    }

    private static byte[] EncodeC1M1Construction2ConditionalReturnBody(
        BoundFunction function,
        C1M1Construction2ConditionalReturn conditionalReturn,
        TrRmFrozenScalarValueBuilder frozenScalarValueBuilder,
        Func<PrimitiveType, uint> typeSpecReferenceFor,
        int contractCount, TrRmArtifactSchema schema)
    {
        var typeTokens = new uint[function.ParameterCount];
        var homeMap = new byte[checked(function.ParameterCount * 8)];
        for (var parameter = 0;
            parameter < function.ParameterCount; parameter++)
        {
            var typeToken = CreateStandardBodyTypeMapToken(
                typeSpecReferenceFor(function.Variables[parameter].Type));
            typeTokens[parameter] = typeToken;
        }

        var maps = TrRmRegisterBodyMapBuilder.Create(typeTokens, []);
        for (var parameter = 0;
            parameter < function.ParameterCount; parameter++)
        {
            var offset = parameter * 8;
            var isReceiver = !function.IsStatic && parameter == 0;
            homeMap[offset] = isReceiver ? (byte)1 : (byte)2;
            BinaryPrimitives.WriteUInt16LittleEndian(
                homeMap.AsSpan(offset + 2, 2),
                checked((ushort)maps.GetTypeMapIndex(
                    typeTokens[parameter])));
            BinaryPrimitives.WriteUInt16LittleEndian(
                homeMap.AsSpan(offset + 4, 2),
                isReceiver ? (ushort)0
                    : checked((ushort)(parameter
                        - (function.IsStatic ? 0 : 1)));
        }

        var constantRows = new List<TrRmFrozenScalarValueReference>();
        var codeBlocks = new List<byte[]> { [] };
        var records = new List<ReadOnlyMemory<byte>> {
            new byte[] { 1, 0, 0, 0, 0, 0, 0, 0 },
        };
        var pending = new Stack<(int Block, BoundStatement Statement)>();
        pending.Push((0, conditionalReturn.Root));
        var visits = 0;
        while (pending.Count != 0)
        {
            if (++visits > SemanticVerifier.MaximumAnalysisVisits)
            {
                throw Unsupported(
                    "A Construction-2 conditional Body exceeds the control-flow analysis limit.");
            }

            var (blockIndex, pendingStatement) = pending.Pop();
            var statement = pendingStatement;
            while (statement is BoundBlock block)
            {
                if (++visits > SemanticVerifier.MaximumAnalysisVisits
                    || !block.RetiredManagedLocals.IsEmpty
                    || block.Statements.Length != 1)
                {
                    throw new InvalidDataException(
                        "A checked Construction-2 conditional branch has unsupported block structure.");
                }

                statement = block.Statements[0];
            }

            if (statement is BoundIf branch)
            {
                if (branch.Else is null)
                {
                    throw new InvalidDataException(
                        "A checked Construction-2 conditional branch lost its false arm.");
                }

                var elseBranch = branch.Else;
                var trueBlock = AddBlock();
                var falseBlock = AddBlock();
                if (records.Count > ushort.MaxValue)
                {
                    throw Unsupported(
                        "A Construction-2 conditional Body exceeds the ConditionalEdge record limit.");
                }

                var edgeRecordIndex = checked((ushort)records.Count);
                records.Add(new(
                    TrRmRegisterBodyRecordWriter.EncodeConditionalEdge(
                        checked((ushort)trueBlock),
                        checked((ushort)falseBlock))));
                codeBlocks[blockIndex] = EncodeCondition(
                    branch.Condition, edgeRecordIndex);
                pending.Push((falseBlock, elseBranch));
                pending.Push((trueBlock, branch.Then));
            }
            else if (statement is BoundReturn { Value: not null } returned)
            {
                codeBlocks[blockIndex]
                    = EncodeReturnBlock(returned.Value);
            }
            else
            {
                throw new InvalidDataException(
                    "A checked Construction-2 conditional branch has an unsupported statement.");
            }
        }

        var blocks = new List<TrRmRegisterBodyBlock>(codeBlocks.Count);
        var code = new List<byte>();
        foreach (var blockCode in codeBlocks)
        {
            blocks.Add(new TrRmRegisterBodyBlock(
                checked((uint)code.Count),
                checked((uint)blockCode.Length), 0));
            code.AddRange(blockCode);
        }

        var constantMap = new byte[checked(
            constantRows.Count * sizeof(uint) * 2)];
        for (var index = 0; index < constantRows.Count; index++)
        {
            var constant = constantRows[index];
            var offset = index * sizeof(uint) * 2;
            BinaryPrimitives.WriteUInt32LittleEndian(
                constantMap.AsSpan(offset), constant.TypeSpecRef);
            BinaryPrimitives.WriteUInt32LittleEndian(
                constantMap.AsSpan(offset + sizeof(uint)),
                constant.ConstantSourceRow);
        }

        return TrRmRegisterBodyWriter.Encode(homeMap, maps.TypeMap,
            default, default, constantMap, blocks, records, code.ToArray(),
            contractCount: contractCount, schema: schema,
            generalPredicateGraphsInitializationReferenceBodies
                : schema.UsesGeneralPredicateGraphs());

        int AddBlock()
        {
            if (codeBlocks.Count > ushort.MaxValue)
            {
                throw Unsupported(
                    "A Construction-2 conditional Body exceeds the Block index limit.");
            }

            var block = codeBlocks.Count;
            codeBlocks.Add([]);
            return block;
        }

        byte[] EncodeCondition(BoundExpression condition,
            ushort edgeIndex)
        {
            var instructions = new List<byte>(18);
            if (condition is BoundRead read)
            {
                AppendHomeLoad(instructions, 0, read.Variable);
            }
            else if (condition is BoundBinary {
                         Left: BoundRead left,
                         Right: BoundRead right,
                     } comparison
                     && comparison.Type == PrimitiveType.Bool)
            {
                AppendHomeLoad(instructions, 0, left.Variable);
                AppendHomeLoad(instructions, 1, right.Variable);
                const byte destinationRegister = 0;
                const byte leftRegister = 0;
                const byte rightRegister = 1;
                var payload = (ushort)(destinationRegister
                    | (0x0F << 4) | (leftRegister << 8)
                    | (rightRegister << 12));
                if (left.Type == PrimitiveType.Bool
                    && right.Type == PrimitiveType.Bool
                    && comparison.Operator is (TokenKind.EqualsEquals
                        or TokenKind.BangEquals))
                {
                    instructions.Add(0xC2);
                    instructions.Add(comparison.Operator
                        == TokenKind.EqualsEquals
                            ? (byte)0x01 : (byte)0x02);
                    instructions.Add((byte)payload);
                    instructions.Add((byte)(payload >> 8));
                }
                else if (left.Type == right.Type
                    && IsC1M1Construction2BodyNumericType(left.Type)
                    && IsC1M1Construction2IntegerComparisonOperator(
                        comparison.Operator))
                {
                    instructions.Add(0xC2);
                    instructions.Add(0);
                    instructions.Add((byte)payload);
                    instructions.Add((byte)(payload >> 8));
                    var predicatePayload = (ushort)(
                        destinationRegister
                        | (destinationRegister << 4) | (0x0F << 8)
                        | (0x0F << 12));
                    instructions.Add(0xC3);
                    instructions.Add(
                        GetC1M1Construction2IntegerComparisonPredicate(
                            comparison.Operator));
                    instructions.Add((byte)predicatePayload);
                    instructions.Add((byte)(predicatePayload >> 8));
                }
                else
                {
                    throw new InvalidDataException(
                        "A Construction-2 conditional Body has an unsupported comparison.");
                }
            }
            else
            {
                throw new InvalidDataException(
                    "A Construction-2 conditional Body lost its checked Boolean condition.");
            }

            instructions.Add(0xE4);
            instructions.Add(0);
            instructions.Add((byte)edgeIndex);
            instructions.Add((byte)(edgeIndex >> 8));
            return instructions.ToArray();
        }

        static void AppendHomeLoad(List<byte> instructions,
            byte destination, int homeIndex)
        {
            var home = checked((ushort)homeIndex);
            instructions.Add(0xD5);
            instructions.Add(destination);
            instructions.Add((byte)home);
            instructions.Add((byte)(home >> 8));
        }

        byte[] EncodeReturnBlock(BoundExpression value)
        {
            if (value is BoundRead read)
            {
                var home = checked((ushort)read.Variable);
                return [
                    0xD5, 0, (byte)home, (byte)(home >> 8), 0x17, 0,
                ];
            }

            if (value is BoundConstant constant)
            {
                var index = checked((ushort)GetConstantMapIndex(constant));
                return [
                    0xD4, 0, (byte)index, (byte)(index >> 8), 0x17, 0,
                ];
            }

            if (value is BoundUnary unary
                && unary.Operand is BoundRead operand
                && unary.Type == function.ReturnType
                && IsC1M1Construction2BodyNumericType(unary.Type)
                && operand.Type == unary.Type)
            {
                var instructions = new List<byte>(10);
                AppendHomeLoad(instructions, 0, operand.Variable);
                byte opcode;
                byte control;
                if (PrimitiveTypeFacts.IsFloating(unary.Type))
                {
                    if (unary.Operator != TokenKind.Minus)
                    {
                        throw new InvalidDataException(
                            "A Construction-2 conditional Body has an unsupported floating unary operation.");
                    }

                    opcode = 0xC0;
                    control = 0x25;
                }
                else
                {
                    (opcode, control)
                        = (unary.Operator, unary.OverflowBehavior) switch
                        {
                            (TokenKind.Tilde, _)
                                => ((byte)0xC1, (byte)0x04),
                            (TokenKind.Minus,
                                BoundIntegerOverflowBehavior.Checked)
                                => ((byte)0xC0, (byte)0x04),
                            (TokenKind.Minus,
                                BoundIntegerOverflowBehavior.Wrapping)
                                => ((byte)0xC0, (byte)0x08),
                            _ => throw new InvalidDataException(
                                "A Construction-2 conditional Body has an unsupported integer unary operation."),
                        };
                }

                instructions.Add(opcode);
                instructions.Add(control);
                instructions.Add(0xF0);
                instructions.Add(0xF0);
                instructions.Add(0x17);
                instructions.Add(0);
                return instructions.ToArray();
            }

            if (value is BoundBinary binary
                && binary.Left.Type == binary.Type
                && binary.Right.Type == binary.Type
                && IsC1M1Construction2BodyNumericType(binary.Type))
            {
                var instructions = new List<byte>(14);
                AppendNumericOperandLoad(instructions, 0, binary.Left);
                AppendNumericOperandLoad(instructions, 1, binary.Right);
                const byte destinationRegister = 0;
                const byte leftRegister = 0;
                const byte rightRegister = 1;
                var payload = (ushort)(destinationRegister
                    | (0x0F << 4) | (leftRegister << 8)
                    | (rightRegister << 12));
                if (PrimitiveTypeFacts.IsFloating(binary.Type))
                {
                    instructions.Add(0xC0);
                    instructions.Add(
                        GetC1M1Construction2FloatingBinaryControl(
                            binary.Operator));
                }
                else
                {
                    var (opcode, control)
                        = GetC1M1Construction2IntegerBinaryEncoding(
                            binary.Operator, binary.OverflowBehavior);
                    instructions.Add(opcode);
                    instructions.Add(control);
                }
                instructions.Add((byte)payload);
                instructions.Add((byte)(payload >> 8));
                instructions.Add(0x17);
                instructions.Add(0);
                return instructions.ToArray();
            }

            throw new InvalidDataException(
                "A Construction-2 conditional Body lost its checked scalar return.");
        }

        int GetConstantMapIndex(BoundConstant constant)
        {
            var frozenValue = frozenScalarValueBuilder.Add(constant);
            if (frozenValue.TypeSpecRef
                != typeSpecReferenceFor(constant.Type))
            {
                throw Unsupported(
                    "A Construction-2 conditional Body ConstantMap requires the exact operand TypeSpecRef.");
            }

            var constantIndex = constantRows.IndexOf(frozenValue);
            if (constantIndex < 0)
            {
                constantIndex = constantRows.Count;
                constantRows.Add(frozenValue);
            }
            return constantIndex;
        }

        void AppendNumericOperandLoad(List<byte> instructions,
            byte destination, BoundExpression operand)
        {
            if (operand is BoundRead read)
            {
                AppendHomeLoad(instructions, destination, read.Variable);
                return;
            }

            if (operand is BoundConstant constant)
            {
                var index = checked((ushort)GetConstantMapIndex(constant));
                instructions.Add(0xD4);
                instructions.Add(destination);
                instructions.Add((byte)index);
                instructions.Add((byte)(index >> 8));
                return;
            }

            throw new InvalidDataException(
                "A Construction-2 conditional binary Body lost a checked numeric operand.");
        }
    }

    private static byte[] EncodeC1M1Construction2StaticMethodBody(
        BoundFunction function,
        Func<PrimitiveType, uint> typeSpecReferenceFor,
        TrRmFrozenScalarValueBuilder frozenScalarValueBuilder,
        int contractCount, TrRmArtifactSchema schema)
    {
        if (!function.IsStatic
            || function.ParameterCount != function.Variables.Length
            || !function.Body.RetiredManagedLocals.IsEmpty)
        {
            throw Unsupported(
                "A Construction-2 static register Body requires only checked value-formal Homes.");
        }

        BoundExpression? returnValue = null;
        if (function.Body.Statements.IsEmpty)
        {
            if (function.ReturnType != PrimitiveType.Void)
            {
                throw Unsupported(
                    "A Construction-2 static non-unit Body requires an explicit return value.");
            }
        }
        else if (function.Body.Statements is [
                     BoundReturn statementReturn
                 ])
        {
            returnValue = statementReturn.Value;
        }
        else
        {
            throw Unsupported(
                "The bounded Construction-2 static register Body supports one direct return.");
        }

        for (var parameter = 0;
            parameter < function.ParameterCount; parameter++)
        {
            var variable = function.Variables[parameter];
            if (variable.IsOut || variable.IsInit
                || variable.IsImplicitRef
                || variable.IsInitializationLocal)
            {
                throw Unsupported(
                    "A Construction-2 static register Body requires by-value formal Homes.");
            }
        }

        uint[] typeTokens = [];
        byte[] homeMap = [];
        byte[] constantMap = [];
        byte[] code;
        (int LeftHome, int RightHome) SetScalarBinaryHomes(
            PrimitiveType type, int leftVariable, int rightVariable)
        {
            var typeToken = CreateStandardBodyTypeMapToken(
                typeSpecReferenceFor(type));
            var homeCount = leftVariable == rightVariable ? 1 : 2;
            typeTokens = new uint[homeCount];
            homeMap = new byte[homeCount * 8];
            for (var home = 0; home < homeCount; home++)
            {
                var variable = home == 0 ? leftVariable : rightVariable;
                typeTokens[home] = typeToken;
                homeMap[home * 8] = 2;
                BinaryPrimitives.WriteUInt16LittleEndian(
                    homeMap.AsSpan(home * 8 + 4, 2),
                    checked((ushort)variable));
            }

            return (0, homeCount == 1 ? 0 : 1);
        }

        void SetIntegerFormalHome(int variable, PrimitiveType type)
        {
            typeTokens = [
                CreateStandardBodyTypeMapToken(
                    typeSpecReferenceFor(type)),
            ];
            homeMap = new byte[8];
            homeMap[0] = 2;
            BinaryPrimitives.WriteUInt16LittleEndian(
                homeMap.AsSpan(4, 2), checked((ushort)variable));
        }

        if (function.ReturnType == PrimitiveType.Void)
        {
            if (returnValue is not null
                && (returnValue.Type != PrimitiveType.Void
                    || returnValue is not (BoundRead or BoundConstant)))
            {
                throw Unsupported(
                    "A Construction-2 static unit Body can only return unit without dropping an expression effect.");
            }

            code = [0x18, 0];
        }
        else if (returnValue is BoundRead read)
        {
            if (read.Variable < 0
                || read.Variable >= function.ParameterCount
                || read.Type != function.ReturnType
                || function.Variables[read.Variable].Type
                    != function.ReturnType)
            {
                throw Unsupported(
                    "A Construction-2 static Body can return only an exact value formal.");
            }

            var typeToken = CreateStandardBodyTypeMapToken(
                typeSpecReferenceFor(read.Type));
            typeTokens = [typeToken];
            homeMap = new byte[8];
            homeMap[0] = 2;
            BinaryPrimitives.WriteUInt16LittleEndian(
                homeMap.AsSpan(4), checked((ushort)read.Variable));
            code = [
                0xD5, 0,
                0, 0,
                0x17, 0,
            ];
        }
        else if (returnValue is BoundUnary unary
            && function.ReturnType == unary.Type
            && IsC1M1Construction2BodyIntegerType(unary.Type)
            && unary.Operand is BoundRead operand
            && operand.Type == unary.Type
            && (uint)operand.Variable < (uint)function.ParameterCount
            && function.Variables[operand.Variable].Type
                == unary.Type
            && unary.Operator is TokenKind.Minus or TokenKind.Tilde)
        {
            SetIntegerFormalHome(operand.Variable, unary.Type);
            var (opcode, operation) = (unary.Operator,
                unary.OverflowBehavior) switch
            {
                (TokenKind.Tilde, _) => ((byte)0xC1, (byte)0x04),
                (TokenKind.Minus,
                    BoundIntegerOverflowBehavior.Checked)
                    => ((byte)0xC0, (byte)0x04),
                (TokenKind.Minus,
                    BoundIntegerOverflowBehavior.Wrapping)
                    => ((byte)0xC0, (byte)0x08),
                _ => throw Unsupported(
                    "The selected static register Body does not encode this unary integer policy."),
            };
            code = [
                0xD5, 0, 0, 0,
                opcode, operation, 0xF0, 0xF0,
                0x17, 0,
            ];
        }
        else if (returnValue is BoundUnary floatingUnary
            && function.ReturnType == floatingUnary.Type
            && PrimitiveTypeFacts.IsFloating(floatingUnary.Type)
            && floatingUnary.Operator == TokenKind.Minus
            && floatingUnary.Operand is BoundRead floatingOperand
            && floatingOperand.Type == floatingUnary.Type
            && (uint)floatingOperand.Variable
                < (uint)function.ParameterCount
            && function.Variables[floatingOperand.Variable].Type
                == floatingUnary.Type)
        {
            SetIntegerFormalHome(floatingOperand.Variable,
                floatingUnary.Type);
            code = [
                0xD5, 0, 0, 0,
                0xC0, 0x25, 0xF0, 0xF0,
                0x17, 0,
            ];
        }
        else if (returnValue is BoundBinary binary
            && function.ReturnType == binary.Type
            && IsC1M1Construction2BodyIntegerType(binary.Type)
            && binary.Left is BoundRead left
            && binary.Right is BoundRead right
            && left.Type == binary.Type
            && right.Type == binary.Type
            && (uint)left.Variable < (uint)function.ParameterCount
            && (uint)right.Variable < (uint)function.ParameterCount
            && function.Variables[left.Variable].Type
                == binary.Type
            && function.Variables[right.Variable].Type
                == binary.Type
            && binary.Operator is TokenKind.Ampersand or TokenKind.Pipe
                or TokenKind.Caret)
        {
            var (leftHome, rightHome)
                = SetScalarBinaryHomes(binary.Type,
                    left.Variable, right.Variable);
            var operation = binary.Operator switch
            {
                TokenKind.Ampersand => (byte)0x01,
                TokenKind.Pipe => (byte)0x02,
                TokenKind.Caret => (byte)0x03,
                _ => throw new InvalidDataException(
                    "A static Body bitwise operation lost its assigned control."),
            };
            var instructions = new List<byte>(14);
            instructions.Add(0xD5);
            instructions.Add(0);
            instructions.Add(checked((byte)leftHome));
            instructions.Add(0);
            instructions.Add(0xD5);
            instructions.Add(1);
            instructions.Add(checked((byte)rightHome));
            instructions.Add(0);
            const byte destinationRegister = 0;
            const byte leftRegister = 0;
            const byte rightRegister = 1;
            var payload = (ushort)(destinationRegister | (0x0F << 4)
                | (leftRegister << 8) | (rightRegister << 12));
            instructions.Add(0xC1);
            instructions.Add(operation);
            instructions.Add((byte)payload);
            instructions.Add((byte)(payload >> 8));
            instructions.Add(0x17);
            instructions.Add(0);
            code = instructions.ToArray();
        }
        else if (returnValue is BoundBinary arithmetic
            && function.ReturnType == arithmetic.Type
            && IsC1M1Construction2BodyNumericType(arithmetic.Type)
            && arithmetic.Left is BoundRead arithmeticLeft
            && arithmetic.Right is BoundRead arithmeticRight
            && arithmeticLeft.Type == arithmetic.Type
            && arithmeticRight.Type == arithmetic.Type
            && (uint)arithmeticLeft.Variable
                < (uint)function.ParameterCount
            && (uint)arithmeticRight.Variable
                < (uint)function.ParameterCount
            && function.Variables[arithmeticLeft.Variable].Type
                == arithmetic.Type
            && function.Variables[arithmeticRight.Variable].Type
                == arithmetic.Type
            && arithmetic.Operator is TokenKind.Plus or TokenKind.Minus
                or TokenKind.Star or TokenKind.Slash or TokenKind.Percent)
        {
            var (leftHome, rightHome) = SetScalarBinaryHomes(
                arithmetic.Type, arithmeticLeft.Variable,
                arithmeticRight.Variable);
            var operation = PrimitiveTypeFacts.IsFloating(arithmetic.Type)
                ? GetC1M1Construction2FloatingBinaryControl(
                    arithmetic.Operator)
                : (arithmetic.Operator, arithmetic.OverflowBehavior) switch
                {
                    (TokenKind.Plus,
                        BoundIntegerOverflowBehavior.Checked) => (byte)0x01,
                    (TokenKind.Minus,
                        BoundIntegerOverflowBehavior.Checked) => (byte)0x02,
                    (TokenKind.Star,
                        BoundIntegerOverflowBehavior.Checked) => (byte)0x03,
                    (TokenKind.Slash,
                        BoundIntegerOverflowBehavior.Checked) => (byte)0x0C,
                    (TokenKind.Percent,
                        BoundIntegerOverflowBehavior.Checked) => (byte)0x0D,
                    (TokenKind.Plus,
                        BoundIntegerOverflowBehavior.Wrapping) => (byte)0x05,
                    (TokenKind.Minus,
                        BoundIntegerOverflowBehavior.Wrapping) => (byte)0x06,
                    (TokenKind.Star,
                        BoundIntegerOverflowBehavior.Wrapping) => (byte)0x07,
                    _ => throw Unsupported(
                        "The selected static register Body does not encode this integer overflow policy."),
                };
            var instructions = new List<byte>(14);
            instructions.Add(0xD5);
            instructions.Add(0);
            instructions.Add(checked((byte)leftHome));
            instructions.Add(0);
            instructions.Add(0xD5);
            instructions.Add(1);
            instructions.Add(checked((byte)rightHome));
            instructions.Add(0);
            const byte destinationRegister = 0;
            const byte leftRegister = 0;
            const byte rightRegister = 1;
            var payload = (ushort)(destinationRegister | (0x0F << 4)
                | (leftRegister << 8) | (rightRegister << 12));
            instructions.Add(0xC0);
            instructions.Add(operation);
            instructions.Add((byte)payload);
            instructions.Add((byte)(payload >> 8));
            instructions.Add(0x17);
            instructions.Add(0);
            code = instructions.ToArray();
        }
        else if (returnValue is BoundBinary comparison
            && function.ReturnType == PrimitiveType.Bool
            && comparison.Type == PrimitiveType.Bool
            && comparison.Left is BoundRead comparisonLeft
            && comparison.Right is BoundRead comparisonRight
            && IsC1M1Construction2BodyNumericType(
                comparisonLeft.Type)
            && comparisonRight.Type == comparisonLeft.Type
            && (uint)comparisonLeft.Variable
                < (uint)function.ParameterCount
            && (uint)comparisonRight.Variable
                < (uint)function.ParameterCount
            && function.Variables[comparisonLeft.Variable].Type
                == comparisonLeft.Type
            && function.Variables[comparisonRight.Variable].Type
                == comparisonRight.Type
            && IsC1M1Construction2IntegerComparisonOperator(
                comparison.Operator))
        {
            var (leftHome, rightHome) = SetScalarBinaryHomes(
                comparisonLeft.Type, comparisonLeft.Variable,
                comparisonRight.Variable);
            code = EncodeC1M1Construction2IntegerComparisonCode(
                leftHome, rightHome, comparison.Operator);
        }
        else if (returnValue is BoundBinary booleanComparison
            && function.ReturnType == PrimitiveType.Bool
            && booleanComparison.Type == PrimitiveType.Bool
            && booleanComparison.Left is BoundRead booleanLeft
            && booleanComparison.Right is BoundRead booleanRight
            && booleanLeft.Type == PrimitiveType.Bool
            && booleanRight.Type == PrimitiveType.Bool
            && (uint)booleanLeft.Variable
                < (uint)function.ParameterCount
            && (uint)booleanRight.Variable
                < (uint)function.ParameterCount
            && function.Variables[booleanLeft.Variable].Type
                == PrimitiveType.Bool
            && function.Variables[booleanRight.Variable].Type
                == PrimitiveType.Bool
            && booleanComparison.Operator is (TokenKind.EqualsEquals
                or TokenKind.BangEquals))
        {
            var (leftHome, rightHome) = SetScalarBinaryHomes(
                PrimitiveType.Bool, booleanLeft.Variable,
                booleanRight.Variable);
            code = EncodeC1M1Construction2BooleanEqualityCode(
                leftHome, rightHome, booleanComparison.Operator);
        }
        else if (returnValue is BoundBinary constantBinary
            && function.ReturnType == constantBinary.Type
            && IsC1M1Construction2BodyNumericType(constantBinary.Type)
            && (PrimitiveTypeFacts.IsFloating(constantBinary.Type)
                ? constantBinary.Operator is TokenKind.Plus
                    or TokenKind.Minus or TokenKind.Star or TokenKind.Slash
                    or TokenKind.Percent
                : constantBinary.Operator is TokenKind.Ampersand
                    or TokenKind.Pipe or TokenKind.Caret or TokenKind.Plus
                    or TokenKind.Minus or TokenKind.Star or TokenKind.Slash
                    or TokenKind.Percent))
        {
            var (formalRead, constant, constantOnLeft)
                = (constantBinary.Left, constantBinary.Right) switch
                {
                    (BoundRead read, BoundConstant value)
                        => (read, value, false),
                    (BoundConstant value, BoundRead read)
                        => (read, value, true),
                    _ => throw Unsupported(
                        "A Construction-2 static constant binary Body requires one direct numeric formal and one scalar constant."),
                };

            if (formalRead.Type != constantBinary.Type
                || (uint)formalRead.Variable
                    >= (uint)function.ParameterCount
                || function.Variables[formalRead.Variable].Type
                    != constantBinary.Type
                || constant.Type != constantBinary.Type
                || constant.RepresentationType != constantBinary.Type
                || !constant.Bits.IsCanonicalFor(constantBinary.Type))
            {
                throw Unsupported(
                    "A Construction-2 static constant binary Body requires exact canonical same-typed operands.");
            }

            SetIntegerFormalHome(formalRead.Variable, formalRead.Type);
            var frozenValue = frozenScalarValueBuilder.Add(constant);
            var expectedTypeSpecRef
                = typeSpecReferenceFor(formalRead.Type);
            if (frozenValue.TypeSpecRef != expectedTypeSpecRef)
            {
                throw Unsupported(
                    "A Construction-2 static constant binary Body requires the exact operand TypeSpecRef.");
            }

            constantMap = new byte[8];
            BinaryPrimitives.WriteUInt32LittleEndian(constantMap,
                frozenValue.TypeSpecRef);
            BinaryPrimitives.WriteUInt32LittleEndian(
                constantMap.AsSpan(4), frozenValue.ConstantSourceRow);
            var instructions = new List<byte>(14);
            if (constantOnLeft)
            {
                instructions.Add(0xD4);
                instructions.Add(0);
                instructions.Add(0);
                instructions.Add(0);
                instructions.Add(0xD5);
                instructions.Add(1);
                instructions.Add(0);
                instructions.Add(0);
            }
            else
            {
                instructions.Add(0xD5);
                instructions.Add(0);
                instructions.Add(0);
                instructions.Add(0);
                instructions.Add(0xD4);
                instructions.Add(1);
                instructions.Add(0);
                instructions.Add(0);
            }

            var (opcode, operation)
                = PrimitiveTypeFacts.IsFloating(constantBinary.Type)
                    ? ((byte)0xC0,
                        GetC1M1Construction2FloatingBinaryControl(
                            constantBinary.Operator))
                    : GetC1M1Construction2IntegerBinaryEncoding(
                        constantBinary.Operator,
                        constantBinary.OverflowBehavior);
            const byte destinationRegister = 0;
            const byte leftRegister = 0;
            const byte rightRegister = 1;
            var payload = (ushort)(destinationRegister | (0x0F << 4)
                | (leftRegister << 8) | (rightRegister << 12));
            instructions.Add(opcode);
            instructions.Add(operation);
            instructions.Add((byte)payload);
            instructions.Add((byte)(payload >> 8));
            instructions.Add(0x17);
            instructions.Add(0);
            code = instructions.ToArray();
        }
        else if (returnValue is BoundConstant constant)
        {
            if (constant.Type != function.ReturnType
                || constant.RepresentationType != function.ReturnType
                || !constant.Bits.IsCanonicalFor(function.ReturnType))
            {
                throw Unsupported(
                    "A Construction-2 static literal Body requires an exact canonical scalar constant.");
            }

            var frozenValue = frozenScalarValueBuilder.Add(constant);
            var expectedTypeSpecRef
                = typeSpecReferenceFor(function.ReturnType);
            if (frozenValue.TypeSpecRef != expectedTypeSpecRef)
            {
                throw Unsupported(
                    "A Construction-2 static Body ConstantMap requires the exact result TypeSpecRef.");
            }

            constantMap = new byte[8];
            BinaryPrimitives.WriteUInt32LittleEndian(constantMap,
                frozenValue.TypeSpecRef);
            BinaryPrimitives.WriteUInt32LittleEndian(
                constantMap.AsSpan(4), frozenValue.ConstantSourceRow);
            code = [
                0xD4, 0, 0, 0,
                0x17, 0,
            ];
        }
        else
        {
            throw Unsupported(
                "The bounded Construction-2 static register Body supports direct scalar return, numeric unary/binary/comparison, Boolean equality/inequality, or fixed-scalar returns.");
        }

        var maps = TrRmRegisterBodyMapBuilder.Create(typeTokens, []);
        if (homeMap.Length != 0)
        {
            if (typeTokens.Length != homeMap.Length / 8)
            {
                throw new InvalidDataException(
                    "A static register Body HomeMap must have one TypeMap token per Home.");
            }

            for (var home = 0; home < typeTokens.Length; home++)
            {
                BinaryPrimitives.WriteUInt16LittleEndian(
                    homeMap.AsSpan(home * 8 + 2, 2),
                    maps.GetTypeMapIndex(typeTokens[home]));
            }
        }

        var records = new ReadOnlyMemory<byte>[] {
            new(new byte[] { 1, 0, 0, 0, 0, 0, 0, 0 }),
        };
        return TrRmRegisterBodyWriter.Encode(homeMap, maps.TypeMap,
            default, default, constantMap,
            [new TrRmRegisterBodyBlock(0,
                checked((uint)code.Length), 0)],
            records, code, contractCount: contractCount,
            schema: schema,
            generalPredicateGraphsInitializationReferenceBodies
                : schema.UsesGeneralPredicateGraphs());
    }

    private static bool IsC1M1Construction2BodyIntegerType(
        PrimitiveType type)
        => PrimitiveTypeFacts.IsIntegerOrWide(type)
            && type is not (PrimitiveType.NativeInt
                or PrimitiveType.NativeUInt);

    private static bool IsC1M1Construction2BodyNumericType(
        PrimitiveType type)
        => IsC1M1Construction2BodyIntegerType(type)
            || PrimitiveTypeFacts.IsFloating(type);

    private static bool IsC1M1Construction2IntegerComparisonOperator(
        TokenKind operation)
        => operation is TokenKind.EqualsEquals or TokenKind.BangEquals
            or TokenKind.Less or TokenKind.LessEquals or TokenKind.Greater
            or TokenKind.GreaterEquals;

    private static byte GetC1M1Construction2IntegerComparisonPredicate(
        TokenKind operation)
        => operation switch
        {
            TokenKind.EqualsEquals => 2,
            TokenKind.BangEquals => 13,
            TokenKind.Less => 1,
            TokenKind.LessEquals => 3,
            TokenKind.Greater => 4,
            TokenKind.GreaterEquals => 6,
            _ => throw Unsupported(
                "The selected integer comparison has no registered predicate."),
        };

    private static (byte Opcode, byte Control)
        GetC1M1Construction2IntegerBinaryEncoding(TokenKind operation,
            BoundIntegerOverflowBehavior overflowBehavior)
        => (operation, overflowBehavior) switch
        {
            (TokenKind.Ampersand, _) => (0xC1, 0x01),
            (TokenKind.Pipe, _) => (0xC1, 0x02),
            (TokenKind.Caret, _) => (0xC1, 0x03),
            (TokenKind.Plus, BoundIntegerOverflowBehavior.Checked)
                => (0xC0, 0x01),
            (TokenKind.Minus, BoundIntegerOverflowBehavior.Checked)
                => (0xC0, 0x02),
            (TokenKind.Star, BoundIntegerOverflowBehavior.Checked)
                => (0xC0, 0x03),
            (TokenKind.Slash, BoundIntegerOverflowBehavior.Checked)
                => (0xC0, 0x0C),
            (TokenKind.Percent, BoundIntegerOverflowBehavior.Checked)
                => (0xC0, 0x0D),
            (TokenKind.Plus, BoundIntegerOverflowBehavior.Wrapping)
                => (0xC0, 0x05),
            (TokenKind.Minus, BoundIntegerOverflowBehavior.Wrapping)
                => (0xC0, 0x06),
            (TokenKind.Star, BoundIntegerOverflowBehavior.Wrapping)
                => (0xC0, 0x07),
            _ => throw Unsupported(
                "The selected integer binary Body has no registered operation."),
        };

    private static byte GetC1M1Construction2FloatingBinaryControl(
        TokenKind operation)
        => operation switch
        {
            TokenKind.Plus => 0x20,
            TokenKind.Minus => 0x21,
            TokenKind.Star => 0x22,
            TokenKind.Slash => 0x23,
            TokenKind.Percent => 0x24,
            _ => throw Unsupported(
                "The selected floating binary Body has no registered operation."),
        };

    private static byte[] EncodeC1M1Construction2IntegerComparisonCode(
        int leftHome, int rightHome, TokenKind operation)
    {
        if (leftHome < 0 || leftHome > ushort.MaxValue
            || rightHome < 0 || rightHome > ushort.MaxValue)
        {
            throw new InvalidDataException(
                "An integer comparison Body has an invalid Home index.");
        }

        var code = new List<byte>(18);
        AppendHomeLoad(0, leftHome);
        AppendHomeLoad(1, rightHome);
        const byte destinationRegister = 0;
        const byte leftRegister = 0;
        const byte rightRegister = 1;
        var payload = (ushort)(destinationRegister | (0x0F << 4)
            | (leftRegister << 8) | (rightRegister << 12));
        code.Add(0xC2);
        code.Add(0);
        code.Add((byte)payload);
        code.Add((byte)(payload >> 8));
        var predicatePayload = (ushort)(destinationRegister
            | (destinationRegister << 4) | (0x0F << 8) | (0x0F << 12));
        code.Add(0xC3);
        code.Add(GetC1M1Construction2IntegerComparisonPredicate(operation));
        code.Add((byte)predicatePayload);
        code.Add((byte)(predicatePayload >> 8));
        code.Add(0x17);
        code.Add(0);
        return code.ToArray();

        void AppendHomeLoad(byte destination, int homeIndex)
        {
            code.Add(0xD5);
            code.Add(destination);
            code.Add(checked((byte)homeIndex));
            code.Add(checked((byte)(homeIndex >> 8)));
        }
    }

    private static byte[] EncodeC1M1Construction2BooleanEqualityCode(
        int leftHome, int rightHome, TokenKind operation)
    {
        if (leftHome < 0 || leftHome > ushort.MaxValue
            || rightHome < 0 || rightHome > ushort.MaxValue
            || operation is not (TokenKind.EqualsEquals
                or TokenKind.BangEquals))
        {
            throw new InvalidDataException(
                "A Boolean comparison Body has an invalid Home or operator.");
        }

        var code = new List<byte>(14);
        AppendHomeLoad(0, leftHome);
        AppendHomeLoad(1, rightHome);
        const byte destinationRegister = 0;
        const byte leftRegister = 0;
        const byte rightRegister = 1;
        var payload = (ushort)(destinationRegister | (0x0F << 4)
            | (leftRegister << 8) | (rightRegister << 12));
        code.Add(0xC2);
        code.Add(operation == TokenKind.EqualsEquals
            ? (byte)0x01 : (byte)0x02);
        code.Add((byte)payload);
        code.Add((byte)(payload >> 8));
        code.Add(0x17);
        code.Add(0);
        return code.ToArray();

        void AppendHomeLoad(byte destination, int homeIndex)
        {
            code.Add(0xD5);
            code.Add(destination);
            code.Add(checked((byte)homeIndex));
            code.Add(checked((byte)(homeIndex >> 8)));
        }
    }

    private static byte[] EncodeC1M1Construction2FieldReadBody(
        int fieldDefRow)
    {
        if (fieldDefRow <= 0 || fieldDefRow > 0xFF_FFFF)
        {
            throw new InvalidDataException(
                "A class field-read Body has an invalid FieldDef row.");
        }

        var body = new byte[8];
        body[0] = 0x02;
        BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(4),
            0x1400_0000u | checked((uint)fieldDefRow));
        return body;
    }

    private static byte[] EncodeC1M1Construction2FieldReferenceBody(
        uint fieldToken, int dependencyContractRow)
    {
        var fieldTokenKind = fieldToken >> 24;
        var fieldTokenRow = fieldToken & 0x00FF_FFFF;
        if (fieldTokenKind is not (0x14 or 0x1C)
            || fieldTokenRow == 0
            || dependencyContractRow <= 0
            || dependencyContractRow > 0xFF_FFFF)
        {
            throw new InvalidDataException(
                "A class field-reference Body has an invalid FieldDef/Import token or Dependency row.");
        }

        var body = new byte[12];
        body[0] = 0x04;
        BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(4),
            fieldToken);
        BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(8),
            checked((uint)dependencyContractRow));
        return body;
    }

    private static byte[] EncodeC1M1Construction2NestedFieldReadBody(
        BoundFunction function,
        C1M1Construction2NestedFieldRead fieldRead,
        Func<PrimitiveType, uint> typeSpecReferenceFor,
        int contractCount, TrRmArtifactSchema schema)
    {
        if (function.IsStatic
            || function.ParameterCount != function.Variables.Length
            || function.ParameterCount is < 1 or > 2
            || (uint)fieldRead.BaseVariableIndex
                >= (uint)function.ParameterCount
            || (fieldRead.BaseVariableIndex == 0
                && fieldRead.FieldDefRows.Length < 2)
            || fieldRead.FieldDefRows.Length
                != fieldRead.FieldTypes.Length)
        {
            throw new InvalidDataException(
                "A nested class FieldRead Body has an invalid receiver or path.");
        }

        var typeTokens = new uint[checked(function.ParameterCount
            + fieldRead.FieldTypes.Length)];
        var homeMap = new byte[checked(function.ParameterCount * 8)];
        for (var variableIndex = 0;
            variableIndex < function.ParameterCount; variableIndex++)
        {
            var typeToken = CreateStandardBodyTypeMapToken(
                typeSpecReferenceFor(
                    function.Variables[variableIndex].Type));
            typeTokens[variableIndex] = typeToken;
            var homeOffset = variableIndex * 8;
            homeMap[homeOffset] = variableIndex == 0
                ? (byte)1 : (byte)2;
            BinaryPrimitives.WriteUInt16LittleEndian(
                homeMap.AsSpan(homeOffset + 2, 2),
                checked((ushort)variableIndex));
            if (variableIndex != 0)
            {
                BinaryPrimitives.WriteUInt16LittleEndian(
                    homeMap.AsSpan(homeOffset + 4, 2),
                    checked((ushort)(variableIndex - 1)));
            }
        }

        for (var index = 0; index < fieldRead.FieldTypes.Length; index++)
        {
            typeTokens[function.ParameterCount + index]
                = CreateStandardBodyTypeMapToken(
                    typeSpecReferenceFor(fieldRead.FieldTypes[index]));
        }

        var maps = TrRmRegisterBodyMapBuilder.Create(typeTokens, []);
        for (var variableIndex = 0;
            variableIndex < function.ParameterCount; variableIndex++)
        {
            var homeOffset = variableIndex * 8;
            BinaryPrimitives.WriteUInt16LittleEndian(
                homeMap.AsSpan(homeOffset + 2, 2),
                checked((ushort)maps.GetTypeMapIndex(
                    typeTokens[variableIndex])));
        }

        var records = new List<ReadOnlyMemory<byte>> {
            new byte[] { 1, 0, 0, 0, 0, 0, 0, 0 },
        };
        var code = new List<byte>(
            checked((fieldRead.FieldDefRows.Length * 4) + 2));
        for (var index = 0;
            index < fieldRead.FieldDefRows.Length; index++)
        {
            var isTerminal = index == fieldRead.FieldDefRows.Length - 1;
            var destinationRegister = isTerminal ? (byte)0 : (byte)1;
            var baseKind = index == 0 ? (byte)2 : (byte)1;
            var baseIndex = index == 0
                ? checked((ushort)fieldRead.BaseVariableIndex)
                : (ushort)1;
            var fieldTypeToken = typeTokens[
                function.ParameterCount + index];
            if (records.Count > ushort.MaxValue)
            {
                throw Unsupported(
                    "A nested class FieldRead Body exceeds the PlaceSelection index limit.");
            }

            var recordIndex = checked((ushort)records.Count);
            records.Add(TrRmRegisterBodyRecordWriter.EncodePlaceSelection(
                targetKind: 2, accessKind: 1, destinationKind: 1,
                baseKind: baseKind,
                destinationIndex: destinationRegister,
                baseIndex: baseIndex, indexKind: 0, targetRefKind: 1,
                indexIndex: 0,
                targetRef: checked((uint)fieldRead.FieldDefRows[index]),
                referentTypeMapIndex: checked((ushort)
                    maps.GetTypeMapIndex(fieldTypeToken)),
                schema: schema));

            var opcode = isTerminal ? (byte)0xDA : (byte)0xD8;
            code.Add(opcode);
            code.Add(destinationRegister);
            code.Add((byte)recordIndex);
            code.Add((byte)(recordIndex >> 8));
        }

        code.Add(0x17);
        code.Add(0);
        var codeBytes = code.ToArray();
        return TrRmRegisterBodyWriter.Encode(homeMap, maps.TypeMap,
            default, default, default,
            [new TrRmRegisterBodyBlock(0,
                checked((uint)codeBytes.Length), 0)],
            records, codeBytes, contractCount: contractCount,
            schema: schema,
            generalPredicateGraphsInitializationReferenceBodies
                : schema.UsesGeneralPredicateGraphs());
    }

    private static byte[] EncodeC1M1Construction2FieldWriteBody(
        int fieldDefRow, int formalOrdinal, byte sourceUse)
    {
        if (fieldDefRow <= 0 || fieldDefRow > 0xFF_FFFF
            || formalOrdinal < 0 || formalOrdinal > ushort.MaxValue
            || sourceUse is < 1 or > 3)
        {
            throw new InvalidDataException(
                "A class field-write Body has an invalid FieldDef, formal or source-use.");
        }

        var body = new byte[12];
        body[0] = 0x03;
        BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(4),
            0x1400_0000u | checked((uint)fieldDefRow));
        BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(8),
            checked((ushort)formalOrdinal));
        body[10] = sourceUse;
        return body;
    }

    private static byte[] EncodeC1M1Construction2NestedFieldWriteBody(
        BoundFunction function,
        C1M1Construction2NestedFieldWrite fieldWrite,
        TrRmFrozenScalarValueBuilder frozenScalarValueBuilder,
        Func<PrimitiveType, uint> typeSpecReferenceFor,
        int contractCount, TrRmArtifactSchema schema)
    {
        if (function.ParameterCount != function.Variables.Length
            || fieldWrite.BaseVariableIndex != 0
            || (function.IsStatic
                ? fieldWrite.FieldDefRows.Length != 1
                    || function.ParameterCount
                        != (fieldWrite.Value is BoundRead ? 2 : 1)
                : !function.Variables[0].IsMutable
                    || function.ParameterCount != 2
                    || fieldWrite.FieldDefRows.Length < 2)
            || fieldWrite.FieldDefRows.Length
                != fieldWrite.FieldTypes.Length
            || fieldWrite.FieldTypes[^1]
                != fieldWrite.Value.Type)
        {
            throw new InvalidDataException(
                "A nested class FieldWrite Body has an invalid receiver, formal or path.");
        }

        var typeTokens = new uint[checked(
            function.ParameterCount + fieldWrite.FieldTypes.Length)];
        for (var index = 0; index < function.ParameterCount; index++)
        {
            typeTokens[index] = CreateStandardBodyTypeMapToken(
                typeSpecReferenceFor(function.Variables[index].Type));
        }
        for (var index = 0; index < fieldWrite.FieldTypes.Length; index++)
        {
            typeTokens[function.ParameterCount + index]
                = CreateStandardBodyTypeMapToken(
                    typeSpecReferenceFor(fieldWrite.FieldTypes[index]));
        }

        var maps = TrRmRegisterBodyMapBuilder.Create(typeTokens, []);
        var homeMap = new byte[checked(function.ParameterCount * 8)];
        for (var index = 0; index < function.ParameterCount; index++)
        {
            var home = homeMap.AsSpan(index * 8, 8);
            var isReceiver = !function.IsStatic && index == 0;
            home[0] = isReceiver ? (byte)1 : (byte)2;
            BinaryPrimitives.WriteUInt16LittleEndian(home[2..],
                checked((ushort)maps.GetTypeMapIndex(typeTokens[index])));
            if (!isReceiver)
            {
                BinaryPrimitives.WriteUInt16LittleEndian(home[4..],
                    checked((ushort)(index - (function.IsStatic ? 0 : 1))));
            }
        }

        var records = new List<ReadOnlyMemory<byte>> {
            new byte[] { 1, 0, 0, 0, 0, 0, 0, 0 },
        };
        var code = new List<byte>(checked(
            (fieldWrite.FieldDefRows.Length * 4) + 10));
        for (var index = 0;
            index < fieldWrite.FieldDefRows.Length; index++)
        {
            if (records.Count > ushort.MaxValue)
            {
                throw Unsupported(
                    "A nested class FieldWrite Body exceeds the PlaceSelection index limit.");
            }

            var recordIndex = checked((ushort)records.Count);
            records.Add(TrRmRegisterBodyRecordWriter.EncodePlaceSelection(
                targetKind: 2, accessKind: 2, destinationKind: 1,
                baseKind: index == 0 ? (byte)2 : (byte)1,
                destinationIndex: 1,
                baseIndex: index == 0
                    ? checked((ushort)fieldWrite.BaseVariableIndex)
                    : (ushort)1,
                indexKind: 0, targetRefKind: 1, indexIndex: 0,
                targetRef: checked((uint)fieldWrite.FieldDefRows[index]),
                referentTypeMapIndex: checked((ushort)
                    maps.GetTypeMapIndex(typeTokens[
                        function.ParameterCount + index])),
                schema: schema));
            code.Add(0xD8);
            code.Add(1);
            code.Add((byte)recordIndex);
            code.Add((byte)(recordIndex >> 8));
        }

        var constantRows = new List<TrRmFrozenScalarValueReference>();
        byte sourceKind;
        ushort sourceIndex;
        if (fieldWrite.Value is BoundRead read
            && read.Variable == 1
            && read.Type == function.Variables[1].Type)
        {
            sourceKind = 2;
            sourceIndex = 1;
        }
        else if (function.IsStatic
            && fieldWrite.Value is BoundConstant constant
            && constant.Type == fieldWrite.FieldTypes[^1])
        {
            sourceKind = 1;
            sourceIndex = 0;
            var constantIndex = checked((ushort)
            GetConstantMapIndex(constant));
            code.Add(0xD4);
            code.Add(0);
            code.Add((byte)constantIndex);
            code.Add((byte)(constantIndex >> 8));
        }
        else
        {
            throw new InvalidDataException(
            "A nested class FieldWrite Body has an unsupported scalar source.");
        }

        if (records.Count > ushort.MaxValue)
        {
            throw Unsupported(
                "A nested class FieldWrite Body exceeds the SemanticStore index limit.");
        }

        var storeRecordIndex = checked((ushort)records.Count);
        records.Add(TrRmRegisterBodyRecordWriter.EncodeSemanticStore(
            placeKind: 1, sourceKind: sourceKind, transition: 2,
            sourceUse: 1, placeIndex: 1, sourceIndex: sourceIndex,
            schema: schema));
        code.Add(0xD9);
        code.Add(0);
        code.Add((byte)storeRecordIndex);
        code.Add((byte)(storeRecordIndex >> 8));
        code.Add(0x18);
        code.Add(0);
        var codeBytes = code.ToArray();
        var constantMap = new byte[checked(constantRows.Count * 8)];
        for (var index = 0; index < constantRows.Count; index++)
        {
            var offset = index * 8;
            BinaryPrimitives.WriteUInt32LittleEndian(
                constantMap.AsSpan(offset), constantRows[index].TypeSpecRef);
            BinaryPrimitives.WriteUInt32LittleEndian(
                constantMap.AsSpan(offset + sizeof(uint)),
                constantRows[index].ConstantSourceRow);
        }

        return TrRmRegisterBodyWriter.Encode(homeMap, maps.TypeMap,
            default, default, constantMap,
            [new TrRmRegisterBodyBlock(0,
                checked((uint)codeBytes.Length), 0)],
            records, codeBytes, contractCount: contractCount,
            schema: schema,
            generalPredicateGraphsInitializationReferenceBodies
                : schema.UsesGeneralPredicateGraphs());

        int GetConstantMapIndex(BoundConstant constant)
        {
            var frozenValue = frozenScalarValueBuilder.Add(constant);
            if (frozenValue.TypeSpecRef
                != typeSpecReferenceFor(constant.Type))
            {
                throw Unsupported(
                    "A Construction-2 class field-write ConstantMap requires the exact FieldDef TypeSpecRef.");
            }

            var constantIndex = constantRows.IndexOf(frozenValue);
            if (constantIndex < 0)
            {
                if (constantRows.Count > ushort.MaxValue)
                {
                    throw Unsupported(
                        "A Construction-2 class FieldWrite Body exceeds the ConstantMap index limit.");
                }

                constantIndex = constantRows.Count;
                constantRows.Add(frozenValue);
            }

            return constantIndex;
        }
    }

    private static byte[] EncodeC1M1Construction2DirectNumericBinaryBody(
        BoundFunction function, BoundBinary binary,
        TrRmFrozenScalarValueBuilder frozenScalarValueBuilder,
        Func<PrimitiveType, uint> typeSpecReferenceFor, int contractCount,
        TrRmArtifactSchema schema)
    {
        const int receiverCount = 1;
        if (function.IsStatic
            || function.ParameterCount < receiverCount + 1
            || function.Variables.Length != function.ParameterCount
            || function.Variables[0].IsMutable
            || function.Variables.Skip(receiverCount).Any(
                static variable =>
                    variable.IsMutable || variable.IsOut || variable.IsInit
                    || variable.IsImplicitRef
                    || variable.IsInitializationLocal))
        {
            throw Unsupported(
                "A Construction-2 instance numeric binary Body requires readonly value formals and a readonly receiver.");
        }

        var leftRead = binary.Left as BoundRead;
        var rightRead = binary.Right as BoundRead;
        var leftConstant = binary.Left as BoundConstant;
        var rightConstant = binary.Right as BoundConstant;
        var leftReadIsValid = IsValidFormalRead(leftRead);
        var rightReadIsValid = IsValidFormalRead(rightRead);
        var leftConstantIsValid = IsValidConstant(leftConstant);
        var rightConstantIsValid = IsValidConstant(rightConstant);
        var leftType = leftRead?.Type ?? leftConstant?.Type;
        var rightType = rightRead?.Type ?? rightConstant?.Type;
        var hasSameNumericType
            = leftType.HasValue && rightType == leftType
                && IsC1M1Construction2BodyNumericType(leftType.Value);
        var isComparison
            = function.ReturnType == PrimitiveType.Bool
                && binary.Type == PrimitiveType.Bool
                && IsC1M1Construction2IntegerComparisonOperator(
                    binary.Operator)
                && hasSameNumericType
                && (leftReadIsValid || leftConstantIsValid)
                && (rightReadIsValid || rightConstantIsValid)
                && (leftReadIsValid || rightReadIsValid);
        var isIntegerValueOperation
            = function.ReturnType == binary.Type
                && leftType.HasValue && binary.Type == leftType.Value
                && hasSameNumericType
                && IsC1M1Construction2BodyIntegerType(leftType.Value)
                && (binary.Operator is TokenKind.Ampersand
                    or TokenKind.Pipe or TokenKind.Caret or TokenKind.Plus
                    or TokenKind.Minus or TokenKind.Star
                    or TokenKind.Slash or TokenKind.Percent)
                && (leftReadIsValid && rightReadIsValid
                    || leftReadIsValid && rightConstantIsValid
                    || leftConstantIsValid && rightReadIsValid);
        var isFloatingValueOperation
            = function.ReturnType == binary.Type
                && leftType.HasValue && binary.Type == leftType.Value
                && hasSameNumericType
                && PrimitiveTypeFacts.IsFloating(leftType.Value)
                && (binary.Operator is TokenKind.Plus or TokenKind.Minus
                    or TokenKind.Star or TokenKind.Slash or TokenKind.Percent)
                && (leftReadIsValid && rightReadIsValid
                    || leftReadIsValid && rightConstantIsValid
                    || leftConstantIsValid && rightReadIsValid);
        if ((!leftReadIsValid && !leftConstantIsValid)
            || (!rightReadIsValid && !rightConstantIsValid)
            || (!isComparison && !isIntegerValueOperation
                && !isFloatingValueOperation))
        {
            throw Unsupported(
                "A Construction-2 instance numeric Body requires direct same-typed formal reads or one canonical constant.");
        }

        byte[] constantMap = [];
        if (leftConstantIsValid || rightConstantIsValid)
        {
            var constant = leftConstantIsValid
                ? leftConstant! : rightConstant!;
            var frozenValue = frozenScalarValueBuilder.Add(constant);
            var expectedTypeSpecRef
                = typeSpecReferenceFor(constant.Type);
            if (frozenValue.TypeSpecRef != expectedTypeSpecRef)
            {
                throw Unsupported(
                    "A Construction-2 instance constant Body requires the exact operand TypeSpecRef.");
            }

            constantMap = new byte[8];
            BinaryPrimitives.WriteUInt32LittleEndian(constantMap,
                frozenValue.TypeSpecRef);
            BinaryPrimitives.WriteUInt32LittleEndian(
                constantMap.AsSpan(4), frozenValue.ConstantSourceRow);
        }

        var typeTokens = new uint[function.ParameterCount];
        var homeMap = new byte[checked(function.ParameterCount * 8)];
        for (var parameter = 0;
            parameter < function.ParameterCount; parameter++)
        {
            var typeToken = CreateStandardBodyTypeMapToken(
                typeSpecReferenceFor(function.Variables[parameter].Type));
            typeTokens[parameter] = typeToken;
        }

        var maps = TrRmRegisterBodyMapBuilder.Create(typeTokens, []);
        for (var parameter = 0;
            parameter < function.ParameterCount; parameter++)
        {
            var offset = parameter * 8;
            homeMap[offset] = parameter == 0 ? (byte)1 : (byte)2;
            BinaryPrimitives.WriteUInt16LittleEndian(
                homeMap.AsSpan(offset + 2, 2),
                maps.GetTypeMapIndex(typeTokens[parameter]));
            BinaryPrimitives.WriteUInt16LittleEndian(
                homeMap.AsSpan(offset + 4, 2),
                parameter == 0 ? (ushort)0
                    : checked((ushort)(parameter - 1)));
        }

        var code = new List<byte>(18);
        AppendOperandLoad(0, leftRead, leftReadIsValid);
        AppendOperandLoad(1, rightRead, rightReadIsValid);
        const byte destinationRegister = 0;
        const byte leftRegister = 0;
        const byte rightRegister = 1;
        var payload = (ushort)(destinationRegister | (0x0F << 4)
            | (leftRegister << 8) | (rightRegister << 12));
        if (isComparison)
        {
            code.Add(0xC2);
            code.Add(0);
            code.Add((byte)payload);
            code.Add((byte)(payload >> 8));
            var predicatePayload = (ushort)(destinationRegister
                | (destinationRegister << 4) | (0x0F << 8)
                | (0x0F << 12));
            code.Add(0xC3);
            code.Add(GetC1M1Construction2IntegerComparisonPredicate(
                binary.Operator));
            code.Add((byte)predicatePayload);
            code.Add((byte)(predicatePayload >> 8));
        }
        else if (isFloatingValueOperation)
        {
            code.Add(0xC0);
            code.Add(GetC1M1Construction2FloatingBinaryControl(
                binary.Operator));
            code.Add((byte)payload);
            code.Add((byte)(payload >> 8));
        }
        else
        {
            var (opcode, operation)
                = GetC1M1Construction2IntegerBinaryEncoding(
                    binary.Operator, binary.OverflowBehavior);
            code.Add(opcode);
            code.Add(operation);
            code.Add((byte)payload);
            code.Add((byte)(payload >> 8));
        }
        code.Add(0x17);
        code.Add(0);

        var records = new ReadOnlyMemory<byte>[] {
            new(new byte[] { 1, 0, 0, 0, 0, 0, 0, 0 }),
        };
        return TrRmRegisterBodyWriter.Encode(homeMap, maps.TypeMap,
            default, default, constantMap,
            [new TrRmRegisterBodyBlock(0,
                checked((uint)code.Count), 0)],
            records, code.ToArray(), contractCount: contractCount,
            schema: schema,
            generalPredicateGraphsInitializationReferenceBodies
                : schema.UsesGeneralPredicateGraphs());

        bool IsValidFormalRead(BoundRead? read)
            => read is not null
                && IsC1M1Construction2BodyNumericType(read.Type)
                && read.Variable >= receiverCount
                && read.Variable < function.ParameterCount
                && function.Variables[read.Variable].Type
                    == read.Type;

        static bool IsValidConstant(BoundConstant? constant)
            => constant is not null
                && IsC1M1Construction2BodyNumericType(constant.Type)
                && constant.RepresentationType == constant.Type
                && constant.Bits.IsCanonicalFor(constant.Type);

        void AppendOperandLoad(byte destination, BoundRead? read,
            bool readIsValid)
        {
            if (readIsValid && read is not null)
            {
                AppendHomeLoad(destination, read.Variable);
            }
            else
            {
                code.Add(0xD4);
                code.Add(destination);
                code.Add(0);
                code.Add(0);
            }
        }

        static void AppendHomeLoad(List<byte> instructions,
            byte destination, int homeIndex)
        {
            instructions.Add(0xD5);
            instructions.Add(destination);
            instructions.Add(checked((byte)homeIndex));
            instructions.Add(checked((byte)(homeIndex >> 8)));
        }
    }

    private static byte[] EncodeC1M1Construction2DirectBooleanComparisonBody(
        BoundFunction function, BoundBinary binary,
        Func<PrimitiveType, uint> typeSpecReferenceFor, int contractCount,
        TrRmArtifactSchema schema)
    {
        if (function.IsStatic || function.ReturnType != PrimitiveType.Bool
            || function.ParameterCount != 3
            || function.Variables.Length != function.ParameterCount
            || function.Variables[0].IsMutable
            || function.Variables[1].Type != PrimitiveType.Bool
            || function.Variables[2].Type != PrimitiveType.Bool
            || function.Variables.Skip(1).Any(static variable =>
                variable.IsMutable || variable.IsOut || variable.IsInit
                || variable.IsImplicitRef
                || variable.IsInitializationLocal)
            || binary.Type != PrimitiveType.Bool
            || binary.Operator is not (TokenKind.EqualsEquals
                or TokenKind.BangEquals)
            || binary.Left is not BoundRead left
            || binary.Right is not BoundRead right
            || left.Type != PrimitiveType.Bool
            || right.Type != PrimitiveType.Bool
            || left.Variable is not (1 or 2)
            || right.Variable is not (1 or 2))
        {
            throw Unsupported(
                "A Construction-2 instance Boolean comparison Body requires two direct Boolean value formals and a readonly receiver.");
        }

        var typeTokens = new uint[function.ParameterCount];
        var homeMap = new byte[checked(function.ParameterCount * 8)];
        for (var parameter = 0;
            parameter < function.ParameterCount; parameter++)
        {
            typeTokens[parameter] = CreateStandardBodyTypeMapToken(
                typeSpecReferenceFor(function.Variables[parameter].Type));
        }

        var maps = TrRmRegisterBodyMapBuilder.Create(typeTokens, []);
        for (var parameter = 0;
            parameter < function.ParameterCount; parameter++)
        {
            var offset = parameter * 8;
            homeMap[offset] = parameter == 0 ? (byte)1 : (byte)2;
            BinaryPrimitives.WriteUInt16LittleEndian(
                homeMap.AsSpan(offset + 2, 2),
                maps.GetTypeMapIndex(typeTokens[parameter]));
            BinaryPrimitives.WriteUInt16LittleEndian(
                homeMap.AsSpan(offset + 4, 2),
                parameter == 0 ? (ushort)0
                    : checked((ushort)(parameter - 1)));
        }

        var code = EncodeC1M1Construction2BooleanEqualityCode(
            left.Variable, right.Variable, binary.Operator);
        ReadOnlyMemory<byte>[] records = [
            new byte[] { 1, 0, 0, 0, 0, 0, 0, 0 },
        ];
        return TrRmRegisterBodyWriter.Encode(homeMap, maps.TypeMap,
            default, default, default,
            [new TrRmRegisterBodyBlock(0,
                checked((uint)code.Length), 0)],
            records, code, contractCount: contractCount,
            schema: schema,
            generalPredicateGraphsInitializationReferenceBodies
                : schema.UsesGeneralPredicateGraphs());
    }

    private static byte[] EncodeC1M1Construction2DirectNumericUnaryBody(
        BoundFunction function, BoundUnary unary,
        Func<PrimitiveType, uint> typeSpecReferenceFor, int contractCount,
        TrRmArtifactSchema schema)
    {
        if (function.IsStatic
            || function.ParameterCount != 2
            || function.Variables.Length != function.ParameterCount
            || function.Variables[0].IsMutable
            || function.Variables[1].Type != unary.Type
            || function.Variables[1].IsMutable
            || function.Variables[1].IsOut || function.Variables[1].IsInit
            || function.Variables[1].IsImplicitRef
            || function.Variables[1].IsInitializationLocal
            || function.ReturnType != unary.Type
            || !IsC1M1Construction2BodyNumericType(unary.Type)
            || unary.Operand is not BoundRead operand
            || operand.Type != unary.Type
            || operand.Variable != 1
            || (PrimitiveTypeFacts.IsFloating(unary.Type)
                ? unary.Operator != TokenKind.Minus
                : unary.Operator is not (TokenKind.Minus or TokenKind.Tilde)))
        {
            throw Unsupported(
                "A Construction-2 instance numeric unary Body requires one direct parameter read and a readonly receiver.");
        }

        var typeTokens = new uint[function.ParameterCount];
        var homeMap = new byte[checked(function.ParameterCount * 8)];
        for (var parameter = 0;
            parameter < function.ParameterCount; parameter++)
        {
            var typeToken = CreateStandardBodyTypeMapToken(
                typeSpecReferenceFor(function.Variables[parameter].Type));
            typeTokens[parameter] = typeToken;
        }

        var maps = TrRmRegisterBodyMapBuilder.Create(typeTokens, []);
        for (var parameter = 0;
            parameter < function.ParameterCount; parameter++)
        {
            var offset = parameter * 8;
            homeMap[offset] = parameter == 0 ? (byte)1 : (byte)2;
            BinaryPrimitives.WriteUInt16LittleEndian(
                homeMap.AsSpan(offset + 2, 2),
                maps.GetTypeMapIndex(typeTokens[parameter]));
            BinaryPrimitives.WriteUInt16LittleEndian(
                homeMap.AsSpan(offset + 4, 2),
                parameter == 0 ? (ushort)0
                    : checked((ushort)(parameter - 1)));
        }

        var (opcode, operation) = PrimitiveTypeFacts.IsFloating(unary.Type)
            ? ((byte)0xC0, (byte)0x25)
            : (unary.Operator, unary.OverflowBehavior) switch
            {
                (TokenKind.Tilde, _) => ((byte)0xC1, (byte)0x04),
                (TokenKind.Minus,
                    BoundIntegerOverflowBehavior.Checked)
                    => ((byte)0xC0, (byte)0x04),
                (TokenKind.Minus,
                    BoundIntegerOverflowBehavior.Wrapping)
                    => ((byte)0xC0, (byte)0x08),
                _ => throw Unsupported(
                    "The selected instance register Body does not encode this integer unary operation."),
            };
        var code = new byte[] {
            0xD5, 0, 1, 0,
            opcode, operation, 0xF0, 0xF0,
            0x17, 0,
        };
        var records = new ReadOnlyMemory<byte>[] {
            new(new byte[] { 1, 0, 0, 0, 0, 0, 0, 0 }),
        };
        return TrRmRegisterBodyWriter.Encode(homeMap, maps.TypeMap,
            default, default, default,
            [new TrRmRegisterBodyBlock(0,
                checked((uint)code.Length), 0)],
            records, code, contractCount: contractCount,
            schema: schema,
            generalPredicateGraphsInitializationReferenceBodies
                : schema.UsesGeneralPredicateGraphs());
    }

    private static bool IsC1M1Construction2ReceiverTypeCompatible(
        PrimitiveType sourceType, bool sourceWritable,
        PrimitiveType targetType, bool targetWritable, BoundProgram program)
    {
        if (ManagedClassTypes.IsClass(sourceType)
            && ManagedClassTypes.IsClass(targetType))
        {
            return ManagedClassTypes.ClassIndex(sourceType)
                    == ManagedClassTypes.ClassIndex(targetType)
                && ManagedClassTypes.IsWritable(sourceType) == sourceWritable
                && ManagedClassTypes.IsWritable(targetType) == targetWritable
                && (!targetWritable || sourceWritable);
        }

        if (!BoundGenericNominalTypeTypes.IsType(sourceType)
            || !BoundGenericNominalTypeTypes.IsType(targetType)
            || !BoundGenericNominalTypeContract.IsNominalClassType(
                sourceType, program.GenericNominalTypeInstances,
                program.GenericNominalTypeDefinitions)
            || !BoundGenericNominalTypeContract.IsNominalClassType(
                targetType, program.GenericNominalTypeInstances,
                program.GenericNominalTypeDefinitions))
        {
            return false;
        }

        var sourceInstance = program.GenericNominalTypeInstances[
            BoundGenericNominalTypeTypes.Index(sourceType)];
        var targetInstance = program.GenericNominalTypeInstances[
            BoundGenericNominalTypeTypes.Index(targetType)];
        return sourceInstance.Definition == targetInstance.Definition
            && sourceInstance.Arguments.IsEmpty
            && targetInstance.Arguments.IsEmpty
            && (sourceInstance.Qualifiers & ~0x01) == 0
            && (targetInstance.Qualifiers & ~0x01) == 0
            && ((sourceInstance.Qualifiers & 1) != 0) == sourceWritable
            && ((targetInstance.Qualifiers & 1) != 0) == targetWritable
            && (!targetWritable || sourceWritable);
    }

    private static byte[] EncodeC1M1Construction2CallNumericBinaryBody(
        BoundFunction function, BoundBinary binary, int targetMethodDefRow,
        BoundFunction targetFunction, BoundProgram program,
        TrRmFrozenScalarValueBuilder frozenScalarValueBuilder,
        Func<PrimitiveType, uint> typeSpecReferenceFor, int contractCount,
        TrRmArtifactSchema schema)
    {
        var callerReceiverCount = function.IsStatic ? 0 : 1;
        var targetReceiverCount = targetFunction.IsStatic ? 0 : 1;
        if (function.ParameterCount != callerReceiverCount + 2
            || function.Variables.Length != function.ParameterCount)
        {
            throw Unsupported(
                "A Construction-2 call-binary Body requires exactly two value parameters.");
        }

        var operationType = function.Variables[callerReceiverCount].Type;
        var rightRead = binary.Right as BoundRead;
        var rightConstant = binary.Right as BoundConstant;
        if (targetMethodDefRow <= 0 || targetMethodDefRow > 0xFF_FFFF
            || !IsC1M1Construction2BodyNumericType(operationType)
            || function.Variables.Skip(callerReceiverCount).Any(
                variable => variable.Type != operationType
                || variable.IsMutable
                || variable.IsOut || variable.IsInit
                || variable.IsImplicitRef
                || variable.IsInitializationLocal)
            || binary.Left is not BoundCall call
            || (rightRead is null && !IsValidConstant(rightConstant))
            || (rightRead is not null
                && (rightRead.Type != operationType
                    || rightRead.Variable < callerReceiverCount
                    || rightRead.Variable >= function.ParameterCount))
            || (rightConstant is not null
                && rightConstant.Type != operationType)
            || call.Type != operationType
            || call.Arguments.Length != targetFunction.ParameterCount
            || !call.DefaultedArguments.IsDefaultOrEmpty
            || call.InterfaceCallIdentity is not null
            || call.GenericMethodCall is not null
            || call.ImportedGenericMethodCall is not null
            || targetFunction.IsExternal || targetFunction.IsClassConstructor
            || targetFunction.IsConstructor || targetFunction.IsCleanup
            || targetFunction.IsOperator
            || targetFunction.ModuleImport is not null
            || targetFunction.GenericMethodInstantiation is not null
            || !StringComparer.Ordinal.Equals(function.DeclaringStruct,
                targetFunction.DeclaringStruct)
            || targetFunction.ReturnType != operationType
            || targetFunction.ParameterCount != targetReceiverCount + 2
            || targetFunction.Variables.Length
                < targetFunction.ParameterCount
            || targetFunction.Variables.Skip(targetReceiverCount)
                .Take(2).Any(variable =>
                    variable.Type != operationType
                    || variable.IsMutable || variable.IsOut
                    || variable.IsInit || variable.IsImplicitRef
                    || variable.IsInitializationLocal))
        {
            throw Unsupported(
                "A Construction-2 call-binary Body requires a same-class direct numeric call and one direct same-typed read or canonical constant.");
        }

        for (var parameter = 0;
            parameter < targetFunction.ParameterCount; parameter++)
        {
            var sourceVariable = targetFunction.IsStatic
                ? callerReceiverCount + parameter
                : parameter == 0 ? 0
                    : callerReceiverCount + parameter - 1;
            var source = function.Variables[sourceVariable];
            var targetParameter = targetFunction.Variables[parameter];
            if (call.ParameterIndex(parameter) != parameter
                || call.ImplicitReferenceTemporary(parameter) != -1
                || call.Arguments[parameter] is not BoundRead argument
                || argument.Variable != sourceVariable
                || argument.Type != source.Type
                || source.IsOut != targetParameter.IsOut
                || source.IsInit != targetParameter.IsInit
                || source.IsImplicitRef != targetParameter.IsImplicitRef
                || source.IsInitializationLocal
                    != targetParameter.IsInitializationLocal)
            {
                throw Unsupported(
                    "A Construction-2 call-binary Body requires direct argument Homes matching the selected MethodSig.");
            }

            var isReceiver = !targetFunction.IsStatic && parameter == 0;
            if (isReceiver
                ? !IsC1M1Construction2ReceiverTypeCompatible(
                    source.Type, source.IsMutable, targetParameter.Type,
                    targetParameter.IsMutable, program)
                : source.Type != targetParameter.Type
                    || source.IsMutable != targetParameter.IsMutable)
            {
                throw Unsupported(
                    "A Construction-2 call-binary Body does not preserve the selected receiver access or formal type.");
            }
        }

        var isComparison
            = function.ReturnType == PrimitiveType.Bool
                && binary.Type == PrimitiveType.Bool
                && IsC1M1Construction2IntegerComparisonOperator(
                    binary.Operator);
        var isIntegerValueOperation
            = function.ReturnType == operationType
                && binary.Type == operationType
                && IsC1M1Construction2BodyIntegerType(operationType)
                && (binary.Operator is TokenKind.Ampersand
                    or TokenKind.Pipe or TokenKind.Caret or TokenKind.Plus
                    or TokenKind.Minus or TokenKind.Star
                    or TokenKind.Slash or TokenKind.Percent);
        var isFloatingValueOperation
            = function.ReturnType == operationType
                && binary.Type == operationType
                && PrimitiveTypeFacts.IsFloating(operationType)
                && (binary.Operator is TokenKind.Plus or TokenKind.Minus
                    or TokenKind.Star or TokenKind.Slash or TokenKind.Percent);
        if (!isComparison && !isIntegerValueOperation
            && !isFloatingValueOperation)
        {
            throw Unsupported(
                "A Construction-2 call-binary Body requires an assigned numeric operation or Boolean comparison.");
        }

        byte[] constantMap = [];
        if (rightConstant is not null)
        {
            var frozenValue
                = frozenScalarValueBuilder.Add(rightConstant);
            var expectedTypeSpecRef
                = typeSpecReferenceFor(operationType);
            if (frozenValue.TypeSpecRef != expectedTypeSpecRef)
            {
                throw Unsupported(
                    "A Construction-2 call constant Body requires the exact operand TypeSpecRef.");
            }

            constantMap = new byte[8];
            BinaryPrimitives.WriteUInt32LittleEndian(constantMap,
                frozenValue.TypeSpecRef);
            BinaryPrimitives.WriteUInt32LittleEndian(
                constantMap.AsSpan(4), frozenValue.ConstantSourceRow);
        }

        var typeTokens = new uint[function.ParameterCount];
        var homeMap = new byte[checked(function.ParameterCount * 8)];
        for (var parameter = 0;
            parameter < function.ParameterCount; parameter++)
        {
            var typeToken = CreateStandardBodyTypeMapToken(
                typeSpecReferenceFor(function.Variables[parameter].Type));
            typeTokens[parameter] = typeToken;
        }

        var targetMethodToken
            = 0x1600_0000u | checked((uint)targetMethodDefRow);
        var maps = TrRmRegisterBodyMapBuilder.Create(typeTokens,
            [targetMethodToken]);
        for (var parameter = 0;
            parameter < function.ParameterCount; parameter++)
        {
            var offset = parameter * 8;
            var role = !function.IsStatic && parameter == 0
                ? (byte)1 : (byte)2;
            homeMap[offset] = role;
            BinaryPrimitives.WriteUInt16LittleEndian(
                homeMap.AsSpan(offset + 2, 2),
                maps.GetTypeMapIndex(typeTokens[parameter]));
            BinaryPrimitives.WriteUInt16LittleEndian(
                homeMap.AsSpan(offset + 4, 2),
                role == 2
                    ? checked((ushort)(parameter - callerReceiverCount))
                    : (ushort)0);
        }

        var callInputs = new TrRmRegisterBodyCallInput[
            targetFunction.ParameterCount];
        for (var parameter = 0;
            parameter < callInputs.Length; parameter++)
        {
            var sourceHome = targetFunction.IsStatic
                ? callerReceiverCount + parameter
                : parameter == 0 ? 0
                    : callerReceiverCount + parameter - 1;
            var sourceUse = !targetFunction.IsStatic && parameter == 0
                ? targetFunction.Variables[0].IsMutable
                    ? (byte)4 : (byte)3
                : (byte)1;
            callInputs[parameter] = new(2, sourceUse,
                checked((ushort)sourceHome));
        }
        var callSite = TrRmRegisterBodyRecordWriter.EncodeCallSite(1, 1,
            maps.GetMethodMapIndex(targetMethodToken), 0, callInputs, [],
            schema: schema);
        ReadOnlyMemory<byte>[] records = [
            new byte[] { 1, 0, 0, 0, 0, 0, 0, 0 },
            new(callSite),
        ];
        var callSiteRecordIndex = checked((ushort)(records.Length - 1));
        var code = new List<byte>(isComparison ? 18 : 14);
        code.Add(0xE0);
        code.Add(0);
        code.Add(unchecked((byte)callSiteRecordIndex));
        code.Add((byte)(callSiteRecordIndex >> 8));
        if (rightRead is not null)
        {
            AppendHomeLoad(1, rightRead.Variable);
        }
        else
        {
            code.Add(0xD4);
            code.Add(1);
            code.Add(0);
            code.Add(0);
        }
        const byte destinationRegister = 0;
        const byte leftRegister = 0;
        const byte rightRegister = 1;
        var payload = (ushort)(destinationRegister | (0x0F << 4)
            | (leftRegister << 8) | (rightRegister << 12));
        if (isComparison)
        {
            code.Add(0xC2);
            code.Add(0);
            code.Add((byte)payload);
            code.Add((byte)(payload >> 8));
            var predicatePayload = (ushort)(destinationRegister
                | (destinationRegister << 4) | (0x0F << 8)
                | (0x0F << 12));
            code.Add(0xC3);
            code.Add(GetC1M1Construction2IntegerComparisonPredicate(
                binary.Operator));
            code.Add((byte)predicatePayload);
            code.Add((byte)(predicatePayload >> 8));
        }
        else if (isFloatingValueOperation)
        {
            code.Add(0xC0);
            code.Add(GetC1M1Construction2FloatingBinaryControl(
                binary.Operator));
            code.Add((byte)payload);
            code.Add((byte)(payload >> 8));
        }
        else
        {
            var (opcode, control)
                = GetC1M1Construction2IntegerBinaryEncoding(
                    binary.Operator, binary.OverflowBehavior);
            code.Add(opcode);
            code.Add(control);
            code.Add((byte)payload);
            code.Add((byte)(payload >> 8));
        }
        code.Add(0x17);
        code.Add(0);
        return TrRmRegisterBodyWriter.Encode(homeMap, maps.TypeMap,
            maps.MethodMap, default, constantMap,
            [new TrRmRegisterBodyBlock(0,
                checked((uint)code.Count), 0)],
            records, code.ToArray(), contractCount: contractCount,
            schema: schema,
            generalPredicateGraphsInitializationReferenceBodies
                : schema.UsesGeneralPredicateGraphs());

        void AppendHomeLoad(byte destination, int homeIndex)
        {
            code.Add(0xD5);
            code.Add(destination);
            code.Add(checked((byte)homeIndex));
            code.Add(checked((byte)(homeIndex >> 8)));
        }

        static bool IsValidConstant(BoundConstant? constant)
            => constant is not null
                && IsC1M1Construction2BodyNumericType(constant.Type)
                && constant.Type == constant.RepresentationType
                && constant.Bits.IsCanonicalFor(constant.Type);
    }

    private static byte[] EncodeC1M1Construction2LocalCopyReturnBody(
        BoundFunction function,
        Func<PrimitiveType, uint> typeSpecReferenceFor, int contractCount,
        TrRmArtifactSchema schema)
    {
        var receiverCount = function.IsStatic ? 0 : 1;
        var localCount = function.Variables.Length - function.ParameterCount;
        var lastLocalVariable = function.Variables.Length - 1;
        if (function.ParameterCount != receiverCount + 1
            || localCount <= 0
            || function.Variables.Length > ushort.MaxValue + 1
            || function.Body.RetiredManagedLocals.Length != 0
            || function.Body.Statements.Length != localCount + 1
            || function.Body.Statements[localCount] is not BoundReturn {
                Value: BoundRead returnedRead,
            })
        {
            throw Unsupported(
                "A Construction-2 local-copy Body requires one value formal, one or more locals, and a direct local return.");
        }

        var operationType = function.Variables[receiverCount].Type;
        if ((!IsC1M1Construction2BodyNumericType(operationType)
                && operationType != PrimitiveType.Bool)
            || function.ReturnType != operationType
            || (!function.IsStatic && function.Variables[0].IsMutable)
            || function.Variables.Skip(receiverCount).Any(variable =>
                variable.Type != operationType || variable.IsMutable
                || variable.IsOut || variable.IsInit
                || variable.IsImplicitRef
                || variable.IsInitializationLocal)
            || returnedRead.Type != operationType
            || returnedRead.Variable != lastLocalVariable)
        {
            throw Unsupported(
                "A Construction-2 local-copy Body requires immutable same-typed scalar values and a direct return of the final local.");
        }

        var localInitializers = new BoundRead[localCount];
        for (var localOrdinal = 0; localOrdinal < localCount;
            localOrdinal++)
        {
            var localVariable = function.ParameterCount + localOrdinal;
            if (function.Body.Statements[localOrdinal] is not BoundLocal local
                || local.Variable != localVariable
                || local.Initializer is not BoundRead initialRead
                || initialRead.Type != operationType
                || initialRead.Variable < 0
                || initialRead.Variable >= localVariable
                || function.Variables[initialRead.Variable].Type
                    != operationType)
            {
                throw Unsupported(
                    "A Construction-2 local-copy Body requires source-ordered same-typed local copies.");
            }

            localInitializers[localOrdinal] = initialRead;
        }

        var typeTokens = new uint[function.Variables.Length];
        var homeMap = new byte[checked(function.Variables.Length * 8)];
        for (var variable = 0; variable < function.Variables.Length;
            variable++)
        {
            var typeToken = CreateStandardBodyTypeMapToken(
                typeSpecReferenceFor(function.Variables[variable].Type));
            typeTokens[variable] = typeToken;
        }

        var maps = TrRmRegisterBodyMapBuilder.Create(typeTokens, []);
        for (var variable = 0; variable < function.Variables.Length;
            variable++)
        {
            var offset = variable * 8;
            var role = !function.IsStatic && variable == 0 ? (byte)1
                : variable < function.ParameterCount ? (byte)2 : (byte)3;
            homeMap[offset] = role;
            BinaryPrimitives.WriteUInt16LittleEndian(
                homeMap.AsSpan(offset + 2, 2),
                maps.GetTypeMapIndex(typeTokens[variable]));
            BinaryPrimitives.WriteUInt16LittleEndian(
                homeMap.AsSpan(offset + 4, 2),
                role == 2
                    ? checked((ushort)(variable - receiverCount))
                    : (ushort)0);
        }

        var code = new List<byte>(checked((localCount * 8) + 8));
        void AppendHomeLoad(int homeIndex)
        {
            code.Add(0xD5);
            code.Add(0);
            code.Add(checked((byte)homeIndex));
            code.Add(checked((byte)(homeIndex >> 8)));
        }

        for (var localOrdinal = 0; localOrdinal < localCount;
            localOrdinal++)
        {
            var localVariable = function.ParameterCount + localOrdinal;
            AppendHomeLoad(localInitializers[localOrdinal].Variable);
            code.Add(0xD6);
            code.Add(0x10);
            code.Add(checked((byte)localVariable));
            code.Add(checked((byte)(localVariable >> 8)));
        }

        AppendHomeLoad(lastLocalVariable);
        code.Add(0xEB);
        code.Add(0);
        code.Add(checked((byte)lastLocalVariable));
        code.Add(checked((byte)(lastLocalVariable >> 8)));
        ReadOnlyMemory<byte>[] records = [
            new byte[] { 1, 0, 0, 0, 0, 0, 0, 0 },
        ];
        return TrRmRegisterBodyWriter.Encode(homeMap, maps.TypeMap,
            default, default, default,
            [new TrRmRegisterBodyBlock(0,
                checked((uint)code.Length), 0)],
            records, code.ToArray(), contractCount: contractCount,
            schema: schema,
            generalPredicateGraphsInitializationReferenceBodies
                : schema.UsesGeneralPredicateGraphs());
    }

    private static byte[] EncodeC1M1Construction2LocalNumericUnaryReturnBody(
        BoundFunction function, BoundUnary unary,
        Func<PrimitiveType, uint> typeSpecReferenceFor, int contractCount,
        TrRmArtifactSchema schema)
    {
        var receiverCount = function.IsStatic ? 0 : 1;
        var localCount = function.Variables.Length
            - function.ParameterCount;
        var lastLocalVariable = function.Variables.Length - 1;
        if (function.ParameterCount != receiverCount + 1
            || localCount <= 0
            || function.Variables.Length > ushort.MaxValue + 1
            || function.ReturnType != unary.Type
            || !IsC1M1Construction2BodyNumericType(unary.Type)
            || (!function.IsStatic && function.Variables[0].IsMutable)
            || !function.Body.RetiredManagedLocals.IsEmpty
            || function.Body.Statements.Length != localCount + 1
            || function.Body.Statements[localCount] is not BoundReturn {
                Value: BoundUnary returnedUnary,
            }
            || returnedUnary != unary
            || function.Variables.Skip(receiverCount).Any(variable =>
                variable.Type != unary.Type || variable.IsMutable
                || variable.IsOut || variable.IsInit
                || variable.IsImplicitRef
                || variable.IsInitializationLocal)
            || unary.Operand is not BoundRead operand
            || operand.Type != unary.Type
            || operand.Variable != lastLocalVariable
            || (PrimitiveTypeFacts.IsFloating(unary.Type)
                ? unary.Operator != TokenKind.Minus
                : unary.Operator is not (TokenKind.Minus
                    or TokenKind.Tilde)))
        {
            throw Unsupported(
                "A Construction-2 local-unary Body requires same-typed numeric local copies and a supported unary return.");
        }

        var localInitializers = new BoundRead[localCount];
        for (var localOrdinal = 0; localOrdinal < localCount;
            localOrdinal++)
        {
            var localVariable = function.ParameterCount + localOrdinal;
            if (function.Body.Statements[localOrdinal] is not BoundLocal local
                || local.Variable != localVariable
                || local.Initializer is not BoundRead initialRead
                || initialRead.Type != unary.Type
                || initialRead.Variable < 0
                || initialRead.Variable >= localVariable
                || function.Variables[initialRead.Variable].Type
                    != unary.Type)
            {
                throw Unsupported(
                    "A Construction-2 local-unary Body requires source-ordered same-typed local copies.");
            }

            localInitializers[localOrdinal] = initialRead;
        }

        var typeTokens = new uint[function.Variables.Length];
        var homeMap = new byte[checked(function.Variables.Length * 8)];
        for (var variable = 0; variable < function.Variables.Length;
            variable++)
        {
            typeTokens[variable] = CreateStandardBodyTypeMapToken(
                typeSpecReferenceFor(function.Variables[variable].Type));
        }

        var maps = TrRmRegisterBodyMapBuilder.Create(typeTokens, []);
        for (var variable = 0; variable < function.Variables.Length;
            variable++)
        {
            var offset = variable * 8;
            var role = !function.IsStatic && variable == 0 ? (byte)1
                : variable < function.ParameterCount ? (byte)2 : (byte)3;
            homeMap[offset] = role;
            BinaryPrimitives.WriteUInt16LittleEndian(
                homeMap.AsSpan(offset + 2, 2),
                maps.GetTypeMapIndex(typeTokens[variable]));
            BinaryPrimitives.WriteUInt16LittleEndian(
                homeMap.AsSpan(offset + 4, 2),
                role == 2
                    ? checked((ushort)(variable - receiverCount))
                    : (ushort)0);
        }

        var (opcode, control) = PrimitiveTypeFacts.IsFloating(unary.Type)
            ? ((byte)0xC0, (byte)0x25)
            : (unary.Operator, unary.OverflowBehavior) switch
            {
                (TokenKind.Tilde, _) => ((byte)0xC1, (byte)0x04),
                (TokenKind.Minus,
                    BoundIntegerOverflowBehavior.Checked)
                    => ((byte)0xC0, (byte)0x04),
                (TokenKind.Minus,
                    BoundIntegerOverflowBehavior.Wrapping)
                    => ((byte)0xC0, (byte)0x08),
                _ => throw Unsupported(
                    "The selected local unary Body does not encode this numeric operation."),
            };
        var code = new List<byte>(checked((localCount * 8) + 10));
        for (var localOrdinal = 0; localOrdinal < localCount;
            localOrdinal++)
        {
            var localVariable = function.ParameterCount + localOrdinal;
            AppendHomeLoad(localInitializers[localOrdinal].Variable);
            code.Add(0xD6);
            code.Add(0x10);
            code.Add(checked((byte)localVariable));
            code.Add(checked((byte)(localVariable >> 8)));
        }

        AppendHomeLoad(lastLocalVariable);
        code.Add(opcode);
        code.Add(control);
        code.Add(0xF0);
        code.Add(0xF0);
        code.Add(0x17);
        code.Add(0);

        ReadOnlyMemory<byte>[] records = [
            new byte[] { 1, 0, 0, 0, 0, 0, 0, 0 },
        ];
        return TrRmRegisterBodyWriter.Encode(homeMap, maps.TypeMap,
            default, default, default,
            [new TrRmRegisterBodyBlock(0,
                checked((uint)code.Count), 0)],
            records, code.ToArray(), contractCount: contractCount,
            schema: schema,
            generalPredicateGraphsInitializationReferenceBodies
                : schema.UsesGeneralPredicateGraphs());

        void AppendHomeLoad(int homeIndex)
        {
            code.Add(0xD5);
            code.Add(0);
            code.Add(checked((byte)homeIndex));
            code.Add(checked((byte)(homeIndex >> 8)));
        }
    }

    private static byte[] EncodeC1M1Construction2LocalNumericBinaryReturnBody(
        BoundFunction function,
        TrRmFrozenScalarValueBuilder frozenScalarValueBuilder,
        Func<PrimitiveType, uint> typeSpecReferenceFor, int contractCount,
        TrRmArtifactSchema schema)
    {
        var receiverCount = function.IsStatic ? 0 : 1;
        var localCount = function.Variables.Length
            - function.ParameterCount;
        if (function.ParameterCount < receiverCount + 1
            || localCount <= 0
            || (!function.IsStatic && function.Variables[0].IsMutable)
            || !function.Body.RetiredManagedLocals.IsEmpty
            || function.Body.Statements.Length != localCount + 1
            || function.Body.Statements[localCount] is not BoundReturn {
                Value: BoundBinary binary,
            })
        {
            throw Unsupported(
                "A Construction-2 local-binary Body requires initialized numeric locals followed by one binary return.");
        }

        var operationType = function.Variables[receiverCount].Type;
        if (!IsC1M1Construction2BodyNumericType(operationType)
            || function.Variables.Skip(receiverCount).Any(variable =>
                variable.Type != operationType || variable.IsMutable
                || variable.IsOut || variable.IsInit
                || variable.IsImplicitRef
                || variable.IsInitializationLocal))
        {
            throw Unsupported(
                "A Construction-2 local-binary Body requires one numeric value type and immutable local copies.");
        }

        var localInitializers = new BoundRead[localCount];
        for (var localOrdinal = 0;
            localOrdinal < localCount; localOrdinal++)
        {
            var localVariable = function.ParameterCount + localOrdinal;
            if (function.Body.Statements[localOrdinal] is not BoundLocal local
                || local.Variable != localVariable
                || local.Initializer is not BoundRead initializer
                || initializer.Type != operationType
                || initializer.Variable < 0
                || initializer.Variable >= localVariable
                || function.Variables[initializer.Variable].Type
                    != operationType)
            {
                throw Unsupported(
                    "A Construction-2 local-binary Body requires source-ordered numeric local copies from initialized Homes.");
            }

            localInitializers[localOrdinal] = initializer;
        }

        var leftRead = binary.Left as BoundRead;
        var rightRead = binary.Right as BoundRead;
        var leftConstant = binary.Left as BoundConstant;
        var rightConstant = binary.Right as BoundConstant;
        var leftReadIsValid = IsValidHomeRead(leftRead);
        var rightReadIsValid = IsValidHomeRead(rightRead);
        var leftConstantIsValid = IsValidConstant(leftConstant);
        var rightConstantIsValid = IsValidConstant(rightConstant);
        var leftType = leftRead?.Type ?? leftConstant?.Type;
        var rightType = rightRead?.Type ?? rightConstant?.Type;
        var hasSameNumericType
            = leftType == operationType && rightType == leftType;

        var isComparison
            = function.ReturnType == PrimitiveType.Bool
                && binary.Type == PrimitiveType.Bool
                && IsC1M1Construction2IntegerComparisonOperator(
                    binary.Operator)
                && hasSameNumericType
                && (leftReadIsValid || leftConstantIsValid)
                && (rightReadIsValid || rightConstantIsValid)
                && (leftReadIsValid || rightReadIsValid);
        var isIntegerValueOperation
            = function.ReturnType == operationType
                && binary.Type == operationType
                && hasSameNumericType
                && IsC1M1Construction2BodyIntegerType(operationType)
                && (binary.Operator is TokenKind.Ampersand
                    or TokenKind.Pipe or TokenKind.Caret or TokenKind.Plus
                    or TokenKind.Minus or TokenKind.Star
                    or TokenKind.Slash or TokenKind.Percent)
                && (leftReadIsValid && rightReadIsValid
                    || leftReadIsValid && rightConstantIsValid
                    || leftConstantIsValid && rightReadIsValid);
        var isFloatingValueOperation
            = function.ReturnType == operationType
                && binary.Type == operationType
                && hasSameNumericType
                && PrimitiveTypeFacts.IsFloating(operationType)
                && (binary.Operator is TokenKind.Plus or TokenKind.Minus
                    or TokenKind.Star or TokenKind.Slash or TokenKind.Percent)
                && (leftReadIsValid && rightReadIsValid
                    || leftReadIsValid && rightConstantIsValid
                    || leftConstantIsValid && rightReadIsValid);
        if ((!leftReadIsValid && !leftConstantIsValid)
            || (!rightReadIsValid && !rightConstantIsValid)
            || (!isComparison && !isIntegerValueOperation
                && !isFloatingValueOperation))
        {
            throw Unsupported(
                "A Construction-2 local-binary Body requires direct same-typed Home reads or one canonical constant.");
        }

        byte[] constantMap = [];
        if (leftConstantIsValid || rightConstantIsValid)
        {
            var constant = leftConstantIsValid
                ? leftConstant! : rightConstant!;
            var frozenValue = frozenScalarValueBuilder.Add(constant);
            var expectedTypeSpecRef
                = typeSpecReferenceFor(operationType);
            if (frozenValue.TypeSpecRef != expectedTypeSpecRef)
            {
                throw Unsupported(
                    "A Construction-2 local constant Body requires the exact operand TypeSpecRef.");
            }

            constantMap = new byte[8];
            BinaryPrimitives.WriteUInt32LittleEndian(constantMap,
                frozenValue.TypeSpecRef);
            BinaryPrimitives.WriteUInt32LittleEndian(
                constantMap.AsSpan(4), frozenValue.ConstantSourceRow);
        }

        var typeTokens = new uint[function.Variables.Length];
        var homeMap = new byte[checked(function.Variables.Length * 8)];
        for (var variable = 0; variable < function.Variables.Length;
            variable++)
        {
            typeTokens[variable] = CreateStandardBodyTypeMapToken(
                typeSpecReferenceFor(function.Variables[variable].Type));
        }

        var maps = TrRmRegisterBodyMapBuilder.Create(typeTokens, []);
        for (var variable = 0; variable < function.Variables.Length;
            variable++)
        {
            var offset = variable * 8;
            var role = !function.IsStatic && variable == 0 ? (byte)1
                : variable < function.ParameterCount ? (byte)2 : (byte)3;
            homeMap[offset] = role;
            BinaryPrimitives.WriteUInt16LittleEndian(
                homeMap.AsSpan(offset + 2, 2),
                maps.GetTypeMapIndex(typeTokens[variable]));
            BinaryPrimitives.WriteUInt16LittleEndian(
                homeMap.AsSpan(offset + 4, 2),
                role == 2
                    ? checked((ushort)(variable - receiverCount))
                    : (ushort)0);
        }

        var code = new List<byte>(checked(localCount * 8
            + (isComparison ? 18 : 14)));
        for (var localOrdinal = 0;
            localOrdinal < localCount; localOrdinal++)
        {
            var localVariable = function.ParameterCount + localOrdinal;
            AppendHomeLoad(code, 0,
                localInitializers[localOrdinal].Variable);
            var homeIndex = checked((ushort)localVariable);
            code.Add(0xD6);
            code.Add(0x10);
            code.Add((byte)homeIndex);
            code.Add((byte)(homeIndex >> 8));
        }

        AppendOperandLoad(0, leftRead, leftReadIsValid);
        AppendOperandLoad(1, rightRead, rightReadIsValid);
        const byte destinationRegister = 0;
        const byte leftRegister = 0;
        const byte rightRegister = 1;
        var payload = (ushort)(destinationRegister | (0x0F << 4)
            | (leftRegister << 8) | (rightRegister << 12));
        if (isComparison)
        {
            code.Add(0xC2);
            code.Add(0);
            code.Add((byte)payload);
            code.Add((byte)(payload >> 8));
            var predicatePayload = (ushort)(destinationRegister
                | (destinationRegister << 4) | (0x0F << 8)
                | (0x0F << 12));
            code.Add(0xC3);
            code.Add(GetC1M1Construction2IntegerComparisonPredicate(
                binary.Operator));
            code.Add((byte)predicatePayload);
            code.Add((byte)(predicatePayload >> 8));
        }
        else if (isFloatingValueOperation)
        {
            code.Add(0xC0);
            code.Add(GetC1M1Construction2FloatingBinaryControl(
                binary.Operator));
            code.Add((byte)payload);
            code.Add((byte)(payload >> 8));
        }
        else
        {
            var (opcode, control)
                = GetC1M1Construction2IntegerBinaryEncoding(
                    binary.Operator, binary.OverflowBehavior);
            code.Add(opcode);
            code.Add(control);
            code.Add((byte)payload);
            code.Add((byte)(payload >> 8));
        }
        code.Add(0x17);
        code.Add(0);

        ReadOnlyMemory<byte>[] records = [
            new byte[] { 1, 0, 0, 0, 0, 0, 0, 0 },
        ];
        return TrRmRegisterBodyWriter.Encode(homeMap, maps.TypeMap,
            default, default, constantMap,
            [new TrRmRegisterBodyBlock(0,
                checked((uint)code.Count), 0)],
            records, code.ToArray(), contractCount: contractCount,
            schema: schema,
            generalPredicateGraphsInitializationReferenceBodies
                : schema.UsesGeneralPredicateGraphs());

        bool IsValidHomeRead(BoundRead? read)
            => read is not null && read.Type == operationType
                && IsC1M1Construction2BodyNumericType(read.Type)
                && read.Variable >= receiverCount
                && read.Variable < function.Variables.Length
                && function.Variables[read.Variable].Type
                    == operationType;

        static bool IsValidConstant(BoundConstant? constant)
            => constant is not null
                && IsC1M1Construction2BodyNumericType(constant.Type)
                && constant.RepresentationType == constant.Type
                && constant.Bits.IsCanonicalFor(constant.Type);

        void AppendOperandLoad(byte destination, BoundRead? read,
            bool readIsValid)
        {
            if (readIsValid && read is not null)
            {
                AppendHomeLoad(code, destination, read.Variable);
            }
            else
            {
                code.Add(0xD4);
                code.Add(destination);
                code.Add(0);
                code.Add(0);
            }
        }

        static void AppendHomeLoad(List<byte> instructions,
            byte destination, int homeIndex)
        {
            instructions.Add(0xD5);
            instructions.Add(destination);
            instructions.Add(checked((byte)homeIndex));
            instructions.Add(checked((byte)(homeIndex >> 8)));
        }
    }

    private static byte[] EncodeC1M1Construction2IdentityForwarderBody(
        int targetMethodDefRow)
    {
        if (targetMethodDefRow <= 0 || targetMethodDefRow > 0xFF_FFFF)
        {
            throw new InvalidDataException(
                "A class identity-forwarder Body has an invalid MethodDef row.");
        }

        var body = new byte[8];
        body[0] = 0x05;
        BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(4),
            0x1600_0000u | checked((uint)targetMethodDefRow));
        return body;
    }

    private static byte[] EncodeC1M1Construction2InstanceMethodBody(
        BoundFunction function, int returnHomeIndex,
        Func<PrimitiveType, uint> typeSpecReferenceFor, int contractCount,
        TrRmArtifactSchema schema)
    {
        if (function.IsStatic
            || function.ParameterCount != function.Variables.Length
            || returnHomeIndex <= 0
            || returnHomeIndex >= function.ParameterCount
            || function.Variables[returnHomeIndex].Type
                != function.ReturnType)
        {
            throw Unsupported(
                "A Construction-2 instance ReturnHome must select a typed explicit value parameter.");
        }

        var typeTokens = new uint[function.ParameterCount];
        var homeMap = new byte[checked(function.ParameterCount * 8)];
        for (var parameter = 0; parameter < function.ParameterCount;
            parameter++)
        {
            var variable = function.Variables[parameter];
            if (variable.IsOut || variable.IsInit
                || variable.IsImplicitRef
                || variable.IsInitializationLocal)
            {
                throw Unsupported(
                    "A Construction-2 instance ReturnHome requires value receiver and parameter Homes.");
            }

            typeTokens[parameter] = CreateStandardBodyTypeMapToken(
                typeSpecReferenceFor(variable.Type));
        }

        var maps = TrRmRegisterBodyMapBuilder.Create(typeTokens, []);
        for (var parameter = 0; parameter < function.ParameterCount;
            parameter++)
        {
            var offset = parameter * 8;
            homeMap[offset] = parameter == 0 ? (byte)1 : (byte)2;
            BinaryPrimitives.WriteUInt16LittleEndian(
                homeMap.AsSpan(offset + 2, 2),
                maps.GetTypeMapIndex(typeTokens[parameter]));
            BinaryPrimitives.WriteUInt16LittleEndian(
                homeMap.AsSpan(offset + 4, 2),
                parameter == 0 ? (ushort)0
                    : checked((ushort)(parameter - 1)));
        }

        var records = new ReadOnlyMemory<byte>[] {
            new(new byte[] { 1, 0, 0, 0, 0, 0, 0, 0 }),
        };
        var code = new byte[] { 0xEB, 0, 0, 0 };
        BinaryPrimitives.WriteUInt16LittleEndian(code.AsSpan(2),
            checked((ushort)returnHomeIndex));
        return TrRmRegisterBodyWriter.Encode(homeMap, maps.TypeMap,
            default, default, default,
            [new TrRmRegisterBodyBlock(0,
                checked((uint)code.Length), 0)],
            records, code, contractCount: contractCount,
            schema: schema,
            generalPredicateGraphsInitializationReferenceBodies
                : schema.UsesGeneralPredicateGraphs());
    }

    private static byte[] EncodeC1M1Construction2NoOpInstanceMethodBody(
        BoundFunction function,
        Func<PrimitiveType, uint> typeSpecReferenceFor,
        int contractCount, TrRmArtifactSchema schema)
    {
        RequireC1M1Construction2NoOpUnitBody(function.Body);
        if (function.ParameterCount != function.Variables.Length
            || function.ParameterCount < 1)
        {
            throw Unsupported(
                "A Construction-2 unit instance method has an invalid receiver or parameter map.");
        }

        var typeTokens = new uint[function.ParameterCount];
        var homeMap = new byte[checked(function.ParameterCount * 8)];
        for (var parameter = 0; parameter < function.ParameterCount;
            parameter++)
        {
            var variable = function.Variables[parameter];
            if (variable.IsOut || variable.IsInit
                || variable.IsImplicitRef)
            {
                throw Unsupported(
                    "A Construction-2 unit instance method requires value-parameter Homes.");
            }

            var typeToken = CreateStandardBodyTypeMapToken(
                typeSpecReferenceFor(variable.Type));
            typeTokens[parameter] = typeToken;
        }

        var maps = TrRmRegisterBodyMapBuilder.Create(typeTokens, []);
        for (var parameter = 0; parameter < function.ParameterCount;
            parameter++)
        {
            var offset = parameter * 8;
            homeMap[offset] = parameter == 0 ? (byte)1 : (byte)2;
            BinaryPrimitives.WriteUInt16LittleEndian(
                homeMap.AsSpan(offset + 2, 2),
                maps.GetTypeMapIndex(typeTokens[parameter]));
            BinaryPrimitives.WriteUInt16LittleEndian(
                homeMap.AsSpan(offset + 4, 2),
                parameter == 0 ? (ushort)0
                    : checked((ushort)(parameter - 1)));
        }

        var records = new ReadOnlyMemory<byte>[] {
            new(new byte[] { 1, 0, 0, 0, 0, 0, 0, 0 }),
        };
        var code = new byte[] { 0x18, 0 };
        return TrRmRegisterBodyWriter.Encode(homeMap, maps.TypeMap,
            default, default, default,
            [new TrRmRegisterBodyBlock(0,
                checked((uint)code.Length), 0)],
            records, code, contractCount: contractCount,
            schema: schema,
            generalPredicateGraphsInitializationReferenceBodies
                : schema.UsesGeneralPredicateGraphs());
    }

    private static C1M1Construction2ConditionalReturn?
        TryGetC1M1Construction2ConditionalReturn(BoundFunction function)
    {
        var receiverCount = function.IsStatic ? 0 : 1;
        if (function.ParameterCount < receiverCount
            || function.ParameterCount != function.Variables.Length
            || function.Body.Statements.Length != 1
            || function.Body.Statements[0] is not BoundIf root
            || !function.Body.RetiredManagedLocals.IsEmpty)
        {
            return null;
        }

        if (receiverCount != 0)
        {
            var receiver = function.Variables[0];
            if (ManagedClassTypes.IsOptional(receiver.Type)
                || !ManagedClassTypes.IsClass(receiver.Type)
                || receiver.IsMutable || receiver.IsOut
                || receiver.IsInit || receiver.IsImplicitRef
                || receiver.IsInitializationLocal)
            {
                return null;
            }
        }

        if (function.ReturnType != PrimitiveType.Bool
            && !IsC1M1Construction2BodyNumericType(function.ReturnType))
        {
            return null;
        }

        for (var parameter = 0; parameter < function.ParameterCount;
            parameter++)
        {
            if (parameter >= receiverCount
                && !IsScalarParameter(parameter))
            {
                return null;
            }
        }

        var pending = new Stack<BoundStatement>();
        pending.Push(root);
        var visits = 0;
        while (pending.Count != 0)
        {
            if (++visits > SemanticVerifier.MaximumAnalysisVisits)
            {
                return null;
            }

            var statement = pending.Pop();
            while (statement is BoundBlock block)
            {
                if (++visits > SemanticVerifier.MaximumAnalysisVisits
                    || !block.RetiredManagedLocals.IsEmpty
                    || block.Statements.Length != 1)
                {
                    return null;
                }

                statement = block.Statements[0];
            }

            if (statement is BoundIf branch)
            {
                if (branch.Else is null
                    || !branch.GenericUniquePropertyWhenTrue.IsEmpty
                    || !branch.GenericUniquePropertyWhenFalse.IsEmpty
                    || branch.GenericUniquePropertyKnownResult is not null
                    || branch.Condition.Type != PrimitiveType.Bool
                    || !IsSupportedCondition(branch.Condition))
                {
                    return null;
                }

                pending.Push(branch.Else);
                pending.Push(branch.Then);
                continue;
            }

            if (statement is BoundReturn { Value: not null } returned
                && returned.Value.Type == function.ReturnType
                && IsScalarReturn(returned.Value))
            {
                continue;
            }

            return null;
        }

        return new C1M1Construction2ConditionalReturn(root);

        bool IsScalarParameter(int variableIndex)
        {
            if ((uint)variableIndex >= (uint)function.ParameterCount
                || variableIndex < receiverCount)
            {
                return false;
            }

            var variable = function.Variables[variableIndex];
            return (variable.Type == PrimitiveType.Bool
                    || IsC1M1Construction2BodyNumericType(variable.Type))
                && !variable.IsMutable
                && !variable.IsOut
                && !variable.IsInit
                && !variable.IsImplicitRef
                && !variable.IsInitializationLocal;
        }

        bool IsScalarRead(BoundRead read)
            => IsScalarParameter(read.Variable)
                && function.Variables[read.Variable].Type == read.Type;

        bool IsSupportedCondition(BoundExpression expression)
        {
            if (expression is BoundRead read)
            {
                return IsScalarRead(read)
                    && read.Type == PrimitiveType.Bool;
            }

            return expression is BoundBinary {
                       Left: BoundRead left,
                       Right: BoundRead right,
                   } comparison
                && comparison.Type == PrimitiveType.Bool
                && IsScalarRead(left)
                && IsScalarRead(right)
                && ((left.Type == PrimitiveType.Bool
                        && right.Type == PrimitiveType.Bool
                        && comparison.Operator is (
                            TokenKind.EqualsEquals
                            or TokenKind.BangEquals))
                    || (left.Type == right.Type
                        && IsC1M1Construction2BodyNumericType(left.Type)
                        && IsC1M1Construction2IntegerComparisonOperator(
                            comparison.Operator)));
        }

        bool IsScalarReturn(BoundExpression value)
        {
            if (value is BoundRead read)
            {
                return IsScalarRead(read);
            }

            if (value is BoundConstant constant)
            {
                return IsScalarConstant(constant, function.ReturnType);
            }

            if (value is BoundBinary binary)
            {
                return IsScalarBinaryReturn(binary);
            }

            if (value is BoundUnary unary)
            {
                return IsScalarUnaryReturn(unary);
            }

            return false;
        }

        static bool IsScalarConstant(BoundConstant constant,
            PrimitiveType type)
            => constant.Type == type
                && constant.RepresentationType == type
                && constant.Bits.IsCanonicalFor(type);

        bool IsScalarNumericOperand(BoundExpression value,
            PrimitiveType type)
            => value is BoundRead read
                ? read.Type == type && IsScalarRead(read)
                : value is BoundConstant constant
                    && IsScalarConstant(constant, type);

        bool IsScalarUnaryReturn(BoundUnary unary)
        {
            if (unary.Type != function.ReturnType
                || !IsC1M1Construction2BodyNumericType(unary.Type)
                || unary.Operand is not BoundRead operand
                || operand.Type != unary.Type
                || !IsScalarRead(operand))
            {
                return false;
            }

            if (PrimitiveTypeFacts.IsFloating(unary.Type))
            {
                return unary.Operator == TokenKind.Minus;
            }

            return unary.Operator switch
            {
                TokenKind.Tilde => true,
                TokenKind.Minus => unary.OverflowBehavior is
                    BoundIntegerOverflowBehavior.Checked
                    or BoundIntegerOverflowBehavior.Wrapping,
                _ => false,
            };
        }

        bool IsScalarBinaryReturn(BoundBinary binary)
        {
            if (binary.Type != function.ReturnType
                || !IsC1M1Construction2BodyNumericType(binary.Type)
                || !IsScalarNumericOperand(binary.Left, binary.Type)
                || !IsScalarNumericOperand(binary.Right, binary.Type)
                || (binary.Left is not BoundRead
                    && binary.Right is not BoundRead))
            {
                return false;
            }

            if (PrimitiveTypeFacts.IsFloating(binary.Type))
            {
                return binary.Operator is TokenKind.Plus
                    or TokenKind.Minus or TokenKind.Star or TokenKind.Slash
                    or TokenKind.Percent;
            }

            if (binary.Operator is TokenKind.Ampersand
                or TokenKind.Pipe or TokenKind.Caret)
            {
                return true;
            }

            return binary.Operator switch
            {
                TokenKind.Plus or TokenKind.Minus or TokenKind.Star
                    => binary.OverflowBehavior is
                        BoundIntegerOverflowBehavior.Checked
                        or BoundIntegerOverflowBehavior.Wrapping,
                TokenKind.Slash or TokenKind.Percent
                    => binary.OverflowBehavior
                        == BoundIntegerOverflowBehavior.Checked,
                _ => false,
            };
        }

        static BoundExpression? GetSingleScalarReturn(
            BoundStatement statement)
        {
            if (statement is BoundBlock block)
            {
                if (!block.RetiredManagedLocals.IsEmpty
                    || block.Statements.Length != 1)
                {
                    return null;
                }

                statement = block.Statements[0];
            }

            return statement switch
            {
                BoundReturn { Value: BoundRead read } => read,
                BoundReturn { Value: BoundConstant constant } => constant,
                BoundReturn { Value: BoundBinary binary } => binary,
                BoundReturn { Value: BoundUnary unary } => unary,
                _ => null,
            };
        }
    }

    private static bool TryGetC1M1Construction2DirectFieldRead(
        BoundFunction function, out int fieldIndex)
    {
        fieldIndex = -1;
        if (function.IsStatic || function.ParameterCount != 1
            || function.Variables.Length != 1
            || function.Variables[0].IsMutable
            || function.ReturnType == PrimitiveType.Void)
        {
            return false;
        }

        BoundFieldRead? returnedField = null;
        var isSupported = true;
        Visit(function.Body);
        if (!isSupported || returnedField is null
            || returnedField.Receiver is not BoundRead { Variable: 0 }
            || returnedField.Type != function.ReturnType)
        {
            return false;
        }

        fieldIndex = returnedField.Field;
        return fieldIndex >= 0;

        void Visit(BoundBlock block)
        {
            if (!block.RetiredManagedLocals.IsEmpty)
            {
                isSupported = false;
                return;
            }

            foreach (var statement in block.Statements)
            {
                if (!isSupported || returnedField is not null)
                {
                    isSupported = false;
                    return;
                }

                if (statement is BoundBlock nested)
                {
                    Visit(nested);
                }
                else if (statement is BoundReturn {
                    Value: BoundFieldRead fieldRead
                })
                {
                    returnedField = fieldRead;
                }
                else
                {
                    isSupported = false;
                    return;
                }
            }
        }
    }

    private static C1M1Construction2FieldReference?
        TryGetC1M1Construction2FieldReference(
            BoundProgram program, BoundFunction function,
            int receiverClassIndex,
            IReadOnlyDictionary<BoundGenericNominalTypeIdentity, int>
                standardClassIndexByIdentity,
            IReadOnlyDictionary<int, C1M1ImportedClassImportPlan>
                typeImportPlanByStandardClassView,
            IReadOnlyDictionary<(int ClassIndex, int FieldIndex), int>
                fieldDefRowByClassAndIndex,
            IReadOnlyDictionary<(int StandardClassViewIndex, int FieldIndex),
                int> fieldDefRowByStandardClassViewAndIndex,
            IReadOnlyDictionary<(int StandardClassViewIndex, int FieldIndex),
                int> fieldImportRowByStandardClassViewAndIndex,
            IReadOnlyList<C1M1ImportedClassTypeDefViewPlan>
                standardClassTypeDefViews,
            Func<int, int, int> getStandardClassTypeDefViewIndex)
    {
        if (function.ParameterCount != function.Variables.Length
            || function.Body.RetiredManagedLocals.Length != 0
            || function.Body.Statements is not [
                BoundReturn {
                    Value: BoundReferenceFormation {
                        Place: BoundFieldRead fieldRead
                    } formation
                }
            ]
            || formation.Type != function.ReturnType
            || !IsC1M1NoOpReadonlyScalarReferenceType(function.ReturnType,
                program.CheckedReferenceTypes))
        {
            return null;
        }

        if (function.DependencyContracts.Length != 1)
        {
            return null;
        }

        var dependency = function.DependencyContracts[0];
        if (dependency.Relation != BoundDependencyRelation.DependsOn
            || dependency.Destination != BoundDependencyDestination.Result
            || dependency.DestinationOrdinal is not null
            || dependency.Sources.Length != 1)
        {
            return null;
        }

        var source = dependency.Sources[0];
        var sourceVariableIndex = source.Kind switch
        {
            BoundDependencySourceKind.Receiver
                when !function.IsStatic
                    && source.FormalOrdinal is null
                    && function.ParameterCount == 1 => 0,
            BoundDependencySourceKind.Formal
                when source.FormalOrdinal == 0
                    && function.ParameterCount
                        == (function.IsStatic ? 1 : 2)
                    => function.IsStatic ? 0 : 1,
            _ => -1,
        };
        if (sourceVariableIndex < 0
            || fieldRead.Receiver is not BoundRead {
                Variable: var variableIndex
            }
            || variableIndex != sourceVariableIndex)
        {
            return null;
        }

        var reference = program.CheckedReferenceTypes[
            CheckedReferenceTypes.Index(function.ReturnType)];
        if (reference.ReferentType != fieldRead.Type)
        {
            return null;
        }

        var fieldClassIndex = -1;
        var fieldDefRow = -1;
        if (source.Kind == BoundDependencySourceKind.Receiver)
        {
            fieldClassIndex = receiverClassIndex;
        }
        else if (!TryGetC1M1SourceClassFieldType(fieldRead.Receiver.Type,
                out fieldClassIndex, out _, out var isOptional)
            || isOptional)
        {
            fieldClassIndex = -1;
        }

        if ((uint)fieldClassIndex < (uint)program.Classes.Length
            && (uint)fieldRead.Field
                < (uint)program.Classes[fieldClassIndex].Fields.Length
            && program.Classes[fieldClassIndex].Fields[fieldRead.Field].Type
                == reference.ReferentType
            && fieldDefRowByClassAndIndex.TryGetValue((
                fieldClassIndex, fieldRead.Field), out fieldDefRow))
        {
            return new C1M1Construction2FieldReference(
                0x1400_0000u | checked((uint)fieldDefRow));
        }

        if (!TryGetC1M1ImportedClassFieldType(fieldRead.Receiver.Type,
                program, standardClassIndexByIdentity,
                out var standardClassIndex, out var instanceIndex))
        {
            return null;
        }

        var standardClassViewIndex = getStandardClassTypeDefViewIndex(
            standardClassIndex, instanceIndex);
        if (!typeImportPlanByStandardClassView.TryGetValue(
                standardClassViewIndex, out var importPlan)
            || !fieldDefRowByStandardClassViewAndIndex.ContainsKey((
                standardClassViewIndex, fieldRead.Field))
            || !fieldImportRowByStandardClassViewAndIndex.TryGetValue((
                standardClassViewIndex, fieldRead.Field),
                out var fieldImportRow))
        {
            return null;
        }

        var classDeclaration
            = standardClassTypeDefViews[standardClassViewIndex]
                .Declaration;
        var providerClassDeclaration
            = program.StandardClassDeclarations[standardClassIndex]
                .Declaration;
        if ((uint)fieldRead.Field >= (uint)classDeclaration.Fields.Length
            || (uint)fieldRead.Field
                >= (uint)providerClassDeclaration.Fields.Length)
        {
            return null;
        }

        var field = classDeclaration.Fields[fieldRead.Field];
        var providerField
            = providerClassDeclaration.Fields[fieldRead.Field];
        if (field.Type != reference.ReferentType
            || !TryGetC1M1SourceScalarTypeSpecReference(field.Type, out _)
            || !IsC1M1ImportedClassFieldImportable(
                providerClassDeclaration, providerField, importPlan))
        {
            return null;
        }

        return new C1M1Construction2FieldReference(
            0x1C00_0000u | checked((uint)fieldImportRow));
    }

    private static C1M1Construction2NestedFieldRead?
        TryGetC1M1Construction2NestedFieldRead(
            BoundProgram program, BoundFunction function, int classIndex,
            IReadOnlyDictionary<int, BoundSourceTypeDeclaration>
                sourceStructDeclarations,
            IReadOnlyDictionary<int, C1M1ImportedStructImportPlan>
                typeImportPlanByStructure,
            IReadOnlyDictionary<BoundGenericNominalTypeIdentity, int>
                standardClassIndexByIdentity,
            IReadOnlyDictionary<int, C1M1ImportedClassImportPlan>
                typeImportPlanByStandardClassView,
            IReadOnlyDictionary<(int ClassIndex, int FieldIndex), int>
                fieldDefRowByClassAndIndex,
            IReadOnlyDictionary<(int StandardClassViewIndex, int FieldIndex),
                int> fieldDefRowByStandardClassViewAndIndex,
            IReadOnlyDictionary<(int StructureIndex, int FieldIndex), int>
                fieldDefRowByStructureAndIndex,
            IReadOnlyList<C1M1ImportedClassTypeDefViewPlan>
                standardClassTypeDefViews,
            Func<int, int, int> getStandardClassTypeDefViewIndex)
    {
        if (function.IsStatic
            || function.ParameterCount != function.Variables.Length
            || function.ParameterCount is < 1 or > 2
            || function.Variables[0].IsMutable
            || function.ReturnType == PrimitiveType.Void
            || !function.Body.RetiredManagedLocals.IsEmpty)
        {
            return null;
        }

        BoundFieldRead? returnedField = null;
        var pendingBlocks = new Stack<BoundBlock>();
        pendingBlocks.Push(function.Body);
        var visits = 0;
        while (pendingBlocks.Count != 0)
        {
            var block = pendingBlocks.Pop();
            if (++visits > SemanticVerifier.MaximumAnalysisVisits
                || !block.RetiredManagedLocals.IsEmpty)
            {
                return null;
            }

            foreach (var statement in block.Statements)
            {
                if (++visits > SemanticVerifier.MaximumAnalysisVisits
                    || returnedField is not null)
                {
                    return null;
                }

                if (statement is BoundBlock nested)
                {
                    pendingBlocks.Push(nested);
                }
                else if (statement is BoundReturn {
                    Value: BoundFieldRead fieldRead
                })
                {
                    returnedField = fieldRead;
                }
                else
                {
                    return null;
                }
            }
        }

        if (returnedField is null
            || returnedField.Type != function.ReturnType
            || !TryGetC1M1SourceScalarTypeSpecReference(
                function.ReturnType, out _))
        {
            return null;
        }

        var fieldPath = new List<BoundFieldRead>();
        BoundExpression root = returnedField;
        while (root is BoundFieldRead fieldRead)
        {
            if (fieldPath.Count
                >= SemanticVerifier.MaximumAnalysisVisits)
            {
                return null;
            }

            fieldPath.Add(fieldRead);
            root = fieldRead.Receiver;
        }

        if (root is not BoundRead receiver
            || (uint)receiver.Variable
                >= (uint)function.ParameterCount
            || receiver.Type != function.Variables[receiver.Variable].Type)
        {
            return null;
        }

        var baseVariableIndex = receiver.Variable;
        var standardClassIndex = -1;
        var standardClassInstanceIndex = -1;
        if (baseVariableIndex == 0)
        {
            if (function.ParameterCount != 1 || fieldPath.Count < 2
                || ManagedClassTypes.IsOptional(receiver.Type)
                || !ManagedClassTypes.IsClass(receiver.Type)
                || ManagedClassTypes.ClassIndex(receiver.Type) != classIndex)
            {
                return null;
            }
        }
        else if (baseVariableIndex == 1
            && function.ParameterCount == 2
            && TryGetC1M1ImportedClassFieldType(receiver.Type, program,
                standardClassIndexByIdentity, out standardClassIndex,
                out standardClassInstanceIndex))
        {
            if (fieldPath.Count == 0)
            {
                return null;
            }
        }
        else
        {
            return null;
        }

        fieldPath.Reverse();
        var fieldDefRows = ImmutableArray.CreateBuilder<int>(
            fieldPath.Count);
        var fieldTypes = ImmutableArray.CreateBuilder<PrimitiveType>(
            fieldPath.Count);
        for (var index = 0; index < fieldPath.Count; index++)
        {
            var fieldRead = fieldPath[index];
            var ownerType = fieldRead.Receiver.Type;
            int fieldDefRow;
            PrimitiveType fieldType;
            if (index == 0 && baseVariableIndex == 0)
            {
                if (!ManagedClassTypes.IsClass(ownerType)
                    || ManagedClassTypes.IsOptional(ownerType)
                    || ManagedClassTypes.ClassIndex(ownerType)
                        != classIndex
                    || (uint)fieldRead.Field
                        >= (uint)program.Classes[classIndex].Fields.Length
                    || !fieldDefRowByClassAndIndex.TryGetValue((
                        classIndex, fieldRead.Field), out fieldDefRow))
                {
                    return null;
                }

                fieldType
                    = program.Classes[classIndex].Fields[fieldRead.Field]
                        .Type;
            }
            else if (index == 0)
            {
                var standardClassViewIndex
                    = getStandardClassTypeDefViewIndex(standardClassIndex,
                        standardClassInstanceIndex);
                var classViewPlan
                    = standardClassTypeDefViews[standardClassViewIndex];
                var classFields = classViewPlan.Declaration.Fields;
                var providerClassFields
                    = program.StandardClassDeclarations[standardClassIndex]
                        .Declaration.Fields;
                if (!TryGetC1M1ImportedClassFieldType(ownerType, program,
                        standardClassIndexByIdentity,
                        out var fieldOwnerStandardClassIndex,
                        out var fieldOwnerInstanceIndex)
                    || fieldOwnerStandardClassIndex != standardClassIndex
                    || getStandardClassTypeDefViewIndex(
                        fieldOwnerStandardClassIndex,
                        fieldOwnerInstanceIndex) != standardClassViewIndex
                    || (uint)fieldRead.Field >= (uint)classFields.Length
                    || (uint)fieldRead.Field
                        >= (uint)providerClassFields.Length
                    || !typeImportPlanByStandardClassView.TryGetValue(
                        standardClassViewIndex, out var importPlan)
                    || !IsC1M1ImportedClassFieldImportable(
                        program.StandardClassDeclarations[
                            standardClassIndex].Declaration,
                        providerClassFields[fieldRead.Field], importPlan)
                    || !fieldDefRowByStandardClassViewAndIndex.TryGetValue((
                        standardClassViewIndex, fieldRead.Field),
                        out fieldDefRow))
                {
                    return null;
                }

                fieldType = classFields[fieldRead.Field].Type;
            }
            else
            {
                if (!TryGetC1M1SourceStructFieldType(ownerType,
                        sourceStructDeclarations,
                        out var structureIndex)
                    || (uint)structureIndex
                        >= (uint)program.Structures.Length
                    || (uint)fieldRead.Field
                        >= (uint)program.Structures[structureIndex]
                            .Fields.Length
                    || !sourceStructDeclarations.TryGetValue(
                        structureIndex, out var declaration)
                    || !IsC1M1SourceStructLayoutSupported(
                        program.Structures[structureIndex], declaration)
                    || (program.Structures[structureIndex]
                            .StandardTypeOrigin is not null
                        && (!typeImportPlanByStructure.TryGetValue(
                                structureIndex, out var importPlan)
                            || !IsC1M1ImportedStructFieldImportable(
                                program.Structures[structureIndex],
                                program.Structures[structureIndex]
                                    .Fields[fieldRead.Field],
                                importPlan)))
                    || !fieldDefRowByStructureAndIndex.TryGetValue((
                        structureIndex, fieldRead.Field),
                        out fieldDefRow))
                {
                    return null;
                }

                fieldType = program.Structures[structureIndex]
                    .Fields[fieldRead.Field].Type;
            }

            if (fieldRead.Type != fieldType)
            {
                return null;
            }

            if (index + 1 < fieldPath.Count
                && (!TryGetC1M1SourceStructFieldType(fieldType,
                        sourceStructDeclarations,
                        out var nestedStructureIndex)
                    || (uint)nestedStructureIndex
                        >= (uint)program.Structures.Length
                    || !sourceStructDeclarations.TryGetValue(
                        nestedStructureIndex, out var nestedDeclaration)
                    || !IsC1M1SourceStructLayoutSupported(
                        program.Structures[nestedStructureIndex],
                        nestedDeclaration)
                    || fieldPath[index + 1].Receiver.Type != fieldType))
            {
                return null;
            }

            fieldDefRows.Add(fieldDefRow);
            fieldTypes.Add(fieldType);
        }

        return new C1M1Construction2NestedFieldRead(
            fieldDefRows.MoveToImmutable(),
            fieldTypes.MoveToImmutable(), baseVariableIndex);
    }

    private static bool TryGetC1M1Construction2DirectFieldWrite(
        BoundFunction function, out int fieldIndex)
    {
        fieldIndex = -1;
        if (function.IsStatic || function.ReturnType != PrimitiveType.Void
            || function.ParameterCount != 2
            || function.Variables.Length != 2
            || !function.Variables[0].IsMutable)
        {
            return false;
        }

        BoundFieldAssignment? assignment = null;
        var returned = false;
        var isSupported = true;
        Visit(function.Body);
        if (!isSupported || assignment is null || !returned
            || assignment.Variable != 0
            || !assignment.NestedFields.IsEmpty
            || assignment.Value is not BoundRead value
            || value.Variable != 1
            || value.Type != function.Variables[1].Type)
        {
            return false;
        }

        fieldIndex = assignment.Field;
        return fieldIndex >= 0;

        void Visit(BoundBlock block)
        {
            if (!block.RetiredManagedLocals.IsEmpty)
            {
                isSupported = false;
                return;
            }

            foreach (var statement in block.Statements)
            {
                if (!isSupported || returned)
                {
                    isSupported = false;
                    return;
                }

                if (statement is BoundBlock nested)
                {
                    Visit(nested);
                }
                else if (statement is BoundFieldAssignment fieldWrite
                    && assignment is null)
                {
                    assignment = fieldWrite;
                }
                else if (statement is BoundReturn { Value: null }
                    && assignment is not null)
                {
                    returned = true;
                }
                else
                {
                    isSupported = false;
                    return;
                }
            }
        }
    }

    private static C1M1Construction2NestedFieldWrite?
        TryGetC1M1Construction2ImportedClassFieldWrite(
            BoundProgram program, BoundFunction function,
            IReadOnlyDictionary<BoundGenericNominalTypeIdentity, int>
                standardClassIndexByIdentity,
            IReadOnlyDictionary<int, C1M1ImportedClassImportPlan>
                typeImportPlanByStandardClassView,
            IReadOnlyDictionary<(int StandardClassViewIndex, int FieldIndex),
                int> fieldDefRowByStandardClassViewAndIndex,
            IReadOnlyDictionary<(int StandardClassViewIndex, int FieldIndex),
                int> fieldImportRowByStandardClassViewAndIndex,
            IReadOnlyList<C1M1ImportedClassTypeDefViewPlan>
                standardClassTypeDefViews,
            Func<int, int, int> getStandardClassTypeDefViewIndex)
    {
        if (!function.IsStatic || function.ReturnType != PrimitiveType.Void
            || function.ParameterCount is < 1 or > 2
            || function.Variables.Length != function.ParameterCount
            || !function.Body.RetiredManagedLocals.IsEmpty)
        {
            return null;
        }

        BoundFieldAssignment? assignment = null;
        var returned = false;
        var isSupported = true;
        Visit(function.Body);
        if (!isSupported || assignment is null
            || assignment.Variable != 0
            || !assignment.NestedFields.IsEmpty
            || assignment.NestedFields.IsDefault
            || assignment.IsUnsafeAuthorized
            || !returned
            || !TryGetC1M1ImportedClassFieldType(
                function.Variables[0].Type, program,
                standardClassIndexByIdentity,
                out var standardClassIndex, out var instanceIndex))
        {
            return null;
        }

        var standardClassViewIndex = getStandardClassTypeDefViewIndex(
            standardClassIndex, instanceIndex);
        if (!typeImportPlanByStandardClassView.TryGetValue(
                standardClassViewIndex, out var importPlan))
        {
            return null;
        }

        if (assignment.Value is BoundRead read)
        {
            if (function.ParameterCount != 2 || read.Variable != 1
                || read.Type != function.Variables[1].Type)
            {
                return null;
            }
        }
        else if (function.ParameterCount != 1
            || assignment.Value is not BoundConstant)
        {
            return null;
        }

        var classDeclaration
            = standardClassTypeDefViews[standardClassViewIndex]
                .Declaration;
        var providerClassDeclaration
            = program.StandardClassDeclarations[standardClassIndex]
                .Declaration;
        var classTypeInstance
            = program.GenericNominalTypeInstances[instanceIndex];
        if ((classTypeInstance.Qualifiers & 0x01) == 0
            || (classDeclaration.TypeDefFlags & 0x80) == 0
            || (uint)assignment.Field >= (uint)classDeclaration.Fields.Length
            || (uint)assignment.Field
                >= (uint)providerClassDeclaration.Fields.Length)
        {
            return null;
        }

        var field = classDeclaration.Fields[assignment.Field];
        if ((field.Flags & 3) != 2 || (field.Flags & 0x08) != 0
            || (field.Flags & 0x04) == 0
            || field.Type != assignment.Value.Type
            || !TryGetC1M1SourceScalarTypeSpecReference(field.Type, out _)
            || !IsC1M1ImportedClassFieldImportable(
                providerClassDeclaration,
                providerClassDeclaration.Fields[assignment.Field],
                importPlan)
            || !fieldImportRowByStandardClassViewAndIndex.ContainsKey((
                standardClassViewIndex, assignment.Field))
            || !fieldDefRowByStandardClassViewAndIndex.TryGetValue((
                standardClassViewIndex, assignment.Field),
                out var fieldDefRow))
        {
            return null;
        }

        return new C1M1Construction2NestedFieldWrite(
            [fieldDefRow], [field.Type], 0, assignment.Value);

        void Visit(BoundBlock block)
        {
            if (!block.RetiredManagedLocals.IsEmpty)
            {
                isSupported = false;
                return;
            }

            foreach (var statement in block.Statements)
            {
                if (!isSupported || returned)
                {
                    isSupported = false;
                    return;
                }

                if (statement is BoundBlock nested)
                {
                    Visit(nested);
                }
                else if (statement is BoundFieldAssignment fieldWrite
                    && assignment is null)
                {
                    assignment = fieldWrite;
                }
                else if (statement is BoundReturn { Value: null }
                    && assignment is not null)
                {
                    returned = true;
                }
                else
                {
                    isSupported = false;
                    return;
                }
            }
        }
    }

    private static C1M1Construction2NestedFieldWrite?
        TryGetC1M1Construction2NestedFieldWrite(
            BoundProgram program, BoundFunction function, int classIndex,
            IReadOnlyDictionary<int, BoundSourceTypeDeclaration>
                sourceStructDeclarations,
            IReadOnlyDictionary<int, C1M1ImportedStructImportPlan>
                typeImportPlanByStructure,
            IReadOnlyDictionary<(int ClassIndex, int FieldIndex), int>
                fieldDefRowByClassAndIndex,
            IReadOnlyDictionary<(int StructureIndex, int FieldIndex), int>
                fieldDefRowByStructureAndIndex)
    {
        if (function.IsStatic || function.ReturnType != PrimitiveType.Void
            || function.ParameterCount != 2
            || function.Variables.Length != 2
            || !function.Variables[0].IsMutable
            || !function.Body.RetiredManagedLocals.IsEmpty)
        {
            return null;
        }

        BoundFieldAssignment? assignment = null;
        var returned = false;
        var isSupported = true;
        Visit(function.Body);
        if (!isSupported || assignment is null
            || assignment.Variable != 0
            || assignment.NestedFields.IsDefaultOrEmpty
            || assignment.IsUnsafeAuthorized
            || assignment.Value is not BoundRead value
            || value.Variable != 1
            || value.Type != function.Variables[1].Type
            || !returned)
        {
            return null;
        }

        var fieldCount = checked(assignment.NestedFields.Length + 1);
        if (fieldCount > SemanticVerifier.MaximumAnalysisVisits
            || !ManagedClassTypes.IsClass(function.Variables[0].Type)
            || ManagedClassTypes.IsOptional(function.Variables[0].Type)
            || ManagedClassTypes.ClassIndex(function.Variables[0].Type)
                != classIndex)
        {
            return null;
        }

        var fieldDefRows = ImmutableArray.CreateBuilder<int>(fieldCount);
        var fieldTypes
            = ImmutableArray.CreateBuilder<PrimitiveType>(fieldCount);
        var ownerType = function.Variables[0].Type;
        for (var index = 0; index < fieldCount; index++)
        {
            var fieldIndex = index == 0 ? assignment.Field
                : assignment.NestedFields[index - 1];
            int fieldDefRow;
            BoundStructField field;
            if (index == 0)
            {
                if ((uint)fieldIndex
                        >= (uint)program.Classes[classIndex].Fields.Length
                    || !fieldDefRowByClassAndIndex.TryGetValue((
                        classIndex, fieldIndex), out fieldDefRow))
                {
                    return null;
                }

                field = program.Classes[classIndex].Fields[fieldIndex];
            }
            else
            {
                if (!TryGetC1M1SourceStructFieldType(ownerType,
                        sourceStructDeclarations,
                        out var structureIndex)
                    || (uint)structureIndex
                        >= (uint)program.Structures.Length
                    || (uint)fieldIndex
                        >= (uint)program.Structures[structureIndex]
                            .Fields.Length
                    || !sourceStructDeclarations.TryGetValue(
                        structureIndex, out var declaration)
                    || !IsC1M1SourceStructLayoutSupported(
                        program.Structures[structureIndex], declaration)
                    || (program.Structures[structureIndex]
                            .StandardTypeOrigin is not null
                        && (!typeImportPlanByStructure.TryGetValue(
                                structureIndex, out var importPlan)
                            || !IsC1M1ImportedStructFieldImportable(
                                program.Structures[structureIndex],
                                program.Structures[structureIndex]
                                    .Fields[fieldIndex],
                                importPlan)))
                    || !fieldDefRowByStructureAndIndex.TryGetValue((
                        structureIndex, fieldIndex), out fieldDefRow))
                {
                    return null;
                }

                field = program.Structures[structureIndex]
                    .Fields[fieldIndex];
            }

            if (field is null || !field.IsMutable
                || field.RequiresUnsafeCaller)
            {
                return null;
            }

            var fieldType = field.Type;
            if (index + 1 < fieldCount
                && (!TryGetC1M1SourceStructFieldType(fieldType,
                        sourceStructDeclarations,
                        out var nestedStructureIndex)
                    || (uint)nestedStructureIndex
                        >= (uint)program.Structures.Length
                    || !sourceStructDeclarations.TryGetValue(
                        nestedStructureIndex, out var nestedDeclaration)
                    || !IsC1M1SourceStructLayoutSupported(
                        program.Structures[nestedStructureIndex],
                        nestedDeclaration)))
            {
                return null;
            }

            fieldDefRows.Add(fieldDefRow);
            fieldTypes.Add(fieldType);
            ownerType = fieldType;
        }

        if (fieldTypes[^1] != value.Type
            || !TryGetC1M1SourceScalarTypeSpecReference(value.Type, out _))
        {
            return null;
        }

        return new C1M1Construction2NestedFieldWrite(
            fieldDefRows.MoveToImmutable(),
            fieldTypes.MoveToImmutable(), 0, value);

        void Visit(BoundBlock block)
        {
            if (!block.RetiredManagedLocals.IsEmpty)
            {
                isSupported = false;
                return;
            }

            foreach (var statement in block.Statements)
            {
                if (!isSupported || returned)
                {
                    isSupported = false;
                    return;
                }

                if (statement is BoundBlock nested)
                {
                    Visit(nested);
                }
                else if (statement is BoundFieldAssignment fieldWrite
                    && assignment is null)
                {
                    assignment = fieldWrite;
                }
                else if (statement is BoundReturn { Value: null }
                    && assignment is not null)
                {
                    returned = true;
                }
                else
                {
                    isSupported = false;
                    return;
                }
            }
        }
    }

    private static bool TryGetC1M1Construction2IdentityForwarder(
        BoundFunction function, ImmutableArray<BoundFunction> functions,
        out int targetFunctionIndex)
    {
        targetFunctionIndex = -1;
        if (function.ReturnType == PrimitiveType.Void
            || function.ParameterCount != function.Variables.Length
            || (!function.IsStatic && (function.ParameterCount == 0
                || function.Variables[0].IsMutable)))
        {
            return false;
        }

        BoundCall? returnedCall = null;
        var isSupported = true;
        Visit(function.Body);
        if (!isSupported || returnedCall is null
            || returnedCall.Type != function.ReturnType
            || returnedCall.Arguments.Length != function.ParameterCount
            || !returnedCall.DefaultedArguments.IsDefaultOrEmpty
            || returnedCall.InterfaceCallIdentity is not null
            || returnedCall.GenericMethodCall is not null
            || returnedCall.ImportedGenericMethodCall is not null
            || (uint)returnedCall.Function >= (uint)functions.Length)
        {
            return false;
        }

        var target = functions[returnedCall.Function];
        if (target.IsStatic != function.IsStatic || target.IsExternal
            || target.IsClassConstructor || target.IsConstructor
            || target.IsCleanup || target.IsOperator
            || target.ModuleImport is not null
            || target.GenericMethodInstantiation is not null
            || !StringComparer.Ordinal.Equals(target.DeclaringStruct,
                function.DeclaringStruct)
            || target.ReturnType != function.ReturnType
            || target.ParameterCount != function.ParameterCount
            || target.Variables.Length < target.ParameterCount)
        {
            return false;
        }

        for (var parameter = 0;
            parameter < function.ParameterCount; parameter++)
        {
            var source = function.Variables[parameter];
            var targetParameter = target.Variables[parameter];
            if (returnedCall.ParameterIndex(parameter) != parameter
                || returnedCall.ImplicitReferenceTemporary(parameter) != -1
                || returnedCall.Arguments[parameter]
                    is not BoundRead read)
            {
                return false;
            }

            if (read.Variable != parameter
                || read.Type != source.Type
                || source.Type != targetParameter.Type
                || source.IsMutable != targetParameter.IsMutable
                || source.IsOut != targetParameter.IsOut
                || source.IsInit != targetParameter.IsInit
                || source.IsImplicitRef != targetParameter.IsImplicitRef
                || source.IsInitializationLocal
                    != targetParameter.IsInitializationLocal)
            {
                return false;
            }
        }

        targetFunctionIndex = returnedCall.Function;
        return true;

        void Visit(BoundBlock block)
        {
            if (!block.RetiredManagedLocals.IsEmpty)
            {
                isSupported = false;
                return;
            }

            foreach (var statement in block.Statements)
            {
                if (!isSupported || returnedCall is not null)
                {
                    isSupported = false;
                    return;
                }

                if (statement is BoundBlock nested)
                {
                    Visit(nested);
                }
                else if (statement is BoundReturn {
                    Value: BoundCall call
                })
                {
                    returnedCall = call;
                }
                else
                {
                    isSupported = false;
                    return;
                }
            }
        }
    }

    private static bool TryGetC1M1Construction2UnitIdentityForwarder(
        BoundFunction function, ImmutableArray<BoundFunction> functions,
        out int targetFunctionIndex)
    {
        targetFunctionIndex = -1;
        if (function.ReturnType != PrimitiveType.Void
            || function.ParameterCount != function.Variables.Length
            || (!function.IsStatic && (function.ParameterCount == 0
                || function.Variables[0].IsMutable))
            || !function.Body.RetiredManagedLocals.IsEmpty)
        {
            return false;
        }

        BoundCall? call = null;
        if (function.Body.Statements is [
                BoundExpressionStatement {
                    Expression: BoundCall singleCall,
                },
            ])
        {
            call = singleCall;
        }
        else if (function.Body.Statements is [
                    BoundExpressionStatement {
                        Expression: BoundCall callBeforeUnitReturn,
                    },
                    BoundReturn { Value: null },
                ])
        {
            call = callBeforeUnitReturn;
        }

        if (call is null || call.Type != PrimitiveType.Void
            || call.Arguments.Length != function.ParameterCount
            || !call.DefaultedArguments.IsDefaultOrEmpty
            || call.InterfaceCallIdentity is not null
            || call.GenericMethodCall is not null
            || call.ImportedGenericMethodCall is not null
            || (uint)call.Function >= (uint)functions.Length)
        {
            return false;
        }

        var target = functions[call.Function];
        if (target.IsStatic != function.IsStatic || target.IsExternal
            || target.IsClassConstructor || target.IsConstructor
            || target.IsCleanup || target.IsOperator
            || target.ModuleImport is not null
            || target.GenericMethodInstantiation is not null
            || !StringComparer.Ordinal.Equals(target.DeclaringStruct,
                function.DeclaringStruct)
            || target.ReturnType != PrimitiveType.Void
            || target.ParameterCount != function.ParameterCount
            || target.Variables.Length < target.ParameterCount)
        {
            return false;
        }

        for (var parameter = 0;
            parameter < function.ParameterCount; parameter++)
        {
            var source = function.Variables[parameter];
            var targetParameter = target.Variables[parameter];
            if (call.ParameterIndex(parameter) != parameter
                || call.ImplicitReferenceTemporary(parameter) != -1
                || call.Arguments[parameter] is not BoundRead read
                || read.Variable != parameter
                || read.Type != source.Type
                || source.Type != targetParameter.Type
                || source.IsMutable != targetParameter.IsMutable
                || source.IsOut != targetParameter.IsOut
                || source.IsInit != targetParameter.IsInit
                || source.IsImplicitRef != targetParameter.IsImplicitRef
                || source.IsInitializationLocal
                    != targetParameter.IsInitializationLocal)
            {
                return false;
            }
        }

        targetFunctionIndex = call.Function;
        return true;
    }

    private static BoundBinary?
        TryGetC1M1Construction2DirectBooleanComparison(
        BoundFunction function)
    {
        if (function.IsStatic || function.ReturnType != PrimitiveType.Bool
            || function.ParameterCount != 3
            || function.ParameterCount != function.Variables.Length
            || function.Variables[0].IsMutable
            || function.Variables[1].Type != PrimitiveType.Bool
            || function.Variables[2].Type != PrimitiveType.Bool
            || function.Variables.Skip(1).Any(static variable =>
                variable.IsMutable || variable.IsOut || variable.IsInit
                || variable.IsImplicitRef
                || variable.IsInitializationLocal))
        {
            return null;
        }

        BoundBinary? returnedBinary = null;
        var isSupported = true;
        Visit(function.Body);
        if (!isSupported || returnedBinary is null
            || returnedBinary.Type != PrimitiveType.Bool
            || returnedBinary.Operator is not (TokenKind.EqualsEquals
                or TokenKind.BangEquals)
            || returnedBinary.Left is not BoundRead left
            || returnedBinary.Right is not BoundRead right
            || left.Type != PrimitiveType.Bool
            || right.Type != PrimitiveType.Bool
            || left.Variable is not (1 or 2)
            || right.Variable is not (1 or 2))
        {
            return null;
        }

        return returnedBinary;

        void Visit(BoundBlock block)
        {
            if (!block.RetiredManagedLocals.IsEmpty)
            {
                isSupported = false;
                return;
            }

            foreach (var statement in block.Statements)
            {
                if (!isSupported || returnedBinary is not null)
                {
                    isSupported = false;
                    return;
                }

                if (statement is BoundBlock nested)
                {
                    Visit(nested);
                }
                else if (statement is BoundReturn {
                    Value: BoundBinary binary
                })
                {
                    returnedBinary = binary;
                }
                else
                {
                    isSupported = false;
                    return;
                }
            }
        }
    }

    private static BoundBinary? TryGetC1M1Construction2DirectNumericBinary(
        BoundFunction function)
    {
        if (function.IsStatic || function.ParameterCount < 2
            || function.ParameterCount != function.Variables.Length
            || function.Variables[0].IsMutable
            || function.Variables.Skip(1).Any(static variable =>
                variable.IsMutable
                || variable.IsOut || variable.IsInit
                || variable.IsImplicitRef
                || variable.IsInitializationLocal))
        {
            return null;
        }

        BoundBinary? returnedBinary = null;
        var isSupported = true;
        Visit(function.Body);
        if (!isSupported || returnedBinary is null)
        {
            return null;
        }

        var leftRead = returnedBinary.Left as BoundRead;
        var rightRead = returnedBinary.Right as BoundRead;
        var leftConstant = returnedBinary.Left as BoundConstant;
        var rightConstant = returnedBinary.Right as BoundConstant;
        var leftReadIsValid = IsValidFormalRead(leftRead);
        var rightReadIsValid = IsValidFormalRead(rightRead);
        var leftConstantIsValid = IsValidConstant(leftConstant);
        var rightConstantIsValid = IsValidConstant(rightConstant);
        var leftType = leftRead?.Type ?? leftConstant?.Type;
        var rightType = rightRead?.Type ?? rightConstant?.Type;
        var hasSameNumericType
            = leftType.HasValue && rightType == leftType
                && IsC1M1Construction2BodyNumericType(leftType.Value);
        var isComparison
            = function.ReturnType == PrimitiveType.Bool
                && returnedBinary.Type == PrimitiveType.Bool
                && IsC1M1Construction2IntegerComparisonOperator(
                    returnedBinary.Operator)
                && hasSameNumericType
                && (leftReadIsValid || leftConstantIsValid)
                && (rightReadIsValid || rightConstantIsValid)
                && (leftReadIsValid || rightReadIsValid);
        var isIntegerValueOperation
            = function.ReturnType == returnedBinary.Type
                && leftType.HasValue && returnedBinary.Type == leftType.Value
                && hasSameNumericType
                && IsC1M1Construction2BodyIntegerType(leftType.Value)
                && (returnedBinary.Operator is TokenKind.Ampersand
                    or TokenKind.Pipe or TokenKind.Caret or TokenKind.Plus
                    or TokenKind.Minus or TokenKind.Star
                    or TokenKind.Slash or TokenKind.Percent)
                && (leftReadIsValid && rightReadIsValid
                    || leftReadIsValid && rightConstantIsValid
                    || leftConstantIsValid && rightReadIsValid);
        var isFloatingValueOperation
            = function.ReturnType == returnedBinary.Type
                && leftType.HasValue && returnedBinary.Type == leftType.Value
                && hasSameNumericType
                && PrimitiveTypeFacts.IsFloating(leftType.Value)
                && (returnedBinary.Operator is TokenKind.Plus
                    or TokenKind.Minus or TokenKind.Star or TokenKind.Slash
                    or TokenKind.Percent)
                && (leftReadIsValid && rightReadIsValid
                    || leftReadIsValid && rightConstantIsValid
                    || leftConstantIsValid && rightReadIsValid);
        return isComparison || isIntegerValueOperation
                || isFloatingValueOperation
            ? returnedBinary : null;

        bool IsValidFormalRead(BoundRead? read)
            => read is not null
                && IsC1M1Construction2BodyNumericType(read.Type)
                && read.Variable > 0
                && read.Variable < function.ParameterCount
                && function.Variables[read.Variable].Type
                    == read.Type;

        static bool IsValidConstant(BoundConstant? constant)
            => constant is not null
                && IsC1M1Construction2BodyNumericType(constant.Type)
                && constant.RepresentationType == constant.Type
                && constant.Bits.IsCanonicalFor(constant.Type);

        void Visit(BoundBlock block)
        {
            if (!block.RetiredManagedLocals.IsEmpty)
            {
                isSupported = false;
                return;
            }

            foreach (var statement in block.Statements)
            {
                if (!isSupported || returnedBinary is not null)
                {
                    isSupported = false;
                    return;
                }

                if (statement is BoundBlock nested)
                {
                    Visit(nested);
                }
                else if (statement is BoundReturn {
                    Value: BoundBinary binary
                })
                {
                    returnedBinary = binary;
                }
                else
                {
                    isSupported = false;
                    return;
                }
            }
        }
    }

    private static BoundUnary? TryGetC1M1Construction2DirectNumericUnary(
        BoundFunction function)
    {
        if (function.IsStatic || function.ParameterCount != 2
            || function.ParameterCount != function.Variables.Length
            || function.Variables[0].IsMutable
            || !IsC1M1Construction2BodyNumericType(
                function.Variables[1].Type)
            || function.Variables[1].IsMutable
            || function.Variables[1].IsOut || function.Variables[1].IsInit
            || function.Variables[1].IsImplicitRef
            || function.Variables[1].IsInitializationLocal)
        {
            return null;
        }

        BoundUnary? returnedUnary = null;
        var isSupported = true;
        Visit(function.Body);
        if (!isSupported || returnedUnary is null
            || returnedUnary.Type != function.ReturnType
            || !IsC1M1Construction2BodyNumericType(returnedUnary.Type)
            || (PrimitiveTypeFacts.IsFloating(returnedUnary.Type)
                ? returnedUnary.Operator != TokenKind.Minus
                : returnedUnary.Operator is not (TokenKind.Minus
                    or TokenKind.Tilde))
            || returnedUnary.Operand is not BoundRead operand
            || operand.Type != returnedUnary.Type
            || operand.Variable != 1)
        {
            return null;
        }

        return returnedUnary;

        void Visit(BoundBlock block)
        {
            if (!block.RetiredManagedLocals.IsEmpty)
            {
                isSupported = false;
                return;
            }

            foreach (var statement in block.Statements)
            {
                if (!isSupported || returnedUnary is not null)
                {
                    isSupported = false;
                    return;
                }

                if (statement is BoundBlock nested)
                {
                    Visit(nested);
                }
                else if (statement is BoundReturn {
                    Value: BoundUnary unary
                })
                {
                    returnedUnary = unary;
                }
                else
                {
                    isSupported = false;
                    return;
                }
            }
        }
    }

    private static BoundBinary?
        TryGetC1M1Construction2DirectCallNumericBinary(
        BoundFunction function, ImmutableArray<BoundFunction> functions,
        BoundProgram program)
    {
        var callerReceiverCount = function.IsStatic ? 0 : 1;
        if (function.ParameterCount != callerReceiverCount + 2
            || function.ParameterCount != function.Variables.Length)
        {
            return null;
        }

        var operationType = function.Variables[callerReceiverCount].Type;
        if (!IsC1M1Construction2BodyNumericType(operationType)
            || function.Variables.Skip(callerReceiverCount).Any(
                variable => variable.Type != operationType
                || variable.IsMutable
                || variable.IsOut || variable.IsInit
                || variable.IsImplicitRef
                || variable.IsInitializationLocal))
        {
            return null;
        }

        BoundBinary? returnedBinary = null;
        var isSupported = true;
        Visit(function.Body);
        var rightRead = returnedBinary?.Right as BoundRead;
        var rightConstant = returnedBinary?.Right as BoundConstant;
        if (!isSupported || returnedBinary is null
            || returnedBinary.Left is not BoundCall call
            || (rightRead is null && !IsValidConstant(rightConstant))
            || (rightRead is not null
                && (rightRead.Type != operationType
                    || rightRead.Variable < callerReceiverCount
                    || rightRead.Variable >= function.ParameterCount))
            || (rightConstant is not null
                && rightConstant.Type != operationType)
            || call.Type != operationType
            || !call.DefaultedArguments.IsDefaultOrEmpty
            || call.InterfaceCallIdentity is not null
            || call.GenericMethodCall is not null
            || call.ImportedGenericMethodCall is not null
            || (uint)call.Function >= (uint)functions.Length)
        {
            return null;
        }

        var isComparison
            = function.ReturnType == PrimitiveType.Bool
                && returnedBinary.Type == PrimitiveType.Bool
                && IsC1M1Construction2IntegerComparisonOperator(
                    returnedBinary.Operator);
        var isIntegerValueOperation
            = function.ReturnType == operationType
                && returnedBinary.Type == operationType
                && IsC1M1Construction2BodyIntegerType(operationType)
                && (returnedBinary.Operator is TokenKind.Ampersand
                    or TokenKind.Pipe or TokenKind.Caret
                    or TokenKind.Plus or TokenKind.Minus or TokenKind.Star
                    or TokenKind.Slash or TokenKind.Percent);
        var isFloatingValueOperation
            = function.ReturnType == operationType
                && returnedBinary.Type == operationType
                && PrimitiveTypeFacts.IsFloating(operationType)
                && (returnedBinary.Operator is TokenKind.Plus
                    or TokenKind.Minus or TokenKind.Star or TokenKind.Slash
                    or TokenKind.Percent);
        if (!isComparison && !isIntegerValueOperation
            && !isFloatingValueOperation)
        {
            return null;
        }

        var target = functions[call.Function];
        var targetReceiverCount = target.IsStatic ? 0 : 1;
        if (target.IsExternal
            || target.IsClassConstructor || target.IsConstructor
            || target.IsCleanup || target.IsOperator
            || target.ModuleImport is not null
            || target.GenericMethodInstantiation is not null
            || !StringComparer.Ordinal.Equals(target.DeclaringStruct,
                function.DeclaringStruct)
            || target.ReturnType != operationType
            || target.ParameterCount != targetReceiverCount + 2
            || call.Arguments.Length != target.ParameterCount
            || target.Variables.Length < target.ParameterCount
            || target.Variables.Skip(targetReceiverCount).Take(2).Any(
                variable => variable.Type != operationType
                    || variable.IsMutable || variable.IsOut
                    || variable.IsInit || variable.IsImplicitRef
                    || variable.IsInitializationLocal))
        {
            return null;
        }

        for (var parameter = 0;
            parameter < target.ParameterCount; parameter++)
        {
            var sourceVariable = target.IsStatic
                ? callerReceiverCount + parameter
                : parameter == 0 ? 0
                    : callerReceiverCount + parameter - 1;
            var source = function.Variables[sourceVariable];
            var targetParameter = target.Variables[parameter];
            if (call.ParameterIndex(parameter) != parameter
                || call.ImplicitReferenceTemporary(parameter) != -1
                || call.Arguments[parameter] is not BoundRead argument
                || argument.Variable != sourceVariable
                || argument.Type != source.Type
                || source.IsOut != targetParameter.IsOut
                || source.IsInit != targetParameter.IsInit
                || source.IsImplicitRef != targetParameter.IsImplicitRef
                || source.IsInitializationLocal
                    != targetParameter.IsInitializationLocal)
            {
                return null;
            }

            var isReceiver = !target.IsStatic && parameter == 0;
            if (isReceiver
                ? !IsC1M1Construction2ReceiverTypeCompatible(
                    source.Type, source.IsMutable, targetParameter.Type,
                    targetParameter.IsMutable, program)
                : source.Type != targetParameter.Type
                    || source.IsMutable != targetParameter.IsMutable)
            {
                return null;
            }
        }

        return returnedBinary;

        static bool IsValidConstant(BoundConstant? constant)
            => constant is not null
                && constant.Type == constant.RepresentationType
                && IsC1M1Construction2BodyNumericType(constant.Type)
                && constant.Bits.IsCanonicalFor(constant.Type);

        void Visit(BoundBlock block)
        {
            if (!block.RetiredManagedLocals.IsEmpty)
            {
                isSupported = false;
                return;
            }

            foreach (var statement in block.Statements)
            {
                if (!isSupported || returnedBinary is not null)
                {
                    isSupported = false;
                    return;
                }

                if (statement is BoundBlock nested)
                {
                    Visit(nested);
                }
                else if (statement is BoundReturn {
                    Value: BoundBinary binary
                })
                {
                    returnedBinary = binary;
                }
                else
                {
                    isSupported = false;
                    return;
                }
            }
        }
    }

    private static bool TryGetC1M1Construction2LocalCopyReturn(
        BoundFunction function)
    {
        var receiverCount = function.IsStatic ? 0 : 1;
        var formalVariable = receiverCount;
        var localCount = function.Variables.Length - function.ParameterCount;
        var lastLocalVariable = function.Variables.Length - 1;
        if (function.ParameterCount != receiverCount + 1
            || localCount <= 0
            || function.Variables.Length > ushort.MaxValue + 1
            || function.Body.RetiredManagedLocals.Length != 0
            || function.Body.Statements.Length != localCount + 1
            || function.Body.Statements[localCount] is not BoundReturn {
                Value: BoundRead returnedRead,
            })
        {
            return false;
        }

        var operationType = function.Variables[formalVariable].Type;
        if ((!IsC1M1Construction2BodyNumericType(operationType)
                && operationType != PrimitiveType.Bool)
            || function.ReturnType != operationType
            || (!function.IsStatic && function.Variables[0].IsMutable)
            || function.Variables.Skip(receiverCount).Any(variable =>
                variable.Type != operationType || variable.IsMutable
                || variable.IsOut || variable.IsInit
                || variable.IsImplicitRef
                || variable.IsInitializationLocal)
            || returnedRead.Type != operationType
            || returnedRead.Variable != lastLocalVariable)
        {
            return false;
        }

        for (var localOrdinal = 0; localOrdinal < localCount;
            localOrdinal++)
        {
            var localVariable = function.ParameterCount + localOrdinal;
            if (function.Body.Statements[localOrdinal] is not BoundLocal local
                || local.Variable != localVariable
                || local.Initializer is not BoundRead initialRead
                || initialRead.Type != operationType
                || initialRead.Variable < 0
                || initialRead.Variable >= localVariable
                || function.Variables[initialRead.Variable].Type
                    != operationType)
            {
                return false;
            }
        }

        return true;
    }

    private static BoundUnary?
        TryGetC1M1Construction2LocalNumericUnaryReturn(
            BoundFunction function)
    {
        var receiverCount = function.IsStatic ? 0 : 1;
        var localCount = function.Variables.Length
            - function.ParameterCount;
        if (function.ParameterCount != receiverCount + 1
            || localCount <= 0
            || function.Variables.Length > ushort.MaxValue + 1
            || !function.Body.RetiredManagedLocals.IsEmpty
            || function.Body.Statements.Length != localCount + 1
            || function.Body.Statements[localCount] is not BoundReturn {
                Value: BoundUnary unary,
            })
        {
            return null;
        }

        var operationType = function.Variables[receiverCount].Type;
        if (!IsC1M1Construction2BodyNumericType(operationType)
            || function.ReturnType != operationType
            || (!function.IsStatic && function.Variables[0].IsMutable)
            || function.Variables.Skip(receiverCount).Any(variable =>
                variable.Type != operationType || variable.IsMutable
                || variable.IsOut || variable.IsInit
                || variable.IsImplicitRef
                || variable.IsInitializationLocal)
            || unary.Type != operationType
            || unary.Operand is not BoundRead operand
            || operand.Type != operationType
            || operand.Variable != function.Variables.Length - 1
            || (PrimitiveTypeFacts.IsFloating(operationType)
                ? unary.Operator != TokenKind.Minus
                : unary.Operator is not (TokenKind.Minus
                    or TokenKind.Tilde)))
        {
            return null;
        }

        for (var localOrdinal = 0; localOrdinal < localCount;
            localOrdinal++)
        {
            var localVariable = function.ParameterCount + localOrdinal;
            if (function.Body.Statements[localOrdinal] is not BoundLocal local
                || local.Variable != localVariable
                || local.Initializer is not BoundRead initialRead
                || initialRead.Type != operationType
                || initialRead.Variable < 0
                || initialRead.Variable >= localVariable
                || function.Variables[initialRead.Variable].Type
                    != operationType)
            {
                return null;
            }
        }

        return unary;
    }

    private static bool TryGetC1M1Construction2LocalNumericBinaryReturn(
        BoundFunction function)
    {
        var receiverCount = function.IsStatic ? 0 : 1;
        var localCount = function.Variables.Length
            - function.ParameterCount;
        if (function.ParameterCount < receiverCount + 1
            || localCount <= 0
            || (!function.IsStatic && function.Variables[0].IsMutable)
            || !function.Body.RetiredManagedLocals.IsEmpty
            || function.Body.Statements.Length != localCount + 1)
        {
            return false;
        }

        var operationType = function.Variables[receiverCount].Type;
        if (!IsC1M1Construction2BodyNumericType(operationType)
            || !function.Variables.Skip(receiverCount).All(
                variable => variable.Type == operationType
                    && !variable.IsMutable && !variable.IsOut
                    && !variable.IsInit && !variable.IsImplicitRef
                    && !variable.IsInitializationLocal))
        {
            return false;
        }

        for (var localOrdinal = 0;
            localOrdinal < localCount; localOrdinal++)
        {
            var localVariable = function.ParameterCount + localOrdinal;
            if (function.Body.Statements[localOrdinal] is not BoundLocal local
                || local.Variable != localVariable
                || local.Initializer is not BoundRead initializer
                || initializer.Type != operationType
                || initializer.Variable < 0
                || initializer.Variable >= localVariable
                || function.Variables[initializer.Variable].Type
                    != operationType)
            {
                return false;
            }
        }

        if (function.Body.Statements[localCount] is not BoundReturn {
                Value: BoundBinary binary,
            })
        {
            return false;
        }

        var leftRead = binary.Left as BoundRead;
        var rightRead = binary.Right as BoundRead;
        var leftConstant = binary.Left as BoundConstant;
        var rightConstant = binary.Right as BoundConstant;
        var leftReadIsValid = IsValidHomeRead(leftRead);
        var rightReadIsValid = IsValidHomeRead(rightRead);
        var leftConstantIsValid = IsValidConstant(leftConstant);
        var rightConstantIsValid = IsValidConstant(rightConstant);
        var leftType = leftRead?.Type ?? leftConstant?.Type;
        var rightType = rightRead?.Type ?? rightConstant?.Type;
        var hasSameNumericType
            = leftType == operationType && rightType == leftType;
        var isComparison
            = function.ReturnType == PrimitiveType.Bool
                && binary.Type == PrimitiveType.Bool
                && IsC1M1Construction2IntegerComparisonOperator(
                    binary.Operator)
                && hasSameNumericType
                && (leftReadIsValid || leftConstantIsValid)
                && (rightReadIsValid || rightConstantIsValid)
                && (leftReadIsValid || rightReadIsValid);
        var isIntegerValueOperation
            = function.ReturnType == operationType
                && binary.Type == operationType
                && hasSameNumericType
                && IsC1M1Construction2BodyIntegerType(operationType)
                && (binary.Operator is TokenKind.Ampersand
                    or TokenKind.Pipe or TokenKind.Caret or TokenKind.Plus
                    or TokenKind.Minus or TokenKind.Star
                    or TokenKind.Slash or TokenKind.Percent)
                && (leftReadIsValid && rightReadIsValid
                    || leftReadIsValid && rightConstantIsValid
                    || leftConstantIsValid && rightReadIsValid);
        var isFloatingValueOperation
            = function.ReturnType == operationType
                && binary.Type == operationType
                && hasSameNumericType
                && PrimitiveTypeFacts.IsFloating(operationType)
                && (binary.Operator is TokenKind.Plus or TokenKind.Minus
                    or TokenKind.Star or TokenKind.Slash or TokenKind.Percent)
                && (leftReadIsValid && rightReadIsValid
                    || leftReadIsValid && rightConstantIsValid
                    || leftConstantIsValid && rightReadIsValid);
        return isComparison || isIntegerValueOperation
            || isFloatingValueOperation;

        bool IsValidHomeRead(BoundRead? read)
            => read is not null && read.Type == operationType
                && IsC1M1Construction2BodyNumericType(read.Type)
                && read.Variable >= receiverCount
                && read.Variable < function.Variables.Length
                && function.Variables[read.Variable].Type
                    == operationType;

        static bool IsValidConstant(BoundConstant? constant)
            => constant is not null
                && IsC1M1Construction2BodyNumericType(constant.Type)
                && constant.RepresentationType == constant.Type
                && constant.Bits.IsCanonicalFor(constant.Type);
    }

    private static bool TryGetC1M1Construction2DirectParameterReturn(
        BoundFunction function, out int returnHomeIndex)
    {
        returnHomeIndex = -1;
        if (function.ReturnType == PrimitiveType.Void
            || function.ParameterCount != function.Variables.Length
            || function.ParameterCount < 2)
        {
            return false;
        }

        BoundRead? returnedRead = null;
        var isSupported = true;
        Visit(function.Body);
        if (!isSupported || returnedRead is null
            || returnedRead.Variable <= 0
            || returnedRead.Variable >= function.ParameterCount
            || returnedRead.Type != function.ReturnType
            || function.Variables[returnedRead.Variable].Type
                != function.ReturnType)
        {
            return false;
        }

        returnHomeIndex = returnedRead.Variable;
        return true;

        void Visit(BoundBlock block)
        {
            if (!block.RetiredManagedLocals.IsEmpty)
            {
                isSupported = false;
                return;
            }

            foreach (var statement in block.Statements)
            {
                if (!isSupported || returnedRead is not null)
                {
                    isSupported = false;
                    return;
                }

                if (statement is BoundBlock nested)
                {
                    Visit(nested);
                }
                else if (statement is BoundReturn { Value: BoundRead read })
                {
                    returnedRead = read;
                }
                else
                {
                    isSupported = false;
                    return;
                }
            }
        }
    }

    private static void RequireC1M1Construction2NoOpUnitBody(
        BoundBlock body)
    {
        var returned = false;
        Visit(body);

        void Visit(BoundBlock block)
        {
            if (!block.RetiredManagedLocals.IsEmpty)
            {
                throw Unsupported(
                    "The bounded Construction-2 instance method producer cannot discard retired-local cleanup.");
            }

            foreach (var statement in block.Statements)
            {
                if (returned)
                {
                    throw Unsupported(
                        "A Construction-2 no-op method cannot continue after its unit return.");
                }

                if (statement is BoundBlock nested)
                {
                    Visit(nested);
                }
                else if (statement is BoundReturn { Value: null })
                {
                    returned = true;
                }
                else
                {
                    throw Unsupported(
                        "The bounded Construction-2 instance method producer supports only an empty unit body.");
                }
            }
        }
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

    private static bool TryGetC1M1ImportedClassFieldType(
        PrimitiveType type, BoundProgram program,
        IReadOnlyDictionary<BoundGenericNominalTypeIdentity, int>
            standardClassIndexByIdentity,
        out int classIndex, out int instanceIndex,
        bool allowOptionalWrapper = false)
    {
        if (!BoundGenericNominalTypeTypes.IsType(type))
        {
            classIndex = -1;
            instanceIndex = -1;
            return false;
        }

        instanceIndex = BoundGenericNominalTypeTypes.Index(type);
        if ((uint)instanceIndex
                >= (uint)program.GenericNominalTypeInstances.Length
            || !BoundGenericNominalTypeContract.IsNominalClassType(type,
                program.GenericNominalTypeInstances,
                program.GenericNominalTypeDefinitions))
        {
            classIndex = -1;
            return false;
        }

        var instance = program.GenericNominalTypeInstances[instanceIndex];
        if (!instance.Arguments.IsEmpty
            || (instance.Qualifiers & ~0x01) != 0
            || (instance.TypeSpecForm is not (0 or 0x02 or 0x03)
                && !(allowOptionalWrapper
                    && instance.TypeSpecForm == 0x06))
            || !standardClassIndexByIdentity.TryGetValue(
                instance.Definition, out classIndex))
        {
            classIndex = -1;
            instanceIndex = -1;
            return false;
        }

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

    private static bool IsC1M1SourceStructLayoutSupported(
        BoundStruct structure, BoundSourceTypeDeclaration declaration)
    {
        if (structure.StandardTypeOrigin is not
            BoundStandardTypeOrigin origin)
        {
            return IsC1M1SourceStructLayoutSupported(structure)
                && structure == declaration.DeclarationSnapshot;
        }

        if (!structure.StandardLayoutOnly
            || origin.DefinitionId >> 24 != 0x13
            || (origin.DefinitionId & 0x00FF_FFFF) == 0
            || origin.Provider is null
            || origin.ExportKeys.IsDefaultOrEmpty
            || !IsC1M1SourceStructLayoutSupported(
                declaration.DeclarationSnapshot))
        {
            return false;
        }

        var actual = structure with {
            Fields = [.. structure.Fields.Select(static field => field with {
                StandardFieldOrigin = null,
            })],
            StandardTypeOrigin = null,
            StandardLayoutOnly = false,
        };
        return actual == declaration.DeclarationSnapshot
            && (origin.ExpectedDeclaration is null
                || origin.ExpectedDeclaration
                    == declaration.DeclarationSnapshot);
    }

    private static bool IsC1M1ImportedStructFieldImportable(
        BoundStruct owner, BoundStructField field,
        C1M1ImportedStructImportPlan importPlan)
    {
        if (owner.StandardTypeOrigin is not BoundStandardTypeOrigin)
        {
            return true;
        }

        private static Dictionary<BoundGenericNominalTypeIdentity, int>
            CreateC1M1StandardClassIndexByIdentity(BoundProgram program)
        {
            var indexes
                = new Dictionary<BoundGenericNominalTypeIdentity, int>();
            for (var index = 0;
                index < program.StandardClassDeclarations.Length; index++)
            {
                var declaration = program.StandardClassDeclarations[index];
                Add(declaration.Declaration.Identity, index);
                foreach (var expectation in declaration.Expectations)
                {
                    Add(expectation.ExpectedDeclaration.Identity, index);
                }
            }

            return indexes;

            void Add(BoundGenericNominalTypeIdentity identity, int index)
            {
                if (!indexes.TryAdd(identity, index)
                    && indexes[identity] != index)
                {
                    throw new InvalidDataException(
                        "One nominal class identity selects conflicting Standard class declarations.");
                }
            }
        }

        if (field.StandardFieldOrigin is not
            BoundStandardFieldOrigin fieldOrigin
            || fieldOrigin.Provider is null
            || !SameA2SourceTypeProviderSnapshot(fieldOrigin.Provider,
                importPlan.Provider)
            || fieldOrigin.DefinitionId >> 24 != 0x14
            || (fieldOrigin.DefinitionId & 0x00FF_FFFF) == 0
            || field.IsPrivate || field.IsInternal
            || fieldOrigin.ExportKeys.IsDefaultOrEmpty)
        {
            return false;
        }

        var exportKeys = fieldOrigin.ExportKeys.ToHashSet(
            StringComparer.Ordinal);
        if (exportKeys.Count != fieldOrigin.ExportKeys.Length
            || exportKeys.Any(static key =>
                !BoundEnumTypes.IsValidModuleDeclarationKey(key)))
        {
            return false;
        }

        if (!fieldOrigin.SelectedExportKeys.IsDefaultOrEmpty)
        {
            var selectedKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var exportKey in fieldOrigin.SelectedExportKeys)
            {
                if (!BoundEnumTypes.IsValidModuleDeclarationKey(exportKey)
                    || !exportKeys.Contains(exportKey)
                    || !selectedKeys.Add(exportKey))
                {
                    return false;
                }
            }
        }

        var actualField = field with {
            StandardFieldOrigin = null,
        };
        return fieldOrigin.ExpectedDeclaration is null
            || actualField == fieldOrigin.ExpectedDeclaration;
    }

    private static bool IsC1M1ImportedClassFieldImportable(
        BoundStandardClassTypeView owner,
        BoundStandardClassFieldDeclaration field,
        C1M1ImportedClassImportPlan importPlan)
    {
        if (!SameA2SourceTypeProviderSnapshot(owner.Provider,
                importPlan.Provider)
            || field.DefinitionId >> 24 != 0x14
            || (field.DefinitionId & 0x00FF_FFFF) == 0
            || field.OwnerDefinitionId != owner.Identity.DefinitionId
            || (field.Flags & 3) != 2
            || (field.Flags & 0x08) != 0
            || field.ExportKeys.IsDefaultOrEmpty)
        {
            return false;
        }

        var exportKeys = field.ExportKeys.ToHashSet(StringComparer.Ordinal);
        if (exportKeys.Count != field.ExportKeys.Length
            || exportKeys.Any(static key =>
                !BoundEnumTypes.IsValidModuleDeclarationKey(key)))
        {
            return false;
        }

        if (!field.SelectedExportKeys.IsDefaultOrEmpty)
        {
            var selectedKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var exportKey in field.SelectedExportKeys)
            {
                if (!BoundEnumTypes.IsValidModuleDeclarationKey(exportKey)
                    || !exportKeys.Contains(exportKey)
                    || !selectedKeys.Add(exportKey))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static C1M1ImportedStructImportPlan
        GetC1M1ImportedStructImportPlan(
            BoundStandardTypeOrigin origin)
    {
        if (origin.Provider is null
            || origin.ExportKeys.IsDefaultOrEmpty)
        {
            throw Unsupported(
                "C1M1 class source emission requires an exact Standard struct TypeDef provider and ExportKey.");
        }

        var exportKeys = origin.ExportKeys.ToHashSet(StringComparer.Ordinal);
        var selectedKeys = new HashSet<string>(StringComparer.Ordinal);
        if (origin.Expectations.IsDefaultOrEmpty)
        {
            if (!origin.IsLookupRoot
                || origin.SelectedExportKeys.IsDefaultOrEmpty)
            {
                throw Unsupported(
                    "C1M1 class source emission requires an explicitly selected Standard struct TypeDef Export.");
            }

            foreach (var exportKey in origin.SelectedExportKeys)
            {
                if (!BoundEnumTypes.IsValidModuleDeclarationKey(exportKey)
                    || !exportKeys.Contains(exportKey)
                    || !selectedKeys.Add(exportKey))
                {
                    throw Unsupported(
                        "C1M1 class source emission requires exact selected Standard struct TypeDef ExportKeys.");
                }
            }

            return new C1M1ImportedStructImportPlan(origin.Provider,
                origin.SelectedExportKeys);
        }

        if (origin.IsLookupRoot
            || !origin.SelectedExportKeys.IsDefaultOrEmpty)
        {
            throw Unsupported(
                "C1M1 imported struct TypeDef expectations cannot be mixed with selected lookup-root exports.");
        }

        var rootSnapshot = origin.ReferenceRoot ?? origin.Provider;
        BoundSourceTypeProviderSnapshot? provider = null;
        uint providerDefinitionId = 0;
        uint expectedDefinitionId = 0;
        var exportKeyBuilder = ImmutableArray.CreateBuilder<string>();
        foreach (var expectation in origin.Expectations)
        {
            if (!SameA2SourceTypeProviderSnapshot(expectation.UsingModule,
                    rootSnapshot))
            {
                continue;
            }

            if (expectation.ImportRow <= 0
                || expectation.ExpectedDefinitionId >> 24 != 0x13
                || (expectation.ExpectedDefinitionId & 0x00FF_FFFF) == 0
                || expectation.ProviderDefinitionId >> 24 != 0x13
                || (expectation.ProviderDefinitionId & 0x00FF_FFFF) == 0
                || !StringComparer.Ordinal.Equals(
                    expectation.ProducerIdentity,
                    expectation.Provider.ModuleIdentity)
                || expectation.TargetRequirement
                    != expectation.Provider.TargetRequirement
                || expectation.RuntimePolicy
                    != expectation.Provider.RuntimePolicy
                || !BoundEnumTypes.IsValidModuleDeclarationKey(
                    expectation.ExportKey)
                || !exportKeys.Contains(expectation.ExportKey))
            {
                throw new InvalidDataException(
                    "An imported struct TypeDef has an invalid root-owned provider expectation.");
            }

            if (provider is null)
            {
                provider = expectation.Provider;
                providerDefinitionId = expectation.ProviderDefinitionId;
                expectedDefinitionId = expectation.ExpectedDefinitionId;
            }
            else if (!SameA2SourceTypeProviderSnapshot(provider,
                         expectation.Provider)
                || providerDefinitionId
                    != expectation.ProviderDefinitionId
                || expectedDefinitionId
                    != expectation.ExpectedDefinitionId)
            {
                throw Invalid(
                    "One imported struct TypeDef has conflicting root-owned provider expectations.");
            }

            if (selectedKeys.Add(expectation.ExportKey))
            {
                exportKeyBuilder.Add(expectation.ExportKey);
            }
        }

        if (provider is null
            || providerDefinitionId != origin.DefinitionId
            || !SameA2SourceTypeProviderSnapshot(provider, origin.Provider)
            || exportKeyBuilder.Count == 0)
        {
            throw Unsupported(
                "C1M1 imported struct closure requires one exact root-owned TypeDef provider binding.");
        }

        return new C1M1ImportedStructImportPlan(provider,
            exportKeyBuilder.MoveToImmutable());
    }

    private static Dictionary<int, BoundSourceTypeDeclaration>
        CreateC1M1SourceStructDeclarations(BoundProgram program)
    {
        var declarations = program.SourceTypeDeclarations.ToDictionary(
            static declaration => declaration.StructureIndex);
        for (var structureIndex = 0;
            structureIndex < program.Structures.Length; structureIndex++)
        {
            var structure = program.Structures[structureIndex];
            if (structure.StandardTypeOrigin is null)
            {
                continue;
            }

            var separator = structure.Name.LastIndexOf('.',
                StringComparison.Ordinal);
            var namespaceName = separator < 0
                ? string.Empty : structure.Name[..separator];
            var typeName = separator < 0
                ? structure.Name : structure.Name[(separator + 1)..];
            var declarationSnapshot = structure with {
                Fields = [.. structure.Fields.Select(static field => field with {
                    StandardFieldOrigin = null,
                })],
                StandardTypeOrigin = null,
                StandardLayoutOnly = false,
            };
            if (string.IsNullOrEmpty(typeName)
                || !declarations.TryAdd(structureIndex,
                    new BoundSourceTypeDeclaration(structureIndex,
                        namespaceName, typeName,
                        structure.StandardTypeOrigin.ExpectedDeclaration
                            ?? declarationSnapshot)))
            {
                throw new InvalidDataException(
                    "A standard struct view has no unique source declaration identity.");
            }
        }

        return declarations;
    }

    private static bool HasSupportedC1M1ImportedNominalClosure(
        BoundProgram program,
        IReadOnlyDictionary<int, BoundSourceTypeDeclaration>
            sourceStructDeclarations,
        IReadOnlyDictionary<BoundGenericNominalTypeIdentity, int>
            standardClassIndexByIdentity)
    {
        var importedStructIdentities
            = new HashSet<BoundGenericNominalTypeIdentity>();
        for (var structureIndex = 0;
            structureIndex < program.Structures.Length; structureIndex++)
        {
            if (sourceStructDeclarations.ContainsKey(structureIndex)
                && program.Structures[structureIndex]
                    .StandardTypeOrigin is
                    BoundStandardTypeOrigin origin)
            {
                importedStructIdentities.Add(
                    new BoundGenericNominalTypeIdentity(
                        origin.Provider.ModuleIdentity,
                        origin.Provider.ModuleIdentity,
                        origin.DefinitionId));
                foreach (var expectation in origin.Expectations)
                {
                    importedStructIdentities.Add(
                        new BoundGenericNominalTypeIdentity(
                            expectation.UsingModule.ModuleIdentity,
                            expectation.UsingModule.ModuleIdentity,
                            expectation.ExpectedDefinitionId));
                    importedStructIdentities.Add(
                        new BoundGenericNominalTypeIdentity(
                            expectation.Provider.ModuleIdentity,
                            expectation.Provider.ModuleIdentity,
                            expectation.ProviderDefinitionId));
                }
            }
        }

        var standardClassIdentities
            = standardClassIndexByIdentity.Keys.ToHashSet();
        var relationNominalIdentities
            = new HashSet<BoundGenericNominalTypeIdentity>();
        foreach (var definition in program.GenericMethodDefinitions)
        {
            foreach (var formal in definition.Formals)
            {
                foreach (var relation in formal.AssociatedRelations)
                {
                    AddC1M1RelationNominalIdentities(
                        relation.RequiredType, relationNominalIdentities);
                }
            }
        }
        var methodSignatureNominalIdentities
            = new HashSet<BoundGenericNominalTypeIdentity>();
        if (!AddC1M1GenericMethodNominalIdentities(program,
                methodSignatureNominalIdentities))
        {
            return false;
        }

        bool IsImportedNominalIdentity(
            BoundGenericNominalTypeIdentity identity)
            => identity.IsValid
                && (importedStructIdentities.Contains(identity)
                    || standardClassIdentities.Contains(identity)
                    || relationNominalIdentities.Contains(identity)
                    || methodSignatureNominalIdentities.Contains(identity));

        if ((!program.StandardClassDeclarations.IsEmpty
                && !BoundStandardClassDeclarationContract.IsValidTable(
                    program.StandardClassDeclarations,
                    program.GenericNominalTypeDefinitions))
            || program.StandardClassDeclarations.Any(
                declaration =>
                    !IsC1M1ImportedClassLayoutSupported(program,
                        declaration))
        {
            return false;
        }

        foreach (var definition in program.GenericNominalTypeDefinitions)
        {
            var isImportedStruct
                = importedStructIdentities.Contains(definition.Identity);
            var isRelationNominal
                = relationNominalIdentities.Contains(definition.Identity);
            var isMethodSignatureNominal
                = methodSignatureNominalIdentities.Contains(
                    definition.Identity);
            if ((!definition.Formals.IsEmpty && !isRelationNominal
                    && !isMethodSignatureNominal)
                || !IsImportedNominalIdentity(definition.Identity)
                || definition.NominalKind is not (1 or 2)
                || (definition.NominalKind == 1 && !isImportedStruct
                    && !isRelationNominal && !isMethodSignatureNominal)
                || (definition.NominalKind == 2 && isImportedStruct))
            {
                return false;
            }
        }

        foreach (var instance in program.GenericNominalTypeInstances)
        {
            var isRelationNominal
                = relationNominalIdentities.Contains(instance.Definition);
            var isMethodSignatureNominal
                = methodSignatureNominalIdentities.Contains(
                    instance.Definition);
            if ((!instance.Arguments.IsEmpty && !isRelationNominal
                    && !isMethodSignatureNominal)
                || (instance.Qualifiers
                    & (isRelationNominal ? ~0x03 : ~0x01)) != 0
                || (isRelationNominal
                    ? instance.TypeSpecForm
                        is not (0 or 0x02 or 0x03 or 0x05 or 0x06)
                    : isMethodSignatureNominal
                        ? instance.TypeSpecForm
                            is not (0 or 0x02 or 0x03 or 0x05 or 0x06)
                        : instance.TypeSpecForm is not (0 or 0x02 or 0x03)
                            && !(instance.TypeSpecForm == 0x06
                                && standardClassIdentities.Contains(
                                    instance.Definition)))
                || !IsImportedNominalIdentity(instance.Definition))
            {
                return false;
            }
        }

        return true;
    }

    private static bool AddC1M1GenericMethodNominalIdentities(
        BoundProgram program,
        HashSet<BoundGenericNominalTypeIdentity> identities)
    {
        foreach (var instance in program.GenericNominalTypeInstances)
        {
            if (!instance.Definition.IsValid)
            {
                return false;
            }

            _ = identities.Add(instance.Definition);
        }

        return true;
    }

    private static void AddC1M1RelationNominalIdentities(
        BoundGenericMethodTypeTerm root,
        HashSet<BoundGenericNominalTypeIdentity> identities)
    {
        var pending = new Stack<(BoundGenericMethodTypeTerm Term, int Depth)>();
        pending.Push((root, 0));
        var remaining = SemanticVerifier.MaximumAnalysisVisits;
        while (pending.TryPop(out var current))
        {
            if (--remaining < 0
                || current.Depth > SemanticVerifier.MaximumDepth)
            {
                throw Unsupported(
                    "CM1AR1 relation TypeSpec identity traversal exceeds its limit.");
            }

            if (current.Term.NominalTypeIdentity
                is BoundGenericNominalTypeIdentity identity)
            {
                _ = identities.Add(identity);
            }

            foreach (var argument in current.Term.TypeArguments)
            {
                pending.Push((argument, checked(current.Depth + 1)));
            }
        }
    }

    private static void AddC1M1RelationNominalIdentities(
        BoundInterfaceType root,
        HashSet<BoundGenericNominalTypeIdentity> identities)
    {
        var pending = new Stack<(BoundInterfaceType Type, int Depth)>();
        pending.Push((root, 0));
        var remaining = SemanticVerifier.MaximumAnalysisVisits;
        while (pending.TryPop(out var current))
        {
            if (--remaining < 0
                || current.Depth > SemanticVerifier.MaximumDepth)
            {
                throw Unsupported(
                    "CM1AR1 relation interface traversal exceeds its limit.");
            }

            if (current.Type.GenericNominalTypeIdentity
                is BoundGenericNominalTypeIdentity identity)
            {
                _ = identities.Add(identity);
            }

            foreach (var argument in current.Type.TypeArguments)
            {
                if (argument.Type is not null)
                {
                    pending.Push((argument.Type,
                        checked(current.Depth + 1)));
                }
                if (argument.ConstantType is not null)
                {
                    pending.Push((argument.ConstantType,
                        checked(current.Depth + 1)));
                }
            }
        }
    }

    private static bool IsC1M1ImportedClassLayoutSupported(
        BoundProgram program,
        BoundStandardClassDeclaration declaration)
    {
        if (declaration.Expectations.IsDefault
            || declaration.SelectedExportKeys.IsDefault
            || declaration.ExportKeys.IsDefaultOrEmpty
            || (declaration.Expectations.IsEmpty
                && declaration.SelectedExportKeys.IsEmpty))
        {
            return false;
        }

        var view = declaration.Declaration;
        if (view.Identity.DefinitionId >> 24 != 0x13
            || (view.Identity.DefinitionId & 0x00FF_FFFF) == 0
            || view.Fields.IsDefault
            || (view.TypeDefFlags & 3) != 2
            || (view.TypeDefFlags >> 2 & 7) != 2
            || view.Layouts.IsDefault
            || view.Layouts.Length != 1
            || !AreC1M1ImportedClassMethodExpectationsSupported(
                program, declaration))
        {
            return false;
        }

        var layout = view.Layouts[0];
        if (layout.OwnerDefinitionId != view.Identity.DefinitionId
            || layout.TargetProfileReference != 0
            || layout.Kind != 0 || layout.Flags != 0
            || layout.Alignment != 0 || layout.Pack != 0
            || layout.FieldDefinitionIds.Length != view.Fields.Length)
        {
            return false;
        }

        for (var fieldIndex = 0; fieldIndex < view.Fields.Length;
            fieldIndex++)
        {
            var field = view.Fields[fieldIndex];
            if (field.DefinitionId >> 24 != 0x14
                || (field.DefinitionId & 0x00FF_FFFF) == 0
                || field.OwnerDefinitionId != view.Identity.DefinitionId
                || layout.FieldDefinitionIds[fieldIndex]
                    != field.DefinitionId)
            {
                return false;
            }
        }

        if (declaration.ExportKeys.IsDefaultOrEmpty)
        {
            return false;
        }

        var exportKeys = declaration.ExportKeys.ToHashSet(
            StringComparer.Ordinal);
        if (exportKeys.Count != declaration.ExportKeys.Length
            || declaration.SelectedExportKeys.Any(key =>
                !BoundEnumTypes.IsValidModuleDeclarationKey(key)
                || !exportKeys.Contains(key))
            || declaration.SelectedExportKeys.Distinct(
                StringComparer.Ordinal).Count()
                != declaration.SelectedExportKeys.Length)
        {
            return false;
        }

        return true;
    }

    private static bool AreC1M1ImportedClassMethodExpectationsSupported(
        BoundProgram program,
        BoundStandardClassDeclaration declaration)
    {
        foreach (var expectation in declaration.Expectations)
        {
            foreach (var methodExpectation in expectation.MethodExpectations)
            {
                if (expectation.ExpectedDeclaration.Identity.DefinitionId
                    != methodExpectation.ExpectedOwnerDefinitionId)
                {
                    return false;
                }

                var expectedMethods = expectation.ExpectedDeclaration.Methods
                    .Where(method => method.DefinitionId
                        == methodExpectation.ExpectedDefinitionId)
                    .ToArray();
                if (expectedMethods.Length != 1)
                {
                    return false;
                }

                var expectedMethod = expectedMethods[0];
                if (expectedMethod.GenericParameterCount == 0
                    && expectedMethod.GenericParameters.IsEmpty)
                {
                    continue;
                }

                var matches = program.RetainedGenericMethodRoots.Where(root =>
                        root.Origin.MethodDefId
                            == methodExpectation.ProviderDefinitionId
                        && StringComparer.Ordinal.Equals(
                            root.Origin.ProviderIdentity,
                            methodExpectation.Provider.ModuleIdentity)
                        && StringComparer.Ordinal.Equals(root.Origin.ExportKey,
                            methodExpectation.ExportKey)
                        && root.Origin.ClassOwner
                            is BoundGenericMethodClassOwner owner
                        && owner.ExpectedDefinitionId
                            == methodExpectation.ExpectedOwnerDefinitionId)
                    .ToArray();
                if (matches.Length != 1
                    || matches[0].Formals.IsDefaultOrEmpty
                    || matches[0].Signature.FormalOrder.IsDefault
                    || matches[0].Signature.FormalOrder.Any(
                        static slot =>
                            slot.Kind != BoundGenericMethodFormalKind.Type)
                    || matches[0].Formals.Any(static formal =>
                        formal.InterfaceConstraints.IsDefault
                        || formal.AssociatedRelations.IsDefault))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static C1M1ImportedClassImportPlan
        GetC1M1ImportedClassImportPlan(BoundProgram program,
            BoundStandardClassDeclaration declaration)
    {
        if (!IsC1M1ImportedClassLayoutSupported(program, declaration))
        {
            throw Unsupported(
                "C1M1 class source emission supports directly selected imported non-generic classes with one default Auto Layout variant.");
        }

        var view = declaration.Declaration;
        var exportKeys = declaration.ExportKeys.ToHashSet(
            StringComparer.Ordinal);
        var selectedExportKeys = new HashSet<string>(StringComparer.Ordinal);
        if (!declaration.SelectedExportKeys.IsEmpty)
        {
            foreach (var exportKey in declaration.SelectedExportKeys)
            {
                if (!BoundEnumTypes.IsValidModuleDeclarationKey(exportKey)
                    || !exportKeys.Contains(exportKey)
                    || !selectedExportKeys.Add(exportKey))
                {
                    throw new InvalidDataException(
                        "A selected imported class TypeDef key is absent from its checked provider view.");
                }
            }
        }
        else
        {
            foreach (var expectation in declaration.Expectations)
            {
                var binding = expectation.Binding;
                if (!SameA2SourceTypeProviderSnapshot(binding.Provider,
                        view.Provider)
                    || binding.ProviderDefinitionId
                        != view.Identity.DefinitionId
                    || !StringComparer.Ordinal.Equals(
                        binding.ProducerIdentity,
                        view.Provider.ModuleIdentity)
                    || binding.TargetRequirement
                        != view.Provider.TargetRequirement
                    || binding.RuntimePolicy != view.Provider.RuntimePolicy
                    || !BoundEnumTypes.IsValidModuleDeclarationKey(
                        binding.ExportKey)
                    || !exportKeys.Contains(binding.ExportKey))
                {
                    throw new InvalidDataException(
                        "An imported class TypeDef expectation lost its exact provider ExportKey.");
                }

                _ = selectedExportKeys.Add(binding.ExportKey);
            }
        }

        if (selectedExportKeys.Count == 0)
        {
            throw Unsupported(
                "C1M1 class source emission requires a selected class TypeDef Import or ExportKey.");
        }

        return new C1M1ImportedClassImportPlan(
            view.Provider, [.. selectedExportKeys]);
    }

    private static byte[] EmitC1M1MetadataClassTypeReference(
        BoundProgram program, string moduleIdentity,
        uint moduleTargetRequirement, string schemaIdentity)
    {
        var schema = GetC1M1ClassMetadataSchema(schemaIdentity);
        if (program.EntryPoint != -1
            || !program.Classes.IsEmpty
            || (!program.Interfaces.IsEmpty
                && !schema.UsesAssociatedRelationConstraints())
            || !program.InterfaceWitnesses.IsEmpty
            || !program.GenericMethodDefinitions.IsEmpty
            || !program.GenericMethodSignatures.IsEmpty
            || !program.SourceTypeDeclarations.IsEmpty
            || !program.SourceGenericTypeDefinitions.IsEmpty
            || !program.EnumDefinitions.IsEmpty
            || !program.SourceGenericTypeInstances.IsEmpty
            || !program.NativeFunctionPointerSignatures.IsEmpty
            || !program.NativePointerTypes.IsEmpty
            || !program.NativeFixedArrayTypes.IsEmpty
            || !AreC1M1ClassCheckedReferenceTypesSupported(
                program.CheckedReferenceTypes)
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
                "C1M1 class reference re-emission requires complete checked carriers.");
        }

        if (!BoundGenericNominalTypeContract.IsValidInstanceTable(
                program.GenericNominalTypeInstances,
                program.GenericNominalTypeDefinitions))
        {
            throw new InvalidDataException(
                "The retained C1M1 class reference has an invalid nominal type instance table.");
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
        var relationNominalIdentities
            = new HashSet<BoundGenericNominalTypeIdentity>();
        foreach (var retainedRoot in program.RetainedGenericMethodRoots)
        {
            var root = retainedRoot.Binding?.Expected ?? retainedRoot;
            foreach (var formal in root.Formals)
            {
                foreach (var relation in formal.AssociatedRelations)
                {
                    AddC1M1RelationNominalIdentities(
                        relation.RequiredType, relationNominalIdentities);
                    AddC1M1RelationNominalIdentities(
                        relation.AssociatedType.DeclaringInterface,
                        relationNominalIdentities);
                }
            }
        }

        if (program.StandardClassDeclarations.Any(declaration =>
                !program.GenericNominalTypeDefinitions.Any(definition =>
                    definition.Identity
                        == declaration.Declaration.Identity
                    && definition.NominalKind == 2)
                && !relationNominalIdentities.Contains(
                    declaration.Declaration.Identity))
            || program.GenericNominalTypeDefinitions.Any(definition =>
                (!definition.Formals.IsEmpty
                    && !relationNominalIdentities.Contains(
                        definition.Identity))
                    || (definition.NominalKind == 1
                        ? definition.ProviderExpectations.IsEmpty
                            && !relationNominalIdentities.Contains(
                                definition.Identity)
                            && (definition.Provider is not
                                    BoundSourceTypeProviderSnapshot provider
                                || !SourceTypeSnapshotsMatch(provider,
                                    rootDeclarations[0].Declaration.Provider))
                        : definition.NominalKind != 2
                            || (!classIdentities.Contains(
                                    definition.Identity)
                                && !relationNominalIdentities.Contains(
                                    definition.Identity))))
        {
            throw Unsupported(
                "C1M1 class re-emission cannot discard or invent a nominal definition.");
        }

        var rootSnapshot = rootDeclarations[0].Declaration.Provider;
        if (!StringComparer.Ordinal.Equals(rootSnapshot.SchemaIdentity,
                schemaIdentity)
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
                    schemaIdentity)
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
                schemaIdentity,
                provider.ArtifactSnapshot.ToArray()));
        }

        foreach (var declaration in program.StandardClassDeclarations)
        {
            foreach (var expectation in declaration.Expectations)
            {
                AddProviderInput(expectation.Binding.Provider);
                foreach (var fieldExpectation
                    in expectation.FieldExpectations)
                {
                    AddProviderInput(fieldExpectation.Provider);
                }
                foreach (var methodExpectation
                    in expectation.MethodExpectations)
                {
                    AddProviderInput(methodExpectation.Provider);
                }
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
        schemaIdentities[0] = schemaIdentity;
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
        var rootIndex = new TrRmArtifactIndex(artifacts[0], schema,
            standardSchemaIdentity: schemaIdentity);
        var rootTables = TrRmStructuralMetadataValidator
            .ValidateC1AndGetExecutionTables(rootIndex);
        EnsureC1ClassDeclarationTablesSupported(rootTables);
        var classProviderValidation
            = TrRmFixedArrayProviderBindingValidator
                .ValidateC1M1ClassImports(rootTables, artifacts,
                    schemaIdentities, moduleValidation.Modules,
                    rootModuleRow);
        ValidateC1M1ClassCarrierBindings(program, moduleValidation.Modules,
            artifacts, schemaIdentities, classProviderValidation.TypeBindings,
            classProviderValidation.MethodBindings);
        ValidateC1M1StructCarrierBindings(program, moduleValidation.Modules,
            artifacts, schemaIdentities,
            classProviderValidation.TypeBindings);
        if (!StringComparer.Ordinal.Equals(
                rootTables.Modules.GetIdentity(rootModuleRow),
                rootSnapshot.ModuleIdentity)
            || rootTables.Modules.GetRequirements(rootModuleRow)
                != (rootSnapshot.TargetRequirement,
                    rootSnapshot.RuntimePolicy)
            || rootTables.Modules.GetGoverningRuleEdition(rootModuleRow)
                != rootSnapshot.GoverningRuleEdition
            || rootTables.Modules.GetEntryMethod(rootModuleRow) != 0)
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
        var fieldBindingsByImportRow = new SortedDictionary<int,
            TrRmStandardFieldProviderBinding>();
        foreach (var fieldBinding in classProviderValidation.FieldBindings)
        {
            if (fieldBinding.ConsumerArtifactIndex != 0
                || fieldBinding.ConsumerModuleRow != rootModuleRow)
            {
                continue;
            }

            if (!fieldBindingsByImportRow.TryAdd(fieldBinding.ImportRow,
                    fieldBinding))
            {
                throw new InvalidDataException(
                    "A retained C1M1 FieldDef Import has conflicting provider bindings.");
            }
        }

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

                foreach (var fieldExpectation
                    in expectation.FieldExpectations)
                {
                    if (fieldExpectation.ImportRow <= 0
                        || fieldExpectation.ImportRow
                            > rootTables.Imports.Count)
                    {
                        throw new InvalidDataException(
                            "A retained class FieldDef Import row is out of range.");
                    }

                    var fieldRow = GetFieldDefRow(
                        fieldExpectation.ExpectedDefinitionId);
                    var expectedOwnerTypeDefRow = GetTypeDefRow(
                        fieldExpectation.ExpectedOwnerDefinitionId);
                    var fieldModuleRefRow = rootTables.Imports
                        .GetModuleRefForImportRow(
                            fieldExpectation.ImportRow);
                    var fieldImportBinding = rootTables.Imports
                        .GetBindingForImportRow(
                            fieldExpectation.ImportRow);
                    var fieldRequirements = rootTables.ModuleRefs
                        .GetRequirements(fieldModuleRefRow);
                    if ((fieldExpectation.ExpectedDefinitionId >> 24)
                            != 0x14
                        || (fieldExpectation.ProviderDefinitionId >> 24)
                            != 0x14
                        || (fieldExpectation.ExpectedOwnerDefinitionId
                            >> 24) != 0x13
                        || (fieldExpectation.ProviderOwnerDefinitionId
                            >> 24) != 0x13
                        || fieldExpectation.OwnerImportRow
                            != binding.ImportRow
                        || fieldExpectation.ExpectedOwnerDefinitionId
                            != binding.ExpectedDefinitionId
                        || fieldExpectation.ProviderOwnerDefinitionId
                            != binding.ProviderDefinitionId
                        || rootTables.TypeDefs.GetOwnerModule(
                            expectedOwnerTypeDefRow) != rootModuleRow
                        || rootTables.Fields.GetOwnerTypeDef(fieldRow)
                            != expectedOwnerTypeDefRow
                        || !rootTables.Imports.IsExpectedDefinition(
                            fieldExpectation.ExpectedDefinitionId)
                        || !fieldBindingsByImportRow.TryGetValue(
                            fieldExpectation.ImportRow,
                            out var resolvedFieldBinding)
                        || resolvedFieldBinding.ExpectedFieldDefRow
                            != fieldRow
                        || resolvedFieldBinding.ExpectedOwnerTypeDefRow
                            != expectedOwnerTypeDefRow
                        || resolvedFieldBinding.OwnerImportRow
                            != fieldExpectation.OwnerImportRow
                        || resolvedFieldBinding.ProviderFieldDefRow
                            != GetFieldDefRow(
                                fieldExpectation.ProviderDefinitionId)
                        || resolvedFieldBinding.ProviderTypeDefRow
                            != GetTypeDefRow(
                                fieldExpectation.ProviderOwnerDefinitionId)
                        || resolvedFieldBinding.ProviderModuleRow
                            != fieldExpectation.Provider.ModuleRow
                        || !StringComparer.Ordinal.Equals(
                            resolvedFieldBinding.ProducerIdentity,
                            fieldExpectation.Provider.ModuleIdentity)
                        || !StringComparer.Ordinal.Equals(
                            resolvedFieldBinding.ExportKey,
                            fieldExpectation.ExportKey)
                        || resolvedFieldBinding.TargetRequirement
                            != fieldExpectation.Provider.TargetRequirement
                        || resolvedFieldBinding.RuntimePolicy
                            != fieldExpectation.Provider.RuntimePolicy
                        || rootTables.Imports
                            .GetExpectedDefinitionForImportRow(
                                fieldExpectation.ImportRow)
                            != fieldExpectation.ExpectedDefinitionId
                        || rootTables.ModuleRefs.GetOwnerModule(
                            fieldModuleRefRow) != rootModuleRow
                        || !SourceTypeSnapshotsMatch(
                            fieldExpectation.Provider, binding.Provider)
                        || !StringComparer.Ordinal.Equals(
                            fieldImportBinding.ExportKey,
                            fieldExpectation.ExportKey)
                        || !StringComparer.Ordinal.Equals(
                            rootTables.ModuleRefs.GetProducerIdentity(
                                fieldModuleRefRow),
                            fieldExpectation.Provider.ModuleIdentity)
                        || fieldRequirements
                            != (fieldExpectation.Provider.TargetRequirement,
                                fieldExpectation.Provider.RuntimePolicy))
                    {
                        throw new InvalidDataException(
                            "A retained class FieldDef Import no longer matches its owner and selected provider.");
                    }
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
            if (definition.NominalKind != 1
                || !definition.ProviderExpectations.IsEmpty
                || definition.Provider is not
                    BoundSourceTypeProviderSnapshot provider
                || !SourceTypeSnapshotsMatch(provider, rootSnapshot))
            {
                continue;
            }

            var typeDefRow = GetTypeDefRow(definition.Identity.DefinitionId);
            if (rootTables.TypeDefs.GetOwnerModule(typeDefRow)
                    != rootModuleRow
                || rootTables.TypeDefs.GetKind(typeDefRow) != 1
                || rootTables.TypeDefs.GetGenericRange(typeDefRow).Count != 0
                || rootTables.TypeDefs.GetContractRange(typeDefRow).Count
                    != 0
                || !GetStandardTypeExportKeys(rootTables, rootModuleRow,
                        typeDefRow).AsSpan()
                    .SequenceEqual(definition.ExportKeys.AsSpan()))
            {
                throw Invalid(
                    "A direct class-context struct TypeDef no longer matches its retained provider view.");
            }

            var view = CreateC1M1TypeDefEmissionView(rootTables,
                rootSnapshot, typeDefRow);
            if (typeDefViewsByOldRow.ContainsKey(typeDefRow))
            {
                continue;
            }

            typeDefViewsByOldRow.Add(typeDefRow, view);
            exportsByOldRow.Add(typeDefRow, definition.ExportKeys);
        }
        var nominalTypeImportRows = new SortedSet<int>();
        foreach (var definition in program.GenericNominalTypeDefinitions)
        {
            if (definition.NominalKind is not (1 or 2))
            {
                continue;
            }

            if (!definition.Formals.IsEmpty)
            {
                if (!relationNominalIdentities.Contains(
                        definition.Identity))
                {
                    throw Unsupported(
                        "C1M1 class re-emission does not retain unrelated generic nominal definitions.");
                }

                foreach (var expectation in definition.ProviderExpectations)
                {
                    if (!SourceTypeSnapshotsMatch(
                            expectation.UsingModule, rootSnapshot))
                    {
                        continue;
                    }

                    var expectedTypeDefRow = GetTypeDefRow(
                        expectation.ExpectedDefinitionId);
                    var moduleRefRow = rootTables.Imports
                        .GetModuleRefForImportRow(expectation.ImportRow);
                    var binding
                        = rootTables.Imports.GetBindingForImportRow(
                            expectation.ImportRow);
                    var requirements = rootTables.ModuleRefs
                        .GetRequirements(moduleRefRow);
                    if ((expectation.ExpectedDefinitionId >> 24) != 0x13
                        || rootTables.TypeDefs.GetOwnerModule(
                            expectedTypeDefRow) != rootModuleRow
                        || rootTables.TypeDefs.GetKind(expectedTypeDefRow)
                            != definition.NominalKind
                        || rootTables.TypeDefs.GetGenericRange(
                            expectedTypeDefRow).Count
                            != definition.Formals.Length
                        || rootTables.TypeDefs.GetContractRange(
                            expectedTypeDefRow).Count != 0
                        || rootTables.Imports
                            .GetExpectedDefinitionForImportRow(
                                expectation.ImportRow)
                            != expectation.ExpectedDefinitionId
                        || !StringComparer.Ordinal.Equals(
                            binding.ExportKey, expectation.ExportKey)
                        || !StringComparer.Ordinal.Equals(
                            rootTables.ModuleRefs
                                .GetProducerIdentity(moduleRefRow),
                            expectation.ProducerIdentity)
                        || requirements
                            != (expectation.TargetRequirement,
                                expectation.RuntimePolicy))
                    {
                        throw new InvalidDataException(
                            "A retained CM1AR1 generic nominal Import expectation does not match its provider snapshot.");
                    }

                    _ = nominalTypeImportRows.Add(expectation.ImportRow);
                }

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
                    || rootTables.TypeDefs.GetKind(expectedTypeDefRow)
                        != definition.NominalKind
                    || rootTables.TypeDefs.GetGenericRange(
                        expectedTypeDefRow).Count != 0
                    || rootTables.TypeDefs.GetContractRange(
                        expectedTypeDefRow).Count != 0
                    || (bindingsByImportRow.TryGetValue(
                            expectation.ImportRow, out var existingBinding)
                        ? existingBinding != expectation
                        : !bindingsByImportRow.TryAdd(
                            expectation.ImportRow, expectation)))
                {
                    throw new InvalidDataException(
                        "A retained C1M1 nominal Import expectation does not match its root snapshot.");
                }

                var typeDefView = CreateC1M1TypeDefEmissionView(
                    rootTables, rootSnapshot, expectedTypeDefRow);
                if (typeDefView.Kind != definition.NominalKind)
                {
                    throw new InvalidDataException(
                        "A retained C1M1 nominal Import TypeDef has an invalid kind.");
                }

                if (!typeDefViewsByOldRow.ContainsKey(expectedTypeDefRow))
                {
                    if (!typeDefViewsByOldRow.TryAdd(expectedTypeDefRow,
                            typeDefView)
                        || !exportsByOldRow.TryAdd(expectedTypeDefRow, []))
                    {
                        throw new InvalidDataException(
                            "A retained C1M1 nominal Import TypeDef is duplicated.");
                    }
                }
            }
        }
        var methodConstraintTypeSpecSeeds
            = schema.UsesAssociatedRelationConstraints()
                ? ImmutableArray<uint>.Empty
                : GetC1M1ClassMethodConstraintTypeSpecSeeds(rootTables,
                    rootModuleRow, typeDefViewsByOldRow.Keys,
                    usesAssociatedRelationConstraints: false);
        var moduleFunctionTypeSpecSeeds
            = ImmutableArray.CreateBuilder<uint>();
        var moduleFunctionTypeSpecSeedSet = new HashSet<uint>();
        void AddModuleFunctionTypeSpecSeed(uint typeSpecReference)
        {
            if (moduleFunctionTypeSpecSeedSet.Add(typeSpecReference))
            {
                moduleFunctionTypeSpecSeeds.Add(typeSpecReference);
            }
        }

        var (sourceMethodStart, sourceMethodEnd)
            = rootTables.Modules.GetMethodDefRange(rootModuleRow);
        for (var methodRow = sourceMethodStart;
            methodRow < sourceMethodEnd; methodRow++)
        {
            if (rootTables.Methods.GetOwnerType(methodRow) != 0)
            {
                continue;
            }

            var signatureRow = rootTables.Methods.GetSignature(methodRow);
            if (rootTables.Signatures.GetGenericRange(signatureRow).Count
                != 0)
            {
                continue;
            }

            AddModuleFunctionTypeSpecSeed(
                rootTables.Signatures.GetResultTypeSpec(signatureRow));
            var (parameterStart, parameterCount)
                = rootTables.Signatures.GetParamRange(signatureRow);
            for (var ordinal = 0; ordinal < parameterCount; ordinal++)
            {
                AddModuleFunctionTypeSpecSeed(
                    rootTables.Parameters.GetTypeSpec(
                        parameterStart + ordinal));
            }

            var bodyRow = rootTables.Methods.GetBodyRow(methodRow);
            if (bodyRow == 0
                || rootTables.Bodies.GetRow(bodyRow).Span[0] != 0x01)
            {
                continue;
            }

            var body = rootTables.Bodies.GetRegisterBody(bodyRow,
                rootTables.Contracts.Count);
            for (var typeMapIndex = 0; typeMapIndex < body.TypeCount;
                typeMapIndex++)
            {
                var token = BinaryPrimitives.ReadUInt32LittleEndian(
                    body.TypeMap.Slice(typeMapIndex * sizeof(uint),
                        sizeof(uint)));
                if ((byte)(token >> 24) == 0x11)
                {
                    AddModuleFunctionTypeSpecSeed(
                        token & 0x00FF_FFFFu);
                }
            }

            for (var fieldMapIndex = 0; fieldMapIndex < body.FieldCount;
                fieldMapIndex++)
            {
                var token = BinaryPrimitives.ReadUInt32LittleEndian(
                    body.FieldMap.Slice(fieldMapIndex * sizeof(uint),
                        sizeof(uint)));
                if ((byte)(token >> 24) == 0x1D)
                {
                    AddModuleFunctionTypeSpecSeed(
                        rootTables.FieldPaths.GetRootTypeSpecRef(
                            checked((int)(token & 0x00FF_FFFFu))));
                }
            }
        }

        AddC1M1LocalNonGenericTypeClosure(rootTables, rootModuleRow,
            rootSnapshot, typeDefViewsByOldRow, exportsByOldRow,
            [.. methodConstraintTypeSpecSeeds,
                .. moduleFunctionTypeSpecSeeds]);

        var methodBindingsByImportRow = new SortedDictionary<int,
            BoundStandardClassMethodExpectation>();
        var expectedMethodsByOldRow = new SortedDictionary<int,
            BoundStandardClassMethodDeclaration>();
        var expectedMethodOwnersByOldRow = new Dictionary<int,
            BoundStandardClassTypeView>();
        var methodImportsByExpectedMethodRow = new Dictionary<int,
            List<BoundStandardClassMethodExpectation>>();
        foreach (var declaration in program.StandardClassDeclarations)
        {
            foreach (var expectation in declaration.Expectations)
            {
                var classBinding = expectation.Binding;
                if (!SourceTypeSnapshotsMatch(classBinding.UsingModule,
                        rootSnapshot))
                {
                    continue;
                }

                foreach (var methodExpectation
                    in expectation.MethodExpectations)
                {
                    var methodRow = GetMethodDefRow(
                        methodExpectation.ExpectedDefinitionId);
                    var method = expectation.ExpectedDeclaration.Methods
                        .SingleOrDefault(candidate =>
                            candidate.DefinitionId
                                == methodExpectation.ExpectedDefinitionId);
                    if (method is null
                        || expectation.ExpectedDeclaration.Identity
                            .DefinitionId
                            != methodExpectation.ExpectedOwnerDefinitionId
                        || methodExpectation.OwnerImportRow
                            != classBinding.ImportRow
                        || methodExpectation.ExpectedOwnerDefinitionId
                            != classBinding.ExpectedDefinitionId
                        || !bindingsByImportRow.ContainsKey(
                            methodExpectation.OwnerImportRow)
                        || !methodBindingsByImportRow.TryAdd(
                            methodExpectation.ImportRow, methodExpectation)
                        || rootTables.Methods.GetOwnerType(methodRow)
                            != GetTypeDefRow(
                                methodExpectation.ExpectedOwnerDefinitionId)
                        || rootTables.Methods.GetBodyRow(methodRow) != 0
                        || !rootTables.Imports.IsExpectedDefinition(
                            methodExpectation.ExpectedDefinitionId))
                    {
                        throw new InvalidDataException(
                            "A retained class MethodDef Import has an invalid or unsupported local expectation.");
                    }

                    if (expectedMethodsByOldRow.TryGetValue(methodRow,
                            out var existingMethod))
                    {
                        if (!StandardClassMethodsMatch(existingMethod,
                                method))
                        {
                            throw new InvalidDataException(
                                "One expected class MethodDef has conflicting retained views.");
                        }
                    }
                    else
                    {
                        expectedMethodsByOldRow.Add(methodRow, method);
                        expectedMethodOwnersByOldRow.Add(methodRow,
                            expectation.ExpectedDeclaration);
                        methodImportsByExpectedMethodRow.Add(methodRow, []);
                    }

                    if (!methodImportsByExpectedMethodRow.TryGetValue(
                            methodRow, out var methodImports))
                    {
                        throw new InvalidDataException(
                            "A retained class MethodDef Import has no expected method group.");
                    }

                    methodImports.Add(methodExpectation);
                }
            }
        }

        var methodEmissionPlans = new SortedDictionary<int,
            C1M1MethodEmissionPlan>();
        foreach (var (methodRow, method) in expectedMethodsByOldRow)
        {
            var imports = methodImportsByExpectedMethodRow[methodRow];
            var firstImport = imports[0];
            if (imports.Any(import =>
                    import.ProviderDefinitionId
                        != firstImport.ProviderDefinitionId
                    || import.ProviderOwnerDefinitionId
                        != firstImport.ProviderOwnerDefinitionId
                    || !StringComparer.Ordinal.Equals(
                        import.Provider.ModuleIdentity,
                        firstImport.Provider.ModuleIdentity)
                    || import.Provider.ModuleRow
                        != firstImport.Provider.ModuleRow
                    || !import.Provider.ProviderArtifactDigest.AsSpan()
                        .SequenceEqual(
                            firstImport.Provider.ProviderArtifactDigest
                                .AsSpan())))
            {
                throw new InvalidDataException(
                    "A class MethodDef alias group resolved through distinct provider views.");
            }

            methodEmissionPlans.Add(methodRow,
                new C1M1MethodEmissionPlan(
                    expectedMethodOwnersByOldRow[methodRow], method,
                    [.. imports], [], ReadOnlyMemory<byte>.Empty));
        }

        foreach (var declaration in rootDeclarations)
        {
            var owner = declaration.Declaration;
            var ownerTypeDefRow
                = GetTypeDefRow(owner.Identity.DefinitionId);
            foreach (var method in owner.Methods)
            {
                var methodRow = GetMethodDefRow(method.DefinitionId);
                var bodyRow = checked((int)method.BodyRow);
                if (bodyRow == 0
                    || !schema.UsesConstructionContracts()
                    || rootTables.TypeDefs.GetOwnerModule(ownerTypeDefRow)
                        != rootModuleRow
                    || rootTables.Methods.GetOwnerType(methodRow)
                        != ownerTypeDefRow
                    || rootTables.Methods.GetBodyRow(methodRow)
                        != method.BodyRow
                    || methodEmissionPlans.ContainsKey(methodRow))
                {
                    throw Unsupported(
                        "CM1 local class MethodDef Bodies require the exact Construction-2 profile and root-owned Body rows.");
                }

                var bodyBytes = rootTables.Bodies.GetRow(bodyRow);
                if (bodyBytes.IsEmpty)
                {
                    throw new InvalidDataException(
                        "A retained local class MethodDef Body is empty.");
                }

                if (bodyBytes[0] == 0x01)
                {
                    var body = rootTables.Bodies.GetRegisterBody(bodyRow,
                        rootTables.Contracts.Count);
                    for (var typeMapIndex = 0;
                        typeMapIndex < body.TypeCount; typeMapIndex++)
                    {
                        var typeMapToken = BinaryPrimitives
                            .ReadUInt32LittleEndian(body.TypeMap.Slice(
                                typeMapIndex * sizeof(uint),
                                sizeof(uint)));
                        var encoding = (byte)(typeMapToken >> 24);
                        if (encoding == 0x11)
                        {
                            _ = rootTables.TypeSpecs.GetTypeSpecRef(
                                typeMapToken & 0x00FF_FFFF);
                        }
                        else if (encoding is not (0xF2 or 0xF3))
                        {
                            throw Unsupported(
                                "CM1 local class MethodDef Bodies require inline or TypeSpec-backed TypeMap entries.");
                        }
                    }
                }
                else if (bodyBytes[0] is not (0x02 or 0x03 or 0x04 or 0x05))
                {
                    throw Unsupported(
                        "CM1 local class MethodDef re-emission supports Body kind 01 streams, kinds 02-04 field accessors and kind 05 identity forwarders.");
                }

                methodEmissionPlans.Add(methodRow,
                    new C1M1MethodEmissionPlan(owner, method, [],
                        method.ExportKeys,
                        bodyBytes.ToArray()));
            }
        }

        var moduleFunctionImportRowsByImportRow
            = new SortedDictionary<int, int>();
        var moduleFunctionImportRowByMethodRow
            = new SortedDictionary<int, List<int>>();
        var moduleFunctionProviderExports
            = new HashSet<(string ProducerIdentity, string ExportKey,
                uint TargetRequirement, uint RuntimePolicy)>();
        for (var importRow = 1;
            importRow <= rootTables.Imports.Count; importRow++)
        {
            var moduleRefRow = rootTables.Imports
                .GetModuleRefForImportRow(importRow);
            if (rootTables.ModuleRefs.GetOwnerModule(moduleRefRow)
                    != rootModuleRow
                || (rootTables.Imports
                    .GetExpectedDefinitionForImportRow(importRow) >> 24)
                    != 0x16)
            {
                continue;
            }

            var expectedDefinition = rootTables.Imports
                .GetExpectedDefinitionForImportRow(importRow);
            var methodRow = GetMethodDefRow(expectedDefinition);
            if (rootTables.Methods.GetOwnerType(methodRow) != 0)
            {
                continue;
            }

            if (rootTables.Methods.GetOwnerModule(methodRow) != rootModuleRow
                || rootTables.Methods.GetBodyRow(methodRow) != 0)
            {
                throw new InvalidDataException(
                    "A top-level MethodDef Import is duplicated or has a local implementation.");
            }

            moduleFunctionImportRowsByImportRow.Add(importRow, methodRow);
            if (!moduleFunctionImportRowByMethodRow.TryGetValue(methodRow,
                    out var methodImportRows))
            {
                methodImportRows = [];
                moduleFunctionImportRowByMethodRow.Add(methodRow,
                    methodImportRows);
            }
            methodImportRows.Add(importRow);
            var binding = rootTables.Imports
                .GetBindingForImportRow(importRow);
            var requirements = rootTables.ModuleRefs.GetRequirements(
                moduleRefRow);
            if (!StringComparer.Ordinal.Equals(
                    binding.ProducerIdentity,
                    rootTables.ModuleRefs.GetProducerIdentity(moduleRefRow))
                || binding.TargetRequirement
                    != requirements.TargetRequirement
                || binding.RuntimePolicy != requirements.RuntimePolicy)
            {
                throw new InvalidDataException(
                    "A top-level MethodDef Import differs from its ModuleRef binding.");
            }

            _ = moduleFunctionProviderExports.Add((
                binding.ProducerIdentity, binding.ExportKey,
                binding.TargetRequirement, binding.RuntimePolicy));
        }

        var moduleFunctionCandidates
            = new List<(int FunctionIndex, int MethodDefRow,
                BoundFunction Function, string ExportKey)>();
        for (var functionIndex = 0;
            functionIndex < program.Functions.Length; functionIndex++)
        {
            var function = program.Functions[functionIndex];
            if (function.DeclaringStruct is not null
                || function.ModuleImport is not BoundModuleImport import
                || !function.IsExternal || !function.IsModuleReference)
            {
                continue;
            }

            if (!StringComparer.Ordinal.Equals(
                    import.ProducerIdentity, rootSnapshot.ModuleIdentity))
            {
                if (!moduleFunctionProviderExports.Contains((
                        import.ProducerIdentity, import.ExportKey,
                        import.TargetRequirement, import.RuntimePolicy)))
                {
                    throw Unsupported(
                        "A retained top-level class-reference function has no exact root MethodDef Import.");
                }

                continue;
            }

            if (!schema.UsesObjectConstruction()
                || import.TargetRequirement != rootSnapshot.TargetRequirement
                || import.RuntimePolicy != rootSnapshot.RuntimePolicy
                || !StringComparer.Ordinal.Equals(function.Name,
                    import.ExportKey)
                || !rootTables.Exports.TryGetDefinitionForKey(
                    rootModuleRow, import.ExportKey, out var definition)
                || (definition >> 24) != 0x16)
            {
                throw Unsupported(
                    "Top-level class-reference function re-emission requires an exact Construction-2 Export from its root Module.");
            }

            var methodRow = GetMethodDefRow(definition);
            var signatureRow = rootTables.Methods.GetSignature(methodRow);
            if (rootTables.Methods.GetOwnerModule(methodRow) != rootModuleRow
                || rootTables.Methods.GetOwnerType(methodRow) != 0
                || rootTables.Methods.GetBodyRow(methodRow) == 0
                || rootTables.Signatures.GetReceiverTypeSpec(signatureRow)
                    != 0
                || rootTables.Signatures.GetGenericRange(signatureRow)
                    .Count != 0)
            {
                throw Unsupported(
                    "Top-level class-reference functions must target bodyful nongeneric module MethodDefs without receivers.");
            }

            moduleFunctionCandidates.Add((functionIndex, methodRow,
                function, import.ExportKey));
        }

        var moduleFunctionEmissionPlans = new SortedDictionary<int,
            C1M1ModuleFunctionEmissionPlan>();
        var moduleFunctionIndexes = new HashSet<int>();
        foreach (var group in moduleFunctionCandidates
            .OrderBy(static candidate => candidate.MethodDefRow)
            .ThenBy(static candidate => candidate.ExportKey,
                StringComparer.Ordinal)
            .GroupBy(static candidate => candidate.MethodDefRow))
        {
            var aliases = group.ToArray();
            var representative = aliases[0];
            var exportKeys = aliases
                .Select(static alias => alias.Function.Name)
                .Distinct(StringComparer.Ordinal)
                .ToImmutableArray();
            if (exportKeys.Length != aliases.Length
                || !moduleFunctionEmissionPlans.TryAdd(
                    representative.MethodDefRow,
                    new C1M1ModuleFunctionEmissionPlan(
                        representative.MethodDefRow,
                        representative.Function, exportKeys, [])))
            {
                throw new InvalidDataException(
                    "A top-level class-reference MethodDef has conflicting retained ExportKeys.");
            }

            foreach (var alias in aliases)
            {
                if (!moduleFunctionIndexes.Add(alias.FunctionIndex))
                {
                    throw new InvalidDataException(
                        "A top-level class-reference function view was retained more than once.");
                }
            }
        }

        foreach (var (methodRow, importRows)
            in moduleFunctionImportRowByMethodRow)
        {
            if (!moduleFunctionEmissionPlans.TryAdd(methodRow,
                    new C1M1ModuleFunctionEmissionPlan(methodRow, null, [],
                        [.. importRows])))
            {
                throw new InvalidDataException(
                    "A top-level MethodDef Import overlaps a retained function definition.");
            }
        }

        var exportedMethodDefinitionIds = rootTables.Exports
            .GetExportsForModule(rootModuleRow)
            .Where(static export => (export.Definition >> 24) == 0x16)
            .Select(static export => export.Definition)
            .ToHashSet();
        var privateModuleFunctionEmissionPlans = new SortedDictionary<int,
            C1M1PrivateModuleFunctionEmissionPlan>();
        var (privateMethodStart, privateMethodEnd)
            = rootTables.Modules.GetMethodDefRange(rootModuleRow);
        for (var methodRow = privateMethodStart;
            methodRow < privateMethodEnd; methodRow++)
        {
            if (methodEmissionPlans.ContainsKey(methodRow)
                || moduleFunctionEmissionPlans.ContainsKey(methodRow)
                || rootTables.Methods.GetOwnerType(methodRow) != 0)
            {
                continue;
            }

            var definitionId = 0x1600_0000u
                | checked((uint)methodRow);
            if (exportedMethodDefinitionIds.Contains(definitionId)
                || rootTables.Imports.IsExpectedDefinition(definitionId))
            {
                continue;
            }

            var signatureRow = rootTables.Methods.GetSignature(methodRow);
            var bodyRow = rootTables.Methods.GetBodyRow(methodRow);
            var flags = rootTables.Methods.GetFlags(methodRow);
            if (!schema.UsesObjectConstruction()
                || (flags & 3) is not (0 or 1)
                || (flags & 4) != 0
                || rootTables.Methods.GetOwnerModule(methodRow)
                    != rootModuleRow
                || bodyRow == 0
                || rootTables.Signatures.GetReceiverTypeSpec(signatureRow) != 0
                || rootTables.Signatures.GetGenericRange(signatureRow).Count
                    != 0)
            {
                throw Unsupported(
                    "Unexported top-level class-reference helpers require bodyful nongeneric Construction-2 MethodDefs without receivers.");
            }

            var body = rootTables.Bodies.GetRow(bodyRow);
            if (body.IsEmpty
                || body.Span[0] is not
                    (0x01 or 0x02 or 0x03 or 0x04 or 0x05)
                || !privateModuleFunctionEmissionPlans.TryAdd(methodRow,
                    new C1M1PrivateModuleFunctionEmissionPlan(methodRow)))
            {
                throw Unsupported(
                    "An unexported top-level class-reference helper has an unsupported Body or duplicate MethodDef.");
            }
        }

        var interfaceImportRows = new SortedSet<int>();

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
            var definitionKind = rootTables.Imports
                .GetExpectedDefinitionForImportRow(importRow) >> 24;
            if (schema.UsesAssociatedRelationConstraints()
                && IsC1M1RetainedInterfaceImport(program, rootTables,
                    importRow))
            {
                _ = interfaceImportRows.Add(importRow);
                continue;
            }
            if (nominalTypeImportRows.Contains(importRow))
            {
                continue;
            }

            if (definitionKind is not (0x13 or 0x14 or 0x16)
                || (definitionKind == 0x13
                    ? !bindingsByImportRow.ContainsKey(importRow)
                    : definitionKind == 0x14
                        ? !fieldBindingsByImportRow.ContainsKey(importRow)
                        : !methodBindingsByImportRow.ContainsKey(importRow)
                            && !moduleFunctionImportRowsByImportRow
                                .ContainsKey(importRow)))
            {
                throw new NotSupportedException(
                    "C1M1 class re-emission only retains selected TypeDef, FieldDef and bodyless MethodDef Imports.");
            }
        }

        if (typeDefViewsByOldRow.Count == 0
            || bindingsByImportRow.Count + fieldBindingsByImportRow.Count
                + methodBindingsByImportRow.Count
                + moduleFunctionImportRowsByImportRow.Count
                + interfaceImportRows.Count
                + nominalTypeImportRows.Count != rootImportCount)
        {
            throw new InvalidDataException(
                "The retained C1M1 class provider closure is incomplete.");
        }

        var (methodStart, methodEnd)
            = rootTables.Modules.GetMethodDefRange(rootModuleRow);
        var retainedMethodDefRows = methodEmissionPlans.Keys.ToHashSet();
        foreach (var methodRow in moduleFunctionEmissionPlans.Keys)
        {
            if (!retainedMethodDefRows.Add(methodRow))
            {
                throw new InvalidDataException(
                    "A top-level module function overlaps a retained class MethodDef.");
            }
        }
        foreach (var methodRow in privateModuleFunctionEmissionPlans.Keys)
        {
            if (!retainedMethodDefRows.Add(methodRow))
            {
                throw new InvalidDataException(
                    "An unexported top-level helper overlaps another retained MethodDef.");
            }
        }
        if (methodEnd - methodStart != retainedMethodDefRows.Count
            || Enumerable.Range(methodStart, methodEnd - methodStart)
                .Any(methodRow => !retainedMethodDefRows.Contains(methodRow)))
        {
            throw Unsupported(
                "C1M1 class re-emission only retains selected class MethodDefs and supported top-level module functions.");
        }

        var moduleRefStartEnd
            = rootTables.Modules.GetModuleRefRange(rootModuleRow);
        var moduleRefRowsByOldRow = new SortedSet<int>();
        foreach (var importRow in bindingsByImportRow.Keys
            .Concat(fieldBindingsByImportRow.Keys)
            .Concat(methodBindingsByImportRow.Keys)
            .Concat(moduleFunctionImportRowsByImportRow.Keys)
            .Concat(interfaceImportRows)
            .Concat(nominalTypeImportRows).Order())
        {
            if (interfaceImportRows.Contains(importRow)
                || nominalTypeImportRows.Contains(importRow))
            {
                _ = moduleRefRowsByOldRow.Add(rootTables.Imports
                    .GetModuleRefForImportRow(importRow));
                continue;
            }
            if (moduleFunctionImportRowsByImportRow.ContainsKey(importRow))
            {
                _ = moduleRefRowsByOldRow.Add(rootTables.Imports
                    .GetModuleRefForImportRow(importRow));
                continue;
            }

            var isMethodImport
                = methodBindingsByImportRow.TryGetValue(importRow,
                    out var methodBinding);
            var isFieldImport
                = fieldBindingsByImportRow.TryGetValue(importRow,
                    out var fieldBinding);
            var binding = isMethodImport || isFieldImport ? null
                : bindingsByImportRow[importRow];
            var expectedDefinitionId = isMethodImport
                ? methodBinding!.ExpectedDefinitionId
                : isFieldImport
                    ? 0x1400_0000u
                        | checked((uint)fieldBinding!.ExpectedFieldDefRow)
                    : binding!.ExpectedDefinitionId;
            var producerIdentity = isMethodImport
                ? methodBinding!.Provider.ModuleIdentity
                : isFieldImport ? fieldBinding!.ProducerIdentity
                    : binding!.ProducerIdentity;
            var exportKey = isMethodImport
                ? methodBinding!.ExportKey
                : isFieldImport ? fieldBinding!.ExportKey
                    : binding!.ExportKey;
            var targetRequirement = isMethodImport
                ? methodBinding!.Provider.TargetRequirement
                : isFieldImport ? fieldBinding!.TargetRequirement
                    : binding!.TargetRequirement;
            var runtimePolicy = isMethodImport
                ? methodBinding!.Provider.RuntimePolicy
                : isFieldImport ? fieldBinding!.RuntimePolicy
                    : binding!.RuntimePolicy;
            if (importRow <= 0 || importRow > rootTables.Imports.Count
                || (rootTables.Imports
                        .GetExpectedDefinitionForImportRow(importRow) >> 24)
                    != (isMethodImport ? 0x16u
                        : isFieldImport ? 0x14u : 0x13u)
                || rootTables.Imports.GetExpectedDefinitionForImportRow(
                    importRow) != expectedDefinitionId
                || !StringComparer.Ordinal.Equals(
                    rootTables.Imports.GetBindingForImportRow(importRow)
                        .ExportKey, exportKey)
                || !StringComparer.Ordinal.Equals(producerIdentity,
                    rootTables.ModuleRefs.GetProducerIdentity(
                        rootTables.Imports.GetModuleRefForImportRow(
                            importRow)))
                || targetRequirement
                    != rootTables.ModuleRefs.GetRequirements(
                        rootTables.Imports.GetModuleRefForImportRow(
                            importRow)).TargetRequirement
                || runtimePolicy
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

        var typeDefBindingEqualityRows
            = new List<TrRmTypeDefBindingEqualityRow>();
        if (IsSelectedClassTypeDefBindingEqualitySchemaIdentity(
                schemaIdentity))
        {
            void AddTypeDefBindingEqualityGroup(
                BoundStandardTypeDefBindingEqualityGroup group)
            {
                if (group.OwnerModule is
                        BoundSourceTypeProviderSnapshot ownerModule
                    && !SourceTypeSnapshotsMatch(ownerModule, rootSnapshot))
                {
                    if (!ownerModule.ArtifactSnapshot.AsSpan().SequenceEqual(
                            rootSnapshot.ArtifactSnapshot.AsSpan()))
                    {
                        return;
                    }

                    throw Unsupported(
                        "C1M1 class equality groups in another owner Module of the root artifact are not yet re-emitted.");
                }

                var typeDefRows = new SortedSet<int>();
                foreach (var endpoint in group.Endpoints)
                {
                    if (!SourceTypeSnapshotsMatch(endpoint.OwnerModule,
                            rootSnapshot))
                    {
                        throw Unsupported(
                            "C1M1 class equality endpoints require one retained TypeDef Module partition.");
                    }

                    var oldTypeDefRow = GetTypeDefRow(
                        endpoint.TypeDefDefinitionId);
                    if (!typeDefRowMap.TryGetValue(oldTypeDefRow,
                            out var typeDefRow)
                        || !typeDefRows.Add(typeDefRow))
                    {
                        throw new InvalidDataException(
                            "A class equality endpoint was not emitted as one distinct TypeDef view.");
                    }
                }

                if (typeDefRows.Count < 2)
                {
                    throw new InvalidDataException(
                        "A class equality group has fewer than two destination TypeDef views.");
                }

                using var typeDefEnumerator = typeDefRows.GetEnumerator();
                _ = typeDefEnumerator.MoveNext();
                var leftTypeDef = checked((uint)typeDefEnumerator.Current);
                while (typeDefEnumerator.MoveNext())
                {
                    typeDefBindingEqualityRows.Add(
                        new TrRmTypeDefBindingEqualityRow(1, leftTypeDef,
                            checked((uint)typeDefEnumerator.Current)));
                }
            }

            foreach (var declaration in program.StandardClassDeclarations)
            {
                foreach (var group
                    in declaration.TypeDefBindingEqualityGroups)
                {
                    AddTypeDefBindingEqualityGroup(group);
                }
            }
            foreach (var definition in program.GenericNominalTypeDefinitions)
            {
                foreach (var group in definition.TypeDefBindingEqualityGroups)
                {
                    AddTypeDefBindingEqualityGroup(group);
                }
            }

            typeDefBindingEqualityRows
                = [.. typeDefBindingEqualityRows.Distinct()
                    .OrderBy(static row => row.OwnerModule)
                    .ThenBy(static row => row.LeftTypeDef)
                    .ThenBy(static row => row.RightTypeDef)];
        }

        var methodDefRowMap = new Dictionary<int, int>();
        var nextMethodDefRow = 0;
        foreach (var oldMethodDefRow in methodEmissionPlans.Keys)
        {
            methodDefRowMap.Add(oldMethodDefRow, ++nextMethodDefRow);
        }
        foreach (var oldMethodDefRow in moduleFunctionEmissionPlans.Keys)
        {
            methodDefRowMap.Add(oldMethodDefRow, ++nextMethodDefRow);
        }
        foreach (var oldMethodDefRow in privateModuleFunctionEmissionPlans.Keys)
        {
            methodDefRowMap.Add(oldMethodDefRow, ++nextMethodDefRow);
        }

        var genericParamRowMap = new Dictionary<int, int>();
        var methodGenericParamRanges
            = new Dictionary<int, (int First, int Count)>();
        var genericParamRows = new List<TrRmGenericParamRow>();
        var genericParamNames = new List<string>();
        var genericConstraintRows = new List<TrRmConstraintRow>();
        var genericConstraintPlans
            = new List<C1M1GenericConstraintPlan>();
        var genericMethodTypeSpecPlans
            = new List<C1M1GenericMethodTypeSpecPlan>();
        var genericMethodDefaultTypeSpecPlans
            = new List<C1M1GenericMethodDefaultTypeSpecPlan>();
        var methodParameterLabelRows
            = new List<(int MethodDefRow, int Ordinal, string Name)>();
        foreach (var (oldMethodRow, plan) in methodEmissionPlans)
        {
            var formals = plan.Method.GenericParameters;
            if (formals.IsDefault
                || formals.Length != plan.Method.GenericParameterCount
                || formals.Length > 8)
            {
                throw Unsupported(
                    "C1M1 generic class MethodDefs require their exact ordered formal rows.");
            }

            var formalFirst = formals.IsEmpty
                ? 0 : genericParamRows.Count + 1;
            for (var ordinal = 0; ordinal < formals.Length; ordinal++)
            {
                var formal = formals[ordinal];
                if (formal is null || formal.Row <= 0
                    || formal.OwnerDefinitionId
                        != plan.Method.DefinitionId
                    || formal.Ordinal != (uint)ordinal
                    || formal.FormalKind != 1
                    || formal.ConstantTypeSpecReference != 0
                    || formal.Constraints.IsDefault
                    || string.IsNullOrEmpty(formal.Name)
                    || !genericParamRowMap.TryAdd(formal.Row,
                        genericParamRows.Count + 1))
                {
                    throw new InvalidDataException(
                        "A retained class MethodDef GenericParam row has an invalid owner, ordinal or type-formal shape.");
                }

                genericParamRows.Add(default);
                genericParamNames.Add(formal.Name);
            }

            methodGenericParamRanges.Add(oldMethodRow,
                (formalFirst, formals.Length));
        }

        var importRowMap = new Dictionary<int, int>();
        var nextImportRow = 0;
        foreach (var oldImportRow in bindingsByImportRow.Keys
            .Concat(fieldBindingsByImportRow.Keys)
            .Concat(methodBindingsByImportRow.Keys)
            .Concat(moduleFunctionImportRowsByImportRow.Keys).Order())
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
        var methodRows = new List<TrRmMethodDefRow>();
        var methodSignatureRows = new List<TrRmMethodSigRow>();
        var parameterRows = new List<TrRmParamRow>();
        var methodNames = new List<string>();
        var bodyRows = new List<ReadOnlyMemory<byte>>();
        var fieldPathRows = new List<TrRmFieldPathRow>();
        var fieldPathRowMap = new Dictionary<int, int>();
        var fieldPathRowsByIdentity
            = new Dictionary<C1M1FieldPathIdentity, int>();
        var parameterSurfaceRows = new List<C1M1ParameterSurfaceRow>();
        var contractRows = new List<TrRmContractRow>();
        var frozenScalarValueBuilder = new TrRmFrozenScalarValueBuilder();
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

        foreach (var (oldMethodRow, plan) in methodEmissionPlans)
        {
            var method = plan.Method;
            var ownerTypeDefRow
                = GetTypeDefRow(plan.Owner.Identity.DefinitionId);
            var newMethodDefRow = methodDefRowMap[oldMethodRow];
            var (genericFirst, genericCount)
                = methodGenericParamRanges[oldMethodRow];
            var hasBody = !plan.Body.IsEmpty;
            var constructionShapeIsSupported
                = method.ConstructionRole switch
                {
                    0 => method.InitializerMethodDefinitionId == 0
                        && method.ConstructionPropertyDefinitionId == 0
                        && method.InitializedMemberDefinitionIds
                            .IsDefaultOrEmpty,
                    1 => schema.UsesObjectConstruction()
                        && method.InitializerMethodDefinitionId == 0
                        && method.ConstructionPropertyDefinitionId == 0
                        && !method.InitializedMemberDefinitionIds.IsDefault,
                    2 => schema.UsesObjectConstruction()
                        && (method.InitializerMethodDefinitionId >> 24)
                            == 0x16u
                        && (method.InitializerMethodDefinitionId
                            & 0x00FF_FFFFu) != 0
                        && method.ConstructionPropertyDefinitionId == 0
                        && method.InitializedMemberDefinitionIds
                            .IsDefaultOrEmpty,
                    _ => false,
                };
            if ((hasBody
                    ? method.BodyRow == 0 || !plan.Imports.IsEmpty
                    : method.BodyRow != 0 || plan.Imports.IsEmpty
                        || !plan.ExportKeys.IsEmpty)
                || !constructionShapeIsSupported
                || (method.Flags & 3) == 3
                || (!plan.Imports.IsEmpty
                    && (method.Flags & 3) != 2)
                || ((method.Flags & 4) != 0
                    && (method.ReceiverTypeSpecReference == 0
                        || !schema.UsesMethodReceiverAccess()))
                || (method.EffectsMask & ~6u) != 0
                || !typeDefRowMap.TryGetValue(ownerTypeDefRow,
                    out var newOwnerTypeDefRow))
            {
                throw Unsupported(
                    "C1M1 source-free class re-emission supports bodyless public MethodDef Imports and bounded local type-formal MethodDefs with FieldDef-only Construction roles.");
            }

            if (method.ReceiverTypeSpecReference != 0)
            {
                var receiver = rootTables.TypeSpecs.GetTypeSpecRef(
                    method.ReceiverTypeSpecReference);
                var mutableReceiver = (receiver.Qualifiers & 1) != 0;
                var usesConstruction2ReceiverAccess
                    = schema.UsesObjectConstruction();
                if (!receiver.IsRow
                    || receiver.Form is not (0x02 or 0x03)
                    || (receiver.Qualifiers & ~0x01) != 0
                    || (!schema.UsesMethodReceiverAccess()
                        && !usesConstruction2ReceiverAccess
                        && mutableReceiver)
                    || (schema.UsesMethodReceiverAccess()
                        ? ((method.Flags & 4) != 0) != mutableReceiver
                        : (method.Flags & 4) != 0)
                    || rootTables.TypeSpecs.GetNominalTypeDefRow(
                        method.ReceiverTypeSpecReference,
                        rootTables.TypeDefs, rootTables.Imports)
                        != ownerTypeDefRow)
                {
                    throw Unsupported(
                        "A retained class MethodDef receiver must use the exact schema-authorized nominal class access.");
                }
            }

            for (var ordinal = 0; ordinal < genericCount; ordinal++)
            {
                var formal = method.GenericParameters[ordinal];
                var genericParamRow = genericFirst + ordinal;
                var constraintFirst = genericConstraintRows.Count + 1;
                var interfaceConstraintOrdinal = -1;
                foreach (var constraint in formal.Constraints)
                {
                    if (constraint.OwnerGenericParamRow != formal.Row)
                    {
                        throw new InvalidDataException(
                            "A retained class MethodDef Constraint has an invalid owner.");
                    }

                    if (schema.UsesAssociatedRelationConstraints())
                    {
                        if (constraint.Kind is < 1 or > 3)
                        {
                            throw new InvalidDataException(
                                "A retained class MethodDef Constraint has an unsupported kind.");
                        }

                        if (constraint.Kind == 1)
                        {
                            interfaceConstraintOrdinal++;
                        }

                        genericConstraintPlans.Add(
                            new C1M1GenericConstraintPlan(oldMethodRow,
                                ordinal, genericParamRow,
                                constraint.Kind, constraint.Payload,
                                constraint.Kind == 1
                                    ? interfaceConstraintOrdinal : -1));
                        continue;
                    }

                    var payload = constraint.Kind switch {
                        1 => RemapC1M1ClassTypeSpec(rootTables,
                            constraint.Payload, typeDefRowMap, importRowMap,
                            outputTypeSpecRows, typeSpecRowMap,
                            activeTypeSpecRows, depth: 0,
                            genericParamRowMap: genericParamRowMap),
                        2 when constraint.Payload is >= 1 and <= 3
                            => constraint.Payload,
                        3 => throw Unsupported(
                            "CM1 source-free generic class MethodDef re-emission does not yet retain associated-relation constraints."),
                        _ => throw new InvalidDataException(
                            "A retained class MethodDef Constraint has an unsupported kind or payload."),
                    };
                    genericConstraintRows.Add(new TrRmConstraintRow(
                        genericParamRow, constraint.Kind, payload));
                }

                var constraintCount = genericConstraintRows.Count
                        - constraintFirst + 1;
                if (constraintCount == 0)
                {
                    constraintFirst = 0;
                }

                var deferredDefaultType
                    = formal.DefaultArgumentReference != 0
                        && IsC1M1ImportedGenericNominalMethodTypeSpec(
                            rootTables, formal.DefaultArgumentReference,
                            nominalTypeImportRows,
                            allowOptionalClassHandle: true);
                var defaultArgumentReference
                    = formal.DefaultArgumentReference == 0 ? 0
                        : deferredDefaultType
                            ? AppendC1M1MethodTypePlaceholder(
                                outputTypeSpecRows, newOwnerTypeDefRow)
                            : RemapC1M1ClassTypeSpec(rootTables,
                                formal.DefaultArgumentReference,
                                typeDefRowMap, importRowMap,
                                outputTypeSpecRows, typeSpecRowMap,
                                activeTypeSpecRows, depth: 0,
                                genericParamRowMap: genericParamRowMap);
                if (deferredDefaultType)
                {
                    genericMethodDefaultTypeSpecPlans.Add(
                        new C1M1GenericMethodDefaultTypeSpecPlan(
                            oldMethodRow, genericParamRow, ordinal));
                }
                genericParamRows[genericParamRow - 1]
                    = new TrRmGenericParamRow(
                        0x1600_0000u | checked((uint)newMethodDefRow),
                        ordinal, 0, constraintFirst, constraintCount,
                        formal.FormalKind, formal.OwnershipDomain,
                        formal.NullabilityDomain, 0,
                        defaultArgumentReference);
            }

            var signatureRow = methodSignatureRows.Count + 1;
            var deferredResultType
                = IsC1M1ImportedGenericNominalMethodTypeSpec(rootTables,
                    method.ReturnTypeSpecReference, nominalTypeImportRows,
                    allowOptionalClassHandle: true);
            var resultTypeSpec = deferredResultType
                ? AppendC1M1MethodTypePlaceholder(outputTypeSpecRows,
                    newOwnerTypeDefRow)
                : RemapC1M1ClassTypeSpec(rootTables,
                    method.ReturnTypeSpecReference, typeDefRowMap,
                    importRowMap, outputTypeSpecRows, typeSpecRowMap,
                    activeTypeSpecRows, depth: 0,
                    genericParamRowMap: genericParamRowMap);
            if (deferredResultType)
            {
                genericMethodTypeSpecPlans.Add(
                    new C1M1GenericMethodTypeSpecPlan(oldMethodRow,
                        signatureRow, 0, -1, IsResult: true));
            }
            var receiverTypeSpec = method.ReceiverTypeSpecReference == 0
                ? 0
                : RemapC1M1ClassTypeSpec(rootTables,
                    method.ReceiverTypeSpecReference, typeDefRowMap,
                    importRowMap, outputTypeSpecRows, typeSpecRowMap,
                        activeTypeSpecRows, depth: 0,
                        genericParamRowMap: genericParamRowMap);
            var parameterFirst = method.Parameters.IsEmpty
                ? 0 : parameterRows.Count + 1;
            for (var ordinal = 0; ordinal < method.Parameters.Length;
                ordinal++)
            {
                var parameter = method.Parameters[ordinal];
                if (parameter.Mode != 0
                    || (parameter.Flags & ~1) != 0
                    || (parameter.Flags == 1
                        && !IsC1M1NoOpReadonlyScalarReferenceTypeSpec(
                            rootTables.TypeSpecs,
                            parameter.TypeSpecReference)))
                {
                    throw Unsupported(
                        "A retained class MethodDef parameter must use the bounded ordinary mode and supported source-call flags.");
                }

                var deferredParameterType
                    = IsC1M1ImportedGenericNominalMethodTypeSpec(
                        rootTables, parameter.TypeSpecReference,
                        nominalTypeImportRows,
                        allowOptionalClassHandle: true);
                var remappedTypeSpec = deferredParameterType
                    ? AppendC1M1MethodTypePlaceholder(outputTypeSpecRows,
                        newOwnerTypeDefRow)
                    : RemapC1M1ClassTypeSpec(rootTables,
                        parameter.TypeSpecReference, typeDefRowMap,
                        importRowMap, outputTypeSpecRows, typeSpecRowMap,
                        activeTypeSpecRows, depth: 0,
                        genericParamRowMap: genericParamRowMap);
                parameterRows.Add(new TrRmParamRow(signatureRow, ordinal,
                    remappedTypeSpec, parameter.Mode, parameter.Flags));
                if (!schema.UsesClassParameterDefaults()
                    && schema.UsesMethodParameterLabels())
                {
                    methodParameterLabelRows.Add((
                        methodDefRowMap[oldMethodRow], ordinal,
                        parameter.Name));
                }

                if (deferredParameterType)
                {
                    genericMethodTypeSpecPlans.Add(
                        new C1M1GenericMethodTypeSpecPlan(oldMethodRow,
                            signatureRow, parameterRows.Count, ordinal,
                            IsResult: false));
                }

                if (schema.UsesClassParameterDefaults())
                {
                    uint defaultConstantSourceRow = 0;
                    if (parameter.DefaultValue is BoundConstant value)
                    {
                        if (!TryGetC1M1ClassScalarType(rootTables,
                                parameter.TypeSpecReference,
                                out var parameterType)
                            || parameterType is (PrimitiveType.NativeInt
                                or PrimitiveType.NativeUInt)
                            || value.Type != parameterType
                            || value.RepresentationType != parameterType
                            || parameterType is PrimitiveType.NativeInt
                                or PrimitiveType.NativeUInt
                            || !value.Bits.IsCanonicalFor(parameterType))
                        {
                            throw Unsupported(
                                "A retained CM1 class parameter default must be an exact fixed-width scalar value.");
                        }

                        var frozenValue
                            = frozenScalarValueBuilder.Add(value);
                        if (frozenValue.TypeSpecRef != remappedTypeSpec)
                        {
                            throw Unsupported(
                                "A retained CM1 class parameter default changed its exact parameter TypeSpec.");
                        }

                        defaultConstantSourceRow
                            = frozenValue.ConstantSourceRow;
                    }

                    parameterSurfaceRows.Add(
                        new C1M1ParameterSurfaceRow(
                            methodDefRowMap[oldMethodRow], ordinal,
                            parameter.Name, defaultConstantSourceRow));
                }
            }

            methodSignatureRows.Add(new TrRmMethodSigRow(
                resultTypeSpec, receiverTypeSpec, parameterFirst,
                method.Parameters.Length, genericFirst, genericCount));

            var contractFirst = 0;
            var methodContractCount = 0;
            var methodDependencyContractRowMap
                = new Dictionary<int, int>();
            if (method.EffectsMask != 0)
            {
                contractFirst = contractRows.Count + 1;
                contractRows.Add(new TrRmContractRow(
                    0x1600_0000u | checked((uint)newMethodDefRow),
                    1, s_effectPayloads[method.EffectsMask]));
                methodContractCount++;
            }

            if (method.ConstructionRole is 1 or 2)
            {
                byte[] constructionPayload;
                if (method.ConstructionRole == 1)
                {
                    var initializedMemberIds
                        = new uint[method.InitializedMemberDefinitionIds
                            .Length];
                    var seenMembers = new HashSet<uint>();
                    for (var memberIndex = 0;
                        memberIndex < initializedMemberIds.Length;
                        memberIndex++)
                    {
                        var memberDefinitionId
                            = method.InitializedMemberDefinitionIds[
                                memberIndex];
                        if ((memberDefinitionId >> 24) != 0x14
                            || !seenMembers.Add(memberDefinitionId))
                        {
                            throw Unsupported(
                                "The bounded class-reference re-emitter supports Construction guarantees only for unique FieldDef members.");
                        }

                        var oldFieldRow = GetFieldDefRow(
                            memberDefinitionId);
                        if (rootTables.Fields.GetOwnerTypeDef(oldFieldRow)
                                != ownerTypeDefRow
                            || (rootTables.Fields.GetFlags(oldFieldRow)
                                & 0x08) != 0
                            || !fieldRowMap.TryGetValue(oldFieldRow,
                                out var newFieldRow))
                        {
                            throw new InvalidDataException(
                                "A retained Construction guarantee does not resolve to an emitted instance FieldDef.");
                        }

                        initializedMemberIds[memberIndex]
                            = 0x1400_0000u
                                | checked((uint)newFieldRow);
                    }

                    constructionPayload = new byte[checked(
                        8 + (initializedMemberIds.Length * sizeof(uint)))];
                    constructionPayload[0] = 1;
                    BinaryPrimitives.WriteUInt32LittleEndian(
                        constructionPayload.AsSpan(4),
                        checked((uint)initializedMemberIds.Length));
                    for (var memberIndex = 0;
                        memberIndex < initializedMemberIds.Length;
                        memberIndex++)
                    {
                        BinaryPrimitives.WriteUInt32LittleEndian(
                            constructionPayload.AsSpan(
                                8 + (memberIndex * sizeof(uint))),
                            initializedMemberIds[memberIndex]);
                    }
                }
                else
                {
                    var oldInitializerMethodRow = GetMethodDefRow(
                        method.InitializerMethodDefinitionId);
                    if (!methodEmissionPlans.TryGetValue(
                            oldInitializerMethodRow,
                            out var initializerPlan)
                        || initializerPlan.Method.ConstructionRole != 1
                        || initializerPlan.Owner.Identity
                            != plan.Owner.Identity
                        || rootTables.Methods.GetOwnerType(
                            oldInitializerMethodRow) != ownerTypeDefRow
                        || rootTables.Methods.GetOwnerModule(
                            oldInitializerMethodRow) != rootModuleRow
                        || !methodDefRowMap.TryGetValue(
                            oldInitializerMethodRow,
                            out var newInitializerMethodRow))
                    {
                        throw new InvalidDataException(
                            "A retained Construction projection does not reference its exact owner initializer MethodDef.");
                    }

                    constructionPayload
                        = EncodeC1M1Construction2ProjectionContract(
                            newInitializerMethodRow);
                }

                if (contractFirst == 0)
                {
                    contractFirst = contractRows.Count + 1;
                }

                contractRows.Add(new TrRmContractRow(
                    0x1600_0000u | checked((uint)newMethodDefRow),
                    0x0003, constructionPayload));
                methodContractCount++;
            }

            var dependencyContractRows
                = rootTables.Contracts.GetDependencyContractRows(
                    oldMethodRow);
            var dependencyClauseView
                = CreateC1M1ClassMethodDependencyClauseView(
                    dependencyContractRows
                        .Select(static row => row.Contract)
                        .ToImmutableArray());
            if (!dependencyClauseView.AsSpan().SequenceEqual(
                    method.DependencyClauses.AsSpan()))
            {
                throw new InvalidDataException(
                    "A retained class MethodDef Dependency view differs from its Contract rows.");
            }

            foreach (var (oldContractRow, dependency)
                in dependencyContractRows)
            {
                if (contractFirst == 0)
                {
                    contractFirst = contractRows.Count + 1;
                }

                var payload = new byte[12];
                payload[0] = dependency.DestinationKind;
                payload[1] = dependency.SourceKind;
                payload[2] = dependency.Relationship;
                payload[3] = dependency.Phase;
                BinaryPrimitives.WriteUInt32LittleEndian(
                    payload.AsSpan(4), dependency.DestinationOrdinal);
                BinaryPrimitives.WriteUInt32LittleEndian(
                    payload.AsSpan(8), dependency.SourceOrdinal);
                contractRows.Add(new TrRmContractRow(
                    0x1600_0000u | checked((uint)newMethodDefRow),
                    4, payload));
                methodDependencyContractRowMap.Add(oldContractRow,
                    contractRows.Count);
                methodContractCount++;
            }

            var outputBodyRow = hasBody ? bodyRows.Count + 1 : 0;
            if (hasBody)
            {
                bodyRows.Add(RemapC1M1ClassMethodBody(rootTables,
                    plan.Body, typeDefRowMap, methodDefRowMap,
                    fieldRowMap, importRowMap, fieldPathRowMap,
                    fieldPathRows, fieldPathRowsByIdentity,
                    methodDependencyContractRowMap,
                    frozenScalarValueBuilder, outputTypeSpecRows,
                    typeSpecRowMap, activeTypeSpecRows, schema,
                    genericParamRowMap));
            }

            methodRows.Add(new TrRmMethodDefRow(newOwnerTypeDefRow, 0,
                signatureRow, method.Flags, contractFirst,
                methodContractCount, outputBodyRow));
            methodNames.Add(method.Name);
            foreach (var exportKey in plan.ExportKeys)
            {
                if (!exportKeys.Add(exportKey))
                {
                    throw new InvalidDataException(
                        "C1M1 class MethodDef ExportKeys must be unique within the Module.");
                }

                exportRows.Add((
                    0x1600_0000u
                        | checked((uint)methodDefRowMap[oldMethodRow]),
                    exportKey));
            }
        }

        var outputClassTypeDefRowsByIdentity
            = new Dictionary<BoundGenericNominalTypeIdentity, int>();
        foreach (var (oldTypeDefRow, view) in typeViewsByOldRow)
        {
            if (!typeDefRowMap.TryGetValue(oldTypeDefRow,
                    out var outputTypeDefRow)
                || !outputClassTypeDefRowsByIdentity.TryAdd(view.Identity,
                    outputTypeDefRow))
            {
                throw new InvalidDataException(
                    "A retained class TypeDef has no unique output row.");
            }
        }

        var sourceFunctionClassTypeSpecRows
            = new Dictionary<(BoundGenericNominalTypeIdentity Identity,
                byte Qualifiers), uint>();
        uint SourceFunctionTypeSpecReference(PrimitiveType type)
        {
            if (type == PrimitiveType.Void)
            {
                return 0xF200_0000u;
            }

            if (TryGetC1M1SourceScalarTypeSpecReference(type,
                    out var scalarTypeSpecReference))
            {
                return scalarTypeSpecReference;
            }

            if (!BoundGenericNominalTypeTypes.IsType(type))
            {
                throw Unsupported(
                    "A local class-reference function parameter must use a supported scalar or direct class TypeSpec.");
            }

            var instanceIndex = BoundGenericNominalTypeTypes.Index(type);
            if ((uint)instanceIndex
                    >= (uint)program.GenericNominalTypeInstances.Length)
            {
                throw new InvalidDataException(
                    "A local class-reference function has an invalid nominal type instance.");
            }

            var instance = program.GenericNominalTypeInstances[
                instanceIndex];
            if (!instance.Arguments.IsEmpty
                || (instance.Qualifiers & ~0x01) != 0
                || !outputClassTypeDefRowsByIdentity.TryGetValue(
                    instance.Definition, out var outputTypeDefRow))
            {
                throw Unsupported(
                    "A local class-reference function parameter must use a selected nongeneric class TypeDef.");
            }

            if (instance.TypeSpecReference != 0)
            {
                if (!StringComparer.Ordinal.Equals(
                        instance.TypeSpecProviderIdentity,
                        rootSnapshot.ModuleIdentity)
                    || instance.TypeSpecForm != 0x02
                    || instance.TypeSpecReference
                        > checked((uint)rootTables.TypeSpecs.Count))
                {
                    throw Unsupported(
                        "A local class-reference function TypeSpec must retain its exact root Module view.");
                }

                var typeSpec = rootTables.TypeSpecs.GetTypeSpecRef(
                    instance.TypeSpecReference);
                var nominalTypeDefRow
                    = rootTables.TypeSpecs.GetNominalTypeDefRow(
                        instance.TypeSpecReference, rootTables.TypeDefs,
                        rootTables.Imports);
                if (!typeSpec.IsRow
                    || typeSpec.Form != 0x02
                    || typeSpec.Qualifiers != instance.Qualifiers
                    || !StringComparer.Ordinal.Equals(
                        instance.Definition.ModuleIdentity,
                        rootSnapshot.ModuleIdentity)
                    || !StringComparer.Ordinal.Equals(
                        instance.Definition.ProviderIdentity,
                        rootSnapshot.ModuleIdentity)
                    || rootTables.TypeDefs.GetKind(nominalTypeDefRow) != 2
                    || nominalTypeDefRow
                        != GetTypeDefRow(instance.Definition.DefinitionId))
                {
                    throw new InvalidDataException(
                        "A local class-reference function TypeSpec changed its checked nominal identity.");
                }

                return RemapC1M1ClassTypeSpec(rootTables,
                    instance.TypeSpecReference, typeDefRowMap,
                    importRowMap, outputTypeSpecRows, typeSpecRowMap,
                    activeTypeSpecRows, depth: 0,
                    genericParamRowMap: genericParamRowMap);
            }

            if (instance.TypeSpecProviderIdentity is not null
                || instance.TypeSpecForm != 0)
            {
                throw new InvalidDataException(
                    "A local class-reference function has an incomplete bare nominal TypeSpec.");
            }

            var key = (instance.Definition, instance.Qualifiers);
            if (!sourceFunctionClassTypeSpecRows.TryGetValue(key,
                    out var typeSpecRow))
            {
                if (outputTypeSpecRows.Count >= 0xFF_FFFF)
                {
                    throw Unsupported(
                        "Local class-reference function TypeSpecs exceed the metadata row limit.");
                }

                typeSpecRow = checked((uint)outputTypeSpecRows.Count + 1);
                outputTypeSpecRows.Add(TrRmTypeSpecTableWriter.Reference(
                    0x02, checked((uint)outputTypeDefRow),
                    instance.Qualifiers));
                sourceFunctionClassTypeSpecRows.Add(key, typeSpecRow);
            }

            return typeSpecRow;
        }

        bool ModuleFunctionTypeMatches(uint typeSpecReference,
            PrimitiveType type)
        {
            var typeSpec = rootTables.TypeSpecs.GetTypeSpecRef(
                typeSpecReference);
            if (!typeSpec.IsRow && typeSpec.Form == 1
                && typeSpec.Qualifiers == 0
                && typeSpec.TypeCode == 0)
            {
                return type == PrimitiveType.Void;
            }

            if (TryGetC1M1ClassScalarType(rootTables,
                    typeSpecReference, out var scalarType))
            {
                return type == scalarType;
            }

            if (typeSpec.IsRow && typeSpec.Form == 0x07
                && typeSpec.Qualifiers == 0
                && CheckedReferenceTypes.IsType(type))
            {
                var referenceIndex = CheckedReferenceTypes.Index(type);
                if ((uint)referenceIndex
                        >= (uint)program.CheckedReferenceTypes.Length)
                {
                    return false;
                }

                var referenceType = program.CheckedReferenceTypes[
                    referenceIndex];
                var typeSpecRow = rootTables.TypeSpecs.GetRow(
                    typeSpec.Row).Span;
                var referentTypeSpecReference
                    = BinaryPrimitives.ReadUInt32LittleEndian(
                        typeSpecRow.Slice(2));
                return !referenceType.IsWritable
                    && !referenceType.IsGarbageCollected
                    && !referenceType.IsInitialization
                    && !referenceType.IsOptional
                    && !referenceType.IsOptionalValue
                    && !referenceType.IsReferentWritable
                    && TryGetC1M1ClassScalarType(rootTables,
                        referentTypeSpecReference, out var referentType)
                    && referenceType.ReferentType == referentType;
            }

            if (!typeSpec.IsRow || typeSpec.Form is not (0x02 or 0x03)
                || !BoundGenericNominalTypeTypes.IsType(type))
            {
                return false;
            }

            var instanceIndex = BoundGenericNominalTypeTypes.Index(type);
            if ((uint)instanceIndex
                    >= (uint)program.GenericNominalTypeInstances.Length)
            {
                return false;
            }

            var instance = program.GenericNominalTypeInstances[
                instanceIndex];
            var typeDefRow = rootTables.TypeSpecs.GetNominalTypeDefRow(
                typeSpecReference, rootTables.TypeDefs, rootTables.Imports);
            if (typeDefRow <= 0
                || rootTables.TypeDefs.GetKind(typeDefRow) != 2
                || !instance.Arguments.IsEmpty
                || instance.Qualifiers != typeSpec.Qualifiers
                || (instance.Qualifiers & ~0x01) != 0
                || !StringComparer.Ordinal.Equals(
                    instance.TypeSpecProviderIdentity,
                    rootSnapshot.ModuleIdentity)
                || instance.TypeSpecReference != typeSpecReference
                || instance.TypeSpecForm != typeSpec.Form
                || !outputClassTypeDefRowsByIdentity.ContainsKey(
                    instance.Definition))
            {
                return false;
            }

            var expectedDefinitionId = 0x1300_0000u
                | checked((uint)typeDefRow);
            if (typeSpec.Form == 0x02)
            {
                return instance.Definition
                    == new BoundGenericNominalTypeIdentity(
                        rootSnapshot.ModuleIdentity,
                        rootSnapshot.ModuleIdentity,
                        expectedDefinitionId);
            }

            var typeBindings = classProviderValidation.TypeBindings
                .Where(binding => binding.ConsumerArtifactIndex == 0
                    && binding.ConsumerModuleRow == rootModuleRow
                    && binding.ImportRow == typeSpec.Row)
                .ToArray();
            return typeBindings.Length == 1
                && typeBindings[0].ExpectedTypeDefRow == typeDefRow
                && typeBindings[0].ActualDefinitionId
                    == instance.Definition.DefinitionId
                && StringComparer.Ordinal.Equals(
                    typeBindings[0].ProducerIdentity,
                    instance.Definition.ModuleIdentity)
                && StringComparer.Ordinal.Equals(
                    typeBindings[0].ProducerIdentity,
                    instance.Definition.ProviderIdentity);
        }

        for (var functionIndex = 0;
            functionIndex < program.Functions.Length; functionIndex++)
        {
            var function = program.Functions[functionIndex];
            if (moduleFunctionIndexes.Contains(functionIndex))
            {
                continue;
            }

            if (function.ModuleImport is null && !function.IsExternal
                && function.DeclaringStruct is null)
            {
                var directCall = function.Body.Statements switch
                {
                    [BoundReturn { Value: BoundCall returnedCall }]
                        => returnedCall,
                    [
                        BoundExpressionStatement {
                            Expression: BoundCall statementCall,
                        },
                        BoundReturn { Value: null },
                    ] when function.ReturnType == PrimitiveType.Void
                        && statementCall.Type == PrimitiveType.Void
                        => statementCall,
                    _ => null,
                };
                var directConstant
                    = function.Body.Statements is [
                        BoundReturn {
                            Value: BoundConstant returnedConstant,
                        },
                    ] ? returnedConstant : null;
                var unitReturnHasNoWork
                    = function.Body.Statements.IsEmpty
                        || function.Body.Statements is [
                            BoundReturn { Value: null },
                        ];
                var isNoOpUnitFunction
                    = function.ReturnType == PrimitiveType.Void
                        && unitReturnHasNoWork;
                if (function.IsStatic || function.IsConstructor
                    || function.IsClassConstructor
                    || function.ClassConstructor != -1
                    || function.IsOperator
                    || function.Intrinsic != BoundIntrinsic.None
                    || function.PropertyName is not null
                    || function.IsCleanup
                    || function.NoFailureProof
                    || function.HasPureEvaluationForm
                    || function.DoesNotReturn
                    || function.HasNativeExport
                    || function.NativeExportEntryName is not null
                    || function.NativeCallableEntryName is not null
                    || function.DirectNativeImport is not null
                    || function.NativeOutputRole is not null
                    || !function.NativeOutputContracts.IsDefaultOrEmpty
                    || function.NativeVTableMethod is not null
                    || function.NativeFunctionPointerType is not null
                    || function.NativeCallableConvention is not null
                    || function.IndexedBody is not null
                    || function.OrdinaryParameterDefaults.IsDefault
                    || (!schema.UsesClassParameterDefaults()
                        && !function.OrdinaryParameterDefaults.IsEmpty)
                    || !function.DeclarationContractClauses.IsDefaultOrEmpty
                    || !function.FriendTargets.IsDefaultOrEmpty
                    || function.ParameterCount
                        != function.Variables.Length
                    || function.ParameterCount
                        > SemanticVerifier.MaximumArguments
                    || (!isNoOpUnitFunction && directCall is null
                        && directConstant is null)
                    || (directCall is not null
                        && directCall.Type != function.ReturnType)
                    || (directConstant is not null
                        && (directConstant.Type != function.ReturnType
                            || directConstant.RepresentationType
                                != function.ReturnType
                            || !directConstant.Bits.IsCanonicalFor(
                                function.ReturnType)
                            || function.ReturnType is PrimitiveType.NativeInt
                                or PrimitiveType.NativeUInt))
                    || !function.Body.RetiredManagedLocals.IsEmpty
                    || (function.IsPrivate && function.IsInternal))
                {
                    throw Unsupported(
                        "A local class-reference function must be an ordinary unit no-op, fixed-scalar constant return, or direct class-method call return.");
                }

                var signatureRow = methodSignatureRows.Count + 1;
                var parameterFirst = function.ParameterCount == 0
                    ? 0 : parameterRows.Count + 1;
                for (var ordinal = 0;
                    ordinal < function.ParameterCount; ordinal++)
                {
                    var parameter = function.Variables[ordinal];
                    if (parameter.IsMutable || parameter.IsOut
                        || parameter.IsInit || parameter.IsImplicitRef
                        || parameter.IsInitializationLocal)
                    {
                        throw Unsupported(
                            "A local class-reference function parameter must be an immutable value formal.");
                    }

                    var typeSpecReference
                        = SourceFunctionTypeSpecReference(parameter.Type);
                    parameterRows.Add(new TrRmParamRow(signatureRow,
                        ordinal, typeSpecReference, 0, 0));
                    if (schema.UsesClassParameterDefaults())
                    {
                        uint defaultConstantSourceRow = 0;
                        var parameterDefaults
                            = function.OrdinaryParameterDefaults
                                .Where(defaultValue =>
                                    defaultValue.ParameterOrdinal == ordinal)
                                .ToArray();
                        if (parameterDefaults.Length > 1)
                        {
                            throw new InvalidDataException(
                                "A local CM1 module function has duplicate parameter defaults.");
                        }

                        if (parameterDefaults.Length == 1)
                        {
                            var parameterDefault = parameterDefaults[0];
                            if (parameterDefault.IsPending
                                || parameterDefault.Value
                                    is not BoundConstant constant
                                || parameterDefault.Expression is not null
                                || constant.Type != parameter.Type
                                || constant.RepresentationType
                                    != parameter.Type
                                || parameter.Type is PrimitiveType.NativeInt
                                    or PrimitiveType.NativeUInt
                                || !constant.Bits.IsCanonicalFor(
                                    parameter.Type)
                                || !TryGetC1M1SourceScalarTypeSpecReference(
                                    parameter.Type, out _))
                            {
                                throw Unsupported(
                                    "A local CM1 module function default must be an exact fixed-scalar constant.");
                            }

                            var frozenValue
                                = frozenScalarValueBuilder.Add(constant);
                            if (frozenValue.TypeSpecRef
                                != typeSpecReference)
                            {
                                throw Unsupported(
                                    "A local CM1 module function default changed its exact parameter TypeSpec.");
                            }

                            defaultConstantSourceRow
                                = frozenValue.ConstantSourceRow;
                        }

                        parameterSurfaceRows.Add(
                            new C1M1ParameterSurfaceRow(
                                methodRows.Count + 1, ordinal,
                                parameter.Name,
                                defaultConstantSourceRow));
                    }
                    else if (schema.UsesMethodParameterLabels())
                    {
                        methodParameterLabelRows.Add((
                            methodRows.Count + 1, ordinal,
                            parameter.Name));
                    }
                }

                var resultTypeSpecReference
                    = function.ReturnType == PrimitiveType.Void
                        ? 0xF200_0000u
                        : SourceFunctionTypeSpecReference(
                            function.ReturnType);
                if (directCall is not null
                    && (function.ReturnType != PrimitiveType.Void
                        && !TryGetC1M1SourceScalarTypeSpecReference(
                            function.ReturnType, out _)))
                {
                    throw Unsupported(
                        "A local class-reference call return must be unit or a fixed scalar value.");
                }

                methodSignatureRows.Add(new TrRmMethodSigRow(
                    resultTypeSpecReference, 0, parameterFirst,
                    function.ParameterCount, 0, 0));
                if (methodRows.Count >= 0xFF_FFFF
                    || bodyRows.Count >= 0xFF_FFFF)
                {
                    throw Unsupported(
                        "Local class-reference functions exceed the MethodDef or Body row limit.");
                }

                var newMethodDefRow = methodRows.Count + 1;
                var bodyRow = bodyRows.Count + 1;
                if (directConstant is not null)
                {
                    if (!schema.UsesObjectConstruction()
                        || !TryGetC1M1SourceScalarTypeSpecReference(
                            function.ReturnType, out _))
                    {
                        throw Unsupported(
                            "A local class-reference constant return requires an exact Construction-2 fixed-scalar result.");
                    }

                    bodyRows.Add(
                        EncodeC1M1MetadataClassConstantReturnBody(
                            directConstant,
                            SourceFunctionTypeSpecReference,
                            frozenScalarValueBuilder, schema));
                }
                else if (directCall is null)
                {
                    bodyRows.Add(
                        EncodeC1M1MetadataClassNoOpUnitBody(schema));
                }
                else
                {
                    if (!schema.UsesObjectConstruction()
                        || (uint)directCall.Function
                            >= (uint)program.Functions.Length)
                    {
                        throw Unsupported(
                            "Ordinary class MethodDef calls require the exact Construction-2 schema and a selected method view.");
                    }

                    var targetFunction
                        = program.Functions[directCall.Function];
                    C1M1ModuleFunctionEmissionPlan[] targetImportPlans
                        = targetFunction.ModuleImport
                                is BoundModuleImport targetImport
                            && targetFunction.IsExternal
                            && targetFunction.IsModuleReference
                            ? moduleFunctionEmissionPlans.Values.Where(plan =>
                                plan.Function is null
                                && plan.ImportRows.Any(importRow => {
                                    var binding = rootTables.Imports
                                        .GetBindingForImportRow(importRow);
                                    return StringComparer.Ordinal.Equals(
                                            binding.ProducerIdentity,
                                            targetImport.ProducerIdentity)
                                        && StringComparer.Ordinal.Equals(
                                            binding.ExportKey,
                                            targetImport.ExportKey)
                                        && binding.TargetRequirement
                                            == targetImport.TargetRequirement
                                        && binding.RuntimePolicy
                                            == targetImport.RuntimePolicy;
                                })).ToArray()
                            : [];
                    if (targetImportPlans.Length > 1)
                    {
                        throw new InvalidDataException(
                            "A forwarded module function call resolves to multiple retained Import expectations.");
                    }

                    if (targetImportPlans.Length == 1)
                    {
                        var targetPlan = targetImportPlans[0];
                        var oldTargetMethodRow = targetPlan.MethodDefRow;
                        var targetMethodRow = methodDefRowMap[
                            oldTargetMethodRow];
                        var targetSignatureRow
                            = rootTables.Methods.GetSignature(
                                oldTargetMethodRow);
                        var (targetParameterStart,
                            targetParameterCount)
                            = rootTables.Signatures.GetParamRange(
                                targetSignatureRow);
                        var targetResultTypeSpecReference
                            = rootTables.Signatures.GetResultTypeSpec(
                                targetSignatureRow);
                        var targetEffectsMask
                            = rootTables.Contracts.GetEffectsMask(
                                oldTargetMethodRow);
                        var targetDependencyContracts
                            = DecodeC1M1ClassMethodDependencyContracts(
                                rootTables.Contracts.GetDependencyContracts(
                                    oldTargetMethodRow),
                                targetParameterCount, hasReceiver: false);
                        var targetResultMatches
                            = targetFunction.ReturnType
                                == PrimitiveType.Void
                                ? IsUnitTypeSpec(rootTables.TypeSpecs,
                                    targetResultTypeSpecReference)
                                : TryGetC1M1ClassScalarType(rootTables,
                                    targetResultTypeSpecReference,
                                    out var targetResultType)
                                    && targetResultType
                                        == targetFunction.ReturnType;
                        if (targetFunction.ReturnType
                                != function.ReturnType
                            || targetFunction.ParameterCount
                                != targetParameterCount
                            || targetFunction.Variables.Length
                                != targetParameterCount
                            || !targetFunction.IsModuleReference
                            || !targetFunction.IsExternal
                            || targetFunction.DoesNotReturn
                            || targetFunction.RequiresUnsafeCaller
                                != ((targetEffectsMask & 4) != 0)
                            || targetFunction.DoesNotReturn
                                != ((targetEffectsMask & 2) != 0)
                            || !C1M1DependencyContractsEqual(
                                targetFunction.DependencyContracts,
                                targetDependencyContracts)
                            || targetFunction.DirectNativeImport is not null
                            || targetFunction.NativeVTableMethod is not null
                            || targetFunction.NativeFunctionPointerType
                                is not null
                            || targetFunction.NativeCallableEntryName
                                is not null
                            || targetFunction.NativeCallableConvention
                                is not null
                            || targetFunction.HasNativeExport
                            || !directCall.DefaultedArguments.IsDefaultOrEmpty
                            || directCall.Type != function.ReturnType
                            || !targetResultMatches
                            || (targetFunction.RequiresUnsafeCaller
                                && !directCall.IsUnsafeAuthorized)
                            || (targetEffectsMask & ~6u) != 0
                            || directCall.InterfaceCallIdentity is not null
                            || directCall.GenericMethodCall is not null
                            || directCall.ImportedGenericMethodCall is not null
                            || rootTables.Methods.GetOwnerModule(
                                oldTargetMethodRow) != rootModuleRow
                            || rootTables.Methods.GetOwnerType(
                                oldTargetMethodRow) != 0
                            || rootTables.Methods.GetBodyRow(
                                oldTargetMethodRow) != 0
                            || (rootTables.Methods.GetFlags(
                                oldTargetMethodRow) & 7) != 2
                            || rootTables.Signatures
                                .GetReceiverTypeSpec(targetSignatureRow) != 0
                            || rootTables.Signatures.GetGenericCount(
                                targetSignatureRow) != 0
                            || rootTables.Contracts.HasConstructionClause(
                                oldTargetMethodRow)
                            || !rootTables.Contracts
                                .GetOutputStateContracts(oldTargetMethodRow)
                                .IsEmpty
                            || !rootTables.Contracts
                                .GetNativeParameters(oldTargetMethodRow)
                                .IsEmpty
                            || !rootTables.Contracts
                                .GetNativeOutputs(oldTargetMethodRow)
                                .IsEmpty
                            || rootTables.NativeImports
                                .HasImportForMethod(oldTargetMethodRow)
                            || rootTables.NativeCallables
                                .HasCallableEntryForMethod(
                                    oldTargetMethodRow)
                            || rootTables.NativeVTables
                                .HasVTableForMethod(oldTargetMethodRow)
                            || rootTables.StandardNativeImports
                                .HasImportForMethod(oldTargetMethodRow))
                        {
                            throw Unsupported(
                                "A local class-reference function can call only a forwarded nongeneric top-level MethodDef with the bounded ordinary value signature and effects.");
                        }

                        bodyRows.Add(
                            EncodeC1M1MetadataClassDirectModuleCallBody(
                                function, directCall, targetFunction,
                                rootTables, oldTargetMethodRow,
                                targetMethodRow, program,
                                frozenScalarValueBuilder,
                                SourceFunctionTypeSpecReference,
                                ModuleFunctionTypeMatches, schema));
                    }
                    else
                    {
                        var targetPlans = methodEmissionPlans.Values.Where(plan =>
                            StringComparer.Ordinal.Equals(
                                targetFunction.Name,
                                (string.IsNullOrEmpty(plan.Owner.Namespace)
                                    ? plan.Owner.Name
                                    : plan.Owner.Namespace + "."
                                        + plan.Owner.Name)
                                + "." + plan.Method.Name))
                            .ToArray();
                        if (targetPlans.Length != 1)
                        {
                            throw Unsupported(
                                "An ordinary class call requires exactly one retained selected MethodDef view.");
                        }

                        var targetPlan = targetPlans[0];
                        var oldTargetMethodRow = GetMethodDefRow(
                            targetPlan.Method.DefinitionId);
                        if (!methodDefRowMap.TryGetValue(oldTargetMethodRow,
                                out var targetMethodDefRow))
                        {
                            throw new InvalidDataException(
                                "A selected ordinary class MethodDef has no output row.");
                        }

                        var receiverTypeSpec
                            = targetPlan.Method.ReceiverTypeSpecReference;
                        if (receiverTypeSpec == 0
                            || (targetPlan.Method.Flags & 4) != 0)
                        {
                            throw Unsupported(
                                "This ordinary class-call Body requires an ordinary receiver in the exact Construction-2 schema.");
                        }

                        var receiverType
                            = rootTables.TypeSpecs.GetTypeSpecRef(
                                receiverTypeSpec);
                        if (!receiverType.IsRow
                            || receiverType.Form is not (0x02 or 0x03)
                            || (receiverType.Qualifiers & ~0x01) != 0)
                        {
                            throw Unsupported(
                                "This ordinary class-call Body requires a direct normal class receiver TypeSpec.");
                        }

                        var targetReturnTypeSpec
                            = rootTables.TypeSpecs.GetTypeSpecRef(
                                targetPlan.Method.ReturnTypeSpecReference);
                        var targetReturnMatches
                            = function.ReturnType == PrimitiveType.Void
                                ? !targetReturnTypeSpec.IsRow
                                    && targetReturnTypeSpec.Form == 1
                                    && targetReturnTypeSpec.Qualifiers == 0
                                    && targetReturnTypeSpec.TypeCode == 0
                                : TryGetC1M1ClassScalarType(rootTables,
                                    targetPlan.Method.ReturnTypeSpecReference,
                                    out var targetReturnType)
                                    && targetReturnType == function.ReturnType;
                        if (!targetReturnMatches)
                        {
                            throw Unsupported(
                                "This ordinary class-call Body requires a matching unit or fixed-scalar MethodDef result.");
                        }

                        bodyRows.Add(
                            EncodeC1M1MetadataClassDirectCallBody(
                                function, directCall, targetFunction,
                                targetPlan, targetMethodDefRow,
                                (receiverType.Qualifiers & 1) != 0,
                                program, frozenScalarValueBuilder,
                                SourceFunctionTypeSpecReference, schema));
                    }
                }

                var methodContractFirst = 0;
                var methodContractCount = 0;
                var methodEffectsMask
                    = function.RequiresUnsafeCaller ? 4u : 0u;
                if (methodEffectsMask != 0)
                {
                    methodContractFirst = contractRows.Count + 1;
                    contractRows.Add(new TrRmContractRow(
                        0x1600_0000u
                            | checked((uint)newMethodDefRow),
                        1, s_effectPayloads[methodEffectsMask]));
                    methodContractCount++;
                }

                foreach (var dependency in function.DependencyContracts)
                {
                    if (dependency.Destination
                            != BoundDependencyDestination.Result
                        || dependency.DestinationOrdinal is not null
                        || dependency.Sources.IsDefaultOrEmpty)
                    {
                        throw Unsupported(
                            "A forwarded module-call function can retain only normal-result dependencies on formal values.");
                    }

                    var relationship = dependency.Relation switch {
                        BoundDependencyRelation.BorrowSourceStorage => 1,
                        BoundDependencyRelation.ForwardsBacking => 2,
                        BoundDependencyRelation.DependsOn => 3,
                        _ => throw Unsupported(
                            "A forwarded module-call function has an unsupported result dependency relationship."),
                    };
                    foreach (var source in dependency.Sources
                        .OrderBy(static source => source.Kind)
                        .ThenBy(static source => source.FormalOrdinal ?? -1))
                    {
                        if (source.Kind
                                != BoundDependencySourceKind.Formal
                            || source.FormalOrdinal is not int sourceOrdinal
                            || (uint)sourceOrdinal
                                >= (uint)function.ParameterCount)
                        {
                            throw Unsupported(
                                "A forwarded module-call dependency must name a caller formal.");
                        }

                        if (methodContractFirst == 0)
                        {
                            methodContractFirst = contractRows.Count + 1;
                        }

                        var payload = new byte[12];
                        payload[0] = 1;
                        payload[1] = 1;
                        payload[2] = checked((byte)relationship);
                        payload[3] = 1;
                        BinaryPrimitives.WriteUInt32LittleEndian(
                            payload.AsSpan(8),
                            checked((uint)sourceOrdinal));
                        contractRows.Add(new TrRmContractRow(
                            0x1600_0000u
                                | checked((uint)newMethodDefRow),
                            4, payload));
                        methodContractCount++;
                    }
                }

                methodRows.Add(new TrRmMethodDefRow(0, 0, signatureRow,
                    function.IsPrivate ? 0u
                        : function.IsInternal ? 1u : 2u,
                    methodContractFirst, methodContractCount, bodyRow));
                methodNames.Add(function.Name);
                if (!function.IsPrivate && !function.IsInternal)
                {
                    if (!exportKeys.Add(function.Name))
                    {
                        throw new InvalidDataException(
                            "A local class-reference function ExportKey conflicts with a retained class member.");
                    }

                    exportRows.Add((
                        0x1600_0000u
                            | checked((uint)newMethodDefRow),
                        function.Name));
                }

                continue;
            }

            var matches = methodEmissionPlans.Values.Where(plan =>
                    StringComparer.Ordinal.Equals(function.Name,
                        (string.IsNullOrEmpty(plan.Owner.Namespace)
                            ? plan.Owner.Name
                            : plan.Owner.Namespace + "."
                                + plan.Owner.Name)
                        + "." + plan.Method.Name))
                .ToArray();
            if (matches.Length != 1)
            {
                throw Unsupported(
                    "The retained class MethodDef call views exceed the selected Import and local Body subset.");
            }

            var methodPlan = matches[0];
            var oldMethodRow = GetMethodDefRow(
                methodPlan.Method.DefinitionId);
            var deferredMethodTypes = genericMethodTypeSpecPlans
                .Where(plan => plan.MethodRow == oldMethodRow)
                .ToArray();
            var methodRoot = deferredMethodTypes.Length == 0 ? null
                : FindC1M1ClassMethodImport(
                    program.RetainedGenericMethodRoots, oldMethodRow);
            var providerIdentity = methodPlan.Imports.IsEmpty
                ? rootSnapshot.ModuleIdentity
                : methodPlan.Imports[0].Provider.ModuleIdentity;
            var providerTargetRequirement = methodPlan.Imports.IsEmpty
                ? rootSnapshot.TargetRequirement
                : methodPlan.Imports[0].Provider.TargetRequirement;
            var providerRuntimePolicy = methodPlan.Imports.IsEmpty
                ? rootSnapshot.RuntimePolicy
                : methodPlan.Imports[0].Provider.RuntimePolicy;
            var hasSelectedExport
                = function.ModuleImport is BoundModuleImport selectedImport
                    && (methodPlan.Imports.IsEmpty
                        ? methodPlan.ExportKeys.Contains(
                            selectedImport.ExportKey,
                            StringComparer.Ordinal)
                        : methodPlan.Imports.Any(import =>
                            StringComparer.Ordinal.Equals(import.ExportKey,
                                selectedImport.ExportKey)));
            var hasDeferredResultType = deferredMethodTypes.Any(
                static plan => plan.IsResult);
            var methodReceiverWritable
                = methodPlan.Method.ReceiverTypeSpecReference != 0
                    && (schema.UsesMethodReceiverAccess()
                        ? (methodPlan.Method.Flags & 4) != 0
                        : schema.UsesObjectConstruction()
                            && (rootTables.TypeSpecs.GetTypeSpecRef(
                                methodPlan.Method.ReceiverTypeSpecReference)
                                .Qualifiers & 1) != 0);
            var returnTypeMatches = hasDeferredResultType
                ? methodRoot is not null
                    && AreC1M1MethodSignatureTypesEquivalent(
                        function.ReturnType, function, program,
                        methodRoot.ProofFunction.ReturnType,
                        methodRoot.ProofFunction)
                : TryGetC1M1ClassScalarType(rootTables,
                    methodPlan.Method.ReturnTypeSpecReference,
                    out var methodReturnType)
                    && function.ReturnType == methodReturnType;
            if (function.ModuleImport is not BoundModuleImport functionImport
                || !function.IsExternal
                || function.IsStatic
                    != (methodPlan.Method.ReceiverTypeSpecReference == 0)
                || !StringComparer.Ordinal.Equals(
                    function.DeclaringStruct,
                    string.IsNullOrEmpty(methodPlan.Owner.Namespace)
                        ? methodPlan.Owner.Name
                        : methodPlan.Owner.Namespace + "."
                            + methodPlan.Owner.Name)
                || !StringComparer.Ordinal.Equals(
                    functionImport.ProducerIdentity,
                    providerIdentity)
                || functionImport.TargetRequirement
                    != providerTargetRequirement
                || functionImport.RuntimePolicy
                    != providerRuntimePolicy
                || !hasSelectedExport
                || !returnTypeMatches
                || function.ParameterCount
                    != methodPlan.Method.Parameters.Length
                        + (methodPlan.Method.ReceiverTypeSpecReference == 0
                            ? 0 : 1)
                || function.Variables.Length < function.ParameterCount
                || (!function.IsStatic
                && function.Variables[0].IsMutable
                    != methodReceiverWritable)
                || function.RequiresUnsafeCaller
                    != ((methodPlan.Method.EffectsMask & 4) != 0)
                || function.DoesNotReturn
                    != ((methodPlan.Method.EffectsMask & 2) != 0))
            {
                throw Unsupported(
                    "A retained class MethodDef call view does not match its complete checked Import expectation.");
            }

            for (var ordinal = 0;
                ordinal < methodPlan.Method.Parameters.Length; ordinal++)
            {
                if (deferredMethodTypes.Any(plan =>
                        !plan.IsResult
                        && plan.ParameterOrdinal == ordinal))
                {
                    var proofIndex = ordinal
                        + (methodPlan.Method.ReceiverTypeSpecReference == 0
                            ? 0 : 1);
                    if (methodRoot is null
                        || (uint)proofIndex
                            >= (uint)methodRoot.ProofFunction.Variables.Length
                        || !AreC1M1MethodSignatureTypesEquivalent(
                            function.Variables[proofIndex].Type, function,
                            program,
                            methodRoot.ProofFunction.Variables[proofIndex]
                                .Type,
                            methodRoot.ProofFunction))
                    {
                        throw Unsupported(
                            "A retained class MethodDef generic nominal parameter differs from its checked imported signature.");
                    }

                    continue;
                }

                if (!TryGetC1M1ClassScalarType(rootTables,
                        methodPlan.Method.Parameters[ordinal]
                            .TypeSpecReference, out var parameterType)
                    || function.Variables[ordinal
                        + (methodPlan.Method.ReceiverTypeSpecReference == 0
                            ? 0 : 1)].Type != parameterType)
                {
                    throw Unsupported(
                        "A retained class MethodDef call view changed its checked parameter types.");
                }
            }
        }

        foreach (var plan in moduleFunctionEmissionPlans.Values)
        {
            var oldMethodRow = plan.MethodDefRow;
            var newMethodRow = methodDefRowMap[oldMethodRow];
            var function = plan.Function;
            var isForwardedImport = !plan.ImportRows.IsEmpty;
            var signatureRow
                = rootTables.Methods.GetSignature(oldMethodRow);
            var (parameterStart, parameterCount)
                = rootTables.Signatures.GetParamRange(signatureRow);
            var effectsMask
                = rootTables.Contracts.GetEffectsMask(oldMethodRow);
            var dependencyContractRows
                = rootTables.Contracts.GetDependencyContractRows(
                    oldMethodRow);
            var dependencyContracts
                = DecodeC1M1ClassMethodDependencyContracts(
                    dependencyContractRows
                        .Select(static row => row.Contract)
                        .ToImmutableArray(),
                    parameterCount, hasReceiver: false);
            var methodFlags
                = rootTables.Methods.GetFlags(oldMethodRow);
            if (rootTables.Methods.GetOwnerType(oldMethodRow) != 0
                || rootTables.Signatures.GetReceiverTypeSpec(signatureRow) != 0
                || rootTables.Signatures.GetGenericRange(signatureRow).Count
                    != 0
                || (methodFlags & 3) != 2
                || (methodFlags & 4) != 0
                || (effectsMask & ~6u) != 0)
            {
                throw new InvalidDataException(
                    "A retained top-level class-reference function differs from its checked MethodDef contract.");
            }

            if (isForwardedImport)
            {
                var definitionId = 0x1600_0000u
                    | checked((uint)oldMethodRow);
                if (function is not null || plan.ImportRows.IsEmpty
                    || rootTables.Methods.GetBodyRow(oldMethodRow) != 0
                    || plan.ImportRows.Any(importRow =>
                        rootTables.Imports
                            .GetExpectedDefinitionForImportRow(importRow)
                            != definitionId)
                    || !plan.ExportKeys.IsEmpty)
                {
                    throw new InvalidDataException(
                        "A forwarded top-level MethodDef must retain bodyless Import aliases and no local Export.");
                }
            }
            else if (function is null
                || rootTables.Methods.GetBodyRow(oldMethodRow) == 0
                || function.ParameterCount != parameterCount
                || function.Variables.Length != parameterCount
                || function.IsPrivate || function.IsInternal
                || !function.IsExternal
                || !function.IsModuleReference
                || function.IsConstructor || function.IsClassConstructor
                || function.ClassConstructor != -1
                || function.IsStatic || function.IsCleanup
                || function.Intrinsic != BoundIntrinsic.None
                || function.NoFailureProof
                || function.HasPureEvaluationForm
                || !function.DeclarationContractClauses.IsDefaultOrEmpty
                || !C1M1DependencyContractsEqual(
                    function.DependencyContracts,
                    dependencyContracts)
                || function.RequiresUnsafeCaller
                    != ((effectsMask & 4) != 0)
                || function.DeclaredRequiresUnsafeCaller
                    != ((effectsMask & 4) != 0)
                || function.DoesNotReturn != ((effectsMask & 2) != 0))
            {
                throw new InvalidDataException(
                    "A retained top-level class-reference function differs from its checked MethodDef contract.");
            }

            var resultTypeSpecReference
                = rootTables.Signatures.GetResultTypeSpec(signatureRow);
            if (function is BoundFunction moduleFunction
                && !ModuleFunctionTypeMatches(resultTypeSpecReference,
                    moduleFunction.ReturnType))
            {
                throw new InvalidDataException(
                    "A retained top-level class-reference function result differs from its MethodSig.");
            }

            var signatureOutputRow = methodSignatureRows.Count + 1;
            var remappedResultTypeSpec = RemapC1M1ClassTypeSpec(
                rootTables, resultTypeSpecReference, typeDefRowMap,
                importRowMap, outputTypeSpecRows, typeSpecRowMap,
                activeTypeSpecRows, depth: 0,
                genericParamRowMap: genericParamRowMap);
            var outputParameterStart = parameterCount == 0
                ? 0 : parameterRows.Count + 1;
            for (var ordinal = 0; ordinal < parameterCount; ordinal++)
            {
                var parameterRow = parameterStart + ordinal;
                var typeSpecReference
                    = rootTables.Parameters.GetTypeSpec(parameterRow);
                var mode = rootTables.Parameters.GetMode(parameterRow);
                var flags = rootTables.Parameters.GetFlags(parameterRow);
                var contextualTypeViewIndex
                    = function is BoundFunction moduleParameterFunction
                        ? GetMethodSignatureTypeViewIndex(
                            moduleParameterFunction, ordinal,
                            moduleParameterFunction.Variables[ordinal].Type)
                        : -1;
                if (mode != 0 || flags > 1
                    || (function is BoundFunction moduleFunction
                        && (moduleFunction.Variables[ordinal].IsMutable
                            || moduleFunction.Variables[ordinal].IsOut
                            || moduleFunction.Variables[ordinal].IsInit
                            || moduleFunction.Variables[ordinal]
                                .IsInitializationLocal
                            || moduleFunction.Variables[ordinal]
                                .IsImplicitRef != (flags == 1)
                            || !ModuleFunctionTypeMatches(
                                typeSpecReference,
                                moduleFunction.Variables[ordinal].Type)))
                {
                    throw new InvalidDataException(
                        "A retained top-level class-reference parameter differs from its checked Param row.");
                }

                var remappedTypeSpec = contextualTypeViewIndex >= 0
                    && function is BoundFunction contextualFunction
                    ? ContextualFieldTypeSpecReference(
                        contextualFunction.Variables[ordinal].Type,
                        contextualTypeViewIndex)
                    : RemapC1M1ClassTypeSpec(rootTables,
                        typeSpecReference, typeDefRowMap, importRowMap,
                        outputTypeSpecRows, typeSpecRowMap,
                        activeTypeSpecRows, depth: 0,
                        genericParamRowMap: genericParamRowMap);
                parameterRows.Add(new TrRmParamRow(signatureOutputRow,
                    ordinal, remappedTypeSpec, mode, flags));

                var hasParameterLabel
                    = rootTables.MethodParameterLabels
                        .TryGetNameStringRow(oldMethodRow,
                            checked((uint)ordinal),
                            out var parameterNameStringRow);
                uint defaultConstantSourceRow = 0;
                var hasDefaultSurface
                    = schema.UsesClassParameterDefaults()
                        && rootTables.MethodParameterLabels
                            .TryGetDefaultConstantSourceRow(oldMethodRow,
                                checked((uint)ordinal),
                                out defaultConstantSourceRow);
                if (schema.UsesClassParameterDefaults()
                    && hasParameterLabel != hasDefaultSurface)
                {
                    throw new InvalidDataException(
                        "A retained top-level class-reference function has an incomplete CM1 parameter surface.");
                }

                if (schema.UsesClassParameterDefaults() && hasParameterLabel)
                {
                    uint remappedDefaultConstantSourceRow = 0;
                    if (defaultConstantSourceRow != 0)
                    {
                        var defaultValue
                            = TrRmConstantSourceValueValidator
                                .MaterializeFrozenScalar(
                                    checked((int)defaultConstantSourceRow),
                                    typeSpecReference,
                                    rootTables.Methods.GetOwnerModule(
                                        oldMethodRow),
                                    rootSnapshot.TargetRequirement,
                                    rootTables.ConstantSources,
                                    rootTables.FrozenValueNodes,
                                    rootTables.Blobs, rootTables.TypeSpecs,
                                    rootTables.Features, rootTables.Modules,
                                    rootTables.A3LayoutResourceLedger);
                        var frozenValue
                            = frozenScalarValueBuilder.Add(defaultValue);
                        if (frozenValue.TypeSpecRef != remappedTypeSpec)
                        {
                            throw new InvalidDataException(
                                "A retained top-level class-reference parameter default changed its exact TypeSpec.");
                        }

                        remappedDefaultConstantSourceRow
                            = frozenValue.ConstantSourceRow;
                    }

                    if (parameterSurfaceRows.Count >= 0xFF_FFFF)
                    {
                        throw Unsupported(
                            "The C1M1 MethodParameterSurface table exceeds its row limit.");
                    }

                    parameterSurfaceRows.Add(
                        new C1M1ParameterSurfaceRow(newMethodRow, ordinal,
                            rootTables.Strings.DecodeName(
                                parameterNameStringRow),
                            remappedDefaultConstantSourceRow));
                }
                else if (schema.UsesMethodParameterLabels()
                    && hasParameterLabel)
                {
                    methodParameterLabelRows.Add((
                        newMethodRow, ordinal,
                        rootTables.Strings.DecodeName(
                            parameterNameStringRow)));
                }
            }

            methodSignatureRows.Add(new TrRmMethodSigRow(
                remappedResultTypeSpec, 0, outputParameterStart,
                parameterCount, 0, 0));

            var methodContractFirst = 0;
            var methodContractCount = 0;
            if (effectsMask != 0)
            {
                methodContractFirst = contractRows.Count + 1;
                contractRows.Add(new TrRmContractRow(
                    0x1600_0000u | checked((uint)newMethodRow), 1,
                    s_effectPayloads[effectsMask]));
                methodContractCount++;
            }

            var methodDependencyContractRowMap
                = new Dictionary<int, int>();
            foreach (var (oldContractRow, dependency)
                in dependencyContractRows)
            {
                if (methodContractFirst == 0)
                {
                    methodContractFirst = contractRows.Count + 1;
                }

                var payload = new byte[12];
                payload[0] = dependency.DestinationKind;
                payload[1] = dependency.SourceKind;
                payload[2] = dependency.Relationship;
                payload[3] = dependency.Phase;
                BinaryPrimitives.WriteUInt32LittleEndian(
                    payload.AsSpan(4), dependency.DestinationOrdinal);
                BinaryPrimitives.WriteUInt32LittleEndian(
                    payload.AsSpan(8), dependency.SourceOrdinal);
                contractRows.Add(new TrRmContractRow(
                    0x1600_0000u | checked((uint)newMethodRow), 4, payload));
                methodDependencyContractRowMap.Add(oldContractRow,
                    contractRows.Count);
                methodContractCount++;
            }

            var outputBodyRow = 0;
            if (isForwardedImport)
            {
                if (rootTables.Methods.GetBodyRow(oldMethodRow) != 0)
                {
                    throw new InvalidDataException(
                        "A forwarded top-level MethodDef unexpectedly has a local Body.");
                }
            }
            else
            {
                var sourceBodyRow
                    = rootTables.Methods.GetBodyRow(oldMethodRow);
                var sourceBody = rootTables.Bodies.GetRow(sourceBodyRow);
                if (sourceBody.IsEmpty
                    || sourceBody.Span[0] is not
                        (0x01 or 0x02 or 0x03 or 0x04 or 0x05))
                {
                    throw Unsupported(
                        "Top-level Construction-2 function re-emission supports Body kinds 01-05.");
                }

                outputBodyRow = bodyRows.Count + 1;
                bodyRows.Add(RemapC1M1ClassMethodBody(rootTables, sourceBody,
                    typeDefRowMap, methodDefRowMap, fieldRowMap,
                    importRowMap, fieldPathRowMap, fieldPathRows,
                    fieldPathRowsByIdentity,
                    methodDependencyContractRowMap,
                    frozenScalarValueBuilder, outputTypeSpecRows,
                    typeSpecRowMap, activeTypeSpecRows, schema,
                    genericParamRowMap));
            }
            methodRows.Add(new TrRmMethodDefRow(0, 0,
                signatureOutputRow, methodFlags, methodContractFirst,
                methodContractCount, outputBodyRow));
            methodNames.Add(rootTables.Methods.GetName(oldMethodRow));
            foreach (var exportKey in plan.ExportKeys)
            {
                if (!exportKeys.Add(exportKey))
                {
                    throw new InvalidDataException(
                        "A top-level class-reference function ExportKey conflicts with a retained class declaration.");
                }

                exportRows.Add((
                    0x1600_0000u | checked((uint)newMethodRow), exportKey));
            }
        }

        foreach (var plan in privateModuleFunctionEmissionPlans.Values)
        {
            var oldMethodRow = plan.MethodDefRow;
            var newMethodRow = methodDefRowMap[oldMethodRow];
            var signatureRow
                = rootTables.Methods.GetSignature(oldMethodRow);
            var (parameterStart, parameterCount)
                = rootTables.Signatures.GetParamRange(signatureRow);
            var effectsMask
                = rootTables.Contracts.GetEffectsMask(oldMethodRow);
            var dependencyContractRows
                = rootTables.Contracts.GetDependencyContractRows(
                    oldMethodRow);
            _ = DecodeC1M1ClassMethodDependencyContracts(
                dependencyContractRows
                    .Select(static row => row.Contract)
                    .ToImmutableArray(),
                parameterCount, hasReceiver: false);
            var methodFlags
                = rootTables.Methods.GetFlags(oldMethodRow);
            var sourceBodyRow
                = rootTables.Methods.GetBodyRow(oldMethodRow);
            if (rootTables.Methods.GetOwnerType(oldMethodRow) != 0
                || rootTables.Methods.GetOwnerModule(oldMethodRow)
                    != rootModuleRow
                || rootTables.Imports.IsExpectedDefinition(
                    0x1600_0000u | checked((uint)oldMethodRow))
                || exportedMethodDefinitionIds.Contains(
                    0x1600_0000u | checked((uint)oldMethodRow))
                || rootTables.Signatures.GetReceiverTypeSpec(signatureRow) != 0
                || rootTables.Signatures.GetGenericRange(signatureRow).Count
                    != 0
                || (methodFlags & 3) is not (0 or 1)
                || (methodFlags & 4) != 0
                || (effectsMask & ~6u) != 0
                || sourceBodyRow == 0)
            {
                throw new InvalidDataException(
                    "A retained private module helper differs from its checked MethodDef contract.");
            }

            var signatureOutputRow = methodSignatureRows.Count + 1;
            var resultTypeSpecReference
                = rootTables.Signatures.GetResultTypeSpec(signatureRow);
            var remappedResultTypeSpec = RemapC1M1ClassTypeSpec(
                rootTables, resultTypeSpecReference, typeDefRowMap,
                importRowMap, outputTypeSpecRows, typeSpecRowMap,
                activeTypeSpecRows, depth: 0,
                genericParamRowMap: genericParamRowMap);
            var outputParameterStart = parameterCount == 0
                ? 0 : parameterRows.Count + 1;
            for (var ordinal = 0; ordinal < parameterCount; ordinal++)
            {
                var parameterRow = parameterStart + ordinal;
                var typeSpecReference
                    = rootTables.Parameters.GetTypeSpec(parameterRow);
                var mode = rootTables.Parameters.GetMode(parameterRow);
                var flags = rootTables.Parameters.GetFlags(parameterRow);
                if (mode != 0 || flags > 1)
                {
                    throw Unsupported(
                        "A retained private module helper requires ordinary by-value Param rows.");
                }

                var remappedTypeSpec = RemapC1M1ClassTypeSpec(rootTables,
                    typeSpecReference, typeDefRowMap, importRowMap,
                    outputTypeSpecRows, typeSpecRowMap, activeTypeSpecRows,
                    depth: 0, genericParamRowMap: genericParamRowMap);
                parameterRows.Add(new TrRmParamRow(signatureOutputRow,
                    ordinal, remappedTypeSpec, mode, flags));
            }

            methodSignatureRows.Add(new TrRmMethodSigRow(
                remappedResultTypeSpec, 0, outputParameterStart,
                parameterCount, 0, 0));

            var methodContractFirst = 0;
            var methodContractCount = 0;
            if (effectsMask != 0)
            {
                methodContractFirst = contractRows.Count + 1;
                contractRows.Add(new TrRmContractRow(
                    0x1600_0000u | checked((uint)newMethodRow), 1,
                    s_effectPayloads[effectsMask]));
                methodContractCount++;
            }

            var methodDependencyContractRowMap
                = new Dictionary<int, int>();
            foreach (var (oldContractRow, dependency)
                in dependencyContractRows)
            {
                if (methodContractFirst == 0)
                {
                    methodContractFirst = contractRows.Count + 1;
                }

                var payload = new byte[12];
                payload[0] = dependency.DestinationKind;
                payload[1] = dependency.SourceKind;
                payload[2] = dependency.Relationship;
                payload[3] = dependency.Phase;
                BinaryPrimitives.WriteUInt32LittleEndian(
                    payload.AsSpan(4), dependency.DestinationOrdinal);
                BinaryPrimitives.WriteUInt32LittleEndian(
                    payload.AsSpan(8), dependency.SourceOrdinal);
                contractRows.Add(new TrRmContractRow(
                    0x1600_0000u | checked((uint)newMethodRow), 4, payload));
                methodDependencyContractRowMap.Add(oldContractRow,
                    contractRows.Count);
                methodContractCount++;
            }

            var sourceBody = rootTables.Bodies.GetRow(sourceBodyRow);
            var outputBodyRow = bodyRows.Count + 1;
            bodyRows.Add(RemapC1M1ClassMethodBody(rootTables, sourceBody,
                typeDefRowMap, methodDefRowMap, fieldRowMap, importRowMap,
                fieldPathRowMap, fieldPathRows, fieldPathRowsByIdentity,
                methodDependencyContractRowMap,
                frozenScalarValueBuilder, outputTypeSpecRows,
                typeSpecRowMap, activeTypeSpecRows, schema,
                genericParamRowMap));
            methodRows.Add(new TrRmMethodDefRow(0, 0, signatureOutputRow,
                methodFlags, methodContractFirst, methodContractCount,
                outputBodyRow));
            methodNames.Add(rootTables.Methods.GetName(oldMethodRow));
        }

        var methodClosureRowCount = (long)methodRows.Count
            + methodSignatureRows.Count + parameterRows.Count
            + genericParamRows.Count + genericConstraintRows.Count
            + parameterSurfaceRows.Count + contractRows.Count
            + bodyRows.Count + fieldPathRows.Count
            + frozenScalarValueBuilder.BlobCount
            + frozenScalarValueBuilder.FrozenValueNodeCount
            + frozenScalarValueBuilder.ConstantSourceCount;
        if (methodRows.Count > 0xFF_FFFF
            || methodSignatureRows.Count > 0xFF_FFFF
            || parameterRows.Count > 0xFF_FFFF
            || genericParamRows.Count > 0xFF_FFFF
            || genericConstraintRows.Count > 0xFF_FFFF
            || parameterSurfaceRows.Count > 0xFF_FFFF
            || contractRows.Count > 0xFF_FFFF
            || bodyRows.Count > 0xFF_FFFF
            || fieldPathRows.Count > 0xFF_FFFF
            || methodClosureRowCount > SemanticVerifier.MaximumAnalysisVisits)
        {
            throw Unsupported(
                "The retained class MethodDef closure exceeds the checked metadata row limits.");
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
        if (genericParamRows.Count != 0
            && !featureRows.Any(static feature =>
                feature.FeatureId
                    == TrRmFeatureTableReader.GenericMethodCallsFeatureId))
        {
            featureRows = [
                .. featureRows,
                TrRmFeatureRow.GenericMethodCallsCandidate,
            ];
        }
        if ((parameterSurfaceRows.Count != 0
                || methodParameterLabelRows.Count != 0)
            && !featureRows.Any(static feature =>
                feature.FeatureId
                    == TrRmFeatureTableReader.MethodParameterLabelsFeatureId))
        {
            featureRows = [
                .. featureRows,
                TrRmFeatureRow.MethodParameterLabelsCandidate,
            ];
        }
        if (frozenScalarValueBuilder.ConstantSourceCount != 0
            && !featureRows.Contains(TrRmFeatureRow.TypedFrozenValues))
        {
            featureRows = [
                .. featureRows,
                TrRmFeatureRow.TypedFrozenValues,
            ];
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
        foreach (var methodName in methodNames)
        {
            AddString(methodName);
        }
        foreach (var genericParamName in genericParamNames)
        {
            AddString(genericParamName);
        }
        foreach (var parameterSurface in parameterSurfaceRows)
        {
            AddString(parameterSurface.Name);
        }
        foreach (var parameterLabel in methodParameterLabelRows)
        {
            AddString(parameterLabel.Name);
        }
        foreach (var moduleRefRow in moduleRefRowsByOldRow)
        {
            AddString(rootTables.ModuleRefs.GetProducerIdentity(moduleRefRow));
        }
        foreach (var binding in bindingsByImportRow.Values)
        {
            AddString(binding.ExportKey);
        }
        foreach (var binding in fieldBindingsByImportRow.Values)
        {
            AddString(binding.ExportKey);
        }
        foreach (var binding in methodBindingsByImportRow.Values)
        {
            AddString(binding.ExportKey);
        }
        foreach (var importRow in moduleFunctionImportRowsByImportRow.Keys)
        {
            AddString(rootTables.Imports.GetBindingForImportRow(importRow)
                .ExportKey);
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
        for (var methodIndex = 0; methodIndex < methodRows.Count;
            methodIndex++)
        {
            methodRows[methodIndex] = methodRows[methodIndex] with {
                NameStringRow = stringRows[methodNames[methodIndex]],
            };
        }
        for (var genericParamIndex = 0;
            genericParamIndex < genericParamRows.Count;
            genericParamIndex++)
        {
            genericParamRows[genericParamIndex]
                = genericParamRows[genericParamIndex] with {
                    NameStringRow = stringRows[
                        genericParamNames[genericParamIndex]],
                };
        }

        var outputTypeSpecBytes = outputTypeSpecRows.Count == 0
            ? []
            : TrRmTypeSpecTableWriter.Encode(outputTypeSpecRows,
                outputTypeDefRows.Count, importRowMap.Count,
                genericParamRows.Count, 0,
                schema, methodDefCount: methodRows.Count);
        var typeSpecReaderBytes = outputTypeSpecRows.Count == 0
            ? new byte[sizeof(uint)] : outputTypeSpecBytes;
        var typeSpecs = new TrRmTypeSpecTableReader(typeSpecReaderBytes,
            outputTypeSpecRows.Count, outputTypeDefRows.Count,
            importRowMap.Count, genericParamRows.Count, 0, schema,
            methodDefCount: methodRows.Count);

        var targetProfileBytes = GetTargetProfileSection(rootIndex,
            out var targetProfileCount);
        var targetProfiles = targetProfileBytes.Length == 0
            ? new TrRmTargetProfileTableReader([], 0)
            : new TrRmTargetProfileTableReader(targetProfileBytes,
                targetProfileCount);
        var featureBytes = TrRmFeatureTableWriter.Encode(featureRows,
            schema);
        var moduleBytes = TrRmModuleTableWriter.Encode([
            new TrRmModuleRow(moduleIdentityStringRow,
                moduleTargetRequirement, rootSnapshot.RuntimePolicy, 0,
                1, 1, 1, 1,
                featureRows.Length == 0 ? 0 : 1,
                featureRows.Length, rootSnapshot.GoverningRuleEdition),
        ], strings, moduleRefRowMap.Count, outputTypeDefRows.Count,
            methodRows.Count, 0,
            featureRows.Length);
        var moduleTables = new TrRmModuleTableReader(moduleBytes, 1,
            strings, moduleRefRowMap.Count, outputTypeDefRows.Count,
            methodRows.Count, 0,
            featureRows.Length);
        var typeDefBytes = TrRmTypeDefTableWriter.Encode(outputTypeDefRows,
            strings, moduleTables, fieldRows.Count, genericParamRows.Count,
            layoutRows.Count, 0, schema);
        var typeDefs = new TrRmTypeDefTableReader(typeDefBytes,
            outputTypeDefRows.Count, strings, moduleTables, fieldRows.Count,
            genericParamRows.Count, layoutRows.Count, 0, schema);

        var typeDefBindingEqualityBytes
            = typeDefBindingEqualityRows.Count == 0 ? []
                : TrRmTypeDefBindingEqualityTableWriter.Encode(
                    typeDefBindingEqualityRows, moduleTables, typeDefs,
                    allowClassTypeDefs:
                        IsSelectedClassTypeDefBindingEqualitySchemaIdentity(
                            schemaIdentity));
        var layoutFieldCount = layoutFieldRows.Count;
        var fieldBytes = fieldRows.Count == 0 ? []
            : TrRmFieldDefTableWriter.Encode(fieldRows, strings, typeDefs,
                typeSpecs, schema);
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
        var moduleRefs = moduleRefRows.Length == 0 ? default
            : new TrRmModuleRefTableReader(moduleRefBytes,
                moduleRefRows.Length, strings, moduleTables);
        var signatureBytes = methodSignatureRows.Count == 0 ? []
            : TrRmMethodSigTableWriter.Encode(methodSignatureRows,
                typeSpecs, parameterRows.Count, genericParamRows.Count);
        var signatures = methodSignatureRows.Count == 0 ? default
            : new TrRmMethodSigTableReader(signatureBytes,
                methodSignatureRows.Count, typeSpecs,
                parameterRows.Count, genericParamRows.Count);
        var parameterBytes = parameterRows.Count == 0 ? []
            : TrRmParamTableWriter.Encode(parameterRows, signatures,
                typeSpecs, schema);
        var parameters = parameterRows.Count == 0 ? default
            : new TrRmParamTableReader(parameterBytes, parameterRows.Count,
                signatures, typeSpecs, schema);
        var methodBytes = methodRows.Count == 0 ? []
            : TrRmMethodDefTableWriter.Encode(methodRows, strings,
                moduleTables, typeDefs, signatures, contractRows.Count,
                bodyRows.Count, schema);
        var bodyBytes = bodyRows.Count == 0 ? []
            : TrRmVariableTableWriter.EncodeBodies(bodyRows);
        var methods = methodRows.Count == 0 ? default
            : new TrRmMethodDefTableReader(methodBytes, methodRows.Count,
                strings, moduleTables, typeDefs, signatures,
                contractRows.Count, bodyRows.Count, schema);
        var classImportRows = CreateC1M1ClassImports(
            bindingsByImportRow, fieldBindingsByImportRow,
            methodBindingsByImportRow,
            moduleFunctionImportRowsByImportRow,
            importRowMap, moduleRefRowMap,
            typeDefRowMap, fieldRowMap, methodDefRowMap, stringRows,
            rootTables);
        var featureReader = featureRows.Length == 0 ? default
            : new TrRmFeatureTableReader(featureBytes, featureRows.Length,
                schema);
        var importBytes = classImportRows.Length == 0 ? []
            : TrRmImportTableWriter.Encode(classImportRows, strings,
                moduleRefs, typeDefs, methods, default, default, default,
                fields, featureReader, moduleTables, schema);
        var imports = classImportRows.Length == 0 ? default
            : new TrRmImportTableReader(importBytes, classImportRows.Length,
                strings, moduleRefs, typeDefs, methods, default, default,
                default, fields, featureReader, moduleTables, schema);
        var genericParamBytes = genericParamRows.Count == 0 ? []
            : TrRmGenericParamTableWriter.Encode(genericParamRows,
                strings, typeDefs, methods, signatures, typeSpecs,
                genericConstraintRows.Count);
        var genericParams = genericParamRows.Count == 0 ? default
            : new TrRmGenericParamTableReader(genericParamBytes,
                genericParamRows.Count, strings, typeDefs, methods,
                signatures, typeSpecs, genericConstraintRows.Count);
        var associatedRelations
            = new TrRmAssociatedRelationTableReader([], 0, typeSpecs, 0,
                imports);
        var constraintBytes = genericConstraintRows.Count == 0 ? []
            : TrRmConstraintTableWriter.Encode(genericConstraintRows,
                genericParams, typeSpecs, associatedRelations);
        var fieldPathBytes = fieldPathRows.Count == 0 ? []
            : TrRmFieldPathTableWriter.Encode(fieldPathRows, typeSpecs,
                fields, imports);
        var frozenScalarValueTables
            = frozenScalarValueBuilder.Encode(typeSpecs);
        var parameterSurfaceBytes
            = new byte[checked(parameterSurfaceRows.Count * 16)];
        for (var index = 0; index < parameterSurfaceRows.Count; index++)
        {
            var surface = parameterSurfaceRows[index];
            var offset = index * 16;
            Write(parameterSurfaceBytes, offset,
                checked((uint)surface.MethodDefRow));
            Write(parameterSurfaceBytes, offset + 4,
                checked((uint)surface.Ordinal));
            Write(parameterSurfaceBytes, offset + 8,
                checked((uint)stringRows[surface.Name]));
            Write(parameterSurfaceBytes, offset + 12,
                surface.DefaultConstantSourceRow);
        }
        var contractBytes = contractRows.Count == 0 ? []
            : schema.UsesConstructionContracts()
                ? TrRmContractTableWriter
                    .EncodeConstructionDraftForSchema(contractRows,
                        typeDefs, methods, signatures, parameters, typeSpecs,
                        fields, default, default, featureReader, moduleTables,
                        predicateGraphCount: 0, schema: schema)
                : TrRmContractTableWriter.EncodeWithPredicateGraphs(
                    contractRows, typeDefs, methods, signatures, parameters,
                    typeSpecs, featureReader, moduleTables,
                    predicateGraphCount: 0, schema: schema);
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
                checked((uint)classImportRows.Length), importBytes));
        }
        if (typeDefBindingEqualityBytes.Length != 0)
        {
            sections.Add(new TrRmSection(0x0067, 0,
                checked((uint)typeDefBindingEqualityRows.Count),
                typeDefBindingEqualityBytes));
        }
        if (exportBytes.Length != 0)
        {
            sections.Add(new TrRmSection(0x001D, 0,
                checked((uint)exportRows.Count), exportBytes));
        }
        if (methodRows.Count != 0)
        {
            sections.Add(new TrRmSection(0x0016, 0,
                checked((uint)methodRows.Count), methodBytes));
            sections.Add(new TrRmSection(0x0017, 0,
                checked((uint)methodSignatureRows.Count), signatureBytes));
        }
        if (genericParamRows.Count != 0)
        {
            sections.Add(new TrRmSection(0x0019, 0,
                checked((uint)genericParamRows.Count), genericParamBytes));
        }
        if (genericConstraintRows.Count != 0)
        {
            sections.Add(new TrRmSection(0x001A, 0,
                checked((uint)genericConstraintRows.Count),
                constraintBytes));
        }
        if (parameterRows.Count != 0)
        {
            sections.Add(new TrRmSection(0x0018, 0,
                checked((uint)parameterRows.Count), parameterBytes));
        }
        if (bodyRows.Count != 0)
        {
            sections.Add(new TrRmSection(0x0020, 0,
                checked((uint)bodyRows.Count), bodyBytes));
        }
        if (contractRows.Count != 0)
        {
            sections.Add(new TrRmSection(0x001B, 0,
                checked((uint)contractRows.Count), contractBytes));
        }
        if (frozenScalarValueTables.BlobCount != 0)
        {
            sections.Add(new TrRmSection(0x0003, 0,
                checked((uint)frozenScalarValueTables.BlobCount),
                frozenScalarValueTables.BlobSection));
        }
        if (frozenScalarValueTables.ConstantSourceCount != 0)
        {
            sections.Add(new TrRmSection(0x0030, 0,
                checked((uint)frozenScalarValueTables.ConstantSourceCount),
                frozenScalarValueTables.ConstantSourceSection));
            sections.Add(new TrRmSection(0x003F, 0,
                checked((uint)frozenScalarValueTables.FrozenValueNodeCount),
                frozenScalarValueTables.FrozenValueNodeSection));
        }
        if (parameterSurfaceRows.Count != 0)
        {
            sections.Add(new TrRmSection(0x0066, 0,
                checked((uint)parameterSurfaceRows.Count),
                parameterSurfaceBytes));
        }
        else if (methodParameterLabelRows.Count != 0)
        {
            var methodParameterLabelBytes = new byte[
                checked(methodParameterLabelRows.Count * 12)];
            for (var index = 0;
                index < methodParameterLabelRows.Count; index++)
            {
                var label = methodParameterLabelRows[index];
                var offset = index * 12;
                Write(methodParameterLabelBytes, offset,
                    checked((uint)label.MethodDefRow));
                Write(methodParameterLabelBytes, offset + 4,
                    checked((uint)label.Ordinal));
                Write(methodParameterLabelBytes, offset + 8,
                    checked((uint)stringRows[label.Name]));
            }

            sections.Add(new TrRmSection(0x0066, 0,
                checked((uint)methodParameterLabelRows.Count),
                methodParameterLabelBytes));
        }
        if (fieldPathRows.Count != 0)
        {
            sections.Add(new TrRmSection(0x004B, 0,
                checked((uint)fieldPathRows.Count), fieldPathBytes));
        }

        sections.Sort(static (left, right) =>
            left.Kind.CompareTo(right.Kind));
        var artifact = TrRmArtifactWriter.Encode(sections, schema,
            standardSchemaIdentity: schemaIdentity);
        if (genericConstraintPlans.Count != 0
            || genericMethodTypeSpecPlans.Count != 0
            || genericMethodDefaultTypeSpecPlans.Count != 0)
        {
            artifact = ReemitC1M1ClassAssociatedRelationConstraints(
                artifact, program, rootTables, rootModuleRow, rootSnapshot,
                schema, genericConstraintPlans, genericParamRows.Count,
                genericParamRowMap, typeDefRowMap, importRowMap,
                genericMethodTypeSpecPlans,
                genericMethodDefaultTypeSpecPlans);
        }

        _ = TrRmStructuralMetadataValidator
            .ValidateC1AndGetExecutionTables(
                new TrRmArtifactIndex(artifact, schema,
                    standardSchemaIdentity: schemaIdentity));
        return artifact;
    }

    private static byte[] EncodeC1M1MetadataClassNoOpUnitBody(
        TrRmArtifactSchema schema)
    {
        var maps = TrRmRegisterBodyMapBuilder.Create(
            Array.Empty<uint>(), []);
        var homeMap = Array.Empty<byte>();
        var records = new ReadOnlyMemory<byte>[] {
            new(new byte[] { 1, 0, 0, 0, 0, 0, 0, 0 }),
        };
        var code = new byte[] { 0x18, 0 };
        return TrRmRegisterBodyWriter.Encode(homeMap, maps.TypeMap,
            default, default, default,
            [new TrRmRegisterBodyBlock(0,
                checked((uint)code.Length), 0)],
            records, code, contractCount: 0, schema: schema,
            generalPredicateGraphsInitializationReferenceBodies
                : schema.UsesGeneralPredicateGraphs());
    }

    private static byte[] EncodeC1M1MetadataClassConstantReturnBody(
        BoundConstant constant,
        Func<PrimitiveType, uint> typeSpecReferenceFor,
        TrRmFrozenScalarValueBuilder frozenScalarValueBuilder,
        TrRmArtifactSchema schema)
    {
        if (!schema.UsesObjectConstruction()
            || constant.Type is PrimitiveType.NativeInt
                or PrimitiveType.NativeUInt
            || constant.RepresentationType != constant.Type
            || !constant.Bits.IsCanonicalFor(constant.Type)
            || !TryGetC1M1SourceScalarTypeSpecReference(
                constant.Type, out _))
        {
            throw Unsupported(
                "A local class-reference constant return requires an exact fixed-scalar Construction-2 value.");
        }

        var typeSpecReference
            = typeSpecReferenceFor(constant.Type);
        var frozenValue = frozenScalarValueBuilder.Add(constant);
        if (frozenValue.TypeSpecRef != typeSpecReference)
        {
            throw Invalid(
                "A local class-reference constant return changed its exact TypeSpec.");
        }

        var constantMap = new byte[8];
        BinaryPrimitives.WriteUInt32LittleEndian(constantMap,
            frozenValue.TypeSpecRef);
        BinaryPrimitives.WriteUInt32LittleEndian(
            constantMap.AsSpan(sizeof(uint)),
            frozenValue.ConstantSourceRow);
        var maps = TrRmRegisterBodyMapBuilder.Create(
            Array.Empty<uint>(), []);
        ReadOnlyMemory<byte>[] records = [
            new byte[] { 1, 0, 0, 0, 0, 0, 0, 0 },
        ];
        var code = new byte[] { 0xD4, 0, 0, 0, 0x17, 0 };
        return TrRmRegisterBodyWriter.Encode(default, maps.TypeMap,
            default, default, constantMap, [
                new TrRmRegisterBodyBlock(0,
                    checked((uint)code.Length), 0),
            ], records, code, contractCount: 0, schema: schema,
            generalPredicateGraphsInitializationReferenceBodies
                : schema.UsesGeneralPredicateGraphs());
    }

    private static byte[] EncodeC1M1MetadataClassDirectModuleCallBody(
        BoundFunction function, BoundCall call,
        BoundFunction targetFunction,
        TrRmStructuralMetadataValidator.ValidatedExecutionTables
            rootTables,
        int oldTargetMethodRow, int targetMethodDefRow,
        BoundProgram program,
        TrRmFrozenScalarValueBuilder frozenScalarValueBuilder,
        Func<PrimitiveType, uint> typeSpecReferenceFor,
        Func<uint, PrimitiveType, bool> methodTypeMatches,
        TrRmArtifactSchema schema)
    {
        if (!schema.UsesObjectConstruction()
            || targetMethodDefRow is <= 0 or > 0xFF_FFFF
            || (function.ReturnType != PrimitiveType.Void
                && !TryGetC1M1SourceScalarTypeSpecReference(
                    function.ReturnType, out _))
            || call.Type != function.ReturnType
            || targetFunction.ReturnType != function.ReturnType
            || targetFunction.ParameterCount
                != targetFunction.Variables.Length
            || targetFunction.ParameterCount
                != call.Arguments.Length
            || call.InterfaceCallIdentity is not null
            || call.GenericMethodCall is not null
            || call.ImportedGenericMethodCall is not null
            || !call.DefaultedArguments.IsDefaultOrEmpty
            || targetFunction.IsConstructor
            || targetFunction.IsClassConstructor
            || targetFunction.IsCleanup || targetFunction.IsOperator
            || targetFunction.Intrinsic != BoundIntrinsic.None
            || targetFunction.DoesNotReturn)
        {
            throw Unsupported(
                "A forwarded module-call Body requires a direct Construction-2 unit or scalar call.");
        }

        var targetSignatureRow = rootTables.Methods.GetSignature(
            oldTargetMethodRow);
        var (targetParameterStart, targetParameterCount)
            = rootTables.Signatures.GetParamRange(targetSignatureRow);
        var targetResultTypeSpecReference
            = rootTables.Signatures.GetResultTypeSpec(targetSignatureRow);
        if (targetParameterCount != targetFunction.ParameterCount
            || rootTables.Signatures.GetReceiverTypeSpec(
                targetSignatureRow) != 0
            || rootTables.Signatures.GetGenericCount(targetSignatureRow) != 0
            || !methodTypeMatches(targetResultTypeSpecReference,
                targetFunction.ReturnType))
        {
            throw Unsupported(
                "A forwarded module-call Body requires the exact nongeneric top-level MethodSig.");
        }

        for (var ordinal = 0; ordinal < targetParameterCount; ordinal++)
        {
            var parameterRow = targetParameterStart + ordinal;
            var targetVariable = targetFunction.Variables[ordinal];
            if (rootTables.Parameters.GetMode(parameterRow) != 0
                || rootTables.Parameters.GetFlags(parameterRow) != 0
                || targetVariable.IsMutable || targetVariable.IsOut
                || targetVariable.IsInit
                || targetVariable.IsImplicitRef
                || targetVariable.IsInitializationLocal
                || !methodTypeMatches(
                    rootTables.Parameters.GetTypeSpec(parameterRow),
                    targetVariable.Type))
            {
                throw Unsupported(
                    "A forwarded module-call Body requires ordinary by-value MethodSig parameters.");
            }
        }

        var typeTokens = new List<uint>(function.ParameterCount);
        var homeIndexByVariable = new int[function.ParameterCount];
        Array.Fill(homeIndexByVariable, -1);
        for (var parameter = 0; parameter < function.ParameterCount;
            parameter++)
        {
            var variable = function.Variables[parameter];
            if (variable.IsMutable || variable.IsOut || variable.IsInit
                || variable.IsImplicitRef
                || variable.IsInitializationLocal)
            {
                throw Unsupported(
                    "A forwarded module-call Body requires immutable value formals.");
            }

            if (variable.Type == PrimitiveType.Void)
            {
                continue;
            }

            var typeToken = CreateStandardBodyTypeMapToken(
                typeSpecReferenceFor(variable.Type));
            homeIndexByVariable[parameter] = typeTokens.Count;
            typeTokens.Add(typeToken);
        }

        var inputSeen = new bool[targetParameterCount];
        var callInputs = new TrRmRegisterBodyCallInput[targetParameterCount];
        var constantMapRows = new List<byte>();
        var callCodePrefix = new List<byte>();
        var constantRegisterCount = 0;
        for (var argumentIndex = 0;
            argumentIndex < call.Arguments.Length; argumentIndex++)
        {
            var formalOrdinal = call.ParameterIndex(argumentIndex);
            if ((uint)formalOrdinal >= (uint)targetParameterCount
                || inputSeen[formalOrdinal]
                || call.ImplicitReferenceTemporary(argumentIndex) != -1)
            {
                throw Unsupported(
                    "A forwarded module-call Body requires one direct value actual per MethodSig formal.");
            }

            var targetVariable
                = targetFunction.Variables[formalOrdinal];
            var argument = call.Arguments[argumentIndex];
            if (targetVariable.Type == PrimitiveType.Void)
            {
                var isUnitActual = argument is BoundConstant {
                    Type: PrimitiveType.Void,
                    RepresentationType: PrimitiveType.Void,
                } || argument is BoundRead unitRead
                    && (uint)unitRead.Variable
                        < (uint)function.ParameterCount
                    && unitRead.Type == PrimitiveType.Void
                    && function.Variables[unitRead.Variable].Type
                        == PrimitiveType.Void
                    && !function.Variables[unitRead.Variable].IsOut
                    && !function.Variables[unitRead.Variable].IsInit
                    && !function.Variables[unitRead.Variable]
                        .IsImplicitRef
                    && !function.Variables[unitRead.Variable]
                        .IsInitializationLocal;
                if (!isUnitActual)
                {
                    throw Unsupported(
                        "A unit module-call formal requires a direct unit actual.");
                }

                callInputs[formalOrdinal] = new(0, 0, 0);
                inputSeen[formalOrdinal] = true;
                continue;
            }

            if (argument is BoundConstant constant)
            {
                if (schema != TrRmArtifactSchema.C1M1Construction2Draft
                    || constant.Type != targetVariable.Type
                    || constant.RepresentationType != constant.Type
                    || constant.Type is PrimitiveType.NativeInt
                        or PrimitiveType.NativeUInt
                    || !constant.Bits.IsCanonicalFor(constant.Type)
                    || !TryGetC1M1SourceScalarTypeSpecReference(
                        constant.Type, out _)
                    || constantRegisterCount >= 13
                    || targetVariable.IsOut || targetVariable.IsInit
                    || targetVariable.IsImplicitRef
                    || targetVariable.IsInitializationLocal)
                {
                    throw Unsupported(
                        "A forwarded module-call constant requires a canonical fixed-scalar actual in the exact label-free Construction-2 profile.");
                }

                var frozenValue
                    = frozenScalarValueBuilder.Add(constant);
                var expectedTypeSpecReference
                    = typeSpecReferenceFor(constant.Type);
                if (frozenValue.TypeSpecRef != expectedTypeSpecReference)
                {
                    throw Unsupported(
                        "A forwarded module-call constant must retain its exact scalar TypeSpec.");
                }

                var constantRow = constantMapRows.Count / 8;
                var constantBytes = new byte[8];
                BinaryPrimitives.WriteUInt32LittleEndian(constantBytes,
                    frozenValue.TypeSpecRef);
                BinaryPrimitives.WriteUInt32LittleEndian(
                    constantBytes.AsSpan(4),
                    frozenValue.ConstantSourceRow);
                constantMapRows.AddRange(constantBytes);

                var constantRegister = constantRegisterCount + 1;
                callCodePrefix.Add(0xD4);
                callCodePrefix.Add(checked((byte)constantRegister));
                callCodePrefix.Add(checked((byte)constantRow));
                callCodePrefix.Add(checked((byte)(constantRow >> 8)));
                callInputs[formalOrdinal] = new(1, 1,
                    checked((ushort)constantRegister));
                constantRegisterCount++;
                inputSeen[formalOrdinal] = true;
                continue;
            }

            if (argument is not BoundRead read
                || (uint)read.Variable >= (uint)function.ParameterCount
                || read.Type != function.Variables[read.Variable].Type)
            {
                throw Unsupported(
                    "A forwarded module-call Body requires direct value-formal actuals.");
            }

            var sourceVariable = function.Variables[read.Variable];
            if (sourceVariable.IsMutable || sourceVariable.IsOut
                || sourceVariable.IsInit || sourceVariable.IsImplicitRef
                || sourceVariable.IsInitializationLocal
                || targetVariable.IsOut || targetVariable.IsInit
                || targetVariable.IsImplicitRef
                || targetVariable.IsInitializationLocal
                || !AreC1M1MethodSignatureTypesEquivalent(
                    sourceVariable.Type, function, program,
                    targetVariable.Type, targetFunction))
            {
                throw Unsupported(
                    "A forwarded module-call Body actual differs from its selected value formal.");
            }

            var sourceHomeIndex = homeIndexByVariable[read.Variable];
            if (sourceHomeIndex < 0)
            {
                throw Unsupported(
                    "A forwarded non-unit module-call input has no formal Home.");
            }

            callInputs[formalOrdinal] = new(2, 1,
                checked((ushort)sourceHomeIndex));
            inputSeen[formalOrdinal] = true;
        }

        if (inputSeen.Any(static seen => !seen))
        {
            throw Unsupported(
                "A forwarded module-call Body omits a selected MethodSig input.");
        }

        var homeMap = new byte[checked(typeTokens.Count * 8)];
        for (var parameter = 0; parameter < function.ParameterCount;
            parameter++)
        {
            var homeIndex = homeIndexByVariable[parameter];
            if (homeIndex < 0)
            {
                continue;
            }

            var offset = homeIndex * 8;
            homeMap[offset] = 2;
            BinaryPrimitives.WriteUInt16LittleEndian(homeMap.AsSpan(
                offset + 4, 2), checked((ushort)parameter));
        }

        var targetMethodToken
            = 0x1600_0000u | checked((uint)targetMethodDefRow);
        var maps = TrRmRegisterBodyMapBuilder.Create(typeTokens,
            [targetMethodToken]);
        for (var homeIndex = 0; homeIndex < typeTokens.Count; homeIndex++)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(
                homeMap.AsSpan(homeIndex * 8 + 2, 2),
                checked((ushort)maps.GetTypeMapIndex(
                    typeTokens[homeIndex])));
        }

        var isUnitResult = function.ReturnType == PrimitiveType.Void;
        var callSite = TrRmRegisterBodyRecordWriter.EncodeCallSite(1,
            isUnitResult ? (byte)0 : (byte)1,
            maps.GetMethodMapIndex(targetMethodToken), 0, callInputs, [],
            schema: schema);
        ReadOnlyMemory<byte>[] records = [
            new byte[] { 1, 0, 0, 0, 0, 0, 0, 0 },
            new(callSite),
        ];
        var code = new List<byte>(callCodePrefix.Count + 6);
        code.AddRange(callCodePrefix);
        code.Add(0xE0);
        code.Add(0);
        code.Add(1);
        code.Add(0);
        code.Add(isUnitResult ? (byte)0x18 : (byte)0x17);
        code.Add(0);
        return TrRmRegisterBodyWriter.Encode(homeMap, maps.TypeMap,
            maps.MethodMap, default, constantMapRows.ToArray(), [
                new TrRmRegisterBodyBlock(0,
                    checked((uint)code.Length), 0),
            ], records, code.ToArray(), contractCount: 0, schema: schema,
            generalPredicateGraphsInitializationReferenceBodies
                : schema.UsesGeneralPredicateGraphs());
    }

    private static bool C1M1DependencyContractsEqual(
        ImmutableArray<BoundDependencyContract> left,
        ImmutableArray<BoundDependencyContract> right)
    {
        if (left.IsDefault || right.IsDefault
            || left.Length != right.Length)
        {
            return false;
        }

        for (var index = 0; index < left.Length; index++)
        {
            var leftContract = left[index];
            var rightContract = right[index];
            if (leftContract.Relation != rightContract.Relation
                || leftContract.Destination != rightContract.Destination
                || leftContract.DestinationOrdinal
                    != rightContract.DestinationOrdinal
                || leftContract.Sources.IsDefault
                || rightContract.Sources.IsDefault
                || !leftContract.Sources.AsSpan().SequenceEqual(
                    rightContract.Sources.AsSpan()))
            {
                return false;
            }
        }

        return true;
    }

    private static byte[] EncodeC1M1MetadataClassDirectCallBody(
        BoundFunction function, BoundCall call,
        BoundFunction targetFunction,
        C1M1MethodEmissionPlan targetPlan, int targetMethodDefRow,
        bool targetWritable, BoundProgram program,
        TrRmFrozenScalarValueBuilder frozenScalarValueBuilder,
        Func<PrimitiveType, uint> typeSpecReferenceFor,
        TrRmArtifactSchema schema)
    {
        var method = targetPlan.Method;
        if (schema != TrRmArtifactSchema.C1M1Construction2Draft
            || !schema.UsesObjectConstruction()
            || schema.UsesMethodReceiverAccess()
            || (function.ReturnType != PrimitiveType.Void
                && !TryGetC1M1SourceScalarTypeSpecReference(
                    function.ReturnType, out _))
            || call.Type != function.ReturnType
            || targetFunction.ReturnType != function.ReturnType
            || targetFunction.IsStatic
            || targetFunction.IsClassConstructor
            || targetFunction.IsConstructor
            || targetFunction.IsCleanup
            || targetFunction.IsOperator
            || targetFunction.Intrinsic != BoundIntrinsic.None
            || targetFunction.RequiresUnsafeCaller
            || targetFunction.DoesNotReturn
            || !targetFunction.DependencyContracts.IsEmpty
            || targetFunction.ParameterCount
                != method.Parameters.Length + 1
            || targetFunction.Variables.Length
                < targetFunction.ParameterCount
            || method.ReceiverTypeSpecReference == 0
            || method.ConstructionRole != 0
            || method.InitializerMethodDefinitionId != 0
            || method.ConstructionPropertyDefinitionId != 0
            || !method.InitializedMemberDefinitionIds.IsEmpty
            || method.GenericParameterCount != 0
            || method.EffectsMask != 0
            || !method.DependencyClauses.IsDefaultOrEmpty
            || (method.Flags & 4) != 0
            || call.Arguments.Length != targetFunction.ParameterCount
            || !call.DefaultedArguments.IsDefaultOrEmpty
            || call.InterfaceCallIdentity is not null
            || call.GenericMethodCall is not null
            || call.ImportedGenericMethodCall is not null
            || call.IsUnsafeAuthorized
            || targetMethodDefRow is <= 0 or > 0xFF_FFFF)
        {
            throw Unsupported(
                "The bounded Construction-2 class-reference Body supports only a direct unit- or scalar-returning ordinary class call with supported value inputs.");
        }

        var receiverType = targetFunction.Variables[0].Type;
        if (!BoundGenericNominalTypeTypes.IsType(receiverType)
            || !BoundGenericNominalTypeContract.IsNominalClassType(
                receiverType, program.GenericNominalTypeInstances,
                program.GenericNominalTypeDefinitions)
            || targetFunction.Variables[0].IsMutable != targetWritable)
        {
            throw Unsupported(
                "The ordinary class call target does not have the selected direct receiver view.");
        }

        var receiverInstance = program.GenericNominalTypeInstances[
            BoundGenericNominalTypeTypes.Index(receiverType)];
        if (!receiverInstance.Arguments.IsEmpty
            || (receiverInstance.Qualifiers & ~0x01) != 0
            || ((receiverInstance.Qualifiers & 0x01) != 0)
                != targetWritable)
        {
            throw Unsupported(
                "The ordinary class call target has a receiver outside the direct Construction-2 access contract.");
        }

        var typeTokens = new List<uint>(function.ParameterCount);
        var homeIndexByVariable = new int[function.ParameterCount];
        Array.Fill(homeIndexByVariable, -1);
        for (var parameter = 0; parameter < function.ParameterCount;
            parameter++)
        {
            if (function.Variables[parameter].Type == PrimitiveType.Void)
            {
                continue;
            }

            var typeToken = CreateStandardBodyTypeMapToken(
                typeSpecReferenceFor(function.Variables[parameter].Type));
            homeIndexByVariable[parameter] = typeTokens.Count;
            typeTokens.Add(typeToken);
        }

        var inputSeen = new bool[targetFunction.ParameterCount];
        var callInputs = new TrRmRegisterBodyCallInput[
            targetFunction.ParameterCount];
        var constantMapRows = new List<byte>();
        var callCodePrefix = new List<byte>();
        var constantRegisterCount = 0;
        for (var argumentIndex = 0;
            argumentIndex < call.Arguments.Length; argumentIndex++)
        {
            var formalOrdinal = call.ParameterIndex(argumentIndex);
            if ((uint)formalOrdinal >= (uint)targetFunction.ParameterCount
                || inputSeen[formalOrdinal]
                || call.ImplicitReferenceTemporary(argumentIndex) != -1)
            {
                throw Unsupported(
                    "The ordinary class call Body requires direct, distinct value-formal Homes for every non-unit MethodSig input.");
            }

            var targetVariable = targetFunction.Variables[formalOrdinal];
            if (formalOrdinal > 0
                && (method.Parameters[formalOrdinal - 1].Mode != 0
                    || method.Parameters[formalOrdinal - 1].Flags != 0
                    || method.Parameters[formalOrdinal - 1]
                        .DefaultValue is not null))
            {
                throw Unsupported(
                    "The ordinary class call Body does not encode parameter modes, flags or defaults.");
            }

            var argument = call.Arguments[argumentIndex];
            if (targetVariable.Type == PrimitiveType.Void)
            {
                var isUnitActual = argument is BoundConstant {
                    Type: PrimitiveType.Void,
                    RepresentationType: PrimitiveType.Void,
                } || argument is BoundRead unitRead
                    && (uint)unitRead.Variable
                        < (uint)function.ParameterCount
                    && unitRead.Type == PrimitiveType.Void
                    && function.Variables[unitRead.Variable].Type
                        == PrimitiveType.Void
                    && !function.Variables[unitRead.Variable].IsOut
                    && !function.Variables[unitRead.Variable].IsInit
                    && !function.Variables[unitRead.Variable].IsImplicitRef
                    && !function.Variables[unitRead.Variable]
                        .IsInitializationLocal;
                if (!isUnitActual)
                {
                    throw Unsupported(
                        "A unit class-method formal requires a direct unit value and carries no CallSite Home.");
                }

                callInputs[formalOrdinal] = new(0, 0, 0);
                inputSeen[formalOrdinal] = true;
                continue;
            }

            if (argument is BoundConstant constant)
            {
                if (constant.Type != targetVariable.Type
                    || constant.RepresentationType != constant.Type
                    || constant.Type is PrimitiveType.NativeInt
                        or PrimitiveType.NativeUInt
                    || !constant.Bits.IsCanonicalFor(constant.Type)
                    || !TryGetC1M1SourceScalarTypeSpecReference(
                        constant.Type, out _)
                    || constantRegisterCount >= 13)
                {
                    throw Unsupported(
                        "A direct class-method constant input requires a canonical fixed-scalar value and an available temporary register.");
                }

                var frozenValue
                    = frozenScalarValueBuilder.Add(constant);
                var expectedTypeSpecReference
                    = typeSpecReferenceFor(constant.Type);
                if (frozenValue.TypeSpecRef != expectedTypeSpecReference)
                {
                    throw Unsupported(
                        "A direct class-method constant input must retain its exact formal TypeSpec.");
                }

                var constantRow = constantMapRows.Count / 8;
                var constantBytes = new byte[8];
                BinaryPrimitives.WriteUInt32LittleEndian(constantBytes,
                    frozenValue.TypeSpecRef);
                BinaryPrimitives.WriteUInt32LittleEndian(
                    constantBytes.AsSpan(4),
                    frozenValue.ConstantSourceRow);
                constantMapRows.AddRange(constantBytes);

                var constantRegister = constantRegisterCount + 1;
                callCodePrefix.Add(0xD4);
                callCodePrefix.Add(checked((byte)constantRegister));
                callCodePrefix.Add(checked((byte)constantRow));
                callCodePrefix.Add(checked((byte)(constantRow >> 8)));
                callInputs[formalOrdinal] = new(1, 1,
                    checked((ushort)constantRegister));
                constantRegisterCount++;
                inputSeen[formalOrdinal] = true;
                continue;
            }

            if (argument is not BoundRead read
                || (uint)read.Variable >= (uint)function.ParameterCount
                || read.Type != function.Variables[read.Variable].Type)
            {
                throw Unsupported(
                    "The ordinary class call Body requires direct, distinct value-formal Homes for every non-unit MethodSig input.");
            }

            var sourceVariable = function.Variables[read.Variable];
            if (sourceVariable.IsMutable || sourceVariable.IsOut
                || sourceVariable.IsInit || sourceVariable.IsImplicitRef
                || sourceVariable.IsInitializationLocal
                || targetVariable.IsOut || targetVariable.IsInit
                || targetVariable.IsImplicitRef
                || targetVariable.IsInitializationLocal)
            {
                throw Unsupported(
                    "The ordinary class call Body does not encode mutable, output, initialization or implicit-reference formals.");
            }

            if (formalOrdinal == 0)
            {
                if (!BoundGenericNominalTypeTypes.IsType(
                        sourceVariable.Type)
                    || !BoundGenericNominalTypeContract.IsNominalClassType(
                        sourceVariable.Type,
                        program.GenericNominalTypeInstances,
                        program.GenericNominalTypeDefinitions))
                {
                    throw Unsupported(
                        "The ordinary class receiver actual must be a selected normal class handle.");
                }

                var sourceReceiver = program.GenericNominalTypeInstances[
                    BoundGenericNominalTypeTypes.Index(
                        sourceVariable.Type)];
                if (sourceReceiver.Definition != receiverInstance.Definition
                    || !sourceReceiver.Arguments.IsEmpty
                    || (targetWritable
                        && (sourceReceiver.Qualifiers & 0x01) == 0))
                {
                    throw Unsupported(
                        "The ordinary class receiver actual does not preserve the target owner and required access.");
                }
            }
            else if (!AreC1M1MethodSignatureTypesEquivalent(
                         sourceVariable.Type, function, program,
                         targetVariable.Type, targetFunction))
            {
                throw Unsupported(
                    "An ordinary class call value actual differs from its selected MethodSig formal.");
            }

            var sourceHomeIndex = homeIndexByVariable[read.Variable];
            if (sourceHomeIndex < 0)
            {
                throw Unsupported(
                    "A non-unit class call input has no materialized formal Home.");
            }

            callInputs[formalOrdinal] = new(2,
                formalOrdinal == 0
                    ? targetWritable ? (byte)4 : (byte)3
                    : (byte)1,
                checked((ushort)sourceHomeIndex));
            inputSeen[formalOrdinal] = true;
        }

        if (inputSeen.Any(static seen => !seen))
        {
            throw Unsupported(
                "The ordinary class call Body omits its receiver or a selected MethodSig formal.");
        }

        var homeMap = new byte[checked(typeTokens.Count * 8)];
        for (var parameter = 0; parameter < function.ParameterCount;
            parameter++)
        {
            var homeIndex = homeIndexByVariable[parameter];
            if (homeIndex < 0)
            {
                continue;
            }

            var offset = homeIndex * 8;
            homeMap[offset] = 2;
            BinaryPrimitives.WriteUInt16LittleEndian(homeMap.AsSpan(
                offset + 4, 2), checked((ushort)parameter));
        }

        var targetMethodToken
            = 0x1600_0000u | checked((uint)targetMethodDefRow);
        var maps = TrRmRegisterBodyMapBuilder.Create(typeTokens,
            [targetMethodToken]);
        for (var homeIndex = 0; homeIndex < typeTokens.Count; homeIndex++)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(
                homeMap.AsSpan(homeIndex * 8 + 2, 2),
                checked((ushort)maps.GetTypeMapIndex(
                    typeTokens[homeIndex])));
        }

        var isUnitResult = function.ReturnType == PrimitiveType.Void;
        var callSite = TrRmRegisterBodyRecordWriter.EncodeCallSite(1,
            isUnitResult ? (byte)0 : (byte)1,
            maps.GetMethodMapIndex(targetMethodToken), 0, callInputs, [],
            schema: schema);
        ReadOnlyMemory<byte>[] records = [
            new byte[] { 1, 0, 0, 0, 0, 0, 0, 0 },
            new(callSite),
        ];
        var code = new List<byte>(callCodePrefix.Count + 6);
        code.AddRange(callCodePrefix);
        code.Add(0xE0);
        code.Add(0);
        code.Add(1);
        code.Add(0);
        code.Add(isUnitResult ? (byte)0x18 : (byte)0x17);
        code.Add(0);
        return TrRmRegisterBodyWriter.Encode(homeMap, maps.TypeMap,
            maps.MethodMap, default, constantMapRows.ToArray(),
            [new TrRmRegisterBodyBlock(0,
                checked((uint)code.Count), 0)],
            records, code.ToArray(), contractCount: 0, schema: schema,
            generalPredicateGraphsInitializationReferenceBodies
                : schema.UsesGeneralPredicateGraphs());
    }

    private static BoundProgram
        CreateC1M1AssociatedRelationReemissionProgram(
            BoundProgram program,
            TrRmStructuralMetadataValidator.ValidatedExecutionTables
                rootTables,
            int rootModuleRow,
            BoundSourceTypeProviderSnapshot rootSnapshot,
            IReadOnlyList<C1M1GenericConstraintPlan> constraintPlans)
    {
        var importsByInterface = new Dictionary<(string Name, int Arity),
            BoundModuleImport>();
        var sourceRoots = program.RetainedGenericMethodRoots
            .Select(static root => root.Binding?.Expected ?? root)
            .ToArray();

        foreach (var plan in constraintPlans)
        {
            if (plan.Kind != 3)
            {
                continue;
            }

            var root = FindC1M1ClassMethodImport(sourceRoots,
                plan.MethodRow);
            var relations = root.Formals[plan.FormalOrdinal]
                .AssociatedRelations;
            var relation = relations.SingleOrDefault(candidate =>
                candidate.AssociatedRelationRow
                    == checked((int)plan.Payload))
                ?? throw Unsupported(
                    "A retained CM1AR1 class Constraint has no typed associated relation.");
            _ = GetC1M1AssociatedInterfaceModuleImport(
                relation.AssociatedType.DeclaringInterface, rootTables,
                rootModuleRow, rootSnapshot, importsByInterface);
        }

        foreach (var root in sourceRoots)
        {
            foreach (var formal in root.Formals)
            {
                foreach (var constraint in formal.InterfaceConstraints)
                {
                    if (constraint.Kind == BoundInterfaceTypeKind.Nominal
                        && constraint.ModuleImport is null)
                    {
                        _ = GetC1M1AssociatedInterfaceModuleImport(
                            constraint, rootTables, rootModuleRow,
                            rootSnapshot, importsByInterface);
                    }
                }
            }
        }

        var roots
            = ImmutableArray.CreateBuilder<BoundGenericMethodImport>(
                sourceRoots.Length);
        foreach (var root in sourceRoots)
        {
            roots.Add(RewriteC1M1AssociatedMethodImport(root, rootTables,
                rootModuleRow, rootSnapshot, importsByInterface));
        }

        var interfaces
            = ImmutableArray.CreateBuilder<BoundInterfaceDefinition>(
                program.Interfaces.Length);
        foreach (var definition in program.Interfaces)
        {
            var key = (definition.QualifiedName,
                definition.Formals.Length);
            interfaces.Add(definition.ModuleImport is null
                && importsByInterface.TryGetValue(key, out var import)
                ? definition with { ModuleImport = import }
                : definition);
        }
        return program with {
            Interfaces = interfaces.MoveToImmutable(),
            RetainedGenericMethodRoots = roots.MoveToImmutable(),
        };
    }

    private static BoundGenericMethodImport FindC1M1ClassMethodImport(
        IReadOnlyList<BoundGenericMethodImport> roots, int methodRow)
    {
        var methodDefId = 0x1600_0000u | checked((uint)methodRow);
        BoundGenericMethodImport? match = null;
        foreach (var root in roots)
        {
            if (root.Origin.MethodDefId != methodDefId)
            {
                continue;
            }

            if (match is not null)
            {
                throw Unsupported(
                    "A retained C1M1 MethodDef has multiple typed import roots.");
            }

            match = root;
        }

        return match ?? throw Unsupported(
            "A retained C1M1 MethodDef has no typed import root.");
    }

    private static bool AreC1M1MethodSignatureTypesEquivalent(
        PrimitiveType functionType, BoundFunction function,
        BoundProgram program, PrimitiveType proofType,
        BoundFunction proofFunction)
    {
        var proofProgram = program with {
            Functions = [proofFunction],
        };
        return SemanticVerifier.TryGetGenericMethodActualTypeTerm(
                functionType, function, program, 0,
                out var functionTerm)
            && SemanticVerifier.TryGetGenericMethodActualTypeTerm(
                proofType, proofFunction, proofProgram, 0,
                out var proofTerm)
            && SemanticVerifier.AreGenericMethodTypeTermsEqual(
                functionTerm, proofTerm, 0);
    }

    private static BoundModuleImport GetC1M1AssociatedInterfaceModuleImport(
        BoundInterfaceType type,
        TrRmStructuralMetadataValidator.ValidatedExecutionTables rootTables,
        int rootModuleRow,
        BoundSourceTypeProviderSnapshot rootSnapshot,
        Dictionary<(string Name, int Arity), BoundModuleImport>
            importsByInterface)
    {
        if (type.Kind != BoundInterfaceTypeKind.Nominal
            || string.IsNullOrEmpty(type.NominalName))
        {
            throw Unsupported(
                "A retained CM1AR1 interface relation has no nominal declaring interface.");
        }
        if (type.ModuleImport is BoundModuleImport existingImport)
        {
            return existingImport;
        }

        var arity = type.TypeArguments.IsDefault
            ? 0 : type.TypeArguments.Length;
        if (importsByInterface.TryGetValue((type.NominalName, arity),
                out var cachedImport))
        {
            return cachedImport;
        }

        var typeDefRow = 0;
        for (var candidate = 1; candidate <= rootTables.TypeDefs.Count;
            candidate++)
        {
            if (rootTables.TypeDefs.GetOwnerModule(candidate)
                    != rootModuleRow
                || rootTables.TypeDefs.GetKind(candidate) != 3
                || !StringComparer.Ordinal.Equals(
                    GetC1M1TypeDefQualifiedName(rootTables.TypeDefs,
                        candidate), type.NominalName)
                || rootTables.TypeDefs.GetGenericRange(candidate).Count
                    != arity)
            {
                continue;
            }

            if (typeDefRow != 0)
            {
                throw Unsupported(
                    "A retained CM1AR1 local interface has multiple root TypeDefs.");
            }

            typeDefRow = candidate;
        }

        if (typeDefRow == 0)
        {
            throw Unsupported(
                "A retained CM1AR1 local interface has no root TypeDef.");
        }

        var typeDefId = 0x1300_0000u | checked((uint)typeDefRow);
        var exportKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var export in rootTables.Exports.GetExportsForModule(
                     rootModuleRow))
        {
            if (export.DefinitionId == typeDefId)
            {
                _ = exportKeys.Add(export.ExportKey);
            }
        }
        if (exportKeys.Count != 1)
        {
            throw Unsupported(
                "A retained CM1AR1 local interface requires one unambiguous owner ExportKey.");
        }

        var import = new BoundModuleImport(rootSnapshot.ModuleIdentity,
            exportKeys.Single(), rootSnapshot.TargetRequirement,
            rootSnapshot.RuntimePolicy);
        importsByInterface.Add((type.NominalName, arity), import);
        return import;
    }

    private static BoundInterfaceType RewriteC1M1AssociatedInterfaceType(
        BoundInterfaceType type,
        TrRmStructuralMetadataValidator.ValidatedExecutionTables rootTables,
        int rootModuleRow,
        BoundSourceTypeProviderSnapshot rootSnapshot,
        Dictionary<(string Name, int Arity), BoundModuleImport>
            importsByInterface)
    {
        var arguments
            = ImmutableArray.CreateBuilder<BoundInterfaceTypeArgument>(
                type.TypeArguments.IsDefault ? 0 : type.TypeArguments.Length);
        if (!type.TypeArguments.IsDefault)
        {
            foreach (var argument in type.TypeArguments)
            {
                arguments.Add(argument with {
                    Type = argument.Type is null ? null
                        : RewriteC1M1AssociatedInterfaceType(argument.Type,
                            rootTables, rootModuleRow, rootSnapshot,
                            importsByInterface),
                    ConstantType = argument.ConstantType is null ? null
                        : RewriteC1M1AssociatedInterfaceType(
                            argument.ConstantType, rootTables,
                            rootModuleRow, rootSnapshot,
                            importsByInterface),
                });
            }
        }

        var rewritten = type with {
            TypeArguments = type.TypeArguments.IsDefault
                ? type.TypeArguments : arguments.MoveToImmutable(),
        };
        if (type.Kind == BoundInterfaceTypeKind.Nominal
            && type.ModuleImport is null)
        {
            rewritten = rewritten with {
                ModuleImport =
                    GetC1M1AssociatedInterfaceModuleImport(rewritten,
                        rootTables, rootModuleRow, rootSnapshot,
                        importsByInterface),
            };
        }

        return rewritten;
    }

    private static ImmutableArray<BoundGenericMethodFormal>
        RewriteC1M1AssociatedFormals(
            ImmutableArray<BoundGenericMethodFormal> formals,
            TrRmStructuralMetadataValidator.ValidatedExecutionTables
                rootTables,
            int rootModuleRow,
            BoundSourceTypeProviderSnapshot rootSnapshot,
            Dictionary<(string Name, int Arity), BoundModuleImport>
                importsByInterface)
    {
        if (formals.IsDefault)
        {
            return formals;
        }

        var result
            = ImmutableArray.CreateBuilder<BoundGenericMethodFormal>(
                formals.Length);
        foreach (var formal in formals)
        {
            var constraints = formal.InterfaceConstraints;
            if (!constraints.IsDefault)
            {
                var rewrittenConstraints
                    = ImmutableArray.CreateBuilder<BoundInterfaceType>(
                        constraints.Length);
                foreach (var constraint in constraints)
                {
                    rewrittenConstraints.Add(
                        RewriteC1M1AssociatedInterfaceType(constraint,
                            rootTables, rootModuleRow, rootSnapshot,
                            importsByInterface));
                }

                constraints = rewrittenConstraints.MoveToImmutable();
            }

            var relations = formal.AssociatedRelations;
            if (!relations.IsDefault)
            {
                var rewrittenRelations
                    = ImmutableArray.CreateBuilder<
                        BoundGenericMethodAssociatedRelation>(
                            relations.Length);
                foreach (var relation in relations)
                {
                    rewrittenRelations.Add(relation with {
                        AssociatedType
                            = relation.AssociatedType with {
                                DeclaringInterface
                                    = RewriteC1M1AssociatedInterfaceType(
                                        relation.AssociatedType
                                            .DeclaringInterface, rootTables,
                                        rootModuleRow, rootSnapshot,
                                        importsByInterface),
                            },
                    });
                }

                relations = rewrittenRelations.MoveToImmutable();
            }

            result.Add(formal with {
                InterfaceConstraints = constraints,
                AssociatedRelations = relations,
            });
        }

        return result.MoveToImmutable();
    }

    private static BoundGenericMethodImport
        RewriteC1M1AssociatedMethodImport(
            BoundGenericMethodImport source,
            TrRmStructuralMetadataValidator.ValidatedExecutionTables
                rootTables,
            int rootModuleRow,
            BoundSourceTypeProviderSnapshot rootSnapshot,
            Dictionary<(string Name, int Arity), BoundModuleImport>
                importsByInterface)
    {
        var rewritten = new BoundGenericMethodImport(source.Origin,
            source.Signature,
            RewriteC1M1AssociatedFormals(source.Formals, rootTables,
                rootModuleRow, rootSnapshot, importsByInterface),
            source.ProofFunction, source.FormalTypeOwnerOrdinal,
            source.SpanTypes, source.OrdinaryParameterDefaults);
        rewritten.CompleteProofFunction(source.ProofFunction,
            source.ProofSupportFunctions,
            source.FormalTypeOwnerOrdinal);
        return rewritten;
    }

    private static void MapC1M1ReemittedInterfaceType(
        BoundInterfaceDefinition definition,
        IReadOnlyDictionary<
            (string ProviderIdentity, string ExportKey, string QualifiedName,
                int Arity, uint TargetRequirement, uint RuntimePolicy), int>
            interfaceImportRowsByOwner,
        TrRmStructuralMetadataValidator.ValidatedExecutionTables rootTables,
        TrRmStructuralMetadataValidator.ValidatedExecutionTables outputTables,
        int rootModuleRow, Dictionary<int, int> typeDefRowMap,
        Dictionary<int, int> importRowMap)
    {
        if (definition.ModuleImport is not BoundModuleImport import)
        {
            return;
        }

        var importKey = (import.ProducerIdentity, import.ExportKey,
            definition.QualifiedName, definition.Formals.Length,
            import.TargetRequirement, import.RuntimePolicy);
        if (!interfaceImportRowsByOwner.TryGetValue(importKey,
                out var outputImportRow))
        {
            return;
        }

        var outputDefinitionId = outputTables.Imports
            .GetExpectedDefinitionForImportRow(outputImportRow);
        if ((outputDefinitionId >> 24) != 0x13)
        {
            throw new InvalidDataException(
                "A re-emitted interface owner Import does not expect a TypeDef.");
        }

        var outputTypeDefRow = checked((int)(
            outputDefinitionId & 0x00FF_FFFF));
        var oldTypeDefRows = new List<int>();
        for (var typeDefRow = 1; typeDefRow <= rootTables.TypeDefs.Count;
            typeDefRow++)
        {
            if (rootTables.TypeDefs.GetKind(typeDefRow) == 3
                && StringComparer.Ordinal.Equals(
                    GetC1M1TypeDefQualifiedName(rootTables.TypeDefs,
                        typeDefRow), definition.QualifiedName)
                && rootTables.TypeDefs.GetGenericRange(typeDefRow).Count
                    == definition.Formals.Length)
            {
                oldTypeDefRows.Add(typeDefRow);
            }
        }

        foreach (var oldTypeDefRow in oldTypeDefRows)
        {
            if (rootTables.TypeDefs.GetOwnerModule(oldTypeDefRow)
                    == rootModuleRow
                && !rootTables.Imports.IsExpectedDefinition(
                    0x1300_0000u | checked((uint)oldTypeDefRow)))
            {
                if (typeDefRowMap.TryGetValue(oldTypeDefRow,
                        out var existingTypeDefRow)
                    && existingTypeDefRow != outputTypeDefRow)
                {
                    throw new InvalidDataException(
                        "A retained CM1AR1 interface TypeDef has conflicting remapped owners.");
                }

                _ = typeDefRowMap.TryAdd(oldTypeDefRow,
                    outputTypeDefRow);
            }
        }

        for (var oldImportRow = 1;
            oldImportRow <= rootTables.Imports.Count;
            oldImportRow++)
        {
            var expectedDefinitionId = rootTables.Imports
                .GetExpectedDefinitionForImportRow(oldImportRow);
            if ((expectedDefinitionId >> 24) != 0x13)
            {
                continue;
            }

            var oldTypeDefRow = checked((int)(
                expectedDefinitionId & 0x00FF_FFFF));
            if (!oldTypeDefRows.Contains(oldTypeDefRow))
            {
                continue;
            }

            var oldModuleRefRow = rootTables.Imports
                .GetModuleRefForImportRow(oldImportRow);
            var oldProviderIdentity = rootTables.ModuleRefs
                .GetProducerIdentity(oldModuleRefRow);
            var oldExportKey = rootTables.Imports
                .GetBindingForImportRow(oldImportRow).ExportKey;
            if (!StringComparer.Ordinal.Equals(oldProviderIdentity,
                    import.ProducerIdentity)
                || !StringComparer.Ordinal.Equals(oldExportKey,
                    import.ExportKey))
            {
                continue;
            }

            if (importRowMap.TryGetValue(oldImportRow,
                    out var existingImportRow)
                && existingImportRow != outputImportRow)
            {
                throw new InvalidDataException(
                    "A retained CM1AR1 interface Import has conflicting remapped owners.");
            }

            _ = importRowMap.TryAdd(oldImportRow, outputImportRow);
            if (typeDefRowMap.TryGetValue(oldTypeDefRow,
                    out var priorTypeDefRow)
                && priorTypeDefRow != outputTypeDefRow)
            {
                throw new InvalidDataException(
                    "A retained CM1AR1 interface TypeDef has conflicting remapped owners.");
            }

            _ = typeDefRowMap.TryAdd(oldTypeDefRow, outputTypeDefRow);
        }
    }

    private static bool IsC1M1RetainedInterfaceImport(
        BoundProgram program,
        TrRmStructuralMetadataValidator.ValidatedExecutionTables tables,
        int importRow)
    {
        var expectedDefinitionId = tables.Imports
            .GetExpectedDefinitionForImportRow(importRow);
        var expectedKind = expectedDefinitionId >> 24;
        var expectedRow = checked((int)(
            expectedDefinitionId & 0x00FF_FFFF));
        var moduleRefRow = tables.Imports
            .GetModuleRefForImportRow(importRow);
        var providerIdentity = tables.ModuleRefs
            .GetProducerIdentity(moduleRefRow);
        var requirements = tables.ModuleRefs
            .GetRequirements(moduleRefRow);
        var exportKey = tables.Imports
            .GetBindingForImportRow(importRow).ExportKey;

        if (expectedKind == 0x13)
        {
            if (tables.TypeDefs.GetKind(expectedRow) != 3)
            {
                return false;
            }

            var qualifiedName = GetC1M1TypeDefQualifiedName(
                tables.TypeDefs, expectedRow);
            var arity = tables.TypeDefs.GetGenericRange(expectedRow).Count;
            return program.Interfaces.Any(definition =>
                definition.ModuleImport is BoundModuleImport ownerImport
                && StringComparer.Ordinal.Equals(
                    ownerImport.ProducerIdentity, providerIdentity)
                && StringComparer.Ordinal.Equals(ownerImport.ExportKey,
                    exportKey)
                && ownerImport.TargetRequirement
                    == requirements.TargetRequirement
                && ownerImport.RuntimePolicy == requirements.RuntimePolicy
                && StringComparer.Ordinal.Equals(definition.QualifiedName,
                    qualifiedName)
                && definition.Formals.Length == arity);
        }

        var associatedTypeImport = expectedKind == 0x20;
        var interfaceMethodImport = expectedKind == 0x16;
        if (!associatedTypeImport && !interfaceMethodImport)
        {
            return false;
        }

        var ownerTypeDefRow = associatedTypeImport
            ? tables.Interfaces.GetOwnerTypeDef(
                tables.AssociatedTypeDefs.GetOwnerInterfaceDef(expectedRow))
            : tables.Methods.GetOwnerType(expectedRow);
        if (tables.TypeDefs.GetKind(ownerTypeDefRow) != 3)
        {
            return false;
        }

        var qualifiedOwnerName = GetC1M1TypeDefQualifiedName(
            tables.TypeDefs, ownerTypeDefRow);
        var ownerArity
            = tables.TypeDefs.GetGenericRange(ownerTypeDefRow).Count;
        var memberName = associatedTypeImport
            ? tables.Strings.DecodeName(
                tables.AssociatedTypeDefs.GetNameStringRow(expectedRow))
            : tables.Methods.GetName(expectedRow);
        return program.Interfaces.Any(definition =>
            definition.ModuleImport is BoundModuleImport ownerImport
            && StringComparer.Ordinal.Equals(
                ownerImport.ProducerIdentity, providerIdentity)
            && ownerImport.TargetRequirement
                == requirements.TargetRequirement
            && ownerImport.RuntimePolicy == requirements.RuntimePolicy
            && StringComparer.Ordinal.Equals(definition.QualifiedName,
                qualifiedOwnerName)
            && definition.Formals.Length == ownerArity
            && definition.Members.Any(member =>
                member.Kind == (associatedTypeImport
                    ? BoundInterfaceMemberKind.AssociatedType
                    : BoundInterfaceMemberKind.Callable)
                && StringComparer.Ordinal.Equals(member.Name, memberName)
                && (StringComparer.Ordinal.Equals(
                        member.DeclarationKey, exportKey)
                    || member.AdditionalDeclarationKeys.Contains(
                        exportKey, StringComparer.Ordinal))));
    }

    private static C1M1GenericMethodTypeEmissionContext
        CreateC1M1GenericMethodTypeEmissionContext(
            BoundProgram program, BoundGenericMethodImport root,
            int methodRow,
            TrRmStructuralMetadataValidator.ValidatedExecutionTables rootTables,
            Dictionary<int, int> genericParamRowMap,
            List<ReadOnlyMemory<byte>> typeSpecRows)
    {
        var methodSignature = rootTables.Methods.GetSignature(methodRow);
        var (oldFormalFirst, oldFormalCount)
            = rootTables.Signatures.GetGenericRange(methodSignature);
        if (oldFormalCount != root.Formals.Length
            || root.Signature.FormalOrder.IsDefault
            || root.Signature.FormalOrder.Length != root.Formals.Length
            || root.Signature.FormalOrder.Any(static formal =>
                formal.Kind != BoundGenericMethodFormalKind.Type)
            || root.Formals.Any(static formal =>
                formal is null || formal.InterfaceConstraints.IsDefault
                        || formal.AssociatedRelations.IsDefault))
        {
            throw Unsupported(
                "A retained C1M1 class MethodDef has an unsupported generic type-formal view.");
        }

        var formalTypeSpecRows = new Dictionary<PrimitiveType, uint>();
        var genericParamRows = new int[root.Formals.Length];
        for (var ordinal = 0; ordinal < root.Formals.Length; ordinal++)
        {
            var oldFormalRow = oldFormalFirst + ordinal;
            if (!genericParamRowMap.TryGetValue(oldFormalRow,
                        out var genericParamRow))
            {
                throw new InvalidDataException(
                        "A retained C1M1 method formal has no output GenericParam row.");
            }

            genericParamRows[ordinal] = genericParamRow;
            var formalTypeSpecRow = checked((uint)typeSpecRows.Count + 1);
            typeSpecRows.Add(TrRmTypeSpecTableWriter.Reference(0x04,
                checked((uint)genericParamRow)));
            if (!formalTypeSpecRows.TryAdd(
                        root.Formals[ordinal].PlaceholderType,
                        formalTypeSpecRow))
            {
                throw Unsupported(
                        "A retained C1M1 class MethodDef has duplicate placeholder type identities.");
            }
        }

        var definition = new BoundGenericMethodDefinition(
            root.Signature.DefinitionOrdinal, root.Signature.Name,
            root.Signature.IsPublic, root.Signature.RequiresUnsafeCaller,
            0, -1, default, root.Formals,
            program with { Functions = [root.ProofFunction] }) {
            FormalTypeOwnerOrdinal = root.FormalTypeOwnerOrdinal,
            IsStatic = root.Signature.IsStatic,
            IsMutable = root.Signature.IsMutable,
            Owner = root.Signature.Owner,
            FormalOrder = root.Signature.FormalOrder,
            OrdinaryParameterDefaults = root.OrdinaryParameterDefaults,
            RuntimeParameterCount = root.ProofFunction.ParameterCount,
        };
        return new C1M1GenericMethodTypeEmissionContext(root, definition,
            formalTypeSpecRows, genericParamRows,
            GetBuiltInTypeSpecRows(typeSpecRows));
    }

    private static byte[] EncodeC1M1GenericParamRows(
        IReadOnlyList<TrRmGenericParamRow> rows)
    {
        var bytes = new byte[checked(rows.Count * 32)];
        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            var offset = index * 32;
            Write(bytes, offset, row.OwnerDefId);
            Write(bytes, offset + 4, checked((uint)row.Ordinal));
            Write(bytes, offset + 8,
                checked((uint)row.NameStringRow));
            Write(bytes, offset + 12,
                checked((uint)row.ConstraintFirst));
            Write(bytes, offset + 16,
                checked((uint)row.ConstraintCount));
            bytes[offset + 20] = row.FormalKind;
            bytes[offset + 21] = row.OwnershipDomain;
            bytes[offset + 22] = row.NullabilityDomain;
            Write(bytes, offset + 24, row.ConstantTypeSpecRef);
            Write(bytes, offset + 28, row.DefaultArgumentRef);
        }

        return bytes;
    }

    private static byte[] EncodeC1M1ConstraintRows(
        IReadOnlyList<TrRmConstraintRow> rows)
    {
        var bytes = new byte[checked(rows.Count * 12)];
        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            var offset = index * 12;
            Write(bytes, offset,
                checked((uint)row.OwnerGenericParamRow));
            BinaryPrimitives.WriteUInt16LittleEndian(
                bytes.AsSpan(offset + 4), row.Kind);
            Write(bytes, offset + 8, row.Payload);
        }

        return bytes;
    }

    private static byte[] CompleteC1M1NominalTypeEmission(
        List<TrRmSection> sections,
        List<(string Text, int Encoding)> names,
        List<ReadOnlyMemory<byte>> typeSpecRows,
        List<TrRmGenericParamRow> genericParamRows,
        List<TrRmConstraintRow> constraintRows,
        GenericMethodNominalTypeWriter nominalTypes,
        TrRmArtifactSchema schema, string standardSchemaIdentity)
    {
        nominalTypes.Complete();
        var features = ReadFeatureRows(sections);
        nominalTypes.MergeFeatures(features);
        var featureBytes = TrRmFeatureTableWriter.Encode(features, schema);
        var featureCount = featureBytes.Length / 8;
        var moduleSection = GetSection(sections, 0x0001);
        var moduleBytes = moduleSection.Bytes.ToArray();
        if (moduleSection.RecordCount != 1)
        {
            throw Unsupported(
                "C1M1 nominal TypeDef closure requires one output Module.");
        }

        var oldFeatureFirst = ReadUInt32(moduleBytes, 24);
        if (featureCount == 0)
        {
            WriteUInt32(moduleBytes, 24, 0);
        }
        else if (oldFeatureFirst == 0)
        {
            WriteUInt32(moduleBytes, 24, 1);
        }
        WriteUInt32(moduleBytes, 28, checked((uint)featureCount));
        SetSection(sections, 0x0001, 1, moduleBytes);
        SetSection(sections, 0x0002, checked((uint)names.Count),
            TrRmStringTableWriter.EncodeTextRows(names));
        SetSection(sections, 0x0004, checked((uint)featureCount),
            featureBytes);

        var exports = ReadExportRows(sections, new HashSet<uint>());
        exports.AddRange(nominalTypes.Exports);
        SetSection(sections, 0x001D, checked((uint)exports.Count),
            EncodeExportRows(exports));
        SetSection(sections, 0x0019,
            checked((uint)genericParamRows.Count),
            EncodeC1M1GenericParamRows(genericParamRows));
        SetSection(sections, 0x001A,
            checked((uint)constraintRows.Count),
            EncodeC1M1ConstraintRows(constraintRows));

        var typeSpecBytes = TrRmTypeSpecTableWriter.Encode(typeSpecRows,
            GetCount(sections, 0x0013), GetCount(sections, 0x001C),
            genericParamRows.Count, GetCount(sections, 0x0052), schema,
            GetCount(sections, 0x0016));
        SetSection(sections, 0x0011,
            checked((uint)typeSpecRows.Count), typeSpecBytes);
        sections.Sort(static (left, right) =>
            left.Kind.CompareTo(right.Kind));
        return TrRmArtifactWriter.Encode(sections, schema,
            standardSchemaIdentity: standardSchemaIdentity);
    }

    private static byte[] ReemitC1M1ClassAssociatedRelationConstraints(
        byte[] artifact, BoundProgram program,
        TrRmStructuralMetadataValidator.ValidatedExecutionTables rootTables,
        int rootModuleRow,
        BoundSourceTypeProviderSnapshot rootSnapshot,
        TrRmArtifactSchema schema,
        IReadOnlyList<C1M1GenericConstraintPlan> constraintPlans,
        int classGenericParamCount,
        Dictionary<int, int> genericParamRowMap,
        Dictionary<int, int> typeDefRowMap,
        Dictionary<int, int> importRowMap,
        IReadOnlyList<C1M1GenericMethodTypeSpecPlan>
            genericMethodTypeSpecPlans,
        IReadOnlyList<C1M1GenericMethodDefaultTypeSpecPlan>
            genericMethodDefaultTypeSpecPlans)
    {
        var reemissionProgram = constraintPlans.Count == 0 ? program
            : CreateC1M1AssociatedRelationReemissionProgram(program,
                rootTables, rootModuleRow, rootSnapshot, constraintPlans);
        var importedAssociatedTypeImportRows
            = new Dictionary<GenericImportedInterfaceAssociatedTypeKey,
                uint>();
        if (constraintPlans.Count != 0)
        {
            artifact = AddImportedGenericInterfaceMetadata(artifact,
                reemissionProgram, schema, null,
                out importedAssociatedTypeImportRows,
                standardSchemaIdentity: rootSnapshot.SchemaIdentity);
        }

        var outputIndex = new TrRmArtifactIndex(artifact, schema,
            standardSchemaIdentity: rootSnapshot.SchemaIdentity);
        var outputTables = TrRmStructuralMetadataValidator
            .ValidateC1AndGetExecutionTables(outputIndex);
        var outputSections = new SortedDictionary<ushort, TrRmSection>();
        for (var sectionIndex = 0;
            sectionIndex < outputIndex.SectionCount; sectionIndex++)
        {
            outputIndex.GetSection(sectionIndex, out var kind, out var flags,
                out var bytes, out var count);
            outputSections.Add(kind, new TrRmSection(kind, flags,
                checked((uint)count), bytes.ToArray()));
        }

        var stringSection = outputSections[0x0002];
        var strings = new TrRmStringTableReader(stringSection.Bytes.Span,
            checked((int)stringSection.RecordCount));
        var importedInterfaceImportRows
            = ReadImportedInterfaceImportRows(
                outputSections.Values.ToArray(), strings);
        var interfaceImportRowsByOwner = new Dictionary<
            (string ProviderIdentity, string ExportKey, string QualifiedName,
                int Arity, uint TargetRequirement, uint RuntimePolicy), int>();
        foreach (var (key, importRow) in importedInterfaceImportRows)
        {
            interfaceImportRowsByOwner.Add(key,
                checked((int)importRow));
        }

        if (constraintPlans.Count != 0)
        {
            foreach (var definition in reemissionProgram.Interfaces)
            {
                MapC1M1ReemittedInterfaceType(definition,
                    interfaceImportRowsByOwner, rootTables, outputTables,
                    rootModuleRow, typeDefRowMap, importRowMap);
            }
        }

        var outputTypeSpecRows
            = new List<ReadOnlyMemory<byte>>(outputTables.TypeSpecs.Count
                + constraintPlans.Count * 2
                + genericMethodTypeSpecPlans.Count
                + genericMethodDefaultTypeSpecPlans.Count);
        for (var row = 1; row <= outputTables.TypeSpecs.Count; row++)
        {
            outputTypeSpecRows.Add(
                outputTables.TypeSpecs.GetRow(row).ToArray());
        }

        var nominalSections = outputSections.Values.ToList();
        var nominalNames = new List<(string Text, int Encoding)>(
            strings.Count + 8);
        for (var row = 1; row <= strings.Count; row++)
        {
            nominalNames.Add((strings.DecodeNamespace(row), 1));
        }

        var nominalFormals = ReadGenericParamRows(nominalSections);
        var nominalConstraints = ReadConstraintRows(nominalSections);
        var nominalTypeWriter = new GenericMethodNominalTypeWriter(
            nominalSections, nominalNames, outputTypeSpecRows,
            nominalFormals, nominalConstraints, reemissionProgram,
            Array.Empty<GenericMethodEmission>(), schema);
        var nominalTypeReferences
            = new Dictionary<BoundGenericNominalTypeIdentity, uint>();
        var nominalTypeSnapshots
            = new List<BoundSourceTypeProviderSnapshot>();
        foreach (var definition
            in reemissionProgram.GenericNominalTypeDefinitions)
        {
            if (definition.Provider is not BoundSourceTypeProviderSnapshot
                    provider)
            {
                continue;
            }

            var nominalReference = 0u;
            foreach (var expectation in definition.ProviderExpectations)
            {
                if (!SourceTypeSnapshotsMatch(expectation.UsingModule,
                        rootSnapshot)
                    || !importRowMap.TryGetValue(expectation.ImportRow,
                        out var outputImportRow))
                {
                    continue;
                }

                var typeSpecReference
                    = checked((uint)outputTypeSpecRows.Count + 1);
                outputTypeSpecRows.Add(
                    TrRmTypeSpecTableWriter.Reference(0x03,
                        checked((uint)outputImportRow)));
                nominalReference = typeSpecReference;
                if (!nominalTypeSnapshots.Contains(expectation.Provider))
                {
                    nominalTypeSnapshots.Add(expectation.Provider);
                }
                break;
            }

            if (nominalReference == 0
                && StringComparer.Ordinal.Equals(
                    definition.Identity.ModuleIdentity,
                    rootSnapshot.ModuleIdentity)
                && StringComparer.Ordinal.Equals(
                    definition.Identity.ProviderIdentity,
                    rootSnapshot.ModuleIdentity)
                && typeDefRowMap.TryGetValue(
                    GetTypeDefRow(definition.Identity.DefinitionId),
                    out var outputTypeDefRow))
            {
                nominalReference
                    = checked((uint)outputTypeSpecRows.Count + 1);
                outputTypeSpecRows.Add(
                    TrRmTypeSpecTableWriter.Reference(0x02,
                        checked((uint)outputTypeDefRow)));
                if (!nominalTypeSnapshots.Contains(provider))
                {
                    nominalTypeSnapshots.Add(provider);
                }
            }

            if (nominalReference != 0
                && !nominalTypeReferences.TryAdd(definition.Identity,
                    nominalReference))
            {
                throw new InvalidDataException(
                    "A retained C1M1 nominal TypeDef has duplicate reusable references.");
            }
        }
        nominalTypeWriter.ImportDefinitionReferences(
            nominalTypeReferences, nominalTypeSnapshots);

        var outputAssociatedRelations
            = new List<TrRmAssociatedRelationRow>(
                outputTables.AssociatedRelations.Count
                    + constraintPlans.Count);
        for (var row = 1; row <= outputTables.AssociatedRelations.Count;
            row++)
        {
            outputAssociatedRelations.Add(
                new TrRmAssociatedRelationRow(
                    outputTables.AssociatedRelations
                        .GetDeclaringInterfaceTypeSpecRef(row),
                    outputTables.AssociatedRelations
                        .GetAssociatedTypeDefReference(row),
                    outputTables.AssociatedRelations
                        .GetRequiredTypeSpecRef(row)));
        }

        BoundGenericMethodAssociatedRelation GetAssociatedRelation(
            C1M1GenericConstraintPlan plan)
        {
            var methodDefId = 0x1600_0000u
                | checked((uint)plan.MethodRow);
            var matches = new List<BoundGenericMethodAssociatedRelation>();
            foreach (var root in reemissionProgram.RetainedGenericMethodRoots)
            {
                if (root.Origin.MethodDefId == methodDefId)
                {
                    matches.AddRange(root.Formals
                        .Where(formal =>
                            formal.Ordinal == plan.FormalOrdinal)
                        .SelectMany(static formal =>
                            formal.AssociatedRelations)
                        .Where(relation => relation.AssociatedRelationRow
                            == checked((int)plan.Payload)));
                }

                if (root.Binding is not BoundGenericMethodImportBinding
                    binding)
                {
                    continue;
                }

                foreach (var method in new[] {
                    binding.Expected, binding.Provider,
                })
                {
                    if (method.Origin.MethodDefId != methodDefId)
                    {
                        continue;
                    }

                    matches.AddRange(method.Formals.Where(formal =>
                            formal.Ordinal == plan.FormalOrdinal)
                        .SelectMany(static formal =>
                            formal.AssociatedRelations)
                        .Where(relation => relation.AssociatedRelationRow
                            == checked((int)plan.Payload)));
                }
            }

            var distinctMatches = matches.Distinct().ToArray();
            if (distinctMatches.Length != 1)
            {
                throw Unsupported(
                    "A retained CM1AR1 class Constraint has no unique typed associated relation.");
            }

            return distinctMatches[0];
        }

        var constraintRows = new List<TrRmConstraintRow>(
            constraintPlans.Count);
        var typeEmissionContexts
            = new Dictionary<int, C1M1GenericMethodTypeEmissionContext>();
        var interfaceConstraintVisits
            = new Dictionary<int, int>();
        var exportedInterfaceTypeSpecRows
            = new Dictionary<(string QualifiedName, int Arity), uint>();
        var emittedMethodTypeSpecReferences
            = new Dictionary<C1M1GenericMethodTypeSpecPlan, uint>();
        foreach (var plan in genericMethodTypeSpecPlans)
        {
            if (!typeEmissionContexts.TryGetValue(plan.MethodRow,
                    out var typeContext))
            {
                var methodRoot = FindC1M1ClassMethodImport(
                    reemissionProgram.RetainedGenericMethodRoots,
                    plan.MethodRow);
                typeContext
                    = CreateC1M1GenericMethodTypeEmissionContext(
                        reemissionProgram, methodRoot, plan.MethodRow,
                        rootTables, genericParamRowMap,
                        outputTypeSpecRows);
                typeEmissionContexts.Add(plan.MethodRow, typeContext);
            }

            var proofFunction = typeContext.Root.ProofFunction;
            PrimitiveType proofType;
            if (plan.IsResult)
            {
                proofType = proofFunction.ReturnType;
            }
            else
            {
                var proofIndex = plan.ParameterOrdinal
                    + (typeContext.Root.Signature.IsStatic ? 0 : 1);
                if ((uint)proofIndex
                    >= (uint)proofFunction.Variables.Length)
                {
                    throw new InvalidDataException(
                        "A retained class MethodDef generic parameter has no checked proof type.");
                }

                proofType = proofFunction.Variables[proofIndex].Type;
            }

            var proofProgram = typeContext.Definition.ProofProgram;
            var typeSpecReference = GenericMethodTypeSpecRef(proofType,
                typeContext.FormalTypeSpecRows,
                typeContext.BuiltInTypeSpecRows, outputTypeSpecRows,
                proofProgram.SpanTypes,
                proofProgram.CheckedReferenceTypes,
                typeContext.Definition.DefinitionOrdinal,
                typeContext.Definition, allowSpanType: true,
                nominalTypes: nominalTypeWriter, depth: 0);
            if (!emittedMethodTypeSpecReferences.TryAdd(plan,
                    typeSpecReference))
            {
                throw new InvalidDataException(
                    "A retained class MethodDef generic TypeSpec is emitted more than once.");
            }
        }

        var emittedDefaultTypeSpecReferences
            = new Dictionary<int, uint>();
        foreach (var plan in genericMethodDefaultTypeSpecPlans)
        {
            if (!typeEmissionContexts.TryGetValue(plan.MethodRow,
                    out var typeContext))
            {
                var methodRoot = FindC1M1ClassMethodImport(
                    reemissionProgram.RetainedGenericMethodRoots,
                    plan.MethodRow);
                typeContext
                    = CreateC1M1GenericMethodTypeEmissionContext(
                        reemissionProgram, methodRoot, plan.MethodRow,
                        rootTables, genericParamRowMap,
                        outputTypeSpecRows);
                typeEmissionContexts.Add(plan.MethodRow, typeContext);
            }

            if ((uint)plan.FormalOrdinal
                    >= (uint)typeContext.Root.Formals.Length
                || typeContext.Root.Formals[plan.FormalOrdinal]
                    .TypeDefault is not { } typeDefault)
            {
                throw new InvalidDataException(
                    "A retained class MethodDef type default has no checked formal term.");
            }

            var typeSpecReference = nominalTypeWriter.TypeTermRef(
                typeDefault.Term, typeContext.Definition,
                typeContext.FormalTypeSpecRows,
                typeContext.BuiltInTypeSpecRows, depth: 0);
            if (!emittedDefaultTypeSpecReferences.TryAdd(
                    plan.GenericParamRow, typeSpecReference))
            {
                throw new InvalidDataException(
                    "A retained class GenericParam default is emitted more than once.");
            }
        }

        foreach (var plan in constraintPlans)
        {
            if (!typeEmissionContexts.TryGetValue(plan.MethodRow,
                    out var typeContext))
            {
                var methodRoot = FindC1M1ClassMethodImport(
                    reemissionProgram.RetainedGenericMethodRoots,
                    plan.MethodRow);
                typeContext
                    = CreateC1M1GenericMethodTypeEmissionContext(
                        reemissionProgram, methodRoot, plan.MethodRow,
                        rootTables, genericParamRowMap,
                        outputTypeSpecRows);
                typeEmissionContexts.Add(plan.MethodRow, typeContext);
                interfaceConstraintVisits.Add(plan.MethodRow,
                    SemanticVerifier.MaximumAnalysisVisits);
            }

            switch (plan.Kind)
            {
                case 1:
                {
                    var formal = typeContext.Root.Formals[
                        plan.FormalOrdinal];
                    if ((uint)plan.InterfaceConstraintOrdinal
                        >= (uint)formal.InterfaceConstraints.Length)
                    {
                        throw new InvalidDataException(
                            "A retained CM1AR1 interface Constraint has no typed interface view.");
                    }

                    var remainingVisits
                        = interfaceConstraintVisits[plan.MethodRow];
                    var typeSpec
                        = GenericMethodInterfaceConstraintTypeSpecRef(
                            formal.InterfaceConstraints[
                                plan.InterfaceConstraintOrdinal],
                            typeContext.Definition,
                            exportedInterfaceTypeSpecRows,
                            importedInterfaceImportRows,
                            typeContext.FormalTypeSpecRows,
                            typeContext.GenericParamRows,
                            typeContext.BuiltInTypeSpecRows,
                            outputTypeSpecRows, depth: 0,
                            ref remainingVisits);
                    interfaceConstraintVisits[plan.MethodRow]
                        = remainingVisits;
                    constraintRows.Add(new TrRmConstraintRow(
                        plan.GenericParamRow, 1, typeSpec));
                    break;
                }

                case 2 when plan.Payload is >= 1 and <= 3:
                    constraintRows.Add(new TrRmConstraintRow(
                        plan.GenericParamRow, 2, plan.Payload));
                    break;

                case 3:
                {
                    var relation
                        = GetAssociatedRelation(plan);
                    var sourceRelationRow
                        = checked((int)plan.Payload);
                    if (relation.DeclaringInterfaceTypeSpecReference
                            != rootTables.AssociatedRelations
                                .GetDeclaringInterfaceTypeSpecRef(
                                    sourceRelationRow)
                        || relation.AssociatedTypeDefReference
                            != rootTables.AssociatedRelations
                                .GetAssociatedTypeDefReference(
                                    sourceRelationRow)
                        || relation.RequiredTypeSpecReference
                            != rootTables.AssociatedRelations
                                .GetRequiredTypeSpecRef(sourceRelationRow))
                    {
                        throw new InvalidDataException(
                            "A retained CM1AR1 typed relation differs from its source AssociatedRelation row.");
                    }

                    var remainingVisits
                        = interfaceConstraintVisits[plan.MethodRow];
                    var declaringInterface
                        = GenericMethodInterfaceConstraintTypeSpecRef(
                            relation.AssociatedType.DeclaringInterface,
                            typeContext.Definition,
                            exportedInterfaceTypeSpecRows,
                            importedInterfaceImportRows,
                            typeContext.FormalTypeSpecRows,
                            typeContext.GenericParamRows,
                            typeContext.BuiltInTypeSpecRows,
                            outputTypeSpecRows, depth: 0,
                            ref remainingVisits);
                    interfaceConstraintVisits[plan.MethodRow]
                        = remainingVisits;
                    var requiredType = nominalTypeWriter.TypeTermRef(
                        relation.RequiredType, typeContext.Definition,
                        typeContext.FormalTypeSpecRows,
                        typeContext.BuiltInTypeSpecRows, depth: 0);
                    var associatedTypeDef
                        = GenericMethodAssociatedTypeDefReference(
                            relation, [], importedAssociatedTypeImportRows);
                    var relationRow
                        = checked((uint)outputAssociatedRelations.Count + 1);
                    outputAssociatedRelations.Add(
                        new TrRmAssociatedRelationRow(declaringInterface,
                            associatedTypeDef, requiredType));
                    constraintRows.Add(new TrRmConstraintRow(
                        plan.GenericParamRow, 3, relationRow));
                    break;
                }

                default:
                    throw new InvalidDataException(
                        "A retained CM1AR1 class Constraint has an unsupported kind or payload.");
            }
        }

        foreach (var (genericParamRow, typeSpecReference)
            in emittedDefaultTypeSpecReferences)
        {
            if (genericParamRow <= 0
                || genericParamRow > nominalFormals.Count
                || nominalFormals[genericParamRow - 1].FormalKind != 1
                || nominalFormals[genericParamRow - 1]
                    .DefaultArgumentRef == 0
                || typeSpecReference == 0
                || typeSpecReference > outputTypeSpecRows.Count)
            {
                throw new InvalidDataException(
                    "A retained class GenericParam default cannot be remapped.");
            }

            nominalFormals[genericParamRow - 1]
                = nominalFormals[genericParamRow - 1] with {
                    DefaultArgumentRef = typeSpecReference,
                };
        }

        artifact = CompleteC1M1NominalTypeEmission(nominalSections,
            nominalNames, outputTypeSpecRows, nominalFormals,
            nominalConstraints, nominalTypeWriter, schema,
            rootSnapshot.SchemaIdentity);
        outputIndex = new TrRmArtifactIndex(artifact, schema,
            standardSchemaIdentity: rootSnapshot.SchemaIdentity);
        outputTables = TrRmStructuralMetadataValidator
            .ValidateC1AndGetExecutionTables(outputIndex);
        outputSections.Clear();
        for (var sectionIndex = 0;
            sectionIndex < outputIndex.SectionCount; sectionIndex++)
        {
            outputIndex.GetSection(sectionIndex, out var kind, out var flags,
                out var bytes, out var count);
            outputSections.Add(kind, new TrRmSection(kind, flags,
                checked((uint)count), bytes.ToArray()));
        }

        var genericParamBytes
            = EncodeC1M1GenericParamRows(nominalFormals);
        var genericParamCount = nominalFormals.Count;
        if (genericParamCount < classGenericParamCount
            || genericParamBytes.Length
                != checked(genericParamCount * 32))
        {
            throw new InvalidDataException(
                "The re-emitted CM1AR1 GenericParam table has an invalid extent.");
        }

        var existingConstraintBytes
            = EncodeC1M1ConstraintRows(nominalConstraints);
        var existingConstraintCount = nominalConstraints.Count;
        if (existingConstraintBytes.Length
            != checked(existingConstraintCount * 12))
        {
            throw new InvalidDataException(
                "The re-emitted CM1AR1 Constraint table has an invalid extent.");
        }

        var classConstraintRanges = new Dictionary<int, (int First,
            int Count)>();
        var nextConstraintRow = 1;
        foreach (var group in constraintRows.GroupBy(
                     static constraint => constraint.OwnerGenericParamRow)
                 .OrderBy(static group => group.Key))
        {
            classConstraintRanges.Add(group.Key,
                (nextConstraintRow, group.Count()));
            nextConstraintRow = checked(nextConstraintRow + group.Count());
        }

        var allConstraintBytes = new byte[checked(
            (constraintRows.Count + existingConstraintCount) * 12)];
        for (var index = 0; index < constraintRows.Count; index++)
        {
            var constraint = constraintRows[index];
            var offset = index * 12;
            BinaryPrimitives.WriteUInt32LittleEndian(
                allConstraintBytes.AsSpan(offset),
                checked((uint)constraint.OwnerGenericParamRow));
            BinaryPrimitives.WriteUInt16LittleEndian(
                allConstraintBytes.AsSpan(offset + 4), constraint.Kind);
            BinaryPrimitives.WriteUInt32LittleEndian(
                allConstraintBytes.AsSpan(offset + 8),
                constraint.Payload);
        }
        existingConstraintBytes.CopyTo(allConstraintBytes,
            constraintRows.Count * 12);

        for (var genericParamRow = 1;
            constraintRows.Count != 0
                && genericParamRow <= genericParamCount;
            genericParamRow++)
        {
            var offset = (genericParamRow - 1) * 32;
            if (genericParamRow <= classGenericParamCount)
            {
                var range = classConstraintRanges.TryGetValue(
                    genericParamRow, out var classRange)
                    ? classRange : (0, 0);
                BinaryPrimitives.WriteUInt32LittleEndian(
                    genericParamBytes.AsSpan(offset + 12),
                    checked((uint)range.Item1));
                BinaryPrimitives.WriteUInt32LittleEndian(
                    genericParamBytes.AsSpan(offset + 16),
                    checked((uint)range.Item2));
            }
            else
            {
                var first = BinaryPrimitives.ReadUInt32LittleEndian(
                    genericParamBytes.AsSpan(offset + 12));
                if (first != 0 && constraintRows.Count != 0)
                {
                    BinaryPrimitives.WriteUInt32LittleEndian(
                        genericParamBytes.AsSpan(offset + 12),
                        checked(first + (uint)constraintRows.Count));
                }
            }
        }

        var typeSpecBytes = outputTypeSpecRows.Count == 0 ? []
            : TrRmTypeSpecTableWriter.Encode(outputTypeSpecRows,
                outputTables.TypeDefs.Count, outputTables.Imports.Count,
                genericParamCount,
                outputTables.NativeFunctionPointerSignatures.Count, schema,
                outputTables.Methods.Count);
        var typeSpecs = outputTypeSpecRows.Count == 0 ? default
            : new TrRmTypeSpecTableReader(typeSpecBytes,
                outputTypeSpecRows.Count, outputTables.TypeDefs.Count,
                outputTables.Imports.Count, genericParamCount,
                outputTables.NativeFunctionPointerSignatures.Count, schema,
                outputTables.Methods.Count);
        if (genericMethodTypeSpecPlans.Count != 0)
        {
            var totalParameterCount = outputTables.Signatures.ParamCount;
            var resultTypeSpecs = new Dictionary<int, uint>();
            var parameterTypeSpecs = new Dictionary<int, uint>();
            foreach (var plan in genericMethodTypeSpecPlans)
            {
                var typeSpecReference
                    = emittedMethodTypeSpecReferences[plan];
                var references = plan.IsResult
                    ? resultTypeSpecs : parameterTypeSpecs;
                var row = plan.IsResult
                    ? plan.MethodSigRow : plan.ParameterRow;
                if (!references.TryAdd(row, typeSpecReference))
                {
                    throw new InvalidDataException(
                        "A retained class MethodSig has multiple generic nominal TypeSpec replacements.");
                }
            }

            var signatureRows = new TrRmMethodSigRow[
                outputTables.Signatures.Count];
            for (var row = 1; row <= signatureRows.Length; row++)
            {
                var (parameterFirst, parameterCount)
                    = outputTables.Signatures.GetParamRange(row);
                var (genericFirst, genericCount)
                    = outputTables.Signatures.GetGenericRange(row);
                signatureRows[row - 1] = new TrRmMethodSigRow(
                    resultTypeSpecs.TryGetValue(row, out var resultTypeSpec)
                        ? resultTypeSpec
                        : outputTables.Signatures
                            .GetResultTypeSpec(row),
                    outputTables.Signatures.GetReceiverTypeSpec(row),
                    parameterFirst, parameterCount,
                    genericFirst, genericCount);
            }

            var signatureBytes = TrRmMethodSigTableWriter.Encode(
                signatureRows, typeSpecs, totalParameterCount,
                genericParamCount);
            var signatures = new TrRmMethodSigTableReader(signatureBytes,
                signatureRows.Length, typeSpecs,
                totalParameterCount, genericParamCount);
            var parameterRows = new TrRmParamRow[totalParameterCount];
            for (var row = 1; row <= parameterRows.Length; row++)
            {
                var owner = outputTables.Signatures.GetParamOwner(row);
                var (parameterFirst, _) = outputTables.Signatures
                    .GetParamRange(owner);
                parameterRows[row - 1] = new TrRmParamRow(owner,
                    row - parameterFirst,
                    parameterTypeSpecs.TryGetValue(row,
                        out var parameterTypeSpec)
                        ? parameterTypeSpec
                        : outputTables.Parameters.GetTypeSpec(row),
                    outputTables.Parameters.GetMode(row),
                    outputTables.Parameters.GetFlags(row));
            }

            var parameterBytes = parameterRows.Length == 0 ? []
                : TrRmParamTableWriter.Encode(parameterRows, signatures,
                    typeSpecs, schema);
            outputSections[0x0017] = new TrRmSection(0x0017, 0,
                checked((uint)signatureRows.Length), signatureBytes);
            if (parameterBytes.Length != 0)
            {
                outputSections[0x0018] = new TrRmSection(0x0018, 0,
                    checked((uint)parameterRows.Length), parameterBytes);
            }
        }

        var associatedRelationBytes
            = outputAssociatedRelations.Count == 0 ? []
                : outputTypeSpecRows.Count == 0
                    ? throw new InvalidDataException(
                        "A retained CM1AR1 AssociatedRelation has no TypeSpec table.")
                : TrRmAssociatedRelationTableWriter.Encode(
                    outputAssociatedRelations, typeSpecs,
                    outputTables.AssociatedTypeDefs.Count,
                    outputTables.Imports);

        if (outputTypeSpecRows.Count != 0
            || outputSections.ContainsKey(0x0011))
        {
            outputSections[0x0011] = new TrRmSection(0x0011, 0,
                checked((uint)outputTypeSpecRows.Count), typeSpecBytes);
        }
        outputSections[0x0019] = new TrRmSection(0x0019, 0,
            checked((uint)genericParamCount), genericParamBytes);
        if (allConstraintBytes.Length != 0)
        {
            outputSections[0x001A] = new TrRmSection(0x001A, 0,
                checked((uint)(constraintRows.Count
                    + existingConstraintCount)), allConstraintBytes);
        }
        if (associatedRelationBytes.Length != 0)
        {
            outputSections[0x0036] = new TrRmSection(0x0036, 0,
                checked((uint)outputAssociatedRelations.Count),
                associatedRelationBytes);
        }

        var result = TrRmArtifactWriter.Encode(
            [.. outputSections.Values], schema,
            standardSchemaIdentity: rootSnapshot.SchemaIdentity);
        _ = TrRmStructuralMetadataValidator
            .ValidateC1AndGetExecutionTables(
                new TrRmArtifactIndex(result, schema,
                    standardSchemaIdentity: rootSnapshot.SchemaIdentity));
        return result;
    }

    private static string GetC1M1TypeDefQualifiedName(
        TrRmTypeDefTableReader typeDefs, int typeDefRow)
    {
        var name = typeDefs.GetName(typeDefRow);
        var namespaceName = typeDefs.GetNamespace(typeDefRow);
        return namespaceName.Length == 0 ? name
            : namespaceName + "." + name;
    }

    private static void ValidateC1M1ClassCarrierBindings(
        BoundProgram program,
        ImmutableArray<TrRmStandardModuleSelection> modules,
        byte[][] artifacts,
        string[] schemaIdentities,
        ImmutableArray<TrRmStandardTypeProviderBinding> typeProviderBindings,
        ImmutableArray<TrRmStandardMethodProviderBinding>
            methodProviderBindings)
    {
        var expectationsByImport = new Dictionary<
            (string ModuleIdentity, int ModuleRow, int ImportRow),
            BoundStandardClassExpectation>();
        var methodExpectationsByImport = new Dictionary<
            (string ModuleIdentity, int ModuleRow, int ImportRow),
            (BoundStandardClassExpectation Owner,
                BoundStandardClassMethodExpectation Method)>();
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
                    usingModule.ArtifactIndex,
                    schemaIdentities[usingModule.ArtifactIndex]);
                EnsureC1ClassDeclarationTablesSupported(usingTables);
                var usingSnapshot = CreateA2SourceTypeProviderSnapshot(
                    usingTables, artifacts[usingModule.ArtifactIndex],
                    schemaIdentities[usingModule.ArtifactIndex],
                    usingModule.ModuleRow);
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

                foreach (var methodExpectation
                    in expectation.MethodExpectations)
                {
                    if (methodExpectation.OwnerImportRow != binding.ImportRow
                        || methodExpectation.ExpectedOwnerDefinitionId
                            != binding.ExpectedDefinitionId
                        || methodExpectation.ProviderOwnerDefinitionId
                            != binding.ProviderDefinitionId
                        || !SourceTypeSnapshotsMatch(
                            methodExpectation.Provider, binding.Provider)
                        || !methodExpectationsByImport.TryAdd((
                            binding.UsingModule.ModuleIdentity,
                            binding.UsingModule.ModuleRow,
                            methodExpectation.ImportRow),
                            (expectation, methodExpectation)))
                    {
                        throw new InvalidDataException(
                            "A retained class MethodDef Import has an invalid owner or duplicate expectation.");
                    }
                }

                var providerModule = modules.Single(module =>
                    StringComparer.Ordinal.Equals(module.Identity,
                        binding.ProducerIdentity)
                    && module.ModuleRow == binding.Provider.ModuleRow);
                var providerTables = GetC1M1Tables(artifacts,
                    providerModule.ArtifactIndex,
                    schemaIdentities[providerModule.ArtifactIndex]);
                EnsureC1ClassDeclarationTablesSupported(providerTables);
                var providerSnapshot = CreateA2SourceTypeProviderSnapshot(
                    providerTables, artifacts[providerModule.ArtifactIndex],
                    schemaIdentities[providerModule.ArtifactIndex],
                    providerModule.ModuleRow);
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
                consumerModule.ArtifactIndex,
                schemaIdentities[consumerModule.ArtifactIndex]);
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

        var boundMethodImports = new HashSet<(
            string ModuleIdentity, int ModuleRow, int ImportRow)>();
        foreach (var binding in methodProviderBindings)
        {
            var consumerModule = modules.Single(module =>
                module.ArtifactIndex == binding.ConsumerArtifactIndex
                && module.ModuleRow == binding.ConsumerModuleRow);
            var consumerTables = GetC1M1Tables(artifacts,
                consumerModule.ArtifactIndex,
                schemaIdentities[consumerModule.ArtifactIndex]);
            var key = (consumerModule.Identity, binding.ConsumerModuleRow,
                binding.ImportRow);
            if (!methodExpectationsByImport.TryGetValue(key,
                    out var retained)
                || !boundMethodImports.Add(key)
                || binding.ExpectedMethodDefRow
                    != GetMethodDefRow(
                        retained.Method.ExpectedDefinitionId)
                || binding.ExpectedOwnerTypeDefRow
                    != GetTypeDefRow(
                        retained.Method.ExpectedOwnerDefinitionId)
                || binding.OwnerImportRow
                    != retained.Method.OwnerImportRow
                || binding.ProviderMethodDefRow
                    != GetMethodDefRow(
                        retained.Method.ProviderDefinitionId)
                || binding.ProviderTypeDefRow
                    != GetTypeDefRow(
                        retained.Method.ProviderOwnerDefinitionId)
                || !StringComparer.Ordinal.Equals(
                    binding.ProducerIdentity,
                    retained.Method.Provider.ModuleIdentity)
                || !StringComparer.Ordinal.Equals(binding.ExportKey,
                    retained.Method.ExportKey)
                || binding.TargetRequirement
                    != retained.Method.Provider.TargetRequirement
                || binding.RuntimePolicy
                    != retained.Method.Provider.RuntimePolicy)
            {
                throw new InvalidDataException(
                    "C1M1 class MethodDef Imports do not match every retained expectation.");
            }

            var expectedMethodRow = binding.ExpectedMethodDefRow;
            var expectedMethod = retained.Owner.ExpectedDeclaration.Methods
                .SingleOrDefault(method =>
                    method.DefinitionId
                        == retained.Method.ExpectedDefinitionId);
            var expectedModuleRefRow = consumerTables.Imports
                .GetModuleRefForImportRow(binding.ImportRow);
            var importBinding = consumerTables.Imports
                .GetBindingForImportRow(binding.ImportRow);
            var moduleRefRequirements = consumerTables.ModuleRefs
                .GetRequirements(expectedModuleRefRow);
            if (expectedMethod is null || expectedMethod.BodyRow != 0
                || consumerTables.Methods.GetOwnerType(expectedMethodRow)
                    != binding.ExpectedOwnerTypeDefRow
                || consumerTables.Imports
                    .GetExpectedDefinitionForImportRow(binding.ImportRow)
                    != retained.Method.ExpectedDefinitionId
                || binding.ModuleRefRow != expectedModuleRefRow
                || !StringComparer.Ordinal.Equals(importBinding.ExportKey,
                    retained.Method.ExportKey)
                || !StringComparer.Ordinal.Equals(
                    importBinding.ProducerIdentity,
                    retained.Method.Provider.ModuleIdentity)
                || importBinding.TargetRequirement
                    != retained.Method.Provider.TargetRequirement
                || importBinding.RuntimePolicy
                    != retained.Method.Provider.RuntimePolicy
                || moduleRefRequirements
                    != (retained.Method.Provider.TargetRequirement,
                        retained.Method.Provider.RuntimePolicy))
            {
                throw new InvalidDataException(
                    "A retained class MethodDef expectation no longer matches its bodyless Import row.");
            }

            var providerModule = modules.Single(module =>
                StringComparer.Ordinal.Equals(module.Identity,
                    retained.Method.Provider.ModuleIdentity)
                && module.ModuleRow == retained.Method.Provider.ModuleRow);
            var providerTables = GetC1M1Tables(artifacts,
                providerModule.ArtifactIndex,
                schemaIdentities[providerModule.ArtifactIndex]);
            var providerSnapshot = CreateA2SourceTypeProviderSnapshot(
                providerTables, artifacts[providerModule.ArtifactIndex],
                schemaIdentities[providerModule.ArtifactIndex],
                providerModule.ModuleRow);
            var providerMethodRow = binding.ProviderMethodDefRow;
            var providerMethodDefinitionId = 0x1600_0000u
                | checked((uint)providerMethodRow);
            if (!SourceTypeSnapshotsMatch(retained.Method.Provider,
                    providerSnapshot)
                || binding.ProviderArtifactIndex
                    != providerModule.ArtifactIndex
                || binding.ProviderModuleRow != providerModule.ModuleRow
                || providerTables.TypeDefs.GetOwnerModule(
                    binding.ProviderTypeDefRow) != providerModule.ModuleRow
                || providerTables.TypeDefs.GetKind(
                    binding.ProviderTypeDefRow) != 2
                || providerTables.Methods.GetOwnerType(providerMethodRow)
                    != binding.ProviderTypeDefRow
                || !providerTables.Exports.TryGetDefinitionForKey(
                    providerModule.ModuleRow, retained.Method.ExportKey,
                    out var providerDefinition)
                || providerDefinition != providerMethodDefinitionId)
            {
                throw new InvalidDataException(
                    "A retained class MethodDef provider selection no longer matches its Module and ExportKey.");
            }

            var providerIdentity = new BoundGenericNominalTypeIdentity(
                providerModule.Identity, providerModule.Identity,
                retained.Method.ProviderOwnerDefinitionId);
            var actualDeclaration = program.StandardClassDeclarations
                .SingleOrDefault(candidate =>
                    candidate.Declaration.Identity == providerIdentity);
            var actualMethod = actualDeclaration?.Declaration.Methods
                .SingleOrDefault(method =>
                    method.DefinitionId == providerMethodDefinitionId);
            if (actualMethod is null
                || actualMethod.BodyRow == 0
                || actualMethod.GenericParameterCount
                    != retained.Method.GenericParameterCount
                || actualMethod.ConstructionRole != 0
                || !actualMethod.ExportKeys.Contains(
                    retained.Method.ExportKey, StringComparer.Ordinal))
            {
                throw new InvalidDataException(
                    "A retained class MethodDef provider view is missing its selected method.");
            }
        }

        if (boundMethodImports.Count != methodExpectationsByImport.Count)
        {
            throw new InvalidDataException(
                "A retained class MethodDef Import expectation has no selected provider binding.");
        }
    }

    private static void ValidateC1M1StructCarrierBindings(
        BoundProgram program,
        ImmutableArray<TrRmStandardModuleSelection> modules,
        byte[][] artifacts,
        string[] schemaIdentities,
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
                consumerModule.ArtifactIndex,
                schemaIdentities[consumerModule.ArtifactIndex]);
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
                schemaIdentities[consumerModule.ArtifactIndex],
                consumerModule.ModuleRow);
            var providerModule = modules.Single(module =>
                StringComparer.Ordinal.Equals(module.Identity,
                    expectation.ProducerIdentity)
                && module.ModuleRow == expectation.Provider.ModuleRow);
            var providerTables = GetC1M1Tables(artifacts,
                providerModule.ArtifactIndex,
                schemaIdentities[providerModule.ArtifactIndex]);
            var providerSnapshot = CreateA2SourceTypeProviderSnapshot(
                providerTables, artifacts[providerModule.ArtifactIndex],
                schemaIdentities[providerModule.ArtifactIndex],
                providerModule.ModuleRow);
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
        GetC1M1Tables(byte[][] artifacts, int artifactIndex,
            string schemaIdentity)
    {
        if ((uint)artifactIndex >= (uint)artifacts.Length)
        {
            throw new InvalidDataException(
                "A C1M1 class provider artifact index is out of range.");
        }

        var schema = GetC1M1ClassMetadataSchema(schemaIdentity);
        return TrRmStructuralMetadataValidator
            .ValidateC1AndGetExecutionTables(
                new TrRmArtifactIndex(artifacts[artifactIndex], schema));
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

    private static byte[] RemapC1M1ClassMethodBody(
        TrRmStructuralMetadataValidator.ValidatedExecutionTables sourceTables,
        ReadOnlyMemory<byte> bodyBytes, Dictionary<int, int> typeDefRowMap,
        Dictionary<int, int> methodDefRowMap,
        Dictionary<int, int> fieldRowMap, Dictionary<int, int> importRowMap,
        Dictionary<int, int> fieldPathRowMap,
        List<TrRmFieldPathRow> fieldPathRows,
        Dictionary<C1M1FieldPathIdentity, int> fieldPathRowsByIdentity,
        Dictionary<int, int> dependencyContractRowMap,
        TrRmFrozenScalarValueBuilder frozenScalarValueBuilder,
        List<ReadOnlyMemory<byte>> outputTypeSpecRows,
        Dictionary<int, uint> typeSpecRowMap,
        HashSet<int> activeTypeSpecRows, TrRmArtifactSchema schema,
        Dictionary<int, int> genericParamRowMap,
        Func<uint, uint>? typeSpecReferenceRemapper = null,
        Func<int, uint, TrRmFrozenScalarValueReference>?
            constantSourceRemapper = null)
    {
        if (!bodyBytes.IsEmpty && bodyBytes.Span[0] is (0x02 or 0x03))
        {
            return RemapC1M1ClassFieldAccessBody(bodyBytes,
                fieldRowMap, importRowMap);
        }

        if (!bodyBytes.IsEmpty && bodyBytes.Span[0] == 0x04)
        {
            return RemapC1M1ClassFieldReferenceBody(bodyBytes,
                fieldRowMap, importRowMap, dependencyContractRowMap);
        }

        if (!bodyBytes.IsEmpty && bodyBytes.Span[0] == 0x05)
        {
            return RemapC1M1ClassIdentityForwarderBody(bodyBytes,
                methodDefRowMap, importRowMap);
        }

        var body = new TrRmRegisterBodyReader(bodyBytes.Span,
            sourceTables.Contracts.Count, schema);
        uint RemapTypeSpecReference(uint typeSpecReference)
            => typeSpecReferenceRemapper is not null
                ? typeSpecReferenceRemapper(typeSpecReference)
                : RemapC1M1ClassTypeSpec(sourceTables,
                    typeSpecReference, typeDefRowMap, importRowMap,
                    outputTypeSpecRows, typeSpecRowMap,
                    activeTypeSpecRows, depth: 0,
                    genericParamRowMap: genericParamRowMap);

        var typeMap = body.TypeMap.ToArray();
        for (var typeMapIndex = 0; typeMapIndex < body.TypeCount;
            typeMapIndex++)
        {
            var offset = typeMapIndex * sizeof(uint);
            var token = BinaryPrimitives.ReadUInt32LittleEndian(
                typeMap.AsSpan(offset));
            var typeSpecReference = (byte)(token >> 24) switch
            {
                0x11 => token & 0x00FF_FFFF,
                0xF2 or 0xF3 => token,
                _ => throw Unsupported(
                    "CM1 local class MethodDef Body TypeMap entries must be inline or TypeSpec-backed."),
            };
            var remappedTypeSpec
                = RemapTypeSpecReference(typeSpecReference);
            BinaryPrimitives.WriteUInt32LittleEndian(typeMap.AsSpan(offset),
                CreateStandardBodyTypeMapToken(remappedTypeSpec));
        }

        int RemapFieldPath(int sourceRow)
        {
            if (fieldPathRowMap.TryGetValue(sourceRow,
                    out var existingRow))
            {
                return existingRow;
            }

            var sourceRootTypeSpec = sourceTables.FieldPaths
                .GetRootTypeSpecRef(sourceRow);
            var rootTypeSpec
                = RemapTypeSpecReference(sourceRootTypeSpec);
            var fieldCount = sourceTables.FieldPaths.GetFieldCount(
                sourceRow);
            var fieldReferences = new uint[fieldCount];
            for (var fieldIndex = 0; fieldIndex < fieldCount; fieldIndex++)
            {
                var token = sourceTables.FieldPaths.GetFieldReference(
                    sourceRow, fieldIndex);
                var row = checked((int)(token & 0x00FF_FFFF));
                fieldReferences[fieldIndex] = (byte)(token >> 24) switch
                {
                    0x14 when fieldRowMap.TryGetValue(row,
                        out var localFieldRow)
                        => 0x1400_0000u
                            | checked((uint)localFieldRow),
                    0x1C when importRowMap.TryGetValue(row,
                        out var fieldImportRow)
                        => 0x1C00_0000u
                            | checked((uint)fieldImportRow),
                    _ => throw Unsupported(
                        "CM1 local class MethodDef Body FieldPath steps must target retained FieldDefs or FieldDef Imports."),
                };
            }

            var identity = new C1M1FieldPathIdentity(rootTypeSpec,
                ImmutableArray.CreateRange(fieldReferences));
            if (!fieldPathRowsByIdentity.TryGetValue(identity,
                    out var outputRow))
            {
                if (fieldPathRows.Count >= 0xFF_FFFF)
                {
                    throw Unsupported(
                        "CM1 local class MethodDef Bodies exceed the FieldPath row limit.");
                }

                outputRow = checked(fieldPathRows.Count + 1);
                fieldPathRows.Add(new TrRmFieldPathRow(rootTypeSpec,
                    fieldReferences));
                fieldPathRowsByIdentity.Add(identity, outputRow);
            }

            fieldPathRowMap.Add(sourceRow, outputRow);
            return outputRow;
        }

        var methodMap = body.MethodMap.ToArray();
        for (var methodMapIndex = 0; methodMapIndex < body.MethodCount;
            methodMapIndex++)
        {
            var offset = methodMapIndex * sizeof(uint);
            var token = BinaryPrimitives.ReadUInt32LittleEndian(
                methodMap.AsSpan(offset));
            var row = checked((int)(token & 0x00FF_FFFF));
            var remappedRow = (byte)(token >> 24) switch
            {
                0x16 when methodDefRowMap.TryGetValue(row,
                    out var localMethodRow) => localMethodRow,
                0x1C when importRowMap.TryGetValue(row,
                    out var importRow) => importRow,
                _ => throw Unsupported(
                    "CM1 local class MethodDef Body MethodMap targets must be retained local or imported MethodDefs.")
            };

            BinaryPrimitives.WriteUInt32LittleEndian(methodMap.AsSpan(offset),
                (token & 0xFF00_0000u) | checked((uint)remappedRow));
        }

        var fieldMap = body.FieldMap.ToArray();
        for (var fieldMapIndex = 0; fieldMapIndex < body.FieldCount;
            fieldMapIndex++)
        {
            var offset = fieldMapIndex * sizeof(uint);
            var token = BinaryPrimitives.ReadUInt32LittleEndian(
                fieldMap.AsSpan(offset));
            var row = checked((int)(token & 0x00FF_FFFF));
            var remappedToken = (byte)(token >> 24) switch
            {
                0x14 when fieldRowMap.TryGetValue(row,
                    out var localFieldRow)
                    => 0x1400_0000u | checked((uint)localFieldRow),
                0x1C when importRowMap.TryGetValue(row,
                    out var fieldImportRow)
                    => 0x1C00_0000u | checked((uint)fieldImportRow),
                0x1D => 0x1D00_0000u
                    | checked((uint)RemapFieldPath(row)),
                _ => throw Unsupported(
                    "CM1 local class MethodDef Body FieldMap targets must be retained local FieldDefs, FieldDef Imports or FieldPaths."),
            };

            BinaryPrimitives.WriteUInt32LittleEndian(fieldMap.AsSpan(offset),
                remappedToken);
        }

        var constantMap = body.ConstantMap.ToArray();
        for (var constantMapIndex = 0;
            constantMapIndex < body.ConstantCount; constantMapIndex++)
        {
            var offset = constantMapIndex * 2 * sizeof(uint);
            var typeSpecReference = BinaryPrimitives
                .ReadUInt32LittleEndian(constantMap.AsSpan(offset));
            var sourceRow = checked((int)BinaryPrimitives
                .ReadUInt32LittleEndian(constantMap.AsSpan(
                    offset + sizeof(uint))));
            if (constantSourceRemapper is not null)
            {
                var remappedConstant = constantSourceRemapper(sourceRow,
                    typeSpecReference);
                BinaryPrimitives.WriteUInt32LittleEndian(
                    constantMap.AsSpan(offset),
                    remappedConstant.TypeSpecRef);
                BinaryPrimitives.WriteUInt32LittleEndian(
                    constantMap.AsSpan(offset + sizeof(uint)),
                    remappedConstant.ConstantSourceRow);
                continue;
            }

            var type = sourceTables.TypeSpecs.GetTypeSpecRef(
                typeSpecReference);
            if (type.IsRow || type.Form != 0x01 || type.Qualifiers != 0
                || !TrRmConstantSourceValueValidator.TryGetBoundScalarType(
                    type.TypeCode, out var primitiveType))
            {
                throw Unsupported(
                    "CM1 local class MethodDef Body ConstantMap entries require an inline scalar TypeSpec.");
            }

            if (sourceTables.ConstantSources.GetKind(sourceRow) != 1)
            {
                throw Unsupported(
                    "CM1 local class MethodDef Body ConstantMap entries require frozen scalar ConstantSources.");
            }

            var valueNodeRow = checked((int)sourceTables.ConstantSources
                .GetPayloadReference(sourceRow));
            if (sourceTables.FrozenValueNodes.GetTypeSpecRef(valueNodeRow)
                != typeSpecReference)
            {
                throw new InvalidDataException(
                    "A retained class MethodDef Body ConstantMap changed its frozen scalar type.");
            }

            var payload = sourceTables.FrozenValueNodes.GetScalarPayload(
                valueNodeRow, sourceTables.Blobs);
            var bits = ScalarBits.FromPayload(primitiveType, payload);
            var remappedConstant = frozenScalarValueBuilder.Add(
                new BoundConstant(bits, primitiveType, default));
            if (remappedConstant.TypeSpecRef != typeSpecReference)
            {
                throw new InvalidDataException(
                    "A retained class MethodDef Body ConstantMap changed its exact scalar TypeSpec.");
            }

            BinaryPrimitives.WriteUInt32LittleEndian(
                constantMap.AsSpan(offset), remappedConstant.TypeSpecRef);
            BinaryPrimitives.WriteUInt32LittleEndian(
                constantMap.AsSpan(offset + sizeof(uint)),
                remappedConstant.ConstantSourceRow);
        }

        var blocks = new TrRmRegisterBodyBlock[body.BlockCount];
        for (var blockIndex = 0; blockIndex < body.BlockCount; blockIndex++)
        {
            var block = body.GetBlock(blockIndex);
            blocks[blockIndex] = new(
                BinaryPrimitives.ReadUInt32LittleEndian(block),
                BinaryPrimitives.ReadUInt32LittleEndian(
                    block.Slice(sizeof(uint))),
                BinaryPrimitives.ReadUInt16LittleEndian(
                    block.Slice(2 * sizeof(uint))));
        }

        var records = new ReadOnlyMemory<byte>[body.RecordCount];
        for (var recordIndex = 0; recordIndex < body.RecordCount;
            recordIndex++)
        {
            var record = body.GetRecord(recordIndex);
            if ((record[0] == 0x03
                    && BinaryPrimitives.ReadUInt32LittleEndian(
                        record.Slice(16)) != 0)
                || (record[0] == 0x05
                    && BinaryPrimitives.ReadUInt32LittleEndian(
                        record.Slice(12)) != 0))
            {
                throw Unsupported(
                    "CM1 local class MethodDef Bodies with contract-row references are not supported.");
            }

            var remappedRecord = record.ToArray();
            if (record[0] == 0x04)
            {
                if (record.Length != 24)
                {
                    throw new InvalidDataException(
                        "A retained class MethodDef PlaceSelection has an invalid extent.");
                }

                if (record[13] == 1)
                {
                    if (record[4] is not (2 or 4))
                    {
                        throw new InvalidDataException(
                            "A retained class MethodDef PlaceSelection has an invalid FieldDef reference shape.");
                    }

                    var oldFieldRow = checked((int)
                        BinaryPrimitives.ReadUInt32LittleEndian(
                            record.Slice(16, sizeof(uint))));
                    if (!fieldRowMap.TryGetValue(oldFieldRow,
                            out var remappedFieldRow))
                    {
                        throw Unsupported(
                            "A retained class MethodDef PlaceSelection must target a retained FieldDef.");
                    }

                    BinaryPrimitives.WriteUInt32LittleEndian(
                        remappedRecord.AsSpan(16),
                        checked((uint)remappedFieldRow));
                }
            }

            records[recordIndex] = remappedRecord;
        }

        return TrRmRegisterBodyWriter.Encode(body.HomeMap.ToArray(),
            typeMap, methodMap, fieldMap,
            constantMap, blocks, records,
            body.Code.ToArray(), contractCount: 0, schema: schema);
    }

    private static byte[] RemapC1M1ClassFieldAccessBody(
        ReadOnlyMemory<byte> bodyBytes, Dictionary<int, int> fieldRowMap,
        Dictionary<int, int> importRowMap)
    {
        var body = bodyBytes.Span;
        var kind = body[0];
        var expectedLength = kind == 0x02 ? 8 : 12;
        if (body.Length != expectedLength || body[1] != 0
            || BinaryPrimitives.ReadUInt16LittleEndian(body.Slice(2)) != 0
            || (kind == 0x03
                && (body[10] is < 1 or > 3 || body[11] != 0)))
        {
            throw Invalid(
                "A retained CM1 compact field Body has invalid flags, reserved bytes or length.");
        }

        var fieldToken = BinaryPrimitives.ReadUInt32LittleEndian(
            body.Slice(4));
        var fieldRow = checked((int)(fieldToken & 0x00FF_FFFF));
        var remappedRow = (byte)(fieldToken >> 24) switch
        {
            0x14 when fieldRowMap.TryGetValue(fieldRow,
                out var localFieldRow) => localFieldRow,
            0x1C when importRowMap.TryGetValue(fieldRow,
                out var fieldImportRow) => fieldImportRow,
            _ => throw Unsupported(
                "A CM1 compact field Body must target a retained local or imported FieldDef."),
        };

        var remappedBody = bodyBytes.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(remappedBody.AsSpan(4),
            (fieldToken & 0xFF00_0000u) | checked((uint)remappedRow));
        return remappedBody;
    }

    private static byte[] RemapC1M1ClassFieldReferenceBody(
        ReadOnlyMemory<byte> bodyBytes, Dictionary<int, int> fieldRowMap,
        Dictionary<int, int> importRowMap,
        Dictionary<int, int> dependencyContractRowMap)
    {
        var body = bodyBytes.Span;
        if (body.Length != 12 || body[1] != 0
            || BinaryPrimitives.ReadUInt16LittleEndian(body.Slice(2)) != 0)
        {
            throw Invalid(
                "A retained CM1 field-reference Body has invalid flags, reserved bytes or length.");
        }

        var fieldToken = BinaryPrimitives.ReadUInt32LittleEndian(
            body.Slice(4));
        var fieldRow = checked((int)(fieldToken & 0x00FF_FFFF));
        var remappedFieldRow = (byte)(fieldToken >> 24) switch
        {
            0x14 when fieldRowMap.TryGetValue(fieldRow,
                out var localFieldRow) => localFieldRow,
            0x1C when importRowMap.TryGetValue(fieldRow,
                out var fieldImportRow) => fieldImportRow,
            _ => throw Unsupported(
                "A CM1 field-reference Body must target a retained local or imported FieldDef."),
        };
        var dependencyContractRow = checked((int)
            BinaryPrimitives.ReadUInt32LittleEndian(body.Slice(8)));
        if (!dependencyContractRowMap.TryGetValue(dependencyContractRow,
                out var remappedDependencyContractRow))
        {
            throw Invalid(
                "A retained CM1 field-reference Body must target a Dependency clause owned by its MethodDef.");
        }

        var remappedBody = bodyBytes.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(remappedBody.AsSpan(4),
            (fieldToken & 0xFF00_0000u)
                | checked((uint)remappedFieldRow));
        BinaryPrimitives.WriteUInt32LittleEndian(remappedBody.AsSpan(8),
            checked((uint)remappedDependencyContractRow));
        return remappedBody;
    }

    private static byte[] RemapC1M1ClassIdentityForwarderBody(
        ReadOnlyMemory<byte> bodyBytes, Dictionary<int, int> methodDefRowMap,
        Dictionary<int, int> importRowMap)
    {
        var body = bodyBytes.Span;
        if (body.Length != 8 || body[1] != 0
            || BinaryPrimitives.ReadUInt16LittleEndian(body.Slice(2)) != 0)
        {
            throw Invalid(
                "A retained CM1 identity-forwarder Body has invalid flags, reserved bytes or length.");
        }

        var methodToken = BinaryPrimitives.ReadUInt32LittleEndian(
            body.Slice(4));
        var methodRow = checked((int)(methodToken & 0x00FF_FFFF));
        var remappedRow = (byte)(methodToken >> 24) switch
        {
            0x16 when methodDefRowMap.TryGetValue(methodRow,
                out var localMethodRow) => localMethodRow,
            0x1C when importRowMap.TryGetValue(methodRow,
                out var importRow) => importRow,
            _ => throw Unsupported(
                "A CM1 identity-forwarder Body must target a retained local or imported MethodDef."),
        };

        var remappedBody = bodyBytes.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(remappedBody.AsSpan(4),
            (methodToken & 0xFF00_0000u) | checked((uint)remappedRow));
        return remappedBody;
    }

    private static bool IsC1M1ImportedGenericNominalMethodTypeSpec(
        TrRmStructuralMetadataValidator.ValidatedExecutionTables
            sourceTables,
        uint typeSpecReference, HashSet<int> nominalTypeImportRows,
        bool allowOptionalClassHandle = false)
    {
        var activeRows = new HashSet<int>();
        var remainingVisits = SemanticVerifier.MaximumAnalysisVisits;
        return Visit(typeSpecReference, depth: 0);

        bool IsImportedConstructedNominal(uint referenceValue)
        {
            var reference = sourceTables.TypeSpecs.GetTypeSpecRef(
                referenceValue);
            if (!reference.IsRow || reference.Form != 0x05)
            {
                return false;
            }

            var row = sourceTables.TypeSpecs.GetRow(reference.Row).Span;
            if (row.Length < 10
                || (row.Length - 6) % sizeof(uint) != 0)
            {
                throw new InvalidDataException(
                    "A retained constructed class MethodDef TypeSpec has an invalid extent.");
            }

            var genericTypeSpec = BinaryPrimitives
                .ReadUInt32LittleEndian(row.Slice(2, sizeof(uint)));
            var genericType = sourceTables.TypeSpecs.GetTypeSpecRef(
                genericTypeSpec);
            return genericType.IsRow && genericType.Form == 0x03
                && nominalTypeImportRows.Contains(
                    checked((int)genericType.Row));
        }

        bool Visit(uint referenceValue, int depth)
        {
            if (depth > SemanticVerifier.MaximumDepth
                || --remainingVisits < 0)
            {
                throw Unsupported(
                    "A retained class MethodDef generic TypeSpec exceeds the checked traversal limits.");
            }

            var reference = sourceTables.TypeSpecs.GetTypeSpecRef(
                referenceValue);
            if (!reference.IsRow)
            {
                return false;
            }

            var rowNumber = checked((int)reference.Row);
            if (!activeRows.Add(rowNumber))
            {
                throw new InvalidDataException(
                    "A retained class MethodDef TypeSpec contains a cycle.");
            }

            try
            {
                var row = sourceTables.TypeSpecs.GetRow(
                    reference.Row).Span;
                if (reference.Form == 0x05)
                {
                    return IsImportedConstructedNominal(referenceValue);
                }

                if (allowOptionalClassHandle
                    && reference.Form == 0x06
                    && reference.Qualifiers == 0 && row.Length == 6)
                {
                    var innerTypeSpec = BinaryPrimitives
                        .ReadUInt32LittleEndian(row.Slice(2, sizeof(uint)));
                    return IsImportedConstructedNominal(innerTypeSpec);
                }

                if (reference.Form != 0x07 || reference.Qualifiers != 0
                    || row.Length != 6)
                {
                    return false;
                }

                var referentTypeSpec = BinaryPrimitives
                    .ReadUInt32LittleEndian(row.Slice(2, sizeof(uint)));
                return Visit(referentTypeSpec, depth + 1);
            }
            finally
            {
                _ = activeRows.Remove(rowNumber);
            }
        }
    }

    private static uint AppendC1M1MethodTypePlaceholder(
        List<ReadOnlyMemory<byte>> outputTypeSpecRows,
        int ownerTypeDefRow)
    {
        // The staging artifact must remain structurally valid before the
        // generic TypeDef and Import closure is copied.
        var typeSpecReference
            = checked((uint)outputTypeSpecRows.Count + 1);
        outputTypeSpecRows.Add(TrRmTypeSpecTableWriter.Reference(0x02,
            checked((uint)ownerTypeDefRow)));
        return typeSpecReference;
    }

    private static uint RemapC1M1ClassTypeSpec(
        TrRmStructuralMetadataValidator.ValidatedExecutionTables sourceTables,
        uint typeSpecReference, Dictionary<int, int> typeDefRowMap,
        Dictionary<int, int> importRowMap,
        List<ReadOnlyMemory<byte>> outputRows,
        Dictionary<int, uint> rowMap, HashSet<int> activeRows, int depth,
        Dictionary<int, int>? genericParamRowMap = null,
        HashSet<int>? ambiguousTypeDefRows = null,
        Dictionary<int, int>? exactTypeDefRowMap = null)
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
                    if (exactTypeDefRowMap is not null
                        && exactTypeDefRowMap.TryGetValue(oldTypeDefRow,
                            out var exactTypeDefRow))
                    {
                        Write(row, 2, checked((uint)exactTypeDefRow));
                        break;
                    }

                    if (ambiguousTypeDefRows?.Contains(oldTypeDefRow)
                        == true)
                    {
                        throw new InvalidDataException(
                            "A class MethodSig direct TypeDef reference cannot distinguish its contextual destination view.");
                    }

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

                case 0x04:
                {
                    var oldGenericParamRow = checked((int)
                        BinaryPrimitives.ReadUInt32LittleEndian(
                            row.AsSpan(2)));
                    if (genericParamRowMap is null
                        || !genericParamRowMap.TryGetValue(
                            oldGenericParamRow,
                            out var newGenericParamRow))
                    {
                        throw Unsupported(
                            "A retained class MethodDef TypeSpec refers to an unretained GenericParam.");
                    }

                    Write(row, 2, checked((uint)newGenericParamRow));
                    break;
                }

                case 0x05:
                {
                    if (row.Length < 10 || (row.Length - 6) % 4 != 0)
                    {
                        throw new InvalidDataException(
                            "A retained constructed TypeSpec has an invalid extent.");
                    }

                    var genericTypeSpec = BinaryPrimitives
                        .ReadUInt32LittleEndian(row.AsSpan(2));
                    var remappedGenericType = RemapC1M1ClassTypeSpec(
                        sourceTables, genericTypeSpec, typeDefRowMap,
                        importRowMap, outputRows, rowMap, activeRows,
                        depth + 1, genericParamRowMap,
                        ambiguousTypeDefRows, exactTypeDefRowMap);
                    Write(row, 2, remappedGenericType);
                    for (var offset = 6; offset < row.Length; offset += 4)
                    {
                        var argument = BinaryPrimitives
                            .ReadUInt32LittleEndian(row.AsSpan(offset));
                        var remappedArgument = RemapC1M1ClassTypeSpec(
                            sourceTables, argument, typeDefRowMap,
                            importRowMap, outputRows, rowMap, activeRows,
                            depth + 1, genericParamRowMap,
                            ambiguousTypeDefRows, exactTypeDefRowMap);
                        Write(row, offset, remappedArgument);
                    }
                    break;
                }

                case >= 0x06 and <= 0x0B:
                case 0x0E:
                {
                    var child = BinaryPrimitives.ReadUInt32LittleEndian(
                        row.AsSpan(2));
                    var remappedChild = RemapC1M1ClassTypeSpec(
                        sourceTables, child, typeDefRowMap, importRowMap,
                        outputRows, rowMap, activeRows, depth + 1,
                        genericParamRowMap, ambiguousTypeDefRows,
                        exactTypeDefRowMap);
                    Write(row, 2, remappedChild);
                    break;
                }

                case 0x0C:
                {
                    var child = BinaryPrimitives.ReadUInt32LittleEndian(
                        row.AsSpan(3));
                    var remappedChild = RemapC1M1ClassTypeSpec(
                        sourceTables, child, typeDefRowMap, importRowMap,
                        outputRows, rowMap, activeRows, depth + 1,
                        genericParamRowMap, ambiguousTypeDefRows,
                        exactTypeDefRowMap);
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
        Dictionary<int, ImmutableArray<string>> exportsByOldRow,
        ImmutableArray<uint> typeSpecSeeds)
    {
        var pendingTypeDefRows = new Queue<int>(typeDefViewsByOldRow.Keys);
        var pendingTypeSpecs = new Stack<(uint Reference, int Depth)>();
        var visitedTypeSpecs = new HashSet<int>();
        var remaining = SemanticVerifier.MaximumAnalysisVisits;
        foreach (var typeSpecReference in typeSpecSeeds)
        {
            pendingTypeSpecs.Push((typeSpecReference, 0));
        }

        void DrainTypeSpecs()
        {
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
                    case 0x04:
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
                        if (kind == 3)
                        {
                            break;
                        }
                        if (kind is not (1 or 2))
                        {
                            throw Unsupported(
                                "C1M1 class re-emission does not retain this local TypeDef dependency kind.");
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
                                = CreateC1M1TypeDefEmissionView(rootTables,
                                    rootSnapshot, referencedTypeDefRow);
                            if (dependencyView.Kind != kind
                                || !typeDefViewsByOldRow.TryAdd(
                                    referencedTypeDefRow, dependencyView)
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

                    case 0x05:
                    {
                        if (row.Length < 10 || (row.Length - 6) % 4 != 0)
                        {
                            throw new InvalidDataException(
                                "A retained constructed TypeSpec has an invalid extent.");
                        }

                        pendingTypeSpecs.Push((
                            BinaryPrimitives.ReadUInt32LittleEndian(
                                row[2..]),
                            checked(pendingTypeSpec.Depth + 1)));
                        for (var offset = 6; offset < row.Length; offset += 4)
                        {
                            pendingTypeSpecs.Push((
                                BinaryPrimitives.ReadUInt32LittleEndian(
                                    row[offset..]),
                                checked(pendingTypeSpec.Depth + 1)));
                        }

                        break;
                    }

                    case >= 0x06 and <= 0x0B:
                    case 0x0C:
                    case 0x0E:
                    {
                        var childOffset = reference.Form == 0x0C ? 3 : 2;
                        var child = BinaryPrimitives
                            .ReadUInt32LittleEndian(row[childOffset..]);
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

        DrainTypeSpecs();
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
                DrainTypeSpecs();
            }
        }
    }

    private static ImmutableArray<uint>
        GetC1M1ClassMethodConstraintTypeSpecSeeds(
            TrRmStructuralMetadataValidator.ValidatedExecutionTables tables,
            int moduleRow, IEnumerable<int> selectedTypeDefRows,
            bool usesAssociatedRelationConstraints)
    {
        var selectedTypes = selectedTypeDefRows.ToHashSet();
        var seeds = ImmutableArray.CreateBuilder<uint>();
        var (methodFirst, methodEnd)
            = tables.Modules.GetMethodDefRange(moduleRow);
        for (var methodRow = methodFirst; methodRow < methodEnd;
            methodRow++)
        {
            if (!selectedTypes.Contains(
                    tables.Methods.GetOwnerType(methodRow)))
            {
                continue;
            }

            var signature = tables.Methods.GetSignature(methodRow);
            var (formalFirst, formalCount)
                = tables.Signatures.GetGenericRange(signature);
            for (var formalOffset = 0; formalOffset < formalCount;
                formalOffset++)
            {
                var (constraintFirst, constraintCount)
                    = tables.GenericParams.GetConstraintRange(
                        formalFirst + formalOffset);
                for (var constraintOffset = 0;
                    constraintOffset < constraintCount;
                    constraintOffset++)
                {
                    var constraintRow
                        = constraintFirst + constraintOffset;
                    switch (tables.Constraints.GetKind(constraintRow))
                    {
                        case 1:
                            seeds.Add(tables.Constraints.GetPayload(
                                constraintRow));
                            break;

                        case 3 when usesAssociatedRelationConstraints:
                        {
                            var relationRow = checked((int)
                                tables.Constraints.GetPayload(
                                    constraintRow));
                            seeds.Add(tables.AssociatedRelations
                                .GetDeclaringInterfaceTypeSpecRef(
                                    relationRow));
                            seeds.Add(tables.AssociatedRelations
                                .GetRequiredTypeSpecRef(relationRow));
                            break;
                        }
                    }
                }
            }
        }

        return seeds.ToImmutable();
    }

    private static TrRmImportRow[] CreateC1M1ClassImports(
        SortedDictionary<int, BoundSourceTypeExpectation> bindingsByImportRow,
        SortedDictionary<int, TrRmStandardFieldProviderBinding>
            fieldBindingsByImportRow,
        SortedDictionary<int, BoundStandardClassMethodExpectation>
            methodBindingsByImportRow,
        SortedDictionary<int, int> moduleFunctionImportRowsByImportRow,
        Dictionary<int, int> importRowMap,
        Dictionary<int, int> moduleRefRowMap,
        Dictionary<int, int> typeDefRowMap,
        Dictionary<int, int> fieldRowMap,
        Dictionary<int, int> methodDefRowMap,
        Dictionary<string, int> stringRows,
        TrRmStructuralMetadataValidator.ValidatedExecutionTables sourceTables)
    {
        var rows = new TrRmImportRow[checked(bindingsByImportRow.Count
            + fieldBindingsByImportRow.Count
            + methodBindingsByImportRow.Count
            + moduleFunctionImportRowsByImportRow.Count)];
        foreach (var oldImportRow in bindingsByImportRow.Keys
            .Concat(fieldBindingsByImportRow.Keys)
            .Concat(methodBindingsByImportRow.Keys)
            .Concat(moduleFunctionImportRowsByImportRow.Keys).Order())
        {
            var newImportRow = importRowMap[oldImportRow];
            var oldModuleRefRow = sourceTables.Imports
                .GetModuleRefForImportRow(oldImportRow);
            var newModuleRefRow = moduleRefRowMap[oldModuleRefRow];
            if (bindingsByImportRow.TryGetValue(oldImportRow,
                    out var typeBinding))
            {
                var expectedOldTypeDefRow
                    = GetTypeDefRow(typeBinding.ExpectedDefinitionId);
                var expectedNewTypeDefRow
                    = typeDefRowMap[expectedOldTypeDefRow];
                rows[newImportRow - 1] = new TrRmImportRow(
                    newModuleRefRow,
                    stringRows[typeBinding.ExportKey],
                    0x1300_0000u | checked((uint)expectedNewTypeDefRow));
                continue;
            }

            if (fieldBindingsByImportRow.TryGetValue(oldImportRow,
                    out var fieldBinding))
            {
                rows[newImportRow - 1] = new TrRmImportRow(
                    newModuleRefRow,
                    stringRows[fieldBinding.ExportKey],
                    0x1400_0000u | checked((uint)fieldRowMap[
                        fieldBinding.ExpectedFieldDefRow]));
                continue;
            }

            if (methodBindingsByImportRow.TryGetValue(oldImportRow,
                    out var methodBinding))
            {
                var expectedOldMethodDefRow
                    = GetMethodDefRow(
                        methodBinding.ExpectedDefinitionId);
                rows[newImportRow - 1] = new TrRmImportRow(
                    newModuleRefRow,
                    stringRows[methodBinding.ExportKey],
                    0x1600_0000u | checked((uint)methodDefRowMap[
                        expectedOldMethodDefRow]));
                continue;
            }

            var expectedModuleFunctionMethodRow
                = moduleFunctionImportRowsByImportRow[oldImportRow];
            var moduleFunctionBinding = sourceTables.Imports
                .GetBindingForImportRow(oldImportRow);
            rows[newImportRow - 1] = new TrRmImportRow(
                newModuleRefRow,
                stringRows[moduleFunctionBinding.ExportKey],
                0x1600_0000u | checked((uint)methodDefRowMap[
                    expectedModuleFunctionMethodRow]));
        }

        return rows;
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
            || left.Layouts.Length != right.Layouts.Length
            || left.Methods.Length != right.Methods.Length)
        {
            return false;
        }

        for (var index = 0; index < left.Fields.Length; index++)
        {
            if (!StandardClassFieldsMatch(left.Fields[index],
                    right.Fields[index]))
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

        for (var index = 0; index < left.Methods.Length; index++)
        {
            if (!StandardClassMethodsMatch(left.Methods[index],
                    right.Methods[index]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool StandardClassFieldsMatch(
        BoundStandardClassFieldDeclaration left,
        BoundStandardClassFieldDeclaration right)
        => left.DefinitionId == right.DefinitionId
            && left.OwnerDefinitionId == right.OwnerDefinitionId
            && StringComparer.Ordinal.Equals(left.Name, right.Name)
            && left.Flags == right.Flags
            && left.TypeSpecReference == right.TypeSpecReference
            && left.Type == right.Type
            && left.ExportKeys.AsSpan().SequenceEqual(
                right.ExportKeys.AsSpan());

    private static bool StandardClassMethodsMatch(
        BoundStandardClassMethodDeclaration left,
        BoundStandardClassMethodDeclaration right)
    {
        if (left.DefinitionId != right.DefinitionId
            || !StringComparer.Ordinal.Equals(left.Name, right.Name)
            || left.Flags != right.Flags
            || left.EffectsMask != right.EffectsMask
            || left.ReturnTypeSpecReference
                != right.ReturnTypeSpecReference
            || left.ReceiverTypeSpecReference
                != right.ReceiverTypeSpecReference
            || left.GenericParameterCount != right.GenericParameterCount
            || left.BodyRow != right.BodyRow
            || left.ConstructionRole != right.ConstructionRole
            || left.InitializerMethodDefinitionId
                != right.InitializerMethodDefinitionId
            || left.ConstructionPropertyDefinitionId
                != right.ConstructionPropertyDefinitionId
            || !left.InitializedMemberDefinitionIds.AsSpan()
                .SequenceEqual(
                    right.InitializedMemberDefinitionIds.AsSpan())
            || !left.ExportKeys.AsSpan().SequenceEqual(
                right.ExportKeys.AsSpan())
            || !left.SelectedExportKeys.AsSpan().SequenceEqual(
                right.SelectedExportKeys.AsSpan())
            || !left.DependencyClauses.AsSpan().SequenceEqual(
                right.DependencyClauses.AsSpan())
            || left.Parameters.Length != right.Parameters.Length)
        {
            return false;
        }

        for (var index = 0; index < left.Parameters.Length; index++)
        {
            var leftParameter = left.Parameters[index];
            var rightParameter = right.Parameters[index];
            if (!StringComparer.Ordinal.Equals(leftParameter.Name,
                    rightParameter.Name)
                || leftParameter.TypeSpecReference
                    != rightParameter.TypeSpecReference
                || leftParameter.Mode != rightParameter.Mode
                || leftParameter.Flags != rightParameter.Flags
                || !Equals(leftParameter.DefaultValue,
                    rightParameter.DefaultValue))
            {
                return false;
            }
        }

        return true;
    }

    private static int GetMethodDefRow(uint definitionId)
    {
        if ((definitionId >> 24) != 0x16
            || (definitionId & 0x00FF_FFFF) == 0)
        {
            throw Invalid(
                "A C1M1 class reference has an invalid MethodDef identity.");
        }

        return checked((int)(definitionId & 0x00FF_FFFF));
    }

    private static bool TryGetC1M1ClassScalarType(
        TrRmStructuralMetadataValidator.ValidatedExecutionTables tables,
        uint typeSpecReference, out PrimitiveType type)
    {
        var typeSpec = tables.TypeSpecs.GetTypeSpecRef(typeSpecReference);
        type = PrimitiveType.Void;
        return typeSpec.Form == 1 && typeSpec.Row == 0
            && typeSpec.Qualifiers == 0
            && TrRmConstantSourceValueValidator.TryGetBoundScalarType(
                typeSpec.TypeCode, out type);
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
