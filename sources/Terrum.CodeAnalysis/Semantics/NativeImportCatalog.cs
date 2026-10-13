// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace Terrum.CodeAnalysis.Semantics;

/// <summary>A compiler-owned catalog of reviewed Windows x64 native imports.</summary>
public sealed class NativeImportCatalog
{
    private static readonly NativeImportAttributeDefinition s_attributeDefinition =
        new("Terrum.Interop.NativeImport", ArgumentCount: 1, RequiresStringLiteral: true, IsRepeatable: false);

    private static readonly NativeImportCatalog s_windowsX64 = new(
        "windows-x64-native-imports-v3",
        [
            new NativeImportDefinition(
                "windows-x64.system32.kernel32.ExitProcess",
                "kernel32.dll",
                "ExitProcess",
                [PrimitiveType.UInt32],
                PrimitiveType.Void,
                IsNoThrow: true,
                RequiresUnsafeCaller: false,
                DoesNotReturn: true,
                DeploymentPolicy: NativeImportDeploymentPolicy.System32),
            new NativeImportDefinition(
                "windows-x64.system32.kernel32.GetCurrentThreadId",
                "kernel32.dll",
                "GetCurrentThreadId",
                [],
                PrimitiveType.UInt32,
                IsNoThrow: true,
                RequiresUnsafeCaller: false,
                DoesNotReturn: false,
                DeploymentPolicy: NativeImportDeploymentPolicy.System32),
            new NativeImportDefinition(
                "windows-x64.system32.kernel32.Sleep",
                "kernel32.dll",
                "Sleep",
                [PrimitiveType.UInt32],
                PrimitiveType.Void,
                IsNoThrow: true,
                RequiresUnsafeCaller: false,
                DoesNotReturn: false,
                DeploymentPolicy: NativeImportDeploymentPolicy.System32),
            new NativeImportDefinition(
                "windows-x64.system32.user32.MessageBoxW",
                "user32.dll",
                "MessageBoxW",
                [PrimitiveType.NativeWindowHandle, PrimitiveType.NativeUtf16ZPointer,
                    PrimitiveType.NativeUtf16ZPointer, PrimitiveType.UInt32],
                PrimitiveType.Int32,
                IsNoThrow: true,
                RequiresUnsafeCaller: false,
                DoesNotReturn: false,
                DeploymentPolicy: NativeImportDeploymentPolicy.System32) {
                ParameterContracts = [
                    NativeImportParameterContract.NoOwnerWindowHandle,
                    NativeImportParameterContract.ReadonlyUtf16ZNoEscape,
                    NativeImportParameterContract.ReadonlyUtf16ZNoEscape,
                    NativeImportParameterContract.Value,
                ],
            },
        ]);

    private readonly ImmutableDictionary<string, NativeImportDefinition> _imports;
    private readonly ImmutableHashSet<string> _callbackTypeNames;
    private readonly ImmutableDictionary<string, NativeComInterfaceDefinition> _comInterfaces;
    private readonly ImmutableDictionary<string, NativeComOwnerDefinition> _comOwners;
    private readonly ImmutableDictionary<string, NativeComMethodDefinition> _comMethods;
    private readonly ImmutableArray<NativeFixedArrayStorageLayoutDefinition> _fixedArrayStorageLayouts;
    private readonly ImmutableArray<NativeStorageRecordLayoutDefinition> _storageRecordLayouts;

    [SuppressMessage("Style", "IDE0072:Populate switch",
        Justification = "Unsupported native parameter types cannot acquire an import contract.")]
    private static bool ValidParameterContracts(NativeImportDefinition import, bool isForTesting)
    {
        var hasRestrictedContract = import.ParameterTypes.Contains(PrimitiveType.NativeUtf16ZPointer)
            || import.ParameterTypes.Contains(PrimitiveType.NativeWindowHandle);
        if (!hasRestrictedContract)
        {
            return import.ParameterContracts.IsDefaultOrEmpty;
        }
        if (import.ParameterContracts.IsDefault || import.ParameterContracts.Length != import.ParameterTypes.Length)
        {
            return false;
        }
        for (var index = 0; index < import.ParameterTypes.Length; index++)
        {
            var type = import.ParameterTypes[index];
            if (type == PrimitiveType.NativeCallbackAddress)
            {
                if (!NativeCallbackParameterContract.TryGetParameter(import, index, out _))
                {
                    return false;
                }
                continue;
            }
            if (type == PrimitiveType.NativeComInterfaceOutputAddress
                || import.ParameterContracts[index] == NativeImportParameterContract.OwnedNativeComInterfaceOutput)
            {
                if (type != PrimitiveType.NativeComInterfaceOutputAddress
                    || import.ParameterContracts[index] != NativeImportParameterContract.OwnedNativeComInterfaceOutput)
                {
                    return false;
                }
                continue;
            }
            var expected = type switch {
                PrimitiveType.NativeUtf16ZPointer => NativeImportParameterContract.ReadonlyUtf16ZNoEscape,
                PrimitiveType.NativeWindowHandle => NativeImportParameterContract.NoOwnerWindowHandle,
                _ => NativeImportParameterContract.Value,
            };
            if (import.ParameterContracts[index] != expected)
            {
                return false;
            }
        }
        return true;
    }

    internal NativeImportCatalog(string identity, ImmutableArray<NativeImportDefinition> imports,
        bool isForTesting = false,
        ImmutableArray<NativeComInterfaceDefinition> comInterfaces = default,
        ImmutableArray<NativeComOwnerDefinition> comOwners = default,
        ImmutableArray<NativeComMethodDefinition> comMethods = default,
        ImmutableArray<(string Source, string Target)> comBaseEdges = default,
        ImmutableArray<NativeComSignatureTypeDefinition> comSignatureTypes = default,
        ImmutableArray<NativeFixedArrayStorageLayoutDefinition> fixedArrayStorageLayouts = default,
        ImmutableArray<NativeStorageRecordLayoutDefinition> nativeStorageRecordLayouts = default)
    {
        if (string.IsNullOrWhiteSpace(identity) || imports.IsDefault
            || (imports.IsEmpty && !isForTesting))
        {
            throw new ArgumentException("A native import catalog requires an identity and at least one entry.",
                nameof(identity));
        }

        var hasComMetadata = !comInterfaces.IsDefault || !comOwners.IsDefault
            || !comMethods.IsDefault || !comBaseEdges.IsDefault || !comSignatureTypes.IsDefault;
        if (hasComMetadata && (comInterfaces.IsDefault || comOwners.IsDefault
                || comMethods.IsDefault || comBaseEdges.IsDefault))
        {
            throw new ArgumentException("The native COM metadata is incomplete or inconsistent.",
                nameof(comInterfaces));
        }
        var signatureTypes = comSignatureTypes.IsDefault ? [] : comSignatureTypes;
        if (hasComMetadata && !NativeComCatalog.IsValid(
            comInterfaces, comOwners, comMethods, comBaseEdges, imports, signatureTypes, out var failureReason))
        {
            throw new ArgumentException($"The native COM metadata is invalid: {failureReason}",
                nameof(comInterfaces));
        }
        var storageLayouts = fixedArrayStorageLayouts.IsDefault ? [] : fixedArrayStorageLayouts;
        if (storageLayouts.Any(static layout => layout is null
                || string.IsNullOrWhiteSpace(layout.ElementTypeName)
                || layout.ElementCount <= 0 || layout.ElementStride <= 0
                || layout.ElementAlignment <= 0
                || (layout.ElementAlignment & (layout.ElementAlignment - 1)) != 0
                || layout.ElementStride % layout.ElementAlignment != 0
                || layout.ElementCount > NativeFixedArrayContract.MaximumByteExtent / layout.ElementStride
                || layout.ByteExtent != layout.ElementCount * layout.ElementStride
                || !NativeFixedArrayContract.IsValidElementFields(layout.ElementFields, layout.ElementStride)
                || (layout.AllowsElementWrites && !layout.AllowsMutableLocal)
                || (layout.AllowsReadonlyUtf16ZLoan
                    && (layout.ElementTypeName != nameof(PrimitiveType.Char16)
                        || layout.ElementStride != sizeof(char)))
                || (layout.AllowsCheckedRuntimeAccess && !layout.AllowsMutableLocal)))
        {
            throw new ArgumentException("A fixed-array storage layout has invalid element geometry.",
                nameof(fixedArrayStorageLayouts));
        }
        var storageRecordLayouts = nativeStorageRecordLayouts.IsDefault ? [] : nativeStorageRecordLayouts;
        if (storageRecordLayouts.Any(static layout => layout is null
                || !NativeStorageRecordContract.IsValidLayout(layout))
            || storageRecordLayouts.Select(static layout => layout.TypeName)
                .Distinct(StringComparer.Ordinal).Count() != storageRecordLayouts.Length
            || storageRecordLayouts.Select(static layout => layout.InitializerName)
                .Distinct(StringComparer.Ordinal).Count() != storageRecordLayouts.Length)
        {
            throw new ArgumentException("A native storage record layout is invalid or duplicated.",
                nameof(nativeStorageRecordLayouts));
        }
        foreach (var import in imports)
        {
            ArgumentNullException.ThrowIfNull(import);
            if (!import.StorageRecordParameters.IsDefaultOrEmpty
                && import.StorageRecordParameters.Any(parameter => parameter is not null
                    && !storageRecordLayouts.Any(layout => layout.TypeName == parameter.TypeName)))
            {
                throw new ArgumentException("A native storage record parameter has no selected layout.",
                    nameof(imports));
            }
            var loanGroups = (import.StorageRecordParameters.IsDefault
                    ? ImmutableArray<NativeStorageRecordParameterDefinition>.Empty
                    : import.StorageRecordParameters)
                .Where(static parameter => parameter?.LoanGroup is not null)
                .GroupBy(static parameter => parameter!.LoanGroup, StringComparer.Ordinal);
            if (loanGroups.Any(static group => group.Count() < 2))
            {
                throw new ArgumentException("A native storage record loan group requires multiple parameters.",
                    nameof(imports));
            }
        }
        if (hasComMetadata && comMethods.Any(method => !method.StorageRecordParameters.IsDefaultOrEmpty
                && (method.StorageRecordParameters.Select(static parameter => parameter?.ParameterIndex)
                        .Distinct().Count() != method.StorageRecordParameters.Length
                    || method.StorageRecordParameters.Any(parameter =>
                        !NativeStorageRecordContract.IsValidComParameter(parameter, method, storageRecordLayouts))
                    || method.StorageRecordParameters.Where(static parameter => parameter?.LoanGroup is not null)
                        .GroupBy(static parameter => parameter!.LoanGroup, StringComparer.Ordinal)
                        .Any(static group => group.Count() < 2))))
        {
            throw new ArgumentException("A COM storage-record parameter has no compatible selected layout.",
                nameof(comMethods));
        }
        if (!hasComMetadata && imports.Any(static import => import is not null
            && ((!import.ParameterTypes.IsDefault
                    && import.ParameterTypes.Contains(PrimitiveType.NativeComInterfaceOutputAddress))
                || !import.ParameterContracts.IsDefault
                    && import.ParameterContracts.Contains(
                        NativeImportParameterContract.OwnedNativeComInterfaceOutput)
                || !import.ComOutputs.IsDefaultOrEmpty)))
        {
            throw new ArgumentException("Native COM output metadata requires matching COM catalog metadata.",
                nameof(imports));
        }

        var builder = ImmutableDictionary.CreateBuilder<string, NativeImportDefinition>(StringComparer.Ordinal);
        foreach (var import in imports)
        {
            ArgumentNullException.ThrowIfNull(import);
            if (string.IsNullOrEmpty(import.Key) || string.IsNullOrEmpty(import.DllName)
                || string.IsNullOrEmpty(import.ExportName) || import.ParameterTypes.IsDefault
                || import.ReturnType is not (PrimitiveType.Void or PrimitiveType.Int32
                    or PrimitiveType.UInt32 or PrimitiveType.Int64 or PrimitiveType.UInt64)
                || import.ParameterTypes.Any(static type => type is not (PrimitiveType.Int32
                    or PrimitiveType.UInt32 or PrimitiveType.Int64 or PrimitiveType.UInt64
                    or PrimitiveType.NativeUtf16ZPointer or PrimitiveType.NativeWindowHandle))
                || !ValidParameterContracts(import)
                || import.DeploymentPolicy is not (NativeImportDeploymentPolicy.System32
                    or NativeImportDeploymentPolicy.ApplicationDirectory)
                || builder.ContainsKey(import.Key))
            {
                throw new ArgumentException(
                    $"Native import catalog entry '{import.Key}' is incomplete, disallowed, or duplicated.",
                    nameof(imports));
            }
            builder.Add(import.Key, import);
        }

        var callbackTypeNames = ImmutableHashSet.CreateBuilder<string>(StringComparer.Ordinal);
        foreach (var import in builder.Values)
        {
            foreach (var callbackParameter in import.CallbackParameters)
            {
                if (callbackParameter is not null)
                {
                    _ = callbackTypeNames.Add(callbackParameter.SourceTypeName);
                }
            }
        }
        Identity = identity;
        IsForTesting = isForTesting;
        _imports = builder.ToImmutable();
        _callbackTypeNames = callbackTypeNames.ToImmutable();
        _comInterfaces = (comInterfaces.IsDefault ? [] : comInterfaces)
            .ToImmutableDictionary(static definition => definition.NominalIdentity, StringComparer.Ordinal);
        _comOwners = (comOwners.IsDefault ? [] : comOwners)
            .ToImmutableDictionary(static definition => definition.BoundOwnerName, StringComparer.Ordinal);
        _comMethods = (comMethods.IsDefault ? [] : comMethods)
            .ToImmutableDictionary(static definition => definition.Identity, StringComparer.Ordinal);
        ComInterfaces = comInterfaces.IsDefault ? [] : comInterfaces;
        ComOwners = comOwners.IsDefault ? [] : comOwners;
        ComMethods = comMethods.IsDefault ? [] : comMethods;
        ComBaseEdges = comBaseEdges.IsDefault ? [] : comBaseEdges;
        ComSignatureTypes = signatureTypes;
        _fixedArrayStorageLayouts = storageLayouts;
        _storageRecordLayouts = storageRecordLayouts;
    }

    /// <summary>Gets the immutable reviewed Windows x64 system-import catalog.</summary>
    public static NativeImportCatalog WindowsX64 => s_windowsX64;

    /// <summary>Gets the versioned identity of this catalog.</summary>
    public string Identity { get; }

    internal bool IsForTesting { get; }

    internal ImmutableArray<NativeImportDefinition> ImportDefinitions => [.. _imports.Values];

    internal ImmutableArray<NativeComInterfaceDefinition> ComInterfaces { get; }

    internal ImmutableArray<NativeComOwnerDefinition> ComOwners { get; }

    internal ImmutableArray<NativeComMethodDefinition> ComMethods { get; }

    internal ImmutableArray<(string Source, string Target)> ComBaseEdges { get; }

    internal ImmutableArray<NativeComSignatureTypeDefinition> ComSignatureTypes { get; }

    internal ImmutableArray<NativeFixedArrayStorageLayoutDefinition> FixedArrayStorageLayouts =>
        _fixedArrayStorageLayouts;

    internal ImmutableArray<NativeStorageRecordLayoutDefinition> StorageRecordLayouts =>
        _storageRecordLayouts;

    internal static NativeImportAttributeDefinition AttributeDefinition => s_attributeDefinition;

    internal bool SupportsNativeFixedArrayStorage => !_fixedArrayStorageLayouts.IsEmpty
        || _imports.Values.Any(static import => import.StorageParameters.Any(parameter =>
            parameter is not null && parameter.AddressType == PrimitiveType.NativeStorageAddress
                && NativeFixedArrayContract.IsCarrier(parameter.CarrierType)));

    internal bool SupportsFixedArrayStorage(PrimitiveType carrierType) =>
        _imports.Values.Any(import => import.StorageParameters.Any(parameter =>
            parameter is not null && parameter.AddressType == PrimitiveType.NativeStorageAddress
                && parameter.CarrierType == carrierType));

    internal bool SupportsFixedArrayStorage(BoundNativeFixedArrayLayout layout,
        IReadOnlyList<BoundStruct> structures)
        => HasFixedArrayStorageLayout(layout, structures, static _ => true);

    internal bool AllowsEmptyFixedArrayInitialization(BoundNativeFixedArrayLayout layout,
        IReadOnlyList<BoundStruct> structures) =>
        HasFixedArrayStorageLayout(layout, structures, static definition => definition.AllowsEmptyInitialization);

    internal bool AllowsMutableFixedArrayLocal(BoundNativeFixedArrayLayout layout,
        IReadOnlyList<BoundStruct> structures) =>
        HasFixedArrayStorageLayout(layout, structures, static definition => definition.AllowsMutableLocal);

    internal bool AllowsFixedArrayElementWrites(BoundNativeFixedArrayLayout layout,
        IReadOnlyList<BoundStruct> structures) =>
        HasFixedArrayStorageLayout(layout, structures, static definition => definition.AllowsElementWrites);

    internal bool AllowsFixedArrayUInt32Indices(BoundNativeFixedArrayLayout layout,
        IReadOnlyList<BoundStruct> structures) =>
        HasFixedArrayStorageLayout(layout, structures, static definition => definition.AllowsUInt32Indices);

    internal bool AllowsCheckedFixedArrayAccess(BoundNativeFixedArrayLayout layout,
        IReadOnlyList<BoundStruct> structures) =>
        HasFixedArrayStorageLayout(layout, structures, static definition => definition.AllowsCheckedRuntimeAccess);

    internal bool AllowsReadonlyUtf16ZFixedArrayLoan(BoundNativeFixedArrayLayout layout,
        IReadOnlyList<BoundStruct> structures) =>
        HasFixedArrayStorageLayout(layout, structures, static definition => definition.AllowsReadonlyUtf16ZLoan);

    private bool HasFixedArrayStorageLayout(BoundNativeFixedArrayLayout layout,
        IReadOnlyList<BoundStruct> structures, Func<NativeFixedArrayStorageLayoutDefinition, bool> predicate)
    {
        var elementTypeName = NativeFixedArrayContract.ElementTypeName(layout.ElementType, structures);
        return elementTypeName is not null
            && (_fixedArrayStorageLayouts.Any(definition =>
                    predicate(definition) && definition.ElementTypeName == elementTypeName
                    && definition.ElementCount == layout.ElementCount
                    && definition.ElementStride == layout.ElementStride
                    && definition.ElementAlignment == layout.Alignment
                    && definition.ByteExtent == layout.ByteExtent
                    && NativeFixedArrayContract.MatchesElementFields(
                        layout.ElementType, structures, definition.ElementFields))
                || _imports.Values.Any(import => !import.StorageParameters.IsDefaultOrEmpty
                    && import.StorageParameters.Any(parameter => parameter is not null
                        && parameter.AddressType == PrimitiveType.NativeStorageAddress
                        && parameter.CarrierType == layout.CarrierType
                        && parameter.ElementTypeName == elementTypeName
                        && parameter.ElementCount == layout.ElementCount
                        && parameter.ElementStride == layout.ElementStride
                        && parameter.ElementAlignment == layout.Alignment
                        && parameter.ByteExtent == layout.ByteExtent
                        && NativeFixedArrayContract.MatchesElementFields(
                            layout.ElementType, structures, parameter.ElementFields)
                        && predicate(new NativeFixedArrayStorageLayoutDefinition(
                            parameter.ElementTypeName, parameter.ElementCount, parameter.ElementStride,
                            parameter.ElementAlignment, parameter.ByteExtent) {
                            ElementFields = parameter.ElementFields,
                            AllowsMutableLocal = !parameter.IsReadonly,
                            AllowsElementWrites = !parameter.IsReadonly,
                        }))));
    }

    internal bool SupportsFixedArrayElement(string elementTypeName, int elementStride, int elementAlignment) =>
        _fixedArrayStorageLayouts.Any(definition => definition.ElementTypeName == elementTypeName
            && definition.ElementStride == elementStride && definition.ElementAlignment == elementAlignment)
        || _imports.Values.Any(import => import.StorageParameters.Any(parameter =>
            parameter is not null && parameter.AddressType == PrimitiveType.NativeStorageAddress
                && parameter.CarrierType == PrimitiveType.NativeFixedArray
                && parameter.ElementTypeName == elementTypeName
                && parameter.ElementStride == elementStride
                && parameter.ElementAlignment == elementAlignment));

    internal bool TryGetImport(string key, out NativeImportDefinition? definition)
        => _imports.TryGetValue(key, out definition);

    internal static bool IsNullPointerParameter(NativeImportDefinition import, int parameter) =>
        (uint)parameter < (uint)import.ParameterTypes.Length
        && import.ParameterTypes[parameter] == PrimitiveType.NativeNullPointer
        && !import.ParameterContracts.IsDefaultOrEmpty
        && parameter < import.ParameterContracts.Length
        && import.ParameterContracts[parameter] == NativeImportParameterContract.NullPointerOnly;

    internal static bool IsNullPointerArgument(BoundExpression argument, BoundFunction target,
        int parameter, BoundProgram program) =>
        target.IsExternal && target.NativeImportKey is string key
        && program.NativeImportCatalog is NativeImportCatalog catalog
        && catalog.TryGetImport(key, out var import) && import is not null
        && IsNullPointerParameter(import, parameter)
        && argument is BoundConstant { Type: PrimitiveType.NativeNullPointer, Value: 0 };

    internal ImmutableHashSet<string> CallbackTypeNames => _callbackTypeNames;

    internal bool IsCallbackTypeName(string typeName) => _callbackTypeNames.Contains(typeName);

    internal bool MatchesCallbackFunction(PrimitiveType callbackType, BoundFunction function)
    {
        foreach (var import in _imports.Values)
        {
            foreach (var parameter in import.CallbackParameters)
            {
                if (parameter is not null && parameter.Signature.CallbackType == callbackType
                    && NativeCallbackContract.MatchesFunction(function, parameter.Signature))
                {
                    return true;
                }
            }
        }

        return false;
    }

    internal bool TryGetComInterface(string identity, out NativeComInterfaceDefinition? definition)
        => _comInterfaces.TryGetValue(identity, out definition);

    internal bool TryGetComOwner(string boundOwnerName, out NativeComOwnerDefinition? definition)
        => _comOwners.TryGetValue(boundOwnerName, out definition);

    internal bool TryGetComMethod(string identity, out NativeComMethodDefinition? definition)
        => _comMethods.TryGetValue(identity, out definition);

    internal bool IsComAssignable(string sourceIdentity, string targetIdentity) =>
        NativeComCatalog.IsAssignable(sourceIdentity, targetIdentity, _comInterfaces);

    internal bool IsComBaseEdgePermitted(string sourceIdentity, string targetIdentity) =>
        NativeComCatalog.IsPermittedBaseEdge(sourceIdentity, targetIdentity, ComInterfaces, ComBaseEdges);

    internal bool SupportsParameterType(PrimitiveType type) =>
        _imports.Values.Any(import => import.ParameterTypes.Contains(type))
            || (type == PrimitiveType.NativeWindowHandle
                && _imports.Values.Any(static import =>
                    !import.CallbackParameters.IsDefaultOrEmpty
                    && import.CallbackParameters.Any(static parameter => parameter is not null
                        && parameter.Signature.ParameterTypes.Contains(PrimitiveType.NativeWindowHandle))));

    internal bool MatchesFunction(BoundFunction function, NativeImportDefinition definition,
        ImmutableArray<BoundStruct> structures = default)
    {
        if (!function.IsExternal || function.NativeImportKey != definition.Key
            || function.IsConstructor || function.IsClassConstructor
            || function.IsOperator || function.IsCleanup || function.PropertyName is not null
            || (function.DeclaringStruct is not null && !function.IsStatic)
            || function.ReturnType != definition.ReturnType
            || function.ParameterCount != definition.ParameterTypes.Length
            || function.Variables.Length != function.ParameterCount
            || function.IsNoThrow != definition.IsNoThrow
            || function.RequiresUnsafeCaller != definition.RequiresUnsafeCaller
            || function.DoesNotReturn != definition.DoesNotReturn
            || !function.Body.Statements.IsEmpty)
        {
            return false;
        }

        for (var parameter = 0; parameter < function.ParameterCount; parameter++)
        {
            var variable = function.Variables[parameter];
            var isComOutput = definition.ParameterTypes[parameter]
                == PrimitiveType.NativeComInterfaceOutputAddress;
            if ((isComOutput
                    ? !MatchesComOutput(variable, definition, parameter, structures)
                    : variable.Type != definition.ParameterTypes[parameter] || variable.IsOut)
                || variable.IsInit || variable.IsMutable
                || (definition.ParameterTypes[parameter] == PrimitiveType.NativeUtf16ZPointer
                    && definition.ParameterContracts[parameter]
                        != NativeImportParameterContract.ReadonlyUtf16ZNoEscape)
                || (definition.ParameterTypes[parameter] == PrimitiveType.NativeUtf8ZPointer
                    && definition.ParameterContracts[parameter]
                        != NativeImportParameterContract.ReadonlyUtf8ZNoEscape)
                || (definition.ParameterTypes[parameter] == PrimitiveType.NativeWindowHandle
                    && (parameter != 0 || definition.ParameterContracts[parameter]
                        != NativeImportParameterContract.NoOwnerWindowHandle)))
            {
                return false;
            }
        }

        return true;
    }

    private bool MatchesComOutput(BoundVariable variable, NativeImportDefinition definition,
        int parameter, ImmutableArray<BoundStruct> structures)
    {
        if (!variable.IsOut || variable.IsInit || variable.IsMutable || structures.IsDefault
            || definition.ParameterContracts.IsDefault
            || parameter >= definition.ParameterContracts.Length
            || definition.ParameterContracts[parameter]
                != NativeImportParameterContract.OwnedNativeComInterfaceOutput)
        {
            return false;
        }

        var program = new BoundProgram([], -1) {
            Structures = structures,
            NativeImportCatalog = this,
        };
        NativeComOwnerDefinition? owner;
        try
        {
            owner = NativeComCallContract.Owner(variable.Type, program);
        }
        catch (ArgumentException)
        {
            return false;
        }
        return owner is not null
            && owner.AcquisitionKind == NativeComOwnerAcquisitionKind.NativeImportOutput
            && owner.AcquisitionIdentity == definition.Key
            && owner.OutputParameterIndex == parameter
            && definition.ComOutputs.Any(output => output.ParameterIndex == parameter
                && output.InterfaceIdentity == owner.InterfaceIdentity
                && output.Effects.HasFlag(NativeComEffects.AcquiresReference));
    }

    internal static NativeImportCatalog CreateForTesting(string identity,
        ImmutableArray<NativeImportDefinition> imports,
        ImmutableArray<NativeComInterfaceDefinition> comInterfaces = default,
        ImmutableArray<NativeComOwnerDefinition> comOwners = default,
        ImmutableArray<NativeComMethodDefinition> comMethods = default,
        ImmutableArray<(string Source, string Target)> comBaseEdges = default,
        ImmutableArray<NativeComSignatureTypeDefinition> comSignatureTypes = default,
        ImmutableArray<NativeFixedArrayStorageLayoutDefinition> fixedArrayStorageLayouts = default,
        ImmutableArray<NativeStorageRecordLayoutDefinition> storageRecordLayouts = default) =>
        new(identity, imports, isForTesting: true, comInterfaces, comOwners, comMethods, comBaseEdges,
            comSignatureTypes, fixedArrayStorageLayouts, storageRecordLayouts);
}

internal sealed record NativeImportAttributeDefinition(string FullyQualifiedName, int ArgumentCount,
    bool RequiresStringLiteral, bool IsRepeatable);

internal sealed record NativeImportDefinition(string Key, string DllName, string ExportName,
    ImmutableArray<PrimitiveType> ParameterTypes, PrimitiveType ReturnType, bool IsNoThrow,
    bool RequiresUnsafeCaller, bool DoesNotReturn, NativeImportDeploymentPolicy DeploymentPolicy)
{
    internal ImmutableArray<NativeImportParameterContract> ParameterContracts { get; init; } = [];
    internal ImmutableArray<NativeCallbackParameterDefinition> CallbackParameters { get; init; } = [];
    internal ImmutableArray<NativeStorageParameterDefinition> StorageParameters { get; init; } = [];
    internal ImmutableArray<NativeAggregateStorageParameterDefinition> AggregateStorageParameters { get; init; } = [];
    internal ImmutableArray<NativeStorageRecordParameterDefinition> StorageRecordParameters { get; init; } = [];
    internal ImmutableArray<NativeComOutputDefinition> ComOutputs { get; init; } = [];
}

internal enum NativeImportParameterContract
{
    Value,
    ReadonlyUtf16ZNoEscape,
    ReadonlyUtf8ZNoEscape,
    NoOwnerWindowHandle,
    NullPointerOnly,
    NativeCallback,
    ReadonlyNativeStorageNoEscape,
    WritableNativeStorageNoEscape,
    OwnedNativeComInterfaceOutput,
}

internal static class NativeWindowHandleContract
{
    internal const string TypeName = "Terrum.Interop.Windows.HWND";
    internal const string NoOwnerMemberName = "NoOwner";
}

internal static class NativeWindowProcedureContract
{
    internal const string TypeName = "Terrum.Interop.Windows.WNDPROC";
    internal static NativeCallbackSignatureDefinition CallbackSignature { get; } = new(
        PrimitiveType.NativeCallbackAddress, PrimitiveType.Int64,
        [PrimitiveType.NativeWindowHandle, PrimitiveType.UInt32, PrimitiveType.UInt64, PrimitiveType.Int64]);

    internal static bool MatchesFunction(BoundFunction function) =>
        NativeCallbackContract.MatchesFunction(function, CallbackSignature);
}

internal static class NativeUtf16ZContract
{
    internal const int MaximumStorageBytes = 8 * 1024 * 1024;

    internal static bool IsValidLiteral(string value)
    {
        if (value.Contains('\0') || ((long)value.Length + 1) * sizeof(char) > MaximumStorageBytes)
        {
            return false;
        }
        for (var index = 0; index < value.Length; index++)
        {
            if (char.IsHighSurrogate(value[index]))
            {
                if (index + 1 >= value.Length || !char.IsLowSurrogate(value[index + 1]))
                {
                    return false;
                }
                index++;
            }
            else if (char.IsLowSurrogate(value[index]))
            {
                return false;
            }
        }
        return true;
    }

    internal static bool IsValidPool(ImmutableArray<string> literals)
    {
        if (literals.IsDefault)
        {
            return false;
        }
        long byteCount = 0;
        foreach (var literal in literals)
        {
            if (literal is null || !IsValidLiteral(literal))
            {
                return false;
            }
            byteCount += ((long)literal.Length + 1) * sizeof(char);
            if (byteCount > MaximumStorageBytes)
            {
                return false;
            }
        }
        return true;
    }

    internal static long StorageBytes(string value) => ((long)value.Length + 1) * sizeof(char);
}

internal static class NativeUtf8ZContract
{
    internal const int MaximumStorageBytes = NativeUtf16ZContract.MaximumStorageBytes;

    internal static bool IsValidLiteral(string value)
    {
        if (value.Length + 1L > MaximumStorageBytes)
        {
            return false;
        }
        foreach (var character in value)
        {
            if (character is '\0' or > '\u007F')
            {
                return false;
            }
        }
        return true;
    }

    internal static bool IsValidPool(ImmutableArray<string> literals)
    {
        if (literals.IsDefault)
        {
            return false;
        }
        long byteCount = 0;
        foreach (var literal in literals)
        {
            if (literal is null || !IsValidLiteral(literal))
            {
                return false;
            }
            byteCount += StorageBytes(literal);
            if (byteCount > MaximumStorageBytes)
            {
                return false;
            }
        }
        return true;
    }

    internal static long StorageBytes(string value) => (long)value.Length + 1;
}

internal static class NativeStringZPoolContract
{
    internal const int MaximumStorageBytes = NativeUtf16ZContract.MaximumStorageBytes;

    internal static bool IsValid(ImmutableArray<string> utf16Literals, ImmutableArray<string> utf8Literals)
    {
        if (!NativeUtf16ZContract.IsValidPool(utf16Literals)
            || !NativeUtf8ZContract.IsValidPool(utf8Literals))
        {
            return false;
        }

        long byteCount = 0;
        foreach (var literal in utf16Literals)
        {
            byteCount += NativeUtf16ZContract.StorageBytes(literal);
            if (byteCount > MaximumStorageBytes)
            {
                return false;
            }
        }
        foreach (var literal in utf8Literals)
        {
            byteCount += NativeUtf8ZContract.StorageBytes(literal);
            if (byteCount > MaximumStorageBytes)
            {
                return false;
            }
        }
        return true;
    }
}

internal enum NativeImportDeploymentPolicy
{
    System32,
    ApplicationDirectory,
}
