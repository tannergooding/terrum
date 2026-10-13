// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using Terrum.CodeAnalysis.Semantics;

namespace Terrum.CodeAnalysis.Native;

internal sealed partial class PeCompiler
{
    private void EmitNativeMappedByteRead(BoundNativeMappedByteRead read, int spill)
    {
        Expression(read.Index, spill);
        _code.Store64(Spill(spill));

        _code.Load64(Spill(spill));
        _code.Load64(Slot(read.Variable) + read.LengthOffset - sizeof(uint), destinationRegister: true);
        _code.Bytes(0x48, 0xC1, 0xEA, 0x20);
        _code.Bytes(0x48, 0x39, 0xD0);
        RuntimeBranch(0x83, 2);

        _code.Load64(Slot(read.Variable) + read.PointerOffset);
        _code.Bytes(0x48, 0x85, 0xC0);
        RuntimeBranch(0x84, 3);
        _code.Load64(Spill(spill), destinationRegister: true);
        _code.Bytes(0x48, 0x01, 0xD0);
        RuntimeBranch(0x82, 3);
        _code.Bytes(0x0F, 0xB6, 0x00);
    }
}
