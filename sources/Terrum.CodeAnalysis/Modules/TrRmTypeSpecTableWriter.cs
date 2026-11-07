// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;

namespace Terrum.CodeAnalysis.Modules;

/// <summary>Emits indexed TypeSpec rows checked against referenced table counts.</summary>
public static class TrRmTypeSpecTableWriter
{
    /// <summary>Creates a built-in type TypeSpec row.</summary>
    /// <param name="code">The built-in type code.</param>
    /// <param name="qualifiers">The TypeSpec qualifier bits.</param>
    /// <returns>The encoded TypeSpec row.</returns>
    public static byte[] Builtin(byte code, byte qualifiers = 0)
        => [0x01, qualifiers, code];

    /// <summary>Creates an inline-family TypeSpec row.</summary>
    /// <param name="familyCode">The family type code.</param>
    /// <param name="argumentCode">The family's inline argument type code.</param>
    /// <param name="qualifiers">The TypeSpec qualifier bits.</param>
    /// <returns>The encoded TypeSpec row.</returns>
    public static byte[] InlineFamily(byte familyCode, byte argumentCode, byte qualifiers = 0)
        => [0x01, qualifiers, familyCode, argumentCode];

    internal static byte[] PredicateCapturedType(uint captureRow)
    {
        if (captureRow is 0 or > 0xFF_FFFF)
        {
            throw new ArgumentOutOfRangeException(nameof(captureRow));
        }

        var bytes = new byte[6];
        bytes[0] = 0x10;
        Write(bytes, 2, captureRow);
        return bytes;
    }

    /// <summary>Creates a row-reference TypeSpec.</summary>
    /// <param name="form">The assigned TypeSpec reference form.</param>
    /// <param name="row">The referenced one-based table row.</param>
    /// <param name="qualifiers">The TypeSpec qualifier bits.</param>
    /// <returns>The encoded TypeSpec row.</returns>
    public static byte[] Reference(byte form, uint row, byte qualifiers = 0)
    {
        if (form is not (0x02 or 0x03 or 0x04))
        {
            throw new ArgumentOutOfRangeException(nameof(form));
        }

        return ReferenceCore(form, row, qualifiers);
    }

    /// <summary>Creates a Standard native function-pointer TypeSpec row.</summary>
    /// <param name="callingConvention">The TypeSpec convention byte.</param>
    /// <param name="signatureContractMethodDef">The bodyless MethodDef anchor row.</param>
    /// <returns>The seven-byte Standard TypeSpec `0x0D` row.</returns>
    public static byte[] NativeFunctionPointer(byte callingConvention,
        uint signatureContractMethodDef)
    {
        if (callingConvention > 4)
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

    /// <summary>Creates a constructed TypeSpec with one or more argument references.</summary>
    /// <param name="genericTypeSpec">The TypeSpec reference for the generic definition.</param>
    /// <param name="argumentReferences">The type or constant argument references.</param>
    /// <param name="qualifiers">The TypeSpec qualifier bits.</param>
    /// <returns>The encoded TypeSpec row.</returns>
    public static byte[] Constructed(uint genericTypeSpec,
        ReadOnlySpan<uint> argumentReferences, byte qualifiers = 0)
    {
        if (argumentReferences.IsEmpty)
        {
            throw new ArgumentException("A constructed TypeSpec requires arguments.",
                nameof(argumentReferences));
        }

        var bytes = new byte[checked(
            6 + (argumentReferences.Length * sizeof(uint)))];
        bytes[0] = 0x05;
        bytes[1] = qualifiers;
        Write(bytes, 2, genericTypeSpec);
        for (var index = 0; index < argumentReferences.Length; index++)
        {
            Write(bytes, 6 + (index * sizeof(uint)), argumentReferences[index]);
        }

        return bytes;
    }

    /// <summary>Creates a wrapper TypeSpec that refers to an inner type.</summary>
    /// <param name="form">The assigned wrapper form.</param>
    /// <param name="innerTypeSpec">The inner TypeSpec reference.</param>
    /// <param name="qualifiers">The TypeSpec qualifier bits.</param>
    /// <returns>The encoded TypeSpec row.</returns>
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

    /// <summary>Creates an indexed-family TypeSpec that refers to an argument type.</summary>
    /// <param name="familyCode">The indexed family type code.</param>
    /// <param name="argumentTypeSpec">The argument TypeSpec reference.</param>
    /// <param name="qualifiers">The TypeSpec qualifier bits.</param>
    /// <returns>The encoded TypeSpec row.</returns>
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

    internal static byte[] FixedInlineArray(uint elementTypeSpecRef,
        ulong elementCount)
    {
        ArgumentOutOfRangeException.ThrowIfZero(elementTypeSpecRef);

        var bytes = new byte[14];
        bytes[0] = 0x11;
        Write(bytes, 2, elementTypeSpecRef);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(6),
            elementCount);
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
        int methodDefCount = 0, int predicateCaptureCount = 0,
        TrRmA3LayoutResourceLedger? resourceLedger = null)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var result = resourceLedger is null
            ? TrRmVariableTableWriter.EncodeBlobs(rows)
            : TrRmVariableTableWriter.EncodeBlobs(rows, resourceLedger);
        _ = new TrRmTypeSpecTableReader(result, rows.Count,
            typeDefCount, importCount, genericParamCount,
            nativeFunctionPointerSignatureCount, schema, methodDefCount,
            predicateCaptureCount, resourceLedger);
        return result;
    }

    private static void Write(byte[] bytes, int offset, uint value)
        => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);
}
