// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System.Collections.Generic;
using System.Collections.Immutable;

namespace Terrum.CodeAnalysis.Semantics;

internal static class ManagedExportShape
{
    internal static bool ScalarOnly(BoundFunction function) =>
        (function.ReturnType == PrimitiveType.Void || Scalar(function.ReturnType))
        && Parameters(function, out var offset)
        && ScalarParameters(function, offset);

    internal static bool Supported(BoundFunction function, ImmutableArray<BoundStruct> structures)
    {
        Dictionary<(PrimitiveType Type, int Depth), bool>? valueCache = null;
        if (!Parameters(function, out var offset)
            || (function.ReturnType != PrimitiveType.Void && !HasValueLayout(function.ReturnType))
            || (function.DeclaringStruct is not null && !function.IsStatic && !function.IsConstructor
                && !function.IsClassConstructor && !function.Variables.IsEmpty
                && function.Variables[0].IsMutable))
        {
            return false;
        }
        for (var index = offset; index < offset + function.ParameterCount; index++)
        {
            var parameter = function.Variables[index];
            if (!HasValueLayout(parameter.Type)
                || (parameter.IsOut && !parameter.IsInit && function.ReturnType != PrimitiveType.Bool
                    && !CopyStruct(parameter.Type, structures)))
            {
                return false;
            }
        }
        return true;

        bool HasValueLayout(PrimitiveType type)
        {
            if (Scalar(type))
            {
                return true;
            }
            valueCache ??= new Dictionary<(PrimitiveType Type, int Depth), bool>();
            return Value(type, structures, 0, valueCache);
        }
    }

    private static bool Parameters(BoundFunction function, out int offset)
    {
        offset = function.IsConstructor ? 1 : 0;
        return !function.Variables.IsDefault && function.ParameterCount >= 0
            && function.ParameterCount <= function.Variables.Length - offset;
    }

    private static bool ScalarParameters(BoundFunction function, int offset)
    {
        for (var index = offset; index < offset + function.ParameterCount; index++)
        {
            var parameter = function.Variables[index];
            if (parameter.IsOut || !Scalar(parameter.Type))
            {
                return false;
            }
        }
        return true;
    }

    private static bool Scalar(PrimitiveType type) => type is PrimitiveType.Bool or PrimitiveType.Int32
        or PrimitiveType.UInt32 or PrimitiveType.Int64 or PrimitiveType.UInt64 or PrimitiveType.Char16
        or PrimitiveType.Float32 or PrimitiveType.Float64;

    private static bool CopyStruct(PrimitiveType type, IReadOnlyList<BoundStruct> structures)
    {
        var index = (int)type - 100;
        return index >= 0 && index < structures.Count && !structures[index].IsUnique;
    }

    private static bool Value(PrimitiveType type, IReadOnlyList<BoundStruct> structures, int depth,
        Dictionary<(PrimitiveType Type, int Depth), bool> cache)
    {
        if (Scalar(type))
        {
            return true;
        }
        var key = (type, depth);
        if (cache.TryGetValue(key, out var supported))
        {
            return supported;
        }
        var index = (int)type - 100;
        if (index < 0 || index >= structures.Count || depth >= structures.Count)
        {
            cache.Add(key, false);
            return false;
        }
        var structure = structures[index];
        if (structure.IsPrivate || structure.IsInternal || structure.IsUnique
            || structure.Size is < 1 or > ValueLayout.MaximumSize || structure.Fields.IsDefaultOrEmpty)
        {
            cache.Add(key, false);
            return false;
        }
        foreach (var field in structure.Fields)
        {
            if (field.IsPrivate || field.IsInternal || !Value(field.Type, structures, depth + 1, cache))
            {
                cache.Add(key, false);
                return false;
            }
        }
        cache.Add(key, true);
        return true;
    }
}
