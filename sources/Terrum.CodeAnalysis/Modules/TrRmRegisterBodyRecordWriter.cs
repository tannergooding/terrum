// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;

namespace Terrum.CodeAnalysis.Modules;

internal readonly record struct TrRmRegisterBodyCallInput(
    byte SelectorKind, byte SourceUse, ushort SelectorIndex);

internal static class TrRmRegisterBodyRecordWriter
{
    private const int RecordHeaderSize = 4;

    internal static byte[] EncodeNativeAddress(byte resultKind, ushort resultIndex,
        ushort methodMapIndex, ushort pointerTypeMapIndex)
    {
        if (resultKind is not (1 or 2))
        {
            throw new ArgumentOutOfRangeException(nameof(resultKind));
        }
        if (resultKind == 1 && (resultIndex > 15 || resultIndex == 14))
        {
            throw new ArgumentOutOfRangeException(nameof(resultIndex));
        }

        var result = new byte[12];
        result[0] = 0x0D;
        result[RecordHeaderSize] = resultKind;
        Write(result, RecordHeaderSize + 2, resultIndex);
        Write(result, RecordHeaderSize + 4, methodMapIndex);
        Write(result, RecordHeaderSize + 6, pointerTypeMapIndex);
        return result;
    }

    internal static byte[] EncodePlaceSelection(byte targetKind,
        byte accessKind, byte destinationKind, byte baseKind,
        ushort destinationIndex, ushort baseIndex, byte indexKind,
        byte targetRefKind, ushort indexIndex, uint targetRef,
        ushort referentTypeMapIndex, TrRmArtifactSchema schema)
    {
        if (targetKind is < 1 or > 4 || accessKind is < 1 or > 5
            || destinationKind > 2 || baseKind > 2 || indexKind > 2
            || targetRefKind > 3
            || (targetRefKind == 3
                && (!schema.UsesForeignInvocationStorageFixedArrays()
                    || targetKind != 3 || targetRef is 0 or > 0xFF_FFFF))
            || (targetKind == 1
                ? targetRefKind != 0
                : targetKind is 2 or 4
                    ? targetRefKind != 1
                    : targetRefKind is not (2 or 3)))
        {
            throw new ArgumentOutOfRangeException(nameof(targetKind));
        }

        var result = new byte[24];
        result[0] = 0x04;
        result[RecordHeaderSize] = targetKind;
        result[RecordHeaderSize + 1] = accessKind;
        result[RecordHeaderSize + 2] = destinationKind;
        result[RecordHeaderSize + 3] = baseKind;
        Write(result, RecordHeaderSize + 4, destinationIndex);
        Write(result, RecordHeaderSize + 6, baseIndex);
        result[RecordHeaderSize + 8] = indexKind;
        result[RecordHeaderSize + 9] = targetRefKind;
        Write(result, RecordHeaderSize + 10, indexIndex);
        Write(result, RecordHeaderSize + 12, targetRef);
        Write(result, RecordHeaderSize + 16, referentTypeMapIndex);
        return result;
    }

    internal static byte[] EncodeStorageAddress(byte sourceKind,
        byte resultKind, ushort sourceIndex, ushort resultIndex,
        ushort pointerTypeMapIndex, uint requiredElementCount)
    {
        if (sourceKind is not (1 or 2)
            || (sourceKind == 1 && sourceIndex > 13)
            || resultKind is not (1 or 2)
            || (resultKind == 1
                && (resultIndex > 15 || resultIndex == 14))
            || requiredElementCount == 0
            || (sourceKind == 2 && resultKind == 2
                && sourceIndex == resultIndex))
        {
            throw new ArgumentOutOfRangeException(nameof(sourceKind));
        }

        var result = new byte[16];
        result[0] = 0x0E;
        result[RecordHeaderSize] = sourceKind;
        result[RecordHeaderSize + 1] = resultKind;
        Write(result, RecordHeaderSize + 2, sourceIndex);
        Write(result, RecordHeaderSize + 4, resultIndex);
        Write(result, RecordHeaderSize + 6, pointerTypeMapIndex);
        Write(result, RecordHeaderSize + 8, requiredElementCount);
        return result;
    }

    internal static byte[] EncodeRootPinReferenceSupport(byte sourceKind,
        byte resultCarrier, ushort sourceIndex, byte resultRegister,
        byte sourceMode, ushort sourceTypeMapIndex, ushort resultTypeMapIndex)
    {
        if (sourceKind is not (1 or 2)
            || (sourceKind == 1 && sourceIndex > 13))
        {
            throw new ArgumentOutOfRangeException(nameof(sourceKind));
        }
        if (resultCarrier > 2
            || (resultCarrier == 0
                ? resultRegister != 15 || resultTypeMapIndex != 0
                : resultRegister > 13))
        {
            throw new ArgumentOutOfRangeException(nameof(resultCarrier));
        }
        if (sourceMode is < 1 or > 3)
        {
            throw new ArgumentOutOfRangeException(nameof(sourceMode));
        }

        var result = new byte[16];
        result[0] = 0x0D;
        result[RecordHeaderSize] = sourceKind;
        result[RecordHeaderSize + 1] = resultCarrier;
        Write(result, RecordHeaderSize + 2, sourceIndex);
        result[RecordHeaderSize + 4] = resultRegister;
        result[RecordHeaderSize + 5] = sourceMode;
        Write(result, RecordHeaderSize + 6, sourceTypeMapIndex);
        Write(result, RecordHeaderSize + 8, resultTypeMapIndex);
        return result;
    }

    internal static byte[] EncodeCallSite(byte targetKind, byte resultKind,
        ushort targetIndex, ushort resultIndex,
        IReadOnlyList<TrRmRegisterBodyCallInput> inputs,
        IReadOnlyList<ushort> preparations)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(preparations);
        if (targetKind is < 1 or > 3)
        {
            throw new ArgumentOutOfRangeException(nameof(targetKind));
        }
        if (targetKind == 2 && targetIndex > 13)
        {
            throw new ArgumentOutOfRangeException(nameof(targetIndex));
        }
        if (resultKind > 2)
        {
            throw new ArgumentOutOfRangeException(nameof(resultKind));
        }
        if ((resultKind == 0 && resultIndex != 0)
            || (resultKind == 1 && (resultIndex > 15 || resultIndex == 14)))
        {
            throw new ArgumentOutOfRangeException(nameof(resultIndex));
        }
        if (inputs.Count > 65_536)
        {
            throw new ArgumentOutOfRangeException(nameof(inputs));
        }
        if (preparations.Count > 65_536)
        {
            throw new ArgumentOutOfRangeException(nameof(preparations));
        }

        foreach (var input in inputs)
        {
            if (input.SelectorKind > 2
                || (input.SelectorKind == 0
                    ? input.SourceUse != 0 || input.SelectorIndex != 0
                    : input.SourceUse is < 1 or > 5)
                || (input.SelectorKind == 1 && input.SelectorIndex > 15))
            {
                throw new ArgumentException("Invalid CallSite input selector.", nameof(inputs));
            }
        }

        var payloadLength = 16L + ((long)inputs.Count * 4)
            + ((long)preparations.Count * sizeof(ushort));
        if (payloadLength > int.MaxValue - RecordHeaderSize)
        {
            throw new ArgumentOutOfRangeException(nameof(inputs));
        }

        var result = new byte[RecordHeaderSize + (int)payloadLength];
        result[0] = 0x02;
        var payloadOffset = RecordHeaderSize;
        result[payloadOffset] = targetKind;
        result[payloadOffset + 1] = resultKind;
        Write(result, payloadOffset + 4, targetIndex);
        Write(result, payloadOffset + 6, resultIndex);
        Write(result, payloadOffset + 8, checked((uint)inputs.Count));
        Write(result, payloadOffset + 12, checked((uint)preparations.Count));
        payloadOffset += 16;
        foreach (var input in inputs)
        {
            result[payloadOffset] = input.SelectorKind;
            result[payloadOffset + 1] = input.SourceUse;
            Write(result, payloadOffset + 2, input.SelectorIndex);
            payloadOffset += 4;
        }
        foreach (var preparation in preparations)
        {
            Write(result, payloadOffset, preparation);
            payloadOffset += sizeof(ushort);
        }

        return result;
    }

    private static void Write(byte[] bytes, int offset, ushort value)
        => BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(offset), value);

    private static void Write(byte[] bytes, int offset, uint value)
        => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);
}
