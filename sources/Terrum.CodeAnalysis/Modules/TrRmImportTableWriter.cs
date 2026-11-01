// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;

namespace Terrum.CodeAnalysis.Modules;

public readonly record struct TrRmImportRow(
    int ModuleRefRow, int ExportKeyStringRow, uint ExpectedLocalDefId);

/// <summary>Emits retained import expectations without binding a producer or its contract.</summary>
public static class TrRmImportTableWriter
{
    /// <summary>Encodes retained imports and validates their expected definitions.</summary>
    /// <param name="rows">The ordered Import rows.</param>
    /// <param name="strings">The validated String table.</param>
    /// <param name="references">The validated ModuleRef table.</param>
    /// <param name="types">The validated TypeDef table.</param>
    /// <param name="methods">The validated MethodDef table.</param>
    /// <param name="properties">The validated PropertyDef table.</param>
    /// <param name="interfaces">The validated InterfaceDef table, or default when unsupported.</param>
    /// <param name="associatedTypeDefs">The validated AssociatedTypeDef table, or default when unsupported.</param>
    /// <param name="fields">The validated FieldDef table, or default when unsupported.</param>
    /// <param name="associatedConstDefs">The validated AssociatedConstDef table, or default when unsupported.</param>
    /// <returns>The encoded Import section.</returns>
    public static byte[] Encode(IReadOnlyList<TrRmImportRow> rows,
        TrRmStringTableReader strings, TrRmModuleRefTableReader references,
        TrRmTypeDefTableReader types, TrRmMethodDefTableReader methods,
        TrRmPropertyDefTableReader properties,
        TrRmInterfaceDefTableReader interfaces = default,
        TrRmAssociatedTypeDefTableReader associatedTypeDefs = default,
        TrRmFieldDefTableReader fields = default,
        TrRmAssociatedConstDefTableReader associatedConstDefs = default)
        => EncodeCore(rows, strings, references, types, methods, properties,
            interfaces, associatedTypeDefs, fields, associatedConstDefs, default, default,
            TrRmArtifactSchema.Standard, default, default, default,
            profileAware: false, resourceLedger: null);

    internal static byte[] Encode(IReadOnlyList<TrRmImportRow> rows,
        TrRmStringTableReader strings, TrRmModuleRefTableReader references,
        TrRmTypeDefTableReader types, TrRmMethodDefTableReader methods,
        TrRmPropertyDefTableReader properties,
        TrRmInterfaceDefTableReader interfaces,
        TrRmAssociatedTypeDefTableReader associatedTypeDefs,
        TrRmFieldDefTableReader fields,
        TrRmFeatureTableReader features, TrRmModuleTableReader modules,
        TrRmArtifactSchema schema,
        TrRmAssociatedConstDefTableReader associatedConstDefs = default,
        TrRmStaticFieldTableReader staticFields = default,
        TrRmStaticFieldContractTableReader staticFieldContracts = default,
        TrRmIntrinsicRegistrationTableReader intrinsicRegistrations = default,
        TrRmA3LayoutResourceLedger? resourceLedger = null)
        => EncodeCore(rows, strings, references, types, methods, properties,
            interfaces, associatedTypeDefs, fields, associatedConstDefs,
            features, modules, schema, staticFields, staticFieldContracts,
            intrinsicRegistrations,
            profileAware: true, resourceLedger: resourceLedger);

    private static byte[] EncodeCore(IReadOnlyList<TrRmImportRow> rows,
        TrRmStringTableReader strings, TrRmModuleRefTableReader references,
        TrRmTypeDefTableReader types, TrRmMethodDefTableReader methods,
        TrRmPropertyDefTableReader properties,
        TrRmInterfaceDefTableReader interfaces,
        TrRmAssociatedTypeDefTableReader associatedTypeDefs,
        TrRmFieldDefTableReader fields,
        TrRmAssociatedConstDefTableReader associatedConstDefs,
        TrRmFeatureTableReader features, TrRmModuleTableReader modules,
        TrRmArtifactSchema schema,
        TrRmStaticFieldTableReader staticFields,
        TrRmStaticFieldContractTableReader staticFieldContracts,
        TrRmIntrinsicRegistrationTableReader intrinsicRegistrations,
        bool profileAware,
        TrRmA3LayoutResourceLedger? resourceLedger)
    {
        ArgumentNullException.ThrowIfNull(rows);
        if (rows.Count is <= 0 or > 0xFF_FFFF)
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
            Write(result, offset, unchecked((uint)row.ModuleRefRow));
            Write(result, offset + 4, unchecked((uint)row.ExportKeyStringRow));
            Write(result, offset + 8, row.ExpectedLocalDefId);
        }

        if (profileAware)
        {
            _ = new TrRmImportTableReader(result, rows.Count, strings, references,
                types, methods, properties, interfaces, associatedTypeDefs,
                fields, features, modules, schema, associatedConstDefs,
                staticFields, staticFieldContracts, intrinsicRegistrations,
                resourceLedger);
        }
        else
        {
            _ = new TrRmImportTableReader(result, rows.Count, strings, references,
                types, methods, properties, interfaces, associatedTypeDefs, fields,
                associatedConstDefs);
        }
        return result;
    }

    private static void Write(byte[] bytes, int offset, uint value)
        => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);
}
