// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.InteropServices;
using Terrum.CodeAnalysis.Lexing;
using Terrum.CodeAnalysis.Semantics;

namespace Terrum.CodeAnalysis.Modules;

public static partial class TrRmExecutableCompiler
{
    private sealed class IndexedEdge(IndexedBlock target, int[] arguments)
    {
        internal IndexedBlock Target = target;
        internal int[] Arguments = arguments;
    }

    private sealed class IndexedBlock(int creation, int region)
    {
        internal int Creation = creation;
        internal int Id;
        internal int Region = region;
        internal bool IsLoopHeader;
        internal readonly List<int> Parameters = [];
        internal readonly List<(int Result, BoundExpression Expression)> Operations = [];
        internal int Condition;
        internal IndexedEdge? True;
        internal IndexedEdge? False;
        internal int ReturnValue = -1;
        internal int FailureReason;
    }

    private sealed class IndexedRegion(int id, int parent, IndexedBlock header, int[] visibleSlots)
    {
        internal int Id = id;
        internal int Parent = parent;
        internal IndexedBlock Header = header;
        internal IndexedBlock ContinueTarget = header;
        internal IndexedBlock? Exit;
        internal int[] VisibleSlots = visibleSlots;
        internal bool HasSourceContinuation;
        internal bool HasHeaderBackedge;
        internal readonly Dictionary<int, (IndexedRegion Target, bool Continue)> Transfers = [];
        internal readonly List<(IndexedBlock Source, IndexedEdge Edge, int Code)> ExitEdges = [];
    }

    private sealed class IndexedBuilder
    {
        private readonly BoundFunction _function;
        private readonly BoundProgram _program;
        private readonly bool _hasFloatingTypes;
        private readonly List<IndexedBlock> _blocks = [];
        private readonly List<IndexedRegion> _regions = [];
        private readonly List<PrimitiveType> _types = [];
        private readonly Stack<IndexedRegion> _loops = [];
        private IndexedBlock? _current;
        private int[] _slots;

        internal IndexedBuilder(BoundFunction function, BoundProgram program, bool hasFloatingTypes)
        {
            _function = function;
            _program = program;
            _hasFloatingTypes = hasFloatingTypes;
            _slots = new int[function.Variables.Length];
            _current = NewBlock(0);
            for (var index = 0; index < function.ParameterCount; index++)
            {
                _slots[index] = NewValue(function.Variables[index].Type);
                _current.Parameters.Add(_slots[index]);
            }
        }

        private static byte[] EncodeIndexed(BoundFunction function, List<IndexedBlock> source,
            List<IndexedRegion> regions, List<PrimitiveType> sourceTypes, BoundProgram program,
            bool hasFloatingTypes)
        {
            static bool IsBackEdge(IndexedBlock block, IndexedEdge edge)
                => edge.Target.IsLoopHeader && block.Region == edge.Target.Region && block != edge.Target;

            var indegree = new int[source.Count];
            foreach (var block in source)
            {
                if (block.True is { } trueEdge && !IsBackEdge(block, trueEdge))
                {
                    indegree[trueEdge.Target.Creation]++;
                }
                if (block.False is { } falseEdge && !IsBackEdge(block, falseEdge))
                {
                    indegree[falseEdge.Target.Creation]++;
                }
            }
            var ordered = new List<IndexedBlock>(source.Count);
            var ready = new SortedSet<int>();
            foreach (var block in source)
            {
                if (indegree[block.Creation] == 0)
                {
                    ready.Add(block.Creation);
                }
            }
            while (ordered.Count < source.Count)
            {
                if (ready.Count == 0)
                {
                    throw Unsupported("Indexed scalar flow contains a non-loop cycle.");
                }
                var first = ready.Min;
                ready.Remove(first);
                var next = source[first];
                next.Id = ordered.Count + 1;
                ordered.Add(next);
                if (next.True is { } trueEdge && !IsBackEdge(next, trueEdge))
                {
                    if (--indegree[trueEdge.Target.Creation] == 0)
                    {
                        ready.Add(trueEdge.Target.Creation);
                    }
                }
                if (next.False is { } falseEdge && !IsBackEdge(next, falseEdge))
                {
                    if (--indegree[falseEdge.Target.Creation] == 0)
                    {
                        ready.Add(falseEdge.Target.Creation);
                    }
                }
            }
            var remap = new int[sourceTypes.Count + 1];
            var types = new PrimitiveType[sourceTypes.Count + 1];
            var owner = new int[sourceTypes.Count + 1];
            var starts = new int[ordered.Count + 1];
            foreach (var block in ordered)
            {
                foreach (var value in block.Parameters)
                {
                    remap[value] = ++starts[block.Id];
                }
                foreach (var (result, _) in block.Operations)
                {
                    if (result != 0)
                    {
                        remap[result] = ++starts[block.Id];
                    }
                }
                starts[block.Id] += starts[block.Id - 1];
                var first = starts[block.Id - 1];
                foreach (var value in block.Parameters)
                {
                    types[remap[value] += first] = sourceTypes[value - 1];
                    owner[remap[value]] = block.Id;
                }
                foreach (var (result, _) in block.Operations)
                {
                    if (result != 0)
                    {
                        types[remap[result] += first] = sourceTypes[result - 1];
                        owner[remap[result]] = block.Id;
                    }
                }
            }
            for (var id = 1; id < remap.Length; id++)
            {
                if (remap[id] == 0)
                {
                    throw Unsupported("Indexed value inventory is incomplete.");
                }
            }

            var lastUses = new SortedDictionary<int, int>?[ordered.Count];
            var liveIns = new HashSet<int>?[ordered.Count];
            var currentBlockIndex = 0;
            var operationOrdinal = 0;
            void Use(int id, int ordinal)
            {
                if (id <= 0)
                {
                    return;
                }
                var mapped = remap[id];
                (lastUses[currentBlockIndex] ??= new SortedDictionary<int, int>())[mapped] = ordinal;
                if (owner[mapped] != ordered[currentBlockIndex].Id)
                {
                    (liveIns[currentBlockIndex] ??= new HashSet<int>()).Add(mapped);
                }
            }
            void UseAtOperation(int id) => Use(id, operationOrdinal);
            Action<int> operationVisitor = UseAtOperation;
            for (var b = 0; b < ordered.Count; b++)
            {
                currentBlockIndex = b;
                var block = ordered[b];
                operationOrdinal = 0;
                for (; operationOrdinal < block.Operations.Count; operationOrdinal++)
                {
                    VisitReads(block.Operations[operationOrdinal].Expression, operationVisitor);
                }
                Use(block.Condition != 0 ? block.Condition : block.ReturnValue, block.Operations.Count);
                if (block.True is { } trueEdge)
                {
                    foreach (var argument in trueEdge.Arguments)
                    {
                        Use(argument, block.Operations.Count);
                    }
                }
                if (block.False is { } falseEdge)
                {
                    foreach (var argument in falseEdge.Arguments)
                    {
                        Use(argument, block.Operations.Count);
                    }
                }
            }
            var liveOuts = new HashSet<int>?[ordered.Count];
            if (regions.Count == 0)
            {
                // Without loop headers, ordering includes every edge, so successors are already final.
                for (var b = ordered.Count - 1; b >= 0; b--)
                {
                    var block = ordered[b];
                    if (block.True is { } trueEdge)
                    {
                        if (liveIns[trueEdge.Target.Id - 1] is HashSet<int> targetLiveIn)
                        {
                            (liveOuts[b] ??= new HashSet<int>()).UnionWith(targetLiveIn);
                        }
                        foreach (var argument in trueEdge.Arguments)
                        {
                            (liveOuts[b] ??= new HashSet<int>()).Add(remap[argument]);
                        }
                    }
                    if (block.False is { } falseEdge)
                    {
                        if (liveIns[falseEdge.Target.Id - 1] is HashSet<int> targetLiveIn)
                        {
                            (liveOuts[b] ??= new HashSet<int>()).UnionWith(targetLiveIn);
                        }
                        foreach (var argument in falseEdge.Arguments)
                        {
                            (liveOuts[b] ??= new HashSet<int>()).Add(remap[argument]);
                        }
                    }
                    if (liveOuts[b] is HashSet<int> liveOut)
                    {
                        foreach (var id in liveOut)
                        {
                            if (owner[id] != block.Id)
                            {
                                (liveIns[b] ??= new HashSet<int>()).Add(id);
                            }
                        }
                    }
                }
            }
            else
            {
                var predecessorCounts = new int[ordered.Count];
                var seeds = new List<int>?[types.Length];
                for (var block = 0; block < ordered.Count; block++)
                {
                    if (liveIns[block] is HashSet<int> liveIn)
                    {
                        foreach (var id in liveIn)
                        {
                            (seeds[id] ??= []).Add(block);
                        }
                    }
                }
                for (var b = 0; b < ordered.Count; b++)
                {
                    var block = ordered[b];
                    void AddEdge(IndexedEdge edge)
                    {
                        predecessorCounts[edge.Target.Id - 1]++;
                        foreach (var argument in edge.Arguments)
                        {
                            (liveOuts[b] ??= new HashSet<int>()).Add(remap[argument]);
                        }
                    }
                    if (block.True is { } trueEdge)
                    {
                        AddEdge(trueEdge);
                    }
                    if (block.False is { } falseEdge)
                    {
                        AddEdge(falseEdge);
                    }
                }
                var predecessorStarts = new int[ordered.Count + 1];
                for (var b = 0; b < ordered.Count; b++)
                {
                    predecessorStarts[b + 1] = predecessorStarts[b] + predecessorCounts[b];
                    predecessorCounts[b] = 0;
                }
                var predecessors = new int[predecessorStarts[ordered.Count]];
                for (var b = 0; b < ordered.Count; b++)
                {
                    var block = ordered[b];
                    void AddPredecessor(IndexedEdge edge)
                    {
                        var target = edge.Target.Id - 1;
                        predecessors[predecessorStarts[target] + predecessorCounts[target]++] = b;
                    }
                    if (block.True is { } trueEdge)
                    {
                        AddPredecessor(trueEdge);
                    }
                    if (block.False is { } falseEdge)
                    {
                        AddPredecessor(falseEdge);
                    }
                }

                // Each block enters the queue once per value, bounding propagation by V * (B + E).
                // External edge arguments already seed In; locally owned arguments seed only Out.
                var pendingBlocks = new int[ordered.Count];
                for (var id = 1; id < types.Length; id++)
                {
                    if (seeds[id] is not List<int> valueSeeds)
                    {
                        continue;
                    }

                    var pendingCount = 0;
                    foreach (var b in valueSeeds)
                    {
                        pendingBlocks[pendingCount++] = b;
                    }
                    for (var next = 0; next < pendingCount; next++)
                    {
                        var b = pendingBlocks[next];
                        for (var index = predecessorStarts[b]; index < predecessorStarts[b + 1]; index++)
                        {
                            var predecessor = predecessors[index];
                            (liveOuts[predecessor] ??= new HashSet<int>()).Add(id);
                            if (owner[id] != predecessor + 1)
                            {
                                var predecessorLiveIn = liveIns[predecessor]
                                    ??= new HashSet<int>();
                                if (predecessorLiveIn.Add(id))
                                {
                                    pendingBlocks[pendingCount++] = predecessor;
                                }
                            }
                        }
                    }
                }
            }
            var useCounts = new int[types.Length];
            foreach (var uses in lastUses)
            {
                if (uses is not null)
                {
                    foreach (var id in uses.Keys)
                    {
                        useCounts[id]++;
                    }
                }
            }

            byte[] EncodeWidth(int width)
            {
                var sentinel = width == 1 ? byte.MaxValue : width == 2 ? ushort.MaxValue : uint.MaxValue;
                var offsets = new List<byte>(checked(width * (ordered.Count + 1)));
                var definitions = new uint[sourceTypes.Count];
                var terminators = new List<int>(ordered.Count);
                var data = new List<byte>(checked(ordered.Count * (width + 3)));
                var overflowedDefinition = false;
                for (var index = 0; index < width; index++)
                {
                    offsets.Add(0);
                }
                foreach (var block in ordered)
                {
                    var blockStart = data.Count;
                    var blockLiveIns = liveIns[block.Id - 1];
                    var blockLiveOuts = liveOuts[block.Id - 1];
                    var blockLastUses = lastUses[block.Id - 1];
                    var presence = (block.Region != 0 ? 1 : 0)
                        | (block.Parameters.Count != 0 ? 2 : 0)
                        | (blockLiveIns is { Count: > 0 } ? 4 : 0)
                        | (blockLiveOuts is { Count: > 0 } ? 8 : 0)
                        | (blockLastUses is { Count: > 0 } ? 16 : 0);
                    data.Add((byte)presence);
                    if (block.Region != 0)
                    {
                        Write(data, (ulong)block.Region, width);
                    }
                    if (block.Parameters.Count != 0)
                    {
                        Write(data, (ulong)block.Parameters.Count, width);
                        foreach (var parameter in block.Parameters)
                        {
                            Write(data, (ulong)remap[parameter], width);
                        }
                    }
                    if (blockLiveIns is { Count: > 0 } liveInSet)
                    {
                        var orderedLiveIns = liveInSet.ToArray();
                        if (orderedLiveIns.Length > 1)
                        {
                            Array.Sort(orderedLiveIns);
                        }
                        WriteList(data, orderedLiveIns, width);
                    }
                    if (blockLiveOuts is { Count: > 0 } liveOutSet)
                    {
                        var orderedLiveOuts = liveOutSet.ToArray();
                        if (orderedLiveOuts.Length > 1)
                        {
                            Array.Sort(orderedLiveOuts);
                        }
                        WriteList(data, orderedLiveOuts, width);
                    }
                    if (blockLastUses is { Count: > 0 } blockLastUseSet)
                    {
                        Write(data, (ulong)blockLastUseSet.Count, width);
                        foreach (var (value, ordinal) in blockLastUseSet)
                        {
                            Write(data, (ulong)value, width);
                            Write(data, (ulong)ordinal, width);
                        }
                    }
                    Write(data, (ulong)block.Operations.Count, width);
                    foreach (var parameter in block.Parameters)
                    {
                        definitions[remap[parameter] - 1] = sentinel;
                    }
                    foreach (var (result, expression) in block.Operations)
                    {
                        if (result != 0)
                        {
                            definitions[remap[result] - 1] = (uint)(data.Count - blockStart);
                            overflowedDefinition |= definitions[remap[result] - 1] >= sentinel;
                        }
                        WriteScalarOperation(data, result == 0 ? 0 : remap[result],
                            expression, remap, function.IsNoThrow, program);
                    }
                    terminators.Add(data.Count - blockStart);
                    WriteIndexedTerminator(data, block, remap);
                    Write(offsets, (ulong)data.Count, width);
                }
                if (data.Count > sentinel || overflowedDefinition
                    || terminators.Any(offset => offset > sentinel)
                    || starts.Any(start => (ulong)start > sentinel))
                {
                    return [];
                }
                var typeCount = hasFloatingTypes ? 8 : 6;
                var bodyCapacity = checked(1 + (4 * width) + (4 * typeCount)
                    + (regions.Count * (1 + (4 * width)))
                    + (sourceTypes.Count * (2 * width))
                    + (starts.Length * width)
                    + (definitions.Length * width)
                    + offsets.Count
                    + (terminators.Count * width)
                    + data.Count);
                var body = new byte[bodyCapacity];
                var bodyPosition = 0;
                var destination = body.AsSpan();
                destination[bodyPosition++] = (byte)(0x08 | WidthCode(width));
                WriteToSpan(destination, ref bodyPosition, (ulong)ordered.Count, width);
                WriteToSpan(destination, ref bodyPosition, (ulong)sourceTypes.Count, width);
                WriteToSpan(destination, ref bodyPosition, (ulong)regions.Count, width);
                WriteToSpan(destination, ref bodyPosition, (ulong)typeCount, width);
                for (var row = 2; row <= typeCount + 1; row++)
                {
                    WriteToSpan(destination, ref bodyPosition, (ulong)row, 4);
                }
                foreach (var region in regions)
                {
                    destination[bodyPosition++] = 1;
                    WriteToSpan(destination, ref bodyPosition, (ulong)region.Parent, width);
                    WriteToSpan(destination, ref bodyPosition, (ulong)region.Header.Id, width);
                    WriteToSpan(destination, ref bodyPosition,
                        (ulong)(region.Exit ?? throw Unsupported("Loop exit is missing.")).Id, width);
                    WriteToSpan(destination, ref bodyPosition, 0, width);
                }
                for (var id = 1; id < types.Length; id++)
                {
                    WriteToSpan(destination, ref bodyPosition, (ulong)(TypeRow(types[id]) - 1), width);
                    WriteToSpan(destination, ref bodyPosition, (ulong)useCounts[id], width);
                }
                foreach (var start in starts)
                {
                    WriteToSpan(destination, ref bodyPosition, (ulong)start, width);
                }
                foreach (var definition in definitions)
                {
                    WriteToSpan(destination, ref bodyPosition, definition, width);
                }
                offsets.CopyTo(body, bodyPosition);
                bodyPosition += offsets.Count;
                foreach (var terminator in terminators)
                {
                    WriteToSpan(destination, ref bodyPosition, (ulong)terminator, width);
                }
                data.CopyTo(body, bodyPosition);
                bodyPosition += data.Count;
                if (bodyPosition != body.Length)
                {
                    throw new InvalidOperationException("Indexed TRRM Body capacity did not match its encoded length.");
                }

                return body;
            }

            for (var width = 1; width <= 4; width *= 2)
            {
                if (ordered.Count > (width == 1 ? byte.MaxValue : width == 2 ? ushort.MaxValue : uint.MaxValue)
                    || sourceTypes.Count > (width == 1 ? byte.MaxValue : width == 2 ? ushort.MaxValue : uint.MaxValue))
                {
                    continue;
                }
                var body = EncodeWidth(width);
                if (body.Length != 0)
                {
                    return body;
                }
            }
            throw Unsupported("Indexed scalar Body exceeds four-byte offset/index bounds.");
        }

        private static void VisitReads(BoundExpression expression, Action<int> visit)
        {
            switch (expression)
            {
                case BoundRead read:
                    visit(read.Variable + 1);
                    break;
                case BoundUnary unary:
                    VisitReads(unary.Operand, visit);
                    break;
                case BoundCast cast:
                    VisitReads(cast.Operand, visit);
                    break;
                case TrRmScalarReinterpret reinterpret:
                    VisitReads(reinterpret.Operand, visit);
                    break;
                case TrRmScalarOptionalLift lift:
                    VisitReads(lift.Value, visit);
                    break;
                case TrRmScalarOptionalPresence presence:
                    VisitReads(presence.Optional, visit);
                    break;
                case TrRmScalarOptionalEquality equality:
                    VisitReads(equality.Left, visit);
                    VisitReads(equality.Right, visit);
                    break;
                case BoundConditional conditional:
                    VisitReads(conditional.Condition, visit);
                    VisitReads(conditional.WhenTrue, visit);
                    VisitReads(conditional.WhenFalse, visit);
                    break;
                case BoundBinary binary:
                    VisitReads(binary.Left, visit);
                    VisitReads(binary.Right, visit);
                    break;
                case BoundCall call:
                    foreach (var argument in call.Arguments)
                    {
                        VisitReads(argument, visit);
                    }
                    break;
            }
        }

        private static void WriteList(List<byte> bytes, int[] values, int width)
        {
            Write(bytes, (ulong)values.Length, width);
            foreach (var value in values)
            {
                Write(bytes, (ulong)value, width);
            }
        }

        private static void WriteIndexedTerminator(List<byte> bytes, IndexedBlock block, int[] remap)
        {
            if (block.FailureReason != 0)
            {
                if (block.FailureReason is < 1 or > 4 || block.Condition != 0
                    || block.True is not null || block.False is not null)
                {
                    throw Unsupported("Indexed failure terminator has invalid flow.");
                }
                bytes.Add(0x87);
                bytes.Add((byte)block.FailureReason);
                return;
            }
            var maximumOperand = Math.Max(block.Condition == 0
                ? block.ReturnValue <= 0 ? 0 : remap[block.ReturnValue]
                : remap[block.Condition], Math.Max(block.True?.Target.Id ?? 0, block.False?.Target.Id ?? 0));
            if (block.True is { } trueEdgeForWidth)
            {
                foreach (var value in trueEdgeForWidth.Arguments)
                {
                    maximumOperand = Math.Max(maximumOperand, remap[value]);
                }
            }
            if (block.False is { } falseEdgeForWidth)
            {
                foreach (var value in falseEdgeForWidth.Arguments)
                {
                    maximumOperand = Math.Max(maximumOperand, remap[value]);
                }
            }
            var referenceWidth = Width((ulong)maximumOperand);
            var countWidth = Width((ulong)Math.Max(block.True?.Arguments.Length ?? 0,
                block.False?.Arguments.Length ?? 0));
            if (block.True is null)
            {
                WriteInstruction(bytes, 0x83, referenceWidth, 0);
                Write(bytes, (ulong)(block.ReturnValue == 0 ? 0 : remap[block.ReturnValue]), referenceWidth);
                return;
            }
            var opcode = block.False is null ? 0x80 : 0x81;
            if (referenceWidth == 1 && countWidth == 1)
            {
                bytes.Add((byte)opcode);
            }
            else
            {
                bytes.Add(0xF0);
                bytes.Add((byte)(WidthCode(referenceWidth) | (WidthCode(countWidth) << 2)));
                bytes.Add((byte)opcode);
            }
            if (block.False is not null)
            {
                Write(bytes, (ulong)remap[block.Condition], referenceWidth);
            }
            if (block.True is { } trueEdgeToWrite)
            {
                WriteIndexedEdge(bytes, trueEdgeToWrite, remap, referenceWidth, countWidth);
            }
            if (block.False is { } falseEdgeToWrite)
            {
                WriteIndexedEdge(bytes, falseEdgeToWrite, remap, referenceWidth, countWidth);
            }
        }

        private static void WriteIndexedEdge(List<byte> bytes, IndexedEdge edge, int[] remap,
            int referenceWidth, int countWidth)
        {
            Write(bytes, (ulong)edge.Target.Id, referenceWidth);
            Write(bytes, (ulong)edge.Arguments.Length, countWidth);
            foreach (var value in edge.Arguments)
            {
                Write(bytes, (ulong)remap[value], referenceWidth);
            }
        }

        private static void WriteScalarOperation(List<byte> bytes, int result,
            BoundExpression expression, int[] remap, bool noThrow, BoundProgram program)
        {
            int ReadOperand(BoundExpression operand) => operand is BoundRead read
                ? remap[read.Variable + 1] : throw Unsupported("Indexed scalar operand was not materialized.");
            var opcode = 0;
            var kind = 0;
            var target = 0;
            var left = 0;
            var right = 0;
            ulong bits = 0;
            var dataWidth = 0;
            switch (expression)
            {
                case BoundConstant constant:
                    opcode = 1;
                    target = TypeRow(constant.Type) - 1;
                    bits = constant.Type switch {
                        PrimitiveType.Int32 or PrimitiveType.UInt32 => unchecked((uint)constant.Value),
                        PrimitiveType.Bool or PrimitiveType.Char16 => (ulong)constant.Value,
                        _ => unchecked((ulong)constant.Value),
                    };
                    dataWidth = Width(bits);
                    break;
                case BoundUnary unary:
                    left = ReadOperand(unary.Operand);
                    (opcode, kind) = unary.Operator switch {
                        TokenKind.Bang => (4, 1),
                        TokenKind.Tilde => (4, 2),
                        TokenKind.Plus => (4, 3),
                        TokenKind.Minus when unary.Type is PrimitiveType.Float32 or PrimitiveType.Float64 => (0x1C, 0),
                        TokenKind.Minus when unary.Type is PrimitiveType.Int32 or PrimitiveType.Int64 => (0x17, 0),
                        _ => throw Unsupported($"Unary operator {unary.Operator} has no executable TRRM encoding."),
                    };
                    break;
                case BoundCast cast:
                    left = ReadOperand(cast.Operand);
                    if (IsFloat(cast.Operand.Type) && IsFloat(cast.Type))
                    {
                        opcode = cast.Operand.Type == cast.Type ? 3 : 0x1A;
                        target = opcode == 0x1A ? TypeRow(cast.Type) - 1 : 0;
                    }
                    else if (IsInteger(cast.Operand.Type) && IsFloat(cast.Type))
                    {
                        opcode = 0x1B;
                        target = TypeRow(cast.Type) - 1;
                    }
                    else if (cast.Operand.Type == PrimitiveType.Char16 && IsFloat(cast.Type))
                    {
                        opcode = 0x1E;
                        target = TypeRow(cast.Type) - 1;
                    }
                    else if (IsFloat(cast.Operand.Type) && IsInteger(cast.Type))
                    {
                        opcode = 0x1F;
                        target = TypeRow(cast.Type) - 1;
                    }
                    else if (IsFloat(cast.Operand.Type) || IsFloat(cast.Type))
                    {
                        throw Unsupported("This floating conversion pair is outside executable TRRM.");
                    }
                    else
                    {
                        opcode = 0x16;
                        target = TypeRow(cast.Type) - 1;
                    }
                    break;
                case TrRmScalarReinterpret reinterpret:
                    left = ReadOperand(reinterpret.Operand);
                    if (!CanReinterpret(reinterpret.Operand.Type, reinterpret.Type))
                    {
                        throw Unsupported("Only equal-width integer bit reinterpretation is executable.");
                    }
                    opcode = 6;
                    target = TypeRow(reinterpret.Type) - 1;
                    break;
                case BoundBinary binary:
                    if (IsFloat(binary.Left.Type))
                    {
                        (opcode, kind) = binary.Operator switch
                        {
                            TokenKind.Plus => (0x1D, 1),
                            TokenKind.Minus => (0x1D, 2),
                            TokenKind.Star => (0x1D, 3),
                            TokenKind.Slash => (0x1D, 4),
                            TokenKind.EqualsEquals => (0x19, 1),
                            TokenKind.BangEquals => (0x19, 2),
                            TokenKind.Less => (0x19, 3),
                            TokenKind.LessEquals => (0x19, 4),
                            TokenKind.Greater => (0x19, 5),
                            TokenKind.GreaterEquals => (0x19, 6),
                            _ => throw Unsupported($"Floating operator {binary.Operator} has no executable TRRM encoding."),
                        };
                        left = ReadOperand(binary.Left);
                        right = ReadOperand(binary.Right);
                        break;
                    }
                    if (binary.Operator is TokenKind.AmpersandAmpersand or TokenKind.PipePipe
                        || binary.Operator is TokenKind.Ampersand or TokenKind.Pipe or TokenKind.Caret
                            && binary.Left.Type == PrimitiveType.Bool
                        || binary.Operator is TokenKind.Less or TokenKind.LessEquals
                            or TokenKind.Greater or TokenKind.GreaterEquals
                            && binary.Left.Type == PrimitiveType.Char16)
                    {
                        throw Unsupported($"Binary operator {binary.Operator} has no executable scalar encoding.");
                    }
                    left = ReadOperand(binary.Left);
                    right = ReadOperand(binary.Right);
                    (opcode, kind) = binary.Operator switch {
                        TokenKind.Ampersand => (5, 1),
                        TokenKind.Pipe => (5, 2),
                        TokenKind.Caret => (5, 3),
                        TokenKind.EqualsEquals => (5, 4),
                        TokenKind.BangEquals => (5, 5),
                        TokenKind.Less => (5, 6),
                        TokenKind.LessEquals => (5, 7),
                        TokenKind.Greater => (5, 8),
                        TokenKind.GreaterEquals => (5, 9),
                        TokenKind.Plus => (0x15, 1),
                        TokenKind.Minus => (0x15, 2),
                        TokenKind.Star => (0x15, 3),
                        TokenKind.Slash => (0x15, 4),
                        TokenKind.Percent => (0x15, 5),
                        TokenKind.ShiftLeft => (0x15, 6),
                        TokenKind.ShiftRight => (0x15, 7),
                        TokenKind.ShiftRightUnsigned => (0x15, 7),
                        _ => throw Unsupported($"Binary operator {binary.Operator} has no executable scalar encoding."),
                    };
                    break;
                case BoundCall call:
                    opcode = 0x12;
                    target = call.Function + 1;
                    if (noThrow && !program.Functions[call.Function].IsNoThrow)
                    {
                        throw Unsupported("NoThrow caller cannot target a may-fail declaration.");
                    }
                    break;
                default:
                    throw Unsupported($"Operation {expression.GetType().Name} is not executable.");
            }
            if (noThrow && opcode is 0x15 or 0x16 or 0x17 or 0x1F)
            {
                throw Unsupported("NoThrow requires a total indexed Body.");
            }
            var maximum = Math.Max(result, target);
            if (expression is BoundCall callOperands)
            {
                foreach (var argument in callOperands.Arguments)
                {
                    maximum = Math.Max(maximum, ReadOperand(argument));
                }
            }
            var referenceWidth = Width((ulong)maximum);
            if (opcode == 0x12)
            {
                var call = (BoundCall)expression;
                var countWidth = Width((ulong)call.Arguments.Length);
                if (referenceWidth == 1 && countWidth == 1)
                {
                    bytes.Add(0x12);
                }
                else
                {
                    bytes.Add(0xF0);
                    bytes.Add((byte)(WidthCode(referenceWidth) | (WidthCode(countWidth) << 2)));
                    bytes.Add(0x12);
                }
                Write(bytes, (ulong)result, referenceWidth);
                Write(bytes, (ulong)target, referenceWidth);
                Write(bytes, (ulong)call.Arguments.Length, countWidth);
                foreach (var argument in call.Arguments)
                {
                    Write(bytes, (ulong)ReadOperand(argument), referenceWidth);
                }
                return;
            }
            WriteInstruction(bytes, opcode, referenceWidth, dataWidth);
            Write(bytes, (ulong)result, referenceWidth);
            if (opcode is 1 or 6 or 0x16 or 0x1A or 0x1B or 0x1E or 0x1F)
            {
                Write(bytes, (ulong)target, referenceWidth);
            }
            if (opcode == 6)
            {
                bytes.Add(3);
            }
            if (opcode is 4 or 5 or 0x15 or 0x19 or 0x1D)
            {
                bytes.Add((byte)kind);
            }
            if (opcode == 1)
            {
                Write(bytes, bits, dataWidth);
            }
            if (opcode is 4 or 6 or 0x16 or 0x17 or 0x1A or 0x1B or 0x1C or 0x1E or 0x1F)
            {
                Write(bytes, (ulong)left, referenceWidth);
            }
            if (opcode is 5 or 0x15 or 0x19 or 0x1D)
            {
                Write(bytes, (ulong)left, referenceWidth);
                Write(bytes, (ulong)right, referenceWidth);
            }
        }

        internal static TrRmScalarGraph DecodeIndexedBody(ReadOnlySpan<byte> body,
            int parameterCount, PrimitiveType resultType, bool noThrow, bool doesNotReturn,
            TrRmTypeSpecTableReader types, ImmutableArray<BoundFunction>.Builder functions)
        {
            var position = 1;
            var width = 1 << (body[0] & 3);
            var blockCount = ReadInt(body, ref position, width);
            var valueCount = ReadInt(body, ref position, width);
            var regionCount = ReadInt(body, ref position, width);
            var typeCount = ReadInt(body, ref position, width);
            if (blockCount == 0 || blockCount > SemanticVerifier.MaximumAnalysisVisits
                || valueCount > SemanticVerifier.MaximumVariables
                || typeCount is < 0 or > 12)
            {
                throw Invalid("Indexed scalar Body exceeds native verification bounds.");
            }
            var typeMap = new int[typeCount];
            for (var index = 0; index < typeMap.Length; index++)
            {
                typeMap[index] = ReadInt(body, ref position, 4);
                if (BodyTypeFromRow(types, typeMap[index]) == PrimitiveType.Void)
                {
                    throw Invalid("Void indexed value type.");
                }
            }
            position += regionCount * (1 + 4 * width);
            var valueTypes = ImmutableArray.CreateBuilder<PrimitiveType>(valueCount);
            for (var value = 0; value < valueCount; value++)
            {
                var typeId = ReadInt(body, ref position, width);
                _ = ReadInt(body, ref position, width);
                if (typeId <= 0 || typeId > typeCount)
                {
                    throw Invalid("Indexed value has unmapped type.");
                }
                valueTypes.Add(BodyTypeFromRow(types, typeMap[typeId - 1]));
            }
            var starts = new int[blockCount + 1];
            for (var index = 0; index < starts.Length; index++)
            {
                starts[index] = ReadInt(body, ref position, width);
            }
            position += valueCount * width;
            var offsets = new int[blockCount + 1];
            for (var index = 0; index < offsets.Length; index++)
            {
                offsets[index] = ReadInt(body, ref position, width);
            }
            var terminators = new int[blockCount];
            for (var index = 0; index < terminators.Length; index++)
            {
                terminators[index] = ReadInt(body, ref position, width);
            }
            var blockData = body[position..];
            if (width != Width((ulong)Math.Max(blockData.Length,
                Math.Max(blockCount, Math.Max(valueCount, typeCount)))))
            {
                throw Invalid("Nonminimal indexed Body-local width.");
            }
            var result = ImmutableArray.CreateBuilder<TrRmScalarBlock>(blockCount);
            for (var b = 0; b < blockCount; b++)
            {
                var extent = blockData[offsets[b]..offsets[b + 1]];
                var cursor = 0;
                var presence = ReadByte(extent, ref cursor);
                if ((presence & 1) != 0)
                {
                    _ = ReadInt(extent, ref cursor, width);
                }
                var parameters = ReadIndexedList(extent, ref cursor, width, (presence & 2) != 0);
                _ = ReadIndexedList(extent, ref cursor, width, (presence & 4) != 0);
                _ = ReadIndexedList(extent, ref cursor, width, (presence & 8) != 0);
                if ((presence & 16) != 0)
                {
                    var count = ReadInt(extent, ref cursor, width);
                    cursor += count * 2 * width;
                }
                var countOperations = ReadInt(extent, ref cursor, width);
                var operationBytes = extent[cursor..terminators[b]];
                // Reuse scalar opcode/effect auditing with earlier SSA IDs as inputs.
                // The indexed audit has already established their real dominance and types.
                var localWidthCode = WidthCode(
                    Width((ulong)Math.Max(Math.Max(starts[b + 1], countOperations), typeCount)));
                var localWidth = 1 << localWidthCode;
                var syntheticCapacity = checked(1 + (2 * localWidth) + (typeMap.Length * 4)
                    + operationBytes.Length + 2);
                var synthetic = new List<byte>(syntheticCapacity) { (byte)(0x04 | localWidthCode) };
                Write(synthetic, (ulong)typeCount, localWidth);
                foreach (var typeRow in typeMap)
                {
                    Write(synthetic, (ulong)typeRow, 4);
                }
                Write(synthetic, (ulong)countOperations, localWidth);
                foreach (var item in operationBytes)
                {
                    synthetic.Add(item);
                }
                synthetic.Add(0x83);
                synthetic.Add(0);
                var variableCount = starts[b] + parameters.Length;
                var variables = ImmutableArray.CreateBuilder<BoundVariable>(variableCount);
                for (var value = 0; value < variableCount; value++)
                {
                    variables.Add(new BoundVariable($"value{value + 1}", valueTypes[value], false, default));
                }
                var statements = DecodeBody(CollectionsMarshal.AsSpan(synthetic), variables.Count,
                    variables, noThrow, types, functions);
                var operations = ImmutableArray.CreateBuilder<BoundStatement>(countOperations);
                foreach (var statement in statements)
                {
                    if (statement is BoundLocal local)
                    {
                        if (valueTypes[local.Variable] != local.Initializer.Type)
                        {
                            throw Invalid("Indexed scalar operation result type mismatch.");
                        }
                        operations.Add(local);
                    }
                    else if (statement is BoundExpressionStatement discarded)
                    {
                        operations.Add(discarded);
                    }
                    else if (statement is TrRmScalarDivRem divRem)
                    {
                        if (valueTypes[divRem.QuotientVariable] != divRem.Type
                            || valueTypes[divRem.RemainderVariable] != divRem.Type)
                        {
                            throw Invalid("Indexed CheckedDivRem result type mismatch.");
                        }
                        operations.Add(divRem);
                    }
                }
                if (operations.Count != countOperations)
                {
                    throw Invalid("Indexed scalar operation count mismatch.");
                }
                cursor = terminators[b];
                var opcode = ReadByte(extent, ref cursor);
                var referenceWidth = 1;
                var countWidth = 1;
                var dataWidth = opcode == 0x82 ? 1 : 0;
                if (opcode == 0xF0)
                {
                    var layout = ReadByte(extent, ref cursor);
                    opcode = ReadByte(extent, ref cursor);
                    referenceWidth = 1 << (layout & 3);
                    countWidth = 1 << ((layout >> 2) & 3);
                    dataWidth = opcode == 0x82 ? 1 << ((layout >> 4) & 3) : 0;
                }
                var condition = opcode == 0x81 ? ReadInt(extent, ref cursor, referenceWidth) : 0;
                var maximumReference = condition;
                var maximumCount = 0;
                var maximumBits = 0UL;
                TrRmScalarEdge? whenTrue = null;
                TrRmScalarEdge? whenFalse = null;
                TrRmScalarEdge? switchDefault = null;
                var switchValue = 0;
                ImmutableArray<TrRmScalarSwitchCase> switchCases = default;
                var returned = 0;
                var failureReason = 0;
                if (opcode is 0x80 or 0x81)
                {
                    whenTrue = ReadIndexedEdge(extent, ref cursor, referenceWidth,
                        countWidth, ref maximumReference, ref maximumCount);
                    if (opcode == 0x81)
                    {
                        whenFalse = ReadIndexedEdge(extent, ref cursor, referenceWidth,
                            countWidth, ref maximumReference, ref maximumCount);
                    }
                }
                else if (opcode == 0x82)
                {
                    switchValue = ReadInt(extent, ref cursor, referenceWidth);
                    maximumReference = switchValue;
                    var caseCount = ReadInt(extent, ref cursor, countWidth);
                    maximumCount = caseCount;
                    if (switchValue <= 0 || switchValue > valueTypes.Count)
                    {
                        throw Invalid("Indexed switch selector is outside the value inventory.");
                    }
                    var selectorType = valueTypes[switchValue - 1];
                    if (!IsSwitchType(selectorType))
                    {
                        throw Invalid("Indexed switch selector type is unsupported.");
                    }
                    var cases = ImmutableArray.CreateBuilder<TrRmScalarSwitchCase>(caseCount);
                    ulong previous = 0;
                    for (var caseIndex = 0; caseIndex < caseCount; caseIndex++)
                    {
                        var bits = ReadBits(extent, ref cursor, dataWidth);
                        if (!FitsSwitchBits(selectorType, bits)
                            || caseIndex != 0 && bits <= previous)
                        {
                            throw Invalid("Indexed switch cases are not canonical for their selector.");
                        }
                        previous = bits;
                        maximumBits = Math.Max(maximumBits, bits);
                        var edge = ReadIndexedEdge(extent, ref cursor, referenceWidth,
                            countWidth, ref maximumReference, ref maximumCount);
                        cases.Add(new TrRmScalarSwitchCase(bits, edge));
                    }
                    switchCases = cases.MoveToImmutable();
                    switchDefault = ReadIndexedEdge(extent, ref cursor, referenceWidth,
                        countWidth, ref maximumReference, ref maximumCount);
                }
                else if (opcode == 0x83)
                {
                    returned = ReadInt(extent, ref cursor, referenceWidth);
                    maximumReference = returned;
                }
                else if (opcode == 0x87)
                {
                    failureReason = ReadByte(extent, ref cursor);
                    if (failureReason is < 1 or > 4)
                    {
                        throw Invalid("Indexed failure reason is unsupported.");
                    }
                }
                else
                {
                    throw Invalid($"Indexed terminator 0x{opcode:X2} is not executable.");
                }
                if (cursor != extent.Length || referenceWidth != Width((ulong)maximumReference)
                    || countWidth != Width((ulong)maximumCount)
                    || opcode == 0x82 && dataWidth != Width(maximumBits))
                {
                    throw Invalid("Indexed scalar terminator has trailing bytes.");
                }
                result.Add(new TrRmScalarBlock(parameters, operations.MoveToImmutable(),
                    condition, whenTrue, whenFalse, returned, failureReason, switchValue,
                    switchCases, switchDefault));
            }
            return TrRmScalarGraph.FromAuditedBody(result.MoveToImmutable(),
                valueTypes.MoveToImmutable(), parameterCount, resultType, noThrow, doesNotReturn);
        }

        private static ImmutableArray<int> ReadIndexedList(ReadOnlySpan<byte> body,
            ref int cursor, int width, bool present)
        {
            if (!present)
            {
                return [];
            }
            var count = ReadInt(body, ref cursor, width);
            var result = ImmutableArray.CreateBuilder<int>(count);
            for (var index = 0; index < count; index++)
            {
                result.Add(ReadInt(body, ref cursor, width));
            }
            return result.MoveToImmutable();
        }

        private static TrRmScalarEdge ReadIndexedEdge(ReadOnlySpan<byte> body,
            ref int cursor, int referenceWidth, int countWidth,
            ref int maximumReference, ref int maximumCount)
        {
            var target = ReadInt(body, ref cursor, referenceWidth);
            var count = ReadInt(body, ref cursor, countWidth);
            maximumReference = Math.Max(maximumReference, target);
            maximumCount = Math.Max(maximumCount, count);
            var args = ImmutableArray.CreateBuilder<int>(count);
            for (var index = 0; index < count; index++)
            {
                var argument = ReadInt(body, ref cursor, referenceWidth);
                maximumReference = Math.Max(maximumReference, argument);
                args.Add(argument);
            }
            return new TrRmScalarEdge(target, args.MoveToImmutable());
        }

        private static bool IsSwitchType(PrimitiveType type)
            => type is PrimitiveType.Bool or PrimitiveType.Char16 or PrimitiveType.Int32
                or PrimitiveType.UInt32 or PrimitiveType.Int64 or PrimitiveType.UInt64;

        private static bool FitsSwitchBits(PrimitiveType type, ulong bits)
            => type switch
            {
                PrimitiveType.Bool => bits <= 1,
                PrimitiveType.Char16 => bits <= ushort.MaxValue,
                PrimitiveType.Int32 or PrimitiveType.UInt32 => bits <= uint.MaxValue,
                _ => IsSwitchType(type),
            };

        private IndexedBlock NewBlock(int region)
        {
            var block = new IndexedBlock(_blocks.Count, region);
            _blocks.Add(block);
            return block;
        }

        private int NewValue(PrimitiveType type)
        {
            if (_types.Count >= SemanticVerifier.MaximumVariables)
            {
                throw Unsupported("Native verification supports at most 256 materialized scalar values per method.");
            }
            _types.Add(type);
            return _types.Count;
        }

        private BoundRead Read(int id)
        {
            if (id <= 0 || id > _types.Count)
            {
                throw Unsupported("Read of an uninitialized scalar local.");
            }
            return new BoundRead(id - 1, _types[id - 1], default);
        }

        private int Add(BoundExpression expression)
        {
            var result = expression.Type == PrimitiveType.Void ? 0 : NewValue(expression.Type);
            (_current ?? throw Unsupported("An operation followed a terminal transfer."))
                .Operations.Add((result, expression));
            return result;
        }

        private int Reinterpret(int value, PrimitiveType destination)
            => Add(new TrRmScalarReinterpret(Read(value), destination));

        private int Shift(TokenKind token, int left, int count, PrimitiveType type)
        {
            var unsignedCount = Reinterpret(count, PrimitiveType.UInt32);
            if (token == TokenKind.ShiftRightUnsigned
                && type is PrimitiveType.Int32 or PrimitiveType.Int64)
            {
                var unsignedType = type == PrimitiveType.Int32
                    ? PrimitiveType.UInt32 : PrimitiveType.UInt64;
                var payload = Reinterpret(left, unsignedType);
                var result = Add(new BoundBinary(TokenKind.ShiftRightUnsigned, Read(payload),
                    Read(unsignedCount), unsignedType, default));
                return Reinterpret(result, type);
            }
            return Add(new BoundBinary(token, Read(left), Read(unsignedCount), type, default));
        }

        private int[] Parameters(IndexedBlock block, int[] values, PrimitiveType? result = null)
        {
            var selected = new int[values.Length];
            if (result is PrimitiveType type)
            {
                block.Parameters.Add(NewValue(type));
            }
            for (var index = 0; index < values.Length; index++)
            {
                if (values[index] != 0)
                {
                    selected[index] = NewValue(_function.Variables[index].Type);
                    block.Parameters.Add(selected[index]);
                }
            }
            return selected;
        }

        private static int[] Arguments(int[] slots, int[] visible, int result = 0)
        {
            var argumentCount = result != 0 ? 1 : 0;
            for (var index = 0; index < visible.Length; index++)
            {
                if (visible[index] != 0)
                {
                    if (slots[index] == 0)
                    {
                        throw Unsupported("A target-visible scalar local is uninitialized.");
                    }
                    argumentCount++;
                }
            }

            var arguments = new int[argumentCount];
            var argumentIndex = 0;
            if (result != 0)
            {
                arguments[argumentIndex++] = result;
            }
            for (var index = 0; index < visible.Length; index++)
            {
                if (visible[index] != 0)
                {
                    arguments[argumentIndex++] = slots[index];
                }
            }
            return arguments;
        }

        private void Jump(IndexedBlock target, int[] slots, int[] visible)
        {
            if (_current is null)
            {
                throw Unsupported("Unreachable source transfer cannot be represented in a compact Body.");
            }
            _current.True = new IndexedEdge(target, Arguments(slots, visible));
            _current = null;
        }

        private int Merge(int condition, Func<int> whenTrue, Func<int> whenFalse,
            PrimitiveType? resultType)
        {
            var region = (_current ?? throw Unsupported("A merge followed a terminal transfer.")).Region;
            var trueBlock = NewBlock(region);
            var falseBlock = NewBlock(region);
            _current.Condition = condition;
            _current.True = new IndexedEdge(trueBlock, []);
            _current.False = new IndexedEdge(falseBlock, []);
            var before = (int[])_slots.Clone();

            _current = trueBlock;
            var trueResult = whenTrue();
            var trueEnd = _current;
            var trueSlots = (int[])_slots.Clone();

            _current = falseBlock;
            _slots = (int[])before.Clone();
            var falseResult = whenFalse();
            var falseEnd = _current;
            var falseSlots = (int[])_slots.Clone();
            if (resultType is not null && (trueEnd is null || falseEnd is null))
            {
                throw Unsupported("A value-producing scalar arm must reach its merge.");
            }
            if (trueEnd is null && falseEnd is null)
            {
                return 0;
            }
            for (var index = 0; index < before.Length; index++)
            {
                if (before[index] == 0 && (trueSlots[index] != 0 || falseSlots[index] != 0)
                    || trueEnd is not null && falseEnd is not null
                        && (trueSlots[index] == 0) != (falseSlots[index] == 0))
                {
                    throw Unsupported("Scalar merge cannot introduce an uninitialized local.");
                }
            }

            var join = NewBlock(region);
            _slots = Parameters(join, before, resultType);
            if (trueEnd is not null)
            {
                trueEnd.True = new IndexedEdge(join, Arguments(trueSlots, before, trueResult));
            }
            if (falseEnd is not null)
            {
                falseEnd.True = new IndexedEdge(join, Arguments(falseSlots, before, falseResult));
            }
            _current = join;
            return resultType is null ? 0 : join.Parameters[0];
        }

        private int Expression(BoundExpression expression)
        {
            switch (expression)
            {
                case BoundRead read:
                    if (read.Variable < 0 || read.Variable >= _slots.Length)
                    {
                        throw Unsupported("Invalid scalar local.");
                    }
                    return _slots[read.Variable] != 0 ? _slots[read.Variable]
                        : throw Unsupported("Read of an uninitialized scalar local.");
                case BoundConstant constant:
                    return Add(constant);
                case BoundUnary unary:
                    return Add(new BoundUnary(unary.Operator, Read(Expression(unary.Operand)),
                        unary.Type, default));
                case BoundCast cast:
                    return Add(new BoundCast(Read(Expression(cast.Operand)), cast.Type, default));
                case BoundUpdate { Place: BoundRead place } update
                    when update.Type is PrimitiveType.Int32 or PrimitiveType.UInt32
                        or PrimitiveType.Int64 or PrimitiveType.UInt64:
                    if (!_function.Variables[place.Variable].IsMutable)
                    {
                        throw Unsupported("Indexed update requires a writable local.");
                    }
                    var previous = Expression(place);
                    var one = Add(new BoundConstant(1, update.Type, default));
                    var replacement = Add(new BoundBinary(update.Operator == TokenKind.PlusPlus
                        ? TokenKind.Plus : TokenKind.Minus, Read(previous), Read(one),
                        update.Type, default));
                    _slots[place.Variable] = replacement;
                    return update.IsPrefix ? replacement : previous;
                case BoundCompoundAssignment { Place: BoundRead place } compound
                    when compound.Type is PrimitiveType.Int32 or PrimitiveType.UInt32
                        or PrimitiveType.Int64 or PrimitiveType.UInt64:
                    if (!_function.Variables[place.Variable].IsMutable)
                    {
                        throw Unsupported("Indexed compound assignment requires a writable local.");
                    }
                    var old = Expression(place);
                    var operand = Expression(compound.Right);
                    var operation = CompoundOperators.Binary(compound.Operator);
                    var next = CompoundOperators.IsShift(compound.Operator)
                        ? Shift(operation, old, operand, compound.Type)
                        : Add(new BoundBinary(operation, Read(old), Read(operand),
                            compound.Type, default));
                    _slots[place.Variable] = next;
                    return next;
                case BoundBinary binary when binary.Operator is TokenKind.AmpersandAmpersand
                    or TokenKind.PipePipe:
                    var left = Expression(binary.Left);
                    return binary.Operator == TokenKind.AmpersandAmpersand
                        ? Merge(left, () => Expression(binary.Right), () => left, PrimitiveType.Bool)
                        : Merge(left, () => left, () => Expression(binary.Right), PrimitiveType.Bool);
                case BoundBinary binary:
                    var lhs = Expression(binary.Left);
                    var rhs = Expression(binary.Right);
                    if (binary.Operator is TokenKind.ShiftLeft or TokenKind.ShiftRight
                        or TokenKind.ShiftRightUnsigned)
                    {
                        return Shift(binary.Operator, lhs, rhs, binary.Type);
                    }
                    return Add(new BoundBinary(binary.Operator, Read(lhs), Read(rhs), binary.Type, default));
                case BoundConditional conditional:
                    var condition = Expression(conditional.Condition);
                    return Merge(condition, () => Expression(conditional.WhenTrue),
                        () => Expression(conditional.WhenFalse), conditional.Type);
                case BoundCall call:
                    var arguments = new BoundExpression[call.Arguments.Length];
                    for (var index = 0; index < call.Arguments.Length; index++)
                    {
                        arguments[call.ParameterIndex(index)] = Read(Expression(call.Arguments[index]));
                    }
                    return Add(new BoundCall(call.Function, arguments.ToImmutableArray(), call.Type, default));
                default:
                    throw Unsupported($"Expression {expression.GetType().Name} has no executable indexed scalar encoding.");
            }
        }

        private void Statement(BoundStatement statement)
        {
            if (_current is null)
            {
                if (_function.ReturnType == PrimitiveType.Void
                    && statement is BoundReturn { Value: null })
                {
                    return;
                }
                throw Unsupported("Statements after a transfer cannot be represented.");
            }
            switch (statement)
            {
                case BoundBlock block when block.RetiredManagedLocals.IsEmpty:
                    var visible = (int[])_slots.Clone();
                    foreach (var child in block.Statements)
                    {
                        Statement(child);
                    }
                    for (var index = 0; index < visible.Length; index++)
                    {
                        if (visible[index] == 0)
                        {
                            _slots[index] = 0;
                        }
                    }
                    break;
                case BoundLocal local:
                    var initialValue = Expression(local.Initializer);
                    _slots[local.Variable] = initialValue;
                    break;
                case BoundAssignment assignment:
                    var assignedValue = Expression(assignment.Value);
                    _slots[assignment.Variable] = assignedValue;
                    break;
                case BoundExpressionStatement discarded:
                    _ = Expression(discarded.Expression);
                    if (discarded.Expression is BoundCall call
                        && _program.Functions[call.Function].DoesNotReturn)
                    {
                        (_current ?? throw Unsupported("Non-returning call did not reach its block."))
                            .FailureReason = 1;
                        _current = null;
                    }
                    break;
                case BoundRequire requirement:
                    var requirementCondition = Expression(requirement.Condition);
                    _ = Merge(requirementCondition, () => 0, () =>
                    {
                        (_current ?? throw Unsupported("Requirement failure did not reach its block."))
                            .FailureReason = 1;
                        _current = null;
                        return 0;
                    }, null);
                    break;
                case BoundReturn result:
                    var returned = result.Value is null ? 0 : Expression(result.Value);
                    (_current ?? throw Unsupported("Return expression did not reach its continuation.")).ReturnValue = returned;
                    _current = null;
                    break;
                case BoundIf conditional:
                    var condition = Expression(conditional.Condition);
                    _ = Merge(condition, () => { Statement(conditional.Then); return 0; },
                        () => { if (conditional.Else is not null) { Statement(conditional.Else); } return 0; },
                        null);
                    break;
                case BoundWhile loop:
                    var parent = _current.Region;
                    var header = NewBlock(_regions.Count + 1);
                    header.IsLoopHeader = true;
                    var entrySlots = (int[])_slots.Clone();
                    var loopRegion = new IndexedRegion(_regions.Count + 1, parent, header, entrySlots);
                    _regions.Add(loopRegion);
                    var headerSlots = Parameters(header, entrySlots);
                    Jump(header, entrySlots, entrySlots);
                    _current = header;
                    _slots = headerSlots;
                    IndexedBlock? testBlock = null;
                    int[]? testSlots = null;
                    if (!loop.IsPostTested)
                    {
                        var test = Expression(loop.Condition);
                        testBlock = _current ?? throw Unsupported("Loop condition did not produce a branch.");
                        testSlots = (int[])_slots.Clone();
                        var body = NewBlock(loopRegion.Id);
                        testBlock.Condition = test;
                        testBlock.True = new IndexedEdge(body, []);
                        _current = body;
                        _slots = (int[])testSlots.Clone();
                    }
                    else if (loop.Iterator is not null)
                    {
                        throw Unsupported("Post-tested iterator loops have no executable scalar encoding.");
                    }

                    IndexedBlock? continuation = null;
                    int[]? continuationSlots = null;
                    if (loop.IsPostTested || loop.Iterator is not null)
                    {
                        continuation = NewBlock(loopRegion.Id);
                        loopRegion.ContinueTarget = continuation;
                        continuationSlots = Parameters(continuation, entrySlots);
                    }
                    _loops.Push(loopRegion);
                    Statement(loop.Body);
                    if (_current is not null)
                    {
                        loopRegion.HasSourceContinuation = true;
                        Jump(continuation ?? header, _slots, entrySlots);
                    }
                    if (continuation is not null && !loopRegion.HasSourceContinuation)
                    {
                        throw Unsupported("A scalar loop iterator or post-test requires a reachable body continuation.");
                    }
                    if (loop.Iterator is not null)
                    {
                        _current = continuation;
                        _slots = (int[])(continuationSlots ?? throw Unsupported("Loop iterator target is missing.")).Clone();
                        Statement(loop.Iterator);
                        if (_current is not null)
                        {
                            Jump(header, _slots, entrySlots);
                        }
                    }
                    _loops.Pop();
                    if (loop.IsPostTested)
                    {
                        _current = continuation;
                        _slots = (int[])(continuationSlots ?? throw Unsupported("Post-tested condition target is missing.")).Clone();
                    }
                    var postCondition = loop.IsPostTested ? Expression(loop.Condition) : 0;
                    var exit = NewBlock(parent);
                    loopRegion.Exit = exit;
                    var exitSlots = Parameters(exit, entrySlots,
                        loopRegion.Transfers.Count == 0 ? null : PrimitiveType.Int32);
                    var normalExitPossible = loop.Condition is not BoundConstant {
                        Type: PrimitiveType.Bool, Value: not 0,
                    } || loopRegion.ExitEdges.Any(static edge => edge.Code == 0);
                    if (loop.IsPostTested)
                    {
                        var conditionBlock = _current ?? throw Unsupported("Post-tested condition did not produce a branch.");
                        var conditionSlots = (int[])_slots.Clone();
                        conditionBlock.Condition = postCondition;
                        conditionBlock.True = new IndexedEdge(header, Arguments(conditionSlots, entrySlots));
                        conditionBlock.False = new IndexedEdge(exit, Arguments(conditionSlots, entrySlots));
                        loopRegion.ExitEdges.Add((conditionBlock, conditionBlock.False, 0));
                    }
                    else
                    {
                        var preTest = testBlock ?? throw Unsupported("Pre-tested condition block is missing.");
                        var falseEdge = new IndexedEdge(exit, Arguments(testSlots
                            ?? throw Unsupported("Pre-tested condition slots are missing."), entrySlots));
                        preTest.False = falseEdge;
                        loopRegion.ExitEdges.Add((preTest, falseEdge, 0));
                    }
                    foreach (var block in _blocks)
                    {
                        if (block.True is { Target: var target } && target == header
                            && block != header && block.True.Arguments.Length != header.Parameters.Count)
                        {
                            throw Unsupported("Loop-carried scalar inventory changed across an edge.");
                        }
                    }
                    _current = exit;
                    _slots = exitSlots;
                    if (loopRegion.Transfers.Count != 0)
                    {
                        // Every edge leaves exactly one region. Carry pending source transfers
                        // as ordinary scalar data until the selected enclosing loop is reached.
                        foreach (var (source, edge, code) in loopRegion.ExitEdges)
                        {
                            var value = NewValue(PrimitiveType.Int32);
                            source.Operations.Add((value, new BoundConstant(code, PrimitiveType.Int32, default)));
                            edge.Arguments = [value, .. edge.Arguments];
                        }
                        var remainingTransfers = loopRegion.Transfers.Count;
                        foreach (var (code, transfer) in loopRegion.Transfers)
                        {
                            // An always-true loop without a source break has no normal
                            // continuation. Do not manufacture one from routing state zero.
                            if (--remainingTransfers == 0 && !normalExitPossible)
                            {
                                Transfer(transfer.Target, transfer.Continue);
                                break;
                            }
                            var value = Add(new BoundConstant(code, PrimitiveType.Int32, default));
                            var pending = Add(new BoundBinary(TokenKind.EqualsEquals,
                                Read(exit.Parameters[0]), Read(value), PrimitiveType.Bool, default));
                            _ = Merge(pending, () => {
                                Transfer(transfer.Target, transfer.Continue);
                                return 0;
                            }, () => 0, null);
                        }
                    }
                    break;
                case BoundBreak transfer:
                    Transfer(TransferTarget(transfer.TargetDepth), false);
                    break;
                case BoundContinue transfer:
                    Transfer(TransferTarget(transfer.TargetDepth), true);
                    break;
                default:
                    throw Unsupported($"Statement {statement.GetType().Name} has no executable indexed scalar encoding.");
            }
        }

        private readonly List<(IndexedRegion Region, IndexedEdge Edge)> _pendingBreaks = [];

        private IndexedRegion TransferTarget(int depth)
        {
            if (depth < 1 || depth > _loops.Count)
            {
                throw Unsupported("Invalid enclosing scalar loop transfer depth.");
            }
            return depth == 1 ? _loops.Peek() : _loops.ElementAt(depth - 1);
        }

        private void Transfer(IndexedRegion target, bool isContinue)
        {
            var region = _loops.Peek();
            if (region == target && isContinue)
            {
                region.HasSourceContinuation = true;
                Jump(region.ContinueTarget, _slots, region.VisibleSlots);
                return;
            }
            var code = 0;
            if (region != target)
            {
                code = target.Id * 2 + (isContinue ? 1 : 0);
                region.Transfers.TryAdd(code, (target, isContinue));
            }
            var source = _current ?? throw Unsupported("Unreachable source transfer.");
            var deferred = new IndexedBlock(-1, region.Parent);
            // Visibility selects membership only; the edge carries the values at this
            // transfer, including outer writes, not the loop-entry definitions.
            Jump(deferred, _slots, region.VisibleSlots);
            var edge = source.True ?? throw Unsupported("Missing scalar transfer edge.");
            region.ExitEdges.Add((source, edge, code));
            _pendingBreaks.Add((region, edge));
        }

        internal byte[] Encode()
        {
            Statement(_function.Body);
            foreach (var block in _blocks)
            {
                if (_function.IsNoThrow && block.FailureReason != 0
                    && (block.Operations.Count == 0
                        || block.Operations[^1].Expression is not BoundCall call
                        || !_program.Functions[call.Function].DoesNotReturn))
                {
                    throw Unsupported("NoThrow indexed Body contains an unproved failure effect.");
                }
                if (block.True?.Target is { IsLoopHeader: true } header
                    && block.Region == header.Region && block != header)
                {
                    _regions[block.Region - 1].HasHeaderBackedge = true;
                }
            }
            if (_current is not null)
            {
                if (!_function.DoesNotReturn || _function.ReturnType != PrimitiveType.Void)
                {
                    throw Unsupported("Indexed scalar Body requires a terminal return.");
                }
                // The DNR audit proves this structurally required continuation unreachable.
                _current.ReturnValue = 0;
                _current = null;
            }
            foreach (var (region, edge) in _pendingBreaks)
            {
                edge.Target = region.Exit ?? throw Unsupported("Loop exit is missing.");
            }
            foreach (var region in _regions)
            {
                if (!region.HasSourceContinuation || !region.HasHeaderBackedge)
                {
                    throw Unsupported("An indexed loop requires a reachable header backedge.");
                }
            }
            return EncodeIndexed(_function, _blocks, _regions, _types, _program, _hasFloatingTypes);
        }
    }
}
