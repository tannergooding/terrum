// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

namespace Terrum.CodeAnalysis.Semantics;

public static partial class SemanticVerifier
{
    private static void VerifyNativeMappedByteRead(BoundNativeMappedByteRead read,
        InitializationState initialized, uint[] fieldMasks, System.Collections.Generic.HashSet<int> declarations,
        BoundFunction function, BoundProgram program, ref int remaining, int depth, FailureEdges? failures)
    {
        Count(ref remaining, depth);
        if (function.IsExternal || function.IsNoThrow || function.IndexedBody is not null
            || read.Variable < function.ParameterCount + (function.IsConstructor ? 1 : 0)
            || read.Variable >= function.Variables.Length
            || !declarations.Contains(read.Variable) || !initialized[read.Variable]
            || !function.Variables[read.Variable].IsMutable
            || !NativeMappedBufferContract.Matches(read, function, program))
        {
            throw InvalidEffect(read.Span, "NativeImport");
        }

        Expression(read.Index, initialized, fieldMasks, declarations, function, program,
            ref remaining, depth + 1, failures);
    }
}
