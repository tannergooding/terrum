// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.IO;

namespace Terrum.CodeAnalysis.Modules;

/// <summary>Validates and indexes the initial scalar compact-linear Body subset.</summary>
/// <remarks>Rejects other assigned operation schemas; it does not execute a Body or validate published effects.</remarks>
public readonly ref struct TrRmCompactLinearBodyReader
{
    // ResolveType's largest key is optional char16 (0x100 | 0x0E).
    private const int MaximumTypeKey = 0x100 | 0x0E;
    internal const int TypeKeyBitmapWordCount = (MaximumTypeKey >> 6) + 1;

    private readonly ValueInfo[] _values;
    private readonly int _valueCount;

    public int ValueCount => _valueCount;
    public int OperationCount { get; }

    private struct ValueInfo
    {
        public ushort TypeKey;
        public int DefinitionOffset;
        public int LastUseOrdinal;
        public bool Used;
    }

    public TrRmCompactLinearBodyReader(TrRmBodyTableReader bodies, int methodRow,
        TrRmModuleTableReader modules, TrRmMethodDefTableReader methods,
        TrRmMethodSigTableReader signatures, TrRmParamTableReader parameters,
        TrRmTypeSpecTableReader types)
        : this(bodies, methodRow, modules, methods, signatures, parameters, types,
            default, default)
    {
    }

    public TrRmCompactLinearBodyReader(TrRmBodyTableReader bodies, int methodRow,
        TrRmModuleTableReader modules, TrRmMethodDefTableReader methods,
        TrRmMethodSigTableReader signatures, TrRmParamTableReader parameters,
        TrRmTypeSpecTableReader types, TrRmImportTableReader imports)
        : this(bodies, methodRow, modules, methods, signatures, parameters, types,
            default, imports)
    {
    }

    public TrRmCompactLinearBodyReader(TrRmBodyTableReader bodies, int methodRow,
        TrRmModuleTableReader modules, TrRmMethodDefTableReader methods,
        TrRmMethodSigTableReader signatures, TrRmParamTableReader parameters,
        TrRmTypeSpecTableReader types, TrRmTypeDefTableReader nominalTypes,
        TrRmImportTableReader imports)
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

        var mappedTypes = new ushort[typeCount + 1];
        Span<ulong> mappedTypeKeys = stackalloc ulong[TypeKeyBitmapWordCount];
        mappedTypeKeys.Clear();
        for (var index = 1; index <= typeCount; index++)
        {
            var reference = Read(body, ref position, 4);
            if (reference == 0 || reference > (uint)types.Count)
            {
                throw InvalidBody();
            }

            var type = ResolveType(types, (int)reference);
            if (!TryAddMappedType(mappedTypeKeys, type))
            {
                throw InvalidBody();
            }

            mappedTypes[index] = type;
        }

        var (first, parameterCount) = signatures.GetParamRange(signature);
        var operationCount = ReadLocal(body, ref position, width);
        var maximum = width == 4 ? int.MaxValue : (1 << (width * 8)) - 1;
        if (parameterCount > maximum || operationCount > int.MaxValue - parameterCount - 1
            || operationCount > (body.Length - position) / 3)
        {
            throw InvalidBody();
        }

        _values = new ValueInfo[parameterCount + operationCount + 1];
        _valueCount = 0;
        OperationCount = operationCount;
        for (var index = 1; index <= parameterCount; index++)
        {
            if (parameters.GetMode(first + index - 1) != 0)
            {
                throw InvalidBody();
            }

            _values[index].TypeKey = MatchType(types, mappedTypeKeys,
                parameters.GetTypeSpec(first + index - 1));
            _values[index].DefinitionOffset = -1;
        }

        var valueCount = parameterCount;
        for (var ordinal = 0; ordinal < operationCount; ordinal++)
        {
            var offset = position;
            var (opcode, referenceWidth, countWidth, dataWidth) =
                ReadInstruction(body, ref position);
            if (opcode is not (1 or 2 or 3 or 4 or 5 or 6 or 7 or 8 or 9 or 0x0A
                or 0x12 or 0x15 or 0x16 or 0x17 or 0x18 or 0x19 or 0x1A or 0x1B or 0x1C or 0x1D or 0x1E or 0x1F))
            {
                throw InvalidBody();
            }

            if (opcode == 0x18 && _values.Length == parameterCount + operationCount + 1)
            {
                var expanded = new ValueInfo[_values.Length + operationCount];
                _values.CopyTo(expanded, 0);
                _values = expanded;
            }

            var result = ReadLocal(body, ref position, referenceWidth);
            if (opcode != 0x12 || result != 0)
            {
                if (valueCount == maximum || result != ++valueCount)
                {
                    throw InvalidBody();
                }
            }

            var secondResult = 0;
            if (opcode == 0x18)
            {
                if (valueCount == maximum)
                {
                    throw InvalidBody();
                }

                secondResult = ReadLocal(body, ref position, referenceWidth);
                if (secondResult != ++valueCount)
                {
                    throw InvalidBody();
                }
            }

            ushort resultType;
            if (opcode is 1 or 2)
            {
                var typeId = ReadLocal(body, ref position, referenceWidth);
                if (typeId == 0 || typeId > typeCount)
                {
                    throw InvalidBody();
                }

                resultType = mappedTypes[typeId];
                if (resultType > byte.MaxValue)
                {
                    throw InvalidBody();
                }

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
            else if (opcode is 8 or 9)
            {
                var typeId = ReadLocal(body, ref position, referenceWidth);
                if (typeId == 0 || typeId > typeCount || mappedTypes[typeId] < 0x100)
                {
                    throw InvalidBody();
                }

                resultType = mappedTypes[typeId];
                if (opcode == 8
                    && Use(body, ref position, referenceWidth, result, ordinal)
                        != (resultType & 0xFF))
                {
                    throw InvalidBody();
                }
            }
            else if (opcode == 0x0A)
            {
                var source = Use(body, ref position, referenceWidth, result, ordinal);
                if (source < 0x100)
                {
                    throw InvalidBody();
                }

                resultType = 1;
            }
            else if (opcode == 0x12)
            {
                var method = ReadLocal(body, ref position, referenceWidth);
                var callerModule = methods.GetOwnerModule(methodRow);
                var (firstMethod, lastMethod) = modules.GetMethodDefRange(callerModule);
                int target;
                if (method == 0)
                {
                    var definition = Read(body, ref position, 4);
                    target = (int)(definition & 0xFF_FFFF);
                    if (definition >> 24 != 0x16 || modules.Count < 2
                        || target == 0 || target > methods.Count)
                    {
                        throw InvalidBody();
                    }

                    var targetModule = methods.GetOwnerModule(target);
                    var (callerTarget, callerPolicy) = modules.GetRequirements(callerModule);
                    var (targetRequirement, targetPolicy) = modules.GetRequirements(targetModule);
                    if (targetModule == callerModule || methods.GetBodyRow(target) == 0
                        || targetPolicy != callerPolicy || targetRequirement > callerTarget)
                    {
                        throw InvalidBody();
                    }
                }
                else if (method > lastMethod - firstMethod)
                {
                    throw InvalidBody();
                }
                else
                {
                    target = firstMethod + method - 1;
                }

                var imported = methods.GetBodyRow(target) == 0;
                var targetOwner = methods.GetOwnerType(target);
                if ((method == 0 || imported)
                    && (methods.GetFlags(target) != 2
                        || targetOwner != 0 && (targetOwner > nominalTypes.Count
                            || (nominalTypes.GetFlags(targetOwner) & 3) != 2
                            || nominalTypes.GetGenericRange(targetOwner).Count != 0)))
                {
                    throw InvalidBody();
                }

                if (imported)
                {
                    if (!imports.TryGetExpectedRequirements(
                            0x1600_0000u | (uint)target, out var requirements))
                    {
                        throw InvalidBody();
                    }

                    var (callerTarget, callerPolicy) = modules.GetRequirements(callerModule);
                    var (targetRequirement, targetPolicy) = requirements;
                    if (targetPolicy != callerPolicy || targetRequirement > callerTarget)
                    {
                        throw InvalidBody();
                    }
                }

                var targetSignature = methods.GetSignature(target);
                if (signatures.GetReceiverTypeSpec(targetSignature) != 0
                    || signatures.GetGenericCount(targetSignature) != 0)
                {
                    throw InvalidBody();
                }

                var targetResult = signatures.GetResultTypeSpec(targetSignature);
                var resultRow = types.GetRow(targetResult);
                var voidResult = resultRow.Length == 3 && resultRow[0] == 1
                    && resultRow[1] == 0 && resultRow[2] == 0;
                if (voidResult ? result != 0 : result == 0)
                {
                    throw InvalidBody();
                }

                resultType = voidResult ? (ushort)0
                    : MatchType(types, mappedTypeKeys, targetResult);
                var (firstArgument, argumentCount) = signatures.GetParamRange(targetSignature);
                var arguments = ReadLocal(body, ref position, countWidth);
                if (arguments != argumentCount
                    || arguments > (body.Length - position) / referenceWidth)
                {
                    throw InvalidBody();
                }

                for (var argument = 0; argument < arguments; argument++)
                {
                    var source = Use(body, ref position, referenceWidth,
                        result == 0 ? valueCount + 1 : result, ordinal);
                    if (parameters.GetMode(firstArgument + argument) != 0
                        || source != MatchType(types, mappedTypeKeys,
                            parameters.GetTypeSpec(firstArgument + argument)))
                    {
                        throw InvalidBody();
                    }
                }
            }
            else if (opcode is 6 or 0x16 or 0x1A or 0x1B or 0x1E or 0x1F)
            {
                var typeId = ReadLocal(body, ref position, referenceWidth);
                if (typeId == 0 || typeId > typeCount)
                {
                    throw InvalidBody();
                }

                resultType = mappedTypes[typeId];
                var policy = opcode == 6 ? ReadByte(body, ref position) : (byte)0;
                var source = Use(body, ref position, referenceWidth, result, ordinal);
                var valid = opcode == 0x1A
                    ? IsFloat(source) && IsFloat(resultType) && source != resultType
                    : opcode == 0x1B
                        ? IsInteger(source) && IsFloat(resultType)
                        : opcode == 0x1F
                            ? IsFloat(source) && IsInteger(resultType)
                        : opcode == 0x1E
                            ? source == 3 && IsFloat(resultType)
                            : opcode == 0x16
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
            else if (opcode is 4 or 0x17 or 0x1C)
            {
                var kind = opcode == 4 ? ReadByte(body, ref position) : (byte)0;
                var source = Use(body, ref position, referenceWidth, result, ordinal);
                if (opcode == 0x1C
                    ? !IsFloat(source)
                    : opcode == 4
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
            else if (opcode == 0x18)
            {
                var left = Use(body, ref position, referenceWidth, result, ordinal);
                var right = Use(body, ref position, referenceWidth, result, ordinal);
                if (left != right || !IsInteger(left))
                {
                    throw InvalidBody();
                }

                resultType = left;
            }
            else if (opcode == 0x19)
            {
                var kind = ReadByte(body, ref position);
                var left = Use(body, ref position, referenceWidth, result, ordinal);
                var right = Use(body, ref position, referenceWidth, result, ordinal);
                if (kind is < 1 or > 6 || left != right || !IsFloat(left))
                {
                    throw InvalidBody();
                }

                resultType = 1;
            }
            else if (opcode == 0x1D)
            {
                var kind = ReadByte(body, ref position);
                var left = Use(body, ref position, referenceWidth, result, ordinal);
                var right = Use(body, ref position, referenceWidth, result, ordinal);
                if (kind is < 1 or > 4 || left != right || !IsFloat(left))
                {
                    throw InvalidBody();
                }

                resultType = left;
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
                            4 or 5 when opcode == 5
                                => left is not (1 or 3 or 8 or 9 or 0x0D or 0x0E)
                                    && left != 0x103,
                            _ => !IsInteger(left),
                        })
                {
                    throw InvalidBody();
                }

                resultType = opcode == 5 && kind is >= 4 and <= 9 ? (ushort)1 : left;
            }

            if ((opcode is not (1 or 2) && dataWidth != 0)
                || resultType != 0 && !IsMappedType(mappedTypeKeys, resultType))
            {
                throw InvalidBody();
            }

            if (result != 0)
            {
                _values[result].TypeKey = resultType;
                _values[result].DefinitionOffset = offset;
            }
            if (secondResult != 0)
            {
                _values[secondResult].TypeKey = resultType;
                _values[secondResult].DefinitionOffset = offset;
            }
        }

        _valueCount = valueCount;
        var (terminator, terminatorWidth, terminatorCountWidth, terminatorDataWidth) =
            ReadInstruction(body, ref position);
        if (terminatorCountWidth != 1 || terminatorDataWidth != 0)
        {
            throw InvalidBody();
        }

        if (terminator == 0x83)
        {
            var result = ReadLocal(body, ref position, terminatorWidth);
            var resultSpec = types.GetRow(signatures.GetResultTypeSpec(signature));
            if (result == 0)
            {
                if (resultSpec.Length != 3 || resultSpec[0] != 1
                    || resultSpec[1] != 0 || resultSpec[2] != 0)
                {
                    throw InvalidBody();
                }
            }
            else
            {
                var expected = MatchType(types, mappedTypeKeys,
                    signatures.GetResultTypeSpec(signature));
                if (result > valueCount || _values[result].TypeKey != expected)
                {
                    throw InvalidBody();
                }

                _values[result].Used = true;
                _values[result].LastUseOrdinal = operationCount;
            }
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
        => value > 0 && value <= ValueCount ? (byte)(_values[value].TypeKey & 0xFF) : throw InvalidBody();

    public bool IsOptional(int value)
        => value > 0 && value <= ValueCount ? _values[value].TypeKey >= 0x100 : throw InvalidBody();

    private static ushort MatchType(TrRmTypeSpecTableReader types,
        ReadOnlySpan<ulong> mappedTypeKeys, int row)
    {
        var type = ResolveType(types, row);
        if (!IsMappedType(mappedTypeKeys, type))
        {
            throw InvalidBody();
        }

        return type;
    }

    internal static bool TryAddMappedType(Span<ulong> mappedTypeKeys, ushort type)
    {
        var word = type >> 6;
        if (type > MaximumTypeKey || word >= mappedTypeKeys.Length)
        {
            return false;
        }

        var mask = 1UL << (type & 63);
        if ((mappedTypeKeys[word] & mask) != 0)
        {
            return false;
        }

        mappedTypeKeys[word] |= mask;
        return true;
    }

    internal static bool IsMappedType(ReadOnlySpan<ulong> mappedTypeKeys, ushort type)
    {
        var word = type >> 6;
        return word < mappedTypeKeys.Length
            && (mappedTypeKeys[word] & (1UL << (type & 63))) != 0;
    }

    internal static ushort ResolveType(TrRmTypeSpecTableReader types, int row)
    {
        var type = types.GetRow(row);
        if (type.Length == 6 && type[0] == 6 && type[1] == 0)
        {
            var innerRow = BinaryPrimitives.ReadUInt32LittleEndian(type[2..]);
            if (innerRow == 0 || innerRow > (uint)types.Count)
            {
                throw InvalidBody();
            }

            var inner = types.GetRow((int)innerRow);
            if (inner.Length != 3 || inner[0] != 1 || inner[1] != 0
                || !IsSupportedOptionalAtom(inner[2]))
            {
                throw InvalidBody();
            }

            return (ushort)(0x100 | inner[2]);
        }

        if (type.Length != 3 || type[0] != 1 || type[1] != 0
            || !IsSupportedAtom(type[2]))
        {
            throw InvalidBody();
        }

        return type[2];
    }

    private static bool IsSupportedAtom(byte type)
        => IsSupportedOptionalAtom(type) || type is 0x14 or 0x15;

    private static bool IsSupportedOptionalAtom(byte type)
        => type is 1 or 3 or 8 or 9 or 0x0D or 0x0E;

    private ushort Use(ReadOnlySpan<byte> body, ref int position, int width, int result, int ordinal)
    {
        var source = ReadLocal(body, ref position, width);
        if (source == 0 || source >= result)
        {
            throw InvalidBody();
        }

        _values[source].Used = true;
        _values[source].LastUseOrdinal = ordinal;
        return _values[source].TypeKey;
    }

    private static byte ReadByte(ReadOnlySpan<byte> body, ref int position)
    {
        if (position >= body.Length)
        {
            throw InvalidBody();
        }

        return body[position++];
    }

    private static (byte Opcode, int ReferenceWidth, int CountWidth, int DataWidth) ReadInstruction(
        ReadOnlySpan<byte> body, ref int position)
    {
        if (position >= body.Length)
        {
            throw InvalidBody();
        }

        var opcode = body[position++];
        if (opcode != 0xF0)
        {
            return (opcode, 1, 1, opcode is 1 or 2 ? 1 : 0);
        }

        if (position + 2 > body.Length)
        {
            throw InvalidBody();
        }

        var layout = body[position++];
        opcode = body[position++];
        if (layout == 0 || (layout & 0xC0) != 0
            || (layout & 3) == 3 || ((layout >> 2) & 3) == 3
            || opcode != 0x12 && (layout & 0x0C) != 0)
        {
            throw InvalidBody();
        }

        var dataWidth = opcode is 1 or 2 ? 1 << ((layout >> 4) & 3) : 0;
        if (dataWidth == 0 && (layout & 0x30) != 0)
        {
            throw InvalidBody();
        }

        return (opcode, (layout & 3) switch { 0 => 1, 1 => 2, _ => 4 },
            ((layout >> 2) & 3) switch { 0 => 1, 1 => 2, _ => 4 }, dataWidth);
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

    private static int BitWidth(ushort type)
        => type switch { 1 => 1, 3 => 16, 8 or 0x0D or 0x14 => 32, _ => 64 };

    private static bool IsInteger(ushort type)
        => type is 8 or 9 or 0x0D or 0x0E;

    private static bool IsFloat(ushort type)
        => type is 0x14 or 0x15;

    private static InvalidDataException InvalidBody()
        => new("Invalid or unsupported TRRM compact-linear Body.");
}
