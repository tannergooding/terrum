// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using Terrum.CodeAnalysis.Semantics;

namespace Terrum.CodeAnalysis.Modules;

internal static class TrRmCopyOriginContractValidator
{
    internal static void Validate(TrRmContractTableReader contracts,
        TrRmTypeDefTableReader types, TrRmFieldDefTableReader fields,
        TrRmMethodDefTableReader methods, TrRmMethodSigTableReader signatures,
        TrRmParamTableReader parameters, TrRmTypeSpecTableReader typeSpecs,
        TrRmFieldPathTableReader fieldPaths, TrRmImportTableReader imports,
        TrRmUnionCaseDefTableReader unionCaseDefs,
        TrRmLayoutTableReader layouts,
        ReadOnlySpan<byte> nativeImportBytes, int nativeImportCount)
    {
        ValidateNativeImportExclusions(nativeImportBytes, nativeImportCount,
            contracts, types, fields, methods, signatures);

        var unionTypes = new bool[types.Count + 1];
        for (var row = 1; row <= unionCaseDefs.Count; row++)
        {
            unionTypes[unionCaseDefs.GetOwnerTypeDef(row)] = true;
        }

        var resolvedPaths = new Dictionary<int, ResolvedPath>();
        var pathRoots = new Dictionary<PathRootKey, PathNode>();
        for (var method = 1; method <= methods.Count; method++)
        {
            var clause = contracts.GetCopyOriginClauseRow(method);
            if (clause.IsEmpty)
            {
                continue;
            }
            if (contracts.HasNativeAbi(method)
                && contracts.GetNativeAbi(method).Role == 3)
            {
                throw InvalidCopyOrigin();
            }

            var data = clause[8..];
            var summaryKind = data[0];
            var valueTypeSpec = Read(data, 4);
            var valueType = typeSpecs.GetTypeSpecRef(valueTypeSpec);
            if (valueType.Form == 0x0E
                || (valueType.Form == 0x01 && valueType.TypeCode == 0))
            {
                throw InvalidCopyOrigin();
            }

            var signature = methods.GetSignature(method);
            var resultTypeSpec = signatures.GetResultTypeSpec(signature);
            var receiverTypeSpec = signatures.GetReceiverTypeSpec(signature);
            if (!AreEquivalentTypeSpecs(valueTypeSpec, resultTypeSpec,
                    typeSpecs, types, imports, 0))
            {
                throw InvalidCopyOrigin();
            }

            if (summaryKind == 2)
            {
                var ownerType = methods.GetOwnerType(method);
                if (ownerType == 0 || receiverTypeSpec == 0
                    || !AreEquivalentTypeSpecs(valueTypeSpec, receiverTypeSpec,
                        typeSpecs, types, imports, 0)
                    || !TryGetNominalType(valueTypeSpec, typeSpecs, types, imports,
                        out var resultType, out _)
                    || resultType != ownerType
                    || !IsInlineStruct(resultType, types, unionTypes, layouts))
                {
                    throw InvalidCopyOrigin();
                }
            }

            var (parameterStart, parameterCount) =
                signatures.GetParamRange(signature);
            var ownerModule = methods.GetOwnerModule(method);
            var destinationPaths = new HashSet<PathNode>();
            var destinationDescendants = new Dictionary<PathNode, int>();
            var wholeSourcePaths = new Dictionary<SourceKey, PathNode>();
            var origins = new HashSet<OriginKey>();
            var factCount = checked((int)Read(data, 8));
            var offset = 12;
            for (var fact = 0; fact < factCount; fact++)
            {
                var destinationPath = Read(data, offset);
                var originCount = checked((int)Read(data, offset + 4));
                offset += 8;

                var destination = ResolvePath(destinationPath, valueTypeSpec,
                    typeSpecs, types, fields, fieldPaths, imports, unionTypes,
                    layouts, ownerModule, resolvedPaths, pathRoots);
                AddDestination(destination, destinationPaths,
                    destinationDescendants);

                for (var origin = 0; origin < originCount; origin++)
                {
                    var sourceKind = data[offset];
                    var sourceView = data[offset + 1];
                    var sourceOrdinal = Read(data, offset + 4);
                    var sourcePath = Read(data, offset + 8);
                    offset += 12;

                    var sourceKey = new SourceKey(
                        sourceKind, sourceView, sourceOrdinal);
                    var sourceTypeSpec = GetSourceTypeSpec(sourceKey,
                        receiverTypeSpec, parameterStart, parameterCount,
                        parameters, typeSpecs, summaryKind);
                    var source = ResolvePath(sourcePath, sourceTypeSpec,
                        typeSpecs, types, fields, fieldPaths, imports, unionTypes,
                        layouts, ownerModule, resolvedPaths, pathRoots);
                    if (!AreEquivalentTypeSpecs(destination.ValueTypeSpec,
                            source.ValueTypeSpec, typeSpecs, types, imports, 0)
                        || !origins.Add(new OriginKey(sourceKey,
                            GetOriginPathNode(sourcePath, sourceKey, source,
                                wholeSourcePaths))))
                    {
                        throw InvalidCopyOrigin();
                    }
                }
            }
        }
    }

    private static void ValidateNativeImportExclusions(
        ReadOnlySpan<byte> nativeImportBytes, int nativeImportCount,
        TrRmContractTableReader contracts, TrRmTypeDefTableReader types,
        TrRmFieldDefTableReader fields, TrRmMethodDefTableReader methods,
        TrRmMethodSigTableReader signatures)
    {
        if (nativeImportCount is < 0 or > 0xFF_FFFF
            || (long)nativeImportCount * 16 != nativeImportBytes.Length)
        {
            throw InvalidCopyOrigin();
        }

        for (var row = 0; row < nativeImportCount; row++)
        {
            var data = nativeImportBytes.Slice(row * 16, 16);
            var ownerKind = data[0];
            var ownerRef = Read(data, 4);
            if ((ownerRef & 0xFF00_0000) != 0 || ownerRef == 0)
            {
                throw InvalidCopyOrigin();
            }

            var ownerRow = checked((int)ownerRef);
            if (ownerKind == 1)
            {
                if (ownerRow > methods.Count
                    || signatures.GetReceiverTypeSpec(
                        methods.GetSignature(ownerRow)) != 0
                    || methods.GetBodyRow(ownerRow) != 0
                    || !contracts.GetCopyOriginClauseRow(ownerRow).IsEmpty)
                {
                    throw InvalidCopyOrigin();
                }
            }
            else if (ownerKind == 2)
            {
                if (ownerRow > fields.Count
                    || (fields.GetFlags(ownerRow) & 0x08) == 0)
                {
                    throw InvalidCopyOrigin();
                }
            }
            else
            {
                throw InvalidCopyOrigin();
            }
        }
    }

    private static uint GetSourceTypeSpec(SourceKey source,
        uint receiverTypeSpec, int parameterStart, int parameterCount,
        TrRmParamTableReader parameters, TrRmTypeSpecTableReader typeSpecs,
        byte summaryKind)
    {
        if (source.Kind == 2)
        {
            if (source.View != 2 || source.Ordinal != 0
                || receiverTypeSpec == 0 || summaryKind == 2
                || typeSpecs.GetTypeSpecRef(receiverTypeSpec).Form == 0x0E)
            {
                throw InvalidCopyOrigin();
            }

            return receiverTypeSpec;
        }

        if (source.Kind != 1 || source.Ordinal >= (uint)parameterCount)
        {
            throw InvalidCopyOrigin();
        }

        var parameterRow = parameterStart + checked((int)source.Ordinal);
        if (parameters.GetMode(parameterRow) != 0)
        {
            throw InvalidCopyOrigin();
        }

        var parameterTypeSpec = parameters.GetTypeSpec(parameterRow);
        var parameterType = typeSpecs.GetTypeSpecRef(parameterTypeSpec);
        if (parameterType.Form == 0x0E)
        {
            throw InvalidCopyOrigin();
        }

        return source.View == 1
            ? parameterTypeSpec
            : GetReferentTypeSpec(parameterType, typeSpecs);
    }

    internal static uint GetReferentTypeSpec(TrRmTypeSpecRefInfo reference,
        TrRmTypeSpecTableReader typeSpecs)
    {
        uint referent;
        if (reference.IsRow && reference.Form is 0x07 or 0x0C)
        {
            var row = typeSpecs.GetRow(reference.Row);
            if (reference.Form == 0x07)
            {
                referent = Read(row, 2);
            }
            else if (row[2] == 0x8B)
            {
                referent = Read(row, 3);
            }
            else
            {
                throw InvalidCopyOrigin();
            }
        }
        else if (reference.Form == 0x01 && reference.TypeCode == 0x8B)
        {
            referent = 0xF200_0000u | reference.ArgumentCode;
        }
        else
        {
            throw InvalidCopyOrigin();
        }

        _ = typeSpecs.GetTypeSpecRef(referent);
        return referent;
    }

    private static ResolvedPath ResolvePath(uint pathRow, uint expectedRoot,
        TrRmTypeSpecTableReader typeSpecs, TrRmTypeDefTableReader types,
        TrRmFieldDefTableReader fields, TrRmFieldPathTableReader fieldPaths,
        TrRmImportTableReader imports, bool[] unionTypes,
        TrRmLayoutTableReader layouts,
        int ownerModule,
        Dictionary<int, ResolvedPath> resolvedPaths,
        Dictionary<PathRootKey, PathNode> pathRoots)
    {
        if (pathRow == 0)
        {
            var wholeRootKey = GetPathRootKey(expectedRoot, typeSpecs,
                types, imports);
            if (!pathRoots.TryGetValue(wholeRootKey, out var rootNode))
            {
                rootNode = new PathNode(null);
                pathRoots.Add(wholeRootKey, rootNode);
            }

            return new ResolvedPath(expectedRoot, rootNode);
        }

        var row = checked((int)pathRow);
        if (resolvedPaths.TryGetValue(row, out var cached))
        {
            var cachedRoot = fieldPaths.GetRootTypeSpecRef(row);
            if (!AreEquivalentPathRoots(expectedRoot, cachedRoot,
                    typeSpecs, types, imports)
                || !TryGetNominalType(cachedRoot, typeSpecs, types,
                    imports, out var cachedType, out _)
                || types.GetOwnerModule(cachedType) != ownerModule)
            {
                throw InvalidCopyOrigin();
            }

            return cached;
        }

        var pathRoot = fieldPaths.GetRootTypeSpecRef(row);
        if (!TryGetNominalType(pathRoot, typeSpecs, types, imports,
                out var rootType, out var qualifiers)
            || !AreEquivalentPathRoots(expectedRoot, pathRoot,
                typeSpecs, types, imports)
            || types.GetOwnerModule(rootType) != ownerModule
            || !IsInlineStruct(rootType, types, unionTypes, layouts))
        {
            throw InvalidCopyOrigin();
        }

        var fieldCount = fieldPaths.GetFieldCount(row);
        var rootKey = new PathRootKey((uint)rootType, false, qualifiers);
        if (!pathRoots.TryGetValue(rootKey, out var root))
        {
            root = new PathNode(null);
            pathRoots.Add(rootKey, root);
        }

        var node = root;
        var currentTypeSpec = pathRoot;
        for (var ordinal = 0; ordinal < fieldCount; ordinal++)
        {
            if (!TryGetNominalType(currentTypeSpec, typeSpecs, types,
                    imports, out var currentType, out _)
                || !IsInlineStruct(currentType, types, unionTypes, layouts))
            {
                throw InvalidCopyOrigin();
            }

            var fieldReference = fieldPaths.GetFieldReference(row, ordinal);
            var fieldDef = GetFieldDefRow(fieldReference, fields, imports);
            if (fields.GetOwnerTypeDef(fieldDef) != currentType
                || types.GetOwnerModule(currentType) != ownerModule
                || (fields.GetFlags(fieldDef) & 0x08) != 0)
            {
                throw InvalidCopyOrigin();
            }

            if (!node.Children.TryGetValue(fieldDef, out var child))
            {
                child = new PathNode(node);
                node.Children.Add(fieldDef, child);
            }

            node = child;
            currentTypeSpec = fields.GetTypeSpecRef(fieldDef);
        }

        var result = new ResolvedPath(currentTypeSpec, node);
        resolvedPaths.Add(row, result);
        return result;
    }

    private static PathRootKey GetPathRootKey(uint typeSpecRef,
        TrRmTypeSpecTableReader typeSpecs, TrRmTypeDefTableReader types,
        TrRmImportTableReader imports)
    {
        return TryGetNominalType(typeSpecRef, typeSpecs, types, imports,
                out var typeDefRow, out var qualifiers)
            ? new PathRootKey((uint)typeDefRow, false, qualifiers)
            : new PathRootKey(typeSpecRef, true, 0);
    }

    private static void AddDestination(ResolvedPath path,
        HashSet<PathNode> destinations,
        Dictionary<PathNode, int> descendants)
    {
        var node = path.PathNode;
        if (node is null)
        {
            throw InvalidCopyOrigin();
        }

        if (destinations.Contains(node)
            || descendants.TryGetValue(node, out var descendantCount)
                && descendantCount != 0)
        {
            throw InvalidCopyOrigin();
        }

        for (var ancestor = node.Parent; ancestor is not null;
            ancestor = ancestor.Parent)
        {
            if (destinations.Contains(ancestor))
            {
                throw InvalidCopyOrigin();
            }
        }

        if (!destinations.Add(node))
        {
            throw InvalidCopyOrigin();
        }

        for (var ancestor = node.Parent; ancestor is not null;
            ancestor = ancestor.Parent)
        {
            descendants[ancestor] = descendants.TryGetValue(ancestor,
                out var count) ? checked(count + 1) : 1;
        }
    }

    private static PathNode GetOriginPathNode(uint pathRow, SourceKey source,
        ResolvedPath path, Dictionary<SourceKey, PathNode> wholeSourcePaths)
    {
        if (pathRow != 0)
        {
            return path.PathNode ?? throw InvalidCopyOrigin();
        }

        if (!wholeSourcePaths.TryGetValue(source, out var node))
        {
            node = new PathNode(null);
            wholeSourcePaths.Add(source, node);
        }

        return node;
    }

    private static int GetFieldDefRow(uint fieldReference,
        TrRmFieldDefTableReader fields, TrRmImportTableReader imports)
    {
        var kind = fieldReference >> 24;
        var row = checked((int)(fieldReference & 0x00FF_FFFF));
        if (kind == 0x14)
        {
            return row;
        }

        if (kind != 0x1C)
        {
            throw InvalidCopyOrigin();
        }

        var expectedDefinition = imports.GetExpectedDefinitionForImportRow(row);
        if ((expectedDefinition >> 24) != 0x14)
        {
            throw InvalidCopyOrigin();
        }

        var fieldDefRow = checked((int)(expectedDefinition & 0x00FF_FFFF));
        _ = fields.GetOwnerTypeDef(fieldDefRow);
        return fieldDefRow;
    }

    private static bool AreEquivalentPathRoots(uint left, uint right,
        TrRmTypeSpecTableReader typeSpecs, TrRmTypeDefTableReader types,
        TrRmImportTableReader imports)
    {
        return TryGetNominalType(left, typeSpecs, types, imports,
                   out var leftType, out var leftQualifiers)
            && TryGetNominalType(right, typeSpecs, types, imports,
                   out var rightType, out var rightQualifiers)
            && leftType == rightType && leftQualifiers == rightQualifiers;
    }

    private static bool TryGetNominalType(uint typeSpecRef,
        TrRmTypeSpecTableReader typeSpecs, TrRmTypeDefTableReader types,
        TrRmImportTableReader imports, out int typeDefRow,
        out byte qualifiers)
    {
        var reference = typeSpecs.GetTypeSpecRef(typeSpecRef);
        if (!reference.IsRow || reference.Form is not (0x02 or 0x03))
        {
            typeDefRow = 0;
            qualifiers = 0;
            return false;
        }

        typeDefRow = typeSpecs.GetNominalTypeDefRow(
            typeSpecRef, types, imports);
        qualifiers = reference.Qualifiers;
        return true;
    }

    private static bool IsInlineStruct(int typeDefRow,
        TrRmTypeDefTableReader types, bool[] unionTypes,
        TrRmLayoutTableReader layouts)
    {
        if (typeDefRow <= 0 || typeDefRow >= unionTypes.Length
            || ((types.GetFlags(typeDefRow) >> 2) & 0x03) != 1
            || unionTypes[typeDefRow])
        {
            return false;
        }

        var (first, count) = types.GetLayoutRange(typeDefRow);
        for (var layout = first; layout < first + count; layout++)
        {
            if (layouts.GetLayoutKind(layout) is not (0 or 1 or 2 or 3))
            {
                return false;
            }
        }

        return true;
    }

    private static bool AreEquivalentTypeSpecs(uint left, uint right,
        TrRmTypeSpecTableReader typeSpecs, TrRmTypeDefTableReader types,
        TrRmImportTableReader imports, int depth,
        bool allowInitQualifier = false)
    {
        if (depth > SemanticVerifier.MaximumDepth)
        {
            throw InvalidCopyOrigin();
        }

        var leftReference = typeSpecs.GetTypeSpecRef(left,
            allowInitQualifier: allowInitQualifier);
        var rightReference = typeSpecs.GetTypeSpecRef(right,
            allowInitQualifier: allowInitQualifier);
        if (leftReference.Form != rightReference.Form
            || leftReference.Qualifiers != rightReference.Qualifiers)
        {
            return false;
        }

        if (leftReference.Form is 0x02 or 0x03)
        {
            return TryGetNominalType(left, typeSpecs, types, imports,
                       out var leftType, out _)
                && TryGetNominalType(right, typeSpecs, types, imports,
                       out var rightType, out _)
                && leftType == rightType;
        }

        var leftRow = leftReference.IsRow
            ? typeSpecs.GetRow(leftReference.Row) : default;
        var rightRow = rightReference.IsRow
            ? typeSpecs.GetRow(rightReference.Row) : default;
        switch (leftReference.Form)
        {
            case 0x01:
                return leftReference.TypeCode == rightReference.TypeCode
                    && leftReference.ArgumentCode == rightReference.ArgumentCode;
            case 0x04:
                return leftReference.IsRow && rightReference.IsRow
                    && Read(leftRow, 2) == Read(rightRow, 2);
            case 0x05:
            case 0x0D:
                return leftReference.IsRow && rightReference.IsRow
                    && leftRow.SequenceEqual(rightRow);
            case >= 0x06 and <= 0x0B:
            case 0x0E:
                return leftReference.IsRow && rightReference.IsRow
                    && AreEquivalentTypeSpecs(Read(leftRow, 2), Read(rightRow, 2),
                        typeSpecs, types, imports, depth + 1);
            case 0x0C:
                return leftReference.IsRow && rightReference.IsRow
                    && leftRow[2] == rightRow[2]
                    && AreEquivalentTypeSpecs(Read(leftRow, 3), Read(rightRow, 3),
                        typeSpecs, types, imports, depth + 1,
                        allowInitQualifier: leftRow[2] == 0x8A);
            case 0x0F:
                return true;
            default:
                return false;
        }
    }

    private static uint Read(ReadOnlySpan<byte> data, int offset)
        => BinaryPrimitives.ReadUInt32LittleEndian(
            data.Slice(offset, sizeof(uint)));

    private static InvalidDataException InvalidCopyOrigin()
        => new("Invalid TRRM CopyOrigin contract.");

    private readonly record struct PathRootKey(
        uint Identity, bool IsTypeSpecRef, byte Qualifiers);

    private readonly record struct SourceKey(byte Kind, byte View, uint Ordinal);

    private readonly record struct OriginKey(SourceKey Source, PathNode Path);

    private sealed record ResolvedPath(uint ValueTypeSpec, PathNode? PathNode);

    private sealed class PathNode(PathNode? parent)
    {
        internal PathNode? Parent { get; } = parent;
        internal Dictionary<int, PathNode> Children { get; } = [];
    }
}
