// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using System.Linq;

namespace Terrum.CodeAnalysis.Semantics;

internal static class NativeStorageRecordContract
{
    internal static bool IsValidLayout(NativeStorageRecordLayoutDefinition layout)
    {
        if (string.IsNullOrWhiteSpace(layout.TypeName)
            || string.IsNullOrWhiteSpace(layout.InitializerName)
            || layout.ByteExtent <= 0 || layout.ByteExtent > ValueLayout.MaximumStructureSize
            || layout.Alignment <= 0 || layout.Alignment > NativeTargetProfile.PointerSize
            || (layout.Alignment & (layout.Alignment - 1)) != 0
            || layout.ByteExtent % layout.Alignment != 0
            || layout.Fields.IsDefaultOrEmpty
            || (layout.BackingPointerFieldIndex is int backingPointerIndex
                && (backingPointerIndex < 0 || backingPointerIndex >= layout.Fields.Length))
            || (layout.BackingPointerFieldIndex is null && layout.BackingExtentFieldIndex is null))
        {
            return false;
        }

        var names = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
        var offset = 0;
        for (var index = 0; index < layout.Fields.Length; index++)
        {
            var field = layout.Fields[index];
            if (field is null || string.IsNullOrWhiteSpace(field.Name) || !names.Add(field.Name)
                || field.Type is not (PrimitiveType.UInt32 or PrimitiveType.NativeStorageAddress)
                || (field.Type == PrimitiveType.NativeStorageAddress
                    != (layout.BackingPointerFieldIndex == index))
                || field.Size != ValueLayout.Size(field.Type, [])
                || field.Offset != Align(offset, ValueLayout.Alignment(field.Type, []))
                || field.Offset > layout.ByteExtent - field.Size
                || field.RequiredValue is long required
                    && (required < 0 || required > uint.MaxValue
                        || (field.MinimumValue is long minimum && required < minimum)
                        || (field.MaximumValue is long maximum && required > maximum))
                || field.MinimumValue is long lower && (lower < 0 || lower > uint.MaxValue)
                || field.MaximumValue is long upper && (upper < 0 || upper > uint.MaxValue)
                || field.MinimumValue is long minimumValue
                    && field.MaximumValue is long maximumValue && minimumValue > maximumValue)
            {
                return false;
            }
            offset = field.Offset + field.Size;
        }

        if (layout.BackingExtentFieldIndex is int extentIndex
                && (extentIndex < 0 || extentIndex >= layout.Fields.Length
                    || layout.Fields[extentIndex].Type != PrimitiveType.UInt32
                    || extentIndex == layout.BackingPointerFieldIndex
                    || layout.Fields[extentIndex].MinimumValue is not > 0))
        {
            return false;
        }

        var requiredAlignment = 1;
        foreach (var field in layout.Fields)
        {
            requiredAlignment = Math.Max(requiredAlignment, ValueLayout.Alignment(field.Type, []));
        }
        return layout.Alignment == requiredAlignment
            && Align(offset, layout.Alignment) == layout.ByteExtent;
    }

    internal static bool IsValidParameter(NativeStorageRecordParameterDefinition parameter,
        NativeImportDefinition import)
    {
        return parameter is not null
            && parameter.ParameterIndex >= 0 && parameter.ParameterIndex < import.ParameterTypes.Length
            && !string.IsNullOrWhiteSpace(parameter.TypeName)
            && (parameter.LoanGroup is null || !string.IsNullOrWhiteSpace(parameter.LoanGroup))
            && parameter.IsReadonly
            && import.ParameterTypes[parameter.ParameterIndex] == PrimitiveType.NativeStorageAddress
            && !import.ParameterContracts.IsDefault
            && import.ParameterContracts[parameter.ParameterIndex]
                == NativeImportParameterContract.ReadonlyNativeStorageNoEscape;
    }

    internal static bool IsValidComParameter(NativeStorageRecordParameterDefinition parameter,
        NativeComMethodDefinition method, ImmutableArray<NativeStorageRecordLayoutDefinition> layouts)
    {
        if (parameter is null || parameter.ParameterIndex <= 0
            || string.IsNullOrWhiteSpace(parameter.TypeName) || !parameter.IsReadonly || !parameter.NoEscape
            || string.IsNullOrWhiteSpace(parameter.SignatureTypeName)
            || (parameter.LoanGroup is not null && string.IsNullOrWhiteSpace(parameter.LoanGroup))
            || !NativeComCatalog.TryParseSignature(method.Signature, out _, out var parameters)
            || parameter.ParameterIndex >= parameters.Length
            || parameters[parameter.ParameterIndex] != $"const {parameter.SignatureTypeName}*"
            || method.OutputParameterIndex == parameter.ParameterIndex
            || method.BorrowedOwnerParameters.Contains(parameter.ParameterIndex)
            || (!method.ParameterAdaptations.IsDefaultOrEmpty
                && method.ParameterAdaptations.Any(adaptation =>
                    adaptation?.ParameterIndex == parameter.ParameterIndex))
            || (!method.HiddenParameters.IsDefaultOrEmpty
                && method.HiddenParameters.Any(hidden =>
                    hidden?.NativeParameterIndex == parameter.ParameterIndex)))
        {
            return false;
        }

        return layouts.Any(layout => layout.TypeName == parameter.TypeName && IsValidLayout(layout));
    }

    internal static NativeStorageRecordFacts? Facts(BoundNativeStorageRecordInitialization initializer,
        NativeImportCatalog? catalog)
    {
        if (initializer.Type != PrimitiveType.NativeStorageRecord
            || initializer.Fields.IsDefault
            || !MatchesLayout(initializer.Layout, catalog)
            || initializer.DataSource is not BoundNativeStorageAddress {
                Type: PrimitiveType.NativeStorageAddress,
                AddressType: PrimitiveType.NativeStorageAddress,
                CarrierType: PrimitiveType.NativeFixedArray,
                Index: BoundConstant { Type: PrimitiveType.Int32, Value: 0 },
                IsReadonly: true,
                ByteExtent: > 0,
                ElementCount: > 0,
                ElementStride: > 0,
            } source
            || source.ElementCount > NativeFixedArrayContract.MaximumByteExtent / source.ElementStride
            || source.ByteExtent != source.ElementCount * source.ElementStride
            || initializer.Loan != new NativeStorageLoan(source.Variable, source.CarrierType,
                source.ElementType, true, source.ByteExtent))
        {
            return null;
        }

        var expectedFieldCount = initializer.Layout.Fields.Length
            - (initializer.Layout.BackingPointerFieldIndex is null ? 0 : 1);
        if (initializer.Fields.Length != expectedFieldCount)
        {
            return null;
        }

        var valueIndex = 0;
        uint? backingExtent = null;
        for (var fieldIndex = 0; fieldIndex < initializer.Layout.Fields.Length; fieldIndex++)
        {
            if (initializer.Layout.BackingPointerFieldIndex == fieldIndex)
            {
                continue;
            }

            var definition = initializer.Layout.Fields[fieldIndex];
            var value = initializer.Fields[valueIndex++];
            if (value is not BoundConstant { Type: PrimitiveType.UInt32 } constant
                || definition.Type != PrimitiveType.UInt32
                || (definition.RequiredValue is long required && constant.Value != required)
                || (definition.MinimumValue is long minimum && constant.Value < minimum)
                || (definition.MaximumValue is long maximum && constant.Value > maximum))
            {
                return null;
            }
            if (initializer.Layout.BackingExtentFieldIndex == fieldIndex)
            {
                if (constant.Value is <= 0 or > int.MaxValue || constant.Value > source.ByteExtent)
                {
                    return null;
                }
                backingExtent = (uint)constant.Value;
            }
        }

        return new NativeStorageRecordFacts(initializer.Loan, backingExtent);
    }

    internal static bool MatchesLayout(NativeStorageRecordLayoutDefinition layout,
        NativeImportCatalog? catalog) =>
        catalog is not null && catalog.StorageRecordLayouts.Contains(layout);

    internal static bool MatchesAddress(BoundNativeStorageAddress address,
        NativeStorageRecordParameterDefinition parameter, NativeImportCatalog? catalog) =>
        parameter.IsReadonly && address.Type == PrimitiveType.NativeStorageAddress
            && address.AddressType == PrimitiveType.NativeStorageAddress
            && address.CarrierType == PrimitiveType.NativeStorageRecord
            && address.ElementType == PrimitiveType.NativeStorageRecord
            && address.ElementCount == 1
            && address.ElementStride > 0
            && address.ByteExtent == address.ElementStride
            && address.IsReadonly
            && address.Index is BoundConstant { Type: PrimitiveType.Int32, Value: 0 }
            && catalog?.StorageRecordLayouts.Any(layout => layout.TypeName == parameter.TypeName
                && layout.ByteExtent == address.ByteExtent
                && MatchesLayout(layout, catalog)) == true;

    internal static bool TryGetLayout(string name, NativeImportCatalog? catalog,
        out NativeStorageRecordLayoutDefinition? layout)
    {
        layout = catalog?.StorageRecordLayouts.FirstOrDefault(candidate => candidate.TypeName == name);
        return layout is not null && MatchesLayout(layout, catalog);
    }

    internal static bool TryGetParameter(NativeImportDefinition import, int parameterIndex,
        out NativeStorageRecordParameterDefinition? parameter)
    {
        if (import.StorageRecordParameters.IsDefault)
        {
            parameter = null;
            return false;
        }

        foreach (var candidate in import.StorageRecordParameters)
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

    internal static bool TryGetParameter(NativeComMethodDefinition method, int parameterIndex,
        out NativeStorageRecordParameterDefinition? parameter)
    {
        if (method.StorageRecordParameters.IsDefaultOrEmpty)
        {
            parameter = null;
            return false;
        }

        foreach (var candidate in method.StorageRecordParameters)
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

    private static int Align(int value, int alignment) => (value + alignment - 1) & -alignment;
}

internal sealed record NativeStorageRecordFacts(NativeStorageLoan Loan, uint? BackingExtent);
