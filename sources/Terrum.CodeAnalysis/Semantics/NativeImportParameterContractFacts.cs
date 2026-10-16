// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System.Collections.Immutable;
using System.Linq;

namespace Terrum.CodeAnalysis.Semantics;

internal static class NativeImportParameterContractFacts
{
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
