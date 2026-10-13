// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Linq;

namespace Terrum.CodeAnalysis.Semantics;

internal static class NativeAggregateContract
{
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

    internal static bool IsPlainValueAggregate(PrimitiveType type, IReadOnlyList<BoundStruct> structures) =>
        IsPlainValueAggregate(type, structures, new HashSet<int>());

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
