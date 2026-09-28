// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.IO;

namespace Terrum.CodeAnalysis.Modules;

/// <summary>Composes provisional TRRM (1, 0) section and cross-table structural checks.</summary>
/// <remarks>Does not verify executable Body instructions, target ABI layouts, executable contracts or import binding.</remarks>
public static class TrRmStructuralMetadataValidator
{
    public static void Validate(ReadOnlySpan<byte> artifact)
    {
        var index = new TrRmArtifactIndex(artifact);
        if (!index.TryGetSection(0x0001, out var moduleBytes, out var moduleCount)
            || !index.TryGetSection(0x0002, out var stringBytes, out var stringCount))
        {
            throw InvalidMetadata();
        }

        index.TryGetSection(0x0010, out var moduleRefBytes, out var moduleRefCount);
        index.TryGetSection(0x0011, out var typeSpecBytes, out var typeSpecCount);
        index.TryGetSection(0x0012, out var typeArgBytes, out var typeArgCount);
        index.TryGetSection(0x0013, out var typeDefBytes, out var typeDefCount);
        index.TryGetSection(0x0014, out var fieldDefBytes, out var fieldDefCount);
        index.TryGetSection(0x0015, out var layoutBytes, out var layoutCount);
        index.TryGetSection(0x0016, out var methodDefBytes, out var methodDefCount);
        index.TryGetSection(0x0017, out var methodSigBytes, out var methodSigCount);
        index.TryGetSection(0x0018, out var paramBytes, out var paramCount);
        index.TryGetSection(0x0019, out var genericParamBytes, out var genericParamCount);
        index.TryGetSection(0x001A, out var constraintBytes, out var constraintCount);
        index.TryGetSection(0x001B, out var contractBytes, out var contractCount);
        index.TryGetSection(0x001C, out var importBytes, out var importCount);
        index.TryGetSection(0x001D, out var exportBytes, out var exportCount);
        index.TryGetSection(0x001E, out var propertyBytes, out var propertyCount);
        index.TryGetSection(0x001F, out var sourceFileBytes, out var sourceFileCount);
        index.TryGetSection(0x0003, out var blobBytes, out var blobCount);
        index.TryGetSection(0x0020, out var bodyBytes, out var bodyCount);

        if (blobCount != 0)
        {
            _ = new TrRmVariableTableIndex(blobBytes, blobCount, 0, 1);
        }

        if (bodyCount != 0)
        {
            _ = new TrRmVariableTableIndex(bodyBytes, bodyCount, 1, 1);
        }

        var strings = new TrRmStringTableReader(stringBytes, stringCount);
        var modules = new TrRmModuleTableReader(moduleBytes, moduleCount, strings,
            moduleRefCount, typeDefCount, methodDefCount, sourceFileCount);
        var moduleRefs = new TrRmModuleRefTableReader(moduleRefBytes, moduleRefCount,
            strings, modules);
        var typeSpecs = typeSpecCount == 0 ? default
            : new TrRmTypeSpecTableReader(typeSpecBytes, typeSpecCount,
                typeDefCount, importCount, genericParamCount, typeArgCount);
        var typeArgs = typeArgCount == 0 ? default
            : new TrRmTypeArgTableReader(typeArgBytes, typeArgCount, typeSpecs);
        var types = new TrRmTypeDefTableReader(typeDefBytes, typeDefCount,
            strings, modules, fieldDefCount, genericParamCount, layoutCount, contractCount);
        var fields = fieldDefCount == 0 ? default
            : new TrRmFieldDefTableReader(fieldDefBytes, fieldDefCount, strings, types, typeSpecs);
        var layouts = layoutCount == 0 ? default
            : new TrRmLayoutTableReader(layoutBytes, layoutCount, types);
        var signatures = methodSigCount == 0 ? default
            : new TrRmMethodSigTableReader(methodSigBytes, methodSigCount,
                typeSpecs, paramCount, genericParamCount);
        var parameters = paramCount == 0 ? default
            : new TrRmParamTableReader(paramBytes, paramCount, signatures, typeSpecs);
        var methods = new TrRmMethodDefTableReader(methodDefBytes, methodDefCount,
            strings, modules, types, signatures, contractCount, bodyCount);
        var properties = propertyCount == 0 ? default
            : new TrRmPropertyDefTableReader(propertyBytes, propertyCount,
                strings, types, typeSpecs, methods, signatures);
        var generics = new TrRmGenericParamTableReader(genericParamBytes, genericParamCount,
            strings, types, methods, signatures, constraintCount);
        var constraints = constraintCount == 0 ? default
            : new TrRmConstraintTableReader(constraintBytes, constraintCount,
                generics, typeSpecs);
        var contracts = new TrRmContractTableReader(contractBytes, contractCount, types, methods);
        var imports = new TrRmImportTableReader(importBytes, importCount,
            strings, moduleRefs, types, methods, properties);
        var exports = new TrRmExportTableReader(exportBytes, exportCount,
            strings, types, methods, properties, imports);
        var sourceFiles = sourceFileCount == 0 ? default
            : new TrRmSourceFileTableReader(sourceFileBytes, sourceFileCount, strings, modules);

        _ = typeArgs;
        _ = fields;
        _ = layouts;
        _ = parameters;
        _ = constraints;
        _ = contracts;
        _ = exports;
        _ = sourceFiles;
    }

    private static InvalidDataException InvalidMetadata()
        => new("Invalid TRRM structural metadata.");
}
