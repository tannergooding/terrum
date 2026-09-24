// Copyright (c) Tanner Gooding and Contributors. Licensed under the MIT License.

using Terrum.CodeAnalysis.Text;

namespace Terrum.CodeAnalysis.Lexing;

public sealed record SyntaxTrivia(TriviaKind Kind, TextSpan Span, string Text);
