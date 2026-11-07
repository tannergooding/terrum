// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.IO;

namespace Terrum.CodeAnalysis.Modules;

internal static class TrRmIntrinsicRegistrationProviderBindingValidator
{
    internal static void Validate(
        TrRmStructuralMetadataValidator.ValidatedExecutionTables consumer,
        IReadOnlyList<byte[]> providerArtifacts)
    {
        var boundImports = new byte[consumer.Imports.Count + 1];
        for (var providerArtifactIndex = 0;
            providerArtifactIndex < providerArtifacts.Count;
            providerArtifactIndex++)
        {
            var providerArtifact = providerArtifacts[providerArtifactIndex];
            ArgumentNullException.ThrowIfNull(providerArtifact);
            var provider = TrRmStructuralMetadataValidator
                .ValidateGeneralPredicateGraphsDraftAndGetExecutionTables(
                    TrRmArtifactIndex.ForGeneralPredicateGraphsDraft(
                        providerArtifact));
            for (var providerModule = 1;
                providerModule <= provider.Modules.Count; providerModule++)
            {
                var producerIdentity = provider.Modules.GetIdentity(providerModule);
                for (var import = 1; import <= consumer.Imports.Count; import++)
                {
                    var expectedDefinition
                        = consumer.Imports.GetExpectedDefinitionForImportRow(import);
                    if ((expectedDefinition >> 24) != 0x26
                        || !consumer.Imports.TryGetExpectedBinding(
                            expectedDefinition, out var binding)
                        || !StringComparer.Ordinal.Equals(
                            producerIdentity, binding.ProducerIdentity))
                    {
                        continue;
                    }

                    if (boundImports[import] != 0
                        || provider.Modules.GetRequirements(providerModule)
                            != (binding.TargetRequirement, binding.RuntimePolicy)
                        || !provider.Exports.TryGetDefinitionForKey(providerModule,
                            binding.ExportKey, out var providerDefinition)
                        || (providerDefinition >> 24) != 0x26)
                    {
                        throw InvalidBinding();
                    }

                    var expectedRow = checked((int)(
                        expectedDefinition & 0x00FF_FFFF));
                    var providerRow = checked((int)(
                        providerDefinition & 0x00FF_FFFF));
                    if (consumer.IntrinsicRegistrations
                            .GetDeclarationRole(expectedRow) != 2
                        || providerRow <= 0
                        || providerRow > provider.IntrinsicRegistrations.Count
                        || provider.IntrinsicRegistrations
                            .GetDeclarationRole(providerRow) != 1
                        || provider.IntrinsicRegistrations
                            .GetOwnerModuleRow(providerRow) != providerModule
                        || provider.IntrinsicRegistrations
                            .GetVisibility(providerRow) != 2
                        || consumer.IntrinsicRegistrations
                            .GetRegistrationId(expectedRow)
                            != provider.IntrinsicRegistrations
                                .GetRegistrationId(providerRow)
                        || consumer.IntrinsicRegistrations
                            .GetRuleRevision(expectedRow)
                            != provider.IntrinsicRegistrations
                                .GetRuleRevision(providerRow))
                    {
                        throw InvalidBinding();
                    }

                    boundImports[import] = 1;
                }
            }
        }

        for (var import = 1; import < boundImports.Length; import++)
        {
            if ((consumer.Imports.GetExpectedDefinitionForImportRow(import)
                    >> 24) == 0x26
                && boundImports[import] == 0)
            {
                throw InvalidBinding();
            }
        }
    }

    private static InvalidDataException InvalidBinding()
        => new("Invalid TRRM intrinsic registration provider binding.");
}
