// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;

namespace Terrum.CodeAnalysis.Modules;

/// <summary>An exact target-profile identity and rule revision.</summary>
/// <param name="ProfileId">The nonzero profile identity.</param>
/// <param name="RuleRevision">The nonzero layout-rule revision.</param>
public readonly record struct TrRmTargetProfileRow(uint ProfileId, uint RuleRevision);

/// <summary>Validates fixed-width target-profile identities without selecting host policy.</summary>
/// <remarks>The borrowed section must remain unchanged while this reader is in use.</remarks>
public readonly ref struct TrRmTargetProfileTableReader
{
    internal const uint WindowsX64NativeProfileId = 1;
    internal const uint WindowsX64NativeProfileRuleRevision = 1;
    internal const uint WindowsX64RootPinProfileId = 2;
    internal const uint WindowsX64RootPinProfileRuleRevision = 1;
    private const int RowSize = 8;
    private readonly ReadOnlySpan<byte> _rows;
    private readonly TrRmArtifactSchema _schema;
    private readonly TrRmA3LayoutResourceLedger? _resourceLedger;

    /// <summary>Gets the number of validated TargetProfile rows.</summary>
    public int Count { get; }

    /// <summary>Validates the fixed-width TargetProfile rows.</summary>
    /// <param name="section">The borrowed TargetProfile section bytes.</param>
    /// <param name="recordCount">The number of eight-byte TargetProfile rows.</param>
    public TrRmTargetProfileTableReader(ReadOnlySpan<byte> section, int recordCount)
        : this(section, recordCount, TrRmArtifactSchema.Standard)
    {
    }

    internal TrRmTargetProfileTableReader(ReadOnlySpan<byte> section,
        int recordCount, TrRmArtifactSchema schema,
        TrRmA3LayoutResourceLedger? resourceLedger = null)
    {
        resourceLedger?.ConsumeWork(3);
        if (recordCount is < 0 or > 0xFF_FFFF
            || (long)recordCount * RowSize != section.Length
            || schema is not (TrRmArtifactSchema.Standard
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
                or TrRmArtifactSchema.ForeignInvocationStorageFixedArrayGenericCopyDraft
                or TrRmArtifactSchema.ScalarFrozenValuesDraft
                or TrRmArtifactSchema.NominalEnumsDraft
                or TrRmArtifactSchema.RootPinRuntimeDraft
                or TrRmArtifactSchema.GeneralPredicateGraphsDraft
                or TrRmArtifactSchema.RegisterExceptionRegionsDraft))
        {
            throw InvalidProfile();
        }

        _rows = section;
        _schema = schema;
        _resourceLedger = resourceLedger;
        Count = recordCount;
        _resourceLedger?.ConsumeWork();
        var profiles = new HashSet<(uint ProfileId, uint RuleRevision)>();
        for (var row = 0; row < recordCount; row++)
        {
            _resourceLedger?.ConsumeWork();
            var profileId = Read(row, 0);
            var ruleRevision = Read(row, 4);
            _resourceLedger?.ConsumeWork(2);
            if (profileId == 0 || ruleRevision == 0
                || profiles.Contains((profileId, ruleRevision)))
            {
                throw InvalidProfile();
            }
            _resourceLedger?.ConsumeWork();
            _ = profiles.Add((profileId, ruleRevision));
        }
    }

    /// <summary>Gets a validated TargetProfile row by its one-based row number.</summary>
    public TrRmTargetProfileRow GetProfile(int row)
    {
        _resourceLedger?.ConsumeWork();
        if (row <= 0 || row > Count)
        {
            throw InvalidProfile();
        }

        return new TrRmTargetProfileRow(Read(row - 1, 0), Read(row - 1, 4));
    }

    /// <summary>Finds an exact profile identity and revision.</summary>
    /// <param name="profileId">The profile identity.</param>
    /// <param name="ruleRevision">The exact rule revision.</param>
    /// <param name="row">Receives the one-based matching row, or zero when absent.</param>
    public bool TryGetProfile(uint profileId, uint ruleRevision, out int row)
    {
        for (var candidate = 1; candidate <= Count; candidate++)
        {
            _resourceLedger?.ConsumeWork();
            var profile = GetProfile(candidate);
            _resourceLedger?.ConsumeWork(2);
            if (profile.ProfileId == profileId && profile.RuleRevision == ruleRevision
                && IsAssigned(profileId, ruleRevision, _schema))
            {
                row = candidate;
                return true;
            }
        }

        row = 0;
        return false;
    }

    internal static bool IsAssigned(uint profileId, uint ruleRevision)
        => profileId == WindowsX64NativeProfileId
            && ruleRevision == WindowsX64NativeProfileRuleRevision;

    internal static bool IsAssigned(uint profileId, uint ruleRevision,
        TrRmArtifactSchema schema)
        => IsAssigned(profileId, ruleRevision)
            || (schema.UsesRootPinRuntime()
                && profileId == WindowsX64RootPinProfileId
                && ruleRevision == WindowsX64RootPinProfileRuleRevision);

    internal bool IsAssignedProfileReference(uint profileRow)
    {
        _resourceLedger?.ConsumeWork(2);
        if (profileRow == 0 || profileRow > (uint)Count)
        {
            return false;
        }

        var profile = GetProfile(checked((int)profileRow));
        return IsAssigned(profile.ProfileId, profile.RuleRevision, _schema);
    }

    private uint Read(int row, int offset)
    {
        _resourceLedger?.ConsumeWork(sizeof(uint) + 1);
        return BinaryPrimitives.ReadUInt32LittleEndian(
            _rows.Slice((row * RowSize) + offset, sizeof(uint)));
    }

    private static InvalidDataException InvalidProfile()
        => new("Invalid TRRM TargetProfile table.");
}
