// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using Terrum.CodeAnalysis.Semantics;

namespace Terrum.CodeAnalysis.Native;

internal sealed partial class PeCompiler
{
    private void SnapshotPlace(int variable)
    {
        if (variable == 0 && IsBorrowedReceiver())
        {
            _code.Load64(Slot(0));
        }
        else
        {
            _code.Address(OwnerPlace(variable));
        }
    }

    private void EmitSnapshotSpan(BoundSnapshotSpan snapshot, int spill)
    {
        var descriptor = Spill(spill);
        var previousEnd = Spill(spill + 1);
        var position = Spill(spill + 2);
        var remaining = Spill(spill + 3);
        var sourceLength = _program.Buffers[snapshot.SourceBuffer].Values.Length;

        SnapshotPlace(snapshot.Variable);
        _code.LoadPointerField(8, 4);
        _code.Bytes(0x85, 0xC0);
        RuntimeBranch(0x8E, 2);
        _code.Byte(0x3D);
        _code.Int32(int.MaxValue / 3);
        RuntimeBranch(0x87, 0);
        _code.Store64(descriptor + TokenSpanContract.CountOffset);
        _code.Store64(remaining);
        _code.Bytes(0x48, 0x6B, 0xC0, 0x03);
        RuntimeBranch(0x80, 0);
        _code.Store64(previousEnd);

        SnapshotPlace(snapshot.Variable);
        _code.LoadPointerField(0, 8);
        _code.Bytes(0x48, 0x85, 0xC0);
        RuntimeBranch(0x84, 2);
        _code.Store64(position);
        _code.Bytes(0x48, 0x83, 0xC0, 0x08);
        RuntimeBranch(0x82, 0);
        _code.Bytes(0xA8, 0x03);
        RuntimeBranch(0x85, 2);
        _code.Store64(descriptor);
        _code.Load64(position);
        _code.Bytes(0x8B, 0x08);
        _code.Load64(previousEnd);
        _code.Bytes(0x48, 0x39, 0xC8);
        RuntimeBranch(0x85, 2);
        _code.Load64(position, destinationRegister: true);
        _code.Bytes(0x8B, 0x4A, 0x04, 0x39, 0xC8);
        RuntimeBranch(0x87, 2);
        _code.Bytes(0x81, 0xF9);
        _code.Int32(NativeBufferIntrinsics.MaximumCapacity);
        RuntimeBranch(0x87, 2);

        _code.Load64(descriptor);
        _code.Store64(position);
        _code.Load64(descriptor + TokenSpanContract.CountOffset);
        _code.Bytes(0xB9);
        _code.Int32(TokenSpanContract.Stride);
        _code.Bytes(0x48, 0xF7, 0xE1); // mul rcx: check the high word before using the byte extent
        _code.Bytes(0x48, 0x85, 0xD2);
        RuntimeBranch(0x85, 0);
        _code.Load64(descriptor, destinationRegister: true);
        _code.Bytes(0x48, 0x01, 0xD0);
        RuntimeBranch(0x82, 0);
        _code.Clear(previousEnd);

        var loop = _code.Position;
        _code.Load64(position, destinationRegister: true);
        _code.Bytes(0x8B, 0x4A, 0x04, 0x44, 0x8B, 0x42, 0x08);
        _code.Load(previousEnd);
        _code.Bytes(0x39, 0xC1);
        RuntimeBranch(0x8C, 2);
        _code.Bytes(0x81, 0xF9);
        _code.Int32(sourceLength);
        RuntimeBranch(0x87, 2);
        _code.Bytes(0x45, 0x85, 0xC0);
        RuntimeBranch(0x88, 2);
        _code.Byte(0xB8);
        _code.Int32(sourceLength);
        _code.Bytes(0x29, 0xC8, 0x41, 0x39, 0xC0);
        RuntimeBranch(0x87, 2);
        _code.Load64(remaining);
        _code.Bytes(0x48, 0x83, 0xF8, 0x01);
        var eof = _code.JumpIf(0x84);
        _code.Bytes(0x8B, 0x02, 0x85, 0xC0);
        RuntimeBranch(0x8E, 2);
        _code.Byte(0x3D);
        _code.Int32(106);
        RuntimeBranch(0x87, 2);
        _code.Bytes(0x45, 0x85, 0xC0);
        RuntimeBranch(0x84, 2);
        _code.Bytes(0x44, 0x01, 0xC1);
        RuntimeBranch(0x80, 0);
        _code.Bytes(0x89, 0xC8);
        _code.Store(previousEnd);
        _code.Bytes(0x48, 0x83, 0xC2, TokenSpanContract.Stride);
        RuntimeBranch(0x82, 0);
        _code.Bytes(0x48, 0x89, 0xD0);
        _code.Store64(position);
        _code.Load64(remaining);
        _code.Bytes(0x48, 0x83, 0xE8, 0x01);
        _code.Store64(remaining);
        var next = _code.Jump();
        _code.PatchRelative(next, loop);

        _code.PatchRelative(eof, _code.Position);
        _code.Bytes(0x8B, 0x02, 0x85, 0xC0);
        RuntimeBranch(0x85, 2);
        _code.Bytes(0x81, 0xF9);
        _code.Int32(sourceLength);
        RuntimeBranch(0x85, 2);
        _code.Bytes(0x45, 0x85, 0xC0);
        RuntimeBranch(0x85, 2);
        _code.Address(descriptor);
    }

    private void EmitSpanRead(BoundSpanRead read, int spill)
    {
        Expression(read.View, spill);
        CopyToFrame(Spill(spill), TokenSpanContract.DescriptorSize);
        Expression(read.Index, spill + 1);
        _code.Bytes(0x48, 0x89, 0xC1);
        _code.Load64(Spill(spill) + TokenSpanContract.CountOffset);
        _code.Bytes(0x48, 0x39, 0xC1);
        RuntimeBranch(0x83, 2);
        _code.Bytes(0x48, 0x89, 0xC8, 0xB9);
        _code.Int32(TokenSpanContract.Stride);
        _code.Bytes(0x48, 0xF7, 0xE1, 0x48, 0x85, 0xD2);
        RuntimeBranch(0x85, 0);
        _code.Load64(Spill(spill), destinationRegister: true);
        _code.Bytes(0x48, 0x01, 0xD0);
        RuntimeBranch(0x82, 0);
        CopyToFrame(Spill(spill + 1), TokenSpanContract.Stride);
        _code.Address(Spill(spill + 1));
    }
}
