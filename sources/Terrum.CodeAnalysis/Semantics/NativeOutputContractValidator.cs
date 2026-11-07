// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace Terrum.CodeAnalysis.Semantics;

internal static class NativeOutputContractValidator
{
    internal static bool IsValid(BoundFunction function,
        IReadOnlyList<BoundStruct> structures,
        ImmutableArray<NativePointerTypeDefinition> pointerTypes,
        ImmutableArray<NativeFunctionPointerSignatureDefinition> functionPointerSignatures)
    {
        var outputs = function.NativeOutputContracts;
        if (outputs.IsDefault)
        {
            return false;
        }
        if (outputs.IsEmpty)
        {
            return function.NativeOutputRole is null;
        }
        if (function.NativeOutputRole != NativeOutputDeclarationRole.DirectImport
            || function.DirectNativeImport is not DirectNativeImport import
            || function.NativeVTableMethod is not null
            || !function.IsExternal || !function.RequiresUnsafeCaller
            || function.ReturnType is not (PrimitiveType.Int32 or PrimitiveType.UInt32)
            || import.Outputs.IsDefault || outputs.Length != import.Outputs.Length
            || function.DependencyContracts.IsDefault)
        {
            return false;
        }

        var outputFormals = new HashSet<int>();
        foreach (var output in outputs)
        {
            if (output is null || !outputFormals.Add(output.FormalOrdinal))
            {
                return false;
            }
        }

        var previousFormal = -1;
        foreach (var output in outputs)
        {
            if (output is null
                || output.FormalOrdinal <= previousFormal
                || output.FormalOrdinal >= function.ParameterCount
                || output.Formation != NativeOutputFormation.VacantWholeObject
                || output.PublicationPhase != NativeOutputPublicationPhase.NormalCompletion
                || output.SuccessState != NativeOutputState.Live
                || output.ComplementState != NativeOutputState.Vacant
                || !ValidSelectors(output, function.ReturnType)
                || !HasValidDependencies(output, function, outputFormals)
                || !MatchesLegacyOutput(output, import.Outputs, function.ReturnType))
            {
                return false;
            }

            var type = output.PointeeType;
            var formal = function.Variables[output.FormalOrdinal];
            if (!DirectNativeImportContract.IsNativeOutputCellType(type, structures,
                    functionPointerSignatures, pointerTypes)
                || !NativePointerTypeContract.IsValidType(formal.Type, pointerTypes)
                || pointerTypes[NativePointerTypes.Index(formal.Type)].PointeeType != type
                || !pointerTypes[NativePointerTypes.Index(formal.Type)].IsWritable
                || formal.IsOut || formal.IsInit)
            {
                return false;
            }

            previousFormal = output.FormalOrdinal;
        }

        return true;
    }

    internal static ImmutableArray<NativeOutputDependency> CreateDependencies(
        ImmutableArray<BoundDependencyContract> contracts, int formalOrdinal)
    {
        if (contracts.IsDefault)
        {
            return default;
        }

        BoundDependencyContract? dependsOn = null;
        BoundDependencyContract? forwardsBacking = null;
        foreach (var contract in contracts)
        {
            if (contract is null)
            {
                return default;
            }
            if (contract.Destination != BoundDependencyDestination.InitializedOutput
                || contract.DestinationOrdinal != formalOrdinal)
            {
                continue;
            }

            switch (contract.Relation)
            {
                case BoundDependencyRelation.DependsOn when dependsOn is null:
                    dependsOn = contract;
                    break;
                case BoundDependencyRelation.ForwardsBacking when forwardsBacking is null:
                    forwardsBacking = contract;
                    break;
                default:
                    return default;
            }
        }

        if (dependsOn is null && forwardsBacking is null)
        {
            return [EmptyDependency(formalOrdinal)];
        }
        if (dependsOn is null || dependsOn.Sources.IsDefault
            || (forwardsBacking is not null
                && (forwardsBacking.Sources.IsDefault || forwardsBacking.Sources.IsEmpty)))
        {
            return default;
        }

        var forwardedSources = new HashSet<BoundDependencySource>();
        if (forwardsBacking is not null)
        {
            foreach (var source in forwardsBacking.Sources)
            {
                if (!dependsOn.Sources.Contains(source)
                    || !forwardedSources.Add(source))
                {
                    return default;
                }
            }
        }

        if (dependsOn.Sources.IsEmpty)
        {
            return forwardsBacking is null
                ? [EmptyDependency(formalOrdinal)]
                : default;
        }

        var orderedSources = new List<BoundDependencySource>(dependsOn.Sources);
        orderedSources.Sort(static (left, right) =>
        {
            var leftKind = OutputSourceKind(left.Kind);
            var rightKind = OutputSourceKind(right.Kind);
            var kindComparison = leftKind.CompareTo(rightKind);
            return kindComparison != 0
                ? kindComparison
                : (left.FormalOrdinal ?? 0).CompareTo(right.FormalOrdinal ?? 0);
        });

        var dependencies = ImmutableArray.CreateBuilder<NativeOutputDependency>(
            orderedSources.Count);
        BoundDependencySource? previousSource = null;
        foreach (var source in orderedSources)
        {
            if (source.Kind is not (BoundDependencySourceKind.Formal
                or BoundDependencySourceKind.Receiver)
                || (source.Kind == BoundDependencySourceKind.Formal
                    && source.FormalOrdinal is null)
                || (source.Kind == BoundDependencySourceKind.Receiver
                    && source.FormalOrdinal is not null)
                || previousSource == source)
            {
                return default;
            }

            var sourceKind = source.Kind switch
            {
                BoundDependencySourceKind.Formal => NativeOutputDependencySourceKind.Formal,
                BoundDependencySourceKind.Receiver => NativeOutputDependencySourceKind.Receiver,
                _ => throw new InvalidOperationException("Validated dependency source is unassigned."),
            };
            var relationship = forwardedSources.Contains(source)
                ? NativeOutputDependencyRelationship.Forward
                : NativeOutputDependencyRelationship.Union;
            dependencies.Add(new NativeOutputDependency(2, sourceKind, relationship,
                NativeOutputPublicationPhase.NormalCompletion, formalOrdinal,
                source.FormalOrdinal ?? 0));
            previousSource = source;
        }

        return dependencies.MoveToImmutable();
    }

    private static bool ValidSelectors(NativeOutputContract output, PrimitiveType resultType)
        => (output.SuccessSelector, output.ComplementSelector, resultType) switch
        {
            (NativeOutputSelector.NonZero, NativeOutputSelector.Zero,
                PrimitiveType.Int32 or PrimitiveType.UInt32) => true,
            (NativeOutputSelector.NonNegative, NativeOutputSelector.Negative,
                PrimitiveType.Int32) => true,
            _ => false,
        };

    private static bool HasValidDependencies(NativeOutputContract output,
        BoundFunction function, HashSet<int> outputFormals)
    {
        var dependencies = output.Dependencies;
        var expected = CreateDependencies(function.DependencyContracts,
            output.FormalOrdinal);
        if (dependencies.IsDefault || dependencies.IsEmpty
            || expected.IsDefault || dependencies.Length != expected.Length)
        {
            return false;
        }

        var previousKind = 0;
        var previousOrdinal = -1;
        for (var index = 0; index < dependencies.Length; index++)
        {
            var dependency = dependencies[index];
            if (dependency != expected[index]
                || dependency.DestinationKind != 2
                || dependency.Phase != NativeOutputPublicationPhase.NormalCompletion
                || dependency.DestinationOrdinal != output.FormalOrdinal)
            {
                return false;
            }

            if (dependency.SourceKind == NativeOutputDependencySourceKind.Empty)
            {
                return dependencies.Length == 1
                    && dependency.Relationship == NativeOutputDependencyRelationship.Empty
                    && dependency.SourceOrdinal == 0;
            }

            if (dependency.SourceKind is not (NativeOutputDependencySourceKind.Formal
                    or NativeOutputDependencySourceKind.Receiver)
                || dependency.Relationship is not (NativeOutputDependencyRelationship.Forward
                    or NativeOutputDependencyRelationship.Union)
                || (dependency.SourceKind == NativeOutputDependencySourceKind.Formal
                    && (dependency.SourceOrdinal < 0
                        || dependency.SourceOrdinal >= function.ParameterCount
                        || outputFormals.Contains(dependency.SourceOrdinal)))
                || (dependency.SourceKind == NativeOutputDependencySourceKind.Receiver
                    && dependency.SourceOrdinal != 0)
                || (byte)dependency.SourceKind < previousKind
                || ((byte)dependency.SourceKind == previousKind
                    && dependency.SourceOrdinal <= previousOrdinal))
            {
                return false;
            }

            previousKind = (byte)dependency.SourceKind;
            previousOrdinal = dependency.SourceOrdinal;
        }

        return true;
    }

    internal static ImmutableArray<BoundDependencyContract> RestoreDependencyContracts(
        ImmutableArray<NativeOutputContract> outputs)
    {
        if (outputs.IsDefault)
        {
            return default;
        }

        var contracts = ImmutableArray.CreateBuilder<BoundDependencyContract>();
        foreach (var output in outputs)
        {
            var dependentSources = ImmutableArray.CreateBuilder<BoundDependencySource>();
            var forwardedSources = ImmutableArray.CreateBuilder<BoundDependencySource>();
            foreach (var dependency in output.Dependencies)
            {
                BoundDependencySource source = dependency.SourceKind switch
                {
                    NativeOutputDependencySourceKind.Formal => new(
                        BoundDependencySourceKind.Formal, dependency.SourceOrdinal),
                    NativeOutputDependencySourceKind.Receiver => new(
                        BoundDependencySourceKind.Receiver, null),
                    _ => default,
                };
                if (dependency.Relationship is NativeOutputDependencyRelationship.Borrow
                    or NativeOutputDependencyRelationship.Forward
                    or NativeOutputDependencyRelationship.Union)
                {
                    dependentSources.Add(source);
                }
                if (dependency.Relationship == NativeOutputDependencyRelationship.Forward)
                {
                    forwardedSources.Add(source);
                }
            }
            if (dependentSources.Count != 0 || forwardedSources.Count == 0)
            {
                contracts.Add(new BoundDependencyContract(BoundDependencyRelation.DependsOn,
                    BoundDependencyDestination.InitializedOutput, output.FormalOrdinal,
                    dependentSources.ToImmutable()));
            }
            if (forwardedSources.Count != 0)
            {
                contracts.Add(new BoundDependencyContract(BoundDependencyRelation.ForwardsBacking,
                    BoundDependencyDestination.InitializedOutput, output.FormalOrdinal,
                    forwardedSources.ToImmutable()));
            }
        }

        return contracts.ToImmutable();
    }

    private static NativeOutputDependency EmptyDependency(int formalOrdinal)
        => new(2, NativeOutputDependencySourceKind.Empty,
            NativeOutputDependencyRelationship.Empty,
            NativeOutputPublicationPhase.NormalCompletion, formalOrdinal, 0);

    private static byte OutputSourceKind(BoundDependencySourceKind sourceKind)
        => sourceKind switch
        {
            BoundDependencySourceKind.Formal => (byte)NativeOutputDependencySourceKind.Formal,
            BoundDependencySourceKind.Receiver => (byte)NativeOutputDependencySourceKind.Receiver,
            _ => 0,
        };

    private static bool MatchesLegacyOutput(NativeOutputContract output,
        ImmutableArray<DirectNativeOutput> legacyOutputs, PrimitiveType resultType)
    {
        DirectNativeOutput? legacy = null;
        foreach (var candidate in legacyOutputs)
        {
            if (candidate.ParameterIndex == output.FormalOrdinal)
            {
                legacy = candidate;
                break;
            }
        }
        if (legacy is null || legacy.PointeeType != output.PointeeType
            || legacy.FailureDisposition != 1)
        {
            return false;
        }

        var expectedCondition = output.SuccessSelector switch
        {
            NativeOutputSelector.NonZero when resultType == PrimitiveType.UInt32 => 2u,
            NativeOutputSelector.NonZero => 1u,
            NativeOutputSelector.NonNegative => 3u,
            _ => 0u,
        };
        return legacy.ResultCondition == expectedCondition;
    }
}
