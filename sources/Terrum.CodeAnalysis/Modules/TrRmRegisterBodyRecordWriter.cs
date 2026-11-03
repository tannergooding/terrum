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

    internal static byte[] EncodeExceptionDirectory(
        IReadOnlyList<TrRmExceptionRegionRow> regions,
        IReadOnlyList<TrRmExceptionHandlerRow> handlers,
        ReadOnlyMemory<byte> blockRegions)
    {
        ArgumentNullException.ThrowIfNull(regions);
        ArgumentNullException.ThrowIfNull(handlers);
        if (regions.Count is < 1 or > 65_536
            || handlers.Count > 65_536
            || blockRegions.Length is < 2 or > 131_072
            || (blockRegions.Length % sizeof(ushort)) != 0
            || blockRegions.Length / sizeof(ushort) > 65_536)
        {
            throw new ArgumentOutOfRangeException(nameof(regions));
        }

        var size = 16L + ((long)regions.Count * 16)
            + ((long)handlers.Count * 20) + blockRegions.Length;
        if (size > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(blockRegions));
        }

        var result = new byte[(int)size];
        result[0] = 0x0F;
        Write(result, RecordHeaderSize, checked((uint)regions.Count));
        Write(result, RecordHeaderSize + sizeof(uint),
            checked((uint)handlers.Count));
        Write(result, RecordHeaderSize + (2 * sizeof(uint)),
            checked((uint)(blockRegions.Length / sizeof(ushort))));
        var position = 16;
        foreach (var region in regions)
        {
            result[position] = region.Kind;
            result[position + 1] = region.Flags;
            Write(result, position + 2, region.ParentRegionIndex);
            Write(result, position + 4, region.EntryBlock);
            Write(result, position + 6, region.FinallyRegionIndex);
            Write(result, position + 8, region.HandlerFirst);
            Write(result, position + 12, region.HandlerCount);
            position += 16;
        }

        foreach (var handler in handlers)
        {
            Write(result, position, handler.ProtectedRegionIndex);
            result[position + 2] = handler.CatchKind;
            result[position + 3] = handler.HasBinding;
            Write(result, position + 4, handler.CatchTypeMapIndex);
            Write(result, position + 6, handler.BindingHomeIndex);
            result[position + 8] = handler.HasFilter;
            Write(result, position + 10, handler.FilterRegionIndex);
            Write(result, position + 12, handler.HandlerRegionIndex);
            Write(result, position + 16, handler.ExceptionWitnessRow);
            position += 20;
        }

        blockRegions.Span.CopyTo(result.AsSpan(position));
        _ = new TrRmExceptionDirectoryReader(result,
            blockRegions.Length / sizeof(ushort), 65_536, 65_536);
        return result;
    }

    internal static byte[] EncodeRegionExit(byte exitKind, byte hasTarget,
        ushort targetBlock, byte valueKind, byte valueUse, ushort valueIndex,
        IReadOnlyList<ushort> regions)
    {
        ArgumentNullException.ThrowIfNull(regions);
        if (exitKind is < 1 or > 4 || hasTarget > 1
            || (hasTarget == 0 && targetBlock != 0)
            || regions.Count > 65_535)
        {
            throw new ArgumentOutOfRangeException(nameof(exitKind));
        }

        var hasValue = valueKind is 1 or 2;
        var valid = exitKind switch
        {
            1 => hasTarget == 1 && valueKind == 0 && valueUse == 0
                && valueIndex == 0 && regions.Count is > 0 and <= 65_535,
            2 => hasTarget == 0 && !hasValue && valueKind == 0
                && valueUse == 0 && valueIndex == 0 && regions.Count == 2,
            3 => hasTarget == 0 && (valueKind is 1 or 2)
                && valueUse == 1 && regions.Count == 1
                && (valueKind != 1 || valueIndex <= 15),
            4 => hasTarget == 0 && !hasValue && valueKind == 0
                && valueUse == 0 && valueIndex == 0 && regions.Count == 1,
            _ => false,
        };
        if (!valid)
        {
            throw new ArgumentOutOfRangeException(nameof(valueKind));
        }

        var result = new byte[16 + (regions.Count * sizeof(ushort))];
        result[0] = 0x0A;
        result[RecordHeaderSize] = exitKind;
        result[RecordHeaderSize + 1] = hasTarget;
        Write(result, RecordHeaderSize + 2, targetBlock);
        result[RecordHeaderSize + 4] = valueKind;
        result[RecordHeaderSize + 5] = valueUse;
        Write(result, RecordHeaderSize + 6, valueIndex);
        Write(result, RecordHeaderSize + 8, checked((uint)regions.Count));
        for (var index = 0; index < regions.Count; index++)
        {
            Write(result, RecordHeaderSize + 12 + (index * sizeof(ushort)),
                regions[index]);
        }

        return result;
    }

    internal static byte[] EncodeNativeAddress(byte resultKind, ushort resultIndex,
        ushort methodMapIndex, ushort pointerTypeMapIndex,
        TrRmA3LayoutResourceLedger? resourceLedger = null)
    {
        if (resultKind is not (1 or 2))
        {
            throw new ArgumentOutOfRangeException(nameof(resultKind));
        }
        if (resultKind == 1 && (resultIndex > 15 || resultIndex == 14))
        {
            throw new ArgumentOutOfRangeException(nameof(resultIndex));
        }

        resourceLedger?.ConsumeWork(1 + 12UL + 5UL);
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
        ushort referentTypeMapIndex, TrRmArtifactSchema schema,
        TrRmA3LayoutResourceLedger? resourceLedger = null)
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

        resourceLedger?.ConsumeWork(1 + 24UL);
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
        ushort pointerTypeMapIndex, uint requiredElementCount,
        TrRmA3LayoutResourceLedger? resourceLedger = null)
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

        resourceLedger?.ConsumeWork(1 + 16UL);
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

    internal static byte[] EncodeSemanticStore(byte placeKind,
        byte sourceKind, byte transition, byte sourceUse, ushort placeIndex,
        ushort sourceIndex)
    {
        if (placeKind != 1 || sourceKind is not (1 or 2)
            || transition != 1 || sourceUse != 1 || placeIndex > 13
            || (sourceKind == 1 && sourceIndex > 15))
        {
            throw new ArgumentOutOfRangeException(nameof(placeKind));
        }

        var result = new byte[20];
        result[0] = 0x05;
        result[RecordHeaderSize] = placeKind;
        result[RecordHeaderSize + 1] = sourceKind;
        result[RecordHeaderSize + 2] = transition;
        result[RecordHeaderSize + 3] = sourceUse;
        Write(result, RecordHeaderSize + 4, placeIndex);
        Write(result, RecordHeaderSize + 6, sourceIndex);
        return result;
    }

    internal static byte[] EncodeEmptyArrayInitialization(
        ushort placeIndex, TrRmArtifactSchema schema)
    {
        if (!schema.UsesForeignInvocationStorageFixedArrayEmptyInitialization()
            || placeIndex > 13)
        {
            throw new ArgumentOutOfRangeException(nameof(placeIndex));
        }

        var result = new byte[20];
        result[0] = 0x05;
        result[RecordHeaderSize] = 1;
        result[RecordHeaderSize + 1] = 0;
        result[RecordHeaderSize + 2] = 1;
        result[RecordHeaderSize + 3] = 0;
        Write(result, RecordHeaderSize + 4, placeIndex);
        return result;
    }

    internal static byte[] EncodeConditionalEdge(ushort trueBlock,
        ushort falseBlock,
        TrRmA3LayoutResourceLedger? resourceLedger = null)
    {
        if (trueBlock == falseBlock)
        {
            throw new ArgumentOutOfRangeException(nameof(falseBlock));
        }

        resourceLedger?.ConsumeWork(1 + 8UL);
        var result = new byte[8];
        result[0] = 0x06;
        Write(result, RecordHeaderSize, trueBlock);
        Write(result, RecordHeaderSize + 2, falseBlock);
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
        IReadOnlyList<ushort> preparations,
        TrRmA3LayoutResourceLedger? resourceLedger = null)
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
            resourceLedger?.ConsumeWork(4);
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

        var resultLength = checked((ulong)(RecordHeaderSize + payloadLength));
        resourceLedger?.ConsumeWork(checked(resultLength + 1UL
            + (ulong)inputs.Count + (ulong)preparations.Count + 8UL));
        var result = new byte[(int)resultLength];
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
