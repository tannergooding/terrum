// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.IO;

namespace Terrum.CodeAnalysis.Modules;

/// <summary>Validates and indexes the initial scalar compact-linear Body subset.</summary>
/// <remarks>Rejects other assigned operation schemas; it does not execute a Body or validate published effects.</remarks>
public readonly ref struct TrRmCompactLinearBodyReader
{
    private readonly ValueInfo[] _values;

    public int ValueCount => _values.Length - 1;
    public int OperationCount { get; }

    private struct ValueInfo
    {
        public byte TypeCode;
        public int DefinitionOffset;
        public int LastUseOrdinal;
        public bool Used;
    }

    public TrRmCompactLinearBodyReader(TrRmBodyTableReader bodies, int methodRow,
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
        if (body.IsEmpty || (body[0] & 0xFC) != 0x04 || (body[0] & 3) == 3)
        {
            throw InvalidBody();
        }

        var width = (body[0] & 3) switch { 0 => 1, 1 => 2, _ => 4 };
        var position = 1;
        var typeCount = ReadLocal(body, ref position, width);
        if (typeCount > (body.Length - position) / 4)
        {
            throw InvalidBody();
        }

        var mappedTypes = new byte[typeCount + 1];
        for (var index = 1; index <= typeCount; index++)
        {
            var reference = Read(body, ref position, 4);
            if (reference == 0 || reference > (uint)types.Count)
            {
                throw InvalidBody();
            }

            var type = types.GetRow((int)reference);
            if (type.Length != 3 || type[0] != 1 || type[1] != 0
                || type[2] is not (1 or 3 or 8 or 9 or 0x0D or 0x0E))
            {
                throw InvalidBody();
            }

            for (var previous = 1; previous < index; previous++)
            {
                if (mappedTypes[previous] == type[2])
                {
                    throw InvalidBody();
                }
            }

            mappedTypes[index] = type[2];
        }

        var (first, parameterCount) = signatures.GetParamRange(signature);
        var operationCount = ReadLocal(body, ref position, width);
        var maximum = width == 4 ? int.MaxValue : (1 << (width * 8)) - 1;
        if (parameterCount > maximum || operationCount > maximum - parameterCount
            || operationCount > (body.Length - position) / 3)
        {
            throw InvalidBody();
        }

        _values = new ValueInfo[parameterCount + operationCount + 1];
        OperationCount = operationCount;
        for (var index = 1; index <= parameterCount; index++)
        {
            if (parameters.GetMode(first + index - 1) != 0)
            {
                throw InvalidBody();
            }

            _values[index].TypeCode = MatchType(types, mappedTypes,
                parameters.GetTypeSpec(first + index - 1));
            _values[index].DefinitionOffset = -1;
        }

        var valueCount = parameterCount;
        for (var ordinal = 0; ordinal < operationCount; ordinal++)
        {
            var offset = position;
            var (opcode, referenceWidth, dataWidth) = ReadInstruction(body, ref position);
            if (opcode is not (1 or 2 or 3 or 4 or 5 or 6 or 7 or 0x15 or 0x16 or 0x17)
                || valueCount == maximum)
            {
                throw InvalidBody();
            }

            var result = ReadLocal(body, ref position, referenceWidth);
            if (result != ++valueCount)
            {
                throw InvalidBody();
            }

            byte resultType;
            if (opcode is 1 or 2)
            {
                var typeId = ReadLocal(body, ref position, referenceWidth);
                if (typeId == 0 || typeId > typeCount)
                {
                    throw InvalidBody();
                }

                resultType = mappedTypes[typeId];
                var bits = Read(body, ref position, dataWidth);
                if (opcode == 1)
                {
                    var size = BitWidth(resultType);
                    if (size < 64 && (bits >> size) != 0
                        || resultType == 1 && bits > 1)
                    {
                        throw InvalidBody();
                    }
                }
                else
                {
                    if (!IsInteger(resultType))
                    {
                        throw InvalidBody();
                    }

                    var shift = 64 - dataWidth * 8;
                    var signed = unchecked((long)(bits << shift)) >> shift;
                    var inRange = resultType switch
                    {
                        8 => signed is >= int.MinValue and <= int.MaxValue,
                        9 => true,
                        0x0D => signed is >= 0 and <= uint.MaxValue,
                        _ => signed >= 0,
                    };
                    if (!inRange)
                    {
                        throw InvalidBody();
                    }
                }
            }
            else if (opcode == 3)
            {
                resultType = Use(body, ref position, referenceWidth, result, ordinal);
            }
            else if (opcode is 6 or 0x16)
            {
                var typeId = ReadLocal(body, ref position, referenceWidth);
                if (typeId == 0 || typeId > typeCount)
                {
                    throw InvalidBody();
                }

                resultType = mappedTypes[typeId];
                var policy = opcode == 6 ? ReadByte(body, ref position) : (byte)0;
                var source = Use(body, ref position, referenceWidth, result, ordinal);
                var valid = opcode == 0x16
                    ? (IsInteger(source) || source == 3)
                        && (IsInteger(resultType) || resultType == 3)
                    : IsInteger(source) && IsInteger(resultType) && policy switch
                    {
                        1 or 2 => true,
                        3 => BitWidth(source) == BitWidth(resultType),
                        _ => false,
                    };
                if (!valid)
                {
                    throw InvalidBody();
                }
            }
            else if (opcode is 4 or 0x17)
            {
                var kind = opcode == 4 ? ReadByte(body, ref position) : (byte)0;
                var source = Use(body, ref position, referenceWidth, result, ordinal);
                if (opcode == 4
                    ? kind switch
                    {
                        1 => source != 1,
                        2 or 3 => !IsInteger(source),
                        _ => true,
                    }
                    : source is not (8 or 9))
                {
                    throw InvalidBody();
                }

                resultType = source;
            }
            else if (opcode == 7)
            {
                var condition = Use(body, ref position, referenceWidth, result, ordinal);
                var whenTrue = Use(body, ref position, referenceWidth, result, ordinal);
                var whenFalse = Use(body, ref position, referenceWidth, result, ordinal);
                if (condition != 1 || whenTrue != whenFalse)
                {
                    throw InvalidBody();
                }

                resultType = whenTrue;
            }
            else
            {
                var kind = ReadByte(body, ref position);
                if (opcode == 0x15 ? kind is < 1 or > 7 : kind is < 1 or > 12)
                {
                    throw InvalidBody();
                }

                var left = Use(body, ref position, referenceWidth, result, ordinal);
                var right = Use(body, ref position, referenceWidth, result, ordinal);
                var shift = opcode == 0x15 && kind is 6 or 7;
                if (shift
                    ? !IsInteger(left) || right != 0x0D
                    : left != right
                        || kind switch
                        {
                            4 or 5 when opcode == 5 => false,
                            _ => !IsInteger(left),
                        })
                {
                    throw InvalidBody();
                }

                resultType = opcode == 5 && kind is >= 4 and <= 9 ? (byte)1 : left;
            }

            if ((opcode is not (1 or 2) && dataWidth != 0)
                || Array.IndexOf(mappedTypes, resultType, 1) < 0)
            {
                throw InvalidBody();
            }

            _values[result].TypeCode = resultType;
            _values[result].DefinitionOffset = offset;
        }

        var (terminator, terminatorWidth, terminatorDataWidth) = ReadInstruction(body, ref position);
        if (terminatorDataWidth != 0)
        {
            throw InvalidBody();
        }

        if (terminator == 0x83)
        {
            var result = ReadLocal(body, ref position, terminatorWidth);
            var expected = MatchType(types, mappedTypes,
                signatures.GetResultTypeSpec(signature));
            if (result == 0 || result > valueCount || _values[result].TypeCode != expected)
            {
                throw InvalidBody();
            }

            _values[result].Used = true;
            _values[result].LastUseOrdinal = operationCount;
        }
        else if (terminator == 0x87 && terminatorWidth == 1)
        {
            if (position >= body.Length || body[position++] is < 1 or > 4)
            {
                throw InvalidBody();
            }
        }
        else
        {
            throw InvalidBody();
        }

        if (position != body.Length)
        {
            throw InvalidBody();
        }
    }

    /// <summary>Body-row-relative opcode offset, or -1 for an entry parameter.</summary>
    public int GetDefinitionOffset(int value)
        => value > 0 && value <= ValueCount ? _values[value].DefinitionOffset : throw InvalidBody();

    public int GetLastUseOrdinal(int value)
        => value > 0 && value <= ValueCount ? _values[value].LastUseOrdinal : throw InvalidBody();

    public int GetUseBlockCount(int value)
        => value > 0 && value <= ValueCount ? (_values[value].Used ? 1 : 0) : throw InvalidBody();

    public byte GetTypeCode(int value)
        => value > 0 && value <= ValueCount ? _values[value].TypeCode : throw InvalidBody();

    private static byte MatchType(TrRmTypeSpecTableReader types, byte[] mappedTypes, int row)
    {
        var type = types.GetRow(row);
        if (type.Length != 3 || type[0] != 1 || type[1] != 0
            || Array.IndexOf(mappedTypes, type[2], 1) < 0)
        {
            throw InvalidBody();
        }

        return type[2];
    }

    private byte Use(ReadOnlySpan<byte> body, ref int position, int width, int result, int ordinal)
    {
        var source = ReadLocal(body, ref position, width);
        if (source == 0 || source >= result)
        {
            throw InvalidBody();
        }

        _values[source].Used = true;
        _values[source].LastUseOrdinal = ordinal;
        return _values[source].TypeCode;
    }

    private static byte ReadByte(ReadOnlySpan<byte> body, ref int position)
    {
        if (position >= body.Length)
        {
            throw InvalidBody();
        }

        return body[position++];
    }

    private static (byte Opcode, int ReferenceWidth, int DataWidth) ReadInstruction(
        ReadOnlySpan<byte> body, ref int position)
    {
        if (position >= body.Length)
        {
            throw InvalidBody();
        }

        var opcode = body[position++];
        if (opcode != 0xF0)
        {
            return (opcode, 1, opcode is 1 or 2 ? 1 : 0);
        }

        if (position + 2 > body.Length)
        {
            throw InvalidBody();
        }

        var layout = body[position++];
        if (layout == 0 || (layout & 0xCC) != 0 || (layout & 3) == 3)
        {
            throw InvalidBody();
        }

        opcode = body[position++];
        var dataWidth = opcode is 1 or 2 ? 1 << ((layout >> 4) & 3) : 0;
        if (dataWidth == 0 && (layout & 0x30) != 0)
        {
            throw InvalidBody();
        }

        return (opcode, (layout & 3) switch { 0 => 1, 1 => 2, _ => 4 }, dataWidth);
    }

    private static int ReadLocal(ReadOnlySpan<byte> body, ref int position, int width)
    {
        var value = Read(body, ref position, width);
        if (value > int.MaxValue)
        {
            throw InvalidBody();
        }

        return (int)value;
    }

    private static ulong Read(ReadOnlySpan<byte> body, ref int position, int width)
    {
        if (width > body.Length - position)
        {
            throw InvalidBody();
        }

        var bytes = body.Slice(position, width);
        position += width;
        return width switch
        {
            1 => bytes[0],
            2 => BinaryPrimitives.ReadUInt16LittleEndian(bytes),
            4 => BinaryPrimitives.ReadUInt32LittleEndian(bytes),
            _ => BinaryPrimitives.ReadUInt64LittleEndian(bytes),
        };
    }

    private static int BitWidth(byte type)
        => type switch { 1 => 1, 3 => 16, 8 or 0x0D => 32, _ => 64 };

    private static bool IsInteger(byte type)
        => type is 8 or 9 or 0x0D or 0x0E;

    private static InvalidDataException InvalidBody()
        => new("Invalid or unsupported TRRM compact-linear Body.");
}
