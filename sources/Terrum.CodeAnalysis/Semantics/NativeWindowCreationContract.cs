// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using System.Linq;

namespace Terrum.CodeAnalysis.Semantics;

internal static class NativeWindowCreationContract
{
    internal const string CatalogIdentity = NativeWindowClassContract.CatalogIdentity;
    internal const string CreateWindowExKey = "windows-x64.system32.user32.CreateWindowExW";
    internal const string DestroyWindowKey = "windows-x64.system32.user32.DestroyWindow";
    internal const string SetWindowPosKey = "windows-x64.system32.user32.SetWindowPos";
    internal const string CreateWindowExExport = "CreateWindowExW";
    internal const string DestroyWindowExport = "DestroyWindow";
    internal const string SetWindowPosExport = "SetWindowPos";

    internal static readonly ImmutableArray<string> NullOnlyParameterTypeNames = [
        "Terrum.Interop.Windows.HMENU",
        "Terrum.Interop.Windows.LPVOID",
        "Terrum.Interop.Windows.NativeNullPointer",
    ];

    internal static bool IsNullOnlyParameterTypeName(string name) =>
        name is "Terrum.Interop.Windows.HMENU"
            or "Terrum.Interop.Windows.LPVOID"
            or "Terrum.Interop.Windows.NativeNullPointer";

    internal static ImmutableArray<NativeImportDefinition> Imports { get; } = [
        new(CreateWindowExKey, "user32.dll", CreateWindowExExport,
            [
                PrimitiveType.UInt32,
                PrimitiveType.NativeUtf16ZPointer,
                PrimitiveType.NativeUtf16ZPointer,
                PrimitiveType.UInt32,
                PrimitiveType.Int32,
                PrimitiveType.Int32,
                PrimitiveType.Int32,
                PrimitiveType.Int32,
                PrimitiveType.NativeWindowHandle,
                PrimitiveType.NativeNullPointer,
                PrimitiveType.NativeModuleHandle,
                PrimitiveType.NativeNullPointer,
            ],
            PrimitiveType.NativeWindowHandle, true, true, false, NativeImportDeploymentPolicy.System32) {
            ParameterContracts = [
                NativeImportParameterContract.Value,
                NativeImportParameterContract.ReadonlyUtf16ZNoEscape,
                NativeImportParameterContract.ReadonlyUtf16ZNoEscape,
                NativeImportParameterContract.Value,
                NativeImportParameterContract.Value,
                NativeImportParameterContract.Value,
                NativeImportParameterContract.Value,
                NativeImportParameterContract.Value,
                NativeImportParameterContract.NoOwnerWindowHandle,
                NativeImportParameterContract.NullPointerOnly,
                NativeImportParameterContract.CurrentModuleHandleNoEscape,
                NativeImportParameterContract.NullPointerOnly,
            ],
            ResultContract = NativeImportResultContract.CreatedWindowHandle,
        },
        new(DestroyWindowKey, "user32.dll", DestroyWindowExport,
            [PrimitiveType.NativeWindowHandle], PrimitiveType.Int32, true, true, false,
            NativeImportDeploymentPolicy.System32) {
            ParameterContracts = [NativeImportParameterContract.CreatedWindowHandleNoEscape],
        },
        new(SetWindowPosKey, "user32.dll", SetWindowPosExport,
            [PrimitiveType.NativeWindowHandle, PrimitiveType.NativeWindowHandle,
                PrimitiveType.Int32, PrimitiveType.Int32, PrimitiveType.Int32, PrimitiveType.Int32,
                PrimitiveType.UInt32],
            PrimitiveType.Int32, true, true, false, NativeImportDeploymentPolicy.System32) {
            ParameterContracts = [
                NativeImportParameterContract.CreatedWindowHandleNoEscape,
                NativeImportParameterContract.NoOwnerWindowHandle,
                NativeImportParameterContract.Value,
                NativeImportParameterContract.Value,
                NativeImportParameterContract.Value,
                NativeImportParameterContract.Value,
                NativeImportParameterContract.Value,
            ],
        },
    ];

    internal static bool IsReservedImport(NativeImportDefinition import) =>
        import.Key is CreateWindowExKey or DestroyWindowKey or SetWindowPosKey
        || MatchesIdentity(import)
        || import.ReturnType == PrimitiveType.NativeWindowHandle;

    internal static bool MatchesIdentity(NativeImportDefinition import) =>
        import.DllName.Equals("user32.dll", StringComparison.OrdinalIgnoreCase)
        && import.ExportName is CreateWindowExExport or DestroyWindowExport or SetWindowPosExport;

    internal static bool MatchesImport(NativeImportDefinition import)
    {
        var expected = import.Key switch {
            CreateWindowExKey => Imports[0],
            DestroyWindowKey => Imports[1],
            SetWindowPosKey => Imports[2],
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
        && catalog.TryGetImport(CreateWindowExKey, out var createWindow)
        && createWindow is not null && MatchesImport(createWindow)
        && catalog.TryGetImport(DestroyWindowKey, out var destroyWindow)
        && destroyWindow is not null && MatchesImport(destroyWindow)
        && catalog.TryGetImport(SetWindowPosKey, out var setWindowPos)
        && setWindowPos is not null && MatchesImport(setWindowPos)
        && NativeWindowClassContract.MatchesCatalog(catalog);

    internal static NativeImportParameterContract? ExpectedHandleParameterContract(
        NativeImportDefinition import, int parameter) =>
        import.Key switch {
            CreateWindowExKey when parameter == 8 => NativeImportParameterContract.NoOwnerWindowHandle,
            DestroyWindowKey when parameter == 0 => NativeImportParameterContract.CreatedWindowHandleNoEscape,
            SetWindowPosKey when parameter == 0 => NativeImportParameterContract.CreatedWindowHandleNoEscape,
            SetWindowPosKey when parameter == 1 => NativeImportParameterContract.NoOwnerWindowHandle,
            _ => null,
        };

    internal static bool IsNullPointerParameter(NativeImportDefinition import, int parameter) =>
        import.Key == CreateWindowExKey && parameter is 9 or 11;

    internal static bool IsCurrentModuleHandleParameter(NativeImportDefinition import, int parameter) =>
        import.Key == CreateWindowExKey && parameter == 10;

    internal static bool IsCreateWindowFunction(BoundFunction function, NativeImportCatalog? catalog) =>
        function.IsExternal && function.NativeImportKey == CreateWindowExKey
        && catalog is not null
        && MatchesCatalog(catalog)
        && catalog.TryGetImport(CreateWindowExKey, out var definition)
        && definition is not null && MatchesImport(definition);

    internal static bool IsCreatedWindowArgument(BoundExpression argument, BoundFunction target,
        int parameter, BoundFunction caller, BoundProgram program)
    {
        if (!target.IsExternal || target.NativeImportKey is not string key)
        {
            return false;
        }

        if ((key == NativeWindowMessageContract.ShowWindowKey && parameter == 0)
            || (key == NativeWindowMessageContract.PeekMessageKey && parameter == 1)
            || (key == SetWindowPosKey && parameter == 0))
        {
            return NativeWindowOwnerContract.IsOwnedWindowArgument(argument, caller, program);
        }
        if (parameter != 0 || key != DestroyWindowKey && key != NativeWindowClientRectContract.ImportKey)
        {
            return false;
        }

        return argument switch {
            BoundCall { Type: PrimitiveType.NativeWindowHandle } createCall =>
                createCall.Function >= 0 && createCall.Function < program.Functions.Length
                && IsCreateWindowFunction(program.Functions[createCall.Function], program.NativeImportCatalog),
            BoundRead { Type: PrimitiveType.NativeWindowHandle } window =>
                IsCreatedWindowLocal(caller, window.Variable, program),
            BoundFieldRead { Type: PrimitiveType.NativeWindowHandle } =>
                NativeWindowOwnerContract.IsOwnedWindowArgument(argument, caller, program),
            _ => false,
        };
    }

    internal static bool IsCreatedWindowLocal(BoundFunction function, int variable, BoundProgram program) =>
        (uint)variable < (uint)function.Variables.Length
        && function.Variables[variable] is {
            Type: PrimitiveType.NativeWindowHandle,
            IsMutable: false,
            IsOut: false,
            IsInit: false,
            IsInitializationLocal: false,
        }
        && variable >= function.ParameterCount + (function.IsConstructor ? 1 : 0)
        && !function.IsExternal
        && function.Variables.Any(static local => local.Type == PrimitiveType.NativeWindowClassEx)
        && program.RuntimePolicy == BoundRuntimePolicy.FailFast
        && NativeWindowClassContract.MatchesCatalog(program.NativeImportCatalog);

    internal static bool IsCreatedWindowInitializer(BoundExpression initializer, int variable,
        BoundFunction function, BoundProgram program) =>
        IsCreatedWindowLocal(function, variable, program)
        && initializer is BoundCall { Type: PrimitiveType.NativeWindowHandle } createCall
        && (uint)createCall.Function < (uint)program.Functions.Length
        && IsCreateWindowFunction(program.Functions[createCall.Function], program.NativeImportCatalog);

    internal static bool IsNullPointerArgument(BoundExpression argument, BoundFunction target,
        int parameter, BoundProgram program) =>
        argument is BoundConstant { Type: PrimitiveType.NativeNullPointer, Value: 0 }
        && target.IsExternal && target.NativeImportKey == CreateWindowExKey
        && IsNullPointerParameter(Imports[0], parameter)
        && MatchesCatalog(program.NativeImportCatalog);
}
