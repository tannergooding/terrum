// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Terrum.CodeAnalysis.Semantics;

internal enum NativeCallingConvention : byte
{
    Native = 0,
    Unmanaged = 1,
    Cdecl = 2,
    Stdcall = 3,
    Vectorcall = 4,
}

internal readonly record struct NativeFunctionPointerSelector(
    byte PhaseTag, uint PhaseLocalOrdinal, byte NodeKind);

internal readonly record struct NativeFunctionPointerOutputState(
    uint FormalOrdinal, uint SelectorOrdinal, byte State);

internal readonly record struct NativeFunctionPointerDependency(
    byte DestinationKind, byte SourceKind, byte Relationship, byte Phase,
    uint DestinationOrdinal, uint SourceOrdinal);

internal sealed record NativeFunctionPointerSignatureDefinition(
    NativeCallingConvention CallingConvention,
    PrimitiveType ReturnType,
    ImmutableArray<PrimitiveType> ParameterTypes)
{
    internal uint EffectsMask { get; init; }
    internal ImmutableArray<NativeFunctionPointerSelector> Selectors { get; init; } = [];
    internal ImmutableArray<NativeFunctionPointerOutputState> OutputStates { get; init; } = [];
    internal ImmutableArray<NativeFunctionPointerDependency> Dependencies { get; init; } = [];
}

internal static class NativeFunctionPointerTypes
{
    internal const int First = ManagedClassTypes.OptionalWriteFirst + 256;

    internal static bool IsType(PrimitiveType type) =>
        (int)type is >= First and < NativePointerTypes.First;

    internal static int Index(PrimitiveType type) => checked((int)type - First);

    internal static PrimitiveType Type(int index) => (PrimitiveType)checked(First + index);
}

internal static class NativeFunctionPointerContract
{
    internal static bool IsValidTable(ImmutableArray<NativeFunctionPointerSignatureDefinition> signatures)
    {
        if (signatures.IsDefault)
        {
            return false;
        }

        var state = new byte[signatures.Length];
        for (var index = 0; index < signatures.Length; index++)
        {
            if (!IsValidSignature(index, signatures, state, 0))
            {
                return false;
            }
        }
        return true;
    }

    internal static bool IsValidType(PrimitiveType type,
        ImmutableArray<NativeFunctionPointerSignatureDefinition> signatures) =>
        NativeFunctionPointerTypes.IsType(type)
            && (uint)NativeFunctionPointerTypes.Index(type) < (uint)signatures.Length;

    internal static bool IsValidSignature(NativeFunctionPointerSignatureDefinition signature,
        ImmutableArray<NativeFunctionPointerSignatureDefinition> signatures)
    {
        if (!IsKnownConvention(signature.CallingConvention)
            || (signature.EffectsMask & ~6u) != 0
            || signature.ParameterTypes.IsDefault
            || signature.Selectors.IsDefault
            || signature.OutputStates.IsDefault
            || signature.Dependencies.IsDefault
            || !IsSupportedReturnType(signature.ReturnType, signatures))
        {
            return false;
        }

        if (!ValidSelectors(signature)
            || !signature.OutputStates.IsEmpty
            || !ValidDependencies(signature))
        {
            return false;
        }

        foreach (var parameterType in signature.ParameterTypes)
        {
            if (!IsSupportedValueType(parameterType, signatures))
            {
                return false;
            }
        }
        return true;
    }

    internal static bool IsKnownConvention(NativeCallingConvention convention) =>
        convention is NativeCallingConvention.Native
            or NativeCallingConvention.Unmanaged
            or NativeCallingConvention.Cdecl
            or NativeCallingConvention.Stdcall
            or NativeCallingConvention.Vectorcall;

    internal static bool IsSupportedWindowsX64Convention(NativeCallingConvention convention) =>
        convention is NativeCallingConvention.Native
            or NativeCallingConvention.Unmanaged
            or NativeCallingConvention.Cdecl
            or NativeCallingConvention.Stdcall;

    internal static bool MatchesFunction(BoundFunction function, PrimitiveType functionPointerType,
        ImmutableArray<NativeFunctionPointerSignatureDefinition> signatures)
    {
        if (!NativeFunctionPointerTypes.IsType(functionPointerType)
            || (uint)NativeFunctionPointerTypes.Index(functionPointerType) >= (uint)signatures.Length
            || function.NativeFunctionPointerType != functionPointerType
            || !function.RequiresUnsafeCaller
            || function.IsConstructor || function.IsClassConstructor
            || function.IsOperator || function.IsCleanup || function.PropertyName is not null
            || function.Intrinsic != BoundIntrinsic.None
            || (function.DeclaringStruct is not null && !function.IsStatic)
            || function.ParameterCount != signatures[NativeFunctionPointerTypes.Index(functionPointerType)]
                .ParameterTypes.Length
            || (function.NativeCallableConvention is NativeCallingConvention functionConvention
                && functionConvention != signatures[NativeFunctionPointerTypes.Index(functionPointerType)]
                    .CallingConvention)
            || function.Variables.Length < function.ParameterCount)
        {
            return false;
        }

        var signature = signatures[NativeFunctionPointerTypes.Index(functionPointerType)];
        if (function.ReturnType != signature.ReturnType
            || signature.EffectsMask != GetDeclaredEffectsMask(
                function.DoesNotReturn, function.DeclaredRequiresUnsafeCaller))
        {
            return false;
        }
        if (!signature.Selectors.IsEmpty || !signature.OutputStates.IsEmpty
            || !TryCreateDependencies(function.DependencyContracts,
                out var dependencies)
            || !SameDependencies(signature.Dependencies, dependencies))
        {
            return false;
        }

        for (var index = 0; index < signature.ParameterTypes.Length; index++)
        {
            if (function.Variables[index] is not {
                IsOut: false, IsInit: false, IsMutable: false, IsImplicitRef: false
            } parameter
                || parameter.Type != signature.ParameterTypes[index])
            {
                return false;
            }
        }
        return true;
    }

    internal static uint GetDeclaredEffectsMask(bool doesNotReturn,
        bool requiresUnsafeCaller)
        => (doesNotReturn ? 2u : 0u) | (requiresUnsafeCaller ? 4u : 0u);

    internal static bool TryCreateDependencies(
        ImmutableArray<BoundDependencyContract> contracts,
        out ImmutableArray<NativeFunctionPointerDependency> dependencies)
    {
        if (contracts.IsDefault)
        {
            dependencies = [];
            return false;
        }

        var result = ImmutableArray.CreateBuilder<NativeFunctionPointerDependency>();
        var forwardingContracts = new List<BoundDependencyContract>();
        foreach (var contract in contracts)
        {
            var destinationKind = contract.Destination switch
            {
                BoundDependencyDestination.Result => (byte)1,
                BoundDependencyDestination.InitializedOutput => (byte)2,
                BoundDependencyDestination.RetainedFormal => (byte)3,
                _ => (byte)0,
            };
            if (destinationKind == 0)
            {
                dependencies = [];
                return false;
            }

            uint ordinal;
            if (destinationKind == 1)
            {
                if (contract.DestinationOrdinal is not null
                    and not 0)
                {
                    dependencies = [];
                    return false;
                }
                ordinal = 0;
            }
            else if (contract.DestinationOrdinal is int destinationOrdinal
                && destinationOrdinal >= 0)
            {
                ordinal = checked((uint)destinationOrdinal);
            }
            else
            {
                dependencies = [];
                return false;
            }
            var phase = contract.Relation switch
            {
                BoundDependencyRelation.DependsOn
                    when destinationKind is 1 or 2 => (byte)1,
                BoundDependencyRelation.Captures => (byte)2,
                BoundDependencyRelation.ForwardsBacking
                    when destinationKind is 1 or 2 => (byte)1,
                BoundDependencyRelation.ForwardsBacking
                    when destinationKind == 3 => (byte)2,
                _ => (byte)0,
            };
            if (phase == 0 || contract.Sources.IsDefault)
            {
                dependencies = [];
                return false;
            }

            if (contract.Relation == BoundDependencyRelation.ForwardsBacking)
            {
                forwardingContracts.Add(contract);
                continue;
            }

            var relationship = contract.Relation switch
            {
                BoundDependencyRelation.DependsOn or BoundDependencyRelation.Captures => (byte)3,
                _ => (byte)0,
            };
            if (contract.Sources.IsEmpty)
            {
                if (contract.Relation == BoundDependencyRelation.ForwardsBacking)
                {
                    dependencies = [];
                    return false;
                }

                result.Add(new NativeFunctionPointerDependency(destinationKind,
                    0, 0, phase, ordinal, 0));
                continue;
            }

            foreach (var source in contract.Sources)
            {
                if (source.Kind != BoundDependencySourceKind.Formal
                    || source.FormalOrdinal is not int sourceOrdinal
                    || sourceOrdinal < 0 || relationship == 0)
                {
                    dependencies = [];
                    return false;
                }

                result.Add(new NativeFunctionPointerDependency(destinationKind,
                    1, relationship, phase, ordinal, checked((uint)sourceOrdinal)));
            }
        }

        foreach (var forwarding in forwardingContracts)
        {
            var destinationKind = forwarding.Destination switch
            {
                BoundDependencyDestination.Result => (byte)1,
                BoundDependencyDestination.InitializedOutput => (byte)2,
                BoundDependencyDestination.RetainedFormal => (byte)3,
                _ => (byte)0,
            };
            if (destinationKind == 0 || forwarding.Sources.IsEmpty)
            {
                dependencies = [];
                return false;
            }

            var destinationOrdinal = destinationKind == 1
                ? 0u : forwarding.DestinationOrdinal is int ordinal && ordinal >= 0
                    ? checked((uint)ordinal) : uint.MaxValue;
            var phase = destinationKind is 1 or 2 ? (byte)1 : (byte)2;
            foreach (var source in forwarding.Sources)
            {
                if (source.Kind != BoundDependencySourceKind.Formal
                    || source.FormalOrdinal is not int sourceOrdinal
                    || sourceOrdinal < 0)
                {
                    dependencies = [];
                    return false;
                }

                var match = -1;
                for (var index = 0; index < result.Count; index++)
                {
                    var dependency = result[index];
                    if (dependency.DestinationKind == destinationKind
                        && dependency.DestinationOrdinal == destinationOrdinal
                        && dependency.Phase == phase
                        && dependency.SourceKind == 1
                        && dependency.SourceOrdinal == (uint)sourceOrdinal)
                    {
                        match = index;
                        break;
                    }
                }
                if (match < 0 || result[match].Relationship is not (2 or 3))
                {
                    dependencies = [];
                    return false;
                }

                result[match] = result[match] with { Relationship = 2 };
            }
        }

        dependencies = [.. result.OrderBy(static dependency => dependency.DestinationKind)
            .ThenBy(static dependency => dependency.DestinationOrdinal)
            .ThenBy(static dependency => dependency.Phase)
            .ThenBy(static dependency => dependency.SourceKind)
            .ThenBy(static dependency => dependency.SourceOrdinal)];
        return true;
    }

    internal static bool AreEquivalent(
        NativeFunctionPointerSignatureDefinition left,
        NativeFunctionPointerSignatureDefinition right)
        => left.CallingConvention == right.CallingConvention
            && left.ReturnType == right.ReturnType
            && left.EffectsMask == right.EffectsMask
            && left.ParameterTypes.AsSpan().SequenceEqual(right.ParameterTypes.AsSpan())
            && left.Selectors.ToHashSet().SetEquals(right.Selectors)
            && left.OutputStates.ToHashSet().SetEquals(right.OutputStates)
            && SameDependencies(left.Dependencies, right.Dependencies);

    internal static bool CanPersistRevision92(
        NativeFunctionPointerSignatureDefinition signature)
        => signature.EffectsMask == 0 && signature.Selectors.IsEmpty
            && signature.OutputStates.IsEmpty && signature.Dependencies.IsEmpty;

    private static bool ValidSelectors(NativeFunctionPointerSignatureDefinition signature)
    {
        var ordinals = new HashSet<uint>();
        foreach (var selector in signature.Selectors)
        {
            if (selector.PhaseTag != 3 || selector.NodeKind is < 1 or > 6
                || !ordinals.Add(selector.PhaseLocalOrdinal))
            {
                return false;
            }
            if ((selector.NodeKind is 1 or 2 && signature.ReturnType != PrimitiveType.Bool)
                || (selector.NodeKind is 3 or 4
                    && signature.ReturnType is not (PrimitiveType.Int32 or PrimitiveType.UInt32))
                || (selector.NodeKind is 5 or 6 && signature.ReturnType != PrimitiveType.Int32))
            {
                return false;
            }
        }

        for (var ordinal = 0; ordinal < signature.Selectors.Length; ordinal++)
        {
            if (!ordinals.Contains(checked((uint)ordinal)))
            {
                return false;
            }
        }
        return true;
    }

    private static bool ValidDependencies(NativeFunctionPointerSignatureDefinition signature)
        => ValidDependencyRows(signature.Dependencies,
            signature.ParameterTypes.Length);

    private static bool ValidDependencyRows(
        ImmutableArray<NativeFunctionPointerDependency> rows, int parameterCount)
    {
        var keys = new HashSet<(byte DestinationKind, uint DestinationOrdinal,
            byte Phase, byte SourceKind, uint SourceOrdinal)>();
        var emptyDestinations = new HashSet<(byte DestinationKind,
            uint DestinationOrdinal, byte Phase)>();
        var sourcedDestinations = new HashSet<(byte DestinationKind,
            uint DestinationOrdinal, byte Phase)>();
        foreach (var dependency in rows)
        {
            var destinationValid = dependency.DestinationKind switch
            {
                1 => dependency.DestinationOrdinal == 0,
                3 => dependency.DestinationOrdinal < (uint)parameterCount,
                _ => false,
            };
            var sourceValid = dependency.SourceKind switch
            {
                0 => dependency.Relationship == 0 && dependency.SourceOrdinal == 0,
                1 => dependency.Relationship is >= 1 and <= 3
                    && dependency.SourceOrdinal < (uint)parameterCount,
                _ => false,
            };
            var key = (dependency.DestinationKind, dependency.DestinationOrdinal,
                dependency.Phase, dependency.SourceKind, dependency.SourceOrdinal);
            var destinationKey = (dependency.DestinationKind,
                dependency.DestinationOrdinal, dependency.Phase);
            if (!destinationValid || !sourceValid
                || dependency.Phase is < 1 or > 3 || !keys.Add(key))
            {
                return false;
            }

            if (dependency.SourceKind == 0)
            {
                _ = emptyDestinations.Add(destinationKey);
            }
            else
            {
                _ = sourcedDestinations.Add(destinationKey);
            }
        }

        return !emptyDestinations.Overlaps(sourcedDestinations);
    }

    private static bool SameDependencies(
        ImmutableArray<NativeFunctionPointerDependency> left,
        ImmutableArray<NativeFunctionPointerDependency> right)
        => left.Length == right.Length && left.ToHashSet().SetEquals(right);

    private static bool DependenciesSatisfy(
        ImmutableArray<NativeFunctionPointerDependency> actual,
        ImmutableArray<NativeFunctionPointerDependency> expected,
        int parameterCount)
    {
        if (!ValidDependencyRows(actual, parameterCount)
            || !ValidDependencyRows(expected, parameterCount))
        {
            return false;
        }

        var actualGroups = actual.Select(static dependency =>
                (dependency.DestinationKind, dependency.DestinationOrdinal,
                    dependency.Phase))
            .ToHashSet();
        var expectedGroups = expected.Select(static dependency =>
                (dependency.DestinationKind, dependency.DestinationOrdinal,
                    dependency.Phase))
            .ToHashSet();
        if (!actualGroups.SetEquals(expectedGroups))
        {
            return false;
        }

        foreach (var group in expectedGroups)
        {
            var actualRows = actual.Where(dependency =>
                (dependency.DestinationKind, dependency.DestinationOrdinal,
                    dependency.Phase) == group).ToArray();
            var expectedRows = expected.Where(dependency =>
                (dependency.DestinationKind, dependency.DestinationOrdinal,
                    dependency.Phase) == group).ToArray();
            var actualEmpty = actualRows[0].SourceKind == 0;
            var expectedEmpty = expectedRows[0].SourceKind == 0;
            if (actualEmpty)
            {
                if (expectedEmpty)
                {
                    continue;
                }
                continue;
            }
            if (expectedEmpty)
            {
                return false;
            }

            foreach (var actualRow in actualRows)
            {
                var matchingExpected = expectedRows.FirstOrDefault(expectedRow =>
                    expectedRow.SourceKind == actualRow.SourceKind
                    && expectedRow.SourceOrdinal == actualRow.SourceOrdinal);
                if (matchingExpected.SourceKind == 0
                    || (actualRow.Relationship != matchingExpected.Relationship
                        && (matchingExpected.Relationship != 3
                            || actualRow.Relationship is not (1 or 2))))
                {
                    return false;
                }
            }
        }
        return true;
    }

    internal static bool CanProject(PrimitiveType actualType, PrimitiveType expectedType,
        ImmutableArray<NativeFunctionPointerSignatureDefinition> signatures)
    {
        if (!IsValidType(actualType, signatures)
            || !IsValidType(expectedType, signatures))
        {
            return false;
        }

        var actual = signatures[NativeFunctionPointerTypes.Index(actualType)];
        var expected = signatures[NativeFunctionPointerTypes.Index(expectedType)];
        return (actual.EffectsMask & ~6u) == 0
            && (expected.EffectsMask & ~6u) == 0
            && actual.CallingConvention == expected.CallingConvention
            && actual.ReturnType == expected.ReturnType
            && actual.ParameterTypes.AsSpan().SequenceEqual(
                expected.ParameterTypes.AsSpan())
            && actual.Selectors.ToHashSet().SetEquals(expected.Selectors)
            && actual.OutputStates.ToHashSet().SetEquals(expected.OutputStates)
            && DependenciesSatisfy(actual.Dependencies, expected.Dependencies,
                actual.ParameterTypes.Length)
            && ((expected.EffectsMask & 2) == 0
                || (actual.EffectsMask & 2) != 0);
    }

    internal static bool MatchesAddressableFunction(BoundFunction function,
        PrimitiveType expectedType,
        ImmutableArray<NativeFunctionPointerSignatureDefinition> signatures)
    {
        return TryGetAddressableFunctionType(function, signatures, out var actualType)
            && CanProject(actualType, expectedType, signatures);
    }

    internal static bool TryCreateNativeImportSignature(BoundFunction function,
        ImmutableArray<NativeFunctionPointerSignatureDefinition> signatures,
        out NativeFunctionPointerSignatureDefinition? signature)
    {
        signature = null;
        if (function.DirectNativeImport is not DirectNativeImport import
            || !function.IsExternal || !function.RequiresUnsafeCaller
            || import.CallingConvention == NativeCallingConvention.Native
            || import.Outputs.IsDefault || !import.Outputs.IsEmpty
            || import.Parameters.IsDefault || !import.Parameters.IsEmpty
            || function.NativeOutputRole is not null
            || function.NativeOutputContracts.IsDefault
            || !function.NativeOutputContracts.IsEmpty
            || function.Variables.IsDefault || function.ParameterCount < 0
            || function.ParameterCount > function.Variables.Length
            || !TryCreateDependencies(function.DependencyContracts,
                out var dependencies))
        {
            return false;
        }

        var parameterTypes = ImmutableArray.CreateBuilder<PrimitiveType>(
            function.ParameterCount);
        for (var index = 0; index < function.ParameterCount; index++)
        {
            var parameter = function.Variables[index];
            if (parameter.IsOut || parameter.IsInit || parameter.IsImplicitRef)
            {
                return false;
            }
            parameterTypes.Add(parameter.Type);
        }

        var candidate = new NativeFunctionPointerSignatureDefinition(
            import.CallingConvention, function.ReturnType,
            parameterTypes.MoveToImmutable()) {
            EffectsMask = GetDeclaredEffectsMask(
                function.DoesNotReturn, function.DeclaredRequiresUnsafeCaller),
            Dependencies = dependencies,
        };
        if (!IsValidSignature(candidate, signatures))
        {
            return false;
        }

        signature = candidate;
        return true;
    }

    internal static bool TryGetAddressableFunctionType(BoundFunction function,
        ImmutableArray<NativeFunctionPointerSignatureDefinition> signatures,
        out PrimitiveType functionPointerType)
    {
        if (function.NativeFunctionPointerType is PrimitiveType nativeCallableType)
        {
            functionPointerType = nativeCallableType;
            return MatchesFunction(function, nativeCallableType, signatures);
        }
        if (function.DirectNativeImport is not null)
        {
            if (TryCreateNativeImportSignature(function, signatures,
                out var nativeImportSignature)
                && nativeImportSignature is not null)
            {
                for (var index = 0; index < signatures.Length; index++)
                {
                    if (AreEquivalent(nativeImportSignature, signatures[index]))
                    {
                        functionPointerType = NativeFunctionPointerTypes.Type(index);
                        return true;
                    }
                }
            }

            functionPointerType = default;
            return false;
        }
        if (!IsOrdinaryAddressableFunction(function))
        {
            functionPointerType = default;
            return false;
        }

        for (var index = 0; index < signatures.Length; index++)
        {
            if (MatchesOrdinaryFunction(function, signatures[index]))
            {
                functionPointerType = NativeFunctionPointerTypes.Type(index);
                return true;
            }
        }
        functionPointerType = default;
        return false;
    }

    private static bool IsOrdinaryAddressableFunction(BoundFunction function) =>
        !function.IsExternal && !function.IsModuleReference
            && function.DirectNativeImport is null && function.NativeVTableMethod is null
            && function.NativeCallableEntryName is null && function.NativeOutputRole is null
            && function.NativeFunctionPointerType is null
            && function.NativeCallableConvention is null
            && !function.IsConstructor && !function.IsClassConstructor
            && !function.IsOperator && !function.IsCleanup && function.PropertyName is null
            && function.Intrinsic == BoundIntrinsic.None
            && (function.DeclaringStruct is null || function.IsStatic)
            && function.ParameterCount <= function.Variables.Length;

    private static bool MatchesOrdinaryFunction(BoundFunction function,
        NativeFunctionPointerSignatureDefinition signature)
    {
        if (signature.CallingConvention != NativeCallingConvention.Native
            || function.ReturnType != signature.ReturnType
            || function.ParameterCount != signature.ParameterTypes.Length
            || signature.EffectsMask != GetDeclaredEffectsMask(
                function.DoesNotReturn, function.DeclaredRequiresUnsafeCaller)
            || !signature.Selectors.IsEmpty || !signature.OutputStates.IsEmpty
            || !TryCreateDependencies(function.DependencyContracts, out var dependencies)
            || !SameDependencies(signature.Dependencies, dependencies))
        {
            return false;
        }

        for (var index = 0; index < signature.ParameterTypes.Length; index++)
        {
            if (function.Variables[index] is { IsOut: false, IsInit: false, IsImplicitRef: false } parameter
                && parameter.Type == signature.ParameterTypes[index])
            {
                continue;
            }
            return false;
        }
        return true;
    }

    internal static bool IsSupportedValueType(PrimitiveType type,
        ImmutableArray<NativeFunctionPointerSignatureDefinition> signatures) =>
        type is PrimitiveType.Bool or PrimitiveType.Char8 or PrimitiveType.Char16 or PrimitiveType.Char32
            or PrimitiveType.Rune or PrimitiveType.Int8 or PrimitiveType.Int16 or PrimitiveType.Int32
            or PrimitiveType.Int64 or PrimitiveType.UInt8 or PrimitiveType.UInt16 or PrimitiveType.UInt32
            or PrimitiveType.UInt64 or PrimitiveType.Float32 or PrimitiveType.Float64
            or PrimitiveType.NativeRawPointer
            || NativePointerTypes.IsType(type)
            || (NativeFunctionPointerTypes.IsType(type)
                && (uint)NativeFunctionPointerTypes.Index(type) < (uint)signatures.Length);

    private static bool IsSupportedReturnType(PrimitiveType type,
        ImmutableArray<NativeFunctionPointerSignatureDefinition> signatures) =>
        type == PrimitiveType.Void || IsSupportedValueType(type, signatures);

    private static bool IsValidSignature(int index,
        ImmutableArray<NativeFunctionPointerSignatureDefinition> signatures, byte[] state, int depth)
    {
        if (depth > SemanticVerifier.MaximumDepth)
        {
            return false;
        }
        if (state[index] == 2)
        {
            return true;
        }
        if (state[index] == 1)
        {
            return false;
        }

        state[index] = 1;
        var signature = signatures[index];
        if (signature is null || !IsKnownConvention(signature.CallingConvention)
            || (signature.EffectsMask & ~6u) != 0
            || signature.ParameterTypes.IsDefault
            || !IsValidType(signature.ReturnType, signatures, state, allowVoid: true, depth: depth + 1))
        {
            return false;
        }
        foreach (var parameterType in signature.ParameterTypes)
        {
            if (!IsValidType(parameterType, signatures, state, allowVoid: false, depth: depth + 1))
            {
                return false;
            }
        }
        state[index] = 2;
        return true;
    }

    private static bool IsValidType(PrimitiveType type,
        ImmutableArray<NativeFunctionPointerSignatureDefinition> signatures, byte[] state, bool allowVoid,
        int depth)
    {
        if (NativeFunctionPointerTypes.IsType(type))
        {
            var index = NativeFunctionPointerTypes.Index(type);
            return (uint)index < (uint)signatures.Length
                && IsValidSignature(index, signatures, state, depth + 1);
        }

        return type == PrimitiveType.NativeRawPointer || NativePointerTypes.IsType(type)
            || type is PrimitiveType.Bool or PrimitiveType.Char8 or PrimitiveType.Char16 or PrimitiveType.Char32
                or PrimitiveType.Rune or PrimitiveType.Int8 or PrimitiveType.Int16 or PrimitiveType.Int32
                or PrimitiveType.Int64 or PrimitiveType.UInt8 or PrimitiveType.UInt16 or PrimitiveType.UInt32
                or PrimitiveType.UInt64 or PrimitiveType.Float32 or PrimitiveType.Float64
            || (allowVoid && type == PrimitiveType.Void);
    }
}
