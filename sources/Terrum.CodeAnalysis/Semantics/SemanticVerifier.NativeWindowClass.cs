// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Linq;

namespace Terrum.CodeAnalysis.Semantics;

public static partial class SemanticVerifier
{
    private static void VerifyNativeWindowClassLifecycles(BoundProgram program)
    {
        foreach (var function in program.Functions)
        {
            var hasWindowClassLocal = function.Variables.Any(static variable =>
                variable.Type == PrimitiveType.NativeWindowClassEx);
            var firstLocal = function.ParameterCount + (function.IsConstructor ? 1 : 0);
            var hasWindowHandleLocal = function.Variables.Skip(firstLocal).Any(static variable =>
                variable.Type == PrimitiveType.NativeWindowHandle);
            var events = new List<(string? ImportKey, bool IsReturn, bool InControlFlow, BoundCall? Call)>();
            CollectWindowClassEvents(function.Body, program, events, inControlFlow: false);
            var lifecycleEvents = events.Where(static item => item.ImportKey is
                NativeWindowClassContract.GetModuleHandleKey
                    or NativeWindowClassContract.RegisterClassExKey
                    or NativeWindowCreationContract.CreateWindowExKey
                    or NativeWindowCreationContract.DestroyWindowKey
                    or NativeWindowClassContract.UnregisterClassKey).ToArray();
            var hasRegistration = lifecycleEvents.Any(static item =>
                item.ImportKey == NativeWindowClassContract.RegisterClassExKey);
            if (!hasWindowClassLocal && !hasWindowHandleLocal && !hasRegistration)
            {
                continue;
            }
            var expected = new[] {
                NativeWindowClassContract.GetModuleHandleKey,
                NativeWindowClassContract.RegisterClassExKey,
                NativeWindowClassContract.GetModuleHandleKey,
                NativeWindowCreationContract.CreateWindowExKey,
                NativeWindowCreationContract.DestroyWindowKey,
                NativeWindowClassContract.GetModuleHandleKey,
                NativeWindowClassContract.UnregisterClassKey,
            };
            if (!hasWindowClassLocal
                || lifecycleEvents.Length != expected.Length
                || !lifecycleEvents.Where(static item => item.ImportKey is not null)
                    .Select(static item => item.ImportKey)
                    .SequenceEqual(expected, StringComparer.Ordinal)
                || (hasWindowHandleLocal
                    && !HasValidCreatedWindowLocal(function, program, lifecycleEvents)))
            {
                throw InvalidEffect(function.Span, "NativeImport");
            }

            var unregisterIndex = events.FindLastIndex(static item =>
                item.ImportKey == NativeWindowClassContract.UnregisterClassKey);
            if (unregisterIndex < 0
                || events.Take(unregisterIndex).Any(static item => item.IsReturn))
            {
                throw InvalidEffect(function.Span, "NativeImport");
            }
        }
    }

    private static bool HasValidCreatedWindowLocal(BoundFunction function, BoundProgram program,
        (string? ImportKey, bool IsReturn, bool InControlFlow, BoundCall? Call)[] events)
    {
        var variable = -1;
        for (var index = 0; index < function.Variables.Length; index++)
        {
            if (function.Variables[index].Type != PrimitiveType.NativeWindowHandle)
            {
                continue;
            }
            if (variable >= 0 || !NativeWindowCreationContract.IsCreatedWindowLocal(function, index, program))
            {
                return false;
            }
            variable = index;
        }
        if (variable < 0)
        {
            return false;
        }

        var locals = new List<BoundLocal>();
        CollectWindowHandleLocals(function.Body, function, locals);
        if (locals.Count != 1 || locals[0].Variable != variable
            || !NativeWindowCreationContract.IsCreatedWindowInitializer(
                locals[0].Initializer, variable, function, program))
        {
            return false;
        }

        var createEvent = events.SingleOrDefault(static item =>
            item.ImportKey == NativeWindowCreationContract.CreateWindowExKey);
        var destroyEvent = events.SingleOrDefault(static item =>
            item.ImportKey == NativeWindowCreationContract.DestroyWindowKey);
        return ReferenceEquals(createEvent.Call, locals[0].Initializer)
            && destroyEvent.Call is { Arguments.Length: 1 } destroyCall
            && destroyCall.ParameterIndex(0) == 0
            && destroyCall.Arguments[0] is BoundRead {
                Variable: var destroyedVariable,
                Type: PrimitiveType.NativeWindowHandle,
            }
            && destroyedVariable == variable;
    }

    private static void CollectWindowHandleLocals(BoundStatement statement, BoundFunction function,
        List<BoundLocal> locals)
    {
        switch (statement)
        {
            case BoundBlock block:
                foreach (var child in block.Statements)
                {
                    CollectWindowHandleLocals(child, function, locals);
                }
                break;
            case BoundLocal local when (uint)local.Variable < (uint)function.Variables.Length
                && function.Variables[local.Variable].Type == PrimitiveType.NativeWindowHandle:
                locals.Add(local);
                break;
            case BoundIf conditional:
                CollectWindowHandleLocals(conditional.Then, function, locals);
                if (conditional.Else is not null)
                {
                    CollectWindowHandleLocals(conditional.Else, function, locals);
                }
                break;
            case BoundWhile loop:
                CollectWindowHandleLocals(loop.Body, function, locals);
                if (loop.Iterator is not null)
                {
                    CollectWindowHandleLocals(loop.Iterator, function, locals);
                }
                break;
            case BoundTryFinally cleanup:
                CollectWindowHandleLocals(cleanup.Body, function, locals);
                CollectWindowHandleLocals(cleanup.Finally, function, locals);
                break;
            case BoundTryCatch recovery:
                CollectWindowHandleLocals(recovery.Body, function, locals);
                CollectWindowHandleLocals(recovery.Catch, function, locals);
                if (recovery.Finally is not null)
                {
                    CollectWindowHandleLocals(recovery.Finally, function, locals);
                }
                break;
            case BoundViewScope view:
                CollectWindowHandleLocals(view.Body, function, locals);
                break;
            case BoundUnsafe unsafeBlock:
                CollectWindowHandleLocals(unsafeBlock.Body, function, locals);
                break;
        }
    }

    private static void CollectWindowClassEvents(BoundStatement statement, BoundProgram program,
        List<(string? ImportKey, bool IsReturn, bool InControlFlow, BoundCall? Call)> events,
        bool inControlFlow)
    {
        switch (statement)
        {
            case BoundBlock block:
                foreach (var child in block.Statements)
                {
                    CollectWindowClassEvents(child, program, events, inControlFlow);
                }
                break;
            case BoundLocal local:
                CollectWindowClassEvents(local.Initializer, program, events, inControlFlow);
                break;
            case BoundExpressionStatement expressionStatement:
                CollectWindowClassEvents(expressionStatement.Expression, program, events, inControlFlow);
                break;
            case BoundReturn { Value: not null } returnStatement:
                CollectWindowClassEvents(returnStatement.Value, program, events, inControlFlow);
                events.Add((null, true, inControlFlow, null));
                break;
            case BoundReturn returnStatement:
                events.Add((null, true, inControlFlow, null));
                break;
            case BoundIf conditional:
                CollectWindowClassEvents(conditional.Condition, program, events, inControlFlow: true);
                CollectWindowClassEvents(conditional.Then, program, events, inControlFlow: true);
                if (conditional.Else is not null)
                {
                    CollectWindowClassEvents(conditional.Else, program, events, inControlFlow: true);
                }
                break;
            case BoundWhile loop:
                CollectWindowClassEvents(loop.Condition, program, events, inControlFlow: true);
                CollectWindowClassEvents(loop.Body, program, events, inControlFlow: true);
                if (loop.Iterator is not null)
                {
                    CollectWindowClassEvents(loop.Iterator, program, events, inControlFlow: true);
                }
                break;
            case BoundTryFinally cleanup:
                CollectWindowClassEvents(cleanup.Body, program, events, inControlFlow: true);
                CollectWindowClassEvents(cleanup.Finally, program, events, inControlFlow: true);
                break;
            case BoundTryCatch recovery:
                CollectWindowClassEvents(recovery.Body, program, events, inControlFlow: true);
                CollectWindowClassEvents(recovery.Catch, program, events, inControlFlow: true);
                if (recovery.Finally is not null)
                {
                    CollectWindowClassEvents(recovery.Finally, program, events, inControlFlow: true);
                }
                break;
            case BoundUnsafe unsafeBlock:
                CollectWindowClassEvents(unsafeBlock.Body, program, events, inControlFlow);
                break;
            case BoundRequire require:
                CollectWindowClassEvents(require.Condition, program, events, inControlFlow);
                break;
        }
    }

    private static void CollectWindowClassEvents(BoundExpression expression, BoundProgram program,
        List<(string? ImportKey, bool IsReturn, bool InControlFlow, BoundCall? Call)> events,
        bool inControlFlow)
    {
        switch (expression)
        {
            case BoundCall call:
                foreach (var argument in call.Arguments)
                {
                    CollectWindowClassEvents(argument, program, events, inControlFlow);
                }
                if (call.Function >= 0 && call.Function < program.Functions.Length)
                {
                    events.Add((program.Functions[call.Function].NativeImportKey, false, inControlFlow, call));
                }
                break;
            case BoundNativeWindowClassExInitialization windowClass:
                CollectWindowClassEvents(windowClass.Instance, program, events, inControlFlow);
                CollectWindowClassEvents(windowClass.ClassName, program, events, inControlFlow);
                break;
            case BoundUnary unary:
                CollectWindowClassEvents(unary.Operand, program, events, inControlFlow);
                break;
            case BoundBinary binary:
                CollectWindowClassEvents(binary.Left, program, events, inControlFlow);
                CollectWindowClassEvents(binary.Right, program, events, inControlFlow);
                break;
            case BoundConditional conditional:
                CollectWindowClassEvents(conditional.Condition, program, events, inControlFlow: true);
                CollectWindowClassEvents(conditional.WhenTrue, program, events, inControlFlow: true);
                CollectWindowClassEvents(conditional.WhenFalse, program, events, inControlFlow: true);
                break;
            case BoundCoalesce coalesce:
                CollectWindowClassEvents(coalesce.Optional, program, events, inControlFlow: true);
                CollectWindowClassEvents(coalesce.Fallback, program, events, inControlFlow: true);
                break;
            case BoundCast cast:
                CollectWindowClassEvents(cast.Operand, program, events, inControlFlow);
                break;
            case BoundUnsafeExpression unsafeExpression:
                CollectWindowClassEvents(unsafeExpression.Expression, program, events, inControlFlow);
                break;
            case BoundNativeWindowHandleIsNoOwner handleTest:
                CollectWindowClassEvents(handleTest.Handle, program, events, inControlFlow);
                break;
        }
    }

    private static void VerifyNativeWindowClassExInitialization(
        BoundNativeWindowClassExInitialization initializer, int variable,
        InitializationState initialized, uint[] fieldMasks, HashSet<int> declarations,
        BoundFunction function, BoundProgram program, ref int remaining, int depth, FailureEdges? failures)
    {
        if (variable < 0 || variable >= function.Variables.Length
            || !NativeWindowClassContract.MatchesCatalog(program.NativeImportCatalog)
            || (program.RuntimePolicy != BoundRuntimePolicy.FailFast
                && !NativeWindowOwnerContract.MatchesProgram(program))
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
            || function.Variables[address.Variable].NativeWindowClassExFacts is null
            || (program.RuntimePolicy != BoundRuntimePolicy.FailFast
                && !NativeWindowOwnerContract.MatchesProgram(program)))
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

    private static bool IsRegisteredWindowClassNameArgument(BoundExpression argument,
        BoundFunction target, int parameter, BoundFunction function, BoundProgram program)
    {
        var isClassNameSlot = target.NativeImportKey switch {
            NativeWindowCreationContract.CreateWindowExKey => parameter == 1,
            NativeWindowClassContract.UnregisterClassKey => parameter == 0,
            _ => false,
        };
        if (!isClassNameSlot)
        {
            return true;
        }
        if (NativeWindowOwnerContract.IsClassNameArgument(argument, target, parameter, function, program))
        {
            return true;
        }
        if (argument is not BoundConstant {
                Type: PrimitiveType.NativeUtf16ZPointer, Value: >= 0,
            } literal)
        {
            return false;
        }

        NativeWindowClassExFacts? facts = null;
        var foundWindowClass = false;
        foreach (var variable in function.Variables)
        {
            if (variable.Type != PrimitiveType.NativeWindowClassEx)
            {
                continue;
            }
            foundWindowClass = true;
            if (facts is not null || variable.NativeWindowClassExFacts is not NativeWindowClassExFacts localFacts)
            {
                return false;
            }
            facts = localFacts;
        }
        return facts is null
            ? !foundWindowClass && target.NativeImportKey == NativeWindowCreationContract.CreateWindowExKey
            : literal.Value == facts.ClassNameLiteralIndex
                && NativeWindowClassContract.MatchesCatalog(program.NativeImportCatalog);
    }
}
