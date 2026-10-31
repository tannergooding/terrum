// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System.Collections.Immutable;
using System.Linq;
using Terrum.CodeAnalysis.Lexing;
using Terrum.CodeAnalysis.Syntax;
using Terrum.CodeAnalysis.Text;

namespace Terrum.CodeAnalysis.Semantics;

internal sealed partial class Binder
{
    private bool TryBindNativeFixedArrayType(SyntaxNode node, out PrimitiveType arrayType,
        out PrimitiveType elementType, out ulong elementCount)
    {
        arrayType = PrimitiveType.Int32;
        elementType = PrimitiveType.Int32;
        elementCount = 0;
        if (_isBindingConstEvalBody)
        {
            Report("TRM3024",
                "ConstEval bodies support only scalar local values.",
                node.Span);
            return false;
        }
        if (!NativeFixedArrayContract.SupportsRuntimePolicy(_runtimePolicy))
        {
            Report("TRM3030", "Fixed-array storage is not supported under the selected runtime policy.",
                node.Span);
            return false;
        }
        if (node.Kind != SyntaxKind.FixedArrayTypeName
            || node.Children is not [
                SyntaxTokenElement { Token.Kind: TokenKind.OpenBracket },
                SyntaxNode elementNode,
                SyntaxTokenElement { Token.Kind: TokenKind.Semicolon },
                SyntaxNode extentNode,
                SyntaxTokenElement { Token.Kind: TokenKind.CloseBracket },
            ])
        {
            Report("TRM3030", "A fixed-array type requires an element type and closed extent.",
                node.Span);
            return false;
        }

        var diagnosticCount = DiagnosticCount;
        var resolvedElement = BindType(elementNode, allowVoid: true);
        var extentExpression = BindExpression(extentNode, PrimitiveType.UInt64);
        var boundExtent = FoldFixedArrayExtentExpression(extentExpression);
        if (boundExtent is not BoundConstant
            && extentExpression.Type == PrimitiveType.UInt64
            && CheckConstExpression(extentExpression, out _)
            && _boundConstEvalFunctions is { } constEvalFunctions)
        {
            try
            {
                boundExtent = EvaluateConstExpression(extentExpression,
                    ConstSlots(_constScalarValues, _variables.Count),
                    constEvalFunctions, 0,
                    new ConstEvalState(_cancellationToken));
            }
            catch (ConstEvalFailure failure)
            {
                Report("TRM3030",
                    $"A fixed-array extent failed required constant evaluation: {failure.Message}",
                    extentNode.Span);
            }
        }
        if (DiagnosticCount != diagnosticCount
            || boundExtent is not BoundConstant { Type: PrimitiveType.UInt64 } constant
            || constant.Bits.High != 0
            || !NativeFixedArrayContract.TryCreateType(resolvedElement,
                constant.Bits.Low, _structs, _nativeFixedArrayTypes,
                out var resolvedArrayType, out _))
        {
            Report("TRM3030", "A fixed-array extent must be an exact closed uint64 value with a valid layout.",
                node.Span);
            return false;
        }

        arrayType = resolvedArrayType;
        elementType = resolvedElement;
        elementCount = constant.Bits.Low;
        return true;
    }

    private BoundExpression FoldFixedArrayExtentExpression(
        BoundExpression expression)
    {
        if (expression is BoundRequiredConstant required)
        {
            return FoldFixedArrayExtentExpression(required.Operand);
        }

        var rewritten = expression.RewriteNestedExpressions(
            FoldFixedArrayExtentExpression);
        if (rewritten is BoundRead read
            && _constScalarValues.TryGetValue(read.Variable,
                out var constant))
        {
            return constant with { Span = read.Span };
        }

        return FoldScalarConstant(rewritten);
    }

    private BoundExpression BindNativeFixedArrayInitializer(SyntaxNode node, PrimitiveType arrayType,
        PrimitiveType elementType, ulong elementCount)
    {
        if (!NativeFixedArrayContract.TryGetLayout(arrayType, elementType,
                elementCount, _structs, _nativeFixedArrayTypes,
                out var layout))
        {
            Report("TRM3030", "The fixed-array carrier has no valid layout.", node.Span);
            return ErrorExpression(node.Span);
        }
        var count = layout.ElementCount;
        if (node.Kind != SyntaxKind.ArrayInitializerExpression
            || node.Children[0] is not SyntaxTokenElement { Token.Kind: TokenKind.OpenBracket }
            || node.Children[^1] is not SyntaxTokenElement { Token.Kind: TokenKind.CloseBracket })
        {
            Report("TRM3030", "A fixed array requires an array initializer.", node.Span);
            return ErrorExpression(node.Span);
        }

        var elements = ImmutableArray.CreateBuilder<SyntaxNode>();
        for (var child = 1; child < node.Children.Length - 1; child++)
        {
            if ((child & 1) != 0)
            {
                if (node.Children[child] is not SyntaxNode element
                    || element.Kind == SyntaxKind.MissingExpression)
                {
                    Report("TRM3030", "Fixed-array elements must be complete and source ordered.",
                        node.Span);
                    return ErrorExpression(node.Span);
                }
                elements.Add(element);
            }
            else
            {
                if (node.Children[child] is not SyntaxTokenElement {
                        Token.Kind: TokenKind.Comma
                    })
                {
                    Report("TRM3030", "Fixed-array elements must be comma separated.",
                        node.Span);
                    return ErrorExpression(node.Span);
                }
            }
        }

        if ((ulong)elements.Count != count
            || (elements.Count == 0
                && !NativeFixedArrayContract.AllowsEmptyInitialization(layout)))
        {
            Report("TRM3030", "A fixed array requires exactly one initializer per element; only zero-length arrays accept an empty initializer.",
                node.Span);
            return ErrorExpression(node.Span);
        }

        var bound = ImmutableArray.CreateBuilder<BoundExpression>(elements.Count);
        foreach (var element in elements)
        {
            if (NativeFixedArrayTypes.IsType(elementType))
            {
                bound.Add(BindNativeFixedArrayValue(element, elementType));
                continue;
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

    private BoundExpression BindNativeFixedArrayValue(SyntaxNode node,
        PrimitiveType arrayType)
    {
        if (!NativeFixedArrayTypes.IsType(arrayType))
        {
            Report("TRM3030", "The nested fixed-array element type is invalid.",
                node.Span);
            return ErrorExpression(node.Span);
        }

        var index = NativeFixedArrayTypes.Index(arrayType);
        if ((uint)index >= (uint)_nativeFixedArrayTypes.Count)
        {
            Report("TRM3030", "The nested fixed-array element type is invalid.",
                node.Span);
            return ErrorExpression(node.Span);
        }

        var definition = _nativeFixedArrayTypes[index];
        if (node.Kind == SyntaxKind.ArrayInitializerExpression)
        {
            return BindNativeFixedArrayInitializer(node, arrayType,
                definition.ElementType, definition.ElementCount);
        }
        if (KnownType(node) != arrayType)
        {
            Report("TRM3030", "A nested fixed-array value must have its exact structural type.",
                node.Span);
            return ErrorExpression(node.Span);
        }

        return BindExpression(node, arrayType);
    }

    private BoundExpression BindNativeFixedArrayPointerAddress(SyntaxNode node,
        PrimitiveType pointerType, TextSpan span)
    {
        if (node.Kind != SyntaxKind.IndexExpression
            || !TryBindNativeFixedArrayAccessPath(node, out var variable,
                out var projections))
        {
            Report("TRM3030", "A native pointer address requires an admitted fixed-array element.", span);
            return ErrorExpression(span);
        }

        var leaf = projections[^1];
        var layout = leaf.Layout;
        var pointer = _nativePointerTypes[NativePointerTypes.Index(pointerType)];
        if (layout.ByteExtent == 0)
        {
            Report("TRM3030",
                "A zero-extent fixed array does not provide borrowable element storage.",
                span);
            return ErrorExpression(span);
        }
        if (pointer.PointeeType != layout.ElementType
            || (pointer.IsWritable && (!_variables[variable].IsMutable
                || !NativeFixedArrayContract.AllowsMutableLocal(layout))))
        {
            Report("TRM3030", "The fixed-array element does not match the native pointer access mode.", span);
            return ErrorExpression(span);
        }

        if (_unsafeDepth == 0)
        {
            Report("TRM3028", "Taking a native pointer address requires an explicit unsafe context.", span);
        }

        return new BoundNativeFixedArrayPointerAddress(variable, layout,
            leaf.Index, pointerType, span) {
            IsUnsafeAuthorized = _unsafeDepth != 0,
            Projections = projections.Length > 1 ? projections : [],
        };
    }

    private BoundExpression BindNativeFixedArrayIndex(SyntaxNode node, BoundNativeFixedArrayLayout layout)
    {
        var knownType = KnownType(node);
        var expectedType = knownType is PrimitiveType type
            && NativeFixedArrayContract.IsIndexType(type)
                ? type : PrimitiveType.Int32;
        return BindExpression(node, expectedType);
    }
}
