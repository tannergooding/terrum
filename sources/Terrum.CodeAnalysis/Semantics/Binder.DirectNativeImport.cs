// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using Terrum.CodeAnalysis.Lexing;
using Terrum.CodeAnalysis.Syntax;
using Terrum.CodeAnalysis.Text;

namespace Terrum.CodeAnalysis.Semantics;

internal sealed partial class Binder
{
    private static bool TryDecodeNativeParameterAttribute(SyntaxNode attribute,
        out NativeParameterDefinition parameter)
    {
        parameter = new(-1, default, default, 0);
        var children = attribute.Children;
        if (children.Length != 16
            || children[0] is not SyntaxTokenElement { Token.Kind: TokenKind.OpenBracket }
            || children[1] is not SyntaxTokenElement { Token.Name: "Terrum" }
            || children[2] is not SyntaxTokenElement { Token.Kind: TokenKind.Dot }
            || children[3] is not SyntaxTokenElement { Token.Name: "Interop" }
            || children[4] is not SyntaxTokenElement { Token.Kind: TokenKind.Dot }
            || children[5] is not SyntaxTokenElement { Token.Name: "NativeParameter" }
            || children[6] is not SyntaxTokenElement { Token.Kind: TokenKind.OpenParen }
            || children[7] is not SyntaxTokenElement ordinalToken
            || ordinalToken.Token.Kind != TokenKind.NumericLiteral
            || !ScannerHost.TryParseIntegerMagnitude(ordinalToken.Token.Text.AsSpan(), out var ordinal)
            || ordinal is 0 or > int.MaxValue
            || children[8] is not SyntaxTokenElement { Token.Kind: TokenKind.Comma }
            || children[9] is not SyntaxTokenElement accessToken
            || children[9] is not SyntaxTokenElement { Token.Kind: TokenKind.Identifier }
            || children[10] is not SyntaxTokenElement { Token.Kind: TokenKind.Comma }
            || children[11] is not SyntaxTokenElement retentionToken
            || children[11] is not SyntaxTokenElement { Token.Kind: TokenKind.Identifier }
            || children[12] is not SyntaxTokenElement { Token.Kind: TokenKind.Comma }
            || children[13] is not SyntaxTokenElement extentToken
            || extentToken.Token.Kind != TokenKind.NumericLiteral
            || !ScannerHost.TryParseIntegerMagnitude(extentToken.Token.Text.AsSpan(), out var extent)
            || extent > uint.MaxValue
            || children[14] is not SyntaxTokenElement { Token.Kind: TokenKind.CloseParen }
            || children[15] is not SyntaxTokenElement { Token.Kind: TokenKind.CloseBracket })
        {
            return false;
        }

        var access = accessToken.Token.Name switch {
            "Read" => NativeParameterAccess.Read,
            "Write" => NativeParameterAccess.Write,
            "ReadWrite" => NativeParameterAccess.ReadWrite,
            _ => (NativeParameterAccess)byte.MaxValue,
        };
        var retention = retentionToken.Token.Name switch {
            "NoEscape" => NativeParameterRetention.NoEscape,
            "MayEscape" => NativeParameterRetention.MayEscape,
            _ => (NativeParameterRetention)byte.MaxValue,
        };
        if ((byte)access == byte.MaxValue || (byte)retention == byte.MaxValue)
        {
            return false;
        }

        parameter = new((int)ordinal, access, retention, (uint)extent);
        return true;
    }

    private BoundExpression BindDirectNativeParameterAddress(SyntaxNode expression,
        DirectNativeParameter parameter, PrimitiveType formalType, TextSpan span) =>
        BindDirectNativeParameterArgument(expression, parameter, formalType, span);

    private BoundExpression BindDirectNativeParameterArgument(SyntaxNode expression,
        DirectNativeParameter parameter, PrimitiveType formalType, TextSpan span)
    {
        if (parameter.Access == NativeParameterAccess.Read
            && NativePointerTypes.IsType(formalType)
            && _nativePointerTypes[NativePointerTypes.Index(formalType)].IsWritable)
        {
            _ = GetNativePointerType(parameter.PointeeType, isWritable: false, span);
        }

        return IsAddressOfExpression(expression)
            ? BindNativeParameterAddress(expression, parameter.Access, parameter.Extent,
                parameter.PointeeType, span)
            : BindDirectNativePointerValue(expression, parameter, formalType);
    }

    private BoundExpression BindDirectNativePointerValue(SyntaxNode expression,
        DirectNativeParameter parameter, PrimitiveType formalType)
    {
        while (expression.Kind == SyntaxKind.ParenthesizedExpression)
        {
            expression = (SyntaxNode)expression.Children[1];
        }
        if (expression.Kind == SyntaxKind.LiteralExpression
            && expression.Children is [SyntaxTokenElement { Token.Text: "null" }])
        {
            if (!DirectNativeImportContract.TryGetNativeParameterArgumentType(
                formalType, parameter, [.. _nativePointerTypes], out var pointerType))
            {
                Report("TRM3030",
                    "A null NativeParameter value has no matching pointer contract.",
                    expression.Span);
                return ErrorExpression(expression.Span);
            }
            return new BoundConstant(0, pointerType, expression.Span);
        }

        var literalType = NativeStringZConstructorType(expression);
        if (literalType is PrimitiveType.NativeUtf16ZPointer or PrimitiveType.NativeUtf8ZPointer
            && (parameter.PointeeType == PrimitiveType.Void
                || (parameter.PointeeType == PrimitiveType.Char16
                    && literalType == PrimitiveType.NativeUtf16ZPointer)))
        {
            return literalType == PrimitiveType.NativeUtf16ZPointer
                ? BindNativeUtf16ZLiteral(expression, literalType)
                : BindNativeUtf8ZLiteral(expression, literalType);
        }
        var value = BindExpression(expression, null);
        var matches = value.Type == PrimitiveType.NativeRawPointer
            ? parameter.PointeeType == PrimitiveType.Void
                && parameter.Access == NativeParameterAccess.Read
            : NativePointerTypeContract.IsValidType(value.Type, [.. _nativePointerTypes])
                && _nativePointerTypes[NativePointerTypes.Index(value.Type)].PointeeType
                    == parameter.PointeeType
                && (parameter.Access == NativeParameterAccess.Read
                    || _nativePointerTypes[NativePointerTypes.Index(value.Type)].IsWritable);
        if (!matches)
        {
            Report("TRM3007",
                "A NativeParameter pointer value must match the pointee and access contract.",
                expression.Span);
            return ErrorExpression(expression.Span);
        }
        return value;
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
        NativeParameterAccess access, uint extent, PrimitiveType pointeeType, TextSpan span)
    {
        while (expression.Kind == SyntaxKind.ParenthesizedExpression)
        {
            expression = (SyntaxNode)expression.Children[1];
        }
        if (expression is not {
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
            && NativeFixedArrayContract.MatchesLayout(layout, _structs))
        {
            var index = BindNativeFixedArrayIndex((SyntaxNode)target.Children[2], layout);
            if ((pointeeType != PrimitiveType.Void && pointeeType != layout.ElementType)
                || (extent > 0 && (ulong)layout.ElementCount < extent)
                || index is not BoundConstant { Type: PrimitiveType.Int32, Value: 0 }
                || _variables[arrayVariable].IsInitializationLocal
                || _unresolvedVacantLocals.Contains(arrayVariable)
                || (access is NativeParameterAccess.Write or NativeParameterAccess.ReadWrite
                    && (!_variables[arrayVariable].IsMutable
                        || !NativeFixedArrayContract.AllowsMutableLocal(layout))))
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
            || extent > 1
            || (pointeeType == PrimitiveType.Void
                && !DirectNativeImportContract.IsInputPointee(_variables[variable].Type, _structs))
            || _variables[variable].IsInitializationLocal
            || _unresolvedVacantLocals.Contains(variable)
            || (access is NativeParameterAccess.Write or NativeParameterAccess.ReadWrite
                && !_variables[variable].IsMutable))
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

    private BoundExpression BindDirectNativeOutputReservation(
        SyntaxNode expression, DirectNativeOutput output, TextSpan span) =>
        BindNativeOutputReservation(expression, output.PointeeType, span);

    private BoundExpression BindNativeOutputReservation(
        SyntaxNode expression, PrimitiveType pointeeType, TextSpan span)
    {
        if (expression.Kind != SyntaxKind.NativeOutputAddressExpression
            || expression.Children.Length != 3
            || expression.Children[2] is not SyntaxNode target)
        {
            Report("TRM3030",
                "A native output argument requires the direct '&init local' reservation form.",
                span);
            return ErrorExpression(span);
        }

        while (target.Kind == SyntaxKind.ParenthesizedExpression)
        {
            target = (SyntaxNode)target.Children[1];
        }
        if (target.Kind != SyntaxKind.NameExpression
            || !TryFindVariable(NameText(target), out var variable)
            || _variables[variable].Type != pointeeType
            || !_variables[variable].IsInitializationLocal
            || _unresolvedVacantLocals.Contains(variable))
        {
            Report("TRM3030",
                "A native output reservation requires a vacant initialization local of the exact aggregate type.",
                span);
            return ErrorExpression(span);
        }

        return new BoundNativeOutputReservation(variable, pointeeType, span);
    }
}
