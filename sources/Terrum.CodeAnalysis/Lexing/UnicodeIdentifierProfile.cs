// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;

namespace Terrum.CodeAnalysis.Lexing;

/// <summary>The pinned identifier data shared by host and compiled Terrum scanner components.</summary>
public static class UnicodeIdentifierProfile
{
    /// <summary>The Unicode version used to generate the derived-property ranges.</summary>
    public const string Version = "17.0.0";

    /// <summary>The SHA-256 of the authoritative DerivedCoreProperties.txt input.</summary>
    public const string SourceSha256 = "24c7fed1195c482faaefd5c1e7eb821c5ee1fb6de07ecdbaa64b56a99da22c08";

    /// <summary>Gets sorted inclusive start/end pairs for XID_Start, without Terrum's additional underscore.</summary>
    public static ReadOnlySpan<int> StartRanges => UnicodeXid.StartRanges;

    /// <summary>Gets sorted inclusive start/end pairs for XID_Continue, without Terrum's additional underscore.</summary>
    public static ReadOnlySpan<int> ContinueRanges => UnicodeXid.ContinueRanges;
}
