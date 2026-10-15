// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System.Collections.Generic;

namespace Terrum.CodeAnalysis.Semantics;

public static partial class SemanticVerifier
{
    private static void VerifyDirectNativeOutputAddress(BoundNativeStorageAddress address,
        DirectNativeOutput output, InitializationState initialized, uint[] fieldMasks,
        HashSet<int> declarations, BoundFunction function, BoundProgram program,
        ref int remaining, int depth, FailureEdges? failures)
    {
        Count(ref remaining, depth);
        var firstLocal = function.ParameterCount + (function.IsConstructor ? 1 : 0);
        if (!DirectNativeImportContract.MatchesAddress(address, output, function, program)
            || address.Variable < firstLocal || !declarations.Contains(address.Variable)
            || !initialized[address.Variable])
        {
            throw InvalidEffect(address.Span, "NativeImport");
        }

        Expression(address.Index, initialized, fieldMasks, declarations, function, program,
            ref remaining, depth + 1, failures);
    }

    private static void VerifyDirectNativeParameterAddress(BoundNativeStorageAddress address,
        DirectNativeParameter parameter, InitializationState initialized, uint[] fieldMasks,
        HashSet<int> declarations, BoundFunction function, BoundProgram program,
        ref int remaining, int depth, FailureEdges? failures)
    {
        Count(ref remaining, depth);
        var firstLocal = function.ParameterCount + (function.IsConstructor ? 1 : 0);
        if (!DirectNativeImportContract.MatchesAddress(address, parameter, function, program)
            || address.Variable < firstLocal || !declarations.Contains(address.Variable)
            || !initialized[address.Variable])
        {
            throw InvalidEffect(address.Span, "NativeImport");
        }

        Expression(address.Index, initialized, fieldMasks, declarations, function, program,
            ref remaining, depth + 1, failures);
    }
}
