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
        "windows-x64-native-imports-v11", []);

    private readonly ImmutableDictionary<string, NativeImportDefinition> _imports;

    [SuppressMessage("Style", "IDE0072:Populate switch",
        Justification = "Unsupported native parameter types cannot acquire an import contract.")]
    private static bool ValidParameterContracts(NativeImportDefinition import)
    {
        var hasRestrictedContract = import.ParameterTypes.Contains(PrimitiveType.NativeUtf16ZPointer)
            || import.ParameterTypes.Contains(PrimitiveType.NativeUtf8ZPointer);
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
            var expected = type switch {
                PrimitiveType.NativeUtf16ZPointer => NativeImportParameterContract.ReadonlyUtf16ZNoEscape,
                PrimitiveType.NativeUtf8ZPointer => NativeImportParameterContract.ReadonlyUtf8ZNoEscape,
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
        bool isForTesting = false)
    {
        if (string.IsNullOrWhiteSpace(identity) || imports.IsDefault)
        {
            throw new ArgumentException("A native contract catalog requires an identity and import data.",
                nameof(identity));
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
                    or PrimitiveType.NativeUtf8ZPointer))
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
        Identity = identity;
        IsForTesting = isForTesting;
        _imports = builder.ToImmutable();
    }

    /// <summary>Gets an empty catalog for Windows x64 native source imports.</summary>
    public static NativeImportCatalog WindowsX64 => s_windowsX64;

    /// <summary>Gets the versioned identity of this catalog.</summary>
    public string Identity { get; }

    internal bool IsForTesting { get; }

    internal static NativeImportAttributeDefinition AttributeDefinition => s_attributeDefinition;

    internal int ImportCount => _imports.Count;

    internal bool TryGetImport(string key, out NativeImportDefinition? definition)
        => _imports.TryGetValue(key, out definition);

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
                        != NativeImportParameterContract.ReadonlyUtf8ZNoEscape))
            {
                return false;
            }
        }

        return true;
    }

    internal static NativeImportCatalog CreateForTesting(string identity,
        ImmutableArray<NativeImportDefinition> imports) =>
        new(identity, imports, isForTesting: true);
}

internal sealed record NativeImportAttributeDefinition(string FullyQualifiedName, int ArgumentCount,
    bool RequiresStringLiteral, bool IsRepeatable);

internal sealed record NativeImportDefinition(string Key, string DllName, string ExportName,
    ImmutableArray<PrimitiveType> ParameterTypes, PrimitiveType ReturnType, bool IsNoThrow,
    bool RequiresUnsafeCaller, bool DoesNotReturn, NativeImportDeploymentPolicy DeploymentPolicy)
{
    internal ImmutableArray<NativeImportParameterContract> ParameterContracts { get; init; } = [];
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
    /// <summary>Reserved ordinal for the retired catalog-selected callback contract.</summary>
    ReservedNativeCallback = 11,
    /// <summary>Reserved ordinal for the retired catalog-selected readonly storage contract.</summary>
    ReservedNativeStorageNoEscape = 12,
    /// <summary>Reserved ordinal for the retired catalog-selected writable storage contract.</summary>
    ReservedWritableNativeStorageNoEscape = 13,
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
