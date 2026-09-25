// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;

namespace Terrum.CodeAnalysis.Native;

internal static class NativeUnwind
{
    internal const int InfoSize = 8;
    internal const int RuntimeFunctionSize = 12;

    internal static void WriteInfo(Span<byte> destination, int stackSize)
    {
        // UNWIND_INFO v1 for the shared seven-byte sub rsp, imm32 prolog.
        // UWOP_ALLOC_LARGE (opinfo 0) uses two slots, with allocation in units of eight.
        destination[0] = 1;
        destination[1] = 7;
        destination[2] = 2;
        destination[3] = 0;
        destination[4] = 7;
        destination[5] = 1;
        BinaryPrimitives.WriteUInt16LittleEndian(destination[6..], checked((ushort)(stackSize / 8)));
    }

    internal static void WriteFunction(Span<byte> destination, NativeFunctionRange function, int textRva, int unwindRva)
    {
        BinaryPrimitives.WriteInt32LittleEndian(destination, checked(textRva + function.Start));
        BinaryPrimitives.WriteInt32LittleEndian(destination[4..], checked(textRva + function.End));
        BinaryPrimitives.WriteInt32LittleEndian(destination[8..], unwindRva);
    }
}
