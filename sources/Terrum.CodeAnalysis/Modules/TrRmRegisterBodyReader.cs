// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.IO;

namespace Terrum.CodeAnalysis.Modules;

internal readonly ref struct TrRmRegisterBodyReader
{
    private const int BlockSize = 12;
    private const int RecordHeaderSize = 4;

    private readonly ReadOnlySpan<byte> _homeMap;
    private readonly ReadOnlySpan<byte> _typeMap;
    private readonly ReadOnlySpan<byte> _methodMap;
    private readonly ReadOnlySpan<byte> _fieldMap;
    private readonly ReadOnlySpan<byte> _constantMap;
    private readonly ReadOnlySpan<byte> _blocks;
    private readonly ReadOnlySpan<byte> _recordOffsets;
    private readonly ReadOnlySpan<byte> _recordData;
    private readonly ReadOnlySpan<byte> _code;
    private readonly int _recordCount;
    private readonly int _contractCount;
    private readonly TrRmArtifactSchema _schema;
    private readonly TrRmExceptionDirectoryReader _exceptionDirectory;
    private readonly TrRmA3LayoutResourceLedger? _resourceLedger;

    internal TrRmRegisterBodyReader(ReadOnlySpan<byte> body, int contractCount = 0,
        TrRmArtifactSchema schema = TrRmArtifactSchema.Standard,
        TrRmA3LayoutResourceLedger? resourceLedger = null)
    {
        if (body.Length < 17)
        {
            throw InvalidBody();
        }

        resourceLedger?.ConsumeWork(checked((ulong)body.Length + 7UL));
        if (contractCount is < 0 or > 0xFF_FFFF
            || body.Length < 17 || body[0] != 0x01 || body[1] != 0x01
            || (body[2] & 0xE0) != 0 || ReadUInt16(body, 3) != 0
            || schema is not (TrRmArtifactSchema.Standard
                or TrRmArtifactSchema.Development
                or TrRmArtifactSchema.DevelopmentFunctionPointerEffectsDraft
                or TrRmArtifactSchema.CopyOriginDraft
                or TrRmArtifactSchema.ForeignInvocationDraft
                or TrRmArtifactSchema.ForeignInvocationStorageAddressDraft
                or TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyDraft
                or TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyEmptyInitializationDraft
                or TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyEmptyUnitInitializationDraft
                or TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyEmptyUnitScalarFrozenValuesDraft
                or TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyReturnCoverageDraft
                or TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyReturnCoverageRepeatInitializationDraft
                or TrRmArtifactSchema.LocalAggregateLayoutDraft
                or TrRmArtifactSchema.ForeignInvocationStorageFixedArrayGenericCopyDraft
                or TrRmArtifactSchema.ScalarFrozenValuesDraft
                or TrRmArtifactSchema.NominalEnumsDraft
                or TrRmArtifactSchema.RootPinRuntimeDraft
                or TrRmArtifactSchema.GeneralPredicateGraphsDraft
                or TrRmArtifactSchema.RegisterExceptionRegionsDraft))
        {
            throw InvalidBody();
        }

        MapMask = body[2];
        _contractCount = contractCount;
        _schema = schema;
        _exceptionDirectory = default;
        _resourceLedger = resourceLedger;
        var position = 5;
        var homeCount = ReadMapCount(body, ref position, MapMask, 0);
        var typeCount = ReadMapCount(body, ref position, MapMask, 1);
        var methodCount = ReadMapCount(body, ref position, MapMask, 2);
        var fieldCount = ReadMapCount(body, ref position, MapMask, 3);
        var constantCount = ReadMapCount(body, ref position, MapMask, 4);
        BlockCount = ReadCount(body, ref position);
        _recordCount = ReadCount(body, ref position);
        CodeByteLength = ReadCount(body, ref position);
        if (BlockCount is < 1 or > 65_536
            || _recordCount < (schema.UsesRegisterExceptionRegions() ? 2 : 1)
            || _recordCount > 65_536
            || CodeByteLength == 0)
        {
            throw InvalidBody();
        }

        HomeCount = homeCount;
        TypeCount = typeCount;
        MethodCount = methodCount;
        FieldCount = fieldCount;
        ConstantCount = constantCount;
        resourceLedger?.ConsumeWork(checked(1UL
            + (ulong)homeCount + (ulong)typeCount + (ulong)methodCount
            + (ulong)fieldCount + (ulong)constantCount
            + (ulong)BlockCount + 2UL * (ulong)_recordCount + 1));

        var mapStart = position;
        var blockStart = AddExtent(mapStart,
            (long)homeCount * 8 + (long)typeCount * 4 + (long)methodCount * 4
                + (long)fieldCount * 4 + (long)constantCount * 8,
            body.Length);
        var recordOffsetsStart = AddExtent(blockStart,
            (long)BlockCount * BlockSize, body.Length);
        var recordDataStart = AddExtent(recordOffsetsStart,
            ((long)_recordCount + 1) * sizeof(uint), body.Length);

        _homeMap = body.Slice(mapStart, homeCount * 8);
        var nextMap = mapStart + homeCount * 8;
        _typeMap = body.Slice(nextMap, typeCount * 4);
        nextMap += typeCount * 4;
        _methodMap = body.Slice(nextMap, methodCount * 4);
        nextMap += methodCount * 4;
        _fieldMap = body.Slice(nextMap, fieldCount * 4);
        nextMap += fieldCount * 4;
        _constantMap = body.Slice(nextMap, constantCount * 8);
        _blocks = body.Slice(blockStart, BlockCount * BlockSize);
        _recordOffsets = body.Slice(recordOffsetsStart,
            (_recordCount + 1) * sizeof(uint));

        if (ReadUInt32(_recordOffsets, 0) != 0)
        {
            throw InvalidBody();
        }

        var previousOffset = 0u;
        for (var record = 1; record <= _recordCount; record++)
        {
            resourceLedger?.ConsumeWork();
            var nextOffset = ReadUInt32(_recordOffsets, record * sizeof(uint));
            if (nextOffset < previousOffset + RecordHeaderSize
                || nextOffset > body.Length - recordDataStart)
            {
                throw InvalidBody();
            }

            previousOffset = nextOffset;
        }

        var codeStart = AddExtent(recordDataStart, previousOffset, body.Length);
        if (CodeByteLength != body.Length - codeStart)
        {
            throw InvalidBody();
        }

        _recordData = body.Slice(recordDataStart, (int)previousOffset);
        _code = body.Slice(codeStart, CodeByteLength);
        if (schema.UsesRegisterExceptionRegions())
        {
            if (GetRecord(0)[0] != 0x0F)
            {
                throw InvalidBody();
            }

            _exceptionDirectory = new TrRmExceptionDirectoryReader(
                GetRecord(0), BlockCount, HomeCount, TypeCount, resourceLedger);
            for (var record = 1; record < _recordCount; record++)
            {
                resourceLedger?.ConsumeWork();
                if (GetRecord(record)[0] == 0x0F)
                {
                    throw InvalidBody();
                }
            }
        }

        for (var record = 0; record < _recordCount; record++)
        {
            resourceLedger?.ConsumeWork();
            ValidateRecord(GetRecord(record));
        }
        ValidateExceptionDirectoryRecords();
        ValidateBlocks();
    }

    internal byte MapMask { get; }
    internal int HomeCount { get; }
    internal int TypeCount { get; }
    internal int MethodCount { get; }
    internal int FieldCount { get; }
    internal int ConstantCount { get; }
    internal TrRmArtifactSchema Schema => _schema;
    internal TrRmA3LayoutResourceLedger? ResourceLedger => _resourceLedger;
    internal int BlockCount { get; }
    internal int RecordCount => _recordCount;
    internal int CodeByteLength { get; }
    internal bool HasExceptionDirectory
        => _schema.UsesRegisterExceptionRegions();
    internal TrRmExceptionDirectoryReader ExceptionDirectory
        => _exceptionDirectory;
    internal bool HasCoreExceptionIdentityUse
    {
        get
        {
            if (!HasExceptionDirectory)
            {
                return false;
            }

            var directory = ExceptionDirectory;
            if (directory.RegionCount > 1 || directory.HandlerCount != 0)
            {
                return true;
            }

            for (var blockIndex = 0; blockIndex < BlockCount; blockIndex++)
            {
                var code = GetBlockCode(blockIndex);
                for (var position = 0; position < code.Length;)
                {
                    _resourceLedger?.ConsumeWork();
                    var opcode = code[position];
                    if (opcode is 0xB4 or 0xB6 or 0xB7
                        or 0xE8 or 0xEA or 0xEB)
                    {
                        return true;
                    }

                    position += InstructionLength(opcode, _schema);
                }
            }

            return false;
        }
    }

    internal bool HasRootPinOperations
    {
        get
        {
            var position = 0;
            while (position < _code.Length)
            {
                _resourceLedger?.ConsumeWork(2);
                var opcode = _code[position];
                if (opcode == 0xF0)
                {
                    return true;
                }

                _resourceLedger?.ConsumeWork();
                position += InstructionLength(opcode, _schema);
            }

            return false;
        }
    }

    internal ReadOnlySpan<byte> HomeMap => _homeMap;
    internal ReadOnlySpan<byte> TypeMap => _typeMap;
    internal ReadOnlySpan<byte> MethodMap => _methodMap;
    internal ReadOnlySpan<byte> FieldMap => _fieldMap;
    internal ReadOnlySpan<byte> ConstantMap => _constantMap;
    internal ReadOnlySpan<byte> Code => _code;

    internal ReadOnlySpan<byte> GetBlock(int blockIndex)
    {
        _resourceLedger?.ConsumeWork();
        if ((uint)blockIndex >= (uint)BlockCount)
        {
            throw InvalidBody();
        }

        return _blocks.Slice(blockIndex * BlockSize, BlockSize);
    }

    internal ReadOnlySpan<byte> GetRecord(int recordIndex)
    {
        _resourceLedger?.ConsumeWork(2);
        if ((uint)recordIndex >= (uint)_recordCount)
        {
            throw InvalidBody();
        }

        var start = ReadUInt32(_recordOffsets, recordIndex * sizeof(uint));
        var end = ReadUInt32(_recordOffsets, (recordIndex + 1) * sizeof(uint));
        return _recordData.Slice((int)start, (int)(end - start));
    }

    internal ReadOnlySpan<byte> GetBlockCode(int blockIndex)
    {
        var block = GetBlock(blockIndex);
        var start = ReadUInt32(block, 0);
        var length = ReadUInt32(block, 4);
        _resourceLedger?.ConsumeWork();
        return _code.Slice((int)start, (int)length);
    }

    private void ValidateBlocks()
    {
        uint codeCursor = 0;
        for (var blockIndex = 0; blockIndex < BlockCount; blockIndex++)
        {
            _resourceLedger?.ConsumeWork();
            var block = _blocks.Slice(blockIndex * BlockSize, BlockSize);
            var codeOffset = ReadUInt32(block, 0);
            var codeLength = ReadUInt32(block, 4);
            var entryStateRecord = ReadUInt16(block, 8);
            if (codeOffset != codeCursor || codeLength == 0
                || codeLength > CodeByteLength - codeCursor
                || entryStateRecord >= _recordCount
                || ReadUInt16(block, 10) != 0
                || GetRecord(entryStateRecord)[0] != 0x01)
            {
                throw InvalidBody();
            }

            ValidateInstructions(_code.Slice((int)codeOffset, (int)codeLength),
                blockIndex);
            codeCursor += codeLength;
        }

        if (codeCursor != CodeByteLength)
        {
            throw InvalidBody();
        }

        ValidateDispatchEntryStates();
    }

    private void ValidateInstructions(ReadOnlySpan<byte> block, int blockIndex)
    {
        var position = 0;
        var lastOpcode = -1;
        while (position < block.Length)
        {
            _resourceLedger?.ConsumeWork(2);
            var opcode = block[position];
            if ((opcode is 0xB6 or 0xEA)
                && !_schema.UsesRegisterExceptionRegions())
            {
                throw InvalidBody();
            }

            _resourceLedger?.ConsumeWork();
            var instructionLength = InstructionLength(opcode, _schema);
            if (instructionLength > block.Length - position)
            {
                throw InvalidBody();
            }

            _resourceLedger?.ConsumeWork();
            var instruction = block.Slice(position, instructionLength);
            ValidateInstructionReferences(opcode, instruction, blockIndex);
            if (_schema.UsesRegisterExceptionRegions()
                && (opcode is 0x17 or 0x18 or 0xB8 or 0xEB)
                && _exceptionDirectory.GetBlockRegion(blockIndex) != 0)
            {
                throw InvalidBody();
            }
            position += instructionLength;
            if (IsDefiniteTerminator(opcode) && position != block.Length)
            {
                throw InvalidBody();
            }

            lastOpcode = opcode;
        }

        if (!IsPotentialTerminator(lastOpcode))
        {
            throw InvalidBody();
        }
    }

    private void ValidateInstructionReferences(byte opcode,
        ReadOnlySpan<byte> instruction, int blockIndex)
    {
        if (instruction.Length == 4)
        {
            ValidateLongControlSelector(opcode, instruction[1]);
            ValidateLongRegisterFamilyPayload(opcode, instruction);
            ValidateLongInstructionReferences(opcode, instruction);
            ValidateExceptionControlTransfer(opcode, instruction, blockIndex);
            return;
        }

        if (opcode is >= 0xA0 and <= 0xAF)
        {
            ValidateRecordReference(instruction[1] >> 4, 0x06);
            ValidateExceptionControlTransfer(opcode, instruction, blockIndex);
            return;
        }

        var x = instruction.Length > 1 ? instruction[1] & 0x0F : 0;
        var y = instruction.Length > 1 ? instruction[1] >> 4 : 0;
        switch (opcode)
        {
            case 0x05:
            case 0x07:
                ValidateMapIndex(0, HomeCount);
                break;
            case 0x06:
            case 0x08:
                ValidateMapIndex(1, HomeCount);
                break;
            case 0x09:
                ValidateMapIndex(0, ConstantCount);
                break;
            case 0x13:
            case 0x15:
            case 0x60:
                ValidateMapIndex(y, HomeCount);
                break;
            case 0x14:
                ValidateMapIndex(x, HomeCount);
                break;
            case 0x16:
                ValidateMapIndex(y, ConstantCount);
                break;
            case >= 0x50 and <= 0x52:
                ValidateMapIndex(y, TypeCount);
                break;
            case 0x61:
                ValidateRecordReference(y, 0x04);
                break;
            case 0x17:
                if (y != 0)
                {
                    throw InvalidBody();
                }
                break;
            case 0x18:
                if (instruction[1] != 0)
                {
                    throw InvalidBody();
                }
                break;
            case 0x66:
                if (y != 0)
                {
                    throw InvalidBody();
                }
                break;
            case 0x80:
            case 0x82:
                ValidateMapIndex(y, MethodCount);
                break;
            case 0x90:
                ValidateRecordReference(instruction[1], 0x02);
                break;
            case 0x91:
                ValidateRecordReference(y, 0x04);
                break;
            case 0x92:
                ValidateRecordReference(instruction[1], 0x05);
                break;
            case 0xB0:
                if (instruction[1] >= BlockCount)
                {
                    throw InvalidBody();
                }
                break;
            case 0xB1:
                ValidateRecordReference(instruction[1] >> 4, 0x06);
                break;
            case 0xB2:
                ValidateRecordReference(instruction[1], 0x07);
                break;
            case 0xB3:
                ValidateRecordReference(instruction[1], 0x08);
                break;
            case 0xB4:
                if (!_schema.UsesRegisterExceptionRegions())
                {
                    throw InvalidBody();
                }
                ValidateRecordReference(instruction[1], 0x09);
                break;
            case 0xB6:
                if (!_schema.UsesRegisterExceptionRegions())
                {
                    throw InvalidBody();
                }
                ValidateRecordReference(instruction[1], 0x0A);
                break;
            case 0xB5:
                if (instruction[1] != 0)
                {
                    throw InvalidBody();
                }
                break;
            case 0xB7:
                if (!_schema.UsesRegisterExceptionRegions()
                    || instruction[1] != 0)
                {
                    throw InvalidBody();
                }
                break;
            case 0xB8:
                if (instruction[1] >= HomeCount)
                {
                    throw InvalidBody();
                }
                break;
        }

        ValidateExceptionControlTransfer(opcode, instruction, blockIndex);
        ValidateShortRegisterRoles(opcode, x, y);
    }

    private void ValidateLongControlSelector(byte opcode, byte control)
    {
        var isAssigned = opcode switch
        {
            0xC0 => control is (>= 0x01 and <= 0x0F) or (>= 0x20 and <= 0x25),
            0xC1 => control is >= 0x01 and <= 0x07,
            0xC2 => control <= 0x02,
            0xC3 or 0xC4 or 0xC5 => control <= 0x0F,
            0xE9 => _schema.UsesForeignInvocationContracts()
                && control == 0,
            0xEC => _schema.UsesForeignInvocationStorageAddresses()
                && control == 0,
            0xF0 => _schema.UsesRootPinRuntime() && control is >= 1 and <= 7,
            _ => true,
        };
        if (!isAssigned)
        {
            throw InvalidBody();
        }
    }

    private void ValidateLongRegisterFamilyPayload(byte opcode,
        ReadOnlySpan<byte> instruction)
    {
        if (opcode is < 0xC0 or > 0xCC)
        {
            return;
        }

        var payload = ReadUInt16(instruction, 2);
        var result0 = payload & 0x0F;
        var result1 = (payload >> 4) & 0x0F;
        var source1 = (payload >> 12) & 0x0F;
        switch (opcode)
        {
            case 0xC0:
                var hasTwoResults = instruction[1] is 0x0E or 0x0F;
                var hasSingleInput = instruction[1] is 0x04 or 0x08 or 0x25;
                if (result0 == 14 || (!hasTwoResults && result1 != 15)
                    || (hasTwoResults && result1 == 14)
                    || (hasTwoResults && result0 < 14 && result1 < 14
                        && result0 == result1)
                    || (hasSingleInput && source1 != 15))
                {
                    throw InvalidBody();
                }
                break;
            case 0xC1:
                if (result0 == 14 || result1 != 15
                    || (instruction[1] == 0x04 && source1 != 15))
                {
                    throw InvalidBody();
                }
                break;
            case 0xC2:
                if (result0 == 14 || result1 != 15)
                {
                    throw InvalidBody();
                }
                break;
            case >= 0xC7 and <= 0xCA:
                ValidateArithmeticRecipePayload(opcode, instruction);
                break;
            case 0xC3:
            case 0xC4:
            case 0xC5:
            case 0xC6:
            case 0xCB:
            case 0xCC:
                if (result0 == 14)
                {
                    throw InvalidBody();
                }
                break;
        }
    }

    private void ValidateArithmeticRecipePayload(byte opcode,
        ReadOnlySpan<byte> instruction)
    {
        var control = instruction[1];
        var payload = ReadUInt16(instruction, 2);
        var destination = payload & 0x0F;
        var sourceA = (payload >> 4) & 0x0F;
        var sourceB = (payload >> 8) & 0x0F;
        var sourceC = (payload >> 12) & 0x0F;
        var innerOperation = control & 0x03;
        var outerOperation = (control >> 2) & 0x03;
        var innerIsNegation = innerOperation == 0;
        var outerIsNegation = outerOperation == 0;
        var outerOperandOrder = (control & 0x10) != 0;
        var negateA = (control & 0x20) != 0;
        var negateB = (control & 0x40) != 0;
        var negateC = (control & 0x80) != 0;
        var absentRegister = opcode == 0xCA ? 15 : 14;
        if (destination == 14 || IsArithmeticRecipeAbsent(sourceA, negateA, absentRegister))
        {
            throw InvalidBody();
        }

        var innerUsesAbsentOperand = IsArithmeticRecipeAbsent(
            sourceB, negateB, absentRegister);
        if (innerIsNegation != innerUsesAbsentOperand)
        {
            throw InvalidBody();
        }

        var outerUsesAbsentOperand = IsArithmeticRecipeAbsent(
            sourceC, negateC, absentRegister);
        var hasInvalidOuterOrder = outerIsNegation && outerOperandOrder;
        if (outerIsNegation != outerUsesAbsentOperand || hasInvalidOuterOrder)
        {
            throw InvalidBody();
        }
    }

    private static bool IsArithmeticRecipeAbsent(int register, bool modifier,
        int absentRegister)
        => register == absentRegister && modifier;

    private void ValidateLongInstructionReferences(byte opcode,
        ReadOnlySpan<byte> instruction)
    {
        var control = instruction[1];
        var reference = ReadUInt16(instruction, 2);
        switch (opcode)
        {
            case >= 0xD0 and <= 0xD3:
                ValidateMapIndex(reference, TypeCount);
                break;
            case 0xD4:
                if ((control & 0xF0) != 0)
                {
                    throw InvalidBody();
                }
                ValidateMapIndex(reference, ConstantCount);
                break;
            case 0xD5:
            case 0xD7:
                if ((control & 0xF0) != 0)
                {
                    throw InvalidBody();
                }
                ValidateMapIndex(reference, HomeCount);
                break;
            case 0xD6:
                if ((control >> 4) is < 1 or > 3)
                {
                    throw InvalidBody();
                }
                ValidateMapIndex(reference, HomeCount);
                break;
            case 0xD8:
            case 0xDA:
            case 0xDB:
            case 0xDD:
                if ((control & 0xF0) != 0)
                {
                    throw InvalidBody();
                }
                ValidateRecordReference(reference, 0x04);
                break;
            case 0xD9:
            case 0xDC:
                if (control != 0)
                {
                    throw InvalidBody();
                }
                ValidateRecordReference(reference, opcode == 0xD9 ? (byte)0x05 : (byte)0x04);
                break;
            case 0xDE:
                var queryKind = control >> 4;
                if (queryKind is < 1 or > 5 || (control & 0x0F) == 14)
                {
                    throw InvalidBody();
                }
                ValidateMapIndex(reference,
                    queryKind is 3 or 4 ? FieldCount : TypeCount);
                break;
            case 0xE0:
                if (control != 0)
                {
                    throw InvalidBody();
                }
                ValidateRecordReference(reference, 0x02);
                break;
            case 0xE1:
                ValidateMapIndex(reference, MethodCount);
                break;
            case 0xE2:
                if ((reference & 0xFFF0) != 0)
                {
                    throw InvalidBody();
                }
                break;
            case 0xE3:
                if (control != 0)
                {
                    throw InvalidBody();
                }
                ValidateMapIndex(reference, BlockCount);
                break;
            case 0xE4:
                if ((control & 0xF0) != 0)
                {
                    throw InvalidBody();
                }
                ValidateRecordReference(reference, 0x06);
                break;
            case 0xE5:
                ValidateRecordReference(reference, 0x06);
                break;
            case 0xE6:
                if (control != 0)
                {
                    throw InvalidBody();
                }
                ValidateRecordReference(reference, 0x07);
                break;
            case 0xE7:
                if (control != 0)
                {
                    throw InvalidBody();
                }
                ValidateRecordReference(reference, 0x08);
                break;
            case 0xE8:
                if (!_schema.UsesRegisterExceptionRegions() || control != 0)
                {
                    throw InvalidBody();
                }
                ValidateRecordReference(reference, 0x09);
                break;
            case 0xEA:
                if (!_schema.UsesRegisterExceptionRegions() || control != 0)
                {
                    throw InvalidBody();
                }
                ValidateRecordReference(reference, 0x0A);
                break;
            case 0xE9:
                if (!_schema.UsesForeignInvocationContracts()
                    || control != 0)
                {
                    throw InvalidBody();
                }
                ValidateRecordReference(reference, 0x0D);
                break;
            case 0xEC:
                if (!_schema.UsesForeignInvocationStorageAddresses()
                    || control != 0)
                {
                    throw InvalidBody();
                }
                ValidateRecordReference(reference, 0x0E);
                break;
            case 0xF0:
                if (!_schema.UsesRootPinRuntime()
                    || control is < 1 or > 7)
                {
                    throw InvalidBody();
                }
                ValidateRecordReference(reference, 0x0D);
                ValidateRootPinAction(control, GetRecord(reference)[RecordHeaderSize..]);
                break;
            case 0xEB:
                if (control != 0)
                {
                    throw InvalidBody();
                }
                ValidateMapIndex(reference, HomeCount);
                break;
        }

        ValidateLongRegisterRoles(opcode, instruction);
    }

    private static void ValidateShortRegisterRoles(byte opcode, int x, int y)
    {
        switch (opcode)
        {
            case 0x10:
            case 0x11:
            case 0x13:
            case 0x15:
            case 0x16:
                ValidateWritableRegister(x);
                break;
            case 0x19:
                ValidateWritableRegister(x);
                ValidateGeneralRegister(y);
                break;
            case 0x1A:
                ValidateGeneralRegister(x);
                break;
            case >= 0x20 and <= 0x2B:
            case >= 0x30 and <= 0x36:
            case 0x40:
            case 0x41:
            case >= 0x50 and <= 0x52:
            case >= 0x70 and <= 0x7F:
                ValidateWritableRegister(x);
                break;
            case 0x12:
                if (x > 12 || y > 14)
                {
                    throw InvalidBody();
                }
                break;
            case 0x60:
            case 0x61:
            case 0x62:
            case 0x64:
            case 0x66:
            case 0x91:
                ValidateGeneralRegister(x);
                break;
            case 0x63:
            case 0x65:
            case 0x67:
                ValidateWritableRegister(x);
                ValidateGeneralRegister(y);
                break;
            case 0x80:
            case 0x82:
                ValidateGeneralRegister(x);
                break;
            case 0x81:
            case 0x83:
                ValidateGeneralRegister(x);
                ValidateGeneralRegister(y);
                break;
        }
    }

    private void ValidateLongRegisterRoles(byte opcode,
        ReadOnlySpan<byte> instruction)
    {
        var control = instruction[1];
        var lowRegister = control & 0x0F;
        var highRegister = control >> 4;
        switch (opcode)
        {
            case >= 0xD0 and <= 0xD3:
                ValidateWritableRegister(highRegister);
                break;
            case 0xD4:
            case 0xD5:
            case 0xD7:
                ValidateWritableRegister(lowRegister);
                break;
            case 0xD8:
                ValidateGeneralRegister(lowRegister);
                break;
            case 0xDA:
            case 0xDB:
            case 0xDD:
            case 0xDE:
                ValidateWritableRegister(lowRegister);
                break;
            case 0xE1:
                ValidateGeneralRegister(lowRegister);
                ValidateWritableRegister(highRegister);
                break;
            case 0xE2:
                ValidateGeneralRegister(lowRegister);
                ValidateGeneralRegister(highRegister);
                ValidateWritableRegister(ReadUInt16(instruction, 2) & 0x0F);
                break;
        }
    }

    private static void ValidateWritableRegister(int register)
    {
        if (register == 14)
        {
            throw InvalidBody();
        }
    }

    private static void ValidateGeneralRegister(int register)
    {
        if (register > 13)
        {
            throw InvalidBody();
        }
    }

    private static void ValidateMapIndex(int index, int count)
    {
        if (index >= count)
        {
            throw InvalidBody();
        }
    }

    private void ValidateRecordReference(int recordIndex, byte expectedKind)
    {
        if (recordIndex >= _recordCount || GetRecord(recordIndex)[0] != expectedKind)
        {
            throw InvalidBody();
        }
    }

    internal bool IsCallSiteAtBlockEnd(int blockIndex, int recordIndex)
    {
        var block = GetBlockCode(blockIndex);
        var position = 0;
        while (position < block.Length)
        {
            _resourceLedger?.ConsumeWork(2);
            var opcode = block[position];
            _resourceLedger?.ConsumeWork();
            var length = InstructionLength(opcode, _schema);
            if (position + length == block.Length)
            {
                _resourceLedger?.ConsumeWork();
                return opcode switch
                {
                    0x90 => block[position + 1] == recordIndex,
                    0xE0 => ReadUInt16(block, position + 2) == recordIndex,
                    _ => false,
                };
            }

            position += length;
        }

        return false;
    }

    internal static int InstructionLength(byte opcode)
        => InstructionLength(opcode, TrRmArtifactSchema.Standard);

    internal static int InstructionLength(byte opcode, TrRmArtifactSchema schema)
    {
        if (opcode <= 0x0F)
        {
            return 1;
        }

        if (opcode is >= 0x10 and <= 0x1A
            or >= 0x20 and <= 0x2B
            or >= 0x30 and <= 0x36
            or 0x40 or 0x41
            or >= 0x50 and <= 0x52
            or >= 0x60 and <= 0x67
            or >= 0x70 and <= 0x7F
            or >= 0x80 and <= 0x83
            or >= 0x90 and <= 0x92
            or >= 0xA0 and <= 0xAF
            or >= 0xB0 and <= 0xB8)
        {
            return 2;
        }

        if ((opcode is (>= 0xC0 and <= 0xCC)
            or (>= 0xD0 and <= 0xDE)
            or (>= 0xE0 and <= 0xE8)
            or 0xEA or 0xEB)
            || (opcode == 0xE9
                && schema.UsesForeignInvocationContracts())
            || (opcode == 0xEC
                && schema.UsesForeignInvocationStorageAddresses())
            || (opcode == 0xF0 && schema.UsesRootPinRuntime()))
        {
            return 4;
        }

        throw InvalidBody();
    }

    private static bool IsPotentialTerminator(int opcode)
        => opcode == 0x00
            || opcode is 0x17 or 0x18
                or >= 0xA0 and <= 0xAF
                or 0xB0 or 0xB1 or 0xB2 or 0xB4 or 0xB5 or 0xB6 or 0xB7 or 0xB8
                or 0x90 or 0xE0
                or >= 0xE3 and <= 0xE6
                or 0xE8 or 0xEA or 0xEB;

    private static bool IsDefiniteTerminator(byte opcode)
        => opcode == 0x00
            || opcode is 0x17 or 0x18
                or >= 0xA0 and <= 0xAF
                or 0xB0 or 0xB1 or 0xB2 or 0xB4 or 0xB5 or 0xB6 or 0xB7 or 0xB8
                or >= 0xE3 and <= 0xE6
                or 0xE8 or 0xEA or 0xEB;

    private int ReadMapCount(ReadOnlySpan<byte> body, ref int position,
        byte mapMask, int bit)
    {
        _resourceLedger?.ConsumeWork(2);
        if ((mapMask & (1 << bit)) == 0)
        {
            return 0;
        }

        var count = ReadCount(body, ref position);
        if (count > 65_536)
        {
            throw InvalidBody();
        }

        return count;
    }

    private int ReadCount(ReadOnlySpan<byte> body, ref int position)
    {
        _resourceLedger?.ConsumeWork(3);
        if (position < 0 || position > body.Length - sizeof(uint))
        {
            throw InvalidBody();
        }

        var value = ReadUInt32(body, position);
        position += sizeof(uint);
        if (value > int.MaxValue)
        {
            throw InvalidBody();
        }

        return (int)value;
    }

    private int AddExtent(int start, long length, int limit)
    {
        _resourceLedger?.ConsumeWork(5);
        var end = start + length;
        if (start < 0 || length < 0 || end > limit || end > int.MaxValue)
        {
            throw InvalidBody();
        }

        return (int)end;
    }

    private void ValidateRecord(ReadOnlySpan<byte> record)
    {
        var maximumRecordKind = _schema.UsesRegisterExceptionRegions()
            ? 0x0F
            : _schema.UsesForeignInvocationStorageAddresses() ? 0x0E : 0x0D;
        if (record.Length < RecordHeaderSize || record[0] is < 0x01
            || record[0] > maximumRecordKind
            || record[1] != 0 || ReadUInt16(record, 2) != 0)
        {
            throw InvalidBody();
        }

        var payload = record[RecordHeaderSize..];
        switch (record[0])
        {
            case 0x01:
                ValidateBlockEntryState(payload);
                break;
            case 0x02:
                ValidateCallSite(payload);
                break;
            case 0x03:
                if (payload.Length != 16 || payload[0] is not (1 or 2)
                    || payload[1] is < 1 or > 5
                    || payload[2] is < 1 or > 3
                    || payload[3] > 5 || payload[6] > 1
                    || payload[7] != 0 || ReadUInt16(payload, 10) != 0
                    || (payload[6] == 0
                        && (ReadUInt16(payload, 8) != 0
                            || ReadUInt32(payload, 12) != 0))
                    || (payload[3] == 0
                        && (payload[6] != 0 || ReadUInt32(payload, 12) != 0))
                    || (ReadUInt32(payload, 12) != 0
                        && ReadUInt32(payload, 12) > _contractCount))
                {
                    throw InvalidBody();
                }
                break;
            case 0x04:
                if (payload.Length != 20
                    || payload[0] is < 1 or > 4 || payload[1] is < 1 or > 5
                    || payload[2] > 2 || payload[3] > 2
                    || payload[8] > 2
                    || payload[9] > (_schema.UsesForeignInvocationStorageFixedArrays()
                        ? 3 : 2)
                    || (payload[9] == 3
                        && (!_schema.UsesForeignInvocationStorageFixedArrays()
                            || payload[0] != 3))
                    || !IsValidSelector(payload[2], ReadUInt16(payload, 4))
                    || !IsValidSelector(payload[3], ReadUInt16(payload, 6))
                    || !IsValidSelector(payload[8], ReadUInt16(payload, 10))
                    || ReadUInt16(payload, 16) >= TypeCount
                    || !IsValidTargetReference(payload)
                    || ReadUInt16(payload, 18) != 0)
                {
                    throw InvalidBody();
                }
                break;
            case 0x05:
                var isEmptyArrayInitialization
                    = _schema.UsesForeignInvocationStorageFixedArrayEmptyInitialization()
                    && payload.Length == 16 && payload[0] == 1
                    && payload[1] == 0 && payload[2] == 1
                    && payload[3] == 0
                    && ReadUInt16(payload, 6) == 0
                    && ReadUInt32(payload, 8) == 0
                    && payload[12] == 0 && payload[13] == 0
                    && ReadUInt16(payload, 14) == 0;
                var isRepeatCopy
                    = _schema
                        .UsesForeignInvocationStorageFixedArrayRepeatInitialization()
                        && payload.Length == 16 && payload[0] == 1
                        && payload[1] is 1 or 2 && payload[2] == 1
                        && payload[3] == 6
                        && ReadUInt32(payload, 8) == 0
                        && payload[12] == 0 && payload[13] == 0
                        && ReadUInt16(payload, 14) == 0;
                if (payload.Length != 16 || payload[0] is < 1 or > 2
                    || payload[1] > 2
                    || payload[2] is < 1 or > 3
                    || (payload[3] > 5 && !isRepeatCopy)
                    || payload[12] > 1 || payload[13] != 0
                    || !IsValidSelector(payload[0], ReadUInt16(payload, 4))
                    || !IsValidSelector(payload[1], ReadUInt16(payload, 6))
                    || (payload[1] == 0
                        ? !isEmptyArrayInitialization
                        : ((payload[3] is < 1 or > 5)
                            && !isRepeatCopy))
                    || (payload[12] == 0
                        && (ReadUInt16(payload, 14) != 0
                            || ReadUInt32(payload, 8) != 0))
                    || (payload[12] != 0
                        && (ReadUInt32(payload, 8) == 0
                            || ReadUInt32(payload, 8) > _contractCount)))
                {
                    throw InvalidBody();
                }
                break;
            case 0x06:
                if (payload.Length != 4
                    || ReadUInt16(payload, 0) >= BlockCount
                    || ReadUInt16(payload, 2) >= BlockCount)
                {
                    throw InvalidBody();
                }
                break;
            case 0x07:
                ValidateSwitch(payload);
                break;
            case 0x08:
                if (payload.Length != 8 || payload[4] is not (1 or 2)
                    || ReadUInt16(payload, 0) >= TypeCount
                    || ReadUInt16(payload, 2) >= _recordCount
                    || GetRecord(ReadUInt16(payload, 2))[0] != 0x0B
                    || payload[5] > 15
                    || payload[7] != 0
                    || (payload[4] == 1 && payload[6] != 0)
                    || (payload[4] == 2 && payload[6] > 15))
                {
                    throw InvalidBody();
                }
                break;
            case 0x09:
                if (payload.Length != 8 || payload[0] is < 1 or > 3
                    || payload[7] != 0)
                {
                    throw InvalidBody();
                }
                if (_schema.UsesRegisterExceptionRegions()
                    && (payload[0] == 3
                        || (payload[0] == 2
                            && ReadUInt16(payload, 4)
                                >= _exceptionDirectory.RegionCount)))
                {
                    throw InvalidBody();
                }
                if (payload[0] == 2
                    ? payload[1] != 0 || ReadUInt16(payload, 2) != 0
                    : payload[1] is not (1 or 2)
                        || !IsValidSelector(payload[1], ReadUInt16(payload, 2))
                        || ReadUInt16(payload, 4) != 0)
                {
                    throw InvalidBody();
                }
                break;
            case 0x0A:
                ValidateRegionExit(payload);
                break;
            case 0x0B:
                ValidateConstructorBinding(payload);
                break;
            case 0x0C:
                throw InvalidBody();
            case 0x0D:
                if (_schema.UsesForeignInvocationContracts())
                {
                    ValidateNativeAddress(payload);
                }
                else if (_schema.UsesRootPinRuntime())
                {
                    ValidateRootPinReferenceSupport(payload);
                }
                else
                {
                    throw InvalidBody();
                }
                break;
            case 0x0E:
                if (!_schema.UsesForeignInvocationStorageAddresses())
                {
                    throw InvalidBody();
                }
                ValidateStorageAddress(payload);
                break;
            case 0x0F:
                if (!_schema.UsesRegisterExceptionRegions())
                {
                    throw InvalidBody();
                }
                break;
        }
    }

    private void ValidateStorageAddress(ReadOnlySpan<byte> payload)
    {
        if (payload.Length != 12)
        {
            throw InvalidBody();
        }

        var sourceKind = payload[0];
        var resultKind = payload[1];
        var sourceIndex = ReadUInt16(payload, 2);
        var resultIndex = ReadUInt16(payload, 4);
        var pointerTypeMapIndex = ReadUInt16(payload, 6);
        var requiredElementCount = ReadUInt32(payload, 8);
        if (sourceKind is not (1 or 2) || resultKind is not (1 or 2)
            || (sourceKind == 1
                ? sourceIndex > 13
                : sourceIndex >= HomeCount)
            || (resultKind == 1
                ? resultIndex is > 15 or 14
                : resultIndex >= HomeCount)
            || pointerTypeMapIndex >= TypeCount
            || requiredElementCount == 0
            || (sourceKind == 2 && resultKind == 2
                && sourceIndex == resultIndex))
        {
            throw InvalidBody();
        }

        if (resultKind == 2)
        {
            var home = HomeMap.Slice(resultIndex * 8, 8);
            if (home[0] != 3 || ReadUInt16(home, 2) != pointerTypeMapIndex)
            {
                throw InvalidBody();
            }
        }
    }

    private void ValidateRootPinReferenceSupport(ReadOnlySpan<byte> payload)
    {
        if (payload.Length != 12)
        {
            throw InvalidBody();
        }

        var sourceKind = payload[0];
        var resultCarrier = payload[1];
        var sourceIndex = ReadUInt16(payload, 2);
        var resultRegister = payload[4];
        var sourceMode = payload[5];
        var sourceTypeMapIndex = ReadUInt16(payload, 6);
        var resultTypeMapIndex = ReadUInt16(payload, 8);
        if (sourceKind is not (1 or 2)
            || resultCarrier > 2
            || (sourceKind == 1
                ? sourceIndex > 13
                : sourceIndex >= HomeCount)
            || (resultCarrier == 0
                ? resultRegister != 15 || resultTypeMapIndex != 0
                : resultRegister > 13 || resultTypeMapIndex >= TypeCount)
            || sourceMode is < 1 or > 3
            || sourceTypeMapIndex >= TypeCount
            || ReadUInt16(payload, 10) != 0)
        {
            throw InvalidBody();
        }
    }

    private static void ValidateRootPinAction(byte action,
        ReadOnlySpan<byte> payload)
    {
        var sourceKind = payload[0];
        var resultCarrier = payload[1];
        var sourceMode = payload[5];
        var valid = action switch
        {
            1 => sourceKind == 1 && resultCarrier == 1 && sourceMode == 1,
            2 => resultCarrier == 2 && sourceMode == 1,
            3 or 4 => resultCarrier == 1 && sourceMode == 1,
            5 => resultCarrier == 1 && sourceMode == 2,
            6 => resultCarrier == 0 && sourceMode == 3,
            7 => resultCarrier == 1 && sourceMode == 1,
            _ => false,
        };
        if (!valid)
        {
            throw InvalidBody();
        }
    }

    private void ValidateNativeAddress(ReadOnlySpan<byte> payload)
    {
        if (payload.Length != 8)
        {
            throw InvalidBody();
        }

        var resultKind = payload[0];
        var resultIndex = ReadUInt16(payload, 2);
        if (resultKind is not (1 or 2) || payload[1] != 0
            || (resultKind == 1
                ? resultIndex > 15 || resultIndex == 14
                : resultIndex >= HomeCount)
            || ReadUInt16(payload, 4) >= MethodCount
            || ReadUInt16(payload, 6) >= TypeCount)
        {
            throw InvalidBody();
        }
    }

    private void ValidateBlockEntryState(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < sizeof(uint))
        {
            throw InvalidBody();
        }

        var registerCount = ReadUInt32(payload, 0);
        if (registerCount > 14
            || 4L + registerCount * 5L != payload.Length)
        {
            throw InvalidBody();
        }

        var previousRegister = -1;
        for (var row = 0; row < registerCount; row++)
        {
            _resourceLedger?.ConsumeWork();
            var offset = 4 + row * 5;
            var register = payload[offset];
            var carrier = payload[offset + 1];
            var typeMapIndex = ReadUInt16(payload, offset + 2);
            var access = payload[offset + 4];
            if (register > 13 || register <= previousRegister || carrier > 2
                || (carrier == 0 && (access != 0 || typeMapIndex >= TypeCount))
                || (carrier == 1 && (access is < 1 or > 5
                    || typeMapIndex >= TypeCount))
                || (carrier == 2 && (typeMapIndex != 0 || access != 0)))
            {
                throw InvalidBody();
            }

            previousRegister = register;
        }
    }

    private void ValidateCallSite(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < 16 || payload[0] is < 1 or > 3
            || payload[1] > 2 || ReadUInt16(payload, 2) != 0)
        {
            throw InvalidBody();
        }

        var resultIndex = ReadUInt16(payload, 6);
        var inputCount = ReadUInt32(payload, 8);
        var preparationCount = ReadUInt32(payload, 12);
        var targetIndex = ReadUInt16(payload, 4);
        if ((payload[0] == 1 && targetIndex >= MethodCount)
            || (payload[0] == 2 && targetIndex > 13)
            || (payload[0] == 3 && targetIndex >= HomeCount)
            || (payload[1] == 1 && resultIndex is > 15 or 14)
            || (payload[1] == 2 && resultIndex >= HomeCount)
            || (payload[1] == 0 && resultIndex != 0)
            || inputCount > 65_536 || preparationCount > 65_536
            || 16L + inputCount * 4L + preparationCount * 2L != payload.Length)
        {
            throw InvalidBody();
        }

        var inputOffset = 16;
        for (var input = 0; input < inputCount; input++)
        {
            _resourceLedger?.ConsumeWork();
            var selectorKind = payload[inputOffset];
            var sourceUse = payload[inputOffset + 1];
            var selectorIndex = ReadUInt16(payload, inputOffset + 2);
            if (selectorKind > 2 || !IsValidSelector(selectorKind, selectorIndex)
                || (selectorKind == 0
                    ? sourceUse != 0 || selectorIndex != 0
                    : sourceUse is < 1 or > 5))
            {
                throw InvalidBody();
            }

            inputOffset += 4;
        }

        for (var preparation = 0; preparation < preparationCount; preparation++)
        {
            _resourceLedger?.ConsumeWork();
            var recordIndex = ReadUInt16(payload, inputOffset + preparation * 2);
            if (recordIndex >= _recordCount || GetRecord(recordIndex)[0] != 0x03)
            {
                throw InvalidBody();
            }
        }
    }

    private void ValidateSwitch(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < 12 || payload[0] is < 1 or > 2 || payload[1] != 0
            || (payload[0] == 1 && ReadUInt16(payload, 2) > 15)
            || (payload[0] == 2 && ReadUInt16(payload, 2) >= HomeCount)
            || ReadUInt16(payload, 4) >= TypeCount
            || ReadUInt16(payload, 6) >= BlockCount)
        {
            throw InvalidBody();
        }

        var caseCount = ReadUInt32(payload, 8);
        if (caseCount > 65_536 || 12L + caseCount * 4L != payload.Length)
        {
            throw InvalidBody();
        }

        for (var item = 0; item < caseCount; item++)
        {
            _resourceLedger?.ConsumeWork();
            var offset = 12 + item * 4;
            if (ReadUInt16(payload, offset) >= ConstantCount
                || ReadUInt16(payload, offset + 2) >= BlockCount)
            {
                throw InvalidBody();
            }
        }
    }

    private void ValidateConstructorBinding(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < 16)
        {
            throw InvalidBody();
        }

        if (ReadUInt16(payload, 0) >= MethodCount
            || ReadUInt16(payload, 2) >= TypeCount)
        {
            throw InvalidBody();
        }

        var suppliedCount = ReadUInt32(payload, 4);
        var formalCount = ReadUInt32(payload, 8);
        var bindingCount = ReadUInt32(payload, 12);
        if (suppliedCount != 0 || bindingCount != formalCount
            || 16L + bindingCount * 8L != payload.Length)
        {
            throw InvalidBody();
        }

        for (var binding = 0; binding < bindingCount; binding++)
        {
            _resourceLedger?.ConsumeWork();
            var offset = 16 + binding * 8;
            var formalOrdinal = ReadUInt32(payload, offset);
            var bindingKind = payload[offset + 4];
            var sourceUse = payload[offset + 5];
            var constantMapIndex = ReadUInt16(payload, offset + 6);
            if (formalOrdinal != (uint)binding || bindingKind is not (1 or 2)
                || sourceUse > 5 || constantMapIndex >= ConstantCount)
            {
                throw InvalidBody();
            }
        }
    }

    private void ValidateExceptionControlTransfer(byte opcode,
        ReadOnlySpan<byte> instruction, int blockIndex)
    {
        if (!_schema.UsesRegisterExceptionRegions())
        {
            return;
        }

        switch (opcode)
        {
            case >= 0xA0 and <= 0xAF:
            case 0xB1:
                ValidateConditionalEdgeTargets(instruction[1] >> 4,
                    blockIndex);
                break;
            case 0xB0:
                ValidateOrdinaryBranch(blockIndex, instruction[1]);
                break;
            case 0xB2:
                ValidateSwitchTargets(instruction[1], blockIndex);
                break;
            case 0xB4:
                ValidateThrowContext(instruction[1], blockIndex);
                break;
            case 0xB6:
                ValidateRegionExitContext(instruction[1], blockIndex);
                break;
            case 0xE3:
                ValidateOrdinaryBranch(blockIndex,
                    ReadUInt16(instruction, 2));
                break;
            case 0xE4:
            case 0xE5:
                ValidateConditionalEdgeTargets(
                    ReadUInt16(instruction, 2), blockIndex);
                break;
            case 0xE6:
                ValidateSwitchTargets(ReadUInt16(instruction, 2), blockIndex);
                break;
            case 0xE8:
                ValidateThrowContext(ReadUInt16(instruction, 2), blockIndex);
                break;
            case 0xEA:
                ValidateRegionExitContext(ReadUInt16(instruction, 2), blockIndex);
                break;
        }
    }

    private void ValidateConditionalEdgeTargets(int recordIndex,
        int sourceBlock)
    {
        ValidateRecordReference(recordIndex, 0x06);
        var payload = GetRecord(recordIndex)[RecordHeaderSize..];
        ValidateOrdinaryBranch(sourceBlock, ReadUInt16(payload, 0));
        ValidateOrdinaryBranch(sourceBlock, ReadUInt16(payload, 2));
    }

    private void ValidateSwitchTargets(int recordIndex, int sourceBlock)
    {
        ValidateRecordReference(recordIndex, 0x07);
        var payload = GetRecord(recordIndex)[RecordHeaderSize..];
        ValidateOrdinaryBranch(sourceBlock, ReadUInt16(payload, 6));
        var caseCount = ReadUInt32(payload, 8);
        for (var item = 0; item < caseCount; item++)
        {
            _resourceLedger?.ConsumeWork();
            ValidateOrdinaryBranch(sourceBlock,
                ReadUInt16(payload, 14 + (item * 4)));
        }
    }

    private void ValidateOrdinaryBranch(int sourceBlock, int targetBlock)
    {
        var directory = _exceptionDirectory;
        var sourceRegionIndex = directory.GetBlockRegion(sourceBlock);
        var targetRegionIndex = directory.GetBlockRegion(targetBlock);
        var targetRegion = directory.GetRegion(targetRegionIndex);
        if ((targetRegion.Kind is 2 or 3 or 4)
            && targetRegion.EntryBlock == targetBlock)
        {
            throw InvalidBody();
        }

        if (sourceRegionIndex == targetRegionIndex)
        {
            return;
        }

        if (targetRegion.Kind != 1
            || targetRegion.ParentRegionIndex != sourceRegionIndex
            || targetRegion.EntryBlock != targetBlock)
        {
            throw InvalidBody();
        }
    }

    private void ValidateThrowContext(int recordIndex, int blockIndex)
    {
        ValidateRecordReference(recordIndex, 0x09);
        var payload = GetRecord(recordIndex)[RecordHeaderSize..];
        if (payload[0] != 2)
        {
            return;
        }

        var handlerRegionIndex = ReadUInt16(payload, 4);
        var handlerRegion = _exceptionDirectory.GetRegion(handlerRegionIndex);
        if (handlerRegion.Kind != 3)
        {
            throw InvalidBody();
        }

        var currentRegionIndex = _exceptionDirectory.GetBlockRegion(blockIndex);
        while (true)
        {
            if (currentRegionIndex == handlerRegionIndex)
            {
                return;
            }

            if (currentRegionIndex == 0)
            {
                throw InvalidBody();
            }

            currentRegionIndex = _exceptionDirectory
                .GetRegion(currentRegionIndex).ParentRegionIndex;
        }
    }

    private void ValidateRegionExitContext(int recordIndex, int blockIndex)
    {
        ValidateRecordReference(recordIndex, 0x0A);
        var payload = GetRecord(recordIndex)[RecordHeaderSize..];
        var exitKind = payload[0];
        var currentRegionIndex = _exceptionDirectory.GetBlockRegion(blockIndex);
        var currentRegion = _exceptionDirectory.GetRegion(currentRegionIndex);
        var regionCount = ReadUInt32(payload, 8);

        if (exitKind == 1)
        {
            var targetBlock = ReadUInt16(payload, 2);
            var targetOwner = _exceptionDirectory.GetBlockRegion(targetBlock);
            var targetEntryStateRecord = ReadUInt16(GetBlock(targetBlock), 8);
            if (ReadUInt32(GetRecord(targetEntryStateRecord),
                    RecordHeaderSize) != 0)
            {
                throw InvalidBody();
            }

            var pathIndex = 0;
            var regionIndex = currentRegionIndex;
            while (regionIndex != targetOwner)
            {
                if (regionIndex == 0 || pathIndex >= regionCount)
                {
                    throw InvalidBody();
                }

                var region = _exceptionDirectory.GetRegion(regionIndex);
                if (region.Kind is 2 or 4
                    || ReadUInt16(payload, 12 + (pathIndex * sizeof(ushort)))
                        != regionIndex)
                {
                    throw InvalidBody();
                }

                pathIndex++;
                regionIndex = region.ParentRegionIndex;
            }

            if (pathIndex != regionCount)
            {
                throw InvalidBody();
            }

            return;
        }

        switch (exitKind)
        {
            case 2:
                if (currentRegion.Kind != 3
                    || _exceptionDirectory.GetRegion(
                        currentRegion.ParentRegionIndex).Kind != 1
                    || ReadUInt16(payload, 12) != currentRegionIndex
                    || ReadUInt16(payload, 14)
                        != currentRegion.ParentRegionIndex)
                {
                    throw InvalidBody();
                }
                break;
            case 3:
                if (currentRegion.Kind != 2
                    || ReadUInt16(payload, 12) != currentRegionIndex)
                {
                    throw InvalidBody();
                }
                break;
            case 4:
                if (currentRegion.Kind != 4
                    || ReadUInt16(payload, 12) != currentRegionIndex)
                {
                    throw InvalidBody();
                }
                break;
            default:
                throw InvalidBody();
        }
    }

    private void ValidateDispatchEntryStates()
    {
        if (!_schema.UsesRegisterExceptionRegions())
        {
            return;
        }

        for (var regionIndex = 1;
            regionIndex < _exceptionDirectory.RegionCount; regionIndex++)
        {
            _resourceLedger?.ConsumeWork();
            var region = _exceptionDirectory.GetRegion(regionIndex);
            if (region.Kind is not (2 or 3 or 4))
            {
                continue;
            }

            var block = GetBlock(region.EntryBlock);
            var entryState = GetRecord(ReadUInt16(block, 8));
            if (ReadUInt32(entryState, RecordHeaderSize) != 0)
            {
                throw InvalidBody();
            }
        }
    }

    private void ValidateRegionExit(ReadOnlySpan<byte> payload)
    {
        if (_schema.UsesRegisterExceptionRegions())
        {
            if (payload.Length < 12 || payload[1] > 1)
            {
                throw InvalidBody();
            }

            var exitKind = payload[0];
            var hasTarget = payload[1];
            var targetBlock = ReadUInt16(payload, 2);
            var valueKind = payload[4];
            var valueUse = payload[5];
            var valueIndex = ReadUInt16(payload, 6);
            var exceptionRegionCount = ReadUInt32(payload, 8);
            if (12L + (exceptionRegionCount * sizeof(ushort)) != payload.Length
                || (hasTarget == 0 && targetBlock != 0)
                || (hasTarget == 1 && targetBlock >= BlockCount))
            {
                throw InvalidBody();
            }

            switch (exitKind)
            {
                case 1:
                    if (hasTarget != 1 || valueKind != 0 || valueUse != 0
                        || valueIndex != 0
                        || exceptionRegionCount is 0 or > 65_535)
                    {
                        throw InvalidBody();
                    }
                    break;
                case 2:
                case 4:
                    if (hasTarget != 0 || valueKind != 0 || valueUse != 0
                        || valueIndex != 0
                        || (exitKind == 2
                            ? exceptionRegionCount != 2
                            : exceptionRegionCount != 1))
                    {
                        throw InvalidBody();
                    }
                    break;
                case 3:
                    if (hasTarget != 0 || valueKind is not (1 or 2)
                        || valueUse != 1 || exceptionRegionCount != 1
                        || (valueKind == 1
                            ? valueIndex > 15 : valueIndex >= HomeCount))
                    {
                        throw InvalidBody();
                    }
                    break;
                default:
                    throw InvalidBody();
            }

            for (var index = 0; index < exceptionRegionCount; index++)
            {
                _resourceLedger?.ConsumeWork();
                var region = ReadUInt16(payload, 12 + (index * sizeof(ushort)));
                if (region == 0 || region >= _exceptionDirectory.RegionCount)
                {
                    throw InvalidBody();
                }
            }
            return;
        }

        if (payload.Length < 8 || payload[1] > 1
            || (payload[1] == 0 && ReadUInt16(payload, 2) != 0)
            || (payload[1] == 1 && ReadUInt16(payload, 2) >= BlockCount))
        {
            throw InvalidBody();
        }

        var regionCount = ReadUInt32(payload, 4);
        if (8L + regionCount * 2L != payload.Length)
        {
            throw InvalidBody();
        }
    }

    private void ValidateExceptionDirectoryRecords()
    {
        if (!_schema.UsesRegisterExceptionRegions())
        {
            return;
        }

        var seenRegions = new int[_exceptionDirectory.RegionCount];
        for (var recordIndex = 1; recordIndex < _recordCount; recordIndex++)
        {
            _resourceLedger?.ConsumeWork();
            var record = GetRecord(recordIndex);
            if (record[0] != 0x0A)
            {
                continue;
            }

            var payload = record[RecordHeaderSize..];
            var regionCount = ReadUInt32(payload, 8);
            for (var index = 0; index < regionCount; index++)
            {
                _resourceLedger?.ConsumeWork();
                var region = ReadUInt16(payload,
                    12 + (index * sizeof(ushort)));
                if (seenRegions[region] == recordIndex)
                {
                    throw InvalidBody();
                }

                seenRegions[region] = recordIndex;
            }
        }
    }

    private bool IsValidSelector(byte kind, ushort index)
        => kind switch
        {
            0 => index == 0,
            1 => index <= 15,
            2 => index < HomeCount,
            _ => false,
        };

    private bool IsValidTargetReference(ReadOnlySpan<byte> payload)
    {
        var targetKind = payload[0];
        var referenceKind = payload[9];
        var reference = ReadUInt32(payload, 12);
        return targetKind switch
        {
            1 => referenceKind == 0 && reference < (uint)HomeCount,
            2 or 4 => referenceKind == 1 && reference != 0
                && (reference & 0xFF00_0000) == 0,
            3 => (referenceKind == 2 && reference < (uint)MethodCount)
                || (_schema.UsesForeignInvocationStorageFixedArrays()
                    && referenceKind == 3 && reference != 0
                    && (reference & 0xFF00_0000) == 0),
            _ => false,
        };
    }

    private ushort ReadUInt16(ReadOnlySpan<byte> bytes, int offset)
    {
        _resourceLedger?.ConsumeWork(sizeof(ushort) + 1);
        if (offset < 0 || offset > bytes.Length - sizeof(ushort))
        {
            throw InvalidBody();
        }

        return BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(offset, sizeof(ushort)));
    }

    private uint ReadUInt32(ReadOnlySpan<byte> bytes, int offset)
    {
        _resourceLedger?.ConsumeWork(sizeof(uint) + 1);
        if (offset < 0 || offset > bytes.Length - sizeof(uint))
        {
            throw InvalidBody();
        }

        return BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(offset, sizeof(uint)));
    }

    private static InvalidDataException InvalidBody()
        => new("Invalid TRRM register Body.");
}

internal static class TrRmBodyImplementationReader
{
    internal static void Validate(ReadOnlySpan<byte> body, int contractCount,
        TrRmArtifactSchema schema,
        TrRmA3LayoutResourceLedger? resourceLedger = null)
    {
        resourceLedger?.ConsumeWork((ulong)body.Length);
        if (body.IsEmpty)
        {
            throw InvalidBody();
        }

        switch (body[0])
        {
            case 0x01:
                _ = new TrRmRegisterBodyReader(body, contractCount, schema,
                    resourceLedger);
                break;
            case 0x02:
                if (body.Length != 8 || body[1] != 0 || ReadUInt16(body, 2) != 0
                    || !IsToken(ReadUInt32(body, 4), 0x14, 0x1C))
                {
                    throw InvalidBody();
                }
                break;
            case 0x03:
                if (body.Length != 12 || body[1] != 0 || ReadUInt16(body, 2) != 0
                    || !IsToken(ReadUInt32(body, 4), 0x14, 0x1C)
                    || body[10] is < 1 or > 3 || body[11] != 0)
                {
                    throw InvalidBody();
                }
                break;
            case 0x04:
                var dependencyRow = ReadUInt32(body, 8);
                if (body.Length != 12 || body[1] != 0 || ReadUInt16(body, 2) != 0
                    || !IsToken(ReadUInt32(body, 4), 0x14, 0x1C)
                    || dependencyRow == 0 || dependencyRow > contractCount)
                {
                    throw InvalidBody();
                }
                break;
            case 0x05:
                if (body.Length != 8 || body[1] != 0 || ReadUInt16(body, 2) != 0
                    || !IsToken(ReadUInt32(body, 4), 0x16, 0x1C))
                {
                    throw InvalidBody();
                }
                break;
            default:
                throw InvalidBody();
        }
    }

    private static bool IsToken(uint token, byte firstKind, byte secondKind)
    {
        var kind = (byte)(token >> 24);
        var row = token & 0x00FF_FFFF;
        return row != 0 && (kind == firstKind || kind == secondKind);
    }

    private static ushort ReadUInt16(ReadOnlySpan<byte> bytes, int offset)
    {
        if (offset < 0 || offset > bytes.Length - sizeof(ushort))
        {
            throw InvalidBody();
        }

        return BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(offset, sizeof(ushort)));
    }

    private static uint ReadUInt32(ReadOnlySpan<byte> bytes, int offset)
    {
        if (offset < 0 || offset > bytes.Length - sizeof(uint))
        {
            throw InvalidBody();
        }

        return BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(offset, sizeof(uint)));
    }

    private static InvalidDataException InvalidBody()
        => new("Invalid TRRM Body implementation.");
}
