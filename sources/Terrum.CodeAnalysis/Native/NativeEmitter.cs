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
    /// <summary>Terminate with status 0xE0540001 for overflow, 0xE0540002 for division by zero, or 0xE0540003 for a failed requirement.</summary>
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
    private readonly int[] _failureStarts = new int[3];
    private readonly int[] _failureEnds = new int[3];
    private int _entryStart;
    private int _entryEnd;
    private int _frame;
    private int _outgoingSize;
    private int _slotCount;
    private int _spillBase;

    internal PeCompiler(BoundProgram program, CancellationToken cancellationToken)
    {
        _program = program;
        _cancellationToken = cancellationToken;
        _starts = new int[program.Functions.Length];
        _ends = new int[program.Functions.Length];
        _frames = new int[program.Functions.Length];
        _outgoing = new int[program.Functions.Length];
    }

    internal byte[] Compile()
    {
        for (var i = 0; i < _program.Functions.Length; i++)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            var function = _program.Functions[i];
            var maximumArguments = 0;
            var spillSlots = StatementSpills(function.Body, ref maximumArguments);
            var outgoingSize = 32 + (8 * Math.Max(0, maximumArguments - 4));
            var required = outgoingSize + (4L * (function.Variables.Length + spillSlots));
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
        if (_program.Functions[_program.EntryPoint].ReturnType == PrimitiveType.Int32)
        {
            _code.Bytes(0x89, 0xC1); // mov ecx, eax
        }
        else
        {
            _code.Bytes(0x31, 0xC9); // xor ecx, ecx
        }
        _imports.Add(_code.ImportCall());
        _code.Byte(0xCC);
        _entryEnd = _code.Position;

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
            _frame = _frames[i];
            _outgoingSize = _outgoing[i];
            _slotCount = function.Variables.Length;
            _spillBase = _outgoingSize + (4 * _slotCount);
            Prolog(_frame);
            for (var argument = 0; argument < function.ParameterCount; argument++)
            {
                if (argument < 4)
                {
                    _code.StoreRegister(argument, Slot(argument));
                }
                else
                {
                    // At entry [rsp] is the return address, [rsp+8..39] is caller home space.
                    _code.Load(_frame + 40 + (8 * (argument - 4)));
                    _code.Store(Slot(argument));
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
        var functions = new List<(int Start, int End, int Frame)> { (_entryStart, _entryEnd, 40) };
        for (var i = 0; i < _failureStarts.Length; i++)
        {
            functions.Add((_failureStarts[i], _failureEnds[i], 40));
        }
        for (var i = 0; i < _starts.Length; i++)
        {
            functions.Add((_starts[i], _ends[i], _frames[i]));
        }

        return PeImage.Build(_code.ToArray(), _entryStart, functions, _imports);
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
            case BoundBinary binary:
                return Math.Max(ExpressionSpills(binary.Left, ref maximumArguments),
                    1 + ExpressionSpills(binary.Right, ref maximumArguments));
            case BoundCall call:
                maximumArguments = Math.Max(maximumArguments, call.Arguments.Length);
                var maximum = call.Arguments.Length;
                for (var i = 0; i < call.Arguments.Length; i++)
                {
                    maximum = Math.Max(maximum, i + ExpressionSpills(call.Arguments[i], ref maximumArguments));
                }
                return maximum;
            default:
                return 0;
        }
    }

    private int Slot(int index) => _outgoingSize + (index * 4);
    private int Spill(int index) => _spillBase + (index * 4);

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
                _code.Store(Slot(local.Variable));
                break;
            case BoundAssignment assignment:
                Expression(assignment.Value, 0);
                _code.Store(Slot(assignment.Variable));
                break;
            case BoundExpressionStatement expression:
                Expression(expression.Expression, 0);
                break;
            case BoundReturn result:
                if (result.Value is not null)
                {
                    Expression(result.Value, 0);
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
                _code.Load(Slot(read.Variable));
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
                _code.Store(Spill(spill));
                Expression(binary.Right, spill + 1);
                _code.Bytes(0x89, 0xC1); // mov ecx, eax
                _code.Load(Spill(spill));
                Binary(binary.Operator);
                break;
            case BoundCall call:
                for (var i = 0; i < call.Arguments.Length; i++)
                {
                    Expression(call.Arguments[i], spill + i);
                    _code.Store(Spill(spill + i));
                }
                for (var i = 4; i < call.Arguments.Length; i++)
                {
                    _code.Load(Spill(spill + i));
                    _code.Store(32 + (8 * (i - 4)));
                }
                for (var i = 0; i < Math.Min(4, call.Arguments.Length); i++)
                {
                    _code.Load(Spill(spill + i));
                    _code.MoveArgument(i);
                }
                _calls.Add((_code.RelativeCall(), call.Function));
                break;
        }
    }

    [SuppressMessage("Style", "IDE0010:Populate switch", Justification = "The verifier restricts binary operators to the supported native profile.")]
    [SuppressMessage("Style", "IDE0072:Populate switch", Justification = "The verifier excludes other token kinds before reaching the comparison encoder.")]
    private void Binary(TokenKind op)
    {
        switch (op)
        {
            case TokenKind.Plus:
                _code.Bytes(0x01, 0xC8);
                RuntimeBranch(0x80, 0);
                break;
            case TokenKind.Minus:
                _code.Bytes(0x29, 0xC8);
                RuntimeBranch(0x80, 0);
                break;
            case TokenKind.Star:
                _code.Bytes(0x0F, 0xAF, 0xC1);
                RuntimeBranch(0x80, 0);
                break;
            case TokenKind.Slash:
            case TokenKind.Percent:
                _code.Bytes(0x85, 0xC9); // test ecx,ecx
                RuntimeBranch(0x84, 1);
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
                _code.Bytes(0x0F, op switch {
                    TokenKind.EqualsEquals => 0x94,
                    TokenKind.BangEquals => 0x95,
                    TokenKind.Less => 0x9C,
                    TokenKind.LessEquals => 0x9E,
                    TokenKind.Greater => 0x9F,
                    TokenKind.GreaterEquals => 0x9D,
                    _ => throw new InvalidOperationException(),
                }, 0xC0);
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
    internal void Load(int offset)
    {
        Bytes(0x8B, 0x84, 0x24);
        Int32(offset);
    }
    internal void Store(int offset)
    {
        Bytes(0x89, 0x84, 0x24);
        Int32(offset);
    }
    internal void StoreRegister(int argument, int offset)
    {
        if (argument >= 2)
        {
            Byte(0x44);
        }
        Bytes(0x89, argument switch {
            0 or 3 => 0x8C,
            1 => 0x94,
            _ => 0x84,
        }, 0x24);
        Int32(offset);
    }
    [SuppressMessage("Style", "IDE0010:Populate switch", Justification = "Stack arguments are handled by the caller; only the first four use registers.")]
    internal void MoveArgument(int argument)
    {
        switch (argument)
        {
            case 0:
                Bytes(0x89, 0xC1);
                break;
            case 1:
                Bytes(0x89, 0xC2);
                break;
            case 2:
                Bytes(0x41, 0x89, 0xC0);
                break;
            case 3:
                Bytes(0x41, 0x89, 0xC1);
                break;
        }
    }
    internal byte[] ToArray() => [.. _bytes];
}
