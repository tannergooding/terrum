// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace Terrum.CodeAnalysis.Semantics;

/// <summary>Validates provisional explicit fixed-storage commitments independently of executable aggregate limits and call classification.</summary>
public static class FixedLayoutStorage
{
    /// <summary>The existing Windows x64 target requirement; not a separate layout-profile identity.</summary>
    public const int WindowsX64TargetRequirement = 1;

    /// <summary>Recomputes committed Sequential and Transparent layouts, returning natural alignment by structure index, or zero for uncommitted structures.</summary>
    /// <exception cref="ArgumentException">A commitment, transitive field, or recorded layout is invalid.</exception>
    /// <exception cref="NotSupportedException">The declaration graph exceeds verification limits.</exception>
    public static ImmutableArray<int> Validate(ImmutableArray<BoundStruct> structures)
        => Validate(structures, ImmutableArray<BoundNativeFixedArrayType>.Empty);

    internal static ImmutableArray<int> Validate(
        ImmutableArray<BoundStruct> structures,
        IReadOnlyList<BoundNativeFixedArrayType> fixedArrays)
        => Compute(structures, validateRecordedLayout: true, fixedArrays).Alignments;

    internal static ImmutableArray<BoundStruct> Materialize(
        ImmutableArray<BoundStruct> structures)
        => Compute(structures, validateRecordedLayout: false,
            ImmutableArray<BoundNativeFixedArrayType>.Empty).Structures;

    internal static ImmutableArray<BoundStruct> Materialize(
        ImmutableArray<BoundStruct> structures,
        IReadOnlyList<BoundNativeFixedArrayType> fixedArrays)
        => Compute(structures, validateRecordedLayout: false,
            fixedArrays).Structures;

    private static (ImmutableArray<BoundStruct> Structures, ImmutableArray<int> Alignments)
        Compute(ImmutableArray<BoundStruct> structures, bool validateRecordedLayout,
            IReadOnlyList<BoundNativeFixedArrayType> fixedArrays)
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
        var names = new HashSet<string>(StringComparer.Ordinal);
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
            if (structure is null || string.IsNullOrWhiteSpace(structure.Name) || !names.Add(structure.Name)
                || structure.BlittableTargetRequirement != WindowsX64TargetRequirement
                || structure.IsSequential == structure.IsTransparent
                || (structure.IsTransparent && structure.Fields.Length != 1)
                || (structure.CleanupMethod is not null && !structure.IsUnique)
                || (structure.IsPrivate && structure.IsInternal)
                || structure.Fields.IsDefaultOrEmpty)
            {
                throw Invalid();
            }
            states[index] = 1;
            var fieldNames = new HashSet<string>(StringComparer.Ordinal);
            var size = 0L;
            var alignment = 1;
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
                int width;
                int fieldAlignment;
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
                        || layout.ByteExtent > int.MaxValue
                        || layout.Alignment > int.MaxValue)
                    {
                        throw Invalid();
                    }

                    width = (int)layout.ByteExtent;
                    fieldAlignment = (int)layout.Alignment;
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
                    height = Math.Max(height, heights[nested] + 1);
                }
                var offset = structure.IsTransparent ? 0 : Align(size, fieldAlignment);
                if (offset > int.MaxValue - width
                    || (validateRecordedLayout
                        && (field.Offset != offset || field.Size != width)))
                {
                    throw Invalid();
                }
                computedFields.Add(field with {
                    Offset = (int)offset,
                    Size = width,
                });
                size = offset + width;
                alignment = Math.Max(alignment, fieldAlignment);
            }
            var computedSize = structure.IsTransparent ? size : Align(size, alignment);
            if (computedSize is < 1 or > int.MaxValue
                || (validateRecordedLayout && structure.Size != computedSize))
            {
                throw Invalid();
            }
            alignments[index] = alignment;
            heights[index] = height;
            computedStructures[index] = structure with {
                Fields = computedFields.MoveToImmutable(),
                Size = (int)computedSize,
            };
            states[index] = 2;
        }
    }

    private static long Align(long size, int alignment) => (size + alignment - 1) & -(long)alignment;

    private static ArgumentException Invalid() =>
        new("Invalid explicit Windows x64 Blittable Sequential storage commitment.");
}
