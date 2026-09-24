// Copyright (c) Tanner Gooding and Contributors. Licensed under the MIT License.

using System.Collections.Immutable;
using Terrum.CodeAnalysis.Diagnostics;
using Terrum.CodeAnalysis.Text;

namespace Terrum.CodeAnalysis.Lexing;

public sealed record LexResult(SourceText Source, ImmutableArray<SyntaxToken> Tokens, ImmutableArray<Diagnostic> Diagnostics);
