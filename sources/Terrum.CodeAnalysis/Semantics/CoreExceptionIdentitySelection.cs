// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;

namespace Terrum.CodeAnalysis.Semantics;

/// <summary>A host-selected pair of Core exception declarations for one using Module.</summary>
/// <param name="UsingModuleIdentity">The exact logical Module that uses the selected identities.</param>
/// <param name="ExceptionInterfaceProducerIdentity">The exact producer Module identity for Core <c>IException&lt;TSelf&gt;</c>.</param>
/// <param name="ExceptionInterfaceExportKey">The exact public TypeDef ExportKey for Core <c>IException&lt;TSelf&gt;</c>.</param>
/// <param name="UnreachableProducerIdentity">The exact producer Module identity for Core <c>UnreachableException</c>.</param>
/// <param name="UnreachableExportKey">The exact public TypeDef ExportKey for Core <c>UnreachableException</c>.</param>
public sealed record CoreExceptionIdentitySelection(
    string UsingModuleIdentity,
    string ExceptionInterfaceProducerIdentity,
    string ExceptionInterfaceExportKey,
    string UnreachableProducerIdentity,
    string UnreachableExportKey);

internal static class CoreExceptionIdentitySelectionContract
{
    private static readonly UTF8Encoding s_utf8 = new(false, true);

    internal static bool IsValidTable(
        ImmutableArray<CoreExceptionIdentitySelection> selections)
    {
        if (selections.IsDefaultOrEmpty)
        {
            return true;
        }

        var usingModules = new HashSet<string>(StringComparer.Ordinal);
        foreach (var selection in selections)
        {
            if (selection is null
                || !IsValidIdentity(selection.UsingModuleIdentity)
                || !IsValidIdentity(
                    selection.ExceptionInterfaceProducerIdentity)
                || !IsValidIdentity(selection.ExceptionInterfaceExportKey)
                || !IsValidIdentity(selection.UnreachableProducerIdentity)
                || !IsValidIdentity(selection.UnreachableExportKey)
                || !usingModules.Add(selection.UsingModuleIdentity))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsValidIdentity(string? identity)
    {
        if (string.IsNullOrEmpty(identity)
            || identity.Contains('\0', StringComparison.Ordinal))
        {
            return false;
        }

        try
        {
            _ = s_utf8.GetByteCount(identity);
            return true;
        }
        catch (EncoderFallbackException)
        {
            return false;
        }
    }
}
