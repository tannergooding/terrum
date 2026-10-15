// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.IO;

namespace Terrum.CodeAnalysis.Modules;

/// <summary>Composes provisional TRRM (1, 0) section and cross-table structural checks.</summary>
/// <remarks>Does not verify executable Body instructions, target ABI layouts, executable contracts or import binding.</remarks>
public static class TrRmStructuralMetadataValidator
{
    internal readonly ref struct ValidatedExecutionTables
    {
        internal ValidatedExecutionTables(TrRmStringTableReader strings,
            TrRmModuleTableReader modules, TrRmTypeDefTableReader typeDefs,
            TrRmTypeSpecTableReader typeSpecs,
            TrRmMethodSigTableReader signatures, TrRmParamTableReader parameters,
            TrRmMethodDefTableReader methods, TrRmBodyTableReader bodies,
            TrRmContractTableReader contracts, TrRmNativeImportTableReader nativeImports)
        {
            Strings = strings;
            Modules = modules;
            TypeDefs = typeDefs;
            TypeSpecs = typeSpecs;
            Signatures = signatures;
            Parameters = parameters;
            Methods = methods;
            Bodies = bodies;
            Contracts = contracts;
            NativeImports = nativeImports;
        }

        internal TrRmStringTableReader Strings { get; }
        internal TrRmModuleTableReader Modules { get; }
        internal TrRmTypeDefTableReader TypeDefs { get; }
        internal TrRmTypeSpecTableReader TypeSpecs { get; }
        internal TrRmMethodSigTableReader Signatures { get; }
        internal TrRmParamTableReader Parameters { get; }
        internal TrRmMethodDefTableReader Methods { get; }
        internal TrRmBodyTableReader Bodies { get; }
        internal TrRmContractTableReader Contracts { get; }
        internal TrRmNativeImportTableReader NativeImports { get; }
    }

    private enum BodyInspection
    {
        None,
        CompactLinear,
        CompactIndexed,
        CompactMixed,
    }

    public static void Validate(ReadOnlySpan<byte> artifact)
        => _ = ValidateCore(new TrRmArtifactIndex(artifact), BodyInspection.None);

    internal static void Validate(TrRmArtifactIndex index)
        => _ = ValidateCore(index, BodyInspection.None);

    internal static ValidatedExecutionTables ValidateAndGetExecutionTables(
        TrRmArtifactIndex index)
        => ValidateCore(index, BodyInspection.None);

    public static void ValidateCompactLinear(ReadOnlySpan<byte> artifact)
        => _ = ValidateCore(artifact, BodyInspection.CompactLinear);

    public static void ValidateCompactIndexed(ReadOnlySpan<byte> artifact)
        => _ = ValidateCore(artifact, BodyInspection.CompactIndexed);

    public static void ValidateCompact(ReadOnlySpan<byte> artifact)
        => _ = ValidateCore(artifact, BodyInspection.CompactMixed);

    private static ValidatedExecutionTables ValidateCore(ReadOnlySpan<byte> artifact,
        BodyInspection inspection)
        => ValidateCore(new TrRmArtifactIndex(artifact), inspection);

    private static ValidatedExecutionTables ValidateCore(TrRmArtifactIndex index,
        BodyInspection inspection)
    {
        if (!index.TryGetSection(0x0001, out var moduleBytes, out var moduleCount)
            || !index.TryGetSection(0x0002, out var stringBytes, out var stringCount))
        {
            throw InvalidMetadata();
        }

        index.TryGetSection(0x0004, out var featureBytes, out var featureCount);
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
        index.TryGetSection(0x0021, out var nativeImportBytes, out var nativeImportCount);
        index.TryGetSection(0x7F00, out var sourceMapBytes, out var sourceMapCount);

        if (blobCount != 0)
        {
            _ = new TrRmBlobTableReader(blobBytes, blobCount);
        }

        var bodies = bodyCount == 0 ? default
            : new TrRmBodyTableReader(bodyBytes, bodyCount);

        var strings = new TrRmStringTableReader(stringBytes, stringCount);
        strings.ValidateShapes(stringCount);
        var features = new TrRmFeatureTableReader(featureBytes, featureCount);
        var modules = new TrRmModuleTableReader(moduleBytes, moduleCount, strings,
            moduleRefCount, typeDefCount, methodDefCount, sourceFileCount, featureCount);
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
        ValidateNativeComFeatureAssignments(features, modules, methods, contracts);
        var nativeImports = new TrRmNativeImportTableReader(nativeImportBytes,
            nativeImportCount, strings, methods, signatures, parameters, typeSpecs, contracts);
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
                || inspection == BodyInspection.CompactLinear ? []
                : new int[methods.Count + 1][];
            if (bodyCount != 0)
            {
                for (var method = 1; method <= methods.Count; method++)
                {
                    var row = methods.GetBodyRow(method);
                    if (row != 0)
                    {
                        var linear = inspection == BodyInspection.CompactLinear
                            || inspection == BodyInspection.CompactMixed
                                && (bodies.GetRow(row)[0] & 0x0C) == 0x04;
                        if (linear)
                        {
                            var body = new TrRmCompactLinearBodyReader(bodies, method, modules,
                                methods, signatures, parameters, typeSpecs, types, imports);
                            if (sourceMapCount != 0)
                            {
                                if (inspection == BodyInspection.CompactMixed)
                                {
                                    indexedCounts[method] = [0, body.OperationCount];
                                }
                                else
                                {
                                    linearCounts[method] = body.OperationCount;
                                }
                            }
                        }
                        else
                        {
                            var body = new TrRmCompactIndexedBodyReader(bodies, method, modules,
                                methods, signatures, parameters, typeSpecs, types, imports);
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
            }

            if (sourceMapCount != 0)
            {
                if (inspection == BodyInspection.CompactLinear)
                {
                    sourceMaps.ValidateCompactLinearLocations(methods, linearCounts);
                }
                else
                {
                    sourceMaps.ValidateCompactLocations(methods, indexedCounts);
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
        return new ValidatedExecutionTables(strings, modules, types, typeSpecs,
            signatures, parameters, methods, bodies, contracts, nativeImports);
    }

    private static void ValidateNativeComFeatureAssignments(
        TrRmFeatureTableReader features, TrRmModuleTableReader modules,
        TrRmMethodDefTableReader methods,
        TrRmContractTableReader contracts)
    {
        for (var module = 1; module <= modules.Count; module++)
        {
            var hasNativeComContracts = false;
            var (typeStart, typeEnd) = modules.GetTypeDefRange(module);
            for (var type = typeStart; type < typeEnd; type++)
            {
                hasNativeComContracts |= contracts.HasNativeComContractsForTypeDef(type);
            }

            var (methodStart, methodEnd) = modules.GetMethodDefRange(module);
            for (var method = methodStart; method < methodEnd; method++)
            {
                hasNativeComContracts |= contracts.HasNativeComContractsForMethodDef(method);
            }

            if (!hasNativeComContracts)
            {
                continue;
            }

            var (featureStart, moduleFeatureCount) = modules.GetFeatureRange(module);
            var declaresNativeComFeature = false;
            for (var offset = 0; offset < moduleFeatureCount; offset++)
            {
                if (features.GetFeature(featureStart + offset).FeatureId
                    == TrRmFeatureTableReader.NativeComContractsFeatureId)
                {
                    declaresNativeComFeature = true;
                    break;
                }
            }

            if (!declaresNativeComFeature)
            {
                throw InvalidMetadata();
            }
        }
    }

    private static InvalidDataException InvalidMetadata()
        => new("Invalid TRRM structural metadata.");
}
