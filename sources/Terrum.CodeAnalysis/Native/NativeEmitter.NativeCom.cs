// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Terrum.CodeAnalysis.Semantics;

namespace Terrum.CodeAnalysis.Native;

internal sealed partial class PeCompiler
{
    private void EmitNativeVTableCall(int slot)
    {
        var byteOffset = checked(slot * NativeTargetProfile.PointerSize);
        _code.Bytes(0x48, 0x8B, 0x01);
        if (byteOffset <= sbyte.MaxValue)
        {
            _code.Bytes(0xFF, 0x50, checked((byte)byteOffset));
        }
        else
        {
            _code.Bytes(0xFF, 0x90);
            _code.Int32(byteOffset);
        }
    }

    private void EmitNativeComRelease(NativeComMethodDefinition method)
    {
        _code.MoveArgument(0, wide: true);
        EmitNativeComVTableCall(method);
    }

    private void EmitNativeComVTableCall(NativeComMethodDefinition method)
    {
        var catalog = _program.NativeImportCatalog;
        if (catalog is null || !catalog.TryGetComMethod(method.Identity, out var catalogued)
            || catalogued is null || !ReferenceEquals(method, catalogued) || !catalogued.IsCallable)
        {
            throw new InvalidOperationException("COM dispatch requires a callable catalog record.");
        }
        EmitNativeVTableCall(catalogued.Slot);
    }

    private void EmitNativeComMethodFunction(BoundFunction function)
    {
        if (!NativeComCallContract.IsValidVTableCallFunction(function, _program))
        {
            throw new InvalidOperationException("The COM method function does not match its catalogued signature.");
        }
        var method = GetNativeComMethod(function);
        if (!NativeComCatalog.TryParseSignature(method.Signature, out _, out var nativeParameters))
        {
            throw new InvalidOperationException("The catalogued COM method signature is invalid.");
        }
        var boundParameterByNative = new int[nativeParameters.Length];
        Array.Fill(boundParameterByNative, -1);
        for (var boundIndex = 0; boundIndex < function.ParameterCount; boundIndex++)
        {
            NativeComBoundParameterMapping[] explicitMappings = function.NativeComParameterMappings.IsDefaultOrEmpty
                ? [] : function.NativeComParameterMappings.Where(mapping =>
                    mapping.BoundParameterIndex == boundIndex).ToArray();
            if (explicitMappings.Length == 0)
            {
                if (boundIndex < nativeParameters.Length
                    && boundParameterByNative[boundIndex] < 0)
                {
                    boundParameterByNative[boundIndex] = boundIndex;
                }
                continue;
            }
            foreach (var mapping in explicitMappings)
            {
                if (boundParameterByNative[mapping.NativeParameterIndex] < 0)
                {
                    boundParameterByNative[mapping.NativeParameterIndex] = boundIndex;
                }
                else if (NativeComCallContract.BoundParameterIndices(
                        function, mapping.NativeParameterIndex).Length <= 1)
                {
                    throw new InvalidOperationException(
                        "A COM method maps multiple bound parameters to one native parameter.");
                }
            }
        }

        var nextSpill = method.OutputParameterIndex >= 0 ? 1 : 0;
        var output = method.OutputParameterIndex >= 0 ? Spill(0) : -1;
        if (output >= 0)
        {
            _code.Bytes(0x31, 0xC0);
            _code.Store64(output);
        }
        var bufferContentsSpills = new Dictionary<int, (int Pointer, int Size)>();
        if (!method.ParameterAdaptations.IsDefaultOrEmpty)
        {
            foreach (var adaptation in method.ParameterAdaptations.Where(static adaptation =>
                adaptation.Kind == NativeComParameterAdaptationKind.ComObjectBufferContents))
            {
                var ownerParameters = NativeComCallContract.BoundParameterIndices(
                    function, adaptation.ParameterIndex);
                if (ownerParameters.Length != 1
                    || adaptation.BufferPointerMethodIdentity is not string pointerMethodIdentity
                    || adaptation.BufferSizeMethodIdentity is not string sizeMethodIdentity)
                {
                    throw new InvalidOperationException(
                        "A COM buffer-content adaptation has no unique owner or accessor methods.");
                }
                var pointer = Spill(nextSpill++);
                var size = Spill(nextSpill++);
                _code.Load64(Slot(ownerParameters[0]));
                _code.LoadPointerField(0, NativeTargetProfile.PointerSize);
                _code.MoveArgument(0, wide: true);
                EmitNativeComVTableCall(CataloguedComMethod(pointerMethodIdentity));
                _code.Store64(pointer);
                _code.Load64(Slot(ownerParameters[0]));
                _code.LoadPointerField(0, NativeTargetProfile.PointerSize);
                _code.MoveArgument(0, wide: true);
                EmitNativeComVTableCall(CataloguedComMethod(sizeMethodIdentity));
                _code.Store64(size);
                bufferContentsSpills.Add(adaptation.ParameterIndex, (pointer, size));
            }
        }
        var interfaceIdSpills = new Dictionary<int, int>();
        if (!function.NativeComHiddenParameters.IsDefaultOrEmpty)
        {
            foreach (var parameter in function.NativeComHiddenParameters)
            {
                if (parameter.Kind == NativeComHiddenParameterKind.InterfaceIdAddress)
                {
                    interfaceIdSpills.Add(parameter.NativeParameterIndex, Spill(nextSpill++));
                }
            }
        }

        for (var nativeIndex = 1; nativeIndex < nativeParameters.Length; nativeIndex++)
        {
            var boundIndex = boundParameterByNative[nativeIndex];
            var type = boundIndex >= 0 ? function.Variables[boundIndex].Type : default;
            var owner = boundIndex >= 0 ? NativeComCallContract.Owner(type, _program) : null;
            var adaptation = boundIndex >= 0 && !method.ParameterAdaptations.IsDefaultOrEmpty
                ? method.ParameterAdaptations.SingleOrDefault(item => item.ParameterIndex == nativeIndex)
                : null;
            var bufferContents = NativeComCallContract.ComObjectBufferContentsAdaptation(method, nativeIndex);
            var isFloating = adaptation?.Kind is not (NativeComParameterAdaptationKind.ValueAddress
                    or NativeComParameterAdaptationKind.ValueOutputAddress
                    or NativeComParameterAdaptationKind.ValueArrayAddress)
                && boundIndex >= 0 && type is PrimitiveType.Float32 or PrimitiveType.Float64;
            var wide = false;
            if (nativeIndex == method.OutputParameterIndex)
            {
                _code.Address(output);
                wide = true;
            }
            else if (boundIndex >= 0)
            {
                if (adaptation?.Kind == NativeComParameterAdaptationKind.ValueArrayAddress)
                {
                    var boundIndices = NativeComCallContract.BoundParameterIndices(function, nativeIndex);
                    if (!NativeComCatalog.TryGetValueArrayElementCount(adaptation.TargetType,
                            out var elementCount)
                        || boundIndices.Length != elementCount)
                    {
                        throw new InvalidOperationException(
                            "A COM value-array adaptation does not match its bound scalar elements.");
                    }
                    var elementSize = Size(function.Variables[boundIndices[0]].Type);
                    var byteCount = checked(elementCount * elementSize);
                    var valueArray = Spill(nextSpill);
                    nextSpill += (byteCount + 15) / 16;
                    for (var elementIndex = 0; elementIndex < boundIndices.Length; elementIndex++)
                    {
                        var source = function.Variables[boundIndices[elementIndex]];
                        if (Size(source.Type) != elementSize)
                        {
                            throw new InvalidOperationException(
                                "A COM value-array adaptation has inconsistent scalar element sizes.");
                        }
                        LoadValue(Slot(boundIndices[elementIndex]), source.Type);
                        StoreValue(valueArray + (elementIndex * elementSize), source.Type);
                    }
                    _code.Address(valueArray);
                    wide = true;
                }
                else if (adaptation?.Kind == NativeComParameterAdaptationKind.AggregateAddress)
                {
                    _code.Address(Slot(boundIndex));
                    wide = true;
                }
                else if (adaptation?.Kind == NativeComParameterAdaptationKind.AggregateStorageAddress)
                {
                    _code.Load64(Slot(boundIndex));
                    wide = true;
                }
                else if (adaptation?.Kind == NativeComParameterAdaptationKind.ValueAddress)
                {
                    _code.Address(Slot(boundIndex));
                    wide = true;
                }
                else if (adaptation?.Kind == NativeComParameterAdaptationKind.ValueOutputAddress)
                {
                    _code.Load64(Slot(boundIndex));
                    wide = true;
                }
                else if (bufferContents is not null)
                {
                    var spills = bufferContentsSpills[bufferContents.ParameterIndex];
                    _code.Load64(NativeComCallContract.IsComObjectBufferSizeParameter(
                        bufferContents, nativeIndex) ? spills.Size : spills.Pointer);
                    wide = true;
                }
                else if (owner is not null)
                {
                    _code.Load64(Slot(boundIndex));
                    _code.LoadPointerField(0, NativeTargetProfile.PointerSize);
                    wide = true;
                    if (adaptation?.Kind == NativeComParameterAdaptationKind.SingleElementOwnerPointerArray)
                    {
                        var pointerCell = Spill(nextSpill++);
                        _code.Store64(pointerCell);
                        _code.Address(pointerCell);
                        wide = true;
                    }
                }
                else
                {
                    LoadValue(Slot(boundIndex), type);
                    wide = IsWide(type);
                }
            }
            else
            {
                var hidden = HiddenParameter(function, nativeIndex);
                switch (hidden.Kind)
                {
                    case NativeComHiddenParameterKind.UInt32:
                        _code.Byte(0xB8);
                        _code.Int32(unchecked((int)hidden.Value));
                        break;
                    case NativeComHiddenParameterKind.NullPointer:
                        _code.Bytes(0x31, 0xC0);
                        wide = true;
                        break;
                    case NativeComHiddenParameterKind.InterfaceIdAddress:
                        EmitNativeComInterfaceIdAddress(
                            interfaceIdSpills[nativeIndex], hidden.InterfaceIdentity
                                ?? throw new InvalidOperationException("A COM interface ID parameter has no identity."));
                        wide = true;
                        break;
                    default:
                        throw new InvalidOperationException("The COM method has an unknown hidden parameter kind.");
                }
            }
            if (nativeIndex < 4)
            {
                if (isFloating)
                {
                    _code.MoveFloatingArgument(nativeIndex, type == PrimitiveType.Float64);
                }
                else
                {
                    _code.MoveArgument(nativeIndex, wide);
                }
            }
            else
            {
                _code.StoreOutgoing64(32 + (8 * (nativeIndex - 4)));
            }
        }

        _code.Load64(Slot(0));
        _code.LoadPointerField(0, NativeTargetProfile.PointerSize);
        _code.MoveArgument(0, wide: true);
        EmitNativeComVTableCall(method);
        if (method.OutputParameterIndex >= 0)
        {
            var outputBoundIndex = boundParameterByNative[method.OutputParameterIndex];
            var outputOwner = NativeComCallContract.Owner(
                function.Variables[outputBoundIndex].Type, _program)
                ?? throw new InvalidOperationException("A catalogued COM output must target a registered owner.");
            EmitNativeComOwnedOutputResult(output, Slot(outputBoundIndex), method,
                CataloguedComMethod(outputOwner.ReleaseMethodIdentity));
        }
        else if (method.Effects.HasFlag(NativeComEffects.SignedHResult)
            && function.ReturnType == PrimitiveType.Bool)
        {
            EmitNativeComHResultSuccessResult();
        }
        else if (function.ReturnType is PrimitiveType.Float32 or PrimitiveType.Float64)
        {
            _code.MoveFloatingReturnToAccumulator(function.ReturnType == PrimitiveType.Float64);
        }
    }

    private static NativeComHiddenParameter HiddenParameter(BoundFunction function, int nativeIndex)
    {
        foreach (var parameter in function.NativeComHiddenParameters)
        {
            if (parameter is not null && parameter.NativeParameterIndex == nativeIndex)
            {
                return parameter;
            }
        }
        throw new InvalidOperationException("A COM method has an unmapped native parameter.");
    }

    private void EmitNativeComInterfaceIdAddress(int destination, string interfaceIdentity)
    {
        var catalog = _program.NativeImportCatalog
            ?? throw new InvalidOperationException("The COM method has no native import catalog.");
        if (!catalog.TryGetComInterface(interfaceIdentity, out var definition) || definition is null)
        {
            throw new InvalidOperationException("The COM method refers to an unknown interface ID.");
        }
        Span<byte> iidBytes = stackalloc byte[16];
        if (!definition.SdkIid.TryWriteBytes(iidBytes))
        {
            throw new InvalidOperationException("The COM interface ID could not be encoded.");
        }
        for (var offset = 0; offset < iidBytes.Length; offset += sizeof(int))
        {
            _code.Byte(0xB8);
            _code.Int32(BinaryPrimitives.ReadInt32LittleEndian(iidBytes.Slice(offset, sizeof(int))));
            _code.Store(destination + offset);
        }
        _code.Address(destination);
    }

    private void EmitNativeComHResultSuccessResult()
    {
        _code.Bytes(0x85, 0xC0);
        var failed = _code.JumpIf(0x88);
        _code.Byte(0xB8);
        _code.Int32(1);
        var done = _code.Jump();
        _code.PatchRelative(failed, _code.Position);
        _code.Bytes(0x31, 0xC0);
        _code.PatchRelative(done, _code.Position);
    }

    private void EmitNativeComOwnedOutputResult(int output, int destination,
        NativeComMethodDefinition method, NativeComMethodDefinition releaseMethod) =>
        EmitNativeComOwnedOutputResult(output, destination,
            method.Effects.HasFlag(NativeComEffects.OutputMayBeWrittenOnFailure), releaseMethod);

    private void EmitNativeComOwnedOutputResult(int output, int destination,
        bool releaseOnFailure, NativeComMethodDefinition releaseMethod)
    {
        _code.Bytes(0x85, 0xC0);
        var failed = _code.JumpIf(0x88);
        _code.Load64(output);
        _code.Bytes(0x48, 0x85, 0xC0);
        var missing = _code.JumpIf(0x84);
        _code.Bytes(0x48, 0x89, 0xC1);
        _code.Load64(destination);
        _code.Bytes(0x48, 0x89, 0x08);
        _code.Bytes(0x31, 0xC0);
        _code.Store64(output);
        _code.Byte(0xB8);
        _code.Int32(1);
        var done = _code.Jump();

        _code.PatchRelative(failed, _code.Position);
        if (releaseOnFailure)
        {
            _code.Load64(output);
            _code.Bytes(0x48, 0x85, 0xC0);
            var absent = _code.JumpIf(0x84);
            EmitNativeComRelease(releaseMethod);
            _code.PatchRelative(absent, _code.Position);
        }
        _code.Bytes(0x31, 0xC0);
        _code.Store64(output);
        var failedResult = _code.Jump();

        _code.PatchRelative(missing, _code.Position);
        _code.Bytes(0x31, 0xC0);
        _code.PatchRelative(failedResult, _code.Position);
        _code.PatchRelative(done, _code.Position);
    }

    private void EmitNativeComOwnerClose(BoundFunction function)
    {
        if (!NativeComCallContract.IsValidOwnerCloseFunction(function, _program)
            || function.NativeComMethod is not NativeComMethodDefinition method)
        {
            throw new InvalidOperationException("The COM owner cleanup does not match its catalog metadata.");
        }

        _code.Load64(Slot(0));
        _code.Bytes(0x48, 0x85, 0xC0);
        var absent = _code.JumpIf(0x84);
        EmitNativeComRelease(method);
        _code.PatchRelative(absent, _code.Position);
    }

    private ImmutableArray<NativeComOutputDefinition> NativeImportComOutputs(BoundCall call)
    {
        var function = _program.Functions[call.Function];
        if (!function.IsExternal || function.NativeImportKey is not string key
            || _program.NativeImportCatalog is not NativeImportCatalog catalog
            || !catalog.TryGetImport(key, out var import) || import is null)
        {
            return [];
        }
        return import.ComOutputs;
    }

    private static NativeComOutputDefinition? NativeImportComOutput(
        ImmutableArray<NativeComOutputDefinition> outputs, BoundCall call, int argumentIndex)
    {
        var parameterIndex = call.ParameterIndex(argumentIndex);
        foreach (var output in outputs)
        {
            if (output.ParameterIndex == parameterIndex)
            {
                return output;
            }
        }
        return null;
    }

    private void EmitNativeImportComOutputOwnership(int variable, PrimitiveType type,
        NativeComOutputDefinition output, int result)
    {
        if (!output.Effects.HasFlag(NativeComEffects.SignedHResult))
        {
            EmitNativeImportComOutputLiveState(variable);
            return;
        }

        _code.Load(result);
        _code.Bytes(0x85, 0xC0);
        var failed = _code.JumpIf(0x88);
        EmitNativeImportComOutputLiveState(variable);
        var done = _code.Jump();

        _code.PatchRelative(failed, _code.Position);
        if (output.Effects.HasFlag(NativeComEffects.OutputMayBeWrittenOnFailure))
        {
            EmitNativeImportComOutputLiveState(variable);
        }
        else
        {
            ZeroStruct(OwnerPlace(variable), Size(type));
            _code.Clear(_liveSlots[_currentFunction][variable]);
        }
        _code.PatchRelative(done, _code.Position);
    }

    private void EmitNativeImportComOutputLiveState(int variable)
    {
        var live = _liveSlots[_currentFunction][variable];
        _code.Load64(OwnerPlace(variable));
        _code.Bytes(0x48, 0x85, 0xC0);
        var absent = _code.JumpIf(0x84);
        _code.Set(live);
        var done = _code.Jump();
        _code.PatchRelative(absent, _code.Position);
        _code.Clear(live);
        _code.PatchRelative(done, _code.Position);
    }

    private void EmitNativeImportComOutputSuccessValidation(
        BoundCall call, ImmutableArray<NativeComOutputDefinition> outputs, int result)
    {
        _code.Load(result);
        _code.Bytes(0x85, 0xC0);
        var failed = _code.JumpIf(0x88);
        foreach (var output in outputs)
        {
            if (!output.Effects.HasFlag(NativeComEffects.SignedHResult))
            {
                continue;
            }
            var argumentIndex = call.ArgumentIndex(output.ParameterIndex);
            if (call.Arguments[argumentIndex] is not BoundOutArgument owner
                || !IsUnique(owner.Type))
            {
                throw new InvalidOperationException("A catalogued COM output must target a unique owner.");
            }
            _code.Load64(OwnerPlace(owner.Variable));
            _code.Bytes(0x48, 0x85, 0xC0);
            var present = _code.JumpIf(0x85);
            EmitMissingNativeComOutputFailure();
            _code.PatchRelative(present, _code.Position);
        }
        _code.PatchRelative(failed, _code.Position);
    }

    private void EmitMissingNativeComOutputFailure()
    {
        for (var index = _cleanups.Count - 1; index >= 0; index--)
        {
            CallCleanup(_cleanups[index]);
        }
        _code.Byte(0xB9);
        _code.Int32(NativeComCallContract.MissingSuccessfulOutputHResult);
        _nativeImportFixups.Add(new NativeImportFixup(_code.ImportCall(), "KERNEL32.dll", "ExitProcess"));
        _code.Bytes(0x0F, 0x0B);
    }

    private void EmitNativeComOwnerTryClone(BoundFunction function)
    {
        if (!NativeComCallContract.IsValidOwnerCloneFunction(function, _program)
            || function.NativeComMethod is not NativeComMethodDefinition method)
        {
            throw new InvalidOperationException("The COM owner clone does not match its catalog metadata.");
        }

        _code.Load64(Slot(0));
        _code.LoadPointerField(0, NativeTargetProfile.PointerSize);
        _code.Bytes(0x48, 0x85, 0xC0);
        var absent = _code.JumpIf(0x84);
        _code.MoveArgument(0, wide: true);
        EmitNativeComVTableCall(method);
        _code.Load64(Slot(0));
        _code.LoadPointerField(0, NativeTargetProfile.PointerSize);
        _code.Bytes(0x48, 0x89, 0xC1);
        _code.Load64(Slot(1));
        _code.Bytes(0x48, 0x89, 0x08);
        _code.Byte(0xB8);
        _code.Int32(1);
        var done = _code.Jump();

        _code.PatchRelative(absent, _code.Position);
        _code.Bytes(0x31, 0xC0, 0x48, 0x89, 0xC1);
        _code.Load64(Slot(1));
        _code.Bytes(0x48, 0x89, 0x08);
        _code.Bytes(0x31, 0xC0);
        _code.PatchRelative(done, _code.Position);
    }

    private NativeComMethodDefinition GetNativeComMethod(BoundFunction function)
    {
        var method = function.NativeComMethod
            ?? throw new InvalidOperationException("The verified COM intrinsic has no catalogued method.");
        var catalog = _program.NativeImportCatalog
            ?? throw new InvalidOperationException("The COM intrinsic has no native import catalog.");
        if (!catalog.TryGetComMethod(method.Identity, out var catalogued)
            || catalogued is null || !ReferenceEquals(method, catalogued) || !catalogued.IsCallable)
        {
            throw new InvalidOperationException("The verified COM intrinsic does not retain its catalog record.");
        }
        return catalogued;
    }

    private NativeComMethodDefinition CataloguedComMethod(string identity)
    {
        var catalog = _program.NativeImportCatalog
            ?? throw new InvalidOperationException("The COM intrinsic has no native import catalog.");
        if (!catalog.TryGetComMethod(identity, out var method) || method is null || !method.IsCallable)
        {
            throw new InvalidOperationException("The native catalog does not contain the required COM method.");
        }
        return method;
    }
}
