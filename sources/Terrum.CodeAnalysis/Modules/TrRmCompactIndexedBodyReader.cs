// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;

namespace Terrum.CodeAnalysis.Modules;

/// <summary>Inspects the root-free, forward-branching scalar compact-indexed subset.</summary>
/// <remarks>Only scalar constants, operators, integer conversions, optional values, Copy, Select, local scalar calls, forward Branch/BranchIf, Return and Fail are supported; this is not an executable-contract verifier.</remarks>
public readonly ref struct TrRmCompactIndexedBodyReader
{
    private readonly record struct Edge(int Target, int[] Arguments);

    private readonly int[] _operationCounts;

    public int BlockCount { get; }
    public int ValueCount { get; }

    public int GetOperationCount(int block)
        => block > 0 && block <= BlockCount ? _operationCounts[block] : throw InvalidBody();

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

        _operationCounts = new int[BlockCount + 1];
        var mappedTypes = new ushort[typeCount + 1];
        for (var typeId = 1; typeId <= typeCount; typeId++)
        {
            var row = Read(body, ref position, 4);
            if (row == 0 || row > (uint)types.Count)
            {
                throw InvalidBody();
            }

            var type = TrRmCompactLinearBodyReader.ResolveType(types, (int)row);
            if (Array.IndexOf(mappedTypes, type, 1, typeId - 1) >= 0)
            {
                throw InvalidBody();
            }

            mappedTypes[typeId] = type;
        }

        var valueTypes = new ushort[ValueCount + 1];
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
        var successors = new Edge[BlockCount + 1][];
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

        var resultTypeSpec = signatures.GetResultTypeSpec(signature);
        var resultRow = types.GetRow(resultTypeSpec);
        var voidResult = resultRow.Length == 3 && resultRow[0] == 1
            && resultRow[1] == 0 && resultRow[2] == 0;
        var resultType = voidResult ? (ushort)0
            : TrRmCompactLinearBodyReader.ResolveType(types, resultTypeSpec);
        var localMethodStart = 0;
        var localMethodEnd = 0;
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

            if (block == 1 && (blockParameters[block].Length != parameterCount
                || liveIns[block].Length != 0
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
            if (operations > (extent.Length - cursor) / 3)
            {
                throw InvalidBody();
            }

            _operationCounts[block] = operations;
            var nextResult = (int)starts[block - 1] + blockParameters[block].Length + 1;
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
                var (opcode, referenceWidth, operationCountWidth, dataWidth) = ReadOperation(extent, ref cursor);
                var result = ReadCount(extent, ref cursor, referenceWidth);
                if (result == 0 && opcode == 0x12)
                {
                    // A void call has no value descriptor or definition offset.
                }
                else if (result != nextResult++ || result > ValueCount || owner[result] != block
                    || definitions[result - 1] != operationOffset)
                {
                    throw InvalidBody();
                }

                var secondResult = 0;
                if (opcode == 0x18)
                {
                    secondResult = ReadCount(extent, ref cursor, referenceWidth);
                    if (secondResult != nextResult++ || secondResult > ValueCount
                        || owner[secondResult] != block
                        || definitions[secondResult - 1] != operationOffset)
                    {
                        throw InvalidBody();
                    }
                }

                if (opcode is 1 or 2)
                {
                    var typeId = ReadCount(extent, ref cursor, referenceWidth);
                    var bits = ReadData(extent, ref cursor, dataWidth);
                    if (typeId == 0 || typeId > typeCount
                        || mappedTypes[typeId] != valueTypes[result]
                        || opcode == 1 && !FitsBitPattern(valueTypes[result], bits)
                        || opcode == 2 && (valueTypes[result] is not (8 or 9 or 0x0D or 0x0E)
                            || !FitsSignedConstant(valueTypes[result], bits, dataWidth)))
                    {
                        throw InvalidBody();
                    }
                }
                else if (opcode == 3)
                {
                    var source = ReadCount(extent, ref cursor, referenceWidth);
                    Use(source, ordinal);
                    if (valueTypes[source] != valueTypes[result])
                    {
                        throw InvalidBody();
                    }
                }
                else if (opcode is 8 or 9)
                {
                    var typeId = ReadCount(extent, ref cursor, referenceWidth);
                    if (typeId == 0 || typeId > typeCount
                        || mappedTypes[typeId] < 0x100
                        || mappedTypes[typeId] != valueTypes[result])
                    {
                        throw InvalidBody();
                    }

                    if (opcode == 8)
                    {
                        var source = ReadCount(extent, ref cursor, referenceWidth);
                        Use(source, ordinal);
                        if (valueTypes[source] != (mappedTypes[typeId] & 0xFF))
                        {
                            throw InvalidBody();
                        }
                    }
                }
                else if (opcode == 0x0A)
                {
                    var source = ReadCount(extent, ref cursor, referenceWidth);
                    Use(source, ordinal);
                    if (valueTypes[source] < 0x100 || valueTypes[result] != 1)
                    {
                        throw InvalidBody();
                    }
                }
                else if (opcode == 4)
                {
                    var kind = Read(extent, ref cursor, 1);
                    var source = ReadCount(extent, ref cursor, referenceWidth);
                    Use(source, ordinal);
                    var type = valueTypes[source];
                    if (valueTypes[result] != type || !(kind == 1 && type == 1
                        || kind is 2 or 3 && IsInteger(type)))
                    {
                        throw InvalidBody();
                    }
                }
                else if (opcode == 5)
                {
                    var kind = Read(extent, ref cursor, 1);
                    var left = ReadCount(extent, ref cursor, referenceWidth);
                    var right = ReadCount(extent, ref cursor, referenceWidth);
                    Use(left, ordinal);
                    Use(right, ordinal);
                    var type = valueTypes[left];
                    var comparison = kind is >= 4 and <= 9;
                    if (kind is < 1 or > 12 || type != valueTypes[right]
                        || (!IsInteger(type) && !(kind is 4 or 5 && type is 1 or 3))
                        || valueTypes[result] != (comparison ? 1 : type))
                    {
                        throw InvalidBody();
                    }
                }
                else if (opcode == 6)
                {
                    var typeId = ReadCount(extent, ref cursor, referenceWidth);
                    var policy = Read(extent, ref cursor, 1);
                    var source = ReadCount(extent, ref cursor, referenceWidth);
                    Use(source, ordinal);
                    if (typeId == 0 || typeId > typeCount
                        || mappedTypes[typeId] != valueTypes[result]
                        || !IsInteger(valueTypes[result]) || !IsInteger(valueTypes[source])
                        || policy is < 1 or > 3
                        || policy == 3 && (valueTypes[result] is 8 or 0x0D)
                            != (valueTypes[source] is 8 or 0x0D))
                    {
                        throw InvalidBody();
                    }
                }
                else if (opcode == 7)
                {
                    var condition = ReadCount(extent, ref cursor, referenceWidth);
                    var whenTrue = ReadCount(extent, ref cursor, referenceWidth);
                    var whenFalse = ReadCount(extent, ref cursor, referenceWidth);
                    Use(condition, ordinal);
                    Use(whenTrue, ordinal);
                    Use(whenFalse, ordinal);
                    if (valueTypes[condition] != 1
                        || valueTypes[whenTrue] != valueTypes[whenFalse]
                        || valueTypes[result] != valueTypes[whenTrue])
                    {
                        throw InvalidBody();
                    }
                }
                else if (opcode == 0x12)
                {
                    var method = ReadCount(extent, ref cursor, referenceWidth);
                    if (localMethodStart == 0)
                    {
                        (localMethodStart, localMethodEnd) = modules.GetMethodDefRange(
                            methods.GetOwnerModule(methodRow));
                    }
                    if (method == 0 || method > localMethodEnd - localMethodStart)
                    {
                        throw InvalidBody();
                    }

                    var target = localMethodStart + method - 1;
                    var targetSignature = methods.GetSignature(target);
                    if (methods.GetOwnerType(target) != 0 || methods.GetBodyRow(target) == 0
                        || signatures.GetReceiverTypeSpec(targetSignature) != 0
                        || signatures.GetGenericCount(targetSignature) != 0)
                    {
                        throw InvalidBody();
                    }

                    var targetResult = signatures.GetResultTypeSpec(targetSignature);
                    var targetRow = types.GetRow(targetResult);
                    var targetVoid = targetRow.Length == 3 && targetRow[0] == 1
                        && targetRow[1] == 0 && targetRow[2] == 0;
                    if (targetVoid ? result != 0
                        : result == 0 || valueTypes[result]
                            != TrRmCompactLinearBodyReader.ResolveType(types, targetResult))
                    {
                        throw InvalidBody();
                    }

                    var (first, count) = signatures.GetParamRange(targetSignature);
                    var arguments = ReadCount(extent, ref cursor, operationCountWidth);
                    if (arguments != count || arguments > (extent.Length - cursor) / referenceWidth)
                    {
                        throw InvalidBody();
                    }

                    for (var argument = 0; argument < arguments; argument++)
                    {
                        var value = ReadCount(extent, ref cursor, referenceWidth);
                        Use(value, ordinal);
                        if (parameters.GetMode(first + argument) != 0
                            || valueTypes[value] != TrRmCompactLinearBodyReader.ResolveType(types,
                                parameters.GetTypeSpec(first + argument)))
                        {
                            throw InvalidBody();
                        }
                    }
                }
                else if (opcode == 0x15)
                {
                    var kind = Read(extent, ref cursor, 1);
                    var left = ReadCount(extent, ref cursor, referenceWidth);
                    var right = ReadCount(extent, ref cursor, referenceWidth);
                    if (kind is < 1 or > 7 || !IsInteger(valueTypes[result])
                        || left <= 0 || left > ValueCount || right <= 0 || right > ValueCount
                        || valueTypes[left] != valueTypes[result]
                        || valueTypes[right] != (kind is 6 or 7 ? 0x0D : valueTypes[result]))
                    {
                        throw InvalidBody();
                    }

                    Use(left, ordinal);
                    Use(right, ordinal);
                }
                else if (opcode == 0x16)
                {
                    var typeId = ReadCount(extent, ref cursor, referenceWidth);
                    var source = ReadCount(extent, ref cursor, referenceWidth);
                    Use(source, ordinal);
                    if (typeId == 0 || typeId > typeCount
                        || mappedTypes[typeId] != valueTypes[result]
                        || !IsInteger(valueTypes[source]) || !IsInteger(valueTypes[result]))
                    {
                        throw InvalidBody();
                    }
                }
                else if (opcode == 0x17)
                {
                    var source = ReadCount(extent, ref cursor, referenceWidth);
                    Use(source, ordinal);
                    if (valueTypes[source] is not (8 or 9)
                        || valueTypes[result] != valueTypes[source])
                    {
                        throw InvalidBody();
                    }
                }
                else if (opcode == 0x18)
                {
                    var left = ReadCount(extent, ref cursor, referenceWidth);
                    var right = ReadCount(extent, ref cursor, referenceWidth);
                    if (left <= 0 || left > ValueCount || right <= 0 || right > ValueCount
                        || !IsInteger(valueTypes[result])
                        || valueTypes[left] != valueTypes[result]
                        || valueTypes[right] != valueTypes[result]
                        || valueTypes[secondResult] != valueTypes[result])
                    {
                        throw InvalidBody();
                    }

                    Use(left, ordinal);
                    Use(right, ordinal);
                }
                else
                {
                    throw InvalidBody();
                }

                if (result != 0 && defined[result])
                {
                    throw InvalidBody();
                }

                if (result != 0)
                {
                    defined[result] = true;
                }
                if (secondResult != 0)
                {
                    defined[secondResult] = true;
                }
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
            var (terminator, terminatorWidth, countWidth, terminatorDataWidth) =
                ReadTerminator(extent, ref cursor);
            if (terminator == 0x81)
            {
                var condition = ReadCount(extent, ref cursor, terminatorWidth);
                if (condition <= 0 || condition > ValueCount || valueTypes[condition] != 1)
                {
                    throw InvalidBody();
                }

                Use(condition, operations);
                var whenTrue = ReadEdge(extent, ref cursor, block, BlockCount,
                    ValueCount, terminatorWidth, countWidth);
                var whenFalse = ReadEdge(extent, ref cursor, block, BlockCount,
                    ValueCount, terminatorWidth, countWidth);
                successors[block] = [whenTrue, whenFalse];
                foreach (var edge in successors[block])
                {
                    foreach (var value in edge.Arguments)
                    {
                        Use(value, operations);
                    }
                }

            }
            else if (terminator == 0x80)
            {
                var edge = ReadEdge(extent, ref cursor, block, BlockCount,
                    ValueCount, terminatorWidth, countWidth);
                foreach (var value in edge.Arguments)
                {
                    Use(value, operations);
                }

                successors[block] = [edge];
            }
            else if (terminator == 0x82)
            {
                var selector = ReadCount(extent, ref cursor, terminatorWidth);
                if (selector <= 0 || selector > ValueCount)
                {
                    throw InvalidBody();
                }

                Use(selector, operations);
                var selectorType = valueTypes[selector];
                if (selectorType is not (1 or 3) && !IsInteger(selectorType))
                {
                    throw InvalidBody();
                }

                var cases = ReadCount(extent, ref cursor, countWidth);
                if (extent.Length - cursor < terminatorWidth + countWidth
                    || cases > (extent.Length - cursor - terminatorWidth - countWidth)
                        / (terminatorDataWidth + terminatorWidth + countWidth))
                {
                    throw InvalidBody();
                }

                var edges = new Edge[cases + 1];
                ulong previous = 0;
                for (var index = 0; index < cases; index++)
                {
                    var bits = ReadData(extent, ref cursor, terminatorDataWidth);
                    if (!FitsBitPattern(selectorType, bits)
                        || index != 0 && bits <= previous)
                    {
                        throw InvalidBody();
                    }

                    previous = bits;
                    edges[index] = ReadEdge(extent, ref cursor, block, BlockCount,
                        ValueCount, terminatorWidth, countWidth);
                }

                edges[cases] = ReadEdge(extent, ref cursor, block, BlockCount,
                    ValueCount, terminatorWidth, countWidth);
                successors[block] = edges;
                foreach (var edge in edges)
                {
                    foreach (var value in edge.Arguments)
                    {
                        Use(value, operations);
                    }
                }
            }
            else if (terminator == 0x83)
            {
                var value = ReadCount(extent, ref cursor, terminatorWidth);
                if (value == 0 ? !voidResult
                    : voidResult || value > ValueCount || valueTypes[value] != resultType)
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

            foreach (var edge in successors[block])
            {
                reachable[edge.Target] = true;
            }
        }

        for (var block = BlockCount; block > 0; block--)
        {
            var neededOut = new HashSet<int>();
            foreach (var edge in successors[block])
            {
                var targetParameters = blockParameters[edge.Target];
                if (targetParameters.Length != edge.Arguments.Length)
                {
                    throw InvalidBody();
                }

                for (var argument = 0; argument < targetParameters.Length; argument++)
                {
                    if (valueTypes[targetParameters[argument]]
                        != valueTypes[edge.Arguments[argument]])
                    {
                        throw InvalidBody();
                    }
                }

                neededOut.UnionWith(liveIns[edge.Target]);
                neededOut.UnionWith(edge.Arguments);
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
        TrRmTypeSpecTableReader types, int first, ushort[] valueTypes)
    {
        for (var index = 0; index < values.Length; index++)
        {
            var type = TrRmCompactLinearBodyReader.ResolveType(types,
                parameters.GetTypeSpec(first + index));
            if (values[index] != index + 1 || parameters.GetMode(first + index) != 0
                || type != valueTypes[values[index]])
            {
                return false;
            }
        }

        return true;
    }

    private static bool Contains(int[] values, int value)
        => Array.BinarySearch(values, value) >= 0;

    private static bool IsInteger(ushort type)
        => type is 8 or 9 or 0x0D or 0x0E;

    private static bool FitsBitPattern(ushort type, ulong bits)
        => type switch
        {
            1 => bits <= 1,
            3 => bits <= ushort.MaxValue,
            8 or 0x0D => bits <= uint.MaxValue,
            9 or 0x0E => true,
            _ => false,
        };

    private static (uint Opcode, int ReferenceWidth, int CountWidth, int DataWidth) ReadTerminator(
        ReadOnlySpan<byte> data, ref int position)
    {
        var opcode = Read(data, ref position, 1);
        if (opcode != 0xF0)
        {
            return (opcode, 1, 1, 1);
        }

        var layout = Read(data, ref position, 1);
        opcode = Read(data, ref position, 1);
        if (layout == 0 || (layout & 0xC0) != 0
            || (layout & 3) == 3 || ((layout >> 2) & 3) == 3
            || opcode is not (0x80 or 0x81 or 0x82 or 0x83)
            || opcode != 0x82 && (layout & 0x30) != 0
            || opcode == 0x83 && (layout & 0x0C) != 0)
        {
            throw InvalidBody();
        }

        var referenceWidth = (layout & 3) switch { 0 => 1, 1 => 2, _ => 4 };
        var countWidth = ((layout >> 2) & 3) switch { 0 => 1, 1 => 2, _ => 4 };
        var dataWidth = opcode == 0x82 ? 1 << ((int)(layout >> 4) & 3) : 0;
        return (opcode, referenceWidth, countWidth, dataWidth);
    }

    private static (uint Opcode, int ReferenceWidth, int CountWidth, int DataWidth) ReadOperation(
        ReadOnlySpan<byte> data, ref int position)
    {
        var opcode = Read(data, ref position, 1);
        if (opcode != 0xF0)
        {
            return (opcode, 1, 1, opcode is 1 or 2 ? 1 : 0);
        }

        var layout = Read(data, ref position, 1);
        opcode = Read(data, ref position, 1);
        if (layout == 0 || (layout & 0xC0) != 0 || (layout & 3) == 3
            || ((layout >> 2) & 3) == 3
            || opcode != 0x12 && (layout & 0x0C) != 0
            || opcode is not (1 or 2) && (layout & 0x30) != 0)
        {
            throw InvalidBody();
        }

        var referenceWidth = (layout & 3) switch { 0 => 1, 1 => 2, _ => 4 };
        var countWidth = ((layout >> 2) & 3) switch { 0 => 1, 1 => 2, _ => 4 };
        var dataWidth = opcode is 1 or 2 ? 1 << ((int)(layout >> 4) & 3) : 0;
        return (opcode, referenceWidth, countWidth, dataWidth);
    }

    private static ulong ReadData(ReadOnlySpan<byte> data, ref int position, int width)
    {
        if (width != 8)
        {
            return Read(data, ref position, width);
        }

        if (position > data.Length - width)
        {
            throw InvalidBody();
        }

        var bits = BinaryPrimitives.ReadUInt64LittleEndian(data[position..]);
        position += width;
        return bits;
    }

    private static bool FitsSignedConstant(ushort type, ulong bits, int dataWidth)
    {
        var shift = 64 - dataWidth * 8;
        var value = unchecked((long)(bits << shift)) >> shift;
        return type switch
        {
            8 => value is >= int.MinValue and <= int.MaxValue,
            9 => true,
            0x0D => value is >= 0 and <= uint.MaxValue,
            _ => value >= 0,
        };
    }

    private static Edge ReadEdge(ReadOnlySpan<byte> data, ref int position,
        int block, int count, int valueCount, int referenceWidth, int countWidth)
    {
        var target = ReadCount(data, ref position, referenceWidth);
        var arguments = ReadCount(data, ref position, countWidth);
        if (target <= block || target > count || arguments > valueCount
            || arguments > (data.Length - position) / referenceWidth)
        {
            throw InvalidBody();
        }

        var values = new int[arguments];
        for (var index = 0; index < arguments; index++)
        {
            values[index] = ReadCount(data, ref position, referenceWidth);
        }

        return new Edge(target, values);
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
