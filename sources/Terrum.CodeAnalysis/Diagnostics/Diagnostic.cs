// Copyright (c) Tanner Gooding and Contributors. Licensed under the MIT License.

using Terrum.CodeAnalysis.Text;

namespace Terrum.CodeAnalysis.Diagnostics;

public sealed record Diagnostic(string Code, DiagnosticSeverity Severity, string Message, TextSpan Span);
