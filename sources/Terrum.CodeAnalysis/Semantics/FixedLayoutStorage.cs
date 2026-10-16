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
        var names = new HashSet<string>(StringComparer.Ordinal);
        var remaining = SemanticVerifier.MaximumAnalysisVisits;
        for (var index = 0; index < structures.Length; index++)
        {
            if (structures[index] is null)
            {
                throw Invalid();
            }
            if (structures[index].BlittableTargetRequirement != 0)
            {
                Visit(index, 0);
            }
        }
        return [.. alignments];

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
                || structure.IsTransparent && structure.Fields.Length != 1
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
                if (field.Type is PrimitiveType.Bool or PrimitiveType.Char16
                    or PrimitiveType.Int32 or PrimitiveType.UInt32 or PrimitiveType.Int64 or PrimitiveType.UInt64
                    or PrimitiveType.Float32 or PrimitiveType.Float64
                    or PrimitiveType.NativeUtf16ZPointer or PrimitiveType.NativeUtf8ZPointer
                    or PrimitiveType.NativeRawPointer
                    || NativePointerTypes.IsType(field.Type)
                    || NativeFunctionPointerTypes.IsType(field.Type))
                {
                    width = ValueLayout.Size(field.Type, structures);
                    fieldAlignment = ValueLayout.Alignment(field.Type, structures);
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
                    width = structures[nested].Size;
                    fieldAlignment = alignments[nested];
                    height = Math.Max(height, heights[nested] + 1);
                }
                var offset = structure.IsTransparent ? 0 : Align(size, fieldAlignment);
                if (offset > int.MaxValue - width || field.Offset != offset || field.Size != width)
                {
                    throw Invalid();
                }
                size = offset + width;
                alignment = Math.Max(alignment, fieldAlignment);
            }
            if ((structure.IsTransparent ? size : Align(size, alignment)) != structure.Size
                || structure.Size < 1)
            {
                throw Invalid();
            }
            alignments[index] = alignment;
            heights[index] = height;
            states[index] = 2;
        }
    }

    private static long Align(long size, int alignment) => (size + alignment - 1) & -(long)alignment;

    private static ArgumentException Invalid() =>
        new("Invalid explicit Windows x64 Blittable Sequential storage commitment.");
}
