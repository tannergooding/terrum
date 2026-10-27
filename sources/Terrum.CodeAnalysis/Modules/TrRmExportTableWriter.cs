// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;

namespace Terrum.CodeAnalysis.Modules;

public readonly record struct TrRmExportRow(uint LocalDefId, int ExportKeyStringRow);

/// <summary>Emits public export keys after checking ownership, visibility and uniqueness.</summary>
public static class TrRmExportTableWriter
{
    /// <summary>Encodes exported local definitions and validates their publication.</summary>
    /// <param name="rows">The ordered Export rows.</param>
    /// <param name="strings">The validated String table.</param>
    /// <param name="types">The validated TypeDef table.</param>
    /// <param name="methods">The validated MethodDef table.</param>
    /// <param name="properties">The validated PropertyDef table.</param>
    /// <param name="imports">The validated Import table.</param>
    /// <param name="interfaces">The validated InterfaceDef table, or default when unsupported.</param>
    /// <param name="associatedTypeDefs">The validated AssociatedTypeDef table, or default when unsupported.</param>
    /// <param name="fields">The validated FieldDef table, or default when unsupported.</param>
    /// <returns>The encoded Export section.</returns>
    public static byte[] Encode(IReadOnlyList<TrRmExportRow> rows,
        TrRmStringTableReader strings, TrRmTypeDefTableReader types,
        TrRmMethodDefTableReader methods, TrRmPropertyDefTableReader properties,
        TrRmImportTableReader imports,
        TrRmInterfaceDefTableReader interfaces = default,
        TrRmAssociatedTypeDefTableReader associatedTypeDefs = default,
        TrRmFieldDefTableReader fields = default)
        => EncodeCore(rows, strings, types, methods, properties, imports,
            interfaces, associatedTypeDefs, fields, default, default,
            TrRmArtifactSchema.Standard, profileAware: false);

    internal static byte[] Encode(IReadOnlyList<TrRmExportRow> rows,
        TrRmStringTableReader strings, TrRmTypeDefTableReader types,
        TrRmMethodDefTableReader methods, TrRmPropertyDefTableReader properties,
        TrRmImportTableReader imports,
        TrRmInterfaceDefTableReader interfaces,
        TrRmAssociatedTypeDefTableReader associatedTypeDefs,
        TrRmFieldDefTableReader fields,
        TrRmFeatureTableReader features, TrRmModuleTableReader modules,
        TrRmArtifactSchema schema)
        => EncodeCore(rows, strings, types, methods, properties, imports,
            interfaces, associatedTypeDefs, fields, features, modules, schema,
            profileAware: true);

    private static byte[] EncodeCore(IReadOnlyList<TrRmExportRow> rows,
        TrRmStringTableReader strings, TrRmTypeDefTableReader types,
        TrRmMethodDefTableReader methods, TrRmPropertyDefTableReader properties,
        TrRmImportTableReader imports,
        TrRmInterfaceDefTableReader interfaces,
        TrRmAssociatedTypeDefTableReader associatedTypeDefs,
        TrRmFieldDefTableReader fields,
        TrRmFeatureTableReader features, TrRmModuleTableReader modules,
        TrRmArtifactSchema schema, bool profileAware)
    {
        ArgumentNullException.ThrowIfNull(rows);
        if (rows.Count is <= 0 or > 0xFF_FFFF)
        {
            throw new ArgumentOutOfRangeException(nameof(rows));
        }

        var result = new byte[rows.Count * 8];
        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            var offset = index * 8;
            BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(offset), row.LocalDefId);
            BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(offset + 4),
                unchecked((uint)row.ExportKeyStringRow));
        }

        if (profileAware)
        {
            _ = new TrRmExportTableReader(result, rows.Count, strings, types,
                methods, properties, imports, interfaces, associatedTypeDefs,
                fields, features, modules, schema);
        }
        else
        {
            _ = new TrRmExportTableReader(result, rows.Count, strings, types,
                methods, properties, imports, interfaces, associatedTypeDefs, fields);
        }
        return result;
    }
}
