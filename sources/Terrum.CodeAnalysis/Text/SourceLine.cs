// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

namespace Terrum.CodeAnalysis.Text;

/// <summary>A source line with and without its terminating line break.</summary>
/// <param name="Span">The half-open UTF-16 range excluding the line break.</param>
/// <param name="SpanIncludingLineBreak">The half-open UTF-16 range including the line break, if any.</param>
public readonly record struct SourceLine(TextSpan Span, TextSpan SpanIncludingLineBreak);
