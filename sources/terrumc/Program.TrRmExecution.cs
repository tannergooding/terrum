// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Terrum.CodeAnalysis.Text;

namespace Terrum.Tools;

internal static partial class Program
{
    private readonly record struct TrRmExecutionConfigurationFile(
        CoreManifestKind? CoreKind, string Path,
        bool IsSourceEnumExports,
        bool IsSourceGenericMethodExports = false,
        bool IsSourceTypeExports = false,
        bool IsSourceProviders = false);

    private const string A3FixedArraySchemaIdentity
        = "trrm/1-draft-foreign-invocation-storage-fixed-arrays-3-unique-1";
    private const string A2EUScalarFrozenValuesSchemaIdentity
        = "trrm/1-draft-foreign-invocation-storage-fixed-arrays-2-empty-unit-initialization-scalar-frozen-values-1";
    private const string A2R1ReturnCoverageSchemaIdentity
        = "trrm/1-draft-foreign-invocation-storage-fixed-arrays-2-empty-unit-initialization-scalar-frozen-values-copy-origin-return-coverage-1";
    private const string A2R1RepeatInitializationSchemaIdentity
        = "trrm/1-draft-foreign-invocation-storage-fixed-arrays-2-empty-unit-initialization-scalar-frozen-values-copy-origin-return-coverage-repeat-initialization-1";
    private const string LocalAggregateLayoutSchemaIdentity
        = "trrm/1-draft-local-aggregate-layout-execution-1";
    private const string RegisterExceptionRegionsSchemaIdentity
        = "trrm/1-draft-register-exception-regions-1";
    private const string GenericMethodCallsSchemaIdentity
        = "trrm/1-draft-generic-method-calls-parameter-labels-1";
    private const string NestedFieldDestinationsSchemaIdentity
        = "trrm/1-draft-init-reference-bodies-local-field-layout-2";
    private const string ForeignNativeInterfaceParameterSurfaceSchemaIdentity
        = "trrm/1-draft-foreign-native-interface-parameter-surface-1";
    private const string ForeignNativeInterfaceAggregateOutputSchemaIdentity
        = "trrm/1-draft-foreign-native-interface-aggregate-output-consumption-1";
    private const string ForeignInvocationStorageFixedArraySchemaIdentity
        = "trrm/1-draft-foreign-invocation-storage-fixed-arrays-2";

    private static bool IsC1SourceProviderSchemaIdentity(
        string schemaIdentity)
        => schemaIdentity is
            "trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1"
            or "trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-1-parameter-labels-1-receiver-access-1"
            or "trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-2-parameter-labels-1-receiver-access-1"
            or "trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-1-parameter-labels-1-receiver-access-1-property-initialized-state-1"
            or "trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-2-parameter-labels-1-receiver-access-1-property-initialized-state-1"
            or "trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-1-parameter-labels-1-receiver-access-1-enum-closure-1"
            or "trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-2-parameter-labels-1-receiver-access-1-enum-closure-1"
            or "trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-1-parameter-labels-1-receiver-access-1-property-initialized-state-1-enum-closure-1"
            or "trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-2-parameter-labels-1-receiver-access-1-property-initialized-state-1-enum-closure-1"
            or "trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-1-parameter-labels-1-receiver-access-1-enum-closure-1-class-instance-layout-1"
            or "trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-2-parameter-labels-1-receiver-access-1-enum-closure-1-class-instance-layout-1"
            or "trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-1-parameter-labels-1-receiver-access-1-property-initialized-state-1-enum-closure-1-class-instance-layout-1"
            or "trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-2-parameter-labels-1-receiver-access-1-property-initialized-state-1-enum-closure-1-class-instance-layout-1"
            or "trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-2-parameter-labels-1-receiver-access-1-class-parameter-defaults-1"
            or "trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-2-parameter-labels-1-receiver-access-1-class-parameter-defaults-1-associated-relation-constraints-1";

    private static bool IsSourceProviderSchemaIdentity(int schemaVersion,
        string schemaIdentity)
        => schemaVersion switch
        {
            1 => StringComparer.Ordinal.Equals(schemaIdentity,
                NestedFieldDestinationsSchemaIdentity),
            2 => IsC1SourceProviderSchemaIdentity(schemaIdentity),
            3 => IsMethodTypeSourceProviderSchemaIdentity(schemaIdentity),
            4 => StringComparer.Ordinal.Equals(schemaIdentity,
                ForeignNativeInterfaceAggregateOutputSchemaIdentity),
            _ => false,
        };

    private static bool IsSourceTypeExportsSchemaIdentity(int schemaVersion,
        string schemaIdentity)
        => schemaVersion switch
        {
            1 => StringComparer.Ordinal.Equals(schemaIdentity,
                NestedFieldDestinationsSchemaIdentity),
            2 => schemaIdentity is
                ForeignNativeInterfaceAggregateOutputSchemaIdentity
                or ForeignNativeInterfaceParameterSurfaceSchemaIdentity
                or ForeignInvocationStorageFixedArraySchemaIdentity,
            _ => false,
        };

    private static bool IsSourceProviderPairing(
        TrRmSourceProvidersFile configuration, string outputSchemaIdentity)
    {
        if (configuration.SchemaVersion != 4)
        {
            return configuration.Providers.All(provider =>
                StringComparer.Ordinal.Equals(provider.SchemaIdentity,
                    outputSchemaIdentity));
        }

        if (!StringComparer.Ordinal.Equals(outputSchemaIdentity,
                ForeignNativeInterfaceAggregateOutputSchemaIdentity)
            || configuration.References.Length != 1)
        {
            return false;
        }

        var reference = configuration.References[0];
        var rootSchemaIdentity = configuration.Providers[
            reference.ProviderInputOrdinal].SchemaIdentity;
        return reference.Kind switch {
            "foreign-native-interface" =>
                (rootSchemaIdentity is
                    ForeignNativeInterfaceAggregateOutputSchemaIdentity
                    or ForeignNativeInterfaceParameterSurfaceSchemaIdentity)
                && configuration.Providers.All(static provider =>
                    provider.SchemaIdentity is
                        ForeignNativeInterfaceAggregateOutputSchemaIdentity
                        or ForeignNativeInterfaceParameterSurfaceSchemaIdentity
                        or ForeignInvocationStorageFixedArraySchemaIdentity),
            "aggregate-types" =>
                StringComparer.Ordinal.Equals(rootSchemaIdentity,
                    ForeignInvocationStorageFixedArraySchemaIdentity)
                && configuration.Providers.All(static provider =>
                    StringComparer.Ordinal.Equals(provider.SchemaIdentity,
                        ForeignInvocationStorageFixedArraySchemaIdentity)),
            _ => false,
        };
    }

    private static bool IsMethodTypeSourceProviderSchemaIdentity(
        string schemaIdentity)
        => schemaIdentity is
            "trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-2-initializer-copy-evidence-1"
            or "trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-2-initializer-copy-evidence-1-type-binding-views-1"
            or "trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-2-parameter-labels-1-receiver-access-1-class-parameter-defaults-1-initializer-copy-evidence-1"
            or "trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-2-parameter-labels-1-receiver-access-1-class-parameter-defaults-1-initializer-copy-evidence-1-type-binding-views-1"
            or "trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-2-parameter-labels-1-receiver-access-1-class-parameter-defaults-1-associated-relation-constraints-1-initializer-copy-evidence-1"
            or "trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-2-parameter-labels-1-receiver-access-1-class-parameter-defaults-1-associated-relation-constraints-1-initializer-copy-evidence-1-type-binding-views-1";

    private static int RunTrRmExecutionCommand(string[] args, bool json)
    {
        var command = args[0];
        var count = args.Length - (json && args[^1] == "--json" ? 1 : 0);
        if (count < 2 || args[1] == "--json"
            || (json && args[^1] != "--json"))
        {
            WriteInputError(json, "TRM2000", Usage);
            return 2;
        }
        string? output = null;
        string? identity = null;
        string? schemaIdentity = null;
        string? moduleIdentity = null;
        string? layoutCoreManifestPath = null;
        string? nativeCoreManifestPath = null;
        string? nativeOutputCoreManifestPath = null;
        string? evaluationCoreManifestPath = null;
        string? sourceEnumExportsPath = null;
        string? sourceTypeExportsPath = null;
        string? sourceGenericMethodExportsPath = null;
        string? sourceProvidersPath = null;
        IReadOnlyList<TrRmSourceTypeExport>? sourceTypeExports = null;
        TrRmSourceTypeExportsFile? sourceTypeExportsFile = null;
        TrRmSourceGenericMethodExportsFile? sourceMethodExportsFile = null;
        TrRmSourceProvidersFile? sourceProvidersFile = null;
        CompilationReference? sourceProvidersReference = null;
        List<string>? sourceProvidersRouteInputPaths = null;
        var configurationFileOrder
            = new List<TrRmExecutionConfigurationFile>();
        LayoutCoreRegistrationSet? layoutCoreRegistration = null;
        NativeCoreRegistrationSet? nativeCoreRegistration = null;
        NativeOutputCoreRegistrationSet? nativeOutputCoreRegistration = null;
        CoreEvaluationFormIdentitySelection? evaluationFormSelection = null;
        var library = false;
        var runtime = false;
        var hasArithmeticProfile = false;
        var sourceArithmeticProfile = SourceArithmeticProfile.Development;
        var paths = new List<string> { args[1] };
        var exports = new List<string>();
        var providers = new List<(string SchemaIdentity, string Path)>();
        var coreExceptionAnchors = new Dictionary<
            (string UsingModuleIdentity, string Role),
            (string ProducerModuleIdentity, string ExportKey)>();
        var uniqueExports = new HashSet<string>(StringComparer.Ordinal);
        var a3AdmissionCallFailed = false;
        var isArtifactCommand = command is "run-trrm" or "build-trrm"
            or "build-trrm-library";
        try
        {
            for (var i = 2; i < count;)
            {
                if (args[i] == "--library" && command == "compile-trrm" && !library)
                {
                    library = true;
                    i++;
                    continue;
                }
                if (args[i] == "--trrm-provider")
                {
                    if (!isArtifactCommand || i + 2 >= count
                        || args[i + 1].Length == 0
                        || args[i + 2].Length == 0 || args[i + 2] == "-")
                    {
                        WriteInputError(json, "TRM2000", Usage);
                        return 2;
                    }

                    providers.Add((args[i + 1], args[i + 2]));
                    i += 3;
                    continue;
                }
                if (args[i] == "--trrm-core-exception-anchor")
                {
                    if (!isArtifactCommand || i + 4 >= count)
                    {
                        WriteInputError(json, "TRM2000", Usage);
                        return 2;
                    }

                    var usingModuleIdentity = args[i + 1];
                    var role = args[i + 2];
                    var producerModuleIdentity = args[i + 3];
                    var exportKey = args[i + 4];
                    if (usingModuleIdentity.Length == 0
                        || role is not ("exception-interface"
                            or "unreachable-payload")
                        || producerModuleIdentity.Length == 0
                        || exportKey.Length == 0
                        || !coreExceptionAnchors.TryAdd(
                            (usingModuleIdentity, role),
                            (producerModuleIdentity, exportKey)))
                    {
                        WriteInputError(json, "TRM2000", Usage);
                        return 2;
                    }

                    i += 5;
                    continue;
                }
                if (i + 1 == count
                    || ((args[i] is "--trrm-schema" or "--trrm-module"
                        or "--trrm-source-enum-exports"
                        or "--trrm-source-type-exports"
                        or "--trrm-source-providers"
                        or "--trrm-source-generic-method-exports")
                        && args[i + 1].StartsWith('-',
                            StringComparison.Ordinal)))
                {
                    WriteInputError(json, "TRM2000", Usage);
                    return 2;
                }
                var value = args[i + 1];
                switch (args[i])
                {
                    case "-o" when command != "run-trrm" && output is null:
                        output = Path.GetFullPath(value);
                        break;
                    case "--identity" when command == "compile-trrm" && identity is null && value.Length > 0:
                        identity = value;
                        break;
                    case "--trrm-schema" when (command == "compile-trrm"
                        || isArtifactCommand)
                        && schemaIdentity is null && value.Length > 0:
                        schemaIdentity = value;
                        break;
                    case "--trrm-module" when isArtifactCommand
                        && moduleIdentity is null && value.Length > 0:
                        moduleIdentity = value;
                        break;
                    case "--source" when command == "compile-trrm" && paths.Count < 256
                        && !paths.Contains(value, StringComparer.OrdinalIgnoreCase):
                        paths.Add(value);
                        break;
                    case "--layout-core-manifest" when command == "compile-trrm"
                        && layoutCoreManifestPath is null
                        && value.Length != 0 && value != "-"
                        && !value.StartsWith('-',
                            StringComparison.Ordinal)
                        && TryGetFullPath(value, out var manifestFullPath):
                        layoutCoreManifestPath = manifestFullPath;
                        configurationFileOrder.Add(
                            new TrRmExecutionConfigurationFile(
                                CoreManifestKind.Layout, manifestFullPath,
                                false));
                        break;
                    case "--native-core-manifest" when command == "compile-trrm"
                        && nativeCoreManifestPath is null
                        && value.Length != 0 && value != "-"
                        && !value.StartsWith('-',
                            StringComparison.Ordinal)
                        && TryGetFullPath(value, out var nativeManifestFullPath):
                        nativeCoreManifestPath = nativeManifestFullPath;
                        configurationFileOrder.Add(
                            new TrRmExecutionConfigurationFile(
                                CoreManifestKind.Native,
                                nativeManifestFullPath, false));
                        break;
                    case "--native-output-core-manifest"
                        when command == "compile-trrm"
                        && nativeOutputCoreManifestPath is null
                        && value.Length != 0 && value != "-"
                        && !value.StartsWith('-', StringComparison.Ordinal)
                        && TryGetFullPath(value,
                            out var nativeOutputManifestFullPath):
                        nativeOutputCoreManifestPath
                            = nativeOutputManifestFullPath;
                        configurationFileOrder.Add(
                            new TrRmExecutionConfigurationFile(
                                CoreManifestKind.NativeOutput,
                                nativeOutputManifestFullPath, false));
                        break;
                    case "--evaluation-core-manifest"
                        when command == "compile-trrm"
                        && evaluationCoreManifestPath is null
                        && value.Length != 0 && value != "-"
                        && !value.StartsWith('-', StringComparison.Ordinal)
                        && TryGetFullPath(value,
                            out var evaluationManifestFullPath):
                        evaluationCoreManifestPath
                            = evaluationManifestFullPath;
                        configurationFileOrder.Add(
                            new TrRmExecutionConfigurationFile(
                                CoreManifestKind.Evaluation,
                                evaluationManifestFullPath, false));
                        break;
                    case "--trrm-source-enum-exports"
                        when command == "compile-trrm"
                        && sourceEnumExportsPath is null
                        && value.Length != 0 && value != "-"
                        && !value.StartsWith('-', StringComparison.Ordinal)
                        && TryGetFullPath(value,
                            out var sourceEnumExportsFullPath):
                        sourceEnumExportsPath = sourceEnumExportsFullPath;
                        configurationFileOrder.Add(
                            new TrRmExecutionConfigurationFile(null,
                                sourceEnumExportsFullPath, true));
                        break;
                    case "--trrm-source-type-exports"
                        when command == "compile-trrm"
                        && sourceTypeExportsPath is null
                        && value.Length != 0 && value != "-"
                        && !value.StartsWith('-', StringComparison.Ordinal)
                        && TryGetFullPath(value,
                            out var sourceTypeExportsFullPath):
                        sourceTypeExportsPath = sourceTypeExportsFullPath;
                        configurationFileOrder.Add(
                            new TrRmExecutionConfigurationFile(null,
                                sourceTypeExportsFullPath, false, false,
                                true));
                        break;
                    case "--trrm-source-providers"
                        when command == "compile-trrm"
                        && sourceProvidersPath is null
                        && value.Length != 0 && value != "-"
                        && !value.StartsWith('-', StringComparison.Ordinal)
                        && TryGetFullPath(value,
                            out var sourceProvidersFullPath):
                        sourceProvidersPath = sourceProvidersFullPath;
                        configurationFileOrder.Add(
                            new TrRmExecutionConfigurationFile(null,
                                sourceProvidersFullPath, false, false,
                                false, true));
                        break;
                    case "--trrm-source-generic-method-exports"
                        when command == "compile-trrm"
                        && sourceGenericMethodExportsPath is null
                        && value.Length != 0 && value != "-"
                        && !value.StartsWith('-', StringComparison.Ordinal)
                        && TryGetFullPath(value,
                            out var sourceGenericMethodExportsFullPath):
                        sourceGenericMethodExportsPath
                            = sourceGenericMethodExportsFullPath;
                        configurationFileOrder.Add(
                            new TrRmExecutionConfigurationFile(null,
                                sourceGenericMethodExportsFullPath, false,
                                true));
                        break;
                    case "--export" when command == "build-trrm-library" && uniqueExports.Add(value):
                        exports.Add(value);
                        break;
                    case "--runtime" when !runtime && value == "fail-fast":
                        runtime = true;
                        break;
                    case "--runtime" when !runtime && value == "checked-exceptions":
                        WriteInputError(json, "TRM5001",
                            "TRRM (1, 0) execution supports only fail-fast; checked-exceptions requires an exceptional-edge contract.");
                        return 1;
                    case "--arithmetic-profile" when command == "compile-trrm"
                        && !hasArithmeticProfile
                        && TryParseSourceArithmeticProfile(value, out sourceArithmeticProfile):
                        hasArithmeticProfile = true;
                        break;
                    default:
                        WriteInputError(json, "TRM2000", Usage);
                        return 2;
                }
                i += 2;
            }
            var coreExceptionIdentitySelections
                = ImmutableArray.CreateBuilder<CoreExceptionIdentitySelection>();
            var selectedUsingModules = new HashSet<string>(
                StringComparer.Ordinal);
            foreach (var (usingModuleIdentity, _) in coreExceptionAnchors.Keys)
            {
                _ = selectedUsingModules.Add(usingModuleIdentity);
            }
            foreach (var usingModuleIdentity in selectedUsingModules)
            {
                if (!coreExceptionAnchors.TryGetValue(
                        (usingModuleIdentity, "exception-interface"),
                        out var exceptionInterface)
                    || !coreExceptionAnchors.TryGetValue(
                        (usingModuleIdentity, "unreachable-payload"),
                        out var unreachablePayload))
                {
                    WriteInputError(json, "TRM2000", Usage);
                    return 2;
                }

                coreExceptionIdentitySelections.Add(
                    new CoreExceptionIdentitySelection(
                        usingModuleIdentity,
                        exceptionInterface.ProducerModuleIdentity,
                        exceptionInterface.ExportKey,
                        unreachablePayload.ProducerModuleIdentity,
                        unreachablePayload.ExportKey));
            }
            var selectedCoreExceptionIdentitySelections
                = coreExceptionIdentitySelections.ToImmutable();
            if (!selectedCoreExceptionIdentitySelections.IsEmpty
                && !StringComparer.Ordinal.Equals(schemaIdentity,
                    RegisterExceptionRegionsSchemaIdentity))
            {
                WriteInputError(json, "TRM2000", Usage);
                return 2;
            }

            if (sourceEnumExportsPath is not null
                && !StringComparer.Ordinal.Equals(schemaIdentity,
                    "trrm/1-draft-nominal-enums-1"))
            {
                WriteInputError(json, "TRM2000", Usage);
                return 2;
            }
            if (sourceTypeExportsPath is not null
                && (schemaIdentity is null
                    || !(schemaIdentity is
                        NestedFieldDestinationsSchemaIdentity
                        or ForeignNativeInterfaceAggregateOutputSchemaIdentity
                        or ForeignNativeInterfaceParameterSurfaceSchemaIdentity
                        or ForeignInvocationStorageFixedArraySchemaIdentity)
                    || sourceEnumExportsPath is not null
                    || sourceGenericMethodExportsPath is not null))
            {
                WriteInputError(json, "TRM2000", Usage);
                return 2;
            }
            if (sourceProvidersPath is not null
                && (schemaIdentity is null
                    || (!StringComparer.Ordinal.Equals(schemaIdentity,
                            NestedFieldDestinationsSchemaIdentity)
                        && !StringComparer.Ordinal.Equals(schemaIdentity,
                            ForeignNativeInterfaceAggregateOutputSchemaIdentity)
                        && !IsC1SourceProviderSchemaIdentity(schemaIdentity)
                        && !IsMethodTypeSourceProviderSchemaIdentity(
                            schemaIdentity))
                    || sourceEnumExportsPath is not null
                    || sourceGenericMethodExportsPath is not null))
            {
                WriteInputError(json, "TRM2000", Usage);
                return 2;
            }
            if (sourceGenericMethodExportsPath is not null
                && (!StringComparer.Ordinal.Equals(schemaIdentity,
                        GenericMethodCallsSchemaIdentity)
                    || sourceEnumExportsPath is not null))
            {
                WriteInputError(json, "TRM2000", Usage);
                return 2;
            }

            if ((command == "compile-trrm"
                && (moduleIdentity is not null || providers.Count != 0))
                || (isArtifactCommand
                    && ((schemaIdentity is null) != (moduleIdentity is null)
                        || (schemaIdentity is null && providers.Count != 0))))
            {
                WriteInputError(json, "TRM2000", Usage);
                return 2;
            }
            var isAc1LibrarySelection
                = command == "build-trrm-library"
                    && StringComparer.Ordinal.Equals(schemaIdentity,
                        ForeignNativeInterfaceAggregateOutputSchemaIdentity)
                    && providers.All(static provider =>
                        provider.SchemaIdentity is
                            ForeignNativeInterfaceAggregateOutputSchemaIdentity
                            or ForeignNativeInterfaceParameterSurfaceSchemaIdentity
                            or ForeignInvocationStorageFixedArraySchemaIdentity);
            if (isArtifactCommand && schemaIdentity is not null
                && !isAc1LibrarySelection
                && (schemaIdentity is not (
                        "trrm/1-draft-foreign-invocation-contracts-1"
                        or "trrm/1-draft-foreign-invocation-storage-addresses-1"
                        or "trrm/1-draft-foreign-invocation-storage-fixed-arrays-2"
                        or "trrm/1-draft-foreign-invocation-storage-fixed-arrays-2-empty-initialization-1"
                        or "trrm/1-draft-foreign-invocation-storage-fixed-arrays-2-empty-unit-initialization-1"
                        or A2EUScalarFrozenValuesSchemaIdentity
                        or A2R1ReturnCoverageSchemaIdentity
                        or A2R1RepeatInitializationSchemaIdentity
                        or "trrm/1-draft-foreign-invocation-storage-fixed-arrays-3-unique-1"
                        or LocalAggregateLayoutSchemaIdentity
                        or NestedFieldDestinationsSchemaIdentity
                        or RegisterExceptionRegionsSchemaIdentity)
                    || providers.Exists(static provider =>
                        provider.SchemaIdentity is not (
                            "trrm/1-draft-foreign-invocation-contracts-1"
                            or "trrm/1-draft-foreign-invocation-storage-addresses-1"
                            or "trrm/1-draft-foreign-invocation-storage-fixed-arrays-2"
                            or "trrm/1-draft-foreign-invocation-storage-fixed-arrays-2-empty-initialization-1"
                            or "trrm/1-draft-foreign-invocation-storage-fixed-arrays-2-empty-unit-initialization-1"
                            or A2EUScalarFrozenValuesSchemaIdentity
                            or A2R1ReturnCoverageSchemaIdentity
                            or A2R1RepeatInitializationSchemaIdentity
                            or "trrm/1-draft-foreign-invocation-storage-fixed-arrays-3-unique-1"
                            or LocalAggregateLayoutSchemaIdentity
                            or NestedFieldDestinationsSchemaIdentity
                            or RegisterExceptionRegionsSchemaIdentity))))
            {
                WriteInputError(json, "TRM2000", Usage);
                return 2;
            }
            if (isArtifactCommand
                && StringComparer.Ordinal.Equals(schemaIdentity,
                    NestedFieldDestinationsSchemaIdentity)
                && providers.Exists(static provider =>
                    !StringComparer.Ordinal.Equals(provider.SchemaIdentity,
                        NestedFieldDestinationsSchemaIdentity)))
            {
                WriteInputError(json, "TRM2000", Usage);
                return 2;
            }
            if (isArtifactCommand && schemaIdentity is not null
                && (StringComparer.Ordinal.Equals(schemaIdentity,
                        RegisterExceptionRegionsSchemaIdentity)
                    || providers.Exists(static provider =>
                        StringComparer.Ordinal.Equals(provider.SchemaIdentity,
                            RegisterExceptionRegionsSchemaIdentity)))
                && (!StringComparer.Ordinal.Equals(schemaIdentity,
                        RegisterExceptionRegionsSchemaIdentity)
                    || providers.Exists(static provider =>
                        !StringComparer.Ordinal.Equals(provider.SchemaIdentity,
                            RegisterExceptionRegionsSchemaIdentity))))
            {
                WriteInputError(json, "TRM2000", Usage);
                return 2;
            }
            if (!runtime || (command == "compile-trrm" && identity is null)
                || (command is "build-trrm" or "build-trrm-library" && args[1] == "-")
                || (command == "run-trrm" && (output is not null || args[1] == "-"))
                || (command != "run-trrm" && output is null)
                || (command == "build-trrm-library" && exports.Count == 0)
                || (output is not null && (!Path.GetExtension(output).Equals(
                    command switch { "compile-trrm" => ".trrm", "build-trrm" => ".exe", _ => ".dll" },
                    StringComparison.OrdinalIgnoreCase)
                    || paths.Any(path => path != "-" && PathsAlias(output,
                        path)))))
            {
                WriteInputError(json, "TRM2000", Usage);
                return 2;
            }
            if (output is not null && providers.Any(provider =>
                    PathsAlias(output, provider.Path)))
            {
                WriteInputError(json, "TRM2000", Usage);
                return 2;
            }
            if (sourceEnumExportsPath is not null
                && ((output is not null
                        && PathsAlias(sourceEnumExportsPath, output))
                    || paths.Any(path => path != "-"
                        && PathsAlias(sourceEnumExportsPath, path))
                    || (layoutCoreManifestPath is string layoutPath
                        && PathsAlias(sourceEnumExportsPath, layoutPath))
                    || (nativeCoreManifestPath is string nativePath
                        && PathsAlias(sourceEnumExportsPath, nativePath))
                    || (nativeOutputCoreManifestPath
                        is string nativeOutputPath
                        && PathsAlias(sourceEnumExportsPath,
                            nativeOutputPath))))
            {
                WriteInputError(json, "TRM2002",
                    "The source enum export file must be distinct from every source, Core manifest and artifact output.");
                return 2;
            }
            if (sourceTypeExportsPath is not null
                && ((output is not null
                        && PathsAlias(sourceTypeExportsPath, output))
                    || paths.Any(path => path != "-"
                        && PathsAlias(sourceTypeExportsPath, path))
                    || (layoutCoreManifestPath is string typeLayoutPath
                        && PathsAlias(sourceTypeExportsPath,
                            typeLayoutPath))
                    || (nativeCoreManifestPath is string typeNativePath
                        && PathsAlias(sourceTypeExportsPath,
                            typeNativePath))
                    || (nativeOutputCoreManifestPath
                        is string typeNativeOutputPath
                        && PathsAlias(sourceTypeExportsPath,
                            typeNativeOutputPath))
                    || (evaluationCoreManifestPath
                        is string typeEvaluationPath
                        && PathsAlias(sourceTypeExportsPath,
                            typeEvaluationPath))
                    || (sourceEnumExportsPath is string enumExportsPath
                        && PathsAlias(sourceTypeExportsPath,
                            enumExportsPath))
                    || (sourceGenericMethodExportsPath
                        is string genericExportsPath
                        && PathsAlias(sourceTypeExportsPath,
                            genericExportsPath))))
            {
                WriteInputError(json, "TRM2002",
                    "The source type export file must be distinct from every source, configuration and artifact output.");
                return 2;
            }
            if (sourceGenericMethodExportsPath is not null)
            {
                var aliasesInput = paths.Any(path => path != "-"
                    && PathsAlias(sourceGenericMethodExportsPath, path));
                var aliasesAnotherSource = false;
                for (var sourceIndex = 0;
                    sourceIndex < paths.Count && !aliasesAnotherSource;
                    sourceIndex++)
                {
                    if (paths[sourceIndex] == "-")
                    {
                        continue;
                    }
                    for (var otherIndex = 0; otherIndex < sourceIndex;
                        otherIndex++)
                    {
                        if (paths[otherIndex] != "-"
                            && PathsAlias(paths[sourceIndex],
                                paths[otherIndex]))
                        {
                            aliasesAnotherSource = true;
                            break;
                        }
                    }
                }

                if (aliasesInput || aliasesAnotherSource
                    || (output is not null
                        && PathsAlias(sourceGenericMethodExportsPath,
                            output))
                    || (layoutCoreManifestPath is string genericLayoutPath
                        && PathsAlias(sourceGenericMethodExportsPath,
                            genericLayoutPath))
                    || (nativeCoreManifestPath is string genericNativePath
                        && PathsAlias(sourceGenericMethodExportsPath,
                            genericNativePath))
                    || (nativeOutputCoreManifestPath
                        is string genericNativeOutputPath
                        && PathsAlias(sourceGenericMethodExportsPath,
                            genericNativeOutputPath)))
                {
                    WriteInputError(json, "TRM2002",
                        "The source generic method export file and source inputs must be distinct from each other, Core manifests and artifact output.");
                    return 2;
                }
            }
            if (evaluationCoreManifestPath is not null
                && ((output is not null
                        && PathsAlias(evaluationCoreManifestPath, output))
                    || paths.Any(path => path != "-"
                        && PathsAlias(evaluationCoreManifestPath, path))
                    || (layoutCoreManifestPath is not null
                        && PathsAlias(evaluationCoreManifestPath,
                            layoutCoreManifestPath))
                    || (nativeCoreManifestPath is not null
                        && PathsAlias(evaluationCoreManifestPath,
                            nativeCoreManifestPath))
                    || (nativeOutputCoreManifestPath is not null
                        && PathsAlias(evaluationCoreManifestPath,
                            nativeOutputCoreManifestPath))
                    || (sourceEnumExportsPath is not null
                        && PathsAlias(evaluationCoreManifestPath,
                            sourceEnumExportsPath))
                    || (sourceTypeExportsPath is not null
                        && PathsAlias(evaluationCoreManifestPath,
                            sourceTypeExportsPath))
                    || (sourceGenericMethodExportsPath is not null
                        && PathsAlias(evaluationCoreManifestPath,
                            sourceGenericMethodExportsPath))))
            {
                WriteInputError(json, "TRM2002",
                    "The evaluation Core manifest must be distinct from every source, configuration and artifact output.");
                return 2;
            }
            if (layoutCoreManifestPath is not null
                || nativeCoreManifestPath is not null
                || nativeOutputCoreManifestPath is not null)
            {
                if ((layoutCoreManifestPath is not null
                        && ((output is not null
                                && PathsAlias(layoutCoreManifestPath, output))
                            || paths.Any(path => path != "-"
                                && PathsAlias(layoutCoreManifestPath, path))))
                    || (nativeCoreManifestPath is not null
                        && ((output is not null
                                && PathsAlias(nativeCoreManifestPath, output))
                            || (layoutCoreManifestPath is not null
                                && PathsAlias(nativeCoreManifestPath,
                                    layoutCoreManifestPath))
                            || (nativeOutputCoreManifestPath is not null
                                && PathsAlias(nativeCoreManifestPath,
                                    nativeOutputCoreManifestPath))
                            || paths.Any(path => path != "-"
                                && PathsAlias(nativeCoreManifestPath, path))))
                    || (layoutCoreManifestPath is not null
                        && nativeCoreManifestPath is not null
                        && PathsAlias(layoutCoreManifestPath,
                            nativeCoreManifestPath))
                    || (nativeOutputCoreManifestPath is not null
                        && ((output is not null
                                && PathsAlias(
                                    nativeOutputCoreManifestPath, output))
                            || (layoutCoreManifestPath is not null
                                && PathsAlias(
                                    nativeOutputCoreManifestPath,
                                    layoutCoreManifestPath))
                            || (nativeCoreManifestPath is not null
                                && PathsAlias(
                                    nativeOutputCoreManifestPath,
                                    nativeCoreManifestPath))
                            || paths.Any(path => path != "-"
                                && PathsAlias(
                                    nativeOutputCoreManifestPath, path)))))
                {
                    WriteInputError(json, "TRM2002",
                        "Core manifests must be distinct from one another, the source and artifact output.");
                    return 2;
                }
            }

            if (sourceProvidersPath is not null)
            {
                var preflightPaths = new List<string>();
                foreach (var sourcePath in paths)
                {
                    if (sourcePath != "-")
                    {
                        preflightPaths.Add(sourcePath);
                    }
                }
                foreach (var configurationFile in configurationFileOrder)
                {
                    preflightPaths.Add(configurationFile.Path);
                }
                if (output is not null)
                {
                    preflightPaths.Add(output);
                }

                for (var left = 0; left < preflightPaths.Count; left++)
                {
                    for (var right = 0; right < left; right++)
                    {
                        if (PathsAlias(preflightPaths[left],
                                preflightPaths[right]))
                        {
                            WriteInputError(json, "TRM2002",
                                "Source, configuration and output paths must be distinct when using source providers.");
                            return 2;
                        }
                    }
                }
            }

            if (configurationFileOrder.Count != 0)
            {
                LayoutCoreManifestDefinition? layoutDefinition = null;
                NativeCoreManifestDefinition? nativeDefinition = null;
                NativeOutputCoreManifestDefinition? nativeOutputDefinition
                    = null;
                EvaluationCoreManifestDefinition? evaluationDefinition = null;
                foreach (var configurationFile in configurationFileOrder)
                {
                    if (configurationFile.IsSourceEnumExports)
                    {
                        sourceTypeExports
                            = ReadTrRmSourceEnumExports(
                                configurationFile.Path);
                        continue;
                    }
                    if (configurationFile.IsSourceTypeExports)
                    {
                        sourceTypeExportsFile
                            = ReadTrRmSourceTypeExports(
                                configurationFile.Path);
                        if (!IsSourceTypeExportsSchemaIdentity(
                                sourceTypeExportsFile.SchemaVersion,
                                schemaIdentity ?? "")
                            || (sourceTypeExportsFile.SchemaVersion == 2
                                && StringComparer.Ordinal.Equals(
                                    schemaIdentity,
                                    ForeignInvocationStorageFixedArraySchemaIdentity)
                                && sourceTypeExportsFile.Exports.Any(
                                    static export =>
                                        !export.FieldExports.IsDefaultOrEmpty
                                        || !export.InterfaceMethodExports
                                            .IsDefaultOrEmpty))
                        {
                            WriteInputError(json, "TRM2000",
                                "The source type export version must explicitly pair the exact output schema and declaration kinds.");
                            return 2;
                        }
                        sourceTypeExports
                            = sourceTypeExportsFile.Exports;
                        continue;
                    }
                    if (configurationFile.IsSourceProviders)
                    {
                        sourceProvidersFile
                            = TrRmSourceProvidersReader.Read(
                                configurationFile.Path);
                        if (schemaIdentity is null
                            || !IsSourceProviderSchemaIdentity(
                                sourceProvidersFile.SchemaVersion,
                                schemaIdentity)
                            || !IsSourceProviderPairing(
                                sourceProvidersFile, schemaIdentity))
                        {
                            WriteInputError(json, "TRM2000",
                                "The source-provider schema version must explicitly pair the exact consumer and provider schemas.");
                            return 2;
                        }
                        continue;
                    }
                    if (configurationFile.IsSourceGenericMethodExports)
                    {
                        sourceMethodExportsFile
                            = TrRmSourceGenericMethodExportsReader.Read(
                                configurationFile.Path);
                        continue;
                    }

                    switch (configurationFile.CoreKind)
                    {
                        case CoreManifestKind.Layout:
                            layoutDefinition
                                = LayoutCoreManifestReader.ReadDefinition(
                                    configurationFile.Path);
                            break;
                        case CoreManifestKind.Native:
                            nativeDefinition
                                = NativeCoreManifestReader.ReadDefinition(
                                    configurationFile.Path);
                            break;
                        case CoreManifestKind.NativeOutput:
                            nativeOutputDefinition
                                = NativeOutputCoreManifestReader
                                    .ReadDefinition(
                                        configurationFile.Path);
                            break;
                        case CoreManifestKind.Evaluation:
                            evaluationDefinition
                                = EvaluationCoreManifestReader.ReadDefinition(
                                    configurationFile.Path);
                            break;
                        default:
                            throw new InvalidOperationException(
                                "A selected TRRM configuration file has no registered reader.");
                    }
                }

                if (sourceMethodExportsFile is not null)
                {
                    ValidateSourceGenericMethodInputPaths(
                        sourceMethodExportsFile, paths,
                        sourceGenericMethodExportsPath!);
                }
                if (sourceTypeExportsFile is {
                        SchemaVersion: 2,
                    } typeExportsFile)
                {
                    ValidateSourceTypeExportsInputPaths(typeExportsFile,
                        paths, sourceTypeExportsPath!);
                }

                var usingModuleIdentity = identity
                    ?? throw new InvalidOperationException(
                        "Missing compile-trrm identity.");
                if ((layoutDefinition is not null
                        && !StringComparer.Ordinal.Equals(
                            layoutDefinition.UsingModuleIdentity,
                            usingModuleIdentity))
                    || (nativeDefinition is not null
                        && !StringComparer.Ordinal.Equals(
                            nativeDefinition.UsingModuleIdentity,
                            usingModuleIdentity))
                    || (nativeOutputDefinition is not null
                        && !StringComparer.Ordinal.Equals(
                            nativeOutputDefinition.UsingModuleIdentity,
                            usingModuleIdentity))
                    || (evaluationDefinition is not null
                        && !StringComparer.Ordinal.Equals(
                            evaluationDefinition.UsingModuleIdentity,
                            usingModuleIdentity)))
                {
                    WriteInputError(json, "TRM2000",
                        "Each Core manifest usingModuleIdentity must exactly match --identity.");
                    return 2;
                }
                evaluationDefinition?.ValidateRuntimePolicy(
                    BoundRuntimePolicy.FailFast, evaluationCoreManifestPath!);
                var registrations = CoreManifestRegistrationComposer.Create(
                    layoutDefinition, nativeDefinition,
                    nativeOutputDefinition, layoutCoreManifestPath,
                    nativeCoreManifestPath, nativeOutputCoreManifestPath,
                    evaluationDefinition, evaluationCoreManifestPath);
                layoutCoreRegistration = registrations.Layout;
                nativeCoreRegistration = registrations.Native;
                nativeOutputCoreRegistration
                    = registrations.NativeOutput;
                evaluationFormSelection = registrations.Evaluation;
            }

            if (sourceProvidersFile is not null)
            {
                var providerPaths = new string[
                    sourceProvidersFile.Providers.Length];
                for (var index = 0; index < providerPaths.Length; index++)
                {
                    var provider = sourceProvidersFile.Providers[index];
                    if (!TryGetFullPath(provider.Path,
                            out var providerPath))
                    {
                        WriteInputError(json, "TRM2001",
                            $"Invalid source provider path '{provider.Path}'.");
                        return 2;
                    }
                    providerPaths[index] = providerPath;
                }

                var routeInputPaths = new List<string>();
                foreach (var sourcePath in paths)
                {
                    if (sourcePath != "-")
                    {
                        routeInputPaths.Add(sourcePath);
                    }
                }
                foreach (var configurationFile in configurationFileOrder)
                {
                    routeInputPaths.Add(configurationFile.Path);
                }
                routeInputPaths.AddRange(providerPaths);
                for (var left = 0; left < routeInputPaths.Count; left++)
                {
                    for (var right = 0; right < left; right++)
                    {
                        if (PathsAlias(routeInputPaths[left],
                                routeInputPaths[right]))
                        {
                            WriteInputError(json, "TRM2002",
                                "Source, configuration and provider files must be distinct when using source providers.");
                            return 2;
                        }
                    }

                    if (output is not null
                        && PathsAlias(routeInputPaths[left], output))
                    {
                        WriteInputError(json, "TRM2002",
                            "Source, configuration and provider files must be distinct from artifact output.");
                        return 2;
                    }
                }

                sourceProvidersRouteInputPaths = routeInputPaths;
                for (var index = 0; index < providerPaths.Length; index++)
                {
                    if (!File.Exists(providerPaths[index])
                        || Directory.Exists(providerPaths[index]))
                    {
                        WriteInputError(json, "TRM2001",
                            $"Source provider file '{providerPaths[index]}' is not a regular file.");
                        return 2;
                    }
                }

                var providerInputs = new TrRmStandardArtifactInput[
                    providerPaths.Length];
                for (var index = 0; index < providerInputs.Length; index++)
                {
                    providerInputs[index]
                        = new TrRmStandardArtifactInput(
                            sourceProvidersFile.Providers[index]
                                .SchemaIdentity,
                            File.ReadAllBytes(providerPaths[index]));
                }

                var references = new CompilationReference[
                    sourceProvidersFile.References.Length];
                for (var referenceIndex = 0;
                    referenceIndex < references.Length; referenceIndex++)
                {
                    var selection
                        = sourceProvidersFile.References[referenceIndex];
                    var providersForReference
                        = new List<TrRmStandardArtifactInput>(
                            providerInputs.Length - 1);
                    for (var providerIndex = 0;
                        providerIndex < providerInputs.Length;
                        providerIndex++)
                    {
                        if (providerIndex != selection.ProviderInputOrdinal)
                        {
                            providersForReference.Add(
                                providerInputs[providerIndex]);
                        }
                    }

                    try
                    {
                        var input = providerInputs[
                            selection.ProviderInputOrdinal];
                        references[referenceIndex]
                            = sourceProvidersFile.SchemaVersion switch {
                            4 when selection.Kind
                                    == "foreign-native-interface"
                                => TrRmExecutableCompiler
                                    .CreateStandardReference(input,
                                        selection.ModuleIdentity,
                                        providersForReference),
                            4 when selection.Kind == "aggregate-types"
                                => TrRmExecutableCompiler
                                    .CreateStandardReference(input,
                                        selection.ModuleIdentity,
                                        providersForReference,
                                        selection.TypeExportKeys),
                                3 => TrRmExecutableCompiler
                                    .CreateStandardReference(input,
                                        selection.ModuleIdentity,
                                        providersForReference,
                                        selection.MethodExportKeys,
                                        selection
                                            .ModuleQualifiedTypeExportKeys
                                            .Select(static selected =>
                                                new TrRmStandardExportKeySelection(
                                                    selected.ModuleIdentity,
                                                    selected.ExportKey))
                                            .ToArray(),
                                        selection
                                            .ModuleQualifiedFieldExportKeys
                                            .Select(static selected =>
                                                new TrRmStandardExportKeySelection(
                                                    selected.ModuleIdentity,
                                                    selected.ExportKey))
                                            .ToArray()),
                                2 when selection.FieldExportKeys.IsEmpty
                                    => TrRmExecutableCompiler
                                        .CreateStandardReference(
                                            input, selection.ModuleIdentity,
                                            providersForReference,
                                            selection.TypeExportKeys),
                                _ => TrRmExecutableCompiler
                                    .CreateStandardReference(
                                        input, selection.ModuleIdentity,
                                        providersForReference,
                                        selection.TypeExportKeys,
                                        selection.FieldExportKeys),
                            };
                    }
                    catch (ArgumentException exception)
                    {
                        WriteInputError(json, "TRM2001",
                            $"Invalid source provider selection at $.references[{referenceIndex}]: {exception.Message}");
                        return 2;
                    }
                }

                sourceProvidersReference = references.Length == 1
                    ? references[0]
                    : TrRmExecutableCompiler
                        .CreateStandardReferenceSet(references);
            }

            byte[] artifact;
            if (command == "compile-trrm")
            {
                var trees = ImmutableArray.CreateBuilder<SyntaxTree>(paths.Count);
                foreach (var path in paths)
                {
                    trees.Add(SyntaxTree.Parse(ReadSource(path), path == "-" ? "<stdin>" : path));
                }
                var snapshots = trees.MoveToImmutable();
                if (sourceMethodExportsFile is not null)
                {
                    ValidateSourceGenericMethodSnapshots(
                        sourceMethodExportsFile, snapshots,
                        sourceGenericMethodExportsPath!);
                }
                if (sourceTypeExportsFile is {
                        SchemaVersion: 2,
                    } typeExportsFile)
                {
                    ValidateSourceTypeExportsSnapshots(typeExportsFile,
                        snapshots, sourceTypeExportsPath!);
                }
                var options = new CompilationOptions {
                    SourceArithmeticProfile = sourceArithmeticProfile,
                    RuntimePolicy = BoundRuntimePolicy.FailFast,
                    LayoutCoreRegistration = layoutCoreRegistration,
                    NativeCoreRegistration = nativeCoreRegistration,
                    NativeOutputCoreRegistration
                        = nativeOutputCoreRegistration,
                    CoreEvaluationFormIdentitySelection
                        = evaluationFormSelection,
                };
                Compilation CreateCompilation()
                {
                    if (sourceProvidersReference is not null)
                    {
                        if (library)
                        {
                            return snapshots.Length == 1
                                ? Compilation.CreateLibrary(snapshots[0],
                                    sourceProvidersReference, options)
                                : Compilation.CreateLibrary(snapshots,
                                    sourceProvidersReference, options);
                        }

                        return snapshots.Length == 1
                            ? Compilation.Create(snapshots[0],
                                sourceProvidersReference, options)
                            : Compilation.Create(snapshots,
                                sourceProvidersReference, options);
                    }

                    if (library)
                    {
                        return snapshots.Length == 1
                            ? Compilation.CreateLibrary(snapshots[0],
                                options)
                            : Compilation.CreateLibrary(snapshots, options);
                    }

                    return snapshots.Length == 1
                        ? Compilation.Create(snapshots[0], options)
                        : Compilation.Create(snapshots, options);
                }

                var compilation = CreateCompilation();
                if (compilation.HasErrors)
                {
                    WriteCompilationResult(command, compilation, compilation.SourceDiagnostics,
                        false, null, null, json);
                    return 1;
                }
                var program = compilation.Program
                    ?? throw new InvalidOperationException("A successful compilation must contain a bound program.");
                if (evaluationFormSelection is not null)
                {
                    throw new NotSupportedException(
                        "TRRM publication cannot retain the selected evaluation-form schema context; a declaration/context codec is required.");
                }
                var sourceModuleIdentity = identity
                    ?? throw new InvalidOperationException("Missing module identity.");
                if (schemaIdentity is null)
                {
                    artifact = TrRmExecutableCompiler.Emit(program,
                        sourceModuleIdentity);
                }
                else
                {
                    try
                    {
                        if (sourceMethodExportsFile
                            is TrRmSourceGenericMethodExportsFile methodMap)
                        {
                            var typeExports = ImmutableArray.CreateBuilder<
                                TrRmSourceTypeExport>(
                                    methodMap.TypeExports.Length);
                            foreach (var typeExport in methodMap.TypeExports)
                            {
                                var interfaceMethodExports =
                                    ImmutableArray.CreateBuilder<
                                        TrRmSourceInterfaceMethodExport>(
                                            typeExport
                                                .InterfaceMethodExports
                                                .Length);
                                foreach (var methodExport in typeExport
                                    .InterfaceMethodExports)
                                {
                                    interfaceMethodExports.Add(
                                        new TrRmSourceInterfaceMethodExport(
                                            methodExport.SourceInputOrdinal,
                                            new TextSpan(
                                                methodExport.DeclarationStart,
                                                methodExport.DeclarationLength),
                                            methodExport.ExportKey));
                                }
                                typeExports.Add(new TrRmSourceTypeExport(
                                    typeExport.Namespace, typeExport.Name,
                                    typeExport.ExportKey) {
                                    GenericArity = typeExport.GenericArity,
                                    InterfaceMethodExports =
                                        interfaceMethodExports
                                            .MoveToImmutable(),
                                });
                            }
                            var methodExports =
                                ImmutableArray.CreateBuilder<
                                    TrRmSourceGenericMethodExport>(
                                        methodMap.Exports.Length);
                            foreach (var export in methodMap.Exports)
                            {
                                methodExports.Add(
                                    new TrRmSourceGenericMethodExport(
                                        program,
                                        export.SourceInputOrdinal,
                                        new TextSpan(
                                            export.DeclarationStart,
                                            export.DeclarationLength),
                                        export.ExportKey));
                            }
                            artifact = TrRmExecutableCompiler.EmitStandard(
                                program, sourceModuleIdentity,
                                schemaIdentity, 1,
                                typeExports.MoveToImmutable(),
                                methodExports.MoveToImmutable());
                        }
                        else
                        {
                            artifact = sourceTypeExports is null
                            ? TrRmExecutableCompiler.EmitStandard(program,
                                sourceModuleIdentity, schemaIdentity, 1)
                            : TrRmExecutableCompiler.EmitStandard(program,
                                sourceModuleIdentity, schemaIdentity, 1,
                                sourceTypeExports);
                        }
                    }
                    catch (ArgumentException exception)
                        when ((sourceEnumExportsPath is not null
                                || sourceTypeExportsPath is not null)
                            && exception.ParamName == "typeExports")
                    {
                        WriteInputError(json, "TRM2001",
                            exception.Message);
                        return 2;
                    }
                    catch (ArgumentException exception)
                        when (sourceGenericMethodExportsPath is not null
                            && exception.ParamName
                                == "genericMethodExports")
                    {
                        WriteInputError(json, "TRM2001",
                            exception.Message);
                        return 2;
                    }
                    catch (ArgumentException exception)
                        when (sourceGenericMethodExportsPath is not null
                            && exception.ParamName == "typeExports")
                    {
                        WriteInputError(json, "TRM2001",
                            exception.Message);
                        return 2;
                    }
                    catch (ArgumentException exception)
                        when (exception.ParamName == "schemaIdentity")
                    {
                        WriteInputError(json, "TRM2000", exception.Message);
                        return 2;
                    }
                }
                var artifactPath = output ?? throw new InvalidOperationException("Missing artifact output path.");
                if (sourceGenericMethodExportsPath is not null
                    && ((output is not null
                            && PathsAlias(sourceGenericMethodExportsPath,
                                output))
                        || paths.Any(path => path != "-"
                            && PathsAlias(sourceGenericMethodExportsPath,
                                path))
                        || (layoutCoreManifestPath is string lateLayoutPath
                            && PathsAlias(sourceGenericMethodExportsPath,
                                lateLayoutPath))
                        || (nativeCoreManifestPath is string lateNativePath
                            && PathsAlias(sourceGenericMethodExportsPath,
                                lateNativePath))
                        || (nativeOutputCoreManifestPath
                            is string lateNativeOutputPath
                            && PathsAlias(sourceGenericMethodExportsPath,
                                lateNativeOutputPath))
                        || (evaluationCoreManifestPath
                            is string lateEvaluationPath
                            && PathsAlias(sourceGenericMethodExportsPath,
                                lateEvaluationPath))))
                {
                    WriteInputError(json, "TRM2002",
                        "The source generic method export file must remain distinct from every source, Core manifest and artifact output.");
                    return 2;
                }
                if (sourceProvidersRouteInputPaths is not null
                    && sourceProvidersRouteInputPaths.Any(
                        path => PathsAlias(path, artifactPath)))
                {
                    WriteInputError(json, "TRM2002",
                        "Source, configuration and provider files must remain distinct from artifact output.");
                    return 2;
                }
                WriteArtifact(artifactPath, artifact);
                WriteTrRmSuccess(command, artifactPath, artifact.Length, json);
                return 0;
            }

            artifact = File.ReadAllBytes(args[1]);
            byte[] image;
            if (schemaIdentity is null)
            {
                var program = TrRmExecutableCompiler.Load(artifact);
                if (command == "run-trrm")
                {
                    if (program.EntryPoint < 0)
                    {
                        throw new InvalidDataException("A library TRRM artifact has no executable entry.");
                    }
                    return NativeJit.Execute(program, NativeRuntimePolicy.FailFast,
                        GraphJitRegistrarAssemblyPath());
                }

                if (command == "build-trrm-library")
                {
                    if (program.EntryPoint >= 0)
                    {
                        throw new InvalidDataException("Library emission requires a no-entry TRRM module.");
                    }
                    image = NativeEmitter.EmitLibrary(program, exports,
                        NativeRuntimePolicy.FailFast);
                }
                else
                {
                    if (program.EntryPoint < 0)
                    {
                        throw new InvalidDataException("An executable TRRM artifact requires an entry.");
                    }
                    image = NativeEmitter.Emit(program,
                        NativeRuntimePolicy.FailFast);
                }
            }
            else
            {
                var providerInputs
                    = new List<TrRmStandardArtifactInput>(providers.Count);
                foreach (var provider in providers)
                {
                    providerInputs.Add(new TrRmStandardArtifactInput(
                        provider.SchemaIdentity,
                        File.ReadAllBytes(provider.Path)));
                }

                var rootInput = new TrRmStandardArtifactInput(schemaIdentity,
                    artifact);
                if (StringComparer.Ordinal.Equals(schemaIdentity,
                        RegisterExceptionRegionsSchemaIdentity))
                {
                    var program = TrRmExecutableCompiler.LoadStandard(rootInput,
                        moduleIdentity!, providerInputs,
                        selectedCoreExceptionIdentitySelections);
                    if (command == "run-trrm")
                    {
                        if (program.EntryPoint < 0)
                        {
                            throw new InvalidDataException(
                                "A library TRRM artifact has no executable entry.");
                        }

                        return NativeJit.Execute(program,
                            NativeRuntimePolicy.FailFast,
                            GraphJitRegistrarAssemblyPath());
                    }
                    else if (command == "build-trrm-library")
                    {
                        if (program.EntryPoint >= 0)
                        {
                            throw new InvalidDataException(
                                "Library emission requires a no-entry TRRM module.");
                        }

                        image = NativeEmitter.EmitLibrary(program, exports,
                            NativeRuntimePolicy.FailFast);
                    }
                    else
                    {
                        if (program.EntryPoint < 0)
                        {
                            throw new InvalidDataException(
                                "An executable TRRM artifact requires an entry.");
                        }

                        image = NativeEmitter.Emit(program,
                            NativeRuntimePolicy.FailFast);
                    }
                }
                else if (command == "run-trrm")
                {
                    try
                    {
                        return NativeModuleCompiler.ExecuteStandard(rootInput,
                            moduleIdentity!, providerInputs,
                            NativeRuntimePolicy.FailFast,
                            GraphJitRegistrarAssemblyPath());
                    }
                    catch (Exception exception)
                        when (StringComparer.Ordinal.Equals(schemaIdentity,
                                A3FixedArraySchemaIdentity)
                            && exception is InvalidDataException
                                or OverflowException or NotSupportedException)
                    {
                        a3AdmissionCallFailed = true;
                        throw;
                    }
                }
                else if (command == "build-trrm-library")
                {
                    try
                    {
                        image = NativeModuleCompiler.EmitStandardLibrary(
                            rootInput, moduleIdentity!, providerInputs, exports,
                            NativeRuntimePolicy.FailFast);
                    }
                    catch (Exception exception)
                        when (StringComparer.Ordinal.Equals(schemaIdentity,
                                A3FixedArraySchemaIdentity)
                            && exception is InvalidDataException
                                or OverflowException or NotSupportedException)
                    {
                        a3AdmissionCallFailed = true;
                        throw;
                    }
                }
                else
                {
                    try
                    {
                        image = NativeModuleCompiler.EmitStandardExecutable(
                            rootInput, moduleIdentity!, providerInputs,
                            NativeRuntimePolicy.FailFast);
                    }
                    catch (Exception exception)
                        when (StringComparer.Ordinal.Equals(schemaIdentity,
                                A3FixedArraySchemaIdentity)
                            && exception is InvalidDataException
                                or OverflowException or NotSupportedException)
                    {
                        a3AdmissionCallFailed = true;
                        throw;
                    }
                }
            }
            var imagePath = output ?? throw new InvalidOperationException("Missing native output path.");
            WriteArtifact(imagePath, image);
            WriteTrRmSuccess(command, imagePath, image.Length, json);
            return 0;
        }
        catch (Exception exception)
            when (TrRmExecutableCompiler.TryGetA3AdmissionFailure(exception,
                out _))
        {
            _ = TrRmExecutableCompiler.TryGetA3AdmissionFailure(exception,
                out var failureKind);
            var (code, status) = failureKind switch
            {
                TrRmA3AdmissionFailureKind.InvalidArtifact
                    => ("TRM5001", 1),
                TrRmA3AdmissionFailureKind.TargetLayoutOverflow
                    => ("TRM5005", 1),
                TrRmA3AdmissionFailureKind.UnsupportedAdmission
                    => ("TRM5006", 1),
                TrRmA3AdmissionFailureKind.ResourceLimit
                    => ("TRM5007", 2),
                _ => throw new InvalidOperationException(
                    "The A3 admission failure kind is not supported.")
            };
            WriteInputError(json, code, exception.Message);
            return status;
        }
        catch (Exception) when (a3AdmissionCallFailed)
        {
            throw;
        }
        catch (Exception exception) when (exception is InvalidDataException or (ArgumentException and not DecoderFallbackException)
            or NotSupportedException)
        {
            WriteInputError(json, "TRM5001", exception.Message);
            return 1;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or DecoderFallbackException or FormatException)
        {
            WriteInputError(json, "TRM2001", exception.Message);
            return 2;
        }
    }

    private static void ValidateSourceGenericMethodInputPaths(
        TrRmSourceGenericMethodExportsFile configuration,
        List<string> sourcePaths, string configurationPath)
    {
        if (configuration.Sources.Length != sourcePaths.Count)
        {
            throw new FormatException(
                $"Invalid source generic method exports '{configurationPath}': source count does not match compile-trrm inputs.");
        }

        for (var index = 0; index < sourcePaths.Count; index++)
        {
            var assertedPath = configuration.Sources[index].Path;
            var sourcePath = sourcePaths[index];
            var matches = assertedPath == "-"
                ? sourcePath == "-"
                : sourcePath != "-"
                    && PathsAlias(assertedPath, sourcePath);
            if (!matches)
            {
                throw new FormatException(
                    $"Invalid source generic method exports '{configurationPath}' at $.sources[{index}].path: source path does not match the ordered compile-trrm input.");
            }
        }
    }

    private static void ValidateSourceGenericMethodSnapshots(
        TrRmSourceGenericMethodExportsFile configuration,
        ImmutableArray<SyntaxTree> snapshots, string configurationPath)
    {
        if (configuration.Sources.Length != snapshots.Length)
        {
            throw new FormatException(
                $"Invalid source generic method exports '{configurationPath}': source count changed during source acquisition.");
        }

        for (var index = 0; index < snapshots.Length; index++)
        {
            var text = snapshots[index].Root.ToFullString();
            var digest = ComputeLowercaseSha256Utf16(text);
            if (!StringComparer.Ordinal.Equals(digest,
                    configuration.Sources[index].SnapshotSha256))
            {
                throw new FormatException(
                    $"Invalid source generic method exports '{configurationPath}' at $.sources[{index}].snapshotSha256: source snapshot does not match.");
            }
        }
    }

    private static void ValidateSourceTypeExportsInputPaths(
        TrRmSourceTypeExportsFile configuration,
        List<string> sourcePaths, string configurationPath)
    {
        if (configuration.Sources.Length != sourcePaths.Count)
        {
            throw new FormatException(
                $"Invalid source type exports '{configurationPath}': source count does not match compile-trrm inputs.");
        }

        for (var index = 0; index < sourcePaths.Count; index++)
        {
            var assertedPath = configuration.Sources[index].Path;
            var sourcePath = sourcePaths[index];
            var matches = assertedPath == "-"
                ? sourcePath == "-"
                : sourcePath != "-"
                    && PathsAlias(assertedPath, sourcePath);
            if (!matches)
            {
                throw new FormatException(
                    $"Invalid source type exports '{configurationPath}' at $.sources[{index}].path: source path does not match the ordered compile-trrm input.");
            }
        }
    }

    private static void ValidateSourceTypeExportsSnapshots(
        TrRmSourceTypeExportsFile configuration,
        ImmutableArray<SyntaxTree> snapshots, string configurationPath)
    {
        if (configuration.Sources.Length != snapshots.Length)
        {
            throw new FormatException(
                $"Invalid source type exports '{configurationPath}': source count changed during source acquisition.");
        }

        for (var index = 0; index < snapshots.Length; index++)
        {
            var text = snapshots[index].Root.ToFullString();
            var digest = ComputeLowercaseSha256Utf16(text);
            if (!StringComparer.Ordinal.Equals(digest,
                    configuration.Sources[index].SnapshotSha256))
            {
                throw new FormatException(
                    $"Invalid source type exports '{configurationPath}' at $.sources[{index}].snapshotSha256: source snapshot does not match.");
            }
        }
    }

    private static string ComputeLowercaseSha256Utf16(string text)
    {
        const string hex = "0123456789abcdef";
        var digest = SHA256.HashData(Encoding.Unicode.GetBytes(text));
        var characters = new char[digest.Length * 2];
        for (var index = 0; index < digest.Length; index++)
        {
            characters[index * 2] = hex[digest[index] >> 4];
            characters[(index * 2) + 1] = hex[digest[index] & 0x0F];
        }
        return new string(characters);
    }

    private static void WriteTrRmSuccess(string command, string path, int bytes, bool json)
    {
        if (!json)
        {
            Console.Out.WriteLine($"{path}: {bytes} bytes ({command}, Windows x64, fail-fast)");
            return;
        }
        using var writer = new Utf8JsonWriter(Console.OpenStandardOutput(),
            new JsonWriterOptions { Indented = true });
        writer.WriteStartObject();
        writer.WriteNumber("schemaVersion", 1);
        writer.WriteString("command", command);
        writer.WriteString("runtimePolicy", "fail-fast");
        writer.WriteBoolean("success", true);
        writer.WriteStartArray("diagnostics");
        writer.WriteEndArray();
        writer.WriteStartObject("artifact");
        writer.WriteString("path", path);
        writer.WriteNumber("length", bytes);
        writer.WriteEndObject();
        writer.WriteEndObject();
    }
}
