using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Terrum.CodeAnalysis.Text;

namespace Terrum.CodeAnalysis.Modules;

/// <summary>Assigns export aliases and member keys to one checked source enum declaration.</summary>
/// <param name="Program">The exact checked program containing the declaration.</param>
/// <param name="SourceInputOrdinal">The declaration's zero-based source input ordinal.</param>
/// <param name="DeclarationSpan">The exact primary EnumDeclaration span.</param>
/// <param name="ExportKeys">The explicit TypeDef aliases; empty means unpublished.</param>
/// <param name="MemberDeclarationKeys">The explicit member keys in source declaration order.</param>
public sealed record TrRmSourceEnumDeclarationExport(
    BoundProgram Program,
    int SourceInputOrdinal,
    TextSpan DeclarationSpan,
    ImmutableArray<string> ExportKeys,
    ImmutableArray<string> MemberDeclarationKeys);

public static partial class TrRmExecutableCompiler
{
    internal sealed record C1EnumClosureSourceEnumExport(
        int EnumIndex,
        BoundEnumDefinition Definition,
        BoundSourceEnumDeclarationHandle Declaration,
        ImmutableArray<string> ExportKeys,
        ImmutableArray<BoundSourceEnumMemberHandle> MemberDeclarations,
        ImmutableArray<string> MemberDeclarationKeys);

    internal sealed class C1EnumClosureSourceEnumExports
    {
        private readonly Dictionary<BoundSourceEnumDeclarationHandle,
            C1EnumClosureSourceEnumExport> _byDeclaration;

        private C1EnumClosureSourceEnumExports(BoundProgram program,
            ImmutableArray<C1EnumClosureSourceEnumExport> exports)
        {
            Program = program;
            Exports = exports;
            _byDeclaration = [];
            foreach (var export in exports)
            {
                _byDeclaration.Add(export.Declaration, export);
            }
        }

        internal BoundProgram Program { get; }
        internal ImmutableArray<C1EnumClosureSourceEnumExport> Exports
            { get; }

        internal static C1EnumClosureSourceEnumExports Capture(
            BoundProgram program,
            IReadOnlyList<TrRmSourceEnumDeclarationExport> enumExports)
        {
            ArgumentNullException.ThrowIfNull(program);
            ArgumentNullException.ThrowIfNull(enumExports);
            if (!BoundEnumTypes.IsValidTable(program.EnumDefinitions))
            {
                throw new ArgumentException(
                    "The checked enum table is invalid.",
                    nameof(program));
            }

            var association = program.SourceEnumProgramAssociation;
            var sourceDefinitionCount = program.EnumDefinitions.Count(
                static definition => definition.Identity
                    is BoundEnumDeclarationIdentity.Source);
            if (association is null
                || !ReferenceEquals(association.Program, program)
                || association.Declarations.IsDefault
                || association.Declarations.Length != sourceDefinitionCount)
            {
                throw new ArgumentException(
                    "The checked program has no exact compiler-owned source enum association.",
                    nameof(enumExports));
            }

            var declarations = new Dictionary<
                (int SourceInputOrdinal, TextSpan Span),
                BoundSourceEnumDeclarationAssociation>();
            var declarationHandles
                = new HashSet<BoundSourceEnumDeclarationHandle>();
            var enumIndexes = new HashSet<int>();
            foreach (var declaration in association.Declarations)
            {
                if ((uint)declaration.EnumIndex
                        >= (uint)program.EnumDefinitions.Length
                    || !enumIndexes.Add(declaration.EnumIndex)
                    || (uint)declaration.SourceInputOrdinal
                        >= (uint)program.SourceInputCount
                    || declaration.Span.Start < 0
                    || declaration.Span.Length <= 0
                    || !ReferenceEquals(program.EnumDefinitions[
                        declaration.EnumIndex],
                        declaration.DeclarationSnapshot)
                    || declaration.DeclarationSnapshot.Identity
                        is not BoundEnumDeclarationIdentity.Source source
                    || !source.Value.IsValid
                    || !declarationHandles.Add(source.Value))
                {
                    throw new ArgumentException(
                        "A source enum association does not identify one exact declaration in this checked program.",
                        nameof(enumExports));
                }

                var primary = source.Value.Parts.SingleOrDefault(
                    static part => part.IsPrimary);
                if (primary is null
                    || primary.SourceInputOrdinal
                        != declaration.SourceInputOrdinal
                    || primary.Declaration.Span != declaration.Span
                    || !ReferenceEquals(primary.Tree, declaration.Tree)
                    || !ReferenceEquals(primary.Declaration,
                        declaration.Declaration)
                    || declaration.DeclarationSnapshot.Name
                        != source.Value.QualifiedName
                    || declaration.DeclarationSnapshot.IsPublic
                        != source.Value.IsPublic
                    || !declaration.DeclarationSnapshot.IsPrimary
                    || declaration.DeclarationSnapshot.ModuleImport is not null
                    || !HasSourceMemberOrder(
                        declaration.DeclarationSnapshot, source.Value))
                {
                    throw new ArgumentException(
                        "A source enum association does not match its checked primary and member declarations.",
                        nameof(enumExports));
                }

                ValidateSpan(declaration.Tree, declaration.SourceInputOrdinal,
                    declaration.Span, program.SourceInputCount,
                    nameof(enumExports));
                if (!declarations.TryAdd(
                    (declaration.SourceInputOrdinal, declaration.Span),
                    declaration))
                {
                    throw new ArgumentException(
                        "The checked program contains duplicate source enum selectors.",
                        nameof(enumExports));
                }
            }

            var snapshots
                = new TrRmSourceEnumDeclarationExport[enumExports.Count];
            for (var index = 0; index < snapshots.Length; index++)
            {
                var export = enumExports[index]
                    ?? throw new ArgumentException(
                        "Source enum mappings cannot contain null entries.",
                        nameof(enumExports));
                snapshots[index] = export with {
                    ExportKeys = export.ExportKeys.IsDefault
                        ? default : [.. export.ExportKeys],
                    MemberDeclarationKeys =
                        export.MemberDeclarationKeys.IsDefault
                            ? default : [.. export.MemberDeclarationKeys],
                };
            }

            var selected = new HashSet<BoundSourceEnumDeclarationHandle>();
            var result
                = ImmutableArray.CreateBuilder<
                    C1EnumClosureSourceEnumExport>(snapshots.Length);
            foreach (var export in snapshots)
            {
                if (!ReferenceEquals(export.Program, program)
                    || export.SourceInputOrdinal < 0
                    || export.DeclarationSpan.Start < 0
                    || export.DeclarationSpan.Length <= 0
                    || export.ExportKeys.IsDefault
                    || export.MemberDeclarationKeys.IsDefault
                    || !declarations.TryGetValue(
                        (export.SourceInputOrdinal, export.DeclarationSpan),
                        out var declaration))
                {
                    throw new ArgumentException(
                        "A source enum mapping must identify this checked program's exact primary declaration.",
                        nameof(enumExports));
                }

                ValidateSpan(declaration.Tree, export.SourceInputOrdinal,
                    export.DeclarationSpan, program.SourceInputCount,
                    nameof(enumExports));
                var definition = declaration.DeclarationSnapshot;
                if (definition.Identity
                    is not BoundEnumDeclarationIdentity.Source identity)
                {
                    throw new ArgumentException(
                        "A source enum mapping no longer identifies a source declaration.",
                        nameof(enumExports));
                }

                if (!selected.Add(identity.Value)
                    || export.MemberDeclarationKeys.Length
                        != definition.Members.Length)
                {
                    throw new ArgumentException(
                        "Source enum declarations must be selected once with one key per declared member.",
                        nameof(enumExports));
                }

                ValidateExportKeys(export.ExportKeys, definition,
                    identity.Value, nameof(enumExports));
                var memberDeclarations
                    = ImmutableArray.CreateBuilder<
                        BoundSourceEnumMemberHandle>(
                        definition.Members.Length);
                var memberKeys = new HashSet<string>(StringComparer.Ordinal);
                for (var memberIndex = 0;
                    memberIndex < definition.Members.Length; memberIndex++)
                {
                    var member = definition.Members[memberIndex];
                    var key = export.MemberDeclarationKeys[memberIndex];
                    if (member.Identity
                            is not BoundEnumMemberIdentity.Source memberSource
                        || member.DeclaringEnum
                            is not BoundEnumDeclarationIdentity.Source owner
                        || !ReferenceEquals(memberSource.Value.Owner,
                            identity.Value)
                        || !ReferenceEquals(owner.Value, identity.Value)
                        || !BoundEnumTypes.IsValidModuleDeclarationKey(key)
                        || !memberKeys.Add(key))
                    {
                        throw new ArgumentException(
                            "A source enum member-key assignment does not match its unique member declaration.",
                            nameof(enumExports));
                    }

                    memberDeclarations.Add(memberSource.Value);
                }

                result.Add(new C1EnumClosureSourceEnumExport(
                    declaration.EnumIndex, definition, identity.Value,
                    export.ExportKeys,
                    memberDeclarations.MoveToImmutable(),
                    export.MemberDeclarationKeys));
            }

            if (selected.Count != declarations.Count)
            {
                throw new NotSupportedException(
                    "Every retained source enum requires one explicit EN1 mapping, including unused declarations.");
            }

            return new C1EnumClosureSourceEnumExports(program,
                result.MoveToImmutable());
        }

        internal bool TryGet(BoundEnumDefinition definition,
            out C1EnumClosureSourceEnumExport export)
        {
            if (definition.Identity
                    is BoundEnumDeclarationIdentity.Source source
                && _byDeclaration.TryGetValue(source.Value, out export!)
                && ReferenceEquals(export.Definition, definition))
            {
                return true;
            }

            export = null!;
            return false;
        }

        private static bool HasSourceMemberOrder(
            BoundEnumDefinition definition,
            BoundSourceEnumDeclarationHandle declaration)
        {
            var sourceMembers = declaration.Parts
                .OrderBy(static part => part.SourceInputOrdinal)
                .SelectMany(static part => part.Declaration.Children
                    .OfType<SyntaxNode>()
                    .Where(static child => child.Kind
                        == SyntaxKind.EnumMemberDeclaration)
                    .Select(child => (part.Tree, Declaration: child)))
                .ToArray();
            if (sourceMembers.Length != definition.Members.Length)
            {
                return false;
            }

            for (var index = 0; index < sourceMembers.Length; index++)
            {
                if (definition.Members[index].Identity
                        is not BoundEnumMemberIdentity.Source source
                    || !ReferenceEquals(source.Value.Owner, declaration)
                    || !ReferenceEquals(source.Value.Tree,
                        sourceMembers[index].Tree)
                    || !ReferenceEquals(source.Value.Declaration,
                        sourceMembers[index].Declaration))
                {
                    return false;
                }
            }

            return true;
        }

        private static void ValidateExportKeys(
            ImmutableArray<string> exportKeys,
            BoundEnumDefinition definition,
            BoundSourceEnumDeclarationHandle declaration,
            string parameterName)
        {
            if (!definition.IsPublic || !declaration.IsPublic
                || declaration.IsFileLocal)
            {
                if (!exportKeys.IsEmpty)
                {
                    throw new ArgumentException(
                        "Only public non-file-local source enums may have ExportKey aliases.",
                        parameterName);
                }

                return;
            }

            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var key in exportKeys)
            {
                if (!BoundEnumTypes.IsValidModuleDeclarationKey(key)
                    || !keys.Add(key))
                {
                    throw new ArgumentException(
                        "Source enum ExportKeys must be valid, unique Module aliases.",
                        parameterName);
                }
            }
        }

        private static void ValidateSpan(SyntaxTree tree,
            int sourceInputOrdinal, TextSpan span, int sourceInputCount,
            string parameterName)
        {
            int end;
            try
            {
                end = checked(span.Start + span.Length);
            }
            catch (OverflowException)
            {
                throw new ArgumentException(
                    "A source enum declaration span overflows.",
                    parameterName);
            }

            if ((uint)sourceInputOrdinal >= (uint)sourceInputCount
                || span.Start < 0 || span.Length <= 0
                || end > tree.Source.Length)
            {
                throw new ArgumentException(
                    "A source enum declaration span is outside its checked UTF-16 snapshot.",
                    parameterName);
            }
        }
    }
}
