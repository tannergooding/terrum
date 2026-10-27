// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;

namespace Terrum.CodeAnalysis.Modules;

/// <summary>Emits indexed TypeSpec rows checked against referenced table counts.</summary>
public static class TrRmTypeSpecTableWriter
{
    public static byte[] Builtin(byte code, byte qualifiers = 0)
        => [0x01, qualifiers, code];

    public static byte[] InlineFamily(byte familyCode, byte argumentCode, byte qualifiers = 0)
        => [0x01, qualifiers, familyCode, argumentCode];

    public static byte[] Reference(byte form, uint row, byte qualifiers = 0)
    {
        if (form is not (0x02 or 0x03 or 0x04))
        {
            throw new ArgumentOutOfRangeException(nameof(form));
        }

        return ReferenceCore(form, row, qualifiers);
    }

    /// <summary>Creates a Standard native function-pointer TypeSpec row.</summary>
    /// <param name="callingConvention">The assigned NativeCallingConvention value.</param>
    /// <param name="signatureContractMethodDef">The bodyless MethodDef anchor row.</param>
    /// <returns>The seven-byte Standard TypeSpec `0x0D` row.</returns>
    public static byte[] NativeFunctionPointer(byte callingConvention,
        uint signatureContractMethodDef)
    {
        if (callingConvention is < 1 or > 4)
        {
            throw new ArgumentOutOfRangeException(nameof(callingConvention));
        }
        if (signatureContractMethodDef is 0 or > 0xFF_FFFF)
        {
            throw new ArgumentOutOfRangeException(nameof(signatureContractMethodDef));
        }

        var bytes = new byte[7];
        bytes[0] = 0x0D;
        bytes[2] = callingConvention;
        Write(bytes, 3, signatureContractMethodDef);
        return bytes;
    }

    /// <summary>Creates a Development-profile signature-row TypeSpec reference.</summary>
    /// <param name="signatureRow">The one-based Development signature-table row.</param>
    /// <returns>The six-byte Development TypeSpec row.</returns>
    public static byte[] DevelopmentFunctionPointerSignatureReference(uint signatureRow)
        => ReferenceCore(0x0D, signatureRow, 0);

    private static byte[] ReferenceCore(byte form, uint row, byte qualifiers)
    {
        var bytes = new byte[6];
        bytes[0] = form;
        bytes[1] = qualifiers;
        Write(bytes, 2, row);
        return bytes;
    }

    public static byte[] Constructed(uint genericTypeSpec,
        ReadOnlySpan<uint> argumentReferences, byte qualifiers = 0)
    {
        if (argumentReferences.IsEmpty)
        {
            throw new ArgumentException("A constructed TypeSpec requires arguments.",
                nameof(argumentReferences));
        }

        var bytes = new byte[checked(6 + argumentReferences.Length * sizeof(uint))];
        bytes[0] = 0x05;
        bytes[1] = qualifiers;
        Write(bytes, 2, genericTypeSpec);
        for (var index = 0; index < argumentReferences.Length; index++)
        {
            Write(bytes, 6 + index * sizeof(uint), argumentReferences[index]);
        }

        return bytes;
    }

    public static byte[] Wrapped(byte form, uint innerTypeSpec, byte qualifiers = 0)
    {
        if (form is < 0x06 or > 0x0B)
        {
            throw new ArgumentOutOfRangeException(nameof(form));
        }

        var bytes = new byte[6];
        bytes[0] = form;
        bytes[1] = qualifiers;
        Write(bytes, 2, innerTypeSpec);
        return bytes;
    }

    public static byte[] IndexedFamily(byte familyCode, uint argumentTypeSpec,
        byte qualifiers = 0)
    {
        var bytes = new byte[7];
        bytes[0] = 0x0C;
        bytes[1] = qualifiers;
        bytes[2] = familyCode;
        Write(bytes, 3, argumentTypeSpec);
        return bytes;
    }

    /// <summary>Encodes TypeSpec rows and validates their referenced table counts.</summary>
    public static byte[] Encode(IReadOnlyList<ReadOnlyMemory<byte>> rows,
        int typeDefCount, int importCount, int genericParamCount,
        int nativeFunctionPointerSignatureCount = 0)
        => Encode(rows, typeDefCount, importCount, genericParamCount,
            nativeFunctionPointerSignatureCount, TrRmArtifactSchema.Standard);

    internal static byte[] Encode(IReadOnlyList<ReadOnlyMemory<byte>> rows,
        int typeDefCount, int importCount, int genericParamCount,
        int nativeFunctionPointerSignatureCount, TrRmArtifactSchema schema,
        int methodDefCount = 0)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var result = TrRmVariableTableWriter.EncodeBlobs(rows);
        _ = new TrRmTypeSpecTableReader(result, rows.Count,
            typeDefCount, importCount, genericParamCount,
            nativeFunctionPointerSignatureCount, schema, methodDefCount);
        return result;
    }

    private static void Write(byte[] bytes, int offset, uint value)
        => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);
}
