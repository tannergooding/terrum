// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Threading;

namespace Terrum.CodeAnalysis.Lexing;

internal enum PunctuationRunKind
{
    Implemented,
    TypeBoundary,
    Unsupported,
    Invalid,
}

internal static class PunctuationRun
{
    internal const string InvalidCode = "TRM0011";
    internal const string UnsupportedCode = "TRM0012";
    internal const string InvalidMessage = "A punctuation run must be one defined spelling or an explicit grammatical boundary.";
    internal const string UnsupportedMessage = "This defined punctuation spelling is not supported by the current source profile.";

    internal static readonly (TokenKind Kind, string Text)[] Spellings =
    [
        (TokenKind.OpenParen, "("), (TokenKind.CloseParen, ")"),
        (TokenKind.OpenBrace, "{"), (TokenKind.CloseBrace, "}"),
        (TokenKind.OpenBracket, "["), (TokenKind.CloseBracket, "]"),
        (TokenKind.Semicolon, ";"), (TokenKind.Comma, ","), (TokenKind.Dot, "."),
        (TokenKind.DotDot, ".."), (TokenKind.DotDotCaret, "..^"),
        (TokenKind.Colon, ":"), (TokenKind.Question, "?"),
        (TokenKind.QuestionDot, "?."),
        (TokenKind.QuestionQuestion, "??"), (TokenKind.QuestionQuestionEquals, "??="),
        (TokenKind.Plus, "+"), (TokenKind.PlusPlus, "++"),
        (TokenKind.Minus, "-"), (TokenKind.MinusMinus, "--"), (TokenKind.Star, "*"), (TokenKind.Slash, "/"),
        (TokenKind.Percent, "%"), (TokenKind.Bang, "!"), (TokenKind.Tilde, "~"),
        (TokenKind.Equals, "="), (TokenKind.EqualsEquals, "=="),
        (TokenKind.Arrow, "=>"), (TokenKind.BangEquals, "!="),
        (TokenKind.Less, "<"), (TokenKind.LessEquals, "<="),
        (TokenKind.Greater, ">"), (TokenKind.GreaterEquals, ">="),
        (TokenKind.Ampersand, "&"), (TokenKind.AmpersandAmpersand, "&&"),
        (TokenKind.Pipe, "|"), (TokenKind.PipePipe, "||"), (TokenKind.Caret, "^"),
        (TokenKind.ShiftLeft, "<<"), (TokenKind.ShiftRight, ">>"),
        (TokenKind.ShiftRightUnsigned, ">>>"),
        (TokenKind.PlusEquals, "+="), (TokenKind.MinusEquals, "-="),
        (TokenKind.StarEquals, "*="), (TokenKind.SlashEquals, "/="),
        (TokenKind.PercentEquals, "%="), (TokenKind.AmpersandEquals, "&="),
        (TokenKind.PipeEquals, "|="), (TokenKind.CaretEquals, "^="),
        (TokenKind.ShiftLeftEquals, "<<="), (TokenKind.ShiftRightEquals, ">>="),
        (TokenKind.ShiftRightUnsignedEquals, ">>>="),
    ];

    internal static bool IsGlyph(char unit) => unit is '!' or '%' or '&' or '*' or '+' or '-'
        or '.' or '/' or ':' or '<' or '=' or '>' or '?' or '^' or '|' or '~';

    internal static int End(ReadOnlySpan<char> text, int start, CancellationToken cancellationToken = default)
    {
        var at = start;
        while (at < text.Length && IsGlyph(text[at]))
        {
            if ((at & 63) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }
            if (text[at] == '/' && at + 1 < text.Length && text[at + 1] is '/' or '*')
            {
                break;
            }
            at++;
        }
        return at;
    }

    internal static PunctuationRunKind Classify(ReadOnlySpan<char> text, out TokenKind tokenKind)
    {
        foreach (var (kind, spelling) in Spellings)
        {
            if (text.SequenceEqual(spelling))
            {
                tokenKind = kind;
                return PunctuationRunKind.Implemented;
            }
        }
        tokenKind = TokenKind.Bad;
        if (text.Length > 1)
        {
            foreach (var unit in text)
            {
                if (unit is not ('>' or '?'))
                {
                    return PunctuationRunKind.Invalid;
                }
            }
            // This is deferred, not valid: only actual type-parser consumption
            // can certify these closers/suffixes (or diagnose repeated marking).
            return PunctuationRunKind.TypeBoundary;
        }
        return PunctuationRunKind.Invalid;
    }

    internal static TokenKind TypeBoundaryToken(ReadOnlySpan<char> text, out int width)
    {
        if (text[0] == '>')
        {
            width = text.StartsWith(">>>") ? 3 : text.StartsWith(">>") ? 2 : 1;
            return width == 3 ? TokenKind.ShiftRightUnsigned : width == 2 ? TokenKind.ShiftRight : TokenKind.Greater;
        }
        width = text.StartsWith("??") ? 2 : 1;
        return width == 2 ? TokenKind.QuestionQuestion : TokenKind.Question;
    }
}
