// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;

namespace Terrum.CodeAnalysis.Semantics;

internal static class NativeWindowLifetimeContract
{
    internal const string ImportKey = "test.application.native-window-lifetime.NativeFfiWindowLifetime";
    internal const string DllName = "native-window-lifetime-probe.dll";
    internal const string ExportName = "NativeFfiWindowLifetime";

    internal static bool MatchesIdentity(NativeImportDefinition import) =>
        string.Equals(import.DllName, DllName, StringComparison.OrdinalIgnoreCase)
        && import.ExportName == ExportName;

    internal static bool MatchesImport(NativeImportDefinition import) =>
        import.Key == ImportKey && import.DllName == DllName && import.ExportName == ExportName
        && import.DeploymentPolicy == NativeImportDeploymentPolicy.ApplicationDirectory
        && import.IsNoThrow && !import.RequiresUnsafeCaller && !import.DoesNotReturn
        && import.ReturnType == PrimitiveType.Int32
        && !import.ParameterTypes.IsDefault && import.ParameterTypes is [PrimitiveType.NativeCallbackAddress]
        && !import.ParameterContracts.IsDefault
        && import.ParameterContracts is [NativeImportParameterContract.NativeCallback]
        && NativeCallbackParameterContract.IsValid(import)
        && NativeCallbackParameterContract.TryGetParameter(import, 0, out var callbackParameter)
        && callbackParameter is { Lifetime: NativeCallbackLifetime.RetainedUntilCallReturns }
        && NativeCallbackContract.SignaturesEqual(callbackParameter.Signature,
            NativeWindowProcedureContract.CallbackSignature);
}
