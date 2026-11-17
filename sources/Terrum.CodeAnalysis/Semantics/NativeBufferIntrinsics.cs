using System.Collections.Generic;
using System.Collections.Immutable;

namespace Terrum.CodeAnalysis.Semantics;

internal static class NativeBufferIntrinsics
{
    internal const string TypeName = "OwnedInt32Buffer";
    internal const int MaximumCapacity = 1_048_576;

    internal static ImmutableArray<BoundFunction> CreateFunctions(
        PrimitiveType initializationOutputType) =>
    [
        Function(BoundIntrinsic.BufferTryCreate, "TryCreate",
            PrimitiveType.Bool, true, [
                Variable("capacity", PrimitiveType.Int32),
                Variable("buffer", initializationOutputType, isInit: true),
            ]),
        Function(BoundIntrinsic.BufferCount, "Count", PrimitiveType.Int32,
            false, [Variable("this", PrimitiveType.OwnedInt32Buffer)]),
        Function(BoundIntrinsic.BufferCapacity, "Capacity",
            PrimitiveType.Int32, false, [
                Variable("this", PrimitiveType.OwnedInt32Buffer),
            ]),
        Function(BoundIntrinsic.BufferRead, "Read", PrimitiveType.Int32,
            false, [
                Variable("this", PrimitiveType.OwnedInt32Buffer),
                Variable("index", PrimitiveType.Int32),
            ]),
        Function(BoundIntrinsic.BufferSet, "Set", PrimitiveType.Void, false, [
            Variable("this", PrimitiveType.OwnedInt32Buffer,
                isMutable: true),
            Variable("index", PrimitiveType.Int32),
            Variable("value", PrimitiveType.Int32),
        ]),
        Function(BoundIntrinsic.BufferTryAppend, "TryAppend",
            PrimitiveType.Bool, false, [
                Variable("this", PrimitiveType.OwnedInt32Buffer,
                    isMutable: true),
                Variable("value", PrimitiveType.Int32),
            ]),
        Function(BoundIntrinsic.BufferClear, "Clear", PrimitiveType.Void,
            false, [
                Variable("this", PrimitiveType.OwnedInt32Buffer,
                    isMutable: true),
            ]),
    ];

    internal static bool IsNoFailureIntrinsic(BoundIntrinsic intrinsic) =>
        intrinsic is BoundIntrinsic.BufferCount
            or BoundIntrinsic.BufferCapacity
            or BoundIntrinsic.BufferTryAppend
            or BoundIntrinsic.BufferClear;

    internal static bool IsInitializationOutputType(PrimitiveType type,
        IReadOnlyList<CheckedReferenceTypeDefinition> checkedReferenceTypes)
    {
        if (!CheckedReferenceTypes.IsType(type))
        {
            return false;
        }

        var index = CheckedReferenceTypes.Index(type);
        return (uint)index < (uint)checkedReferenceTypes.Count
            && checkedReferenceTypes[index]
                == new CheckedReferenceTypeDefinition(
                    PrimitiveType.OwnedInt32Buffer,
                    IsWritable: false,
                    IsGarbageCollected: false,
                    IsInitialization: true,
                    IsOptional: false);
    }

    internal static bool Matches(BoundFunction function,
        IReadOnlyList<CheckedReferenceTypeDefinition> checkedReferenceTypes)
    {
        var index = (int)function.Intrinsic - 1;
        if (index < 0 || index >= 7)
        {
            return false;
        }

        var outputType = CheckedReferenceTypes.Type(0);
        if (function.Intrinsic == BoundIntrinsic.BufferTryCreate)
        {
            if (function.Variables.Length != 2
                || !function.Variables[1].IsInit
                || function.Variables[1].IsOut
                || !IsInitializationOutputType(
                    function.Variables[1].Type, checkedReferenceTypes))
            {
                return false;
            }

            outputType = function.Variables[1].Type;
        }

        var expected = CreateFunctions(outputType)[index];
        if (function.Name != expected.Name
            || function.ReturnType != expected.ReturnType
            || function.ParameterCount != expected.ParameterCount
            || function.Variables.Length != expected.Variables.Length
            || function.DeclaringStruct != TypeName
            || function.IsConstructor || function.IsCleanup
            || function.IsExternal || function.IsPrivate || !function.IsInternal
            || function.IsStatic != expected.IsStatic
            || function.NoFailureProof != expected.NoFailureProof
            || function.DoesNotReturn || function.Body is null
            || function.Body.Statements.IsDefault
            || !function.Body.Statements.IsEmpty)
        {
            return false;
        }
        for (var variable = 0; variable < expected.Variables.Length; variable++)
        {
            var actual = function.Variables[variable];
            var canonical = expected.Variables[variable];
            if (actual.Name != canonical.Name || actual.Type != canonical.Type
                || actual.IsMutable != canonical.IsMutable
                || actual.IsOut != canonical.IsOut
                || actual.IsInit != canonical.IsInit)
            {
                return false;
            }
        }
        return true;
    }

    private static BoundVariable Variable(string name, PrimitiveType type,
        bool isMutable = false, bool isOut = false, bool isInit = false) =>
        new(name, type, isMutable, default) {
            IsOut = isOut,
            IsInit = isInit,
        };

    private static BoundFunction Function(BoundIntrinsic intrinsic,
        string name, PrimitiveType result, bool isStatic,
        ImmutableArray<BoundVariable> variables) =>
        new(TypeName + "." + name, result, variables, variables.Length,
            new BoundBlock([], default), default) {
            DeclaringStruct = TypeName,
            IsStatic = isStatic,
            IsInternal = true,
            NoFailureProof = intrinsic is BoundIntrinsic.BufferCount
                or BoundIntrinsic.BufferCapacity
                or BoundIntrinsic.BufferTryAppend
                or BoundIntrinsic.BufferClear,
            Intrinsic = intrinsic,
        };
}
