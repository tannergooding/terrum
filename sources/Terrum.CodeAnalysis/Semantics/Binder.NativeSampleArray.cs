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
        var hasFixedArrayStorage = _sourceProfile.Has(NativeSourceFeature.NativeFixedArrayStorage);
        if (!hasFixedArrayStorage
            || node.Kind != SyntaxKind.InlineArrayTypeName
            || node.Children is not [
                SyntaxTokenElement { Token.Kind: TokenKind.Identifier, Token.Text: "InlineArray" },
                SyntaxTokenElement { Token.Kind: TokenKind.Less },
                SyntaxNode elementNode,
                SyntaxTokenElement { Token.Kind: TokenKind.Comma },
                SyntaxTokenElement { Token.Kind: TokenKind.NumericLiteral } extentToken,
                SyntaxTokenElement { Token.Kind: TokenKind.Greater },
            ])
        {
            Report("TRM3030", "InlineArray requires a native fixed-array layout admitted by the active catalog.",
                node.Span);
            return false;
        }

        var extent = extentToken.Token.Text;
        var resolvedElement = BindType(elementNode, allowVoid: false);
        if (int.TryParse(extent, NumberStyles.None, CultureInfo.InvariantCulture, out var resolvedCount)
            && NativeFixedArrayContract.TryGetLayout(PrimitiveType.NativeFixedArray, resolvedElement,
                resolvedCount, _structs, _nativeImportCatalog, out _))
        {
            arrayType = PrimitiveType.NativeFixedArray;
            elementType = resolvedElement;
            elementCount = resolvedCount;
            return true;
        }

        Report("TRM3030", "InlineArray must match an exact selected native fixed-array layout.", node.Span);
        return false;
    }

    private BoundExpression BindNativeFixedArrayInitializer(SyntaxNode node, PrimitiveType arrayType,
        PrimitiveType elementType, int elementCount)
    {
        if (!NativeFixedArrayContract.TryGetLayout(arrayType, elementType, elementCount, _structs,
            _nativeImportCatalog, out var layout))
        {
            Report("TRM3030", "The selected native fixed-array carrier has no admitted layout.", node.Span);
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
                && NativeFixedArrayContract.AllowsEmptyInitialization(layout, _structs, _nativeImportCatalog))
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

    private BoundExpression BindNativeFixedArrayUtf16ZAddress(SyntaxNode node, TextSpan span)
    {
        if (node.Kind != SyntaxKind.IndexExpression
            || !TryFindNativeFixedArrayLocal((SyntaxNode)node.Children[0], out var variable)
            || _variables[variable].NativeFixedArrayLayout is not BoundNativeFixedArrayLayout layout
            || !NativeFixedArrayContract.AllowsReadonlyUtf16ZLoan(
                layout, _structs, _nativeImportCatalog))
        {
            Report("TRM3030", "A UTF-16Z address requires a selected fixed-array layout that permits the loan.",
                span);
            return ErrorExpression(span);
        }
        var index = BindNativeFixedArrayIndex((SyntaxNode)node.Children[2], layout);
        if (index.Type != PrimitiveType.Int32
            && !(index.Type == PrimitiveType.UInt32
                && NativeFixedArrayContract.AllowsUInt32Indices(layout, _structs, _nativeImportCatalog)))
        {
            Report("TRM3030", "The selected fixed-array layout does not permit this index type.", index.Span);
        }
        if (index is not BoundConstant { Type: PrimitiveType.Int32, Value: 0 })
        {
            Report("TRM3030", "A UTF-16Z fixed-array loan must begin at code unit zero.", index.Span);
        }
        return new BoundNativeStorageAddress(variable, index, layout.CarrierType, layout.ElementType,
            PrimitiveType.NativeUtf16ZPointer, layout.ElementCount, layout.ElementStride,
            layout.ByteExtent, IsReadonly: true, Span: span);
    }

    private BoundExpression BindNativeFixedArrayIndex(SyntaxNode node, BoundNativeFixedArrayLayout layout)
    {
        var expectedType = NativeFixedArrayContract.AllowsUInt32Indices(layout, _structs, _nativeImportCatalog)
            && KnownType(node) == PrimitiveType.UInt32
            ? PrimitiveType.UInt32 : PrimitiveType.Int32;
        return BindExpression(node, expectedType);
    }

}
