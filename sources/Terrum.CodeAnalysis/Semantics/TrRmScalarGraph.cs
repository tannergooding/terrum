// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Immutable;

namespace Terrum.CodeAnalysis.Semantics;

internal sealed record TrRmScalarEdge(int Target, ImmutableArray<int> Arguments);

/// <summary>An audited, equal-width integer bit reinterpretation; never a structured source cast.</summary>
internal sealed record TrRmScalarReinterpret(BoundExpression Operand, PrimitiveType Type)
    : BoundExpression(Type, default);

internal sealed record TrRmScalarBlock(ImmutableArray<int> Parameters,
    ImmutableArray<BoundStatement> Operations, int Condition, TrRmScalarEdge? True,
    TrRmScalarEdge? False, int ReturnValue);

/// <summary>Immutable, source-free scalar SSA body admitted only after compact Body validation.</summary>
internal sealed class TrRmScalarGraph
{
    internal ImmutableArray<TrRmScalarBlock> Blocks { get; }
    internal ImmutableArray<PrimitiveType> Types { get; }
    internal int ParameterCount { get; }
    internal PrimitiveType ResultType { get; }
    internal bool NoThrow { get; }

    private TrRmScalarGraph(ImmutableArray<TrRmScalarBlock> blocks,
        ImmutableArray<PrimitiveType> types, int parameterCount,
        PrimitiveType resultType, bool noThrow)
    {
        Blocks = blocks;
        Types = types;
        ParameterCount = parameterCount;
        ResultType = resultType;
        NoThrow = noThrow;
    }

    internal static TrRmScalarGraph FromAuditedBody(ImmutableArray<TrRmScalarBlock> blocks,
        ImmutableArray<PrimitiveType> types, int parameterCount,
        PrimitiveType resultType, bool noThrow)
        => new(blocks, types, parameterCount, resultType, noThrow);

    internal void Verify(BoundProgram program, BoundFunction function, ref int remaining)
    {
        if (program.RuntimePolicy != BoundRuntimePolicy.FailFast || function.IsExternal
            || function.DoesNotReturn || function.IsConstructor || function.IsClassConstructor
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
            foreach (var operation in block.Operations)
            {
                Visit(ref remaining);
                var call = operation switch
                {
                    BoundLocal { Initializer: BoundCall value } => value,
                    BoundExpressionStatement { Expression: BoundCall value } => value,
                    _ => null,
                };
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
                if (target is null || target.Variables.IsDefault
                    || target.Variables.Length < call.Arguments.Length
                    || target.IsExternal || target.DoesNotReturn || target.IsConstructor
                    || target.IsClassConstructor || target.ClassConstructor != -1
                    || target.DeclaringStruct is not null || target.IsStatic || target.IsCleanup
                    || target.PropertyName is not null || target.Intrinsic != BoundIntrinsic.None
                    || target.ReturnType != call.Type || target.ParameterCount != call.Arguments.Length
                    || function.IsNoThrow && !target.IsNoThrow)
                {
                    throw new ArgumentException("Indexed call target declaration does not match.");
                }
                for (var index = 0; index < call.Arguments.Length; index++)
                {
                    Visit(ref remaining);
                    var parameter = target.Variables[index];
                    if (parameter.IsOut || parameter.IsInit
                        || parameter.Type != call.Arguments[index].Type)
                    {
                        throw new ArgumentException("Indexed call argument declaration does not match.");
                    }
                }
            }
        }
    }

    private static void Visit(ref int remaining)
    {
        if (--remaining < 0)
        {
            throw new NotSupportedException("Scalar graph exceeds the native verification visit limit.");
        }
    }
}
