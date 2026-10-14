// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using Terrum.CodeAnalysis.Semantics;

namespace Terrum.CodeAnalysis.Native;

internal sealed partial class PeCompiler
{
    private void EmitNativeWindowClassEx(int variable, BoundNativeWindowClassExInitialization descriptor)
    {
        var offset = Slot(variable);
        for (var field = 0; field < NativeWindowClassContract.WindowClassExSize; field += sizeof(uint))
        {
            _code.Clear(offset + field);
        }

        _code.Bytes(0xB8, (byte)NativeWindowClassContract.WindowClassExSize, 0x00, 0x00, 0x00);
        _code.Store(offset);
        Expression(descriptor.Callback, 0);
        _code.Store64(offset + 8);
        Expression(descriptor.Instance, 0);
        _code.Store64(offset + 24);
        Expression(descriptor.ClassName, 0);
        _code.Store64(offset + 64);
    }
}
