// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System.Collections.Generic;

namespace Terrum.CodeAnalysis.Semantics;

public static partial class SemanticVerifier
{
    private static void VerifyDependencyContracts(BoundFunction function,
        BoundProgram program)
    {
        if (function.DependencyContracts.IsDefault
            || function.NativeOutputContracts.IsDefault)
        {
            throw InvalidEffect(function.Span, "Ownership");
        }

        var hasReceiverSlot = function.DeclaringStruct is not null
            && !function.IsStatic && !function.IsClassConstructor;
        var receiverOffset = hasReceiverSlot ? 1 : 0;
        var explicitParameterCount = function.ParameterCount
            - (hasReceiverSlot && !function.IsConstructor ? 1 : 0);
        if (explicitParameterCount < 0)
        {
            throw InvalidEffect(function.Span, "Ownership");
        }

        var seenTargets = new HashSet<(BoundDependencyRelation,
            BoundDependencyDestination, int?)>();
        foreach (var contract in function.DependencyContracts)
        {
            if (contract is null || contract.Sources.IsDefault
                || contract.Relation is not (BoundDependencyRelation.DependsOn
                    or BoundDependencyRelation.Captures
                    or BoundDependencyRelation.ForwardsBacking))
            {
                throw InvalidEffect(function.Span, "Ownership");
            }

            if (!IsValidDependencyDestination(contract, function, program,
                explicitParameterCount, receiverOffset, hasReceiverSlot)
                || !seenTargets.Add((contract.Relation, contract.Destination,
                    contract.DestinationOrdinal)))
            {
                throw InvalidEffect(function.Span, "Ownership");
            }
            if (contract.Relation == BoundDependencyRelation.ForwardsBacking
                && contract.Sources.IsEmpty)
            {
                throw InvalidEffect(function.Span, "Ownership");
            }

            var seenSources = new HashSet<BoundDependencySource>();
            foreach (var source in contract.Sources)
            {
                var validSource = source.Kind switch
                {
                    BoundDependencySourceKind.Receiver
                        => source.FormalOrdinal is null && hasReceiverSlot
                            && !function.IsConstructor,
                    BoundDependencySourceKind.Formal
                        => source.FormalOrdinal is int ordinal
                            && (uint)ordinal < (uint)explicitParameterCount
                            && !IsNativeOutputFormal(ordinal, function),
                    _ => false,
                };
                if (!validSource || !seenSources.Add(source))
                {
                    throw InvalidEffect(function.Span, "Ownership");
                }
            }
        }

        foreach (var forwarding in function.DependencyContracts)
        {
            if (forwarding.Relation != BoundDependencyRelation.ForwardsBacking)
            {
                continue;
            }

            var hasRelationship = false;
            var permittedSources = new HashSet<BoundDependencySource>();
            foreach (var relationship in function.DependencyContracts)
            {
                if (relationship.Destination != forwarding.Destination
                    || relationship.DestinationOrdinal != forwarding.DestinationOrdinal
                    || relationship.Relation is not (BoundDependencyRelation.DependsOn
                        or BoundDependencyRelation.Captures))
                {
                    continue;
                }

                hasRelationship = true;
                permittedSources.UnionWith(relationship.Sources);
            }
            if (!hasRelationship)
            {
                throw InvalidEffect(function.Span, "Ownership");
            }
            foreach (var source in forwarding.Sources)
            {
                if (!permittedSources.Contains(source))
                {
                    throw InvalidEffect(function.Span, "Ownership");
                }
            }
        }
    }

    private static bool IsValidDependencyDestination(
        BoundDependencyContract contract, BoundFunction function,
        BoundProgram program, int explicitParameterCount,
        int receiverOffset, bool hasReceiverSlot)
    {
        var validShape = contract.Destination switch
        {
            BoundDependencyDestination.Result
                => contract.DestinationOrdinal is null
                    && !function.IsConstructor && function.ReturnType != PrimitiveType.Void
                    && (contract.Relation != BoundDependencyRelation.Captures
                        || IsRetainedDependencyValue(function.ReturnType, program)),
            BoundDependencyDestination.Receiver
                => contract.DestinationOrdinal is null && hasReceiverSlot
                    && contract.Relation != BoundDependencyRelation.DependsOn,
            BoundDependencyDestination.InitializedOutput
                => IsOutputDestination(contract.DestinationOrdinal, function,
                    explicitParameterCount, receiverOffset),
            BoundDependencyDestination.RetainedFormal
                => IsRetainedFormalDestination(contract.DestinationOrdinal,
                    function, explicitParameterCount, receiverOffset, program),
            _ => false,
        };
        if (!validShape)
        {
            return false;
        }
        if (contract.Destination == BoundDependencyDestination.InitializedOutput
            && contract.Relation == BoundDependencyRelation.Captures
            && IsNativeOutputFormal(contract.DestinationOrdinal, function))
        {
            return false;
        }

        return contract.Relation switch
        {
            BoundDependencyRelation.DependsOn
                => contract.Destination is BoundDependencyDestination.Result
                    or BoundDependencyDestination.InitializedOutput,
            BoundDependencyRelation.Captures
                => contract.Destination is BoundDependencyDestination.Result
                    or BoundDependencyDestination.Receiver
                    or BoundDependencyDestination.InitializedOutput
                    or BoundDependencyDestination.RetainedFormal,
            BoundDependencyRelation.ForwardsBacking
                => contract.Destination is BoundDependencyDestination.Result
                    or BoundDependencyDestination.Receiver
                    or BoundDependencyDestination.InitializedOutput
                    or BoundDependencyDestination.RetainedFormal,
            _ => false,
        };
    }

    private static bool IsOutputDestination(int? ordinal, BoundFunction function,
        int explicitParameterCount, int receiverOffset)
    {
        if (ordinal is not int parameter || (uint)parameter >= (uint)explicitParameterCount)
        {
            return false;
        }

        var slot = parameter + receiverOffset;
        return (uint)slot < (uint)function.Variables.Length
            && (function.Variables[slot].IsInit
                || IsNativeOutputFormal(parameter, function));
    }

    private static bool IsNativeOutputFormal(int? ordinal, BoundFunction function)
    {
        if (ordinal is not int formal || function.NativeOutputContracts.IsDefault)
        {
            return false;
        }

        foreach (var output in function.NativeOutputContracts)
        {
            if (output.FormalOrdinal == formal)
            {
                return true;
            }
        }
        return false;
    }

    private static bool IsRetainedFormalDestination(int? ordinal,
        BoundFunction function, int explicitParameterCount, int receiverOffset,
        BoundProgram program)
    {
        if (ordinal is not int parameter || (uint)parameter >= (uint)explicitParameterCount)
        {
            return false;
        }
        if (IsNativeOutputFormal(parameter, function))
        {
            return false;
        }

        var slot = parameter + receiverOffset;
        if ((uint)slot >= (uint)function.Variables.Length)
        {
            return false;
        }

        var formal = function.Variables[slot];
        var nativePointerHolder = IsNativePointerHolder(formal.Type, program);
        var checkedReferenceHolder = false;
        if (CheckedReferenceTypeContract.IsValidType(
            formal.Type, program.CheckedReferenceTypes))
        {
            var reference = program.CheckedReferenceTypes[
                CheckedReferenceTypes.Index(formal.Type)];
            checkedReferenceHolder = reference.IsWritable
                && !reference.IsGarbageCollected
                && !reference.IsInitialization
                && !reference.IsOptional
                && (IsUnique(program, reference.ReferentType)
                    || ManagedClassTypes.IsManaged(reference.ReferentType)
                    || TokenSpanContract.IsDependent(
                        reference.ReferentType, program.Structures));
        }
        return formal.IsOut || formal.IsImplicitRef
            || ManagedClassTypes.IsManaged(formal.Type)
            || IsUnique(program, formal.Type)
            || nativePointerHolder || checkedReferenceHolder;
    }

    private static bool IsRetainedDependencyValue(PrimitiveType type,
        BoundProgram program)
        => ManagedClassTypes.IsManaged(type) || IsUnique(program, type);
}
