// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Generic;

namespace Terrum.CodeAnalysis.Semantics;

internal static class NativeFixedArrayContract
{
    // Bound inline locals separately from the much smaller aggregate-layout limit.
    internal const int MaximumByteExtent = 4096;

    internal static bool IsCarrier(PrimitiveType type) => type == PrimitiveType.NativeFixedArray;

    internal static bool SupportsRuntimePolicy(NativeImportCatalog? catalog,
        BoundRuntimePolicy runtimePolicy) =>
        catalog is not null
        && runtimePolicy is BoundRuntimePolicy.FailFast or BoundRuntimePolicy.CheckedExceptions;

    internal static bool MatchesProgram(BoundProgram program) =>
        SupportsRuntimePolicy(program.NativeImportCatalog, program.RuntimePolicy);

    internal static bool IsSupportedElementType(PrimitiveType type,
        IReadOnlyList<BoundStruct> structures, NativeImportCatalog? catalog) =>
        IsSupportedElement(type, structures, catalog, new HashSet<int>());

    internal static bool TryGetLayout(PrimitiveType carrierType, PrimitiveType elementType, int elementCount,
        IReadOnlyList<BoundStruct> structures, NativeImportCatalog? catalog,
        out BoundNativeFixedArrayLayout layout)
    {
        if (IsCarrier(carrierType) && elementCount > 0)
        {
            var isSupportedElement = IsSupportedElementType(elementType, structures, catalog);
            if (!isSupportedElement)
            {
                layout = default;
                return false;
            }
            var elementStride = ValueLayout.Size(elementType, structures);
            var alignment = ValueLayout.Alignment(elementType, structures, catalog);
            if (elementStride <= 0 || alignment <= 0 || elementStride % alignment != 0
                || elementCount > MaximumByteExtent / elementStride)
            {
                layout = default;
                return false;
            }
            var candidate = new BoundNativeFixedArrayLayout(carrierType, elementType, elementCount,
                elementStride, elementStride * elementCount, alignment);
            layout = candidate;
            return true;
        }
        layout = default;
        return false;
    }

    private static bool IsSupportedElement(PrimitiveType type,
        IReadOnlyList<BoundStruct> structures, NativeImportCatalog? catalog, HashSet<int> visiting)
    {
        if (type is PrimitiveType.Bool or PrimitiveType.Char16
            or PrimitiveType.Int32 or PrimitiveType.UInt32
            or PrimitiveType.Int64 or PrimitiveType.UInt64
            or PrimitiveType.Float32 or PrimitiveType.Float64)
        {
            return true;
        }

        var index = (int)type - 100;
        if (index < 0 || index >= structures.Count || !visiting.Add(index))
        {
            return false;
        }

        var structure = structures[index];
        var valid = structure.BlittableTargetRequirement == FixedLayoutStorage.WindowsX64TargetRequirement
            && (structure.IsSequential || structure.IsTransparent)
            && !structure.IsUnique && structure.CleanupMethod is null
            && !structure.Fields.IsDefaultOrEmpty;
        if (valid)
        {
            var offset = 0;
            var alignment = 0;
            foreach (var field in structure.Fields)
            {
                if (field.RequiresUnsafeCaller
                    || !IsSupportedElement(field.Type, structures, catalog, visiting))
                {
                    valid = false;
                    break;
                }
                var fieldSize = ValueLayout.Size(field.Type, structures);
                var fieldAlignment = ValueLayout.Alignment(field.Type, structures, catalog);
                if (fieldSize <= 0 || fieldAlignment <= 0
                    || fieldAlignment > ValueLayout.MaximumStructureSize)
                {
                    valid = false;
                    break;
                }
                var alignedOffset = (offset + fieldAlignment - 1) & -fieldAlignment;
                if (field.Offset != alignedOffset || field.Size != fieldSize
                    || alignedOffset > ValueLayout.MaximumStructureSize - fieldSize)
                {
                    valid = false;
                    break;
                }
                offset = alignedOffset + fieldSize;
                alignment = Math.Max(alignment, fieldAlignment);
            }
            valid &= alignment > 0
                && ((offset + alignment - 1) & -alignment) == structure.Size;
        }
        _ = visiting.Remove(index);
        return valid;
    }

    internal static bool MatchesLayout(BoundNativeFixedArrayLayout layout,
        IReadOnlyList<BoundStruct> structures, NativeImportCatalog? catalog) =>
        TryGetLayout(layout.CarrierType, layout.ElementType, layout.ElementCount,
            structures, catalog, out var expected)
            && layout == expected;

    internal static bool AllowsEmptyInitialization(BoundNativeFixedArrayLayout layout,
        IReadOnlyList<BoundStruct> structures, NativeImportCatalog? catalog) =>
        IsScalar(layout.ElementType);

    internal static bool AllowsMutableLocal(BoundNativeFixedArrayLayout layout,
        IReadOnlyList<BoundStruct> structures, NativeImportCatalog? catalog) =>
        IsCarrier(layout.CarrierType);

    internal static bool AllowsElementWrites(BoundNativeFixedArrayLayout layout,
        IReadOnlyList<BoundStruct> structures, NativeImportCatalog? catalog) =>
        IsCarrier(layout.CarrierType);

    internal static bool AllowsUInt32Indices(BoundNativeFixedArrayLayout layout,
        IReadOnlyList<BoundStruct> structures, NativeImportCatalog? catalog) =>
        IsCarrier(layout.CarrierType);

    private static bool IsScalar(PrimitiveType type) =>
        type is PrimitiveType.Bool or PrimitiveType.Char16
            or PrimitiveType.Int32 or PrimitiveType.UInt32
            or PrimitiveType.Int64 or PrimitiveType.UInt64
            or PrimitiveType.Float32 or PrimitiveType.Float64;
}
