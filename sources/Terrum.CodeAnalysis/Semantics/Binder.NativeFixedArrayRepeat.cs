using Terrum.CodeAnalysis.Lexing;
using Terrum.CodeAnalysis.Syntax;
using Terrum.CodeAnalysis.Text;

namespace Terrum.CodeAnalysis.Semantics;

internal sealed partial class Binder
{
    private BoundExpression BindNativeFixedArrayValueOrInitializer(
        SyntaxNode node, PrimitiveType arrayType)
    {
        if (!NativeFixedArrayTypes.IsType(arrayType))
        {
            return BindNativeFixedArrayValue(node, arrayType);
        }

        var index = NativeFixedArrayTypes.Index(arrayType);
        if ((uint)index >= (uint)_nativeFixedArrayTypes.Count)
        {
            Report("TRM3030", "The fixed-array value type is invalid.",
                node.Span);
            return ErrorExpression(node.Span);
        }

        var definition = _nativeFixedArrayTypes[index];
        return node.Kind switch {
            SyntaxKind.ArrayInitializerExpression =>
                BindNativeFixedArrayInitializer(node, arrayType,
                    definition.ElementType, definition.ElementCount),
            SyntaxKind.FixedArrayRepeatExpression =>
                BindNativeFixedArrayRepeatInitializer(node, arrayType,
                    definition.ElementType, definition.ElementCount),
            _ => BindNativeFixedArrayValue(node, arrayType),
        };
    }

    private BoundExpression BindNativeFixedArrayRepeatInitializer(
        SyntaxNode node, PrimitiveType arrayType, PrimitiveType elementType,
        ulong elementCount)
    {
        if (_isBindingConstEvalBody)
        {
            Report("TRM3024",
                "ConstEval bodies support only scalar local values.",
                node.Span);
            return ErrorExpression(node.Span);
        }
        if (!NativeFixedArrayContract.SupportsRuntimePolicy(_runtimePolicy))
        {
            Report("TRM3030",
                "Fixed-array storage is not supported under the selected runtime policy.",
                node.Span);
            return ErrorExpression(node.Span);
        }
        if (!NativeFixedArrayContract.TryGetLayout(arrayType, elementType,
                elementCount, _structs, _nativeFixedArrayTypes,
                out var layout)
            || !NativeFixedArrayContract.IsRootlessCopyFieldType(
                elementType, _structs, _nativeFixedArrayTypes))
        {
            Report("TRM3030",
                "The fixed-array repeat element requires a valid rootless Copy layout.",
                node.Span);
            return ErrorExpression(node.Span);
        }
        if (node.Kind != SyntaxKind.FixedArrayRepeatExpression
            || node.Children is not [
                SyntaxTokenElement { Token.Kind: TokenKind.OpenBracket },
                SyntaxNode seedNode,
                SyntaxTokenElement { Token.Kind: TokenKind.Semicolon },
                SyntaxNode extentNode,
                SyntaxTokenElement { Token.Kind: TokenKind.CloseBracket },
            ]
            || seedNode.Kind == SyntaxKind.MissingExpression
            || extentNode.Kind == SyntaxKind.MissingExpression)
        {
            Report("TRM3030",
                "A fixed-array repeat requires one seed and an exact count.",
                node.Span);
            return ErrorExpression(node.Span);
        }

        var diagnosticsBeforeCount = DiagnosticCount;
        var extentExpression = BindExpression(extentNode,
            PrimitiveType.UInt64);
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
                    $"A fixed-array repeat count failed required constant evaluation: {failure.Message}",
                    extentNode.Span);
            }
        }
        if (DiagnosticCount != diagnosticsBeforeCount
            || boundExtent is not BoundConstant {
                Type: PrimitiveType.UInt64,
            } constant
            || constant.Bits.High != 0
            || constant.Bits.Low != layout.ElementCount)
        {
            Report("TRM3030",
                "A fixed-array repeat count must be the exact closed uint64 extent of its target.",
                extentNode.Span);
            return ErrorExpression(node.Span);
        }

        var seed = NativeFixedArrayTypes.IsType(elementType)
            ? BindNativeFixedArrayValueOrInitializer(seedNode, elementType)
            : BindExpression(seedNode, elementType);
        if (seed.Type != elementType)
        {
            Report("TRM3030",
                "A fixed-array repeat seed must have its exact element type.",
                seedNode.Span);
            return ErrorExpression(node.Span);
        }

        return new BoundNativeFixedArrayRepeatInitialization(layout, seed,
            node.Span);
    }
}
