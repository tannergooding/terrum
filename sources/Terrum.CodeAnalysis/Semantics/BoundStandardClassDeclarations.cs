// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Terrum.CodeAnalysis.Semantics;

internal sealed record BoundStandardClassFieldDeclaration(
    uint DefinitionId, uint OwnerDefinitionId, string Name, uint Flags,
    uint TypeSpecReference)
{
    internal ImmutableArray<string> ExportKeys { get; init; } = [];
    internal ImmutableArray<string> SelectedExportKeys { get; init; } = [];
    internal PrimitiveType Type { get; init; } = PrimitiveType.Void;
}

internal sealed record BoundStandardClassLayoutVariant(
    uint Row, uint OwnerDefinitionId, uint TargetProfileReference,
    uint TargetProfileId, uint TargetProfileRuleRevision, byte Kind,
    byte Flags, uint Alignment, uint Pack,
    ImmutableArray<uint> FieldDefinitionIds);

internal sealed record BoundStandardClassMethodParameter(
    string Name, uint TypeSpecReference, byte Mode, byte Flags,
    BoundConstant? DefaultValue);

internal readonly record struct BoundStandardClassMethodDependencyClause(
    byte DestinationKind, byte SourceKind, byte Relationship, byte Phase,
    uint DestinationOrdinal, uint SourceOrdinal);

internal readonly record struct BoundStandardClassMethodGenericConstraint(
    int Row, int OwnerGenericParamRow, ushort Kind, uint Payload);

internal sealed record BoundStandardClassMethodGenericParameter(
    int Row, uint OwnerDefinitionId, uint Ordinal, string Name,
    byte FormalKind, byte OwnershipDomain, byte NullabilityDomain,
    uint ConstantTypeSpecReference, uint DefaultArgumentReference,
    ImmutableArray<BoundStandardClassMethodGenericConstraint> Constraints);

internal sealed record BoundStandardClassMethodDeclaration(
    uint DefinitionId, string Name, uint Flags,
    uint EffectsMask, uint ReturnTypeSpecReference,
    uint ReceiverTypeSpecReference,
    ImmutableArray<BoundStandardClassMethodParameter> Parameters,
    ImmutableArray<string> ExportKeys, ImmutableArray<string> SelectedExportKeys,
    int GenericParameterCount, uint BodyRow, byte ConstructionRole,
    uint InitializerMethodDefinitionId,
    uint ConstructionPropertyDefinitionId,
    ImmutableArray<uint> InitializedMemberDefinitionIds)
{
    internal ImmutableArray<BoundStandardClassMethodDependencyClause>
        DependencyClauses
    {
        get; init;
    } = [];

    internal ImmutableArray<BoundStandardClassMethodGenericParameter>
        GenericParameters
    {
        get; init;
    } = [];
}

internal sealed record BoundStandardClassTypeView(
    BoundGenericNominalTypeIdentity Identity, string Namespace, string Name,
    uint TypeDefFlags,
    ImmutableArray<BoundStandardClassFieldDeclaration> Fields,
    ImmutableArray<BoundStandardClassLayoutVariant> Layouts,
    BoundSourceTypeProviderSnapshot Provider)
{
    internal ImmutableArray<BoundStandardClassMethodDeclaration> Methods
    {
        get; init;
    } = [];

    internal bool TryGetDeclaredMutability(out bool isMutable)
    {
        if (Provider is not null
            && (StringComparer.Ordinal.Equals(Provider.SchemaIdentity,
                    "trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1")
                || StandardGenericMethodSchema.IsCM1ClassParameterDefaults(
                    Provider.SchemaIdentity)
                || BoundStandardEnumDeclarationContract
                    .IsSelectedSchemaIdentity(Provider.SchemaIdentity)))
        {
            isMutable = (TypeDefFlags & 0x80) != 0;
            return true;
        }

        isMutable = false;
        return false;
    }
}

internal sealed record BoundStandardClassExpectation(
    BoundSourceTypeExpectation Binding,
    BoundStandardClassTypeView ExpectedDeclaration)
{
    internal ImmutableArray<BoundStandardClassFieldExpectation>
        FieldExpectations { get; init; } = [];
    internal ImmutableArray<BoundStandardClassMethodExpectation>
        MethodExpectations { get; init; } = [];
}

internal sealed record BoundStandardClassFieldExpectation(
    int ImportRow, int OwnerImportRow, uint ExpectedDefinitionId,
    uint ExpectedOwnerDefinitionId, uint ProviderDefinitionId,
    uint ProviderOwnerDefinitionId, string ExportKey,
    BoundSourceTypeProviderSnapshot Provider);

internal sealed record BoundStandardClassMethodExpectation(
    int ImportRow, int OwnerImportRow, uint ExpectedDefinitionId,
    uint ExpectedOwnerDefinitionId, uint ProviderDefinitionId,
    uint ProviderOwnerDefinitionId, string ExportKey,
    BoundSourceTypeProviderSnapshot Provider);

internal sealed record BoundStandardClassDeclaration(
    BoundStandardClassTypeView Declaration,
    ImmutableArray<string> ExportKeys,
    ImmutableArray<BoundStandardClassExpectation> Expectations)
{
    internal ImmutableArray<string> SelectedExportKeys { get; init; } = [];
}

internal static class BoundStandardClassDeclarationContract
{
    private const string C1SchemaIdentity
        = "trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1";
    private const string C1M1SchemaIdentity
        = "trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1";
    internal static bool TryGetSelectedConstruction2ClassDeclaration(
        PrimitiveType type,
        IReadOnlyList<BoundGenericNominalTypeInstance> instances,
        IReadOnlyList<BoundGenericNominalTypeDefinition> definitions,
        IReadOnlyList<BoundStandardClassDeclaration> declarations,
        out BoundStandardClassDeclaration declaration)
    {
        declaration = null!;
        if (!BoundGenericNominalTypeContract.IsNominalClassType(type,
                instances, definitions))
        {
            return false;
        }

        var instanceIndex = BoundGenericNominalTypeTypes.Index(type);
        if ((uint)instanceIndex >= (uint)instances.Count)
        {
            return false;
        }

        var instance = instances[instanceIndex];
        if (!instance.Arguments.IsEmpty
            || (instance.Qualifiers & ~0x01) != 0
            || instance.TypeSpecForm is not (0 or 0x02 or 0x03))
        {
            return false;
        }

        foreach (var candidate in declarations)
        {
            var selectedImportedClassView
                = !candidate.Expectations.IsEmpty
                    && StandardGenericMethodSchema
                        .IsCM1ClassParameterDefaults(
                            candidate.Declaration.Provider.SchemaIdentity);
            if (candidate.Declaration.Identity == instance.Definition
                && !candidate.SelectedExportKeys.IsEmpty
                && (candidate.Expectations.IsEmpty
                    || selectedImportedClassView)
                && (instance.TypeSpecReference == 0
                    ? instance.TypeSpecProviderIdentity is null
                        && instance.TypeSpecForm == 0
                    : instance.TypeSpecProviderIdentity
                            is string providerIdentity
                        && (instance.TypeSpecForm is 0x02 or 0x03)
                        && (StringComparer.Ordinal.Equals(
                                candidate.Declaration.Provider
                                    .ModuleIdentity, providerIdentity)
                            || candidate.Expectations.Any(expectation =>
                                StringComparer.Ordinal.Equals(
                                    expectation.Binding.UsingModule
                                        .ModuleIdentity,
                                    providerIdentity))))
            {
                declaration = candidate;
                return true;
            }
        }

        return false;
    }

    internal static bool IsValidTable(
        ImmutableArray<BoundStandardClassDeclaration> declarations,
        ImmutableArray<BoundGenericNominalTypeDefinition> nominalTypes)
    {
        if (declarations.IsDefault
            || declarations.Length > BoundGenericNominalTypeTypes.MaximumCount)
        {
            return false;
        }
        if (declarations.IsEmpty)
        {
            return true;
        }
        if (!BoundGenericNominalTypeContract.IsValidDefinitionTable(
                nominalTypes))
        {
            return false;
        }

        var definitions = nominalTypes.ToDictionary(
            static definition => definition.Identity);
        var identities = new HashSet<BoundGenericNominalTypeIdentity>();
        var fieldImportKeys = new HashSet<(
            string SchemaIdentity, string ModuleIdentity, int ImportRow)>();
        var methodImportKeys = new HashSet<(
            string SchemaIdentity, string ModuleIdentity, int ImportRow)>();
        var totalRows = 0L;
        foreach (var declaration in declarations)
        {
            if (declaration is null
                || !IsValidView(declaration.Declaration)
                || declaration.ExportKeys.IsDefault
                || declaration.Expectations.IsDefault
                || declaration.SelectedExportKeys.IsDefault
                || !identities.Add(declaration.Declaration.Identity)
                || !definitions.TryGetValue(declaration.Declaration.Identity,
                    out var definition)
                || definition.NominalKind != 2
                || !definition.Formals.IsEmpty
                || !StringComparer.Ordinal.Equals(definition.Namespace,
                    declaration.Declaration.Namespace)
                || !StringComparer.Ordinal.Equals(definition.Name,
                    declaration.Declaration.Name)
                || (!declaration.ExportKeys.IsEmpty
                    && (declaration.Declaration.TypeDefFlags & 3) != 2)
                || !IsUniqueText(declaration.ExportKeys)
                || !IsUniqueText(declaration.SelectedExportKeys)
                || declaration.SelectedExportKeys.Any(key =>
                    !declaration.ExportKeys.Contains(key,
                        StringComparer.Ordinal))
                || (!declaration.Expectations.IsEmpty
                    && !declaration.SelectedExportKeys.IsEmpty
                    && !StandardGenericMethodSchema
                        .IsCM1ClassParameterDefaults(
                            declaration.Declaration.Provider.SchemaIdentity)
                    && !BoundStandardEnumDeclarationContract
                        .IsSelectedClassInstanceLayoutSchemaIdentity(
                            declaration.Declaration.Provider
                                .SchemaIdentity))
            {
                return false;
            }
            totalRows += declaration.Declaration.Fields.Length
                + declaration.Declaration.Layouts.Length
                + declaration.Declaration.Methods.Length
                + declaration.Expectations.Length;
            foreach (var method in declaration.Declaration.Methods)
            {
                totalRows += method.Parameters.Length;
            }
            if (totalRows > SemanticVerifier.MaximumAnalysisVisits)
            {
                return false;
            }

            var expectationKeys = new HashSet<(
                string SchemaIdentity, string ModuleIdentity, int ImportRow)>();
            foreach (var expectation in declaration.Expectations)
            {
                if (expectation is null
                    || !IsValidView(expectation.ExpectedDeclaration)
                    || expectation.ExpectedDeclaration.Identity
                        == declaration.Declaration.Identity
                    || expectation.Binding is not BoundSourceTypeExpectation
                        binding
                    || binding.ImportRow <= 0
                    || binding.ExpectedDefinitionId
                        != expectation.ExpectedDeclaration.Identity.DefinitionId
                    || binding.ProviderDefinitionId
                        != declaration.Declaration.Identity.DefinitionId
                    || !expectationKeys.Add((
                        binding.UsingModule.SchemaIdentity,
                        binding.UsingModule.ModuleIdentity,
                        binding.ImportRow))
                    || expectation.FieldExpectations.IsDefault
                    || expectation.MethodExpectations.IsDefault
                    || (expectation.ExpectedDeclaration.TypeDefFlags & 3)
                        != 2
                    || (declaration.Declaration.TypeDefFlags & 3) != 2
                    || !StringComparer.Ordinal.Equals(
                        binding.ProducerIdentity,
                        declaration.Declaration.Identity.ProviderIdentity)
                    || !declaration.ExportKeys.Contains(binding.ExportKey,
                        StringComparer.Ordinal)
                    || !SnapshotsMatch(binding.UsingModule,
                        expectation.ExpectedDeclaration.Provider)
                    || !SnapshotsMatch(binding.Provider,
                        declaration.Declaration.Provider)
                    || !AreEquivalentDeclarationContracts(
                        expectation.ExpectedDeclaration,
                        declaration.Declaration)
                    || binding.TargetRequirement
                        != declaration.Declaration.Provider.TargetRequirement
                    || binding.RuntimePolicy
                        != declaration.Declaration.Provider.RuntimePolicy)
                {
                    return false;
                }

                totalRows += expectation.ExpectedDeclaration.Fields.Length
                    + expectation.ExpectedDeclaration.Layouts.Length
                    + expectation.ExpectedDeclaration.Methods.Length
                    + expectation.FieldExpectations.Length
                    + expectation.MethodExpectations.Length;
                foreach (var method
                    in expectation.ExpectedDeclaration.Methods)
                {
                    if (method.BodyRow != 0)
                    {
                        return false;
                    }
                    totalRows += method.Parameters.Length;
                }

                if (totalRows > SemanticVerifier.MaximumAnalysisVisits)
                {
                    return false;
                }

                var expectedFields = expectation.ExpectedDeclaration.Fields
                    .ToDictionary(static field => field.DefinitionId);
                var actualFields = declaration.Declaration.Fields
                    .ToDictionary(static field => field.DefinitionId);
                var fieldImportRows = new HashSet<int>();
                var providerFieldByExpectedDefinition = new Dictionary<
                    uint, BoundStandardClassFieldExpectation>();
                foreach (var fieldExpectation in expectation.FieldExpectations)
                {
                    if (fieldExpectation is null
                        || fieldExpectation.ImportRow <= 0
                        || fieldExpectation.OwnerImportRow != binding.ImportRow
                        || fieldExpectation.ExpectedOwnerDefinitionId
                            != binding.ExpectedDefinitionId
                        || fieldExpectation.ProviderOwnerDefinitionId
                            != declaration.Declaration.Identity.DefinitionId
                        || (fieldExpectation.ExpectedDefinitionId >> 24) != 0x14
                        || (fieldExpectation.ExpectedDefinitionId
                            & 0x00FF_FFFF) == 0
                        || (fieldExpectation.ProviderDefinitionId >> 24)
                            != 0x14
                        || (fieldExpectation.ProviderDefinitionId
                            & 0x00FF_FFFF) == 0
                        || !fieldImportRows.Add(fieldExpectation.ImportRow)
                        || !fieldImportKeys.Add((
                            binding.UsingModule.SchemaIdentity,
                            binding.UsingModule.ModuleIdentity,
                            fieldExpectation.ImportRow))
                        || !expectedFields.TryGetValue(
                            fieldExpectation.ExpectedDefinitionId,
                            out var expectedField)
                        || expectedField.OwnerDefinitionId
                            != fieldExpectation.ExpectedOwnerDefinitionId
                        || !actualFields.TryGetValue(
                            fieldExpectation.ProviderDefinitionId,
                            out var actualField)
                        || actualField.OwnerDefinitionId
                            != fieldExpectation.ProviderOwnerDefinitionId
                        || !actualField.ExportKeys.Contains(
                            fieldExpectation.ExportKey, StringComparer.Ordinal)
                        || !SnapshotsMatch(fieldExpectation.Provider,
                            declaration.Declaration.Provider))
                    {
                        return false;
                    }

                    if (providerFieldByExpectedDefinition.TryGetValue(
                            fieldExpectation.ExpectedDefinitionId,
                            out var existingFieldExpectation))
                    {
                        if (existingFieldExpectation.ProviderDefinitionId
                                != fieldExpectation.ProviderDefinitionId
                            || existingFieldExpectation
                                .ProviderOwnerDefinitionId
                                != fieldExpectation.ProviderOwnerDefinitionId
                            || !SnapshotsMatch(existingFieldExpectation.Provider,
                                fieldExpectation.Provider))
                        {
                            return false;
                        }
                    }
                    else
                    {
                        providerFieldByExpectedDefinition.Add(
                            fieldExpectation.ExpectedDefinitionId,
                            fieldExpectation);
                    }
                }

                var expectedMethods = expectation.ExpectedDeclaration.Methods
                    .ToDictionary(static method => method.DefinitionId);
                var actualMethods = declaration.Declaration.Methods
                    .ToDictionary(static method => method.DefinitionId);
                var methodImportRows = new HashSet<int>();
                foreach (var methodExpectation
                    in expectation.MethodExpectations)
                {
                    if (methodExpectation is null
                        || methodExpectation.ImportRow <= 0
                        || methodExpectation.OwnerImportRow
                            != binding.ImportRow
                        || methodExpectation.ExpectedOwnerDefinitionId
                            != binding.ExpectedDefinitionId
                        || methodExpectation.ProviderOwnerDefinitionId
                            != declaration.Declaration.Identity
                                .DefinitionId
                        || (methodExpectation.ExpectedDefinitionId >> 24)
                            != 0x16
                        || (methodExpectation.ExpectedDefinitionId
                            & 0x00FF_FFFF) == 0
                        || (methodExpectation.ProviderDefinitionId >> 24)
                            != 0x16
                        || (methodExpectation.ProviderDefinitionId
                            & 0x00FF_FFFF) == 0
                        || !methodImportRows.Add(
                            methodExpectation.ImportRow)
                        || !methodImportKeys.Add((
                            binding.UsingModule.SchemaIdentity,
                            binding.UsingModule.ModuleIdentity,
                            methodExpectation.ImportRow))
                        || !expectedMethods.TryGetValue(
                            methodExpectation.ExpectedDefinitionId,
                            out var expectedMethod)
                        || !actualMethods.TryGetValue(
                            methodExpectation.ProviderDefinitionId,
                            out var actualMethod)
                        || !actualMethod.ExportKeys.Contains(
                            methodExpectation.ExportKey,
                            StringComparer.Ordinal)
                        || !SnapshotsMatch(methodExpectation.Provider,
                            declaration.Declaration.Provider)
                        || expectedMethod.ConstructionRole
                            != actualMethod.ConstructionRole
                        || expectedMethod.Parameters.Length
                            != actualMethod.Parameters.Length)
                    {
                        return false;
                    }

                    for (var parameter = 0;
                        parameter < expectedMethod.Parameters.Length;
                        parameter++)
                    {
                        var expectedParameter
                            = expectedMethod.Parameters[parameter];
                        var actualParameter
                            = actualMethod.Parameters[parameter];
                        if (expectedParameter.Mode != actualParameter.Mode
                            || expectedParameter.Flags
                                != actualParameter.Flags)
                        {
                            return false;
                        }
                    }
                }

            }
        }

        return true;
    }

    private static bool IsValidView(BoundStandardClassTypeView view)
    {
        if (view is null || !view.Identity.IsValid
            || (view.Identity.DefinitionId >> 24) != 0x13
            || !IsValidNamespace(view.Namespace) || !IsValidText(view.Name)
            || view.Fields.IsDefault || view.Layouts.IsDefault
            || view.Methods.IsDefault
            || !IsValidSnapshot(view.Provider)
            || !StringComparer.Ordinal.Equals(view.Identity.ModuleIdentity,
                view.Provider.ModuleIdentity)
            || !StringComparer.Ordinal.Equals(view.Provider.SchemaIdentity,
                C1SchemaIdentity)
                && !StringComparer.Ordinal.Equals(view.Provider.SchemaIdentity,
                    C1M1SchemaIdentity)
                && !StandardGenericMethodSchema
                    .IsCM1ClassParameterDefaults(
                        view.Provider.SchemaIdentity)
                && !BoundStandardEnumDeclarationContract
                    .IsSelectedSchemaIdentity(
                        view.Provider.SchemaIdentity))
        {
            return false;
        }

        var kind = (view.TypeDefFlags >> 2) & 7;
        var schemaAllowsMutability
            = StringComparer.Ordinal.Equals(view.Provider.SchemaIdentity,
                C1M1SchemaIdentity)
                || StandardGenericMethodSchema
                    .IsCM1ClassParameterDefaults(
                        view.Provider.SchemaIdentity)
                || BoundStandardEnumDeclarationContract
                    .IsSelectedSchemaIdentity(
                        view.Provider.SchemaIdentity);
        uint allowedFlags = schemaAllowsMutability ? 0x8Fu : 0x0Fu;
        if (kind != 2 || (view.TypeDefFlags & ~allowedFlags) != 0
            || (view.TypeDefFlags & 3) == 3)
        {
            return false;
        }

        var fields = new Dictionary<uint, BoundStandardClassFieldDeclaration>();
        foreach (var field in view.Fields)
        {
            if (field is null || (field.DefinitionId >> 24) != 0x14
                || (field.DefinitionId & 0x00FF_FFFF) == 0
                || field.OwnerDefinitionId != view.Identity.DefinitionId
                || !IsValidText(field.Name)
                || !BoundGenericNominalTypeContract.IsTypeSpecReference(
                    field.TypeSpecReference)
                || field.ExportKeys.IsDefault
                || !IsUniqueText(field.ExportKeys)
                || field.SelectedExportKeys.IsDefault
                || !IsUniqueText(field.SelectedExportKeys)
                || field.SelectedExportKeys.Any(key =>
                    !field.ExportKeys.Contains(key,
                        StringComparer.Ordinal))
                || !fields.TryAdd(field.DefinitionId, field))
            {
                return false;
            }
        }

        var methods = new Dictionary<uint,
            BoundStandardClassMethodDeclaration>();
        var genericParameterRows = new HashSet<int>();
        var genericConstraintRows = new HashSet<int>();
        var previousMethodRow = 0u;
        foreach (var method in view.Methods)
        {
            if (method is null
                || (method.DefinitionId >> 24) != 0x16
                || (method.DefinitionId & 0x00FF_FFFF) == 0
                || method.DefinitionId <= previousMethodRow
                || !IsValidText(method.Name)
                || (method.Flags & ~7u) != 0
                || (method.Flags & 3u) == 3
                || (method.EffectsMask & ~6u) != 0
                || !BoundGenericNominalTypeContract.IsTypeSpecReference(
                    method.ReturnTypeSpecReference)
                || (method.ReceiverTypeSpecReference != 0
                    && !BoundGenericNominalTypeContract.IsTypeSpecReference(
                        method.ReceiverTypeSpecReference))
                || method.Parameters.IsDefault
                || method.Parameters.Length
                    > SemanticVerifier.MaximumArguments
                || method.DependencyClauses.IsDefault
                || method.DependencyClauses.Length
                    > SemanticVerifier.MaximumAnalysisVisits
                || method.ExportKeys.IsDefault
                || !IsUniqueText(method.ExportKeys)
                || method.SelectedExportKeys.IsDefault
                || !IsUniqueText(method.SelectedExportKeys)
                || method.SelectedExportKeys.Any(key =>
                    !method.ExportKeys.Contains(key,
                        StringComparer.Ordinal))
                || method.GenericParameterCount < 0
                || method.GenericParameters.IsDefault
                || method.GenericParameters.Length
                    != method.GenericParameterCount
                || method.BodyRow > 0x00FF_FFFF
                || method.ConstructionRole > 3
                || (method.ConstructionRole == 2
                    ? (method.InitializerMethodDefinitionId >> 24) != 0x16
                        || (method.InitializerMethodDefinitionId
                            & 0x00FF_FFFF) == 0
                    : method.InitializerMethodDefinitionId != 0)
                || (method.ConstructionRole == 3
                    ? (method.ConstructionPropertyDefinitionId >> 24)
                            != 0x1E
                        || (method.ConstructionPropertyDefinitionId
                            & 0x00FF_FFFF) == 0
                    : method.ConstructionPropertyDefinitionId != 0)
                || method.InitializedMemberDefinitionIds.IsDefault
                || !methods.TryAdd(method.DefinitionId, method))
            {
                return false;
            }

            var genericNames = new HashSet<string>(StringComparer.Ordinal);
            for (var ordinal = 0;
                ordinal < method.GenericParameters.Length; ordinal++)
            {
                var formal = method.GenericParameters[ordinal];
                if (formal is null || formal.Row <= 0
                    || formal.Row > 0x00FF_FFFF
                    || !genericParameterRows.Add(formal.Row)
                    || (ordinal != 0
                        && formal.Row
                            != method.GenericParameters[ordinal - 1].Row + 1)
                    || formal.OwnerDefinitionId != method.DefinitionId
                    || formal.Ordinal != (uint)ordinal
                    || !IsValidText(formal.Name)
                    || !genericNames.Add(formal.Name)
                    || formal.Constraints.IsDefault
                    || formal.FormalKind is not (1 or 2))
                {
                    return false;
                }

                if (formal.FormalKind == 1
                    ? formal.OwnershipDomain is < 1 or > 3
                        || formal.NullabilityDomain is < 1 or > 2
                        || formal.ConstantTypeSpecReference != 0
                        || (formal.DefaultArgumentReference != 0
                            && !BoundGenericNominalTypeContract
                                .IsTypeSpecReference(
                                    formal.DefaultArgumentReference))
                    : formal.OwnershipDomain != 0
                        || formal.NullabilityDomain != 0
                        || !BoundGenericNominalTypeContract
                            .IsTypeSpecReference(
                                formal.ConstantTypeSpecReference)
                        || !formal.Constraints.IsEmpty)
                {
                    return false;
                }

                for (var constraintOrdinal = 0;
                    constraintOrdinal < formal.Constraints.Length;
                    constraintOrdinal++)
                {
                    var constraint = formal.Constraints[constraintOrdinal];
                    if (constraint.Row <= 0
                        || constraint.Row > 0x00FF_FFFF
                        || !genericConstraintRows.Add(constraint.Row)
                        || (constraintOrdinal != 0
                            && constraint.Row
                                != formal.Constraints[
                                    constraintOrdinal - 1].Row + 1)
                        || constraint.OwnerGenericParamRow
                            != formal.Row
                        || constraint.Kind is not (1 or 2 or 3)
                        || constraint.Payload == 0
                        || (constraint.Kind == 1
                            && !BoundGenericNominalTypeContract
                                .IsTypeSpecReference(constraint.Payload))
                        || (constraint.Kind == 2
                            && constraint.Payload > 3))
                    {
                        return false;
                    }
                }
            }

            var parameterNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (var parameter in method.Parameters)
            {
                if (parameter is null || !IsValidText(parameter.Name)
                    || !BoundGenericNominalTypeContract
                        .IsTypeSpecReference(
                            parameter.TypeSpecReference)
                    || parameter.Mode > 3
                    || (parameter.Flags & ~1) != 0
                    || !parameterNames.Add(parameter.Name)
                    || (parameter.DefaultValue is BoundConstant value
                        && (value.RepresentationType != value.Type
                            || !value.Bits.IsCanonicalFor(value.Type)
                            || (value.Type is PrimitiveType.NativeInt
                                or PrimitiveType.NativeUInt))))
                {
                    return false;
                }
            }

            var dependencyKeys = new HashSet<(
                byte DestinationKind, uint DestinationOrdinal, byte Phase,
                byte SourceKind, uint SourceOrdinal)>();
            var emptyDependencyDestinations
                = new HashSet<(byte Kind, uint Ordinal, byte Phase)>();
            var sourcedDependencyDestinations
                = new HashSet<(byte Kind, uint Ordinal, byte Phase)>();
            foreach (var dependency in method.DependencyClauses)
            {
                var destinationValid = dependency.DestinationKind switch
                {
                    1 => dependency.DestinationOrdinal == 0,
                    2 or 3 => dependency.DestinationOrdinal
                        < (uint)method.Parameters.Length,
                    4 => dependency.DestinationOrdinal == 0
                        && method.ReceiverTypeSpecReference != 0,
                    _ => false,
                };
                var sourceValid = dependency.SourceKind switch
                {
                    0 => dependency.Relationship == 0
                        && dependency.SourceOrdinal == 0,
                    1 => dependency.Relationship is >= 1 and <= 3
                        && dependency.SourceOrdinal
                            < (uint)method.Parameters.Length,
                    2 => dependency.Relationship is >= 1 and <= 3
                        && dependency.SourceOrdinal == 0
                        && method.ReceiverTypeSpecReference != 0,
                    _ => false,
                };
                if (dependency.Phase is < 1 or > 3
                    || !destinationValid || !sourceValid
                    || !dependencyKeys.Add((
                        dependency.DestinationKind,
                        dependency.DestinationOrdinal, dependency.Phase,
                        dependency.SourceKind, dependency.SourceOrdinal)))
                {
                    return false;
                }

                var destinationKey = (dependency.DestinationKind,
                    dependency.DestinationOrdinal, dependency.Phase);
                _ = dependency.SourceKind == 0
                    ? emptyDependencyDestinations.Add(destinationKey)
                    : sourcedDependencyDestinations.Add(destinationKey);
            }
            if (emptyDependencyDestinations.Overlaps(
                    sourcedDependencyDestinations))
            {
                return false;
            }

            if (method.ConstructionRole != 1
                && !method.InitializedMemberDefinitionIds.IsEmpty)
            {
                return false;
            }
            if (method.ConstructionRole == 1)
            {
                var initializedMembers = new HashSet<uint>();
                foreach (var memberDefinitionId
                    in method.InitializedMemberDefinitionIds)
                {
                    if ((memberDefinitionId >> 24) != 0x14
                        || !fields.ContainsKey(memberDefinitionId)
                        || !initializedMembers.Add(memberDefinitionId))
                    {
                        return false;
                    }
                }
            }

            previousMethodRow = method.DefinitionId;
        }

        foreach (var method in methods.Values)
        {
            if (method.ConstructionRole != 2)
            {
                continue;
            }

            if (!methods.TryGetValue(method.InitializerMethodDefinitionId,
                    out var initializer)
                || initializer.ConstructionRole != 1
                || initializer.Parameters.Length != method.Parameters.Length)
            {
                return false;
            }

            for (var ordinal = 0;
                ordinal < method.Parameters.Length; ordinal++)
            {
                var initializerParameter
                    = initializer.Parameters[ordinal];
                var projectionParameter = method.Parameters[ordinal];
                if (!StringComparer.Ordinal.Equals(
                        initializerParameter.Name,
                        projectionParameter.Name)
                    || initializerParameter.Mode
                        != projectionParameter.Mode
                    || initializerParameter.Flags
                        != projectionParameter.Flags
                    || !AreEquivalentFrozenDefaults(
                        initializerParameter.DefaultValue,
                        projectionParameter.DefaultValue))
                {
                    return false;
                }
            }
        }

        var layoutRows = new HashSet<uint>();
        var targetProfiles = new HashSet<uint>();
        var hasDefault = false;
        foreach (var layout in view.Layouts)
        {
            if (layout is null || layout.Row == 0
                || layout.OwnerDefinitionId != view.Identity.DefinitionId
                || !layoutRows.Add(layout.Row)
                || layout.TargetProfileReference
                    > (uint)view.Provider.TargetProfiles.Length
                || !MatchesTargetProfile(layout, view.Provider)
                || layout.Kind > 4 || (layout.Flags & ~1) != 0
                || layout.Alignment > 8192
                || (layout.Alignment != 0
                    && (layout.Alignment & (layout.Alignment - 1)) != 0)
                || (layout.Kind is 1 or 2
                    ? layout.Pack is not (1 or 2 or 4 or 8 or 16)
                    : layout.Pack is not (0 or 1 or 2 or 4 or 8 or 16))
                || ((layout.Flags & 1) != 0
                    && layout.TargetProfileReference == 0)
                || (layout.Kind is 0 or 3
                    && (layout.Alignment != 0 || layout.Pack != 0))
                || layout.FieldDefinitionIds.IsDefault
                || !targetProfiles.Add(layout.TargetProfileReference))
            {
                return false;
            }

            hasDefault |= layout.TargetProfileReference == 0;
            var listedFields = new HashSet<uint>();
            foreach (var fieldId in layout.FieldDefinitionIds)
            {
                if (!fields.TryGetValue(fieldId, out var field)
                    || (field.Flags & 8) != 0
                    || !listedFields.Add(fieldId))
                {
                    return false;
                }
            }

            if (listedFields.Count != fields.Values.Count(
                    static field => (field.Flags & 8) == 0))
            {
                return false;
            }
        }

        return hasDefault;
    }

    private static bool AreEquivalentFrozenDefaults(
        BoundConstant? left, BoundConstant? right)
        => left is null ? right is null
            : right is not null && left.Type == right.Type
                && left.RepresentationType == right.RepresentationType
                && left.Bits.Equals(right.Bits);

    private static bool AreEquivalentDeclarationContracts(
        BoundStandardClassTypeView expected,
        BoundStandardClassTypeView actual)
    {
        if (!StringComparer.Ordinal.Equals(expected.Namespace,
                actual.Namespace)
            || !StringComparer.Ordinal.Equals(expected.Name, actual.Name)
            || expected.TypeDefFlags != actual.TypeDefFlags
            || expected.Fields.Length != actual.Fields.Length
            || expected.Methods.Length != actual.Methods.Length
            || expected.Layouts.Length != actual.Layouts.Length)
        {
            return false;
        }

        for (var ordinal = 0; ordinal < expected.Fields.Length; ordinal++)
        {
            var expectedField = expected.Fields[ordinal];
            var actualField = actual.Fields[ordinal];
            if (!StringComparer.Ordinal.Equals(expectedField.Name,
                    actualField.Name)
                || expectedField.Flags != actualField.Flags)
            {
                return false;
            }
        }

        for (var ordinal = 0; ordinal < expected.Methods.Length; ordinal++)
        {
            var expectedMethod = expected.Methods[ordinal];
            var actualMethod = actual.Methods[ordinal];
            if (!StringComparer.Ordinal.Equals(expectedMethod.Name,
                    actualMethod.Name)
                || expectedMethod.Flags != actualMethod.Flags
                || expectedMethod.EffectsMask != actualMethod.EffectsMask
                || expectedMethod.GenericParameterCount
                    != actualMethod.GenericParameterCount
                || expectedMethod.ConstructionRole
                    != actualMethod.ConstructionRole
                || expectedMethod.Parameters.Length
                    != actualMethod.Parameters.Length
                || expectedMethod.InitializedMemberDefinitionIds.Length
                    != actualMethod.InitializedMemberDefinitionIds.Length
                || (expectedMethod.ConstructionPropertyDefinitionId != 0)
                    != (actualMethod.ConstructionPropertyDefinitionId != 0))
            {
                return false;
            }

            for (var parameter = 0;
                parameter < expectedMethod.Parameters.Length; parameter++)
            {
                var expectedParameter
                    = expectedMethod.Parameters[parameter];
                var actualParameter
                    = actualMethod.Parameters[parameter];
                if (expectedParameter.Mode != actualParameter.Mode
                    || expectedParameter.Flags != actualParameter.Flags)
                {
                    return false;
                }
            }
        }

        foreach (var expectedLayout in expected.Layouts)
        {
            var actualLayout = actual.Layouts.SingleOrDefault(layout =>
                layout.TargetProfileId == expectedLayout.TargetProfileId
                && layout.TargetProfileRuleRevision
                    == expectedLayout.TargetProfileRuleRevision);
            if (actualLayout is null
                || expectedLayout.Kind != actualLayout.Kind
                || expectedLayout.Flags != actualLayout.Flags
                || expectedLayout.Alignment != actualLayout.Alignment
                || expectedLayout.Pack != actualLayout.Pack
                || !LayoutFieldsMatch(expectedLayout, expected, actualLayout,
                    actual))
            {
                return false;
            }
        }

        return true;
    }

    private static bool LayoutFieldsMatch(
        BoundStandardClassLayoutVariant expectedLayout,
        BoundStandardClassTypeView expected,
        BoundStandardClassLayoutVariant actualLayout,
        BoundStandardClassTypeView actual)
    {
        if (expectedLayout.FieldDefinitionIds.Length
            != actualLayout.FieldDefinitionIds.Length)
        {
            return false;
        }

        for (var ordinal = 0;
            ordinal < expectedLayout.FieldDefinitionIds.Length; ordinal++)
        {
            var expectedFieldOrdinal = FindFieldOrdinal(expected,
                expectedLayout.FieldDefinitionIds[ordinal]);
            var actualFieldOrdinal = FindFieldOrdinal(actual,
                actualLayout.FieldDefinitionIds[ordinal]);
            if (expectedFieldOrdinal != actualFieldOrdinal)
            {
                return false;
            }
        }

        return true;
    }

    private static int FindFieldOrdinal(BoundStandardClassTypeView view,
        uint fieldDefinitionId)
    {
        for (var ordinal = 0; ordinal < view.Fields.Length; ordinal++)
        {
            if (view.Fields[ordinal].DefinitionId == fieldDefinitionId)
            {
                return ordinal;
            }
        }

        return -1;
    }

    private static bool MatchesTargetProfile(
        BoundStandardClassLayoutVariant layout,
        BoundSourceTypeProviderSnapshot provider)
    {
        if (layout.TargetProfileReference == 0)
        {
            return layout.TargetProfileId == 0
                && layout.TargetProfileRuleRevision == 0;
        }

        var profile = provider.TargetProfiles[
            checked((int)layout.TargetProfileReference - 1)];
        return layout.TargetProfileId == profile.ProfileId
            && layout.TargetProfileRuleRevision == profile.RuleRevision;
    }

    private static bool IsValidSnapshot(
        BoundSourceTypeProviderSnapshot snapshot)
        => snapshot is not null
            && snapshot.ArtifactSnapshot is { IsDefault: false, Length: > 0 }
            && snapshot.ModuleRow > 0
            && IsValidText(snapshot.SchemaIdentity)
            && IsValidText(snapshot.ModuleIdentity)
            && snapshot.Features is { IsDefault: false }
            && snapshot.TargetProfiles is { IsDefault: false };

    private static bool SnapshotsMatch(
        BoundSourceTypeProviderSnapshot left,
        BoundSourceTypeProviderSnapshot right)
        => left is not null && right is not null
            && StringComparer.Ordinal.Equals(left.SchemaIdentity,
                right.SchemaIdentity)
            && left.ModuleRow == right.ModuleRow
            && StringComparer.Ordinal.Equals(left.ModuleIdentity,
                right.ModuleIdentity)
            && left.GoverningRuleEdition == right.GoverningRuleEdition
            && left.TargetRequirement == right.TargetRequirement
            && left.RuntimePolicy == right.RuntimePolicy
            && left.Features.AsSpan().SequenceEqual(right.Features.AsSpan())
            && left.TargetProfiles.AsSpan().SequenceEqual(
                right.TargetProfiles.AsSpan())
            && left.ArtifactSnapshot.AsSpan().SequenceEqual(
                right.ArtifactSnapshot.AsSpan());

    private static bool IsUniqueText(ImmutableArray<string> values)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        return values.All(value => IsValidText(value) && seen.Add(value));
    }

    private static bool IsValidText(string value)
        => value is { Length: > 0 and <= 4096 }
            && value.IndexOf('\0') < 0;

    private static bool IsValidNamespace(string value)
        => value is { Length: <= 4096 }
            && value.IndexOf('\0') < 0;
}
