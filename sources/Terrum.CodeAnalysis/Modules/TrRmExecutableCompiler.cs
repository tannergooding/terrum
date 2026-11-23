// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using Terrum.CodeAnalysis.Lexing;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Text;

namespace Terrum.CodeAnalysis.Modules;

/// <summary>Explicitly assigns an ExportKey to one source-authored nominal type declaration.</summary>
/// <param name="Namespace">The declaration's exact source namespace, or empty for the global namespace.</param>
/// <param name="Name">The declaration's exact simple source name.</param>
/// <param name="ExportKey">The producer-selected TRRM export key; it is not derived from the source name.</param>
public sealed record TrRmSourceTypeExport(
    string Namespace, string Name, string ExportKey)
{
    /// <summary>Gets the selected generic arity, or -1 when not specified.</summary>
    public int GenericArity { get; init; } = -1;

    /// <summary>
    /// Gets the complete, declaration-ordered enum member key assignment.
    /// The default value means this mapping does not assign enum members.
    /// </summary>
    public ImmutableArray<string> EnumMemberDeclarationKeys { get; init; }

    /// <summary>Gets the explicitly assigned public FieldDef ExportKeys.</summary>
    public ImmutableArray<TrRmSourceFieldExport> FieldExports { get; init; }

    /// <summary>Gets the source-anchored InterfaceMethod ExportKeys.</summary>
    public ImmutableArray<TrRmSourceInterfaceMethodExport>
        InterfaceMethodExports { get; init; }

    /// <summary>Gets the source-anchored associated-type ExportKeys.</summary>
    public ImmutableArray<TrRmSourceInterfaceAssociatedTypeExport>
        InterfaceAssociatedTypeExports { get; init; }
}

/// <summary>Explicitly assigns an ExportKey to one direct source field declaration.</summary>
/// <param name="Name">The field's exact simple source name within its selected owner.</param>
/// <param name="ExportKey">The producer-selected TRRM export key; it is not derived from the field name.</param>
public sealed record TrRmSourceFieldExport(string Name, string ExportKey);

/// <summary>Explicitly assigns an ExportKey to one direct source interface requirement.</summary>
/// <param name="SourceInputOrdinal">The zero-based source input containing the declaration.</param>
/// <param name="DeclarationSpan">The exact complete requirement declaration span.</param>
/// <param name="ExportKey">The producer-selected MethodDef ExportKey.</param>
public sealed record TrRmSourceInterfaceMethodExport(
    int SourceInputOrdinal, TextSpan DeclarationSpan, string ExportKey);

/// <summary>Explicitly assigns an ExportKey to one direct associated-type declaration.</summary>
/// <param name="SourceInputOrdinal">The zero-based source input containing the declaration.</param>
/// <param name="DeclarationSpan">The exact complete associated-type declaration span.</param>
/// <param name="ExportKey">The producer-selected AssociatedTypeDef ExportKey.</param>
public sealed record TrRmSourceInterfaceAssociatedTypeExport(
    int SourceInputOrdinal, TextSpan DeclarationSpan, string ExportKey);

/// <summary>Emits and audits root-free fail-fast scalar compact TRRM bodies.</summary>
public static partial class TrRmExecutableCompiler
{
    /// <summary>Classifies an exception marked at an A3 admission failure origin.</summary>
    /// <param name="exception">The exception to inspect.</param>
    /// <param name="kind">The marked A3 failure kind, or <see cref="TrRmA3AdmissionFailureKind.None"/>.</param>
    /// <returns>Whether the exact exception instance carries an A3 pre-execution admission marker.</returns>
    public static bool TryGetA3AdmissionFailure(Exception exception,
        out TrRmA3AdmissionFailureKind kind)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return TrRmA3AdmissionFailureMarkers.TryGet(exception, out kind);
    }

    private const string StandardLayoutQueriesSchemaIdentity
        = "trrm/1-draft-layout-queries-1";
    private const string StandardNominalEnumsSchemaIdentity
        = "trrm/1-draft-nominal-enums-1";
    private const string StandardForeignInvocationSchemaIdentity
        = "trrm/1-draft-foreign-invocation-contracts-1";
    private const string StandardForeignNativeInterfaceSchemaIdentity
        = "trrm/1-draft-foreign-native-interface-contracts-1";
    private const string StandardForeignNativeInterfaceParameterSurfaceSchemaIdentity
        = "trrm/1-draft-foreign-native-interface-parameter-surface-1";
    private const string StandardForeignNativeInterfaceAggregateOutputConsumptionSchemaIdentity
        = "trrm/1-draft-foreign-native-interface-aggregate-output-consumption-1";
    private const string StandardForeignInvocationStorageAddressSchemaIdentity
        = "trrm/1-draft-foreign-invocation-storage-addresses-1";
    private const string StandardForeignInvocationStorageFixedArraySchemaIdentity
        = "trrm/1-draft-foreign-invocation-storage-fixed-arrays-2";
    private const string StandardForeignInvocationStorageFixedArrayEmptyInitializationSchemaIdentity
        = "trrm/1-draft-foreign-invocation-storage-fixed-arrays-2-empty-initialization-1";
    private const string StandardForeignInvocationStorageFixedArrayEmptyUnitInitializationSchemaIdentity
        = "trrm/1-draft-foreign-invocation-storage-fixed-arrays-2-empty-unit-initialization-1";
    private const string StandardForeignInvocationStorageFixedArrayEmptyUnitScalarFrozenValuesSchemaIdentity
        = "trrm/1-draft-foreign-invocation-storage-fixed-arrays-2-empty-unit-initialization-scalar-frozen-values-1";
    private const string StandardForeignInvocationStorageFixedArrayCopyReturnCoverageSchemaIdentity
        = "trrm/1-draft-foreign-invocation-storage-fixed-arrays-2-empty-unit-initialization-scalar-frozen-values-copy-origin-return-coverage-1";
    private const string StandardForeignInvocationStorageFixedArrayCopyReturnCoverageRepeatInitializationSchemaIdentity
        = "trrm/1-draft-foreign-invocation-storage-fixed-arrays-2-empty-unit-initialization-scalar-frozen-values-copy-origin-return-coverage-repeat-initialization-1";
    private const string StandardLocalAggregateLayoutSchemaIdentity
        = "trrm/1-draft-local-aggregate-layout-execution-1";
    private const string StandardLocalAggregatePointerLoadsSchemaIdentity
        = "trrm/1-draft-local-aggregate-layout-execution-pointer-loads-1";
    private const string StandardForeignInvocationStorageGenericFixedArraySchemaIdentity
        = "trrm/1-draft-foreign-invocation-storage-fixed-arrays-3-unique-1";
    private const string StandardRegisterExceptionRegionsSchemaIdentity
        = "trrm/1-draft-register-exception-regions-1";
    private const string StandardGenericMethodCallsSchemaIdentity
        = StandardGenericMethodSchema.GenericMethodCalls;
    private const string StandardGenericMethodCallsParameterLabelsSchemaIdentity
        = StandardGenericMethodSchema.GenericMethodCallsParameterLabels;
    private const string StandardGenericMethodCallsParameterLabelsReceiverAccessSchemaIdentity
        = StandardGenericMethodSchema
            .GenericMethodCallsParameterLabelsReceiverAccess;
    private const string StandardInitReferenceBodiesSchemaIdentity
        = "trrm/1-draft-init-reference-bodies-1";
    private const string StandardInitReferenceBodiesLocalFieldLayoutSchemaIdentity
        = "trrm/1-draft-init-reference-bodies-local-field-layout-1";
    private const string StandardInitReferenceBodiesNestedFieldDestinationsSchemaIdentity
        = "trrm/1-draft-init-reference-bodies-local-field-layout-2";
    private const string StandardInitReferenceBodiesGeneralPredicateGraphsSchemaIdentity
        = "trrm/1-draft-init-reference-bodies-general-predicate-graphs-1";
    private const string StandardInitReferenceBodiesPointerLoadsSchemaIdentity
        = "trrm/1-draft-init-reference-bodies-pointer-loads-1";
    private const string StandardInitReferenceBodiesGeneralPredicateGraphsPointerLoadsSchemaIdentity
        = "trrm/1-draft-init-reference-bodies-general-predicate-graphs-pointer-loads-1";
    private const string StandardInitReferenceBodiesPointerLoadsNativeParameterSchemaIdentity
        = "trrm/1-draft-init-reference-bodies-pointer-loads-native-parameter-contracts-1";
    private const string StandardInitReferenceBodiesGeneralPredicateGraphsPointerLoadsNativeParameterSchemaIdentity
        = "trrm/1-draft-init-reference-bodies-general-predicate-graphs-pointer-loads-native-parameter-contracts-1";
    private const string StandardC1SchemaIdentity
        = "trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1";
    private const string StandardC1M1SchemaIdentity
        = "trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1";
    private const string StandardC1M1ConstructionSchemaIdentity
        = "trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-1";
    private const string StandardC1M1Construction2SchemaIdentity
        = "trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-2";
    private const string StandardC1M1Construction2InitializerCopyEvidenceSchemaIdentity
        = StandardGenericMethodSchema.C1M1Construction2InitializerCopyEvidence;
    private const string StandardG1L1R1SchemaIdentity
        = "trrm/1-draft-generic-method-calls-parameter-labels-1-receiver-access-1";
    private const string StandardC1L1R1SchemaIdentity
        = "trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-parameter-labels-1-receiver-access-1";
    private const string StandardC1M1L1R1SchemaIdentity
        = "trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-parameter-labels-1-receiver-access-1";
    private const string StandardC1M1ConstructionL1R1SchemaIdentity
        = "trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-1-parameter-labels-1-receiver-access-1";
    private const string StandardC1M1Construction2L1R1SchemaIdentity
        = "trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-2-parameter-labels-1-receiver-access-1";
    private const string StandardC1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsSchemaIdentity
        = StandardGenericMethodSchema
            .C1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaults;
    private const string StandardC1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsInitializerCopyEvidenceSchemaIdentity
        = StandardGenericMethodSchema
            .C1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsInitializerCopyEvidence;
    private const string StandardCM1AR1SchemaIdentity
        = StandardGenericMethodSchema
            .C1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsAssociatedRelationConstraints;
    private const string StandardC1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsAssociatedRelationConstraintsInitializerCopyEvidenceSchemaIdentity
        = StandardGenericMethodSchema
            .C1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsAssociatedRelationConstraintsInitializerCopyEvidence;
    private const string StandardC1M1Construction2TypeBindingViewsSchemaIdentity
        = StandardGenericMethodSchema.C1M1Construction2TypeBindingViews;
    private const string StandardC1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsTypeBindingViewsSchemaIdentity
        = StandardGenericMethodSchema
            .C1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsTypeBindingViews;
    private const string StandardC1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsAssociatedRelationConstraintsTypeBindingViewsSchemaIdentity
        = StandardGenericMethodSchema
            .C1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsAssociatedRelationConstraintsTypeBindingViews;
    private const string StandardC1M1Construction2ClassTypeBindingViewsSchemaIdentity
        = StandardGenericMethodSchema.C1M1Construction2ClassTypeBindingViews;
    private const string StandardC1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsClassTypeBindingViewsSchemaIdentity
        = StandardGenericMethodSchema
            .C1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsClassTypeBindingViews;
    private const string StandardC1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsAssociatedRelationConstraintsClassTypeBindingViewsSchemaIdentity
        = StandardGenericMethodSchema
            .C1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsAssociatedRelationConstraintsClassTypeBindingViews;
    private const string StandardC1M1ConstructionL1R1PropertyInitializedStateSchemaIdentity
        = "trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-1-parameter-labels-1-receiver-access-1-property-initialized-state-1";
    private const string StandardC1M1Construction2L1R1PropertyInitializedStateSchemaIdentity
        = "trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-2-parameter-labels-1-receiver-access-1-property-initialized-state-1";
    private const string StandardC1M1ConstructionL1R1EnumClosureSchemaIdentity
        = "trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-1-parameter-labels-1-receiver-access-1-enum-closure-1";
    private const string StandardC1M1Construction2L1R1EnumClosureSchemaIdentity
        = "trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-2-parameter-labels-1-receiver-access-1-enum-closure-1";
    private const string StandardC1M1ConstructionL1R1PropertyInitializedStateEnumClosureSchemaIdentity
        = "trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-1-parameter-labels-1-receiver-access-1-property-initialized-state-1-enum-closure-1";
    private const string StandardC1M1Construction2L1R1PropertyInitializedStateEnumClosureSchemaIdentity
        = "trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-2-parameter-labels-1-receiver-access-1-property-initialized-state-1-enum-closure-1";
    private const string StandardC1M1ConstructionL1R1EnumClosureInitializerCopyEvidenceSchemaIdentity
        = "trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-1-parameter-labels-1-receiver-access-1-enum-closure-1-initializer-copy-evidence-1";
    private const string StandardC1M1Construction2L1R1EnumClosureInitializerCopyEvidenceSchemaIdentity
        = "trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-2-parameter-labels-1-receiver-access-1-enum-closure-1-initializer-copy-evidence-1";
    private const string StandardC1M1ConstructionL1R1ClassInstanceLayoutInitializerCopyEvidenceSchemaIdentity
        = "trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-1-parameter-labels-1-receiver-access-1-enum-closure-1-class-instance-layout-1-initializer-copy-evidence-1";
    private const string StandardC1M1Construction2L1R1ClassInstanceLayoutInitializerCopyEvidenceSchemaIdentity
        = "trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-2-parameter-labels-1-receiver-access-1-enum-closure-1-class-instance-layout-1-initializer-copy-evidence-1";
    private const string StandardC1M1ConstructionL1R1ClassInstanceLayoutSchemaIdentity
        = "trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-1-parameter-labels-1-receiver-access-1-enum-closure-1-class-instance-layout-1";
    private const string StandardC1M1Construction2L1R1ClassInstanceLayoutSchemaIdentity
        = "trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-2-parameter-labels-1-receiver-access-1-enum-closure-1-class-instance-layout-1";
    private const string StandardC1M1ConstructionL1R1PropertyInitializedStateClassInstanceLayoutSchemaIdentity
        = "trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-1-parameter-labels-1-receiver-access-1-property-initialized-state-1-enum-closure-1-class-instance-layout-1";
    private const string StandardC1M1Construction2L1R1PropertyInitializedStateClassInstanceLayoutSchemaIdentity
        = "trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-2-parameter-labels-1-receiver-access-1-property-initialized-state-1-enum-closure-1-class-instance-layout-1";

    private static bool IsSelectedC1SchemaIdentity(string schemaIdentity)
        => StringComparer.Ordinal.Equals(schemaIdentity,
                StandardC1SchemaIdentity)
            || StringComparer.Ordinal.Equals(schemaIdentity,
                StandardC1M1SchemaIdentity)
            || StringComparer.Ordinal.Equals(schemaIdentity,
                StandardC1M1ConstructionSchemaIdentity)
            || StringComparer.Ordinal.Equals(schemaIdentity,
                StandardC1M1Construction2SchemaIdentity)
            || StringComparer.Ordinal.Equals(schemaIdentity,
                StandardC1M1Construction2InitializerCopyEvidenceSchemaIdentity)
            || StringComparer.Ordinal.Equals(schemaIdentity,
                StandardC1M1Construction2TypeBindingViewsSchemaIdentity)
            || StringComparer.Ordinal.Equals(schemaIdentity,
                StandardC1L1R1SchemaIdentity)
            || StringComparer.Ordinal.Equals(schemaIdentity,
                StandardC1M1L1R1SchemaIdentity)
            || StringComparer.Ordinal.Equals(schemaIdentity,
                StandardC1M1ConstructionL1R1SchemaIdentity)
            || StringComparer.Ordinal.Equals(schemaIdentity,
                StandardC1M1Construction2L1R1SchemaIdentity)
            || StringComparer.Ordinal.Equals(schemaIdentity,
                StandardC1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsSchemaIdentity)
            || StringComparer.Ordinal.Equals(schemaIdentity,
                StandardC1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsInitializerCopyEvidenceSchemaIdentity)
            || StringComparer.Ordinal.Equals(schemaIdentity,
                StandardC1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsTypeBindingViewsSchemaIdentity)
            || StringComparer.Ordinal.Equals(schemaIdentity,
                StandardCM1AR1SchemaIdentity)
            || StringComparer.Ordinal.Equals(schemaIdentity,
                StandardC1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsAssociatedRelationConstraintsInitializerCopyEvidenceSchemaIdentity)
            || StandardGenericMethodSchema.IsTypeBindingViews(schemaIdentity)
            || StandardGenericMethodSchema.IsClassTypeBindingViews(
                schemaIdentity)
            || StringComparer.Ordinal.Equals(schemaIdentity,
                StandardC1M1ConstructionL1R1PropertyInitializedStateSchemaIdentity)
            || StringComparer.Ordinal.Equals(schemaIdentity,
                StandardC1M1Construction2L1R1PropertyInitializedStateSchemaIdentity)
            || StringComparer.Ordinal.Equals(schemaIdentity,
                StandardC1M1ConstructionL1R1EnumClosureSchemaIdentity)
            || StringComparer.Ordinal.Equals(schemaIdentity,
                StandardC1M1Construction2L1R1EnumClosureSchemaIdentity)
            || StringComparer.Ordinal.Equals(schemaIdentity,
                StandardC1M1ConstructionL1R1PropertyInitializedStateEnumClosureSchemaIdentity)
            || StringComparer.Ordinal.Equals(schemaIdentity,
                StandardC1M1Construction2L1R1PropertyInitializedStateEnumClosureSchemaIdentity)
            || StringComparer.Ordinal.Equals(schemaIdentity,
                StandardC1M1ConstructionL1R1EnumClosureInitializerCopyEvidenceSchemaIdentity)
            || StringComparer.Ordinal.Equals(schemaIdentity,
                StandardC1M1Construction2L1R1EnumClosureInitializerCopyEvidenceSchemaIdentity)
            || StringComparer.Ordinal.Equals(schemaIdentity,
                StandardC1M1ConstructionL1R1ClassInstanceLayoutSchemaIdentity)
            || StringComparer.Ordinal.Equals(schemaIdentity,
                StandardC1M1Construction2L1R1ClassInstanceLayoutSchemaIdentity)
            || StringComparer.Ordinal.Equals(schemaIdentity,
                StandardC1M1ConstructionL1R1PropertyInitializedStateClassInstanceLayoutSchemaIdentity)
            || StringComparer.Ordinal.Equals(schemaIdentity,
                StandardC1M1Construction2L1R1PropertyInitializedStateClassInstanceLayoutSchemaIdentity)
            || StringComparer.Ordinal.Equals(schemaIdentity,
                StandardC1M1ConstructionL1R1ClassInstanceLayoutInitializerCopyEvidenceSchemaIdentity)
            || StringComparer.Ordinal.Equals(schemaIdentity,
                StandardC1M1Construction2L1R1ClassInstanceLayoutInitializerCopyEvidenceSchemaIdentity);

    internal static bool IsSelectedC1EnumClosureSchemaIdentity(
        string schemaIdentity)
        => schemaIdentity is
            StandardC1M1ConstructionL1R1EnumClosureSchemaIdentity
            or StandardC1M1Construction2L1R1EnumClosureSchemaIdentity
            or StandardC1M1ConstructionL1R1PropertyInitializedStateEnumClosureSchemaIdentity
            or StandardC1M1Construction2L1R1PropertyInitializedStateEnumClosureSchemaIdentity
            or StandardC1M1ConstructionL1R1EnumClosureInitializerCopyEvidenceSchemaIdentity
            or StandardC1M1Construction2L1R1EnumClosureInitializerCopyEvidenceSchemaIdentity
            or StandardC1M1ConstructionL1R1ClassInstanceLayoutSchemaIdentity
            or StandardC1M1Construction2L1R1ClassInstanceLayoutSchemaIdentity
            or StandardC1M1ConstructionL1R1PropertyInitializedStateClassInstanceLayoutSchemaIdentity
            or StandardC1M1Construction2L1R1PropertyInitializedStateClassInstanceLayoutSchemaIdentity
            or StandardC1M1ConstructionL1R1ClassInstanceLayoutInitializerCopyEvidenceSchemaIdentity
            or StandardC1M1Construction2L1R1ClassInstanceLayoutInitializerCopyEvidenceSchemaIdentity;

    internal static bool IsSelectedC1ClassInstanceLayoutSchemaIdentity(
        string schemaIdentity)
        => BoundStandardEnumDeclarationContract
            .IsSelectedClassInstanceLayoutSchemaIdentity(schemaIdentity);

    internal static bool IsSelectedTypeDefBindingEqualitySchemaIdentity(
        string? schemaIdentity)
        => StandardGenericMethodSchema.IsTypeBindingViews(schemaIdentity)
            || StandardGenericMethodSchema.IsClassTypeBindingViews(
                schemaIdentity);

    internal static bool IsSelectedStandardLayoutQueriesSchemaIdentity(
        string? schemaIdentity)
        => StringComparer.Ordinal.Equals(schemaIdentity,
                StandardLayoutQueriesSchemaIdentity)
            || TrRmArtifactSchemaFacts
                .IsSelectedTerrumFunctionPointerSchemaIdentity(
                    schemaIdentity);

    internal static bool AreAllSelectedStandardLayoutQueriesSchemaIdentities(
        IReadOnlyList<string> schemaIdentities)
    {
        if (schemaIdentities.Count == 0
            || !IsSelectedStandardLayoutQueriesSchemaIdentity(
                schemaIdentities[0]))
        {
            return false;
        }

        var selectedIdentity = schemaIdentities[0];
        for (var index = 1; index < schemaIdentities.Count; index++)
        {
            if (!StringComparer.Ordinal.Equals(
                    schemaIdentities[index], selectedIdentity))
            {
                return false;
            }
        }

        return true;
    }

    internal static bool IsSelectedClassTypeDefBindingEqualitySchemaIdentity(
        string? schemaIdentity)
        => StandardGenericMethodSchema.IsClassTypeBindingViews(
            schemaIdentity);

    private static bool IsSelectedGeneralPredicateGraphsSchemaIdentity(
        string schemaIdentity)
        => StringComparer.Ordinal.Equals(schemaIdentity,
                StandardInitReferenceBodiesGeneralPredicateGraphsSchemaIdentity)
            || StringComparer.Ordinal.Equals(schemaIdentity,
                StandardInitReferenceBodiesGeneralPredicateGraphsPointerLoadsSchemaIdentity)
            || StringComparer.Ordinal.Equals(schemaIdentity,
                StandardInitReferenceBodiesGeneralPredicateGraphsPointerLoadsNativeParameterSchemaIdentity)
            || IsSelectedC1SchemaIdentity(schemaIdentity);

    private static bool IsSelectedNativePointerLoadsSchemaIdentity(
        string schemaIdentity)
        => StringComparer.Ordinal.Equals(schemaIdentity,
                StandardInitReferenceBodiesPointerLoadsSchemaIdentity)
            || StringComparer.Ordinal.Equals(schemaIdentity,
                StandardInitReferenceBodiesGeneralPredicateGraphsPointerLoadsSchemaIdentity)
            || StringComparer.Ordinal.Equals(schemaIdentity,
                StandardInitReferenceBodiesPointerLoadsNativeParameterSchemaIdentity)
            || StringComparer.Ordinal.Equals(schemaIdentity,
                StandardInitReferenceBodiesGeneralPredicateGraphsPointerLoadsNativeParameterSchemaIdentity)
            || StringComparer.Ordinal.Equals(schemaIdentity,
                StandardLocalAggregatePointerLoadsSchemaIdentity);

    internal static bool IsSelectedLocalAggregatePointerLoadsSchemaIdentity(
        string? schemaIdentity)
        => StringComparer.Ordinal.Equals(schemaIdentity,
            StandardLocalAggregatePointerLoadsSchemaIdentity);

    internal static bool IsSelectedLocalAggregateLayoutSchemaIdentity(
        string? schemaIdentity)
        => StringComparer.Ordinal.Equals(schemaIdentity,
                StandardLocalAggregateLayoutSchemaIdentity)
            || IsSelectedLocalAggregatePointerLoadsSchemaIdentity(
                schemaIdentity);

    private static bool
        IsSelectedInitializationReferenceGeneralPredicateGraphsSchemaIdentity(
            string schemaIdentity)
        => StringComparer.Ordinal.Equals(schemaIdentity,
                StandardInitReferenceBodiesGeneralPredicateGraphsSchemaIdentity)
            || StringComparer.Ordinal.Equals(schemaIdentity,
                StandardInitReferenceBodiesGeneralPredicateGraphsPointerLoadsSchemaIdentity)
            || StringComparer.Ordinal.Equals(schemaIdentity,
                StandardInitReferenceBodiesGeneralPredicateGraphsPointerLoadsNativeParameterSchemaIdentity);

    private static bool IsSelectedInitializationReferenceSchemaIdentity(
        string schemaIdentity)
        => StringComparer.Ordinal.Equals(schemaIdentity,
                StandardInitReferenceBodiesSchemaIdentity)
            || StringComparer.Ordinal.Equals(schemaIdentity,
                StandardForeignNativeInterfaceSchemaIdentity)
            || StringComparer.Ordinal.Equals(schemaIdentity,
                StandardForeignNativeInterfaceParameterSurfaceSchemaIdentity)
            || StringComparer.Ordinal.Equals(schemaIdentity,
                StandardForeignNativeInterfaceAggregateOutputConsumptionSchemaIdentity)
            || IsSelectedInitializationReferenceLocalFieldLayoutSchemaIdentity(
                schemaIdentity)
            || StringComparer.Ordinal.Equals(schemaIdentity,
                StandardInitReferenceBodiesPointerLoadsSchemaIdentity)
            || StringComparer.Ordinal.Equals(schemaIdentity,
                StandardInitReferenceBodiesPointerLoadsNativeParameterSchemaIdentity)
            || IsSelectedGeneralPredicateGraphsSchemaIdentity(schemaIdentity);

    private static bool IsSelectedForeignNativeInterfaceSchemaIdentity(
        string? schemaIdentity)
        => StringComparer.Ordinal.Equals(schemaIdentity,
                StandardForeignNativeInterfaceSchemaIdentity)
            || StringComparer.Ordinal.Equals(schemaIdentity,
                StandardForeignNativeInterfaceParameterSurfaceSchemaIdentity)
            || StringComparer.Ordinal.Equals(schemaIdentity,
                StandardForeignNativeInterfaceAggregateOutputConsumptionSchemaIdentity);

    internal static bool IsSelectedFniAggregateProviderSchemaIdentity(
        string schemaIdentity)
    {
        if (IsSelectedForeignNativeInterfaceSchemaIdentity(schemaIdentity)
            || IsSelectedStandardLayoutQueriesSchemaIdentity(
                schemaIdentity)
            || IsSelectedInitializationReferenceLocalFieldLayoutSchemaIdentity(
                schemaIdentity))
        {
            return true;
        }

        var schema = GetForeignInvocationSchema(schemaIdentity);
        return schema.UsesForeignInvocationStorageFixedArrays()
            || schema.UsesForeignInvocationStorageAddresses()
            || schema.UsesLocalAggregateLayout();
    }

    internal static bool IsSelectedFniAggregateOriginSchemaIdentity(
        string outputSchemaIdentity, string originSchemaIdentity)
    {
        if (StringComparer.Ordinal.Equals(outputSchemaIdentity,
                StandardForeignNativeInterfaceAggregateOutputConsumptionSchemaIdentity))
        {
            return originSchemaIdentity is
                StandardForeignNativeInterfaceAggregateOutputConsumptionSchemaIdentity
                or StandardForeignNativeInterfaceParameterSurfaceSchemaIdentity
                or StandardForeignInvocationStorageFixedArraySchemaIdentity;
        }

        if (StringComparer.Ordinal.Equals(outputSchemaIdentity,
                StandardForeignNativeInterfaceParameterSurfaceSchemaIdentity))
        {
            return originSchemaIdentity is
                StandardForeignNativeInterfaceParameterSurfaceSchemaIdentity
                or StandardForeignInvocationStorageFixedArraySchemaIdentity;
        }

        return StringComparer.Ordinal.Equals(outputSchemaIdentity,
            originSchemaIdentity);
    }

    internal static string GetSelectedFniAggregateOriginExportKey(
        BoundStandardTypeOrigin origin)
    {
        var selectedExportKeys = origin.SelectedExportKeys;
        if (selectedExportKeys.IsDefaultOrEmpty)
        {
            if (origin.ExportKeys.Length != 1)
            {
                throw Unsupported(
                    "An FNI aggregate TypeDef has no unique selected provider ExportKey.");
            }

            return origin.ExportKeys[0];
        }

        if (selectedExportKeys.Length != 1
            || !origin.ExportKeys.Contains(selectedExportKeys[0],
                StringComparer.Ordinal))
        {
            throw Unsupported(
                "An FNI aggregate TypeDef does not retain one exact provider ExportKey.");
        }

        return selectedExportKeys[0];
    }

    private static bool
        IsSelectedInitializationReferenceLocalFieldLayoutSchemaIdentity(
            string schemaIdentity)
        => StringComparer.Ordinal.Equals(schemaIdentity,
                StandardInitReferenceBodiesLocalFieldLayoutSchemaIdentity)
            || StringComparer.Ordinal.Equals(schemaIdentity,
                StandardInitReferenceBodiesNestedFieldDestinationsSchemaIdentity);

    private static readonly PrimitiveType[] s_types =
    [
        PrimitiveType.Void, PrimitiveType.Bool, PrimitiveType.Char8, PrimitiveType.Char16,
        PrimitiveType.Char32, PrimitiveType.Rune, PrimitiveType.Int8, PrimitiveType.Int16,
        PrimitiveType.Int32, PrimitiveType.Int64, PrimitiveType.UInt8, PrimitiveType.UInt16,
        PrimitiveType.UInt32, PrimitiveType.UInt64, PrimitiveType.NativeInt,
        PrimitiveType.NativeUInt,
        PrimitiveType.Float32, PrimitiveType.Float64,
    ];
    private static readonly byte[] s_codes =
        [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 0x0B, 0x0C, 0x0D, 0x0E,
            0x10, 0x11, 0x14, 0x15];
    private static readonly ReadOnlyMemory<byte>[] s_typeRows = CreateTypeRows(s_codes);
    private static readonly ReadOnlyMemory<byte>[] s_nonFloatTypeRows = CreateTypeRows(s_codes[..^2]);
    private static readonly ReadOnlyMemory<byte>[] s_effectPayloads =
    [
        ReadOnlyMemory<byte>.Empty,
        new byte[] { 1, 0, 0, 0 },
        new byte[] { 2, 0, 0, 0 },
        new byte[] { 3, 0, 0, 0 },
        new byte[] { 4, 0, 0, 0 },
        new byte[] { 5, 0, 0, 0 },
        new byte[] { 6, 0, 0, 0 },
        new byte[] { 7, 0, 0, 0 },
    ];

    /// <summary>Emits a checked program as a Development-profile TRRM artifact.</summary>
    /// <param name="program">The checked program to serialize.</param>
    /// <param name="moduleIdentity">The output Module identity.</param>
    /// <returns>The complete validated TRRM artifact.</returns>
    public static byte[] Emit(BoundProgram program, string moduleIdentity)
        => EmitDevelopment(program, moduleIdentity, TrRmArtifactSchema.Development);

    /// <summary>Emits an executable TRRM artifact under one exact Standard schema.</summary>
    /// <param name="program">The checked program to serialize.</param>
    /// <param name="moduleIdentity">The output Module identity.</param>
    /// <param name="schemaIdentity">The exact out-of-band Standard schema identity.</param>
    /// <param name="moduleTargetRequirement">The selected Module target requirement.</param>
    /// <returns>The complete validated TRRM artifact.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="ArgumentException">An identity is empty or not selected by this emitter.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The target requirement is unassigned.</exception>
    /// <exception cref="NotSupportedException">The selected schema cannot represent the program.</exception>
    public static byte[] EmitStandard(BoundProgram program, string moduleIdentity,
        string schemaIdentity, uint moduleTargetRequirement)
        => EmitStandardCore(program, moduleIdentity, schemaIdentity,
            moduleTargetRequirement, null);

    /// <summary>Emits an exact supported contract-predicate profile and returns its Core contract-failure selections.</summary>
    /// <param name="program">The checked program to serialize.</param>
    /// <param name="moduleIdentity">The output Module identity.</param>
    /// <param name="schemaIdentity">The exact B+I+G or no-label Construction-2 schema identity.</param>
    /// <param name="moduleTargetRequirement">The selected Module target requirement.</param>
    /// <param name="coreContractFailureIdentitySelections">The nonserialized Core failure selections retained by the program.</param>
    /// <returns>The complete validated TRRM artifact.</returns>
    /// <remarks>This returns Core role selections only; phase-specific failure-construction maps remain separate and are not produced here.</remarks>
    /// <exception cref="ArgumentNullException">The program is null.</exception>
    /// <exception cref="ArgumentException">The selection table is invalid.</exception>
    /// <exception cref="NotSupportedException">The selected schema or retained contract context is unsupported.</exception>
    public static byte[] EmitStandard(BoundProgram program,
        string moduleIdentity, string schemaIdentity,
        uint moduleTargetRequirement,
        out ImmutableArray<CoreContractFailureIdentitySelection>
            coreContractFailureIdentitySelections)
    {
        ArgumentNullException.ThrowIfNull(program);
        ArgumentException.ThrowIfNullOrEmpty(moduleIdentity);
        ArgumentException.ThrowIfNullOrEmpty(schemaIdentity);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(
            moduleTargetRequirement,
            TrRmModuleTableReader.WindowsX64NativeTargetRequirement);
        var isInitializationReferenceGeneralPredicateGraphs
            = IsSelectedInitializationReferenceGeneralPredicateGraphsSchemaIdentity(
                schemaIdentity);
        var isNoLabelConstruction2 = StringComparer.Ordinal.Equals(
            schemaIdentity, StandardC1M1Construction2SchemaIdentity);
        if (!isInitializationReferenceGeneralPredicateGraphs
            && !isNoLabelConstruction2)
        {
            throw Unsupported(
                "Core contract-failure selections require the exact B+I+G or no-label Construction-2 profile.");
        }
        if (!CoreContractFailureIdentitySelectionContract.IsValidTable(
                program.CoreContractFailureIdentitySelections))
        {
            throw new ArgumentException(
                "The Core contract-failure identity selections are invalid.",
                nameof(program));
        }

        byte[] artifact;
        if (isNoLabelConstruction2)
        {
            ValidateConstruction2DeclarationContractFailureContexts(
                program, moduleIdentity);
            artifact = EmitC1M1Construction2(program, moduleIdentity,
                moduleTargetRequirement, null, null,
                hasCoreContractFailureIdentityTransport: true);
        }
        else
        {
            artifact = EmitStandardCore(program, moduleIdentity,
                schemaIdentity, moduleTargetRequirement, null);
        }
        coreContractFailureIdentitySelections
            = program.CoreContractFailureIdentitySelections.IsDefault
                ? [] : program.CoreContractFailureIdentitySelections;
        return artifact;
    }

    /// <summary>Emits an exact selected Standard schema with explicit source declaration exports.</summary>
    /// <param name="program">The checked source-bound program to serialize.</param>
    /// <param name="moduleIdentity">The output Module identity.</param>
    /// <param name="schemaIdentity">The exact out-of-band Standard schema identity.</param>
    /// <param name="moduleTargetRequirement">The selected Module target requirement.</param>
    /// <param name="typeExports">The explicit source TypeDef and FieldDef ExportKey mappings, including complete enum member keys when selecting NominalEnumsDraft.</param>
    /// <returns>The complete validated TRRM artifact.</returns>
    /// <exception cref="ArgumentNullException">An argument or mapping is null.</exception>
    /// <exception cref="ArgumentException">An identity or mapping is invalid or duplicated.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The target requirement is unassigned.</exception>
    /// <exception cref="NotSupportedException">The selected operation or schema cannot represent the program.</exception>
    public static byte[] EmitStandard(BoundProgram program, string moduleIdentity,
        string schemaIdentity, uint moduleTargetRequirement,
        IReadOnlyList<TrRmSourceTypeExport> typeExports)
    {
        ArgumentNullException.ThrowIfNull(program);
        ArgumentNullException.ThrowIfNull(typeExports);
        ArgumentException.ThrowIfNullOrEmpty(moduleIdentity);
        ArgumentException.ThrowIfNullOrEmpty(schemaIdentity);
        var typeExportSnapshot = new TrRmSourceTypeExport[typeExports.Count];
        for (var index = 0; index < typeExportSnapshot.Length; index++)
        {
            var export = typeExports[index]
                ?? throw new ArgumentException(
                    "Source TypeDef export mappings cannot contain null entries.",
                    nameof(typeExports));
            typeExportSnapshot[index] = export with {
                EnumMemberDeclarationKeys = export
                    .EnumMemberDeclarationKeys.IsDefault
                    ? default : [.. export.EnumMemberDeclarationKeys],
                FieldExports = export.FieldExports.IsDefault
                    ? default : [.. export.FieldExports],
                InterfaceMethodExports = export.InterfaceMethodExports.IsDefault
                    ? default : [.. export.InterfaceMethodExports],
                InterfaceAssociatedTypeExports
                    = export.InterfaceAssociatedTypeExports.IsDefault
                        ? default : [.. export.InterfaceAssociatedTypeExports],
            };
        }

        if (StringComparer.Ordinal.Equals(schemaIdentity,
                StandardForeignNativeInterfaceSchemaIdentity)
            || StringComparer.Ordinal.Equals(schemaIdentity,
                StandardForeignNativeInterfaceParameterSurfaceSchemaIdentity)
            || StringComparer.Ordinal.Equals(schemaIdentity,
                StandardForeignNativeInterfaceAggregateOutputConsumptionSchemaIdentity))
        {
            return EmitStandardCore(program, moduleIdentity, schemaIdentity,
                moduleTargetRequirement, typeExportSnapshot);
        }

        if (typeExportSnapshot.Length == 0)
        {
            return EmitStandard(program, moduleIdentity, schemaIdentity,
                moduleTargetRequirement);
        }
        if (!IsSelectedStandardLayoutQueriesSchemaIdentity(schemaIdentity)
            && !StringComparer.Ordinal.Equals(schemaIdentity,
                StandardInitReferenceBodiesNestedFieldDestinationsSchemaIdentity)
            && !IsC1M1MethodTypeSchemaIdentity(schemaIdentity)
            && typeExportSnapshot.Any(static export =>
                !export.FieldExports.IsDefaultOrEmpty))
        {
            throw Unsupported(
                "FieldDef exports require the exact LayoutQueries or nested FieldPath schema.");
        }
        if (StringComparer.Ordinal.Equals(schemaIdentity,
                StandardNominalEnumsSchemaIdentity))
        {
            if (typeExportSnapshot.Any(static export =>
                    export.EnumMemberDeclarationKeys.IsDefault))
            {
                throw new ArgumentException(
                    "NominalEnumsDraft source enum exports require complete member-key arrays.",
                    nameof(typeExports));
            }

            return EmitStandardCore(program, moduleIdentity, schemaIdentity,
                moduleTargetRequirement, typeExportSnapshot);
        }
        if (typeExportSnapshot.Any(static export =>
                !export.EnumMemberDeclarationKeys.IsDefault))
        {
            throw Unsupported(
                "Enum member-key assignments require the exact NominalEnumsDraft schema.");
        }
        if (IsSelectedStandardLayoutQueriesSchemaIdentity(schemaIdentity))
        {
            if (program.EntryPoint != -1)
            {
                throw Unsupported(
                    "Standard LayoutQueries type exports require a library.");
            }

            return EmitStandardCore(program, moduleIdentity, schemaIdentity,
                moduleTargetRequirement, typeExportSnapshot);
        }

        if (IsC1M1MethodTypeSchemaIdentity(schemaIdentity))
        {
            if (program.EntryPoint != -1
                || !program.Classes.IsEmpty
                || !program.StandardClassDeclarations.IsEmpty
                || !program.Interfaces.IsEmpty
                || !program.GenericNominalTypeDefinitions.IsEmpty
                || !program.GenericNominalTypeInstances.IsEmpty
                || !program.GenericMethodDefinitions.IsEmpty
                || !program.GenericMethodSignatures.IsEmpty
                || program.Structures.IsEmpty)
            {
                throw Unsupported(
                    "Explicit Construction-2 TypeDef exports require a classless nongeneric source module with struct declarations.");
            }

            return EmitStandardCore(program, moduleIdentity,
                schemaIdentity, moduleTargetRequirement,
                typeExportSnapshot, sourceClassExports:
                    new SourceClassExports(program, []));
        }

        var initializationReferenceLocalFieldLayout
            = StringComparer.Ordinal.Equals(schemaIdentity,
                StandardInitReferenceBodiesLocalFieldLayoutSchemaIdentity)
                || StringComparer.Ordinal.Equals(schemaIdentity,
                    StandardInitReferenceBodiesNestedFieldDestinationsSchemaIdentity);
        if (!initializationReferenceLocalFieldLayout
            && !StringComparer.Ordinal.Equals(schemaIdentity,
                StandardForeignInvocationStorageFixedArraySchemaIdentity)
            && !StringComparer.Ordinal.Equals(schemaIdentity,
                StandardForeignInvocationStorageFixedArrayEmptyInitializationSchemaIdentity)
            && !StringComparer.Ordinal.Equals(schemaIdentity,
                StandardForeignInvocationStorageFixedArrayEmptyUnitInitializationSchemaIdentity)
            && !StringComparer.Ordinal.Equals(schemaIdentity,
                StandardForeignInvocationStorageFixedArrayEmptyUnitScalarFrozenValuesSchemaIdentity)
            && !StringComparer.Ordinal.Equals(schemaIdentity,
                StandardForeignInvocationStorageFixedArrayCopyReturnCoverageSchemaIdentity)
            && !StringComparer.Ordinal.Equals(schemaIdentity,
                StandardForeignInvocationStorageFixedArrayCopyReturnCoverageRepeatInitializationSchemaIdentity)
            && !StringComparer.Ordinal.Equals(schemaIdentity,
                StandardLocalAggregateLayoutSchemaIdentity)
            && !StringComparer.Ordinal.Equals(schemaIdentity,
                StandardLocalAggregatePointerLoadsSchemaIdentity))
        {
            throw Unsupported(
                "Explicit source TypeDef exports require an exact selected fixed-array schema.");
        }
        if (program.EntryPoint != -1
            || (!initializationReferenceLocalFieldLayout
                && !program.Functions.IsEmpty)
            || program.Structures.IsEmpty)
        {
            throw Unsupported(
                "Explicit source TypeDef exports require a source library with nominal declarations.");
        }

        return EmitStandardCore(program, moduleIdentity, schemaIdentity,
            moduleTargetRequirement, typeExportSnapshot);
    }

    /// <summary>Emits an exact G1L1 or supported C1-family L1 schema with explicit source generic-method and interface exports.</summary>
    /// <param name="program">The checked source-bound program to serialize.</param>
    /// <param name="moduleIdentity">The output Module identity.</param>
    /// <param name="schemaIdentity">The exact out-of-band schema identity.</param>
    /// <param name="moduleTargetRequirement">The selected Module target requirement.</param>
    /// <param name="typeExports">The explicit interface TypeDef exports.</param>
    /// <param name="genericMethodExports">The explicit source generic MethodDef ExportKey mappings.</param>
    /// <returns>The complete validated TRRM artifact.</returns>
    /// <exception cref="ArgumentNullException">An argument or mapping is null.</exception>
    /// <exception cref="ArgumentException">An identity or mapping is invalid.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The target requirement is unassigned.</exception>
    /// <exception cref="NotSupportedException">The selected carrier cannot represent the program.</exception>
    public static byte[] EmitStandard(BoundProgram program, string moduleIdentity,
        string schemaIdentity, uint moduleTargetRequirement,
        IReadOnlyList<TrRmSourceTypeExport> typeExports,
        IReadOnlyList<TrRmSourceGenericMethodExport> genericMethodExports)
    {
        ArgumentNullException.ThrowIfNull(program);
        ArgumentNullException.ThrowIfNull(typeExports);
        ArgumentNullException.ThrowIfNull(genericMethodExports);
        ArgumentException.ThrowIfNullOrEmpty(moduleIdentity);
        ArgumentException.ThrowIfNullOrEmpty(schemaIdentity);
        if (BoundEnumTypes.HasStandardDeclarations(
                program.EnumDefinitions)
            && !IsSelectedC1EnumClosureSchemaIdentity(schemaIdentity))
        {
            throw Unsupported(
                "Standard enum origins require one of the exact EN1 schema identities.");
        }

        ArgumentOutOfRangeException.ThrowIfGreaterThan(
            moduleTargetRequirement,
            TrRmModuleTableReader.WindowsX64NativeTargetRequirement);
        if (!StringComparer.Ordinal.Equals(schemaIdentity,
                StandardGenericMethodCallsParameterLabelsSchemaIdentity)
            && !StringComparer.Ordinal.Equals(schemaIdentity,
                StandardC1L1R1SchemaIdentity)
            && !StringComparer.Ordinal.Equals(schemaIdentity,
                StandardC1M1L1R1SchemaIdentity)
            && !StringComparer.Ordinal.Equals(schemaIdentity,
                StandardC1M1ConstructionL1R1SchemaIdentity)
            && !StringComparer.Ordinal.Equals(schemaIdentity,
                StandardC1M1Construction2L1R1SchemaIdentity))
        {
            throw new ArgumentException(
                "The six-argument source declaration route requires an exact G1L1 or supported C1-family L1 schema identity.",
                nameof(schemaIdentity));
        }
        var methodExportSnapshot = new TrRmSourceGenericMethodExport[
            genericMethodExports.Count];
        for (var index = 0; index < methodExportSnapshot.Length; index++)
        {
            methodExportSnapshot[index] = genericMethodExports[index]
                ?? throw new ArgumentException(
                    "Source generic method mappings cannot contain null entries.",
                    nameof(genericMethodExports));
        }

        var typeExportSnapshot = new TrRmSourceTypeExport[
            typeExports.Count];
        for (var index = 0; index < typeExportSnapshot.Length; index++)
        {
            var export = typeExports[index]
                ?? throw new ArgumentException(
                    "Source TypeDef mappings cannot contain null entries.",
                    nameof(typeExports));
            typeExportSnapshot[index] = export with {
                EnumMemberDeclarationKeys = export
                    .EnumMemberDeclarationKeys.IsDefault
                    ? default : [.. export.EnumMemberDeclarationKeys],
                FieldExports = export.FieldExports.IsDefault
                    ? default : [.. export.FieldExports],
                InterfaceMethodExports = export
                    .InterfaceMethodExports.IsDefault
                    ? default : [.. export.InterfaceMethodExports],
                InterfaceAssociatedTypeExports = export
                    .InterfaceAssociatedTypeExports.IsDefault
                    ? default : [.. export.InterfaceAssociatedTypeExports],
            };
        }

        var sourceMethodExports = SourceGenericMethodExports.Capture(program,
            moduleIdentity, methodExportSnapshot);
        return EmitStandardCore(program, moduleIdentity, schemaIdentity,
            moduleTargetRequirement, typeExportSnapshot,
            sourceMethodExports);
    }

    /// <summary>Emits C1M1 class declarations or construction-successor class/member exports from checked source mappings.</summary>
    /// <remarks>The C1M1 route emits TypeDef, FieldDef, Layout and class/field Export rows only. Construction successors use their exact member contracts; the bounded Construction-2 route emits real initializer/projection MethodDefs and Bodies for supported direct scalar field initialization, zero-parameter static scalar methods, and empty unit-returning instance methods. Broader method Bodies and properties remain unsupported.</remarks>
    public static byte[] EmitStandard(BoundProgram program, string moduleIdentity,
        string schemaIdentity, uint moduleTargetRequirement,
        IReadOnlyList<TrRmSourceTypeExport> typeExports,
        IReadOnlyList<TrRmSourceGenericMethodExport> genericMethodExports,
        IReadOnlyList<TrRmSourceClassExport> classExports)
    {
        ArgumentNullException.ThrowIfNull(program);
        ArgumentNullException.ThrowIfNull(typeExports);
        ArgumentNullException.ThrowIfNull(genericMethodExports);
        ArgumentNullException.ThrowIfNull(classExports);
        ArgumentException.ThrowIfNullOrEmpty(moduleIdentity);
        ArgumentException.ThrowIfNullOrEmpty(schemaIdentity);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(
            moduleTargetRequirement,
            TrRmModuleTableReader.WindowsX64NativeTargetRequirement);
        var emitsClassDeclarations = StringComparer.Ordinal.Equals(
            schemaIdentity, StandardC1M1SchemaIdentity);
        if (!emitsClassDeclarations
            && !StringComparer.Ordinal.Equals(schemaIdentity,
                StandardC1M1ConstructionSchemaIdentity)
            && !StringComparer.Ordinal.Equals(schemaIdentity,
                StandardC1M1Construction2SchemaIdentity)
            && !StringComparer.Ordinal.Equals(schemaIdentity,
                StandardC1M1Construction2InitializerCopyEvidenceSchemaIdentity)
            && !StringComparer.Ordinal.Equals(schemaIdentity,
                StandardC1M1Construction2TypeBindingViewsSchemaIdentity)
            && !StringComparer.Ordinal.Equals(schemaIdentity,
                StandardC1M1Construction2ClassTypeBindingViewsSchemaIdentity)
            && !StringComparer.Ordinal.Equals(schemaIdentity,
                StandardC1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsSchemaIdentity)
            && !StringComparer.Ordinal.Equals(schemaIdentity,
                StandardC1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsInitializerCopyEvidenceSchemaIdentity)
            && !StringComparer.Ordinal.Equals(schemaIdentity,
                StandardC1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsTypeBindingViewsSchemaIdentity)
            && !StringComparer.Ordinal.Equals(schemaIdentity,
                StandardC1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsClassTypeBindingViewsSchemaIdentity)
            && !StringComparer.Ordinal.Equals(schemaIdentity,
                StandardCM1AR1SchemaIdentity)
            && !StringComparer.Ordinal.Equals(schemaIdentity,
                StandardC1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsAssociatedRelationConstraintsInitializerCopyEvidenceSchemaIdentity)
            && !StringComparer.Ordinal.Equals(schemaIdentity,
                StandardC1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsAssociatedRelationConstraintsTypeBindingViewsSchemaIdentity))
            && !StringComparer.Ordinal.Equals(schemaIdentity,
                StandardC1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsAssociatedRelationConstraintsClassTypeBindingViewsSchemaIdentity))
        {
            throw new ArgumentException(
                "The seven-argument source class route requires an exact C1M1, Construction-1, Construction-2 or CM1 identity.",
                nameof(schemaIdentity));
        }

        var methodExportSnapshot = new TrRmSourceGenericMethodExport[
            genericMethodExports.Count];
        for (var index = 0; index < methodExportSnapshot.Length; index++)
        {
            methodExportSnapshot[index] = genericMethodExports[index]
                ?? throw new ArgumentException(
                    "Source generic method mappings cannot contain null entries.",
                    nameof(genericMethodExports));
        }

        var typeExportSnapshot = new TrRmSourceTypeExport[typeExports.Count];
        for (var index = 0; index < typeExportSnapshot.Length; index++)
        {
            var export = typeExports[index]
                ?? throw new ArgumentException(
                    "Source TypeDef mappings cannot contain null entries.",
                    nameof(typeExports));
            typeExportSnapshot[index] = export with {
                EnumMemberDeclarationKeys = export
                    .EnumMemberDeclarationKeys.IsDefault
                    ? default : [.. export.EnumMemberDeclarationKeys],
                FieldExports = export.FieldExports.IsDefault
                    ? default : [.. export.FieldExports],
                InterfaceMethodExports = export.InterfaceMethodExports.IsDefault
                    ? default : [.. export.InterfaceMethodExports],
                InterfaceAssociatedTypeExports
                    = export.InterfaceAssociatedTypeExports.IsDefault
                        ? default : [.. export.InterfaceAssociatedTypeExports],
            };
        }

        if (emitsClassDeclarations
            && (typeExportSnapshot.Length != 0
                || methodExportSnapshot.Length != 0))
        {
            throw new ArgumentException(
                "C1M1 source class declaration emission does not accept source TypeDef or generic MethodDef mappings.",
                nameof(typeExports));
        }

        var classExportSnapshot = new TrRmSourceClassExport[classExports.Count];
        for (var index = 0; index < classExportSnapshot.Length; index++)
        {
            var export = classExports[index]
                ?? throw new ArgumentException(
                    "Source class mappings cannot contain null entries.",
                    nameof(classExports));
            classExportSnapshot[index] = export with {
                MemberExports = export.MemberExports.IsDefault
                    ? default : [.. export.MemberExports],
            };
        }

        var sourceMethodExports = SourceGenericMethodExports.Capture(program,
            moduleIdentity, methodExportSnapshot);
        var sourceClassExports = SourceClassExports.Capture(program,
            moduleIdentity, typeExportSnapshot, methodExportSnapshot,
            classExportSnapshot,
            requirePublicCallableKeys: !emitsClassDeclarations);
        return EmitStandardCore(program, moduleIdentity, schemaIdentity,
            moduleTargetRequirement, typeExportSnapshot,
            sourceMethodExports, sourceClassExports);
    }

    /// <summary>Emits an exact EN1 schema from joint source type, method, class and enum mappings.</summary>
    /// <param name="program">The exact checked source-bound program.</param>
    /// <param name="moduleIdentity">The output Module identity.</param>
    /// <param name="schemaIdentity">One exact EN1 schema identity.</param>
    /// <param name="moduleTargetRequirement">The selected Module target requirement.</param>
    /// <param name="typeExports">Explicit struct/interface mappings; enum keys remain unset.</param>
    /// <param name="genericMethodExports">Explicit source generic MethodDef mappings.</param>
    /// <param name="classExports">Explicit source class and member mappings.</param>
    /// <param name="enumExports">Explicit source enum aliases and complete member keys.</param>
    /// <returns>The complete validated EN1 artifact.</returns>
    public static byte[] EmitStandard(BoundProgram program, string moduleIdentity,
        string schemaIdentity, uint moduleTargetRequirement,
        IReadOnlyList<TrRmSourceTypeExport> typeExports,
        IReadOnlyList<TrRmSourceGenericMethodExport> genericMethodExports,
        IReadOnlyList<TrRmSourceClassExport> classExports,
        IReadOnlyList<TrRmSourceEnumDeclarationExport> enumExports)
    {
        ArgumentNullException.ThrowIfNull(program);
        ArgumentNullException.ThrowIfNull(typeExports);
        ArgumentNullException.ThrowIfNull(genericMethodExports);
        ArgumentNullException.ThrowIfNull(classExports);
        ArgumentNullException.ThrowIfNull(enumExports);
        ArgumentException.ThrowIfNullOrEmpty(moduleIdentity);
        ArgumentException.ThrowIfNullOrEmpty(schemaIdentity);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(
            moduleTargetRequirement,
            TrRmModuleTableReader.WindowsX64NativeTargetRequirement);
        if (!IsSelectedC1EnumClosureSchemaIdentity(schemaIdentity))
        {
            throw new ArgumentException(
                "The eight-argument source enum route requires one exact EN1 schema identity.",
                nameof(schemaIdentity));
        }

        var typeExportSnapshot
            = new TrRmSourceTypeExport[typeExports.Count];
        for (var index = 0; index < typeExportSnapshot.Length; index++)
        {
            var export = typeExports[index]
                ?? throw new ArgumentException(
                    "Source TypeDef mappings cannot contain null entries.",
                    nameof(typeExports));
            if (!export.EnumMemberDeclarationKeys.IsDefault)
            {
                throw new ArgumentException(
                    "EN1 enum member keys must use the source-anchored enum mapping list.",
                    nameof(typeExports));
            }

            typeExportSnapshot[index] = export with {
                FieldExports = export.FieldExports.IsDefault
                    ? default : [.. export.FieldExports],
                InterfaceMethodExports =
                    export.InterfaceMethodExports.IsDefault
                        ? default : [.. export.InterfaceMethodExports],
                InterfaceAssociatedTypeExports =
                    export.InterfaceAssociatedTypeExports.IsDefault
                        ? default : [.. export.InterfaceAssociatedTypeExports],
            };
        }

        var genericMethodExportSnapshot
            = new TrRmSourceGenericMethodExport[
                genericMethodExports.Count];
        for (var index = 0;
            index < genericMethodExportSnapshot.Length; index++)
        {
            genericMethodExportSnapshot[index]
                = genericMethodExports[index]
                    ?? throw new ArgumentException(
                        "Source generic MethodDef mappings cannot contain null entries.",
                        nameof(genericMethodExports));
        }

        var classExportSnapshot
            = new TrRmSourceClassExport[classExports.Count];
        for (var index = 0; index < classExportSnapshot.Length; index++)
        {
            var export = classExports[index]
                ?? throw new ArgumentException(
                    "Source class mappings cannot contain null entries.",
                    nameof(classExports));
            classExportSnapshot[index] = export with {
                MemberExports = export.MemberExports.IsDefault
                    ? default : [.. export.MemberExports],
            };
        }

        var enumExportSnapshot
            = new TrRmSourceEnumDeclarationExport[enumExports.Count];
        for (var index = 0; index < enumExportSnapshot.Length; index++)
        {
            var export = enumExports[index]
                ?? throw new ArgumentException(
                    "Source enum mappings cannot contain null entries.",
                    nameof(enumExports));
            enumExportSnapshot[index] = export with {
                ExportKeys = export.ExportKeys.IsDefault
                    ? default : [.. export.ExportKeys],
                MemberDeclarationKeys =
                    export.MemberDeclarationKeys.IsDefault
                        ? default : [.. export.MemberDeclarationKeys],
            };
        }

        var sourceMethodExports = SourceGenericMethodExports.Capture(program,
            moduleIdentity, genericMethodExportSnapshot);
        var sourceClassExports = SourceClassExports.Capture(program,
            moduleIdentity, typeExportSnapshot,
            genericMethodExportSnapshot, classExportSnapshot,
            enumExportSnapshot, requirePublicCallableKeys: true);
        var sourceEnumExports = C1EnumClosureSourceEnumExports.Capture(
            program, enumExportSnapshot);
        var schema = GetForeignInvocationSchema(schemaIdentity);
        return EmitC1EnumClosureDeclarationLibrary(program, moduleIdentity,
            schemaIdentity, moduleTargetRequirement, schema, typeExportSnapshot,
            sourceMethodExports, sourceClassExports, sourceEnumExports);
    }

    private static byte[] EmitStandardCore(BoundProgram program,
        string moduleIdentity, string schemaIdentity,
        uint moduleTargetRequirement,
        IReadOnlyList<TrRmSourceTypeExport>? sourceTypeExports,
        SourceGenericMethodExports? sourceGenericMethodExports = null,
        SourceClassExports? sourceClassExports = null,
        IReadOnlyList<BoundGenericMethodDefinition>?
            retainedGenericMethodDefinitions = null)
    {
        ArgumentNullException.ThrowIfNull(program);
        if (retainedGenericMethodDefinitions is not null
            && (sourceGenericMethodExports is null
                || !ReferenceEquals(sourceGenericMethodExports.Program,
                    program)
                || sourceTypeExports is not null
                || sourceClassExports is not null
                || (!StringComparer.Ordinal.Equals(schemaIdentity,
                        StandardGenericMethodCallsSchemaIdentity)
                    && !StringComparer.Ordinal.Equals(schemaIdentity,
                        StandardGenericMethodCallsParameterLabelsSchemaIdentity)
                    && !StringComparer.Ordinal.Equals(schemaIdentity,
                        StandardGenericMethodCallsParameterLabelsReceiverAccessSchemaIdentity))
                || !program.GenericMethodDefinitions.IsEmpty))
        {
            throw new ArgumentException(
                "Retained generic MethodDef selections require an exact G1-family source-free route.",
                nameof(retainedGenericMethodDefinitions));
        }
        if (program.EntryPoint == -1
            && program.Functions.Any(static function =>
                !function.IsPrivate && !function.IsInternal
                && function.HasPureEvaluationForm))
        {
            throw Unsupported(
                "The selected TRRM schema does not retain evaluation-form identities.");
        }
        if (!program.InterfaceWitnesses.IsEmpty)
        {
            throw Unsupported(
                "The selected TRRM source profile does not yet emit nominal interface witnesses.");
        }
        var supportsInitializationReferenceBodies
            = IsSelectedInitializationReferenceSchemaIdentity(
                schemaIdentity);
        if (program.Functions.Any(function =>
            function.DeclarationContractClauses.IsDefault
                || (!function.DeclarationContractClauses.IsEmpty
                    && (!supportsInitializationReferenceBodies
                        || !SemanticVerifier
                            .HasOnlyUnconditionalInitializationOutputContracts(
                                function, program)))))
        {
            throw Unsupported(
                "The selected TRRM schema does not retain these declaration contract clauses.");
        }
        if (!IsSelectedGeneralPredicateGraphsSchemaIdentity(schemaIdentity)
            && program.Functions.Any(static function =>
                !function.CheckedOutputStatePredicates.IsDefaultOrEmpty))
        {
            throw Unsupported(
                "The selected TRRM schema does not retain general OutputState predicates.");
        }
        RejectStaticInitializationFacts(program);
        ArgumentException.ThrowIfNullOrEmpty(moduleIdentity);
        ArgumentException.ThrowIfNullOrEmpty(schemaIdentity);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(
            moduleTargetRequirement,
            TrRmModuleTableReader.WindowsX64NativeTargetRequirement);
        if (BoundEnumTypes.HasStandardDeclarations(
                program.EnumDefinitions)
            && !IsSelectedC1EnumClosureSchemaIdentity(schemaIdentity))
        {
            throw Unsupported(
                "Standard enum origins require one of the exact EN1 schema identities.");
        }
        if (IsSelectedC1EnumClosureSchemaIdentity(schemaIdentity)
            && (BoundEnumTypes.HasStandardDeclarations(
                    program.EnumDefinitions)
                || !program.StandardClassDeclarations.IsEmpty))
        {
            if (sourceTypeExports is not null
                || sourceGenericMethodExports is not null
                || sourceClassExports is not null)
            {
                throw Unsupported(
                    "EN1 source-free enum re-emission does not accept fresh source mappings.");
            }

            return EmitC1EnumClosureDeclarationLibrary(program,
                moduleIdentity, schemaIdentity, moduleTargetRequirement,
                GetForeignInvocationSchema(schemaIdentity), null, null,
                null, null);
        }

        var requiresSourceClassExports
            = StringComparer.Ordinal.Equals(schemaIdentity,
                    StandardC1M1ConstructionSchemaIdentity)
                || StringComparer.Ordinal.Equals(schemaIdentity,
                    StandardC1M1Construction2SchemaIdentity)
                || StringComparer.Ordinal.Equals(schemaIdentity,
                    StandardC1M1Construction2InitializerCopyEvidenceSchemaIdentity)
                || StringComparer.Ordinal.Equals(schemaIdentity,
                    StandardC1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsSchemaIdentity)
                || StringComparer.Ordinal.Equals(schemaIdentity,
                    StandardC1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsInitializerCopyEvidenceSchemaIdentity)
                || StringComparer.Ordinal.Equals(schemaIdentity,
                    StandardCM1AR1SchemaIdentity)
                || StringComparer.Ordinal.Equals(schemaIdentity,
                    StandardC1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsAssociatedRelationConstraintsInitializerCopyEvidenceSchemaIdentity)
                || StringComparer.Ordinal.Equals(schemaIdentity,
                    StandardC1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsAssociatedRelationConstraintsTypeBindingViewsSchemaIdentity)
                || StandardGenericMethodSchema.IsClassTypeBindingViews(
                    schemaIdentity);
        var hasContextualTypeViews
            = program.StandardQuerySourceAssociation
                is BoundStandardQuerySourceAssociation typeViewAssociation
                && !typeViewAssociation.TypeViews.IsDefaultOrEmpty;
        var hasTopLevelMethodImports = program.Functions.Any(
            static function => function.ModuleImport is not null);
        var hasSupportedModuleImports = program.Functions.All(
            static function => function.ModuleImport is null
                || (function.IsExternal && function.IsModuleReference
                    && function.DeclaringStruct is null
                    && function.DirectNativeImport is null
                    && function.NativeVTableMethod is null
                    && function.NativeFunctionPointerType is null));
        var isMethodTypeReference
            = IsC1M1MethodTypeSchemaIdentity(schemaIdentity)
                && program.Classes.IsEmpty
                && program.StandardClassDeclarations.IsEmpty
                && hasSupportedModuleImports
                && (hasContextualTypeViews || hasTopLevelMethodImports
                    || IsSelectedTypeDefBindingEqualitySchemaIdentity(
                        schemaIdentity));
        if ((requiresSourceClassExports && sourceClassExports is null
                && program.StandardClassDeclarations.IsEmpty
                && program.StandardFieldProviderClosure is null
                && !isMethodTypeReference)
            || (sourceClassExports is not null
                && !StringComparer.Ordinal.Equals(schemaIdentity,
                    StandardC1M1SchemaIdentity)
                && !requiresSourceClassExports)
            || (sourceClassExports is not null
                && !ReferenceEquals(sourceClassExports.Program, program)))
        {
            throw new ArgumentException(
                "The selected C1M1 class route requires source-class mappings for the exact checked program.",
                nameof(sourceClassExports));
        }

        if (program.StandardFieldProviderClosure is { } fieldProviderClosure)
        {
            if (sourceTypeExports is not null
                || sourceGenericMethodExports is not null
                || sourceClassExports is not null
                || !StringComparer.Ordinal.Equals(schemaIdentity,
                    fieldProviderClosure.SchemaIdentity)
                || !StringComparer.Ordinal.Equals(moduleIdentity,
                    fieldProviderClosure.RootModuleIdentity)
                || moduleTargetRequirement
                    != fieldProviderClosure.TargetRequirement
                || fieldProviderClosure.RuntimePolicy != 0
                || fieldProviderClosure.Artifacts.IsEmpty
                || fieldProviderClosure.Artifacts[0].ArtifactIndex != 0
                || (program with {
                    StandardFieldProviderClosure = null,
                }) != fieldProviderClosure.OriginalProgram)
            {
                throw Unsupported(
                    "A bound Standard FieldDef provider closure can only re-emit its unchanged root program under the exact schema selection.");
            }

            if (StringComparer.Ordinal.Equals(schemaIdentity,
                    StandardInitReferenceBodiesNestedFieldDestinationsSchemaIdentity))
            {
                var existingProviderClosures
                    = program.StandardQuerySourceAssociation
                        ?.ProviderClosures ?? [];
                var providerClosures
                    = existingProviderClosures.Any(closure =>
                        ReferenceEquals(closure, fieldProviderClosure))
                        ? existingProviderClosures
                        : existingProviderClosures.Add(fieldProviderClosure);
                program = program with {
                    StandardFieldProviderClosure = null,
                    StandardQuerySourceAssociation
                        = new BoundStandardQuerySourceAssociation(
                            providerClosures),
                };
            }
            else
            {
                return [.. fieldProviderClosure.Artifacts[0]
                    .ArtifactSnapshot];
            }
        }

        if (IsSelectedStandardLayoutQueriesSchemaIdentity(schemaIdentity)
            && moduleTargetRequirement
                != TrRmModuleTableReader.WindowsX64NativeTargetRequirement)
        {
            throw Unsupported(
                "The current Standard executable source emitter requires the Windows x64 target.");
        }
        if (((schemaIdentity is StandardInitReferenceBodiesSchemaIdentity
                or StandardInitReferenceBodiesLocalFieldLayoutSchemaIdentity
                or StandardInitReferenceBodiesNestedFieldDestinationsSchemaIdentity
                or StandardInitReferenceBodiesPointerLoadsSchemaIdentity
                or StandardInitReferenceBodiesGeneralPredicateGraphsSchemaIdentity
                or StandardInitReferenceBodiesGeneralPredicateGraphsPointerLoadsSchemaIdentity
                or StandardInitReferenceBodiesPointerLoadsNativeParameterSchemaIdentity
                or StandardInitReferenceBodiesGeneralPredicateGraphsPointerLoadsNativeParameterSchemaIdentity)
            || IsSelectedC1SchemaIdentity(schemaIdentity))
            && moduleTargetRequirement
                != TrRmModuleTableReader.WindowsX64NativeTargetRequirement)
        {
            throw Unsupported(
                "The Standard initialization-reference Body profile requires the Windows x64 target.");
        }
        if ((schemaIdentity is StandardForeignInvocationSchemaIdentity
                or StandardForeignNativeInterfaceSchemaIdentity
                or StandardForeignNativeInterfaceParameterSurfaceSchemaIdentity
                or StandardForeignNativeInterfaceAggregateOutputConsumptionSchemaIdentity
                or StandardForeignInvocationStorageAddressSchemaIdentity
                or StandardForeignInvocationStorageFixedArraySchemaIdentity
                or StandardForeignInvocationStorageFixedArrayEmptyInitializationSchemaIdentity
                or StandardForeignInvocationStorageFixedArrayEmptyUnitInitializationSchemaIdentity
                or StandardForeignInvocationStorageFixedArrayEmptyUnitScalarFrozenValuesSchemaIdentity
                or StandardForeignInvocationStorageFixedArrayCopyReturnCoverageSchemaIdentity
                or StandardForeignInvocationStorageFixedArrayCopyReturnCoverageRepeatInitializationSchemaIdentity
                or StandardLocalAggregateLayoutSchemaIdentity
                or StandardLocalAggregatePointerLoadsSchemaIdentity
                or StandardForeignInvocationStorageGenericFixedArraySchemaIdentity)
            && moduleTargetRequirement
                != TrRmModuleTableReader.WindowsX64NativeTargetRequirement)
        {
            throw Unsupported(
                "The foreign-invocation source emitter requires the Windows x64 target.");
        }

        if ((StringComparer.Ordinal.Equals(schemaIdentity,
                StandardForeignInvocationSchemaIdentity)
                || StringComparer.Ordinal.Equals(schemaIdentity,
                    StandardForeignInvocationStorageAddressSchemaIdentity)
                || StringComparer.Ordinal.Equals(schemaIdentity,
                    StandardForeignInvocationStorageFixedArraySchemaIdentity))
            && HasDirectNativeOutputCall(program))
        {
            return EmitStandardNativeOutputCallProgram(program,
                moduleIdentity,
                StringComparer.Ordinal.Equals(schemaIdentity,
                    StandardForeignInvocationSchemaIdentity)
                    ? TrRmArtifactSchema.ForeignInvocationDraft
                    : StringComparer.Ordinal.Equals(schemaIdentity,
                        StandardForeignInvocationStorageAddressSchemaIdentity)
                        ? TrRmArtifactSchema.ForeignInvocationStorageAddressDraft
                        : TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyDraft);
        }

        if ((StringComparer.Ordinal.Equals(schemaIdentity,
                StandardForeignInvocationSchemaIdentity)
                || StringComparer.Ordinal.Equals(schemaIdentity,
                    StandardForeignInvocationStorageAddressSchemaIdentity))
            && HasStandaloneNativeOutputProviderDeclaration(program))
        {
            return EmitStandardNativeOutputProviderDeclaration(program,
                moduleIdentity, schemaIdentity, moduleTargetRequirement,
                StringComparer.Ordinal.Equals(schemaIdentity,
                    StandardForeignInvocationSchemaIdentity)
                    ? TrRmArtifactSchema.ForeignInvocationDraft
                    : TrRmArtifactSchema.ForeignInvocationStorageAddressDraft);
        }

        if (StringComparer.Ordinal.Equals(schemaIdentity,
                StandardC1M1SchemaIdentity)
            && sourceClassExports is not null)
        {
            if (sourceTypeExports is { Count: > 0 }
                || sourceGenericMethodExports?.Exports.IsEmpty == false
                || !program.StandardClassDeclarations.IsEmpty)
            {
                throw Unsupported(
                    "C1M1 source class declaration emission accepts only captured local class declarations.");
            }

            return EmitC1M1SourceClassDeclarations(program, moduleIdentity,
                moduleTargetRequirement, sourceClassExports, out _,
                out _);
        }

        if ((StringComparer.Ordinal.Equals(schemaIdentity,
                StandardC1M1SchemaIdentity)
                || StringComparer.Ordinal.Equals(schemaIdentity,
                    StandardC1M1Construction2SchemaIdentity)
                || StringComparer.Ordinal.Equals(schemaIdentity,
                    StandardC1M1Construction2InitializerCopyEvidenceSchemaIdentity)
                || StringComparer.Ordinal.Equals(schemaIdentity,
                    StandardC1M1ConstructionL1R1SchemaIdentity)
                || StringComparer.Ordinal.Equals(schemaIdentity,
                    StandardC1M1Construction2L1R1SchemaIdentity)
                || StringComparer.Ordinal.Equals(schemaIdentity,
                    StandardC1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsSchemaIdentity)
                || StringComparer.Ordinal.Equals(schemaIdentity,
                    StandardC1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsInitializerCopyEvidenceSchemaIdentity)
                || StringComparer.Ordinal.Equals(schemaIdentity,
                    StandardCM1AR1SchemaIdentity)
                || StringComparer.Ordinal.Equals(schemaIdentity,
                    StandardC1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsAssociatedRelationConstraintsInitializerCopyEvidenceSchemaIdentity)
                || StandardGenericMethodSchema.IsClassTypeBindingViews(
                    schemaIdentity))
            && !program.StandardClassDeclarations.IsEmpty)
        {
            if (sourceClassExports?.Exports.IsEmpty == false
                || sourceTypeExports is { Count: > 0 }
                || sourceGenericMethodExports?.Exports.IsEmpty == false)
            {
                throw Unsupported(
                    "C1M1 metadata-only class re-emission does not accept new source mappings.");
            }

            return EmitC1M1MetadataClassTypeReference(program,
                moduleIdentity, moduleTargetRequirement, schemaIdentity);
        }

        return schemaIdentity switch {
            StandardLayoutQueriesSchemaIdentity
                => EmitDevelopment(program, moduleIdentity,
                    TrRmArtifactSchema.Standard, moduleTargetRequirement,
                    sourceTypeExports),
            TrRmArtifactSchemaFacts
                .StandardTerrumFunctionPointerSchemaIdentity
                => EmitDevelopment(program, moduleIdentity,
                    TrRmArtifactSchema.Standard, moduleTargetRequirement,
                    sourceTypeExports,
                    standardSchemaIdentity: schemaIdentity),
            StandardInitReferenceBodiesSchemaIdentity
                => EmitDevelopment(program, moduleIdentity,
                    TrRmArtifactSchema.Standard, moduleTargetRequirement,
                    sourceTypeExports,
                    standardInitializationReferenceBodies: true),
            StandardInitReferenceBodiesPointerLoadsSchemaIdentity
                => EmitDevelopment(program, moduleIdentity,
                    TrRmArtifactSchema.Standard, moduleTargetRequirement,
                    sourceTypeExports,
                    standardInitializationReferenceBodies: true,
                    standardNativePointerLoads: true),
            StandardInitReferenceBodiesPointerLoadsNativeParameterSchemaIdentity
                => EmitDevelopment(program, moduleIdentity,
                    TrRmArtifactSchema
                        .InitializationReferencePointerLoadNativeParameterDraft,
                    moduleTargetRequirement, sourceTypeExports,
                    standardInitializationReferenceBodies: true,
                    standardNativePointerLoads: true),
            StandardInitReferenceBodiesGeneralPredicateGraphsPointerLoadsNativeParameterSchemaIdentity
                => EmitDevelopment(program, moduleIdentity,
                    TrRmArtifactSchema
                        .InitializationReferenceGeneralPredicatePointerLoadNativeParameterDraft,
                    moduleTargetRequirement, sourceTypeExports,
                    standardInitializationReferenceBodies: true,
                    standardNativePointerLoads: true),
            StandardInitReferenceBodiesLocalFieldLayoutSchemaIdentity
                => EmitDevelopment(program, moduleIdentity,
                    TrRmArtifactSchema.InitializationReferenceLocalFieldLayoutDraft,
                    moduleTargetRequirement, sourceTypeExports,
                    standardInitializationReferenceBodies: true),
            StandardInitReferenceBodiesNestedFieldDestinationsSchemaIdentity
                => EmitDevelopment(program, moduleIdentity,
                    TrRmArtifactSchema
                        .InitializationReferenceLocalFieldLayoutNestedFieldDestinationsDraft,
                    moduleTargetRequirement, sourceTypeExports,
                    standardInitializationReferenceBodies: true),
            StandardInitReferenceBodiesGeneralPredicateGraphsSchemaIdentity
                => EmitDevelopment(program, moduleIdentity,
                    TrRmArtifactSchema.GeneralPredicateGraphsDraft,
                    moduleTargetRequirement, sourceTypeExports,
                    standardInitializationReferenceBodies: true),
            StandardInitReferenceBodiesGeneralPredicateGraphsPointerLoadsSchemaIdentity
                => EmitDevelopment(program, moduleIdentity,
                    TrRmArtifactSchema.GeneralPredicateGraphsDraft,
                    moduleTargetRequirement, sourceTypeExports,
                    standardInitializationReferenceBodies: true,
                    standardNativePointerLoads: true),
            StandardC1SchemaIdentity
                => EmitC1InitializationReferenceBodies(program,
                    moduleIdentity, moduleTargetRequirement,
                    sourceTypeExports, sourceGenericMethodExports,
                    TrRmArtifactSchema.C1Draft),
            StandardC1L1R1SchemaIdentity
                => EmitC1InitializationReferenceBodies(program,
                    moduleIdentity, moduleTargetRequirement,
                    sourceTypeExports, sourceGenericMethodExports,
                    TrRmArtifactSchema.C1ParameterLabelsReceiverAccessDraft),
            StandardC1M1SchemaIdentity
                => EmitC1InitializationReferenceBodies(program,
                    moduleIdentity, moduleTargetRequirement,
                    sourceTypeExports, sourceGenericMethodExports,
                    TrRmArtifactSchema.C1M1Draft),
            StandardC1M1L1R1SchemaIdentity
                => EmitC1InitializationReferenceBodies(program,
                    moduleIdentity, moduleTargetRequirement,
                    sourceTypeExports, sourceGenericMethodExports,
                    TrRmArtifactSchema.C1M1ParameterLabelsReceiverAccessDraft),
            StandardC1M1ConstructionSchemaIdentity
                => EmitC1M1Construction(program, moduleIdentity,
                    moduleTargetRequirement, sourceTypeExports,
                    sourceGenericMethodExports,
                    sourceClassExports
                        ?? throw new ArgumentException(
                            "The C1M1 construction successor requires source-class mappings.",
                            nameof(sourceClassExports))),
            StandardC1M1ConstructionL1R1SchemaIdentity
                => EmitC1M1ConstructionL1R1(program, moduleIdentity,
                    moduleTargetRequirement, sourceTypeExports,
                    sourceGenericMethodExports),
            StandardC1M1Construction2SchemaIdentity
                => EmitC1M1Construction2(program, moduleIdentity,
                    moduleTargetRequirement, sourceTypeExports,
                    sourceGenericMethodExports,
                    sourceClassExports: sourceClassExports),
            StandardC1M1Construction2InitializerCopyEvidenceSchemaIdentity
                => EmitC1M1Construction2(program, moduleIdentity,
                    moduleTargetRequirement, sourceTypeExports,
                    sourceGenericMethodExports,
                    TrRmArtifactSchema.C1M1Construction2InitializerCopyEvidenceDraft,
                    sourceClassExports,
                    standardSchemaIdentity: schemaIdentity),
            StandardC1M1Construction2TypeBindingViewsSchemaIdentity
                => EmitC1M1Construction2(program, moduleIdentity,
                    moduleTargetRequirement, sourceTypeExports,
                    sourceGenericMethodExports,
                    TrRmArtifactSchema.C1M1Construction2InitializerCopyEvidenceDraft,
                    sourceClassExports,
                    standardSchemaIdentity: schemaIdentity),
            StandardC1M1Construction2ClassTypeBindingViewsSchemaIdentity
                => EmitC1M1Construction2(program, moduleIdentity,
                    moduleTargetRequirement, sourceTypeExports,
                    sourceGenericMethodExports,
                    TrRmArtifactSchema.C1M1Construction2InitializerCopyEvidenceDraft,
                    sourceClassExports,
                    standardSchemaIdentity: schemaIdentity),
            StandardC1M1Construction2L1R1SchemaIdentity
                => EmitC1M1Construction2(program, moduleIdentity,
                    moduleTargetRequirement, sourceTypeExports,
                    sourceGenericMethodExports,
                    TrRmArtifactSchema
                        .C1M1Construction2ParameterLabelsReceiverAccessDraft),
            StandardC1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsSchemaIdentity
                => EmitC1M1Construction2(program, moduleIdentity,
                    moduleTargetRequirement, sourceTypeExports,
                    sourceGenericMethodExports,
                    TrRmArtifactSchema
                        .C1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsDraft,
                    sourceClassExports),
            StandardC1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsInitializerCopyEvidenceSchemaIdentity
                => EmitC1M1Construction2(program, moduleIdentity,
                    moduleTargetRequirement, sourceTypeExports,
                    sourceGenericMethodExports,
                    TrRmArtifactSchema
                        .C1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsInitializerCopyEvidenceDraft,
                    sourceClassExports,
                    standardSchemaIdentity: schemaIdentity),
            StandardC1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsTypeBindingViewsSchemaIdentity
                => EmitC1M1Construction2(program, moduleIdentity,
                    moduleTargetRequirement, sourceTypeExports,
                    sourceGenericMethodExports,
                    TrRmArtifactSchema
                        .C1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsInitializerCopyEvidenceDraft,
                    sourceClassExports,
                    standardSchemaIdentity: schemaIdentity),
            StandardC1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsClassTypeBindingViewsSchemaIdentity
                => EmitC1M1Construction2(program, moduleIdentity,
                    moduleTargetRequirement, sourceTypeExports,
                    sourceGenericMethodExports,
                    TrRmArtifactSchema
                        .C1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsInitializerCopyEvidenceDraft,
                    sourceClassExports,
                    standardSchemaIdentity: schemaIdentity),
            StandardCM1AR1SchemaIdentity
                => EmitC1M1Construction2(program, moduleIdentity,
                    moduleTargetRequirement, sourceTypeExports,
                    sourceGenericMethodExports,
                    TrRmArtifactSchema
                        .C1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsAssociatedRelationConstraintsDraft,
                    sourceClassExports),
            StandardC1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsAssociatedRelationConstraintsInitializerCopyEvidenceSchemaIdentity
                => EmitC1M1Construction2(program, moduleIdentity,
                    moduleTargetRequirement, sourceTypeExports,
                    sourceGenericMethodExports,
                    TrRmArtifactSchema
                        .C1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsAssociatedRelationConstraintsInitializerCopyEvidenceDraft,
                    sourceClassExports,
                    standardSchemaIdentity: schemaIdentity),
            StandardC1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsAssociatedRelationConstraintsTypeBindingViewsSchemaIdentity
                => EmitC1M1Construction2(program, moduleIdentity,
                    moduleTargetRequirement, sourceTypeExports,
                    sourceGenericMethodExports,
                    TrRmArtifactSchema
                        .C1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsAssociatedRelationConstraintsInitializerCopyEvidenceDraft,
                    sourceClassExports,
                    standardSchemaIdentity: schemaIdentity),
            StandardC1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsAssociatedRelationConstraintsClassTypeBindingViewsSchemaIdentity
                => EmitC1M1Construction2(program, moduleIdentity,
                    moduleTargetRequirement, sourceTypeExports,
                    sourceGenericMethodExports,
                    TrRmArtifactSchema
                        .C1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsAssociatedRelationConstraintsInitializerCopyEvidenceDraft,
                    sourceClassExports,
                    standardSchemaIdentity: schemaIdentity),
            StandardNominalEnumsSchemaIdentity
                => EmitNominalEnumsDraftSource(program, moduleIdentity,
                    moduleTargetRequirement, sourceTypeExports),
            StandardForeignInvocationSchemaIdentity
                => EmitForeignInvocationDraftNativeAddressLibrary(
                    program, moduleIdentity),
            StandardForeignNativeInterfaceSchemaIdentity
                or StandardForeignNativeInterfaceParameterSurfaceSchemaIdentity
                or StandardForeignNativeInterfaceAggregateOutputConsumptionSchemaIdentity
                => EmitForeignNativeInterfaceDeclarations(program,
                    moduleIdentity, schemaIdentity, moduleTargetRequirement,
                    sourceTypeExports),
            StandardForeignInvocationStorageAddressSchemaIdentity
                => EmitForeignInvocationDraftNativeAddressLibrary(program,
                    moduleIdentity,
                    TrRmArtifactSchema.ForeignInvocationStorageAddressDraft),
            StandardForeignInvocationStorageFixedArraySchemaIdentity
                => EmitForeignInvocationDraftNativeAddressLibrary(program,
                    moduleIdentity,
                    TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyDraft,
                    sourceTypeExports),
            StandardForeignInvocationStorageFixedArrayEmptyInitializationSchemaIdentity
                => EmitForeignInvocationDraftNativeAddressLibrary(program,
                    moduleIdentity,
                    TrRmArtifactSchema
                        .ForeignInvocationStorageFixedArrayCopyEmptyInitializationDraft,
                    sourceTypeExports),
            StandardForeignInvocationStorageFixedArrayEmptyUnitInitializationSchemaIdentity
                => EmitForeignInvocationDraftNativeAddressLibrary(program,
                    moduleIdentity,
                    TrRmArtifactSchema
                        .ForeignInvocationStorageFixedArrayCopyEmptyUnitInitializationDraft,
                    sourceTypeExports),
            StandardForeignInvocationStorageFixedArrayEmptyUnitScalarFrozenValuesSchemaIdentity
                => EmitForeignInvocationDraftNativeAddressLibrary(program,
                    moduleIdentity,
                    TrRmArtifactSchema
                        .ForeignInvocationStorageFixedArrayCopyEmptyUnitScalarFrozenValuesDraft,
                    sourceTypeExports),
            StandardForeignInvocationStorageFixedArrayCopyReturnCoverageSchemaIdentity
                => EmitForeignInvocationDraftNativeAddressLibrary(program,
                    moduleIdentity,
                    TrRmArtifactSchema
                        .ForeignInvocationStorageFixedArrayCopyReturnCoverageDraft,
                    sourceTypeExports),
            StandardForeignInvocationStorageFixedArrayCopyReturnCoverageRepeatInitializationSchemaIdentity
                => EmitForeignInvocationDraftNativeAddressLibrary(program,
                    moduleIdentity,
                    TrRmArtifactSchema
                        .ForeignInvocationStorageFixedArrayCopyReturnCoverageRepeatInitializationDraft,
                    sourceTypeExports),
            StandardLocalAggregateLayoutSchemaIdentity
                => EmitLocalAggregateLayoutDraft(program, moduleIdentity,
                    sourceTypeExports),
            StandardLocalAggregatePointerLoadsSchemaIdentity
                => EmitLocalAggregatePointerLoadsDraft(program,
                    moduleIdentity, sourceTypeExports),
            StandardForeignInvocationStorageGenericFixedArraySchemaIdentity
                => EmitForeignInvocationDraftNativeAddressLibrary(program,
                    moduleIdentity,
                    TrRmArtifactSchema.ForeignInvocationStorageFixedArrayGenericCopyDraft),
            StandardRegisterExceptionRegionsSchemaIdentity
                => EmitRegisterExceptionRegionsDraft(program, moduleIdentity,
                    moduleTargetRequirement),
            StandardGenericMethodCallsSchemaIdentity
                => EmitGenericMethodCallsDraftSource(program, moduleIdentity,
                    moduleTargetRequirement, sourceGenericMethodExports,
                    sourceTypeExports,
                    retainedMethodDefinitions:
                        retainedGenericMethodDefinitions),
            StandardGenericMethodCallsParameterLabelsSchemaIdentity
                => EmitGenericMethodCallsDraftSource(program, moduleIdentity,
                    moduleTargetRequirement, sourceGenericMethodExports,
                    sourceTypeExports,
                    TrRmArtifactSchema.GenericMethodCallsParameterLabelsDraft,
                    retainedMethodDefinitions:
                        retainedGenericMethodDefinitions),
            StandardGenericMethodCallsParameterLabelsReceiverAccessSchemaIdentity
                => EmitGenericMethodCallsDraftSource(program, moduleIdentity,
                    moduleTargetRequirement, sourceGenericMethodExports,
                    sourceTypeExports,
                    TrRmArtifactSchema.GenericMethodCallsParameterLabelsReceiverAccessDraft,
                    retainedMethodDefinitions:
                        retainedGenericMethodDefinitions),
            _ => throw new ArgumentException(
                "The exact Standard TRRM schema identity is not supported by this emitter.",
                nameof(schemaIdentity)),
        };
    }

    private static byte[] EmitC1InitializationReferenceBodies(
        BoundProgram program, string moduleIdentity,
        uint moduleTargetRequirement,
        IReadOnlyList<TrRmSourceTypeExport>? sourceTypeExports,
        SourceGenericMethodExports? sourceGenericMethodExports,
        TrRmArtifactSchema schema)
    {
        var supportsL1 = schema.UsesGeneralMethodParameterLabels();
        if (!supportsL1 && (sourceTypeExports is not null
                || sourceGenericMethodExports is not null))
        {
            throw Unsupported(
                "C1 source declaration and generic MethodDef publication inputs are not yet integrated.");
        }
        if (!program.Classes.IsEmpty
            || (!supportsL1 && !program.Interfaces.IsEmpty)
            || !program.InterfaceWitnesses.IsEmpty
            || !program.GenericNominalTypeDefinitions.IsEmpty
            || !program.GenericNominalTypeInstances.IsEmpty)
        {
            throw Unsupported(
                "C1 class, interface, witness and generic nominal publication is not yet integrated.");
        }

        var hasGenericMethodCall = false;
        foreach (var function in program.Functions)
        {
            if (!BoundTreeTraversal.VisitExpressions(function.Body,
                    expression =>
                    {
                        if (expression is BoundCall call
                            && (call.GenericMethodCall is not null
                                || call.ImportedGenericMethodCall is not null))
                        {
                            hasGenericMethodCall = true;
                        }
                    }))
            {
                throw Unsupported(
                    "C1 cannot inspect a function Body for generic MethodDef calls.");
            }
        }

        if (!supportsL1
            && (!program.GenericMethodDefinitions.IsEmpty
                || !program.GenericMethodSignatures.IsEmpty
                || !program.GenericMethodOperatorProtocols.IsEmpty
                || hasGenericMethodCall))
        {
            throw Unsupported(
                "C1 generic MethodDef and GenericCall emission is not yet integrated.");
        }

        if (supportsL1)
        {
            return EmitGenericMethodCallsDraftSource(program, moduleIdentity,
                moduleTargetRequirement, sourceGenericMethodExports,
                sourceTypeExports, schema, baseSchema: schema,
                standardInitializationReferenceBodies: true);
        }

        return EmitDevelopment(program, moduleIdentity, schema,
            moduleTargetRequirement,
            standardInitializationReferenceBodies: true);
    }

    private static byte[] EmitC1M1Construction(BoundProgram program,
        string moduleIdentity, uint moduleTargetRequirement,
        IReadOnlyList<TrRmSourceTypeExport>? sourceTypeExports,
        SourceGenericMethodExports? sourceGenericMethodExports,
        SourceClassExports sourceClassExports)
    {
        if (!ReferenceEquals(sourceClassExports.Program, program)
            || sourceTypeExports is { Count: > 0 }
            || sourceGenericMethodExports is { Exports.IsEmpty: false }
            || !program.Classes.IsEmpty
            || !program.Interfaces.IsEmpty
            || !program.InterfaceWitnesses.IsEmpty
            || !program.StandardClassDeclarations.IsEmpty
            || !program.GenericNominalTypeDefinitions.IsEmpty
            || !program.GenericNominalTypeInstances.IsEmpty
            || !program.GenericMethodDefinitions.IsEmpty
            || !program.GenericMethodSignatures.IsEmpty)
        {
            throw Unsupported(
                "The C1M1 construction source route does not yet emit class or generic declaration closure.");
        }

        return EmitDevelopment(program, moduleIdentity,
            TrRmArtifactSchema.C1M1ConstructionDraft,
            moduleTargetRequirement,
            standardInitializationReferenceBodies: true);
    }

    private static byte[] EmitC1M1ConstructionL1R1(
        BoundProgram program, string moduleIdentity,
        uint moduleTargetRequirement,
        IReadOnlyList<TrRmSourceTypeExport>? sourceTypeExports,
        SourceGenericMethodExports? sourceGenericMethodExports)
    {
        if (!program.Classes.IsEmpty
            || !program.StandardClassDeclarations.IsEmpty
            || !program.GenericNominalTypeDefinitions.IsEmpty
            || !program.GenericNominalTypeInstances.IsEmpty
            || !program.InterfaceWitnesses.IsEmpty)
        {
            throw Unsupported(
                "The Construction-1 L1 source route does not yet emit class, witness or generic nominal closure.");
        }

        return EmitGenericMethodCallsDraftSource(program, moduleIdentity,
            moduleTargetRequirement, sourceGenericMethodExports,
            sourceTypeExports,
            TrRmArtifactSchema.C1M1ConstructionParameterLabelsReceiverAccessDraft,
            baseSchema: TrRmArtifactSchema.C1M1ConstructionDraft,
            standardInitializationReferenceBodies: true);
    }

    private static byte[] EmitC1M1Construction2(BoundProgram program,
        string moduleIdentity, uint moduleTargetRequirement,
        IReadOnlyList<TrRmSourceTypeExport>? sourceTypeExports,
        SourceGenericMethodExports? sourceGenericMethodExports,
        TrRmArtifactSchema schema
            = TrRmArtifactSchema.C1M1Construction2Draft,
        SourceClassExports? sourceClassExports = null,
        string? standardSchemaIdentity = null,
        bool hasCoreContractFailureIdentityTransport = false)
    {
        if (!hasCoreContractFailureIdentityTransport
            && program.Functions.Any(static function =>
                !function.DeclarationContractClauses.IsDefaultOrEmpty))
        {
            throw Unsupported(
                "Construction-2 declaration predicates require the context-returning emission route.");
        }

        var hasContextualTypeViews
            = program.StandardQuerySourceAssociation
                is BoundStandardQuerySourceAssociation association
                && !association.TypeViews.IsDefaultOrEmpty;
        var hasTopLevelMethodImports = program.Functions.Any(
            static function => function.ModuleImport is not null);
        var hasSupportedModuleImports = program.Functions.All(
            static function => function.ModuleImport is null
                || (function.IsExternal && function.IsModuleReference
                    && function.DeclaringStruct is null
                    && function.DirectNativeImport is null
                    && function.NativeVTableMethod is null
                    && function.NativeFunctionPointerType is null));
        var isMethodTypeReference
            = IsC1M1MethodTypeSchemaIdentity(
                standardSchemaIdentity ?? string.Empty)
                && program.Classes.IsEmpty
                && program.StandardClassDeclarations.IsEmpty
                && hasSupportedModuleImports
                && (hasContextualTypeViews || hasTopLevelMethodImports
                    || IsSelectedTypeDefBindingEqualitySchemaIdentity(
                        standardSchemaIdentity));
        if (hasContextualTypeViews
            && (!program.Classes.IsEmpty
                || !program.StandardClassDeclarations.IsEmpty
                || sourceClassExports is not null))
        {
            throw Unsupported(
                "The provisional contextual TypeDef profile is limited to classless method/type references.");
        }

        if (sourceClassExports is not null)
        {
            if (!ReferenceEquals(sourceClassExports.Program, program))
            {
                throw Unsupported(
                    "The construction-2 source class route requires its captured local declarations.");
            }

            if (sourceClassExports.Exports.IsEmpty
                && program.Classes.IsEmpty
                && program.StandardClassDeclarations.IsEmpty
                && program.Interfaces.IsEmpty
                && program.GenericNominalTypeDefinitions.IsEmpty
                && program.GenericNominalTypeInstances.IsEmpty
                && program.GenericMethodDefinitions.IsEmpty
                && program.GenericMethodSignatures.IsEmpty
                && sourceGenericMethodExports?.Exports.IsEmpty != false)
            {
                return EmitDevelopment(program, moduleIdentity, schema,
                    moduleTargetRequirement, sourceTypeExports,
                    standardInitializationReferenceBodies: true,
                    standardSchemaIdentity: standardSchemaIdentity,
                    hasCoreContractFailureIdentityTransport:
                        hasCoreContractFailureIdentityTransport);
            }

            if (schema.UsesClassParameterDefaults()
                && sourceTypeExports is not { Count: > 0 }
                && program.GenericMethodDefinitions
                    .SelectMany(static definition => definition.Formals)
                    .Any(static formal =>
                        formal.InterfaceConstraints.Any(static constraint =>
                            constraint.Kind
                                == BoundInterfaceTypeKind.Nominal
                            && constraint.ModuleImport is null)
                        || formal.AssociatedRelations.Any(static relation =>
                            relation.AssociatedType.DeclaringInterface
                                .ModuleImport is null)))
            {
                throw Unsupported(
                    "CM1 source generic methods over local interfaces require explicit source TypeDef mappings.");
            }

            var classArtifact = EmitC1M1SourceClassDeclarations(program,
                moduleIdentity, moduleTargetRequirement, sourceClassExports,
                out var frozenScalarValueBuilder,
                out var classMethodGenericFormalPlans, schema,
                standardSchemaIdentity);
            if (schema.UsesClassParameterDefaults()
                || !classMethodGenericFormalPlans.IsEmpty)
            {
                return EmitGenericMethodCallsDraftSource(program,
                    moduleIdentity, moduleTargetRequirement,
                    sourceGenericMethodExports, sourceTypeExports, schema,
                    baseSchema: schema,
                    standardInitializationReferenceBodies: true,
                    baseArtifact: classArtifact,
                    frozenScalarValueBuilder:
                        frozenScalarValueBuilder,
                    sourceClassExports: sourceClassExports,
                    sourceClassMethodGenericFormalPlans:
                        classMethodGenericFormalPlans,
                    standardSchemaIdentity: standardSchemaIdentity);
            }

            if (sourceTypeExports is { Count: > 0 }
                || sourceGenericMethodExports is { Exports.IsEmpty: false })
            {
                throw Unsupported(
                    "The Construction-2 class route does not yet compose D1 generic or interface metadata outside CM1.");
            }

            return classArtifact;
        }

        var supportsL1 = schema.UsesGeneralMethodParameterLabels();
        if ((!supportsL1 && sourceTypeExports is { Count: > 0 })
            || (!supportsL1
                && sourceGenericMethodExports is { Exports.IsEmpty: false })
            || !program.Classes.IsEmpty
            || (!supportsL1 && !program.Interfaces.IsEmpty)
            || !program.InterfaceWitnesses.IsEmpty
            || !program.StandardClassDeclarations.IsEmpty
            || !program.GenericNominalTypeDefinitions.IsEmpty
            || !program.GenericNominalTypeInstances.IsEmpty
            || (!supportsL1
                && (!program.GenericMethodDefinitions.IsEmpty
                    || !program.GenericMethodSignatures.IsEmpty
                    || !program.GenericMethodOperatorProtocols.IsEmpty)))
        {
            throw Unsupported(
                supportsL1
                    ? "The construction-2 L1 source route does not yet emit class, witness or generic nominal declaration closure."
                    : "The construction-2 source route does not yet emit class, witness, generic nominal or generic-method declaration closure.");
        }

        if (isMethodTypeReference)
        {
            return EmitDevelopment(program, moduleIdentity, schema,
                moduleTargetRequirement,
                standardInitializationReferenceBodies: true,
                standardSchemaIdentity: standardSchemaIdentity,
                hasCoreContractFailureIdentityTransport:
                    hasCoreContractFailureIdentityTransport);
        }

        if (supportsL1)
        {
            return EmitGenericMethodCallsDraftSource(program, moduleIdentity,
                moduleTargetRequirement, sourceGenericMethodExports,
                sourceTypeExports, schema, baseSchema:
                    TrRmArtifactSchema.C1M1Construction2Draft,
                standardInitializationReferenceBodies: true);
        }

        return EmitDevelopment(program, moduleIdentity,
            schema,
            moduleTargetRequirement,
            standardInitializationReferenceBodies: true,
            standardSchemaIdentity: standardSchemaIdentity,
            hasCoreContractFailureIdentityTransport:
                hasCoreContractFailureIdentityTransport);
    }

    internal static byte[] EmitDevelopmentFunctionPointerEffectsDraft(
        BoundProgram program, string moduleIdentity)
        => EmitDevelopment(program, moduleIdentity,
            TrRmArtifactSchema.DevelopmentFunctionPointerEffectsDraft);

    internal static byte[] EmitDevelopmentNativeConventionsDraft(
        BoundProgram program, string moduleIdentity)
        => EmitDevelopment(program, moduleIdentity,
            TrRmArtifactSchema.DevelopmentNativeConventionsDraft);

    private static byte[] EmitRegisterExceptionRegionsDraft(
        BoundProgram program, string moduleIdentity,
        uint moduleTargetRequirement)
        => EmitDevelopment(program, moduleIdentity,
            TrRmArtifactSchema.RegisterExceptionRegionsDraft,
            moduleTargetRequirement);

    private static bool HasNativeFunctionPointerValueUse(BoundProgram program)
    {
        if (program.Functions.Any(static function =>
                NativeFunctionPointerTypes.IsType(function.ReturnType)
                || function.Variables.Any(static variable =>
                    NativeFunctionPointerTypes.IsType(variable.Type)))
            || program.Structures.Any(static structure =>
                structure.Fields.Any(static field =>
                    NativeFunctionPointerTypes.IsType(field.Type)))
            || program.Classes.Any(static @class =>
                @class.Fields.Any(static field =>
                    NativeFunctionPointerTypes.IsType(field.Type)))
            || program.NativePointerTypes.Any(static pointer =>
                NativeFunctionPointerTypes.IsType(pointer.PointeeType)))
        {
            return true;
        }

        foreach (var function in program.Functions)
        {
            var found = false;
            if (!BoundTreeTraversal.VisitExpressions(function.Body,
                expression => found |= NativeFunctionPointerTypes.IsType(
                    expression.Type)) || found)
            {
                return true;
            }
        }

        return false;
    }

    private static void RejectStaticInitializationFacts(BoundProgram program)
    {
        if (!program.StaticFields.IsDefaultOrEmpty
            || !program.StaticInitializationGroups.IsDefaultOrEmpty
            || program.Functions.Any(static function =>
                function.StaticConstructorIdentity is not null
                    || function.StaticInitializationTrigger is not null))
        {
            throw Unsupported(
                "The selected TRRM schema does not yet retain static-constructor identity or trigger contracts.");
        }
    }

    private static void ValidateConstruction2DeclarationContractFailureContexts(
        BoundProgram program, string moduleIdentity)
    {
        var failureSelections
            = program.CoreContractFailureIdentitySelections;
        foreach (var function in program.Functions)
        {
            if (function.DeclarationContractClauses.IsDefaultOrEmpty)
            {
                continue;
            }

            if (function.IsExternal || function.ModuleImport is not null)
            {
                throw Unsupported(
                    "Construction-2 declaration predicate emission does not yet retain bodyless or imported failure-plan correspondence.");
            }

            var preconditionOrdinal = 0u;
            var postconditionOrdinal = 0u;
            var hasPostcondition = false;
            foreach (var clause in function.DeclarationContractClauses)
            {
                if (clause.ExplicitFailureType is PrimitiveType)
                {
                    throw Unsupported(
                        "Construction-2 phase-1 ExplicitType clauses require the selected precondition failure-binding map.");
                }

                switch (clause.Phase)
                {
                    case BoundDeclarationContractPhase.Precondition:
                        if (hasPostcondition
                            || clause.PhaseLocalOrdinal
                                != preconditionOrdinal++)
                        {
                            throw Unsupported(
                                "Construction-2 precondition graph ordinals must retain their contiguous source order.");
                        }
                        break;
                    case BoundDeclarationContractPhase.NormalPostcondition:
                        hasPostcondition = true;
                        if (clause.PhaseLocalOrdinal
                            != postconditionOrdinal++)
                        {
                            throw Unsupported(
                                "Construction-2 postcondition graph ordinals must retain their contiguous source order.");
                        }
                        break;
                    default:
                        throw Unsupported(
                            "Construction-2 declaration predicate emission received an unknown phase.");
                }

                if (clause.UsingModuleIdentity is not string usingModuleIdentity
                    || !StringComparer.Ordinal.Equals(usingModuleIdentity,
                        moduleIdentity)
                    || clause.SelectedFailureClass is not
                        CoreContractFailureDeclarationIdentity
                            selectedFailureClass)
                {
                    throw Unsupported(
                        "Construction-2 declaration failure context must retain its exact originating Module and selected Core class.");
                }

                if (failureSelections.IsDefaultOrEmpty)
                {
                    throw Unsupported(
                        "Construction-2 declaration failure context lacks its nonserialized Core role selection.");
                }

                var selection = failureSelections
                    .FirstOrDefault(candidate =>
                        StringComparer.Ordinal.Equals(
                            candidate.UsingModuleIdentity,
                            usingModuleIdentity));
                var expectedFailureClass = clause.Phase
                    == BoundDeclarationContractPhase.Precondition
                        ? selection?.RequirementExceptionClass
                        : selection?.PostconditionExceptionClass;
                if (selection is null
                    || expectedFailureClass != selectedFailureClass)
                {
                    throw Unsupported(
                        "Construction-2 declaration failure context does not match the selected Core role for its originating Module.");
                }
            }
        }
    }

    private static byte[] EmitDevelopment(BoundProgram program, string moduleIdentity,
        TrRmArtifactSchema schema,
        uint moduleTargetRequirement
            = TrRmModuleTableReader.WindowsX64NativeTargetRequirement,
        IReadOnlyList<TrRmSourceTypeExport>? sourceTypeExports = null,
        bool standardInitializationReferenceBodies = false,
        bool standardNativePointerLoads = false,
        bool allowGenericMethodNominalTypeTables = false,
        string? standardSchemaIdentity = null,
        bool hasCoreContractFailureIdentityTransport = false,
        Func<BoundInterfaceMemberIdentity, uint>?
            foreignInterfaceMethodMapTokenForCall = null,
        BoundProgram? semanticVerificationProgram = null)
    {
        ArgumentNullException.ThrowIfNull(program);
        var standardTerrumFunctionPointerProfile
            = schema.UsesTerrumFunctionPointer(standardSchemaIdentity);
        if (BoundEnumTypes.HasStandardDeclarations(
                program.EnumDefinitions)
            && !schema.UsesC1EnumClosure())
        {
            throw Unsupported(
                "Standard enum origins cannot be emitted under Development or a parent Standard schema.");
        }

        if (standardInitializationReferenceBodies
            && schema is not (TrRmArtifactSchema.Standard
                or TrRmArtifactSchema.InitializationReferenceLocalFieldLayoutDraft
                or TrRmArtifactSchema.InitializationReferenceLocalFieldLayoutNestedFieldDestinationsDraft
                or TrRmArtifactSchema.GeneralPredicateGraphsDraft
                or TrRmArtifactSchema.C1Draft
                or TrRmArtifactSchema.C1ParameterLabelsReceiverAccessDraft
                or TrRmArtifactSchema.C1M1Draft
                or TrRmArtifactSchema.C1M1ParameterLabelsReceiverAccessDraft
                or TrRmArtifactSchema.C1M1ConstructionDraft
                or TrRmArtifactSchema.C1M1ConstructionParameterLabelsReceiverAccessDraft
                or TrRmArtifactSchema.C1M1Construction2Draft
                or TrRmArtifactSchema.C1M1Construction2ParameterLabelsReceiverAccessDraft
                or TrRmArtifactSchema.C1M1Construction2InitializerCopyEvidenceDraft
                or TrRmArtifactSchema.C1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsInitializerCopyEvidenceDraft
                or TrRmArtifactSchema.C1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsAssociatedRelationConstraintsInitializerCopyEvidenceDraft
                or TrRmArtifactSchema.C1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsDraft
                or TrRmArtifactSchema.C1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsAssociatedRelationConstraintsDraft
                or TrRmArtifactSchema.C1M1ConstructionL1R1PropertyInitializedStateDraft
                or TrRmArtifactSchema.C1M1Construction2L1R1PropertyInitializedStateDraft
                or TrRmArtifactSchema.C1M1ConstructionL1R1EnumClosureDraft
                or TrRmArtifactSchema.C1M1Construction2L1R1EnumClosureDraft
                or TrRmArtifactSchema.C1M1ConstructionL1R1EnumClosureInitializerCopyEvidenceDraft
                or TrRmArtifactSchema.C1M1Construction2L1R1EnumClosureInitializerCopyEvidenceDraft
                or TrRmArtifactSchema.C1M1ConstructionL1R1EnumClosureClassInstanceLayoutInitializerCopyEvidenceDraft
                or TrRmArtifactSchema.C1M1Construction2L1R1EnumClosureClassInstanceLayoutInitializerCopyEvidenceDraft
                or TrRmArtifactSchema.C1M1ConstructionL1R1PropertyInitializedStateEnumClosureDraft
                or TrRmArtifactSchema.C1M1Construction2L1R1PropertyInitializedStateEnumClosureDraft
                or TrRmArtifactSchema.InitializationReferencePointerLoadNativeParameterDraft
                or TrRmArtifactSchema.InitializationReferenceGeneralPredicatePointerLoadNativeParameterDraft
                or TrRmArtifactSchema.ForeignNativeInterfaceDraft
                or TrRmArtifactSchema.ForeignNativeInterfaceParameterSurfaceDraft
                or TrRmArtifactSchema.ForeignNativeInterfaceAggregateOutputConsumptionDraft))
        {
            throw new ArgumentException(
                "Initialization-reference Bodies require a selected Standard or GeneralPredicateGraphsDraft profile.",
                nameof(schema));
        }
        if (standardNativePointerLoads
            && (!standardInitializationReferenceBodies
                || schema is not (TrRmArtifactSchema.Standard
                    or TrRmArtifactSchema.GeneralPredicateGraphsDraft
                    or TrRmArtifactSchema.InitializationReferencePointerLoadNativeParameterDraft
                    or TrRmArtifactSchema.InitializationReferenceGeneralPredicatePointerLoadNativeParameterDraft)))
        {
            throw new ArgumentException(
                "Native pointer loads require an exact B+I or B+I+G successor.",
                nameof(standardNativePointerLoads));
        }
        var standardNativeParameterProfile = schema is
            TrRmArtifactSchema.InitializationReferencePointerLoadNativeParameterDraft
                or TrRmArtifactSchema
                    .InitializationReferenceGeneralPredicatePointerLoadNativeParameterDraft;
        if (standardNativeParameterProfile
            && (program.NativeFunctionPointerSignatures.Length != 0
                || program.Functions.Any(HasNativeFunctionPointerBodyOperation)
                || program.Functions.Any(static function =>
                    function.NativeFunctionPointerType is not null
                    || function.NativeVTableMethod is not null
                    || (function.DirectNativeImport
                        is DirectNativeImport nativeImport
                        && !nativeImport.Outputs.IsDefaultOrEmpty))))
        {
            throw Unsupported(
                "The pointer-load NativeParameter successors do not select function-pointer anchors, NativeVTables or NativeOutput contracts.");
        }
        if (standardNativeParameterProfile && !standardNativePointerLoads)
        {
            throw new ArgumentException(
                "The pointer-load NativeParameter successors require the typed pointer-load Body operation.",
                nameof(standardNativePointerLoads));
        }
        var generalPredicateGraphsInitializationReferenceBodies
            = standardInitializationReferenceBodies
                && schema.UsesGeneralPredicateGraphs();
        var emitsConstruction2DeclarationPredicateGraphs
            = hasCoreContractFailureIdentityTransport
                && standardInitializationReferenceBodies
                && generalPredicateGraphsInitializationReferenceBodies
                && schema == TrRmArtifactSchema.C1M1Construction2Draft;
        bool IsScalarValueType(PrimitiveType type)
            => Scalar(type)
                || (generalPredicateGraphsInitializationReferenceBodies
                    && (PrimitiveTypeFacts.IsBuiltInScalar(type)
                        || (emitsConstruction2DeclarationPredicateGraphs
                            && TryGetOptionalBuiltInScalarType(type,
                                program.CheckedReferenceTypes, out _))));
        if (program.EntryPoint == -1
            && program.Functions.Any(static function =>
                !function.IsPrivate && !function.IsInternal
                && function.HasPureEvaluationForm))
        {
            throw Unsupported(
                "The selected TRRM schema does not retain evaluation-form identities.");
        }
        if (sourceTypeExports is not null
            && schema is not (TrRmArtifactSchema.Standard
                or TrRmArtifactSchema.InitializationReferenceLocalFieldLayoutDraft
                or TrRmArtifactSchema.InitializationReferenceLocalFieldLayoutNestedFieldDestinationsDraft
                or TrRmArtifactSchema.ForeignNativeInterfaceDraft
                or TrRmArtifactSchema.ForeignNativeInterfaceParameterSurfaceDraft
                or TrRmArtifactSchema.ForeignNativeInterfaceAggregateOutputConsumptionDraft))
        {
            throw Unsupported(
                "Explicit source type exports require an exact Standard schema.");
        }
        if (program.Functions.Any(static function =>
            function.ImportedGenericMethod is not null))
        {
            throw Unsupported(
                "The selected TRRM schema does not yet retain imported generic MethodDef specialization origins.");
        }
        if (program.Functions.Any(function =>
            function.DeclarationContractClauses.IsDefault
                || (!function.DeclarationContractClauses.IsEmpty
                    && (!standardInitializationReferenceBodies
                        || (!emitsConstruction2DeclarationPredicateGraphs
                            && !SemanticVerifier
                                .HasOnlyUnconditionalInitializationOutputContracts(
                                    function, program))))))
        {
            throw Unsupported(
                "The selected TRRM schema does not retain these declaration contract clauses.");
        }
        if (emitsConstruction2DeclarationPredicateGraphs)
        {
            ValidateConstruction2DeclarationContractFailureContexts(
                program, moduleIdentity);
        }
        if (!generalPredicateGraphsInitializationReferenceBodies
            && program.Functions.Any(static function =>
                !function.CheckedOutputStatePredicates.IsDefaultOrEmpty))
        {
            throw Unsupported(
                "The selected TRRM schema does not retain general OutputState predicates.");
        }
        RejectStaticInitializationFacts(program);
        var standardNativeExportEmission = schema
                == TrRmArtifactSchema.Standard
            && program.Functions.Any(static function => function.HasNativeExport);
        var hasNativeExportTypeMethods = program.Functions.Any(
            static function => function.HasNativeExport
                && function.DeclaringStruct is not null);
        var standardNativeCallableEmission = standardNativeExportEmission
            || (schema == TrRmArtifactSchema.Standard
                && program.Functions.Any(static function =>
                    function.NativeCallableConvention is not null));
        if (program.Functions.Any(static function =>
                function.HasNativeExport)
            && !standardNativeExportEmission)
        {
            throw Unsupported(
                "The selected TRRM schema has no NativeExport source projection.");
        }

        if (standardNativeCallableEmission
            && (program.Functions.Any(static function =>
                    function.DirectNativeImport is not null
                    || function.NativeVTableMethod is not null
                    || function.NativeCallableEntryName is not null
                    || (function.HasNativeExport
                        && function.DeclaringStruct is not null
                        && !function.IsStatic))
                || !program.NativePointerTypes.IsEmpty
                || HasNativeFunctionPointerValueUse(program)))
        {
            throw Unsupported(
                "The current Standard NativeExport source emitter requires scalar method signatures without other native pointer contracts.");
        }
        if (BoundGenericNominalTypeContract.UsesAny(program)
            && (!allowGenericMethodNominalTypeTables
                || program.Functions.Any(static function =>
                    BoundGenericNominalTypeTypes.IsType(function.ReturnType)
                    || function.Variables.Any(static variable =>
                        BoundGenericNominalTypeTypes.IsType(variable.Type)))))
        {
            throw Unsupported(
                "The executable TRRM profile has no generic nominal signature or body lowering.");
        }
        if (program.Functions.Any(function =>
            function.IsExternal && function.DirectNativeImport is null
                && (function.NativeFunctionPointerType is null
                    || function.ModuleImport is null)
                && !(standardInitializationReferenceBodies
                    && function.IsModuleReference
                    && function.ModuleImport is not null)))
        {
            throw Unsupported("External TRRM declarations require a direct import or retained native callable dependency.");
        }
        if (string.IsNullOrEmpty(moduleIdentity))
        {
            throw new ArgumentException("A module identity is required.", nameof(moduleIdentity));
        }

        var programToVerify = semanticVerificationProgram ?? program;
        if (programToVerify.EntryPoint != program.EntryPoint
            || !programToVerify.Functions.Equals(program.Functions))
        {
            throw new ArgumentException(
                "The emitted program and semantic verification program have different function identities.",
                nameof(semanticVerificationProgram));
        }

        if (programToVerify.EntryPoint == -1)
        {
            SemanticVerifier.VerifyLibraryReferences(programToVerify);
        }
        else if (programToVerify.Functions.Any(
                     static function => function.IsExternal))
        {
            SemanticVerifier.VerifyReferences(programToVerify);
        }
        else
        {
            SemanticVerifier.Verify(programToVerify);
        }

        if (BoundEnumTypes.UsesAny(program))
        {
            throw Unsupported("Standard TRRM has no assigned nominal enum TypeSpec or TypeDef encoding.");
        }
        if (!NativePointerTypeContract.IsValidTable(program))
        {
            throw Unsupported("The native pointer or function-pointer type table is invalid.");
        }
        if (program.NativeFunctionPointerSignatures.Any(signature =>
            !(schema.UsesDevelopmentFunctionPointerEffects()
                ? NativeFunctionPointerContract.CanPersistDevelopmentFunctionPointerEffectsDraft(
                    signature)
                : NativeFunctionPointerContract.CanPersistRevision92(signature))))
        {
            throw Unsupported(
                "The Development function-pointer signature table does not persist complete invocation contracts.");
        }
        if (standardTerrumFunctionPointerProfile
            && program.Functions.Any(
                HasNativeFunctionPointerBodyOperation))
        {
            throw Unsupported(
                "The selected Standard function-pointer successor does not admit NativeAddress formation or call.value execution.");
        }
        foreach (var function in program.Functions)
        {
            if (!BoundTreeTraversal.VisitExpressions(function.Body, expression => {
                if (expression is BoundNativeFunctionAddress address
                    && ((uint)address.Function >= (uint)program.Functions.Length
                        || program.Functions[address.Function].NativeFunctionPointerType
                            != address.Type))
                {
                    throw Unsupported(
                        "The Development address operation cannot persist a projected function-pointer view.");
                }
            }))
            {
                throw Unsupported("A function Body cannot be traversed for function-pointer addresses.");
            }
        }
        if (!schema.UsesDevelopmentNativeConventions()
            && (program.NativeFunctionPointerSignatures
                    .Select((signature, index) => (signature, index))
                    .Any(pair => pair.signature.CallingConvention
                            != NativeCallingConvention.Native
                            && !(standardNativeCallableEmission
                            && program.Functions.Any(function =>
                                function.NativeFunctionPointerType
                                    == NativeFunctionPointerTypes.Type(
                                        pair.index)
                                && function.NativeCallableConvention
                                            == pair.signature.CallingConvention)))
                || program.Functions.Any(function =>
                    (function.NativeCallableConvention is NativeCallingConvention convention
                                && convention != NativeCallingConvention.Native
                                    && !standardNativeCallableEmission)
                    || (!standardInitializationReferenceBodies
                        && function.DirectNativeImport
                            is DirectNativeImport import
                        && (import.CallingConvention != NativeCallingConvention.Native
                            || import.ResolutionMode != NativeImportResolutionMode.Link
                            || import.ResolutionPolicy
                                != NativeImportResolutionPolicy.NotApplicable)))))
        {
            throw Unsupported(
                "The current TRRM function-pointer writer does not preserve normalized calling conventions.");
        }
        foreach (var function in program.Functions)
        {
            if (function.NativeFunctionPointerType is not PrimitiveType nativeFunctionPointerType)
            {
                continue;
            }

            if (function.DirectNativeImport is not null
                || !NativeFunctionPointerContract.MatchesFunction(function,
                    nativeFunctionPointerType, program.NativeFunctionPointerSignatures))
            {
                throw Unsupported($"Function '{function.Name}' has an invalid native callable contract.");
            }

            var signatureIndex = NativeFunctionPointerTypes.Index(nativeFunctionPointerType);
            var signature = program.NativeFunctionPointerSignatures[signatureIndex];
            if ((signature.ReturnType != PrimitiveType.Void
                    && !NativeFunctionPointerContract.IsSupportedValueType(
                        signature.ReturnType, program.NativeFunctionPointerSignatures))
                || signature.ParameterTypes.Any(type =>
                    !NativeFunctionPointerContract.IsSupportedValueType(
                        type, program.NativeFunctionPointerSignatures)))
            {
                throw Unsupported($"Function '{function.Name}' has a native callable type outside the TRRM value subset.");
            }
        }
        var blittableStructureIndexes = CollectNativeBlittableStructureIndexes(program);
        var hasRegisteredQueryOperations = program.Functions
            .Any(HasStandardRegisteredQuery);
        if (schema.UsesInitializationReferenceLocalFieldLayout()
            && hasRegisteredQueryOperations)
        {
            throw Unsupported(
                "The initialization-reference local-field layout profile does not select LayoutQueries.");
        }
        var hasSourceTypeExports = sourceTypeExports is { Count: > 0 };
        var standardInitializationFieldStructureIndexes
            = standardInitializationReferenceBodies
                ? CollectStandardInitializationReferenceFieldStructureIndexes(
                    program)
                : [];
        var includeStandardInitializationFieldMetadata
            = !standardInitializationFieldStructureIndexes.IsEmpty;
        var includeQueryStructureMetadata
            = (schema is TrRmArtifactSchema.Standard
                or TrRmArtifactSchema.InitializationReferenceLocalFieldLayoutDraft
                or TrRmArtifactSchema.InitializationReferenceLocalFieldLayoutNestedFieldDestinationsDraft
                or TrRmArtifactSchema.RegisterExceptionRegionsDraft
                or TrRmArtifactSchema.ForeignNativeInterfaceDraft
                or TrRmArtifactSchema.ForeignNativeInterfaceParameterSurfaceDraft
                or TrRmArtifactSchema.ForeignNativeInterfaceAggregateOutputConsumptionDraft)
            && (hasRegisteredQueryOperations || hasSourceTypeExports
                || (schema.UsesForeignNativeInterface()
                    && !blittableStructureIndexes.IsEmpty)
                || (schema.UsesInitializationReferenceLocalFieldLayout()
                    && program.Structures.Any(static structure =>
                        structure.StandardLayoutOnly
                        || structure.StandardTypeOrigin is not null)));
        var emittedBlittableStructureIndexSet
            = blittableStructureIndexes.ToHashSet();
        if (includeQueryStructureMetadata)
        {
            emittedBlittableStructureIndexSet.UnionWith(
                CollectStandardRegisteredQueryStructureIndexes(program,
                    sourceTypeExports,
                    includeStandardTypeOrigins:
                        schema.UsesInitializationReferenceLocalFieldLayout()));
        }

        if (sourceTypeExports is { Count: > 0 })
        {
            if (program.SourceTypeDeclarations.IsDefault)
            {
                throw Unsupported(
                    "Explicit source TypeDef exports require retained source declaration identities.");
            }

            var sourceDeclarations = program.SourceTypeDeclarations
                .ToDictionary(static declaration =>
                    (declaration.Namespace, declaration.Name));
            var pendingSourceStructures = new Stack<int>();
            foreach (var export in sourceTypeExports)
            {
                if (export is null
                    || !sourceDeclarations.TryGetValue(
                        (export.Namespace, export.Name),
                        out var declaration))
                {
                    throw new ArgumentException(
                        "A source TypeDef ExportKey does not select a checked declaration.",
                        nameof(sourceTypeExports));
                }

                pendingSourceStructures.Push(
                    declaration.StructureIndex);
            }

            while (pendingSourceStructures.TryPop(
                out var sourceStructureIndex))
            {
                if ((uint)sourceStructureIndex
                    >= (uint)program.Structures.Length)
                {
                    throw Invalid(
                        "A source TypeDef export has an invalid structure identity.");
                }
                if (!emittedBlittableStructureIndexSet.Add(
                        sourceStructureIndex))
                {
                    continue;
                }

                if (emittedBlittableStructureIndexSet.Count
                    > SemanticVerifier.MaximumVariables)
                {
                    throw Unsupported(
                        "The source TypeDef closure exceeds the checked structure limit.");
                }

                foreach (var field in program.Structures[
                        sourceStructureIndex].Fields)
                {
                    var nestedStructureIndex = (int)field.Type - 100;
                    if ((uint)nestedStructureIndex
                        < (uint)program.Structures.Length)
                    {
                        pendingSourceStructures.Push(
                            nestedStructureIndex);
                    }
                }
            }
        }

        var emittedBlittableStructureIndexes
            = emittedBlittableStructureIndexSet
                .OrderBy(static index => index).ToArray();
        var contextualTypeViews
            = program.StandardQuerySourceAssociation
                is BoundStandardQuerySourceAssociation typeViewAssociation
                && !typeViewAssociation.TypeViews.IsDefault
                    ? typeViewAssociation.TypeViews
                    : ImmutableArray<BoundStandardTypeView>.Empty;
        var hasContextualTypeViews = !contextualTypeViews.IsEmpty;
        if (hasContextualTypeViews
            && !IsC1M1MethodTypeSchemaIdentity(
                standardSchemaIdentity ?? string.Empty))
        {
            throw Unsupported(
                "Contextual TypeDef views require an exact Construction-2 method/type schema.");
        }
        if (hasContextualTypeViews
            && program.Structures.Any(static structure =>
                structure.StandardTypeOrigin is
                    BoundStandardTypeOrigin origin
                && origin.ContextualViewIndexes.Length > 1)
            && !IsSelectedTypeDefBindingEqualitySchemaIdentity(
                standardSchemaIdentity))
        {
            throw Unsupported(
                "Distinct contextual TypeDef expectations require an exact TypeBindingViews schema.");
        }

        var typeDefStructureIndexSet
            = emittedBlittableStructureIndexSet.ToHashSet();
        typeDefStructureIndexSet.UnionWith(
            standardInitializationFieldStructureIndexes);
        if (hasContextualTypeViews)
        {
            typeDefStructureIndexSet.UnionWith(
                Enumerable.Range(0, program.Structures.Length));
        }

        var typeDefStructureIndexes = typeDefStructureIndexSet
            .OrderBy(static index => index).ToArray();
        if (hasNativeExportTypeMethods)
        {
            typeDefStructureIndexes = [
                .. Enumerable.Range(0, program.Structures.Length),
            ];
        }
        var typeDefStructureViewIndexes
            = Enumerable.Repeat(-1, typeDefStructureIndexes.Length)
                .ToArray();
        if (hasContextualTypeViews)
        {
            var structureViews = new List<(int StructureIndex,
                int TypeViewIndex)>();
            foreach (var structureIndex in typeDefStructureIndexes)
            {
                var origin = program.Structures[structureIndex]
                    .StandardTypeOrigin;
                if (origin is null
                    || origin.ContextualViewIndexes.IsDefaultOrEmpty)
                {
                    throw Invalid(
                        "A retained contextual TypeDef has no complete view-index map.");
                }

                foreach (var typeViewIndex in origin.ContextualViewIndexes)
                {
                    if ((uint)typeViewIndex
                            >= (uint)contextualTypeViews.Length
                        || contextualTypeViews[typeViewIndex]
                            .StructureIndex != structureIndex)
                    {
                        throw Invalid(
                            "A contextual TypeDef view does not map to its canonical structure.");
                    }

                    structureViews.Add((structureIndex, typeViewIndex));
                }
            }

            if (structureViews.Select(static view => view.TypeViewIndex)
                    .Distinct().Count() != contextualTypeViews.Length)
            {
                throw Invalid(
                    "The selected contextual TypeDef views do not have a one-to-one emitted destination.");
            }

            typeDefStructureIndexes = [.. structureViews.Select(
                static view => view.StructureIndex)];
            typeDefStructureViewIndexes = [.. structureViews.Select(
                static view => view.TypeViewIndex)];
        }
        if (typeDefStructureIndexes.Any(index =>
                program.Structures[index].SourceGenericTypeDefinitionIndex
                    >= 0))
        {
            throw Unsupported(
                "The selected TRRM schema has no assigned source generic type persistence.");
        }
        var blittableStructureIndexSet
            = emittedBlittableStructureIndexSet.ToHashSet();
        blittableStructureIndexSet.UnionWith(
            standardInitializationFieldStructureIndexes);
        if (program.RuntimePolicy != BoundRuntimePolicy.FailFast
            || !program.Classes.IsEmpty || !program.Buffers.IsEmpty)
        {
            throw Unsupported("Executable TRRM supports fail-fast scalar bodies and declaration-only native boundary libraries.");
        }
        if (!hasNativeExportTypeMethods
            && typeDefStructureIndexes.Length == 0
            && !program.Structures.IsEmpty)
        {
            throw Unsupported(
                "The selected TRRM schema has structure declarations but no admitted TypeDef metadata route.");
        }
        if (!blittableStructureIndexes.IsEmpty
            && !includeQueryStructureMetadata
            && !includeStandardInitializationFieldMetadata
            && (program.EntryPoint != -1
                || program.Functions.Any(static function =>
                    !function.IsExternal
                    && function.NativeVTableMethod is null
                    && !function.HasNativeExport)))
        {
            throw Unsupported(
                "The selected TRRM schema has Blittable structure use without a complete layout metadata route.");
        }
        if (hasNativeExportTypeMethods && program.EntryPoint != -1)
        {
            throw Unsupported(
                "NativeExport type methods require a declaration-only library.");
        }
        if (!standardInitializationReferenceBodies
            && program.Functions.Any(static function =>
                function.IndexedBody is not null))
        {
            throw Unsupported(
                "The selected TRRM schema does not support indexed Body re-emission.");
        }
        if (program.EntryPoint >= 0
            && (program.Functions[program.EntryPoint].ReturnType != PrimitiveType.Int32
                || program.Functions[program.EntryPoint].ParameterCount != 0))
        {
            throw Unsupported("Executable entry must return int32 and take no parameters.");
        }
        if (program.Functions.Any(static function =>
                function.Variables.Any(static variable =>
                    variable.IsImplicitRef || variable.IsImplicitReferenceTemporary)))
        {
            throw Unsupported("Executable TRRM does not persist implicit reference parameter contracts.");
        }

        var functionFeatures = new (bool HasFloatingTypes, bool HasIndexedFlow,
            bool HasNonScalarStorage)[program.Functions.Length];
        var outputFeatures = new bool[program.Functions.Length];
        var foreignInterfaceCallFeatures
            = new bool[program.Functions.Length];
        if (standardInitializationReferenceBodies
            && schema.UsesForeignNativeInterface())
        {
            for (var index = 0; index < program.Functions.Length; index++)
            {
                foreignInterfaceCallFeatures[index]
                    = HasForeignNativeInterfaceCall(
                        program.Functions[index]);
            }
        }
        var hasFloatingTypes = false;
        var parameterCount = 0;
        var effectCount = 0;
        var exportCount = 0;
        var nativeImportCount = 0;
        var nativeCallableCount = 0;
        var nativeCallableContractCount = 0;
        var nativeVTableCount = 0;
        for (var index = 0; index < program.Functions.Length; index++)
        {
            var function = program.Functions[index];
            if (function.NoFailureProof)
            {
                throw Unsupported(
                    $"Function '{function.Name}' carries a retired NoThrow claim.");
            }
            if (function.IsExternal && function.DirectNativeImport is not null
                && !DirectNativeImportContract.IsValid(function, program.Structures,
                    program.NativeFunctionPointerSignatures,
                    program.NativePointerTypes, program.EnumDefinitions))
            {
                throw Unsupported($"Function '{function.Name}' has an invalid direct native import contract.");
            }
            if (function.ModuleImport is BoundModuleImport moduleImport
                && (!((function.IsExternal
                            && function.NativeFunctionPointerType is not null)
                        || (standardInitializationReferenceBodies
                            && function.IsExternal
                            && function.IsModuleReference
                            && function.NativeFunctionPointerType is null
                            && function.DirectNativeImport is null
                            && function.NativeVTableMethod is null)
                        || (standardNativePointerLoads
                            && schema.UsesNativeParameterContracts()
                            && function.IsExternal
                            && function.IsModuleReference
                            && function.NativeFunctionPointerType is null
                            && function.DirectNativeImport is not null
                            && function.NativeVTableMethod is null)
                        || (!function.IsExternal && function.NativeVTableMethod is not null
                            && function.Intrinsic == BoundIntrinsic.NativeVTableCall))
                    || string.IsNullOrEmpty(moduleImport.ProducerIdentity)
                    || moduleImport.ExportKey != function.Name
                    || moduleImport.TargetRequirement != 1 || moduleImport.RuntimePolicy != 0))
            {
                throw Unsupported($"Function '{function.Name}' has an invalid native dependency.");
            }
            var hasNativeVTable = function.NativeVTableMethod is not null;
            if (hasNativeVTable
                && (!NativeVTableCallContract.IsValid(function, program)
                    || function.Intrinsic != BoundIntrinsic.NativeVTableCall))
            {
                throw Unsupported($"Function '{function.Name}' has an invalid native vtable contract.");
            }
            var features = function.IsExternal || hasNativeVTable
                || foreignInterfaceCallFeatures[index] ? default
                : AnalyzeFunction(function, program,
                    generalPredicateGraphsInitializationReferenceBodies,
                    emitsConstruction2DeclarationPredicateGraphs);
            functionFeatures[index] = features;
            outputFeatures[index] = !function.IsExternal && !hasNativeVTable
                && HasOutputFeature(function, program);
            hasFloatingTypes |= features.HasFloatingTypes;
            if (function.DirectNativeImport is DirectNativeImport directImport)
            {
                hasFloatingTypes |= IsFloat(function.ReturnType);
                hasFloatingTypes |= directImport.Outputs.Any(static output => IsFloat(output.PointeeType));
                hasFloatingTypes |= directImport.Parameters.Any(static parameter => IsFloat(parameter.PointeeType));
                hasFloatingTypes |= function.Variables.Take(function.ParameterCount)
                    .Any(static variable => IsFloat(variable.Type));
            }
            if (hasNativeVTable)
            {
                hasFloatingTypes |= IsFloat(function.ReturnType)
                    || function.Variables.Take(function.ParameterCount)
                        .Any(static variable => IsFloat(variable.Type));
            }
            parameterCount = checked(parameterCount + function.ParameterCount);
            effectCount += function.DoesNotReturn || function.RequiresUnsafeCaller ? 1 : 0;
            exportCount += !function.IsPrivate && !function.IsInternal
                && function.ModuleImport is null ? 1 : 0;
            nativeImportCount += function.DirectNativeImport is not null ? 1 : 0;
            nativeCallableCount += function.NativeFunctionPointerType is not null
                && !function.IsExternal ? 1 : 0;
            nativeCallableContractCount += function.NativeFunctionPointerType is not null ? 1 : 0;
            nativeVTableCount += hasNativeVTable ? 1 : 0;
        }
        if (schema.UsesRegisterExceptionRegions()
            && outputFeatures.Any(static enabled => enabled))
        {
            throw Unsupported(
                "The B+E1 source producer does not compose NativeOutput stream Bodies.");
        }

        foreach (var signature in program.NativeFunctionPointerSignatures)
        {
            hasFloatingTypes |= IsFloat(signature.ReturnType)
                || signature.ParameterTypes.Any(IsFloat);
        }
        foreach (var structureIndex in typeDefStructureIndexes)
        {
            hasFloatingTypes |= program.Structures[structureIndex].Fields
                .Any(static field => IsFloat(field.Type));
        }
        hasFloatingTypes |= program.NativePointerTypes.Any(
            static pointer => IsFloat(pointer.PointeeType));
        var names = new List<(string Text, int Encoding)>(
            program.Functions.Length + program.NativeFunctionPointerSignatures.Length + 1)
        {
            (moduleIdentity, 1),
        };
        var standardProviderSnapshots = new Dictionary<string,
            BoundSourceTypeProviderSnapshot>(StringComparer.Ordinal);
        void AddStandardProviderSnapshot(
            BoundSourceTypeProviderSnapshot provider)
        {
            var isMethodTypeSchema = IsC1M1MethodTypeSchemaIdentity(
                standardSchemaIdentity ?? string.Empty);
            var expectedSchemaIdentity = isMethodTypeSchema
                ? standardSchemaIdentity!
                : schema
                    .UsesNestedInitializationReferenceFieldDestinations()
                ? StandardInitReferenceBodiesNestedFieldDestinationsSchemaIdentity
                : schema.UsesInitializationReferenceLocalFieldLayout()
                    ? StandardInitReferenceBodiesLocalFieldLayoutSchemaIdentity
                    : IsSelectedStandardLayoutQueriesSchemaIdentity(
                        standardSchemaIdentity)
                        ? standardSchemaIdentity!
                    : StandardLayoutQueriesSchemaIdentity;
            if ((!isMethodTypeSchema
                    && schema is not (TrRmArtifactSchema.Standard
                    or TrRmArtifactSchema.InitializationReferenceLocalFieldLayoutDraft
                    or TrRmArtifactSchema.InitializationReferenceLocalFieldLayoutNestedFieldDestinationsDraft))
                || !StringComparer.Ordinal.Equals(provider.SchemaIdentity,
                    expectedSchemaIdentity)
                || string.IsNullOrEmpty(provider.ModuleIdentity))
            {
                throw Unsupported(
                    "Standard source type origins require a selected provider schema and Module identity.");
            }

            if (standardProviderSnapshots.TryGetValue(provider.ModuleIdentity,
                    out var existing)
                && !SameA2SourceTypeProviderSnapshot(existing, provider))
            {
                throw Unsupported(
                    "One selected Module identity has conflicting provider snapshots.");
            }

            standardProviderSnapshots[provider.ModuleIdentity] = provider;
        }

        foreach (var structure in program.Structures)
        {
            if (structure.StandardTypeOrigin is { } typeOrigin)
            {
                AddStandardProviderSnapshot(typeOrigin.Provider);
            }
            foreach (var field in structure.Fields)
            {
                if (field.StandardFieldOrigin is { } fieldOrigin)
                {
                    AddStandardProviderSnapshot(fieldOrigin.Provider);
                }
            }
        }
        if (standardProviderSnapshots.Count != 0
            && schema.UsesNestedInitializationReferenceFieldDestinations())
        {
            var sourceAssociation
                = program.StandardQuerySourceAssociation
                    ?? throw Unsupported(
                        "A nested FieldPath TypeDef/FieldDef Import has no retained provider association.");
            var retainedProviderModules
                = ValidateStandardQueryProviderClosures(
                    sourceAssociation.ProviderClosures);
            foreach (var provider in standardProviderSnapshots.Values)
            {
                ValidateStandardQueryProviderSnapshot(provider,
                    retainedProviderModules);
            }
        }
        else if (standardProviderSnapshots.Count != 0
            && !schema.UsesInitializationReferenceLocalFieldLayout())
        {
            var sourceAssociation
                = program.StandardQuerySourceAssociation
                    ?? throw Unsupported(
                        "A Standard source TypeDef/FieldDef Import has no retained provider association.");
            var retainedProviderModules
                = ValidateStandardQueryProviderClosures(
                    sourceAssociation.ProviderClosures);
            foreach (var provider in standardProviderSnapshots.Values)
            {
                ValidateStandardQueryProviderSnapshot(provider,
                    retainedProviderModules);
            }
        }
        else if (schema.UsesInitializationReferenceLocalFieldLayout()
            && standardProviderSnapshots.Values.Any(static provider =>
                provider.TargetRequirement
                    != TrRmModuleTableReader.WindowsX64NativeTargetRequirement
                || provider.RuntimePolicy != 0))
        {
            throw Unsupported(
                "Initialization-reference local-field TypeDef Imports require the retained Windows x64 fail-fast provider tuple.");
        }

        var moduleImportIdentities = program.Functions
            .Where(static function => function.ModuleImport is not null)
            .Select(static function => function.ModuleImport!.ProducerIdentity)
            .Concat(standardProviderSnapshots.Keys)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static identity => identity, StringComparer.Ordinal)
            .ToArray();
        var moduleReferenceStringRows = new Dictionary<string, int>(
            moduleImportIdentities.Length, StringComparer.Ordinal);
        foreach (var identity in moduleImportIdentities)
        {
            moduleReferenceStringRows.Add(identity, names.Count + 1);
            names.Add((identity, 1));
        }
        var moduleReferenceRowByIdentity = new Dictionary<string, int>(
            moduleImportIdentities.Length, StringComparer.Ordinal);
        for (var index = 0; index < moduleImportIdentities.Length; index++)
        {
            moduleReferenceRowByIdentity.Add(moduleImportIdentities[index],
                index + 1);
        }
        var structureTypeDefRows = new Dictionary<int, int>(
            typeDefStructureIndexes.Length);
        var structureIndexesByName = new Dictionary<string, List<int>>(
            typeDefStructureIndexes.Length, StringComparer.Ordinal);
        var structureNamespaceRows = new int[typeDefStructureIndexes.Length];
        var structureNameRows = new int[typeDefStructureIndexes.Length];
        var structureFieldNameRows = new int[typeDefStructureIndexes.Length][];
        var typeDefRowByContextualView = new Dictionary<int, int>(
            contextualTypeViews.Length);
        BoundStruct TypeDefStructure(int typeDefIndex)
        {
            var structureIndex = typeDefStructureIndexes[typeDefIndex];
            var typeViewIndex = typeDefStructureViewIndexes[typeDefIndex];
            return typeViewIndex < 0
                ? program.Structures[structureIndex]
                : contextualTypeViews[typeViewIndex].ViewDeclaration;
        }

        bool IsConstruction2CopyValueType(PrimitiveType type)
        {
            if (!schema.UsesConstructionInitializerCopyEvidence()
                || BoundEnumTypes.IsType(type))
            {
                return false;
            }

            var structureIndex = (int)type - 100;
            if ((uint)structureIndex
                    >= (uint)program.Structures.Length
                || !typeDefStructureIndexSet.Contains(structureIndex))
            {
                return false;
            }

            var structure = program.Structures[structureIndex];
            return (structure.StandardTypeOrigin is null
                    ? !structure.IsUnique
                    : structure.DeclaredCopyEvidence == true)
                && structure.BlittableTargetRequirement
                    == FixedLayoutStorage.WindowsX64TargetRequirement
                && !structure.IsUnique
                && structure.CleanupMethod is null
                && (structure.IsSequential || structure.IsTransparent);
        }

        for (var index = 0; index < typeDefStructureIndexes.Length; index++)
        {
            var structureIndex = typeDefStructureIndexes[index];
            var structure = TypeDefStructure(index);
            var separator = structure.Name.LastIndexOf('.', StringComparison.Ordinal);
            var typeNamespace = separator < 0 ? "" : structure.Name[..separator];
            var typeName = separator < 0 ? structure.Name : structure.Name[(separator + 1)..];
            if (typeName.Length == 0
                || !structureTypeDefRows.ContainsKey(structureIndex)
                    && !structureTypeDefRows.TryAdd(structureIndex,
                        index + 1))
            {
                throw Unsupported($"Native Blittable structure identity '{structure.Name}' cannot be represented in TRRM.");
            }
            if (!structureIndexesByName.TryGetValue(structure.Name,
                    out var structureIndexes))
            {
                structureIndexes = [];
                structureIndexesByName.Add(structure.Name,
                    structureIndexes);
            }
            else if (structureIndexes.Any(existingStructureIndex =>
                    program.Structures[existingStructureIndex]
                        .StandardTypeOrigin is null
                    && structure.StandardTypeOrigin is null))
            {
                throw Unsupported(
                    $"Private Blittable structure identity '{structure.Name}' is selected more than once.");
            }

            if (typeDefStructureViewIndexes[index] >= 0
                && !typeDefRowByContextualView.TryAdd(
                    typeDefStructureViewIndexes[index], index + 1))
            {
                throw Invalid(
                    "A contextual TypeDef view has multiple destination rows.");
            }
            structureIndexes.Add(structureIndex);
            structureNamespaceRows[index] = names.Count + 1;
            names.Add((typeNamespace, 1));
            structureNameRows[index] = names.Count + 1;
            names.Add((typeName, 1));
            structureFieldNameRows[index] = new int[structure.Fields.Length];
            for (var field = 0; field < structure.Fields.Length; field++)
            {
                structureFieldNameRows[index][field] = names.Count + 1;
                names.Add((structure.Fields[field].Name, 1));
            }
        }

        var nameRows = new int[program.Functions.Length];
        var nativeLibraryRows = new int[program.Functions.Length];
        var nativeExportRows = new int[program.Functions.Length];
        var nativeExportEntryNameRows = new int[program.Functions.Length];
        var nativeCallableContractRows = new int[program.Functions.Length];
        var nativeCallableEntryNameRows = new int[program.Functions.Length];
        var nativeCallableEntryNames = new HashSet<string>(StringComparer.Ordinal);
        var seen = new HashSet<string>(program.Functions.Length, StringComparer.Ordinal);
        var bodies = new List<ReadOnlyMemory<byte>>(program.Functions.Length);
        var fieldPathRows = new List<TrRmFieldPathRow>();
        var standardInitializationFieldPathRows
            = new Dictionary<string, uint>(StringComparer.Ordinal);
        var registeredQueryBodyCount = 0;
        var signatures = new List<TrRmMethodSigRow>(program.Functions.Length);
        var parameters = new List<TrRmParamRow>(parameterCount);
        var methodParameterSurfaceRows
            = new List<C1M1ParameterSurfaceRow>();
        var methods = new List<TrRmMethodDefRow>(
            program.Functions.Length + program.NativeFunctionPointerSignatures.Length);
        var contracts = new List<TrRmContractRow>(
            effectCount + nativeImportCount + nativeCallableCount + nativeVTableCount
            + (2 * program.NativeFunctionPointerSignatures.Length));
        var predicateGraphs = new List<TrRmPredicateGraphRow>();
        var predicateCaptures = new List<TrRmPredicateCaptureRow>();
        var predicateTypeUses = new List<TrRmPredicateTypeUseRow>();
        var predicateTypeUseRowsByTypeSpecRef
            = new Dictionary<uint, uint>();
        var predicateTypePatterns
            = new List<ReadOnlyMemory<byte>>();
        var predicateExactTypePatternRowsByTypeUseRow
            = new Dictionary<uint, uint>();
        var predicateReferenceTypePatternRows
            = new Dictionary<(byte Kind, uint ExactTypePatternRow), uint>();
        var predicateReadFootprints
            = new List<TrRmPredicateReadFootprintRow>();
        var predicateReadRoots = new List<TrRmPredicateReadRootRow>();
        var predicateNodes = new List<TrRmPredicateNodeRow>();
        var predicateNodeOperands
            = new List<TrRmPredicateNodeOperandRow>();
        var predicateChildEdges = new List<TrRmPredicateChildEdgeRow>();
        var predicateOperations = new List<TrRmPredicateOperationRow>();
        var predicateFrozenScalarValues
            = new TrRmFrozenScalarValueBuilder();
        var exports = new List<TrRmExportRow>(exportCount);
        var typeRowsList = (hasFloatingTypes ? s_typeRows : s_nonFloatTypeRows).ToList();
        var generalPredicateScalarTypeRows
            = new Dictionary<PrimitiveType, int>();
        if (generalPredicateGraphsInitializationReferenceBodies)
        {
            for (var index = 0; index < typeRowsList.Count; index++)
            {
                if (PrimitiveTypeFacts.IsBuiltInScalar(s_types[index]))
                {
                    generalPredicateScalarTypeRows.Add(s_types[index],
                        index + 1);
                }
            }
        }
        var hasNativeRawPointerType = program.Functions.Any(static function =>
            function.ReturnType == PrimitiveType.NativeRawPointer
            || function.Variables.Any(static variable => variable.Type == PrimitiveType.NativeRawPointer))
            || program.NativeFunctionPointerSignatures.Any(static signature =>
                signature.ReturnType == PrimitiveType.NativeRawPointer
                || signature.ParameterTypes.Contains(PrimitiveType.NativeRawPointer))
            || program.NativePointerTypes.Any(static type =>
                type.PointeeType == PrimitiveType.NativeRawPointer)
            || program.Structures.Any(static structure => structure.Fields.Any(
                static field => field.Type == PrimitiveType.NativeRawPointer));
        var nativeRawPointerTypeRow = 0;
        if (hasNativeRawPointerType)
        {
            nativeRawPointerTypeRow = typeRowsList.Count + 1;
            typeRowsList.Add(TrRmTypeSpecTableWriter.InlineFamily(0x8C, 0));
        }
        var nativeFunctionPointerTypeRows =
            new Dictionary<PrimitiveType, int>(program.NativeFunctionPointerSignatures.Length);
        for (var signature = 0;
            !standardNativeCallableEmission
            &&
            signature < program.NativeFunctionPointerSignatures.Length; signature++)
        {
            var row = typeRowsList.Count + 1;
            var signatureContract
                = program.NativeFunctionPointerSignatures[signature];
            var anchorMethodDefRow = checked(
                (uint)(program.Functions.Length + signature + 1));
            if (schema.IsDevelopmentProfile())
            {
                typeRowsList.Add(TrRmTypeSpecTableWriter
                    .DevelopmentFunctionPointerSignatureReference(
                        checked((uint)(signature + 1))));
            }
            else if (standardTerrumFunctionPointerProfile
                && signatureContract.CallingConvention
                    == NativeCallingConvention.Native)
            {
                typeRowsList.Add(
                    TrRmTypeSpecTableWriter.NativeFunctionPointer(
                        0, anchorMethodDefRow,
                        standardSchemaIdentity!));
            }
            else if (schema.UsesForeignInvocationContracts()
                && signatureContract.CallingConvention
                    != NativeCallingConvention.Native)
            {
                typeRowsList.Add(
                    TrRmTypeSpecTableWriter.NativeFunctionPointer(
                        (byte)signatureContract.CallingConvention,
                        anchorMethodDefRow));
            }
            else
            {
                throw Unsupported(
                    "The selected TRRM schema has no function-pointer TypeSpec anchor for this convention.");
            }
            nativeFunctionPointerTypeRows.Add(NativeFunctionPointerTypes.Type(signature), row);
        }
        var nativePointerTypeRows = new Dictionary<PrimitiveType, int>(program.NativePointerTypes.Length);
        var nativeOutputTypeRows = new Dictionary<PrimitiveType, int>();
        var nativeParameterTypeRows = new Dictionary<PrimitiveType, int>();
        if (nativeRawPointerTypeRow != 0)
        {
            nativeParameterTypeRows.Add(PrimitiveType.Void, nativeRawPointerTypeRow);
        }
        var blittableParameterTypeRows = new Dictionary<PrimitiveType, int>();
        var typeDefTypeSpecRows = new Dictionary<int, int>();
        var checkedReferenceTypeSpecRows
            = new Dictionary<PrimitiveType, int>();
        var activeCheckedReferenceTypeRows
            = new HashSet<PrimitiveType>();
        var structureFieldFirst = new int[typeDefStructureIndexes.Length];
        var structureLayoutFirst = new int[typeDefStructureIndexes.Length];
        var structureLayoutCount = new int[typeDefStructureIndexes.Length];
        var fieldRows = new List<TrRmFieldDefRow>();
        var layoutRows = new List<TrRmLayoutRow>(blittableStructureIndexes.Length * 2);
        var layoutFieldRows = new List<TrRmLayoutFieldRow>();
        var fieldRowCount = 0;
        for (var index = 0; index < typeDefStructureIndexes.Length; index++)
        {
            var fieldCount = TypeDefStructure(index).Fields.Length;
            structureFieldFirst[index] = fieldCount == 0
                ? 0 : checked(fieldRowCount + 1);
            fieldRowCount = checked(fieldRowCount + fieldCount);
        }

        int GeneralPredicateScalarTypeSpecRow(PrimitiveType type)
        {
            if (generalPredicateScalarTypeRows.TryGetValue(type,
                    out var existingRow))
            {
                return existingRow;
            }

            for (var typeCode = 0; typeCode <= 0x19; typeCode++)
            {
                if (TrRmConstantSourceValueValidator.TryGetBoundScalarType(
                        (byte)typeCode, out var candidate)
                    && candidate == type)
                {
                    var row = checked(typeRowsList.Count + 1);
                    typeRowsList.Add(TrRmTypeSpecTableWriter.Builtin(
                        (byte)typeCode));
                    generalPredicateScalarTypeRows.Add(type, row);
                    return row;
                }
            }

            throw Unsupported(
                $"The GeneralPredicateGraphsDraft profile cannot represent scalar type {type}.");
        }

        int TypeSpecRowFor(PrimitiveType type)
        {
            if (CheckedReferenceTypes.IsType(type))
            {
                return CheckedReferenceTypeSpecRow(type);
            }
            if (type == PrimitiveType.Void)
            {
                return TypeRow(type);
            }
            if (PrimitiveTypeFacts.IsBuiltInScalar(type))
            {
                return generalPredicateGraphsInitializationReferenceBodies
                    ? GeneralPredicateScalarTypeSpecRow(type)
                    : TypeRow(type);
            }

            return NativeValueTypeRow(type, nativeRawPointerTypeRow,
                nativeFunctionPointerTypeRows, typeRowsList,
                nativePointerTypeRows, program.NativePointerTypes,
                structureTypeDefRows, typeDefTypeSpecRows, TypeSpecRowFor);
        }

        uint PredicateTypeUseRowFor(PrimitiveType type)
        {
            var typeSpecRef = checked((uint)TypeSpecRowFor(type));
            if (predicateTypeUseRowsByTypeSpecRef.TryGetValue(typeSpecRef,
                    out var existingRow))
            {
                return existingRow;
            }

            var row = checked((uint)predicateTypeUses.Count + 1);
            predicateTypeUses.Add(
                new TrRmPredicateTypeUseRow(typeSpecRef));
            predicateTypeUseRowsByTypeSpecRef.Add(typeSpecRef, row);
            return row;
        }

        uint ExactTypePatternRowFor(uint typeUseRow)
        {
            if (predicateExactTypePatternRowsByTypeUseRow.TryGetValue(
                    typeUseRow, out var existingRow))
            {
                return existingRow;
            }

            var data = new byte[12];
            data[0] = 1;
            BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(4),
                typeUseRow);
            var row = checked((uint)predicateTypePatterns.Count + 1);
            predicateTypePatterns.Add(new ReadOnlyMemory<byte>(data));
            predicateExactTypePatternRowsByTypeUseRow.Add(typeUseRow, row);
            return row;
        }

        uint ReferenceTypePatternRowFor(PrimitiveType type)
        {
            if (!CheckedReferenceTypeContract
                .IsSupportedReferencePatternType(type,
                    program.CheckedReferenceTypes))
            {
                throw Unsupported(
                    "A reference-pattern TypePattern has an invalid checked-reference TypeSpec.");
            }

            var reference = program.CheckedReferenceTypes[
                CheckedReferenceTypes.Index(type)];
            var exactTypeUseRow
                = PredicateTypeUseRowFor(reference.ReferentType);
            var exactTypePatternRow
                = ExactTypePatternRowFor(exactTypeUseRow);
            var kind = reference.IsWritable ? (byte)10 : (byte)9;
            var key = (kind, exactTypePatternRow);
            if (predicateReferenceTypePatternRows.TryGetValue(key,
                    out var existingRow))
            {
                return existingRow;
            }

            var data = new byte[16];
            data[0] = kind;
            BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(8), 1);
            BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(12),
                exactTypePatternRow);
            var row = checked((uint)predicateTypePatterns.Count + 1);
            predicateTypePatterns.Add(new ReadOnlyMemory<byte>(data));
            predicateReferenceTypePatternRows.Add(key, row);
            return row;
        }

        uint FieldDefRowFor(BoundFieldRead field)
        {
            var structureIndex = (int)field.Receiver.Type - 100;
            if (structureIndex < 0
                || !structureTypeDefRows.TryGetValue(structureIndex,
                    out var typeDefRow)
                || typeDefRow <= 0
                || typeDefRow > typeDefStructureIndexes.Length)
            {
                throw Unsupported(
                    "A Standard initialization-reference field has no local TypeDef row.");
            }

            var typeDefIndex = typeDefRow - 1;
            if (typeDefStructureIndexes[typeDefIndex] != structureIndex
                || structureIndex >= program.Structures.Length
                || (uint)field.Field
                    >= (uint)program.Structures[structureIndex].Fields.Length
                || program.Structures[structureIndex].Fields[field.Field].Type
                    != field.Type)
            {
                throw Unsupported(
                    "A Standard initialization-reference field is outside its local structure.");
            }

            var firstFieldRow = structureFieldFirst[typeDefIndex];
            var fieldRow = checked(firstFieldRow + field.Field);
            if (firstFieldRow == 0 || fieldRow > fieldRows.Count)
            {
                throw Unsupported(
                    "A Standard initialization-reference field has no FieldDef row.");
            }

            return checked((uint)fieldRow);
        }

        int CheckedReferenceTypeSpecRow(PrimitiveType type)
        {
            if (checkedReferenceTypeSpecRows.TryGetValue(type,
                    out var existingRow))
            {
                return existingRow;
            }
            if (!CheckedReferenceTypeContract.IsValidCompleteType(type,
                    program.CheckedReferenceTypes)
                || !activeCheckedReferenceTypeRows.Add(type))
            {
                throw Unsupported(
                    "A checked-reference TypeSpec is invalid or recursive.");
            }

            try
            {
                var definition = program.CheckedReferenceTypes[
                    CheckedReferenceTypes.Index(type)];
                if (definition.IsOptionalValue
                    && emitsConstruction2DeclarationPredicateGraphs)
                {
                    if (!TryGetOptionalBuiltInScalarType(type,
                            program.CheckedReferenceTypes,
                            out var payloadType)
                        || payloadType != definition.ReferentType)
                    {
                        throw Unsupported(
                            "GeneralPredicateGraphsDraft optional-value TypeSpecs require a fixed-scalar payload.");
                    }

                    var payloadRow = TypeSpecRowFor(payloadType);
                    var optionalRow = checked(typeRowsList.Count + 1);
                    typeRowsList.Add(TrRmTypeSpecTableWriter.Wrapped(0x06,
                        checked((uint)payloadRow)));
                    checkedReferenceTypeSpecRows.Add(type, optionalRow);
                    return optionalRow;
                }
                if (definition.IsOptionalValue || definition.IsOptional)
                {
                    throw Unsupported(
                        "Optional checked-reference TypeSpecs are outside the executable TRRM profile.");
                }

                int row;
                var innerRow = TypeSpecRowFor(definition.ReferentType);
                var innerTypeSpecRef = checked((uint)innerRow);
                if (definition.IsReferentWritable)
                {
                    if (innerRow <= 0
                        || innerRow > typeRowsList.Count
                        || typeRowsList[innerRow - 1].Length < 2)
                    {
                        throw Unsupported(
                            "A checked-reference writable referent has no row-backed TypeSpec.");
                    }

                    var qualifiedInnerType = typeRowsList[innerRow - 1]
                        .ToArray();
                    qualifiedInnerType[1] |= 0x01;
                    innerTypeSpecRef = checked(
                        (uint)typeRowsList.Count + 1);
                    typeRowsList.Add(qualifiedInnerType);
                }
                row = typeRowsList.Count + 1;
                if (definition.IsInitialization)
                {
                    typeRowsList.Add(
                        TrRmTypeSpecTableWriter.InitializationReference(
                            innerTypeSpecRef,
                            definition.IsWritable));
                }
                else
                {
                    var qualifiers = (byte)(
                        (definition.IsWritable ? 1 : 0)
                        | (definition.IsGarbageCollected ? 2 : 0));
                    typeRowsList.Add(TrRmTypeSpecTableWriter.Wrapped(
                        0x07, innerTypeSpecRef, qualifiers));
                }

                checkedReferenceTypeSpecRows.Add(type, row);
                return row;
            }
            finally
            {
                _ = activeCheckedReferenceTypeRows.Remove(type);
            }
        }

        void AddStandardInitializationOutputStateContracts(
            uint owner,
            ImmutableArray<NativeFunctionPointerSelector> selectors,
            ImmutableArray<NativeFunctionPointerOutputState> outputStates)
        {
            var graphRowsBySelector = new Dictionary<uint, int>(
                selectors.Length);
            foreach (var selector in selectors.OrderBy(
                static item => item.PhaseLocalOrdinal))
            {
                var graphRow = checked(predicateGraphs.Count + 1);
                predicateGraphs.Add(new TrRmPredicateGraphRow(graphRow,
                    graphRow));
                predicateReadFootprints.Add(
                    new TrRmPredicateReadFootprintRow(graphRow, 1, 0, 0));
                predicateReadRoots.Add(
                    new TrRmPredicateReadRootRow(5, 0));
                predicateNodes.Add(new TrRmPredicateNodeRow(
                    selector.NodeKind, 0xF200_0001,
                    checked((uint)graphRow), 1));
                predicateNodeOperands.Add(
                    new TrRmPredicateNodeOperandRow(1,
                        checked((uint)graphRow)));
                if (!graphRowsBySelector.TryAdd(
                        selector.PhaseLocalOrdinal, graphRow))
                {
                    throw Unsupported(
                        "A Standard initialization-reference selector is duplicated.");
                }

                var predicatePayload = new byte[12];
                predicatePayload[0] = selector.PhaseTag;
                BinaryPrimitives.WriteUInt32LittleEndian(
                    predicatePayload.AsSpan(4),
                    selector.PhaseLocalOrdinal);
                BinaryPrimitives.WriteUInt32LittleEndian(
                    predicatePayload.AsSpan(8),
                    checked((uint)graphRow));
                contracts.Add(new TrRmContractRow(owner, 0x0B,
                    predicatePayload));
            }

            foreach (var outputState in outputStates)
            {
                if (!graphRowsBySelector.TryGetValue(
                        outputState.SelectorOrdinal,
                        out var graphRow))
                {
                    throw Unsupported(
                        "A Standard initialization-reference OutputState has no selector.");
                }

                var outputPayload = new byte[12];
                BinaryPrimitives.WriteUInt32LittleEndian(outputPayload,
                    outputState.FormalOrdinal);
                BinaryPrimitives.WriteUInt32LittleEndian(
                    outputPayload.AsSpan(4), checked((uint)graphRow));
                outputPayload[8] = outputState.State;
                contracts.Add(new TrRmContractRow(owner, 0x0002,
                    outputPayload));
            }
        }

        BoundExpression[] GetGeneralPredicateChildren(
            BoundExpression expression)
        {
            if (expression is BoundRead or BoundConstant
                or BoundIsInitialized
                or BoundIsNull
                or BoundWildcardPattern
                or BoundTypeTestPattern
                or BoundTypedValuePattern
                or BoundReferenceTypeTestPattern
                or BoundContractReturnValue
                or BoundReferenceInitializationTest or BoundIsSome
                or BoundIsVariablePattern
                or BoundBinary { IsConstantPatternTest: true })
            {
                return [];
            }

            return expression switch {
                BoundUnary {
                    IsPatternComposition: true,
                    PatternObservation: not null,
                } unary => [unary.Operand],
                BoundBinary {
                    IsPatternComposition: true,
                    PatternObservation: not null,
                } binary => [
                    binary.Left,
                    binary.Right,
                ],
                BoundUnary {
                    Operator: TokenKind.Bang,
                    Type: PrimitiveType.Bool,
                    Operand.Type: PrimitiveType.Bool,
                } unary => [unary.Operand],
                BoundUnary unary when
                    TryGetGeneralPredicateUnaryOperation(unary,
                        out _, out _) => [unary.Operand],
                BoundCast cast when
                    TryGetGeneralPredicateConversion(cast,
                        out _, out _) => [cast.Operand],
                BoundBinary {
                    Type: PrimitiveType.Bool,
                    Operator: TokenKind.AmpersandAmpersand
                        or TokenKind.PipePipe
                        or TokenKind.Ampersand
                        or TokenKind.Pipe
                        or TokenKind.Caret,
                    Left.Type: PrimitiveType.Bool,
                    Right.Type: PrimitiveType.Bool,
                } binary => [binary.Left, binary.Right],
                BoundBinary binary when
                    TryGetGeneralPredicateBinaryOperation(binary,
                        out _, out _) => [
                            binary.Left,
                            binary.Right,
                        ],
                BoundConditional conditional when
                    conditional.Condition.Type == PrimitiveType.Bool
                    && conditional.WhenTrue.Type
                        == conditional.WhenFalse.Type
                    && conditional.Type == conditional.WhenTrue.Type
                    && PrimitiveTypeFacts.IsBuiltInScalar(
                        conditional.Type) => [
                    conditional.Condition,
                    conditional.WhenTrue,
                    conditional.WhenFalse,
                ],
                _ => throw Unsupported(
                    $"General predicate expression '{expression.GetType().Name}' is outside the current graph producer subset."),
            };
        }

        void AddInitializedMatchGraph(
            int parentNodeRow, uint childOrdinal,
            PrimitiveType subjectType, uint readRootRow,
            ref int rootNodeRow)
        {
            var matchNodeRow = checked(predicateNodes.Count + 1);
            predicateNodes.Add(new TrRmPredicateNodeRow(0x34,
                checked((uint)TypeSpecRowFor(PrimitiveType.Bool)), 0, 0));
            if (parentNodeRow == 0)
            {
                rootNodeRow = matchNodeRow;
            }
            else
            {
                predicateChildEdges.Add(new TrRmPredicateChildEdgeRow(
                    parentNodeRow, childOrdinal, matchNodeRow));
            }

            var subjectNodeRow = checked((uint)predicateNodes.Count + 1);
            var subjectOperandFirstRow = checked(
                (uint)predicateNodeOperands.Count + 1);
            predicateNodeOperands.Add(
                new TrRmPredicateNodeOperandRow(1, readRootRow));
            predicateNodes.Add(new TrRmPredicateNodeRow(0x10,
                checked((uint)TypeSpecRowFor(subjectType)),
                subjectOperandFirstRow, 1));

            var initializedPatternNodeRow
                = checked((uint)predicateNodes.Count + 1);
            predicateNodes.Add(new TrRmPredicateNodeRow(0x41,
                checked((uint)TypeSpecRowFor(PrimitiveType.Bool)), 0, 0));
            predicateChildEdges.Add(new TrRmPredicateChildEdgeRow(
                matchNodeRow, 0, checked((int)subjectNodeRow)));
            predicateChildEdges.Add(new TrRmPredicateChildEdgeRow(
                matchNodeRow, 1,
                checked((int)initializedPatternNodeRow)));
        }

        void AddInferredBindingMatchGraph(
            int parentNodeRow, uint childOrdinal,
            PrimitiveType optionalType, uint inputRootRow,
            uint patternNodeKind, uint captureRow,
            ref int rootNodeRow)
        {
            var matchNodeRow = checked(predicateNodes.Count + 1);
            predicateNodes.Add(new TrRmPredicateNodeRow(0x34,
                checked((uint)TypeSpecRowFor(PrimitiveType.Bool)), 0, 0));
            if (parentNodeRow == 0)
            {
                rootNodeRow = matchNodeRow;
            }
            else
            {
                predicateChildEdges.Add(new TrRmPredicateChildEdgeRow(
                    parentNodeRow, childOrdinal, matchNodeRow));
            }

            var subjectNodeRow = checked((uint)predicateNodes.Count + 1);
            var subjectOperandFirstRow = checked(
                (uint)predicateNodeOperands.Count + 1);
            predicateNodeOperands.Add(
                new TrRmPredicateNodeOperandRow(1, inputRootRow));
            predicateNodes.Add(new TrRmPredicateNodeRow(0x11,
                checked((uint)TypeSpecRowFor(optionalType)),
                subjectOperandFirstRow, 1));

            var bindingPatternNodeRow
                = checked((uint)predicateNodes.Count + 1);
            var patternOperandFirstRow = checked(
                (uint)predicateNodeOperands.Count + 1);
            predicateNodeOperands.Add(
                new TrRmPredicateNodeOperandRow(3, captureRow));
            predicateNodes.Add(new TrRmPredicateNodeRow(patternNodeKind,
                checked((uint)TypeSpecRowFor(PrimitiveType.Bool)),
                patternOperandFirstRow, 1));
            predicateChildEdges.Add(new TrRmPredicateChildEdgeRow(
                matchNodeRow, 0, checked((int)subjectNodeRow)));
            predicateChildEdges.Add(new TrRmPredicateChildEdgeRow(
                matchNodeRow, 1, checked((int)bindingPatternNodeRow)));
        }

        void AddNullMatchGraph(int parentNodeRow, uint childOrdinal,
            PrimitiveType optionalType, uint inputRootRow,
            ref int rootNodeRow)
        {
            var matchNodeRow = checked(predicateNodes.Count + 1);
            predicateNodes.Add(new TrRmPredicateNodeRow(0x34,
                checked((uint)TypeSpecRowFor(PrimitiveType.Bool)), 0, 0));
            if (parentNodeRow == 0)
            {
                rootNodeRow = matchNodeRow;
            }
            else
            {
                predicateChildEdges.Add(new TrRmPredicateChildEdgeRow(
                    parentNodeRow, childOrdinal, matchNodeRow));
            }

            var subjectNodeRow = checked((uint)predicateNodes.Count + 1);
            var subjectOperandFirstRow = checked(
                (uint)predicateNodeOperands.Count + 1);
            predicateNodeOperands.Add(
                new TrRmPredicateNodeOperandRow(1, inputRootRow));
            predicateNodes.Add(new TrRmPredicateNodeRow(0x11,
                checked((uint)TypeSpecRowFor(optionalType)),
                subjectOperandFirstRow, 1));

            var nullPatternNodeRow = checked((uint)predicateNodes.Count + 1);
            predicateNodes.Add(new TrRmPredicateNodeRow(0x42,
                checked((uint)TypeSpecRowFor(PrimitiveType.Bool)), 0, 0));
            predicateChildEdges.Add(new TrRmPredicateChildEdgeRow(
                matchNodeRow, 0, checked((int)subjectNodeRow)));
            predicateChildEdges.Add(new TrRmPredicateChildEdgeRow(
                matchNodeRow, 1, checked((int)nullPatternNodeRow)));
        }

        void AddTypeTestMatchGraph(int parentNodeRow,
            uint childOrdinal, PrimitiveType subjectType,
            uint inputRootRow, uint typeUseRow,
            ref int rootNodeRow)
        {
            var matchNodeRow = checked(predicateNodes.Count + 1);
            predicateNodes.Add(new TrRmPredicateNodeRow(0x34,
                checked((uint)TypeSpecRowFor(PrimitiveType.Bool)), 0, 0));
            if (parentNodeRow == 0)
            {
                rootNodeRow = matchNodeRow;
            }
            else
            {
                predicateChildEdges.Add(new TrRmPredicateChildEdgeRow(
                    parentNodeRow, childOrdinal, matchNodeRow));
            }

            var subjectNodeRow = checked((uint)predicateNodes.Count + 1);
            var subjectOperandFirstRow = checked(
                (uint)predicateNodeOperands.Count + 1);
            predicateNodeOperands.Add(
                new TrRmPredicateNodeOperandRow(1, inputRootRow));
            predicateNodes.Add(new TrRmPredicateNodeRow(0x11,
                checked((uint)TypeSpecRowFor(subjectType)),
                subjectOperandFirstRow, 1));

            var typeTestPatternNodeRow
                = checked((uint)predicateNodes.Count + 1);
            var patternOperandFirstRow = checked(
                (uint)predicateNodeOperands.Count + 1);
            predicateNodeOperands.Add(
                new TrRmPredicateNodeOperandRow(6, typeUseRow));
            predicateNodes.Add(new TrRmPredicateNodeRow(0x44,
                checked((uint)TypeSpecRowFor(PrimitiveType.Bool)),
                patternOperandFirstRow, 1));
            predicateChildEdges.Add(new TrRmPredicateChildEdgeRow(
                matchNodeRow, 0, checked((int)subjectNodeRow)));
            predicateChildEdges.Add(new TrRmPredicateChildEdgeRow(
                matchNodeRow, 1, checked((int)typeTestPatternNodeRow)));
        }

        void AddTypedValueBindMatchGraph(int parentNodeRow,
            uint childOrdinal, PrimitiveType subjectType,
            uint inputRootRow, uint typeUseRow, uint captureRow,
            ref int rootNodeRow)
        {
            var matchNodeRow = checked(predicateNodes.Count + 1);
            predicateNodes.Add(new TrRmPredicateNodeRow(0x34,
                checked((uint)TypeSpecRowFor(PrimitiveType.Bool)), 0, 0));
            if (parentNodeRow == 0)
            {
                rootNodeRow = matchNodeRow;
            }
            else
            {
                predicateChildEdges.Add(new TrRmPredicateChildEdgeRow(
                    parentNodeRow, childOrdinal, matchNodeRow));
            }

            var subjectNodeRow = checked((uint)predicateNodes.Count + 1);
            var subjectOperandFirstRow = checked(
                (uint)predicateNodeOperands.Count + 1);
            predicateNodeOperands.Add(
                new TrRmPredicateNodeOperandRow(1, inputRootRow));
            predicateNodes.Add(new TrRmPredicateNodeRow(0x11,
                checked((uint)TypeSpecRowFor(subjectType)),
                subjectOperandFirstRow, 1));

            var typedValuePatternNodeRow
                = checked((uint)predicateNodes.Count + 1);
            var patternOperandFirstRow = checked(
                (uint)predicateNodeOperands.Count + 1);
            predicateNodeOperands.Add(
                new TrRmPredicateNodeOperandRow(6, typeUseRow));
            predicateNodeOperands.Add(
                new TrRmPredicateNodeOperandRow(3, captureRow));
            predicateNodes.Add(new TrRmPredicateNodeRow(0x45,
                checked((uint)TypeSpecRowFor(PrimitiveType.Bool)),
                patternOperandFirstRow, 2));
            predicateChildEdges.Add(new TrRmPredicateChildEdgeRow(
                matchNodeRow, 0, checked((int)subjectNodeRow)));
            predicateChildEdges.Add(new TrRmPredicateChildEdgeRow(
                matchNodeRow, 1,
                checked((int)typedValuePatternNodeRow)));
        }

        void AddReferencePatternMatchGraph(int parentNodeRow,
            uint childOrdinal, PrimitiveType subjectType,
            uint inputRootRow, uint typePatternRow, uint captureRow,
            ref int rootNodeRow)
        {
            var matchNodeRow = checked(predicateNodes.Count + 1);
            predicateNodes.Add(new TrRmPredicateNodeRow(0x34,
                checked((uint)TypeSpecRowFor(PrimitiveType.Bool)), 0, 0));
            if (parentNodeRow == 0)
            {
                rootNodeRow = matchNodeRow;
            }
            else
            {
                predicateChildEdges.Add(new TrRmPredicateChildEdgeRow(
                    parentNodeRow, childOrdinal, matchNodeRow));
            }

            var subjectNodeRow = checked((uint)predicateNodes.Count + 1);
            var subjectOperandFirstRow = checked(
                (uint)predicateNodeOperands.Count + 1);
            predicateNodeOperands.Add(
                new TrRmPredicateNodeOperandRow(1, inputRootRow));
            predicateNodes.Add(new TrRmPredicateNodeRow(0x11,
                checked((uint)TypeSpecRowFor(subjectType)),
                subjectOperandFirstRow, 1));

            var referencePatternNodeRow
                = checked((uint)predicateNodes.Count + 1);
            var patternOperandFirstRow = checked(
                (uint)predicateNodeOperands.Count + 1);
            predicateNodeOperands.Add(
                new TrRmPredicateNodeOperandRow(0x0B, typePatternRow));
            var patternOperandCount = 1u;
            if (captureRow != 0)
            {
                predicateNodeOperands.Add(
                    new TrRmPredicateNodeOperandRow(3, captureRow));
                patternOperandCount++;
            }
            predicateNodes.Add(new TrRmPredicateNodeRow(0x48,
                checked((uint)TypeSpecRowFor(PrimitiveType.Bool)),
                patternOperandFirstRow, patternOperandCount));
            predicateChildEdges.Add(new TrRmPredicateChildEdgeRow(
                matchNodeRow, 0, checked((int)subjectNodeRow)));
            predicateChildEdges.Add(new TrRmPredicateChildEdgeRow(
                matchNodeRow, 1,
                checked((int)referencePatternNodeRow)));
        }

        void AddConstantPatternMatchGraph(int parentNodeRow,
            uint childOrdinal, PrimitiveType subjectType,
            uint inputRootRow, BoundConstant constant,
            ref int rootNodeRow)
        {
            var literal
                = predicateFrozenScalarValues.AddFrozenValueNode(constant);
            var matchNodeRow = checked(predicateNodes.Count + 1);
            predicateNodes.Add(new TrRmPredicateNodeRow(0x34,
                checked((uint)TypeSpecRowFor(PrimitiveType.Bool)), 0, 0));
            if (parentNodeRow == 0)
            {
                rootNodeRow = matchNodeRow;
            }
            else
            {
                predicateChildEdges.Add(new TrRmPredicateChildEdgeRow(
                    parentNodeRow, childOrdinal, matchNodeRow));
            }

            var subjectNodeRow = checked((uint)predicateNodes.Count + 1);
            var subjectOperandFirstRow = checked(
                (uint)predicateNodeOperands.Count + 1);
            predicateNodeOperands.Add(
                new TrRmPredicateNodeOperandRow(1, inputRootRow));
            predicateNodes.Add(new TrRmPredicateNodeRow(0x11,
                checked((uint)TypeSpecRowFor(subjectType)),
                subjectOperandFirstRow, 1));

            var constantPatternNodeRow
                = checked((uint)predicateNodes.Count + 1);
            var patternOperandFirstRow = checked(
                (uint)predicateNodeOperands.Count + 1);
            predicateNodeOperands.Add(new TrRmPredicateNodeOperandRow(2,
                literal.FrozenValueNodeRow));
            predicateNodes.Add(new TrRmPredicateNodeRow(0x43,
                checked((uint)TypeSpecRowFor(PrimitiveType.Bool)),
                patternOperandFirstRow, 1));
            predicateChildEdges.Add(new TrRmPredicateChildEdgeRow(
                matchNodeRow, 0, checked((int)subjectNodeRow)));
            predicateChildEdges.Add(new TrRmPredicateChildEdgeRow(
                matchNodeRow, 1, checked((int)constantPatternNodeRow)));
        }

        int AddPatternGraphNode(int parentNodeRow, uint childOrdinal,
            uint nodeKind, params TrRmPredicateNodeOperandRow[] nodeOperands)
        {
            var nodeRow = checked(predicateNodes.Count + 1);
            var operandFirstRow = nodeOperands.Length == 0
                ? 0u : checked((uint)predicateNodeOperands.Count + 1);
            predicateNodeOperands.AddRange(nodeOperands);
            predicateNodes.Add(new TrRmPredicateNodeRow(nodeKind,
                checked((uint)TypeSpecRowFor(PrimitiveType.Bool)),
                operandFirstRow, checked((uint)nodeOperands.Length)));
            predicateChildEdges.Add(new TrRmPredicateChildEdgeRow(
                parentNodeRow, childOrdinal, nodeRow));
            return nodeRow;
        }

        void BuildPatternCompositionNode(
            BoundFunction function, BoundExpression pattern,
            BoundRead observation, int parentNodeRow, uint childOrdinal,
            Dictionary<(byte Kind, int Identity), uint> rootRowsByIdentity,
            Dictionary<int, (uint CaptureRow, uint Ordinal,
                PrimitiveType Type)> captureRowsByVariable,
            BoundDeclarationContractPhase declarationPhase)
        {
            bool IsSameObservation(BoundExpression value)
                => value is BoundRead read
                    && read.Variable == observation.Variable
                    && read.Type == observation.Type;

            if (pattern is BoundUnary {
                    IsPatternComposition: true,
                    Operator: TokenKind.Bang,
                    PatternObservation: BoundRead patternObservation,
                } notPattern)
            {
                if (!IsSameObservation(patternObservation))
                {
                    throw Unsupported(
                        "A nested PatternNot must retain its parent's input observation.");
                }

                var notNode = AddPatternGraphNode(parentNodeRow,
                    childOrdinal, 0x4C);
                BuildPatternCompositionNode(function, notPattern.Operand,
                    observation, notNode, 0, rootRowsByIdentity,
                    captureRowsByVariable, declarationPhase);
                return;
            }

            if (pattern is BoundBinary {
                    IsPatternComposition: true,
                    Operator: TokenKind.AmpersandAmpersand
                        or TokenKind.PipePipe,
                    PatternObservation: BoundRead patternObservation,
                } binaryPattern)
            {
                if (!IsSameObservation(patternObservation))
                {
                    throw Unsupported(
                        "A nested pattern composition must retain its parent's input observation.");
                }

                var nodeKind = binaryPattern.Operator
                    == TokenKind.AmpersandAmpersand ? 0x4D : 0x4E;
                var compositionNode = AddPatternGraphNode(parentNodeRow,
                    childOrdinal, checked((uint)nodeKind));
                BuildPatternCompositionNode(function, binaryPattern.Left,
                    observation, compositionNode, 0, rootRowsByIdentity,
                    captureRowsByVariable, declarationPhase);
                BuildPatternCompositionNode(function, binaryPattern.Right,
                    observation, compositionNode, 1, rootRowsByIdentity,
                    captureRowsByVariable, declarationPhase);
                return;
            }

            if (observation.Variable < 0
                || observation.Variable >= function.ParameterCount
                || function.Variables[observation.Variable].Type
                    != observation.Type
                || function.Variables[observation.Variable].IsOut
                || function.Variables[observation.Variable].IsInit
                || function.Variables[observation.Variable].IsMutable
                || !rootRowsByIdentity.TryGetValue(
                    (2, observation.Variable), out _))
            {
                throw Unsupported(
                    "A composed declaration pattern requires one immutable input-formal observation.");
            }

            switch (pattern)
            {
                case BoundWildcardPattern wildcardPattern
                    when IsSameObservation(
                        wildcardPattern.Observation):
                    _ = AddPatternGraphNode(parentNodeRow, childOrdinal,
                        0x40);
                    return;

                case BoundTypeTestPattern typeTest
                    when IsSameObservation(typeTest.Value)
                        && declarationPhase is (
                            BoundDeclarationContractPhase.Precondition
                            or BoundDeclarationContractPhase
                                .NormalPostcondition)
                        && (PrimitiveTypeFacts.IsBuiltInScalar(
                                observation.Type)
                            || TryGetOptionalBuiltInScalarType(
                                observation.Type,
                                program.CheckedReferenceTypes, out _))
                        && generalPredicateScalarTypeRows.ContainsKey(
                            typeTest.TestedType):
                    _ = AddPatternGraphNode(parentNodeRow, childOrdinal,
                        0x44, new TrRmPredicateNodeOperandRow(6,
                            PredicateTypeUseRowFor(typeTest.TestedType)));
                    return;

                case BoundTypedValuePattern typedValuePattern
                    when IsSameObservation(typedValuePattern.Value)
                        && declarationPhase is (
                            BoundDeclarationContractPhase.Precondition
                            or BoundDeclarationContractPhase
                                .NormalPostcondition)
                        && (PrimitiveTypeFacts.IsBuiltInScalar(
                                observation.Type)
                            || TryGetOptionalBuiltInScalarType(
                                observation.Type,
                                program.CheckedReferenceTypes, out _))
                        && generalPredicateScalarTypeRows.ContainsKey(
                            typedValuePattern.TestedType)
                        && captureRowsByVariable.TryGetValue(
                            typedValuePattern.Variable,
                            out var valueCapture)
                        && valueCapture.Type == typedValuePattern.TestedType:
                    _ = AddPatternGraphNode(parentNodeRow, childOrdinal,
                        0x45,
                        new TrRmPredicateNodeOperandRow(6,
                            PredicateTypeUseRowFor(
                                typedValuePattern.TestedType)),
                        new TrRmPredicateNodeOperandRow(3,
                            valueCapture.CaptureRow));
                    return;

                case BoundReferenceTypeTestPattern referencePattern
                    when IsSameObservation(referencePattern.Value)
                        && declarationPhase is (
                            BoundDeclarationContractPhase.Precondition
                            or BoundDeclarationContractPhase
                                .NormalPostcondition)
                        && CheckedReferenceTypeContract
                            .IsSupportedReferencePatternType(
                                observation.Type,
                                program.CheckedReferenceTypes)
                        && referencePattern.Variable >= -1:
                    var referenceOperands = new List<
                        TrRmPredicateNodeOperandRow> {
                        new(0x0B, ReferenceTypePatternRowFor(
                            referencePattern.TestedType)),
                    };
                    if (referencePattern.Variable >= 0)
                    {
                        if (!captureRowsByVariable.TryGetValue(
                                referencePattern.Variable,
                                out var referenceCapture)
                            || referenceCapture.Type
                                != referencePattern.TestedType)
                        {
                            throw Unsupported(
                                "A composed ReferencePattern has no matching role-2 capture.");
                        }

                        referenceOperands.Add(
                            new TrRmPredicateNodeOperandRow(3,
                                referenceCapture.CaptureRow));
                    }

                    _ = AddPatternGraphNode(parentNodeRow, childOrdinal,
                        0x48, [.. referenceOperands]);
                    return;

                case BoundIsNull nullTest
                    when declarationPhase
                        == BoundDeclarationContractPhase
                            .NormalPostcondition
                        && IsSameObservation(nullTest.Value)
                        && TryGetOptionalBuiltInScalarType(
                            observation.Type,
                            program.CheckedReferenceTypes, out _):
                    _ = AddPatternGraphNode(parentNodeRow, childOrdinal,
                        0x42);
                    return;

                case BoundIsSome somePattern
                    when declarationPhase
                        == BoundDeclarationContractPhase
                            .NormalPostcondition
                        && IsSameObservation(somePattern.Value)
                        && TryGetOptionalBuiltInScalarType(
                            observation.Type, program.CheckedReferenceTypes,
                            out var payloadType)
                        && captureRowsByVariable.TryGetValue(
                            somePattern.Variable, out var someCapture)
                        && someCapture.Type == payloadType:
                    _ = AddPatternGraphNode(parentNodeRow, childOrdinal,
                        0x46, new TrRmPredicateNodeOperandRow(3,
                            someCapture.CaptureRow));
                    return;

                case BoundIsVariablePattern variablePattern
                    when declarationPhase
                        == BoundDeclarationContractPhase
                            .NormalPostcondition
                        && IsSameObservation(variablePattern.Value)
                        && TryGetOptionalBuiltInScalarType(
                            observation.Type, program.CheckedReferenceTypes,
                            out var variablePayloadType)
                        && captureRowsByVariable.TryGetValue(
                            variablePattern.Variable,
                            out var variableCapture)
                        && variableCapture.Type
                            == (variablePattern.IncludeNull
                                ? observation.Type : variablePayloadType):
                    _ = AddPatternGraphNode(parentNodeRow, childOrdinal,
                        variablePattern.IncludeNull ? 0x47u : 0x46u,
                        new TrRmPredicateNodeOperandRow(3,
                            variableCapture.CaptureRow));
                    return;

                case BoundBinary {
                        IsConstantPatternTest: true,
                        Operator: TokenKind.EqualsEquals,
                        Left: BoundRead constantSubject,
                        Right: BoundConstant constant,
                    } when IsSameObservation(constantSubject)
                        && declarationPhase is (
                            BoundDeclarationContractPhase.Precondition
                            or BoundDeclarationContractPhase
                                .NormalPostcondition)
                        && PrimitiveTypeFacts.IsBuiltInScalar(
                            observation.Type)
                        && constant.Type == observation.Type
                        && constant.RepresentationType == observation.Type
                        && constant.Bits.IsCanonicalFor(observation.Type):
                    var frozenValue
                        = predicateFrozenScalarValues
                            .AddFrozenValueNode(constant);
                    _ = AddPatternGraphNode(parentNodeRow, childOrdinal,
                        0x43, new TrRmPredicateNodeOperandRow(2,
                            frozenValue.FrozenValueNodeRow));
                    return;
            }

            throw Unsupported(
                "A composed declaration pattern contains an unsupported child pattern.");
        }

        int AddPatternCompositionMatchGraph(
            BoundFunction function, BoundExpression expression,
            int parentNodeRow, uint childOrdinal,
            Dictionary<(byte Kind, int Identity), uint> rootRowsByIdentity,
            Dictionary<int, (uint CaptureRow, uint Ordinal,
                PrimitiveType Type)> captureRowsByVariable,
            BoundDeclarationContractPhase? declarationPhase)
        {
            var observation = expression switch {
                BoundUnary {
                    IsPatternComposition: true,
                    PatternObservation: BoundRead unaryRead,
                } => unaryRead,
                BoundBinary {
                    IsPatternComposition: true,
                    PatternObservation: BoundRead binaryRead,
                } => binaryRead,
                _ => throw Unsupported(
                    "A pattern composition has no retained input observation."),
            };
            if (declarationPhase is not (
                    BoundDeclarationContractPhase.Precondition
                    or BoundDeclarationContractPhase.NormalPostcondition)
                || observation.Variable < 0
                || observation.Variable >= function.ParameterCount
                || function.Variables[observation.Variable].Type
                    != observation.Type
                || function.Variables[observation.Variable].IsOut
                || function.Variables[observation.Variable].IsInit
                || function.Variables[observation.Variable].IsMutable
                || (!PrimitiveTypeFacts.IsBuiltInScalar(
                        observation.Type)
                    && !TryGetOptionalBuiltInScalarType(
                        observation.Type, program.CheckedReferenceTypes,
                        out _)
                    && !CheckedReferenceTypeContract
                        .IsSupportedReferencePatternType(
                            observation.Type,
                            program.CheckedReferenceTypes))
                || !rootRowsByIdentity.TryGetValue(
                    (2, observation.Variable), out var inputRootRow))
            {
                throw Unsupported(
                    "A composed declaration pattern requires its immutable phase-1/2 input formal.");
            }

            var matchNodeRow = checked(predicateNodes.Count + 1);
            predicateNodes.Add(new TrRmPredicateNodeRow(0x34,
                checked((uint)TypeSpecRowFor(PrimitiveType.Bool)), 0, 0));
            if (parentNodeRow != 0)
            {
                predicateChildEdges.Add(new TrRmPredicateChildEdgeRow(
                    parentNodeRow, childOrdinal, matchNodeRow));
            }

            var subjectNodeRow = checked((uint)predicateNodes.Count + 1);
            var subjectOperandFirstRow = checked(
                (uint)predicateNodeOperands.Count + 1);
            predicateNodeOperands.Add(
                new TrRmPredicateNodeOperandRow(1, inputRootRow));
            predicateNodes.Add(new TrRmPredicateNodeRow(0x10,
                checked((uint)TypeSpecRowFor(observation.Type)),
                subjectOperandFirstRow, 1));
            predicateChildEdges.Add(new TrRmPredicateChildEdgeRow(
                matchNodeRow, 0, checked((int)subjectNodeRow)));
            BuildPatternCompositionNode(function, expression, observation,
                matchNodeRow, 1, rootRowsByIdentity,
                captureRowsByVariable, declarationPhase.Value);
            return matchNodeRow;
        }

        void AddCapturedValueLoadGraph(
            int parentNodeRow, uint childOrdinal,
            PrimitiveType capturedType, uint captureRow,
            ref int rootNodeRow)
        {
            var loadNodeRow = checked(predicateNodes.Count + 1);
            predicateNodes.Add(new TrRmPredicateNodeRow(0x13,
                checked((uint)TypeSpecRowFor(capturedType)), 0, 0));
            if (parentNodeRow == 0)
            {
                rootNodeRow = loadNodeRow;
            }
            else
            {
                predicateChildEdges.Add(new TrRmPredicateChildEdgeRow(
                    parentNodeRow, childOrdinal, loadNodeRow));
            }

            var capturePlaceNodeRow
                = checked((uint)predicateNodes.Count + 1);
            var operandFirstRow = checked(
                (uint)predicateNodeOperands.Count + 1);
            predicateNodeOperands.Add(
                new TrRmPredicateNodeOperandRow(3, captureRow));
            predicateNodes.Add(new TrRmPredicateNodeRow(0x12,
                checked((uint)TypeSpecRowFor(capturedType)),
                operandFirstRow, 1));
            predicateChildEdges.Add(new TrRmPredicateChildEdgeRow(
                loadNodeRow, 0, checked((int)capturePlaceNodeRow)));
        }

        int BuildGeneralPredicateNode(
            BoundFunction function, BoundExpression expression,
            Dictionary<(byte Kind, int Identity), uint>
                rootRowsByIdentity,
            Dictionary<int, (uint CaptureRow, uint Ordinal,
                PrimitiveType Type)> captureRowsByVariable,
            BoundDeclarationContractPhase? declarationPhase)
        {
            var stack = new Stack<(BoundExpression Expression,
                int ParentNodeRow, uint ChildOrdinal, bool PostOrder,
                int NodeRow, uint NodeKind,
                uint OperandFirstRow, uint OperandCount)>();
            stack.Push((expression, 0, 0, false, 0, 0, 0, 0));
            var rootNodeRow = 0;
            while (stack.Count != 0)
            {
                var frame = stack.Pop();
                if (frame.PostOrder)
                {
                    predicateNodes[frame.NodeRow - 1]
                        = new TrRmPredicateNodeRow(
                            frame.NodeKind,
                            checked((uint)TypeSpecRowFor(
                                frame.Expression.Type)),
                            frame.OperandFirstRow,
                            frame.OperandCount);
                    continue;
                }

                if (frame.Expression is BoundBinary {
                        IsConstantPatternTest: true,
                    } constantPatternTest)
                {
                    if (declarationPhase
                            is not (BoundDeclarationContractPhase.Precondition
                                or BoundDeclarationContractPhase
                                    .NormalPostcondition)
                        || constantPatternTest.Operator
                            != TokenKind.EqualsEquals
                        || constantPatternTest.Type != PrimitiveType.Bool
                        || constantPatternTest.Left
                            is not BoundRead subjectRead
                        || constantPatternTest.Right
                            is not BoundConstant constant
                        || subjectRead.Variable < 0
                        || subjectRead.Variable >= function.ParameterCount
                        || function.Variables[subjectRead.Variable].IsOut
                        || function.Variables[subjectRead.Variable].IsInit
                        || function.Variables[subjectRead.Variable].IsMutable
                        || function.Variables[subjectRead.Variable].Type
                            != subjectRead.Type
                        || !PrimitiveTypeFacts.IsBuiltInScalar(
                            subjectRead.Type)
                        || constant.Type != subjectRead.Type
                        || constant.RepresentationType
                            != subjectRead.Type
                        || !constant.Bits.IsCanonicalFor(constant.Type)
                        || !rootRowsByIdentity.TryGetValue(
                            (2, subjectRead.Variable),
                            out var constantSubjectRootRow))
                    {
                        throw Unsupported(
                            "A scalar constant-pattern graph requires an exact typed input formal and canonical literal.");
                    }

                    AddConstantPatternMatchGraph(frame.ParentNodeRow,
                        frame.ChildOrdinal, subjectRead.Type,
                        constantSubjectRootRow, constant,
                        ref rootNodeRow);
                    continue;
                }

                if (frame.Expression is BoundUnary {
                        IsPatternComposition: true,
                    } or BoundBinary {
                        IsPatternComposition: true,
                    })
                {
                    var matchNodeRow = AddPatternCompositionMatchGraph(
                        function, frame.Expression, frame.ParentNodeRow,
                        frame.ChildOrdinal, rootRowsByIdentity,
                        captureRowsByVariable, declarationPhase);
                    if (frame.ParentNodeRow == 0)
                    {
                        rootNodeRow = matchNodeRow;
                    }
                    continue;
                }

                if (frame.Expression is BoundWildcardPattern wildcardPattern)
                {
                    if (declarationPhase is not (
                            BoundDeclarationContractPhase.Precondition
                            or BoundDeclarationContractPhase
                                .NormalPostcondition)
                        || wildcardPattern.Observation
                            is not BoundRead wildcardRead
                        || wildcardRead.Variable < 0
                        || wildcardRead.Variable >= function.ParameterCount
                        || function.Variables[wildcardRead.Variable].Type
                            != wildcardRead.Type
                        || function.Variables[wildcardRead.Variable].IsOut
                        || function.Variables[wildcardRead.Variable].IsInit
                        || function.Variables[wildcardRead.Variable].IsMutable
                        || (!PrimitiveTypeFacts.IsBuiltInScalar(
                                wildcardRead.Type)
                            && !TryGetOptionalBuiltInScalarType(
                                wildcardRead.Type,
                                program.CheckedReferenceTypes, out _)
                            && !CheckedReferenceTypeContract
                                .IsSupportedReferencePatternType(
                                    wildcardRead.Type,
                                    program.CheckedReferenceTypes))
                        || !rootRowsByIdentity.TryGetValue(
                            (2, wildcardRead.Variable),
                            out var wildcardInputRootRow))
                    {
                        throw Unsupported(
                            "A wildcard graph requires one supported immutable input-formal observation.");
                    }

                    var matchNodeRow = checked(predicateNodes.Count + 1);
                    predicateNodes.Add(new TrRmPredicateNodeRow(0x34,
                        checked((uint)TypeSpecRowFor(PrimitiveType.Bool)),
                        0, 0));
                    if (frame.ParentNodeRow == 0)
                    {
                        rootNodeRow = matchNodeRow;
                    }
                    else
                    {
                        predicateChildEdges.Add(
                            new TrRmPredicateChildEdgeRow(
                                frame.ParentNodeRow, frame.ChildOrdinal,
                                matchNodeRow));
                    }

                    var subjectNodeRow
                        = checked((uint)predicateNodes.Count + 1);
                    var subjectOperandFirstRow = checked(
                        (uint)predicateNodeOperands.Count + 1);
                    predicateNodeOperands.Add(
                        new TrRmPredicateNodeOperandRow(1,
                            wildcardInputRootRow));
                    predicateNodes.Add(new TrRmPredicateNodeRow(0x10,
                        checked((uint)TypeSpecRowFor(wildcardRead.Type)),
                        subjectOperandFirstRow, 1));
                    var wildcardNodeRow
                        = checked((uint)predicateNodes.Count + 1);
                    predicateNodes.Add(new TrRmPredicateNodeRow(0x40,
                        checked((uint)TypeSpecRowFor(PrimitiveType.Bool)),
                        0, 0));
                    predicateChildEdges.Add(
                        new TrRmPredicateChildEdgeRow(matchNodeRow, 0,
                            checked((int)subjectNodeRow)));
                    predicateChildEdges.Add(
                        new TrRmPredicateChildEdgeRow(matchNodeRow, 1,
                            checked((int)wildcardNodeRow)));
                    continue;
                }

                if (frame.Expression is BoundIsNull nullTest)
                {
                    if (declarationPhase
                            != BoundDeclarationContractPhase
                                .NormalPostcondition
                        || nullTest.Value is not BoundRead optionalRead
                        || !rootRowsByIdentity.TryGetValue(
                            (2, optionalRead.Variable),
                            out var nullInputRootRow))
                    {
                        throw Unsupported(
                            "A Null pattern graph requires its phase-2 optional input value.");
                    }

                    AddNullMatchGraph(frame.ParentNodeRow,
                        frame.ChildOrdinal, optionalRead.Type,
                        nullInputRootRow, ref rootNodeRow);
                    continue;
                }

                if (frame.Expression
                    is BoundTypeTestPattern typeTest)
                {
                    if (declarationPhase is not (
                            BoundDeclarationContractPhase.Precondition
                            or BoundDeclarationContractPhase
                                .NormalPostcondition)
                        || typeTest.Value is not BoundRead optionalRead
                        || optionalRead.Variable < 0
                        || optionalRead.Variable >= function.ParameterCount
                        || function.Variables[optionalRead.Variable].Type
                            != optionalRead.Type
                        || function.Variables[optionalRead.Variable].IsOut
                        || function.Variables[optionalRead.Variable].IsInit
                        || function.Variables[optionalRead.Variable]
                            .IsMutable
                        || (!PrimitiveTypeFacts.IsBuiltInScalar(
                                optionalRead.Type)
                            && !TryGetOptionalBuiltInScalarType(
                                optionalRead.Type,
                                program.CheckedReferenceTypes, out _))
                        || !generalPredicateScalarTypeRows.ContainsKey(
                            typeTest.TestedType)
                        || !rootRowsByIdentity.TryGetValue(
                            (2, optionalRead.Variable),
                            out var typeTestInputRootRow))
                    {
                        throw Unsupported(
                            "A TypeTest graph requires a scalar input formal and supported scalar TypeUse.");
                    }

                    AddTypeTestMatchGraph(frame.ParentNodeRow,
                        frame.ChildOrdinal, optionalRead.Type,
                        typeTestInputRootRow,
                        PredicateTypeUseRowFor(typeTest.TestedType),
                        ref rootNodeRow);
                    continue;
                }

                if (frame.Expression
                    is BoundTypedValuePattern typedValuePattern)
                {
                    if (declarationPhase is not (
                            BoundDeclarationContractPhase.Precondition
                            or BoundDeclarationContractPhase
                                .NormalPostcondition)
                        || typedValuePattern.Value
                            is not BoundRead typedValueRead
                        || typedValueRead.Variable < 0
                        || typedValueRead.Variable >= function.ParameterCount
                        || function.Variables[typedValueRead.Variable].Type
                            != typedValueRead.Type
                        || function.Variables[typedValueRead.Variable].IsOut
                        || function.Variables[typedValueRead.Variable].IsInit
                        || function.Variables[typedValueRead.Variable]
                            .IsMutable
                        || (!PrimitiveTypeFacts.IsBuiltInScalar(
                                typedValueRead.Type)
                            && !TryGetOptionalBuiltInScalarType(
                                typedValueRead.Type,
                                program.CheckedReferenceTypes, out _))
                        || !generalPredicateScalarTypeRows.ContainsKey(
                            typedValuePattern.TestedType)
                        || !captureRowsByVariable.TryGetValue(
                            typedValuePattern.Variable,
                            out var typedValueCapture)
                        || typedValueCapture.Type
                            != typedValuePattern.TestedType
                        || !rootRowsByIdentity.TryGetValue(
                            (2, typedValueRead.Variable),
                            out var typedValueInputRootRow))
                    {
                        throw Unsupported(
                            "A TypedValueBind graph requires a scalar input, TypeUse and graph-local value capture.");
                    }

                    AddTypedValueBindMatchGraph(frame.ParentNodeRow,
                        frame.ChildOrdinal, typedValueRead.Type,
                        typedValueInputRootRow,
                        PredicateTypeUseRowFor(
                            typedValuePattern.TestedType),
                        typedValueCapture.CaptureRow, ref rootNodeRow);
                    continue;
                }

                if (frame.Expression
                    is BoundReferenceTypeTestPattern referenceTypeTest)
                {
                    if (declarationPhase is not (
                            BoundDeclarationContractPhase.Precondition
                            or BoundDeclarationContractPhase
                                .NormalPostcondition)
                        || referenceTypeTest.Value
                            is not BoundRead referenceRead
                        || referenceRead.Variable < 0
                        || referenceRead.Variable >= function.ParameterCount
                        || function.Variables[referenceRead.Variable].Type
                            != referenceRead.Type
                        || function.Variables[referenceRead.Variable].IsOut
                        || function.Variables[referenceRead.Variable].IsInit
                        || function.Variables[referenceRead.Variable]
                            .IsMutable
                        || !CheckedReferenceTypeContract
                            .IsSupportedReferencePatternType(
                            referenceRead.Type,
                            program.CheckedReferenceTypes)
                        || !rootRowsByIdentity.TryGetValue(
                            (2, referenceRead.Variable),
                            out var referenceInputRootRow))
                    {
                        throw Unsupported(
                            "A ReferencePattern graph requires its phase-1/2 checked-reference input root.");
                    }

                    if (referenceTypeTest.Variable < -1)
                    {
                        throw Unsupported(
                            "A ReferencePattern graph has an invalid capture index.");
                    }
                    var referenceCaptureRow = 0u;
                    if (referenceTypeTest.Variable >= 0)
                    {
                        if (!captureRowsByVariable.TryGetValue(
                                referenceTypeTest.Variable,
                                out var referenceCapture)
                            || referenceCapture.Type
                                != referenceTypeTest.TestedType)
                        {
                            throw Unsupported(
                                "A ReferencePattern graph has no matching role-2 capture.");
                        }

                        referenceCaptureRow
                            = referenceCapture.CaptureRow;
                    }

                    AddReferencePatternMatchGraph(frame.ParentNodeRow,
                        frame.ChildOrdinal, referenceRead.Type,
                        referenceInputRootRow,
                        ReferenceTypePatternRowFor(
                            referenceTypeTest.TestedType),
                        referenceCaptureRow, ref rootNodeRow);
                    continue;
                }

                if (frame.Expression is BoundIsSome optionalTest)
                {
                    if (declarationPhase
                            != BoundDeclarationContractPhase
                                .NormalPostcondition
                        || optionalTest.Value is not BoundRead optionalRead
                        || !captureRowsByVariable.TryGetValue(
                            optionalTest.Variable,
                            out var someValueBinding)
                        || !rootRowsByIdentity.TryGetValue(
                            (2, optionalRead.Variable),
                            out var optionalInputRootRow)
                        || someValueBinding.Type
                            != function.Variables[
                                optionalTest.Variable].Type)
                    {
                        throw Unsupported(
                            "An InferredNonNullBind graph requires its phase-2 optional input and graph-local value capture.");
                    }

                    AddInferredBindingMatchGraph(frame.ParentNodeRow,
                        frame.ChildOrdinal, optionalRead.Type,
                        optionalInputRootRow, 0x46,
                        someValueBinding.CaptureRow, ref rootNodeRow);
                    continue;
                }

                if (frame.Expression
                    is BoundIsVariablePattern variablePattern)
                {
                    if (declarationPhase
                            != BoundDeclarationContractPhase
                                .NormalPostcondition
                        || variablePattern.Value
                            is not BoundRead optionalRead
                        || !captureRowsByVariable.TryGetValue(
                            variablePattern.Variable,
                            out var nullableValueBinding)
                        || !rootRowsByIdentity.TryGetValue(
                            (2, optionalRead.Variable),
                            out var nullableInputRootRow)
                        || !TryGetOptionalBuiltInScalarType(
                            optionalRead.Type, program.CheckedReferenceTypes,
                            out var payloadType)
                        || nullableValueBinding.Type
                            != (variablePattern.IncludeNull
                                ? optionalRead.Type : payloadType))
                    {
                        throw Unsupported(
                            "An inferred optional binding requires its phase-2 input and graph-local capture.");
                    }

                    AddInferredBindingMatchGraph(frame.ParentNodeRow,
                        frame.ChildOrdinal, optionalRead.Type,
                        nullableInputRootRow,
                        variablePattern.IncludeNull ? 0x47u : 0x46u,
                        nullableValueBinding.CaptureRow, ref rootNodeRow);
                    continue;
                }

                if (frame.Expression is BoundRead capturedRead
                    && captureRowsByVariable.TryGetValue(
                        capturedRead.Variable,
                        out var capturedValueBinding))
                {
                    if (declarationPhase
                            != BoundDeclarationContractPhase
                                .NormalPostcondition
                        || capturedRead.Type != capturedValueBinding.Type)
                    {
                        throw Unsupported(
                            "A graph-local role-1 capture read has an invalid phase or type.");
                    }

                    AddCapturedValueLoadGraph(frame.ParentNodeRow,
                        frame.ChildOrdinal, capturedValueBinding.Type,
                        capturedValueBinding.CaptureRow, ref rootNodeRow);
                    continue;
                }

                if (frame.Expression is BoundIsInitialized initialized)
                {
                    if (declarationPhase
                            != BoundDeclarationContractPhase
                                .NormalPostcondition
                        || initialized.Variable < 0
                        || initialized.Variable >= function.ParameterCount
                        || function.Variables[initialized.Variable].IsOut
                        || function.Variables[initialized.Variable].IsInit
                        || function.Variables[initialized.Variable].IsMutable
                        || !PrimitiveTypeFacts.IsBuiltInScalar(
                            function.Variables[initialized.Variable].Type)
                        || !rootRowsByIdentity.TryGetValue(
                            (2, initialized.Variable),
                            out var valueReadRootRow))
                    {
                        throw Unsupported(
                            "An Initialized pattern graph requires a scalar input formal in a phase-2 clause.");
                    }

                    AddInitializedMatchGraph(frame.ParentNodeRow,
                        frame.ChildOrdinal,
                        function.Variables[initialized.Variable].Type,
                        valueReadRootRow, ref rootNodeRow);
                    continue;
                }

                if (frame.Expression
                    is BoundReferenceInitializationTest
                        referenceInitializationTest)
                {
                    if (declarationPhase
                            != BoundDeclarationContractPhase
                                .NormalPostcondition
                        || referenceInitializationTest.Descriptor
                            is not BoundRead descriptor
                        || descriptor.Variable < 0
                        || descriptor.Variable >= function.ParameterCount
                        || function.Variables[descriptor.Variable].Type
                            != descriptor.Type
                        || !IsStandardInitializationReferenceParameter(
                            function.Variables[descriptor.Variable], program)
                        || !TryGetStandardInitializationReferenceReferentType(
                            descriptor.Type, program.CheckedReferenceTypes,
                            out var referentType)
                        || !PrimitiveTypeFacts.IsBuiltInScalar(referentType)
                        || !rootRowsByIdentity.TryGetValue(
                            (4, descriptor.Variable),
                            out var outputReadRootRow))
                    {
                        throw Unsupported(
                            "An Initialized pattern graph requires a scalar logical output in a phase-2 clause.");
                    }

                    AddInitializedMatchGraph(frame.ParentNodeRow,
                        frame.ChildOrdinal, referentType,
                        outputReadRootRow,
                        ref rootNodeRow);
                    continue;
                }

                var children = GetGeneralPredicateChildren(
                    frame.Expression);
                var nodeKind = frame.Expression switch {
                    BoundRead => 0x11u,
                    BoundContractReturnValue => 0x11u,
                    BoundConstant => 0x14u,
                    BoundUnary {
                        Operator: TokenKind.Bang,
                    } => 0x23u,
                    BoundUnary unary when
                        TryGetGeneralPredicateUnaryOperation(unary,
                            out _, out _) => 0x20u,
                    BoundCast cast when
                        TryGetGeneralPredicateConversion(cast,
                            out _, out _) => 0x22u,
                    BoundConditional => 0x29u,
                    BoundBinary {
                        Type: PrimitiveType.Bool,
                        Left.Type: PrimitiveType.Bool,
                        Right.Type: PrimitiveType.Bool,
                        Operator: TokenKind.Ampersand
                            or TokenKind.Pipe or TokenKind.Caret,
                    } binary => binary.Operator switch {
                        TokenKind.Ampersand => 0x24u,
                        TokenKind.Pipe => 0x25u,
                        TokenKind.Caret => 0x26u,
                        _ => 0u,
                    },
                    BoundBinary {
                        Type: PrimitiveType.Bool,
                        Left.Type: PrimitiveType.Bool,
                        Right.Type: PrimitiveType.Bool,
                        Operator: TokenKind.AmpersandAmpersand
                            or TokenKind.PipePipe,
                    } binary => binary.Operator switch {
                        TokenKind.AmpersandAmpersand => 0x27u,
                        TokenKind.PipePipe => 0x28u,
                        _ => 0u,
                    },
                    BoundBinary binary when
                        TryGetGeneralPredicateBinaryOperation(binary,
                            out _, out _) => 0x21u,
                    _ => 0u,
                };
                if (nodeKind == 0)
                {
                    throw Unsupported(
                        "A GeneralPredicateGraphsDraft clause has an unsupported operation.");
                }

                var nodeRow = checked(predicateNodes.Count + 1);
                predicateNodes.Add(default);
                if (frame.ParentNodeRow == 0)
                {
                    rootNodeRow = nodeRow;
                }
                else
                {
                    predicateChildEdges.Add(
                        new TrRmPredicateChildEdgeRow(
                            frame.ParentNodeRow,
                            frame.ChildOrdinal, nodeRow));
                }

                if (frame.Expression is BoundRead read)
                {
                    var operandFirstRow = checked((uint)(
                        predicateNodeOperands.Count + 1));
                    predicateNodeOperands.Add(
                        new TrRmPredicateNodeOperandRow(1,
                            rootRowsByIdentity[(2, read.Variable)]));
                    predicateNodes[nodeRow - 1]
                        = new TrRmPredicateNodeRow(nodeKind,
                            checked((uint)TypeSpecRowFor(read.Type)),
                            operandFirstRow, 1);
                    continue;
                }
                if (frame.Expression is BoundContractReturnValue result)
                {
                    var operandFirstRow = checked((uint)(
                        predicateNodeOperands.Count + 1));
                    predicateNodeOperands.Add(
                        new TrRmPredicateNodeOperandRow(1,
                            rootRowsByIdentity[(5, 0)]));
                    predicateNodes[nodeRow - 1]
                        = new TrRmPredicateNodeRow(nodeKind,
                            checked((uint)TypeSpecRowFor(result.Type)),
                            operandFirstRow, 1);
                    continue;
                }
                if (frame.Expression is BoundConstant constant)
                {
                    var operandFirstRow = checked((uint)(
                        predicateNodeOperands.Count + 1));
                    var frozenValue
                        = predicateFrozenScalarValues
                            .AddFrozenValueNode(constant);
                    predicateNodeOperands.Add(
                        new TrRmPredicateNodeOperandRow(2,
                            frozenValue.FrozenValueNodeRow));
                    predicateNodes[nodeRow - 1]
                        = new TrRmPredicateNodeRow(nodeKind,
                            checked((uint)TypeSpecRowFor(constant.Type)),
                            operandFirstRow, 1);
                    continue;
                }

                var nodeOperandFirstRow = 0u;
                var nodeOperandCount = 0u;
                if (frame.Expression is BoundUnary unary
                    && TryGetGeneralPredicateUnaryOperation(unary,
                        out var unaryOperationCode, out var unaryPolicy))
                {
                    var operationRow = checked(
                        (uint)predicateOperations.Count + 1);
                    predicateOperations.Add(
                        new TrRmPredicateOperationRow(
                            unaryOperationCode, unaryPolicy));
                    nodeOperandFirstRow = checked((uint)(
                        predicateNodeOperands.Count + 1));
                    predicateNodeOperands.Add(
                        new TrRmPredicateNodeOperandRow(4, operationRow));
                    nodeOperandCount = 1;
                }
                else if (frame.Expression is BoundCast cast
                    && TryGetGeneralPredicateConversion(cast,
                        out var conversionOperationCode,
                        out var conversionPolicy))
                {
                    var operationRow = checked(
                        (uint)predicateOperations.Count + 1);
                    predicateOperations.Add(
                        new TrRmPredicateOperationRow(
                            conversionOperationCode, conversionPolicy));
                    nodeOperandFirstRow = checked((uint)(
                        predicateNodeOperands.Count + 1));
                    predicateNodeOperands.Add(
                        new TrRmPredicateNodeOperandRow(4, operationRow));
                    nodeOperandCount = 1;
                }
                else if (frame.Expression is BoundBinary binary
                    && TryGetGeneralPredicateBinaryOperation(binary,
                        out var operationCode, out var policy))
                {
                    var operationRow = checked(
                        (uint)predicateOperations.Count + 1);
                    predicateOperations.Add(
                        new TrRmPredicateOperationRow(operationCode,
                            policy));
                    nodeOperandFirstRow = checked((uint)(
                        predicateNodeOperands.Count + 1));
                    predicateNodeOperands.Add(
                        new TrRmPredicateNodeOperandRow(4, operationRow));
                    nodeOperandCount = 1;
                }

                stack.Push((frame.Expression, frame.ParentNodeRow,
                    frame.ChildOrdinal, true, nodeRow, nodeKind,
                    nodeOperandFirstRow, nodeOperandCount));
                for (var child = children.Length - 1; child >= 0; child--)
                {
                    stack.Push((children[child], nodeRow,
                        checked((uint)child), false, 0, 0, 0, 0));
                }
            }

            return rootNodeRow;
        }

        void AddGeneralInitializationOutputStateContracts(
            BoundFunction function, uint owner,
            ImmutableArray<BoundOutputStatePredicate> predicates,
            int precedingSelectorCount)
        {
            if (!generalPredicateGraphsInitializationReferenceBodies)
            {
                throw Unsupported(
                    "General OutputState predicates require the exact GeneralPredicateGraphsDraft initialization profile.");
            }

            var ordered = predicates
                .OrderBy(static predicate => predicate.PhaseLocalOrdinal)
                .ThenBy(static predicate => predicate.FormalOrdinal)
                .ThenBy(static predicate => predicate.State)
                .ToArray();
            var groupIndex = 0;
            foreach (var group in ordered.GroupBy(
                static predicate => predicate.PhaseLocalOrdinal))
            {
                if (group.Key != checked((uint)(precedingSelectorCount
                        + groupIndex++)))
                {
                    throw Unsupported(
                        "General initialization-reference selector ordinals must follow the bounded selector group contiguously.");
                }

                var predicate = group.First().Predicate;
                if (group.Any(candidate =>
                        !Equals(candidate.Predicate, predicate))
                    || group.Any(candidate =>
                        candidate.FormalOrdinal < 0
                            || candidate.FormalOrdinal
                                >= function.ParameterCount
                            || candidate.State is not (0 or 1)
                            || !IsStandardInitializationReferenceParameter(
                                function.Variables[
                                    candidate.FormalOrdinal], program)))
                {
                    throw Unsupported(
                        "A GeneralPredicateGraphsDraft selector has an invalid scalar init output association.");
                }

                var graphRow = checked(predicateGraphs.Count + 1);
                var footprintRow
                    = checked(predicateReadFootprints.Count + 1);
                var readRoots = new SortedSet<(byte Kind, int Identity)>();
                var pendingExpressions = new Stack<BoundExpression>();
                pendingExpressions.Push(predicate);
                while (pendingExpressions.Count != 0)
                {
                    var expression = pendingExpressions.Pop();
                    var children = GetGeneralPredicateChildren(expression);
                    if (expression is BoundRead read)
                    {
                        if (read.Variable < 0
                            || read.Variable >= function.ParameterCount
                            || function.Variables[read.Variable].IsOut
                            || function.Variables[read.Variable].IsInit
                            || function.Variables[read.Variable].IsMutable
                            || function.Variables[read.Variable].Type
                                != read.Type
                            || !PrimitiveTypeFacts.IsBuiltInScalar(
                                read.Type))
                        {
                            throw Unsupported(
                                "A GeneralPredicateGraphsDraft selector must read immutable scalar input formals.");
                        }

                        _ = readRoots.Add((2, read.Variable));
                    }
                    else if (expression is BoundContractReturnValue result)
                    {
                        if (result.Type != function.ReturnType
                            || !PrimitiveTypeFacts.IsBuiltInScalar(
                                result.Type))
                        {
                            throw Unsupported(
                                "A GeneralPredicateGraphsDraft selector requires a built-in scalar saved result.");
                        }

                        _ = readRoots.Add((5, 0));
                    }
                    for (var child = children.Length - 1; child >= 0;
                        child--)
                    {
                        pendingExpressions.Push(children[child]);
                    }
                }

                var firstRootRow = readRoots.Count == 0
                    ? 0 : checked(predicateReadRoots.Count + 1);
                var rootRowsByIdentity = new Dictionary<
                    (byte Kind, int Identity), uint>(
                    readRoots.Count);
                foreach (var root in readRoots)
                {
                    var rootRow = checked((uint)(
                        predicateReadRoots.Count + 1));
                    predicateReadRoots.Add(new TrRmPredicateReadRootRow(
                        root.Kind, checked((uint)root.Identity)));
                    rootRowsByIdentity.Add(root, rootRow);
                }
                predicateReadFootprints.Add(
                    new TrRmPredicateReadFootprintRow(firstRootRow,
                        readRoots.Count, 0, 0));
                var noCaptures = new Dictionary<int,
                    (uint CaptureRow, uint Ordinal, PrimitiveType Type)>();
                var graphRootNode = BuildGeneralPredicateNode(function,
                    predicate, rootRowsByIdentity, noCaptures, null);
                predicateGraphs.Add(new TrRmPredicateGraphRow(
                    graphRootNode, footprintRow));

                var predicatePayload = new byte[12];
                predicatePayload[0] = 3;
                BinaryPrimitives.WriteUInt32LittleEndian(
                    predicatePayload.AsSpan(4), group.Key);
                BinaryPrimitives.WriteUInt32LittleEndian(
                    predicatePayload.AsSpan(8), checked((uint)graphRow));
                contracts.Add(new TrRmContractRow(owner, 0x0B,
                    predicatePayload));

                var emittedOutputs = new HashSet<(int Formal, byte State)>();
                foreach (var output in group)
                {
                    if (!emittedOutputs.Add((output.FormalOrdinal,
                            output.State)))
                    {
                        throw Unsupported(
                            "A general OutputState predicate duplicates one formal/state association.");
                    }

                    var outputPayload = new byte[12];
                    BinaryPrimitives.WriteUInt32LittleEndian(outputPayload,
                        checked((uint)output.FormalOrdinal));
                    BinaryPrimitives.WriteUInt32LittleEndian(
                        outputPayload.AsSpan(4), checked((uint)graphRow));
                    outputPayload[8] = output.State;
                    contracts.Add(new TrRmContractRow(owner, 0x0002,
                        outputPayload));
                }
            }
        }

        void AddGeneralDeclarationContractGraphs(
            BoundFunction function, uint owner)
        {
            if (!emitsConstruction2DeclarationPredicateGraphs)
            {
                throw Unsupported(
                    "Phase-1/2 predicate graphs require the exact no-label Construction-2 profile and its failure-identity output.");
            }

            foreach (var clause in function.DeclarationContractClauses)
            {
                if (clause.Predicate.Type != PrimitiveType.Bool)
                {
                    throw Unsupported(
                        "A declaration predicate graph must have a Boolean root.");
                }

                var graphRow = checked(predicateGraphs.Count + 1);
                var footprintRow
                    = checked(predicateReadFootprints.Count + 1);
                var readRoots = new SortedSet<(byte Kind, int Identity)>();
                var captureRowsByVariable = new Dictionary<int,
                    (uint CaptureRow, uint Ordinal, PrimitiveType Type)>();
                void AddRoleOneCapture(int variable,
                    PrimitiveType capturedType, bool allowMutable = false)
                {
                    if (variable < function.ParameterCount
                        || variable >= function.Variables.Length
                        || function.Variables[variable].Type != capturedType
                        || function.Variables[variable].IsOut
                        || function.Variables[variable].IsInit
                        || (!allowMutable
                            && function.Variables[variable].IsMutable)
                        || captureRowsByVariable.ContainsKey(variable))
                    {
                        throw Unsupported(
                            "A role-1 graph capture has an invalid owner, type or ordinal.");
                    }

                    var captureOrdinal
                        = checked((uint)captureRowsByVariable.Count);
                    var captureRow
                        = checked((uint)predicateCaptures.Count + 1);
                    var capturedTypeSpecRef
                        = checked((uint)TypeSpecRowFor(capturedType));
                    predicateCaptures.Add(new TrRmPredicateCaptureRow(
                        graphRow, captureOrdinal, 1, 0,
                        capturedTypeSpecRef));
                    captureRowsByVariable.Add(variable,
                        (captureRow, captureOrdinal, capturedType));
                }

                void AddRoleTwoCapture(int variable,
                    PrimitiveType capturedType)
                {
                    if (variable < function.ParameterCount
                            + (function.IsConstructor ? 1 : 0)
                        || variable >= function.Variables.Length
                        || function.Variables[variable].Type != capturedType
                        || function.Variables[variable].IsOut
                        || function.Variables[variable].IsInit
                        || function.Variables[variable]
                            .IsInitializationLocal
                        || !CheckedReferenceTypeContract
                            .IsSupportedReferencePatternType(capturedType,
                                program.CheckedReferenceTypes)
                        || captureRowsByVariable.ContainsKey(variable))
                    {
                        throw Unsupported(
                            "A role-2 graph capture has an invalid owner, type or ordinal.");
                    }

                    var captureOrdinal
                        = checked((uint)captureRowsByVariable.Count);
                    var captureRow
                        = checked((uint)predicateCaptures.Count + 1);
                    var capturedTypeSpecRef
                        = checked((uint)TypeSpecRowFor(capturedType));
                    var flags = function.Variables[variable].IsMutable
                        ? (byte)1 : (byte)0;
                    predicateCaptures.Add(new TrRmPredicateCaptureRow(
                        graphRow, captureOrdinal, 2, flags,
                        capturedTypeSpecRef));
                    captureRowsByVariable.Add(variable,
                        (captureRow, captureOrdinal, capturedType));
                }

                void AddPatternObservationRoot(
                    BoundExpression observation)
                {
                    if (clause.Phase is not (
                            BoundDeclarationContractPhase.Precondition
                            or BoundDeclarationContractPhase
                                .NormalPostcondition)
                        || observation is not BoundRead patternRead
                        || patternRead.Variable < 0
                        || patternRead.Variable >= function.ParameterCount
                        || function.Variables[patternRead.Variable].Type
                            != patternRead.Type
                        || function.Variables[patternRead.Variable].IsOut
                        || function.Variables[patternRead.Variable].IsInit
                        || function.Variables[patternRead.Variable].IsMutable
                        || (!PrimitiveTypeFacts.IsBuiltInScalar(
                                patternRead.Type)
                            && !TryGetOptionalBuiltInScalarType(
                                patternRead.Type,
                                program.CheckedReferenceTypes, out _)
                            && !CheckedReferenceTypeContract
                                .IsSupportedReferencePatternType(
                                    patternRead.Type,
                                    program.CheckedReferenceTypes)))
                    {
                        throw Unsupported(
                            "A composed declaration pattern requires one supported immutable input-formal observation.");
                    }

                    _ = readRoots.Add((2, patternRead.Variable));
                }

                var pendingExpressions = new Stack<BoundExpression>();
                pendingExpressions.Push(clause.Predicate);
                while (pendingExpressions.Count != 0)
                {
                    var expression = pendingExpressions.Pop();
                    var children = GetGeneralPredicateChildren(expression);
                    if (expression is BoundUnary {
                            IsPatternComposition: true,
                            PatternObservation: { } unaryObservation,
                        })
                    {
                        AddPatternObservationRoot(unaryObservation);
                    }
                    else if (expression is BoundBinary {
                            IsPatternComposition: true,
                            PatternObservation: { } binaryObservation,
                        })
                    {
                        AddPatternObservationRoot(binaryObservation);
                    }
                    else if (expression is BoundWildcardPattern wildcardPattern)
                    {
                        AddPatternObservationRoot(
                            wildcardPattern.Observation);
                    }
                    else if (expression is BoundBinary {
                            IsConstantPatternTest: true,
                        } constantPatternTest)
                    {
                        if (clause.Phase is not (
                                BoundDeclarationContractPhase.Precondition
                                or BoundDeclarationContractPhase
                                    .NormalPostcondition)
                            || constantPatternTest.Operator
                                != TokenKind.EqualsEquals
                            || constantPatternTest.Type
                                != PrimitiveType.Bool
                            || constantPatternTest.Left
                                is not BoundRead subjectRead
                            || constantPatternTest.Right
                                is not BoundConstant constant
                            || subjectRead.Variable < 0
                            || subjectRead.Variable
                                >= function.ParameterCount
                            || function.Variables[subjectRead.Variable].IsOut
                            || function.Variables[subjectRead.Variable].IsInit
                            || function.Variables[subjectRead.Variable]
                                .IsMutable
                            || function.Variables[subjectRead.Variable].Type
                                != subjectRead.Type
                            || !PrimitiveTypeFacts.IsBuiltInScalar(
                                subjectRead.Type)
                            || constant.Type != subjectRead.Type
                            || constant.RepresentationType != subjectRead.Type
                            || !constant.Bits.IsCanonicalFor(constant.Type))
                        {
                            throw Unsupported(
                                "A scalar constant-pattern graph requires an exact typed input formal and canonical literal.");
                        }

                        _ = readRoots.Add((2, subjectRead.Variable));
                    }
                    else if (expression is BoundIsNull nullTest)
                    {
                        if (clause.Phase
                                != BoundDeclarationContractPhase
                                    .NormalPostcondition
                            || nullTest.Value is not BoundRead optionalRead
                            || optionalRead.Variable < 0
                            || optionalRead.Variable
                                >= function.ParameterCount
                            || function.Variables[optionalRead.Variable].Type
                                != optionalRead.Type
                            || function.Variables[optionalRead.Variable].IsOut
                            || function.Variables[optionalRead.Variable].IsInit
                            || function.Variables[optionalRead.Variable]
                                .IsMutable
                            || !TryGetOptionalBuiltInScalarType(
                                optionalRead.Type,
                                program.CheckedReferenceTypes, out _))
                        {
                            throw Unsupported(
                                "A Null pattern graph requires a scalar optional input formal.");
                        }

                        _ = readRoots.Add((2, optionalRead.Variable));
                    }
                    else if (expression
                        is BoundTypeTestPattern typeTest)
                    {
                        if (clause.Phase is not (
                                BoundDeclarationContractPhase.Precondition
                                or BoundDeclarationContractPhase
                                    .NormalPostcondition)
                            || typeTest.Value is not BoundRead optionalRead
                            || optionalRead.Variable < 0
                            || optionalRead.Variable
                                >= function.ParameterCount
                            || function.Variables[optionalRead.Variable].Type
                                != optionalRead.Type
                            || function.Variables[optionalRead.Variable].IsOut
                            || function.Variables[optionalRead.Variable].IsInit
                            || function.Variables[optionalRead.Variable]
                                .IsMutable
                            || (!PrimitiveTypeFacts.IsBuiltInScalar(
                                    optionalRead.Type)
                                && !TryGetOptionalBuiltInScalarType(
                                    optionalRead.Type,
                                    program.CheckedReferenceTypes, out _))
                            || !generalPredicateScalarTypeRows.ContainsKey(
                                typeTest.TestedType))
                        {
                            throw Unsupported(
                                "A TypeTest graph requires a scalar input and a supported scalar TypeUse in a declaration clause.");
                        }

                        _ = readRoots.Add((2, optionalRead.Variable));
                    }
                    else if (expression is BoundIsSome optionalTest)
                    {
                        if (clause.Phase
                                != BoundDeclarationContractPhase
                                    .NormalPostcondition
                            || optionalTest.Value is not BoundRead optionalRead
                            || optionalRead.Variable < 0
                            || optionalRead.Variable
                                >= function.ParameterCount
                            || function.Variables[optionalRead.Variable].Type
                                != optionalRead.Type
                            || function.Variables[optionalRead.Variable].IsOut
                            || function.Variables[optionalRead.Variable].IsInit
                            || function.Variables[optionalRead.Variable]
                                .IsMutable
                            || !TryGetOptionalBuiltInScalarType(
                                optionalRead.Type,
                                program.CheckedReferenceTypes,
                                out var payloadType))
                        {
                            throw Unsupported(
                                "InferredNonNullBind requires one scalar optional input and its exact graph-local value capture.");
                        }

                        AddRoleOneCapture(optionalTest.Variable,
                            payloadType);
                        _ = readRoots.Add((2, optionalRead.Variable));
                    }
                    else if (expression
                        is BoundIsVariablePattern variablePattern)
                    {
                        if (clause.Phase
                                != BoundDeclarationContractPhase
                                    .NormalPostcondition
                            || variablePattern.Value
                                is not BoundRead optionalRead
                            || optionalRead.Variable < 0
                            || optionalRead.Variable
                                >= function.ParameterCount
                            || function.Variables[optionalRead.Variable].Type
                                != optionalRead.Type
                            || function.Variables[optionalRead.Variable].IsOut
                            || function.Variables[optionalRead.Variable].IsInit
                            || function.Variables[optionalRead.Variable]
                                .IsMutable
                            || !TryGetOptionalBuiltInScalarType(
                                optionalRead.Type,
                                program.CheckedReferenceTypes,
                                out var payloadType))
                        {
                            throw Unsupported(
                                "An inferred optional binding requires a scalar optional input.");
                        }

                        var captureType = variablePattern.IncludeNull
                            ? optionalRead.Type : payloadType;
                        AddRoleOneCapture(variablePattern.Variable,
                            captureType);
                        _ = readRoots.Add((2, optionalRead.Variable));
                    }
                    else if (expression
                        is BoundTypedValuePattern typedValuePattern)
                    {
                        if (clause.Phase is not (
                                BoundDeclarationContractPhase.Precondition
                                or BoundDeclarationContractPhase
                                    .NormalPostcondition)
                            || typedValuePattern.Value
                                is not BoundRead typedValueRead
                            || typedValueRead.Variable < 0
                            || typedValueRead.Variable
                                >= function.ParameterCount
                            || function.Variables[typedValueRead.Variable]
                                .Type != typedValueRead.Type
                            || function.Variables[typedValueRead.Variable]
                                .IsOut
                            || function.Variables[typedValueRead.Variable]
                                .IsInit
                            || function.Variables[typedValueRead.Variable]
                                .IsMutable
                            || (!PrimitiveTypeFacts.IsBuiltInScalar(
                                    typedValueRead.Type)
                                && !TryGetOptionalBuiltInScalarType(
                                    typedValueRead.Type,
                                    program.CheckedReferenceTypes,
                                    out _))
                            || !generalPredicateScalarTypeRows.ContainsKey(
                                typedValuePattern.TestedType))
                        {
                            throw Unsupported(
                                "A TypedValueBind graph requires a scalar input and supported scalar TypeUse.");
                        }

                        AddRoleOneCapture(typedValuePattern.Variable,
                            typedValuePattern.TestedType,
                            allowMutable: true);
                        _ = readRoots.Add((2, typedValueRead.Variable));
                    }
                    else if (expression
                        is BoundReferenceTypeTestPattern referenceTypeTest)
                    {
                        if (clause.Phase is not (
                                BoundDeclarationContractPhase.Precondition
                                or BoundDeclarationContractPhase
                                    .NormalPostcondition)
                            || referenceTypeTest.Value
                                is not BoundRead referenceRead
                            || referenceRead.Variable < 0
                            || referenceRead.Variable
                                >= function.ParameterCount
                            || function.Variables[referenceRead.Variable]
                                .Type != referenceRead.Type
                            || function.Variables[referenceRead.Variable]
                                .IsOut
                            || function.Variables[referenceRead.Variable]
                                .IsInit
                            || function.Variables[referenceRead.Variable]
                                .IsMutable
                            || !CheckedReferenceTypeContract
                                .IsSupportedReferencePatternType(
                                referenceRead.Type,
                                program.CheckedReferenceTypes))
                        {
                            throw Unsupported(
                                "A reference-pattern graph requires a named checked-reference input formal.");
                        }

                        if (referenceTypeTest.Variable < -1)
                        {
                            throw Unsupported(
                                "A reference-pattern graph has an invalid capture index.");
                        }
                        if (referenceTypeTest.Variable >= 0)
                        {
                            AddRoleTwoCapture(referenceTypeTest.Variable,
                                referenceTypeTest.TestedType);
                        }
                        _ = ReferenceTypePatternRowFor(
                            referenceTypeTest.TestedType);
                        _ = readRoots.Add((2, referenceRead.Variable));
                    }
                    else if (expression is BoundRead capturedRead
                        && captureRowsByVariable.TryGetValue(
                            capturedRead.Variable, out var capture))
                    {
                        if (capturedRead.Type != capture.Type)
                        {
                            throw Unsupported(
                                "A graph-local capture read has a mismatched type.");
                        }
                    }
                    else if (expression is BoundRead formalRead)
                    {
                        if (formalRead.Variable < 0
                            || formalRead.Variable >= function.ParameterCount
                            || function.Variables[formalRead.Variable].IsOut
                            || function.Variables[formalRead.Variable].IsInit
                            || function.Variables[formalRead.Variable].IsMutable
                            || function.Variables[formalRead.Variable].Type
                                != formalRead.Type
                            || !PrimitiveTypeFacts.IsBuiltInScalar(
                                formalRead.Type))
                        {
                            throw Unsupported(
                                "A declaration predicate graph must read only immutable scalar input formals.");
                        }

                        _ = readRoots.Add((2, formalRead.Variable));
                    }
                    else if (expression is BoundIsInitialized initialized)
                    {
                        if (clause.Phase
                                != BoundDeclarationContractPhase
                                    .NormalPostcondition
                            || initialized.Variable < 0
                            || initialized.Variable
                                >= function.ParameterCount
                            || function.Variables[initialized.Variable].IsOut
                            || function.Variables[initialized.Variable].IsInit
                            || function.Variables[initialized.Variable]
                                .IsMutable
                            || !PrimitiveTypeFacts.IsBuiltInScalar(
                                function.Variables[initialized.Variable].Type))
                        {
                            throw Unsupported(
                                "A phase-2 Initialized pattern requires a scalar input formal.");
                        }

                        _ = readRoots.Add((2, initialized.Variable));
                    }
                    else if (expression
                        is BoundContractReturnValue result)
                    {
                        if (clause.Phase
                                != BoundDeclarationContractPhase
                                    .NormalPostcondition
                            || result.Type != function.ReturnType
                            || !PrimitiveTypeFacts.IsBuiltInScalar(
                                result.Type))
                        {
                            throw Unsupported(
                                "A declaration predicate graph has an unsupported saved-result observation.");
                        }

                        _ = readRoots.Add((5, 0));
                    }
                    else if (expression
                        is BoundReferenceInitializationTest initialized)
                    {
                        if (clause.Phase
                                != BoundDeclarationContractPhase
                                    .NormalPostcondition
                            || initialized.Descriptor
                                is not BoundRead descriptor
                            || descriptor.Variable < 0
                            || descriptor.Variable
                                >= function.ParameterCount
                            || function.Variables[descriptor.Variable].Type
                                != descriptor.Type
                            || !IsStandardInitializationReferenceParameter(
                                function.Variables[descriptor.Variable],
                                program)
                            || !TryGetStandardInitializationReferenceReferentType(
                                descriptor.Type,
                                program.CheckedReferenceTypes,
                                out var referentType)
                            || !PrimitiveTypeFacts.IsBuiltInScalar(
                                referentType))
                        {
                            throw Unsupported(
                                "A phase-2 Initialized pattern requires a scalar logical output formal.");
                        }

                        _ = readRoots.Add((4, descriptor.Variable));
                    }
                    else if (expression is BoundConstant constant
                        && !PrimitiveTypeFacts.IsBuiltInScalar(
                            constant.Type))
                    {
                        throw Unsupported(
                            "A declaration predicate graph has an unsupported non-scalar literal.");
                    }

                    for (var child = children.Length - 1; child >= 0;
                        child--)
                    {
                        pendingExpressions.Push(children[child]);
                    }
                }

                var firstRootRow = readRoots.Count == 0
                    ? 0 : checked(predicateReadRoots.Count + 1);
                var rootRowsByIdentity = new Dictionary<
                    (byte Kind, int Identity), uint>(readRoots.Count);
                foreach (var root in readRoots)
                {
                    var rootRow = checked((uint)(
                        predicateReadRoots.Count + 1));
                    predicateReadRoots.Add(new TrRmPredicateReadRootRow(
                        root.Kind, checked((uint)root.Identity)));
                    rootRowsByIdentity.Add(root, rootRow);
                }

                predicateReadFootprints.Add(
                    new TrRmPredicateReadFootprintRow(firstRootRow,
                        readRoots.Count, 0, 0));
                var graphRootNode = BuildGeneralPredicateNode(function,
                    clause.Predicate, rootRowsByIdentity,
                    captureRowsByVariable, clause.Phase);
                predicateGraphs.Add(new TrRmPredicateGraphRow(
                    graphRootNode, footprintRow));

                var predicatePayload = new byte[12];
                predicatePayload[0] = (byte)clause.Phase;
                BinaryPrimitives.WriteUInt32LittleEndian(
                    predicatePayload.AsSpan(4),
                    clause.PhaseLocalOrdinal);
                BinaryPrimitives.WriteUInt32LittleEndian(
                    predicatePayload.AsSpan(8),
                    checked((uint)graphRow));
                contracts.Add(new TrRmContractRow(owner, 0x0B,
                    predicatePayload));
            }
        }

        var standardSourceImportMappings = hasContextualTypeViews
            ? CreateC1M1MethodTypeSourceImportMappings(program,
                typeDefStructureIndexes, typeDefStructureViewIndexes,
                typeDefRowByContextualView, structureFieldFirst, names)
            : CreateStandardLayoutQuerySourceImportMappings(program,
                schema.UsesNestedInitializationReferenceFieldDestinations()
                    ? StandardInitReferenceBodiesNestedFieldDestinationsSchemaIdentity
                    : schema.UsesInitializationReferenceLocalFieldLayout()
                        ? StandardInitReferenceBodiesLocalFieldLayoutSchemaIdentity
                        : IsSelectedStandardLayoutQueriesSchemaIdentity(
                            standardSchemaIdentity)
                            ? standardSchemaIdentity!
                        : StandardLayoutQueriesSchemaIdentity,
                structureTypeDefRows, structureFieldFirst, names);
        var pendingStandardImports
            = new List<PendingStandardImport>(
                standardSourceImportMappings.Count
                    + program.Functions.Count(static function =>
                        function.ModuleImport is not null));
        for (var index = 0; index < program.Functions.Length; index++)
        {
            if (program.Functions[index].ModuleImport
                is BoundModuleImport moduleImport)
            {
                pendingStandardImports.Add(new PendingStandardImport(
                    moduleReferenceRowByIdentity[
                        moduleImport.ProducerIdentity],
                    0x1600_0000u | checked((uint)(index + 1)),
                    moduleImport.ProducerIdentity, moduleImport.ExportKey,
                    0, index, 0));
            }
        }

        foreach (var import in standardSourceImportMappings)
        {
            pendingStandardImports.Add(new PendingStandardImport(
                moduleReferenceRowByIdentity[import.ProducerIdentity],
                import.ExpectedDefinitionId, import.ProducerIdentity,
                import.ExportKey, import.ExportKeyStringRow, -1, 0));
        }

        pendingStandardImports.Sort(static (left, right) =>
        {
            var moduleRefOrder = left.ModuleRefRow.CompareTo(
                right.ModuleRefRow);
            if (moduleRefOrder != 0)
            {
                return moduleRefOrder;
            }

            var definitionOrder = left.ExpectedDefinitionId.CompareTo(
                right.ExpectedDefinitionId);
            return definitionOrder != 0 ? definitionOrder
                : StringComparer.Ordinal.Compare(left.ExportKey,
                    right.ExportKey);
        });

        var standardFieldImportRows = new Dictionary<(
            uint ExpectedDefinitionId, string ProducerIdentity,
            string ExportKey), int>();
        var standardTypeImportRows = new Dictionary<(
            uint ExpectedDefinitionId, string ProducerIdentity,
            string ExportKey), int>();
        var standardMethodImportRows = new Dictionary<int, int>();
        var importTriples = new HashSet<(int ModuleRefRow,
            uint ExpectedDefinitionId, string ExportKey)>();
        for (var index = 0; index < pendingStandardImports.Count; index++)
        {
            var import = pendingStandardImports[index];
            if (!importTriples.Add((import.ModuleRefRow,
                    import.ExpectedDefinitionId, import.ExportKey)))
            {
                throw Unsupported(
                    "One ModuleRef cannot select the same ExportKey/expected definition twice.");
            }

            var importRow = index + 1;
            pendingStandardImports[index] = import with {
                ImportRow = importRow,
            };
            if (import.FunctionIndex >= 0)
            {
                if ((import.ExpectedDefinitionId >> 24) != 0x16
                    || !standardMethodImportRows.TryAdd(
                        import.FunctionIndex, importRow))
                {
                    throw Unsupported(
                        "A selected MethodDef Import has duplicate local provider identity.");
                }
                continue;
            }

            var mapKey = (import.ExpectedDefinitionId,
                import.ProducerIdentity, import.ExportKey);
            if ((import.ExpectedDefinitionId >> 24) == 0x14)
            {
                if (!standardFieldImportRows.TryAdd(mapKey, importRow))
                {
                    throw Unsupported(
                        "A selected FieldDef Import has duplicate local provider identity.");
                }
            }
            else if ((import.ExpectedDefinitionId >> 24) == 0x13)
            {
                if (!standardTypeImportRows.TryAdd(mapKey, importRow))
                {
                    throw Unsupported(
                        "A selected TypeDef Import has duplicate local provider identity.");
                }
            }
        }

        uint FieldPathRowFor(BoundFieldRead leaf)
        {
            if (!schema.UsesNestedInitializationReferenceFieldDestinations()
                || !TryGetInitializationFieldPath(leaf, out var root,
                    out var path)
                || path.IsDefaultOrEmpty)
            {
                throw Unsupported(
                    "A nested initialization-reference target requires the exact FieldPath successor.");
            }
            if (path[0].Receiver.Type != root.Type
                || path[^1].Type != leaf.Type)
            {
                throw Unsupported(
                    "A nested initialization-reference FieldPath has an invalid root or leaf type.");
            }
            if (!HasSupportedStandardInitializationFieldPathGeometry(
                root.Type, path, program.Structures,
                program.CheckedReferenceTypes, program.EnumDefinitions,
                program.NativeLoweringAgreement))
            {
                throw Unsupported(
                    "A nested initialization-reference FieldPath has unsupported owner layout or an underaligned leaf.");
            }

            var rootTypeSpecRef = checked((uint)TypeSpecRowFor(root.Type));
            var fieldReferences = new uint[path.Length];
            for (var index = 0; index < path.Length; index++)
            {
                var step = path[index];
                var expectedOwnerType = index == 0
                    ? root.Type : path[index - 1].Type;
                var structureIndex = (int)step.Receiver.Type - 100;
                var fieldOrdinal = step.Field;
                if (step.Receiver.Type != expectedOwnerType
                    || (uint)structureIndex
                        >= (uint)program.Structures.Length
                    || !structureTypeDefRows.TryGetValue(structureIndex,
                        out var typeDefRow)
                    || typeDefRow <= 0
                    || typeDefRow > typeDefStructureIndexes.Length
                    || fieldOrdinal < 0
                    || fieldOrdinal >= program.Structures[structureIndex]
                        .Fields.Length
                    || program.Structures[structureIndex]
                        .Fields[fieldOrdinal].Type != step.Type)
                {
                    throw Unsupported(
                        "A nested initialization-reference FieldPath has an invalid owner or FieldDef.");
                }

                var ownerStructure = program.Structures[structureIndex];
                if (ownerStructure.IsUnique
                    || ownerStructure.CleanupMethod is not null
                    || ownerStructure.SourceGenericTypeDefinitionIndex >= 0
                    || ownerStructure.BlittableTargetRequirement
                        != FixedLayoutStorage.WindowsX64TargetRequirement
                    || (!ownerStructure.IsSequential
                        && !ownerStructure.IsTransparent))
                {
                    throw Unsupported(
                        "A nested initialization-reference FieldPath owner is not a closed Copy Blittable Sequential/Transparent struct.");
                }

                var firstField = structureFieldFirst[typeDefRow - 1];
                if (firstField == 0)
                {
                    throw Unsupported(
                        "A nested initialization-reference FieldPath step has no expected FieldDef.");
                }

                var fieldRow = checked(firstField + fieldOrdinal);
                if (fieldRow > fieldRows.Count
                    || fieldRow > 0x00FF_FFFF)
                {
                    throw Unsupported(
                        "A nested initialization-reference FieldPath step exceeds the FieldDef identity range.");
                }

                var localFieldDefinition
                    = 0x1400_0000u | checked((uint)fieldRow);
                var boundField
                    = ownerStructure.Fields[fieldOrdinal];
                if (boundField.StandardFieldOrigin is
                    BoundStandardFieldOrigin fieldOrigin)
                {
                    var selectedFieldKeys
                        = fieldOrigin.SelectedExportKeys.IsDefaultOrEmpty
                            ? fieldOrigin.ExportKeys
                            : fieldOrigin.SelectedExportKeys;
                    if (fieldOrigin.ExportKeys.IsDefaultOrEmpty
                        || selectedFieldKeys.IsDefaultOrEmpty
                        || !standardFieldImportRows.TryGetValue(
                            (localFieldDefinition,
                                fieldOrigin.Provider.ModuleIdentity,
                                selectedFieldKeys[0]), out var importRow))
                    {
                        throw Unsupported(
                            "A nested initialization-reference FieldPath Import has no exact selected FieldDef key.");
                    }

                    fieldReferences[index] = 0x1C00_0000u
                        | checked((uint)importRow);
                }
                else
                {
                    if (ownerStructure.StandardTypeOrigin is not null)
                    {
                        throw Unsupported(
                            "A nested initialization-reference FieldPath in an imported struct requires an explicit selected FieldDef key.");
                    }

                    fieldReferences[index] = localFieldDefinition;
                }
            }

            var signature = string.Concat(
                rootTypeSpecRef.ToString("X8",
                    System.Globalization.CultureInfo.InvariantCulture),
                ":",
                string.Join(",", fieldReferences.Select(
                    static fieldReference => fieldReference.ToString("X8",
                        System.Globalization.CultureInfo.InvariantCulture))));
            if (standardInitializationFieldPathRows.TryGetValue(
                signature, out var existingRow))
            {
                return existingRow;
            }
            if (fieldPathRows.Count >= 0x00FF_FFFF)
            {
                throw Unsupported(
                    "The nested initialization-reference FieldPath table exceeds its row domain.");
            }

            fieldPathRows.Add(new TrRmFieldPathRow(rootTypeSpecRef,
                fieldReferences));
            var row = checked((uint)fieldPathRows.Count);
            standardInitializationFieldPathRows.Add(signature, row);
            return row;
        }

        uint MethodMapTokenForFunction(int functionIndex)
        {
            if ((uint)functionIndex >= (uint)program.Functions.Length)
            {
                throw Invalid(
                    "A Standard Body call target is outside the function table.");
            }

            if (standardMethodImportRows.TryGetValue(functionIndex,
                    out var importRow))
            {
                return 0x1C00_0000u | checked((uint)importRow);
            }
            if (standardNativePointerLoads
                && program.Functions[functionIndex].DirectNativeImport
                    is not null)
            {
                return 0x1600_0000u | checked((uint)(functionIndex + 1));
            }

            if (program.Functions[functionIndex].IsExternal)
            {
                throw Unsupported(
                    "The Standard initialization-reference Body subset does not support non-Module external calls.");
            }

            return 0x1600_0000u | checked((uint)(functionIndex + 1));
        }

        for (var typeDefIndex = 0;
            typeDefIndex < typeDefStructureIndexes.Length; typeDefIndex++)
        {
            var structureIndex = typeDefStructureIndexes[typeDefIndex];
            var typeViewIndex = typeDefStructureViewIndexes[typeDefIndex];
            if (typeViewIndex >= 0)
            {
                var typeDefRow = typeDefIndex + 1;
                var view = contextualTypeViews[typeViewIndex];
                var key = view.Expectation?.ExportKey ?? view.ExportKey
                    ?? throw Invalid(
                        "A contextual TypeDef TypeSpec has no retained ExportKey.");
                var expectedDefinition = 0x1300_0000u
                    | checked((uint)typeDefRow);
                if (!standardTypeImportRows.TryGetValue(
                        (expectedDefinition, view.Provider.ModuleIdentity,
                            key), out var importRow))
                {
                    throw Unsupported(
                        "A contextual TypeDef has no matching TypeDef Import row.");
                }

                var contextualTypeSpecRow = typeRowsList.Count + 1;
                typeRowsList.Add(TrRmTypeSpecTableWriter.Reference(0x03,
                    checked((uint)importRow)));
                typeDefTypeSpecRows.Add(typeDefRow,
                    contextualTypeSpecRow);
                continue;
            }

            var origin = program.Structures[structureIndex]
                .StandardTypeOrigin;
            if (origin is null)
            {
                continue;
            }

            var typeDefRow = structureTypeDefRows[structureIndex];
            var expectedDefinition = 0x1300_0000u
                | checked((uint)typeDefRow);
            var selectedTypeKeys = origin.SelectedExportKeys.IsDefaultOrEmpty
                ? origin.ExportKeys : origin.SelectedExportKeys;
            var key = selectedTypeKeys[0];
            if (!standardTypeImportRows.TryGetValue(
                    (expectedDefinition, origin.Provider.ModuleIdentity, key),
                    out var importRow))
            {
                throw Unsupported(
                    "A query TypeMap type has no matching TypeDef Import row.");
            }

            var typeSpecRow = typeRowsList.Count + 1;
            typeRowsList.Add(TrRmTypeSpecTableWriter.Reference(0x03,
                checked((uint)importRow)));
            typeDefTypeSpecRows.Add(typeDefRow, typeSpecRow);
        }

        int ContextualTypeSpecRowForView(int typeViewIndex)
        {
            if (!typeDefRowByContextualView.TryGetValue(typeViewIndex,
                    out var typeDefRow)
                || (uint)typeViewIndex
                    >= (uint)contextualTypeViews.Length)
            {
                throw Invalid(
                    "A contextual type occurrence has no emitted TypeDef view.");
            }

            if (typeDefTypeSpecRows.TryGetValue(typeDefRow,
                    out var existingRow))
            {
                return existingRow;
            }

            var view = contextualTypeViews[typeViewIndex];
            var key = view.Expectation?.ExportKey ?? view.ExportKey
                ?? throw Invalid(
                    "A contextual TypeDef TypeSpec has no retained ExportKey.");
            var expectedDefinition = 0x1300_0000u
                | checked((uint)typeDefRow);
            if (!standardTypeImportRows.TryGetValue(
                    (expectedDefinition, view.Provider.ModuleIdentity, key),
                    out var importRow))
            {
                throw Unsupported(
                    "A contextual type occurrence has no matching TypeDef Import row.");
            }

            var typeSpecRow = typeRowsList.Count + 1;
            typeRowsList.Add(TrRmTypeSpecTableWriter.Reference(0x03,
                checked((uint)importRow)));
            typeDefTypeSpecRows.Add(typeDefRow, typeSpecRow);
            return typeSpecRow;
        }

        int ContextualMethodTypeSpecRow(int functionIndex,
            int parameterOrdinal, PrimitiveType type)
        {
            var structureIndex = (int)type - 100;
            if ((uint)structureIndex >= (uint)program.Structures.Length
                || !hasContextualTypeViews)
            {
                return 0;
            }

            var function = program.Functions[functionIndex];
            if (function.ModuleImport is not BoundModuleImport methodImport
                || program.StandardQuerySourceAssociation
                    is not BoundStandardQuerySourceAssociation association
                || association.MethodTypeViews.IsDefaultOrEmpty)
            {
                throw Invalid(
                    "A struct-valued MethodDef Import has no retained contextual signature view.");
            }

            var matchingViews = association.MethodTypeViews.Where(view =>
                    StringComparer.Ordinal.Equals(
                        view.MethodExportKey, methodImport.ExportKey)
                    && view.SignaturePosition == parameterOrdinal)
                .ToArray();
            if (matchingViews.Length != 1
                || (uint)matchingViews[0].TypeViewIndex
                    >= (uint)contextualTypeViews.Length
                || contextualTypeViews[matchingViews[0].TypeViewIndex]
                    .StructureIndex != structureIndex)
            {
                throw Invalid(
                    "A struct-valued MethodDef Import has an ambiguous or mismatched contextual type view.");
            }

            return ContextualTypeSpecRowForView(
                matchingViews[0].TypeViewIndex);
        }

        _ = blittableStructureIndexSet.Count == 0
            ? [] : FixedLayoutStorage.Validate(program.Structures,
                program.NativeFixedArrayTypes,
                enumDefinitions: program.EnumDefinitions);
        for (var index = 0; index < typeDefStructureIndexes.Length; index++)
        {
            var structureIndex = typeDefStructureIndexes[index];
            var structure = TypeDefStructure(index);
            for (var field = 0; field < structure.Fields.Length; field++)
            {
                var fieldDefinition = structure.Fields[field];
                var typeViewIndex = typeDefStructureViewIndexes[index];
                var fieldTypeViewIndex = typeViewIndex < 0
                    ? -1
                    : contextualTypeViews[typeViewIndex]
                        .FieldTypeViewIndexes[field];
                var fieldTypeRow = fieldTypeViewIndex >= 0
                    ? ContextualTypeSpecRowForView(fieldTypeViewIndex)
                    : blittableStructureIndexSet.Contains(structureIndex)
                        ? NativeBlittableFieldTypeRow(typeRowsList,
                            typeDefTypeSpecRows, structureTypeDefRows,
                            fieldDefinition.Type, nativeRawPointerTypeRow,
                            nativeFunctionPointerTypeRows,
                            nativePointerTypeRows,
                            program.NativePointerTypes, TypeSpecRowFor)
                        : NativeValueTypeRow(fieldDefinition.Type,
                            nativeRawPointerTypeRow,
                            nativeFunctionPointerTypeRows, typeRowsList,
                            nativePointerTypeRows,
                            program.NativePointerTypes,
                            structureTypeDefRows, typeDefTypeSpecRows,
                            TypeSpecRowFor);
                var visibility = fieldDefinition.IsPrivate ? 0u
                    : fieldDefinition.IsInternal ? 1u : 2u;
                var fieldFlags = visibility | (fieldDefinition.IsMutable ? 4u : 0u);
                fieldRows.Add(new TrRmFieldDefRow(index + 1, structureFieldNameRows[index][field],
                    checked((uint)fieldTypeRow), fieldFlags));
            }

            structureLayoutFirst[index] = layoutRows.Count + 1;
            structureLayoutCount[index] = 2;
            var layoutKind = structure.LayoutKind;
            var declaredAlignment = structure.DeclaredAlignment is int alignment
                ? checked((uint)alignment) : 0u;
            var pack = checked((uint)structure.Packing);
            for (var variant = 0; variant < 2; variant++)
            {
                var layoutRow = layoutRows.Count + 1;
                var fieldFirst = structure.Fields.IsEmpty ? 0 : layoutFieldRows.Count + 1;
                layoutRows.Add(new TrRmLayoutRow(index + 1,
                    variant == 0 ? 0u : 1u, layoutKind, variant == 0 ? (byte)0 : (byte)1,
                    declaredAlignment, pack, fieldFirst, structure.Fields.Length));
                for (var field = 0; field < structure.Fields.Length; field++)
                {
                    layoutFieldRows.Add(new TrRmLayoutFieldRow(layoutRow, field,
                        structureFieldFirst[index] + field));
                }
            }
        }
        foreach (var function in program.Functions)
        {
            foreach (var variable in function.Variables)
            {
                if (NativePointerTypes.IsType(variable.Type))
                {
                    _ = NativeValueTypeRow(variable.Type, nativeRawPointerTypeRow,
                        nativeFunctionPointerTypeRows, typeRowsList, nativePointerTypeRows,
                        program.NativePointerTypes, structureTypeDefRows,
                        typeDefTypeSpecRows, TypeSpecRowFor);
                }
            }
            if (NativePointerTypes.IsType(function.ReturnType))
            {
                _ = NativeValueTypeRow(function.ReturnType, nativeRawPointerTypeRow,
                    nativeFunctionPointerTypeRows, typeRowsList, nativePointerTypeRows,
                    program.NativePointerTypes, structureTypeDefRows,
                    typeDefTypeSpecRows, TypeSpecRowFor);
            }
        }
        for (var index = 0; index < program.Functions.Length; index++)
        {
            var function = program.Functions[index];
            var hasRegisteredQueryBody = HasStandardRegisteredQuery(function);
            var hasNativeVTable = function.NativeVTableMethod is not null;
            var hasNativeExportTypeMethod = function.HasNativeExport
                && function.DeclaringStruct is not null && function.IsStatic;
            if (!seen.Add(function.Name) || function.IsConstructor
                || function.IsClassConstructor || function.ClassConstructor != -1
                || (function.DeclaringStruct is not null
                    && !hasNativeExportTypeMethod)
                || function.PropertyName is not null
                || (function.IsStatic && !hasNativeExportTypeMethod)
                || (function.Intrinsic != BoundIntrinsic.None
                    && !(hasNativeVTable && function.Intrinsic == BoundIntrinsic.NativeVTableCall))
                || function.IsCleanup
                || (!IsScalarValueType(function.ReturnType)
                    && !(function.IsExternal && function.DirectNativeImport is not null
                        && function.ReturnType == PrimitiveType.NativeRawPointer)
                    && !NativeFunctionPointerTypes.IsType(function.ReturnType)
                    && !NativePointerTypes.IsType(function.ReturnType)
                    && !(hasNativeVTable && function.ReturnType == PrimitiveType.NativeRawPointer)))
            {
                throw Unsupported($"Function '{function.Name}' has an unsupported declaration or effect.");
            }
            if (!function.IsExternal && functionFeatures[index].HasNonScalarStorage
                && !outputFeatures[index] && !hasRegisteredQueryBody)
            {
                throw Unsupported($"Function '{function.Name}' contains non-scalar storage.");
            }
            var hasOnlyNativeVTableOutputs = function.NativeVTableMethod
                    is NativeVTableMethodDefinition vtableMethod
                && !vtableMethod.Outputs.IsDefaultOrEmpty
                && vtableMethod.Outputs.Length == function.Variables
                    .Take(function.ParameterCount)
                    .Count(static parameter => parameter.IsOut);
            if (function.DirectNativeImport is null && !hasOnlyNativeVTableOutputs
                && HasOutputParameters(function) && !function.IsPrivate && !function.IsInternal
                && !(standardInitializationReferenceBodies
                    && function.Variables.Take(function.ParameterCount)
                        .Any(variable =>
                            IsStandardInitializationReferenceParameter(
                                variable, program))))
            {
                throw Unsupported($"Output function '{function.Name}' must not be exported.");
            }

            nameRows[index] = names.Count + 1;
            names.Add((function.Name, 1));
            if (function.HasNativeExport
                && (!function.NativeExportUsesDeclarationName
                    || function.NativeExportEntryName != function.Name))
            {
                nativeExportEntryNameRows[index] = names.Count + 1;
                names.Add((function.NativeExportEntryName!, 0));
            }
            if (function.DirectNativeImport is DirectNativeImport directImport)
            {
                nativeLibraryRows[index] = names.Count + 1;
                names.Add((directImport.LibraryName, 0));
                nativeExportRows[index] = names.Count + 1;
                names.Add((directImport.ExportName, 0));
            }
            if (function.NativeFunctionPointerType is not null
                && function.NativeCallableEntryName is string entryName)
            {
                if (!DirectNativeImportContract.IsValidExportName(entryName)
                    || !nativeCallableEntryNames.Add(entryName))
                {
                    throw Unsupported($"Function '{function.Name}' has an invalid or duplicate native callable entry name.");
                }

                nativeCallableEntryNameRows[index] = names.Count + 1;
                names.Add((entryName, 0));
            }
            else if (function.NativeCallableEntryName is not null)
            {
                throw Unsupported($"Function '{function.Name}' has a native callable entry name without a native callable contract.");
            }

            var first = function.ParameterCount == 0 ? 0 : parameters.Count + 1;
            var defaultsByOrdinal
                = new Dictionary<int, BoundOrdinaryParameterDefault>();
            if (schema.UsesClassParameterDefaults())
            {
                if (!function.HasOrdinaryParameterDefaultSurface
                    || function.OrdinaryParameterDefaults.IsDefault)
                {
                    throw Unsupported(
                        "CM1 MethodDef emission requires the checked parameter-label and default surface.");
                }

                var previousDefaultOrdinal = -1;
                foreach (var parameterDefault
                    in function.OrdinaryParameterDefaults)
                {
                    if (parameterDefault is null
                        || parameterDefault.ParameterOrdinal
                            <= previousDefaultOrdinal
                        || parameterDefault.ParameterOrdinal
                            >= function.ParameterCount
                        || parameterDefault.IsPending
                        || parameterDefault.Value is not BoundConstant
                        || parameterDefault.Expression is not null
                        || !defaultsByOrdinal.TryAdd(
                            parameterDefault.ParameterOrdinal,
                            parameterDefault))
                    {
                        throw Unsupported(
                            "CM1 MethodDef defaults require unique closed scalar values at valid formal ordinals.");
                    }

                    previousDefaultOrdinal
                        = parameterDefault.ParameterOrdinal;
                }
            }

            for (var parameter = 0; parameter < function.ParameterCount; parameter++)
            {
                var variable = function.Variables[parameter];
                var isContextualMethodImport
                    = hasContextualTypeViews
                        && function.ModuleImport is not null;
                var nativeOutput = DirectNativeImportContract.OutputForParameter(function, parameter)
                    ?? NativeVTableCallContract.OutputForParameter(function, parameter);
                var nativeParameter = function.NativeVTableMethod is not null
                    ? NativeVTableCallContract.ParameterForParameter(function, parameter)
                    : DirectNativeImportContract.ParameterForParameter(function, parameter);
                var isNativeOutput = nativeOutput is not null;
                var isNativeCallableValue = function.NativeFunctionPointerType is not null
                    && NativeFunctionPointerContract.IsSupportedValueType(variable.Type,
                        program.NativeFunctionPointerSignatures);
                var isNativeFunctionPointerValue = NativeFunctionPointerTypes.IsType(variable.Type);
                var isNativePointerValue = NativePointerTypes.IsType(variable.Type);
                var invalidNativeParameter = nativeParameter is not null
                    && !DirectNativeImportContract.MatchesNativeParameterFormalType(
                        variable.Type, nativeParameter.PointeeType,
                        nativeParameter.Access, program.NativePointerTypes);
                var initializationReferenceParameter
                    = standardInitializationReferenceBodies
                        && IsStandardInitializationReferenceParameter(
                            variable, program);
                var contextualParameterTypeRow
                    = ContextualMethodTypeSpecRow(index, parameter,
                        variable.Type);
                uint parameterType;
                if (contextualParameterTypeRow != 0)
                {
                    parameterType
                        = checked((uint)contextualParameterTypeRow);
                }
                else if (isContextualMethodImport
                    && (variable.Type == PrimitiveType.Void
                        || TryGetC1M1SourceScalarTypeSpecReference(
                            variable.Type, out _)))
                {
                    parameterType
                        = variable.Type == PrimitiveType.Void
                            ? 0xF200_0000u
                            : GetC1M1SourceScalarTypeSpecReference(
                                variable.Type);
                }
                else if (schema.UsesClassParameterDefaults()
                    && TryGetC1M1SourceScalarTypeSpecReference(
                        variable.Type, out _))
                {
                    parameterType = GetC1M1SourceScalarTypeSpecReference(
                        variable.Type);
                }
                else
                {
                    var parameterTypeRow = isNativeOutput
                        ? NativeOutputTypeRow(typeRowsList,
                            nativeOutputTypeRows,
                            nativeOutput!.PointeeType)
                        : NativeValueTypeRow(variable.Type,
                            nativeRawPointerTypeRow,
                            nativeFunctionPointerTypeRows, typeRowsList,
                            nativePointerTypeRows, program.NativePointerTypes,
                            structureTypeDefRows, typeDefTypeSpecRows,
                            TypeSpecRowFor);
                    parameterType = checked((uint)parameterTypeRow);
                }

                if ((variable.Type == PrimitiveType.Void
                        && !isContextualMethodImport)
                    || variable.IsInitializationLocal
                    || (function.DirectNativeImport is null && !hasNativeVTable
                        && !outputFeatures[index]
                        && !initializationReferenceParameter
                            && ((!IsScalarValueType(variable.Type)
                                    && variable.Type != PrimitiveType.Void
                                    && !IsConstruction2CopyValueType(
                                        variable.Type)
                                    && contextualParameterTypeRow == 0
                                    && !hasRegisteredQueryBody
                                    && !isNativeCallableValue
                                && !isNativeFunctionPointerValue && !isNativePointerValue)
                            || variable.IsOut != variable.IsInit
                            || (variable.IsInit && !IsOutputScalarType(variable.Type))))
                    || (hasNativeVTable
                        && (isNativeOutput
                            ? variable.Type != PrimitiveType.NativeStorageAddress
                                || !variable.IsOut || variable.IsInit
                            : variable.IsOut || variable.IsInit
                                || (!IsScalarValueType(variable.Type)
                                    && variable.Type != PrimitiveType.NativeRawPointer
                                    && !isNativePointerValue
                                    && !NativeFunctionPointerTypes.IsType(variable.Type))))
                    || (function.DirectNativeImport is not null
                        && (isNativeOutput
                            ? variable.Type != PrimitiveType.NativeStorageAddress
                                || !variable.IsOut || variable.IsInit
                            : nativeParameter is not null
                                ? variable.IsOut || variable.IsInit
                                : (!IsScalarValueType(variable.Type)
                                        && variable.Type != PrimitiveType.NativeRawPointer
                                        && !isNativePointerValue
                                        && !isNativeFunctionPointerValue)
                                    || variable.IsOut || variable.IsInit))
                    || invalidNativeParameter)
                {
                    throw Unsupported($"Function '{function.Name}' has a non-value scalar parameter.");
                }

                parameters.Add(new TrRmParamRow(index + 1, parameter,
                    parameterType,
                    variable.IsInit && function.DirectNativeImport is null
                        && !initializationReferenceParameter ? (byte)2 : (byte)0));
                if (schema.UsesClassParameterDefaults())
                {
                    var defaultConstantSourceRow = 0u;
                    if (defaultsByOrdinal.TryGetValue(parameter,
                            out var parameterDefault))
                    {
                        var value = (BoundConstant)parameterDefault.Value!;
                        if (value.Type != variable.Type
                            || value.RepresentationType != variable.Type
                            || variable.Type is PrimitiveType.Void
                                or PrimitiveType.NativeInt
                                or PrimitiveType.NativeUInt
                            || !value.Bits.IsCanonicalFor(variable.Type)
                            || !TryGetC1M1SourceScalarTypeSpecReference(
                                variable.Type, out _))
                        {
                            throw Unsupported(
                                "CM1 MethodDef defaults require exact fixed-width scalar values.");
                        }

                        var frozenValue
                            = predicateFrozenScalarValues.Add(value);
                        if (frozenValue.TypeSpecRef != parameterType)
                        {
                            throw Unsupported(
                                "A CM1 default must retain the exact MethodSig parameter TypeSpecRef.");
                        }

                        defaultConstantSourceRow
                            = frozenValue.ConstantSourceRow;
                    }

                    methodParameterSurfaceRows.Add(
                        new C1M1ParameterSurfaceRow(index + 1,
                            parameter, variable.Name,
                            defaultConstantSourceRow));
                }
            }

            var isContextualMethodImport
                = hasContextualTypeViews
                    && function.ModuleImport is not null;
            uint resultTypeSpecRef;
            if ((isContextualMethodImport
                    || schema.UsesClassParameterDefaults())
                && function.ReturnType == PrimitiveType.Void)
            {
                resultTypeSpecRef = 0xF200_0000u;
            }
            else if ((isContextualMethodImport
                    || schema.UsesClassParameterDefaults())
                && TryGetC1M1SourceScalarTypeSpecReference(
                    function.ReturnType, out _))
            {
                resultTypeSpecRef = GetC1M1SourceScalarTypeSpecReference(
                    function.ReturnType);
            }
            else
            {
                var resultTypeRow = function.DirectNativeImport is not null
                    && function.ReturnType == PrimitiveType.NativeRawPointer
                        ? NativeParameterTypeRow(typeRowsList,
                            nativeParameterTypeRows, PrimitiveType.Void)
                        : NativeValueTypeRow(function.ReturnType,
                            nativeRawPointerTypeRow,
                            nativeFunctionPointerTypeRows, typeRowsList,
                            nativePointerTypeRows,
                            program.NativePointerTypes,
                            structureTypeDefRows, typeDefTypeSpecRows,
                            TypeSpecRowFor);
                resultTypeSpecRef = checked((uint)resultTypeRow);
            }
            signatures.Add(new TrRmMethodSigRow(resultTypeSpecRef, 0,
                first, function.ParameterCount, 0, 0));
            var bodyRow = 0;
            if (!function.IsExternal && !hasNativeVTable)
            {
                bodyRow = bodies.Count + 1;
                if (standardNativePointerLoads
                    && TryGetStandardNativePointerLoadRead(function, program,
                        out var pointerLoadBody))
                {
                    bodies.Add(EncodeStandardNativePointerLoadBody(function,
                        program, pointerLoadBody,
                        type => checked((uint)TypeSpecRowFor(type)),
                        MethodMapTokenForFunction, schema));
                }
                else if (function.IndexedBody is TrRmScalarGraph indexedBody)
                {
                    bodies.Add(IndexedBuilder.Encode(indexedBody, function,
                        program, functionFeatures[index].HasFloatingTypes,
                        nativeRawPointerTypeRow,
                        nativeFunctionPointerTypeRows,
                        nativePointerTypeRows));
                }
                else if (hasRegisteredQueryBody)
                {
                    if (schema is not TrRmArtifactSchema.Standard
                        and not TrRmArtifactSchema.RegisterExceptionRegionsDraft)
                    {
                        throw Unsupported(
                            $"Function '{function.Name}' requires the Standard LayoutQueries schema.");
                    }

                    registeredQueryBodyCount++;
                    bodies.Add(EncodeStandardRegisteredQueryBody(function,
                        program, schema, typeRowsList, nativeRawPointerTypeRow,
                        nativeFunctionPointerTypeRows, nativePointerTypeRows,
                        structureTypeDefRows, typeDefTypeSpecRows,
                        structureFieldFirst, fieldPathRows,
                        standardFieldImportRows));
                }
                else if (standardInitializationReferenceBodies
                    && foreignInterfaceCallFeatures[index])
                {
                    bodies.Add(EncodeStandardInitializationReferenceBody(
                        function, program, TypeSpecRowFor,
                        MethodMapTokenForFunction, FieldDefRowFor,
                        FieldPathRowFor,
                        foreignInterfaceMethodMapTokenForCall
                            ?? throw Unsupported(
                                "The F+I Body writer requires exact InterfaceMethod MethodMap selection."),
                        predicateFrozenScalarValues, schema));
                }
                else if (outputFeatures[index])
                {
                    bodies.Add(standardInitializationReferenceBodies
                        ? EncodeStandardInitializationReferenceBody(
                            function, program, TypeSpecRowFor,
                            MethodMapTokenForFunction, FieldDefRowFor,
                            FieldPathRowFor,
                            foreignInterfaceMethodMapTokenForCall,
                            predicateFrozenScalarValues,
                            schema)
                        : EncodeOutputBody(function, program, typeRowsList,
                            nativeParameterTypeRows, nativeOutputTypeRows,
                            nativeFunctionPointerTypeRows,
                            nativePointerTypeRows, structureTypeDefRows,
                            typeDefTypeSpecRows, nativeRawPointerTypeRow));
                }
                else if (functionFeatures[index].HasIndexedFlow)
                {
                    bodies.Add(new IndexedBuilder(function, program,
                        functionFeatures[index].HasFloatingTypes,
                        nativeRawPointerTypeRow,
                        nativeFunctionPointerTypeRows,
                        nativePointerTypeRows).Encode());
                }
                else
                {
                    bodies.Add(EncodeBody(function,
                        functionFeatures[index].HasFloatingTypes,
                        nativeRawPointerTypeRow,
                        nativeFunctionPointerTypeRows,
                        nativePointerTypeRows));
                }
            }

            var effectMask = schema.UsesDevelopmentFunctionPointerEffects()
                && (function.DirectNativeImport is not null
                    || function.NativeFunctionPointerType is not null)
                ? NativeFunctionPointerContract.GetDeclaredEffectsMask(
                    function.DoesNotReturn, function.DeclaredRequiresUnsafeCaller)
                : (function.DoesNotReturn ? 2u : 0u)
                    | (function.RequiresUnsafeCaller ? 4u : 0u);
            var standardOutputSelectors
                = ImmutableArray<NativeFunctionPointerSelector>.Empty;
            var standardOutputStates
                = ImmutableArray<NativeFunctionPointerOutputState>.Empty;
            var generalOutputStatePredicates
                = function.CheckedOutputStatePredicates.IsDefault
                    ? [] : function.CheckedOutputStatePredicates;
            if (standardInitializationReferenceBodies)
            {
                (standardOutputSelectors, standardOutputStates)
                    = GetStandardInitializationOutputStateVectors(
                        function, program);
            }
            var contractFirst = effectMask != 0 || function.DirectNativeImport is not null
                || function.NativeFunctionPointerType is not null || hasNativeVTable
                || !standardOutputSelectors.IsEmpty
                || !generalOutputStatePredicates.IsEmpty
                || !function.DeclarationContractClauses.IsEmpty
                    ? contracts.Count + 1 : 0;
            if (effectMask != 0)
            {
                contracts.Add(new TrRmContractRow(0x1600_0000u | (uint)(index + 1), 1,
                    s_effectPayloads[effectMask]));
            }
            if (emitsConstruction2DeclarationPredicateGraphs
                && !function.DeclarationContractClauses.IsEmpty)
            {
                AddGeneralDeclarationContractGraphs(function,
                    0x1600_0000u | checked((uint)(index + 1)));
            }
            if (function.DirectNativeImport is DirectNativeImport nativeImport)
            {
                if (!standardNativePointerLoads)
                {
                    contracts.Add(new TrRmContractRow(
                        0x1600_0000u | (uint)(index + 1), 0x10,
                        NativeAbiPayload(1, NativeAbiConvention(schema,
                            nativeImport.CallingConvention, 1))));
                }

                foreach (var output in nativeImport.Outputs)
                {
                    if (standardNativeParameterProfile)
                    {
                        throw Unsupported(
                            "The pointer-load NativeParameter successors do not select NativeOutput contracts.");
                    }

                    contracts.Add(new TrRmContractRow(0x1600_0000u | (uint)(index + 1), 0x12,
                        NativeOutputPayload(output.ParameterIndex, output.ResultCondition,
                            output.FailureDisposition)));
                }
                foreach (var parameter in nativeImport.Parameters)
                {
                    if (schema.UsesNativeParameterContracts())
                    {
                        contracts.Add(new TrRmContractRow(
                            0x1600_0000u | (uint)(index + 1), 0x0E,
                            StandardNativeParameterContractPayload(
                                parameter.ParameterIndex, parameter.Access,
                                parameter.Retention,
                                checked((uint)parameter.Extent))));
                    }
                    else if (standardNativePointerLoads)
                    {
                        throw Unsupported(
                            "A pointer-load Body with NativeParameter clauses requires an exact NativeParameter successor.");
                    }
                    else
                    {
                        contracts.Add(new TrRmContractRow(
                            0x1600_0000u | (uint)(index + 1), 0x11,
                            NativeParameterPayload(parameter.ParameterIndex,
                                (byte)((byte)parameter.Access + 1),
                                (byte)parameter.Retention,
                                checked((uint)parameter.Extent))));
                    }
                }
            }
            else if (function.NativeFunctionPointerType is not null
                && standardNativeCallableEmission)
            {
                if (function.NativeCallableConvention is not
                    NativeCallingConvention convention)
                {
                    throw Unsupported(
                        $"Function '{function.Name}' has no selected NativeCallable convention.");
                }

                nativeCallableContractRows[index] = contracts.Count + 1;
                contracts.Add(new TrRmContractRow(
                    0x1600_0000u | (uint)(index + 1), 0x000C,
                    StandardNativeCallableContractPayload(convention)));
            }
            else if (function.NativeFunctionPointerType is not null)
            {
                contracts.Add(new TrRmContractRow(0x1600_0000u | (uint)(index + 1), 0x10,
                    NativeAbiPayload(2, NativeAbiConvention(schema,
                        function.NativeCallableConvention, 2))));
            }
            else if (hasNativeVTable)
            {
                if (schema.UsesDevelopmentNativeConventions()
                    && function.NativeCallableConvention is not null)
                {
                    throw Unsupported(
                        "A NativeVTable declaration cannot carry a NativeCallable convention.");
                }
                contracts.Add(new TrRmContractRow(0x1600_0000u | (uint)(index + 1), 0x10,
                    NativeAbiPayload(4)));
                var method = function.NativeVTableMethod!;
                foreach (var output in method.Outputs)
                {
                    contracts.Add(new TrRmContractRow(0x1600_0000u | (uint)(index + 1), 0x12,
                        NativeOutputPayload(output.ParameterIndex, output.ResultCondition,
                            output.FailureDisposition)));
                }
                foreach (var parameter in method.Parameters)
                {
                    contracts.Add(new TrRmContractRow(0x1600_0000u | (uint)(index + 1), 0x11,
                        NativeParameterPayload(parameter.ParameterIndex,
                            (byte)((byte)parameter.Access + 1), (byte)parameter.Retention,
                            (uint)parameter.Extent)));
                }
            }
            if (!standardOutputSelectors.IsEmpty)
            {
                AddStandardInitializationOutputStateContracts(
                    0x1600_0000u | checked((uint)(index + 1)),
                    standardOutputSelectors, standardOutputStates);
            }
            if (!generalOutputStatePredicates.IsEmpty)
            {
                AddGeneralInitializationOutputStateContracts(function,
                    0x1600_0000u | checked((uint)(index + 1)),
                    generalOutputStatePredicates,
                    standardOutputSelectors.Length);
            }

            var visibility = function.IsPrivate ? 0u : function.IsInternal ? 1u : 2u;
            var ownerTypeDefRow = 0;
            if (function.DeclaringStruct is string declaringStruct)
            {
                if (!structureIndexesByName.TryGetValue(declaringStruct,
                        out var structureIndexes)
                    || structureIndexes.Count == 0)
                {
                    throw Unsupported(
                        $"Function '{function.Name}' has no selected Standard TypeDef owner.");
                }

                var ownerIdentity = function.ModuleImport?.ProducerIdentity
                    ?? moduleIdentity;
                var ownerStructureIndexes = structureIndexes.Where(index =>
                        program.Structures[index].StandardTypeOrigin is
                            BoundStandardTypeOrigin origin
                            ? StringComparer.Ordinal.Equals(
                                origin.Provider.ModuleIdentity,
                                ownerIdentity)
                            : StringComparer.Ordinal.Equals(ownerIdentity,
                                moduleIdentity))
                    .ToArray();
                if (ownerStructureIndexes.Length == 0
                    && structureIndexes.Count == 1)
                {
                    ownerStructureIndexes = [structureIndexes[0]];
                }
                if (ownerStructureIndexes.Length != 1
                    || !structureTypeDefRows.TryGetValue(
                        ownerStructureIndexes[0],
                        out ownerTypeDefRow))
                {
                    throw Unsupported(
                        $"Function '{function.Name}' has no selected Standard TypeDef owner.");
                }
            }
            methods.Add(new TrRmMethodDefRow(ownerTypeDefRow, nameRows[index],
                index + 1, visibility,
                contractFirst, contractFirst == 0 ? 0 : contracts.Count - contractFirst + 1, bodyRow));
            if (visibility == 2 && function.ModuleImport is null)
            {
                exports.Add(new TrRmExportRow(0x1600_0000u | (uint)(index + 1), nameRows[index]));
            }
        }

        var nativeFunctionPointerSignatureRows =
            new List<TrRmNativeFunctionPointerSignatureRow>(
                program.NativeFunctionPointerSignatures.Length);
        for (var index = 0;
            !standardNativeCallableEmission
            && index < program.NativeFunctionPointerSignatures.Length; index++)
        {
            var signature = program.NativeFunctionPointerSignatures[index];
            var methodRow = methods.Count + 1;
            if (standardTerrumFunctionPointerProfile
                && methodRow != program.Functions.Length + index + 1)
            {
                throw Unsupported(
                    "Terrum function-pointer anchors do not follow the selected MethodDef layout.");
            }
            var first = signature.ParameterTypes.IsEmpty ? 0 : parameters.Count + 1;
            foreach (var parameterType in signature.ParameterTypes)
            {
                parameters.Add(new TrRmParamRow(methodRow, parameters.Count - first + 1,
                    checked((uint)NativeValueTypeRow(parameterType, nativeRawPointerTypeRow,
                        nativeFunctionPointerTypeRows, typeRowsList, nativePointerTypeRows,
                        program.NativePointerTypes, structureTypeDefRows,
                        typeDefTypeSpecRows, TypeSpecRowFor)), 0));
            }

            signatures.Add(new TrRmMethodSigRow(
                checked((uint)NativeValueTypeRow(signature.ReturnType, nativeRawPointerTypeRow,
                    nativeFunctionPointerTypeRows, typeRowsList, nativePointerTypeRows,
                    program.NativePointerTypes, structureTypeDefRows,
                    typeDefTypeSpecRows, TypeSpecRowFor)),
                0, first, signature.ParameterTypes.Length, 0, 0));
            var contractFirst = contracts.Count + 1;
            var anchorEffectsMask = schema.UsesDevelopmentFunctionPointerEffects()
                ? signature.EffectsMask : 4u;
            if (anchorEffectsMask != 0)
            {
                contracts.Add(new TrRmContractRow(0x1600_0000u | (uint)methodRow, 1,
                    s_effectPayloads[anchorEffectsMask]));
            }
            if (!standardTerrumFunctionPointerProfile
                || signature.CallingConvention
                    != NativeCallingConvention.Native)
            {
                contracts.Add(new TrRmContractRow(
                    0x1600_0000u | (uint)methodRow, 0x10,
                    NativeAbiPayload(3, NativeAbiConvention(schema,
                        signature.CallingConvention, 3))));
            }
            var nameRow = names.Count + 1;
            names.Add(($"__trrm_fnptr_signature_{index}", 1));
            methods.Add(new TrRmMethodDefRow(0, nameRow, signatures.Count, 0,
                contractFirst, contracts.Count - contractFirst + 1, 0));
            nativeFunctionPointerSignatureRows.Add(
                new TrRmNativeFunctionPointerSignatureRow(methodRow));
        }
        var nativeFunctionPointerSignatureTableCount
            = schema.IsDevelopmentProfile()
                ? nativeFunctionPointerSignatureRows.Count : 0;

        AddStandardLayoutQuerySourceExports(program, sourceTypeExports,
            schema.UsesInitializationReferenceLocalFieldLayout()
                || schema.UsesConstructionInitializerCopyEvidence(),
            structureTypeDefRows, structureFieldFirst, names, exports,
            requireDeclaredCopyEvidence:
                schema.UsesConstructionInitializerCopyEvidence());
        var methodParameterSurfaceStringRows = new Dictionary<(
            int MethodDefRow, int Ordinal), int>();
        foreach (var parameterSurface in methodParameterSurfaceRows)
        {
            var nameStringRow = names.Count + 1;
            names.Add((parameterSurface.Name, 1));
            if (!methodParameterSurfaceStringRows.TryAdd((
                    parameterSurface.MethodDefRow,
                    parameterSurface.Ordinal), nameStringRow))
            {
                throw Invalid(
                    "A MethodParameterSurface contains duplicate formal rows.");
            }
        }

        var stringsBytes = names.Any(static name => name.Text.Length == 0)
            ? TrRmStringTableWriter.EncodeTextRows(names)
            : TrRmStringTableWriter.EncodeNames(names);
        var strings = new TrRmStringTableReader(stringsBytes, names.Count);
        var hasOutputFeatures = outputFeatures.Any(static enabled => enabled);
        var requiresStandardNativeOutputCells
            = schema.UsesForeignNativeInterface()
                && !program.Interfaces.IsDefaultOrEmpty
                && program.Interfaces.Any(static definition =>
                    !definition.Members.IsDefaultOrEmpty
                    && definition.Members.Any(static member =>
                        !member.NativeOutputContracts.IsEmpty));
        var featureRows = new List<TrRmDevelopmentFeatureRow>(8);
        var standardInitializationBodyFeatureProfile
            = standardInitializationReferenceBodies
                && ((schema is TrRmArtifactSchema.Standard
                        or TrRmArtifactSchema.InitializationReferenceLocalFieldLayoutDraft
                        or TrRmArtifactSchema.InitializationReferenceLocalFieldLayoutNestedFieldDestinationsDraft)
                    || schema.UsesForeignNativeInterface()
                    || schema.UsesGeneralPredicateGraphs()
                    || schema
                        == TrRmArtifactSchema.InitializationReferencePointerLoadNativeParameterDraft);
        var requiresStandardNativeBoundaryContracts = false;
        var requiresStandardRootlessNativeValues = false;
        var requiresStandardDirectNativeCalls = false;
        bool HasAC1DeclaredCopyEvidence(BoundStruct structure)
            => structure.CleanupMethod is null
                && (structure.StandardTypeOrigin is null
                    ? !structure.IsUnique
                    : structure.DeclaredCopyEvidence == true);
        var requiresStandardForeignNativeInterfaceAggregateLayoutClosure
            = schema.UsesForeignNativeInterfaceAggregateOutputConsumption()
                && !blittableStructureIndexes.IsEmpty;
        var requiresStandardDeclaredCopyEvidence
            = schema.UsesInitializationReferenceLocalFieldLayout()
                && typeDefStructureIndexes.Any(index =>
                    program.Structures[index].BlittableTargetRequirement
                        == FixedLayoutStorage.WindowsX64TargetRequirement
                    && !program.Structures[index].IsUnique
                    && program.Structures[index].CleanupMethod is null
                    && (program.Structures[index].IsSequential
                        || program.Structures[index].IsTransparent))
            || (schema == TrRmArtifactSchema
                    .ForeignNativeInterfaceAggregateOutputConsumptionDraft
                && typeDefStructureIndexes.Any(index =>
                    HasAC1DeclaredCopyEvidence(
                        program.Structures[index])));
        if (requiresStandardDeclaredCopyEvidence)
        {
            requiresStandardNativeBoundaryContracts = true;
            requiresStandardRootlessNativeValues = true;
        }
        if (hasOutputFeatures && !standardInitializationReferenceBodies)
        {
            featureRows.Add(TrRmDevelopmentFeatureRow.ScalarInitOutput);
        }
        if (nativeImportCount != 0 || nativeCallableCount != 0 || nativeVTableCount != 0
            || nativeFunctionPointerSignatureRows.Count != 0
            || nativeRawPointerTypeRow != 0
            || !program.NativePointerTypes.IsEmpty
            || !blittableStructureIndexes.IsEmpty)
        {
            if (standardInitializationBodyFeatureProfile
                || standardTerrumFunctionPointerProfile)
            {
                requiresStandardNativeBoundaryContracts = true;
            }
            else
            {
                featureRows.Add(
                    TrRmDevelopmentFeatureRow.NativeBoundaryContracts);
            }
        }
        var hasNativeFunctionPointerBodyOperations = program.Functions
            .Any(HasNativeFunctionPointerBodyOperation);
        if (standardNativeCallableEmission
            && (nativeImportCount != 0 || nativeVTableCount != 0
                || hasNativeFunctionPointerBodyOperations
                || outputFeatures.Any(static enabled => enabled)
                || program.Functions.Any(static function =>
                    function.NativeCallableEntryName is not null)))
        {
            throw Unsupported(
                "The current Standard NativeExport source route supports callable entries without imports, vtable contracts, output state or pointer-value Bodies.");
        }
        var hasRootlessNativeValues = nativeImportCount != 0 || nativeCallableCount != 0
            || nativeVTableCount != 0
            || nativeFunctionPointerSignatureRows.Count != 0
            || nativeRawPointerTypeRow != 0
            || !program.NativePointerTypes.IsEmpty
            || hasNativeFunctionPointerBodyOperations
            || Enumerable.Range(0, program.Functions.Length).Any(index =>
                outputFeatures[index] && !program.Functions[index].IsExternal
                    && HasRootlessNativeValues(program.Functions[index]));
        if (hasRootlessNativeValues)
        {
            if (standardInitializationBodyFeatureProfile
                || standardTerrumFunctionPointerProfile)
            {
                requiresStandardRootlessNativeValues = true;
            }
            else
            {
                featureRows.Add(
                    TrRmDevelopmentFeatureRow.RootlessNativeValues);
            }
        }
        if (nativeImportCount != 0 || nativeCallableContractCount != 0
            || hasNativeFunctionPointerBodyOperations)
        {
            if (standardInitializationBodyFeatureProfile
                && standardNativePointerLoads)
            {
                requiresStandardDirectNativeCalls = true;
            }
            else
            {
                featureRows.Add(TrRmDevelopmentFeatureRow.DirectNativeCalls);
            }
        }
        if (nativeCallableContractCount != 0 || hasNativeFunctionPointerBodyOperations)
        {
            featureRows.Add(TrRmDevelopmentFeatureRow.NativeCallableEntries);
        }
        if (nativeVTableCount != 0)
        {
            featureRows.Add(TrRmDevelopmentFeatureRow.NativeVTableCalls);
        }

        var featureCount = featureRows.Count;
        byte[] featureBytes;
        var hasNativeParameters = standardNativeParameterProfile
            && program.Functions.Any(static function =>
                function.DirectNativeImport is DirectNativeImport nativeImport
                && !nativeImport.Parameters.IsDefaultOrEmpty);
        if (standardNativeCallableEmission)
        {
            var standardFeatures = new List<TrRmFeatureRow>(4) {
                TrRmFeatureRow.NativeBoundaryContracts,
            };
            if (registeredQueryBodyCount != 0
                || fieldPathRows.Count != 0
                || standardSourceImportMappings.Any(
                    static import =>
                        (import.ExpectedDefinitionId >> 24) == 0x14)
                || sourceTypeExports?.Any(static export =>
                    !export.FieldExports.IsDefaultOrEmpty) == true)
            {
                standardFeatures.Add(TrRmFeatureRow.LayoutQueries);
            }
            if (standardNativeExportEmission)
            {
                standardFeatures.Add(TrRmFeatureRow.RootlessNativeValues);
                standardFeatures.Add(TrRmFeatureRow.DirectNativeCalls);
                standardFeatures.Add(TrRmFeatureRow.NativeExportEntries);
            }
            featureCount = standardFeatures.Count;
            featureBytes = TrRmFeatureTableWriter.Encode(standardFeatures,
                TrRmArtifactSchema.Standard);
        }
        else if ((schema is TrRmArtifactSchema.Standard
                or TrRmArtifactSchema.InitializationReferenceLocalFieldLayoutDraft
                or TrRmArtifactSchema.InitializationReferenceLocalFieldLayoutNestedFieldDestinationsDraft
                or TrRmArtifactSchema.ForeignNativeInterfaceDraft
                or TrRmArtifactSchema.ForeignNativeInterfaceParameterSurfaceDraft
                or TrRmArtifactSchema.ForeignNativeInterfaceAggregateOutputConsumptionDraft)
            && (registeredQueryBodyCount != 0
                || fieldPathRows.Count != 0
                || standardSourceImportMappings.Any(
                    static import =>
                        (import.ExpectedDefinitionId >> 24) == 0x14)
                || sourceTypeExports?.Any(static export =>
                    !export.FieldExports.IsDefaultOrEmpty) == true
                || predicateGraphs.Count != 0
                || requiresStandardNativeBoundaryContracts
                || requiresStandardRootlessNativeValues
                || requiresStandardDirectNativeCalls
                || requiresStandardNativeOutputCells
                || requiresStandardForeignNativeInterfaceAggregateLayoutClosure))
        {
            var standardFeatures = new List<TrRmFeatureRow>(6);
            if (requiresStandardNativeBoundaryContracts)
            {
                standardFeatures.Add(
                    TrRmFeatureRow.NativeBoundaryContracts);
            }
            if (requiresStandardRootlessNativeValues)
            {
                standardFeatures.Add(TrRmFeatureRow.RootlessNativeValues);
            }
            if (requiresStandardDirectNativeCalls)
            {
                standardFeatures.Add(TrRmFeatureRow.DirectNativeCalls);
            }
            if (requiresStandardNativeOutputCells)
            {
                standardFeatures.Add(
                    TrRmFeatureRow.NativeOutputCellsCandidate);
            }
            if (predicateGraphs.Count != 0)
            {
                standardFeatures.Add(TrRmFeatureRow.PublishedPredicates);
            }
            if (registeredQueryBodyCount != 0
                || fieldPathRows.Count != 0
                || standardSourceImportMappings.Any(
                    static import =>
                        (import.ExpectedDefinitionId >> 24) == 0x14)
                || sourceTypeExports?.Any(static export =>
                    !export.FieldExports.IsDefaultOrEmpty) == true
                || requiresStandardForeignNativeInterfaceAggregateLayoutClosure)
            {
                standardFeatures.Add(TrRmFeatureRow.LayoutQueries);
            }
            featureCount = standardFeatures.Count;
            featureBytes = TrRmFeatureTableWriter.Encode(standardFeatures,
                schema);
        }
        else if (standardNativeParameterProfile
            && schema
                == TrRmArtifactSchema.InitializationReferencePointerLoadNativeParameterDraft
            && standardInitializationReferenceBodies)
        {
            if (featureRows.Count != 0)
            {
                throw Unsupported(
                    "The pointer-load NativeParameter profile does not compose development-only feature profiles.");
            }

            var nativeParameterFeatures = new List<TrRmFeatureRow>(5);
            if (requiresStandardNativeBoundaryContracts)
            {
                nativeParameterFeatures.Add(
                    TrRmFeatureRow.NativeBoundaryContracts);
            }
            if (requiresStandardRootlessNativeValues)
            {
                nativeParameterFeatures.Add(
                    TrRmFeatureRow.RootlessNativeValues);
            }
            if (requiresStandardDirectNativeCalls)
            {
                nativeParameterFeatures.Add(TrRmFeatureRow.DirectNativeCalls);
            }
            if (predicateGraphs.Count != 0)
            {
                nativeParameterFeatures.Add(TrRmFeatureRow.PublishedPredicates);
            }
            if (hasNativeParameters)
            {
                nativeParameterFeatures.Add(
                    TrRmFeatureRow.ForeignInvocationContractsCandidate);
            }

            featureCount = nativeParameterFeatures.Count;
            featureBytes = featureCount == 0 ? []
                : TrRmFeatureTableWriter.Encode(nativeParameterFeatures,
                    schema);
        }
        else if (schema.UsesGeneralPredicateGraphs()
            && standardInitializationReferenceBodies)
        {
            if (featureRows.Count != 0)
            {
                throw Unsupported(
                    "The GeneralPredicateGraphsDraft initialization profile does not compose development-only feature profiles.");
            }

            var generalFeatures = new List<TrRmFeatureRow>(4);
            if (requiresStandardNativeBoundaryContracts)
            {
                generalFeatures.Add(
                    TrRmFeatureRow.NativeBoundaryContracts);
            }
            if (requiresStandardRootlessNativeValues)
            {
                generalFeatures.Add(TrRmFeatureRow.RootlessNativeValues);
            }
            if (requiresStandardDirectNativeCalls)
            {
                generalFeatures.Add(TrRmFeatureRow.DirectNativeCalls);
            }
            if (hasNativeParameters)
            {
                generalFeatures.Add(
                    TrRmFeatureRow.ForeignInvocationContractsCandidate);
            }
            if (predicateFrozenScalarValues.FrozenValueNodeCount != 0)
            {
                generalFeatures.Add(TrRmFeatureRow.TypedFrozenValues);
            }
            if (methodParameterSurfaceRows.Count != 0)
            {
                generalFeatures.Add(
                    TrRmFeatureRow.MethodParameterLabelsCandidate);
            }
            if (predicateGraphs.Count != 0)
            {
                generalFeatures.Add(TrRmFeatureRow.PublishedPredicates);
            }
            if (predicateNodes.Any(static node => node.NodeKind > 6))
            {
                generalFeatures.Add(
                    TrRmFeatureRow.GeneralPredicateGraphsCandidate);
            }

            featureCount = generalFeatures.Count;
            featureBytes = featureCount == 0 ? []
                : TrRmFeatureTableWriter.Encode(generalFeatures, schema);
        }
        else if (!schema.IsDevelopmentProfile() && featureRows.Count != 0)
        {
            throw Unsupported(
                "The selected Standard schema does not support these development-only features.");
        }
        else
        {
            featureBytes = featureCount == 0
                ? [] : TrRmDevelopmentFeatureTableWriter.Encode(featureRows);
        }
        var standardFeatureTable
            = ((schema is TrRmArtifactSchema.Standard
                    or TrRmArtifactSchema.InitializationReferenceLocalFieldLayoutDraft
                    or TrRmArtifactSchema.InitializationReferenceLocalFieldLayoutNestedFieldDestinationsDraft
                    or TrRmArtifactSchema.ForeignNativeInterfaceDraft
                    or TrRmArtifactSchema.ForeignNativeInterfaceParameterSurfaceDraft
                    or TrRmArtifactSchema.ForeignNativeInterfaceAggregateOutputConsumptionDraft)
                || schema.UsesGeneralPredicateGraphs()
                || standardNativeParameterProfile)
                && featureCount != 0
                ? new TrRmFeatureTableReader(featureBytes, featureCount, schema)
                : default;
        var typeDefCount = typeDefStructureIndexes.Length;
        var moduleRefRows = new List<TrRmModuleRefRow>(moduleImportIdentities.Length);
        foreach (var identity in moduleImportIdentities)
        {
            var moduleImport = program.Functions
                .Select(static function => function.ModuleImport)
                .FirstOrDefault(import => import?.ProducerIdentity == identity);
            if (moduleImport is not null)
            {
                if (standardProviderSnapshots.TryGetValue(identity,
                        out var provider)
                    && (provider.TargetRequirement
                            != moduleImport.TargetRequirement
                        || provider.RuntimePolicy
                            != moduleImport.RuntimePolicy))
                {
                    throw Unsupported(
                        "A method and type Import disagree on their ModuleRef requirements.");
                }

                moduleRefRows.Add(new TrRmModuleRefRow(
                    moduleReferenceStringRows[identity],
                    moduleImport.TargetRequirement,
                    moduleImport.RuntimePolicy));
            }
            else if (standardProviderSnapshots.TryGetValue(identity,
                         out var provider))
            {
                moduleRefRows.Add(new TrRmModuleRefRow(
                    moduleReferenceStringRows[identity],
                    provider.TargetRequirement, provider.RuntimePolicy));
            }
            else
            {
                throw Unsupported(
                    "A ModuleRef has no retained method or Standard type provider origin.");
            }
        }
        var moduleBytes = TrRmModuleTableWriter.Encode(
            [new TrRmModuleRow(1, moduleTargetRequirement, 0, program.EntryPoint + 1, 1, 1, 1, 1,
                featureCount == 0 ? 0 : 1, featureCount)],
            strings, moduleRefRows.Count, typeDefCount, methods.Count, 0, featureCount);
        var modules = new TrRmModuleTableReader(moduleBytes, 1, strings, moduleRefRows.Count, typeDefCount,
            methods.Count, 0, featureCount);
        var moduleRefBytes = moduleRefRows.Count == 0 ? []
            : TrRmModuleRefTableWriter.Encode(moduleRefRows, strings, modules);
        var moduleRefs = moduleRefRows.Count == 0 ? default
            : new TrRmModuleRefTableReader(moduleRefBytes, moduleRefRows.Count, strings, modules);
        var typeRows = typeRowsList.ToArray();
        var typeBytes = TrRmTypeSpecTableWriter.Encode(typeRows, typeDefCount,
            pendingStandardImports.Count, 0,
            nativeFunctionPointerSignatureTableCount, schema,
            methodDefCount: methods.Count,
            standardSchemaIdentity: standardSchemaIdentity);
        var types = new TrRmTypeSpecTableReader(typeBytes, typeRows.Length,
            typeDefCount, pendingStandardImports.Count, 0,
            nativeFunctionPointerSignatureTableCount, schema,
            methodDefCount: methods.Count,
            standardSchemaIdentity: standardSchemaIdentity);
        var sigBytes = signatures.Count == 0 ? []
            : TrRmMethodSigTableWriter.Encode(signatures, types,
                parameters.Count, 0);
        var sigs = signatures.Count == 0 ? default
            : new TrRmMethodSigTableReader(sigBytes, signatures.Count, types,
                parameters.Count, 0);
        var paramBytes = parameters.Count == 0 ? []
            : TrRmParamTableWriter.Encode(parameters, sigs, types,
                schema);
        var methodParameterSurfaceBytes
            = new byte[checked(methodParameterSurfaceRows.Count * 16)];
        for (var surfaceIndex = 0;
            surfaceIndex < methodParameterSurfaceRows.Count;
            surfaceIndex++)
        {
            var surface = methodParameterSurfaceRows[surfaceIndex];
            var offset = surfaceIndex * 16;
            BinaryPrimitives.WriteUInt32LittleEndian(
                methodParameterSurfaceBytes.AsSpan(offset, 4),
                checked((uint)surface.MethodDefRow));
            BinaryPrimitives.WriteUInt32LittleEndian(
                methodParameterSurfaceBytes.AsSpan(offset + 4, 4),
                checked((uint)surface.Ordinal));
            BinaryPrimitives.WriteUInt32LittleEndian(
                methodParameterSurfaceBytes.AsSpan(offset + 8, 4),
                checked((uint)methodParameterSurfaceStringRows[(
                    surface.MethodDefRow, surface.Ordinal)]));
            BinaryPrimitives.WriteUInt32LittleEndian(
                methodParameterSurfaceBytes.AsSpan(offset + 12, 4),
                surface.DefaultConstantSourceRow);
        }
        var typeDefRows = new TrRmTypeDefRow[typeDefCount];
        for (var type = 0; type < typeDefStructureIndexes.Length; type++)
        {
            var structure = TypeDefStructure(type);
            var visibility = structure.IsPrivate ? 0u : structure.IsInternal ? 1u : 2u;
            var ac1DeclaredCopy
                = schema
                    == TrRmArtifactSchema.ForeignNativeInterfaceAggregateOutputConsumptionDraft
                && HasAC1DeclaredCopyEvidence(structure);
            var declaredCopy
                = ac1DeclaredCopy
                    || schema.UsesConstructionInitializerCopyEvidence()
                    && (structure.StandardTypeOrigin is null
                        ? !structure.IsUnique
                        : structure.DeclaredCopyEvidence == true)
                ? TrRmTypeDefTableReader.DeclaredCopyFlag
                : schema.UsesInitializationReferenceLocalFieldLayout()
                && structure.BlittableTargetRequirement
                    == FixedLayoutStorage.WindowsX64TargetRequirement
                && !structure.IsUnique && structure.CleanupMethod is null
                && (structure.IsSequential || structure.IsTransparent)
                    ? TrRmTypeDefTableReader.DeclaredCopyFlag : 0u;
            typeDefRows[type] = new TrRmTypeDefRow(structureNamespaceRows[type],
                structureNameRows[type], visibility | 4u | declaredCopy,
                structureFieldFirst[type], structure.Fields.Length, 0, 0,
                structureLayoutFirst[type], structureLayoutCount[type], 0, 0);
        }
        var typeDefBytes = typeDefRows.Length == 0 ? []
            : TrRmTypeDefTableWriter.Encode(typeDefRows, strings, modules, fieldRows.Count,
                0, layoutRows.Count, contracts.Count, schema);
        var typeDefs = new TrRmTypeDefTableReader(typeDefBytes, typeDefRows.Length,
            strings, modules, fieldRows.Count, 0, layoutRows.Count, contracts.Count,
            schema);
        var typeDefBindingEqualityRows
            = new List<TrRmTypeDefBindingEqualityRow>();
        if (IsSelectedTypeDefBindingEqualitySchemaIdentity(
                standardSchemaIdentity))
        {
            foreach (var structureIndex in typeDefStructureIndexSet.Order())
            {
                var origin = program.Structures[structureIndex]
                    .StandardTypeOrigin;
                if (origin is null
                    || origin.ContextualViewIndexes.Length < 2)
                {
                    continue;
                }

                var destinationRows = origin.ContextualViewIndexes
                    .Select(typeViewIndex =>
                        typeDefRowByContextualView.TryGetValue(
                            typeViewIndex, out var typeDefRow)
                            ? typeDefRow
                            : throw Invalid(
                                "A contextual TypeDef equality endpoint has no emitted row."))
                    .Order().ToArray();
                if (destinationRows.Distinct().Count()
                    != destinationRows.Length)
                {
                    throw Invalid(
                        "A contextual TypeDef equality group maps multiple views to one destination row.");
                }

                var leftTypeDef = checked((uint)destinationRows[0]);
                for (var index = 1; index < destinationRows.Length; index++)
                {
                    typeDefBindingEqualityRows.Add(
                        new TrRmTypeDefBindingEqualityRow(1,
                            leftTypeDef,
                            checked((uint)destinationRows[index])));
                }
            }
        }
        var targetProfileBytes = layoutRows.Count == 0
                && !schema.UsesForeignNativeInterface() ? []
            : TrRmTargetProfileTableWriter.Encode([new TrRmTargetProfileRow(1, 1)]);
        var targetProfiles = new TrRmTargetProfileTableReader(targetProfileBytes,
            targetProfileBytes.Length == 0 ? 0 : 1);
        var layoutBytes = layoutRows.Count == 0 ? []
            : TrRmLayoutTableWriter.Encode(layoutRows, typeDefs,
                targetProfiles, layoutFieldRows.Count);
        var layouts = new TrRmLayoutTableReader(layoutBytes, layoutRows.Count,
            typeDefs, targetProfiles, layoutFieldRows.Count);
        var fieldBytes = fieldRows.Count == 0 ? []
            : TrRmFieldDefTableWriter.Encode(fieldRows, strings, typeDefs, types,
                schema);
        var fields = fieldRows.Count == 0 ? default
            : new TrRmFieldDefTableReader(fieldBytes, fieldRows.Count, strings,
                typeDefs, types, schema);
        var layoutFieldBytes = layoutFieldRows.Count == 0 ? []
            : TrRmLayoutFieldTableWriter.Encode(layoutFieldRows, layouts, typeDefs, fields);
        var layoutFields = new TrRmLayoutFieldTableReader(layoutFieldBytes,
            layoutFieldRows.Count, layouts, typeDefs, fields);
        var methodBytes = methods.Count == 0 ? []
            : TrRmMethodDefTableWriter.Encode(methods, strings, modules,
                typeDefs, sigs, contracts.Count, bodies.Count, schema);
        var defs = methods.Count == 0 ? default
            : new TrRmMethodDefTableReader(methodBytes, methods.Count, strings,
                modules, typeDefs, sigs, contracts.Count, bodies.Count, schema);
        var paramReader = parameters.Count == 0 ? default
            : new TrRmParamTableReader(paramBytes, parameters.Count, sigs, types,
                schema);
        var contractBytes = contracts.Count == 0 ? []
            : schema.UsesDevelopmentNativeConventions()
                ? TrRmContractTableWriter.EncodeDevelopmentNativeConventionsDraft(
                    contracts, typeDefs, defs, sigs, paramReader, types)
            : schema.UsesDevelopmentFunctionPointerEffects()
                ? TrRmContractTableWriter.EncodeDevelopmentFunctionPointerEffectsDraft(
                    contracts, typeDefs, defs, sigs, paramReader, types)
                : schema.UsesGeneralPredicateGraphs()
                ? TrRmContractTableWriter
                    .EncodeGeneralPredicateGraphsDraft(contracts,
                        typeDefs, defs, sigs, paramReader, types,
                        fieldPathCount: 0,
                        features: standardFeatureTable, modules: modules,
                        predicateGraphCount: predicateGraphs.Count,
                        schema: schema)
                : schema.UsesNativeParameterContracts()
                ? TrRmContractTableWriter.EncodeForeignInvocationDraft(
                    contracts, typeDefs, defs, sigs, paramReader, types,
                    standardFeatureTable, modules,
                    predicateGraphCount: predicateGraphs.Count, schema: schema)
                : (schema is TrRmArtifactSchema.Standard
                    or TrRmArtifactSchema.InitializationReferenceLocalFieldLayoutDraft
                    or TrRmArtifactSchema.InitializationReferenceLocalFieldLayoutNestedFieldDestinationsDraft)
                && predicateGraphs.Count != 0
                ? TrRmContractTableWriter.EncodeWithPredicateGraphs(
                    contracts, typeDefs, defs, sigs, paramReader, types,
                    standardFeatureTable, modules, predicateGraphs.Count,
                    schema)
            : schema is TrRmArtifactSchema.Standard
                or TrRmArtifactSchema.InitializationReferenceLocalFieldLayoutDraft
                or TrRmArtifactSchema.InitializationReferenceLocalFieldLayoutNestedFieldDestinationsDraft
                    ? TrRmContractTableWriter.EncodeStandardProfile(contracts,
                        typeDefs, defs, sigs, paramReader, types, schema)
                : TrRmContractTableWriter.Encode(contracts, typeDefs, defs, sigs,
                    paramReader, types, developmentProfile: true);
        var contractReader = schema.UsesGeneralPredicateGraphs()
            || schema.UsesNativeParameterContracts()
            || predicateGraphs.Count != 0
            ? new TrRmContractTableReader(contractBytes, contracts.Count,
            typeDefs, defs, sigs, paramReader, types,
            fieldPathCount: 0, features: standardFeatureTable,
            modules: modules, schema: schema,
            predicateGraphCount: predicateGraphs.Count)
            : new TrRmContractTableReader(contractBytes, contracts.Count,
            typeDefs, defs, sigs, paramReader, types, schema);
        var moduleImportRows = new List<TrRmImportRow>(
            pendingStandardImports.Count);
        foreach (var pendingImport in pendingStandardImports)
        {
            var exportKeyRow = pendingImport.FunctionIndex >= 0
                ? nameRows[pendingImport.FunctionIndex]
                : pendingImport.ExportKeyStringRow;
            moduleImportRows.Add(new TrRmImportRow(
                pendingImport.ModuleRefRow, exportKeyRow,
                pendingImport.ExpectedDefinitionId));
        }
        var importBytes = moduleImportRows.Count == 0 ? []
            : (schema is (TrRmArtifactSchema.Standard
                or TrRmArtifactSchema.InitializationReferenceLocalFieldLayoutDraft
                or TrRmArtifactSchema.InitializationReferenceLocalFieldLayoutNestedFieldDestinationsDraft
                or TrRmArtifactSchema.InitializationReferencePointerLoadNativeParameterDraft)
                || IsC1M1MethodTypeSchemaIdentity(
                    standardSchemaIdentity ?? string.Empty))
                ? TrRmImportTableWriter.Encode(moduleImportRows, strings,
                    moduleRefs, typeDefs, defs, default, default, default,
                    fields, standardFeatureTable, modules, schema)
                : TrRmImportTableWriter.Encode(moduleImportRows, strings,
                    moduleRefs, typeDefs, defs, default);
        var imports = moduleImportRows.Count == 0 ? default
            : (schema is (TrRmArtifactSchema.Standard
                or TrRmArtifactSchema.InitializationReferenceLocalFieldLayoutDraft
                or TrRmArtifactSchema.InitializationReferenceLocalFieldLayoutNestedFieldDestinationsDraft
                or TrRmArtifactSchema.InitializationReferencePointerLoadNativeParameterDraft)
                || IsC1M1MethodTypeSchemaIdentity(
                    standardSchemaIdentity ?? string.Empty))
                ? new TrRmImportTableReader(importBytes,
                    moduleImportRows.Count, strings, moduleRefs, typeDefs,
                    defs, default, default, default, fields,
                    standardFeatureTable, modules, schema)
                : new TrRmImportTableReader(importBytes,
                    moduleImportRows.Count, strings, moduleRefs, typeDefs,
                    defs, default);
        var fieldPathBytes = fieldPathRows.Count == 0 ? []
            : TrRmFieldPathTableWriter.Encode(fieldPathRows, types, fields,
                imports);
        var exportBytes = exports.Count == 0 ? []
            : schema == TrRmArtifactSchema.Standard
                || IsC1M1MethodTypeSchemaIdentity(
                    standardSchemaIdentity ?? string.Empty)
                ? TrRmExportTableWriter.Encode(exports, strings, typeDefs, defs,
                    default, imports, default, default, fields,
                    standardFeatureTable, modules, schema)
                : TrRmExportTableWriter.Encode(exports, strings, typeDefs, defs,
                    default, imports);
        var nativeImportSectionId = (ushort)0x0030;
        byte[] nativeImportBytes;
        if (nativeImportCount == 0)
        {
            nativeImportBytes = [];
        }
        else if (schema.UsesDevelopmentNativeConventions())
        {
            var nativeImportRows =
                new List<TrRmDevelopmentNativeImportResolutionRow>(nativeImportCount);
            for (var index = 0; index < program.Functions.Length; index++)
            {
                if (program.Functions[index].DirectNativeImport
                    is DirectNativeImport nativeImport)
                {
                    nativeImportRows.Add(new TrRmDevelopmentNativeImportResolutionRow(
                        index + 1, nativeLibraryRows[index], nativeExportRows[index],
                        nativeImport.ResolutionMode, nativeImport.ResolutionPolicy));
                }
            }
            nativeImportBytes =
                TrRmDevelopmentNativeImportTableWriter.EncodeNativeConventionsDraft(
                    nativeImportRows, strings, defs, sigs, paramReader, types,
                    contractReader, layouts);
        }
        else if (standardNativePointerLoads)
        {
            var nativeImportRows = new List<TrRmNativeImportRow>(
                    nativeImportCount);
            for (var index = 0; index < program.Functions.Length; index++)
            {
                    if (program.Functions[index].DirectNativeImport
                        is DirectNativeImport nativeImport)
                    {
                        nativeImportRows.Add(new TrRmNativeImportRow(1,
                            (byte)nativeImport.ResolutionMode,
                            (byte)nativeImport.ResolutionPolicy,
                            (byte)nativeImport.CallingConvention, index + 1,
                            nativeLibraryRows[index], nativeExportRows[index]));
                    }
            }

            nativeImportBytes = TrRmNativeImportTableWriter.Encode(
                    nativeImportRows, strings, defs, fields, typeDefs, imports,
                    modules, sigs, paramReader, types, contractReader, layouts);
            nativeImportSectionId = 0x0021;
        }
        else
        {
            var nativeImportRows = new List<TrRmDevelopmentNativeImportRow>(nativeImportCount);
            for (var index = 0; index < program.Functions.Length; index++)
            {
                if (program.Functions[index].DirectNativeImport is not null)
                {
                    nativeImportRows.Add(new TrRmDevelopmentNativeImportRow(index + 1,
                        nativeLibraryRows[index], nativeExportRows[index], 1));
                }
            }
            nativeImportBytes = TrRmDevelopmentNativeImportTableWriter.Encode(
                nativeImportRows, strings, defs, sigs, paramReader, types,
                contractReader, layouts);
        }
        var nativeFunctionPointerSignatureBytes
            = nativeFunctionPointerSignatureTableCount == 0 ? []
            : TrRmNativeFunctionPointerSignatureTableWriter.Encode(
                nativeFunctionPointerSignatureRows, defs, sigs, paramReader, types,
                contractReader);
        var nativeFunctionPointerSignatureReader
            = nativeFunctionPointerSignatureTableCount == 0 ? default
                : new TrRmNativeFunctionPointerSignatureTableReader(
                    nativeFunctionPointerSignatureBytes,
                    nativeFunctionPointerSignatureTableCount,
                    defs, sigs, paramReader, types, contractReader);
        var nativeCallableRows = new List<TrRmNativeCallableRow>(nativeCallableCount);
        for (var index = 0; index < program.Functions.Length; index++)
        {
            if (!standardNativeCallableEmission
                && program.Functions[index].NativeFunctionPointerType is not null
                && !program.Functions[index].IsExternal)
            {
                nativeCallableRows.Add(new TrRmNativeCallableRow(index + 1,
                    nativeCallableEntryNameRows[index]));
            }
        }
        var nativeCallableBytes = nativeCallableRows.Count == 0 ? []
            : TrRmNativeCallableTableWriter.Encode(nativeCallableRows, strings, defs, sigs,
                paramReader, types, contractReader, nativeFunctionPointerSignatureReader, imports);
        var nativeExportTableRows = new List<TrRmNativeExportRow>();
        if (standardNativeExportEmission)
        {
            for (var index = 0; index < program.Functions.Length; index++)
            {
                var function = program.Functions[index];
                if (!function.HasNativeExport)
                {
                    continue;
                }

                if (nativeCallableContractRows[index] == 0)
                {
                    throw Unsupported(
                        $"Function '{function.Name}' has no Standard NativeCallable clause.");
                }

                nativeExportTableRows.Add(new TrRmNativeExportRow(1,
                    index + 1, nativeExportEntryNameRows[index],
                    nativeCallableContractRows[index]));
            }
        }
        var nativeExportTableBytes = nativeExportTableRows.Count == 0 ? []
            : TrRmNativeExportTableWriter.Encode(nativeExportTableRows,
                strings, defs, fields, typeDefs, imports, modules, sigs, types,
                contractReader, layouts);
        var nativeVTableRows = new List<TrRmNativeVTableRow>(nativeVTableCount);
        for (var index = 0; index < program.Functions.Length; index++)
        {
            if (program.Functions[index].NativeVTableMethod is NativeVTableMethodDefinition method)
            {
                nativeVTableRows.Add(new TrRmNativeVTableRow(index + 1, 0,
                    method.VTableByteOffset, method.Slot));
            }
        }
        var nativeVTableBytes = nativeVTableRows.Count == 0 ? []
            : TrRmNativeVTableTableWriter.Encode(nativeVTableRows, defs, sigs, paramReader,
                types, contractReader, layouts);
        var predicateGraphBytes = predicateGraphs.Count == 0 ? []
            : TrRmPredicateGraphTableWriter.Encode(predicateGraphs,
                predicateNodes.Count, predicateReadFootprints.Count);
        var predicateReadFootprintBytes
            = predicateReadFootprints.Count == 0 ? []
            : TrRmPredicateReadFootprintTableWriter.Encode(
                predicateReadFootprints, predicateReadRoots.Count, 0);
        var predicateReadRootBytes = predicateReadRoots.Count == 0 ? []
            : TrRmPredicateReadRootTableWriter.Encode(predicateReadRoots,
                0, 0, schema);
        var predicateNodeBytes = predicateNodes.Count == 0 ? []
            : TrRmPredicateNodeTableWriter.Encode(predicateNodes,
                predicateNodeOperands.Count, types, schema);
        var predicateCaptureBytes = predicateCaptures.Count == 0 ? []
            : TrRmPredicateCaptureTableWriter.Encode(predicateCaptures,
                predicateGraphs.Count, types);
        var predicateTypeUseBytes = predicateTypeUses.Count == 0 ? []
            : TrRmPredicateTypeUseTableWriter.Encode(predicateTypeUses,
                types, schema);
        var predicateFrozenScalarTables
            = predicateFrozenScalarValues.FrozenValueNodeCount == 0
                ? new TrRmFrozenScalarValueTables([], 0, [], 0, [], 0)
                : predicateFrozenScalarValues.Encode(types);
        var predicateTypePatternBytes = predicateTypePatterns.Count == 0 ? []
            : TrRmPredicateTypePatternTableWriter.Encode(
                predicateTypePatterns, predicateCaptures.Count,
                predicateFrozenScalarTables.ConstantSourceCount,
                predicateTypeUses.Count, schema);
        var predicateOperandCounts = new TrRmPredicateTableRowCounts(
            predicateReadRoots.Count,
            predicateFrozenScalarTables.FrozenValueNodeCount,
            predicateCaptures.Count,
            predicateOperations.Count,
            0, predicateTypeUses.Count, 0, 0, 0, 0,
            predicateTypePatterns.Count);
        var predicateNodeOperandBytes = predicateNodeOperands.Count == 0 ? []
            : TrRmPredicateNodeOperandTableWriter.Encode(
                predicateNodeOperands, predicateOperandCounts, schema);
        var predicateOperationBytes = predicateOperations.Count == 0 ? []
            : TrRmPredicateOperationTableWriter.Encode(
                predicateOperations, schema);
        var predicateGraphReader = predicateGraphs.Count == 0
            ? default
            : new TrRmPredicateGraphTableReader(predicateGraphBytes,
                predicateGraphs.Count, predicateNodes.Count,
                predicateReadFootprints.Count);
        var predicateChildEdgeBytes = predicateChildEdges.Count == 0 ? []
            : TrRmPredicateChildEdgeTableWriter.Encode(
                predicateChildEdges, predicateNodes.Count,
                predicateGraphReader);
        var sectionCount = 3 + (methods.Count != 0 ? 1 : 0)
            + (signatures.Count != 0 ? 1 : 0)
            + (featureCount != 0 ? 1 : 0)
            + (moduleRefRows.Count != 0 ? 1 : 0)
            + (typeDefRows.Length != 0 ? 1 : 0)
            + (fieldRows.Count != 0 ? 1 : 0) + (layoutRows.Count != 0 ? 1 : 0)
            + (parameters.Count != 0 ? 1 : 0)
            + (contracts.Count != 0 ? 1 : 0) + (exports.Count != 0 ? 1 : 0)
            + (moduleImportRows.Count != 0 ? 1 : 0)
            + (bodies.Count != 0 ? 1 : 0) + (nativeImportCount != 0 ? 1 : 0)
            + (nativeCallableRows.Count != 0 ? 1 : 0) + (nativeVTableRows.Count != 0 ? 1 : 0);
        sectionCount += nativeFunctionPointerSignatureTableCount != 0 ? 1 : 0;
        sectionCount += layoutFieldRows.Count != 0 ? 1 : 0;
        sectionCount += targetProfiles.Count != 0 ? 1 : 0;
        sectionCount += nativeExportTableRows.Count != 0 ? 1 : 0;
        sectionCount += fieldPathRows.Count != 0 ? 1 : 0;
        sectionCount += predicateFrozenScalarTables.BlobCount != 0 ? 1 : 0;
        sectionCount += predicateFrozenScalarTables.ConstantSourceCount
            != 0 ? 1 : 0;
        sectionCount += predicateFrozenScalarTables.FrozenValueNodeCount
            != 0 ? 1 : 0;
        sectionCount += predicateGraphs.Count == 0 ? 0 : 5;
        sectionCount += predicateChildEdges.Count == 0 ? 0 : 1;
        sectionCount += predicateCaptures.Count == 0 ? 0 : 1;
        sectionCount += predicateTypeUses.Count == 0 ? 0 : 1;
        sectionCount += predicateTypePatterns.Count == 0 ? 0 : 1;
        sectionCount += predicateOperations.Count == 0 ? 0 : 1;
        sectionCount += methodParameterSurfaceRows.Count == 0 ? 0 : 1;
        sectionCount += typeDefBindingEqualityRows.Count == 0 ? 0 : 1;
        var sections = new List<TrRmSection>(sectionCount)
        {
            new(1, 0, 1, moduleBytes),
            new(2, 0, (uint)names.Count, stringsBytes),
        };
        if (predicateFrozenScalarTables.BlobCount != 0)
        {
            sections.Add(new(3, 0,
                (uint)predicateFrozenScalarTables.BlobCount,
                predicateFrozenScalarTables.BlobSection));
        }
        if (featureCount != 0)
        {
            sections.Add(new(4, 0, (uint)featureCount, featureBytes));
        }
        if (moduleRefRows.Count != 0)
        {
            sections.Add(new(0x10, 0, (uint)moduleRefRows.Count, moduleRefBytes));
        }
        sections.Add(new(0x11, 0, (uint)typeRows.Length, typeBytes));
        if (typeDefRows.Length != 0)
        {
            sections.Add(new(0x13, 0, (uint)typeDefRows.Length, typeDefBytes));
        }
        if (fieldRows.Count != 0)
        {
            sections.Add(new(0x14, 0, (uint)fieldRows.Count, fieldBytes));
        }
        if (layoutRows.Count != 0)
        {
            sections.Add(new(0x15, 0, (uint)layoutRows.Count, layoutBytes));
        }
        if (methods.Count != 0)
        {
            sections.Add(new(0x16, 0, (uint)methods.Count, methodBytes));
        }
        if (signatures.Count != 0)
        {
            sections.Add(new(0x17, 0, (uint)signatures.Count, sigBytes));
        }
        if (parameters.Count != 0)
        {
            sections.Add(new(0x18, 0, (uint)parameters.Count, paramBytes));
        }
        if (contracts.Count != 0)
        {
            sections.Add(new(0x1B, 0, (uint)contracts.Count, contractBytes));
        }
        if (moduleImportRows.Count != 0)
        {
            sections.Add(new(0x1C, 0, (uint)moduleImportRows.Count, importBytes));
        }
        if (exports.Count != 0)
        {
            sections.Add(new(0x1D, 0, (uint)exports.Count, exportBytes));
        }
        if (bodies.Count != 0)
        {
            sections.Add(new(0x20, 0, (uint)bodies.Count, TrRmVariableTableWriter.EncodeBodies(bodies)));
        }
        if (nativeExportTableRows.Count != 0)
        {
            sections.Add(new(0x0022, 0,
                (uint)nativeExportTableRows.Count, nativeExportTableBytes));
        }
        if (layoutFieldRows.Count != 0)
        {
            sections.Add(new(0x23, 0, (uint)layoutFieldRows.Count, layoutFieldBytes));
        }
        if (nativeImportCount != 0)
        {
            sections.Add(new(nativeImportSectionId, 0,
                (uint)nativeImportCount, nativeImportBytes));
        }
        if (predicateFrozenScalarTables.ConstantSourceCount != 0)
        {
            if (nativeImportCount != 0
                && nativeImportSectionId == 0x0030)
            {
                throw Unsupported(
                    "CM1 parameter defaults cannot share the ConstantSource section with NativeImports.");
            }

            sections.Add(new(0x0030, 0,
                (uint)predicateFrozenScalarTables.ConstantSourceCount,
                predicateFrozenScalarTables.ConstantSourceSection));
        }
        if (nativeCallableRows.Count != 0)
        {
            sections.Add(new(0x31, 0, (uint)nativeCallableRows.Count, nativeCallableBytes));
        }
        if (nativeVTableRows.Count != 0)
        {
            sections.Add(new(0x32, 0, (uint)nativeVTableRows.Count, nativeVTableBytes));
        }
        if (nativeFunctionPointerSignatureTableCount != 0)
        {
            sections.Add(new(0x33, 0, (uint)nativeFunctionPointerSignatureTableCount,
                nativeFunctionPointerSignatureBytes));
        }
        if (predicateGraphs.Count != 0)
        {
            sections.Add(new(0x0037, 0, (uint)predicateGraphs.Count,
                predicateGraphBytes));
            sections.Add(new(0x0038, 0, (uint)predicateReadFootprints.Count,
                predicateReadFootprintBytes));
            sections.Add(new(0x0039, 0, (uint)predicateReadRoots.Count,
                predicateReadRootBytes));
            sections.Add(new(0x003B, 0, (uint)predicateNodes.Count,
                predicateNodeBytes));
            sections.Add(new(0x003C, 0, (uint)predicateNodeOperands.Count,
                predicateNodeOperandBytes));
            if (predicateChildEdges.Count != 0)
            {
                sections.Add(new(0x003D, 0,
                    (uint)predicateChildEdges.Count,
                    predicateChildEdgeBytes));
            }
            if (predicateCaptures.Count != 0)
            {
                sections.Add(new(0x003E, 0,
                    (uint)predicateCaptures.Count,
                    predicateCaptureBytes));
            }
            if (predicateOperations.Count != 0)
            {
                sections.Add(new(0x004D, 0,
                    (uint)predicateOperations.Count,
                    predicateOperationBytes));
            }
            if (predicateTypeUses.Count != 0)
            {
                sections.Add(new(0x004F, 0,
                    (uint)predicateTypeUses.Count,
                    predicateTypeUseBytes));
            }
            if (predicateTypePatterns.Count != 0)
            {
                sections.Add(new(0x0052, 0,
                    (uint)predicateTypePatterns.Count,
                    predicateTypePatternBytes));
            }
        }
        if (predicateFrozenScalarTables.FrozenValueNodeCount != 0)
        {
            sections.Add(new(0x003F, 0,
                (uint)predicateFrozenScalarTables.FrozenValueNodeCount,
                predicateFrozenScalarTables.FrozenValueNodeSection));
        }
        if (targetProfiles.Count != 0)
        {
            sections.Add(new(0x48, 0, (uint)targetProfiles.Count, targetProfileBytes));
        }
        if (fieldPathRows.Count != 0)
        {
            sections.Add(new(0x4B, 0, (uint)fieldPathRows.Count,
                fieldPathBytes));
        }
        if (methodParameterSurfaceRows.Count != 0)
        {
            sections.Add(new(0x0066, 0,
                (uint)methodParameterSurfaceRows.Count,
                methodParameterSurfaceBytes));
        }
        if (typeDefBindingEqualityRows.Count != 0)
        {
            sections.Add(new(0x0067, 0,
                (uint)typeDefBindingEqualityRows.Count,
                TrRmTypeDefBindingEqualityTableWriter.Encode(
                    typeDefBindingEqualityRows, modules, typeDefs)));
        }
        var artifactSchemaIdentity
            = IsSelectedTypeDefBindingEqualitySchemaIdentity(
                standardSchemaIdentity)
                || standardTerrumFunctionPointerProfile
                ? standardSchemaIdentity : null;
        var artifact = TrRmArtifactWriter.Encode(sections, schema,
            standardSchemaIdentity: artifactSchemaIdentity);
        if (schema.UsesDevelopmentNativeConventions())
        {
            _ = LoadDevelopmentNativeConventionsDraft(artifact);
        }
        else if (schema.UsesDevelopmentFunctionPointerEffects())
        {
            _ = LoadDevelopmentFunctionPointerEffectsDraft(artifact);
        }
        else if (schema.UsesRegisterExceptionRegions())
        {
            _ = LoadRegisterExceptionRegionsDraft(artifact);
        }
        else if (standardNativeParameterProfile
            && schema
                == TrRmArtifactSchema.InitializationReferencePointerLoadNativeParameterDraft)
        {
            _ = TrRmStructuralMetadataValidator
                .ValidateInitializationReferencePointerLoadNativeParameterDraftAndGetExecutionTables(
                    TrRmArtifactIndex
                        .ForInitializationReferencePointerLoadNativeParameterDraft(
                            artifact));
        }
        else if (standardNativeParameterProfile
            && schema
                == TrRmArtifactSchema.InitializationReferenceGeneralPredicatePointerLoadNativeParameterDraft)
        {
            _ = TrRmStructuralMetadataValidator
                .ValidateInitializationReferenceGeneralPredicatePointerLoadNativeParameterDraftAndGetExecutionTables(
                    TrRmArtifactIndex
                        .ForInitializationReferenceGeneralPredicatePointerLoadNativeParameterDraft(
                            artifact));
        }
        else if (standardNativePointerLoads
            && schema == TrRmArtifactSchema.Standard)
        {
            _ = TrRmStructuralMetadataValidator
                .ValidateAndGetExecutionTables(
                    TrRmArtifactIndex.ForNativePointerLoadsDraft(artifact));
        }
        else if (schema is TrRmArtifactSchema.Standard
            or TrRmArtifactSchema.InitializationReferenceLocalFieldLayoutDraft
            or TrRmArtifactSchema.InitializationReferenceLocalFieldLayoutNestedFieldDestinationsDraft)
        {
            _ = TrRmStructuralMetadataValidator
                .ValidateAndGetExecutionTables(new TrRmArtifactIndex(
                    artifact, schema,
                    standardSchemaIdentity: artifactSchemaIdentity));
        }
        else if (schema.UsesGeneralPredicateGraphs()
            && schema.UsesGenericMethodCalls())
        {
            _ = TrRmStructuralMetadataValidator
                .ValidateC1AndGetExecutionTables(
                    new TrRmArtifactIndex(artifact, schema,
                        standardSchemaIdentity:
                            artifactSchemaIdentity));
        }
        else if (schema.UsesGeneralPredicateGraphs())
        {
            _ = TrRmStructuralMetadataValidator
                .ValidateGeneralPredicateGraphsDraftAndGetExecutionTables(
                    standardNativePointerLoads
                        ? TrRmArtifactIndex
                            .ForGeneralPredicateGraphsNativePointerLoadsDraft(
                                artifact)
                        : TrRmArtifactIndex
                            .ForGeneralPredicateGraphsDraft(artifact));
        }
        // The FNI composer appends interface metadata and resolves its temporary Body targets.
        else if (!schema.UsesForeignNativeInterface())
        {
            _ = Load(artifact);
        }
        return artifact;
    }

    private static ReadOnlyMemory<byte>[] CreateTypeRows(byte[] codes)
    {
        var rows = new ReadOnlyMemory<byte>[codes.Length];
        for (var index = 0; index < codes.Length; index++)
        {
            rows[index] = TrRmTypeSpecTableWriter.Builtin(codes[index]);
        }
        return rows;
    }

    /// <summary>Loads a Development-profile TRRM artifact as a checked program.</summary>
    /// <param name="artifact">The artifact bytes to validate and load.</param>
    /// <returns>The loaded checked program.</returns>
    public static BoundProgram Load(ReadOnlySpan<byte> artifact)
        => LoadDevelopment(artifact, TrRmArtifactSchema.Development);

    internal static BoundProgram LoadDevelopmentFunctionPointerEffectsDraft(
        ReadOnlySpan<byte> artifact)
        => LoadDevelopment(artifact,
            TrRmArtifactSchema.DevelopmentFunctionPointerEffectsDraft);

    internal static BoundProgram LoadDevelopmentNativeConventionsDraft(
        ReadOnlySpan<byte> artifact)
        => LoadDevelopment(artifact,
            TrRmArtifactSchema.DevelopmentNativeConventionsDraft);

    internal static BoundProgram LoadRegisterExceptionRegionsDraft(
        ReadOnlySpan<byte> artifact,
        ImmutableArray<CoreExceptionIdentitySelection>
            coreExceptionIdentitySelections = default)
        => LoadDevelopment(artifact,
            TrRmArtifactSchema.RegisterExceptionRegionsDraft,
            coreExceptionIdentitySelections);

    private static BoundProgram LoadDevelopment(ReadOnlySpan<byte> artifact,
        TrRmArtifactSchema schema,
        ImmutableArray<CoreExceptionIdentitySelection>
            coreExceptionIdentitySelections = default,
        string? standardSchemaIdentity = null,
        IReadOnlyDictionary<int, BoundModuleImport>?
            selectedModuleImports = null,
        ImmutableArray<CoreContractFailureIdentitySelection>
            coreContractFailureIdentitySelections = default,
        int selectedModuleRow = 0,
        BoundProgram? standardClassQueryProgram = null,
        bool standardConstruction2MethodLinking = false,
        IReadOnlyDictionary<uint, PrimitiveType>?
            nominalClassTypesByTypeSpecReference = null,
        IReadOnlyDictionary<int,
            (int ArtifactIndex, int ModuleRow, int TypeDefRow)>?
            standardFunctionPointerProviderTypeIdentities = null)
    {
        var standardInitializationReferenceBodies
            = standardSchemaIdentity is not null
                && IsSelectedInitializationReferenceSchemaIdentity(
                    standardSchemaIdentity);
        var standardConstruction2MethodLinkingProfile
            = standardConstruction2MethodLinking
                && standardSchemaIdentity is { } construction2Schema
                && (IsSelectedC1ClassConstructionSchemaIdentity(
                        construction2Schema)
                    || StandardGenericMethodSchema
                        .IsCM1ClassParameterDefaults(construction2Schema)
                    || IsC1M1MethodTypeSchemaIdentity(
                        construction2Schema))
                && GetForeignInvocationSchema(construction2Schema)
                    .UsesObjectConstruction()
                && !IsSelectedC1ClassInstanceLayoutSchemaIdentity(
                    construction2Schema);
        if (standardConstruction2MethodLinking
            && !standardConstruction2MethodLinkingProfile)
        {
            throw new ArgumentException(
                "The selected Construction-2 Method linking profile is invalid.",
                nameof(standardConstruction2MethodLinking));
        }
        var generalPredicateGraphsInitializationReferenceBodies
            = standardSchemaIdentity is not null
                && IsSelectedGeneralPredicateGraphsSchemaIdentity(
                    standardSchemaIdentity);
        var standardNativePointerLoads
            = standardSchemaIdentity is not null
                && IsSelectedNativePointerLoadsSchemaIdentity(
                    standardSchemaIdentity);
        var standardNativeParameterProfile
            = standardNativePointerLoads
                && schema.UsesNativeParameterContracts();
        var standardTerrumFunctionPointerProfile
            = TrRmArtifactSchemaFacts
                .IsSelectedTerrumFunctionPointerSchemaIdentity(
                    standardSchemaIdentity);
        var standardRegisterBodies
            = standardInitializationReferenceBodies
                || standardConstruction2MethodLinkingProfile;
        if (standardSchemaIdentity is not null
            && (!(standardInitializationReferenceBodies
                    || standardConstruction2MethodLinkingProfile
                    || standardTerrumFunctionPointerProfile)
                || schema != GetForeignInvocationSchema(
                    standardSchemaIdentity)))
        {
            throw new ArgumentException(
                "The selected initialization-reference Body profile is invalid.",
                nameof(standardSchemaIdentity));
        }
        if (selectedModuleImports is not null
            && !(standardInitializationReferenceBodies
                || standardConstruction2MethodLinkingProfile))
        {
            throw new ArgumentException(
                "Selected source-method imports require an assigned Standard Body profile.",
                nameof(selectedModuleImports));
        }

        if (!CoreExceptionIdentitySelectionContract.IsValidTable(
            coreExceptionIdentitySelections))
        {
            throw new ArgumentException(
                "The Core exception identity selections are invalid.",
                nameof(coreExceptionIdentitySelections));
        }
        if (!CoreContractFailureIdentitySelectionContract.IsValidTable(
            coreContractFailureIdentitySelections))
        {
            throw new ArgumentException(
                "The Core contract-failure identity selections are invalid.",
                nameof(coreContractFailureIdentitySelections));
        }
        if (!coreContractFailureIdentitySelections.IsDefaultOrEmpty
            && !generalPredicateGraphsInitializationReferenceBodies)
        {
            throw Unsupported(
                "Core contract-failure identity selections require the exact B+I+G profile.");
        }

        var index = standardNativePointerLoads
            ? schema switch
            {
                TrRmArtifactSchema.Standard
                    => TrRmArtifactIndex.ForNativePointerLoadsDraft(artifact),
                TrRmArtifactSchema.GeneralPredicateGraphsDraft
                    => TrRmArtifactIndex
                        .ForGeneralPredicateGraphsNativePointerLoadsDraft(
                            artifact),
                TrRmArtifactSchema
                    .InitializationReferencePointerLoadNativeParameterDraft
                    => TrRmArtifactIndex
                        .ForInitializationReferencePointerLoadNativeParameterDraft(
                            artifact),
                TrRmArtifactSchema
                    .InitializationReferenceGeneralPredicatePointerLoadNativeParameterDraft
                    => TrRmArtifactIndex
                        .ForInitializationReferenceGeneralPredicatePointerLoadNativeParameterDraft(
                            artifact),
                _ => throw new ArgumentOutOfRangeException(nameof(schema)),
            }
            : standardSchemaIdentity is { } selectedSchemaIdentity
                && (IsSelectedC1ClassInstanceLayoutSchemaIdentity(
                        selectedSchemaIdentity)
                    || IsSelectedTypeDefBindingEqualitySchemaIdentity(
                        selectedSchemaIdentity)
                    || TrRmArtifactSchemaFacts
                        .IsSelectedTerrumFunctionPointerSchemaIdentity(
                            selectedSchemaIdentity))
                    ? new TrRmArtifactIndex(artifact, schema, null,
                        usesNativePointerLoads: false,
                        standardSchemaIdentity: selectedSchemaIdentity)
            : new TrRmArtifactIndex(artifact, schema);
        var validatedTables = schema.UsesDevelopmentNativeConventions()
            ? TrRmStructuralMetadataValidator
                .ValidateDevelopmentNativeConventionsDraftAndGetExecutionTables(index)
            : schema.UsesDevelopmentFunctionPointerEffects()
            ? TrRmStructuralMetadataValidator
                .ValidateDevelopmentFunctionPointerEffectsDraftAndGetExecutionTables(index)
            : schema.UsesRegisterExceptionRegions()
                ? TrRmStructuralMetadataValidator
                    .ValidateRegisterExceptionRegionsDraftAndGetExecutionTables(index)
            : schema.UsesGeneralPredicateGraphs()
                && schema.UsesGenericMethodCalls()
                ? TrRmStructuralMetadataValidator
                    .ValidateC1AndGetExecutionTables(index)
            : schema
                    == TrRmArtifactSchema.InitializationReferencePointerLoadNativeParameterDraft
                ? TrRmStructuralMetadataValidator
                    .ValidateInitializationReferencePointerLoadNativeParameterDraftAndGetExecutionTables(
                        index)
                : schema
                    == TrRmArtifactSchema.InitializationReferenceGeneralPredicatePointerLoadNativeParameterDraft
                ? TrRmStructuralMetadataValidator
                    .ValidateInitializationReferenceGeneralPredicatePointerLoadNativeParameterDraftAndGetExecutionTables(
                        index)
            : schema.UsesGeneralPredicateGraphs()
                ? TrRmStructuralMetadataValidator
                    .ValidateGeneralPredicateGraphsDraftAndGetExecutionTables(
                        index)
                : schema is TrRmArtifactSchema.Standard
                    or TrRmArtifactSchema.InitializationReferenceLocalFieldLayoutDraft
                    or TrRmArtifactSchema.InitializationReferenceLocalFieldLayoutNestedFieldDestinationsDraft
                    or TrRmArtifactSchema.ForeignNativeInterfaceDraft
                    or TrRmArtifactSchema.ForeignNativeInterfaceParameterSurfaceDraft
                    or TrRmArtifactSchema.ForeignNativeInterfaceAggregateOutputConsumptionDraft
                    ? TrRmStructuralMetadataValidator
                        .ValidateAndGetExecutionTables(index)
                : TrRmStructuralMetadataValidator
                    .ValidateDevelopmentAndGetExecutionTables(index);
        var modules = validatedTables.Modules;
        var moduleRefs = validatedTables.ModuleRefs;
        var typeDefs = validatedTables.TypeDefs;
        var strings = validatedTables.Strings;
        var types = validatedTables.TypeSpecs;
        var fields = validatedTables.Fields;
        var fieldPaths = validatedTables.FieldPaths;
        var layouts = validatedTables.Layouts;
        var layoutFields = validatedTables.LayoutFields;
        var targetProfiles = validatedTables.TargetProfiles;
        var sigs = validatedTables.Signatures;
        var defs = validatedTables.Methods;
        var parameters = validatedTables.Parameters;
        var bodies = validatedTables.Bodies;
        var contractTable = validatedTables.Contracts;
        var nativeImports = validatedTables.NativeImports;
        var standardNativeImports = validatedTables.StandardNativeImports;
        var nativeCallables = validatedTables.NativeCallables;
        var nativeFunctionPointerSignatureTable = validatedTables.NativeFunctionPointerSignatures;
        var nativeVTables = validatedTables.NativeVTables;
        var imports = validatedTables.Imports;
        var foreignInterfaceMethodRows = new HashSet<int>();
        if (schema.UsesForeignNativeInterface())
        {
            for (var foreignInterfaceRow = 1;
                foreignInterfaceRow
                    <= validatedTables.ForeignNativeInterfaces.Count;
                foreignInterfaceRow++)
            {
                var interfaceDef = checked((int)
                    validatedTables.ForeignNativeInterfaces
                        .GetOwnerInterfaceDef(foreignInterfaceRow));
                var ownerTypeDef
                    = validatedTables.Interfaces
                        .GetOwnerTypeDef(interfaceDef);
                if (typeDefs.GetOwnerModule(ownerTypeDef)
                    != selectedModuleRow)
                {
                    continue;
                }

                var (firstMethod, methodCount)
                    = validatedTables.Interfaces.GetMethodRange(
                        interfaceDef);
                for (var ordinal = 0; ordinal < methodCount; ordinal++)
                {
                    _ = foreignInterfaceMethodRows.Add(
                        validatedTables.InterfaceMethods
                            .GetMethodDefRow(firstMethod + ordinal));
                }
            }
        }
        var foreignInterfaceBodyTargets
            = schema.UsesForeignNativeInterface()
                ? standardClassQueryProgram is null
                    ? new Dictionary<int,
                        ForeignNativeInterfaceBodyTarget>()
                    : CreateForeignNativeInterfaceBodyTargets(
                        validatedTables, selectedModuleRow,
                        standardClassQueryProgram.Interfaces)
                : new Dictionary<int,
                    ForeignNativeInterfaceBodyTarget>();
        var typeCount = types.Count;
        for (var section = 0; section < index.SectionCount; section++)
        {
            index.GetSection(section, out var kind, out _, out _, out _);
            var isPublishedPredicateSection
                = standardInitializationReferenceBodies
                    && validatedTables.Features.TryGetFeature(
                        TrRmFeatureTableReader.PublishedPredicatesFeatureId,
                        out _)
                    && kind is 0x37 or 0x38 or 0x39 or 0x3B or 0x3C;
            var isGeneralPredicateGraphSection
                = generalPredicateGraphsInitializationReferenceBodies
                    && kind == 0x3D;
            var isSelectedStandardNativeImportSection
                = standardNativePointerLoads && kind == 0x0021;
            var isForeignNativeInterfaceSection
                = schema.UsesForeignNativeInterface()
                    && (kind is 0x0024 or 0x0025 or 0x004C or 0x0052
                        or 0x0066);
            if (kind is not (1 or 2 or 4 or 0x10 or 0x11 or 0x13 or 0x14 or 0x15
                or 0x16 or 0x17 or 0x18
                or 0x1B or 0x1C or 0x1D or 0x20
                or 0x23 or 0x30 or 0x31 or 0x32 or 0x33 or 0x48 or 0x4B)
                && !isPublishedPredicateSection
                && !isGeneralPredicateGraphSection
                && !isForeignNativeInterfaceSection
                && !isSelectedStandardNativeImportSection)
            {
                throw Invalid($"Section 0x{kind:X4} is not executable.");
            }
        }

        _ = index.TryGetSection(1, out _, out var moduleCount);
        _ = index.TryGetSection(0x16, out _, out var methodCount);
        _ = index.TryGetSection(0x17, out _, out var sigCount);
        _ = index.TryGetSection(0x18, out _, out _);
        _ = index.TryGetSection(4, out var featureBytes, out var featureCount);
        _ = index.TryGetSection(0x1B, out var contractBytes, out _);
        _ = index.TryGetSection(0x1C, out _, out _);
        _ = index.TryGetSection(0x1D, out var exportBytes, out var exportCount);
        _ = index.TryGetSection(0x20, out _, out _);
        if (selectedModuleRow == 0)
        {
            if (moduleCount != 1)
            {
                throw Unsupported(
                    "Executable TRRM loading requires an explicit Module selection when the artifact contains multiple Modules.");
            }
            selectedModuleRow = 1;
        }
        if (selectedModuleRow < 1 || selectedModuleRow > moduleCount)
        {
            throw new ArgumentOutOfRangeException(nameof(selectedModuleRow));
        }

        var (methodStart, methodEnd)
            = modules.GetMethodDefRange(selectedModuleRow);
        var selectedMethodCount = methodEnd - methodStart;
        var (typeDefStart, typeDefEnd)
            = modules.GetTypeDefRange(selectedModuleRow);
        var selectedTypeDefRows = new HashSet<int>(
            Enumerable.Range(typeDefStart, typeDefEnd - typeDefStart));
        var selectedImportRows = new HashSet<int>();
        for (var importRow = 1; importRow <= imports.Count; importRow++)
        {
            var moduleRefRow
                = imports.GetModuleRefForImportRow(importRow);
            if (moduleRefs.GetOwnerModule(moduleRefRow)
                == selectedModuleRow)
            {
                _ = selectedImportRows.Add(importRow);
            }
        }
        var selectedSignatures = new HashSet<int>();
        var selectedBodyCount = 0;
        for (var methodRow = methodStart; methodRow < methodEnd;
            methodRow++)
        {
            _ = selectedSignatures.Add(defs.GetSignature(methodRow));
            selectedBodyCount += defs.GetBodyRow(methodRow) != 0 ? 1 : 0;
        }

        var typeOnlyInitializationReferenceLayoutModule
            = schema.UsesInitializationReferenceLocalFieldLayout()
                && selectedMethodCount == 0
                && selectedTypeDefRows.Count != 0
                && modules.GetEntryMethod(selectedModuleRow) == 0;
        if ((selectedMethodCount == 0
                && !typeOnlyInitializationReferenceLayoutModule)
            || selectedBodyCount > selectedMethodCount
            || (selectedMethodCount != 0
                && selectedSignatures.Count == 0)
            || typeCount == 0)
        {
            throw Invalid(
                "Expected an executable Module or an exact type-only initialization-reference layout Module.");
        }

        if (schema.UsesRegisterExceptionRegions() && featureCount != 0)
        {
            throw Unsupported(
                "The current B+E1 scalar loader does not consume Module Feature requirements.");
        }

        var featureTable = default(TrRmDevelopmentFeatureTableReader);
        var hasOptionalScalarTypes = false;
        for (var row = 1; row <= typeCount; row++)
        {
            hasOptionalScalarTypes |= types.GetRow(row)[0] == 0x06
                && TrRmOptionalScalar.IsOptionalType(BodyTypeFromRow(types, row));
        }
        if (modules.GetRequirements(selectedModuleRow) != (1u, 0u))
        {
            throw Invalid("Only fail-fast Windows x64 target requirement 1 is executable.");
        }
        var (firstFeature, requiredFeatureCount)
            = modules.GetFeatureRange(selectedModuleRow);
        uint scalarInitOutputRuleRevision = 0;
        uint rootlessNativeValuesRuleRevision = 0;
        if ((schema is TrRmArtifactSchema.Standard
                or TrRmArtifactSchema.InitializationReferenceLocalFieldLayoutDraft
                or TrRmArtifactSchema.InitializationReferenceLocalFieldLayoutNestedFieldDestinationsDraft
                or TrRmArtifactSchema.ForeignNativeInterfaceDraft
                or TrRmArtifactSchema.ForeignNativeInterfaceParameterSurfaceDraft
                or TrRmArtifactSchema.ForeignNativeInterfaceAggregateOutputConsumptionDraft)
            || schema.UsesGeneralPredicateGraphs()
            || schema.UsesNativeParameterContracts())
        {
            var standardFeatureTable = new TrRmFeatureTableReader(featureBytes,
                featureCount, schema);
            for (var feature = firstFeature;
                feature < firstFeature + requiredFeatureCount; feature++)
            {
                var required = standardFeatureTable.GetFeature(feature);
                if (required.FeatureId
                    == TrRmFeatureTableReader.RootlessNativeValuesFeatureId)
                {
                    rootlessNativeValuesRuleRevision = required.RuleRevision;
                }
            }
        }
        else
        {
            featureTable = new TrRmDevelopmentFeatureTableReader(featureBytes,
                featureCount);
            for (var feature = firstFeature;
                feature < firstFeature + requiredFeatureCount; feature++)
            {
                var required = featureTable.GetFeature(feature);
                if (required.FeatureId
                    == TrRmDevelopmentFeatureTableReader.ScalarInitOutputFeatureId)
                {
                    scalarInitOutputRuleRevision
                        = required.MinimumRuleRevision;
                }
                else if (required.FeatureId
                    == TrRmDevelopmentFeatureTableReader.RootlessNativeValuesFeatureId)
                {
                    rootlessNativeValuesRuleRevision
                        = required.MinimumRuleRevision;
                }
            }
        }
        if (nativeFunctionPointerSignatureTable.Count != 0
            && rootlessNativeValuesRuleRevision == 0)
        {
            throw Invalid("Native function-pointer signatures require the RootlessNativeValues feature.");
        }

        if (schema.UsesForeignNativeInterface())
        {
            for (var methodRow = methodStart;
                methodRow < methodEnd; methodRow++)
            {
                var bodyRow = defs.GetBodyRow(methodRow);
                if (bodyRow != 0
                    && bodies.GetRow(bodyRow)[0] == 0x01)
                {
                    validatedTables.ValidateRegisterBodyMapsForMethod(
                        methodRow);
                }
            }
        }

        var optionalStructures = hasOptionalScalarTypes ? TrRmOptionalScalar.Structures : [];
        var nativePointerTypeDefinitions = new List<NativePointerTypeDefinition>();
        var checkedReferenceTypeDefinitions
            = new List<CheckedReferenceTypeDefinition>();
        var checkedReferenceTypeIndexes
            = new Dictionary<CheckedReferenceTypeDefinition, int>();
        var functionPointerSignatureIndexByTypeSpecRow
            = new int[types.Count + 1];
        Array.Fill(functionPointerSignatureIndexByTypeSpecRow, -1);
        var standardFunctionPointerSignatures
            = ImmutableArray<NativeFunctionPointerSignatureDefinition>.Empty;
        var standardFunctionPointerTypeDefClosureRows
            = ImmutableArray<int>.Empty;
        int[]? standardFunctionPointerStructureTypeByTypeDef = null;
        var standardFunctionPointerAnchorProfile
            = schema == TrRmArtifactSchema.Standard;
        if (standardFunctionPointerAnchorProfile)
        {
            standardFunctionPointerStructureTypeByTypeDef
                = new int[typeDefs.Count + 1];
            Array.Fill(standardFunctionPointerStructureTypeByTypeDef, -1);
            if (targetProfiles.TryGetProfile(1, 1, out _))
            {
                var nextStructureIndex = optionalStructures.Length;
                for (var typeDefRow = 1;
                    typeDefRow <= typeDefs.Count; typeDefRow++)
                {
                    if (selectedTypeDefRows.Contains(typeDefRow)
                        && layouts.HasBlittableLayout(typeDefRow, 1, 1))
                    {
                        standardFunctionPointerStructureTypeByTypeDef[
                            typeDefRow] = nextStructureIndex++;
                    }
                }

                var anchorClosure
                    = CollectStandardFunctionPointerAnchorClosure(
                        validatedTables, selectedModuleRow,
                        standardFunctionPointerStructureTypeByTypeDef,
                        standardSchemaIdentity is { } selectedIdentity
                            && TrRmArtifactSchemaFacts
                                .IsSelectedTerrumFunctionPointerSchemaIdentity(
                                    selectedIdentity));
                standardFunctionPointerTypeDefClosureRows
                    = anchorClosure.AdditionalTypeDefRows;
                foreach (var typeDefRow
                    in standardFunctionPointerTypeDefClosureRows)
                {
                    if (standardFunctionPointerStructureTypeByTypeDef[
                            typeDefRow] >= 0)
                    {
                        throw Invalid(
                            "A defining-Module TypeDef is already represented by the selected structure map.");
                    }

                    standardFunctionPointerStructureTypeByTypeDef[
                        typeDefRow] = nextStructureIndex++;
                }

                var signatureStructureTypeByTypeDef
                    = standardFunctionPointerStructureTypeByTypeDef;
                if (TrRmArtifactSchemaFacts
                        .IsSelectedTerrumFunctionPointerSchemaIdentity(
                            standardSchemaIdentity)
                    && standardFunctionPointerProviderTypeIdentities
                        is { Count: > 0 })
                {
                    var canonicalStructureIndexesByProviderType
                        = new Dictionary<(int ArtifactIndex, int ModuleRow,
                            int TypeDefRow), int>();
                    var aliasStructureIndexesByTypeDef
                        = new Dictionary<int, int>();
                    for (var typeDefRow = 1;
                        typeDefRow < standardFunctionPointerStructureTypeByTypeDef
                            .Length;
                        typeDefRow++)
                    {
                        if (standardFunctionPointerStructureTypeByTypeDef[
                                typeDefRow] < 0
                            || !standardFunctionPointerProviderTypeIdentities
                                .TryGetValue(typeDefRow,
                                    out var providerTypeIdentity))
                        {
                            continue;
                        }

                        if (canonicalStructureIndexesByProviderType.TryGetValue(
                                providerTypeIdentity,
                                out var canonicalStructureIndex))
                        {
                            aliasStructureIndexesByTypeDef[typeDefRow]
                                = canonicalStructureIndex;
                        }
                        else
                        {
                            canonicalStructureIndexesByProviderType.Add(
                                providerTypeIdentity,
                                standardFunctionPointerStructureTypeByTypeDef[
                                    typeDefRow]);
                        }
                    }

                    if (aliasStructureIndexesByTypeDef.Count != 0)
                    {
                        signatureStructureTypeByTypeDef
                            = (int[])standardFunctionPointerStructureTypeByTypeDef
                                .Clone();
                        foreach (var (typeDefRow, canonicalStructureIndex)
                            in aliasStructureIndexesByTypeDef)
                        {
                            signatureStructureTypeByTypeDef[typeDefRow]
                                = canonicalStructureIndex;
                        }
                    }
                }

                standardFunctionPointerSignatures
                    = LoadStandardFunctionPointerSignatures(validatedTables,
                        selectedModuleRow,
                        signatureStructureTypeByTypeDef,
                        nativePointerTypeDefinitions,
                        functionPointerSignatureIndexByTypeSpecRow,
                        anchorClosure.AnchorTypeSpecRows);
            }
        }

        Action<int[]>? mapFunctionPointerTypeDefClosure = null;
        if (!standardFunctionPointerTypeDefClosureRows.IsEmpty)
        {
            mapFunctionPointerTypeDefClosure = structureTypeByTypeDef =>
            {
                foreach (var typeDefRow
                    in standardFunctionPointerTypeDefClosureRows)
                {
                    structureTypeByTypeDef[typeDefRow]
                        = standardFunctionPointerStructureTypeByTypeDef![
                            typeDefRow];
                }
            };
        }

        var structures = LoadBlittableStructures(typeDefs, fields, layouts, layoutFields,
            targetProfiles, types, optionalStructures, nativePointerTypeDefinitions,
            out var structureTypeByTypeDef, imports: imports,
            selectedTypeDefRows: selectedTypeDefRows,
            includedTypeDefRows: selectedTypeDefRows,
            allowDuplicateImportedViews: schema is TrRmArtifactSchema.Standard
                or TrRmArtifactSchema.InitializationReferenceLocalFieldLayoutDraft
                or TrRmArtifactSchema.InitializationReferenceLocalFieldLayoutNestedFieldDestinationsDraft,
            nominalClassTypesByTypeSpecReference:
                nominalClassTypesByTypeSpecReference,
            functionPointerSignatureIndexByTypeSpecRow:
                standardFunctionPointerAnchorProfile
                    ? functionPointerSignatureIndexByTypeSpecRow : null,
            beforeFieldLoad: mapFunctionPointerTypeDefClosure,
            deferLayoutValidation:
                !standardFunctionPointerTypeDefClosureRows.IsEmpty);
        if (standardFunctionPointerStructureTypeByTypeDef is not null
            && Enumerable.Range(0, typeDefEnd - typeDefStart)
                .Any(offset => structureTypeByTypeDef[
                    typeDefStart + offset] !=
                    standardFunctionPointerStructureTypeByTypeDef[
                        typeDefStart + offset]))
        {
            throw Invalid(
                "The Standard function-pointer signature TypeDef map changed while loading layouts.");
        }
        if (!standardFunctionPointerTypeDefClosureRows.IsEmpty)
        {
            var combinedTypeDefRows = new HashSet<int>(selectedTypeDefRows);
            var existingStructureIndexesByTypeDef
                = new Dictionary<int, int>(selectedTypeDefRows.Count);
            for (var typeDefRow = typeDefStart;
                typeDefRow < typeDefEnd; typeDefRow++)
            {
                if (structureTypeByTypeDef[typeDefRow] >= 0)
                {
                    existingStructureIndexesByTypeDef.Add(typeDefRow,
                        structureTypeByTypeDef[typeDefRow]);
                }
            }

            foreach (var typeDefRow
                in standardFunctionPointerTypeDefClosureRows)
            {
                _ = combinedTypeDefRows.Add(typeDefRow);
            }

            structures = LoadBlittableStructures(typeDefs, fields, layouts,
                layoutFields, targetProfiles, types, structures,
                nativePointerTypeDefinitions,
                out var combinedStructureTypeByTypeDef, imports: imports,
                selectedTypeDefRows: combinedTypeDefRows,
                includedTypeDefRows: combinedTypeDefRows,
                allowDuplicateImportedViews: true,
                nominalClassTypesByTypeSpecReference:
                    nominalClassTypesByTypeSpecReference,
                existingStructureTypeIndexesByTypeDef:
                    existingStructureIndexesByTypeDef,
                functionPointerSignatureIndexByTypeSpecRow:
                    functionPointerSignatureIndexByTypeSpecRow,
                hiddenTypeDefRows: standardFunctionPointerTypeDefClosureRows
                    .ToHashSet());
            if (!combinedStructureTypeByTypeDef.AsSpan().SequenceEqual(
                    standardFunctionPointerStructureTypeByTypeDef))
            {
                throw Invalid(
                    "The defining-Module function-pointer TypeDef closure changed while loading layouts.");
            }
            structureTypeByTypeDef = combinedStructureTypeByTypeDef;
        }

        var declaredExports = new HashSet<int>(exportCount);
        for (var row = 0; row < exportCount; row++)
        {
            var id = BinaryPrimitives.ReadUInt32LittleEndian(exportBytes.Slice(row * 8));
            var name = BinaryPrimitives.ReadUInt32LittleEndian(exportBytes.Slice((row * 8) + 4));
            var method = id & 0xFF_FFFF;
            var definitionKind = id >> 24;
            if (((schema is TrRmArtifactSchema.Standard
                    or TrRmArtifactSchema.InitializationReferenceLocalFieldLayoutDraft
                    or TrRmArtifactSchema.InitializationReferenceLocalFieldLayoutNestedFieldDestinationsDraft)
                    || schema.UsesForeignNativeInterface())
                && (definitionKind is 0x13 or 0x14))
            {
                continue;
            }

            if (schema.UsesForeignNativeInterface()
                && definitionKind == 0x16
                && foreignInterfaceMethodRows.Contains(checked((int)method)))
            {
                continue;
            }

            if (definitionKind != 0x16 || method == 0
                || method > (uint)methodCount
                || strings.DecodeName((int)name)
                    != defs.GetName((int)method))
            {
                throw Invalid("Unsupported export identity.");
            }
            var methodRow = checked((int)method);
            if (defs.GetOwnerModule(methodRow) != selectedModuleRow)
            {
                continue;
            }
            if (!declaredExports.Add(methodRow))
            {
                throw Invalid("Unsupported export identity.");
            }
        }

        var functions = ImmutableArray.CreateBuilder<BoundFunction>(
            selectedMethodCount);
        var seen = new HashSet<string>(selectedMethodCount,
            StringComparer.Ordinal);
        var functionIndexByMethodRow = new int[methodCount + 1];
        Array.Fill(functionIndexByMethodRow, -1);
        var nativeFunctionPointerSignatureDefinitions =
            ImmutableArray.CreateBuilder<NativeFunctionPointerSignatureDefinition>(
                nativeFunctionPointerSignatureTable.Count);
        for (var signatureRow = 1;
            signatureRow <= nativeFunctionPointerSignatureTable.Count; signatureRow++)
        {
            var methodRow = nativeFunctionPointerSignatureTable.GetMethodDefRow(signatureRow);
            if (defs.GetOwnerModule(methodRow) != selectedModuleRow)
            {
                continue;
            }
            var signature = defs.GetSignature(methodRow);
            var resultType = FromNativeValueRow(types, sigs.GetResultTypeSpec(signature),
                nativePointerTypeDefinitions, structureTypeByTypeDef,
                imports, checkedReferenceTypeDefinitions,
                checkedReferenceTypeIndexes,
                nominalClassTypesByTypeSpecReference,
                functionPointerSignatureIndexByAnchorTypeSpec:
                    developmentProfile
                        ? null
                        : functionPointerSignatureIndexByTypeSpecRow);
            var (first, count) = sigs.GetParamRange(signature);
            var parameterTypes = ImmutableArray.CreateBuilder<PrimitiveType>(count);
            for (var parameter = 0; parameter < count; parameter++)
            {
                parameterTypes.Add(FromNativeValueRow(types,
                    parameters.GetTypeSpec(first + parameter),
                    nativePointerTypeDefinitions, structureTypeByTypeDef,
                    imports, checkedReferenceTypeDefinitions,
                    checkedReferenceTypeIndexes,
                    nominalClassTypesByTypeSpecReference));
            }

            nativeFunctionPointerSignatureDefinitions.Add(
                new NativeFunctionPointerSignatureDefinition(
                    nativeFunctionPointerSignatureTable.GetCallingConvention(signatureRow),
                    resultType, parameterTypes.MoveToImmutable()) {
                    EffectsMask = schema.UsesDevelopmentFunctionPointerEffects()
                        ? nativeFunctionPointerSignatureTable
                            .GetDeclaredEffectsMask(signatureRow) : 0,
                });
        }
        var nativeFunctionPointerSignatureArray = developmentProfile
            ? nativeFunctionPointerSignatureDefinitions.ToImmutable()
            : standardFunctionPointerAnchorProfile
                ? standardFunctionPointerSignatures
                : ImmutableArray<NativeFunctionPointerSignatureDefinition>.Empty;
        var signatureIndexByAnchorMethod = new int[methodCount + 1];
        Array.Fill(signatureIndexByAnchorMethod, -1);
        for (var signatureRow = 1;
            signatureRow <= nativeFunctionPointerSignatureTable.Count;
            signatureRow++)
        {
            var anchorMethodRow
                = nativeFunctionPointerSignatureTable.GetMethodDefRow(
                    signatureRow);
            if (defs.GetOwnerModule(anchorMethodRow)
                != selectedModuleRow)
            {
                continue;
            }
            signatureIndexByAnchorMethod[anchorMethodRow]
                = signatureRow - 1;
        }
        if (!NativePointerTypeContract.IsValidTable(
            [.. nativePointerTypeDefinitions], structures,
            nativeFunctionPointerSignatureArray,
            checkedReferenceTypes: [.. checkedReferenceTypeDefinitions]))
        {
            throw Invalid("Invalid native pointer or function-pointer type table.");
        }

        var nativeCallableTypeByMethod = new PrimitiveType?[methodCount + 1];
        for (var row = methodStart; row < methodEnd; row++)
        {
            if (nativeCallables.HasCallableForMethod(row))
            {
                nativeCallableTypeByMethod[row] = NativeFunctionPointerTypes.Type(
                    nativeCallables.GetFunctionPointerSignatureIndex(row));
            }
        }
        var usedSignatures = new bool[sigCount + 1];
        var usedSignatureCount = 0;
        var importedNativeCallableCount = 0;
        var importedNativeVTableCount = 0;
        var importedNativeParameterMethodCount = 0;
        var importedInitializationReferenceMethodCount = 0;
        var foreignNativeInterfaceImportCount = 0;
        for (var signatureRow = 1;
            signatureRow <= nativeFunctionPointerSignatureTable.Count; signatureRow++)
        {
            var methodRow = nativeFunctionPointerSignatureTable.GetMethodDefRow(signatureRow);
            if (defs.GetOwnerModule(methodRow) != selectedModuleRow)
            {
                continue;
            }
            var signature = defs.GetSignature(methodRow);
            if (!usedSignatures[signature])
            {
                usedSignatures[signature] = true;
                usedSignatureCount++;
            }
        }
        for (var row = methodStart; row < methodEnd; row++)
        {
            if (nativeFunctionPointerSignatureTable.HasSignatureForMethod(row))
            {
                continue;
            }
            if (!developmentProfile
                && validatedTables.NativeFunctionPointerAnchors
                    .IsAnchorMethodDef(row))
            {
                var anchorSignature = defs.GetSignature(row);
                if (!usedSignatures[anchorSignature])
                {
                    usedSignatures[anchorSignature] = true;
                    usedSignatureCount++;
                }

                continue;
            }

            if (foreignInterfaceMethodRows.Contains(row))
            {
                if (defs.GetBodyRow(row) != 0)
                {
                    throw Invalid(
                        "A ForeignNativeInterface MethodDef cannot carry a Body.");
                }

                var foreignSignature = defs.GetSignature(row);
                if (!usedSignatures[foreignSignature])
                {
                    usedSignatures[foreignSignature] = true;
                    usedSignatureCount++;
                }

                continue;
            }

            var flags = defs.GetFlags(row);
            var name = defs.GetName(row);
            var signature = defs.GetSignature(row);
            var isStandardNativeImport
                = !schema.IsDevelopmentProfile()
                    && standardNativeImports.HasImportForMethod(row);
            var isNativeImport = isStandardNativeImport
                || nativeImports.HasImportForMethod(row);
            var isNativeCallable = nativeCallables.HasCallableForMethod(row);
            var isNativeVTable = nativeVTables.HasVTableForMethod(row);
            var importDefinition = imports.IsExpectedDefinition(0x1600_0000u | (uint)row);
            var isImportedNativeCallable = importDefinition && isNativeCallable;
            var isImportedNativeVTable = importDefinition && isNativeVTable;
            var isImportedNativeParameterMethod
                = standardNativeParameterProfile
                    && importDefinition && isStandardNativeImport;
            var isImportedInitializationReferenceMethod
                = standardInitializationReferenceBodies && importDefinition
                    && !isNativeImport && !isNativeCallable
                    && !isNativeVTable;
            var isImportedConstruction2Method
                = standardConstruction2MethodLinkingProfile
                    && importDefinition && !isNativeImport
                    && !isNativeCallable && !isNativeVTable;
            importedNativeCallableCount += isImportedNativeCallable ? 1 : 0;
            importedNativeVTableCount += isImportedNativeVTable ? 1 : 0;
            importedNativeParameterMethodCount
                += isImportedNativeParameterMethod ? 1 : 0;
            importedInitializationReferenceMethodCount
                += isImportedInitializationReferenceMethod ? 1 : 0;
            if (standardConstruction2MethodLinkingProfile
                && defs.GetOwnerType(row) != 0)
            {
                if (importDefinition)
                {
                    throw Unsupported(
                        "Construction-2 runtime linking initially requires local class Methods; imported class-owned MethodDefs remain outside this route.");
                }

                if (!usedSignatures[signature])
                {
                    usedSignatures[signature] = true;
                    usedSignatureCount++;
                }

                continue;
            }

            if (defs.GetOwnerType(row) != 0
                || (importDefinition && !isImportedNativeCallable
                    && !isImportedNativeVTable
                    && !isImportedNativeParameterMethod
                    && !isImportedInitializationReferenceMethod
                    && !isImportedConstruction2Method)
                || (isNativeImport || isNativeVTable || isImportedNativeCallable
                    || isImportedInitializationReferenceMethod
                    || isImportedConstruction2Method)
                    != (defs.GetBodyRow(row) == 0)
                || sigs.GetReceiverTypeSpec(signature) != 0
                || sigs.GetGenericCount(signature) != 0
                || !seen.Add(name))
            {
                throw Invalid($"Unsupported declaration for method {row}.");
            }
            if ((flags == 2 && !importDefinition) != declaredExports.Contains(row))
            {
                throw Invalid("Every public method requires its matching named export.");
            }
            if (!usedSignatures[signature])
            {
                usedSignatures[signature] = true;
                usedSignatureCount++;
            }

            var (first, count) = sigs.GetParamRange(signature);
            var variables = ImmutableArray.CreateBuilder<BoundVariable>(count);
            var nativeOutputs = isNativeImport || isNativeVTable
                ? contractTable.GetNativeOutputs(row) : [];
            var nativeParameters = isNativeImport || isNativeVTable
                ? contractTable.GetNativeParameters(row) : [];
            for (var param = 0; param < count; param++)
            {
                var nativeOutput = FindNativeOutput(nativeOutputs, param);
                var nativeParameter = FindNativeParameter(nativeParameters, param);
                var typeSpecRow = parameters.GetTypeSpec(first + param);
                var type = nativeOutput is not null
                    ? PrimitiveType.NativeStorageAddress
                    : FromNativeValueRow(types, typeSpecRow,
                        nativePointerTypeDefinitions, structureTypeByTypeDef,
                        imports, checkedReferenceTypeDefinitions,
                        checkedReferenceTypeIndexes,
                        nominalClassTypesByTypeSpecReference,
                        functionPointerSignatureIndexByAnchorTypeSpec:
                            standardFunctionPointerAnchorProfile
                                ? functionPointerSignatureIndexByTypeSpecRow
                                : null);
                var mode = parameters.GetMode(first + param);
                var initializationReferenceParameter
                    = standardInitializationReferenceBodies
                        && CheckedReferenceTypes.IsType(type)
                        && checkedReferenceTypeDefinitions[
                            CheckedReferenceTypes.Index(type)]
                            .IsInitialization;
                if (type == PrimitiveType.Void
                    || (mode != 0 && mode != 2)
                    || (standardInitializationReferenceBodies && mode != 0)
                    || (isNativeImport && (mode != 0
                        || (nativeOutput is null && nativeParameter is null
                            && !NativeVTableCallContract.IsNativeValueType(type,
                                nativeFunctionPointerSignatureArray,
                                [.. nativePointerTypeDefinitions]))
                        || (nativeOutput is not null && type != PrimitiveType.NativeStorageAddress)))
                    || (isNativeVTable && (mode != 0
                        || (nativeOutput is not null
                            ? type != PrimitiveType.NativeStorageAddress
                                || nativeParameter is not null
                            : nativeParameter is null
                            && !NativeVTableCallContract.IsNativeValueType(type,
                                nativeFunctionPointerSignatureArray,
                                [.. nativePointerTypeDefinitions]))))
                    || (!isNativeImport && mode == 2
                        && (!IsSupportedScalarInitOutputType(type,
                                scalarInitOutputRuleRevision,
                                checkedReferenceTypeDefinitions)
                            || flags == 2)))
                {
                    throw Invalid("Unsupported parameter type or mode.");
                }
                variables.Add(new BoundVariable($"arg{param}", type,
                    nativeOutput is not null, default) {
                    IsOut = nativeOutput is not null,
                    IsInit = mode == 2 || initializationReferenceParameter,
                });
            }

            var (contractFirst, contracts) = defs.GetContractRange(row);
            var doesNotReturn = false;
            var requiresUnsafeCaller = false;
            var declaredRequiresUnsafeCaller = false;
            if (contracts != 0)
            {
                for (var contractIndex = 0; contractIndex < contracts; contractIndex++)
                {
                    var clause = contractTable.GetRow(contractFirst + contractIndex);
                    var kind = BinaryPrimitives.ReadUInt16LittleEndian(clause[4..]);
                    if (standardInitializationReferenceBodies
                        && (kind is 0x0002 or 0x000B))
                    {
                        continue;
                    }
                    if (standardConstruction2MethodLinkingProfile
                        && kind == 4)
                    {
                        continue;
                    }
                    if ((kind is 0x10 or 0x11 or 0x12)
                        && (isNativeImport || isNativeCallable || isNativeVTable
                            || nativeFunctionPointerSignatureTable.HasSignatureForMethod(row)))
                    {
                        continue;
                    }
                    if (kind == 0x0E && standardNativeParameterProfile
                        && isStandardNativeImport)
                    {
                        continue;
                    }

                    var effectMask = clause.Length == 12
                        ? BinaryPrimitives.ReadUInt32LittleEndian(clause[8..]) : 0;
                    if (clause.Length != 12 || kind != 1 || (effectMask & ~6u) != 0)
                    {
                        throw Invalid("Unsupported method contract.");
                    }
                    doesNotReturn = (effectMask & 2) != 0;
                    requiresUnsafeCaller = (effectMask & 4) != 0;
                    if (schema.UsesDevelopmentFunctionPointerEffects())
                    {
                        declaredRequiresUnsafeCaller = (effectMask & 4) != 0;
                    }
                }
            }
            if (schema.UsesDevelopmentFunctionPointerEffects())
            {
                var nativeAbiRole = contractTable.HasNativeAbi(row)
                    ? contractTable.GetNativeAbi(row).Role : (byte)0;
                requiresUnsafeCaller = declaredRequiresUnsafeCaller
                    || nativeAbiRole is 1 or 2 or 3 or 4;
            }
            else if (standardConstruction2MethodLinkingProfile)
            {
                declaredRequiresUnsafeCaller = requiresUnsafeCaller;
            }

            DirectNativeImport? directNativeImport = null;
            NativeVTableMethodDefinition? nativeVTableMethod = null;
            BoundModuleImport? moduleImport = null;
            if (isNativeImport)
            {
                var standardImport = isStandardNativeImport
                    ? standardNativeImports.GetImportForMethod(row)
                    : default;
                var importLibraryName = isStandardNativeImport
                    ? standardNativeImports.GetLibraryName(standardImport)
                    : nativeImports.GetLibraryName(row);
                var importEntryName = isStandardNativeImport
                    ? standardNativeImports.GetEntryName(standardImport)
                    : nativeImports.GetExportName(row);
                var directOutputs = ImmutableArray.CreateBuilder<DirectNativeOutput>(nativeOutputs.Length);
                foreach (var output in nativeOutputs)
                {
                    directOutputs.Add(new DirectNativeOutput(output.ParameterOrdinal,
                        output.PointeeType,
                        output.ResultCondition, output.FailureDisposition));
                }
                var directParameters = ImmutableArray.CreateBuilder<DirectNativeParameter>(
                    nativeParameters.IsDefault ? 0 : nativeParameters.Length);
                if (!nativeParameters.IsDefaultOrEmpty)
                {
                    foreach (var parameter in nativeParameters)
                    {
                        var typeSpecRow = parameters.GetTypeSpec(first + parameter.ParameterOrdinal);
                        var pointeeType = NativeParameterPointee(types, typeSpecRow,
                            parameter.Access, nativePointerTypeDefinitions, layouts,
                            structureTypeByTypeDef);
                        if (parameter.Access is < 1 or > 3
                            || parameter.Retention != 1 || parameter.ExtentKind is not (0 or 1)
                            || parameter.PointerDepth != 1 || parameter.RequiredAlignment != 0)
                        {
                            throw Invalid("Invalid direct native parameter contract.");
                        }

                        directParameters.Add(new DirectNativeParameter(parameter.ParameterOrdinal,
                            pointeeType, (NativeParameterAccess)(parameter.Access - 1),
                            NativeParameterRetention.NoEscape, parameter.Extent));
                    }
                }
                directNativeImport = new DirectNativeImport(importLibraryName,
                    importEntryName, directOutputs.MoveToImmutable()) {
                    CallingConvention = isStandardNativeImport
                        ? (NativeCallingConvention)standardImport.CallConv
                        : schema.UsesDevelopmentNativeConventions()
                            ? TrRmDevelopmentNativeConventions.DecodeConvention(
                                contractTable.GetNativeAbi(row)
                                    .ConventionOrReserved, 1)
                            : NativeCallingConvention.Native,
                    ResolutionMode = isStandardNativeImport
                        ? (NativeImportResolutionMode)standardImport.ResolutionMode
                        : nativeImports.GetResolutionMode(row),
                    ResolutionPolicy = isStandardNativeImport
                        ? (NativeImportResolutionPolicy)standardImport.ResolutionPolicy
                        : nativeImports.GetResolutionPolicy(row),
                    Parameters = directParameters.MoveToImmutable(),
                };
            }
            else if (isNativeVTable)
            {
                var directOutputs = ImmutableArray.CreateBuilder<DirectNativeOutput>(
                    nativeOutputs.Length);
                foreach (var output in nativeOutputs)
                {
                    directOutputs.Add(new DirectNativeOutput(output.ParameterOrdinal,
                        output.PointeeType, output.ResultCondition, output.FailureDisposition));
                }
                var directParameters = ImmutableArray.CreateBuilder<DirectNativeParameter>(
                    nativeParameters.IsDefault ? 0 : nativeParameters.Length);
                if (!nativeParameters.IsDefaultOrEmpty)
                {
                    foreach (var parameter in nativeParameters)
                    {
                        var typeSpecRow = parameters.GetTypeSpec(first + parameter.ParameterOrdinal);
                        var pointeeType = NativeParameterPointee(types, typeSpecRow,
                            parameter.Access, nativePointerTypeDefinitions, layouts,
                            structureTypeByTypeDef);
                        directParameters.Add(new DirectNativeParameter(parameter.ParameterOrdinal,
                            pointeeType, (NativeParameterAccess)(parameter.Access - 1),
                            NativeParameterRetention.NoEscape, parameter.Extent));
                    }
                }

                nativeVTableMethod = new NativeVTableMethodDefinition(
                    nativeVTables.GetSlot(row), directParameters.MoveToImmutable(),
                    nativeVTables.GetVTableByteOffset(row)) {
                    Outputs = directOutputs.MoveToImmutable(),
                };
            }
            if (isImportedNativeCallable || isImportedNativeVTable
                || isImportedNativeParameterMethod)
            {
                if (!imports.TryGetExpectedBinding(0x1600_0000u | (uint)row,
                    out var importBinding))
                {
                    throw Invalid("Imported native declaration has no retained provider binding.");
                }

                moduleImport = new BoundModuleImport(importBinding.ProducerIdentity,
                    importBinding.ExportKey, importBinding.TargetRequirement,
                    importBinding.RuntimePolicy);
            }
            else if (isImportedInitializationReferenceMethod
                || isImportedConstruction2Method)
            {
                if (selectedModuleImports is null
                    || !selectedModuleImports.TryGetValue(row,
                        out var selectedModuleImport))
                {
                    throw Invalid(
                        "A selected Standard MethodDef Import has no validated provider binding.");
                }

                moduleImport = selectedModuleImport;
            }
            functionIndexByMethodRow[row] = functions.Count;
            var resultTypeRow = sigs.GetResultTypeSpec(signature);
            var returnType = isNativeImport
                ? TrRmNativeImportTypeContract.TryGetDirectNativeImportResultType(
                    types, resultTypeRow, out _)
                        ? FromNativeValueRow(types, resultTypeRow,
                            nativePointerTypeDefinitions, structureTypeByTypeDef,
                            imports, checkedReferenceTypeDefinitions,
                            checkedReferenceTypeIndexes,
                            nominalClassTypesByTypeSpecReference,
                            functionPointerSignatureIndexByAnchorTypeSpec:
                                standardFunctionPointerAnchorProfile
                                    ? functionPointerSignatureIndexByTypeSpecRow
                                    : null)
                        : throw Invalid("Unsupported direct native import result type.")
                : FromNativeValueRow(types, resultTypeRow,
                    nativePointerTypeDefinitions, structureTypeByTypeDef,
                    imports, checkedReferenceTypeDefinitions,
                    checkedReferenceTypeIndexes,
                    nominalClassTypesByTypeSpecReference,
                    functionPointerSignatureIndexByAnchorTypeSpec:
                        standardFunctionPointerAnchorProfile
                            ? functionPointerSignatureIndexByTypeSpecRow
                            : null);
            var function = new BoundFunction(name, returnType,
                variables.ToImmutable(), count, new BoundBlock([], default), default) {
                DoesNotReturn = doesNotReturn,
                RequiresUnsafeCaller = requiresUnsafeCaller,
                DeclaredRequiresUnsafeCaller = declaredRequiresUnsafeCaller,
                IsExternal = isNativeImport || isImportedNativeCallable
                    || isImportedInitializationReferenceMethod
                    || isImportedConstruction2Method,
                IsModuleReference = isImportedNativeParameterMethod
                    || isImportedInitializationReferenceMethod
                    || isImportedConstruction2Method,
                Intrinsic = isNativeVTable ? BoundIntrinsic.NativeVTableCall : BoundIntrinsic.None,
                DirectNativeImport = directNativeImport,
                ModuleImport = moduleImport,
                DependencyContracts = standardConstruction2MethodLinkingProfile
                    ? DecodeC1M1ClassMethodDependencyContracts(
                        contractTable.GetDependencyContracts(row), count,
                        hasReceiver: false)
                    : [],
                NativeVTableMethod = nativeVTableMethod,
                NativeFunctionPointerType = nativeCallableTypeByMethod[row],
                NativeCallableConvention = schema.UsesDevelopmentNativeConventions()
                    && isNativeCallable
                        ? TrRmDevelopmentNativeConventions.DecodeConvention(
                            contractTable.GetNativeAbi(row).ConventionOrReserved, 2)
                        : null,
                NativeCallableEntryName = isNativeCallable
                    && nativeCallables.HasCallableEntryForMethod(row)
                        ? nativeCallables.GetEntryName(row) : null,
                IsPrivate = flags == 0,
                IsInternal = flags == 1,
                HasStandardCheckedOutputStateMetadata
                    = standardInitializationReferenceBodies,
            };
            if (standardInitializationReferenceBodies)
            {
                var checkedOutputMetadata
                    = ReadStandardInitializationOutputStateVectors(row,
                        function, checkedReferenceTypeDefinitions,
                        validatedTables);
                function = function with {
                    CheckedOutputSelectors = checkedOutputMetadata.Selectors,
                    CheckedOutputStates = checkedOutputMetadata.OutputStates,
                    CheckedOutputStatePredicates
                        = checkedOutputMetadata.GeneralPredicates,
                };
            }
            functions.Add(function);
        }
        var layoutQueryImportCount = 0;
        var initializationReferenceTypeImportCount = 0;
        if (schema is TrRmArtifactSchema.Standard
            or TrRmArtifactSchema.InitializationReferenceLocalFieldLayoutDraft
            or TrRmArtifactSchema.InitializationReferenceLocalFieldLayoutNestedFieldDestinationsDraft)
        {
            for (var importRow = 1; importRow <= imports.Count; importRow++)
            {
                if (!selectedImportRows.Contains(importRow))
                {
                    continue;
                }
                var definitionKind = imports
                    .GetExpectedDefinitionForImportRow(importRow) >> 24;
                if (schema.UsesInitializationReferenceLocalFieldLayout()
                    && definitionKind == 0x13)
                {
                    initializationReferenceTypeImportCount++;
                }
                else if (definitionKind is 0x13 or 0x14)
                {
                    layoutQueryImportCount++;
                }
            }
        }

        if (standardInitializationReferenceBodies
            && (layoutQueryImportCount != 0
                || importedInitializationReferenceMethodCount
                    + importedNativeParameterMethodCount
                    != (selectedModuleImports?.Count ?? 0)))
        {
            throw Invalid(
                "The Standard initialization-reference profile has an unbound or unsupported Import.");
        }

        if (schema.UsesForeignNativeInterface())
        {
            foreach (var importRow in selectedImportRows)
            {
                var definition
                    = imports.GetExpectedDefinitionForImportRow(importRow);
                var definitionKind = definition >> 24;
                var expectedRow = checked((int)(definition
                    & 0x00FF_FFFF));
                if ((definitionKind is 0x13 or 0x14)
                    || definitionKind == 0x16
                        && (foreignInterfaceMethodRows.Contains(expectedRow)
                            || nativeFunctionPointerSignatureTable
                                .HasSignatureForMethod(expectedRow)))
                {
                    foreignNativeInterfaceImportCount++;
                }
            }
        }

        var selectedConstruction2MethodImportCount = 0;
        var selectedConstruction2ClassTypeImportCount = 0;
        var selectedConstruction2FieldImportCount = 0;
        if (standardConstruction2MethodLinkingProfile)
        {
            foreach (var importRow in selectedImportRows)
            {
                var definition
                    = imports.GetExpectedDefinitionForImportRow(importRow);
                var definitionKind = definition >> 24;
                if (definitionKind == 0x16)
                {
                    var methodRow = checked((int)(
                        definition & 0x00FF_FFFF));
                    if (!standardNativeImports
                            .HasImportForMethod(methodRow)
                        && !nativeImports.HasImportForMethod(methodRow)
                        && !nativeCallables.HasCallableForMethod(methodRow)
                        && !nativeVTables.HasVTableForMethod(methodRow))
                    {
                        selectedConstruction2MethodImportCount++;
                    }
                }
                else if (definitionKind == 0x13
                    && typeDefs.GetKind(checked((int)(
                        definition & 0x00FF_FFFF))) == 2)
                {
                    selectedConstruction2ClassTypeImportCount++;
                }
                else if (definitionKind == 0x14)
                {
                    selectedConstruction2FieldImportCount++;
                }
            }
        }

        if (importedNativeCallableCount + importedNativeVTableCount
                + importedNativeParameterMethodCount
                + importedInitializationReferenceMethodCount
                + selectedConstruction2MethodImportCount
                + selectedConstruction2ClassTypeImportCount
                + selectedConstruction2FieldImportCount
                + initializationReferenceTypeImportCount
                + layoutQueryImportCount
                + foreignNativeInterfaceImportCount
                    != selectedImportRows.Count)
        {
            throw Invalid(
                "Executable TRRM supports native callable/vtable imports and validated Standard LayoutQueries type/field imports.");
        }
        if (usedSignatureCount != selectedSignatures.Count)
        {
            throw Invalid("Orphan MethodSig is not part of a defined function.");
        }

        var entry = modules.GetEntryMethod(selectedModuleRow) - 1;
        if (entry >= 0
            && (entry + 1 < methodStart || entry + 1 >= methodEnd
                || functionIndexByMethodRow[entry + 1] < 0))
        {
            throw Invalid("Entry method must be a local int32 no-argument function.");
        }

        for (var row = methodStart; row < methodEnd; row++)
        {
            var functionIndex = functionIndexByMethodRow[row];
            if (foreignInterfaceMethodRows.Contains(row))
            {
                continue;
            }
            if (standardConstruction2MethodLinkingProfile
                && functionIndex < 0 && defs.GetOwnerType(row) != 0)
            {
                continue;
            }
            if (functionIndex < 0)
            {
                continue;
            }
            var declaration = functions[functionIndex];
            if (declaration.IsExternal || declaration.NativeVTableMethod is not null)
            {
                continue;
            }
            var count = declaration.ParameterCount;
            var body = bodies.GetRow(defs.GetBodyRow(row));
            TrRmScalarGraph? indexed = null;
            ImmutableArray<BoundVariable>.Builder variables;
            ImmutableArray<BoundStatement> statements;
            var bodyForm = body[0] & 0x0C;
            if (schema.UsesRegisterExceptionRegions()
                && bodyForm == 0x08)
            {
                throw Unsupported(
                    "The current B+E1 scalar loader does not consume compact-indexed Bodies.");
            }
            if (bodyForm == 0x08)
            {
                if (schema == TrRmArtifactSchema.Standard
                    && !standardInitializationReferenceBodies)
                {
                    throw Unsupported(
                        "The bounded Standard LayoutQueries loader does not consume compact-indexed Bodies.");
                }

                TrRmCompactIndexedBodyReader.ValidateOnly(bodies, row, modules, defs, sigs,
                    parameters, types, featureTable, nativeCallables, nativeImports,
                    nativeFunctionPointerSignatureTable, nativeVTables, declaration.DoesNotReturn);
                indexed = IndexedBuilder.DecodeIndexedBody(body, count,
                    declaration.ReturnType, declaration.DoesNotReturn, types, functions,
                    functionIndexByMethodRow,
                    nativeFunctionPointerSignatureDefinitions.ToImmutable(),
                    nativePointerTypeDefinitions, structureTypeByTypeDef);
                variables = ImmutableArray.CreateBuilder<BoundVariable>(indexed.Types.Length);
                variables.AddRange(declaration.Variables);
                for (var value = count; value < indexed.Types.Length; value++)
                {
                    variables.Add(new BoundVariable($"value{value + 1}", indexed.Types[value], false, default));
                }
                statements = [];
            }
            else if (bodyForm == 0x04)
            {
                variables = declaration.Variables.ToBuilder();
                _ = new TrRmCompactLinearBodyReader(bodies, row, modules, defs, sigs,
                    parameters, types, typeDefs, imports, nativeImports);
                statements = DecodeBody(body, count, variables, types, functions,
                    functionIndexByMethodRow,
                    nativeFunctionPointerSignatureDefinitions.ToImmutable(),
                    nativePointerTypeDefinitions: nativePointerTypeDefinitions,
                    structureTypeByTypeDef: structureTypeByTypeDef);
                if (statements.Any(static statement => statement is BoundLocal {
                    Initializer: TrRmScalarReinterpret or TrRmScalarConvert
                        or TrRmScalarOptionalNone
                        or TrRmScalarOptionalLift or TrRmScalarOptionalPresence
                        or TrRmScalarOptionalEquality or BoundBinary { Operator: TokenKind.ShiftLeft or TokenKind.ShiftRight }
                }
                    or BoundLocal { Initializer: BoundCall }
                    or BoundExpressionStatement { Expression: BoundCall }
                    or TrRmScalarDivRem))
                {
                    var operations = statements[..^1];
                    var returned = (BoundReturn)statements[^1];
                    var variableTypes = ImmutableArray.CreateBuilder<PrimitiveType>(variables.Count);
                    for (var variableIndex = 0; variableIndex < variables.Count; variableIndex++)
                    {
                        variableTypes.Add(variables[variableIndex].Type);
                    }
                    indexed = TrRmScalarGraph.FromAuditedBody(
                        [new TrRmScalarBlock([.. Enumerable.Range(1, count)],
                            operations, 0, null, null,
                            returned.Value is BoundRead read ? read.Variable + 1 : 0, 0)],
                        variableTypes.MoveToImmutable(),
                        count, declaration.ReturnType, declaration.DoesNotReturn);
                    statements = [];
                }
            }
            else if (bodyForm == 0)
            {
                variables = declaration.Variables.ToBuilder();
                statements = [];
                var registeredQueryBody = false;
                if (standardRegisterBodies)
                {
                    var registerBody = bodies.GetRegisterBody(
                        defs.GetBodyRow(row), contractTable.Count,
                        generalPredicateGraphsInitializationReferenceBodies);
                    var standardRegisteredQueryBody
                        = standardSchemaIdentity is { } classQuerySchema
                            && IsSelectedC1ClassInstanceLayoutSchemaIdentity(
                                classQuerySchema)
                            && registerBody.HasLayoutQueryOperation;
                    var standardScalarControlFlowBody
                        = generalPredicateGraphsInitializationReferenceBodies
                            && !standardConstruction2MethodLinkingProfile
                            && !standardRegisteredQueryBody
                            && IsStandardScalarControlFlowBody(registerBody,
                                declaration, types, imports,
                                nativePointerTypeDefinitions,
                                structureTypeByTypeDef,
                                checkedReferenceTypeDefinitions,
                                checkedReferenceTypeIndexes,
                                nominalClassTypesByTypeSpecReference);
                    var scalarConstants
                        = ImmutableArray<BoundConstant>.Empty;
                    if (registerBody.ConstantCount != 0
                        && (standardConstruction2MethodLinkingProfile
                            || registerBody.Schema
                                .UsesForeignNativeInterface()
                            || standardScalarControlFlowBody))
                    {
                        var constants
                            = ImmutableArray.CreateBuilder<BoundConstant>(
                                registerBody.ConstantCount);
                        var targetRequirement = modules.GetRequirements(
                            selectedModuleRow).TargetRequirement;
                        for (var constantIndex = 0;
                            constantIndex < registerBody.ConstantCount;
                            constantIndex++)
                        {
                            var constantOffset = constantIndex
                                * (sizeof(uint) * 2);
                            var typeSpecReference
                                = BinaryPrimitives.ReadUInt32LittleEndian(
                                    registerBody.ConstantMap.Slice(
                                        constantOffset));
                            var constantSourceRow = checked((int)
                                BinaryPrimitives.ReadUInt32LittleEndian(
                                    registerBody.ConstantMap.Slice(
                                        constantOffset + sizeof(uint))));
                            constants.Add(
                                TrRmConstantSourceValueValidator
                                    .MaterializeFrozenScalar(
                                        constantSourceRow,
                                        typeSpecReference,
                                        selectedModuleRow,
                                        targetRequirement,
                                        validatedTables.ConstantSources,
                                        validatedTables.FrozenValueNodes,
                                        validatedTables.Blobs,
                                        validatedTables.TypeSpecs,
                                        validatedTables.Features,
                                        validatedTables.Modules,
                                        validatedTables.A3LayoutResourceLedger));
                        }

                        scalarConstants = constants.MoveToImmutable();
                    }

                    if (standardRegisteredQueryBody)
                    {
                        statements = DecodeStandardRegisteredQueryBody(
                            registerBody, declaration, variables, modules,
                            typeDefs, types, fields, fieldPaths, imports,
                            nativePointerTypeDefinitions,
                            signatureIndexByAnchorMethod,
                            structureTypeByTypeDef,
                            standardClassQueryProgram,
                            modules.GetIdentity(defs.GetOwnerModule(row)));
                    }
                    else if (standardScalarControlFlowBody)
                    {
                        indexed = DecodeStandardScalarControlFlowBody(
                            registerBody, declaration, variables, types,
                            imports, nativePointerTypeDefinitions,
                            structureTypeByTypeDef,
                            checkedReferenceTypeDefinitions,
                            checkedReferenceTypeIndexes,
                            nominalClassTypesByTypeSpecReference,
                            scalarConstants);
                    }
                    else
                    {
                        statements
                            = DecodeStandardInitializationReferenceBody(
                                registerBody, declaration, variables,
                                typeDefs, fields, fieldPaths, types, imports,
                                standardNativeImports,
                                checkedReferenceTypeDefinitions,
                                checkedReferenceTypeIndexes,
                                nativePointerTypeDefinitions,
                                structureTypeByTypeDef, structures,
                                functions, functionIndexByMethodRow,
                                construction2MethodLinking:
                                    standardConstruction2MethodLinkingProfile,
                                scalarConstants: scalarConstants,
                                nominalClassTypesByTypeSpecReference:
                                    nominalClassTypesByTypeSpecReference,
                                foreignInterfaceBodyTargets:
                                    foreignInterfaceBodyTargets);
                    }

                    registeredQueryBody = true;
                }
                else if ((schema is TrRmArtifactSchema.Standard
                        or TrRmArtifactSchema.RegisterExceptionRegionsDraft)
                    && body.Length >= 17 && body[0] == 0x01
                    && body[1] == 0x01)
                {
                    var registerBody = bodies.GetRegisterBody(
                        defs.GetBodyRow(row), contractTable.Count);
                    if (registerBody.HasLayoutQueryOperation)
                    {
                        statements = DecodeStandardRegisteredQueryBody(
                            registerBody, declaration, variables, modules,
                            typeDefs, types, fields, fieldPaths, imports,
                            nativePointerTypeDefinitions,
                            signatureIndexByAnchorMethod,
                            structureTypeByTypeDef);
                        registeredQueryBody = true;
                    }
                }

                if (!registeredQueryBody)
                {
                    var outputBody = TrRmScalarInitOutputBodyReader.Read(bodies,
                        row, modules, defs, sigs, parameters, types,
                        contractBytes, contractTable, nativeImports,
                        nativeVTables, scalarInitOutputRuleRevision,
                        rootlessNativeValuesRuleRevision);
                    statements = DecodeOutputBody(outputBody, types, variables,
                        functions, functionIndexByMethodRow, structures,
                        checkedReferenceTypeDefinitions,
                        nativePointerTypeDefinitions, structureTypeByTypeDef,
                        scalarInitOutputRuleRevision);
                }
            }
            else
            {
                throw Invalid("Unsupported executable Body form.");
            }

            functions[functionIndex] = declaration with {
                Variables = variables.ToImmutable(),
                Body = new BoundBlock(statements, default),
                IndexedBody = indexed,
            };
        }
        if (entry >= 0)
        {
            var executableEntry = functions[functionIndexByMethodRow[entry + 1]];
            if (executableEntry.ReturnType != PrimitiveType.Int32
                || executableEntry.ParameterCount != 0 || executableEntry.IsExternal)
            {
                throw Invalid("Entry method must be a local int32 no-argument function.");
            }
        }
        var programFunctions = functions.ToImmutable();
        var entryFunctionIndex = entry < 0 ? -1 : functionIndexByMethodRow[entry + 1];
        var checkedReferenceTypes
            = checkedReferenceTypeDefinitions.ToImmutableArray();
        if (!CheckedReferenceTypeContract.IsValidTable(
                checkedReferenceTypes, nativePointerTypeDefinitions))
        {
            throw Invalid("Invalid checked-reference TypeSpec table.");
        }

        var program = new BoundProgram(programFunctions, entryFunctionIndex) {
            RuntimePolicy = BoundRuntimePolicy.FailFast,
            Structures = standardClassQueryProgram is null
                ? structures : standardClassQueryProgram.Structures,
            CoreExceptionIdentitySelections =
                coreExceptionIdentitySelections.IsDefault
                    ? [] : coreExceptionIdentitySelections,
            CoreContractFailureIdentitySelections =
                coreContractFailureIdentitySelections.IsDefault
                    ? [] : coreContractFailureIdentitySelections,
            NativeFunctionPointerSignatures = standardClassQueryProgram is null
                ? nativeFunctionPointerSignatureArray
                : standardClassQueryProgram.NativeFunctionPointerSignatures,
            NativePointerTypes = standardClassQueryProgram is null
                ? [.. nativePointerTypeDefinitions]
                : standardClassQueryProgram.NativePointerTypes,
            StandardFunctionPointerTypeDefClosureRows =
                standardClassQueryProgram is null
                    ? standardFunctionPointerTypeDefClosureRows : [],
            CheckedReferenceTypes = standardClassQueryProgram is null
                ? checkedReferenceTypes
                : standardClassQueryProgram.CheckedReferenceTypes,
            EnumDefinitions = standardClassQueryProgram is null
                ? [] : standardClassQueryProgram.EnumDefinitions,
            Interfaces = standardClassQueryProgram is null
                ? [] : standardClassQueryProgram.Interfaces,
            InterfaceWitnesses = standardClassQueryProgram is null
                ? [] : standardClassQueryProgram.InterfaceWitnesses,
            NativeFixedArrayTypes = standardClassQueryProgram is null
                ? [] : standardClassQueryProgram.NativeFixedArrayTypes,
            SpanTypes = standardClassQueryProgram is null
                ? [] : standardClassQueryProgram.SpanTypes,
            GenericNominalTypeDefinitions = standardClassQueryProgram is null
                ? [] : standardClassQueryProgram.GenericNominalTypeDefinitions,
            GenericNominalTypeInstances = standardClassQueryProgram is null
                ? [] : standardClassQueryProgram.GenericNominalTypeInstances,
            StandardClassDeclarations = standardClassQueryProgram is null
                ? [] : standardClassQueryProgram.StandardClassDeclarations,
        };
        try
        {
            if (entry == -1)
            {
                SemanticVerifier.VerifyLibraryReferences(program);
            }
            else
            {
                SemanticVerifier.VerifyReferences(program);
            }
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            throw new InvalidDataException($"TRRM executable semantic audit failed: {exception.Message}", exception);
        }
        return program;
    }

    /// <summary>Reads a validated TRRM library and exposes its public native declarations to source binding.</summary>
    public static CompilationReference CreateReference(byte[] providerArtifact)
        => CreateReference(providerArtifact, TrRmArtifactSchema.Development);

    /// <summary>Reads nominal TypeDef identities and supported signatures from a checked Standard TRRM provider.</summary>
    /// <param name="providerArtifact">The Standard TRRM provider artifact.</param>
    /// <returns>A source reference carrying the provider's exported nominal declarations.</returns>
    /// <exception cref="NotSupportedException">
    /// The artifact contains no generic nominal definitions, multiple logical Modules, or unsupported generic signature contracts.
    /// </exception>
    /// <exception cref="InvalidDataException">The provider artifact is structurally invalid.</exception>
    public static CompilationReference CreateGenericNominalTypeReference(
        byte[] providerArtifact)
        => CreateGenericNominalTypeReference(providerArtifact,
            TrRmArtifactSchema.Standard);

    internal static CompilationReference
        CreateNominalEnumsDraftGenericNominalTypeReference(
            byte[] providerArtifact)
        => CreateGenericNominalTypeReference(providerArtifact,
            TrRmArtifactSchema.NominalEnumsDraft);

    internal static CompilationReference
        CreateGenericConstantMethodsDraftGenericNominalTypeReference(
            byte[] providerArtifact)
        => CreateGenericNominalTypeReference(providerArtifact,
            TrRmArtifactSchema.GenericConstantMethodsDraft);

    internal static CompilationReference
        CreateGenericConstantMethodsDraftGenericNominalTypeReference(
            byte[] providerArtifact, IReadOnlyList<byte[]> providers)
    {
        ArgumentNullException.ThrowIfNull(providers);
        return CreateGenericNominalTypeReference(providerArtifact,
            TrRmArtifactSchema.GenericConstantMethodsDraft, providers);
    }

    private static CompilationReference CreateGenericNominalTypeReference(
        byte[] providerArtifact, TrRmArtifactSchema schema,
        IReadOnlyList<byte[]>? providers = null)
    {
        ArgumentNullException.ThrowIfNull(providerArtifact);
        var index = new TrRmArtifactIndex(providerArtifact, schema);
        var tables = schema.SupportsNominalEnums()
            ? TrRmStructuralMetadataValidator
                .ValidateNominalEnumsDraftAndGetExecutionTables(index)
            : TrRmStructuralMetadataValidator
                .ValidateAndGetExecutionTables(index);
        if (tables.Modules.Count != 1)
        {
            throw Unsupported(
                "A generic nominal source reference requires one logical provider Module.");
        }

        var providerIdentity = tables.Modules.GetIdentity(1);
        var (targetRequirement, runtimePolicy) = tables.Modules.GetRequirements(1);
        var enumDefinitions = LoadGenericNominalEnumDefinitions(tables);
        var wrapperStructures = LoadGenericNominalTransparentWrappers(
            tables, providerArtifact, providers ?? [],
            out var wrapperDomainsByTypeDef,
            out var importedGenericDefinitions);
        var localDefinitions = CreateGenericNominalTypeDefinitions(tables,
            providerIdentity, enumDefinitions, wrapperDomainsByTypeDef);
        var definitionBuilder = ImmutableArray.CreateBuilder<
            BoundGenericNominalTypeDefinition>(
                localDefinitions.Length + importedGenericDefinitions.Length);
        var definitionIdentities = new HashSet<
            BoundGenericNominalTypeIdentity>();
        foreach (var definition in localDefinitions
            .Concat(importedGenericDefinitions))
        {
            if (!definitionIdentities.Add(definition.Identity))
            {
                throw InvalidWrapperBinding();
            }
            definitionBuilder.Add(definition);
        }
        var definitions = definitionBuilder.MoveToImmutable();
        if (!definitions.Any(static definition => !definition.Formals.IsEmpty))
        {
            throw Unsupported(
                "A generic nominal source reference requires at least one exported generic TypeDef.");
        }

        return CreateGenericNominalTypeReference(tables, definitions,
            providerIdentity, targetRequirement, runtimePolicy,
            enumDefinitions, wrapperStructures);
    }

    internal static CompilationReference
        CreateDevelopmentFunctionPointerEffectsDraftReference(byte[] providerArtifact)
        => CreateReference(providerArtifact,
            TrRmArtifactSchema.DevelopmentFunctionPointerEffectsDraft);

    internal static CompilationReference
        CreateDevelopmentNativeConventionsDraftReference(byte[] providerArtifact)
        => CreateReference(providerArtifact,
            TrRmArtifactSchema.DevelopmentNativeConventionsDraft);

    private static CompilationReference CreateReference(byte[] providerArtifact,
        TrRmArtifactSchema schema)
    {
        ArgumentNullException.ThrowIfNull(providerArtifact);
        var artifactIndex = new TrRmArtifactIndex(providerArtifact, schema);
        var validatedTables = schema.UsesDevelopmentNativeConventions()
            ? TrRmStructuralMetadataValidator
                .ValidateDevelopmentNativeConventionsDraftAndGetExecutionTables(
                    artifactIndex)
            : schema.UsesDevelopmentFunctionPointerEffects()
            ? TrRmStructuralMetadataValidator
                .ValidateDevelopmentFunctionPointerEffectsDraftAndGetExecutionTables(
                    artifactIndex)
            : TrRmStructuralMetadataValidator
                .ValidateDevelopmentAndGetExecutionTables(artifactIndex);
        var providerIdentity = validatedTables.Modules.GetIdentity(1);
        var (targetRequirement, runtimePolicy) = validatedTables.Modules.GetRequirements(1);
        var genericNominalTypeDefinitions = CreateGenericNominalTypeDefinitions(
            validatedTables, providerIdentity, []);
        if (genericNominalTypeDefinitions.Any(static definition =>
                !definition.Formals.IsEmpty)
            && (validatedTables.Methods.Count == 0
                || ContainsConstructedGenericNominalTypeUse(validatedTables)))
        {
            return CreateGenericNominalTypeReference(validatedTables,
                genericNominalTypeDefinitions, providerIdentity,
                targetRequirement, runtimePolicy, [], []);
        }
        if (validatedTables.Methods.Count == 0)
        {
            if (genericNominalTypeDefinitions.IsEmpty)
            {
                throw Unsupported("A TRRM reference requires public declarations.");
            }

            return new CompilationReference([], [], (BoundRuntimePolicy)runtimePolicy,
                genericNominalTypeDefinitions: genericNominalTypeDefinitions);
        }

        var program = LoadDevelopment(providerArtifact, schema);
        if (program.EntryPoint != -1
            || !program.Classes.IsEmpty || !program.Buffers.IsEmpty
            || program.Functions.Any(static function =>
                !function.IsPrivate && !function.IsInternal
                    && (function.IsExternal
                        ? function.DirectNativeImport is null
                            && function.NativeFunctionPointerType is null
                            && function.NativeVTableMethod is null
                        : function.NativeFunctionPointerType is null
                            && function.NativeVTableMethod is null)))
        {
            throw Unsupported("A TRRM native reference requires public native declarations.");
        }

        var structureMap = new int[program.Structures.Length];
        Array.Fill(structureMap, -1);
        var includedStructureIndexes = new List<int>();
        var structures = ImmutableArray.CreateBuilder<BoundStruct>();
        for (var index = 0; index < program.Structures.Length; index++)
        {
            if (program.Structures[index].BlittableTargetRequirement == 0)
            {
                continue;
            }

            structureMap[index] = includedStructureIndexes.Count;
            includedStructureIndexes.Add(index);
        }
        foreach (var index in includedStructureIndexes)
        {
            var structure = program.Structures[index];
            var fields = ImmutableArray.CreateBuilder<BoundStructField>(structure.Fields.Length);
            foreach (var field in structure.Fields)
            {
                fields.Add(field with { Type = RemapStructureType(field.Type, structureMap) });
            }
            structures.Add(structure with { Fields = fields.MoveToImmutable() });
        }

        var nativePointerTypes = ImmutableArray.CreateBuilder<NativePointerTypeDefinition>(
            program.NativePointerTypes.Length);
        foreach (var pointerType in program.NativePointerTypes)
        {
            nativePointerTypes.Add(pointerType with {
                PointeeType = RemapStructureType(pointerType.PointeeType, structureMap),
            });
        }

        var functions = program.Functions
            .Where(static function => !function.IsPrivate && !function.IsInternal)
            .Select(function => RemapNativeImportStructureTypes(function, structureMap))
            .Select(function => function.NativeFunctionPointerType is not null
                || function.NativeVTableMethod is not null
                ? function with {
                    IsExternal = true,
                    Intrinsic = BoundIntrinsic.None,
                    Body = new BoundBlock([], default),
                    IndexedBody = null,
                    NativeCallableEntryName = null,
                    ModuleImport = function.ModuleImport ?? new BoundModuleImport(providerIdentity,
                        function.Name, targetRequirement, runtimePolicy),
                }
                : function)
            .Select(static function => function.NativeVTableMethod is not null
                ? function with {
                    IsExternal = false,
                    Intrinsic = BoundIntrinsic.NativeVTableCall,
                }
                : function)
            .ToImmutableArray();
        if (functions.IsEmpty && genericNominalTypeDefinitions.IsEmpty)
        {
            throw Unsupported("A TRRM reference requires at least one public declaration.");
        }

        return new CompilationReference(structures.ToImmutable(), functions, program.RuntimePolicy,
            nativeFunctionPointerSignatures: program.NativeFunctionPointerSignatures,
            nativePointerTypes: nativePointerTypes.MoveToImmutable(),
            genericNominalTypeDefinitions: genericNominalTypeDefinitions);
    }

    private static bool ContainsConstructedGenericNominalTypeUse(
        TrRmStructuralMetadataValidator.ValidatedExecutionTables tables)
    {
        for (var method = 1; method <= tables.Methods.Count; method++)
        {
            var signature = tables.Methods.GetSignature(method);
            if (ContainsConstructedGenericNominalTypeSpec(
                tables.Signatures.GetResultTypeSpec(signature), tables.TypeSpecs, 0))
            {
                return true;
            }

            var (first, count) = tables.Signatures.GetParamRange(signature);
            for (var parameter = 0; parameter < count; parameter++)
            {
                if (ContainsConstructedGenericNominalTypeSpec(
                    tables.Parameters.GetTypeSpec(first + parameter),
                    tables.TypeSpecs, 0))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool ContainsConstructedGenericNominalTypeSpec(
        uint typeSpecRef, TrRmTypeSpecTableReader typeSpecs, int depth)
    {
        if (depth > SemanticVerifier.MaximumDepth)
        {
            throw Invalid("A method signature TypeSpec exceeds the nesting limit.");
        }

        var typeSpec = typeSpecs.GetTypeSpecRef(typeSpecRef,
            allowInitQualifier: true);
        if (!typeSpec.IsRow)
        {
            return false;
        }
        if (typeSpec.Form == 0x05)
        {
            return true;
        }

        var row = typeSpecs.GetRow(typeSpec.Row);
        if (typeSpec.Form is (>= 0x06 and <= 0x0B) or 0x0E)
        {
            return ContainsConstructedGenericNominalTypeSpec(
                BinaryPrimitives.ReadUInt32LittleEndian(row[2..]),
                typeSpecs, depth + 1);
        }
        if (typeSpec.Form == 0x0C)
        {
            return ContainsConstructedGenericNominalTypeSpec(
                BinaryPrimitives.ReadUInt32LittleEndian(row[3..]),
                typeSpecs, depth + 1);
        }

        return false;
    }

    private static CompilationReference CreateGenericNominalTypeReference(
        TrRmStructuralMetadataValidator.ValidatedExecutionTables tables,
        ImmutableArray<BoundGenericNominalTypeDefinition> definitions,
        string providerIdentity, uint targetRequirement, uint runtimePolicy,
        ImmutableArray<BoundEnumDefinition> enumDefinitions,
        ImmutableArray<BoundStruct> structures)
    {
        var functions = ImmutableArray.CreateBuilder<BoundFunction>();
        var instances = ImmutableArray.CreateBuilder<BoundGenericNominalTypeInstance>();
        var instanceByKey = new Dictionary<string, PrimitiveType>(StringComparer.Ordinal);
        var nativePointerTypes =
            ImmutableArray.CreateBuilder<NativePointerTypeDefinition>();
        var nativePointerTypeIndexes =
            new Dictionary<(PrimitiveType PointeeType, bool IsWritable), PrimitiveType>();
        var exports = tables.Exports;
        var signatures = tables.Signatures;
        var methods = tables.Methods;
        var parameters = tables.Parameters;
        for (var methodRow = 1; methodRow <= methods.Count; methodRow++)
        {
            var definitionId = 0x1600_0000u | (uint)methodRow;
            if (methods.GetOwnerModule(methodRow) != 1
                || methods.GetOwnerType(methodRow) != 0
                || (methods.GetFlags(methodRow) & 3) != 2
                || !exports.TryGetExportKeyForDefinition(1, definitionId,
                    out var exportKey))
            {
                continue;
            }
            if (methods.GetContractRange(methodRow).Count != 0
                || signatures.GetGenericCount(methods.GetSignature(methodRow)) != 0
                || signatures.GetReceiverTypeSpec(methods.GetSignature(methodRow)) != 0
                || (tables.NativeImports.Count != 0
                    && tables.NativeImports.HasImportForMethod(methodRow))
                || (tables.NativeCallables.Count != 0
                    && tables.NativeCallables.HasCallableForMethod(methodRow))
                || (tables.NativeVTables.Count != 0
                    && tables.NativeVTables.HasVTableForMethod(methodRow))
                || (tables.NativeFunctionPointerSignatures.Count != 0
                    && tables.NativeFunctionPointerSignatures.HasSignatureForMethod(methodRow)))
            {
                throw Unsupported(
                    "The generic nominal source-reference path requires top-level methods without additional contracts.");
            }
            if (functions.Count >= SemanticVerifier.MaximumFunctions)
            {
                throw Unsupported("The generic nominal source-reference function limit was exceeded.");
            }

            var signatureRow = methods.GetSignature(methodRow);
            var returnType = ReadGenericNominalTypeSpec(
                signatures.GetResultTypeSpec(signatureRow), tables, definitions,
                providerIdentity, instances, instanceByKey, nativePointerTypes,
                nativePointerTypeIndexes, 0);
            var (firstParameter, parameterCount) =
                signatures.GetParamRange(signatureRow);
            if (parameterCount > SemanticVerifier.MaximumArguments)
            {
                throw Unsupported("A generic nominal source-reference signature exceeds the parameter limit.");
            }

            var variables = ImmutableArray.CreateBuilder<BoundVariable>(parameterCount);
            for (var parameter = 0; parameter < parameterCount; parameter++)
            {
                var parameterRow = firstParameter + parameter;
                if (parameters.GetMode(parameterRow) != 0)
                {
                    throw Unsupported(
                        "The generic nominal source-reference path supports only by-value parameters.");
                }

                var parameterType = ReadGenericNominalTypeSpec(
                    parameters.GetTypeSpec(parameterRow), tables, definitions,
                    providerIdentity, instances, instanceByKey, nativePointerTypes,
                    nativePointerTypeIndexes, 0);
                if (parameterType == PrimitiveType.Void)
                {
                    throw new InvalidDataException(
                        "A generic nominal source-reference parameter has void type.");
                }
                variables.Add(new BoundVariable($"arg{parameter}", parameterType,
                    false, default));
            }

            functions.Add(new BoundFunction(exportKey, returnType,
                variables.MoveToImmutable(), parameterCount,
                new BoundBlock([], default), default) {
                IsExternal = true,
                IsModuleReference = true,
                ModuleImport = new BoundModuleImport(providerIdentity, exportKey,
                    targetRequirement, runtimePolicy),
            });
        }

        return new CompilationReference(structures, functions.ToImmutable(),
            (BoundRuntimePolicy)runtimePolicy,
            enumDefinitions: enumDefinitions,
            genericNominalTypeDefinitions: definitions,
            genericNominalTypeInstances: instances.ToImmutable(),
            nativePointerTypes: nativePointerTypes.ToImmutable());
    }

    private static PrimitiveType ReadGenericNominalTypeSpec(
        uint typeSpecRef,
        TrRmStructuralMetadataValidator.ValidatedExecutionTables tables,
        ImmutableArray<BoundGenericNominalTypeDefinition> definitions,
        string providerIdentity,
        ImmutableArray<BoundGenericNominalTypeInstance>.Builder instances,
        Dictionary<string, PrimitiveType> instanceByKey,
        ImmutableArray<NativePointerTypeDefinition>.Builder nativePointerTypes,
        Dictionary<(PrimitiveType PointeeType, bool IsWritable), PrimitiveType>
            nativePointerTypeIndexes,
        int depth, int methodRow = 0,
        ImmutableArray<PrimitiveType> methodFormalTypes = default,
        int methodFormalTypeOwnerOrdinal = -1,
        GenericMethodReadContext? methodContext = null,
        int methodArtifactIndex = 0, int methodModuleRow = 0,
        TrRmGenericMethodFormalImport[]? methodFormalRecords = null)
    {
        if (depth > SemanticVerifier.MaximumDepth)
        {
            throw Unsupported("A generic nominal TypeSpec exceeds the type nesting limit.");
        }

        var typeSpec = tables.TypeSpecs.GetTypeSpecRef(typeSpecRef);
        if (typeSpec.Form != 0x05 || !typeSpec.IsRow)
        {
            var argument = ReadGenericNominalTypeArgument(typeSpecRef, tables,
                definitions, providerIdentity, instances, instanceByKey,
                nativePointerTypes, nativePointerTypeIndexes, depth + 1,
                methodRow, methodFormalTypes,
                methodFormalTypeOwnerOrdinal, methodContext,
                methodArtifactIndex, methodModuleRow, methodFormalRecords);
            if (argument.NominalTypeIdentity is null)
            {
                if (argument.TypeSpecForm == 0x06
                    && BoundGenericNominalTypeTypes.IsType(argument.Type))
                {
                    var innerInstance = instances[
                        BoundGenericNominalTypeTypes.Index(argument.Type)];
                    var innerDefinition = definitions.FirstOrDefault(
                        definition => definition.Identity
                            == innerInstance.Definition);
                    if (innerDefinition is null
                        || innerDefinition.NominalKind != 2)
                    {
                        throw Unsupported(
                            "G1 optional nominal wrappers are supported only for class TypeDefs.");
                    }

                    return AddGenericNominalTypeInstance(
                        innerInstance.Definition, innerInstance.Arguments,
                        argument.Qualifiers, typeSpecRef, providerIdentity,
                        0x06, instances, instanceByKey);
                }

                if (argument.TypeSpecForm != 0)
                {
                    throw Unsupported(
                        "The generic nominal source-reference path cannot represent this complete signature TypeSpec.");
                }
                return argument.Type;
            }

            var nominalDefinition = definitions.FirstOrDefault(definition =>
                definition.Identity == argument.NominalTypeIdentity.Value);
            if (nominalDefinition is null
                || !nominalDefinition.Formals.IsEmpty)
            {
                throw new InvalidDataException(
                    "A complete TypeSpec cannot refer to an open generic nominal definition.");
            }

            if (argument.TypeSpecForm == 0x06
                && nominalDefinition.NominalKind != 2)
            {
                throw Unsupported(
                    "G1 optional nominal wrappers are supported only for class TypeDefs.");
            }
            if (argument.TypeSpecForm is not (0 or 0x02 or 0x03 or 0x06))
            {
                throw Unsupported(
                    "The generic nominal source-reference path cannot represent this complete signature TypeSpec.");
            }

            return AddGenericNominalTypeInstance(argument.NominalTypeIdentity.Value,
                [], argument.Qualifiers, typeSpecRef, providerIdentity,
                argument.TypeSpecForm == 0x06 ? (byte)0x06 : (byte)0,
                instances, instanceByKey);
        }

        var constructed = tables.TypeSpecs.GetRow(typeSpec.Row);
        var genericTypeSpecRef = BinaryPrimitives.ReadUInt32LittleEndian(
            constructed[2..]);
        var genericTypeSpec = tables.TypeSpecs.GetTypeSpecRef(genericTypeSpecRef);
        if (!genericTypeSpec.IsRow
            || genericTypeSpec.Form is not (0x02 or 0x03))
        {
            throw Unsupported(
                "The generic nominal source-reference path requires a TypeDef or TypeDef Import identity.");
        }

        var typeDefRow = tables.TypeSpecs.GetNominalTypeDefRow(
            genericTypeSpecRef, tables.TypeDefs, tables.Imports);
        var typeDefImportRow = genericTypeSpec.Form == 0x03
            ? checked((int)BinaryPrimitives.ReadUInt32LittleEndian(
                tables.TypeSpecs.GetRow(genericTypeSpec.Row)
                    .Slice(2, sizeof(uint)))) : 0;
        if (!TryResolveGenericNominalTypeIdentity(tables, typeDefRow,
                providerIdentity, definitions, typeDefImportRow,
                out var identity))
        {
            throw new InvalidDataException(
                "A generic nominal TypeSpec import has no selected provider definition.");
        }
        var nominalDefinition = definitions.FirstOrDefault(definition =>
            definition.Identity == identity);
        if (nominalDefinition is null
            || nominalDefinition.Formals.Length
                != tables.TypeSpecs.GetConstructedArgumentCount(typeSpec.Row))
        {
            throw new InvalidDataException(
                "A constructed TypeSpec does not match an imported generic nominal definition.");
        }

        var boundTypeArguments
            = new BoundGenericNominalTypeArgument?[
                nominalDefinition.Formals.Length];
        var constantSourceRows = new int[nominalDefinition.Formals.Length];
        for (var ordinal = 0; ordinal < nominalDefinition.Formals.Length; ordinal++)
        {
            var formal = nominalDefinition.Formals[ordinal];
            var argumentRef = tables.TypeSpecs.GetConstructedArgument(
                typeSpec.Row, ordinal);
            if (formal.Kind == BoundGenericArgumentKind.Type)
            {
                BoundGenericNominalTypeArgument argument;
                var argumentTypeSpec = tables.TypeSpecs.GetTypeSpecRef(
                    argumentRef, allowInitQualifier: true);
                if (methodContext is not null
                    && methodFormalRecords is not null
                    && methodRow > 0
                    && methodModuleRow > 0
                    && ((argumentTypeSpec.Form == 0x01
                            && TrRmConstantSourceValueValidator
                                .TryGetBoundScalarType(
                                    argumentTypeSpec.TypeCode, out _))
                        || (argumentTypeSpec.Form is
                            0x04 or 0x05 or 0x07 or 0x0E)
                        || (argumentTypeSpec.Form == 0x0C
                            && argumentTypeSpec.TypeCode == 0x8A)))
                {
                    var resolvedArgumentType =
                        ResolveGenericMethodTypeSpec(methodContext,
                            methodArtifactIndex, tables, methodModuleRow,
                            tables.TypeSpecs, tables.GenericParams,
                            methodFormalRecords, methodRow,
                            methodFormalTypeOwnerOrdinal, argumentRef,
                            methodFormalTypes, false, ordinal, out _);
                    argument = methodContext.ReadGenericNominalTypeArgument(
                        tables, methodArtifactIndex, methodModuleRow,
                        argumentRef, methodRow, methodFormalTypes,
                        methodFormalRecords, resolvedArgumentType);
                }
                else
                {
                    argument = ReadGenericNominalTypeArgument(argumentRef,
                        tables, definitions, providerIdentity, instances,
                        instanceByKey, nativePointerTypes,
                        nativePointerTypeIndexes, depth + 1, methodRow,
                        methodFormalTypes, methodFormalTypeOwnerOrdinal,
                        methodContext, methodArtifactIndex,
                        methodModuleRow, methodFormalRecords);
                }
                if (argument.TypeSpecForm is >= 0x06 and <= 0x0F
                    && argument.TypeArgumentTerm is not {
                        Kind: BoundGenericMethodTypeTermKind.CheckedReference
                            or BoundGenericMethodTypeTermKind.Span,
                    })
                {
                    throw Unsupported(
                        "A G1 constructed nominal TypeSpec argument uses an unsupported wrapper term.");
                }
                boundTypeArguments[ordinal] = argument;
            }
            else if (formal.Kind == BoundGenericArgumentKind.Constant)
            {
                constantSourceRows[ordinal] = checked((int)argumentRef);
            }
            else
            {
                throw new InvalidDataException(
                    "A constructed TypeSpec has an unsupported generic formal.");
            }
        }

        var requiresTypeSubstitution = false;
        foreach (var formal in nominalDefinition.Formals)
        {
            if (formal.Kind
                    != BoundGenericArgumentKind.Constant
                || formal.ConstantDomainTypeTerm is not
                    BoundGenericMethodTypeTerm domainTerm)
            {
                continue;
            }

            if (!BoundGenericMethodTypeTerms.TryGetFormalDependencies(
                domainTerm, out var dependencies)
                || !BoundGenericMethodTypeTerms
                    .TryGetConstantFormalDependencies(domainTerm,
                        out var constantDependencies)
                || !constantDependencies.IsEmpty)
            {
                throw new InvalidDataException(
                    "A generic nominal constant domain has invalid formal dependencies.");
            }
            requiresTypeSubstitution |= !dependencies.IsEmpty;
        }

        ImmutableArray<BoundGenericMethodTypeTerm> checkedTypeArgumentTerms = [];
        if (requiresTypeSubstitution)
        {
            var typeArgumentTerms = ImmutableArray.CreateBuilder<
                BoundGenericMethodTypeTerm>(
                    nominalDefinition.Formals.Count(static formal =>
                        formal.Kind == BoundGenericArgumentKind.Type));
            foreach (var typeArgument in boundTypeArguments)
            {
                if (typeArgument is not BoundGenericNominalTypeArgument argument
                    || argument.Kind != BoundGenericArgumentKind.Type)
                {
                    continue;
                }

                if (!TryGetGenericNominalTypeArgumentTerm(argument,
                    definitions, instances, out var typeArgumentTerm,
                    depth + 1, methodFormalTypes,
                    methodFormalTypeOwnerOrdinal))
                {
                    throw Unsupported(
                        "A generic nominal type argument cannot form a complete type term.");
                }
                typeArgumentTerms.Add(typeArgumentTerm);
            }
            checkedTypeArgumentTerms = typeArgumentTerms.MoveToImmutable();
        }

        var arguments = ImmutableArray.CreateBuilder<
            BoundGenericNominalTypeArgument>(
                nominalDefinition.Formals.Length);
        for (var ordinal = 0; ordinal < nominalDefinition.Formals.Length;
            ordinal++)
        {
            var formal = nominalDefinition.Formals[ordinal];
            if (formal.Kind == BoundGenericArgumentKind.Type)
            {
                arguments.Add(boundTypeArguments[ordinal]!);
                continue;
            }

            var sourceRow = constantSourceRows[ordinal];
            var sourceReference = tables.TypeSpecs.GetConstructedArgument(
                typeSpec.Row, ordinal);
            if (tables.ConstantSources.GetKind(sourceRow) == 4)
            {
                throw Unsupported(
                    "A generic nominal signature cannot consume a ConstantSource formal without its generic owner context.");
            }
            var constantArgument =
                BoundGenericNominalTypeArgument.ForConstantSource(
                    providerIdentity, sourceReference);
            if (formal.ConstantDomainType is PrimitiveType domainType
                && formal.ConstantCarrierType is PrimitiveType carrierType
                && tables.ConstantSources.GetKind(sourceRow) == 1)
            {
                var ownerModule = tables.TypeDefs.GetOwnerModule(typeDefRow);
                var payload =
                    TrRmConstantSourceValueValidator.GetFrozenScalarPayload(
                        sourceRow, formal.ConstantTypeSpecRef, ownerModule,
                        tables.ConstantSources, tables.FrozenValueNodes,
                        tables.Blobs, tables.TypeSpecs, tables.Features,
                        tables.Modules, tables.TypeDefs, tables.EnumDefs,
                        tables.Imports, tables.A3LayoutResourceLedger,
                        tables.Fields,
                        tables.TransparentConstantDomains);
                var bits = ScalarBits.FromPayload(carrierType, payload);
                if (!bits.IsCanonicalFor(carrierType))
                {
                    throw new InvalidDataException(
                        "A generic nominal constant does not match its decoded domain.");
                }

                constantArgument =
                    BoundGenericNominalTypeArgument.ForConstantSource(
                        providerIdentity, sourceReference, domainType,
                        carrierType, bits.Low, bits.High);
            }
            else if (formal.ConstantDomainTypeTerm is
                BoundGenericMethodTypeTerm domainTemplate)
            {
                if (tables.ConstantSources.GetKind(sourceRow) != 1
                    || !BoundGenericMethodTypeTerms.TrySubstitute(
                        domainTemplate, checkedTypeArgumentTerms, [],
                        out var expectedDomain)
                    || !BoundGenericMethodTypeTerms.TryGetFormalDependencies(
                        expectedDomain, out var domainTypeDependencies)
                    || !domainTypeDependencies.IsEmpty
                    || !BoundGenericMethodTypeTerms
                        .TryGetConstantFormalDependencies(expectedDomain,
                            out var domainConstantDependencies)
                    || !domainConstantDependencies.IsEmpty)
                {
                    throw new NotSupportedException(
                        "A generic nominal wrapper value requires a closed type domain and a frozen scalar source.");
                }

                var valueNode = checked((int)
                    tables.ConstantSources.GetPayloadReference(sourceRow));
                var valueTypeSpecRef
                    = tables.FrozenValueNodes.GetTypeSpecRef(valueNode);
                var actualDomainType = ReadGenericNominalTypeSpec(
                    valueTypeSpecRef, tables, definitions, providerIdentity,
                    instances, instanceByKey, nativePointerTypes,
                    nativePointerTypeIndexes, depth + 1);
                if (!TryGetGenericNominalTypeArgumentTerm(
                        BoundGenericNominalTypeArgument.ForType(
                            actualDomainType),
                        definitions, instances, out var actualDomain,
                        depth + 1)
                    || !SemanticVerifier.AreGenericMethodTypeTermsEqual(
                        expectedDomain, actualDomain, 0)
                    || !TryGetGenericNominalTransparentCarrier(
                        actualDomainType,
                        definitions, instances, out var genericCarrier))
                {
                    throw new InvalidDataException(
                        "A generic nominal constant does not match its substituted domain.");
                }

                var ownerModule
                    = tables.TypeDefs.GetOwnerModule(typeDefRow);
                var payload =
                    TrRmConstantSourceValueValidator.GetFrozenScalarPayload(
                        sourceRow, valueTypeSpecRef, ownerModule,
                        tables.ConstantSources, tables.FrozenValueNodes,
                        tables.Blobs, tables.TypeSpecs, tables.Features,
                        tables.Modules, tables.TypeDefs, tables.EnumDefs,
                        tables.Imports, tables.A3LayoutResourceLedger,
                        tables.Fields,
                        tables.TransparentConstantDomains);
                var bits = ScalarBits.FromPayload(genericCarrier, payload);
                if (!bits.IsCanonicalFor(genericCarrier))
                {
                    throw new InvalidDataException(
                        "A generic nominal wrapper value is not canonical in its carrier.");
                }

                constantArgument =
                    BoundGenericNominalTypeArgument.ForConstantSource(
                        providerIdentity, sourceReference, actualDomainType,
                        genericCarrier, bits.Low, bits.High);
            }

            arguments.Add(constantArgument);
        }

        return AddGenericNominalTypeInstance(identity, arguments.MoveToImmutable(),
            typeSpec.Qualifiers, typeSpecRef, providerIdentity, 0,
            instances, instanceByKey);
    }

    private static BoundGenericNominalTypeArgument ReadGenericNominalTypeArgument(
        uint typeSpecRef,
        TrRmStructuralMetadataValidator.ValidatedExecutionTables tables,
        ImmutableArray<BoundGenericNominalTypeDefinition> definitions,
        string providerIdentity,
        ImmutableArray<BoundGenericNominalTypeInstance>.Builder instances,
        Dictionary<string, PrimitiveType> instanceByKey,
        ImmutableArray<NativePointerTypeDefinition>.Builder nativePointerTypes,
        Dictionary<(PrimitiveType PointeeType, bool IsWritable), PrimitiveType>
            nativePointerTypeIndexes,
        int depth, int methodRow = 0,
        ImmutableArray<PrimitiveType> methodFormalTypes = default,
        int methodFormalTypeOwnerOrdinal = -1,
        GenericMethodReadContext? methodContext = null,
        int methodArtifactIndex = 0, int methodModuleRow = 0,
        TrRmGenericMethodFormalImport[]? methodFormalRecords = null)
    {
        var typeSpec = tables.TypeSpecs.GetTypeSpecRef(typeSpecRef);
        if (typeSpec.Form == 0x05)
        {
            var constructedType = ReadGenericNominalTypeSpec(typeSpecRef, tables,
                definitions, providerIdentity, instances, instanceByKey,
                nativePointerTypes, nativePointerTypeIndexes, depth + 1,
                methodRow, methodFormalTypes,
                methodFormalTypeOwnerOrdinal, methodContext,
                methodArtifactIndex, methodModuleRow, methodFormalRecords);
            return BoundGenericNominalTypeArgument.ForMetadataType(
                constructedType, typeSpec.Qualifiers, typeSpecRef, providerIdentity);
        }

        var bytes = tables.TypeSpecs.GetTypeSpecBytes(typeSpecRef);
        if (typeSpec.Form == 0x04 && typeSpec.IsRow)
        {
            if (methodRow <= 0 || methodFormalTypes.IsDefault)
            {
                throw Unsupported(
                    "A constructed TypeSpec method formal requires its exact GenericParam owner context.");
            }

            var genericParamRow = checked((int)BinaryPrimitives
                .ReadUInt32LittleEndian(bytes.Slice(2,
                    sizeof(uint))));
            var formalOrdinal = tables.GenericParams.GetOrdinal(
                genericParamRow);
            if (tables.GenericParams.GetOwnerDefId(genericParamRow)
                    != (0x1600_0000u | checked((uint)methodRow))
                || tables.GenericParams.GetFormalKind(genericParamRow) != 1
                || (uint)formalOrdinal >= (uint)methodFormalTypes.Length)
            {
                throw Invalid(
                    "A constructed TypeSpec references a foreign or non-type GenericParam.");
            }

            if (methodFormalTypeOwnerOrdinal < 0)
            {
                throw Unsupported(
                    "A constructed TypeSpec method formal has no retained generic owner identity.");
            }

            return BoundGenericNominalTypeArgument.ForMetadataType(
                methodFormalTypes[checked((int)formalOrdinal)],
                typeSpec.Qualifiers, typeSpecRef, providerIdentity,
                typeSpecForm: 0x04) with {
                TypeFormalDefinitionOrdinal =
                    methodFormalTypeOwnerOrdinal,
                TypeFormalOrdinal = checked((int)formalOrdinal),
                TypeFormalOwnerKind
                    = BoundGenericTypeFormalOwnerKind.GenericMethod,
            };
        }

        if (typeSpec.Form is (0x02 or 0x03) && typeSpec.IsRow)
        {
            var typeDefRow = tables.TypeSpecs.GetNominalTypeDefRow(
                typeSpecRef, tables.TypeDefs, tables.Imports);
            var importRow = typeSpec.Form == 0x03
                ? checked((int)BinaryPrimitives.ReadUInt32LittleEndian(
                    tables.TypeSpecs.GetRow(typeSpec.Row)
                        .Slice(2, sizeof(uint)))) : 0;
            if (!TryResolveGenericNominalTypeIdentity(tables, typeDefRow,
                    providerIdentity, definitions, importRow,
                    out var identity))
            {
                throw Unsupported(
                    "A generic nominal payload refers to a TypeDef outside the selected source reference.");
            }
            if (definitions.First(definition =>
                    definition.Identity == identity).Formals.Length != 0)
            {
                throw new InvalidDataException(
                    "A generic nominal payload cannot refer to an open generic TypeDef.");
            }

            return BoundGenericNominalTypeArgument.ForNominalType(identity,
                typeSpecForm: typeSpec.Form, qualifiers: typeSpec.Qualifiers,
                typeSpecReference: typeSpecRef,
                typeSpecProviderIdentity: providerIdentity);
        }

        if (typeSpec.Form == 0x01)
        {
            if (typeSpec.TypeCode == 0x8C)
            {
                if (!TryGetGenericNominalPointerPointee(
                    typeSpec.ArgumentCode, out var pointeeType))
                {
                    throw Unsupported(
                        "A generic nominal pointer argument has an unsupported pointee type.");
                }

                var pointerType = GetGenericNominalPointerType(pointeeType,
                    (typeSpec.Qualifiers & 0x01) != 0,
                    nativePointerTypes, nativePointerTypeIndexes);
                return BoundGenericNominalTypeArgument.ForMetadataType(
                    pointerType, 0, typeSpecRef, providerIdentity);
            }

            if (!TrRmConstantSourceValueValidator.TryGetBoundScalarType(
                typeSpec.TypeCode, out var scalarType))
            {
                return BoundGenericNominalTypeArgument.ForMetadataType(
                    PrimitiveType.Void, typeSpec.Qualifiers,
                    typeSpecRef, providerIdentity, 0x01,
                    typeSpec.TypeCode, typeSpec.ArgumentCode);
            }

            return BoundGenericNominalTypeArgument.ForMetadataType(scalarType,
                typeSpec.Qualifiers, typeSpecRef, providerIdentity);
        }

        if (typeSpec.Form is (>= 0x06 and <= 0x0B) or 0x0E)
        {
            var innerReference = BinaryPrimitives.ReadUInt32LittleEndian(bytes[2..]);
            var inner = ReadGenericNominalTypeArgument(innerReference, tables,
                definitions, providerIdentity, instances, instanceByKey,
                nativePointerTypes, nativePointerTypeIndexes, depth + 1,
                methodRow, methodFormalTypes,
                methodFormalTypeOwnerOrdinal, methodContext,
                methodArtifactIndex, methodModuleRow, methodFormalRecords);
            return inner with {
                Qualifiers = typeSpec.Qualifiers,
                TypeSpecForm = typeSpec.Form,
                TypeSpecReference = typeSpecRef,
                TypeSpecProviderIdentity = providerIdentity,
            };
        }

        if (typeSpec.Form == 0x0C)
        {
            var innerReference = BinaryPrimitives.ReadUInt32LittleEndian(bytes[3..]);
            var inner = ReadGenericNominalTypeArgument(innerReference, tables,
                definitions, providerIdentity, instances, instanceByKey,
                nativePointerTypes, nativePointerTypeIndexes, depth + 1,
                methodRow, methodFormalTypes,
                methodFormalTypeOwnerOrdinal, methodContext,
                methodArtifactIndex, methodModuleRow, methodFormalRecords);
            if (bytes[2] == 0x8C)
            {
                if (inner.NominalTypeIdentity is not null)
                {
                    throw Unsupported(
                        "A generic nominal pointer payload requires an admitted local pointee representation.");
                }
                var pointerType = GetGenericNominalPointerType(inner.Type,
                    (typeSpec.Qualifiers & 0x01) != 0,
                    nativePointerTypes, nativePointerTypeIndexes);
                return BoundGenericNominalTypeArgument.ForMetadataType(
                    pointerType, 0, typeSpecRef, providerIdentity);
            }
            return inner with {
                Qualifiers = typeSpec.Qualifiers,
                TypeSpecForm = typeSpec.Form,
                TypeCode = bytes[2],
                TypeSpecReference = typeSpecRef,
                TypeSpecProviderIdentity = providerIdentity,
            };
        }

        if (typeSpec.Form == 0x0F)
        {
            return BoundGenericNominalTypeArgument.ForMetadataType(
                PrimitiveType.Void, 0, typeSpecRef, providerIdentity,
                typeSpecForm: 0x0F);
        }

        throw Unsupported(
            "The generic nominal source-reference path encountered an unsupported TypeSpec form.");
    }

    private static bool TryGetGenericNominalPointerPointee(
        byte argumentCode, out PrimitiveType pointeeType)
    {
        if (argumentCode == 0)
        {
            pointeeType = PrimitiveType.Void;
            return true;
        }

        return TrRmConstantSourceValueValidator.TryGetBoundScalarType(
            argumentCode, out pointeeType);
    }

    private static PrimitiveType GetGenericNominalPointerType(
        PrimitiveType pointeeType, bool isWritable,
        ImmutableArray<NativePointerTypeDefinition>.Builder pointerTypes,
        Dictionary<(PrimitiveType PointeeType, bool IsWritable), PrimitiveType> indexes)
    {
        var key = (pointeeType, isWritable);
        if (indexes.TryGetValue(key, out var type))
        {
            return type;
        }

        if (pointerTypes.Count >= SemanticVerifier.MaximumAnalysisVisits)
        {
            throw Unsupported("The generic nominal pointer type limit was exceeded.");
        }

        type = NativePointerTypes.Type(pointerTypes.Count);
        pointerTypes.Add(new NativePointerTypeDefinition(pointeeType, isWritable));
        indexes.Add(key, type);
        return type;
    }

    private static PrimitiveType AddGenericNominalTypeInstance(
        BoundGenericNominalTypeIdentity identity,
        ImmutableArray<BoundGenericNominalTypeArgument> arguments,
        byte qualifiers, uint typeSpecReference, string providerIdentity,
        byte typeSpecForm,
        ImmutableArray<BoundGenericNominalTypeInstance>.Builder instances,
        Dictionary<string, PrimitiveType> instanceByKey)
    {
        var key = BoundGenericNominalTypeContract.CreateUseKey(
            identity, arguments, qualifiers, typeSpecForm);
        if (instanceByKey.TryGetValue(key, out var existing))
        {
            return existing;
        }
        if (instances.Count >= BoundGenericNominalTypeTypes.MaximumCount)
        {
            throw Unsupported("The generic nominal type instance limit was exceeded.");
        }

        var type = BoundGenericNominalTypeTypes.Type(instances.Count);
        instances.Add(new BoundGenericNominalTypeInstance(identity, arguments,
            qualifiers, typeSpecReference, providerIdentity, typeSpecForm));
        instanceByKey.Add(key, type);
        return type;
    }

    private static bool TryGetGenericNominalTypeArgumentTerm(
        BoundGenericNominalTypeArgument argument,
        ImmutableArray<BoundGenericNominalTypeDefinition> definitions,
        ImmutableArray<BoundGenericNominalTypeInstance>.Builder instances,
        out BoundGenericMethodTypeTerm term, int depth = 0,
        ImmutableArray<PrimitiveType> formalTypes = default,
        int formalTypeOwnerOrdinal = -1)
    {
        term = null!;
        if (depth > SemanticVerifier.MaximumDepth
            || argument.Kind != BoundGenericArgumentKind.Type)
        {
            return false;
        }

        if (argument.TypeArgumentTerm is
            BoundGenericMethodTypeTerm retainedTypeArgumentTerm)
        {
            if ((argument.HasTypeFormalOwnerIdentity
                    && argument.TypeFormalDefinitionOrdinal
                        != formalTypeOwnerOrdinal)
                || !BoundGenericMethodTypeTerms.TryGetFormalDependencies(
                    retainedTypeArgumentTerm,
                    out var retainedFormalDependencies)
                || !BoundGenericMethodTypeTerms
                    .TryGetConstantFormalDependencies(
                        retainedTypeArgumentTerm, out _)
                || (!retainedFormalDependencies.IsEmpty
                    && (formalTypes.IsDefault
                        || formalTypeOwnerOrdinal < 0))
                || (!formalTypes.IsDefault
                    && retainedFormalDependencies.Any(dependency =>
                        (uint)dependency >= (uint)formalTypes.Length))
                || (argument.HasTypeFormalIdentity
                    && (retainedTypeArgumentTerm.Kind
                            != BoundGenericMethodTypeTermKind.Formal
                        || retainedFormalDependencies.Length != 1
                        || retainedFormalDependencies[0]
                            != argument.TypeFormalOrdinal
                        || formalTypes.IsDefault
                        || (uint)argument.TypeFormalOrdinal
                            >= (uint)formalTypes.Length
                        || formalTypes[argument.TypeFormalOrdinal]
                            != argument.Type))
                || (!argument.HasTypeFormalOwnerIdentity
                    && !retainedFormalDependencies.IsEmpty))
            {
                return false;
            }

            term = retainedTypeArgumentTerm;
            return ApplyQualifiers(argument.Qualifiers, ref term);
        }

        if (argument.NominalTypeIdentity is
            BoundGenericNominalTypeIdentity nominalIdentity)
        {
            if (argument.TypeSpecForm == 0x06
                && definitions.FirstOrDefault(definition =>
                    definition.Identity == nominalIdentity)?.NominalKind
                    != 2)
            {
                return false;
            }

            term = new BoundGenericMethodTypeTerm(
                BoundGenericMethodTypeTermKind.ImportedGeneric,
                PrimitiveType.Void, -1, -1, [],
                false, false, false, false) {
                NominalTypeIdentity = nominalIdentity,
                IsOptional = argument.TypeSpecForm == 0x06,
            };
            return ApplyQualifiers(argument.Qualifiers, ref term);
        }

        if (argument.HasTypeFormalIdentity)
        {
            if (argument.TypeSpecForm == 0x06
                || formalTypeOwnerOrdinal
                    != argument.TypeFormalDefinitionOrdinal
                || formalTypes.IsDefault
                || (uint)argument.TypeFormalOrdinal
                    >= (uint)formalTypes.Length
                || formalTypes[argument.TypeFormalOrdinal] != argument.Type)
            {
                return false;
            }

            term = new BoundGenericMethodTypeTerm(
                BoundGenericMethodTypeTermKind.Formal,
                PrimitiveType.Void, argument.TypeFormalOrdinal, -1, [],
                false, false, false, false);
            return ApplyQualifiers(argument.Qualifiers, ref term);
        }

        if (!formalTypes.IsDefault)
        {
            if (argument.TypeSpecForm == 0x06)
            {
                return false;
            }
            var formalOrdinal = formalTypes.IndexOf(argument.Type);
            if (formalOrdinal >= 0)
            {
                term = new BoundGenericMethodTypeTerm(
                    BoundGenericMethodTypeTermKind.Formal,
                    PrimitiveType.Void, formalOrdinal, -1, [],
                    false, false, false, false);
                return ApplyQualifiers(argument.Qualifiers, ref term);
            }
        }

        if (!BoundGenericNominalTypeTypes.IsType(argument.Type))
        {
            if (argument.Type == PrimitiveType.Void
                || argument.TypeSpecForm == 0x06)
            {
                return false;
            }

            term = CreateConcreteGenericNominalTypeTerm(argument.Type);
            return ApplyQualifiers(argument.Qualifiers, ref term);
        }

        var instanceIndex = BoundGenericNominalTypeTypes.Index(argument.Type);
        if ((uint)instanceIndex >= (uint)instances.Count)
        {
            return false;
        }

        var instance = instances[instanceIndex];
        var definition = definitions.FirstOrDefault(candidate =>
            candidate.Identity == instance.Definition);
        if (definition is null || instance.Arguments.IsDefault
            || definition.Formals.Length != instance.Arguments.Length
            || (instance.TypeSpecForm == 0x06
                && definition.NominalKind != 2))
        {
            return false;
        }

        var typeArguments = ImmutableArray.CreateBuilder<
            BoundGenericMethodTypeTerm>(
                definition.Formals.Count(static formal =>
                    formal.Kind == BoundGenericArgumentKind.Type));
        var constantArguments = ImmutableArray.CreateBuilder<
            BoundGenericMethodTypeConstantArgument>(
                definition.Formals.Count(static formal =>
                    formal.Kind == BoundGenericArgumentKind.Constant));
        for (var ordinal = 0; ordinal < definition.Formals.Length; ordinal++)
        {
            var formal = definition.Formals[ordinal];
            var actual = instance.Arguments[ordinal];
            if (formal.Kind == BoundGenericArgumentKind.Type)
            {
                if (!TryGetGenericNominalTypeArgumentTerm(actual,
                    definitions, instances, out var typeArgument,
                    depth + 1, formalTypes, formalTypeOwnerOrdinal))
                {
                    return false;
                }
                typeArguments.Add(typeArgument);
                continue;
            }

            if (formal.Kind != BoundGenericArgumentKind.Constant)
            {
                return false;
            }
            if (actual.HasDecodedConstant
                && actual.ConstantDomainType is PrimitiveType domainType
                && actual.ConstantCarrierType is PrimitiveType carrierType)
            {
                constantArguments.Add(
                    new BoundGenericMethodTypeConstantArgument(
                        BoundGenericMethodTypeConstantArgumentKind.Value, -1,
                        domainType, carrierType,
                        new ScalarBits(actual.ConstantLow,
                            actual.ConstantHigh)));
                continue;
            }
            if (actual.HasConstantFormalIdentity)
            {
                constantArguments.Add(
                    new BoundGenericMethodTypeConstantArgument(
                        BoundGenericMethodTypeConstantArgumentKind.Formal,
                        actual.ConstantFormalOrdinal,
                        PrimitiveType.Void, PrimitiveType.Void, default) {
                        FormalDefinitionOrdinal =
                            actual.ConstantFormalDefinitionOrdinal,
                        FormalOwnerKind =
                            actual.ConstantFormalOwnerKind,
                    });
                continue;
            }

            return false;
        }

        term = new BoundGenericMethodTypeTerm(
            BoundGenericMethodTypeTermKind.ImportedGeneric,
            PrimitiveType.Void, -1, -1,
            typeArguments.MoveToImmutable(),
            false, false, false, false) {
            NominalTypeIdentity = instance.Definition,
            ConstantArguments = constantArguments.MoveToImmutable(),
        };
        term = term with {
            IsWritable = (instance.Qualifiers & 0x01) != 0,
            IsGarbageCollected = (instance.Qualifiers & 0x02) != 0,
            IsInitialization = (instance.Qualifiers & 0x04) != 0,
            IsOptional = instance.TypeSpecForm == 0x06,
        };
        return ApplyQualifiers(argument.Qualifiers, ref term);

        static bool ApplyQualifiers(byte qualifiers,
            ref BoundGenericMethodTypeTerm typeTerm)
        {
            if ((qualifiers & ~0x07) != 0)
            {
                return false;
            }

            typeTerm = typeTerm with {
                IsWritable = typeTerm.IsWritable || (qualifiers & 0x01) != 0,
                IsGarbageCollected = typeTerm.IsGarbageCollected
                    || (qualifiers & 0x02) != 0,
                IsInitialization = typeTerm.IsInitialization
                    || (qualifiers & 0x04) != 0,
            };
            return true;
        }
    }

    private static bool TryGetGenericNominalTransparentCarrier(
        PrimitiveType type,
        ImmutableArray<BoundGenericNominalTypeDefinition> definitions,
        ImmutableArray<BoundGenericNominalTypeInstance>.Builder instances,
        out PrimitiveType carrierType)
    {
        carrierType = PrimitiveType.Void;
        if (!BoundGenericNominalTypeTypes.IsType(type))
        {
            return false;
        }

        var index = BoundGenericNominalTypeTypes.Index(type);
        if ((uint)index >= (uint)instances.Count)
        {
            return false;
        }

        var instance = instances[index];
        var definition = definitions.FirstOrDefault(candidate =>
            candidate.Identity == instance.Definition);
        if (definition?.TransparentConstantCarrierType
            is not PrimitiveType carrier)
        {
            return false;
        }

        carrierType = carrier;
        return true;
    }

    private static ImmutableArray<BoundGenericNominalTypeDefinition>
        CreateGenericNominalTypeDefinitions(
            TrRmStructuralMetadataValidator.ValidatedExecutionTables tables,
            string providerIdentity,
            ImmutableArray<BoundEnumDefinition> enumDefinitions,
            Dictionary<int, GenericNominalWrapperDomain>? wrapperDomainsByTypeDef = null,
            int moduleRow = 1, int? onlyTypeDefRow = null)
    {
        var typeDefs = tables.TypeDefs;
        var genericParams = tables.GenericParams;
        var constraints = tables.Constraints;
        var definitions = ImmutableArray.CreateBuilder<BoundGenericNominalTypeDefinition>();
        long metadataRows = 0;
        for (var typeDefRow = 1; typeDefRow <= typeDefs.Count; typeDefRow++)
        {
            if ((onlyTypeDefRow is int selectedTypeDefRow
                    && typeDefRow != selectedTypeDefRow)
                || typeDefs.GetOwnerModule(typeDefRow) != moduleRow
                || (typeDefs.GetFlags(typeDefRow) & 3) != 2
                || typeDefs.GetKind(typeDefRow) == 4
                || !tables.Exports.TryGetExportKeyForDefinition(
                    moduleRow, 0x1300_0000u | (uint)typeDefRow, out _))
            {
                continue;
            }

            var (firstFormal, formalCount) = typeDefs.GetGenericRange(typeDefRow);
            if (formalCount > SemanticVerifier.MaximumAnalysisVisits
                || definitions.Count >= BoundGenericNominalTypeTypes.MaximumCount)
            {
                throw Unsupported("The generic nominal definition or formal limit was exceeded.");
            }
            metadataRows += formalCount;
            if (metadataRows > SemanticVerifier.MaximumAnalysisVisits)
            {
                throw Unsupported("The generic nominal metadata traversal limit was exceeded.");
            }
            var moduleIdentity = tables.Modules.GetIdentity(moduleRow);
            var identity = new BoundGenericNominalTypeIdentity(
                providerIdentity, moduleIdentity, 0x1300_0000u | (uint)typeDefRow);
            var formals = ImmutableArray.CreateBuilder<BoundGenericNominalTypeFormal>(formalCount);
            for (var ordinal = 0; ordinal < formalCount; ordinal++)
            {
                var row = firstFormal + ordinal;
                var formalKind = genericParams.GetFormalKind(row) switch {
                    1 => BoundGenericArgumentKind.Type,
                    2 => BoundGenericArgumentKind.Constant,
                    _ => throw new InvalidDataException("Invalid TRRM generic nominal formal."),
                };
                var constantTypeSpecRef =
                    genericParams.GetConstantTypeSpecRef(row);
                var (constantDomainType, constantCarrierType,
                    constantDomainTypeTerm) =
                    formalKind == BoundGenericArgumentKind.Constant
                        ? GetGenericNominalConstantDomainTypes(tables,
                            constantTypeSpecRef, typeDefRow,
                            providerIdentity, enumDefinitions,
                            wrapperDomainsByTypeDef)
                        : (null, null, null);
                var defaultReference = genericParams.GetDefaultArgumentRef(row);
                var isOpenFormalForwardingDefault =
                    formalKind == BoundGenericArgumentKind.Constant
                    && tables.Schema.UsesGenericConstantMethods()
                    && defaultReference != 0
                    && tables.ConstantSources.GetKind(
                        checked((int)defaultReference)) == 4;
                if (defaultReference != 0
                    && constantDomainTypeTerm is not null
                    && constantDomainType is null
                    && !isOpenFormalForwardingDefault)
                {
                    throw Unsupported(
                        "A generic nominal constant default requires a closed domain.");
                }
                var defaultValue = defaultReference == 0 ? null
                    : CreateGenericNominalTypeDefault(formalKind, defaultReference,
                        providerIdentity, tables, constantTypeSpecRef,
                        constantDomainType, constantCarrierType,
                        constantDomainTypeTerm,
                        typeDefs.GetOwnerModule(typeDefRow), typeDefRow,
                        identity.DefinitionId,
                        genericParams.GetOrdinal(row), enumDefinitions,
                        wrapperDomainsByTypeDef);
                var (firstConstraint, constraintCount) =
                    genericParams.GetConstraintRange(row);
                metadataRows += constraintCount;
                if (metadataRows > SemanticVerifier.MaximumAnalysisVisits)
                {
                    throw Unsupported("The generic nominal constraint limit was exceeded.");
                }
                var formalConstraints =
                    ImmutableArray.CreateBuilder<BoundGenericNominalTypeConstraint>(
                        constraintCount);
                for (var constraintRow = firstConstraint;
                    constraintRow < firstConstraint + constraintCount;
                    constraintRow++)
                {
                    formalConstraints.Add(new BoundGenericNominalTypeConstraint(
                        constraints.GetKind(constraintRow),
                        constraints.GetPayload(constraintRow)));
                }

                formals.Add(new BoundGenericNominalTypeFormal(
                    genericParams.GetOwnerDefId(row),
                    genericParams.GetOrdinal(row),
                    tables.Strings.DecodeName(genericParams.GetNameStringRow(row)),
                    formalKind,
                    genericParams.GetOwnershipDomain(row),
                    genericParams.GetNullabilityDomain(row),
                    constantTypeSpecRef,
                    defaultValue,
                    formalConstraints.MoveToImmutable()) {
                    ConstantDomainType = constantDomainType,
                    ConstantCarrierType = constantCarrierType,
                    ConstantDomainTypeTerm = constantDomainTypeTerm,
                });
            }

            definitions.Add(new BoundGenericNominalTypeDefinition(identity,
                typeDefs.GetNamespace(typeDefRow), typeDefs.GetName(typeDefRow),
                typeDefs.GetKind(typeDefRow), formals.MoveToImmutable(),
                tables.TypeSpecs.Count, tables.ConstantSources.Count,
                tables.Constraints.Count, tables.AssociatedRelations.Count) {
                TransparentConstantCarrierType =
                    wrapperDomainsByTypeDef?.TryGetValue(typeDefRow,
                        out var wrapperDomain) == true
                        && wrapperDomain.Type is null
                        ? wrapperDomain.CarrierType : null,
            });
        }

        return definitions.ToImmutable();
    }

    private static bool TryResolveGenericNominalTypeIdentity(
        TrRmStructuralMetadataValidator.ValidatedExecutionTables tables,
        int typeDefRow, string providerIdentity,
        ImmutableArray<BoundGenericNominalTypeDefinition> definitions,
        int importRow,
        out BoundGenericNominalTypeIdentity identity)
    {
        identity = default;
        if (typeDefRow <= 0 || typeDefRow > tables.TypeDefs.Count
            || definitions.IsDefault)
        {
            return false;
        }

        var expectedDefinitionId = 0x1300_0000u | (uint)typeDefRow;
        if (importRow != 0)
        {
            if (tables.Imports.GetExpectedDefinitionForImportRow(importRow)
                != expectedDefinitionId)
            {
                return false;
            }
            var binding = tables.Imports.GetBindingForImportRow(importRow);
            return TryResolveBoundIdentity(binding, expectedDefinitionId,
                definitions, out identity);
        }
        if (tables.Imports.TryGetExpectedBinding(expectedDefinitionId,
                out var binding))
        {
            return TryResolveBoundIdentity(binding, expectedDefinitionId,
                definitions, out identity);
        }

        identity = new BoundGenericNominalTypeIdentity(providerIdentity,
            tables.Modules.GetIdentity(
                tables.TypeDefs.GetOwnerModule(typeDefRow)),
            expectedDefinitionId);
        return true;

        static bool TryResolveBoundIdentity(TrRmImportBinding binding,
            uint expectedDefinitionId,
            ImmutableArray<BoundGenericNominalTypeDefinition> definitions,
            out BoundGenericNominalTypeIdentity resolvedIdentity)
        {
            var matches = definitions.Where(definition =>
                definition.Identity.ProviderIdentity
                    == binding.ProducerIdentity
                && !definition.ProviderExpectations.IsDefaultOrEmpty
                && definition.ProviderExpectations.Any(expectation =>
                    expectation.ExpectedDefinitionId
                        == expectedDefinitionId
                    && expectation.ProducerIdentity
                        == binding.ProducerIdentity
                    && expectation.ExportKey == binding.ExportKey
                    && expectation.TargetRequirement
                        == binding.TargetRequirement
                    && expectation.RuntimePolicy == binding.RuntimePolicy))
                .ToArray();
            if (matches.Length == 1)
            {
                resolvedIdentity = matches[0].Identity;
                return true;
            }

            resolvedIdentity = default;
            return false;
        }
    }

    private static (PrimitiveType? DomainType, PrimitiveType? CarrierType,
        BoundGenericMethodTypeTerm? DomainTypeTerm)
        GetGenericNominalConstantDomainTypes(
            TrRmStructuralMetadataValidator.ValidatedExecutionTables tables,
            uint typeSpecRef,
            int ownerTypeDefRow,
            string providerIdentity,
            ImmutableArray<BoundEnumDefinition> enumDefinitions,
            Dictionary<int, GenericNominalWrapperDomain>? wrapperDomainsByTypeDef)
    {
        var type = tables.TypeSpecs.GetTypeSpecRef(typeSpecRef);
        if (type.Qualifiers == 0
            && type.Form == 0x01
            && TrRmConstantSourceValueValidator.TryGetBoundScalarType(
                type.TypeCode, out var primitiveType)
            && primitiveType != PrimitiveType.Void)
        {
            return (primitiveType, primitiveType, null);
        }

        if (type.IsRow && type.Form is 0x02 or 0x03
            && type.Qualifiers == 0)
        {
            var typeDefRow = tables.TypeSpecs.GetNominalTypeDefRow(
                typeSpecRef, tables.TypeDefs, tables.Imports);
            if (tables.TypeDefs.GetKind(typeDefRow) == 4)
            {
                var enumOrdinal = 0;
                for (var row = 1; row < typeDefRow; row++)
                {
                    if (tables.TypeDefs.GetKind(row) == 4)
                    {
                        enumOrdinal++;
                    }
                }
                if ((uint)enumOrdinal < (uint)enumDefinitions.Length)
                {
                    var enumType = BoundEnumTypes.Type(enumOrdinal);
                    return (enumType,
                        enumDefinitions[enumOrdinal].UnderlyingType,
                        null);
                }
            }
            else if (tables.TypeDefs.GetKind(typeDefRow) == 1
                && tables.TransparentConstantDomains.TryGetForOwner(
                    typeDefRow, out _, out _))
            {
                if (wrapperDomainsByTypeDef is not null
                    && wrapperDomainsByTypeDef.TryGetValue(typeDefRow,
                        out var wrapperDomain))
                {
                    return (wrapperDomain.Type,
                        wrapperDomain.CarrierType,
                        null);
                }

                throw Unsupported(
                    "The selected transparent wrapper has no checked source-reference structure.");
            }
        }

        if (type.Qualifiers == 0 && type.Form == 0x05 && type.IsRow
            && TryCreateGenericNominalTransparentWrapperTypeTerm(
                typeSpecRef, ownerTypeDefRow, providerIdentity, tables,
                wrapperDomainsByTypeDef, out var wrapperTerm,
                out var wrapperCarrier))
        {
            return (null, wrapperCarrier, wrapperTerm);
        }

        return (null, null, null);
    }

    private static BoundGenericMethodTypeTerm
        CreateConcreteGenericNominalTypeTerm(PrimitiveType type) =>
        new(BoundGenericMethodTypeTermKind.Concrete, type, -1, -1, [],
            false, false, false, false);

    private static bool TryCreateGenericNominalTransparentWrapperTypeTerm(
        uint typeSpecRef, int ownerTypeDefRow, string providerIdentity,
        TrRmStructuralMetadataValidator.ValidatedExecutionTables tables,
        Dictionary<int, GenericNominalWrapperDomain>? wrapperDomainsByTypeDef,
        out BoundGenericMethodTypeTerm term,
        out PrimitiveType carrierType)
    {
        term = null!;
        carrierType = PrimitiveType.Void;
        var typeSpec = tables.TypeSpecs.GetTypeSpecRef(typeSpecRef);
        if (typeSpec.Form != 0x05 || !typeSpec.IsRow
            || wrapperDomainsByTypeDef is null)
        {
            return false;
        }

        var constructed = tables.TypeSpecs.GetRow(typeSpec.Row);
        var genericTypeSpecRef = BinaryPrimitives.ReadUInt32LittleEndian(
            constructed[2..]);
        var genericTypeSpec = tables.TypeSpecs.GetTypeSpecRef(
            genericTypeSpecRef);
        if (!genericTypeSpec.IsRow
            || genericTypeSpec.Form is not (0x02 or 0x03))
        {
            return false;
        }

        var wrapperTypeDefRow = tables.TypeSpecs.GetNominalTypeDefRow(
            genericTypeSpecRef, tables.TypeDefs, tables.Imports);
        if (!wrapperDomainsByTypeDef.TryGetValue(wrapperTypeDefRow,
                out var wrapperDomain)
            || wrapperDomain.Type is not null
            || !TryReadGenericNominalTypeTerm(typeSpecRef,
                ownerTypeDefRow, providerIdentity, tables, 0,
                out term, wrapperDomainsByTypeDef))
        {
            return false;
        }

        carrierType = wrapperDomain.CarrierType;
        return true;
    }

    private static bool TryReadGenericNominalTypeTerm(
        uint typeSpecRef, int ownerTypeDefRow, string providerIdentity,
        TrRmStructuralMetadataValidator.ValidatedExecutionTables tables,
        int depth, out BoundGenericMethodTypeTerm term,
        Dictionary<int, GenericNominalWrapperDomain>?
            wrapperDomainsByTypeDef = null)
    {
        term = null!;
        if (depth > SemanticVerifier.MaximumDepth)
        {
            return false;
        }

        var typeSpec = tables.TypeSpecs.GetTypeSpecRef(typeSpecRef);
        if (typeSpec.Qualifiers != 0)
        {
            return false;
        }

        if (typeSpec.Form == 0x01
            && TrRmConstantSourceValueValidator.TryGetBoundScalarType(
                typeSpec.TypeCode, out var primitiveType)
            && primitiveType != PrimitiveType.Void)
        {
            term = CreateConcreteGenericNominalTypeTerm(primitiveType);
            return true;
        }

        if (typeSpec.Form == 0x04 && typeSpec.IsRow)
        {
            var genericParamRow = checked((int)
                BinaryPrimitives.ReadUInt32LittleEndian(
                    tables.TypeSpecs.GetRow(typeSpec.Row)[2..]));
            var (firstFormal, formalCount)
                = tables.TypeDefs.GetGenericRange(ownerTypeDefRow);
            var typeFormalOrdinal = 0;
            for (var ordinal = 0; ordinal < formalCount; ordinal++)
            {
                var row = firstFormal + ordinal;
                if (row == genericParamRow)
                {
                    if (tables.GenericParams.GetFormalKind(row) != 1
                        || tables.GenericParams.GetOwnerDefId(row)
                            != (0x1300_0000u | (uint)ownerTypeDefRow))
                    {
                        return false;
                    }

                    term = new BoundGenericMethodTypeTerm(
                        BoundGenericMethodTypeTermKind.Formal,
                        PrimitiveType.Void, typeFormalOrdinal, -1, [],
                        false, false, false, false);
                    return true;
                }

                if (tables.GenericParams.GetFormalKind(row) == 1)
                {
                    typeFormalOrdinal++;
                }
            }

            return false;
        }

        int genericTypeDefRow;
        var typeArguments = ImmutableArray<BoundGenericMethodTypeTerm>.Empty;
        if (typeSpec.Form is 0x02 or 0x03 && typeSpec.IsRow)
        {
            genericTypeDefRow = tables.TypeSpecs.GetNominalTypeDefRow(
                typeSpecRef, tables.TypeDefs, tables.Imports);
            if (tables.TypeDefs.GetGenericRange(genericTypeDefRow).Count != 0)
            {
                return false;
            }
        }
        else if (typeSpec.Form == 0x05 && typeSpec.IsRow)
        {
            var constructed = tables.TypeSpecs.GetRow(typeSpec.Row);
            var genericTypeSpecRef
                = BinaryPrimitives.ReadUInt32LittleEndian(
                    constructed[2..]);
            var genericTypeSpec
                = tables.TypeSpecs.GetTypeSpecRef(genericTypeSpecRef);
            if (!genericTypeSpec.IsRow
                || genericTypeSpec.Form is not (0x02 or 0x03))
            {
                return false;
            }

            genericTypeDefRow = tables.TypeSpecs.GetNominalTypeDefRow(
                genericTypeSpecRef, tables.TypeDefs, tables.Imports);
            var (firstFormal, formalCount)
                = tables.TypeDefs.GetGenericRange(genericTypeDefRow);
            if (formalCount
                != tables.TypeSpecs.GetConstructedArgumentCount(typeSpec.Row))
            {
                return false;
            }

            var arguments = ImmutableArray.CreateBuilder<
                BoundGenericMethodTypeTerm>(formalCount);
            for (var ordinal = 0; ordinal < formalCount; ordinal++)
            {
                var formalRow = firstFormal + ordinal;
                if (tables.GenericParams.GetFormalKind(formalRow) != 1
                    || tables.GenericParams.GetConstraintRange(formalRow)
                        .Count != 0
                    || !TryReadGenericNominalTypeTerm(
                        tables.TypeSpecs.GetConstructedArgument(
                            typeSpec.Row, ordinal),
                        ownerTypeDefRow, providerIdentity, tables,
                        depth + 1, out var argument,
                        wrapperDomainsByTypeDef))
                {
                    return false;
                }
                arguments.Add(argument);
            }

            typeArguments = arguments.MoveToImmutable();
        }
        else
        {
            return false;
        }

        BoundGenericNominalTypeIdentity identity;
        if (wrapperDomainsByTypeDef?.TryGetValue(genericTypeDefRow,
                out var wrapperDomain) == true
            && wrapperDomain.Identity is BoundGenericNominalTypeIdentity
                providerTypeIdentity)
        {
            identity = providerTypeIdentity;
        }
        else
        {
            if (tables.Imports.TryGetExpectedBinding(
                    0x1300_0000u | (uint)genericTypeDefRow, out _))
            {
                return false;
            }

            identity = new BoundGenericNominalTypeIdentity(
                providerIdentity,
                tables.Modules.GetIdentity(
                    tables.TypeDefs.GetOwnerModule(genericTypeDefRow)),
                0x1300_0000u | (uint)genericTypeDefRow);
        }
        term = new BoundGenericMethodTypeTerm(
            BoundGenericMethodTypeTermKind.ImportedGeneric,
            PrimitiveType.Void, -1, -1, typeArguments,
            false, false, false, false) {
            NominalTypeIdentity = identity,
        };
        return true;
    }

    private static BoundGenericNominalTypeDefault CreateGenericNominalTypeDefault(
        BoundGenericArgumentKind kind, uint reference, string providerIdentity,
        TrRmStructuralMetadataValidator.ValidatedExecutionTables tables,
        uint constantTypeSpecRef, PrimitiveType? constantDomainType,
        PrimitiveType? constantCarrierType,
        BoundGenericMethodTypeTerm? constantDomainTypeTerm, int ownerModule,
        int ownerTypeDefRow, uint ownerDefinitionId, uint formalOrdinal,
        ImmutableArray<BoundEnumDefinition> enumDefinitions,
        Dictionary<int, GenericNominalWrapperDomain>? wrapperDomainsByTypeDef)
    {
        var typeSpecs = tables.TypeSpecs;
        PrimitiveType? decodedType = null;
        byte qualifiers = 0;
        BoundGenericNominalTypeArgument? decodedConstantArgument = null;
        int? earlierFormalOrdinal = null;
        if (kind == BoundGenericArgumentKind.Type)
        {
            var typeSpec = typeSpecs.GetTypeSpecRef(reference);
            qualifiers = typeSpec.Qualifiers;
            if (typeSpec.Form == 0x01 && typeSpec.Qualifiers == 0
                && TrRmConstantSourceValueValidator.TryGetBoundScalarType(
                    typeSpec.TypeCode, out var primitiveType))
            {
                decodedType = primitiveType;
            }
        }
        else if (kind == BoundGenericArgumentKind.Constant)
        {
            var sourceRow = checked((int)reference);
            var sourceKind = tables.ConstantSources.GetKind(sourceRow);
            if (sourceKind == 1
                && constantDomainType is PrimitiveType domainType
                && constantCarrierType is PrimitiveType carrierType)
            {
                var payload =
                    TrRmConstantSourceValueValidator.GetFrozenScalarPayload(
                        sourceRow, constantTypeSpecRef, ownerModule,
                        tables.ConstantSources, tables.FrozenValueNodes,
                        tables.Blobs, tables.TypeSpecs, tables.Features,
                        tables.Modules, tables.TypeDefs, tables.EnumDefs,
                        tables.Imports, tables.A3LayoutResourceLedger,
                        tables.Fields,
                        tables.TransparentConstantDomains);
                var bits = ScalarBits.FromPayload(carrierType, payload);
                if (!bits.IsCanonicalFor(carrierType))
                {
                    throw new InvalidDataException(
                        "A generic nominal constant default is not canonical in its domain.");
                }

                decodedConstantArgument =
                    BoundGenericNominalTypeArgument.ForConstantSource(
                        providerIdentity, reference, domainType, carrierType,
                        bits.Low, bits.High);
            }
            else if (sourceKind == 4
                && tables.Schema.UsesGenericConstantMethods())
            {
                var sourceFormalRow = checked((int)
                    tables.ConstantSources.GetPayloadReference(sourceRow));
                if (tables.GenericParams.GetOwnerDefId(sourceFormalRow)
                        != ownerDefinitionId
                    || tables.GenericParams.GetFormalKind(sourceFormalRow) != 2
                    || tables.GenericParams.GetOrdinal(sourceFormalRow)
                        >= formalOrdinal)
                {
                    throw new InvalidDataException(
                        "A generic nominal constant default must reference an earlier constant formal of the same TypeDef.");
                }

                var (sourceDomainType, sourceCarrierType,
                    sourceDomainTypeTerm) =
                    GetGenericNominalConstantDomainTypes(tables,
                        tables.GenericParams.GetConstantTypeSpecRef(
                            sourceFormalRow), ownerTypeDefRow,
                        providerIdentity, enumDefinitions,
                        wrapperDomainsByTypeDef);
                var sameDomain =
                    (constantDomainType is not null
                        || constantDomainTypeTerm is not null)
                    && (sourceDomainType is not null
                        || sourceDomainTypeTerm is not null)
                    && sourceDomainType == constantDomainType
                    && sourceCarrierType == constantCarrierType
                    && (sourceDomainTypeTerm is null
                        ? constantDomainTypeTerm is null
                        : constantDomainTypeTerm is not null
                            && SemanticVerifier.AreGenericMethodTypeTermsEqual(
                                sourceDomainTypeTerm,
                                constantDomainTypeTerm, 0));
                if (!sameDomain)
                {
                    throw new InvalidDataException(
                        "A generic nominal constant default must preserve the earlier formal's exact domain.");
                }

                earlierFormalOrdinal = checked((int)
                    tables.GenericParams.GetOrdinal(sourceFormalRow));
            }
        }

        return new BoundGenericNominalTypeDefault(
            kind, reference, providerIdentity, decodedType, qualifiers) {
            DecodedConstantArgument = decodedConstantArgument,
            EarlierFormalOrdinal = earlierFormalOrdinal,
        };
    }

    private static BoundFunction RemapNativeImportStructureTypes(BoundFunction function,
        int[] structureMap)
    {
        var variables = function.Variables.Select(variable => variable with {
            Type = RemapStructureType(variable.Type, structureMap),
        }).ToImmutableArray();
        var remappedImport = function.DirectNativeImport;
        if (remappedImport is DirectNativeImport import && !import.Parameters.IsDefaultOrEmpty)
        {
            remappedImport = import with {
                Parameters = [.. import.Parameters.Select(parameter => parameter with
                {
                    PointeeType = RemapStructureType(parameter.PointeeType, structureMap),
                })],
            };
        }

        var remappedVTableMethod = function.NativeVTableMethod;
        if (remappedVTableMethod is NativeVTableMethodDefinition method
            && (!method.Parameters.IsDefaultOrEmpty || !method.Outputs.IsDefaultOrEmpty))
        {
            remappedVTableMethod = method with {
                Parameters = [.. method.Parameters.Select(parameter => parameter with
                {
                    PointeeType = RemapStructureType(parameter.PointeeType, structureMap),
                })],
                Outputs = [.. method.Outputs.Select(output => output with
                {
                    PointeeType = RemapStructureType(output.PointeeType, structureMap),
                })],
            };
        }

        return function with {
            ReturnType = RemapStructureType(function.ReturnType, structureMap),
            Variables = variables,
            DirectNativeImport = remappedImport,
            NativeVTableMethod = remappedVTableMethod,
        };
    }

    private static PrimitiveType RemapStructureType(PrimitiveType type, int[] structureMap)
    {
        if ((int)type is < 100 or >= ManagedClassTypes.First)
        {
            return type;
        }

        var sourceIndex = (int)type - 100;
        if (sourceIndex >= structureMap.Length || structureMap[sourceIndex] < 0)
        {
            throw Unsupported("A native import reference has an unrepresentable aggregate type.");
        }
        return (PrimitiveType)(100 + structureMap[sourceIndex]);
    }

    private static bool Scalar(PrimitiveType type) => Array.IndexOf(s_types, type) >= 0;

    private static bool TryGetOptionalBuiltInScalarType(
        PrimitiveType type,
        IReadOnlyList<CheckedReferenceTypeDefinition> checkedReferenceTypes,
        out PrimitiveType payloadType)
    {
        payloadType = default;
        return CheckedReferenceTypeContract.IsOptionalValueType(type,
                checkedReferenceTypes)
            && SourceOptionalTypes.TryGetRequired(type,
                checkedReferenceTypes, out payloadType)
            && PrimitiveTypeFacts.IsBuiltInScalar(payloadType);
    }

    private static bool CanReinterpret(PrimitiveType source, PrimitiveType destination)
        => (source is PrimitiveType.Int32 or PrimitiveType.UInt32
                && destination is PrimitiveType.Int32 or PrimitiveType.UInt32)
            || (source is PrimitiveType.Int64 or PrimitiveType.UInt64
                or PrimitiveType.NativeInt or PrimitiveType.NativeUInt
                && destination is PrimitiveType.Int64 or PrimitiveType.UInt64
                    or PrimitiveType.NativeInt or PrimitiveType.NativeUInt);

    private static bool IsInteger(PrimitiveType type)
        => type is PrimitiveType.Int32 or PrimitiveType.UInt32
            or PrimitiveType.Int64 or PrimitiveType.UInt64
            or PrimitiveType.NativeInt or PrimitiveType.NativeUInt;

    private static bool IsFloat(PrimitiveType type)
        => type is PrimitiveType.Float32 or PrimitiveType.Float64;

    private static (bool HasFloatingTypes, bool HasIndexedFlow,
        bool HasNonScalarStorage) AnalyzeFunction(BoundFunction function,
        BoundProgram program,
        bool includeGeneralPredicateScalars,
        bool includeOptionalScalarValues)
    {
        var hasFloatingTypes = IsFloat(function.ReturnType);
        var hasNonScalarStorage = false;
        for (var index = 0; index < function.Variables.Length; index++)
        {
            var variable = function.Variables[index];
            var optionalScalarPayloadType = default(PrimitiveType);
            var isOptionalScalar = includeOptionalScalarValues
                && TryGetOptionalBuiltInScalarType(variable.Type,
                    program.CheckedReferenceTypes,
                    out optionalScalarPayloadType);
            hasFloatingTypes |= IsFloat(variable.Type)
                || (isOptionalScalar
                    && IsFloat(optionalScalarPayloadType));
            var isNativeCallableParameter = function.NativeFunctionPointerType is not null
                && index < function.ParameterCount
                && (variable.Type == PrimitiveType.NativeRawPointer
                    || NativeFunctionPointerTypes.IsType(variable.Type));
            hasNonScalarStorage |= (!Scalar(variable.Type)
                    && !(includeGeneralPredicateScalars
                        && (PrimitiveTypeFacts.IsBuiltInScalar(variable.Type)
                            || isOptionalScalar))
                    && !isNativeCallableParameter
                    && !NativeFunctionPointerTypes.IsType(variable.Type)
                    && !NativePointerTypes.IsType(variable.Type))
                || variable.Type == PrimitiveType.Void;
        }

        var hasIndexedFlow = function.IndexedBody is not null;
        if (function.IndexedBody is TrRmScalarGraph indexedBody)
        {
            hasFloatingTypes |= IsFloat(indexedBody.ResultType);
            foreach (var type in indexedBody.Types)
            {
                hasFloatingTypes |= IsFloat(type);
            }
        }
        AnalyzeStatement(function.Body, false, ref hasFloatingTypes, ref hasIndexedFlow);
        return (hasFloatingTypes, hasIndexedFlow, hasNonScalarStorage);
    }

    private static bool HasNativeFunctionPointerBodyOperation(BoundFunction function)
    {
        var hasNativeFunctionPointerOperation = false;
        _ = BoundTreeTraversal.RewriteExpressions(function, expression => {
            hasNativeFunctionPointerOperation |= expression is BoundNativeFunctionAddress
                or BoundIndirectCall;
            return expression;
        });
        return hasNativeFunctionPointerOperation;
    }

    private static void AnalyzeStatement(BoundStatement statement, bool nested,
        ref bool hasFloatingTypes, ref bool hasIndexedFlow)
    {
        if (hasFloatingTypes && hasIndexedFlow)
        {
            return;
        }

        switch (statement)
        {
            case BoundBlock block:
                if (nested && !hasIndexedFlow)
                {
                    foreach (var child in block.Statements)
                    {
                        if (child is BoundLocal)
                        {
                            hasIndexedFlow = true;
                            break;
                        }
                    }
                }
                foreach (var child in block.Statements)
                {
                    AnalyzeStatement(child, true, ref hasFloatingTypes, ref hasIndexedFlow);
                    if (hasFloatingTypes && hasIndexedFlow)
                    {
                        return;
                    }
                }
                break;
            case BoundLocal local:
                AnalyzeExpression(local.Initializer, ref hasFloatingTypes, ref hasIndexedFlow);
                break;
            case BoundAssignment assignment:
                AnalyzeExpression(assignment.Value, ref hasFloatingTypes, ref hasIndexedFlow);
                break;
            case BoundExpressionStatement expression:
                AnalyzeExpression(expression.Expression, ref hasFloatingTypes, ref hasIndexedFlow);
                break;
            case BoundReturn { Value: BoundExpression value }:
                AnalyzeExpression(value, ref hasFloatingTypes, ref hasIndexedFlow);
                break;
            case BoundIf conditional:
                hasIndexedFlow = true;
                AnalyzeExpression(conditional.Condition, ref hasFloatingTypes, ref hasIndexedFlow);
                AnalyzeStatement(conditional.Then, true, ref hasFloatingTypes, ref hasIndexedFlow);
                if (conditional.Else is not null)
                {
                    AnalyzeStatement(conditional.Else, true, ref hasFloatingTypes, ref hasIndexedFlow);
                }
                break;
            case BoundWhile loop:
                hasIndexedFlow = true;
                AnalyzeExpression(loop.Condition, ref hasFloatingTypes, ref hasIndexedFlow);
                AnalyzeStatement(loop.Body, true, ref hasFloatingTypes, ref hasIndexedFlow);
                if (loop.Iterator is not null)
                {
                    AnalyzeStatement(loop.Iterator, true, ref hasFloatingTypes, ref hasIndexedFlow);
                }
                break;
            case BoundUnsafe unsafeBlock:
                hasIndexedFlow = true;
                AnalyzeStatement(unsafeBlock.Body, true, ref hasFloatingTypes, ref hasIndexedFlow);
                break;
            case BoundRequire require:
                hasIndexedFlow = true;
                AnalyzeExpression(require.Condition, ref hasFloatingTypes, ref hasIndexedFlow);
                break;
        }
    }

    private static void AnalyzeExpression(BoundExpression expression,
        ref bool hasFloatingTypes, ref bool hasIndexedFlow)
    {
        if (hasFloatingTypes && hasIndexedFlow)
        {
            return;
        }
        hasFloatingTypes |= IsFloat(expression.Type);
        if (hasFloatingTypes && hasIndexedFlow)
        {
            return;
        }

        switch (expression)
        {
            case BoundUnsafeExpression unsafeExpression:
                hasIndexedFlow = true;
                AnalyzeExpression(unsafeExpression.Expression,
                    ref hasFloatingTypes, ref hasIndexedFlow);
                break;
            case BoundBinary binary:
                hasIndexedFlow |= binary.Operator is TokenKind.ShiftLeft or TokenKind.ShiftRight
                    or TokenKind.ShiftRightUnsigned or TokenKind.AmpersandAmpersand or TokenKind.PipePipe;
                AnalyzeExpression(binary.Left, ref hasFloatingTypes, ref hasIndexedFlow);
                AnalyzeExpression(binary.Right, ref hasFloatingTypes, ref hasIndexedFlow);
                break;
            case BoundUnary unary:
                AnalyzeExpression(unary.Operand, ref hasFloatingTypes, ref hasIndexedFlow);
                break;
            case BoundCast cast:
                AnalyzeExpression(cast.Operand, ref hasFloatingTypes, ref hasIndexedFlow);
                break;
            case BoundConditional conditional:
                hasIndexedFlow = true;
                AnalyzeExpression(conditional.Condition, ref hasFloatingTypes, ref hasIndexedFlow);
                AnalyzeExpression(conditional.WhenTrue, ref hasFloatingTypes, ref hasIndexedFlow);
                AnalyzeExpression(conditional.WhenFalse, ref hasFloatingTypes, ref hasIndexedFlow);
                break;
            case BoundCall call:
                hasIndexedFlow = true;
                foreach (var argument in call.Arguments)
                {
                    AnalyzeExpression(argument, ref hasFloatingTypes, ref hasIndexedFlow);
                    if (hasFloatingTypes && hasIndexedFlow)
                    {
                        return;
                    }
                }
                break;
            case BoundIndirectCall call:
                hasIndexedFlow = true;
                AnalyzeExpression(call.Target, ref hasFloatingTypes, ref hasIndexedFlow);
                foreach (var argument in call.Arguments)
                {
                    AnalyzeExpression(argument, ref hasFloatingTypes, ref hasIndexedFlow);
                    if (hasFloatingTypes && hasIndexedFlow)
                    {
                        return;
                    }
                }
                break;
            case BoundNativeFunctionAddress:
                hasIndexedFlow = true;
                break;
            case BoundUpdate or BoundCompoundAssignment:
                hasIndexedFlow = true;
                break;
        }
    }

    private static ImmutableArray<int> CollectNativeBlittableStructureIndexes(BoundProgram program)
    {
        var required = new bool[program.Structures.Length];
        var visiting = new bool[program.Structures.Length];
        var pointerVisited = new bool[program.NativePointerTypes.Length];
        var functionPointerVisited = new bool[program.NativeFunctionPointerSignatures.Length];
        foreach (var function in program.Functions)
        {
            if (function.DirectNativeImport is DirectNativeImport import)
            {
                foreach (var parameter in import.Parameters)
                {
                    VisitType(parameter.PointeeType, throughPointer: true);
                }
            }

            if (function.NativeVTableMethod is NativeVTableMethodDefinition vtableMethod)
            {
                foreach (var parameter in vtableMethod.Parameters)
                {
                    VisitType(parameter.PointeeType, throughPointer: true);
                }
            }
        }
        for (var index = 0; index < program.NativePointerTypes.Length; index++)
        {
            VisitType(NativePointerTypes.Type(index), throughPointer: true);
        }
        for (var index = 0; index < program.NativeFunctionPointerSignatures.Length; index++)
        {
            var signature = program.NativeFunctionPointerSignatures[index];
            VisitType(signature.ReturnType, throughPointer: true);
            foreach (var parameterType in signature.ParameterTypes)
            {
                VisitType(parameterType, throughPointer: true);
            }
        }

        var indexes = ImmutableArray.CreateBuilder<int>();
        for (var index = 0; index < required.Length; index++)
        {
            if (required[index])
            {
                indexes.Add(index);
            }
        }
        return indexes.ToImmutable();

        void VisitType(PrimitiveType type, bool throughPointer)
        {
            if (BoundEnumTypes.IsType(type))
            {
                throw Unsupported("Standard TRRM has no assigned nominal enum TypeSpec or TypeDef encoding.");
            }
            if ((int)type is >= 100 and < BoundEnumTypes.First)
            {
                Visit((int)type - 100, throughPointer);
                return;
            }
            if (NativePointerTypes.IsType(type))
            {
                var index = NativePointerTypes.Index(type);
                if ((uint)index >= (uint)program.NativePointerTypes.Length)
                {
                    throw Unsupported("A native pointer type is outside the pointer type table.");
                }
                if (pointerVisited[index])
                {
                    return;
                }

                pointerVisited[index] = true;
                VisitType(program.NativePointerTypes[index].PointeeType, throughPointer: true);
                return;
            }
            if (NativeFunctionPointerTypes.IsType(type))
            {
                var index = NativeFunctionPointerTypes.Index(type);
                if ((uint)index >= (uint)program.NativeFunctionPointerSignatures.Length)
                {
                    throw Unsupported("A native function-pointer type is outside its signature table.");
                }
                if (functionPointerVisited[index])
                {
                    return;
                }

                functionPointerVisited[index] = true;
                var signature = program.NativeFunctionPointerSignatures[index];
                VisitType(signature.ReturnType, throughPointer: true);
                foreach (var parameterType in signature.ParameterTypes)
                {
                    VisitType(parameterType, throughPointer: true);
                }
                return;
            }
            if (DirectNativeImportContract.IsScalar(type)
                || type is PrimitiveType.NativeRawPointer or PrimitiveType.Void)
            {
                return;
            }

            throw Unsupported($"Native value type '{type}' has no provisional TRRM encoding.");
        }

        void Visit(int index, bool throughPointer)
        {
            if (index < 0 || index >= program.Structures.Length)
            {
                throw Unsupported("A native aggregate pointee is outside the structure table.");
            }
            if (required[index])
            {
                return;
            }
            if (visiting[index])
            {
                if (throughPointer)
                {
                    return;
                }
                throw Unsupported("A native Blittable structure graph is recursive.");
            }

            var structure = program.Structures[index];
            if (structure.BlittableTargetRequirement != FixedLayoutStorage.WindowsX64TargetRequirement
                || structure.LayoutKind is not (1 or 2 or 3)
                || structure.IsUnique || structure.CleanupMethod is not null
                || (structure.IsPrivate && structure.IsInternal)
                || structure.Fields.IsDefaultOrEmpty)
            {
                throw Unsupported($"Native aggregate '{structure.Name}' lacks a supported fixed Blittable contract.");
            }

            visiting[index] = true;
            foreach (var field in structure.Fields)
            {
                if (field is null || field.RequiresUnsafeCaller
                    || (field.IsPrivate && field.IsInternal))
                {
                    throw Unsupported($"Native Blittable field in '{structure.Name}' has unsupported access metadata.");
                }

                if (DirectNativeImportContract.IsScalar(field.Type))
                {
                    continue;
                }
                VisitType(field.Type, throughPointer: NativePointerTypes.IsType(field.Type)
                    || NativeFunctionPointerTypes.IsType(field.Type));
            }

            visiting[index] = false;
            required[index] = true;
        }
    }

    private static int TypeRow(PrimitiveType type)
    {
        if (BoundEnumTypes.IsType(type))
        {
            throw Unsupported("Standard TRRM has no assigned nominal enum TypeSpec or TypeDef encoding.");
        }
        var row = Array.IndexOf(s_types, type) + 1;
        if (row == 0)
        {
            throw Unsupported($"Unsupported scalar type {type}.");
        }
        return row;
    }

    private static TrRmNativeOutputContract? FindNativeOutput(
        ImmutableArray<TrRmNativeOutputContract> outputs, int parameterIndex)
    {
        foreach (var output in outputs)
        {
            if (output.ParameterOrdinal == parameterIndex)
            {
                return output;
            }
        }

        return null;
    }

    private static TrRmNativeParameterContract? FindNativeParameter(
        ImmutableArray<TrRmNativeParameterContract> parameters, int parameterIndex)
    {
        if (parameters.IsDefaultOrEmpty)
        {
            return null;
        }

        foreach (var parameter in parameters)
        {
            if (parameter.ParameterOrdinal == parameterIndex)
            {
                return parameter;
            }
        }

        return null;
    }

    private static int NativeOutputTypeRow(List<ReadOnlyMemory<byte>> typeRows,
        Dictionary<PrimitiveType, int> typeRowMap, PrimitiveType pointeeType)
    {
        if (typeRowMap.TryGetValue(pointeeType, out var row))
        {
            return row;
        }

        if (pointeeType == PrimitiveType.NativeRawPointer)
        {
            var innerRow = typeRows.Count + 1;
            typeRows.Add(TrRmTypeSpecTableWriter.InlineFamily(0x8C, 0));
            row = typeRows.Count + 1;
            typeRows.Add(TrRmTypeSpecTableWriter.IndexedFamily(0x8C, (uint)innerRow, 1));
            typeRowMap.Add(pointeeType, row);
            return row;
        }

        var typeIndex = Array.IndexOf(s_types, pointeeType);
        if (typeIndex < 0)
        {
            throw Unsupported($"Unsupported native output pointee type {pointeeType}.");
        }
        row = typeRows.Count + 1;
        typeRows.Add(TrRmTypeSpecTableWriter.InlineFamily(0x8C, s_codes[typeIndex], 1));
        typeRowMap.Add(pointeeType, row);
        return row;
    }

    private static int NativeParameterTypeRow(List<ReadOnlyMemory<byte>> typeRows,
        Dictionary<PrimitiveType, int> typeRowMap, PrimitiveType pointeeType)
    {
        if (typeRowMap.TryGetValue(pointeeType, out var row))
        {
            return row;
        }

        if (pointeeType == PrimitiveType.Void)
        {
            row = typeRows.Count + 1;
            typeRows.Add(TrRmTypeSpecTableWriter.InlineFamily(0x8C, 0));
        }
        else if (pointeeType == PrimitiveType.NativeRawPointer)
        {
            var innerRow = typeRows.Count + 1;
            typeRows.Add(TrRmTypeSpecTableWriter.InlineFamily(0x8C, 0));
            row = typeRows.Count + 1;
            typeRows.Add(TrRmTypeSpecTableWriter.IndexedFamily(0x8C, (uint)innerRow));
        }
        else
        {
            var typeIndex = Array.IndexOf(s_types, pointeeType);
            if (typeIndex < 0)
            {
                throw Unsupported($"Unsupported native parameter pointee type {pointeeType}.");
            }
            row = typeRows.Count + 1;
            typeRows.Add(TrRmTypeSpecTableWriter.InlineFamily(0x8C, s_codes[typeIndex]));
        }

        typeRowMap.Add(pointeeType, row);
        return row;
    }

    private static int NativeParameterTypeRow(List<ReadOnlyMemory<byte>> typeRows,
        Dictionary<PrimitiveType, int> typeRowMap, PrimitiveType pointeeType,
        Dictionary<int, int> structureTypeDefRows, Dictionary<int, int> typeDefTypeSpecRows,
        Dictionary<PrimitiveType, int> structurePointerTypeRows)
    {
        if (BoundEnumTypes.IsType(pointeeType))
        {
            throw Unsupported("Native pointer parameters cannot use an unrepresented enum pointee type.");
        }
        if ((int)pointeeType is >= 100 and < BoundEnumTypes.First)
        {
            if (structurePointerTypeRows.TryGetValue(pointeeType, out var existing))
            {
                return existing;
            }

            var structureIndex = (int)pointeeType - 100;
            if (!structureTypeDefRows.TryGetValue(structureIndex, out var typeDefRow))
            {
                throw Unsupported($"Unsupported native parameter aggregate pointee {pointeeType}.");
            }

            var typeDefTypeSpecRow = TypeDefTypeSpecRow(typeRows, typeDefTypeSpecRows, typeDefRow);
            var pointerTypeSpecRow = typeRows.Count + 1;
            typeRows.Add(TrRmTypeSpecTableWriter.IndexedFamily(0x8C,
                (uint)typeDefTypeSpecRow));
            structurePointerTypeRows.Add(pointeeType, pointerTypeSpecRow);
            return pointerTypeSpecRow;
        }

        return NativeParameterTypeRow(typeRows, typeRowMap, pointeeType);
    }

    private static int NativeValueTypeRow(PrimitiveType type, int nativeRawPointerTypeRow,
        Dictionary<PrimitiveType, int> nativeFunctionPointerTypeRows)
    {
        if (NativeFunctionPointerTypes.IsType(type))
        {
            return nativeFunctionPointerTypeRows.TryGetValue(type, out var row)
                ? row : throw Unsupported("A native function-pointer type has no TypeSpec row.");
        }

        if (type != PrimitiveType.NativeRawPointer)
        {
            return TypeRow(type);
        }

        return nativeRawPointerTypeRow != 0
            ? nativeRawPointerTypeRow
            : throw Unsupported("A native vtable pointer type has no raw-pointer TypeSpec row.");
    }

    private static int NativeValueTypeRow(PrimitiveType type, int nativeRawPointerTypeRow,
        Dictionary<PrimitiveType, int> nativeFunctionPointerTypeRows,
        List<ReadOnlyMemory<byte>> typeRows,
        Dictionary<PrimitiveType, int> nativePointerTypeRows,
        ImmutableArray<NativePointerTypeDefinition> nativePointerTypes,
        Dictionary<int, int> structureTypeDefRows,
        Dictionary<int, int> typeDefTypeSpecRows,
        Func<PrimitiveType, int>? typeSpecRowOverride = null)
    {
        if (CheckedReferenceTypes.IsType(type))
        {
            return typeSpecRowOverride is not null
                ? typeSpecRowOverride(type)
                : throw Unsupported(
                    "A checked-reference value has no complete TypeSpec mapping.");
        }

        if (BoundEnumTypes.IsType(type))
        {
            throw Unsupported("Standard TRRM has no assigned nominal enum TypeSpec or TypeDef encoding.");
        }
        if (NativePointerTypes.IsType(type))
        {
            return NativePointerTypeRow(type, nativeRawPointerTypeRow,
                nativeFunctionPointerTypeRows, typeRows, nativePointerTypeRows,
                nativePointerTypes, structureTypeDefRows, typeDefTypeSpecRows,
                typeSpecRowOverride);
        }

        if (NativeFunctionPointerTypes.IsType(type))
        {
            return nativeFunctionPointerTypeRows.TryGetValue(type, out var row)
                ? row : throw Unsupported("A native function-pointer type has no TypeSpec row.");
        }

        if (type == PrimitiveType.NativeRawPointer)
        {
            return nativeRawPointerTypeRow != 0
                ? nativeRawPointerTypeRow
                : throw Unsupported("A native vtable pointer type has no raw-pointer TypeSpec row.");
        }

        if ((int)type is >= 100 and < BoundEnumTypes.First)
        {
            var structureIndex = (int)type - 100;
            if (!structureTypeDefRows.TryGetValue(structureIndex, out var typeDefRow))
            {
                throw Unsupported($"A native pointer refers to an unrepresented structure type {type}.");
            }

            return TypeDefTypeSpecRow(typeRows, typeDefTypeSpecRows, typeDefRow);
        }

        return typeSpecRowOverride is not null
                && PrimitiveTypeFacts.IsBuiltInScalar(type)
            ? typeSpecRowOverride(type)
            : TypeRow(type);
    }

    private static int NativePointerTypeRow(PrimitiveType pointerType,
        int nativeRawPointerTypeRow,
        Dictionary<PrimitiveType, int> nativeFunctionPointerTypeRows,
        List<ReadOnlyMemory<byte>> typeRows,
        Dictionary<PrimitiveType, int> nativePointerTypeRows,
        ImmutableArray<NativePointerTypeDefinition> nativePointerTypes,
        Dictionary<int, int> structureTypeDefRows,
        Dictionary<int, int> typeDefTypeSpecRows,
        Func<PrimitiveType, int>? typeSpecRowOverride = null)
    {
        if (nativePointerTypeRows.TryGetValue(pointerType, out var row))
        {
            return row;
        }

        var pointerIndex = NativePointerTypes.Index(pointerType);
        if ((uint)pointerIndex >= (uint)nativePointerTypes.Length)
        {
            throw Unsupported("A native pointer type has no definition.");
        }

        var definition = nativePointerTypes[pointerIndex];
        var qualifiers = definition.IsWritable ? (byte)1 : (byte)0;
        if (definition.PointeeType == PrimitiveType.Void)
        {
            row = typeRows.Count + 1;
            typeRows.Add(TrRmTypeSpecTableWriter.InlineFamily(0x8C, 0, qualifiers));
        }
        else if (Array.IndexOf(s_types, definition.PointeeType) is var scalarIndex
            && scalarIndex >= 0)
        {
            row = typeRows.Count + 1;
            typeRows.Add(TrRmTypeSpecTableWriter.InlineFamily(0x8C,
                s_codes[scalarIndex], qualifiers));
        }
        else
        {
            var pointeeRow = typeSpecRowOverride is null
                ? NativeValueTypeRow(definition.PointeeType,
                    nativeRawPointerTypeRow, nativeFunctionPointerTypeRows,
                    typeRows, nativePointerTypeRows, nativePointerTypes,
                    structureTypeDefRows, typeDefTypeSpecRows)
                : typeSpecRowOverride(definition.PointeeType);
            row = typeRows.Count + 1;
            typeRows.Add(TrRmTypeSpecTableWriter.IndexedFamily(0x8C,
                checked((uint)pointeeRow), qualifiers));
        }

        nativePointerTypeRows.Add(pointerType, row);
        return row;
    }

    private static int NativeBlittableFieldTypeRow(List<ReadOnlyMemory<byte>> typeRows,
        Dictionary<int, int> typeDefTypeSpecRows, Dictionary<int, int> structureTypeDefRows,
        PrimitiveType fieldType, int nativeRawPointerTypeRow,
        Dictionary<PrimitiveType, int> nativeFunctionPointerTypeRows,
        Dictionary<PrimitiveType, int> nativePointerTypeRows,
        ImmutableArray<NativePointerTypeDefinition> nativePointerTypes,
        Func<PrimitiveType, int>? typeSpecRowOverride = null)
    {
        if (BoundEnumTypes.IsType(fieldType))
        {
            throw Unsupported("Standard TRRM has no assigned enum layout/type encoding.");
        }
        if ((int)fieldType is >= 100 and < BoundEnumTypes.First)
        {
            var structureIndex = (int)fieldType - 100;
            if (!structureTypeDefRows.TryGetValue(structureIndex, out var typeDefRow))
            {
                throw Unsupported($"Unsupported native Blittable field type {fieldType}.");
            }
            return TypeDefTypeSpecRow(typeRows, typeDefTypeSpecRows, typeDefRow);
        }

        return NativeValueTypeRow(fieldType, nativeRawPointerTypeRow,
            nativeFunctionPointerTypeRows, typeRows, nativePointerTypeRows,
            nativePointerTypes, structureTypeDefRows, typeDefTypeSpecRows,
            typeSpecRowOverride);
    }

    private static int TypeDefTypeSpecRow(List<ReadOnlyMemory<byte>> typeRows,
        Dictionary<int, int> typeDefTypeSpecRows, int typeDefRow)
    {
        if (typeDefTypeSpecRows.TryGetValue(typeDefRow, out var row))
        {
            return row;
        }

        row = typeRows.Count + 1;
        typeRows.Add(TrRmTypeSpecTableWriter.Reference(0x02, (uint)typeDefRow));
        typeDefTypeSpecRows.Add(typeDefRow, row);
        return row;
    }

    private static byte NativeAbiConvention(TrRmArtifactSchema schema,
        NativeCallingConvention? convention, byte role)
    {
        if (!schema.UsesDevelopmentNativeConventions())
        {
            return 0;
        }

        if (convention is not NativeCallingConvention selected
            || !TrRmDevelopmentNativeConventions.TryEncodeConvention(
                selected, role, out var value))
        {
            throw Unsupported(
                "The selected Development schema cannot represent this native convention.");
        }

        return value;
    }

    private static ReadOnlyMemory<byte> StandardNativeCallableContractPayload(
        NativeCallingConvention convention)
    {
        var conventionByte = convention switch {
            NativeCallingConvention.Unmanaged => (byte)1,
            NativeCallingConvention.Cdecl => (byte)2,
            NativeCallingConvention.Stdcall => (byte)3,
            NativeCallingConvention.Vectorcall => (byte)4,
            _ => throw Unsupported(
                "The Standard NativeCallable clause requires one of the four registered conventions."),
        };
        var payload = new byte[4];
        payload[0] = conventionByte;
        return payload;
    }

    private static ReadOnlyMemory<byte> NativeAbiPayload(byte role = 1,
        byte conventionOrReserved = 0)
    {
        var payload = new byte[8];
        BinaryPrimitives.WriteUInt32LittleEndian(payload, 1);
        payload[4] = role;
        payload[6] = conventionOrReserved;
        return payload;
    }

    private static ReadOnlyMemory<byte> NativeOutputPayload(int parameterIndex,
        uint resultCondition, uint failureDisposition)
    {
        var condition = resultCondition switch {
            DirectNativeImportContract.Int32NonZeroResultCondition
                or DirectNativeImportContract.UInt32NonZeroResultCondition => (byte)2,
            DirectNativeImportContract.Int32NonNegativeResultCondition => (byte)4,
            _ => throw Unsupported("Unsupported native output result condition."),
        };
        if (failureDisposition != 1)
        {
            throw Unsupported("Unsupported native output failure disposition.");
        }

        var payload = new byte[20];
        BinaryPrimitives.WriteUInt32LittleEndian(payload, (uint)parameterIndex);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(4), 1);
        payload[8] = condition;
        payload[9] = 2;
        return payload;
    }

    private static ReadOnlyMemory<byte> NativeParameterPayload(int ordinal, byte access,
        byte retention, uint extent)
    {
        var payload = new byte[20];
        if (retention != (byte)NativeParameterRetention.NoEscape)
        {
            throw Unsupported("Only call-extent native parameter retention is supported.");
        }

        BinaryPrimitives.WriteUInt32LittleEndian(payload, (uint)ordinal);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(4), 1);
        payload[8] = access;
        payload[9] = 1;
        payload[10] = extent == 0 ? (byte)0 : (byte)1;
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(12), extent);
        return payload;
    }

    private static ReadOnlyMemory<byte> StandardNativeParameterContractPayload(
        int ordinal, NativeParameterAccess access,
        NativeParameterRetention retention, uint extent)
    {
        if (access is not (NativeParameterAccess.Read
                or NativeParameterAccess.Write
                or NativeParameterAccess.ReadWrite)
            || retention != NativeParameterRetention.NoEscape)
        {
            throw Unsupported(
                "The selected Standard NativeParameter profile supports only bounded NoEscape pointer contracts.");
        }

        var payload = new byte[12];
        BinaryPrimitives.WriteUInt32LittleEndian(payload,
            checked((uint)ordinal));
        payload[4] = checked((byte)((byte)access + 1));
        payload[5] = 1;
        payload[6] = extent == 0 ? (byte)0 : (byte)1;
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(8), extent);
        return payload;
    }

    private readonly record struct StandardFunctionPointerAnchorClosure(
        ImmutableArray<int> AnchorTypeSpecRows,
        ImmutableArray<int> AdditionalTypeDefRows);

    private static StandardFunctionPointerAnchorClosure
        CollectStandardFunctionPointerAnchorClosure(
        TrRmStructuralMetadataValidator.ValidatedExecutionTables tables,
        int selectedModuleRow, int[] structureTypeByTypeDef,
        bool allowCrossModuleAnchors)
    {
        var anchors = tables.NativeFunctionPointerAnchors;
        var reachableAnchorTypeSpecRows = new HashSet<int>();
        var additionalTypeDefRows = new HashSet<int>();
        var visitedTypeSpecContexts
            = new HashSet<(uint TypeSpecReference, int ModuleRow,
                bool AnchorContext)>();
        var pendingTypeSpecReferences
            = new Stack<(uint TypeSpecReference, int ModuleRow,
                bool AnchorContext)>();
        var pendingAdditionalTypeDefs = new Queue<int>();
        var visitedAdditionalTypeDefs = new HashSet<int>();
        var remainingReachabilityWork
            = SemanticVerifier.MaximumAnalysisVisits;
        if (!tables.TargetProfiles.TryGetProfile(1, 1,
                out var windowsX64ProfileRow))
        {
            throw Invalid(
                "The defining-Module TypeDef closure has no Windows x64 TargetProfile.");
        }

        void EnqueueTypeSpecReference(uint typeSpecReference,
            int moduleRow, bool anchorContext)
        {
            if (typeSpecReference == 0)
            {
                return;
            }

            if (--remainingReachabilityWork < 0)
            {
                throw new NotSupportedException(
                    "The Standard function-pointer anchor reachability exceeds the analysis limit.");
            }

            pendingTypeSpecReferences.Push((typeSpecReference, moduleRow,
                anchorContext));
        }

        var (selectedMethodStart, selectedMethodEnd)
            = tables.Modules.GetMethodDefRange(selectedModuleRow);
        for (var methodRow = selectedMethodStart;
            methodRow < selectedMethodEnd; methodRow++)
        {
            var signatureRow = tables.Methods.GetSignature(methodRow);
            EnqueueTypeSpecReference(
                tables.Signatures.GetResultTypeSpec(signatureRow),
                selectedModuleRow, anchorContext: false);
            var (firstParameter, parameterCount)
                = tables.Signatures.GetParamRange(signatureRow);
            for (var ordinal = 0; ordinal < parameterCount; ordinal++)
            {
                EnqueueTypeSpecReference(
                    tables.Parameters.GetTypeSpec(firstParameter + ordinal),
                    selectedModuleRow, anchorContext: false);
            }

            var bodyRow = tables.Methods.GetBodyRow(methodRow);
            if (bodyRow == 0
                || tables.Bodies.GetRow(bodyRow)[0] != 0x01)
            {
                continue;
            }

            var body = tables.Bodies.GetRegisterBody(bodyRow,
                tables.Contracts.Count);
            for (var typeMapIndex = 0; typeMapIndex < body.TypeCount;
                typeMapIndex++)
            {
                var token = BinaryPrimitives.ReadUInt32LittleEndian(
                    body.TypeMap.Slice(typeMapIndex * sizeof(uint)));
                if (token >> 24 == 0x11)
                {
                    EnqueueTypeSpecReference(
                        token & 0x00FF_FFFFu, selectedModuleRow,
                        anchorContext: false);
                }
            }
        }

        var (selectedTypeDefStart, selectedTypeDefEnd)
            = tables.Modules.GetTypeDefRange(selectedModuleRow);
        for (var typeDefRow = selectedTypeDefStart;
            typeDefRow < selectedTypeDefEnd; typeDefRow++)
        {
            if (!tables.Layouts.HasBlittableLayout(typeDefRow, 1, 1))
            {
                continue;
            }

            var layoutRow = tables.Layouts.GetLayoutForProfile(typeDefRow,
                checked((uint)windowsX64ProfileRow));
            var (firstField, fieldCount)
                = tables.Layouts.GetLayoutFieldRange(layoutRow);
            for (var ordinal = 0; ordinal < fieldCount; ordinal++)
            {
                var fieldRow = tables.LayoutFields.GetFieldDef(
                    firstField + ordinal);
                EnqueueTypeSpecReference(
                    tables.Fields.GetTypeSpecRef(fieldRow),
                    selectedModuleRow, anchorContext: false);
            }
        }

        for (var typeSpecRow = 1;
            typeSpecRow <= tables.TypeSpecs.Count; typeSpecRow++)
        {
            if (anchors.HasAnchorForTypeSpec(typeSpecRow)
                && tables.Methods.GetOwnerModule(
                    anchors.GetAnchorMethodDefRow(typeSpecRow))
                    == selectedModuleRow
                && reachableAnchorTypeSpecRows.Add(typeSpecRow))
            {
                var methodDefRow
                    = anchors.GetAnchorMethodDefRow(typeSpecRow);
                var signatureRow
                    = tables.Methods.GetSignature(methodDefRow);
                var ownerModuleRow
                    = tables.Methods.GetOwnerModule(methodDefRow);
                EnqueueTypeSpecReference(
                    tables.Signatures.GetResultTypeSpec(signatureRow),
                    ownerModuleRow, anchorContext: true);
                var (firstParameter, parameterCount)
                    = tables.Signatures.GetParamRange(signatureRow);
                for (var ordinal = 0; ordinal < parameterCount; ordinal++)
                {
                    EnqueueTypeSpecReference(
                        tables.Parameters.GetTypeSpec(
                            firstParameter + ordinal),
                        ownerModuleRow, anchorContext: true);
                }
            }
        }

        while (pendingTypeSpecReferences.Count != 0
            || pendingAdditionalTypeDefs.Count != 0)
        {
            if (pendingTypeSpecReferences.TryPop(
                out var pendingTypeSpec))
            {
                var (typeSpecReference, contextModuleRow, anchorContext)
                    = pendingTypeSpec;
                if (!visitedTypeSpecContexts.Add((typeSpecReference,
                        contextModuleRow, anchorContext)))
                {
                    continue;
                }

                if (--remainingReachabilityWork < 0)
                {
                    throw new NotSupportedException(
                        "The Standard function-pointer anchor reachability exceeds the analysis limit.");
                }

                var typeSpec = tables.TypeSpecs.GetTypeSpecRef(
                    typeSpecReference);
                if (!typeSpec.IsRow)
                {
                    continue;
                }

                if (anchors.HasAnchorForTypeSpec(typeSpec.Row))
                {
                    var methodDefRow
                        = anchors.GetAnchorMethodDefRow(typeSpec.Row);
                    var ownerModuleRow
                        = tables.Methods.GetOwnerModule(methodDefRow);
                    if (ownerModuleRow != selectedModuleRow
                        && !allowCrossModuleAnchors)
                    {
                        continue;
                    }

                    if (reachableAnchorTypeSpecRows.Add(typeSpec.Row))
                    {
                        var signatureRow
                            = tables.Methods.GetSignature(methodDefRow);
                        EnqueueTypeSpecReference(
                            tables.Signatures.GetResultTypeSpec(signatureRow),
                            ownerModuleRow, anchorContext: true);
                        var (firstParameter, parameterCount)
                            = tables.Signatures.GetParamRange(signatureRow);
                        for (var ordinal = 0; ordinal < parameterCount;
                            ordinal++)
                        {
                            EnqueueTypeSpecReference(
                                tables.Parameters.GetTypeSpec(
                                    firstParameter + ordinal),
                                ownerModuleRow, anchorContext: true);
                        }
                    }

                    continue;
                }

                var typeSpecBytes = tables.TypeSpecs.GetTypeSpecBytes(
                    typeSpecReference);
                if (anchorContext && typeSpecBytes.Length == 6
                    && (typeSpecBytes[0] is 0x02 or 0x03))
                {
                    var typeDefRow = typeSpecBytes[0] == 0x02
                        ? checked((int)BinaryPrimitives
                            .ReadUInt32LittleEndian(typeSpecBytes[2..]))
                        : 0;
                    if (typeSpecBytes[0] == 0x03)
                    {
                        var importRow = checked((int)BinaryPrimitives
                            .ReadUInt32LittleEndian(typeSpecBytes[2..]));
                        var moduleRefRow
                            = tables.Imports.GetModuleRefForImportRow(
                                importRow);
                        if (tables.ModuleRefs.GetOwnerModule(moduleRefRow)
                                != contextModuleRow)
                        {
                            throw Invalid(
                                "A function-pointer anchor Import is not owned by its defining Module.");
                        }

                        var expectedDefinition = tables.Imports
                            .GetExpectedDefinitionForImportRow(importRow);
                        if ((expectedDefinition >> 24) != 0x13)
                        {
                            throw Invalid(
                                "A function-pointer anchor Import is not a TypeDef.");
                        }

                        typeDefRow = checked((int)(expectedDefinition
                            & 0x00FF_FFFF));
                    }

                    if ((uint)typeDefRow == 0
                        || (uint)typeDefRow > (uint)tables.TypeDefs.Count)
                    {
                        throw Invalid(
                            "A function-pointer anchor TypeDef is out of range.");
                    }

                    if (structureTypeByTypeDef[typeDefRow] < 0)
                    {
                        if (tables.TypeDefs.GetOwnerModule(typeDefRow)
                            == selectedModuleRow)
                        {
                            throw Unsupported(
                                "A function-pointer anchor references an unrepresented selected-Module TypeDef.");
                        }
                        if (!allowCrossModuleAnchors)
                        {
                            throw Unsupported(
                                "The selected Standard profile does not admit a cross-Module function-pointer TypeDef closure.");
                        }

                        if (!tables.Layouts.HasBlittableLayout(typeDefRow,
                                1, 1))
                        {
                            throw Unsupported(
                                "A function-pointer anchor references a TypeDef without a supported Blittable layout.");
                        }

                        if (additionalTypeDefRows.Add(typeDefRow))
                        {
                            pendingAdditionalTypeDefs.Enqueue(typeDefRow);
                        }
                    }
                }

                if (typeSpecBytes.Length == 6
                    && (typeSpecBytes[0] is >= 0x06 and <= 0x0B or 0x0E))
                {
                    EnqueueTypeSpecReference(
                        BinaryPrimitives.ReadUInt32LittleEndian(
                            typeSpecBytes[2..]),
                        contextModuleRow, anchorContext);
                }
                else if (typeSpecBytes.Length == 7
                    && typeSpecBytes[0] == 0x0C)
                {
                    EnqueueTypeSpecReference(
                        BinaryPrimitives.ReadUInt32LittleEndian(
                            typeSpecBytes[3..]),
                        contextModuleRow, anchorContext);
                }

                continue;
            }

            var additionalTypeDefRow
                = pendingAdditionalTypeDefs.Dequeue();
            if (!visitedAdditionalTypeDefs.Add(additionalTypeDefRow))
            {
                continue;
            }

            var ownerModuleRow
                = tables.TypeDefs.GetOwnerModule(additionalTypeDefRow);
            var layoutRow = tables.Layouts.GetLayoutForProfile(
                additionalTypeDefRow,
                checked((uint)windowsX64ProfileRow));
            var (firstField, fieldCount)
                = tables.Layouts.GetLayoutFieldRange(layoutRow);
            for (var ordinal = 0; ordinal < fieldCount; ordinal++)
            {
                var fieldRow = tables.LayoutFields.GetFieldDef(
                    firstField + ordinal);
                EnqueueTypeSpecReference(
                    tables.Fields.GetTypeSpecRef(fieldRow),
                    ownerModuleRow, anchorContext: true);
            }
        }

        return new StandardFunctionPointerAnchorClosure(
            [.. reachableAnchorTypeSpecRows.OrderBy(
                static row => row)],
            [.. additionalTypeDefRows.OrderBy(static row => row)]);
    }

    private static ImmutableArray<NativeFunctionPointerSignatureDefinition>
        LoadStandardFunctionPointerSignatures(
        TrRmStructuralMetadataValidator.ValidatedExecutionTables tables,
        int selectedModuleRow, int[] structureTypeByTypeDef,
        List<NativePointerTypeDefinition> nativePointerTypeDefinitions,
        int[] signatureIndexByTypeSpecRow,
        ImmutableArray<int> anchorTypeSpecRows)
    {
        var anchors = tables.NativeFunctionPointerAnchors;
        var indexByAnchor = new Dictionary<(byte Convention,
            int MethodDefRow), int>();
        var anchorsToDecode = new List<(byte Convention,
            int MethodDefRow)>();
        foreach (var typeSpecRow in anchorTypeSpecRows)
        {
            if (!anchors.HasAnchorForTypeSpec(typeSpecRow))
            {
                throw Invalid(
                    "The selected function-pointer anchor closure contains a non-anchor TypeSpec.");
            }

            var methodDefRow = anchors.GetAnchorMethodDefRow(typeSpecRow);
            var convention = anchors.GetCallingConvention(typeSpecRow);
            var key = (convention, methodDefRow);
            if (!indexByAnchor.TryGetValue(key, out var signatureIndex))
            {
                if (anchorsToDecode.Count
                    >= SemanticVerifier.MaximumAnalysisVisits)
                {
                    throw new NotSupportedException(
                        "The selected Standard function-pointer anchor table exceeds the analysis limit.");
                }

                signatureIndex = anchorsToDecode.Count;
                indexByAnchor.Add(key, signatureIndex);
                anchorsToDecode.Add(key);
            }

            signatureIndexByTypeSpecRow[typeSpecRow] = signatureIndex;
        }

        if (anchorsToDecode.Count != 0
            && !TrRmFeatureManifestValidator.Requires(tables.Features,
                tables.Modules, selectedModuleRow,
                TrRmFeatureTableReader.RootlessNativeValuesFeatureId,
                tables.Schema))
        {
            throw Invalid(
                "A Standard function-pointer value requires RootlessNativeValues 2/1.");
        }

        var signatures
            = ImmutableArray.CreateBuilder<
                NativeFunctionPointerSignatureDefinition>(
                    anchorsToDecode.Count);
        foreach (var (convention, methodDefRow) in anchorsToDecode)
        {
            var signatureRow
                = tables.Methods.GetSignature(methodDefRow);
            var returnType = FromNativeValueRow(tables.TypeSpecs,
                tables.Signatures.GetResultTypeSpec(signatureRow),
                nativePointerTypeDefinitions, structureTypeByTypeDef,
                tables.Imports,
                functionPointerSignatureIndexByAnchorTypeSpec:
                    signatureIndexByTypeSpecRow);
            var (firstParameter, parameterCount)
                = tables.Signatures.GetParamRange(signatureRow);
            var parameterTypes
                = ImmutableArray.CreateBuilder<PrimitiveType>(
                    parameterCount);
            for (var ordinal = 0; ordinal < parameterCount; ordinal++)
            {
                parameterTypes.Add(FromNativeValueRow(tables.TypeSpecs,
                    tables.Parameters.GetTypeSpec(firstParameter + ordinal),
                    nativePointerTypeDefinitions, structureTypeByTypeDef,
                    tables.Imports,
                    functionPointerSignatureIndexByAnchorTypeSpec:
                        signatureIndexByTypeSpecRow));
            }

            var predicateContracts
                = tables.Contracts.GetPredicateGraphContracts(methodDefRow);
            var selectorOrdinalByGraphRow
                = new Dictionary<int, uint>(predicateContracts.Length);
            var selectors
                = ImmutableArray.CreateBuilder<
                    NativeFunctionPointerSelector>(
                        predicateContracts.Length);
            foreach (var predicate in predicateContracts)
            {
                if (predicate.PhaseTag != 3)
                {
                    throw Unsupported(
                        "A Standard function-pointer anchor contains an unsupported predicate phase.");
                }

                var rootNode = tables.PredicateGraphs.GetRootNodeRow(
                    predicate.PredicateGraphRow);
                var nodeKind = tables.PredicateNodes.GetNodeKind(rootNode);
                if (nodeKind > byte.MaxValue
                    || !selectorOrdinalByGraphRow.TryAdd(
                        predicate.PredicateGraphRow,
                        predicate.PhaseLocalOrdinal))
                {
                    throw Invalid(
                        "A Standard function-pointer selector is malformed.");
                }

                selectors.Add(new NativeFunctionPointerSelector(
                    predicate.PhaseTag, predicate.PhaseLocalOrdinal,
                    checked((byte)nodeKind)));
            }

            var outputStates
                = ImmutableArray.CreateBuilder<
                    NativeFunctionPointerOutputState>();
            foreach (var outputState in tables.Contracts
                         .GetOutputStateContracts(methodDefRow))
            {
                if (!selectorOrdinalByGraphRow.TryGetValue(
                        outputState.PredicateGraphRow,
                        out var selectorOrdinal))
                {
                    throw Invalid(
                        "A Standard function-pointer output state has no selector.");
                }

                outputStates.Add(new NativeFunctionPointerOutputState(
                    checked((uint)outputState.ParameterOrdinal),
                    selectorOrdinal, outputState.State));
            }

            if (!tables.Contracts.GetNativeParameters(methodDefRow).IsEmpty)
            {
                throw Unsupported(
                    "Standard function-pointer anchors do not select NativeParameter contracts.");
            }

            signatures.Add(new NativeFunctionPointerSignatureDefinition(
                (NativeCallingConvention)convention, returnType,
                parameterTypes.MoveToImmutable()) {
                EffectsMask = tables.Contracts.GetEffectsMask(methodDefRow),
                Selectors = selectors.MoveToImmutable(),
                OutputStates = outputStates.MoveToImmutable(),
                Dependencies = [.. tables.Contracts
                    .GetDependencyContracts(methodDefRow)
                    .Select(static dependency =>
                        new NativeFunctionPointerDependency(
                            dependency.DestinationKind,
                            dependency.SourceKind,
                            dependency.Relationship,
                            dependency.Phase,
                            dependency.DestinationOrdinal,
                            dependency.SourceOrdinal))],
                NativeParameters = [],
            });
        }

        var sourceSignatures = signatures.MoveToImmutable();
        var sourcePointerTypes = nativePointerTypeDefinitions.ToArray();
        var signatureMapping = new int[sourceSignatures.Length];
        Array.Fill(signatureMapping, -1);
        var signatureStates = new byte[sourceSignatures.Length];
        var pointerMapping = new int[sourcePointerTypes.Length];
        Array.Fill(pointerMapping, -1);
        var pointerStates = new byte[sourcePointerTypes.Length];
        var canonicalSignatures
            = ImmutableArray.CreateBuilder<
                NativeFunctionPointerSignatureDefinition>();
        var canonicalPointers
            = ImmutableArray.CreateBuilder<NativePointerTypeDefinition>();
        var pointerIndexByDefinition
            = new Dictionary<NativePointerTypeDefinition, int>();
        var signatureIndexesByHash
            = new Dictionary<int, List<int>>();
        var remainingCanonicalizationWork
            = SemanticVerifier.MaximumAnalysisVisits;

        int SignatureHash(
            NativeFunctionPointerSignatureDefinition signature)
        {
            var hash = new HashCode();
            hash.Add(signature.CallingConvention);
            hash.Add(signature.ReturnType);
            hash.Add(signature.EffectsMask);
            foreach (var type in signature.ParameterTypes)
            {
                if (--remainingCanonicalizationWork < 0)
                {
                    throw new NotSupportedException(
                        "The Standard function-pointer signature normalization exceeds its analysis limit.");
                }
                hash.Add(type);
            }

            foreach (var selector in signature.Selectors
                .OrderBy(static selector => selector.PhaseTag)
                .ThenBy(static selector => selector.PhaseLocalOrdinal)
                .ThenBy(static selector => selector.NodeKind))
            {
                if (--remainingCanonicalizationWork < 0)
                {
                    throw new NotSupportedException(
                        "The Standard function-pointer signature normalization exceeds its analysis limit.");
                }
                hash.Add(selector);
            }

            foreach (var outputState in signature.OutputStates
                .OrderBy(static output => output.FormalOrdinal)
                .ThenBy(static output => output.SelectorOrdinal)
                .ThenBy(static output => output.State))
            {
                if (--remainingCanonicalizationWork < 0)
                {
                    throw new NotSupportedException(
                        "The Standard function-pointer signature normalization exceeds its analysis limit.");
                }
                hash.Add(outputState);
            }

            foreach (var dependency in signature.Dependencies
                .OrderBy(static dependency => dependency.DestinationKind)
                .ThenBy(static dependency => dependency.DestinationOrdinal)
                .ThenBy(static dependency => dependency.Phase)
                .ThenBy(static dependency => dependency.SourceKind)
                .ThenBy(static dependency => dependency.SourceOrdinal)
                .ThenBy(static dependency => dependency.Relationship))
            {
                if (--remainingCanonicalizationWork < 0)
                {
                    throw new NotSupportedException(
                        "The Standard function-pointer signature normalization exceeds its analysis limit.");
                }
                hash.Add(dependency);
            }

            foreach (var parameter in signature.NativeParameters
                .OrderBy(static parameter => parameter.ParameterIndex)
                .ThenBy(static parameter => parameter.PointeeType)
                .ThenBy(static parameter => parameter.Access)
                .ThenBy(static parameter => parameter.Retention)
                .ThenBy(static parameter => parameter.Extent))
            {
                if (--remainingCanonicalizationWork < 0)
                {
                    throw new NotSupportedException(
                        "The Standard function-pointer signature normalization exceeds its analysis limit.");
                }
                hash.Add(parameter.ParameterIndex);
                hash.Add(parameter.PointeeType);
                hash.Add(parameter.Access);
                hash.Add(parameter.Retention);
                hash.Add(parameter.Extent);
            }

            return hash.ToHashCode();
        }

        PrimitiveType RemapSignatureType(PrimitiveType type)
        {
            if (NativeFunctionPointerTypes.IsType(type))
            {
                return NativeFunctionPointerTypes.Type(
                    RemapSignature(NativeFunctionPointerTypes.Index(type)));
            }

            if (NativePointerTypes.IsType(type))
            {
                return NativePointerTypes.Type(
                    RemapPointer(NativePointerTypes.Index(type)));
            }

            return type;
        }

        int RemapPointer(int pointerIndex)
        {
            if ((uint)pointerIndex >= (uint)pointerMapping.Length)
            {
                throw Invalid(
                    "A Standard function-pointer signature has an invalid native-pointer type.");
            }
            if (pointerMapping[pointerIndex] >= 0)
            {
                return pointerMapping[pointerIndex];
            }
            if (pointerStates[pointerIndex] == 1
                || --remainingCanonicalizationWork < 0)
            {
                throw Invalid(
                    "A Standard function-pointer signature has a recursive native-pointer type.");
            }

            pointerStates[pointerIndex] = 1;
            var candidate = sourcePointerTypes[pointerIndex] with {
                PointeeType = RemapSignatureType(
                    sourcePointerTypes[pointerIndex].PointeeType),
            };
            if (!pointerIndexByDefinition.TryGetValue(candidate,
                    out var remappedIndex))
            {
                remappedIndex = canonicalPointers.Count;
                pointerIndexByDefinition.Add(candidate, remappedIndex);
                canonicalPointers.Add(candidate);
            }

            pointerMapping[pointerIndex] = remappedIndex;
            pointerStates[pointerIndex] = 2;
            return remappedIndex;
        }

        int RemapSignature(int signatureIndex)
        {
            if ((uint)signatureIndex >= (uint)sourceSignatures.Length)
            {
                throw Invalid(
                    "A Standard function-pointer signature references an invalid nested signature.");
            }
            if (signatureMapping[signatureIndex] >= 0)
            {
                return signatureMapping[signatureIndex];
            }
            if (signatureStates[signatureIndex] == 1
                || --remainingCanonicalizationWork < 0)
            {
                throw Invalid(
                    "A Standard function-pointer signature graph is recursive.");
            }

            signatureStates[signatureIndex] = 1;
            var source = sourceSignatures[signatureIndex];
            var candidate = source with {
                ReturnType = RemapSignatureType(source.ReturnType),
                ParameterTypes = [
                    .. source.ParameterTypes.Select(
                        RemapSignatureType),
                ],
                NativeParameters = [
                    .. source.NativeParameters.Select(parameter =>
                        parameter with {
                            PointeeType = RemapSignatureType(
                                parameter.PointeeType),
                        }),
                ],
            };
            var signatureHash = SignatureHash(candidate);
            var remappedIndex = -1;
            if (signatureIndexesByHash.TryGetValue(signatureHash,
                    out var candidates))
            {
                foreach (var candidateIndex in candidates)
                {
                    if (--remainingCanonicalizationWork < 0)
                    {
                        throw new NotSupportedException(
                            "The Standard function-pointer signature normalization exceeds its analysis limit.");
                    }

                    if (NativeFunctionPointerContract.AreEquivalent(
                            canonicalSignatures[candidateIndex],
                            candidate))
                    {
                        remappedIndex = candidateIndex;
                        break;
                    }
                }
            }

            if (remappedIndex < 0)
            {
                remappedIndex = canonicalSignatures.Count;
                canonicalSignatures.Add(candidate);
                if (candidates is null)
                {
                    candidates = [];
                    signatureIndexesByHash.Add(signatureHash, candidates);
                }
                candidates.Add(remappedIndex);
            }

            signatureMapping[signatureIndex] = remappedIndex;
            signatureStates[signatureIndex] = 2;
            return remappedIndex;
        }

        for (var signatureIndex = 0;
            signatureIndex < sourceSignatures.Length; signatureIndex++)
        {
            _ = RemapSignature(signatureIndex);
        }

        foreach (var typeSpecRow in Enumerable.Range(1,
            signatureIndexByTypeSpecRow.Length - 1))
        {
            var signatureIndex
                = signatureIndexByTypeSpecRow[typeSpecRow];
            if (signatureIndex >= 0)
            {
                signatureIndexByTypeSpecRow[typeSpecRow]
                    = signatureMapping[signatureIndex];
            }
        }

        nativePointerTypeDefinitions.Clear();
        nativePointerTypeDefinitions.AddRange(canonicalPointers);
        return canonicalSignatures.MoveToImmutable();
    }

    private static ImmutableArray<BoundStruct> LoadBlittableStructures(
        TrRmTypeDefTableReader typeDefs, TrRmFieldDefTableReader fields,
        TrRmLayoutTableReader layouts, TrRmLayoutFieldTableReader layoutFields,
        TrRmTargetProfileTableReader targetProfiles, TrRmTypeSpecTableReader typeSpecs,
        ImmutableArray<BoundStruct> optionalStructures,
        List<NativePointerTypeDefinition> nativePointerTypes,
        out int[] structureTypeByTypeDef,
        IReadOnlyList<BoundNativeFixedArrayType>? fixedArrayTypes = null,
        Dictionary<int, PrimitiveType>? arrayTypeByTypeSpecRow = null,
        HashSet<int>? selectedTypeDefRows = null,
        ImmutableArray<BoundStruct>? previousStructures = null,
        TrRmImportTableReader imports = default,
        HashSet<int>? includedTypeDefRows = null,
        bool allowDuplicateImportedViews = false,
        IReadOnlyDictionary<uint, PrimitiveType>?
            nominalClassTypesByTypeSpecReference = null,
        IReadOnlyDictionary<int, int>?
            existingStructureTypeIndexesByTypeDef = null,
        int[]? functionPointerSignatureIndexByTypeSpecRow = null,
        Action<int[]>? beforeFieldLoad = null,
        bool deferLayoutValidation = false,
        IReadOnlySet<int>? hiddenTypeDefRows = null)
    {
        fixedArrayTypes ??= ImmutableArray<BoundNativeFixedArrayType>.Empty;
        if (!targetProfiles.TryGetProfile(1, 1, out var windowsX64ProfileRow))
        {
            var hasBlittableVariant = false;
            for (var type = 1; type <= typeDefs.Count; type++)
            {
                hasBlittableVariant |= layouts.HasBlittableVariant(type);
            }
            if (hasBlittableVariant)
            {
                throw Invalid("The artifact does not contain the assigned Windows x64 TargetProfile.");
            }
        }

        var structureRows = new List<int>();
        structureTypeByTypeDef = new int[typeDefs.Count + 1];
        Array.Fill(structureTypeByTypeDef, -1);
        for (var row = 1; row <= typeDefs.Count; row++)
        {
            if ((includedTypeDefRows is not null
                    && !includedTypeDefRows.Contains(row))
                || !layouts.HasBlittableLayout(row, 1, 1))
            {
                continue;
            }

            if (existingStructureTypeIndexesByTypeDef is not null
                && existingStructureTypeIndexesByTypeDef.TryGetValue(row,
                    out var existingStructureIndex))
            {
                if ((uint)existingStructureIndex
                    >= (uint)optionalStructures.Length)
                {
                    throw Invalid(
                        "A prior native Blittable-layout TypeDef map is inconsistent.");
                }

                structureTypeByTypeDef[row] = existingStructureIndex;
                continue;
            }

            structureTypeByTypeDef[row] = optionalStructures.Length + structureRows.Count;
            structureRows.Add(row);
        }

        if (previousStructures is { } previousState
            && (previousState.IsDefault
                || previousState.Length
                    != optionalStructures.Length + structureRows.Count))
        {
            throw Invalid("A prior native Blittable-layout state is inconsistent.");
        }

        var structures = optionalStructures.ToList();
        foreach (var row in structureRows)
        {
            var flags = typeDefs.GetFlags(row);
            var layoutRow = layouts.GetLayoutForProfile(row, checked((uint)windowsX64ProfileRow));
            var layoutKind = layouts.GetLayoutKind(layoutRow);
            var pack = layouts.GetPack(layoutRow);
            var alignment = layouts.GetAlignment(layoutRow);
            if (layouts.GetTargetProfileRef(layoutRow) != windowsX64ProfileRow
                || layoutKind is not (1 or 2 or 3)
                || layouts.GetFlags(layoutRow) != 1
                || (layoutKind is 1 or 2
                    ? pack is not (1 or 2 or 4 or 8 or 16)
                    : pack != 0 || alignment != 0))
            {
                throw Invalid("A native Blittable Layout uses an unsupported Windows x64 variant.");
            }

            var typeNamespace = typeDefs.GetNamespace(row);
            var name = typeNamespace.Length == 0
                ? typeDefs.GetName(row) : typeNamespace + "." + typeDefs.GetName(row);
            var structureIndex = structureTypeByTypeDef[row];
            if (previousStructures is { } previous
                && (uint)structureIndex < (uint)previous.Length
                && previous[structureIndex].Size > 0)
            {
                structures.Add(previous[structureIndex]);
                continue;
            }

            structures.Add(new BoundStruct(name, [], 0) {
                IsSequential = layoutKind == 1,
                IsTransparent = layoutKind == 3,
                BlittableTargetRequirement = selectedTypeDefRows is null
                    || selectedTypeDefRows.Contains(row)
                    ? FixedLayoutStorage.WindowsX64TargetRequirement : 0,
                Packing = layoutKind is 1 or 2 ? checked((int)pack) : 0,
                DeclaredAlignment = alignment == 0 ? null
                    : checked((int)alignment),
                IsPrivate = (flags & 3) == 0,
                IsInternal = (flags & 3) == 1,
            });
        }

        beforeFieldLoad?.Invoke(structureTypeByTypeDef);

        foreach (var row in structureRows)
        {
            if (selectedTypeDefRows is not null
                && !selectedTypeDefRows.Contains(row))
            {
                continue;
            }

            var structureIndex = structureTypeByTypeDef[row];
            if (structures[structureIndex].Size > 0)
            {
                continue;
            }

            var layoutRow = layouts.GetLayoutForProfile(row, checked((uint)windowsX64ProfileRow));
            var (firstField, fieldCount) = layouts.GetLayoutFieldRange(layoutRow);
            if (fieldCount == 0)
            {
                throw Invalid("A native Blittable TypeDef must have fields.");
            }

            var structureFields = ImmutableArray.CreateBuilder<BoundStructField>(fieldCount);
            for (var ordinal = 0; ordinal < fieldCount; ordinal++)
            {
                var fieldRow = layoutFields.GetFieldDef(firstField + ordinal);
                var fieldTypeSpecRef = fields.GetTypeSpecRef(fieldRow);
                var fieldTypeReference = typeSpecs.GetTypeSpecRef(
                    fieldTypeSpecRef);
                PrimitiveType fieldType;
                if (fieldTypeReference.IsRow
                    && fieldTypeReference.Form == 0x11)
                {
                    if (arrayTypeByTypeSpecRow is null
                        || !arrayTypeByTypeSpecRow.TryGetValue(
                            fieldTypeReference.Row, out fieldType))
                    {
                        throw Invalid("A native Blittable field has an unmapped fixed-array TypeSpec.");
                    }
                }
                else
                {
                    fieldType = FromNativeValueRow(typeSpecs,
                        fieldTypeSpecRef, nativePointerTypes,
                        structureTypeByTypeDef, imports,
                        nominalClassTypesByTypeSpecReference:
                            nominalClassTypesByTypeSpecReference,
                        functionPointerSignatureIndexByAnchorTypeSpec:
                            functionPointerSignatureIndexByTypeSpecRow);
                }
                var fieldFlags = fields.GetFlags(fieldRow);
                structureFields.Add(new BoundStructField(fields.GetName(fieldRow),
                    fieldType, 0, 0) {
                    IsPrivate = (fieldFlags & 3) == 0,
                    IsInternal = (fieldFlags & 3) == 1,
                    IsMutable = (fieldFlags & 4) != 0,
                });
            }

            structures[structureIndex] = structures[structureIndex] with {
                Fields = structureFields.MoveToImmutable(),
            };
        }

        HashSet<int>? duplicateNameViewIndexes = null;
        if (allowDuplicateImportedViews)
        {
            duplicateNameViewIndexes = [];
            var duplicateNames = new HashSet<string>(
                StringComparer.Ordinal);
            for (var row = 1; row <= typeDefs.Count; row++)
            {
                if ((includedTypeDefRows is null
                        || includedTypeDefRows.Contains(row))
                    && structureTypeByTypeDef[row] >= 0
                    && (imports.IsExpectedDefinition(
                            0x1300_0000u | checked((uint)row))
                        || hiddenTypeDefRows?.Contains(row) == true))
                {
                    var structureIndex = structureTypeByTypeDef[row];
                    _ = duplicateNames.Add(structures[structureIndex].Name);
                }
            }

            if (duplicateNames.Count != 0)
            {
                for (var typeDefRow = 1;
                    typeDefRow <= typeDefs.Count; typeDefRow++)
                {
                    if ((includedTypeDefRows is null
                            || includedTypeDefRows.Contains(typeDefRow))
                        && structureTypeByTypeDef[typeDefRow] >= 0
                        && duplicateNames.Contains(
                            structures[structureTypeByTypeDef[
                                typeDefRow]].Name))
                    {
                        _ = duplicateNameViewIndexes.Add(
                            structureTypeByTypeDef[typeDefRow]);
                    }
                }
            }
        }

        if (deferLayoutValidation)
        {
            return [.. structures];
        }

        try
        {
            var result = FixedLayoutStorage.Materialize([.. structures],
                fixedArrayTypes, duplicateNameViewIndexes);
            _ = FixedLayoutStorage.Validate(result, fixedArrayTypes,
                duplicateNameViewIndexes);
            return result;
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            throw new InvalidDataException(
                $"TRRM native Blittable-layout validation failed: {exception.Message}", exception);
        }
    }

    private static bool IsSupportedScalarInitOutputType(
        PrimitiveType type, uint ruleRevision,
        List<CheckedReferenceTypeDefinition> checkedReferenceTypes)
    {
        if (ruleRevision == 0
            || !CheckedReferenceTypeContract.IsValidType(type,
                checkedReferenceTypes))
        {
            return false;
        }

        var reference = checkedReferenceTypes[
            CheckedReferenceTypes.Index(type)];
        return reference.IsInitialization
            && !reference.IsGarbageCollected
            && !reference.IsOptional
            && IsOutputScalarType(reference.ReferentType)
            && (ruleRevision
                    != TrRmDevelopmentFeatureTableReader
                        .ScalarInitOutputInitialRuleRevision
                || reference.ReferentType == PrimitiveType.Int32);
    }

    private static PrimitiveType FromNativeValueRow(TrRmTypeSpecTableReader types,
        uint typeSpecRef, List<NativePointerTypeDefinition> nativePointerTypes,
        int[] structureTypeByTypeDef,
        TrRmImportTableReader imports = default,
        List<CheckedReferenceTypeDefinition>? checkedReferenceTypeDefinitions = null,
        Dictionary<CheckedReferenceTypeDefinition, int>?
            checkedReferenceTypeIndexes = null,
        IReadOnlyDictionary<uint, PrimitiveType>?
            nominalClassTypesByTypeSpecReference = null,
        int[]? functionPointerSignatureIndexByAnchorTypeSpec = null)
    {
        if (nominalClassTypesByTypeSpecReference is not null
            && nominalClassTypesByTypeSpecReference.TryGetValue(
                typeSpecRef, out var nominalClassType))
        {
            return nominalClassType;
        }

        var typeSpecReference = types.GetTypeSpecRef(typeSpecRef);
        if (typeSpecReference.IsRow && typeSpecReference.Form == 0x0D)
        {
            if (functionPointerSignatureIndexByAnchorTypeSpec is null
                || (uint)typeSpecReference.Row
                    >= (uint)functionPointerSignatureIndexByAnchorTypeSpec.Length
                || functionPointerSignatureIndexByAnchorTypeSpec[
                    typeSpecReference.Row] < 0)
            {
                throw Invalid(
                    "A native Blittable field has an unmapped function-pointer anchor.");
            }

            return NativeFunctionPointerTypes.Type(
                functionPointerSignatureIndexByAnchorTypeSpec[
                    typeSpecReference.Row]);
        }

        var type = types.GetTypeSpecBytes(typeSpecRef);
        if (type.Length == 4 && type[0] == 0x01 && type[2] == 0x8C)
        {
            var pointeeIndex = Array.IndexOf(s_codes, type[3]);
            if (pointeeIndex < 0)
            {
                throw Invalid("Unsupported native pointer pointee TypeSpec.");
            }
            return s_types[pointeeIndex] == PrimitiveType.Void && type[1] == 0
                ? PrimitiveType.NativeRawPointer
                : GetNativePointerType(nativePointerTypes, s_types[pointeeIndex],
                    type[1] != 0);
        }

        if (type.Length == 6 && type[0] == 0x08)
        {
            var pointeeRef = BinaryPrimitives.ReadUInt32LittleEndian(
                type[2..]);
            var pointeeType = FromNativeValueRow(types, pointeeRef,
                nativePointerTypes, structureTypeByTypeDef, imports,
                checkedReferenceTypeDefinitions,
                checkedReferenceTypeIndexes,
                nominalClassTypesByTypeSpecReference,
                functionPointerSignatureIndexByAnchorTypeSpec);
            return pointeeType == PrimitiveType.Void && type[1] == 0
                ? PrimitiveType.NativeRawPointer
                : GetNativePointerType(nativePointerTypes, pointeeType,
                    type[1] != 0);
        }

        if (type.Length == 7 && type[0] == 0x0C && type[2] == 0x8C)
        {
            var pointeeRef = BinaryPrimitives.ReadUInt32LittleEndian(type[3..]);
            var pointeeType = FromNativeValueRow(types, pointeeRef,
                nativePointerTypes, structureTypeByTypeDef, imports,
                checkedReferenceTypeDefinitions,
                checkedReferenceTypeIndexes,
                nominalClassTypesByTypeSpecReference,
                functionPointerSignatureIndexByAnchorTypeSpec);
            return pointeeType == PrimitiveType.Void && type[1] == 0
                ? PrimitiveType.NativeRawPointer
                : GetNativePointerType(nativePointerTypes, pointeeType, type[1] != 0);
        }

        if (type.Length == 6 && type[0] is 0x07 or 0x0E)
        {
            if (checkedReferenceTypeDefinitions is null
                || checkedReferenceTypeIndexes is null)
            {
                throw Invalid(
                    "A checked-reference TypeSpec has no executable type context.");
            }

            var referentTypeSpec
                = BinaryPrimitives.ReadUInt32LittleEndian(type[2..]);
            var referentTypeSpecReference = types.GetTypeSpecRef(
                referentTypeSpec);
            var referentType = FromNativeValueRow(types, referentTypeSpec,
                nativePointerTypes, structureTypeByTypeDef, imports,
                checkedReferenceTypeDefinitions,
                checkedReferenceTypeIndexes,
                nominalClassTypesByTypeSpecReference,
                functionPointerSignatureIndexByAnchorTypeSpec);
            var isInitialization = type[0] == 0x0E;
            var qualifiers = type[1];
            var definition = new CheckedReferenceTypeDefinition(
                referentType, (qualifiers & 1) != 0,
                !isInitialization && (qualifiers & 2) != 0,
                isInitialization, IsOptional: false,
                IsReferentWritable:
                    referentTypeSpecReference.Form == 0x04
                    && (referentTypeSpecReference.Qualifiers & 0x01) != 0);
            return GetOrAddCheckedReferenceType(
                definition, checkedReferenceTypeDefinitions,
                checkedReferenceTypeIndexes);
        }

        if (type.Length == 6 && type[0] == 0x02 && type[1] == 0)
        {
            var typeDefRow = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(type[2..]));
            if ((uint)typeDefRow < (uint)structureTypeByTypeDef.Length
                && structureTypeByTypeDef[typeDefRow] >= 0)
            {
                return (PrimitiveType)(100 + structureTypeByTypeDef[typeDefRow]);
            }

            throw Invalid("A native pointer refers to an unrepresented structure type.");
        }

        if (type.Length == 6 && type[0] == 0x03 && type[1] == 0)
        {
            if (imports.Count == 0)
            {
                throw Invalid(
                    "A native value references an Import without a selected provider context.");
            }

            var importRow = checked((int)
                BinaryPrimitives.ReadUInt32LittleEndian(type[2..]));
            var definition
                = imports.GetExpectedDefinitionForImportRow(importRow);
            if (definition >> 24 != 0x13)
            {
                throw Invalid("A native value Import is not a TypeDef.");
            }

            var typeDefRow = checked((int)(definition & 0x00FF_FFFF));
            if ((uint)typeDefRow < (uint)structureTypeByTypeDef.Length
                && structureTypeByTypeDef[typeDefRow] >= 0)
            {
                return (PrimitiveType)(100
                    + structureTypeByTypeDef[typeDefRow]);
            }

            throw Invalid("An imported TypeDef has no represented local expectation.");
        }

        if (type.Length == 6 && type[0] == 0x0D && type[1] == 0)
        {
            var signatureRow = BinaryPrimitives.ReadUInt32LittleEndian(type[2..]);
            if (signatureRow == 0 || signatureRow > types.NativeFunctionPointerSignatureCount)
            {
                throw Invalid("Invalid native function-pointer TypeSpec.");
            }

            return NativeFunctionPointerTypes.Type(checked((int)signatureRow - 1));
        }

        return FromRow(types, typeSpecRef);
    }

    private static PrimitiveType GetOrAddCheckedReferenceType(
        CheckedReferenceTypeDefinition definition,
        List<CheckedReferenceTypeDefinition> definitions,
        Dictionary<CheckedReferenceTypeDefinition, int> indexes)
    {
        if (indexes.TryGetValue(definition, out var existingIndex))
        {
            return CheckedReferenceTypes.Type(existingIndex);
        }

        if (definitions.Count >= SemanticVerifier.MaximumAnalysisVisits)
        {
            throw Invalid("The checked-reference type table exceeds the analysis limit.");
        }

        var index = definitions.Count;
        definitions.Add(definition);
        indexes.Add(definition, index);
        return CheckedReferenceTypes.Type(index);
    }

    private static PrimitiveType FromNativeValueRow(TrRmTypeSpecTableReader types,
        int row, List<NativePointerTypeDefinition> nativePointerTypes,
        int[] structureTypeByTypeDef,
        TrRmImportTableReader imports = default,
        IReadOnlyDictionary<uint, PrimitiveType>?
            nominalClassTypesByTypeSpecReference = null)
        => FromNativeValueRow(types, checked((uint)row), nativePointerTypes,
            structureTypeByTypeDef, imports,
            nominalClassTypesByTypeSpecReference:
                nominalClassTypesByTypeSpecReference);

    private static PrimitiveType GetNativePointerType(
        List<NativePointerTypeDefinition> nativePointerTypes,
        PrimitiveType pointeeType, bool isWritable)
    {
        if (pointeeType == PrimitiveType.Void && !isWritable)
        {
            return PrimitiveType.NativeRawPointer;
        }

        for (var index = 0; index < nativePointerTypes.Count; index++)
        {
            var existing = nativePointerTypes[index];
            if (existing.PointeeType == pointeeType && existing.IsWritable == isWritable)
            {
                return NativePointerTypes.Type(index);
            }
        }

        if (nativePointerTypes.Count >= SemanticVerifier.MaximumAnalysisVisits)
        {
            throw Invalid("The native pointer type table exceeds the analysis limit.");
        }

        var type = NativePointerTypes.Type(nativePointerTypes.Count);
        nativePointerTypes.Add(new NativePointerTypeDefinition(pointeeType, isWritable));
        return type;
    }

    private static PrimitiveType NativeParameterPointee(TrRmTypeSpecTableReader typeSpecs,
        uint typeSpecRef, byte access,
        List<NativePointerTypeDefinition> nativePointerTypes,
        TrRmLayoutTableReader layouts,
        int[] structureTypeByTypeDef,
        TrRmTypeDefTableReader typeDefs = default,
        TrRmImportTableReader imports = default,
        bool hasImportContext = false)
    {
        var isValidPointee = hasImportContext
            ? TrRmNativeImportTypeContract.TryGetNativeParameterPointee(
                typeSpecs, typeSpecRef, access, layouts, typeDefs, imports,
                out _)
            : TrRmNativeImportTypeContract.TryGetNativeParameterPointee(
                typeSpecs, typeSpecRef, access, layouts, out _);
        if (!isValidPointee)
        {
            throw Invalid("Invalid native parameter pointee TypeSpec.");
        }

        var formalType = FromNativeValueRow(typeSpecs, typeSpecRef,
            nativePointerTypes, structureTypeByTypeDef, imports);
        if (formalType == PrimitiveType.NativeRawPointer)
        {
            return PrimitiveType.Void;
        }

        if (!NativePointerTypes.IsType(formalType))
        {
            throw Invalid("NativeParameter formal is not a native pointer TypeSpec.");
        }

        var pointerIndex = NativePointerTypes.Index(formalType);
        if ((uint)pointerIndex >= (uint)nativePointerTypes.Count)
        {
            throw Invalid("NativeParameter formal has no native pointer definition.");
        }

        return nativePointerTypes[pointerIndex].PointeeType;
    }

    private static PrimitiveType BodyTypeFromRow(TrRmTypeSpecTableReader types, int row)
    {
        var typeSpec = types.GetRow(row);
        if (typeSpec.Length != 6 || typeSpec[0] != 0x06)
        {
            if (typeSpec.Length == 4 && typeSpec[0] == 0x01
                && typeSpec[1] == 0 && typeSpec[2] == 0x8C)
            {
                return PrimitiveType.NativeRawPointer;
            }

            return FromNativeValueRow(types, row);
        }

        var resolved = TrRmCompactLinearBodyReader.ResolveType(types, row);
        var payloadCode = (byte)(resolved & 0xFF);
        var payloadIndex = Array.IndexOf(s_codes, payloadCode);
        return payloadIndex > 0
            ? TrRmOptionalScalar.StorageType(s_types[payloadIndex])
            : throw Invalid("Unsupported optional scalar payload.");
    }

    private static PrimitiveType BodyTypeFromRow(TrRmTypeSpecTableReader types, int row,
        List<NativePointerTypeDefinition> nativePointerTypeDefinitions,
        int[] structureTypeByTypeDef)
    {
        var typeSpec = types.GetRow(row);
        if (typeSpec.Length == 6 && typeSpec[0] == 0x06)
        {
            var resolved = TrRmCompactLinearBodyReader.ResolveType(types, row);
            var payloadCode = (byte)(resolved & 0xFF);
            var payloadIndex = Array.IndexOf(s_codes, payloadCode);
            return payloadIndex > 0
                ? TrRmOptionalScalar.StorageType(s_types[payloadIndex])
                : throw Invalid("Unsupported optional scalar payload.");
        }

        return FromNativeValueRow(types, row, nativePointerTypeDefinitions,
            structureTypeByTypeDef);
    }

    private static PrimitiveType FromRow(TrRmTypeSpecTableReader types, uint typeSpecRef)
    {
        var bytes = types.GetTypeSpecBytes(typeSpecRef);
        if (bytes.Length != 3 || bytes[0] != 1 || bytes[1] != 0)
        {
            throw Invalid("Unsupported qualified or constructed TypeSpec.");
        }
        return TrRmConstantSourceValueValidator.TryGetBoundScalarType(
                bytes[2], out var type)
            ? type
            : throw Invalid("Unsupported scalar TypeSpec.");
    }

    private static PrimitiveType FromRow(TrRmTypeSpecTableReader types, int row)
        => FromRow(types, checked((uint)row));

    private static PrimitiveType FromNativeValueRow(TrRmTypeSpecTableReader types,
        uint typeSpecRef)
    {
        var bytes = types.GetTypeSpecBytes(typeSpecRef);
        if (bytes.Length == 4 && bytes[0] == 1 && bytes[1] == 0
            && bytes[2] == 0x8C && bytes[3] <= 0x19)
        {
            return PrimitiveType.NativeRawPointer;
        }
        if (bytes.Length == 6 && bytes[0] == 0x0D && bytes[1] == 0)
        {
            var signatureRow = BinaryPrimitives.ReadUInt32LittleEndian(bytes[2..]);
            if (signatureRow == 0 || signatureRow > types.NativeFunctionPointerSignatureCount)
            {
                throw Invalid("Invalid native function-pointer TypeSpec.");
            }
            return NativeFunctionPointerTypes.Type(checked((int)signatureRow - 1));
        }

        return FromRow(types, typeSpecRef);
    }

    private static PrimitiveType FromNativeValueRow(TrRmTypeSpecTableReader types, int row)
        => FromNativeValueRow(types, checked((uint)row));
    private static NotSupportedException Unsupported(string reason) => new(reason);
    private static InvalidDataException Invalid(string reason) => new($"Invalid or unsupported executable TRRM: {reason}");
}
