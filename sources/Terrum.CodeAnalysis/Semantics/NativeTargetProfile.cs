// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Reflection.PortableExecutable;

namespace Terrum.CodeAnalysis.Semantics;

internal static class NativeTargetProfile
{
    internal const Machine CoffMachine = Machine.Amd64;

    // This is the compilation target, not the process hosting the compiler.
    internal static int PointerSize => CoffMachine switch {
        Machine.Amd64 => 8,
        _ => throw new NotSupportedException("Native pointers require the Windows x64 target."),
    };

    internal static PrimitiveType UnsignedIndexCarrier => PointerSize switch {
        8 => PrimitiveType.UInt64,
        _ => throw new NotSupportedException("No unsigned index carrier is supported for this native target."),
    };
}
