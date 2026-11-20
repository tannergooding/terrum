// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

namespace Terrum.CodeAnalysis.Modules;

/// <summary>An exact ExportKey selection qualified by its declaring Module.</summary>
/// <param name="ModuleIdentity">The exact identity of the declaring Module.</param>
/// <param name="ExportKey">The exact ExportKey published by that Module.</param>
public sealed record TrRmStandardExportKeySelection(
    string ModuleIdentity, string ExportKey);
