// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;

namespace Terrum.CodeAnalysis.Native;

internal static class NativeUnwind
{
    internal const int RuntimeFunctionSize = 12;
    internal const int StackProbeThreshold = 0xF00;

    internal static int PrologSize(NativeFunctionRange function)
        => function.UsesAlignedFrame
            ? 30 + (function.UsesStackProbe ? 10 : 0)
            : (function.UsesFramePointer ? 11 : 7)
                + (function.UsesStackProbe ? 10 : 0);

    internal static int StackProbeAllocationSize(NativeFunctionRange function)
        => function.UsesAlignedFrame
            ? checked(GetAllocationSize(function)
                + function.StackAlignment - 1)
            : GetAllocationSize(function);

    internal static int StackFrameAllocationSize(NativeFunctionRange function)
        => GetAllocationSize(function);

    internal static int InfoSize(NativeFunctionRange function)
    {
        var allocationSize = GetAllocationSize(function);
        var codeSlots = GetAllocationCodeSlots(allocationSize)
            + (function.UsesAlignedFrame ? 4
                : function.UsesFramePointer ? 1 : 0);
        var infoSize = Align4(4 + (codeSlots * 2));
        return checked(infoSize
            + (function.ExceptionScopes.IsEmpty ? 0
                : 8 + (function.ExceptionScopes.Length * 16)));
    }

    internal static void WriteInfo(Span<byte> destination, NativeFunctionRange function, int textRva = 0)
    {
        // UWOP_ALLOC_LARGE records the sub-rsp operand; probe calls do not change
        // the unwind state, so allocation begins only after the final subtraction.
        destination[0] = function.ExceptionScopes.IsEmpty ? (byte)1 : (byte)0x19;
        destination[1] = checked((byte)PrologSize(function));
        var allocationSize = GetAllocationSize(function);
        var allocationSlots = GetAllocationCodeSlots(allocationSize);
        destination[2] = checked((byte)(allocationSlots
            + (function.UsesAlignedFrame ? 4
                : function.UsesFramePointer ? 1 : 0)));
        destination[3] = function.UsesAlignedFrame ? (byte)0x0C : (byte)0;
        if (function.UsesAlignedFrame)
        {
            var codeOffset = 4;
            WriteAllocationCode(destination, ref codeOffset,
                allocationSize, function.UsesStackProbe ? 30 : 20);
            destination[codeOffset++] = 13;
            destination[codeOffset++] = 0x02;
            destination[codeOffset++] = 6;
            destination[codeOffset++] = 0x03;
            destination[codeOffset++] = 3;
            destination[codeOffset++] = 0xC0;
            destination[codeOffset++] = 1;
            destination[codeOffset] = 0x50;
        }
        else
        {
            destination[4] = checked((byte)((function.UsesFramePointer ? 1 : 0)
                + (function.UsesStackProbe ? 10 : 0) + 7));
            var usesWideAllocation = allocationSlots == 3;
            destination[5] = usesWideAllocation ? (byte)0x11 : (byte)0x01;
            if (usesWideAllocation)
            {
                BinaryPrimitives.WriteUInt32LittleEndian(destination[6..],
                    checked((uint)allocationSize));
            }
            else
            {
                BinaryPrimitives.WriteUInt16LittleEndian(destination[6..],
                    checked((ushort)(allocationSize / 8)));
            }
            if (function.UsesFramePointer)
            {
                var pushCode = 6 + (usesWideAllocation ? 4 : 2);
                destination[pushCode] = 1;
                destination[pushCode + 1] = 0x50; // UWOP_PUSH_NONVOL RBP
            }
        }
        if (!function.ExceptionScopes.IsEmpty)
        {
            var at = Align4(4 + (destination[2] * 2));
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

    private static int GetAllocationSize(NativeFunctionRange function)
        => checked(function.StackSize
            - (function.UsesAlignedFrame ? 24
                : function.UsesFramePointer ? 8 : 0));

    private static int GetAllocationCodeSlots(int allocationSize)
        => allocationSize / 8 > ushort.MaxValue ? 3 : 2;

    private static void WriteAllocationCode(Span<byte> destination,
        ref int codeOffset, int allocationSize, int prologOffset)
    {
        var usesWideAllocation = GetAllocationCodeSlots(allocationSize) == 3;
        destination[codeOffset++] = checked((byte)prologOffset);
        destination[codeOffset++] = usesWideAllocation ? (byte)0x11 : (byte)0x01;
        if (usesWideAllocation)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(
                destination[codeOffset..], checked((uint)allocationSize));
            codeOffset += sizeof(uint);
        }
        else
        {
            BinaryPrimitives.WriteUInt16LittleEndian(
                destination[codeOffset..],
                checked((ushort)(allocationSize / 8)));
            codeOffset += sizeof(ushort);
        }
    }

    private static int Align4(int value) => (value + 3) & ~3;

    internal static void WriteFunction(Span<byte> destination, NativeFunctionRange function, int textRva, int unwindRva)
    {
        BinaryPrimitives.WriteInt32LittleEndian(destination, checked(textRva + function.Start));
        BinaryPrimitives.WriteInt32LittleEndian(destination[4..], checked(textRva + function.End));
        BinaryPrimitives.WriteInt32LittleEndian(destination[8..], unwindRva);
    }
}
