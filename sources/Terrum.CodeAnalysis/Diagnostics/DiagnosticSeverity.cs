// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

namespace Terrum.CodeAnalysis.Diagnostics;

/// <summary>The significance of a diagnostic.</summary>
public enum DiagnosticSeverity
{
    /// <summary>An error that prevents the source from being considered valid.</summary>
    Error,
    /// <summary>A warning about source that may still be valid.</summary>
    Warning,
    /// <summary>An informational observation about the source.</summary>
    Info,
}
