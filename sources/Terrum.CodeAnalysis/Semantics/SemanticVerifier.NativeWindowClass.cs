// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Generic;

namespace Terrum.CodeAnalysis.Semantics;

public static partial class SemanticVerifier
{
    private static void VerifyNativeWindowClassExInitialization(
        BoundNativeWindowClassExInitialization initializer, int variable,
        InitializationState initialized, uint[] fieldMasks, HashSet<int> declarations,
        BoundFunction function, BoundProgram program, ref int remaining, int depth, FailureEdges? failures)
    {
        if (variable < 0 || variable >= function.Variables.Length
            || !NativeWindowClassContract.MatchesCatalog(program.NativeImportCatalog)
            || function.Variables[variable].NativeWindowClassExFacts
                is not NativeWindowClassExFacts facts
            || NativeWindowClassContract.Facts(initializer, program.NativeUtf16ZLiterals) != facts
            || initializer.Callback.Function < 0
            || initializer.Callback.Function >= program.Functions.Length
            || !NativeWindowProcedureContract.MatchesFunction(
                program.Functions[initializer.Callback.Function])
            || initializer.Instance is not BoundCall moduleCall
            || moduleCall.Function < 0
            || moduleCall.Function >= program.Functions.Length
            || !NativeWindowClassContract.IsGetModuleHandleFunction(
                program.Functions[moduleCall.Function], program.NativeImportCatalog))
        {
            throw InvalidEffect(initializer.Span, "NativeImport");
        }

        Expression(initializer.Instance, initialized, fieldMasks, declarations, function, program,
            ref remaining, depth + 1, failures, allowCurrentModuleHandleValue: true);
        Expression(initializer.ClassName, initialized, fieldMasks, declarations, function, program,
            ref remaining, depth + 1, failures);
    }

    private static void VerifyNativeWindowClassExAddress(BoundExpression expression,
        BoundFunction target, int parameter, InitializationState initialized,
        HashSet<int> declarations, BoundFunction function, BoundProgram program,
        ref int remaining, int depth)
    {
        Count(ref remaining, depth);
        if (expression is not BoundNativeWindowClassExAddress address
            || !NativeWindowClassContract.IsWindowClassAddressArgument(
                expression, target, parameter, program)
            || address.Variable < 0 || address.Variable >= initialized.Length
            || !initialized[address.Variable]
            || !declarations.Contains(address.Variable)
            || function.Variables[address.Variable].Type != PrimitiveType.NativeWindowClassEx
            || function.Variables[address.Variable].NativeWindowClassExFacts is null)
        {
            throw InvalidEffect(expression.Span, "NativeImport");
        }
    }

    private static bool IsNullPointerParameter(BoundFunction function, int parameter, BoundProgram program)
    {
        if (!function.IsExternal || parameter < 0 || parameter >= function.ParameterCount
            || function.NativeImportKey is not string key
            || program.NativeImportCatalog is not NativeImportCatalog catalog
            || !catalog.TryGetImport(key, out var import) || import is null
            || !NativeImportCatalog.IsNullPointerParameter(import, parameter))
        {
            return false;
        }

        return true;
    }

    private static bool IsCurrentModuleHandleParameter(BoundFunction function, int parameter,
        BoundProgram program)
    {
        if (!function.IsExternal || parameter < 0 || parameter >= function.ParameterCount
            || function.NativeImportKey is not string key
            || program.NativeImportCatalog is not NativeImportCatalog catalog
            || !catalog.TryGetImport(key, out var import) || import is null
            || import.ParameterTypes[parameter] != PrimitiveType.NativeModuleHandle
            || import.ParameterContracts[parameter] != NativeImportParameterContract.CurrentModuleHandleNoEscape)
        {
            return false;
        }

        return NativeWindowClassContract.IsCurrentModuleHandleParameter(import, parameter)
            || NativeWindowCreationContract.IsCurrentModuleHandleParameter(import, parameter);
    }
}
