// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System.Collections.Immutable;

namespace Terrum.CodeAnalysis.Semantics;

internal static class NativeManagedNodeIntrinsics
{
    internal const string TypeName = "ManagedNode";
    internal const string HeapName = "ManagedHeap";
    internal const int MaximumNodes = 4096;

    internal static ImmutableArray<BoundFunction> Functions { get; } = [
        Function(BoundIntrinsic.NodeTryCreate, TypeName, "TryCreate", PrimitiveType.Bool,
            [Variable("value", PrimitiveType.Int32), Variable("node", PrimitiveType.ManagedNode, isOut: true)]),
        Function(BoundIntrinsic.NodeValue, TypeName, "Value", PrimitiveType.Int32,
            [Variable("this", PrimitiveType.ManagedNode)]),
        Function(BoundIntrinsic.NodeSetValue, TypeName, "SetValue", PrimitiveType.Void,
            [Variable("this", PrimitiveType.ManagedNode), Variable("value", PrimitiveType.Int32)]),
        Function(BoundIntrinsic.NodeSetNext, TypeName, "SetNext", PrimitiveType.Void,
            [Variable("this", PrimitiveType.ManagedNode), Variable("next", PrimitiveType.ManagedNode)]),
        Function(BoundIntrinsic.NodeClearNext, TypeName, "ClearNext", PrimitiveType.Void,
            [Variable("this", PrimitiveType.ManagedNode)]),
        Function(BoundIntrinsic.NodeTryGetNext, TypeName, "TryGetNext", PrimitiveType.Bool,
            [Variable("this", PrimitiveType.ManagedNode), Variable("next", PrimitiveType.ManagedNode, isOut: true)]),
        Function(BoundIntrinsic.NodeCollect, HeapName, "Collect", PrimitiveType.Int32, []),
    ];

    internal static bool Matches(BoundFunction function)
    {
        var index = (int)function.Intrinsic - (int)BoundIntrinsic.NodeTryCreate;
        if (index < 0 || index >= Functions.Length)
        {
            return false;
        }
        var expected = Functions[index];
        if (function.Name != expected.Name || function.ReturnType != expected.ReturnType
            || function.ParameterCount != expected.ParameterCount || function.Variables.Length != expected.Variables.Length
            || function.DeclaringStruct != expected.DeclaringStruct
            || function.IsExternal || function.IsPrivate || !function.IsInternal
            || function.IsConstructor || function.IsCleanup || function.IsStatic != expected.IsStatic
            || function.IsNoThrow != expected.IsNoThrow || function.DoesNotReturn
            || function.Body.Statements.IsDefault || !function.Body.Statements.IsEmpty)
        {
            return false;
        }
        for (var i = 0; i < expected.Variables.Length; i++)
        {
            if (function.Variables[i].Name != expected.Variables[i].Name
                || function.Variables[i].Type != expected.Variables[i].Type
                || function.Variables[i].IsOut != expected.Variables[i].IsOut
                || function.Variables[i].IsMutable != expected.Variables[i].IsMutable)
            {
                return false;
            }
        }
        return true;
    }

    private static BoundVariable Variable(string name, PrimitiveType type, bool isOut = false) =>
        new(name, type, false, default) { IsOut = isOut };

    private static BoundFunction Function(BoundIntrinsic intrinsic, string typeName, string name,
        PrimitiveType returnType, ImmutableArray<BoundVariable> variables) =>
        new(typeName + "." + name, returnType, variables, variables.Length, new BoundBlock([], default), default) {
            DeclaringStruct = typeName,
            IsStatic = intrinsic is BoundIntrinsic.NodeTryCreate or BoundIntrinsic.NodeCollect,
            IsInternal = true,
            IsNoThrow = intrinsic is not BoundIntrinsic.NodeTryCreate,
            Intrinsic = intrinsic,
        };
}
