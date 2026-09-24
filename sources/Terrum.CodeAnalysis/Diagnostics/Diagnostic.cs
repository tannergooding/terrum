// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using Terrum.CodeAnalysis.Text;

namespace Terrum.CodeAnalysis.Diagnostics;

/// <summary>A source-located compiler diagnostic.</summary>
/// <param name="Code">The stable diagnostic identifier.</param>
/// <param name="Severity">The diagnostic's severity.</param>
/// <param name="Message">The human-readable explanation.</param>
/// <param name="Span">The UTF-16 source range associated with the diagnostic.</param>
public sealed record Diagnostic(string Code, DiagnosticSeverity Severity, string Message, TextSpan Span);
