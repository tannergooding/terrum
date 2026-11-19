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

    private readonly record struct StandardC1M1ClassTypeOnlyInstanceKey(
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
            return CreateC1M1TypeOnlyClassReferenceSet(
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
        if (!isLayoutQueriesReference && !isNestedFieldPathReference)
        {
            throw Unsupported(
                "Reference composition requires exact LayoutQueries or nested FieldPath selections.");
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
        CreateC1M1TypeOnlyClassReferenceSet(
            CompilationReference[] references)
    {
        var runtimePolicy = references[0].RuntimePolicy;
        var definitions
            = ImmutableArray.CreateBuilder<
                BoundGenericNominalTypeDefinition>();
        var definitionIndexes = new Dictionary<
            BoundGenericNominalTypeIdentity, int>();
        var instances
            = ImmutableArray.CreateBuilder<
                BoundGenericNominalTypeInstance>();
        var instanceIndexes = new Dictionary<
            StandardC1M1ClassTypeOnlyInstanceKey, int>();
        var declarations
            = new List<BoundStandardClassDeclaration>();
        var declarationIndexes = new Dictionary<
            BoundGenericNominalTypeIdentity, int>();
        var declarationNames = new HashSet<(string Namespace, string Name)>();
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
                || !reference.Structures.IsEmpty
                || !reference.Functions.IsEmpty
                || !reference.Classes.IsEmpty
                || !reference.NativeFunctionPointerSignatures.IsEmpty
                || !reference.NativePointerTypes.IsEmpty
                || !reference.NativeFixedArrayTypes.IsEmpty
                || !reference.EnumDefinitions.IsEmpty
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
                || reference.StandardQuerySourceAssociation is not null
                || !reference.CoreEvaluationFormIdentitySelections.IsEmpty
                || !reference.CoreEvaluationFormSchemaProviders.IsEmpty
                || !reference.SpanTypes.IsEmpty
                || reference.StandardClassDeclarations.IsEmpty
                || reference.GenericNominalTypeDefinitions.IsEmpty
                || reference.GenericNominalTypeInstances.IsEmpty)
            {
                throw Unsupported(
                    "C1M1 type-only class composition requires only selected class TypeDefs without member, interface, runtime, or other type-family obligations.");
            }

            var sourceDefinitions = reference.GenericNominalTypeDefinitions;
            if (!BoundGenericNominalTypeContract.IsValidDefinitionTable(
                    sourceDefinitions)
                || !BoundGenericNominalTypeContract.IsValidInstanceTable(
                    reference.GenericNominalTypeInstances,
                    sourceDefinitions))
            {
                throw Invalid(
                    "A selected C1M1 class reference has invalid nominal type tables.");
            }

            foreach (var definition in sourceDefinitions)
            {
                if (definition.NominalKind != 2
                    || !definition.Formals.IsEmpty
                    || !definition.ProviderExpectations.IsEmpty
                    || !StringComparer.Ordinal.Equals(
                        definition.Identity.ProviderIdentity,
                        definition.Identity.ModuleIdentity))
                {
                    throw Unsupported(
                        "C1M1 class composition currently requires directly selected nongeneric class TypeDefs without imported nominal expectations.");
                }

                if (definitionIndexes.TryGetValue(definition.Identity,
                        out var existingIndex))
                {
                    var existing = definitions[existingIndex];
                    if (!BoundGenericNominalTypeContract
                        .AreEquivalentDefinitions(existing, definition))
                    {
                        throw Invalid(
                            "Views of one C1M1 class TypeDef disagree on the actual provider definition.");
                    }
                }
                else
                {
                    if (definitions.Count
                        >= BoundGenericNominalTypeTypes.MaximumCount)
                    {
                        throw new NotSupportedException(
                            "The composed C1M1 nominal TypeDef closure exceeds the analysis limit.");
                    }

                    definitionIndexes.Add(definition.Identity,
                        definitions.Count);
                    definitions.Add(definition);
                }
            }

            var sourceInstances = reference.GenericNominalTypeInstances;
            for (var instanceIndex = 0;
                instanceIndex < sourceInstances.Length; instanceIndex++)
            {
                var instance = sourceInstances[instanceIndex];
                if (!instance.Arguments.IsEmpty
                    || instance.Qualifiers != 0
                    || instance.TypeSpecReference != 0
                    || instance.TypeSpecProviderIdentity is not null
                    || instance.TypeSpecForm != 0
                    || !definitionIndexes.ContainsKey(instance.Definition))
                {
                    throw Unsupported(
                        "C1M1 type-only class composition requires direct, unqualified class instances.");
                }

                var key = new StandardC1M1ClassTypeOnlyInstanceKey(
                    instance.Definition, instance.Qualifiers,
                    instance.TypeSpecReference,
                    instance.TypeSpecProviderIdentity,
                    instance.TypeSpecForm);
                if (!instanceIndexes.ContainsKey(key))
                {
                    if (instances.Count
                        >= BoundGenericNominalTypeTypes.MaximumCount)
                    {
                        throw new NotSupportedException(
                            "The composed C1M1 class instance table exceeds the analysis limit.");
                    }

                    var mappedInstanceIndex = instances.Count;
                    instanceIndexes.Add(key, mappedInstanceIndex);
                    instances.Add(instance);
                }

            }

            foreach (var declaration in reference.StandardClassDeclarations)
            {
                var view = declaration.Declaration;
                if (!StringComparer.Ordinal.Equals(view.Provider.SchemaIdentity,
                        StandardC1M1SchemaIdentity)
                    || !definitionIndexes.ContainsKey(view.Identity)
                    || !declaration.Expectations.IsEmpty
                    || declaration.SelectedExportKeys.IsDefaultOrEmpty
                    || declaration.ExportKeys.IsDefaultOrEmpty
                    || view.Fields.Any(field =>
                        !IsC1M1TypeOnlyComposedFieldType(field.Type))
                    || !view.Layouts.IsEmpty
                    || !view.Methods.IsEmpty)
                {
                    throw Unsupported(
                        "C1M1 class composition currently requires selected class roots with only primitive fields and without layouts, methods, or imported expectations.");
                }

                AddProviderSnapshot(view.Provider);
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
                        || !existing.ExportKeys.AsSpan().SequenceEqual(
                            declaration.ExportKeys.AsSpan()))
                    {
                        throw Invalid(
                            "Views of one C1M1 class TypeDef disagree on the checked declaration.");
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
                                "Views of one C1M1 class TypeDef disagree on a FieldDef declaration.");
                        }

                        var selectedFieldKeys = new List<string>(
                            existingField.SelectedExportKeys);
                        AddDistinct(selectedFieldKeys,
                            field.SelectedExportKeys);
                        mergedFields.Add(existingField with {
                            SelectedExportKeys = [.. selectedFieldKeys],
                        });
                    }

                    var selectedKeys = new List<string>(
                        existing.SelectedExportKeys);
                    AddDistinct(selectedKeys,
                        declaration.SelectedExportKeys);
                    declarations[existingIndex] = existing with {
                        Declaration = existingView with {
                            Fields = mergedFields.MoveToImmutable(),
                        },
                        SelectedExportKeys = [.. selectedKeys],
                    };
                }
                else
                {
                    if (!declarationNames.Add(nameIdentity))
                    {
                        throw Invalid(
                            "Selected C1M1 class references contain ambiguous fully qualified TypeDef names.");
                    }

                    declarationIndexes.Add(view.Identity,
                        declarations.Count);
                    declarations.Add(declaration);
                }
            }

            if (sourceDefinitions.Any(definition =>
                    !declarationIndexes.ContainsKey(definition.Identity)))
            {
                throw Unsupported(
                    "C1M1 class composition requires a retained source declaration for every nominal TypeDef.");
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
                "The composed C1M1 class TypeDef closure is invalid.");
        }

        var mergedProgram = new BoundProgram([], -1) {
            RuntimePolicy = runtimePolicy,
            GenericNominalTypeDefinitions = definitionTable,
            GenericNominalTypeInstances = instanceTable,
            StandardClassDeclarations = declarationTable,
        };
        SemanticVerifier.VerifyLibraryReferences(mergedProgram);
        return new CompilationReference([], [], runtimePolicy,
            genericNominalTypeDefinitions: definitionTable,
            genericNominalTypeInstances: instanceTable,
            standardClassDeclarations: declarationTable);

        void AddProviderSnapshot(BoundSourceTypeProviderSnapshot provider)
        {
            if (!StringComparer.Ordinal.Equals(provider.SchemaIdentity,
                    StandardC1M1SchemaIdentity)
                || provider.RuntimePolicy != (uint)runtimePolicy)
            {
                throw Invalid(
                    "C1M1 class references have an incompatible provider schema or runtime policy.");
            }

            var key = (provider.SchemaIdentity, provider.ModuleIdentity);
            if (providerSnapshots.TryGetValue(key, out var existing))
            {
                if (!SameA2SourceTypeProviderSnapshot(existing, provider))
                {
                    throw Invalid(
                        "C1M1 references contain conflicting snapshots for one provider Module.");
                }
            }
            else
            {
                providerSnapshots.Add(key, provider);
            }
        }
    }

    private static bool IsC1M1TypeOnlyComposedFieldType(PrimitiveType type)
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
                StandardInitReferenceBodiesNestedFieldDestinationsSchemaIdentity))
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
