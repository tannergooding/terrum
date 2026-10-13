// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System.Collections.Generic;
using System.Collections.Immutable;

namespace Terrum.CodeAnalysis.Semantics;

public static partial class SemanticVerifier
{
    private static void VerifyNativeStorageRecordInitialization(BoundExpression expression, int variable,
        InitializationState initialized, uint[] fieldMasks, HashSet<int> declarations,
        BoundFunction function, BoundProgram program, ref int remaining, int depth, FailureEdges? failures)
    {
        Count(ref remaining, depth);
        if (expression is not BoundNativeStorageRecordInitialization record
            || NativeStorageRecordContract.Facts(record, program.NativeImportCatalog)
                is not NativeStorageRecordFacts facts
            || record.Type != PrimitiveType.NativeStorageRecord
            || variable < function.ParameterCount + (function.IsConstructor ? 1 : 0)
            || variable >= function.Variables.Length
            || function.Variables[variable].IsMutable
            || function.Variables[variable].NativeStorageRecordLayout != record.Layout
            || function.Variables[variable].NativeStorageRecordFacts != facts
            || !NativeStorageRecordContract.MatchesLayout(record.Layout, program.NativeImportCatalog))
        {
            throw InvalidEffect(expression.Span, "NativeImport");
        }

        VerifyNativeStorageRecordBacking(record.DataSource, facts.Loan, initialized, fieldMasks,
            declarations, function, program, ref remaining, depth + 1, failures);
        foreach (var field in record.Fields)
        {
            Expression(field, initialized, fieldMasks, declarations, function, program,
                ref remaining, depth + 1, failures);
        }
    }

    private static NativeStorageRecordFacts VerifyNativeStorageRecordAddress(BoundExpression expression,
        NativeStorageRecordParameterDefinition parameter, InitializationState initialized,
        uint[] fieldMasks, HashSet<int> declarations, BoundFunction function, BoundProgram program,
        ref int remaining, int depth, FailureEdges? failures)
    {
        Count(ref remaining, depth);
        if (expression is not BoundNativeStorageAddress address
            || address.Type != PrimitiveType.NativeStorageAddress
            || address.AddressType != PrimitiveType.NativeStorageAddress
            || address.CarrierType != PrimitiveType.NativeStorageRecord
            || address.ElementType != PrimitiveType.NativeStorageRecord
            || address.ElementCount != 1
            || address.ElementStride <= 0
            || address.ByteExtent != address.ElementStride
            || !address.IsReadonly
            || address.Index is not BoundConstant { Type: PrimitiveType.Int32, Value: 0 }
            || address.Variable < function.ParameterCount + (function.IsConstructor ? 1 : 0)
            || address.Variable >= function.Variables.Length
            || !declarations.Contains(address.Variable) || !initialized[address.Variable]
            || function.Variables[address.Variable].IsMutable
            || function.Variables[address.Variable].Type != PrimitiveType.NativeStorageRecord
            || function.Variables[address.Variable].NativeStorageRecordLayout
                is not NativeStorageRecordLayoutDefinition layout
            || layout.TypeName != parameter.TypeName
            || layout.ByteExtent != address.ByteExtent
            || function.Variables[address.Variable].NativeStorageRecordFacts
                is not NativeStorageRecordFacts facts
            || FindNativeStorageRecordInitialization(function.Body, address.Variable,
                ref remaining, depth + 1) is not BoundNativeStorageRecordInitialization initializer
            || initializer.Layout != layout
            || NativeStorageRecordContract.Facts(initializer, program.NativeImportCatalog) is not NativeStorageRecordFacts initializedFacts
            || initializedFacts != facts
            || facts.Loan.IsReadonly is false)
        {
            throw InvalidEffect(expression?.Span ?? default, "NativeImport");
        }

        VerifyNativeStorageRecordBacking(initializer.DataSource, facts.Loan, initialized, fieldMasks,
            declarations, function, program, ref remaining, depth + 1, failures);
        return facts;
    }

    private static void VerifyNativeStorageRecordBacking(BoundExpression source, NativeStorageLoan loan,
        InitializationState initialized, uint[] fieldMasks, HashSet<int> declarations,
        BoundFunction function, BoundProgram program, ref int remaining, int depth, FailureEdges? failures)
    {
        if (!loan.IsReadonly || source is not BoundNativeStorageAddress {
            Type: PrimitiveType.NativeStorageAddress,
            AddressType: PrimitiveType.NativeStorageAddress,
            CarrierType: PrimitiveType.NativeFixedArray,
            Index: BoundConstant { Type: PrimitiveType.Int32, Value: 0 },
            IsReadonly: true,
        } address
            || address.Variable != loan.BackingVariable
            || address.ElementType != loan.ElementType
            || address.ByteExtent != loan.ByteExtent
            || address.CarrierType != loan.CarrierType
            || (uint)address.Variable >= (uint)function.Variables.Length
            || function.Variables[address.Variable].NativeFixedArrayLayout
                is not BoundNativeFixedArrayLayout layout
            || layout.ElementType != loan.ElementType
            || layout.ByteExtent != loan.ByteExtent
            || !NativeFixedArrayContract.MatchesLayout(layout, program.Structures,
                program.NativeImportCatalog)
            || !NativeStorageParameterContract.SupportsFixedArrayParameter(program.NativeImportCatalog,
                layout, program.Structures, PrimitiveType.NativeStorageAddress, isReadonly: true))
        {
            throw InvalidEffect(source.Span, "NativeImport");
        }

        VerifyNativeFixedArrayPlace(address.Variable, address.Index, address.CarrierType,
            0, true, initialized, fieldMasks, declarations, function, program,
            ref remaining, depth + 1, failures);
    }

    private static BoundNativeStorageRecordInitialization? FindNativeStorageRecordInitialization(
        BoundStatement statement, int variable, ref int remaining, int depth)
    {
        Count(ref remaining, depth);
        switch (statement)
        {
            case BoundLocal local when local.Variable == variable:
                return local.Initializer as BoundNativeStorageRecordInitialization;
            case BoundBlock block:
                foreach (var child in block.Statements)
                {
                    if (FindNativeStorageRecordInitialization(child, variable, ref remaining, depth + 1)
                        is BoundNativeStorageRecordInitialization initializer)
                    {
                        return initializer;
                    }
                }
                break;
            case BoundIf conditional:
                return FindNativeStorageRecordInitialization(conditional.Then, variable, ref remaining, depth + 1)
                    ?? (conditional.Else is null ? null : FindNativeStorageRecordInitialization(
                        conditional.Else, variable, ref remaining, depth + 1));
            case BoundWhile loop:
                return FindNativeStorageRecordInitialization(loop.Body, variable, ref remaining, depth + 1)
                    ?? (loop.Iterator is null ? null : FindNativeStorageRecordInitialization(
                        loop.Iterator, variable, ref remaining, depth + 1));
            case BoundTryFinally cleanup:
                return FindNativeStorageRecordInitialization(cleanup.Body, variable, ref remaining, depth + 1)
                    ?? FindNativeStorageRecordInitialization(cleanup.Finally, variable, ref remaining, depth + 1);
            case BoundTryCatch recovery:
                return FindNativeStorageRecordInitialization(recovery.Body, variable, ref remaining, depth + 1)
                    ?? FindNativeStorageRecordInitialization(recovery.Catch, variable, ref remaining, depth + 1)
                    ?? (recovery.Finally is null ? null : FindNativeStorageRecordInitialization(
                        recovery.Finally, variable, ref remaining, depth + 1));
            case BoundUnsafe unsafeBlock:
                return FindNativeStorageRecordInitialization(unsafeBlock.Body, variable,
                    ref remaining, depth + 1);
            case BoundViewScope view:
                return FindNativeStorageRecordInitialization(view.Body, variable,
                    ref remaining, depth + 1);
        }

        return null;
    }

    private static void VerifyNativeStorageRecordLoanGroups(BoundCall call,
        ImmutableArray<NativeStorageRecordParameterDefinition> parameters,
        IReadOnlyDictionary<string, NativeStorageLoan> loans)
    {
        if (parameters.IsDefaultOrEmpty)
        {
            return;
        }

        foreach (var parameter in parameters)
        {
            if (parameter?.LoanGroup is string group && !loans.ContainsKey(group))
            {
                throw InvalidEffect(call.Span, "NativeImport");
            }
        }
    }
}
