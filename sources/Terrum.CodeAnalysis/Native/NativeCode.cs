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

    /// <summary>Code offsets for source functions in bound-program order; -1 denotes an external declaration.</summary>
    public ImmutableArray<int> SourceFunctionOffsets { get; init; } = [];

    /// <summary>Byte offset of optional per-execution managed-node state, following all host buffers.</summary>
    public int ManagedRuntimeOffset { get; init; } = -1;

    /// <summary>Byte length of compiler-supplied host buffer data, excluding managed runtime state.</summary>
    public int HostDataLength { get; init; }

    /// <summary>Code displacements targeting locations in Data.</summary>
    public ImmutableArray<NativeDataFixup> DataFixups { get; init; } = [];

    /// <summary>RIP-relative code displacements targeting named native imports; executable images only.</summary>
    internal ImmutableArray<NativeImportFixup> NativeImports { get; init; } = [];
}

/// <summary>A machine-code range with a bounded stack frame and matching Windows x64 unwind metadata.</summary>
public readonly record struct NativeFunctionRange(int Start, int End, int StackSize)
{
    /// <summary>The prolog saves RBP and addresses frame slots relative to its stable post-allocation value.</summary>
    public bool UsesFramePointer { get; init; }

    /// <summary>The prolog receives the source function's frame base in RDX, for an out-of-line cleanup.</summary>
    public bool UsesParentFrame { get; init; }

    /// <summary>Image-relative address of the compiler-owned trampoline to the Windows language handler.</summary>
    public int ExceptionHandler { get; init; }

    /// <summary>Protected instruction ranges and their native cleanup or filter/landing addresses.</summary>
    public ImmutableArray<NativeExceptionScope> ExceptionScopes { get; init; } = [];
}

/// <summary>A Windows x64 scope-table entry, with a zero target for cleanup funclets.</summary>
public readonly record struct NativeExceptionScope(int Begin, int End, int Handler, int Target);

/// <summary>A RIP-relative displacement in code targeting an offset in the data image.</summary>
public readonly record struct NativeDataFixup(int DisplacementOffset, int DataOffset);

// A negative declaration index identifies a compiler runtime import rather than a source binding.
internal readonly record struct NativeImportFixup(int DisplacementOffset, string DllName, string ExportName,
    int DeclarationIndex = -1);
