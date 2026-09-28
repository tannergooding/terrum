// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Terrum.CodeAnalysis.Lexing;
using Terrum.CodeAnalysis.Text;

namespace Terrum.CodeAnalysis.Semantics;

/// <summary>Validates untrusted structured IR before a native backend consumes it.</summary>
public static class SemanticVerifier
{
    private const string FunctionIndexKey = "Terrum.FunctionIndex";
    private const string SourceSpanKey = "Terrum.SourceSpan";
    private const string InvalidEffectKey = "Terrum.InvalidEffect";
    internal const int MaximumFunctions = 1024;
    internal const int MaximumVariables = 256;
    internal const int MaximumArguments = 64;
    internal const int MaximumDepth = 128;
    internal const int MaximumAnalysisVisits = 100_000;

    /// <summary>Checks types, control flow, slot mutation, definite assignment, and boundedness of a program. The node limit counts analysis visits, including repeated outcome and feasibility checks.</summary>
    /// <exception cref="ArgumentException">The program is malformed.</exception>
    /// <exception cref="NotSupportedException">The program exceeds an emitter resource limit.</exception>
    public static void Verify(BoundProgram program)
    {
        ArgumentNullException.ThrowIfNull(program);
        VerifyCore(program, requireEntryPoint: true);
    }

    /// <summary>Checks a fully defined library with no process entry point. Libraries cannot be emitted as executables without linking.</summary>
    /// <exception cref="ArgumentException">The library is malformed or contains an entry point.</exception>
    /// <exception cref="NotSupportedException">The library exceeds verification limits.</exception>
    public static void VerifyLibrary(BoundProgram program) => VerifyCore(program, requireEntryPoint: false);

    internal static void VerifyReferences(BoundProgram program)
    {
        ArgumentNullException.ThrowIfNull(program);
        VerifyCore(program, requireEntryPoint: true, allowExternal: true);
    }

    internal static void VerifyLibraryReferences(BoundProgram program)
    {
        ArgumentNullException.ThrowIfNull(program);
        VerifyCore(program, requireEntryPoint: false, allowExternal: true);
    }

    private static void VerifyCore(BoundProgram program, bool requireEntryPoint, bool allowExternal = false,
        bool allowGenericProofDependencies = false)
    {
        ArgumentNullException.ThrowIfNull(program);
        if (program.Functions.IsDefaultOrEmpty || (requireEntryPoint
            ? program.EntryPoint < 0 || program.EntryPoint >= program.Functions.Length : program.EntryPoint != -1))
        {
            throw Invalid();
        }
        if (program.RuntimePolicy is not (BoundRuntimePolicy.FailFast or BoundRuntimePolicy.CheckedExceptions))
        {
            throw Invalid();
        }
        if (program.Functions.Length > MaximumFunctions)
        {
            throw Limit();
        }
        foreach (var function in program.Functions)
        {
            if (function is null || function.Variables.IsDefault)
            {
                throw Invalid();
            }
            if (function.Variables.Length > MaximumVariables)
            {
                throw Limit();
            }
            if (function.Variables.Any(static variable => variable is null))
            {
                throw Invalid();
            }
        }
        if (program.Classes.IsDefault || program.Classes.Length > MaximumVariables)
        {
            throw Invalid();
        }
        var usesManagedNodes = !program.Classes.IsEmpty || program.Functions.Any(static function =>
            function.Intrinsic >= BoundIntrinsic.NodeTryCreate
            || ManagedClassTypes.IsManaged(function.ReturnType)
            || function.Variables.Any(static variable => ManagedClassTypes.IsManaged(variable.Type)));
        // Isolated definition-site proofs may use private managed declarations, but executable
        // and persisted programs must still enforce the public external-call boundary.
        if (usesManagedNodes && !allowGenericProofDependencies && program.Functions.Any(function =>
            function.IsExternal && !ManagedExportShape.Supported(function, program.Structures)))
        {
            throw InvalidEffect(default, "Ownership");
        }
        if (program.Structures.IsDefault || program.Structures.Length > MaximumVariables)
        {
            throw Invalid();
        }
        if (program.Buffers.IsDefault || program.Buffers.Length > MaximumVariables)
        {
            throw Invalid();
        }
        var bufferNames = new HashSet<string>(StringComparer.Ordinal);
        long bufferWords = 0;
        foreach (var buffer in program.Buffers)
        {
            if (buffer is null || string.IsNullOrWhiteSpace(buffer.Name) || !bufferNames.Add(buffer.Name)
                || buffer.ElementType is not (PrimitiveType.Int32 or PrimitiveType.UInt32 or PrimitiveType.Char16 or PrimitiveType.Bool)
                || buffer.Values.IsDefault)
            {
                throw Invalid();
            }
            bufferWords += buffer.Values.Length;
            if (bufferWords > 1_048_576)
            {
                throw Limit();
            }
            foreach (var value in buffer.Values)
            {
                if ((buffer.ElementType == PrimitiveType.Bool && value is not (0 or 1))
                    || (buffer.ElementType == PrimitiveType.Char16 && value is < 0 or > ushort.MaxValue))
                {
                    throw Invalid();
                }
            }
        }
        var structNames = new HashSet<string>(StringComparer.Ordinal);
        var layoutStates = new byte[program.Structures.Length];
        var alignments = new int[program.Structures.Length];
        var layoutHeights = new int[program.Structures.Length];
        for (var index = 0; index < program.Structures.Length; index++)
        {
            VerifyLayout(index, 0);
        }
        var classNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var @class in program.Classes)
        {
            if (@class is null || string.IsNullOrWhiteSpace(@class.Name)
                || @class.Name is NativeManagedNodeIntrinsics.TypeName or NativeManagedNodeIntrinsics.HeapName
                || !classNames.Add(@class.Name) || structNames.Contains(@class.Name)
                || @class.Fields.IsDefaultOrEmpty || @class.Fields.Length > 16
                || @class.Size is < 8 or > ManagedClassTypes.MaximumPayload || (@class.Size & 7) != 0)
            {
                throw Invalid();
            }
            var offset = 0;
            var fieldNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (var field in @class.Fields)
            {
                if (field is null || !ValidType(field.Type, allowVoid: false, program)
                    || field.Type is PrimitiveType.OwnedInt32Buffer or PrimitiveType.ReadonlyInt32BufferView
                        or PrimitiveType.MutableInt32BufferView
                    || IsUnique(program, field.Type))
                {
                    throw Invalid();
                }
                var width = ValueLayout.Size(field.Type, program.Structures);
                var alignment = width == 0 ? 1 : ValueLayout.Alignment(field.Type, program.Structures);
                offset = (offset + alignment - 1) & -alignment;
                if (field.Size != width || field.Offset != offset || (field.IsMutable && !@class.IsMutable)
                    || width is < 1 or > ValueLayout.MaximumSize
                    || offset + width > ManagedClassTypes.MaximumPayload
                    || string.IsNullOrWhiteSpace(field.Name) || !fieldNames.Add(field.Name)
                    || (field.IsPrivate && field.IsInternal))
                {
                    throw Invalid();
                }
                offset += width;
            }
            if (((offset + 7) & ~7) != @class.Size)
            {
                throw Invalid();
            }
        }

        void VerifyLayout(int index, int depth)
        {
            if (depth >= MaximumDepth || layoutStates[index] == 1)
            {
                throw Invalid();
            }
            if (layoutStates[index] == 2)
            {
                if (depth + layoutHeights[index] > MaximumDepth)
                {
                    throw Invalid();
                }
                return;
            }
            layoutStates[index] = 1;
            var structure = program.Structures[index];
            if (structure is null || string.IsNullOrWhiteSpace(structure.Name)
                || structure.Name is NativeBufferIntrinsics.TypeName or NativeManagedNodeIntrinsics.TypeName
                    or NativeManagedNodeIntrinsics.HeapName || !structNames.Add(structure.Name)
                || (structure.IsPrivate && structure.IsInternal)
                || structure.Fields.IsDefaultOrEmpty || structure.Size is < 1 or > ValueLayout.MaximumSize)
            {
                throw Invalid();
            }
            var offset = 0;
            var alignment = 1;
            var height = 1;
            var fieldNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (var field in structure.Fields)
            {
                if (field is null || !ValidType(field.Type, allowVoid: false, program))
                {
                    throw Invalid();
                }
                if (ManagedClassTypes.IsManaged(field.Type))
                {
                    throw InvalidEffect(default, "Ownership");
                }
                var nested = (int)field.Type >= 100 && !ManagedClassTypes.IsClass(field.Type)
                    ? (int)field.Type - 100 : -1;
                if (nested >= program.Structures.Length)
                {
                    throw Invalid();
                }
                if (nested >= 0)
                {
                    VerifyLayout(nested, depth + 1);
                    if (program.Structures[nested].IsUnique && !structure.IsUnique)
                    {
                        throw Invalid();
                    }
                    height = Math.Max(height, layoutHeights[nested] + 1);
                }
                if (field.Type == PrimitiveType.OwnedInt32Buffer && !structure.IsUnique)
                {
                    throw Invalid();
                }
                var width = ValueLayout.Size(field.Type, program.Structures);
                var fieldAlignment = nested >= 0 ? alignments[nested] : width;
                var alignedOffset = fieldAlignment == 0 ? offset : (offset + fieldAlignment - 1) & -fieldAlignment;
                if (string.IsNullOrWhiteSpace(field.Name) || !fieldNames.Add(field.Name)
                    || (field.IsPrivate && field.IsInternal)
                    || width == 0 || field.Size != width || field.Offset != alignedOffset || alignedOffset + width > ValueLayout.MaximumSize)
                {
                    throw Invalid();
                }
                offset = alignedOffset + width;
                alignment = Math.Max(alignment, fieldAlignment);
            }
            if (((offset + alignment - 1) & -alignment) != structure.Size || structure.Fields.Length > ValueLayout.MaximumSize)
            {
                throw Invalid();
            }
            alignments[index] = alignment;
            layoutHeights[index] = height;
            layoutStates[index] = 2;
        }

        var names = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < program.Functions.Length; i++)
        {
            var function = program.Functions[i];
            if (function is null || string.IsNullOrEmpty(function.Name) || !names.Add(function.Name)
                || (function.IsPrivate && function.IsInternal)
                || (function.DoesNotReturn && function.ReturnType != PrimitiveType.Void)
                || !ValidType(function.ReturnType, allowVoid: true, program) || function.Variables.IsDefault
                || function.ParameterCount < 0 || function.ParameterCount > function.Variables.Length
                || function.Body is null)
            {
                throw Invalid();
            }
            if (function.Variables.Length > MaximumVariables || function.ParameterCount > MaximumArguments)
            {
                throw Limit();
            }
            if (function.Intrinsic is BoundIntrinsic.None or BoundIntrinsic.ClassTryCreate)
            {
                for (var variableIndex = 0; variableIndex < function.Variables.Length; variableIndex++)
                {
                    var variable = function.Variables[variableIndex];
                    if (variable is null || string.IsNullOrEmpty(variable.Name)
                        || (variable.Type is PrimitiveType.ReadonlyInt32BufferView or PrimitiveType.MutableInt32BufferView
                            ? variableIndex < function.ParameterCount + (function.IsConstructor ? 1 : 0)
                                || variable.IsMutable != (variable.Type == PrimitiveType.MutableInt32BufferView)
                                || variable.IsOut || function.IsExternal
                            : !ValidType(variable.Type, allowVoid: false, program)))
                    {
                        throw Invalid();
                    }
                    if ((variable.IsInit && (!variable.IsOut
                        || (!function.IsPrivate && !function.IsInternal)))
                        || (variable.IsOut && (variableIndex >= function.ParameterCount + (function.IsConstructor ? 1 : 0)
                            || function.ReturnType != PrimitiveType.Bool)))
                    {
                        throw Invalid();
                    }
                }
            }
            if (function.Intrinsic == BoundIntrinsic.ClassTryCreate)
            {
                VerifyClassFactory(program, function, i);
                continue;
            }
            if (function.Intrinsic != BoundIntrinsic.None)
            {
                if (function.PropertyName is not null
                    || (!NativeBufferIntrinsics.Matches(function) && !NativeManagedNodeIntrinsics.Matches(function)))
                {
                    throw Invalid();
                }
                continue;
            }
            if (function.Name.StartsWith(NativeBufferIntrinsics.TypeName + ".", StringComparison.Ordinal)
                || function.Name.StartsWith(NativeManagedNodeIntrinsics.TypeName + ".", StringComparison.Ordinal)
                || function.Name.StartsWith(NativeManagedNodeIntrinsics.HeapName + ".", StringComparison.Ordinal))
            {
                throw Invalid();
            }
            if (function.IsExternal && (!allowExternal
                || function.Body.Statements.IsDefault || !function.Body.Statements.IsEmpty
                || function.Variables.Length != function.ParameterCount + (function.IsConstructor ? 1 : 0)
                || (!allowGenericProofDependencies && (function.IsPrivate || function.IsInternal
                    || ManagedClassTypes.IsManaged(function.ReturnType)
                    || function.Variables.Any(static variable => ManagedClassTypes.IsManaged(variable.Type))))))
            {
                throw Invalid();
            }
            if (function.DeclaringStruct is string declaringStruct)
            {
                var declaringType = -1;
                for (var type = 0; type < program.Structures.Length; type++)
                {
                    if (program.Structures[type].Name == declaringStruct)
                    {
                        declaringType = type;
                        break;
                    }
                }
                for (var type = 0; type < program.Classes.Length; type++)
                {
                    if (program.Classes[type].Name == declaringStruct)
                    {
                        declaringType = ManagedClassTypes.First + type;
                        break;
                    }
                }
                if (declaringType < 0 || !function.Name.StartsWith(declaringStruct + ".", StringComparison.Ordinal)
                    || (function.IsConstructor && function.IsStatic)
                    || (!function.IsStatic && (function.Variables.IsEmpty
                        || function.Variables[0].Name != "this"
                        || function.Variables[0].IsOut
                        || ManagedClassTypes.Readonly(function.Variables[0].Type)
                            != (PrimitiveType)(declaringType >= ManagedClassTypes.First
                                ? declaringType : 100 + declaringType)
                        || (declaringType >= ManagedClassTypes.First
                            && ManagedClassTypes.IsWritable(function.Variables[0].Type)
                            && !program.Classes[declaringType - ManagedClassTypes.First].IsMutable)
                        || (declaringType >= ManagedClassTypes.First && !function.IsClassConstructor
                            && function.Variables[0].IsMutable)
                        || (!function.IsConstructor && function.ParameterCount < 1))))
                {
                    throw Invalid();
                }
                if (!function.IsStatic && !function.IsConstructor
                    && declaringType < ManagedClassTypes.First && program.Structures[declaringType].IsUnique
                    && function.Variables[0].IsOut)
                {
                    throw Invalid();
                }
            }
            else if (function.IsConstructor || function.IsPrivate)
            {
                throw Invalid();
            }
            if (function.PropertyName is string propertyName
                && (string.IsNullOrEmpty(propertyName) || function.DeclaringStruct is not string owner
                    || (function.Name != owner + ".#get:" + propertyName
                        && function.Name != owner + ".$producer.#get:" + propertyName)
                    || function.IsStatic || function.IsConstructor || function.IsClassConstructor
                    || function.IsCleanup || function.DoesNotReturn || function.ParameterCount != 1
                    || function.Variables[0].IsMutable || IsUnique(program, function.Variables[0].Type)
                    || !IsClassArgumentType(function.ReturnType, program.Structures)
                    || program.Structures.Any(structure => structure.Name == owner
                        && structure.Fields.Any(field => field.Name == propertyName))
                    || program.Classes.Any(@class => @class.Name == owner
                        && @class.Fields.Any(field => field.Name == propertyName))))
            {
                throw Invalid();
            }
            if (function.IsConstructor && (function.DeclaringStruct is null
                || function.ReturnType != function.Variables[0].Type
                || (int)function.ReturnType < 100
                || function.ParameterCount >= function.Variables.Length))
            {
                throw Invalid();
            }
            if (function.IsClassConstructor && (function.IsConstructor || function.IsStatic
                || function.IsExternal || function.ReturnType != PrimitiveType.Void
                || function.DeclaringStruct is null || function.Variables.IsEmpty
                || !ManagedClassTypes.IsClass(function.Variables[0].Type)
                || function.Name != function.DeclaringStruct + ".#ctor"
                || function.ParameterCount < 1 || function.ParameterCount > MaximumArguments
                || function.Variables.Skip(1).Take(function.ParameterCount - 1)
                    .Any(variable => variable.IsOut || !IsClassArgumentType(variable.Type, program.Structures))))
            {
                throw Invalid();
            }
            if (!function.IsClassConstructor && function.ClassConstructor >= 0)
            {
                throw Invalid();
            }
            if (function.IsCleanup && (function.DeclaringStruct is null || function.IsConstructor
                || function.IsStatic || function.IsPrivate || function.IsInternal || !function.IsNoThrow
                || function.DoesNotReturn || function.ReturnType != PrimitiveType.Void
                || function.ParameterCount != 1 || function.Variables[0].IsMutable))
            {
                throw Invalid();
            }
            if (function.IsCleanup && (function.Variables.IsEmpty
                || !IsUnique(program, function.Variables[0].Type)
                || program.Structures[(int)function.Variables[0].Type - 100].CleanupMethod != function.Name))
            {
                throw Invalid();
            }
            for (var variableIndex = function.IsConstructor ? 1 : 0;
                variableIndex < function.ParameterCount + (function.IsConstructor ? 1 : 0); variableIndex++)
            {
                if (IsUnique(program, function.Variables[variableIndex].Type)
                    && variableIndex == 0 && function.DeclaringStruct is not null && !function.IsStatic)
                {
                    continue;
                }
                if (IsUnique(program, function.Variables[variableIndex].Type)
                    && function.IsCleanup)
                {
                    throw Invalid();
                }
            }
        }
        for (var classIndex = 0; classIndex < program.Classes.Length; classIndex++)
        {
            var @class = program.Classes[classIndex];
            var classType = (PrimitiveType)(ManagedClassTypes.First + classIndex);
            if (!allowGenericProofDependencies
                && (program.Functions.Count(function => function.IsClassConstructor
                    && function.DeclaringStruct == @class.Name
                    && function.Variables[0].Type == (@class.IsMutable
                        ? ManagedClassTypes.Writable(classType) : classType)) != 1
                    || program.Functions.Count(function => function.Intrinsic == BoundIntrinsic.ClassTryCreate
                        && function.DeclaringStruct == @class.Name) != 1))
            {
                throw Invalid();
            }
        }
        foreach (var structure in program.Structures)
        {
            if (structure.CleanupMethod is not string cleanup)
            {
                continue;
            }
            var matches = 0;
            foreach (var function in program.Functions)
            {
                if (function.Name == cleanup && function.IsCleanup && function.DeclaringStruct == structure.Name)
                {
                    matches++;
                }
            }
            if (!structure.IsUnique || matches != 1)
            {
                throw Invalid();
            }
        }

        if (requireEntryPoint)
        {
            var entry = program.Functions[program.EntryPoint];
            if ((entry.Name != "Main" && !entry.Name.EndsWith(".Main", StringComparison.Ordinal)) || entry.ParameterCount != 0
                || entry.IsExternal || entry.DeclaringStruct is not null || entry.ReturnType is not (PrimitiveType.Void or PrimitiveType.Int32))
            {
                throw Invalid();
            }
        }

        var remaining = MaximumAnalysisVisits;
        for (var functionIndex = 0; functionIndex < program.Functions.Length; functionIndex++)
        {
            var function = program.Functions[functionIndex];
            if (function.IsExternal)
            {
                continue;
            }
            if (function.Intrinsic != BoundIntrinsic.None)
            {
                continue;
            }
            var initialized = new bool[function.Variables.Length];
            if (function.IsConstructor)
            {
                Array.Fill(initialized, true, 1, function.ParameterCount);
            }
            else
            {
                Array.Fill(initialized, true, 0, function.ParameterCount);
            }
            if (function.IsClassConstructor)
            {
                initialized[0] = false;
            }
            for (var i = 0; i < function.ParameterCount; i++)
            {
                initialized[i + (function.IsConstructor ? 1 : 0)] &= !function.Variables[i + (function.IsConstructor ? 1 : 0)].IsOut;
            }
            var fieldMasks = new uint[function.Variables.Length];
            for (var i = 0; i < initialized.Length; i++)
            {
                if (initialized[i] && HasFields(program, function.Variables[i].Type))
                {
                    fieldMasks[i] = FullMask(program, function.Variables[i].Type);
                }
            }
            var declarations = new HashSet<int>();
            try
            {
                VerifyOwnerScopes(function.Body, function, program);
                VerifyViewScopes(function.Body, function);
                VerifyManagedRoots(function);
                var returns = Statement(function.Body, initialized, fieldMasks, declarations, function, program, ref remaining, 0);
                if (function.DoesNotReturn && !returns)
                {
                    throw InvalidEffect(function.Body.Span, "DoesNotReturn");
                }
                if (function.ReturnType != PrimitiveType.Void && !returns)
                {
                    throw InvalidEffect(function.Body.Span, "NormalReturn");
                }
                for (var i = function.ParameterCount + (function.IsConstructor ? 1 : 0); i < function.Variables.Length; i++)
                {
                    if (!declarations.Contains(i))
                    {
                        throw Invalid();
                    }
                }
            }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
            {
                exception.Data[FunctionIndexKey] = functionIndex;
                throw;
            }
        }
    }

    private static void VerifyManagedRoots(BoundFunction function)
    {
        var retired = new HashSet<int>();
        var active = new HashSet<int>();
        var remaining = MaximumAnalysisVisits;
        for (var slot = 0; slot < function.ParameterCount + (function.IsConstructor ? 1 : 0); slot++)
        {
            if (ManagedClassTypes.IsManaged(function.Variables[slot].Type))
            {
                _ = active.Add(slot);
            }
        }

        void Declare(int slot, HashSet<int> scope, TextSpan span)
        {
            if (slot < function.ParameterCount + (function.IsConstructor ? 1 : 0)
                || slot >= function.Variables.Length || slot < 0
                || !ManagedClassTypes.IsManaged(function.Variables[slot].Type)
                || !scope.Add(slot) || !active.Add(slot))
            {
                throw InvalidEffect(span, "Ownership");
            }
        }

        void Expression(BoundExpression expression, HashSet<int> scope, int depth)
        {
            Count(ref remaining, depth);
            if (expression is null)
            {
                throw Invalid();
            }
            switch (expression)
            {
                case BoundRead read when ManagedClassTypes.IsManaged(read.Type):
                    if (!active.Contains(read.Variable))
                    {
                        throw InvalidEffect(read.Span, "Ownership");
                    }
                    break;
                case BoundOutArgument output when ManagedClassTypes.IsManaged(output.Type):
                    if (output.DeclaresVariable)
                    {
                        Declare(output.Variable, scope, output.Span);
                    }
                    else if (!active.Contains(output.Variable))
                    {
                        throw InvalidEffect(output.Span, "Ownership");
                    }
                    break;
                case BoundCall call:
                    if (call.Arguments.IsDefault)
                    {
                        throw Invalid();
                    }
                    foreach (var argument in call.Arguments)
                    {
                        Expression(argument, scope, depth + 1);
                    }
                    break;
                case BoundOptionalLift lift:
                    Expression(lift.Value, scope, depth + 1);
                    break;
                case BoundReadonlyProjection projection:
                    Expression(projection.Value, scope, depth + 1);
                    break;
                case BoundIsNull test:
                    Expression(test.Value, scope, depth + 1);
                    break;
                case BoundIsSome pattern:
                    Expression(pattern.Value, scope, depth + 1);
                    Declare(pattern.Variable, scope, pattern.Span);
                    break;
                case BoundUnary unary:
                    Expression(unary.Operand, scope, depth + 1);
                    break;
                case BoundCast cast:
                    Expression(cast.Operand, scope, depth + 1);
                    break;
                case BoundBinary binary:
                    Expression(binary.Left, scope, depth + 1);
                    Expression(binary.Right, scope, depth + 1);
                    break;
                case BoundFieldRead field:
                    Expression(field.Receiver, scope, depth + 1);
                    break;
                case BoundFieldLoan loan:
                    Expression(loan.Place, scope, depth + 1);
                    break;
                case BoundMutableFieldLoan loan:
                    Expression(loan.Place, scope, depth + 1);
                    break;
                case BoundBufferRead buffer:
                    Expression(buffer.Index, scope, depth + 1);
                    break;
                case BoundViewRead view:
                    Expression(view.Index, scope, depth + 1);
                    break;
                case BoundConstant or BoundNull or BoundRead or BoundMove or BoundReceiverLoan
                    or BoundMutableReceiverLoan or BoundOutArgument or BoundViewLength:
                    break;
                default:
                    throw Invalid();
            }
        }

        void Statement(BoundStatement statement, HashSet<int> scope, int depth)
        {
            Count(ref remaining, depth);
            if (statement is null)
            {
                throw Invalid();
            }
            switch (statement)
            {
                case BoundBlock block:
                    if (block.RetiredManagedLocals.IsDefault || block.Statements.IsDefault)
                    {
                        throw Invalid();
                    }
                    var nested = block.IsManagedLexicalScope ? new HashSet<int>() : scope;
                    foreach (var child in block.Statements)
                    {
                        Statement(child, nested, depth + 1);
                    }
                    if (block.IsManagedLexicalScope)
                    {
                        if (nested.Count != block.RetiredManagedLocals.Length)
                        {
                            throw InvalidEffect(block.Span, "Ownership");
                        }
                        foreach (var slot in block.RetiredManagedLocals)
                        {
                            if (!nested.Remove(slot) || !active.Remove(slot) || !retired.Add(slot))
                            {
                                throw InvalidEffect(block.Span, "Ownership");
                            }
                        }
                    }
                    else if (!block.RetiredManagedLocals.IsEmpty)
                    {
                        throw InvalidEffect(block.Span, "Ownership");
                    }
                    break;
                case BoundTryFinally cleanup:
                    Statement(cleanup.Body, scope, depth + 1);
                    Statement(cleanup.Finally, scope, depth + 1);
                    break;
                case BoundViewScope view:
                    Expression(view.Start, scope, depth + 1);
                    Expression(view.Length, scope, depth + 1);
                    Statement(view.Body, scope, depth + 1);
                    break;
                case BoundTryCatch recovery:
                    Statement(recovery.Body, scope, depth + 1);
                    Statement(recovery.Catch, scope, depth + 1);
                    if (recovery.Finally is not null)
                    {
                        Statement(recovery.Finally, scope, depth + 1);
                    }
                    break;
                case BoundUnsafe unsafeBlock:
                    Statement(unsafeBlock.Body, scope, depth + 1);
                    break;
                case BoundIf conditional:
                    Expression(conditional.Condition, scope, depth + 1);
                    Statement(conditional.Then, scope, depth + 1);
                    if (conditional.Else is not null)
                    {
                        Statement(conditional.Else, scope, depth + 1);
                    }
                    break;
                case BoundWhile loop:
                    Expression(loop.Condition, scope, depth + 1);
                    Statement(loop.Body, scope, depth + 1);
                    if (loop.Iterator is not null)
                    {
                        Statement(loop.Iterator, scope, depth + 1);
                    }
                    break;
                case BoundLocal local:
                    Expression(local.Initializer, scope, depth + 1);
                    if (local.Variable < 0 || local.Variable >= function.Variables.Length)
                    {
                        throw Invalid();
                    }
                    if (ManagedClassTypes.IsManaged(function.Variables[local.Variable].Type))
                    {
                        Declare(local.Variable, scope, local.Span);
                    }
                    break;
                case BoundAssignment assignment:
                    Expression(assignment.Value, scope, depth + 1);
                    if (assignment.Variable < 0 || assignment.Variable >= function.Variables.Length)
                    {
                        throw Invalid();
                    }
                    if (ManagedClassTypes.IsManaged(function.Variables[assignment.Variable].Type)
                        && !active.Contains(assignment.Variable))
                    {
                        throw InvalidEffect(assignment.Span, "Ownership");
                    }
                    break;
                case BoundFieldAssignment field:
                    Expression(field.Value, scope, depth + 1);
                    break;
                case BoundBufferAssignment buffer:
                    Expression(buffer.Index, scope, depth + 1);
                    Expression(buffer.Value, scope, depth + 1);
                    break;
                case BoundViewSet view:
                    Expression(view.Index, scope, depth + 1);
                    Expression(view.Value, scope, depth + 1);
                    break;
                case BoundExpressionStatement effects:
                    Expression(effects.Expression, scope, depth + 1);
                    break;
                case BoundRequire requirement:
                    Expression(requirement.Condition, scope, depth + 1);
                    break;
                case BoundReturn result when result.Value is not null:
                    Expression(result.Value, scope, depth + 1);
                    break;
                case BoundDrop or BoundDropConstructedFields or BoundOwnerDeclaration or BoundReturn
                    or BoundBreak or BoundContinue:
                    break;
                default:
                    throw Invalid();
            }
        }

        Statement(function.Body, new HashSet<int>(), 0);
        for (var slot = function.ParameterCount + (function.IsConstructor ? 1 : 0);
            slot < function.Variables.Length; slot++)
        {
            if (ManagedClassTypes.IsManaged(function.Variables[slot].Type) && !retired.Contains(slot))
            {
                throw InvalidEffect(function.Variables[slot].Span, "Ownership");
            }
        }
    }

    [SuppressMessage("Style", "IDE0010:Populate switch", Justification = "Leaf nodes have no ownership scope children.")]
    private static void VerifyOwnerScopes(BoundBlock body, BoundFunction function, BoundProgram program)
    {
        var owners = new HashSet<BoundLocal>(ReferenceEqualityComparer.Instance);
        var outputOwners = new HashSet<BoundOwnerDeclaration>(ReferenceEqualityComparer.Instance);
        var allowed = new HashSet<BoundDrop>(ReferenceEqualityComparer.Instance);
        var drops = new List<BoundDrop>();
        var parameterBody = body;
        for (var slot = function.IsConstructor ? 1 : 0;
            slot < function.ParameterCount + (function.IsConstructor ? 1 : 0); slot++)
        {
            if (!IsUnique(program, function.Variables[slot].Type)
                || (slot == 0 && function.DeclaringStruct is not null && !function.IsStatic))
            {
                continue;
            }
            if (parameterBody.Statements is not [BoundTryFinally parameterRegion]
                || parameterRegion.Finally is not BoundBlock { Statements: [BoundDrop drop] }
                || drop.Variable != slot || drop.IsOutput != function.Variables[slot].IsOut
                || !allowed.Add(drop))
            {
                throw Invalid(function.Span);
            }
            parameterBody = parameterRegion.Body;
        }
        BoundDropConstructedFields? constructedFields = null;
        if (function.IsConstructor && program.Structures[(int)function.ReturnType - 100].Fields
            .Any(field => IsUnique(program, field.Type)))
        {
            if (parameterBody.Statements is not [BoundTryFinally constructorRegion]
                || constructorRegion.Body is null || constructorRegion.Body.Statements.IsDefault
                || constructorRegion.Finally is not BoundBlock { Statements: [BoundDropConstructedFields partial] }
                || partial.Variable != 0 || partial.AcquisitionOrder.IsDefault
                || !partial.AcquisitionOrder.SequenceEqual(ConstructorAcquisitions(constructorRegion.Body, function, program)))
            {
                throw Invalid(function.Span);
            }
            constructedFields = partial;
        }
        var pending = new Stack<(BoundStatement Statement, int Depth)>();
        pending.Push((body, 0));
        var visits = MaximumAnalysisVisits;
        while (pending.TryPop(out var item))
        {
            Count(ref visits, item.Depth);
            switch (item.Statement)
            {
                case BoundBlock block:
                    if (block.Statements.IsDefault)
                    {
                        throw Invalid();
                    }
                    for (var index = 0; index < block.Statements.Length; index++)
                    {
                        var child = block.Statements[index];
                        if (child is BoundLocal local && local.Variable >= 0
                            && local.Variable < function.Variables.Length
                            && IsUnique(program, function.Variables[local.Variable].Type))
                        {
                            if (index + 1 >= block.Statements.Length
                                || block.Statements[index + 1] is not BoundTryFinally region
                                || (index + 2 != block.Statements.Length
                                    && !(index + 3 == block.Statements.Length
                                        && block.Statements[^1] is BoundReturn terminal
                                        && ((function.ReturnType == PrimitiveType.Void && terminal.Value is null)
                                            || (function.IsConstructor && terminal.Value is BoundRead { Variable: 0 }))))
                                || region.Finally is not BoundBlock finallyBlock
                                || finallyBlock.Statements is not [BoundDrop drop]
                                || drop.Variable != local.Variable || drop.IsOutput || !allowed.Add(drop)
                                || !owners.Add(local))
                            {
                                throw Invalid(local.Span);
                            }
                        }
                        if (child is BoundOwnerDeclaration output
                            && output.Variable >= 0 && output.Variable < function.Variables.Length
                            && IsUnique(program, function.Variables[output.Variable].Type))
                        {
                            if (index + 1 >= block.Statements.Length
                                || block.Statements[index + 1] is not BoundTryFinally outputRegion
                                || (index + 2 != block.Statements.Length
                                    && !(index + 3 == block.Statements.Length
                                        && block.Statements[^1] is BoundReturn terminal
                                        && ((function.ReturnType == PrimitiveType.Void && terminal.Value is null)
                                            || (function.IsConstructor && terminal.Value is BoundRead { Variable: 0 }))))
                                || outputRegion.Finally.Statements is not [BoundDrop outputDrop]
                                || outputDrop.Variable != output.Variable || outputDrop.IsOutput
                                || !allowed.Add(outputDrop) || !outputOwners.Add(output))
                            {
                                throw Invalid(output.Span);
                            }
                        }
                        pending.Push((child, item.Depth + 1));
                    }
                    break;
                case BoundLocal local:
                    if (local.Initializer is null
                        || (IsUnique(program, local.Initializer.Type) && !owners.Remove(local)))
                    {
                        throw Invalid(local.Span);
                    }
                    break;
                case BoundOwnerDeclaration output:
                    if (!outputOwners.Remove(output))
                    {
                        throw Invalid(output.Span);
                    }
                    break;
                case BoundDrop drop:
                    drops.Add(drop);
                    break;
                case BoundDropConstructedFields partial:
                    if (!ReferenceEquals(partial, constructedFields))
                    {
                        throw Invalid(partial.Span);
                    }
                    constructedFields = null;
                    break;
                case BoundTryFinally region:
                    pending.Push((region.Body, item.Depth + 1));
                    pending.Push((region.Finally, item.Depth + 1));
                    break;
                case BoundViewScope view:
                    pending.Push((view.Body, item.Depth + 1));
                    break;
                case BoundTryCatch recovery:
                    pending.Push((recovery.Body, item.Depth + 1));
                    pending.Push((recovery.Catch, item.Depth + 1));
                    if (recovery.Finally is not null)
                    {
                        pending.Push((recovery.Finally, item.Depth + 1));
                    }
                    break;
                case BoundUnsafe unsafeBlock:
                    pending.Push((unsafeBlock.Body, item.Depth + 1));
                    break;
                case BoundIf conditional:
                    pending.Push((conditional.Then, item.Depth + 1));
                    if (conditional.Else is not null)
                    {
                        pending.Push((conditional.Else, item.Depth + 1));
                    }
                    break;
                case BoundWhile loop:
                    pending.Push((loop.Body, item.Depth + 1));
                    if (loop.Iterator is not null)
                    {
                        pending.Push((loop.Iterator, item.Depth + 1));
                    }
                    break;
            }
        }
        foreach (var drop in drops)
        {
            if (!allowed.Remove(drop))
            {
                throw Invalid(drop.Span);
            }
        }
        if (allowed.Count != 0 || owners.Count != 0 || outputOwners.Count != 0
            || constructedFields is not null)
        {
            throw Invalid();
        }
    }

    private static void VerifyViewScopes(BoundBlock body, BoundFunction function)
    {
        var roots = new int[function.Variables.Length];
        var exclusive = new bool[roots.Length];
        var active = new bool[roots.Length];
        var declared = new HashSet<int>();
        var remaining = MaximumAnalysisVisits;

        void Expression(BoundExpression expression, int depth, int acquiringRoot = -1)
        {
            Count(ref remaining, depth);
            if (expression is null)
            {
                throw Invalid();
            }
            switch (expression)
            {
                case BoundMove move:
                    if (move.Variable < 0 || move.Variable >= roots.Length
                        || roots[move.Variable] != 0 || exclusive[move.Variable]
                        || move.Type is PrimitiveType.ReadonlyInt32BufferView or PrimitiveType.MutableInt32BufferView)
                    {
                        throw InvalidEffect(move.Span, "Ownership");
                    }
                    break;
                case BoundOutArgument output:
                    if (output.Variable < 0 || output.Variable >= roots.Length
                        || roots[output.Variable] != 0 || exclusive[output.Variable]
                        || output.Type is PrimitiveType.ReadonlyInt32BufferView or PrimitiveType.MutableInt32BufferView)
                    {
                        throw InvalidEffect(output.Span, "Ownership");
                    }
                    break;
                case BoundMutableReceiverLoan loan:
                    if (loan.Variable < 0 || loan.Variable >= roots.Length
                        || roots[loan.Variable] != 0 || exclusive[loan.Variable])
                    {
                        throw InvalidEffect(loan.Span, "Ownership");
                    }
                    break;
                case BoundMutableFieldLoan loan:
                    var mutableRoot = LoanRoot(loan, ref remaining, depth + 1);
                    if (mutableRoot < 0 || mutableRoot >= roots.Length
                        || roots[mutableRoot] != 0 || exclusive[mutableRoot])
                    {
                        throw InvalidEffect(loan.Span, "Ownership");
                    }
                    break;
                case BoundReceiverLoan loan:
                    if (loan.Variable < 0 || loan.Variable >= roots.Length
                        || (exclusive[loan.Variable] && loan.Variable != acquiringRoot))
                    {
                        throw InvalidEffect(loan.Span, "Ownership");
                    }
                    break;
                case BoundFieldLoan loan:
                    var loanRoot = LoanRoot(loan, ref remaining, depth + 1);
                    if (loanRoot < 0 || loanRoot >= roots.Length
                        || (exclusive[loanRoot] && loanRoot != acquiringRoot))
                    {
                        throw InvalidEffect(loan.Span, "Ownership");
                    }
                    break;
                case BoundViewLength length:
                    if (length.Variable < 0 || length.Variable >= active.Length || !active[length.Variable])
                    {
                        throw InvalidEffect(length.Span, "Ownership");
                    }
                    break;
                case BoundViewRead viewRead:
                    if (viewRead.Variable < 0 || viewRead.Variable >= active.Length || !active[viewRead.Variable])
                    {
                        throw InvalidEffect(viewRead.Span, "Ownership");
                    }
                    Expression(viewRead.Index, depth + 1, acquiringRoot);
                    break;
                case BoundRead localRead when localRead.Type is PrimitiveType.ReadonlyInt32BufferView
                    or PrimitiveType.MutableInt32BufferView:
                    throw InvalidEffect(localRead.Span, "Ownership");
                case BoundRead localRead when localRead.Variable < 0 || localRead.Variable >= roots.Length
                    || (exclusive[localRead.Variable] && localRead.Variable != acquiringRoot):
                    throw InvalidEffect(localRead.Span, "Ownership");
                case BoundCall call:
                    if (call.Arguments.IsDefault)
                    {
                        throw Invalid();
                    }
                    foreach (var argument in call.Arguments)
                    {
                        Expression(argument, depth + 1, acquiringRoot);
                    }
                    break;
                case BoundUnary unary:
                    Expression(unary.Operand, depth + 1, acquiringRoot);
                    break;
                case BoundCast cast:
                    Expression(cast.Operand, depth + 1, acquiringRoot);
                    break;
                case BoundOptionalLift lift:
                    Expression(lift.Value, depth + 1, acquiringRoot);
                    break;
                case BoundReadonlyProjection projection:
                    Expression(projection.Value, depth + 1, acquiringRoot);
                    break;
                case BoundIsNull test:
                    Expression(test.Value, depth + 1, acquiringRoot);
                    break;
                case BoundIsSome pattern:
                    Expression(pattern.Value, depth + 1, acquiringRoot);
                    break;
                case BoundBinary binary:
                    Expression(binary.Left, depth + 1, acquiringRoot);
                    Expression(binary.Right, depth + 1, acquiringRoot);
                    break;
                case BoundFieldRead field:
                    Expression(field.Receiver, depth + 1, acquiringRoot);
                    break;
                case BoundBufferRead buffer:
                    Expression(buffer.Index, depth + 1, acquiringRoot);
                    break;
                case BoundConstant or BoundNull or BoundRead:
                    break;
                default:
                    throw Invalid();
            }
        }

        void Statement(BoundStatement statement, int depth)
        {
            Count(ref remaining, depth);
            if (statement is null)
            {
                throw Invalid();
            }
            switch (statement)
            {
                case BoundBlock block:
                    if (block.Statements.IsDefault)
                    {
                        throw Invalid();
                    }
                    foreach (var child in block.Statements)
                    {
                        Statement(child, depth + 1);
                    }
                    break;
                case BoundViewScope view:
                    if (view.Variable < function.ParameterCount + (function.IsConstructor ? 1 : 0)
                        || view.Variable >= roots.Length || view.Variable < 0
                        || function.Variables[view.Variable].Type != (view.IsMutable
                            ? PrimitiveType.MutableInt32BufferView : PrimitiveType.ReadonlyInt32BufferView)
                        || function.Variables[view.Variable].IsMutable != view.IsMutable
                        || !declared.Add(view.Variable) || active[view.Variable]
                        || view.Body is null || view.Owner is null)
                    {
                        throw InvalidEffect(view.Span, "Ownership");
                    }
                    var root = ViewRoot(view.Owner, ref remaining, depth + 1);
                    if (root < 0 || root >= roots.Length)
                    {
                        throw InvalidEffect(view.Owner.Span, "Ownership");
                    }
                    if (exclusive[root] || (view.IsMutable && roots[root] != 0))
                    {
                        throw InvalidEffect(view.Span, "Ownership");
                    }
                    if (view.IsMutable)
                    {
                        exclusive[root] = true;
                    }
                    else
                    {
                        roots[root]++;
                    }
                    Expression(view.Start, depth + 1, view.IsMutable ? root : -1);
                    Expression(view.Length, depth + 1, view.IsMutable ? root : -1);
                    active[view.Variable] = true;
                    Statement(view.Body, depth + 1);
                    active[view.Variable] = false;
                    if (view.IsMutable)
                    {
                        exclusive[root] = false;
                    }
                    else
                    {
                        roots[root]--;
                    }
                    break;
                case BoundLocal local:
                    if (local.Variable < 0 || local.Variable >= roots.Length
                        || function.Variables[local.Variable].Type is PrimitiveType.ReadonlyInt32BufferView
                            or PrimitiveType.MutableInt32BufferView)
                    {
                        throw InvalidEffect(local.Span, "Ownership");
                    }
                    Expression(local.Initializer, depth + 1);
                    break;
                case BoundAssignment assignment:
                    if (assignment.Variable < 0 || assignment.Variable >= roots.Length
                        || roots[assignment.Variable] != 0 || exclusive[assignment.Variable]
                        || function.Variables[assignment.Variable].Type is PrimitiveType.ReadonlyInt32BufferView
                            or PrimitiveType.MutableInt32BufferView)
                    {
                        throw InvalidEffect(assignment.Span, "Ownership");
                    }
                    Expression(assignment.Value, depth + 1);
                    break;
                case BoundFieldAssignment field:
                    if (field.Variable < 0 || field.Variable >= roots.Length
                        || roots[field.Variable] != 0 || exclusive[field.Variable])
                    {
                        throw InvalidEffect(field.Span, "Ownership");
                    }
                    Expression(field.Value, depth + 1);
                    break;
                case BoundDrop drop:
                    if (drop.Variable < 0 || drop.Variable >= roots.Length
                        || roots[drop.Variable] != 0 || exclusive[drop.Variable])
                    {
                        throw InvalidEffect(drop.Span, "Ownership");
                    }
                    break;
                case BoundBufferAssignment write:
                    Expression(write.Index, depth + 1);
                    Expression(write.Value, depth + 1);
                    break;
                case BoundViewSet viewWrite:
                    if (viewWrite.Variable < 0 || viewWrite.Variable >= active.Length || !active[viewWrite.Variable]
                        || function.Variables[viewWrite.Variable].Type != PrimitiveType.MutableInt32BufferView)
                    {
                        throw InvalidEffect(viewWrite.Span, "Ownership");
                    }
                    Expression(viewWrite.Index, depth + 1);
                    Expression(viewWrite.Value, depth + 1);
                    break;
                case BoundExpressionStatement effects:
                    Expression(effects.Expression, depth + 1);
                    break;
                case BoundIf conditional:
                    Expression(conditional.Condition, depth + 1);
                    Statement(conditional.Then, depth + 1);
                    if (conditional.Else is not null)
                    {
                        Statement(conditional.Else, depth + 1);
                    }
                    break;
                case BoundWhile loop:
                    Expression(loop.Condition, depth + 1);
                    Statement(loop.Body, depth + 1);
                    if (loop.Iterator is not null)
                    {
                        Statement(loop.Iterator, depth + 1);
                    }
                    break;
                case BoundTryFinally cleanup:
                    Statement(cleanup.Body, depth + 1);
                    Statement(cleanup.Finally, depth + 1);
                    break;
                case BoundTryCatch recovery:
                    Statement(recovery.Body, depth + 1);
                    Statement(recovery.Catch, depth + 1);
                    if (recovery.Finally is not null)
                    {
                        Statement(recovery.Finally, depth + 1);
                    }
                    break;
                case BoundUnsafe unsafeBlock:
                    Statement(unsafeBlock.Body, depth + 1);
                    break;
                case BoundRequire requirement:
                    Expression(requirement.Condition, depth + 1);
                    break;
                case BoundReturn result:
                    if (result.Value is not null)
                    {
                        Expression(result.Value, depth + 1);
                    }
                    break;
                case BoundOwnerDeclaration or BoundDropConstructedFields or BoundBreak or BoundContinue:
                    break;
                default:
                    throw Invalid();
            }
        }

        Statement(body, 0);
        for (var slot = function.ParameterCount + (function.IsConstructor ? 1 : 0); slot < roots.Length; slot++)
        {
            if (!declared.Contains(slot) && function.Variables[slot].Type is
                PrimitiveType.ReadonlyInt32BufferView or PrimitiveType.MutableInt32BufferView)
            {
                throw Invalid();
            }
        }
    }

    private static int ViewRoot(BoundExpression owner, ref int remaining, int depth)
    {
        Count(ref remaining, depth);
        if (owner.Type != PrimitiveType.OwnedInt32Buffer)
        {
            throw InvalidEffect(owner.Span, "Ownership");
        }
        if (owner is BoundReceiverLoan or BoundMutableReceiverLoan)
        {
            return owner is BoundReceiverLoan named ? named.Variable : ((BoundMutableReceiverLoan)owner).Variable;
        }
        return LoanRoot(owner, ref remaining, depth + 1);
    }

    private static int LoanRoot(BoundExpression owner, ref int remaining, int depth)
    {
        if (owner is not (BoundFieldLoan or BoundMutableFieldLoan))
        {
            throw InvalidEffect(owner.Span, "Ownership");
        }
        BoundExpression place = owner is BoundFieldLoan field ? field.Place : ((BoundMutableFieldLoan)owner).Place;
        while (place is BoundFieldRead nested)
        {
            Count(ref remaining, depth++);
            place = nested.Receiver;
        }
        return place is BoundRead root ? root.Variable : throw InvalidEffect(owner.Span, "Ownership");
    }

    [SuppressMessage("Style", "IDE0010:Populate switch", Justification = "Only field acquisitions and nested control regions contribute to the order.")]
    private static ImmutableArray<int> ConstructorAcquisitions(BoundBlock body, BoundFunction function, BoundProgram program)
    {
        var order = ImmutableArray.CreateBuilder<int>();
        var pending = new Stack<(BoundStatement Statement, bool Sequential, int Depth)>();
        pending.Push((body, true, 0));
        var remaining = MaximumAnalysisVisits;
        var fields = program.Structures[(int)function.ReturnType - 100].Fields;
        while (pending.TryPop(out var item))
        {
            Count(ref remaining, item.Depth);
            if (item.Statement is null)
            {
                throw Invalid();
            }
            switch (item.Statement)
            {
                case BoundBlock block:
                    if (block.Statements.IsDefault)
                    {
                        throw Invalid();
                    }
                    for (var i = block.Statements.Length - 1; i >= 0; i--)
                    {
                        pending.Push((block.Statements[i], item.Sequential, item.Depth + 1));
                    }
                    break;
                case BoundTryFinally region:
                    pending.Push((region.Finally, false, item.Depth + 1));
                    pending.Push((region.Body, item.Sequential, item.Depth + 1));
                    break;
                case BoundViewScope view:
                    pending.Push((view.Body, item.Sequential, item.Depth + 1));
                    break;
                case BoundFieldAssignment assignment when assignment.Variable == 0
                    && assignment.Field >= 0 && assignment.Field < fields.Length
                    && IsUnique(program, fields[assignment.Field].Type):
                    if (!item.Sequential || !assignment.NestedFields.IsEmpty || order.Contains(assignment.Field))
                    {
                        throw InvalidEffect(assignment.Span, "Ownership");
                    }
                    order.Add(assignment.Field);
                    break;
                case BoundIf conditional:
                    if (conditional.Else is not null)
                    {
                        pending.Push((conditional.Else, false, item.Depth + 1));
                    }
                    pending.Push((conditional.Then, false, item.Depth + 1));
                    break;
                case BoundWhile loop:
                    pending.Push((loop.Body, false, item.Depth + 1));
                    if (loop.Iterator is not null)
                    {
                        pending.Push((loop.Iterator, false, item.Depth + 1));
                    }
                    break;
                case BoundTryCatch recovery:
                    if (recovery.Finally is not null)
                    {
                        pending.Push((recovery.Finally, false, item.Depth + 1));
                    }
                    pending.Push((recovery.Catch, false, item.Depth + 1));
                    pending.Push((recovery.Body, false, item.Depth + 1));
                    break;
                case BoundUnsafe unsafeBlock:
                    pending.Push((unsafeBlock.Body, item.Sequential, item.Depth + 1));
                    break;
            }
        }
        return order.ToImmutable();
    }

    internal static bool TryVerifySource(BoundProgram program, out int functionIndex, out TextSpan span, out bool exceededWorkLimit,
        out string? invalidEffect, bool allowGenericProofDependencies = false)
    {
        try
        {
            VerifyCore(program, requireEntryPoint: program.EntryPoint != -1, allowExternal: true,
                allowGenericProofDependencies: allowGenericProofDependencies);
            functionIndex = -1;
            span = default;
            exceededWorkLimit = false;
            invalidEffect = null;
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            functionIndex = exception.Data[FunctionIndexKey] is int index ? index : Math.Max(0, program.EntryPoint);
            span = exception.Data[SourceSpanKey] is TextSpan sourceSpan
                ? sourceSpan : program.Functions.IsDefaultOrEmpty ? default : program.Functions[functionIndex].Span;
            exceededWorkLimit = exception is NotSupportedException;
            invalidEffect = exception.Data[InvalidEffectKey] is string effect ? effect : null;
            return false;
        }
    }

    private static bool Statement(BoundStatement statement, bool[] initialized, uint[] fieldMasks, HashSet<int> declarations,
        BoundFunction function, BoundProgram program, ref int remaining, int depth, bool reachable = true,
        List<CleanupScope>? cleanups = null, bool inCleanup = false, bool inUnsafe = false, FailureEdges? failures = null,
        List<LoopScope>? loops = null)
    {
        Count(ref remaining, depth);
        cleanups ??= [];
        loops ??= [];
        if (!reachable)
        {
            failures = null;
        }
        switch (statement)
        {
            case BoundBlock block:
                if (block.Statements.IsDefault)
                {
                    throw Invalid();
                }
                var returns = false;
                foreach (var child in block.Statements)
                {
                    if (child is null)
                    {
                        throw Invalid();
                    }
                    var childReturns = Statement(child, initialized, fieldMasks, declarations, function, program, ref remaining, depth + 1,
                        reachable && !returns, cleanups, inCleanup, inUnsafe, failures, loops);
                    returns |= childReturns;
                }
                return returns;
            case BoundLocal local:
                if (local.Variable < function.ParameterCount + (function.IsConstructor ? 1 : 0)
                    || local.Variable >= initialized.Length
                    || !declarations.Add(local.Variable) || initialized[local.Variable])
                {
                    throw Invalid();
                }
                Expression(local.Initializer, initialized, fieldMasks, declarations, function, program, ref remaining, depth + 1, failures);
                if (local.Initializer.Type != function.Variables[local.Variable].Type)
                {
                    throw Invalid();
                }
                initialized[local.Variable] = true;
                if (HasFields(program, local.Initializer.Type))
                {
                    fieldMasks[local.Variable] = FullMask(program, local.Initializer.Type);
                }
                return false;
            case BoundOwnerDeclaration output:
                if (output.Variable < function.ParameterCount + (function.IsConstructor ? 1 : 0)
                    || output.Variable >= initialized.Length
                    || !IsUnique(program, function.Variables[output.Variable].Type)
                    || initialized[output.Variable] || declarations.Contains(output.Variable))
                {
                    throw Invalid(output.Span);
                }
                return false;
            case BoundDrop drop:
                if (!inCleanup || drop.Variable < 0 || drop.Variable >= initialized.Length
                    || (drop.Variable >= function.ParameterCount + (function.IsConstructor ? 1 : 0)
                        && !declarations.Contains(drop.Variable))
                    || !IsUnique(program, function.Variables[drop.Variable].Type)
                    || (drop.IsOutput && (!function.Variables[drop.Variable].IsOut
                        || function.ReturnType != PrimitiveType.Bool)))
                {
                    throw Invalid(drop.Span);
                }
                var method = function.Variables[drop.Variable].Type == PrimitiveType.OwnedInt32Buffer
                    ? null : program.Structures[(int)function.Variables[drop.Variable].Type - 100].CleanupMethod;
                if (method is null ? drop.CleanupFunction != -1
                    : drop.CleanupFunction < 0 || drop.CleanupFunction >= program.Functions.Length
                        || program.Functions[drop.CleanupFunction].Name != method)
                {
                    throw Invalid(drop.Span);
                }
                initialized[drop.Variable] = false;
                fieldMasks[drop.Variable] = 0;
                return false;
            case BoundDropConstructedFields partial:
                if (!inCleanup || !function.IsConstructor || partial.Variable != 0
                    || partial.AcquisitionOrder.IsDefault)
                {
                    throw Invalid(partial.Span);
                }
                return false;
            case BoundAssignment assignment:
                if (assignment.Variable < 0 || assignment.Variable >= initialized.Length
                    || (assignment.Variable < function.ParameterCount && !function.Variables[assignment.Variable].IsOut)
                    || (function.IsConstructor && assignment.Variable == 0)
                    || (!initialized[assignment.Variable] && !function.Variables[assignment.Variable].IsOut
                        && !declarations.Contains(assignment.Variable))
                    || !function.Variables[assignment.Variable].IsMutable)
                {
                    throw Invalid();
                }
                Expression(assignment.Value, initialized, fieldMasks, declarations, function, program, ref remaining, depth + 1, failures);
                if (assignment.Value.Type != function.Variables[assignment.Variable].Type)
                {
                    throw Invalid();
                }
                if (HasFields(program, assignment.Value.Type))
                {
                    fieldMasks[assignment.Variable] = FullMask(program, assignment.Value.Type);
                }
                initialized[assignment.Variable] = true;
                return false;
            case BoundFieldAssignment fieldAssignment:
                if (fieldAssignment.Variable < 0 || fieldAssignment.Variable >= initialized.Length
                    || (!function.Variables[fieldAssignment.Variable].IsMutable
                        && !ManagedClassTypes.IsClass(function.Variables[fieldAssignment.Variable].Type))
                    || fieldAssignment.NestedFields.IsDefault)
                {
                    throw Invalid();
                }
                var ownerType = function.Variables[fieldAssignment.Variable].Type;
                if (!HasFields(program, ownerType)
                    || fieldAssignment.Field < 0 || fieldAssignment.Field >= Fields(program, ownerType).Length
                    || !CanAccessField(function, program, ownerType, fieldAssignment.Field)
                    || (!initialized[fieldAssignment.Variable]
                        && (!(function.IsConstructor || function.IsClassConstructor)
                            || fieldAssignment.Variable != 0)))
                {
                    throw Invalid();
                }
                if (IsUnique(program, ownerType) && function.Variables[fieldAssignment.Variable].IsOut)
                {
                    throw InvalidEffect(fieldAssignment.Span, "Ownership");
                }
                var targetField = Fields(program, ownerType)[fieldAssignment.Field];
                if (ManagedClassTypes.IsClass(ownerType) && !ManagedClassTypes.IsWritable(ownerType)
                    && !((function.IsConstructor || function.IsClassConstructor)
                        && fieldAssignment.Variable == 0 && fieldAssignment.NestedFields.IsEmpty)
                    && (fieldAssignment.NestedFields.IsEmpty || !ManagedClassTypes.IsClass(targetField.Type)))
                {
                    throw Invalid();
                }
                if (IsUnique(program, targetField.Type)
                    && (!function.IsConstructor || fieldAssignment.Variable != 0
                        || !fieldAssignment.NestedFields.IsEmpty
                        || (fieldMasks[0] & (1u << fieldAssignment.Field)) != 0))
                {
                    throw InvalidEffect(fieldAssignment.Span, "Ownership");
                }
                if (!targetField.IsMutable
                    && (!ManagedClassTypes.IsClass(ownerType) || fieldAssignment.NestedFields.IsEmpty
                        || !ManagedClassTypes.IsClass(targetField.Type))
                    && !((function.IsConstructor || function.IsClassConstructor) && fieldAssignment.Variable == 0))
                {
                    throw Invalid();
                }
                var nestedWrite = !fieldAssignment.NestedFields.IsEmpty;
                if (nestedWrite && (fieldMasks[fieldAssignment.Variable] & (1u << fieldAssignment.Field)) == 0)
                {
                    throw Invalid();
                }
                for (var index = 0; index < fieldAssignment.NestedFields.Length; index++)
                {
                    Count(ref remaining, depth + index + 1);
                    var nestedType = targetField.Type;
                    var nestedField = fieldAssignment.NestedFields[index];
                    if (!HasFields(program, nestedType)
                        || nestedField < 0 || nestedField >= Fields(program, nestedType).Length
                        || !CanAccessField(function, program, nestedType, nestedField))
                    {
                        throw Invalid();
                    }
                    targetField = Fields(program, nestedType)[nestedField];
                    if (ManagedClassTypes.IsClass(nestedType) && !ManagedClassTypes.IsWritable(nestedType)
                        && (index == fieldAssignment.NestedFields.Length - 1
                            || !ManagedClassTypes.IsClass(targetField.Type)))
                    {
                        throw Invalid();
                    }
                    if (IsUnique(program, nestedType))
                    {
                        throw InvalidEffect(fieldAssignment.Span, "Ownership");
                    }
                    if (!targetField.IsMutable
                        && (!ManagedClassTypes.IsClass(nestedType)
                            || !ManagedClassTypes.IsClass(targetField.Type)
                            || index == fieldAssignment.NestedFields.Length - 1))
                    {
                        throw Invalid();
                    }
                }
                Expression(fieldAssignment.Value, initialized, fieldMasks, declarations, function, program, ref remaining, depth + 1, failures);
                if (IsUnique(program, targetField.Type)
                    && fieldAssignment.Value is not (BoundMove or BoundCall))
                {
                    throw InvalidEffect(fieldAssignment.Span, "Ownership");
                }
                if (fieldAssignment.Value.Type != targetField.Type
                    || (nestedWrite && (fieldMasks[fieldAssignment.Variable] & (1u << fieldAssignment.Field)) == 0))
                {
                    throw Invalid();
                }
                fieldMasks[fieldAssignment.Variable] |= 1u << fieldAssignment.Field;
                initialized[fieldAssignment.Variable] = fieldMasks[fieldAssignment.Variable]
                    == FullMask(program, ownerType);
                return false;
            case BoundBufferAssignment bufferWrite:
                if (function.IsNoThrow && !IsProvenBufferIndex(bufferWrite.Buffer, bufferWrite.Index, program))
                {
                    throw InvalidEffect(bufferWrite.Span);
                }
                if (bufferWrite.Buffer < 0 || bufferWrite.Buffer >= program.Buffers.Length
                    || !program.Buffers[bufferWrite.Buffer].IsWritable)
                {
                    throw Invalid();
                }
                Expression(bufferWrite.Index, initialized, fieldMasks, declarations, function, program, ref remaining, depth + 1, failures);
                failures?.Add(initialized, fieldMasks);
                Expression(bufferWrite.Value, initialized, fieldMasks, declarations, function, program, ref remaining, depth + 1, failures);
                if (bufferWrite.Index.Type != PrimitiveType.Int32
                    || bufferWrite.Value.Type != program.Buffers[bufferWrite.Buffer].ElementType)
                {
                    throw Invalid();
                }
                return false;
            case BoundViewSet viewSet:
                if (viewSet.Variable < 0 || viewSet.Variable >= initialized.Length
                    || !initialized[viewSet.Variable]
                    || function.Variables[viewSet.Variable].Type != PrimitiveType.MutableInt32BufferView)
                {
                    throw InvalidEffect(viewSet.Span, "Ownership");
                }
                Expression(viewSet.Index, initialized, fieldMasks, declarations, function, program,
                    ref remaining, depth + 1, failures);
                Expression(viewSet.Value, initialized, fieldMasks, declarations, function, program,
                    ref remaining, depth + 1, failures);
                if (viewSet.Index.Type != PrimitiveType.Int32 || viewSet.Value.Type != PrimitiveType.Int32)
                {
                    throw Invalid();
                }
                if (function.IsNoThrow)
                {
                    throw InvalidEffect(viewSet.Span);
                }
                failures?.Add(initialized, fieldMasks);
                return false;
            case BoundExpressionStatement expressionStatement:
                if (expressionStatement.Expression is not (BoundCall or BoundViewRead))
                {
                    throw Invalid();
                }
                if (IsUnique(program, expressionStatement.Expression.Type))
                {
                    throw InvalidEffect(expressionStatement.Span, "Ownership");
                }
                Expression(expressionStatement.Expression, initialized, fieldMasks, declarations, function, program, ref remaining, depth + 1, failures);
                return expressionStatement.Expression is BoundCall statementCall
                    && program.Functions[statementCall.Function].DoesNotReturn;
            case BoundIf conditional:
                var beforeCondition = (bool[])initialized.Clone();
                Expression(conditional.Condition, initialized, fieldMasks, declarations, function, program, ref remaining, depth + 1, failures);
                if (conditional.Condition.Type != PrimitiveType.Bool || conditional.Then is null)
                {
                    throw Invalid();
                }
                var thenReachable = CanBe(conditional.Condition, true, ref remaining);
                var elseReachable = CanBe(conditional.Condition, false, ref remaining);
                var thenState = Outcome(conditional.Condition, beforeCondition, true, ref remaining);
                var thenMasks = (uint[])fieldMasks.Clone();
                MarkOutMasks(thenState, thenMasks, function, program);
                var thenReturns = Statement(conditional.Then, thenState, thenMasks, declarations, function, program, ref remaining, depth + 1,
                    reachable && thenReachable, cleanups, inCleanup, inUnsafe, failures, loops);
                var elseState = Outcome(conditional.Condition, beforeCondition, false, ref remaining);
                var elseMasks = (uint[])fieldMasks.Clone();
                MarkOutMasks(elseState, elseMasks, function, program);
                var elseReturns = conditional.Else is not null
                    && Statement(conditional.Else, elseState, elseMasks, declarations, function, program, ref remaining, depth + 1,
                        reachable && elseReachable, cleanups, inCleanup, inUnsafe, failures, loops);
                for (var i = 0; i < initialized.Length; i++)
                {
                    initialized[i] = !thenReachable || thenReturns ? elseState[i]
                        : !elseReachable || elseReturns ? thenState[i] : thenState[i] && elseState[i];
                    fieldMasks[i] = !thenReachable || thenReturns ? elseMasks[i]
                        : !elseReachable || elseReturns ? thenMasks[i] : thenMasks[i] & elseMasks[i];
                }
                return (!thenReachable || thenReturns) && (!elseReachable || elseReturns);
            case BoundWhile loop:
                if (loop.Iterator is not (null or BoundAssignment or BoundExpressionStatement
                    or BoundBlock { Statements.IsEmpty: true, IsManagedLexicalScope: false, RetiredManagedLocals.IsEmpty: true }))
                {
                    throw Invalid(loop.Span);
                }
                var entryState = (bool[])initialized.Clone();
                var entryMasks = (uint[])fieldMasks.Clone();
                var headState = (bool[])entryState.Clone();
                var headMasks = (uint[])entryMasks.Clone();
                var priorDeclarations = new HashSet<int>(declarations);
                for (var iteration = 0; iteration <= initialized.Length * 9; iteration++)
                {
                    var conditionInput = (bool[])headState.Clone();
                    var conditionState = (bool[])headState.Clone();
                    var conditionMasks = (uint[])headMasks.Clone();
                    var iterationDeclarations = new HashSet<int>(priorDeclarations);
                    Expression(loop.Condition, conditionState, conditionMasks, iterationDeclarations, function, program, ref remaining, depth + 1, failures);
                    if (loop.Condition.Type != PrimitiveType.Bool || loop.Body is null)
                    {
                        throw Invalid();
                    }
                    var bodyState = Outcome(loop.Condition, conditionInput, true, ref remaining);
                    var bodyMasks = (uint[])conditionMasks.Clone();
                    MarkOutMasks(bodyState, bodyMasks, function, program);
                    var bodyReachable = CanBe(loop.Condition, true, ref remaining);
                    var exitReachable = CanBe(loop.Condition, false, ref remaining);
                    var iteratorDeclarations = new HashSet<int>(iterationDeclarations);
                    var loopScope = new LoopScope(cleanups.Count);
                    loops.Add(loopScope);
                    var bodyReturns = Statement(loop.Body, bodyState, bodyMasks, iterationDeclarations, function, program, ref remaining, depth + 1,
                        reachable && bodyReachable, cleanups, inCleanup, inUnsafe, failures, loops);
                    loops.RemoveAt(loops.Count - 1);
                    var exitState = Outcome(loop.Condition, conditionInput, false, ref remaining);
                    var exitMasks = (uint[])conditionMasks.Clone();
                    MarkOutMasks(exitState, exitMasks, function, program);
                    var backEdges = new FailureEdges();
                    if (bodyReachable && !bodyReturns)
                    {
                        backEdges.Add(bodyState, bodyMasks);
                    }
                    if (loopScope.Continues.HasEdges)
                    {
                        backEdges.Add(loopScope.Continues.State, loopScope.Continues.Masks);
                    }
                    if (loop.Iterator is not null)
                    {
                        var iteratorState = backEdges.HasEdges ? backEdges.State : (bool[])bodyState.Clone();
                        var iteratorMasks = backEdges.HasEdges ? backEdges.Masks : (uint[])bodyMasks.Clone();
                        for (var slot = function.ParameterCount + (function.IsConstructor ? 1 : 0);
                            slot < iteratorState.Length; slot++)
                        {
                            if (!iteratorDeclarations.Contains(slot))
                            {
                                iteratorState[slot] = false;
                                iteratorMasks[slot] = 0;
                            }
                        }
                        var iteratorReturns = Statement(loop.Iterator, iteratorState, iteratorMasks, iteratorDeclarations,
                            function, program, ref remaining, depth + 1, reachable && backEdges.HasEdges,
                            cleanups, inCleanup, inUnsafe, failures, loops);
                        iterationDeclarations.UnionWith(iteratorDeclarations);
                        backEdges = new FailureEdges();
                        if (!iteratorReturns && bodyReachable && (loopScope.Continues.HasEdges || !bodyReturns))
                        {
                            backEdges.Add(iteratorState, iteratorMasks);
                        }
                    }
                    var exits = new FailureEdges();
                    if (exitReachable)
                    {
                        exits.Add(exitState, exitMasks);
                    }
                    if (loopScope.Breaks.HasEdges)
                    {
                        exits.Add(loopScope.Breaks.State, loopScope.Breaks.Masks);
                    }
                    var backState = backEdges.HasEdges ? backEdges.State : null;
                    var backMasks = backEdges.HasEdges ? backEdges.Masks : null;
                    var changed = false;
                    for (var i = 0; i < headState.Length; i++)
                    {
                        var nextState = entryState[i] && (backState is null || backState[i]);
                        var nextMask = entryMasks[i] & (backMasks is null ? uint.MaxValue : backMasks[i]);
                        changed |= nextState != headState[i] || nextMask != headMasks[i];
                        headState[i] = nextState;
                        headMasks[i] = nextMask;
                    }
                    if (!changed)
                    {
                        if (exits.HasEdges)
                        {
                            Array.Copy(exits.State, initialized, initialized.Length);
                            Array.Copy(exits.Masks, fieldMasks, fieldMasks.Length);
                        }
                        declarations.UnionWith(iterationDeclarations);
                        return !exits.HasEdges;
                    }
                }
                throw Limit();
            case BoundBreak or BoundContinue:
                if (loops.Count == 0)
                {
                    throw InvalidEffect(statement.Span, inCleanup ? "FinallyReturn" : "LoopTransfer");
                }
                var target = loops[^1];
                var transferState = (bool[])initialized.Clone();
                var transferMasks = (uint[])fieldMasks.Clone();
                var transferDeclarations = new HashSet<int>(declarations);
                for (var index = cleanups.Count - 1; index >= target.CleanupDepth; index--)
                {
                    VerifyCleanup(cleanups[index].Body, transferState, transferMasks, transferDeclarations,
                        function, program, ref remaining, depth + 1, reachable, inUnsafe, cleanups[index].Visible);
                }
                if (reachable)
                {
                    (statement is BoundBreak ? target.Breaks : target.Continues).Add(transferState, transferMasks);
                }
                return true;
            case BoundTryFinally cleanup:
                return TryFinallyStatement(cleanup, initialized, fieldMasks, declarations, function, program,
                    ref remaining, depth, reachable, cleanups, inCleanup, inUnsafe, failures, loops);
            case BoundViewScope view:
                if (view.Variable < function.ParameterCount + (function.IsConstructor ? 1 : 0)
                    || view.Variable >= initialized.Length || view.Variable < 0
                    || function.Variables[view.Variable].Type != (view.IsMutable
                        ? PrimitiveType.MutableInt32BufferView : PrimitiveType.ReadonlyInt32BufferView)
                    || function.Variables[view.Variable].IsMutable != view.IsMutable
                    || !declarations.Add(view.Variable) || initialized[view.Variable]
                    || view.Owner is null || view.Body is null)
                {
                    throw InvalidEffect(view.Span, "Ownership");
                }
                if (view.Owner is BoundReceiverLoan named)
                {
                    if (view.IsMutable || named.Type != PrimitiveType.OwnedInt32Buffer
                        || named.Variable < 0 || named.Variable >= initialized.Length
                        || function.Variables[named.Variable].Type != named.Type
                        || !initialized[named.Variable])
                    {
                        throw InvalidEffect(named.Span, "Ownership");
                    }
                }
                else if (view.Owner is BoundMutableReceiverLoan mutable)
                {
                    if (!view.IsMutable || mutable.Type != PrimitiveType.OwnedInt32Buffer
                        || mutable.Variable < 0 || mutable.Variable >= initialized.Length
                        || function.Variables[mutable.Variable].Type != mutable.Type
                        || !function.Variables[mutable.Variable].IsMutable || !initialized[mutable.Variable])
                    {
                        throw InvalidEffect(mutable.Span, "Ownership");
                    }
                }
                else if (view.Owner is BoundFieldLoan fieldLoan)
                {
                    if (view.IsMutable || fieldLoan.Type != PrimitiveType.OwnedInt32Buffer
                        || fieldLoan.Place is null || fieldLoan.Place.Type != fieldLoan.Type)
                    {
                        throw InvalidEffect(fieldLoan.Span, "Ownership");
                    }
                    _ = VerifyUniqueFieldPlace(fieldLoan.Place, initialized, fieldMasks,
                        function, program, ref remaining, depth + 1);
                }
                else if (view.Owner is BoundMutableFieldLoan mutableField)
                {
                    if (!view.IsMutable || mutableField.Type != PrimitiveType.OwnedInt32Buffer
                        || mutableField.Place is null || mutableField.Place.Type != mutableField.Type)
                    {
                        throw InvalidEffect(mutableField.Span, "Ownership");
                    }
                    _ = VerifyMutableFieldPlace(mutableField.Place, initialized, function, program,
                        ref remaining, depth + 1, allowBufferLeaf: true);
                }
                else
                {
                    throw InvalidEffect(view.Owner.Span, "Ownership");
                }
                Expression(view.Start, initialized, fieldMasks, declarations, function, program, ref remaining, depth + 1, failures);
                Expression(view.Length, initialized, fieldMasks, declarations, function, program, ref remaining, depth + 1, failures);
                if (view.Start.Type != PrimitiveType.Int32 || view.Length.Type != PrimitiveType.Int32)
                {
                    throw Invalid();
                }
                if (function.IsNoThrow)
                {
                    throw InvalidEffect(view.Span);
                }
                failures?.Add(initialized, fieldMasks);
                initialized[view.Variable] = true;
                var viewReturns = Statement(view.Body, initialized, fieldMasks, declarations, function, program,
                    ref remaining, depth + 1, reachable, cleanups, inCleanup, inUnsafe, failures, loops);
                initialized[view.Variable] = false;
                return viewReturns;
            case BoundUnsafe unsafeBlock:
                if (unsafeBlock.Body is null)
                {
                    throw Invalid(unsafeBlock.Span);
                }
                return Statement(unsafeBlock.Body, initialized, fieldMasks, declarations, function, program,
                    ref remaining, depth + 1, reachable, cleanups, inCleanup, inUnsafe: true, failures: failures, loops: loops);
            case BoundTryCatch recovery:
                return TryCatchStatement(recovery, initialized, fieldMasks, declarations, function, program,
                    ref remaining, depth, reachable, cleanups, inCleanup, inUnsafe, failures, loops);
            case BoundRequire requirement:
                var beforeRequirement = (bool[])initialized.Clone();
                Expression(requirement.Condition, initialized, fieldMasks, declarations, function, program, ref remaining, depth + 1, failures);
                if (requirement.Condition.Type != PrimitiveType.Bool)
                {
                    throw Invalid();
                }
                if (function.IsNoThrow && CanBe(requirement.Condition, false, ref remaining))
                {
                    throw InvalidEffect(requirement.Span);
                }
                if (CanBe(requirement.Condition, false, ref remaining))
                {
                    var rejected = Outcome(requirement.Condition, beforeRequirement, false, ref remaining);
                    var rejectedMasks = (uint[])fieldMasks.Clone();
                    MarkOutMasks(rejected, rejectedMasks, function, program);
                    failures?.Add(rejected, rejectedMasks);
                }
                var accepted = Outcome(requirement.Condition, beforeRequirement, true, ref remaining);
                Array.Copy(accepted, initialized, initialized.Length);
                MarkOutMasks(initialized, fieldMasks, function, program);
                return !CanBe(requirement.Condition, true, ref remaining);
            case BoundReturn result:
                if (inCleanup)
                {
                    throw InvalidEffect(result.Span, "FinallyReturn");
                }
                if (function.DoesNotReturn && reachable)
                {
                    throw InvalidEffect(result.Span, "DoesNotReturn");
                }
                if (function.IsClassConstructor && (!initialized[0]
                    || fieldMasks[0] != FullMask(program, function.Variables[0].Type)))
                {
                    throw InvalidEffect(result.Span, "Ownership");
                }
                var beforeReturn = (bool[])initialized.Clone();
                if (result.Value is null)
                {
                    if (function.ReturnType != PrimitiveType.Void)
                    {
                        throw Invalid();
                    }
                }
                else
                {
                    if (function.IsConstructor && result.Value is not BoundRead { Variable: 0 })
                    {
                        throw Invalid(result.Span);
                    }
                    if (function.IsConstructor && IsUnique(program, function.ReturnType)
                        && result.Value is BoundRead { Variable: 0 } receiver)
                    {
                        if (!initialized[0] || receiver.Type != function.ReturnType)
                        {
                            throw Invalid(result.Span);
                        }
                    }
                    else
                    {
                        Expression(result.Value, initialized, fieldMasks, declarations, function, program, ref remaining, depth + 1, failures);
                    }
                    if (result.Value.Type != function.ReturnType)
                    {
                        throw Invalid();
                    }
                }
                if (function.ReturnType == PrimitiveType.Bool && result.Value is BoundExpression boolResult)
                {
                    foreach (var predicate in new[] { true, false })
                    {
                        if (!CanBe(boolResult, predicate, ref remaining))
                        {
                            continue;
                        }
                        var returnState = Outcome(boolResult, beforeReturn, predicate, ref remaining);
                        var returnMasks = (uint[])fieldMasks.Clone();
                        MarkOutMasks(returnState, returnMasks, function, program);
                        var returnDeclarations = new HashSet<int>(declarations);
                        for (var index = cleanups.Count - 1; index >= 0; index--)
                        {
                            VerifyCleanup(cleanups[index].Body, returnState, returnMasks, returnDeclarations,
                                function, program, ref remaining, depth + 1, reachable, inUnsafe,
                                cleanups[index].Visible, successfulReturn: predicate);
                        }
                        if (predicate)
                        {
                            for (var i = 0; i < function.ParameterCount; i++)
                            {
                                if (function.Variables[i].IsOut && !returnState[i])
                                {
                                    throw Invalid(result.Span);
                                }
                            }
                        }
                    }
                }
                else if (cleanups.Count != 0)
                {
                    var returnState = (bool[])initialized.Clone();
                    var returnMasks = (uint[])fieldMasks.Clone();
                    var returnDeclarations = new HashSet<int>(declarations);
                    for (var index = cleanups.Count - 1; index >= 0; index--)
                    {
                        VerifyCleanup(cleanups[index].Body, returnState, returnMasks, returnDeclarations,
                            function, program, ref remaining, depth + 1, reachable, inUnsafe, cleanups[index].Visible);
                    }
                }
                return true;
            default:
                throw Invalid();
        }
    }

    private static bool TryFinallyStatement(BoundTryFinally cleanup, bool[] initialized, uint[] fieldMasks,
        HashSet<int> declarations, BoundFunction function, BoundProgram program, ref int remaining, int depth,
        bool reachable, List<CleanupScope> cleanups, bool inCleanup, bool inUnsafe, FailureEdges? failures, List<LoopScope> loops)
    {
        if (cleanup.Body is null || cleanup.Finally is null)
        {
            throw Invalid(cleanup.Span);
        }
        var cleanupFailures = program.RuntimePolicy == BoundRuntimePolicy.CheckedExceptions
            ? new FailureEdges() : null;
        var cleanupScope = new CleanupScope(cleanup.Finally, [.. declarations]);
        cleanups.Add(cleanupScope);
        var tryReturns = Statement(cleanup.Body, initialized, fieldMasks, declarations, function, program,
            ref remaining, depth + 1, reachable, cleanups, inCleanup, inUnsafe, cleanupFailures, loops);
        cleanups.RemoveAt(cleanups.Count - 1);
        if (cleanupFailures?.HasEdges == true)
        {
            var exceptionalState = cleanupFailures.State;
            var exceptionalMasks = cleanupFailures.Masks;
            VerifyCleanup(cleanup.Finally, exceptionalState, exceptionalMasks, [.. declarations],
                function, program, ref remaining, depth + 1, reachable, inUnsafe, cleanupScope.Visible);
            failures?.Add(exceptionalState, exceptionalMasks);
        }
        if (tryReturns)
        {
            var unreachableState = (bool[])initialized.Clone();
            var unreachableMasks = (uint[])fieldMasks.Clone();
            for (var index = 0; index < unreachableMasks.Length; index++)
            {
                unreachableState[index] = index < function.ParameterCount + (function.IsConstructor ? 1 : 0)
                    || declarations.Contains(index);
                if (unreachableState[index] && HasFields(program, function.Variables[index].Type))
                {
                    unreachableMasks[index] = FullMask(program, function.Variables[index].Type);
                }
            }
            VerifyCleanup(cleanup.Finally, unreachableState, unreachableMasks, declarations, function,
                program, ref remaining, depth + 1, reachable: false, inUnsafe, cleanupScope.Visible);
        }
        else
        {
            VerifyCleanup(cleanup.Finally, initialized, fieldMasks, declarations, function, program,
                ref remaining, depth + 1, reachable, inUnsafe, cleanupScope.Visible);
        }
        return tryReturns;
    }

    private static bool TryCatchStatement(BoundTryCatch recovery, bool[] initialized, uint[] fieldMasks,
        HashSet<int> declarations, BoundFunction function, BoundProgram program, ref int remaining, int depth,
        bool reachable, List<CleanupScope> cleanups, bool inCleanup, bool inUnsafe, FailureEdges? failures, List<LoopScope> loops)
    {
        if (!inUnsafe)
        {
            throw InvalidEffect(recovery.Span, "UnsafeRecovery");
        }
        if (program.RuntimePolicy != BoundRuntimePolicy.CheckedExceptions)
        {
            throw InvalidEffect(recovery.Span, "CheckedPolicy");
        }
        if (inCleanup || recovery.Body is null || recovery.Catch is null)
        {
            throw Invalid(recovery.Span);
        }
        var recoveryEntryState = (bool[])initialized.Clone();
        var recoveryEntryMasks = (uint[])fieldMasks.Clone();
        var recoveryDeclarations = new HashSet<int>(declarations);
        var caughtFailures = new FailureEdges();
        var recoveryScope = recovery.Finally is null ? null : new CleanupScope(recovery.Finally, [.. declarations]);
        if (recovery.Finally is not null)
        {
            cleanups.Add(recoveryScope!);
        }
        var recoveryBodyReturns = Statement(recovery.Body, initialized, fieldMasks, declarations, function,
            program, ref remaining, depth + 1, reachable, cleanups, inCleanup, inUnsafe, caughtFailures, loops);
        var catchState = caughtFailures.HasEdges ? caughtFailures.State : recoveryEntryState;
        var catchMasks = caughtFailures.HasEdges ? caughtFailures.Masks : recoveryEntryMasks;
        for (var index = 0; index < catchState.Length; index++)
        {
            if (index >= function.ParameterCount + (function.IsConstructor ? 1 : 0)
                && !recoveryDeclarations.Contains(index))
            {
                catchState[index] = false;
                catchMasks[index] = 0;
            }
        }
        var catchFailures = failures is not null || recovery.Finally is not null
            ? new FailureEdges() : null;
        var catchReturns = Statement(recovery.Catch, catchState, catchMasks, declarations, function,
            program, ref remaining, depth + 1, reachable && caughtFailures.HasEdges, cleanups,
            inCleanup, inUnsafe, catchFailures, loops);
        if (recovery.Finally is not null)
        {
            cleanups.RemoveAt(cleanups.Count - 1);
            if (catchFailures?.HasEdges == true)
            {
                var exceptionalState = catchFailures.State;
                var exceptionalMasks = catchFailures.Masks;
                VerifyCleanup(recovery.Finally, exceptionalState, exceptionalMasks, [.. declarations],
                    function, program, ref remaining, depth + 1, reachable, inUnsafe, recoveryScope!.Visible);
                failures?.Add(exceptionalState, exceptionalMasks);
            }
        }
        else if (catchFailures?.HasEdges == true)
        {
            failures!.Add(catchFailures.State, catchFailures.Masks);
        }
        for (var i = 0; i < initialized.Length; i++)
        {
            initialized[i] = recoveryBodyReturns && caughtFailures.HasEdges ? catchState[i]
                : !caughtFailures.HasEdges || catchReturns ? initialized[i]
                : initialized[i] && catchState[i];
            fieldMasks[i] = recoveryBodyReturns && caughtFailures.HasEdges ? catchMasks[i]
                : !caughtFailures.HasEdges || catchReturns ? fieldMasks[i]
                : fieldMasks[i] & catchMasks[i];
        }
        if (recovery.Finally is not null)
        {
            VerifyCleanup(recovery.Finally, initialized, fieldMasks, declarations, function,
                program, ref remaining, depth + 1,
                reachable && !(recoveryBodyReturns && (!caughtFailures.HasEdges || catchReturns)), inUnsafe,
                recoveryScope!.Visible);
        }
        return recoveryBodyReturns && (!caughtFailures.HasEdges || catchReturns);
    }

    private static void VerifyCleanup(BoundBlock cleanup, bool[] initialized, uint[] fieldMasks,
        HashSet<int> declarations, BoundFunction function, BoundProgram program, ref int remaining, int depth, bool reachable,
        bool inUnsafe, HashSet<int>? visible = null, bool? successfulReturn = null)
    {
        if (visible is not null)
        {
            for (var index = 0; index < initialized.Length; index++)
            {
                if (index >= function.ParameterCount + (function.IsConstructor ? 1 : 0)
                    && !visible.Contains(index))
                {
                    initialized[index] = false;
                    fieldMasks[index] = 0;
                }
            }
        }
        var outputTransfer = successfulReturn == true
            && cleanup.Statements is [BoundDrop { IsOutput: true }];
        var checkState = outputTransfer ? (bool[])initialized.Clone() : initialized;
        var checkMasks = outputTransfer ? (uint[])fieldMasks.Clone() : fieldMasks;
        if (Statement(cleanup, checkState, checkMasks, declarations, function with { IsNoThrow = true },
            program, ref remaining, depth, reachable, inCleanup: true, inUnsafe: inUnsafe)
            || HasUnsupportedCleanupFlow(cleanup, program, ref remaining, depth))
        {
            throw InvalidEffect(cleanup.Span, "FinallyNoReturn");
        }
    }

    private sealed record CleanupScope(BoundBlock Body, HashSet<int> Visible);

    private sealed class LoopScope(int cleanupDepth)
    {
        internal int CleanupDepth { get; } = cleanupDepth;
        internal FailureEdges Breaks { get; } = new();
        internal FailureEdges Continues { get; } = new();
    }

    private static bool HasUnsupportedCleanupFlow(BoundStatement statement, BoundProgram program, ref int remaining, int depth)
    {
        Count(ref remaining, depth);
        switch (statement)
        {
            case BoundBlock block:
                foreach (var child in block.Statements)
                {
                    if (HasUnsupportedCleanupFlow(child, program, ref remaining, depth + 1))
                    {
                        return true;
                    }
                }
                return false;
            case BoundExpressionStatement { Expression: BoundCall call }:
                return program.Functions[call.Function].DoesNotReturn;
            case BoundIf conditional:
                return (CanBe(conditional.Condition, true, ref remaining)
                    && HasUnsupportedCleanupFlow(conditional.Then, program, ref remaining, depth + 1))
                    || (conditional.Else is not null && CanBe(conditional.Condition, false, ref remaining)
                        && HasUnsupportedCleanupFlow(conditional.Else, program, ref remaining, depth + 1));
            case BoundWhile loop:
                return CanBe(loop.Condition, true, ref remaining)
                    && loop is not {
                        Condition: BoundConstant { Type: PrimitiveType.Bool, Value: 1 },
                        Body: BoundBlock { Statements: [BoundBreak] },
                    };
            case BoundTryFinally nested:
                return HasUnsupportedCleanupFlow(nested.Body, program, ref remaining, depth + 1)
                    || HasUnsupportedCleanupFlow(nested.Finally, program, ref remaining, depth + 1);
            case BoundViewScope view:
                return HasUnsupportedCleanupFlow(view.Body, program, ref remaining, depth + 1);
            case BoundUnsafe unsafeBlock:
                return HasUnsupportedCleanupFlow(unsafeBlock.Body, program, ref remaining, depth + 1);
            default:
                return false;
        }
    }

    private sealed class FailureEdges
    {
        private bool[]? _state;
        private uint[]? _masks;

        internal bool HasEdges => _state is not null;

        internal bool[] State => (bool[])(_state ?? throw Invalid()).Clone();

        internal uint[] Masks => (uint[])(_masks ?? throw Invalid()).Clone();

        internal void Add(bool[] state, uint[] masks)
        {
            if (_state is null)
            {
                _state = (bool[])state.Clone();
                _masks = (uint[])masks.Clone();
                return;
            }
            for (var index = 0; index < state.Length; index++)
            {
                _state[index] &= state[index];
                _masks![index] &= masks[index];
            }
        }
    }

    private static int VerifyUniqueFieldPlace(BoundFieldRead place, bool[] initialized, uint[] fieldMasks,
        BoundFunction function, BoundProgram program, ref int remaining, int depth)
    {
        Count(ref remaining, depth);
        if (place is null || place.Receiver is null)
        {
            throw Invalid();
        }
        var owner = (int)place.Receiver.Type - 100;
        if (owner < 0 || owner >= program.Structures.Length
            || place.Field < 0 || place.Field >= program.Structures[owner].Fields.Length
            || !program.Structures[owner].IsUnique || !IsUnique(program, place.Type)
            || place.Type != program.Structures[owner].Fields[place.Field].Type
            || !CanAccessField(function, program.Structures[owner], place.Field))
        {
            throw InvalidEffect(place.Span, "Ownership");
        }
        if (place.Receiver is BoundFieldRead parent)
        {
            return VerifyUniqueFieldPlace(parent, initialized, fieldMasks, function, program,
                ref remaining, depth + 1);
        }
        if (place.Receiver is not BoundRead root || root.Variable < 0
            || root.Variable >= initialized.Length
            || root.Type != function.Variables[root.Variable].Type
            || !IsUnique(program, root.Type)
            || (!initialized[root.Variable]
                && !(function.IsConstructor && root.Variable == 0
                    && (fieldMasks[0] & (1u << place.Field)) != 0)))
        {
            throw InvalidEffect(place.Span, "Ownership");
        }
        return root.Variable;
    }

    private static int VerifyMutableFieldPlace(BoundFieldRead place, bool[] initialized,
        BoundFunction function, BoundProgram program, ref int remaining, int depth, bool allowBufferLeaf = false)
    {
        Count(ref remaining, depth);
        if (place is null || place.Receiver is null)
        {
            throw Invalid();
        }
        var owner = (int)place.Receiver.Type - 100;
        if (owner < 0 || owner >= program.Structures.Length
            || place.Field < 0 || place.Field >= program.Structures[owner].Fields.Length
            || !program.Structures[owner].Fields[place.Field].IsMutable
            || !CanAccessField(function, program.Structures[owner], place.Field)
            || place.Type != program.Structures[owner].Fields[place.Field].Type
            || (!(allowBufferLeaf && place.Type == PrimitiveType.OwnedInt32Buffer)
                && (!HasFields(program, place.Type) || ManagedClassTypes.IsClass(place.Type))))
        {
            throw InvalidEffect(place.Span, "Ownership");
        }
        if (place.Receiver is BoundFieldRead parent)
        {
            return VerifyMutableFieldPlace(parent, initialized, function, program, ref remaining, depth + 1);
        }
        if (place.Receiver is not BoundRead root || root.Variable < 0
            || root.Variable >= initialized.Length || !initialized[root.Variable]
            || root.Type != function.Variables[root.Variable].Type
            || !function.Variables[root.Variable].IsMutable)
        {
            throw InvalidEffect(place.Span, "Ownership");
        }
        return root.Variable;
    }

    [SuppressMessage("Style", "IDE0072:Populate switch", Justification = "Unsupported token kinds must remain invalid rather than becoming accepted operators.")]
    private static void Expression(BoundExpression expression, bool[] initialized, uint[] fieldMasks,
        HashSet<int> declarations, BoundFunction function,
        BoundProgram program, ref int remaining, int depth, FailureEdges? failures = null)
    {
        Count(ref remaining, depth);
        if (expression is null || !ValidType(expression.Type, allowVoid: expression is BoundCall, program))
        {
            throw Invalid();
        }
        switch (expression)
        {
            case BoundConstant constant:
                if (constant.Type is not (PrimitiveType.Int32 or PrimitiveType.UInt32 or PrimitiveType.Int64 or PrimitiveType.UInt64 or PrimitiveType.Char16 or PrimitiveType.Bool)
                    || (constant.Type is PrimitiveType.Int32 or PrimitiveType.UInt32
                        && constant.Value is < int.MinValue or > int.MaxValue)
                    || (constant.Type == PrimitiveType.Bool && constant.Value is not (0 or 1))
                    || (constant.Type == PrimitiveType.Char16 && constant.Value is < 0 or > ushort.MaxValue))
                {
                    throw Invalid();
                }
                break;
            case BoundNull:
                if (!ManagedClassTypes.IsOptional(expression.Type))
                {
                    throw Invalid();
                }
                break;
            case BoundOptionalLift lift:
                if (!ManagedClassTypes.IsOptional(lift.Type)
                    || lift.Value is null || lift.Value.Type != ManagedClassTypes.Required(lift.Type))
                {
                    throw Invalid();
                }
                Expression(lift.Value, initialized, fieldMasks, declarations, function, program,
                    ref remaining, depth + 1, failures);
                break;
            case BoundReadonlyProjection projection:
                if (projection.Value is null || !ManagedClassTypes.IsWritable(projection.Value.Type)
                    || projection.Type != ManagedClassTypes.Readonly(projection.Value.Type))
                {
                    throw Invalid();
                }
                Expression(projection.Value, initialized, fieldMasks, declarations, function, program,
                    ref remaining, depth + 1, failures);
                break;
            case BoundIsNull test:
                if (test.Value is null || !ManagedClassTypes.IsOptional(test.Value.Type))
                {
                    throw Invalid();
                }
                Expression(test.Value, initialized, fieldMasks, declarations, function, program,
                    ref remaining, depth + 1, failures);
                break;
            case BoundIsSome pattern:
                if (pattern.Value is null || !ManagedClassTypes.IsOptional(pattern.Value.Type)
                    || pattern.Variable < function.ParameterCount + (function.IsConstructor ? 1 : 0)
                    || pattern.Variable < 0 || pattern.Variable >= initialized.Length
                    || function.Variables[pattern.Variable].Type != ManagedClassTypes.Required(pattern.Value.Type)
                    || function.Variables[pattern.Variable].IsOut
                    || function.Variables[pattern.Variable].IsMutable
                    || declarations.Contains(pattern.Variable))
                {
                    throw Invalid();
                }
                Expression(pattern.Value, initialized, fieldMasks, declarations, function, program,
                    ref remaining, depth + 1, failures);
                _ = declarations.Add(pattern.Variable);
                break;
            case BoundCast cast:
                Expression(cast.Operand, initialized, fieldMasks, declarations, function, program, ref remaining, depth + 1, failures);
                if (cast.Operand.Type is not (PrimitiveType.Int32 or PrimitiveType.UInt32 or PrimitiveType.Int64 or PrimitiveType.UInt64 or PrimitiveType.Char16)
                    || cast.Type is not (PrimitiveType.Int32 or PrimitiveType.UInt32 or PrimitiveType.Int64 or PrimitiveType.UInt64 or PrimitiveType.Char16))
                {
                    throw Invalid();
                }
                if (function.IsNoThrow && !IsTotalConversion(cast.Operand.Type, cast.Type))
                {
                    throw InvalidEffect(cast.Span);
                }
                if (!IsTotalConversion(cast.Operand.Type, cast.Type))
                {
                    failures?.Add(initialized, fieldMasks);
                }
                break;
            case BoundFieldRead fieldRead:
                if (fieldRead.Receiver is null)
                {
                    throw Invalid(fieldRead.Span);
                }
                var readType = fieldRead.Receiver.Type;
                if (!HasFields(program, readType)
                    || fieldRead.Field < 0 || fieldRead.Field >= Fields(program, readType).Length
                    || !CanAccessField(function, program, readType, fieldRead.Field)
                    || fieldRead.Type != Fields(program, readType)[fieldRead.Field].Type)
                {
                    throw Invalid();
                }
                if (IsUnique(program, readType) && fieldRead.Receiver is not BoundRead)
                {
                    if (fieldRead.Receiver is not BoundFieldRead candidate || !IsUnique(program, candidate.Type))
                    {
                        throw InvalidEffect(fieldRead.Span, "Ownership");
                    }
                }
                if (fieldRead.Receiver is BoundRead owned && IsUnique(program, owned.Type))
                {
                    if (owned.Variable < 0 || owned.Variable >= initialized.Length
                        || owned.Type != function.Variables[owned.Variable].Type
                        || (!initialized[owned.Variable]
                            && (!function.IsConstructor || owned.Variable != 0
                                || (fieldMasks[0] & (1u << fieldRead.Field)) == 0)))
                    {
                        throw InvalidEffect(fieldRead.Span, "Ownership");
                    }
                }
                else if (fieldRead.Receiver is BoundFieldRead nested && IsUnique(program, nested.Type))
                {
                    _ = VerifyUniqueFieldPlace(nested, initialized, fieldMasks, function, program, ref remaining, depth + 1);
                }
                else if (fieldRead.Receiver is BoundRead partial
                    && (function.IsConstructor || function.IsClassConstructor) && partial.Variable == 0)
                {
                    if (partial.Type != function.Variables[0].Type || (fieldMasks[0] & (1u << fieldRead.Field)) == 0)
                    {
                        throw Invalid(fieldRead.Span);
                    }
                }
                else
                {
                    Expression(fieldRead.Receiver, initialized, fieldMasks, declarations, function, program, ref remaining, depth + 1, failures);
                }
                if (IsUnique(program, fieldRead.Type))
                {
                    throw InvalidEffect(fieldRead.Span, "Ownership");
                }
                break;
            case BoundRead read:
                if (IsUnique(program, read.Type))
                {
                    throw InvalidEffect(read.Span, "Ownership");
                }
                if (read.Variable < 0 || read.Variable >= initialized.Length || !initialized[read.Variable]
                    || read.Type != function.Variables[read.Variable].Type)
                {
                    throw Invalid(read.Span);
                }
                break;
            case BoundViewLength length:
                if (length.Variable < 0 || length.Variable >= initialized.Length
                    || !initialized[length.Variable]
                    || function.Variables[length.Variable].Type is not (PrimitiveType.ReadonlyInt32BufferView
                        or PrimitiveType.MutableInt32BufferView))
                {
                    throw InvalidEffect(length.Span, "Ownership");
                }
                break;
            case BoundViewRead viewRead:
                if (viewRead.Variable < 0 || viewRead.Variable >= initialized.Length
                    || !initialized[viewRead.Variable]
                    || function.Variables[viewRead.Variable].Type is not (PrimitiveType.ReadonlyInt32BufferView
                        or PrimitiveType.MutableInt32BufferView))
                {
                    throw InvalidEffect(viewRead.Span, "Ownership");
                }
                Expression(viewRead.Index, initialized, fieldMasks, declarations, function, program, ref remaining, depth + 1, failures);
                if (viewRead.Index.Type != PrimitiveType.Int32)
                {
                    throw Invalid();
                }
                if (function.IsNoThrow)
                {
                    throw InvalidEffect(viewRead.Span);
                }
                failures?.Add(initialized, fieldMasks);
                break;
            case BoundMove move:
                if (!IsUnique(program, move.Type) || move.Variable < 0 || move.Variable >= initialized.Length
                    || (move.Variable == 0 && function.DeclaringStruct is not null && !function.IsStatic)
                    || !initialized[move.Variable] || move.Type != function.Variables[move.Variable].Type
                    || (move.Variable >= function.ParameterCount + (function.IsConstructor ? 1 : 0)
                        && !declarations.Contains(move.Variable)))
                {
                    throw InvalidEffect(move.Span, "Ownership");
                }
                initialized[move.Variable] = false;
                fieldMasks[move.Variable] = 0;
                break;
            case BoundReceiverLoan loan:
                throw InvalidEffect(loan.Span, "Ownership");
            case BoundMutableReceiverLoan mutableLoan:
                throw InvalidEffect(mutableLoan.Span, "Ownership");
            case BoundMutableFieldLoan mutableField:
                throw InvalidEffect(mutableField.Span, "Ownership");
            case BoundFieldLoan fieldLoan:
                throw InvalidEffect(fieldLoan.Span, "Ownership");
            case BoundOutArgument:
                throw Invalid();
            case BoundBufferRead bufferRead:
                if (function.IsNoThrow && !IsProvenBufferIndex(bufferRead.Buffer, bufferRead.Index, program))
                {
                    throw InvalidEffect(bufferRead.Span);
                }
                if (bufferRead.Buffer < 0 || bufferRead.Buffer >= program.Buffers.Length
                    || bufferRead.Type != program.Buffers[bufferRead.Buffer].ElementType)
                {
                    throw Invalid();
                }
                Expression(bufferRead.Index, initialized, fieldMasks, declarations, function, program, ref remaining, depth + 1, failures);
                failures?.Add(initialized, fieldMasks);
                if (bufferRead.Index.Type != PrimitiveType.Int32)
                {
                    throw Invalid();
                }
                break;
            case BoundUnary unary:
                Expression(unary.Operand, initialized, fieldMasks, declarations, function, program, ref remaining, depth + 1, failures);
                var unaryValid = unary.Operator switch {
                    TokenKind.Plus or TokenKind.Tilde =>
                        unary.Operand.Type is PrimitiveType.Int32 or PrimitiveType.UInt32 or PrimitiveType.Int64 or PrimitiveType.UInt64 && unary.Type == unary.Operand.Type,
                    TokenKind.Minus => unary.Operand.Type is PrimitiveType.Int32 or PrimitiveType.Int64 && unary.Type == unary.Operand.Type,
                    TokenKind.Bang => unary.Operand.Type == PrimitiveType.Bool && unary.Type == PrimitiveType.Bool,
                    _ => false,
                };
                if (!unaryValid)
                {
                    throw Invalid();
                }
                if (function.IsNoThrow && unary.Operator == TokenKind.Minus
                    && (unary.Operand is not BoundConstant operand
                        || operand.Value == (unary.Type == PrimitiveType.Int32 ? int.MinValue : long.MinValue)))
                {
                    throw InvalidEffect(unary.Span);
                }
                if (unary.Operator == TokenKind.Minus)
                {
                    failures?.Add(initialized, fieldMasks);
                }
                break;
            case BoundBinary binary:
                var beforeLeft = (bool[])initialized.Clone();
                Expression(binary.Left, initialized, fieldMasks, declarations, function, program, ref remaining, depth + 1, failures);
                if (binary.Operator is TokenKind.AmpersandAmpersand or TokenKind.PipePipe)
                {
                    var evaluateWhenTrue = binary.Operator == TokenKind.AmpersandAmpersand;
                    var rightState = Outcome(binary.Left, beforeLeft, evaluateWhenTrue, ref remaining);
                    var rightMasks = (uint[])fieldMasks.Clone();
                    MarkOutMasks(rightState, rightMasks, function, program);
                    Expression(binary.Right, rightState, rightMasks, declarations, function, program, ref remaining, depth + 1,
                        CanBe(binary.Left, evaluateWhenTrue, ref remaining) ? failures : null);
                    var skippedState = Outcome(binary.Left, beforeLeft, !evaluateWhenTrue, ref remaining);
                    var skippedMasks = (uint[])fieldMasks.Clone();
                    MarkOutMasks(skippedState, skippedMasks, function, program);
                    var evaluates = CanBe(binary.Left, evaluateWhenTrue, ref remaining);
                    var skips = CanBe(binary.Left, !evaluateWhenTrue, ref remaining);
                    for (var i = 0; i < initialized.Length; i++)
                    {
                        initialized[i] = !evaluates ? skippedState[i] : !skips ? rightState[i]
                            : rightState[i] && skippedState[i];
                        fieldMasks[i] = !evaluates ? skippedMasks[i] : !skips ? rightMasks[i]
                            : rightMasks[i] & skippedMasks[i];
                    }
                }
                else
                {
                    Expression(binary.Right, initialized, fieldMasks, declarations, function, program, ref remaining, depth + 1, failures);
                }
                var operands = binary.Left.Type == binary.Right.Type && binary.Left.Type != PrimitiveType.Void;
                var binaryValid = binary.Operator switch {
                    TokenKind.Plus or TokenKind.Minus or TokenKind.Star or TokenKind.Slash or TokenKind.Percent =>
                        operands && binary.Left.Type is PrimitiveType.Int32 or PrimitiveType.UInt32 or PrimitiveType.Int64 or PrimitiveType.UInt64
                            && binary.Type == binary.Left.Type,
                    TokenKind.Less or TokenKind.LessEquals or TokenKind.Greater or TokenKind.GreaterEquals =>
                        operands && binary.Left.Type is PrimitiveType.Int32 or PrimitiveType.UInt32 or PrimitiveType.Int64 or PrimitiveType.UInt64 or PrimitiveType.Char16
                            && binary.Type == PrimitiveType.Bool,
                    TokenKind.EqualsEquals or TokenKind.BangEquals =>
                        operands && binary.Left.Type is PrimitiveType.Int32 or PrimitiveType.UInt32 or PrimitiveType.Int64 or PrimitiveType.UInt64 or PrimitiveType.Char16 or PrimitiveType.Bool
                            && binary.Type == PrimitiveType.Bool,
                    TokenKind.Ampersand or TokenKind.Pipe or TokenKind.Caret =>
                        operands && binary.Left.Type is PrimitiveType.Int32 or PrimitiveType.UInt32 or PrimitiveType.Int64 or PrimitiveType.UInt64 or PrimitiveType.Bool
                            && binary.Type == binary.Left.Type,
                    TokenKind.AmpersandAmpersand or TokenKind.PipePipe =>
                        operands && binary.Left.Type == PrimitiveType.Bool && binary.Type == PrimitiveType.Bool,
                    _ => false,
                };
                if (!binaryValid)
                {
                    throw Invalid();
                }
                if (function.IsNoThrow && binary.Operator is TokenKind.Plus or TokenKind.Minus or TokenKind.Star
                    or TokenKind.Slash or TokenKind.Percent)
                {
                    throw InvalidEffect(binary.Span);
                }
                if (binary.Operator is TokenKind.Plus or TokenKind.Minus or TokenKind.Star or TokenKind.Slash or TokenKind.Percent)
                {
                    failures?.Add(initialized, fieldMasks);
                }
                break;
            case BoundCall call:
                if (call.Function < 0 || call.Function >= program.Functions.Length || call.Arguments.IsDefault)
                {
                    throw Invalid();
                }
                if (call.Arguments.Length > MaximumArguments)
                {
                    throw Limit();
                }
                var target = program.Functions[call.Function];
                if (target.IsCleanup || target.IsClassConstructor)
                {
                    throw InvalidEffect(call.Span, "Ownership");
                }
                if (function.IsNoThrow && !target.IsNoThrow)
                {
                    throw InvalidEffect(call.Span);
                }
                if (call.Arguments.Length != target.ParameterCount || call.Type != target.ReturnType)
                {
                    throw Invalid();
                }
                if (target.DeclaringStruct is not null && !target.IsStatic && !target.IsConstructor
                    && target.Variables[0].IsMutable
                    && (int)target.Variables[0].Type is >= 100 and < ManagedClassTypes.First
                    && call.Arguments[0] is not (BoundMutableReceiverLoan or BoundMutableFieldLoan))
                {
                    throw Invalid();
                }
                if (target.IsPrivate)
                {
                    if (function.DeclaringStruct != target.DeclaringStruct)
                    {
                        throw Invalid();
                    }
                }
                var outTargets = new HashSet<int>();
                var loanRoot = -1;
                var loanOfInitializedField = false;
                var reservedReceiver = -1;
                for (var i = 0; i < call.Arguments.Length; i++)
                {
                    var argument = call.Arguments[i];
                    if (reservedReceiver >= 0 && InvalidatesReservedOwner(argument, reservedReceiver,
                        ref remaining, depth + 1))
                    {
                        throw InvalidEffect(argument.Span, "Ownership");
                    }
                    if (argument is BoundOutArgument output)
                    {
                        if (output.Variable < 0 || output.Variable >= initialized.Length
                            || (output.Variable < function.ParameterCount + (function.IsConstructor ? 1 : 0)
                                && (!function.Variables[output.Variable].IsOut || output.DeclaresVariable))
                            || output.Type != function.Variables[output.Variable].Type
                            || (output.DeclaresVariable && (!declarations.Add(output.Variable) || initialized[output.Variable]))
                            || (!output.DeclaresVariable && !output.IsInit
                                && !function.Variables[output.Variable].IsMutable)
                            || (output.IsInit && initialized[output.Variable])
                            || output.IsInit != target.Variables[i + (target.IsConstructor ? 1 : 0)].IsInit)
                        {
                            throw Invalid();
                        }
                        if (!outTargets.Add(output.Variable))
                        {
                            throw InvalidEffect(output.Span, "Ownership");
                        }
                        initialized[output.Variable] = false;
                        fieldMasks[output.Variable] = 0;
                    }
                    else
                    {
                        if (argument is BoundMutableReceiverLoan mutableLoan)
                        {
                            var bufferMutation = target.Intrinsic is BoundIntrinsic.BufferSet
                                or BoundIntrinsic.BufferTryAppend or BoundIntrinsic.BufferClear;
                            var structMutation = target.Intrinsic == BoundIntrinsic.None
                                && target.DeclaringStruct is not null && !target.IsStatic
                                && !target.IsConstructor && target.Variables[0].IsMutable
                                && HasFields(program, mutableLoan.Type)
                                && !ManagedClassTypes.IsClass(mutableLoan.Type);
                            if (i != 0 || (!bufferMutation && !structMutation)
                                || mutableLoan.Variable < 0 || mutableLoan.Variable >= initialized.Length
                                || (bufferMutation && mutableLoan.Type != PrimitiveType.OwnedInt32Buffer)
                                || mutableLoan.Type != target.Variables[0].Type
                                || function.Variables[mutableLoan.Variable].Type != mutableLoan.Type
                                || !function.Variables[mutableLoan.Variable].IsMutable
                                || !initialized[mutableLoan.Variable])
                            {
                                throw InvalidEffect(mutableLoan.Span, "Ownership");
                            }
                            loanRoot = mutableLoan.Variable;
                        }
                        else if (argument is BoundMutableFieldLoan mutableField)
                        {
                            var bufferMutation = target.Intrinsic is BoundIntrinsic.BufferSet
                                or BoundIntrinsic.BufferTryAppend or BoundIntrinsic.BufferClear;
                            if (i != 0 || (target.Intrinsic != BoundIntrinsic.None && !bufferMutation)
                                || target.DeclaringStruct is null || target.IsStatic || target.IsConstructor
                                || !target.Variables[0].IsMutable || mutableField.Place is null
                                || mutableField.Type != target.Variables[0].Type
                                || mutableField.Place.Type != mutableField.Type
                                || (bufferMutation && mutableField.Type != PrimitiveType.OwnedInt32Buffer))
                            {
                                throw InvalidEffect(mutableField.Span, "Ownership");
                            }
                            loanRoot = VerifyMutableFieldPlace(mutableField.Place, initialized,
                                function, program, ref remaining, depth + 1, bufferMutation);
                        }
                        else if (argument is BoundReceiverLoan loan)
                        {
                            if (i != 0 || target.DeclaringStruct is null || target.IsStatic || target.IsConstructor
                                || loan.Variable < 0 || loan.Variable >= initialized.Length
                                || loan.Type != target.Variables[0].Type || !IsUnique(program, loan.Type)
                                || target.Variables[0].IsMutable
                                || !initialized[loan.Variable]
                                || function.Variables[loan.Variable].Type != loan.Type)
                            {
                                throw InvalidEffect(loan.Span, "Ownership");
                            }
                            loanRoot = loan.Variable;
                        }
                        else if (argument is BoundFieldLoan fieldLoan)
                        {
                            if (i != 0 || target.DeclaringStruct is null || target.IsStatic || target.IsConstructor
                                || fieldLoan.Place is null
                                || fieldLoan.Type != target.Variables[0].Type
                                || target.Variables[0].IsMutable
                                || fieldLoan.Place.Type != fieldLoan.Type || !IsUnique(program, fieldLoan.Type))
                            {
                                throw InvalidEffect(fieldLoan.Span, "Ownership");
                            }
                            loanRoot = VerifyUniqueFieldPlace(fieldLoan.Place, initialized, fieldMasks,
                                function, program, ref remaining, depth + 1);
                            loanOfInitializedField = true;
                        }
                        else
                        {
                            if (IsUnique(program, argument.Type)
                                && argument is not (BoundMove or BoundCall))
                            {
                                throw InvalidEffect(argument.Span, "Ownership");
                            }
                            Expression(argument, initialized, fieldMasks, declarations, function, program, ref remaining, depth + 1, failures);
                        }
                    }
                    if (i == 0 && loanRoot >= 0)
                    {
                        reservedReceiver = loanRoot;
                    }
                    if (argument.Type != target.Variables[i + (target.IsConstructor ? 1 : 0)].Type)
                    {
                        throw Invalid();
                    }
                    if ((argument is BoundOutArgument)
                        != target.Variables[i + (target.IsConstructor ? 1 : 0)].IsOut)
                    {
                        throw Invalid();
                    }
                }
                if (loanRoot >= 0 && !initialized[loanRoot]
                    && !(loanOfInitializedField && function.IsConstructor && loanRoot == 0))
                {
                    throw InvalidEffect(call.Span, "Ownership");
                }
                if (!target.IsNoThrow)
                {
                    failures?.Add(initialized, fieldMasks);
                }
                break;
            default:
                throw Invalid();
        }
    }

    private static bool ValidType(PrimitiveType type, bool allowVoid, BoundProgram program) =>
        type is PrimitiveType.Int32 or PrimitiveType.UInt32 or PrimitiveType.Int64 or PrimitiveType.UInt64
            or PrimitiveType.Char16 or PrimitiveType.Bool or PrimitiveType.OwnedInt32Buffer
            or PrimitiveType.ManagedNode or PrimitiveType.OptionalManagedNode
            || (allowVoid && type == PrimitiveType.Void)
            || ((int)type >= 100 && (int)type - 100 < program.Structures.Length)
            || (ManagedClassTypes.IsManaged(type) && (ManagedClassTypes.Readonly(type) is
                PrimitiveType.ManagedNode or PrimitiveType.OptionalManagedNode))
            || ((ManagedClassTypes.IsClass(type) || (ManagedClassTypes.IsOptional(type)
                && type is not (PrimitiveType.OptionalManagedNode or PrimitiveType.OptionalWriteManagedNode)))
                && ManagedClassTypes.ClassIndex(type) < program.Classes.Length
                && (!ManagedClassTypes.IsWritable(type)
                    || program.Classes[ManagedClassTypes.ClassIndex(type)].IsMutable));

    internal static bool IsClassArgumentType(PrimitiveType type, IReadOnlyList<BoundStruct> structures) =>
        type is PrimitiveType.Int32 or PrimitiveType.UInt32 or PrimitiveType.Int64
            or PrimitiveType.UInt64 or PrimitiveType.Char16 or PrimitiveType.Bool
            || ManagedClassTypes.IsManaged(type)
            || ((int)type >= 100 && (int)type - 100 < structures.Count
                && !structures[(int)type - 100].IsUnique);

    private static bool IsUnique(BoundProgram program, PrimitiveType type) =>
        type == PrimitiveType.OwnedInt32Buffer
            || ((int)type >= 100 && (int)type - 100 < program.Structures.Length
                && program.Structures[(int)type - 100].IsUnique);

    private static void VerifyClassFactory(BoundProgram program, BoundFunction function, int index)
    {
        var constructorIndex = function.ClassConstructor;
        if (constructorIndex < 0 || constructorIndex >= program.Functions.Length || constructorIndex == index
            || function.DeclaringStruct is not string name
            || function.Name != name + ".TryCreate"
            || function.ReturnType != PrimitiveType.Bool || function.IsConstructor
            || function.IsClassConstructor || function.IsExternal
            || !function.IsStatic || function.IsCleanup || function.DoesNotReturn
            || function.Body.Statements.IsDefault || !function.Body.Statements.IsEmpty
            || function.ParameterCount < 1 || function.ParameterCount > MaximumArguments
            || function.Variables.Length != function.ParameterCount + 1)
        {
            throw Invalid();
        }
        var constructor = program.Functions[constructorIndex];
        var type = function.Variables[function.ParameterCount - 1].Type;
        if (constructor is null || constructor.Variables.IsDefaultOrEmpty
            || constructor.ParameterCount < 1 || constructor.ParameterCount > MaximumArguments
            || constructor.Variables.Length < constructor.ParameterCount
            || !ManagedClassTypes.IsClass(type)
            || ManagedClassTypes.ClassIndex(type) >= program.Classes.Length
            || program.Classes[ManagedClassTypes.ClassIndex(type)].Name != name
            || ManagedClassTypes.IsWritable(type) != program.Classes[ManagedClassTypes.ClassIndex(type)].IsMutable
            || !constructor.IsClassConstructor || constructor.DeclaringStruct != name
            || function.IsPrivate != constructor.IsPrivate
            || function.IsInternal != !constructor.IsPrivate
            || constructor.ParameterCount != function.ParameterCount
            || constructor.Variables[0].Type != type
            || function.Variables[function.ParameterCount - 1] is not { IsOut: true, IsMutable: false }
            || function.Variables[function.ParameterCount] is not { IsOut: false, IsMutable: false }
            || function.Variables[function.ParameterCount].Type != type
            || function.IsNoThrow != constructor.IsNoThrow)
        {
            throw Invalid();
        }
        for (var parameter = 0; parameter < function.ParameterCount - 1; parameter++)
        {
            if (function.Variables[parameter].Type != constructor.Variables[parameter + 1].Type
                || function.Variables[parameter].IsOut || function.Variables[parameter].IsMutable
                || constructor.Variables[parameter + 1].IsOut
                || !IsClassArgumentType(function.Variables[parameter].Type, program.Structures))
            {
                throw Invalid();
            }
        }
    }

    private static bool InvalidatesReservedOwner(BoundExpression expression, int variable, ref int remaining, int depth)
    {
        Count(ref remaining, depth);
        if (expression is BoundCall call)
        {
            foreach (var argument in call.Arguments)
            {
                if (InvalidatesReservedOwner(argument, variable, ref remaining, depth + 1))
                {
                    return true;
                }
            }
            return false;
        }
        return expression switch {
            BoundMove move => move.Variable == variable,
            BoundOutArgument output => output.Variable == variable,
            BoundMutableReceiverLoan loan => loan.Variable == variable,
            BoundMutableFieldLoan loan => LoanRoot(loan, ref remaining, depth + 1) == variable,
            BoundUnary unary => InvalidatesReservedOwner(unary.Operand, variable, ref remaining, depth + 1),
            BoundCast cast => InvalidatesReservedOwner(cast.Operand, variable, ref remaining, depth + 1),
            BoundOptionalLift lift => InvalidatesReservedOwner(lift.Value, variable, ref remaining, depth + 1),
            BoundReadonlyProjection projection => InvalidatesReservedOwner(projection.Value, variable, ref remaining, depth + 1),
            BoundIsNull test => InvalidatesReservedOwner(test.Value, variable, ref remaining, depth + 1),
            BoundIsSome pattern => InvalidatesReservedOwner(pattern.Value, variable, ref remaining, depth + 1),
            BoundBinary binary => InvalidatesReservedOwner(binary.Left, variable, ref remaining, depth + 1)
                || InvalidatesReservedOwner(binary.Right, variable, ref remaining, depth + 1),
            BoundFieldRead field => InvalidatesReservedOwner(field.Receiver, variable, ref remaining, depth + 1),
            BoundBufferRead buffer => InvalidatesReservedOwner(buffer.Index, variable, ref remaining, depth + 1),
            BoundViewRead view => InvalidatesReservedOwner(view.Index, variable, ref remaining, depth + 1),
            _ => false,
        };
    }

    private static bool IsProvenBufferIndex(int buffer, BoundExpression index, BoundProgram program) =>
        buffer >= 0 && buffer < program.Buffers.Length
            && index is BoundConstant { Type: PrimitiveType.Int32 } constant
            && constant.Value >= 0 && constant.Value < program.Buffers[buffer].Values.Length;

    private static bool IsTotalConversion(PrimitiveType source, PrimitiveType destination) =>
        source == destination || source == PrimitiveType.Char16
            || (source == PrimitiveType.Int32 && destination == PrimitiveType.Int64)
            || (source == PrimitiveType.UInt32 && destination is PrimitiveType.Int64 or PrimitiveType.UInt64);

    private static uint FullMask(BoundProgram program, PrimitiveType type) =>
        (1u << Fields(program, type).Length) - 1;

    private static bool HasFields(BoundProgram program, PrimitiveType type) =>
        ManagedClassTypes.IsClass(type)
            ? ManagedClassTypes.ClassIndex(type) < program.Classes.Length
            : (int)type >= 100 && (int)type < ManagedClassTypes.First
                && (int)type - 100 < program.Structures.Length;

    private static ImmutableArray<BoundStructField> Fields(BoundProgram program, PrimitiveType type) =>
        ManagedClassTypes.IsClass(type) ? program.Classes[ManagedClassTypes.ClassIndex(type)].Fields
            : program.Structures[(int)type - 100].Fields;

    private static bool CanAccessField(BoundFunction function, BoundProgram program, PrimitiveType type,
        int field) =>
        ManagedClassTypes.IsClass(type)
            ? !program.Classes[ManagedClassTypes.ClassIndex(type)].Fields[field].IsPrivate
                || function.DeclaringStruct == program.Classes[ManagedClassTypes.ClassIndex(type)].Name
            : CanAccessField(function, program.Structures[(int)type - 100], field);

    private static bool CanAccessField(BoundFunction function, BoundStruct structure, int field) =>
        !structure.Fields[field].IsPrivate || function.DeclaringStruct == structure.Name;

    private static void MarkOutMasks(bool[] initialized, uint[] masks, BoundFunction function, BoundProgram program)
    {
        for (var i = 0; i < initialized.Length; i++)
        {
            var type = function.Variables[i].Type;
            if (initialized[i] && HasFields(program, type))
            {
                masks[i] = FullMask(program, type);
            }
        }
    }

    private static bool[] Outcome(BoundExpression expression, bool[] initialized, bool result, ref int remaining)
    {
        Count(ref remaining, 0);
        if (expression is BoundUnary { Operator: TokenKind.Bang } unary)
        {
            return Outcome(unary.Operand, initialized, !result, ref remaining);
        }
        if (expression is BoundBinary { Operator: TokenKind.AmpersandAmpersand } and)
        {
            var leftTrue = Outcome(and.Left, initialized, true, ref remaining);
            if (result)
            {
                return Outcome(and.Right, leftTrue, true, ref remaining);
            }
            var firstFalse = Outcome(and.Left, initialized, false, ref remaining);
            var rightFalse = Outcome(and.Right, leftTrue, false, ref remaining);
            return Combine(firstFalse, CanBe(and.Left, false, ref remaining), rightFalse,
                CanBe(and.Left, true, ref remaining) && CanBe(and.Right, false, ref remaining));
        }
        if (expression is BoundBinary { Operator: TokenKind.PipePipe } or)
        {
            var leftFalse = Outcome(or.Left, initialized, false, ref remaining);
            if (!result)
            {
                return Outcome(or.Right, leftFalse, false, ref remaining);
            }
            var firstTrue = Outcome(or.Left, initialized, true, ref remaining);
            var rightTrue = Outcome(or.Right, leftFalse, true, ref remaining);
            return Combine(firstTrue, CanBe(or.Left, true, ref remaining), rightTrue,
                CanBe(or.Left, false, ref remaining) && CanBe(or.Right, true, ref remaining));
        }
        var state = (bool[])initialized.Clone();
        if (expression is BoundIsSome pattern)
        {
            VacateNestedOutputs(pattern.Value, state, ref remaining);
            state[pattern.Variable] = result;
            return state;
        }
        if (expression is BoundCall call)
        {
            foreach (var argument in call.Arguments)
            {
                if (argument is BoundOutArgument output)
                {
                    state[output.Variable] = result;
                }
                else
                {
                    VacateNestedOutputs(argument, state, ref remaining);
                }
            }
        }
        else
        {
            VacateNestedOutputs(expression, state, ref remaining);
        }
        return state;
    }

    private static void VacateNestedOutputs(BoundExpression expression, bool[] state, ref int remaining)
    {
        Count(ref remaining, 0);
        switch (expression)
        {
            case BoundCall call:
                foreach (var argument in call.Arguments)
                {
                    if (argument is BoundOutArgument output)
                    {
                        state[output.Variable] = false;
                    }
                    else
                    {
                        VacateNestedOutputs(argument, state, ref remaining);
                    }
                }
                break;
            case BoundBinary { Operator: TokenKind.AmpersandAmpersand or TokenKind.PipePipe } logical:
                var evaluateWhenTrue = logical.Operator == TokenKind.AmpersandAmpersand;
                var evaluated = Outcome(logical.Left, state, evaluateWhenTrue, ref remaining);
                VacateNestedOutputs(logical.Right, evaluated, ref remaining);
                var skipped = Outcome(logical.Left, state, !evaluateWhenTrue, ref remaining);
                var combined = Combine(evaluated, CanBe(logical.Left, evaluateWhenTrue, ref remaining),
                    skipped, CanBe(logical.Left, !evaluateWhenTrue, ref remaining));
                Array.Copy(combined, state, state.Length);
                break;
            case BoundBinary binary:
                VacateNestedOutputs(binary.Left, state, ref remaining);
                VacateNestedOutputs(binary.Right, state, ref remaining);
                break;
            case BoundUnary unary:
                VacateNestedOutputs(unary.Operand, state, ref remaining);
                break;
            case BoundCast cast:
                VacateNestedOutputs(cast.Operand, state, ref remaining);
                break;
            case BoundOptionalLift lift:
                VacateNestedOutputs(lift.Value, state, ref remaining);
                break;
            case BoundReadonlyProjection projection:
                VacateNestedOutputs(projection.Value, state, ref remaining);
                break;
            case BoundIsNull test:
                VacateNestedOutputs(test.Value, state, ref remaining);
                break;
            case BoundIsSome pattern:
                VacateNestedOutputs(pattern.Value, state, ref remaining);
                state[pattern.Variable] = false;
                break;
            case BoundFieldRead field:
                VacateNestedOutputs(field.Receiver, state, ref remaining);
                break;
            case BoundBufferRead buffer:
                VacateNestedOutputs(buffer.Index, state, ref remaining);
                break;
            case BoundViewRead view:
                VacateNestedOutputs(view.Index, state, ref remaining);
                break;
            case BoundViewLength:
            case BoundConstant:
            case BoundNull:
            case BoundRead:
            case BoundReceiverLoan:
            case BoundMutableReceiverLoan:
            case BoundMutableFieldLoan:
            case BoundFieldLoan:
                break;
            case BoundMove move:
                if (move.Variable < 0 || move.Variable >= state.Length)
                {
                    throw InvalidEffect(move.Span, "Ownership");
                }
                state[move.Variable] = false;
                break;
            default:
                throw Invalid();
        }
    }

    private static bool CanBe(BoundExpression expression, bool result, ref int remaining)
    {
        Count(ref remaining, 0);
        return expression switch {
            BoundConstant { Type: PrimitiveType.Bool } constant => constant.Value != (result ? 0 : 1),
            BoundUnary { Operator: TokenKind.Bang } unary => CanBe(unary.Operand, !result, ref remaining),
            BoundBinary { Operator: TokenKind.AmpersandAmpersand } and => result
                ? CanBe(and.Left, true, ref remaining) && CanBe(and.Right, true, ref remaining)
                : CanBe(and.Left, false, ref remaining) || (CanBe(and.Left, true, ref remaining) && CanBe(and.Right, false, ref remaining)),
            BoundBinary { Operator: TokenKind.PipePipe } or => result
                ? CanBe(or.Left, true, ref remaining) || (CanBe(or.Left, false, ref remaining) && CanBe(or.Right, true, ref remaining))
                : CanBe(or.Left, false, ref remaining) && CanBe(or.Right, false, ref remaining),
            _ => true,
        };
    }

    private static bool[] Combine(bool[] left, bool leftFeasible, bool[] right, bool rightFeasible)
    {
        if (!leftFeasible)
        {
            return right;
        }
        if (!rightFeasible)
        {
            return left;
        }
        for (var i = 0; i < left.Length; i++)
        {
            left[i] &= right[i];
        }
        return left;
    }

    private static void Count(ref int remaining, int depth)
    {
        if (depth > MaximumDepth || --remaining < 0)
        {
            throw Limit();
        }
    }

    private static ArgumentException Invalid() => new("Invalid bound program.", "program");
    private static ArgumentException Invalid(TextSpan span)
    {
        var exception = Invalid();
        exception.Data[SourceSpanKey] = span;
        return exception;
    }
    private static ArgumentException InvalidEffect(TextSpan span, string contract = "NoThrow")
    {
        var exception = Invalid(span);
        exception.Data[InvalidEffectKey] = contract;
        return exception;
    }
    private static NotSupportedException Limit() => new("Bound program exceeds native resource limits.");
}
