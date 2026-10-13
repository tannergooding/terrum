// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System.Collections.Generic;

namespace Terrum.CodeAnalysis.Semantics;

public static partial class SemanticVerifier
{
    private static void VerifyNativeAggregateStorageAddress(BoundNativeStorageAddress address,
        NativeAggregateStorageParameterDefinition parameter,
        InitializationState initialized, uint[] fieldMasks, HashSet<int> declarations,
        BoundFunction function, BoundProgram program, ref int remaining, int depth)
    {
        Count(ref remaining, depth);
        var variable = address.Variable;
        // The catalogued no-escape loan is independent of runtime policy; validate its lifetime and contents here.
        if (!NativeStorageParameterContract.MatchesAggregateAddress(address, parameter, program)
            || variable < function.ParameterCount + (function.IsConstructor ? 1 : 0)
            || variable >= function.Variables.Length
            || !declarations.Contains(variable) || !initialized[variable]
            || function.Variables[variable].Type != address.CarrierType
            || fieldMasks[variable] != FullMask(program, address.CarrierType))
        {
            throw InvalidEffect(address.Span, "NativeImport");
        }
    }

    private static void VerifyNativeValueOutputAddress(BoundNativeStorageAddress address,
        NativeComParameterAdaptation adaptation, InitializationState initialized,
        HashSet<int> declarations, BoundFunction function, BoundProgram program,
        ref int remaining, int depth)
    {
        Count(ref remaining, depth);
        var variable = address.Variable;
        if (!NativeStorageParameterContract.MatchesValueOutputAddress(address, adaptation, program)
            || variable < function.ParameterCount + (function.IsConstructor ? 1 : 0)
            || variable >= function.Variables.Length
            || !declarations.Contains(variable) || !initialized[variable]
            || !function.Variables[variable].IsMutable
            || function.Variables[variable].Type != address.CarrierType)
        {
            throw InvalidEffect(address.Span, "NativeImport");
        }
    }
}
