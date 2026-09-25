// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;

namespace Terrum.CodeAnalysis.Native;

internal static class NativeUnwind
{
    internal const int BasicInfoSize = 8;
    internal const int FramePointerInfoSize = 12;
    internal const int RuntimeFunctionSize = 12;

    internal static int InfoSize(NativeFunctionRange function) =>
        function.UsesFramePointer ? FramePointerInfoSize : BasicInfoSize;

    internal static void WriteInfo(Span<byte> destination, NativeFunctionRange function)
    {
        // UWOP_ALLOC_LARGE records the sub rsp operand. Saved-RBP functions also
        // record the preceding push; RBP holds the post-allocation frame base.
        destination[0] = 1;
        destination[1] = function.UsesFramePointer ? (byte)11 : (byte)7;
        destination[2] = function.UsesFramePointer ? (byte)3 : (byte)2;
        destination[3] = 0;
        destination[4] = function.UsesFramePointer ? (byte)8 : (byte)7;
        destination[5] = 1;
        BinaryPrimitives.WriteUInt16LittleEndian(destination[6..],
            checked((ushort)((function.StackSize - (function.UsesFramePointer ? 8 : 0)) / 8)));
        if (function.UsesFramePointer)
        {
            destination[8] = 1;
            destination[9] = 0x50; // UWOP_PUSH_NONVOL RBP, preceding the allocation
            destination[10] = 0;
            destination[11] = 0;
        }
    }

    internal static void WriteFunction(Span<byte> destination, NativeFunctionRange function, int textRva, int unwindRva)
    {
        BinaryPrimitives.WriteInt32LittleEndian(destination, checked(textRva + function.Start));
        BinaryPrimitives.WriteInt32LittleEndian(destination[4..], checked(textRva + function.End));
        BinaryPrimitives.WriteInt32LittleEndian(destination[8..], unwindRva);
    }
}
