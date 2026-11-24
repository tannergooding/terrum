// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;

namespace Terrum.CodeAnalysis.Modules;

/// <summary>Selects the exact Core exception interface for one originating Module.</summary>
/// <param name="UsingModuleIdentity">The original logical Module that owns the failure contract.</param>
/// <param name="ProviderIdentity">The selected provider Module identity.</param>
/// <param name="ExportKey">The exact public interface TypeDef ExportKey.</param>
/// <remarks>This is a nonserialized binding input, not a Core name lookup or authentication claim.</remarks>
public sealed record TrRmExceptionInterfaceBinding(
    string UsingModuleIdentity, string ProviderIdentity, string ExportKey);

internal static class TrRmExceptionInterfaceBindingContract
{
    private static readonly UTF8Encoding s_utf8 = new(false, true);

    internal static bool IsValidTable(
        ImmutableArray<TrRmExceptionInterfaceBinding> bindings)
    {
        if (bindings.IsDefaultOrEmpty)
        {
            return true;
        }

        var usingModules = new HashSet<string>(StringComparer.Ordinal);
        foreach (var binding in bindings)
        {
            if (binding is null
                || !IsValidIdentity(binding.UsingModuleIdentity)
                || !usingModules.Add(binding.UsingModuleIdentity)
                || !IsValidIdentity(binding.ProviderIdentity)
                || !IsValidIdentity(binding.ExportKey))
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
