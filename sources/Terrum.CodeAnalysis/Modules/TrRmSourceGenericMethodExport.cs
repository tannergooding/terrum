// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Text;

namespace Terrum.CodeAnalysis.Modules;

/// <summary>Explicitly assigns an ExportKey to one source-authored generic method declaration.</summary>
/// <param name="Program">The exact checked program containing the declaration.</param>
/// <param name="SourceInputOrdinal">The declaration's zero-based source input ordinal.</param>
/// <param name="DeclarationSpan">The exact full declaration span.</param>
/// <param name="ExportKey">The producer-selected TRRM export key.</param>
public sealed record TrRmSourceGenericMethodExport(
    BoundProgram Program,
    int SourceInputOrdinal,
    TextSpan DeclarationSpan,
    string ExportKey);

public static partial class TrRmExecutableCompiler
{
    internal sealed record SourceGenericMethodExport(
        BoundGenericMethodDefinition Definition, string ExportKey);

    internal sealed class SourceGenericMethodExports
    {
        private readonly Dictionary<int, ImmutableArray<string>>
            _keysByDefinitionOrdinal;

        private SourceGenericMethodExports(BoundProgram program,
            ImmutableArray<SourceGenericMethodExport> exports)
        {
            Program = program;
            Exports = exports;
            _keysByDefinitionOrdinal = exports
                .GroupBy(static export => export.Definition.DefinitionOrdinal)
                .ToDictionary(static group => group.Key,
                    static group => group.Select(
                        static export => export.ExportKey).ToImmutableArray());
        }

        internal BoundProgram Program { get; }
        internal ImmutableArray<SourceGenericMethodExport> Exports { get; }

        internal ImmutableArray<string> GetKeys(
            BoundGenericMethodDefinition definition)
            => _keysByDefinitionOrdinal.TryGetValue(
                definition.DefinitionOrdinal, out var keys)
                ? keys : [];

        internal static SourceGenericMethodExports Capture(
            BoundProgram program, string moduleIdentity,
            IReadOnlyList<TrRmSourceGenericMethodExport> methodExports)
        {
            ArgumentNullException.ThrowIfNull(program);
            ArgumentException.ThrowIfNullOrEmpty(moduleIdentity);
            ArgumentNullException.ThrowIfNull(methodExports);

            var existingExportKeys = new HashSet<string>(
                StringComparer.Ordinal);
            foreach (var function in program.Functions)
            {
                if (!function.IsPrivate && !function.IsInternal
                    && function.ModuleImport is null
                    && function.GenericMethodInstantiation is null
                    && !existingExportKeys.Add(function.Name))
                {
                    throw new ArgumentException(
                        "The checked program contains duplicate local ExportKeys.",
                        nameof(methodExports));
                }
            }

            foreach (var definition in program.EnumDefinitions)
            {
                if (definition.Identity
                        is BoundEnumDeclarationIdentity.Core core
                    && StringComparer.Ordinal.Equals(
                        core.Value.ProviderIdentity, moduleIdentity)
                    && !existingExportKeys.Add(core.Value.ExportKey))
                {
                    throw new ArgumentException(
                        "The checked program contains duplicate local ExportKeys.",
                        nameof(methodExports));
                }
            }

            var snapshot = new TrRmSourceGenericMethodExport[
                methodExports.Count];
            for (var index = 0; index < snapshot.Length; index++)
            {
                var export = methodExports[index]
                    ?? throw new ArgumentException(
                        "Source generic method mappings cannot contain null entries.",
                        nameof(methodExports));
                snapshot[index] = export with { };
            }

            var selected = new HashSet<int>();
            var exports = ImmutableArray.CreateBuilder<
                SourceGenericMethodExport>(snapshot.Length);
            foreach (var export in snapshot)
            {
                if (!ReferenceEquals(export.Program, program)
                    || export.SourceInputOrdinal < 0
                    || export.SourceInputOrdinal >= program.SourceInputCount
                    || export.DeclarationSpan.Start < 0
                    || export.DeclarationSpan.Length <= 0)
                {
                    throw new ArgumentException(
                        "A source generic method mapping does not identify this checked program.",
                        nameof(methodExports));
                }

                try
                {
                    _ = checked(export.DeclarationSpan.Start
                        + export.DeclarationSpan.Length);
                }
                catch (OverflowException)
                {
                    throw new ArgumentException(
                        "A source generic method declaration span overflows.",
                        nameof(methodExports));
                }

                var matching = program.GenericMethodDefinitions
                    .Where(definition =>
                        definition.SourceInputOrdinal
                            == export.SourceInputOrdinal
                        && definition.Span == export.DeclarationSpan)
                    .ToArray();
                if (matching.Length != 1
                    || !matching[0].IsPublic
                    || matching[0].FormalOrder.IsEmpty
                    || matching[0].DefinitionOrdinal < 0
                    || matching[0].FunctionIndex < 0
                    || matching[0].FunctionIndex
                        >= matching[0].ProofProgram.Functions.Length)
                {
                    throw new ArgumentException(
                        "A source generic method mapping must select one public local open generic definition.",
                        nameof(methodExports));
                }

                var definition = matching[0];
                var proofFunction = definition.ProofProgram.Functions[
                    definition.FunctionIndex];
                if (proofFunction.GenericMethodProofDefinitionOrdinal
                    != definition.DefinitionOrdinal)
                {
                    throw new ArgumentException(
                        "A source generic method mapping does not match its checked proof definition.",
                        nameof(methodExports));
                }

                if (string.IsNullOrEmpty(export.ExportKey)
                    || export.ExportKey.Contains('\0',
                        StringComparison.Ordinal)
                    || !IsValidSourceGenericMethodExportKey(
                        export.ExportKey)
                    || !existingExportKeys.Add(export.ExportKey))
                {
                    throw new ArgumentException(
                        "Source generic method ExportKeys must be valid and unique within the Module.",
                        nameof(methodExports));
                }

                _ = selected.Add(definition.DefinitionOrdinal);
                exports.Add(new SourceGenericMethodExport(definition,
                    export.ExportKey));
            }

            foreach (var definition in program.GenericMethodDefinitions)
            {
                if (definition.IsPublic
                    && !selected.Contains(definition.DefinitionOrdinal))
                {
                    throw new NotSupportedException(
                        "Every retained public generic method requires an explicit source ExportKey.");
                }
            }

            return new SourceGenericMethodExports(program,
                exports.MoveToImmutable());
        }
    }

    private static bool IsValidSourceGenericMethodExportKey(string key)
    {
        try
        {
            var byteCount = s_sourceTypeExportKeyEncoding.GetByteCount(key);
            return byteCount is >= 1 and <= 0xFF_FFFF;
        }
        catch (EncoderFallbackException)
        {
            return false;
        }
    }
}
