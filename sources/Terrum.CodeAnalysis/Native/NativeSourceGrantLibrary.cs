// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using Terrum.CodeAnalysis.Semantics;

namespace Terrum.CodeAnalysis.Native;

/// <summary>Managed signature metadata for one explicitly selected Windows x64 DLL export.</summary>
public sealed record NativeLibraryExportDescriptor(string Name, bool SourceDependent, PrimitiveType ReturnType,
    ImmutableArray<PrimitiveType> ParameterTypes);

/// <summary>A provisional native library image and descriptors for its exact selected export set.</summary>
/// <remarks>
/// This result supports the unapproved source-grant library experiment. It does
/// not define a stable or public native ABI.
/// </remarks>
[SuppressMessage("Performance", "CA1819:Properties should not return arrays",
    Justification = "The caller owns the emitted image and writes it as a complete artifact.")]
public sealed record NativeSourceGrantLibrary(byte[] Image, ImmutableArray<NativeLibraryExportDescriptor> Exports);
