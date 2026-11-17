// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Terrum.CodeAnalysis.Lexing;
using Terrum.CodeAnalysis.Text;

namespace Terrum.CodeAnalysis.Semantics;

public static partial class SemanticVerifier
{
    private static void VerifyPureEvaluationForms(BoundProgram program)
    {
        var verified = new bool[program.Functions.Length];
        var pureCallGraph = new bool[program.Functions.Length];
        var pending = new Stack<int>();
        var remaining = MaximumAnalysisVisits;
        var firstPureRoot = -1;

        for (var rootIndex = 0; rootIndex < program.Functions.Length;
            rootIndex++)
        {
            var root = program.Functions[rootIndex];
            if (!root.HasPureEvaluationForm)
            {
                continue;
            }
            firstPureRoot = firstPureRoot < 0 ? rootIndex : firstPureRoot;
            pureCallGraph[rootIndex] = true;
            if (root.IsExternal)
            {
                if (!root.IsModuleReference)
                {
                    ThrowPureEvaluationFailure(rootIndex, root.Span);
                }
                continue;
            }

            pending.Push(rootIndex);
            while (pending.TryPop(out var functionIndex))
            {
                if (--remaining < 0)
                {
                    ThrowPureEvaluationLimit(functionIndex,
                        program.Functions[functionIndex].Span);
                }
                if (verified[functionIndex])
                {
                    continue;
                }

                verified[functionIndex] = true;
                var function = program.Functions[functionIndex];
                if (function.IsExternal)
                {
                    ThrowPureEvaluationFailure(functionIndex,
                        function.Span);
                }
                if (function.Intrinsic != BoundIntrinsic.None
                    || function.IndexedBody is not null
                    || function.IsCleanup || function.DoesNotReturn
                    || !IsPureEvaluationResultType(function.ReturnType,
                        program, function))
                {
                    ThrowPureEvaluationFailure(functionIndex,
                        function.Span);
                }

                var firstParameterIndex = function.IsConstructor ? 1 : 0;
                if (function.Variables.IsDefault
                    || function.ParameterCount < 0
                    || firstParameterIndex > function.Variables.Length
                    || function.ParameterCount
                        > function.Variables.Length - firstParameterIndex)
                {
                    ThrowPureEvaluationFailure(functionIndex,
                        function.Span);
                }
                var parameterEnd = firstParameterIndex
                    + function.ParameterCount;
                for (var parameterIndex = firstParameterIndex;
                    parameterIndex < parameterEnd;
                    parameterIndex++)
                {
                    if (!IsPureEvaluationParameter(function,
                        parameterIndex, program))
                    {
                        ThrowPureEvaluationFailure(functionIndex,
                            function.Span);
                    }
                }

                if (!BoundTreeTraversal.VisitStatements(function.Body,
                    statement => {
                        if (--remaining < 0)
                        {
                            ThrowPureEvaluationLimit(functionIndex,
                                statement.Span);
                        }
                        if (!IsPureEvaluationStatement(statement, function,
                            program))
                        {
                            ThrowPureEvaluationFailure(functionIndex,
                                statement.Span);
                        }
                    }))
                {
                    ThrowPureEvaluationFailure(functionIndex,
                        function.Span);
                }

                void VisitExpression(BoundExpression expression)
                {
                    if (--remaining < 0)
                    {
                        ThrowPureEvaluationLimit(functionIndex,
                            expression.Span);
                    }

                    if (expression is BoundCall call)
                    {
                        if (!TryAddPureEvaluationCallee(call, program,
                            function))
                        {
                            ThrowPureEvaluationFailure(functionIndex,
                                call.Span);
                        }
                        pureCallGraph[call.Function] = true;
                    }
                    else if (!IsPureEvaluationExpression(expression,
                        function, program))
                    {
                        ThrowPureEvaluationFailure(functionIndex,
                            expression.Span);
                    }
                }

                if (!BoundTreeTraversal.VisitExpressions(function.Body,
                    VisitExpression)
                    || !BoundTreeTraversal.VisitContractExpressions(
                        function, VisitExpression))
                {
                    ThrowPureEvaluationFailure(functionIndex,
                        function.Span);
                }
            }
        }

        if (firstPureRoot < 0)
        {
            return;
        }

        var proofFunctions = new BoundFunction[program.Functions.Length];
        for (var functionIndex = 0;
            functionIndex < proofFunctions.Length; functionIndex++)
        {
            var function = program.Functions[functionIndex];
            proofFunctions[functionIndex] = pureCallGraph[functionIndex]
                ? function with {
                    NoFailureProof = function.NoFailureProof
                        || !function.IsExternal,
                    IsPureEvaluationProof = true,
                }
                : function with {
                    IsPureEvaluationProof = false,
                };
        }
        var proofProgram = program with {
            Functions = [.. proofFunctions],
        };
        if (!TryVerifySource(proofProgram, out var failureFunctionIndex,
                out var failureSpan, out var exceededWorkLimit,
                out var failureEffect,
                allowGenericProofDependencies: true,
                verifyPureEvaluationForms: false,
                pureEvaluationFailureProof: true))
        {
            var genericMethodIndex
                = failureFunctionIndex - program.Functions.Length;
            var diagnosticFunction = failureFunctionIndex >= 0
                    && (failureFunctionIndex >= program.Functions.Length
                        ? (uint)genericMethodIndex
                            < (uint)program.GenericMethodDefinitions.Length
                        : pureCallGraph[failureFunctionIndex])
                ? failureFunctionIndex : firstPureRoot;
            if (exceededWorkLimit)
            {
                ThrowPureEvaluationLimit(diagnosticFunction, failureSpan);
            }
            ThrowPureEvaluationFailure(diagnosticFunction, failureSpan);
        }
    }

    private static bool TryAddPureEvaluationCallee(BoundCall call,
        BoundProgram program, BoundFunction caller)
    {
        if (call.Function < 0 || call.Function >= program.Functions.Length
            || call.Arguments.IsDefault
            || !IsPureEvaluationResultType(call.Type, program, caller)
            || call.ImportedGenericMethodCall is not null
            || call.InterfaceCallIdentity is not null)
        {
            return false;
        }

        if (call.GenericMethodCall is BoundGenericMethodInstantiation
                instantiation)
        {
            if ((uint)instantiation.DefinitionOrdinal
                    >= (uint)program.GenericMethodDefinitions.Length)
            {
                return false;
            }

            var definition = program.GenericMethodDefinitions[
                instantiation.DefinitionOrdinal];
            if (definition.ProofProgram is not BoundProgram proofProgram)
            {
                return false;
            }
            var proofFunction = (uint)definition.FunctionIndex
                    < (uint)proofProgram.Functions.Length
                ? proofProgram.Functions[definition.FunctionIndex] : null;
            return definition.DefinitionOrdinal
                    == instantiation.DefinitionOrdinal
                && (uint)definition.FunctionIndex
                    < (uint)proofProgram.Functions.Length
                && proofFunction is not null
                && proofFunction.HasPureEvaluationForm;
        }

        var callee = program.Functions[call.Function];
        if (callee.IsExternal)
        {
            var supported = callee.IsModuleReference
                && callee.HasPureEvaluationForm;
            return supported;
        }
        if (callee.Intrinsic != BoundIntrinsic.None
            || callee.IndexedBody is not null)
        {
            return false;
        }
        return callee.HasPureEvaluationForm;
    }

    private static bool IsPureEvaluationStatement(BoundStatement statement,
        BoundFunction function, BoundProgram program)
        => statement switch {
            BoundBlock block => block.RetiredManagedLocals.IsDefaultOrEmpty,
            BoundLocal local => IsPureEvaluationSlot(function,
                local.Variable, program),
            BoundAssignment assignment
                => IsPureLocalWriteSlot(function, assignment.Variable,
                    program),
            BoundFieldAssignment assignment
                => IsPureFieldAssignment(assignment, function, program),
            BoundReturn => IsPureEvaluationResultType(function.ReturnType,
                program, function),
            BoundIf or BoundWhile
                or BoundExpressionStatement or BoundBreak or BoundContinue
                => true,
            _ => false,
        };

    private static bool IsPureEvaluationExpression(
        BoundExpression expression, BoundFunction function,
        BoundProgram program)
        => expression switch {
            BoundConstant constant
                => IsPureEvaluationValueType(constant.Type, program,
                    function),
            BoundContractReturnValue value
                => IsPureEvaluationValueType(value.Type, program, function),
            BoundOutArgument output
                => IsPureOutputValueType(output.Type),
            BoundRead read
                => IsPureEvaluationReadSlot(function, read.Variable, program)
                    && (IsPureEvaluationValueType(read.Type, program,
                            function)
                        || IsPureOutputDescriptorType(read.Type, program)
                        || (IsPureEvaluationParameterSlot(function,
                                read.Variable)
                            && IsPureReadonlyClassInputType(read.Type,
                                program))),
            BoundReferenceWrite referenceWrite
                => IsPureInitializationWrite(referenceWrite, function,
                    program),
            BoundReferenceRead referenceRead
                => IsPureReferenceRead(referenceRead, program),
            BoundFieldRead fieldRead
                => IsPureFieldRead(fieldRead, program),
            BoundUpdate update
                => IsPureEvaluationMutationPlace(update.Place, function,
                    program)
                    && update.Place.Type == update.Type
                    && update.Operator is TokenKind.PlusPlus
                        or TokenKind.MinusMinus
                    && (PrimitiveTypeFacts.IsIntegerOrWide(update.Type)
                        || PrimitiveTypeFacts.IsFloating(update.Type)),
            BoundCompoundAssignment compound
                => IsPureEvaluationMutationPlace(compound.Place, function,
                    program)
                    && compound.Place.Type == compound.Type
                    && compound.Right is not null
                    && CompoundOperators.IsSupported(compound.Operator)
                    && (PrimitiveTypeFacts.IsIntegerOrWide(compound.Type)
                        || PrimitiveTypeFacts.IsFloating(compound.Type))
                    && (CompoundOperators.IsShift(compound.Operator)
                        ? PrimitiveTypeFacts.IsIntegerOrWide(compound.Type)
                            && PrimitiveTypeFacts.IsIntegerOrWide(
                                compound.Right.Type)
                        : compound.Right.Type == compound.Type)
                    && IsPureEvaluationBinary(new BoundBinary(
                        CompoundOperators.Binary(compound.Operator),
                        compound.Place, compound.Right, compound.Type,
                        compound.Span), program),
            BoundCast cast
                => IsPureEvaluationValueType(cast.Operand.Type, program,
                    function)
                    && IsPureEvaluationValueType(cast.Type, program,
                        function),
            BoundUnary unary => IsPureEvaluationUnary(unary, program),
            BoundBinary binary => IsPureEvaluationBinary(binary, program),
            BoundConditional conditional
                => IsPureEvaluationValueType(conditional.Type, program),
            _ => false,
        };

    private static bool IsPureEvaluationSlot(BoundFunction function,
        int variable, BoundProgram program)
        => (uint)variable < (uint)function.Variables.Length
            && IsPureEvaluationValueType(
                function.Variables[variable].Type, program, function)
            && !function.Variables[variable].IsOut
            && !function.Variables[variable].IsInit
            && !function.Variables[variable].IsImplicitRef
            && !function.Variables[variable].IsInitializationLocal
            && !function.Variables[variable].IsImplicitReferenceTemporary;

    private static bool IsPureEvaluationParameter(BoundFunction function,
        int variable, BoundProgram program)
    {
        if (variable < (function.IsConstructor ? 1 : 0)
            || variable >= (function.IsConstructor ? 1 : 0)
                + function.ParameterCount
            || (uint)variable >= (uint)function.Variables.Length)
        {
            return false;
        }

        var parameter = function.Variables[variable];
        if (parameter.IsInit)
        {
            return IsPureInitializationReferenceType(
                parameter.Type, program)
                || IsPureOutputValueType(parameter.Type);
        }
        if (parameter.IsOut)
        {
            return parameter.IsMutable
                && IsPureOutputValueType(parameter.Type);
        }

        return !parameter.IsInitializationLocal
            && !parameter.IsImplicitReferenceTemporary
            && (IsPureOutputDescriptorType(parameter.Type, program)
                || IsPureReadonlyClassInputType(parameter.Type, program)
                || IsPureEvaluationValueType(parameter.Type, program,
                    function));
    }

    private static bool IsPureEvaluationReadSlot(BoundFunction function,
        int variable, BoundProgram program)
    {
        if ((uint)variable >= (uint)function.Variables.Length)
        {
            return false;
        }

        var slot = function.Variables[variable];
        if (slot.IsInit)
        {
            return IsPureInitializationReferenceType(slot.Type, program)
                || IsPureOutputValueType(slot.Type);
        }
        if (slot.IsOut)
        {
            return IsPureOutputValueType(slot.Type);
        }

        return !slot.IsInitializationLocal
            && !slot.IsImplicitReferenceTemporary
            && (IsPureOutputDescriptorType(slot.Type, program)
                || (IsPureEvaluationParameterSlot(function, variable)
                    && IsPureReadonlyClassInputType(slot.Type, program))
                || IsPureEvaluationValueType(slot.Type, program,
                    function));
    }

    private static bool IsPureEvaluationParameterSlot(
        BoundFunction function, int variable)
    {
        var firstParameterIndex = function.IsConstructor ? 1 : 0;
        var parameterEnd = firstParameterIndex + function.ParameterCount;
        return variable >= firstParameterIndex && variable < parameterEnd;
    }

    private static bool IsPureReadonlyClassInputType(PrimitiveType type,
        BoundProgram program)
    {
        if (!ManagedClassTypes.IsClass(type)
            || ManagedClassTypes.IsWritable(type))
        {
            return false;
        }

        var classIndex = ManagedClassTypes.ClassIndex(type);
        return (uint)classIndex < (uint)program.Classes.Length;
    }

    private static bool IsPureEvaluationResultType(PrimitiveType type,
        BoundProgram program, BoundFunction? function = null)
        => type == PrimitiveType.Void
            || IsPureEvaluationValueType(type, program, function);

    private static bool IsPureOutputValueType(PrimitiveType type)
        => IsPureEvaluationScalarType(type);

    private static bool IsPureInitializationReferenceType(
        PrimitiveType type, BoundProgram program)
    {
        if (!CheckedReferenceTypes.IsType(type))
        {
            return false;
        }

        var referenceIndex = CheckedReferenceTypes.Index(type);
        if ((uint)referenceIndex
            >= (uint)program.CheckedReferenceTypes.Length)
        {
            return false;
        }

        var reference = program.CheckedReferenceTypes[referenceIndex];
        return reference.IsInitialization && !reference.IsOptional
            && !reference.IsOptionalValue
            && IsPureOutputValueType(reference.ReferentType);
    }

    private static bool IsPureOutputDescriptorType(PrimitiveType type,
        BoundProgram program)
        => IsPureInitializationReferenceType(type, program);

    private static bool IsPureEvaluationValueType(PrimitiveType type,
        BoundProgram program)
        => IsPureEvaluationValueType(type, program, null);

    private static bool IsPureEvaluationValueType(PrimitiveType type,
        BoundProgram program, BoundFunction? function)
        => IsPureEvaluationValueType(type, program, function,
            new HashSet<PrimitiveType>(), 0);

    private static bool IsPureEvaluationScalarType(PrimitiveType type)
        => type is PrimitiveType.Bool or PrimitiveType.Char8
            or PrimitiveType.Char16 or PrimitiveType.Char32
            or PrimitiveType.Rune or PrimitiveType.Int8
            or PrimitiveType.Int16 or PrimitiveType.Int32
            or PrimitiveType.Int64 or PrimitiveType.Int128
            or PrimitiveType.NativeInt or PrimitiveType.UInt8
            or PrimitiveType.UInt16 or PrimitiveType.UInt32
            or PrimitiveType.UInt64 or PrimitiveType.UInt128
            or PrimitiveType.NativeUInt or PrimitiveType.Float16
            or PrimitiveType.BFloat16 or PrimitiveType.Float32
            or PrimitiveType.Float64 or PrimitiveType.Float128
            or PrimitiveType.Decimal32 or PrimitiveType.Decimal64
            or PrimitiveType.Decimal128
            || BoundEnumTypes.IsType(type);

    private static bool IsPureEvaluationValueType(PrimitiveType type,
        BoundProgram program, HashSet<PrimitiveType> active, int depth)
        => IsPureEvaluationValueType(type, program, null, active, depth);

    private static bool IsPureEvaluationValueType(PrimitiveType type,
        BoundProgram program, BoundFunction? function,
        HashSet<PrimitiveType> active, int depth)
    {
        if (IsPureEvaluationScalarType(type))
        {
            return true;
        }

        if (function is not null
            && TryGetCallerGenericTypeFormal(function, program, type,
                out var formal, out _)
            && formal is not null
            && formal.OwnershipDomain == 1)
        {
            return true;
        }

        if (depth > MaximumDepth || !active.Add(type))
        {
            return false;
        }

        try
        {
            if (CheckedReferenceTypes.IsType(type))
            {
                var referenceIndex = CheckedReferenceTypes.Index(type);
                if ((uint)referenceIndex
                        >= (uint)program.CheckedReferenceTypes.Length)
                {
                    return false;
                }

                var reference
                    = program.CheckedReferenceTypes[referenceIndex];
                return !reference.IsInitialization
                    && !reference.IsOptional
                    && !reference.IsOptionalValue
                    && IsPureEvaluationValueType(
                        reference.ReferentType, program, function, active,
                        depth + 1);
            }

            return IsPureCopyStructure(type, program, active, function);
        }
        finally
        {
            _ = active.Remove(type);
        }
    }

    private static bool IsPureReferenceRead(BoundReferenceRead referenceRead,
        BoundProgram program)
    {
        if (!CheckedReferenceTypes.IsType(referenceRead.Descriptor.Type))
        {
            return false;
        }

        var referenceIndex = CheckedReferenceTypes.Index(
            referenceRead.Descriptor.Type);
        if ((uint)referenceIndex
            >= (uint)program.CheckedReferenceTypes.Length)
        {
            return false;
        }

        var reference = program.CheckedReferenceTypes[referenceIndex];
        return !reference.IsInitialization && !reference.IsOptional
            && !reference.IsOptionalValue
            && reference.ReferentType == referenceRead.Type
            && IsPureEvaluationValueType(referenceRead.Type, program);
    }

    private static bool IsPureInitializationWrite(
        BoundReferenceWrite referenceWrite, BoundFunction function,
        BoundProgram program)
    {
        if (referenceWrite.Descriptor is not BoundRead descriptor
            || (uint)descriptor.Variable
                >= (uint)function.Variables.Length)
        {
            return false;
        }

        var parameter = function.Variables[descriptor.Variable];
        return parameter.Type == descriptor.Type
            && IsPureOutputDescriptorType(parameter.Type, program)
            && program.CheckedReferenceTypes[
                CheckedReferenceTypes.Index(parameter.Type)]
                    .ReferentType == referenceWrite.Value.Type
            && IsPureOutputValueType(referenceWrite.Value.Type);
    }

    private static bool IsPureCopyStructure(PrimitiveType type,
        BoundProgram program, HashSet<PrimitiveType> active,
        BoundFunction? function)
    {
        if (ManagedClassTypes.IsManaged(type)
            || NativePointerTypes.IsType(type)
            || BoundEnumTypes.IsType(type))
        {
            return false;
        }

        var structureIndex = (int)type - 100;
        if ((uint)structureIndex >= (uint)program.Structures.Length)
        {
            return false;
        }

        var structure = program.Structures[structureIndex];
        if (structure.IsUnique || structure.IsUnion
            || structure.CleanupMethod is not null
            || structure.Fields.IsDefault)
        {
            return false;
        }

        foreach (var field in structure.Fields)
        {
            if (field.RequiresUnsafeCaller
                || !IsPureEvaluationValueType(field.Type, program, function,
                    active, active.Count))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsPureFieldRead(BoundFieldRead fieldRead,
        BoundProgram program)
    {
        if (ManagedClassTypes.IsClass(fieldRead.Receiver.Type))
        {
            if (!IsPureReadonlyClassInputType(
                    fieldRead.Receiver.Type, program)
                || fieldRead.IsUnsafeAuthorized)
            {
                return false;
            }

            var classIndex = ManagedClassTypes.ClassIndex(
                fieldRead.Receiver.Type);
            var fields = program.Classes[classIndex].Fields;
            return (uint)fieldRead.Field < (uint)fields.Length
                && !fields[fieldRead.Field].RequiresUnsafeCaller
                && fields[fieldRead.Field].Type == fieldRead.Type
                && IsPureEvaluationValueType(fieldRead.Type, program);
        }

        var structureIndex = (int)fieldRead.Receiver.Type - 100;
        return IsPureEvaluationValueType(fieldRead.Receiver.Type, program)
            && (uint)structureIndex < (uint)program.Structures.Length
            && IsPureEvaluationValueType(fieldRead.Type, program)
            && fieldRead.Field >= 0
            && fieldRead.Field
                < program.Structures[structureIndex].Fields.Length
            && program.Structures[structureIndex]
                .Fields[fieldRead.Field].Type == fieldRead.Type;
    }

    private static bool IsPureFieldAssignment(
        BoundFieldAssignment assignment, BoundFunction function,
        BoundProgram program)
    {
        if (assignment.NestedFields.IsDefault)
        {
            return false;
        }

        return IsPureWritableFieldPath(function, assignment.Variable,
            assignment.Field, assignment.NestedFields, program);
    }

    private static bool IsPureWritableFieldPath(
        BoundFunction function, int variable, int firstField,
        ImmutableArray<int> nestedFields, BoundProgram program)
    {
        if ((uint)variable >= (uint)function.Variables.Length
            || !IsPureLocalWriteSlot(function, variable, program)
            || nestedFields.IsDefault)
        {
            return false;
        }

        var ownerType = function.Variables[variable].Type;
        for (var pathIndex = 0;
            pathIndex <= nestedFields.Length;
            pathIndex++)
        {
            var fieldIndex = pathIndex == 0 ? firstField
                : nestedFields[pathIndex - 1];
            var structureIndex = (int)ownerType - 100;
            if ((uint)structureIndex >= (uint)program.Structures.Length
                || fieldIndex < 0
                || fieldIndex
                    >= program.Structures[structureIndex].Fields.Length)
            {
                return false;
            }

            var field = program.Structures[structureIndex]
                .Fields[fieldIndex];
            if (!field.IsMutable
                && !(function.IsConstructor
                    && variable == 0))
            {
                return false;
            }
            ownerType = field.Type;
        }

        return IsPureEvaluationValueType(ownerType, program);
    }

    private static bool IsPureEvaluationMutationPlace(
        BoundExpression place, BoundFunction function,
        BoundProgram program)
    {
        if (place is BoundRead read)
        {
            return IsPureLocalWriteSlot(function, read.Variable, program);
        }

        var fields = new List<int>();
        var root = place;
        while (root is BoundFieldRead field)
        {
            fields.Add(field.Field);
            root = field.Receiver;
        }
        if (root is not BoundRead owner || fields.Count == 0)
        {
            return false;
        }

        fields.Reverse();
        var nestedFields = ImmutableArray.CreateBuilder<int>(
            fields.Count - 1);
        for (var index = 1; index < fields.Count; index++)
        {
            nestedFields.Add(fields[index]);
        }
        return IsPureWritableFieldPath(function, owner.Variable,
            fields[0], nestedFields.MoveToImmutable(), program);
    }

    private static bool IsPureLocalWriteSlot(BoundFunction function,
        int variable, BoundProgram program)
    {
        if ((uint)variable >= (uint)function.Variables.Length)
        {
            return false;
        }

        var slot = function.Variables[variable];
        if (slot.IsInit)
        {
            return IsPureOutputValueType(slot.Type);
        }
        if (slot.IsOut)
        {
            return !slot.IsInit && slot.IsMutable
                && IsPureOutputValueType(slot.Type);
        }

        return IsPureEvaluationSlot(function, variable, program)
            && slot.IsMutable
            && (variable != 0 || function.DeclaringStruct is null
                || function.IsConstructor);
    }

    private static bool IsPureEvaluationUnary(BoundUnary unary,
        BoundProgram program)
    {
        if (unary.Operator == TokenKind.Bang)
        {
            return unary.Type == PrimitiveType.Bool;
        }
        if (unary.Operator == TokenKind.Tilde)
        {
            return PrimitiveTypeFacts.IsIntegerOrWide(unary.Type);
        }
        if (unary.Operator == TokenKind.Plus)
        {
            return IsPureEvaluationValueType(unary.Type, program);
        }
        if (unary.Operator != TokenKind.Minus)
        {
            return false;
        }

        return PrimitiveTypeFacts.IsFloating(unary.Type)
            || PrimitiveTypeFacts.IsIntegerOrWide(unary.Type);
    }

    private static bool IsPureEvaluationBinary(BoundBinary binary,
        BoundProgram program)
    {
        if (!IsPureEvaluationValueType(binary.Left.Type, program)
            || !IsPureEvaluationValueType(binary.Right.Type, program)
            || !IsPureEvaluationValueType(binary.Type, program))
        {
            return false;
        }

        if (binary.Operator is TokenKind.EqualsEquals
            or TokenKind.BangEquals or TokenKind.Less
            or TokenKind.LessEquals or TokenKind.Greater
            or TokenKind.GreaterEquals)
        {
            return binary.Type == PrimitiveType.Bool;
        }
        if (binary.Operator is TokenKind.AmpersandAmpersand
                or TokenKind.PipePipe)
        {
            return binary.Type == PrimitiveType.Bool
                && binary.Left.Type == PrimitiveType.Bool
                && binary.Right.Type == PrimitiveType.Bool;
        }
        if (binary.Operator is TokenKind.Ampersand
                or TokenKind.Pipe or TokenKind.Caret)
        {
            return PrimitiveTypeFacts.IsIntegerOrWide(binary.Type);
        }

        return binary.Operator is TokenKind.Plus or TokenKind.Minus
                or TokenKind.Star or TokenKind.Slash or TokenKind.Percent
                or TokenKind.ShiftLeft or TokenKind.ShiftRight
                or TokenKind.ShiftRightUnsigned
            && (PrimitiveTypeFacts.IsIntegerOrWide(binary.Type)
                || (PrimitiveTypeFacts.IsFloating(binary.Type)
                    && binary.Operator is TokenKind.Plus or TokenKind.Minus
                        or TokenKind.Star or TokenKind.Slash
                        or TokenKind.Percent));
    }

    private static void ThrowPureEvaluationFailure(int functionIndex,
        TextSpan span)
    {
        var exception = InvalidEffect(span, "PureEvaluation");
        exception.Data[FunctionIndexKey] = functionIndex;
        throw exception;
    }

    private static void ThrowPureEvaluationLimit(int functionIndex,
        TextSpan span)
    {
        var exception = Limit();
        exception.Data[FunctionIndexKey] = functionIndex;
        exception.Data[SourceSpanKey] = span;
        throw exception;
    }
}
