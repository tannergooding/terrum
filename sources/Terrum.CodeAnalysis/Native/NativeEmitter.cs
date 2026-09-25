// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using Terrum.CodeAnalysis.Lexing;
using Terrum.CodeAnalysis.Semantics;

namespace Terrum.CodeAnalysis.Native;

/// <summary>Behavior on a checked runtime failure in a native executable.</summary>
public enum NativeRuntimePolicy
{
    /// <summary>Terminate with 0xE0540001 for overflow, 0xE0540002 for zero division, 0xE0540003 for failed requirements, or 0xE0540004 for buffer bounds.</summary>
    FailFast,
    /// <summary>Use the bounded, versioned native checked-failure exception contract.</summary>
    CheckedExceptions,
}

/// <summary>Maps one verified external function declaration to a named Windows DLL export.</summary>
public sealed record NativeImportBinding(string FunctionName, string DllName, string ExportName);

/// <summary>Emits owned, deterministic Windows x64 PE32+ artifacts from verified structured IR.</summary>
public static class NativeEmitter
{
    /// <summary>Emits a native consumer executable with explicit bindings for every external declaration.</summary>
    /// <remarks>Bindings specify ABI identities, not verified provider contracts; callers must validate provider compatibility.</remarks>
    public static byte[] EmitWithImports(BoundProgram program, IReadOnlyList<NativeImportBinding> bindings,
        NativeRuntimePolicy runtimePolicy, CancellationToken cancellationToken = default)
    {
        if (runtimePolicy is not (NativeRuntimePolicy.FailFast or NativeRuntimePolicy.CheckedExceptions))
        {
            throw new NotSupportedException("Unsupported native runtime policy.");
        }
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(program);
        ArgumentNullException.ThrowIfNull(bindings);
        SemanticVerifier.VerifyReferences(program);
        if (program.RuntimePolicy != (BoundRuntimePolicy)runtimePolicy)
        {
            throw new ArgumentException("Native and verified checked-failure policies differ.", nameof(program));
        }
        if (bindings.Count is < 1 or > 1024)
        {
            throw new ArgumentException("Native consumers require 1 to 1024 explicit import bindings.", nameof(bindings));
        }
        var externals = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var index = 0; index < program.Functions.Length; index++)
        {
            if (program.Functions[index].IsExternal)
            {
                externals.Add(program.Functions[index].Name, index);
            }
        }
        if (bindings.Count != externals.Count)
        {
            throw new ArgumentException("Every external function needs exactly one native import binding.", nameof(bindings));
        }
        var selected = new Dictionary<int, NativeImportBinding>();
        var importNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var binding in bindings)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (binding is null || string.IsNullOrEmpty(binding.FunctionName)
                || !externals.TryGetValue(binding.FunctionName, out var index)
                || !ValidDllName(binding.DllName) || !ValidImportName(binding.ExportName)
                || !importNames.Add(binding.DllName.ToUpperInvariant() + "\0" + binding.ExportName))
            {
                throw new ArgumentException("Import bindings must uniquely identify external declarations and DLL exports.", nameof(bindings));
            }
            if (!selected.TryAdd(index, binding))
            {
                throw new ArgumentException("An external function has multiple native import bindings.", nameof(bindings));
            }
        }
        var code = new PeCompiler(program, cancellationToken, imports: selected).Compile();
        return PeImage.Build(code);
    }

    private static bool ValidImportName(string? name) => name is { Length: >= 1 and <= 256 }
        && name.All(static character => character is >= '!' and <= '~');

    private static bool ValidDllName(string? name) => name is { Length: >= 5 and <= 256 }
        && name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
        && name.All(static character => character is (>= '0' and <= '9') or (>= 'A' and <= 'Z')
            or (>= 'a' and <= 'z') or '_' or '-' or '.');

    /// <summary>Verifies a library and emits a Windows x64 DLL with explicitly named public exports.</summary>
    /// <exception cref="ArgumentException">The library, export names, or public value layouts are invalid.</exception>
    /// <exception cref="NotSupportedException">The runtime policy or a native resource limit is unsupported.</exception>
    public static byte[] EmitLibrary(BoundProgram program, IReadOnlyList<string> exports,
        NativeRuntimePolicy runtimePolicy, CancellationToken cancellationToken = default)
    {
        if (runtimePolicy is not (NativeRuntimePolicy.FailFast or NativeRuntimePolicy.CheckedExceptions))
        {
            throw new NotSupportedException("Unsupported native runtime policy.");
        }
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(program);
        SemanticVerifier.VerifyLibrary(program);
        if (program.RuntimePolicy != (BoundRuntimePolicy)runtimePolicy)
        {
            throw new ArgumentException("Native and verified checked-failure policies differ.", nameof(program));
        }
        ArgumentNullException.ThrowIfNull(exports);
        if (!program.Buffers.IsEmpty)
        {
            throw new NotSupportedException("Native DLLs do not expose host buffer capabilities.");
        }
        if (exports.Count is < 1 or > 1024)
        {
            throw new ArgumentException("A native library requires 1 to 1024 explicit exports.", nameof(exports));
        }
        var selected = new List<(string Name, int Index)>(exports.Count);
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var name in exports)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrEmpty(name) || !names.Add(name)
                || name.Length > 256 || name.Any(static character => character is < '!' or > '~'))
            {
                throw new ArgumentException("Export names must be unique printable ASCII names of at most 256 bytes.", nameof(exports));
            }
            var index = -1;
            for (var i = 0; i < program.Functions.Length; i++)
            {
                if (program.Functions[i].Name == name)
                {
                    index = i;
                    break;
                }
            }
            if (index < 0 || !Exportable(program, program.Functions[index]))
            {
                throw new ArgumentException($"'{name}' is not an accessible native library export.", nameof(exports));
            }
            selected.Add((name, index));
        }
        selected.Sort(static (left, right) => StringComparer.Ordinal.Compare(left.Name, right.Name));
        var code = new PeCompiler(program, cancellationToken, library: true).Compile();
        return PeImage.BuildLibrary(code, [.. selected], program.Functions.Length);
    }

    private static bool Exportable(BoundProgram program, BoundFunction function)
    {
        if (function.IsPrivate || function.IsInternal || function.IsExternal)
        {
            return false;
        }
        if (function.DeclaringStruct is string owner
            && !program.Structures.Any(structure => structure.Name == owner && !structure.IsPrivate && !structure.IsInternal))
        {
            return false;
        }
        if (!PublicLayout(function.ReturnType))
        {
            return false;
        }
        for (var i = 0; i < function.ParameterCount; i++)
        {
            if (!PublicLayout(function.Variables[i + (function.IsConstructor ? 1 : 0)].Type))
            {
                return false;
            }
        }
        return true;

        bool PublicLayout(PrimitiveType type)
        {
            if ((int)type < 100)
            {
                return true;
            }
            var structure = program.Structures[(int)type - 100];
            return !structure.IsPrivate && !structure.IsInternal
                && structure.Fields.All(field => !field.IsPrivate && !field.IsInternal && PublicLayout(field.Type));
        }
    }

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
        if (runtimePolicy is not (NativeRuntimePolicy.FailFast or NativeRuntimePolicy.CheckedExceptions))
        {
            throw new NotSupportedException("Unsupported native runtime policy.");
        }
        cancellationToken.ThrowIfCancellationRequested();
        SemanticVerifier.Verify(program);
        if (program.RuntimePolicy != (BoundRuntimePolicy)runtimePolicy)
        {
            throw new ArgumentException("Native and verified checked-failure policies differ.", nameof(program));
        }
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
    private readonly bool _library;
    private readonly IReadOnlyDictionary<int, NativeImportBinding>? _nativeImports;
    private readonly CancellationToken _cancellationToken;
    private readonly MachineCode _code = new();
    private readonly int[] _starts;
    private readonly int[] _ends;
    private readonly int[] _frames;
    private readonly int[][] _slots;
    private readonly int[] _spillBases;
    private readonly int[] _outBases;
    private readonly int[] _returnSlots;
    private readonly List<(int Offset, int Target)> _calls = new();
    private readonly List<(int Offset, int Target)> _runtimeCalls = new();
    private readonly List<(int Offset, int Target)> _cleanupCalls = [];
    private readonly List<(BoundBlock Body, int Function, int Start, int End)> _funclets = [];
    private readonly List<int> _imports = new();
    private readonly List<NativeImportFixup> _nativeImportFixups = [];
    private readonly List<NativeDataFixup> _dataFixups = [];
    private readonly int[] _bufferOffsets;
    private readonly byte[] _data;
    private readonly bool _checkedExceptions;
    private readonly List<NativeExceptionScope>[] _scopes;
    private readonly List<NativeExceptionScope> _entryScopes = [];
    private readonly int _readOnlyDataLength;
    private readonly int[] _failureStarts = new int[4];
    private readonly int[] _failureEnds = new int[4];
    private int _entryStart;
    private int _entryEnd;
    private int _exceptionHandler;
    private int _catchFilter;
    private int _entryFilter;
    private int _frame;
    private int _spillBase;
    private int _outBase;
    private int _returnSlot;
    private int _currentFunction;
    private bool _emittingFunclet;
    private readonly List<int> _cleanups = [];

    internal PeCompiler(BoundProgram program, CancellationToken cancellationToken, bool library = false,
        IReadOnlyDictionary<int, NativeImportBinding>? imports = null)
    {
        _program = program;
        _checkedExceptions = program.RuntimePolicy == BoundRuntimePolicy.CheckedExceptions;
        _library = library;
        _nativeImports = imports;
        _cancellationToken = cancellationToken;
        _starts = new int[program.Functions.Length];
        _scopes = new List<NativeExceptionScope>[program.Functions.Length];
        for (var index = 0; index < _scopes.Length; index++)
        {
            _scopes[index] = [];
        }
        _ends = new int[program.Functions.Length];
        _frames = new int[program.Functions.Length];
        _slots = new int[program.Functions.Length][];
        _spillBases = new int[program.Functions.Length];
        _outBases = new int[program.Functions.Length];
        _returnSlots = new int[program.Functions.Length];
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
        PrepareFrames();
        var (processEntry, processEnd) = EmitEntryAndFailureHelpers();
        EmitSourceFunctions();
        EmitCleanupFunclets();

        foreach (var (offset, target) in _calls)
        {
            _code.PatchRelative(offset, _starts[target]);
        }
        foreach (var (offset, target) in _runtimeCalls)
        {
            _code.PatchRelative(offset, _failureStarts[target]);
        }
        foreach (var (offset, target) in _cleanupCalls)
        {
            _code.PatchRelative(offset, _funclets[target].Start);
        }
        if (_checkedExceptions)
        {
            _exceptionHandler = _code.Position;
            _nativeImportFixups.Add(new NativeImportFixup(_code.ImportJump(), "NTDLL.dll", "__C_specific_handler"));
            _catchFilter = _code.Position;
            EmitExceptionFilter(storeReason: false);
            _entryFilter = _code.Position;
            EmitExceptionFilter(storeReason: true);
        }
        if (_code.Position > MaximumText)
        {
            throw new NotSupportedException("Native text exceeds the 8 MiB image ceiling.");
        }
        var functions = new List<NativeFunctionRange>();
        if (!_library)
        {
            functions.Add(new NativeFunctionRange(_entryStart, _entryEnd, 40) {
                ExceptionHandler = _checkedExceptions ? _exceptionHandler : 0,
                ExceptionScopes = [.. _entryScopes.Select(scope => scope with { Handler = _entryFilter })],
            });
            functions.Add(new NativeFunctionRange(processEntry, processEnd, 40));
        }
        for (var i = 0; i < _failureStarts.Length; i++)
        {
            functions.Add(new NativeFunctionRange(_failureStarts[i], _failureEnds[i], _checkedExceptions ? 72 : 40));
        }
        for (var i = 0; i < _starts.Length; i++)
        {
            if (!_program.Functions[i].IsExternal)
            {
                functions.Add(new NativeFunctionRange(_starts[i], _ends[i], _frames[i]) {
                    UsesFramePointer = true,
                    ExceptionHandler = _checkedExceptions ? _exceptionHandler : 0,
                    ExceptionScopes = [.. _scopes[i].Select(scope => scope with {
                        Handler = scope.Target != 0 ? _catchFilter : _funclets[-scope.Handler - 1].Start,
                    })],
                });
            }
        }
        foreach (var (_, function, start, end) in _funclets)
        {
            functions.Add(new NativeFunctionRange(start, end, _frames[function]) {
                UsesFramePointer = true,
                UsesParentFrame = true,
            });
        }

        return new NativeCode(_code.ToArray(), _entryStart,
            [.. functions],
            [.. _imports]) {
            AotEntryPoint = processEntry,
            Data = _data,
            ReadOnlyDataLength = _readOnlyDataLength,
            BufferOffsets = [.. _bufferOffsets],
            DataFixups = [.. _dataFixups],
            NativeImports = [.. _nativeImportFixups],
        };
    }

    private void PrepareFrames()
    {
        for (var i = 0; i < _program.Functions.Length; i++)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            var function = _program.Functions[i];
            if (function.IsExternal)
            {
                continue;
            }
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
            var at = (outgoingSize + 15) & ~15;
            var slots = new int[function.Variables.Length];
            for (var variable = 0; variable < slots.Length; variable++)
            {
                if (IsIndirect(function.Variables[variable].Type) && !function.Variables[variable].IsOut)
                {
                    at = (at + 15) & ~15;
                }
                slots[variable] = at;
                at += IsIndirect(function.Variables[variable].Type) && !function.Variables[variable].IsOut ? 16 : 8;
                at = (at + 7) & ~7;
            }
            _slots[i] = slots;
            _spillBases[i] = (at + 15) & ~15;
            _outBases[i] = _spillBases[i] + (16 * spillSlots);
            _returnSlots[i] = _outBases[i] + (16 * outParameters);
            var required = _returnSlots[i] + (IsIndirect(function.ReturnType) ? 8L : 0L);
            if (ContainsCleanup(function.Body))
            {
                required = ((required + 15) & ~15L) + 16;
            }
            required += 8; // Saved RBP lives above the addressable frame slots.
            if (required > MaximumFrame)
            {
                throw new NotSupportedException("Native frame exceeds the 3840-byte page-safe ceiling.");
            }
            var frame = AlignFrame((int)required);
            if (frame > MaximumFrame)
            {
                throw new NotSupportedException("Native frame exceeds the 3840-byte page-safe ceiling.");
            }
            _frames[i] = frame;
        }
    }

    private void EmitSourceFunctions()
    {
        for (var i = 0; i < _program.Functions.Length; i++)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            var function = _program.Functions[i];
            if (function.IsExternal)
            {
                continue;
            }
            _starts[i] = _code.Position;
            _currentFunction = i;
            _frame = _frames[i];
            _spillBase = _spillBases[i];
            _outBase = _outBases[i];
            _returnSlot = _returnSlots[i];
            _cleanups.Clear();
            _code.Byte(0x55); // push rbp
            Prolog(_frame - 8);
            _code.Bytes(0x48, 0x89, 0xE5); // mov rbp,rsp
            _code.UseFramePointer = true;
            var hidden = IsIndirect(function.ReturnType) ? 1 : 0;
            if (hidden != 0)
            {
                _code.StoreRegister(0, _returnSlot, true);
            }
            if (function.IsConstructor)
            {
                if (IsIndirect(function.ReturnType))
                {
                    ZeroStruct(Slot(0), Size(function.ReturnType));
                }
                else
                {
                    _code.Bytes(0x31, 0xC0);
                    _code.Store64(Slot(0));
                }
            }
            for (var argument = 0; argument < function.ParameterCount; argument++)
            {
                var slot = argument + (function.IsConstructor ? 1 : 0);
                var abiArgument = argument + hidden;
                var type = function.Variables[slot].Type;
                if (abiArgument < 4)
                {
                    _code.StoreRegister(abiArgument, Slot(slot),
                        IsStruct(type) || IsWide(type) || function.Variables[slot].IsOut);
                }
                else
                {
                    // At entry [rsp] is the return address, [rsp+8..39] is caller home space.
                    _code.Load64(_frame + 40 + (8 * (abiArgument - 4)));
                    _code.Store64(Slot(slot));
                }
            }
            for (var argument = 0; argument < function.ParameterCount; argument++)
            {
                var slot = argument + (function.IsConstructor ? 1 : 0);
                var type = function.Variables[slot].Type;
                if (IsIndirect(type) && !function.Variables[slot].IsOut)
                {
                    _code.Load64(Slot(slot));
                    CopyToFrame(Slot(slot), Size(type));
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
    }

    private void EmitCleanupFunclets()
    {
        for (var index = 0; index < _funclets.Count; index++)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            var (body, function, _, _) = _funclets[index];
            _currentFunction = function;
            _frame = _frames[function];
            _spillBase = _spillBases[function];
            _outBase = _outBases[function];
            _returnSlot = _returnSlots[function];
            var start = _code.Position;
            _code.Byte(0x55); // push rbp
            Prolog(_frame - 8);
            _code.Bytes(0x48, 0x89, 0xD5); // mov rbp,rdx: the source function's frame base
            _emittingFunclet = true;
            Statement(body);
            _emittingFunclet = false;
            Epilog();
            _funclets[index] = (body, function, start, _code.Position);
            if (_code.Position > MaximumText)
            {
                throw new NotSupportedException("Native text exceeds the 8 MiB image ceiling.");
            }
        }
    }

    private (int ProcessEntry, int ProcessEnd) EmitEntryAndFailureHelpers()
    {
        if (!_library)
        {
            _entryStart = _code.Position;
            Prolog(40);
            var protectedStart = _code.Position;
            _calls.Add((_code.RelativeCall(), _program.EntryPoint));
            if (_checkedExceptions)
            {
                _code.Byte(0x90); // keep the protected call continuation outside the epilog
            }
            var protectedEnd = _code.Position;
            if (_program.Functions[_program.EntryPoint].ReturnType == PrimitiveType.Void)
            {
                _code.Bytes(0x31, 0xC0); // xor eax,eax
            }
            _frame = 40;
            Epilog();
            if (_checkedExceptions)
            {
                var handled = _code.Position;
                _code.Bytes(0x8B, 0x4C, 0x24, 0x20); // recorded failure reason
                _code.Bytes(0x81, 0xC9, 0x00, 0x00, 0x54, 0xE0); // 0xE0540000 | reason
                _imports.Add(_code.ImportCall());
                _code.Byte(0xCC);
                _entryScopes.Add(new NativeExceptionScope(protectedStart, protectedEnd, 0, handled));
            }
            _entryEnd = _code.Position;
        }

        var processEntry = _code.Position;
        if (!_library)
        {
            Prolog(40);
            _code.PatchRelative(_code.RelativeCall(), _entryStart);
            _code.Bytes(0x89, 0xC1); // mov ecx,eax
            _imports.Add(_code.ImportCall());
            _code.Byte(0xCC);
        }
        var processEnd = _code.Position;

        for (var failure = 0; failure < _failureStarts.Length; failure++)
        {
            _failureStarts[failure] = _code.Position;
            if (_checkedExceptions)
            {
                Prolog(72);
                _code.Bytes(0x48, 0xB8);
                _code.Int64(0x54455252554D0001);
                _code.Bytes(0x48, 0x89, 0x44, 0x24, 0x20);
                _code.Bytes(0x48, 0xC7, 0x44, 0x24, 0x28, 0x01, 0x00, 0x00, 0x00);
                _code.Bytes(0x48, 0xC7, 0x44, 0x24, 0x30);
                _code.Int32(failure + 1);
                _code.Byte(0xB9);
                _code.Int32(unchecked((int)0xE0540092));
                _code.Bytes(0xBA, 0x01, 0x00, 0x00, 0x00); // EXCEPTION_NONCONTINUABLE
                _code.Bytes(0x41, 0xB8, 0x03, 0x00, 0x00, 0x00);
                _code.Bytes(0x4C, 0x8D, 0x4C, 0x24, 0x20);
                _nativeImportFixups.Add(new NativeImportFixup(_code.ImportCall(), "KERNEL32.dll", "RaiseException"));
            }
            else
            {
                Prolog(40);
                _code.Byte(0xB9);
                _code.Int32(unchecked((int)0xE0540001) + failure);
                _imports.Add(_code.ImportCall());
            }
            _code.Byte(0xCC);
            _failureEnds[failure] = _code.Position;
        }
        return (processEntry, processEnd);
    }

    private static int AlignFrame(int bytes) => ((bytes + 8 + 15) & ~15) - 8;

    private static bool ContainsCleanup(BoundStatement statement) => statement switch {
        BoundTryFinally => true,
        BoundTryCatch recovery => recovery.Finally is not null || ContainsCleanup(recovery.Body)
            || ContainsCleanup(recovery.Catch),
        BoundUnsafe unsafeBlock => ContainsCleanup(unsafeBlock.Body),
        BoundBlock block => block.Statements.Any(ContainsCleanup),
        BoundIf conditional => ContainsCleanup(conditional.Then)
            || (conditional.Else is not null && ContainsCleanup(conditional.Else)),
        BoundWhile loop => ContainsCleanup(loop.Body),
        _ => false,
    };

    private int PendingReturnSlot() => (_returnSlot + (IsIndirect(_program.Functions[_currentFunction].ReturnType) ? 8 : 0) + 15) & ~15;

    private int StatementSpills(BoundStatement statement, ref int maximumArguments)
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
            case BoundTryFinally cleanup:
                return Math.Max(StatementSpills(cleanup.Body, ref maximumArguments),
                    StatementSpills(cleanup.Finally, ref maximumArguments));
            case BoundUnsafe unsafeBlock:
                return StatementSpills(unsafeBlock.Body, ref maximumArguments);
            case BoundTryCatch recovery:
                return Math.Max(StatementSpills(recovery.Body, ref maximumArguments),
                    Math.Max(StatementSpills(recovery.Catch, ref maximumArguments),
                        recovery.Finally is null ? 0 : StatementSpills(recovery.Finally, ref maximumArguments)));
            case BoundRequire requirement:
                return ExpressionSpills(requirement.Condition, ref maximumArguments);
            case BoundReturn result when result.Value is BoundExpression value:
                return ExpressionSpills(value, ref maximumArguments);
            default:
                return 0;
        }
    }

    private int ExpressionSpills(BoundExpression expression, ref int maximumArguments)
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
                maximumArguments = Math.Max(maximumArguments,
                    call.Arguments.Length + (IsIndirect(call.Type) ? 1 : 0));
                var maximum = 1 + (call.Arguments.Length * 2);
                for (var i = 0; i < call.Arguments.Length; i++)
                {
                    if (call.Arguments[i] is not BoundOutArgument)
                    {
                        maximum = Math.Max(maximum, 1 + (call.Arguments.Length * 2)
                            + ExpressionSpills(call.Arguments[i], ref maximumArguments));
                    }
                }
                return maximum;
            default:
                return 0;
        }
    }

    private int Slot(int index) => _slots[_currentFunction][index];
    private int Spill(int index) => _spillBase + (index * 16);
    private int OutSlot(int index)
    {
        var ordinal = 0;
        for (var i = 0; i < index; i++)
        {
            ordinal += _program.Functions[_currentFunction].Variables[i].IsOut ? 1 : 0;
        }
        return _outBase + (16 * ordinal);
    }
    private static bool IsStruct(PrimitiveType type) => (int)type >= 100;
    private static bool IsWide(PrimitiveType type) => type is PrimitiveType.Int64 or PrimitiveType.UInt64;
    private int Size(PrimitiveType type) => ValueLayout.Size(type, _program.Structures);
    private bool IsIndirect(PrimitiveType type) => IsStruct(type) && Size(type) is not (1 or 2 or 4 or 8);
    private void StoreValue(int offset, PrimitiveType type)
    {
        if (IsIndirect(type))
        {
            CopyToFrame(offset, Size(type));
        }
        else if (IsStruct(type) || IsWide(type))
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
        if (IsIndirect(type))
        {
            _code.Address(offset);
        }
        else if (IsStruct(type) || IsWide(type))
        {
            _code.Load64(offset);
        }
        else
        {
            _code.Load(offset);
        }
    }

    private void CopyToFrame(int destination, int size)
    {
        _code.AddressToRdx(destination);
        _code.CopyMemory(size);
    }

    private void ZeroStruct(int destination, int size)
    {
        _code.Bytes(0x31, 0xC0);
        var offset = 0;
        while (offset < size)
        {
            var width = Math.Min(4, size - offset);
            if (width == 3)
            {
                width = 2;
            }
            _code.StoreField(destination + offset, width);
            offset += width;
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
        _code.Int32(_frame - (_code.UseFramePointer ? 8 : 0));
        if (_code.UseFramePointer)
        {
            _code.Byte(0x5D); // pop rbp
        }
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

    private void CallCleanup(int index)
    {
        _code.Bytes(0x48, 0x89, 0xEA); // mov rdx,rbp: pass the owning function's frame base
        _cleanupCalls.Add((_code.RelativeCall(), index));
    }

    private void EmitExceptionFilter(bool storeReason)
    {
        // A leaf filter rejects everything except the private noncontinuable, versioned
        // four-reason record. The entry filter additionally records the reason in its frame.
        _code.Bytes(0x48, 0x8B, 0x01); // mov rax,[rcx]: ExceptionRecord
        _code.Bytes(0x81, 0x38);
        _code.Int32(unchecked((int)0xE0540092));
        var reject = new List<int> { _code.JumpIf(0x85) };
        _code.Bytes(0xF6, 0x40, 0x04, 0x01);
        reject.Add(_code.JumpIf(0x84));
        _code.Bytes(0x83, 0x78, 0x18, 0x03);
        reject.Add(_code.JumpIf(0x85));
        _code.Bytes(0x49, 0xB8);
        _code.Int64(0x54455252554D0001);
        _code.Byte(0x4C);
        _code.Byte(0x39);
        _code.Byte(0x40);
        _code.Byte(0x20);
        reject.Add(_code.JumpIf(0x85));
        _code.Bytes(0x48, 0x83, 0x78, 0x28, 0x01);
        reject.Add(_code.JumpIf(0x85));
        _code.Bytes(0x4C, 0x8B, 0x40, 0x30); // reason
        _code.Bytes(0x49, 0x83, 0xF8, 0x01);
        reject.Add(_code.JumpIf(0x82));
        _code.Bytes(0x49, 0x83, 0xF8, 0x04);
        reject.Add(_code.JumpIf(0x87));
        if (storeReason)
        {
            _code.Bytes(0x44, 0x89, 0x42, 0x20); // store reason in entry frame, outside home space
        }
        _code.Bytes(0xB8, 0x01, 0x00, 0x00, 0x00, 0xC3);
        foreach (var offset in reject)
        {
            _code.PatchRelative(offset, _code.Position);
        }
        _code.Bytes(0x31, 0xC0, 0xC3);
    }

    [SuppressMessage("Style", "IDE0010:Populate switch", Justification = "The verifier excludes other structured statement variants.")]
    private void Statement(BoundStatement statement)
    {
        _cancellationToken.ThrowIfCancellationRequested();
        if (_code.Position > MaximumText)
        {
            throw new NotSupportedException("Native text exceeds the 8 MiB image ceiling.");
        }
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
                    if (IsIndirect(assignment.Value.Type))
                    {
                        CopyToFrame(OutSlot(assignment.Variable), Size(assignment.Value.Type));
                    }
                    else
                    {
                        _code.Store64(OutSlot(assignment.Variable));
                    }
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
                var destination = (variable.IsOut ? OutSlot(fieldAssignment.Variable) : Slot(fieldAssignment.Variable)) + field.Offset;
                foreach (var nestedField in fieldAssignment.NestedFields)
                {
                    field = _program.Structures[(int)field.Type - 100].Fields[nestedField];
                    destination += field.Offset;
                }
                if (IsIndirect(field.Type))
                {
                    CopyToFrame(destination, field.Size);
                }
                else
                {
                    _code.StoreField(destination, field.Size);
                }
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
                    if (_cleanups.Count != 0)
                    {
                        StoreValue(PendingReturnSlot(), result.Value.Type);
                    }
                }
                for (var index = _cleanups.Count - 1; index >= 0; index--)
                {
                    CallCleanup(_cleanups[index]);
                }
                if (result.Value is not null)
                {
                    if (_cleanups.Count != 0)
                    {
                        LoadValue(PendingReturnSlot(), result.Value.Type);
                    }
                    if (IsIndirect(result.Value.Type))
                    {
                        _code.Load64(_returnSlot, destinationRegister: true);
                        _code.CopyMemory(Size(result.Value.Type));
                        _code.Load64(_returnSlot);
                    }
                }
                if (_program.Functions[_currentFunction].ReturnType == PrimitiveType.Bool)
                {
                    CommitOutParameters();
                }
                if (_checkedExceptions)
                {
                    _code.Byte(0x90); // call-site continuation must not start at an unwind epilog
                }
                Epilog();
                break;
            case BoundTryFinally cleanup:
                var funclet = _funclets.Count;
                _funclets.Add((cleanup.Finally, _currentFunction, 0, 0));
                _cleanups.Add(funclet);
                var protectedBegin = _code.Position;
                Statement(cleanup.Body);
                if (_checkedExceptions && !_emittingFunclet)
                {
                    _code.Byte(0x90);
                }
                var protectedEnd = _code.Position;
                _cleanups.RemoveAt(_cleanups.Count - 1);
                if (_checkedExceptions && !_emittingFunclet && protectedBegin != protectedEnd)
                {
                    _scopes[_currentFunction].Add(new NativeExceptionScope(protectedBegin, protectedEnd, -funclet - 1, 0));
                }
                CallCleanup(funclet);
                break;
            case BoundUnsafe unsafeBlock:
                Statement(unsafeBlock.Body);
                break;
            case BoundTryCatch recovery:
                var cleanupIndex = -1;
                if (recovery.Finally is not null)
                {
                    cleanupIndex = _funclets.Count;
                    _funclets.Add((recovery.Finally, _currentFunction, 0, 0));
                    _cleanups.Add(cleanupIndex);
                }
                var bodyBegin = _code.Position;
                Statement(recovery.Body);
                _code.Byte(0x90);
                var bodyEnd = _code.Position;
                var skipCatch = _code.Jump();
                var landing = _code.Position;
                Statement(recovery.Catch);
                if (cleanupIndex >= 0)
                {
                    _code.Byte(0x90);
                }
                var catchEnd = _code.Position;
                _code.PatchRelative(skipCatch, catchEnd);
                if (bodyBegin != bodyEnd)
                {
                    _scopes[_currentFunction].Add(new NativeExceptionScope(bodyBegin, bodyEnd, 0, landing));
                }
                if (cleanupIndex >= 0)
                {
                    _cleanups.RemoveAt(_cleanups.Count - 1);
                    _scopes[_currentFunction].Add(new NativeExceptionScope(bodyBegin, catchEnd, -cleanupIndex - 1, 0));
                    CallCleanup(cleanupIndex);
                }
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
            default:
                throw new ArgumentException("Unknown native statement kind.", nameof(statement));
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
            var size = Size(variables[i].Type);
            if (IsIndirect(variables[i].Type))
            {
                _code.Address(OutSlot(i));
                _code.Load64(Slot(i), destinationRegister: true);
                _code.CopyMemory(size);
                continue;
            }
            _code.Load64(Slot(i));
            _code.Bytes(0x48, 0x89, 0xC2); // mov rdx,rax
            _code.Load64(OutSlot(i));
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
                if (IsWide(constant.Type))
                {
                    _code.Bytes(0x48, 0xB8);
                    _code.Int64(constant.Value);
                }
                else
                {
                    _code.Byte(0xB8);
                    _code.Int32(checked((int)constant.Value));
                }
                break;
            case BoundRead read:
                LoadValue(_program.Functions[_currentFunction].Variables[read.Variable].IsOut
                    ? OutSlot(read.Variable) : Slot(read.Variable), read.Type);
                break;
            case BoundFieldRead fieldRead:
                Expression(fieldRead.Receiver, spill);
                var field = _program.Structures[(int)fieldRead.Receiver.Type - 100].Fields[fieldRead.Field];
                if (IsIndirect(fieldRead.Receiver.Type))
                {
                    if (IsIndirect(field.Type))
                    {
                        _code.PointerOffset(field.Offset);
                    }
                    else
                    {
                        _code.LoadPointerField(field.Offset, field.Size);
                    }
                    break;
                }
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
                else if (field.Size == 4)
                {
                    _code.Bytes(0x89, 0xC0); // clear adjacent aggregate bits above the field
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
                        if (IsWide(unary.Type))
                        {
                            _code.Byte(0x48);
                        }
                        _code.Bytes(0xF7, 0xD8);
                        RuntimeBranch(0x80, 0);
                        break;
                    case TokenKind.Tilde:
                        if (IsWide(unary.Type))
                        {
                            _code.Byte(0x48);
                        }
                        _code.Bytes(0xF7, 0xD0);
                        break;
                    case TokenKind.Bang:
                        _code.Bytes(0x83, 0xF0, 0x01);
                        break;
                }
                break;
            case BoundCast cast:
                Expression(cast.Operand, spill);
                Cast(cast.Operand.Type, cast.Type);
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
                if (IsWide(binary.Left.Type))
                {
                    _code.Bytes(0x48, 0x89, 0xC1); // mov rcx,rax
                }
                else
                {
                    _code.Bytes(0x89, 0xC1); // mov ecx,eax
                }
                LoadValue(Spill(spill), binary.Left.Type);
                Binary(binary.Operator, binary.Left.Type);
                break;
            case BoundCall call:
                var argumentsBase = spill + 1;
                for (var i = 0; i < call.Arguments.Length; i++)
                {
                    if (call.Arguments[i] is BoundOutArgument)
                    {
                        var scratch = Spill(argumentsBase + call.Arguments.Length + i);
                        ZeroStruct(scratch, IsIndirect(call.Arguments[i].Type) ? Size(call.Arguments[i].Type) : 8);
                        _code.Address(scratch);
                        _code.Store64(Spill(argumentsBase + i));
                    }
                    else
                    {
                        Expression(call.Arguments[i], argumentsBase + (call.Arguments.Length * 2));
                        StoreValue(Spill(argumentsBase + i), call.Arguments[i].Type);
                    }
                }
                var hidden = IsIndirect(call.Type) ? 1 : 0;
                for (var i = Math.Max(0, 4 - hidden); i < call.Arguments.Length; i++)
                {
                    if (call.Arguments[i] is BoundOutArgument)
                    {
                        _code.Load64(Spill(argumentsBase + i));
                    }
                    else
                    {
                        LoadValue(Spill(argumentsBase + i), call.Arguments[i].Type);
                    }
                    _code.StoreOutgoing64(32 + (8 * (i + hidden - 4)));
                }
                for (var i = 0; i < Math.Min(4 - hidden, call.Arguments.Length); i++)
                {
                    if (call.Arguments[i] is BoundOutArgument)
                    {
                        _code.Load64(Spill(argumentsBase + i));
                    }
                    else
                    {
                        LoadValue(Spill(argumentsBase + i), call.Arguments[i].Type);
                    }
                    _code.MoveArgument(i + hidden,
                        call.Arguments[i] is BoundOutArgument || IsStruct(call.Arguments[i].Type) || IsWide(call.Arguments[i].Type));
                }
                if (hidden != 0)
                {
                    _code.Address(Spill(spill));
                    _code.MoveArgument(0, true);
                }
                if (_program.Functions[call.Function].IsExternal)
                {
                    if (_nativeImports is null || !_nativeImports.TryGetValue(call.Function, out var import))
                    {
                        throw new InvalidOperationException("External call has no verified native import binding.");
                    }
                    _nativeImportFixups.Add(new NativeImportFixup(_code.ImportCall(), import.DllName, import.ExportName));
                }
                else
                {
                    _calls.Add((_code.RelativeCall(), call.Function));
                }
                if (hidden != 0)
                {
                    _code.Address(Spill(spill));
                }
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
                                var scratch = Spill(argumentsBase + call.Arguments.Length + i);
                                var destination = _program.Functions[_currentFunction].Variables[output.Variable].IsOut
                                    ? OutSlot(output.Variable) : Slot(output.Variable);
                                if (IsIndirect(output.Type))
                                {
                                    _code.Address(scratch);
                                    CopyToFrame(destination, Size(output.Type));
                                    continue;
                                }
                                if (output.Type is PrimitiveType.Bool or PrimitiveType.Char16)
                                {
                                    _code.LoadUnsigned(scratch, output.Type == PrimitiveType.Bool ? 1 : 2);
                                }
                                else
                                {
                                    LoadValue(scratch, output.Type);
                                }
                                StoreValue(destination, output.Type);
                            }
                        }
                        _code.Bytes([0xB8, 0x01, 0x00, 0x00, 0x00]);
                        _code.PatchRelative(skip, _code.Position);
                    }
                }
                break;
        }
    }

    private void Cast(PrimitiveType source, PrimitiveType target)
    {
        if (source == PrimitiveType.Int32 && IsWide(target))
        {
            _code.Bytes(0x48, 0x63, 0xC0); // movsxd rax,eax
        }
        if (target == PrimitiveType.Char16 && source != PrimitiveType.Char16)
        {
            if (IsWide(source))
            {
                _code.Byte(0x48);
            }
            _code.Byte(0x3D);
            _code.Int32(ushort.MaxValue);
            RuntimeBranch(0x87, 0);
        }
        else if ((target is PrimitiveType.UInt32 or PrimitiveType.UInt64
            && source is PrimitiveType.Int32 or PrimitiveType.Int64)
            || (target == PrimitiveType.Int32 && source is PrimitiveType.UInt32 or PrimitiveType.UInt64)
            || (target == PrimitiveType.Int64 && source == PrimitiveType.UInt64))
        {
            if (IsWide(source) || IsWide(target))
            {
                _code.Byte(0x48);
            }
            _code.Bytes(0x85, 0xC0);
            RuntimeBranch(0x88, 0);
        }
        if (IsWide(source) && target is PrimitiveType.Int32 or PrimitiveType.UInt32)
        {
            if (target == PrimitiveType.Int32)
            {
                _code.Bytes(0x48, 0x63, 0xC8); // movsxd rcx,eax
            }
            else
            {
                _code.Bytes(0x89, 0xC1); // mov ecx,eax; zero-extends rcx
            }
            _code.Bytes(0x48, 0x39, 0xC8); // cmp rax,rcx
            RuntimeBranch(0x85, 0);
        }
    }

    [SuppressMessage("Style", "IDE0010:Populate switch", Justification = "The verifier restricts binary operators to the supported native profile.")]
    [SuppressMessage("Style", "IDE0072:Populate switch", Justification = "The verifier excludes other token kinds before reaching the comparison encoder.")]
    private void Binary(TokenKind op, PrimitiveType type)
    {
        if (IsWide(type))
        {
            BinaryWide(op, type == PrimitiveType.UInt64);
            return;
        }
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

    [SuppressMessage("Style", "IDE0010:Populate switch", Justification = "The verifier restricts binary operators to the supported native profile.")]
    [SuppressMessage("Style", "IDE0072:Populate switch", Justification = "The verifier excludes unsupported token kinds before reaching this encoder.")]
    private void BinaryWide(TokenKind op, bool unsigned)
    {
        switch (op)
        {
            case TokenKind.Plus:
            case TokenKind.Minus:
                _code.Bytes(0x48, op == TokenKind.Plus ? (byte)0x01 : (byte)0x29, 0xC8);
                RuntimeBranch(unsigned ? (byte)0x82 : (byte)0x80, 0);
                break;
            case TokenKind.Star:
                if (unsigned)
                {
                    _code.Bytes(0x48, 0xF7, 0xE1); // mul rcx; the upper product must be zero
                    _code.Bytes(0x48, 0x85, 0xD2);
                    RuntimeBranch(0x85, 0);
                }
                else
                {
                    _code.Bytes(0x48, 0x0F, 0xAF, 0xC1);
                    RuntimeBranch(0x80, 0);
                }
                break;
            case TokenKind.Slash:
            case TokenKind.Percent:
                _code.Bytes(0x48, 0x85, 0xC9);
                RuntimeBranch(0x84, 1);
                if (unsigned)
                {
                    _code.Bytes(0x31, 0xD2, 0x48, 0xF7, 0xF1); // xor edx,edx; div rcx
                }
                else
                {
                    _code.Bytes(0x48, 0xBA);
                    _code.Int64(long.MinValue);
                    _code.Bytes(0x48, 0x39, 0xD0); // cmp rax,rdx
                    var normal = _code.JumpIf(0x85);
                    _code.Bytes(0x48, 0x83, 0xF9, 0xFF); // cmp rcx,-1
                    var normalAgain = _code.JumpIf(0x85);
                    if (op == TokenKind.Slash)
                    {
                        _runtimeCalls.Add((_code.RelativeCall(), 0));
                    }
                    else
                    {
                        _code.Bytes(0x31, 0xC0); // min % -1 is zero
                        var done = _code.Jump();
                        _code.PatchRelative(normal, _code.Position);
                        _code.PatchRelative(normalAgain, _code.Position);
                        _code.Bytes(0x48, 0x99, 0x48, 0xF7, 0xF9); // cqo; idiv rcx
                        _code.Bytes(0x48, 0x89, 0xD0);
                        _code.PatchRelative(done, _code.Position);
                        break;
                    }
                    _code.PatchRelative(normal, _code.Position);
                    _code.PatchRelative(normalAgain, _code.Position);
                    _code.Bytes(0x48, 0x99, 0x48, 0xF7, 0xF9);
                }
                if (op == TokenKind.Percent)
                {
                    _code.Bytes(0x48, 0x89, 0xD0); // mov rax,rdx
                }
                break;
            case TokenKind.Ampersand:
            case TokenKind.Pipe:
            case TokenKind.Caret:
                _code.Bytes(0x48, op switch {
                    TokenKind.Ampersand => 0x21,
                    TokenKind.Pipe => 0x09,
                    _ => 0x31,
                }, 0xC8);
                break;
            default:
                _code.Bytes(0x48, 0x39, 0xC8);
                _code.Bytes(0x0F, (byte)(op switch {
                    TokenKind.EqualsEquals => 0x94,
                    TokenKind.BangEquals => 0x95,
                    TokenKind.Less => unsigned ? 0x92 : 0x9C,
                    TokenKind.LessEquals => unsigned ? 0x96 : 0x9E,
                    TokenKind.Greater => unsigned ? 0x97 : 0x9F,
                    TokenKind.GreaterEquals => unsigned ? 0x93 : 0x9D,
                    _ => throw new InvalidOperationException(),
                }), 0xC0);
                _code.Bytes(0x0F, 0xB6, 0xC0);
                break;
        }
    }
}

internal sealed class MachineCode
{
    private readonly List<byte> _bytes = new();
    internal bool UseFramePointer { get; set; }
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
    internal void Int64(long value)
    {
        for (var i = 0; i < 8; i++)
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
    internal int ImportJump()
    {
        Bytes(0xFF, 0x25); // jmp qword ptr [rip+disp32]
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
        if (UseFramePointer)
        {
            Bytes(0x8B, 0x85);
        }
        else
        {
            Bytes(0x8B, 0x84, 0x24);
        }
        Int32(offset);
    }
    internal void Load64(int offset, bool destinationRegister = false)
    {
        if (destinationRegister)
        {
            if (UseFramePointer)
            {
                Bytes(0x48, 0x8B, 0x95);
            }
            else
            {
                Bytes(0x48, 0x8B, 0x94, 0x24);
            }
            Int32(offset);
        }
        else
        {
            Byte(0x48);
            Load(offset);
        }
    }
    internal void LoadUnsigned(int offset, int width)
    {
        Bytes(0x0F, width == 1 ? (byte)0xB6 : (byte)0xB7, UseFramePointer ? (byte)0x85 : (byte)0x84);
        if (!UseFramePointer)
        {
            Byte(0x24);
        }
        Int32(offset);
    }
    internal void Address(int offset)
    {
        Bytes(0x48, 0x8D, UseFramePointer ? (byte)0x85 : (byte)0x84);
        if (!UseFramePointer)
        {
            Byte(0x24);
        }
        Int32(offset);
    }
    internal void AddressToRdx(int offset)
    {
        Bytes(0x48, 0x8D, UseFramePointer ? (byte)0x95 : (byte)0x94);
        if (!UseFramePointer)
        {
            Byte(0x24);
        }
        Int32(offset);
    }
    internal void PointerOffset(int offset)
    {
        Bytes(0x48, 0x8D, 0x80);
        Int32(offset);
    }
    internal void LoadPointerField(int offset, int width)
    {
        if (width == 8)
        {
            Byte(0x48);
        }
        if (width is 1 or 2)
        {
            Bytes(0x0F, width == 1 ? (byte)0xB6 : (byte)0xB7);
        }
        else
        {
            Byte(0x8B);
        }
        Byte(0x80);
        Int32(offset);
    }
    internal void CopyMemory(int size)
    {
        var at = 0;
        while (at < size)
        {
            var remaining = size - at;
            var width = remaining >= 8 ? 8 : remaining >= 4 ? 4 : remaining >= 2 ? 2 : 1;
            if (width == 8)
            {
                Byte(0x48);
            }
            else if (width == 2)
            {
                Byte(0x66);
            }
            if (width == 1)
            {
                Bytes(0x0F, 0xB6, 0x88);
            }
            else
            {
                Bytes(0x8B, 0x88);
            }
            Int32(at);
            if (width == 8)
            {
                Byte(0x48);
            }
            else if (width == 2)
            {
                Byte(0x66);
            }
            Bytes(width == 1 ? (byte)0x88 : (byte)0x89, 0x8A);
            Int32(at);
            at += width;
        }
    }
    internal void Store(int offset)
    {
        if (UseFramePointer)
        {
            Bytes(0x89, 0x85);
        }
        else
        {
            Bytes(0x89, 0x84, 0x24);
        }
        Int32(offset);
    }
    internal void StoreOutgoing64(int offset)
    {
        Bytes(0x48, 0x89, 0x84, 0x24);
        Int32(offset);
    }
    internal void Store64(int offset)
    {
        Byte(0x48);
        Store(offset);
    }
    internal void StoreField(int offset, int width)
    {
        if (width == 8)
        {
            Byte(0x48);
        }
        if (width == 2)
        {
            Byte(0x66);
        }
        if (width == 1)
        {
            Bytes(0x88, UseFramePointer ? (byte)0x85 : (byte)0x84);
        }
        else
        {
            Bytes(0x89, UseFramePointer ? (byte)0x85 : (byte)0x84);
        }
        if (!UseFramePointer)
        {
            Byte(0x24);
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
        var register = argument switch {
            0 or 3 => 0x8C,
            1 => 0x94,
            _ => 0x84,
        };
        Bytes(0x89, (byte)(register + (UseFramePointer ? 1 : 0)));
        if (!UseFramePointer)
        {
            Byte(0x24);
        }
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
