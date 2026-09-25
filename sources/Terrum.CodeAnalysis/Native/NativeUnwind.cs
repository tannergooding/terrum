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
        checked((function.UsesFramePointer ? FramePointerInfoSize : BasicInfoSize)
            + (function.ExceptionScopes.IsEmpty ? 0 : 8 + (function.ExceptionScopes.Length * 16)));

    internal static void WriteInfo(Span<byte> destination, NativeFunctionRange function, int textRva = 0)
    {
        // UWOP_ALLOC_LARGE records the sub rsp operand. Saved-RBP functions also
        // record the preceding push; RBP holds the post-allocation frame base.
        destination[0] = function.ExceptionScopes.IsEmpty ? (byte)1 : (byte)0x19;
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
        if (!function.ExceptionScopes.IsEmpty)
        {
            var at = function.UsesFramePointer ? FramePointerInfoSize : BasicInfoSize;
            BinaryPrimitives.WriteInt32LittleEndian(destination[at..], checked(textRva + function.ExceptionHandler));
            BinaryPrimitives.WriteInt32LittleEndian(destination[(at + 4)..], function.ExceptionScopes.Length);
            at += 8;
            foreach (var scope in function.ExceptionScopes)
            {
                BinaryPrimitives.WriteInt32LittleEndian(destination[at..], checked(textRva + scope.Begin));
                BinaryPrimitives.WriteInt32LittleEndian(destination[(at + 4)..], checked(textRva + scope.End));
                BinaryPrimitives.WriteInt32LittleEndian(destination[(at + 8)..], checked(textRva + scope.Handler));
                BinaryPrimitives.WriteInt32LittleEndian(destination[(at + 12)..],
                    scope.Target == 0 ? 0 : checked(textRva + scope.Target));
                at += 16;
            }
        }
    }

    internal static void WriteFunction(Span<byte> destination, NativeFunctionRange function, int textRva, int unwindRva)
    {
        BinaryPrimitives.WriteInt32LittleEndian(destination, checked(textRva + function.Start));
        BinaryPrimitives.WriteInt32LittleEndian(destination[4..], checked(textRva + function.End));
        BinaryPrimitives.WriteInt32LittleEndian(destination[8..], unwindRva);
    }
}
