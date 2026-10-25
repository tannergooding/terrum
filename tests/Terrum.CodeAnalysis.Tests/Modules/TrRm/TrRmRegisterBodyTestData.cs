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
    {
        if (resultRegister is not (0x0E or 0x0F))
        {
            throw new ArgumentOutOfRangeException(nameof(resultRegister));
        }

        var stringBytes = TrRmStringTableWriter.EncodeTextRows([
            ("", 0), ("module", 0), ("Main", 0),
        ]);
        var strings = new TrRmStringTableReader(stringBytes, 3);
        var moduleBytes = TrRmModuleTableWriter.Encode([
            new TrRmModuleRow(2, 1, 0, 1, 1, 1, 1, 1, 0, 0, 1),
        ], strings, 0, 0, 1, 0, 0);
        var modules = new TrRmModuleTableReader(moduleBytes, 1, strings,
            0, 0, 1, 0, 0);
        var typeSpecBytes = TrRmTypeSpecTableWriter.Encode([
            TrRmTypeSpecTableWriter.Builtin(0),
            TrRmTypeSpecTableWriter.Builtin(8),
        ], 0, 0, 0);
        var typeSpecs = new TrRmTypeSpecTableReader(typeSpecBytes, 2, 0, 0, 0);
        var signatureBytes = TrRmMethodSigTableWriter.Encode([
            new TrRmMethodSigRow(0xF200_0008, 0, 0, 0, 0, 0),
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
        byte[] payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        if ((typeCode == 0) != (payload.Length == 0))
        {
            throw new ArgumentException("Unit alone has an empty scalar payload.", nameof(payload));
        }

        var typeSpecRef = 0xF200_0000u | typeCode;
        var stringBytes = TrRmStringTableWriter.EncodeTextRows([
            ("", 0), ("module", 0), ("Main", 0),
        ]);
        var strings = new TrRmStringTableReader(stringBytes, 3);
        var featureBytes = TrRmFeatureTableWriter.Encode([
            TrRmFeatureRow.TypedFrozenValues,
        ]);
        var moduleBytes = TrRmModuleTableWriter.Encode([
            new TrRmModuleRow(2, 1, 0, 1, 1, 1, 1, 1, 1, 1, 1),
        ], strings, 0, 0, 1, 0, 1);
        var typeSpecBytes = TrRmTypeSpecTableWriter.Encode([
            TrRmTypeSpecTableWriter.Builtin(typeCode),
        ], 0, 0, 0);
        var typeSpecs = new TrRmTypeSpecTableReader(typeSpecBytes, 1, 0, 0, 0);
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
        return TrRmArtifactWriter.Encode(sections);
    }

}
