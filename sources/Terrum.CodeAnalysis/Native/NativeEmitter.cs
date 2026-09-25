// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using Terrum.CodeAnalysis.Lexing;
using Terrum.CodeAnalysis.Semantics;

namespace Terrum.CodeAnalysis.Native;

/// <summary>Behavior on a checked runtime failure in a native executable.</summary>
public enum NativeRuntimePolicy
{
    /// <summary>Terminate with 0xE0540001 for overflow, 0xE0540002 for zero division, 0xE0540003 for failed requirements, or 0xE0540004 for buffer bounds.</summary>
    FailFast,
}

/// <summary>Emits an owned, deterministic, Windows x64 PE32+ executable from structured IR.</summary>
public static class NativeEmitter
{
    /// <summary>Verifies and emits a complete executable image without accessing the filesystem.</summary>
    /// <exception cref="ArgumentException">The IR is invalid.</exception>
    /// <exception cref="NotSupportedException">The runtime policy or an IR, frame, or image resource limit is unsupported.</exception>
    public static byte[] Emit(BoundProgram program, NativeRuntimePolicy runtimePolicy, CancellationToken cancellationToken = default)
    {
        var code = EmitCode(program, runtimePolicy, cancellationToken);
        return PeImage.Build(code);
    }

    /// <summary>Verifies and emits relocatable code with a callable int32(void) entry point.</summary>
    public static NativeCode EmitCode(BoundProgram program, NativeRuntimePolicy runtimePolicy, CancellationToken cancellationToken = default)
    {
        if (runtimePolicy != NativeRuntimePolicy.FailFast)
        {
            throw new NotSupportedException("Unsupported native runtime policy.");
        }
        cancellationToken.ThrowIfCancellationRequested();
        SemanticVerifier.Verify(program);
        return new PeCompiler(program, cancellationToken).Compile();
    }
}

internal sealed class PeCompiler
{
    // A sub-4 KiB frame cannot cross more than one guard page in its single allocation.
    // 32 bytes of outgoing home space and all outgoing stack arguments stay at the bottom of each frame.
    private const int MaximumFrame = 0xF00;
    private const int MaximumText = 8 * 1024 * 1024;
    private readonly BoundProgram _program;
    private readonly CancellationToken _cancellationToken;
    private readonly MachineCode _code = new();
    private readonly int[] _starts;
    private readonly int[] _ends;
    private readonly int[] _frames;
    private readonly int[] _outgoing;
    private readonly List<(int Offset, int Target)> _calls = new();
    private readonly List<(int Offset, int Target)> _runtimeCalls = new();
    private readonly List<int> _imports = new();
    private readonly List<NativeDataFixup> _dataFixups = [];
    private readonly int[] _bufferOffsets;
    private readonly byte[] _data;
    private readonly int _readOnlyDataLength;
    private readonly int[] _failureStarts = new int[4];
    private readonly int[] _failureEnds = new int[4];
    private int _entryStart;
    private int _entryEnd;
    private int _frame;
    private int _outgoingSize;
    private int _slotCount;
    private int _spillBase;
    private int _outBase;
    private int _currentFunction;

    internal PeCompiler(BoundProgram program, CancellationToken cancellationToken)
    {
        _program = program;
        _cancellationToken = cancellationToken;
        _starts = new int[program.Functions.Length];
        _ends = new int[program.Functions.Length];
        _frames = new int[program.Functions.Length];
        _outgoing = new int[program.Functions.Length];
        _bufferOffsets = new int[program.Buffers.Length];
        var readOnlyBytes = 0;
        foreach (var buffer in program.Buffers)
        {
            if (!buffer.IsWritable)
            {
                readOnlyBytes = checked(readOnlyBytes + (buffer.Values.Length * 4));
            }
        }
        _readOnlyDataLength = readOnlyBytes;
        var readOnlyAt = 0;
        var writableAt = readOnlyBytes;
        for (var index = 0; index < program.Buffers.Length; index++)
        {
            var buffer = program.Buffers[index];
            _bufferOffsets[index] = buffer.IsWritable ? writableAt : readOnlyAt;
            if (buffer.IsWritable)
            {
                writableAt = checked(writableAt + (buffer.Values.Length * 4));
            }
            else
            {
                readOnlyAt = checked(readOnlyAt + (buffer.Values.Length * 4));
            }
        }
        _data = new byte[writableAt];
        for (var index = 0; index < program.Buffers.Length; index++)
        {
            var buffer = program.Buffers[index];
            for (var element = 0; element < buffer.Values.Length; element++)
            {
                System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(
                    _data.AsSpan(_bufferOffsets[index] + (element * 4), 4), buffer.Values[element]);
            }
        }
    }

    internal NativeCode Compile()
    {
        for (var i = 0; i < _program.Functions.Length; i++)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            var function = _program.Functions[i];
            var maximumArguments = 0;
            var spillSlots = StatementSpills(function.Body, ref maximumArguments);
            var outParameters = 0;
            foreach (var variable in function.Variables)
            {
                if (variable.IsOut)
                {
                    outParameters++;
                }
            }
            var outgoingSize = 32 + (8 * Math.Max(0, maximumArguments - 4));
            var required = outgoingSize + (8L * (function.Variables.Length + spillSlots + outParameters));
            if (required > MaximumFrame)
            {
                throw new NotSupportedException("Native frame exceeds the 3840-byte page-safe ceiling.");
            }
            var frame = AlignFrame((int)required);
            if (frame > MaximumFrame)
            {
                throw new NotSupportedException("Native frame exceeds the 3840-byte page-safe ceiling.");
            }
            _outgoing[i] = outgoingSize;
            _frames[i] = frame;
        }

        _entryStart = _code.Position;
        Prolog(40);
        _calls.Add((_code.RelativeCall(), _program.EntryPoint));
        if (_program.Functions[_program.EntryPoint].ReturnType == PrimitiveType.Void)
        {
            _code.Bytes(0x31, 0xC0); // xor eax,eax
        }
        _frame = 40;
        Epilog();
        _entryEnd = _code.Position;

        var processEntry = _code.Position;
        Prolog(40);
        _code.PatchRelative(_code.RelativeCall(), _entryStart);
        _code.Bytes(0x89, 0xC1); // mov ecx,eax
        _imports.Add(_code.ImportCall());
        _code.Byte(0xCC);
        var processEnd = _code.Position;

        for (var failure = 0; failure < _failureStarts.Length; failure++)
        {
            _failureStarts[failure] = _code.Position;
            Prolog(40);
            _code.Byte(0xB9);
            _code.Int32(unchecked((int)0xE0540001) + failure);
            _imports.Add(_code.ImportCall());
            _code.Byte(0xCC);
            _failureEnds[failure] = _code.Position;
        }

        for (var i = 0; i < _program.Functions.Length; i++)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            var function = _program.Functions[i];
            _starts[i] = _code.Position;
            _currentFunction = i;
            _frame = _frames[i];
            _outgoingSize = _outgoing[i];
            _slotCount = function.Variables.Length;
            _spillBase = _outgoingSize + (8 * _slotCount);
            var ignoredArguments = 0;
            _outBase = _spillBase + (8 * StatementSpills(function.Body, ref ignoredArguments));
            Prolog(_frame);
            if (function.IsConstructor)
            {
                _code.Bytes(0x31, 0xC0);
                _code.Store64(Slot(0));
            }
            for (var argument = 0; argument < function.ParameterCount; argument++)
            {
                var slot = argument + (function.IsConstructor ? 1 : 0);
                if (argument < 4)
                {
                    _code.StoreRegister(argument, Slot(slot),
                        IsStruct(function.Variables[slot].Type) || function.Variables[slot].IsOut);
                }
                else
                {
                    // At entry [rsp] is the return address, [rsp+8..39] is caller home space.
                    _code.Load64(_frame + 40 + (8 * (argument - 4)));
                    _code.Store64(Slot(slot));
                }
                if (!function.Variables[slot].IsOut && function.Variables[slot].Type is PrimitiveType.Bool or PrimitiveType.Char16)
                {
                    _code.Load(Slot(slot));
                    _code.Bytes(0x0F, function.Variables[slot].Type == PrimitiveType.Bool ? (byte)0xB6 : (byte)0xB7, 0xC0);
                    _code.Store(Slot(slot));
                }
            }
            Statement(function.Body);
            if (function.ReturnType == PrimitiveType.Void)
            {
                Epilog();
            }
            _ends[i] = _code.Position;
            if (_code.Position > MaximumText)
            {
                throw new NotSupportedException("Native text exceeds the 8 MiB image ceiling.");
            }
        }

        foreach (var (offset, target) in _calls)
        {
            _code.PatchRelative(offset, _starts[target]);
        }
        foreach (var (offset, target) in _runtimeCalls)
        {
            _code.PatchRelative(offset, _failureStarts[target]);
        }
        var functions = new List<(int Start, int End, int Frame)>
        {
            (_entryStart, _entryEnd, 40),
            (processEntry, processEnd, 40),
        };
        for (var i = 0; i < _failureStarts.Length; i++)
        {
            functions.Add((_failureStarts[i], _failureEnds[i], 40));
        }
        for (var i = 0; i < _starts.Length; i++)
        {
            functions.Add((_starts[i], _ends[i], _frames[i]));
        }

        return new NativeCode(_code.ToArray(), _entryStart,
            [.. functions.ConvertAll(static range => new NativeFunctionRange(range.Start, range.End, range.Frame))],
            [.. _imports]) {
            AotEntryPoint = processEntry,
            Data = _data,
            ReadOnlyDataLength = _readOnlyDataLength,
            BufferOffsets = [.. _bufferOffsets],
            DataFixups = [.. _dataFixups],
        };
    }

    private static int AlignFrame(int bytes) => ((bytes + 8 + 15) & ~15) - 8;

    private static int StatementSpills(BoundStatement statement, ref int maximumArguments)
    {
        switch (statement)
        {
            case BoundBlock block:
                var maximum = 0;
                foreach (var child in block.Statements)
                {
                    maximum = Math.Max(maximum, StatementSpills(child, ref maximumArguments));
                }
                return maximum;
            case BoundLocal local:
                return ExpressionSpills(local.Initializer, ref maximumArguments);
            case BoundAssignment assignment:
                return ExpressionSpills(assignment.Value, ref maximumArguments);
            case BoundFieldAssignment assignment:
                return ExpressionSpills(assignment.Value, ref maximumArguments);
            case BoundBufferAssignment assignment:
                return Math.Max(ExpressionSpills(assignment.Index, ref maximumArguments),
                    1 + ExpressionSpills(assignment.Value, ref maximumArguments));
            case BoundExpressionStatement expression:
                return ExpressionSpills(expression.Expression, ref maximumArguments);
            case BoundIf conditional:
                return Math.Max(ExpressionSpills(conditional.Condition, ref maximumArguments),
                    Math.Max(StatementSpills(conditional.Then, ref maximumArguments),
                        conditional.Else is null ? 0 : StatementSpills(conditional.Else, ref maximumArguments)));
            case BoundWhile loop:
                return Math.Max(ExpressionSpills(loop.Condition, ref maximumArguments),
                    StatementSpills(loop.Body, ref maximumArguments));
            case BoundRequire requirement:
                return ExpressionSpills(requirement.Condition, ref maximumArguments);
            case BoundReturn result when result.Value is BoundExpression value:
                return ExpressionSpills(value, ref maximumArguments);
            default:
                return 0;
        }
    }

    private static int ExpressionSpills(BoundExpression expression, ref int maximumArguments)
    {
        switch (expression)
        {
            case BoundUnary unary:
                return ExpressionSpills(unary.Operand, ref maximumArguments);
            case BoundCast cast:
                return ExpressionSpills(cast.Operand, ref maximumArguments);
            case BoundFieldRead field:
                return ExpressionSpills(field.Receiver, ref maximumArguments);
            case BoundBufferRead buffer:
                return ExpressionSpills(buffer.Index, ref maximumArguments);
            case BoundBinary binary:
                return Math.Max(ExpressionSpills(binary.Left, ref maximumArguments),
                    1 + ExpressionSpills(binary.Right, ref maximumArguments));
            case BoundCall call:
                maximumArguments = Math.Max(maximumArguments, call.Arguments.Length);
                var maximum = call.Arguments.Length * 2;
                for (var i = 0; i < call.Arguments.Length; i++)
                {
                    if (call.Arguments[i] is not BoundOutArgument)
                    {
                        maximum = Math.Max(maximum, (call.Arguments.Length * 2)
                            + ExpressionSpills(call.Arguments[i], ref maximumArguments));
                    }
                }
                return maximum;
            default:
                return 0;
        }
    }

    private int Slot(int index) => _outgoingSize + (index * 8);
    private int Spill(int index) => _spillBase + (index * 8);
    private int OutSlot(int index)
    {
        var ordinal = 0;
        for (var i = 0; i < index; i++)
        {
            ordinal += _program.Functions[_currentFunction].Variables[i].IsOut ? 1 : 0;
        }
        return _outBase + (8 * ordinal);
    }
    private static bool IsStruct(PrimitiveType type) => (int)type >= 100;
    private void StoreValue(int offset, PrimitiveType type)
    {
        if (IsStruct(type))
        {
            _code.Store64(offset);
        }
        else
        {
            _code.Store(offset);
        }
    }
    private void LoadValue(int offset, PrimitiveType type)
    {
        if (IsStruct(type))
        {
            _code.Load64(offset);
        }
        else
        {
            _code.Load(offset);
        }
    }

    private void Prolog(int frame)
    {
        // Entry rsp is 8 mod 16. Subtracting 8 mod 16 aligns rsp at each call site.
        _code.Bytes(0x48, 0x81, 0xEC);
        _code.Int32(frame);
    }

    private void Epilog()
    {
        _code.Bytes(0x48, 0x81, 0xC4);
        _code.Int32(_frame);
        _code.Byte(0xC3);
    }

    private void RuntimeBranch(byte condition, int target)
    {
        // Transfer through a call, not a jump: the helper needs the ordinary
        // Windows entry alignment (rsp == 8 mod 16) and a valid unwind chain.
        var skip = _code.JumpIf((byte)(condition ^ 1));
        _runtimeCalls.Add((_code.RelativeCall(), target));
        _code.PatchRelative(skip, _code.Position);
    }

    [SuppressMessage("Style", "IDE0010:Populate switch", Justification = "The verifier excludes other structured statement variants.")]
    private void Statement(BoundStatement statement)
    {
        _cancellationToken.ThrowIfCancellationRequested();
        switch (statement)
        {
            case BoundRequire requirement:
                Expression(requirement.Condition, 0);
                _code.Bytes(0x85, 0xC0);
                RuntimeBranch(0x84, 2);
                break;
            case BoundBlock block:
                foreach (var child in block.Statements)
                {
                    Statement(child);
                }
                break;
            case BoundLocal local:
                Expression(local.Initializer, 0);
                StoreValue(Slot(local.Variable), local.Initializer.Type);
                break;
            case BoundAssignment assignment:
                Expression(assignment.Value, 0);
                if (_program.Functions[_currentFunction].Variables[assignment.Variable].IsOut)
                {
                    _code.Store64(OutSlot(assignment.Variable));
                }
                else
                {
                    StoreValue(Slot(assignment.Variable), assignment.Value.Type);
                }
                break;
            case BoundFieldAssignment fieldAssignment:
                Expression(fieldAssignment.Value, 0);
                var variable = _program.Functions[_currentFunction].Variables[fieldAssignment.Variable];
                var fieldType = variable.Type;
                var field = _program.Structures[(int)fieldType - 100].Fields[fieldAssignment.Field];
                _code.StoreField((variable.IsOut ? OutSlot(fieldAssignment.Variable) : Slot(fieldAssignment.Variable)) + field.Offset,
                    field.Size);
                break;
            case BoundBufferAssignment bufferWrite:
                Expression(bufferWrite.Index, 0);
                CheckBufferIndex(bufferWrite.Buffer);
                _code.Store(Spill(0));
                Expression(bufferWrite.Value, 1);
                _code.Bytes(0x89, 0xC1); // mov ecx,eax
                _code.Load(Spill(0));
                _dataFixups.Add(new NativeDataFixup(_code.DataAddress(), _bufferOffsets[bufferWrite.Buffer]));
                _code.Bytes(0x89, 0x0C, 0x82); // mov [rdx+rax*4],ecx
                break;
            case BoundExpressionStatement expression:
                Expression(expression.Expression, 0);
                break;
            case BoundReturn result:
                if (result.Value is not null)
                {
                    Expression(result.Value, 0);
                }
                if (_program.Functions[_currentFunction].ReturnType == PrimitiveType.Bool)
                {
                    CommitOutParameters();
                }
                Epilog();
                break;
            case BoundIf conditional:
                Expression(conditional.Condition, 0);
                _code.Bytes(0x85, 0xC0);
                var falseBranch = _code.JumpIf(0x84);
                Statement(conditional.Then);
                if (conditional.Else is not null)
                {
                    var end = _code.Jump();
                    _code.PatchRelative(falseBranch, _code.Position);
                    Statement(conditional.Else);
                    _code.PatchRelative(end, _code.Position);
                }

                else
                {
                    _code.PatchRelative(falseBranch, _code.Position);
                }
                break;
            case BoundWhile loop:
                var start = _code.Position;
                Expression(loop.Condition, 0);
                _code.Bytes(0x85, 0xC0);
                var exit = _code.JumpIf(0x84);
                Statement(loop.Body);
                _code.PatchRelative(_code.Jump(), start);
                _code.PatchRelative(exit, _code.Position);
                break;
        }
    }

    [SuppressMessage("Style", "IDE0072:Populate switch", Justification = "The verifier rejects void output parameters; struct types use the layout fallback.")]
    private void CommitOutParameters()
    {
        var variables = _program.Functions[_currentFunction].Variables;
        var hasOut = false;
        foreach (var variable in variables)
        {
            hasOut |= variable.IsOut;
        }

        if (!hasOut)
        {
            return;
        }
        _code.Bytes(0x85, 0xC0);
        var skip = _code.JumpIf(0x84);
        for (var i = 0; i < variables.Length; i++)
        {
            if (!variables[i].IsOut)
            {
                continue;
            }
            _code.Load64(Slot(i));
            _code.Bytes(0x48, 0x89, 0xC2); // mov rdx,rax
            _code.Load64(OutSlot(i));
            var size = variables[i].Type switch {
                PrimitiveType.Bool => 1,
                PrimitiveType.Char16 => 2,
                PrimitiveType.Int32 or PrimitiveType.UInt32 => 4,
                _ => _program.Structures[(int)variables[i].Type - 100].Size,
            };
            if (size == 1)
            {
                _code.Bytes(0x88, 0x02);
            }
            else if (size == 2)
            {
                _code.Bytes(0x66, 0x89, 0x02);
            }
            else if (size == 4)
            {
                _code.Bytes(0x89, 0x02);
            }
            else
            {
                _code.Bytes(0x48, 0x89, 0x02);
            }
        }
        _code.Bytes(0xB8, 0x01, 0x00, 0x00, 0x00);
        _code.PatchRelative(skip, _code.Position);
    }

    private void CheckBufferIndex(int buffer)
    {
        _code.Byte(0x3D);
        _code.Int32(_program.Buffers[buffer].Values.Length);
        RuntimeBranch(0x83, 3); // unsigned >= length rejects negative indexes too
    }

    [SuppressMessage("Style", "IDE0010:Populate switch", Justification = "The verifier excludes unsupported expression variants and unary operators.")]
    private void Expression(BoundExpression expression, int spill)
    {
        _cancellationToken.ThrowIfCancellationRequested();
        switch (expression)
        {
            case BoundConstant constant:
                _code.Byte(0xB8);
                _code.Int32(constant.Value);
                break;
            case BoundRead read:
                LoadValue(_program.Functions[_currentFunction].Variables[read.Variable].IsOut
                    ? OutSlot(read.Variable) : Slot(read.Variable), read.Type);
                break;
            case BoundFieldRead fieldRead:
                Expression(fieldRead.Receiver, spill);
                var field = _program.Structures[(int)fieldRead.Receiver.Type - 100].Fields[fieldRead.Field];
                if (field.Offset != 0)
                {
                    _code.Bytes(0x48, 0xC1, 0xE8, checked((byte)(field.Offset * 8)));
                }
                if (field.Size == 1)
                {
                    _code.Bytes(0x0F, 0xB6, 0xC0);
                }
                else if (field.Size == 2)
                {
                    _code.Bytes(0x0F, 0xB7, 0xC0);
                }
                break;
            case BoundBufferRead bufferRead:
                Expression(bufferRead.Index, spill);
                CheckBufferIndex(bufferRead.Buffer);
                _dataFixups.Add(new NativeDataFixup(_code.DataAddress(), _bufferOffsets[bufferRead.Buffer]));
                _code.Bytes(0x8B, 0x04, 0x82); // mov eax,[rdx+rax*4]
                break;
            case BoundUnary unary:
                Expression(unary.Operand, spill);
                switch (unary.Operator)
                {
                    case TokenKind.Minus:
                        _code.Bytes(0xF7, 0xD8);
                        RuntimeBranch(0x80, 0);
                        break;
                    case TokenKind.Tilde:
                        _code.Bytes(0xF7, 0xD0);
                        break;
                    case TokenKind.Bang:
                        _code.Bytes(0x83, 0xF0, 0x01);
                        break;
                }
                break;
            case BoundCast cast:
                Expression(cast.Operand, spill);
                if ((cast.Type == PrimitiveType.UInt32 && cast.Operand.Type == PrimitiveType.Int32)
                    || (cast.Type == PrimitiveType.Int32 && cast.Operand.Type == PrimitiveType.UInt32))
                {
                    _code.Bytes(0x85, 0xC0);
                    RuntimeBranch(0x88, 0);
                }
                else if (cast.Type == PrimitiveType.Char16 && cast.Operand.Type != PrimitiveType.Char16)
                {
                    _code.Byte(0x3D);
                    _code.Int32(ushort.MaxValue);
                    RuntimeBranch(0x87, 0);
                }
                break;
            case BoundBinary binary:
                if (binary.Operator is TokenKind.AmpersandAmpersand or TokenKind.PipePipe)
                {
                    Expression(binary.Left, spill);
                    _code.Bytes(0x85, 0xC0);
                    var skip = _code.JumpIf(binary.Operator == TokenKind.AmpersandAmpersand ? (byte)0x84 : (byte)0x85);
                    Expression(binary.Right, spill);
                    _code.PatchRelative(skip, _code.Position);
                    return;
                }
                Expression(binary.Left, spill);
                StoreValue(Spill(spill), binary.Left.Type);
                Expression(binary.Right, spill + 1);
                _code.Bytes(0x89, 0xC1); // mov ecx, eax
                LoadValue(Spill(spill), binary.Left.Type);
                Binary(binary.Operator, binary.Left.Type);
                break;
            case BoundCall call:
                for (var i = 0; i < call.Arguments.Length; i++)
                {
                    if (call.Arguments[i] is BoundOutArgument)
                    {
                        _code.Bytes(0x31, 0xC0);
                        _code.Store64(Spill(spill + call.Arguments.Length + i));
                        _code.Address(Spill(spill + call.Arguments.Length + i));
                        _code.Store64(Spill(spill + i));
                    }
                    else
                    {
                        Expression(call.Arguments[i], spill + (call.Arguments.Length * 2));
                        StoreValue(Spill(spill + i), call.Arguments[i].Type);
                    }
                }
                for (var i = 4; i < call.Arguments.Length; i++)
                {
                    LoadValue(Spill(spill + i), call.Arguments[i] is BoundOutArgument
                        ? (PrimitiveType)100 : call.Arguments[i].Type);
                    _code.Store64(32 + (8 * (i - 4)));
                }
                for (var i = 0; i < Math.Min(4, call.Arguments.Length); i++)
                {
                    LoadValue(Spill(spill + i), call.Arguments[i] is BoundOutArgument
                        ? (PrimitiveType)100 : call.Arguments[i].Type);
                    _code.MoveArgument(i, call.Arguments[i] is BoundOutArgument || IsStruct(call.Arguments[i].Type));
                }
                _calls.Add((_code.RelativeCall(), call.Function));
                if (call.Type == PrimitiveType.Bool)
                {
                    var hasOut = false;
                    foreach (var argument in call.Arguments)
                    {
                        hasOut |= argument is BoundOutArgument;
                    }
                    if (hasOut)
                    {
                        _code.Bytes(0x85, 0xC0);
                        var skip = _code.JumpIf(0x84);
                        for (var i = 0; i < call.Arguments.Length; i++)
                        {
                            if (call.Arguments[i] is BoundOutArgument output)
                            {
                                var scratch = Spill(spill + call.Arguments.Length + i);
                                if (output.Type is PrimitiveType.Bool or PrimitiveType.Char16)
                                {
                                    _code.LoadUnsigned(scratch, output.Type == PrimitiveType.Bool ? 1 : 2);
                                }
                                else
                                {
                                    LoadValue(scratch, output.Type);
                                }
                                StoreValue(Slot(output.Variable), output.Type);
                            }
                        }
                        _code.Bytes([0xB8, 0x01, 0x00, 0x00, 0x00]);
                        _code.PatchRelative(skip, _code.Position);
                    }
                }
                break;
        }
    }

    [SuppressMessage("Style", "IDE0010:Populate switch", Justification = "The verifier restricts binary operators to the supported native profile.")]
    [SuppressMessage("Style", "IDE0072:Populate switch", Justification = "The verifier excludes other token kinds before reaching the comparison encoder.")]
    private void Binary(TokenKind op, PrimitiveType type)
    {
        var unsigned = type is PrimitiveType.UInt32 or PrimitiveType.Char16;
        switch (op)
        {
            case TokenKind.Plus:
                _code.Bytes(0x01, 0xC8);
                RuntimeBranch(unsigned ? (byte)0x82 : (byte)0x80, 0);
                break;
            case TokenKind.Minus:
                _code.Bytes(0x29, 0xC8);
                RuntimeBranch(unsigned ? (byte)0x82 : (byte)0x80, 0);
                break;
            case TokenKind.Star:
                if (unsigned)
                {
                    _code.Bytes(0xF7, 0xE1); // mul ecx; high half in edx
                    _code.Bytes(0x85, 0xD2);
                    RuntimeBranch(0x85, 0);
                }
                else
                {
                    _code.Bytes(0x0F, 0xAF, 0xC1);
                    RuntimeBranch(0x80, 0);
                }
                break;
            case TokenKind.Slash:
            case TokenKind.Percent:
                _code.Bytes(0x85, 0xC9); // test ecx,ecx
                RuntimeBranch(0x84, 1);
                if (unsigned)
                {
                    _code.Bytes(0x31, 0xD2, 0xF7, 0xF1); // xor edx,edx; div ecx
                    if (op == TokenKind.Percent)
                    {
                        _code.Bytes(0x89, 0xD0);
                    }
                    break;
                }
                _code.Byte(0x3D); // cmp eax, int.MinValue
                _code.Int32(int.MinValue);
                var normal = _code.JumpIf(0x85);
                _code.Bytes(0x83, 0xF9, 0xFF); // cmp ecx,-1
                var normalAgain = _code.JumpIf(0x85);
                if (op == TokenKind.Slash)
                {
                    _runtimeCalls.Add((_code.RelativeCall(), 0));
                }
                else
                {
                    _code.Bytes(0x31, 0xC0); // int.MinValue % -1 is zero
                    var done = _code.Jump();
                    _code.PatchRelative(normal, _code.Position);
                    _code.PatchRelative(normalAgain, _code.Position);
                    _code.Bytes(0x99, 0xF7, 0xF9); // cdq; idiv ecx
                    _code.Bytes(0x89, 0xD0);
                    _code.PatchRelative(done, _code.Position);
                    break;
                }
                _code.PatchRelative(normal, _code.Position);
                _code.PatchRelative(normalAgain, _code.Position);
                _code.Bytes(0x99, 0xF7, 0xF9);
                break;
            case TokenKind.Ampersand:
                _code.Bytes(0x21, 0xC8);
                break;
            case TokenKind.Pipe:
                _code.Bytes(0x09, 0xC8);
                break;
            case TokenKind.Caret:
                _code.Bytes(0x31, 0xC8);
                break;
            default:
                _code.Bytes(0x39, 0xC8); // cmp eax,ecx
                _code.Bytes(0x0F, (byte)(op switch {
                    TokenKind.EqualsEquals => 0x94,
                    TokenKind.BangEquals => 0x95,
                    TokenKind.Less => unsigned ? 0x92 : 0x9C,
                    TokenKind.LessEquals => unsigned ? 0x96 : 0x9E,
                    TokenKind.Greater => unsigned ? 0x97 : 0x9F,
                    TokenKind.GreaterEquals => unsigned ? 0x93 : 0x9D,
                    _ => throw new InvalidOperationException(),
                }), 0xC0);
                _code.Bytes(0x0F, 0xB6, 0xC0); // movzx eax,al
                break;
        }
    }
}

internal sealed class MachineCode
{
    private readonly List<byte> _bytes = new();
    internal int Position => _bytes.Count;
    internal void Byte(byte value) => _bytes.Add(value);
    internal void Bytes(params byte[] values) => _bytes.AddRange(values);
    internal void Int32(int value)
    {
        for (var i = 0; i < 4; i++)
        {
            Byte(unchecked((byte)(value >> (8 * i))));
        }
    }
    internal void PatchInt32(int offset, int value)
    {
        for (var i = 0; i < 4; i++)
        {
            _bytes[offset + i] = unchecked((byte)(value >> (8 * i)));
        }
    }
    internal void PatchRelative(int offset, int target) => PatchInt32(offset, checked(target - offset - 4));
    internal int Jump()
    {
        Byte(0xE9);
        var offset = Position;
        Int32(0);
        return offset;
    }
    internal int JumpIf(byte condition)
    {
        Bytes(0x0F, condition);
        var offset = Position;
        Int32(0);
        return offset;
    }
    internal int RelativeCall()
    {
        Byte(0xE8);
        var offset = Position;
        Int32(0);
        return offset;
    }
    internal int ImportCall()
    {
        Bytes(0xFF, 0x15); // call qword ptr [rip+disp32]
        var offset = Position;
        Int32(0);
        return offset;
    }
    internal int DataAddress()
    {
        Bytes(0x48, 0x8D, 0x15); // lea rdx,[rip+disp32]
        var offset = Position;
        Int32(0);
        return offset;
    }
    internal void Load(int offset)
    {
        Bytes(0x8B, 0x84, 0x24);
        Int32(offset);
    }
    internal void Load64(int offset)
    {
        Byte(0x48);
        Load(offset);
    }
    internal void LoadUnsigned(int offset, int width)
    {
        Bytes(0x0F, width == 1 ? (byte)0xB6 : (byte)0xB7, 0x84, 0x24);
        Int32(offset);
    }
    internal void Address(int offset)
    {
        Bytes(0x48, 0x8D, 0x84, 0x24);
        Int32(offset);
    }
    internal void Store(int offset)
    {
        Bytes(0x89, 0x84, 0x24);
        Int32(offset);
    }
    internal void Store64(int offset)
    {
        Byte(0x48);
        Store(offset);
    }
    internal void StoreField(int offset, int width)
    {
        if (width == 2)
        {
            Byte(0x66);
        }
        if (width == 1)
        {
            Bytes(0x88, 0x84, 0x24);
        }
        else
        {
            Bytes(0x89, 0x84, 0x24);
        }
        Int32(offset);
    }
    internal void StoreRegister(int argument, int offset, bool wide)
    {
        if (argument >= 2)
        {
            Byte(wide ? (byte)0x4C : (byte)0x44);
        }
        else if (wide)
        {
            Byte(0x48);
        }
        Bytes(0x89, argument switch {
            0 or 3 => 0x8C,
            1 => 0x94,
            _ => 0x84,
        }, 0x24);
        Int32(offset);
    }
    [SuppressMessage("Style", "IDE0010:Populate switch", Justification = "Stack arguments are handled by the caller; only the first four use registers.")]
    internal void MoveArgument(int argument, bool wide)
    {
        switch (argument)
        {
            case 0:
                if (wide)
                {
                    Byte(0x48);
                }
                Bytes(0x89, 0xC1);
                break;
            case 1:
                if (wide)
                {
                    Byte(0x48);
                }
                Bytes(0x89, 0xC2);
                break;
            case 2:
                Bytes(wide ? (byte)0x49 : (byte)0x41, 0x89, 0xC0);
                break;
            case 3:
                Bytes(wide ? (byte)0x49 : (byte)0x41, 0x89, 0xC1);
                break;
        }
    }
    internal byte[] ToArray() => [.. _bytes];
}
