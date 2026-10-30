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
        Func<PrimitiveType, bool> isValidType)
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
                        || (formal.TypeDefault is not null
                            && !IsValidType(formal.TypeDefault, definition, definitions,
                                isValidType, ordinal)))
                    {
                        return false;
                    }
                }
                else
                {
                    if (formal.TypeDefault is not null || formal.ConstantType is null)
                    {
                        return false;
                    }
                    var constantType = formal.ConstantType;
                    if (!IsValidType(constantType, definition, definitions,
                            isValidType, ordinal))
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
                            isValidType, definition.Formals.Length))
                    {
                        return false;
                    }
                }
                else if (constraint.Kind != BoundInterfaceConstraintKind.Attribute
                    || constraint.Type is not null || string.IsNullOrWhiteSpace(constraint.AttributeName)
                    || constraint.AttributeName.Contains('\0', StringComparison.Ordinal))
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
                        definition.Formals.Length))
                {
                    return false;
                }
                baseTypes.Add(baseType);
            }
            if (!IsValidMembers(definition, definitions, isValidType))
            {
                return false;
            }
        }

        return HasAcyclicBases(definitions);
    }

    private static bool IsValidMembers(
        BoundInterfaceDefinition owner,
        IReadOnlyDictionary<InterfaceKey, BoundInterfaceDefinition> definitions,
        Func<PrimitiveType, bool> isValidType)
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
                || member.Attributes.IsDefault || member.Attributes.Any(string.IsNullOrWhiteSpace)
                || member.AttributeTokens.IsDefault || member.AttributeTokens.Any(string.IsNullOrWhiteSpace)
                || member.ContractTokens.IsDefault
                || member.TypeParameters.IsDefault || member.TypeParameters.Length > SemanticVerifier.MaximumArguments
                || member.Constraints.IsDefault || member.Parameters.IsDefault
                || member.Parameters.Length > SemanticVerifier.MaximumArguments
                || member.Dependencies.IsDefault || member.Accessors.IsDefault
                || member.Accessors.Length > 3)
            {
                return false;
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
                            isValidType, owner.Formals.Length)))
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
                    || !IsValidType(member.AssociatedConstantType, owner, definitions,
                        isValidType, owner.Formals.Length)
                    || (member.AssociatedConstantDefault is BoundConstant constant
                        && (member.AssociatedConstantType.Kind != BoundInterfaceTypeKind.Primitive
                            || member.AssociatedConstantType.PrimitiveType != constant.Type
                            || !IsConstantDomainType(constant.Type, isValidType))))
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
                    member.TypeParameters, formalOrdinal)
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
                            owner.Formals.Length, member.TypeParameters, member.TypeParameters.Length))
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
                        or BoundInterfaceMemberKind.Operator))
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
                        owner.Formals.Length, member.TypeParameters, member.TypeParameters.Length))
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
                    or BoundDependencyRelation.Captures or BoundDependencyRelation.ForwardsBacking)
                || !seen.Add((contract.Relation, contract.Destination, contract.DestinationOrdinal))
                || (contract.Relation == BoundDependencyRelation.ForwardsBacking
                    && contract.Sources.IsEmpty))
            {
                return false;
            }

            var destinationValid = contract.Destination switch {
                BoundDependencyDestination.Result =>
                    hasResult && contract.DestinationOrdinal is null
                        && contract.Relation is BoundDependencyRelation.DependsOn
                            or BoundDependencyRelation.Captures
                            or BoundDependencyRelation.ForwardsBacking,
                BoundDependencyDestination.Receiver =>
                    hasReceiver && contract.DestinationOrdinal is null
                        && contract.Relation is BoundDependencyRelation.Captures
                            or BoundDependencyRelation.ForwardsBacking,
                BoundDependencyDestination.InitializedOutput =>
                    contract.DestinationOrdinal is int output
                        && (uint)output < (uint)parameters.Length
                        && parameters[output].Mode == BoundInterfaceParameterMode.Init
                        && contract.Relation is BoundDependencyRelation.DependsOn
                            or BoundDependencyRelation.Captures
                            or BoundDependencyRelation.ForwardsBacking,
                BoundDependencyDestination.RetainedFormal =>
                    contract.DestinationOrdinal is int formal
                        && (uint)formal < (uint)parameters.Length
                        && parameters[formal].Mode is not (BoundInterfaceParameterMode.Out
                            or BoundInterfaceParameterMode.Init)
                        && contract.Relation is BoundDependencyRelation.Captures
                            or BoundDependencyRelation.ForwardsBacking,
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
                        or BoundInterfaceParameterMode.Init)
                {
                    return false;
                }
            }
        }

        foreach (var forwarding in contracts)
        {
            if (forwarding.Relation != BoundDependencyRelation.ForwardsBacking)
            {
                continue;
            }
            var relatedSources = new HashSet<BoundDependencySource>();
            foreach (var relationship in contracts)
            {
                if (relationship.Destination == forwarding.Destination
                    && relationship.DestinationOrdinal == forwarding.DestinationOrdinal
                    && relationship.Relation is BoundDependencyRelation.DependsOn
                        or BoundDependencyRelation.Captures)
                {
                    relatedSources.UnionWith(relationship.Sources);
                }
            }
            if (forwarding.Sources.Any(source => !relatedSources.Contains(source)))
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
        int visibleMethodTypeParameterCount)
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
                        owner.Formals.Length, methodFormals, visibleMethodTypeParameterCount));
        }
        return formal.TypeDefault is null && formal.ConstantType is not null
            && IsValidType(formal.ConstantType, owner, definitions, isValidType,
                owner.Formals.Length, methodFormals, visibleMethodTypeParameterCount)
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
        bool allowVoid = false)
    {
        var visits = SemanticVerifier.MaximumAnalysisVisits;
        return IsValidType(type, owner, definitions, isValidType,
            visibleTypeParameterCount, methodFormals, visibleMethodTypeParameterCount,
            allowVoid, 0, ref visits);
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
        ref int visits)
    {
        if (type is null || depth > SemanticVerifier.MaximumDepth
            || --visits < 0 || type.TypeArguments.IsDefault
            || type.TypeArguments.Length > SemanticVerifier.MaximumArguments)
        {
            return false;
        }

        BoundInterfaceDefinition? nominalDefinition = null;
        switch (type.Kind)
        {
            case BoundInterfaceTypeKind.Primitive:
                return type.TypeParameterScope == BoundInterfaceTypeParameterScope.None
                    && type.NominalName is null && type.TypeParameterOrdinal == -1
                    && type.ModuleImport is null
                    && type.TypeArguments.IsEmpty && !type.IsWritable && !type.IsOptional
                    && !type.IsGarbageCollected && !type.IsInitialization
                    && (type.PrimitiveType == PrimitiveType.Void
                        ? allowVoid : isValidType(type.PrimitiveType));

            case BoundInterfaceTypeKind.TypeParameter:
                if (type.PrimitiveType != PrimitiveType.Void || type.NominalName is not null
                    || type.ModuleImport is not null || !type.TypeArguments.IsEmpty
                    || type.IsGarbageCollected || type.IsInitialization)
                {
                    return false;
                }
                return type.TypeParameterScope switch {
                    BoundInterfaceTypeParameterScope.Interface =>
                        (uint)type.TypeParameterOrdinal < (uint)visibleTypeParameterCount
                        && owner.Formals[type.TypeParameterOrdinal].Kind == BoundInterfaceFormalKind.Type,
                    BoundInterfaceTypeParameterScope.Member =>
                        !methodFormals.IsDefault
                        && (uint)type.TypeParameterOrdinal < (uint)visibleMethodTypeParameterCount
                        && (uint)type.TypeParameterOrdinal < (uint)methodFormals.Length
                        && methodFormals[type.TypeParameterOrdinal].Kind == BoundInterfaceFormalKind.Type,
                    _ => false,
                };

            case BoundInterfaceTypeKind.Nominal:
                if (type.TypeParameterScope != BoundInterfaceTypeParameterScope.None
                    || type.PrimitiveType != PrimitiveType.Void || type.NominalName is null
                    || type.TypeParameterOrdinal != -1 || type.IsGarbageCollected || type.IsInitialization
                    || !definitions.TryGetValue(Identity(type.NominalName,
                        type.TypeArguments.Length, type.ModuleImport), out nominalDefinition)
                    || nominalDefinition.Formals.Length != type.TypeArguments.Length)
                {
                    return false;
                }
                break;

            case BoundInterfaceTypeKind.CheckedReference:
                if (type.TypeParameterScope != BoundInterfaceTypeParameterScope.None
                    || type.PrimitiveType != PrimitiveType.Void || type.NominalName is not null
                    || type.TypeParameterOrdinal != -1 || type.ModuleImport is not null
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
                    if (argument is null
                        || argument.Kind != BoundInterfaceTypeArgumentKind.Type
                        || argument.Type is null || argument.ConstantType is not null
                        || argument.ConstantValue is not null
                        || argument.ConstantParameterScope
                            != BoundInterfaceTypeParameterScope.None
                        || argument.ConstantParameterOrdinal != -1
                        || !IsValidType(argument.Type, owner, definitions,
                            isValidType, visibleTypeParameterCount, methodFormals,
                            visibleMethodTypeParameterCount, false, depth + 1,
                            ref visits))
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
                        depth + 1, ref visits))
                {
                    return false;
                }
                precedingArguments.Add(argument);
            }
        }
        else if (type.Kind == BoundInterfaceTypeKind.CheckedReference
            && !IsValidType(type.TypeArguments[0].Type!, owner, definitions,
                isValidType, visibleTypeParameterCount, methodFormals,
                visibleMethodTypeParameterCount, false, depth + 1, ref visits))
        {
            return false;
        }
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
        ref int visits)
    {
        if (argument is null
            || argument.Kind != BoundInterfaceTypeArgumentKind.Constant
            || argument.Type is not null || argument.ConstantType is null
            || !AreEquivalent(argument.ConstantType, expectedConstantType)
            || !IsValidType(argument.ConstantType, owner, definitions,
                isValidType, visibleTypeParameterCount, methodFormals,
                visibleMethodTypeParameterCount, false, depth, ref visits))
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

    internal static bool AreEquivalent(BoundInterfaceType left, BoundInterfaceType right)
    {
        if (left.Kind != right.Kind || left.PrimitiveType != right.PrimitiveType
            || left.TypeParameterScope != right.TypeParameterScope
            || left.NominalName != right.NominalName
            || left.ModuleImport != right.ModuleImport
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
