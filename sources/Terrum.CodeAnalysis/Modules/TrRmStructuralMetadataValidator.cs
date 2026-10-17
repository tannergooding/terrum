// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
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
            TrRmTypeSpecTableReader typeSpecs, TrRmFieldDefTableReader fields,
            TrRmLayoutTableReader layouts,
            TrRmMethodSigTableReader signatures, TrRmParamTableReader parameters,
            TrRmMethodDefTableReader methods, TrRmBodyTableReader bodies,
            TrRmContractTableReader contracts, TrRmNativeImportTableReader nativeImports,
            TrRmNativeCallableTableReader nativeCallables,
            TrRmNativeFunctionPointerSignatureTableReader nativeFunctionPointerSignatures,
            TrRmNativeVTableTableReader nativeVTables, TrRmImportTableReader imports)
        {
            Strings = strings;
            Modules = modules;
            TypeDefs = typeDefs;
            TypeSpecs = typeSpecs;
            Fields = fields;
            Layouts = layouts;
            Signatures = signatures;
            Parameters = parameters;
            Methods = methods;
            Bodies = bodies;
            Contracts = contracts;
            NativeImports = nativeImports;
            NativeCallables = nativeCallables;
            NativeFunctionPointerSignatures = nativeFunctionPointerSignatures;
            NativeVTables = nativeVTables;
            Imports = imports;
        }

        internal TrRmStringTableReader Strings { get; }
        internal TrRmModuleTableReader Modules { get; }
        internal TrRmTypeDefTableReader TypeDefs { get; }
        internal TrRmTypeSpecTableReader TypeSpecs { get; }
        internal TrRmFieldDefTableReader Fields { get; }
        internal TrRmLayoutTableReader Layouts { get; }
        internal TrRmMethodSigTableReader Signatures { get; }
        internal TrRmParamTableReader Parameters { get; }
        internal TrRmMethodDefTableReader Methods { get; }
        internal TrRmBodyTableReader Bodies { get; }
        internal TrRmContractTableReader Contracts { get; }
        internal TrRmNativeImportTableReader NativeImports { get; }
        internal TrRmNativeCallableTableReader NativeCallables { get; }
        internal TrRmNativeFunctionPointerSignatureTableReader NativeFunctionPointerSignatures { get; }
        internal TrRmNativeVTableTableReader NativeVTables { get; }
        internal TrRmImportTableReader Imports { get; }
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

        _ = index.TryGetSection(0x0004, out var featureBytes, out var featureCount);
        _ = index.TryGetSection(0x0010, out var moduleRefBytes, out var moduleRefCount);
        _ = index.TryGetSection(0x0011, out var typeSpecBytes, out var typeSpecCount);
        _ = index.TryGetSection(0x0012, out var typeArgBytes, out var typeArgCount);
        _ = index.TryGetSection(0x0013, out var typeDefBytes, out var typeDefCount);
        _ = index.TryGetSection(0x0014, out var fieldDefBytes, out var fieldDefCount);
        _ = index.TryGetSection(0x0015, out var layoutBytes, out var layoutCount);
        _ = index.TryGetSection(0x0016, out var methodDefBytes, out var methodDefCount);
        _ = index.TryGetSection(0x0017, out var methodSigBytes, out var methodSigCount);
        _ = index.TryGetSection(0x0018, out var paramBytes, out var paramCount);
        _ = index.TryGetSection(0x0019, out var genericParamBytes, out var genericParamCount);
        _ = index.TryGetSection(0x001A, out var constraintBytes, out var constraintCount);
        _ = index.TryGetSection(0x001B, out var contractBytes, out var contractCount);
        _ = index.TryGetSection(0x001C, out var importBytes, out var importCount);
        _ = index.TryGetSection(0x001D, out var exportBytes, out var exportCount);
        _ = index.TryGetSection(0x001E, out var propertyBytes, out var propertyCount);
        _ = index.TryGetSection(0x001F, out var sourceFileBytes, out var sourceFileCount);
        _ = index.TryGetSection(0x0003, out var blobBytes, out var blobCount);
        _ = index.TryGetSection(0x0020, out var bodyBytes, out var bodyCount);
        _ = index.TryGetSection(0x0030, out var nativeImportBytes, out var nativeImportCount);
        _ = index.TryGetSection(0x0031, out var nativeCallableBytes, out var nativeCallableCount);
        _ = index.TryGetSection(0x0032, out var nativeVTableBytes, out var nativeVTableCount);
        _ = index.TryGetSection(0x0033, out var nativeFunctionPointerSignatureBytes,
            out var nativeFunctionPointerSignatureCount);
        _ = index.TryGetSection(0x7F00, out var sourceMapBytes, out var sourceMapCount);

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
        var typeSpecs = typeSpecCount == 0 && nativeFunctionPointerSignatureCount == 0 ? default
            : new TrRmTypeSpecTableReader(typeSpecBytes, typeSpecCount,
                typeDefCount, importCount, genericParamCount, typeArgCount,
                nativeFunctionPointerSignatureCount);
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
        ValidateBlittableLayouts(types, layouts, contracts);
        var imports = new TrRmImportTableReader(importBytes, importCount,
            strings, moduleRefs, types, methods, properties);
        var exports = new TrRmExportTableReader(exportBytes, exportCount,
            strings, types, methods, properties, imports);
        var nativeImports = new TrRmNativeImportTableReader(nativeImportBytes,
            nativeImportCount, strings, methods, signatures, parameters, typeSpecs, contracts);
        var nativeFunctionPointerSignatures =
            new TrRmNativeFunctionPointerSignatureTableReader(
                nativeFunctionPointerSignatureBytes, nativeFunctionPointerSignatureCount,
                methods, signatures, parameters, typeSpecs, contracts);
        var nativeCallables = new TrRmNativeCallableTableReader(nativeCallableBytes,
            nativeCallableCount, strings, methods, signatures, parameters, typeSpecs, contracts,
            nativeFunctionPointerSignatures, imports);
        var nativeVTables = new TrRmNativeVTableTableReader(nativeVTableBytes,
            nativeVTableCount, methods, signatures, parameters, typeSpecs, contracts);
        for (var method = 1; method <= methods.Count; method++)
        {
            if (contracts.HasNativeAbi(method)
                && contracts.GetNativeAbi(method).Role is not (1 or 2 or 3 or 4))
            {
                throw InvalidMetadata();
            }
        }
        ValidateNativeBoundaryFeatureAssignments(features, modules, typeSpecs, signatures,
            parameters, methods, bodies, contracts, nativeImports, nativeCallables,
            nativeFunctionPointerSignatures, nativeVTables);
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
                            || (inspection == BodyInspection.CompactMixed
                                && (bodies.GetRow(row)[0] & 0x0C) == 0x04);
                        if (linear)
                        {
                            var body = new TrRmCompactLinearBodyReader(bodies, method, modules,
                                methods, signatures, parameters, typeSpecs, types, imports,
                                nativeImports);
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
                                methods, signatures, parameters, typeSpecs, types, imports,
                                features, nativeCallables, nativeImports,
                                nativeFunctionPointerSignatures,
                                nativeVTables);
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
            fields, layouts, signatures, parameters, methods, bodies, contracts, nativeImports,
            nativeCallables, nativeFunctionPointerSignatures, nativeVTables, imports);
    }

    private static void ValidateNativeBoundaryFeatureAssignments(
        TrRmFeatureTableReader features, TrRmModuleTableReader modules,
        TrRmTypeSpecTableReader typeSpecs, TrRmMethodSigTableReader signatures,
        TrRmParamTableReader parameters, TrRmMethodDefTableReader methods,
        TrRmBodyTableReader bodies, TrRmContractTableReader contracts,
        TrRmNativeImportTableReader nativeImports,
        TrRmNativeCallableTableReader nativeCallables,
        TrRmNativeFunctionPointerSignatureTableReader nativeFunctionPointerSignatures,
        TrRmNativeVTableTableReader nativeVTables)
    {
        for (var module = 1; module <= modules.Count; module++)
        {
            var hasNativeBoundaryContracts = false;
            var hasNativeVTables = false;
            var hasNativeFunctionPointerSignatures = false;
            var hasNativePointerValues = false;
            var hasNativeImports = false;
            var hasNativeCallableEntries = false;
            var (typeStart, typeEnd) = modules.GetTypeDefRange(module);
            for (var type = typeStart; type < typeEnd; type++)
            {
                hasNativeBoundaryContracts |= contracts.GetNativeBlittableTargetRequirement(type) != 0;
            }
            var (methodStart, methodEnd) = modules.GetMethodDefRange(module);
            for (var method = methodStart; method < methodEnd; method++)
            {
                var hasFunctionPointerContract = nativeFunctionPointerSignatures.HasSignatureForMethod(method)
                    || nativeCallables.HasCallableForMethod(method)
                    || HasNativeFunctionPointerType(method, methods, signatures,
                        parameters, typeSpecs);
                hasNativePointerValues |= HasNativePointerType(method, methods,
                    signatures, parameters, typeSpecs)
                    || HasNativePointerValueInBody(method, methods, bodies, typeSpecs);
                hasNativeImports |= nativeImports.HasImportForMethod(method);
                hasNativeCallableEntries |= nativeCallables.HasCallableForMethod(method);
                hasNativeBoundaryContracts |= nativeImports.HasImportForMethod(method)
                    || hasFunctionPointerContract
                    || hasNativePointerValues
                    || nativeVTables.HasVTableForMethod(method);
                hasNativeFunctionPointerSignatures |= hasFunctionPointerContract;
                hasNativeVTables |= nativeVTables.HasVTableForMethod(method);
            }

            var (featureStart, moduleFeatureCount) = modules.GetFeatureRange(module);
            var declaresNativeBoundaryFeature = false;
            var declaresRootlessNativeValues = false;
            var declaresDirectNativeCalls = false;
            var declaresNativeCallableEntries = false;
            var declaresNativeVTableCalls = false;
            for (var offset = 0; offset < moduleFeatureCount; offset++)
            {
                var featureId = features.GetFeature(featureStart + offset).FeatureId;
                if (featureId == TrRmFeatureTableReader.NativeBoundaryContractsFeatureId)
                {
                    declaresNativeBoundaryFeature = true;
                }
                else if (featureId == TrRmFeatureTableReader.RootlessNativeValuesFeatureId)
                {
                    declaresRootlessNativeValues = true;
                }
                else if (featureId == TrRmFeatureTableReader.DirectNativeCallsFeatureId)
                {
                    declaresDirectNativeCalls = true;
                }
                else if (featureId == TrRmFeatureTableReader.NativeCallableEntriesFeatureId)
                {
                    declaresNativeCallableEntries = true;
                }
                else if (featureId == TrRmFeatureTableReader.NativeVTableCallsFeatureId)
                {
                    declaresNativeVTableCalls = true;
                }
            }

            if (hasNativeBoundaryContracts != declaresNativeBoundaryFeature)
            {
                throw InvalidMetadata();
            }
            if ((hasNativeVTables && (!declaresRootlessNativeValues || !declaresNativeVTableCalls))
                || (hasNativeVTables != declaresNativeVTableCalls)
                || (hasNativeFunctionPointerSignatures && !declaresRootlessNativeValues)
                || (hasNativePointerValues && !declaresRootlessNativeValues)
                || (declaresRootlessNativeValues && !declaresNativeBoundaryFeature)
                || (declaresNativeVTableCalls && !declaresRootlessNativeValues)
                || (hasNativeImports && !declaresDirectNativeCalls)
                || (hasNativeCallableEntries && !declaresNativeCallableEntries)
                || (declaresDirectNativeCalls
                    && (!declaresNativeBoundaryFeature || !declaresRootlessNativeValues))
                || (declaresNativeCallableEntries
                    && (!declaresDirectNativeCalls || !declaresRootlessNativeValues
                        || !declaresNativeBoundaryFeature)))
            {
                throw InvalidMetadata();
            }
        }
    }

    private static bool HasNativeFunctionPointerType(int method,
        TrRmMethodDefTableReader methods, TrRmMethodSigTableReader signatures,
        TrRmParamTableReader parameters, TrRmTypeSpecTableReader typeSpecs)
    {
        var signature = methods.GetSignature(method);
        if (typeSpecs.GetRow(signatures.GetResultTypeSpec(signature))[0] == 0x0D)
        {
            return true;
        }

        var (first, count) = signatures.GetParamRange(signature);
        for (var parameter = 0; parameter < count; parameter++)
        {
            if (typeSpecs.GetRow(parameters.GetTypeSpec(first + parameter))[0] == 0x0D)
            {
                return true;
            }
        }
        return false;
    }

    private static bool HasNativePointerType(int method,
        TrRmMethodDefTableReader methods, TrRmMethodSigTableReader signatures,
        TrRmParamTableReader parameters, TrRmTypeSpecTableReader typeSpecs)
    {
        var signature = methods.GetSignature(method);
        if (IsNativePointerType(typeSpecs.GetRow(signatures.GetResultTypeSpec(signature))))
        {
            return true;
        }

        var (first, count) = signatures.GetParamRange(signature);
        for (var parameter = 0; parameter < count; parameter++)
        {
            if (IsNativePointerType(typeSpecs.GetRow(parameters.GetTypeSpec(first + parameter))))
            {
                return true;
            }
        }
        return false;
    }

    private static bool HasNativePointerValueInBody(int method,
        TrRmMethodDefTableReader methods, TrRmBodyTableReader bodies,
        TrRmTypeSpecTableReader typeSpecs)
    {
        var bodyRow = methods.GetBodyRow(method);
        if (bodyRow == 0)
        {
            return false;
        }

        var body = bodies.GetRow(bodyRow);
        if (body.Length < 2 || (body[0] & 0x0C) is not (0x04 or 0x08)
            || (body[0] & 3) == 3)
        {
            return false;
        }

        var width = (body[0] & 3) switch { 0 => 1, 1 => 2, _ => 4 };
        if (body.Length < 1 + width)
        {
            throw InvalidMetadata();
        }

        var position = 1;
        var bodyForm = body[0] & 0x0C;
        if (bodyForm == 0x08)
        {
            _ = ReadBodyCount(body, ref position, width);
            _ = ReadBodyCount(body, ref position, width);
            _ = ReadBodyCount(body, ref position, width);
        }
        var typeCount = ReadBodyCount(body, ref position, width);
        if (typeCount > (uint)((body.Length - position) / sizeof(uint)))
        {
            throw InvalidMetadata();
        }

        for (var type = 0u; type < typeCount; type++)
        {
            var typeSpec = BinaryPrimitives.ReadUInt32LittleEndian(
                body.Slice(position, sizeof(uint)));
            position += sizeof(uint);
            if (typeSpec == 0 || typeSpec > (uint)typeSpecs.Count)
            {
                throw InvalidMetadata();
            }
            if (IsNativePointerType(typeSpecs.GetRow((int)typeSpec)))
            {
                return true;
            }
        }
        return false;
    }

    private static uint ReadBodyCount(ReadOnlySpan<byte> body, ref int position, int width)
    {
        if (width > body.Length - position)
        {
            throw InvalidMetadata();
        }

        var value = width switch
        {
            1 => body[position],
            2 => BinaryPrimitives.ReadUInt16LittleEndian(body.Slice(position, 2)),
            _ => BinaryPrimitives.ReadUInt32LittleEndian(body.Slice(position, 4)),
        };
        position += width;
        return value;
    }

    private static bool IsNativePointerType(ReadOnlySpan<byte> typeSpec)
        => typeSpec.Length >= 3 && typeSpec[2] == 0x8C
            && typeSpec[0] is 0x01 or 0x0C;

    private static void ValidateBlittableLayouts(TrRmTypeDefTableReader types,
        TrRmLayoutTableReader layouts, TrRmContractTableReader contracts)
    {
        for (var type = 1; type <= types.Count; type++)
        {
            var targetRequirement = contracts.GetNativeBlittableTargetRequirement(type);
            if (targetRequirement == 0)
            {
                continue;
            }

            var layoutRow = types.GetLayoutRow(type);
            if (layoutRow == 0
                || layouts.GetTargetRequirement(layoutRow) != targetRequirement
                || layouts.GetSize(layoutRow) == 0
                || layouts.GetAlignment(layoutRow) == 0
                || layouts.GetPack(layoutRow) != 0
                || layouts.GetFlags(layoutRow) != 0)
            {
                throw InvalidMetadata();
            }
        }
    }

    private static InvalidDataException InvalidMetadata()
        => new("Invalid TRRM structural metadata.");
}
