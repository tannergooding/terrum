// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;

namespace Terrum.CodeAnalysis.Semantics;

/// <summary>An exact provider Module and public export key for a selected contract-failure declaration.</summary>
/// <param name="ProviderIdentity">The selected provider's semantic Module identity.</param>
/// <param name="ExportKey">The declaration's exact public export key.</param>
public readonly record struct CoreContractFailureDeclarationIdentity(
    string ProviderIdentity,
    string ExportKey);

/// <summary>
/// Host-selected Core default-failure classes and RequireException schema for
/// one using logical Module.
/// </summary>
/// <param name="UsingModuleIdentity">The exact logical Module that owns the selection.</param>
/// <param name="RequirementExceptionClass">The Core RequirementException class identity, when used.</param>
/// <param name="PostconditionExceptionClass">The Core PostconditionException class identity, when used.</param>
/// <param name="RequireExceptionSchema">The Core RequireException semantic schema identity, when used.</param>
/// <remarks>This is an explicit binding input, not a serialized record or authentication claim.</remarks>
public sealed record CoreContractFailureIdentitySelection(
    string UsingModuleIdentity,
    CoreContractFailureDeclarationIdentity? RequirementExceptionClass,
    CoreContractFailureDeclarationIdentity? PostconditionExceptionClass,
    CoreContractFailureDeclarationIdentity? RequireExceptionSchema);

internal static class CoreContractFailureIdentitySelectionContract
{
    private static readonly UTF8Encoding s_utf8 = new(false, true);

    internal static bool IsValidTable(
        ImmutableArray<CoreContractFailureIdentitySelection> selections)
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
                || !usingModules.Add(selection.UsingModuleIdentity)
                || (selection.RequirementExceptionClass is null
                    && selection.PostconditionExceptionClass is null
                    && selection.RequireExceptionSchema is null)
                || (selection.RequirementExceptionClass is
                        CoreContractFailureDeclarationIdentity requirement
                    && !IsValidDeclaration(requirement))
                || (selection.PostconditionExceptionClass is
                        CoreContractFailureDeclarationIdentity postcondition
                    && !IsValidDeclaration(postcondition))
                || (selection.RequireExceptionSchema is
                        CoreContractFailureDeclarationIdentity schema
                    && !IsValidDeclaration(schema))
                || (selection.RequirementExceptionClass is
                        CoreContractFailureDeclarationIdentity selectedRequirement
                    && selection.PostconditionExceptionClass is
                        CoreContractFailureDeclarationIdentity selectedPostcondition
                    && selectedRequirement == selectedPostcondition))
            {
                return false;
            }

            var exports = new HashSet<(string Provider, string Key)>();
            if ((selection.RequirementExceptionClass is
                    CoreContractFailureDeclarationIdentity requirementIdentity
                    && !exports.Add((requirementIdentity.ProviderIdentity,
                        requirementIdentity.ExportKey)))
                || (selection.PostconditionExceptionClass is
                    CoreContractFailureDeclarationIdentity postconditionIdentity
                    && !exports.Add((postconditionIdentity.ProviderIdentity,
                        postconditionIdentity.ExportKey)))
                || (selection.RequireExceptionSchema is
                    CoreContractFailureDeclarationIdentity schemaIdentity
                    && !exports.Add((schemaIdentity.ProviderIdentity,
                        schemaIdentity.ExportKey))))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsValidDeclaration(
        CoreContractFailureDeclarationIdentity identity)
        => IsValidIdentity(identity.ProviderIdentity)
            && IsValidIdentity(identity.ExportKey);

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
