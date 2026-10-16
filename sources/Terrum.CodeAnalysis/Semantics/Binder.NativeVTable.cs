using System;
using Terrum.CodeAnalysis.Lexing;
using Terrum.CodeAnalysis.Syntax;

namespace Terrum.CodeAnalysis.Semantics;

internal sealed partial class Binder
{
    private static bool TryDecodeNativeVTableMethodAttribute(SyntaxNode attribute, out int slot)
    {
        slot = -1;
        return attribute.Children is [
            SyntaxTokenElement { Token.Kind: TokenKind.OpenBracket },
            SyntaxTokenElement { Token.Name: "Terrum" },
            SyntaxTokenElement { Token.Kind: TokenKind.Dot },
            SyntaxTokenElement { Token.Name: "Interop" },
            SyntaxTokenElement { Token.Kind: TokenKind.Dot },
            SyntaxTokenElement { Token.Name: "NativeVTableMethod" },
            SyntaxTokenElement { Token.Kind: TokenKind.OpenParen },
            SyntaxTokenElement slotToken,
            SyntaxTokenElement { Token.Kind: TokenKind.CloseParen },
            SyntaxTokenElement { Token.Kind: TokenKind.CloseBracket }]
            && slotToken.Token.Kind == TokenKind.NumericLiteral
            && ScannerHost.TryParseIntegerMagnitude(slotToken.Token.Text.AsSpan(), out var slotValue)
            && slotValue <= (ulong)(int.MaxValue / NativeTargetProfile.PointerSize)
            && (slot = (int)slotValue) >= 0;
    }
}
