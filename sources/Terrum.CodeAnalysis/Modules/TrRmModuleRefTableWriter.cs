// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;

namespace Terrum.CodeAnalysis.Modules;

/// <summary>Describes a producer reference and its target and runtime requirements.</summary>
/// <param name="ProducerIdentityStringRow">The String row containing the producer identity.</param>
/// <param name="TargetRequirement">The producer's target requirement.</param>
/// <param name="RuntimePolicy">The producer's runtime policy.</param>
public readonly record struct TrRmModuleRefRow(
    int ProducerIdentityStringRow, uint TargetRequirement, uint RuntimePolicy);

/// <summary>Emits ModuleRef rows with checked ownership and co-located producer requirements.</summary>
/// <remarks>External producers remain unresolved until import binding.</remarks>
public static class TrRmModuleRefTableWriter
{
    /// <summary>Encodes ModuleRef rows and validates their owning Module partitions.</summary>
    /// <param name="rows">The ModuleRef rows in table order.</param>
    /// <param name="strings">The validated String table.</param>
    /// <param name="modules">The validated Module table.</param>
    /// <returns>The encoded ModuleRef section.</returns>
    public static byte[] Encode(IReadOnlyList<TrRmModuleRefRow> rows,
        TrRmStringTableReader strings, TrRmModuleTableReader modules)
        => Encode(rows, strings, modules, null);

    internal static byte[] Encode(IReadOnlyList<TrRmModuleRefRow> rows,
        TrRmStringTableReader strings, TrRmModuleTableReader modules,
        TrRmA3LayoutResourceLedger? resourceLedger)
    {
        ArgumentNullException.ThrowIfNull(rows);
        if (rows.Count > 0xFF_FFFF)
        {
            throw new ArgumentOutOfRangeException(nameof(rows));
        }

        resourceLedger?.ConsumeWork(checked(1UL
            + ((ulong)rows.Count
                * (1UL + 3UL + (3UL * sizeof(uint))))));
        var result = new byte[rows.Count * 12];
        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            var offset = index * 12;
            Write(result, offset, unchecked((uint)row.ProducerIdentityStringRow));
            Write(result, offset + 4, row.TargetRequirement);
            Write(result, offset + 8, row.RuntimePolicy);
        }

        _ = new TrRmModuleRefTableReader(result, rows.Count, strings, modules,
            resourceLedger);
        return result;
    }

    private static void Write(byte[] bytes, int offset, uint value)
        => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);
}
