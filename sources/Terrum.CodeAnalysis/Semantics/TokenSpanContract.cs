// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Linq;

namespace Terrum.CodeAnalysis.Semantics;

internal enum TokenSpanTypeRole : byte
{
    None,
    Token,
    Range,
    Snapshot,
}

internal static class TokenSpanContract
{
    internal const string TokenName = "Terrum.CodeAnalysis.Token";
    internal const string RangeName = "Terrum.CodeAnalysis.SourceRange";
    internal const string SnapshotName = "Terrum.CodeAnalysis.TokenSnapshot";
    internal const string CursorName = "Terrum.CodeAnalysis.TokenCursor";
    internal const int Stride = 12;
    internal static PrimitiveType IndexCarrier => NativeTargetProfile.UnsignedIndexCarrier;
    internal static int CountOffset => NativeTargetProfile.PointerSize;
    internal static int DescriptorSize => checked(2 * NativeTargetProfile.PointerSize);

    internal static TokenSpanTypeRole RoleOf(BoundStruct structure) =>
        structure.TokenSpanRole != TokenSpanTypeRole.None ? structure.TokenSpanRole : structure.Name switch {
            TokenName => TokenSpanTypeRole.Token,
            RangeName => TokenSpanTypeRole.Range,
            SnapshotName => TokenSpanTypeRole.Snapshot,
            _ => TokenSpanTypeRole.None,
        };

    internal static int Find(IReadOnlyList<BoundStruct> structures, string name)
    {
        var role = name switch {
            TokenName => TokenSpanTypeRole.Token,
            RangeName => TokenSpanTypeRole.Range,
            SnapshotName => TokenSpanTypeRole.Snapshot,
            _ => TokenSpanTypeRole.None,
        };
        for (var index = 0; index < structures.Count; index++)
        {
            if (role != TokenSpanTypeRole.None
                ? RoleOf(structures[index]) == role
                : structures[index].Name == name)
            {
                return index;
            }
        }
        return -1;
    }

    internal static bool IsCursor(PrimitiveType type, IReadOnlyList<BoundStruct> structures) =>
        (int)type >= 100 && (int)type - 100 < structures.Count
        && structures[(int)type - 100].Name == CursorName
        && structures[(int)type - 100].Fields.Any(static field => field.Type == PrimitiveType.TokenSpan);

    internal static bool IsDependent(PrimitiveType type, IReadOnlyList<BoundStruct> structures) =>
        type == PrimitiveType.TokenSpan || IsCursor(type, structures);

    internal static bool HasMetadata(BoundProgram program)
    {
        if (program.Structures.Any(static structure => structure.Fields.Any(static field => field.Type == PrimitiveType.TokenSpan)))
        {
            return true;
        }
        List<BoundBlock>? bodies = null;
        foreach (var function in program.Functions)
        {
            if (!function.CapturedParameters.IsDefaultOrEmpty || function.ReturnsForwardedBacking
                || !function.ResultDependencyParameters.IsDefaultOrEmpty
                || function.ReturnType == PrimitiveType.TokenSpan
                || function.Variables.Any(static variable => variable.Type == PrimitiveType.TokenSpan))
            {
                return true;
            }
            (bodies ??= []).Add(function.Body);
        }
        if (bodies is not null)
        {
            foreach (var body in bodies)
            {
                if (HasSpanNodes(body))
                {
                    return true;
                }
            }
        }
        return false;
    }

    private static bool HasSpanNodes(BoundStatement body)
    {
        var pending = new Stack<object>();
        pending.Push(body);
        var remaining = SemanticVerifier.MaximumAnalysisVisits;
        while (pending.TryPop(out var node))
        {
            if (--remaining < 0)
            {
                throw new NotSupportedException("Compilation-local span inspection exceeds the verifier work limit.");
            }
            if (node is BoundExpression { Type: PrimitiveType.TokenSpan } or BoundDropSpan)
            {
                return true;
            }
            switch (node)
            {
                case BoundBlock block:
                    if (block.Statements.IsDefault)
                    {
                        throw new ArgumentException("A span-inspected block requires initialized statements.");
                    }
                    foreach (var child in block.Statements)
                    {
                        pending.Push(child);
                    }
                    break;
                case BoundTryFinally region:
                    pending.Push(region.Body);
                    pending.Push(region.Finally);
                    break;
                case BoundTryCatch region:
                    pending.Push(region.Body);
                    pending.Push(region.Catch);
                    if (region.Finally is not null)
                    {
                        pending.Push(region.Finally);
                    }
                    break;
                case BoundUnsafe region:
                    pending.Push(region.Body);
                    break;
                case BoundViewScope region:
                    pending.Push(region.Body);
                    pending.Push(region.Start);
                    pending.Push(region.RangeEnd ?? region.Length);
                    break;
                case BoundIf conditional:
                    pending.Push(conditional.Condition);
                    pending.Push(conditional.Then);
                    if (conditional.Else is not null)
                    {
                        pending.Push(conditional.Else);
                    }
                    break;
                case BoundWhile loop:
                    pending.Push(loop.Condition);
                    pending.Push(loop.Body);
                    if (loop.Iterator is not null)
                    {
                        pending.Push(loop.Iterator);
                    }
                    break;
                case BoundLocal local:
                    pending.Push(local.Initializer);
                    break;
                case BoundAssignment assignment:
                    pending.Push(assignment.Value);
                    break;
                case BoundFieldAssignment assignment:
                    pending.Push(assignment.Value);
                    break;
                case BoundBufferAssignment assignment:
                    pending.Push(assignment.Index);
                    pending.Push(assignment.Value);
                    break;
                case BoundViewSet assignment:
                    pending.Push(assignment.Index);
                    pending.Push(assignment.Value);
                    break;
                case BoundOptionalAssignment assignment:
                    pending.Push(assignment.Place);
                    pending.Push(assignment.Value);
                    break;
                case BoundExpressionStatement effects:
                    pending.Push(effects.Expression);
                    break;
                case BoundRequire requirement:
                    pending.Push(requirement.Condition);
                    break;
                case BoundReturn { Value: BoundExpression value }:
                    pending.Push(value);
                    break;
                case BoundUnsafeExpression unsafeExpression:
                    pending.Push(unsafeExpression.Expression);
                    break;
                case BoundSnapshotSpan or BoundSpanLength or BoundSpanRead or BoundSpanAdvance:
                    return true;
                case BoundCall call:
                    if (call.Arguments.IsDefault)
                    {
                        throw new ArgumentException("A span-inspected call requires initialized arguments.");
                    }
                    foreach (var argument in call.Arguments)
                    {
                        pending.Push(argument);
                    }
                    break;
                case BoundUnary unary:
                    pending.Push(unary.Operand);
                    break;
                case BoundCast cast:
                    pending.Push(cast.Operand);
                    break;
                case BoundFieldRead field:
                    pending.Push(field.Receiver);
                    break;
                case BoundBufferRead read:
                    pending.Push(read.Index);
                    break;
                case BoundViewRead read:
                    pending.Push(read.Index);
                    break;
                case BoundBinary binary:
                    pending.Push(binary.Left);
                    pending.Push(binary.Right);
                    break;
                case BoundConditional conditional:
                    pending.Push(conditional.Condition);
                    pending.Push(conditional.WhenTrue);
                    pending.Push(conditional.WhenFalse);
                    break;
                case BoundCoalesce coalesce:
                    pending.Push(coalesce.Optional);
                    pending.Push(coalesce.Fallback);
                    break;
                case BoundReadonlyProjection projection:
                    pending.Push(projection.Value);
                    break;
                case BoundOptionalLift lift:
                    pending.Push(lift.Value);
                    break;
                case BoundIsNull test:
                    pending.Push(test.Value);
                    break;
                case BoundIsSome test:
                    pending.Push(test.Value);
                    break;
                case BoundIsVariablePattern test:
                    pending.Push(test.Value);
                    break;
                case BoundUpdate update:
                    pending.Push(update.Place);
                    break;
                case BoundNominalUpdate update:
                    pending.Push(update.Place);
                    break;
                case BoundCompoundAssignment assignment:
                    pending.Push(assignment.Place);
                    pending.Push(assignment.Right);
                    break;
            }
        }
        return false;
    }

    internal static bool ValidTokenLayout(IReadOnlyList<BoundStruct> structures)
    {
        var token = Find(structures, TokenName);
        var range = Find(structures, RangeName);
        return token >= 0 && range >= 0
            && !structures[token].IsUnique && !structures[range].IsUnique
            && structures[token].Size == Stride && structures[range].Size == 8
            && structures[token].Fields is [var kind, var location]
            && kind.Name == "Kind" && kind.Type == PrimitiveType.Int32 && kind.Offset == 0 && kind.Size == 4
            && location.Name == "Range" && location.Type == (PrimitiveType)(100 + range)
            && location.Offset == 4 && location.Size == 8
            && structures[range].Fields is [var start, var length]
            && start.Name == "Start" && start.Type == PrimitiveType.Int32 && start.Offset == 0 && start.Size == 4
            && length.Name == "Length" && length.Type == PrimitiveType.Int32 && length.Offset == 4 && length.Size == 4
            && ValueLayout.Alignment((PrimitiveType)(100 + token), structures) == 4;
    }

    internal static bool ValidSnapshot(BoundStruct structure) =>
        RoleOf(structure) == TokenSpanTypeRole.Snapshot && structure.IsUnique && structure.Size == 16
        && structure.Fields is [var records, var count]
        && records.Name == "_records" && records.Type == PrimitiveType.OwnedInt32Buffer
        && records.Offset == 0 && records.Size == 8 && records.IsPrivate && !records.IsMutable
        && !records.RequiresUnsafeCaller
        && count.Name == "_count" && count.Type == PrimitiveType.Int32
        && count.Offset == 8 && count.Size == 4 && count.IsPrivate && !count.IsMutable
        && !count.RequiresUnsafeCaller;

    internal static bool ValidCursor(BoundStruct structure) =>
        structure.Name == CursorName && structure.IsUnique && structure.Size == DescriptorSize
        && structure.CleanupMethod is null && !structure.IsSequential && !structure.IsTransparent
        && structure.Fields is [var tokens]
        && tokens.Name == "_tokens" && tokens.Type == PrimitiveType.TokenSpan
        && tokens.Offset == 0 && tokens.Size == DescriptorSize && tokens.IsPrivate && tokens.IsMutable
        && !tokens.RequiresUnsafeCaller;
}
