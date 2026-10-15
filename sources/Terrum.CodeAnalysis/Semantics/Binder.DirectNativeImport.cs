// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using Terrum.CodeAnalysis.Syntax;
using Terrum.CodeAnalysis.Text;
using Terrum.CodeAnalysis.Lexing;

namespace Terrum.CodeAnalysis.Semantics;

internal sealed partial class Binder
{
    private BoundExpression BindDirectNativeOutputAddress(
        SyntaxNode expression, DirectNativeOutput output, TextSpan span)
    {
        while (expression.Kind == SyntaxKind.ParenthesizedExpression)
        {
            expression = (SyntaxNode)expression.Children[1];
        }

        if (expression is not {
                Kind: SyntaxKind.UnaryExpression,
            } || ((SyntaxTokenElement)expression.Children[0]).Token.Kind != TokenKind.Ampersand)
        {
            Report("TRM3030", "A direct native output requires the address of a writable local.", span);
            return ErrorExpression(span);
        }

        var target = (SyntaxNode)expression.Children[1];
        while (target.Kind == SyntaxKind.ParenthesizedExpression)
        {
            target = (SyntaxNode)target.Children[1];
        }
        if (target.Kind != SyntaxKind.NameExpression
            || !TryFindVariable(NameText(target), out var variable)
            || _variables[variable].Type != output.PointeeType
            || !_variables[variable].IsMutable
            || _unresolvedVacantLocals.Contains(variable)
            || _variables[variable].IsInitializationLocal)
        {
            Report("TRM3030",
                "A direct native output address requires an initialized, mutable local with the exact pointee type.",
                span);
            return ErrorExpression(span);
        }

        var extent = ValueLayout.Size(output.PointeeType, _structs);
        return new BoundNativeStorageAddress(variable,
            new BoundConstant(0, PrimitiveType.Int32, span), output.PointeeType, output.PointeeType,
            PrimitiveType.NativeStorageAddress, 1, extent, extent, IsReadonly: false, Span: span);
    }
}
