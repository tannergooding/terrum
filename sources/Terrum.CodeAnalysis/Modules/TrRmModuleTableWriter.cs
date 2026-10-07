// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;

namespace Terrum.CodeAnalysis.Modules;

/// <summary>A Module row and its artifact-wide table and feature ranges.</summary>
/// <param name="IdentityStringRow">The one-based String row for the module identity.</param>
/// <param name="TargetRequirement">The target requirement value.</param>
/// <param name="RuntimePolicy">The runtime policy value.</param>
/// <param name="EntryMethodDefRow">The one-based entry MethodDef row, or zero when absent.</param>
/// <param name="ModuleRefStart">The one-based first ModuleRef row owned by the module.</param>
/// <param name="TypeDefStart">The one-based first TypeDef row owned by the module.</param>
/// <param name="MethodDefStart">The one-based first MethodDef row owned by the module.</param>
/// <param name="SourceFileStart">The one-based first SourceFile row owned by the module.</param>
/// <param name="FeatureFirst">The one-based first Feature row required by the module, or zero when absent.</param>
/// <param name="FeatureCount">The number of required Feature rows.</param>
public readonly record struct TrRmModuleRow(
    int IdentityStringRow, uint TargetRequirement, uint RuntimePolicy, int EntryMethodDefRow,
    int ModuleRefStart, int TypeDefStart, int MethodDefStart, int SourceFileStart,
    int FeatureFirst = 0, int FeatureCount = 0);

/// <summary>Emits Module rows with validated identities and artifact-wide ownership partitions.</summary>
public static class TrRmModuleTableWriter
{
    /// <summary>Encodes Module rows with validated identities, ownership, and feature ranges.</summary>
    /// <param name="rows">The ordered Module rows to encode.</param>
    /// <param name="strings">The validated String table used for module identities.</param>
    /// <param name="moduleRefCount">The number of artifact ModuleRef rows.</param>
    /// <param name="typeDefCount">The number of artifact TypeDef rows.</param>
    /// <param name="methodDefCount">The number of artifact MethodDef rows.</param>
    /// <param name="sourceFileCount">The number of artifact SourceFile rows.</param>
    /// <param name="featureCount">The number of artifact Feature rows, or -1 if not supplied.</param>
    /// <returns>The encoded Module section.</returns>
    public static byte[] Encode(IReadOnlyList<TrRmModuleRow> rows,
        TrRmStringTableReader strings, int moduleRefCount, int typeDefCount,
        int methodDefCount, int sourceFileCount, int featureCount = -1)
    {
        ArgumentNullException.ThrowIfNull(rows);
        if (rows.Count is <= 0 or > 0xFF_FFFF)
        {
            throw new ArgumentOutOfRangeException(nameof(rows));
        }

        var result = new byte[rows.Count * 44];
        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            var offset = index * 44;
            Write(result, offset, row.IdentityStringRow);
            Write(result, offset + 4, row.TargetRequirement);
            Write(result, offset + 8, row.RuntimePolicy);
            Write(result, offset + 12, row.EntryMethodDefRow);
            Write(result, offset + 16, row.FeatureFirst);
            Write(result, offset + 20, row.FeatureCount);
            Write(result, offset + 28, row.ModuleRefStart);
            Write(result, offset + 32, row.TypeDefStart);
            Write(result, offset + 36, row.MethodDefStart);
            Write(result, offset + 40, row.SourceFileStart);
        }

        _ = new TrRmModuleTableReader(result, rows.Count, strings,
            moduleRefCount, typeDefCount, methodDefCount, sourceFileCount, featureCount);
        return result;
    }

    private static void Write(byte[] bytes, int offset, int value)
        => Write(bytes, offset, unchecked((uint)value));

    private static void Write(byte[] bytes, int offset, uint value)
        => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);
}
