// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Terrum.CodeAnalysis.Text;

namespace Terrum.CodeAnalysis.Lexing;

/// <summary>Bounds the provisional, in-process scalar frontend result projection.</summary>
public static partial class ScalarFrontendHost
{
    public const int WordCount = 392;
    public const int BufferIndex = 10;

    private static int StatementKind(int kind) => kind switch {
        16 => 3, 17 => 11, 18 => 12, 19 => 6, 20 => 7, _ => kind,
    };

    public static ImmutableArray<HostBuffer> CreateBuffers(SourceText source)
    {
        var buffers = ScannerHost.CreateBuffers(source);
        if (buffers.Length != BufferIndex)
        {
            throw new InvalidOperationException("The scanner host buffer layout changed.");
        }
        return buffers.Add(new HostBuffer("Frontend", PrimitiveType.Int32,
            [.. new int[WordCount]], true))
            .Add(new HostBuffer("FrontendProgram", PrimitiveType.Int32,
                [.. new int[ProgramWordCount]], true));
    }

    public static ScalarFrontendResult ReadResult(SourceText source, ReadOnlySpan<byte> bytes)
        => ReadResult(source, bytes, BoundRuntimePolicy.FailFast);

    public static ScalarFrontendResult ReadResult(SourceText source, ReadOnlySpan<byte> bytes,
        BoundRuntimePolicy policy)
        => ReadResultCore(source, bytes, policy, null);

    private static ScalarFrontendResult ReadResultCore(SourceText source, ReadOnlySpan<byte> bytes,
        BoundRuntimePolicy policy, ProgramReadContext? context)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (policy is not (BoundRuntimePolicy.FailFast or BoundRuntimePolicy.CheckedExceptions))
        {
            throw InvalidResult();
        }
        if (bytes.Length != WordCount * sizeof(int))
        {
            throw InvalidResult();
        }

        var words = new int[WordCount];
        for (var index = 0; index < words.Length; index++)
        {
            words[index] = BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(index * 4, 4));
        }
        if (words[0] != 85)
        {
            throw InvalidResult();
        }
        // Both layouts are currently produced. Complete-program frames always
        // contain trees; single-function publication selects its populated layout.
        var isTree = context is not null
            || words[5] == 0 || words[37] != 0 || words[298] != 0;
        var allowInitRefArguments = context is not null
            || isTree && words[17] != 0;

        if (isTree && source.Length > ScannerHost.MaximumSourceLength
            || words[1] is not (1 or 2))
        {
            throw InvalidResult();
        }

        var errorSpan = Span(3);
        if (words[1] == 2)
        {
            if (words[2] is < 1 or > 4 || !ValidSpan(errorSpan)
                || !AllZero(5, WordCount))
            {
                throw InvalidResult();
            }
            return new ScalarFrontendResult(false, false, words[2], errorSpan,
                0, 0, 0, 0, 0, default, default, default, default,
                false, false, default, default, default);
        }

        var name = Span(9);
        if (context is null && SyntaxTree.Parse(source).HasErrors)
        {
            throw InvalidResult();
        }
        var aliases = context?.Aliases ?? InventoryScalarAliases(source);
        var firstName = Span(11);
        var secondName = Span(13);
        var thirdName = Span(35);
        var count = words[6];
        var effects = isTree ? words[2] & 6 : 0;
        var isPublic = (words[2] & 1) != 0;
        if (words[2] < 0 || words[2] > (isTree ? 15 : 1)
            || !AllZero(3, 5)
            || words[5] != 0 && !ScalarType(words[5])
            || (effects & 4) != 0 && words[5] != 0
            || count is < 0 or > 3
            || !isTree && !AllZero(37, WordCount)
            || (count == 0 ? words[7] != 0 : !ScalarType(words[7]))
            || (count <= 1 ? words[8] != 0 : !ScalarType(words[8]))
            || (count < 3 ? words[34] != 0 : !ScalarType(words[34]))
            || !ValidSpan(name, nonempty: true)
            || (count == 0 ? firstName != default : !ValidSpan(firstName, nonempty: true))
            || (count < 2 ? secondName != default : !ValidSpan(secondName, nonempty: true))
            || (count < 3 ? thirdName != default : !ValidSpan(thirdName, nonempty: true))
            || !ValidHeader()
            || words[15] is not (0 or 1 or 2))
        {
            throw InvalidResult();
        }

        if (isTree)
        {
            return ReadTree();
        }

        var condition = ValidateFloatingLiteral(Expression(16));
        var whenTrue = ValidateFloatingLiteral(Expression(22));
        var whenFalse = ValidateFloatingLiteral(Expression(28));
        if (!ValidExpression(whenTrue, words[5], count)
            || (words[15] == 0
                ? !AllZero(16, 22) || !AllZero(28, 34)
                : !ValidExpression(condition, 5, count)
                    || !ValidExpression(whenFalse, words[5], count))
            || !ValidBody())
        {
            throw InvalidResult();
        }

        return new ScalarFrontendResult(true, isPublic, 0, default, words[5], count,
            words[7], words[8], words[34], name, firstName, secondName, thirdName,
            words[15] != 0, words[15] == 2, condition, whenTrue, whenFalse);

        TextSpan Span(int offset)
        {
            var start = words[offset];
            var length = words[offset + 1];
            if (start < 0 || start > source.Length || length < 0
                || length > source.Length - start)
            {
                throw InvalidResult();
            }
            return new TextSpan(start, length);
        }

        ScalarFrontendExpression Expression(int offset) => new(
            words[offset], words[offset + 1], words[offset + 2], words[offset + 3],
            Span(offset + 4));

        ScalarFrontendExpression ValidateFloatingLiteral(ScalarFrontendExpression expression)
        {
            if (expression.Operation == 3 && expression.Type is 7 or 8)
            {
                var span = NumericSpan(source, expression.Span);
                if (!FloatingProjectionMatches(source.Text.AsSpan(span.Start, span.Length),
                    expression.Type, expression.LeftParameter, expression.RightParameter))
                {
                    throw InvalidResult();
                }
            }
            return expression;
        }

        ScalarFrontendResult ReadTree()
        {
            var voidArrow = words[5] == 0 && words[41] == 1;
            if (!AllZero(allowInitRefArguments ? 18 : 17, 34)
                || words[16] < 0
                || (words[16] & ~((1 << count) - 1)) != 0
                || words[17] < 0
                || (words[17] & ~((1 << count) - 1)) != 0
                || (words[16] & words[17]) != 0
                || words[37] is < 0 or > 32
                || words[41] is not (0 or 1)
                || (words[41] == 1 && words[15] != 0)
                || !AllZero(42 + words[37] * 8, 298)
                || words[298] is < 0 or > 12
                || words[299] is < 0 or > 8
                || words[41] != 0 && (voidArrow ? words[298] != 1
                    : words[298] != 0 || words[299] != 0)
                || !AllZero(300 + words[299] * 4, 332)
                || !AllZero(332 + words[298] * 5, WordCount))
            {
                throw InvalidResult();
            }
            var locals = ImmutableArray.CreateBuilder<ScalarFrontendLocal>(words[299]);
            for (var i = 0; i < words[299]; i++)
            {
                var offset = 300 + i * 4;
                var flags = words[offset + 1];
                var local = new ScalarFrontendLocal(words[offset], (flags & 1) != 0,
                    Span(offset + 2)) { IsInitialization = (flags & 2) != 0, IsConstant = (flags & 4) != 0 };
                if (!ScalarType(local.Type) || flags is not (0 or 1 or 2 or 4 or 8)
                    || !ValidSpan(local.Name, nonempty: true))
                {
                    throw InvalidResult();
                }
                local = local with { IsSwitchSelector = flags == 8 };
                locals.Add(local);
            }
            var declaredLocals = locals.MoveToImmutable();
            var statements = ImmutableArray.CreateBuilder<ScalarFrontendStatement>(words[298]);
            for (var i = 0; i < words[298]; i++)
            {
                var offset = 332 + i * 5;
                var statement = new ScalarFrontendStatement(words[offset], words[offset + 1],
                    words[offset + 2], Span(offset + 3));
                var kind = StatementKind(statement.Kind);
                if (voidArrow && kind is not (15 or 21))
                {
                    throw InvalidResult();
                }
                if (kind is < 1 or > 39 or >= 16 and <= 20
                    || (kind is 3 or 4 or 5 or 11 or 12 or 14 or 23 or 29 or 30 or 31 or 32 or 33 or 34 or 38 or 39
                        ? statement.Slot <= i || statement.Slot > words[298]
                        : kind is 6 or 7
                            ? statement.Slot < 0 || statement.Slot >= i
                            : kind is 8 or 10 or 13 or 21 or 22 or 35 or 36 or 37
                                ? statement.Slot != 0
                            : kind is 2 or 9
                                ? statement.Slot <= count
                                    ? statement.Slot < 1
                                        || ((kind == 2 ? words[16] | words[17] : words[16])
                                            & (1 << (statement.Slot - 1))) == 0
                                    : statement.Slot > count + declaredLocals.Length
                                : statement.Slot <= count || statement.Slot > count + declaredLocals.Length)
                    || (kind is 5 or 6 or 7 or 13 or 14 or 22 or 23 or 24 or 25 or 26 or 29 or 30 or 31 or 32 or 33 or 34 ? statement.Root != -1
                        : kind == 39 ? statement.Root < -1 || statement.Root >= words[37]
                        : kind == 12 ? statement.Root < -1 || statement.Root >= words[37]
                        : statement.Root < 0 || statement.Root >= words[37])
                    || !ValidSpan(statement.Span, nonempty: true))
                {
                    throw InvalidResult();
                }
                statements.Add(statement);
            }
            var orderedStatements = statements.MoveToImmutable();
            var bodySpans = new TextSpan[orderedStatements.Length];
            var loopSpans = new TextSpan[orderedStatements.Length];
            var nodes = ImmutableArray.CreateBuilder<ScalarFrontendNode>(words[37]);
            var depths = new int[words[37]];
            var intrinsicTypes = new int[words[37]];
            for (var i = 0; i < words[37]; i++)
            {
                var offset = 42 + i * 8;
                var node = new ScalarFrontendNode(words[offset], words[offset + 1],
                    Span(offset + 2), words[offset + 4], words[offset + 5],
                    words[offset + 6], words[offset + 7]);
                if (node.Operation == 3 && node.Type is 7 or 8
                    && !FloatingProjectionMatches(source.Text.AsSpan(
                        node.Span.Start, node.Span.Length), node.Type, node.Low, node.High))
                {
                    throw InvalidResult();
                }
                var call = node.Operation == 102 || node.Operation == 105 && context is not null;
                var callee = node.Operation == 105 ? context?.Callee(i) : null;
                var callCount = callee?.ParameterTypes.Length ?? count;
                if (call && (node.High & 0x100) != 0)
                {
                    if ((node.High & ~0x1FF) != 0
                        || (node.High & 3) != callCount || callCount == 0)
                    {
                        throw InvalidResult();
                    }
                    var mappedParameters = ImmutableArray.CreateBuilder<int>(callCount);
                    var assignedParameters = new bool[callCount];
                    for (var ordinal = 0; ordinal < callCount; ordinal++)
                    {
                        var parameter = (node.High >> (2 + (ordinal * 2))) & 3;
                        if (parameter >= callCount || assignedParameters[parameter])
                        {
                            throw InvalidResult();
                        }
                        assignedParameters[parameter] = true;
                        mappedParameters.Add(parameter);
                    }
                    node = node with {
                        High = callCount,
                        ArgumentParameterIndices = mappedParameters.MoveToImmutable(),
                    };
                }
                var unary = node.Operation is 46 or 62 or 63 or 83 or 86 or 87 or 109 or 110
                    || node.Operation is 57 or 58 && node.Right == -1;
                var output = node.Operation is 103 or 104 or 107 or 108;
                var outputDeclaration = node.Operation is 104 or 108;
                var initOutput = node.Operation is 107 or 108;
                var leaf = node.Operation is 0 or 3 or 20 or 21 || output;
                if ((!ScalarType(node.Type)
                        && !(context is null && words[5] == 0 && call && node.Type == 0)
                        && !(context is not null && node.Operation == 105 && node.Type == 0))
                    || !ValidSpan(node.Span, nonempty: true)
                    || (!leaf && !unary && !call && node.Operation is not (>= 57 and <= 61
                        or >= 65 and <= 75 or 84 or >= 88 and <= 101))
                    || (node.Operation == 102 && context is not null)
                    || (node.Operation == 109 && node.Type != BooleanTypeCode)
                    || (call && (node.Type != (callee?.ReturnType ?? words[5])
                        || callee is { ReturnType: 0 }
                            && !orderedStatements.Any(entry => entry.Kind == 37 && entry.Root == i)
                        || node.High != callCount
                        || (callCount == 0 && (node.Left != -1 || node.Right != -1 || node.Low != -1))
                        || (callCount > 0 && (node.Left < 0 || node.Left >= i))
                        || (callCount < 2 && node.Right != -1)
                        || (callCount >= 2 && (node.Right < 0 || node.Right >= i))
                        || (callCount < 3 && node.Low != -1)
                        || (callCount == 3 && (node.Low < 0 || node.Low >= i))))
                    || (output && (initOutput && !allowInitRefArguments
                        || node.Left < 1 || outputDeclaration && node.Left <= count
                        || node.Left > count + declaredLocals.Length
                        || node.Low != 0 || node.High != 0
                        || node.Type != (node.Left <= count
                            ? ParameterType(node.Left) : declaredLocals[node.Left - count - 1].Type)))
                    || (leaf && node.Right != -1)
                    || (unary && node.Right != -1)
                    || (!leaf && !call && (node.Left < 0 || node.Left >= i
                        || (!unary && (node.Right < 0 || node.Right >= i))))
                    || (node.Operation != 3 && node.Operation != 84
                        && node.Operation is not (86 or 87)
                        && !call && !output
                        && (node.Low != 0 || node.High != 0))
                    || (node.Operation is 86 or 87
                        && (node.Low is not (0 or 1) || node.High != 0))
                    || (node.Operation is >= 91 and <= 101
                        && orderedStatements.Count(entry => entry.Kind == 9 && entry.Root == i) != 1)
                    || (node.Operation == 84 && (node.Low < 0 || node.Low >= i
                        || node.High != 0))
                    || (node.Operation == 0 && (node.Left < 1
                        || node.Left > count + declaredLocals.Length
                        || node.Type != (node.Left <= count
                            ? ParameterType(node.Left) : declaredLocals[node.Left - count - 1].Type)))
                    || (node.Operation is 20 or 21 && (node.Left != -1 || node.Type != 5))
                    || (node.Operation == 3 && (node.Left != -1 || node.Type == 5)))
                {
                    throw InvalidResult();
                }
                if (call)
                {
                    var arguments = new[] { node.Left, node.Right, node.Low };
                    for (var ordinal = 0; ordinal < callCount; ordinal++)
                    {
                        var parameter = node.ArgumentParameterIndices.IsDefault
                            ? ordinal : node.ArgumentParameterIndices[ordinal];
                        var argument = nodes[arguments[ordinal]];
                        var expectsOut = callee is not null
                            ? (callee.OutParameterMask & (1 << parameter)) != 0
                            : (words[16] & (1 << parameter)) != 0;
                        var expectsInitRef = callee is not null
                            ? (callee.InitRefParameterMask & (1 << parameter)) != 0
                            : (words[17] & (1 << parameter)) != 0;
                        if (argument.Type != (callee?.ParameterTypes[parameter] ?? ParameterType(parameter + 1))
                            || expectsOut != (argument.Operation is 103 or 104)
                            || expectsInitRef != (argument.Operation is 107 or 108))
                        {
                            throw InvalidResult();
                        }
                        depths[i] = Math.Max(depths[i], depths[arguments[ordinal]] + 1);
                    }
                    if (depths[i] > 32)
                    {
                        throw InvalidResult();
                    }
                }
                else if (!leaf)
                {
                    var operand = nodes[node.Left];
                    depths[i] = depths[node.Left] + 1;
                    if (unary)
                    {
                        if ((node.Operation is not (83 or 109) && node.Type != operand.Type)
                            || node.Operation == 109
                                && (node.Type != BooleanTypeCode || operand.Operation != 0)
                            || node.Operation == 83 && (!SupportedScalarConversion(operand.Type, node.Type)
                                || intrinsicTypes[node.Left] == 0 && node.Type != operand.Type)
                            || node.Operation is not (46 or 83 or 110)
                                && node.Operation is not (86 or 87)
                                && operand.Operation is not (102 or 105) && operand.Right != -1
                            || node.Operation is 86 or 87 && (node.Type is < 1 or > 4
                                || !UpdatePlace(node.Left))
                            || (node.Operation == 62 && node.Type != 5)
                            || (node.Operation == 63 && node.Type is not (>= 1 and <= 4))
                            || (node.Operation is 57 or 58
                                && (node.Type is not (>= 1 and <= 4 or 7 or 8)
                                    || node.Operation == 58 && node.Type is not (1 or 3 or 7 or 8))))
                        {
                            throw InvalidResult();
                        }
                    }
                    else if (node.Operation == 84)
                    {
                        var whenTrue = nodes[node.Right];
                        var whenFalse = nodes[node.Low];
                        depths[i] = Math.Max(depths[i], Math.Max(depths[node.Right] + 1,
                            depths[node.Low] + 1));
                        if (operand.Type != 5 || node.Type != whenTrue.Type
                            || node.Type != whenFalse.Type)
                        {
                            throw InvalidResult();
                        }
                    }
                    else
                    {
                        var rhs = nodes[node.Right];
                        depths[i] = Math.Max(depths[i], depths[node.Right] + 1);
                        var comparison = node.Operation is >= 65 and <= 70;
                        var logical = node.Operation is 72 or 74;
                        var bitwise = node.Operation is 71 or 73 or 75;
                        var shift = node.Operation is >= 88 and <= 90 or >= 99 and <= 101;
                        var compound = node.Operation is >= 91 and <= 101;
                        if ((shift ? rhs.Type != 1 : operand.Type != rhs.Type)
                            || node.Type != (comparison ? 5 : operand.Type)
                            || compound && !UpdatePlace(node.Left)
                            || compound && node.Type is < 1 or > 4
                            || shift && operand.Type is < 1 or > 4
                            || (comparison && intrinsicTypes[node.Left] == 0
                                && intrinsicTypes[node.Right] == 0)
                            || (logical && operand.Type != 5)
                            || (comparison && (operand.Type == 6 || operand.Type == 5)
                                && node.Operation >= 67)
                            || (bitwise && operand.Type == 6)
                            || (!logical && !bitwise && !comparison
                                && operand.Type is not (>= 1 and <= 4)
                                && !((operand.Type is 7 or 8) && node.Operation is >= 57 and <= 60)))
                        {
                            throw InvalidResult();
                        }
                    }
                    if (depths[i] > 32)
                    {
                        throw InvalidResult();
                    }
                }
                // Comparison results and conditional predicates cannot establish numeric operand context.
                intrinsicTypes[i] = node.Operation switch {
                    0 or 20 or 21 or 62 or 83 or 86 or 87 or 102 or 103 or 104 or 110
                        or 105 or 107 or 108 or 109
                        or >= 65 and <= 70 or 72 or 74 => node.Type,
                    3 => 0,
                    >= 88 and <= 90 => intrinsicTypes[node.Left] != 0
                        ? intrinsicTypes[node.Left] : intrinsicTypes[node.Right],
                    84 => intrinsicTypes[node.Right] != 0
                        ? intrinsicTypes[node.Right] : intrinsicTypes[node.Low],
                    _ => intrinsicTypes[node.Left] != 0 || node.Right < 0
                        ? intrinsicTypes[node.Left] : intrinsicTypes[node.Right],
                };
                nodes.Add(node);
            }
            var hasInitializationLocals = declaredLocals.Any(static local => local.IsInitialization);
            var hasVacantDeclarations = orderedStatements.Any(static entry => entry.Kind is 25 or 26);
            var hasConstantLocals = declaredLocals.Any(static local => local.IsConstant);
            var hasConstantDeclarations = orderedStatements.Any(static entry => entry.Kind is 27 or 28);
            if (hasInitializationLocals != hasVacantDeclarations
                || hasConstantLocals != hasConstantDeclarations)
            {
                throw InvalidResult();
            }
            var tree = nodes.MoveToImmutable();
            var hasDoesNotReturnCallStatement = context is not null
                && orderedStatements.Any(statement => statement.Kind == 37
                    && tree[statement.Root].Operation == 105
                    && (context.Callee(statement.Root).Effects & 4) != 0);
            var conditionRoot = words[38];
            var trueRoot = words[39];
            var falseRoot = words[40];
            var structuredBody = trueRoot == -1
                || orderedStatements.Any(static entry => entry.Kind == 8);
            // Braced declaration-effect bodies publish source-ordered statements,
            // not a terminal return envelope.
            if (effects != 0 && words[41] == 0 && !structuredBody)
            {
                throw InvalidResult();
            }
            if (structuredBody && words[5] != 0
                && !orderedStatements.Any(static entry => entry.Kind is 8 or 11 or 12 or 17 or 18 or 21)
                && !hasDoesNotReturnCallStatement)
            {
                throw InvalidResult();
            }
            if (structuredBody
                    ? words[15] != 0 || words[41] != (voidArrow ? 1 : 0)
                        || conditionRoot != -1 || trueRoot != -1 || falseRoot != -1
                    : trueRoot is < 0 or >= 32 || trueRoot >= tree.Length
                        || tree[trueRoot].Type != words[5]
                        || (words[15] == 0
                    ? conditionRoot != -1 || falseRoot != -1
                    : conditionRoot < 0 || conditionRoot >= tree.Length
                        || tree[conditionRoot].Type != 5 || falseRoot < 0
                        || falseRoot >= tree.Length || tree[falseRoot].Type != words[5]))
            {
                throw InvalidResult();
            }
            var lexical = Lexer.Lex(source);
            var tokens = lexical.Tokens;
            if (tokens.Length > 64)
            {
                throw InvalidResult();
            }
            var at = 0;
            var used = new bool[tree.Length];
            var activeLocals = 0;
            var visibleLocals = 0;
            var statementCursor = 0;
            var activeLoops = new System.Collections.Generic.List<(int Header, string? Label)>();
            var seenNames = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
            var switchLocalForStatement = new int[orderedStatements.Length];
            Array.Fill(switchLocalForStatement, -1);
            if (!TakeDeclarationEffects(tokens, ref at, effects)
                || !TakeCallableModifiers(tokens, ref at, isPublic, true,
                    (words[2] & 8) != 0))
            {
                throw InvalidResult();
            }
            if (!lexical.Diagnostics.IsEmpty
                || !Take(words[5] == 0 ? TokenKind.VoidKeyword : TokenKind.Identifier)
                || !Take(TokenKind.Identifier)
                || !Take(TokenKind.OpenParen))
            {
                throw InvalidResult();
            }
            for (var parameter = 0; parameter < count; parameter++)
            {
                var isOut = (words[16] & (1 << parameter)) != 0;
                var isInitRef = allowInitRefArguments
                    && (words[17] & (1 << parameter)) != 0;
                if (parameter != 0 && !Take(TokenKind.Comma)
                    || isOut && !Take(TokenKind.OutKeyword)
                    || isInitRef && (!Take(TokenKind.Identifier) || tokens[at - 1].Text != "init"
                        || !Take(TokenKind.RefKeyword) || !Take(TokenKind.Less))
                    || !Take(TokenKind.Identifier)
                    || isInitRef && !Take(TokenKind.Greater)
                    || !Take(TokenKind.Identifier))
                {
                    throw InvalidResult();
                }
            }
            if (!Take(TokenKind.CloseParen))
            {
                throw InvalidResult();
            }
            if (count > 0) { seenNames.Add(SourceName(source, firstName)); }
            if (count > 1) { seenNames.Add(SourceName(source, secondName)); }
            if (count > 2) { seenNames.Add(SourceName(source, thirdName)); }
            if (words[41] == 1)
            {
                if (!Take(TokenKind.Arrow))
                {
                    throw InvalidResult();
                }
                if (voidArrow)
                {
                    TakeStatements(orderedStatements.Length, 0, -1);
                    if (activeLocals != declaredLocals.Length)
                    {
                        throw InvalidResult();
                    }
                }
                else if (!TakeTree(trueRoot) || !Take(TokenKind.Semicolon))
                {
                    throw InvalidResult();
                }
            }
            else
            {
                if (!Take(TokenKind.OpenBrace))
                {
                    throw InvalidResult();
                }
                TakeStatements(orderedStatements.Length, 0, -1);
                if (words[15] != 0 && (!Take(TokenKind.IfKeyword)
                    || !Take(TokenKind.OpenParen) || !TakeTree(conditionRoot)
                    || !Take(TokenKind.CloseParen) || !Take(TokenKind.OpenBrace)))
                {
                    throw InvalidResult();
                }
                if (!structuredBody
                    && (!Take(TokenKind.ReturnKeyword) || !TakeTree(trueRoot)
                    || !Take(TokenKind.Semicolon)))
                {
                    throw InvalidResult();
                }
                if (words[15] != 0)
                {
                    if (!Take(TokenKind.CloseBrace)
                        || (words[15] == 1 && (!Take(TokenKind.ElseKeyword)
                            || !Take(TokenKind.OpenBrace)))
                        || !Take(TokenKind.ReturnKeyword) || !TakeTree(falseRoot)
                        || !Take(TokenKind.Semicolon)
                        || (words[15] == 1 && !Take(TokenKind.CloseBrace)))
                    {
                        throw InvalidResult();
                    }
                }
                if (activeLocals != declaredLocals.Length)
                {
                    throw InvalidResult();
                }
                if (!Take(TokenKind.CloseBrace))
                {
                    throw InvalidResult();
                }
            }
            if (!Take(TokenKind.EndOfFile) || at != tokens.Length)
            {
                throw InvalidResult();
            }
            if (used.Any(seen => !seen)
                || declaredLocals.Count(static local => local.IsSwitchSelector)
                    != switchLocalForStatement.Count(static local => local >= 0))
            {
                throw InvalidResult();
            }
            var resultStatements = ImmutableArray.CreateBuilder<ScalarFrontendStatement>(orderedStatements.Length);
            for (var index = 0; index < orderedStatements.Length; index++)
            {
                var entry = orderedStatements[index];
                resultStatements.Add(entry with
                {
                    BodySpan = bodySpans[index],
                    LoopSpan = loopSpans[index],
                    SwitchLocal = switchLocalForStatement[index],
                });
            }
            var projection = new ScalarFrontendResult(true, isPublic, 0, default,
                words[5], count, words[7], words[8], words[34], name,
                firstName, secondName, thirdName, words[15] != 0, words[15] == 2,
                Root(conditionRoot), Root(trueRoot), Root(falseRoot));
            projection = projection with { Nodes = tree, ConditionRoot = conditionRoot,
                TrueRoot = trueRoot, FalseRoot = falseRoot, IsExpressionBody = words[41] == 1,
                IsNoThrow = (effects & 2) != 0, DoesNotReturn = (effects & 4) != 0,
                RequiresUnsafeCaller = (words[2] & 8) != 0,
                OutParameterMask = words[16],
                InitRefParameterMask = words[17],
                CallTargets = context?.Targets ?? default,
                Locals = declaredLocals,
                Statements = resultStatements.MoveToImmutable(),
            };
            if (context is null)
            {
                // Structured returns have no terminal envelope: check every path before publishing.
                BindLibraryCore(source, projection, policy);
            }
            return projection;

            ScalarFrontendExpression Root(int index) => index == -1
                ? default : new ScalarFrontendExpression(tree[index].Type,
                    tree[index].Operation, tree[index].Left, tree[index].Right,
                    tree[index].Span);

            bool ContainsRead(int index)
            {
                var node = tree[index];
                if (node.Operation is 103 or 104)
                {
                    return false;
                }
                if (node.Operation is 102 or 105)
                {
                    return node.Left >= 0 && ContainsRead(node.Left)
                        || node.Right >= 0 && ContainsRead(node.Right)
                        || node.Low >= 0 && ContainsRead(node.Low);
                }
                return node.Operation == 0
                    || node.Left >= 0 && ContainsRead(node.Left)
                    || node.Right >= 0 && ContainsRead(node.Right)
                    || node.Operation == 84 && ContainsRead(node.Low);
            }

            bool Take(TokenKind kind)
            {
                if (at >= tokens.Length || tokens[at].Kind != kind)
                {
                    return false;
                }
                at++;
                return true;
            }

            bool Visible(int slot)
                => slot <= count || (slot <= count + activeLocals
                    && (visibleLocals & (1 << (slot - count - 1))) != 0);

            bool VisibleName(string spelling)
            {
                for (var i = 0; i < activeLocals; i++)
                {
                    if ((visibleLocals & (1 << i)) != 0
                        && SourceName(source, declaredLocals[i].Name) == spelling)
                    {
                        return true;
                    }
                }
                return false;
            }

            bool ParameterNamed(string spelling)
                => count > 0 && SourceName(source, firstName) == spelling
                    || count > 1 && SourceName(source, secondName) == spelling
                    || count > 2 && SourceName(source, thirdName) == spelling;

            void TakeStatements(int end, int depth, int nearestLoop, int clause = 0, bool singleIf = false)
            {
                if (depth > 6)
                {
                    throw InvalidResult();
                }
                void SkipEmptyStatements()
                {
                    if (clause != 0)
                    {
                        return;
                    }
                    while (at < tokens.Length && tokens[at].Kind == TokenKind.Semicolon)
                    {
                        at++;
                    }
                }
                while (true)
                {
                    SkipEmptyStatements();
                    if (statementCursor >= end)
                    {
                        break;
                    }
                    var statementIndex = statementCursor++;
                    var entry = orderedStatements[statementIndex];
                    var kind = StatementKind(entry.Kind);
                    var start = at;
                    if (clause != 0 && kind is not (1 or 2 or 9 or 10 or 13 or 24 or 25 or 26 or 37)
                        || clause == 2 && kind is 1 or 25 or 26 or 27 or 28)
                    {
                        throw InvalidResult();
                    }
                    if (kind == 38)
                    {
                        if (clause != 0 || entry.Slot <= statementCursor
                            || entry.Root < 0 || entry.Root >= tree.Length
                            || tree[entry.Root].Type is < 1 or > 6)
                        {
                            throw InvalidResult();
                        }
                        if (!Take(TokenKind.SwitchKeyword))
                        {
                            throw InvalidResult();
                        }
                        var keywordSpan = tokens[at - 1].Span;
                        if (entry.Span.Start != keywordSpan.Start
                            || !Take(TokenKind.OpenParen) || !TakeTree(entry.Root)
                            || !Take(TokenKind.CloseParen) || !Take(TokenKind.OpenBrace))
                        {
                            throw InvalidResult();
                        }
                        var selectorScopeLocals = visibleLocals;
                        if (activeLocals >= declaredLocals.Length)
                        {
                            throw InvalidResult();
                        }
                        var selectorLocal = declaredLocals[activeLocals];
                        if (!selectorLocal.IsSwitchSelector
                            || selectorLocal.Type != tree[entry.Root].Type
                            || selectorLocal.Name != keywordSpan
                            )
                        {
                            throw InvalidResult();
                        }
                        switchLocalForStatement[statementIndex] = count + activeLocals;
                        visibleLocals |= 1 << activeLocals;
                        activeLocals++;
                        var switchLocals = visibleLocals;
                        var caseCount = 0;
                        var armCount = 0;
                        var foundDefault = false;
                        while (statementCursor < entry.Slot)
                        {
                            var armIndex = statementCursor++;
                            var arm = orderedStatements[armIndex];
                            if (arm.Kind != 39 || arm.Slot < statementCursor
                                || arm.Slot > entry.Slot)
                            {
                                throw InvalidResult();
                            }
                            var armStart = at;
                            if (arm.Root == -1)
                            {
                                if (foundDefault || caseCount == 0 || !Take(TokenKind.Underscore))
                                {
                                    throw InvalidResult();
                                }
                                foundDefault = true;
                            }
                            else
                            {
                                if (foundDefault || tree[arm.Root].Type != tree[entry.Root].Type
                                    || !TakeTree(arm.Root))
                                {
                                    throw InvalidResult();
                                }
                                caseCount++;
                            }
                            if (!Take(TokenKind.Arrow) || !Take(TokenKind.OpenBrace))
                            {
                                throw InvalidResult();
                            }
                            var armBodyStart = tokens[at - 1].Span.Start;
                            TakeStatements(arm.Slot, depth + 1, nearestLoop);
                            if (!Take(TokenKind.CloseBrace))
                            {
                                throw InvalidResult();
                            }
                            visibleLocals = switchLocals;
                            bodySpans[armIndex] = new TextSpan(armBodyStart,
                                tokens[at - 1].Span.End - armBodyStart);
                            var armSpan = new TextSpan(tokens[armStart].Span.Start,
                                tokens[at - 1].Span.End - tokens[armStart].Span.Start);
                            if (arm.Span != armSpan)
                            {
                                throw InvalidResult();
                            }
                            armCount++;
                            if (at < tokens.Length - 1 && tokens[at].Kind == TokenKind.Comma)
                            {
                                at++;
                                if (statementCursor == entry.Slot
                                    && at < tokens.Length - 1
                                    && tokens[at].Kind == TokenKind.CloseBrace)
                                {
                                    break;
                                }
                            }
                            else if (statementCursor < entry.Slot)
                            {
                                throw InvalidResult();
                            }
                        }
                        if (statementCursor != entry.Slot || armCount < 2 || caseCount == 0
                            || !foundDefault || !Take(TokenKind.CloseBrace))
                        {
                            throw InvalidResult();
                        }
                        var switchSpan = new TextSpan(keywordSpan.Start,
                            tokens[at - 1].Span.End - keywordSpan.Start);
                        if (entry.Span != switchSpan)
                        {
                            throw InvalidResult();
                        }
                        visibleLocals = selectorScopeLocals;
                    }
                    else if (kind is 3 or 4 or 11 or 12 or 14 or 29 or 31 or 32)
                    {
                        if (entry.Slot < statementCursor || entry.Slot > end
                            || (entry.Root != -1 && tree[entry.Root].Type != 5))
                        {
                            throw InvalidResult();
                        }
                        var enclosingForLocals = visibleLocals;
                        string? label = null;
                        if (entry.Kind is >= 16 and <= 18)
                        {
                            if (!Take(TokenKind.Identifier))
                            {
                                throw InvalidResult();
                            }
                            label = tokens[at - 1].Name;
                            if (activeLoops.Any(loop => loop.Label == label)
                                || !Take(TokenKind.Colon)
                                || tokens[at - 2].Span.End != tokens[at - 1].Span.Start
                                || source.GetLinePosition(tokens[at - 1].Span.Start).Line
                                    != source.GetLinePosition(tokens[at].Span.Start).Line)
                            {
                                throw InvalidResult();
                            }
                        }
                        var loopStart = tokens[at].Span.Start;
                        if (kind == 12)
                        {
                            if (statementCursor + 2 > entry.Slot
                                || !Take(TokenKind.ForKeyword) || !Take(TokenKind.OpenParen))
                            {
                                throw InvalidResult();
                            }
                            TakeStatements(statementCursor + 1, depth, nearestLoop, 1);
                            if (!Take(TokenKind.Semicolon))
                            {
                                throw InvalidResult();
                            }
                            if (entry.Root != -1 && !TakeTree(entry.Root)
                                || !Take(TokenKind.Semicolon))
                            {
                                throw InvalidResult();
                            }
                            TakeStatements(statementCursor + 1, depth, nearestLoop, 2);
                            if (!Take(TokenKind.CloseParen) || !Take(TokenKind.OpenBrace))
                            {
                                throw InvalidResult();
                            }
                        }
                        else if (kind == 14)
                        {
                            if (!Take(TokenKind.OpenBrace))
                            {
                                throw InvalidResult();
                            }
                        }
                        else if (kind == 11)
                        {
                            if (!Take(TokenKind.DoKeyword) || !Take(TokenKind.OpenBrace))
                            {
                                throw InvalidResult();
                            }
                        }
                        else if (kind == 29)
                        {
                            if (!Take(TokenKind.TryKeyword) || !Take(TokenKind.OpenBrace))
                            {
                                throw InvalidResult();
                            }
                        }
                        else if (kind == 31)
                        {
                            if (!Take(TokenKind.UnsafeKeyword) || !Take(TokenKind.OpenBrace))
                            {
                                throw InvalidResult();
                            }
                        }
                        else if (kind == 32)
                        {
                            if (!Take(TokenKind.TryKeyword) || !Take(TokenKind.OpenBrace))
                            {
                                throw InvalidResult();
                            }
                        }
                        else if (!Take(kind == 3 ? TokenKind.WhileKeyword : TokenKind.IfKeyword)
                            || !Take(TokenKind.OpenParen) || !TakeTree(entry.Root)
                            || !Take(TokenKind.CloseParen) || !Take(TokenKind.OpenBrace))
                        {
                            throw InvalidResult();
                        }
                        var bodyStart = tokens[at - 1].Span.Start;
                        var enclosingLocals = visibleLocals;
                        if (kind is 3 or 11 or 12)
                        {
                            activeLoops.Add((statementIndex, label));
                        }
                        TakeStatements(entry.Slot, depth + 1,
                            kind is 3 or 11 or 12 ? statementIndex : nearestLoop);
                        if (kind is 3 or 11 or 12)
                        {
                            activeLoops.RemoveAt(activeLoops.Count - 1);
                        }
                        if (!Take(TokenKind.CloseBrace))
                        {
                            throw InvalidResult();
                        }
                        visibleLocals = enclosingLocals;
                        bodySpans[statementIndex] = new TextSpan(bodyStart,
                            tokens[at - 1].Span.End - bodyStart);
                        if (kind == 29)
                        {
                            if (statementCursor != entry.Slot || statementCursor >= end)
                            {
                                throw InvalidResult();
                            }
                            var markerIndex = statementCursor++;
                            var marker = orderedStatements[markerIndex];
                            if (markerIndex != entry.Slot || marker.Kind != 30
                                || marker.Slot < statementCursor || marker.Slot > end
                                || !Take(TokenKind.FinallyKeyword) || !Take(TokenKind.OpenBrace))
                            {
                                throw InvalidResult();
                            }
                            var finallyStart = tokens[at - 2].Span.Start;
                            var cleanupStart = tokens[at - 1].Span.Start;
                            var enclosingCleanupLocals = visibleLocals;
                            TakeStatements(marker.Slot, depth + 1, nearestLoop);
                            if (!Take(TokenKind.CloseBrace))
                            {
                                throw InvalidResult();
                            }
                            visibleLocals = enclosingCleanupLocals;
                            bodySpans[markerIndex] = new TextSpan(cleanupStart,
                                tokens[at - 1].Span.End - cleanupStart);
                            if (marker.Span.Start != finallyStart || marker.Span.End != tokens[at - 1].Span.End)
                            {
                                throw InvalidResult();
                            }
                        }
                        else if (kind == 32)
                        {
                            if (statementCursor != entry.Slot || statementCursor >= end)
                            {
                                throw InvalidResult();
                            }
                            var markerIndex = statementCursor++;
                            var marker = orderedStatements[markerIndex];
                            if (markerIndex != entry.Slot || marker.Kind != 33
                                || marker.Slot < statementCursor || marker.Slot > end
                                || !Take(TokenKind.CatchKeyword) || !Take(TokenKind.OpenBrace))
                            {
                                throw InvalidResult();
                            }
                            var catchStart = tokens[at - 2].Span.Start;
                            var handlerStart = tokens[at - 1].Span.Start;
                            var enclosingCatchLocals = visibleLocals;
                            TakeStatements(marker.Slot, depth + 1, nearestLoop);
                            if (!Take(TokenKind.CloseBrace))
                            {
                                throw InvalidResult();
                            }
                            visibleLocals = enclosingCatchLocals;
                            bodySpans[markerIndex] = new TextSpan(handlerStart,
                                tokens[at - 1].Span.End - handlerStart);
                            if (marker.Span.Start != catchStart || marker.Span.End != tokens[at - 1].Span.End)
                            {
                                throw InvalidResult();
                            }
                            if (at < tokens.Length - 1 && tokens[at].Kind == TokenKind.FinallyKeyword)
                            {
                                if (statementCursor != marker.Slot || statementCursor >= end)
                                {
                                    throw InvalidResult();
                                }
                                var cleanupIndex = statementCursor++;
                                var cleanup = orderedStatements[cleanupIndex];
                                if (cleanupIndex != marker.Slot || cleanup.Kind != 34
                                    || cleanup.Slot < statementCursor || cleanup.Slot > end
                                    || !Take(TokenKind.FinallyKeyword) || !Take(TokenKind.OpenBrace))
                                {
                                    throw InvalidResult();
                                }
                                var finallyStart = tokens[at - 2].Span.Start;
                                var cleanupStart = tokens[at - 1].Span.Start;
                                var enclosingCleanupLocals = visibleLocals;
                                TakeStatements(cleanup.Slot, depth + 1, nearestLoop);
                                if (!Take(TokenKind.CloseBrace))
                                {
                                    throw InvalidResult();
                                }
                                visibleLocals = enclosingCleanupLocals;
                                bodySpans[cleanupIndex] = new TextSpan(cleanupStart,
                                    tokens[at - 1].Span.End - cleanupStart);
                                if (cleanup.Span.Start != finallyStart
                                    || cleanup.Span.End != tokens[at - 1].Span.End)
                                {
                                    throw InvalidResult();
                                }
                            }
                        }
                        if (kind == 11 && (!Take(TokenKind.WhileKeyword)
                            || !Take(TokenKind.OpenParen) || !TakeTree(entry.Root)
                            || !Take(TokenKind.CloseParen) || !Take(TokenKind.Semicolon)))
                        {
                            throw InvalidResult();
                        }
                        loopSpans[statementIndex] = new TextSpan(loopStart, tokens[at - 1].Span.End - loopStart);
                        if (kind == 12)
                        {
                            visibleLocals = enclosingForLocals;
                        }
                        if (kind == 4 && statementCursor < end
                            && orderedStatements[statementCursor].Kind is 5 or 23)
                        {
                            var markerIndex = statementCursor++;
                            var marker = orderedStatements[markerIndex];
                            var elseStart = at;
                            if (markerIndex != entry.Slot || marker.Slot < statementCursor
                                || marker.Slot > end
                                || !Take(TokenKind.ElseKeyword))
                            {
                                throw InvalidResult();
                            }
                            if (marker.Kind == 23)
                            {
                                if (statementCursor >= marker.Slot || orderedStatements[statementCursor].Kind != 4)
                                {
                                    throw InvalidResult();
                                }
                                TakeStatements(marker.Slot, depth + 1, nearestLoop, singleIf: true);
                            }
                            else
                            {
                                if (!Take(TokenKind.OpenBrace))
                                {
                                    throw InvalidResult();
                                }
                                var elseBodyStart = tokens[at - 1].Span.Start;
                                TakeStatements(marker.Slot, depth + 1, nearestLoop);
                                if (!Take(TokenKind.CloseBrace))
                                {
                                    throw InvalidResult();
                                }
                                bodySpans[markerIndex] = new TextSpan(elseBodyStart,
                                    tokens[at - 1].Span.End - elseBodyStart);
                            }
                            if (marker.Span.Start != tokens[elseStart].Span.Start
                                || marker.Span.End != tokens[at - 1].Span.End)
                            {
                                throw InvalidResult();
                            }
                            visibleLocals = enclosingLocals;
                        }
                    }
                    else if (entry.Kind is 1 or 27 or 28)
                    {
                        if (entry.Slot <= count + activeLocals
                            || entry.Slot > count + declaredLocals.Length)
                        {
                            throw InvalidResult();
                        }
                        var local = declaredLocals[entry.Slot - count - 1];
                        var constant = entry.Kind is 27 or 28;
                        if (local.IsConstant != constant || local.IsInitialization
                            || (constant && local.IsMutable)
                            || (constant && (!Take(TokenKind.Identifier) || tokens[at - 1].Text != "const"))
                            || (local.IsMutable && !Take(TokenKind.MutKeyword))
                            || at >= tokens.Length
                            || (!local.IsMutable && tokens[at].Kind == TokenKind.MutKeyword)
                            || (tokens[at].Kind != TokenKind.VarKeyword
                                && tokens[at].Kind != TokenKind.Identifier)
                            || !Take(tokens[at].Kind))
                        {
                            throw InvalidResult();
                        }
                        var inferred = tokens[at - 1].Kind == TokenKind.VarKeyword;
                        var hasUncontextualShift = IsUncontextualShiftRoot(
                            tree, intrinsicTypes, entry.Root);
                        if (constant && inferred
                            || !inferred && !MatchesScalarType(tokens[at - 1], local.Type, aliases)
                            || !Take(TokenKind.Identifier)
                            || tokens[at - 1].Span != local.Name
                            || seenNames.Contains(tokens[at - 1].Name)
                            || VisibleName(tokens[at - 1].Name)
                            || !Take(TokenKind.Equals)
                            || tree[entry.Root].Type != local.Type
                            || entry.Kind == 27 && tree[entry.Root].Operation is not (3 or 20 or 21)
                            || entry.Kind == 28 && (tree[entry.Root].Operation is 3 or 20 or 21)
                            || inferred && (intrinsicTypes[entry.Root] == 0 || hasUncontextualShift)
                            || !TakeTree(entry.Root)
                            || entry.Slot != count + activeLocals + 1
                            || clause == 0 && !Take(TokenKind.Semicolon))
                        {
                            throw InvalidResult();
                        }
                        visibleLocals |= 1 << activeLocals;
                        activeLocals++;
                    }
                    else if (entry.Kind is 25 or 26)
                    {
                        if (activeLocals >= declaredLocals.Length
                            || entry.Slot != count + activeLocals + 1)
                        {
                            throw InvalidResult();
                        }
                        var local = declaredLocals[activeLocals];
                        var inferred = entry.Kind == 26;
                        if (!local.IsInitialization || local.IsMutable
                            || !Take(TokenKind.Identifier) || tokens[at - 1].Text != "init"
                            || !Take(inferred ? TokenKind.VarKeyword : TokenKind.Identifier)
                            || !inferred && !MatchesScalarType(tokens[at - 1], local.Type, aliases)
                            || !Take(TokenKind.Identifier) || tokens[at - 1].Span != local.Name
                            || clause == 0 && !Take(TokenKind.Semicolon))
                        {
                            throw InvalidResult();
                        }
                        visibleLocals |= 1 << activeLocals;
                        activeLocals++;
                    }
                    else if (entry.Kind == 15)
                    {
                        if (activeLocals >= declaredLocals.Length
                            || entry.Slot != count + activeLocals + 1)
                        {
                            throw InvalidResult();
                        }
                        var local = declaredLocals[activeLocals];
                        if (local.IsMutable || !Take(TokenKind.Underscore)
                            || tokens[at - 1].Span != local.Name
                            || !Take(TokenKind.Equals)
                            || tree[entry.Root].Type != local.Type
                            || intrinsicTypes[entry.Root] == 0
                            || IsUncontextualShiftRoot(tree, intrinsicTypes, entry.Root)
                            || !TakeTree(entry.Root) || !Take(TokenKind.Semicolon))
                        {
                            throw InvalidResult();
                        }
                        activeLocals++;
                    }
                    else if (entry.Kind == 2)
                    {
                        if (entry.Slot <= count)
                        {
                            var parameterName = entry.Slot switch {
                                1 => firstName, 2 => secondName, _ => thirdName,
                            };
                            var parameterType = entry.Slot switch {
                                1 => words[7], 2 => words[8], _ => words[34],
                            };
                            if (tree[entry.Root].Type != parameterType
                                || !Take(TokenKind.Identifier)
                                || tokens[at - 1].Name != SourceName(source, parameterName)
                                || !Take(TokenKind.Equals) || !TakeTree(entry.Root)
                                || clause == 0 && !Take(TokenKind.Semicolon))
                            {
                                throw InvalidResult();
                            }
                        }
                        else
                        {
                            var local = declaredLocals[entry.Slot - count - 1];
                            var inferredInitialization = local.IsInitialization
                                && orderedStatements.Any(statement => statement.Kind == 26
                                    && statement.Slot == entry.Slot);
                            if (!Visible(entry.Slot) || !local.IsMutable && !local.IsInitialization
                                || !Take(TokenKind.Identifier)
                                || tokens[at - 1].Name != SourceName(source, local.Name)
                                || !Take(TokenKind.Equals) || tree[entry.Root].Type != local.Type
                                || inferredInitialization
                                    && IsUncontextualShiftRoot(tree, intrinsicTypes, entry.Root)
                                || !TakeTree(entry.Root)
                                || clause == 0 && !Take(TokenKind.Semicolon))
                            {
                                throw InvalidResult();
                            }
                        }
                    }
                    else if (entry.Kind == 24)
                    {
                        var local = declaredLocals[entry.Slot - count - 1];
                        if (!Visible(entry.Slot)
                            || !Take(TokenKind.Identifier)
                            || tokens[at - 1].Name != SourceName(source, local.Name)
                            || !Take(TokenKind.Equals) || !Take(TokenKind.Underscore)
                            || clause == 0 && !Take(TokenKind.Semicolon))
                        {
                            throw InvalidResult();
                        }
                    }
                    else if (kind is 6 or 7)
                    {
                        if (!Take(kind == 6 ? TokenKind.BreakKeyword : TokenKind.ContinueKeyword))
                        {
                            throw InvalidResult();
                        }
                        var target = nearestLoop;
                        if (entry.Kind is 19 or 20)
                        {
                            if (!Take(TokenKind.Identifier))
                            {
                                throw InvalidResult();
                            }
                            var selected = activeLoops.FindLastIndex(loop => loop.Label == tokens[at - 1].Name);
                            if (selected < 0)
                            {
                                throw InvalidResult();
                            }
                            target = activeLoops[selected].Header;
                        }
                        if (entry.Slot != target || !Take(TokenKind.Semicolon))
                        {
                            throw InvalidResult();
                        }
                    }
                    else if (entry.Kind == 8)
                    {
                        if (tree[entry.Root].Type != words[5]
                            || !Take(TokenKind.ReturnKeyword)
                            || !TakeTree(entry.Root) || !Take(TokenKind.Semicolon))
                        {
                            throw InvalidResult();
                        }
                    }
                    else if (entry.Kind == 21)
                    {
                        if (tree[entry.Root].Type != 5 || !Take(TokenKind.RequireKeyword)
                            || !TakeTree(entry.Root) || !Take(TokenKind.Semicolon))
                        {
                            throw InvalidResult();
                        }
                    }
                    else if (kind is 35 or 36)
                    {
                        if (at >= tokens.Length)
                        {
                            throw InvalidResult();
                        }
                        var proofStart = tokens[at].Span.Start;
                        var proofKeywordIsValid = Take(TokenKind.StaticKeyword)
                            && (kind == 35
                                ? Take(TokenKind.RequireKeyword)
                                : Take(TokenKind.Identifier) && tokens[at - 1].Text == "ensure");
                        if (clause != 0 || kind == 36 && depth != 0 || !proofKeywordIsValid
                            || tree[entry.Root].Type != 5
                            || !TakeTree(entry.Root) || !Take(TokenKind.Semicolon)
                            || entry.Span.Start != proofStart
                            || entry.Span.End != tokens[at - 1].Span.End
                            || kind == 36 && ContainsRead(entry.Root))
                        {
                            throw InvalidResult();
                        }
                    }
                    else if (entry.Kind == 22)
                    {
                        if (words[5] != 0 || !Take(TokenKind.ReturnKeyword))
                        {
                            throw InvalidResult();
                        }
                        var groups = 0;
                        while (Take(TokenKind.OpenParen))
                        {
                            if (++groups > 16)
                            {
                                throw InvalidResult();
                            }
                        }
                        for (var group = 0; group < groups; group++)
                        {
                            if (!Take(TokenKind.CloseParen))
                            {
                                throw InvalidResult();
                            }
                        }
                        if (!Take(TokenKind.Semicolon))
                        {
                            throw InvalidResult();
                        }
                    }
                    else if (entry.Kind == 9)
                    {
                        var compound = tree[entry.Root];
                        if (compound.Operation is < 91 or > 101)
                        {
                            throw InvalidResult();
                        }
                        var place = Ungroup(compound.Left);
                        var writable = entry.Slot <= count
                            ? entry.Slot >= 1 && (words[16] & (1 << (entry.Slot - 1))) != 0
                            : Visible(entry.Slot) && declaredLocals[entry.Slot - count - 1].IsMutable;
                        if (tree[place].Left != entry.Slot
                            || !writable
                            || !TakeTree(entry.Root)
                            || clause == 0 && !Take(TokenKind.Semicolon))
                        {
                            throw InvalidResult();
                        }
                    }
                    else if (entry.Kind == 10)
                    {
                        var update = tree[entry.Root];
                        if (update.Operation is not (86 or 87)
                            || !TakeTree(entry.Root)
                            || clause == 0 && !Take(TokenKind.Semicolon))
                        {
                            throw InvalidResult();
                        }
                    }
                    else if (entry.Kind == 37)
                    {
                        if (tree[entry.Root].Operation is not (102 or 105)
                            || !TakeTree(entry.Root)
                            || clause == 0 && !Take(TokenKind.Semicolon))
                        {
                            throw InvalidResult();
                        }
                    }
                    else if (entry.Kind == 13)
                    {
                        if (clause == 0 || at >= tokens.Length
                            || tokens[at].Kind != (clause == 1
                                ? TokenKind.Semicolon : TokenKind.CloseParen)
                            || entry.Span != tokens[at].Span)
                        {
                            throw InvalidResult();
                        }
                        continue;
                    }
                    else
                    {
                        throw InvalidResult();
                    }
                    if (entry.Span.Start != tokens[start].Span.Start
                        || entry.Span.End != tokens[at - 1].Span.End)
                    {
                        throw InvalidResult();
                    }
                    if (singleIf && (kind != 4 || statementCursor != end))
                    {
                        throw InvalidResult();
                    }
                }
                if (statementCursor != end)
                {
                    throw InvalidResult();
                }
            }

            bool TakeTree(int index, int parseDepth = 0)
            {
                var node = tree[index];
                var start = at;
                if (used[index] || parseDepth >= 16)
                {
                    return false;
                }
                used[index] = true;
                if (node.Operation == 110)
                {
                    if (!Take(TokenKind.UnsafeKeyword) || !Take(TokenKind.OpenParen)
                        || !TakeTree(node.Left, parseDepth + 1) || !Take(TokenKind.CloseParen))
                    {
                        return false;
                    }
                }
                else if (node.Operation == 46)
                {
                    if (!Take(TokenKind.OpenParen) || !TakeTree(node.Left, parseDepth + 1)
                        || !Take(TokenKind.CloseParen))
                    {
                        return false;
                    }
                }
                else if (node.Operation == 109)
                {
                    if (tree[node.Left].Operation != 0
                        || !TakeTree(node.Left, parseDepth + 1)
                        || !Take(TokenKind.Identifier) || tokens[at - 1].Name != "is"
                        || tokens[at - 1].Span.Length != 2
                        || !Take(TokenKind.Identifier) || tokens[at - 1].Name != "init"
                        || tokens[at - 1].Span.Length != 4)
                    {
                        return false;
                    }
                }
                else if (node.Operation == 83)
                {
                    if (!Take(TokenKind.Identifier)
                        || !MatchesPrimitiveScalarType(tokens[at - 1], node.Type)
                        || tokens[at - 1].Name == SourceName(source, name)
                        || context is not null && context.Declarations.Any(
                            declaration => declaration.Name == tokens[at - 1].Name)
                        || seenNames.Contains(tokens[at - 1].Name)
                        || VisibleName(tokens[at - 1].Name)
                        || !Take(TokenKind.OpenParen) || !TakeTree(node.Left, parseDepth + 1)
                        || !Take(TokenKind.CloseParen))
                    {
                        return false;
                    }
                }
                else if (node.Operation == 84)
                {
                    if (Precedence(tree[node.Left]) <= Precedence(node)
                        || !TakeTree(node.Left, parseDepth) || !Take(TokenKind.Question)
                        || !TakeTree(node.Right, parseDepth + 1) || !Take(TokenKind.Colon)
                        || !TakeTree(node.Low, parseDepth + 1))
                    {
                        return false;
                    }
                }
                else if (node.Operation is 102 or 105)
                {
                    var arguments = new[] { node.Left, node.Right, node.Low };
                    var callName = node.Operation == 105 && context is not null
                        ? context.Callee(index).Name : SourceName(source, name);
                    var callCount = node.Operation == 105 ? node.High : count;
                    if (!Take(TokenKind.Identifier)
                        || tokens[at - 1].Name != callName
                        || ParameterNamed(callName)
                        || VisibleName(callName)
                        || !Take(TokenKind.OpenParen))
                    {
                        return false;
                    }
                    var hasOutOfPositionNamedArgument = false;
                    var hasNamedArgument = false;
                    var nextPositionalParameter = 0;
                    for (var ordinal = 0; ordinal < callCount; ordinal++)
                    {
                        if (ordinal != 0 && !Take(TokenKind.Comma))
                        {
                            return false;
                        }
                        var parameter = node.ArgumentParameterIndices.IsDefault
                            ? ordinal : node.ArgumentParameterIndices[ordinal];
                        if (at + 1 < tokens.Length && tokens[at].Kind == TokenKind.Identifier
                            && tokens[at + 1].Kind == TokenKind.Colon)
                        {
                            if (node.ArgumentParameterIndices.IsDefault
                                || !Take(TokenKind.Identifier)
                                || tokens[at - 1].Name != CallParameterName(index, parameter)
                                || !Take(TokenKind.Colon))
                            {
                                return false;
                            }
                            hasNamedArgument = true;
                            if (parameter == nextPositionalParameter)
                            {
                                nextPositionalParameter++;
                            }
                            else
                            {
                                hasOutOfPositionNamedArgument = true;
                            }
                        }
                        else
                        {
                            if (hasOutOfPositionNamedArgument || parameter != nextPositionalParameter)
                            {
                                return false;
                            }
                            nextPositionalParameter++;
                        }
                        var expectsOut = node.Operation == 105 && context is not null
                            ? (context.Callee(index).OutParameterMask & (1 << parameter)) != 0
                            : (words[16] & (1 << parameter)) != 0;
                        var expectsInitRef = node.Operation == 105 && context is not null
                            ? (context.Callee(index).InitRefParameterMask & (1 << parameter)) != 0
                            : (words[17] & (1 << parameter)) != 0;
                        if (expectsOut != (tree[arguments[ordinal]].Operation is 103 or 104)
                            || expectsInitRef != (tree[arguments[ordinal]].Operation is 107 or 108)
                            || !TakeTree(arguments[ordinal], parseDepth + 1))
                        {
                            return false;
                        }
                    }
                    if (hasNamedArgument != !node.ArgumentParameterIndices.IsDefault
                        || !Take(TokenKind.CloseParen))
                    {
                        return false;
                    }
                }
                else if (node.Operation is 103 or 107)
                {
                    var isInit = node.Operation == 107;
                    if (!Visible(node.Left))
                    {
                        return false;
                    }
                    var writable = isInit
                        ? node.Left > count
                            || ((words[16] | words[17]) & (1 << (node.Left - 1))) != 0
                        : node.Left <= count
                            ? (words[16] & (1 << (node.Left - 1))) != 0
                            : declaredLocals[node.Left - count - 1].IsMutable;
                    var spelling = node.Left > count
                        ? declaredLocals[node.Left - count - 1].Name
                        : node.Left switch {
                            1 => firstName, 2 => secondName, _ => thirdName,
                        };
                    if (!writable
                        || isInit && (tokens[at].Kind != TokenKind.Identifier
                            || tokens[at].Name != "init" || tokens[at].Span.Length != 4
                            || !Take(TokenKind.Identifier))
                        || !isInit && !Take(TokenKind.OutKeyword)
                        || !Take(TokenKind.Identifier)
                        || tokens[at - 1].Name != SourceName(source, spelling))
                    {
                        return false;
                    }
                }
                else if (node.Operation is 104 or 108)
                {
                    var isInit = node.Operation == 108;
                    var localIndex = node.Left - count - 1;
                    var local = declaredLocals[localIndex];
                    if (node.Left != count + activeLocals + 1
                        || activeLocals >= declaredLocals.Length
                        || local.IsMutable || local.IsInitialization || local.IsConstant
                        || isInit && (tokens[at].Kind != TokenKind.Identifier
                            || tokens[at].Name != "init" || tokens[at].Span.Length != 4
                            || !Take(TokenKind.Identifier))
                        || !isInit && !Take(TokenKind.OutKeyword)
                        || !Take(TokenKind.VarKeyword)
                        || !Take(TokenKind.Identifier)
                        || tokens[at - 1].Span != local.Name
                        || seenNames.Contains(tokens[at - 1].Name)
                        || VisibleName(tokens[at - 1].Name))
                    {
                        return false;
                    }
                    visibleLocals |= 1 << activeLocals;
                    activeLocals++;
                }
                else if (node.Operation is 86 or 87 && node.Low == 1)
                {
                    if (!Take((TokenKind)node.Operation) || !TakeTree(node.Left, parseDepth + 1)
                        || !ActiveWritablePlace(node.Left))
                    {
                        return false;
                    }
                }
                else if (node.Operation is 86 or 87)
                {
                    if (!TakeTree(node.Left, parseDepth) || !ActiveWritablePlace(node.Left)
                        || !Take((TokenKind)node.Operation))
                    {
                        return false;
                    }
                }
                else if (node.Operation is >= 91 and <= 101)
                {
                    if (!TakeTree(node.Left, parseDepth) || !ActiveWritablePlace(node.Left)
                        || !Take((TokenKind)node.Operation) || !TakeTree(node.Right, parseDepth))
                    {
                        return false;
                    }
                }
                else if (node.Operation is 57 or 58 or 62 or 63 && node.Right == -1)
                {
                    if (!Take((TokenKind)node.Operation) || !TakeTree(node.Left, parseDepth + 1))
                    {
                        return false;
                    }
                }
                else if (node.Operation == 0)
                {
                    if (!Visible(node.Left))
                    {
                        return false;
                    }
                    var spelling = node.Left > count
                        ? declaredLocals[node.Left - count - 1].Name
                        : node.Left switch {
                            1 => firstName, 2 => secondName, _ => thirdName,
                        };
                    if (!Take(TokenKind.Identifier)
                        || tokens[at - 1].Name != SourceName(source, spelling))
                    {
                        return false;
                    }
                }
                else if (node.Operation == 3)
                {
                    if (at < tokens.Length && tokens[at].Kind == TokenKind.CharacterLiteral)
                    {
                        if (node.Type != 6 || !Take(TokenKind.CharacterLiteral)
                            || !CharacterLiteralDecoder.TryDecode(tokens[at - 1].Text.AsSpan(),
                                out var scalar)
                            || scalar > ushort.MaxValue || node.Low != scalar || node.High != 0)
                        {
                            return false;
                        }
                    }
                    else if (!Take(TokenKind.NumericLiteral)
                        || (node.Type is 7 or 8
                            ? !FloatingProjectionMatches(tokens[at - 1].Text.AsSpan(),
                                node.Type, node.Low, node.High)
                            : !ValidMagnitude(tokens[at - 1].Text.AsSpan(), node.Type,
                                tree.Any(parent => parent.Operation == 58 && parent.Left == index
                                    && parent.Right == -1), node.Low, node.High)))
                    {
                        return false;
                    }
                }
                else if (node.Operation is 20 or 21)
                {
                    if (!Take((TokenKind)node.Operation))
                    {
                        return false;
                    }
                }
                else if (Precedence(tree[node.Left]) < Precedence(node)
                    || Precedence(tree[node.Right]) <= Precedence(node)
                    || !TakeTree(node.Left, parseDepth) || !Take((TokenKind)node.Operation)
                    || !TakeTree(node.Right, parseDepth + 1))
                {
                    return false;
                }
                return start < at && node.Span.Start == tokens[start].Span.Start
                    && node.Span.End == tokens[at - 1].Span.End;
            }

            int Precedence(ScalarFrontendNode node)
            {
                if (node.Operation == 109) { return 6; }
                if (node.Right == -1 || node.Operation == 46)
                {
                    return 12;
                }
                return node.Operation switch {
                    84 => 0,
                    >= 59 and <= 61 => 10,
                    >= 88 and <= 90 => 8,
                    57 or 58 => 9,
                    >= 67 and <= 70 => 7,
                    65 or 66 => 6,
                    71 => 5, 75 => 4, 73 => 3, 72 => 2, 74 => 1,
                    _ => 12,
                };
            }

            int Ungroup(int index)
            {
                while (tree[index].Operation == 46)
                {
                    index = tree[index].Left;
                }
                return index;
            }

            bool ActiveWritablePlace(int index)
            {
                var read = tree[Ungroup(index)];
                return read.Operation == 0
                    && (read.Left <= count
                        ? read.Left > 0 && (words[16] & (1 << (read.Left - 1))) != 0
                        : Visible(read.Left) && declaredLocals[read.Left - count - 1].IsMutable);
            }

            bool UpdatePlace(int index)
            {
                while (nodes[index].Operation == 46)
                {
                    index = nodes[index].Left;
                }
                var read = nodes[index];
                return read.Operation == 0 && read.Type is >= 1 and <= 4
                    && (read.Left <= count
                        ? read.Left > 0 && (words[16] & (1 << (read.Left - 1))) != 0
                        : declaredLocals[read.Left - count - 1].IsMutable);
            }
        }

        bool ValidSpan(TextSpan span, bool nonempty = false)
            => span.Start >= 0 && span.Start <= source.Length && span.Length >= (nonempty ? 1 : 0)
                && span.Length <= source.Length - span.Start;

        bool ValidHeader()
        {
            var end = count switch { 0 => name.End, 1 => firstName.End, 2 => secondName.End, _ => thirdName.End };
            var lexical = Lexer.Lex(SourceText.From(source.Text[..end]));
            var tokens = lexical.Tokens;
            var attributeEnd = 0;
            if (!TakeDeclarationEffects(tokens, ref attributeEnd, effects)
                || !TakeCallableModifiers(tokens, ref attributeEnd, isPublic,
                    true, (words[2] & 8) != 0))
            {
                return false;
            }
            var offset = attributeEnd;
            if (!lexical.Diagnostics.IsEmpty || tokens.Length <= offset + 1
                || tokens[offset].Kind != (words[5] == 0 ? TokenKind.VoidKeyword : TokenKind.Identifier)
                || !MatchesScalarType(tokens[offset], words[5], aliases)
                || tokens[offset + 1].Kind != TokenKind.Identifier
                || tokens[offset + 1].Span != name)
            {
                return false;
            }
            var at = offset + 2;
            if (count == 0)
            {
                return at == tokens.Length - 1;
            }
            if (at >= tokens.Length || tokens[at++].Kind != TokenKind.OpenParen)
            {
                return false;
            }
            for (var parameter = 0; parameter < count; parameter++)
            {
                if (parameter != 0
                    && (at >= tokens.Length || tokens[at++].Kind != TokenKind.Comma))
                {
                    return false;
                }
                if (at >= tokens.Length)
                {
                    return false;
                }
                var isOut = isTree
                    && (words[16] & (1 << parameter)) != 0;
                var isInitRef = allowInitRefArguments
                    && (words[17] & (1 << parameter)) != 0;
                if (isOut)
                {
                    if (tokens[at++].Kind != TokenKind.OutKeyword) { return false; }
                }
                else if (isInitRef)
                {
                    if (tokens[at].Kind != TokenKind.Identifier || tokens[at].Name != "init"
                        || tokens[at].Span.Length != 4 || tokens[at++].Kind != TokenKind.Identifier
                        || tokens[at++].Kind != TokenKind.RefKeyword
                        || tokens[at++].Kind != TokenKind.Less)
                    {
                        return false;
                    }
                }
                else if (tokens[at].Kind == TokenKind.OutKeyword
                    || tokens[at].Kind == TokenKind.Identifier && tokens[at].Name == "init")
                {
                    return false;
                }
                var expectedType = parameter switch { 0 => words[7], 1 => words[8], _ => words[34] };
                var expectedName = parameter switch { 0 => firstName, 1 => secondName, _ => thirdName };
                if (at >= tokens.Length - 1
                    || tokens[at].Kind != TokenKind.Identifier
                    || !MatchesScalarType(tokens[at], expectedType, aliases))
                {
                    return false;
                }
                at++;
                if (isInitRef && (at >= tokens.Length || tokens[at++].Kind != TokenKind.Greater))
                {
                    return false;
                }
                if (at >= tokens.Length - 1
                    || tokens[at].Kind != TokenKind.Identifier || tokens[at++].Span != expectedName)
                {
                    return false;
                }
                if (parameter > 0 && SourceName(source, expectedName) == SourceName(source, firstName)
                    || parameter > 1 && SourceName(source, expectedName) == SourceName(source, secondName))
                {
                    return false;
                }
            }
            return at == tokens.Length - 1;
        }

        bool ValidBody()
        {
            var headerEnd = count switch { 0 => name.End, 1 => firstName.End, 2 => secondName.End, _ => thirdName.End };
            var lexical = Lexer.Lex(SourceText.From(source.Text[headerEnd..]));
            var tokens = lexical.Tokens;
            var at = 0;
            if (!lexical.Diagnostics.IsEmpty
                || !Take(count == 0 ? TokenKind.OpenParen : TokenKind.CloseParen)
                || (count == 0 && !Take(TokenKind.CloseParen))
                || !Take(TokenKind.OpenBrace))
            {
                return false;
            }

            if (words[15] != 0
                && (!Take(TokenKind.IfKeyword) || !Take(TokenKind.OpenParen)
                    || !TakeExpression(condition) || !Take(TokenKind.CloseParen)
                    || !Take(TokenKind.OpenBrace)))
            {
                return false;
            }
            if (!TakeReturn(whenTrue))
            {
                return false;
            }
            if (words[15] != 0)
            {
                if (!Take(TokenKind.CloseBrace)
                    || (words[15] == 1 && (!Take(TokenKind.ElseKeyword)
                        || !Take(TokenKind.OpenBrace)))
                    || !TakeReturn(whenFalse)
                    || (words[15] == 1 && !Take(TokenKind.CloseBrace)))
                {
                    return false;
                }
            }
            return Take(TokenKind.CloseBrace) && Take(TokenKind.EndOfFile)
                && at == tokens.Length;

            bool Take(TokenKind kind)
            {
                if (at >= tokens.Length || tokens[at].Kind != kind)
                {
                    return false;
                }
                at++;
                return true;
            }

            bool TakeReturn(ScalarFrontendExpression expression)
                => Take(TokenKind.ReturnKeyword) && TakeExpression(expression)
                    && Take(TokenKind.Semicolon);

            bool TakeExpression(ScalarFrontendExpression expression)
            {
                var groups = 0;
                while (at < tokens.Length && tokens[at].Kind == TokenKind.OpenParen
                    && headerEnd + tokens[at].Span.Start < expression.Span.Start)
                {
                    at++;
                    groups++;
                }
                if (at >= tokens.Length
                    || headerEnd + tokens[at].Span.Start != expression.Span.Start)
                {
                    return false;
                }
                while (at < tokens.Length && tokens[at].Kind != TokenKind.EndOfFile
                    && headerEnd + tokens[at].Span.End <= expression.Span.End)
                {
                    at++;
                }
                if (at == 0 || headerEnd + tokens[at - 1].Span.End != expression.Span.End)
                {
                    return false;
                }
                while (groups-- > 0)
                {
                    if (!Take(TokenKind.CloseParen))
                    {
                        return false;
                    }
                }
                return true;
            }
        }

        bool ValidExpression(ScalarFrontendExpression expression, int type, int parameters)
        {
            var encodedOffset = expression.Operation >= 40000 ? 40000 : 0;
            var operation = expression.Operation - encodedOffset;
            var left = expression.LeftParameter;
            var right = expression.RightParameter;
            var leftType = ParameterType(left);
            var rightType = ParameterType(right);
            if (encodedOffset != 0 && (parameters != 3
                || operation < 100 || operation >= 40000))
            {
                return false;
            }
            if (operation == 62)
            {
                if (expression.Type != 5 || type != 5
                    || !ValidSpan(expression.Span, nonempty: true))
                {
                    return false;
                }
                var lexical = Lexer.Lex(SourceText.From(source.Text.Substring(
                    expression.Span.Start, expression.Span.Length)));
                var tokens = lexical.Tokens;
                if (!lexical.Diagnostics.IsEmpty
                    || !TryUnaryOperand(tokens, TokenKind.Bang, expression.Span.Length,
                        out var operandIndex))
                {
                    return false;
                }
                if (left == 0)
                {
                    return right is 20 or 21 && tokens[operandIndex].Kind == (TokenKind)right;
                }
                var name = ParameterName(left);
                return right == 0 && left >= 1 && left <= parameters && leftType == 5
                    && tokens[operandIndex].Kind == TokenKind.Identifier
                    && tokens[operandIndex].Name == SourceName(source, name);
            }
            if (operation is 63 or 163)
            {
                if (expression.Type != type || type is not (1 or 2 or 3 or 4)
                    || !ValidSpan(expression.Span, nonempty: true))
                {
                    return false;
                }
                var lexical = Lexer.Lex(SourceText.From(source.Text.Substring(
                    expression.Span.Start, expression.Span.Length)));
                var tokens = lexical.Tokens;
                if (!lexical.Diagnostics.IsEmpty
                    || !TryUnaryOperand(tokens, TokenKind.Tilde, expression.Span.Length,
                        out var operandIndex))
                {
                    return false;
                }
                if (operation == 163)
                {
                    return tokens[operandIndex].Kind == TokenKind.NumericLiteral
                        && ValidMagnitude(tokens[operandIndex].Text.AsSpan(), type, false, left, right);
                }
                var name = ParameterName(left);
                return right == 0 && left >= 1 && left <= parameters && leftType == type
                    && tokens[operandIndex].Kind == TokenKind.Identifier
                    && tokens[operandIndex].Name == SourceName(source, name);
            }
            if (operation is 57 or 58 && right == 0)
            {
                if (expression.Type != type
                    || (operation == 58 ? type is not (1 or 3) : type is < 1 or > 4)
                    || !ValidSpan(expression.Span, nonempty: true))
                {
                    return false;
                }
                var lexical = Lexer.Lex(SourceText.From(source.Text.Substring(
                    expression.Span.Start, expression.Span.Length)));
                var tokens = lexical.Tokens;
                var name = ParameterName(left);
                if (!lexical.Diagnostics.IsEmpty
                    || !TryUnaryOperand(tokens, (TokenKind)operation, expression.Span.Length,
                        out var operandIndex))
                {
                    return false;
                }
                return tokens[operandIndex].Kind == TokenKind.Identifier
                    && right == 0 && left >= 1 && left <= parameters && leftType == type
                    && tokens[operandIndex].Name == SourceName(source, name);
            }
            if (operation is 657 or 658)
            {
                if (expression.Type != type
                    || (operation == 658 ? type is not (1 or 3 or 7 or 8)
                        : type is not (1 or 2 or 3 or 4 or 7 or 8))
                    || !ValidSpan(expression.Span, nonempty: true))
                {
                    return false;
                }
                var lexical = Lexer.Lex(SourceText.From(source.Text.Substring(
                    expression.Span.Start, expression.Span.Length)));
                var tokens = lexical.Tokens;
                return lexical.Diagnostics.IsEmpty
                    && TryUnaryOperand(tokens, operation == 658 ? TokenKind.Minus
                        : TokenKind.Plus, expression.Span.Length,
                        out var operandIndex)
                    && tokens[operandIndex].Kind == TokenKind.NumericLiteral
                    && (type is 7 or 8
                        ? FloatingProjectionMatches(tokens[operandIndex].Text.AsSpan(),
                            type, left, right)
                        : ValidMagnitude(tokens[operandIndex].Text.AsSpan(),
                            type, false, left, right));
            }
            if (IsNegatedBooleanBinary(operation))
            {
                return expression.Type == 5 && type == 5
                    && ValidSpan(expression.Span, nonempty: true)
                    && TryGroupedUnarySpan(source, expression.Span,
                        TokenKind.Bang, out var nestedSpan)
                    && ValidExpression(new ScalarFrontendExpression(5, operation - 2000 + encodedOffset,
                        left, right, nestedSpan), 5, parameters);
            }
            if (IsComplementedIntegerBinary(operation))
            {
                return expression.Type == type
                    && type is >= 1 and <= 4
                    && ((operation - 4000 is not (>= 57 and <= 61 or 71 or 73 or 75))
                        || left >= 1 && right >= 1)
                    && ValidSpan(expression.Span, nonempty: true)
                    && TryGroupedUnarySpan(source, expression.Span,
                        TokenKind.Tilde, out var nestedSpan)
                    && ValidExpression(new ScalarFrontendExpression(type, operation - 4000 + encodedOffset,
                        left, right, nestedSpan), type, parameters);
            }
            if (IsSignedIntegerBinary(operation))
            {
                var minus = operation >= 8000;
                var offset = minus ? 8000 : 6000;
                var binary = operation - offset;
                return expression.Type == type
                    && (minus ? type is 1 or 3 : type is >= 1 and <= 4)
                    && ((binary is not (>= 57 and <= 61 or 71 or 73 or 75))
                        || left >= 1 && right >= 1)
                    && ValidSpan(expression.Span, nonempty: true)
                    && TryGroupedUnarySpan(source, expression.Span,
                        minus ? TokenKind.Minus : TokenKind.Plus, out var nestedSpan)
                    && ValidExpression(new ScalarFrontendExpression(type, binary + encodedOffset,
                        left, right, nestedSpan), type, parameters);
            }
            if (IsSignedIntegerBinary(operation - 16000))
            {
                return expression.Type == type
                    && type is >= 1 and <= 4
                    && ValidSpan(expression.Span, nonempty: true)
                    && TryGroupedUnarySpan(source, expression.Span,
                        TokenKind.Tilde, out var nestedSpan)
                    && ValidExpression(new ScalarFrontendExpression(type,
                        operation - 16000 + encodedOffset, left, right, nestedSpan), type, parameters);
            }
            if (operation is >= 38000 and < 40000)
            {
                return expression.Type == type
                    && type is >= 1 and <= 4
                    && IsComplementedIntegerBinary(operation - 34000)
                    && ValidSpan(expression.Span, nonempty: true)
                    && TryGroupedUnarySpan(source, expression.Span,
                        TokenKind.Tilde, out var nestedSpan)
                    && ValidExpression(new ScalarFrontendExpression(type,
                        operation - 34000 + encodedOffset, left, right, nestedSpan), type, parameters);
            }
            if (operation is 10003 or 10057 or 10058 or 10657 or 10658)
            {
                return expression.Type == type
                    && type is >= 1 and <= 4 && ValidSpan(expression.Span, nonempty: true)
                    && TryGroupedUnarySpan(source, expression.Span,
                        TokenKind.Tilde, out var nestedSpan)
                    && (operation != 10003 || source.Text[nestedSpan.Start] == '-')
                    && ValidExpression(new ScalarFrontendExpression(type,
                        operation - 10000 + encodedOffset, left, right, nestedSpan), type, parameters);
            }
            if (operation is 12063 or 12163 or 14063 or 14163)
            {
                var minus = operation is 14063 or 14163;
                var nestedOperation = operation - (minus ? 14000 : 12000);
                return expression.Type == type
                    && (minus ? type is 1 or 3 : type is >= 1 and <= 4)
                    && ValidSpan(expression.Span, nonempty: true)
                    && TryGroupedUnarySpan(source, expression.Span,
                        minus ? TokenKind.Minus : TokenKind.Plus, out var nestedSpan)
                    && ValidExpression(new ScalarFrontendExpression(type, nestedOperation + encodedOffset,
                        left, right, nestedSpan), type, parameters);
            }
            if (operation is >= 30000 and < 34000)
            {
                var minus = operation >= 32000;
                var nestedOperation = operation - (minus ? 28000 : 26000);
                return expression.Type == type
                    && (minus ? type is 1 or 3 : type is >= 1 and <= 4)
                    && IsComplementedIntegerBinary(nestedOperation)
                    && ValidSpan(expression.Span, nonempty: true)
                    && TryGroupedUnarySpan(source, expression.Span,
                        minus ? TokenKind.Minus : TokenKind.Plus, out var nestedSpan)
                    && ValidExpression(new ScalarFrontendExpression(type,
                        nestedOperation + encodedOffset, left, right, nestedSpan), type, parameters);
            }
            if (operation == 16062 || operation is >= 18000 and < 20000
                && IsNegatedBooleanBinary(operation - 16000))
            {
                return expression.Type == 5 && type == 5
                    && ValidSpan(expression.Span, nonempty: true)
                    && TryGroupedUnarySpan(source, expression.Span,
                        TokenKind.Bang, out var nestedSpan)
                    && ValidExpression(new ScalarFrontendExpression(5,
                        operation - 16000 + encodedOffset,
                        left, right, nestedSpan), 5, parameters);
            }
            if (operation / 100 is 7 or 9
                && operation % 100 is 65 or 66 or >= 71 and <= 75)
            {
                if (expression.Type != 5 || type != 5
                    || left < 1 || left > parameters
                    || leftType != 5
                    || right is not (20 or 21)
                    || !ValidSpan(expression.Span, nonempty: true))
                {
                    return false;
                }
                var literalFirst = operation >= 900;
                var lexical = Lexer.Lex(SourceText.From(source.Text.Substring(
                    expression.Span.Start, expression.Span.Length)));
                var tokens = lexical.Tokens;
                var name = ParameterName(left);
                return lexical.Diagnostics.IsEmpty
                    && TryBinaryOperandTokens(tokens, (TokenKind)(operation % 100),
                        expression.Span.Length, out var first, out var second)
                    && tokens[first].Kind == (literalFirst ? (TokenKind)right : TokenKind.Identifier)
                    && (literalFirst || tokens[first].Name
                        == SourceName(source, name))
                    && tokens[second].Kind == (literalFirst ? TokenKind.Identifier : (TokenKind)right)
                    && (!literalFirst || tokens[second].Name
                        == SourceName(source, name));
            }
            if (operation / 100 == 11
                && operation % 100 is 65 or 66 or >= 71 and <= 75)
            {
                if (expression.Type != 5 || type != 5
                    || left is not (20 or 21) || right is not (20 or 21)
                    || !ValidSpan(expression.Span, nonempty: true))
                {
                    return false;
                }
                var lexical = Lexer.Lex(SourceText.From(source.Text.Substring(
                    expression.Span.Start, expression.Span.Length)));
                var tokens = lexical.Tokens;
                return lexical.Diagnostics.IsEmpty
                    && TryBinaryOperandTokens(tokens, (TokenKind)(operation % 100),
                        expression.Span.Length, out var first, out var second)
                    && tokens[first].Kind == (TokenKind)left && tokens[second].Kind == (TokenKind)right;
            }
            if (operation / 100 == 5 && operation % 100 is >= 57 and <= 61 or 71 or 73 or 75)
            {
                if (expression.Type != type || type is not (1 or 2)
                    || !ValidSpan(expression.Span, nonempty: true))
                {
                    return false;
                }
                var text = source.Text.Substring(expression.Span.Start, expression.Span.Length);
                var lexical = Lexer.Lex(SourceText.From(text));
                var tokens = lexical.Tokens;
                return lexical.Diagnostics.IsEmpty
                    && TryBinaryOperandTokens(tokens, (TokenKind)(operation % 100),
                        text.Length, out var first, out var second)
                    && tokens[first].Kind == TokenKind.NumericLiteral
                    && tokens[second].Kind == TokenKind.NumericLiteral
                    && ValidMagnitude(tokens[first].Text.AsSpan(), type, false, left, 0)
                    && ValidMagnitude(tokens[second].Text.AsSpan(), type, false, right, 0);
            }
            var positiveLiteral = operation >= 1600;
            var negativeLiteral = !positiveLiteral && operation >= 1200;
            var binaryOperation = operation - (positiveLiteral ? 1600 : negativeLiteral ? 1200 : 0);
            if (binaryOperation / 100 is >= 1 and <= 4
                && binaryOperation % 100 is >= 57 and <= 61 or >= 65 and <= 70
                    or 71 or 73 or 75 or >= 88 and <= 90)
            {
                var literalFirst = binaryOperation >= 300;
                var parameter = encodedOffset != 0 ? 3
                    : binaryOperation / 100 - (literalFirst ? 2 : 0);
                var parameterType = ParameterType(parameter);
                var comparison = binaryOperation % 100 is >= 65 and <= 70;
                var shift = binaryOperation % 100 is >= 88 and <= 90;
                if ((encodedOffset != 0 && binaryOperation / 100 != (literalFirst ? 3 : 1))
                    || expression.Type != type
                    || (shift ? (literalFirst
                            ? parameterType != 1 || type is not (1 or 2 or 3 or 4)
                            : type != parameterType || parameterType is not (1 or 2 or 3 or 4))
                        : comparison ? type != 5 || parameterType is not (1 or 2 or 3 or 4 or 6 or 7 or 8)
                        : type != parameterType || parameterType is not (1 or 2 or 3 or 4 or 7 or 8))
                    || parameter > parameters
                    || negativeLiteral && (shift
                        ? literalFirst && type is not (1 or 3)
                        : parameterType is not (1 or 3 or 7 or 8))
                    || !ValidSpan(expression.Span, nonempty: true))
                {
                    return false;
                }
                var text = source.Text.Substring(expression.Span.Start, expression.Span.Length);
                var lexical = Lexer.Lex(SourceText.From(text));
                var tokens = lexical.Tokens;
                var name = ParameterName(parameter);
                return lexical.Diagnostics.IsEmpty
                    && TryBinaryOperandTokens(tokens, (TokenKind)(binaryOperation % 100),
                        text.Length, out var first, out var second, literalFirst,
                        positiveLiteral ? TokenKind.Plus
                            : negativeLiteral ? TokenKind.Minus : TokenKind.EndOfFile)
                    && tokens[literalFirst ? second : first].Kind == TokenKind.Identifier
                    && tokens[literalFirst ? second : first].Name
                        == SourceName(source, name)
                    && tokens[literalFirst ? first : second].Kind == TokenKind.NumericLiteral
                    && (shift
                        ? ValidMagnitude(tokens[literalFirst ? first : second].Text.AsSpan(),
                            literalFirst ? type : 1, negativeLiteral, left, right)
                        : parameterType is 7 or 8
                        ? FloatingProjectionMatches(
                            tokens[literalFirst ? first : second].Text.AsSpan(),
                            parameterType, left, right)
                        : ValidMagnitude(tokens[literalFirst ? first : second].Text.AsSpan(),
                            parameterType, negativeLiteral, left, right));
            }
            if (operation == 3)
            {
                if (expression.Type != type || type is not (1 or 2 or 3 or 4 or 6 or 7 or 8)
                    || !ValidSpan(expression.Span, nonempty: true))
                {
                    return false;
                }
                var negative = source.Text[expression.Span.Start] == '-';
                var numberSpan = NumericSpan(source, expression.Span);
                var digits = source.Text.AsSpan(numberSpan.Start, numberSpan.Length);
                return type is 7 or 8
                    ? FloatingProjectionMatches(digits, type, left, right)
                    : ValidMagnitude(digits, type, negative, left, right);
            }
            if (operation is 20 or 21)
            {
                return type == 5 && expression.Type == 5 && left == 0 && right == 0
                    && ValidSpan(expression.Span, nonempty: true)
                    && source.Text.AsSpan(expression.Span.Start, expression.Span.Length)
                        .SequenceEqual((operation == 20 ? "true" : "false").AsSpan());
            }
            if (operation is >= 88 and <= 90)
            {
                return encodedOffset == 0
                    && expression.Type == type
                    && type == leftType && leftType is >= 1 and <= 4
                    && rightType == 1
                    && ValidSpan(expression.Span, nonempty: true)
                    && left >= 1 && left <= parameters
                    && right >= 1 && right <= parameters
                    && MatchesSource(expression, left, right);
            }
            return encodedOffset == 0 && expression.Type == type
                && ValidSpan(expression.Span, nonempty: true)
                && left >= 1 && left <= parameters
                && (operation == 0 ? right == 0 && leftType == type
                    : (operation is >= 57 and <= 61 or >= 65 and <= 75)
                        && right >= 1 && right <= parameters && leftType == rightType
                        && (operation is >= 57 and <= 61 or 71 or 73 or 75
                            ? type == leftType && (leftType is >= 1 and <= 4
                                || operation is 71 or 73 or 75 && leftType == 5)
                            : type == 5 && (operation is 72 or 74
                                ? leftType == 5 : leftType is >= 1 and <= 4 or 6
                                    || (operation is 65 or 66) && leftType == 5)))
                && MatchesSource(expression, left, right);
        }

        int ParameterType(int index) => index switch {
            1 => words[7], 2 => words[8], 3 => words[34], _ => 0,
        };

        TextSpan ParameterName(int index) => index switch {
            1 => firstName, 2 => secondName, 3 => thirdName, _ => default,
        };

        string CallParameterName(int nodeIndex, int parameterIndex) =>
            context is not null
                ? context.Callee(nodeIndex).ParameterNames[parameterIndex]
                : SourceName(source, ParameterName(parameterIndex + 1));

        bool MatchesSource(ScalarFrontendExpression expression, int left, int right)
        {
            var leftName = ParameterName(left);
            var span = expression.Span;
            if (span.Length == 0)
            {
                return false;
            }
            if (expression.Operation == 0)
            {
                if (span.Length == leftName.Length
                    && source.Text.AsSpan(span.Start, span.Length)
                        .SequenceEqual(source.Text.AsSpan(leftName.Start, leftName.Length)))
                {
                    return true;
                }
                var single = Lexer.Lex(SourceText.From(source.Text.Substring(span.Start, span.Length)));
                return single.Diagnostics.IsEmpty && single.Tokens.Length == 2
                    && single.Tokens[0].Kind == TokenKind.Identifier
                    && single.Tokens[0].Span.Length == span.Length
                    && single.Tokens[0].Name == SourceName(source, leftName);
            }

            var rightName = ParameterName(right);
            var groupedLeft = source.Text[span.Start] == '(';
            var middleLength = span.Length - leftName.Length - rightName.Length;
            var spelling = expression.Operation switch {
                57 => "+", 58 => "-", 59 => "*", 60 => "/", 61 => "%",
                65 => "==", 66 => "!=", 67 => "<",
                68 => "<=", 69 => ">", 70 => ">=", 71 => "&", 73 => "|",
                72 => "&&", 74 => "||", 75 => "^",
                88 => "<<", 89 => ">>", 90 => ">>>", _ => throw InvalidResult(),
            };
            if (!groupedLeft && middleLength >= 1
                && source.Text.AsSpan(span.Start, leftName.Length)
                    .SequenceEqual(source.Text.AsSpan(leftName.Start, leftName.Length))
                && source.Text.AsSpan(span.End - rightName.Length,
                    rightName.Length).SequenceEqual(source.Text.AsSpan(
                        rightName.Start, rightName.Length)))
            {
                var middle = source.Text.AsSpan(span.Start + leftName.Length, middleLength);
                if (middle.Trim().SequenceEqual(spelling.AsSpan()))
                {
                    return true;
                }
                var middleTokens = Lexer.Lex(SourceText.From(middle.ToString()));
                if (middleTokens.Diagnostics.IsEmpty && middleTokens.Tokens.Length == 2
                    && middleTokens.Tokens[0].Kind == (TokenKind)expression.Operation)
                {
                    return true;
                }
            }
            var lexical = Lexer.Lex(SourceText.From(
                source.Text.Substring(span.Start, span.Length)));
            var tokens = lexical.Tokens;
            if (!lexical.Diagnostics.IsEmpty || tokens.Length < 4
                || tokens[0].Span.Start != 0)
            {
                return false;
            }
            var leftIndex = 0;
            while (leftIndex < tokens.Length - 1
                && tokens[leftIndex].Kind == TokenKind.OpenParen)
            {
                leftIndex++;
            }
            var leftGroups = leftIndex;
            var operationIndex = leftIndex + leftGroups + 1;
            if (operationIndex >= tokens.Length - 1
                || tokens[leftIndex].Kind != TokenKind.Identifier
                || tokens[leftIndex].Name != SourceName(source, leftName)
                || tokens[operationIndex].Kind != (TokenKind)expression.Operation)
            {
                return false;
            }
            for (var index = 0; index < leftGroups; index++)
            {
                if (tokens[leftIndex + 1 + index].Kind != TokenKind.CloseParen)
                {
                    return false;
                }
            }
            var operandIndex = operationIndex + 1;
            while (operandIndex < tokens.Length - 1
                && tokens[operandIndex].Kind == TokenKind.OpenParen)
            {
                operandIndex++;
            }
            var groups = operandIndex - operationIndex - 1;
            if (tokens.Length != operandIndex + groups + 2
                || tokens[operandIndex].Kind != TokenKind.Identifier
                || tokens[operandIndex].Name != SourceName(source, rightName)
                || tokens[tokens.Length - 2].Span.End != span.Length)
            {
                return false;
            }
            for (var index = 0; index < groups; index++)
            {
                if (tokens[operandIndex + 1 + index].Kind != TokenKind.CloseParen)
                {
                    return false;
                }
            }
            return true;
        }

        bool AllZero(int first, int end)
        {
            for (var index = first; index < end; index++)
            {
                if (words[index] != 0)
                {
                    return false;
                }
            }
            return true;
        }
    }

    private static int UnaryOperandIndex(ImmutableArray<SyntaxToken> tokens)
    {
        var index = 1;
        while (index < tokens.Length - 1 && tokens[index].Kind == TokenKind.OpenParen)
        {
            index++;
        }
        return index;
    }

    private static bool TryBinaryOperandTokens(ImmutableArray<SyntaxToken> tokens,
        TokenKind operation, int length, out int left, out int right,
        bool literalFirst = false, TokenKind literalSign = TokenKind.EndOfFile)
    {
        var at = 0;
        left = -1;
        right = -1;
        if (tokens.Length < 4 || tokens[0].Span.Start != 0
            || !TakeOperand(literalFirst ? literalSign : TokenKind.EndOfFile, out left)
            || at >= tokens.Length - 1
            || tokens[at++].Kind != operation
            || !TakeOperand(literalFirst ? TokenKind.EndOfFile : literalSign, out right))
        {
            return false;
        }
        return at == tokens.Length - 1 && tokens[at - 1].Span.End == length;

        bool TakeOperand(TokenKind sign, out int operand)
        {
            operand = -1;
            var groups = 0;
            var foundSign = false;
            while (at < tokens.Length - 1)
            {
                if (tokens[at].Kind == TokenKind.OpenParen)
                {
                    at++;
                    groups++;
                }
                else if (!foundSign && sign != TokenKind.EndOfFile
                    && tokens[at].Kind == sign)
                {
                    at++;
                    foundSign = true;
                }
                else
                {
                    break;
                }
            }
            if (at >= tokens.Length - 1
                || (sign != TokenKind.EndOfFile && !foundSign))
            {
                return false;
            }
            operand = at++;
            for (var index = 0; index < groups; index++)
            {
                if (at >= tokens.Length - 1 || tokens[at++].Kind != TokenKind.CloseParen)
                {
                    return false;
                }
            }
            return true;
        }
    }

    private static bool TryUnaryOperand(ImmutableArray<SyntaxToken> tokens,
        TokenKind operation, int length, out int operandIndex)
    {
        operandIndex = UnaryOperandIndex(tokens);
        var groups = operandIndex - 1;
        if (tokens.Length != 3 + groups * 2 || tokens[0].Kind != operation
            || tokens[0].Span.Start != 0 || tokens[tokens.Length - 2].Span.End != length)
        {
            return false;
        }
        for (var index = 0; index < groups; index++)
        {
            if (tokens[operandIndex + 1 + index].Kind != TokenKind.CloseParen)
            {
                return false;
            }
        }
        return true;
    }

    private static bool IsNegatedBooleanBinary(int operation)
    {
        if (operation >= 4000)
        {
            return false;
        }
        if (operation is >= 2065 and <= 2075)
        {
            return true;
        }
        var nested = operation - 2000;
        if ((nested / 100 is 7 or 9 or 11)
            && (nested % 100 is 65 or 66 or >= 71 and <= 75))
        {
            return true;
        }
        var comparison = nested >= 1600 ? nested - 1600
            : nested >= 1200 ? nested - 1200 : nested;
        return (comparison / 100 is >= 1 and <= 4)
            && (comparison % 100 is >= 65 and <= 70);
    }

    private static bool IsComplementedIntegerBinary(int operation)
    {
        if (operation is < 4000 or >= 6000)
        {
            return false;
        }
        var nested = operation - 4000;
        var binary = nested >= 1600 ? nested - 1600
            : nested >= 1200 ? nested - 1200 : nested;
        return binary is >= 57 and <= 61 or 71 or 73 or 75
            || (binary / 100 is >= 1 and <= 5)
                && (binary % 100 is >= 57 and <= 61 or 71 or 73 or 75);
    }

    private static bool IsSignedIntegerBinary(int operation)
    {
        if (operation is < 6000 or >= 10000)
        {
            return false;
        }
        var offset = operation >= 8000 ? 8000 : 6000;
        return IsComplementedIntegerBinary(operation - offset + 4000);
    }

    private static bool TryGroupedUnarySpan(SourceText source, TextSpan span,
        TokenKind prefix, out TextSpan nested)
    {
        nested = default;
        var lexical = Lexer.Lex(SourceText.From(source.Text.Substring(span.Start, span.Length)));
        var tokens = lexical.Tokens;
        if (!lexical.Diagnostics.IsEmpty || tokens.Length < 6
            || tokens[0].Kind != prefix || tokens[0].Span.Start != 0)
        {
            return false;
        }
        var last = tokens.Length - 2;
        if (tokens[1].Kind != TokenKind.OpenParen
            || tokens[last].Kind != TokenKind.CloseParen
            || tokens[last].Span.End != span.Length)
        {
            return false;
        }
        var openings = new int[tokens.Length];
        var closing = new int[tokens.Length];
        var depth = 0;
        for (var index = 1; index <= last; index++)
        {
            if (tokens[index].Kind == TokenKind.OpenParen)
            {
                openings[depth++] = index;
            }
            else if (tokens[index].Kind == TokenKind.CloseParen)
            {
                if (depth == 0)
                {
                    return false;
                }
                closing[openings[--depth]] = index;
            }
            if (depth == 0 && index < last)
            {
                return false;
            }
        }
        if (depth != 0)
        {
            return false;
        }
        var at = 2;
        last--;
        while (at < last && tokens[at].Kind == TokenKind.OpenParen
            && closing[at] == last)
        {
            at++;
            last--;
        }
        nested = new TextSpan(span.Start + tokens[at].Span.Start,
            tokens[last].Span.End - tokens[at].Span.Start);
        return true;
    }

    private static bool ScalarType(int type) => type is >= 1 and <= 8;

    private static bool IsUncontextualShiftRoot(
        ImmutableArray<ScalarFrontendNode> nodes, ReadOnlySpan<int> intrinsicTypes, int root)
    {
        if ((uint)root >= (uint)nodes.Length)
        {
            return false;
        }
        while (nodes[root].Operation is 46 or 110)
        {
            root = nodes[root].Left;
            if ((uint)root >= (uint)nodes.Length)
            {
                return false;
            }
        }
        var expression = nodes[root];
        return expression.Operation is >= 88 and <= 90
            && (uint)expression.Left < (uint)nodes.Length
            && intrinsicTypes[expression.Left] == 0;
    }

    private static bool SupportedScalarConversion(int sourceType, int targetType)
        => ((sourceType is 1 or 2 or 3 or 4 or 6)
                && (targetType is 1 or 2 or 3 or 4 or 6))
            || ((sourceType is 1 or 2 or 3 or 4 or 6 or 7 or 8)
                && (targetType is 7 or 8))
            || ((sourceType is 7 or 8)
                && (targetType is 1 or 2 or 3 or 4));

    private static bool TryParseFloatingLiteral(ReadOnlySpan<char> text, int type, out long value)
    {
        value = 0;
        if (type is not (7 or 8) || text.IsEmpty)
        {
            return false;
        }

        var lexical = Lexer.Lex(SourceText.From(text.ToString()));
        var tokens = lexical.Tokens;
        if (!lexical.Diagnostics.IsEmpty || tokens.Length != 2
            || tokens[0].Kind != TokenKind.NumericLiteral
            || tokens[0].Span.Start != 0 || tokens[0].Span.Length != text.Length
            || tokens[1].Kind != TokenKind.EndOfFile)
        {
            return false;
        }

        var primitiveType = type == 7 ? PrimitiveType.Float32 : PrimitiveType.Float64;
        return FloatingLiteralParser.TryParse(tokens[0].Text.AsSpan(), primitiveType, out value);
    }

    private static bool FloatingProjectionMatches(ReadOnlySpan<char> text,
        int type, int low, int high)
    {
        return TryParseFloatingLiteral(text, type, out var expected)
            && (type == 8 || high == 0)
            && FloatingProjectionValue(type, low, high) == expected;
    }

    private static long FloatingProjectionValue(int type, int low, int high)
        => type switch {
            7 => (uint)low,
            8 => unchecked((long)((ulong)(uint)high << 32 | (uint)low)),
            _ => throw InvalidResult(),
        };

    private static bool ValidMagnitude(ReadOnlySpan<char> digits, int type, bool negative,
        int low, int high)
    {
        if (digits.IsEmpty || type is not (1 or 2 or 3 or 4 or 6)
            || (negative && type is not (1 or 3))
            || !ScannerHost.TryParseIntegerMagnitude(digits, out var magnitude))
        {
            return false;
        }
        var based = ScannerHost.IntegerRadix(digits) != 10;
        ulong max = type switch {
            1 when based => uint.MaxValue,
            1 when negative => (ulong)int.MaxValue + 1,
            1 => int.MaxValue,
            2 => uint.MaxValue,
            3 when based => ulong.MaxValue,
            3 when negative => (ulong)long.MaxValue + 1,
            3 => long.MaxValue,
            4 => ulong.MaxValue,
            _ => ushort.MaxValue,
        };
        return magnitude <= max && magnitude == unchecked((ulong)(uint)high << 32 | (uint)low);
    }

    private static TextSpan NumericSpan(SourceText source, TextSpan expression)
    {
        if (source.Text[expression.Start] != '-')
        {
            return expression;
        }
        var lexical = Lexer.Lex(SourceText.From(source.Text.Substring(expression.Start, expression.Length)));
        var tokens = lexical.Tokens;
        if (!lexical.Diagnostics.IsEmpty || tokens.Length != 3
            || tokens[0].Kind != TokenKind.Minus || tokens[0].Span.Start != 0
            || tokens[1].Kind != TokenKind.NumericLiteral || tokens[1].Span.End != expression.Length)
        {
            throw InvalidResult();
        }
        return new TextSpan(expression.Start + tokens[1].Span.Start, tokens[1].Span.Length);
    }

    private static string SourceName(SourceText source, TextSpan span)
        => IdentifierName.GetName(source.Text.Substring(span.Start, span.Length));

    private static string TypeName(int code) => code switch {
        0 => "void",
        1 => "int32", 2 => "uint32", 3 => "int64", 4 => "uint64",
        5 => "bool", 6 => "char16", 7 => "float32", 8 => "float64",
        _ => throw InvalidResult(),
    };

    private static bool MatchesScalarType(SyntaxToken token, int code,
        ScalarAliasEnvironment aliases)
    {
        if (code == 0)
        {
            return token.Kind == TokenKind.VoidKeyword;
        }
        return token.Kind == TokenKind.Identifier
            && (token.Name == TypeName(code)
                || aliases.TryGetType(token.Name, out var aliasType) && aliasType == code);
    }

    private static bool MatchesPrimitiveScalarType(SyntaxToken token, int code)
        => code is >= 1 and <= 8 && token.Kind == TokenKind.Identifier
            && token.Name == TypeName(code);

    private static TextSpan ConstantLocalSpan(SourceText source, TextSpan declaration)
    {
        var lexical = Lexer.Lex(SourceText.From(source.Text.Substring(
            declaration.Start, declaration.Length)));
        if (!lexical.Diagnostics.IsEmpty || lexical.Tokens.Length < 3
            || lexical.Tokens[0].Kind != TokenKind.Identifier
            || lexical.Tokens[0].Text != "const"
            || lexical.Tokens[1].Kind != TokenKind.Identifier)
        {
            throw InvalidResult();
        }
        var start = declaration.Start + lexical.Tokens[1].Span.Start;
        return new TextSpan(start, declaration.End - start);
    }

    /// <summary>Builds a verifier-checked scalar library with source-declared visibility from native frontend facts.</summary>
    public static BoundProgram BindLibrary(SourceText source, ReadOnlySpan<byte> bytes,
        BoundRuntimePolicy policy)
    {
        var result = ReadResult(source, bytes, policy);
        if (!result.IsValid)
        {
            throw InvalidResult();
        }
        return BindLibraryCore(source, result, policy);
    }

    private static BoundExpression FoldBoundedConstantExpression(BoundExpression expression,
        BoundConstant?[] constantLocals)
    {
        switch (expression)
        {
            case BoundRead read when constantLocals[read.Variable] is { } constant:
                return constant with { Span = read.Span };
            case BoundConditional conditional:
            {
                var condition = FoldBoundedConstantExpression(conditional.Condition, constantLocals);
                if (condition is BoundConstant { Type: PrimitiveType.Bool } value)
                {
                    var selected = FoldBoundedConstantExpression(value.Value != 0
                        ? conditional.WhenTrue : conditional.WhenFalse, constantLocals);
                    return selected is BoundConstant constant && constant.Type == conditional.Type
                        ? constant with { Span = conditional.Span }
                        : expression;
                }
                return expression;
            }
            case BoundUnary unary:
                return Binder.FoldScalarConstant(unary with {
                    Operand = FoldBoundedConstantExpression(unary.Operand, constantLocals),
                });
            case BoundBinary binary:
            {
                var left = FoldBoundedConstantExpression(binary.Left, constantLocals);
                if (binary.Operator is TokenKind.AmpersandAmpersand or TokenKind.PipePipe
                    && left is BoundConstant { Type: PrimitiveType.Bool } boolean
                    && (binary.Operator == TokenKind.AmpersandAmpersand && boolean.Value == 0
                        || binary.Operator == TokenKind.PipePipe && boolean.Value != 0))
                {
                    return new BoundConstant(boolean.Value == 0 ? 0 : 1,
                        PrimitiveType.Bool, binary.Span);
                }
                return Binder.FoldScalarConstant(binary with {
                    Left = left,
                    Right = FoldBoundedConstantExpression(binary.Right, constantLocals),
                });
            }
            case BoundCast cast:
                return Binder.FoldScalarConstant(cast with {
                    Operand = FoldBoundedConstantExpression(cast.Operand, constantLocals),
                });
            default:
                return expression;
        }
    }

    private static BoundProgram BindLibraryCore(SourceText source, ScalarFrontendResult result,
        BoundRuntimePolicy policy, bool verify = true, bool omitImplicitReturn = false)
    {
        var variables = ImmutableArray.CreateBuilder<BoundVariable>(result.ParameterCount);
        if (result.ParameterCount > 0)
        {
            var isOut = (result.OutParameterMask & 1) != 0;
            var isInit = (result.InitRefParameterMask & 1) != 0;
            var isOutput = isOut || isInit;
            variables.Add(new BoundVariable(Name(result.FirstName), Type(result.FirstType),
                isOutput, result.FirstName) { IsOut = isOutput, IsInit = isInit });
        }
        if (result.ParameterCount >= 2)
        {
            var isOut = (result.OutParameterMask & 2) != 0;
            var isInit = (result.InitRefParameterMask & 2) != 0;
            var isOutput = isOut || isInit;
            variables.Add(new BoundVariable(Name(result.SecondName), Type(result.SecondType),
                isOutput, result.SecondName) { IsOut = isOutput, IsInit = isInit });
        }
        if (result.ParameterCount == 3)
        {
            var isOut = (result.OutParameterMask & 4) != 0;
            var isInit = (result.InitRefParameterMask & 4) != 0;
            var isOutput = isOut || isInit;
            variables.Add(new BoundVariable(Name(result.ThirdName), Type(result.ThirdType),
                isOutput, result.ThirdName) { IsOut = isOutput, IsInit = isInit });
        }
        if (!result.Locals.IsDefault)
        {
            foreach (var local in result.Locals)
            {
                var hidden = Name(local.Name) == "_";
                var switchSelector = local.IsSwitchSelector;
                var span = switchSelector
                    ? result.Statements.Single(statement => statement.Kind == 38
                        && statement.SwitchLocal == variables.Count).Span
                    : hidden
                    ? result.Statements.Single(statement => statement.Kind == 15
                        && statement.Slot == variables.Count + 1).Span
                    : local.Name;
                variables.Add(new BoundVariable(switchSelector ? "<switch-selector>"
                    : hidden ? "<discard>" : Name(local.Name),
                    Type(local.Type), local.IsMutable, span) {
                    IsInitializationLocal = local.IsInitialization,
                });
            }
        }
        var types = variables.ToImmutable();
        var methodSpan = new TextSpan(0, source.Length);
        var callableBodySpan = methodSpan;
        var hasElseIf = !result.Statements.IsDefault
            && result.Statements.Any(static statement => statement.Kind == 23);
        var hasSwitch = !result.Statements.IsDefault
            && result.Statements.Any(static statement => statement.Kind == 38);
        var hostedScopes = hasElseIf || hasSwitch || result.IsNoThrow || result.DoesNotReturn;
        var returnType = Type(result.ReturnType);
        var constantLocals = new BoundConstant?[types.Length];
        var constantDeclarations = new Dictionary<int, int>();
        if (!result.Statements.IsDefault)
        {
            foreach (var statement in result.Statements)
            {
                if (statement.Kind is not (27 or 28))
                {
                    continue;
                }
                constantDeclarations.Add(statement.Root, statement.Slot - 1);
            }
        }
        BoundExpression[]? treeExpressions = null;
        if (!result.Nodes.IsDefault)
        {
            treeExpressions = new BoundExpression[result.Nodes.Length];
            for (var i = 0; i < treeExpressions.Length; i++)
            {
                var node = result.Nodes[i];
                var type = Type(node.Type);
                BoundExpression bound;
                if (node.Operation == 46)
                {
                    bound = treeExpressions[node.Left];
                }
                else if (node.Operation == 0)
                {
                    bound = new BoundRead(node.Left - 1, type, node.Span);
                }
                else if (node.Operation is 20 or 21)
                {
                    bound = new BoundConstant(node.Operation == 20 ? 1 : 0, type, node.Span);
                }
                else if (node.Operation == 3)
                {
                    var value = node.Type is 7 or 8
                        ? FloatingProjectionValue(node.Type, node.Low, node.High)
                        : node.Type is 1 or 2 or 6 ? node.Low
                            : unchecked((long)((ulong)(uint)node.High << 32 | (uint)node.Low));
                    bound = new BoundConstant(value, type, node.Span);
                }
                else if (node.Operation is 103 or 104)
                {
                    bound = new BoundOutArgument(node.Left - 1,
                        node.Operation == 104, type, node.Span);
                }
                else if (node.Operation is 107 or 108)
                {
                    bound = new BoundOutArgument(node.Left - 1,
                        node.Operation == 108, type, node.Span) { IsInit = true };
                }
                else if (node.Operation == 109)
                {
                    bound = new BoundIsInitialized(result.Nodes[node.Left].Left - 1, node.Span);
                }
                else if (node.Operation == 110)
                {
                    bound = treeExpressions[node.Left];
                }
                else if (node.Operation is 102 or 105)
                {
                    var argumentCount = node.Operation == 105 ? node.High : result.ParameterCount;
                    var arguments = ImmutableArray.CreateBuilder<BoundExpression>(argumentCount);
                    var argumentNodes = new[] { node.Left, node.Right, node.Low };
                    for (var ordinal = 0; ordinal < argumentCount; ordinal++)
                    {
                        arguments.Add(treeExpressions[argumentNodes[ordinal]]);
                    }
                    var isUnsafeAuthorized = !result.Statements.IsDefault
                        && result.Statements.Any(statement => statement.Kind == 31
                            && statement.BodySpan.Start <= node.Span.Start
                            && node.Span.End <= statement.BodySpan.End)
                        || result.Nodes.Any(scope => scope.Operation == 110
                            && scope.Span.Start <= node.Span.Start
                            && node.Span.End <= scope.Span.End);
                    bound = new BoundCall(node.Operation == 105 ? result.CallTargets[i] : 0,
                        arguments.ToImmutable(), type, node.Span) {
                        ArgumentParameterIndices = node.ArgumentParameterIndices,
                        IsUnsafeAuthorized = isUnsafeAuthorized,
                    };
                }
                else if (node.Operation == 83)
                {
                    bound = node.Type == result.Nodes[node.Left].Type
                        ? treeExpressions[node.Left]
                        : Binder.FoldScalarConstant(new BoundCast(
                            treeExpressions[node.Left], type, node.Span));
                }
                else if (node.Operation == 84)
                {
                    bound = new BoundConditional(treeExpressions[node.Left],
                        treeExpressions[node.Right], treeExpressions[node.Low], type, node.Span);
                }
                else if (node.Operation is 86 or 87)
                {
                    bound = new BoundUpdate((TokenKind)node.Operation,
                        treeExpressions[node.Left], node.Low == 1, type, node.Span);
                }
                else if (node.Operation is >= 91 and <= 101)
                {
                    bound = new BoundCompoundAssignment((TokenKind)node.Operation,
                        treeExpressions[node.Left], treeExpressions[node.Right], type, node.Span);
                }
                else if (node.Right == -1)
                {
                    var operand = treeExpressions[node.Left];
                    if (node.Operation == (int)TokenKind.Minus
                        && result.Nodes[node.Left].Operation == 3
                        && ScannerHost.IntegerRadix(source.Text.AsSpan(
                            operand.Span.Start, operand.Span.Length)) == 10
                        && ((node.Type == 1 && unchecked((uint)result.Nodes[node.Left].Low) == (ulong)int.MaxValue + 1)
                            || (node.Type == 3 && unchecked((ulong)(uint)result.Nodes[node.Left].High << 32
                                | (uint)result.Nodes[node.Left].Low) == (ulong)long.MaxValue + 1)))
                    {
                        bound = new BoundConstant(node.Type == 1 ? int.MinValue : long.MinValue,
                            type, node.Span);
                    }
                    else
                    {
                        bound = Binder.FoldScalarConstant(new BoundUnary(
                            (TokenKind)node.Operation, operand, type, node.Span));
                    }
                }
                else
                {
                    bound = Binder.FoldScalarConstant(new BoundBinary(
                        (TokenKind)node.Operation, treeExpressions[node.Left],
                        treeExpressions[node.Right], type, node.Span));
                }
                treeExpressions[i] = bound;
                if (constantDeclarations.TryGetValue(i, out var localIndex))
                {
                    if (FoldBoundedConstantExpression(bound, constantLocals) is not BoundConstant constant
                        || constant.Type != types[localIndex].Type)
                    {
                        throw InvalidResult();
                    }
                    constantLocals[localIndex] = constant;
                }
            }
        }
        if (!result.Statements.IsDefault)
        {
            foreach (var proof in result.Statements.Where(static statement => statement.Kind is 35 or 36))
            {
                if (treeExpressions is null
                    || FoldBoundedConstantExpression(treeExpressions[proof.Root], constantLocals) is not BoundConstant {
                        Type: PrimitiveType.Bool, Value: 1,
                    })
                {
                    throw InvalidResult();
                }
            }
        }
        var switchConstants = new Dictionary<int, BoundConstant>();
        if (!result.Statements.IsDefault)
        {
            for (var index = 0; index < result.Statements.Length; index++)
            {
                var statement = result.Statements[index];
                if (statement.Kind != 38)
                {
                    continue;
                }
                var selectorType = Type(result.Nodes[statement.Root].Type);
                var priorConstants = new List<BoundConstant>();
                for (var armIndex = index + 1; armIndex < statement.Slot;)
                {
                    var arm = result.Statements[armIndex];
                    if (arm.Kind != 39)
                    {
                        throw InvalidResult();
                    }
                    if (arm.Root >= 0)
                    {
                        if (treeExpressions is null
                            || FoldBoundedConstantExpression(treeExpressions[arm.Root], constantLocals)
                                is not BoundConstant constant
                            || constant.Type != selectorType)
                        {
                            throw InvalidResult();
                        }
                        foreach (var prior in priorConstants)
                        {
                            if (Binder.FoldScalarConstant(new BoundBinary(TokenKind.EqualsEquals,
                                    prior, constant, PrimitiveType.Bool, arm.Span))
                                is BoundConstant { Value: not 0 })
                            {
                                throw InvalidResult();
                            }
                        }
                        priorConstants.Add(constant);
                        switchConstants.Add(armIndex, constant);
                    }
                    armIndex = arm.Slot;
                }
            }
        }
        var body = ImmutableArray.CreateBuilder<BoundStatement>();
        if (!result.Statements.IsDefault)
        {
            var expressions = treeExpressions ?? throw InvalidResult();
            var at = 0;
            var loops = new System.Collections.Generic.List<int>();
            body.AddRange(LowerStatements(result.Statements.Length));

            ImmutableArray<BoundStatement> LowerLoopBody(int end, int header)
            {
                loops.Add(header);
                var nested = LowerStatements(end);
                loops.RemoveAt(loops.Count - 1);
                return nested;
            }

            ImmutableArray<BoundStatement> LowerStatements(int end)
            {
                var statements = ImmutableArray.CreateBuilder<BoundStatement>();
                while (at < end)
                {
                    var statement = result.Statements[at++];
                    var kind = StatementKind(statement.Kind);
                    var loopSpan = statement.Kind is >= 16 and <= 18 ? statement.LoopSpan : statement.Span;
                    if (kind == 29)
                    {
                        var body = LowerStatements(statement.Slot);
                        var cleanup = result.Statements[at++];
                        statements.Add(new BoundTryFinally(
                            new BoundBlock(body, statement.BodySpan) { IsManagedLexicalScope = true },
                            new BoundBlock(LowerStatements(cleanup.Slot), cleanup.BodySpan) {
                                IsManagedLexicalScope = true,
                            }, statement.Span));
                    }
                    else if (kind == 31)
                    {
                        statements.Add(new BoundUnsafe(new BoundBlock(
                            LowerStatements(statement.Slot), statement.BodySpan) {
                                IsManagedLexicalScope = true,
                            }, statement.Span));
                    }
                    else if (kind == 32)
                    {
                        var body = LowerStatements(statement.Slot);
                        var handler = result.Statements[at++];
                        var handlerBody = LowerStatements(handler.Slot);
                        BoundBlock? cleanup = null;
                        if (at < end && result.Statements[at].Kind == 34)
                        {
                            var cleanupStatement = result.Statements[at++];
                            cleanup = new BoundBlock(LowerStatements(cleanupStatement.Slot),
                                cleanupStatement.BodySpan) { IsManagedLexicalScope = true };
                        }
                        statements.Add(new BoundTryCatch(
                            new BoundBlock(body, statement.BodySpan) { IsManagedLexicalScope = true },
                            new BoundBlock(handlerBody, handler.BodySpan) { IsManagedLexicalScope = true },
                            cleanup, statement.Span));
                    }
                    else if (kind == 14)
                    {
                        statements.Add(new BoundBlock(LowerStatements(statement.Slot), statement.BodySpan) {
                            IsManagedLexicalScope = true,
                        });
                    }
                    else if (kind == 38)
                    {
                        var arms = new List<(int Index, ScalarFrontendStatement Statement,
                            ImmutableArray<BoundStatement> Body)>();
                        while (at < statement.Slot)
                        {
                            var armIndex = at++;
                            var arm = result.Statements[armIndex];
                            arms.Add((armIndex, arm, LowerStatements(arm.Slot)));
                        }
                        BoundStatement? alternate = null;
                        for (var index = arms.Count - 1; index >= 0; index--)
                        {
                            var (armIndex, arm, armBody) = arms[index];
                            var block = new BoundBlock(armBody, arm.BodySpan) {
                                IsManagedLexicalScope = hostedScopes,
                            };
                            if (arm.Root == -1)
                            {
                                alternate = block;
                                continue;
                            }
                            var condition = new BoundBinary(TokenKind.EqualsEquals,
                                new BoundRead(statement.SwitchLocal, Type(result.Nodes[statement.Root].Type),
                                    arm.Span),
                                switchConstants[armIndex],
                                PrimitiveType.Bool, arm.Span);
                            alternate = new BoundIf(condition, block, alternate, arm.Span);
                        }
                        if (alternate is null)
                        {
                            throw InvalidResult();
                        }
                        statements.Add(new BoundBlock([
                            new BoundLocal(statement.SwitchLocal, expressions[statement.Root], statement.Span),
                            alternate,
                        ], statement.Span) { IsManagedLexicalScope = true });
                    }
                    else if (kind == 3)
                    {
                        var value = expressions[statement.Root];
                        var nested = LowerLoopBody(statement.Slot, at - 1);
                        statements.Add(new BoundWhile(value, new BoundBlock(nested,
                            statement.BodySpan) { IsManagedLexicalScope = hostedScopes }, loopSpan));
                    }
                    else if (kind == 11)
                    {
                        var nested = LowerLoopBody(statement.Slot, at - 1);
                        statements.Add(new BoundWhile(expressions[statement.Root],
                            new BoundBlock(nested, statement.BodySpan) { IsManagedLexicalScope = hostedScopes }, loopSpan) {
                            IsPostTested = true,
                        });
                    }
                    else if (kind == 12)
                    {
                        var header = at - 1;
                        var initializer = LowerStatements(at + 1)[0];
                        var iterator = LowerStatements(at + 1)[0];
                        var nested = new BoundBlock(LowerLoopBody(statement.Slot, header),
                            statement.BodySpan) { IsManagedLexicalScope = hostedScopes };
                        var hasContinue = result.Statements.Skip(header + 3)
                            .Take(statement.Slot - header - 3)
                            .Any(entry => entry.Kind is 7 or 20 && entry.Slot == header);
                        var loopBody = hasContinue ? nested
                            : new BoundBlock([nested, iterator], loopSpan);
                        var condition = statement.Root == -1
                            ? new BoundConstant(1, PrimitiveType.Bool, loopSpan)
                            : expressions[statement.Root];
                        var loop = new BoundWhile(condition, loopBody, loopSpan) {
                            Iterator = hasContinue ? iterator : null,
                        };
                        statements.Add(new BoundBlock([initializer, loop], loopSpan) {
                            IsManagedLexicalScope = true,
                        });
                    }
                    else if (statement.Kind == 4)
                    {
                        var value = expressions[statement.Root];
                        var whenTrue = LowerStatements(statement.Slot);
                        BoundStatement? whenFalse = null;
                        if (at < end && result.Statements[at].Kind is 5 or 23)
                        {
                            var marker = result.Statements[at++];
                            var alternate = LowerStatements(marker.Slot);
                            whenFalse = marker.Kind == 23 ? alternate[0]
                                : new BoundBlock(alternate, marker.BodySpan) { IsManagedLexicalScope = hostedScopes };
                        }
                        statements.Add(new BoundIf(value,
                            new BoundBlock(whenTrue, statement.BodySpan) { IsManagedLexicalScope = hostedScopes },
                            whenFalse, statement.Span));
                    }
                    else if (kind is 6 or 7)
                    {
                        var selected = loops.LastIndexOf(statement.Slot);
                        if (selected < 0)
                        {
                            throw InvalidResult();
                        }
                        var depth = loops.Count - selected;
                        statements.Add(kind == 6
                            ? new BoundBreak(statement.Span) { TargetDepth = depth }
                            : new BoundContinue(statement.Span) { TargetDepth = depth });
                    }
                    else if (statement.Kind == 8)
                    {
                        statements.Add(new BoundReturn(expressions[statement.Root], statement.Span));
                    }
                    else if (statement.Kind == 21)
                    {
                        statements.Add(new BoundRequire(expressions[statement.Root], statement.Span));
                    }
                    else if (statement.Kind is 35 or 36)
                    {
                        statements.Add(new BoundBlock([], statement.Span));
                    }
                    else if (statement.Kind == 22)
                    {
                        statements.Add(new BoundReturn(null, statement.Span));
                    }
                    else if (statement.Kind is 9 or 10 or 37)
                    {
                        statements.Add(new BoundExpressionStatement(expressions[statement.Root],
                            statement.Span));
                    }
                    else if (statement.Kind == 24)
                    {
                        statements.Add(new BoundLifetimeEnd(statement.Slot - 1, -1, statement.Span));
                    }
                    else if (statement.Kind is 25 or 26)
                    {
                        statements.Add(new BoundVacantLocal(statement.Slot - 1, statement.Span));
                    }
                    else if (statement.Kind == 15)
                    {
                        statements.Add(new BoundBlock([
                            new BoundLocal(statement.Slot - 1, expressions[statement.Root],
                                statement.Span)], statement.Span) {
                            IsManagedLexicalScope = true,
                        });
                    }
                    else if (statement.Kind == 13)
                    {
                        statements.Add(new BoundBlock([], statement.Span));
                    }
                    else
                    {
                        var value = expressions[statement.Root];
                        if (statement.Kind is 27 or 28
                            && constantLocals[statement.Slot - 1] is { } constant)
                        {
                            value = constant with { Span = value.Span };
                        }
                        statements.Add(statement.Kind is 1 or 27 or 28
                            ? new BoundLocal(statement.Slot - 1, value,
                                statement.Kind is 27 or 28
                                    ? ConstantLocalSpan(source, statement.Span)
                                    : statement.Span)
                            : new BoundAssignment(statement.Slot - 1, value, statement.Span));
                    }
                }
                return statements.ToImmutable();
            }
        }
        if (result.HasCondition)
        {
            var conditional = new BoundIf(treeExpressions is null
                    ? Expression(result.Condition) : treeExpressions[result.ConditionRoot],
                new BoundBlock([Return(result.WhenTrue)], result.WhenTrue.Span),
                result.HasFallthrough ? null
                    : new BoundBlock([Return(result.WhenFalse)], result.WhenFalse.Span),
                new TextSpan(result.Condition.Span.Start,
                    (result.HasFallthrough ? result.WhenTrue.Span.End : result.WhenFalse.Span.End)
                        - result.Condition.Span.Start));
            body.Add(conditional);
            if (result.HasFallthrough)
            {
                body.Add(Return(result.WhenFalse));
            }
        }
        else if (result.ReturnType != 0 && !omitImplicitReturn
            && (result.Statements.IsDefaultOrEmpty
            || !result.Statements.Any(static statement => statement.Kind == 8)
                && (result.TrueRoot >= 0
                    || !result.Statements.Any(static statement => statement.Kind is 11 or 12 or 17 or 18 or 21))))
        {
            body.Add(Return(result.WhenTrue));
        }

        if (returnType == PrimitiveType.Void && !result.DoesNotReturn)
        {
            var tokens = Lexer.Lex(source).Tokens;
            body.Add(new BoundReturn(null, new TextSpan(tokens[^2].Span.End, 0)));
            if (result.IsExpressionBody)
            {
                methodSpan = new TextSpan(tokens[0].Span.Start, tokens[^2].Span.End - tokens[0].Span.Start);
                var arrow = tokens.First(static token => token.Kind == TokenKind.Arrow);
                callableBodySpan = new TextSpan(arrow.Span.Start, tokens[^2].Span.End - arrow.Span.Start);
            }
        }
        if (hostedScopes)
        {
            var tokens = Lexer.Lex(source).Tokens;
            methodSpan = new TextSpan(tokens[0].Span.Start, tokens[^2].Span.End - tokens[0].Span.Start);
            var opener = tokens.First(token => token.Kind == (result.IsExpressionBody ? TokenKind.Arrow : TokenKind.OpenBrace));
            callableBodySpan = new TextSpan(opener.Span.Start, tokens[^2].Span.End - opener.Span.Start);
            if (result.ReturnType != 0 && result.IsExpressionBody)
            {
                if (body.Count != 1 || body[0] is not BoundReturn returned)
                {
                    throw InvalidResult();
                }
                body[0] = returned with { Span = callableBodySpan };
            }
        }

        var function = new BoundFunction(Name(result.Name), returnType, types, result.ParameterCount,
            new BoundBlock(body.ToImmutable(), callableBodySpan) {
                IsManagedLexicalScope = hostedScopes || returnType == PrimitiveType.Void && result.IsExpressionBody,
            }, methodSpan) {
                IsInternal = !result.IsPublic, IsNoThrow = result.IsNoThrow,
                RequiresUnsafeCaller = result.RequiresUnsafeCaller, DoesNotReturn = result.DoesNotReturn,
            };
        var program = new BoundProgram([function], -1) { RuntimePolicy = policy };
        if (verify)
        {
            SemanticVerifier.VerifyLibrary(program);
        }
        return program;

        string Name(TextSpan span) => SourceName(source, span);

        BoundReturn Return(ScalarFrontendExpression expression)
            => treeExpressions is null
                ? new BoundReturn(Expression(expression), expression.Span)
                : new BoundReturn(treeExpressions[expression == result.WhenTrue
                    ? result.TrueRoot : result.FalseRoot], expression.Span);

        BoundExpression Expression(ScalarFrontendExpression expression)
        {
            var encodedOffset = expression.Operation >= 40000 ? 40000 : 0;
            var operation = expression.Operation - encodedOffset;
            if (operation == 62)
            {
                var lexical = Lexer.Lex(SourceText.From(source.Text.Substring(
                    expression.Span.Start, expression.Span.Length)));
                var token = lexical.Tokens[UnaryOperandIndex(lexical.Tokens)];
                var operandSpan = new TextSpan(expression.Span.Start + token.Span.Start, token.Span.Length);
                BoundExpression operand = expression.LeftParameter == 0
                    ? new BoundConstant(expression.RightParameter == 20 ? 1 : 0,
                        PrimitiveType.Bool, operandSpan)
                    : new BoundRead(expression.LeftParameter - 1, PrimitiveType.Bool, operandSpan);
                return Binder.FoldScalarConstant(new BoundUnary(TokenKind.Bang, operand,
                    PrimitiveType.Bool, expression.Span));
            }
            if (operation is 63 or 163)
            {
                var lexical = Lexer.Lex(SourceText.From(source.Text.Substring(
                    expression.Span.Start, expression.Span.Length)));
                var token = lexical.Tokens[UnaryOperandIndex(lexical.Tokens)];
                var operandSpan = new TextSpan(expression.Span.Start + token.Span.Start, token.Span.Length);
                var type = Type(expression.Type);
                BoundExpression operand;
                if (operation == 63)
                {
                    operand = new BoundRead(expression.LeftParameter - 1, type, operandSpan);
                }
                else
                {
                    var value = expression.Type is 1 or 2
                        ? expression.LeftParameter
                        : unchecked((long)((ulong)(uint)expression.RightParameter << 32
                            | (uint)expression.LeftParameter));
                    operand = new BoundConstant(value, type, operandSpan);
                }
                return Binder.FoldScalarConstant(new BoundUnary(TokenKind.Tilde, operand,
                    type, expression.Span));
            }
            if (operation is 57 or 58 && expression.RightParameter == 0)
            {
                var lexical = Lexer.Lex(SourceText.From(source.Text.Substring(
                    expression.Span.Start, expression.Span.Length)));
                var token = lexical.Tokens[UnaryOperandIndex(lexical.Tokens)];
                var operandSpan = new TextSpan(expression.Span.Start + token.Span.Start, token.Span.Length);
                var type = Type(expression.Type);
                return new BoundUnary((TokenKind)operation,
                    new BoundRead(expression.LeftParameter - 1, type, operandSpan),
                    type, expression.Span);
            }
            if (operation is 657 or 658)
            {
                var lexical = Lexer.Lex(SourceText.From(source.Text.Substring(
                    expression.Span.Start, expression.Span.Length)));
                var token = lexical.Tokens[UnaryOperandIndex(lexical.Tokens)];
                var operandSpan = new TextSpan(expression.Span.Start + token.Span.Start, token.Span.Length);
                var type = Type(expression.Type);
                var value = expression.Type is 7 or 8
                    ? FloatingProjectionValue(expression.Type,
                        expression.LeftParameter, expression.RightParameter)
                    : expression.Type is 1 or 2
                        ? expression.LeftParameter
                        : unchecked((long)((ulong)(uint)expression.RightParameter << 32
                            | (uint)expression.LeftParameter));
                return Binder.FoldScalarConstant(new BoundUnary(operation == 658
                    ? TokenKind.Minus : TokenKind.Plus,
                    new BoundConstant(value, type, operandSpan), type, expression.Span));
            }
            if (IsNegatedBooleanBinary(operation))
            {
                if (!TryGroupedUnarySpan(source, expression.Span,
                    TokenKind.Bang, out var nestedSpan))
                {
                    throw InvalidResult();
                }
                var nested = new ScalarFrontendExpression(5, operation - 2000 + encodedOffset,
                    expression.LeftParameter, expression.RightParameter, nestedSpan);
                return Binder.FoldScalarConstant(new BoundUnary(TokenKind.Bang,
                    Expression(nested), PrimitiveType.Bool, expression.Span));
            }
            if (IsComplementedIntegerBinary(operation))
            {
                if (!TryGroupedUnarySpan(source, expression.Span,
                    TokenKind.Tilde, out var nestedSpan))
                {
                    throw InvalidResult();
                }
                var nested = new ScalarFrontendExpression(expression.Type,
                    operation - 4000 + encodedOffset, expression.LeftParameter,
                    expression.RightParameter, nestedSpan);
                var type = Type(expression.Type);
                return Binder.FoldScalarConstant(new BoundUnary(TokenKind.Tilde,
                    Expression(nested), type, expression.Span));
            }
            if (IsSignedIntegerBinary(operation))
            {
                var minus = operation >= 8000;
                var offset = minus ? 8000 : 6000;
                if (!TryGroupedUnarySpan(source, expression.Span,
                    minus ? TokenKind.Minus : TokenKind.Plus, out var nestedSpan))
                {
                    throw InvalidResult();
                }
                var nested = new ScalarFrontendExpression(expression.Type,
                    operation - offset + encodedOffset, expression.LeftParameter,
                    expression.RightParameter, nestedSpan);
                var type = Type(expression.Type);
                return Binder.FoldScalarConstant(new BoundUnary(minus
                    ? TokenKind.Minus : TokenKind.Plus, Expression(nested),
                    type, expression.Span));
            }
            if (IsSignedIntegerBinary(operation - 16000))
            {
                if (!TryGroupedUnarySpan(source, expression.Span,
                    TokenKind.Tilde, out var nestedSpan))
                {
                    throw InvalidResult();
                }
                var type = Type(expression.Type);
                var nested = new ScalarFrontendExpression(expression.Type,
                    operation - 16000 + encodedOffset, expression.LeftParameter,
                    expression.RightParameter, nestedSpan);
                return Binder.FoldScalarConstant(new BoundUnary(TokenKind.Tilde,
                    Expression(nested), type, expression.Span));
            }
            if (operation is >= 38000 and < 40000)
            {
                if (!TryGroupedUnarySpan(source, expression.Span,
                    TokenKind.Tilde, out var nestedSpan))
                {
                    throw InvalidResult();
                }
                var type = Type(expression.Type);
                var nested = new ScalarFrontendExpression(expression.Type,
                    operation - 34000 + encodedOffset, expression.LeftParameter,
                    expression.RightParameter, nestedSpan);
                return Binder.FoldScalarConstant(new BoundUnary(TokenKind.Tilde,
                    Expression(nested), type, expression.Span));
            }
            if (operation is 10003 or 10057 or 10058 or 10657 or 10658)
            {
                if (!TryGroupedUnarySpan(source, expression.Span,
                    TokenKind.Tilde, out var nestedSpan))
                {
                    throw InvalidResult();
                }
                var type = Type(expression.Type);
                var nested = new ScalarFrontendExpression(expression.Type,
                    operation - 10000 + encodedOffset, expression.LeftParameter,
                    expression.RightParameter, nestedSpan);
                return Binder.FoldScalarConstant(new BoundUnary(TokenKind.Tilde,
                    Expression(nested), type, expression.Span));
            }
            if (operation is 12063 or 12163 or 14063 or 14163)
            {
                var minus = operation is 14063 or 14163;
                if (!TryGroupedUnarySpan(source, expression.Span,
                    minus ? TokenKind.Minus : TokenKind.Plus, out var nestedSpan))
                {
                    throw InvalidResult();
                }
                var type = Type(expression.Type);
                var nested = new ScalarFrontendExpression(expression.Type,
                    operation - (minus ? 14000 : 12000) + encodedOffset,
                    expression.LeftParameter, expression.RightParameter, nestedSpan);
                return Binder.FoldScalarConstant(new BoundUnary(minus
                    ? TokenKind.Minus : TokenKind.Plus,
                    Expression(nested), type, expression.Span));
            }
            if (operation is >= 30000 and < 34000)
            {
                var minus = operation >= 32000;
                if (!TryGroupedUnarySpan(source, expression.Span,
                    minus ? TokenKind.Minus : TokenKind.Plus, out var nestedSpan))
                {
                    throw InvalidResult();
                }
                var type = Type(expression.Type);
                var nested = new ScalarFrontendExpression(expression.Type,
                    operation - (minus ? 28000 : 26000) + encodedOffset,
                    expression.LeftParameter, expression.RightParameter, nestedSpan);
                return Binder.FoldScalarConstant(new BoundUnary(minus
                    ? TokenKind.Minus : TokenKind.Plus, Expression(nested),
                    type, expression.Span));
            }
            if (operation == 16062
                || operation is >= 18000 and < 20000
                    && IsNegatedBooleanBinary(operation - 16000))
            {
                if (!TryGroupedUnarySpan(source, expression.Span,
                    TokenKind.Bang, out var nestedSpan))
                {
                    throw InvalidResult();
                }
                var nested = new ScalarFrontendExpression(5,
                    operation - 16000 + encodedOffset,
                    expression.LeftParameter, expression.RightParameter, nestedSpan);
                return Binder.FoldScalarConstant(new BoundUnary(TokenKind.Bang,
                    Expression(nested), PrimitiveType.Bool, expression.Span));
            }
            if (operation / 100 is 7 or 9
                && operation % 100 is 65 or 66 or >= 71 and <= 75)
            {
                var literalFirst = operation >= 900;
                var tokens = Lexer.Lex(SourceText.From(source.Text.Substring(
                    expression.Span.Start, expression.Span.Length))).Tokens;
                if (!TryBinaryOperandTokens(tokens, (TokenKind)(operation % 100),
                    expression.Span.Length, out var firstIndex, out var secondIndex))
                {
                    throw InvalidResult();
                }
                var first = new TextSpan(expression.Span.Start + tokens[firstIndex].Span.Start,
                    tokens[firstIndex].Span.Length);
                var second = new TextSpan(expression.Span.Start + tokens[secondIndex].Span.Start,
                    tokens[secondIndex].Span.Length);
                var parameter = new BoundRead(expression.LeftParameter - 1,
                    PrimitiveType.Bool, literalFirst ? second : first);
                var literal = new BoundConstant(expression.RightParameter == 20 ? 1 : 0,
                    PrimitiveType.Bool, literalFirst ? first : second);
                return new BoundBinary((TokenKind)(operation % 100),
                    literalFirst ? literal : parameter, literalFirst ? parameter : literal,
                    PrimitiveType.Bool, expression.Span);
            }
            if (operation / 100 == 11
                && operation % 100 is 65 or 66 or >= 71 and <= 75)
            {
                var tokens = Lexer.Lex(SourceText.From(source.Text.Substring(
                    expression.Span.Start, expression.Span.Length))).Tokens;
                if (!TryBinaryOperandTokens(tokens, (TokenKind)(operation % 100),
                    expression.Span.Length, out var firstIndex, out var secondIndex))
                {
                    throw InvalidResult();
                }
                var first = new BoundConstant(expression.LeftParameter == 20 ? 1 : 0,
                    PrimitiveType.Bool, new TextSpan(expression.Span.Start + tokens[firstIndex].Span.Start,
                        tokens[firstIndex].Span.Length));
                var second = new BoundConstant(expression.RightParameter == 20 ? 1 : 0,
                    PrimitiveType.Bool, new TextSpan(expression.Span.Start + tokens[secondIndex].Span.Start,
                        tokens[secondIndex].Span.Length));
                return Binder.FoldScalarConstant(new BoundBinary(
                    (TokenKind)(operation % 100), first, second,
                    PrimitiveType.Bool, expression.Span));
            }
            if (operation / 100 == 5
                && operation % 100 is >= 57 and <= 61 or 71 or 73 or 75)
            {
                var lexical = Lexer.Lex(SourceText.From(source.Text.Substring(
                    expression.Span.Start, expression.Span.Length)));
                var tokens = lexical.Tokens;
                var type = Type(expression.Type);
                var first = new BoundConstant(expression.LeftParameter, type,
                    new TextSpan(expression.Span.Start + tokens[0].Span.Start, tokens[0].Span.Length));
                var second = new BoundConstant(expression.RightParameter, type,
                    new TextSpan(expression.Span.Start + tokens[2].Span.Start, tokens[2].Span.Length));
                return Binder.FoldScalarConstant(new BoundBinary(
                    (TokenKind)(operation % 100), first, second, type,
                    expression.Span));
            }
            var positiveLiteral = operation >= 1600;
            var negativeLiteral = !positiveLiteral && operation >= 1200;
            var binaryOperation = operation
                - (positiveLiteral ? 1600 : negativeLiteral ? 1200 : 0);
            if (binaryOperation / 100 is >= 1 and <= 4
                && binaryOperation % 100 is >= 57 and <= 61 or >= 65 and <= 70
                    or 71 or 73 or 75 or >= 88 and <= 90)
            {
                var literalFirst = binaryOperation >= 300;
                var parameter = encodedOffset != 0 ? 3
                    : binaryOperation / 100 - (literalFirst ? 2 : 0);
                var lexical = Lexer.Lex(SourceText.From(source.Text.Substring(
                    expression.Span.Start, expression.Span.Length)));
                var tokens = lexical.Tokens;
                if (!TryBinaryOperandTokens(tokens, (TokenKind)(binaryOperation % 100),
                    expression.Span.Length, out var first, out var second, literalFirst,
                    positiveLiteral ? TokenKind.Plus
                        : negativeLiteral ? TokenKind.Minus : TokenKind.EndOfFile))
                {
                    throw InvalidResult();
                }
                var numberIndex = literalFirst ? first : second;
                var nameIndex = literalFirst ? second : first;
                var parameterType = types[parameter - 1].Type;
                var shift = binaryOperation % 100 is >= 88 and <= 90;
                var literalType = shift
                    ? literalFirst ? Type(expression.Type) : PrimitiveType.Int32
                    : parameterType;
                var value = literalType is PrimitiveType.Float32 or PrimitiveType.Float64
                    ? FloatingProjectionValue(literalType == PrimitiveType.Float32 ? 7 : 8,
                        expression.LeftParameter, expression.RightParameter)
                    : literalType is PrimitiveType.Int32 or PrimitiveType.UInt32
                        ? expression.LeftParameter
                        : unchecked((long)((ulong)(uint)expression.RightParameter << 32
                            | (uint)expression.LeftParameter));
                var leftOperand = new BoundRead(parameter - 1, parameterType,
                    new TextSpan(expression.Span.Start + tokens[nameIndex].Span.Start, tokens[nameIndex].Span.Length));
                var numberSpan = new TextSpan(expression.Span.Start + tokens[numberIndex].Span.Start,
                    tokens[numberIndex].Span.Length);
                BoundExpression literal = new BoundConstant(value,
                    literalType, numberSpan);
                if (negativeLiteral || positiveLiteral)
                {
                    var signedSpan = new TextSpan(expression.Span.Start + tokens[numberIndex - 1].Span.Start,
                        tokens[numberIndex].Span.End - tokens[numberIndex - 1].Span.Start);
                    var magnitude = unchecked((ulong)(uint)expression.RightParameter << 32
                        | (uint)expression.LeftParameter);
                    var minimum = literalType == PrimitiveType.Int32
                        ? (ulong)int.MaxValue + 1 : (ulong)long.MaxValue + 1;
                    literal = negativeLiteral
                        && ScannerHost.IntegerRadix(tokens[numberIndex].Text.AsSpan()) == 10
                        && magnitude == minimum
                        ? new BoundConstant(literalType == PrimitiveType.Int32
                            ? int.MinValue : long.MinValue, literalType, signedSpan)
                        : Binder.FoldScalarConstant(new BoundUnary(
                            negativeLiteral ? TokenKind.Minus : TokenKind.Plus, literal,
                            literalType, signedSpan));
                }
                return new BoundBinary((TokenKind)(binaryOperation % 100),
                    literalFirst ? literal : leftOperand, literalFirst ? leftOperand : literal,
                    Type(expression.Type), expression.Span);
            }
            if (operation == 3)
            {
                var negative = source.Text[expression.Span.Start] == '-';
                var numberSpan = expression.Type is 7 or 8
                    ? NumericSpan(source, expression.Span) : expression.Span;
                var value = expression.Type is 7 or 8
                    ? FloatingProjectionValue(expression.Type,
                        expression.LeftParameter, expression.RightParameter)
                    : expression.Type is 1 or 2
                        ? expression.LeftParameter
                        : unchecked((long)((ulong)(uint)expression.RightParameter << 32
                            | (uint)expression.LeftParameter));
                if (!negative)
                {
                    return new BoundConstant(value, Type(expression.Type), expression.Span);
                }
                var minimum = expression.Type == 1 ? (ulong)int.MaxValue + 1
                    : (ulong)long.MaxValue + 1;
                var magnitude = unchecked((ulong)(uint)expression.RightParameter << 32
                    | (uint)expression.LeftParameter);
                var operandSpan = NumericSpan(source, expression.Span);
                var digits = source.Text.AsSpan(operandSpan.Start, operandSpan.Length);
                if (ScannerHost.IntegerRadix(digits) == 10 && magnitude == minimum)
                {
                    return new BoundConstant(expression.Type == 1 ? int.MinValue : long.MinValue,
                        Type(expression.Type), expression.Span);
                }
                return Binder.FoldScalarConstant(new BoundUnary(TokenKind.Minus,
                    new BoundConstant(value, Type(expression.Type), operandSpan),
                    Type(expression.Type), expression.Span));
            }
            if (operation is 20 or 21)
            {
                return new BoundConstant(operation == 20 ? 1 : 0,
                    PrimitiveType.Bool, expression.Span);
            }
            if (operation == 0)
            {
                return new BoundRead(expression.LeftParameter - 1,
                    types[expression.LeftParameter - 1].Type, expression.Span);
            }
            var operands = Lexer.Lex(SourceText.From(source.Text.Substring(
                expression.Span.Start, expression.Span.Length))).Tokens;
            var firstOperand = operands.First(static token => token.Kind == TokenKind.Identifier).Span;
            var secondOperand = operands.Last(static token => token.Kind == TokenKind.Identifier).Span;
            var left = new BoundRead(expression.LeftParameter - 1,
                types[expression.LeftParameter - 1].Type,
                new TextSpan(expression.Span.Start + firstOperand.Start, firstOperand.Length));
            var right = new BoundRead(expression.RightParameter - 1,
                types[expression.RightParameter - 1].Type,
                new TextSpan(expression.Span.Start + secondOperand.Start, secondOperand.Length));
            return new BoundBinary((TokenKind)operation, left, right,
                Type(expression.Type), expression.Span);
        }
    }

    private static PrimitiveType Type(int code) => code switch {
        0 => PrimitiveType.Void,
        1 => PrimitiveType.Int32, 2 => PrimitiveType.UInt32,
        3 => PrimitiveType.Int64, 4 => PrimitiveType.UInt64,
        5 => PrimitiveType.Bool, 6 => PrimitiveType.Char16,
        7 => PrimitiveType.Float32, 8 => PrimitiveType.Float64,
        _ => throw InvalidResult(),
    };

    private static bool TakeDeclarationEffects(ImmutableArray<SyntaxToken> tokens, ref int at, int expected)
    {
        var effects = 0;
        while (at < tokens.Length && tokens[at].Kind == TokenKind.OpenBracket)
        {
            if (at + 2 >= tokens.Length || tokens[at + 1].Kind != TokenKind.Identifier
                || tokens[at + 2].Kind != TokenKind.CloseBracket)
            {
                return false;
            }
            var flag = tokens[at + 1].Text switch {
                "NoThrow" => 2,
                "DoesNotReturn" => 4,
                _ => 0,
            };
            if (flag == 0 || (effects & flag) != 0)
            {
                return false;
            }
            effects |= flag;
            at += 3;
        }
        return effects == expected;
    }

    private static bool TakeCallableModifiers(ImmutableArray<SyntaxToken> tokens, ref int at,
        bool expectedPublic, bool allowInternal, bool expectedCallerUnsafe)
    {
        var hasVisibility = false;
        var isPublic = false;
        var callerUnsafe = false;
        while (at < tokens.Length)
        {
            var kind = tokens[at].Kind;
            if (kind is TokenKind.PublicKeyword or TokenKind.InternalKeyword)
            {
                if (hasVisibility || kind == TokenKind.InternalKeyword && !allowInternal)
                {
                    return false;
                }
                hasVisibility = true;
                isPublic = kind == TokenKind.PublicKeyword;
                at++;
            }
            else if (kind == TokenKind.UnsafeKeyword)
            {
                if (callerUnsafe)
                {
                    return false;
                }
                callerUnsafe = true;
                at++;
            }
            else
            {
                break;
            }
        }
        return isPublic == expectedPublic && callerUnsafe == expectedCallerUnsafe;
    }

    private static ArgumentException InvalidResult()
        => new("Invalid scalar frontend projection.");
}

public readonly record struct ScalarFrontendExpression(
    int Type, int Operation, int LeftParameter, int RightParameter, TextSpan Span);

public readonly record struct ScalarFrontendNode(
    int Operation, int Type, TextSpan Span, int Left, int Right, int Low, int High)
{
    public ImmutableArray<int> ArgumentParameterIndices { get; init; }
}

public readonly record struct ScalarFrontendLocal(int Type, bool IsMutable, TextSpan Name)
{
    public bool IsInitialization { get; init; }
    public bool IsConstant { get; init; }
    public bool IsSwitchSelector { get; init; }
}

public readonly record struct ScalarFrontendStatement(int Kind, int Slot, int Root, TextSpan Span)
{
    public TextSpan BodySpan { get; init; }
    public TextSpan LoopSpan { get; init; }
    public int SwitchLocal { get; init; } = -1;
}

public readonly record struct ScalarFrontendResult(
    bool IsValid, bool IsPublic, int ErrorCode, TextSpan ErrorSpan, int ReturnType, int ParameterCount,
    int FirstType, int SecondType, int ThirdType, TextSpan Name, TextSpan FirstName, TextSpan SecondName,
    TextSpan ThirdName,
    bool HasCondition, bool HasFallthrough, ScalarFrontendExpression Condition, ScalarFrontendExpression WhenTrue,
    ScalarFrontendExpression WhenFalse)
{
    public bool IsNoThrow { get; init; }
    public bool DoesNotReturn { get; init; }
    /// <summary>Whether calling this declaration requires an explicit unsafe caller scope.</summary>
    public bool RequiresUnsafeCaller { get; init; }
    public ImmutableArray<ScalarFrontendNode> Nodes { get; init; }
    public int ConditionRoot { get; init; }
    public int TrueRoot { get; init; }
    public int FalseRoot { get; init; }
    public bool IsExpressionBody { get; init; }
    public ImmutableArray<ScalarFrontendLocal> Locals { get; init; }
    public ImmutableArray<ScalarFrontendStatement> Statements { get; init; }
    /// <summary>Bitmask identifying scalar <c>out</c> parameters in the bounded projection.</summary>
    public int OutParameterMask { get; init; }
    /// <summary>Bitmask identifying scalar <c>init ref</c> parameters in single or complete snapshots.</summary>
    public int InitRefParameterMask { get; init; }
    public ImmutableArray<int> CallTargets { get; init; }
    public ImmutableArray<ScalarFrontendResult> Functions { get; init; }
}
