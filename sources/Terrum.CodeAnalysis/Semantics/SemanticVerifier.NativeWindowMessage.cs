// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System.Collections.Generic;

namespace Terrum.CodeAnalysis.Semantics;

public static partial class SemanticVerifier
{
    private static void VerifyNativeWindowMessageAddress(BoundExpression expression,
        BoundFunction target, int parameter, InitializationState initialized, HashSet<int> declarations,
        BoundFunction function, BoundProgram program, ref int remaining, int depth)
    {
        Count(ref remaining, depth);
        if (expression is not BoundNativeWindowMessageAddress address
            || !NativeWindowMessageContract.IsMessageAddressArgument(
                expression, target, parameter, function, program)
            || address.Variable < 0 || address.Variable >= initialized.Length
            || !initialized[address.Variable] || !declarations.Contains(address.Variable)
            || function.Variables[address.Variable].Type != PrimitiveType.NativeWindowMessage)
        {
            throw InvalidEffect(expression.Span, "NativeImport");
        }

        if (!NativeWindowMessageContract.IsMessageAddressParameter(target, parameter, program))
        {
            throw InvalidEffect(expression.Span, "NativeImport");
        }
    }
}
