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

    public int Count => _rows.Count;

    public TrRmBodyTableReader(ReadOnlySpan<byte> section, int recordCount)
        : this(section, recordCount, TrRmArtifactSchema.Standard)
    {
    }

    internal TrRmBodyTableReader(ReadOnlySpan<byte> section, int recordCount,
        TrRmArtifactSchema schema,
        TrRmA3LayoutResourceLedger? resourceLedger = null)
    {
        if (schema is not (TrRmArtifactSchema.Standard
            or TrRmArtifactSchema.Development
            or TrRmArtifactSchema.DevelopmentFunctionPointerEffectsDraft
            or TrRmArtifactSchema.CopyOriginDraft
            or TrRmArtifactSchema.ForeignInvocationDraft
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
            or TrRmArtifactSchema.RegisterExceptionRegionsDraft))
        {
            throw new ArgumentOutOfRangeException(nameof(schema));
        }

        _rows = new TrRmVariableTableIndex(section, recordCount, 1, 1,
            resourceLedger);
        _schema = schema;
        _resourceLedger = resourceLedger;
    }

    public ReadOnlySpan<byte> GetRow(int row)
    {
        _resourceLedger?.ConsumeWork();
        return _rows.GetRow(row);
    }

    internal TrRmRegisterBodyReader GetRegisterBody(int row, int contractCount = 0)
    {
        _resourceLedger?.ConsumeWork();
        return new(_rows.GetRow(row), contractCount, _schema,
            _resourceLedger);
    }

    internal void ValidateSelectedImplementation(int row, int contractCount)
    {
        _resourceLedger?.ConsumeWork();
        TrRmBodyImplementationReader.Validate(_rows.GetRow(row),
            contractCount, _schema, _resourceLedger);
    }
}
