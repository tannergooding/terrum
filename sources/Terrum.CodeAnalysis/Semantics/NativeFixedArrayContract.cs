// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Numerics;

namespace Terrum.CodeAnalysis.Semantics;

internal static class NativeFixedArrayContract
{
    private readonly record struct TypeLayout(
        ulong Size, ulong Alignment, ulong RequiredAlignment);

    private readonly record struct ArrayLayout(
        ulong ElementStride, ulong ByteExtent, ulong Alignment,
        ulong RequiredAlignment);

    internal static bool IsCarrier(PrimitiveType type)
        => NativeFixedArrayTypes.IsType(type);

    internal static bool SupportsRuntimePolicy(BoundRuntimePolicy runtimePolicy)
        => runtimePolicy is BoundRuntimePolicy.FailFast
            or BoundRuntimePolicy.CheckedExceptions;

    internal static bool MatchesProgram(BoundProgram program)
    {
        var fixedArrays = program.NativeFixedArrayTypes;
        if (!SupportsRuntimePolicy(program.RuntimePolicy)
            || fixedArrays.IsDefault
            || !IsValidTable(program.Structures, fixedArrays))
        {
            return false;
        }

        foreach (var fixedArray in fixedArrays)
        {
            if ((NativePointerTypes.IsType(fixedArray.ElementType)
                    && !NativePointerTypeContract.IsValidType(
                        fixedArray.ElementType, program.NativePointerTypes))
                || (NativeFunctionPointerTypes.IsType(fixedArray.ElementType)
                    && !NativeFunctionPointerContract.IsValidType(
                        fixedArray.ElementType,
                        program.NativeFunctionPointerSignatures)))
            {
                return false;
            }
        }

        return true;
    }

    internal static bool IsValidTable(
        IReadOnlyList<BoundStruct> structures,
        IReadOnlyList<BoundNativeFixedArrayType> fixedArrays)
    {
        if (fixedArrays.Count is > SemanticVerifier.MaximumAnalysisVisits
            or > NativeFixedArrayTypes.MaximumCount)
        {
            return false;
        }

        var identities = new HashSet<(PrimitiveType ElementType,
            ulong ElementCount)>();
        for (var index = 0; index < fixedArrays.Count; index++)
        {
            var definition = fixedArrays[index];
            if (!identities.Add((definition.ElementType,
                    definition.ElementCount))
                || !TryComputeLayout(definition.ElementType,
                    definition.ElementCount, structures, fixedArrays,
                    out var expected)
                || expected.ElementStride != definition.ElementStride
                || expected.ByteExtent != definition.ByteExtent
                || expected.Alignment != definition.Alignment
                || expected.RequiredAlignment
                    != definition.RequiredAlignment)
            {
                return false;
            }
        }

        return true;
    }

    internal static bool IsSupportedElementType(PrimitiveType type,
        IReadOnlyList<BoundStruct> structures,
        IReadOnlyList<BoundNativeFixedArrayType> fixedArrays)
        => IsSupportedElement(type, structures, fixedArrays, []);

    internal static bool TryCreateType(PrimitiveType elementType,
        ulong elementCount, IReadOnlyList<BoundStruct> structures,
        List<BoundNativeFixedArrayType> fixedArrays,
        out PrimitiveType arrayType, out BoundNativeFixedArrayLayout layout)
    {
        arrayType = PrimitiveType.Int32;
        layout = default;
        if (!TryComputeLayout(elementType, elementCount, structures,
                fixedArrays, out var geometry))
        {
            return false;
        }

        for (var index = 0; index < fixedArrays.Count; index++)
        {
            var existing = fixedArrays[index];
            if (existing.ElementType != elementType
                || existing.ElementCount != elementCount)
            {
                continue;
            }

            arrayType = NativeFixedArrayTypes.Type(index);
            layout = CreateLayout(arrayType, elementType, elementCount,
                geometry);
            return existing.ElementStride == geometry.ElementStride
                && existing.ByteExtent == geometry.ByteExtent
                && existing.Alignment == geometry.Alignment
                && existing.RequiredAlignment
                    == geometry.RequiredAlignment;
        }

        if (fixedArrays.Count is >= SemanticVerifier.MaximumAnalysisVisits
            or >= NativeFixedArrayTypes.MaximumCount)
        {
            return false;
        }

        arrayType = NativeFixedArrayTypes.Type(fixedArrays.Count);
        fixedArrays.Add(new BoundNativeFixedArrayType(elementType,
            elementCount, geometry.ElementStride, geometry.ByteExtent,
            geometry.Alignment, geometry.RequiredAlignment));
        layout = CreateLayout(arrayType, elementType, elementCount, geometry);
        return true;
    }

    internal static bool TryGetLayout(PrimitiveType arrayType,
        PrimitiveType elementType, ulong elementCount,
        IReadOnlyList<BoundStruct> structures,
        IReadOnlyList<BoundNativeFixedArrayType> fixedArrays,
        out BoundNativeFixedArrayLayout layout)
    {
        layout = default;
        if (!NativeFixedArrayTypes.IsType(arrayType))
        {
            return false;
        }

        var index = NativeFixedArrayTypes.Index(arrayType);
        if ((uint)index >= (uint)fixedArrays.Count)
        {
            return false;
        }

        var definition = fixedArrays[index];
        if (definition.ElementType != elementType
            || definition.ElementCount != elementCount
            || !TryComputeLayout(elementType, elementCount, structures,
                fixedArrays, out var geometry)
            || definition.ElementStride != geometry.ElementStride
            || definition.ByteExtent != geometry.ByteExtent
            || definition.Alignment != geometry.Alignment
            || definition.RequiredAlignment != geometry.RequiredAlignment)
        {
            return false;
        }

        layout = CreateLayout(arrayType, elementType, elementCount, geometry);
        return true;
    }

    internal static bool TryGetLayout(PrimitiveType arrayType,
        IReadOnlyList<BoundStruct> structures,
        IReadOnlyList<BoundNativeFixedArrayType> fixedArrays,
        out BoundNativeFixedArrayLayout layout)
    {
        layout = default;
        if (!NativeFixedArrayTypes.IsType(arrayType))
        {
            return false;
        }

        var index = NativeFixedArrayTypes.Index(arrayType);
        if ((uint)index >= (uint)fixedArrays.Count)
        {
            return false;
        }

        var definition = fixedArrays[index];
        return TryGetLayout(arrayType, definition.ElementType,
            definition.ElementCount, structures, fixedArrays, out layout);
    }

    internal static bool MatchesType(PrimitiveType arrayType,
        BoundNativeFixedArrayLayout layout,
        IReadOnlyList<BoundStruct> structures,
        IReadOnlyList<BoundNativeFixedArrayType> fixedArrays)
        => arrayType == layout.CarrierType
            && TryGetLayout(arrayType, structures, fixedArrays,
                out var expected)
            && expected == layout;

    internal static bool MatchesType(PrimitiveType arrayType,
        BoundNativeFixedArrayLayout layout, BoundProgram program)
        => MatchesType(arrayType, layout, program.Structures,
            program.NativeFixedArrayTypes);

    internal static bool MatchesLayout(BoundNativeFixedArrayLayout layout,
        IReadOnlyList<BoundStruct> structures,
        IReadOnlyList<BoundNativeFixedArrayType> fixedArrays)
        => TryGetLayout(layout.CarrierType, layout.ElementType,
                layout.ElementCount, structures, fixedArrays,
                out var expected)
            && expected == layout;

    internal static bool MatchesLayout(BoundNativeFixedArrayLayout layout,
        BoundProgram program)
        => MatchesProgram(program)
            && MatchesLayout(layout, program.Structures,
                program.NativeFixedArrayTypes);

    internal static bool IsRevision100RuntimeArrayLayout(
        BoundNativeFixedArrayLayout layout,
        IReadOnlyList<BoundStruct> structures,
        IReadOnlyList<BoundNativeFixedArrayType> fixedArrays,
        IReadOnlyList<NativePointerTypeDefinition> pointerTypes,
        IReadOnlyList<NativeFunctionPointerSignatureDefinition>
            functionPointers)
    {
        return MatchesLayout(layout, structures, fixedArrays)
            && (layout.ElementType == PrimitiveType.Void
                || (layout.ElementStride != 0
                    && !HasMetadataOnlyArrayType(layout.ElementType,
                        structures, fixedArrays, pointerTypes,
                        functionPointers, Array.Empty<BoundClass>(), [], 0,
                        SemanticVerifier.MaximumAnalysisVisits)));
    }

    internal static bool HasMetadataOnlyArrayType(
        PrimitiveType type,
        IReadOnlyList<BoundStruct> structures,
        IReadOnlyList<BoundNativeFixedArrayType> fixedArrays,
        IReadOnlyList<NativePointerTypeDefinition> pointerTypes,
        IReadOnlyList<NativeFunctionPointerSignatureDefinition>
            functionPointers)
        => HasMetadataOnlyArrayType(type, structures, fixedArrays,
            pointerTypes, functionPointers, null, [], 0,
            SemanticVerifier.MaximumAnalysisVisits);

    internal static bool HasMetadataOnlyArrayType(
        PrimitiveType type,
        IReadOnlyList<BoundStruct> structures,
        IReadOnlyList<BoundNativeFixedArrayType> fixedArrays,
        IReadOnlyList<NativePointerTypeDefinition> pointerTypes,
        IReadOnlyList<NativeFunctionPointerSignatureDefinition>
            functionPointers,
        IReadOnlyList<BoundClass> classes)
        => HasMetadataOnlyArrayType(type, structures, fixedArrays,
            pointerTypes, functionPointers, classes, [], 0,
            SemanticVerifier.MaximumAnalysisVisits);

    internal static bool HasMetadataOnlyArrayClassFields(
        IReadOnlyList<BoundClass> classes,
        IReadOnlyList<BoundStruct> structures,
        IReadOnlyList<BoundNativeFixedArrayType> fixedArrays,
        IReadOnlyList<NativePointerTypeDefinition> pointerTypes,
        IReadOnlyList<NativeFunctionPointerSignatureDefinition>
            functionPointers)
    {
        foreach (var classType in classes)
        {
            if (classType is null || classType.Fields.IsDefault)
            {
                return true;
            }
            foreach (var field in classType.Fields)
            {
                if (field is null
                    || HasMetadataOnlyArrayType(field.Type, structures,
                        fixedArrays, pointerTypes, functionPointers, classes))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool HasMetadataOnlyArrayType(
        PrimitiveType type,
        IReadOnlyList<BoundStruct> structures,
        IReadOnlyList<BoundNativeFixedArrayType> fixedArrays,
        IReadOnlyList<NativePointerTypeDefinition> pointerTypes,
        IReadOnlyList<NativeFunctionPointerSignatureDefinition>
            functionPointers,
        IReadOnlyList<BoundClass>? classes,
        HashSet<PrimitiveType> visiting,
        int depth,
        int remaining)
    {
        if (depth > SemanticVerifier.MaximumDepth || remaining <= 0)
        {
            return true;
        }
        if (NativeFixedArrayTypes.IsType(type))
        {
            return true;
        }
        if (NativePointerTypes.IsType(type))
        {
            var index = NativePointerTypes.Index(type);
            if ((uint)index >= (uint)pointerTypes.Count)
            {
                return true;
            }
            if (!visiting.Add(type))
            {
                return false;
            }
            try
            {
                return HasMetadataOnlyArrayType(
                    pointerTypes[index].PointeeType, structures,
                    fixedArrays, pointerTypes, functionPointers, classes, visiting,
                    depth + 1, remaining - 1);
            }
            finally
            {
                _ = visiting.Remove(type);
            }
        }
        if (NativeFunctionPointerTypes.IsType(type))
        {
            var index = NativeFunctionPointerTypes.Index(type);
            if ((uint)index >= (uint)functionPointers.Count)
            {
                return true;
            }
            if (!visiting.Add(type))
            {
                return false;
            }
            try
            {
                var signature = functionPointers[index];
                if (HasMetadataOnlyArrayType(signature.ReturnType,
                        structures, fixedArrays, pointerTypes,
                        functionPointers, classes, visiting, depth + 1,
                        remaining - 1))
                {
                    return true;
                }
                foreach (var parameterType in signature.ParameterTypes)
                {
                    if (HasMetadataOnlyArrayType(parameterType, structures,
                            fixedArrays, pointerTypes, functionPointers, classes,
                            visiting, depth + 1, remaining - 1))
                    {
                        return true;
                    }
                }
                return false;
            }
            finally
            {
                _ = visiting.Remove(type);
            }
        }

        var classType = ManagedClassTypes.IsOptional(type)
            ? ManagedClassTypes.Required(type) : type;
        if (ManagedClassTypes.IsClass(classType))
        {
            if (classes is null)
            {
                return false;
            }

            var classIndex = ManagedClassTypes.ClassIndex(classType);
            if ((uint)classIndex >= (uint)classes.Count)
            {
                return true;
            }

            var identity = ManagedClassTypes.Readonly(classType);
            if (!visiting.Add(identity))
            {
                return false;
            }
            try
            {
                var classDefinition = classes[classIndex];
                if (classDefinition is null || classDefinition.Fields.IsDefault)
                {
                    return true;
                }
                foreach (var field in classDefinition.Fields)
                {
                    if (field is null
                        || HasMetadataOnlyArrayType(field.Type, structures,
                            fixedArrays, pointerTypes, functionPointers,
                            classes, visiting, depth + 1, remaining - 1))
                    {
                        return true;
                    }
                }
                return false;
            }
            finally
            {
                _ = visiting.Remove(identity);
            }
        }

        var structureIndex = (int)type - 100;
        if (BoundEnumTypes.IsType(type)
            || structureIndex < 0 || structureIndex >= structures.Count)
        {
            return false;
        }
        if (!visiting.Add(type))
        {
            return false;
        }
        try
        {
            var structure = structures[structureIndex];
            if (structure.Fields.IsDefault)
            {
                return true;
            }
            foreach (var field in structure.Fields)
            {
                if (HasMetadataOnlyArrayType(field.Type, structures,
                        fixedArrays, pointerTypes, functionPointers, classes,
                        visiting, depth + 1, remaining - 1))
                {
                    return true;
                }
            }
            return false;
        }
        finally
        {
            _ = visiting.Remove(type);
        }
    }

    internal static bool AllowsEmptyInitialization(
        BoundNativeFixedArrayLayout layout)
        => layout.ElementCount == 0;

    internal static bool AllowsMutableLocal(BoundNativeFixedArrayLayout layout)
        => IsCarrier(layout.CarrierType);

    internal static bool AllowsElementWrites(BoundNativeFixedArrayLayout layout)
        => IsCarrier(layout.CarrierType);

    internal static bool AllowsUInt32Indices(BoundNativeFixedArrayLayout layout)
        => IsCarrier(layout.CarrierType);

    internal static bool IsIndexType(PrimitiveType type)
        => type is PrimitiveType.Int8 or PrimitiveType.Int16
            or PrimitiveType.Int32 or PrimitiveType.Int64
            or PrimitiveType.Int128 or PrimitiveType.UInt8
            or PrimitiveType.UInt16 or PrimitiveType.UInt32
            or PrimitiveType.UInt64 or PrimitiveType.UInt128;

    internal static bool IsConstantIndexInRange(BoundExpression expression,
        ulong elementCount)
        => TryGetUnsignedIndex(expression, out var value)
            && value < elementCount;

    internal static bool IsConstantIndex(BoundExpression expression,
        ulong expectedValue)
        => TryGetUnsignedIndex(expression, out var value)
            && value == expectedValue;

    internal static bool TryGetUnsignedIndex(BoundExpression expression,
        out ulong value)
    {
        value = 0;
        if (expression is not BoundConstant constant
            || !TryGetIntegerValue(constant, out var integerValue)
            || integerValue < BigInteger.Zero
            || integerValue > ulong.MaxValue)
        {
            return false;
        }

        value = (ulong)integerValue;
        return true;
    }

    private static bool TryGetIntegerValue(BoundConstant constant,
        out BigInteger value)
    {
        if (constant.Type is PrimitiveType.Int128 or PrimitiveType.UInt128)
        {
            return constant.Bits.TryGetIntegerValue(constant.Type, out value);
        }

        if (!IsIndexType(constant.Type) || constant.Bits.High != 0)
        {
            value = BigInteger.Zero;
            return false;
        }

        var width = PrimitiveTypeFacts.ScalarWidth(constant.Type);
        if (width is <= 0 or > sizeof(ulong))
        {
            value = BigInteger.Zero;
            return false;
        }

        var bitWidth = width * 8;
        var bits = bitWidth == 64
            ? constant.Bits.Low
            : constant.Bits.Low & ((1UL << bitWidth) - 1);
        var isSigned = constant.Type is PrimitiveType.Int8
            or PrimitiveType.Int16 or PrimitiveType.Int32
            or PrimitiveType.Int64;
        value = isSigned && (bits & (1UL << (bitWidth - 1))) != 0
            ? new BigInteger(bits) - (BigInteger.One << bitWidth)
            : new BigInteger(bits);
        return true;
    }

    private static BoundNativeFixedArrayLayout CreateLayout(
        PrimitiveType arrayType, PrimitiveType elementType,
        ulong elementCount, ArrayLayout geometry)
        => new(arrayType, elementType, elementCount,
            geometry.ElementStride, geometry.ByteExtent,
            geometry.Alignment,
            geometry.RequiredAlignment);

    private static bool TryComputeLayout(PrimitiveType elementType,
        ulong elementCount, IReadOnlyList<BoundStruct> structures,
        IReadOnlyList<BoundNativeFixedArrayType> fixedArrays,
        out ArrayLayout layout)
    {
        layout = default;
        if (!IsSupportedElementType(elementType, structures, fixedArrays)
            || !TryGetTypeLayout(elementType, structures, fixedArrays,
                out var elementLayout))
        {
            return false;
        }

        var alignment = Math.Max(1UL, elementLayout.Alignment);
        try
        {
            var stride = RoundUp(elementLayout.Size, alignment);
            var extent = checked(elementCount * stride);
            if (extent > GetTargetNuintMaximum())
            {
                return false;
            }

            layout = new ArrayLayout(stride, extent, alignment,
                elementLayout.RequiredAlignment);
            return true;
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    private static bool IsSupportedElement(PrimitiveType type,
        IReadOnlyList<BoundStruct> structures,
        IReadOnlyList<BoundNativeFixedArrayType> fixedArrays,
        HashSet<PrimitiveType> visiting)
    {
        if (type == PrimitiveType.Void
            || (PrimitiveTypeFacts.ScalarWidth(type) > 0
                && !BoundEnumTypes.IsType(type))
            || type is PrimitiveType.NativeUtf16ZPointer
                or PrimitiveType.NativeUtf8ZPointer
            || type == PrimitiveType.NativeRawPointer
            || NativePointerTypes.IsType(type)
            || NativeFunctionPointerTypes.IsType(type))
        {
            return true;
        }

        if (!visiting.Add(type))
        {
            return false;
        }

        try
        {
            if (NativeFixedArrayTypes.IsType(type))
            {
                var index = NativeFixedArrayTypes.Index(type);
                return (uint)index < (uint)fixedArrays.Count
                    && IsSupportedElement(fixedArrays[index].ElementType,
                        structures, fixedArrays, visiting);
            }

            var structureIndex = (int)type - 100;
            if (BoundEnumTypes.IsType(type)
                || structureIndex < 0 || structureIndex >= structures.Count)
            {
                return false;
            }

            var structure = structures[structureIndex];
            if (structure.BlittableTargetRequirement
                    != FixedLayoutStorage.WindowsX64TargetRequirement
                || structure.IsSequential == structure.IsTransparent
                || structure.IsUnique || structure.CleanupMethod is not null
                || structure.Fields.IsDefaultOrEmpty
                || (structure.IsTransparent && structure.Fields.Length != 1))
            {
                return false;
            }

            foreach (var field in structure.Fields)
            {
                if (field.RequiresUnsafeCaller
                    || !IsSupportedElement(field.Type, structures,
                        fixedArrays, visiting))
                {
                    return false;
                }
            }

            return true;
        }
        finally
        {
            _ = visiting.Remove(type);
        }
    }

    private static bool TryGetTypeLayout(PrimitiveType type,
        IReadOnlyList<BoundStruct> structures,
        IReadOnlyList<BoundNativeFixedArrayType> fixedArrays,
        out TypeLayout layout)
    {
        if (NativeFixedArrayTypes.IsType(type))
        {
            var index = NativeFixedArrayTypes.Index(type);
            if ((uint)index < (uint)fixedArrays.Count)
            {
                var fixedArray = fixedArrays[index];
                layout = new TypeLayout(fixedArray.ByteExtent,
                    fixedArray.Alignment, fixedArray.RequiredAlignment);
                return true;
            }
        }

        if (type == PrimitiveType.Void)
        {
            layout = new TypeLayout(0, 0, 1);
            return true;
        }

        if (NativePointerTypes.IsType(type)
            || NativeFunctionPointerTypes.IsType(type)
            || type is PrimitiveType.NativeUtf16ZPointer
                or PrimitiveType.NativeUtf8ZPointer
            || type == PrimitiveType.NativeRawPointer)
        {
            var size = ValueLayout.Size(type, structures);
            var alignment = ValueLayout.Alignment(type, structures);
            if (size <= 0 || alignment <= 0)
            {
                layout = default;
                return false;
            }

            layout = new TypeLayout((ulong)size, (ulong)alignment, 1);
            return true;
        }

        var structureIndex = (int)type - 100;
        if (!BoundEnumTypes.IsType(type) && structureIndex >= 0
            && structureIndex < structures.Count)
        {
            return TryGetStructureLayout(structures[structureIndex],
                structures, fixedArrays, out layout);
        }

        if (PrimitiveTypeFacts.ScalarWidth(type) > 0)
        {
            var size = ValueLayout.Size(type, structures);
            var alignment = ValueLayout.Alignment(type, structures);
            if (size <= 0 || alignment <= 0)
            {
                layout = default;
                return false;
            }

            layout = new TypeLayout((ulong)size, (ulong)alignment, 1);
            return true;
        }

        layout = default;
        return false;
    }

    private static bool TryGetStructureLayout(BoundStruct structure,
        IReadOnlyList<BoundStruct> structures,
        IReadOnlyList<BoundNativeFixedArrayType> fixedArrays,
        out TypeLayout layout)
    {
        layout = default;
        if (structure.Fields.IsDefaultOrEmpty
            || structure.IsSequential == structure.IsTransparent
            || (structure.IsTransparent && structure.Fields.Length != 1))
        {
            return false;
        }

        ulong offset = 0;
        ulong alignment = 1;
        ulong requiredAlignment = 1;
        foreach (var field in structure.Fields)
        {
            if (!TryGetTypeLayout(field.Type, structures, fixedArrays,
                    out var fieldLayout)
                || fieldLayout.Size
                    > (ulong)ValueLayout.MaximumStructureSize)
            {
                return false;
            }

            var fieldAlignment = Math.Max(1UL, fieldLayout.Alignment);
            var fieldOffset = structure.IsTransparent
                ? 0 : RoundUp(offset, fieldAlignment);
            if (fieldOffset > (ulong)ValueLayout.MaximumStructureSize
                    - fieldLayout.Size
                || field.Offset != (int)fieldOffset
                || field.Size != (int)fieldLayout.Size)
            {
                return false;
            }

            offset = fieldOffset + fieldLayout.Size;
            alignment = Math.Max(alignment, fieldAlignment);
            requiredAlignment = Math.Max(requiredAlignment,
                fieldLayout.RequiredAlignment);
        }

        var size = structure.IsTransparent
            ? offset : RoundUp(offset, alignment);
        if (size != (ulong)structure.Size
            || size > (ulong)ValueLayout.MaximumStructureSize)
        {
            return false;
        }

        layout = new TypeLayout(size, alignment, requiredAlignment);
        return true;
    }

    private static ulong RoundUp(ulong value, ulong alignment)
    {
        if (alignment == 0 || (alignment & (alignment - 1)) != 0)
        {
            throw new OverflowException();
        }

        return checked((value + alignment - 1) & ~(alignment - 1));
    }

    private static ulong GetTargetNuintMaximum()
        => NativeTargetProfile.PointerSize == sizeof(ulong)
            ? ulong.MaxValue
            : uint.MaxValue;
}
