// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Terrum.CodeAnalysis.Semantics;

namespace Terrum.CodeAnalysis.Modules;

public static partial class ModuleCompiler
{
    private readonly record struct PrivateImplicitReferenceFormal(
        PrimitiveType ValueType, PrimitiveType DescriptorType);

    private static BoundProgram CanonicalizePrivateImplicitReferences(
        BoundProgram program)
    {
        var referenceTypes = program.CheckedReferenceTypes.IsDefault
            ? [] : program.CheckedReferenceTypes.ToList();
        var functions = program.Functions.ToBuilder();
        var sourceFormals = new Dictionary<int,
            PrivateImplicitReferenceFormal>?[functions.Count];
        var requiresRewrite = false;

        for (var functionIndex = 0; functionIndex < functions.Count;
            functionIndex++)
        {
            var function = functions[functionIndex];
            var variables = function.Variables.ToBuilder();
            Dictionary<int, PrivateImplicitReferenceFormal>? formals = null;
            for (var variableIndex = 0;
                variableIndex < variables.Count; variableIndex++)
            {
                var variable = variables[variableIndex];
                if (!variable.IsImplicitRef)
                {
                    if (variable.IsCanonicalImplicitReference)
                    {
                        throw new ModuleContractException(
                            "A non-implicit formal carries canonical implicit-reference state.");
                    }
                    continue;
                }

                if (variable.IsCanonicalImplicitReference)
                {
                    if (!CheckedReferenceTypeContract
                        .IsCanonicalImplicitReferenceType(variable.Type,
                            program.Structures, referenceTypes))
                    {
                        throw new ModuleContractException(
                            "A canonical implicit-reference formal has an invalid descriptor type.");
                    }
                    continue;
                }

                var valueType = variable.Type;
                if (!CheckedReferenceTypeContract.IsImplicitReferenceValueType(
                        valueType, program.Structures, referenceTypes))
                {
                    throw new ModuleContractException(
                        "The source implicit-reference formal has no admitted logical value type.");
                }

                var descriptorType = FindOrAddPrivateReferenceType(
                    referenceTypes, valueType);
                variables[variableIndex] = variable with {
                    Type = descriptorType,
                    IsCanonicalImplicitReference = true,
                };
                (formals ??= new Dictionary<int,
                    PrivateImplicitReferenceFormal>()).Add(variableIndex,
                    new PrivateImplicitReferenceFormal(valueType,
                        descriptorType));
                requiresRewrite = true;
            }

            if (formals is not null)
            {
                sourceFormals[functionIndex] = formals;
                functions[functionIndex] = function with {
                    Variables = variables.ToImmutable(),
                };
            }
            if (!BoundTreeTraversal.VisitExpressions(function.Body,
                    expression => {
                        if (expression is BoundCall call
                            && (((uint)call.Function < (uint)functions.Count
                                    && functions[call.Function].Variables.Any(
                                        static variable => variable.IsImplicitRef))
                                || !call.ArgumentImplicitReferenceTemporaries
                                    .IsDefault))
                        {
                            requiresRewrite = true;
                        }
                    }))
            {
                throw new ModuleContractException(
                    "The source program contains an invalid implicit-reference body.");
            }
        }

        if (!requiresRewrite)
        {
            return program;
        }

        var canonicalProgram = program with {
            Functions = functions.ToImmutable(),
            CheckedReferenceTypes = [.. referenceTypes],
        };
        if (!CheckedReferenceTypeContract.IsValidTable(
                canonicalProgram.CheckedReferenceTypes))
        {
            throw new ModuleContractException(
                "Canonical implicit-reference types are invalid.");
        }

        var rewrittenFunctions = canonicalProgram.Functions.ToBuilder();
        for (var functionIndex = 0;
            functionIndex < rewrittenFunctions.Count; functionIndex++)
        {
            var callerFormals = sourceFormals[functionIndex];
            var function = rewrittenFunctions[functionIndex];
            rewrittenFunctions[functionIndex]
                = BoundTreeTraversal.RewriteExpressions(function,
                    expression => CanonicalizePrivateImplicitExpression(
                        expression, callerFormals, function,
                        canonicalProgram));
        }

        return canonicalProgram with {
            Functions = rewrittenFunctions.ToImmutable(),
        };
    }

    private static PrimitiveType FindOrAddPrivateReferenceType(
        List<CheckedReferenceTypeDefinition> referenceTypes,
        PrimitiveType valueType)
    {
        var definition = new CheckedReferenceTypeDefinition(valueType,
            IsWritable: false, IsGarbageCollected: false,
            IsInitialization: false, IsOptional: false);
        for (var index = 0; index < referenceTypes.Count; index++)
        {
            if (referenceTypes[index] == definition)
            {
                return CheckedReferenceTypes.Type(index);
            }
        }

        if (referenceTypes.Count >= SemanticVerifier.MaximumAnalysisVisits)
        {
            throw new ModuleContractException(
                "Canonical implicit-reference type table exceeds the supported bound.");
        }

        var type = CheckedReferenceTypes.Type(referenceTypes.Count);
        referenceTypes.Add(definition);
        return type;
    }

    private static BoundExpression CanonicalizePrivateImplicitExpression(
        BoundExpression expression,
        IReadOnlyDictionary<int, PrivateImplicitReferenceFormal>? callerFormals,
        BoundFunction caller,
        BoundProgram program)
    {
        if (expression is BoundMove move
            && callerFormals is not null
            && callerFormals.TryGetValue(move.Variable,
                out var movedFormal)
            && move.Type == movedFormal.ValueType)
        {
            return new BoundReferenceRead(
                new BoundRead(move.Variable, movedFormal.DescriptorType,
                    move.Span),
                movedFormal.ValueType, move.Span);
        }

        if (expression is BoundFieldMove fieldMove
            && callerFormals is not null
            && PlaceUsesImplicitReferenceValue(fieldMove.Place,
                callerFormals))
        {
            return fieldMove.Place with {
                Type = fieldMove.Type,
                Span = fieldMove.Span,
            };
        }

        if (expression is BoundRead read
            && callerFormals is not null
            && callerFormals.TryGetValue(read.Variable,
                out var sourceFormal)
            && read.Type == sourceFormal.ValueType)
        {
            return new BoundReferenceRead(
                new BoundRead(read.Variable, sourceFormal.DescriptorType,
                    read.Span),
                sourceFormal.ValueType, read.Span);
        }

        if (expression is BoundReferenceFormation formation
            && TryCreatePrivatePlace(formation.Place, caller, program,
                out var formationPlace))
        {
            var kind = PrivatePlaceHasScopedProvenance(formationPlace)
                ? (byte)2 : (byte)1;
            return new BoundPrivateReferenceForm(kind, formationPlace,
                formation.Type, formation.Span);
        }

        if (expression is not BoundCall call)
        {
            return expression;
        }

        if ((uint)call.Function >= (uint)program.Functions.Length
            || call.Arguments.IsDefault
            || (!call.ArgumentParameterIndices.IsDefault
                && call.ArgumentParameterIndices.Length
                    != call.Arguments.Length)
            || (!call.ArgumentImplicitReferenceTemporaries.IsDefault
                && call.ArgumentImplicitReferenceTemporaries.Length
                    != call.Arguments.Length))
        {
            throw new ModuleContractException(
                "Implicit-reference call has invalid target or argument mappings.");
        }

        var target = program.Functions[call.Function];
        var arguments = call.Arguments.ToBuilder();
        for (var argumentIndex = 0;
            argumentIndex < call.Arguments.Length; argumentIndex++)
        {
            var parameterIndex = call.ParameterIndex(argumentIndex);
            if ((uint)parameterIndex >= (uint)target.ParameterCount)
            {
                throw new ModuleContractException(
                    "Implicit-reference call names an invalid formal ordinal.");
            }

            var formalSlot = parameterIndex
                + (target.IsConstructor ? 1 : 0);
            var formal = target.Variables[formalSlot];
            var temporary = call.ImplicitReferenceTemporary(argumentIndex);
            if (temporary < -1)
            {
                throw new ModuleContractException(
                    "Implicit-reference call has an invalid backing index.");
            }
            if (!formal.IsImplicitRef)
            {
                if (temporary >= 0)
                {
                    throw new ModuleContractException(
                        "A non-implicit formal has an implicit-reference backing.");
                }
                continue;
            }

            if (!formal.IsCanonicalImplicitReference
                || !CheckedReferenceTypeContract
                    .IsCanonicalImplicitReferenceType(formal.Type,
                        program.Structures,
                        program.CheckedReferenceTypes))
            {
                throw new ModuleContractException(
                    "Implicit-reference call target is not canonical.");
            }

            var valueType = program.CheckedReferenceTypes[
                CheckedReferenceTypes.Index(formal.Type)].ReferentType;
            var argument = arguments[argumentIndex];
            if (temporary >= 0)
            {
                if ((uint)temporary >= (uint)caller.Variables.Length
                    || temporary < caller.ParameterCount
                        + (caller.IsConstructor ? 1 : 0)
                    || !caller.Variables[temporary]
                        .IsImplicitReferenceTemporary
                    || !caller.Variables[temporary].IsInitializationLocal
                    || !caller.Variables[temporary].IsMutable
                    || caller.Variables[temporary].Type != valueType
                    || argument.Type != valueType)
                {
                    throw new ModuleContractException(
                        "Implicit-reference materialization has inconsistent value or backing identity.");
                }
                arguments[argumentIndex]
                    = new BoundImplicitReferenceMaterialize(temporary,
                        argument, formal.Type, argument.Span);
                continue;
            }

            if (argument.Type == formal.Type)
            {
                continue;
            }
            if (CheckedReferenceTypeContract.IsValidType(argument.Type,
                    program.CheckedReferenceTypes))
            {
                var argumentReference = program.CheckedReferenceTypes[
                    CheckedReferenceTypes.Index(argument.Type)];
                if (!argumentReference.IsInitialization
                    && !argumentReference.IsGarbageCollected
                    && !argumentReference.IsOptional
                    && argumentReference.ReferentType == valueType)
                {
                    arguments[argumentIndex]
                        = new BoundReadonlyProjection(argument,
                            formal.Type, argument.Span);
                    continue;
                }
            }
            if (argument is BoundReferenceRead referenceRead
                && referenceRead.Type == valueType
                && referenceRead.Descriptor.Type == formal.Type)
            {
                arguments[argumentIndex] = referenceRead.Descriptor;
                continue;
            }
            if (argument.Type != valueType
                || !TryCreatePrivatePlace(argument, caller, program,
                    out var place)
                || place.Type != valueType)
            {
                throw new ModuleContractException(
                    "Implicit-reference adaptation has neither an exact descriptor nor an admitted place.");
            }

            var formationKind = PrivatePlaceHasScopedProvenance(place)
                ? (byte)2 : (byte)1;
            arguments[argumentIndex] = new BoundPrivateReferenceForm(
                formationKind, place, formal.Type, argument.Span);
        }

        return call with {
            Arguments = arguments.ToImmutable(),
            ArgumentImplicitReferenceTemporaries = default,
        };
    }

    private static bool PlaceUsesImplicitReferenceValue(
        BoundExpression expression,
        IReadOnlyDictionary<int, PrivateImplicitReferenceFormal> formals) =>
        expression switch {
            BoundRead read => formals.ContainsKey(read.Variable),
            BoundReferenceRead referenceRead =>
                referenceRead.Descriptor is BoundRead descriptor
                && formals.ContainsKey(descriptor.Variable),
            BoundFieldRead field =>
                PlaceUsesImplicitReferenceValue(field.Receiver, formals),
            _ => false,
        };

    private static bool TryCreatePrivatePlace(BoundExpression expression,
        BoundFunction function, BoundProgram program,
        out BoundPrivatePlace place)
    {
        switch (expression)
        {
            case BoundRead read
                when (uint)read.Variable < (uint)function.Variables.Length
                    && function.Variables[read.Variable].Type == read.Type:
                place = new BoundPrivateLocalPlace(read.Variable,
                    read.Type);
                return true;
            case BoundReferenceRead referenceRead
                when CheckedReferenceTypeContract.IsValidType(
                    referenceRead.Descriptor.Type,
                    program.CheckedReferenceTypes)
                    && program.CheckedReferenceTypes[
                        CheckedReferenceTypes.Index(
                            referenceRead.Descriptor.Type)]
                        .ReferentType == referenceRead.Type:
                place = new BoundPrivateReferentPlace(
                    referenceRead.Descriptor, referenceRead.Type);
                return true;
            case BoundFieldRead field:
                if (ManagedClassTypes.IsClass(field.Receiver.Type))
                {
                    place = new BoundPrivateObjectFieldPlace(
                        field.Receiver.Type, (uint)field.Field,
                        field.Receiver, field.Type);
                    return true;
                }
                if (!TryCreatePrivatePlace(field.Receiver, function,
                        program,
                        out var receiver)
                    || receiver.Type != field.Receiver.Type)
                {
                    break;
                }
                place = new BoundPrivateInlineFieldPlace(
                    field.Receiver.Type, (uint)field.Field, receiver,
                    field.Type);
                return true;
            case BoundBufferRead buffer:
                place = new BoundPrivateInvocationBufferElementPlace(
                    buffer.Buffer, buffer.Index, buffer.Type);
                return true;
            case BoundViewRead view:
                place = new BoundPrivateLexicalViewElementPlace(
                    view.Variable, view.Index, view.Type);
                return true;
            case BoundNativeFixedArrayElement element:
            {
                var arrayExpression = element.FieldPlace
                    ?? new BoundRead(element.Variable,
                        element.Layout.CarrierType, element.Span);
                if (TryCreatePrivatePlace(arrayExpression, function, program,
                        out var arrayPlace))
                {
                    place = new BoundPrivateFixedArrayElementPlace(
                        element.Layout.CarrierType, arrayPlace,
                        element.Index, element.Type);
                    return true;
                }
                break;
            }
            case BoundNativeFixedArrayPathElement path
                when !path.Projections.IsDefaultOrEmpty:
            {
                var arrayExpression = path.FieldPlace
                    ?? new BoundRead(path.Variable,
                        path.Projections[0].Layout.CarrierType,
                        path.Span);
                if (!TryCreatePrivatePlace(arrayExpression, function, program,
                        out var arrayPlace))
                {
                    break;
                }

                var validPath = true;
                foreach (var projection in path.Projections)
                {
                    if (arrayPlace.Type != projection.Layout.CarrierType)
                    {
                        validPath = false;
                        break;
                    }
                    arrayPlace = new BoundPrivateFixedArrayElementPlace(
                        projection.Layout.CarrierType, arrayPlace,
                        projection.Index,
                        projection.Layout.ElementType);
                }
                if (validPath && arrayPlace.Type == path.Type)
                {
                    place = arrayPlace;
                    return true;
                }
                break;
            }
        }

        place = default!;
        return false;
    }
}
