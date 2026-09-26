// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System.Collections.Immutable;

namespace Terrum.CodeAnalysis.Semantics;

internal static class NativeBufferIntrinsics
{
    internal const string TypeName = "OwnedInt32Buffer";
    internal const int MaximumCapacity = 1_048_576;

    internal static ImmutableArray<BoundFunction> Functions { get; } = [
        Function(BoundIntrinsic.BufferTryCreate, "TryCreate", PrimitiveType.Bool, true,
            [Variable("capacity", PrimitiveType.Int32), Variable("buffer", PrimitiveType.OwnedInt32Buffer, isOut: true)]),
        Function(BoundIntrinsic.BufferCount, "Count", PrimitiveType.Int32, false,
            [Variable("this", PrimitiveType.OwnedInt32Buffer)]),
        Function(BoundIntrinsic.BufferCapacity, "Capacity", PrimitiveType.Int32, false,
            [Variable("this", PrimitiveType.OwnedInt32Buffer)]),
        Function(BoundIntrinsic.BufferRead, "Read", PrimitiveType.Int32, false,
            [Variable("this", PrimitiveType.OwnedInt32Buffer), Variable("index", PrimitiveType.Int32)]),
        Function(BoundIntrinsic.BufferSet, "Set", PrimitiveType.Void, false,
            [Variable("this", PrimitiveType.OwnedInt32Buffer, isMutable: true),
                Variable("index", PrimitiveType.Int32), Variable("value", PrimitiveType.Int32)]),
        Function(BoundIntrinsic.BufferTryAppend, "TryAppend", PrimitiveType.Bool, false,
            [Variable("this", PrimitiveType.OwnedInt32Buffer, isMutable: true),
                Variable("value", PrimitiveType.Int32)]),
    ];

    internal static bool Matches(BoundFunction function)
    {
        var index = (int)function.Intrinsic - 1;
        if (index < 0 || index >= Functions.Length)
        {
            return false;
        }
        var expected = Functions[index];
        if (function.Name != expected.Name || function.ReturnType != expected.ReturnType
            || function.ParameterCount != expected.ParameterCount || function.Variables.Length != expected.Variables.Length
            || function.DeclaringStruct != TypeName || function.IsConstructor || function.IsCleanup
            || function.IsExternal || function.IsPrivate || !function.IsInternal
            || function.IsStatic != expected.IsStatic || function.IsNoThrow != expected.IsNoThrow
            || function.DoesNotReturn || function.Body is null
            || function.Body.Statements.IsDefault || !function.Body.Statements.IsEmpty)
        {
            // The empty body is a compiler implementation marker, not a source-defined function.
            return false;
        }
        for (var variable = 0; variable < expected.Variables.Length; variable++)
        {
            var actual = function.Variables[variable];
            var canonical = expected.Variables[variable];
            if (actual.Name != canonical.Name || actual.Type != canonical.Type
                || actual.IsMutable != canonical.IsMutable || actual.IsOut != canonical.IsOut)
            {
                return false;
            }
        }
        return true;
    }

    private static BoundVariable Variable(string name, PrimitiveType type, bool isMutable = false, bool isOut = false) =>
        new(name, type, isMutable, default) { IsOut = isOut };

    private static BoundFunction Function(BoundIntrinsic intrinsic, string name, PrimitiveType result,
        bool isStatic, ImmutableArray<BoundVariable> variables) =>
        new(TypeName + "." + name, result, variables, variables.Length, new BoundBlock([], default), default) {
            DeclaringStruct = TypeName,
            IsStatic = isStatic,
            IsInternal = true,
            IsNoThrow = intrinsic is BoundIntrinsic.BufferCount or BoundIntrinsic.BufferCapacity or BoundIntrinsic.BufferTryAppend,
            Intrinsic = intrinsic,
        };
}
