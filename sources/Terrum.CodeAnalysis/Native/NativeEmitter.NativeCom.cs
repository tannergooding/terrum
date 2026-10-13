// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
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
            var nativeIndex = NativeComCallContract.NativeParameterIndex(function, boundIndex, method);
            if (boundParameterByNative[nativeIndex] < 0)
            {
                boundParameterByNative[nativeIndex] = boundIndex;
            }
        }

        var nextSpill = method.OutputParameterIndex >= 0 ? 1 : 0;
        var output = method.OutputParameterIndex >= 0 ? Spill(0) : -1;
        if (output >= 0)
        {
            _code.Bytes(0x31, 0xC0);
            _code.Store64(output);
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
        EmitNativeComRelease(method);
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
