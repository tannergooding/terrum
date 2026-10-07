// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using System.Linq;

namespace Terrum.CodeAnalysis.Semantics;

/// <summary>A compiler-owned catalog of reviewed Windows x64 native imports.</summary>
public sealed class NativeImportCatalog
{
    private static readonly NativeImportAttributeDefinition s_attributeDefinition =
        new("Terrum.Interop.NativeImport", ArgumentCount: 1, RequiresStringLiteral: true, IsRepeatable: false);

    private static readonly NativeImportCatalog s_windowsX64 = new(
        "windows-x64-native-imports-v1",
        [
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
        ]);

    private readonly ImmutableDictionary<string, NativeImportDefinition> _imports;

    internal NativeImportCatalog(string identity, ImmutableArray<NativeImportDefinition> imports)
    {
        if (string.IsNullOrWhiteSpace(identity) || imports.IsDefaultOrEmpty)
        {
            throw new ArgumentException("A native import catalog requires an identity and at least one entry.",
                nameof(identity));
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
                    or PrimitiveType.UInt32 or PrimitiveType.Int64 or PrimitiveType.UInt64))
                || import.DeploymentPolicy is not (NativeImportDeploymentPolicy.System32
                    or NativeImportDeploymentPolicy.ApplicationDirectory)
                || builder.ContainsKey(import.Key))
            {
                throw new ArgumentException("Native import catalog entries must be complete and uniquely keyed.",
                    nameof(imports));
            }
            builder.Add(import.Key, import);
        }

        Identity = identity;
        _imports = builder.ToImmutable();
    }

    /// <summary>Gets the immutable reviewed Windows x64 system-import catalog.</summary>
    public static NativeImportCatalog WindowsX64 => s_windowsX64;

    /// <summary>Gets the versioned identity of this catalog.</summary>
    public string Identity { get; }

    internal static NativeImportAttributeDefinition AttributeDefinition => s_attributeDefinition;

    internal bool TryGetImport(string key, out NativeImportDefinition? definition)
        => _imports.TryGetValue(key, out definition);

    internal bool MatchesFunction(BoundFunction function, NativeImportDefinition definition)
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
            if (function.Variables[parameter].Type != definition.ParameterTypes[parameter]
                || function.Variables[parameter].IsOut || function.Variables[parameter].IsInit
                || function.Variables[parameter].IsMutable)
            {
                return false;
            }
        }

        return true;
    }

    internal static NativeImportCatalog CreateForTesting(string identity,
        ImmutableArray<NativeImportDefinition> imports) => new(identity, imports);
}

internal sealed record NativeImportAttributeDefinition(string FullyQualifiedName, int ArgumentCount,
    bool RequiresStringLiteral, bool IsRepeatable);

internal sealed record NativeImportDefinition(string Key, string DllName, string ExportName,
    ImmutableArray<PrimitiveType> ParameterTypes, PrimitiveType ReturnType, bool IsNoThrow,
    bool RequiresUnsafeCaller, bool DoesNotReturn, NativeImportDeploymentPolicy DeploymentPolicy);

internal enum NativeImportDeploymentPolicy
{
    System32,
    ApplicationDirectory,
}
