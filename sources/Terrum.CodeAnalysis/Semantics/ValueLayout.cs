// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Terrum.CodeAnalysis.Semantics;

internal static class ValueLayout
{
    internal const int MaximumSize = 16;
    // Address-based storage can exceed the value profile while remaining bounded to sixteen 64-bit fields.
    internal const int MaximumStructureSize = 16 * sizeof(ulong);

    [SuppressMessage("Style", "IDE0072:Populate switch", Justification = "Unsupported values have no executable layout.")]
    internal static int Size(PrimitiveType type, IReadOnlyList<BoundStruct> structures,
        IReadOnlyList<CheckedReferenceTypeDefinition>? checkedReferenceTypes = null,
        string? nativeLoweringAgreement = null)
    {
        if (BoundGenericNominalTypeTypes.IsType(type))
        {
            throw new NotSupportedException(
                "Generic nominal types do not have a supported value layout.");
        }

        return type switch {
        PrimitiveType.Bool or PrimitiveType.Char8 or PrimitiveType.Int8 or PrimitiveType.UInt8 => 1,
        PrimitiveType.Char16 or PrimitiveType.Int16 or PrimitiveType.UInt16 => 2,
        PrimitiveType.Char32 or PrimitiveType.Rune or PrimitiveType.Int32 or PrimitiveType.UInt32
            or PrimitiveType.Float32 => 4,
        PrimitiveType.Int64 or PrimitiveType.UInt64 or PrimitiveType.Float64 or PrimitiveType.Decimal64
            or PrimitiveType.OwnedInt32Buffer => 8,
        PrimitiveType.Int128 or PrimitiveType.UInt128 or PrimitiveType.Float128 or PrimitiveType.Decimal128 => 16,
        PrimitiveType.Float16 or PrimitiveType.BFloat16 => 2,
        PrimitiveType.Decimal32 => 4,
        _ when ManagedClassTypes.IsManaged(type) => 8,
        _ when NativePointerTypes.IsType(type) => NativeTargetProfile.PointerSize,
        PrimitiveType.ReadonlyInt32BufferView or PrimitiveType.MutableInt32BufferView => 16,
        PrimitiveType.TokenSpan => TokenSpanContract.DescriptorSize,
        PrimitiveType.NativeUtf16ZPointer or PrimitiveType.NativeUtf8ZPointer
                or PrimitiveType.NativeStorageAddress or PrimitiveType.NativeRawPointer
                    => NativeTargetProfile.PointerSize,
                _ when NativeFunctionPointerTypes.IsType(type) => NativeTargetProfile.PointerSize,
        _ when CheckedReferenceTypes.IsType(type) => CheckedReferenceSize(
            type, checkedReferenceTypes, nativeLoweringAgreement),
        PrimitiveType.Void => 0,
        _ when BoundEnumTypes.IsType(type) => sizeof(int),
        _ when (int)type >= 100 && (int)type < ManagedClassTypes.First
            && (int)type - 100 < structures.Count => structures[(int)type - 100].Size,
        _ => 0,
        };
    }

    // Callers have established that all nested layouts are acyclic and complete.
    internal static int Alignment(PrimitiveType type, IReadOnlyList<BoundStruct> structures,
        IReadOnlyList<CheckedReferenceTypeDefinition>? checkedReferenceTypes = null,
        string? nativeLoweringAgreement = null,
        IReadOnlyList<BoundNativeFixedArrayType>? fixedArrayTypes = null)
    {
        if (BoundGenericNominalTypeTypes.IsType(type))
        {
            throw new NotSupportedException(
                "Generic nominal types do not have a supported value layout.");
        }

        if (NativePointerTypes.IsType(type) || NativeFunctionPointerTypes.IsType(type))
        {
            return NativeTargetProfile.PointerSize;
        }
        if (CheckedReferenceTypes.IsType(type))
        {
            var size = CheckedReferenceSize(type, checkedReferenceTypes,
                nativeLoweringAgreement);
            return size == 0 ? 0 : nativeLoweringAgreement is null
                ? NativeTargetProfile.PointerSize : sizeof(ulong);
        }
        if (ManagedClassTypes.IsManaged(type))
        {
            return 8;
        }
        if (BoundEnumTypes.IsType(type))
        {
            return sizeof(int);
        }
        if ((int)type < 100)
        {
            if (type is PrimitiveType.TokenSpan or PrimitiveType.NativeUtf16ZPointer or PrimitiveType.NativeUtf8ZPointer
                or PrimitiveType.NativeStorageAddress or PrimitiveType.NativeRawPointer)
            {
                return NativeTargetProfile.PointerSize;
            }
            return type is PrimitiveType.ReadonlyInt32BufferView or PrimitiveType.MutableInt32BufferView
                ? 8 : Size(type, structures, checkedReferenceTypes,
                    nativeLoweringAgreement);
        }
        if (NativeFixedArrayTypes.IsType(type))
        {
            if (fixedArrayTypes is null
                || !NativeFixedArrayContract.TryGetLayout(type, structures,
                    fixedArrayTypes, out var arrayLayout)
                || arrayLayout.Alignment > int.MaxValue)
            {
                throw new NotSupportedException(
                    "Fixed-array alignment requires its validated type table.");
            }

            return (int)arrayLayout.Alignment;
        }
        var structure = structures[(int)type - 100];
        var alignment = 1;
        foreach (var field in structure.Fields)
        {
            alignment = Math.Max(alignment, Alignment(field.Type, structures,
                checkedReferenceTypes, nativeLoweringAgreement,
                fixedArrayTypes));
        }
        return alignment;
    }

    private static int CheckedReferenceSize(PrimitiveType type,
        IReadOnlyList<CheckedReferenceTypeDefinition>? checkedReferenceTypes,
        string? nativeLoweringAgreement)
    {
        if (nativeLoweringAgreement is not
            (null or CompilationOptions.WindowsX64NormalBackingAgreement))
        {
            throw new NotSupportedException(
                "The selected native lowering agreement has no value layout.");
        }
        if (checkedReferenceTypes is null)
        {
            return 0;
        }

        var index = CheckedReferenceTypes.Index(type);
        if ((uint)index >= (uint)checkedReferenceTypes.Count)
        {
            return 0;
        }
        var referenceType = checkedReferenceTypes[index];
        return referenceType is not null && !referenceType.IsGarbageCollected
            && !referenceType.IsInitialization
            ? nativeLoweringAgreement is null
                ? NativeTargetProfile.PointerSize : 2 * sizeof(ulong)
            : 0;
    }
}
