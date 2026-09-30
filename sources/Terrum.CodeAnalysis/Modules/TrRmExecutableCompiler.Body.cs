// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Terrum.CodeAnalysis.Lexing;
using Terrum.CodeAnalysis.Semantics;

namespace Terrum.CodeAnalysis.Modules;

public static partial class TrRmExecutableCompiler
{
    private static byte[] EncodeBody(BoundFunction function)
    {
        var instructions = new List<byte>();
        var current = new int[function.Variables.Length];
        var next = function.ParameterCount;
        for (var parameter = 0; parameter < next; parameter++)
        {
            current[parameter] = parameter + 1;
        }
        int returnValue = -1;
        var ended = false;

        int EmitExpression(BoundExpression expression)
        {
            int opcode;
            int kind = 0;
            int type = 0;
            int left = 0;
            int right = 0;
            ulong data = 0;
            int dataWidth = 0;
            switch (expression)
            {
                case BoundRead read:
                    if (read.Variable < 0 || read.Variable >= current.Length
                        || current[read.Variable] == 0)
                    {
                        throw Unsupported("Read of an uninitialized scalar local.");
                    }
                    return current[read.Variable];
                case BoundConstant constant:
                    opcode = 1;
                    type = TypeRow(constant.Type) - 1;
                    data = constant.Type switch
                    {
                        PrimitiveType.Int32 or PrimitiveType.UInt32 => unchecked((uint)constant.Value),
                        PrimitiveType.Bool or PrimitiveType.Char16 => (ulong)constant.Value,
                        _ => unchecked((ulong)constant.Value),
                    };
                    dataWidth = Width(data);
                    break;
                case BoundUnary unary:
                    left = EmitExpression(unary.Operand);
                    (opcode, kind) = unary.Operator switch
                    {
                        TokenKind.Bang => (4, 1),
                        TokenKind.Tilde => (4, 2),
                        TokenKind.Plus => (4, 3),
                        TokenKind.Minus when unary.Type is PrimitiveType.Int32 or PrimitiveType.Int64 => (0x17, 0),
                        _ => throw Unsupported($"Unary operator {unary.Operator} has no executable TRRM encoding."),
                    };
                    break;
                case BoundBinary binary:
                    if (binary.Operator is TokenKind.AmpersandAmpersand or TokenKind.PipePipe
                        or TokenKind.ShiftLeft or TokenKind.ShiftRight or TokenKind.ShiftRightUnsigned)
                    {
                        throw Unsupported($"Lazy or shift operator {binary.Operator} is outside the linear TRRM profile.");
                    }
                    if (binary.Operator is TokenKind.Ampersand or TokenKind.Pipe or TokenKind.Caret
                        && binary.Left.Type == PrimitiveType.Bool
                        || binary.Operator is TokenKind.Less or TokenKind.LessEquals
                            or TokenKind.Greater or TokenKind.GreaterEquals
                            && binary.Left.Type == PrimitiveType.Char16)
                    {
                        throw Unsupported("Boolean bitwise and char16 ordering have no executable TRRM scalar operation.");
                    }
                    left = EmitExpression(binary.Left);
                    right = EmitExpression(binary.Right);
                    (opcode, kind) = binary.Operator switch
                    {
                        TokenKind.Ampersand => (5, 1),
                        TokenKind.Pipe => (5, 2),
                        TokenKind.Caret => (5, 3),
                        TokenKind.EqualsEquals => (5, 4),
                        TokenKind.BangEquals => (5, 5),
                        TokenKind.Less => (5, 6),
                        TokenKind.LessEquals => (5, 7),
                        TokenKind.Greater => (5, 8),
                        TokenKind.GreaterEquals => (5, 9),
                        TokenKind.Plus => (0x15, 1),
                        TokenKind.Minus => (0x15, 2),
                        TokenKind.Star => (0x15, 3),
                        TokenKind.Slash => (0x15, 4),
                        TokenKind.Percent => (0x15, 5),
                        _ => throw Unsupported($"Binary operator {binary.Operator} has no executable TRRM encoding."),
                    };
                    break;
                case BoundCast cast:
                    left = EmitExpression(cast.Operand);
                    opcode = 0x16;
                    type = TypeRow(cast.Type) - 1;
                    break;
                default:
                    throw Unsupported($"Expression {expression.GetType().Name} is outside the linear TRRM profile.");
            }

            if (++next > SemanticVerifier.MaximumVariables)
            {
                throw Unsupported("Native verification supports at most 256 materialized scalar values per method.");
            }
            var referenceWidth = Width((ulong)Math.Max(next, type));
            WriteInstruction(instructions, opcode, referenceWidth, dataWidth);
            Write(instructions, (ulong)next, referenceWidth);
            if (opcode is 1 or 0x16)
            {
                Write(instructions, (ulong)type, referenceWidth);
            }
            if (opcode is 4 or 5 or 0x15)
            {
                instructions.Add((byte)kind);
            }
            if (opcode == 1)
            {
                Write(instructions, data, dataWidth);
            }
            if (opcode is 4 or 0x16 or 0x17)
            {
                Write(instructions, (ulong)left, referenceWidth);
            }
            if (opcode is 5 or 0x15)
            {
                Write(instructions, (ulong)left, referenceWidth);
                Write(instructions, (ulong)right, referenceWidth);
            }
            if (function.IsNoThrow && opcode is 0x15 or 0x16 or 0x17)
            {
                throw Unsupported("NoThrow requires a total Body, not a source-only constant proof.");
            }
            return next;
        }

        void EmitStatement(BoundStatement statement)
        {
            if (ended)
            {
                // The binder appends a void return even after an authored return.
                if (function.ReturnType == PrimitiveType.Void && statement is BoundReturn { Value: null })
                {
                    return;
                }
                throw Unsupported("Statements after the terminal return are not linear.");
            }
            switch (statement)
            {
                case BoundBlock block when block.RetiredManagedLocals.IsEmpty:
                    foreach (var child in block.Statements)
                    {
                        EmitStatement(child);
                    }
                    break;
                case BoundLocal local:
                    current[local.Variable] = EmitExpression(local.Initializer);
                    break;
                case BoundAssignment assignment:
                    current[assignment.Variable] = EmitExpression(assignment.Value);
                    break;
                case BoundExpressionStatement discarded:
                    _ = EmitExpression(discarded.Expression);
                    break;
                case BoundReturn result:
                    returnValue = result.Value is null ? 0 : EmitExpression(result.Value);
                    ended = true;
                    break;
                default:
                    throw Unsupported($"Statement {statement.GetType().Name} is outside the linear TRRM profile.");
            }
        }

        EmitStatement(function.Body);
        if (!ended)
        {
            throw Unsupported("A linear Body requires a terminal return.");
        }
        var localWidth = Width((ulong)Math.Max(next, 6));
        var body = new List<byte> { (byte)(0x04 | WidthCode(localWidth)) };
        Write(body, 6, localWidth);
        for (var row = 2; row <= 7; row++)
        {
            Write(body, (ulong)row, 4);
        }
        Write(body, (ulong)(next - function.ParameterCount), localWidth);
        body.AddRange(instructions);
        var terminatorWidth = Width((ulong)returnValue);
        WriteInstruction(body, 0x83, terminatorWidth, 0);
        Write(body, (ulong)returnValue, terminatorWidth);
        return body.ToArray();
    }

    private static ImmutableArray<BoundStatement> DecodeBody(ReadOnlySpan<byte> body,
        int parameterCount, ImmutableArray<BoundVariable>.Builder variables, bool noThrow,
        TrRmTypeSpecTableReader types, ImmutableArray<BoundFunction>.Builder functions)
    {
        var position = 0;
        var header = ReadByte(body, ref position);
        var localWidth = 1 << (header & 3);
        var typeCount = ReadInt(body, ref position, localWidth);
        if (typeCount is < 0 or > 6)
        {
            throw Invalid("Body type map exceeds the scalar profile.");
        }
        var mappedTypes = new PrimitiveType[typeCount + 1];
        for (var id = 1; id <= typeCount; id++)
        {
            var type = FromRow(types, ReadInt(body, ref position, 4));
            if (type == PrimitiveType.Void || Array.IndexOf(mappedTypes, type, 1, id - 1) >= 0)
            {
                throw Invalid("Void or duplicate scalar in Body type map.");
            }
            mappedTypes[id] = type;
        }
        var count = ReadInt(body, ref position, localWidth);
        if (count > SemanticVerifier.MaximumAnalysisVisits)
        {
            throw Invalid("Body operation inventory exceeds verification bounds.");
        }
        // A void call consumes an operation ordinal, not a value identifier.
        // The structural reader has already checked every definition and use.
        var nextValue = parameterCount;
        var statements = ImmutableArray.CreateBuilder<BoundStatement>();
        for (var ordinal = 0; ordinal < count; ordinal++)
        {
            var (opcode, width, countWidth, dataWidth) = ReadInstruction(body, ref position);
            if (opcode is not (1 or 2 or 3 or 4 or 5 or 6 or 0x12 or 0x15 or 0x16 or 0x17))
            {
                throw Invalid($"Operation 0x{opcode:X2} is not executable.");
            }
            var result = ReadInt(body, ref position, width);
            if (result != 0 && result != ++nextValue || result == 0 && opcode != 0x12
                || nextValue > SemanticVerifier.MaximumVariables)
            {
                throw Invalid("Noncontiguous value definitions.");
            }
            var typeId = opcode is 1 or 2 or 6 or 0x16 ? ReadInt(body, ref position, width) : 0;
            var policy = opcode == 6 ? ReadByte(body, ref position) : (byte)0;
            var kind = opcode is 4 or 5 or 0x15 ? ReadByte(body, ref position) : (byte)0;
            var bits = opcode is 1 or 2 ? ReadBits(body, ref position, dataWidth) : 0;
            var left = opcode is 3 or 4 or 6 or 0x16 or 0x17 or 5 or 0x15
                ? ReadInt(body, ref position, width) : 0;
            var right = opcode is 5 or 0x15 ? ReadInt(body, ref position, width) : 0;
            var method = opcode == 0x12 ? ReadInt(body, ref position, width) : 0;
            if (opcode == 0x12 && (method == 0 || method > functions.Count))
            {
                throw Invalid("Call target is not a module-local definition.");
            }
            var argumentCount = opcode == 0x12 ? ReadInt(body, ref position, countWidth) : 0;
            var arguments = ImmutableArray.CreateBuilder<BoundExpression>(argumentCount);
            for (var argument = 0; argument < argumentCount; argument++)
            {
                var id = ReadInt(body, ref position, width);
                if (id <= 0 || id > variables.Count)
                {
                    throw Invalid("Undefined call operand.");
                }
                arguments.Add(new BoundRead(id - 1, variables[id - 1].Type, default));
            }
            var maximum = Math.Max(Math.Max(Math.Max(result, typeId), method), Math.Max(left, right));
            foreach (var argument in arguments)
            {
                maximum = Math.Max(maximum, ((BoundRead)argument).Variable + 1);
            }
            if (width != Width((ulong)maximum) || countWidth != Width((ulong)argumentCount)
                || opcode == 1 && dataWidth != Width(bits)
                || opcode == 2 && dataWidth != SignedWidth(
                    dataWidth == 8 ? unchecked((long)bits)
                        : unchecked((long)(bits << (64 - dataWidth * 8))) >> (64 - dataWidth * 8)))
            {
                throw Invalid("Nonminimal instruction width.");
            }
            if (noThrow && opcode is 0x15 or 0x16 or 0x17)
            {
                throw Invalid("NoThrow cannot rely on source-only constant proofs for checked operations.");
            }
            BoundRead Operand(int id)
            {
                if (id <= 0 || id > variables.Count)
                {
                    throw Invalid("Undefined operation operand.");
                }
                return new BoundRead(id - 1, variables[id - 1].Type, default);
            }
            BoundExpression expression;
            if (opcode is 1 or 2)
            {
                if (typeId <= 0 || typeId > typeCount)
                {
                    throw Invalid("Body-local type is not mapped.");
                }
                var type = mappedTypes[typeId];
                var signed = dataWidth == 8 ? unchecked((long)bits)
                    : unchecked((long)(bits << (64 - dataWidth * 8))) >> (64 - dataWidth * 8);
                var value = opcode == 2 ? signed : unchecked((long)bits);
                if (type is PrimitiveType.Int32 or PrimitiveType.UInt32)
                {
                    value = unchecked((int)value);
                }
                expression = new BoundConstant(value, type, default);
            }
            else if (opcode == 0x12)
            {
                var target = functions[method - 1];
                if (target.ReturnType == PrimitiveType.Void != (result == 0)
                    || target.ParameterCount != argumentCount
                    || noThrow && !target.IsNoThrow)
                {
                    throw Invalid("Call signature or effect does not match.");
                }
                for (var argument = 0; argument < argumentCount; argument++)
                {
                    if (target.Variables[argument].Type != arguments[argument].Type
                        || target.Variables[argument].IsOut || target.Variables[argument].IsInit)
                    {
                        throw Invalid("Call argument type or mode does not match.");
                    }
                }
                expression = new BoundCall(method - 1, arguments.MoveToImmutable(), target.ReturnType, default);
            }
            else if (opcode == 3)
            {
                expression = Operand(left);
            }
            else if (opcode == 6)
            {
                if (typeId <= 0 || typeId > typeCount || policy != 3
                    || !CanReinterpret(Operand(left).Type, mappedTypes[typeId]))
                {
                    throw Invalid("Unsupported scalar representation conversion.");
                }
                expression = new TrRmScalarReinterpret(Operand(left), mappedTypes[typeId]);
            }
            else if (opcode == 0x16)
            {
                if (typeId <= 0 || typeId > typeCount)
                {
                    throw Invalid("Conversion target is not mapped.");
                }
                expression = new BoundCast(Operand(left), mappedTypes[typeId], default);
            }
            else if (opcode == 0x17)
            {
                var operand = Operand(left);
                expression = new BoundUnary(TokenKind.Minus, operand, operand.Type, default);
            }
            else if (opcode == 4)
            {
                var operand = Operand(left);
                var token = kind switch
                {
                    1 => TokenKind.Bang, 2 => TokenKind.Tilde, 3 => TokenKind.Plus,
                    _ => throw Invalid("Unsupported unary kind."),
                };
                expression = new BoundUnary(token, operand, operand.Type, default);
            }
            else
            {
                var first = Operand(left);
                var second = Operand(right);
                var token = (opcode, kind) switch
                {
                    (5, 1) => TokenKind.Ampersand, (5, 2) => TokenKind.Pipe,
                    (5, 3) => TokenKind.Caret, (5, 4) => TokenKind.EqualsEquals,
                    (5, 5) => TokenKind.BangEquals, (5, 6) => TokenKind.Less,
                    (5, 7) => TokenKind.LessEquals, (5, 8) => TokenKind.Greater,
                    (5, 9) => TokenKind.GreaterEquals,
                    (0x15, 1) => TokenKind.Plus, (0x15, 2) => TokenKind.Minus,
                    (0x15, 3) => TokenKind.Star, (0x15, 4) => TokenKind.Slash,
                    (0x15, 5) => TokenKind.Percent,
                    (0x15, 6) => TokenKind.ShiftLeft,
                    (0x15, 7) => TokenKind.ShiftRight,
                    _ => throw Invalid("Unsupported binary kind."),
                };
                if (opcode == 0x15 && kind is 6 or 7
                    && (first.Type is not (PrimitiveType.Int32 or PrimitiveType.UInt32
                        or PrimitiveType.Int64 or PrimitiveType.UInt64)
                        || second.Type != PrimitiveType.UInt32))
                {
                    throw Invalid("Checked shift requires an integer and a uint32 count.");
                }
                var resultType = opcode == 5 && kind is >= 4 and <= 9
                    ? PrimitiveType.Bool : first.Type;
                expression = new BoundBinary(token, first, second, resultType, default);
            }
            if (result == 0)
            {
                statements.Add(new BoundExpressionStatement(expression, default));
            }
            else
            {
                variables.Add(new BoundVariable($"value{result}", expression.Type, false, default));
                statements.Add(new BoundLocal(result - 1, expression, default));
            }
        }
        if (localWidth != Width((ulong)Math.Max(Math.Max(nextValue, count), typeCount)))
        {
            throw Invalid("Nonminimal Body-local width.");
        }
        var (terminator, returnWidth, returnCountWidth, returnDataWidth) = ReadInstruction(body, ref position);
        if (terminator != 0x83 || returnDataWidth != 0 || returnCountWidth != 1)
        {
            throw Invalid("Only terminal Return is supported.");
        }
        var returned = ReadInt(body, ref position, returnWidth);
        if (returnWidth != Width((ulong)returned) || position != body.Length
            || returned > variables.Count)
        {
            throw Invalid("Invalid or noncanonical terminal Return.");
        }
        statements.Add(new BoundReturn(returned == 0 ? null
            : new BoundRead(returned - 1, variables[returned - 1].Type, default), default));
        return statements.ToImmutable();
    }

    private static int Width(ulong bits) => bits <= byte.MaxValue ? 1
        : bits <= ushort.MaxValue ? 2 : bits <= uint.MaxValue ? 4 : 8;
    private static int SignedWidth(long value) => value is >= sbyte.MinValue and <= sbyte.MaxValue ? 1
        : value is >= short.MinValue and <= short.MaxValue ? 2
        : value is >= int.MinValue and <= int.MaxValue ? 4 : 8;
    private static int WidthCode(int width) => width switch { 1 => 0, 2 => 1, 4 => 2, _ => 3 };
    private static void WriteInstruction(List<byte> bytes, int opcode, int width, int dataWidth)
    {
        if (width == 1 && dataWidth is 0 or 1)
        {
            bytes.Add((byte)opcode);
        }
        else
        {
            bytes.Add(0xF0);
            bytes.Add((byte)(WidthCode(width) | (dataWidth == 0 ? 0 : WidthCode(dataWidth) << 4)));
            bytes.Add((byte)opcode);
        }
    }
    private static void Write(List<byte> bytes, ulong value, int width)
    {
        for (var shift = 0; shift < width * 8; shift += 8)
        {
            bytes.Add((byte)(value >> shift));
        }
    }
    private static byte ReadByte(ReadOnlySpan<byte> bytes, ref int position)
    {
        if (position >= bytes.Length)
        {
            throw Invalid("Truncated Body.");
        }
        return bytes[position++];
    }
    private static ulong ReadBits(ReadOnlySpan<byte> bytes, ref int position, int width)
    {
        if (width > bytes.Length - position)
        {
            throw Invalid("Truncated Body operand.");
        }
        ulong bits = 0;
        for (var shift = 0; shift < width * 8; shift += 8)
        {
            bits |= (ulong)bytes[position++] << shift;
        }
        return bits;
    }
    private static int ReadInt(ReadOnlySpan<byte> bytes, ref int position, int width)
    {
        var bits = ReadBits(bytes, ref position, width);
        if (bits > int.MaxValue)
        {
            throw Invalid("Body-local index exceeds host bounds.");
        }
        return (int)bits;
    }
    private static (byte Opcode, int Width, int CountWidth, int DataWidth) ReadInstruction(ReadOnlySpan<byte> bytes, ref int position)
    {
        var opcode = ReadByte(bytes, ref position);
        if (opcode != 0xF0)
        {
            return (opcode, 1, 1, opcode is 1 or 2 ? 1 : 0);
        }
        var layout = ReadByte(bytes, ref position);
        opcode = ReadByte(bytes, ref position);
        if (layout == 0 || (layout & 0xC0) != 0 || (layout & 3) == 3
            || ((layout >> 2) & 3) == 3 || (layout & 0x0C) != 0 && opcode != 0x12
            || (layout & 0x30) != 0 && opcode is not (1 or 2))
        {
            throw Invalid("Unsupported wide instruction layout.");
        }
        var width = (layout & 3) switch { 0 => 1, 1 => 2, 2 => 4, _ => throw Invalid("Reserved reference width.") };
        var dataWidth = opcode is 1 or 2 ? 1 << ((layout >> 4) & 3) : 0;
        return (opcode, width, 1 << ((layout >> 2) & 3), dataWidth);
    }
}
