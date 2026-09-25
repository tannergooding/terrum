// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Terrum.CodeAnalysis.Lexing;
using Terrum.CodeAnalysis.Text;

namespace Terrum.CodeAnalysis.Semantics;

/// <summary>Validates untrusted structured IR before a native backend consumes it.</summary>
public static class SemanticVerifier
{
    private const string FunctionIndexKey = "Terrum.FunctionIndex";
    private const string SourceSpanKey = "Terrum.SourceSpan";
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

    private static void VerifyCore(BoundProgram program, bool requireEntryPoint, bool allowExternal = false)
    {
        ArgumentNullException.ThrowIfNull(program);
        if (program.Functions.IsDefaultOrEmpty || (requireEntryPoint
            ? program.EntryPoint < 0 || program.EntryPoint >= program.Functions.Length : program.EntryPoint != -1))
        {
            throw Invalid();
        }
        if (program.Functions.Length > MaximumFunctions)
        {
            throw Limit();
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
        foreach (var structure in program.Structures)
        {
            if (structure is null || string.IsNullOrWhiteSpace(structure.Name) || !structNames.Add(structure.Name)
                || (structure.IsPrivate && structure.IsInternal)
                || structure.Fields.IsDefaultOrEmpty || structure.Size is not (1 or 2 or 4 or 8))
            {
                throw Invalid();
            }
            var offset = 0;
            var alignment = 1;
            var fieldNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (var field in structure.Fields)
            {
                if (field is null)
                {
                    throw Invalid();
                }
                var width = field.Type switch {
                    PrimitiveType.Bool => 1,
                    PrimitiveType.Char16 => 2,
                    PrimitiveType.Int32 or PrimitiveType.UInt32 => 4,
                    PrimitiveType.Void => 0,
                    _ => 0,
                };
                var alignedOffset = width == 0 ? offset : (offset + width - 1) & -width;
                if (string.IsNullOrWhiteSpace(field.Name) || !fieldNames.Add(field.Name)
                    || (field.IsPrivate && field.IsInternal)
                    || width == 0 || field.Size != width || field.Offset != alignedOffset || alignedOffset + width > 8)
                {
                    throw Invalid();
                }
                offset = alignedOffset + width;
                alignment = Math.Max(alignment, width);
            }
            if (((offset + alignment - 1) & -alignment) != structure.Size || structure.Fields.Length > 8)
            {
                throw Invalid();
            }
        }

        var names = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < program.Functions.Length; i++)
        {
            var function = program.Functions[i];
            if (function is null || string.IsNullOrEmpty(function.Name) || !names.Add(function.Name)
                || (function.IsPrivate && function.IsInternal)
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
            if (function.IsExternal && (!allowExternal || function.IsPrivate || function.IsInternal
                || function.Body.Statements.IsDefault || !function.Body.Statements.IsEmpty
                || function.Variables.Length != function.ParameterCount + (function.IsConstructor ? 1 : 0)))
            {
                throw Invalid();
            }
            for (var variableIndex = 0; variableIndex < function.Variables.Length; variableIndex++)
            {
                var variable = function.Variables[variableIndex];
                if (variable is null || string.IsNullOrEmpty(variable.Name) || !ValidType(variable.Type, allowVoid: false, program))
                {
                    throw Invalid();
                }
                if (variable.IsOut && (variableIndex >= function.ParameterCount + (function.IsConstructor ? 1 : 0)
                    || function.ReturnType != PrimitiveType.Bool))
                {
                    throw Invalid();
                }
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
                if (declaringType < 0 || !function.Name.StartsWith(declaringStruct + ".", StringComparison.Ordinal)
                    || (function.IsConstructor && function.IsStatic)
                    || (!function.IsStatic && (function.Variables.IsEmpty
                        || function.Variables[0].Name != "this"
                        || function.Variables[0].Type != (PrimitiveType)(100 + declaringType)
                        || (!function.IsConstructor && function.ParameterCount < 1))))
                {
                    throw Invalid();
                }
            }
            else if (function.IsConstructor || function.IsPrivate)
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
            var initialized = new bool[function.Variables.Length];
            if (function.IsConstructor)
            {
                Array.Fill(initialized, true, 1, function.ParameterCount);
            }
            else
            {
                Array.Fill(initialized, true, 0, function.ParameterCount);
            }
            for (var i = 0; i < function.ParameterCount; i++)
            {
                initialized[i + (function.IsConstructor ? 1 : 0)] &= !function.Variables[i + (function.IsConstructor ? 1 : 0)].IsOut;
            }
            var fieldMasks = new uint[function.Variables.Length];
            for (var i = 0; i < initialized.Length; i++)
            {
                if (initialized[i] && (int)function.Variables[i].Type >= 100)
                {
                    fieldMasks[i] = FullMask(program, function.Variables[i].Type);
                }
            }
            var declarations = new HashSet<int>();
            try
            {
                var returns = Statement(function.Body, initialized, fieldMasks, declarations, function, program, ref remaining, 0);
                if (function.ReturnType != PrimitiveType.Void && !returns)
                {
                    throw Invalid();
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

    internal static bool TryVerifySource(BoundProgram program, out int functionIndex, out TextSpan span, out bool exceededWorkLimit)
    {
        try
        {
            VerifyCore(program, requireEntryPoint: program.EntryPoint != -1, allowExternal: true);
            functionIndex = -1;
            span = default;
            exceededWorkLimit = false;
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            functionIndex = exception.Data[FunctionIndexKey] is int index ? index : Math.Max(0, program.EntryPoint);
            span = exception.Data[SourceSpanKey] is TextSpan sourceSpan
                ? sourceSpan : program.Functions.IsDefaultOrEmpty ? default : program.Functions[functionIndex].Span;
            exceededWorkLimit = exception is NotSupportedException;
            return false;
        }
    }

    private static bool Statement(BoundStatement statement, bool[] initialized, uint[] fieldMasks, HashSet<int> declarations,
        BoundFunction function, BoundProgram program, ref int remaining, int depth)
    {
        Count(ref remaining, depth);
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
                    var childReturns = Statement(child, initialized, fieldMasks, declarations, function, program, ref remaining, depth + 1);
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
                Expression(local.Initializer, initialized, fieldMasks, declarations, function, program, ref remaining, depth + 1);
                if (local.Initializer.Type != function.Variables[local.Variable].Type)
                {
                    throw Invalid();
                }
                initialized[local.Variable] = true;
                if ((int)local.Initializer.Type >= 100)
                {
                    fieldMasks[local.Variable] = FullMask(program, local.Initializer.Type);
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
                Expression(assignment.Value, initialized, fieldMasks, declarations, function, program, ref remaining, depth + 1);
                if (assignment.Value.Type != function.Variables[assignment.Variable].Type)
                {
                    throw Invalid();
                }
                if ((int)assignment.Value.Type >= 100)
                {
                    fieldMasks[assignment.Variable] = FullMask(program, assignment.Value.Type);
                }
                initialized[assignment.Variable] = true;
                return false;
            case BoundFieldAssignment fieldAssignment:
                if (fieldAssignment.Variable < 0 || fieldAssignment.Variable >= initialized.Length
                    || !function.Variables[fieldAssignment.Variable].IsMutable)
                {
                    throw Invalid();
                }
                var structType = (int)function.Variables[fieldAssignment.Variable].Type - 100;
                if (structType < 0 || structType >= program.Structures.Length
                    || fieldAssignment.Field < 0 || fieldAssignment.Field >= program.Structures[structType].Fields.Length
                    || !CanAccessField(function, program.Structures[structType], fieldAssignment.Field)
                    || (!initialized[fieldAssignment.Variable]
                        && (!function.IsConstructor || fieldAssignment.Variable != 0)))
                {
                    throw Invalid();
                }
                Expression(fieldAssignment.Value, initialized, fieldMasks, declarations, function, program, ref remaining, depth + 1);
                if (fieldAssignment.Value.Type != program.Structures[structType].Fields[fieldAssignment.Field].Type)
                {
                    throw Invalid();
                }
                fieldMasks[fieldAssignment.Variable] |= 1u << fieldAssignment.Field;
                initialized[fieldAssignment.Variable] = fieldMasks[fieldAssignment.Variable]
                    == FullMask(program, function.Variables[fieldAssignment.Variable].Type);
                return false;
            case BoundBufferAssignment bufferWrite:
                if (bufferWrite.Buffer < 0 || bufferWrite.Buffer >= program.Buffers.Length
                    || !program.Buffers[bufferWrite.Buffer].IsWritable)
                {
                    throw Invalid();
                }
                Expression(bufferWrite.Index, initialized, fieldMasks, declarations, function, program, ref remaining, depth + 1);
                Expression(bufferWrite.Value, initialized, fieldMasks, declarations, function, program, ref remaining, depth + 1);
                if (bufferWrite.Index.Type != PrimitiveType.Int32
                    || bufferWrite.Value.Type != program.Buffers[bufferWrite.Buffer].ElementType)
                {
                    throw Invalid();
                }
                return false;
            case BoundExpressionStatement expressionStatement:
                if (expressionStatement.Expression is not BoundCall)
                {
                    throw Invalid();
                }
                Expression(expressionStatement.Expression, initialized, fieldMasks, declarations, function, program, ref remaining, depth + 1);
                return false;
            case BoundIf conditional:
                var beforeCondition = (bool[])initialized.Clone();
                Expression(conditional.Condition, initialized, fieldMasks, declarations, function, program, ref remaining, depth + 1);
                if (conditional.Condition.Type != PrimitiveType.Bool || conditional.Then is null)
                {
                    throw Invalid();
                }
                var thenState = Outcome(conditional.Condition, beforeCondition, true, ref remaining);
                var thenMasks = (uint[])fieldMasks.Clone();
                MarkOutMasks(thenState, thenMasks, function, program);
                var thenReturns = Statement(conditional.Then, thenState, thenMasks, declarations, function, program, ref remaining, depth + 1);
                var elseState = Outcome(conditional.Condition, beforeCondition, false, ref remaining);
                var elseMasks = (uint[])fieldMasks.Clone();
                MarkOutMasks(elseState, elseMasks, function, program);
                var elseReturns = conditional.Else is not null
                    && Statement(conditional.Else, elseState, elseMasks, declarations, function, program, ref remaining, depth + 1);
                var thenReachable = CanBe(conditional.Condition, true, ref remaining);
                var elseReachable = CanBe(conditional.Condition, false, ref remaining);
                for (var i = 0; i < initialized.Length; i++)
                {
                    initialized[i] = !thenReachable || thenReturns ? elseState[i]
                        : !elseReachable || elseReturns ? thenState[i] : thenState[i] && elseState[i];
                    fieldMasks[i] = !thenReachable || thenReturns ? elseMasks[i]
                        : !elseReachable || elseReturns ? thenMasks[i] : thenMasks[i] & elseMasks[i];
                }
                return (!thenReachable || thenReturns) && (!elseReachable || elseReturns);
            case BoundWhile loop:
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
                    Expression(loop.Condition, conditionState, conditionMasks, iterationDeclarations, function, program, ref remaining, depth + 1);
                    if (loop.Condition.Type != PrimitiveType.Bool || loop.Body is null)
                    {
                        throw Invalid();
                    }
                    var bodyState = Outcome(loop.Condition, conditionInput, true, ref remaining);
                    var bodyMasks = (uint[])conditionMasks.Clone();
                    MarkOutMasks(bodyState, bodyMasks, function, program);
                    var bodyReturns = Statement(loop.Body, bodyState, bodyMasks, iterationDeclarations, function, program, ref remaining, depth + 1);
                    var exitState = Outcome(loop.Condition, conditionInput, false, ref remaining);
                    var exitMasks = (uint[])conditionMasks.Clone();
                    MarkOutMasks(exitState, exitMasks, function, program);
                    if (!CanBe(loop.Condition, true, ref remaining) || bodyReturns)
                    {
                        Array.Copy(exitState, initialized, initialized.Length);
                        Array.Copy(exitMasks, fieldMasks, fieldMasks.Length);
                        declarations.UnionWith(iterationDeclarations);
                        return false;
                    }
                    var changed = false;
                    for (var i = 0; i < headState.Length; i++)
                    {
                        var nextState = entryState[i] && bodyState[i];
                        var nextMask = entryMasks[i] & bodyMasks[i];
                        changed |= nextState != headState[i] || nextMask != headMasks[i];
                        headState[i] = nextState;
                        headMasks[i] = nextMask;
                    }
                    if (!changed)
                    {
                        Array.Copy(exitState, initialized, initialized.Length);
                        Array.Copy(exitMasks, fieldMasks, fieldMasks.Length);
                        declarations.UnionWith(iterationDeclarations);
                        return false;
                    }
                }
                throw Limit();
            case BoundRequire requirement:
                var beforeRequirement = (bool[])initialized.Clone();
                Expression(requirement.Condition, initialized, fieldMasks, declarations, function, program, ref remaining, depth + 1);
                if (requirement.Condition.Type != PrimitiveType.Bool)
                {
                    throw Invalid();
                }
                var accepted = Outcome(requirement.Condition, beforeRequirement, true, ref remaining);
                Array.Copy(accepted, initialized, initialized.Length);
                MarkOutMasks(initialized, fieldMasks, function, program);
                return false;
            case BoundReturn result:
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
                    Expression(result.Value, initialized, fieldMasks, declarations, function, program, ref remaining, depth + 1);
                    if (result.Value.Type != function.ReturnType)
                    {
                        throw Invalid();
                    }
                }
                if (function.ReturnType == PrimitiveType.Bool && result.Value is BoundExpression boolResult
                    && CanBe(boolResult, true, ref remaining))
                {
                    var trueState = Outcome(boolResult, beforeReturn, true, ref remaining);
                    for (var i = 0; i < function.ParameterCount; i++)
                    {
                        if (function.Variables[i].IsOut && !trueState[i])
                        {
                            throw Invalid(result.Span);
                        }
                    }
                }
                return true;
            default:
                throw Invalid();
        }
    }

    [SuppressMessage("Style", "IDE0072:Populate switch", Justification = "Unsupported token kinds must remain invalid rather than becoming accepted operators.")]
    private static void Expression(BoundExpression expression, bool[] initialized, uint[] fieldMasks,
        HashSet<int> declarations, BoundFunction function,
        BoundProgram program, ref int remaining, int depth)
    {
        Count(ref remaining, depth);
        if (expression is null || !ValidType(expression.Type, allowVoid: expression is BoundCall, program))
        {
            throw Invalid();
        }
        switch (expression)
        {
            case BoundConstant constant:
                if (constant.Type is not (PrimitiveType.Int32 or PrimitiveType.UInt32 or PrimitiveType.Char16 or PrimitiveType.Bool)
                    || (constant.Type == PrimitiveType.Bool && constant.Value is not (0 or 1))
                    || (constant.Type == PrimitiveType.Char16 && constant.Value is < 0 or > ushort.MaxValue))
                {
                    throw Invalid();
                }
                break;
            case BoundCast cast:
                Expression(cast.Operand, initialized, fieldMasks, declarations, function, program, ref remaining, depth + 1);
                if (cast.Operand.Type is not (PrimitiveType.Int32 or PrimitiveType.UInt32 or PrimitiveType.Char16)
                    || cast.Type is not (PrimitiveType.Int32 or PrimitiveType.UInt32 or PrimitiveType.Char16))
                {
                    throw Invalid();
                }
                break;
            case BoundFieldRead fieldRead:
                if (fieldRead.Receiver is null)
                {
                    throw Invalid(fieldRead.Span);
                }
                var readType = (int)fieldRead.Receiver.Type - 100;
                if (readType < 0 || readType >= program.Structures.Length
                    || fieldRead.Field < 0 || fieldRead.Field >= program.Structures[readType].Fields.Length
                    || !CanAccessField(function, program.Structures[readType], fieldRead.Field)
                    || fieldRead.Type != program.Structures[readType].Fields[fieldRead.Field].Type)
                {
                    throw Invalid();
                }
                if (fieldRead.Receiver is BoundRead partial && function.IsConstructor && partial.Variable == 0)
                {
                    if (partial.Type != function.Variables[0].Type || (fieldMasks[0] & (1u << fieldRead.Field)) == 0)
                    {
                        throw Invalid(fieldRead.Span);
                    }
                }
                else
                {
                    Expression(fieldRead.Receiver, initialized, fieldMasks, declarations, function, program, ref remaining, depth + 1);
                }
                break;
            case BoundRead read:
                if (read.Variable < 0 || read.Variable >= initialized.Length || !initialized[read.Variable]
                    || read.Type != function.Variables[read.Variable].Type)
                {
                    throw Invalid(read.Span);
                }
                break;
            case BoundOutArgument:
                throw Invalid();
            case BoundBufferRead bufferRead:
                if (bufferRead.Buffer < 0 || bufferRead.Buffer >= program.Buffers.Length
                    || bufferRead.Type != program.Buffers[bufferRead.Buffer].ElementType)
                {
                    throw Invalid();
                }
                Expression(bufferRead.Index, initialized, fieldMasks, declarations, function, program, ref remaining, depth + 1);
                if (bufferRead.Index.Type != PrimitiveType.Int32)
                {
                    throw Invalid();
                }
                break;
            case BoundUnary unary:
                Expression(unary.Operand, initialized, fieldMasks, declarations, function, program, ref remaining, depth + 1);
                var unaryValid = unary.Operator switch {
                    TokenKind.Plus or TokenKind.Tilde =>
                        unary.Operand.Type is PrimitiveType.Int32 or PrimitiveType.UInt32 && unary.Type == unary.Operand.Type,
                    TokenKind.Minus => unary.Operand.Type == PrimitiveType.Int32 && unary.Type == PrimitiveType.Int32,
                    TokenKind.Bang => unary.Operand.Type == PrimitiveType.Bool && unary.Type == PrimitiveType.Bool,
                    _ => false,
                };
                if (!unaryValid)
                {
                    throw Invalid();
                }
                break;
            case BoundBinary binary:
                var beforeLeft = (bool[])initialized.Clone();
                Expression(binary.Left, initialized, fieldMasks, declarations, function, program, ref remaining, depth + 1);
                if (binary.Operator is TokenKind.AmpersandAmpersand or TokenKind.PipePipe)
                {
                    var evaluateWhenTrue = binary.Operator == TokenKind.AmpersandAmpersand;
                    var rightState = Outcome(binary.Left, beforeLeft, evaluateWhenTrue, ref remaining);
                    var rightMasks = (uint[])fieldMasks.Clone();
                    MarkOutMasks(rightState, rightMasks, function, program);
                    Expression(binary.Right, rightState, rightMasks, declarations, function, program, ref remaining, depth + 1);
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
                    Expression(binary.Right, initialized, fieldMasks, declarations, function, program, ref remaining, depth + 1);
                }
                var operands = binary.Left.Type == binary.Right.Type && binary.Left.Type != PrimitiveType.Void;
                var binaryValid = binary.Operator switch {
                    TokenKind.Plus or TokenKind.Minus or TokenKind.Star or TokenKind.Slash or TokenKind.Percent =>
                        operands && binary.Left.Type is PrimitiveType.Int32 or PrimitiveType.UInt32
                            && binary.Type == binary.Left.Type,
                    TokenKind.Less or TokenKind.LessEquals or TokenKind.Greater or TokenKind.GreaterEquals =>
                        operands && binary.Left.Type is PrimitiveType.Int32 or PrimitiveType.UInt32 or PrimitiveType.Char16
                            && binary.Type == PrimitiveType.Bool,
                    TokenKind.EqualsEquals or TokenKind.BangEquals =>
                        operands && binary.Left.Type is PrimitiveType.Int32 or PrimitiveType.UInt32 or PrimitiveType.Char16 or PrimitiveType.Bool
                            && binary.Type == PrimitiveType.Bool,
                    TokenKind.Ampersand or TokenKind.Pipe or TokenKind.Caret =>
                        operands && binary.Left.Type is PrimitiveType.Int32 or PrimitiveType.UInt32 or PrimitiveType.Bool
                            && binary.Type == binary.Left.Type,
                    TokenKind.AmpersandAmpersand or TokenKind.PipePipe =>
                        operands && binary.Left.Type == PrimitiveType.Bool && binary.Type == PrimitiveType.Bool,
                    _ => false,
                };
                if (!binaryValid)
                {
                    throw Invalid();
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
                if (call.Arguments.Length != target.ParameterCount || call.Type != target.ReturnType)
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
                for (var i = 0; i < call.Arguments.Length; i++)
                {
                    var argument = call.Arguments[i];
                    if (argument is BoundOutArgument output)
                    {
                        if (output.Variable < function.ParameterCount || output.Variable >= initialized.Length
                            || output.Type != function.Variables[output.Variable].Type
                            || !outTargets.Add(output.Variable)
                            || (output.DeclaresVariable && (!declarations.Add(output.Variable) || initialized[output.Variable]))
                            || (!output.DeclaresVariable && !function.Variables[output.Variable].IsMutable))
                        {
                            throw Invalid();
                        }
                        initialized[output.Variable] = false;
                        fieldMasks[output.Variable] = 0;
                    }
                    else
                    {
                        Expression(argument, initialized, fieldMasks, declarations, function, program, ref remaining, depth + 1);
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
                break;
            default:
                throw Invalid();
        }
    }

    private static bool ValidType(PrimitiveType type, bool allowVoid, BoundProgram program) =>
        type is PrimitiveType.Int32 or PrimitiveType.UInt32 or PrimitiveType.Char16 or PrimitiveType.Bool
            || (allowVoid && type == PrimitiveType.Void)
            || ((int)type >= 100 && (int)type - 100 < program.Structures.Length);

    private static uint FullMask(BoundProgram program, PrimitiveType type) =>
        (1u << program.Structures[(int)type - 100].Fields.Length) - 1;

    private static bool CanAccessField(BoundFunction function, BoundStruct structure, int field) =>
        !structure.Fields[field].IsPrivate || function.DeclaringStruct == structure.Name;

    private static void MarkOutMasks(bool[] initialized, uint[] masks, BoundFunction function, BoundProgram program)
    {
        for (var i = 0; i < initialized.Length; i++)
        {
            var type = function.Variables[i].Type;
            if (initialized[i] && (int)type >= 100)
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
            case BoundFieldRead field:
                VacateNestedOutputs(field.Receiver, state, ref remaining);
                break;
            case BoundBufferRead buffer:
                VacateNestedOutputs(buffer.Index, state, ref remaining);
                break;
            case BoundConstant:
            case BoundRead:
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
    private static NotSupportedException Limit() => new("Bound program exceeds native resource limits.");
}
