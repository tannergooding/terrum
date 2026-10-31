// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;

namespace Terrum.CodeAnalysis.Modules;

internal enum TrRmStandardNativeOperation
{
    EmitExecutable,
    EmitLibrary,
    Execute,
}

internal readonly record struct TrRmStandardModuleSelection(
    int ArtifactIndex, int ModuleRow, string Identity,
    uint TargetRequirement, uint RuntimePolicy, int EntryMethodDefRow,
    int MethodDefStart, int MethodDefEnd);

internal sealed record TrRmStandardNativeProviderBinding(
    string RootTargetName,
    string ProducerIdentity,
    string ExportKey,
    uint TargetRequirement,
    uint RuntimePolicy,
    int RootModuleRow,
    int RootMethodRow,
    int ProviderArtifactIndex,
    int ProviderModuleRow,
    int ProviderMethodRow,
    string ActualLibraryName,
    string ActualExportName);

internal sealed class TrRmStandardNativeBindingPlan
{
    private const string ForeignInvocationSchemaIdentity
        = "trrm/1-draft-foreign-invocation-contracts-1";
    private const string ForeignInvocationStorageAddressSchemaIdentity
        = "trrm/1-draft-foreign-invocation-storage-addresses-1";
    private const string ForeignInvocationStorageFixedArrayCopySchemaIdentity
        = "trrm/1-draft-foreign-invocation-storage-fixed-arrays-2";
    private const string ForeignInvocationStorageGenericFixedArraySchemaIdentity
        = "trrm/1-draft-foreign-invocation-storage-fixed-arrays-3-unique-1";
    private readonly TrRmA3LayoutResourceLedger? _a3LayoutResourceLedger;
    private readonly ImmutableArray<TrRmStandardTypeProviderBinding>
        _typeProviderBindings;
    private readonly ImmutableArray<byte[]> _artifacts;
    private readonly ImmutableArray<string> _schemaIdentities;
    private readonly ImmutableArray<TrRmStandardModuleSelection> _modules;
    private readonly ImmutableArray<TrRmStandardNativeProviderBinding> _providerBindings;

    internal TrRmStandardNativeBindingPlan(BoundProgram rootProgram,
        string moduleIdentity, byte[][] artifacts,
        IReadOnlyList<string> schemaIdentities,
        ImmutableArray<TrRmStandardModuleSelection> modules,
        int rootModuleRow, TrRmStandardNativeOperation operation,
        NativeRuntimePolicy runtimePolicy,
        TrRmStandardNativeProviderBinding? providerBinding,
        ImmutableArray<TrRmStandardTypeProviderBinding> typeProviderBindings,
        TrRmA3LayoutResourceLedger? a3LayoutResourceLedger)
    {
        a3LayoutResourceLedger?.ConsumeWork();
        RootProgram = rootProgram ?? throw new ArgumentNullException(nameof(rootProgram));
        ArgumentException.ThrowIfNullOrEmpty(moduleIdentity);
        ArgumentNullException.ThrowIfNull(artifacts);
        ArgumentNullException.ThrowIfNull(schemaIdentities);
        var schemaIdentityCount = schemaIdentities.Count;
        if (schemaIdentityCount != artifacts.Length)
        {
            throw new ArgumentException(
                "Standard schema selections do not match the owned artifacts.",
                nameof(schemaIdentities));
        }

        _a3LayoutResourceLedger = a3LayoutResourceLedger;
        _a3LayoutResourceLedger?.ConsumeWork(checked(
            (ulong)artifacts.Length + (ulong)schemaIdentityCount + 2UL
            + (providerBinding is null ? 0UL : 2UL)));
        ModuleIdentity = moduleIdentity;
        _artifacts = [.. artifacts];
        _schemaIdentities = [.. schemaIdentities];
        _modules = modules;
        _providerBindings = providerBinding is null ? [] : [providerBinding];
        _typeProviderBindings = typeProviderBindings;
        RootModuleRow = rootModuleRow;
        Operation = operation;
        RuntimePolicy = runtimePolicy;
    }

    internal BoundProgram RootProgram { get; }
    internal string ModuleIdentity { get; }
    internal int RootModuleRow { get; }
    internal TrRmStandardNativeOperation Operation { get; }
    internal NativeRuntimePolicy RuntimePolicy { get; }
    internal TrRmA3AdmissionScope? A3AdmissionScope
        => _a3LayoutResourceLedger?.AdmissionScope;

    internal void ValidateLibraryExports(IReadOnlyList<string> exports)
    {
        ArgumentNullException.ThrowIfNull(exports);
        foreach (var binding in _providerBindings)
        {
            for (var index = 0; index < exports.Count; index++)
            {
                if (StringComparer.Ordinal.Equals(exports[index],
                        binding.RootTargetName)
                    || StringComparer.Ordinal.Equals(exports[index],
                        binding.ExportKey))
                {
                    throw new ArgumentException(
                        "A selected provider Import is not a root-owned library export.",
                        nameof(exports));
                }
            }
        }
    }

    internal (BoundProgram Program, ImmutableArray<NativeImportBinding> Bindings)
        CreateLoweringInputs(TrRmStandardNativeOperation operation)
    {
        _a3LayoutResourceLedger?.ConsumeWork(2);
        if (_schemaIdentities.IsDefaultOrEmpty
            || _schemaIdentities.Length != _artifacts.Length)
        {
            throw new InvalidDataException(
                "The Standard native binding plan has incomplete schema selections.");
        }
        _a3LayoutResourceLedger?.ConsumeWork(5);
        if (operation != Operation || RuntimePolicy != NativeRuntimePolicy.FailFast
            || RootProgram.RuntimePolicy != BoundRuntimePolicy.FailFast
            || _modules.IsDefaultOrEmpty)
        {
            throw new InvalidDataException(
                "The Standard native binding plan has invalid operation or Module context.");
        }

        var hasRootModuleContext = false;
        foreach (var module in _modules)
        {
            _a3LayoutResourceLedger?.ConsumeWork();
            if (module.ArtifactIndex == 0
                && module.ModuleRow == RootModuleRow)
            {
                _a3LayoutResourceLedger?.ConsumeWork(
                    GetStringComparisonWork(module.Identity, ModuleIdentity));
                if (StringComparer.Ordinal.Equals(module.Identity,
                        ModuleIdentity))
                {
                    hasRootModuleContext = true;
                    break;
                }
            }
        }
        if (!hasRootModuleContext)
        {
            throw new InvalidDataException(
                "The Standard native binding plan has invalid operation or Module context.");
        }

        _a3LayoutResourceLedger?.ConsumeWork(
            checked((ulong)_schemaIdentities.Length + 1UL));
        foreach (var schemaIdentity in _schemaIdentities)
        {
            if (!IsSelectedSchemaIdentity(schemaIdentity))
            {
                throw new InvalidDataException(
                    "The Standard native binding plan has an unselected schema.");
            }
        }

        _a3LayoutResourceLedger?.ConsumeWork(
            checked((ulong)_modules.Length + 1UL));
        TrRmStandardModuleSelection? rootSelection = null;
        foreach (var module in _modules)
        {
            _a3LayoutResourceLedger?.ConsumeWork(8);
            if (module.ArtifactIndex < 0
                || module.ArtifactIndex >= _artifacts.Length
                || module.ModuleRow <= 0
                || string.IsNullOrEmpty(module.Identity)
                || module.TargetRequirement != 1
                || module.RuntimePolicy != 0
                || module.EntryMethodDefRow < 0
                || module.MethodDefStart <= 0
                || module.MethodDefEnd < module.MethodDefStart)
            {
                throw new InvalidDataException(
                    "The Standard native binding plan has invalid Module requirements.");
            }

            if (module.ArtifactIndex == 0
                && module.ModuleRow == RootModuleRow)
            {
                rootSelection = module;
            }
        }

        _a3LayoutResourceLedger?.ConsumeWork();
        if (rootSelection is not TrRmStandardModuleSelection selectedRoot
            || !AreEqualOrdinal(selectedRoot.Identity, ModuleIdentity))
        {
            throw new InvalidDataException(
                "The Standard native binding plan lost its selected root Module.");
        }

        if (!_typeProviderBindings.IsDefaultOrEmpty)
        {
            _a3LayoutResourceLedger?.ConsumeWork();
            var rootSchemaIdentity = _schemaIdentities[0];
            if (!IsFixedArraySchemaIdentity(rootSchemaIdentity))
            {
                throw new InvalidDataException(
                    "The Standard native binding plan has fixed-array type bindings under another schema.");
            }

            var rootSchema = GetFixedArraySchema(rootSchemaIdentity);
            _a3LayoutResourceLedger?.ConsumeWork(2);
            var rootIndex = rootSchema
                == TrRmArtifactSchema.ForeignInvocationStorageFixedArrayGenericCopyDraft
                ? TrRmArtifactIndex
                    .ForForeignInvocationStorageFixedArrayGenericCopyDraft(
                        _artifacts[0])
                : TrRmArtifactIndex
                    .ForForeignInvocationStorageFixedArrayCopyDraft(
                        _artifacts[0]);
            var rootTables = rootSchema
                == TrRmArtifactSchema.ForeignInvocationStorageFixedArrayGenericCopyDraft
                ? TrRmStructuralMetadataValidator
                    .ValidateForeignInvocationStorageFixedArrayGenericCopyDraftAndGetExecutionTables(
                        rootIndex, _a3LayoutResourceLedger,
                        createA3ResourceLedger: _a3LayoutResourceLedger is null)
                : TrRmStructuralMetadataValidator
                    .ValidateForeignInvocationStorageFixedArrayCopyDraftAndGetExecutionTables(
                        rootIndex);
            _a3LayoutResourceLedger?.ConsumeWork(checked(
                (ulong)_artifacts.Length + (ulong)_schemaIdentities.Length
                + 2UL));
            var typeProviderBindings = TrRmFixedArrayProviderBindingValidator
                .Validate(rootTables, [.. _artifacts], [.. _schemaIdentities],
                    _modules, RootModuleRow);
            var typeProviderBindingsMatch
                = typeProviderBindings.Length == _typeProviderBindings.Length;
            _a3LayoutResourceLedger?.ConsumeWork();
            if (typeProviderBindingsMatch)
            {
                _a3LayoutResourceLedger?.ConsumeWork(checked(
                    (ulong)typeProviderBindings.Length + 1UL));
                for (var index = 0; index < typeProviderBindings.Length; index++)
                {
                    var expectedBinding = _typeProviderBindings[index];
                    var actualBinding = typeProviderBindings[index];
                    _a3LayoutResourceLedger?.ConsumeWork(checked(9UL
                        + GetStringComparisonWork(
                            expectedBinding.ProducerIdentity,
                            actualBinding.ProducerIdentity)
                        + GetStringComparisonWork(expectedBinding.ExportKey,
                            actualBinding.ExportKey)));
                    if (expectedBinding != actualBinding)
                    {
                        typeProviderBindingsMatch = false;
                        break;
                    }
                }
            }

            if (!typeProviderBindingsMatch)
            {
                var exception = new InvalidDataException(
                    "The Standard native binding plan changed a fixed-array type provider binding.");
                _a3LayoutResourceLedger?.AdmissionScope.Mark(exception,
                    TrRmA3AdmissionFailureKind.InvalidArtifact);
                throw exception;
            }
        }

        foreach (var binding in _providerBindings)
        {
            if (binding.RootModuleRow != RootModuleRow
                || binding.RootMethodRow < selectedRoot.MethodDefStart
                || binding.RootMethodRow >= selectedRoot.MethodDefEnd)
            {
                throw new InvalidDataException(
                    "The Standard native binding plan has an invalid root Import origin.");
            }

            var foundProviderModule = false;
            foreach (var module in _modules)
            {
                if (module.ArtifactIndex == binding.ProviderArtifactIndex
                    && module.ModuleRow == binding.ProviderModuleRow
                    && StringComparer.Ordinal.Equals(module.Identity,
                        binding.ProducerIdentity)
                    && module.TargetRequirement == binding.TargetRequirement
                    && module.RuntimePolicy == binding.RuntimePolicy
                    && binding.ProviderMethodRow >= module.MethodDefStart
                    && binding.ProviderMethodRow < module.MethodDefEnd)
                {
                    foundProviderModule = true;
                    break;
                }
            }

            if (!foundProviderModule)
            {
                throw new InvalidDataException(
                    "The Standard native binding plan has an invalid provider origin.");
            }
        }

        var functions = RootProgram.Functions.ToBuilder();
        var providerBindingsByTarget = new Dictionary<string,
            TrRmStandardNativeProviderBinding>(
                _providerBindings.Length, StringComparer.Ordinal);
        foreach (var binding in _providerBindings)
        {
            if (string.IsNullOrEmpty(binding.RootTargetName)
                || string.IsNullOrEmpty(binding.ProducerIdentity)
                || string.IsNullOrEmpty(binding.ExportKey)
                || string.IsNullOrEmpty(binding.ActualLibraryName)
                || string.IsNullOrEmpty(binding.ActualExportName)
                || binding.ProviderArtifactIndex < 0
                || binding.ProviderArtifactIndex >= _artifacts.Length
                || binding.ProviderModuleRow <= 0
                || binding.ProviderMethodRow <= 0
                || (binding.ProviderArtifactIndex == 0
                    && binding.ProviderModuleRow == RootModuleRow)
                || !providerBindingsByTarget.TryAdd(binding.RootTargetName,
                    binding))
            {
                throw new InvalidDataException(
                    "The Standard native binding plan contains an invalid provider origin.");
            }
        }

        var resolvedTargets = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < functions.Count; index++)
        {
            var function = functions[index];
            if (function.ModuleImport is null)
            {
                if (function.IsModuleReference)
                {
                    throw new InvalidDataException(
                        "A module-reference marker has no retained provider expectation.");
                }

                continue;
            }

            if (!providerBindingsByTarget.TryGetValue(function.Name,
                    out var binding)
                || !StringComparer.Ordinal.Equals(
                    function.ModuleImport.ProducerIdentity,
                    binding.ProducerIdentity)
                || !StringComparer.Ordinal.Equals(
                    function.ModuleImport.ExportKey, binding.ExportKey)
                || function.ModuleImport.TargetRequirement
                    != binding.TargetRequirement
                || function.ModuleImport.RuntimePolicy != binding.RuntimePolicy
                || function.DirectNativeImport is not DirectNativeImport import
                || !StringComparer.Ordinal.Equals(import.LibraryName,
                    binding.ActualLibraryName)
                || !StringComparer.Ordinal.Equals(import.ExportName,
                    binding.ActualExportName)
                || !function.IsExternal || !function.IsModuleReference)
            {
                throw new InvalidDataException(
                    "The Standard native binding plan no longer matches its root Import.");
            }

            if (!resolvedTargets.Add(function.Name))
            {
                throw new InvalidDataException(
                    "The Standard native binding plan resolves one root target more than once.");
            }

            functions[index] = function with {
                ModuleImport = null,
                IsModuleReference = false,
            };
        }

        if (resolvedTargets.Count != providerBindingsByTarget.Count)
        {
            throw new InvalidDataException(
                "The Standard native binding plan has an unconsumed provider origin.");
        }

        var program = RootProgram with {
            Functions = functions.ToImmutable(),
        };
        if (program.EntryPoint < 0)
        {
            SemanticVerifier.VerifyLibraryWithDirectNativeImports(program);
        }
        else
        {
            SemanticVerifier.VerifyWithDirectNativeImports(program);
        }

        var nativeBindings = ImmutableArray.CreateBuilder<NativeImportBinding>();
        foreach (var function in program.Functions)
        {
            if (!function.IsExternal)
            {
                continue;
            }

            if (function.DirectNativeImport is not DirectNativeImport import)
            {
                throw new NotSupportedException(
                    "The Standard native binding plan supports direct NativeImport declarations only.");
            }

            if (providerBindingsByTarget.TryGetValue(function.Name,
                    out var provider))
            {
                nativeBindings.Add(new NativeImportBinding(function.Name,
                    provider.ActualLibraryName, provider.ActualExportName));
            }
            else
            {
                nativeBindings.Add(new NativeImportBinding(function.Name,
                    import.LibraryName, import.ExportName));
            }
        }

        if (nativeBindings.Count == 0)
        {
            var exception = new NotSupportedException(
                "The Standard native binding plan contains no direct NativeImport.");
            _a3LayoutResourceLedger?.AdmissionScope.Mark(exception,
                TrRmA3AdmissionFailureKind.UnsupportedAdmission);
            throw exception;
        }

        return (program, nativeBindings.ToImmutable());
    }

    private bool IsSelectedSchemaIdentity(string? schemaIdentity)
        => AreEqualOrdinal(schemaIdentity, ForeignInvocationSchemaIdentity)
            || AreEqualOrdinal(schemaIdentity,
                ForeignInvocationStorageAddressSchemaIdentity)
            || AreEqualOrdinal(schemaIdentity,
                ForeignInvocationStorageFixedArrayCopySchemaIdentity)
            || AreEqualOrdinal(schemaIdentity,
                ForeignInvocationStorageGenericFixedArraySchemaIdentity);

    private bool IsFixedArraySchemaIdentity(string? schemaIdentity)
        => AreEqualOrdinal(schemaIdentity,
                ForeignInvocationStorageFixedArrayCopySchemaIdentity)
            || AreEqualOrdinal(schemaIdentity,
                ForeignInvocationStorageGenericFixedArraySchemaIdentity);

    private TrRmArtifactSchema GetFixedArraySchema(string? schemaIdentity)
    {
        if (AreEqualOrdinal(schemaIdentity,
                ForeignInvocationStorageGenericFixedArraySchemaIdentity))
        {
            return TrRmArtifactSchema.ForeignInvocationStorageFixedArrayGenericCopyDraft;
        }

        if (AreEqualOrdinal(schemaIdentity,
                ForeignInvocationStorageFixedArrayCopySchemaIdentity))
        {
            return TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyDraft;
        }

        throw new InvalidDataException(
            "The Standard native binding plan has an unselected fixed-array schema.");
    }

    private bool AreEqualOrdinal(string? left, string right)
    {
        _a3LayoutResourceLedger?.ConsumeWork(
            GetStringComparisonWork(left, right));
        return StringComparer.Ordinal.Equals(left, right);
    }

    private static ulong GetStringComparisonWork(string? left, string? right)
    {
        var characterCount = checked((ulong)(left?.Length ?? 0)
            + (ulong)(right?.Length ?? 0));
        return checked((characterCount * sizeof(char)) + 1UL);
    }
}
