// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

namespace Terrum.CodeAnalysis.Text;

/// <summary>A zero-based line and UTF-16 character offset within that line.</summary>
/// <param name="Line">The zero-based line number.</param>
/// <param name="Character">The zero-based UTF-16 offset from the start of the line.</param>
public readonly record struct LinePosition(int Line, int Character);
