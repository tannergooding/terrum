// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

namespace Terrum.CodeAnalysis.Native;

/// <summary>Verified Windows x64 machine code with a callable int32(void) entry point.</summary>
/// <param name="Code">Position-independent text bytes.</param>
/// <param name="EntryPoint">Callable entry offset relative to Code.</param>
/// <param name="Functions">Ranges requiring Windows x64 unwind metadata.</param>
/// <param name="ExitProcessFixups">RIP-relative ExitProcess IAT displacement offsets, relative to Code.</param>
[SuppressMessage("Performance", "CA1819:Properties should not return arrays", Justification = "Owned executable allocation needs byte[]; callers must clone before mutation.")]
public sealed record NativeCode(byte[] Code, int EntryPoint, ImmutableArray<NativeFunctionRange> Functions,
    ImmutableArray<int> ExitProcessFixups)
{
    /// <summary>The process entry wrapper, distinct from the callable entry point.</summary>
    public int AotEntryPoint { get; init; }

    /// <summary>Compiler-owned read-only and writable data, in that order.</summary>
    [SuppressMessage("Performance", "CA1819:Properties should not return arrays", Justification = "The native emitter and JIT share an owned image.")]
    public byte[] Data { get; init; } = [];

    /// <summary>Length of the read-only prefix in Data.</summary>
    public int ReadOnlyDataLength { get; init; }

    /// <summary>Byte offsets into Data for all buffers, indexed by bound-program order.</summary>
    public ImmutableArray<int> BufferOffsets { get; init; } = [];

    /// <summary>Code displacements targeting locations in Data.</summary>
    public ImmutableArray<NativeDataFixup> DataFixups { get; init; } = [];

    /// <summary>RIP-relative call displacements targeting named native imports; executable images only.</summary>
    internal ImmutableArray<NativeImportFixup> NativeImports { get; init; } = [];
}

/// <summary>A machine-code range with an rsp allocation in its prolog.</summary>
public readonly record struct NativeFunctionRange(int Start, int End, int StackSize);

/// <summary>A RIP-relative displacement in code targeting an offset in the data image.</summary>
public readonly record struct NativeDataFixup(int DisplacementOffset, int DataOffset);

internal readonly record struct NativeImportFixup(int DisplacementOffset, string DllName, string ExportName);
