// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Collections.Immutable;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Terrum.CodeAnalysis.Text;

namespace Terrum.CodeAnalysis.Lexing;

public readonly record struct ScalarFrontendReadResult(ScalarFrontendResult Projection, BoundProgram? Program);

public static partial class ScalarFrontendHost
{
    public const int ProgramBufferIndex = BufferIndex + 1;
    public const int ProgramWordCount = 4284;
    public const int ProgramFrameWordCount = 428;
    public const int MaximumFunctions = 10;

    private sealed record ProgramDeclaration(string Name, int ReturnType,
        ImmutableArray<int> ParameterTypes, int TokenStart, int TokenEnd, TextSpan Span, TextSpan BodySpan);

    private sealed record ProgramReadContext(ImmutableArray<ProgramDeclaration> Declarations, int[] Targets,
        bool AllowVoidCalls)
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

    /// <summary>Reads a complete v74/v75 declaration snapshot, or an unchanged v1–v73 result.</summary>
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
        if (words[0] is not (74 or 75) || words[1] != 1 || words[2] is < 2 or > MaximumFunctions
            || words[3] != 0 || source.Length > 16_384
            || words.AsSpan(4 + words[2] * ProgramFrameWordCount).IndexOfAnyExcept(0) >= 0)
        {
            throw InvalidResult();
        }
        var declarations = InventoryProgram(source);
        if (declarations.Length != words[2])
        {
            throw InvalidResult();
        }
        var functions = ImmutableArray.CreateBuilder<ScalarFrontendResult>(declarations.Length);
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
            var targets = words.AsSpan(frame + 396, 32).ToArray();
            var nodeCount = words[frame + 4 + 37];
            if (nodeCount is < 0 or > 32)
            {
                throw InvalidResult();
            }
            for (var node = 0; node < 32; node++)
            {
                var operation = node < nodeCount ? words[frame + 4 + 42 + node * 8] : 0;
                if (operation == 105
                    ? targets[node] < 0 || targets[node] >= declarations.Length
                    : targets[node] != -1)
                {
                    throw InvalidResult();
                }
            }
            var context = new ProgramReadContext(declarations, targets, words[0] == 75);
            var function = ReadResultCore(FunctionSource(source, declaration.Span), payload, policy, context);
            if (!function.IsValid || function.ReturnType != declaration.ReturnType
                || function.ParameterCount != declaration.ParameterTypes.Length
                || SourceName(source, function.Name) != declaration.Name)
            {
                throw InvalidResult();
            }
            functions.Add(function);
        }
        var results = functions.MoveToImmutable();
        var hasVoidCall = false;
        foreach (var function in results)
        {
            foreach (var node in function.Nodes)
            {
                hasVoidCall |= node.Operation == 105 && node.Type == 0;
            }
        }
        if ((words[0] == 75) != hasVoidCall)
        {
            throw InvalidResult();
        }
        var program = BindProgramCore(source, results, declarations, policy);
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

    private static BoundProgram BindProgramCore(SourceText source, ImmutableArray<ScalarFrontendResult> results,
        ImmutableArray<ProgramDeclaration> declarations, BoundRuntimePolicy policy)
    {
        var functions = ImmutableArray.CreateBuilder<BoundFunction>(results.Length);
        for (var i = 0; i < results.Length; i++)
        {
            var function = BindLibraryCore(FunctionSource(source, declarations[i].Span),
                results[i], policy, verify: false).Functions[0];
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
        var text = new string(' ', source.Length).ToCharArray();
        source.Text.AsSpan(span.Start, span.Length).CopyTo(text.AsSpan(span.Start));
        return SourceText.From(new string(text));
    }

    private static ImmutableArray<ProgramDeclaration> InventoryProgram(SourceText source)
    {
        var lexical = Lexer.Lex(source);
        var tokens = lexical.Tokens;
        if (!lexical.Diagnostics.IsEmpty || tokens.Length > 64 || SyntaxTree.Parse(source).HasErrors)
        {
            throw InvalidResult();
        }
        var declarations = ImmutableArray.CreateBuilder<ProgramDeclaration>();
        var names = new HashSet<string>(StringComparer.Ordinal);
        var at = 0;
        while (at < tokens.Length - 1)
        {
            var start = at;
            var effects = 0;
            while (tokens[at].Kind == TokenKind.OpenBracket)
            {
                if (!Take(TokenKind.OpenBracket) || at >= tokens.Length
                    || tokens[at].Text != "NoThrow" || effects != 0
                    || !Take(TokenKind.Identifier) || !Take(TokenKind.CloseBracket))
                {
                    throw InvalidResult();
                }
                effects = 2;
            }
            if (tokens[at].Kind is TokenKind.PublicKeyword or TokenKind.InternalKeyword) { at++; }
            var returnType = ScalarCode(tokens[at].Text, allowVoid: true);
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
            while (at < tokens.Length && tokens[at].Kind != TokenKind.CloseParen)
            {
                if (parameterTypes.Count == 3 || parameterTypes.Count != 0 && !Take(TokenKind.Comma))
                {
                    throw InvalidResult();
                }
                var type = ScalarCode(tokens[at].Text, allowVoid: false);
                if (!Take(TokenKind.Identifier) || at >= tokens.Length
                    || tokens[at].Kind != TokenKind.Identifier || !parameterNames.Add(tokens[at].Name))
                {
                    throw InvalidResult();
                }
                at++;
                parameterTypes.Add(type);
            }
            if (!Take(TokenKind.CloseParen))
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
            declarations.Add(new ProgramDeclaration(name.Name, returnType, parameterTypes.ToImmutable(),
                start, at, new TextSpan(tokens[start].Span.Start,
                    tokens[at - 1].Span.End - tokens[start].Span.Start), bodySpan));
        }
        if (!Take(TokenKind.EndOfFile) || at != tokens.Length)
        {
            throw InvalidResult();
        }
        return declarations.ToImmutable();

        bool Take(TokenKind kind)
        {
            if (at >= tokens.Length || tokens[at].Kind != kind) { return false; }
            at++;
            return true;
        }

        static int ScalarCode(string text, bool allowVoid) => text switch {
            "void" when allowVoid => 0,
            "int32" => 1, "uint32" => 2, "int64" => 3, "uint64" => 4,
            "bool" => 5, "char16" => 6, _ => throw InvalidResult(),
        };
    }
}
