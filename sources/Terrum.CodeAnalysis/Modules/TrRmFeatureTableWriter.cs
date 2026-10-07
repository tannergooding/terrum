// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;

namespace Terrum.CodeAnalysis.Modules;

/// <summary>Emits validated required-feature rows for provisional TRRM (1, 0) artifacts.</summary>
public static class TrRmFeatureTableWriter
{
    /// <summary>Encodes rows after validating their identifiers and supported rule revisions.</summary>
    /// <param name="rows">The ordered feature rows to encode.</param>
    /// <returns>The encoded Feature section.</returns>
    public static byte[] Encode(IReadOnlyList<TrRmFeatureRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        if (rows.Count > 0xFF_FFFF)
        {
            throw new ArgumentOutOfRangeException(nameof(rows));
        }

        var result = new byte[rows.Count * 8];
        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            Write(result, index * 8, row.FeatureId);
            Write(result, (index * 8) + 4, row.MinimumRuleRevision);
        }

        _ = new TrRmFeatureTableReader(result, rows.Count);
        return result;
    }

    private static void Write(byte[] bytes, int offset, uint value)
        => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);
}
