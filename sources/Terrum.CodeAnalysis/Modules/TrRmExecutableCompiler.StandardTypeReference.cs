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
                    schemaIdentities, moduleIdentity, exportKeys, []);
            }

            if (!StringComparer.Ordinal.Equals(schemaIdentities[0],
                    StandardC1M1SchemaIdentity)
                && !StringComparer.Ordinal.Equals(schemaIdentities[0],
                    StandardC1M1Construction2SchemaIdentity)
                && !StringComparer.Ordinal.Equals(schemaIdentities[0],
                    StandardC1M1Construction2InitializerCopyEvidenceSchemaIdentity)
                && !StringComparer.Ordinal.Equals(schemaIdentities[0],
                    StandardC1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsSchemaIdentity)
                && !StringComparer.Ordinal.Equals(schemaIdentities[0],
                    StandardC1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsInitializerCopyEvidenceSchemaIdentity)
                && !StringComparer.Ordinal.Equals(schemaIdentities[0],
                    StandardCM1AR1SchemaIdentity)
                && !StringComparer.Ordinal.Equals(schemaIdentities[0],
                    StandardC1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsAssociatedRelationConstraintsInitializerCopyEvidenceSchemaIdentity)
                && !StringComparer.Ordinal.Equals(schemaIdentities[0],
                    StandardC1M1ConstructionL1R1SchemaIdentity)
                && !StringComparer.Ordinal.Equals(schemaIdentities[0],
                    StandardC1M1Construction2L1R1SchemaIdentity))
            {
                throw Unsupported(
                    "Complete Standard class declarations require an exact class schema.");
            }

            if (schemaIdentities.Skip(1).Any(schemaIdentity =>
                    !StringComparer.Ordinal.Equals(schemaIdentity,
                        schemaIdentities[0])))
            {
                throw Unsupported(
                    "The C1 class source-reference projection requires exact C1 or C1M1 selections for every provider.");
            }

            return CreateC1M1ClassSourceTypeReference(artifacts,
                schemaIdentities, moduleIdentity, exportKeys, []);
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
    /// <returns>The selected type and field declarations under exact C1M1, CM1, CM1AR1, Construction-1/2, LayoutQueries, nested FieldPath or EN1 schema selection.</returns>
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

        if (schemaIdentities.Length != 0
            && schemaIdentities.All(static schemaIdentity =>
                StringComparer.Ordinal.Equals(schemaIdentity,
                    StandardC1M1SchemaIdentity)
                || StringComparer.Ordinal.Equals(schemaIdentity,
                    StandardC1M1Construction2SchemaIdentity)
                || StringComparer.Ordinal.Equals(schemaIdentity,
                    StandardC1M1Construction2InitializerCopyEvidenceSchemaIdentity)
                || StringComparer.Ordinal.Equals(schemaIdentity,
                    StandardC1M1ConstructionL1R1SchemaIdentity)
                || StringComparer.Ordinal.Equals(schemaIdentity,
                    StandardC1M1Construction2L1R1SchemaIdentity)
                || StandardGenericMethodSchema
                    .IsCM1ClassParameterDefaults(schemaIdentity)))
        {
            return CreateC1M1ClassSourceTypeReference(artifacts,
                schemaIdentities, moduleIdentity, typeKeys, fieldKeys);
        }

        if (schemaIdentities.Length != 0
            && schemaIdentities.All(
                IsSelectedC1EnumClosureSchemaIdentity))
        {
            return CreateC1EnumClosureSourceTypeReference(artifacts,
                schemaIdentities, moduleIdentity, typeKeys, fieldKeys,
                resolveAllClassFieldTypes:
                    fieldKeys.Length != 0
                        && IsSelectedC1ClassInstanceLayoutSchemaIdentity(
                            schemaIdentities[0]));
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

    private readonly record struct GenericClassMethodImportReadResult(
        ImmutableArray<BoundGenericMethodImport> Imports,
        ImmutableArray<BoundGenericNominalTypeDefinition>
            NominalTypeDefinitions,
        ImmutableArray<BoundGenericNominalTypeInstance>
            NominalTypeInstances,
        ImmutableArray<BoundSpanTypeDefinition> SpanTypes,
        ImmutableArray<CheckedReferenceTypeDefinition>
            CheckedReferenceTypes,
        ImmutableArray<NativePointerTypeDefinition> NativePointerTypes,
        ImmutableArray<BoundInterfaceDefinition> Interfaces)
    {
        internal static GenericClassMethodImportReadResult Empty
            => new([], [], [], [], [], [], []);
    }

    private static CompilationReference CreateC1M1ClassSourceTypeReference(
        byte[][] artifacts, string[] schemaIdentities, string moduleIdentity,
        string[] exportKeys, string[] selectedFieldExportKeys,
        ImmutableArray<(string ProviderIdentity, string ExportKey)>
            selectedImportedClassTypeKeys = default,
        bool allowClassLayoutQueryBody = false,
        bool resolveAllClassFieldTypes = false,
        bool includeModuleFunctions = false,
        bool allowModuleEntryPointForRuntimeLinking = false)
    {
        var isEN1 = IsSelectedC1EnumClosureSchemaIdentity(
            schemaIdentities[0]);
        var isCM1 = StandardGenericMethodSchema.IsCM1ClassParameterDefaults(
            schemaIdentities[0]);
        var isConstruction
            = IsSelectedC1ClassConstructionSchemaIdentity(
                schemaIdentities[0]);
        if ((!isEN1 && !isCM1 && !isConstruction
                && !StringComparer.Ordinal.Equals(schemaIdentities[0],
                    StandardC1M1SchemaIdentity))
            || schemaIdentities.Skip(1).Any(schemaIdentity =>
                !StringComparer.Ordinal.Equals(schemaIdentity,
                    schemaIdentities[0])))
        {
            throw Unsupported(
                "The C1 class source-reference projection requires one exact C1M1, CM1, Construction-1/2 or EN1 schema selection for every provider.");
        }
        if (allowClassLayoutQueryBody
            && !IsSelectedC1ClassInstanceLayoutSchemaIdentity(
                schemaIdentities[0]))
        {
            throw Unsupported(
                "Class query Body type references require an exact class-instance-layout successor.");
        }
        if (resolveAllClassFieldTypes
            && !IsSelectedC1ClassInstanceLayoutSchemaIdentity(
                schemaIdentities[0]))
        {
            throw Unsupported(
                "Complete class field layout closure requires an exact class-instance-layout successor.");
        }

        var moduleValidation = ValidateStandardInputModules(artifacts,
            schemaIdentities, moduleIdentity, a3LayoutResourceLedger: null);
        var rootModuleRow = moduleValidation.RootModuleRow;
        var rootTables = ValidateForeignInvocationArtifact(
            CreateForeignInvocationIndex(artifacts[0], schemaIdentities[0]),
            schemaIdentities[0], null, createA3ResourceLedger: false);
        EnsureC1ClassDeclarationTablesSupported(rootTables);
        var usesMethodReceiverAccess
            = !isEN1 && GetC1M1ClassMetadataSchema(
                schemaIdentities[0]).UsesMethodReceiverAccess();
        var usesConstruction2ReceiverAccess
            = rootTables.Schema.UsesObjectConstruction();
        if (isEN1 && rootTables.Schema.UsesPropertyInitializedState())
        {
            throw Unsupported(
                "The bounded EN1 class source-reference projection does not yet retain PI1 property and initialized-state declarations.");
        }
        var (methodStart, methodEnd)
            = rootTables.Modules.GetMethodDefRange(rootModuleRow);
        var hasUnsupportedClassMethod = false;
        for (var methodRow = methodStart;
            methodRow < methodEnd; methodRow++)
        {
            if (rootTables.Methods.GetOwnerType(methodRow) != 0)
            {
                hasUnsupportedClassMethod = true;
                break;
            }
        }

        if ((!allowModuleEntryPointForRuntimeLinking
                && rootTables.Modules.GetEntryMethod(rootModuleRow) != 0)
            || (hasUnsupportedClassMethod && !allowClassLayoutQueryBody
                && !isCM1 && !isConstruction))
        {
            throw Unsupported(
                "The bounded class source-reference projection does not retain class-owned MethodDefs for this exact schema.");
        }

        var selectedTypeDefs = new Dictionary<int, List<string>>();
        var selectedFieldKeySet = selectedFieldExportKeys.ToHashSet(
            StringComparer.Ordinal);
        if (selectedFieldKeySet.Count != selectedFieldExportKeys.Length)
        {
            throw new ArgumentException(
                "Selected FieldDef ExportKeys cannot contain duplicates.",
                nameof(selectedFieldExportKeys));
        }
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

        var fieldProviderBindings
            = ImmutableArray<TrRmStandardFieldProviderBinding>.Empty;
        ImmutableArray<TrRmStandardTypeProviderBinding>
            typeProviderBindings;
        ImmutableArray<TrRmStandardMethodProviderBinding>
            methodProviderBindings;
        if (isEN1)
        {
            var fieldValidation
                = TrRmFixedArrayProviderBindingValidator
                    .ValidateFieldDefImports(rootTables, artifacts,
                        schemaIdentities, moduleValidation.Modules,
                        rootModuleRow);
            fieldProviderBindings = fieldValidation.FieldBindings;
            typeProviderBindings = fieldValidation.TypeBindings;
            methodProviderBindings = fieldValidation.MethodBindings;
        }
        else
        {
            var classProviderValidation
                = TrRmFixedArrayProviderBindingValidator
                    .ValidateC1M1ClassImports(rootTables, artifacts,
                        schemaIdentities, moduleValidation.Modules,
                        rootModuleRow);
            typeProviderBindings
                = classProviderValidation.TypeBindings;
            fieldProviderBindings
                = classProviderValidation.FieldBindings;
            methodProviderBindings
                = classProviderValidation.MethodBindings;
        }
        var genericClassMethodRead = isCM1
            ? CreateCM1GenericClassMethodImports(artifacts,
                schemaIdentities, moduleIdentity, rootModuleRow,
                rootTables, typeProviderBindings, methodProviderBindings,
                selectedTypeDefs)
            : GenericClassMethodImportReadResult.Empty;

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
                var fieldExpectations
                    = ImmutableArray.CreateBuilder<
                        BoundStandardClassFieldExpectation>();
                foreach (var fieldBinding in fieldProviderBindings)
                {
                    if (fieldBinding.ConsumerArtifactIndex
                            != binding.ConsumerArtifactIndex
                        || fieldBinding.ConsumerModuleRow
                            != binding.ConsumerModuleRow
                        || fieldBinding.OwnerImportRow
                            != binding.ImportRow
                        || fieldBinding.ExpectedOwnerTypeDefRow
                            != binding.ExpectedTypeDefRow)
                    {
                        continue;
                    }

                    if (fieldBinding.ProviderArtifactIndex
                            != binding.ProviderArtifactIndex
                        || fieldBinding.ProviderModuleRow
                            != binding.ProviderModuleRow
                        || fieldBinding.ProviderTypeDefRow
                            != binding.ProviderTypeDefRow)
                    {
                        throw Invalid(
                            "An EN1 class FieldDef Import resolved through a different provider than its owner TypeDef Import.");
                    }

                    var fieldProviderTables = GetC1M1Tables(artifacts,
                        schemaIdentities,
                        fieldBinding.ProviderArtifactIndex);
                    var fieldProviderSnapshot
                        = CreateA2SourceTypeProviderSnapshot(
                            fieldProviderTables,
                            artifacts[fieldBinding.ProviderArtifactIndex],
                            schemaIdentities[
                                fieldBinding.ProviderArtifactIndex],
                            fieldBinding.ProviderModuleRow);
                    fieldExpectations.Add(
                        new BoundStandardClassFieldExpectation(
                            fieldBinding.ImportRow,
                            fieldBinding.OwnerImportRow,
                            0x1400_0000u | checked((uint)
                                fieldBinding.ExpectedFieldDefRow),
                            expectedDefinitionId,
                            0x1400_0000u | checked((uint)
                                fieldBinding.ProviderFieldDefRow),
                            providerDefinitionId,
                            fieldBinding.ExportKey,
                            fieldProviderSnapshot));
                }

                var methodExpectations
                    = ImmutableArray.CreateBuilder<
                        BoundStandardClassMethodExpectation>();
                foreach (var methodBinding in methodProviderBindings)
                {
                    if (methodBinding.ConsumerArtifactIndex
                            != binding.ConsumerArtifactIndex
                        || methodBinding.ConsumerModuleRow
                            != binding.ConsumerModuleRow
                        || methodBinding.OwnerImportRow
                            != binding.ImportRow
                        || methodBinding.ExpectedOwnerTypeDefRow
                            != binding.ExpectedTypeDefRow)
                    {
                        continue;
                    }

                    if (methodBinding.ProviderArtifactIndex
                            != binding.ProviderArtifactIndex
                        || methodBinding.ProviderModuleRow
                            != binding.ProviderModuleRow
                        || methodBinding.ProviderTypeDefRow
                            != binding.ProviderTypeDefRow)
                    {
                        throw Invalid(
                            "A class MethodDef Import resolved through a different provider than its owner TypeDef Import.");
                    }

                    var methodProviderTables = GetC1M1Tables(artifacts,
                        schemaIdentities,
                        methodBinding.ProviderArtifactIndex);
                    var methodProviderSnapshot
                        = CreateA2SourceTypeProviderSnapshot(
                            methodProviderTables,
                            artifacts[methodBinding.ProviderArtifactIndex],
                            schemaIdentities[
                                methodBinding.ProviderArtifactIndex],
                            methodBinding.ProviderModuleRow);
                    methodExpectations.Add(
                        new BoundStandardClassMethodExpectation(
                            methodBinding.ImportRow,
                            methodBinding.OwnerImportRow,
                            0x1600_0000u | checked((uint)
                                methodBinding.ExpectedMethodDefRow),
                            expectedDefinitionId,
                            0x1600_0000u | checked((uint)
                                methodBinding.ProviderMethodDefRow),
                            providerDefinitionId,
                            methodBinding.ExportKey,
                            methodProviderSnapshot));
                }

                if (!classExpectationsByActualType.TryGetValue(
                        actualIdentity, out var classExpectations))
                {
                    classExpectations = [];
                    classExpectationsByActualType.Add(actualIdentity,
                        classExpectations);
                }
                classExpectations.Add(
                    new BoundStandardClassExpectation(expectation,
                        expectedView) {
                        FieldExpectations = fieldExpectations.ToImmutable(),
                        MethodExpectations =
                            methodExpectations.ToImmutable(),
                    });
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
            if (type.Form == 0x0D && type.IsRow)
            {
                _ = DecodeEN1FieldFunctionPointer(rootTables, type.Row,
                    rootModuleRow);
                return;
            }
            if (type.Form == 0x01 && type.TypeCode == 0x8C
                && type.ArgumentCode == 0 && type.Qualifiers == 0)
            {
                if (rootTables.Modules.GetRequirements(rootModuleRow)
                    .TargetRequirement
                    != TrRmModuleTableReader
                        .WindowsX64NativeTargetRequirement)
                {
                    throw Unsupported(
                        "A native pointer field requires the Windows x64 target.");
                }

                return;
            }
            if (type.Form is 0x01 or 0x0C
                && type.TypeCode == 0x8C
                && (type.Qualifiers & ~0x01) == 0
                && rootTables.TypeSpecs.TryGetNativePointerPointeeTypeSpecRef(
                    typeSpecReference, out var pointeeTypeSpecReference))
            {
                var pointeeTypeSpec = pointeeTypeSpecReference;
                var pointee = rootTables.TypeSpecs.GetTypeSpecRef(
                    pointeeTypeSpec);
                var pointeeDepth = 0;
                while (pointee.Form == 0x06 && pointee.Qualifiers == 0)
                {
                    if (++pointeeDepth > SemanticVerifier.MaximumDepth
                        || --remainingClassTypeVisits < 0)
                    {
                        throw Unsupported(
                            "An EN1 native pointer pointee exceeds the TypeSpec traversal limit.");
                    }

                    var optionalRow = rootTables.TypeSpecs.GetRow(
                        pointee.Row);
                    pointeeTypeSpec = BinaryPrimitives
                        .ReadUInt32LittleEndian(optionalRow[2..]);
                    pointee = rootTables.TypeSpecs.GetTypeSpecRef(
                        pointeeTypeSpec);
                }
                if (rootTables.Modules.GetRequirements(rootModuleRow)
                    .TargetRequirement
                    != TrRmModuleTableReader
                        .WindowsX64NativeTargetRequirement)
                {
                    throw Unsupported(
                        "A native pointer field requires the Windows x64 target.");
                }

                if (pointee.Form == 0xF2 && pointee.Qualifiers == 0
                    && TrRmConstantSourceValueValidator
                        .TryGetBoundScalarType(pointee.TypeCode, out _))
                {
                    return;
                }

                if (pointee.Form is 0x02 or 0x03)
                {
                    var pointeeTypeDefRow = rootTables.TypeSpecs
                        .GetNominalTypeDefRow(pointeeTypeSpec,
                            rootTables.TypeDefs, rootTables.Imports);
                    var pointeeKind
                        = rootTables.TypeDefs.GetKind(pointeeTypeDefRow);
                    if ((pointee.Qualifiers == 0
                            && (pointeeKind is 1 or 4)
                            || isEN1 && pointeeKind == 2)
                        && rootTables.TypeDefs.GetGenericRange(
                            pointeeTypeDefRow).Count == 0)
                    {
                        return;
                    }
                }

                throw Unsupported(
                    "The C1 class TypeSpec closure supports scalar, enum, non-generic struct or class-handle pointer pointees.");
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
            var selectedFields
                = ImmutableArray.CreateBuilder<
                    BoundStandardClassFieldDeclaration>(
                        declaration.Fields.Length);
            foreach (var field in declaration.Fields)
            {
                var selectedKeys = selectedFieldExportKeys.Where(
                        field.ExportKeys.Contains)
                    .ToImmutableArray();
                if (!selectedKeys.IsEmpty
                    && ((field.Flags & 3) != 2
                        || (field.Flags & 8) != 0))
                {
                    throw new ArgumentException(
                        "A selected FieldDef ExportKey does not identify a public instance class field.",
                        nameof(selectedFieldExportKeys));
                }

                selectedFields.Add(field with {
                    SelectedExportKeys = selectedKeys,
                });
            }

            declaration = declaration with {
                Fields = selectedFields.MoveToImmutable(),
            };
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

        var retainedImportedClassTypeKeys
            = selectedImportedClassTypeKeys.IsDefault
                ? new HashSet<(string ProviderIdentity, string ExportKey)>()
                : selectedImportedClassTypeKeys.ToHashSet();
        foreach (var declaration in classDeclarations)
        {
            if (!StringComparer.Ordinal.Equals(
                    declaration.Declaration.Identity.ModuleIdentity,
                    moduleIdentity)
                || declaration.SelectedExportKeys.IsEmpty)
            {
                continue;
            }

            foreach (var method in declaration.Declaration.Methods)
            {
                if (method.SelectedExportKeys.IsEmpty
                    || method.GenericParameterCount != 0)
                {
                    continue;
                }

                IncludeImportedClassType(method.ReturnTypeSpecReference);
                IncludeImportedClassType(
                    method.ReceiverTypeSpecReference);
                foreach (var parameter in method.Parameters)
                {
                    IncludeImportedClassType(
                        parameter.TypeSpecReference);
                }
            }
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
            var selectedFields = ImmutableArray.CreateBuilder<
                BoundStandardClassFieldDeclaration>(
                    declaration.Fields.Length);
            foreach (var field in declaration.Fields)
            {
                selectedFields.Add(field with {
                    SelectedExportKeys = [.. selectedFieldExportKeys.Where(
                        field.ExportKeys.Contains)],
                });
            }
            declaration = declaration with {
                Fields = selectedFields.MoveToImmutable(),
            };
            var selectedTypeKeys
                = retainedImportedClassTypeKeys.Where(selection =>
                            StringComparer.Ordinal.Equals(
                                selection.ProviderIdentity,
                                providerModule.Identity))
                        .Select(static selection => selection.ExportKey)
                        .Where(key => declaration.ExportKeys.Contains(key,
                            StringComparer.Ordinal))
                        .Distinct(StringComparer.Ordinal)
                        .ToImmutableArray();
            classDeclarations.Add(new BoundStandardClassDeclaration(
                declaration,
                GetStandardTypeExportKeys(providerTables,
                    binding.ProviderModuleRow, binding.ProviderTypeDefRow),
                classExpectationsByActualType.TryGetValue(identity,
                    out var expectations) ? [.. expectations] : []) {
                SelectedExportKeys = selectedTypeKeys,
            });
        }

        void IncludeImportedClassType(uint typeSpecReference)
        {
            if (typeSpecReference == 0)
            {
                return;
            }

            var typeSpec = rootTables.TypeSpecs.GetTypeSpecRef(
                typeSpecReference);
            if (!typeSpec.IsRow || typeSpec.Form != 0x03)
            {
                return;
            }

            var typeDefRow = rootTables.TypeSpecs.GetNominalTypeDefRow(
                typeSpecReference, rootTables.TypeDefs, rootTables.Imports);
            if (rootTables.TypeDefs.GetKind(typeDefRow) != 2)
            {
                return;
            }

            var importBinding = rootTables.Imports.GetBindingForImportRow(
                checked((int)typeSpec.Row));
            retainedImportedClassTypeKeys.Add((
                importBinding.ProducerIdentity, importBinding.ExportKey));
        }

        if (isEN1)
        {
            foreach (var binding in typeProviderBindings)
            {
                if (binding.ConsumerArtifactIndex != 0
                    || binding.ConsumerModuleRow != rootModuleRow
                    || rootTables.TypeDefs.GetKind(
                        binding.ExpectedTypeDefRow) != 2)
                {
                    continue;
                }

                AddTypeDefinition(typeDefinitions, definitionsByIdentity,
                    bindingsByActualType, rootTables, moduleIdentity,
                    rootModuleRow, binding.ExpectedTypeDefRow);
            }
        }

        foreach (var typeDefinition
            in genericClassMethodRead.NominalTypeDefinitions)
        {
            if (definitionsByIdentity.TryGetValue(typeDefinition.Identity,
                    out var existingIndex))
            {
                var existing = typeDefinitions[existingIndex];
                if (!BoundGenericNominalTypeContract
                        .AreEquivalentDefinitions(existing, typeDefinition))
                {
                    throw Invalid(
                        "A generic class MethodDef changed a retained nominal TypeDef view.");
                }

                var expectations
                    = existing.ProviderExpectations.ToBuilder();
                foreach (var expectation
                    in typeDefinition.ProviderExpectations)
                {
                    if (!expectations.Any(candidate =>
                            BoundGenericNominalTypeContract
                                .AreEquivalentProviderExpectations(
                                    candidate, expectation)))
                    {
                        expectations.Add(expectation);
                    }
                }
                typeDefinitions[existingIndex] = existing with {
                    ProviderExpectations = expectations.ToImmutable(),
                };
            }
            else
            {
                definitionsByIdentity.Add(typeDefinition.Identity,
                    typeDefinitions.Count);
                typeDefinitions.Add(typeDefinition);
            }
        }

        var genericNominalTypeInstances
            = ImmutableArray.CreateBuilder<BoundGenericNominalTypeInstance>(
                genericClassMethodRead.NominalTypeInstances.Length
                    + classDeclarations.Count);
        genericNominalTypeInstances.AddRange(
            genericClassMethodRead.NominalTypeInstances);
        var classTypeInstanceIndexes
            = new Dictionary<BoundGenericNominalTypeIdentity, int>();
        for (var index = 0;
            index < genericNominalTypeInstances.Count; index++)
        {
            var instance = genericNominalTypeInstances[index];
            if (instance.Arguments.IsEmpty && instance.Qualifiers == 0
                && instance.TypeSpecReference == 0
                && instance.TypeSpecProviderIdentity is null
                && instance.TypeSpecForm == 0)
            {
                _ = classTypeInstanceIndexes.TryAdd(
                    instance.Definition, index);
            }
        }
        foreach (var declaration in classDeclarations)
        {
            if (declaration.SelectedExportKeys.IsEmpty
                || classTypeInstanceIndexes.ContainsKey(
                    declaration.Declaration.Identity))
            {
                continue;
            }

            var instanceIndex = genericNominalTypeInstances.Count;
            classTypeInstanceIndexes.Add(
                declaration.Declaration.Identity, instanceIndex);
            genericNominalTypeInstances.Add(
                new BoundGenericNominalTypeInstance(
                    declaration.Declaration.Identity, [], 0));
        }

        var classMethodTypeSpecInstanceIndexes = new Dictionary<(
            BoundGenericNominalTypeIdentity Identity,
            uint TypeSpecReference), int>();
        var checkedReferenceTypes
            = ImmutableArray.CreateBuilder<CheckedReferenceTypeDefinition>(
                genericClassMethodRead.CheckedReferenceTypes.Length + 4);
        checkedReferenceTypes.AddRange(
            genericClassMethodRead.CheckedReferenceTypes);
        var checkedReferenceTypeIndexes
            = new Dictionary<CheckedReferenceTypeDefinition, int>();
        for (var index = 0; index < checkedReferenceTypes.Count; index++)
        {
            _ = checkedReferenceTypeIndexes.TryAdd(
                checkedReferenceTypes[index], index);
        }
        var functions = ImmutableArray.CreateBuilder<BoundFunction>();
        var importedFunctionNames = new HashSet<string>(
            StringComparer.Ordinal);
        foreach (var declaration in classDeclarations)
        {
            if (declaration.SelectedExportKeys.IsEmpty
                || !declaration.Expectations.IsEmpty)
            {
                continue;
            }

            var className = string.IsNullOrEmpty(
                    declaration.Declaration.Namespace)
                ? declaration.Declaration.Name
                : declaration.Declaration.Namespace + "."
                    + declaration.Declaration.Name;
            foreach (var method in declaration.Declaration.Methods)
            {
                if (method.SelectedExportKeys.IsEmpty
                    || method.ConstructionRole != 0
                    || (method.Flags & 3) != 2)
                {
                    continue;
                }

                if (method.GenericParameterCount != 0)
                {
                    continue;
                }

                if (method.BodyRow == 0
                    || !TryGetClassMethodResultType(
                        method.ReturnTypeSpecReference, out var returnType))
                {
                    throw Unsupported(
                        "The bounded CM1 class source reference supports only bodyful nongeneric scalar, selected class-handle or plain checked-reference member results.");
                }

                var variables = ImmutableArray.CreateBuilder<BoundVariable>(
                    method.Parameters.Length
                        + (method.ReceiverTypeSpecReference == 0 ? 0 : 1));
                var isStatic = method.ReceiverTypeSpecReference == 0;
                if (!isStatic)
                {
                    var receiverTypeSpec = rootTables.TypeSpecs
                        .GetTypeSpecRef(
                            method.ReceiverTypeSpecReference);
                    if (!receiverTypeSpec.IsRow
                        || receiverTypeSpec.Form is not (0x02 or 0x03)
                        || (receiverTypeSpec.Qualifiers & ~0x01) != 0
                        || (!usesMethodReceiverAccess
                            && !usesConstruction2ReceiverAccess
                            && receiverTypeSpec.Qualifiers != 0)
                        || (usesMethodReceiverAccess
                            ? ((method.Flags & 4) != 0)
                                != ((receiverTypeSpec.Qualifiers & 1) != 0)
                            : (method.Flags & 4) != 0))
                    {
                        throw Unsupported(
                            "A class method receiver requires the exact selected class access contract.");
                    }

                    var receiverTypeDefRow = rootTables.TypeSpecs
                        .GetNominalTypeDefRow(
                            method.ReceiverTypeSpecReference,
                            rootTables.TypeDefs, rootTables.Imports);
                    if (receiverTypeDefRow
                            != (int)(declaration.Declaration.Identity
                                .DefinitionId & 0x00FF_FFFF))
                    {
                        throw Unsupported(
                            "A class method receiver does not match the selected class handle view.");
                    }

                    if (!TryGetClassMethodNominalType(
                            method.ReceiverTypeSpecReference,
                            out var receiverType))
                    {
                        throw Unsupported(
                            "A class method receiver has no selected normal class TypeSpec.");
                    }

                    variables.Add(new BoundVariable("this",
                        receiverType,
                        (receiverTypeSpec.Qualifiers & 1) != 0, default));
                }

                var defaults
                    = ImmutableArray.CreateBuilder<
                        BoundOrdinaryParameterDefault>();
                for (var ordinal = 0;
                    ordinal < method.Parameters.Length; ordinal++)
                {
                    var parameter = method.Parameters[ordinal];
                    if (parameter.Mode != 0
                        || !TryGetClassMethodParameterType(
                            parameter.TypeSpecReference, parameter.Flags,
                            out var parameterType,
                            out var isImplicitRef))
                    {
                        throw Unsupported(
                            "The bounded CM1 class source reference supports unit, scalar, selected class-handle or plain checked-reference member parameters.");
                    }

                    variables.Add(new BoundVariable(parameter.Name,
                        parameterType, false, default) {
                        IsImplicitRef = isImplicitRef,
                    });
                    if (parameter.DefaultValue is BoundConstant value)
                    {
                        defaults.Add(new BoundOrdinaryParameterDefault(
                            ordinal, value, null, -1, default,
                            IsPending: false));
                    }
                }

                var exportKey = method.SelectedExportKeys[0];
                var functionName = className + "." + method.Name;
                if (!importedFunctionNames.Add(functionName))
                {
                    throw Unsupported(
                        "The bounded CM1 class source reference does not support overloaded static member names.");
                }

                var targetRequirement
                    = declaration.Declaration.Provider.TargetRequirement;
                var runtimePolicy
                    = declaration.Declaration.Provider.RuntimePolicy;
                functions.Add(new BoundFunction(functionName, returnType,
                    variables.MoveToImmutable(),
                    method.Parameters.Length + (isStatic ? 0 : 1),
                    new BoundBlock([], default),
                    default) {
                    IsExternal = true,
                    IsStatic = isStatic,
                    DeclaringStruct = className,
                    RequiresUnsafeCaller = (method.EffectsMask & 4) != 0,
                    DeclaredRequiresUnsafeCaller =
                        (method.EffectsMask & 4) != 0,
                    DoesNotReturn = (method.EffectsMask & 2) != 0,
                    DependencyContracts =
                        DecodeC1M1ClassMethodDependencyContracts(
                            method.DependencyClauses,
                            method.Parameters.Length,
                            method.ReceiverTypeSpecReference != 0),
                    ModuleImport = new BoundModuleImport(
                        declaration.Declaration.Provider.ModuleIdentity,
                        exportKey, targetRequirement, runtimePolicy),
                    HasOrdinaryParameterDefaultSurface = true,
                    OrdinaryParameterDefaults =
                        defaults.ToImmutable(),
                });
            }
        }

        foreach (var declaration in classDeclarations)
        {
            if (declaration.SelectedExportKeys.IsEmpty
                || declaration.Expectations.IsEmpty)
            {
                continue;
            }

            var className = string.IsNullOrEmpty(
                    declaration.Declaration.Namespace)
                ? declaration.Declaration.Name
                : declaration.Declaration.Namespace + "."
                    + declaration.Declaration.Name;
            foreach (var classExpectation in declaration.Expectations)
            {
                var expectedMethods = classExpectation.ExpectedDeclaration
                    .Methods.ToDictionary(static method => method.DefinitionId);
                foreach (var methodGroup in classExpectation
                    .MethodExpectations.GroupBy(
                        static expectation => expectation.ExpectedDefinitionId))
                {
                    // Alias rows stay in the expectation; overload resolution needs one target.
                    var methodExpectations = methodGroup
                        .OrderBy(static expectation => expectation.ExportKey,
                            StringComparer.Ordinal).ToArray();
                    var methodExpectation = methodExpectations[0];
                    if (methodExpectations.Any(candidate =>
                            candidate.ProviderDefinitionId
                                != methodExpectation.ProviderDefinitionId
                            || !StringComparer.Ordinal.Equals(
                                candidate.Provider.ModuleIdentity,
                                methodExpectation.Provider.ModuleIdentity)
                            || !candidate.Provider.ProviderArtifactDigest
                                .AsSpan().SequenceEqual(
                                    methodExpectation.Provider
                                        .ProviderArtifactDigest.AsSpan())))
                    {
                        throw Invalid(
                            "A class MethodDef alias group resolved through distinct provider views.");
                    }

                    if (!expectedMethods.TryGetValue(
                            methodExpectation.ExpectedDefinitionId,
                            out var method)
                        || method.ConstructionRole != 0
                        || method.GenericParameterCount != 0
                        || (method.Flags & 3) != 2)
                    {
                        continue;
                    }

                    var providerBinding = methodProviderBindings.Single(
                        candidate =>
                            candidate.ConsumerArtifactIndex == 0
                            && candidate.ConsumerModuleRow == rootModuleRow
                            && candidate.ImportRow
                                == methodExpectation.ImportRow);
                    var providerTables = GetC1M1Tables(artifacts,
                        schemaIdentities,
                        providerBinding.ProviderArtifactIndex);
                    var providerMethod = declaration.Declaration.Methods
                        .Single(candidate => candidate.DefinitionId
                            == methodExpectation.ProviderDefinitionId);
                    if (method.BodyRow != 0 || providerMethod.BodyRow == 0
                        || !TryGetClassMethodResultType(
                            method.ReturnTypeSpecReference,
                            out var returnType))
                    {
                        throw Unsupported(
                            "The bounded CM1 class source reference supports only bodyless expected Methods with bodyful nongeneric scalar, selected class-handle or plain checked-reference providers.");
                    }

                    var isStatic = method.ReceiverTypeSpecReference == 0;
                    var variables
                        = ImmutableArray.CreateBuilder<BoundVariable>(
                            method.Parameters.Length
                                + (isStatic ? 0 : 1));
                    if (!isStatic)
                    {
                        var expectedReceiverTypeSpec
                            = rootTables.TypeSpecs.GetTypeSpecRef(
                                method.ReceiverTypeSpecReference);
                        var receiverTypeSpec = providerTables.TypeSpecs
                            .GetTypeSpecRef(
                                providerMethod.ReceiverTypeSpecReference);
                        if (!expectedReceiverTypeSpec.IsRow
                            || expectedReceiverTypeSpec.Form
                                is not (0x02 or 0x03)
                            || (expectedReceiverTypeSpec.Qualifiers
                                & ~0x01) != 0
                            || !receiverTypeSpec.IsRow
                            || receiverTypeSpec.Form is not (0x02 or 0x03)
                            || (receiverTypeSpec.Qualifiers & ~0x01) != 0
                            || (!usesMethodReceiverAccess
                                && !usesConstruction2ReceiverAccess
                                && expectedReceiverTypeSpec.Qualifiers != 0)
                            || expectedReceiverTypeSpec.Qualifiers
                                != receiverTypeSpec.Qualifiers
                            || (usesMethodReceiverAccess
                                ? ((method.Flags & 4) != 0)
                                    != ((expectedReceiverTypeSpec
                                        .Qualifiers & 1) != 0)
                                    || ((providerMethod.Flags & 4) != 0)
                                        != ((receiverTypeSpec.Qualifiers
                                            & 1) != 0)
                                : (method.Flags & 4) != 0
                                    || (providerMethod.Flags & 4) != 0)
                            || rootTables.TypeSpecs
                                .GetNominalTypeDefRow(
                                    method.ReceiverTypeSpecReference,
                                    rootTables.TypeDefs,
                                    rootTables.Imports)
                                != GetTypeDefRow(
                                    methodExpectation
                                        .ExpectedOwnerDefinitionId)
                            || providerTables.TypeSpecs
                                .GetNominalTypeDefRow(
                                    providerMethod.ReceiverTypeSpecReference,
                                    providerTables.TypeDefs,
                                    providerTables.Imports)
                                != providerBinding.ProviderTypeDefRow)
                        {
                            throw Invalid(
                                "A class MethodDef Import receiver does not match its selected provider class.");
                        }

                        if (!TryGetClassMethodNominalType(
                                method.ReceiverTypeSpecReference,
                                out var receiverType))
                        {
                            throw Invalid(
                                "A class MethodDef Import receiver has no selected normal class TypeSpec.");
                        }

                        variables.Add(new BoundVariable("this",
                            receiverType,
                            (expectedReceiverTypeSpec.Qualifiers & 1) != 0,
                            default));
                    }

                    var defaults = ImmutableArray.CreateBuilder<
                        BoundOrdinaryParameterDefault>();
                    for (var ordinal = 0;
                        ordinal < method.Parameters.Length; ordinal++)
                    {
                        var parameter = method.Parameters[ordinal];
                        if (parameter.Mode != 0
                            || !TryGetClassMethodParameterType(
                                parameter.TypeSpecReference,
                                parameter.Flags, out var parameterType,
                                out var isImplicitRef))
                        {
                            throw Unsupported(
                                "The bounded CM1 class source reference supports unit, scalar, selected class-handle or plain checked-reference imported member parameters.");
                        }

                        variables.Add(new BoundVariable(parameter.Name,
                            parameterType, false, default) {
                            IsImplicitRef = isImplicitRef,
                        });
                        if (parameter.DefaultValue is BoundConstant value)
                        {
                            defaults.Add(new BoundOrdinaryParameterDefault(
                                ordinal, value, null, -1, default,
                                IsPending: false));
                        }
                    }

                    var functionName = className + "." + method.Name;
                    if (!importedFunctionNames.Add(functionName))
                    {
                        throw Unsupported(
                            "The bounded CM1 class source reference does not support overloaded imported member names.");
                    }

                    functions.Add(new BoundFunction(functionName,
                        returnType, variables.MoveToImmutable(),
                        method.Parameters.Length + (isStatic ? 0 : 1),
                        new BoundBlock([], default), default) {
                        IsExternal = true,
                        IsStatic = isStatic,
                        DeclaringStruct = className,
                        RequiresUnsafeCaller = (method.EffectsMask & 4) != 0,
                        DeclaredRequiresUnsafeCaller =
                            (method.EffectsMask & 4) != 0,
                        DoesNotReturn = (method.EffectsMask & 2) != 0,
                        DependencyContracts =
                            DecodeC1M1ClassMethodDependencyContracts(
                                method.DependencyClauses,
                                method.Parameters.Length,
                                method.ReceiverTypeSpecReference != 0),
                        ModuleImport = new BoundModuleImport(
                            methodExpectation.Provider.ModuleIdentity,
                            methodExpectation.ExportKey,
                            methodExpectation.Provider.TargetRequirement,
                            methodExpectation.Provider.RuntimePolicy),
                        HasOrdinaryParameterDefaultSurface = true,
                        OrdinaryParameterDefaults =
                            defaults.ToImmutable(),
                    });
                }
            }
        }

        if (includeModuleFunctions)
        {
            if (!rootTables.Schema.UsesObjectConstruction())
            {
                throw Unsupported(
                    "The bounded full-module class reference retains top-level functions only for exact Construction-2.");
            }

            foreach (var (definitionId, exportKey) in
                rootTables.Exports.GetExportsForModule(rootModuleRow))
            {
                if ((definitionId >> 24) != 0x16)
                {
                    continue;
                }

                var methodRow = GetMethodDefRow(definitionId);
                var signatureRow
                    = rootTables.Methods.GetSignature(methodRow);
                if (rootTables.Methods.GetOwnerType(methodRow) != 0
                    || rootTables.Signatures.GetReceiverTypeSpec(
                        signatureRow) != 0
                    || rootTables.Signatures.GetGenericRange(
                        signatureRow).Count != 0
                    || !TryGetClassMethodResultType(
                        rootTables.Signatures.GetResultTypeSpec(
                            signatureRow), out var returnType))
                {
                    throw Unsupported(
                        "A selected C2 module function requires a bounded non-generic signature without a receiver.");
                }

                var (parameterStart, parameterCount)
                    = rootTables.Signatures.GetParamRange(signatureRow);
                if (parameterCount > SemanticVerifier.MaximumArguments)
                {
                    throw Unsupported(
                        "A selected C2 module function exceeds the checked logical parameter limit.");
                }

                var variables
                    = ImmutableArray.CreateBuilder<BoundVariable>(
                        parameterCount);
                var ordinaryParameterDefaults
                    = ImmutableArray.CreateBuilder<
                        BoundOrdinaryParameterDefault>();
                var hasClassParameterSurface
                    = rootTables.Schema.UsesClassParameterDefaults();
                for (var ordinal = 0; ordinal < parameterCount; ordinal++)
                {
                    var parameterRow = parameterStart + ordinal;
                    if (rootTables.Parameters.GetMode(parameterRow) != 0
                        || !TryGetClassMethodParameterType(
                            rootTables.Parameters.GetTypeSpec(parameterRow),
                            rootTables.Parameters.GetFlags(parameterRow),
                            out var parameterType,
                            out var isImplicitRef))
                    {
                        throw Unsupported(
                            "A selected C2 module function has an unsupported parameter mode or type.");
                    }

                    var parameterName = "arg" + ordinal.ToString(
                        System.Globalization.CultureInfo.InvariantCulture);
                    if (hasClassParameterSurface)
                    {
                        if (!rootTables.MethodParameterLabels
                                .TryGetNameStringRow(methodRow,
                                    checked((uint)ordinal),
                                    out var nameStringRow)
                            || !rootTables.MethodParameterLabels
                                .TryGetDefaultConstantSourceRow(methodRow,
                                    checked((uint)ordinal),
                                    out var defaultConstantSourceRow))
                        {
                            throw Invalid(
                                "A CM1 top-level MethodDef has an incomplete parameter surface.");
                        }

                        parameterName
                            = rootTables.Strings.DecodeName(nameStringRow);
                        if (defaultConstantSourceRow != 0)
                        {
                            var defaultValue
                                = TrRmConstantSourceValueValidator
                                    .MaterializeFrozenScalar(
                                        checked((int)
                                            defaultConstantSourceRow),
                                        rootTables.Parameters
                                            .GetTypeSpec(parameterRow),
                                        rootModuleRow,
                                        rootSnapshot.TargetRequirement,
                                        rootTables.ConstantSources,
                                        rootTables.FrozenValueNodes,
                                        rootTables.Blobs,
                                        rootTables.TypeSpecs,
                                        rootTables.Features,
                                        rootTables.Modules,
                                        rootTables.A3LayoutResourceLedger);
                            ordinaryParameterDefaults.Add(
                                new BoundOrdinaryParameterDefault(ordinal,
                                    defaultValue, null, -1, default,
                                    IsPending: false));
                        }
                    }
                    else if (rootTables.Schema.UsesMethodParameterLabels()
                        && rootTables.MethodParameterLabels
                            .TryGetNameStringRow(methodRow,
                                checked((uint)ordinal),
                                out var labelStringRow))
                    {
                        parameterName
                            = rootTables.Strings.DecodeName(
                                labelStringRow);
                    }

                    variables.Add(new BoundVariable(
                        parameterName,
                        parameterType, false, default) {
                        IsImplicitRef = isImplicitRef,
                    });
                }

                var dependencyClauses
                    = rootTables.Contracts.GetDependencyContractRows(
                            methodRow)
                        .Select(static row => row.Contract)
                        .ToImmutableArray();
                var effectsMask
                    = rootTables.Contracts.GetEffectsMask(methodRow);
                if ((effectsMask & ~6u) != 0
                    || !importedFunctionNames.Add(exportKey))
                {
                    throw Invalid(
                        "A selected C2 module function has conflicting effects or an ExportKey collision.");
                }

                functions.Add(new BoundFunction(exportKey, returnType,
                    variables.MoveToImmutable(), parameterCount,
                    new BoundBlock([], default), default) {
                    IsExternal = true,
                    IsModuleReference = true,
                    RequiresUnsafeCaller = (effectsMask & 4) != 0,
                    DeclaredRequiresUnsafeCaller =
                        (effectsMask & 4) != 0,
                    DoesNotReturn = (effectsMask & 2) != 0,
                    DependencyContracts =
                        DecodeC1M1ClassMethodDependencyContracts(
                            dependencyClauses, parameterCount,
                            hasReceiver: false),
                    ModuleImport = new BoundModuleImport(
                        moduleIdentity, exportKey,
                        rootSnapshot.TargetRequirement,
                        rootSnapshot.RuntimePolicy),
                    HasOrdinaryParameterDefaultSurface
                        = hasClassParameterSurface,
                    OrdinaryParameterDefaults
                        = ordinaryParameterDefaults.ToImmutable(),
                });
            }
        }

        bool TryGetScalarType(uint typeSpecReference,
            out PrimitiveType type)
        {
            type = PrimitiveType.Void;
            var typeSpec = rootTables.TypeSpecs.GetTypeSpecRef(
                typeSpecReference);
            return typeSpec.Form == 1 && typeSpec.Row == 0
                && typeSpec.Qualifiers == 0
                && TrRmConstantSourceValueValidator
                    .TryGetBoundScalarType(typeSpec.TypeCode, out type);
        }

        bool TryGetClassMethodNominalType(uint typeSpecReference,
            out PrimitiveType type)
        {
            type = PrimitiveType.Void;
            var typeSpec = rootTables.TypeSpecs.GetTypeSpecRef(
                typeSpecReference);
            if (!typeSpec.IsRow
                || typeSpec.Form is not (0x02 or 0x03)
                || typeSpec.Qualifiers > 1)
            {
                return false;
            }

            var typeDefRow = rootTables.TypeSpecs.GetNominalTypeDefRow(
                typeSpecReference, rootTables.TypeDefs, rootTables.Imports);
            if (rootTables.TypeDefs.GetKind(typeDefRow) != 2)
            {
                return false;
            }

            var expectedIdentity = new BoundGenericNominalTypeIdentity(
                moduleIdentity, moduleIdentity,
                0x1300_0000u | checked((uint)typeDefRow));
            BoundStandardClassDeclaration? selectedDeclaration = null;
            foreach (var candidate in classDeclarations)
            {
                var matchesExpected = candidate.Declaration.Identity
                        == expectedIdentity
                    || candidate.Expectations.Any(expectation =>
                        expectation.ExpectedDeclaration.Identity
                            == expectedIdentity);
                if (!matchesExpected)
                {
                    continue;
                }

                if (selectedDeclaration is not null
                    && selectedDeclaration.Declaration.Identity
                        != candidate.Declaration.Identity)
                {
                    throw Invalid(
                        "A CM1 class method TypeSpec resolves through conflicting class provider views.");
                }

                selectedDeclaration = candidate;
            }

            if (selectedDeclaration is null
                || selectedDeclaration.SelectedExportKeys.IsEmpty)
            {
                return false;
            }

            if ((typeSpec.Qualifiers & 1) != 0
                && (!selectedDeclaration.Declaration
                        .TryGetDeclaredMutability(out var ownerIsMutable)
                    || !ownerIsMutable))
            {
                return false;
            }

            var identity = selectedDeclaration.Declaration.Identity;
            if (!classTypeInstanceIndexes.ContainsKey(identity))
            {
                throw Invalid(
                    "A selected CM1 class method TypeSpec has no nominal class instance.");
            }

            var key = (identity, typeSpecReference);
            if (!classMethodTypeSpecInstanceIndexes.TryGetValue(key,
                    out var instanceIndex))
            {
                if (genericNominalTypeInstances.Count
                    >= BoundGenericNominalTypeTypes.MaximumCount)
                {
                    throw Unsupported(
                        "CM1 class method nominal parameter types exceed the analysis limit.");
                }

                instanceIndex = genericNominalTypeInstances.Count;
                genericNominalTypeInstances.Add(
                    new BoundGenericNominalTypeInstance(identity, [],
                        typeSpec.Qualifiers, typeSpecReference,
                        moduleIdentity, typeSpec.Form));
                classMethodTypeSpecInstanceIndexes.Add(key, instanceIndex);
            }

            type = BoundGenericNominalTypeTypes.Type(instanceIndex);
            return true;
        }

        bool TryGetClassMethodResultType(uint typeSpecReference,
            out PrimitiveType type)
        {
            if (TryGetScalarType(typeSpecReference, out type))
            {
                return true;
            }

            return TryGetClassMethodParameterType(typeSpecReference, 0,
                    out type, out var isImplicitRef)
                && !isImplicitRef;
        }

        bool TryGetClassMethodParameterType(uint typeSpecReference,
            byte flags, out PrimitiveType type, out bool isImplicitRef)
        {
            type = PrimitiveType.Void;
            isImplicitRef = false;
            if (flags > 1)
            {
                return false;
            }

            var parameterTypeSpec = rootTables.TypeSpecs.GetTypeSpecRef(
                typeSpecReference);
            if (!parameterTypeSpec.IsRow
                && parameterTypeSpec.Form == 1
                && parameterTypeSpec.Qualifiers == 0
                && parameterTypeSpec.TypeCode == 0)
            {
                return flags == 0;
            }

            if (TryGetScalarType(typeSpecReference, out type))
            {
                return flags == 0 && type != PrimitiveType.Void;
            }

            if (TryGetClassMethodNominalType(typeSpecReference, out type))
            {
                return flags == 0;
            }

            var reference = rootTables.TypeSpecs.GetTypeSpecRef(
                typeSpecReference);
            if (!reference.IsRow || reference.Form != 0x07
                || reference.Qualifiers != 0)
            {
                return false;
            }

            var row = rootTables.TypeSpecs.GetRow(reference.Row);
            var referentTypeSpecReference
                = BinaryPrimitives.ReadUInt32LittleEndian(row.Slice(2));
            if (!TryGetScalarType(referentTypeSpecReference,
                    out var referentType)
                || referentType == PrimitiveType.Void)
            {
                return false;
            }

            var definition = new CheckedReferenceTypeDefinition(
                referentType, IsWritable: false,
                IsGarbageCollected: false, IsInitialization: false,
                IsOptional: false);
            if (!checkedReferenceTypeIndexes.TryGetValue(definition,
                    out var referenceIndex))
            {
                if (checkedReferenceTypes.Count
                    >= SemanticVerifier.MaximumAnalysisVisits)
                {
                    throw Unsupported(
                        "CM1 class method checked-reference signatures exceed the analysis limit.");
                }

                referenceIndex = checkedReferenceTypes.Count;
                checkedReferenceTypes.Add(definition);
                checkedReferenceTypeIndexes.Add(definition,
                    referenceIndex);
            }

            type = CheckedReferenceTypes.Type(referenceIndex);
            isImplicitRef = flags == 1;
            return true;
        }

        var checkedReferenceTypeTable
            = checkedReferenceTypes.ToImmutable();
        if (!CheckedReferenceTypeContract.IsValidTable(
                checkedReferenceTypeTable))
        {
            throw Invalid(
                "The retained CM1 class method checked-reference table is invalid.");
        }

        return new CompilationReference([], functions.ToImmutable(),
            (BoundRuntimePolicy)rootTables.Modules
                .GetRequirements(rootModuleRow).RuntimePolicy,
            genericNominalTypeDefinitions: typeDefinitions.ToImmutable(),
            genericNominalTypeInstances:
                genericNominalTypeInstances.ToImmutable(),
            standardClassDeclarations: classDeclarations.ToImmutable(),
            genericMethodImports: genericClassMethodRead.Imports,
            retainedGenericMethodRoots:
                [.. genericClassMethodRead.Imports.Where(static import =>
                    !import.Origin.IsMethodDefTarget)],
            spanTypes: genericClassMethodRead.SpanTypes,
            checkedReferenceTypes: checkedReferenceTypeTable,
            nativePointerTypes:
                genericClassMethodRead.NativePointerTypes,
            interfaces: genericClassMethodRead.Interfaces);
    }

    private static GenericClassMethodImportReadResult
        CreateCM1GenericClassMethodImports(
        byte[][] artifacts, string[] schemaIdentities,
        string moduleIdentity, int rootModuleRow,
        TrRmStructuralMetadataValidator.ValidatedExecutionTables rootTables,
        ImmutableArray<TrRmStandardTypeProviderBinding>
            typeProviderBindings,
        ImmutableArray<TrRmStandardMethodProviderBinding>
            methodProviderBindings,
        Dictionary<int, List<string>> selectedTypeDefs)
    {
        var importRows = methodProviderBindings
            .Where(binding =>
                binding.ConsumerArtifactIndex == 0
                && binding.ConsumerModuleRow == rootModuleRow
                && rootTables.Methods.GetOwnerType(
                    binding.ExpectedMethodDefRow) != 0
                && rootTables.Signatures.GetGenericCount(
                    rootTables.Methods.GetSignature(
                        binding.ExpectedMethodDefRow)) != 0)
            .Select(static binding => binding.ImportRow)
            .Distinct()
            .Order()
            .ToArray();
        var selectedClassMethodExports
            = rootTables.Exports.GetExportsForModule(rootModuleRow)
                .Where(export =>
                    (export.Definition >> 24) == 0x16)
                .Where(export =>
                {
                    var methodRow = checked((int)(
                        export.Definition & 0x00FF_FFFF));
                    var ownerTypeDefRow = rootTables.Methods
                        .GetOwnerType(methodRow);
                    return ownerTypeDefRow != 0
                        && selectedTypeDefs.ContainsKey(ownerTypeDefRow)
                        && rootTables.Signatures.GetGenericCount(
                            rootTables.Methods.GetSignature(methodRow)) != 0;
                })
                .GroupBy(static export => export.Definition)
                .Select(static group => group
                    .OrderBy(static export => export.ExportKey,
                        StringComparer.Ordinal).First())
                .ToArray();
        if (importRows.Length == 0
            && selectedClassMethodExports.Length == 0)
        {
            return GenericClassMethodImportReadResult.Empty;
        }

        var providers = new TrRmStandardArtifactInput[
            artifacts.Length - 1];
        for (var index = 1; index < artifacts.Length; index++)
        {
            providers[index - 1] = new TrRmStandardArtifactInput(
                schemaIdentities[index], artifacts[index]);
        }

        var context = new GenericMethodReadContext(
            new TrRmStandardArtifactInput(schemaIdentities[0],
                artifacts[0]), providers);
        var tables
            = TrRmStructuralMetadataValidator
                .ValidateGenericMethodCallsDraftAndGetExecutionTables(
                    new TrRmArtifactIndex(context.Artifacts[0],
                        context.Schema));
        var moduleRow = FindGenericMethodModule(tables.Modules,
            moduleIdentity);
        if (moduleRow != rootModuleRow)
        {
            throw Invalid(
                "The generic class MethodDef validation selected a different root Module.");
        }

        var imports = ImmutableArray.CreateBuilder<
            BoundGenericMethodImport>(importRows.Length
                + selectedClassMethodExports.Length);
        foreach (var importRow in importRows)
        {
            var methodBinding = methodProviderBindings.Single(binding =>
                binding.ConsumerArtifactIndex == 0
                && binding.ConsumerModuleRow == moduleRow
                && binding.ImportRow == importRow);
            var ownerTypeBinding = typeProviderBindings.Single(binding =>
                binding.ConsumerArtifactIndex == 0
                && binding.ConsumerModuleRow == moduleRow
                && binding.ImportRow == methodBinding.OwnerImportRow);
            var classOwner = new BoundGenericMethodClassOwner(
                new BoundGenericNominalTypeIdentity(
                    ownerTypeBinding.ProducerIdentity,
                    ownerTypeBinding.ProducerIdentity,
                    ownerTypeBinding.ActualDefinitionId),
                ownerTypeBinding.ExportKey,
                0x1300_0000u | checked((uint)
                    ownerTypeBinding.ExpectedTypeDefRow),
                ownerTypeBinding.TargetRequirement,
                ownerTypeBinding.RuntimePolicy);
            var imported = CreateExpectedGenericMethodImport(tables,
                context, 0, moduleRow, importRow);
            CompleteGenericClassMethodOwner(imported, classOwner);
            if (imported.Binding is not BoundGenericMethodImportBinding
                    binding)
            {
                throw Invalid(
                    "A selected generic class MethodDef Import has no validated provider binding.");
            }
            imports.Add(binding.Provider);
        }
        foreach (var export in selectedClassMethodExports)
        {
            var methodRow = checked((int)(
                export.Definition & 0x00FF_FFFF));
            var ownerTypeDefRow
                = tables.Methods.GetOwnerType(methodRow);
            var ownerDefinitionId = 0x1300_0000u
                | checked((uint)ownerTypeDefRow);
            var classOwner = new BoundGenericMethodClassOwner(
                new BoundGenericNominalTypeIdentity(moduleIdentity,
                    moduleIdentity, ownerDefinitionId),
                selectedTypeDefs[ownerTypeDefRow]
                    .OrderBy(static key => key, StringComparer.Ordinal)
                    .First(),
                ownerDefinitionId,
                tables.Modules.GetRequirements(moduleRow)
                    .TargetRequirement,
                tables.Modules.GetRequirements(moduleRow).RuntimePolicy);
            var imported = CreateGenericMethodImport(tables, context, 0,
                moduleRow, export.Definition, export.ExportKey,
                isMethodDefTarget: false);
            imported.CompleteClassOwner(classOwner);
            imports.Add(imported);
        }

        context.CompleteBindings();
        return new GenericClassMethodImportReadResult(
            imports.ToImmutable(),
            context.GenericNominalTypeDefinitions.ToImmutable(),
            context.GenericNominalTypeInstances.ToImmutable(),
            [.. context.SpanTypes],
            [.. context.CheckedReferenceTypeDefinitions],
            context.GenericNominalPointerTypes.ToImmutable(),
            [.. context.InterfaceDefinitions.Values
                .SelectMany(static definitions => definitions)
                .Distinct()]);
    }

    private static void CompleteGenericClassMethodOwner(
        BoundGenericMethodImport imported,
        BoundGenericMethodClassOwner classOwner)
    {
        imported.CompleteClassOwner(classOwner);
        if (imported.Binding is BoundGenericMethodImportBinding binding)
        {
            binding.Expected.CompleteClassOwner(classOwner);
            binding.Provider.CompleteClassOwner(classOwner);
        }
    }

    private static ImmutableArray<BoundDependencyContract>
        DecodeC1M1ClassMethodDependencyContracts(
            ImmutableArray<BoundStandardClassMethodDependencyClause>
                dependencies,
            int parameterCount, bool hasReceiver)
    {
        if (dependencies.IsDefault)
        {
            throw Invalid(
                "A CM1 class MethodDef has an invalid Dependency clause view.");
        }
        if (dependencies.IsEmpty)
        {
            return [];
        }

        var dependsOnSources
            = ImmutableArray.CreateBuilder<BoundDependencySource>(
                dependencies.Length);
        var borrowedSources
            = ImmutableArray.CreateBuilder<BoundDependencySource>();
        var forwardedSources
            = ImmutableArray.CreateBuilder<BoundDependencySource>();
        foreach (var dependency in dependencies)
        {
            if (dependency.DestinationKind != 1
                || dependency.DestinationOrdinal != 0
                || dependency.Phase != 1)
            {
                throw Unsupported(
                    "CM1 class method references currently support only normal-result Dependency clauses.");
            }

            if (dependency.SourceKind == 0)
            {
                if (dependencies.Length != 1
                    || dependency.Relationship != 0
                    || dependency.SourceOrdinal != 0)
                {
                    throw Invalid(
                        "A CM1 class MethodDef has an invalid empty result Dependency source set.");
                }

                continue;
            }

            var source = dependency.SourceKind switch
            {
                1 when dependency.SourceOrdinal
                    < (uint)parameterCount
                    => new BoundDependencySource(
                        BoundDependencySourceKind.Formal,
                        checked((int)dependency.SourceOrdinal)),
                2 when hasReceiver && dependency.SourceOrdinal == 0
                    => new BoundDependencySource(
                        BoundDependencySourceKind.Receiver, null),
                _ => throw Invalid(
                    "A CM1 class MethodDef has an invalid result Dependency source."),
            };
            if (dependency.Relationship is < 1 or > 3)
            {
                throw Invalid(
                    "A CM1 class MethodDef has an invalid result Dependency relationship.");
            }

            dependsOnSources.Add(source);
            if (dependency.Relationship == 1)
            {
                borrowedSources.Add(source);
            }
            else if (dependency.Relationship == 2)
            {
                forwardedSources.Add(source);
            }
        }

        static ImmutableArray<BoundDependencySource> CanonicalizeSources(
            ImmutableArray<BoundDependencySource>.Builder sources)
            => sources.ToImmutable()
                .OrderBy(static source => source.Kind)
                .ThenBy(static source => source.FormalOrdinal ?? -1)
                .ToImmutableArray();

        var result = ImmutableArray.CreateBuilder<BoundDependencyContract>(3);
        result.Add(new BoundDependencyContract(
            BoundDependencyRelation.DependsOn,
            BoundDependencyDestination.Result, null,
            CanonicalizeSources(dependsOnSources)));
        if (!borrowedSources.IsEmpty)
        {
            result.Add(new BoundDependencyContract(
                BoundDependencyRelation.BorrowSourceStorage,
                BoundDependencyDestination.Result, null,
                CanonicalizeSources(borrowedSources)));
        }
        if (!forwardedSources.IsEmpty)
        {
            result.Add(new BoundDependencyContract(
                BoundDependencyRelation.ForwardsBacking,
                BoundDependencyDestination.Result, null,
                CanonicalizeSources(forwardedSources)));
        }
        return result.ToImmutable();
    }

    internal static ImmutableArray<BoundDependencyContract>
        DecodeC1M1ClassMethodDependencyContracts(
            ImmutableArray<TrRmDependencyContract> dependencies,
            int parameterCount, bool hasReceiver)
        => DecodeC1M1ClassMethodDependencyContracts(
            CreateC1M1ClassMethodDependencyClauseView(dependencies),
            parameterCount, hasReceiver);

    private static ImmutableArray<
        BoundStandardClassMethodDependencyClause>
        CreateC1M1ClassMethodDependencyClauseView(
            ImmutableArray<TrRmDependencyContract> dependencies)
    {
        if (dependencies.IsDefault)
        {
            throw Invalid(
                "A CM1 class MethodDef has an invalid Dependency clause view.");
        }

        return dependencies
            .OrderBy(static dependency => dependency.DestinationKind)
            .ThenBy(static dependency => dependency.DestinationOrdinal)
            .ThenBy(static dependency => dependency.Phase)
            .ThenBy(static dependency => dependency.SourceKind)
            .ThenBy(static dependency => dependency.SourceOrdinal)
            .Select(static dependency =>
                new BoundStandardClassMethodDependencyClause(
                    dependency.DestinationKind, dependency.SourceKind,
                    dependency.Relationship, dependency.Phase,
                    dependency.DestinationOrdinal,
                    dependency.SourceOrdinal))
            .ToImmutableArray();
    }

    private static BoundStandardClassTypeView CreateStandardClassTypeView(
        TrRmStructuralMetadataValidator.ValidatedExecutionTables tables,
        BoundGenericNominalTypeIdentity identity,
        BoundSourceTypeProviderSnapshot provider, int typeDefRow)
    {
        var typeDefId = 0x1300_0000u | checked((uint)typeDefRow);
        var (fieldStart, fieldEnd)
            = tables.TypeDefs.GetFieldRange(typeDefRow);
        var exportKeysByField = new Dictionary<uint, List<string>>();
        foreach (var (definitionId, exportKey) in tables.Exports
            .GetExportsForModule(provider.ModuleRow))
        {
            if ((definitionId >> 24) != 0x14)
            {
                continue;
            }

            if (!exportKeysByField.TryGetValue(definitionId,
                    out var exportKeys))
            {
                exportKeys = [];
                exportKeysByField.Add(definitionId, exportKeys);
            }
            exportKeys.Add(exportKey);
        }

        var fields = ImmutableArray.CreateBuilder<
            BoundStandardClassFieldDeclaration>(fieldEnd - fieldStart);
        for (var fieldRow = fieldStart; fieldRow < fieldEnd; fieldRow++)
        {
            var definitionId = 0x1400_0000u
                | checked((uint)fieldRow);
            fields.Add(new BoundStandardClassFieldDeclaration(
                definitionId, typeDefId,
                tables.Fields.GetName(fieldRow),
                tables.Fields.GetFlags(fieldRow),
                tables.Fields.GetTypeSpecRef(fieldRow)) {
                ExportKeys = exportKeysByField.TryGetValue(definitionId,
                    out var exportKeys) ? [.. exportKeys] : [],
            });
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

        var (methodStart, methodEnd)
            = tables.TypeDefs.GetMethodRange(typeDefRow);
        var methodExportKeys = new Dictionary<uint, List<string>>();
        foreach (var (definitionId, exportKey) in tables.Exports
            .GetExportsForModule(provider.ModuleRow))
        {
            if ((definitionId >> 24) != 0x16)
            {
                continue;
            }

            if (!methodExportKeys.TryGetValue(definitionId,
                    out var exportKeys))
            {
                exportKeys = [];
                methodExportKeys.Add(definitionId, exportKeys);
            }

            exportKeys.Add(exportKey);
        }

        var methods = ImmutableArray.CreateBuilder<
            BoundStandardClassMethodDeclaration>();
        for (var methodRow = methodStart; methodRow < methodEnd;
            methodRow++)
        {
            if (tables.NativeFunctionPointerAnchors
                .IsAnchorMethodDef(methodRow))
            {
                continue;
            }

            var definitionId = 0x1600_0000u
                | checked((uint)methodRow);
            var signature = tables.Methods.GetSignature(methodRow);
            var (parameterStart, parameterCount)
                = tables.Signatures.GetParamRange(signature);
            var genericParameterCount
                = tables.Signatures.GetGenericCount(signature);
            var (genericParameterStart, _) =
                tables.Signatures.GetGenericRange(signature);
            var genericParameters
                = ImmutableArray.CreateBuilder<
                    BoundStandardClassMethodGenericParameter>(
                        genericParameterCount);
            for (var ordinal = 0;
                ordinal < genericParameterCount; ordinal++)
            {
                var genericParameterRow
                    = genericParameterStart + ordinal;
                if (tables.GenericParams.GetOwnerDefId(
                        genericParameterRow) != definitionId
                    || tables.GenericParams.GetOrdinal(
                        genericParameterRow) != (uint)ordinal)
                {
                    throw Invalid(
                        "A CM1 class MethodDef GenericParam has an invalid owner or ordinal.");
                }

                var (constraintFirst, constraintCount)
                    = tables.GenericParams.GetConstraintRange(
                        genericParameterRow);
                var constraints
                    = ImmutableArray.CreateBuilder<
                        BoundStandardClassMethodGenericConstraint>(
                            constraintCount);
                for (var constraintOffset = 0;
                    constraintOffset < constraintCount;
                    constraintOffset++)
                {
                    var constraintRow = constraintFirst
                        + constraintOffset;
                    var ownerGenericParamRow
                        = tables.Constraints.GetOwnerGenericParam(
                            constraintRow);
                    if (ownerGenericParamRow != genericParameterRow)
                    {
                        throw Invalid(
                            "A CM1 class MethodDef Constraint has an invalid GenericParam owner.");
                    }

                    constraints.Add(
                        new BoundStandardClassMethodGenericConstraint(
                            constraintRow, ownerGenericParamRow,
                            tables.Constraints.GetKind(constraintRow),
                            tables.Constraints.GetPayload(constraintRow)));
                }

                genericParameters.Add(
                    new BoundStandardClassMethodGenericParameter(
                        genericParameterRow, definitionId,
                        checked((uint)ordinal),
                        tables.Strings.DecodeName(
                            tables.GenericParams.GetNameStringRow(
                                genericParameterRow)),
                        tables.GenericParams.GetFormalKind(
                            genericParameterRow),
                        tables.GenericParams.GetOwnershipDomain(
                            genericParameterRow),
                        tables.GenericParams.GetNullabilityDomain(
                            genericParameterRow),
                        tables.GenericParams.GetConstantTypeSpecRef(
                            genericParameterRow),
                        tables.GenericParams.GetDefaultArgumentRef(
                            genericParameterRow),
                        constraints.MoveToImmutable()));
            }
            var hasClassParameterSurface
                = tables.Schema.UsesClassParameterDefaults()
                    && genericParameterCount == 0;
            var hasParameterLabels
                = tables.Schema.UsesMethodParameterLabels();
            var parameters = ImmutableArray.CreateBuilder<
                BoundStandardClassMethodParameter>(parameterCount);
            for (var ordinal = 0; ordinal < parameterCount; ordinal++)
            {
                var parameterRow = parameterStart + ordinal;
                var name = "arg" + ordinal.ToString(
                    System.Globalization.CultureInfo.InvariantCulture);
                BoundConstant? defaultValue = null;
                if (hasParameterLabels)
                {
                    if (!tables.MethodParameterLabels.TryGetNameStringRow(
                            methodRow, checked((uint)ordinal),
                            out var nameStringRow))
                    {
                        throw Invalid(
                            "A class MethodDef has an incomplete parameter-label surface.");
                    }

                    name = tables.Strings.DecodeName(nameStringRow);
                }
                if (hasClassParameterSurface)
                {
                    if (!tables.MethodParameterLabels
                            .TryGetDefaultConstantSourceRow(methodRow,
                                checked((uint)ordinal),
                                out var constantSourceRow))
                    {
                        throw Invalid(
                            "A CM1 class MethodDef has an incomplete parameter surface.");
                    }

                    if (constantSourceRow != 0)
                    {
                        var ownerModule
                            = tables.Methods.GetOwnerModule(methodRow);
                        var (targetRequirement, _)
                            = tables.Modules.GetRequirements(ownerModule);
                        defaultValue
                            = TrRmConstantSourceValueValidator
                                .MaterializeFrozenScalar(
                                    checked((int)constantSourceRow),
                                    tables.Parameters.GetTypeSpec(
                                        parameterRow),
                                    ownerModule, targetRequirement,
                                    tables.ConstantSources,
                                    tables.FrozenValueNodes, tables.Blobs,
                                    tables.TypeSpecs, tables.Features,
                                    tables.Modules,
                                    tables.A3LayoutResourceLedger);
                    }
                }

                parameters.Add(new BoundStandardClassMethodParameter(
                    name,
                    tables.Parameters.GetTypeSpec(parameterRow),
                    tables.Parameters.GetMode(parameterRow),
                    tables.Parameters.GetFlags(parameterRow),
                    defaultValue));
            }

            var constructionRole = (byte)0;
            uint initializerMethodDefinitionId = 0;
            uint constructionPropertyDefinitionId = 0;
            var initializedMembers = ImmutableArray<uint>.Empty;
            if (tables.Contracts.HasConstructionClause(methodRow))
            {
                var construction
                    = tables.Contracts.GetConstructionClause(methodRow);
                constructionRole = construction.RoleTag;
                initializerMethodDefinitionId
                    = construction.InitializerMethodDefRow == 0 ? 0
                        : 0x1600_0000u
                            | construction.InitializerMethodDefRow;
                constructionPropertyDefinitionId
                    = construction.PropertyDefRow == 0 ? 0
                        : 0x1E00_0000u | construction.PropertyDefRow;
                initializedMembers
                    = construction.InitializedMemberDefIds;
            }

            var exportKeys = methodExportKeys.TryGetValue(definitionId,
                    out var keys)
                ? keys.ToImmutableArray() : [];
            methods.Add(new BoundStandardClassMethodDeclaration(
                definitionId, tables.Methods.GetName(methodRow),
                tables.Methods.GetFlags(methodRow),
                tables.Contracts.GetEffectsMask(methodRow),
                tables.Signatures.GetResultTypeSpec(signature),
                tables.Signatures.GetReceiverTypeSpec(signature),
                parameters.MoveToImmutable(), exportKeys, exportKeys,
                genericParameterCount,
                checked((uint)tables.Methods.GetBodyRow(methodRow)),
                constructionRole, initializerMethodDefinitionId,
                constructionPropertyDefinitionId, initializedMembers) {
                DependencyClauses =
                    CreateC1M1ClassMethodDependencyClauseView(
                        tables.Contracts.GetDependencyContracts(methodRow)),
                GenericParameters = genericParameters.MoveToImmutable(),
            });
        }

        return new BoundStandardClassTypeView(identity,
            tables.TypeDefs.GetNamespace(typeDefRow),
            tables.TypeDefs.GetName(typeDefRow),
            tables.TypeDefs.GetFlags(typeDefRow), fields.MoveToImmutable(),
            layouts.MoveToImmutable(), provider) {
            Methods = methods.ToImmutable(),
        };
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
            && !StandardGenericMethodSchema
                .IsCM1ClassParameterDefaults(schemaIdentity)
            && !IsSelectedC1ClassConstructionSchemaIdentity(
                schemaIdentity)
            && !IsSelectedC1EnumClosureSchemaIdentity(schemaIdentity))
        {
            throw Unsupported(
                "The C1 class provider closure requires an exact supported C1, CM1, Construction or EN1 selection.");
        }

        var tables = ValidateForeignInvocationArtifact(
            CreateForeignInvocationIndex(artifacts[artifactIndex],
                schemaIdentity), schemaIdentity, null,
            createA3ResourceLedger: false);
        EnsureC1ClassDeclarationTablesSupported(tables);
        return tables;
    }

    internal static bool IsSelectedC1ClassConstructionSchemaIdentity(
        string schemaIdentity)
        => schemaIdentity is
            StandardC1M1Construction2SchemaIdentity
            or StandardC1M1Construction2InitializerCopyEvidenceSchemaIdentity
            or StandardC1M1ConstructionL1R1SchemaIdentity
            or StandardC1M1Construction2L1R1SchemaIdentity
            or StandardC1M1ConstructionL1R1EnumClosureSchemaIdentity
            or StandardC1M1Construction2L1R1EnumClosureSchemaIdentity
            or StandardC1M1ConstructionL1R1EnumClosureInitializerCopyEvidenceSchemaIdentity
            or StandardC1M1Construction2L1R1EnumClosureInitializerCopyEvidenceSchemaIdentity
            or StandardC1M1ConstructionL1R1ClassInstanceLayoutInitializerCopyEvidenceSchemaIdentity
            or StandardC1M1Construction2L1R1ClassInstanceLayoutInitializerCopyEvidenceSchemaIdentity
            or StandardC1M1ConstructionL1R1ClassInstanceLayoutSchemaIdentity
            or StandardC1M1Construction2L1R1ClassInstanceLayoutSchemaIdentity;

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
        string[] selectedExportKeys, string[] selectedFieldExportKeys,
        ImmutableArray<(string ProviderIdentity, string ExportKey)>
            selectedImportedClassTypeKeys = default,
        bool allowClassLayoutQueryBody = false,
        bool resolveAllClassFieldTypes = false)
    {
        if (selectedFieldExportKeys.Distinct(StringComparer.Ordinal)
                .Count() != selectedFieldExportKeys.Length)
        {
            throw new ArgumentException(
                "Selected FieldDef ExportKeys cannot contain duplicates.",
                nameof(selectedFieldExportKeys));
        }

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
                schemaIdentities, moduleIdentity, selectedExportKeys,
                selectedFieldExportKeys,
                allowClassLayoutQueryBody: allowClassLayoutQueryBody,
                resolveAllClassFieldTypes: resolveAllClassFieldTypes);
        }

        var classKeySet = selectedClassKeys.ToHashSet(
            StringComparer.Ordinal);
        var enumAndStructKeys = selectedExportKeys.Where(exportKey =>
            !classKeySet.Contains(exportKey)).ToArray();
        var classReference = CreateC1M1ClassSourceTypeReference(artifacts,
            schemaIdentities, moduleIdentity, [.. selectedClassKeys],
            selectedFieldExportKeys, selectedImportedClassTypeKeys,
            allowClassLayoutQueryBody, resolveAllClassFieldTypes);
        var enumAndStructReference
            = CreateC1EnumClosureSourceTypeReferenceCore(artifacts,
                schemaIdentities, moduleIdentity, enumAndStructKeys,
                selectedFieldExportKeys, classReference,
                allowClassLayoutQueryBody,
                resolveAllClassFieldTypes);
        if (selectedFieldExportKeys.Length != 0)
        {
            var selectedFields = new Dictionary<string, (
                BoundSourceTypeProviderSnapshot Provider,
                uint DefinitionId)>(StringComparer.Ordinal);

            void AddSelectedField(string key,
                BoundSourceTypeProviderSnapshot provider,
                uint definitionId)
            {
                if (selectedFields.TryGetValue(key, out var existing))
                {
                    if (existing.DefinitionId != definitionId
                        || !SameA2SourceTypeProviderSnapshot(
                            existing.Provider, provider))
                    {
                        throw new ArgumentException(
                            "A selected FieldDef ExportKey is ambiguous across the selected provider closure.",
                            nameof(selectedFieldExportKeys));
                    }

                    return;
                }

                selectedFields.Add(key, (provider, definitionId));
            }

            foreach (var declaration in
                enumAndStructReference.StandardClassDeclarations)
            {
                foreach (var field in declaration.Declaration.Fields)
                {
                    foreach (var key in field.SelectedExportKeys)
                    {
                        AddSelectedField(key,
                            declaration.Declaration.Provider,
                            field.DefinitionId);
                    }
                }
            }
            foreach (var structure in enumAndStructReference.Structures)
            {
                foreach (var field in structure.Fields)
                {
                    if (field.StandardFieldOrigin is
                        BoundStandardFieldOrigin fieldOrigin)
                    {
                        foreach (var key in fieldOrigin.SelectedExportKeys)
                        {
                            AddSelectedField(key, fieldOrigin.Provider,
                                fieldOrigin.DefinitionId);
                        }
                    }
                }
            }
            if (selectedFieldExportKeys.Any(key =>
                    !selectedFields.ContainsKey(key)))
            {
                throw new ArgumentException(
                    "A selected FieldDef ExportKey is not inside a selected class or inline-struct closure.",
                    nameof(selectedFieldExportKeys));
            }
        }

        if (classReference.RuntimePolicy
                != enumAndStructReference.RuntimePolicy)
        {
            throw Invalid(
                "The EN1 class and enum references selected different runtime policies.");
        }

        return enumAndStructReference;
    }

    internal static CompilationReference
        CreateEN1ClassLayoutQuerySourceTypeReference(
            byte[][] artifacts, string[] schemaIdentities,
            string moduleIdentity, string[] selectedTypeExportKeys,
            string[] selectedFieldExportKeys,
            ImmutableArray<(string ProviderIdentity, string ExportKey)>
                selectedImportedClassTypeKeys)
    {
        if (schemaIdentities.Length == 0
            || schemaIdentities.Any(schemaIdentity =>
                !IsSelectedC1ClassInstanceLayoutSchemaIdentity(
                    schemaIdentity)))
        {
            throw Unsupported(
                "An EN1 class query Body reference requires one exact class-instance-layout successor for every provider.");
        }

        return CreateC1EnumClosureSourceTypeReference(artifacts,
            schemaIdentities, moduleIdentity, selectedTypeExportKeys,
            selectedFieldExportKeys, selectedImportedClassTypeKeys,
            allowClassLayoutQueryBody: true,
            resolveAllClassFieldTypes: true);
    }

    private static CompilationReference CreateC1EnumClosureSourceTypeReferenceCore(
        byte[][] artifacts, string[] schemaIdentities, string moduleIdentity,
        string[] selectedExportKeys, string[] selectedFieldExportKeys,
        CompilationReference? standardClassReference = null,
        bool allowClassLayoutQueryBody = false,
        bool resolveAllClassFieldTypes = false)
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
            || (methodStart != methodEnd && !allowClassLayoutQueryBody)
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

        var standardClassDeclarations
            = standardClassReference?.StandardClassDeclarations ?? [];
        var genericNominalTypeDefinitions
            = standardClassReference?.GenericNominalTypeDefinitions
                ?? ImmutableArray<BoundGenericNominalTypeDefinition>.Empty;
        var genericNominalTypeDefinitionBuilder
            = genericNominalTypeDefinitions.ToBuilder();
        var genericNominalTypeIdentities = genericNominalTypeDefinitions
            .Select(static definition => definition.Identity)
            .ToHashSet();
        if (standardClassReference is not null)
        {
            foreach (var typeDefRow in structTypeDefRows)
            {
                var identity = new BoundGenericNominalTypeIdentity(
                    moduleIdentity, moduleIdentity,
                    0x1300_0000u | checked((uint)typeDefRow));
                if (genericNominalTypeIdentities.Add(identity))
                {
                    genericNominalTypeDefinitionBuilder.Add(
                        new BoundGenericNominalTypeDefinition(identity,
                            rootTables.TypeDefs.GetNamespace(typeDefRow),
                            rootTables.TypeDefs.GetName(typeDefRow), 1, [],
                            rootTables.TypeSpecs.Count,
                            rootTables.ConstantSources.Count,
                            rootTables.Constraints.Count,
                            rootTables.AssociatedRelations.Count));
                }
            }
        }

        genericNominalTypeDefinitions
            = genericNominalTypeDefinitionBuilder.ToImmutable();
        var classIdentityByRootTypeDefRow = new Dictionary<int,
            BoundGenericNominalTypeIdentity>();
        var classIdentityByRootImportRow = new Dictionary<int,
            BoundGenericNominalTypeIdentity>();
        foreach (var declaration in standardClassDeclarations)
        {
            var classIdentity = declaration.Declaration.Identity;
            if (BoundStandardEnumDeclarationContract.SnapshotsMatch(
                    declaration.Declaration.Provider, rootSnapshot))
            {
                var typeDefId = classIdentity.DefinitionId;
                if ((typeDefId >> 24) != 0x13
                    || !classIdentityByRootTypeDefRow.TryAdd(
                        checked((int)(typeDefId & 0x00FF_FFFF)),
                        classIdentity))
                {
                    throw Invalid(
                        "The EN1 class reference contains an invalid or duplicate local class TypeDef identity.");
                }
            }

            foreach (var expectation in declaration.Expectations)
            {
                var binding = expectation.Binding;
                if (!BoundStandardEnumDeclarationContract.SnapshotsMatch(
                        binding.UsingModule, rootSnapshot))
                {
                    continue;
                }

                if (binding.ImportRow <= 0
                    || !classIdentityByRootImportRow.TryAdd(
                        binding.ImportRow, classIdentity))
                {
                    throw Invalid(
                        "The EN1 class reference contains an invalid or duplicate class Import identity.");
                }
            }
        }

        var genericNominalTypeInstances
            = standardClassReference?.GenericNominalTypeInstances
                .ToBuilder()
                ?? ImmutableArray.CreateBuilder<
                    BoundGenericNominalTypeInstance>();
        var nativePointerTypes
            = ImmutableArray.CreateBuilder<NativePointerTypeDefinition>();
        var nativePointerTypeIndexes
            = new Dictionary<NativePointerTypeDefinition, int>();
        var nativeFunctionPointerSignatures
            = ImmutableArray.CreateBuilder<
                NativeFunctionPointerSignatureDefinition>();
        int GetOrAddNativeFunctionPointerSignature(
            NativeFunctionPointerSignatureDefinition signature)
        {
            for (var index = 0;
                index < nativeFunctionPointerSignatures.Count; index++)
            {
                if (NativeFunctionPointerContract.AreEquivalent(
                        nativeFunctionPointerSignatures[index], signature))
                {
                    return index;
                }
            }

            if (nativeFunctionPointerSignatures.Count
                >= SemanticVerifier.MaximumAnalysisVisits)
            {
                throw Unsupported(
                    "The EN1 native function-pointer signature limit was exceeded.");
            }

            nativeFunctionPointerSignatures.Add(signature);
            return nativeFunctionPointerSignatures.Count - 1;
        }

        var classTypeInstanceIndexes = new Dictionary<(
            BoundGenericNominalTypeIdentity Identity,
            string TypeSpecProviderIdentity, uint TypeSpecReference), int>();
        for (var instanceIndex = 0;
            instanceIndex < genericNominalTypeInstances.Count;
            instanceIndex++)
        {
            var instance = genericNominalTypeInstances[instanceIndex];
            if (instance.TypeSpecReference != 0
                && instance.TypeSpecProviderIdentity
                    is string typeSpecProviderIdentity)
            {
                _ = classTypeInstanceIndexes.TryAdd((
                    instance.Definition, typeSpecProviderIdentity,
                    instance.TypeSpecReference), instanceIndex);
            }
        }

        var structures = structTypeDefRows.Length == 0
            ? ImmutableArray<BoundStruct>.Empty
            : CreateC1EnumClosureStructSourceTypes(rootTables,
                rootSnapshot, rootModuleRow, structTypeDefRows,
                selectedByTypeDef, enumIndexByTypeDefRow,
                enumIndexByImportRow, structureIndexByImportRow,
                enumDefinitions, nativePointerTypes,
                nativeFunctionPointerSignatures,
                nativePointerTypeIndexes, typeProviderBindings,
                fieldProviderValidation.FieldBindings, GetTables,
                GetSnapshot, GetExportKeys,
                genericNominalTypeDefinitions,
                genericNominalTypeInstances, classTypeInstanceIndexes,
                classIdentityByRootTypeDefRow,
                classIdentityByRootImportRow);
        var structureIndexByRootTypeDefRow = new Dictionary<int, int>(
            structTypeDefRows.Length);
        for (var structureIndex = 0;
            structureIndex < structTypeDefRows.Length;
            structureIndex++)
        {
            structureIndexByRootTypeDefRow.Add(
                structTypeDefRows[structureIndex], structureIndex);
        }

        if (selectedFieldExportKeys.Length != 0 && !structures.IsEmpty)
        {
            var selectedStructures = new HashSet<int>();
            var pendingStructures = new Stack<int>();

            void IncludeInlineStructure(uint typeSpecReference)
            {
                var type = rootTables.TypeSpecs.GetTypeSpecRef(
                    typeSpecReference);
                int structureIndex;
                if (type.Form == 0x02 && type.Qualifiers == 0)
                {
                    if (!structureIndexByRootTypeDefRow.TryGetValue(
                            checked((int)type.Row), out structureIndex))
                    {
                        return;
                    }
                }
                else if (type.Form == 0x03 && type.Qualifiers == 0)
                {
                    if (!structureIndexByImportRow.TryGetValue(
                            checked((int)type.Row), out structureIndex))
                    {
                        return;
                    }
                }
                else
                {
                    return;
                }

                _ = AddStructure(structureIndex);
            }

            bool AddStructure(int structureIndex)
            {
                if (!selectedStructures.Add(structureIndex))
                {
                    return false;
                }

                pendingStructures.Push(structureIndex);
                return true;
            }

            if (typeSpecForm == 0x0D && type.Form == 0x0D
                && type.IsRow)
            {
                var signature = DecodeEN1FieldFunctionPointer(rootTables,
                    type.Row, rootModuleRow);
                fieldType = NativeFunctionPointerTypes.Type(
                    GetOrAddNativeFunctionPointerSignature(signature));
                return true;
            }

            for (var structureIndex = 0;
                structureIndex < structTypeDefRows.Length;
                structureIndex++)
            {
                if (selectedByTypeDef.ContainsKey(
                        structTypeDefRows[structureIndex]))
                {
                    _ = AddStructure(structureIndex);
                }
            }
            foreach (var declaration in standardClassDeclarations)
            {
                if (declaration.SelectedExportKeys.IsEmpty
                    || !BoundStandardEnumDeclarationContract.SnapshotsMatch(
                        declaration.Declaration.Provider, rootSnapshot))
                {
                    continue;
                }

                foreach (var field in declaration.Declaration.Fields)
                {
                    IncludeInlineStructure(field.TypeSpecReference);
                }
            }

            while (pendingStructures.TryPop(out var structureIndex))
            {
                foreach (var field in structures[structureIndex].Fields)
                {
                    if (BoundEnumTypes.IsType(field.Type))
                    {
                        continue;
                    }

                    var fieldTypeValue = (int)field.Type;
                    if (fieldTypeValue < 100
                        || fieldTypeValue >= ManagedClassTypes.First)
                    {
                        continue;
                    }

                    var nestedIndex = fieldTypeValue - 100;
                    if ((uint)nestedIndex < (uint)structures.Length
                        && selectedStructures.Add(nestedIndex))
                    {
                        pendingStructures.Push(nestedIndex);
                    }
                }
            }

            var structureBuilder = structures.ToBuilder();
            foreach (var structureIndex in selectedStructures)
            {
                var structure = structureBuilder[structureIndex];
                var fields = structure.Fields.ToBuilder();
                for (var fieldIndex = 0; fieldIndex < fields.Count;
                    fieldIndex++)
                {
                    var field = fields[fieldIndex];
                    var origin = field.StandardFieldOrigin;
                    if (origin is null)
                    {
                        continue;
                    }

                    var selectedKeys = selectedFieldExportKeys.Where(
                            origin.ExportKeys.Contains)
                        .ToImmutableArray();
                    if (selectedKeys.IsEmpty)
                    {
                        continue;
                    }
                    if (field.IsPrivate || field.IsInternal)
                    {
                        throw Invalid(
                            "An EN1 selected FieldDef ExportKey does not identify a public field.");
                    }

                    fields[fieldIndex] = field with {
                        StandardFieldOrigin = origin with {
                            SelectedExportKeys = selectedKeys,
                        },
                    };
                }

                structureBuilder[structureIndex] = structure with {
                    Fields = fields.ToImmutable(),
                };
            }

            structures = structureBuilder.ToImmutable();
        }

        bool TryDecodeSelectedClassFieldType(uint typeSpecReference,
            out PrimitiveType fieldType, int recursionDepth = 0)
        {
            if (recursionDepth > SemanticVerifier.MaximumDepth)
            {
                throw Unsupported(
                    "The bounded EN1 class layout TypeSpec closure exceeds its nesting limit.");
            }

            fieldType = PrimitiveType.Void;
            var type = rootTables.TypeSpecs.GetTypeSpecRef(
                typeSpecReference);
            var typeSpecForm = type.Form;
            var qualifierSource = type;
            var depth = 0;
            while (type.Form == 0x06 && type.Qualifiers == 0)
            {
                if (++depth > SemanticVerifier.MaximumDepth)
                {
                    throw Unsupported(
                        "An EN1 class query FieldDef TypeSpec exceeds the nesting limit.");
                }

                var row = rootTables.TypeSpecs.GetRow(type.Row);
                var innerTypeSpec = BinaryPrimitives
                    .ReadUInt32LittleEndian(row[2..]);
                type = rootTables.TypeSpecs.GetTypeSpecRef(innerTypeSpec);
                qualifierSource = type;
            }

            if (typeSpecForm == 0xF2 && type.Form == 0xF2
                && type.Qualifiers == 0
                && TrRmConstantSourceValueValidator
                    .TryGetBoundScalarType(type.TypeCode,
                        out var scalarType)
                && scalarType != PrimitiveType.Void)
            {
                fieldType = scalarType;
                return true;
            }

            if (typeSpecForm == type.Form && type.Form == 0x01
                && type.TypeCode == 0x8C && type.ArgumentCode == 0
                && type.Qualifiers == 0)
            {
                if (rootTables.Modules.GetRequirements(rootModuleRow)
                    .TargetRequirement
                    != TrRmModuleTableReader
                        .WindowsX64NativeTargetRequirement)
                {
                    throw Unsupported(
                        "A native pointer field requires the Windows x64 target.");
                }

                fieldType = PrimitiveType.NativeRawPointer;
                return true;
            }

            if (typeSpecForm == type.Form
                && type.Form is 0x01 or 0x0C
                && type.TypeCode == 0x8C
                && (type.Qualifiers & ~0x01) == 0
                && rootTables.TypeSpecs.TryGetNativePointerPointeeTypeSpecRef(
                    typeSpecReference, out var pointeeTypeSpecReference))
            {
                var pointee = rootTables.TypeSpecs.GetTypeSpecRef(
                    pointeeTypeSpecReference);
                PrimitiveType pointeeType;
                if (pointee.Form == 0xF2 && pointee.Qualifiers == 0
                    && TrRmConstantSourceValueValidator
                        .TryGetBoundScalarType(pointee.TypeCode,
                            out var scalarPointeeType))
                {
                    pointeeType = scalarPointeeType;
                }
                else if (TryDecodeSelectedClassFieldType(
                        pointeeTypeSpecReference, out var nominalPointeeType,
                        recursionDepth + 1)
                    && IsC1EnumClosurePointerPointeeType(
                        nominalPointeeType,
                        allowClassHandlePointee: isEN1,
                        nominalTypeInstances:
                            genericNominalTypeInstances,
                        nominalTypeDefinitions:
                            genericNominalTypeDefinitions))
                {
                    pointeeType = nominalPointeeType;
                }
                else
                {
                    throw Unsupported(
                        "The bounded EN1 class layout TypeSpec closure supports scalar, enum, non-generic struct or class-handle pointer pointees.");
                }

                if (rootTables.Modules.GetRequirements(rootModuleRow)
                    .TargetRequirement
                    != TrRmModuleTableReader
                        .WindowsX64NativeTargetRequirement)
                {
                    throw Unsupported(
                        "A native pointer field requires the Windows x64 target.");
                }

                var pointer = new NativePointerTypeDefinition(pointeeType,
                    (type.Qualifiers & 1) != 0);
                if (!nativePointerTypeIndexes.TryGetValue(pointer,
                        out var pointerIndex))
                {
                    pointerIndex = nativePointerTypes.Count;
                    nativePointerTypes.Add(pointer);
                    nativePointerTypeIndexes.Add(pointer, pointerIndex);
                }

                fieldType = NativePointerTypes.Type(pointerIndex);
                return true;
            }

            var typeRow = checked((int)type.Row);
            var classTypeIdentity
                = default(BoundGenericNominalTypeIdentity);
            var hasClassIdentity = type.Form switch {
                0x02 => classIdentityByRootTypeDefRow.TryGetValue(
                    typeRow, out classTypeIdentity),
                0x03 => classIdentityByRootImportRow.TryGetValue(
                    typeRow, out classTypeIdentity),
                _ => false,
            };
            if (hasClassIdentity)
            {
                var definition = genericNominalTypeDefinitions
                    .FirstOrDefault(candidate =>
                        candidate.Identity == classTypeIdentity);
                if (definition is null || definition.NominalKind != 2
                    || !definition.Formals.IsEmpty)
                {
                    throw Invalid(
                        "An EN1 class query field has no complete nongeneric class identity.");
                }

                var key = (classTypeIdentity, rootSnapshot.ModuleIdentity,
                    typeSpecReference);
                if (!classTypeInstanceIndexes.TryGetValue(key,
                        out var instanceIndex))
                {
                    if (genericNominalTypeInstances.Count
                        >= BoundGenericNominalTypeTypes.MaximumCount)
                    {
                        throw Unsupported(
                            "The EN1 class query type instance limit was exceeded.");
                    }

                    instanceIndex = genericNominalTypeInstances.Count;
                    genericNominalTypeInstances.Add(
                        new BoundGenericNominalTypeInstance(
                            classTypeIdentity, [],
                            qualifierSource.Qualifiers,
                            typeSpecReference, rootSnapshot.ModuleIdentity,
                            typeSpecForm));
                    classTypeInstanceIndexes.Add(key, instanceIndex);
                }

                fieldType = BoundGenericNominalTypeTypes.Type(
                    instanceIndex);
                return true;
            }

            if (typeSpecForm != type.Form || type.Qualifiers != 0)
            {
                return false;
            }

            if (type.Form == 0x02
                && structureIndexByRootTypeDefRow.TryGetValue(typeRow,
                    out var structureIndex))
            {
                fieldType = (PrimitiveType)(100 + structureIndex);
                return true;
            }
            if (type.Form == 0x03
                && structureIndexByImportRow.TryGetValue(typeRow,
                    out var importedStructureIndex))
            {
                fieldType = (PrimitiveType)(100
                    + importedStructureIndex);
                return true;
            }
            if (type.Form == 0x02
                && enumIndexByTypeDefRow.TryGetValue(typeRow,
                    out var enumIndex))
            {
                fieldType = BoundEnumTypes.Type(enumIndex);
                return true;
            }
            if (type.Form == 0x03
                && enumIndexByImportRow.TryGetValue(typeRow,
                    out var importedEnumIndex))
            {
                fieldType = BoundEnumTypes.Type(importedEnumIndex);
                return true;
            }

            return false;
        }

        if (selectedFieldExportKeys.Length != 0)
        {
            var declarations = standardClassDeclarations.ToBuilder();
            for (var declarationIndex = 0;
                declarationIndex < declarations.Count;
                declarationIndex++)
            {
                var declaration = declarations[declarationIndex];
                if (declaration.SelectedExportKeys.IsEmpty
                    || !BoundStandardEnumDeclarationContract.SnapshotsMatch(
                        declaration.Declaration.Provider, rootSnapshot))
                {
                    continue;
                }

                var fields = declaration.Declaration.Fields.ToBuilder();
                for (var fieldIndex = 0; fieldIndex < fields.Count;
                    fieldIndex++)
                {
                    var field = fields[fieldIndex];
                    if (field.SelectedExportKeys.IsEmpty
                        && !resolveAllClassFieldTypes)
                    {
                        continue;
                    }
                    if (!TryDecodeSelectedClassFieldType(
                            field.TypeSpecReference, out var fieldType))
                    {
                        if (resolveAllClassFieldTypes
                            && !field.SelectedExportKeys.IsEmpty)
                        {
                            throw Unsupported(
                                "An EN1 class query FieldDef does not have a supported selected type.");
                        }

                        continue;
                    }

                    fields[fieldIndex] = field with { Type = fieldType };
                }

                declarations[declarationIndex] = declaration with {
                    Declaration = declaration.Declaration with {
                        Fields = fields.ToImmutable(),
                    },
                };
            }

            standardClassDeclarations = declarations.ToImmutable();
        }

        var program = new BoundProgram([], -1) {
            RuntimePolicy = (BoundRuntimePolicy)rootTables.Modules
                .GetRequirements(rootModuleRow).RuntimePolicy,
            Structures = structures,
            EnumDefinitions = enumDefinitions,
            NativePointerTypes = nativePointerTypes.ToImmutable(),
            NativeFunctionPointerSignatures
                = nativeFunctionPointerSignatures.ToImmutable(),
            GenericNominalTypeDefinitions =
                genericNominalTypeDefinitions,
            GenericNominalTypeInstances =
                genericNominalTypeInstances.ToImmutable(),
            StandardClassDeclarations = standardClassDeclarations,
        };
        if (!structures.IsEmpty || !nativePointerTypes.IsEmpty
            || !nativeFunctionPointerSignatures.IsEmpty)
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
            enumDefinitions: enumDefinitions,
            nativeFunctionPointerSignatures:
                nativeFunctionPointerSignatures.ToImmutable(),
            nativePointerTypes: nativePointerTypes.ToImmutable(),
            genericNominalTypeDefinitions:
                genericNominalTypeDefinitions,
            genericNominalTypeInstances:
                genericNominalTypeInstances.ToImmutable(),
            standardClassDeclarations: standardClassDeclarations);
    }

    private static bool IsC1EnumClosureStructType(PrimitiveType type)
        => !BoundEnumTypes.IsType(type)
            && (int)type >= 100
            && (int)type < ManagedClassTypes.First;

    private static bool IsC1EnumClosurePointerPointeeType(
        PrimitiveType type, ImmutableArray<BoundClass> classes = default,
        bool allowClassHandlePointee = false,
        IReadOnlyList<BoundGenericNominalTypeInstance>?
            nominalTypeInstances = null,
        IReadOnlyList<BoundGenericNominalTypeDefinition>?
            nominalTypeDefinitions = null)
    {
        if (IsC1EnumClosureStructType(type) || BoundEnumTypes.IsType(type))
        {
            return true;
        }

        var typeCode = (int)type;
        if (typeCode >= ManagedClassTypes.First
            && typeCode < ManagedClassTypes.OptionalWriteFirst + 256)
        {
            var classIndex = ManagedClassTypes.ClassIndex(type);
            return allowClassHandlePointee && !classes.IsDefault
                && (uint)classIndex < (uint)classes.Length;
        }

        if (!allowClassHandlePointee
            || nominalTypeInstances is null
            || nominalTypeDefinitions is null
            || !BoundGenericNominalTypeContract.IsNominalClassType(type,
                nominalTypeInstances, nominalTypeDefinitions))
        {
            return false;
        }

        return nominalTypeInstances[
            BoundGenericNominalTypeTypes.Index(type)].Arguments.IsEmpty;
    }

    private static bool IsSupportedEN1FieldFunctionPointer(
        NativeFunctionPointerSignatureDefinition signature)
        => NativeFunctionPointerContract.IsValidSignature(signature,
                ImmutableArray.Create(signature))
            && signature.CallingConvention
                != NativeCallingConvention.Native
            && NativeFunctionPointerContract
                .IsSupportedWindowsX64Convention(
                    signature.CallingConvention)
            && signature.EffectsMask == 0
            && signature.Selectors.IsEmpty
            && signature.OutputStates.IsEmpty
            && signature.Dependencies.IsEmpty
            && signature.NativeParameters.IsDefaultOrEmpty
            && (signature.ReturnType == PrimitiveType.Void
                || PrimitiveTypeFacts.IsBuiltInScalar(signature.ReturnType))
            && signature.ParameterTypes.All(
                PrimitiveTypeFacts.IsBuiltInScalar);

    private static NativeFunctionPointerSignatureDefinition
        DecodeEN1FieldFunctionPointer(
            TrRmStructuralMetadataValidator.ValidatedExecutionTables tables,
            int typeSpecRow, int moduleRow)
    {
        var anchorMethodRow = tables.NativeFunctionPointerAnchors
            .GetAnchorMethodDefRow(typeSpecRow);
        if (tables.Methods.GetContractRange(anchorMethodRow).Count != 0)
        {
            throw Unsupported(
                "The bounded EN1 function-pointer field route supports scalar signatures without extra method contracts.");
        }

        var signatureRow = tables.Methods.GetSignature(anchorMethodRow);
        var returnTypeSpecRef = tables.Signatures
            .GetResultTypeSpec(signatureRow);
        var returnType = DecodeScalar(returnTypeSpecRef,
            allowVoid: true);
        var (parameterFirst, parameterCount)
            = tables.Signatures.GetParamRange(signatureRow);
        var parameterTypes
            = ImmutableArray.CreateBuilder<PrimitiveType>(parameterCount);
        for (var parameter = 0; parameter < parameterCount; parameter++)
        {
            parameterTypes.Add(DecodeScalar(
                tables.Parameters.GetTypeSpec(parameterFirst + parameter),
                allowVoid: false));
        }

        var signature = new NativeFunctionPointerSignatureDefinition(
            (NativeCallingConvention)tables.NativeFunctionPointerAnchors
                .GetCallingConvention(typeSpecRow),
            returnType, parameterTypes.MoveToImmutable());
        if (tables.Modules.GetRequirements(moduleRow).TargetRequirement
            != TrRmModuleTableReader.WindowsX64NativeTargetRequirement
            || !IsSupportedEN1FieldFunctionPointer(signature))
        {
            throw Unsupported(
                "The bounded EN1 function-pointer field route requires the selected Windows x64 target and a covered scalar signature.");
        }

        return signature;

        PrimitiveType DecodeScalar(uint typeSpecRef, bool allowVoid)
        {
            var type = tables.TypeSpecs.GetTypeSpecRef(typeSpecRef);
            if (type.Form != 0xF2 || type.Qualifiers != 0
                || !TrRmConstantSourceValueValidator
                    .TryGetBoundScalarType(type.TypeCode, out var scalarType)
                || (scalarType == PrimitiveType.Void && !allowVoid))
            {
                throw Unsupported(
                    "The bounded EN1 function-pointer field route supports only scalar signature TypeSpecs.");
            }

            if (scalarType is (PrimitiveType.NativeInt
                    or PrimitiveType.NativeUInt)
                && tables.Modules.GetRequirements(moduleRow)
                    .TargetRequirement
                    != TrRmModuleTableReader
                        .WindowsX64NativeTargetRequirement)
            {
                throw Unsupported(
                    "A native-width function-pointer signature requires the Windows x64 target.");
            }

            return scalarType;
        }
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
        ImmutableArray<NativePointerTypeDefinition>.Builder
            nativePointerTypes,
        ImmutableArray<NativeFunctionPointerSignatureDefinition>.Builder
            nativeFunctionPointerSignatures,
        Dictionary<NativePointerTypeDefinition, int>
            nativePointerTypeIndexes,
        ImmutableArray<TrRmStandardTypeProviderBinding> typeProviderBindings,
        ImmutableArray<TrRmStandardFieldProviderBinding>
            fieldProviderBindings,
        Func<int,
            TrRmStructuralMetadataValidator.ValidatedExecutionTables>
            getTables,
        Func<int, int, BoundSourceTypeProviderSnapshot> getSnapshot,
        Func<int, int, int, ImmutableArray<string>> getExportKeys,
        ImmutableArray<BoundGenericNominalTypeDefinition>
            classTypeDefinitions,
        ImmutableArray<BoundGenericNominalTypeInstance>.Builder
            classTypeInstances,
        Dictionary<(BoundGenericNominalTypeIdentity Identity,
            string TypeSpecProviderIdentity, uint TypeSpecReference), int>
            classTypeInstanceIndexes,
        Dictionary<int, BoundGenericNominalTypeIdentity>
            classIdentityByRootTypeDefRow,
        Dictionary<int, BoundGenericNominalTypeIdentity>
            classIdentityByRootImportRow)
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

        bool TryGetClassFieldType(uint typeSpecReference,
            out PrimitiveType fieldType)
        {
            fieldType = PrimitiveType.Void;
            var type = tables.TypeSpecs.GetTypeSpecRef(typeSpecReference);
            var typeSpecForm = type.Form;
            var qualifierSource = type;
            var depth = 0;
            while (type.Form == 0x06 && type.Qualifiers == 0)
            {
                if (++depth > SemanticVerifier.MaximumDepth)
                {
                    throw Unsupported(
                        "The EN1 class-handle TypeSpec exceeds the nesting limit.");
                }

                var row = tables.TypeSpecs.GetRow(type.Row);
                var innerTypeSpec = BinaryPrimitives
                    .ReadUInt32LittleEndian(row[2..]);
                type = tables.TypeSpecs.GetTypeSpecRef(innerTypeSpec);
                qualifierSource = type;
            }

            var classIdentity = default(
                BoundGenericNominalTypeIdentity);
            var typeRow = checked((int)type.Row);
            var hasClassIdentity = type.Form switch {
                0x02 => classIdentityByRootTypeDefRow.TryGetValue(
                    typeRow, out classIdentity),
                0x03 => classIdentityByRootImportRow.TryGetValue(
                    typeRow, out classIdentity),
                _ => false,
            };
            if (!hasClassIdentity)
            {
                return false;
            }

            var definition = classTypeDefinitions.SingleOrDefault(
                candidate => candidate.Identity == classIdentity);
            if (definition is null || definition.NominalKind != 2
                || !definition.Formals.IsEmpty)
            {
                throw Invalid(
                    "An EN1 class FieldDef TypeSpec has no complete non-generic class declaration.");
            }

            var key = (classIdentity, provider.ModuleIdentity,
                typeSpecReference);
            if (!classTypeInstanceIndexes.TryGetValue(key,
                    out var instanceIndex))
            {
                if (classTypeInstances.Count
                    >= BoundGenericNominalTypeTypes.MaximumCount)
                {
                    throw Unsupported(
                        "The EN1 class-handle instance limit was exceeded.");
                }

                instanceIndex = classTypeInstances.Count;
                classTypeInstances.Add(new BoundGenericNominalTypeInstance(
                    classIdentity, [],
                    qualifierSource.Qualifiers,
                    typeSpecReference, provider.ModuleIdentity,
                    typeSpecForm));
                classTypeInstanceIndexes.Add(key, instanceIndex);
            }

            fieldType = BoundGenericNominalTypeTypes.Type(instanceIndex);
            return true;
        }

        int GetOrAddNativeFunctionPointerSignature(
            NativeFunctionPointerSignatureDefinition signature)
        {
            for (var index = 0;
                index < nativeFunctionPointerSignatures.Count; index++)
            {
                if (NativeFunctionPointerContract.AreEquivalent(
                        nativeFunctionPointerSignatures[index], signature))
                {
                    return index;
                }
            }

            if (nativeFunctionPointerSignatures.Count
                >= SemanticVerifier.MaximumAnalysisVisits)
            {
                throw Unsupported(
                    "The EN1 native function-pointer signature limit was exceeded.");
            }

            nativeFunctionPointerSignatures.Add(signature);
            return nativeFunctionPointerSignatures.Count - 1;
        }

        PrimitiveType DecodeFieldType(uint typeSpecReference,
            int depth = 0)
        {
            if (depth > SemanticVerifier.MaximumDepth)
            {
                throw Unsupported(
                    "The bounded EN1 struct TypeSpec closure exceeds its nesting limit.");
            }

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

            if (type.Form == 0x01 && type.TypeCode == 0x8C
                && type.ArgumentCode == 0 && type.Qualifiers == 0)
            {
                if (tables.Modules.GetRequirements(moduleRow)
                    .TargetRequirement
                    != TrRmModuleTableReader
                        .WindowsX64NativeTargetRequirement)
                {
                    throw Unsupported(
                        "A native pointer field requires the Windows x64 target.");
                }

                return PrimitiveType.NativeRawPointer;
            }

            if (type.Form == 0x0D && type.IsRow)
            {
                var signature = DecodeEN1FieldFunctionPointer(tables,
                    type.Row, moduleRow);
                return NativeFunctionPointerTypes.Type(
                    GetOrAddNativeFunctionPointerSignature(signature));
            }

            if (type.Form is 0x01 or 0x0C
                && type.TypeCode == 0x8C
                && (type.Qualifiers & ~0x01) == 0
                && tables.TypeSpecs.TryGetNativePointerPointeeTypeSpecRef(
                    typeSpecReference, out var pointeeTypeSpecReference))
            {
                var pointee = tables.TypeSpecs.GetTypeSpecRef(
                    pointeeTypeSpecReference);
                PrimitiveType pointeeType;
                if (pointee.Form == 0xF2 && pointee.Qualifiers == 0
                    && TrRmConstantSourceValueValidator
                        .TryGetBoundScalarType(pointee.TypeCode,
                            out var scalarPointeeType))
                {
                    pointeeType = scalarPointeeType;
                }
                else if (pointee.Form is 0x02 or 0x03
                    || pointee.Form == 0x06 && pointee.Qualifiers == 0)
                {
                    pointeeType = DecodeFieldType(
                        pointeeTypeSpecReference, depth + 1);
                    if (!IsC1EnumClosurePointerPointeeType(pointeeType,
                            allowClassHandlePointee: true,
                            nominalTypeInstances: classTypeInstances,
                            nominalTypeDefinitions:
                                classTypeDefinitions))
                    {
                        throw Unsupported(
                            "The bounded EN1 struct reference supports scalar, enum, non-generic struct or class-handle pointer pointees.");
                    }
                }
                else
                {
                    throw Unsupported(
                        "The bounded EN1 struct reference supports scalar, enum, non-generic struct or class-handle pointer pointees.");
                }

                if (tables.Modules.GetRequirements(moduleRow)
                    .TargetRequirement
                    != TrRmModuleTableReader
                        .WindowsX64NativeTargetRequirement)
                {
                    throw Unsupported(
                        "A native pointer field requires the Windows x64 target.");
                }

                var pointer = new NativePointerTypeDefinition(pointeeType,
                    (type.Qualifiers & 1) != 0);
                if (!nativePointerTypeIndexes.TryGetValue(pointer,
                        out var pointerIndex))
                {
                    pointerIndex = nativePointerTypes.Count;
                    nativePointerTypes.Add(pointer);
                    nativePointerTypeIndexes.Add(pointer, pointerIndex);
                }

                return NativePointerTypes.Type(pointerIndex);
            }

            if (TryGetClassFieldType(typeSpecReference,
                    out var classFieldType))
            {
                return classFieldType;
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
                DeclaredCopyEvidence
                    = tables.Schema.SupportsDeclaredCopyEvidence()
                        ? tables.TypeDefs.HasDeclaredCopy(typeDefRow)
                        : null,
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

                var isStandardClassHandle
                    = BoundGenericNominalTypeContract.IsNominalClassType(
                        field.Type, classTypeInstances,
                        classTypeDefinitions);
                var width = isStandardClassHandle ? 8UL
                    : ValueLayout.Size(field.Type, structures,
                        enumDefinitions: enumDefinitions);
                var fieldAlignment = isStandardClassHandle ? 8
                    : ValueLayout.Alignment(field.Type, structures,
                        enumDefinitions: enumDefinitions);
                var fieldRequiredAlignment = isStandardClassHandle ? 8
                    : ValueLayout.RequiredAlignment(field.Type, structures,
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
                new TrRmStandardFieldProviderValidation([], [], []),
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
