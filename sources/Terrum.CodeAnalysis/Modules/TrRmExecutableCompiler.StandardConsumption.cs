// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;

namespace Terrum.CodeAnalysis.Modules;

/// <summary>One complete TRRM artifact paired with its exact out-of-band schema identity.</summary>
/// <param name="SchemaIdentity">The case-sensitive schema identity selected for the artifact.</param>
/// <param name="Artifact">The complete artifact bytes.</param>
public sealed record TrRmStandardArtifactInput(
    string SchemaIdentity, ReadOnlyMemory<byte> Artifact);

public static partial class TrRmExecutableCompiler
{
    /// <summary>Loads one explicitly selected Standard Module and its supplied provider artifacts.</summary>
    /// <param name="input">The complete root artifact and its exact schema selection.</param>
    /// <param name="moduleIdentity">The exact identity of the root Module to load.</param>
    /// <param name="providers">The complete explicitly supplied provider artifact set.</param>
    /// <returns>The checked root program.</returns>
    /// <exception cref="ArgumentNullException">An input, provider list or artifact is null.</exception>
    /// <exception cref="ArgumentException">A selection or Module identity is empty or unrecognized.</exception>
    /// <exception cref="NotSupportedException">The selected schema is not supported by this operation.</exception>
    /// <exception cref="InvalidDataException">An artifact or selected Module is invalid.</exception>
    public static BoundProgram LoadStandard(TrRmStandardArtifactInput input,
        string moduleIdentity, IReadOnlyList<TrRmStandardArtifactInput> providers)
    {
        ArgumentException.ThrowIfNullOrEmpty(moduleIdentity);
        var artifacts = SnapshotStandardInputs(input, providers);
        return LoadStandardProgram(artifacts, moduleIdentity,
            verifySemantics: true, out _, out _);
    }

    internal static TrRmStandardNativeBindingPlan
        LoadStandardNativeBindingPlan(TrRmStandardArtifactInput input,
            string moduleIdentity,
            IReadOnlyList<TrRmStandardArtifactInput> providers,
            TrRmStandardNativeOperation operation,
            NativeRuntimePolicy runtimePolicy)
    {
        ArgumentException.ThrowIfNullOrEmpty(moduleIdentity);
        if (runtimePolicy != NativeRuntimePolicy.FailFast)
        {
            throw Unsupported(
                "The bounded F native binding plan supports fail-fast only.");
        }

        var artifacts = SnapshotStandardInputs(input, providers);
        var schemaIdentities = ImmutableArray.CreateBuilder<string>(
            artifacts.Length);
        schemaIdentities.Add(input.SchemaIdentity);
        for (var index = 0; index < providers.Count; index++)
        {
            schemaIdentities.Add(providers[index].SchemaIdentity);
        }

        var program = LoadStandardProgram(artifacts, moduleIdentity,
            verifySemantics: true, out var providerBinding,
            out var moduleValidation);
        return new TrRmStandardNativeBindingPlan(program, moduleIdentity,
            artifacts, schemaIdentities.MoveToImmutable(),
            moduleValidation.Modules, moduleValidation.RootModuleRow,
            operation, runtimePolicy, providerBinding);
    }

    /// <summary>Creates a source reference from one explicitly selected Standard Module.</summary>
    /// <param name="input">The complete provider artifact and its exact schema selection.</param>
    /// <param name="moduleIdentity">The exact identity of the Module to expose.</param>
    /// <param name="providers">The complete explicitly supplied provider artifact set.</param>
    /// <returns>The selected Module's exported declarations.</returns>
    /// <exception cref="ArgumentNullException">An input, provider list or artifact is null.</exception>
    /// <exception cref="ArgumentException">A selection or Module identity is empty or unrecognized.</exception>
    /// <exception cref="NotSupportedException">The selected schema is not supported by this operation.</exception>
    /// <exception cref="InvalidDataException">An artifact or selected Module is invalid.</exception>
    public static CompilationReference CreateStandardReference(
        TrRmStandardArtifactInput input, string moduleIdentity,
        IReadOnlyList<TrRmStandardArtifactInput> providers)
    {
        ArgumentException.ThrowIfNullOrEmpty(moduleIdentity);
        var artifacts = SnapshotStandardInputs(input, providers);
        var program = LoadStandardProgram(artifacts, moduleIdentity,
            verifySemantics: false, out _, out _);
        var index = TrRmArtifactIndex.ForForeignInvocationDraft(artifacts[0]);
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationDraftAndGetExecutionTables(index);
        var moduleRow = FindModuleRow(tables.Modules, moduleIdentity);
        var (targetRequirement, runtimePolicy)
            = tables.Modules.GetRequirements(moduleRow);
        var (methodStart, methodEnd)
            = tables.Modules.GetMethodDefRange(moduleRow);
        var functionByName = program.Functions.ToDictionary(
            static function => function.Name, StringComparer.Ordinal);
        var functions = ImmutableArray.CreateBuilder<BoundFunction>();
        for (var row = methodStart; row < methodEnd; row++)
        {
            if (tables.NativeFunctionPointerAnchors.IsAnchorMethodDef(row))
            {
                continue;
            }

            var functionName = tables.Methods.GetName(row);
            if (!functionByName.TryGetValue(functionName, out var function))
            {
                throw Invalid("The selected F Module has an unrepresented MethodDef.");
            }

            if (!tables.Exports.TryGetExportKeyForDefinition(moduleRow,
                    0x1600_0000u | (uint)row, out var exportKey))
            {
                continue;
            }

            functions.Add(function with
            {
                Name = exportKey,
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

        if (functions.Count == 0)
        {
            throw Unsupported("The selected F Module has no supported exported declarations.");
        }

        return new CompilationReference([], functions.ToImmutable(),
            (BoundRuntimePolicy)runtimePolicy,
            nativeFunctionPointerSignatures: program.NativeFunctionPointerSignatures,
            nativePointerTypes: program.NativePointerTypes);
    }

    private static BoundProgram LoadStandardProgram(byte[][] artifacts,
        string moduleIdentity, bool verifySemantics,
        out TrRmStandardNativeProviderBinding? providerBinding,
        out StandardModuleValidation moduleValidation)
    {
        providerBinding = null;
        moduleValidation = ValidateStandardInputModules(artifacts,
            moduleIdentity);
        var rootModuleRow = moduleValidation.RootModuleRow;
        var index = TrRmArtifactIndex.ForForeignInvocationDraft(artifacts[0]);
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationDraftAndGetExecutionTables(index);
        if (tables.Imports.Count == 0 && tables.ModuleRefs.Count == 0)
        {
            return LoadForeignInvocationDraftNativeAddressLibrary(
                artifacts[0], rootModuleRow, null, verifySemantics);
        }

        if (tables.Imports.Count != 1 || tables.ModuleRefs.Count != 1)
        {
            throw Unsupported(
                "The bounded F consumer supports one imported NativeImport target.");
        }

        var expectedDefinition = tables.Imports
            .GetExpectedDefinitionForImportRow(1);
        if (expectedDefinition >> 24 != 0x16)
        {
            throw Unsupported("The bounded F consumer imports a MethodDef target.");
        }

        var targetMethodRow = checked((int)(expectedDefinition & 0x00FF_FFFF));
        if (!tables.StandardNativeImports.HasImportForMethod(targetMethodRow)
            || !tables.Imports.TryGetExpectedBinding(expectedDefinition,
                out var expectedBinding))
        {
            throw Invalid("The NativeAddress target has no retained NativeImport expectation.");
        }

        var (moduleRefStart, moduleRefEnd)
            = tables.Modules.GetModuleRefRange(rootModuleRow);
        if (moduleRefEnd - moduleRefStart != 1)
        {
            throw Unsupported(
                "The bounded F consumer requires one ModuleRef on the selected root Module.");
        }

        var moduleRefRow = tables.Imports.GetModuleRefForImportRow(1);
        if (moduleRefRow != moduleRefStart
            || tables.ModuleRefs.GetOwnerModule(moduleRefRow) != rootModuleRow)
        {
            throw Invalid("The retained Import is not owned by the selected root Module.");
        }
        var moduleRefIdentity
            = tables.ModuleRefs.GetProducerIdentity(moduleRefRow);
        var (moduleRefTargetRequirement, moduleRefRuntimePolicy)
            = tables.ModuleRefs.GetRequirements(moduleRefRow);
        if (!StringComparer.Ordinal.Equals(moduleRefIdentity,
                expectedBinding.ProducerIdentity)
            || moduleRefTargetRequirement != expectedBinding.TargetRequirement
            || moduleRefRuntimePolicy != expectedBinding.RuntimePolicy)
        {
            throw Invalid("The retained ModuleRef and Import contexts differ.");
        }

        if (StringComparer.Ordinal.Equals(
                tables.Modules.GetIdentity(rootModuleRow),
                expectedBinding.ProducerIdentity))
        {
            throw Unsupported(
                "The bounded F consumer does not permit the selected root Module to satisfy its own Import.");
        }

        var (providerArtifactIndex, providerArtifact, providerModuleRow)
            = FindSelectedProviderModule(artifacts, moduleValidation.Modules,
                rootModuleRow, expectedBinding.ProducerIdentity);
        var (providerMethodRow, providerLibraryName, providerExportName)
            = ValidateNativeImportProvider(tables,
            targetMethodRow, expectedBinding, providerArtifact,
            providerModuleRow);
        var moduleImport = new BoundModuleImport(
            expectedBinding.ProducerIdentity, expectedBinding.ExportKey,
            expectedBinding.TargetRequirement, expectedBinding.RuntimePolicy);
        var program = LoadForeignInvocationDraftNativeAddressLibrary(
            artifacts[0], rootModuleRow, moduleImport, verifySemantics);
        providerBinding = new TrRmStandardNativeProviderBinding(
            tables.Methods.GetName(targetMethodRow),
            expectedBinding.ProducerIdentity, expectedBinding.ExportKey,
            expectedBinding.TargetRequirement, expectedBinding.RuntimePolicy,
            rootModuleRow, targetMethodRow, providerArtifactIndex,
            providerModuleRow, providerMethodRow,
            providerLibraryName, providerExportName);
        return program;
    }

    private static (int ArtifactIndex, byte[] Artifact, int ModuleRow)
        FindSelectedProviderModule(
        byte[][] artifacts,
        ImmutableArray<TrRmStandardModuleSelection> modules,
        int rootModuleRow,
        string providerIdentity)
    {
        foreach (var module in modules)
        {
            if (module.ArtifactIndex == 0
                && module.ModuleRow == rootModuleRow)
            {
                continue;
            }

            if (StringComparer.Ordinal.Equals(module.Identity,
                    providerIdentity))
            {
                return (module.ArtifactIndex,
                    artifacts[module.ArtifactIndex], module.ModuleRow);
            }
        }

        throw Invalid($"The selected provider Module '{providerIdentity}' was not supplied.");
    }

    private static (int MethodRow, string LibraryName, string ExportName)
        ValidateNativeImportProvider(
        TrRmStructuralMetadataValidator.ValidatedExecutionTables consumer,
        int expectedMethodRow, TrRmImportBinding expectedBinding,
        byte[] providerArtifact, int providerModuleRow)
    {
        var providerIndex = TrRmArtifactIndex
            .ForForeignInvocationDraft(providerArtifact);
        var provider = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationDraftAndGetExecutionTables(providerIndex);
        var (providerTargetRequirement, providerRuntimePolicy)
            = provider.Modules.GetRequirements(providerModuleRow);
        var (methodStart, methodEnd)
            = provider.Modules.GetMethodDefRange(providerModuleRow);
        if (!StringComparer.Ordinal.Equals(
                provider.Modules.GetIdentity(providerModuleRow),
                expectedBinding.ProducerIdentity)
            || providerTargetRequirement != expectedBinding.TargetRequirement
            || providerRuntimePolicy != expectedBinding.RuntimePolicy
            || methodEnd - methodStart != 3)
        {
            throw Invalid("The selected provider Module does not match the retained ModuleRef.");
        }

        var providerMethodRow = 0;
        for (var row = methodStart; row < methodEnd; row++)
        {
            if (provider.Exports.TryGetExportKeyForDefinition(providerModuleRow,
                    0x1600_0000u | (uint)row, out var exportKey)
                && StringComparer.Ordinal.Equals(exportKey,
                    expectedBinding.ExportKey))
            {
                providerMethodRow = row;
                break;
            }
        }

        if (providerMethodRow == 0
            || provider.Methods.GetOwnerType(providerMethodRow) != 0
            || provider.Methods.GetOwnerModule(providerMethodRow)
                != providerModuleRow
            || provider.Methods.GetFlags(providerMethodRow) != 2
            || provider.Methods.GetBodyRow(providerMethodRow) != 0
            || !provider.StandardNativeImports.HasImportForMethod(providerMethodRow))
        {
            throw Invalid("The selected export is not a provider-owned NativeImport.");
        }

        _ = LoadForeignInvocationDraftNativeAddressLibrary(providerArtifact,
            providerModuleRow, null, verifySemantics: false);
        EnsureNativeAddressContracts(provider.Methods, provider.Contracts,
            providerMethodRow,
            provider.Contracts.GetNativeParameters(providerMethodRow).Length + 1,
            allowEffects: true);
        EnsureNativeAddressContracts(consumer.Methods, consumer.Contracts,
            expectedMethodRow,
            consumer.Contracts.GetNativeParameters(expectedMethodRow).Length + 1,
            allowEffects: true);
        var expectedSignatureRow = consumer.Methods.GetSignature(expectedMethodRow);
        var providerSignatureRow = provider.Methods.GetSignature(providerMethodRow);
        var (expectedParameterFirst, expectedParameterCount)
            = consumer.Signatures.GetParamRange(expectedSignatureRow);
        var (providerParameterFirst, providerParameterCount)
            = provider.Signatures.GetParamRange(providerSignatureRow);
        if (consumer.Signatures.GetReceiverTypeSpec(expectedSignatureRow) != 0
            || provider.Signatures.GetReceiverTypeSpec(providerSignatureRow) != 0
            || consumer.Signatures.GetGenericCount(expectedSignatureRow) != 0
            || provider.Signatures.GetGenericCount(providerSignatureRow) != 0
            || expectedParameterCount != providerParameterCount
            || !consumer.TypeSpecs.GetTypeSpecBytes(
                consumer.Signatures.GetResultTypeSpec(expectedSignatureRow))
                .SequenceEqual(provider.TypeSpecs.GetTypeSpecBytes(
                    provider.Signatures.GetResultTypeSpec(providerSignatureRow)))
            || consumer.Contracts.GetEffectsMask(expectedMethodRow) != 4
            || provider.Contracts.GetEffectsMask(providerMethodRow) != 4
            || !consumer.Contracts.GetNativeOutputs(expectedMethodRow).IsEmpty
            || !provider.Contracts.GetNativeOutputs(providerMethodRow).IsEmpty
            || !consumer.Contracts.GetNativeParameters(expectedMethodRow)
                .SequenceEqual(provider.Contracts.GetNativeParameters(providerMethodRow)))
        {
            throw Invalid("The selected provider does not satisfy the original NativeImport signature.");
        }

        for (var ordinal = 0; ordinal < expectedParameterCount; ordinal++)
        {
            var expectedParameterRow = expectedParameterFirst + ordinal;
            var providerParameterRow = providerParameterFirst + ordinal;
            if (consumer.Parameters.GetMode(expectedParameterRow)
                    != provider.Parameters.GetMode(providerParameterRow)
                || !consumer.TypeSpecs.GetTypeSpecBytes(
                    consumer.Parameters.GetTypeSpec(expectedParameterRow))
                    .SequenceEqual(provider.TypeSpecs.GetTypeSpecBytes(
                        provider.Parameters.GetTypeSpec(providerParameterRow))))
            {
                throw Invalid("The selected provider has an incompatible NativeImport formal.");
            }
        }

        var expectedImport
            = consumer.StandardNativeImports.GetImportForMethod(expectedMethodRow);
        var providerImport
            = provider.StandardNativeImports.GetImportForMethod(providerMethodRow);
        var providerLibraryName
            = provider.StandardNativeImports.GetLibraryName(providerImport);
        var providerExportName
            = provider.StandardNativeImports.GetEntryName(providerImport);
        if (expectedImport.OwnerKind != providerImport.OwnerKind
            || expectedImport.ResolutionMode != providerImport.ResolutionMode
            || expectedImport.ResolutionPolicy != providerImport.ResolutionPolicy
            || expectedImport.CallConv != providerImport.CallConv
            || !StringComparer.Ordinal.Equals(
                consumer.StandardNativeImports.GetLibraryName(expectedImport),
                providerLibraryName)
            || !StringComparer.Ordinal.Equals(
                consumer.StandardNativeImports.GetEntryName(expectedImport),
                providerExportName))
        {
            throw Invalid("The selected provider does not satisfy the original NativeImport descriptor.");
        }

        return (providerMethodRow, providerLibraryName, providerExportName);
    }

    private static byte[][] SnapshotStandardInputs(
        TrRmStandardArtifactInput input,
        IReadOnlyList<TrRmStandardArtifactInput> providers)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(providers);
        var artifacts = new byte[providers.Count + 1][];
        artifacts[0] = SnapshotStandardInput(input, nameof(input));
        for (var index = 0; index < providers.Count; index++)
        {
            var provider = providers[index]
                ?? throw new ArgumentNullException(
                    $"{nameof(providers)}[{index}]");
            artifacts[index + 1] = SnapshotStandardInput(provider,
                $"{nameof(providers)}[{index}]");
        }

        return artifacts;
    }

    private static byte[] SnapshotStandardInput(
        TrRmStandardArtifactInput input, string parameterName)
    {
        ArgumentException.ThrowIfNullOrEmpty(input.SchemaIdentity,
            parameterName);
        if (input.SchemaIdentity != StandardForeignInvocationSchemaIdentity)
        {
            throw new ArgumentException(
                "The exact Standard schema identity is not selected by this consumer.",
                parameterName);
        }

        return [.. input.Artifact.Span];
    }

    private readonly record struct StandardModuleValidation(
        int RootModuleRow,
        ImmutableArray<TrRmStandardModuleSelection> Modules);

    private static StandardModuleValidation ValidateStandardInputModules(
        byte[][] artifacts, string rootModuleIdentity)
    {
        var identities = new HashSet<string>(StringComparer.Ordinal);
        var rootModuleRow = 0;
        var moduleSelections
            = ImmutableArray.CreateBuilder<TrRmStandardModuleSelection>();
        for (var index = 0; index < artifacts.Length; index++)
        {
            var artifactIndex = TrRmArtifactIndex
                .ForForeignInvocationDraft(artifacts[index]);
            var tables = TrRmStructuralMetadataValidator
                .ValidateForeignInvocationDraftAndGetExecutionTables(artifactIndex);
            var populatedModuleCount = 0;
            for (var moduleRow = 1; moduleRow <= tables.Modules.Count;
                moduleRow++)
            {
                var identity = tables.Modules.GetIdentity(moduleRow);
                if (!identities.Add(identity))
                {
                    throw Invalid("A selected Module identity appears in multiple artifacts.");
                }

                if (index == 0
                    && StringComparer.Ordinal.Equals(identity,
                        rootModuleIdentity))
                {
                    rootModuleRow = moduleRow;
                }

                var (targetRequirement, runtimePolicy)
                    = tables.Modules.GetRequirements(moduleRow);
                if ((targetRequirement, runtimePolicy) != (1u, 0u))
                {
                    throw Unsupported(
                        "The bounded F consumer supports only Windows x64 fail-fast Modules.");
                }

                var (featureStart, featureCount)
                    = tables.Modules.GetFeatureRange(moduleRow);
                for (var offset = 0; offset < featureCount; offset++)
                {
                    var feature = tables.Features.GetFeature(
                        featureStart + offset);
                    if (feature.FeatureId
                        is not (TrRmFeatureTableReader.NativeExportEntriesFeatureId
                            or TrRmFeatureTableReader.ForeignInvocationContractsCandidateFeatureId))
                    {
                        throw Unsupported(
                            "The bounded F consumer does not support a required Module feature.");
                    }
                }

                var (methodStart, methodEnd)
                    = tables.Modules.GetMethodDefRange(moduleRow);
                moduleSelections.Add(new TrRmStandardModuleSelection(index,
                    moduleRow, identity, targetRequirement, runtimePolicy,
                    tables.Modules.GetEntryMethod(moduleRow), methodStart,
                    methodEnd));
                if (methodEnd == methodStart)
                {
                    continue;
                }

                populatedModuleCount++;
                var (moduleRefStart, moduleRefEnd)
                    = tables.Modules.GetModuleRefRange(moduleRow);
                var isSelectedRoot = index == 0
                    && moduleRow == rootModuleRow;
                if (!isSelectedRoot && moduleRefEnd != moduleRefStart)
                {
                    throw Unsupported(
                        "Only the selected root Module may retain a provider Import in this F slice.");
                }

                if (!isSelectedRoot)
                {
                    _ = LoadForeignInvocationDraftNativeAddressLibrary(
                        artifacts[index], moduleRow, null,
                        verifySemantics: false);
                }
            }

            if (index == 0 && rootModuleRow == 0)
            {
                throw new ArgumentException(
                    "The selected root Module identity is not present in the artifact.",
                    nameof(rootModuleIdentity));
            }

            var (rootMethodStart, rootMethodEnd)
                = tables.Modules.GetMethodDefRange(rootModuleRow);
            if (populatedModuleCount == 0
                || (index == 0 && rootMethodEnd == rootMethodStart))
            {
                throw Unsupported(
                    "The selected F artifact must contain a supported populated Module.");
            }
        }

        return new StandardModuleValidation(rootModuleRow,
            moduleSelections.ToImmutable());
    }

    private static int FindModuleRow(TrRmModuleTableReader modules,
        string moduleIdentity)
    {
        for (var moduleRow = 1; moduleRow <= modules.Count; moduleRow++)
        {
            if (StringComparer.Ordinal.Equals(
                    modules.GetIdentity(moduleRow), moduleIdentity))
            {
                return moduleRow;
            }
        }

        throw new ArgumentException(
            "The selected Module identity is not present in the artifact.",
            nameof(moduleIdentity));
    }
}
