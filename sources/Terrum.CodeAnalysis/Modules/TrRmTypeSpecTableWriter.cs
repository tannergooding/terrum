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

        var bytes = new byte[6];
        bytes[0] = form;
        bytes[1] = qualifiers;
        Write(bytes, 2, row);
        return bytes;
    }

    public static byte[] Constructed(uint genericTypeSpec, uint typeArgFirst,
        uint typeArgCount, byte qualifiers = 0)
    {
        var bytes = new byte[14];
        bytes[0] = 0x05;
        bytes[1] = qualifiers;
        Write(bytes, 2, genericTypeSpec);
        Write(bytes, 6, typeArgFirst);
        Write(bytes, 10, typeArgCount);
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

    public static byte[] Encode(IReadOnlyList<ReadOnlyMemory<byte>> rows,
        int typeDefCount, int importCount, int genericParamCount, int typeArgCount)
    {
        var result = TrRmVariableTableWriter.EncodeBlobs(rows);
        _ = new TrRmTypeSpecTableReader(result, rows.Count,
            typeDefCount, importCount, genericParamCount, typeArgCount);
        return result;
    }

    private static void Write(byte[] bytes, int offset, uint value)
        => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);
}
