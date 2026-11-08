// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Terrum.CodeAnalysis.Semantics;

internal static class ValueLayout
{
    internal const int MaximumSize = 16;
    internal const int MaximumStructureSize = 8192;
    internal const int MaximumAddressStorageSize = 16 * sizeof(ulong);

    [SuppressMessage("Style", "IDE0072:Populate switch", Justification = "Unsupported values have no executable layout.")]
    internal static ulong Size(PrimitiveType type, IReadOnlyList<BoundStruct> structures,
        IReadOnlyList<CheckedReferenceTypeDefinition>? checkedReferenceTypes = null,
        string? nativeLoweringAgreement = null,
        IReadOnlyList<BoundNativeFixedArrayType>? fixedArrayTypes = null)
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
        _ when NativePointerTypes.IsType(type)
            => checked((ulong)NativeTargetProfile.PointerSize),
        PrimitiveType.ReadonlyInt32BufferView or PrimitiveType.MutableInt32BufferView => 16,
        PrimitiveType.TokenSpan
            => checked((ulong)TokenSpanContract.DescriptorSize),
        _ when NativeFixedArrayTypes.IsType(type)
            => TryGetFixedArrayLayout(type, structures, fixedArrayTypes,
                checkedReferenceTypes,
                out var fixedArrayLayout)
                ? fixedArrayLayout.ByteExtent : 0,
        PrimitiveType.NativeUtf16ZPointer or PrimitiveType.NativeUtf8ZPointer
                or PrimitiveType.NativeStorageAddress or PrimitiveType.NativeRawPointer
                    => checked((ulong)NativeTargetProfile.PointerSize),
                _ when NativeFunctionPointerTypes.IsType(type)
                    => checked((ulong)NativeTargetProfile.PointerSize),
        _ when CheckedReferenceTypes.IsType(type) => checked((ulong)CheckedReferenceSize(
            type, structures, checkedReferenceTypes, nativeLoweringAgreement,
            fixedArrayTypes)),
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
            if (CheckedReferenceTypeContract.IsOptionalValueType(
                    type, checkedReferenceTypes ?? []))
            {
                return TryGetOptionalValueLayout(type, structures,
                        checkedReferenceTypes, nativeLoweringAgreement,
                        fixedArrayTypes, out var optionalLayout)
                    ? optionalLayout.Alignment : 0;
            }

            var size = CheckedReferenceSize(type, structures,
                checkedReferenceTypes, nativeLoweringAgreement,
                fixedArrayTypes);
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
                ? 8 : checked((int)Size(type, structures,
                    checkedReferenceTypes, nativeLoweringAgreement));
        }
        if (NativeFixedArrayTypes.IsType(type))
        {
            if (fixedArrayTypes is null
                || !NativeFixedArrayContract.TryGetLayout(type, structures,
                    fixedArrayTypes, out var arrayLayout,
                    checkedReferenceTypes)
                || arrayLayout.Alignment > int.MaxValue)
            {
                throw new NotSupportedException(
                    "Fixed-array alignment requires its validated type table.");
            }

            return (int)arrayLayout.Alignment;
        }
        var structureIndex = (int)type - 100;
        if ((uint)structureIndex >= (uint)structures.Count)
        {
            return 0;
        }
        return structures[structureIndex].Alignment;
    }

    internal static int RequiredAlignment(PrimitiveType type,
        IReadOnlyList<BoundStruct> structures,
        IReadOnlyList<BoundNativeFixedArrayType>? fixedArrayTypes = null,
        IReadOnlyList<CheckedReferenceTypeDefinition>? checkedReferenceTypes = null)
    {
        if (CheckedReferenceTypes.IsType(type)
            && CheckedReferenceTypeContract.IsOptionalValueType(type,
                checkedReferenceTypes ?? []))
        {
            return checkedReferenceTypes is not null
                ? RequiredAlignment(
                    checkedReferenceTypes[CheckedReferenceTypes.Index(type)]
                        .ReferentType, structures, fixedArrayTypes,
                    checkedReferenceTypes)
                : 1;
        }
        if (NativeFixedArrayTypes.IsType(type))
        {
            if (fixedArrayTypes is null
                || !NativeFixedArrayContract.TryGetLayout(type, structures,
                    fixedArrayTypes, out var arrayLayout,
                    checkedReferenceTypes)
                || arrayLayout.RequiredAlignment > int.MaxValue)
            {
                throw new NotSupportedException(
                    "Fixed-array required alignment requires its validated type table.");
            }

            return (int)arrayLayout.RequiredAlignment;
        }
        if ((int)type < 100 || (int)type >= ManagedClassTypes.First
            || BoundEnumTypes.IsType(type)
            || NativePointerTypes.IsType(type)
            || NativeFunctionPointerTypes.IsType(type)
            || CheckedReferenceTypes.IsType(type)
            || BoundGenericNominalTypeTypes.IsType(type))
        {
            return 1;
        }
        var structureIndex = (int)type - 100;
        return (uint)structureIndex < (uint)structures.Count
            ? structures[structureIndex].RequiredAlignment
            : 1;
    }

    internal static bool TryGetOptionalValueLayout(PrimitiveType type,
        IReadOnlyList<BoundStruct> structures,
        IReadOnlyList<CheckedReferenceTypeDefinition>? checkedReferenceTypes,
        string? nativeLoweringAgreement,
        IReadOnlyList<BoundNativeFixedArrayType>? fixedArrayTypes,
        out OptionalValueLayout layout)
    {
        layout = default;
        if (checkedReferenceTypes is null
            || !CheckedReferenceTypeContract.IsOptionalValueType(type,
                checkedReferenceTypes))
        {
            return false;
        }

        var payloadType
            = checkedReferenceTypes[CheckedReferenceTypes.Index(type)]
                .ReferentType;
        if (!IsRootlessCopyPayload(payloadType, structures,
                checkedReferenceTypes, fixedArrayTypes,
                new byte[structures.Count], 0))
        {
            return false;
        }

        var payloadSize = Size(payloadType, structures,
            checkedReferenceTypes, nativeLoweringAgreement, fixedArrayTypes);
        var payloadAlignment = Alignment(payloadType, structures,
            checkedReferenceTypes, nativeLoweringAgreement, fixedArrayTypes);
        var requiredAlignment = RequiredAlignment(payloadType, structures,
            fixedArrayTypes, checkedReferenceTypes);

        var alignment = Math.Max(1, payloadAlignment);
        var required = Math.Max(1, requiredAlignment);
        var isNullNiche = payloadType == PrimitiveType.NativeRawPointer
            || NativePointerTypes.IsType(payloadType);
        if (isNullNiche)
        {
            layout = new OptionalValueLayout(payloadType, 0, payloadSize,
                alignment, required, UsesNullNiche: true);
            return payloadSize != 0;
        }

        var strideAlignment = Math.Max(alignment, required);
        var payloadOffset = RoundUp(1, strideAlignment);
        var size = RoundUp(checked(payloadOffset + payloadSize),
            strideAlignment);
        layout = new OptionalValueLayout(payloadType, payloadOffset, size,
            alignment, required, UsesNullNiche: false);
        return true;
    }

    private static bool IsRootlessCopyPayload(PrimitiveType type,
        IReadOnlyList<BoundStruct> structures,
        IReadOnlyList<CheckedReferenceTypeDefinition> checkedReferenceTypes,
        IReadOnlyList<BoundNativeFixedArrayType>? fixedArrayTypes,
        byte[] structureStates, int depth)
    {
        if (depth > SemanticVerifier.MaximumDepth
            || type == PrimitiveType.TokenSpan
            || ManagedClassTypes.IsManaged(type)
            || BoundGenericNominalTypeTypes.IsType(type))
        {
            return false;
        }
        if (CheckedReferenceTypes.IsType(type))
        {
            return CheckedReferenceTypeContract.IsOptionalValueType(type,
                    checkedReferenceTypes)
                && IsRootlessCopyPayload(
                    checkedReferenceTypes[CheckedReferenceTypes.Index(type)]
                        .ReferentType,
                    structures, checkedReferenceTypes, fixedArrayTypes,
                    structureStates, depth + 1);
        }
        if (type == PrimitiveType.Void
            || PrimitiveTypeFacts.ScalarWidth(type) > 0
            || type is PrimitiveType.NativeRawPointer
            || NativePointerTypes.IsType(type)
            || NativeFunctionPointerTypes.IsType(type)
            || BoundEnumTypes.IsType(type))
        {
            return true;
        }
        if (NativeFixedArrayTypes.IsType(type))
        {
            return TryGetFixedArrayLayout(type, structures, fixedArrayTypes,
                    checkedReferenceTypes, out _)
                && fixedArrayTypes is not null
                && IsRootlessCopyPayload(
                    fixedArrayTypes[NativeFixedArrayTypes.Index(type)]
                        .ElementType,
                    structures, checkedReferenceTypes, fixedArrayTypes,
                    structureStates,
                    depth + 1);
        }

        var structureIndex = (int)type - 100;
        if (structureIndex < 0
            || structureIndex >= structures.Count
            || structureIndex >= BoundEnumTypes.First - 100)
        {
            return false;
        }
        if (structureStates[structureIndex] == 1)
        {
            return false;
        }
        if (structureStates[structureIndex] == 2)
        {
            return true;
        }

        var structure = structures[structureIndex];
        if (structure.IsUnique || structure.CleanupMethod is not null)
        {
            return false;
        }

        structureStates[structureIndex] = 1;
        foreach (var field in structure.Fields)
        {
            if (!IsRootlessCopyPayload(field.Type, structures,
                    checkedReferenceTypes, fixedArrayTypes,
                    structureStates, depth + 1))
            {
                return false;
            }
        }
        structureStates[structureIndex] = 2;
        return true;
    }

    private static bool TryGetFixedArrayLayout(PrimitiveType type,
        IReadOnlyList<BoundStruct> structures,
        IReadOnlyList<BoundNativeFixedArrayType>? fixedArrayTypes,
        IReadOnlyList<CheckedReferenceTypeDefinition>?
            checkedReferenceTypes,
        out BoundNativeFixedArrayLayout layout)
    {
        if (fixedArrayTypes is not null
            && NativeFixedArrayTypes.IsType(type)
            && (uint)NativeFixedArrayTypes.Index(type)
                < (uint)fixedArrayTypes.Count)
        {
            var array = fixedArrayTypes[NativeFixedArrayTypes.Index(type)];
            return NativeFixedArrayContract.TryGetLayout(type,
                array.ElementType, array.ElementCount, structures,
                fixedArrayTypes, out layout, checkedReferenceTypes);
        }

        layout = default;
        return false;
    }

    private static ulong RoundUp(ulong value, int alignment)
    {
        if (alignment <= 0 || (alignment & (alignment - 1)) != 0)
        {
            throw new NotSupportedException(
                "Optional payload alignment is not a supported power of two.");
        }

        var mask = checked((ulong)alignment - 1);
        return checked(value + mask) & ~mask;
    }

    private static ulong CheckedReferenceSize(PrimitiveType type,
        IReadOnlyList<BoundStruct> structures,
        IReadOnlyList<CheckedReferenceTypeDefinition>? checkedReferenceTypes,
        string? nativeLoweringAgreement,
        IReadOnlyList<BoundNativeFixedArrayType>? fixedArrayTypes)
    {
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
        if (referenceType is null)
        {
            return 0;
        }
        if (referenceType.IsOptionalValue)
        {
            return TryGetOptionalValueLayout(type, structures,
                checkedReferenceTypes, nativeLoweringAgreement,
                fixedArrayTypes, out var optionalLayout)
                    ? optionalLayout.Size : 0;
        }
        if (nativeLoweringAgreement is not
            (null or CompilationOptions.WindowsX64NormalBackingAgreement
                or CompilationOptions.WindowsX64RootlessReferencesAgreement))
        {
            throw new NotSupportedException(
                "The selected native lowering agreement has no value layout.");
        }
        return !referenceType.IsGarbageCollected
            && !referenceType.IsInitialization
            ? nativeLoweringAgreement
                == CompilationOptions.WindowsX64NormalBackingAgreement
                    ? 2UL * sizeof(ulong)
                    : checked((ulong)NativeTargetProfile.PointerSize)
            : 0;
    }
}

internal readonly record struct OptionalValueLayout(
    PrimitiveType PayloadType,
    ulong PayloadOffset,
    ulong Size,
    int Alignment,
    int RequiredAlignment,
    bool UsesNullNiche);
