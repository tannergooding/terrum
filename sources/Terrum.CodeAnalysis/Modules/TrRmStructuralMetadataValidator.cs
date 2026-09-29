// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.IO;

namespace Terrum.CodeAnalysis.Modules;

/// <summary>Composes provisional TRRM (1, 0) section and cross-table structural checks.</summary>
/// <remarks>Does not verify executable Body instructions, target ABI layouts, executable contracts or import binding.</remarks>
public static class TrRmStructuralMetadataValidator
{
    private enum BodyInspection
    {
        None,
        CompactLinear,
        CompactIndexed,
    }

    public static void Validate(ReadOnlySpan<byte> artifact)
        => ValidateCore(artifact, BodyInspection.None);

    public static void ValidateCompactLinear(ReadOnlySpan<byte> artifact)
        => ValidateCore(artifact, BodyInspection.CompactLinear);

    public static void ValidateCompactIndexed(ReadOnlySpan<byte> artifact)
        => ValidateCore(artifact, BodyInspection.CompactIndexed);

    private static void ValidateCore(ReadOnlySpan<byte> artifact, BodyInspection inspection)
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
        index.TryGetSection(0x7F00, out var sourceMapBytes, out var sourceMapCount);

        if (blobCount != 0)
        {
            _ = new TrRmBlobTableReader(blobBytes, blobCount);
        }

        var bodies = bodyCount == 0 ? default
            : new TrRmBodyTableReader(bodyBytes, bodyCount);

        var strings = new TrRmStringTableReader(stringBytes, stringCount);
        strings.ValidateShapes(stringCount);
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
        var layouts = layoutCount == 0 ? default
            : new TrRmLayoutTableReader(layoutBytes, layoutCount, types);
        var fields = fieldDefCount == 0 ? default
            : new TrRmFieldDefTableReader(fieldDefBytes, fieldDefCount, strings, types,
                typeSpecs, layouts);
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
        var contracts = new TrRmContractTableReader(contractBytes, contractCount,
            types, methods, signatures, parameters, typeSpecs);
        var imports = new TrRmImportTableReader(importBytes, importCount,
            strings, moduleRefs, types, methods, properties);
        var exports = new TrRmExportTableReader(exportBytes, exportCount,
            strings, types, methods, properties, imports);
        var sourceFiles = sourceFileCount == 0 ? default
            : new TrRmSourceFileTableReader(sourceFileBytes, sourceFileCount, strings, modules);
        var sourceMaps = sourceMapCount == 0 ? default
            : new TrRmSourceMapTableReader(sourceMapBytes, sourceMapCount,
                methods, sourceFiles);

        if (inspection != BodyInspection.None)
        {
            var linearCounts = sourceMapCount == 0
                || inspection != BodyInspection.CompactLinear ? Array.Empty<int>()
                : new int[methods.Count + 1];
            var indexedCounts = sourceMapCount == 0
                || inspection != BodyInspection.CompactIndexed ? []
                : new int[methods.Count + 1][];
            for (var method = 1; method <= methods.Count; method++)
            {
                if (methods.GetBodyRow(method) != 0)
                {
                    if (inspection == BodyInspection.CompactLinear)
                    {
                        var body = new TrRmCompactLinearBodyReader(bodies, method, modules,
                            methods, signatures, parameters, typeSpecs);
                        if (sourceMapCount != 0)
                        {
                            linearCounts[method] = body.OperationCount;
                        }
                    }
                    else
                    {
                        var body = new TrRmCompactIndexedBodyReader(bodies, method, modules,
                            methods, signatures, parameters, typeSpecs);
                        if (sourceMapCount != 0)
                        {
                            var counts = new int[body.BlockCount + 1];
                            for (var block = 1; block <= body.BlockCount; block++)
                            {
                                counts[block] = body.GetOperationCount(block);
                            }

                            indexedCounts[method] = counts;
                        }
                    }
                }
            }

            if (sourceMapCount != 0)
            {
                if (inspection == BodyInspection.CompactLinear)
                {
                    sourceMaps.ValidateCompactLinearLocations(methods, linearCounts);
                }
                else
                {
                    sourceMaps.ValidateCompactIndexedLocations(methods, indexedCounts);
                }
            }
        }

        _ = typeArgs;
        _ = fields;
        _ = layouts;
        _ = parameters;
        _ = constraints;
        _ = contracts;
        _ = exports;
        _ = sourceFiles;
        _ = sourceMaps;
    }

    private static InvalidDataException InvalidMetadata()
        => new("Invalid TRRM structural metadata.");
}
