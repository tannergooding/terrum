// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace Terrum.CodeAnalysis.Semantics;

internal static class NativeFixedArrayContract
{
    // Cap catalogued inline locals separately from the much smaller aggregate-layout limit.
    internal const int MaximumByteExtent = 4096;

    internal static bool IsCarrier(PrimitiveType type) => type == PrimitiveType.NativeFixedArray;

    internal static bool MatchesCatalog(NativeImportCatalog? catalog) =>
        catalog?.SupportsNativeFixedArrayStorage == true;

    internal static bool MatchesProgram(BoundProgram program) =>
        (MatchesCatalog(program.NativeImportCatalog)
            && program.RuntimePolicy == BoundRuntimePolicy.FailFast)
        || NativeWindowsModulePathContract.MatchesPathStorageProgram(program);

    internal static bool TryGetLayout(PrimitiveType carrierType, PrimitiveType elementType, int elementCount,
        IReadOnlyList<BoundStruct> structures, NativeImportCatalog? catalog,
        out BoundNativeFixedArrayLayout layout)
    {
        if (IsCarrier(carrierType) && elementCount > 0)
        {
            var isSupportedElement =
                IsSupportedCataloguedElement(elementType, structures, catalog, new HashSet<int>());
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
            if (NativeStorageParameterContract.SupportsFixedArrayParameter(catalog, candidate, structures,
                PrimitiveType.NativeStorageAddress, isReadonly: true))
            {
                layout = candidate;
                return true;
            }
        }
        layout = default;
        return false;
    }

    internal static bool IsCataloguedElement(PrimitiveType type, IReadOnlyList<BoundStruct> structures,
        NativeImportCatalog? catalog)
    {
        var typeName = ElementTypeName(type, structures);
        return typeName is not null
            && IsSupportedCataloguedElement(type, structures, catalog, new HashSet<int>())
            && catalog?.SupportsFixedArrayElement(typeName, ValueLayout.Size(type, structures),
                ValueLayout.Alignment(type, structures, catalog)) == true;
    }

    private static bool IsSupportedCataloguedElement(PrimitiveType type,
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
        var valid = structure.IsSequential && !structure.IsTransparent && !structure.IsUnique
            && structure.CleanupMethod is null && !structure.Fields.IsDefaultOrEmpty;
        if (valid)
        {
            var offset = 0;
            var alignment = 0;
            foreach (var field in structure.Fields)
            {
                if (field.RequiresUnsafeCaller
                    || !IsSupportedCataloguedElement(field.Type, structures, catalog, visiting))
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
        catalog?.AllowsEmptyFixedArrayInitialization(layout, structures) == true;

    internal static bool AllowsMutableLocal(BoundNativeFixedArrayLayout layout,
        IReadOnlyList<BoundStruct> structures, NativeImportCatalog? catalog) =>
        catalog?.AllowsMutableFixedArrayLocal(layout, structures) == true;

    internal static bool AllowsElementWrites(BoundNativeFixedArrayLayout layout,
        IReadOnlyList<BoundStruct> structures, NativeImportCatalog? catalog) =>
        catalog?.AllowsFixedArrayElementWrites(layout, structures) == true;

    internal static bool AllowsUInt32Indices(BoundNativeFixedArrayLayout layout,
        IReadOnlyList<BoundStruct> structures, NativeImportCatalog? catalog) =>
        catalog?.AllowsFixedArrayUInt32Indices(layout, structures) == true;

    internal static bool AllowsCheckedRuntimeAccess(BoundNativeFixedArrayLayout layout,
        IReadOnlyList<BoundStruct> structures, NativeImportCatalog? catalog) =>
        catalog?.AllowsCheckedFixedArrayAccess(layout, structures) == true;

    internal static bool AllowsReadonlyUtf16ZLoan(BoundNativeFixedArrayLayout layout,
        IReadOnlyList<BoundStruct> structures, NativeImportCatalog? catalog) =>
        catalog?.AllowsReadonlyUtf16ZFixedArrayLoan(layout, structures) == true;

    internal static bool IsValidElementFields(ImmutableArray<NativeFixedArrayFieldLayoutDefinition> fields,
        int elementStride)
    {
        if (fields.IsDefaultOrEmpty)
        {
            return true;
        }

        var names = new HashSet<string>(StringComparer.Ordinal);
        var end = 0;
        foreach (var field in fields)
        {
            if (field is null || string.IsNullOrWhiteSpace(field.Name)
                || string.IsNullOrWhiteSpace(field.TypeName) || !names.Add(field.Name)
                || field.Offset < end || field.Size <= 0
                || field.Offset > elementStride - field.Size)
            {
                return false;
            }
            end = field.Offset + field.Size;
        }
        return true;
    }

    internal static bool MatchesElementFields(PrimitiveType type, IReadOnlyList<BoundStruct> structures,
        ImmutableArray<NativeFixedArrayFieldLayoutDefinition> expectedFields)
    {
        if (expectedFields.IsDefaultOrEmpty)
        {
            return true;
        }

        var index = (int)type - 100;
        if (index < 0 || index >= structures.Count)
        {
            return false;
        }

        var actualFields = structures[index].Fields;
        if (actualFields.IsDefault || actualFields.Length != expectedFields.Length)
        {
            return false;
        }

        for (var fieldIndex = 0; fieldIndex < actualFields.Length; fieldIndex++)
        {
            var actual = actualFields[fieldIndex];
            var expected = expectedFields[fieldIndex];
            if (expected is null || actual.Name != expected.Name
                || ElementTypeName(actual.Type, structures) != expected.TypeName
                || actual.Offset != expected.Offset || actual.Size != expected.Size
                || (expected.IsMutable is bool isMutable && actual.IsMutable != isMutable)
                || (expected.IsPrivate is bool isPrivate && actual.IsPrivate != isPrivate)
                || (expected.IsInternal is bool isInternal && actual.IsInternal != isInternal)
                || (expected.RequiresUnsafeCaller is bool requiresUnsafeCaller
                    && actual.RequiresUnsafeCaller != requiresUnsafeCaller))
            {
                return false;
            }
        }
        return true;
    }

    internal static string? ElementTypeName(PrimitiveType type, IReadOnlyList<BoundStruct> structures)
    {
        var typeIndex = (int)type - 100;
        if (typeIndex >= 0)
        {
            return typeIndex < structures.Count ? structures[typeIndex].Name : null;
        }
        return (int)type < 100 ? type.ToString() : null;
    }
}
