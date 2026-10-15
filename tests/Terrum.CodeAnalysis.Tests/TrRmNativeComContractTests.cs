// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using Terrum.CodeAnalysis.Modules;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests;

public sealed class TrRmNativeComContractTests
{
    [Test]
    public void NativeComAcquisitionRequiresFixedOwnerOutputStateSourceAndCleanup()
    {
        Create();

        Assert.Throws<InvalidDataException>(() => Create(includeOutputState: false));
        Assert.Throws<InvalidDataException>(() => Create(outputStateOnFalse: 1));
        Assert.Throws<InvalidDataException>(() => Create(sourceOutputOrdinal: 1));
        Assert.Throws<InvalidDataException>(() => Create(acquisitionFailureDisposition: 0));
        Assert.Throws<InvalidDataException>(() => Create(acquisitionOwnerTypeDef: 1));
        Assert.Throws<InvalidDataException>(() => Create(includeOwnerCleanup: false));
        Assert.Throws<InvalidDataException>(() => Create(includeNativeComFeature: false));
    }

    private static void Create(
        bool includeOutputState = true,
        byte outputStateOnFalse = 0,
        uint sourceOutputOrdinal = 0,
        uint acquisitionFailureDisposition = 1,
        uint acquisitionOwnerTypeDef = 2,
        bool includeOwnerCleanup = true,
        bool includeNativeComFeature = true)
    {
        var typeContracts = new List<byte[]>
        {
            NativeComInterface(Guid.Parse("00112233-4455-6677-8899-aabbccddeeff")),
            NativeComOwner(1, 2),
        };
        if (includeOwnerCleanup)
        {
            typeContracts.Add(Cleanup(3, 2));
        }

        var sourceContracts = new[]
        {
            Effects(0x1600_0001, 4),
            NativeOutput(parameterOrdinal: 0, resultCondition: 1, failureDisposition: 1),
        };
        var wrapperContracts = new List<byte[]>();
        if (includeOutputState)
        {
            wrapperContracts.Add(OutputState(outputStateOnFalse));
        }
        wrapperContracts.Add(NativeComAcquisition(
            outputOrdinal: 0, acquisitionOwnerTypeDef, sourceMethodDef: 1,
            sourceOutputOrdinal, acquisitionFailureDisposition));
        var cleanupContracts = new[] { Effects(0x1600_0003, 1) };

        var allContracts = new List<byte[]>(typeContracts.Count + sourceContracts.Length
            + wrapperContracts.Count + cleanupContracts.Length);
        allContracts.AddRange(typeContracts);
        allContracts.AddRange(sourceContracts);
        allContracts.AddRange(wrapperContracts);
        allContracts.AddRange(cleanupContracts);

        var typeFirst = 1;
        var sourceFirst = typeContracts.Count + 1;
        var wrapperFirst = sourceFirst + sourceContracts.Length;
        var cleanupFirst = wrapperFirst + wrapperContracts.Count;

        var stringBytes = TrRmStringTableWriter.EncodeTextRows(
        [
            ("", 0), ("Module", 0), ("Owner", 0), ("Acquire", 0),
            ("TryAcquire", 0), ("Close", 0), ("value", 0), ("IFixture", 0),
            ("kernel32.dll", 0), ("GetFixture", 0),
        ]);
        var strings = new TrRmStringTableReader(stringBytes, 10);
        var moduleBytes = new byte[44];
        Write(moduleBytes, 0, 2);
        Write(moduleBytes, 4, 1);
        Write(moduleBytes, 28, 1);
        Write(moduleBytes, 32, 1);
        Write(moduleBytes, 36, 1);
        Write(moduleBytes, 40, 1);
        var modules = new TrRmModuleTableReader(moduleBytes, 1, strings,
            0, 2, 3, 0);

        var typeDefBytes = new byte[2 * 40];
        Write(typeDefBytes, 0, 2);
        Write(typeDefBytes, 4, 8);
        Write(typeDefBytes, 8, 12);
        Write(typeDefBytes, 32, (uint)typeFirst);
        Write(typeDefBytes, 36, 1);
        Write(typeDefBytes, 40, 2);
        Write(typeDefBytes, 44, 3);
        Write(typeDefBytes, 48, 4);
        Write(typeDefBytes, 72, 2);
        Write(typeDefBytes, 76, (uint)typeContracts.Count - 1);
        var types = new TrRmTypeDefTableReader(typeDefBytes, 2, strings, modules,
            0, 0, 0, allContracts.Count);

        var typeSpecBytes = TrRmTypeSpecTableWriter.Encode(
        [
            TrRmTypeSpecTableWriter.Builtin(8),
            TrRmTypeSpecTableWriter.Builtin(1),
            TrRmTypeSpecTableWriter.Builtin(0),
            TrRmTypeSpecTableWriter.InlineFamily(0x8C, 0),
            TrRmTypeSpecTableWriter.IndexedFamily(0x8C, 4, qualifiers: 1),
            TrRmTypeSpecTableWriter.Reference(0x02, 2),
        ], typeDefCount: 2, importCount: 0, genericParamCount: 0, typeArgCount: 0);
        var typeSpecs = new TrRmTypeSpecTableReader(typeSpecBytes, 6,
            typeDefCount: 2, importCount: 0, genericParamCount: 0, typeArgCount: 0);

        var signaturesBytes = new byte[3 * 24];
        WriteSignature(signaturesBytes, 0, resultTypeSpec: 1, firstParameter: 1, parameterCount: 1);
        WriteSignature(signaturesBytes, 24, resultTypeSpec: 2, firstParameter: 2, parameterCount: 1);
        WriteSignature(signaturesBytes, 48, resultTypeSpec: 3, receiverTypeSpec: 6);
        var signatures = new TrRmMethodSigTableReader(signaturesBytes, 3,
            typeSpecs, paramCount: 2, genericParamCount: 0);

        var parameterBytes = new byte[2 * 16];
        WriteParameter(parameterBytes, 0, signature: 1, typeSpec: 5);
        WriteParameter(parameterBytes, 16, signature: 2, typeSpec: 6);
        var parameters = new TrRmParamTableReader(parameterBytes, 2, signatures, typeSpecs);

        var methodBytes = new byte[3 * 28];
        WriteMethod(methodBytes, 0, ownerType: 0, name: 4, signature: 1,
            flags: 2, contractFirst: sourceFirst, contractCount: sourceContracts.Length);
        WriteMethod(methodBytes, 28, ownerType: 0, name: 5, signature: 2,
            flags: 2, contractFirst: wrapperFirst, contractCount: wrapperContracts.Count, body: 1);
        WriteMethod(methodBytes, 56, ownerType: 2, name: 6, signature: 3,
            flags: 0, contractFirst: cleanupFirst, contractCount: cleanupContracts.Length, body: 2);
        var methods = new TrRmMethodDefTableReader(methodBytes, 3, strings, modules,
            types, signatures, allContracts.Count, bodyCount: 2);

        var contractBytes = Section(allContracts);
        _ = new TrRmContractTableReader(contractBytes,
            allContracts.Count, types, methods, signatures, parameters, typeSpecs);

        var featureBytes = includeNativeComFeature
            ? TrRmFeatureTableWriter.Encode([TrRmFeatureRow.NativeComContracts])
            : [];
        var featureCount = includeNativeComFeature ? 1 : 0;
        Write(moduleBytes, 16, includeNativeComFeature ? 1u : 0u);
        Write(moduleBytes, 20, (uint)featureCount);
        var bodyBytes = new byte[]
        {
            0, 0, 0, 0, 1, 0, 0, 0, 2, 0, 0, 0, 0, 0,
        };
        var nativeImportBytes = new byte[16];
        Write(nativeImportBytes, 0, 1);
        Write(nativeImportBytes, 4, 9);
        Write(nativeImportBytes, 8, 10);
        Write(nativeImportBytes, 12, 1);
        TrRmSection[] featureSections = includeNativeComFeature
            ? [new TrRmSection(0x0004, 0, (uint)featureCount, featureBytes)]
            : [];
        var artifact = TrRmArtifactWriter.Encode([
            new TrRmSection(0x0001, 0, 1, moduleBytes),
            new TrRmSection(0x0002, 0, 10, stringBytes),
            .. featureSections,
            new TrRmSection(0x0011, 0, 6, typeSpecBytes),
            new TrRmSection(0x0013, 0, 2, typeDefBytes),
            new TrRmSection(0x0016, 0, 3, methodBytes),
            new TrRmSection(0x0017, 0, 3, signaturesBytes),
            new TrRmSection(0x0018, 0, 2, parameterBytes),
            new TrRmSection(0x001B, 0, (uint)allContracts.Count, contractBytes),
            new TrRmSection(0x0020, 0, 2, bodyBytes),
            new TrRmSection(0x0021, 0, 1, nativeImportBytes),
        ]);
        TrRmStructuralMetadataValidator.Validate(artifact);
    }

    private static byte[] NativeComInterface(Guid iid)
    {
        var payload = new byte[20];
        _ = iid.TryWriteBytes(payload.AsSpan(0, 16), bigEndian: true, out _);
        return Contract(0x1300_0001, 7, payload);
    }

    private static byte[] NativeComOwner(uint interfaceTypeDef, uint ownerTypeDef)
    {
        var payload = new byte[4];
        Write(payload, 0, interfaceTypeDef);
        return Contract(0x1300_0000u | ownerTypeDef, 9, payload);
    }

    private static byte[] Cleanup(uint methodDef, uint ownerTypeDef)
    {
        var payload = new byte[4];
        Write(payload, 0, methodDef);
        return Contract(0x1300_0000u | ownerTypeDef, 5, payload);
    }

    private static byte[] Effects(uint ownerDefId, uint mask)
    {
        var payload = new byte[4];
        Write(payload, 0, mask);
        return Contract(ownerDefId, 1, payload);
    }

    private static byte[] NativeOutput(uint parameterOrdinal, uint resultCondition, uint failureDisposition)
    {
        var payload = new byte[16];
        Write(payload, 0, parameterOrdinal);
        Write(payload, 4, resultCondition);
        Write(payload, 8, failureDisposition);
        return Contract(0x1600_0001, 6, payload);
    }

    private static byte[] OutputState(byte onFalse)
    {
        var payload = new byte[8];
        Write(payload, 0, 0);
        payload[4] = 1;
        payload[5] = onFalse;
        return Contract(0x1600_0002, 2, payload);
    }

    private static byte[] NativeComAcquisition(uint outputOrdinal, uint acquisitionOwnerTypeDef,
        uint sourceMethodDef, uint sourceOutputOrdinal, uint failureDisposition)
    {
        var payload = new byte[20];
        Write(payload, 0, outputOrdinal);
        Write(payload, 4, acquisitionOwnerTypeDef);
        Write(payload, 8, sourceMethodDef);
        Write(payload, 12, sourceOutputOrdinal);
        Write(payload, 16, failureDisposition);
        return Contract(0x1600_0002, 11, payload);
    }

    private static byte[] Contract(uint owner, ushort kind, byte[] payload)
    {
        var row = new byte[8 + payload.Length];
        Write(row, 0, owner);
        BinaryPrimitives.WriteUInt16LittleEndian(row.AsSpan(4), kind);
        payload.CopyTo(row.AsSpan(8));
        return row;
    }

    private static byte[] Section(IReadOnlyList<byte[]> rows)
    {
        var indexSize = (rows.Count + 1) * sizeof(uint);
        var dataSize = 0;
        foreach (var row in rows)
        {
            dataSize += row.Length;
        }

        var section = new byte[indexSize + dataSize];
        var offset = 0;
        for (var index = 0; index < rows.Count; index++)
        {
            Write(section, index * sizeof(uint), (uint)offset);
            rows[index].CopyTo(section.AsSpan(indexSize + offset));
            offset += rows[index].Length;
        }
        Write(section, rows.Count * sizeof(uint), (uint)offset);
        return section;
    }

    private static void WriteSignature(byte[] bytes, int offset, uint resultTypeSpec,
        uint receiverTypeSpec = 0, uint firstParameter = 0, uint parameterCount = 0)
    {
        Write(bytes, offset, resultTypeSpec);
        Write(bytes, offset + 4, receiverTypeSpec);
        Write(bytes, offset + 8, firstParameter);
        Write(bytes, offset + 12, parameterCount);
    }

    private static void WriteParameter(byte[] bytes, int offset, uint signature, uint typeSpec)
    {
        Write(bytes, offset, signature);
        Write(bytes, offset + 4, 0);
        Write(bytes, offset + 8, typeSpec);
    }

    private static void WriteMethod(byte[] bytes, int offset, uint ownerType, uint name, uint signature,
        uint flags, int contractFirst, int contractCount, uint body = 0)
    {
        Write(bytes, offset, ownerType);
        Write(bytes, offset + 4, name);
        Write(bytes, offset + 8, signature);
        Write(bytes, offset + 12, flags);
        Write(bytes, offset + 16, (uint)contractFirst);
        Write(bytes, offset + 20, (uint)contractCount);
        Write(bytes, offset + 24, body);
    }

    private static void Write(byte[] bytes, int offset, uint value)
        => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);
}
