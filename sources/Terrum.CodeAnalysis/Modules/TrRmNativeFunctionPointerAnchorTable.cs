// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.IO;

namespace Terrum.CodeAnalysis.Modules;

/// <summary>Validates Standard TypeSpec native function-pointer anchors.</summary>
internal readonly ref struct TrRmNativeFunctionPointerAnchorTableReader
{
    private readonly int[] _methodByTypeSpecRow;
    private readonly byte[] _conventionByTypeSpecRow;

    internal TrRmNativeFunctionPointerAnchorTableReader(
        TrRmTypeSpecTableReader typeSpecs, TrRmMethodDefTableReader methods,
        TrRmMethodSigTableReader signatures, TrRmContractTableReader contracts,
        TrRmModuleTableReader modules, TrRmImportTableReader imports,
        TrRmFeatureTableReader features, TrRmArtifactSchema schema,
        TrRmExportTableReader exports, TrRmPropertyDefTableReader properties,
        TrRmInterfaceMethodTableReader interfaceMethods,
        TrRmNativeImportTableReader nativeImports,
        TrRmNativeExportTableReader nativeExports)
    {
        _methodByTypeSpecRow = new int[typeSpecs.Count + 1];
        _conventionByTypeSpecRow = new byte[typeSpecs.Count + 1];
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
        }
    }

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
