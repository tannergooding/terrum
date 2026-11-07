// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using Terrum.CodeAnalysis.Modules;

namespace Terrum.CodeAnalysis.Tests.Modules.TrRm;

internal static class TrRmRegisterBodyTestData
{
    internal static byte[] MinimalReturnBody()
        =>
        [
            0x01, 0x01, 0, 0, 0,
            1, 0, 0, 0,
            1, 0, 0, 0,
            1, 0, 0, 0,
            0, 0, 0, 0,
            1, 0, 0, 0,
            0, 0, 0, 0,
            0, 0, 0, 0,
            8, 0, 0, 0,
            1, 0, 0, 0,
            0, 0, 0, 0,
            0,
        ];

    internal static byte[] StandardInt32EntryArtifact(byte resultRegister)
        => StandardScalarReturnArtifact(resultRegister, 0x08, isEntryPoint: true,
            methodName: "Main");

    internal static byte[] StandardFloatingRemainderLibraryArtifact(byte typeCode,
        byte operationCode = 0x2B, string methodName = "Remainder",
        byte? secondParameterTypeCode = null, byte firstFormalOrdinal = 0,
        byte secondFormalOrdinal = 1, byte unarySourceRegister = 0,
        byte binarySourceRegister = 1)
        => StandardScalarOperationLibraryArtifact(typeCode, operationCode,
            methodName, secondParameterTypeCode, firstFormalOrdinal,
            secondFormalOrdinal, unarySourceRegister, binarySourceRegister,
            floating: true);

    internal static byte[] StandardIntegerOperationLibraryArtifact(byte typeCode,
        byte operationCode, string methodName = "Compute",
        byte? secondParameterTypeCode = null, byte firstFormalOrdinal = 0,
        byte secondFormalOrdinal = 1, byte unarySourceRegister = 0,
        byte binarySourceRegister = 1)
        => StandardScalarOperationLibraryArtifact(typeCode, operationCode,
            methodName, secondParameterTypeCode, firstFormalOrdinal,
            secondFormalOrdinal, unarySourceRegister, binarySourceRegister,
            floating: false);

    private static byte[] StandardScalarOperationLibraryArtifact(byte typeCode,
        byte operationCode, string methodName, byte? secondParameterTypeCode,
        byte firstFormalOrdinal, byte secondFormalOrdinal, byte unarySourceRegister,
        byte binarySourceRegister, bool floating)
    {
        if (floating
            ? typeCode is not (0x14 or 0x15)
            : typeCode is not (0x06 or 0x07 or 0x08 or 0x09 or 0x0A
                or 0x0B or 0x0C or 0x0D or 0x0E or 0x0F))
        {
            throw new ArgumentOutOfRangeException(nameof(typeCode));
        }
        if (floating
            ? operationCode is < 0x26 or > 0x2B
            : operationCode is < 0x20 or > 0x25)
        {
            throw new ArgumentOutOfRangeException(nameof(operationCode));
        }
        if (firstFormalOrdinal > 0x0F || secondFormalOrdinal > 0x0F
            || unarySourceRegister > 0x0F || binarySourceRegister > 0x0F)
        {
            throw new ArgumentOutOfRangeException(nameof(firstFormalOrdinal));
        }

        var stringBytes = TrRmStringTableWriter.EncodeTextRows([
            ("", 0), ("module", 0), (methodName, 0),
        ]);
        var strings = new TrRmStringTableReader(stringBytes, 3);
        var moduleBytes = TrRmModuleTableWriter.Encode([
            new TrRmModuleRow(2, 1, 0, 0, 1, 1, 1, 1, 0, 0, 1),
        ], strings, 0, 0, 1, 0, 0);
        var modules = new TrRmModuleTableReader(moduleBytes, 1, strings,
            0, 0, 1, 0, 0);
        var secondTypeCode = secondParameterTypeCode ?? typeCode;
        ReadOnlyMemory<byte>[] typeSpecRows = secondTypeCode == typeCode
            ? [TrRmTypeSpecTableWriter.Builtin(typeCode)]
            : [
                TrRmTypeSpecTableWriter.Builtin(typeCode),
                TrRmTypeSpecTableWriter.Builtin(secondTypeCode),
            ];
        var typeSpecBytes = TrRmTypeSpecTableWriter.Encode(typeSpecRows, 0, 0, 0);
        var typeSpecs = new TrRmTypeSpecTableReader(typeSpecBytes,
            typeSpecRows.Length, 0, 0, 0);
        var signatureBytes = TrRmMethodSigTableWriter.Encode([
            new TrRmMethodSigRow(1, 0, 1, 2, 0, 0),
        ], typeSpecs, 2, 0);
        var signatures = new TrRmMethodSigTableReader(signatureBytes, 1,
            typeSpecs, 2, 0);
        var parameterBytes = TrRmParamTableWriter.Encode([
            new TrRmParamRow(1, 0, 1, 0),
            new TrRmParamRow(1, 1, secondTypeCode == typeCode ? 1u : 2u, 0),
        ], signatures, typeSpecs);
        var typeDefs = new TrRmTypeDefTableReader([], 0, strings, modules,
            0, 0, 0, 0);
        var methodBytes = TrRmMethodDefTableWriter.Encode([
            new TrRmMethodDefRow(0, 3, 1, 2, 0, 0, 1),
        ], strings, modules, typeDefs, signatures, 0, 1);
        var body = new byte[] {
            0x01, 0x01, 0, 0, 0,
            1, 0, 0, 0,
            1, 0, 0, 0,
            8, 0, 0, 0,
            0, 0, 0, 0,
            8, 0, 0, 0,
            0, 0, 0, 0,
            0, 0, 0, 0,
            8, 0, 0, 0,
            1, 0, 0, 0,
            0, 0, 0, 0,
            0x11, (byte)(firstFormalOrdinal << 4),
            0x11, (byte)((secondFormalOrdinal << 4) | 1),
            operationCode, (byte)((operationCode is 0x20 or 0x26
                ? unarySourceRegister : binarySourceRegister) << 4),
            0x17, 0x00,
        };
        return TrRmArtifactWriter.Encode([
            new TrRmSection(0x0001, 0, 1, moduleBytes),
            new TrRmSection(0x0002, 0, 3, stringBytes),
            new TrRmSection(0x0011, 0, (uint)typeSpecRows.Length, typeSpecBytes),
            new TrRmSection(0x0016, 0, 1, methodBytes),
            new TrRmSection(0x0017, 0, 1, signatureBytes),
            new TrRmSection(0x0018, 0, 2, parameterBytes),
            new TrRmSection(0x0020, 0, 1,
                TrRmVariableTableWriter.EncodeBodies([body])),
        ]);
    }

    internal static byte[] StandardScalarReturnArtifact(byte resultRegister, byte typeCode,
        bool isEntryPoint, string methodName)
    {
        if (resultRegister is not (0x0E or 0x0F))
        {
            throw new ArgumentOutOfRangeException(nameof(resultRegister));
        }

        var stringBytes = TrRmStringTableWriter.EncodeTextRows([
            ("", 0), ("module", 0), (methodName, 0),
        ]);
        var strings = new TrRmStringTableReader(stringBytes, 3);
        var moduleBytes = TrRmModuleTableWriter.Encode([
            new TrRmModuleRow(2, 1, 0, isEntryPoint ? 1 : 0, 1, 1, 1, 1, 0, 0, 1),
        ], strings, 0, 0, 1, 0, 0);
        var modules = new TrRmModuleTableReader(moduleBytes, 1, strings,
            0, 0, 1, 0, 0);
        var typeSpecBytes = TrRmTypeSpecTableWriter.Encode([
            TrRmTypeSpecTableWriter.Builtin(0),
            TrRmTypeSpecTableWriter.Builtin(typeCode),
        ], 0, 0, 0);
        var typeSpecs = new TrRmTypeSpecTableReader(typeSpecBytes, 2, 0, 0, 0);
        var signatureBytes = TrRmMethodSigTableWriter.Encode([
            new TrRmMethodSigRow(0xF200_0000u | typeCode, 0, 0, 0, 0, 0),
        ], typeSpecs, 0, 0);
        var signatures = new TrRmMethodSigTableReader(signatureBytes, 1,
            typeSpecs, 0, 0);
        var typeDefs = new TrRmTypeDefTableReader([], 0, strings, modules,
            0, 0, 0, 0);
        var methodBytes = TrRmMethodDefTableWriter.Encode([
            new TrRmMethodDefRow(0, 3, 1, 2, 0, 0, 1),
        ], strings, modules, typeDefs, signatures, 0, 1);
        var body = new byte[] {
            0x01, 0x01, 0, 0, 0,
            1, 0, 0, 0,
            1, 0, 0, 0,
            2, 0, 0, 0,
            0, 0, 0, 0,
            2, 0, 0, 0,
            0, 0, 0, 0,
            0, 0, 0, 0,
            8, 0, 0, 0,
            1, 0, 0, 0, 0, 0, 0, 0,
            0x17, resultRegister,
        };
        return TrRmArtifactWriter.Encode([
            new TrRmSection(0x0001, 0, 1, moduleBytes),
            new TrRmSection(0x0002, 0, 3, stringBytes),
            new TrRmSection(0x0011, 0, 2, typeSpecBytes),
            new TrRmSection(0x0016, 0, 1, methodBytes),
            new TrRmSection(0x0017, 0, 1, signatureBytes),
            new TrRmSection(0x0020, 0, 1,
                TrRmVariableTableWriter.EncodeBodies([body])),
        ]);
    }

    internal static byte[] StandardScalarConstantEntryArtifact(byte typeCode,
        byte[] payload, bool isEntryPoint = true, string methodName = "Main",
        TrRmArtifactSchema schema = TrRmArtifactSchema.Standard)
    {
        ArgumentNullException.ThrowIfNull(payload);
        if (typeCode == 0 != (payload.Length == 0))
        {
            throw new ArgumentException("Unit alone has an empty scalar payload.", nameof(payload));
        }

        var typeSpecRef = 0xF200_0000u | typeCode;
        var stringBytes = TrRmStringTableWriter.EncodeTextRows([
            ("", 0), ("module", 0), (methodName, 0),
        ]);
        var strings = new TrRmStringTableReader(stringBytes, 3);
        var featureBytes = TrRmFeatureTableWriter.Encode([
            TrRmFeatureRow.TypedFrozenValues,
        ], schema);
        var moduleBytes = TrRmModuleTableWriter.Encode([
            new TrRmModuleRow(2, 1, 0, isEntryPoint ? 1 : 0, 1, 1, 1, 1, 1, 1, 1),
        ], strings, 0, 0, 1, 0, 1);
        var typeSpecBytes = TrRmTypeSpecTableWriter.Encode([
            TrRmTypeSpecTableWriter.Builtin(typeCode),
        ], 0, 0, 0, 0, schema);
        var typeSpecs = new TrRmTypeSpecTableReader(typeSpecBytes, 1, 0, 0, 0,
            0, schema);
        var signatureBytes = TrRmMethodSigTableWriter.Encode([
            new TrRmMethodSigRow(typeSpecRef, 0, 0, 0, 0, 0),
        ], typeSpecs, 0, 0);
        var signatures = new TrRmMethodSigTableReader(signatureBytes, 1,
            typeSpecs, 0, 0);
        var modules = new TrRmModuleTableReader(moduleBytes, 1, strings,
            0, 0, 1, 0, 1);
        var typeDefs = new TrRmTypeDefTableReader([], 0, strings, modules,
            0, 0, 0, 0);
        var methodBytes = TrRmMethodDefTableWriter.Encode([
            new TrRmMethodDefRow(0, 3, 1, 2, 0, 0, 1),
        ], strings, modules, typeDefs, signatures, 0, 1);
        const int codeByteLength = 2;
        var body = new byte[57 + codeByteLength];
        body[0] = 1;
        body[1] = 1;
        body[2] = 0x10;
        BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(5), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(9), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(13), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(17), (uint)codeByteLength);
        BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(21), typeSpecRef);
        BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(25), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(33), (uint)codeByteLength);
        BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(41), 0);
        BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(45), 8);
        body[49] = 1;
        body[57] = typeCode == 0 ? (byte)0x18 : (byte)0x09;
        body[58] = 0;

        var sections = new List<TrRmSection>
        {
            new(0x0001, 0, 1, moduleBytes),
            new(0x0002, 0, 3, stringBytes),
            new(0x0004, 0, 1, featureBytes),
            new(0x0011, 0, 1, typeSpecBytes),
            new(0x0016, 0, 1, methodBytes),
            new(0x0017, 0, 1, signatureBytes),
            new(0x0020, 0, 1,
                TrRmVariableTableWriter.EncodeBodies([body])),
            new(0x0030, 0, 1, new byte[] { 1, 0, 0, 0, 1, 0, 0, 0 }),
        };
        if (payload.Length != 0)
        {
            sections.Add(new TrRmSection(0x0003, 0, 1,
                TrRmVariableTableWriter.EncodeBlobs([payload])));
        }
        var node = new byte[24];
        BinaryPrimitives.WriteUInt32LittleEndian(node, typeSpecRef);
        node[4] = 1;
        BinaryPrimitives.WriteUInt32LittleEndian(node.AsSpan(8),
            payload.Length == 0 ? 0u : 1u);
        sections.Add(new TrRmSection(0x003F, 0, 1, node));
        sections.Sort(static (left, right) => left.Kind.CompareTo(right.Kind));
        return TrRmArtifactWriter.Encode(sections, schema);
    }
}
