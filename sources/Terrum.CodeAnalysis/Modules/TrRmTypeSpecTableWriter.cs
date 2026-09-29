// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Generic;

namespace Terrum.CodeAnalysis.Modules;

/// <summary>Emits indexed TypeSpec rows checked against referenced table counts.</summary>
public static class TrRmTypeSpecTableWriter
{
    public static byte[] Encode(IReadOnlyList<ReadOnlyMemory<byte>> rows,
        int typeDefCount, int importCount, int genericParamCount, int typeArgCount)
    {
        var result = TrRmVariableTableWriter.EncodeBlobs(rows);
        _ = new TrRmTypeSpecTableReader(result, rows.Count,
            typeDefCount, importCount, genericParamCount, typeArgCount);
        return result;
    }
}
