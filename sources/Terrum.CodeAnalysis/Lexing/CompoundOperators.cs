// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;

namespace Terrum.CodeAnalysis.Lexing;

internal static class CompoundOperators
{
    internal static bool IsShift(TokenKind kind) => kind is TokenKind.ShiftLeftEquals
        or TokenKind.ShiftRightEquals or TokenKind.ShiftRightUnsignedEquals;

    internal static TokenKind Binary(TokenKind kind) => kind switch {
        TokenKind.PlusEquals => TokenKind.Plus,
        TokenKind.MinusEquals => TokenKind.Minus,
        TokenKind.StarEquals => TokenKind.Star,
        TokenKind.SlashEquals => TokenKind.Slash,
        TokenKind.PercentEquals => TokenKind.Percent,
        TokenKind.AmpersandEquals => TokenKind.Ampersand,
        TokenKind.PipeEquals => TokenKind.Pipe,
        TokenKind.CaretEquals => TokenKind.Caret,
        TokenKind.ShiftLeftEquals => TokenKind.ShiftLeft,
        TokenKind.ShiftRightEquals => TokenKind.ShiftRight,
        TokenKind.ShiftRightUnsignedEquals => TokenKind.ShiftRightUnsigned,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    internal static bool IsSupported(TokenKind kind) => kind is TokenKind.PlusEquals
        or TokenKind.MinusEquals or TokenKind.StarEquals or TokenKind.SlashEquals
        or TokenKind.PercentEquals or TokenKind.AmpersandEquals or TokenKind.PipeEquals
        or TokenKind.CaretEquals or TokenKind.ShiftLeftEquals or TokenKind.ShiftRightEquals
        or TokenKind.ShiftRightUnsignedEquals;
}
