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
    /// <summary>Creates a source reference from explicitly selected A2 TypeDef exports.</summary>
    /// <param name="input">The complete provider artifact and its exact schema selection.</param>
    /// <param name="moduleIdentity">The exact identity of the Module to expose.</param>
    /// <param name="providers">The complete explicitly supplied provider artifact set.</param>
    /// <param name="typeExportKeys">The exact ExportKeys that select source type roots.</param>
    /// <returns>The selected TypeDefs and their checked nominal/array closure.</returns>
    /// <exception cref="ArgumentNullException">An input, list or selected key is null.</exception>
    /// <exception cref="ArgumentException">A selected key is empty, duplicated, missing or has the wrong kind.</exception>
    /// <exception cref="NotSupportedException">The selected schema or type closure is outside the bounded A2 projection.</exception>
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
        if (schemaIdentities[0] is not (
                StandardForeignInvocationStorageFixedArraySchemaIdentity
                or StandardForeignInvocationStorageFixedArrayEmptyInitializationSchemaIdentity
                or StandardForeignInvocationStorageFixedArrayEmptyUnitInitializationSchemaIdentity
                or StandardForeignInvocationStorageFixedArrayEmptyUnitScalarFrozenValuesSchemaIdentity
                or StandardForeignInvocationStorageFixedArrayCopyReturnCoverageSchemaIdentity
                or StandardForeignInvocationStorageFixedArrayCopyReturnCoverageRepeatInitializationSchemaIdentity
                or StandardLocalAggregateLayoutSchemaIdentity))
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
                    or StandardLocalAggregateLayoutSchemaIdentity)))
        {
            throw Unsupported(
                "The bounded A2 type-reference projection requires A2, A2E or A2EU provider artifacts.");
        }

        var program = LoadStandardProgram(artifacts, schemaIdentities,
            moduleIdentity, true, null, out _,
            out _,
            out var typeProviderBindings, out var moduleValidation, out _);
        return CreateA2SourceTypeReference(artifacts, schemaIdentities,
            moduleIdentity, exportKeys, program, moduleValidation,
            typeProviderBindings);
    }

    private static CompilationReference CreateA2SourceTypeReference(
        byte[][] artifacts, string[] schemaIdentities, string moduleIdentity,
        string[] exportKeys, BoundProgram program,
        StandardModuleValidation moduleValidation,
        ImmutableArray<TrRmStandardTypeProviderBinding> typeProviderBindings)
    {
        var artifact = artifacts[0];
        var schemaIdentity = schemaIdentities[0];
        var tables = ValidateForeignInvocationArtifact(
            CreateForeignInvocationIndex(artifact, schemaIdentity),
            schemaIdentity, null,
            createA3ResourceLedger: false);
        var moduleRow = moduleValidation.RootModuleRow;
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
                or TrRmArtifactSchema.LocalAggregateLayoutDraft)
            || moduleRow <= 0 || moduleRow > tables.Modules.Count
            || typeDefStart <= 0 || typeDefEnd - typeDefStart
                != program.Structures.Length
            || methodStart != methodEnd
            || tables.Modules.GetEntryMethod(moduleRow) != 0)
        {
            throw Unsupported(
                "The A2 type-reference projection requires one type-only root Module.");
        }

        var runtimePolicy
            = tables.Modules.GetRequirements(moduleRow).RuntimePolicy;
        var keysByTypeDefRow = new Dictionary<int, List<string>>();
        var rootTypeDefRows = new HashSet<int>();
        foreach (var exportKey in exportKeys)
        {
            if (!tables.Exports.TryGetDefinitionForKey(moduleRow,
                    exportKey, out var definition))
            {
                throw new ArgumentException(
                    "A selected TypeDef ExportKey is not published by the selected Module.",
                    nameof(exportKeys));
            }
            if (definition >> 24 != 0x13)
            {
                throw new ArgumentException(
                    "A selected ExportKey does not identify a TypeDef.",
                    nameof(exportKeys));
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
        var selectedStructures = new HashSet<int>();
        var selectedArrayTypes = new HashSet<int>();
        var selectedPointerTypes = new HashSet<int>();
        var visitedStructures = new HashSet<int>();
        var pendingTypes = new Stack<PrimitiveType>();
        foreach (var typeDefRow in rootTypeDefRows)
        {
            var structureIndex = typeDefRow - typeDefStart;
            _ = selectedStructures.Add(structureIndex);
            pendingTypes.Push((PrimitiveType)(100 + structureIndex));
        }

        var remaining = SemanticVerifier.MaximumAnalysisVisits;
        while (pendingTypes.TryPop(out var type))
        {
            if (--remaining < 0)
            {
                throw new NotSupportedException(
                    "The A2 type-reference closure exceeds the analysis limit.");
            }

            if (NativeFixedArrayTypes.IsType(type))
            {
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

        PrimitiveType RemapType(PrimitiveType type)
        {
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
        var projectedStructures = ImmutableArray.CreateBuilder<BoundStruct>(
            structureOrder.Length);
        foreach (var structureIndex in structureOrder)
        {
            var typeDefRow = typeDefStart + structureIndex;
            var original = program.Structures[structureIndex];
            var fields = ImmutableArray.CreateBuilder<BoundStructField>(
                original.Fields.Length);
            foreach (var field in original.Fields)
            {
                fields.Add(field with { Type = RemapType(field.Type) });
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
            var expectedDeclaration = original with {
                Fields = projectedFields,
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
        if (!NativePointerTypeContract.IsValidTable(pointerTypes,
                structures, [], fixedArrays))
        {
            throw Invalid("The projected A2 native pointer table is invalid.");
        }

        var projectedProgram = new BoundProgram([], -1)
        {
            RuntimePolicy = (BoundRuntimePolicy)runtimePolicy,
            Structures = structures,
            NativePointerTypes = pointerTypes,
            NativeFixedArrayTypes = fixedArrays,
        };
        if (!NativeFixedArrayContract.MatchesProgram(projectedProgram))
        {
            throw Invalid("The projected A2 fixed-array type table is invalid.");
        }
        SemanticVerifier.VerifyLibraryWithDirectNativeImports(projectedProgram);
        return new CompilationReference(structures, [], (BoundRuntimePolicy)runtimePolicy,
            nativePointerTypes: pointerTypes,
            nativeFixedArrayTypes: fixedArrays);
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
        ImmutableArray<TrRmStandardTypeProviderBinding> typeProviderBindings)
    {
        if (!(StringComparer.Ordinal.Equals(schemaIdentities[0],
                StandardForeignInvocationStorageFixedArraySchemaIdentity)
            || StringComparer.Ordinal.Equals(schemaIdentities[0],
                StandardForeignInvocationStorageFixedArrayEmptyInitializationSchemaIdentity)
            || StringComparer.Ordinal.Equals(schemaIdentities[0],
                StandardForeignInvocationStorageFixedArrayEmptyUnitInitializationSchemaIdentity)
            || StringComparer.Ordinal.Equals(schemaIdentities[0],
                StandardForeignInvocationStorageFixedArrayEmptyUnitScalarFrozenValuesSchemaIdentity)
            || StringComparer.Ordinal.Equals(schemaIdentities[0],
                StandardForeignInvocationStorageFixedArrayCopyReturnCoverageSchemaIdentity)
            || StringComparer.Ordinal.Equals(schemaIdentities[0],
                StandardLocalAggregateLayoutSchemaIdentity))
            || tables.Schema is not (
                TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyDraft
                or TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyEmptyInitializationDraft
                or TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyEmptyUnitInitializationDraft
                or TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyEmptyUnitScalarFrozenValuesDraft
                or TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyReturnCoverageDraft
                or TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyReturnCoverageRepeatInitializationDraft
                or TrRmArtifactSchema.LocalAggregateLayoutDraft)
            || program.EntryPoint != -1 || !program.Functions.IsEmpty
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
            artifacts[0], schemaIdentities[0], moduleRow);
        var snapshotsByModule = new Dictionary<(int ArtifactIndex, int ModuleRow),
            BoundSourceTypeProviderSnapshot>
        {
            [(0, moduleRow)] = rootSnapshot,
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
                    or StandardLocalAggregateLayoutSchemaIdentity))
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
