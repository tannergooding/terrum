// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using Terrum.CodeAnalysis.Semantics;

namespace Terrum.CodeAnalysis.Modules;

public static partial class TrRmExecutableCompiler
{
    private readonly record struct StandardQueryTypeIdentity(
        string ModuleIdentity, uint DefinitionId);

    private readonly record struct StandardClassTypeOnlyInstanceKey(
        BoundGenericNominalTypeIdentity Definition, byte Qualifiers,
        uint TypeSpecReference, string? TypeSpecProviderIdentity,
        byte TypeSpecForm);

    private sealed class StandardQueryTypeViewGroup(
        BoundSourceTypeProviderSnapshot provider, uint definitionId,
        string name)
    {
        internal BoundSourceTypeProviderSnapshot Provider { get; } = provider;
        internal uint DefinitionId { get; } = definitionId;
        internal string Name { get; } = name;
        internal List<(int ReferenceIndex, int StructureIndex,
            BoundStruct Structure)> Views { get; } = [];
    }

    private readonly record struct StandardQueryProviderModuleSnapshot(
        string SchemaIdentity, ImmutableArray<byte> ArtifactSnapshot,
        int ModuleRow, string ModuleIdentity, uint GoverningRuleEdition,
        uint TargetRequirement, uint RuntimePolicy);

    /// <summary>Combines independently selected exact Standard type references.</summary>
    /// <param name="references">Two or more source references created by supported exact Standard reference APIs.
    /// The caller must not modify the list while it is captured.</param>
    /// <returns>A source reference with reconciled actual identities and retained provider views.</returns>
    /// <exception cref="ArgumentNullException">The reference list or an entry is null.</exception>
    /// <exception cref="ArgumentException">Fewer than two references were supplied.</exception>
    /// <exception cref="NotSupportedException">A reference uses an unsupported schema or type family.</exception>
    /// <exception cref="InvalidDataException">Provider snapshots, actual identities or declaration views conflict.</exception>
    public static CompilationReference CreateStandardReferenceSet(
        IReadOnlyList<CompilationReference> references)
    {
        ArgumentNullException.ThrowIfNull(references);
        if (references.Count < 2)
        {
            throw new ArgumentException(
                "A Standard reference set requires at least two selected references.",
                nameof(references));
        }

        var selectedReferences = new CompilationReference[references.Count];
        for (var index = 0; index < selectedReferences.Length; index++)
        {
            selectedReferences[index] = references[index]
                ?? throw new ArgumentException(
                    "A Standard reference set cannot contain null entries.",
                    nameof(references));
        }

        if (selectedReferences.Any(static reference =>
                !reference.StandardClassDeclarations.IsEmpty))
        {
            return CreateTypeOnlyClassReferenceSet(
                selectedReferences);
        }

        var runtimePolicy = selectedReferences[0].RuntimePolicy;
        var providerClosures
            = ImmutableArray.CreateBuilder<BoundStandardFieldProviderClosure>();
        foreach (var reference in selectedReferences)
        {
            if (reference.RuntimePolicy != runtimePolicy)
            {
                throw Invalid(
                    "Selected Standard references require different runtime policies.");
            }
            if (!reference.Functions.IsEmpty
                || !reference.Classes.IsEmpty
                || !reference.NativeFixedArrayTypes.IsEmpty
                || !reference.NativeFunctionPointerSignatures.IsEmpty
                || !reference.EnumDefinitions.IsEmpty
                || !reference.CheckedReferenceTypes.IsEmpty
                || !reference.Interfaces.IsEmpty
                || !reference.GenericNominalTypeDefinitions.IsEmpty
                || !reference.GenericNominalTypeInstances.IsEmpty
                || !reference.GenericMethodImports.IsEmpty
                || reference.Structures.IsEmpty
                || reference.StandardQuerySourceAssociation
                    is not BoundStandardQuerySourceAssociation association
                || association.ProviderClosures.IsDefaultOrEmpty)
            {
                throw Unsupported(
                    "Reference composition requires type-only Standard references with retained provider associations.");
            }
            if (!NativePointerTypeContract.IsValidTable(
                    reference.NativePointerTypes, reference.Structures, [],
                    reference.NativeFixedArrayTypes,
                    reference.CheckedReferenceTypes,
                    reference.EnumDefinitions))
            {
                throw Invalid(
                    "A selected Standard reference has an invalid native-pointer type table.");
            }

            providerClosures.AddRange(association.ProviderClosures);
        }

        if (providerClosures.IsDefaultOrEmpty
            || providerClosures[0] is null)
        {
            throw Unsupported(
                "Reference composition requires at least one retained provider closure.");
        }
        var sourceSchemaIdentity
            = providerClosures[0].SchemaIdentity;
        var isLayoutQueriesReference
            = StringComparer.Ordinal.Equals(sourceSchemaIdentity,
                StandardLayoutQueriesSchemaIdentity);
        var isNestedFieldPathReference
            = StringComparer.Ordinal.Equals(sourceSchemaIdentity,
                StandardInitReferenceBodiesNestedFieldDestinationsSchemaIdentity);
        var isClassInstanceLayoutReference
            = IsSelectedC1ClassInstanceLayoutSchemaIdentity(
                sourceSchemaIdentity);
        if (!isLayoutQueriesReference && !isNestedFieldPathReference
            && !isClassInstanceLayoutReference)
        {
            throw Unsupported(
                "Reference composition requires an exact LayoutQueries, nested FieldPath or class-instance-layout selection.");
        }

        var providerModules = ValidateStandardQueryProviderClosures(
            providerClosures.ToImmutable());
        var groups = new List<StandardQueryTypeViewGroup>();
        var groupByIdentity = new Dictionary<StandardQueryTypeIdentity, int>();
        var structureMaps = new int[selectedReferences.Length][];
        var pointerMaps = new int[selectedReferences.Length][];
        var pointerStates = new byte[selectedReferences.Length][];
        var totalPointerCount = 0;
        for (var referenceIndex = 0;
            referenceIndex < selectedReferences.Length; referenceIndex++)
        {
            var pointerCount = selectedReferences[referenceIndex]
                .NativePointerTypes.Length;
            if (pointerCount
                > SemanticVerifier.MaximumAnalysisVisits - totalPointerCount)
            {
                throw new NotSupportedException(
                    "The composed Standard native-pointer closure exceeds the analysis limit.");
            }
            totalPointerCount += pointerCount;
            pointerMaps[referenceIndex] = new int[pointerCount];
            Array.Fill(pointerMaps[referenceIndex], -1);
            pointerStates[referenceIndex] = new byte[pointerCount];
        }

        for (var referenceIndex = 0;
            referenceIndex < selectedReferences.Length; referenceIndex++)
        {
            var reference = selectedReferences[referenceIndex];
            var structures = reference.Structures;
            var map = new int[structures.Length];
            structureMaps[referenceIndex] = map;

            for (var structureIndex = 0;
                structureIndex < structures.Length; structureIndex++)
            {
                var structure = structures[structureIndex];
                var origin = structure.StandardTypeOrigin;
                if (structure.StandardLayoutOnly
                        != isLayoutQueriesReference
                    || origin is null
                    || origin.DefinitionId >> 24 != 0x13
                    || !StringComparer.Ordinal.Equals(
                        origin.Provider.SchemaIdentity,
                        sourceSchemaIdentity)
                    || origin.ExportKeys.IsDefaultOrEmpty
                    || origin.ExpectedDeclaration is null)
                {
                    throw Unsupported(
                        "A selected reference contains a type without a complete TypeDef origin for its exact schema.");
                }

                ValidateStandardQueryProviderSnapshot(origin.Provider,
                    providerModules);
                var identity = new StandardQueryTypeIdentity(
                    origin.Provider.ModuleIdentity, origin.DefinitionId);
                if (!groupByIdentity.TryGetValue(identity,
                        out var groupIndex))
                {
                    groupIndex = groups.Count;
                    groupByIdentity.Add(identity, groupIndex);
                    groups.Add(new StandardQueryTypeViewGroup(
                        origin.Provider, origin.DefinitionId,
                        structure.Name));
                }
                else
                {
                    var existingGroup = groups[groupIndex];
                    if (!SameA2SourceTypeProviderSnapshot(
                            existingGroup.Provider, origin.Provider)
                        || !StringComparer.Ordinal.Equals(
                            existingGroup.Name, structure.Name))
                    {
                        throw Invalid(
                            "One actual Standard TypeDef has conflicting provider identity or declaration spelling.");
                    }
                }

                groups[groupIndex].Views.Add((referenceIndex,
                    structureIndex, structure));
                map[structureIndex] = groupIndex;
            }
        }

        PrimitiveType RemapType(int referenceIndex, PrimitiveType type)
        {
            if (BoundEnumTypes.IsType(type)
                || NativeFixedArrayTypes.IsType(type)
                || NativeFunctionPointerTypes.IsType(type)
                || ManagedClassTypes.IsClass(type))
            {
                throw Unsupported(
                    "The selected Standard reference set contains an unsupported type family.");
            }

            if (NativePointerTypes.IsType(type))
            {
                return NativePointerTypes.Type(RemapPointerType(
                    referenceIndex, NativePointerTypes.Index(type)));
            }

            var structureIndex = (int)type - 100;
            if (structureIndex < 0
                || structureIndex
                    >= selectedReferences[referenceIndex].Structures.Length)
            {
                return type;
            }

            return (PrimitiveType)(100
                + structureMaps[referenceIndex][structureIndex]);
        }

        var mergedPointerTypes
            = ImmutableArray.CreateBuilder<NativePointerTypeDefinition>();
        var pointerIndexes = new Dictionary<
            (PrimitiveType PointeeType, bool IsWritable), int>();
        var remainingPointerVisits = SemanticVerifier.MaximumAnalysisVisits;
        int RemapPointerType(int referenceIndex, int pointerIndex)
        {
            var sourcePointerTypes
                = selectedReferences[referenceIndex].NativePointerTypes;
            if ((uint)pointerIndex >= (uint)sourcePointerTypes.Length)
            {
                throw Invalid(
                    "A selected Standard reference contains an invalid native-pointer type index.");
            }
            if (pointerMaps[referenceIndex][pointerIndex] >= 0)
            {
                return pointerMaps[referenceIndex][pointerIndex];
            }
            if (pointerStates[referenceIndex][pointerIndex] == 1)
            {
                throw Invalid(
                    "A selected Standard reference contains a recursive native-pointer type.");
            }
            if (--remainingPointerVisits < 0)
            {
                throw new NotSupportedException(
                    "The composed Standard native-pointer closure exceeds the analysis limit.");
            }

            pointerStates[referenceIndex][pointerIndex] = 1;
            var sourcePointer = sourcePointerTypes[pointerIndex];
            var pointeeType = RemapType(referenceIndex,
                sourcePointer.PointeeType);
            var key = (pointeeType, sourcePointer.IsWritable);
            if (!pointerIndexes.TryGetValue(key, out var mappedIndex))
            {
                if (mergedPointerTypes.Count
                    >= SemanticVerifier.MaximumAnalysisVisits)
                {
                    throw new NotSupportedException(
                        "The composed Standard native-pointer table exceeds the analysis limit.");
                }
                mappedIndex = mergedPointerTypes.Count;
                pointerIndexes.Add(key, mappedIndex);
                mergedPointerTypes.Add(new NativePointerTypeDefinition(
                    pointeeType, sourcePointer.IsWritable));
            }

            pointerMaps[referenceIndex][pointerIndex] = mappedIndex;
            pointerStates[referenceIndex][pointerIndex] = 2;
            return mappedIndex;
        }

        for (var referenceIndex = 0;
            referenceIndex < selectedReferences.Length; referenceIndex++)
        {
            for (var pointerIndex = 0;
                pointerIndex < pointerMaps[referenceIndex].Length;
                pointerIndex++)
            {
                _ = RemapPointerType(referenceIndex, pointerIndex);
            }
        }

        var pointerTypesResult = mergedPointerTypes.ToImmutable();
        var mergedStructures = ImmutableArray.CreateBuilder<BoundStruct>(
            groups.Count);
        foreach (var group in groups)
        {
            var representative = group.Views.FirstOrDefault(
                static view => view.Structure.StandardTypeOrigin!
                    .IsLookupRoot);
            if (representative.Structure is null)
            {
                representative = group.Views[0];
            }

            var referenceIndex = representative.ReferenceIndex;
            var sourceStructure = representative.Structure;
            var typeOrigins = group.Views.Select(
                    static view => view.Structure.StandardTypeOrigin!)
                .ToArray();
            var selectedTypeKeys = new List<string>();
            var allTypeKeys = new List<string>();
            var expectations
                = ImmutableArray.CreateBuilder<BoundSourceTypeExpectation>();
            var expectedTypeViews = new List<(int ReferenceIndex,
                BoundStruct Declaration)>();
            foreach (var (viewReferenceIndex, _, viewStructure)
                in group.Views)
            {
                var origin = viewStructure.StandardTypeOrigin!;
                AddDistinct(allTypeKeys, origin.ExportKeys);
                var selectedKeys = origin.SelectedExportKeys.IsDefault
                    ? origin.IsLookupRoot ? origin.ExportKeys : []
                    : origin.SelectedExportKeys;
                AddDistinct(selectedTypeKeys, selectedKeys);
                if (!origin.Expectations.IsDefaultOrEmpty)
                {
                    expectations.AddRange(origin.Expectations);
                }
                if (origin.ExpectedDeclaration is BoundStruct expected)
                {
                    expectedTypeViews.Add((viewReferenceIndex, expected));
                }
                foreach (var additionalExpected in
                    origin.AdditionalExpectedDeclarations.IsDefaultOrEmpty
                        ? [] : origin.AdditionalExpectedDeclarations)
                {
                    expectedTypeViews.Add((viewReferenceIndex,
                        additionalExpected));
                }
            }

            if (expectedTypeViews.Count == 0)
            {
                throw Unsupported(
                    "A composed Standard TypeDef has no retained expected declaration view.");
            }

            var expectedTypeDeclarations = new List<BoundStruct>(
                expectedTypeViews.Count);
            foreach (var (viewReferenceIndex, expected) in expectedTypeViews)
            {
                var remappedExpected = BoundTreeTraversal.RewriteTypes(
                    expected, type => RemapType(viewReferenceIndex, type));
                expectedTypeDeclarations.Add(remappedExpected with {
                    StandardTypeOrigin = null,
                    StandardLayoutOnly = false,
                    Fields = [.. remappedExpected.Fields.Select(
                        static field => field with {
                            StandardFieldOrigin = null,
                        })],
                });
            }

            var mergedFields = ImmutableArray.CreateBuilder<BoundStructField>(
                sourceStructure.Fields.Length);
            for (var fieldOrdinal = 0;
                fieldOrdinal < sourceStructure.Fields.Length; fieldOrdinal++)
            {
                var sourceField = sourceStructure.Fields[fieldOrdinal];
                var mappedFieldType = RemapType(referenceIndex,
                    sourceField.Type);
                var fieldViews = group.Views.Select(view =>
                    (view.ReferenceIndex,
                        Field: view.Structure.Fields[fieldOrdinal])).ToArray();
                if (fieldViews.Any(view =>
                        !StringComparer.Ordinal.Equals(
                            view.Field.Name, sourceField.Name)
                        || RemapType(view.ReferenceIndex,
                                view.Field.Type) != mappedFieldType
                        || view.Field.Offset != sourceField.Offset
                        || view.Field.Size != sourceField.Size
                        || view.Field.IsMutable != sourceField.IsMutable
                        || view.Field.IsPrivate != sourceField.IsPrivate
                        || view.Field.IsInternal != sourceField.IsInternal
                        || view.Field.RequiresUnsafeCaller
                            != sourceField.RequiresUnsafeCaller))
                {
                    throw Invalid(
                        "Views of one actual Standard TypeDef disagree on a field declaration.");
                }

                var fieldOrigins = fieldViews
                    .Select(static view => view.Field.StandardFieldOrigin)
                    .Where(static origin => origin is not null)
                    .Cast<BoundStandardFieldOrigin>()
                    .ToArray();
                BoundStandardFieldOrigin? mergedFieldOrigin = null;
                if (fieldOrigins.Length != 0)
                {
                    var provider = group.Provider;
                    var definitionId = fieldOrigins[0].DefinitionId;
                    var fieldKeys = new List<string>();
                    var selectedFieldKeys = new List<string>();
                    var expectedFieldViews
                        = new List<(int ReferenceIndex,
                            BoundStructField Declaration)>();
                    foreach (var (viewReferenceIndex, viewField) in fieldViews)
                    {
                        if (viewField.StandardFieldOrigin
                            is not BoundStandardFieldOrigin fieldOrigin)
                        {
                            continue;
                        }

                        if (fieldOrigin.DefinitionId != definitionId
                            || !SameA2SourceTypeProviderSnapshot(
                                fieldOrigin.Provider, provider)
                            || fieldOrigin.ExpectedDeclaration is null)
                        {
                            throw Invalid(
                                "Views of one actual FieldDef have conflicting provider origins.");
                        }

                        ValidateStandardQueryProviderSnapshot(
                            fieldOrigin.Provider, providerModules);
                        AddDistinct(fieldKeys, fieldOrigin.ExportKeys);
                        var selectedKeys
                            = fieldOrigin.SelectedExportKeys.IsDefault
                                ? fieldOrigin.IsLookupRoot
                                    ? fieldOrigin.ExportKeys : []
                                : fieldOrigin.SelectedExportKeys;
                        AddDistinct(selectedFieldKeys, selectedKeys);
                        expectedFieldViews.Add((viewReferenceIndex,
                            fieldOrigin.ExpectedDeclaration));
                        foreach (var expected in
                            fieldOrigin.AdditionalExpectedDeclarations
                                .IsDefaultOrEmpty
                                ? [] : fieldOrigin.AdditionalExpectedDeclarations)
                        {
                            expectedFieldViews.Add((viewReferenceIndex,
                                expected));
                        }
                    }

                    var expectedFields = new List<BoundStructField>(
                        expectedFieldViews.Count);
                    foreach (var (viewReferenceIndex, expected) in
                        expectedFieldViews)
                    {
                        expectedFields.Add(expected with {
                            Type = RemapType(viewReferenceIndex,
                                expected.Type),
                            StandardFieldOrigin = null,
                        });
                    }

                    mergedFieldOrigin = new BoundStandardFieldOrigin(
                        provider, definitionId, [.. fieldKeys],
                        fieldOrigins.Any(static origin =>
                            origin.IsLookupRoot),
                        expectedFields.Count == 0
                            ? null : expectedFields[0]) {
                        SelectedExportKeys = [.. selectedFieldKeys],
                        AdditionalExpectedDeclarations
                            = expectedFields.Count <= 1 ? []
                            : [.. expectedFields.Skip(1)],
                    };
                }

                mergedFields.Add(sourceField with {
                    Type = mappedFieldType,
                    StandardFieldOrigin = mergedFieldOrigin,
                });
            }

            var mergedExpected = expectedTypeDeclarations[0];
            var mergedOrigin = new BoundStandardTypeOrigin(
                group.Provider, group.DefinitionId, [.. allTypeKeys],
                typeOrigins.Any(static origin => origin.IsLookupRoot),
                expectations.ToImmutable(),
                ReferenceRoot: typeOrigins[0].ReferenceRoot
                    ?? group.Provider,
                ExpectedDeclaration: mergedExpected,
                ExpectedFixedArrayTypes:
                    typeOrigins[0].ExpectedFixedArrayTypes,
                ExpectedNativePointerTypes: pointerTypesResult) {
                SelectedExportKeys = [.. selectedTypeKeys],
                AdditionalExpectedDeclarations
                    = expectedTypeDeclarations.Count <= 1 ? []
                    : [.. expectedTypeDeclarations.Skip(1)],
            };
            mergedStructures.Add(sourceStructure with {
                Fields = mergedFields.MoveToImmutable(),
                StandardTypeOrigin = mergedOrigin,
                StandardLayoutOnly = isLayoutQueriesReference,
            });
        }

        var structuresResult = mergedStructures.MoveToImmutable();
        if (!NativePointerTypeContract.IsValidTable(pointerTypesResult,
                structuresResult, []))
        {
            throw Invalid(
                "The composed Standard native-pointer type table is invalid.");
        }
        var mergedProgram = new BoundProgram([], -1) {
            RuntimePolicy = runtimePolicy,
            Structures = structuresResult,
            NativePointerTypes = pointerTypesResult,
            StandardQuerySourceAssociation
                = new BoundStandardQuerySourceAssociation(
                    providerClosures.ToImmutable()),
        };
        SemanticVerifier.VerifyLibraryReferences(mergedProgram);
        var (evaluationFormSelections, evaluationFormProviders)
            = ModuleCompiler.MergeCoreEvaluationFormContexts(
                selectedReferences);
        return new CompilationReference(structuresResult, [],
            runtimePolicy,
            nativePointerTypes: pointerTypesResult,
            standardQuerySourceAssociation:
                mergedProgram.StandardQuerySourceAssociation,
            coreEvaluationFormIdentitySelections:
                evaluationFormSelections,
            coreEvaluationFormSchemaProviders:
                evaluationFormProviders);
    }

    private static CompilationReference
        CreateTypeOnlyClassReferenceSet(
            CompilationReference[] references)
    {
        var runtimePolicy = references[0].RuntimePolicy;
        if (references[0].StandardClassDeclarations.IsEmpty)
        {
            throw Unsupported(
                "A class reference set cannot mix Standard class and non-class references.");
        }
        var classSchemaIdentity = references[0]
            .StandardClassDeclarations[0].Declaration.Provider.SchemaIdentity;
        if (!IsSupportedClassTypeOnlySchema(classSchemaIdentity))
        {
            throw Unsupported(
                "The selected Standard class schema is not supported by the bounded type-only composer.");
        }

        var selectedClassIdentities = references
            .SelectMany(static reference =>
                reference.StandardClassDeclarations)
            .Where(static declaration =>
                !declaration.SelectedExportKeys.IsDefaultOrEmpty)
            .Select(static declaration =>
                declaration.Declaration.Identity)
            .ToHashSet();
        var classHandleTypeDependencies
            = new HashSet<BoundGenericNominalTypeIdentity>();
        var classTypeBindingEqualityEndpointsByActualType
            = new Dictionary<BoundGenericNominalTypeIdentity,
                List<BoundStandardClassTypeDefBindingEqualityEndpoint>>();
        var createsClassTypeBindingEquality
            = StandardGenericMethodSchema.IsClassTypeBindingViews(
                classSchemaIdentity);
        void AddClassTypeBindingEqualityEndpoint(
            BoundGenericNominalTypeIdentity actualIdentity,
            BoundStandardClassTypeDefBindingEqualityEndpoint endpoint)
        {
            if (!createsClassTypeBindingEquality)
            {
                return;
            }

            if (!classTypeBindingEqualityEndpointsByActualType.TryGetValue(
                    actualIdentity, out var endpoints))
            {
                endpoints = [];
                classTypeBindingEqualityEndpointsByActualType.Add(
                    actualIdentity, endpoints);
            }

            if (!endpoints.Any(candidate =>
                    candidate.TypeDefDefinitionId
                        == endpoint.TypeDefDefinitionId
                    && candidate.ImportRow == endpoint.ImportRow
                    && StringComparer.Ordinal.Equals(candidate.ExportKey,
                        endpoint.ExportKey)
                    && SameA2SourceTypeProviderSnapshot(
                        candidate.OwnerModule, endpoint.OwnerModule)))
            {
                endpoints.Add(endpoint);
            }
        }

        for (var referenceIndex = 0;
            referenceIndex < references.Length; referenceIndex++)
        {
            var reference = references[referenceIndex];
            foreach (var declaration in reference.StandardClassDeclarations)
            {
                if (!declaration.SelectedExportKeys.IsDefaultOrEmpty)
                {
                    AddClassTypeBindingEqualityEndpoint(
                        declaration.Declaration.Identity,
                        new BoundStandardClassTypeDefBindingEqualityEndpoint(
                            declaration.Declaration.Provider,
                            declaration.Declaration.Identity.DefinitionId,
                            0, null));
                }

                foreach (var group
                    in declaration.TypeDefBindingEqualityGroups)
                {
                    foreach (var endpoint in group.Endpoints)
                    {
                        AddClassTypeBindingEqualityEndpoint(
                            declaration.Declaration.Identity, endpoint);
                    }
                }
            }

            var pendingClassIdentities
                = new Queue<BoundGenericNominalTypeIdentity>(
                    reference.StandardClassDeclarations
                        .Where(static declaration =>
                            !declaration.SelectedExportKeys
                                .IsDefaultOrEmpty)
                        .Select(static declaration =>
                            declaration.Declaration.Identity));
            var visitedClassIdentities
                = new HashSet<BoundGenericNominalTypeIdentity>();
            var visitedStructureIndexes = new HashSet<int>();

            void IncludeClassHandleType(PrimitiveType type)
            {
                if (BoundGenericNominalTypeTypes.IsType(type))
                {
                    var instanceIndex
                        = BoundGenericNominalTypeTypes.Index(type);
                    if ((uint)instanceIndex
                        >= (uint)reference.GenericNominalTypeInstances.Length)
                    {
                        throw Invalid(
                            "A selected class or struct FieldDef has an invalid nominal type instance.");
                    }

                    var instance
                        = reference.GenericNominalTypeInstances[
                            instanceIndex];
                    var definition
                        = reference.GenericNominalTypeDefinitions
                            .FirstOrDefault(candidate =>
                                candidate.Identity
                                == instance.Definition);
                    if (definition is null)
                    {
                        throw Invalid(
                            "A selected class or struct FieldDef has no nominal TypeDef definition.");
                    }

                    if (definition.NominalKind == 2)
                    {
                        _ = classHandleTypeDependencies.Add(
                            definition.Identity);
                        pendingClassIdentities.Enqueue(
                            definition.Identity);

                        var classDeclaration
                            = reference.StandardClassDeclarations
                                .SingleOrDefault(candidate =>
                                    candidate.Declaration.Identity
                                        == definition.Identity)
                            ?? throw Invalid(
                                "A selected class handle field lost its declaration view.");
                        if (instance.TypeSpecForm == 0x03
                            && instance.TypeSpecReference != 0
                            && instance.TypeSpecProviderIdentity
                                is string usingModuleIdentity)
                        {
                            var importRow = checked((int)
                                instance.TypeSpecReference);
                            var expectation
                                = classDeclaration.Expectations
                                    .SingleOrDefault(candidate =>
                                        candidate.Binding.ImportRow
                                            == importRow
                                        && StringComparer.Ordinal.Equals(
                                            candidate.Binding.UsingModule
                                                .ModuleIdentity,
                                            usingModuleIdentity)
                                        && candidate.Binding
                                            .ExpectedDefinitionId
                                            == definition.Identity
                                                .DefinitionId);
                            if (expectation is not null)
                            {
                                AddClassTypeBindingEqualityEndpoint(
                                    definition.Identity,
                                    new BoundStandardClassTypeDefBindingEqualityEndpoint(
                                        expectation.Binding.UsingModule,
                                        expectation.Binding
                                            .ExpectedDefinitionId,
                                        expectation.Binding.ImportRow,
                                        expectation.Binding.ExportKey));
                            }
                        }
                        else if (instance.TypeSpecForm is 0 or 0x02)
                        {
                            AddClassTypeBindingEqualityEndpoint(
                                definition.Identity,
                                new BoundStandardClassTypeDefBindingEqualityEndpoint(
                                    classDeclaration.Declaration.Provider,
                                    definition.Identity.DefinitionId, 0,
                                    null));
                        }
                    }

                    return;
                }

                if (BoundEnumTypes.IsType(type)
                    || BoundSpanTypeTypes.IsType(type)
                    || ManagedClassTypes.IsClass(type)
                    || NativePointerTypes.IsType(type)
                    || NativeFixedArrayTypes.IsType(type)
                    || NativeFunctionPointerTypes.IsType(type)
                    || CheckedReferenceTypes.IsType(type))
                {
                    return;
                }

                var structureIndex = (int)type - 100;
                if ((uint)structureIndex >= (uint)reference.Structures.Length
                    || !visitedStructureIndexes.Add(structureIndex))
                {
                    return;
                }

                foreach (var field in reference.Structures[
                    structureIndex].Fields)
                {
                    IncludeClassHandleType(field.Type);
                }
            }

            while (pendingClassIdentities.TryDequeue(
                out var classIdentity))
            {
                if (!visitedClassIdentities.Add(classIdentity))
                {
                    continue;
                }

                var declaration
                    = reference.StandardClassDeclarations.SingleOrDefault(
                        candidate =>
                            candidate.Declaration.Identity == classIdentity)
                        ?? throw Invalid(
                            "A selected class handle dependency has no provider declaration view.");
                foreach (var field in declaration.Declaration.Fields)
                {
                    IncludeClassHandleType(field.Type);
                }
            }
        }

        var structureReferences = new List<CompilationReference>();
        var structureReferenceIndexes = new List<int>();
        for (var referenceIndex = 0;
            referenceIndex < references.Length; referenceIndex++)
        {
            var reference = references[referenceIndex];
            if (reference.Structures.IsEmpty)
            {
                continue;
            }

            if (!IsSelectedC1ClassInstanceLayoutSchemaIdentity(
                    classSchemaIdentity)
                || reference.StandardClassDeclarations.IsEmpty
                || !StringComparer.Ordinal.Equals(
                    reference.StandardClassDeclarations[0]
                        .Declaration.Provider.SchemaIdentity,
                    classSchemaIdentity))
            {
                throw Unsupported(
                    "Class layout composition with inline struct dependencies requires the exact class-instance-layout schema.");
            }

            structureReferenceIndexes.Add(referenceIndex);
            structureReferences.Add(new CompilationReference(
                reference.Structures, [], runtimePolicy,
                nativePointerTypes: reference.NativePointerTypes,
                standardQuerySourceAssociation:
                    reference.StandardQuerySourceAssociation));
        }

        bool IsSelectedClassHandleInStructure(int referenceIndex,
            BoundSourceTypeProviderSnapshot ownerProvider,
            PrimitiveType type)
        {
            if (!BoundGenericNominalTypeTypes.IsType(type))
            {
                return false;
            }

            var reference = references[referenceIndex];
            var instanceIndex = BoundGenericNominalTypeTypes.Index(type);
            if ((uint)instanceIndex
                >= (uint)reference.GenericNominalTypeInstances.Length)
            {
                return false;
            }

            var instance
                = reference.GenericNominalTypeInstances[instanceIndex];
            var definition = reference.GenericNominalTypeDefinitions
                .FirstOrDefault(candidate =>
                    candidate.Identity == instance.Definition);
            var declaration = reference.StandardClassDeclarations
                .SingleOrDefault(candidate =>
                    candidate.Declaration.Identity == instance.Definition);
            if (definition is null || definition.NominalKind != 2
                || declaration is null
                || (!selectedClassIdentities.Contains(instance.Definition)
                    && !classHandleTypeDependencies.Contains(
                        instance.Definition))
                || !instance.Arguments.IsEmpty
                || instance.Qualifiers != 0
                || !StringComparer.Ordinal.Equals(
                    instance.TypeSpecProviderIdentity,
                    ownerProvider.ModuleIdentity))
            {
                return false;
            }

            var classView = declaration.Declaration;
            if (!StringComparer.Ordinal.Equals(
                    classView.Identity.ProviderIdentity,
                    classView.Provider.ModuleIdentity)
                || !StringComparer.Ordinal.Equals(
                    classView.Identity.ModuleIdentity,
                    classView.Provider.ModuleIdentity))
            {
                return false;
            }

            if (instance.TypeSpecForm == 0x02)
            {
                return true;
            }

            return instance.TypeSpecForm == 0x03
                && declaration.Expectations.Any(expectation =>
                    StringComparer.Ordinal.Equals(
                        expectation.Binding.UsingModule.ModuleIdentity,
                        ownerProvider.ModuleIdentity)
                    && SameA2SourceTypeProviderSnapshot(
                        expectation.Binding.UsingModule, ownerProvider)
                    && SameA2SourceTypeProviderSnapshot(
                        expectation.Binding.Provider, classView.Provider)
                    && expectation.Binding.ProviderDefinitionId
                        == instance.Definition.DefinitionId
                    && definition.Provider is
                        BoundSourceTypeProviderSnapshot provider
                    && SameA2SourceTypeProviderSnapshot(provider,
                        classView.Provider)
                    && !definition.ProviderExpectations.IsDefaultOrEmpty
                    && definition.ProviderExpectations.Any(
                        typeExpectation =>
                            BoundGenericNominalTypeContract
                                .AreEquivalentProviderExpectations(
                                    typeExpectation,
                                    expectation.Binding)));
        }

        bool IsDirectStructureFieldType(int referenceIndex,
            BoundSourceTypeProviderSnapshot ownerProvider,
            PrimitiveType type)
        {
            if (BoundEnumTypes.IsType(type))
            {
                var enumIndex = BoundEnumTypes.Index(type);
                var enumDefinitions
                    = references[referenceIndex].EnumDefinitions;
                if ((uint)enumIndex >= (uint)enumDefinitions.Length
                    || enumDefinitions[enumIndex].ModuleImport is not null
                    || enumDefinitions[enumIndex].Identity
                        is not BoundEnumDeclarationIdentity.Standard standard
                    || !BoundStandardEnumDeclarationContract
                        .IsValidDefinition(standard.Value,
                            enumDefinitions[enumIndex]))
                {
                    return false;
                }

                var occurrence = standard.Value;
                var provider = occurrence.Declaration.Site.Snapshot;
                return occurrence.IsLookupRoot
                    ? occurrence.Expectations.IsEmpty
                        && SameA2SourceTypeProviderSnapshot(ownerProvider,
                            provider)
                    : occurrence.SelectedExportKeys.IsEmpty
                        && !occurrence.Expectations.IsDefaultOrEmpty
                        && occurrence.Expectations.All(expectation =>
                            SameA2SourceTypeProviderSnapshot(ownerProvider,
                                expectation.Binding.UsingModule)
                            && SameA2SourceTypeProviderSnapshot(provider,
                                expectation.Binding.Provider)
                            && expectation.Binding.ProviderDefinitionId
                                == occurrence.Declaration.Site.DefinitionId);
            }

            if (BoundGenericNominalTypeTypes.IsType(type))
            {
                return IsSelectedClassHandleInStructure(referenceIndex,
                    ownerProvider, type);
            }

            if (BoundSpanTypeTypes.IsType(type)
                || ManagedClassTypes.IsClass(type)
                || NativePointerTypes.IsType(type)
                || NativeFixedArrayTypes.IsType(type)
                || NativeFunctionPointerTypes.IsType(type)
                || CheckedReferenceTypes.IsType(type))
            {
                return false;
            }

            var structureIndex = (int)type - 100;
            if (structureIndex < 0)
            {
                return IsStandardClassTypeOnlyComposedFieldType(type);
            }

            var sourceStructures
                = references[referenceIndex].Structures;
            return (uint)structureIndex < (uint)sourceStructures.Length
                && sourceStructures[structureIndex].StandardTypeOrigin
                    is BoundStandardTypeOrigin nestedOrigin
                && SameA2SourceTypeProviderSnapshot(ownerProvider,
                    nestedOrigin.Provider);
        }

        var directStructureComposition = structureReferences.Count != 0;
        for (var referencePosition = 0;
            directStructureComposition
                && referencePosition < structureReferences.Count;
            referencePosition++)
        {
            var structureReference = structureReferences[referencePosition];
            var referenceIndex
                = structureReferenceIndexes[referencePosition];
            if (!structureReference.NativePointerTypes.IsEmpty
                || structureReference.StandardQuerySourceAssociation
                    is not null)
            {
                directStructureComposition = false;
                break;
            }

            foreach (var structure in structureReference.Structures)
            {
                if (structure.StandardTypeOrigin is not
                        BoundStandardTypeOrigin origin
                    || !origin.Expectations.IsDefaultOrEmpty
                    || !origin.IsLookupRoot
                    || origin.SelectedExportKeys.IsDefaultOrEmpty
                    || origin.ExpectedDeclaration is null
                    || !origin.AdditionalExpectedDeclarations.IsDefaultOrEmpty
                    || !StringComparer.Ordinal.Equals(
                        origin.Provider.SchemaIdentity,
                        classSchemaIdentity)
                    || structure.Fields.Any(field =>
                        field.StandardFieldOrigin is not null
                        && (field.StandardFieldOrigin
                                .ExpectedDeclaration is null
                            || !field.StandardFieldOrigin
                                .AdditionalExpectedDeclarations
                                .IsDefaultOrEmpty))
                    || structure.Fields.Any(field =>
                        !IsDirectStructureFieldType(referenceIndex,
                            origin.Provider, field.Type)))
                {
                    directStructureComposition = false;
                    break;
                }
            }
        }
        if (structureReferences.Count > 1
            && !directStructureComposition
            && structureReferences.Any(static reference =>
                reference.StandardQuerySourceAssociation is null))
        {
            throw Unsupported(
                "Multi-provider inline struct composition requires retained provider closures for every structure reference.");
        }

        CompilationReference? structureReferenceSet = null;
        var directStructureCompositionSources
            = new List<(int ReferenceIndex, int StructureIndex)>();
        ImmutableArray<BoundStruct> structuresResult;
        ImmutableArray<NativePointerTypeDefinition>
            nativePointerTypesResult;
        BoundStandardQuerySourceAssociation?
            standardQuerySourceAssociation;
        if (directStructureComposition)
        {
            var mergedDirectStructures
                = ImmutableArray.CreateBuilder<BoundStruct>();
            var directStructureIndexes = new Dictionary<(
                string ModuleIdentity, uint DefinitionId), int>();
            for (var referencePosition = 0;
                referencePosition < structureReferences.Count;
                referencePosition++)
            {
                var structureReference
                    = structureReferences[referencePosition];
                var referenceIndex
                    = structureReferenceIndexes[referencePosition];
                for (var structureIndex = 0;
                    structureIndex < structureReference.Structures.Length;
                    structureIndex++)
                {
                    var structure
                        = structureReference.Structures[structureIndex];
                    var origin = structure.StandardTypeOrigin!;
                    var key = (origin.Provider.ModuleIdentity,
                        origin.DefinitionId);
                    if (!directStructureIndexes.TryAdd(key,
                            mergedDirectStructures.Count))
                    {
                        throw Unsupported(
                            "Direct inline-struct composition currently requires distinct provider TypeDefs.");
                    }

                    mergedDirectStructures.Add(structure);
                    directStructureCompositionSources.Add((referenceIndex,
                        structureIndex));
                }
            }

            structuresResult = mergedDirectStructures.MoveToImmutable();
            nativePointerTypesResult = [];
            standardQuerySourceAssociation = null;
        }
        else
        {
            structureReferenceSet = structureReferences.Count switch {
                0 => null,
                1 => structureReferences[0],
                _ => CreateStandardReferenceSet(structureReferences),
            };
            structuresResult = structureReferenceSet?.Structures ?? [];
            nativePointerTypesResult
                = structureReferenceSet?.NativePointerTypes ?? [];
            standardQuerySourceAssociation
                = structureReferenceSet?.StandardQuerySourceAssociation;
        }
        if (structureReferences.Count > 1
            && !directStructureComposition)
        {
            if (structureReferences.Any(static reference =>
                    reference.Structures.Any(static structure =>
                        structure.Fields.Any(static field =>
                            BoundEnumTypes.IsType(field.Type)))))
            {
                throw Unsupported(
                    "Multi-provider imported struct composition does not yet admit inline enum fields.");
            }

            if (isClassInstanceLayoutReference)
            {
                structuresResult = [.. structuresResult.Select(
                    static structure => structure with {
                        StandardLayoutOnly = true,
                    })];
            }
        }
        var structureIndexByProviderIdentity = new Dictionary<(
            string ModuleIdentity, uint DefinitionId), int>();
        var structureDefinitionIdentities
            = new HashSet<BoundGenericNominalTypeIdentity>();
        for (var structureIndex = 0;
            structureIndex < structuresResult.Length; structureIndex++)
        {
            var origin = structuresResult[structureIndex]
                .StandardTypeOrigin
                ?? throw Invalid(
                    "A composed class layout structure lost its provider origin.");
            var key = (origin.Provider.ModuleIdentity,
                origin.DefinitionId);
            if (!structureIndexByProviderIdentity.TryAdd(key,
                    structureIndex)
                || !structureDefinitionIdentities.Add(
                    new BoundGenericNominalTypeIdentity(
                        origin.Provider.ModuleIdentity,
                        origin.Provider.ModuleIdentity,
                        origin.DefinitionId)))
            {
                throw Invalid(
                    "A composed class layout contains a duplicate structure TypeDef identity.");
            }
        }

        var structureMaps = new int[references.Length][];
        for (var referenceIndex = 0;
            referenceIndex < references.Length; referenceIndex++)
        {
            var sourceStructures = references[referenceIndex].Structures;
            var map = new int[sourceStructures.Length];
            for (var structureIndex = 0;
                structureIndex < sourceStructures.Length; structureIndex++)
            {
                var origin = sourceStructures[structureIndex]
                    .StandardTypeOrigin
                    ?? throw Invalid(
                        "A selected class layout structure lost its provider origin.");
                if (!structureIndexByProviderIdentity.TryGetValue(
                        (origin.Provider.ModuleIdentity,
                            origin.DefinitionId),
                        out map[structureIndex]))
                {
                    throw Invalid(
                        "A selected class layout structure is absent from the composed type closure.");
                }
            }

            structureMaps[referenceIndex] = map;
        }

        if (directStructureComposition)
        {
            var remappedStructures = structuresResult.ToBuilder();
            for (var structureIndex = 0;
                structureIndex < directStructureCompositionSources.Count;
                structureIndex++)
            {
                var (referenceIndex, sourceStructureIndex)
                    = directStructureCompositionSources[structureIndex];
                var sourceStructure
                    = references[referenceIndex]
                        .Structures[sourceStructureIndex];
                var origin = sourceStructure.StandardTypeOrigin!;
                PrimitiveType RemapDirectStructureFieldType(
                    PrimitiveType type)
                {
                    if (BoundEnumTypes.IsType(type))
                    {
                        return type;
                    }

                    if (BoundGenericNominalTypeTypes.IsType(type))
                    {
                        return type;
                    }

                    if (BoundSpanTypeTypes.IsType(type)
                        || ManagedClassTypes.IsClass(type)
                        || NativePointerTypes.IsType(type)
                        || NativeFixedArrayTypes.IsType(type)
                        || NativeFunctionPointerTypes.IsType(type)
                        || CheckedReferenceTypes.IsType(type))
                    {
                        throw Unsupported(
                            "Direct class layout struct composition supports only scalar and same-provider inline-struct fields.");
                    }

                    var nestedStructureIndex = (int)type - 100;
                    if (nestedStructureIndex >= 0
                        && nestedStructureIndex
                            < references[referenceIndex].Structures.Length)
                    {
                        var nestedOrigin
                            = references[referenceIndex]
                                .Structures[nestedStructureIndex]
                                .StandardTypeOrigin
                            ?? throw Invalid(
                                "A nested class layout struct lost its provider origin.");
                        if (!SameA2SourceTypeProviderSnapshot(
                                origin.Provider, nestedOrigin.Provider))
                        {
                            throw Unsupported(
                                "Direct class layout struct nesting must remain within its provider Module.");
                        }

                        return (PrimitiveType)(100
                            + structureMaps[referenceIndex][
                                nestedStructureIndex]);
                    }
                    if (nestedStructureIndex >= 0
                        || !IsStandardClassTypeOnlyComposedFieldType(type))
                    {
                        throw Unsupported(
                            "A direct class layout struct field is outside the selected scalar or nested-struct closure.");
                    }

                    return type;
                }

                var fields
                    = ImmutableArray.CreateBuilder<BoundStructField>(
                        sourceStructure.Fields.Length);
                foreach (var field in sourceStructure.Fields)
                {
                    var fieldOrigin = field.StandardFieldOrigin;
                    if (fieldOrigin is not null)
                    {
                        if (fieldOrigin.ExpectedDeclaration is not
                                BoundStructField expectedField
                            || !fieldOrigin.AdditionalExpectedDeclarations
                                .IsDefaultOrEmpty)
                        {
                            throw Unsupported(
                                "Direct class layout struct FieldDefs require one exact expected declaration.");
                        }

                        fieldOrigin = fieldOrigin with {
                            ExpectedDeclaration = expectedField with {
                                Type = RemapDirectStructureFieldType(
                                    expectedField.Type),
                                StandardFieldOrigin = null,
                            },
                        };
                    }

                    fields.Add(field with {
                        Type = RemapDirectStructureFieldType(field.Type),
                        StandardFieldOrigin = fieldOrigin,
                    });
                }

                var expectedDeclaration
                    = origin.ExpectedDeclaration! with {
                        Fields = [.. origin.ExpectedDeclaration.Fields
                            .Select(field => field with {
                                Type = RemapDirectStructureFieldType(
                                    field.Type),
                                StandardFieldOrigin = null,
                            })],
                        StandardTypeOrigin = null,
                        StandardLayoutOnly = false,
                    };
                remappedStructures[structureIndex]
                    = sourceStructure with {
                        Fields = fields.MoveToImmutable(),
                        StandardTypeOrigin = origin with {
                            ExpectedDeclaration = expectedDeclaration,
                        },
                    };
            }

            structuresResult = remappedStructures.MoveToImmutable();
        }

        var enumDefinitionGroups = new List<(
            BoundEnumDefinition Definition,
            BoundStandardEnumOccurrence Occurrence,
            List<BoundStandardEnumView> OriginalDeclarations,
            List<BoundStandardEnumExpectation> Expectations,
            List<string> ExportKeys,
            List<string> SelectedExportKeys)>();
        var enumGroupBySite = new Dictionary<(
            string SchemaIdentity, string ModuleIdentity, int ModuleRow,
            uint DefinitionId), int>();
        var enumMaps = new int[references.Length][];
        bool IsSelectedClassProvider(int referenceIndex,
            BoundSourceTypeProviderSnapshot provider)
            => references[referenceIndex].StandardClassDeclarations.Any(
                declaration =>
                    (declaration.Expectations.IsEmpty
                        && !declaration.SelectedExportKeys.IsDefaultOrEmpty
                        && BoundStandardEnumDeclarationContract.SnapshotsMatch(
                            declaration.Declaration.Provider, provider))
                    || declaration.Expectations.Any(expectation =>
                        BoundStandardEnumDeclarationContract.SnapshotsMatch(
                            expectation.Binding.UsingModule, provider)
                        || BoundStandardEnumDeclarationContract.SnapshotsMatch(
                            expectation.Binding.Provider, provider)));

        static bool SameEnumSite(BoundStandardEnumTypeSite left,
            BoundStandardEnumTypeSite right)
            => left.DefinitionId == right.DefinitionId
                && BoundStandardEnumDeclarationContract.SnapshotsMatch(
                    left.Snapshot, right.Snapshot);

        static bool SameEnumExpectationKey(
            BoundStandardEnumExpectation left,
            BoundStandardEnumExpectation right)
            => left.Binding.ImportRow == right.Binding.ImportRow
                && BoundStandardEnumDeclarationContract.SnapshotsMatch(
                    left.Binding.UsingModule, right.Binding.UsingModule);

        static bool SameEnumExpectation(
            BoundStandardEnumExpectation left,
            BoundStandardEnumExpectation right)
        {
            var leftBinding = left.Binding;
            var rightBinding = right.Binding;
            return SameEnumExpectationKey(left, right)
                && leftBinding.ExpectedDefinitionId
                    == rightBinding.ExpectedDefinitionId
                && StringComparer.Ordinal.Equals(
                    leftBinding.ProducerIdentity,
                    rightBinding.ProducerIdentity)
                && StringComparer.Ordinal.Equals(leftBinding.ExportKey,
                    rightBinding.ExportKey)
                && leftBinding.TargetRequirement
                    == rightBinding.TargetRequirement
                && leftBinding.RuntimePolicy
                    == rightBinding.RuntimePolicy
                && leftBinding.ProviderDefinitionId
                    == rightBinding.ProviderDefinitionId
                && BoundStandardEnumDeclarationContract.SnapshotsMatch(
                    leftBinding.Provider, rightBinding.Provider)
                && SameEnumSite(left.ExpectedDeclaration.Site,
                    right.ExpectedDeclaration.Site)
                && BoundStandardEnumDeclarationContract.ContractMatches(
                    left.ExpectedDeclaration,
                    right.ExpectedDeclaration);
        }

        for (var referenceIndex = 0;
            referenceIndex < references.Length; referenceIndex++)
        {
            var sourceEnums = references[referenceIndex].EnumDefinitions;
            var map = new int[sourceEnums.Length];
            if (!sourceEnums.IsEmpty)
            {
                if (!BoundEnumTypes.IsValidTable(sourceEnums))
                {
                    throw Unsupported(
                        "A selected class reference contains an invalid enum closure.");
                }

                for (var enumIndex = 0;
                    enumIndex < sourceEnums.Length; enumIndex++)
                {
                    var definition = sourceEnums[enumIndex];
                    if (definition.Identity
                            is not BoundEnumDeclarationIdentity.Standard
                                standard
                        || definition.ModuleImport is not null
                        || !BoundStandardEnumDeclarationContract
                            .IsValidDefinition(standard.Value, definition)
                        || !StringComparer.Ordinal.Equals(
                            standard.Value.Declaration.Site.Snapshot
                                .SchemaIdentity, classSchemaIdentity))
                    {
                        throw Unsupported(
                            "Class reference composition requires exact selected-schema Standard enum declarations.");
                    }

                    var occurrence = standard.Value;
                    var isSelectedRoot = occurrence.IsLookupRoot
                        && !occurrence.SelectedExportKeys.IsDefaultOrEmpty
                        && occurrence.Expectations.IsEmpty
                        && IsSelectedClassProvider(referenceIndex,
                            occurrence.Declaration.Site.Snapshot);
                    var isImportedDependency = !occurrence.IsLookupRoot
                        && occurrence.SelectedExportKeys.IsEmpty
                        && !occurrence.Expectations.IsDefaultOrEmpty
                        && occurrence.Expectations.All(expectation =>
                            IsSelectedClassProvider(referenceIndex,
                                expectation.Binding.UsingModule)
                            && BoundStandardEnumDeclarationContract
                                .SnapshotsMatch(
                                    expectation.Binding.Provider,
                                    occurrence.Declaration.Site.Snapshot)
                            && expectation.Binding.ProviderDefinitionId
                                == occurrence.Declaration.Site.DefinitionId);
                    if (!isSelectedRoot && !isImportedDependency)
                    {
                        throw Unsupported(
                            "Class reference composition requires selected direct enum roots or enum imports owned by a selected class root.");
                    }

                    var site = occurrence.Declaration.Site;
                    var siteKey = (site.Snapshot.SchemaIdentity,
                        site.Snapshot.ModuleIdentity,
                        site.Snapshot.ModuleRow, site.DefinitionId);
                    if (enumGroupBySite.TryGetValue(siteKey,
                            out var enumGroupIndex))
                    {
                        var group = enumDefinitionGroups[enumGroupIndex];
                        if (!BoundStandardEnumDeclarationContract
                                .SnapshotsMatch(
                                    group.Occurrence.Declaration.Site
                                        .Snapshot,
                                    site.Snapshot)
                            || !BoundStandardEnumDeclarationContract
                                .ContractMatches(
                                    group.Occurrence.Declaration,
                                    occurrence.Declaration)
                            || !BoundEnumTypes.HasSameContractShape(
                                group.Definition, definition))
                        {
                            throw Invalid(
                                "Selected class references contain conflicting views of one enum TypeDef.");
                        }

                        foreach (var original in occurrence.OriginalDeclarations)
                        {
                            if (!group.OriginalDeclarations.Any(existing =>
                                    SameEnumSite(existing.Site,
                                        original.Site)))
                            {
                                group.OriginalDeclarations.Add(original);
                            }
                        }
                        foreach (var expectation in occurrence.Expectations)
                        {
                            var existing = group.Expectations.FirstOrDefault(
                                candidate => SameEnumExpectationKey(candidate,
                                    expectation));
                            if (existing is null)
                            {
                                group.Expectations.Add(expectation);
                            }
                            else if (!SameEnumExpectation(existing,
                                         expectation))
                            {
                                throw Invalid(
                                    "Selected class references contain conflicting expectations for one enum Import.");
                            }
                        }

                        AddDistinct(group.ExportKeys,
                            occurrence.ExportKeys);
                        AddDistinct(group.SelectedExportKeys,
                            occurrence.SelectedExportKeys);
                    }
                    else
                    {
                        enumGroupIndex = enumDefinitionGroups.Count;
                        enumGroupBySite.Add(siteKey, enumGroupIndex);
                        enumDefinitionGroups.Add((definition, occurrence,
                            [.. occurrence.OriginalDeclarations],
                            [.. occurrence.Expectations],
                            [.. occurrence.ExportKeys],
                            [.. occurrence.SelectedExportKeys]));
                    }

                    map[enumIndex] = enumGroupIndex;
                }
            }

            enumMaps[referenceIndex] = map;
        }

        var enumDefinitionsBuilder
            = ImmutableArray.CreateBuilder<BoundEnumDefinition>(
                enumDefinitionGroups.Count);
        foreach (var group in enumDefinitionGroups)
        {
            var occurrence = new BoundStandardEnumOccurrence(
                group.Occurrence.Declaration,
                [.. group.OriginalDeclarations],
                [.. group.Expectations], [.. group.ExportKeys],
                [.. group.SelectedExportKeys]);
            if (!occurrence.IsValid)
            {
                throw Invalid(
                    "A composed class enum view lost its provider or Import expectations.");
            }
            var identity
                = new BoundEnumDeclarationIdentity.Standard(occurrence);
            var members
                = ImmutableArray.CreateBuilder<BoundEnumMemberDefinition>(
                    group.Definition.Members.Length);
            foreach (var member in group.Definition.Members)
            {
                if (member.DeclarationKey is not string declarationKey)
                {
                    throw Invalid(
                        "A selected Standard enum member has no declaration key.");
                }

                members.Add(member with {
                    Identity = new BoundEnumMemberIdentity.OwnerKey(
                        identity, declarationKey),
                    DeclaringEnum = identity,
                });
            }

            enumDefinitionsBuilder.Add(group.Definition with {
                Identity = identity,
                Members = members.MoveToImmutable(),
            });
        }
        var enumDefinitionsResult = enumDefinitionsBuilder.MoveToImmutable();

        if (directStructureComposition)
        {
            var remappedStructures = structuresResult.ToBuilder();
            for (var structureIndex = 0;
                structureIndex < directStructureCompositionSources.Count;
                structureIndex++)
            {
                var (referenceIndex, _) =
                    directStructureCompositionSources[structureIndex];
                var enumMap = enumMaps[referenceIndex];
                PrimitiveType RemapDirectStructureEnumType(
                    PrimitiveType type)
                {
                    if (!BoundEnumTypes.IsType(type))
                    {
                        return type;
                    }

                    var enumIndex = BoundEnumTypes.Index(type);
                    if ((uint)enumIndex >= (uint)enumMap.Length)
                    {
                        throw Unsupported(
                            "A direct class layout struct enum field is outside the selected enum provider closure.");
                    }

                    return BoundEnumTypes.Type(enumMap[enumIndex]);
                }

                BoundStructField RemapDirectStructureField(
                    BoundStructField field)
                {
                    var fieldOrigin = field.StandardFieldOrigin;
                    if (fieldOrigin is not null)
                    {
                        if (fieldOrigin.ExpectedDeclaration is not
                                BoundStructField expectedField)
                        {
                            throw Unsupported(
                                "Direct class layout struct FieldDefs require one exact expected declaration.");
                        }

                        fieldOrigin = fieldOrigin with {
                            ExpectedDeclaration = expectedField with {
                                Type = RemapDirectStructureEnumType(
                                    expectedField.Type),
                                StandardFieldOrigin = null,
                            },
                        };
                    }

                    return field with {
                        Type = RemapDirectStructureEnumType(field.Type),
                        StandardFieldOrigin = fieldOrigin,
                    };
                }

                var structure = remappedStructures[structureIndex];
                var origin = structure.StandardTypeOrigin!;
                var expectedDeclaration
                    = origin.ExpectedDeclaration! with {
                        Fields = [.. origin.ExpectedDeclaration.Fields
                            .Select(field => field with {
                                Type = RemapDirectStructureEnumType(
                                    field.Type),
                                StandardFieldOrigin = null,
                            })],
                    };
                remappedStructures[structureIndex] = structure with {
                    Fields = [.. structure.Fields.Select(
                        RemapDirectStructureField)],
                    StandardTypeOrigin = origin with {
                        ExpectedDeclaration = expectedDeclaration,
                    },
                };
            }

            structuresResult = remappedStructures.MoveToImmutable();
        }

        var genericNominalInstanceMaps = new int[references.Length][];
        bool IsSelectedClassTypeSpecProvider(int referenceIndex,
            string providerIdentity)
            => references[referenceIndex].StandardClassDeclarations.Any(
                declaration =>
                    StringComparer.Ordinal.Equals(
                        declaration.Declaration.Provider.ModuleIdentity,
                        providerIdentity)
                    && (IsSelectedClassProvider(referenceIndex,
                            declaration.Declaration.Provider)
                        || (classHandleTypeDependencies.Contains(
                                declaration.Declaration.Identity)
                            && declaration.Expectations.IsEmpty
                            && StringComparer.Ordinal.Equals(
                                declaration.Declaration.Identity
                                    .ProviderIdentity,
                                providerIdentity)
                            && StringComparer.Ordinal.Equals(
                                declaration.Declaration.Identity
                                    .ModuleIdentity,
                                providerIdentity))));
        PrimitiveType RemapClassFieldType(int referenceIndex,
            PrimitiveType type,
            BoundSourceTypeProviderSnapshot classProvider)
        {
            if (BoundEnumTypes.IsType(type))
            {
                var enumIndex = BoundEnumTypes.Index(type);
                if ((uint)enumIndex
                    < (uint)enumMaps[referenceIndex].Length)
                {
                    return BoundEnumTypes.Type(
                        enumMaps[referenceIndex][enumIndex]);
                }

                throw Unsupported(
                    "A class FieldDef enum type is outside the selected enum provider closure.");
            }

            if (BoundGenericNominalTypeTypes.IsType(type))
            {
                var instanceIndex
                    = BoundGenericNominalTypeTypes.Index(type);
                var sourceInstances
                    = references[referenceIndex]
                        .GenericNominalTypeInstances;
                var sourceDefinitions
                    = references[referenceIndex]
                        .GenericNominalTypeDefinitions;
                if ((uint)instanceIndex >= (uint)sourceInstances.Length
                    || (uint)instanceIndex
                        >= (uint)genericNominalInstanceMaps[
                            referenceIndex].Length)
                {
                    throw Unsupported(
                        "A class FieldDef nominal type is outside its selected provider closure.");
                }

                var sourceInstance = sourceInstances[instanceIndex];
                var sourceDefinition = sourceDefinitions.FirstOrDefault(
                    definition =>
                        definition.Identity == sourceInstance.Definition);
                if (sourceDefinition is null
                    || sourceDefinition.NominalKind != 2
                    || (!classHandleTypeDependencies.Contains(
                            sourceInstance.Definition)
                        && !selectedClassIdentities.Contains(
                            sourceInstance.Definition)))
                {
                    throw Unsupported(
                        "Class FieldDefs can only retain selected class TypeDef handle dependencies.");
                }

                var classDeclaration
                    = references[referenceIndex]
                        .StandardClassDeclarations.SingleOrDefault(
                            declaration =>
                                declaration.Declaration.Identity
                                == sourceInstance.Definition)
                    ?? throw Invalid(
                        "A class handle field lost its selected class declaration view.");
                var classView = classDeclaration.Declaration;
                var hasDirectClassView
                    = sourceInstance.TypeSpecForm == 0x02
                        && StringComparer.Ordinal.Equals(
                            sourceInstance.TypeSpecProviderIdentity,
                            classProvider.ModuleIdentity)
                        && IsSelectedClassTypeSpecProvider(referenceIndex,
                            classProvider.ModuleIdentity)
                        && StringComparer.Ordinal.Equals(
                            classView.Identity.ProviderIdentity,
                            classView.Provider.ModuleIdentity)
                        && StringComparer.Ordinal.Equals(
                            classView.Identity.ModuleIdentity,
                            classView.Provider.ModuleIdentity);
                var hasImportedClassView
                    = !classDeclaration.Expectations.IsDefaultOrEmpty
                    && classDeclaration.Expectations.Any(expectation =>
                        IsSelectedClassProvider(referenceIndex,
                            expectation.Binding.UsingModule)
                        && SameA2SourceTypeProviderSnapshot(
                            expectation.Binding.Provider,
                            classView.Provider)
                        && expectation.Binding.ProviderDefinitionId
                            == sourceInstance.Definition.DefinitionId
                        && sourceDefinition.Provider is
                            BoundSourceTypeProviderSnapshot provider
                        && SameA2SourceTypeProviderSnapshot(provider,
                            classView.Provider)
                        && sourceDefinition.ProviderExpectations.Any(
                            typeExpectation =>
                                BoundGenericNominalTypeContract
                                    .AreEquivalentProviderExpectations(
                                        typeExpectation,
                                        expectation.Binding)));
                if (!hasDirectClassView && !hasImportedClassView)
                {
                    throw Unsupported(
                        "Class FieldDefs require a selected class handle with an exact direct or imported provider view.");
                }

                return BoundGenericNominalTypeTypes.Type(
                    genericNominalInstanceMaps[referenceIndex][
                        instanceIndex]);
            }

            if (BoundSpanTypeTypes.IsType(type)
                || ManagedClassTypes.IsClass(type)
                || NativePointerTypes.IsType(type)
                || NativeFixedArrayTypes.IsType(type)
                || NativeFunctionPointerTypes.IsType(type)
                || CheckedReferenceTypes.IsType(type))
            {
                throw Unsupported(
                    "Class reference composition does not yet admit class, pointer or other non-inline-struct field identities.");
            }

            var structureIndex = (int)type - 100;
            if (structureIndex >= 0
                && structureIndex
                    < references[referenceIndex].Structures.Length)
            {
                var structureOrigin
                    = references[referenceIndex]
                        .Structures[structureIndex].StandardTypeOrigin
                        ?? throw Invalid(
                            "A class FieldDef structure type lost its provider origin.");
                if (!SameA2SourceTypeProviderSnapshot(classProvider,
                        structureOrigin.Provider))
                {
                    throw Unsupported(
                        "A composed class inline-struct field must refer to a direct structure from the same provider Module.");
                }

                return (PrimitiveType)(100
                    + structureMaps[referenceIndex][structureIndex]);
            }
            if (structureIndex >= 0)
            {
                throw Unsupported(
                    "A class FieldDef references a structure outside its selected provider closure.");
            }

            if (!IsStandardClassTypeOnlyComposedFieldType(type))
            {
                throw Unsupported(
                    "Class reference composition requires scalar or inline-struct field types.");
            }

            return type;
        }

        var definitions
            = ImmutableArray.CreateBuilder<
                BoundGenericNominalTypeDefinition>();
        var definitionIndexes = new Dictionary<
            BoundGenericNominalTypeIdentity, int>();
        var instances
            = ImmutableArray.CreateBuilder<
                BoundGenericNominalTypeInstance>();
        var instanceIndexes = new Dictionary<
            StandardClassTypeOnlyInstanceKey, int>();
        var declarations
            = new List<BoundStandardClassDeclaration>();
        var declarationIndexes = new Dictionary<
            BoundGenericNominalTypeIdentity, int>();
        var declarationNames = new HashSet<(string Namespace, string Name)>();
        var retainedClassIdentities
            = new HashSet<BoundGenericNominalTypeIdentity>(
                selectedClassIdentities);
        retainedClassIdentities.UnionWith(classHandleTypeDependencies);
        var providerSnapshots = new Dictionary<
            (string SchemaIdentity, string ModuleIdentity),
            BoundSourceTypeProviderSnapshot>();

        for (var referenceIndex = 0;
            referenceIndex < references.Length; referenceIndex++)
        {
            var reference = references[referenceIndex];
            if (reference.RuntimePolicy != runtimePolicy)
            {
                throw Invalid(
                    "Selected Standard class references require the same runtime policy.");
            }
            if (reference.RequiresSourceGrant
                || reference.InvocationProfileId is not null
                || !reference.Functions.IsEmpty
                || !reference.Classes.IsEmpty
                || !reference.NativeFunctionPointerSignatures.IsEmpty
                || (reference.Structures.IsEmpty
                    && !reference.NativePointerTypes.IsEmpty)
                || !reference.NativeFixedArrayTypes.IsEmpty
                || !reference.CheckedReferenceTypes.IsEmpty
                || !reference.Interfaces.IsEmpty
                || !reference.PrivateNominalTypeOrigins.IsEmpty
                || !reference.PrivateClassReferences.IsEmpty
                || reference.CoreTaskType is not null
                || !reference.CoreExceptionIdentitySelections.IsEmpty
                || reference.NativeCoreRegistration is not null
                || reference.LayoutCoreRegistration is not null
                || reference.StaticInitializationCoreRegistration is not null
                || !reference.CoreContractFailureIdentitySelections.IsEmpty
                || reference.LazyInitializationCoreRegistration is not null
                || reference.NativeOutputCoreRegistration is not null
                || !reference.GenericMethodImports.IsEmpty
                || !reference.RetainedGenericMethodRoots.IsEmpty
                || (reference.Structures.IsEmpty
                    && reference.StandardQuerySourceAssociation is not null)
                || !reference.CoreEvaluationFormIdentitySelections.IsEmpty
                || !reference.CoreEvaluationFormSchemaProviders.IsEmpty
                || !reference.SpanTypes.IsEmpty
                || reference.StandardClassDeclarations.IsEmpty
                || reference.GenericNominalTypeDefinitions.IsEmpty
                || reference.GenericNominalTypeInstances.IsEmpty
                || reference.StandardClassDeclarations.Any(declaration =>
                    !StringComparer.Ordinal.Equals(
                        declaration.Declaration.Provider.SchemaIdentity,
                        classSchemaIdentity)))
            {
                throw Unsupported(
                    "Type-only class composition requires selected class TypeDefs without member, interface, runtime, or other type-family obligations.");
            }

            var sourceDefinitions = reference.GenericNominalTypeDefinitions;
            if (!BoundGenericNominalTypeContract.IsValidDefinitionTable(
                    sourceDefinitions)
                || !BoundGenericNominalTypeContract.IsValidInstanceTable(
                    reference.GenericNominalTypeInstances,
                    sourceDefinitions))
            {
                throw Invalid(
                    "A selected class reference has invalid nominal type tables.");
            }

            foreach (var definition in sourceDefinitions)
            {
                var isClassDefinition = definition.NominalKind == 2;
                if (isClassDefinition
                    && !retainedClassIdentities.Contains(
                        definition.Identity))
                {
                    continue;
                }

                var isStructureDefinition
                    = definition.NominalKind == 1
                        && structureDefinitionIdentities.Contains(
                            definition.Identity);
                var hasDirectProvider
                    = definition.ProviderExpectations.IsDefaultOrEmpty
                        && StringComparer.Ordinal.Equals(
                            definition.Identity.ProviderIdentity,
                            definition.Identity.ModuleIdentity);
                var hasSelectedImportedClassProvider
                    = isClassDefinition
                        && classHandleTypeDependencies.Contains(
                            definition.Identity)
                        && !definition.ProviderExpectations
                            .IsDefaultOrEmpty
                        && definition.Provider is
                            BoundSourceTypeProviderSnapshot provider
                        && definition.ProviderExpectations.All(expectation =>
                            IsSelectedClassProvider(referenceIndex,
                                expectation.UsingModule)
                            && SameA2SourceTypeProviderSnapshot(
                                expectation.Provider, provider)
                            && expectation.ProviderDefinitionId
                                == definition.Identity.DefinitionId);
                if ((!isClassDefinition && !isStructureDefinition)
                    || !definition.Formals.IsEmpty
                    || (!hasDirectProvider
                        && !hasSelectedImportedClassProvider))
                {
                    throw Unsupported(
                        "Type-only class composition requires direct TypeDefs or selected imported class-handle TypeDefs.");
                }

                if (definitionIndexes.TryGetValue(definition.Identity,
                        out var existingIndex))
                {
                    var existing = definitions[existingIndex];
                    if (!BoundGenericNominalTypeContract
                        .AreEquivalentDefinitions(existing, definition))
                    {
                        throw Invalid(
                            "Views of one Standard class TypeDef disagree on the actual provider definition.");
                    }

                    var providerExpectations
                        = existing.ProviderExpectations.ToBuilder();
                    foreach (var expectation in
                        definition.ProviderExpectations)
                    {
                        if (!providerExpectations.Any(candidate =>
                                BoundGenericNominalTypeContract
                                    .AreEquivalentProviderExpectations(
                                        candidate, expectation)))
                        {
                            providerExpectations.Add(expectation);
                        }
                    }
                    definitions[existingIndex] = existing with {
                        ProviderExpectations
                            = providerExpectations.ToImmutable(),
                    };
                }
                else
                {
                    if (definitions.Count
                        >= BoundGenericNominalTypeTypes.MaximumCount)
                    {
                        throw new NotSupportedException(
                            "The composed Standard class TypeDef closure exceeds the analysis limit.");
                    }

                    definitionIndexes.Add(definition.Identity,
                        definitions.Count);
                    definitions.Add(definition);
                }
            }

            var sourceInstances = reference.GenericNominalTypeInstances;
            var instanceMap = new int[sourceInstances.Length];
            for (var instanceIndex = 0;
                instanceIndex < sourceInstances.Length; instanceIndex++)
            {
                var instance = sourceInstances[instanceIndex];
                var hasDirectTypeSpec = instance.TypeSpecReference == 0
                    && instance.TypeSpecProviderIdentity is null
                    && instance.TypeSpecForm == 0;
                var instanceDefinition = sourceDefinitions.FirstOrDefault(
                    definition =>
                        definition.Identity == instance.Definition);
                if (instanceDefinition is null)
                {
                    throw Invalid(
                        "A selected class reference instance has no nominal TypeDef definition.");
                }
                if (!retainedClassIdentities.Contains(instance.Definition)
                    && !structureDefinitionIdentities.Contains(
                        instance.Definition))
                {
                    continue;
                }

                var instanceClassDeclaration
                    = reference.StandardClassDeclarations.SingleOrDefault(
                        declaration =>
                            declaration.Declaration.Identity
                            == instance.Definition);
                var hasDirectClassTypeSpec
                    = instance.TypeSpecReference != 0
                        && instance.TypeSpecForm == 0x02
                        && instance.TypeSpecProviderIdentity is
                            string typeSpecProviderIdentity
                        && instanceDefinition.NominalKind == 2
                        && IsSelectedClassTypeSpecProvider(referenceIndex,
                            typeSpecProviderIdentity);
                var hasImportedClassTypeSpec = false;
                if (instance.TypeSpecReference != 0
                    && instance.TypeSpecForm == 0x03
                    && instance.TypeSpecProviderIdentity is
                        string typeSpecProviderIdentity
                    && instanceDefinition?.NominalKind == 2
                    && instanceClassDeclaration is not null)
                {
                    hasImportedClassTypeSpec
                        = instanceClassDeclaration.Expectations.Any(
                            expectation =>
                                StringComparer.Ordinal.Equals(
                                    expectation.Binding.UsingModule
                                        .ModuleIdentity,
                                    typeSpecProviderIdentity)
                                && IsSelectedClassProvider(referenceIndex,
                                    expectation.Binding.UsingModule)
                                && SameA2SourceTypeProviderSnapshot(
                                    expectation.Binding.Provider,
                                    instanceClassDeclaration.Declaration
                                        .Provider)
                                && expectation.Binding.ProviderDefinitionId
                                    == instance.Definition.DefinitionId);
                }
                if (!instance.Arguments.IsEmpty
                    || instance.Qualifiers != 0
                    || (!hasDirectTypeSpec && !hasDirectClassTypeSpec
                        && !hasImportedClassTypeSpec)
                    || !definitionIndexes.ContainsKey(instance.Definition))
                {
                    throw Unsupported(
                        "Type-only class composition requires direct instances or selected direct/imported class-handle TypeSpecs.");
                }

                var key = new StandardClassTypeOnlyInstanceKey(
                    instance.Definition, instance.Qualifiers,
                    instance.TypeSpecReference,
                    instance.TypeSpecProviderIdentity,
                    instance.TypeSpecForm);
                if (!instanceIndexes.TryGetValue(key,
                        out var mappedInstanceIndex))
                {
                    if (instances.Count
                        >= BoundGenericNominalTypeTypes.MaximumCount)
                    {
                        throw new NotSupportedException(
                            "The composed Standard class instance table exceeds the analysis limit.");
                    }

                    mappedInstanceIndex = instances.Count;
                    instanceIndexes.Add(key, mappedInstanceIndex);
                    instances.Add(instance);
                }

                instanceMap[instanceIndex] = mappedInstanceIndex;
            }
            genericNominalInstanceMaps[referenceIndex] = instanceMap;

            foreach (var declaration in reference.StandardClassDeclarations)
            {
                if (!retainedClassIdentities.Contains(
                        declaration.Declaration.Identity))
                {
                    continue;
                }

                var view = declaration.Declaration with {
                    Fields = [.. declaration.Declaration.Fields.Select(
                        field => field with {
                            Type = RemapClassFieldType(referenceIndex,
                                field.Type,
                                declaration.Declaration.Provider),
                        })],
                };
                if (!definitionIndexes.TryGetValue(view.Identity,
                        out var typeDefinitionIndex))
                {
                    throw Invalid(
                        "A selected class declaration lost its nominal TypeDef definition.");
                }

                var typeDefinition = definitions[typeDefinitionIndex];
                var hasSelectedImportedClassView
                    = classHandleTypeDependencies.Contains(view.Identity)
                        && !declaration.Expectations.IsDefaultOrEmpty
                        && declaration.Expectations.All(expectation =>
                            IsSelectedClassProvider(referenceIndex,
                                expectation.Binding.UsingModule)
                            && SameA2SourceTypeProviderSnapshot(
                                expectation.Binding.Provider,
                                view.Provider)
                            && expectation.Binding.ProviderDefinitionId
                                == view.Identity.DefinitionId
                            && typeDefinition.ProviderExpectations.Any(
                                typeExpectation =>
                                    BoundGenericNominalTypeContract
                                        .AreEquivalentProviderExpectations(
                                            typeExpectation,
                                            expectation.Binding)));
                var hasDirectClassHandleDependency
                    = classHandleTypeDependencies.Contains(view.Identity)
                        && (declaration.Expectations.IsEmpty
                            || hasSelectedImportedClassView)
                        && StringComparer.Ordinal.Equals(
                            view.Identity.ProviderIdentity,
                            view.Identity.ModuleIdentity)
                        && StringComparer.Ordinal.Equals(
                            view.Identity.ModuleIdentity,
                            view.Provider.ModuleIdentity)
                        && (typeDefinition.Provider is null
                            || SameA2SourceTypeProviderSnapshot(
                                typeDefinition.Provider, view.Provider))
                        && (typeDefinition.ProviderExpectations
                                .IsDefaultOrEmpty
                            || typeDefinition.ProviderExpectations.All(
                                expectation =>
                                    IsSelectedClassProvider(referenceIndex,
                                        expectation.UsingModule)
                                    && SameA2SourceTypeProviderSnapshot(
                                        expectation.Provider, view.Provider)
                                    && expectation.ProviderDefinitionId
                                        == view.Identity.DefinitionId));
                var isSelectedClassRoot
                    = !declaration.SelectedExportKeys.IsDefaultOrEmpty
                        || selectedClassIdentities.Contains(view.Identity);
                var remappedDeclaration = declaration with {
                    Declaration = view,
                };
                if (!StringComparer.Ordinal.Equals(view.Provider.SchemaIdentity,
                        classSchemaIdentity)
                    || !definitionIndexes.ContainsKey(view.Identity)
                    || (!isSelectedClassRoot
                        && !hasSelectedImportedClassView
                        && !hasDirectClassHandleDependency)
                    || (declaration.ExportKeys.IsDefaultOrEmpty
                        && !hasSelectedImportedClassView
                        && !hasDirectClassHandleDependency)
                    || !view.Methods.IsEmpty)
                {
                    throw Unsupported(
                        "Type-only class composition requires selected class roots or exact imported class-handle dependencies without methods.");
                }

                AddProviderSnapshot(view.Provider);
                foreach (var expectation in declaration.Expectations)
                {
                    AddProviderSnapshot(expectation.Binding.UsingModule);
                    AddProviderSnapshot(expectation.Binding.Provider);
                }
                var nameIdentity = (view.Namespace, view.Name);
                if (declarationIndexes.TryGetValue(view.Identity,
                        out var existingIndex))
                {
                    var existing = declarations[existingIndex];
                    var existingView = existing.Declaration;
                    if (!SameA2SourceTypeProviderSnapshot(
                            existingView.Provider, view.Provider)
                        || !StringComparer.Ordinal.Equals(
                            existingView.Namespace, view.Namespace)
                        || !StringComparer.Ordinal.Equals(
                            existingView.Name, view.Name)
                        || existingView.TypeDefFlags != view.TypeDefFlags
                        || existingView.Fields.Length
                            != view.Fields.Length
                        || existingView.Layouts.Length
                            != view.Layouts.Length
                        || !existing.ExportKeys.AsSpan().SequenceEqual(
                            declaration.ExportKeys.AsSpan()))
                    {
                        throw Invalid(
                            "Views of one Standard class TypeDef disagree on the checked declaration.");
                    }

                    var mergedFields = ImmutableArray.CreateBuilder<
                        BoundStandardClassFieldDeclaration>(
                            existingView.Fields.Length);
                    for (var fieldIndex = 0;
                        fieldIndex < existingView.Fields.Length; fieldIndex++)
                    {
                        var existingField = existingView.Fields[fieldIndex];
                        var field = view.Fields[fieldIndex];
                        if (existingField.DefinitionId != field.DefinitionId
                            || existingField.OwnerDefinitionId
                                != field.OwnerDefinitionId
                            || !StringComparer.Ordinal.Equals(
                                existingField.Name, field.Name)
                            || existingField.Flags != field.Flags
                            || existingField.TypeSpecReference
                                != field.TypeSpecReference
                            || existingField.Type != field.Type
                            || !existingField.ExportKeys.AsSpan().SequenceEqual(
                                field.ExportKeys.AsSpan()))
                        {
                            throw Invalid(
                                "Views of one Standard class TypeDef disagree on a FieldDef declaration.");
                        }

                        var selectedFieldKeys = new List<string>(
                            existingField.SelectedExportKeys);
                        AddDistinct(selectedFieldKeys,
                            field.SelectedExportKeys);
                        mergedFields.Add(existingField with {
                            SelectedExportKeys = [.. selectedFieldKeys],
                        });
                    }

                    for (var layoutIndex = 0;
                        layoutIndex < existingView.Layouts.Length;
                        layoutIndex++)
                    {
                        var existingLayout
                            = existingView.Layouts[layoutIndex];
                        var layout = view.Layouts[layoutIndex];
                        if (existingLayout.Row != layout.Row
                            || existingLayout.OwnerDefinitionId
                                != layout.OwnerDefinitionId
                            || existingLayout.TargetProfileReference
                                != layout.TargetProfileReference
                            || existingLayout.TargetProfileId
                                != layout.TargetProfileId
                            || existingLayout.TargetProfileRuleRevision
                                != layout.TargetProfileRuleRevision
                            || existingLayout.Kind != layout.Kind
                            || existingLayout.Flags != layout.Flags
                            || existingLayout.Alignment
                                != layout.Alignment
                            || existingLayout.Pack != layout.Pack
                            || !existingLayout.FieldDefinitionIds.AsSpan()
                                .SequenceEqual(
                                    layout.FieldDefinitionIds.AsSpan()))
                        {
                            throw Invalid(
                                "Views of one Standard class TypeDef disagree on a target layout variant.");
                        }
                    }

                    var selectedKeys = new List<string>(
                        existing.SelectedExportKeys);
                    AddDistinct(selectedKeys,
                        declaration.SelectedExportKeys);
                    var classExpectations
                        = existing.Expectations.ToBuilder();
                    foreach (var expectation in declaration.Expectations)
                    {
                        var existingExpectationIndex = -1;
                        for (var expectationIndex = 0;
                            expectationIndex < classExpectations.Count;
                            expectationIndex++)
                        {
                            var candidate
                                = classExpectations[expectationIndex];
                            if (candidate.Binding.ImportRow
                                    == expectation.Binding.ImportRow
                                && SameA2SourceTypeProviderSnapshot(
                                    candidate.Binding.UsingModule,
                                    expectation.Binding.UsingModule))
                            {
                                existingExpectationIndex
                                    = expectationIndex;
                                break;
                            }
                        }

                        if (existingExpectationIndex < 0)
                        {
                            classExpectations.Add(expectation);
                            continue;
                        }

                        var existingExpectation
                            = classExpectations[
                                existingExpectationIndex];
                        if (existingExpectation.Binding
                                .ExpectedDefinitionId
                                != expectation.Binding.ExpectedDefinitionId
                            || existingExpectation.Binding
                                .ProviderDefinitionId
                                != expectation.Binding.ProviderDefinitionId
                            || !StringComparer.Ordinal.Equals(
                                existingExpectation.Binding.ProducerIdentity,
                                expectation.Binding.ProducerIdentity)
                            || !StringComparer.Ordinal.Equals(
                                existingExpectation.Binding.ExportKey,
                                expectation.Binding.ExportKey)
                            || existingExpectation.Binding.TargetRequirement
                                != expectation.Binding.TargetRequirement
                            || existingExpectation.Binding.RuntimePolicy
                                != expectation.Binding.RuntimePolicy
                            || !SameA2SourceTypeProviderSnapshot(
                                existingExpectation.Binding.Provider,
                                expectation.Binding.Provider))
                        {
                            throw Invalid(
                                "Views of one Standard class TypeDef disagree on an imported provider expectation.");
                        }
                    }

                    declarations[existingIndex] = existing with {
                        Declaration = existingView with {
                            Fields = mergedFields.MoveToImmutable(),
                        },
                        SelectedExportKeys = [.. selectedKeys],
                        Expectations = classExpectations.ToImmutable(),
                    };
                }
                else
                {
                    if (!declarationNames.Add(nameIdentity))
                    {
                        throw Invalid(
                            "Selected Standard class references contain ambiguous fully qualified TypeDef names.");
                    }

                    declarationIndexes.Add(view.Identity,
                        declarations.Count);
                    declarations.Add(remappedDeclaration);
                }
            }

            if (sourceDefinitions.Any(definition =>
                    (definition.NominalKind != 2
                        || retainedClassIdentities.Contains(
                            definition.Identity))
                    && !declarationIndexes.ContainsKey(definition.Identity)
                    && !structureDefinitionIdentities.Contains(
                        definition.Identity)))
            {
                throw Unsupported(
                    "Class composition requires a retained source declaration for every nominal TypeDef.");
            }
        }

        if (directStructureComposition)
        {
            var remappedStructures = structuresResult.ToBuilder();
            for (var structureIndex = 0;
                structureIndex < directStructureCompositionSources.Count;
                structureIndex++)
            {
                var (referenceIndex, _) =
                    directStructureCompositionSources[structureIndex];
                var structure = remappedStructures[structureIndex];
                var origin = structure.StandardTypeOrigin!;
                PrimitiveType RemapClassHandle(PrimitiveType type)
                    => BoundGenericNominalTypeTypes.IsType(type)
                        ? RemapClassFieldType(referenceIndex, type,
                            origin.Provider)
                        : type;

                BoundStructField RemapField(BoundStructField field)
                {
                    var fieldOrigin = field.StandardFieldOrigin;
                    if (fieldOrigin?.ExpectedDeclaration
                        is BoundStructField expectedField)
                    {
                        fieldOrigin = fieldOrigin with {
                            ExpectedDeclaration = expectedField with {
                                Type = RemapClassHandle(expectedField.Type),
                                StandardFieldOrigin = null,
                            },
                        };
                    }

                    return field with {
                        Type = RemapClassHandle(field.Type),
                        StandardFieldOrigin = fieldOrigin,
                    };
                }

                var expectedDeclaration
                    = origin.ExpectedDeclaration! with {
                        Fields = [.. origin.ExpectedDeclaration.Fields
                            .Select(RemapField)],
                    };
                remappedStructures[structureIndex] = structure with {
                    Fields = [.. structure.Fields.Select(RemapField)],
                    StandardTypeOrigin = origin with {
                        ExpectedDeclaration = expectedDeclaration,
                    },
                };
            }

            structuresResult = remappedStructures.MoveToImmutable();
        }

        if (createsClassTypeBindingEquality)
        {
            for (var declarationIndex = 0;
                declarationIndex < declarations.Count; declarationIndex++)
            {
                var declaration = declarations[declarationIndex];
                if (!classTypeBindingEqualityEndpointsByActualType.TryGetValue(
                        declaration.Declaration.Identity,
                        out var endpoints)
                    || endpoints.Count < 2)
                {
                    continue;
                }

                declarations[declarationIndex] = declaration with {
                    TypeDefBindingEqualityGroups = [
                        new BoundStandardClassTypeDefBindingEqualityGroup(
                            [.. endpoints]),
                    ],
                };
            }
        }

        var definitionTable = definitions.ToImmutable();
        var instanceTable = instances.ToImmutable();
        var declarationTable = declarations.ToImmutableArray();
        if (!BoundGenericNominalTypeContract.IsValidDefinitionTable(
                definitionTable)
            || !BoundGenericNominalTypeContract.IsValidInstanceTable(
                instanceTable, definitionTable)
            || !BoundStandardClassDeclarationContract.IsValidTable(
                declarationTable, definitionTable))
        {
            throw Invalid(
                "The composed Standard class TypeDef closure is invalid.");
        }

        var mergedProgram = new BoundProgram([], -1) {
            RuntimePolicy = runtimePolicy,
            Structures = structuresResult,
            NativePointerTypes = nativePointerTypesResult,
            EnumDefinitions = enumDefinitionsResult,
            GenericNominalTypeDefinitions = definitionTable,
            GenericNominalTypeInstances = instanceTable,
            StandardClassDeclarations = declarationTable,
            StandardQuerySourceAssociation
                = standardQuerySourceAssociation,
        };
        if (!NativePointerTypeContract.IsValidTable(
                nativePointerTypesResult, structuresResult, []))
        {
            throw Invalid(
                "The composed Standard class/struct type closure is invalid.");
        }
        if (!BoundEnumTypes.IsValidTable(enumDefinitionsResult))
        {
            throw Invalid(
                "The composed Standard class enum type closure is invalid.");
        }
        SemanticVerifier.VerifyLibraryReferences(mergedProgram);
        return new CompilationReference(structuresResult, [], runtimePolicy,
            nativePointerTypes: nativePointerTypesResult,
            enumDefinitions: enumDefinitionsResult,
            genericNominalTypeDefinitions: definitionTable,
            genericNominalTypeInstances: instanceTable,
            standardClassDeclarations: declarationTable,
            standardQuerySourceAssociation:
                standardQuerySourceAssociation);

        void AddProviderSnapshot(BoundSourceTypeProviderSnapshot provider)
        {
            if (!StringComparer.Ordinal.Equals(provider.SchemaIdentity,
                    classSchemaIdentity)
                || provider.RuntimePolicy != (uint)runtimePolicy)
            {
                throw Invalid(
                    "Selected class references have an incompatible provider schema or runtime policy.");
            }

            var key = (provider.SchemaIdentity, provider.ModuleIdentity);
            if (providerSnapshots.TryGetValue(key, out var existing))
            {
                if (!SameA2SourceTypeProviderSnapshot(existing, provider))
                {
                    throw Invalid(
                        "Class references contain conflicting snapshots for one provider Module.");
                }
            }
            else
            {
                providerSnapshots.Add(key, provider);
            }
        }

        static bool IsSupportedClassTypeOnlySchema(string schemaIdentity)
            => StringComparer.Ordinal.Equals(schemaIdentity,
                    StandardC1M1SchemaIdentity)
                || StringComparer.Ordinal.Equals(schemaIdentity,
                    StandardC1M1Construction2SchemaIdentity)
                || StringComparer.Ordinal.Equals(schemaIdentity,
                    StandardC1M1Construction2InitializerCopyEvidenceSchemaIdentity)
                || StringComparer.Ordinal.Equals(schemaIdentity,
                    StandardC1M1ConstructionL1R1SchemaIdentity)
                || StringComparer.Ordinal.Equals(schemaIdentity,
                    StandardC1M1Construction2L1R1SchemaIdentity)
                || StandardGenericMethodSchema.IsCM1ClassParameterDefaults(
                    schemaIdentity)
                || StandardGenericMethodSchema.IsClassTypeBindingViews(
                    schemaIdentity)
                || IsSelectedC1EnumClosureSchemaIdentity(schemaIdentity);
    }

    private static bool IsStandardClassTypeOnlyComposedFieldType(
        PrimitiveType type)
        => type != PrimitiveType.Void
            && !BoundGenericNominalTypeTypes.IsType(type)
            && !BoundSpanTypeTypes.IsType(type)
            && !BoundEnumTypes.IsType(type)
            && !ManagedClassTypes.IsClass(type)
            && !NativePointerTypes.IsType(type)
            && !NativeFixedArrayTypes.IsType(type)
            && !NativeFunctionPointerTypes.IsType(type)
            && !CheckedReferenceTypes.IsType(type);

    private static void AddDistinct(List<string> destination,
        ImmutableArray<string> values)
    {
        if (values.IsDefaultOrEmpty)
        {
            return;
        }

        var existing = destination.ToHashSet(StringComparer.Ordinal);
        foreach (var value in values)
        {
            if (existing.Add(value))
            {
                destination.Add(value);
            }
        }
    }

    private static Dictionary<string,
            StandardQueryProviderModuleSnapshot>
        ValidateStandardQueryProviderClosures(
        ImmutableArray<BoundStandardFieldProviderClosure> closures)
    {
        if (closures.IsDefaultOrEmpty || closures[0] is null)
        {
            throw Unsupported(
                "A Standard reference set has no retained provider closures.");
        }

        var modulesByIdentity = new Dictionary<string,
            StandardQueryProviderModuleSnapshot>(StringComparer.Ordinal);
        var schemaIdentity = closures[0].SchemaIdentity;
        if (!StringComparer.Ordinal.Equals(schemaIdentity,
                StandardLayoutQueriesSchemaIdentity)
            && !StringComparer.Ordinal.Equals(schemaIdentity,
                StandardInitReferenceBodiesNestedFieldDestinationsSchemaIdentity)
            && !IsC1M1MethodTypeSchemaIdentity(schemaIdentity)
            && !IsSelectedC1ClassInstanceLayoutSchemaIdentity(
                schemaIdentity))
        {
            throw Unsupported(
                "A retained Standard provider closure has an unsupported exact schema.");
        }
        foreach (var closure in closures)
        {
            if (closure is null
                || !StringComparer.Ordinal.Equals(closure.SchemaIdentity,
                    schemaIdentity)
                || closure.Artifacts.IsDefaultOrEmpty
                || closure.Modules.IsDefaultOrEmpty
                || closure.Artifacts[0].ArtifactIndex != 0
                || !StringComparer.Ordinal.Equals(
                    closure.Artifacts[0].SchemaIdentity,
                    closure.SchemaIdentity)
                || closure.Artifacts[0].ArtifactSnapshot.IsDefaultOrEmpty
                || closure.OriginalProgram is null)
            {
                throw Invalid(
                    "A retained Standard provider closure is incomplete.");
            }

            var artifactsByIndex = new Dictionary<int,
                BoundStandardFieldProviderArtifact>();
            foreach (var artifact in closure.Artifacts)
            {
                if (artifact.ArtifactIndex < 0
                    || !StringComparer.Ordinal.Equals(
                        artifact.SchemaIdentity,
                        schemaIdentity)
                    || artifact.ArtifactSnapshot.IsDefaultOrEmpty
                    || !artifactsByIndex.TryAdd(artifact.ArtifactIndex,
                        artifact))
                {
                    throw Invalid(
                        "A Standard provider closure has an invalid artifact directory.");
                }
            }

            var foundRoot = false;
            foreach (var module in closure.Modules)
            {
                if (module.ArtifactIndex < 0
                    || !artifactsByIndex.TryGetValue(module.ArtifactIndex,
                        out var artifact)
                    || module.ModuleRow <= 0
                    || string.IsNullOrEmpty(module.Identity)
                    || module.GoverningRuleEdition == 0)
                {
                    throw Invalid(
                        "A Standard provider closure has an invalid Module directory.");
                }

                if (module.ArtifactIndex == closure.Artifacts[0].ArtifactIndex
                    && module.ModuleRow == closure.RootModuleRow
                    && StringComparer.Ordinal.Equals(module.Identity,
                        closure.RootModuleIdentity))
                {
                    foundRoot = true;
                }

                var snapshot = new StandardQueryProviderModuleSnapshot(
                    artifact.SchemaIdentity, artifact.ArtifactSnapshot,
                    module.ModuleRow, module.Identity,
                    module.GoverningRuleEdition, module.TargetRequirement,
                    module.RuntimePolicy);
                if (modulesByIdentity.TryGetValue(module.Identity,
                        out var existing)
                    && (!StringComparer.Ordinal.Equals(
                            existing.SchemaIdentity,
                            snapshot.SchemaIdentity)
                        || !existing.ArtifactSnapshot.AsSpan().SequenceEqual(
                            snapshot.ArtifactSnapshot.AsSpan())
                        || existing.ModuleRow != snapshot.ModuleRow
                        || existing.GoverningRuleEdition
                            != snapshot.GoverningRuleEdition
                        || existing.TargetRequirement
                            != snapshot.TargetRequirement
                        || existing.RuntimePolicy != snapshot.RuntimePolicy))
                {
                    throw Invalid(
                        "One Standard Module identity has conflicting selected snapshots.");
                }

                modulesByIdentity[module.Identity] = snapshot;
            }

            if (!foundRoot)
            {
                throw Invalid(
                    "A retained Standard provider closure omits its selected root Module.");
            }
        }

        return modulesByIdentity;
    }

    private static void ValidateStandardQueryProviderSnapshot(
        BoundSourceTypeProviderSnapshot provider,
        Dictionary<string, StandardQueryProviderModuleSnapshot>
            modulesByIdentity)
    {
        if (!modulesByIdentity.TryGetValue(provider.ModuleIdentity,
                out var module)
            || !StringComparer.Ordinal.Equals(provider.SchemaIdentity,
                module.SchemaIdentity)
            || !provider.ArtifactSnapshot.AsSpan().SequenceEqual(
                module.ArtifactSnapshot.AsSpan())
            || provider.ModuleRow != module.ModuleRow
            || provider.GoverningRuleEdition
                != module.GoverningRuleEdition
            || provider.TargetRequirement != module.TargetRequirement
            || provider.RuntimePolicy != module.RuntimePolicy)
        {
            throw Invalid(
                "A Standard type/field origin is absent from its retained provider closure.");
        }
    }
}
