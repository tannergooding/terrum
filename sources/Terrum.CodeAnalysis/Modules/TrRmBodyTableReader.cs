// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;

namespace Terrum.CodeAnalysis.Modules;

/// <summary>Validated index over one present TRRM Body section; instruction bytes remain opaque.</summary>
/// <remarks>The borrowed section bytes must remain unchanged while this reader is in use.</remarks>
public readonly ref struct TrRmBodyTableReader
{
    private readonly TrRmVariableTableIndex _rows;
    private readonly TrRmArtifactSchema _schema;
    private readonly TrRmA3LayoutResourceLedger? _resourceLedger;
    private readonly bool _usesNativePointerLoads;

    /// <summary>Gets the number of validated Body rows.</summary>
    public int Count => _rows.Count;

    /// <summary>Validates and indexes the borrowed Body section.</summary>
    /// <param name="section">The Body section bytes, which must remain unchanged while this reader is in use.</param>
    /// <param name="recordCount">The number of Body rows.</param>
    public TrRmBodyTableReader(ReadOnlySpan<byte> section, int recordCount)
        : this(section, recordCount, TrRmArtifactSchema.Standard)
    {
    }

    internal TrRmBodyTableReader(ReadOnlySpan<byte> section, int recordCount,
        TrRmArtifactSchema schema,
        TrRmA3LayoutResourceLedger? resourceLedger = null,
        bool usesNativePointerLoads = false)
    {
        if (schema is not (TrRmArtifactSchema.Standard
            or TrRmArtifactSchema.InitializationReferenceLocalFieldLayoutDraft
            or TrRmArtifactSchema.InitializationReferenceLocalFieldLayoutNestedFieldDestinationsDraft
            or TrRmArtifactSchema.Development
            or TrRmArtifactSchema.DevelopmentFunctionPointerEffectsDraft
            or TrRmArtifactSchema.DevelopmentNativeConventionsDraft
            or TrRmArtifactSchema.CopyOriginDraft
            or TrRmArtifactSchema.ForeignInvocationDraft
            or TrRmArtifactSchema.ForeignNativeInterfaceDraft
            or TrRmArtifactSchema.ForeignNativeInterfaceParameterSurfaceDraft
            or TrRmArtifactSchema.ForeignNativeInterfaceParameterSurfaceEnumClosureDraft
            or TrRmArtifactSchema.ForeignNativeInterfaceAggregateOutputConsumptionDraft
            or TrRmArtifactSchema.ForeignInvocationStorageAddressDraft
            or TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyDraft
            or TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyEmptyInitializationDraft
            or TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyEmptyUnitInitializationDraft
            or TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyEmptyUnitScalarFrozenValuesDraft
            or TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyReturnCoverageDraft
            or TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyReturnCoverageRepeatInitializationDraft
            or TrRmArtifactSchema.LocalAggregateLayoutDraft
            or TrRmArtifactSchema.ForeignInvocationStorageFixedArrayGenericCopyDraft
            or TrRmArtifactSchema.ScalarFrozenValuesDraft
            or TrRmArtifactSchema.NominalEnumsDraft
            or TrRmArtifactSchema.RootPinRuntimeDraft
            or TrRmArtifactSchema.GeneralPredicateGraphsDraft
            or TrRmArtifactSchema.InitializationReferencePointerLoadNativeParameterDraft
            or TrRmArtifactSchema.InitializationReferenceGeneralPredicatePointerLoadNativeParameterDraft
            or TrRmArtifactSchema.RegisterExceptionRegionsDraft
            or TrRmArtifactSchema.GenericMethodCallsDraft
            or TrRmArtifactSchema.GenericMethodCallsParameterLabelsDraft
            or TrRmArtifactSchema.GenericMethodCallsParameterLabelsReceiverAccessDraft
            or TrRmArtifactSchema.C1Draft
            or TrRmArtifactSchema.C1ParameterLabelsReceiverAccessDraft
            or TrRmArtifactSchema.C1M1Draft
            or TrRmArtifactSchema.C1M1ParameterLabelsReceiverAccessDraft
            or TrRmArtifactSchema.C1M1ConstructionDraft
            or TrRmArtifactSchema.C1M1ConstructionParameterLabelsReceiverAccessDraft
            or TrRmArtifactSchema.C1M1Construction2Draft
            or TrRmArtifactSchema.C1M1Construction2ParameterLabelsReceiverAccessDraft
            or TrRmArtifactSchema.C1M1Construction2InitializerCopyEvidenceDraft
            or TrRmArtifactSchema.C1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsDraft
            or TrRmArtifactSchema.C1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsInitializerCopyEvidenceDraft
            or TrRmArtifactSchema.C1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsAssociatedRelationConstraintsDraft
            or TrRmArtifactSchema.C1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsAssociatedRelationConstraintsInitializerCopyEvidenceDraft
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
            or TrRmArtifactSchema.GenericConstantMethodsDraft))
        {
            throw new ArgumentOutOfRangeException(nameof(schema));
        }

        _rows = new TrRmVariableTableIndex(section, recordCount, 1, 1,
            resourceLedger);
        _schema = schema;
        _resourceLedger = resourceLedger;
        _usesNativePointerLoads = usesNativePointerLoads;
    }

    /// <summary>Gets the borrowed bytes of a one-based Body row without interpreting its instructions.</summary>
    /// <param name="row">The one-based row number.</param>
    /// <returns>The row's bytes.</returns>
    public ReadOnlySpan<byte> GetRow(int row)
    {
        _resourceLedger?.ConsumeWork();
        return _rows.GetRow(row);
    }

    internal TrRmRegisterBodyReader GetRegisterBody(int row,
        int contractCount = 0,
        bool generalPredicateGraphsInitializationReferenceBodies = false)
    {
        _resourceLedger?.ConsumeWork();
        return new(_rows.GetRow(row), contractCount, _schema,
            _resourceLedger,
            generalPredicateGraphsInitializationReferenceBodies,
            _usesNativePointerLoads);
    }

    internal void ValidateSelectedImplementation(int row, int contractCount)
    {
        _resourceLedger?.ConsumeWork();
        TrRmBodyImplementationReader.Validate(_rows.GetRow(row),
            contractCount, _schema, _resourceLedger);
    }
}
