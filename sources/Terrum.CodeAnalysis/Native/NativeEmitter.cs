// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Numerics;
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
        NativeRuntimePolicy runtimePolicy, CancellationToken cancellationToken = default) =>
        EmitWithImportUsage(program, bindings, runtimePolicy, cancellationToken).Image;

    internal static (byte[] Image, ImmutableArray<NativeImportBinding> Imports)
        EmitWithImportUsage(BoundProgram program, IReadOnlyList<NativeImportBinding> bindings,
            NativeRuntimePolicy runtimePolicy, CancellationToken cancellationToken = default)
    {
        var (code, selected) = CompileWithImports(program, bindings, runtimePolicy, cancellationToken);
        return (PeImage.Build(code), UsedImports(code, selected));
    }

    /// <summary>Emits callable native code with verified external declaration bindings for graph JIT callers.</summary>
    internal static NativeCode EmitCodeWithImports(BoundProgram program, IReadOnlyList<NativeImportBinding> bindings,
        NativeRuntimePolicy runtimePolicy, CancellationToken cancellationToken = default) =>
        CompileWithImports(program, bindings, runtimePolicy, cancellationToken).Code;

    private static (NativeCode Code, Dictionary<int, NativeImportBinding> Selected)
        CompileWithImports(BoundProgram program, IReadOnlyList<NativeImportBinding> bindings,
            NativeRuntimePolicy runtimePolicy, CancellationToken cancellationToken)
    {
        if (runtimePolicy is not (NativeRuntimePolicy.FailFast or NativeRuntimePolicy.CheckedExceptions))
        {
            throw new NotSupportedException("Unsupported native runtime policy.");
        }
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(program);
        ArgumentNullException.ThrowIfNull(bindings);
        if (program.RequiresSourceGrant)
        {
            throw new NotSupportedException("Native modules do not support Host.Source requirements.");
        }
        SemanticVerifier.VerifyReferences(program);
        if (program.RuntimePolicy != (BoundRuntimePolicy)runtimePolicy)
        {
            throw new ArgumentException("Native and verified checked-failure policies differ.", nameof(program));
        }
        var selected = SelectImports(program, bindings, cancellationToken);
        var managedNodes = UsesManagedNodes(program);
        var compiler = new PeCompiler(program, cancellationToken, managedNodes, imports: selected);
        return (compiler.Compile(), selected);
    }

    private static Dictionary<int, NativeImportBinding> SelectImports(BoundProgram program,
        IReadOnlyList<NativeImportBinding> bindings, CancellationToken cancellationToken)
    {
        if (bindings.Count is < 1 or > 1024)
        {
            throw new ArgumentException("Native consumers require 1 to 1024 explicit import bindings.", nameof(bindings));
        }
        var externals = new Dictionary<string, int>(bindings.Count, StringComparer.Ordinal);
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
        var selected = new Dictionary<int, NativeImportBinding>(bindings.Count);
        var importNames = new HashSet<string>(bindings.Count, StringComparer.Ordinal);
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
        return selected;
    }

    private static ImmutableArray<NativeImportBinding> UsedImports(NativeCode code,
        Dictionary<int, NativeImportBinding>? bindings)
    {
        if (bindings is null)
        {
            return [];
        }
        var used = new HashSet<int>();
        foreach (var fixup in code.NativeImports)
        {
            if (fixup.DeclarationIndex < 0)
            {
                continue;
            }
            if (!bindings.TryGetValue(fixup.DeclarationIndex, out var binding)
                || !fixup.DllName.Equals(binding.DllName, StringComparison.OrdinalIgnoreCase)
                || fixup.ExportName != binding.ExportName)
            {
                throw new InvalidOperationException("Emitted native import has no verified declaration binding.");
            }
            _ = used.Add(fixup.DeclarationIndex);
        }
        return [.. used.Select(index => bindings[index]).OrderBy(static binding => binding.FunctionName, StringComparer.Ordinal)];
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
        NativeRuntimePolicy runtimePolicy, CancellationToken cancellationToken = default) =>
        EmitLibraryCore(program, exports, null, runtimePolicy, cancellationToken).Image;

    internal static (byte[] Image, NativeCode Code) EmitSourceGrantLibraryForTest(
        BoundProgram program, IReadOnlyList<string> exports, NativeRuntimePolicy runtimePolicy)
    {
        ArgumentNullException.ThrowIfNull(program);
        SemanticVerifier.VerifyLibrary(program);
        var dependent = NativeSourceGrantExperiment.Classify(program);
        NativeCode? emitted = null;
        var result = EmitLibraryCore(program, exports, null, runtimePolicy, default, dependent,
            code => emitted = code);

        return (result.Image, emitted ?? throw new InvalidOperationException("No experiment code was emitted."));
    }

    /// <summary>Emits a library whose verified external declarations have explicit DLL import bindings.</summary>
    /// <remarks>Bindings specify ABI identities, not provider compatibility; callers must validate dependency contracts.</remarks>
    public static byte[] EmitLibraryWithImports(BoundProgram program, IReadOnlyList<string> exports,
        IReadOnlyList<NativeImportBinding> bindings, NativeRuntimePolicy runtimePolicy,
        CancellationToken cancellationToken = default) =>
        EmitLibraryWithImportUsage(program, exports, bindings, runtimePolicy, cancellationToken).Image;

    internal static (byte[] Image, ImmutableArray<NativeImportBinding> Imports)
        EmitLibraryWithImportUsage(BoundProgram program, IReadOnlyList<string> exports,
            IReadOnlyList<NativeImportBinding> bindings, NativeRuntimePolicy runtimePolicy,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        return EmitLibraryCore(program, exports, bindings, runtimePolicy, cancellationToken);
    }

    private static (byte[] Image, ImmutableArray<NativeImportBinding> Imports)
        EmitLibraryCore(BoundProgram program, IReadOnlyList<string> exports,
        IReadOnlyList<NativeImportBinding>? bindings, NativeRuntimePolicy runtimePolicy, CancellationToken cancellationToken,
        bool[]? sourceDependent = null, Action<NativeCode>? captureCode = null)
    {
        if (runtimePolicy is not (NativeRuntimePolicy.FailFast or NativeRuntimePolicy.CheckedExceptions))
        {
            throw new NotSupportedException("Unsupported native runtime policy.");
        }
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(program);
        if (program.RequiresSourceGrant && !program.IsInvocationSourceBound && sourceDependent is null)
        {
            throw new NotSupportedException("An invocation-scoped Host.Source grant must be bound through NativeJit.");
        }
        if (bindings is null)
        {
            SemanticVerifier.VerifyLibrary(program);
        }
        else
        {
            SemanticVerifier.VerifyLibraryReferences(program);
        }
        if (program.RuntimePolicy != (BoundRuntimePolicy)runtimePolicy)
        {
            throw new ArgumentException("Native and verified checked-failure policies differ.", nameof(program));
        }
        ArgumentNullException.ThrowIfNull(exports);
        if (!program.Buffers.IsEmpty && sourceDependent is null)
        {
            throw new NotSupportedException("Native DLLs do not expose host buffer capabilities.");
        }
        if (exports.Count is < 1 or > 1024)
        {
            throw new ArgumentException("A native library requires 1 to 1024 explicit exports.", nameof(exports));
        }
        var selected = new List<(string Name, int Index)>(exports.Count);
        var names = new HashSet<string>(StringComparer.Ordinal);
        var managedExports = UsesManagedNodes(program);
        var functionIndices = new Dictionary<string, int>(program.Functions.Length, StringComparer.Ordinal);
        for (var index = 0; index < program.Functions.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            functionIndices.Add(program.Functions[index].Name, index);
        }
        var structureVisibility = new Dictionary<string, bool>(program.Structures.Length,
            StringComparer.Ordinal);
        var publicLayouts = new Dictionary<PrimitiveType, bool>(program.Structures.Length);
        foreach (var structure in program.Structures)
        {
            cancellationToken.ThrowIfCancellationRequested();
            structureVisibility.Add(structure.Name, !structure.IsPrivate && !structure.IsInternal);
        }
        foreach (var name in exports)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrEmpty(name) || !names.Add(name)
                || name.Length > 256 || name.Any(static character => character is < '!' or > '~'))
            {
                throw new ArgumentException("Export names must be unique printable ASCII names of at most 256 bytes.", nameof(exports));
            }
            if (!functionIndices.TryGetValue(name, out var index)
                || !Exportable(program, program.Functions[index], managedExports,
                    structureVisibility, publicLayouts))
            {
                throw new ArgumentException($"'{name}' is not an accessible native library export.", nameof(exports));
            }
            selected.Add((name, index));
        }
        selected.Sort(static (left, right) => StringComparer.Ordinal.Compare(left.Name, right.Name));
        var imports = bindings is null ? null : SelectImports(program, bindings, cancellationToken);
        var compiler = new PeCompiler(program, cancellationToken, managedExports, library: true, imports: imports,
            sourceDependent: sourceDependent);
        var code = compiler.Compile();
        captureCode?.Invoke(code);
        return (PeImage.BuildLibrary(code, [.. selected]), UsedImports(code, imports));
    }

    internal static bool UsesManagedNodes(BoundProgram program) => !program.Classes.IsEmpty
        || program.Functions.Any(static function =>
        function.Intrinsic is >= BoundIntrinsic.NodeTryCreate and <= BoundIntrinsic.NodeCollect
        || ManagedClassTypes.IsManaged(function.ReturnType)
        || function.Variables.Any(static variable => ManagedClassTypes.IsManaged(variable.Type)));

    private static bool Exportable(BoundProgram program, BoundFunction function, bool managedExports,
        IReadOnlyDictionary<string, bool> structureVisibility, Dictionary<PrimitiveType, bool> publicLayouts)
    {
        if (function.IsPrivate || function.IsInternal || function.IsExternal)
        {
            return false;
        }
        if (function.DeclaringStruct is string owner)
        {
            if (structureVisibility.TryGetValue(owner, out var isPublic))
            {
                if (!isPublic)
                {
                    return false;
                }
            }
            else if (!Binder.TryPrimitiveType(owner, out _))
            {
                return false;
            }
        }
        if (!PublicLayout(function.ReturnType)
            || (managedExports && !ManagedExportShape.Supported(function, program.Structures)))
        {
            return false;
        }
        for (var i = 0; i < function.ParameterCount; i++)
        {
            var parameter = function.Variables[i + (function.IsConstructor ? 1 : 0)];
            if (!PublicLayout(parameter.Type))
            {
                return false;
            }
        }
        return true;

        bool PublicLayout(PrimitiveType type)
        {
            if (publicLayouts.TryGetValue(type, out var isPublic))
            {
                return isPublic;
            }
            if (ManagedClassTypes.IsManaged(type) || TokenSpanContract.IsDependent(type, program.Structures))
            {
                isPublic = false;
            }
            else if ((int)type < 100)
            {
                isPublic = true;
            }
            else
            {
                var structure = program.Structures[(int)type - 100];
                isPublic = !structure.IsPrivate && !structure.IsInternal;
                var fields = structure.Fields;
                for (var index = 0; isPublic && index < fields.Length; index++)
                {
                    var field = fields[index];
                    isPublic = !field.IsPrivate && !field.IsInternal && PublicLayout(field.Type);
                }
            }
            publicLayouts.Add(type, isPublic);
            return isPublic;
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
        ArgumentNullException.ThrowIfNull(program);
        if (program.RequiresSourceGrant && !program.IsInvocationSourceBound)
        {
            throw new NotSupportedException("An invocation-scoped Host.Source grant must be bound through NativeJit.");
        }
        SemanticVerifier.Verify(program);
        if (program.RuntimePolicy != (BoundRuntimePolicy)runtimePolicy)
        {
            throw new ArgumentException("Native and verified checked-failure policies differ.", nameof(program));
        }
        var managedNodes = UsesManagedNodes(program);
        return new PeCompiler(program, cancellationToken, managedNodes).Compile();
    }
}

internal sealed partial class PeCompiler
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
    private readonly int[] _edgeCopyBases;
    private readonly int[][] _liveSlots;
    private readonly int[][] _initializationFlags;
    private readonly int[][] _constructedFieldSlots;
    private readonly int[] _replacementSlots;
    private readonly int[] _cleanupArgumentSlots;
    private readonly List<(int Offset, int Target)> _calls = new();
    private readonly List<(int Offset, int Target)> _runtimeCalls = new();
    private readonly List<(int Offset, int Target)> _cleanupCalls = [];
    private readonly List<(BoundBlock Body, int Function, int Start, int End)> _funclets = [];
    private readonly Dictionary<int, List<(int Value, int Live, PrimitiveType Type)>> _stagedOwners = [];
    private readonly Dictionary<int, List<int>> _managedStagedRoots = [];
    private readonly HashSet<int> _unwindRootFrames = [];
    private readonly List<int> _imports = new();
    private readonly List<NativeImportFixup> _nativeImportFixups = [];
    private readonly List<NativeDataFixup> _dataFixups = [];
    private readonly int[] _bufferOffsets;
    private readonly bool[]? _sourceDependent;
    private readonly int[] _sourceSlots;
    private readonly byte[] _data;
    private readonly bool _managedNodes;
    private readonly int _managedRuntimeOffset;
    private readonly int _hostDataLength;
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
    private readonly List<NormalExit> _normalExits = [];
    private readonly List<LoopTarget> _loopTargets = [];

    private sealed record NormalExit(int Cleanup, ImmutableArray<int> RetiredRoots);

    private sealed class LoopTarget(int exitDepth)
    {
        internal int ExitDepth { get; } = exitDepth;
        internal List<int> Breaks { get; } = [];
        internal List<int> Continues { get; } = [];
    }

    internal PeCompiler(BoundProgram program, CancellationToken cancellationToken, bool managedNodes, bool library = false,
        IReadOnlyDictionary<int, NativeImportBinding>? imports = null, bool[]? sourceDependent = null)
    {
        _program = program;
        _sourceDependent = sourceDependent;
        _sourceSlots = sourceDependent is null ? [] : new int[program.Functions.Length];
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
        _edgeCopyBases = new int[program.Functions.Length];
        _liveSlots = new int[program.Functions.Length][];
        _initializationFlags = new int[program.Functions.Length][];
        _managedRootFrames = new int[program.Functions.Length];
        _managedRootFlags = new Dictionary<int, int>[program.Functions.Length];
        for (var index = 0; index < _managedRootFlags.Length; index++)
        {
            _managedRootFlags[index] = [];
        }
        _constructedFieldSlots = new int[program.Functions.Length][];
        _replacementSlots = new int[program.Functions.Length];
        _cleanupArgumentSlots = new int[program.Functions.Length];
        _bufferOffsets = sourceDependent is null ? new int[program.Buffers.Length] : [];
        if (sourceDependent is not null)
        {
            // Invocation storage has no image data, address fixups, or retained runtime state.
            _data = [];
            _managedRuntimeOffset = -1;
            return;
        }
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
        _hostDataLength = writableAt;
        _managedNodes = managedNodes;
        _managedRuntimeOffset = _managedNodes ? (writableAt + 7) & ~7 : -1;
        _data = new byte[_managedNodes ? _managedRuntimeOffset + 24 : writableAt];
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
        EmitManagedCollector();
        EmitManagedDescriptors();

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
        var functionCount = checked((_library ? 0 : 2) + _failureStarts.Length + _funclets.Count
            + (_managedNodes ? 1 : 0));
        var sourceFunctionOffsets = ImmutableArray.CreateBuilder<int>(_program.Functions.Length);
        for (var i = 0; i < _program.Functions.Length; i++)
        {
            var function = _program.Functions[i];
            sourceFunctionOffsets.Add(function.IsExternal ? -1 : _starts[i]);
            if (!function.IsExternal)
            {
                functionCount = checked(functionCount + 1);
            }
        }
        var functions = ImmutableArray.CreateBuilder<NativeFunctionRange>(functionCount);
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
        if (_managedNodes)
        {
            functions.Add(new NativeFunctionRange(_managedCollectorStart, _managedCollectorEnd, 120));
        }

        return new NativeCode(_code.ToArray(), _entryStart,
            functions.MoveToImmutable(),
            [.. _imports]) {
            AotEntryPoint = processEntry,
            Data = _data,
            ReadOnlyDataLength = _readOnlyDataLength,
            BufferOffsets = [.. _bufferOffsets],
            SourceFunctionOffsets = sourceFunctionOffsets.MoveToImmutable(),
            DataFixups = [.. _dataFixups],
            NativeImports = [.. _nativeImportFixups],
            ManagedRuntimeOffset = _managedRuntimeOffset,
            HostDataLength = _hostDataLength,
        };
    }

    private void PrepareFrames()
    {
        var hasIndirectCleanup = false;
        for (var type = 0; type < _program.Structures.Length; type++)
        {
            if (_program.Structures[type].CleanupMethod is not null
                && IsIndirect((PrimitiveType)(100 + type)))
            {
                hasIndirectCleanup = true;
                break;
            }
        }
        for (var i = 0; i < _program.Functions.Length; i++)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            var function = _program.Functions[i];
            if (function.IsExternal)
            {
                continue;
            }
            var maximumArguments = 0;
            var needsReturnSnapshot = false;
            var spillSlots = StatementSpills(function.Body, ref maximumArguments, ref needsReturnSnapshot,
                _checkedExceptions && _managedNodes);
            if (function.IndexedBody is TrRmScalarGraph indexed)
            {
                foreach (var block in indexed.Blocks)
                {
                    foreach (var operation in block.Operations)
                    {
                        spillSlots = Math.Max(spillSlots, StatementSpills(operation, ref maximumArguments));
                    }
                }
            }
            if (function.Intrinsic == BoundIntrinsic.ClassTryCreate)
            {
                maximumArguments = Math.Max(maximumArguments, function.ParameterCount);
            }
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
            if (_sourceDependent?[i] == true)
            {
                _sourceSlots[i] = at;
                at += 16;
            }
            var slots = new int[function.Variables.Length];
            for (var variable = 0; variable < slots.Length; variable++)
            {
                if ((IsIndirect(function.Variables[variable].Type)
                    || function.Variables[variable].Type is PrimitiveType.ReadonlyInt32BufferView
                        or PrimitiveType.MutableInt32BufferView)
                    && !function.Variables[variable].IsOut)
                {
                    at = (at + 15) & ~15;
                }
                slots[variable] = at;
                at += (IsIndirect(function.Variables[variable].Type)
                    || function.Variables[variable].Type is PrimitiveType.ReadonlyInt32BufferView
                        or PrimitiveType.MutableInt32BufferView)
                    && !function.Variables[variable].IsOut ? 16 : 8;
                at = (at + 7) & ~7;
            }
            _slots[i] = slots;
            _spillBases[i] = (at + 15) & ~15;
            _outBases[i] = _spillBases[i] + (16 * spillSlots);
            _edgeCopyBases[i] = _outBases[i] + (16 * outParameters);
            var edgeParameters = 0;
            if (function.IndexedBody is not null)
            {
                foreach (var block in function.IndexedBody.Blocks)
                {
                    edgeParameters = Math.Max(edgeParameters, block.Parameters.Length);
                }
            }
            _returnSlots[i] = _edgeCopyBases[i] + (16 * edgeParameters);
            var hasOutputParameters = false;
            var parameterOffset = function.IsConstructor ? 1 : 0;
            for (var parameter = parameterOffset;
                parameter < function.ParameterCount + parameterOffset; parameter++)
            {
                hasOutputParameters |= function.Variables[parameter].IsOut;
            }
            var snapshotsOutputReturn = hasOutputParameters
                && function.ReturnType is not (PrimitiveType.Bool or PrimitiveType.Void);
            // Publishing outputs uses the return registers, so preserve non-Boolean results first.
            var required = _returnSlots[i] + (IsIndirect(function.ReturnType) || snapshotsOutputReturn ? 8L : 0L);
            if (needsReturnSnapshot)
            {
                required = ((required + 15) & ~15L) + 16;
            }
            var live = new int[function.Variables.Length];
            Array.Fill(live, -1);
            var hasUnique = false;
            for (var variable = 0; variable < function.Variables.Length; variable++)
            {
                if (IsUnique(function.Variables[variable].Type)
                    && !(variable == 0 && function.DeclaringStruct is not null && !function.IsStatic))
                {
                    live[variable] = checked((int)required);
                    required += 8;
                    hasUnique = true;
                }
            }
            _liveSlots[i] = live;
            var initializationTests = InitializationTestVariables(function);
            var initializationFlags = new int[function.Variables.Length];
            Array.Fill(initializationFlags, -1);
            for (var variable = 0; variable < function.Variables.Length; variable++)
            {
                var slot = function.Variables[variable];
                if (initializationTests.Contains(variable)
                    && !ManagedClassTypes.IsManaged(slot.Type) && !IsUnique(slot.Type))
                {
                    initializationFlags[variable] = checked((int)required);
                    required += 4;
                }
            }
            _initializationFlags[i] = initializationFlags;
            var constructed = function.IsConstructor
                ? new int[_program.Structures[(int)function.ReturnType - 100].Fields.Length] : [];
            Array.Fill(constructed, -1);
            for (var field = 0; field < constructed.Length; field++)
            {
                if (IsUnique(_program.Structures[(int)function.ReturnType - 100].Fields[field].Type))
                {
                    constructed[field] = checked((int)required);
                    required += 8;
                }
            }
            _constructedFieldSlots[i] = constructed;
            _replacementSlots[i] = checked((int)((required + 15) & ~15L));
            if (hasUnique)
            {
                required = _replacementSlots[i] + 16L;
            }
            _cleanupArgumentSlots[i] = -1;
            if (hasIndirectCleanup)
            {
                _cleanupArgumentSlots[i] = checked((int)((required + 15) & ~15L));
                required = _cleanupArgumentSlots[i] + 16L;
            }
            required = PrepareManagedRoots(i, function, spillSlots, needsReturnSnapshot, required);
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
            _normalExits.Clear();
            _loopTargets.Clear();
            _code.Byte(0x55); // push rbp
            Prolog(_frame - 8);
            _code.Bytes(0x48, 0x89, 0xE5); // mov rbp,rsp
            _code.UseFramePointer = true;
            var sourceHidden = _sourceDependent?[i] == true ? 2 : 0;
            if (sourceHidden != 0)
            {
                // RCX/RDX are volatile; preserve the complete context before emitting any calls.
                _code.StoreRegister(0, _sourceSlots[i], true);
                _code.StoreRegister(1, _sourceSlots[i] + 8, false);
            }
            foreach (var live in _liveSlots[i])
            {
                if (live >= 0)
                {
                    _code.Clear(live);
                }
            }
            foreach (var flag in _initializationFlags[i])
            {
                if (flag >= 0)
                {
                    _code.Clear(flag);
                }
            }
            foreach (var field in _constructedFieldSlots[i])
            {
                if (field >= 0)
                {
                    _code.Clear(field);
                }
            }
            var hiddenReturn = IsIndirect(function.ReturnType) ? 1 : 0;
            var hidden = hiddenReturn + sourceHidden;
            if (hiddenReturn != 0)
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
                if (IsIndirect(type) && !function.Variables[slot].IsOut
                    && !(slot == 0 && IsBorrowedReceiver()))
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
            for (var argument = 0; argument < function.ParameterCount; argument++)
            {
                var slot = argument + (function.IsConstructor ? 1 : 0);
                if (_liveSlots[i][slot] >= 0 && !function.Variables[slot].IsOut)
                {
                    _code.Set(_liveSlots[i][slot]);
                }
                var initializationFlag = _initializationFlags[i][slot];
                if (initializationFlag >= 0 && !function.Variables[slot].IsOut)
                {
                    _code.Set(initializationFlag);
                }
            }
            if (function.Intrinsic is BoundIntrinsic.None or BoundIntrinsic.ClassTryCreate)
            {
                RegisterManagedRoots(i);
            }
            var rootsBegin = _code.Position;
            if (function.Intrinsic != BoundIntrinsic.None)
            {
                if (function.Intrinsic == BoundIntrinsic.ClassTryCreate)
                {
                    EmitManagedClassFactory(function);
                }
                else if (function.Intrinsic >= BoundIntrinsic.NodeTryCreate)
                {
                    EmitManagedIntrinsic(function.Intrinsic);
                }
                else
                {
                    EmitBufferIntrinsic(function.Intrinsic);
                }
                if (function.Intrinsic == BoundIntrinsic.ClassTryCreate)
                {
                    UnregisterManagedRoots(i);
                }
                Epilog();
            }
            else
            {
                if (function.IndexedBody is TrRmScalarGraph graph)
                {
                    EmitIndexedBody(graph);
                }
                else
                {
                    Statement(function.Body);
                }
                if (function.ReturnType == PrimitiveType.Void)
                {
                    UnregisterManagedRoots(i);
                    Epilog();
                }
            }
            if (_checkedExceptions && _managedRootFlags[i].Count != 0)
            {
                var unwind = _funclets.Count;
                _funclets.Add((new BoundBlock([], function.Span), i, 0, 0));
                _ = _unwindRootFrames.Add(unwind);
                _scopes[i].Add(new NativeExceptionScope(rootsBegin, _code.Position, -unwind - 1, 0));
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
            _normalExits.Clear();
            _loopTargets.Clear();
            Statement(body);
            if (_stagedOwners.TryGetValue(index, out var owners))
            {
                for (var owner = owners.Count - 1; owner >= 0; owner--)
                {
                    var (value, live, type) = owners[owner];
                    _code.Load(live);
                    _code.Bytes(0x85, 0xC0);
                    var vacant = _code.JumpIf(0x84);
                    _code.Clear(live);
                    EmitDropValue(value, type);
                    _code.PatchRelative(vacant, _code.Position);
                }
            }
            if (_managedStagedRoots.TryGetValue(index, out var roots))
            {
                foreach (var slot in roots)
                {
                    SetManagedRoot(slot, active: false);
                }
            }
            if (_managedAbandonedFactories.TryGetValue(index, out var provisional))
            {
                SetManagedRoot(provisional, active: false);
                EmitManagedAbandon(provisional);
            }
            if (_unwindRootFrames.Contains(index))
            {
                UnregisterManagedRoots(function);
            }
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
            if (_managedNodes)
            {
                _code.Bytes(0x89, 0x44, 0x24, 0x20);
                _dataFixups.Add(new NativeDataFixup(_code.DataAddress(), _managedRuntimeOffset));
                _code.Bytes(0x48, 0x89, 0xD1);
                _managedCollectorCalls.Add(_code.RelativeCall());
                _code.Bytes(0x8B, 0x44, 0x24, 0x20);
            }
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

    private int PendingReturnSlot() => (_returnSlot + (IsIndirect(_program.Functions[_currentFunction].ReturnType) ? 8 : 0) + 15) & ~15;

    private int StatementSpills(BoundStatement statement, ref int maximumArguments)
    {
        var ignoredCleanup = false;
        return StatementSpills(statement, ref maximumArguments, ref ignoredCleanup, managedScopes: false);
    }

    private int StatementSpills(BoundStatement statement, ref int maximumArguments,
        ref bool containsCleanup, bool managedScopes)
    {
        switch (statement)
        {
            case BoundBlock block:
                containsCleanup |= managedScopes && !block.RetiredManagedLocals.IsEmpty;
                var maximum = 0;
                foreach (var child in block.Statements)
                {
                    maximum = Math.Max(maximum,
                        StatementSpills(child, ref maximumArguments, ref containsCleanup, managedScopes));
                }
                return maximum;
            case BoundLocal local:
                return ExpressionSpills(local.Initializer, ref maximumArguments);
            case BoundViewScope view:
                return Math.Max(2, Math.Max(ExpressionSpills(view.Start, ref maximumArguments),
                    Math.Max(1 + ExpressionSpills(view.RangeEnd ?? view.Length, ref maximumArguments),
                        StatementSpills(view.Body, ref maximumArguments, ref containsCleanup, managedScopes))));
            case BoundViewSet write:
                return Math.Max(2, Math.Max(ExpressionSpills(write.Index, ref maximumArguments),
                    1 + ExpressionSpills(write.Value, ref maximumArguments)));
            case BoundAssignment assignment:
                return Math.Max(ExpressionSpills(assignment.Value, ref maximumArguments),
                    HasDeclaredCleanup(assignment.Value.Type)
                        ? CleanupSpills(ref maximumArguments) : 0);
            case BoundOptionalAssignment optionalAssignment:
                return optionalAssignment.Place is BoundFieldRead field
                    ? Math.Max(ExpressionSpills(field.Receiver, ref maximumArguments),
                        1 + ExpressionSpills(optionalAssignment.Value, ref maximumArguments))
                    : ExpressionSpills(optionalAssignment.Value, ref maximumArguments);
            case BoundDrop drop:
                return drop.CleanupFunction >= 0 ? CleanupSpills(ref maximumArguments) : 0;
            case BoundLifetimeEnd end:
                return end.CleanupFunction >= 0 ? CleanupSpills(ref maximumArguments) : 0;
            case BoundFieldAssignment assignment:
                return Math.Max(2, 1 + ExpressionSpills(assignment.Value, ref maximumArguments));
            case BoundBufferAssignment assignment:
                return Math.Max(ExpressionSpills(assignment.Index, ref maximumArguments),
                    1 + ExpressionSpills(assignment.Value, ref maximumArguments));
            case BoundExpressionStatement expression:
                return ExpressionSpills(expression.Expression, ref maximumArguments);
            case BoundIf conditional:
                return Math.Max(ExpressionSpills(conditional.Condition, ref maximumArguments),
                    Math.Max(StatementSpills(conditional.Then, ref maximumArguments,
                            ref containsCleanup, managedScopes),
                        conditional.Else is null ? 0 : StatementSpills(conditional.Else, ref maximumArguments,
                            ref containsCleanup, managedScopes)));
            case BoundWhile loop:
                return Math.Max(ExpressionSpills(loop.Condition, ref maximumArguments),
                    Math.Max(StatementSpills(loop.Body, ref maximumArguments, ref containsCleanup, managedScopes),
                        loop.Iterator is null ? 0 : StatementSpills(loop.Iterator, ref maximumArguments,
                            ref containsCleanup, managedScopes)));
            case BoundTryFinally cleanup:
                containsCleanup = true;
                return Math.Max(StatementSpills(cleanup.Body, ref maximumArguments,
                        ref containsCleanup, managedScopes),
                    StatementSpills(cleanup.Finally, ref maximumArguments, ref containsCleanup, managedScopes));
            case BoundUnsafe unsafeBlock:
                return StatementSpills(unsafeBlock.Body, ref maximumArguments, ref containsCleanup, managedScopes);
            case BoundTryCatch recovery:
                containsCleanup |= recovery.Finally is not null;
                return Math.Max(StatementSpills(recovery.Body, ref maximumArguments,
                        ref containsCleanup, managedScopes),
                    Math.Max(StatementSpills(recovery.Catch, ref maximumArguments, ref containsCleanup, managedScopes),
                        recovery.Finally is null ? 0 : StatementSpills(recovery.Finally, ref maximumArguments,
                            ref containsCleanup, managedScopes)));
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
            case BoundUpdate update:
                return Math.Max(3, update.Place switch {
                    BoundFieldRead field => ExpressionSpills(field.Receiver, ref maximumArguments),
                    BoundBufferRead buffer => ExpressionSpills(buffer.Index, ref maximumArguments),
                    _ => 0,
                });
            case BoundCompoundAssignment compound:
                return Math.Max(4 + ExpressionSpills(compound.Right, ref maximumArguments),
                    Math.Max(3, compound.Place switch {
                        BoundFieldRead field => ExpressionSpills(field.Receiver, ref maximumArguments),
                        BoundBufferRead buffer => ExpressionSpills(buffer.Index, ref maximumArguments),
                        _ => 0,
                    }));
            case BoundNominalUpdate update:
                maximumArguments = Math.Max(maximumArguments, IsIndirect(update.Type) ? 2 : 1);
                return Math.Max(4, update.Place is BoundFieldRead nominalField
                    ? ExpressionSpills(nominalField.Receiver, ref maximumArguments) : 0);
            case BoundCast cast:
                return Math.Max(RequiresFloatingEnvironment(cast.Operand.Type, cast.Type) ? 3 : 0,
                    ExpressionSpills(cast.Operand, ref maximumArguments));
            case TrRmScalarConvert conversion:
                return ExpressionSpills(conversion.Operand, ref maximumArguments);
            case TrRmScalarWrappingBinary wrapping:
                return Math.Max(ExpressionSpills(wrapping.Left, ref maximumArguments),
                    1 + ExpressionSpills(wrapping.Right, ref maximumArguments));
            case TrRmScalarOptionalLift lift:
                return 1 + ExpressionSpills(lift.Value, ref maximumArguments);
            case TrRmScalarOptionalNone none when IsIndirect(none.Type):
                return 1;
            case TrRmScalarOptionalPresence presence:
                return ExpressionSpills(presence.Optional, ref maximumArguments);
            case TrRmScalarOptionalEquality:
                return 2;
            case BoundOptionalLift lift:
                return ExpressionSpills(lift.Value, ref maximumArguments);
            case BoundCoalesce coalesce:
                return Math.Max(ExpressionSpills(coalesce.Optional, ref maximumArguments),
                    ExpressionSpills(coalesce.Fallback, ref maximumArguments));
            case BoundReadonlyProjection projection:
                return ExpressionSpills(projection.Value, ref maximumArguments);
            case BoundIsNull test:
                return ExpressionSpills(test.Value, ref maximumArguments);
            case BoundIsSome pattern:
                return ExpressionSpills(pattern.Value, ref maximumArguments);
            case BoundIsVariablePattern pattern:
                return ExpressionSpills(pattern.Value, ref maximumArguments);
            case BoundIsInitialized:
                return 0;
            case BoundFieldRead field:
                return ExpressionSpills(field.Receiver, ref maximumArguments);
            case BoundBufferRead buffer:
                return ExpressionSpills(buffer.Index, ref maximumArguments);
            case BoundViewRead view:
                return ExpressionSpills(view.Index, ref maximumArguments);
            case BoundSnapshotSpan:
                return 4;
            case BoundSpanLength length:
                return ExpressionSpills(length.View, ref maximumArguments);
            case BoundSpanRead read:
                return Math.Max(2, Math.Max(ExpressionSpills(read.View, ref maximumArguments),
                    1 + ExpressionSpills(read.Index, ref maximumArguments)));
            case BoundSpanAdvance advance:
                return Math.Max(1, ExpressionSpills(advance.View, ref maximumArguments));
            case BoundBinary binary:
                return Math.Max(3, Math.Max(ExpressionSpills(binary.Left, ref maximumArguments),
                    1 + ExpressionSpills(binary.Right, ref maximumArguments)));
            case BoundConditional conditional:
                return Math.Max(ExpressionSpills(conditional.Condition, ref maximumArguments),
                    Math.Max(ExpressionSpills(conditional.WhenTrue, ref maximumArguments),
                        ExpressionSpills(conditional.WhenFalse, ref maximumArguments)));
            case BoundCall call:
                maximumArguments = Math.Max(maximumArguments,
                    call.Arguments.Length + (IsIndirect(call.Type) ? 1 : 0)
                        + (_sourceDependent?[call.Function] == true ? 2 : 0));
                var maximum = 1 + (call.Arguments.Length * 2);
                for (var i = 0; i < call.Arguments.Length; i++)
                {
                    if (call.Arguments[i] is BoundOutArgument output && HasDeclaredCleanup(output.Type))
                    {
                        maximum = Math.Max(maximum, 1 + (call.Arguments.Length * 2)
                            + CleanupSpills(ref maximumArguments));
                    }
                    else if (call.Arguments[i] is not BoundOutArgument)
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

    private static int CleanupSpills(ref int maximumArguments)
    {
        maximumArguments = Math.Max(maximumArguments, 1);
        return 3;
    }

    private static HashSet<int> InitializationTestVariables(BoundFunction function)
    {
        var variables = new HashSet<int>();
        VisitStatement(function.Body);
        return variables;

        void VisitStatement(BoundStatement statement)
        {
            switch (statement)
            {
                case BoundBlock block:
                    foreach (var child in block.Statements)
                    {
                        VisitStatement(child);
                    }
                    break;
                case BoundLocal local:
                    VisitExpression(local.Initializer);
                    break;
                case BoundAssignment assignment:
                    VisitExpression(assignment.Value);
                    break;
                case BoundOptionalAssignment assignment:
                    VisitExpression(assignment.Place);
                    VisitExpression(assignment.Value);
                    break;
                case BoundFieldAssignment assignment:
                    VisitExpression(assignment.Value);
                    break;
                case BoundBufferAssignment assignment:
                    VisitExpression(assignment.Index);
                    VisitExpression(assignment.Value);
                    break;
                case BoundExpressionStatement expressionStatement:
                    VisitExpression(expressionStatement.Expression);
                    break;
                case BoundIf conditional:
                    VisitExpression(conditional.Condition);
                    VisitStatement(conditional.Then);
                    if (conditional.Else is not null)
                    {
                        VisitStatement(conditional.Else);
                    }
                    break;
                case BoundWhile loop:
                    VisitExpression(loop.Condition);
                    VisitStatement(loop.Body);
                    if (loop.Iterator is not null)
                    {
                        VisitStatement(loop.Iterator);
                    }
                    break;
                case BoundTryFinally cleanup:
                    VisitStatement(cleanup.Body);
                    VisitStatement(cleanup.Finally);
                    break;
                case BoundViewScope view:
                    VisitExpression(view.Owner);
                    VisitExpression(view.Start);
                    VisitExpression(view.RangeEnd ?? view.Length);
                    VisitStatement(view.Body);
                    break;
                case BoundViewSet write:
                    VisitExpression(write.Index);
                    VisitExpression(write.Value);
                    break;
                case BoundUnsafe unsafeBlock:
                    VisitStatement(unsafeBlock.Body);
                    break;
                case BoundTryCatch recovery:
                    VisitStatement(recovery.Body);
                    VisitStatement(recovery.Catch);
                    if (recovery.Finally is not null)
                    {
                        VisitStatement(recovery.Finally);
                    }
                    break;
                case BoundRequire requirement:
                    VisitExpression(requirement.Condition);
                    break;
                case BoundReturn { Value: BoundExpression value }:
                    VisitExpression(value);
                    break;
            }
        }

        void VisitExpression(BoundExpression expression)
        {
            switch (expression)
            {
                case BoundIsInitialized initializationTest:
                    _ = variables.Add(initializationTest.Variable);
                    break;
                case BoundUnary unary:
                    VisitExpression(unary.Operand);
                    break;
                case BoundUpdate update:
                    VisitExpression(update.Place);
                    break;
                case BoundCompoundAssignment compound:
                    VisitExpression(compound.Place);
                    VisitExpression(compound.Right);
                    break;
                case BoundNominalUpdate update:
                    VisitExpression(update.Place);
                    break;
                case BoundCast cast:
                    VisitExpression(cast.Operand);
                    break;
                case TrRmScalarConvert conversion:
                    VisitExpression(conversion.Operand);
                    break;
                case TrRmScalarWrappingBinary wrapping:
                    VisitExpression(wrapping.Left);
                    VisitExpression(wrapping.Right);
                    break;
                case BoundOptionalLift lift:
                    VisitExpression(lift.Value);
                    break;
                case BoundReadonlyProjection projection:
                    VisitExpression(projection.Value);
                    break;
                case BoundIsNull test:
                    VisitExpression(test.Value);
                    break;
                case BoundIsSome pattern:
                    VisitExpression(pattern.Value);
                    break;
                case BoundIsVariablePattern pattern:
                    VisitExpression(pattern.Value);
                    break;
                case BoundFieldRead field:
                    VisitExpression(field.Receiver);
                    break;
                case BoundFieldLoan loan:
                    VisitExpression(loan.Place);
                    break;
                case BoundMutableFieldLoan loan:
                    VisitExpression(loan.Place);
                    break;
                case BoundViewRead view:
                    VisitExpression(view.Index);
                    break;
                case BoundSpanLength length:
                    VisitExpression(length.View);
                    break;
                case BoundSpanRead read:
                    VisitExpression(read.View);
                    VisitExpression(read.Index);
                    break;
                case BoundSpanAdvance advance:
                    VisitExpression(advance.View);
                    break;
                case BoundBufferRead buffer:
                    VisitExpression(buffer.Index);
                    break;
                case BoundBinary binary:
                    VisitExpression(binary.Left);
                    VisitExpression(binary.Right);
                    break;
                case BoundConditional conditional:
                    VisitExpression(conditional.Condition);
                    VisitExpression(conditional.WhenTrue);
                    VisitExpression(conditional.WhenFalse);
                    break;
                case BoundCoalesce coalesce:
                    VisitExpression(coalesce.Optional);
                    VisitExpression(coalesce.Fallback);
                    break;
                case BoundCall call:
                    foreach (var argument in call.Arguments)
                    {
                        VisitExpression(argument);
                    }
                    break;
            }
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
    private int OwnerPlace(int index) =>
        _program.Functions[_currentFunction].Variables[index].IsOut ? OutSlot(index) : Slot(index);
    private void SetInitializationFlag(int variable, bool initialized)
    {
        var flag = _initializationFlags[_currentFunction][variable];
        if (flag < 0)
        {
            return;
        }
        if (initialized)
        {
            _code.Set(flag);
        }
        else
        {
            _code.Clear(flag);
        }
    }

    private void EmitInitializationTest(int variable)
    {
        var type = _program.Functions[_currentFunction].Variables[variable].Type;
        if (ManagedClassTypes.IsManaged(type))
        {
            var sourceSlot = OwnerPlace(variable);
            if (!_managedRootFlags[_currentFunction].TryGetValue(sourceSlot, out var flag))
            {
                throw new InvalidOperationException("Managed initialization test has no runtime state flag.");
            }
            _code.Load(flag);
        }
        else if (IsUnique(type))
        {
            var flag = _liveSlots[_currentFunction][variable];
            if (flag >= 0)
            {
                _code.Load(flag);
            }
            else
            {
                _code.Bytes(0xB8, 0x01, 0x00, 0x00, 0x00);
            }
        }
        else
        {
            var flag = _initializationFlags[_currentFunction][variable];
            if (flag >= 0)
            {
                _code.Load(flag);
            }
            else
            {
                _code.Bytes(0xB8, 0x01, 0x00, 0x00, 0x00);
            }
        }
        _code.Bytes(0x85, 0xC0, 0x0F, 0x95, 0xC0, 0x0F, 0xB6, 0xC0);
    }

    private static bool IsStruct(PrimitiveType type) =>
        (int)type is >= 100 and < ManagedClassTypes.First;
    private static bool RequiresFloatingEnvironment(PrimitiveType source, PrimitiveType target) =>
        source is PrimitiveType.Float32 or PrimitiveType.Float64
            || target is PrimitiveType.Float32 or PrimitiveType.Float64;
    private static bool IsWide(PrimitiveType type) =>
        type is PrimitiveType.Int64 or PrimitiveType.UInt64 or PrimitiveType.Float64 or PrimitiveType.OwnedInt32Buffer
            or PrimitiveType.TokenSpan
            || ManagedClassTypes.IsManaged(type);
    private int Size(PrimitiveType type) => ValueLayout.Size(type, _program.Structures);
    private bool IsIndirect(PrimitiveType type) => type == PrimitiveType.TokenSpan
        || IsStruct(type) && Size(type) is not (1 or 2 or 4 or 8);
    private bool IsUnique(PrimitiveType type) =>
        type == PrimitiveType.OwnedInt32Buffer
            || (IsStruct(type) && _program.Structures[(int)type - 100].IsUnique);
    private bool HasDeclaredCleanup(PrimitiveType type) =>
        IsStruct(type) && _program.Structures[(int)type - 100].CleanupMethod is not null;
    private bool IsBorrowedReceiver()
    {
        var function = _program.Functions[_currentFunction];
        return function.DeclaringStruct is not null && !function.IsConstructor && !function.IsCleanup
            && !function.IsStatic && (IsUnique(function.Variables[0].Type)
                || (IsStruct(function.Variables[0].Type) && function.Variables[0].IsMutable));
    }

    private void EmitUniqueFieldPlace(BoundExpression expression)
    {
        if (expression is BoundRead root)
        {
            if (root.Variable == 0 && IsBorrowedReceiver())
            {
                _code.Load64(Slot(0));
            }
            else
            {
                _code.Address(OwnerPlace(root.Variable));
            }
            return;
        }
        var fieldRead = (BoundFieldRead)expression;
        EmitUniqueFieldPlace(fieldRead.Receiver);
        var field = _program.Structures[(int)fieldRead.Receiver.Type - 100].Fields[fieldRead.Field];
        _code.PointerOffset(field.Offset);
    }

    private bool UpdateAddress(BoundExpression place, int spill)
    {
        switch (place)
        {
            case BoundRead read:
                if (read.Variable == 0 && IsBorrowedReceiver())
                {
                    _code.Load64(Slot(0));
                }
                else
                {
                    _code.Address(OwnerPlace(read.Variable));
                }
                return false;
            case BoundBufferRead buffer:
                EmitBufferIndex(buffer.Buffer, buffer.Index, buffer.IsEndRelativeOffset, spill);
                _dataFixups.Add(new NativeDataFixup(_code.DataAddress(), _bufferOffsets[buffer.Buffer]));
                _code.Bytes(0x48, 0x8D, 0x04, 0x82); // lea rax,[rdx+rax*4]
                return false;
            case BoundFieldRead field:
                bool rooted;
                int offset;
                if (ManagedClassTypes.IsClass(field.Receiver.Type))
                {
                    Expression(field.Receiver, spill);
                    _code.Store64(Spill(spill));
                    SetManagedRoot(Spill(spill), active: true);
                    offset = 32 + _program.Classes[ManagedClassTypes.ClassIndex(field.Receiver.Type)]
                        .Fields[field.Field].Offset;
                    rooted = true;
                }
                else
                {
                    rooted = UpdateAddress(field.Receiver, spill);
                    offset = _program.Structures[(int)field.Receiver.Type - 100].Fields[field.Field].Offset;
                }
                _code.PointerOffset(offset);
                return rooted;
            default:
                throw new InvalidOperationException("Verified update requires a writable place.");
        }
    }

    private void DropOwner(int variable)
    {
        var live = _liveSlots[_currentFunction][variable];
        _code.Load(live);
        _code.Bytes(0x85, 0xC0);
        var vacant = _code.JumpIf(0x84);
        var type = _program.Functions[_currentFunction].Variables[variable].Type;
        _code.Clear(live);
        EmitDropValue(OwnerPlace(variable), type);
        _code.PatchRelative(vacant, _code.Position);
    }

    private void EmitBufferIntrinsic(BoundIntrinsic intrinsic)
    {
        switch (intrinsic)
        {
            case BoundIntrinsic.BufferTryCreate:
                _code.Load(Slot(0));
                _code.Bytes(0x85, 0xC0);
                RuntimeBranch(0x88, 0);
                _code.Byte(0x3D);
                _code.Int32(NativeBufferIntrinsics.MaximumCapacity);
                RuntimeBranch(0x8F, 0);
                _nativeImportFixups.Add(new NativeImportFixup(_code.ImportCall(), "KERNEL32.dll", "GetProcessHeap"));
                _code.MoveArgument(0, true);
                _code.Bytes(0x31, 0xD2); // HeapAlloc flags = 0
                _code.Load(Slot(0));
                _code.Bytes(0x4C, 0x8D, 0x04, 0x85);
                _code.Int32(8); // r8 = sizeof(header) + capacity * sizeof(int32)
                _nativeImportFixups.Add(new NativeImportFixup(_code.ImportCall(), "KERNEL32.dll", "HeapAlloc"));
                _code.Bytes(0x48, 0x85, 0xC0);
                var allocationFailed = _code.JumpIf(0x84);
                _code.Bytes(0x48, 0x89, 0xC1); // rcx = allocated header
                _code.Bytes(0xC7, 0x01);
                _code.Int32(0); // initialized prefix is empty
                _code.Load(Slot(0));
                _code.Bytes(0x89, 0x41, 0x04);
                _code.Load64(Slot(1));
                _code.Bytes(0x48, 0x89, 0x08); // publish owner only after header initialization
                _code.Bytes(0xB8);
                _code.Int32(1);
                var created = _code.Jump();
                _code.PatchRelative(allocationFailed, _code.Position);
                _code.Bytes(0x31, 0xC0);
                _code.PatchRelative(created, _code.Position);
                break;
            case BoundIntrinsic.BufferCount:
            case BoundIntrinsic.BufferCapacity:
                _code.Load64(Slot(0));
                _code.Bytes(0x48, 0x8B, 0x00); // receiver place -> opaque owner handle
                _code.Bytes(0x8B, intrinsic == BoundIntrinsic.BufferCount ? (byte)0x00 : (byte)0x40);
                if (intrinsic == BoundIntrinsic.BufferCapacity)
                {
                    _code.Byte(0x04);
                }
                break;
            case BoundIntrinsic.BufferRead:
            case BoundIntrinsic.BufferSet:
                _code.Load64(Slot(0));
                _code.Bytes(0x48, 0x8B, 0x00, 0x48, 0x89, 0xC2); // rdx = owner handle
                _code.Load(Slot(1));
                _code.Bytes(0x3B, 0x02); // unsigned index >= count includes negative indexes
                RuntimeBranch(0x83, 2);
                if (intrinsic == BoundIntrinsic.BufferRead)
                {
                    _code.Bytes(0x8B, 0x44, 0x82, 0x08);
                }
                else
                {
                    _code.Bytes(0x89, 0xC1); // rcx = checked index
                    _code.Load(Slot(2));
                    _code.Bytes(0x89, 0x44, 0x8A, 0x08);
                }
                break;
            case BoundIntrinsic.BufferTryAppend:
                _code.Load64(Slot(0));
                _code.Bytes(0x48, 0x8B, 0x00, 0x48, 0x89, 0xC2);
                _code.Bytes(0x8B, 0x02, 0x3B, 0x42, 0x04); // count < capacity
                var full = _code.JumpIf(0x83);
                _code.Bytes(0x89, 0xC1);
                _code.Load(Slot(1));
                _code.Bytes(0x89, 0x44, 0x8A, 0x08); // initialize element before publishing count
                _code.Bytes(0xFF, 0x02, 0xB8);
                _code.Int32(1);
                var appended = _code.Jump();
                _code.PatchRelative(full, _code.Position);
                _code.Bytes(0x31, 0xC0);
                _code.PatchRelative(appended, _code.Position);
                break;
            case BoundIntrinsic.BufferClear:
                _code.Load64(Slot(0));
                _code.Bytes(0x48, 0x8B, 0x00, 0xC7, 0x00);
                _code.Int32(0);
                break;
            case BoundIntrinsic.None or BoundIntrinsic.NodeTryCreate or BoundIntrinsic.NodeValue
                or BoundIntrinsic.NodeSetValue or BoundIntrinsic.NodeSetNext or BoundIntrinsic.NodeClearNext
                or BoundIntrinsic.NodeTryGetNext or BoundIntrinsic.NodeCollect or BoundIntrinsic.ClassTryCreate:
            default:
                throw new InvalidOperationException("Unknown verified native buffer intrinsic.");
        }
    }

    private void EmitDropValue(int place, PrimitiveType type)
    {
        if (type == PrimitiveType.OwnedInt32Buffer)
        {
            _nativeImportFixups.Add(new NativeImportFixup(_code.ImportCall(), "KERNEL32.dll", "GetProcessHeap"));
            _code.MoveArgument(0, true);
            _code.Bytes(0x31, 0xD2); // HeapFree flags = 0
            _code.Load64(place);
            _code.Bytes(0x49, 0x89, 0xC0); // r8 = owner handle
            _nativeImportFixups.Add(new NativeImportFixup(_code.ImportCall(), "KERNEL32.dll", "HeapFree"));
            _code.Bytes(0x85, 0xC0);
            var released = _code.JumpIf(0x85);
            _code.Bytes(0xB9);
            _code.Int32(unchecked((int)0xE0540005));
            _nativeImportFixups.Add(new NativeImportFixup(_code.ImportCall(), "KERNEL32.dll", "ExitProcess"));
            _code.Bytes(0x0F, 0x0B); // ExitProcess cannot return
            _code.PatchRelative(released, _code.Position);
            return;
        }
        var structure = _program.Structures[(int)type - 100];
        var name = structure.CleanupMethod;
        if (name is not null)
        {
            var method = -1;
            for (var index = 0; index < _program.Functions.Length; index++)
            {
                if (_program.Functions[index].Name == name)
                {
                    method = index;
                    break;
                }
            }
            if (method < 0)
            {
                throw new InvalidOperationException("Verified unique cleanup method is missing.");
            }
            if (IsIndirect(type))
            {
                var scratch = _cleanupArgumentSlots[_currentFunction];
                if (scratch < 0)
                {
                    throw new InvalidOperationException("Indirect cleanup requires dedicated argument storage.");
                }
                _code.Address(place);
                CopyToFrame(scratch, Size(type));
                _code.Address(scratch);
            }
            else
            {
                LoadValue(place, type);
            }
            _code.MoveArgument(0, true);
            EmitTargetCall(method);
        }
        for (var field = structure.Fields.Length - 1; field >= 0; field--)
        {
            if (IsUnique(structure.Fields[field].Type))
            {
                EmitDropValue(place + structure.Fields[field].Offset, structure.Fields[field].Type);
            }
        }
    }
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

    private void EmitOptionalPresenceInAccumulator(PrimitiveType optionalType)
    {
        var payloadSize = Size(TrRmOptionalScalar.PayloadType(optionalType));
        if (IsIndirect(optionalType))
        {
            _code.Bytes(0x0F, 0xB6, 0x80);
            _code.Int32(payloadSize);
        }
        else
        {
            _code.Bytes(0x48, 0xC1, 0xE8, (byte)(payloadSize * 8), 0x83, 0xE0, 0x01);
        }
    }

    private void LoadOptionalPresence(int offset, PrimitiveType optionalType)
    {
        if (IsIndirect(optionalType))
        {
            _code.Address(offset);
        }
        else
        {
            _code.Load64(offset);
        }
        EmitOptionalPresenceInAccumulator(optionalType);
    }

    private void EmitOptionalEquality(TrRmScalarOptionalEquality equality, int spill)
    {
        var type = equality.Left.Type;
        var left = Spill(spill);
        var right = Spill(spill + 1);
        Expression(equality.Left, spill + 2);
        StoreValue(left, type);
        Expression(equality.Right, spill + 2);
        StoreValue(right, type);

        LoadOptionalPresence(left, type);
        _code.Bytes(0x85, 0xC0);
        var leftAbsent = _code.JumpIf(0x84);
        LoadOptionalPresence(right, type);
        _code.Bytes(0x85, 0xC0);
        var rightAbsent = _code.JumpIf(0x84);
        LoadValue(left, equality.PayloadType);
        if (IsWide(equality.PayloadType))
        {
            _code.Bytes(0x48, 0x89, 0xC1);
        }
        else
        {
            _code.Bytes(0x89, 0xC1);
        }
        LoadValue(right, equality.PayloadType);
        if (IsWide(equality.PayloadType))
        {
            _code.Bytes(0x48, 0x39, 0xC8);
        }
        else
        {
            _code.Bytes(0x39, 0xC8);
        }
        _code.Bytes(0x0F, equality.Negated ? (byte)0x95 : (byte)0x94, 0xC0);
        _code.Bytes(0x0F, 0xB6, 0xC0);
        var compared = _code.Jump();

        _code.PatchRelative(leftAbsent, _code.Position);
        LoadOptionalPresence(right, type);
        _code.Bytes(0x85, 0xC0);
        var leftAbsentRightPresent = _code.JumpIf(0x85);
        _code.Byte(0xB8);
        _code.Int32(equality.Negated ? 0 : 1);
        var bothAbsent = _code.Jump();

        _code.PatchRelative(rightAbsent, _code.Position);
        _code.Byte(0xB8);
        _code.Int32(equality.Negated ? 1 : 0);
        var mismatchedPresence = _code.Jump();

        _code.PatchRelative(leftAbsentRightPresent, _code.Position);
        _code.Byte(0xB8);
        _code.Int32(equality.Negated ? 1 : 0);

        _code.PatchRelative(bothAbsent, _code.Position);
        _code.PatchRelative(mismatchedPresence, _code.Position);
        _code.PatchRelative(compared, _code.Position);
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

    private void FloatingRuntimeBranch(byte condition, int target, int savedMxcsr, int controlledMxcsr)
    {
        var skip = _code.JumpIf((byte)(condition ^ 1));
        _code.LoadMxcsr(savedMxcsr);
        _runtimeCalls.Add((_code.RelativeCall(), target));
        _code.LoadMxcsr(controlledMxcsr);
        _code.PatchRelative(skip, _code.Position);
    }

    private void CallCleanup(int index)
    {
        _code.Bytes(0x48, 0x89, 0xEA); // mov rdx,rbp: pass the owning function's frame base
        _code.Bytes(0x31, 0xC9); // normal cleanup has no exception record
        _cleanupCalls.Add((_code.RelativeCall(), index));
    }

    private void EmitTargetCall(int function)
    {
        if (_program.Functions[function].IsExternal)
        {
            if (_nativeImports is null || !_nativeImports.TryGetValue(function, out var import))
            {
                throw new InvalidOperationException("External call has no verified native import binding.");
            }
            _nativeImportFixups.Add(new NativeImportFixup(_code.ImportCall(), import.DllName, import.ExportName, function));
        }
        else
        {
            _calls.Add((_code.RelativeCall(), function));
        }
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

    private void EmitIndexedBody(TrRmScalarGraph graph)
    {
        var positions = new int[graph.Blocks.Length + 1];
        var fixups = new List<(int Offset, int Target)>();

        void EmitEdge(TrRmScalarEdge edge)
        {
            var parameters = graph.Blocks[edge.Target - 1].Parameters;
            // Backedges can exchange parameter values; no destination may overwrite a later source.
            for (var i = 0; i < parameters.Length; i++)
            {
                var type = graph.Types[edge.Arguments[i] - 1];
                var source = Slot(edge.Arguments[i] - 1);
                var staged = _edgeCopyBases[_currentFunction] + (16 * i);
                if (IsIndirect(type))
                {
                    _code.Address(source);
                    CopyToFrame(staged, Size(type));
                }
                else
                {
                    LoadValue(source, type);
                    StoreValue(staged, type);
                }
            }
            for (var i = 0; i < parameters.Length; i++)
            {
                var type = graph.Types[parameters[i] - 1];
                var destination = Slot(parameters[i] - 1);
                var staged = _edgeCopyBases[_currentFunction] + (16 * i);
                if (IsIndirect(type))
                {
                    _code.Address(staged);
                    CopyToFrame(destination, Size(type));
                }
                else
                {
                    LoadValue(staged, type);
                    StoreValue(destination, type);
                }
            }
            fixups.Add((_code.Jump(), edge.Target));
        }

        for (var id = 1; id <= graph.Blocks.Length; id++)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            positions[id] = _code.Position;
            var block = graph.Blocks[id - 1];
            foreach (var operation in block.Operations)
            {
                Statement(operation);
            }
            if (block.FailureReason != 0)
            {
                // TRRM reason IDs differ from the native fail-fast status order.
                var target = block.FailureReason switch
                {
                    1 => 2,
                    2 => 0,
                    3 => 1,
                    4 => 0,
                    _ => throw new InvalidOperationException("Indexed failure reason was not verified."),
                };
                _runtimeCalls.Add((_code.RelativeCall(), target));
                _code.Byte(0xCC);
            }
            else if (block.SwitchValue != 0)
            {
                var selectorType = graph.Types[block.SwitchValue - 1];
                LoadValue(Slot(block.SwitchValue - 1), selectorType);
                if (IsWide(selectorType))
                {
                    _code.Bytes(0x49, 0x89, 0xC0);
                }
                else
                {
                    _code.Bytes(0x41, 0x89, 0xC0);
                }
                foreach (var switchCase in block.SwitchCases)
                {
                    if (IsWide(selectorType))
                    {
                        _code.Bytes(0x48, 0xB8);
                        _code.Int64(unchecked((long)switchCase.Bits));
                        _code.Bytes(0x49, 0x39, 0xC0);
                    }
                    else
                    {
                        _code.Byte(0xB8);
                        _code.Int32(unchecked((int)switchCase.Bits));
                        _code.Bytes(0x41, 0x39, 0xC0);
                    }
                    var nextCase = _code.JumpIf(0x85);
                    EmitEdge(switchCase.Edge);
                    _code.PatchRelative(nextCase, _code.Position);
                }
                EmitEdge(block.SwitchDefault
                    ?? throw new InvalidOperationException("Indexed switch lacks a default edge."));
            }
            else if (block.Condition != 0)
            {
                _code.Load(Slot(block.Condition - 1));
                _code.Bytes(0x85, 0xC0);
                var falseJump = _code.JumpIf(0x84);
                EmitEdge(block.True ?? throw new InvalidOperationException("Indexed branch lacks true edge."));
                _code.PatchRelative(falseJump, _code.Position);
                EmitEdge(block.False ?? throw new InvalidOperationException("Indexed branch lacks false edge."));
            }
            else if (block.True is not null)
            {
                EmitEdge(block.True);
            }
            else
            {
                Statement(new BoundReturn(block.ReturnValue == 0 ? null
                    : new BoundRead(block.ReturnValue - 1, graph.Types[block.ReturnValue - 1], default),
                    default));
            }
            if (_code.Position > MaximumText)
            {
                throw new NotSupportedException("Native text exceeds the 8 MiB image ceiling.");
            }
        }
        foreach (var (offset, target) in fixups)
        {
            _code.PatchRelative(offset, positions[target]);
        }
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
            case BoundDropSpan span:
                SetInitializationFlag(span.Variable, initialized: false);
                break;
            case BoundRequire requirement:
                Expression(requirement.Condition, 0);
                _code.Bytes(0x85, 0xC0);
                RuntimeBranch(0x84, 2);
                break;
            case BoundBlock block:
                var rootCleanup = -1;
                if (_checkedExceptions && !_emittingFunclet && !block.RetiredManagedLocals.IsEmpty)
                {
                    rootCleanup = _funclets.Count;
                    _funclets.Add((new BoundBlock([], block.Span) {
                        RetiredManagedLocals = block.RetiredManagedLocals,
                    }, _currentFunction, 0, 0));
                    _cleanups.Add(rootCleanup);
                }
                if (!block.RetiredManagedLocals.IsEmpty)
                {
                    _normalExits.Add(new NormalExit(rootCleanup, block.RetiredManagedLocals));
                }
                var rootScopeBegin = _code.Position;
                foreach (var child in block.Statements)
                {
                    Statement(child);
                }
                if (rootCleanup >= 0)
                {
                    _code.Byte(0x90);
                    _cleanups.RemoveAt(_cleanups.Count - 1);
                    _scopes[_currentFunction].Add(new NativeExceptionScope(rootScopeBegin, _code.Position,
                        -rootCleanup - 1, 0));
                    CallCleanup(rootCleanup);
                }
                else
                {
                    foreach (var retired in block.RetiredManagedLocals)
                    {
                        SetManagedRoot(OwnerPlace(retired), active: false);
                    }
                }
                if (!block.RetiredManagedLocals.IsEmpty)
                {
                    _normalExits.RemoveAt(_normalExits.Count - 1);
                }
                break;
            case TrRmScalarDivRem division:
                Expression(division.Dividend, 0);
                if (IsWide(division.Type))
                {
                    _code.Bytes(0x49, 0x89, 0xC0); // Preserve the dividend in volatile r8.
                }
                else
                {
                    _code.Bytes(0x41, 0x89, 0xC0);
                }
                Expression(division.Divisor, 1);
                if (IsWide(division.Type))
                {
                    _code.Bytes(0x48, 0x89, 0xC1, 0x4C, 0x89, 0xC0);
                }
                else
                {
                    _code.Bytes(0x89, 0xC1, 0x44, 0x89, 0xC0);
                }
                CheckedDivRem(division.Type);
                StoreValue(Slot(division.QuotientVariable), division.Type);
                SetInitializationFlag(division.QuotientVariable, initialized: true);
                if (IsWide(division.Type))
                {
                    _code.Bytes(0x48, 0x89, 0xD0);
                }
                else
                {
                    _code.Bytes(0x89, 0xD0);
                }
                StoreValue(Slot(division.RemainderVariable), division.Type);
                SetInitializationFlag(division.RemainderVariable, initialized: true);
                break;
            case BoundLocal local:
                Expression(local.Initializer, 0);
                StoreValue(Slot(local.Variable), local.Initializer.Type);
                SetInitializationFlag(local.Variable, initialized: true);
                if (ManagedClassTypes.IsManaged(local.Initializer.Type))
                {
                    SetManagedRoot(Slot(local.Variable), active: true);
                }
                if (IsUnique(local.Initializer.Type))
                {
                    _code.Set(_liveSlots[_currentFunction][local.Variable]);
                }
                break;
            case BoundViewScope view:
                Expression(view.Start, 0);
                _code.Store(Spill(0));
                Expression(view.RangeEnd ?? view.Length, 1);
                _code.Store(Spill(1));
                Expression(view.Owner, 2);
                _code.Bytes(0x48, 0x8B, 0x10); // rdx = heap header through the reserved owner place
                _code.Load(Spill(0));
                _code.Bytes(0x85, 0xC0);
                RuntimeBranch(0x88, 2);
                _code.Bytes(0x3B, 0x02); // start <= initialized count
                RuntimeBranch(0x87, 2);
                if (view.RangeEnd is null)
                {
                    _code.Bytes(0x89, 0xC1); // rcx = checked start
                    _code.Load(Spill(1));
                    _code.Bytes(0x85, 0xC0);
                    RuntimeBranch(0x88, 2);
                    _code.Bytes(0x44, 0x8B, 0x02, 0x41, 0x29, 0xC8); // r8d = count - start
                    _code.Bytes(0x44, 0x39, 0xC0); // length <= remaining initialized elements
                    RuntimeBranch(0x87, 2);
                }
                else
                {
                    if (view.IsStartEndRelative)
                    {
                        _code.Bytes(0x8B, 0x0A, 0x29, 0xC1, 0x89, 0xC8); // start = count - checked offset
                        _code.Store(Spill(0));
                    }
                    _code.Load(Spill(1));
                    _code.Bytes(0x85, 0xC0);
                    RuntimeBranch(0x88, 2);
                    _code.Bytes(0x3B, 0x02); // end <= initialized count
                    RuntimeBranch(0x87, 2);
                    if (view.IsRangeEndRelative)
                    {
                        _code.Bytes(0x8B, 0x0A, 0x29, 0xC1, 0x89, 0xC8); // end = count - checked offset
                        _code.Store(Spill(1));
                    }
                    _code.Load(Spill(0));
                    _code.Bytes(0x89, 0xC1); // rcx = start
                    _code.Load(Spill(1));
                    _code.Bytes(0x39, 0xC1); // start <= end
                    RuntimeBranch(0x8F, 2);
                    _code.Bytes(0x29, 0xC8); // length = end - start after both are checked
                    _code.Store(Spill(1));
                }
                _code.Bytes(0x48, 0x8D, 0x44, 0x8A, 0x08); // pointer to first element of the slice
                _code.Store64(Slot(view.Variable));
                _code.Load(Spill(1));
                _code.Store(Slot(view.Variable) + 8);
                Statement(view.Body);
                break;
            case BoundViewSet write:
                Expression(write.Index, 0);
                _code.Store(Spill(0));
                Expression(write.Value, 1);
                _code.Store(Spill(1));
                _code.Load(Spill(0));
                _code.Bytes(0x89, 0xC1);
                _code.Load(Slot(write.Variable) + 8);
                _code.Bytes(0x39, 0xC1);
                RuntimeBranch(0x83, 2);
                _code.Load64(Slot(write.Variable), destinationRegister: true);
                _code.Load(Spill(1));
                _code.Bytes(0x89, 0x04, 0x8A);
                break;
            case BoundOwnerDeclaration or BoundVacantLocal:
                break;
            case BoundLifetimeEnd end:
                var endedType = _program.Functions[_currentFunction].Variables[end.Variable].Type;
                if (IsUnique(endedType))
                {
                    DropOwner(end.Variable);
                }
                else if (ManagedClassTypes.IsManaged(endedType))
                {
                    SetManagedRoot(Slot(end.Variable), active: false);
                }
                SetInitializationFlag(end.Variable, initialized: false);
                break;
            case BoundAssignment assignment:
                Expression(assignment.Value, 0);
                if (ManagedClassTypes.IsManaged(assignment.Value.Type))
                {
                    var managedDestination = OwnerPlace(assignment.Variable);
                    StoreValue(managedDestination, assignment.Value.Type);
                    SetManagedRoot(managedDestination, active: true);
                    break;
                }
                if (IsUnique(assignment.Value.Type))
                {
                    StoreValue(_replacementSlots[_currentFunction], assignment.Value.Type);
                    DropOwner(assignment.Variable);
                    LoadValue(_replacementSlots[_currentFunction], assignment.Value.Type);
                    StoreValue(OwnerPlace(assignment.Variable), assignment.Value.Type);
                    _code.Set(_liveSlots[_currentFunction][assignment.Variable]);
                    break;
                }
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
                SetInitializationFlag(assignment.Variable, initialized: true);
                break;
            case BoundOptionalAssignment optionalAssignment:
                if (optionalAssignment.Place is BoundRead optionalLocal)
                {
                    var optionalDestination = OwnerPlace(optionalLocal.Variable);
                    _code.Load64(optionalDestination);
                    _code.Bytes(0x48, 0x85, 0xC0);
                    var localPresent = _code.JumpIf(0x85);
                    Expression(optionalAssignment.Value, 0);
                    StoreValue(optionalDestination, optionalLocal.Type);
                    SetManagedRoot(optionalDestination, active: true);
                    _code.PatchRelative(localPresent, _code.Position);
                    break;
                }
                var optionalField = (BoundFieldRead)optionalAssignment.Place;
                Expression(optionalField.Receiver, 0);
                _code.Store64(Spill(0));
                SetManagedRoot(Spill(0), active: true);
                var fieldDeclaration = _program.Classes[ManagedClassTypes.ClassIndex(optionalField.Receiver.Type)]
                    .Fields[optionalField.Field];
                _code.LoadPointerField(32 + fieldDeclaration.Offset, 8);
                _code.Bytes(0x48, 0x85, 0xC0);
                var fieldPresent = _code.JumpIf(0x85);
                var fallbackBegin = _code.Position;
                var fallbackFunclet = -1;
                if (_checkedExceptions && !_emittingFunclet)
                {
                    fallbackFunclet = _funclets.Count;
                    _funclets.Add((new BoundBlock([], optionalAssignment.Span), _currentFunction, 0, 0));
                    _managedStagedRoots.Add(fallbackFunclet, [Spill(0)]);
                }
                Expression(optionalAssignment.Value, 1);
                _code.Bytes(0x48, 0x89, 0xC1);
                _code.Load64(Spill(0));
                _code.PointerOffset(32 + fieldDeclaration.Offset);
                _code.Bytes(0x48, 0x89, 0xC2, 0x48, 0x89, 0xC8);
                _code.Bytes(0x48, 0x89, 0x02);
                if (fallbackFunclet >= 0)
                {
                    _code.Byte(0x90);
                    _scopes[_currentFunction].Add(new NativeExceptionScope(fallbackBegin, _code.Position,
                        -fallbackFunclet - 1, 0));
                }
                _code.PatchRelative(fieldPresent, _code.Position);
                SetManagedRoot(Spill(0), active: false);
                break;
            case BoundDrop drop:
                if (drop.IsOutput)
                {
                    _code.Bytes(0x48, 0x85, 0xC9); // the unwind funclet receives an exception record
                    var unwind = _code.JumpIf(0x85);
                    if (_program.Functions[_currentFunction].ReturnType == PrimitiveType.Bool)
                    {
                        _code.Load(PendingReturnSlot());
                        _code.Bytes(0x85, 0xC0);
                        var transferred = _code.JumpIf(0x85);
                        _code.PatchRelative(unwind, _code.Position);
                        DropOwner(drop.Variable);
                        _code.PatchRelative(transferred, _code.Position);
                    }
                    else
                    {
                        var transferred = _code.JumpIf(0x84);
                        _code.PatchRelative(unwind, _code.Position);
                        DropOwner(drop.Variable);
                        _code.PatchRelative(transferred, _code.Position);
                    }
                }
                else
                {
                    DropOwner(drop.Variable);
                }
                break;
            case BoundDropConstructedFields partial:
                _code.Bytes(0x48, 0x85, 0xC9); // only an exceptional unwind owns unfinished fields
                var completed = _code.JumpIf(0x84);
                var fields = _program.Structures[(int)_program.Functions[_currentFunction].ReturnType - 100].Fields;
                for (var index = partial.AcquisitionOrder.Length - 1; index >= 0; index--)
                {
                    var fieldIndex = partial.AcquisitionOrder[index];
                    var live = _constructedFieldSlots[_currentFunction][fieldIndex];
                    _code.Load(live);
                    _code.Bytes(0x85, 0xC0);
                    var vacant = _code.JumpIf(0x84);
                    _code.Clear(live);
                    EmitDropValue(Slot(0) + fields[fieldIndex].Offset, fields[fieldIndex].Type);
                    _code.PatchRelative(vacant, _code.Position);
                }
                _code.PatchRelative(completed, _code.Position);
                break;
            case BoundFieldAssignment fieldAssignment:
                var variable = _program.Functions[_currentFunction].Variables[fieldAssignment.Variable];
                var fieldType = variable.Type;
                var classField = ManagedClassTypes.IsClass(fieldType);
                var field = classField ? _program.Classes[ManagedClassTypes.ClassIndex(fieldType)]
                    .Fields[fieldAssignment.Field] : _program.Structures[(int)fieldType - 100].Fields[fieldAssignment.Field];
                var destination = (variable.IsOut ? OutSlot(fieldAssignment.Variable) : Slot(fieldAssignment.Variable))
                    + field.Offset;
                if (classField)
                {
                    _code.Load64(OwnerPlace(fieldAssignment.Variable));
                    var inlineOffset = 0;
                    foreach (var nestedField in fieldAssignment.NestedFields)
                    {
                        var containingType = field.Type;
                        if (ManagedClassTypes.IsClass(containingType))
                        {
                            _code.PointerOffset(32 + inlineOffset + field.Offset);
                            _code.Bytes(0x48, 0x8B, 0x00);
                            field = _program.Classes[ManagedClassTypes.ClassIndex(containingType)]
                                .Fields[nestedField];
                            inlineOffset = 0;
                        }
                        else
                        {
                            inlineOffset += field.Offset;
                            field = _program.Structures[(int)containingType - 100].Fields[nestedField];
                        }
                    }
                    _code.Store64(Spill(0));
                    SetManagedRoot(Spill(0), active: true);
                    var stagedBegin = _code.Position;
                    var stagedFunclet = -1;
                    if (_checkedExceptions && !_emittingFunclet)
                    {
                        stagedFunclet = _funclets.Count;
                        _funclets.Add((new BoundBlock([], fieldAssignment.Span), _currentFunction, 0, 0));
                        _managedStagedRoots.Add(stagedFunclet, [Spill(0)]);
                    }
                    Expression(fieldAssignment.Value, 1);
                    if (IsIndirect(field.Type))
                    {
                        _code.Bytes(0x48, 0x89, 0xC1); // preserve the source pointer without overwriting its spill
                    }
                    else
                    {
                        _code.Store64(Spill(1));
                    }
                    _code.Load64(Spill(0));
                    _code.PointerOffset(32 + inlineOffset + field.Offset);
                    _code.Bytes(0x48, 0x89, 0xC2);
                    if (IsIndirect(field.Type))
                    {
                        _code.Bytes(0x48, 0x89, 0xC8);
                        _code.CopyMemory(field.Size);
                    }
                    else
                    {
                        _code.Load64(Spill(1));
                        switch (field.Size)
                        {
                            case 1:
                                _code.Bytes(0x88, 0x02);
                                break;
                            case 2:
                                _code.Bytes(0x66, 0x89, 0x02);
                                break;
                            case 4:
                                _code.Bytes(0x89, 0x02);
                                break;
                            default:
                                _code.Bytes(0x48, 0x89, 0x02);
                                break;
                        }
                    }
                    SetManagedRoot(Spill(0), active: false);
                    if (stagedFunclet >= 0)
                    {
                        _code.Byte(0x90);
                        _scopes[_currentFunction].Add(new NativeExceptionScope(stagedBegin, _code.Position,
                            -stagedFunclet - 1, 0));
                    }
                }
                else
                {
                    foreach (var nestedField in fieldAssignment.NestedFields)
                    {
                        field = _program.Structures[(int)field.Type - 100].Fields[nestedField];
                        destination += field.Offset;
                    }
                    Expression(fieldAssignment.Value, 0);
                    if (fieldAssignment.Variable == 0 && IsBorrowedReceiver())
                    {
                        StoreValue(Spill(0), field.Type);
                        _code.Load64(Slot(0));
                        _code.PointerOffset(destination - Slot(0));
                        _code.Bytes(0x48, 0x89, 0xC2);
                        LoadValue(Spill(0), field.Type);
                        if (IsIndirect(field.Type))
                        {
                            _code.CopyMemory(field.Size);
                        }
                        else
                        {
                            switch (field.Size)
                            {
                                case 1:
                                    _code.Bytes(0x88, 0x02);
                                    break;
                                case 2:
                                    _code.Bytes(0x66, 0x89, 0x02);
                                    break;
                                case 4:
                                    _code.Bytes(0x89, 0x02);
                                    break;
                                default:
                                    _code.Bytes(0x48, 0x89, 0x02);
                                    break;
                            }
                        }
                    }
                    else if (IsIndirect(field.Type))
                    {
                        CopyToFrame(destination, field.Size);
                    }
                    else
                    {
                        _code.StoreField(destination, field.Size);
                    }
                }
                if (fieldAssignment.Variable == 0 && fieldAssignment.NestedFields.IsEmpty
                    && IsUnique(field.Type)
                    && _constructedFieldSlots[_currentFunction].Length > fieldAssignment.Field
                    && _constructedFieldSlots[_currentFunction][fieldAssignment.Field] >= 0)
                {
                    _code.Set(_constructedFieldSlots[_currentFunction][fieldAssignment.Field]);
                }
                break;
            case BoundBufferAssignment bufferWrite:
                EmitBufferIndex(bufferWrite.Buffer, bufferWrite.Index, bufferWrite.IsEndRelativeOffset, 0);
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
                        if (ManagedClassTypes.IsManaged(result.Value.Type))
                        {
                            SetManagedRoot(PendingReturnSlot(), active: true);
                        }
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
                var hasOutParameter = false;
                var currentFunction = _program.Functions[_currentFunction];
                var parameterOffset = currentFunction.IsConstructor ? 1 : 0;
                for (var i = parameterOffset;
                    i < currentFunction.ParameterCount + parameterOffset; i++)
                {
                    hasOutParameter |= currentFunction.Variables[i].IsOut;
                }
                if (currentFunction.ReturnType == PrimitiveType.Bool || hasOutParameter)
                {
                    var returnType = currentFunction.ReturnType;
                    if (returnType != PrimitiveType.Bool && returnType != PrimitiveType.Void
                        && !IsIndirect(returnType))
                    {
                        StoreValue(PendingReturnSlot(), returnType);
                    }
                    CommitOutParameters();
                    if (returnType != PrimitiveType.Bool && returnType != PrimitiveType.Void)
                    {
                        if (IsIndirect(returnType))
                        {
                            _code.Load64(_returnSlot);
                        }
                        else
                        {
                            LoadValue(PendingReturnSlot(), returnType);
                        }
                    }
                }
                if (_checkedExceptions)
                {
                    _code.Byte(0x90); // call-site continuation must not start at an unwind epilog
                }
                UnregisterManagedRoots(_currentFunction);
                Epilog();
                break;
            case BoundTryFinally cleanup:
                var funclet = _funclets.Count;
                _funclets.Add((cleanup.Finally, _currentFunction, 0, 0));
                _cleanups.Add(funclet);
                _normalExits.Add(new NormalExit(funclet, []));
                var protectedBegin = _code.Position;
                Statement(cleanup.Body);
                if (_checkedExceptions && !_emittingFunclet)
                {
                    _code.Byte(0x90);
                }
                var protectedEnd = _code.Position;
                _cleanups.RemoveAt(_cleanups.Count - 1);
                _normalExits.RemoveAt(_normalExits.Count - 1);
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
                    _normalExits.Add(new NormalExit(cleanupIndex, []));
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
                    _normalExits.RemoveAt(_normalExits.Count - 1);
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
                var exit = -1;
                if (!loop.IsPostTested)
                {
                    Expression(loop.Condition, 0);
                    _code.Bytes(0x85, 0xC0);
                    exit = _code.JumpIf(0x84);
                }
                var loopTarget = new LoopTarget(_normalExits.Count);
                _loopTargets.Add(loopTarget);
                Statement(loop.Body);
                var iteratorStart = _code.Position;
                if (loop.Iterator is not null)
                {
                    Statement(loop.Iterator);
                }
                var conditionStart = _code.Position;
                if (loop.IsPostTested)
                {
                    Expression(loop.Condition, 0);
                    _code.Bytes(0x85, 0xC0);
                    exit = _code.JumpIf(0x84);
                }
                foreach (var jump in loopTarget.Continues)
                {
                    _code.PatchRelative(jump, loop.IsPostTested ? conditionStart
                        : loop.Iterator is null ? start : iteratorStart);
                }
                _loopTargets.RemoveAt(_loopTargets.Count - 1);
                _code.PatchRelative(_code.Jump(), start);
                _code.PatchRelative(exit, _code.Position);
                foreach (var jump in loopTarget.Breaks)
                {
                    _code.PatchRelative(jump, _code.Position);
                }
                break;
            case BoundBreak or BoundContinue:
                if (_loopTargets.Count == 0)
                {
                    throw new ArgumentException("Loop transfer has no verified target.", nameof(statement));
                }
                var targetDepth = statement is BoundBreak exitLoop ? exitLoop.TargetDepth
                    : ((BoundContinue)statement).TargetDepth;
                if (targetDepth <= 0 || targetDepth > _loopTargets.Count)
                {
                    throw new ArgumentException("Loop transfer has no verified target.", nameof(statement));
                }
                var target = _loopTargets[^targetDepth];
                for (var index = _normalExits.Count - 1; index >= target.ExitDepth; index--)
                {
                    var normalExit = _normalExits[index];
                    if (normalExit.Cleanup >= 0)
                    {
                        CallCleanup(normalExit.Cleanup);
                    }
                    else
                    {
                        foreach (var retired in normalExit.RetiredRoots)
                        {
                            SetManagedRoot(OwnerPlace(retired), active: false);
                        }
                    }
                }
                var transfer = _code.Jump();
                (statement is BoundBreak ? target.Breaks : target.Continues).Add(transfer);
                break;
            default:
                throw new ArgumentException("Unknown native statement kind.", nameof(statement));
        }
    }

    [SuppressMessage("Style", "IDE0072:Populate switch", Justification = "The verified scalar layouts and eight-byte fallback cover all output slots.")]
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
        var conditional = _program.Functions[_currentFunction].ReturnType == PrimitiveType.Bool;
        var skip = -1;
        if (conditional)
        {
            _code.Bytes(0x85, 0xC0);
            skip = _code.JumpIf(0x84);
        }
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
        if (conditional)
        {
            _code.Bytes(0xB8, 0x01, 0x00, 0x00, 0x00);
            _code.PatchRelative(skip, _code.Position);
        }
    }

    private void CheckBufferIndex(int buffer)
    {
        if (_sourceDependent is not null)
        {
            _code.Bytes(0x3B, 0x85); // cmp eax,[rbp+sourceCount]
            _code.Int32(_sourceSlots[_currentFunction] + 8);
            RuntimeBranch(0x83, 3);
            return;
        }
        _code.Byte(0x3D);
        _code.Int32(_program.Buffers[buffer].Values.Length);
        RuntimeBranch(0x83, 3); // unsigned >= length rejects negative indexes too
    }

    private void EmitBufferIndex(int buffer, BoundExpression index, bool isEndRelative, int spill)
    {
        Expression(index, spill);
        if (isEndRelative)
        {
            _code.Bytes(0x85, 0xC0); // test eax,eax
            var negativeOffset = _code.JumpIf(0x88);
            _code.Byte(0x3D); // cmp eax,length
            _code.Int32(_program.Buffers[buffer].Values.Length);
            var outOfRange = _code.JumpIf(0x87);
            _code.Byte(0xB9); // mov ecx,length
            _code.Int32(_program.Buffers[buffer].Values.Length);
            _code.Bytes(0x29, 0xC1, 0x89, 0xC8); // ecx = length - eax; eax = ecx
            var converted = _code.Jump();
            _code.PatchRelative(negativeOffset, _code.Position);
            _code.PatchRelative(outOfRange, _code.Position);
            _code.Bytes(0xB8); // mov eax,-1
            _code.Int32(-1);
            _code.PatchRelative(converted, _code.Position);
        }
        CheckBufferIndex(buffer);
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
                    _code.Int32(constant.Type == PrimitiveType.Float32
                        ? unchecked((int)constant.Value)
                        : checked((int)constant.Value));
                }
                break;
            case BoundNull:
                _code.Bytes(0x31, 0xC0);
                break;
            case BoundOptionalLift lift:
                Expression(lift.Value, spill);
                break;
            case BoundCoalesce coalesce:
                Expression(coalesce.Optional, spill);
                _code.Bytes(0x48, 0x85, 0xC0);
                var present = _code.JumpIf(0x85);
                Expression(coalesce.Fallback, spill);
                _code.PatchRelative(present, _code.Position);
                break;
            case BoundReadonlyProjection projection:
                Expression(projection.Value, spill);
                break;
            case BoundIsNull test:
                Expression(test.Value, spill);
                _code.Bytes(0x48, 0x85, 0xC0, 0x0F, 0x94, 0xC0, 0x0F, 0xB6, 0xC0);
                break;
            case BoundIsSome pattern:
                Expression(pattern.Value, spill);
                _code.Bytes(0x48, 0x85, 0xC0);
                var empty = _code.JumpIf(0x84);
                _code.Store64(Slot(pattern.Variable));
                SetManagedRoot(Slot(pattern.Variable), active: true);
                _code.Bytes(0xB8, 0x01, 0x00, 0x00, 0x00);
                var patternDone = _code.Jump();
                _code.PatchRelative(empty, _code.Position);
                SetManagedRoot(Slot(pattern.Variable), active: false);
                _code.Bytes(0x31, 0xC0);
                _code.PatchRelative(patternDone, _code.Position);
                break;
            case BoundIsVariablePattern pattern:
                if (pattern.Value is not BoundRead source)
                {
                    Expression(pattern.Value, spill);
                    _code.Store64(Slot(pattern.Variable));
                    SetManagedRoot(Slot(pattern.Variable), active: true);
                    _code.Bytes(0xB8, 0x01, 0x00, 0x00, 0x00);
                    break;
                }

                var sourceSlot = OwnerPlace(source.Variable);
                if (!_managedRootFlags[_currentFunction].TryGetValue(sourceSlot, out var initializedFlag))
                {
                    throw new InvalidOperationException("Managed pattern source has no runtime initialization flag.");
                }

                // The active root bit tracks publication separately from pointer bits, including initialized null.
                _code.Load(initializedFlag);
                _code.Bytes(0x85, 0xC0);
                var vacant = _code.JumpIf(0x84);
                _code.Load64(sourceSlot);
                var absent = -1;
                if (!pattern.IncludeNull)
                {
                    _code.Bytes(0x48, 0x85, 0xC0);
                    absent = _code.JumpIf(0x84);
                }
                _code.Store64(Slot(pattern.Variable));
                SetManagedRoot(Slot(pattern.Variable), active: true);
                _code.Bytes(0xB8, 0x01, 0x00, 0x00, 0x00);
                var variablePatternDone = _code.Jump();
                _code.PatchRelative(vacant, _code.Position);
                if (absent >= 0)
                {
                    _code.PatchRelative(absent, _code.Position);
                }
                SetManagedRoot(Slot(pattern.Variable), active: false);
                _code.Bytes(0x31, 0xC0);
                _code.PatchRelative(variablePatternDone, _code.Position);
                break;
            case BoundIsInitialized initializationTest:
                EmitInitializationTest(initializationTest.Variable);
                break;
            case BoundRead read:
                if (read.Variable == 0 && IsBorrowedReceiver() && !IsUnique(read.Type))
                {
                    _code.Load64(Slot(0));
                    if (!IsIndirect(read.Type))
                    {
                        _code.LoadPointerField(0, Size(read.Type));
                    }
                }
                else
                {
                    LoadValue(_program.Functions[_currentFunction].Variables[read.Variable].IsOut
                        ? OutSlot(read.Variable) : Slot(read.Variable), read.Type);
                }
                break;
            case BoundViewLength length:
                _code.Load(Slot(length.Variable) + 8);
                break;
            case BoundBufferLength length:
                if (_sourceDependent is not null)
                {
                    _code.Load(_sourceSlots[_currentFunction] + 8);
                }
                else
                {
                    _code.Byte(0xB8);
                    _code.Int32(_program.Buffers[length.Buffer].Values.Length);
                }
                break;
            case BoundSnapshotSpan snapshot:
                EmitSnapshotSpan(snapshot, spill);
                break;
            case BoundSpanLength length:
                Expression(length.View, spill);
                _code.LoadPointerField(TokenSpanContract.CountOffset, NativeTargetProfile.PointerSize);
                break;
            case BoundSpanRead read:
                EmitSpanRead(read, spill);
                break;
            case BoundSpanAdvance advance:
                Expression(advance.View, spill);
                CopyToFrame(Spill(spill), TokenSpanContract.DescriptorSize);
                _code.Load64(Spill(spill) + TokenSpanContract.CountOffset);
                _code.Bytes(0x48, 0x85, 0xC0);
                RuntimeBranch(0x84, 2);
                _code.Bytes(0x48, 0x83, 0xE8, 0x01);
                _code.Store64(Spill(spill) + TokenSpanContract.CountOffset);
                _code.Load64(Spill(spill));
                _code.Bytes(0x48, 0x83, 0xC0, TokenSpanContract.Stride);
                RuntimeBranch(0x82, 0);
                _code.Store64(Spill(spill));
                _code.Address(Spill(spill));
                break;
            case BoundViewRead viewRead:
                Expression(viewRead.Index, spill);
                _code.Bytes(0x89, 0xC1); // ecx = index
                _code.Load(Slot(viewRead.Variable) + 8);
                _code.Bytes(0x39, 0xC1); // unsigned index >= length also rejects negative indexes
                RuntimeBranch(0x83, 2);
                _code.Load64(Slot(viewRead.Variable), destinationRegister: true);
                _code.Bytes(0x8B, 0x04, 0x8A);
                break;
            case BoundMove move:
                LoadValue(OwnerPlace(move.Variable), move.Type);
                _code.Clear(_liveSlots[_currentFunction][move.Variable]);
                break;
            case BoundReceiverLoan loan:
                if (loan.Variable == 0 && IsBorrowedReceiver())
                {
                    _code.Load64(Slot(0));
                }
                else
                {
                    _code.Address(OwnerPlace(loan.Variable));
                }
                break;
            case BoundFieldLoan fieldLoan:
                EmitUniqueFieldPlace(fieldLoan.Place);
                break;
            case BoundMutableFieldLoan mutableField:
                EmitUniqueFieldPlace(mutableField.Place);
                break;
            case BoundMutableReceiverLoan mutableLoan:
                if (mutableLoan.Variable == 0 && IsBorrowedReceiver())
                {
                    _code.Load64(Slot(0));
                }
                else
                {
                    _code.Address(OwnerPlace(mutableLoan.Variable));
                }
                break;
            case BoundFieldRead fieldRead:
                if (ManagedClassTypes.IsClass(fieldRead.Receiver.Type))
                {
                    Expression(fieldRead.Receiver, spill);
                    var classField = _program.Classes[ManagedClassTypes.ClassIndex(
                        fieldRead.Receiver.Type)].Fields[fieldRead.Field];
                    if (IsIndirect(classField.Type))
                    {
                        _code.PointerOffset(32 + classField.Offset);
                    }
                    else
                    {
                        _code.LoadPointerField(32 + classField.Offset, classField.Size);
                    }
                    break;
                }
                if (IsUnique(fieldRead.Receiver.Type))
                {
                    EmitUniqueFieldPlace(fieldRead.Receiver);
                }
                else
                {
                    Expression(fieldRead.Receiver, spill);
                }
                var field = _program.Structures[(int)fieldRead.Receiver.Type - 100].Fields[fieldRead.Field];
                if (IsIndirect(fieldRead.Receiver.Type) || IsUnique(fieldRead.Receiver.Type))
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
                EmitBufferIndex(bufferRead.Buffer, bufferRead.Index, bufferRead.IsEndRelativeOffset, spill);
                if (_sourceDependent is not null)
                {
                    _code.Bytes(0x48, 0x8B, 0x95); // mov rdx,[rbp+sourcePointer]
                    _code.Int32(_sourceSlots[_currentFunction]);
                    _code.Bytes(0x0F, 0xB7, 0x04, 0x42); // movzx eax,word ptr [rdx+rax*2]
                }
                else
                {
                    _dataFixups.Add(new NativeDataFixup(_code.DataAddress(), _bufferOffsets[bufferRead.Buffer]));
                    _code.Bytes(0x8B, 0x04, 0x82); // mov eax,[rdx+rax*4]
                }
                break;
            case BoundUnary unary:
                Expression(unary.Operand, spill);
                switch (unary.Operator)
                {
                    case TokenKind.Minus:
                        if (unary.Type is PrimitiveType.Float32 or PrimitiveType.Float64)
                        {
                            if (unary.Type == PrimitiveType.Float64)
                            {
                                _code.Byte(0x48);
                            }
                            _code.Bytes(0x0F, 0xBA, 0xF8, unary.Type == PrimitiveType.Float32 ? (byte)31 : (byte)63);
                        }
                        else
                        {
                            if (IsWide(unary.Type))
                            {
                                _code.Byte(0x48);
                            }
                            _code.Bytes(0xF7, 0xD8);
                            RuntimeBranch(0x80, 0);
                        }
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
            case BoundUpdate update:
                var rootedUpdate = UpdateAddress(update.Place, spill);
                _code.Store64(Spill(spill + 1));
                _code.LoadPointerField(0, IsWide(update.Type) ? 8 : 4);
                StoreValue(Spill(spill + 2), update.Type);
                var updateBegin = _code.Position;
                var updateFunclet = -1;
                if (rootedUpdate && _checkedExceptions && !_emittingFunclet)
                {
                    updateFunclet = _funclets.Count;
                    _funclets.Add((new BoundBlock([], update.Span), _currentFunction, 0, 0));
                    _managedStagedRoots.Add(updateFunclet, [Spill(spill)]);
                }
                if (IsWide(update.Type))
                {
                    _code.Byte(0x48);
                }
                _code.Bytes(0x83, update.Operator == TokenKind.PlusPlus ? (byte)0xC0 : (byte)0xE8, 0x01);
                RuntimeBranch(update.Type is PrimitiveType.UInt32 or PrimitiveType.UInt64
                    ? (byte)0x82 : (byte)0x80, 0);
                _code.Load64(Spill(spill + 1), destinationRegister: true);
                if (IsWide(update.Type))
                {
                    _code.Byte(0x48);
                }
                _code.Bytes(0x89, 0x02);
                if (updateFunclet >= 0)
                {
                    _code.Byte(0x90);
                    _scopes[_currentFunction].Add(new NativeExceptionScope(updateBegin, _code.Position,
                        -updateFunclet - 1, 0));
                }
                if (rootedUpdate)
                {
                    SetManagedRoot(Spill(spill), active: false);
                }
                if (!update.IsPrefix)
                {
                    LoadValue(Spill(spill + 2), update.Type);
                }
                break;
            case BoundCompoundAssignment compound:
            {
                var rooted = UpdateAddress(compound.Place, spill);
                _code.Store64(Spill(spill + 1));
                _code.LoadPointerField(0, IsWide(compound.Type) ? 8 : 4);
                StoreValue(Spill(spill + 2), compound.Type);
                var begin = _code.Position;
                var funclet = -1;
                if (rooted && _checkedExceptions && !_emittingFunclet)
                {
                    funclet = _funclets.Count;
                    _funclets.Add((new BoundBlock([], compound.Span), _currentFunction, 0, 0));
                    _managedStagedRoots.Add(funclet, [Spill(spill)]);
                }
                Expression(compound.Right, spill + 4);
                if (IsWide(compound.Right.Type))
                {
                    _code.Bytes(0x48, 0x89, 0xC1);
                }
                else
                {
                    _code.Bytes(0x89, 0xC1);
                }
                LoadValue(Spill(spill + 2), compound.Type);
                Binary(CompoundOperators.Binary(compound.Operator), compound.Type);
                _code.Load64(Spill(spill + 1), destinationRegister: true);
                if (IsWide(compound.Type))
                {
                    _code.Byte(0x48);
                }
                _code.Bytes(0x89, 0x02);
                if (funclet >= 0)
                {
                    _code.Byte(0x90);
                    _scopes[_currentFunction].Add(new NativeExceptionScope(begin, _code.Position,
                        -funclet - 1, 0));
                }
                if (rooted)
                {
                    SetManagedRoot(Spill(spill), active: false);
                }
                break;
            }
            case BoundNominalUpdate update:
            {
                var rooted = UpdateAddress(update.Place, spill);
                _code.Store64(Spill(spill + 1));
                _code.AddressToRdx(Spill(spill + 2));
                _code.CopyMemory(Size(update.Type));
                var callBegin = _code.Position;
                var rootFunclet = -1;
                if (rooted && _checkedExceptions && !_emittingFunclet)
                {
                    rootFunclet = _funclets.Count;
                    _funclets.Add((new BoundBlock([], update.Span), _currentFunction, 0, 0));
                    _managedStagedRoots.Add(rootFunclet, [Spill(spill)]);
                }
                LoadValue(Spill(spill + 2), update.Type);
                _code.MoveArgument(IsIndirect(update.Type) ? 1 : 0, true);
                if (IsIndirect(update.Type))
                {
                    _code.Address(Spill(spill + 3));
                    _code.MoveArgument(0, true);
                }
                EmitTargetCall(update.Function);
                if (!IsIndirect(update.Type))
                {
                    StoreValue(Spill(spill + 3), update.Type);
                }
                if (rootFunclet >= 0)
                {
                    _code.Byte(0x90);
                    _scopes[_currentFunction].Add(new NativeExceptionScope(callBegin, _code.Position,
                        -rootFunclet - 1, 0));
                }
                _code.Address(Spill(spill + 3));
                _code.Load64(Spill(spill + 1), destinationRegister: true);
                _code.CopyMemory(Size(update.Type));
                if (rooted)
                {
                    SetManagedRoot(Spill(spill), active: false);
                }
                LoadValue(Spill(spill + (update.IsPrefix ? 3 : 2)), update.Type);
                break;
            }
            case BoundCast cast:
                Expression(cast.Operand, spill);
                Cast(cast.Operand.Type, cast.Type, spill);
                break;
            case TrRmScalarConvert conversion:
                Expression(conversion.Operand, spill);
                ConvertInteger(conversion.Operand.Type, conversion.Type, conversion.Policy);
                break;
            case TrRmScalarReinterpret reinterpret:
                Expression(reinterpret.Operand, spill);
                break;
            case TrRmScalarWrappingBinary wrapping:
                Expression(wrapping.Left, spill);
                StoreValue(Spill(spill), wrapping.Type);
                Expression(wrapping.Right, spill + 1);
                if (IsWide(wrapping.Type))
                {
                    _code.Bytes(0x48, 0x89, 0xC1);
                }
                else
                {
                    _code.Bytes(0x89, 0xC1);
                }
                LoadValue(Spill(spill), wrapping.Type);
                WrappingBinary(wrapping.Operator, wrapping.Type);
                break;
            case TrRmScalarOptionalNone none:
                if (IsIndirect(none.Type))
                {
                    ZeroStruct(Spill(spill), Size(none.Type));
                    _code.Address(Spill(spill));
                }
                else
                {
                    _code.Bytes(0x31, 0xC0);
                }
                break;
            case TrRmScalarOptionalLift lift:
            {
                var storage = Spill(spill);
                ZeroStruct(storage, Size(lift.Type));
                Expression(lift.Value, spill + 1);
                StoreValue(storage, lift.Value.Type);
                _code.Byte(0xB8);
                _code.Int32(1);
                _code.StoreField(storage + Size(lift.Value.Type), 1);
                if (IsIndirect(lift.Type))
                {
                    _code.Address(storage);
                }
                else
                {
                    _code.Load64(storage);
                }
                break;
            }
            case TrRmScalarOptionalPresence presence:
                Expression(presence.Optional, spill);
                EmitOptionalPresenceInAccumulator(presence.Optional.Type);
                break;
            case TrRmScalarOptionalEquality equality:
                EmitOptionalEquality(equality, spill);
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
                var floating = binary.Left.Type is PrimitiveType.Float32 or PrimitiveType.Float64;
                Expression(binary.Left, spill);
                if (!floating && (TryEmitUnsignedPowerOfTwoBinary(binary) || TryEmitSignedPowerOfTwoBinary(binary)))
                {
                    break;
                }
                StoreValue(Spill(spill), binary.Left.Type);
                Expression(binary.Right, spill + 1);
                if (floating)
                {
                    FloatingBinary(binary.Operator, binary.Left.Type, spill);
                }
                else
                {
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
                }
                break;
            case BoundConditional conditional:
            {
                Expression(conditional.Condition, spill);
                _code.Bytes(0x85, 0xC0);
                var whenFalse = _code.JumpIf(0x84);
                Expression(conditional.WhenTrue, spill);
                var done = _code.Jump();
                _code.PatchRelative(whenFalse, _code.Position);
                Expression(conditional.WhenFalse, spill);
                _code.PatchRelative(done, _code.Position);
                break;
            }
            case BoundCall call:
                var argumentsBase = spill + 1;
                List<(int Value, int Live, PrimitiveType Type)>? staged = null;
                for (var i = 0; i < call.Arguments.Length; i++)
                {
                    if (!IsUnique(call.Arguments[i].Type)
                        || call.Arguments[i] is BoundReceiverLoan or BoundMutableReceiverLoan
                            or BoundFieldLoan or BoundMutableFieldLoan or BoundOutArgument)
                    {
                        continue;
                    }
                    staged ??= [];
                    var live = Spill(argumentsBase + call.Arguments.Length + i);
                    staged.Add((Spill(argumentsBase + i), live, call.Arguments[i].Type));
                    _code.Clear(live);
                }
                var stagedBegin = _code.Position;
                var stagedFunclet = -1;
                var managedFunclet = -1;
                if (_checkedExceptions && _managedNodes && !_emittingFunclet)
                {
                    List<int>? managedSlots = null;
                    for (var i = 0; i < call.Arguments.Length; i++)
                    {
                        if (ManagedClassTypes.IsManaged(call.Arguments[i].Type))
                        {
                            managedSlots ??= [];
                            managedSlots.Add(Spill(argumentsBase + i));
                            if (call.Arguments[i] is BoundOutArgument)
                            {
                                managedSlots.Add(Spill(argumentsBase + call.Arguments.Length + i));
                            }
                        }
                    }
                    if (managedSlots is not null)
                    {
                        managedFunclet = _funclets.Count;
                        _funclets.Add((new BoundBlock([], call.Span), _currentFunction, 0, 0));
                        _managedStagedRoots.Add(managedFunclet, managedSlots);
                    }
                }
                if (_checkedExceptions && staged is not null && !_emittingFunclet)
                {
                    stagedFunclet = _funclets.Count;
                    _funclets.Add((new BoundBlock([], call.Span), _currentFunction, 0, 0));
                    _stagedOwners.Add(stagedFunclet, staged);
                }
                for (var i = 0; i < call.Arguments.Length; i++)
                {
                    if (call.Arguments[i] is BoundOutArgument)
                    {
                        var scratch = Spill(argumentsBase + call.Arguments.Length + i);
                        ZeroStruct(scratch, IsIndirect(call.Arguments[i].Type) ? Size(call.Arguments[i].Type) : 8);
                        if (ManagedClassTypes.IsManaged(call.Arguments[i].Type))
                        {
                            SetManagedRoot(scratch, active: true);
                        }
                        _code.Address(scratch);
                        _code.Store64(Spill(argumentsBase + i));
                    }
                    else
                    {
                        Expression(call.Arguments[i], argumentsBase + (call.Arguments.Length * 2));
                        if (call.Arguments[i] is BoundReceiverLoan or BoundMutableReceiverLoan
                            or BoundFieldLoan or BoundMutableFieldLoan)
                        {
                            _code.Store64(Spill(argumentsBase + i));
                        }
                        else
                        {
                            StoreValue(Spill(argumentsBase + i), call.Arguments[i].Type);
                            if (ManagedClassTypes.IsManaged(call.Arguments[i].Type))
                            {
                                SetManagedRoot(Spill(argumentsBase + i), active: true);
                            }
                        }
                        if (staged is not null && IsUnique(call.Arguments[i].Type)
                            && call.Arguments[i] is not (BoundReceiverLoan or BoundMutableReceiverLoan
                                or BoundFieldLoan or BoundMutableFieldLoan))
                        {
                            _code.Set(Spill(argumentsBase + call.Arguments.Length + i));
                        }
                    }
                }
                if (stagedFunclet >= 0)
                {
                    _code.Byte(0x90);
                    _scopes[_currentFunction].Add(new NativeExceptionScope(stagedBegin, _code.Position,
                        -stagedFunclet - 1, 0));
                }
                if (staged is not null)
                {
                    foreach (var stagedOwner in staged)
                    {
                        _code.Clear(stagedOwner.Live);
                    }
                }
                foreach (var argument in call.Arguments)
                {
                    if (argument is BoundOutArgument output && IsUnique(output.Type))
                    {
                        DropOwner(output.Variable);
                    }
                    if (argument is BoundOutArgument managedOutput
                        && ManagedClassTypes.IsManaged(managedOutput.Type))
                    {
                        SetManagedRoot(OwnerPlace(managedOutput.Variable), active: false);
                    }
                }
                var hiddenReturn = IsIndirect(call.Type) ? 1 : 0;
                var sourceHidden = _sourceDependent?[call.Function] == true ? 2 : 0;
                var hidden = hiddenReturn + sourceHidden;
                for (var i = Math.Max(0, 4 - hidden); i < call.Arguments.Length; i++)
                {
                    var argumentIndex = call.ArgumentIndex(i);
                    if (call.Arguments[argumentIndex] is BoundOutArgument or BoundReceiverLoan or BoundMutableReceiverLoan
                        or BoundFieldLoan or BoundMutableFieldLoan)
                    {
                        _code.Load64(Spill(argumentsBase + argumentIndex));
                    }
                    else
                    {
                        LoadValue(Spill(argumentsBase + argumentIndex), call.Arguments[argumentIndex].Type);
                    }
                    _code.StoreOutgoing64(32 + (8 * (i + hidden - 4)));
                }
                for (var i = 0; i < Math.Min(4 - hidden, call.Arguments.Length); i++)
                {
                    var argumentIndex = call.ArgumentIndex(i);
                    if (call.Arguments[argumentIndex] is BoundOutArgument or BoundReceiverLoan or BoundMutableReceiverLoan
                        or BoundFieldLoan or BoundMutableFieldLoan)
                    {
                        _code.Load64(Spill(argumentsBase + argumentIndex));
                    }
                    else
                    {
                        LoadValue(Spill(argumentsBase + argumentIndex), call.Arguments[argumentIndex].Type);
                    }
                    _code.MoveArgument(i + hidden,
                        call.Arguments[argumentIndex] is BoundOutArgument or BoundReceiverLoan or BoundMutableReceiverLoan
                            or BoundFieldLoan or BoundMutableFieldLoan
                            || IsStruct(call.Arguments[argumentIndex].Type) || IsWide(call.Arguments[argumentIndex].Type));
                }
                if (sourceHidden != 0)
                {
                    _code.Load64(_sourceSlots[_currentFunction]);
                    _code.MoveArgument(0, true);
                    _code.Load(_sourceSlots[_currentFunction] + 8);
                    _code.MoveArgument(1, false);
                }
                if (hiddenReturn != 0)
                {
                    _code.Address(Spill(spill));
                    _code.MoveArgument(0, true);
                }
                EmitTargetCall(call.Function);
                if (hiddenReturn != 0)
                {
                    _code.Address(Spill(spill));
                }
                var hasOut = false;
                foreach (var argument in call.Arguments)
                {
                    hasOut |= argument is BoundOutArgument;
                }
                if (hasOut)
                {
                    var conditionalOutputs = call.Type == PrimitiveType.Bool;
                    var skip = -1;
                    if (conditionalOutputs)
                    {
                        _code.Bytes(0x85, 0xC0);
                        skip = _code.JumpIf(0x84);
                    }
                    else if (call.Type != PrimitiveType.Void && !IsIndirect(call.Type))
                    {
                        StoreValue(Spill(spill), call.Type);
                    }
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
                                if (IsUnique(output.Type))
                                {
                                    _code.Set(_liveSlots[_currentFunction][output.Variable]);
                                }
                                SetInitializationFlag(output.Variable, initialized: true);
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
                            if (ManagedClassTypes.IsManaged(output.Type))
                            {
                                SetManagedRoot(destination, active: true);
                            }
                            if (IsUnique(output.Type))
                            {
                                _code.Set(_liveSlots[_currentFunction][output.Variable]);
                            }
                            SetInitializationFlag(output.Variable, initialized: true);
                        }
                    }
                    if (conditionalOutputs)
                    {
                        _code.Bytes([0xB8, 0x01, 0x00, 0x00, 0x00]);
                        var done = _code.Jump();
                        _code.PatchRelative(skip, _code.Position);
                        for (var i = 0; i < call.Arguments.Length; i++)
                        {
                            if (call.Arguments[i] is BoundOutArgument output)
                            {
                                if (IsUnique(output.Type))
                                {
                                    _code.Clear(_liveSlots[_currentFunction][output.Variable]);
                                }
                                SetInitializationFlag(output.Variable, initialized: false);
                            }
                        }
                        _code.PatchRelative(done, _code.Position);
                    }
                    else if (call.Type != PrimitiveType.Void)
                    {
                        if (IsIndirect(call.Type))
                        {
                            _code.Address(Spill(spill));
                        }
                        else
                        {
                            LoadValue(Spill(spill), call.Type);
                        }
                    }
                }
                for (var i = 0; i < call.Arguments.Length; i++)
                {
                    if (ManagedClassTypes.IsManaged(call.Arguments[i].Type))
                    {
                        SetManagedRoot(Spill(argumentsBase + i), active: false);
                        if (call.Arguments[i] is BoundOutArgument)
                        {
                            SetManagedRoot(Spill(argumentsBase + call.Arguments.Length + i), active: false);
                        }
                    }
                }
                if (managedFunclet >= 0)
                {
                    _code.Byte(0x90);
                    _scopes[_currentFunction].Add(new NativeExceptionScope(stagedBegin, _code.Position,
                        -managedFunclet - 1, 0));
                }
                break;
        }
    }

    private void Cast(PrimitiveType source, PrimitiveType target, int spill)
    {
        if (!RequiresFloatingEnvironment(source, target))
        {
            CastPrimitive(source, target, 0, 0);
            return;
        }

        var operand = Spill(spill + 2);
        // MXCSR setup uses EAX, so preserve the conversion payload before changing the control state.
        StoreValue(operand, source);
        var savedMxcsr = Spill(spill);
        var controlledMxcsr = Spill(spill + 1);
        BeginFloatingEnvironment(savedMxcsr, controlledMxcsr);
        LoadValue(operand, source);
        CastPrimitive(source, target, savedMxcsr, controlledMxcsr);
        _code.LoadMxcsr(savedMxcsr);
    }

    private void CastPrimitive(PrimitiveType source, PrimitiveType target, int savedMxcsr, int controlledMxcsr)
    {
        if (source is PrimitiveType.Float32 or PrimitiveType.Float64
            && target is PrimitiveType.Int32 or PrimitiveType.UInt32 or PrimitiveType.Int64 or PrimitiveType.UInt64)
        {
            EmitFloatingToInteger(source, target, savedMxcsr, controlledMxcsr);
            return;
        }
        if (target is PrimitiveType.Float32 or PrimitiveType.Float64
            && source is PrimitiveType.Int32 or PrimitiveType.UInt32 or PrimitiveType.Int64
                or PrimitiveType.UInt64 or PrimitiveType.Char16)
        {
            if (source == PrimitiveType.UInt64)
            {
                EmitUInt64ToFloating(target);
            }
            else
            {
                if (source == PrimitiveType.Char16)
                {
                    _code.Bytes(0x0F, 0xB7, 0xC0); // movzx eax,ax
                }
                EmitIntegerToFloating(source, target);
            }
            return;
        }
        if (source == PrimitiveType.Float32 && target == PrimitiveType.Float64)
        {
            _code.Bytes(0x66, 0x0F, 0x6E, 0xC0); // movd xmm0,eax
            _code.Bytes(0xF3, 0x0F, 0x5A, 0xC0); // cvtss2sd xmm0,xmm0
            _code.Bytes(0x66, 0x48, 0x0F, 0x7E, 0xC0); // movq rax,xmm0
            return;
        }
        if (source == PrimitiveType.Float64 && target == PrimitiveType.Float32)
        {
            _code.Bytes(0x66, 0x48, 0x0F, 0x6E, 0xC0); // movq xmm0,rax
            _code.Bytes(0xF2, 0x0F, 0x5A, 0xC0); // cvtsd2ss xmm0,xmm0
            _code.Bytes(0x66, 0x0F, 0x7E, 0xC0); // movd eax,xmm0
            return;
        }
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

    private void EmitFloatingToInteger(PrimitiveType source, PrimitiveType target, int savedMxcsr, int controlledMxcsr)
    {
        if (source == PrimitiveType.Float32)
        {
            _code.Bytes(0x66, 0x0F, 0x6E, 0xC0); // movd xmm0,eax
            _code.Bytes(0xF3, 0x0F, 0x5A, 0xC0); // cvtss2sd xmm0,xmm0
        }
        else
        {
            _code.Bytes(0x66, 0x48, 0x0F, 0x6E, 0xC0); // movq xmm0,rax
        }

        _code.Bytes(0x66, 0x0F, 0x2E, 0xC0); // ucomisd xmm0,xmm0 rejects NaN
        FloatingRuntimeBranch(0x8A, 0, savedMxcsr, controlledMxcsr);
        switch (target)
        {
            case PrimitiveType.Int32:
                CompareFloat64(-2147483649.0);
                FloatingRuntimeBranch(0x86, 0, savedMxcsr, controlledMxcsr);
                CompareFloat64(2147483648.0);
                FloatingRuntimeBranch(0x83, 0, savedMxcsr, controlledMxcsr);
                break;
            case PrimitiveType.UInt32:
                CompareFloat64(-1.0);
                FloatingRuntimeBranch(0x86, 0, savedMxcsr, controlledMxcsr);
                CompareFloat64(4294967296.0);
                FloatingRuntimeBranch(0x83, 0, savedMxcsr, controlledMxcsr);
                break;
            case PrimitiveType.Int64:
                CompareFloat64(-9223372036854775808.0);
                FloatingRuntimeBranch(0x82, 0, savedMxcsr, controlledMxcsr);
                CompareFloat64(9223372036854775808.0);
                FloatingRuntimeBranch(0x83, 0, savedMxcsr, controlledMxcsr);
                break;
            case PrimitiveType.UInt64:
                CompareFloat64(-1.0);
                FloatingRuntimeBranch(0x86, 0, savedMxcsr, controlledMxcsr);
                CompareFloat64(18446744073709551616.0);
                FloatingRuntimeBranch(0x83, 0, savedMxcsr, controlledMxcsr);
                CompareFloat64(9223372036854775808.0);
                var belowSignedLimit = _code.JumpIf(0x82);
                _code.Bytes(0xF2, 0x0F, 0x5C, 0xC1); // subtract 2^63 before the signed conversion
                _code.Bytes(0xF2, 0x48, 0x0F, 0x2C, 0xC0);
                _code.Bytes(0x48, 0xBA);
                _code.Int64(long.MinValue);
                _code.Bytes(0x48, 0x09, 0xD0); // restore the unsigned high bit
                var completedUnsigned = _code.Jump();
                _code.PatchRelative(belowSignedLimit, _code.Position);
                _code.Bytes(0xF2, 0x48, 0x0F, 0x2C, 0xC0);
                _code.PatchRelative(completedUnsigned, _code.Position);
                break;
            default:
                throw new InvalidOperationException("Floating-to-integer target was not verified.");
        }
        if (target != PrimitiveType.UInt64)
        {
            _code.Bytes(0xF2, 0x48, 0x0F, 0x2C, 0xC0); // cvttsd2si rax,xmm0
        }
    }

    private void CompareFloat64(double value)
    {
        _code.Bytes(0x48, 0xB8);
        _code.Int64(BitConverter.DoubleToInt64Bits(value));
        _code.Bytes(0x66, 0x48, 0x0F, 0x6E, 0xC8); // movq xmm1,rax
        _code.Bytes(0x66, 0x0F, 0x2E, 0xC1); // ucomisd xmm0,xmm1
    }

    private void EmitIntegerToFloating(PrimitiveType source, PrimitiveType target)
    {
        _code.Byte(target == PrimitiveType.Float32 ? (byte)0xF3 : (byte)0xF2);
        if (source is PrimitiveType.UInt32 or PrimitiveType.Int64)
        {
            _code.Byte(0x48);
        }
        _code.Bytes(0x0F, 0x2A, 0xC0);
        EmitFloatingResult(target);
    }

    private void EmitUInt64ToFloating(PrimitiveType target)
    {
        // CVTSI2S treats a 64-bit source as signed, so round the high unsigned half explicitly.
        _code.Bytes(0x48, 0x85, 0xC0);
        var highUnsigned = _code.JumpIf(0x88);
        EmitIntegerToFloating(PrimitiveType.Int64, target);
        var complete = _code.Jump();
        _code.PatchRelative(highUnsigned, _code.Position);

        var precision = target == PrimitiveType.Float32 ? 24 : 53;
        var shift = 64 - precision;
        _code.Bytes(0x48, 0x89, 0xC1); // mov rcx,rax
        _code.Bytes(0x48, 0xC1, 0xE8, (byte)shift); // q = value >> shift

        _code.Bytes(0x48, 0x89, 0xCA); // mov rdx,rcx
        _code.Bytes(0x48, 0xC1, 0xEA, (byte)(shift - 1));
        _code.Bytes(0x83, 0xE2, 0x01); // guard bit
        _code.Bytes(0x85, 0xD2);
        var noGuardBit = _code.JumpIf(0x84);

        _code.Bytes(0x48, 0x89, 0xCA);
        _code.Bytes(0x48, 0xC1, 0xE2, (byte)(64 - (shift - 1)));
        _code.Bytes(0x48, 0x85, 0xD2); // sticky bits below the guard bit
        var tie = _code.JumpIf(0x84);
        _code.Bytes(0x48, 0x83, 0xC0, 0x01);
        var roundedUp = _code.Jump();
        _code.PatchRelative(tie, _code.Position);
        _code.Bytes(0x48, 0xA9, 0x01, 0x00, 0x00, 0x00); // ties round to an even significand
        var evenSignificand = _code.JumpIf(0x84);
        _code.Bytes(0x48, 0x83, 0xC0, 0x01);
        _code.PatchRelative(evenSignificand, _code.Position);
        _code.PatchRelative(roundedUp, _code.Position);
        _code.PatchRelative(noGuardBit, _code.Position);

        _code.Bytes(0x48, 0x0F, 0xBA, 0xE0, (byte)precision); // test for significand carry
        var carry = _code.JumpIf(0x82);
        EmitUInt64FloatingBits(target, 63);
        var packed = _code.Jump();
        _code.PatchRelative(carry, _code.Position);
        _code.Bytes(0x48, 0xC1, 0xE8, 0x01);
        EmitUInt64FloatingBits(target, 64);
        _code.PatchRelative(packed, _code.Position);
        _code.PatchRelative(complete, _code.Position);
    }

    private void EmitUInt64FloatingBits(PrimitiveType target, int exponent)
    {
        var precision = target == PrimitiveType.Float32 ? 24 : 53;
        var fractionMask = (1UL << (precision - 1)) - 1;
        var exponentBias = target == PrimitiveType.Float32 ? 127 : 1023;
        var exponentBits = (ulong)(exponent + exponentBias) << (precision - 1);
        _code.Bytes(0x48, 0xBA);
        _code.Int64(unchecked((long)fractionMask));
        _code.Bytes(0x48, 0x21, 0xD0); // retain the fraction bits
        _code.Bytes(0x48, 0xBA);
        _code.Int64(unchecked((long)exponentBits));
        _code.Bytes(0x48, 0x09, 0xD0); // add the biased exponent
    }

    private void EmitFloatingResult(PrimitiveType target)
    {
        if (target == PrimitiveType.Float32)
        {
            _code.Bytes(0x66, 0x0F, 0x7E, 0xC0); // movd eax,xmm0
        }
        else
        {
            _code.Bytes(0x66, 0x48, 0x0F, 0x7E, 0xC0); // movq rax,xmm0
        }
    }

    private void CheckedDivRem(PrimitiveType type)
    {
        var isWide = IsWide(type);
        var isUnsigned = type is PrimitiveType.UInt32 or PrimitiveType.UInt64;
        if (isWide)
        {
            _code.Bytes(0x48, 0x85, 0xC9);
            RuntimeBranch(0x84, 1);
            if (isUnsigned)
            {
                _code.Bytes(0x31, 0xD2, 0x48, 0xF7, 0xF1);
                return;
            }
            _code.Bytes(0x48, 0xBA);
            _code.Int64(long.MinValue);
            _code.Bytes(0x48, 0x39, 0xD0);
            var normal = _code.JumpIf(0x85);
            _code.Bytes(0x48, 0x83, 0xF9, 0xFF);
            var normalAgain = _code.JumpIf(0x85);
            _runtimeCalls.Add((_code.RelativeCall(), 0));
            _code.PatchRelative(normal, _code.Position);
            _code.PatchRelative(normalAgain, _code.Position);
            _code.Bytes(0x48, 0x99, 0x48, 0xF7, 0xF9);
            return;
        }

        _code.Bytes(0x85, 0xC9);
        RuntimeBranch(0x84, 1);
        if (isUnsigned)
        {
            _code.Bytes(0x31, 0xD2, 0xF7, 0xF1);
            return;
        }
        _code.Byte(0x3D);
        _code.Int32(int.MinValue);
        var normal32 = _code.JumpIf(0x85);
        _code.Bytes(0x83, 0xF9, 0xFF);
        var normalAgain32 = _code.JumpIf(0x85);
        _runtimeCalls.Add((_code.RelativeCall(), 0));
        _code.PatchRelative(normal32, _code.Position);
        _code.PatchRelative(normalAgain32, _code.Position);
        _code.Bytes(0x99, 0xF7, 0xF9);
    }

    private void ConvertInteger(PrimitiveType source, PrimitiveType target, byte policy)
    {
        if (policy == 1)
        {
            if (source == PrimitiveType.Int32 && IsWide(target))
            {
                _code.Bytes(0x48, 0x63, 0xC0); // Sign-extend the source value before widening.
            }
            return;
        }
        if (policy != 2)
        {
            throw new InvalidOperationException("TRRM integer conversion policy was not verified.");
        }
        if (source == target)
        {
            return;
        }
        if (source == PrimitiveType.Int32)
        {
            if (target == PrimitiveType.Int64)
            {
                _code.Bytes(0x48, 0x63, 0xC0);
            }
            else if (target is PrimitiveType.UInt32 or PrimitiveType.UInt64)
            {
                _code.Bytes(0x85, 0xC0);
                var nonnegative = _code.JumpIf(0x89);
                _code.Bytes(0x31, 0xC0);
                _code.PatchRelative(nonnegative, _code.Position);
            }
            return;
        }
        if (source == PrimitiveType.UInt32)
        {
            if (target == PrimitiveType.Int32)
            {
                _code.Bytes(0x85, 0xC0);
                var inRange = _code.JumpIf(0x89);
                _code.Byte(0xB8);
                _code.Int32(int.MaxValue);
                _code.PatchRelative(inRange, _code.Position);
            }
            return;
        }
        if (source == PrimitiveType.Int64)
        {
            if (target == PrimitiveType.UInt64)
            {
                _code.Bytes(0x48, 0x85, 0xC0);
                var nonnegative = _code.JumpIf(0x89);
                _code.Bytes(0x31, 0xC0);
                _code.PatchRelative(nonnegative, _code.Position);
            }
            else if (target == PrimitiveType.Int32)
            {
                _code.Bytes(0x48, 0x3D);
                _code.Int32(int.MinValue);
                var aboveMinimum = _code.JumpIf(0x8D);
                _code.Byte(0xB8);
                _code.Int32(int.MinValue);
                var belowRange = _code.Jump();
                _code.PatchRelative(aboveMinimum, _code.Position);
                _code.Bytes(0x48, 0x3D);
                _code.Int32(int.MaxValue);
                var belowMaximum = _code.JumpIf(0x8E);
                _code.Byte(0xB8);
                _code.Int32(int.MaxValue);
                _code.PatchRelative(belowMaximum, _code.Position);
                _code.PatchRelative(belowRange, _code.Position);
            }
            else if (target == PrimitiveType.UInt32)
            {
                _code.Bytes(0x48, 0x85, 0xC0);
                var nonnegative = _code.JumpIf(0x89);
                _code.Bytes(0x31, 0xC0);
                var negative = _code.Jump();
                _code.PatchRelative(nonnegative, _code.Position);
                _code.Byte(0xB9);
                _code.Int32(-1);
                _code.Bytes(0x48, 0x39, 0xC8);
                var inRange = _code.JumpIf(0x86);
                _code.Byte(0xB8);
                _code.Int32(-1);
                _code.PatchRelative(inRange, _code.Position);
                _code.PatchRelative(negative, _code.Position);
            }
            return;
        }
        if (source == PrimitiveType.UInt64)
        {
            if (target == PrimitiveType.Int64)
            {
                _code.Bytes(0x48, 0x85, 0xC0);
                var inRange = _code.JumpIf(0x89);
                _code.Bytes(0x48, 0xB8);
                _code.Int64(long.MaxValue);
                _code.PatchRelative(inRange, _code.Position);
            }
            else if (target == PrimitiveType.Int32)
            {
                _code.Byte(0xB9);
                _code.Int32(int.MaxValue);
                _code.Bytes(0x48, 0x39, 0xC8);
                var inRange = _code.JumpIf(0x86);
                _code.Byte(0xB8);
                _code.Int32(int.MaxValue);
                _code.PatchRelative(inRange, _code.Position);
            }
            else if (target == PrimitiveType.UInt32)
            {
                _code.Byte(0xB9);
                _code.Int32(-1);
                _code.Bytes(0x48, 0x39, 0xC8);
                var inRange = _code.JumpIf(0x86);
                _code.Byte(0xB8);
                _code.Int32(-1);
                _code.PatchRelative(inRange, _code.Position);
            }
            return;
        }
        throw new InvalidOperationException("TRRM integer conversion types were not verified.");
    }

    private void WrappingBinary(TokenKind op, PrimitiveType type)
    {
        if (IsWide(type))
        {
            _code.Byte(0x48);
        }
        switch (op)
        {
            case TokenKind.Plus:
                _code.Bytes(0x01, 0xC8);
                break;
            case TokenKind.Minus:
                _code.Bytes(0x29, 0xC8);
                break;
            case TokenKind.Star:
                _code.Bytes(0x0F, 0xAF, 0xC1);
                break;
            default:
                throw new InvalidOperationException("TRRM wrapping Binary operator was not verified.");
        }
    }

    private bool TryEmitUnsignedPowerOfTwoBinary(BoundBinary binary)
    {
        if (binary.Operator is not (TokenKind.Slash or TokenKind.Percent)
            || binary.Left.Type is not (PrimitiveType.UInt32 or PrimitiveType.UInt64)
            || binary.Right is not BoundConstant constant)
        {
            return false;
        }

        var divisor = binary.Left.Type == PrimitiveType.UInt32
            ? unchecked((uint)constant.Value)
            : unchecked((ulong)constant.Value);
        if (divisor == 0 || (divisor & (divisor - 1)) != 0)
        {
            return false;
        }

        if (binary.Operator == TokenKind.Slash)
        {
            if (divisor != 1)
            {
                if (binary.Left.Type == PrimitiveType.UInt64)
                {
                    _code.Byte(0x48);
                }
                _code.Bytes(0xC1, 0xE8, (byte)BitOperations.TrailingZeroCount(divisor));
            }
        }
        else if (divisor == 1)
        {
            _code.Bytes(0x31, 0xC0);
        }
        else
        {
            var mask = divisor - 1;
            if (binary.Left.Type == PrimitiveType.UInt32 || mask <= uint.MaxValue)
            {
                _code.Byte(0x25); // and eax,imm32; zero-extends for uint64
                _code.Int32(unchecked((int)mask));
            }
            else
            {
                _code.Bytes(0x48, 0xB9);
                _code.Int64(unchecked((long)mask));
                _code.Bytes(0x48, 0x21, 0xC8);
            }
        }

        return true;
    }

    private bool TryEmitSignedPowerOfTwoBinary(BoundBinary binary)
    {
        if (binary.Operator is not (TokenKind.Slash or TokenKind.Percent)
            || binary.Left.Type is not (PrimitiveType.Int32 or PrimitiveType.Int64)
            || binary.Right is not BoundConstant constant)
        {
            return false;
        }

        var divisor = constant.Value;
        if (divisor <= 0 || (divisor & (divisor - 1)) != 0)
        {
            return false;
        }

        if (divisor == 1)
        {
            if (binary.Operator == TokenKind.Percent)
            {
                _code.Bytes(0x31, 0xC0);
            }
            return true;
        }

        var wide = binary.Left.Type == PrimitiveType.Int64;
        var width = wide ? 64 : 32;
        var shift = BitOperations.TrailingZeroCount((ulong)divisor);
        if (binary.Operator == TokenKind.Percent)
        {
            if (wide)
            {
                _code.Byte(0x48);
            }
            _code.Bytes(0x89, 0xC2); // preserve the numerator in rdx
        }

        // Only negative numerators receive a bias of divisor - 1. Its addition cannot
        // overflow: a negative n plus at most divisor - 1 is at most divisor - 2.
        if (wide)
        {
            _code.Byte(0x48);
        }
        _code.Bytes(0x89, 0xC1); // mov rcx,rax
        if (wide)
        {
            _code.Byte(0x48);
        }
        _code.Bytes(0xC1, 0xF9, (byte)(width - 1)); // sar rcx,width-1
        if (wide)
        {
            _code.Byte(0x48);
        }
        _code.Bytes(0xC1, 0xE9, (byte)(width - shift)); // logical shift yields 0 or divisor - 1
        if (wide)
        {
            _code.Byte(0x48);
        }
        _code.Bytes(0x01, 0xC8); // add rax,rcx
        if (wide)
        {
            _code.Byte(0x48);
        }
        _code.Bytes(0xC1, 0xF8, (byte)shift); // arithmetic quotient truncated toward zero

        if (binary.Operator == TokenKind.Percent)
        {
            if (wide)
            {
                _code.Byte(0x48);
            }
            _code.Bytes(0xC1, 0xE0, (byte)shift); // quotient * divisor is within the numerator's range
            if (wide)
            {
                _code.Byte(0x48);
            }
            _code.Bytes(0x29, 0xC2); // sub rdx,rax
            if (wide)
            {
                _code.Byte(0x48);
            }
            _code.Bytes(0x89, 0xD0);
        }

        return true;
    }

    private void BeginFloatingEnvironment(int savedMxcsr, int controlledMxcsr)
    {
        _code.StoreMxcsr(savedMxcsr);
        _code.Load(savedMxcsr);
        _code.Byte(0x25); // and eax,~(RC[14:13] | DAZ[6] | FTZ[15])
        _code.Int32(unchecked((int)0xFFFF1FBF));
        _code.Byte(0x0D); // or eax,all six SSE exception masks
        _code.Int32(0x1F80);
        _code.Store(controlledMxcsr);
        _code.LoadMxcsr(controlledMxcsr);
    }

    private void FloatingBinary(TokenKind op, PrimitiveType type, int spill)
    {
        var isSingle = type == PrimitiveType.Float32;
        if (isSingle)
        {
            _code.Bytes(0x66, 0x0F, 0x6E, 0xC8); // movd xmm1,eax
        }
        else
        {
            _code.Bytes(0x66, 0x48, 0x0F, 0x6E, 0xC8); // movq xmm1,rax
        }

        var savedMxcsr = Spill(spill + 1);
        var controlledMxcsr = Spill(spill + 2);
        BeginFloatingEnvironment(savedMxcsr, controlledMxcsr);

        LoadValue(Spill(spill), type);
        if (isSingle)
        {
            _code.Bytes(0x66, 0x0F, 0x6E, 0xC0); // movd xmm0,eax
        }
        else
        {
            _code.Bytes(0x66, 0x48, 0x0F, 0x6E, 0xC0); // movq xmm0,rax
        }

        if (op is TokenKind.Plus or TokenKind.Minus or TokenKind.Star or TokenKind.Slash)
        {
            _code.Bytes(isSingle ? (byte)0xF3 : (byte)0xF2, 0x0F,
                op switch {
                    TokenKind.Plus => (byte)0x58,
                    TokenKind.Minus => (byte)0x5C,
                    TokenKind.Star => (byte)0x59,
                    TokenKind.Slash => (byte)0x5E,
                    _ => throw new InvalidOperationException(),
                }, 0xC1);
            if (isSingle)
            {
                _code.Bytes(0x0F, 0x2E, 0xC0); // ucomiss xmm0,xmm0
            }
            else
            {
                _code.Bytes(0x66, 0x0F, 0x2E, 0xC0); // ucomisd xmm0,xmm0
            }
            var notNaN = _code.JumpIf(0x8B);
            if (isSingle)
            {
                _code.Byte(0xB8);
                _code.Int32(unchecked((int)0x7FC00000));
            }
            else
            {
                _code.Bytes(0x48, 0xB8);
                _code.Int64(unchecked((long)0x7FF8000000000000UL));
            }
            var done = _code.Jump();
            _code.PatchRelative(notNaN, _code.Position);
            if (isSingle)
            {
                _code.Bytes(0x66, 0x0F, 0x7E, 0xC0); // movd eax,xmm0
            }
            else
            {
                _code.Bytes(0x66, 0x48, 0x0F, 0x7E, 0xC0); // movq rax,xmm0
            }
            _code.PatchRelative(done, _code.Position);
        }
        else
        {
            if (isSingle)
            {
                _code.Bytes(0x0F, 0x2E, 0xC1); // ucomiss xmm0,xmm1
            }
            else
            {
                _code.Bytes(0x66, 0x0F, 0x2E, 0xC1); // ucomisd xmm0,xmm1
            }
            switch (op)
            {
                case TokenKind.EqualsEquals:
                    _code.Bytes(0x0F, 0x94, 0xC0, 0x0F, 0x9B, 0xC1, 0x20, 0xC8);
                    break;
                case TokenKind.BangEquals:
                    _code.Bytes(0x0F, 0x95, 0xC0, 0x0F, 0x9A, 0xC1, 0x08, 0xC8);
                    break;
                case TokenKind.Less:
                    _code.Bytes(0x0F, 0x92, 0xC0, 0x0F, 0x9B, 0xC1, 0x20, 0xC8);
                    break;
                case TokenKind.LessEquals:
                    _code.Bytes(0x0F, 0x96, 0xC0, 0x0F, 0x9B, 0xC1, 0x20, 0xC8);
                    break;
                case TokenKind.Greater:
                    _code.Bytes(0x0F, 0x97, 0xC0);
                    break;
                case TokenKind.GreaterEquals:
                    _code.Bytes(0x0F, 0x93, 0xC0);
                    break;
                default:
                    throw new InvalidOperationException();
            }
            _code.Bytes(0x0F, 0xB6, 0xC0); // movzx eax,al
        }
        _code.LoadMxcsr(savedMxcsr);
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
            case TokenKind.ShiftLeft:
            case TokenKind.ShiftRight:
            case TokenKind.ShiftRightUnsigned:
                _code.Bytes(0x83, 0xF9, 0x20); // cmp ecx,32; unsigned comparison rejects negative counts
                RuntimeBranch(0x83, 0);
                _code.Bytes(0xD3, op == TokenKind.ShiftLeft ? (byte)0xE0
                    : op == TokenKind.ShiftRight && !unsigned ? (byte)0xF8 : (byte)0xE8);
                break;
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
            case TokenKind.ShiftLeft:
            case TokenKind.ShiftRight:
            case TokenKind.ShiftRightUnsigned:
                _code.Bytes(0x83, 0xF9, 0x40); // cmp ecx,64
                RuntimeBranch(0x83, 0);
                _code.Bytes(0x48, 0xD3, op == TokenKind.ShiftLeft ? (byte)0xE0
                    : op == TokenKind.ShiftRight && !unsigned ? (byte)0xF8 : (byte)0xE8);
                break;
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
                byte opcode = op switch {
                    TokenKind.Ampersand => 0x21,
                    TokenKind.Pipe => 0x09,
                    _ => 0x31,
                };
                _code.Bytes(0x48, opcode, 0xC8);
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
    internal void Bytes(params ReadOnlySpan<byte> values) => _bytes.AddRange(values);
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
    internal void StoreMxcsr(int offset)
    {
        Bytes(0x0F, 0xAE, UseFramePointer ? (byte)0x9D : (byte)0x9C);
        if (!UseFramePointer)
        {
            Byte(0x24);
        }
        Int32(offset);
    }
    internal void LoadMxcsr(int offset)
    {
        Bytes(0x0F, 0xAE, UseFramePointer ? (byte)0x95 : (byte)0x94);
        if (!UseFramePointer)
        {
            Byte(0x24);
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
    internal void Clear(int offset)
    {
        Bytes(0xC7, UseFramePointer ? (byte)0x85 : (byte)0x84);
        if (!UseFramePointer)
        {
            Byte(0x24);
        }
        Int32(offset);
        Int32(0);
    }
    internal void Set(int offset)
    {
        Bytes(0xC7, UseFramePointer ? (byte)0x85 : (byte)0x84);
        if (!UseFramePointer)
        {
            Byte(0x24);
        }
        Int32(offset);
        Int32(1);
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
