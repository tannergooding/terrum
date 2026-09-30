// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Immutable;

namespace Terrum.CodeAnalysis.Semantics;

internal sealed record TrRmScalarEdge(int Target, ImmutableArray<int> Arguments);

/// <summary>An audited, equal-width integer bit reinterpretation; never a structured source cast.</summary>
internal sealed record TrRmScalarReinterpret(BoundExpression Operand, PrimitiveType Type)
    : BoundExpression(Type, default);

internal sealed record TrRmScalarBlock(ImmutableArray<int> Parameters,
    ImmutableArray<BoundLocal> Operations, int Condition, TrRmScalarEdge? True,
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

    internal void Verify(BoundFunction function, BoundRuntimePolicy policy)
    {
        if (policy != BoundRuntimePolicy.FailFast || function.IsExternal
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
    }
}
