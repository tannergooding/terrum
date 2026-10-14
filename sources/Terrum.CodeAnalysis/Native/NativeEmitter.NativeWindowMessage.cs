// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using Terrum.CodeAnalysis.Semantics;

namespace Terrum.CodeAnalysis.Native;

internal sealed partial class PeCompiler
{
    private void EmitNativeWindowMessage(int variable)
    {
        var offset = Slot(variable);
        for (var field = 0; field < NativeWindowMessageContract.MessageSize; field += sizeof(uint))
        {
            _code.Clear(offset + field);
        }
    }

    private void EmitNativeWindowMessageAccessor(BoundFunction function)
    {
        if (!NativeWindowMessageContract.IsAccessorFunction(function))
        {
            throw new InvalidOperationException("A Windows message accessor does not match its verified contract.");
        }

        _code.Load64(Slot(0));
        switch (function.Intrinsic)
        {
            case BoundIntrinsic.NativeWindowMessageId:
                _code.Bytes(0x8B, 0x40, 0x08);
                break;
            case BoundIntrinsic.NativeWindowMessageWParam:
                _code.Bytes(0x48, 0x8B, 0x40, 0x10);
                break;
            case BoundIntrinsic.NativeWindowMessageWindowIs:
                _code.Bytes(0x48, 0x8B, 0x10);
                _code.Load64(Slot(1));
                _code.Bytes(0x48, 0x39, 0xC2, 0x0F, 0x94, 0xC0, 0x0F, 0xB6, 0xC0);
                break;
            default:
                throw new InvalidOperationException("A Windows message accessor has an unknown field.");
        }
    }
}
