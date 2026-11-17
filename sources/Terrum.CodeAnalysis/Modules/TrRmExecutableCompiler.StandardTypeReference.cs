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
    /// <summary>Creates a source reference from explicitly selected Standard TypeDef exports.</summary>
    /// <param name="input">The complete provider artifact and its exact schema selection.</param>
    /// <param name="moduleIdentity">The exact identity of the Module to expose.</param>
    /// <param name="providers">The complete explicitly supplied provider artifact set.</param>
    /// <param name="typeExportKeys">The exact ExportKeys that select source type roots.</param>
    /// <returns>The selected TypeDefs and their checked nominal closure.</returns>
    /// <exception cref="ArgumentNullException">An input, list or selected key is null.</exception>
    /// <exception cref="ArgumentException">A selected key is empty, duplicated, missing or has the wrong kind.</exception>
    /// <exception cref="NotSupportedException">The selected schema or type closure is outside the bounded projection.</exception>
    /// <exception cref="InvalidDataException">The selected artifact or type closure is invalid.</exception>
    public static CompilationReference CreateStandardReference(
        TrRmStandardArtifactInput input, string moduleIdentity,
        IReadOnlyList<TrRmStandardArtifactInput> providers,
        IReadOnlyList<string> typeExportKeys)
    {
        ArgumentException.ThrowIfNullOrEmpty(moduleIdentity);
        ArgumentNullException.ThrowIfNull(typeExportKeys);
        var exportKeys = new string[typeExportKeys.Count];
        var seenKeys = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < exportKeys.Length; index++)
        {
            var key = typeExportKeys[index];
            if (string.IsNullOrEmpty(key)
                || key.Contains('\0', StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "Type ExportKeys must be nonempty scalar text.",
                    nameof(typeExportKeys));
            }

            try
            {
                _ = s_sourceTypeExportKeyEncoding.GetByteCount(key);
            }
            catch (System.Text.EncoderFallbackException exception)
            {
                throw new ArgumentException(
                    "Type ExportKeys must be well-formed scalar text.",
                    nameof(typeExportKeys), exception);
            }

            if (!seenKeys.Add(key))
            {
                throw new ArgumentException(
                    "Type ExportKeys must be unique.",
                    nameof(typeExportKeys));
            }

            exportKeys[index] = key;
        }

        if (exportKeys.Length == 0)
        {
            throw new ArgumentException(
                "At least one TypeDef ExportKey must be selected.",
                nameof(typeExportKeys));
        }

        var artifacts = SnapshotStandardInputs(input, providers);
        var schemaIdentities = GetStandardSchemaIdentities(input, providers);
        if (StringComparer.Ordinal.Equals(schemaIdentities[0],
                StandardLayoutQueriesSchemaIdentity))
        {
            if (schemaIdentities.Any(static schemaIdentity =>
                    !StringComparer.Ordinal.Equals(schemaIdentity,
                        StandardLayoutQueriesSchemaIdentity)))
            {
                throw Unsupported(
                    "The LayoutQueries source-reference projection requires exact LayoutQueries selections for every provider.");
            }

            return CreateLayoutQueriesSourceTypeReference(artifacts,
                schemaIdentities, moduleIdentity, exportKeys, []);
        }
        if (IsSelectedC1SchemaIdentity(schemaIdentities[0]))
        {
            if (IsSelectedC1EnumClosureSchemaIdentity(schemaIdentities[0]))
            {
                if (schemaIdentities.Skip(1).Any(schemaIdentity =>
                        !StringComparer.Ordinal.Equals(schemaIdentity,
                            schemaIdentities[0])))
                {
                    throw Unsupported(
                        "The EN1 enum source-reference projection requires exact matching EN1 selections for every provider.");
                }

                return CreateC1EnumClosureSourceTypeReference(artifacts,
                    schemaIdentities, moduleIdentity, exportKeys);
            }

            if (!StringComparer.Ordinal.Equals(schemaIdentities[0],
                    StandardC1M1SchemaIdentity))
            {
                throw Unsupported(
                    "Complete Standard class declarations require the exact C1M1 schema.");
            }

            if (schemaIdentities.Skip(1).Any(schemaIdentity =>
                    !StringComparer.Ordinal.Equals(schemaIdentity,
                        schemaIdentities[0])))
            {
                throw Unsupported(
                    "The C1 class source-reference projection requires exact C1 or C1M1 selections for every provider.");
            }

            return CreateC1M1ClassSourceTypeReference(artifacts,
                schemaIdentities, moduleIdentity, exportKeys);
        }
        if (schemaIdentities[0] is not (
                StandardForeignInvocationStorageFixedArraySchemaIdentity
                or StandardForeignInvocationStorageFixedArrayEmptyInitializationSchemaIdentity
                or StandardForeignInvocationStorageFixedArrayEmptyUnitInitializationSchemaIdentity
                or StandardForeignInvocationStorageFixedArrayEmptyUnitScalarFrozenValuesSchemaIdentity
                or StandardForeignInvocationStorageFixedArrayCopyReturnCoverageSchemaIdentity
                or StandardForeignInvocationStorageFixedArrayCopyReturnCoverageRepeatInitializationSchemaIdentity
                or StandardLocalAggregateLayoutSchemaIdentity
                or StandardInitReferenceBodiesLocalFieldLayoutSchemaIdentity
                or StandardInitReferenceBodiesNestedFieldDestinationsSchemaIdentity))
        {
            throw Unsupported(
                "Explicit TypeDef roots require the exact A2, A2E or A2EU schema.");
        }
        if (schemaIdentities.Skip(1).Any(schemaIdentity =>
                schemaIdentity is not (
                    StandardForeignInvocationStorageFixedArraySchemaIdentity
                    or StandardForeignInvocationStorageFixedArrayEmptyInitializationSchemaIdentity
                    or StandardForeignInvocationStorageFixedArrayEmptyUnitInitializationSchemaIdentity
                    or StandardForeignInvocationStorageFixedArrayEmptyUnitScalarFrozenValuesSchemaIdentity
                    or StandardForeignInvocationStorageFixedArrayCopyReturnCoverageSchemaIdentity
                    or StandardForeignInvocationStorageFixedArrayCopyReturnCoverageRepeatInitializationSchemaIdentity
                    or StandardLocalAggregateLayoutSchemaIdentity
                    or StandardInitReferenceBodiesLocalFieldLayoutSchemaIdentity
                    or StandardInitReferenceBodiesNestedFieldDestinationsSchemaIdentity)))
        {
            throw Unsupported(
                "The bounded A2 type-reference projection requires A2, A2E or A2EU provider artifacts.");
        }

        var program = LoadStandardProgram(artifacts, schemaIdentities,
            moduleIdentity, true, null, out _,
            out _,
            out var typeProviderBindings, out var moduleValidation, out _);
        return CreateA2SourceTypeReference(artifacts, schemaIdentities,
            moduleIdentity, exportKeys, [], program,
            moduleValidation, typeProviderBindings);
    }

    /// <summary>Creates a source reference from explicitly selected type and field exports.</summary>
    /// <param name="input">The complete provider artifact and its exact schema selection.</param>
    /// <param name="moduleIdentity">The exact identity of the Module to expose.</param>
    /// <param name="providers">The complete explicitly supplied provider artifact set.</param>
    /// <param name="typeExportKeys">The exact TypeDef ExportKeys that select source type roots.</param>
    /// <param name="fieldExportKeys">The exact FieldDef ExportKeys selected within those roots' checked type closure.</param>
    /// <returns>The selected type and field declarations under the exact LayoutQueries or nested FieldPath schema.</returns>
    /// <exception cref="ArgumentNullException">An input, list or selected key is null.</exception>
    /// <exception cref="ArgumentException">A selected key is empty, duplicated, missing or has the wrong kind.</exception>
    /// <exception cref="NotSupportedException">The selected provider closure is outside the bounded projection.</exception>
    /// <exception cref="InvalidDataException">The selected artifact or provider closure is invalid.</exception>
    public static CompilationReference CreateStandardReference(
        TrRmStandardArtifactInput input, string moduleIdentity,
        IReadOnlyList<TrRmStandardArtifactInput> providers,
        IReadOnlyList<string> typeExportKeys,
        IReadOnlyList<string> fieldExportKeys)
    {
        ArgumentException.ThrowIfNullOrEmpty(moduleIdentity);
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(providers);
        var typeKeys = CaptureLayoutQueryExportKeys(typeExportKeys,
            nameof(typeExportKeys), requireOne: true);
        var fieldKeys = CaptureLayoutQueryExportKeys(fieldExportKeys,
            nameof(fieldExportKeys), requireOne: false);
        var artifacts = SnapshotStandardInputs(input, providers);
        var schemaIdentities = GetStandardSchemaIdentities(input, providers);
        if (schemaIdentities.All(static schemaIdentity =>
                StringComparer.Ordinal.Equals(schemaIdentity,
                    StandardLayoutQueriesSchemaIdentity)))
        {
            return CreateLayoutQueriesSourceTypeReference(artifacts,
                schemaIdentities, moduleIdentity, typeKeys, fieldKeys);
        }

        if (schemaIdentities.All(static schemaIdentity =>
                StringComparer.Ordinal.Equals(schemaIdentity,
                    StandardInitReferenceBodiesNestedFieldDestinationsSchemaIdentity)))
        {
            var program = LoadStandardProgram(artifacts, schemaIdentities,
                moduleIdentity, true, null, out _, out _,
                out var typeProviderBindings, out var moduleValidation,
                out _);
            return CreateA2SourceTypeReference(artifacts, schemaIdentities,
                moduleIdentity, typeKeys, fieldKeys, program,
                moduleValidation, typeProviderBindings);
        }

        if (schemaIdentities.Length == 0
            || schemaIdentities.Any(static schemaIdentity =>
                !StringComparer.Ordinal.Equals(schemaIdentity,
                    StandardLayoutQueriesSchemaIdentity)
                && !StringComparer.Ordinal.Equals(schemaIdentity,
                    StandardInitReferenceBodiesNestedFieldDestinationsSchemaIdentity)))
        {
            throw Unsupported(
                "The explicit type/field source reference requires exact LayoutQueries or nested FieldPath selections for every provider.");
        }

        throw Unsupported(
            "The explicit type/field source reference cannot mix schema identities.");
    }

    private static CompilationReference CreateC1M1ClassSourceTypeReference(
        byte[][] artifacts, string[] schemaIdentities, string moduleIdentity,
        string[] exportKeys)
    {
        var isEN1 = IsSelectedC1EnumClosureSchemaIdentity(
            schemaIdentities[0]);
        if ((!isEN1 && !StringComparer.Ordinal.Equals(
                    schemaIdentities[0], StandardC1M1SchemaIdentity))
            || schemaIdentities.Skip(1).Any(schemaIdentity =>
                !StringComparer.Ordinal.Equals(schemaIdentity,
                    schemaIdentities[0])))
        {
            throw Unsupported(
                "The C1 class source-reference projection requires one exact C1M1 or EN1 schema selection for every provider.");
        }

        var moduleValidation = ValidateStandardInputModules(artifacts,
            schemaIdentities, moduleIdentity, a3LayoutResourceLedger: null);
        var rootModuleRow = moduleValidation.RootModuleRow;
        var rootTables = ValidateForeignInvocationArtifact(
            CreateForeignInvocationIndex(artifacts[0], schemaIdentities[0]),
            schemaIdentities[0], null, createA3ResourceLedger: false);
        EnsureC1ClassDeclarationTablesSupported(rootTables);
        if (isEN1 && rootTables.Schema.UsesPropertyInitializedState())
        {
            throw Unsupported(
                "The bounded EN1 class source-reference projection does not yet retain PI1 property and initialized-state declarations.");
        }
        var (methodStart, methodEnd)
            = rootTables.Modules.GetMethodDefRange(rootModuleRow);
        if (rootTables.Modules.GetEntryMethod(rootModuleRow) != 0
            || methodStart != methodEnd)
        {
            throw Unsupported(
                "The C1 metadata-only class projection requires a type-only Module without MethodDefs.");
        }

        var selectedTypeDefs = new Dictionary<int, List<string>>();
        foreach (var exportKey in exportKeys)
        {
            if (!rootTables.Exports.TryGetDefinitionForKey(rootModuleRow,
                    exportKey, out var definition))
            {
                throw new ArgumentException(
                    "A selected TypeDef ExportKey is not published by the selected Module.",
                    nameof(exportKeys));
            }
            if ((definition >> 24) != 0x13)
            {
                throw new ArgumentException(
                    "A selected ExportKey does not identify a TypeDef.",
                    nameof(exportKeys));
            }

            var typeDefRow = checked((int)(definition & 0x00FF_FFFF));
            if (rootTables.TypeDefs.GetOwnerModule(typeDefRow)
                    != rootModuleRow
                || rootTables.TypeDefs.GetKind(typeDefRow) != 2
                || (rootTables.TypeDefs.GetFlags(typeDefRow) & 3) != 2)
            {
                throw Invalid(
                    "A selected C1 TypeDef Export does not identify a local public class.");
            }

            if (rootTables.TypeDefs.GetGenericRange(typeDefRow).Count != 0)
            {
                throw Unsupported(
                    "The C1 metadata-only class projection does not yet support generic class formals.");
            }
            if (rootTables.TypeDefs.GetContractRange(typeDefRow).Count != 0)
            {
                throw Unsupported(
                    "The C1 metadata-only class projection does not yet support TypeDef contracts.");
            }

            if (!selectedTypeDefs.TryGetValue(typeDefRow,
                    out var selectedKeys))
            {
                selectedKeys = [];
                selectedTypeDefs.Add(typeDefRow, selectedKeys);
            }
            selectedKeys.Add(exportKey);
        }

        ImmutableArray<TrRmStandardTypeProviderBinding>
            typeProviderBindings;
        if (isEN1)
        {
            var fieldValidation
                = TrRmFixedArrayProviderBindingValidator
                    .ValidateFieldDefImports(rootTables, artifacts,
                        schemaIdentities, moduleValidation.Modules,
                        rootModuleRow);
            if (fieldValidation.FieldBindings.Any(binding =>
                    binding.ConsumerArtifactIndex == 0
                    && binding.ConsumerModuleRow == rootModuleRow
                    && rootTables.TypeDefs.GetKind(
                        binding.ExpectedOwnerTypeDefRow) == 2))
            {
                throw Unsupported(
                    "The EN1 class source-reference projection does not yet support imported class FieldDefs.");
            }

            typeProviderBindings = fieldValidation.TypeBindings;
        }
        else
        {
            typeProviderBindings
                = TrRmFixedArrayProviderBindingValidator
                    .ValidateC1M1TypeDefImports(rootTables, artifacts,
                        schemaIdentities, moduleValidation.Modules,
                        rootModuleRow);
        }
        var rootSnapshot = CreateA2SourceTypeProviderSnapshot(rootTables,
            artifacts[0], schemaIdentities[0], rootModuleRow);

        var bindingsByActualType = new Dictionary<
            BoundGenericNominalTypeIdentity,
            List<BoundSourceTypeExpectation>>();
        var classExpectationsByActualType = new Dictionary<
            BoundGenericNominalTypeIdentity,
            List<BoundStandardClassExpectation>>();
        foreach (var binding in typeProviderBindings)
        {
            var consumerTables = binding.ConsumerArtifactIndex == 0
                ? rootTables
                : GetC1M1Tables(artifacts, schemaIdentities,
                    binding.ConsumerArtifactIndex);
            var providerTables = GetC1M1Tables(artifacts,
                schemaIdentities, binding.ProviderArtifactIndex);
            var expectedKind = consumerTables.TypeDefs.GetKind(
                binding.ExpectedTypeDefRow);
            var providerKind = providerTables.TypeDefs.GetKind(
                binding.ProviderTypeDefRow);
            if (isEN1 && expectedKind == 4 && providerKind == 4)
            {
                continue;
            }
            if (expectedKind is not (1 or 2)
                || providerKind != expectedKind
                || consumerTables.TypeDefs.GetGenericRange(
                    binding.ExpectedTypeDefRow).Count != 0
                || consumerTables.TypeDefs.GetContractRange(
                    binding.ExpectedTypeDefRow).Count != 0
                || providerTables.TypeDefs.GetGenericRange(
                    binding.ProviderTypeDefRow).Count != 0
                || providerTables.TypeDefs.GetContractRange(
                    binding.ProviderTypeDefRow).Count != 0)
            {
                throw Unsupported(
                    "The C1M1 class reference does not yet support imported interface, generic or contracted TypeDefs.");
            }

            var consumerModule = moduleValidation.Modules.Single(module =>
                module.ArtifactIndex == binding.ConsumerArtifactIndex
                && module.ModuleRow == binding.ConsumerModuleRow);
            var providerModule = moduleValidation.Modules.Single(module =>
                module.ArtifactIndex == binding.ProviderArtifactIndex
                && module.ModuleRow == binding.ProviderModuleRow);
            var consumerSnapshot = CreateA2SourceTypeProviderSnapshot(
                consumerTables, artifacts[binding.ConsumerArtifactIndex],
                schemaIdentities[binding.ConsumerArtifactIndex],
                binding.ConsumerModuleRow);
            var providerSnapshot = CreateA2SourceTypeProviderSnapshot(
                providerTables, artifacts[binding.ProviderArtifactIndex],
                schemaIdentities[binding.ProviderArtifactIndex],
                binding.ProviderModuleRow);
            var expectedDefinitionId = 0x1300_0000u
                | checked((uint)binding.ExpectedTypeDefRow);
            var providerDefinitionId = 0x1300_0000u
                | checked((uint)binding.ProviderTypeDefRow);
            var expectation = new BoundSourceTypeExpectation(
                consumerSnapshot, binding.ImportRow, expectedDefinitionId,
                binding.ProducerIdentity, binding.ExportKey,
                binding.TargetRequirement, binding.RuntimePolicy,
                providerSnapshot, providerDefinitionId);
            var actualIdentity = new BoundGenericNominalTypeIdentity(
                providerModule.Identity, providerModule.Identity,
                providerDefinitionId);
            if (!bindingsByActualType.TryGetValue(actualIdentity,
                    out var providerExpectations))
            {
                providerExpectations = [];
                bindingsByActualType.Add(actualIdentity,
                    providerExpectations);
            }
            providerExpectations.Add(expectation);

            if (consumerTables.TypeDefs.GetKind(
                    binding.ExpectedTypeDefRow) == 2)
            {
                var expectedIdentity = new BoundGenericNominalTypeIdentity(
                    consumerModule.Identity, consumerModule.Identity,
                    expectedDefinitionId);
                var expectedView = CreateStandardClassTypeView(
                    consumerTables, expectedIdentity, consumerSnapshot,
                    binding.ExpectedTypeDefRow);
                if (!classExpectationsByActualType.TryGetValue(
                        actualIdentity, out var classExpectations))
                {
                    classExpectations = [];
                    classExpectationsByActualType.Add(actualIdentity,
                        classExpectations);
                }
                classExpectations.Add(new BoundStandardClassExpectation(
                    expectation, expectedView));
            }
        }

        var localClassTypeDefRows = new SortedSet<int>();
        var pendingLocalClassTypeDefRows = new Queue<int>();
        foreach (var typeDefRow in selectedTypeDefs.Keys)
        {
            if (localClassTypeDefRows.Add(typeDefRow))
            {
                pendingLocalClassTypeDefRows.Enqueue(typeDefRow);
            }
        }
        if (isEN1)
        {
            for (var typeDefRow = 1;
                typeDefRow <= rootTables.TypeDefs.Count; typeDefRow++)
            {
                var definitionId = 0x1300_0000u
                    | checked((uint)typeDefRow);
                if (rootTables.TypeDefs.GetOwnerModule(typeDefRow)
                        == rootModuleRow
                    && rootTables.TypeDefs.GetKind(typeDefRow) == 2
                    && !rootTables.Imports.IsExpectedDefinition(definitionId)
                    && localClassTypeDefRows.Add(typeDefRow))
                {
                    if (localClassTypeDefRows.Count
                        > ManagedClassTypes.OptionalFirst
                            - ManagedClassTypes.First)
                    {
                        throw Unsupported(
                            "The C1 local class TypeSpec closure exceeds the registered class limit.");
                    }

                    pendingLocalClassTypeDefRows.Enqueue(typeDefRow);
                }
            }
        }
        var visitedClassTypeSpecs = new HashSet<uint>();
        var remainingClassTypeVisits
            = SemanticVerifier.MaximumAnalysisVisits;
        void VisitClassFieldTypeSpec(uint typeSpecReference, int depth)
        {
            if (--remainingClassTypeVisits < 0
                || depth > SemanticVerifier.MaximumDepth)
            {
                throw Unsupported(
                    "The C1 class TypeSpec closure exceeds its traversal limit.");
            }

            var type = rootTables.TypeSpecs.GetTypeSpecRef(
                typeSpecReference);
            if (type.Form == 0xF2 && type.Qualifiers == 0)
            {
                return;
            }
            if (!type.IsRow)
            {
                throw Unsupported(
                    "The C1 class TypeSpec closure contains an unsupported inline family.");
            }
            if (!visitedClassTypeSpecs.Add(typeSpecReference))
            {
                return;
            }

            var row = rootTables.TypeSpecs.GetRow(type.Row);
            if (type.Form == 0x06)
            {
                if (type.Qualifiers != 0)
                {
                    throw Unsupported(
                        "The C1 class TypeSpec closure does not support qualified optional wrappers.");
                }

                VisitClassFieldTypeSpec(BinaryPrimitives
                    .ReadUInt32LittleEndian(row[2..]), depth + 1);
                return;
            }

            if (type.Form == 0x03)
            {
                var importRow = checked((int)type.Row);
                var expectedDefinitionId = rootTables.Imports
                    .GetExpectedDefinitionForImportRow(importRow);
                if ((expectedDefinitionId >> 24) != 0x13)
                {
                    throw Invalid(
                        "A C1 class TypeSpec Import does not identify a TypeDef.");
                }

                var expectedTypeDefRow = checked((int)(
                    expectedDefinitionId & 0x00FF_FFFF));
                var expectedKind
                    = rootTables.TypeDefs.GetKind(expectedTypeDefRow);
                if (expectedKind is not (1 or 2)
                    && !(isEN1 && expectedKind == 4))
                {
                    throw Unsupported(
                        "The C1 class TypeSpec closure imports an unsupported TypeDef kind.");
                }

                return;
            }

            if (type.Form != 0x02)
            {
                throw Unsupported(
                    "The C1 class TypeSpec closure supports scalar, optional, and nominal TypeSpecs only.");
            }

            var typeDefRow = checked((int)type.Row);
            if (rootTables.TypeDefs.GetOwnerModule(typeDefRow)
                != rootModuleRow)
            {
                throw Invalid(
                    "A direct C1 class TypeSpec references a TypeDef owned by another Module.");
            }

            var kind = rootTables.TypeDefs.GetKind(typeDefRow);
            if (kind == 2)
            {
                var definitionId = 0x1300_0000u
                    | checked((uint)typeDefRow);
                if (rootTables.Imports.IsExpectedDefinition(definitionId))
                {
                    throw Invalid(
                        "A C1 class handle TypeSpec directly references an expected TypeDef stub.");
                }
                if (rootTables.TypeDefs.GetGenericRange(typeDefRow).Count != 0
                    || rootTables.TypeDefs.GetContractRange(typeDefRow).Count
                        != 0)
                {
                    throw Unsupported(
                        "The C1 class TypeSpec closure does not support generic or contracted local class dependencies.");
                }

                if (localClassTypeDefRows.Add(typeDefRow))
                {
                    if (localClassTypeDefRows.Count
                        > ManagedClassTypes.OptionalFirst
                            - ManagedClassTypes.First)
                    {
                        throw Unsupported(
                            "The C1 local class TypeSpec closure exceeds the registered class limit.");
                    }

                    pendingLocalClassTypeDefRows.Enqueue(typeDefRow);
                }
                return;
            }

            if ((kind == 1
                    && rootTables.TypeDefs.GetGenericRange(typeDefRow).Count == 0
                    && rootTables.TypeDefs.GetContractRange(typeDefRow).Count
                        == 0)
                || (isEN1 && kind == 4))
            {
                return;
            }

            throw Unsupported(
                "The C1 class TypeSpec closure supports only non-generic local structs, enum Types and local classes.");
        }

        while (pendingLocalClassTypeDefRows.TryDequeue(
            out var localClassTypeDefRow))
        {
            if (--remainingClassTypeVisits < 0)
            {
                throw Unsupported(
                    "The C1 local class TypeDef closure exceeds its traversal limit.");
            }

            var (fieldStart, fieldEnd)
                = rootTables.TypeDefs.GetFieldRange(localClassTypeDefRow);
            for (var fieldRow = fieldStart; fieldRow < fieldEnd; fieldRow++)
            {
                VisitClassFieldTypeSpec(
                    rootTables.Fields.GetTypeSpecRef(fieldRow), 1);
            }
        }

        var typeDefinitions = ImmutableArray.CreateBuilder<
            BoundGenericNominalTypeDefinition>();
        var definitionsByIdentity = new Dictionary<
            BoundGenericNominalTypeIdentity, int>();
        foreach (var typeDefRow in localClassTypeDefRows)
        {
            AddTypeDefinition(typeDefinitions, definitionsByIdentity,
                bindingsByActualType, rootTables, moduleIdentity,
                rootModuleRow, typeDefRow);
        }
        foreach (var binding in typeProviderBindings)
        {
            var providerTables = GetC1M1Tables(artifacts, schemaIdentities,
                binding.ProviderArtifactIndex);
            if (isEN1
                && providerTables.TypeDefs.GetKind(binding.ProviderTypeDefRow)
                    == 4)
            {
                continue;
            }
            if (providerTables.TypeDefs.GetKind(binding.ProviderTypeDefRow)
                is not (1 or 2))
            {
                throw Unsupported(
                    "The C1M1 class reference does not support interface TypeDef provider imports.");
            }

            var providerModule = moduleValidation.Modules.Single(module =>
                module.ArtifactIndex == binding.ProviderArtifactIndex
                && module.ModuleRow == binding.ProviderModuleRow);
            AddTypeDefinition(typeDefinitions, definitionsByIdentity,
                bindingsByActualType, providerTables,
                providerModule.Identity, binding.ProviderModuleRow,
                binding.ProviderTypeDefRow);
        }

        static void AddTypeDefinition(
            ImmutableArray<BoundGenericNominalTypeDefinition>.Builder
                typeDefinitions,
            Dictionary<BoundGenericNominalTypeIdentity, int>
                definitionsByIdentity,
            Dictionary<BoundGenericNominalTypeIdentity,
                List<BoundSourceTypeExpectation>> bindingsByActualType,
            TrRmStructuralMetadataValidator.ValidatedExecutionTables tables,
            string moduleIdentity, int providerModuleRow, int typeDefRow)
        {
            var identity = new BoundGenericNominalTypeIdentity(
                moduleIdentity, moduleIdentity,
                0x1300_0000u | checked((uint)typeDefRow));
            if (definitionsByIdentity.ContainsKey(identity))
            {
                return;
            }

            var definitions = CreateGenericNominalTypeDefinitions(tables,
                moduleIdentity, [], moduleRow: providerModuleRow,
                onlyTypeDefRow: typeDefRow);
            BoundGenericNominalTypeDefinition definition;
            if (definitions.Length == 0
                && tables.TypeDefs.GetKind(typeDefRow) == 2
                && tables.TypeDefs.GetGenericRange(typeDefRow).Count == 0
                && tables.TypeDefs.GetContractRange(typeDefRow).Count == 0
                && !tables.Exports.TryGetExportKeyForDefinition(
                    providerModuleRow, identity.DefinitionId, out _))
            {
                definition = new BoundGenericNominalTypeDefinition(
                    identity, tables.TypeDefs.GetNamespace(typeDefRow),
                    tables.TypeDefs.GetName(typeDefRow), 2, [],
                    tables.TypeSpecs.Count, tables.ConstantSources.Count,
                    tables.Constraints.Count,
                    tables.AssociatedRelations.Count);
            }
            else if (definitions.Length == 1)
            {
                definition = definitions[0];
            }
            else
            {
                throw Unsupported(
                    "The C1 class source-reference closure contains a non-public or unsupported nominal TypeDef.");
            }

            if (definition.Identity != identity)
            {
                throw Invalid(
                    "A C1 nominal provider definition has an unexpected identity.");
            }
            if (definition.NominalKind is 1 or 2
                && (!definition.Formals.IsEmpty
                    || tables.TypeDefs.GetContractRange(typeDefRow).Count
                        != 0))
            {
                throw Unsupported(
                    "The C1 metadata-only class projection does not yet support generic class or struct formals or TypeDef contracts.");
            }
            if (bindingsByActualType.TryGetValue(definition.Identity,
                    out var expectations))
            {
                definition = definition with {
                    ProviderExpectations = [.. expectations],
                };
            }

            definitionsByIdentity.Add(definition.Identity,
                typeDefinitions.Count);
            typeDefinitions.Add(definition);
        }

        var classDeclarations = ImmutableArray.CreateBuilder<
            BoundStandardClassDeclaration>();
        var classIdentities = new HashSet<
            BoundGenericNominalTypeIdentity>();
        foreach (var typeDefRow in selectedTypeDefs.Keys)
        {
            var identity = new BoundGenericNominalTypeIdentity(
                moduleIdentity, moduleIdentity,
                0x1300_0000u | checked((uint)typeDefRow));
            var declaration = CreateStandardClassTypeView(rootTables,
                identity, rootSnapshot, typeDefRow);
            classDeclarations.Add(new BoundStandardClassDeclaration(
                declaration,
                GetStandardTypeExportKeys(rootTables, rootModuleRow,
                    typeDefRow), []) {
                SelectedExportKeys = [.. selectedTypeDefs[typeDefRow]],
            });
            _ = classIdentities.Add(identity);
        }

        foreach (var typeDefRow in localClassTypeDefRows)
        {
            if (selectedTypeDefs.ContainsKey(typeDefRow))
            {
                continue;
            }

            var identity = new BoundGenericNominalTypeIdentity(
                moduleIdentity, moduleIdentity,
                0x1300_0000u | checked((uint)typeDefRow));
            if (!classIdentities.Add(identity))
            {
                throw Invalid(
                    "A local EN1 class TypeDef dependency was retained more than once.");
            }

            var declaration = CreateStandardClassTypeView(rootTables,
                identity, rootSnapshot, typeDefRow);
            classDeclarations.Add(new BoundStandardClassDeclaration(
                declaration, [], []));
        }

        foreach (var binding in typeProviderBindings)
        {
            var providerTables = GetC1M1Tables(artifacts, schemaIdentities,
                binding.ProviderArtifactIndex);
            if (providerTables.TypeDefs.GetKind(binding.ProviderTypeDefRow)
                != 2)
            {
                continue;
            }

            var providerModule = moduleValidation.Modules.Single(module =>
                module.ArtifactIndex == binding.ProviderArtifactIndex
                && module.ModuleRow == binding.ProviderModuleRow);
            var identity = new BoundGenericNominalTypeIdentity(
                providerModule.Identity, providerModule.Identity,
                0x1300_0000u | checked((uint)binding.ProviderTypeDefRow));
            if (!classIdentities.Add(identity))
            {
                continue;
            }

            var declaration = CreateStandardClassTypeView(providerTables,
                identity,
                CreateA2SourceTypeProviderSnapshot(providerTables,
                    artifacts[binding.ProviderArtifactIndex],
                    schemaIdentities[binding.ProviderArtifactIndex],
                    binding.ProviderModuleRow),
                binding.ProviderTypeDefRow);
            classDeclarations.Add(new BoundStandardClassDeclaration(
                declaration,
                GetStandardTypeExportKeys(providerTables,
                    binding.ProviderModuleRow, binding.ProviderTypeDefRow),
                classExpectationsByActualType.TryGetValue(identity,
                    out var expectations) ? [.. expectations] : []));
        }

        return new CompilationReference([], [],
            (BoundRuntimePolicy)rootTables.Modules
                .GetRequirements(rootModuleRow).RuntimePolicy,
            genericNominalTypeDefinitions: typeDefinitions.ToImmutable(),
            standardClassDeclarations: classDeclarations.ToImmutable());
    }

    private static BoundStandardClassTypeView CreateStandardClassTypeView(
        TrRmStructuralMetadataValidator.ValidatedExecutionTables tables,
        BoundGenericNominalTypeIdentity identity,
        BoundSourceTypeProviderSnapshot provider, int typeDefRow)
    {
        var typeDefId = 0x1300_0000u | checked((uint)typeDefRow);
        var (fieldStart, fieldEnd)
            = tables.TypeDefs.GetFieldRange(typeDefRow);
        var fields = ImmutableArray.CreateBuilder<
            BoundStandardClassFieldDeclaration>(fieldEnd - fieldStart);
        for (var fieldRow = fieldStart; fieldRow < fieldEnd; fieldRow++)
        {
            fields.Add(new BoundStandardClassFieldDeclaration(
                0x1400_0000u | checked((uint)fieldRow), typeDefId,
                tables.Fields.GetName(fieldRow),
                tables.Fields.GetFlags(fieldRow),
                tables.Fields.GetTypeSpecRef(fieldRow)));
        }

        var (layoutStart, layoutCount)
            = tables.TypeDefs.GetLayoutRange(typeDefRow);
        var layouts = ImmutableArray.CreateBuilder<
            BoundStandardClassLayoutVariant>(layoutCount);
        for (var layoutOffset = 0; layoutOffset < layoutCount;
            layoutOffset++)
        {
            var layoutRow = layoutStart + layoutOffset;
            var targetProfileReference
                = tables.Layouts.GetTargetProfileRef(layoutRow);
            var profile = targetProfileReference == 0
                ? default
                : tables.TargetProfiles.GetProfile(
                    checked((int)targetProfileReference));
            var (layoutFieldStart, layoutFieldCount)
                = tables.Layouts.GetLayoutFieldRange(layoutRow);
            var fieldIds = ImmutableArray.CreateBuilder<uint>(
                layoutFieldCount);
            for (var fieldOffset = 0; fieldOffset < layoutFieldCount;
                fieldOffset++)
            {
                var layoutFieldRow = layoutFieldStart + fieldOffset;
                fieldIds.Add(0x1400_0000u | checked((uint)
                    tables.LayoutFields.GetFieldDef(layoutFieldRow)));
            }

            layouts.Add(new BoundStandardClassLayoutVariant(
                (uint)layoutRow, typeDefId, targetProfileReference,
                profile.ProfileId, profile.RuleRevision,
                tables.Layouts.GetLayoutKind(layoutRow),
                tables.Layouts.GetFlags(layoutRow),
                tables.Layouts.GetAlignment(layoutRow),
                tables.Layouts.GetPack(layoutRow),
                fieldIds.MoveToImmutable()));
        }

        return new BoundStandardClassTypeView(identity,
            tables.TypeDefs.GetNamespace(typeDefRow),
            tables.TypeDefs.GetName(typeDefRow),
            tables.TypeDefs.GetFlags(typeDefRow), fields.MoveToImmutable(),
            layouts.MoveToImmutable(), provider);
    }

    private static void EnsureC1ClassDeclarationTablesSupported(
        TrRmStructuralMetadataValidator.ValidatedExecutionTables tables)
    {
        if (tables.Interfaces.Count != 0
            || tables.InterfaceBases.Count != 0
            || tables.InterfaceMethods.Count != 0
            || tables.InterfaceConstDefs.Count != 0
            || tables.InterfaceWitnesses.Count != 0
            || tables.WitnessMembers.Count != 0
            || tables.WitnessAssociatedTypes.Count != 0
            || tables.WitnessAssociatedConsts.Count != 0
            || tables.AssociatedTypeDefs.Count != 0
            || tables.AssociatedConstDefs.Count != 0
            || tables.AssociatedRelations.Count != 0)
        {
            throw Unsupported(
                "The C1 metadata-only class projection does not yet support interface or witness declarations.");
        }
    }

    private static TrRmStructuralMetadataValidator.ValidatedExecutionTables
        GetC1M1Tables(byte[][] artifacts, string[] schemaIdentities,
            int artifactIndex)
    {
        var schemaIdentity = schemaIdentities[artifactIndex];
        if (!StringComparer.Ordinal.Equals(schemaIdentity,
                StandardC1M1SchemaIdentity)
            && !IsSelectedC1EnumClosureSchemaIdentity(schemaIdentity))
        {
            throw Unsupported(
                "The C1 class provider closure requires exact C1M1 or EN1 selections.");
        }

        var tables = ValidateForeignInvocationArtifact(
            CreateForeignInvocationIndex(artifacts[artifactIndex],
                schemaIdentity), schemaIdentity, null,
            createA3ResourceLedger: false);
        EnsureC1ClassDeclarationTablesSupported(tables);
        return tables;
    }

    private static ImmutableArray<string> GetStandardTypeExportKeys(
        TrRmStructuralMetadataValidator.ValidatedExecutionTables tables,
        int moduleRow, int typeDefRow)
    {
        var definitionId = 0x1300_0000u | checked((uint)typeDefRow);
        var keys = ImmutableArray.CreateBuilder<string>();
        foreach (var (definition, exportKey) in tables.Exports
            .GetExportsForModule(moduleRow))
        {
            if (definition == definitionId)
            {
                keys.Add(exportKey);
            }
        }

        return keys.ToImmutable();
    }

    private static CompilationReference CreateC1EnumClosureSourceTypeReference(
        byte[][] artifacts, string[] schemaIdentities, string moduleIdentity,
        string[] selectedExportKeys)
    {
        var moduleValidation = ValidateStandardInputModules(artifacts,
            schemaIdentities, moduleIdentity, a3LayoutResourceLedger: null);
        var rootTables = ValidateForeignInvocationArtifact(
            CreateForeignInvocationIndex(artifacts[0], schemaIdentities[0]),
            schemaIdentities[0], null, createA3ResourceLedger: false);
        var selectedClassKeys = new List<string>();
        foreach (var exportKey in selectedExportKeys)
        {
            if (rootTables.Exports.TryGetDefinitionForKey(
                    moduleValidation.RootModuleRow, exportKey,
                    out var definition)
                && (definition >> 24) == 0x13
                && rootTables.TypeDefs.GetKind(checked((int)(
                    definition & 0x00FF_FFFF))) == 2)
            {
                selectedClassKeys.Add(exportKey);
            }
        }

        var (typeDefStart, typeDefEnd)
            = rootTables.Modules.GetTypeDefRange(
                moduleValidation.RootModuleRow);
        var hasClassTypeDefs = Enumerable.Range(typeDefStart,
                typeDefEnd - typeDefStart)
            .Any(typeDefRow =>
                rootTables.TypeDefs.GetKind(typeDefRow) == 2);
        if (selectedClassKeys.Count == 0 && !hasClassTypeDefs)
        {
            return CreateC1EnumClosureSourceTypeReferenceCore(artifacts,
                schemaIdentities, moduleIdentity, selectedExportKeys);
        }

        var classKeySet = selectedClassKeys.ToHashSet(
            StringComparer.Ordinal);
        var enumAndStructKeys = selectedExportKeys.Where(exportKey =>
            !classKeySet.Contains(exportKey)).ToArray();
        var classReference = CreateC1M1ClassSourceTypeReference(artifacts,
            schemaIdentities, moduleIdentity, [.. selectedClassKeys]);
        var enumAndStructReference
            = CreateC1EnumClosureSourceTypeReferenceCore(artifacts,
                schemaIdentities, moduleIdentity, enumAndStructKeys);
        if (classReference.RuntimePolicy
                != enumAndStructReference.RuntimePolicy)
        {
            throw Invalid(
                "The EN1 class and enum references selected different runtime policies.");
        }

        return new CompilationReference(
            enumAndStructReference.Structures,
            enumAndStructReference.Functions,
            enumAndStructReference.RuntimePolicy,
            enumDefinitions: enumAndStructReference.EnumDefinitions,
            genericNominalTypeDefinitions:
                classReference.GenericNominalTypeDefinitions,
            standardQuerySourceAssociation:
                enumAndStructReference.StandardQuerySourceAssociation,
            standardClassDeclarations:
                classReference.StandardClassDeclarations);
    }

    private static CompilationReference CreateC1EnumClosureSourceTypeReferenceCore(
        byte[][] artifacts, string[] schemaIdentities, string moduleIdentity,
        string[] selectedExportKeys)
    {
        var moduleValidation = ValidateStandardInputModules(artifacts,
            schemaIdentities, moduleIdentity, a3LayoutResourceLedger: null);
        var rootModuleRow = moduleValidation.RootModuleRow;
        var rootTables = ValidateForeignInvocationArtifact(
            CreateForeignInvocationIndex(artifacts[0], schemaIdentities[0]),
            schemaIdentities[0], null, createA3ResourceLedger: false);
        if (!rootTables.Schema.UsesC1EnumClosure())
        {
            throw Unsupported(
                "The bounded EN1 source-reference route requires an exact EN1 Module.");
        }

        var fieldProviderValidation
            = TrRmFixedArrayProviderBindingValidator
                .ValidateFieldDefImports(rootTables, artifacts,
                    schemaIdentities, moduleValidation.Modules,
                    rootModuleRow);
        var typeProviderBindings = fieldProviderValidation.TypeBindings;
        var tablesByArtifact
            = new Dictionary<int,
                TrRmStructuralMetadataValidator.ValidatedExecutionTables> {
                [0] = rootTables,
            };
        var snapshotsByModule
            = new Dictionary<(int ArtifactIndex, int ModuleRow),
                BoundSourceTypeProviderSnapshot>();
        var exportKeysByModule
            = new Dictionary<(int ArtifactIndex, int ModuleRow),
                Dictionary<uint, ImmutableArray<string>>>();

        TrRmStructuralMetadataValidator.ValidatedExecutionTables GetTables(
            int artifactIndex)
        {
            if (!tablesByArtifact.TryGetValue(artifactIndex,
                    out var tables))
            {
                tables = ValidateForeignInvocationArtifact(
                    CreateForeignInvocationIndex(artifacts[artifactIndex],
                        schemaIdentities[artifactIndex]),
                    schemaIdentities[artifactIndex], null,
                    createA3ResourceLedger: false);
                tablesByArtifact.Add(artifactIndex, tables);
            }

            return tables;
        }

        BoundSourceTypeProviderSnapshot GetSnapshot(int artifactIndex,
            int moduleRow)
        {
            var key = (artifactIndex, moduleRow);
            if (!snapshotsByModule.TryGetValue(key, out var snapshot))
            {
                snapshot = CreateA2SourceTypeProviderSnapshot(
                    GetTables(artifactIndex), artifacts[artifactIndex],
                    schemaIdentities[artifactIndex], moduleRow);
                snapshotsByModule.Add(key, snapshot);
            }

            return snapshot;
        }

        ImmutableArray<string> GetExportKeys(int artifactIndex,
            int moduleRow, int typeDefRow)
        {
            var key = (artifactIndex, moduleRow);
            if (!exportKeysByModule.TryGetValue(key, out var exports))
            {
                var tables = GetTables(artifactIndex);
                var keysByDefinition
                    = new Dictionary<uint, List<string>>();
                foreach (var (definition, exportKey) in tables.Exports
                    .GetExportsForModule(moduleRow))
                {
                    if (!keysByDefinition.TryGetValue(definition,
                            out var keys))
                    {
                        keys = [];
                        keysByDefinition.Add(definition, keys);
                    }

                    keys.Add(exportKey);
                }

                exports = keysByDefinition.ToDictionary(
                    static item => item.Key,
                    static item => item.Value.ToImmutableArray());
                exportKeysByModule.Add(key, exports);
            }

            var definitionId = 0x1300_0000u
                | checked((uint)typeDefRow);
            return exports.TryGetValue(definitionId, out var exportKeys)
                ? exportKeys : ImmutableArray<string>.Empty;
        }

        var (methodStart, methodEnd)
            = rootTables.Modules.GetMethodDefRange(rootModuleRow);
        var (typeDefStart, typeDefEnd)
            = rootTables.Modules.GetTypeDefRange(rootModuleRow);
        var typeDefCount = typeDefEnd - typeDefStart;
        var typeDefRows = Enumerable.Range(typeDefStart, typeDefCount)
            .ToArray();
        var structTypeDefRows = typeDefRows.Where(typeDefRow =>
            rootTables.TypeDefs.GetKind(typeDefRow) == 1).ToArray();
        var classTypeDefCount = typeDefRows.Count(typeDefRow =>
            rootTables.TypeDefs.GetKind(typeDefRow) == 2);
        var structureIndexByTypeDefRow = structTypeDefRows
            .Select((typeDefRow, structureIndex) => (typeDefRow,
                structureIndex))
            .ToDictionary(static item => item.typeDefRow,
                static item => item.structureIndex);
        var enumTypeDefCount = typeDefRows.Count(typeDefRow =>
            rootTables.TypeDefs.GetKind(typeDefRow) == 4);
        if (rootTables.Modules.GetEntryMethod(rootModuleRow) != 0
            || methodStart != methodEnd
            || typeDefCount == 0
            || typeDefCount > SemanticVerifier.MaximumAnalysisVisits
            || structTypeDefRows.Length
                > ManagedClassTypes.First - 100
            || classTypeDefCount
                > ManagedClassTypes.OptionalFirst - ManagedClassTypes.First
            || enumTypeDefCount > BoundEnumTypes.MaximumCount
            || typeDefRows.Any(typeDefRow =>
                rootTables.TypeDefs.GetKind(typeDefRow)
                    is not (1 or 2 or 4)))
        {
            throw Unsupported(
                "The bounded EN1 source-reference route requires an enum and non-generic struct declaration Module within the supported limits.");
        }
        var selectedByTypeDef = new Dictionary<int, List<string>>();
        foreach (var exportKey in selectedExportKeys)
        {
            if (!rootTables.Exports.TryGetDefinitionForKey(rootModuleRow,
                    exportKey, out var definitionId))
            {
                throw new ArgumentException(
                    "A selected TypeDef ExportKey is not published by the selected EN1 Module.",
                    nameof(selectedExportKeys));
            }
            if ((definitionId >> 24) != 0x13)
            {
                throw new ArgumentException(
                    "A selected ExportKey does not identify a TypeDef.",
                    nameof(selectedExportKeys));
            }

            var typeDefRow = checked((int)(definitionId & 0x00FF_FFFF));
            if (typeDefRow < typeDefStart
                || typeDefRow >= typeDefEnd
                || rootTables.TypeDefs.GetOwnerModule(typeDefRow)
                    != rootModuleRow
                || rootTables.TypeDefs.GetKind(typeDefRow) is not (1 or 4)
                || rootTables.Imports.IsExpectedDefinition(definitionId)
                || (rootTables.TypeDefs.GetFlags(typeDefRow) & 3) != 2)
            {
                throw Invalid(
                    "A selected EN1 TypeDef Export does not identify a local public enum or struct.");
            }
            if (rootTables.TypeDefs.GetKind(typeDefRow) == 1
                && (rootTables.TypeDefs.GetGenericRange(typeDefRow).Count != 0
                    || rootTables.TypeDefs.GetContractRange(typeDefRow).Count != 0))
            {
                throw Unsupported(
                    "The bounded EN1 struct type-reference route supports only non-generic, uncontracted local TypeDefs.");
            }

            if (!selectedByTypeDef.TryGetValue(typeDefRow,
                    out var selectedKeys))
            {
                selectedKeys = [];
                selectedByTypeDef.Add(typeDefRow, selectedKeys);
            }
            selectedKeys.Add(exportKey);
        }

        var importedViewsByActual = new Dictionary<
            (int ArtifactIndex, int ModuleRow, int TypeDefRow),
            BoundStandardEnumView>();
        var originalViewsByActual = new Dictionary<
            (int ArtifactIndex, int ModuleRow, int TypeDefRow),
            List<BoundStandardEnumView>>();
        var expectationsByActual = new Dictionary<
            (int ArtifactIndex, int ModuleRow, int TypeDefRow),
            List<BoundStandardEnumExpectation>>();
        foreach (var binding in typeProviderBindings)
        {
            var consumerTables = GetTables(binding.ConsumerArtifactIndex);
            var providerTables = GetTables(binding.ProviderArtifactIndex);
            var expectedTypeDefRow = binding.ExpectedTypeDefRow;
            var providerTypeDefRow = binding.ProviderTypeDefRow;
            var expectedKind
                = consumerTables.TypeDefs.GetKind(expectedTypeDefRow);
            var providerKind
                = providerTables.TypeDefs.GetKind(providerTypeDefRow);
            if (expectedKind != providerKind)
            {
                if (binding.ConsumerArtifactIndex == 0
                    && binding.ConsumerModuleRow == rootModuleRow)
                {
                    throw Invalid(
                        "An EN1 TypeDef Import changed its declaration kind.");
                }

                continue;
            }
            if (expectedKind == 1)
            {
                if (binding.ConsumerArtifactIndex == 0
                    && binding.ConsumerModuleRow == rootModuleRow
                    && (!structureIndexByTypeDefRow.ContainsKey(
                            expectedTypeDefRow)
                        || consumerTables.TypeDefs.GetGenericRange(
                            expectedTypeDefRow).Count != 0
                        || consumerTables.TypeDefs.GetContractRange(
                            expectedTypeDefRow).Count != 0
                        || providerTables.TypeDefs.GetGenericRange(
                            providerTypeDefRow).Count != 0
                        || providerTables.TypeDefs.GetContractRange(
                            providerTypeDefRow).Count != 0))
                {
                    throw Unsupported(
                        "The bounded EN1 source-reference route supports only local non-generic uncontracted struct Import stubs.");
                }

                continue;
            }
            if (expectedKind == 2)
            {
                continue;
            }
            if (expectedKind != 4)
            {
                if (binding.ConsumerArtifactIndex == 0
                    && binding.ConsumerModuleRow == rootModuleRow)
                {
                    throw Unsupported(
                        "The bounded EN1 source-reference route supports imported enum and non-generic struct TypeDefs only.");
                }
                continue;
            }

            var consumerSnapshot = GetSnapshot(
                binding.ConsumerArtifactIndex,
                binding.ConsumerModuleRow);
            var providerSnapshot = GetSnapshot(
                binding.ProviderArtifactIndex,
                binding.ProviderModuleRow);
            var expectedView = CreateStandardEnumView(consumerTables,
                consumerSnapshot, expectedTypeDefRow);
            var actualView = CreateStandardEnumView(providerTables,
                providerSnapshot, providerTypeDefRow);
            var expectedDefinitionId = 0x1300_0000u
                | checked((uint)expectedTypeDefRow);
            var providerDefinitionId = 0x1300_0000u
                | checked((uint)providerTypeDefRow);
            var expectation = new BoundStandardEnumExpectation(
                new BoundSourceTypeExpectation(consumerSnapshot,
                    binding.ImportRow, expectedDefinitionId,
                    binding.ProducerIdentity, binding.ExportKey,
                    binding.TargetRequirement, binding.RuntimePolicy,
                    providerSnapshot, providerDefinitionId),
                expectedView);
            var actualKey = (binding.ProviderArtifactIndex,
                binding.ProviderModuleRow, providerTypeDefRow);
            if (importedViewsByActual.TryGetValue(actualKey,
                    out var existingView))
            {
                if (!BoundStandardEnumDeclarationContract.ContractMatches(
                        existingView, actualView))
                {
                    throw Invalid(
                        "An EN1 provider site has inconsistent enum snapshots.");
                }
            }
            else
            {
                importedViewsByActual.Add(actualKey, actualView);
                originalViewsByActual.Add(actualKey, [actualView]);
                expectationsByActual.Add(actualKey, []);
            }

            var originalViews = originalViewsByActual[actualKey];
            var expectedSite
                = BoundStandardEnumDeclarationContract.GetSiteIdentity(
                    expectedView.Site);
            if (!originalViews.Any(original =>
                    BoundStandardEnumDeclarationContract.GetSiteIdentity(
                        original.Site) == expectedSite))
            {
                originalViews.Add(expectedView);
            }

            expectationsByActual[actualKey].Add(expectation);
        }

        var definitions
            = ImmutableArray.CreateBuilder<BoundEnumDefinition>(
                checked(typeDefCount + importedViewsByActual.Count));
        var enumIndexByTypeDefRow = new Dictionary<int, int>();
        var enumIndexByActual = new Dictionary<
            (int ArtifactIndex, int ModuleRow, int TypeDefRow), int>();
        var rootSnapshot = GetSnapshot(0, rootModuleRow);
        for (var offset = 0; offset < typeDefCount; offset++)
        {
            var typeDefRow = typeDefStart + offset;
            if (rootTables.TypeDefs.GetOwnerModule(typeDefRow)
                    != rootModuleRow)
            {
                throw Unsupported(
                    "The bounded EN1 source-reference route cannot root a TypeDef owned by another Module.");
            }
            if (rootTables.TypeDefs.GetKind(typeDefRow) == 1)
            {
                var definitionId = 0x1300_0000u
                    | checked((uint)typeDefRow);
                if (rootTables.TypeDefs.GetGenericRange(typeDefRow).Count
                        != 0
                    || rootTables.TypeDefs.GetContractRange(typeDefRow).Count
                        != 0)
                {
                    throw Unsupported(
                        "The bounded EN1 struct type-reference route requires non-generic TypeDefs without contracts.");
                }
                if (rootTables.Imports.IsExpectedDefinition(definitionId)
                    && (selectedByTypeDef.ContainsKey(typeDefRow)
                        || !GetExportKeys(0, rootModuleRow,
                            typeDefRow).IsEmpty))
                {
                    throw Invalid(
                        "An EN1 expected struct declaration cannot be selected or re-exported.");
                }
                continue;
            }
            if (rootTables.TypeDefs.GetKind(typeDefRow) == 2)
            {
                continue;
            }
            if (rootTables.TypeDefs.GetKind(typeDefRow) != 4)
            {
                throw Unsupported(
                    "The bounded EN1 source-reference route does not support this TypeDef kind.");
            }

            var definitionId = 0x1300_0000u
                | checked((uint)typeDefRow);
            if (rootTables.Imports.IsExpectedDefinition(definitionId))
            {
                if (selectedByTypeDef.ContainsKey(typeDefRow)
                    || !GetExportKeys(0, rootModuleRow,
                        typeDefRow).IsEmpty)
                {
                    throw Invalid(
                        "An EN1 expected enum declaration cannot be selected or re-exported.");
                }

                continue;
            }

            var view = CreateStandardEnumView(rootTables, rootSnapshot,
                typeDefRow);
            var exportKeys = GetExportKeys(0, rootModuleRow, typeDefRow);
            var selectedKeysForType
                = selectedByTypeDef.TryGetValue(typeDefRow,
                    out var selected)
                    ? selected.ToImmutableArray()
                    : ImmutableArray<string>.Empty;
            var occurrence = new BoundStandardEnumOccurrence(view, [view],
                [], exportKeys, selectedKeysForType);
            enumIndexByTypeDefRow.Add(typeDefRow, definitions.Count);
            definitions.Add(CreateStandardEnumDefinition(view, occurrence));
        }

        foreach (var actualKey in importedViewsByActual.Keys
            .OrderBy(static key => key.ArtifactIndex)
            .ThenBy(static key => key.ModuleRow)
            .ThenBy(static key => key.TypeDefRow))
        {
            var view = importedViewsByActual[actualKey];
            var exportKeys = GetExportKeys(actualKey.ArtifactIndex,
                actualKey.ModuleRow, actualKey.TypeDefRow);
            var occurrence = new BoundStandardEnumOccurrence(view,
                [.. originalViewsByActual[actualKey]],
                [.. expectationsByActual[actualKey]], exportKeys, []);
            enumIndexByActual.Add(actualKey, definitions.Count);
            definitions.Add(CreateStandardEnumDefinition(view, occurrence));
        }

        var enumDefinitions = definitions.ToImmutable();
        if (!BoundEnumTypes.IsValidTable(enumDefinitions))
        {
            throw Invalid(
                "The EN1 enum declaration closure cannot be reconstructed as a checked type table.");
        }

        var enumIndexByImportRow = new Dictionary<int, int>();
        var structureIndexByImportRow = new Dictionary<int, int>();
        foreach (var binding in typeProviderBindings)
        {
            if (binding.ConsumerArtifactIndex != 0
                || binding.ConsumerModuleRow != rootModuleRow)
            {
                continue;
            }

            var expectedKind = rootTables.TypeDefs.GetKind(
                binding.ExpectedTypeDefRow);
            if (expectedKind == 1)
            {
                if (!structureIndexByTypeDefRow.TryGetValue(
                        binding.ExpectedTypeDefRow,
                        out var structureIndex)
                    || !structureIndexByImportRow.TryAdd(
                        binding.ImportRow, structureIndex))
                {
                    throw Invalid(
                        "An EN1 imported struct FieldDef type lost its expected TypeDef view.");
                }
                continue;
            }
            if (expectedKind == 2)
            {
                continue;
            }

            var actualKey = (binding.ProviderArtifactIndex,
                binding.ProviderModuleRow, binding.ProviderTypeDefRow);
            if (expectedKind != 4
                || !enumIndexByActual.TryGetValue(actualKey,
                    out var enumIndex)
                || !enumIndexByImportRow.TryAdd(binding.ImportRow,
                    enumIndex))
            {
                throw Invalid(
                    "An EN1 imported enum FieldDef type lost its provider occurrence.");
            }
        }

        var structures = structTypeDefRows.Length == 0
            ? ImmutableArray<BoundStruct>.Empty
            : CreateC1EnumClosureStructSourceTypes(rootTables,
                rootSnapshot, rootModuleRow, structTypeDefRows,
                selectedByTypeDef, enumIndexByTypeDefRow,
                enumIndexByImportRow, structureIndexByImportRow,
                enumDefinitions, typeProviderBindings,
                fieldProviderValidation.FieldBindings, GetTables,
                GetSnapshot, GetExportKeys);
        var program = new BoundProgram([], -1) {
            RuntimePolicy = (BoundRuntimePolicy)rootTables.Modules
                .GetRequirements(rootModuleRow).RuntimePolicy,
            Structures = structures,
            EnumDefinitions = enumDefinitions,
        };
        if (!structures.IsEmpty)
        {
            SemanticVerifier.VerifyLibraryReferences(program);
        }
        if (!fieldProviderValidation.FieldBindings.IsEmpty)
        {
            program = RetainStandardFieldProviderClosure(program,
                fieldProviderValidation, artifacts, schemaIdentities,
                moduleValidation.Modules, rootModuleRow, moduleIdentity);
        }
        var standardQuerySourceAssociation
            = program.StandardFieldProviderClosure is
                BoundStandardFieldProviderClosure fieldProviderClosure
                ? new BoundStandardQuerySourceAssociation([
                    fieldProviderClosure,
                ])
                : null;
        return new CompilationReference(structures, [],
            (BoundRuntimePolicy)rootTables.Modules
                .GetRequirements(rootModuleRow).RuntimePolicy,
            standardQuerySourceAssociation:
                standardQuerySourceAssociation,
            enumDefinitions: enumDefinitions);
    }

    private static ImmutableArray<BoundStruct>
        CreateC1EnumClosureStructSourceTypes(
        TrRmStructuralMetadataValidator.ValidatedExecutionTables tables,
        BoundSourceTypeProviderSnapshot provider, int moduleRow,
        int[] typeDefRows, Dictionary<int, List<string>> selectedByTypeDef,
        Dictionary<int, int> enumIndexByTypeDefRow,
        Dictionary<int, int> enumIndexByImportRow,
        Dictionary<int, int> structureIndexByImportRow,
        ImmutableArray<BoundEnumDefinition> enumDefinitions,
        ImmutableArray<TrRmStandardTypeProviderBinding> typeProviderBindings,
        ImmutableArray<TrRmStandardFieldProviderBinding>
            fieldProviderBindings,
        Func<int,
            TrRmStructuralMetadataValidator.ValidatedExecutionTables>
            getTables,
        Func<int, int, BoundSourceTypeProviderSnapshot> getSnapshot,
        Func<int, int, int, ImmutableArray<string>> getExportKeys)
    {
        var structureIndexByTypeDefRow = new Dictionary<int, int>(
            typeDefRows.Length);
        var typeBindingsByExpectedTypeDef = new Dictionary<int,
            List<TrRmStandardTypeProviderBinding>>();
        var fieldBindingsByExpectedField = new Dictionary<
            (int TypeDefRow, int FieldOrdinal),
            List<TrRmStandardFieldProviderBinding>>();
        for (var rowIndex = 0; rowIndex < typeDefRows.Length; rowIndex++)
        {
            var typeDefRow = typeDefRows[rowIndex];
            var flags = tables.TypeDefs.GetFlags(typeDefRow);
            if (tables.TypeDefs.GetKind(typeDefRow) != 1
                || tables.TypeDefs.GetOwnerModule(typeDefRow) != moduleRow
                || (flags & ~0x07u) != 0
                || (flags & 3) == 3
                || tables.TypeDefs.GetGenericRange(typeDefRow).Count != 0
                || tables.TypeDefs.GetContractRange(typeDefRow).Count != 0
                || !tables.TypeDefs.HasDeclaredCopy(typeDefRow)
                || (tables.Imports.IsExpectedDefinition(
                        0x1300_0000u | checked((uint)typeDefRow))
                    && !typeBindingsByExpectedTypeDef.ContainsKey(
                        typeDefRow)))
            {
                throw Unsupported(
                    "The bounded EN1 source struct reference requires local or bound non-generic Copy TypeDefs without contracts.");
            }

            var (layoutFirst, layoutCount)
                = tables.TypeDefs.GetLayoutRange(typeDefRow);
            if (layoutCount != 1)
            {
                throw Unsupported(
                    "The bounded EN1 source struct reference supports one default Auto Layout variant.");
            }

            var layout = layoutFirst;
            if (tables.Layouts.GetTargetProfileRef(layout) != 0
                || tables.Layouts.GetLayoutKind(layout) != 0
                || tables.Layouts.GetFlags(layout) != 0
                || tables.Layouts.GetAlignment(layout) != 0
                || tables.Layouts.GetPack(layout) != 0)
            {
                throw Unsupported(
                    "The bounded EN1 source struct reference supports only the default Auto Layout variant.");
            }

            structureIndexByTypeDefRow.Add(typeDefRow, rowIndex);
        }

        foreach (var binding in typeProviderBindings)
        {
            if (binding.ConsumerArtifactIndex != 0
                || binding.ConsumerModuleRow != moduleRow
                || !structureIndexByTypeDefRow.ContainsKey(
                    binding.ExpectedTypeDefRow))
            {
                continue;
            }

            if (!typeBindingsByExpectedTypeDef.TryGetValue(
                    binding.ExpectedTypeDefRow, out var bindings))
            {
                bindings = [];
                typeBindingsByExpectedTypeDef.Add(
                    binding.ExpectedTypeDefRow, bindings);
            }
            bindings.Add(binding);
        }

        foreach (var binding in fieldProviderBindings)
        {
            if (binding.ConsumerArtifactIndex != 0
                || binding.ConsumerModuleRow != moduleRow)
            {
                continue;
            }

            var expectedOwnerTypeDefRow
                = binding.ExpectedOwnerTypeDefRow;
            if (!structureIndexByTypeDefRow.ContainsKey(
                    expectedOwnerTypeDefRow))
            {
                throw Invalid(
                    "An EN1 FieldDef Import has an owner outside the retained struct closure.");
            }

            var (fieldStart, fieldEnd)
                = tables.TypeDefs.GetFieldRange(expectedOwnerTypeDefRow);
            if (binding.ExpectedFieldDefRow < fieldStart
                || binding.ExpectedFieldDefRow >= fieldEnd
                || tables.Fields.GetOwnerTypeDef(
                    binding.ExpectedFieldDefRow)
                    != expectedOwnerTypeDefRow)
            {
                throw Invalid(
                    "An EN1 FieldDef Import is outside its expected owner.");
            }

            var key = (expectedOwnerTypeDefRow,
                binding.ExpectedFieldDefRow - fieldStart);
            if (!fieldBindingsByExpectedField.TryGetValue(key,
                    out var bindings))
            {
                bindings = [];
                fieldBindingsByExpectedField.Add(key, bindings);
            }
            bindings.Add(binding);
        }

        var exportsByField = new Dictionary<int, List<string>>();
        foreach (var (definition, exportKey) in tables.Exports
            .GetExportsForModule(moduleRow))
        {
            if ((definition >> 24) != 0x14)
            {
                continue;
            }

            var fieldRow = checked((int)(definition & 0x00FF_FFFF));
            if (!structureIndexByTypeDefRow.ContainsKey(
                    tables.Fields.GetOwnerTypeDef(fieldRow)))
            {
                throw Invalid(
                    "An EN1 FieldDef Export is outside the retained struct closure.");
            }
            if (tables.Imports.IsExpectedDefinition(
                    0x1300_0000u | checked((uint)
                        tables.Fields.GetOwnerTypeDef(fieldRow))))
            {
                throw Invalid(
                    "An EN1 imported struct TypeDef cannot re-export its expected FieldDefs.");
            }
            if (!exportsByField.TryGetValue(fieldRow, out var keys))
            {
                keys = [];
                exportsByField.Add(fieldRow, keys);
            }
            keys.Add(exportKey);
        }

        PrimitiveType DecodeFieldType(uint typeSpecReference)
        {
            var type = tables.TypeSpecs.GetTypeSpecRef(typeSpecReference);
            if (type.Form == 0xF2 && type.Qualifiers == 0
                && TrRmConstantSourceValueValidator.TryGetBoundScalarType(
                    type.TypeCode, out var scalarType)
                && scalarType != PrimitiveType.Void)
            {
                if (type.TypeCode is 0x10 or 0x11
                    && tables.Modules.GetRequirements(moduleRow)
                        .TargetRequirement
                        != TrRmModuleTableReader
                            .WindowsX64NativeTargetRequirement)
                {
                    throw Unsupported(
                        "EN1 native-width struct fields require the selected Windows x64 target.");
                }

                return scalarType;
            }

            if (type.Form == 0x02 && type.Qualifiers == 0
                && structureIndexByTypeDefRow.TryGetValue(type.Row,
                    out var structureIndex))
            {
                return (PrimitiveType)(100 + structureIndex);
            }

            if (type.Form == 0x02 && type.Qualifiers == 0
                && enumIndexByTypeDefRow.TryGetValue(type.Row,
                    out var enumIndex))
            {
                var definition = enumDefinitions[enumIndex];
                if ((definition.UnderlyingType is PrimitiveType.NativeInt
                        or PrimitiveType.NativeUInt)
                    && tables.Modules.GetRequirements(moduleRow)
                        .TargetRequirement
                        != TrRmModuleTableReader
                            .WindowsX64NativeTargetRequirement)
                {
                    throw Unsupported(
                        "EN1 native-width enum fields require the selected Windows x64 target.");
                }

                return BoundEnumTypes.Type(enumIndex);
            }

            if (type.Form == 0x03 && type.Qualifiers == 0
                && structureIndexByImportRow.TryGetValue(type.Row,
                    out var importedStructureIndex))
            {
                return (PrimitiveType)(100 + importedStructureIndex);
            }

            if (type.Form == 0x03 && type.Qualifiers == 0
                && enumIndexByImportRow.TryGetValue(type.Row,
                    out var importedEnumIndex))
            {
                var definition = enumDefinitions[importedEnumIndex];
                if ((definition.UnderlyingType is PrimitiveType.NativeInt
                        or PrimitiveType.NativeUInt)
                    && tables.Modules.GetRequirements(moduleRow)
                        .TargetRequirement
                        != TrRmModuleTableReader
                            .WindowsX64NativeTargetRequirement)
                {
                    throw Unsupported(
                        "EN1 native-width enum fields require the selected Windows x64 target.");
                }

                return BoundEnumTypes.Type(importedEnumIndex);
            }

            throw Unsupported(
                "The bounded EN1 struct reference supports only scalar, local/imported enum and local/imported non-generic struct FieldDefs.");
        }

        var structures = new List<BoundStruct>(typeDefRows.Length);
        for (var rowIndex = 0; rowIndex < typeDefRows.Length; rowIndex++)
        {
            var typeDefRow = typeDefRows[rowIndex];
            var typeDefId = 0x1300_0000u
                | checked((uint)typeDefRow);
            var (fieldStart, fieldEnd)
                = tables.TypeDefs.GetFieldRange(typeDefRow);
            var fields = ImmutableArray.CreateBuilder<BoundStructField>(
                fieldEnd - fieldStart);
            for (var fieldRow = fieldStart; fieldRow < fieldEnd;
                fieldRow++)
            {
                var flags = tables.Fields.GetFlags(fieldRow);
                if ((flags & ~0x07u) != 0 || (flags & 3) == 3)
                {
                    throw Unsupported(
                        "The bounded EN1 source struct reference cannot discard FieldDef access or ownership flags.");
                }

                fields.Add(new BoundStructField(
                    tables.Fields.GetName(fieldRow),
                    DecodeFieldType(
                        tables.Fields.GetTypeSpecRef(fieldRow)), 0, 0) {
                    IsPrivate = (flags & 3) == 0,
                    IsInternal = (flags & 3) == 1,
                    IsMutable = (flags & 0x04) != 0,
                });
            }

            var (layoutStart, layoutCount)
                = tables.TypeDefs.GetLayoutRange(typeDefRow);
            var (layoutFieldFirst, layoutFieldCount)
                = tables.Layouts.GetLayoutFieldRange(layoutStart);
            if (layoutFieldCount != fields.Count)
            {
                throw Invalid(
                    "An EN1 source struct Layout does not describe its complete FieldDef order.");
            }
            for (var ordinal = 0; ordinal < layoutFieldCount; ordinal++)
            {
                var layoutFieldRow = layoutFieldFirst + ordinal;
                if (tables.LayoutFields.GetOwnerLayout(layoutFieldRow)
                        != layoutStart
                    || tables.LayoutFields.GetOrdinal(layoutFieldRow)
                        != ordinal
                    || tables.LayoutFields.GetFieldDef(layoutFieldRow)
                        != fieldStart + ordinal)
                {
                    throw Invalid(
                        "An EN1 source struct Layout does not preserve declaration field order.");
                }
            }

            var typeDefFlags = tables.TypeDefs.GetFlags(typeDefRow);
            var namespaceName = tables.TypeDefs.GetNamespace(typeDefRow);
            var typeName = tables.TypeDefs.GetName(typeDefRow);
            BoundStandardTypeOrigin origin;
            if (typeBindingsByExpectedTypeDef.TryGetValue(typeDefRow,
                    out var bindings))
            {
                var firstBinding = bindings[0];
                if (selectedByTypeDef.ContainsKey(typeDefRow)
                    || bindings.Any(binding =>
                        binding.ProviderArtifactIndex
                            != firstBinding.ProviderArtifactIndex
                        || binding.ProviderModuleRow
                            != firstBinding.ProviderModuleRow
                        || binding.ProviderTypeDefRow
                            != firstBinding.ProviderTypeDefRow
                        || !StringComparer.Ordinal.Equals(
                            binding.ProducerIdentity,
                            firstBinding.ProducerIdentity)
                        || binding.TargetRequirement
                            != firstBinding.TargetRequirement
                        || binding.RuntimePolicy
                            != firstBinding.RuntimePolicy))
                {
                    throw Invalid(
                        "One EN1 expected struct TypeDef has conflicting provider origins.");
                }

                var providerSnapshot = getSnapshot(
                    firstBinding.ProviderArtifactIndex,
                    firstBinding.ProviderModuleRow);
                var expectations
                    = ImmutableArray.CreateBuilder<
                        BoundSourceTypeExpectation>(
                        bindings.Count + typeProviderBindings.Length);
                foreach (var binding in bindings)
                {
                    expectations.Add(new BoundSourceTypeExpectation(
                        provider, binding.ImportRow,
                        0x1300_0000u
                            | checked((uint)typeDefRow),
                        binding.ProducerIdentity, binding.ExportKey,
                        binding.TargetRequirement, binding.RuntimePolicy,
                        providerSnapshot,
                        0x1300_0000u
                            | checked((uint)binding.ProviderTypeDefRow)));
                }

                foreach (var binding in typeProviderBindings)
                {
                    if (binding.ConsumerArtifactIndex
                            != firstBinding.ProviderArtifactIndex
                        || binding.ConsumerModuleRow
                            != firstBinding.ProviderModuleRow)
                    {
                        continue;
                    }

                    expectations.Add(new BoundSourceTypeExpectation(
                        getSnapshot(binding.ConsumerArtifactIndex,
                            binding.ConsumerModuleRow),
                        binding.ImportRow,
                        0x1300_0000u
                            | checked((uint)binding.ExpectedTypeDefRow),
                        binding.ProducerIdentity, binding.ExportKey,
                        binding.TargetRequirement, binding.RuntimePolicy,
                        getSnapshot(binding.ProviderArtifactIndex,
                            binding.ProviderModuleRow),
                        0x1300_0000u
                            | checked((uint)binding.ProviderTypeDefRow)));
                }

                var exportKeys = bindings.Select(
                        static binding => binding.ExportKey)
                    .Distinct(StringComparer.Ordinal)
                    .ToImmutableArray();
                origin = new BoundStandardTypeOrigin(providerSnapshot,
                    0x1300_0000u
                        | checked((uint)firstBinding.ProviderTypeDefRow),
                    exportKeys, false, expectations.ToImmutable(),
                    ReferenceRoot: provider) {
                    SelectedExportKeys = [],
                };
            }
            else
            {
                var exportKeys = getExportKeys(0, moduleRow, typeDefRow);
                var selectedKeys = selectedByTypeDef.TryGetValue(typeDefRow,
                    out var keys) ? keys.ToImmutableArray()
                    : ImmutableArray<string>.Empty;
                origin = new BoundStandardTypeOrigin(provider, typeDefId,
                    exportKeys, selectedByTypeDef.ContainsKey(typeDefRow),
                    ReferenceRoot: provider) {
                    SelectedExportKeys = selectedKeys,
                };
            }
            structures.Add(new BoundStruct(
                namespaceName.Length == 0 ? typeName
                    : namespaceName + "." + typeName,
                fields.MoveToImmutable(), 1) {
                IsPrivate = (typeDefFlags & 3) == 0,
                IsInternal = (typeDefFlags & 3) == 1,
                StandardTypeOrigin = origin,
                StandardLayoutOnly = true,
            });
        }

        var states = new byte[structures.Count];
        var remaining = SemanticVerifier.MaximumAnalysisVisits;
        for (var structureIndex = 0;
            structureIndex < structures.Count; structureIndex++)
        {
            ComputeLayout(structureIndex, 0);
        }

        return [.. structures];

        void ComputeLayout(int structureIndex, int depth)
        {
            if (--remaining < 0)
            {
                throw new NotSupportedException(
                    "The EN1 struct layout closure exceeds the analysis limit.");
            }
            if (depth > SemanticVerifier.MaximumDepth)
            {
                throw new NotSupportedException(
                    "The EN1 struct layout closure exceeds the nesting limit.");
            }
            if (states[structureIndex] == 2)
            {
                return;
            }
            if (states[structureIndex] == 1)
            {
                throw Invalid(
                    "The EN1 source struct FieldDef graph is recursive.");
            }
            states[structureIndex] = 1;

            var structure = structures[structureIndex];
            var fields = ImmutableArray.CreateBuilder<BoundStructField>(
                structure.Fields.Length);
            ulong offset = 0;
            var alignment = 1;
            var requiredAlignment = 1;
            foreach (var field in structure.Fields)
            {
                if (--remaining < 0)
                {
                    throw new NotSupportedException(
                        "The EN1 struct layout closure exceeds the analysis limit.");
                }

                var nestedIndex = (int)field.Type - 100;
                if (!BoundEnumTypes.IsType(field.Type)
                    && nestedIndex >= 0
                    && nestedIndex < structures.Count)
                {
                    ComputeLayout(nestedIndex, depth + 1);
                }

                var width = ValueLayout.Size(field.Type, structures,
                    enumDefinitions: enumDefinitions);
                var fieldAlignment = ValueLayout.Alignment(field.Type,
                    structures, enumDefinitions: enumDefinitions);
                var fieldRequiredAlignment
                    = ValueLayout.RequiredAlignment(field.Type, structures,
                        enumDefinitions: enumDefinitions);
                if (width == 0 || fieldAlignment <= 0
                    || fieldRequiredAlignment <= 0)
                {
                    throw Invalid(
                        "An EN1 source struct field has no complete Auto Layout.");
                }

                var fieldOffset = Align(offset, fieldAlignment);
                if (fieldOffset > ulong.MaxValue - width)
                {
                    throw Invalid(
                        "An EN1 source struct layout exceeds its representable extent.");
                }

                fields.Add(field with {
                    Offset = fieldOffset,
                    Size = width,
                });
                offset = fieldOffset + width;
                alignment = Math.Max(alignment, fieldAlignment);
                requiredAlignment = Math.Max(requiredAlignment,
                    fieldRequiredAlignment);
            }

            if (offset == 0)
            {
                offset = 1;
            }
            var size = Align(offset, alignment);
            if (size > ValueLayout.MaximumStructureSize)
            {
                throw new NotSupportedException(
                    "An EN1 source struct exceeds the supported local layout size.");
            }

            var laidOut = structure with {
                Fields = fields.MoveToImmutable(),
                Size = size,
                Alignment = alignment,
                RequiredAlignment = requiredAlignment,
            };
            var (fieldStart, _) = tables.TypeDefs.GetFieldRange(
                typeDefRows[structureIndex]);
            var fieldsWithOrigins = laidOut.Fields.ToBuilder();
            for (var fieldOrdinal = 0;
                fieldOrdinal < fieldsWithOrigins.Count; fieldOrdinal++)
            {
                var fieldRow = fieldStart + fieldOrdinal;
                var field = fieldsWithOrigins[fieldOrdinal];
                if (fieldBindingsByExpectedField.TryGetValue(
                        (typeDefRows[structureIndex], fieldOrdinal),
                        out var fieldBindings))
                {
                    if (exportsByField.ContainsKey(fieldRow))
                    {
                        throw Invalid(
                            "An EN1 expected FieldDef cannot also be exported by its consumer Module.");
                    }

                    var firstBinding = fieldBindings[0];
                    if (fieldBindings.Any(binding =>
                            binding.ProviderArtifactIndex
                                != firstBinding.ProviderArtifactIndex
                            || binding.ProviderModuleRow
                                != firstBinding.ProviderModuleRow
                            || binding.ProviderTypeDefRow
                                != firstBinding.ProviderTypeDefRow
                            || binding.ProviderFieldDefRow
                                != firstBinding.ProviderFieldDefRow
                            || !StringComparer.Ordinal.Equals(
                                binding.ProducerIdentity,
                                firstBinding.ProducerIdentity)
                            || binding.TargetRequirement
                                != firstBinding.TargetRequirement
                            || binding.RuntimePolicy
                                != firstBinding.RuntimePolicy))
                    {
                        throw Invalid(
                            "One EN1 expected FieldDef has conflicting provider origins.");
                    }

                    var providerSnapshot = getSnapshot(
                        firstBinding.ProviderArtifactIndex,
                        firstBinding.ProviderModuleRow);
                    var typeOrigin = laidOut.StandardTypeOrigin
                        ?? throw Invalid(
                            "An EN1 imported FieldDef owner has no TypeDef origin.");
                    if (!SameA2SourceTypeProviderSnapshot(
                            providerSnapshot, typeOrigin.Provider)
                        || typeOrigin.DefinitionId
                            != (0x1300_0000u
                                | checked((uint)firstBinding.ProviderTypeDefRow))
                        || !StringComparer.Ordinal.Equals(
                            providerSnapshot.ModuleIdentity,
                            firstBinding.ProducerIdentity)
                        || firstBinding.TargetRequirement
                            != providerSnapshot.TargetRequirement
                        || firstBinding.RuntimePolicy
                            != providerSnapshot.RuntimePolicy)
                    {
                        throw Invalid(
                            "An EN1 FieldDef Import does not bind through its exact owner TypeDef provider.");
                    }

                    var exportKeys = fieldBindings.Select(
                            static binding => binding.ExportKey)
                        .Distinct(StringComparer.Ordinal)
                        .ToImmutableArray();
                    var expectedField = field with {
                        StandardFieldOrigin = null,
                    };
                    fieldsWithOrigins[fieldOrdinal] = field with {
                        StandardFieldOrigin = new BoundStandardFieldOrigin(
                            providerSnapshot,
                            0x1400_0000u
                                | checked((uint)
                                    firstBinding.ProviderFieldDefRow),
                            exportKeys, false, expectedField) {
                            SelectedExportKeys = [],
                        },
                    };
                    continue;
                }

                if (!exportsByField.TryGetValue(fieldRow,
                        out var exportKeys))
                {
                    continue;
                }

                var expectedField = field with {
                    StandardFieldOrigin = null,
                };
                fieldsWithOrigins[fieldOrdinal] = field with {
                    StandardFieldOrigin = new BoundStandardFieldOrigin(
                        provider, 0x1400_0000u
                            | checked((uint)fieldRow),
                        [.. exportKeys], false, expectedField),
                };
            }
            laidOut = laidOut with {
                Fields = fieldsWithOrigins.MoveToImmutable(),
            };
            var expected = laidOut with {
                Fields = [.. laidOut.Fields.Select(static field => field with {
                    StandardFieldOrigin = null,
                })],
                StandardTypeOrigin = null,
                StandardLayoutOnly = false,
            };
            var origin = laidOut.StandardTypeOrigin
                ?? throw Invalid(
                    "An EN1 source struct has no retained TypeDef origin.");
            structures[structureIndex] = laidOut with {
                StandardTypeOrigin = origin with {
                    ExpectedDeclaration = expected,
                },
            };
            states[structureIndex] = 2;
        }

        static ulong Align(ulong value, int alignment)
        {
            var alignmentValue = checked((ulong)alignment);
            var remainder = value % alignmentValue;
            var padding = remainder == 0 ? 0
                : alignmentValue - remainder;
            if (value > ulong.MaxValue - padding)
            {
                throw Invalid(
                    "An EN1 source struct alignment exceeds its representable extent.");
            }

            return value + padding;
        }
    }

    private static BoundStandardEnumView CreateStandardEnumView(
        TrRmStructuralMetadataValidator.ValidatedExecutionTables tables,
        BoundSourceTypeProviderSnapshot snapshot, int typeDefRow)
    {
        var definitionId = 0x1300_0000u
            | checked((uint)typeDefRow);
        var enumRow = tables.EnumDefs.GetRowForTypeDef(typeDefRow);
        var underlyingType
            = ResolveEnumUnderlyingType(tables, enumRow);
        var (memberFirst, memberCount)
            = tables.EnumDefs.GetMemberRange(enumRow);
        var members
            = ImmutableArray.CreateBuilder<BoundStandardEnumMemberView>(
                memberCount);
        for (var ordinal = 0; ordinal < memberCount; ordinal++)
        {
            var memberRow = memberFirst + ordinal;
            members.Add(new BoundStandardEnumMemberView(
                tables.EnumMembers.GetDeclarationKey(memberRow),
                tables.EnumMembers.GetName(memberRow),
                ReadGenericEnumMemberValue(tables, typeDefRow, memberRow,
                    underlyingType)));
        }

        return new BoundStandardEnumView(
            new BoundStandardEnumTypeSite(snapshot, definitionId),
            tables.TypeDefs.GetNamespace(typeDefRow),
            tables.TypeDefs.GetName(typeDefRow),
            tables.TypeDefs.GetFlags(typeDefRow), underlyingType,
            tables.EnumDefs.GetFlags(enumRow),
            CreateStandardEnumLayouts(tables, typeDefRow, definitionId),
            members.MoveToImmutable());
    }

    private static BoundEnumDefinition CreateStandardEnumDefinition(
        BoundStandardEnumView view,
        BoundStandardEnumOccurrence occurrence)
    {
        var identity
            = new BoundEnumDeclarationIdentity.Standard(occurrence);
        var isPublic = (view.TypeDefFlags & 3) == 2;
        return new BoundEnumDefinition(identity,
            view.Namespace.Length == 0
                ? view.Name : $"{view.Namespace}.{view.Name}",
            view.UnderlyingType,
            [.. view.Members.Select(member =>
                new BoundEnumMemberDefinition(
                    new BoundEnumMemberIdentity.OwnerKey(identity,
                        member.DeclarationKey),
                    identity, member.Name, member.Value, isPublic,
                    member.DeclarationKey))],
            isPublic, IsPrimary: true,
            IsFlags: view.EnumFlags == 1)
        {
            BlittableTargetRequirement = view.Layouts.Any(
                static layout => (layout.Flags & 1) != 0)
                ? FixedLayoutStorage.WindowsX64TargetRequirement : 0,
        };
    }

    private static ImmutableArray<BoundStandardEnumLayoutVariant>
        CreateStandardEnumLayouts(
            TrRmStructuralMetadataValidator.ValidatedExecutionTables tables,
            int typeDefRow, uint definitionId)
    {
        var (layoutStart, layoutCount)
            = tables.TypeDefs.GetLayoutRange(typeDefRow);
        if (layoutCount == 0)
        {
            throw Invalid(
                "An EN1 enum TypeDef has no layout variant.");
        }

        var layouts
            = ImmutableArray.CreateBuilder<
                BoundStandardEnumLayoutVariant>(layoutCount);
        for (var offset = 0; offset < layoutCount; offset++)
        {
            var layoutRow = layoutStart + offset;
            var targetProfileReference
                = tables.Layouts.GetTargetProfileRef(layoutRow);
            var profile = targetProfileReference == 0
                ? default
                : tables.TargetProfiles.GetProfile(
                    checked((int)targetProfileReference));
            var (_, layoutFieldCount)
                = tables.Layouts.GetLayoutFieldRange(layoutRow);
            if (layoutFieldCount != 0)
            {
                throw Invalid(
                    "An EN1 enum layout cannot contain fields.");
            }

            layouts.Add(new BoundStandardEnumLayoutVariant(
                checked((uint)layoutRow), definitionId,
                targetProfileReference, profile.ProfileId,
                profile.RuleRevision,
                tables.Layouts.GetLayoutKind(layoutRow),
                tables.Layouts.GetFlags(layoutRow),
                tables.Layouts.GetAlignment(layoutRow),
                tables.Layouts.GetPack(layoutRow), []));
        }

        return layouts.MoveToImmutable();
    }

    private static string[] CaptureLayoutQueryExportKeys(
        IReadOnlyList<string> exportKeys, string parameterName,
        bool requireOne)
    {
        ArgumentNullException.ThrowIfNull(exportKeys, parameterName);
        var snapshot = new string[exportKeys.Count];
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < snapshot.Length; index++)
        {
            var key = exportKeys[index];
            if (string.IsNullOrEmpty(key)
                || key.Contains('\0', StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "Selected ExportKeys must be nonempty NUL-free text.",
                    parameterName);
            }

            try
            {
                _ = s_sourceTypeExportKeyEncoding.GetByteCount(key);
            }
            catch (System.Text.EncoderFallbackException exception)
            {
                throw new ArgumentException(
                    "Selected ExportKeys must be well-formed scalar text.",
                    parameterName, exception);
            }

            if (!seen.Add(key))
            {
                throw new ArgumentException(
                    "Selected ExportKeys must be unique.",
                    parameterName);
            }

            snapshot[index] = key;
        }

        if (requireOne && snapshot.Length == 0)
        {
            throw new ArgumentException(
                "At least one TypeDef ExportKey must be selected.",
                parameterName);
        }

        return snapshot;
    }

    private static CompilationReference CreateLayoutQueriesSourceTypeReference(
        byte[][] artifacts, string[] schemaIdentities, string moduleIdentity,
        string[] typeExportKeys, string[] fieldExportKeys)
    {
        var program = LoadStandardProgram(artifacts, schemaIdentities,
            moduleIdentity, true, null, out _, out _,
            out _, out var moduleValidation, out _);
        var index = new TrRmArtifactIndex(artifacts[0],
            TrRmArtifactSchema.Standard);
        var tables = TrRmStructuralMetadataValidator
            .ValidateAndGetExecutionTables(index);
        var moduleRow = moduleValidation.RootModuleRow;
        var (targetRequirement, runtimePolicy)
            = tables.Modules.GetRequirements(moduleRow);
        var (typeDefStart, typeDefEnd)
            = tables.Modules.GetTypeDefRange(moduleRow);
        if (tables.Schema != TrRmArtifactSchema.Standard
            || targetRequirement
                != TrRmModuleTableReader.WindowsX64NativeTargetRequirement
            || runtimePolicy != 0
            || typeDefStart <= 0
            || typeDefEnd - typeDefStart != program.Structures.Length)
        {
            throw Unsupported(
                "The bounded LayoutQueries source projection requires a Blittable type Module under the selected Windows x64 target.");
        }

        var providerSnapshot = CreateA2SourceTypeProviderSnapshot(tables,
            artifacts[0], schemaIdentities[0], moduleRow);
        var requestedTypes = new Dictionary<int, List<string>>();
        foreach (var exportKey in typeExportKeys)
        {
            if (!tables.Exports.TryGetDefinitionForKey(moduleRow, exportKey,
                    out var definition)
                || definition >> 24 != 0x13)
            {
                throw new ArgumentException(
                    "A selected ExportKey does not identify a TypeDef in the selected Module.",
                    nameof(typeExportKeys));
            }

            var typeDefRow = checked((int)(definition & 0x00FF_FFFF));
            if (typeDefRow < typeDefStart || typeDefRow >= typeDefEnd
                || tables.TypeDefs.GetOwnerModule(typeDefRow) != moduleRow
                || tables.Imports.IsExpectedDefinition(definition)
                || tables.TypeDefs.GetKind(typeDefRow) != 1
                || (tables.TypeDefs.GetFlags(typeDefRow) & 3) != 2
                || tables.TypeDefs.GetGenericRange(typeDefRow).Count != 0)
            {
                throw Invalid(
                    "A selected LayoutQueries TypeDef Export does not identify a local public nongeneric struct.");
            }

            var structureIndex = typeDefRow - typeDefStart;
            var structure = program.Structures[structureIndex];
            if (structure.StandardTypeOrigin is not null
                || structure.IsPrivate || structure.IsInternal
                || structure.IsUnique || structure.CleanupMethod is not null
                || structure.SourceGenericTypeDefinitionIndex >= 0
                || structure.BlittableTargetRequirement
                    != FixedLayoutStorage.WindowsX64TargetRequirement
                || !tables.Layouts.HasBlittableLayout(typeDefRow,
                    TrRmTargetProfileTableReader.WindowsX64NativeProfileId,
                    TrRmTargetProfileTableReader.WindowsX64NativeProfileRuleRevision))
            {
                throw Unsupported(
                    "A selected LayoutQueries root requires a public nongeneric Copy struct with the supported Blittable layout.");
            }

            if (!requestedTypes.TryGetValue(typeDefRow,
                    out var selectedKeys))
            {
                selectedKeys = [];
                requestedTypes.Add(typeDefRow, selectedKeys);
            }
            selectedKeys.Add(exportKey);
        }

        var requestedFields = new Dictionary<(int TypeDefRow, int FieldOrdinal),
            List<(string Key, int FieldRow)>>();
        foreach (var exportKey in fieldExportKeys)
        {
            if (!tables.Exports.TryGetDefinitionForKey(moduleRow, exportKey,
                    out var definition)
                || definition >> 24 != 0x14)
            {
                throw new ArgumentException(
                    "A selected ExportKey does not identify a FieldDef in the selected Module.",
                    nameof(fieldExportKeys));
            }

            var fieldRow = checked((int)(definition & 0x00FF_FFFF));
            var ownerTypeDefRow = tables.Fields.GetOwnerTypeDef(fieldRow);
            if (!requestedTypes.ContainsKey(ownerTypeDefRow)
                || tables.TypeDefs.GetOwnerModule(ownerTypeDefRow) != moduleRow
                || tables.Imports.IsExpectedDefinition(
                    0x1300_0000u | checked((uint)ownerTypeDefRow))
                || (tables.Fields.GetFlags(fieldRow) & 3) != 2)
            {
                throw new ArgumentException(
                    "A selected FieldDef ExportKey must identify a public field owned by a selected TypeDef root.",
                    nameof(fieldExportKeys));
            }

            var (fieldStart, fieldEnd)
                = tables.TypeDefs.GetFieldRange(ownerTypeDefRow);
            if (fieldRow < fieldStart || fieldRow >= fieldEnd)
            {
                throw Invalid(
                    "A selected FieldDef is outside its owning TypeDef range.");
            }

            var fieldOrdinal = fieldRow - fieldStart;
            var selector = (ownerTypeDefRow, fieldOrdinal);
            if (!requestedFields.TryGetValue(selector, out var selectedKeys))
            {
                selectedKeys = [];
                requestedFields.Add(selector, selectedKeys);
            }
            selectedKeys.Add((exportKey, fieldRow));
        }

        var selectedStructures = new HashSet<int>();
        var pendingStructures = new Stack<int>();
        foreach (var typeDefRow in requestedTypes.Keys)
        {
            var structureIndex = typeDefRow - typeDefStart;
            _ = selectedStructures.Add(structureIndex);
            pendingStructures.Push(structureIndex);
        }

        while (pendingStructures.TryPop(out var structureIndex))
        {
            var structure = program.Structures[structureIndex];
            if (structure.SourceGenericTypeDefinitionIndex >= 0)
            {
                throw Unsupported(
                    "The LayoutQueries source projection does not admit generic nominal closure.");
            }

            foreach (var field in structure.Fields)
            {
                if (BoundEnumTypes.IsType(field.Type)
                    || NativeFixedArrayTypes.IsType(field.Type)
                    || NativePointerTypes.IsType(field.Type)
                    || NativeFunctionPointerTypes.IsType(field.Type)
                    || ManagedClassTypes.IsClass(field.Type))
                {
                    throw Unsupported(
                        "The bounded LayoutQueries source projection requires scalar or inline-struct field types.");
                }

                var childIndex = (int)field.Type - 100;
                if (childIndex < 0
                    || childIndex >= program.Structures.Length
                    || !selectedStructures.Add(childIndex))
                {
                    continue;
                }

                pendingStructures.Push(childIndex);
            }
        }

        var allTypeExports = new Dictionary<int, List<string>>();
        foreach (var (definition, exportKey) in tables.Exports
            .GetExportsForModule(moduleRow))
        {
            if (definition >> 24 != 0x13)
            {
                continue;
            }

            var typeDefRow = checked((int)(definition & 0x00FF_FFFF));
            if (tables.Imports.IsExpectedDefinition(definition))
            {
                throw Invalid(
                    "A local LayoutQueries TypeDef Export cannot identify an Import expectation.");
            }

            if (!allTypeExports.TryGetValue(typeDefRow, out var keys))
            {
                keys = [];
                allTypeExports.Add(typeDefRow, keys);
            }
            keys.Add(exportKey);
        }

        foreach (var structureIndex in selectedStructures)
        {
            var typeDefRow = typeDefStart + structureIndex;
            if (requestedTypes.ContainsKey(typeDefRow))
            {
                continue;
            }

            var structure = program.Structures[structureIndex];
            if (structure.StandardTypeOrigin is not null)
            {
                if (structure.StandardTypeOrigin.ExportKeys.IsDefaultOrEmpty
                    || structure.StandardTypeOrigin.Provider.SchemaIdentity
                        != StandardLayoutQueriesSchemaIdentity)
                {
                    throw Unsupported(
                        "A nested LayoutQueries TypeDef Import has no retained provider ExportKey origin.");
                }

                continue;
            }

            if (structure.IsPrivate || structure.IsInternal
                || !allTypeExports.TryGetValue(typeDefRow, out var keys)
                || keys.Count != 1)
            {
                throw Unsupported(
                    "A nominal LayoutQueries closure child needs one public TypeDef ExportKey.");
            }
        }

        var structureOrder = selectedStructures.ToArray();
        Array.Sort(structureOrder);
        var structureIndexMap = new Dictionary<int, int>(
            structureOrder.Length);
        for (var indexInProjection = 0;
            indexInProjection < structureOrder.Length; indexInProjection++)
        {
            structureIndexMap.Add(structureOrder[indexInProjection],
                indexInProjection);
        }

        PrimitiveType RemapType(PrimitiveType type)
        {
            var structureIndex = (int)type - 100;
            if (structureIndex < 0
                || structureIndex >= program.Structures.Length)
            {
                return type;
            }

            return structureIndexMap.TryGetValue(structureIndex,
                    out var remappedIndex)
                ? (PrimitiveType)(100 + remappedIndex)
                : throw Invalid(
                    "A selected LayoutQueries source type is missing its inline closure mapping.");
        }

        var projectedStructures = ImmutableArray.CreateBuilder<BoundStruct>(
            structureOrder.Length);
        foreach (var structureIndex in structureOrder)
        {
            var typeDefRow = typeDefStart + structureIndex;
            var original = program.Structures[structureIndex];
            var remappedOriginal = BoundTreeTraversal.RewriteTypes(original,
                RemapType);
            var fields = remappedOriginal.Fields.ToBuilder();
            for (var fieldOrdinal = 0;
                fieldOrdinal < remappedOriginal.Fields.Length; fieldOrdinal++)
            {
                var remappedField = fields[fieldOrdinal];
                var (fieldStart, _) = tables.TypeDefs.GetFieldRange(
                    typeDefRow);
                if (requestedFields.TryGetValue(
                        (typeDefRow, fieldOrdinal), out var requestedFieldKeys))
                {
                    var (_, fieldRow) = requestedFieldKeys[0];
                    var fieldOrigin = new BoundStandardFieldOrigin(
                        providerSnapshot,
                        0x1400_0000u | checked((uint)fieldRow),
                        [.. requestedFieldKeys.Select(static item => item.Key)],
                        true, remappedField with {
                            StandardFieldOrigin = null,
                        }) {
                        SelectedExportKeys = [
                            .. requestedFieldKeys.Select(
                                static item => item.Key),
                        ],
                    };
                    remappedField = remappedField with {
                        StandardFieldOrigin = fieldOrigin,
                    };
                }
                else if (fieldStart != 0
                    && requestedFields.Keys.Any(key =>
                        key.TypeDefRow == typeDefRow
                        && key.FieldOrdinal == fieldOrdinal))
                {
                    throw Invalid(
                        "A selected FieldDef export could not be retained on its source field.");
                }

                fields[fieldOrdinal] = remappedField;
            }

            var projectedFields = fields.ToImmutable();
            var expectedDeclaration = remappedOriginal with {
                Fields = [.. projectedFields.Select(static field => field with {
                    StandardFieldOrigin = null,
                })],
                StandardTypeOrigin = null,
                StandardLayoutOnly = false,
            };
            var existingOrigin = remappedOriginal.StandardTypeOrigin;
            BoundStandardTypeOrigin origin;
            if (existingOrigin is not null)
            {
                if (requestedTypes.ContainsKey(typeDefRow))
                {
                    throw Invalid(
                        "A selected LayoutQueries TypeDef Export identifies an imported expectation.");
                }

                origin = existingOrigin with {
                    IsLookupRoot = false,
                    SelectedExportKeys = [],
                    ExpectedDeclaration = expectedDeclaration,
                };
            }
            else
            {
                ImmutableArray<string> selectedTypeKeys
                    = requestedTypes.TryGetValue(typeDefRow,
                        out var requestedKeys)
                    ? [.. requestedKeys]
                    : [allTypeExports[typeDefRow][0]];
                origin = new BoundStandardTypeOrigin(providerSnapshot,
                    0x1300_0000u | checked((uint)typeDefRow),
                    selectedTypeKeys,
                    requestedTypes.ContainsKey(typeDefRow),
                    ReferenceRoot: providerSnapshot,
                    ExpectedDeclaration: expectedDeclaration) {
                    SelectedExportKeys = requestedTypes.ContainsKey(
                        typeDefRow) ? selectedTypeKeys : [],
                };
            }

            projectedStructures.Add(remappedOriginal with {
                Fields = projectedFields,
                StandardTypeOrigin = origin,
                StandardLayoutOnly = true,
            });
        }

        var structures = projectedStructures.MoveToImmutable();
        var projectedProgram = new BoundProgram([], -1) {
            RuntimePolicy = (BoundRuntimePolicy)runtimePolicy,
            Structures = structures,
        };
        SemanticVerifier.VerifyLibraryReferences(projectedProgram);
        var standardFieldProviderClosure
            = program.StandardFieldProviderClosure
                ?? throw Unsupported(
                    "A LayoutQueries source reference has no retained provider closure.");
        var standardQuerySourceAssociation
            = new BoundStandardQuerySourceAssociation([
                standardFieldProviderClosure,
            ]);
        return new CompilationReference(structures, [],
            (BoundRuntimePolicy)runtimePolicy,
            standardQuerySourceAssociation:
                standardQuerySourceAssociation,
            coreEvaluationFormIdentitySelections:
                program.CoreEvaluationFormIdentitySelections,
            coreEvaluationFormSchemaProviders:
                program.CoreEvaluationFormSchemaProviders);
    }

    private static CompilationReference CreateA2SourceTypeReference(
        byte[][] artifacts, string[] schemaIdentities, string moduleIdentity,
        string[] typeExportKeys, string[] fieldExportKeys,
        BoundProgram program,
        StandardModuleValidation moduleValidation,
        ImmutableArray<TrRmStandardTypeProviderBinding> typeProviderBindings)
    {
        var artifact = artifacts[0];
        var schemaIdentity = schemaIdentities[0];
        var initializationReferenceLocalFieldLayout
            = StringComparer.Ordinal.Equals(schemaIdentity,
                StandardInitReferenceBodiesLocalFieldLayoutSchemaIdentity)
                || StringComparer.Ordinal.Equals(schemaIdentity,
                    StandardInitReferenceBodiesNestedFieldDestinationsSchemaIdentity);
        var tables = ValidateForeignInvocationArtifact(
            CreateForeignInvocationIndex(artifact, schemaIdentity),
            schemaIdentity, null,
            createA3ResourceLedger: false);
        var moduleRow = moduleValidation.RootModuleRow;
        if (StringComparer.Ordinal.Equals(schemaIdentity,
                StandardInitReferenceBodiesNestedFieldDestinationsSchemaIdentity)
            && program.StandardFieldProviderClosure is null)
        {
            program = RetainStandardFieldProviderClosure(program,
                new TrRmStandardFieldProviderValidation([], []),
                artifacts, schemaIdentities, moduleValidation.Modules,
                moduleRow, moduleIdentity);
        }

        var (typeDefStart, typeDefEnd)
            = tables.Modules.GetTypeDefRange(moduleRow);
        var (methodStart, methodEnd)
            = tables.Modules.GetMethodDefRange(moduleRow);
        if (tables.Schema is not (
                TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyDraft
                or TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyEmptyInitializationDraft
                or TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyEmptyUnitInitializationDraft
                or TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyEmptyUnitScalarFrozenValuesDraft
                or TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyReturnCoverageDraft
                or TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyReturnCoverageRepeatInitializationDraft
                or TrRmArtifactSchema.LocalAggregateLayoutDraft
                or TrRmArtifactSchema.InitializationReferenceLocalFieldLayoutDraft
                or TrRmArtifactSchema.InitializationReferenceLocalFieldLayoutNestedFieldDestinationsDraft)
            || moduleRow <= 0 || moduleRow > tables.Modules.Count
            || typeDefStart <= 0 || typeDefEnd - typeDefStart
                != program.Structures.Length
            || (methodStart != methodEnd
                && !(initializationReferenceLocalFieldLayout))
            || tables.Modules.GetEntryMethod(moduleRow) != 0)
        {
            throw Unsupported(
                "The A2 type-reference projection requires one type-only root Module.");
        }

        var (targetRequirement, runtimePolicy)
            = tables.Modules.GetRequirements(moduleRow);
        var keysByTypeDefRow = new Dictionary<int, List<string>>();
        var rootTypeDefRows = new HashSet<int>();
        foreach (var exportKey in typeExportKeys)
        {
            if (!tables.Exports.TryGetDefinitionForKey(moduleRow,
                    exportKey, out var definition))
            {
                throw new ArgumentException(
                    "A selected TypeDef ExportKey is not published by the selected Module.",
                    nameof(typeExportKeys));
            }
            if (definition >> 24 != 0x13)
            {
                throw new ArgumentException(
                    "A selected ExportKey does not identify a TypeDef.",
                    nameof(typeExportKeys));
            }

            var typeDefRow = checked((int)(definition & 0x00FF_FFFF));
            if (typeDefRow < typeDefStart || typeDefRow >= typeDefEnd
                || tables.TypeDefs.GetOwnerModule(typeDefRow) != moduleRow
                || tables.TypeDefs.GetKind(typeDefRow) != 1
                || !tables.TypeDefs.HasDeclaredCopy(typeDefRow)
                || (tables.TypeDefs.GetFlags(typeDefRow) & 3) != 2)
            {
                throw Invalid(
                    "A selected A2 TypeDef Export does not identify a local public Copy struct.");
            }

            var structureIndex = typeDefRow - typeDefStart;
            var structure = program.Structures[structureIndex];
            if (initializationReferenceLocalFieldLayout
                && !structure.IsSequential && !structure.IsTransparent)
            {
                throw Unsupported(
                    "The initialization-reference local-field layout profile requires Sequential or Transparent Copy types.");
            }

            if (!keysByTypeDefRow.TryGetValue(typeDefRow,
                    out var selectedKeys))
            {
                selectedKeys = [];
                keysByTypeDefRow.Add(typeDefRow, selectedKeys);
                _ = rootTypeDefRows.Add(typeDefRow);
            }
            selectedKeys.Add(exportKey);
        }

        var providerSnapshot = CreateA2SourceTypeProviderSnapshot(
            tables, artifact, schemaIdentity, moduleRow);
        var providerSnapshots = new Dictionary<(int ArtifactIndex, int ModuleRow),
            BoundSourceTypeProviderSnapshot>
        {
            [(0, moduleRow)] = providerSnapshot,
        };

        BoundSourceTypeProviderSnapshot GetProviderSnapshot(
            int artifactIndex, int providerModuleRow)
        {
            var key = (artifactIndex, providerModuleRow);
            if (providerSnapshots.TryGetValue(key, out var snapshot))
            {
                return snapshot;
            }

            var providerArtifact = artifacts[artifactIndex];
            var providerSchema = schemaIdentities[artifactIndex];
            var providerTables = ValidateForeignInvocationArtifact(
                CreateForeignInvocationIndex(providerArtifact, providerSchema),
                providerSchema, null, createA3ResourceLedger: false);
            snapshot = CreateA2SourceTypeProviderSnapshot(providerTables,
                providerArtifact, providerSchema, providerModuleRow);
            providerSnapshots.Add(key, snapshot);
            return snapshot;
        }

        var requestedFields = new Dictionary<
            (int TypeDefRow, int FieldOrdinal),
            List<(string Key, uint DefinitionId,
                BoundSourceTypeProviderSnapshot Provider,
                bool IsLookupRoot)>>();
        void AddRequestedField(int typeDefRow, int fieldOrdinal,
            string exportKey, uint definitionId,
            BoundSourceTypeProviderSnapshot provider, bool isLookupRoot)
        {
            var key = (typeDefRow, fieldOrdinal);
            if (!requestedFields.TryGetValue(key, out var selections))
            {
                selections = [];
                requestedFields.Add(key, selections);
            }

            if (selections.Any(selection =>
                    !SameA2SourceTypeProviderSnapshot(
                        selection.Provider, provider)
                    || selection.DefinitionId != definitionId))
            {
                throw Invalid(
                    "One selected A2 FieldDef has conflicting provider origins.");
            }

            var existingIndex = selections.FindIndex(selection =>
                StringComparer.Ordinal.Equals(selection.Key, exportKey));
            if (existingIndex < 0)
            {
                selections.Add((exportKey, definitionId, provider,
                    isLookupRoot));
            }
            else if (isLookupRoot
                && !selections[existingIndex].IsLookupRoot)
            {
                var existing = selections[existingIndex];
                selections[existingIndex] = (existing.Key,
                    existing.DefinitionId, existing.Provider, true);
            }
        }

        var bindingsByExpectedTypeDef = new Dictionary<int,
            List<TrRmStandardTypeProviderBinding>>();
        var originsByTypeDefRow = new Dictionary<int,
            BoundStandardTypeOrigin>();
        foreach (var binding in typeProviderBindings)
        {
            if (binding.ConsumerArtifactIndex != 0
                || binding.ConsumerModuleRow != moduleRow)
            {
                continue;
            }

            if (!bindingsByExpectedTypeDef.TryGetValue(
                    binding.ExpectedTypeDefRow, out var bindings))
            {
                bindings = [];
                bindingsByExpectedTypeDef.Add(
                    binding.ExpectedTypeDefRow, bindings);
            }
            bindings.Add(binding);
        }
        if (program.StandardFieldProviderClosure
            is BoundStandardFieldProviderClosure fieldProviderClosure)
        {
            if (!StringComparer.Ordinal.Equals(
                    fieldProviderClosure.SchemaIdentity,
                    StandardInitReferenceBodiesNestedFieldDestinationsSchemaIdentity)
                || !StringComparer.Ordinal.Equals(
                    fieldProviderClosure.RootModuleIdentity,
                    moduleIdentity)
                || fieldProviderClosure.RootModuleRow != moduleRow
                || (program with {
                    StandardFieldProviderClosure = null,
                }) != fieldProviderClosure.OriginalProgram)
            {
                throw Invalid(
                    "The A2 FieldDef provider closure has a different root or schema selection.");
            }

            foreach (var edge in fieldProviderClosure.FieldEdges)
            {
                if (edge.ConsumerArtifactIndex != 0
                    || edge.ConsumerModuleRow != moduleRow)
                {
                    continue;
                }

                if ((edge.ExpectedDefinitionId >> 24) != 0x14
                    || (edge.ActualDefinitionId >> 24) != 0x14
                    || edge.ExpectedOwnerTypeDefRow < typeDefStart
                    || edge.ExpectedOwnerTypeDefRow >= typeDefEnd)
                {
                    throw Invalid(
                        "A retained A2 FieldDef provider edge has an invalid local owner or definition.");
                }

                var (fieldStart, fieldEnd) = tables.TypeDefs.GetFieldRange(
                    edge.ExpectedOwnerTypeDefRow);
                var expectedFieldRow = checked((int)(
                    edge.ExpectedDefinitionId & 0x00FF_FFFF));
                if (expectedFieldRow < fieldStart
                    || expectedFieldRow >= fieldEnd)
                {
                    throw Invalid(
                        "A retained A2 FieldDef provider edge is outside its expected owner.");
                }

                var provider = GetProviderSnapshot(
                    edge.ProviderArtifactIndex, edge.ProviderModuleRow);
                AddRequestedField(edge.ExpectedOwnerTypeDefRow,
                    expectedFieldRow - fieldStart, edge.ExportKey,
                    edge.ActualDefinitionId, provider,
                    isLookupRoot: false);
            }
        }

        var selectedStructures = new HashSet<int>();
        var selectedArrayTypes = new HashSet<int>();
        var selectedPointerTypes = new HashSet<int>();
        var selectedCheckedReferenceTypes = new HashSet<int>();
        var visitedStructures = new HashSet<int>();
        var pendingTypes = new Stack<PrimitiveType>();
        var methodRoots = new List<(BoundFunction Function, string ExportKey)>();
        if (initializationReferenceLocalFieldLayout)
        {
            var functionByName = program.Functions.ToDictionary(
                static function => function.Name, StringComparer.Ordinal);
            for (var methodRow = methodStart; methodRow < methodEnd;
                methodRow++)
            {
                if (tables.NativeFunctionPointerAnchors
                    .IsAnchorMethodDef(methodRow)
                    || !tables.Exports.TryGetExportKeyForDefinition(
                        moduleRow, 0x1600_0000u
                            | checked((uint)methodRow),
                        out var exportKey))
                {
                    continue;
                }

                var name = tables.Methods.GetName(methodRow);
                if (!functionByName.TryGetValue(name, out var function)
                    || function.IsExternal
                    || function.Variables.Length < function.ParameterCount)
                {
                    throw Invalid(
                        "A selected initialization-reference MethodDef has no matching local source signature.");
                }

                methodRoots.Add((function, exportKey));
            }
        }

        foreach (var typeDefRow in rootTypeDefRows)
        {
            var structureIndex = typeDefRow - typeDefStart;
            _ = selectedStructures.Add(structureIndex);
            pendingTypes.Push((PrimitiveType)(100 + structureIndex));
        }
        foreach (var (function, _) in methodRoots)
        {
            pendingTypes.Push(function.ReturnType);
            for (var ordinal = 0; ordinal < function.ParameterCount;
                ordinal++)
            {
                pendingTypes.Push(function.Variables[ordinal].Type);
            }
        }

        var remaining = SemanticVerifier.MaximumAnalysisVisits;
        while (pendingTypes.TryPop(out var type))
        {
            if (--remaining < 0)
            {
                throw new NotSupportedException(
                    "The A2 type-reference closure exceeds the analysis limit.");
            }

            if (CheckedReferenceTypes.IsType(type))
            {
                var referenceIndex = CheckedReferenceTypes.Index(type);
                if ((uint)referenceIndex
                    >= (uint)program.CheckedReferenceTypes.Length)
                {
                    throw Invalid(
                        "A selected method refers to an unknown checked-reference type.");
                }

                if (selectedCheckedReferenceTypes.Add(referenceIndex))
                {
                    pendingTypes.Push(
                        program.CheckedReferenceTypes[referenceIndex]
                            .ReferentType);
                }
                continue;
            }

            if (NativeFixedArrayTypes.IsType(type))
            {
                if (initializationReferenceLocalFieldLayout)
                {
                    throw Unsupported(
                        "The initialization-reference local-field layout profile does not select fixed arrays.");
                }

                var arrayIndex = NativeFixedArrayTypes.Index(type);
                if ((uint)arrayIndex
                    >= (uint)program.NativeFixedArrayTypes.Length)
                {
                    throw Invalid(
                        "A selected A2 TypeDef references an unknown fixed array.");
                }
                if (selectedArrayTypes.Add(arrayIndex))
                {
                    pendingTypes.Push(
                        program.NativeFixedArrayTypes[arrayIndex].ElementType);
                }
                continue;
            }

            if (NativePointerTypes.IsType(type))
            {
                var pointerIndex = NativePointerTypes.Index(type);
                if ((uint)pointerIndex
                    >= (uint)program.NativePointerTypes.Length)
                {
                    throw Invalid(
                        "A selected A2 TypeDef references an unknown native pointer type.");
                }
                if (selectedPointerTypes.Add(pointerIndex))
                {
                    pendingTypes.Push(
                        program.NativePointerTypes[pointerIndex].PointeeType);
                }
                continue;
            }

            if (NativeFunctionPointerTypes.IsType(type)
                || BoundEnumTypes.IsType(type)
                || ManagedClassTypes.IsClass(type))
            {
                throw Unsupported(
                    "The A2 type-reference closure contains an unsupported type family.");
            }

            var structureIndex = (int)type - 100;
            if (structureIndex >= 0
                && structureIndex < program.Structures.Length)
            {
                var typeDefRow = typeDefStart + structureIndex;
                if (!keysByTypeDefRow.ContainsKey(typeDefRow))
                {
                    if (bindingsByExpectedTypeDef.TryGetValue(
                            typeDefRow, out var importBindings))
                    {
                        var firstBinding = importBindings[0];
                        if (importBindings.Any(binding =>
                                binding.ProviderArtifactIndex
                                    != firstBinding.ProviderArtifactIndex
                                || binding.ProviderModuleRow
                                    != firstBinding.ProviderModuleRow
                                || binding.ProviderTypeDefRow
                                    != firstBinding.ProviderTypeDefRow
                                || !StringComparer.Ordinal.Equals(
                                    binding.ProducerIdentity,
                                    firstBinding.ProducerIdentity)
                                || binding.TargetRequirement
                                    != firstBinding.TargetRequirement
                                || binding.RuntimePolicy
                                    != firstBinding.RuntimePolicy))
                        {
                            throw Invalid(
                                "One imported A2 TypeDef has conflicting provider origins.");
                        }

                        var importedProviderSnapshot = GetProviderSnapshot(
                            firstBinding.ProviderArtifactIndex,
                            firstBinding.ProviderModuleRow);
                        var importExpectations
                            = ImmutableArray.CreateBuilder<
                                BoundSourceTypeExpectation>(
                                importBindings.Count
                                    + typeProviderBindings.Length);
                        foreach (var binding in importBindings)
                        {
                            importExpectations.Add(
                                new BoundSourceTypeExpectation(providerSnapshot,
                                    binding.ImportRow,
                                    0x1300_0000u
                                        | checked((uint)typeDefRow),
                                    binding.ProducerIdentity,
                                    binding.ExportKey,
                                    binding.TargetRequirement,
                                    binding.RuntimePolicy,
                                    importedProviderSnapshot,
                                    0x1300_0000u
                                        | checked((uint)binding.ProviderTypeDefRow)));
                        }
                        foreach (var binding in typeProviderBindings)
                        {
                            if (binding.ConsumerArtifactIndex
                                    != firstBinding.ProviderArtifactIndex
                                || binding.ConsumerModuleRow
                                    != firstBinding.ProviderModuleRow)
                            {
                                continue;
                            }

                            importExpectations.Add(
                                new BoundSourceTypeExpectation(
                                    GetProviderSnapshot(
                                        binding.ConsumerArtifactIndex,
                                        binding.ConsumerModuleRow),
                                    binding.ImportRow,
                                    0x1300_0000u
                                        | checked((uint)binding.ExpectedTypeDefRow),
                                    binding.ProducerIdentity,
                                    binding.ExportKey,
                                    binding.TargetRequirement,
                                    binding.RuntimePolicy,
                                    GetProviderSnapshot(
                                        binding.ProviderArtifactIndex,
                                        binding.ProviderModuleRow),
                                    0x1300_0000u
                                        | checked((uint)binding.ProviderTypeDefRow)));
                        }
                        var importedKeys = importBindings.Select(
                                static binding => binding.ExportKey)
                            .Distinct(StringComparer.Ordinal).ToList();
                        keysByTypeDefRow.Add(typeDefRow, importedKeys);
                        originsByTypeDefRow.Add(typeDefRow,
                            new BoundStandardTypeOrigin(
                                importedProviderSnapshot,
                                0x1300_0000u
                                    | checked((uint)firstBinding.ProviderTypeDefRow),
                                [.. importedKeys], false,
                                importExpectations.ToImmutable(),
                                providerSnapshot));
                    }
                    else if (tables.Exports.TryGetExportKeyForDefinition(
                                 moduleRow,
                                 0x1300_0000u
                                     | checked((uint)typeDefRow),
                                 out var childExportKey))
                    {
                        keysByTypeDefRow.Add(typeDefRow, [childExportKey]);
                    }
                    else
                    {
                        throw Unsupported(
                            "A nominal child has no one unambiguous public TypeDef ExportKey.");
                    }
                }

                if (!tables.TypeDefs.HasDeclaredCopy(typeDefRow)
                    || (tables.TypeDefs.GetFlags(typeDefRow) & 3) != 2)
                {
                    throw Invalid(
                        "An A2 nominal type-reference child lacks public Copy evidence.");
                }
                _ = selectedStructures.Add(structureIndex);
                if (!visitedStructures.Add(structureIndex))
                {
                    continue;
                }

                foreach (var field in program.Structures[structureIndex].Fields)
                {
                    if (--remaining < 0)
                    {
                        throw new NotSupportedException(
                            "The A2 type-reference field closure exceeds the analysis limit.");
                    }
                    pendingTypes.Push(field.Type);
                }
                continue;
            }

            if (type == PrimitiveType.Void
                || PrimitiveTypeFacts.ScalarWidth(type) > 0
                || type is PrimitiveType.NativeRawPointer
                    or PrimitiveType.NativeUtf8ZPointer
                    or PrimitiveType.NativeUtf16ZPointer)
            {
                continue;
            }

            throw Unsupported(
                "The A2 type-reference closure contains an unsupported value type.");
        }

        foreach (var exportKey in fieldExportKeys)
        {
            var matches = new List<(int TypeDefRow, int FieldOrdinal,
                uint DefinitionId,
                BoundSourceTypeProviderSnapshot Provider)>();
            foreach (var structureIndex in selectedStructures)
            {
                if (--remaining < 0)
                {
                    throw new NotSupportedException(
                        "The A2 FieldDef source projection exceeds the analysis limit.");
                }

                var expectedTypeDefRow = typeDefStart + structureIndex;
                var providerArtifactIndex = 0;
                var providerModuleRow = moduleRow;
                var providerTypeDefRow = expectedTypeDefRow;
                if (bindingsByExpectedTypeDef.TryGetValue(
                        expectedTypeDefRow, out var typeBindings))
                {
                    var firstBinding = typeBindings[0];
                    if (typeBindings.Any(binding =>
                            binding.ProviderArtifactIndex
                                != firstBinding.ProviderArtifactIndex
                            || binding.ProviderModuleRow
                                != firstBinding.ProviderModuleRow
                            || binding.ProviderTypeDefRow
                                != firstBinding.ProviderTypeDefRow
                            || !StringComparer.Ordinal.Equals(
                                binding.ProducerIdentity,
                                firstBinding.ProducerIdentity)))
                    {
                        throw Invalid(
                            "One A2 TypeDef has conflicting FieldDef owner providers.");
                    }

                    providerArtifactIndex
                        = firstBinding.ProviderArtifactIndex;
                    providerModuleRow = firstBinding.ProviderModuleRow;
                    providerTypeDefRow = firstBinding.ProviderTypeDefRow;
                }

                var providerTables = providerArtifactIndex == 0
                    ? tables
                    : ValidateForeignInvocationArtifact(
                        CreateForeignInvocationIndex(
                            artifacts[providerArtifactIndex],
                            schemaIdentities[providerArtifactIndex]),
                        schemaIdentities[providerArtifactIndex], null,
                        createA3ResourceLedger: false);
                if (!providerTables.Exports.TryGetDefinitionForKey(
                        providerModuleRow, exportKey,
                        out var definition)
                    || (definition >> 24) != 0x14)
                {
                    continue;
                }

                var providerFieldRow = checked((int)(
                    definition & 0x00FF_FFFF));
                var actualOwnerTypeDefRow = providerTables.Fields
                    .GetOwnerTypeDef(providerFieldRow);
                if (actualOwnerTypeDefRow != providerTypeDefRow)
                {
                    continue;
                }

                var (providerFieldStart, providerFieldEnd)
                    = providerTables.TypeDefs.GetFieldRange(
                        providerTypeDefRow);
                if (providerFieldRow < providerFieldStart
                    || providerFieldRow >= providerFieldEnd
                    || (providerTables.Fields.GetFlags(providerFieldRow) & 3)
                        != 2
                    || (providerTables.Fields.GetFlags(providerFieldRow) & 8)
                        != 0)
                {
                    throw Invalid(
                        "A selected A2 FieldDef Export is not a public instance field of its provider TypeDef.");
                }

                var fieldOrdinal = providerFieldRow - providerFieldStart;
                var (expectedFieldStart, expectedFieldEnd)
                    = tables.TypeDefs.GetFieldRange(expectedTypeDefRow);
                if (expectedFieldStart == 0
                    || fieldOrdinal >= expectedFieldEnd
                        - expectedFieldStart
                    || (uint)fieldOrdinal
                        >= (uint)program.Structures[structureIndex]
                            .Fields.Length)
                {
                    throw Invalid(
                        "A selected A2 FieldDef Export is outside its expected TypeDef view.");
                }

                matches.Add((expectedTypeDefRow, fieldOrdinal, definition,
                    GetProviderSnapshot(providerArtifactIndex,
                        providerModuleRow)));
            }

            if (matches.Count == 0)
            {
                throw new ArgumentException(
                    "A selected FieldDef ExportKey does not identify a field in the selected TypeDef closure.",
                    nameof(fieldExportKeys));
            }

            var selectedProvider = matches[0].Provider;
            var selectedDefinition = matches[0].DefinitionId;
            if (matches.Any(match =>
                    !SameA2SourceTypeProviderSnapshot(
                        selectedProvider, match.Provider)
                    || match.DefinitionId != selectedDefinition))
            {
                throw new ArgumentException(
                    "A selected FieldDef ExportKey is ambiguous across the selected provider closure.",
                    nameof(fieldExportKeys));
            }

            foreach (var match in matches)
            {
                AddRequestedField(match.TypeDefRow, match.FieldOrdinal,
                    exportKey, match.DefinitionId, match.Provider,
                    isLookupRoot: true);
            }
        }

        var structureOrder = selectedStructures.ToArray();
        Array.Sort(structureOrder);
        var structureIndexMap = new Dictionary<int, int>(
            structureOrder.Length);
        for (var index = 0; index < structureOrder.Length; index++)
        {
            structureIndexMap.Add(structureOrder[index], index);
        }

        var arrayOrder = selectedArrayTypes.ToArray();
        Array.Sort(arrayOrder);
        var arrayIndexMap = new Dictionary<int, int>(arrayOrder.Length);
        for (var index = 0; index < arrayOrder.Length; index++)
        {
            arrayIndexMap.Add(arrayOrder[index], index);
        }

        var pointerOrder = selectedPointerTypes.ToArray();
        Array.Sort(pointerOrder);
        var pointerIndexMap = new Dictionary<int, int>(pointerOrder.Length);
        for (var index = 0; index < pointerOrder.Length; index++)
        {
            pointerIndexMap.Add(pointerOrder[index], index);
        }

        var checkedReferenceOrder = selectedCheckedReferenceTypes.ToArray();
        Array.Sort(checkedReferenceOrder);
        var checkedReferenceIndexMap = new Dictionary<int, int>(
            checkedReferenceOrder.Length);
        for (var index = 0; index < checkedReferenceOrder.Length; index++)
        {
            checkedReferenceIndexMap.Add(checkedReferenceOrder[index], index);
        }

        PrimitiveType RemapType(PrimitiveType type)
        {
            if (CheckedReferenceTypes.IsType(type))
            {
                var referenceIndex = CheckedReferenceTypes.Index(type);
                return checkedReferenceIndexMap.TryGetValue(referenceIndex,
                        out var remappedReferenceIndex)
                    ? CheckedReferenceTypes.Type(remappedReferenceIndex)
                    : throw Invalid(
                        "A method reference is missing its checked-reference closure mapping.");
            }

            if (NativeFixedArrayTypes.IsType(type))
            {
                return arrayIndexMap.TryGetValue(
                        NativeFixedArrayTypes.Index(type), out var arrayIndex)
                    ? NativeFixedArrayTypes.Type(arrayIndex)
                    : throw Invalid(
                        "An A2 type reference is missing its array closure mapping.");
            }
            if (NativePointerTypes.IsType(type))
            {
                return pointerIndexMap.TryGetValue(
                        NativePointerTypes.Index(type), out var pointerIndex)
                    ? NativePointerTypes.Type(pointerIndex)
                    : throw Invalid(
                        "An A2 type reference is missing its pointer closure mapping.");
            }
            if (NativeFunctionPointerTypes.IsType(type)
                || BoundEnumTypes.IsType(type)
                || ManagedClassTypes.IsClass(type))
            {
                throw Unsupported(
                    "The A2 type-reference projection cannot remap this type family.");
            }

            var structureIndex = (int)type - 100;
            if (structureIndex >= 0
                && structureIndex < program.Structures.Length)
            {
                return structureIndexMap.TryGetValue(structureIndex,
                        out var remappedIndex)
                    ? (PrimitiveType)(100 + remappedIndex)
                    : throw Invalid(
                        "An A2 type reference is missing its TypeDef closure mapping.");
            }

            return type;
        }

        var projectedArrays = ImmutableArray.CreateBuilder<
            BoundNativeFixedArrayType>(arrayOrder.Length);
        foreach (var arrayIndex in arrayOrder)
        {
            projectedArrays.Add(program.NativeFixedArrayTypes[arrayIndex] with
            {
                ElementType = RemapType(
                    program.NativeFixedArrayTypes[arrayIndex].ElementType),
            });
        }

        var projectedPointers = ImmutableArray.CreateBuilder<
            NativePointerTypeDefinition>(pointerOrder.Length);
        foreach (var pointerIndex in pointerOrder)
        {
            projectedPointers.Add(program.NativePointerTypes[pointerIndex] with
            {
                PointeeType = RemapType(
                    program.NativePointerTypes[pointerIndex].PointeeType),
            });
        }

        var expectedFixedArrayTypes = projectedArrays.ToImmutable();
        var expectedNativePointerTypes = projectedPointers.ToImmutable();
        var projectedCheckedReferenceTypes
            = ImmutableArray.CreateBuilder<CheckedReferenceTypeDefinition>(
                checkedReferenceOrder.Length);
        foreach (var referenceIndex in checkedReferenceOrder)
        {
            projectedCheckedReferenceTypes.Add(
                program.CheckedReferenceTypes[referenceIndex] with {
                    ReferentType = RemapType(
                        program.CheckedReferenceTypes[referenceIndex]
                            .ReferentType),
                });
        }

        var projectedStructures = ImmutableArray.CreateBuilder<BoundStruct>(
            structureOrder.Length);
        foreach (var structureIndex in structureOrder)
        {
            var typeDefRow = typeDefStart + structureIndex;
            var original = program.Structures[structureIndex];
            var remappedOriginal = BoundTreeTraversal.RewriteTypes(original,
                RemapType);
            var fields = remappedOriginal.Fields.ToBuilder();
            for (var fieldOrdinal = 0;
                fieldOrdinal < remappedOriginal.Fields.Length; fieldOrdinal++)
            {
                if (!requestedFields.TryGetValue(
                        (typeDefRow, fieldOrdinal), out var selections))
                {
                    continue;
                }

                var field = fields[fieldOrdinal] with {
                    StandardFieldOrigin = null,
                };
                var selectedKeys = selections.Select(
                        static selection => selection.Key)
                    .Distinct(StringComparer.Ordinal).ToImmutableArray();
                var fieldOrigin = new BoundStandardFieldOrigin(
                    selections[0].Provider, selections[0].DefinitionId,
                    selectedKeys,
                    selections.Any(static selection =>
                        selection.IsLookupRoot),
                    field) {
                    SelectedExportKeys = selectedKeys,
                };
                fields[fieldOrdinal] = field with {
                    StandardFieldOrigin = fieldOrigin,
                };
            }

            var projectedFields = fields.MoveToImmutable();
            var origin = originsByTypeDefRow.TryGetValue(typeDefRow,
                out var importedOrigin)
                ? importedOrigin
                : new BoundStandardTypeOrigin(providerSnapshot,
                    0x1300_0000u | checked((uint)typeDefRow),
                    [.. keysByTypeDefRow[typeDefRow]],
                    rootTypeDefRows.Contains(typeDefRow),
                    ReferenceRoot: providerSnapshot);
            var expectedDeclaration = remappedOriginal with {
                Fields = [.. projectedFields.Select(static field => field with {
                    StandardFieldOrigin = null,
                })],
                StandardTypeOrigin = null,
            };
            projectedStructures.Add(original with {
                Fields = projectedFields,
                StandardTypeOrigin = origin with {
                    ExpectedDeclaration = expectedDeclaration,
                    ExpectedFixedArrayTypes = expectedFixedArrayTypes,
                    ExpectedNativePointerTypes
                        = expectedNativePointerTypes,
                },
            });
        }

        var structures = projectedStructures.MoveToImmutable();
        var fixedArrays = projectedArrays.MoveToImmutable();
        var pointerTypes = projectedPointers.MoveToImmutable();
        var checkedReferences = projectedCheckedReferenceTypes
            .MoveToImmutable();
        var projectedFunctions
            = ImmutableArray.CreateBuilder<BoundFunction>(methodRoots.Count);
        foreach (var (function, exportKey) in methodRoots)
        {
            var variables = ImmutableArray.CreateBuilder<BoundVariable>(
                function.ParameterCount);
            for (var ordinal = 0; ordinal < function.ParameterCount; ordinal++)
            {
                variables.Add(function.Variables[ordinal] with {
                    Type = RemapType(function.Variables[ordinal].Type),
                });
            }

            projectedFunctions.Add(function with {
                Name = exportKey,
                ReturnType = RemapType(function.ReturnType),
                Variables = variables.MoveToImmutable(),
                Body = new BoundBlock([], default),
                IndexedBody = null,
                IsExternal = true,
                IsModuleReference = true,
                IsPrivate = false,
                IsInternal = false,
                ModuleImport = new BoundModuleImport(moduleIdentity,
                    exportKey, targetRequirement, runtimePolicy),
            });
        }
        var functions = projectedFunctions.MoveToImmutable();
        if (!NativePointerTypeContract.IsValidTable(pointerTypes,
                structures, [], fixedArrays, checkedReferences))
        {
            throw Invalid("The projected A2 native pointer table is invalid.");
        }

        var projectedProgram = new BoundProgram(functions, -1)
        {
            RuntimePolicy = (BoundRuntimePolicy)runtimePolicy,
            Structures = structures,
            NativePointerTypes = pointerTypes,
            NativeFixedArrayTypes = fixedArrays,
            CheckedReferenceTypes = checkedReferences,
        };
        if (!NativeFixedArrayContract.MatchesProgram(projectedProgram))
        {
            throw Invalid("The projected A2 fixed-array type table is invalid.");
        }
        if (initializationReferenceLocalFieldLayout)
        {
            SemanticVerifier.VerifyLibraryReferences(projectedProgram);
        }
        else
        {
            SemanticVerifier.VerifyLibraryWithDirectNativeImports(
                projectedProgram);
        }
        var standardQuerySourceAssociation
            = program.StandardFieldProviderClosure is
                BoundStandardFieldProviderClosure fieldProviderClosure
                ? new BoundStandardQuerySourceAssociation([
                    fieldProviderClosure,
                ])
                : null;
        return new CompilationReference(structures, functions,
            (BoundRuntimePolicy)runtimePolicy,
            nativePointerTypes: pointerTypes,
            nativeFixedArrayTypes: fixedArrays,
            checkedReferenceTypes: checkedReferences,
            standardQuerySourceAssociation:
                standardQuerySourceAssociation,
            coreEvaluationFormIdentitySelections:
                program.CoreEvaluationFormIdentitySelections,
            coreEvaluationFormSchemaProviders:
                program.CoreEvaluationFormSchemaProviders);
    }

    private static BoundSourceTypeProviderSnapshot
        CreateA2SourceTypeProviderSnapshot(
        TrRmStructuralMetadataValidator.ValidatedExecutionTables tables,
        byte[] artifact, string schemaIdentity, int moduleRow)
    {
        var (targetRequirement, runtimePolicy)
            = tables.Modules.GetRequirements(moduleRow);
        var (featureFirst, featureCount)
            = tables.Modules.GetFeatureRange(moduleRow);
        var features = ImmutableArray.CreateBuilder<BoundSourceTypeFeature>(
            featureCount);
        for (var offset = 0; offset < featureCount; offset++)
        {
            var feature = tables.Features.GetFeature(featureFirst + offset);
            features.Add(new BoundSourceTypeFeature(feature.FeatureId,
                feature.RuleRevision));
        }

        var targetProfiles
            = ImmutableArray.CreateBuilder<BoundSourceTypeTargetProfile>(
                tables.TargetProfiles.Count);
        for (var row = 1; row <= tables.TargetProfiles.Count; row++)
        {
            var profile = tables.TargetProfiles.GetProfile(row);
            targetProfiles.Add(new BoundSourceTypeTargetProfile(
                profile.ProfileId, profile.RuleRevision));
        }

        return new BoundSourceTypeProviderSnapshot(schemaIdentity, [.. artifact],
            moduleRow, tables.Modules.GetIdentity(moduleRow),
            tables.Modules.GetGoverningRuleEdition(moduleRow),
            targetRequirement, runtimePolicy, features.MoveToImmutable(),
            targetProfiles.MoveToImmutable());
    }

    private static BoundProgram RetainLoadedA2SourceTypeFacts(
        BoundProgram program, byte[][] artifacts, string[] schemaIdentities,
        TrRmStructuralMetadataValidator.ValidatedExecutionTables tables,
        int moduleRow,
        ImmutableArray<TrRmStandardTypeProviderBinding> typeProviderBindings,
        bool retainExecutableTypes = false,
        int consumerArtifactIndex = 0)
    {
        if ((uint)consumerArtifactIndex >= (uint)artifacts.Length
            || consumerArtifactIndex >= schemaIdentities.Length
            || !(StringComparer.Ordinal.Equals(
                    schemaIdentities[consumerArtifactIndex],
                StandardForeignInvocationStorageFixedArraySchemaIdentity)
            || StringComparer.Ordinal.Equals(
                schemaIdentities[consumerArtifactIndex],
                StandardForeignInvocationStorageFixedArrayEmptyInitializationSchemaIdentity)
            || StringComparer.Ordinal.Equals(
                schemaIdentities[consumerArtifactIndex],
                StandardForeignInvocationStorageFixedArrayEmptyUnitInitializationSchemaIdentity)
            || StringComparer.Ordinal.Equals(
                schemaIdentities[consumerArtifactIndex],
                StandardForeignInvocationStorageFixedArrayEmptyUnitScalarFrozenValuesSchemaIdentity)
            || StringComparer.Ordinal.Equals(
                schemaIdentities[consumerArtifactIndex],
                StandardForeignInvocationStorageFixedArrayCopyReturnCoverageSchemaIdentity)
            || StringComparer.Ordinal.Equals(
                schemaIdentities[consumerArtifactIndex],
                StandardForeignInvocationStorageFixedArrayCopyReturnCoverageRepeatInitializationSchemaIdentity)
            || StringComparer.Ordinal.Equals(
                schemaIdentities[consumerArtifactIndex],
                StandardLocalAggregateLayoutSchemaIdentity)
            || StringComparer.Ordinal.Equals(
                schemaIdentities[consumerArtifactIndex],
                StandardInitReferenceBodiesLocalFieldLayoutSchemaIdentity)
            || StringComparer.Ordinal.Equals(
                schemaIdentities[consumerArtifactIndex],
                StandardInitReferenceBodiesNestedFieldDestinationsSchemaIdentity)
            || StringComparer.Ordinal.Equals(
                schemaIdentities[consumerArtifactIndex],
                StandardLayoutQueriesSchemaIdentity))
            || tables.Schema is not (
                TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyDraft
                or TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyEmptyInitializationDraft
                or TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyEmptyUnitInitializationDraft
                or TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyEmptyUnitScalarFrozenValuesDraft
                or TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyReturnCoverageDraft
                or TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyReturnCoverageRepeatInitializationDraft
                or TrRmArtifactSchema.LocalAggregateLayoutDraft
                or TrRmArtifactSchema.InitializationReferenceLocalFieldLayoutDraft
                or TrRmArtifactSchema.InitializationReferenceLocalFieldLayoutNestedFieldDestinationsDraft
                or TrRmArtifactSchema.Standard)
            || (!retainExecutableTypes
                && (program.EntryPoint != -1
                    || !program.Functions.IsEmpty))
            || program.Structures.IsEmpty)
        {
            return program;
        }

        var (typeDefStart, typeDefEnd)
            = tables.Modules.GetTypeDefRange(moduleRow);
        if (typeDefStart <= 0
            || typeDefEnd - typeDefStart != program.Structures.Length)
        {
            throw Invalid(
                "The loaded A2 type declarations do not match their Module row range.");
        }

        var rootSnapshot = CreateA2SourceTypeProviderSnapshot(tables,
            artifacts[consumerArtifactIndex],
            schemaIdentities[consumerArtifactIndex], moduleRow);
        var snapshotsByModule = new Dictionary<(int ArtifactIndex, int ModuleRow),
            BoundSourceTypeProviderSnapshot>
        {
            [(consumerArtifactIndex, moduleRow)] = rootSnapshot,
        };

        BoundSourceTypeProviderSnapshot GetSnapshot(int artifactIndex,
            int selectedModuleRow)
        {
            var key = (artifactIndex, selectedModuleRow);
            if (snapshotsByModule.TryGetValue(key, out var snapshot))
            {
                return snapshot;
            }

            var schemaIdentity = schemaIdentities[artifactIndex];
            if (schemaIdentity is not (
                    StandardForeignInvocationStorageFixedArraySchemaIdentity
                    or StandardForeignInvocationStorageFixedArrayEmptyInitializationSchemaIdentity
                    or StandardForeignInvocationStorageFixedArrayEmptyUnitInitializationSchemaIdentity
                    or StandardForeignInvocationStorageFixedArrayEmptyUnitScalarFrozenValuesSchemaIdentity
                    or StandardForeignInvocationStorageFixedArrayCopyReturnCoverageSchemaIdentity
                    or StandardForeignInvocationStorageFixedArrayCopyReturnCoverageRepeatInitializationSchemaIdentity
                    or StandardLocalAggregateLayoutSchemaIdentity
                    or StandardInitReferenceBodiesLocalFieldLayoutSchemaIdentity
                    or StandardInitReferenceBodiesNestedFieldDestinationsSchemaIdentity
                    or StandardLayoutQueriesSchemaIdentity))
            {
                throw Unsupported(
                    "Loaded A2 type origins require A2, A2E or A2EU provider snapshots.");
            }

            var providerTables = ValidateForeignInvocationArtifact(
                CreateForeignInvocationIndex(artifacts[artifactIndex],
                    schemaIdentity), schemaIdentity, null,
                createA3ResourceLedger: false);
            snapshot = CreateA2SourceTypeProviderSnapshot(providerTables,
                artifacts[artifactIndex], schemaIdentity, selectedModuleRow);
            snapshotsByModule.Add(key, snapshot);
            return snapshot;
        }

        var bindingsByExpectedTypeDef = new Dictionary<int,
            List<TrRmStandardTypeProviderBinding>>();
        foreach (var binding in typeProviderBindings)
        {
            if (binding.ConsumerArtifactIndex != consumerArtifactIndex
                || binding.ConsumerModuleRow != moduleRow)
            {
                continue;
            }

            if (!bindingsByExpectedTypeDef.TryGetValue(
                    binding.ExpectedTypeDefRow, out var bindings))
            {
                bindings = [];
                bindingsByExpectedTypeDef.Add(
                    binding.ExpectedTypeDefRow, bindings);
            }

            bindings.Add(binding);
        }

        var structures = ImmutableArray.CreateBuilder<BoundStruct>(
            program.Structures.Length);
        for (var structureIndex = 0;
            structureIndex < program.Structures.Length; structureIndex++)
        {
            var structure = program.Structures[structureIndex];
            var typeDefRow = typeDefStart + structureIndex;
            if (!bindingsByExpectedTypeDef.TryGetValue(typeDefRow,
                    out var bindings))
            {
                structures.Add(structure);
                continue;
            }

            var firstBinding = bindings[0];
            if (bindings.Any(binding =>
                    binding.ProviderArtifactIndex
                        != firstBinding.ProviderArtifactIndex
                    || binding.ProviderModuleRow
                        != firstBinding.ProviderModuleRow
                    || binding.ProviderTypeDefRow
                        != firstBinding.ProviderTypeDefRow
                    || !StringComparer.Ordinal.Equals(
                        binding.ProducerIdentity,
                        firstBinding.ProducerIdentity)
                    || binding.TargetRequirement
                        != firstBinding.TargetRequirement
                    || binding.RuntimePolicy != firstBinding.RuntimePolicy))
            {
                throw Invalid(
                    "One loaded A2 TypeDef has conflicting provider origins.");
            }

            var providerSnapshot = GetSnapshot(
                firstBinding.ProviderArtifactIndex,
                firstBinding.ProviderModuleRow);
            var expectations = ImmutableArray.CreateBuilder<
                BoundSourceTypeExpectation>(
                bindings.Count + typeProviderBindings.Length);
            foreach (var binding in bindings)
            {
                expectations.Add(new BoundSourceTypeExpectation(rootSnapshot,
                    binding.ImportRow,
                    0x1300_0000u | checked((uint)typeDefRow),
                    binding.ProducerIdentity, binding.ExportKey,
                    binding.TargetRequirement, binding.RuntimePolicy,
                    providerSnapshot,
                    0x1300_0000u
                        | checked((uint)binding.ProviderTypeDefRow)));
            }

            foreach (var binding in typeProviderBindings)
            {
                if (binding.ConsumerArtifactIndex
                        != firstBinding.ProviderArtifactIndex
                    || binding.ConsumerModuleRow
                        != firstBinding.ProviderModuleRow)
                {
                    continue;
                }

                expectations.Add(new BoundSourceTypeExpectation(
                    GetSnapshot(binding.ConsumerArtifactIndex,
                        binding.ConsumerModuleRow),
                    binding.ImportRow,
                    0x1300_0000u
                        | checked((uint)binding.ExpectedTypeDefRow),
                    binding.ProducerIdentity, binding.ExportKey,
                    binding.TargetRequirement, binding.RuntimePolicy,
                    GetSnapshot(binding.ProviderArtifactIndex,
                        binding.ProviderModuleRow),
                    0x1300_0000u
                        | checked((uint)binding.ProviderTypeDefRow)));
            }

            var exportKeys = bindings.Select(
                    static binding => binding.ExportKey)
                .Distinct(StringComparer.Ordinal).ToImmutableArray();
            var expectedDeclaration = structure with {
                StandardTypeOrigin = null,
            };
            structures.Add(structure with {
                StandardTypeOrigin = new BoundStandardTypeOrigin(
                    providerSnapshot,
                    0x1300_0000u
                        | checked((uint)firstBinding.ProviderTypeDefRow),
                    exportKeys, false, expectations.ToImmutable(),
                    rootSnapshot, expectedDeclaration,
                    program.NativeFixedArrayTypes,
                    program.NativePointerTypes),
            });
        }

        var exportsByStructure = new List<string>?[
            program.Structures.Length];
        foreach (var (definition, exportKey) in tables.Exports
            .GetExportsForModule(moduleRow))
        {
            if (definition >> 24 != 0x13)
            {
                continue;
            }

            var typeDefRow = checked((int)(definition
                & 0x00FF_FFFF));
            if (typeDefRow < typeDefStart || typeDefRow >= typeDefEnd
                || tables.TypeDefs.GetOwnerModule(typeDefRow) != moduleRow
                || tables.Imports.IsExpectedDefinition(definition))
            {
                throw Invalid(
                    "A loaded A2 TypeDef publication does not identify a local declaration.");
            }

            var structureIndex = typeDefRow - typeDefStart;
            var keys = exportsByStructure[structureIndex] ??= [];
            keys.Add(exportKey);
        }

        var publications = ImmutableArray.CreateBuilder<
            BoundSourceTypePublication>();
        for (var structureIndex = 0;
            structureIndex < exportsByStructure.Length; structureIndex++)
        {
            var declaration = structures[structureIndex];
            var keys = exportsByStructure[structureIndex] ?? [];
            if (declaration.StandardTypeOrigin is not null)
            {
                if (keys.Count != 0)
                {
                    throw Invalid(
                        "An imported A2 TypeDef cannot retain local publication keys.");
                }

                continue;
            }

            publications.Add(new BoundSourceTypePublication(structureIndex,
                [.. keys], declaration with {
                    StandardTypeOrigin = null,
                }, program.NativeFixedArrayTypes,
                program.NativePointerTypes));
        }

        return program with {
            Structures = structures.MoveToImmutable(),
            SourceTypePublications = publications.ToImmutable(),
        };
    }
}
