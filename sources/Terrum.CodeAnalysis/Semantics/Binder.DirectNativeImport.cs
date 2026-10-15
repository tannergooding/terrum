// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using Terrum.CodeAnalysis.Syntax;
using Terrum.CodeAnalysis.Text;
using Terrum.CodeAnalysis.Lexing;

namespace Terrum.CodeAnalysis.Semantics;

internal sealed partial class Binder
{
    private BoundExpression BindDirectNativeParameterAddress(SyntaxNode expression,
        DirectNativeParameter parameter, TextSpan span) =>
        IsAddressOfExpression(expression)
            ? BindNativeParameterAddress(expression, parameter.Access, parameter.Extent,
                parameter.PointeeType, span)
            : BindDirectNativePointerValue(expression, parameter);

    private BoundExpression BindDirectNativePointerValue(SyntaxNode expression,
        DirectNativeParameter parameter)
    {
        while (expression.Kind == SyntaxKind.ParenthesizedExpression)
        {
            expression = (SyntaxNode)expression.Children[1];
        }
        if (expression.Kind == SyntaxKind.LiteralExpression
            && expression.Children is [SyntaxTokenElement { Token.Text: "null" }])
        {
            return new BoundConstant(0, PrimitiveType.NativeRawPointer, expression.Span);
        }

        var literalType = NativeStringZConstructorType(expression);
        if (literalType is PrimitiveType.NativeUtf16ZPointer or PrimitiveType.NativeUtf8ZPointer
            && (parameter.PointeeType == PrimitiveType.Void
                || parameter.PointeeType == PrimitiveType.Char16
                    && literalType == PrimitiveType.NativeUtf16ZPointer))
        {
            return literalType == PrimitiveType.NativeUtf16ZPointer
                ? BindNativeUtf16ZLiteral(expression, literalType)
                : BindNativeUtf8ZLiteral(expression, literalType);
        }
        return BindExpression(expression, PrimitiveType.NativeRawPointer);
    }

    private static PrimitiveType? NativeStringZConstructorType(SyntaxNode expression)
    {
        while (expression.Kind == SyntaxKind.ParenthesizedExpression)
        {
            expression = (SyntaxNode)expression.Children[1];
        }
        if (expression.Kind != SyntaxKind.InvocationExpression
            || ((SyntaxNode)expression.Children[0]).Kind != SyntaxKind.NameExpression)
        {
            return null;
        }
        var name = NameText((SyntaxNode)expression.Children[0]);
        return name switch {
            "utf16z" => PrimitiveType.NativeUtf16ZPointer,
            "utf8z" => PrimitiveType.NativeUtf8ZPointer,
            _ => null,
        };
    }

    private static bool IsAddressOfExpression(SyntaxNode expression)
    {
        while (expression.Kind == SyntaxKind.ParenthesizedExpression)
        {
            expression = (SyntaxNode)expression.Children[1];
        }
        return expression.Kind == SyntaxKind.UnaryExpression
            && ((SyntaxTokenElement)expression.Children[0]).Token.Kind == TokenKind.Ampersand;
    }

    private BoundExpression BindNativeParameterAddress(SyntaxNode expression,
        NativeParameterAccess access, int extent, PrimitiveType pointeeType, TextSpan span)
    {
        while (expression.Kind == SyntaxKind.ParenthesizedExpression)
        {
            expression = (SyntaxNode)expression.Children[1];
        }
        if (extent is not (0 or 1) || expression is not {
                Kind: SyntaxKind.UnaryExpression,
            } || ((SyntaxTokenElement)expression.Children[0]).Token.Kind != TokenKind.Ampersand)
        {
            Report("TRM3030",
                "A NativeParameter address requires the address of stable local storage.",
                span);
            return ErrorExpression(span);
        }

        var target = (SyntaxNode)expression.Children[1];
        while (target.Kind == SyntaxKind.ParenthesizedExpression)
        {
            target = (SyntaxNode)target.Children[1];
        }

        if (target.Kind == SyntaxKind.IndexExpression
            && TryFindNativeFixedArrayLocal((SyntaxNode)target.Children[0], out var arrayVariable)
            && _variables[arrayVariable].NativeFixedArrayLayout is BoundNativeFixedArrayLayout layout
            && NativeFixedArrayContract.MatchesLayout(layout, _structs, _nativeImportCatalog))
        {
            var index = BindNativeFixedArrayIndex((SyntaxNode)target.Children[2], layout);
            if ((pointeeType != PrimitiveType.Void && pointeeType != layout.ElementType)
                || index is not BoundConstant { Type: PrimitiveType.Int32, Value: 0 }
                || _variables[arrayVariable].IsInitializationLocal
                || _unresolvedVacantLocals.Contains(arrayVariable)
                || access is NativeParameterAccess.Write or NativeParameterAccess.ReadWrite
                    && (!_variables[arrayVariable].IsMutable
                        || !NativeFixedArrayContract.AllowsMutableLocal(
                            layout, _structs, _nativeImportCatalog)))
            {
                Report("TRM3030",
                    "A NativeParameter address requires a matching initialized fixed array and access mode.",
                    span);
                return ErrorExpression(span);
            }

            return new BoundNativeStorageAddress(arrayVariable, index, layout.CarrierType,
                layout.ElementType, PrimitiveType.NativeStorageAddress, layout.ElementCount,
                layout.ElementStride, layout.ByteExtent,
                IsReadonly: access == NativeParameterAccess.Read, Span: span);
        }

        if (target.Kind != SyntaxKind.NameExpression
            || !TryFindVariable(NameText(target), out var variable)
            || (pointeeType != PrimitiveType.Void && _variables[variable].Type != pointeeType)
            || (pointeeType == PrimitiveType.Void
                && !DirectNativeImportContract.IsInputPointee(_variables[variable].Type, _structs))
            || _variables[variable].IsInitializationLocal
            || _unresolvedVacantLocals.Contains(variable)
            || access is NativeParameterAccess.Write or NativeParameterAccess.ReadWrite
                && !_variables[variable].IsMutable)
        {
            Report("TRM3030",
                "A NativeParameter address requires initialized local storage with matching type and access.",
                span);
            return ErrorExpression(span);
        }

        var carrierType = _variables[variable].Type;
        var extentBytes = ValueLayout.Size(carrierType, _structs);
        return new BoundNativeStorageAddress(variable,
            new BoundConstant(0, PrimitiveType.Int32, span), carrierType, carrierType,
            PrimitiveType.NativeStorageAddress, 1, extentBytes, extentBytes,
            IsReadonly: access == NativeParameterAccess.Read, Span: span);
    }

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
