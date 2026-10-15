// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using System.Linq;

namespace Terrum.CodeAnalysis.Semantics;

internal static class NativeWindowMessageContract
{
    internal const string CatalogIdentity = NativeWindowClassContract.CatalogIdentity;
    internal const string TypeName = "Terrum.Interop.Windows.MSG";
    internal const string AddressTypeName = "Terrum.Interop.Windows.MSGAddress";
    internal const string InitializerName = "msgzero";
    internal const string IdAccessorName = "msgid";
    internal const string WParamAccessorName = "msgwparam";
    internal const string WindowIsAccessorName = "msgwindowis";
    internal const string ShowWindowKey = "windows-x64.system32.user32.ShowWindow";
    internal const string GetMessageKey = "windows-x64.system32.user32.GetMessageW";
    internal const string TranslateMessageKey = "windows-x64.system32.user32.TranslateMessage";
    internal const string DispatchMessageKey = "windows-x64.system32.user32.DispatchMessageW";
    internal const string DefWindowProcKey = "windows-x64.system32.user32.DefWindowProcW";
    internal const string PostQuitMessageKey = "windows-x64.system32.user32.PostQuitMessage";
    internal const string PostMessageKey = "windows-x64.system32.user32.PostMessageW";
    internal const string PeekMessageKey = "windows-x64.system32.user32.PeekMessageW";
    internal const int MessageSize = 48;
    internal const int MessageAlignment = 8;

    internal static ImmutableArray<NativeImportDefinition> Imports { get; } = [
        new(ShowWindowKey, "user32.dll", "ShowWindow",
            [PrimitiveType.NativeWindowHandle, PrimitiveType.Int32], PrimitiveType.Int32,
            true, true, false, NativeImportDeploymentPolicy.System32) {
            ParameterContracts = [
                NativeImportParameterContract.CreatedWindowHandleNoEscape,
                NativeImportParameterContract.Value,
            ],
        },
        new(GetMessageKey, "user32.dll", "GetMessageW",
            [PrimitiveType.NativeWindowMessageAddress, PrimitiveType.NativeWindowHandle,
                PrimitiveType.UInt32, PrimitiveType.UInt32], PrimitiveType.Int32,
            true, true, false, NativeImportDeploymentPolicy.System32) {
            ParameterContracts = [
                NativeImportParameterContract.WritableWindowMessageNoEscape,
                NativeImportParameterContract.NoOwnerWindowHandle,
                NativeImportParameterContract.Value,
                NativeImportParameterContract.Value,
            ],
        },
        new(TranslateMessageKey, "user32.dll", "TranslateMessage",
            [PrimitiveType.NativeWindowMessageAddress], PrimitiveType.Int32,
            true, true, false, NativeImportDeploymentPolicy.System32) {
            ParameterContracts = [NativeImportParameterContract.ReadonlyWindowMessageNoEscape],
        },
        new(DispatchMessageKey, "user32.dll", "DispatchMessageW",
            [PrimitiveType.NativeWindowMessageAddress], PrimitiveType.Int64,
            true, true, false, NativeImportDeploymentPolicy.System32) {
            ParameterContracts = [NativeImportParameterContract.ReadonlyWindowMessageNoEscape],
        },
        new(DefWindowProcKey, "user32.dll", "DefWindowProcW",
            [PrimitiveType.NativeWindowHandle, PrimitiveType.UInt32, PrimitiveType.UInt64,
                PrimitiveType.Int64], PrimitiveType.Int64,
            true, true, false, NativeImportDeploymentPolicy.System32) {
            ParameterContracts = [
                NativeImportParameterContract.WindowProcedureBorrowedHandle,
                NativeImportParameterContract.Value,
                NativeImportParameterContract.Value,
                NativeImportParameterContract.Value,
            ],
        },
        new(PostQuitMessageKey, "user32.dll", "PostQuitMessage",
            [PrimitiveType.Int32], PrimitiveType.Void,
            true, true, false, NativeImportDeploymentPolicy.System32),
        new(PostMessageKey, "user32.dll", "PostMessageW",
            [PrimitiveType.NativeWindowHandle, PrimitiveType.UInt32, PrimitiveType.UInt64,
                PrimitiveType.Int64], PrimitiveType.Int32,
            true, true, false, NativeImportDeploymentPolicy.System32) {
            ParameterContracts = [
                NativeImportParameterContract.WindowProcedureBorrowedHandle,
                NativeImportParameterContract.Value,
                NativeImportParameterContract.Value,
                NativeImportParameterContract.Value,
            ],
        },
        new(PeekMessageKey, "user32.dll", "PeekMessageW",
            [PrimitiveType.NativeWindowMessageAddress, PrimitiveType.NativeWindowHandle,
                PrimitiveType.UInt32, PrimitiveType.UInt32, PrimitiveType.UInt32], PrimitiveType.Int32,
            true, true, false, NativeImportDeploymentPolicy.System32) {
            ParameterContracts = [
                NativeImportParameterContract.WritableWindowMessageNoEscape,
                NativeImportParameterContract.NoOwnerOrCreatedWindowHandleNoEscape,
                NativeImportParameterContract.Value,
                NativeImportParameterContract.Value,
                NativeImportParameterContract.Value,
            ],
        },
    ];

    internal static ImmutableArray<BoundFunction> AccessorFunctions { get; } = [
        Accessor(IdAccessorName, PrimitiveType.UInt32, BoundIntrinsic.NativeWindowMessageId),
        Accessor(WParamAccessorName, PrimitiveType.UInt64, BoundIntrinsic.NativeWindowMessageWParam),
        new(WindowIsAccessorName, PrimitiveType.Bool, [
            new("message", PrimitiveType.NativeWindowMessageAddress, false, default),
            new("window", PrimitiveType.NativeWindowHandle, false, default),
        ], 2, new BoundBlock([], default), default) {
            IsInternal = true,
            IsNoThrow = true,
            Intrinsic = BoundIntrinsic.NativeWindowMessageWindowIs,
        },
    ];

    private static BoundFunction Accessor(string name, PrimitiveType returnType, BoundIntrinsic intrinsic) =>
        new(name, returnType, [
            new("message", PrimitiveType.NativeWindowMessageAddress, false, default),
        ], 1, new BoundBlock([], default), default) {
            IsInternal = true,
            IsNoThrow = true,
            Intrinsic = intrinsic,
        };

    internal static bool IsTypeName(string name) => name is TypeName or AddressTypeName;

    internal static PrimitiveType Type(string name) => name switch {
        TypeName => PrimitiveType.NativeWindowMessage,
        AddressTypeName => PrimitiveType.NativeWindowMessageAddress,
        _ => throw new ArgumentOutOfRangeException(nameof(name)),
    };

    internal static bool IsReservedImport(NativeImportDefinition import) =>
        import.Key is ShowWindowKey or GetMessageKey or TranslateMessageKey or DispatchMessageKey
            or DefWindowProcKey or PostQuitMessageKey or PostMessageKey or PeekMessageKey
        || MatchesIdentity(import)
        || (!import.ParameterTypes.IsDefaultOrEmpty
            && import.ParameterTypes.Contains(PrimitiveType.NativeWindowMessageAddress))
        || (!import.ParameterContracts.IsDefaultOrEmpty
            && (import.ParameterContracts.Contains(NativeImportParameterContract.WritableWindowMessageNoEscape)
                || import.ParameterContracts.Contains(NativeImportParameterContract.ReadonlyWindowMessageNoEscape)
                || import.ParameterContracts.Contains(NativeImportParameterContract.WindowProcedureBorrowedHandle)));

    internal static bool MatchesIdentity(NativeImportDefinition import) =>
        import.DllName.Equals("user32.dll", StringComparison.OrdinalIgnoreCase)
        && import.ExportName is "ShowWindow" or "GetMessageW" or "TranslateMessage"
            or "DispatchMessageW" or "DefWindowProcW" or "PostQuitMessage" or "PostMessageW" or "PeekMessageW";

    internal static bool MatchesImport(NativeImportDefinition import)
    {
        var expected = import.Key switch {
            ShowWindowKey => Imports[0],
            GetMessageKey => Imports[1],
            TranslateMessageKey => Imports[2],
            DispatchMessageKey => Imports[3],
            DefWindowProcKey => Imports[4],
            PostQuitMessageKey => Imports[5],
            PostMessageKey => Imports[6],
            PeekMessageKey => Imports[7],
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
        catalog is { IsForTesting: false, Identity: NativeWindowClassContract.CatalogIdentity }
        && NativeWindowClassContract.MatchesCatalog(catalog)
        && NativeWindowCreationContract.MatchesCatalog(catalog)
        && Imports.All(expected => catalog.TryGetImport(expected.Key, out var actual)
            && actual is not null && MatchesImport(actual));

    internal static NativeImportParameterContract? ExpectedHandleParameterContract(
        NativeImportDefinition import, int parameter) =>
        import.Key switch {
            ShowWindowKey when parameter == 0 => NativeImportParameterContract.CreatedWindowHandleNoEscape,
            PeekMessageKey when parameter == 1 =>
                NativeImportParameterContract.NoOwnerOrCreatedWindowHandleNoEscape,
            DefWindowProcKey or PostMessageKey when parameter == 0 =>
                NativeImportParameterContract.WindowProcedureBorrowedHandle,
            _ => null,
        };

    internal static NativeImportParameterContract ExpectedMessageAddressParameterContract(
        NativeImportDefinition import, int parameter) =>
        import.Key switch {
            GetMessageKey or PeekMessageKey when parameter == 0 =>
                NativeImportParameterContract.WritableWindowMessageNoEscape,
            TranslateMessageKey or DispatchMessageKey when parameter == 0 =>
                NativeImportParameterContract.ReadonlyWindowMessageNoEscape,
            _ => NativeImportParameterContract.Value,
        };

    internal static bool MatchesMessageAddressParameter(NativeImportDefinition import, int parameter) =>
        parameter >= 0 && parameter < import.ParameterTypes.Length
        && import.ParameterTypes[parameter] == PrimitiveType.NativeWindowMessageAddress
        && parameter < import.ParameterContracts.Length
        && import.ParameterContracts[parameter]
            == ExpectedMessageAddressParameterContract(import, parameter)
        && ExpectedMessageAddressParameterContract(import, parameter)
            is NativeImportParameterContract.WritableWindowMessageNoEscape
                or NativeImportParameterContract.ReadonlyWindowMessageNoEscape;

    internal static bool IsMessageAddressParameter(
        BoundFunction function, int parameter, BoundProgram program)
    {
        if (program.NativeImportCatalog is not NativeImportCatalog catalog
            || !catalog.SupportsWindowMessageLoop)
        {
            return false;
        }

        if (IsAccessorFunction(function))
        {
            return parameter == 0;
        }

        return function.IsExternal && function.NativeImportKey is string key
            && catalog.TryGetImport(key, out var import) && import is not null
            && MatchesImport(import) && MatchesMessageAddressParameter(import, parameter);
    }

    internal static bool IsMessageAddressArgument(BoundExpression argument, BoundFunction target,
        int parameter, BoundFunction caller, BoundProgram program)
    {
        if (parameter != 0 || argument is not BoundNativeWindowMessageAddress address
            || address.Variable < 0 || address.Variable >= caller.Variables.Length
            || caller.Variables[address.Variable].Type != PrimitiveType.NativeWindowMessage
            || !IsMessageAddressParameter(target, parameter, program))
        {
            return false;
        }

        if (IsAccessorFunction(target))
        {
            return true;
        }

        var key = target.NativeImportKey;
        if (key is not (GetMessageKey or PeekMessageKey)
            || !caller.Variables[address.Variable].IsMutable)
        {
            return key is TranslateMessageKey or DispatchMessageKey;
        }

        return true;
    }

    internal static bool IsAccessorFunction(BoundFunction function) =>
        function.IsInternal && function.IsNoThrow && !function.IsExternal
        && (function.Name, function.ReturnType, function.Intrinsic) switch {
            (IdAccessorName, PrimitiveType.UInt32, BoundIntrinsic.NativeWindowMessageId)
                or (WParamAccessorName, PrimitiveType.UInt64, BoundIntrinsic.NativeWindowMessageWParam) =>
                function.ParameterCount == 1 && function.Variables.Length == 1
                    && function.Variables[0].Type == PrimitiveType.NativeWindowMessageAddress,
            (WindowIsAccessorName, PrimitiveType.Bool, BoundIntrinsic.NativeWindowMessageWindowIs) =>
                function.ParameterCount == 2 && function.Variables.Length == 2
                    && function.Variables[0].Type == PrimitiveType.NativeWindowMessageAddress
                    && function.Variables[1].Type == PrimitiveType.NativeWindowHandle,
            _ => false,
        };

    internal static bool IsAccessorWindowArgument(BoundExpression argument, BoundFunction target,
        int parameter, BoundFunction caller, BoundProgram program) =>
        parameter == 1 && target.Name == WindowIsAccessorName && IsAccessorFunction(target)
        && NativeHandleOwnerContract.IsOwnerHandleFieldRead(argument, caller, program);

    internal static bool IsWindowProcedureHandleArgument(BoundExpression argument, BoundFunction target,
        int parameter, BoundFunction caller, BoundProgram program)
        => parameter == 0 && target.NativeImportKey is DefWindowProcKey or PostMessageKey
        && argument is BoundRead { Variable: 0, Type: PrimitiveType.NativeWindowHandle }
        && program.NativeImportCatalog?.MatchesCallbackFunction(
            PrimitiveType.NativeCallbackAddress, caller) == true;
}
