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
        if (!Parameters(function, out var offset)
            || (function.ReturnType != PrimitiveType.Void && !Value(function.ReturnType, structures, 0))
            || (function.DeclaringStruct is not null && !function.IsStatic && !function.IsConstructor
                && !function.IsClassConstructor && !function.Variables.IsEmpty
                && function.Variables[0].IsMutable))
        {
            return false;
        }
        for (var index = offset; index < offset + function.ParameterCount; index++)
        {
            var parameter = function.Variables[index];
            if (!Value(parameter.Type, structures, 0)
                || (parameter.IsOut && !parameter.IsInit && function.ReturnType != PrimitiveType.Bool))
            {
                return false;
            }
        }
        return true;
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
        or PrimitiveType.UInt32 or PrimitiveType.Int64 or PrimitiveType.UInt64 or PrimitiveType.Char16;

    private static bool Value(PrimitiveType type, IReadOnlyList<BoundStruct> structures, int depth)
    {
        if (Scalar(type))
        {
            return true;
        }
        var index = (int)type - 100;
        if (index < 0 || index >= structures.Count || depth >= structures.Count)
        {
            return false;
        }
        var structure = structures[index];
        if (structure.IsPrivate || structure.IsInternal || structure.IsUnique
            || structure.Size is < 1 or > ValueLayout.MaximumSize || structure.Fields.IsDefaultOrEmpty)
        {
            return false;
        }
        foreach (var field in structure.Fields)
        {
            if (field.IsPrivate || field.IsInternal || !Value(field.Type, structures, depth + 1))
            {
                return false;
            }
        }
        return true;
    }
}
