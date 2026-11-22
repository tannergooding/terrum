using System;
using System.Collections.Immutable;
using System.Linq;
using Terrum.CodeAnalysis.Syntax;

namespace Terrum.CodeAnalysis.Semantics;

internal sealed record BoundSourceInterfacePart(
    int SourceInputOrdinal, SyntaxTree Tree, SyntaxNode Declaration);

internal sealed class BoundSourceInterfaceDeclarationHandle(
    string qualifiedName,
    bool isPartial,
    ImmutableArray<BoundSourceInterfacePart> parts)
{
    internal string QualifiedName { get; } = qualifiedName;
    internal bool IsPartial { get; } = isPartial;
    internal ImmutableArray<BoundSourceInterfacePart> Parts { get; } = parts;

    internal bool IsValid
        => !string.IsNullOrWhiteSpace(QualifiedName)
            && !IsPartial
            && !Parts.IsDefaultOrEmpty
            && Parts.Length == 1
            && Parts.All(static part => part is not null
                && part.SourceInputOrdinal >= 0
                && part.Tree is not null
                && part.Declaration.Kind
                    == SyntaxKind.InterfaceDeclaration);

    internal bool ContainsMember(SyntaxTree tree, SyntaxNode member)
        => IsValid
            && member.Kind == SyntaxKind.FunctionDeclaration
            && Parts.Any(part => ReferenceEquals(part.Tree, tree)
                && part.Declaration.Children.OfType<SyntaxNode>()
                    .Any(child => ReferenceEquals(child, member)));
}

internal sealed class BoundSourceInterfaceMemberHandle(
    BoundSourceInterfaceDeclarationHandle owner,
    int ordinal,
    SyntaxTree tree,
    SyntaxNode declaration)
{
    internal BoundSourceInterfaceDeclarationHandle Owner { get; } = owner;
    internal int Ordinal { get; } = ordinal;
    internal SyntaxTree Tree { get; } = tree;
    internal SyntaxNode Declaration { get; } = declaration;

    internal bool IsValid
        => Ordinal >= 0
            && Owner.IsValid
            && Owner.ContainsMember(Tree, Declaration);
}
