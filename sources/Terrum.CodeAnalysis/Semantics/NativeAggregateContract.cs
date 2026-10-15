// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Linq;

namespace Terrum.CodeAnalysis.Semantics;

internal static class NativeAggregateContract
{
    internal static bool IsValidStorageLayout(NativeAggregateStorageLayoutDefinition layout)
    {
        if (layout is null || string.IsNullOrWhiteSpace(layout.TypeName)
            || layout.ByteExtent <= 0 || layout.ByteExtent > ValueLayout.MaximumStructureSize
            || layout.Alignment <= 0 || (layout.Alignment & (layout.Alignment - 1)) != 0
            || layout.ByteExtent % layout.Alignment != 0 || layout.Fields.IsDefaultOrEmpty)
        {
            return false;
        }

        var names = new HashSet<string>(StringComparer.Ordinal);
        var end = 0;
        foreach (var field in layout.Fields)
        {
            if (field is null || string.IsNullOrWhiteSpace(field.Name)
                || string.IsNullOrWhiteSpace(field.TypeName) || !names.Add(field.Name)
                || field.Offset < end || field.Size <= 0
                || field.Offset > layout.ByteExtent - field.Size)
            {
                return false;
            }
            end = field.Offset + field.Size;
        }
        return true;
    }

    internal static bool MatchesStorageLayout(PrimitiveType type, IReadOnlyList<BoundStruct> structures,
        NativeImportCatalog? catalog, NativeAggregateStorageLayoutDefinition layout)
    {
        var index = (int)type - 100;
        if (index < 0 || index >= structures.Count || !IsValidStorageLayout(layout)
            || !TryResolveSourceType(layout.TypeName, structures, out var layoutType)
            || layoutType != type)
        {
            return false;
        }

        var structure = structures[index];
        if (structure.Size != layout.ByteExtent
            || ValueLayout.Alignment(type, structures, catalog) != layout.Alignment
            || structure.Fields.Length != layout.Fields.Length)
        {
            return false;
        }
        for (var fieldIndex = 0; fieldIndex < structure.Fields.Length; fieldIndex++)
        {
            var actual = structure.Fields[fieldIndex];
            var expected = layout.Fields[fieldIndex];
            if (actual.Name != expected.Name || actual.Offset != expected.Offset
                || actual.Size != expected.Size
                || !MatchesFieldType(actual.Type, expected.TypeName, structures))
            {
                return false;
            }
        }
        return true;
    }

    internal static bool TryResolveSourceType(string sourceType, IReadOnlyList<BoundStruct> structures,
        out PrimitiveType type)
    {
        var index = -1;
        for (var candidate = 0; candidate < structures.Count; candidate++)
        {
            if (structures[candidate].Name == sourceType)
            {
                index = candidate;
                break;
            }
        }
        if (index < 0)
        {
            var suffix = "." + sourceType;
            for (var candidate = 0; candidate < structures.Count; candidate++)
            {
                if (!structures[candidate].Name.EndsWith(suffix, StringComparison.Ordinal))
                {
                    continue;
                }
                if (index >= 0)
                {
                    type = default;
                    return false;
                }
                index = candidate;
            }
        }
        type = index < 0 ? default : (PrimitiveType)(100 + index);
        return index >= 0;
    }

    private static bool MatchesFieldType(PrimitiveType type, string typeName,
        IReadOnlyList<BoundStruct> structures) =>
        (int)type >= 100
            ? TryResolveSourceType(typeName, structures, out var resolved) && resolved == type
            : NativeFixedArrayContract.ElementTypeName(type, structures) == typeName;

    internal static bool IsPlainValueAggregate(PrimitiveType type, IReadOnlyList<BoundStruct> structures) =>
        IsPlainValueAggregate(type, structures, new HashSet<int>());

    internal static bool IsOwnedWindowHandleFieldArgument(BoundExpression argument,
        BoundFunction target, int parameter, BoundFunction caller, BoundProgram program)
    {
        var hasWindowHandleProvenance = argument is BoundNativeNoOwner
            || NativeHandleOwnerContract.IsOwnedWindowArgument(argument, caller, program)
            || argument is BoundRead {
                Type: PrimitiveType.NativeWindowHandle,
                Variable: var windowVariable,
            } && NativeWindowCreationContract.IsCreatedWindowLocal(caller, windowVariable, program);
        if (!target.IsConstructor || target.DeclaringStruct is not string aggregateName
            || (uint)parameter >= (uint)target.Variables.Length
            || target.Variables[parameter] is not {
                Type: PrimitiveType.NativeWindowHandle,
                IsOut: false,
                IsInit: false,
            }
            || target.Body is not BoundBlock body || body.Statements.IsDefault
            || !hasWindowHandleProvenance
            || !TryResolveSourceType(aggregateName, program.Structures, out var aggregateType)
            || program.NativeImportCatalog is not NativeImportCatalog catalog
            || !catalog.SupportsAggregateStorageLayout(aggregateType, program.Structures))
        {
            return false;
        }

        var assignments = body.Statements.OfType<BoundFieldAssignment>()
            .Where(assignment => assignment.Variable == 0
                && assignment.Value is BoundRead {
                    Variable: var sourceVariable,
                    Type: PrimitiveType.NativeWindowHandle,
                } && sourceVariable == parameter)
            .ToArray();
        if (assignments.Length != 1
            || (uint)assignments[0].Field >= (uint)program.Structures[(int)aggregateType - 100].Fields.Length
            || program.Structures[(int)aggregateType - 100].Fields[assignments[0].Field].Type
                != PrimitiveType.NativeWindowHandle)
        {
            return false;
        }

        return catalog.AggregateStorageLayouts.Any(layout =>
            MatchesStorageLayout(aggregateType, program.Structures, catalog, layout)
            && layout.Fields[assignments[0].Field].TypeName
                == nameof(PrimitiveType.NativeWindowHandle));
    }

    private static bool IsPlainValueAggregate(PrimitiveType type, IReadOnlyList<BoundStruct> structures,
        HashSet<int> visiting)
    {
        var index = (int)type - 100;
        if (index < 0 || index >= structures.Count || !visiting.Add(index))
        {
            return false;
        }

        var structure = structures[index];
        var valid = !structure.IsUnique && structure.CleanupMethod is null
            && !structure.Fields.IsDefaultOrEmpty
            && structure.Fields.All(field => IsPlainValueField(field.Type, structures, visiting));
        _ = visiting.Remove(index);
        return valid;
    }

    private static bool IsPlainValueField(PrimitiveType type, IReadOnlyList<BoundStruct> structures,
        HashSet<int> visiting) =>
        type is PrimitiveType.Bool or PrimitiveType.Char16
            or PrimitiveType.Int32 or PrimitiveType.UInt32
            or PrimitiveType.Int64 or PrimitiveType.UInt64
            or PrimitiveType.Float32 or PrimitiveType.Float64 or PrimitiveType.NativeWindowHandle
        || IsPlainValueAggregate(type, structures, visiting);
}
