// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using System.Collections.Generic;

namespace Terrum.CodeAnalysis.Semantics;

internal static class NativeWindowClassContract
{
    internal const string CatalogIdentity = "windows-x64-native-imports-v9";
    internal const string GetModuleHandleKey = "windows-x64.system32.kernel32.GetModuleHandleW";
    internal const string RegisterClassExKey = "windows-x64.system32.user32.RegisterClassExW";
    internal const string UnregisterClassKey = "windows-x64.system32.user32.UnregisterClassW";
    internal const string GetModuleHandleExport = "GetModuleHandleW";
    internal const string RegisterClassExExport = "RegisterClassExW";
    internal const string UnregisterClassExport = "UnregisterClassW";
    internal const string ModuleHandleTypeName = "Terrum.Interop.Windows.HINSTANCE";
    internal const string WindowClassTypeName = "Terrum.Interop.Windows.WNDCLASSEXW";
    internal const string WindowClassAddressTypeName = "Terrum.Interop.Windows.ReadonlyWNDCLASSEXW";
    internal const string InitializerName = "windowclassex";
    internal const int WindowClassExSize = 80;

    internal static bool IsTypeName(string name) =>
        name is ModuleHandleTypeName or WindowClassTypeName or WindowClassAddressTypeName;

    internal static PrimitiveType Type(string name) => name switch {
        ModuleHandleTypeName => PrimitiveType.NativeModuleHandle,
        WindowClassTypeName => PrimitiveType.NativeWindowClassEx,
        WindowClassAddressTypeName => PrimitiveType.NativeWindowClassExAddress,
        _ => throw new ArgumentOutOfRangeException(nameof(name)),
    };

    internal static ImmutableArray<NativeImportDefinition> Imports { get; } = [
        new(GetModuleHandleKey, "kernel32.dll", GetModuleHandleExport,
            [PrimitiveType.NativeNullPointer], PrimitiveType.NativeModuleHandle, true, true, false,
            NativeImportDeploymentPolicy.System32) {
            ParameterContracts = [NativeImportParameterContract.NullPointerOnly],
            ResultContract = NativeImportResultContract.CurrentModuleHandle,
        },
        new(RegisterClassExKey, "user32.dll", RegisterClassExExport,
            [PrimitiveType.NativeWindowClassExAddress], PrimitiveType.Char16, true, true, false,
            NativeImportDeploymentPolicy.System32) {
            ParameterContracts = [NativeImportParameterContract.RegisteredWindowClassDescriptorNoEscape],
        },
        new(UnregisterClassKey, "user32.dll", UnregisterClassExport,
            [PrimitiveType.NativeUtf16ZPointer, PrimitiveType.NativeModuleHandle],
            PrimitiveType.Int32, true, true, false, NativeImportDeploymentPolicy.System32) {
            ParameterContracts = [
                NativeImportParameterContract.ReadonlyUtf16ZNoEscape,
                NativeImportParameterContract.CurrentModuleHandleNoEscape,
            ],
        },
    ];

    internal static bool IsReservedImport(NativeImportDefinition import) =>
        import.Key is GetModuleHandleKey or RegisterClassExKey or UnregisterClassKey
        || MatchesIdentity(import)
        || (!import.ParameterTypes.IsDefaultOrEmpty
            && import.ParameterTypes.Contains(PrimitiveType.NativeWindowClassExAddress))
        || import.ReturnType == PrimitiveType.NativeModuleHandle
        || (!import.ParameterContracts.IsDefaultOrEmpty
            && import.ParameterContracts.Contains(
                NativeImportParameterContract.RegisteredWindowClassDescriptorNoEscape));

    internal static bool MatchesIdentity(NativeImportDefinition import) =>
        import.DllName.Equals("kernel32.dll", StringComparison.OrdinalIgnoreCase)
            && import.ExportName == GetModuleHandleExport
        || import.DllName.Equals("user32.dll", StringComparison.OrdinalIgnoreCase)
            && import.ExportName is RegisterClassExExport or UnregisterClassExport;

    internal static bool MatchesImport(NativeImportDefinition import)
    {
        var expected = import.Key switch {
            GetModuleHandleKey => Imports[0],
            RegisterClassExKey => Imports[1],
            UnregisterClassKey => Imports[2],
            _ => null,
        };
        return expected is not null
            && import.DllName.Equals(expected.DllName, StringComparison.OrdinalIgnoreCase)
            && import.ExportName == expected.ExportName
            && import.ParameterTypes.AsSpan().SequenceEqual(expected.ParameterTypes.AsSpan())
            && import.ReturnType == expected.ReturnType
            && import.IsNoThrow == expected.IsNoThrow
            && import.RequiresUnsafeCaller == expected.RequiresUnsafeCaller
            && import.DoesNotReturn == expected.DoesNotReturn
            && import.DeploymentPolicy == expected.DeploymentPolicy
            && import.ParameterContracts.AsSpan().SequenceEqual(expected.ParameterContracts.AsSpan())
            && import.ResultContract == expected.ResultContract;
    }

    internal static bool MatchesCatalog(NativeImportCatalog? catalog) =>
        catalog is { IsForTesting: false, Identity: CatalogIdentity }
        && catalog.TryGetImport(GetModuleHandleKey, out var getModuleHandle)
        && getModuleHandle is not null && MatchesImport(getModuleHandle)
        && catalog.TryGetImport(RegisterClassExKey, out var registerClass)
        && registerClass is not null && MatchesImport(registerClass)
        && catalog.TryGetImport(UnregisterClassKey, out var unregisterClass)
        && unregisterClass is not null && MatchesImport(unregisterClass);

    internal static bool IsCurrentModuleHandleArgument(BoundExpression argument,
        BoundFunction target, int parameter, BoundProgram program) =>
        parameter >= 0 && target.IsExternal && target.NativeImportKey is string key
        && program.NativeImportCatalog is NativeImportCatalog catalog
        && catalog.TryGetImport(key, out var definition) && definition is not null
        && (IsCurrentModuleHandleParameter(definition, parameter)
            || NativeWindowCreationContract.IsCurrentModuleHandleParameter(definition, parameter))
        && argument is BoundCall { Type: PrimitiveType.NativeModuleHandle } getModuleCall
        && getModuleCall.Function >= 0 && getModuleCall.Function < program.Functions.Length
        && IsGetModuleHandleFunction(program.Functions[getModuleCall.Function], program.NativeImportCatalog);

    internal static bool IsCurrentModuleHandleParameter(NativeImportDefinition import, int parameter) =>
        import.Key == UnregisterClassKey && parameter == 1;

    internal static bool IsGetModuleHandleFunction(BoundFunction function, NativeImportCatalog? catalog) =>
        function.IsExternal && function.NativeImportKey == GetModuleHandleKey
        && catalog is not null && MatchesCatalog(catalog)
        && catalog.TryGetImport(GetModuleHandleKey, out var definition)
        && definition is not null && MatchesImport(definition);

    internal static bool IsWindowClassAddressArgument(BoundExpression argument, BoundFunction target,
        int parameter, BoundProgram program) =>
        parameter == 0 && target.IsExternal && target.NativeImportKey == RegisterClassExKey
        && argument is BoundNativeWindowClassExAddress
        && MatchesCatalog(program.NativeImportCatalog);

    internal static bool IsNullPointerArgument(BoundExpression argument, BoundFunction target,
        int parameter, BoundProgram program) =>
        argument is BoundConstant { Type: PrimitiveType.NativeNullPointer, Value: 0 }
        && target.IsExternal && target.NativeImportKey == GetModuleHandleKey && parameter == 0
        && MatchesCatalog(program.NativeImportCatalog);

    internal static bool IsNullPointerParameter(NativeImportDefinition import, int parameter) =>
        import.Key == GetModuleHandleKey && parameter == 0;

    internal static NativeWindowClassExFacts? Facts(
        BoundNativeWindowClassExInitialization initializer, IReadOnlyList<string> literals)
    {
        if (initializer.Callback is null
            || initializer.Callback.Function < 0
            || initializer.ClassName is not BoundConstant {
                Type: PrimitiveType.NativeUtf16ZPointer, Value: >= 0,
            } className
            || className.Value >= literals.Count
            || !IsGetModuleHandleCall(initializer.Instance))
        {
            return null;
        }

        return new NativeWindowClassExFacts(initializer.Callback.Function, checked((int)className.Value));
    }

    internal static bool IsGetModuleHandleCall(BoundExpression expression) =>
        expression is BoundCall { Function: >= 0, Type: PrimitiveType.NativeModuleHandle };
}
