// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using System.Linq;
using Terrum.CodeAnalysis.Lexing;

namespace Terrum.CodeAnalysis.Semantics;

internal static class NativeWindowsModulePathContract
{
    internal const int Capacity = 260;
    internal const int StorageBytes = Capacity * sizeof(char);
    internal const string GetModuleFileNameKey = "windows-x64.system32.kernel32.GetModuleFileNameW";
    internal const string GetLastErrorKey = "windows-x64.system32.kernel32.GetLastError";

    internal static NativeFixedArrayStorageLayoutDefinition StorageLayout { get; } = new(
        nameof(PrimitiveType.Char16), Capacity, sizeof(char), sizeof(char), StorageBytes) {
        AllowsEmptyInitialization = true,
        AllowsMutableLocal = true,
        AllowsElementWrites = true,
        AllowsUInt32Indices = true,
        AllowsCheckedRuntimeAccess = true,
        AllowsReadonlyUtf16ZLoan = true,
    };

    internal static NativeImportDefinition GetModuleFileNameImport { get; } = new(
        GetModuleFileNameKey, "kernel32.dll", "GetModuleFileNameW", [
            PrimitiveType.NativeNullPointer, PrimitiveType.NativeStorageAddress, PrimitiveType.UInt32,
        ], PrimitiveType.UInt32, true, true, false, NativeImportDeploymentPolicy.System32) {
        ParameterContracts = [
            NativeImportParameterContract.NullPointerOnly,
            NativeImportParameterContract.WritableNativeStorageNoEscape,
            NativeImportParameterContract.Value,
        ],
        StorageParameters = [
            new(1, PrimitiveType.NativeStorageAddress, PrimitiveType.NativeFixedArray, StorageBytes,
                false, 0, nameof(PrimitiveType.Char16), Capacity,
                sizeof(char), sizeof(char)),
        ],
    };

    internal static NativeImportDefinition GetLastErrorImport { get; } = new(
        GetLastErrorKey, "kernel32.dll", "GetLastError", [], PrimitiveType.UInt32,
        true, true, false, NativeImportDeploymentPolicy.System32);

    internal static ImmutableArray<NativeImportDefinition> Imports { get; } = [
        GetModuleFileNameImport,
        GetLastErrorImport,
    ];

    internal static bool IsReservedImport(NativeImportDefinition import) =>
        import.Key is GetModuleFileNameKey or GetLastErrorKey
        || import.ExportName is "GetModuleFileNameW" or "GetLastError";

    internal static bool MatchesImport(NativeImportDefinition import)
    {
        if (import.Key == GetModuleFileNameKey)
        {
            return !import.ParameterTypes.IsDefault && !import.ParameterContracts.IsDefault
                && import.ParameterTypes.SequenceEqual(GetModuleFileNameImport.ParameterTypes)
                && import.ParameterContracts.SequenceEqual(GetModuleFileNameImport.ParameterContracts)
                && import with {
                    ParameterTypes = GetModuleFileNameImport.ParameterTypes,
                    ParameterContracts = GetModuleFileNameImport.ParameterContracts,
                } == GetModuleFileNameImport;
        }
        return import.Key == GetLastErrorKey && import == GetLastErrorImport;
    }

    internal static bool MatchesImportFunction(BoundFunction function, NativeImportDefinition definition)
    {
        if (!IsReservedImport(definition) || !MatchesImport(definition)
            || !function.IsExternal || function.NativeImportKey != definition.Key
            || function.ReturnType != definition.ReturnType || function.ParameterCount != definition.ParameterTypes.Length
            || function.Variables.IsDefault || function.Variables.Length != definition.ParameterTypes.Length
            || !function.IsNoThrow || !function.RequiresUnsafeCaller || function.DoesNotReturn
            || function.IsConstructor || function.IsClassConstructor || function.IsOperator || function.IsCleanup
            || function.PropertyName is not null || function.DeclaringStruct is not null
            || function.Intrinsic != BoundIntrinsic.None || function.Body is null
            || !function.Body.Statements.IsEmpty)
        {
            return false;
        }
        for (var parameter = 0; parameter < definition.ParameterTypes.Length; parameter++)
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

    internal static bool MatchesCatalog(NativeImportCatalog? catalog) =>
        catalog is { IsForTesting: false, Identity: NativeWindowClassContract.CatalogIdentity }
        && catalog.TryGetImport(GetModuleFileNameKey, out var getModuleFileName)
        && getModuleFileName == GetModuleFileNameImport
        && catalog.TryGetImport(GetLastErrorKey, out var getLastError)
        && getLastError == GetLastErrorImport;

    internal static bool IsNullPointerParameter(NativeImportDefinition import, int parameter) =>
        import.Key == GetModuleFileNameKey && parameter == 0;

    internal static bool IsNullPointerArgument(BoundExpression argument, BoundFunction target,
        int parameter, BoundProgram program) =>
        argument is BoundConstant { Type: PrimitiveType.NativeNullPointer, Value: 0 }
        && target.IsExternal && target.NativeImportKey == GetModuleFileNameKey
        && IsNullPointerParameter(GetModuleFileNameImport, parameter)
        && MatchesCatalog(program.NativeImportCatalog);
}
