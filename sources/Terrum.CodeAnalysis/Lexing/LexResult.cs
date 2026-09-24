// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System.Collections.Immutable;
using Terrum.CodeAnalysis.Diagnostics;
using Terrum.CodeAnalysis.Text;

namespace Terrum.CodeAnalysis.Lexing;

/// <summary>The tokens and diagnostics produced from one source snapshot.</summary>
/// <param name="Source">The source that was lexed.</param>
/// <param name="Tokens">The lexical stream, including its end-of-file token.</param>
/// <param name="Diagnostics">Diagnostics reported while lexing the source.</param>
public sealed record LexResult(SourceText Source, ImmutableArray<SyntaxToken> Tokens, ImmutableArray<Diagnostic> Diagnostics);
