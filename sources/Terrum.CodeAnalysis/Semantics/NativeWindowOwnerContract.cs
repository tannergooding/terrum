// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Terrum.CodeAnalysis.Lexing;

namespace Terrum.CodeAnalysis.Semantics;

internal static class NativeWindowOwnerContract
{
    internal const string TypeName = "Triangle.Interop.WindowOwner";
    internal const string ConstructorName = TypeName + ".#ctor";
    internal const string RunName = TypeName + ".Run";
    internal const string CleanupName = TypeName + ".Close";
    internal const string FailureHelperName = TypeName + ".ExitIfFalse";
    internal const string CallbackName = "Triangle.Interop.Host.WindowProcedure";
    internal const string ClassName = "TerrumOwnerClass";
    internal const string ExitProcessKey = "windows-x64.system32.kernel32.ExitProcess";
    internal const string GetCurrentThreadIdKey = "windows-x64.system32.kernel32.GetCurrentThreadId";
    internal const string GetModuleHandleKey = NativeWindowClassContract.GetModuleHandleKey;
    internal const string RegisterClassKey = NativeWindowClassContract.RegisterClassExKey;
    internal const string UnregisterClassKey = NativeWindowClassContract.UnregisterClassKey;
    internal const string CreateWindowKey = NativeWindowCreationContract.CreateWindowExKey;
    internal const string DestroyWindowKey = NativeWindowCreationContract.DestroyWindowKey;

    internal static bool HasOwnerType(ImmutableArray<BoundStruct> structures) =>
        !structures.IsDefault && structures.Any(static structure => structure is not null
            && structure.Name == TypeName);

    internal static bool IsReservedTypeName(string name) => name == TypeName;

    internal static bool IsWindowHandleField(BoundStruct structure, BoundStructField field) =>
        structure.Name == TypeName && field.Name == "Handle"
        && field.Type == PrimitiveType.NativeWindowHandle && field.IsPrivate && !field.IsInternal
        && !field.IsMutable && !field.RequiresUnsafeCaller;

    internal static bool IsOwnerConstructor(BoundFunction function, BoundProgram program) =>
        function.Name == ConstructorName && function.DeclaringStruct == TypeName
        && function.IsConstructor && MatchesCatalog(program.NativeImportCatalog);

    internal static bool IsOwnerConstructor(BoundFunction function, NativeImportCatalog? catalog) =>
        function.Name == ConstructorName && function.DeclaringStruct == TypeName
        && function.IsConstructor && MatchesCatalog(catalog);

    internal static bool IsOwnedHandleExpression(BoundExpression expression, BoundFunction function,
        BoundProgram program) =>
        IsOwnerConstructor(function, program)
            && (expression is BoundNativeNoOwner
                || (expression is BoundCall { Type: PrimitiveType.NativeWindowHandle } call
                    && call.Function >= 0 && call.Function < program.Functions.Length
                    && NativeWindowCreationContract.IsCreateWindowFunction(
                        program.Functions[call.Function], program.NativeImportCatalog))
                || IsOwnerHandleFieldRead(expression, function, program));

    internal static bool IsOwnerHandleFieldRead(BoundExpression expression, BoundFunction function,
        BoundProgram program) =>
        expression is BoundFieldRead {
            Field: 0,
            Type: PrimitiveType.NativeWindowHandle,
        } field
        && IsOwnerFieldRead(field, function, program)
        && field.Receiver is BoundRead { Variable: 0 };

    private static bool IsOwnerFieldRead(BoundFieldRead field, BoundFunction function, BoundProgram program)
    {
        var ownerIndex = (int)OwnerType(program.Structures) - 100;
        return ownerIndex >= 0 && ownerIndex < program.Structures.Length
            && field.Field >= 0 && field.Field < program.Structures[ownerIndex].Fields.Length
            && field.Receiver is BoundRead { Variable: 0 } receiver
            && receiver.Type == OwnerType(program.Structures)
            && field.Type == program.Structures[ownerIndex].Fields[field.Field].Type
            && (function.Name == ConstructorName || function.Name == CleanupName || function.Name == RunName)
            && function.DeclaringStruct == TypeName
            && MatchesCatalog(program.NativeImportCatalog);
    }

    internal static bool IsOwnerHandleComparison(BoundNativeWindowHandleIsNoOwner comparison,
        BoundFunction function, BoundProgram program) =>
        (function.Name == ConstructorName || function.Name == CleanupName)
        && function.DeclaringStruct == TypeName
        && IsOwnerHandleFieldRead(comparison.Handle, function, program);

    internal static bool IsOwnedWindowArgument(BoundExpression argument, BoundFunction caller,
        BoundProgram program) =>
        (caller.Name is CleanupName or RunName) && caller.DeclaringStruct == TypeName
        && IsOwnerHandleFieldRead(argument, caller, program);

    internal static bool IsClassNameArgument(BoundExpression argument, BoundFunction target,
        int parameter, BoundFunction caller, BoundProgram program) =>
        caller.DeclaringStruct == TypeName
        && (caller.Name == ConstructorName || caller.Name == CleanupName)
        && ((target.NativeImportKey == CreateWindowKey && parameter == 1)
            || (target.NativeImportKey == UnregisterClassKey && parameter == 0))
        && IsClassNameLiteral(argument, program.NativeUtf16ZLiterals);

    internal static bool MatchesProgram(BoundProgram program)
    {
        return MatchesStandaloneProgram(program)
            || MatchesGenericOwnerProgram(program);
    }

    private static bool MatchesGenericOwnerProgram(BoundProgram program)
    {
        if (program.RuntimePolicy != BoundRuntimePolicy.CheckedExceptions
            || !MatchesCatalog(program.NativeImportCatalog)
            || program.Structures.IsDefault || program.Functions.IsDefault)
        {
            return false;
        }
        if (program.Structures.Count(static structure => structure?.Name == TypeName) != 1
            || program.Structures.Count(static structure => structure?.Name == "Triangle.Interop.Host") != 1
            || !MatchesOwnerStructure(program.Structures.Single(static structure => structure.Name == TypeName))
            || !MatchesHostStructure(program.Structures.Single(
                static structure => structure.Name == "Triangle.Interop.Host")))
        {
            return false;
        }
        if (!program.NativeUtf16ZLiterals.Contains(ClassName, StringComparer.Ordinal))
        {
            return false;
        }

        var functions = new Dictionary<string, BoundFunction>(program.Functions.Length, StringComparer.Ordinal);
        foreach (var function in program.Functions)
        {
            if (function is null || string.IsNullOrEmpty(function.Name)
                || function.Body is null || function.Variables.IsDefault
                || !functions.TryAdd(function.Name, function))
            {
                return false;
            }
        }

        if (!functions.TryGetValue(ConstructorName, out var constructor)
            || !functions.TryGetValue(FailureHelperName, out var failureHelper)
            || !functions.TryGetValue(CleanupName, out var cleanup)
            || !functions.TryGetValue(RunName, out var run)
            || !functions.TryGetValue(CallbackName, out var callback))
        {
            return false;
        }
        if (run.IsStatic || run.IsConstructor || run.IsCleanup || run.IsExternal
            || run.DoesNotReturn || run.ReturnType != PrimitiveType.Int32
            || run.ParameterCount != 1 || run.Variables.Length < 1
            || run.Variables[0].Type != OwnerType(program.Structures))
        {
            return false;
        }
        if (run.Body.Statements.IsEmpty
            || !IsFailureCheck(run.Body.Statements[0], run, program,
                new BoundBinary(TokenKind.EqualsEquals,
                    new BoundFieldRead(new BoundRead(0, OwnerType(program.Structures), default),
                        2, PrimitiveType.UInt32, default),
                    ImportCall(program, GetCurrentThreadIdKey), PrimitiveType.Bool, default)))
        {
            return false;
        }

        var matches = MatchesImports(program, functions)
            && NativeWindowProcedureContract.MatchesFunction(callback)
            && MatchesConstructor(constructor, program)
            && MatchesFailureHelper(failureHelper, program)
            && MatchesCleanup(cleanup, program);
        return matches;
    }

    private static bool MatchesStandaloneProgram(BoundProgram program)
    {
        if (program.RuntimePolicy != BoundRuntimePolicy.CheckedExceptions
            || !MatchesCatalog(program.NativeImportCatalog)
            || program.Structures.IsDefault || program.Structures.Length != 2
            || program.Functions.IsDefault || program.Functions.Length != 19
            || !program.Classes.IsEmpty || !program.Buffers.IsEmpty
            || !program.NativeUtf8ZLiterals.IsEmpty
            || program.NativeUtf16ZLiterals.Length != 2
            || !program.NativeUtf16ZLiterals.Contains(ClassName, StringComparer.Ordinal))
        {
            return false;
        }
        if (program.Structures.Any(static structure => structure is null))
        {
            return false;
        }

        var owners = program.Structures.Where(static structure => structure.Name == TypeName).ToArray();
        var hosts = program.Structures.Where(static structure => structure.Name == "Triangle.Interop.Host").ToArray();
        if (owners.Length != 1 || hosts.Length != 1
            || !MatchesOwnerStructure(owners[0]) || !MatchesHostStructure(hosts[0]))
        {
            return false;
        }

        var functions = new Dictionary<string, BoundFunction>(program.Functions.Length, StringComparer.Ordinal);
        foreach (var function in program.Functions)
        {
            if (function is null || string.IsNullOrEmpty(function.Name)
                || function.Body is null || function.Variables.IsDefault
                || function.Variables.Any(static variable => variable is null)
                || function.ParameterCount < 0 || function.ParameterCount > function.Variables.Length
                || !functions.TryAdd(function.Name, function))
            {
                return false;
            }
        }
        var expectedNames = new[] {
            "Triangle.Interop.ExitProcess",
            "Triangle.Interop.GetCurrentThreadId",
            "Triangle.Interop.GetModuleHandleW",
            "Triangle.Interop.RegisterClassExW",
            "Triangle.Interop.UnregisterClassW",
            "Triangle.Interop.CreateWindowExW",
            "Triangle.Interop.DestroyWindow",
            "Triangle.Interop.ShowWindow",
            "Triangle.Interop.GetMessageW",
            "Triangle.Interop.TranslateMessage",
            "Triangle.Interop.DispatchMessageW",
            "Triangle.Interop.DefWindowProcW",
            "Triangle.Interop.PostQuitMessage",
            CallbackName,
            ConstructorName,
            FailureHelperName,
            CleanupName,
            RunName,
            "Triangle.Interop.Main",
        };
        if (functions.Count != expectedNames.Length || !expectedNames.All(functions.ContainsKey))
        {
            return false;
        }

        return MatchesImports(program, functions)
            && MatchesCallback(functions[CallbackName], program)
            && MatchesConstructor(functions[ConstructorName], program)
            && MatchesFailureHelper(functions[FailureHelperName], program)
            && MatchesCleanup(functions[CleanupName], program)
            && MatchesRun(functions[RunName], program)
            && MatchesEntryPoint(functions["Triangle.Interop.Main"], functions[ConstructorName],
                functions[CleanupName], functions[RunName], program);
    }

    internal static bool MatchesCatalog(NativeImportCatalog? catalog) =>
        catalog is { IsForTesting: false, Identity: NativeWindowClassContract.CatalogIdentity }
        && NativeWindowClassContract.MatchesCatalog(catalog)
        && NativeWindowCreationContract.MatchesCatalog(catalog)
        && NativeWindowMessageContract.MatchesCatalog(catalog)
        && catalog.TryGetImport(ExitProcessKey, out var exitProcess)
        && exitProcess is not null && exitProcess.DllName.Equals("kernel32.dll", StringComparison.OrdinalIgnoreCase)
        && exitProcess.ExportName == "ExitProcess"
        && exitProcess.ParameterTypes.AsSpan().SequenceEqual([PrimitiveType.UInt32])
        && exitProcess.ReturnType == PrimitiveType.Void && exitProcess.IsNoThrow
        && exitProcess.RequiresUnsafeCaller && exitProcess.DoesNotReturn
        && exitProcess.DeploymentPolicy == NativeImportDeploymentPolicy.System32
        && catalog.TryGetImport(GetCurrentThreadIdKey, out var threadId)
        && threadId is not null && threadId.DllName.Equals("kernel32.dll", StringComparison.OrdinalIgnoreCase)
        && threadId.ExportName == "GetCurrentThreadId" && threadId.ParameterTypes.IsEmpty
        && threadId.ReturnType == PrimitiveType.UInt32 && threadId.IsNoThrow
        && threadId.RequiresUnsafeCaller && !threadId.DoesNotReturn
        && threadId.DeploymentPolicy == NativeImportDeploymentPolicy.System32;

    internal static bool MatchesOwnerStructure(BoundStruct structure) =>
        structure.Name == TypeName && structure.IsUnique && !structure.IsSequential
        && !structure.IsTransparent && structure.Size == 16 && structure.CleanupMethod == CleanupName
        && structure.Fields.Length == 3
        && structure.Fields[0] is BoundStructField handle
        && IsWindowHandleField(structure, handle)
        && handle.Offset == 0 && handle.Size == 8
        && structure.Fields[1] is BoundStructField atom
        && IsField(atom, "Atom", PrimitiveType.Char16, 8, 2, isPrivate: true)
        && structure.Fields[2] is BoundStructField threadId
        && IsField(threadId, "ThreadId", PrimitiveType.UInt32, 12, 4, isPrivate: true);

    internal static bool MatchesHostStructure(BoundStruct structure) =>
        structure.Name == "Triangle.Interop.Host" && !structure.IsUnique
        && structure.Fields.Length == 1 && structure.Fields[0] is { Type: PrimitiveType.Int32 };

    private static bool IsField(BoundStructField field, string name, PrimitiveType type, int offset, int size,
        bool isPrivate) =>
        field.Name == name && field.Type == type && field.Offset == offset && field.Size == size
        && field.IsPrivate == isPrivate && !field.IsInternal && !field.IsMutable
        && !field.RequiresUnsafeCaller;

    internal static bool MatchesImports(BoundProgram program,
        IReadOnlyDictionary<string, BoundFunction> functions)
    {
        var imports = new[] {
            (Name: "Triangle.Interop.ExitProcess", Key: ExitProcessKey),
            (Name: "Triangle.Interop.GetCurrentThreadId", Key: GetCurrentThreadIdKey),
            (Name: "Triangle.Interop.GetModuleHandleW", Key: GetModuleHandleKey),
            (Name: "Triangle.Interop.RegisterClassExW", Key: RegisterClassKey),
            (Name: "Triangle.Interop.UnregisterClassW", Key: UnregisterClassKey),
            (Name: "Triangle.Interop.CreateWindowExW", Key: CreateWindowKey),
            (Name: "Triangle.Interop.DestroyWindow", Key: DestroyWindowKey),
            (Name: "Triangle.Interop.ShowWindow", Key: NativeWindowMessageContract.ShowWindowKey),
            (Name: "Triangle.Interop.GetMessageW", Key: NativeWindowMessageContract.GetMessageKey),
            (Name: "Triangle.Interop.TranslateMessage", Key: NativeWindowMessageContract.TranslateMessageKey),
            (Name: "Triangle.Interop.DispatchMessageW", Key: NativeWindowMessageContract.DispatchMessageKey),
            (Name: "Triangle.Interop.DefWindowProcW", Key: NativeWindowMessageContract.DefWindowProcKey),
            (Name: "Triangle.Interop.PostQuitMessage", Key: NativeWindowMessageContract.PostQuitMessageKey),
        };
        return imports.All(import => functions[import.Name] is { IsExternal: true } function
            && function.NativeImportKey == import.Key
            && program.NativeImportCatalog is NativeImportCatalog catalog
            && catalog.TryGetImport(import.Key, out var definition)
            && definition is not null && catalog.MatchesFunction(function, definition));
    }

    internal static bool MatchesCallback(BoundFunction function, BoundProgram program) =>
        function.Name == CallbackName && function.DeclaringStruct == "Triangle.Interop.Host"
        && function.IsStatic && NativeWindowProcedureContract.MatchesFunction(function)
        && function.Body.Statements.Length == 2
        && function.Body.Statements[0] is BoundIf {
            Condition: BoundBinary {
                Operator: TokenKind.EqualsEquals,
                Left: BoundRead { Variable: 1, Type: PrimitiveType.UInt32 },
                Right: BoundConstant { Type: PrimitiveType.UInt32, Value: 16 },
                Type: PrimitiveType.Bool,
            },
            Else: null,
        } close
        && close.Then is BoundBlock { Statements.Length: 2 } closeBody
        && closeBody.Statements[0] is BoundExpressionStatement {
            Expression: BoundCall { Arguments.Length: 1 } postQuit,
        }
        && IsImportCall(postQuit, program, NativeWindowMessageContract.PostQuitMessageKey)
        && postQuit.Arguments[0] is BoundConstant { Type: PrimitiveType.Int32, Value: 0 }
        && closeBody.Statements[1] is BoundReturn {
            Value: BoundConstant { Type: PrimitiveType.Int64, Value: 0 },
        }
        && function.Body.Statements[1] is BoundReturn {
            Value: BoundCall { Arguments.Length: 4 } defWindowProc,
        }
        && IsImportCall(defWindowProc, program, NativeWindowMessageContract.DefWindowProcKey)
        && defWindowProc.Arguments[0] is BoundRead { Variable: 0, Type: PrimitiveType.NativeWindowHandle }
        && defWindowProc.Arguments[1] is BoundRead { Variable: 1, Type: PrimitiveType.UInt32 }
        && defWindowProc.Arguments[2] is BoundRead { Variable: 2, Type: PrimitiveType.UInt64 }
        && defWindowProc.Arguments[3] is BoundRead { Variable: 3, Type: PrimitiveType.Int64 }
        && CallsMatch(function, program, [
            NativeWindowMessageContract.PostQuitMessageKey, NativeWindowMessageContract.DefWindowProcKey,
        ]);

    internal static bool MatchesConstructor(BoundFunction function, BoundProgram program)
    {
        if (!IsOwnerConstructor(function, program) || function.ParameterCount != 0
            || function.IsStatic || function.IsNoThrow || function.IsCleanup || function.DoesNotReturn
            || function.ReturnType != OwnerType(program.Structures)
            || function.Variables.Length != 2 || function.Variables[0].Type != OwnerType(program.Structures))
        {
            return false;
        }

        var handleField = 0;
        var atomField = 1;
        var threadIdField = 2;
        if (function.Body.Statements.Length != 8)
        {
            return false;
        }
        if (!IsFieldAssignment(function.Body.Statements[0], handleField,
            static value => value is BoundNativeNoOwner))
        {
            return false;
        }
        if (!IsFieldAssignment(function.Body.Statements[1], atomField,
            static value => value is BoundConstant { Type: PrimitiveType.Char16, Value: 0 }))
        {
            return false;
        }
        if (!IsFieldAssignment(function.Body.Statements[2], threadIdField,
            value => IsImportCall(value, program, GetCurrentThreadIdKey)))
        {
            return false;
        }
        if (function.Body.Statements[3] is not BoundRequire {
                Condition: BoundBinary {
                    Operator: TokenKind.BangEquals,
                    Left: BoundFieldRead { Field: 2 },
                    Right: BoundConstant { Type: PrimitiveType.UInt32, Value: 0 },
                },
            })
        {
            return false;
        }
        if (function.Body.Statements[4] is not BoundLocal {
                Variable: var descriptorVariable,
                Initializer: BoundNativeWindowClassExInitialization,
            })
        {
            return false;
        }
        if (!IsFieldAssignment(function.Body.Statements[5], atomField,
            value => IsImportCall(value, program, RegisterClassKey)))
        {
            return false;
        }
        if (function.Body.Statements[6] is not BoundTryFinally cleanup)
        {
            return false;
        }
        if (function.Body.Statements[7] is not BoundReturn {
                Value: BoundRead { Variable: 0, Type: var returnType },
            })
        {
            return false;
        }
        if (returnType != OwnerType(program.Structures))
        {
            return false;
        }

        var descriptor = (BoundLocal)function.Body.Statements[4];
        var register = FieldAssignment(function.Body.Statements[5]);
        var registerCall = (BoundCall)register.Value;
        if (registerCall.Arguments.Length != 1)
        {
            return false;
        }
        if (registerCall.Arguments[0] is not BoundNativeWindowClassExAddress {
                Variable: var registeredDescriptor,
            })
        {
            return false;
        }
        if (registeredDescriptor != descriptorVariable)
        {
            return false;
        }
        if (cleanup.Body.Statements.Length != 3)
        {
            return false;
        }
        if (cleanup.Body.Statements[0] is not BoundRequire {
                Condition: BoundBinary {
                    Operator: TokenKind.BangEquals,
                    Left: BoundFieldRead { Field: 1 },
                    Right: BoundConstant { Type: PrimitiveType.Char16, Value: 0 },
                },
            })
        {
            return false;
        }
        if (!IsFieldAssignment(cleanup.Body.Statements[1], handleField,
                value => IsImportCall(value, program, CreateWindowKey)))
        {
            return false;
        }
        if (cleanup.Body.Statements[2] is not BoundRequire {
                Condition: BoundNativeWindowHandleIsNoOwner {
                    IsNegated: true,
                },
            })
        {
            return false;
        }
        if (cleanup.Finally.Statements.Length != 1)
        {
            return false;
        }
        if (cleanup.Finally.Statements[0] is not BoundIf rollback)
        {
            return false;
        }
        if (!IsRollbackCondition(rollback.Condition, function, program))
        {
            return false;
        }
        if (rollback.Else is not null)
        {
            return false;
        }
        if (!CallsMatch(function, program, [
                GetCurrentThreadIdKey, GetModuleHandleKey, RegisterClassKey, GetModuleHandleKey,
                CreateWindowKey, GetModuleHandleKey, UnregisterClassKey, FailureHelperName,
            ]))
        {
            return false;
        }

        var createCall = (BoundCall)FieldAssignment(cleanup.Body.Statements[1]).Value;
        return IsCreateWindowCall(createCall, function, program, descriptorVariable)
            && IsRollbackCall(rollback.Then, program);
    }

    internal static bool MatchesFailureHelper(BoundFunction function, BoundProgram program)
    {
        if (function.Name != FailureHelperName || function.DeclaringStruct != TypeName
            || !function.IsPrivate || !function.IsStatic || !function.IsNoThrow
            || function.IsCleanup || function.IsConstructor || function.DoesNotReturn
            || function.ReturnType != PrimitiveType.Void || function.ParameterCount != 1
            || function.Variables.Length != 1 || function.Variables[0].Type != PrimitiveType.Bool
            || function.Body.Statements.Length != 2
            || function.Body.Statements[0] is not BoundIf conditional
            || conditional.Else is not null
            || function.Body.Statements[1] is not BoundReturn { Value: null }
            || conditional.Condition is not BoundUnary {
                Operand: BoundRead { Variable: 0, Type: PrimitiveType.Bool },
            } notCondition
            || notCondition.Operator != TokenKind.Bang)
        {
            return false;
        }

        return TryGetDirectCallStatement(conditional.Then, out var call)
            && call is { Arguments.Length: 1 }
            && IsImportCall(call, program, ExitProcessKey)
            && call.Arguments[0] is BoundConstant { Type: PrimitiveType.UInt32, Value: 87 };
    }

    internal static bool MatchesCleanup(BoundFunction function, BoundProgram program)
    {
        if (function.Name != CleanupName || function.DeclaringStruct != TypeName
            || function.IsStatic || function.IsConstructor || !function.IsCleanup || !function.IsNoThrow
            || function.DoesNotReturn
            || function.ReturnType != PrimitiveType.Void || function.ParameterCount != 1
            || function.Variables.Length != 1 || function.Variables[0].Type != OwnerType(program.Structures)
            || function.Body.Statements.Length != 4
            || function.Body.Statements[3] is not BoundReturn { Value: null }
            || !IsFailureCheck(function.Body.Statements[0], function, program,
                new BoundBinary(TokenKind.EqualsEquals,
                    new BoundFieldRead(new BoundRead(0, OwnerType(program.Structures), default),
                        2, PrimitiveType.UInt32, default),
                    ImportCall(program, GetCurrentThreadIdKey), PrimitiveType.Bool, default))
            || !IsFailureCheck(function.Body.Statements[1], function, program,
                new BoundBinary(TokenKind.BangEquals,
                    ImportCall(program, DestroyWindowKey,
                        new BoundFieldRead(new BoundRead(0, OwnerType(program.Structures), default),
                            0, PrimitiveType.NativeWindowHandle, default)),
                    new BoundConstant(0, PrimitiveType.Int32, default), PrimitiveType.Bool, default))
            || !IsUnregisterFailureCheck(function.Body.Statements[2], program)
            || !CallsMatch(function, program, [
                GetCurrentThreadIdKey, FailureHelperName, DestroyWindowKey, FailureHelperName,
                GetModuleHandleKey, UnregisterClassKey, FailureHelperName,
            ]))
        {
            return false;
        }

        var calls = new List<BoundCall>();
        if (!TryCollectCalls(function.Body, calls))
        {
            return false;
        }
        var destroy = calls.SingleOrDefault(call => ImportKey(call, program) == DestroyWindowKey);
        return destroy is { Arguments.Length: 1 }
            && NativeWindowOwnerContract.IsOwnedWindowArgument(
                destroy.Arguments[0], function, program);
    }

    private static bool MatchesRun(BoundFunction function, BoundProgram program)
    {
        if (function.Name != RunName || function.DeclaringStruct != TypeName
            || function.IsStatic || function.IsConstructor || function.IsCleanup || !function.IsNoThrow
            || function.DoesNotReturn || function.IsExternal || function.ReturnType != PrimitiveType.Int32
            || function.ParameterCount != 1 || function.Variables.Length != 3
            || function.Variables[0].Type != OwnerType(program.Structures)
            || function.Variables[1] is not {
                Type: PrimitiveType.NativeWindowMessage,
                IsMutable: true,
                IsOut: false,
                IsInit: false,
            }
            || function.Variables[2] is not {
                Type: PrimitiveType.Int32,
                IsMutable: true,
                IsOut: false,
                IsInit: false,
            }
            || function.Body.Statements.Length != 6
            || !IsFailureCheck(function.Body.Statements[0], function, program,
                new BoundBinary(TokenKind.EqualsEquals,
                    new BoundFieldRead(new BoundRead(0, OwnerType(program.Structures), default),
                        2, PrimitiveType.UInt32, default),
                    ImportCall(program, GetCurrentThreadIdKey), PrimitiveType.Bool, default)))
        {
            return false;
        }

        if (function.Body.Statements[1] is not BoundExpressionStatement {
                Expression: BoundCall { Arguments.Length: 2 } showWindow,
            }
            || !IsImportCall(showWindow, program, NativeWindowMessageContract.ShowWindowKey)
            || !IsOwnedWindowArgument(showWindow.Arguments[0], function, program)
            || showWindow.Arguments[1] is not BoundConstant { Type: PrimitiveType.Int32, Value: 5 }
            || function.Body.Statements[2] is not BoundLocal {
                Variable: 1,
                Initializer: BoundNativeWindowMessageInitialization,
            }
            || function.Body.Statements[3] is not BoundLocal {
                Variable: 2,
                Initializer: BoundCall { Arguments.Length: 4 } firstGetMessage,
            }
            || !IsGetMessageCall(firstGetMessage, program, messageVariable: 1)
            || function.Body.Statements[4] is not BoundWhile {
                Condition: BoundBinary {
                    Operator: TokenKind.Greater,
                    Left: BoundRead { Variable: 2, Type: PrimitiveType.Int32 },
                    Right: BoundConstant { Type: PrimitiveType.Int32, Value: 0 },
                    Type: PrimitiveType.Bool,
                },
                Iterator: null,
                Body: BoundBlock { Statements.Length: 3 } loopBody,
            }
            || loopBody.Statements[0] is not BoundExpressionStatement {
                Expression: BoundCall { Arguments.Length: 1 } translate,
            }
            || !IsImportCall(translate, program, NativeWindowMessageContract.TranslateMessageKey)
            || translate.Arguments[0] is not BoundNativeWindowMessageAddress { Variable: 1 }
            || loopBody.Statements[1] is not BoundExpressionStatement {
                Expression: BoundCall { Arguments.Length: 1 } dispatch,
            }
            || !IsImportCall(dispatch, program, NativeWindowMessageContract.DispatchMessageKey)
            || dispatch.Arguments[0] is not BoundNativeWindowMessageAddress { Variable: 1 }
            || loopBody.Statements[2] is not BoundAssignment {
                Variable: 2,
                Value: BoundCall { Arguments.Length: 4 } nextGetMessage,
            }
            || !IsGetMessageCall(nextGetMessage, program, messageVariable: 1)
            || function.Body.Statements[5] is not BoundReturn {
                Value: BoundConditional {
                    Condition: BoundBinary {
                        Operator: TokenKind.Less,
                        Left: BoundRead { Variable: 2, Type: PrimitiveType.Int32 },
                        Right: BoundConstant { Type: PrimitiveType.Int32, Value: 0 },
                        Type: PrimitiveType.Bool,
                    },
                    WhenTrue: BoundConstant { Type: PrimitiveType.Int32, Value: 1 },
                    WhenFalse: BoundConstant { Type: PrimitiveType.Int32, Value: 0 },
                    Type: PrimitiveType.Int32,
                },
            })
        {
            return false;
        }

        return CallsMatch(function, program, [
            GetCurrentThreadIdKey, FailureHelperName, NativeWindowMessageContract.ShowWindowKey,
            NativeWindowMessageContract.GetMessageKey, NativeWindowMessageContract.TranslateMessageKey,
            NativeWindowMessageContract.DispatchMessageKey, NativeWindowMessageContract.GetMessageKey,
        ]);
    }

    private static bool IsGetMessageCall(BoundCall call, BoundProgram program, int messageVariable) =>
        IsImportCall(call, program, NativeWindowMessageContract.GetMessageKey)
        && call.Arguments.Length == 4
        && call.Arguments[0] is BoundNativeWindowMessageAddress { Variable: var variable }
        && variable == messageVariable
        && call.Arguments[1] is BoundNativeNoOwner
        && call.Arguments[2] is BoundConstant { Type: PrimitiveType.UInt32, Value: 0 }
        && call.Arguments[3] is BoundConstant { Type: PrimitiveType.UInt32, Value: 0 };

    internal static bool MatchesEntryPoint(BoundFunction function, BoundFunction constructor,
        BoundFunction cleanup, BoundFunction run, BoundProgram program)
    {
        if (function.Name != "Triangle.Interop.Main" || function.IsExternal
            || function.ParameterCount != 0 || function.ReturnType != PrimitiveType.Int32
            || function.DeclaringStruct is not null || function.IsConstructor || function.IsCleanup
            || function.DoesNotReturn
            || function.Variables.Length != 1
            || function.Body.Statements.Length != 2
            || function.Body.Statements.OfType<BoundLocal>().Count(local =>
                local.Variable >= 0 && local.Variable < function.Variables.Length
                && function.Variables[local.Variable].Type == OwnerType(program.Structures)) != 1)
        {
            return false;
        }

        var local = function.Body.Statements.OfType<BoundLocal>().Single();
        if (local.Variable != 0 || local.Initializer is not BoundCall constructorCall
            || constructorCall.Function < 0 || constructorCall.Function >= program.Functions.Length
            || !ReferenceEquals(program.Functions[constructorCall.Function], constructor)
            || constructorCall.Arguments.Length != 0)
        {
            return false;
        }
        if (function.Body.Statements[1] is not BoundTryFinally cleanupScope
            || cleanupScope.Body.Statements.Length != 1
            || cleanupScope.Body.Statements[0] is not BoundReturn {
                Value: BoundCall { Arguments.Length: 1 } runCall,
            }
            || runCall.Function < 0 || runCall.Function >= program.Functions.Length
            || !ReferenceEquals(program.Functions[runCall.Function], run)
            || runCall.Arguments[0] is not BoundReceiverLoan {
                Variable: 0,
                Type: var receiverType,
            }
            || receiverType != OwnerType(program.Structures)
            || cleanupScope.Finally.Statements.Length != 1
            || cleanupScope.Finally.Statements[0] is not BoundDrop drop
            || drop.Variable != local.Variable
            || drop.CleanupFunction < 0 || drop.CleanupFunction >= program.Functions.Length
            || !ReferenceEquals(program.Functions[drop.CleanupFunction], cleanup))
        {
            return false;
        }
        return CallsMatch(function, program, [ConstructorName, RunName]);
    }

    private static bool IsRollbackCondition(BoundExpression expression, BoundFunction function,
        BoundProgram program) =>
        expression is BoundBinary {
            Operator: TokenKind.AmpersandAmpersand,
            Left: BoundNativeWindowHandleIsNoOwner { IsNegated: false },
            Right: BoundBinary {
                Operator: TokenKind.BangEquals,
                Left: BoundFieldRead { Field: 1 },
                Right: BoundConstant { Type: PrimitiveType.Char16, Value: 0 },
            },
        } condition
        && IsOwnerHandleComparison((BoundNativeWindowHandleIsNoOwner)condition.Left, function, program);

    private static bool IsRollbackCall(BoundStatement statement, BoundProgram program) => IsCheckedUnregisterStatement(statement, program);

    private static bool IsCreateWindowCall(BoundCall call, BoundFunction function,
        BoundProgram program, int descriptorVariable)
    {
        if (ImportKey(call, program) != CreateWindowKey         || call.Arguments.Length != 12
        || !IsClassNameLiteral(call.Arguments[1], program.NativeUtf16ZLiterals)
        || !IsClassNameLiteral(call.Arguments[2], program.NativeUtf16ZLiterals, expected: null)
            || call.Arguments[8] is not BoundNativeNoOwner
            || call.Arguments[9] is not BoundConstant { Type: PrimitiveType.NativeNullPointer, Value: 0 }
            || !IsCurrentModuleCall(call.Arguments[10], program)
            || call.Arguments[11] is not BoundConstant { Type: PrimitiveType.NativeNullPointer, Value: 0 })
        {
            return false;
        }

        return call.Arguments[0] is BoundConstant { Type: PrimitiveType.UInt32, Value: 0 }
            && call.Arguments[4] is BoundConstant { Type: PrimitiveType.Int32, Value: 100 }
            && call.Arguments[5] is BoundConstant { Type: PrimitiveType.Int32, Value: 100 }
            && call.Arguments[6] is BoundConstant { Type: PrimitiveType.Int32, Value: 800 }
            && call.Arguments[7] is BoundConstant { Type: PrimitiveType.Int32, Value: 600 }
            && call.Arguments[3] is BoundConstant { Type: PrimitiveType.UInt32, Value: 13238272 }
            && descriptorVariable >= 0
            && function.Variables[descriptorVariable].Type == PrimitiveType.NativeWindowClassEx;
    }

    private static bool IsFailureCheck(BoundStatement statement, BoundFunction function,
        BoundProgram program, BoundExpression expected) =>
        statement is BoundExpressionStatement {
            Expression: BoundCall { Arguments.Length: 1 } call,
        }
        && CallName(call, program) == FailureHelperName
        && call.Arguments[0] is BoundBinary { Operator: var op, Left: var left, Right: var right, Type: var type }
        && expected is BoundBinary expectedBinary
        && op == expectedBinary.Operator && type == PrimitiveType.Bool
        && MatchExpectedExpression(left, expectedBinary.Left, function, program)
        && MatchExpectedExpression(right, expectedBinary.Right, function, program);

    private static bool MatchExpectedExpression(BoundExpression actual, BoundExpression expected,
        BoundFunction function, BoundProgram program)
    {
        if (expected is BoundCall expectedCall)
        {
            var importKey = ImportKey(expectedCall, program);
            return actual is BoundCall actualCall && ImportKey(actualCall, program) == importKey
                && (importKey != DestroyWindowKey
                    || (actualCall.Arguments.Length == 1
                        && IsOwnedWindowArgument(actualCall.Arguments[0], function, program)));
        }
        if (expected is BoundFieldRead expectedField)
        {
            return actual is BoundFieldRead actualField
                && actualField.Field == expectedField.Field
                && actualField.Type == expectedField.Type
                && IsOwnerFieldRead(actualField, function, program);
        }
        return expected is BoundConstant expectedConstant
            && actual is BoundConstant actualConstant
            && expectedConstant.Type == actualConstant.Type && expectedConstant.Value == actualConstant.Value;
    }

    private static bool IsUnregisterFailureCheck(BoundStatement statement, BoundProgram program) => IsCheckedUnregisterStatement(statement, program);

    private static bool IsCheckedUnregisterStatement(BoundStatement statement, BoundProgram program)
    {
        if (!TryGetDirectCallStatement(statement, out var helper)
            || helper is not { Arguments.Length: 1 }
            || CallName(helper, program) != FailureHelperName
            || helper.Arguments[0] is not BoundBinary {
                Operator: TokenKind.BangEquals,
                Left: BoundCall unregister,
                Right: BoundConstant { Type: PrimitiveType.Int32, Value: 0 },
            })
        {
            return false;
        }

        return IsImportCall(unregister, program, UnregisterClassKey)
            && unregister.Arguments.Length == 2
            && IsClassNameLiteral(unregister.Arguments[0], program.NativeUtf16ZLiterals)
            && IsCurrentModuleCall(unregister.Arguments[1], program);
    }

    private static bool IsCurrentModuleCall(BoundExpression expression, BoundProgram program) =>
        expression is BoundCall { Arguments.Length: 1 } call
        && IsImportCall(call, program, GetModuleHandleKey)
        && call.Arguments[0] is BoundConstant { Type: PrimitiveType.NativeNullPointer, Value: 0 };

    private static bool IsClassNameLiteral(BoundExpression expression, ImmutableArray<string> literals,
        string? expected = ClassName) =>
        expression is BoundConstant {
            Type: PrimitiveType.NativeUtf16ZPointer,
            Value: >= 0,
        } constant
        && constant.Value < literals.Length
        && (expected is null || literals[(int)constant.Value] == expected);

    private static bool IsFieldAssignment(BoundStatement statement, int field,
        Func<BoundExpression, bool> valuePredicate) =>
        statement is BoundFieldAssignment { Variable: 0, Field: var actualField, Value: var value }
        && actualField == field && valuePredicate(value);

    private static BoundFieldAssignment FieldAssignment(BoundStatement statement) =>
        (BoundFieldAssignment)statement;

    private static bool TryGetDirectCallStatement(BoundStatement statement, out BoundCall? call)
    {
        while (statement is BoundBlock { Statements.Length: 1 } block)
        {
            statement = block.Statements[0];
        }

        if (statement is BoundExpressionStatement { Expression: BoundCall directCall })
        {
            call = directCall;
            return true;
        }

        call = null;
        return false;
    }

    private static bool IsImportCall(BoundExpression expression, BoundProgram program, string importKey) =>
        expression is BoundCall call && IsImportCall(call, program, importKey);

    private static bool IsImportCall(BoundCall call, BoundProgram program, string importKey) =>
        call.Function >= 0 && call.Function < program.Functions.Length
        && program.Functions[call.Function].IsExternal
        && program.Functions[call.Function].NativeImportKey == importKey
        && MatchesCatalog(program.NativeImportCatalog);

    private static BoundCall ImportCall(BoundProgram program, string importKey,
        params BoundExpression[] arguments)
    {
        var function = program.Functions.Single(item => item.NativeImportKey == importKey);
        return new BoundCall(program.Functions.IndexOf(function), [.. arguments], function.ReturnType, default);
    }

    private static bool CallsMatch(BoundFunction function, BoundProgram program,
        ImmutableArray<string> expected)
    {
        var calls = new List<BoundCall>();
        if (!TryCollectCalls(function.Body, calls))
        {
            return false;
        }
        var actual = calls.Select(call => CallIdentity(call, program)).ToArray();
        return actual.SequenceEqual(expected, StringComparer.Ordinal);
    }

    private static string? CallIdentity(BoundCall call, BoundProgram program) =>
        call.Function >= 0 && call.Function < program.Functions.Length
            ? program.Functions[call.Function].NativeImportKey ?? program.Functions[call.Function].Name
            : null;

    private static string? ImportKey(BoundCall call, BoundProgram program) =>
        call.Function >= 0 && call.Function < program.Functions.Length
            ? program.Functions[call.Function].NativeImportKey : null;

    private static string? CallName(BoundCall call, BoundProgram program) =>
        call.Function >= 0 && call.Function < program.Functions.Length
            ? program.Functions[call.Function].Name : null;

    internal static PrimitiveType OwnerType(ImmutableArray<BoundStruct> structures)
    {
        for (var index = 0; index < structures.Length; index++)
        {
            if (structures[index].Name == TypeName)
            {
                return (PrimitiveType)(100 + index);
            }
        }
        return PrimitiveType.Int32;
    }

    private static bool TryCollectCalls(BoundStatement statement, List<BoundCall> calls)
    {
        switch (statement)
        {
            case BoundBlock block:
                foreach (var child in block.Statements)
                {
                    if (!TryCollectCalls(child, calls))
                    {
                        return false;
                    }
                }
                return true;
            case BoundVacantLocal:
            case BoundLifetimeEnd:
            case BoundOwnerDeclaration:
            case BoundBreak:
            case BoundContinue:
            case BoundDrop:
            case BoundDropSpan:
            case BoundDropConstructedFields:
                return true;
            case BoundLocal local:
                return TryCollectCalls(local.Initializer, calls);
            case BoundAssignment assignment:
                return TryCollectCalls(assignment.Value, calls);
            case BoundOptionalAssignment assignment:
                return TryCollectCalls(assignment.Place, calls)
                    && TryCollectCalls(assignment.Value, calls);
            case BoundFieldAssignment assignment:
                return TryCollectCalls(assignment.Value, calls);
            case BoundBufferAssignment assignment:
                return TryCollectCalls(assignment.Index, calls)
                    && TryCollectCalls(assignment.Value, calls);
            case BoundExpressionStatement expression:
                return TryCollectCalls(expression.Expression, calls);
            case BoundIf conditional:
                if (!TryCollectCalls(conditional.Condition, calls)
                    || !TryCollectCalls(conditional.Then, calls))
                {
                    return false;
                }
                if (conditional.Else is not null)
                {
                    return TryCollectCalls(conditional.Else, calls);
                }
                return true;
            case BoundWhile loop:
                return TryCollectCalls(loop.Condition, calls)
                    && TryCollectCalls(loop.Body, calls)
                    && (loop.Iterator is null || TryCollectCalls(loop.Iterator, calls));
            case BoundTryFinally cleanup:
                return TryCollectCalls(cleanup.Body, calls)
                    && TryCollectCalls(cleanup.Finally, calls);
            case BoundTryCatch recovery:
                return TryCollectCalls(recovery.Body, calls)
                    && TryCollectCalls(recovery.Catch, calls)
                    && (recovery.Finally is null || TryCollectCalls(recovery.Finally, calls));
            case BoundViewScope view:
                return TryCollectCalls(view.Owner, calls)
                    && TryCollectCalls(view.Start, calls)
                    && TryCollectCalls(view.Length, calls)
                    && (view.RangeEnd is null || TryCollectCalls(view.RangeEnd, calls))
                    && TryCollectCalls(view.Body, calls);
            case BoundViewSet set:
                return TryCollectCalls(set.Index, calls)
                    && TryCollectCalls(set.Value, calls);
            case BoundRequire require:
                return TryCollectCalls(require.Condition, calls);
            case BoundReturn { Value: not null } returnStatement:
                return TryCollectCalls(returnStatement.Value, calls);
            case BoundReturn:
                return true;
            case BoundUnsafe unsafeBlock:
                return TryCollectCalls(unsafeBlock.Body, calls);
            default:
                return false;
        }
    }

    private static bool TryCollectCalls(BoundExpression expression, List<BoundCall> calls)
    {
        switch (expression)
        {
            case BoundCall call:
                foreach (var argument in call.Arguments)
                {
                    if (!TryCollectCalls(argument, calls))
                    {
                        return false;
                    }
                }
                calls.Add(call);
                return true;
            case BoundBinary binary:
                return TryCollectCalls(binary.Left, calls)
                    && TryCollectCalls(binary.Right, calls);
            case BoundUnary unary:
                return TryCollectCalls(unary.Operand, calls);
            case BoundConditional conditional:
                return TryCollectCalls(conditional.Condition, calls)
                    && TryCollectCalls(conditional.WhenTrue, calls)
                    && TryCollectCalls(conditional.WhenFalse, calls);
            case BoundCast cast:
                return TryCollectCalls(cast.Operand, calls);
            case BoundFieldRead field:
                return TryCollectCalls(field.Receiver, calls);
            case BoundNativeWindowClassExInitialization windowClass:
                return TryCollectCalls(windowClass.Instance, calls)
                    && TryCollectCalls(windowClass.ClassName, calls);
            case BoundNativeWindowMessageInitialization:
            case BoundNativeWindowMessageAddress:
                return true;
            case BoundNativeWindowHandleIsNoOwner handleTest:
                return TryCollectCalls(handleTest.Handle, calls);
            case BoundNativeFixedArrayInitialization fixedArray:
                return TryCollectCalls(fixedArray.Elements, calls);
            case BoundNativeFixedArrayElement fixedArrayElement:
                return TryCollectCalls(fixedArrayElement.Index, calls);
            case BoundNativeStorageAddress address:
                return TryCollectCalls(address.Index, calls);
            case BoundOptionalLift optional:
                return TryCollectCalls(optional.Value, calls);
            case BoundCoalesce coalesce:
                return TryCollectCalls(coalesce.Optional, calls)
                    && TryCollectCalls(coalesce.Fallback, calls);
            case BoundReadonlyProjection projection:
                return TryCollectCalls(projection.Value, calls);
            case BoundIsNull isNull:
                return TryCollectCalls(isNull.Value, calls);
            case BoundIsSome isSome:
                return TryCollectCalls(isSome.Value, calls);
            case BoundIsVariablePattern variablePattern:
                return TryCollectCalls(variablePattern.Value, calls);
            case BoundViewRead viewRead:
                return TryCollectCalls(viewRead.Index, calls);
            case BoundSpanLength spanLength:
                return TryCollectCalls(spanLength.View, calls);
            case BoundSpanRead spanRead:
                return TryCollectCalls(spanRead.View, calls)
                    && TryCollectCalls(spanRead.Index, calls);
            case BoundSpanAdvance spanAdvance:
                return TryCollectCalls(spanAdvance.View, calls);
            case BoundFieldLoan fieldLoan:
                return TryCollectCalls(fieldLoan.Place, calls);
            case BoundMutableFieldLoan fieldLoan:
                return TryCollectCalls(fieldLoan.Place, calls);
            case BoundUpdate update:
                return TryCollectCalls(update.Place, calls);
            case BoundCompoundAssignment compoundAssignment:
                return TryCollectCalls(compoundAssignment.Place, calls)
                    && TryCollectCalls(compoundAssignment.Right, calls);
            case BoundUnsafeExpression unsafeExpression:
                return TryCollectCalls(unsafeExpression.Expression, calls);
            case BoundBufferRead bufferRead:
                return TryCollectCalls(bufferRead.Index, calls);
            case BoundConstant:
            case BoundNativeNoOwner:
            case BoundNativeCallbackAddress:
            case BoundNativeWindowClassExAddress:
            case BoundNull:
            case BoundIsInitialized:
            case BoundRead:
            case BoundMove:
            case BoundReceiverLoan:
            case BoundMutableReceiverLoan:
            case BoundViewLength:
            case BoundBufferLength:
            case BoundSnapshotSpan:
            case BoundOutArgument:
                return true;
            default:
                return false;
        }
    }

    private static bool TryCollectCalls(ImmutableArray<BoundExpression> expressions, List<BoundCall> calls)
    {
        foreach (var expression in expressions)
        {
            if (!TryCollectCalls(expression, calls))
            {
                return false;
            }
        }
        return true;
    }
}
