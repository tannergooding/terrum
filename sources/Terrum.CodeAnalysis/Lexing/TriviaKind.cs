// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

namespace Terrum.CodeAnalysis.Lexing;

/// <summary>The kind of source text retained between lexical tokens.</summary>
public enum TriviaKind
{
    /// <summary>Whitespace other than a line break.</summary>
    Whitespace,
    /// <summary>A line break.</summary>
    NewLine,
    /// <summary>A comment ending at a line break or end of file.</summary>
    SingleLineComment,
    /// <summary>A delimited block comment.</summary>
    MultiLineComment,
}
