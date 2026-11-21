// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Terrum.CodeAnalysis.Semantics;

internal static class BoundInterfaceContract
{
    private readonly record struct InterfaceKey(
        string Name,
        int Arity,
        string? ProducerIdentity,
        string? ExportKey,
        uint TargetRequirement,
        uint RuntimePolicy);

    internal static bool IsValidTable(
        ImmutableArray<BoundInterfaceDefinition> interfaces,
        Func<PrimitiveType, bool> isValidType,
        ImmutableArray<BoundGenericNominalTypeDefinition>
            genericNominalTypeDefinitions = default,
        ImmutableArray<BoundSourceGenericTypeDefinition>
            sourceGenericTypeDefinitions = default)
    {
        if (interfaces.IsDefault || interfaces.Length > SemanticVerifier.MaximumAnalysisVisits)
        {
            return false;
        }

        var definitions = new Dictionary<InterfaceKey, BoundInterfaceDefinition>();
        foreach (var definition in interfaces)
        {
            var moduleImport = definition?.ModuleImport;
            if (definition is null || string.IsNullOrWhiteSpace(definition.QualifiedName)
                || definition.QualifiedName.Contains('\0', StringComparison.Ordinal)
                || (moduleImport is not null
                    && (string.IsNullOrWhiteSpace(moduleImport.ProducerIdentity)
                        || string.IsNullOrWhiteSpace(moduleImport.ExportKey)
                        || moduleImport.ProducerIdentity.Contains('\0', StringComparison.Ordinal)
                        || moduleImport.ExportKey.Contains('\0', StringComparison.Ordinal)
                        || moduleImport.TargetRequirement > 1 || moduleImport.RuntimePolicy > 1))
                || definition.Accessibility is < BoundInterfaceAccessibility.Default
                    or > BoundInterfaceAccessibility.Private
                || definition.IsPublic != (definition.Accessibility == BoundInterfaceAccessibility.Public)
                || definition.AttributeTokens.IsDefault
                || definition.AttributeTokens.Any(string.IsNullOrWhiteSpace)
                || definition.Formals.IsDefault
                || definition.Formals.Length > SemanticVerifier.MaximumArguments
                || definition.Bases.IsDefault
                || definition.Bases.Length > SemanticVerifier.MaximumAnalysisVisits
                || definition.Constraints.IsDefault
                || definition.Constraints.Length > SemanticVerifier.MaximumAnalysisVisits
                || definition.Members.IsDefault
                || definition.Members.Length > SemanticVerifier.MaximumAnalysisVisits
                || (definition.ForeignNativeInterface
                    is BoundForeignNativeInterfaceLayout foreignLayout
                    && (definition.IsPartial || !definition.Formals.IsEmpty
                        || !definition.Bases.IsEmpty
                        || !definition.Constraints.IsEmpty
                        || foreignLayout.VTableByteOffset
                            % NativeTargetProfile.PointerSize != 0
                        || definition.Members.IsEmpty
                        || definition.Members.Any(static member =>
                            member is null
                            || member.Kind != BoundInterfaceMemberKind.Callable
                            || member.Accessibility
                                != BoundInterfaceAccessibility.Public
                            || member.IsStatic || member.IsExternal
                            || member.IsPartial
                            || !member.DeclaredRequiresUnsafeCaller
                            || !member.RequiresUnsafeCaller
                            || member.NativeCallableConvention is null
                            || member.IsAsync || member.HasBody
                            || !member.TypeParameters.IsEmpty
                            || !member.Constraints.IsEmpty
                            || !member.Accessors.IsEmpty)))
                || !definitions.TryAdd(Identity(definition.QualifiedName,
                    definition.Formals.Length, moduleImport), definition))
            {
                return false;
            }

            var formalNames = new HashSet<string>(StringComparer.Ordinal);
            var selfCount = 0;
            for (var ordinal = 0; ordinal < definition.Formals.Length; ordinal++)
            {
                var formal = definition.Formals[ordinal];
                if (formal is null)
                {
                    return false;
                }
                if (formal.Ordinal != ordinal
                    || string.IsNullOrWhiteSpace(formal.Name)
                    || formal.Name.Contains('\0', StringComparison.Ordinal)
                    || formal.Attributes.IsDefault
                    || formal.Attributes.Any(string.IsNullOrWhiteSpace)
                    || formal.AttributeTokens.IsDefault
                    || formal.AttributeTokens.Any(string.IsNullOrWhiteSpace)
                    || !formalNames.Add(formal.Name)
                    || formal.Kind is not (BoundInterfaceFormalKind.Type or BoundInterfaceFormalKind.Constant))
                {
                    return false;
                }
                if (formal.IsSelf)
                {
                    if (ordinal != 0 || formal.Kind != BoundInterfaceFormalKind.Type || ++selfCount != 1)
                    {
                        return false;
                    }
                }
                if (formal.Kind == BoundInterfaceFormalKind.Type)
                {
                    if (formal.ConstantType is not null || formal.ConstantDefault is not null
                        || (formal.RequiresWritable
                            && formal.TypeDefault is not null
                            && !formal.TypeDefault.IsWritable)
                        || (formal.TypeDefault is not null
                            && !IsValidType(formal.TypeDefault, definition, definitions,
                                isValidType, ordinal,
                                genericNominalTypeDefinitions:
                                    genericNominalTypeDefinitions,
                                sourceGenericTypeDefinitions:
                                    sourceGenericTypeDefinitions)))
                    {
                        return false;
                    }
                }
                else
                {
                    if (formal.RequiresWritable
                        || formal.TypeDefault is not null
                        || formal.ConstantType is null)
                    {
                        return false;
                    }
                    var constantType = formal.ConstantType;
                    if (!IsValidType(constantType, definition, definitions,
                            isValidType, ordinal,
                            genericNominalTypeDefinitions:
                                genericNominalTypeDefinitions,
                            sourceGenericTypeDefinitions:
                                sourceGenericTypeDefinitions))
                    {
                        return false;
                    }
                    if (formal.ConstantDefault is BoundConstant constant
                        && (constantType.Kind != BoundInterfaceTypeKind.Primitive
                            || constant.Type != constantType.PrimitiveType
                            || !IsConstantDomainType(constant.Type, isValidType)))
                    {
                        return false;
                    }
                }
            }

            var requiresWritable = new bool[definition.Formals.Length];
            foreach (var constraint in definition.Constraints)
            {
                if (constraint is null || (uint)constraint.FormalOrdinal >= (uint)definition.Formals.Length
                    || constraint.FormalScope != BoundInterfaceTypeParameterScope.Interface
                    || definition.Formals[constraint.FormalOrdinal].Kind != BoundInterfaceFormalKind.Type)
                {
                    return false;
                }
                if (constraint.Kind == BoundInterfaceConstraintKind.Type)
                {
                    if (constraint.Type is null || constraint.AttributeName is not null
                        || !IsValidType(constraint.Type, definition, definitions,
                            isValidType, definition.Formals.Length,
                            genericNominalTypeDefinitions:
                                genericNominalTypeDefinitions,
                            sourceGenericTypeDefinitions:
                                sourceGenericTypeDefinitions))
                    {
                        return false;
                    }
                }
                else if (constraint.Kind == BoundInterfaceConstraintKind.Write)
                {
                    if (constraint.Type is not null
                        || constraint.AttributeName is not null)
                    {
                        return false;
                    }

                    requiresWritable[constraint.FormalOrdinal] = true;
                }
                else if (constraint.Kind != BoundInterfaceConstraintKind.Attribute
                    || constraint.Type is not null || string.IsNullOrWhiteSpace(constraint.AttributeName)
                    || constraint.AttributeName.Contains('\0', StringComparison.Ordinal))
                {
                    return false;
                }
            }
            for (var ordinal = 0;
                ordinal < definition.Formals.Length; ordinal++)
            {
                if (definition.Formals[ordinal].RequiresWritable
                    != requiresWritable[ordinal])
                {
                    return false;
                }
            }

            var baseTypes = new List<BoundInterfaceType>();
            foreach (var baseType in definition.Bases)
            {
                if (baseType is null || baseType.Kind != BoundInterfaceTypeKind.Nominal
                    || baseType.NominalName is null
                    || baseTypes.Exists(previous => AreEquivalent(previous, baseType))
                    || !IsValidType(baseType, definition, definitions, isValidType,
                        definition.Formals.Length,
                        genericNominalTypeDefinitions:
                            genericNominalTypeDefinitions,
                        sourceGenericTypeDefinitions:
                            sourceGenericTypeDefinitions))
                {
                    return false;
                }
                baseTypes.Add(baseType);
            }
            if (!IsValidMembers(definition, definitions, isValidType,
                genericNominalTypeDefinitions,
                sourceGenericTypeDefinitions))
            {
                return false;
            }
        }

        return HasAcyclicBases(definitions);
    }

    private static bool IsValidInterfaceParameterDefault(
        BoundConstant value)
    {
        var type = value.Type;
        if (type is not (PrimitiveType.Void or PrimitiveType.Bool or PrimitiveType.Char8
                or PrimitiveType.Char16 or PrimitiveType.Char32
                or PrimitiveType.Rune or PrimitiveType.Int8
                or PrimitiveType.Int16 or PrimitiveType.Int32
                or PrimitiveType.Int64 or PrimitiveType.UInt8
                or PrimitiveType.UInt16 or PrimitiveType.UInt32
                or PrimitiveType.UInt64 or PrimitiveType.Int128
                or PrimitiveType.UInt128 or PrimitiveType.Float16
                or PrimitiveType.BFloat16 or PrimitiveType.Float32
                or PrimitiveType.Float64 or PrimitiveType.Float128
                or PrimitiveType.Decimal32 or PrimitiveType.Decimal64
                or PrimitiveType.Decimal128)
            && !BoundEnumTypes.IsType(type))
        {
            return false;
        }

        var carrierType = value.RepresentationType;
        return (BoundEnumTypes.IsType(type)
                ? PrimitiveTypeFacts.IsIntegerOrWide(carrierType)
                : carrierType == type)
            && value.Bits.IsCanonicalFor(carrierType);
    }

    private static bool IsValidMembers(
        BoundInterfaceDefinition owner,
        IReadOnlyDictionary<InterfaceKey, BoundInterfaceDefinition> definitions,
        Func<PrimitiveType, bool> isValidType,
        ImmutableArray<BoundGenericNominalTypeDefinition>
            genericNominalTypeDefinitions,
        ImmutableArray<BoundSourceGenericTypeDefinition>
            sourceGenericTypeDefinitions)
    {
        var memberKeys = new HashSet<string>(StringComparer.Ordinal);
        for (var ordinal = 0; ordinal < owner.Members.Length; ordinal++)
        {
            var member = owner.Members[ordinal];
            if (member is null || member.Ordinal != ordinal
                || string.IsNullOrWhiteSpace(member.Name) || member.Name.Contains('\0', StringComparison.Ordinal)
                || string.IsNullOrWhiteSpace(member.DeclarationKey)
                || member.DeclarationKey.Contains('\0', StringComparison.Ordinal)
                || !memberKeys.Add(member.DeclarationKey)
                || member.Kind is < BoundInterfaceMemberKind.Callable or > BoundInterfaceMemberKind.Constant
                || member.Accessibility is < BoundInterfaceAccessibility.Default or > BoundInterfaceAccessibility.Private
                || (member.Accessibility == BoundInterfaceAccessibility.Private && !member.HasBody)
                || (member.RequiresMutableReceiver
                    && (member.Kind != BoundInterfaceMemberKind.Callable
                        || member.IsStatic))
                || member.Attributes.IsDefault || member.Attributes.Any(string.IsNullOrWhiteSpace)
                || member.AttributeTokens.IsDefault || member.AttributeTokens.Any(string.IsNullOrWhiteSpace)
                || member.ContractTokens.IsDefault
                || member.AdditionalDeclarationKeys.IsDefault
                || member.TypeParameters.IsDefault || member.TypeParameters.Length > SemanticVerifier.MaximumArguments
                || member.Constraints.IsDefault || member.Parameters.IsDefault
                || member.Parameters.Length > SemanticVerifier.MaximumArguments
                || member.Dependencies.IsDefault || member.Accessors.IsDefault
                || member.NativeOutputContracts.IsDefault
                || member.NativeOutputContracts.Length
                    > member.Parameters.Length
                || member.Accessors.Length > 3
                || (member.DeclaredRequiresUnsafeCaller
                    && !member.RequiresUnsafeCaller)
                || (member.NativeCallableConvention
                    is NativeCallingConvention convention
                    && (member.Kind != BoundInterfaceMemberKind.Callable
                        || !member.RequiresUnsafeCaller
                        || convention is not (
                            NativeCallingConvention.Unmanaged
                            or NativeCallingConvention.Cdecl
                            or NativeCallingConvention.Stdcall
                            or NativeCallingConvention.Vectorcall))))
            {
                return false;
            }
            foreach (var additionalKey in member.AdditionalDeclarationKeys)
            {
                if (string.IsNullOrWhiteSpace(additionalKey)
                    || additionalKey.Contains('\0', StringComparison.Ordinal)
                    || !memberKeys.Add(additionalKey))
                {
                    return false;
                }
            }
            if (member.Kind is BoundInterfaceMemberKind.Callable or BoundInterfaceMemberKind.Operator
                    or BoundInterfaceMemberKind.Property or BoundInterfaceMemberKind.Indexer
                ? member.ResultType is null
                : member.ResultType is not null)
            {
                return false;
            }
            if ((member.Kind is BoundInterfaceMemberKind.Property or BoundInterfaceMemberKind.Indexer)
                != !member.Accessors.IsEmpty)
            {
                return false;
            }
            if (member.Kind is BoundInterfaceMemberKind.Property or BoundInterfaceMemberKind.Indexer)
            {
                var accessorKinds = new HashSet<BoundInterfaceAccessorKind>();
                if (!member.Accessors.Any(static accessor => accessor.Kind == BoundInterfaceAccessorKind.Get)
                    || member.Accessors.Any(accessor => !accessorKinds.Add(accessor.Kind))
                    || (accessorKinds.Contains(BoundInterfaceAccessorKind.Set)
                        && accessorKinds.Contains(BoundInterfaceAccessorKind.Init)))
                {
                    return false;
                }
            }
            if (member.Kind is not (BoundInterfaceMemberKind.Callable or BoundInterfaceMemberKind.Operator)
                && !member.TypeParameters.IsEmpty)
            {
                return false;
            }
            if (member.Kind == BoundInterfaceMemberKind.AssociatedType)
            {
                if (member.AssociatedConstantType is not null
                    || member.AssociatedConstantDefault is not null
                    || (member.AssociatedDefaultType is not null
                        && !IsValidType(member.AssociatedDefaultType, owner, definitions,
                           isValidType, owner.Formals.Length,
                           genericNominalTypeDefinitions:
                               genericNominalTypeDefinitions,
                           sourceGenericTypeDefinitions:
                               sourceGenericTypeDefinitions)))
                {
                    return false;
                }
            }
            else if (member.AssociatedDefaultType is not null)
            {
                return false;
            }
            if (member.Kind == BoundInterfaceMemberKind.Constant)
            {
                if (member.AssociatedConstantType is null
                    || member.AssociatedConstantDefault is not null
                    || !IsValidType(member.AssociatedConstantType, owner, definitions,
                        isValidType, owner.Formals.Length,
                        genericNominalTypeDefinitions:
                            genericNominalTypeDefinitions,
                        sourceGenericTypeDefinitions:
                            sourceGenericTypeDefinitions))
                {
                    return false;
                }
            }
            else if (member.AssociatedConstantType is not null || member.AssociatedConstantDefault is not null)
            {
                return false;
            }
            if (!IsValidMemberDependencies(member.Dependencies, member, null))
            {
                return false;
            }

            var formalNames = new HashSet<string>(StringComparer.Ordinal);
            for (var formalOrdinal = 0; formalOrdinal < member.TypeParameters.Length; formalOrdinal++)
            {
                var formal = member.TypeParameters[formalOrdinal];
                if (!IsValidFormal(formal, formalOrdinal, owner, definitions, isValidType,
                    member.TypeParameters, formalOrdinal,
                    genericNominalTypeDefinitions,
                    sourceGenericTypeDefinitions)
                    || !formalNames.Add(formal.Name) || formal.IsSelf)
                {
                    return false;
                }
            }

            foreach (var constraint in member.Constraints)
            {
                if (constraint is null || constraint.FormalScope != BoundInterfaceTypeParameterScope.Member
                    || (uint)constraint.FormalOrdinal >= (uint)member.TypeParameters.Length
                    || member.TypeParameters[constraint.FormalOrdinal].Kind != BoundInterfaceFormalKind.Type)
                {
                    return false;
                }
                if (constraint.Kind == BoundInterfaceConstraintKind.Type)
                {
                    if (constraint.Type is null || constraint.AttributeName is not null
                        || !IsValidType(constraint.Type, owner, definitions, isValidType,
                            owner.Formals.Length, member.TypeParameters,
                            member.TypeParameters.Length,
                            genericNominalTypeDefinitions:
                                genericNominalTypeDefinitions,
                            sourceGenericTypeDefinitions:
                                sourceGenericTypeDefinitions))
                    {
                        return false;
                    }
                }
                else if (constraint.Kind != BoundInterfaceConstraintKind.Attribute
                    || constraint.Type is not null || string.IsNullOrWhiteSpace(constraint.AttributeName))
                {
                    return false;
                }
            }

            if (member.ResultType is BoundInterfaceType result
                && !IsValidType(result, owner, definitions, isValidType,
                    owner.Formals.Length, member.TypeParameters, member.TypeParameters.Length,
                    allowVoid: member.Kind is BoundInterfaceMemberKind.Callable
                        or BoundInterfaceMemberKind.Operator,
                    genericNominalTypeDefinitions:
                        genericNominalTypeDefinitions,
                    sourceGenericTypeDefinitions:
                        sourceGenericTypeDefinitions))
            {
                return false;
            }

            var parameterNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (var parameter in member.Parameters)
            {
                if (parameter is null || string.IsNullOrWhiteSpace(parameter.Name)
                    || !parameterNames.Add(parameter.Name) || parameter.Attributes.IsDefault
                    || parameter.Attributes.Any(string.IsNullOrWhiteSpace)
                    || parameter.AttributeTokens.IsDefault
                    || parameter.AttributeTokens.Any(string.IsNullOrWhiteSpace)
                    || parameter.Mode is < BoundInterfaceParameterMode.Value
                        or > BoundInterfaceParameterMode.ImplicitRef
                    || !IsValidType(parameter.Type, owner, definitions, isValidType,
                        owner.Formals.Length, member.TypeParameters,
                        member.TypeParameters.Length,
                        allowVoid: parameter.Mode
                            == BoundInterfaceParameterMode.Value,
                        genericNominalTypeDefinitions:
                            genericNominalTypeDefinitions,
                        sourceGenericTypeDefinitions:
                            sourceGenericTypeDefinitions)
                    || (parameter.DefaultValue is BoundConstant defaultValue
                        && (parameter.Mode
                                != BoundInterfaceParameterMode.Value
                            || parameter.Type.Kind
                                != BoundInterfaceTypeKind.Primitive
                            || defaultValue.Type
                                != parameter.Type.PrimitiveType
                            || !IsValidInterfaceParameterDefault(
                                defaultValue))))
                {
                    return false;
                }
            }

            foreach (var accessor in member.Accessors)
            {
                if (accessor is null
                    || accessor.Kind is < BoundInterfaceAccessorKind.Get or > BoundInterfaceAccessorKind.Init
                    || accessor.Accessibility is < BoundInterfaceAccessibility.Default
                        or > BoundInterfaceAccessibility.Private
                    || (accessor.IsMutable
                        && (member.IsStatic
                            || accessor.Kind
                                == BoundInterfaceAccessorKind.Init))
                    || (accessor.Accessibility == BoundInterfaceAccessibility.Private
                        && !accessor.HasBody)
                    || accessor.Attributes.IsDefault || accessor.Attributes.Any(string.IsNullOrWhiteSpace)
                    || accessor.AttributeTokens.IsDefault
                    || accessor.AttributeTokens.Any(string.IsNullOrWhiteSpace)
                    || accessor.Dependencies.IsDefault || accessor.ContractTokens.IsDefault)
                {
                    return false;
                }
                if (!IsValidMemberDependencies(accessor.Dependencies, member, accessor))
                {
                    return false;
                }
            }
        }
        return true;
    }

    private static bool IsValidMemberDependencies(
        ImmutableArray<BoundDependencyContract> contracts,
        BoundInterfaceMemberDefinition member,
        BoundInterfaceAccessor? accessor)
    {
        var parameters = accessor is null ? member.Parameters
            : (accessor.Kind is BoundInterfaceAccessorKind.Set or BoundInterfaceAccessorKind.Init)
                && member.ResultType is BoundInterfaceType accessorValueType
                ? [new BoundInterfaceParameter(
                    "value", accessorValueType, BoundInterfaceParameterMode.Value, [], [])]
                : [];
        var resultType = accessor is null ? member.ResultType
            : accessor.Kind == BoundInterfaceAccessorKind.Get ? member.ResultType
            : new BoundInterfaceType(BoundInterfaceTypeKind.Primitive,
                PrimitiveType.Void, null, -1, [], false, false, false, false);
        var hasResult = resultType is not null
            && !(resultType.Kind == BoundInterfaceTypeKind.Primitive
                && resultType.PrimitiveType == PrimitiveType.Void);
        var hasReceiver = !member.IsStatic;
        var seen = new HashSet<(BoundDependencyRelation, BoundDependencyDestination, int?)>();
        foreach (var contract in contracts)
        {
            if (contract is null || contract.Sources.IsDefault
                || contract.Relation is not (BoundDependencyRelation.DependsOn
                    or BoundDependencyRelation.Captures
                    or BoundDependencyRelation.ForwardsBacking
                    or BoundDependencyRelation.BorrowSourceStorage)
                || !seen.Add((contract.Relation, contract.Destination, contract.DestinationOrdinal))
                || ((contract.Relation is BoundDependencyRelation.ForwardsBacking
                        or BoundDependencyRelation.BorrowSourceStorage)
                    && contract.Sources.IsEmpty))
            {
                return false;
            }

            var destinationValid = contract.Destination switch {
                BoundDependencyDestination.Result =>
                    hasResult && contract.DestinationOrdinal is null
                        && contract.Relation is BoundDependencyRelation.DependsOn
                            or BoundDependencyRelation.Captures
                            or BoundDependencyRelation.ForwardsBacking
                            or BoundDependencyRelation.BorrowSourceStorage,
                BoundDependencyDestination.Receiver =>
                    hasReceiver && contract.DestinationOrdinal is null
                        && contract.Relation is BoundDependencyRelation.Captures
                            or BoundDependencyRelation.ForwardsBacking
                            or BoundDependencyRelation.BorrowSourceStorage,
                BoundDependencyDestination.InitializedOutput =>
                    contract.DestinationOrdinal is int output
                        && (uint)output < (uint)parameters.Length
                        && (parameters[output].Mode
                                == BoundInterfaceParameterMode.Init
                            || member.NativeOutputContracts.Any(
                                nativeOutput => nativeOutput.FormalOrdinal
                                    == output))
                        && contract.Relation is BoundDependencyRelation.DependsOn
                            or BoundDependencyRelation.Captures
                            or BoundDependencyRelation.ForwardsBacking
                            or BoundDependencyRelation.BorrowSourceStorage,
                BoundDependencyDestination.RetainedFormal =>
                    contract.DestinationOrdinal is int formal
                        && (uint)formal < (uint)parameters.Length
                        && parameters[formal].Mode is not (BoundInterfaceParameterMode.Out
                            or BoundInterfaceParameterMode.Init)
                        && contract.Relation is BoundDependencyRelation.Captures
                            or BoundDependencyRelation.ForwardsBacking
                            or BoundDependencyRelation.BorrowSourceStorage,
                _ => false,
            };
            if (!destinationValid)
            {
                return false;
            }
            foreach (var source in contract.Sources)
            {
                if (source.Kind == BoundDependencySourceKind.Receiver)
                {
                    if (!hasReceiver || source.FormalOrdinal is not null)
                    {
                        return false;
                    }
                }
                else if (source.Kind != BoundDependencySourceKind.Formal
                    || source.FormalOrdinal is not int sourceFormal
                    || (uint)sourceFormal >= (uint)parameters.Length
                    || parameters[sourceFormal].Mode is BoundInterfaceParameterMode.Out
                        or BoundInterfaceParameterMode.Init
                    || member.NativeOutputContracts.Any(
                        nativeOutput => nativeOutput.FormalOrdinal
                            == sourceFormal))
                {
                    return false;
                }
            }
        }

        foreach (var refinement in contracts)
        {
            if (refinement.Relation is not (BoundDependencyRelation.ForwardsBacking
                or BoundDependencyRelation.BorrowSourceStorage))
            {
                continue;
            }
            var relatedSources = new HashSet<BoundDependencySource>();
            foreach (var relationship in contracts)
            {
                if (relationship.Destination == refinement.Destination
                    && relationship.DestinationOrdinal
                        == refinement.DestinationOrdinal
                    && (relationship.Relation is BoundDependencyRelation.DependsOn
                        or BoundDependencyRelation.Captures))
                {
                    relatedSources.UnionWith(relationship.Sources);
                }
            }
            if (refinement.Sources.Any(source => !relatedSources.Contains(source)))
            {
                return false;
            }
            var oppositeRefinement
                = refinement.Relation == BoundDependencyRelation.ForwardsBacking
                    ? BoundDependencyRelation.BorrowSourceStorage
                    : BoundDependencyRelation.ForwardsBacking;
            if (refinement.Sources.Any(source => contracts.Any(contract =>
                contract.Relation == oppositeRefinement
                && contract.Destination == refinement.Destination
                && contract.DestinationOrdinal
                    == refinement.DestinationOrdinal
                && contract.Sources.Contains(source))))
            {
                return false;
            }
        }
        return true;
    }

    private static bool IsValidFormal(
        BoundInterfaceFormal formal,
        int ordinal,
        BoundInterfaceDefinition owner,
        IReadOnlyDictionary<InterfaceKey, BoundInterfaceDefinition> definitions,
        Func<PrimitiveType, bool> isValidType,
        ImmutableArray<BoundInterfaceFormal> methodFormals,
        int visibleMethodTypeParameterCount,
        ImmutableArray<BoundGenericNominalTypeDefinition>
            genericNominalTypeDefinitions,
        ImmutableArray<BoundSourceGenericTypeDefinition>
            sourceGenericTypeDefinitions)
    {
        if (formal is null || formal.Ordinal != ordinal
            || string.IsNullOrWhiteSpace(formal.Name) || formal.Name.Contains('\0', StringComparison.Ordinal)
            || formal.Attributes.IsDefault || formal.Attributes.Any(string.IsNullOrWhiteSpace)
            || formal.Kind is not (BoundInterfaceFormalKind.Type or BoundInterfaceFormalKind.Constant))
        {
            return false;
        }
        if (formal.Kind == BoundInterfaceFormalKind.Type)
        {
            return formal.ConstantType is null && formal.ConstantDefault is null
                && (formal.TypeDefault is null
                    || IsValidType(formal.TypeDefault, owner, definitions, isValidType,
                        owner.Formals.Length, methodFormals,
                        visibleMethodTypeParameterCount,
                        genericNominalTypeDefinitions:
                            genericNominalTypeDefinitions,
                        sourceGenericTypeDefinitions:
                            sourceGenericTypeDefinitions));
        }
        return formal.TypeDefault is null && formal.ConstantType is not null
            && IsValidType(formal.ConstantType, owner, definitions, isValidType,
                owner.Formals.Length, methodFormals,
                visibleMethodTypeParameterCount,
                genericNominalTypeDefinitions:
                    genericNominalTypeDefinitions,
                sourceGenericTypeDefinitions:
                    sourceGenericTypeDefinitions)
            && (formal.ConstantType.Kind
                    != BoundInterfaceTypeKind.GenericNominal
                || formal.ConstantType.IsGenericNominalConstantDomain)
            && (formal.ConstantDefault is not BoundConstant constant
                || (formal.ConstantType.Kind == BoundInterfaceTypeKind.Primitive
                    && constant.Type == formal.ConstantType.PrimitiveType
                    && IsConstantDomainType(constant.Type, isValidType)));
    }

    private static bool IsConstantDomainType(
        PrimitiveType type, Func<PrimitiveType, bool> isValidType) =>
        isValidType(type)
        && (BoundEnumTypes.IsType(type)
            || PrimitiveTypeFacts.IsIntegerOrWide(type)
            || PrimitiveTypeFacts.IsFloating(type)
            || type == PrimitiveType.Bool);

    private static bool IsValidType(
        BoundInterfaceType type,
        BoundInterfaceDefinition owner,
        IReadOnlyDictionary<InterfaceKey, BoundInterfaceDefinition> definitions,
        Func<PrimitiveType, bool> isValidType,
        int visibleTypeParameterCount,
        ImmutableArray<BoundInterfaceFormal> methodFormals = default,
        int visibleMethodTypeParameterCount = 0,
        bool allowVoid = false,
        ImmutableArray<BoundGenericNominalTypeDefinition>
            genericNominalTypeDefinitions = default,
        ImmutableArray<BoundSourceGenericTypeDefinition>
            sourceGenericTypeDefinitions = default)
    {
        var visits = SemanticVerifier.MaximumAnalysisVisits;
        return IsValidType(type, owner, definitions, isValidType,
            visibleTypeParameterCount, methodFormals, visibleMethodTypeParameterCount,
            allowVoid, 0, ref visits, genericNominalTypeDefinitions,
            sourceGenericTypeDefinitions);
    }

    internal static bool IsValidGenericMethodConstraintTypes(
        ImmutableArray<BoundInterfaceType> types,
        ImmutableArray<BoundInterfaceDefinition> interfaces,
        Func<PrimitiveType, bool> isValidType,
        int ownerFormalCount,
        ImmutableArray<BoundInterfaceFormal> methodFormals,
        ImmutableArray<BoundGenericNominalTypeDefinition>
            genericNominalTypeDefinitions = default,
        ImmutableArray<BoundSourceGenericTypeDefinition>
            sourceGenericTypeDefinitions = default)
    {
        if (types.IsDefault || types.Length > SemanticVerifier.MaximumArguments
            || interfaces.IsDefault
            || interfaces.Length > SemanticVerifier.MaximumAnalysisVisits
            || ownerFormalCount < 0
            || ownerFormalCount > SemanticVerifier.MaximumArguments
            || methodFormals.IsDefault
            || methodFormals.Length > SemanticVerifier.MaximumArguments)
        {
            return false;
        }
        if (types.IsEmpty)
        {
            return true;
        }

        var ownerFormals = ImmutableArray.CreateBuilder<BoundInterfaceFormal>(
            ownerFormalCount);
        for (var ordinal = 0; ordinal < ownerFormalCount; ordinal++)
        {
            ownerFormals.Add(new BoundInterfaceFormal(ordinal,
                $"$owner{ordinal}", BoundInterfaceFormalKind.Type, false,
                [], [], null, null, null));
        }
        for (var ordinal = 0; ordinal < methodFormals.Length; ordinal++)
        {
            if (methodFormals[ordinal] is not BoundInterfaceFormal formal
                || formal.Ordinal != ordinal)
            {
                return false;
            }
        }

        var owner = new BoundInterfaceDefinition("$generic-method", false,
            false, ownerFormals.MoveToImmutable(), [], [], []);
        var definitions = new Dictionary<InterfaceKey,
            BoundInterfaceDefinition>();
        foreach (var definition in interfaces)
        {
            if (definition is null || string.IsNullOrWhiteSpace(
                    definition.QualifiedName)
                || !definitions.TryAdd(Identity(definition.QualifiedName,
                    definition.Formals.Length, definition.ModuleImport),
                    definition))
            {
                return false;
            }
        }

        var boundMethodFormals = methodFormals;
        var remainingVisits = SemanticVerifier.MaximumAnalysisVisits;
        for (var index = 0; index < types.Length; index++)
        {
            var type = types[index];
            if (type is null || type.Kind != BoundInterfaceTypeKind.Nominal
                || !HasUnqualifiedInterfaceRoot(type)
                || ContainsExistentialTypeArgument(type)
                || !IsValidType(type, owner, definitions, isValidType,
                    ownerFormalCount, boundMethodFormals,
                    boundMethodFormals.Length, false, 0,
                    ref remainingVisits, genericNominalTypeDefinitions,
                    sourceGenericTypeDefinitions)
                || types.Take(index).Any(previous =>
                    AreEquivalent(previous, type)))
            {
                return false;
            }
        }
        return true;
    }

    internal static bool IsValidClosedInterfaceInstance(
        BoundInterfaceType type,
        ImmutableArray<BoundInterfaceDefinition> interfaces,
        Func<PrimitiveType, bool> isValidType,
        ImmutableArray<BoundGenericNominalTypeDefinition>
            genericNominalTypeDefinitions = default,
        ImmutableArray<BoundSourceGenericTypeDefinition>
            sourceGenericTypeDefinitions = default) =>
        IsNominalInterfaceType(type)
        && !ContainsExistentialTypeArgument(type)
        && IsValidClosedType(type, interfaces, isValidType,
            genericNominalTypeDefinitions,
            sourceGenericTypeDefinitions);

    private static bool IsNominalInterfaceType(
        BoundInterfaceType type) =>
        type is not null
        && type.Kind == BoundInterfaceTypeKind.Nominal
        && type.NominalName is not null
        && !type.TypeArguments.IsDefault
        && HasUnqualifiedInterfaceRoot(type);

    private static bool HasUnqualifiedInterfaceRoot(
        BoundInterfaceType type) =>
        !type.IsWritable && !type.IsOptional
        && !type.IsGarbageCollected && !type.IsInitialization;

    private static bool ContainsExistentialTypeArgument(
        BoundInterfaceType type)
    {
        foreach (var argument in type.TypeArguments)
        {
            if (argument is null)
            {
                return true;
            }
            if (argument.Kind
                == BoundInterfaceTypeArgumentKind.ExistentialType
                || (argument.Type is BoundInterfaceType typeArgument
                    && ContainsExistentialTypeArgument(typeArgument))
                || (argument.ConstantType
                        is BoundInterfaceType constantType
                    && ContainsExistentialTypeArgument(constantType)))
            {
                return true;
            }
        }
        return false;
    }

    internal static bool IsValidClosedType(
        BoundInterfaceType type,
        ImmutableArray<BoundInterfaceDefinition> interfaces,
        Func<PrimitiveType, bool> isValidType,
        ImmutableArray<BoundGenericNominalTypeDefinition>
            genericNominalTypeDefinitions = default,
        ImmutableArray<BoundSourceGenericTypeDefinition>
            sourceGenericTypeDefinitions = default)
    {
        if (type is null || interfaces.IsDefault
            || interfaces.Length > SemanticVerifier.MaximumAnalysisVisits)
        {
            return false;
        }

        var definitions = new Dictionary<InterfaceKey,
            BoundInterfaceDefinition>();
        foreach (var definition in interfaces)
        {
            if (definition is null || string.IsNullOrWhiteSpace(
                    definition.QualifiedName)
                || !definitions.TryAdd(Identity(definition.QualifiedName,
                    definition.Formals.Length, definition.ModuleImport),
                    definition))
            {
                return false;
            }
        }

        var owner = new BoundInterfaceDefinition("$closed-type", false,
            false, [], [], [], []);
        return IsValidType(type, owner, definitions, isValidType, 0,
            genericNominalTypeDefinitions:
                genericNominalTypeDefinitions,
            sourceGenericTypeDefinitions:
                sourceGenericTypeDefinitions);
    }

    internal static bool IsValidInterfaceMemberIdentity(
        BoundInterfaceMemberIdentity identity,
        ImmutableArray<BoundInterfaceDefinition> interfaces,
        Func<PrimitiveType, bool> isValidType,
        bool allowOpenFormals = false,
        ImmutableArray<BoundGenericNominalTypeDefinition>
            genericNominalTypeDefinitions = default,
        ImmutableArray<BoundSourceGenericTypeDefinition>
            sourceGenericTypeDefinitions = default)
        => TryGetInterfaceMemberDefinition(identity, interfaces,
            isValidType, out _, allowOpenFormals,
            genericNominalTypeDefinitions,
            sourceGenericTypeDefinitions);

    internal static bool TryGetInterfaceMemberDefinition(
        BoundInterfaceMemberIdentity identity,
        ImmutableArray<BoundInterfaceDefinition> interfaces,
        Func<PrimitiveType, bool> isValidType,
        out BoundInterfaceMemberDefinition? member,
        bool allowOpenFormals = false,
        ImmutableArray<BoundGenericNominalTypeDefinition>
            genericNominalTypeDefinitions = default,
        ImmutableArray<BoundSourceGenericTypeDefinition>
            sourceGenericTypeDefinitions = default)
    {
        member = null;
        if (identity is null
            || string.IsNullOrWhiteSpace(identity.DeclarationKey)
            || identity.MemberOrdinal < 0
            || (allowOpenFormals
                ? !IsNominalInterfaceType(identity.DeclaringInterface)
                : !IsValidClosedInterfaceInstance(
                    identity.DeclaringInterface, interfaces, isValidType,
                    genericNominalTypeDefinitions,
                    sourceGenericTypeDefinitions)))
        {
            return false;
        }

        var type = identity.DeclaringInterface;
        var definition = interfaces.FirstOrDefault(candidate =>
            candidate is not null
            && Identity(candidate.QualifiedName, candidate.Formals.Length,
                candidate.ModuleImport)
                == Identity(type.NominalName!,
                    type.TypeArguments.Length, type.ModuleImport));
        if (definition is null
            || (uint)identity.MemberOrdinal
                >= (uint)definition.Members.Length
            || definition.Members[identity.MemberOrdinal] is not
                BoundInterfaceMemberDefinition selected
            || (selected.DeclarationKey != identity.DeclarationKey
                && !selected.AdditionalDeclarationKeys.Contains(
                    identity.DeclarationKey, StringComparer.Ordinal))
            || selected.Accessibility
                == BoundInterfaceAccessibility.Private
            || (selected.Kind is BoundInterfaceMemberKind.Property
                    or BoundInterfaceMemberKind.Indexer
                ? (uint)identity.AccessorOrdinal
                        >= (uint)selected.Accessors.Length
                    || selected.Accessors[identity.AccessorOrdinal]
                        .Accessibility == BoundInterfaceAccessibility.Private
                : identity.AccessorOrdinal != -1))
        {
            return false;
        }

        member = selected;
        return true;
    }

    internal static bool TryGetRequirementIdentities(
        BoundInterfaceType interfaceType,
        ImmutableArray<BoundInterfaceDefinition> interfaces,
        Func<PrimitiveType, bool> isValidType,
        out ImmutableArray<BoundInterfaceMemberIdentity> requirements,
        bool allowOpenFormals = false,
        ImmutableArray<BoundGenericNominalTypeDefinition>
            genericNominalTypeDefinitions = default,
        ImmutableArray<BoundSourceGenericTypeDefinition>
            sourceGenericTypeDefinitions = default)
    {
        requirements = [];
        if (allowOpenFormals
            ? !IsNominalInterfaceType(interfaceType)
            : !IsValidClosedInterfaceInstance(interfaceType, interfaces,
                isValidType, genericNominalTypeDefinitions,
                sourceGenericTypeDefinitions))
        {
            return false;
        }

        var definitions = new Dictionary<InterfaceKey,
            BoundInterfaceDefinition>();
        foreach (var definition in interfaces)
        {
            if (definition is null || string.IsNullOrWhiteSpace(
                    definition.QualifiedName)
                || !definitions.TryAdd(Identity(definition.QualifiedName,
                    definition.Formals.Length, definition.ModuleImport),
                    definition))
            {
                return false;
            }
        }

        var pending = new Stack<BoundInterfaceType>();
        var visited = new List<BoundInterfaceType>();
        var result = ImmutableArray.CreateBuilder<
            BoundInterfaceMemberIdentity>();
        pending.Push(interfaceType);
        var remainingVisits = SemanticVerifier.MaximumAnalysisVisits;
        while (pending.TryPop(out var current))
        {
            if (--remainingVisits < 0
                || visited.Any(previous =>
                    AreEquivalent(previous, current)))
            {
                if (remainingVisits < 0)
                {
                    return false;
                }
                continue;
            }
            if (current.NominalName is not string nominalName
                || !definitions.TryGetValue(Identity(nominalName,
                    current.TypeArguments.Length, current.ModuleImport),
                    out var definition)
                || current.TypeArguments.IsDefault
                || current.TypeArguments.Length != definition.Formals.Length)
            {
                return false;
            }
            visited.Add(current);

            for (var memberOrdinal = 0;
                memberOrdinal < definition.Members.Length; memberOrdinal++)
            {
                var member = definition.Members[memberOrdinal];
                if (member is null
                    || member.Accessibility
                        == BoundInterfaceAccessibility.Private)
                {
                    continue;
                }

                if (member.Kind is BoundInterfaceMemberKind.Property
                    or BoundInterfaceMemberKind.Indexer)
                {
                    for (var accessorOrdinal = 0;
                        accessorOrdinal < member.Accessors.Length;
                        accessorOrdinal++)
                    {
                        if (member.Accessors[accessorOrdinal]
                            .Accessibility
                            != BoundInterfaceAccessibility.Private)
                        {
                            result.Add(new BoundInterfaceMemberIdentity(
                                current, memberOrdinal,
                                member.DeclarationKey, accessorOrdinal));
                        }
                    }
                }
                else
                {
                    result.Add(new BoundInterfaceMemberIdentity(current,
                        memberOrdinal, member.DeclarationKey));
                }
                if (result.Count > SemanticVerifier.MaximumAnalysisVisits)
                {
                    return false;
                }
            }

            foreach (var baseType in definition.Bases)
            {
                pending.Push(SubstituteTypeParameters(baseType,
                    current.TypeArguments));
            }
        }

        requirements = result.ToImmutable();
        return true;
    }

    internal static bool IsInInheritanceClosure(
        BoundInterfaceType root,
        BoundInterfaceType requested,
        ImmutableArray<BoundInterfaceDefinition> interfaces)
    {
        if (root is null || requested is null || interfaces.IsDefault
            || interfaces.Length > SemanticVerifier.MaximumAnalysisVisits)
        {
            return false;
        }

        var definitions = new Dictionary<InterfaceKey,
            BoundInterfaceDefinition>();
        foreach (var definition in interfaces)
        {
            if (definition is null || string.IsNullOrWhiteSpace(
                    definition.QualifiedName)
                || !definitions.TryAdd(Identity(definition.QualifiedName,
                    definition.Formals.Length, definition.ModuleImport),
                    definition))
            {
                return false;
            }
        }

        var pending = new Stack<BoundInterfaceType>();
        var visited = new List<BoundInterfaceType>();
        pending.Push(root);
        var remainingVisits = SemanticVerifier.MaximumAnalysisVisits;
        while (pending.TryPop(out var current))
        {
            if (--remainingVisits < 0)
            {
                return false;
            }
            if (AreEquivalent(current, requested))
            {
                return true;
            }
            if (visited.Any(previous =>
                    AreEquivalent(previous, current)))
            {
                continue;
            }
            if (current.NominalName is not string nominalName
                || !definitions.TryGetValue(Identity(nominalName,
                    current.TypeArguments.Length, current.ModuleImport),
                    out var definition))
            {
                return false;
            }

            visited.Add(current);
            foreach (var baseType in definition.Bases)
            {
                pending.Push(SubstituteTypeParameters(baseType,
                    current.TypeArguments));
            }
        }
        return false;
    }

    internal static bool AreEquivalent(
        BoundInterfaceMemberIdentity left,
        BoundInterfaceMemberIdentity right) =>
        left.MemberOrdinal == right.MemberOrdinal
        && left.AccessorOrdinal == right.AccessorOrdinal
        && left.DeclarationKey == right.DeclarationKey
        && AreEquivalent(left.DeclaringInterface,
            right.DeclaringInterface);

    internal static IEqualityComparer<BoundInterfaceMemberIdentity>
        MemberIdentityComparer { get; } = new InterfaceMemberIdentityComparer();

    private sealed class InterfaceMemberIdentityComparer
        : IEqualityComparer<BoundInterfaceMemberIdentity>
    {
        public bool Equals(BoundInterfaceMemberIdentity? left,
            BoundInterfaceMemberIdentity? right) =>
            left is null || right is null
                ? left is null && right is null
                : AreEquivalent(left, right);

        public int GetHashCode(BoundInterfaceMemberIdentity identity)
        {
            var hash = new HashCode();
            AddInterfaceTypeHash(ref hash, identity.DeclaringInterface);
            hash.Add(identity.MemberOrdinal);
            hash.Add(identity.DeclarationKey, StringComparer.Ordinal);
            hash.Add(identity.AccessorOrdinal);
            return hash.ToHashCode();
        }

        private static void AddInterfaceTypeHash(ref HashCode hash,
            BoundInterfaceType type)
        {
            hash.Add(type.Kind);
            hash.Add(type.PrimitiveType);
            hash.Add(type.NominalName, StringComparer.Ordinal);
            hash.Add(type.TypeParameterOrdinal);
            hash.Add(type.TypeParameterScope);
            hash.Add(type.IsWritable);
            hash.Add(type.IsOptional);
            hash.Add(type.IsGarbageCollected);
            hash.Add(type.IsInitialization);
            hash.Add(type.ModuleImport);
            hash.Add(type.GenericNominalTypeIdentity);
            hash.Add(type.SourceGenericTypeDefinitionIndex);
            hash.Add(type.IsGenericNominalConstantDomain);
            foreach (var argument in type.TypeArguments)
            {
                hash.Add(argument.Kind);
                hash.Add(argument.ConstantParameterScope);
                hash.Add(argument.ConstantParameterOrdinal);
                hash.Add(argument.ConstantValue);
                if (argument.Type is BoundInterfaceType argumentType)
                {
                    AddInterfaceTypeHash(ref hash, argumentType);
                }
                if (argument.ConstantType
                    is BoundInterfaceType constantType)
                {
                    AddInterfaceTypeHash(ref hash, constantType);
                }
            }
        }
    }

    private static bool IsValidType(
        BoundInterfaceType type,
        BoundInterfaceDefinition owner,
        IReadOnlyDictionary<InterfaceKey, BoundInterfaceDefinition> definitions,
        Func<PrimitiveType, bool> isValidType,
        int visibleTypeParameterCount,
        ImmutableArray<BoundInterfaceFormal> methodFormals,
        int visibleMethodTypeParameterCount,
        bool allowVoid,
        int depth,
        ref int visits,
        ImmutableArray<BoundGenericNominalTypeDefinition>
            genericNominalTypeDefinitions,
        ImmutableArray<BoundSourceGenericTypeDefinition>
            sourceGenericTypeDefinitions)
    {
        if (type is null || depth > SemanticVerifier.MaximumDepth
            || --visits < 0 || type.TypeArguments.IsDefault
            || type.TypeArguments.Length > SemanticVerifier.MaximumArguments)
        {
            return false;
        }

        BoundInterfaceDefinition? nominalDefinition = null;
        BoundGenericNominalTypeDefinition? genericNominalDefinition = null;
        BoundSourceGenericTypeDefinition? sourceGenericDefinition = null;
        switch (type.Kind)
        {
            case BoundInterfaceTypeKind.Primitive:
                return type.TypeParameterScope == BoundInterfaceTypeParameterScope.None
                    && type.NominalName is null && type.TypeParameterOrdinal == -1
                    && type.ModuleImport is null
                    && type.GenericNominalTypeIdentity is null
                    && type.SourceGenericTypeDefinitionIndex == -1
                    && !type.IsGenericNominalConstantDomain
                    && type.TypeArguments.IsEmpty && !type.IsOptional
                    && (type.PrimitiveType != PrimitiveType.Void
                        || !type.IsWritable)
                    && !type.IsGarbageCollected && !type.IsInitialization
                    && (type.PrimitiveType == PrimitiveType.Void
                        ? allowVoid : isValidType(type.PrimitiveType));

            case BoundInterfaceTypeKind.TypeParameter:
                if (type.PrimitiveType != PrimitiveType.Void || type.NominalName is not null
                    || type.ModuleImport is not null || !type.TypeArguments.IsEmpty
                    || type.GenericNominalTypeIdentity is not null
                    || type.SourceGenericTypeDefinitionIndex != -1
                    || type.IsGenericNominalConstantDomain
                    || type.IsGarbageCollected || type.IsInitialization)
                {
                    return false;
                }
                return type.TypeParameterScope switch {
                    BoundInterfaceTypeParameterScope.Interface =>
                        (uint)type.TypeParameterOrdinal < (uint)visibleTypeParameterCount
                        && owner.Formals[type.TypeParameterOrdinal].Kind
                            == BoundInterfaceFormalKind.Type
                        && (!type.IsWritable
                            || owner.Formals[type.TypeParameterOrdinal]
                                .RequiresWritable),
                    BoundInterfaceTypeParameterScope.Member =>
                        !methodFormals.IsDefault
                        && (uint)type.TypeParameterOrdinal < (uint)visibleMethodTypeParameterCount
                        && (uint)type.TypeParameterOrdinal < (uint)methodFormals.Length
                        && methodFormals[type.TypeParameterOrdinal].Kind
                            == BoundInterfaceFormalKind.Type
                        && (!type.IsWritable
                            || methodFormals[type.TypeParameterOrdinal]
                                .RequiresWritable),
                    _ => false,
                };

            case BoundInterfaceTypeKind.Nominal:
                if (type.TypeParameterScope != BoundInterfaceTypeParameterScope.None
                    || type.PrimitiveType != PrimitiveType.Void || type.NominalName is null
                    || type.TypeParameterOrdinal != -1 || type.IsGarbageCollected || type.IsInitialization
                    || type.GenericNominalTypeIdentity is not null
                    || type.SourceGenericTypeDefinitionIndex != -1
                    || type.IsGenericNominalConstantDomain
                    || !definitions.TryGetValue(Identity(type.NominalName,
                        type.TypeArguments.Length, type.ModuleImport), out nominalDefinition)
                    || nominalDefinition.Formals.Length != type.TypeArguments.Length)
                {
                    return false;
                }
                break;

            case BoundInterfaceTypeKind.GenericNominal:
                if (type.TypeParameterScope
                        != BoundInterfaceTypeParameterScope.None
                    || type.PrimitiveType != PrimitiveType.Void
                    || type.NominalName is null
                    || type.TypeParameterOrdinal != -1
                    || type.ModuleImport is not null
                    || (type.IsGenericNominalConstantDomain
                        && (type.IsWritable || type.IsOptional
                            || type.IsGarbageCollected
                            || type.IsInitialization)))
                {
                    return false;
                }

                if (type.SourceGenericTypeDefinitionIndex >= 0)
                {
                    if (type.GenericNominalTypeIdentity is not null
                        || sourceGenericTypeDefinitions.IsDefault
                        || (uint)type.SourceGenericTypeDefinitionIndex
                            >= (uint)sourceGenericTypeDefinitions.Length
                        || sourceGenericTypeDefinitions[
                                type.SourceGenericTypeDefinitionIndex]
                            is not BoundSourceGenericTypeDefinition
                                sourceDefinition
                        || sourceDefinition.DefinitionIndex
                            != type.SourceGenericTypeDefinitionIndex
                        || sourceDefinition.Formals.IsDefaultOrEmpty
                        || sourceDefinition.FormalOrder.IsDefault
                        || sourceDefinition.Formals.Length
                            != type.TypeArguments.Length
                        || sourceDefinition.Formals.Length
                            != sourceDefinition.FormalOrder.Length
                        || (type.IsGenericNominalConstantDomain
                            && sourceDefinition
                                .TransparentConstantCarrierType is null)
                        || !StringComparer.Ordinal.Equals(type.NominalName,
                            sourceDefinition.QualifiedName))
                    {
                        return false;
                    }

                    sourceGenericDefinition = sourceDefinition;
                    foreach (var formal in sourceDefinition.Formals)
                    {
                        if ((uint)formal.Ordinal
                                >= (uint)sourceDefinition.FormalOrder.Length
                            || sourceDefinition.FormalOrder[formal.Ordinal]
                                is not BoundGenericMethodFormalSlot slot
                            || slot.Ordinal != formal.Ordinal
                            || (formal.Kind
                                    == BoundGenericMethodFormalKind.Type
                                && (slot.TypeFormalOrdinal
                                        != formal.TypeFormalOrdinal
                                    || slot.ConstantFormalOrdinal != -1
                                    || formal.OwnershipDomain != 1
                                    || formal.NullabilityDomain != 1
                                    || formal.RequiresBlittable))
                            || (formal.Kind
                                    == BoundGenericMethodFormalKind.Constant
                                && (slot.ConstantFormalOrdinal
                                        != formal.ConstantFormalOrdinal
                                    || slot.TypeFormalOrdinal != -1
                                    || formal.ConstantDomain is null))
                            || formal.Kind is not (
                                BoundGenericMethodFormalKind.Type
                                or BoundGenericMethodFormalKind.Constant))
                        {
                            return false;
                        }
                    }
                }
                else if (type.SourceGenericTypeDefinitionIndex != -1
                    || type.GenericNominalTypeIdentity
                        is not BoundGenericNominalTypeIdentity genericIdentity
                    || !genericIdentity.IsValid
                    || !TryGetGenericNominalTypeDefinition(
                        genericIdentity, genericNominalTypeDefinitions,
                        out var importedDefinition)
                    || (type.IsGenericNominalConstantDomain
                        && importedDefinition
                            .TransparentConstantCarrierType is null)
                    || importedDefinition.Formals.IsDefaultOrEmpty
                    || importedDefinition.Formals.Length
                        != type.TypeArguments.Length
                    || importedDefinition.Formals.Any(static formal =>
                        formal.Constraints.IsDefault
                        || !formal.Constraints.IsEmpty)
                    || !StringComparer.Ordinal.Equals(type.NominalName,
                        importedDefinition.Namespace + "."
                            + importedDefinition.Name))
                {
                    return false;
                }
                else
                {
                    genericNominalDefinition = importedDefinition;
                }
                break;

            case BoundInterfaceTypeKind.CheckedReference:
                if (type.TypeParameterScope != BoundInterfaceTypeParameterScope.None
                    || type.PrimitiveType != PrimitiveType.Void || type.NominalName is not null
                    || type.TypeParameterOrdinal != -1 || type.ModuleImport is not null
                    || type.GenericNominalTypeIdentity is not null
                    || type.SourceGenericTypeDefinitionIndex != -1
                    || type.IsGenericNominalConstantDomain
                    || type.TypeArguments.Length != 1
                    || type.TypeArguments[0].Kind != BoundInterfaceTypeArgumentKind.Type
                    || type.TypeArguments[0].Type is null
                    || type.TypeArguments[0].ConstantType is not null
                    || type.TypeArguments[0].ConstantValue is not null
                    || type.TypeArguments[0].ConstantParameterScope
                        != BoundInterfaceTypeParameterScope.None
                    || type.TypeArguments[0].ConstantParameterOrdinal != -1
                    || (type.IsGarbageCollected && type.IsInitialization))
                {
                    return false;
                }
                break;

            default:
                return false;
        }

        if (type.Kind == BoundInterfaceTypeKind.Nominal)
        {
            var precedingArguments =
                ImmutableArray.CreateBuilder<BoundInterfaceTypeArgument>(
                    type.TypeArguments.Length);
            for (var ordinal = 0; ordinal < type.TypeArguments.Length; ordinal++)
            {
                var argument = type.TypeArguments[ordinal];
                var formal = nominalDefinition!.Formals[ordinal];
                if (formal.Kind == BoundInterfaceFormalKind.Type)
                {
                    if (argument is null)
                    {
                        return false;
                    }
                    if (argument.Kind
                        == BoundInterfaceTypeArgumentKind.ExistentialType)
                    {
                        if (formal.RequiresWritable
                            || argument.Type is not null
                            || argument.ConstantType is not null
                            || argument.ConstantValue is not null
                            || argument.ConstantParameterScope
                                != BoundInterfaceTypeParameterScope.None
                            || argument.ConstantParameterOrdinal != -1)
                        {
                            return false;
                        }
                    }
                    else if (argument.Kind
                            != BoundInterfaceTypeArgumentKind.Type
                        || argument.Type is null
                        || argument.ConstantType is not null
                        || argument.ConstantValue is not null
                        || argument.ConstantParameterScope
                            != BoundInterfaceTypeParameterScope.None
                        || argument.ConstantParameterOrdinal != -1
                        || (formal.RequiresWritable
                            && !argument.Type.IsWritable)
                        || !IsValidType(argument.Type, owner, definitions,
                            isValidType, visibleTypeParameterCount,
                            methodFormals, visibleMethodTypeParameterCount,
                            false, depth + 1, ref visits,
                            genericNominalTypeDefinitions,
                            sourceGenericTypeDefinitions))
                    {
                        return false;
                    }
                }
                else if (formal.Kind != BoundInterfaceFormalKind.Constant
                    || formal.ConstantType is not BoundInterfaceType formalConstantType
                    || !IsValidConstantArgument(argument,
                        SubstituteTypeParameters(formalConstantType,
                            precedingArguments.ToImmutable()),
                        owner,
                        definitions, isValidType, visibleTypeParameterCount,
                        methodFormals, visibleMethodTypeParameterCount,
                        depth + 1, ref visits,
                        genericNominalTypeDefinitions,
                        sourceGenericTypeDefinitions))
                {
                    return false;
                }
                precedingArguments.Add(argument);
            }
        }
        else if (type.Kind == BoundInterfaceTypeKind.GenericNominal)
        {
            if (sourceGenericDefinition is
                BoundSourceGenericTypeDefinition sourceDefinition)
            {
                foreach (var formal in sourceDefinition.Formals)
                {
                    var argument = type.TypeArguments[formal.Ordinal];
                    if (formal.Kind
                        == BoundGenericMethodFormalKind.Type)
                    {
                        if (argument is null
                            || argument.Kind
                                != BoundInterfaceTypeArgumentKind.Type
                            || argument.Type is null
                            || argument.ConstantType is not null
                            || argument.ConstantValue is not null
                            || argument.ConstantParameterScope
                                != BoundInterfaceTypeParameterScope.None
                            || argument.ConstantParameterOrdinal != -1
                            || !IsValidType(argument.Type, owner,
                                definitions, isValidType,
                                visibleTypeParameterCount, methodFormals,
                                visibleMethodTypeParameterCount, false,
                                depth + 1, ref visits,
                                genericNominalTypeDefinitions,
                                sourceGenericTypeDefinitions))
                        {
                            return false;
                        }
                        continue;
                    }

                    if (!TryGetSourceGenericConstantDomain(formal,
                            sourceDefinition, type.TypeArguments,
                            genericNominalTypeDefinitions,
                            sourceGenericTypeDefinitions,
                            ref visits, out var expectedConstantType)
                        || !IsValidConstantArgument(argument,
                            expectedConstantType, owner, definitions,
                            isValidType, visibleTypeParameterCount,
                            methodFormals,
                            visibleMethodTypeParameterCount,
                            depth + 1, ref visits,
                            genericNominalTypeDefinitions,
                            sourceGenericTypeDefinitions))
                    {
                        return false;
                    }
                }
            }
            else
            {
                for (var ordinal = 0;
                    ordinal < type.TypeArguments.Length; ordinal++)
                {
                    var formal = genericNominalDefinition!.Formals[ordinal];
                    var argument = type.TypeArguments[ordinal];
                    if (formal.Kind == BoundGenericArgumentKind.Type)
                    {
                        if (argument is null
                            || argument.Kind
                                != BoundInterfaceTypeArgumentKind.Type
                            || argument.Type is null
                            || argument.ConstantType is not null
                            || argument.ConstantValue is not null
                            || argument.ConstantParameterScope
                                != BoundInterfaceTypeParameterScope.None
                            || argument.ConstantParameterOrdinal != -1
                            || !IsValidType(argument.Type, owner,
                                definitions, isValidType,
                                visibleTypeParameterCount, methodFormals,
                                visibleMethodTypeParameterCount, false,
                                depth + 1, ref visits,
                                genericNominalTypeDefinitions,
                                sourceGenericTypeDefinitions))
                        {
                            return false;
                        }
                        continue;
                    }

                    if (formal.Kind != BoundGenericArgumentKind.Constant
                        || formal.ConstantDomainType is not
                            PrimitiveType domainType
                        || formal.ConstantCarrierType is not
                            PrimitiveType carrierType
                        || (!PrimitiveTypeFacts.IsIntegerOrWide(
                                carrierType)
                            && carrierType != PrimitiveType.Bool
                            && !BoundEnumTypes.IsType(domainType))
                        || (formal.ConstantDomainTypeTerm is not null
                            && (formal.ConstantDomainTypeTerm.Kind
                                    != BoundGenericMethodTypeTermKind
                                        .Concrete
                                || formal.ConstantDomainTypeTerm.ConcreteType
                                    != domainType)))
                    {
                        return false;
                    }

                    var expectedConstantType = new BoundInterfaceType(
                        BoundInterfaceTypeKind.Primitive, domainType, null,
                        -1, [], false, false, false, false);
                    if (!IsValidConstantArgument(argument!,
                        expectedConstantType, owner, definitions,
                        isValidType, visibleTypeParameterCount,
                        methodFormals, visibleMethodTypeParameterCount,
                        depth + 1, ref visits,
                        genericNominalTypeDefinitions,
                        sourceGenericTypeDefinitions))
                    {
                        return false;
                    }
                }
            }
        }
        else if (type.Kind == BoundInterfaceTypeKind.CheckedReference
            && !IsValidType(type.TypeArguments[0].Type!, owner, definitions,
                isValidType, visibleTypeParameterCount, methodFormals,
                visibleMethodTypeParameterCount, false, depth + 1, ref visits,
                genericNominalTypeDefinitions,
                sourceGenericTypeDefinitions))
        {
            return false;
        }
        return true;
    }

    private static bool TryGetSourceGenericConstantDomain(
        BoundSourceGenericTypeFormal formal,
        BoundSourceGenericTypeDefinition owner,
        ImmutableArray<BoundInterfaceTypeArgument> ownerArguments,
        ImmutableArray<BoundGenericNominalTypeDefinition>
            genericNominalTypeDefinitions,
        ImmutableArray<BoundSourceGenericTypeDefinition>
            sourceGenericTypeDefinitions,
        ref int visits,
        out BoundInterfaceType domain)
    {
        domain = null!;
        if (formal.ConstantDomain is not
                BoundGenericMethodTypeTerm constantDomain)
        {
            return false;
        }

        return TryCreateSourceGenericInterfaceType(
            constantDomain, owner, ownerArguments,
            genericNominalTypeDefinitions, sourceGenericTypeDefinitions,
            isConstantDomain: true, ref visits, 0, out domain);
    }

    private static bool TryCreateSourceGenericInterfaceType(
        BoundGenericMethodTypeTerm term,
        BoundSourceGenericTypeDefinition owner,
        ImmutableArray<BoundInterfaceTypeArgument> ownerArguments,
        ImmutableArray<BoundGenericNominalTypeDefinition>
            genericNominalTypeDefinitions,
        ImmutableArray<BoundSourceGenericTypeDefinition>
            sourceGenericTypeDefinitions,
        bool isConstantDomain,
        ref int visits, int depth, out BoundInterfaceType type)
    {
        type = null!;
        if (depth > SemanticVerifier.MaximumDepth || --visits < 0
            || term.TypeArguments.IsDefault
            || term.ConstantArguments.IsDefault
            || (isConstantDomain
                && (term.IsWritable || term.IsGarbageCollected
                    || term.IsInitialization || term.IsOptional)))
        {
            return false;
        }

        if (term.Kind == BoundGenericMethodTypeTermKind.Concrete)
        {
            if (term.ConcreteType == PrimitiveType.Void
                || !term.TypeArguments.IsEmpty
                || !term.ConstantArguments.IsEmpty)
            {
                return false;
            }

            type = new BoundInterfaceType(
                BoundInterfaceTypeKind.Primitive, term.ConcreteType,
                null, -1, [], term.IsWritable, term.IsOptional,
                term.IsGarbageCollected, term.IsInitialization);
            return true;
        }

        if (term.Kind == BoundGenericMethodTypeTermKind.Formal)
        {
            var formal = owner.Formals.FirstOrDefault(candidate =>
                candidate.Kind == BoundGenericMethodFormalKind.Type
                && candidate.TypeFormalOrdinal == term.FormalOrdinal);
            if (formal is null
                || (uint)formal.Ordinal >= (uint)ownerArguments.Length
                || ownerArguments[formal.Ordinal] is not
                    BoundInterfaceTypeArgument {
                        Kind: BoundInterfaceTypeArgumentKind.Type,
                        Type: BoundInterfaceType actualType,
                    })
            {
                return false;
            }

            type = actualType with {
                IsWritable = actualType.IsWritable || term.IsWritable,
                IsOptional = actualType.IsOptional || term.IsOptional,
                IsGarbageCollected = actualType.IsGarbageCollected
                    || term.IsGarbageCollected,
                IsInitialization = actualType.IsInitialization
                    || term.IsInitialization,
            };
            return true;
        }

        if (term.Kind == BoundGenericMethodTypeTermKind.SourceGeneric)
        {
            if ((uint)term.SourceGenericTypeDefinitionIndex
                    >= (uint)sourceGenericTypeDefinitions.Length)
            {
                return false;
            }

            var definition = sourceGenericTypeDefinitions[
                term.SourceGenericTypeDefinitionIndex];
            if (definition is null
                || definition.DefinitionIndex
                    != term.SourceGenericTypeDefinitionIndex
                || definition.Formals.IsDefaultOrEmpty
                || definition.FormalOrder.IsDefault
                || definition.Formals.Length
                    != definition.FormalOrder.Length
                || definition.Formals.Count(static formal =>
                    formal.Kind == BoundGenericMethodFormalKind.Type)
                    != term.TypeArguments.Length
                || definition.Formals.Count(static formal =>
                    formal.Kind
                        == BoundGenericMethodFormalKind.Constant)
                    != term.ConstantArguments.Length
                || (isConstantDomain
                    && definition.TransparentConstantCarrierType is null))
            {
                return false;
            }

            var arguments = ImmutableArray.CreateBuilder<
                BoundInterfaceTypeArgument>(definition.Formals.Length);
            foreach (var formal in definition.Formals)
            {
                if ((uint)formal.Ordinal
                        >= (uint)definition.FormalOrder.Length
                    || definition.FormalOrder[formal.Ordinal]
                        is not BoundGenericMethodFormalSlot slot
                    || slot.Ordinal != formal.Ordinal
                    || formal.Kind != slot.Kind)
                {
                    return false;
                }

                if (formal.Kind == BoundGenericMethodFormalKind.Type)
                {
                    if (formal.RequiresBlittable
                        || formal.OwnershipDomain != 1
                        || formal.NullabilityDomain != 1
                        || (uint)formal.TypeFormalOrdinal
                            >= (uint)term.TypeArguments.Length
                        || slot.TypeFormalOrdinal
                            != formal.TypeFormalOrdinal
                        || slot.ConstantFormalOrdinal != -1
                        || !TryCreateSourceGenericInterfaceType(
                            term.TypeArguments[formal.TypeFormalOrdinal],
                            owner, ownerArguments,
                            genericNominalTypeDefinitions,
                            sourceGenericTypeDefinitions,
                            isConstantDomain: false, ref visits,
                            depth + 1, out var typeArgument))
                    {
                        return false;
                    }

                    arguments.Add(
                        BoundInterfaceTypeArgument.ForType(typeArgument));
                    continue;
                }

                if (formal.Kind
                        != BoundGenericMethodFormalKind.Constant
                    || (uint)formal.ConstantFormalOrdinal
                        >= (uint)term.ConstantArguments.Length
                    || slot.ConstantFormalOrdinal
                        != formal.ConstantFormalOrdinal
                    || slot.TypeFormalOrdinal != -1
                    || !TryCreateSourceGenericFormalConstantArgument(
                        term.ConstantArguments[
                            formal.ConstantFormalOrdinal],
                        owner, ownerArguments,
                        out var constantArgument))
                {
                    return false;
                }

                arguments.Add(constantArgument);
            }

            var completeArguments = arguments.MoveToImmutable();
            foreach (var formal in definition.Formals)
            {
                if (formal.Kind == BoundGenericMethodFormalKind.Type)
                {
                    continue;
                }
                if (formal.Kind
                        != BoundGenericMethodFormalKind.Constant
                    || !TryGetSourceGenericConstantDomain(formal,
                        definition, completeArguments,
                        genericNominalTypeDefinitions,
                        sourceGenericTypeDefinitions, ref visits,
                        out var expectedConstantType)
                    || !AreEquivalent(
                        completeArguments[formal.Ordinal].ConstantType!,
                        expectedConstantType))
                {
                    return false;
                }
            }

            type = new BoundInterfaceType(
                BoundInterfaceTypeKind.GenericNominal,
                PrimitiveType.Void, definition.QualifiedName, -1,
                completeArguments, term.IsWritable, term.IsOptional,
                term.IsGarbageCollected, term.IsInitialization) {
                SourceGenericTypeDefinitionIndex =
                    definition.DefinitionIndex,
                IsGenericNominalConstantDomain = isConstantDomain,
            };
            return true;
        }

        if (term.Kind == BoundGenericMethodTypeTermKind.ImportedGeneric
            && term.NominalTypeIdentity
                is BoundGenericNominalTypeIdentity identity
            && TryGetGenericNominalTypeDefinition(identity,
                genericNominalTypeDefinitions, out var importedDefinition)
            && !term.ConstantArguments.IsDefault
            && importedDefinition.Formals.Count(static formal =>
                formal.Kind == BoundGenericArgumentKind.Type)
                == term.TypeArguments.Length
            && importedDefinition.Formals.Count(static formal =>
                formal.Kind == BoundGenericArgumentKind.Constant)
                == term.ConstantArguments.Length
            && importedDefinition.Formals.All(static formal =>
                !formal.Constraints.IsDefault
                && formal.Constraints.IsEmpty)
            && (!isConstantDomain
                || importedDefinition.TransparentConstantCarrierType
                    is not null))
        {
            var arguments = ImmutableArray.CreateBuilder<
                BoundInterfaceTypeArgument>(importedDefinition.Formals.Length);
            var typeArgumentOrdinal = 0;
            var constantArgumentOrdinal = 0;
            foreach (var formal in importedDefinition.Formals)
            {
                if (formal.Kind == BoundGenericArgumentKind.Type)
                {
                    if (!TryCreateSourceGenericInterfaceType(
                        term.TypeArguments[typeArgumentOrdinal++],
                        owner, ownerArguments,
                        genericNominalTypeDefinitions,
                        sourceGenericTypeDefinitions,
                        isConstantDomain: false, ref visits, depth + 1,
                        out var boundArgument))
                    {
                        return false;
                    }

                    arguments.Add(
                        BoundInterfaceTypeArgument.ForType(boundArgument));
                    continue;
                }

                if (formal.Kind != BoundGenericArgumentKind.Constant
                    || formal.ConstantDomainType is not
                        PrimitiveType domainType
                    || formal.ConstantCarrierType is not
                        PrimitiveType carrierType
                    || (!PrimitiveTypeFacts.IsIntegerOrWide(carrierType)
                        && carrierType != PrimitiveType.Bool
                        && !BoundEnumTypes.IsType(domainType))
                    || (formal.ConstantDomainTypeTerm is not null
                        && (formal.ConstantDomainTypeTerm.Kind
                                != BoundGenericMethodTypeTermKind.Concrete
                            || formal.ConstantDomainTypeTerm.ConcreteType
                                != domainType)))
                {
                    return false;
                }

                var constantArgument = term.ConstantArguments[
                    constantArgumentOrdinal++];
                if (constantArgument.Kind
                        == BoundGenericMethodTypeConstantArgumentKind.Value
                    && (constantArgument.DomainType != domainType
                        || constantArgument.CarrierType != carrierType))
                {
                    return false;
                }

                if (!TryCreateSourceGenericFormalConstantArgument(
                        constantArgument, owner, ownerArguments,
                        out var boundConstantArgument)
                    || boundConstantArgument.ConstantType is not
                        BoundInterfaceType actualDomain
                    || !AreEquivalent(actualDomain,
                        new BoundInterfaceType(
                            BoundInterfaceTypeKind.Primitive, domainType,
                            null, -1, [], false, false, false, false)))
                {
                    return false;
                }

                arguments.Add(boundConstantArgument);
            }

            type = new BoundInterfaceType(
                BoundInterfaceTypeKind.GenericNominal,
                PrimitiveType.Void,
                importedDefinition.Namespace + "."
                    + importedDefinition.Name,
                -1, arguments.MoveToImmutable(), term.IsWritable,
                term.IsOptional, term.IsGarbageCollected,
                term.IsInitialization) {
                GenericNominalTypeIdentity = identity,
                IsGenericNominalConstantDomain = isConstantDomain,
            };
            return true;
        }

        return false;
    }

    private static bool TryCreateSourceGenericFormalConstantArgument(
        BoundGenericMethodTypeConstantArgument argument,
        BoundSourceGenericTypeDefinition owner,
        ImmutableArray<BoundInterfaceTypeArgument> ownerArguments,
        out BoundInterfaceTypeArgument boundArgument)
    {
        boundArgument = null!;
        if (argument.Kind
            == BoundGenericMethodTypeConstantArgumentKind.Value)
        {
            if (argument.DomainType == PrimitiveType.Void
                || argument.CarrierType == PrimitiveType.Void
                || !argument.Bits.IsCanonicalFor(argument.CarrierType))
            {
                return false;
            }

            var constantType = new BoundInterfaceType(
                BoundInterfaceTypeKind.Primitive, argument.DomainType,
                null, -1, [], false, false, false, false);
            var value = argument.DomainType == argument.CarrierType
                ? new BoundConstant(argument.Bits, argument.DomainType,
                    default)
                : new BoundConstant(argument.Bits, argument.DomainType,
                    argument.CarrierType, default);
            boundArgument = BoundInterfaceTypeArgument.ForConstant(
                constantType, value);
            return true;
        }

        if (argument.Kind
                != BoundGenericMethodTypeConstantArgumentKind.Formal
            || argument.FormalOwnerKind
                != BoundGenericTypeFormalOwnerKind.SourceGenericType
            || argument.FormalDefinitionOrdinal != owner.DefinitionIndex)
        {
            return false;
        }

        var formal = owner.Formals.FirstOrDefault(candidate =>
            candidate.Kind == BoundGenericMethodFormalKind.Constant
            && candidate.ConstantFormalOrdinal == argument.FormalOrdinal);
        if (formal is null
            || (uint)formal.Ordinal >= (uint)ownerArguments.Length
            || ownerArguments[formal.Ordinal] is not
                BoundInterfaceTypeArgument {
                    Kind: BoundInterfaceTypeArgumentKind.Constant,
                    ConstantType: not null,
                } actualArgument)
        {
            return false;
        }

        boundArgument = actualArgument;
        return true;
    }

    private static bool IsValidConstantArgument(
        BoundInterfaceTypeArgument argument,
        BoundInterfaceType expectedConstantType,
        BoundInterfaceDefinition owner,
        IReadOnlyDictionary<InterfaceKey, BoundInterfaceDefinition> definitions,
        Func<PrimitiveType, bool> isValidType,
        int visibleTypeParameterCount,
        ImmutableArray<BoundInterfaceFormal> methodFormals,
        int visibleMethodTypeParameterCount,
        int depth,
        ref int visits,
        ImmutableArray<BoundGenericNominalTypeDefinition>
            genericNominalTypeDefinitions,
        ImmutableArray<BoundSourceGenericTypeDefinition>
            sourceGenericTypeDefinitions)
    {
        if (argument is null
            || argument.Kind != BoundInterfaceTypeArgumentKind.Constant
            || argument.Type is not null || argument.ConstantType is null
            || !AreEquivalent(argument.ConstantType, expectedConstantType)
            || !IsValidType(argument.ConstantType, owner, definitions,
                isValidType, visibleTypeParameterCount, methodFormals,
                visibleMethodTypeParameterCount, false, depth, ref visits,
                genericNominalTypeDefinitions,
                sourceGenericTypeDefinitions))
        {
            return false;
        }

        if (argument.ConstantValue is BoundConstant constant)
        {
            return argument.ConstantParameterScope
                    == BoundInterfaceTypeParameterScope.None
                && argument.ConstantParameterOrdinal == -1
                && argument.ConstantType.Kind == BoundInterfaceTypeKind.Primitive
                && constant.Type == argument.ConstantType.PrimitiveType
                && isValidType(constant.Type)
                && (BoundEnumTypes.IsType(constant.Type)
                    || PrimitiveTypeFacts.IsIntegerOrWide(constant.Type)
                    || PrimitiveTypeFacts.IsFloating(constant.Type)
                    || constant.Type == PrimitiveType.Bool);
        }

        if (argument.ConstantParameterScope == BoundInterfaceTypeParameterScope.Interface)
        {
            return (uint)argument.ConstantParameterOrdinal
                    < (uint)visibleTypeParameterCount
                && owner.Formals[argument.ConstantParameterOrdinal].Kind
                    == BoundInterfaceFormalKind.Constant
                && owner.Formals[argument.ConstantParameterOrdinal].ConstantType is
                    BoundInterfaceType constantType
                && AreEquivalent(argument.ConstantType, constantType);
        }
        if (argument.ConstantParameterScope == BoundInterfaceTypeParameterScope.Member)
        {
            return !methodFormals.IsDefault
                && (uint)argument.ConstantParameterOrdinal
                    < (uint)visibleMethodTypeParameterCount
                && (uint)argument.ConstantParameterOrdinal
                    < (uint)methodFormals.Length
                && methodFormals[argument.ConstantParameterOrdinal].Kind
                    == BoundInterfaceFormalKind.Constant
                && methodFormals[argument.ConstantParameterOrdinal].ConstantType is
                    BoundInterfaceType constantType
                && AreEquivalent(argument.ConstantType, constantType);
        }
        return false;
    }

    internal static BoundInterfaceType SubstituteTypeParameters(
        BoundInterfaceType type,
        IReadOnlyList<BoundInterfaceTypeArgument> arguments,
        int depth = 0)
    {
        if (depth > SemanticVerifier.MaximumDepth)
        {
            throw new NotSupportedException(
                "Interface generic substitution exceeds the type nesting limit.");
        }
        if (type.Kind == BoundInterfaceTypeKind.TypeParameter
            && type.TypeParameterScope == BoundInterfaceTypeParameterScope.Interface
            && (uint)type.TypeParameterOrdinal < (uint)arguments.Count
            && arguments[type.TypeParameterOrdinal] is
                { Kind: BoundInterfaceTypeArgumentKind.Type, Type: BoundInterfaceType actualType })
        {
            return actualType;
        }

        return type with
        {
            TypeArguments = [.. type.TypeArguments.Select(argument =>
                SubstituteTypeArgument(argument, arguments, depth + 1))],
        };
    }

    private static BoundInterfaceTypeArgument SubstituteTypeArgument(
        BoundInterfaceTypeArgument argument,
        IReadOnlyList<BoundInterfaceTypeArgument> arguments,
        int depth)
    {
        if (depth > SemanticVerifier.MaximumDepth)
        {
            throw new NotSupportedException(
                "Interface generic substitution exceeds the type nesting limit.");
        }
        if (argument.Kind == BoundInterfaceTypeArgumentKind.Constant
            && argument.ConstantParameterScope
                == BoundInterfaceTypeParameterScope.Interface
            && (uint)argument.ConstantParameterOrdinal < (uint)arguments.Count
            && arguments[argument.ConstantParameterOrdinal] is
                { Kind: BoundInterfaceTypeArgumentKind.Constant } actualConstant)
        {
            return actualConstant;
        }

        return argument with
        {
            Type = argument.Type is BoundInterfaceType type
                ? SubstituteTypeParameters(type, arguments, depth + 1) : null,
            ConstantType = argument.ConstantType is BoundInterfaceType constantType
                ? SubstituteTypeParameters(constantType, arguments, depth + 1) : null,
        };
    }

    internal static bool HasAcyclicBases(ImmutableArray<BoundInterfaceDefinition> interfaces)
    {
        if (interfaces.IsDefault)
        {
            return false;
        }
        var definitions = new Dictionary<InterfaceKey, BoundInterfaceDefinition>();
        foreach (var definition in interfaces)
        {
            if (definition is null || string.IsNullOrWhiteSpace(definition.QualifiedName)
                || definition.Formals.IsDefault
                || !definitions.TryAdd(Identity(definition.QualifiedName,
                    definition.Formals.Length, definition.ModuleImport), definition))
            {
                return false;
            }
        }
        return HasAcyclicBases(definitions);
    }

    private static bool HasAcyclicBases(
        IReadOnlyDictionary<InterfaceKey, BoundInterfaceDefinition> definitions)
    {
        var states = new Dictionary<InterfaceKey, byte>();
        foreach (var key in definitions.Keys)
        {
            if (!Visit(key, 0))
            {
                return false;
            }
        }
        return true;

        bool Visit(InterfaceKey key, int depth)
        {
            if (depth > SemanticVerifier.MaximumDepth || !definitions.TryGetValue(key, out var definition)
                || (states.TryGetValue(key, out var state) && state == 1))
            {
                return false;
            }
            if (state == 2)
            {
                return true;
            }
            states[key] = 1;
            foreach (var baseType in definition.Bases)
            {
                if (!Visit(Identity(baseType.NominalName!,
                    baseType.TypeArguments.Length, baseType.ModuleImport), depth + 1))
                {
                    return false;
                }
            }
            states[key] = 2;
            return true;
        }
    }
    private static InterfaceKey Identity(string name, int arity, BoundModuleImport? import) =>
        new(name, arity, import?.ProducerIdentity, import?.ExportKey,
            import?.TargetRequirement ?? 0, import?.RuntimePolicy ?? 0);

    private static bool TryGetGenericNominalTypeDefinition(
        BoundGenericNominalTypeIdentity identity,
        ImmutableArray<BoundGenericNominalTypeDefinition> definitions,
        out BoundGenericNominalTypeDefinition definition)
    {
        definition = null!;
        if (definitions.IsDefault)
        {
            return false;
        }

        foreach (var candidate in definitions)
        {
            if (candidate.Identity != identity)
            {
                continue;
            }
            if (definition is not null)
            {
                definition = null!;
                return false;
            }
            definition = candidate;
        }

        return definition is not null;
    }

    internal static bool AreEquivalent(BoundInterfaceType left, BoundInterfaceType right)
    {
        if (left.Kind != right.Kind || left.PrimitiveType != right.PrimitiveType
            || left.TypeParameterScope != right.TypeParameterScope
            || left.NominalName != right.NominalName
            || left.ModuleImport != right.ModuleImport
            || left.GenericNominalTypeIdentity
                != right.GenericNominalTypeIdentity
            || left.SourceGenericTypeDefinitionIndex
                != right.SourceGenericTypeDefinitionIndex
            || left.IsGenericNominalConstantDomain
                != right.IsGenericNominalConstantDomain
            || left.TypeParameterOrdinal != right.TypeParameterOrdinal
            || left.IsWritable != right.IsWritable || left.IsOptional != right.IsOptional
            || left.IsGarbageCollected != right.IsGarbageCollected
            || left.IsInitialization != right.IsInitialization
            || left.TypeArguments.Length != right.TypeArguments.Length)
        {
            return false;
        }
        for (var index = 0; index < left.TypeArguments.Length; index++)
        {
            if (!AreEquivalent(left.TypeArguments[index], right.TypeArguments[index]))
            {
                return false;
            }
        }
        return true;
    }

    internal static bool AreEquivalentProviderInterfaceViews(
        BoundInterfaceType expected, BoundInterfaceType provider)
    {
        var sameImportedNominal = expected.Kind
                == BoundInterfaceTypeKind.Nominal
            && provider.Kind == BoundInterfaceTypeKind.Nominal
            && expected.ModuleImport is not null
            && provider.ModuleImport is not null;
        if (expected.Kind != provider.Kind
            || expected.PrimitiveType != provider.PrimitiveType
            || expected.TypeParameterScope != provider.TypeParameterScope
            || (!sameImportedNominal
                && expected.NominalName != provider.NominalName)
            || expected.ModuleImport != provider.ModuleImport
            || expected.GenericNominalTypeIdentity
                != provider.GenericNominalTypeIdentity
            || expected.SourceGenericTypeDefinitionIndex
                != provider.SourceGenericTypeDefinitionIndex
            || expected.IsGenericNominalConstantDomain
                != provider.IsGenericNominalConstantDomain
            || expected.TypeParameterOrdinal
                != provider.TypeParameterOrdinal
            || expected.IsWritable != provider.IsWritable
            || expected.IsOptional != provider.IsOptional
            || expected.IsGarbageCollected
                != provider.IsGarbageCollected
            || expected.IsInitialization != provider.IsInitialization
            || expected.TypeArguments.IsDefault
            || provider.TypeArguments.IsDefault
            || expected.TypeArguments.Length != provider.TypeArguments.Length)
        {
            return false;
        }

        for (var ordinal = 0; ordinal < expected.TypeArguments.Length;
            ordinal++)
        {
            if (!AreEquivalentProviderInterfaceTypeArguments(
                    expected.TypeArguments[ordinal],
                    provider.TypeArguments[ordinal]))
            {
                return false;
            }
        }
        return true;
    }

    internal static bool AreEquivalentAssociatedTypeIdentities(
        BoundInterfaceMemberIdentity left,
        BoundInterfaceMemberIdentity right)
        => left is not null && right is not null
            && StringComparer.Ordinal.Equals(left.DeclarationKey,
                right.DeclarationKey)
            && AreEquivalentProviderInterfaceViews(
                left.DeclaringInterface, right.DeclaringInterface);

    private static bool AreEquivalentProviderInterfaceTypeArguments(
        BoundInterfaceTypeArgument expected,
        BoundInterfaceTypeArgument provider)
    {
        if (expected.Kind != provider.Kind
            || expected.ConstantParameterScope
                != provider.ConstantParameterScope
            || expected.ConstantParameterOrdinal
                != provider.ConstantParameterOrdinal)
        {
            return false;
        }

        if (expected.Kind == BoundInterfaceTypeArgumentKind.Type)
        {
            return expected.Type is not null
                && provider.Type is not null
                && AreEquivalentProviderInterfaceViews(expected.Type,
                    provider.Type);
        }
        if (expected.Kind == BoundInterfaceTypeArgumentKind.ExistentialType)
        {
            return expected.Type is null && provider.Type is null
                && expected.ConstantType is null
                && provider.ConstantType is null
                && expected.ConstantValue is null
                && provider.ConstantValue is null;
        }

        if (expected.ConstantType is null || provider.ConstantType is null
            || !AreEquivalentProviderInterfaceViews(
                expected.ConstantType, provider.ConstantType))
        {
            return false;
        }
        if (expected.ConstantValue is BoundConstant expectedConstant
            && provider.ConstantValue is BoundConstant providerConstant)
        {
            return expectedConstant.Type == providerConstant.Type
                && expectedConstant.Bits.Equals(providerConstant.Bits);
        }
        return expected.ConstantValue is null
            && provider.ConstantValue is null;
    }

    private static bool AreEquivalent(
        BoundInterfaceTypeArgument left, BoundInterfaceTypeArgument right)
    {
        if (left.Kind != right.Kind
            || left.ConstantParameterScope != right.ConstantParameterScope
            || left.ConstantParameterOrdinal != right.ConstantParameterOrdinal)
        {
            return false;
        }
        if (left.Kind == BoundInterfaceTypeArgumentKind.Type)
        {
            return left.Type is not null && right.Type is not null
                && AreEquivalent(left.Type, right.Type);
        }
        if (left.Kind == BoundInterfaceTypeArgumentKind.ExistentialType)
        {
            return left.Type is null && right.Type is null
                && left.ConstantType is null && right.ConstantType is null
                && left.ConstantValue is null && right.ConstantValue is null;
        }

        if (left.ConstantType is null || right.ConstantType is null
            || !AreEquivalent(left.ConstantType, right.ConstantType))
        {
            return false;
        }
        if (left.ConstantValue is BoundConstant leftConstant
            && right.ConstantValue is BoundConstant rightConstant)
        {
            return leftConstant.Type == rightConstant.Type
                && leftConstant.Bits.Equals(rightConstant.Bits);
        }
        return left.ConstantValue is null && right.ConstantValue is null;
    }
}
