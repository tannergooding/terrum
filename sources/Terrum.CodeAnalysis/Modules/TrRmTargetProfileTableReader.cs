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
    private const int RowSize = 8;
    private readonly ReadOnlySpan<byte> _rows;

    /// <summary>Gets the number of validated TargetProfile rows.</summary>
    public int Count { get; }

    /// <summary>Validates the fixed-width TargetProfile rows.</summary>
    /// <param name="section">The borrowed TargetProfile section bytes.</param>
    /// <param name="recordCount">The number of eight-byte TargetProfile rows.</param>
    public TrRmTargetProfileTableReader(ReadOnlySpan<byte> section, int recordCount)
    {
        if (recordCount is < 0 or > 0xFF_FFFF
            || (long)recordCount * RowSize != section.Length)
        {
            throw InvalidProfile();
        }

        _rows = section;
        Count = recordCount;
        var profiles = new HashSet<(uint ProfileId, uint RuleRevision)>();
        for (var row = 0; row < recordCount; row++)
        {
            var profileId = Read(row, 0);
            var ruleRevision = Read(row, 4);
            if (profileId == 0 || ruleRevision == 0
                || !profiles.Add((profileId, ruleRevision)))
            {
                throw InvalidProfile();
            }
        }
    }

    /// <summary>Gets a validated TargetProfile row by its one-based row number.</summary>
    public TrRmTargetProfileRow GetProfile(int row)
    {
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
            var profile = GetProfile(candidate);
            if (profile.ProfileId == profileId && profile.RuleRevision == ruleRevision)
            {
                row = candidate;
                return true;
            }
        }

        row = 0;
        return false;
    }

    internal static bool IsAssigned(uint profileId, uint ruleRevision)
        => profileId == 1 && ruleRevision == 1;

    private uint Read(int row, int offset)
        => BinaryPrimitives.ReadUInt32LittleEndian(
            _rows.Slice((row * RowSize) + offset, sizeof(uint)));

    private static InvalidDataException InvalidProfile()
        => new("Invalid TRRM TargetProfile table.");
}
