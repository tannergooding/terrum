// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace Terrum.CodeAnalysis.Semantics;

/// <summary>Validates provisional fixed-storage commitments independently of executable aggregate limits and call classification.</summary>
public static class FixedLayoutStorage
{
    /// <summary>The existing Windows x64 target requirement; not a separate layout-profile identity.</summary>
    public const int WindowsX64TargetRequirement = 1;

    /// <summary>Recomputes committed Sequential, Union, and Transparent layouts, returning natural alignment by structure index, or zero for uncommitted structures.</summary>
    /// <exception cref="ArgumentException">A commitment, transitive field, or recorded layout is invalid.</exception>
    /// <exception cref="NotSupportedException">The declaration graph exceeds verification limits.</exception>
    public static ImmutableArray<int> Validate(ImmutableArray<BoundStruct> structures)
        => Validate(structures, ImmutableArray<BoundNativeFixedArrayType>.Empty);

    internal static ImmutableArray<int> Validate(
        ImmutableArray<BoundStruct> structures,
        IReadOnlyList<BoundNativeFixedArrayType> fixedArrays,
        IReadOnlySet<int>? duplicateNameViewIndexes = null)
        => Compute(structures, validateRecordedLayout: true, fixedArrays,
            duplicateNameViewIndexes).Alignments;

    internal static ImmutableArray<BoundStruct> Materialize(
        ImmutableArray<BoundStruct> structures)
        => Compute(structures, validateRecordedLayout: false,
            ImmutableArray<BoundNativeFixedArrayType>.Empty).Structures;

    internal static ImmutableArray<BoundStruct> Materialize(
        ImmutableArray<BoundStruct> structures,
        IReadOnlyList<BoundNativeFixedArrayType> fixedArrays,
        IReadOnlySet<int>? duplicateNameViewIndexes = null)
        => Compute(structures, validateRecordedLayout: false,
            fixedArrays, duplicateNameViewIndexes).Structures;

    private static (ImmutableArray<BoundStruct> Structures, ImmutableArray<int> Alignments)
        Compute(ImmutableArray<BoundStruct> structures, bool validateRecordedLayout,
            IReadOnlyList<BoundNativeFixedArrayType> fixedArrays,
            IReadOnlySet<int>? duplicateNameViewIndexes = null)
    {
        if (structures.IsDefault)
        {
            throw new ArgumentException("Fixed-storage declarations must be initialized.", nameof(structures));
        }
        if (structures.Length > SemanticVerifier.MaximumFunctions)
        {
            throw new NotSupportedException("Fixed-storage declarations exceed the semantic type limit.");
        }
        var states = new byte[structures.Length];
        var alignments = new int[structures.Length];
        var heights = new int[structures.Length];
        var computedStructures = new BoundStruct[structures.Length];
        var names = new Dictionary<string, bool>(StringComparer.Ordinal);
        var remaining = SemanticVerifier.MaximumAnalysisVisits;
        for (var index = 0; index < structures.Length; index++)
        {
            if (structures[index] is null)
            {
                throw Invalid();
            }
            computedStructures[index] = structures[index];
            if (structures[index].BlittableTargetRequirement != 0)
            {
                Visit(index, 0);
            }
        }
        return ([.. computedStructures], [.. alignments]);

        void Visit(int index, int depth)
        {
            if (depth >= SemanticVerifier.MaximumDepth || --remaining < 0)
            {
                throw new NotSupportedException("Fixed-storage declarations exceed the semantic nesting or visit limit.");
            }
            if (states[index] == 1)
            {
                throw Invalid();
            }
            if (states[index] == 2)
            {
                if (depth + heights[index] > SemanticVerifier.MaximumDepth)
                {
                    throw new NotSupportedException("Fixed-storage declarations exceed the semantic nesting limit.");
                }
                return;
            }
            var structure = structures[index];
            if (structure is null || string.IsNullOrWhiteSpace(structure.Name))
            {
                throw Invalid();
            }

            var isHiddenImportedView
                = (duplicateNameViewIndexes?.Contains(index) ?? false)
                    || structure.StandardTypeOrigin?.IsLookupRoot == false;
            if (names.TryGetValue(structure.Name, out var existingHiddenView))
            {
                if (!existingHiddenView || !isHiddenImportedView)
                {
                    throw Invalid();
                }
            }
            else
            {
                names.Add(structure.Name, isHiddenImportedView);
            }

            if (structure.BlittableTargetRequirement != WindowsX64TargetRequirement
                || structure.LayoutKind is not (1 or 2 or 3)
                || (structure.IsTransparent && structure.Fields.Length != 1)
                || (structure.IsSequential || structure.IsUnion
                    ? structure.Packing is not (1 or 2 or 4 or 8 or 16)
                    : structure.Packing != 0)
                || (structure.DeclaredAlignment is int declaredAlignment
                    && ((!structure.IsSequential && !structure.IsUnion)
                        || declaredAlignment is < 1 or > 8192
                        || (declaredAlignment & (declaredAlignment - 1)) != 0))
                || (structure.CleanupMethod is not null && !structure.IsUnique)
                || (structure.IsPrivate && structure.IsInternal)
                || structure.Fields.IsDefaultOrEmpty)
            {
                throw Invalid();
            }
            states[index] = 1;
            var fieldNames = new HashSet<string>(StringComparer.Ordinal);
            ulong size = 0;
            var alignment = structure.IsTransparent ? 0 : 1;
            var requiredAlignment = 1;
            var height = 1;
            var computedFields = ImmutableArray.CreateBuilder<BoundStructField>(
                structure.Fields.Length);
            foreach (var field in structure.Fields)
            {
                if (--remaining < 0)
                {
                    throw new NotSupportedException("Fixed-storage fields exceed the semantic visit limit.");
                }
                if (field is null || string.IsNullOrWhiteSpace(field.Name) || !fieldNames.Add(field.Name)
                    || (field.IsPrivate && field.IsInternal))
                {
                    throw Invalid();
                }
                ulong width;
                int fieldAlignment;
                int fieldRequiredAlignment;
                if (NativeFixedArrayTypes.IsType(field.Type))
                {
                    var arrayIndex = NativeFixedArrayTypes.Index(field.Type);
                    if ((uint)arrayIndex >= (uint)fixedArrays.Count)
                    {
                        throw Invalid();
                    }

                    var array = fixedArrays[arrayIndex];
                    if (!NativeFixedArrayContract.TryGetLayout(field.Type,
                            array.ElementType, array.ElementCount, structures,
                            fixedArrays, out var layout)
                        || layout.Alignment > int.MaxValue
                        || layout.RequiredAlignment > int.MaxValue)
                    {
                        throw Invalid();
                    }

                    width = layout.ByteExtent;
                    fieldAlignment = (int)layout.Alignment;
                    fieldRequiredAlignment = (int)layout.RequiredAlignment;
                }
                else if (PrimitiveTypeFacts.ScalarWidth(field.Type) > 0
                    || field.Type is PrimitiveType.NativeUtf16ZPointer
                        or PrimitiveType.NativeUtf8ZPointer
                        or PrimitiveType.NativeRawPointer
                    || NativePointerTypes.IsType(field.Type)
                    || NativeFunctionPointerTypes.IsType(field.Type))
                {
                    width = ValueLayout.Size(field.Type, computedStructures);
                    fieldAlignment = ValueLayout.Alignment(field.Type, computedStructures);
                    fieldRequiredAlignment = 1;
                }
                else
                {
                    if ((int)field.Type is < 100 or >= ManagedClassTypes.First)
                    {
                        throw Invalid();
                    }
                    var nested = (int)field.Type - 100;
                    if (nested >= structures.Length)
                    {
                        throw Invalid();
                    }
                    Visit(nested, depth + 1);
                    if (structures[nested].IsUnique && !structure.IsUnique)
                    {
                        throw Invalid();
                    }
                    width = computedStructures[nested].Size;
                    fieldAlignment = alignments[nested];
                    fieldRequiredAlignment = computedStructures[nested]
                        .RequiredAlignment;
                    height = Math.Max(height, heights[nested] + 1);
                }
                if (structure.IsUnion
                    && !NativeFixedArrayContract.IsRootlessCopyFieldType(
                        field.Type, structures, fixedArrays))
                {
                    throw Invalid();
                }
                var fieldPlacementAlignment = fieldAlignment;
                if ((structure.IsSequential || structure.IsUnion)
                    && width != 0)
                {
                    fieldPlacementAlignment = Math.Max(
                        Math.Min(fieldAlignment, structure.Packing),
                        fieldRequiredAlignment);
                }
                var offset = structure.IsTransparent || structure.IsUnion
                    ? 0UL : width == 0 ? size : Align(size,
                        fieldPlacementAlignment);
                ulong end;
                try
                {
                    end = checked(offset + width);
                }
                catch (OverflowException)
                {
                    throw Invalid();
                }
                if (validateRecordedLayout
                    && (field.Offset != offset || field.Size != width))
                {
                    throw Invalid();
                }
                computedFields.Add(field with {
                    Offset = offset,
                    Size = width,
                });
                size = structure.IsUnion
                    ? Math.Max(size, width) : end;
                if (structure.IsTransparent)
                {
                    alignment = fieldAlignment;
                }
                else if (width != 0)
                {
                    alignment = Math.Max(alignment,
                        fieldPlacementAlignment);
                }
                requiredAlignment = Math.Max(requiredAlignment,
                    fieldRequiredAlignment);
            }
            if (structure.DeclaredAlignment is int explicitAlignment)
            {
                if (explicitAlignment < alignment)
                {
                    throw Invalid();
                }
                alignment = explicitAlignment;
                requiredAlignment = Math.Max(requiredAlignment,
                    explicitAlignment);
            }
            if (!structure.IsTransparent && size == 0)
            {
                size = 1;
            }
            var computedSize = structure.IsTransparent ? size
                : Align(size, alignment);
            if (computedSize < (structure.IsTransparent ? 0UL : 1UL)
                || (validateRecordedLayout
                    && (structure.Size != computedSize
                        || structure.Alignment != alignment
                        || structure.RequiredAlignment != requiredAlignment)))
            {
                throw Invalid();
            }
            alignments[index] = alignment;
            heights[index] = height;
            computedStructures[index] = structure with {
                Fields = computedFields.MoveToImmutable(),
                Size = computedSize,
                Alignment = alignment,
                RequiredAlignment = requiredAlignment,
            };
            states[index] = 2;
        }
    }

    private static ulong Align(ulong size, int alignment)
    {
        if (alignment <= 0 || (alignment & (alignment - 1)) != 0)
        {
            throw Invalid();
        }

        var wideAlignment = checked((ulong)alignment);
        var remainder = size & (wideAlignment - 1);
        if (remainder == 0)
        {
            return size;
        }

        try
        {
            return checked(size + wideAlignment - remainder);
        }
        catch (OverflowException)
        {
            throw Invalid();
        }
    }

    private static ArgumentException Invalid() =>
        new("Invalid explicit Windows x64 Blittable fixed-storage commitment.");
}
