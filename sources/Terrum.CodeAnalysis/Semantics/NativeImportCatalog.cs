// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace Terrum.CodeAnalysis.Semantics;

/// <summary>A native import and ABI-contract catalog.</summary>
public sealed class NativeImportCatalog
{
    private static readonly NativeImportAttributeDefinition s_attributeDefinition =
        new("Terrum.Interop.NativeImport", ArgumentCount: 1, RequiresStringLiteral: true, IsRepeatable: false);

    private static readonly NativeImportCatalog s_windowsX64 = new(
        "windows-x64-native-imports-v10", []);

    private readonly ImmutableDictionary<string, NativeImportDefinition> _imports;
    private readonly ImmutableHashSet<string> _callbackTypeNames;
    private readonly ImmutableArray<NativeFixedArrayStorageLayoutDefinition> _fixedArrayStorageLayouts;
    private readonly ImmutableArray<NativeStorageRecordLayoutDefinition> _storageRecordLayouts;
    private readonly ImmutableArray<NativeAggregateStorageLayoutDefinition> _aggregateStorageLayouts;

    [SuppressMessage("Style", "IDE0072:Populate switch",
        Justification = "Unsupported native parameter types cannot acquire an import contract.")]
    private static bool ValidParameterContracts(NativeImportDefinition import)
    {
        if (!NativeStorageParameterContract.IsValid(import)
            || !NativeCallbackParameterContract.IsValid(import))
        {
            return false;
        }

        var hasRestrictedContract = import.ParameterTypes.Contains(PrimitiveType.NativeUtf16ZPointer)
            || import.ParameterTypes.Contains(PrimitiveType.NativeUtf8ZPointer)
            || import.ParameterTypes.Contains(PrimitiveType.NativeCallbackAddress)
            || import.ParameterTypes.Contains(PrimitiveType.NativeStorageAddress);
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
            var expected = type switch {
                PrimitiveType.NativeUtf16ZPointer =>
                    NativeStorageParameterContract.TryGetParameter(import, index, out var utf16Storage)
                    && utf16Storage is not null
                        ? NativeStorageParameterContract.ExpectedParameterContract(import, index)
                        : NativeImportParameterContract.ReadonlyUtf16ZNoEscape,
                PrimitiveType.NativeUtf8ZPointer => NativeImportParameterContract.ReadonlyUtf8ZNoEscape,
                PrimitiveType.NativeStorageAddress =>
                    NativeStorageParameterContract.ExpectedParameterContract(import, index),
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
        ImmutableArray<NativeFixedArrayStorageLayoutDefinition> fixedArrayStorageLayouts = default,
        ImmutableArray<NativeStorageRecordLayoutDefinition> nativeStorageRecordLayouts = default,
        ImmutableArray<NativeAggregateStorageLayoutDefinition> aggregateStorageLayouts = default)
    {
        if (string.IsNullOrWhiteSpace(identity) || imports.IsDefault)
        {
            throw new ArgumentException("A native contract catalog requires an identity and import data.",
                nameof(identity));
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
        var aggregateLayouts = aggregateStorageLayouts.IsDefault ? [] : aggregateStorageLayouts;
        if (aggregateLayouts.Any(static layout => !NativeAggregateContract.IsValidStorageLayout(layout))
            || aggregateLayouts.Select(static layout => layout.TypeName)
                .Distinct(StringComparer.Ordinal).Count() != aggregateLayouts.Length)
        {
            throw new ArgumentException("A native aggregate storage layout is invalid or duplicated.",
                nameof(aggregateStorageLayouts));
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
            if (!import.AggregateStorageParameters.IsDefaultOrEmpty
                && import.AggregateStorageParameters.Any(parameter => parameter?.SourceType is string sourceType
                    && !aggregateLayouts.Any(layout => layout.TypeName == sourceType
                        && layout.ByteExtent == parameter.ByteExtent
                        && layout.Alignment == parameter.RequiredAlignment)))
            {
                throw new ArgumentException("A native aggregate storage parameter has no selected layout.",
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
        var builder = ImmutableDictionary.CreateBuilder<string, NativeImportDefinition>(StringComparer.Ordinal);
        foreach (var import in imports)
        {
            ArgumentNullException.ThrowIfNull(import);
            if (string.IsNullOrEmpty(import.Key) || string.IsNullOrEmpty(import.DllName)
                || string.IsNullOrEmpty(import.ExportName) || import.ParameterTypes.IsDefault
                || import.ReturnType is not (PrimitiveType.Void or PrimitiveType.Int32
                    or PrimitiveType.UInt32 or PrimitiveType.Int64 or PrimitiveType.UInt64
                    or PrimitiveType.Char16)
                || import.ParameterTypes.Any(static type => type is not (PrimitiveType.Int32
                    or PrimitiveType.UInt32 or PrimitiveType.Int64 or PrimitiveType.UInt64
                    or PrimitiveType.Char16
                    or PrimitiveType.NativeUtf16ZPointer
                    or PrimitiveType.NativeUtf8ZPointer
                    or PrimitiveType.NativeCallbackAddress
                    or PrimitiveType.NativeStorageAddress))
                || !import.RequiresUnsafeCaller
                || import.ResultContract != NativeImportResultContract.Value
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
        _fixedArrayStorageLayouts = storageLayouts;
        _storageRecordLayouts = storageRecordLayouts;
        _aggregateStorageLayouts = aggregateLayouts;
    }

    /// <summary>Gets an empty catalog for Windows x64 native source imports.</summary>
    public static NativeImportCatalog WindowsX64 => s_windowsX64;

    /// <summary>Gets the versioned identity of this catalog.</summary>
    public string Identity { get; }

    internal bool IsForTesting { get; }

    internal ImmutableArray<NativeImportDefinition> ImportDefinitions => [.. _imports.Values];

    internal ImmutableArray<NativeFixedArrayStorageLayoutDefinition> FixedArrayStorageLayouts =>
        _fixedArrayStorageLayouts;

    internal ImmutableArray<NativeStorageRecordLayoutDefinition> StorageRecordLayouts =>
        _storageRecordLayouts;

    internal ImmutableArray<NativeAggregateStorageLayoutDefinition> AggregateStorageLayouts =>
        _aggregateStorageLayouts;

    internal static NativeImportAttributeDefinition AttributeDefinition => s_attributeDefinition;

    internal bool SupportsCataloguedFixedArrayStorage => IsForTesting
        && (!_fixedArrayStorageLayouts.IsEmpty
            || _imports.Values.Any(static import => import.StorageParameters.Any(parameter =>
            parameter is not null && parameter.AddressType == PrimitiveType.NativeStorageAddress
                && NativeFixedArrayContract.IsCarrier(parameter.CarrierType))));

    internal bool SupportsNativeFixedArrayStorage => !_fixedArrayStorageLayouts.IsEmpty
        || _imports.Values.Any(static import => import.StorageParameters.Any(parameter =>
            parameter is not null && parameter.AddressType == PrimitiveType.NativeStorageAddress
                && NativeFixedArrayContract.IsCarrier(parameter.CarrierType)));

    internal bool SupportsCheckedFixedArrayStorage =>
        _fixedArrayStorageLayouts.Any(static layout => layout.AllowsCheckedRuntimeAccess);

    internal bool SupportsAggregateStorageLayout(PrimitiveType type,
        IReadOnlyList<BoundStruct> structures)
    {
        var typeIndex = (int)type - 100;
        if (typeIndex < 0 || typeIndex >= structures.Count
            || !NativeAggregateContract.IsPlainValueAggregate(type, structures))
        {
            return false;
        }

        return _aggregateStorageLayouts.Any(layout =>
            NativeAggregateContract.MatchesStorageLayout(type, structures, this, layout));
    }

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

    internal int ImportCount => _imports.Count;

    internal bool TryGetImport(string key, out NativeImportDefinition? definition)
        => _imports.TryGetValue(key, out definition);

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

    internal bool SupportsParameterType(PrimitiveType type) =>
        _imports.Values.Any(import => import.ParameterTypes.Contains(type));

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
            if (variable.Type != definition.ParameterTypes[parameter] || variable.IsOut
                || variable.IsInit || variable.IsMutable
                || (definition.ParameterTypes[parameter] == PrimitiveType.NativeUtf16ZPointer
                    && definition.ParameterContracts[parameter]
                        != NativeImportParameterContract.ReadonlyUtf16ZNoEscape)
                || (definition.ParameterTypes[parameter] == PrimitiveType.NativeUtf8ZPointer
                    && definition.ParameterContracts[parameter]
                        != NativeImportParameterContract.ReadonlyUtf8ZNoEscape)
                || (definition.ParameterTypes[parameter] == PrimitiveType.NativeCallbackAddress
                    && !NativeCallbackParameterContract.TryGetParameter(definition, parameter, out _)))
            {
                return false;
            }
        }

        return true;
    }

    internal static NativeImportCatalog CreateForTesting(string identity,
        ImmutableArray<NativeImportDefinition> imports,
        ImmutableArray<NativeFixedArrayStorageLayoutDefinition> fixedArrayStorageLayouts = default,
        ImmutableArray<NativeStorageRecordLayoutDefinition> storageRecordLayouts = default,
        ImmutableArray<NativeAggregateStorageLayoutDefinition> aggregateStorageLayouts = default) =>
        new(identity, imports, isForTesting: true, fixedArrayStorageLayouts,
            storageRecordLayouts, aggregateStorageLayouts);
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
    internal NativeImportResultContract ResultContract { get; init; }
}

internal enum NativeImportResultContract
{
    Value,
}

internal enum NativeImportParameterContract
{
    Value,
    ReadonlyUtf16ZNoEscape,
    ReadonlyUtf8ZNoEscape,
    /// <summary>Invokes synchronously without registering or retaining the callback.</summary>
    NativeCallback = 11,
    ReadonlyNativeStorageNoEscape,
    WritableNativeStorageNoEscape,
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
