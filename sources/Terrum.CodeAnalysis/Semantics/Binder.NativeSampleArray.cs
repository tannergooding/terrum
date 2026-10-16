// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using Terrum.CodeAnalysis.Lexing;
using Terrum.CodeAnalysis.Syntax;
using Terrum.CodeAnalysis.Text;

namespace Terrum.CodeAnalysis.Semantics;

internal sealed partial class Binder
{
    private bool TryBindNativeFixedArrayType(SyntaxNode node, out PrimitiveType arrayType,
        out PrimitiveType elementType, out int elementCount)
    {
        arrayType = PrimitiveType.Int32;
        elementType = PrimitiveType.Int32;
        elementCount = 0;
        if (!NativeFixedArrayContract.SupportsRuntimePolicy(_runtimePolicy))
        {
            Report("TRM3030", "InlineArray is not supported under the selected runtime policy.",
                node.Span);
            return false;
        }
        if (node.Kind != SyntaxKind.InlineArrayTypeName
            || node.Children is not [
                SyntaxTokenElement { Token.Kind: TokenKind.Identifier, Token.Text: "InlineArray" },
                SyntaxTokenElement { Token.Kind: TokenKind.Less },
                SyntaxNode elementNode,
                SyntaxTokenElement { Token.Kind: TokenKind.Comma },
                SyntaxTokenElement { Token.Kind: TokenKind.NumericLiteral } extentToken,
                SyntaxTokenElement { Token.Kind: TokenKind.Greater },
            ])
        {
            Report("TRM3030", "InlineArray requires a fixed numeric extent.", node.Span);
            return false;
        }

        var extent = extentToken.Token.Text;
        var resolvedElement = BindType(elementNode, allowVoid: false);
        if (int.TryParse(extent, NumberStyles.None, CultureInfo.InvariantCulture, out var resolvedCount)
            && NativeFixedArrayContract.TryGetLayout(PrimitiveType.NativeFixedArray, resolvedElement,
                resolvedCount, _structs, out _))
        {
            arrayType = PrimitiveType.NativeFixedArray;
            elementType = resolvedElement;
            elementCount = resolvedCount;
            return true;
        }

        Report("TRM3030", "InlineArray must have a valid fixed-array layout under the selected target.",
            node.Span);
        return false;
    }

    private BoundExpression BindNativeFixedArrayInitializer(SyntaxNode node, PrimitiveType arrayType,
        PrimitiveType elementType, int elementCount)
    {
        if (!NativeFixedArrayContract.TryGetLayout(arrayType, elementType, elementCount, _structs,
            out var layout))
        {
            Report("TRM3030", "The fixed-array carrier has no valid layout.", node.Span);
            return ErrorExpression(node.Span);
        }
        var count = layout.ElementCount;
        var elements = node.Children.OfType<SyntaxNode>().ToArray();
        if (node.Kind != SyntaxKind.ArrayInitializerExpression
            || node.Children[0] is not SyntaxTokenElement { Token.Kind: TokenKind.OpenBracket }
            || node.Children[^1] is not SyntaxTokenElement { Token.Kind: TokenKind.CloseBracket })
        {
            Report("TRM3030", "A fixed array requires an array initializer.", node.Span);
            return ErrorExpression(node.Span);
        }
        if (elements.Length == 0)
        {
            if (node.Children.Length == 2
                && NativeFixedArrayContract.AllowsEmptyInitialization(layout))
            {
                return new BoundNativeFixedArrayInitialization(layout, [], node.Span);
            }
            Report("TRM3030", "The selected fixed-array layout does not permit empty zero-initialization.",
                node.Span);
            return ErrorExpression(node.Span);
        }
        if (count == 0 || elements.Length != count || node.Children.Length != (2 * count) + 1)
        {
            Report("TRM3030", "A fixed array requires exactly one complete initializer per element.",
                node.Span);
            return ErrorExpression(node.Span);
        }
        for (var child = 1; child < node.Children.Length - 1; child++)
        {
            if ((child & 1) == 0
                    ? node.Children[child] is not SyntaxTokenElement { Token.Kind: TokenKind.Comma }
                    : node.Children[child] is not SyntaxNode)
            {
                Report("TRM3030", "Fixed-array elements must be complete and source ordered.", node.Span);
                return ErrorExpression(node.Span);
            }
        }

        var bound = ImmutableArray.CreateBuilder<BoundExpression>(count);
        foreach (var element in elements)
        {
            if (element.Kind == SyntaxKind.MissingExpression)
            {
                Report("TRM3030", "Fixed-array elements must be completely initialized.", element.Span);
                return ErrorExpression(node.Span);
            }
            bound.Add(BindExpression(element, elementType));
        }
        if (bound.Any(element => element.Type != elementType))
        {
            Report("TRM3030", "Every fixed-array element must have its exact element type.", node.Span);
            return ErrorExpression(node.Span);
        }
        return new BoundNativeFixedArrayInitialization(layout, bound.MoveToImmutable(), node.Span);
    }

    private BoundExpression BindNativeFixedArrayPointerAddress(SyntaxNode node,
        PrimitiveType pointerType, TextSpan span)
    {
        if (node.Kind != SyntaxKind.IndexExpression
            || !TryFindNativeFixedArrayLocal((SyntaxNode)node.Children[0], out var variable)
            || _variables[variable].NativeFixedArrayLayout is not BoundNativeFixedArrayLayout layout)
        {
            Report("TRM3030", "A native pointer address requires an admitted fixed-array element.", span);
            return ErrorExpression(span);
        }

        var pointer = _nativePointerTypes[NativePointerTypes.Index(pointerType)];
        if (pointer.PointeeType != layout.ElementType
            || pointer.IsWritable && (!_variables[variable].IsMutable
                || !NativeFixedArrayContract.AllowsMutableLocal(layout)))
        {
            Report("TRM3030", "The fixed-array element does not match the native pointer access mode.", span);
            return ErrorExpression(span);
        }

        var index = BindNativeFixedArrayIndex((SyntaxNode)node.Children[2], layout);
        if (index is BoundConstant constant && (constant.Value < 0 || constant.Value >= layout.ElementCount))
        {
            Report("TRM3030", $"Fixed-array element index must be in [0, {layout.ElementCount}).", index.Span);
        }
        if (_unsafeDepth == 0)
        {
            Report("TRM3028", "Taking a native pointer address requires an explicit unsafe context.", span);
        }

        return new BoundNativeFixedArrayPointerAddress(variable, layout, index, pointerType, span) {
            IsUnsafeAuthorized = _unsafeDepth != 0,
        };
    }

    private BoundExpression BindNativeFixedArrayIndex(SyntaxNode node, BoundNativeFixedArrayLayout layout)
    {
        var expectedType = NativeFixedArrayContract.AllowsUInt32Indices(layout)
            && KnownType(node) == PrimitiveType.UInt32
            ? PrimitiveType.UInt32 : PrimitiveType.Int32;
        return BindExpression(node, expectedType);
    }

}
