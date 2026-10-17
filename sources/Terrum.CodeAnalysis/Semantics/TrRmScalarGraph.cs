// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Terrum.CodeAnalysis.Lexing;
using Terrum.CodeAnalysis.Text;

namespace Terrum.CodeAnalysis.Semantics;

internal sealed record TrRmScalarEdge(int Target, ImmutableArray<int> Arguments);

internal sealed record TrRmScalarSwitchCase(ulong Bits, TrRmScalarEdge Edge);

/// <summary>An audited, equal-width integer bit reinterpretation; never a structured source cast.</summary>
internal sealed record TrRmScalarReinterpret(BoundExpression Operand, PrimitiveType Type)
    : BoundExpression(Type, default);

internal sealed record TrRmScalarConvert(BoundExpression Operand, PrimitiveType Type, byte Policy)
    : BoundExpression(Type, default);

internal sealed record TrRmScalarWrappingBinary(TokenKind Operator, BoundRead Left,
    BoundRead Right, PrimitiveType Type) : BoundExpression(Type, default);

internal sealed record TrRmScalarOptionalNone(PrimitiveType Type) : BoundExpression(Type, default);

internal sealed record TrRmScalarOptionalLift(BoundExpression Value, PrimitiveType Type)
    : BoundExpression(Type, default);

internal sealed record TrRmScalarOptionalPresence(BoundExpression Optional)
    : BoundExpression(PrimitiveType.Bool, default);

internal sealed record TrRmScalarOptionalEquality(BoundRead Left, BoundRead Right,
    PrimitiveType PayloadType, bool Negated) : BoundExpression(PrimitiveType.Bool, default);

internal sealed record TrRmScalarDivRem(int QuotientVariable, int RemainderVariable,
    BoundRead Dividend, BoundRead Divisor, PrimitiveType Type) : BoundStatement(default(TextSpan));

internal static class TrRmOptionalScalar
{
    private static readonly PrimitiveType[] s_payloadTypes =
    [
        PrimitiveType.Bool, PrimitiveType.Char16, PrimitiveType.Int32,
        PrimitiveType.UInt32, PrimitiveType.Int64, PrimitiveType.UInt64,
    ];

    internal static ImmutableArray<BoundStruct> Structures { get; } = CreateStructures();

    internal static bool IsOptionalType(PrimitiveType type)
        => (int)type is >= 100 and < 106;

    internal static PrimitiveType StorageType(PrimitiveType payloadType)
    {
        var index = Array.IndexOf(s_payloadTypes, payloadType);
        return index >= 0 ? (PrimitiveType)(100 + index)
            : throw new ArgumentOutOfRangeException(nameof(payloadType));
    }

    internal static PrimitiveType PayloadType(PrimitiveType optionalType)
        => IsOptionalType(optionalType)
            ? s_payloadTypes[(int)optionalType - 100]
            : throw new ArgumentOutOfRangeException(nameof(optionalType));

    private static ImmutableArray<BoundStruct> CreateStructures()
    {
        var structures = ImmutableArray.CreateBuilder<BoundStruct>(s_payloadTypes.Length);
        foreach (var payloadType in s_payloadTypes)
        {
            var payloadSize = ValueLayout.Size(payloadType, []);
            var alignment = payloadSize;
            var size = (payloadSize + 1 + alignment - 1) & -alignment;
            structures.Add(new BoundStruct($"$trrm.Optional<{payloadType}>",
                [
                    new BoundStructField("Payload", payloadType, 0, payloadSize),
                    new BoundStructField("Present", PrimitiveType.Bool, payloadSize, 1),
                ], size)
            {
                IsInternal = true,
            });
        }
        return structures.MoveToImmutable();
    }
}

internal sealed record TrRmScalarBlock(ImmutableArray<int> Parameters,
    ImmutableArray<BoundStatement> Operations, int Condition, TrRmScalarEdge? True,
    TrRmScalarEdge? False, int ReturnValue, int FailureReason, int SwitchValue = 0,
    ImmutableArray<TrRmScalarSwitchCase> SwitchCases = default,
    TrRmScalarEdge? SwitchDefault = null, bool IsCallerUnsafeScope = false);

/// <summary>Immutable, source-free scalar SSA body admitted only after compact Body validation.</summary>
internal sealed class TrRmScalarGraph
{
    internal ImmutableArray<TrRmScalarBlock> Blocks { get; }
    internal ImmutableArray<PrimitiveType> Types { get; }
    internal int ParameterCount { get; }
    internal PrimitiveType ResultType { get; }
    internal bool NoThrow { get; }
    internal bool DoesNotReturn { get; }

    private TrRmScalarGraph(ImmutableArray<TrRmScalarBlock> blocks,
        ImmutableArray<PrimitiveType> types, int parameterCount,
        PrimitiveType resultType, bool noThrow, bool doesNotReturn)
    {
        Blocks = blocks;
        Types = types;
        ParameterCount = parameterCount;
        ResultType = resultType;
        NoThrow = noThrow;
        DoesNotReturn = doesNotReturn;
    }

    internal static TrRmScalarGraph FromAuditedBody(ImmutableArray<TrRmScalarBlock> blocks,
        ImmutableArray<PrimitiveType> types, int parameterCount,
        PrimitiveType resultType, bool noThrow, bool doesNotReturn)
        => new(blocks, types, parameterCount, resultType, noThrow, doesNotReturn);

    internal void Verify(BoundProgram program, BoundFunction function, ref int remaining)
    {
        if (program.RuntimePolicy != BoundRuntimePolicy.FailFast || function.IsExternal
            || function.DoesNotReturn != DoesNotReturn || function.IsConstructor || function.IsClassConstructor
            || function.ClassConstructor != -1 || function.DeclaringStruct is not null
            || function.IsStatic || function.IsCleanup || function.PropertyName is not null
            || function.Intrinsic != BoundIntrinsic.None
            || function.ParameterCount != ParameterCount || function.ReturnType != ResultType
            || function.IsNoThrow != NoThrow || function.Variables.Length != Types.Length
            || Blocks.IsDefaultOrEmpty || Blocks.Length > SemanticVerifier.MaximumAnalysisVisits
            || Types.Length > SemanticVerifier.MaximumVariables)
        {
            throw new ArgumentException("Indexed scalar Body does not match its declaration.");
        }
        for (var value = 0; value < Types.Length; value++)
        {
            if (function.Variables[value].Type != Types[value]
                || function.Variables[value].IsOut || function.Variables[value].IsInit
                || Types[value] == PrimitiveType.Void)
            {
                throw new ArgumentException("Indexed scalar value inventory does not match its declaration.");
            }
        }
        if (function.Body.Statements.IsDefault || !function.Body.Statements.IsEmpty
            || function.Body.IsManagedLexicalScope || !function.Body.RetiredManagedLocals.IsDefaultOrEmpty)
        {
            throw new ArgumentException("Indexed scalar Body cannot also contain structured statements.");
        }
        foreach (var block in Blocks)
        {
            Visit(ref remaining);
            for (var operationIndex = 0; operationIndex < block.Operations.Length; operationIndex++)
            {
                var operation = block.Operations[operationIndex];
                Visit(ref remaining);
                var call = operation switch
                {
                    BoundLocal { Initializer: BoundCall value } => value,
                    BoundExpressionStatement { Expression: BoundCall value } => value,
                    _ => null,
                };
                if (operation is BoundLocal
                    { Initializer: TrRmScalarWrappingBinary wrapping } local
                    && (!IsInteger(wrapping.Type)
                        || wrapping.Operator is not (TokenKind.Plus or TokenKind.Minus or TokenKind.Star)
                        || local.Variable < ParameterCount || local.Variable >= Types.Length
                        || Types[local.Variable] != wrapping.Type
                        || (uint)wrapping.Left.Variable >= (uint)Types.Length
                        || (uint)wrapping.Right.Variable >= (uint)Types.Length
                        || Types[wrapping.Left.Variable] != wrapping.Type
                        || Types[wrapping.Right.Variable] != wrapping.Type))
                {
                    throw new ArgumentException("Indexed wrapping Binary result or operand contract does not match.");
                }
                if (operation is BoundLocal { Initializer: var initializer } optionalLocal
                    && (initializer is TrRmScalarOptionalNone or TrRmScalarOptionalLift
                        or TrRmScalarOptionalPresence or TrRmScalarOptionalEquality
                        || initializer is BoundConditional conditional
                            && TrRmOptionalScalar.IsOptionalType(conditional.Type))
                    && (optionalLocal.Variable < ParameterCount || optionalLocal.Variable >= Types.Length
                        || Types[optionalLocal.Variable] != initializer.Type
                        || !ValidOptionalExpression(initializer, Types)))
                {
                    throw new ArgumentException("Indexed optional scalar operation does not match its value types.");
                }
                if (operation is TrRmScalarDivRem divRem
                    && (NoThrow || !IsInteger(divRem.Type)
                        || divRem.QuotientVariable < 0 || divRem.QuotientVariable >= Types.Length
                        || divRem.RemainderVariable < 0 || divRem.RemainderVariable >= Types.Length
                        || divRem.QuotientVariable == divRem.RemainderVariable
                        || Types[divRem.QuotientVariable] != divRem.Type
                        || Types[divRem.RemainderVariable] != divRem.Type
                        || (uint)divRem.Dividend.Variable >= (uint)Types.Length
                        || (uint)divRem.Divisor.Variable >= (uint)Types.Length
                        || Types[divRem.Dividend.Variable] != divRem.Type
                        || Types[divRem.Divisor.Variable] != divRem.Type))
                {
                    throw new ArgumentException("Indexed CheckedDivRem result or operand contract does not match.");
                }
                var nativeAddress = operation switch
                {
                    BoundLocal { Initializer: BoundNativeFunctionAddress value } => value,
                    BoundExpressionStatement { Expression: BoundNativeFunctionAddress value } => value,
                    _ => null,
                };
                if (nativeAddress is not null)
                {
                    if (!nativeAddress.IsUnsafeAuthorized || !block.IsCallerUnsafeScope
                        || (uint)nativeAddress.Function >= (uint)program.Functions.Length
                        || !NativeFunctionPointerContract.MatchesFunction(
                            program.Functions[nativeAddress.Function], nativeAddress.Type,
                            program.NativeFunctionPointerSignatures)
                        || (operation is BoundLocal addressLocal
                            && (addressLocal.Variable < ParameterCount
                                || addressLocal.Variable >= Types.Length
                                || Types[addressLocal.Variable] != nativeAddress.Type)))
                    {
                        throw new ArgumentException("Indexed native function address violates its callable or unsafe-scope contract.");
                    }
                }
                var indirectCall = operation switch
                {
                    BoundLocal { Initializer: BoundIndirectCall value } => value,
                    BoundExpressionStatement { Expression: BoundIndirectCall value } => value,
                    _ => null,
                };
                if (indirectCall is not null)
                {
                    if (!indirectCall.IsUnsafeAuthorized || !block.IsCallerUnsafeScope
                        || indirectCall.Arguments.IsDefault
                        || !NativeFunctionPointerTypes.IsType(indirectCall.Target.Type)
                        || !NativeFunctionPointerContract.IsValidType(
                            indirectCall.Target.Type, program.NativeFunctionPointerSignatures)
                        || !GraphReadMatches(indirectCall.Target, indirectCall.Target.Type, Types)
                        || (operation is BoundLocal indirectLocal
                            && (indirectLocal.Variable < ParameterCount
                                || indirectLocal.Variable >= Types.Length
                                || Types[indirectLocal.Variable] != indirectCall.Type)))
                    {
                        throw new ArgumentException("Indexed indirect call violates its function-pointer or unsafe-scope contract.");
                    }

                    var signature = program.NativeFunctionPointerSignatures[
                        NativeFunctionPointerTypes.Index(indirectCall.Target.Type)];
                    if (indirectCall.Type != signature.ReturnType
                        || indirectCall.Arguments.Length != signature.ParameterTypes.Length)
                    {
                        throw new ArgumentException("Indexed indirect call signature does not match.");
                    }
                    for (var argument = 0; argument < indirectCall.Arguments.Length; argument++)
                    {
                        Visit(ref remaining);
                        if (indirectCall.Arguments[argument].Type != signature.ParameterTypes[argument]
                            || !GraphReadMatches(indirectCall.Arguments[argument],
                                signature.ParameterTypes[argument], Types))
                        {
                            throw new ArgumentException("Indexed indirect call argument does not match its signature.");
                        }
                    }
                }
                if (call is null)
                {
                    continue;
                }
                var hasNonPositionalArguments = false;
                if (!call.ArgumentParameterIndices.IsDefault)
                {
                    for (var index = 0; index < call.ArgumentParameterIndices.Length; index++)
                    {
                        if (call.ArgumentParameterIndices[index] != index)
                        {
                            hasNonPositionalArguments = true;
                            break;
                        }
                    }
                }
                if (hasNonPositionalArguments)
                {
                    throw new ArgumentException("Indexed calls require source-order and parameter-order arguments to match.");
                }
                if ((uint)call.Function >= (uint)program.Functions.Length)
                {
                    throw new ArgumentException("Indexed call target is outside the module.");
                }
                var target = program.Functions[call.Function];
                var isDirectNativeImport = target?.IsExternal == true
                    && target.DirectNativeImport is not null
                    && DirectNativeImportContract.IsValid(target, program.Structures,
                        program.NativeFunctionPointerSignatures, program.NativePointerTypes);
                if (target is null || target.Variables.IsDefault
                    || target.Variables.Length < call.Arguments.Length
                    || (target.IsExternal && !isDirectNativeImport) || target.IsConstructor
                    || target.IsClassConstructor || target.ClassConstructor != -1
                    || target.DeclaringStruct is not null || target.IsStatic || target.IsCleanup
                    || target.PropertyName is not null
                    || (target.Intrinsic != BoundIntrinsic.None
                        && (target.Intrinsic != BoundIntrinsic.NativeVTableCall
                            || target.NativeVTableMethod is null))
                    || target.RequiresUnsafeCaller != call.IsUnsafeAuthorized
                    || target.RequiresUnsafeCaller && !block.IsCallerUnsafeScope
                    || target.ReturnType != call.Type
                    || target.ParameterCount != call.Arguments.Length
                    || function.IsNoThrow && !target.IsNoThrow)
                {
                    throw new ArgumentException("Indexed call target declaration does not match.");
                }
                if (target.DoesNotReturn
                    && (operationIndex != block.Operations.Length - 1
                        || operation is not BoundExpressionStatement
                        || block.Condition != 0 || block.True is not null || block.False is not null
                        || block.SwitchValue != 0
                        || block.FailureReason != 1))
                {
                    throw new ArgumentException("A non-returning call must terminate its indexed block.");
                }
                for (var index = 0; index < call.Arguments.Length; index++)
                {
                    Visit(ref remaining);
                    var parameter = target.Variables[index];
                    var output = isDirectNativeImport
                        ? DirectNativeImportContract.OutputForParameter(target, index)
                        : null;
                    var nativeParameter = isDirectNativeImport
                        ? DirectNativeImportContract.ParameterForParameter(target, index)
                        : null;
                    var isMatchingNativeAddress = call.Arguments[index] is BoundNativeStorageAddress address
                        && ((output is not null
                            && DirectNativeImportContract.MatchesAddress(address, output, function, program))
                            || (nativeParameter is not null
                                && DirectNativeImportContract.MatchesAddress(address, nativeParameter,
                                    function, program)));
                    if (parameter.IsInit || (parameter.IsOut && output is null)
                        || (parameter.Type != call.Arguments[index].Type && !isMatchingNativeAddress))
                    {
                        throw new ArgumentException("Indexed call argument declaration does not match.");
                    }
                }
            }
            var hasSwitch = block.SwitchValue != 0;
            if (block.FailureReason is < 0 or > 4
                || (block.FailureReason != 0 && (block.Condition != 0
                    || block.True is not null || block.False is not null || hasSwitch))
                || (hasSwitch && (block.Condition != 0 || block.True is not null
                    || block.False is not null || block.FailureReason != 0
                    || (uint)(block.SwitchValue - 1) >= (uint)Types.Length
                    || !IsSwitchType(Types[block.SwitchValue - 1])
                    || block.SwitchCases.IsDefault || block.SwitchDefault is null))
                || (!hasSwitch && (!block.SwitchCases.IsDefault || block.SwitchDefault is not null))
                || (NoThrow && block.FailureReason != 0
                    && !HasTerminalDoesNotReturnCall(block, program)))
            {
                throw new ArgumentException("Indexed terminator violates the scalar graph contract.");
            }
            if (hasSwitch)
            {
                var selectorType = Types[block.SwitchValue - 1];
                ulong previous = 0;
                for (var caseIndex = 0; caseIndex < block.SwitchCases.Length; caseIndex++)
                {
                    Visit(ref remaining);
                    var switchCase = block.SwitchCases[caseIndex];
                    if (!FitsSwitchBits(selectorType, switchCase.Bits)
                        || caseIndex != 0 && switchCase.Bits <= previous)
                    {
                        throw new ArgumentException("Indexed switch cases are not canonical for their selector.");
                    }
                    previous = switchCase.Bits;
                }
            }
        }
        if (DoesNotReturn)
        {
            VerifyDoesNotReturn(program, ref remaining);
        }
    }

    private void VerifyDoesNotReturn(BoundProgram program, ref int remaining)
    {
        var reachable = new bool[Blocks.Length];
        var queued = new bool[Blocks.Length];
        var hasIncoming = new bool[Types.Length + 1];
        var booleanConstants = new bool?[Types.Length + 1];
        var pending = new Queue<int>();
        reachable[0] = true;
        queued[0] = true;
        pending.Enqueue(0);
        while (pending.TryDequeue(out var index))
        {
            queued[index] = false;
            Visit(ref remaining);
            var block = Blocks[index];
            foreach (var operation in block.Operations)
            {
                Visit(ref remaining);
                if (operation is BoundLocal { Initializer: var initializer } local
                    && local.Variable >= 0 && local.Variable < Types.Length)
                {
                    booleanConstants[local.Variable + 1] =
                        EvaluateBoolean(initializer, booleanConstants);
                }
            }
            if (block.FailureReason != 0 || HasTerminalDoesNotReturnCall(block, program))
            {
                continue;
            }
            if (block.SwitchValue != 0)
            {
                foreach (var switchCase in block.SwitchCases)
                {
                    Enqueue(switchCase.Edge, ref remaining);
                }
                Enqueue(block.SwitchDefault, ref remaining);
            }
            else if (block.Condition != 0)
            {
                if (block.Condition < 1 || block.Condition >= booleanConstants.Length)
                {
                    throw new ArgumentException("Indexed branch condition is outside the value inventory.");
                }
                var constant = block.Condition < booleanConstants.Length
                    ? booleanConstants[block.Condition] : null;
                if (constant is not false)
                {
                    Enqueue(block.True, ref remaining);
                }
                if (constant is not true)
                {
                    Enqueue(block.False, ref remaining);
                }
            }
            else if (block.True is not null)
            {
                Enqueue(block.True, ref remaining);
            }
            else
            {
                if (DoesNotReturn)
                {
                    throw new ArgumentException("DoesNotReturn body has a reachable normal return.");
                }
            }
        }

        void Enqueue(TrRmScalarEdge? edge, ref int remaining)
        {
            if (edge is null || edge.Target < 1 || edge.Target > Blocks.Length)
            {
                throw new ArgumentException("Indexed control-flow edge is outside the Body.");
            }
            var target = edge.Target - 1;
            var targetParameters = Blocks[target].Parameters;
            if (targetParameters.Length != edge.Arguments.Length)
            {
                throw new ArgumentException("Indexed edge argument count does not match its target.");
            }
            var changed = false;
            for (var parameterIndex = 0; parameterIndex < targetParameters.Length; parameterIndex++)
            {
                Visit(ref remaining);
                var parameter = targetParameters[parameterIndex];
                if (parameter < 1 || parameter >= booleanConstants.Length)
                {
                    throw new ArgumentException("Indexed block parameter is outside the value inventory.");
                }
                var argument = edge.Arguments[parameterIndex];
                var constant = argument >= 1 && argument < booleanConstants.Length
                    ? booleanConstants[argument] : null;
                if (!hasIncoming[parameter])
                {
                    hasIncoming[parameter] = true;
                    booleanConstants[parameter] = constant;
                    changed |= constant is not null;
                }
                else if (booleanConstants[parameter] is bool previous && previous != constant)
                {
                    booleanConstants[parameter] = null;
                    changed = true;
                }
            }
            if (!reachable[target])
            {
                reachable[target] = true;
                changed = true;
            }
            if (changed && !queued[target])
            {
                queued[target] = true;
                pending.Enqueue(target);
            }
        }
    }

    private static bool GraphReadMatches(BoundExpression expression, PrimitiveType type,
        ImmutableArray<PrimitiveType> types)
        => expression is BoundRead read
            && (uint)read.Variable < (uint)types.Length
            && read.Type == type
            && types[read.Variable] == type;

    private static bool HasTerminalDoesNotReturnCall(TrRmScalarBlock block, BoundProgram program)
    {
        if (block.Operations.IsDefaultOrEmpty
            || block.Operations[^1] is not BoundExpressionStatement { Expression: BoundCall call }
            || (uint)call.Function >= (uint)program.Functions.Length)
        {
            return false;
        }

        var target = program.Functions[call.Function];
        return target is not null && target.DoesNotReturn;
    }

    private static bool? EvaluateBoolean(BoundExpression expression, bool?[] known)
    {
        switch (expression)
        {
            case BoundConstant { Type: PrimitiveType.Bool } constant:
                return constant.Value != 0;
            case BoundRead read when read.Variable >= 0 && read.Variable + 1 < known.Length:
                return known[read.Variable + 1];
            case BoundConditional conditional:
                var condition = EvaluateBoolean(conditional.Condition, known);
                if (condition is bool selected)
                {
                    return EvaluateBoolean(selected ? conditional.WhenTrue : conditional.WhenFalse, known);
                }
                var whenTrue = EvaluateBoolean(conditional.WhenTrue, known);
                var whenFalse = EvaluateBoolean(conditional.WhenFalse, known);
                return whenTrue == whenFalse ? whenTrue : null;
            case BoundUnary { Operator: TokenKind.Bang } unary:
                return EvaluateBoolean(unary.Operand, known) is bool operand ? !operand : null;
            case BoundBinary { Operator: TokenKind.AmpersandAmpersand } andBinary:
                var leftAnd = EvaluateBoolean(andBinary.Left, known);
                var rightAnd = EvaluateBoolean(andBinary.Right, known);
                return leftAnd == false || rightAnd == false ? false
                    : leftAnd == true && rightAnd == true ? true : null;
            case BoundBinary { Operator: TokenKind.PipePipe } orBinary:
                var leftOr = EvaluateBoolean(orBinary.Left, known);
                var rightOr = EvaluateBoolean(orBinary.Right, known);
                return leftOr == true || rightOr == true ? true
                    : leftOr == false && rightOr == false ? false : null;
            case BoundBinary { Operator: TokenKind.EqualsEquals } equalBinary:
                var leftEqual = EvaluateBoolean(equalBinary.Left, known);
                var rightEqual = EvaluateBoolean(equalBinary.Right, known);
                return leftEqual is bool leftEqualValue && rightEqual is bool rightEqualValue
                    ? leftEqualValue == rightEqualValue : null;
            case BoundBinary { Operator: TokenKind.BangEquals } unequalBinary:
                var leftUnequal = EvaluateBoolean(unequalBinary.Left, known);
                var rightUnequal = EvaluateBoolean(unequalBinary.Right, known);
                return leftUnequal is bool leftUnequalValue && rightUnequal is bool rightUnequalValue
                    ? leftUnequalValue != rightUnequalValue : null;
            default:
                return null;
        }
    }

    private static bool IsInteger(PrimitiveType type)
        => type is PrimitiveType.Int32 or PrimitiveType.UInt32
            or PrimitiveType.Int64 or PrimitiveType.UInt64;

    private static bool ValidOptionalExpression(BoundExpression expression,
        ImmutableArray<PrimitiveType> types)
    {
        return expression switch
        {
            TrRmScalarOptionalNone none => TrRmOptionalScalar.IsOptionalType(none.Type),
            TrRmScalarOptionalLift lift => TrRmOptionalScalar.IsOptionalType(lift.Type)
                && IsReadOfType(lift.Value, TrRmOptionalScalar.PayloadType(lift.Type), types),
            TrRmScalarOptionalPresence presence => IsOptionalValue(presence.Optional, types),
            TrRmScalarOptionalEquality equality => equality.PayloadType == PrimitiveType.Char16
                && IsOptionalRead(equality.Left, equality.PayloadType, types)
                && IsOptionalRead(equality.Right, equality.PayloadType, types),
            BoundConditional conditional => conditional.Condition.Type == PrimitiveType.Bool
                && IsReadOfType(conditional.Condition, PrimitiveType.Bool, types)
                && IsOptionalValue(conditional.WhenTrue, types)
                && IsOptionalValue(conditional.WhenFalse, types)
                && conditional.WhenTrue.Type == conditional.Type
                && conditional.WhenFalse.Type == conditional.Type
                && TrRmOptionalScalar.IsOptionalType(conditional.Type),
            _ => false,
        };
    }

    private static bool IsOptionalValue(BoundExpression expression,
        ImmutableArray<PrimitiveType> types)
        => expression is BoundRead read && TrRmOptionalScalar.IsOptionalType(read.Type)
                && IsOptionalRead(read,
                TrRmOptionalScalar.PayloadType(read.Type), types)
            || expression is TrRmScalarOptionalNone none
                && TrRmOptionalScalar.IsOptionalType(none.Type)
            || expression is TrRmScalarOptionalLift lift
                && TrRmOptionalScalar.IsOptionalType(lift.Type)
                && IsReadOfType(lift.Value, TrRmOptionalScalar.PayloadType(lift.Type), types)
            || expression is BoundConditional conditional
                && ValidOptionalExpression(conditional, types);

    private static bool IsReadOfType(BoundExpression expression, PrimitiveType type,
        ImmutableArray<PrimitiveType> types)
        => expression is BoundRead read
            && (uint)read.Variable < (uint)types.Length
            && read.Type == type && types[read.Variable] == type;

    private static bool IsOptionalRead(BoundRead read, PrimitiveType payloadType,
        ImmutableArray<PrimitiveType> types)
        => (uint)read.Variable < (uint)types.Length
            && types[read.Variable] == read.Type
            && TrRmOptionalScalar.IsOptionalType(read.Type)
            && TrRmOptionalScalar.PayloadType(read.Type) == payloadType;

    private static bool IsSwitchType(PrimitiveType type)
        => type is PrimitiveType.Bool or PrimitiveType.Char16 || IsInteger(type);

    private static bool FitsSwitchBits(PrimitiveType type, ulong bits)
        => type switch
        {
            PrimitiveType.Bool => bits <= 1,
            PrimitiveType.Char16 => bits <= ushort.MaxValue,
            PrimitiveType.Int32 or PrimitiveType.UInt32 => bits <= uint.MaxValue,
            _ => IsInteger(type),
        };

    private static void Visit(ref int remaining)
    {
        if (--remaining < 0)
        {
            throw new NotSupportedException("Scalar graph exceeds the native verification visit limit.");
        }
    }
}
