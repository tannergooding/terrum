// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Terrum.CodeAnalysis.Semantics;

internal static class NativeStorageParameterContract
{
    internal const string AddressTypeName = "Terrum.Interop.NativeStorageAddress";

    internal static bool IsValid(NativeImportDefinition import)
    {
        if (import.StorageParameters.IsDefault || import.AggregateStorageParameters.IsDefault
            || import.StorageRecordParameters.IsDefault)
        {
            return false;
        }

        var hasStorageContract = !import.ParameterContracts.IsDefaultOrEmpty
            && import.ParameterContracts.Any(static contract => contract is
                NativeImportParameterContract.ReadonlyNativeStorageNoEscape
                or NativeImportParameterContract.WritableNativeStorageNoEscape);
        var hasAggregateAddress = !import.ParameterTypes.IsDefault
            && import.ParameterTypes.Contains(PrimitiveType.NativeStorageAddress);
        var hasStorageRecord = !import.StorageRecordParameters.IsEmpty;
        if (import.StorageParameters.IsDefaultOrEmpty && import.AggregateStorageParameters.IsDefaultOrEmpty
            && !hasStorageRecord && !hasStorageContract && !hasAggregateAddress)
        {
            return true;
        }

        if (import.ParameterTypes.IsDefault || import.StorageParameters.IsDefault
            || import.AggregateStorageParameters.IsDefault
            || import.ParameterContracts.IsDefault
            || import.ParameterContracts.Length != import.ParameterTypes.Length)
        {
            return false;
        }

        var hasStorageParameter = new bool[import.ParameterTypes.Length];
        foreach (var parameter in import.StorageParameters)
        {
            if (parameter is null || parameter.ParameterIndex < 0
                || parameter.ParameterIndex >= import.ParameterTypes.Length
                || hasStorageParameter[parameter.ParameterIndex]
                || import.ParameterContracts.IsDefault
                || import.ParameterContracts[parameter.ParameterIndex]
                    != ExpectedParameterContract(parameter)
                || import.ParameterTypes[parameter.ParameterIndex] != parameter.AddressType
                || parameter.AddressType is not (PrimitiveType.NativeStorageAddress
                    or PrimitiveType.NativeUtf16ZPointer)
                || !NativeFixedArrayContract.IsCarrier(parameter.CarrierType)
                || parameter.ByteExtent <= 0
                || parameter.ByteExtent > NativeFixedArrayContract.MaximumByteExtent
                || parameter.ElementCount <= 0 || parameter.ElementStride <= 0
                || parameter.ElementAlignment <= 0
                || string.IsNullOrWhiteSpace(parameter.ElementTypeName)
                || (parameter.ElementAlignment & (parameter.ElementAlignment - 1)) != 0
                || parameter.ElementStride % parameter.ElementAlignment != 0
                || parameter.ElementCount > NativeFixedArrayContract.MaximumByteExtent / parameter.ElementStride
                || parameter.ByteExtent != parameter.ElementCount * parameter.ElementStride
                || !NativeFixedArrayContract.IsValidElementFields(
                    parameter.ElementFields, parameter.ElementStride)
                || (parameter.RequiredStartIndex is int requiredStartIndex
                    && (requiredStartIndex < 0 || requiredStartIndex >= parameter.ElementCount))
                || (parameter.AddressType == PrimitiveType.NativeUtf16ZPointer
                    && (!parameter.IsReadonly
                        || parameter.ElementTypeName != nameof(PrimitiveType.Char16)
                        || parameter.ElementStride != sizeof(char)))
                || ValueLayout.Size(parameter.AddressType, []) != NativeTargetProfile.PointerSize)
            {
                return false;
            }

            hasStorageParameter[parameter.ParameterIndex] = true;
        }

        if (import.AggregateStorageParameters.Length > 1)
        {
            // Call-site alias checks are not defined for multiple generic aggregate loans.
            return false;
        }
        foreach (var parameter in import.AggregateStorageParameters)
        {
            if (parameter is null || parameter.ParameterIndex < 0
                || parameter.ParameterIndex >= import.ParameterTypes.Length
                || hasStorageParameter[parameter.ParameterIndex]
                || import.ParameterContracts[parameter.ParameterIndex]
                    != (parameter.IsReadonly
                        ? NativeImportParameterContract.ReadonlyNativeStorageNoEscape
                        : NativeImportParameterContract.WritableNativeStorageNoEscape)
                || import.ParameterTypes[parameter.ParameterIndex] != parameter.AddressType
                || parameter.AddressType != PrimitiveType.NativeStorageAddress
                || parameter.SourceType is string sourceType && string.IsNullOrWhiteSpace(sourceType)
                || parameter.ByteExtent <= 0 || parameter.RequiredAlignment <= 0
                || (parameter.RequiredAlignment & (parameter.RequiredAlignment - 1)) != 0
                || parameter.ByteExtent % parameter.RequiredAlignment != 0
                || ValueLayout.Size(parameter.AddressType, []) != NativeTargetProfile.PointerSize)
            {
                return false;
            }

            hasStorageParameter[parameter.ParameterIndex] = true;
        }

        foreach (var parameter in import.StorageRecordParameters)
        {
            if (!NativeStorageRecordContract.IsValidParameter(parameter, import)
                || hasStorageParameter[parameter.ParameterIndex])
            {
                return false;
            }
            hasStorageParameter[parameter.ParameterIndex] = true;
        }

        for (var index = 0; index < hasStorageParameter.Length; index++)
        {
            var declaresStorage = import.ParameterContracts[index]
                is NativeImportParameterContract.ReadonlyNativeStorageNoEscape
                    or NativeImportParameterContract.WritableNativeStorageNoEscape;
            if ((declaresStorage && !hasStorageParameter[index])
                || (hasStorageParameter[index] && !declaresStorage
                    && (import.ParameterTypes[index] != PrimitiveType.NativeUtf16ZPointer
                        || import.ParameterContracts[index]
                            != NativeImportParameterContract.ReadonlyUtf16ZNoEscape)))
            {
                return false;
            }
            if (import.ParameterTypes[index] == PrimitiveType.NativeStorageAddress
                && !hasStorageParameter[index])
            {
                return false;
            }
        }

        return true;
    }

    internal static bool TryGetParameter(NativeImportDefinition import, int parameterIndex,
        out NativeStorageParameterDefinition? parameter)
    {
        if (import.StorageParameters.IsDefault)
        {
            parameter = null;
            return false;
        }

        foreach (var candidate in import.StorageParameters)
        {
            if (candidate?.ParameterIndex == parameterIndex)
            {
                parameter = candidate;
                return true;
            }
        }

        parameter = null;
        return false;
    }

    internal static bool TryGetParameter(BoundFunction target, int parameterIndex,
        BoundProgram program, out NativeStorageParameterDefinition? parameter)
    {
        parameter = null;
        if (!NativeImportBoundParameterMappingContract.TryGetImportParameter(
                target, parameterIndex, program.NativeImportCatalog, out var import, out var nativeParameter)
            || import is null
            || !TryGetParameter(import, nativeParameter, out var selected))
        {
            return false;
        }
        parameter = selected;
        return parameter is not null;
    }

    internal static bool TryGetImportParameterContract(BoundFunction target, int parameterIndex,
        BoundProgram program, out NativeImportParameterContract contract)
    {
        contract = default;
        if (!NativeImportBoundParameterMappingContract.TryGetImportParameter(
                target, parameterIndex, program.NativeImportCatalog, out var import, out var nativeParameter)
            || import is null || import.ParameterContracts.IsDefaultOrEmpty
            || (uint)nativeParameter >= (uint)import.ParameterContracts.Length)
        {
            return false;
        }
        contract = import.ParameterContracts[nativeParameter];
        return true;
    }

    internal static bool IsReadonlyStringParameter(BoundFunction target, int parameterIndex,
        PrimitiveType parameterType, BoundProgram program)
    {
        var expectedContract = parameterType switch {
            PrimitiveType.NativeUtf16ZPointer => NativeImportParameterContract.ReadonlyUtf16ZNoEscape,
            PrimitiveType.NativeUtf8ZPointer => NativeImportParameterContract.ReadonlyUtf8ZNoEscape,
            _ => NativeImportParameterContract.Value,
        };
        return expectedContract != NativeImportParameterContract.Value
            && TryGetImportParameterContract(target, parameterIndex, program, out var actualContract)
            && actualContract == expectedContract;
    }

    internal static bool TryGetAggregateParameter(NativeImportDefinition import, int parameterIndex,
        out NativeAggregateStorageParameterDefinition? parameter)
    {
        if (import.AggregateStorageParameters.IsDefault)
        {
            parameter = null;
            return false;
        }

        foreach (var candidate in import.AggregateStorageParameters)
        {
            if (candidate?.ParameterIndex == parameterIndex)
            {
                parameter = candidate;
                return true;
            }
        }

        parameter = null;
        return false;
    }

    internal static NativeImportParameterContract ExpectedParameterContract(
        NativeImportDefinition import, int parameterIndex)
    {
        if (TryGetParameter(import, parameterIndex, out var storageParameter) && storageParameter is not null)
        {
            return ExpectedParameterContract(storageParameter);
        }
        if (TryGetAggregateParameter(import, parameterIndex, out var aggregateParameter)
            && aggregateParameter is not null)
        {
            return aggregateParameter.IsReadonly
                ? NativeImportParameterContract.ReadonlyNativeStorageNoEscape
                : NativeImportParameterContract.WritableNativeStorageNoEscape;
        }
        if (NativeStorageRecordContract.TryGetParameter(import, parameterIndex, out var storageRecordParameter)
            && storageRecordParameter is not null)
        {
            return NativeImportParameterContract.ReadonlyNativeStorageNoEscape;
        }
        return NativeImportParameterContract.Value;
    }

    internal static bool SupportsFixedArrayParameter(NativeImportCatalog? catalog,
        BoundNativeFixedArrayLayout layout, IReadOnlyList<BoundStruct> structures,
        PrimitiveType addressType, bool isReadonly) =>
        catalog is not null
        && NativeFixedArrayContract.IsCarrier(layout.CarrierType)
        && ((isReadonly && catalog.SupportsFixedArrayStorage(layout, structures))
            || catalog.ImportDefinitions.Any(import => !import.StorageParameters.IsDefaultOrEmpty
                && import.StorageParameters.Any(parameter =>
            parameter is not null && parameter.IsReadonly == isReadonly
                && parameter.AddressType == addressType
                && parameter.CarrierType == layout.CarrierType
                    && parameter.ElementTypeName == NativeFixedArrayContract.ElementTypeName(
                        layout.ElementType, structures)
                    && parameter.ElementCount == layout.ElementCount
                    && parameter.ElementStride == layout.ElementStride
                    && parameter.ElementAlignment == layout.Alignment
                    && parameter.ByteExtent == layout.ByteExtent
                    && NativeFixedArrayContract.MatchesElementFields(
                        layout.ElementType, structures, parameter.ElementFields)
                    && parameter.RequiredStartIndex == 0
                    && (parameter.IsReadonly || !isReadonly))));

    private static NativeImportParameterContract ExpectedParameterContract(
        NativeStorageParameterDefinition parameter) =>
        parameter.AddressType switch {
            PrimitiveType.NativeUtf16ZPointer => NativeImportParameterContract.ReadonlyUtf16ZNoEscape,
            _ => parameter.IsReadonly
                ? NativeImportParameterContract.ReadonlyNativeStorageNoEscape
                : NativeImportParameterContract.WritableNativeStorageNoEscape,
        };

    internal static bool MatchesAddress(BoundNativeStorageAddress address,
        NativeStorageParameterDefinition parameter) =>
        address.Type == address.AddressType
            && address.AddressType == parameter.AddressType
            && address.CarrierType == parameter.CarrierType
            && address.ByteExtent == parameter.ByteExtent
            && address.IsReadonly == parameter.IsReadonly;

    internal static bool MatchesComParameterAddress(BoundNativeStorageAddress address,
        NativeComMethodDefinition method, int nativeParameterIndex, BoundFunction caller,
        BoundProgram program)
    {
        if (program.NativeImportCatalog is not NativeImportCatalog catalog
            || !NativeComCallContract.TryGetNativeParameter(method, nativeParameterIndex,
                out var parameter)
            || parameter is null || parameter.Retention != NativeParameterRetention.NoEscape
            || parameter.Extent != 1
            || !NativeComCallContract.TryGetNativeParameterPointeeType(method,
                nativeParameterIndex, catalog, program.Structures, out var pointeeType)
            || !DirectNativeImportContract.IsInputPointee(pointeeType, program.Structures))
        {
            return false;
        }

        var extent = ValueLayout.Size(pointeeType, program.Structures);
        var variable = address.Variable;
        var isReadonly = parameter.Access == NativeParameterAccess.Read;
        return parameter.Access is NativeParameterAccess.Read
                or NativeParameterAccess.Write or NativeParameterAccess.ReadWrite
            && address.Type == PrimitiveType.NativeStorageAddress
            && address.AddressType == PrimitiveType.NativeStorageAddress
            && address.CarrierType == pointeeType && address.ElementType == pointeeType
            && address.ElementCount == 1 && address.ElementStride == extent
            && address.ByteExtent == extent && address.IsReadonly == isReadonly
            && address.Index is BoundConstant { Type: PrimitiveType.Int32, Value: 0 }
            && (uint)variable < (uint)caller.Variables.Length
            && caller.Variables[variable].Type == pointeeType
            && (isReadonly || caller.Variables[variable].IsMutable)
            && !caller.Variables[variable].IsInitializationLocal;
    }

    internal static bool MatchesAggregateAddress(BoundNativeStorageAddress address,
        NativeAggregateStorageParameterDefinition parameter, BoundProgram program)
    {
        var typeIndex = (int)address.CarrierType - 100;
        return address.Type == PrimitiveType.NativeStorageAddress
            && address.AddressType == parameter.AddressType
            && parameter.AddressType == PrimitiveType.NativeStorageAddress
            && address.CarrierType == address.ElementType
            && address.ElementCount == 1
            && address.ElementStride == parameter.ByteExtent
            && address.ByteExtent == parameter.ByteExtent
            && address.Index is BoundConstant { Type: PrimitiveType.Int32, Value: 0 }
            && (parameter.IsReadonly || !address.IsReadonly)
            && typeIndex >= 0 && typeIndex < program.Structures.Length
            && ValueLayout.Size(address.CarrierType, program.Structures) == parameter.ByteExtent
            && ValueLayout.Alignment(address.CarrierType, program.Structures, program.NativeImportCatalog)
                == parameter.RequiredAlignment
            && NativeAggregateContract.IsPlainValueAggregate(address.CarrierType, program.Structures)
            && (parameter.SourceType is null
                || (NativeAggregateContract.TryResolveSourceType(parameter.SourceType,
                        program.Structures, out var sourceType)
                    && sourceType == address.CarrierType
                    && program.NativeImportCatalog?.SupportsAggregateStorageLayout(
                        address.CarrierType, program.Structures) == true));
    }

    internal static bool MatchesAggregateAddress(BoundNativeStorageAddress address,
        NativeComParameterAdaptation adaptation, BoundProgram program)
    {
        var typeIndex = (int)address.CarrierType - 100;
        var isReadonly = adaptation.TargetType.StartsWith("const ", System.StringComparison.Ordinal);
        return adaptation.Kind == NativeComParameterAdaptationKind.AggregateStorageAddress
            && adaptation.NoEscape
            && address.Type == PrimitiveType.NativeStorageAddress
            && address.AddressType == PrimitiveType.NativeStorageAddress
            && address.CarrierType == address.ElementType
            && address.ElementCount == 1
            && address.ElementStride == adaptation.SourceSize
            && address.ByteExtent == adaptation.SourceSize
            && address.Index is BoundConstant { Type: PrimitiveType.Int32, Value: 0 }
            && (isReadonly || !address.IsReadonly)
            && typeIndex >= 0 && typeIndex < program.Structures.Length
            && NativeAggregateContract.TryResolveSourceType(adaptation.SourceType,
                program.Structures, out var sourceType)
            && sourceType == address.CarrierType
            && ValueLayout.Size(address.CarrierType, program.Structures) == adaptation.SourceSize
            && ValueLayout.Alignment(address.CarrierType, program.Structures, program.NativeImportCatalog)
                == adaptation.SourceAlignment
            && NativeAggregateContract.IsPlainValueAggregate(address.CarrierType, program.Structures);
    }

    internal static bool MatchesValueOutputAddress(BoundNativeStorageAddress address,
        NativeComParameterAdaptation adaptation, BoundProgram program)
    {
        if (program.NativeImportCatalog is not NativeImportCatalog catalog
            || !NativeComCallContract.IsValidValueOutputAddressType(adaptation, catalog,
                program.Structures, out var sourceType))
        {
            return false;
        }
        var extent = ValueLayout.Size(sourceType, program.Structures);
        return address.Type == PrimitiveType.NativeStorageAddress
            && address.AddressType == PrimitiveType.NativeStorageAddress
            && address.CarrierType == sourceType
            && address.ElementType == sourceType
            && address.ElementCount == 1
            && address.ElementStride == extent
            && address.ByteExtent == extent
            && address.Index is BoundConstant { Type: PrimitiveType.Int32, Value: 0 }
            && !address.IsReadonly;
    }

    internal static bool SupportsValueOutputAddressType(NativeImportCatalog? catalog,
        PrimitiveType type, IReadOnlyList<BoundStruct> structures, bool isReadonly) =>
        !isReadonly && catalog is not null
        && catalog.ComMethods.Any(method => !method.ParameterAdaptations.IsDefaultOrEmpty
            && method.ParameterAdaptations.Any(adaptation => adaptation is not null
                && NativeComCallContract.IsValidValueOutputAddressType(adaptation, catalog,
                    structures, out var sourceType) && sourceType == type));

    internal static bool SupportsAggregateType(NativeImportCatalog? catalog, PrimitiveType type,
        IReadOnlyList<BoundStruct> structures, bool isReadonly)
    {
        var typeIndex = (int)type - 100;
        if (catalog is null || typeIndex < 0 || typeIndex >= structures.Count
            || !NativeAggregateContract.IsPlainValueAggregate(type, structures))
        {
            return false;
        }

        var byteExtent = ValueLayout.Size(type, structures);
        var alignment = ValueLayout.Alignment(type, structures, catalog);
        return catalog.SupportsParameterType(PrimitiveType.NativeStorageAddress)
                && catalog.ImportDefinitions.Any(import =>
            import.AggregateStorageParameters.Any(parameter => parameter is not null
                && parameter.AddressType == PrimitiveType.NativeStorageAddress
                && parameter.ByteExtent == byteExtent
                && parameter.RequiredAlignment == alignment
                && (parameter.SourceType is null
                    || (NativeAggregateContract.TryResolveSourceType(parameter.SourceType, structures,
                            out var sourceType)
                        && sourceType == type
                        && catalog.SupportsAggregateStorageLayout(type, structures)))
                && (parameter.IsReadonly || !isReadonly)))
            || catalog.ComMethods.Any(method => !method.ParameterAdaptations.IsDefaultOrEmpty
                && method.ParameterAdaptations.Any(adaptation => adaptation is not null
                    && adaptation.Kind == NativeComParameterAdaptationKind.AggregateStorageAddress
                    && adaptation.NoEscape
                    && NativeAggregateContract.TryResolveSourceType(adaptation.SourceType, structures,
                        out var sourceType) && sourceType == type
                    && adaptation.SourceSize == byteExtent
                    && adaptation.SourceAlignment == alignment
                    && (adaptation.TargetType.StartsWith("const ", System.StringComparison.Ordinal)
                        || !isReadonly)));
    }

    internal static bool SupportsStorageRecordType(NativeStorageRecordLayoutDefinition layout,
        NativeImportCatalog? catalog, bool isReadonly) =>
        isReadonly && NativeStorageRecordContract.MatchesLayout(layout, catalog);

    internal static bool IsStorageAddressParameter(BoundFunction function, int parameterIndex,
        NativeImportCatalog? catalog) =>
        function.IsExternal && parameterIndex >= 0 && parameterIndex < function.ParameterCount
            && function.NativeImportKey is string key && catalog is not null
            && catalog.TryGetImport(key, out var import) && import is not null
            && parameterIndex < import.ParameterTypes.Length
            && import.ParameterTypes[parameterIndex] == PrimitiveType.NativeStorageAddress
            && (TryGetParameter(import, parameterIndex, out _)
                || TryGetAggregateParameter(import, parameterIndex, out _)
                || NativeStorageRecordContract.TryGetParameter(import, parameterIndex, out _));
}

internal static class NativeImportBoundParameterMappingContract
{
    internal static bool TryGetImportParameter(BoundFunction target, int parameterIndex,
        NativeImportCatalog? catalog, out NativeImportDefinition? import, out int nativeParameter)
    {
        import = null;
        nativeParameter = -1;
        if (catalog is null || target.Variables.IsDefault
            || target.ParameterCount < 0 || target.ParameterCount > target.Variables.Length
            || (uint)parameterIndex >= (uint)target.ParameterCount)
        {
            return false;
        }

        if (target.IsExternal)
        {
            if (!target.NativeImportParameterMappings.IsDefaultOrEmpty
                || target.NativeImportKey is not string importKey
                || !catalog.TryGetImport(importKey, out var externalImport) || externalImport is null
                || parameterIndex >= externalImport.ParameterTypes.Length
                || target.Variables[parameterIndex].Type != externalImport.ParameterTypes[parameterIndex])
            {
                return false;
            }
            import = externalImport;
            nativeParameter = parameterIndex;
            return true;
        }

        var mappings = target.NativeImportParameterMappings;
        if (mappings.IsDefaultOrEmpty || mappings.Any(static mapping => mapping is null)
            || mappings.Select(static mapping => mapping.BoundParameterIndex).Distinct().Count()
                != mappings.Length
            || mappings.Select(static mapping => (mapping.ImportKey, mapping.NativeParameterIndex)).Distinct().Count()
                != mappings.Length)
        {
            return false;
        }

        foreach (var mapping in mappings)
        {
            if (mapping.ImportKey is not { Length: > 0 }
                || !catalog.TryGetImport(mapping.ImportKey, out var mappedImport) || mappedImport is null
                || (uint)mapping.BoundParameterIndex >= (uint)target.ParameterCount
                || (uint)mapping.NativeParameterIndex >= (uint)mappedImport.ParameterTypes.Length
                || target.Variables[mapping.BoundParameterIndex].IsOut
                || target.Variables[mapping.BoundParameterIndex].Type
                    != mappedImport.ParameterTypes[mapping.NativeParameterIndex])
            {
                return false;
            }
        }

        var selected = mappings.SingleOrDefault(mapping => mapping.BoundParameterIndex == parameterIndex);
        if (selected is null)
        {
            return false;
        }
        if (!catalog.TryGetImport(selected.ImportKey, out var selectedImport) || selectedImport is null)
        {
            return false;
        }
        import = selectedImport;
        nativeParameter = selected.NativeParameterIndex;
        return true;
    }

    internal static bool HaveSameMappings(
        ImmutableArray<NativeImportBoundParameterMapping> left,
        ImmutableArray<NativeImportBoundParameterMapping> right) =>
        left.IsDefaultOrEmpty ? right.IsDefaultOrEmpty
            : !right.IsDefaultOrEmpty && left.SequenceEqual(right);
}

internal sealed record NativeImportBoundParameterMapping(
    string ImportKey, int BoundParameterIndex, int NativeParameterIndex);

internal sealed record NativeStorageParameterDefinition(int ParameterIndex, PrimitiveType AddressType,
    PrimitiveType CarrierType, int ByteExtent, bool IsReadonly, int? RequiredStartIndex,
    string ElementTypeName, int ElementCount, int ElementStride, int ElementAlignment)
{
    internal ImmutableArray<NativeFixedArrayFieldLayoutDefinition> ElementFields { get; init; } = [];
}

internal sealed record NativeStorageRecordParameterDefinition(
    int ParameterIndex,
    string TypeName,
    bool IsReadonly,
    string? LoanGroup = null,
    bool NoEscape = false,
    string? SignatureTypeName = null);

internal sealed record NativeStorageRecordLayoutDefinition(
    string TypeName,
    string InitializerName,
    int ByteExtent,
    int Alignment,
    ImmutableArray<NativeStorageRecordFieldDefinition> Fields,
    int? BackingPointerFieldIndex = null,
    int? BackingExtentFieldIndex = null);

internal sealed record NativeStorageRecordFieldDefinition(
    string Name,
    PrimitiveType Type,
    int Offset,
    int Size,
    long? RequiredValue = null,
    long? MinimumValue = null,
    long? MaximumValue = null);

internal sealed record NativeFixedArrayStorageLayoutDefinition(string ElementTypeName, int ElementCount,
    int ElementStride, int ElementAlignment, int ByteExtent)
{
    internal ImmutableArray<NativeFixedArrayFieldLayoutDefinition> ElementFields { get; init; } = [];
    internal bool AllowsEmptyInitialization { get; init; }
    internal bool AllowsMutableLocal { get; init; }
    internal bool AllowsElementWrites { get; init; }
    internal bool AllowsUInt32Indices { get; init; }
    internal bool AllowsCheckedRuntimeAccess { get; init; }
    internal bool AllowsReadonlyUtf16ZLoan { get; init; }
}

internal sealed record NativeFixedArrayFieldLayoutDefinition(string Name, string TypeName, int Offset, int Size,
    bool? IsMutable = null, bool? IsPrivate = null, bool? IsInternal = null, bool? RequiresUnsafeCaller = null);
