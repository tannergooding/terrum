// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;

namespace Terrum.CodeAnalysis.Modules;

/// <summary>Validates Standard TypeSpec native function-pointer anchors.</summary>
internal readonly ref struct TrRmNativeFunctionPointerAnchorTableReader
{
    private readonly int[] _methodByTypeSpecRow;
    private readonly byte[] _conventionByTypeSpecRow;
    private readonly bool[] _isAnchorMethodDef;
    private readonly TrRmTypeSpecTableReader _typeSpecs;
    private readonly TrRmTypeDefTableReader _typeDefs;
    private readonly TrRmGenericParamTableReader _genericParams;
    private readonly TrRmMethodDefTableReader _methods;
    private readonly TrRmMethodSigTableReader _signatures;
    private readonly TrRmParamTableReader _parameters;
    private readonly TrRmContractTableReader _contracts;
    private readonly TrRmImportTableReader _imports;
    private readonly TrRmConstantSourceTableReader _constantSources;
    private readonly TrRmFrozenValueNodeTableReader _frozenValueNodes;
    private readonly TrRmBlobTableReader _blobs;
    private readonly byte[] _predicateSelectorKinds;

    internal TrRmNativeFunctionPointerAnchorTableReader(
        TrRmTypeSpecTableReader typeSpecs, TrRmTypeDefTableReader typeDefs,
        TrRmGenericParamTableReader genericParams, TrRmMethodDefTableReader methods,
        TrRmMethodSigTableReader signatures, TrRmParamTableReader parameters,
        TrRmContractTableReader contracts, TrRmImportTableReader imports,
        TrRmConstantSourceTableReader constantSources,
        TrRmFrozenValueNodeTableReader frozenValueNodes, TrRmBlobTableReader blobs,
        TrRmFeatureTableReader features, TrRmArtifactSchema schema,
        byte[] predicateSelectorKinds,
        TrRmModuleTableReader modules,
        TrRmExportTableReader exports, TrRmPropertyDefTableReader properties,
        TrRmInterfaceMethodTableReader interfaceMethods,
        TrRmNativeImportTableReader nativeImports,
        TrRmNativeExportTableReader nativeExports)
    {
        _typeSpecs = typeSpecs;
        _typeDefs = typeDefs;
        _genericParams = genericParams;
        _methods = methods;
        _signatures = signatures;
        _parameters = parameters;
        _contracts = contracts;
        _imports = imports;
        _constantSources = constantSources;
        _frozenValueNodes = frozenValueNodes;
        _blobs = blobs;
        _predicateSelectorKinds = predicateSelectorKinds;
        _methodByTypeSpecRow = new int[typeSpecs.Count + 1];
        _conventionByTypeSpecRow = new byte[typeSpecs.Count + 1];
        _isAnchorMethodDef = new bool[methods.Count + 1];
        var types = typeSpecs;
        for (var typeSpecRow = 1; typeSpecRow <= typeSpecs.Count; typeSpecRow++)
        {
            if (!types.TryGetNativeFunctionPointerAnchor(typeSpecRow,
                out var callingConvention, out var methodDefRow))
            {
                continue;
            }

            var signature = methods.GetSignature(methodDefRow);
            var ownerModule = methods.GetOwnerModule(methodDefRow);
            if (!TrRmFeatureManifestValidator.Requires(features, modules,
                    ownerModule,
                    TrRmFeatureTableReader.NativeBoundaryContractsFeatureId,
                    schema)
                || methods.GetBodyRow(methodDefRow) != 0
                || signatures.GetReceiverTypeSpec(signature) != 0
                || signatures.GetGenericCount(signature) != 0
                || contracts.HasNativeCallableContract(methodDefRow)
                || contracts.IsCleanupMethod(methodDefRow)
                || modules.GetEntryMethod(ownerModule) == methodDefRow
                || imports.IsExpectedDefinition(
                    0x1600_0000u | (uint)methodDefRow)
                || exports.HasExportForMethod(methodDefRow)
                || nativeImports.HasImportForMethod(methodDefRow)
                || nativeExports.HasExportForMethod(methodDefRow)
                || HasPropertyAccessor(properties, methodDefRow)
                || HasInterfaceMethod(interfaceMethods, methodDefRow)
                || HasUnsupportedContract(contracts, methods, methodDefRow))
            {
                throw InvalidAnchor();
            }

            _methodByTypeSpecRow[typeSpecRow] = methodDefRow;
            _conventionByTypeSpecRow[typeSpecRow] = callingConvention;
            _isAnchorMethodDef[methodDefRow] = true;
        }

        ValidateAnchorCycles();
    }

    internal bool IsAnchorMethodDef(int methodDefRow)
        => _isAnchorMethodDef is not null && methodDefRow > 0
            && methodDefRow < _isAnchorMethodDef.Length
            && _isAnchorMethodDef[methodDefRow];

    internal bool HasAnchorForTypeSpec(int typeSpecRow)
        => _methodByTypeSpecRow is not null && typeSpecRow > 0
            && typeSpecRow < _methodByTypeSpecRow.Length
            && _methodByTypeSpecRow[typeSpecRow] != 0;

    internal int GetAnchorMethodDefRow(int typeSpecRow)
        => HasAnchorForTypeSpec(typeSpecRow)
            ? _methodByTypeSpecRow[typeSpecRow] : throw InvalidAnchor();

    internal byte GetCallingConvention(int typeSpecRow)
        => HasAnchorForTypeSpec(typeSpecRow)
            ? _conventionByTypeSpecRow[typeSpecRow] : throw InvalidAnchor();

    internal bool AreEquivalentTypeSpecRefs(uint left, uint right)
        => AreEquivalentTypeSpecRefs(left, right, false,
            new HashSet<(uint Left, uint Right, bool AllowInitQualifier)>(),
            new HashSet<(int Left, int Right)>());

    private bool AreEquivalentTypeSpecRefs(uint left, uint right,
        bool allowInitQualifier,
        HashSet<(uint Left, uint Right, bool AllowInitQualifier)> activeTypes,
        HashSet<(int Left, int Right)> activeAnchors)
    {
        var leftType = _typeSpecs.GetTypeSpecRef(left,
            allowInitQualifier: allowInitQualifier);
        var rightType = _typeSpecs.GetTypeSpecRef(right,
            allowInitQualifier: allowInitQualifier);
        if (leftType.Qualifiers != rightType.Qualifiers)
        {
            return false;
        }

        if (leftType.Form == 0x0C && rightType.Form == 0x01
            && rightType.TypeCode >= 0x80)
        {
            return EquivalentIndexedAndInlineFamily(leftType, rightType,
                activeTypes, activeAnchors);
        }

        if (rightType.Form == 0x0C && leftType.Form == 0x01
            && leftType.TypeCode >= 0x80)
        {
            return EquivalentIndexedAndInlineFamily(rightType, leftType,
                activeTypes, activeAnchors);
        }

        var nominalReferencePair = leftType.Form is 0x02 or 0x03
            && rightType.Form is 0x02 or 0x03;
        if ((leftType.Form != rightType.Form && !nominalReferencePair)
            || !activeTypes.Add((left, right, allowInitQualifier)))
        {
            return false;
        }

        try
        {
            var leftRow = leftType.IsRow ? _typeSpecs.GetRow(leftType.Row) : default;
            var rightRow = rightType.IsRow ? _typeSpecs.GetRow(rightType.Row) : default;
            return (nominalReferencePair ? (byte)0x02 : leftType.Form) switch
            {
                0x01 => leftType.TypeCode == rightType.TypeCode
                    && leftType.ArgumentCode == rightType.ArgumentCode,
                0x02 or 0x03
                    => _typeSpecs.GetNominalTypeDefRow(left, _typeDefs, _imports)
                        == _typeSpecs.GetNominalTypeDefRow(right, _typeDefs, _imports),
                0x04 => leftType.IsRow && rightType.IsRow
                    && Read(leftRow, 2) == Read(rightRow, 2),
                0x05 => EquivalentConstructedType(leftType, rightType,
                    left, right, activeTypes, activeAnchors),
                (>= 0x06 and <= 0x0B) or 0x0E
                    => leftType.IsRow && rightType.IsRow
                        && AreEquivalentTypeSpecRefs(Read(leftRow, 2),
                            Read(rightRow, 2), false, activeTypes, activeAnchors),
                0x0C => leftType.IsRow && rightType.IsRow
                        && leftRow[2] == rightRow[2]
                        && AreEquivalentTypeSpecRefs(Read(leftRow, 3),
                            Read(rightRow, 3), leftRow[2] == 0x8A,
                            activeTypes, activeAnchors),
                0x0D => leftType.IsRow && rightType.IsRow
                        && EquivalentAnchors(leftType.Row, rightType.Row,
                            activeTypes, activeAnchors),
                0x0F => true,
                _ => false,
            };
        }
        finally
        {
            _ = activeTypes.Remove((left, right, allowInitQualifier));
        }
    }

    private bool EquivalentIndexedAndInlineFamily(TrRmTypeSpecRefInfo indexed,
        TrRmTypeSpecRefInfo inline,
        HashSet<(uint Left, uint Right, bool AllowInitQualifier)> activeTypes,
        HashSet<(int Left, int Right)> activeAnchors)
    {
        var row = _typeSpecs.GetRow(indexed.Row);
        return row[2] == inline.TypeCode
            && AreEquivalentTypeSpecRefs(Read(row, 3),
                0xF200_0000u | inline.ArgumentCode, row[2] == 0x8A,
                activeTypes, activeAnchors);
    }

    private bool EquivalentConstructedType(TrRmTypeSpecRefInfo leftType,
        TrRmTypeSpecRefInfo rightType, uint left, uint right,
        HashSet<(uint Left, uint Right, bool AllowInitQualifier)> activeTypes,
        HashSet<(int Left, int Right)> activeAnchors)
    {
        if (!leftType.IsRow || !rightType.IsRow)
        {
            return false;
        }

        var leftDefinition = _typeSpecs.GetNominalTypeDefRow(left, _typeDefs, _imports);
        var rightDefinition = _typeSpecs.GetNominalTypeDefRow(right, _typeDefs, _imports);
        if (leftDefinition != rightDefinition)
        {
            return false;
        }

        var (formalFirst, formalCount) = _typeDefs.GetGenericRange(leftDefinition);
        var leftCount = _typeSpecs.GetConstructedArgumentCount(leftType.Row);
        var rightCount = _typeSpecs.GetConstructedArgumentCount(rightType.Row);
        if (leftCount != formalCount || rightCount != formalCount)
        {
            return false;
        }

        for (var ordinal = 0; ordinal < formalCount; ordinal++)
        {
            var formal = formalFirst + ordinal;
            var leftArgument = _typeSpecs.GetConstructedArgument(leftType.Row, ordinal);
            var rightArgument = _typeSpecs.GetConstructedArgument(rightType.Row, ordinal);
            switch (_genericParams.GetFormalKind(formal))
            {
                case 1:
                    if (!AreEquivalentTypeSpecRefs(leftArgument, rightArgument,
                        false, activeTypes, activeAnchors))
                    {
                        return false;
                    }
                    break;
                case 2:
                    if (!EquivalentConstantArguments(formal, leftArgument,
                        rightArgument, activeTypes, activeAnchors))
                    {
                        return false;
                    }
                    break;
                default:
                    return false;
            }
        }

        return true;
    }

    private bool EquivalentConstantArguments(int formal, uint left, uint right,
        HashSet<(uint Left, uint Right, bool AllowInitQualifier)> activeTypes,
        HashSet<(int Left, int Right)> activeAnchors)
    {
        if (_constantSources.GetKind(checked((int)left)) != 1
            || _constantSources.GetKind(checked((int)right)) != 1)
        {
            return false;
        }

        var leftNode = checked((int)_constantSources.GetPayloadReference(checked((int)left)));
        var rightNode = checked((int)_constantSources.GetPayloadReference(checked((int)right)));
        if (_frozenValueNodes.GetKind(leftNode) != 1
            || _frozenValueNodes.GetKind(rightNode) != 1
            || !AreEquivalentTypeSpecRefs(_genericParams.GetConstantTypeSpecRef(formal),
                _frozenValueNodes.GetTypeSpecRef(leftNode), false,
                activeTypes, activeAnchors)
            || !AreEquivalentTypeSpecRefs(_genericParams.GetConstantTypeSpecRef(formal),
                _frozenValueNodes.GetTypeSpecRef(rightNode), false,
                activeTypes, activeAnchors))
        {
            return false;
        }

        var leftType = _typeSpecs.GetTypeSpecRef(
            _frozenValueNodes.GetTypeSpecRef(leftNode));
        if (leftType.Form != 0x01 || leftType.Qualifiers != 0
            || !TrRmFrozenValueNodeTableReader.IsRegisteredScalarTypeCode(
                leftType.TypeCode))
        {
            return false;
        }

        return _frozenValueNodes.GetScalarPayload(leftNode, _blobs)
            .SequenceEqual(_frozenValueNodes.GetScalarPayload(rightNode, _blobs));
    }

    private bool EquivalentAnchors(int leftTypeSpecRow, int rightTypeSpecRow,
        HashSet<(uint Left, uint Right, bool AllowInitQualifier)> activeTypes,
        HashSet<(int Left, int Right)> activeAnchors)
    {
        if (!HasAnchorForTypeSpec(leftTypeSpecRow)
            || !HasAnchorForTypeSpec(rightTypeSpecRow))
        {
            return false;
        }

        var leftMethod = GetAnchorMethodDefRow(leftTypeSpecRow);
        var rightMethod = GetAnchorMethodDefRow(rightTypeSpecRow);
        var anchorKey = (leftMethod, rightMethod);
        if (_conventionByTypeSpecRow[leftTypeSpecRow]
                != _conventionByTypeSpecRow[rightTypeSpecRow]
            || !activeAnchors.Add(anchorKey))
        {
            return false;
        }

        try
        {
            var leftSignature = _methods.GetSignature(leftMethod);
            var rightSignature = _methods.GetSignature(rightMethod);
            if (!AreEquivalentTypeSpecRefs(_signatures.GetResultTypeSpec(leftSignature),
                    _signatures.GetResultTypeSpec(rightSignature), false,
                    activeTypes, activeAnchors))
            {
                return false;
            }

            var (leftFirst, leftCount) = _signatures.GetParamRange(leftSignature);
            var (rightFirst, rightCount) = _signatures.GetParamRange(rightSignature);
            if (leftCount != rightCount)
            {
                return false;
            }
            for (var ordinal = 0; ordinal < leftCount; ordinal++)
            {
                var leftParameter = leftFirst + ordinal;
                var rightParameter = rightFirst + ordinal;
                if (_parameters.GetMode(leftParameter) != _parameters.GetMode(rightParameter)
                    || !AreEquivalentTypeSpecRefs(
                        _parameters.GetTypeSpec(leftParameter),
                        _parameters.GetTypeSpec(rightParameter), false,
                        activeTypes, activeAnchors))
                {
                    return false;
                }
            }

            return EquivalentContracts(leftMethod, rightMethod);
        }
        finally
        {
            _ = activeAnchors.Remove(anchorKey);
        }
    }

    private bool EquivalentContracts(int leftMethod, int rightMethod)
    {
        if (_contracts.GetEffectsMask(leftMethod)
            != _contracts.GetEffectsMask(rightMethod))
        {
            return false;
        }

        var leftPredicates = _contracts.GetPredicateGraphContracts(leftMethod);
        var rightPredicates = _contracts.GetPredicateGraphContracts(rightMethod);
        if (leftPredicates.Length != rightPredicates.Length)
        {
            return false;
        }
        foreach (var leftPredicate in leftPredicates)
        {
            var identity = GetPredicateIdentity(leftPredicate);
            var found = false;
            foreach (var rightPredicate in rightPredicates)
            {
                if (identity == GetPredicateIdentity(rightPredicate))
                {
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                return false;
            }
        }

        var leftOutputs = _contracts.GetOutputStateContracts(leftMethod);
        var rightOutputs = _contracts.GetOutputStateContracts(rightMethod);
        if (leftOutputs.Length != rightOutputs.Length)
        {
            return false;
        }
        foreach (var leftOutput in leftOutputs)
        {
            var identity = (leftOutput.ParameterOrdinal, leftOutput.State,
                GetPredicateIdentity(leftMethod, leftOutput.PredicateGraphRow));
            var found = false;
            foreach (var rightOutput in rightOutputs)
            {
                if (identity == (rightOutput.ParameterOrdinal, rightOutput.State,
                    GetPredicateIdentity(rightMethod, rightOutput.PredicateGraphRow)))
                {
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                return false;
            }
        }

        var leftDependencies = _contracts.GetDependencyContracts(leftMethod);
        var rightDependencies = _contracts.GetDependencyContracts(rightMethod);
        if (leftDependencies.Length != rightDependencies.Length)
        {
            return false;
        }
        foreach (var dependency in leftDependencies)
        {
            var found = false;
            foreach (var candidate in rightDependencies)
            {
                if (dependency == candidate)
                {
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                return false;
            }
        }

        return true;
    }

    private (byte Phase, uint Ordinal, byte SelectorKind) GetPredicateIdentity(
        TrRmPredicateGraphContract predicate)
    {
        var graph = predicate.PredicateGraphRow;
        if (graph <= 0 || graph >= _predicateSelectorKinds.Length
            || _predicateSelectorKinds[graph] == 0)
        {
            throw InvalidAnchor();
        }

        return (predicate.PhaseTag, predicate.PhaseLocalOrdinal,
            _predicateSelectorKinds[graph]);
    }

    private (byte Phase, uint Ordinal, byte SelectorKind) GetPredicateIdentity(
        int method, int graphRow)
    {
        foreach (var predicate in _contracts.GetPredicateGraphContracts(method))
        {
            if (predicate.PredicateGraphRow == graphRow)
            {
                return GetPredicateIdentity(predicate);
            }
        }

        throw InvalidAnchor();
    }

    private void ValidateAnchorCycles()
    {
        var activeTypeRows = new HashSet<int>();
        var activeMethods = new HashSet<int>();
        for (var typeSpecRow = 1; typeSpecRow < _methodByTypeSpecRow.Length;
             typeSpecRow++)
        {
            if (_methodByTypeSpecRow[typeSpecRow] != 0)
            {
                VisitTypeSpec((uint)typeSpecRow, activeTypeRows, activeMethods);
            }
        }
    }

    private void VisitTypeSpec(uint typeSpecRef, HashSet<int> activeTypeRows,
        HashSet<int> activeMethods)
    {
        var type = _typeSpecs.GetTypeSpecRef(typeSpecRef,
            allowInitQualifier: true);
        if (!type.IsRow)
        {
            return;
        }
        if (!activeTypeRows.Add(type.Row))
        {
            throw InvalidAnchor();
        }

        try
        {
            var row = _typeSpecs.GetRow(type.Row);
            switch (type.Form)
            {
                case 0x05:
                    var typeDef = _typeSpecs.GetNominalTypeDefRow(typeSpecRef,
                        _typeDefs, _imports);
                    var (formalFirst, formalCount) = _typeDefs.GetGenericRange(typeDef);
                    if (_typeSpecs.GetConstructedArgumentCount(type.Row) != formalCount)
                    {
                        throw InvalidAnchor();
                    }
                    for (var ordinal = 0; ordinal < formalCount; ordinal++)
                    {
                        var formal = formalFirst + ordinal;
                        if (_genericParams.GetFormalKind(formal) == 1)
                        {
                            VisitTypeSpec(_typeSpecs.GetConstructedArgument(type.Row,
                                ordinal), activeTypeRows, activeMethods);
                        }
                        else if (_genericParams.GetFormalKind(formal) == 2)
                        {
                            VisitTypeSpec(_genericParams.GetConstantTypeSpecRef(formal),
                                activeTypeRows, activeMethods);
                        }
                        else
                        {
                            throw InvalidAnchor();
                        }
                    }
                    break;
                case >= 0x06 and <= 0x0B:
                case 0x0E:
                    VisitTypeSpec(Read(row, 2), activeTypeRows, activeMethods);
                    break;
                case 0x0C:
                    VisitTypeSpec(Read(row, 3), activeTypeRows, activeMethods);
                    break;
                case 0x0D:
                    VisitAnchorMethod(GetAnchorMethodDefRow(type.Row),
                        activeTypeRows, activeMethods);
                    break;
            }
        }
        finally
        {
            _ = activeTypeRows.Remove(type.Row);
        }
    }

    private void VisitAnchorMethod(int method, HashSet<int> activeTypeRows,
        HashSet<int> activeMethods)
    {
        if (!activeMethods.Add(method))
        {
            throw InvalidAnchor();
        }

        try
        {
            var signature = _methods.GetSignature(method);
            VisitTypeSpec(_signatures.GetResultTypeSpec(signature),
                activeTypeRows, activeMethods);
            var (first, count) = _signatures.GetParamRange(signature);
            for (var ordinal = 0; ordinal < count; ordinal++)
            {
                VisitTypeSpec(_parameters.GetTypeSpec(first + ordinal),
                    activeTypeRows, activeMethods);
            }
        }
        finally
        {
            _ = activeMethods.Remove(method);
        }
    }

    private static bool HasPropertyAccessor(TrRmPropertyDefTableReader properties,
        int methodDefRow)
    {
        for (var property = 1; property <= properties.Count; property++)
        {
            if (properties.GetGetterMethod(property) == methodDefRow)
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasInterfaceMethod(
        TrRmInterfaceMethodTableReader interfaceMethods, int methodDefRow)
    {
        for (var row = 1; row <= interfaceMethods.Count; row++)
        {
            if (interfaceMethods.GetMethodDefRow(row) == methodDefRow)
            {
                return true;
            }
        }

        return false;
    }

    private static uint Read(ReadOnlySpan<byte> data, int offset)
        => BinaryPrimitives.ReadUInt32LittleEndian(
            data.Slice(offset, sizeof(uint)));

    private static bool HasUnsupportedContract(TrRmContractTableReader contracts,
        TrRmMethodDefTableReader methods, int methodDefRow)
    {
        var (first, count) = methods.GetContractRange(methodDefRow);
        for (var offset = 0; offset < count; offset++)
        {
            var kind = BinaryPrimitives.ReadUInt16LittleEndian(
                contracts.GetRow(first + offset)[4..]);
            if (kind is not (1 or 2 or 4 or 0x0B))
            {
                return true;
            }
        }

        foreach (var predicate in contracts.GetPredicateGraphContracts(methodDefRow))
        {
            if (predicate.PhaseTag != 3)
            {
                return true;
            }
        }

        return false;
    }

    private static InvalidDataException InvalidAnchor()
        => new("Invalid Standard TRRM native function-pointer anchor.");
}
