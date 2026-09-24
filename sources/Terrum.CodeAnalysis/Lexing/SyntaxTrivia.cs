// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using Terrum.CodeAnalysis.Text;

namespace Terrum.CodeAnalysis.Lexing;

/// <summary>A source-preserving trivia item attached to the following token.</summary>
/// <param name="Kind">The kind of whitespace or comment.</param>
/// <param name="Span">The item's half-open UTF-16 source range.</param>
/// <param name="Text">The item's exact source text.</param>
public sealed record SyntaxTrivia(TriviaKind Kind, TextSpan Span, string Text);
