// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;

namespace Terrum.CodeAnalysis.Modules;

/// <summary>Inspects the root-free, forward-branching scalar compact-indexed subset.</summary>
/// <remarks>Only checked integer addition, BranchIf without edge arguments, Return and Fail are supported; this is not an executable-contract verifier.</remarks>
public readonly ref struct TrRmCompactIndexedBodyReader
{
    public int BlockCount { get; }
    public int ValueCount { get; }

    public TrRmCompactIndexedBodyReader(TrRmBodyTableReader bodies, int methodRow,
        TrRmModuleTableReader modules, TrRmMethodDefTableReader methods,
        TrRmMethodSigTableReader signatures, TrRmParamTableReader parameters,
        TrRmTypeSpecTableReader types)
    {
        var bodyRow = methods.GetBodyRow(methodRow);
        if (bodyRow == 0
            || modules.GetRequirements(methods.GetOwnerModule(methodRow)).RuntimePolicy != 0)
        {
            throw InvalidBody();
        }

        var signature = methods.GetSignature(methodRow);
        if (signatures.GetReceiverTypeSpec(signature) != 0
            || signatures.GetGenericCount(signature) != 0)
        {
            throw InvalidBody();
        }

        var body = bodies.GetRow(bodyRow);
        if (body.IsEmpty || (body[0] & 0xFC) != 0x08 || (body[0] & 3) == 3)
        {
            throw InvalidBody();
        }

        var width = (body[0] & 3) switch { 0 => 1, 1 => 2, _ => 4 };
        var sentinel = width == 4 ? uint.MaxValue : (1u << (width * 8)) - 1;
        var position = 1;
        BlockCount = ReadCount(body, ref position, width);
        ValueCount = ReadCount(body, ref position, width);
        var regionCount = ReadCount(body, ref position, width);
        var typeCount = ReadCount(body, ref position, width);
        if (BlockCount == 0 || BlockCount > (body.Length - position) / (3 * width + 1)
            || ValueCount > (body.Length - position) / (3 * width)
            || regionCount != 0 || typeCount > (body.Length - position) / 4)
        {
            throw InvalidBody();
        }

        var mappedTypes = new byte[typeCount + 1];
        for (var typeId = 1; typeId <= typeCount; typeId++)
        {
            var row = Read(body, ref position, 4);
            if (row == 0 || row > (uint)types.Count)
            {
                throw InvalidBody();
            }

            var type = types.GetRow((int)row);
            if (type.Length != 3 || type[0] != 1 || type[1] != 0
                || type[2] is not (1 or 8 or 9 or 0x0D or 0x0E)
                || Array.IndexOf(mappedTypes, type[2], 1, typeId - 1) >= 0)
            {
                throw InvalidBody();
            }

            mappedTypes[typeId] = type[2];
        }

        var valueTypes = new byte[ValueCount + 1];
        var useBlockCounts = new int[ValueCount + 1];
        for (var value = 1; value <= ValueCount; value++)
        {
            var typeId = ReadCount(body, ref position, width);
            var uses = ReadCount(body, ref position, width);
            if (typeId == 0 || typeId > typeCount || uses > BlockCount)
            {
                throw InvalidBody();
            }

            valueTypes[value] = mappedTypes[typeId];
            useBlockCounts[value] = uses;
        }

        var starts = ReadIndex(body, ref position, width, BlockCount + 1);
        var definitions = ReadIndex(body, ref position, width, ValueCount);
        var offsets = ReadIndex(body, ref position, width, BlockCount + 1);
        var terminators = ReadIndex(body, ref position, width, BlockCount);
        if (starts[0] != 0 || starts[BlockCount] != ValueCount
            || offsets[0] != 0 || offsets[BlockCount] != body.Length - position)
        {
            throw InvalidBody();
        }

        var blockData = body[position..];
        var (firstParameter, parameterCount) = signatures.GetParamRange(signature);
        if (parameterCount > ValueCount)
        {
            throw InvalidBody();
        }

        var owner = new int[ValueCount + 1];
        var valueCount = ValueCount;
        var defined = new bool[ValueCount + 1];
        var actualUses = new int[ValueCount + 1];
        var blockParameters = new int[BlockCount + 1][];
        var liveIns = new int[BlockCount + 1][];
        var liveOuts = new int[BlockCount + 1][];
        var successors = new int[BlockCount + 1][];
        var externalUses = new HashSet<int>[BlockCount + 1];
        for (var block = 1; block <= BlockCount; block++)
        {
            if (starts[block] < starts[block - 1]
                || starts[block] > ValueCount
                || offsets[block] <= offsets[block - 1]
                || offsets[block] > blockData.Length
                || terminators[block - 1] >= offsets[block] - offsets[block - 1])
            {
                throw InvalidBody();
            }

            for (var value = starts[block - 1] + 1; value <= starts[block]; value++)
            {
                owner[value] = block;
            }
        }

        if (starts[1] < parameterCount)
        {
            throw InvalidBody();
        }

        var resultRow = types.GetRow(signatures.GetResultTypeSpec(signature));
        var voidResult = resultRow.Length == 3 && resultRow[0] == 1
            && resultRow[1] == 0 && resultRow[2] == 0;
        if (!voidResult && (resultRow.Length != 3 || resultRow[0] != 1
            || resultRow[1] != 0
            || resultRow[2] is not (1 or 8 or 9 or 0x0D or 0x0E)))
        {
            throw InvalidBody();
        }

        for (var block = 1; block <= BlockCount; block++)
        {
            var extent = blockData[(int)offsets[block - 1]..(int)offsets[block]];
            var cursor = 0;
            var presence = Read(extent, ref cursor, 1);
            if ((presence & ~0x1Eu) != 0)
            {
                throw InvalidBody();
            }

            blockParameters[block] = ReadList(extent, ref cursor, width, (presence & 2) != 0, ValueCount);
            liveIns[block] = ReadList(extent, ref cursor, width, (presence & 4) != 0, ValueCount);
            liveOuts[block] = ReadList(extent, ref cursor, width, (presence & 8) != 0, ValueCount);
            var declaredLastUses = new Dictionary<int, int>();
            if ((presence & 16) != 0)
            {
                var count = ReadCount(extent, ref cursor, width);
                if (count == 0 || count > ValueCount || count > (extent.Length - cursor) / (2 * width))
                {
                    throw InvalidBody();
                }

                var previous = 0;
                for (var item = 0; item < count; item++)
                {
                    var value = ReadCount(extent, ref cursor, width);
                    var lastUse = ReadCount(extent, ref cursor, width);
                    if (value <= previous || value > ValueCount)
                    {
                        throw InvalidBody();
                    }

                    declaredLastUses.Add(value, lastUse);
                    previous = value;
                }
            }

            var expectedParameters = block == 1 ? parameterCount : 0;
            if (blockParameters[block].Length != expectedParameters
                || block == 1 && (liveIns[block].Length != 0
                    || !ParametersMatch(blockParameters[block], parameters, types,
                        firstParameter, valueTypes)))
            {
                throw InvalidBody();
            }

            foreach (var value in blockParameters[block])
            {
                if (owner[value] != block || definitions[value - 1] != sentinel)
                {
                    throw InvalidBody();
                }

                defined[value] = true;
            }

            var operations = ReadCount(extent, ref cursor, width);
            if (operations > (extent.Length - cursor) / 5)
            {
                throw InvalidBody();
            }

            var nextResult = (int)starts[block - 1] + expectedParameters + 1;
            var observedLastUses = new Dictionary<int, int>();
            externalUses[block] = new HashSet<int>();
            var useOffset = cursor;
            void Use(int value, int ordinal)
            {
                if (value <= 0 || value > valueCount
                    || owner[value] > block
                    || (owner[value] == block && (!defined[value]
                        || definitions[value - 1] != sentinel
                            && definitions[value - 1] >= useOffset))
                    || owner[value] != block && !Contains(liveIns[block], value))
                {
                    throw InvalidBody();
                }

                if (owner[value] != block)
                {
                    externalUses[block].Add(value);
                }

                if (!observedLastUses.ContainsKey(value))
                {
                    actualUses[value]++;
                }

                observedLastUses[value] = ordinal;
            }

            for (var ordinal = 0; ordinal < operations; ordinal++)
            {
                var operationOffset = cursor;
                useOffset = cursor;
                if (Read(extent, ref cursor, 1) != 0x15)
                {
                    throw InvalidBody();
                }

                var result = ReadCount(extent, ref cursor, 1);
                var kind = Read(extent, ref cursor, 1);
                var left = ReadCount(extent, ref cursor, 1);
                var right = ReadCount(extent, ref cursor, 1);
                if (result != nextResult++ || result > ValueCount || owner[result] != block
                    || definitions[result - 1] != operationOffset || kind != 1
                    || valueTypes[result] is not (8 or 9 or 0x0D or 0x0E)
                    || left <= 0 || left > ValueCount || right <= 0 || right > ValueCount
                    || valueTypes[left] != valueTypes[result]
                    || valueTypes[right] != valueTypes[result])
                {
                    throw InvalidBody();
                }

                Use(left, ordinal);
                Use(right, ordinal);
                if (defined[result])
                {
                    throw InvalidBody();
                }

                defined[result] = true;
            }

            if (cursor != terminators[block - 1])
            {
                throw InvalidBody();
            }

            if (nextResult != starts[block] + 1)
            {
                throw InvalidBody();
            }

            useOffset = cursor;
            var terminator = Read(extent, ref cursor, 1);
            if (terminator == 0x81)
            {
                var condition = ReadCount(extent, ref cursor, 1);
                if (condition <= 0 || condition > ValueCount || valueTypes[condition] != 1)
                {
                    throw InvalidBody();
                }

                Use(condition, operations);
                var trueTarget = ReadEdge(extent, ref cursor, block, BlockCount);
                var falseTarget = ReadEdge(extent, ref cursor, block, BlockCount);
                successors[block] = [trueTarget, falseTarget];
            }
            else if (terminator == 0x83)
            {
                var value = ReadCount(extent, ref cursor, 1);
                if (value == 0 ? !voidResult
                    : voidResult || value > ValueCount || valueTypes[value] != resultRow[2])
                {
                    throw InvalidBody();
                }

                if (value != 0)
                {
                    Use(value, operations);
                }

                successors[block] = [];
            }
            else if (terminator == 0x87 && Read(extent, ref cursor, 1) is >= 1 and <= 4)
            {
                successors[block] = [];
            }
            else
            {
                throw InvalidBody();
            }

            if (cursor != extent.Length || declaredLastUses.Count != observedLastUses.Count)
            {
                throw InvalidBody();
            }

            foreach (var (value, ordinal) in declaredLastUses)
            {
                if (!observedLastUses.TryGetValue(value, out var actual) || actual != ordinal)
                {
                    throw InvalidBody();
                }
            }

            for (var value = starts[block - 1] + 1; value <= starts[block]; value++)
            {
                if (definitions[value - 1] == sentinel
                    ? !Contains(blockParameters[block], (int)value)
                    : definitions[value - 1] >= terminators[block - 1] || !defined[value])
                {
                    throw InvalidBody();
                }
            }
        }

        for (var value = 1; value <= ValueCount; value++)
        {
            if (actualUses[value] != useBlockCounts[value])
            {
                throw InvalidBody();
            }
        }

        var reachable = new bool[BlockCount + 1];
        reachable[1] = true;
        for (var block = 1; block <= BlockCount; block++)
        {
            if (!reachable[block])
            {
                throw InvalidBody();
            }

            foreach (var target in successors[block])
            {
                reachable[target] = true;
            }
        }

        for (var block = BlockCount; block > 0; block--)
        {
            var neededOut = new HashSet<int>();
            foreach (var target in successors[block])
            {
                neededOut.UnionWith(liveIns[target]);
            }

            if (!neededOut.SetEquals(liveOuts[block]))
            {
                throw InvalidBody();
            }

            var neededIn = new HashSet<int>(externalUses[block]);
            foreach (var value in neededOut)
            {
                if (owner[value] != block)
                {
                    neededIn.Add(value);
                }
            }

            if (!neededIn.SetEquals(liveIns[block]))
            {
                throw InvalidBody();
            }
        }
    }

    private static bool ParametersMatch(int[] values, TrRmParamTableReader parameters,
        TrRmTypeSpecTableReader types, int first, byte[] valueTypes)
    {
        for (var index = 0; index < values.Length; index++)
        {
            var type = types.GetRow(parameters.GetTypeSpec(first + index));
            if (values[index] != index + 1 || parameters.GetMode(first + index) != 0
                || type.Length != 3 || type[0] != 1 || type[1] != 0
                || type[2] != valueTypes[values[index]])
            {
                return false;
            }
        }

        return true;
    }

    private static bool Contains(int[] values, int value)
        => Array.BinarySearch(values, value) >= 0;

    private static int ReadEdge(ReadOnlySpan<byte> data, ref int position, int block, int count)
    {
        var target = ReadCount(data, ref position, 1);
        var arguments = ReadCount(data, ref position, 1);
        if (target <= block || target > count || arguments != 0)
        {
            throw InvalidBody();
        }

        return target;
    }

    private static int[] ReadList(ReadOnlySpan<byte> data, ref int position, int width,
        bool present, int maximum)
    {
        if (!present)
        {
            return [];
        }

        var count = ReadCount(data, ref position, width);
        if (count == 0 || count > maximum || count > (data.Length - position) / width)
        {
            throw InvalidBody();
        }

        var values = new int[count];
        for (var index = 0; index < count; index++)
        {
            values[index] = ReadCount(data, ref position, width);
            if (values[index] <= 0 || values[index] > maximum
                || index > 0 && values[index] <= values[index - 1])
            {
                throw InvalidBody();
            }
        }

        return values;
    }

    private static uint[] ReadIndex(ReadOnlySpan<byte> data, ref int position, int width,
        int count)
    {
        if (count > (data.Length - position) / width)
        {
            throw InvalidBody();
        }

        var values = new uint[count];
        for (var index = 0; index < count; index++)
        {
            values[index] = Read(data, ref position, width);
        }

        return values;
    }

    private static int ReadCount(ReadOnlySpan<byte> data, ref int position, int width)
    {
        var count = Read(data, ref position, width);
        if (count > int.MaxValue)
        {
            throw InvalidBody();
        }

        return (int)count;
    }

    private static uint Read(ReadOnlySpan<byte> data, ref int position, int width)
    {
        if (position > data.Length - width)
        {
            throw InvalidBody();
        }

        var result = width switch
        {
            1 => data[position],
            2 => BinaryPrimitives.ReadUInt16LittleEndian(data[position..]),
            _ => BinaryPrimitives.ReadUInt32LittleEndian(data[position..]),
        };
        position += width;
        return result;
    }

    private static InvalidDataException InvalidBody()
        => new("Invalid or unsupported TRRM compact-indexed Body.");
}
