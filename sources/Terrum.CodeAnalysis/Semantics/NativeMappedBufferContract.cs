// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Linq;

namespace Terrum.CodeAnalysis.Semantics;

internal static class NativeMappedBufferContract
{
    internal const string ReadByteIntrinsicName = "nativebyte";

    internal static bool TryGetDefinition(PrimitiveType type, NativeImportCatalog? catalog,
        System.Collections.Generic.IReadOnlyList<BoundStruct> structures, out NativeComMethodDefinition? mapMethod,
        out NativeComMappedBufferDefinition? definition)
    {
        mapMethod = null;
        definition = null;
        if (catalog is null || !IsStruct(type, structures))
        {
            return false;
        }

        var structure = structures[(int)type - 100];
        if (!NativeAggregateContract.TryResolveSourceType(structure.Name, structures,
                out var resolvedStructureType)
            || resolvedStructureType != type)
        {
            return false;
        }
        foreach (var candidate in catalog.ComMethods)
        {
            var mapped = candidate.MappedBufferOutput;
            var adaptation = mapped is null
                ? null : FindAggregateAddressAdaptation(candidate, mapped);
            if (mapped is null || mapped.ReadOnlyMapArguments.IsDefaultOrEmpty
                || !NativeAggregateContract.TryResolveSourceType(mapped.SourceType, structures,
                    out var mappedStructureType)
                || mappedStructureType != type
                || adaptation is null
                || !NativeAggregateContract.IsPlainValueAggregate(type, structures)
                || structure.Size != adaptation.SourceSize
                || ValueLayout.Alignment(type, structures, catalog) != adaptation.SourceAlignment
                || !structure.Fields.Any(field => field.Offset == mapped.PointerOffset
                    && field.Type == PrimitiveType.Int64 && field.Size == NativeTargetProfile.PointerSize))
            {
                continue;
            }
            if (!structure.Fields.Any(field => field.Offset == mapped.LengthOffset
                && field.Type == PrimitiveType.UInt32 && field.Size == sizeof(uint)))
            {
                continue;
            }

            mapMethod = candidate;
            definition = mapped;
            return true;
        }

        return false;
    }

    internal static bool Matches(BoundNativeMappedByteRead read, BoundFunction function, BoundProgram program)
    {
        if ((uint)read.Variable >= (uint)function.Variables.Length
            || read.Index.Type != PrimitiveType.UInt64)
        {
            return false;
        }

        var mappedType = function.Variables[read.Variable].Type;
        if (!TryGetDefinition(mappedType, program.NativeImportCatalog, program.Structures,
                out var mapMethod, out var definition)
            || mapMethod is null || definition is null
            || read.SourceType != definition.SourceType
            || read.PointerOffset != definition.PointerOffset
            || read.LengthOffset != definition.LengthOffset
            || read.MapMethodIdentity != mapMethod.Identity
            || read.UnmapMethodIdentity != definition.UnmapMethodIdentity
            || !HasMappedLifetime(read, function, program, mapMethod, definition))
        {
            return false;
        }

        return true;
    }

    internal static bool ContainsByteRead(BoundStatement statement) =>
        ContainsExpression(statement, static expression => expression is BoundNativeMappedByteRead);

    private static bool HasMappedLifetime(BoundNativeMappedByteRead read, BoundFunction function,
        BoundProgram program, NativeComMethodDefinition mapMethod, NativeComMappedBufferDefinition definition)
    {
        var mapCallCount = 0;
        _ = BoundTreeTraversal.RewriteCalls(function, call => {
            if (IsMappedOutputCall(call, read.Variable, mapMethod, definition, function, program))
            {
                mapCallCount++;
            }
            return call;
        });
        if (mapCallCount != 1)
        {
            return false;
        }

        var scopes = new List<(BoundCall MapCall, BoundTryFinally Scope)>();
        CollectMapScopes(function.Body, scopes, read.Variable, mapMethod, definition, function, program);
        return scopes is [(var mapCall, var scope)]
            && ContainsExpression(scope.Body, expression => ReferenceEquals(expression, read))
            && MatchesReadonlyMapCall(mapCall, mapMethod, definition, program)
            && MatchesUnmapFinally(scope, mapCall, mapMethod, definition, program)
            && !WritesMappedValue(scope.Body, read.Variable);
    }

    private static void CollectMapScopes(BoundStatement statement,
        List<(BoundCall MapCall, BoundTryFinally Scope)> scopes, int mappedVariable,
        NativeComMethodDefinition mapMethod, NativeComMappedBufferDefinition definition,
        BoundFunction function, BoundProgram program)
    {
        switch (statement)
        {
            case BoundBlock block:
                for (var index = 0; index + 1 < block.Statements.Length; index++)
                {
                    if (block.Statements[index] is BoundIf {
                            Condition: BoundUnary {
                                Operator: Lexing.TokenKind.Bang,
                                Operand: BoundCall mapCall,
                            },
                            Then: BoundBlock { Statements: [BoundReturn] },
                            Else: null,
                        }
                        && block.Statements[index + 1] is BoundTryFinally scope
                        && IsMappedOutputCall(mapCall, mappedVariable, mapMethod, definition, function, program))
                    {
                        scopes.Add((mapCall, scope));
                    }
                }
                foreach (var child in block.Statements)
                {
                    CollectMapScopes(child, scopes, mappedVariable, mapMethod, definition, function, program);
                }
                break;
            case BoundIf branch:
                CollectMapScopes(branch.Then, scopes, mappedVariable, mapMethod, definition, function, program);
                if (branch.Else is not null)
                {
                    CollectMapScopes(branch.Else, scopes, mappedVariable, mapMethod, definition, function, program);
                }
                break;
            case BoundTryFinally scope:
                CollectMapScopes(scope.Body, scopes, mappedVariable, mapMethod, definition, function, program);
                CollectMapScopes(scope.Finally, scopes, mappedVariable, mapMethod, definition, function, program);
                break;
            case BoundTryCatch scope:
                CollectMapScopes(scope.Body, scopes, mappedVariable, mapMethod, definition, function, program);
                CollectMapScopes(scope.Catch, scopes, mappedVariable, mapMethod, definition, function, program);
                if (scope.Finally is not null)
                {
                    CollectMapScopes(scope.Finally, scopes, mappedVariable, mapMethod, definition, function, program);
                }
                break;
            case BoundWhile loop:
                CollectMapScopes(loop.Body, scopes, mappedVariable, mapMethod, definition, function, program);
                if (loop.Iterator is not null)
                {
                    CollectMapScopes(loop.Iterator, scopes, mappedVariable, mapMethod, definition, function, program);
                }
                break;
            case BoundUnsafe unsafeBlock:
                CollectMapScopes(unsafeBlock.Body, scopes, mappedVariable, mapMethod, definition, function, program);
                break;
            case BoundViewScope view:
                CollectMapScopes(view.Body, scopes, mappedVariable, mapMethod, definition, function, program);
                break;
        }
    }

    private static bool IsMappedOutputCall(BoundCall call, int mappedVariable,
        NativeComMethodDefinition mapMethod, NativeComMappedBufferDefinition definition,
        BoundFunction function, BoundProgram program)
    {
        if ((uint)call.Function >= (uint)program.Functions.Length
            || !ReferenceEquals(program.Functions[call.Function].NativeComMethod, mapMethod)
            || !TryGetNativeArgument(call, program.Functions[call.Function], mapMethod,
                definition.OutputParameterIndex, out var argument)
            || argument is not BoundNativeStorageAddress address)
        {
            return false;
        }

        var mappedType = function.Variables[mappedVariable].Type;
        var size = ValueLayout.Size(mappedType, program.Structures);
        return address.Variable == mappedVariable
            && address.CarrierType == mappedType && address.ElementType == mappedType
            && address.Type == PrimitiveType.NativeStorageAddress
            && address.ElementCount == 1 && address.ElementStride == size && address.ByteExtent == size
            && !address.IsReadonly
            && address.Index is BoundConstant { Type: PrimitiveType.Int32, Value: 0 };
    }

    private static bool MatchesReadonlyMapCall(BoundCall mapCall, NativeComMethodDefinition mapMethod,
        NativeComMappedBufferDefinition definition, BoundProgram program)
    {
        if ((uint)mapCall.Function >= (uint)program.Functions.Length
            || !ReferenceEquals(program.Functions[mapCall.Function].NativeComMethod, mapMethod))
        {
            return false;
        }

        var target = program.Functions[mapCall.Function];
        foreach (var required in definition.ReadOnlyMapArguments)
        {
            if (!TryGetNativeArgument(mapCall, target, mapMethod, required.NativeParameterIndex,
                    out var argument)
                || argument is not BoundConstant { Type: PrimitiveType.UInt32, Value: var value }
                || value != required.Value)
            {
                return false;
            }
        }

        return true;
    }

    private static bool MatchesUnmapFinally(BoundTryFinally scope, BoundCall mapCall,
        NativeComMethodDefinition mapMethod, NativeComMappedBufferDefinition definition, BoundProgram program)
    {
        if (program.NativeImportCatalog is not NativeImportCatalog catalog
            || !catalog.TryGetComMethod(definition.UnmapMethodIdentity, out var unmapMethod)
            || unmapMethod is null
            || scope.Finally.Statements is not [
                BoundExpressionStatement { Expression: BoundCall unmapCall },
            ]
            || (uint)unmapCall.Function >= (uint)program.Functions.Length
            || !ReferenceEquals(program.Functions[unmapCall.Function].NativeComMethod, unmapMethod)
            || CountCalls(scope.Body, definition.UnmapMethodIdentity, program) != 0)
        {
            return false;
        }

        var mapTarget = program.Functions[mapCall.Function];
        var unmapTarget = program.Functions[unmapCall.Function];
        return TryGetNativeArgument(mapCall, mapTarget, mapMethod, 0, out var mapContext)
            && TryGetNativeArgument(mapCall, mapTarget, mapMethod, 1, out var mapResource)
            && TryGetNativeArgument(mapCall, mapTarget, mapMethod, 2, out var mapSubresource)
            && TryGetNativeArgument(unmapCall, unmapTarget, unmapMethod, 0, out var unmapContext)
            && TryGetNativeArgument(unmapCall, unmapTarget, unmapMethod, 1, out var unmapResource)
            && TryGetNativeArgument(unmapCall, unmapTarget, unmapMethod, 2, out var unmapSubresource)
            && mapContext is not null && mapResource is not null && mapSubresource is not null
            && unmapContext is not null && unmapResource is not null && unmapSubresource is not null
            && SameOwnerLoan(mapContext, unmapContext)
            && SameOwnerLoan(mapResource, unmapResource)
            && SameSubresource(mapSubresource, unmapSubresource);
    }

    private static bool TryGetNativeArgument(BoundCall call, BoundFunction target,
        NativeComMethodDefinition method, int nativeParameter, out BoundExpression? argument)
    {
        argument = null;
        if (call.Arguments.IsDefault)
        {
            return false;
        }

        for (var index = 0; index < call.Arguments.Length; index++)
        {
            if (NativeComCallContract.NativeParameterIndex(target, call.ParameterIndex(index), method)
                != nativeParameter)
            {
                continue;
            }
            if (argument is not null)
            {
                return false;
            }
            argument = call.Arguments[index];
        }

        return argument is not null;
    }

    private static int CountCalls(BoundStatement statement, string methodIdentity, BoundProgram program)
    {
        var count = 0;
        _ = BoundTreeTraversal.RewriteCalls(
            new BoundFunction("native-mapped-buffer-call-count", PrimitiveType.Void, [], 0,
                new BoundBlock([statement], statement.Span), statement.Span),
            call => {
                if ((uint)call.Function < (uint)program.Functions.Length
                    && program.Functions[call.Function].NativeComMethod?.Identity == methodIdentity)
                {
                    count++;
                }
                return call;
            });
        return count;
    }

    private static bool SameOwnerLoan(BoundExpression left, BoundExpression right) =>
        (left, right) switch {
            (BoundReceiverLoan leftLoan, BoundReceiverLoan rightLoan) =>
                leftLoan.Variable == rightLoan.Variable && leftLoan.Type == rightLoan.Type,
            (BoundFieldLoan leftLoan, BoundFieldLoan rightLoan) =>
                SameFieldRead(leftLoan.Place, rightLoan.Place),
            _ => false,
        };

    private static bool SameFieldRead(BoundFieldRead left, BoundFieldRead right) =>
        left.Field == right.Field && left.Type == right.Type
        && SamePlace(left.Receiver, right.Receiver);

    private static bool SamePlace(BoundExpression left, BoundExpression right) =>
        (left, right) switch {
            (BoundRead leftRead, BoundRead rightRead) =>
                leftRead.Variable == rightRead.Variable && leftRead.Type == rightRead.Type,
            (BoundReceiverLoan leftLoan, BoundReceiverLoan rightLoan) =>
                leftLoan.Variable == rightLoan.Variable && leftLoan.Type == rightLoan.Type,
            (BoundFieldRead leftField, BoundFieldRead rightField) =>
                SameFieldRead(leftField, rightField),
            _ => false,
        };

    private static bool SameSubresource(BoundExpression left, BoundExpression right) =>
        left is BoundConstant { Type: PrimitiveType.UInt32, Value: var leftValue }
        && right is BoundConstant { Type: PrimitiveType.UInt32, Value: var rightValue }
        && leftValue == rightValue;

    private static bool WritesMappedValue(BoundStatement statement, int variable)
    {
        var writesMappedExpression = ContainsExpression(statement, expression => expression switch {
            BoundNativeStorageAddress address => address.Variable == variable,
            BoundOutArgument output => output.Variable == variable,
            BoundMove move => move.Variable == variable,
            BoundMutableReceiverLoan loan => loan.Variable == variable,
            BoundMutableFieldLoan loan => PlaceVariable(loan.Place) == variable,
            BoundUpdate update => PlaceVariable(update.Place) == variable,
            BoundCompoundAssignment assignment => PlaceVariable(assignment.Place) == variable,
            BoundNominalUpdate update => PlaceVariable(update.Place) == variable,
            _ => false,
        });
        return writesMappedExpression || statement switch {
            BoundBlock block => block.Statements.Any(child => WritesMappedValue(child, variable)),
            BoundLocal local => local.Variable == variable,
            BoundVacantLocal local => local.Variable == variable,
            BoundLifetimeEnd end => end.Variable == variable,
            BoundOwnerDeclaration owner => owner.Variable == variable,
            BoundAssignment assignment => assignment.Variable == variable,
            BoundOptionalAssignment assignment => PlaceVariable(assignment.Place) == variable,
            BoundFieldAssignment assignment => assignment.Variable == variable,
            BoundIf branch => WritesMappedValue(branch.Then, variable)
                || branch.Else is not null && WritesMappedValue(branch.Else, variable),
            BoundWhile loop => WritesMappedValue(loop.Body, variable)
                || loop.Iterator is not null && WritesMappedValue(loop.Iterator, variable),
            BoundTryFinally scope => WritesMappedValue(scope.Body, variable)
                || WritesMappedValue(scope.Finally, variable),
            BoundTryCatch scope => WritesMappedValue(scope.Body, variable)
                || WritesMappedValue(scope.Catch, variable)
                || scope.Finally is not null && WritesMappedValue(scope.Finally, variable),
            BoundUnsafe unsafeBlock => WritesMappedValue(unsafeBlock.Body, variable),
            BoundViewScope view => view.Variable == variable
                || WritesMappedValue(view.Body, variable),
            BoundViewSet view => view.Variable == variable,
            BoundDrop drop => drop.Variable == variable,
            BoundDropSpan drop => drop.Variable == variable,
            BoundDropConstructedFields drop => drop.Variable == variable,
            _ => false,
        };
    }

    private static int PlaceVariable(BoundExpression expression) =>
        expression switch {
            BoundRead read => read.Variable,
            BoundReceiverLoan loan => loan.Variable,
            BoundMutableReceiverLoan loan => loan.Variable,
            BoundFieldRead field => PlaceVariable(field.Receiver),
            BoundFieldLoan loan => PlaceVariable(loan.Place.Receiver),
            BoundMutableFieldLoan loan => PlaceVariable(loan.Place.Receiver),
            _ => -1,
        };

    private static bool ContainsExpression(BoundStatement statement, Func<BoundExpression, bool> predicate) =>
        statement switch {
            BoundBlock block => block.Statements.Any(child => ContainsExpression(child, predicate)),
            BoundLocal local => ContainsExpression(local.Initializer, predicate),
            BoundAssignment assignment => ContainsExpression(assignment.Value, predicate),
            BoundOptionalAssignment assignment => ContainsExpression(assignment.Place, predicate)
                || ContainsExpression(assignment.Value, predicate),
            BoundFieldAssignment assignment => ContainsExpression(assignment.Value, predicate),
            BoundBufferAssignment assignment => ContainsExpression(assignment.Index, predicate)
                || ContainsExpression(assignment.Value, predicate),
            BoundExpressionStatement expression => ContainsExpression(expression.Expression, predicate),
            BoundIf branch => ContainsExpression(branch.Condition, predicate)
                || ContainsExpression(branch.Then, predicate)
                || branch.Else is not null && ContainsExpression(branch.Else, predicate),
            BoundTryFinally scope => ContainsExpression(scope.Body, predicate)
                || ContainsExpression(scope.Finally, predicate),
            BoundTryCatch scope => ContainsExpression(scope.Body, predicate)
                || ContainsExpression(scope.Catch, predicate)
                || scope.Finally is not null && ContainsExpression(scope.Finally, predicate),
            BoundWhile loop => ContainsExpression(loop.Condition, predicate)
                || ContainsExpression(loop.Body, predicate)
                || loop.Iterator is not null && ContainsExpression(loop.Iterator, predicate),
            BoundUnsafe unsafeBlock => ContainsExpression(unsafeBlock.Body, predicate),
            BoundViewScope view => ContainsExpression(view.Owner, predicate)
                || ContainsExpression(view.Start, predicate)
                || ContainsExpression(view.Length, predicate)
                || ContainsExpression(view.Body, predicate)
                || view.RangeEnd is not null && ContainsExpression(view.RangeEnd, predicate),
            BoundViewSet view => ContainsExpression(view.Index, predicate)
                || ContainsExpression(view.Value, predicate),
            BoundRequire require => ContainsExpression(require.Condition, predicate),
            BoundReturn result => result.Value is not null && ContainsExpression(result.Value, predicate),
            _ => false,
        };

    private static bool ContainsExpression(BoundExpression expression, Func<BoundExpression, bool> predicate)
    {
        if (predicate(expression))
        {
            return true;
        }
        if (!expression.HasValidNestedExpressions)
        {
            return false;
        }

        foreach (var nested in expression.NestedExpressions)
        {
            if (ContainsExpression(nested, predicate))
            {
                return true;
            }
        }
        return false;
    }

    private static NativeComParameterAdaptation? FindAggregateAddressAdaptation(NativeComMethodDefinition method,
        NativeComMappedBufferDefinition mapped)
    {
        if (method.ParameterAdaptations.IsDefaultOrEmpty)
        {
            return null;
        }

        foreach (var candidate in method.ParameterAdaptations)
        {
            if (candidate.ParameterIndex == mapped.OutputParameterIndex
                && candidate.Kind == NativeComParameterAdaptationKind.AggregateStorageAddress
                && candidate.NoEscape && candidate.SourceType == mapped.SourceType)
            {
                return candidate;
            }
        }

        return null;
    }

    private static bool IsStruct(PrimitiveType type,
        System.Collections.Generic.IReadOnlyList<BoundStruct> structures)
    {
        var index = (int)type - 100;
        return index >= 0 && index < structures.Count;
    }
}
