// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System.Collections.Immutable;
using Terrum.CodeAnalysis.Semantics;

namespace Terrum.CodeAnalysis.Modules;

/// <summary>Explicitly assigns an output ExportKey to one checked retained generic MethodDef.</summary>
/// <param name="Program">The exact checked program retaining the root MethodDef.</param>
/// <param name="ProviderIdentity">The originating MethodDef's exact Module identity.</param>
/// <param name="SchemaIdentity">The exact schema used to check the originating artifact.</param>
/// <param name="ProviderArtifactDigest">The 32-byte SHA-256 digest of the complete originating artifact.</param>
/// <param name="MethodDefId">The full artifact-local MethodDef DefId.</param>
/// <param name="OriginExportKey">The checked origin ExportKey selecting that MethodDef.</param>
/// <param name="ExportKey">The explicitly selected output ExportKey.</param>
public sealed record TrRmRetainedGenericMethodExport(
    BoundProgram Program,
    string ProviderIdentity,
    string SchemaIdentity,
    ImmutableArray<byte> ProviderArtifactDigest,
    uint MethodDefId,
    string OriginExportKey,
    string ExportKey);
