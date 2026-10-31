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
    private readonly TrRmEnumDefTableReader _enumDefs;
    private readonly TrRmGenericParamTableReader _genericParams;
    private readonly TrRmMethodDefTableReader _methods;
    private readonly TrRmMethodSigTableReader _signatures;
    private readonly TrRmParamTableReader _parameters;
    private readonly TrRmContractTableReader _contracts;
    private readonly TrRmImportTableReader _imports;
    private readonly TrRmConstantSourceTableReader _constantSources;
    private readonly TrRmFrozenValueNodeTableReader _frozenValueNodes;
    private readonly TrRmBlobTableReader _blobs;
    private readonly TrRmFeatureTableReader _features;
    private readonly TrRmModuleTableReader _modules;
    private readonly byte[] _predicateSelectorKinds;
    private readonly TrRmA3LayoutResourceLedger? _resourceLedger;

    internal TrRmNativeFunctionPointerAnchorTableReader(
        TrRmTypeSpecTableReader typeSpecs, TrRmTypeDefTableReader typeDefs,
        TrRmEnumDefTableReader enumDefs,
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
        TrRmNativeExportTableReader nativeExports,
        TrRmA3LayoutResourceLedger? resourceLedger = null)
    {
        _typeSpecs = typeSpecs;
        _typeDefs = typeDefs;
        _enumDefs = enumDefs;
        _genericParams = genericParams;
        _methods = methods;
        _signatures = signatures;
        _parameters = parameters;
        _contracts = contracts;
        _imports = imports;
        _constantSources = constantSources;
        _frozenValueNodes = frozenValueNodes;
        _blobs = blobs;
        _features = features;
        _modules = modules;
        _predicateSelectorKinds = predicateSelectorKinds;
        _resourceLedger = resourceLedger;
        var typeSpecEntries = checked((ulong)typeSpecs.Count + 1UL);
        var typeSpecStateWork = checked(typeSpecEntries * 2UL);
        var methodStateWork = checked((ulong)methods.Count + 1UL);
        _resourceLedger?.ConsumeWork(
            checked(typeSpecStateWork + methodStateWork));
        _methodByTypeSpecRow = new int[typeSpecs.Count + 1];
        _conventionByTypeSpecRow = new byte[typeSpecs.Count + 1];
        _isAnchorMethodDef = new bool[methods.Count + 1];
        var types = typeSpecs;
        for (var typeSpecRow = 1; typeSpecRow <= typeSpecs.Count; typeSpecRow++)
        {
            _resourceLedger?.ConsumeWork();
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

            var (firstParameter, parameterCount) = signatures.GetParamRange(signature);
            for (var ordinal = 0; ordinal < parameterCount; ordinal++)
            {
                if (parameters.GetMode(firstParameter + ordinal) != 0)
                {
                    throw InvalidAnchor();
                }
            }

            _resourceLedger?.ConsumeWork(3);
            _methodByTypeSpecRow[typeSpecRow] = methodDefRow;
            _conventionByTypeSpecRow[typeSpecRow] = callingConvention;
            _isAnchorMethodDef[methodDefRow] = true;
        }

        ValidateAnchorCycles();
    }

    internal bool IsAnchorMethodDef(int methodDefRow)
    {
        _resourceLedger?.ConsumeWork(2);
        return _isAnchorMethodDef is not null && methodDefRow > 0
            && methodDefRow < _isAnchorMethodDef.Length
            && _isAnchorMethodDef[methodDefRow];
    }

    internal bool HasAnchorForTypeSpec(int typeSpecRow)
    {
        _resourceLedger?.ConsumeWork(2);
        return _methodByTypeSpecRow is not null && typeSpecRow > 0
            && typeSpecRow < _methodByTypeSpecRow.Length
            && _methodByTypeSpecRow[typeSpecRow] != 0;
    }

    internal int GetAnchorMethodDefRow(int typeSpecRow)
    {
        if (!HasAnchorForTypeSpec(typeSpecRow))
        {
            throw InvalidAnchor();
        }

        _resourceLedger?.ConsumeWork();
        return _methodByTypeSpecRow[typeSpecRow];
    }

    internal byte GetCallingConvention(int typeSpecRow)
    {
        if (!HasAnchorForTypeSpec(typeSpecRow))
        {
            throw InvalidAnchor();
        }

        _resourceLedger?.ConsumeWork();
        return _conventionByTypeSpecRow[typeSpecRow];
    }

    internal bool AreEquivalentAnchorContracts(int methodRow,
        TrRmNativeFunctionPointerAnchorTableReader other,
        int otherMethodRow)
    {
        var leftNativeParameters = _contracts.GetNativeParameters(methodRow);
        var rightNativeParameters
            = other._contracts.GetNativeParameters(otherMethodRow);
        var leftDependencies = _contracts.GetDependencyContracts(methodRow);
        var rightDependencies
            = other._contracts.GetDependencyContracts(otherMethodRow);
        _resourceLedger?.ConsumeWork(checked(
            (ulong)(leftNativeParameters.Length + rightNativeParameters.Length
                + leftDependencies.Length + rightDependencies.Length) + 1UL));
        _resourceLedger?.ConsumeWork(2);
        var leftNativeParameterSet
            = new HashSet<TrRmNativeParameterContract>(leftNativeParameters);
        var rightNativeParameterSet
            = new HashSet<TrRmNativeParameterContract>(rightNativeParameters);
        _resourceLedger?.ConsumeWork(checked(
            (ulong)(leftDependencies.Length + rightDependencies.Length) + 2UL));
        var leftDependencySet
            = new HashSet<TrRmDependencyContract>(leftDependencies);
        var rightDependencySet
            = new HashSet<TrRmDependencyContract>(rightDependencies);
        return _contracts.GetEffectsMask(methodRow)
                == other._contracts.GetEffectsMask(otherMethodRow)
            && SamePredicateContracts(methodRow, other, otherMethodRow)
            && SameOutputStateContracts(methodRow, other, otherMethodRow)
            && leftNativeParameters.Length == rightNativeParameters.Length
            && leftNativeParameterSet.SetEquals(rightNativeParameterSet)
            && leftDependencies.Length == rightDependencies.Length
            && leftDependencySet.SetEquals(rightDependencySet);
    }

    private bool SamePredicateContracts(int methodRow,
        TrRmNativeFunctionPointerAnchorTableReader other,
        int otherMethodRow)
    {
        var left = _contracts.GetPredicateGraphContracts(methodRow);
        var right = other._contracts.GetPredicateGraphContracts(
            otherMethodRow);
        _resourceLedger?.ConsumeWork(checked((ulong)(left.Length + right.Length)
            + 1UL));
        if (left.Length != right.Length)
        {
            return false;
        }

        foreach (var predicate in left)
        {
            _resourceLedger?.ConsumeWork();
            var identity = GetPredicateIdentity(predicate);
            var found = false;
            foreach (var candidate in right)
            {
                _resourceLedger?.ConsumeWork();
                if (identity == other.GetPredicateIdentity(candidate))
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

    private bool SameOutputStateContracts(int methodRow,
        TrRmNativeFunctionPointerAnchorTableReader other,
        int otherMethodRow)
    {
        var left = _contracts.GetOutputStateContracts(methodRow);
        var right = other._contracts.GetOutputStateContracts(otherMethodRow);
        _resourceLedger?.ConsumeWork(checked((ulong)(left.Length + right.Length)
            + 1UL));
        if (left.Length != right.Length)
        {
            return false;
        }

        foreach (var outputState in left)
        {
            _resourceLedger?.ConsumeWork();
            var identity = (outputState.ParameterOrdinal, outputState.State,
                GetPredicateIdentity(methodRow,
                    outputState.PredicateGraphRow));
            var found = false;
            foreach (var candidate in right)
            {
                _resourceLedger?.ConsumeWork();
                if (identity == (candidate.ParameterOrdinal, candidate.State,
                        other.GetPredicateIdentity(otherMethodRow,
                            candidate.PredicateGraphRow)))
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

    internal bool MatchesAddressableMethod(int methodDefRow, int typeSpecRow,
        byte? targetConvention)
    {
        _resourceLedger?.ConsumeWork(4);
        if (!HasAnchorForTypeSpec(typeSpecRow)
            || methodDefRow <= 0 || methodDefRow > _methods.Count
            || IsAnchorMethodDef(methodDefRow)
            || (targetConvention is byte convention
                && convention != GetCallingConvention(typeSpecRow)))
        {
            return false;
        }

        var anchorMethod = GetAnchorMethodDefRow(typeSpecRow);
        var targetSignature = _methods.GetSignature(methodDefRow);
        var anchorSignature = _methods.GetSignature(anchorMethod);
        if (_signatures.GetReceiverTypeSpec(targetSignature) != 0
            || _signatures.GetGenericCount(targetSignature) != 0
            || _signatures.GetReceiverTypeSpec(anchorSignature) != 0
            || _signatures.GetGenericCount(anchorSignature) != 0
            || !AreEquivalentTypeSpecRefs(
                _signatures.GetResultTypeSpec(targetSignature),
                _signatures.GetResultTypeSpec(anchorSignature)))
        {
            return false;
        }

        var (targetFirst, targetCount) = _signatures.GetParamRange(targetSignature);
        var (anchorFirst, anchorCount) = _signatures.GetParamRange(anchorSignature);
        if (targetCount != anchorCount)
        {
            return false;
        }

        for (var ordinal = 0; ordinal < targetCount; ordinal++)
        {
            _resourceLedger?.ConsumeWork(3);
            var targetParameter = targetFirst + ordinal;
            var anchorParameter = anchorFirst + ordinal;
            if (_parameters.GetMode(targetParameter) != 0
                || _parameters.GetMode(anchorParameter) != 0
                || !AreEquivalentTypeSpecRefs(
                    _parameters.GetTypeSpec(targetParameter),
                    _parameters.GetTypeSpec(anchorParameter)))
            {
                return false;
            }
        }

        return ContractsSatisfy(methodDefRow, anchorMethod);
    }

    internal bool AreEquivalentTypeSpecRefs(uint left, uint right)
    {
        _resourceLedger?.ConsumeWork(2);
        return AreEquivalentTypeSpecRefs(left, right, false,
            new HashSet<(uint Left, uint Right, bool AllowInitQualifier)>(),
            new HashSet<(int Left, int Right)>());
    }

    private bool AreEquivalentTypeSpecRefs(uint left, uint right,
        bool allowInitQualifier,
        HashSet<(uint Left, uint Right, bool AllowInitQualifier)> activeTypes,
        HashSet<(int Left, int Right)> activeAnchors)
    {
        using var frame = _resourceLedger is null
            ? default : _resourceLedger.EnterFrame();
        _resourceLedger?.ConsumeWork(2);
        var leftType = _typeSpecs.GetTypeSpecRef(left,
            allowInitQualifier: allowInitQualifier);
        var rightType = _typeSpecs.GetTypeSpecRef(right,
            allowInitQualifier: allowInitQualifier);
        _resourceLedger?.ConsumeWork();
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
        _resourceLedger?.ConsumeWork(2);
        if (leftType.Form != rightType.Form && !nominalReferencePair)
        {
            return false;
        }
        _resourceLedger?.ConsumeWork(2);
        if (!activeTypes.Add((left, right, allowInitQualifier)))
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
            _resourceLedger?.ConsumeWork();
            _ = activeTypes.Remove((left, right, allowInitQualifier));
        }
    }

    private bool EquivalentIndexedAndInlineFamily(TrRmTypeSpecRefInfo indexed,
        TrRmTypeSpecRefInfo inline,
        HashSet<(uint Left, uint Right, bool AllowInitQualifier)> activeTypes,
        HashSet<(int Left, int Right)> activeAnchors)
    {
        _resourceLedger?.ConsumeWork();
        var row = _typeSpecs.GetRow(indexed.Row);
        _resourceLedger?.ConsumeWork();
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
            _resourceLedger?.ConsumeWork();
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
        _resourceLedger?.ConsumeWork(2);
        if (_constantSources.GetKind(checked((int)left)) != 1
            || _constantSources.GetKind(checked((int)right)) != 1)
        {
            return false;
        }

        var expectedTypeSpecRef = _genericParams.GetConstantTypeSpecRef(formal);
        if (!AreEquivalentTypeSpecRefs(expectedTypeSpecRef,
                _frozenValueNodes.GetTypeSpecRef(checked((int)_constantSources
                    .GetPayloadReference(checked((int)left)))), false,
                activeTypes, activeAnchors)
            || !AreEquivalentTypeSpecRefs(expectedTypeSpecRef,
                _frozenValueNodes.GetTypeSpecRef(checked((int)_constantSources
                    .GetPayloadReference(checked((int)right)))), false,
                activeTypes, activeAnchors))
        {
            return false;
        }

        var owner = _genericParams.GetOwnerDefId(formal);
        var ownerRow = checked((int)(owner & 0x00FF_FFFF));
        var ownerModule = (byte)(owner >> 24) switch
        {
            0x13 => _typeDefs.GetOwnerModule(ownerRow),
            0x16 => _methods.GetOwnerModule(ownerRow),
            _ => throw InvalidAnchor(),
        };
        var leftPayload = TrRmConstantSourceValueValidator.GetFrozenScalarPayload(
            checked((int)left), expectedTypeSpecRef, ownerModule,
            _constantSources, _frozenValueNodes, _blobs, _typeSpecs,
            _features, _modules, _typeDefs, _enumDefs, _imports);
        var rightPayload = TrRmConstantSourceValueValidator.GetFrozenScalarPayload(
            checked((int)right), expectedTypeSpecRef, ownerModule,
            _constantSources, _frozenValueNodes, _blobs, _typeSpecs,
            _features, _modules, _typeDefs, _enumDefs, _imports);

        _resourceLedger?.ConsumeWork(checked((ulong)leftPayload.Length
            + (ulong)rightPayload.Length + 1UL));
        return leftPayload.SequenceEqual(rightPayload);
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
        _resourceLedger?.ConsumeWork(2);
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
            _resourceLedger?.ConsumeWork();
            _ = activeAnchors.Remove(anchorKey);
        }
    }

    private bool ContractsSatisfy(int actualMethod, int expectedMethod)
    {
        if (!_contracts.GetNativeOutputs(actualMethod).IsDefaultOrEmpty
            || !SameNativeParameterContracts(actualMethod, expectedMethod))
        {
            return false;
        }

        var actualEffects = _contracts.GetEffectsMask(actualMethod);
        var expectedEffects = _contracts.GetEffectsMask(expectedMethod);
        if ((expectedEffects & 2) != 0 && (actualEffects & 2) == 0)
        {
            return false;
        }

        return SamePredicateContracts(actualMethod, expectedMethod)
            && SameOutputStateContracts(actualMethod, expectedMethod)
            && DependenciesSatisfy(actualMethod, expectedMethod);
    }

    private bool SamePredicateContracts(int leftMethod, int rightMethod)
    {
        var left = _contracts.GetPredicateGraphContracts(leftMethod);
        var right = _contracts.GetPredicateGraphContracts(rightMethod);
        _resourceLedger?.ConsumeWork(checked((ulong)(left.Length + right.Length)
            + 1UL));
        if (left.Length != right.Length)
        {
            return false;
        }

        foreach (var candidate in left)
        {
            _resourceLedger?.ConsumeWork();
            var identity = GetPredicateIdentity(candidate);
            var found = false;
            foreach (var other in right)
            {
                _resourceLedger?.ConsumeWork();
                if (identity == GetPredicateIdentity(other))
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

    private bool SameOutputStateContracts(int leftMethod, int rightMethod)
    {
        var left = _contracts.GetOutputStateContracts(leftMethod);
        var right = _contracts.GetOutputStateContracts(rightMethod);
        _resourceLedger?.ConsumeWork(checked((ulong)(left.Length + right.Length)
            + 1UL));
        if (left.Length != right.Length)
        {
            return false;
        }

        foreach (var candidate in left)
        {
            _resourceLedger?.ConsumeWork();
            var identity = (candidate.ParameterOrdinal, candidate.State,
                GetPredicateIdentity(leftMethod, candidate.PredicateGraphRow));
            var found = false;
            foreach (var other in right)
            {
                _resourceLedger?.ConsumeWork();
                if (identity == (other.ParameterOrdinal, other.State,
                    GetPredicateIdentity(rightMethod, other.PredicateGraphRow)))
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

    private bool SameNativeParameterContracts(int leftMethod, int rightMethod)
    {
        var left = _contracts.GetNativeParameters(leftMethod);
        var right = _contracts.GetNativeParameters(rightMethod);
        _resourceLedger?.ConsumeWork(checked((ulong)(left.Length + right.Length)
            + 3UL));
        var leftSet = new HashSet<TrRmNativeParameterContract>(left);
        var rightSet = new HashSet<TrRmNativeParameterContract>(right);
        return left.Length == right.Length && leftSet.SetEquals(rightSet);
    }

    private bool SameDependencyContracts(int leftMethod, int rightMethod)
    {
        var left = _contracts.GetDependencyContracts(leftMethod);
        var right = _contracts.GetDependencyContracts(rightMethod);
        _resourceLedger?.ConsumeWork(checked((ulong)(left.Length + right.Length)
            + 3UL));
        var leftSet = new HashSet<TrRmDependencyContract>(left);
        var rightSet = new HashSet<TrRmDependencyContract>(right);
        return left.Length == right.Length && leftSet.SetEquals(rightSet);
    }

    private bool DependenciesSatisfy(int actualMethod, int expectedMethod)
    {
        var actual = _contracts.GetDependencyContracts(actualMethod);
        var expected = _contracts.GetDependencyContracts(expectedMethod);
        _resourceLedger?.ConsumeWork(checked(4UL
            + (ulong)actual.Length + (ulong)expected.Length));
        var actualGroups = new HashSet<(byte DestinationKind,
            uint DestinationOrdinal, byte Phase)>();
        var expectedGroups = new HashSet<(byte DestinationKind,
            uint DestinationOrdinal, byte Phase)>();
        foreach (var dependency in actual)
        {
            _resourceLedger?.ConsumeWork(2);
            _ = actualGroups.Add((dependency.DestinationKind,
                dependency.DestinationOrdinal, dependency.Phase));
        }
        foreach (var dependency in expected)
        {
            _resourceLedger?.ConsumeWork(2);
            _ = expectedGroups.Add((dependency.DestinationKind,
                dependency.DestinationOrdinal, dependency.Phase));
        }
        if (!actualGroups.SetEquals(expectedGroups))
        {
            return false;
        }

        foreach (var group in expectedGroups)
        {
            _resourceLedger?.ConsumeWork(4);
            var actualRows = new List<TrRmDependencyContract>();
            var expectedRows = new List<TrRmDependencyContract>();
            foreach (var dependency in actual)
            {
                _resourceLedger?.ConsumeWork();
                if ((dependency.DestinationKind, dependency.DestinationOrdinal,
                    dependency.Phase) == group)
                {
                    actualRows.Add(dependency);
                }
            }
            foreach (var dependency in expected)
            {
                _resourceLedger?.ConsumeWork();
                if ((dependency.DestinationKind, dependency.DestinationOrdinal,
                    dependency.Phase) == group)
                {
                    expectedRows.Add(dependency);
                }
            }

            if (actualRows[0].SourceKind == 0)
            {
                continue;
            }
            if (expectedRows[0].SourceKind == 0)
            {
                return false;
            }

            foreach (var actualDependency in actualRows)
            {
                _resourceLedger?.ConsumeWork();
                var found = false;
                foreach (var expectedDependency in expectedRows)
                {
                    _resourceLedger?.ConsumeWork();
                    if (expectedDependency.SourceKind
                            != actualDependency.SourceKind
                        || expectedDependency.SourceOrdinal
                            != actualDependency.SourceOrdinal)
                    {
                        continue;
                    }

                    found = actualDependency.Relationship
                            == expectedDependency.Relationship
                        || (expectedDependency.Relationship == 3
                            && actualDependency.Relationship is 1 or 2);
                    break;
                }

                if (!found)
                {
                    return false;
                }
            }
        }

        return true;
    }

    private bool EquivalentContracts(int leftMethod, int rightMethod)
        => _contracts.GetEffectsMask(leftMethod)
                == _contracts.GetEffectsMask(rightMethod)
            && SamePredicateContracts(leftMethod, rightMethod)
            && SameOutputStateContracts(leftMethod, rightMethod)
            && SameNativeParameterContracts(leftMethod, rightMethod)
            && SameDependencyContracts(leftMethod, rightMethod);

    private (byte Phase, uint Ordinal, byte SelectorKind) GetPredicateIdentity(
        TrRmPredicateGraphContract predicate)
    {
        var graph = predicate.PredicateGraphRow;
        _resourceLedger?.ConsumeWork(3);
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
            _resourceLedger?.ConsumeWork();
            if (predicate.PredicateGraphRow == graphRow)
            {
                return GetPredicateIdentity(predicate);
            }
        }

        throw InvalidAnchor();
    }

    private void ValidateAnchorCycles()
    {
        _resourceLedger?.ConsumeWork(2);
        var activeTypeRows = new HashSet<int>();
        var activeMethods = new HashSet<int>();
        for (var typeSpecRow = 1; typeSpecRow < _methodByTypeSpecRow.Length;
             typeSpecRow++)
        {
            _resourceLedger?.ConsumeWork();
            if (_methodByTypeSpecRow[typeSpecRow] != 0)
            {
                VisitTypeSpec((uint)typeSpecRow, activeTypeRows, activeMethods);
            }
        }
    }

    private void VisitTypeSpec(uint typeSpecRef, HashSet<int> activeTypeRows,
        HashSet<int> activeMethods)
    {
        using var frame = _resourceLedger is null
            ? default : _resourceLedger.EnterFrame();
        _resourceLedger?.ConsumeWork();
        var type = _typeSpecs.GetTypeSpecRef(typeSpecRef,
            allowInitQualifier: true);
        if (!type.IsRow)
        {
            return;
        }
        _resourceLedger?.ConsumeWork(2);
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
            _resourceLedger?.ConsumeWork();
            _ = activeTypeRows.Remove(type.Row);
        }
    }

    private void VisitAnchorMethod(int method, HashSet<int> activeTypeRows,
        HashSet<int> activeMethods)
    {
        using var frame = _resourceLedger is null
            ? default : _resourceLedger.EnterFrame();
        _resourceLedger?.ConsumeWork(2);
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
            _resourceLedger?.ConsumeWork();
            _ = activeMethods.Remove(method);
        }
    }

    private bool HasPropertyAccessor(TrRmPropertyDefTableReader properties,
        int methodDefRow)
    {
        for (var property = 1; property <= properties.Count; property++)
        {
            _resourceLedger?.ConsumeWork();
            if (properties.GetGetterMethod(property) == methodDefRow)
            {
                return true;
            }
        }

        return false;
    }

    private bool HasInterfaceMethod(
        TrRmInterfaceMethodTableReader interfaceMethods, int methodDefRow)
    {
        for (var row = 1; row <= interfaceMethods.Count; row++)
        {
            _resourceLedger?.ConsumeWork(2);
            if (interfaceMethods.GetMethodDefRow(row) == methodDefRow)
            {
                return true;
            }
        }

        return false;
    }

    private uint Read(ReadOnlySpan<byte> data, int offset)
    {
        _resourceLedger?.ConsumeWork(sizeof(uint) + 1);
        return BinaryPrimitives.ReadUInt32LittleEndian(
            data.Slice(offset, sizeof(uint)));
    }

    private static bool HasUnsupportedContract(TrRmContractTableReader contracts,
        TrRmMethodDefTableReader methods, int methodDefRow)
    {
        var (first, count) = methods.GetContractRange(methodDefRow);
        for (var offset = 0; offset < count; offset++)
        {
            var kind = BinaryPrimitives.ReadUInt16LittleEndian(
                contracts.GetRow(first + offset)[4..]);
            if (kind is not (1 or 2 or 4 or 0x0B)
                && !(kind == 0x0E
                    && contracts.Schema.UsesForeignInvocationContracts()))
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
