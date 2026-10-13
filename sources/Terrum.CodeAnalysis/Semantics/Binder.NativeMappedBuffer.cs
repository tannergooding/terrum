// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System.Linq;
using Terrum.CodeAnalysis.Syntax;

namespace Terrum.CodeAnalysis.Semantics;

internal sealed partial class Binder
{
    private BoundExpression BindNativeMappedByteRead(SyntaxNode node)
    {
        var arguments = ((SyntaxNode)node.Children[1]).Children.OfType<SyntaxNode>().ToArray();
        if (arguments.Length != 2 || arguments.Any(static argument =>
                argument.Kind != SyntaxKind.Argument || argument.Children is not [SyntaxNode]))
        {
            Report("TRM3030",
                "nativebyte requires a mapped local and a uint64 byte index.",
                node.Span);
            return ErrorExpression(node.Span);
        }

        var mappedExpression = (SyntaxNode)arguments[0].Children[0];
        while (mappedExpression.Kind == SyntaxKind.ParenthesizedExpression)
        {
            mappedExpression = (SyntaxNode)mappedExpression.Children[1];
        }
        if (mappedExpression.Kind != SyntaxKind.NameExpression
            || !TryFindVariable(NameText(mappedExpression), out var variable)
            || !NativeMappedBufferContract.TryGetDefinition(_variables[variable].Type,
                _nativeImportCatalog, _structs, out var mapMethod, out var mappedBuffer)
            || mapMethod is null || mappedBuffer is null)
        {
            Report("TRM3030",
                "nativebyte requires a local mapped-output aggregate from the selected no-escape native catalog.",
                node.Span);
            return ErrorExpression(node.Span);
        }

        var index = BindExpression((SyntaxNode)arguments[1].Children[0], PrimitiveType.UInt64);
        if (index.Type != PrimitiveType.UInt64)
        {
            Report("TRM3030", "nativebyte requires a uint64 index.", node.Span);
            return ErrorExpression(node.Span);
        }

        return new BoundNativeMappedByteRead(variable, index, mappedBuffer.SourceType,
            mappedBuffer.PointerOffset, mappedBuffer.LengthOffset, mapMethod.Identity,
            mappedBuffer.UnmapMethodIdentity, node.Span);
    }
}
