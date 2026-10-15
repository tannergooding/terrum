// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System.Linq;

namespace Terrum.CodeAnalysis.Semantics;

internal static class NativeHandleOwnerContract
{
    internal static bool IsWindowHandleOwner(BoundStruct structure) =>
        structure.IsUnique && structure.CleanupMethod is not null
        && !structure.Fields.IsDefaultOrEmpty
        && structure.Fields.Any(field => IsWindowHandleField(structure, field));

    internal static bool IsWindowHandleOwnerType(PrimitiveType type, BoundProgram program)
    {
        var index = (int)type - 100;
        return (uint)index < (uint)program.Structures.Length
            && IsWindowHandleOwner(program.Structures[index]);
    }

    internal static bool IsWindowHandleField(BoundStruct structure, BoundStructField field) =>
        structure.IsUnique && structure.CleanupMethod is not null
        && field.Type == PrimitiveType.NativeWindowHandle
        && field.IsPrivate && !field.IsInternal && !field.IsMutable && !field.RequiresUnsafeCaller;

    internal static bool IsOwnerConstructor(BoundFunction function, BoundProgram program) =>
        function.IsConstructor && function.DeclaringStruct is string name
        && program.Structures.Any(structure =>
            structure.Name == name && IsWindowHandleOwner(structure));

    internal static bool IsOwnedHandleExpression(BoundExpression expression, BoundFunction function,
        BoundProgram program) =>
        IsOwnerConstructor(function, program)
        && (expression is BoundNativeNoOwner
            || (expression is BoundCall { Type: PrimitiveType.NativeWindowHandle } call
                && (uint)call.Function < (uint)program.Functions.Length
                && NativeWindowCreationContract.IsCreateWindowFunction(
                    program.Functions[call.Function], program.NativeImportCatalog))
            || IsOwnerHandleFieldRead(expression, function, program));

    internal static bool IsOwnerHandleFieldRead(BoundExpression expression, BoundFunction function,
        BoundProgram program)
    {
        if (expression is not BoundFieldRead {
                Type: PrimitiveType.NativeWindowHandle,
                Receiver: BoundRead { Variable: 0, Type: var ownerType },
            } field
            || !IsWindowHandleOwnerType(ownerType, program)
            || function.IsStatic || function.IsExternal
            || function.DeclaringStruct != program.Structures[(int)ownerType - 100].Name
            || (uint)field.Field >= (uint)program.Structures[(int)ownerType - 100].Fields.Length)
        {
            return false;
        }

        var structure = program.Structures[(int)ownerType - 100];
        return IsWindowHandleField(structure, structure.Fields[field.Field]);
    }

    internal static bool IsOwnerHandleComparison(BoundNativeWindowHandleIsNoOwner comparison,
        BoundFunction function, BoundProgram program) =>
        IsOwnerHandleFieldRead(comparison.Handle, function, program);

    internal static bool IsOwnedWindowArgument(BoundExpression argument, BoundFunction caller,
        BoundProgram program) =>
        IsOwnerHandleFieldRead(argument, caller, program);
}
