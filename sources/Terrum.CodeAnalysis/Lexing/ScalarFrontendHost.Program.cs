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

public readonly record struct ScalarFrontendReadResult(ScalarFrontendResult Projection, BoundProgram? Program);

public static partial class ScalarFrontendHost
{
    private const int BooleanTypeCode = 5;

    public const int ProgramBufferIndex = BufferIndex + 1;
    public const int ProgramWordCount = 4284;
    public const int ProgramFrameWordCount = 428;
    public const int MaximumFunctions = 10;

    private sealed record ProgramDeclaration(string Name, int ReturnType, int Effects, int OutParameterMask,
        int InitRefParameterMask, ImmutableArray<int> ParameterTypes, ImmutableArray<string> ParameterNames,
        int TokenStart, int TokenEnd, TextSpan Span, TextSpan BodySpan);

    private sealed record ProgramReadContext(ImmutableArray<ProgramDeclaration> Declarations,
        ImmutableArray<int> Targets, ScalarAliasEnvironment Aliases)
    {
        public ProgramDeclaration Callee(int node)
        {
            var target = Targets[node];
            if ((uint)target >= (uint)Declarations.Length)
            {
                throw InvalidResult();
            }
            return Declarations[target];
        }
    }

    private sealed record ProgramInventory(ImmutableArray<ProgramDeclaration> Declarations,
        ScalarAliasEnvironment Aliases);

    private sealed record AliasDeclaration(string Name, string Target);

    private sealed record ScalarAliasEnvironment(ImmutableDictionary<string, int> Types)
    {
        public bool TryGetType(string name, out int type) => Types.TryGetValue(name, out type);
    }

    /// <summary>Reads only current v85 complete-program and single-function projections.</summary>
    public static ScalarFrontendResult ReadResult(SourceText source, ReadOnlySpan<byte> bytes,
        ReadOnlySpan<byte> programBytes, BoundRuntimePolicy policy)
        => ReadProgram(source, bytes, programBytes, policy).Projection;

    /// <summary>Reads and verifies a projection, retaining the bound multi-function program for its consumer.</summary>
    public static ScalarFrontendReadResult ReadProgram(SourceText source, ReadOnlySpan<byte> bytes,
        ReadOnlySpan<byte> programBytes, BoundRuntimePolicy policy)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (programBytes.Length != ProgramWordCount * sizeof(int))
        {
            throw InvalidResult();
        }
        if (programBytes.IndexOfAnyExcept((byte)0) < 0)
        {
            return new ScalarFrontendReadResult(ReadResult(source, bytes, policy), null);
        }
        if (bytes.Length != WordCount * sizeof(int) || bytes.IndexOfAnyExcept((byte)0) >= 0)
        {
            throw InvalidResult();
        }
        var words = new int[ProgramWordCount];
        for (var i = 0; i < words.Length; i++)
        {
            words[i] = BinaryPrimitives.ReadInt32LittleEndian(programBytes.Slice(i * sizeof(int), sizeof(int)));
        }
        if (words[0] != 85
            || words[1] != 1 || words[2] is < 1 or > MaximumFunctions
            || words[3] != 0 || source.Length > ScannerHost.MaximumSourceLength
            || words.AsSpan(4 + words[2] * ProgramFrameWordCount).IndexOfAnyExcept(0) >= 0)
        {
            throw InvalidResult();
        }
        var inventory = InventoryProgram(source);
        var declarations = inventory.Declarations;
        if (declarations.Length != words[2])
        {
            throw InvalidResult();
        }
        var functions = ImmutableArray.CreateBuilder<ScalarFrontendResult>(declarations.Length);
        var functionSources = ImmutableArray.CreateBuilder<SourceText>(declarations.Length);
        for (var i = 0; i < declarations.Length; i++)
        {
            var declaration = declarations[i];
            var frame = 4 + i * ProgramFrameWordCount;
            if (words[frame] != declaration.TokenStart || words[frame + 1] != declaration.TokenEnd
                || words[frame + 2] != declaration.Span.Start || words[frame + 3] != declaration.Span.Length)
            {
                throw InvalidResult();
            }
            var payload = programBytes.Slice((frame + 4) * sizeof(int), WordCount * sizeof(int));
            var targets = ImmutableArray.CreateBuilder<int>(32);
            var nodeCount = words[frame + 4 + 37];
            if (nodeCount is < 0 or > 32)
            {
                throw InvalidResult();
            }
            for (var node = 0; node < 32; node++)
            {
                var operation = node < nodeCount ? words[frame + 4 + 42 + node * 8] : 0;
                var target = words[frame + 396 + node];
                if (operation == 105
                    ? target < 0 || target >= declarations.Length
                    : target != -1)
                {
                    throw InvalidResult();
                }
                targets.Add(target);
            }
            var context = new ProgramReadContext(declarations, targets.MoveToImmutable(),
                inventory.Aliases);
            var functionSource = FunctionSource(source, declaration.Span);
            functionSources.Add(functionSource);
            var function = ReadResultCore(functionSource, payload, policy, context);
            if (!function.IsValid || function.ReturnType != declaration.ReturnType
                || function.ParameterCount != declaration.ParameterTypes.Length
                || function.OutParameterMask != declaration.OutParameterMask
                || function.InitRefParameterMask != declaration.InitRefParameterMask
                || function.IsNoThrow != ((declaration.Effects & 2) != 0)
                || function.DoesNotReturn != ((declaration.Effects & 4) != 0)
                || function.RequiresUnsafeCaller != ((declaration.Effects & 8) != 0)
                || SourceName(source, function.Name) != declaration.Name)
            {
                throw InvalidResult();
            }
            functions.Add(function);
        }
        var results = functions.MoveToImmutable();
        if (results.Any(static function => function.Statements.Any(
            static statement => statement.Kind == 38))
            && SyntaxTree.Parse(MaskSwitchStatements(source, results)).HasErrors)
        {
            throw InvalidResult();
        }
        var program = BindProgramCore(results, declarations, functionSources.MoveToImmutable(), policy);
        return new ScalarFrontendReadResult(results[0] with { Functions = results }, program);
    }

    /// <summary>Lowers audited function identities in declaration order, then verifies the whole library.</summary>
    public static BoundProgram BindLibrary(SourceText source, ReadOnlySpan<byte> bytes,
        ReadOnlySpan<byte> programBytes, BoundRuntimePolicy policy)
    {
        var read = ReadProgram(source, bytes, programBytes, policy);
        if (!read.Projection.IsValid)
        {
            throw InvalidResult();
        }
        return read.Program ?? BindLibraryCore(source, read.Projection, policy);
    }

    private static BoundProgram BindProgramCore(ImmutableArray<ScalarFrontendResult> results,
        ImmutableArray<ProgramDeclaration> declarations, ImmutableArray<SourceText> functionSources,
        BoundRuntimePolicy policy)
    {
        var functions = ImmutableArray.CreateBuilder<BoundFunction>(results.Length);
        for (var i = 0; i < results.Length; i++)
        {
            var hasDoesNotReturnCallStatement = false;
            foreach (var statement in results[i].Statements)
            {
                if (statement.Kind == 37
                    && results[i].Nodes[statement.Root].Operation == 105
                    && (declarations[results[i].CallTargets[statement.Root]].Effects & 4) != 0)
                {
                    hasDoesNotReturnCallStatement = true;
                    break;
                }
            }
            var omitImplicitReturn = hasDoesNotReturnCallStatement && results[i].TrueRoot < 0;
            var function = BindLibraryCore(functionSources[i],
                results[i], policy, verify: false,
                omitImplicitReturn: omitImplicitReturn).Functions[0];
            functions.Add(function with {
                Span = declarations[i].Span,
                Body = function.Body with { Span = declarations[i].BodySpan },
            });
        }
        var program = new BoundProgram(functions.MoveToImmutable(), -1) { RuntimePolicy = policy };
        SemanticVerifier.VerifyLibrary(program);
        return program;
    }

    // Preserve absolute source ranges while isolating the existing single-body
    // audit. InventoryProgram has already proved that these spans partition all
    // non-trivia source tokens; no other declaration can disappear into padding.
    private static SourceText FunctionSource(SourceText source, TextSpan span)
    {
        var text = string.Create(source.Length, (Source: source, Span: span), static (destination, state) =>
        {
            destination.Fill(' ');
            state.Source.Text.AsSpan(state.Span.Start, state.Span.Length)
                .CopyTo(destination[state.Span.Start..]);
        });
        return SourceText.From(text);
    }

    private static SourceText MaskSwitchStatements(SourceText source,
        ImmutableArray<ScalarFrontendResult> functions)
    {
        var spans = new List<TextSpan>();
        foreach (var function in functions)
        {
            foreach (var statement in function.Statements)
            {
                if (statement.Kind == 38)
                {
                    spans.Add(statement.Span);
                }
            }
        }
        spans.Sort(static (left, right) => left.Start != right.Start
            ? left.Start.CompareTo(right.Start) : right.Length.CompareTo(left.Length));

        var text = string.Create(source.Length, (Source: source, Spans: spans), static (destination, state) =>
        {
            state.Source.Text.AsSpan().CopyTo(destination);
            var maskedUntil = -1;
            foreach (var span in state.Spans)
            {
                if (span.Start < maskedUntil)
                {
                    continue;
                }
                if (span.Length < 2 || span.End > destination.Length)
                {
                    throw InvalidResult();
                }
                destination.Slice(span.Start, span.Length).Fill(' ');
                destination[span.Start] = '{';
                destination[span.Start + 1] = '}';
                maskedUntil = span.End;
            }
        });
        return SourceText.From(text);
    }

    private static ProgramInventory InventoryProgram(SourceText source)
    {
        var lexical = Lexer.Lex(source);
        var tokens = lexical.Tokens;
        if (!lexical.Diagnostics.IsEmpty || tokens.Length > 64)
        {
            throw InvalidResult();
        }
        var hasSwitch = false;
        foreach (var token in tokens)
        {
            if (token.Kind == TokenKind.SwitchKeyword)
            {
                hasSwitch = true;
                break;
            }
        }
        // The project parser does not recognize this switch form. ReadProgram audits each
        // frame first, then parses the full source with only audited switch spans masked.
        if (!hasSwitch && SyntaxTree.Parse(lexical).HasErrors)
        {
            throw InvalidResult();
        }
        var aliases = InventoryScalarAliases(tokens);
        var declarations = ImmutableArray.CreateBuilder<ProgramDeclaration>();
        var names = new HashSet<string>(StringComparer.Ordinal);
        var at = 0;
        while (at < tokens.Length - 1)
        {
            if (TrySkipAlias(tokens, ref at))
            {
                continue;
            }
            var start = at;
            var effects = 0;
            while (tokens[at].Kind == TokenKind.OpenBracket)
            {
                if (!Take(TokenKind.OpenBracket) || at >= tokens.Length)
                {
                    throw InvalidResult();
                }
                var effect = tokens[at].Text switch {
                    "NoThrow" => 2,
                    "DoesNotReturn" => 4,
                    _ => 0,
                };
                if (effect == 0 || (effects & effect) != 0
                    || !Take(TokenKind.Identifier) || !Take(TokenKind.CloseBracket))
                {
                    throw InvalidResult();
                }
                effects |= effect;
            }
            var hasVisibility = false;
            while (tokens[at].Kind is TokenKind.PublicKeyword or TokenKind.InternalKeyword
                or TokenKind.UnsafeKeyword)
            {
                if (tokens[at].Kind == TokenKind.UnsafeKeyword)
                {
                    if ((effects & 8) != 0)
                    {
                        throw InvalidResult();
                    }
                    effects |= 8;
                }
                else
                {
                    if (hasVisibility)
                    {
                        throw InvalidResult();
                    }
                    hasVisibility = true;
                }
                at++;
            }
            var returnType = ScalarCode(tokens[at].Name, allowVoid: true, aliases);
            if (!Take(returnType == 0 ? TokenKind.VoidKeyword : TokenKind.Identifier)
                || at >= tokens.Length || tokens[at].Kind != TokenKind.Identifier)
            {
                throw InvalidResult();
            }
            var name = tokens[at++];
            if (!names.Add(name.Name) || !Take(TokenKind.OpenParen)
                || name.Span.End != tokens[at - 1].Span.Start)
            {
                throw InvalidResult();
            }
            var parameterTypes = ImmutableArray.CreateBuilder<int>();
            var parameterNames = new HashSet<string>(StringComparer.Ordinal);
            var parameterNameList = ImmutableArray.CreateBuilder<string>();
            var outParameterMask = 0;
            var initRefParameterMask = 0;
            while (at < tokens.Length && tokens[at].Kind != TokenKind.CloseParen)
            {
                if (parameterTypes.Count == 3 || parameterTypes.Count != 0 && !Take(TokenKind.Comma))
                {
                    throw InvalidResult();
                }
                var isOut = tokens[at].Kind == TokenKind.OutKeyword;
                var isInitRef = tokens[at].Kind == TokenKind.Identifier
                    && tokens[at].Name == "init" && tokens[at].Span.Length == 4;
                if (isOut) { at++; }
                if (isInitRef
                    && (!Take(TokenKind.Identifier) || tokens[at - 1].Name != "init"
                        || !Take(TokenKind.RefKeyword) || !Take(TokenKind.Less)))
                {
                    throw InvalidResult();
                }
                var type = ScalarCode(tokens[at].Name, allowVoid: false, aliases);
                if (!Take(TokenKind.Identifier)
                    || (isInitRef && !Take(TokenKind.Greater))
                    || at >= tokens.Length
                    || tokens[at].Kind != TokenKind.Identifier || !parameterNames.Add(tokens[at].Name))
                {
                    throw InvalidResult();
                }
                parameterNameList.Add(tokens[at].Name);
                at++;
                if (isOut) { outParameterMask |= 1 << parameterTypes.Count; }
                if (isInitRef)
                {
                    initRefParameterMask |= 1 << parameterTypes.Count;
                }
                parameterTypes.Add(type);
            }
            if (!Take(TokenKind.CloseParen))
            {
                throw InvalidResult();
            }
            if ((effects & 4) != 0 && returnType != 0)
            {
                throw InvalidResult();
            }
            TextSpan bodySpan;
            if (Take(TokenKind.Arrow))
            {
                var bodyStart = tokens[at - 1].Span.Start;
                while (at < tokens.Length && tokens[at].Kind != TokenKind.Semicolon
                    && tokens[at].Kind != TokenKind.EndOfFile)
                {
                    at++;
                }
                if (!Take(TokenKind.Semicolon)) { throw InvalidResult(); }
                bodySpan = new TextSpan(bodyStart, tokens[at - 1].Span.End - bodyStart);
            }
            else if (Take(TokenKind.OpenBrace))
            {
                var bodyStart = tokens[at - 1].Span.Start;
                var depth = 1;
                while (at < tokens.Length - 1 && depth != 0)
                {
                    if (tokens[at].Kind == TokenKind.OpenBrace) { depth++; }
                    if (tokens[at].Kind == TokenKind.CloseBrace) { depth--; }
                    at++;
                }
                if (depth != 0) { throw InvalidResult(); }
                bodySpan = new TextSpan(bodyStart, tokens[at - 1].Span.End - bodyStart);
            }
            else
            {
                throw InvalidResult();
            }
            if (declarations.Count == MaximumFunctions) { throw InvalidResult(); }
            declarations.Add(new ProgramDeclaration(name.Name, returnType, effects, outParameterMask,
                initRefParameterMask, parameterTypes.ToImmutable(), parameterNameList.ToImmutable(),
                start, at, new TextSpan(tokens[start].Span.Start,
                    tokens[at - 1].Span.End - tokens[start].Span.Start), bodySpan));
        }
        if (!Take(TokenKind.EndOfFile) || at != tokens.Length)
        {
            throw InvalidResult();
        }
        return new ProgramInventory(declarations.ToImmutable(), aliases);

        bool Take(TokenKind kind)
        {
            if (at >= tokens.Length || tokens[at].Kind != kind) { return false; }
            at++;
            return true;
        }

    }

    private static bool TrySkipAlias(ImmutableArray<SyntaxToken> tokens, ref int at)
    {
        var start = at;
        while (at < tokens.Length && tokens[at].Kind is TokenKind.PublicKeyword or TokenKind.InternalKeyword)
        {
            at++;
        }
        if (at >= tokens.Length || tokens[at].Kind != TokenKind.AliasKeyword)
        {
            at = start;
            return false;
        }
        while (at < tokens.Length && tokens[at].Kind != TokenKind.Semicolon)
        {
            at++;
        }
        if (at >= tokens.Length)
        {
            throw InvalidResult();
        }
        at++;
        return true;
    }

    private static ScalarAliasEnvironment InventoryScalarAliases(ImmutableArray<SyntaxToken> tokens)
    {
        var declarations = new Dictionary<string, AliasDeclaration>(StringComparer.Ordinal);
        var at = 0;
        while (at < tokens.Length - 1)
        {
            var start = at;
            var hasAttributes = false;
            while (at < tokens.Length && tokens[at].Kind == TokenKind.OpenBracket)
            {
                hasAttributes = true;
                while (at < tokens.Length && tokens[at++].Kind != TokenKind.CloseBracket)
                {
                }
            }
            var visibility = 0;
            while (at < tokens.Length && tokens[at].Kind is TokenKind.PublicKeyword or TokenKind.InternalKeyword)
            {
                var currentVisibility = tokens[at++].Kind == TokenKind.PublicKeyword ? 1 : 2;
                if (visibility != 0)
                {
                    throw InvalidResult();
                }
                visibility = currentVisibility;
            }
            if (at < tokens.Length && tokens[at].Kind == TokenKind.AliasKeyword)
            {
                if (hasAttributes || visibility == 1)
                {
                    throw InvalidResult();
                }
                at++;
                if (at + 3 >= tokens.Length || tokens[at].Kind != TokenKind.Identifier
                    || tokens[at + 1].Kind != TokenKind.Equals
                    || tokens[at + 2].Kind != TokenKind.Identifier
                    || tokens[at + 3].Kind != TokenKind.Semicolon
                    || !declarations.TryAdd(tokens[at].Name,
                        new AliasDeclaration(tokens[at].Name, tokens[at + 2].Name)))
                {
                    throw InvalidResult();
                }
                at += 4;
                continue;
            }

            at = start;
            var bodyStart = -1;
            for (var index = at; index < tokens.Length - 1; index++)
            {
                if (tokens[index].Kind is TokenKind.Arrow or TokenKind.OpenBrace)
                {
                    bodyStart = index;
                    break;
                }
            }
            if (bodyStart < 0)
            {
                throw InvalidResult();
            }
            if (tokens[bodyStart].Kind == TokenKind.Arrow)
            {
                at = bodyStart + 1;
                while (at < tokens.Length - 1 && tokens[at].Kind != TokenKind.Semicolon)
                {
                    at++;
                }
                if (at >= tokens.Length - 1)
                {
                    throw InvalidResult();
                }
                at++;
            }
            else
            {
                var depth = 1;
                at = bodyStart + 1;
                while (at < tokens.Length - 1 && depth != 0)
                {
                    if (tokens[at].Kind == TokenKind.OpenBrace) { depth++; }
                    if (tokens[at].Kind == TokenKind.CloseBrace) { depth--; }
                    at++;
                }
                if (depth != 0)
                {
                    throw InvalidResult();
                }
            }
        }

        var resolved = new Dictionary<string, int>(StringComparer.Ordinal);
        while (resolved.Count != declarations.Count)
        {
            var madeProgress = false;
            foreach (var declaration in declarations.Values)
            {
                if (resolved.ContainsKey(declaration.Name))
                {
                    continue;
                }
                if (!TryGetPrimitiveScalarCode(declaration.Target, allowVoid: false, out var type))
                {
                    if (resolved.TryGetValue(declaration.Target, out type))
                    {
                        resolved.Add(declaration.Name, type);
                        madeProgress = true;
                    }
                    else if (!declarations.ContainsKey(declaration.Target))
                    {
                        throw InvalidResult();
                    }
                    continue;
                }
                resolved.Add(declaration.Name, type);
                madeProgress = true;
            }
            if (!madeProgress)
            {
                throw InvalidResult();
            }
        }
        return new ScalarAliasEnvironment(resolved.ToImmutableDictionary(StringComparer.Ordinal));
    }

    private static int ScalarCode(string text, bool allowVoid, ScalarAliasEnvironment? aliases)
    {
        if (TryGetPrimitiveScalarCode(text, allowVoid, out var type))
        {
            return type;
        }
        return aliases is not null && aliases.TryGetType(text, out type)
            ? type
            : throw InvalidResult();
    }

    private static bool TryGetPrimitiveScalarCode(string text, bool allowVoid, out int type)
    {
        type = text switch {
            "void" when allowVoid => 0,
            "int32" => 1, "uint32" => 2, "int64" => 3, "uint64" => 4,
            "bool" => 5, "char16" => 6, "float32" => 7, "float64" => 8,
            _ => -1,
        };
        return type >= 0;
    }
}
