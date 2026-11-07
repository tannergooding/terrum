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

/// <summary>Combines the scalar projection with its validated complete program, when available.</summary>
/// <param name="Projection">The bounded scalar projection of the source.</param>
/// <param name="Program">The reconstructed and verified program, or null when unavailable.</param>
public readonly record struct ScalarFrontendReadResult(
    ScalarFrontendResult Projection, BoundProgram? Program);

public static partial class ScalarFrontendHost
{
    private const int BooleanTypeCode = 5;

    /// <summary>Index of the complete-program frontend buffer.</summary>
    public const int ProgramBufferIndex = BufferIndex + 1;
    internal const int ProgramBodyWordCount = 712;
    /// <summary>Number of 32-bit words in one complete-program function frame.</summary>
    public const int ProgramFrameWordCount = 4 + ProgramBodyWordCount + MaximumNodeCount;
    /// <summary>Maximum number of functions in a complete-program projection.</summary>
    public const int MaximumFunctions = 32;
    /// <summary>Maximum number of alias declarations in a complete-program projection.</summary>
    public const int MaximumAliases = 12;
    /// <summary>Number of words reserved for complete-program alias data.</summary>
    public const int ProgramAliasScratchWordCount = 1 + (MaximumAliases * 5);
    /// <summary>Number of 32-bit words in the complete-program projection buffer.</summary>
    public const int ProgramWordCount = 4 + (MaximumFunctions * ProgramFrameWordCount)
        + ProgramAliasScratchWordCount;

    private sealed record ProgramDeclaration(string Name, string QualifiedName, TextSpan NamespaceNameSpan,
        int ReturnType, int Effects, int OutParameterMask, int InitRefParameterMask,
        ImmutableArray<int> ParameterTypes, ImmutableArray<string> ParameterNames,
        int TokenStart, int TokenEnd, TextSpan Span, TextSpan BodySpan,
        ScalarAliasEnvironment Aliases);

    private sealed record ProgramScope(string NamespaceName, TextSpan NamespaceNameSpan,
        int TokenStart, int TokenEnd);

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

    private sealed record ProgramInventory(ImmutableArray<ProgramDeclaration> Declarations);

    private sealed record AliasDeclaration(string Name, string Target);

    private sealed record ScalarAliasEnvironment(ImmutableDictionary<string, int> Types)
    {
        public bool TryGetType(string name, out int type) => Types.TryGetValue(name, out type);
    }

    /// <summary>Reads v88 complete-program and v87 single-function projections.</summary>
    public static ScalarFrontendResult ReadResult(SourceText source, ReadOnlySpan<byte> bytes,
        ReadOnlySpan<byte> programBytes, BoundRuntimePolicy policy)
        => ReadProgram(source, bytes, programBytes, policy).Projection;

    /// <summary>Reads a complete scalar frontend result using the selected compilation options.</summary>
    public static ScalarFrontendResult ReadResult(SourceText source, ReadOnlySpan<byte> bytes,
        ReadOnlySpan<byte> programBytes, CompilationOptions options)
        => ReadProgram(source, bytes, programBytes, options).Projection;

    /// <summary>Reads and verifies a projection, retaining the bound multi-function program for its consumer.</summary>
    public static ScalarFrontendReadResult ReadProgram(SourceText source, ReadOnlySpan<byte> bytes,
        ReadOnlySpan<byte> programBytes, BoundRuntimePolicy policy)
        => ReadProgramCore(source, bytes, programBytes, policy, SourceArithmeticProfile.Development);

    /// <summary>Reads and binds a complete scalar frontend program using the selected compilation options.</summary>
    public static ScalarFrontendReadResult ReadProgram(SourceText source, ReadOnlySpan<byte> bytes,
        ReadOnlySpan<byte> programBytes, CompilationOptions options)
    {
        Compilation.ValidateOptions(options);
        return ReadProgramCore(source, bytes, programBytes, options.RuntimePolicy,
            options.SourceArithmeticProfile);
    }

    private static ScalarFrontendReadResult ReadProgramCore(SourceText source, ReadOnlySpan<byte> bytes,
        ReadOnlySpan<byte> programBytes, BoundRuntimePolicy policy,
        SourceArithmeticProfile sourceArithmeticProfile)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (programBytes.Length != ProgramWordCount * sizeof(int))
        {
            throw InvalidResult();
        }
        if (programBytes.IndexOfAnyExcept((byte)0) < 0)
        {
            return new ScalarFrontendReadResult(ReadResultCore(source, bytes, policy, null,
                sourceArithmeticProfile), null);
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
        if (words[0] != 88
            || words[1] != 1 || words[2] is < 1 or > MaximumFunctions
            || words[3] != 0 || source.Length > ScannerHost.MaximumSourceLength
            || words.AsSpan(4 + (words[2] * ProgramFrameWordCount)).IndexOfAnyExcept(0) >= 0)
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
            var frame = 4 + (i * ProgramFrameWordCount);
            if (words[frame] != declaration.TokenStart || words[frame + 1] != declaration.TokenEnd
                || words[frame + 2] != declaration.Span.Start || words[frame + 3] != declaration.Span.Length
                || words[frame + 4 + 21] != declaration.NamespaceNameSpan.Start
                || words[frame + 4 + 22] != declaration.NamespaceNameSpan.Length)
            {
                throw InvalidResult();
            }
            var payload = programBytes.Slice((frame + 4) * sizeof(int),
                ProgramBodyWordCount * sizeof(int));
            var targets = ImmutableArray.CreateBuilder<int>(MaximumNodeCount);
            var nodeCount = words[frame + 4 + 37];
            if (nodeCount is < 0 or > MaximumNodeCount)
            {
                throw InvalidResult();
            }
            for (var node = 0; node < MaximumNodeCount; node++)
            {
                var operation = node < nodeCount
                    ? words[frame + 4 + NodeWordOffset(node)] : 0;
                var target = words[frame + 4 + ProgramBodyWordCount + node];
                if (operation == 105
                    ? target < 0 || target >= declarations.Length
                    : target != -1)
                {
                    throw InvalidResult();
                }
                targets.Add(target);
            }
            var context = new ProgramReadContext(declarations, targets.MoveToImmutable(),
                declaration.Aliases);
            var functionSource = FunctionSource(source, declaration.Span);
            functionSources.Add(functionSource);
            var function = ReadResultCore(functionSource, payload, policy, context,
                sourceArithmeticProfile);
            if (!function.IsValid || function.ReturnType != declaration.ReturnType
                || function.ParameterCount != declaration.ParameterTypes.Length
                || function.OutParameterMask != declaration.OutParameterMask
                || function.InitRefParameterMask != declaration.InitRefParameterMask
                || (declaration.Effects & 2) != 0
                || function.DoesNotReturn != ((declaration.Effects & 4) != 0)
                || function.RequiresUnsafeCaller != ((declaration.Effects & 8) != 0)
                || function.NamespaceNameSpan != declaration.NamespaceNameSpan
                || SourceName(source, function.Name) != declaration.Name
                || (declaration.ParameterTypes.Length > 0
                    && (function.FirstType != declaration.ParameterTypes[0]
                        || SourceName(source, function.FirstName) != declaration.ParameterNames[0]))
                || (declaration.ParameterTypes.Length > 1
                    && (function.SecondType != declaration.ParameterTypes[1]
                        || SourceName(source, function.SecondName) != declaration.ParameterNames[1]))
                || (declaration.ParameterTypes.Length > 2
                    && (function.ThirdType != declaration.ParameterTypes[2]
                        || SourceName(source, function.ThirdName) != declaration.ParameterNames[2]))
                || (declaration.ParameterTypes.Length > 3
                    && (function.FourthType != declaration.ParameterTypes[3]
                        || SourceName(source, function.FourthName) != declaration.ParameterNames[3])))
            {
                throw InvalidResult();
            }
            functions.Add(function with { QualifiedName = declaration.QualifiedName });
        }
        var results = functions.MoveToImmutable();
        if (results.Any(static function => function.Statements.Any(
            static statement => statement.Kind == 38))
            && SyntaxTree.Parse(source).HasErrors)
        {
            throw InvalidResult();
        }
        var program = BindProgramCore(results, declarations, functionSources.MoveToImmutable(), policy,
            sourceArithmeticProfile);
        if (!SemanticVerifier.TryVerifySource(program, out var functionIndex,
            out var span, out _, out var invalidEffect))
        {
            if (System.Environment.GetEnvironmentVariable(
                    "TERRUM_DEBUG_SCALAR_FRONTEND") == "1")
            {
                System.Console.Error.WriteLine(invalidEffect);
            }
            var invalidIndex = (uint)functionIndex < (uint)results.Length ? functionIndex : 0;
            var invalidFunctions = results.SetItem(invalidIndex, results[invalidIndex] with {
                IsValid = false,
                ErrorCode = 1,
                ErrorSpan = span,
            });
            var projection = invalidFunctions[0] with {
                IsValid = false,
                ErrorCode = 1,
                ErrorSpan = span,
                Functions = invalidFunctions,
            };
            return new ScalarFrontendReadResult(projection, null);
        }
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

    /// <summary>Binds and verifies a complete scalar library using the selected compilation options.</summary>
    public static BoundProgram BindLibrary(SourceText source, ReadOnlySpan<byte> bytes,
        ReadOnlySpan<byte> programBytes, CompilationOptions options)
    {
        ArgumentNullException.ThrowIfNull(source);
        Compilation.ValidateOptions(options);
        var read = ReadProgram(source, bytes, programBytes, options);
        if (!read.Projection.IsValid)
        {
            throw InvalidResult();
        }
        return read.Program ?? BindLibraryCore(source, read.Projection, options.RuntimePolicy,
            options.SourceArithmeticProfile);
    }

    private static BoundProgram BindProgramCore(ImmutableArray<ScalarFrontendResult> results,
        ImmutableArray<ProgramDeclaration> declarations, ImmutableArray<SourceText> functionSources,
        BoundRuntimePolicy policy, SourceArithmeticProfile sourceArithmeticProfile)
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
                results[i], policy, sourceArithmeticProfile, verify: false,
                omitImplicitReturn: omitImplicitReturn).Functions[0];
            functions.Add(function with {
                Name = declarations[i].QualifiedName,
                Span = declarations[i].Span,
                Body = function.Body with { Span = declarations[i].BodySpan },
            });
        }
        var program = new BoundProgram(functions.MoveToImmutable(), -1) { RuntimePolicy = policy };
        return program;
    }

    // Preserve absolute ranges while isolating each body audit. InventoryProgram
    // separately validates allowed namespace/alias prefixes and matches every
    // projected function frame to one original declaration.
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

    private static ProgramInventory InventoryProgram(SourceText source)
    {
        var lexical = Lexer.Lex(source);
        var tokens = lexical.Tokens;
        if (!lexical.Diagnostics.IsEmpty)
        {
            throw InvalidResult();
        }
        var endToken = tokens.Length - 1;
        var scopes = new List<ProgramScope>();
        string? namespaceIdentity = null;
        var cursor = 0;
        var fileScoped = false;
        while (cursor < endToken)
        {
            if (tokens[cursor].Kind == TokenKind.NamespaceKeyword)
            {
                var namespaceKeywordIndex = cursor;
                cursor++;
                if (cursor >= endToken || tokens[cursor].Kind != TokenKind.Identifier)
                {
                    throw InvalidResult();
                }
                var firstName = tokens[cursor];
                var lastName = firstName;
                List<string> namespaceParts = [firstName.Name];
                cursor++;
                while (cursor < endToken && tokens[cursor].Kind == TokenKind.Dot)
                {
                    cursor++;
                    if (cursor >= endToken || tokens[cursor].Kind != TokenKind.Identifier)
                    {
                        throw InvalidResult();
                    }
                    lastName = tokens[cursor];
                    namespaceParts.Add(lastName.Name);
                    cursor++;
                }
                var currentNamespace = string.Join(".", namespaceParts);
                var currentNameSpan = new TextSpan(firstName.Span.Start,
                    lastName.Span.End - firstName.Span.Start);
                if (cursor >= endToken)
                {
                    throw InvalidResult();
                }
                if (tokens[cursor].Kind == TokenKind.Semicolon)
                {
                    if (namespaceKeywordIndex != 0 || scopes.Count != 0
                        || namespaceIdentity is not null
                        || (cursor + 1 < endToken
                            && tokens.Skip(cursor + 1).Take(endToken - cursor - 1)
                                .Any(static token =>
                                    token.Kind == TokenKind.NamespaceKeyword)))
                    {
                        throw InvalidResult();
                    }
                    fileScoped = true;
                    namespaceIdentity = currentNamespace;
                    scopes.Add(new ProgramScope(currentNamespace, currentNameSpan,
                        cursor + 1, endToken));
                    break;
                }
                if (tokens[cursor].Kind != TokenKind.OpenBrace || fileScoped)
                {
                    throw InvalidResult();
                }
                cursor = ReadNamespaceBody(tokens, cursor, endToken, currentNamespace,
                    currentNameSpan, scopes, 1, out var resolvedNamespace);
                namespaceIdentity ??= resolvedNamespace;
                continue;
            }

            var scopeStart = cursor;
            var declarationDepth = 0;
            while (cursor < endToken)
            {
                if (declarationDepth == 0
                    && tokens[cursor].Kind == TokenKind.NamespaceKeyword)
                {
                    break;
                }
                if (tokens[cursor].Kind == TokenKind.OpenBrace)
                {
                    declarationDepth++;
                }
                else if (tokens[cursor].Kind == TokenKind.CloseBrace)
                {
                    if (declarationDepth == 0)
                    {
                        throw InvalidResult();
                    }
                    declarationDepth--;
                }
                cursor++;
            }
            if (declarationDepth != 0)
            {
                throw InvalidResult();
            }
            if (scopeStart < cursor)
            {
                scopes.Add(new ProgramScope(string.Empty, default, scopeStart, cursor));
            }
        }
        if (scopes.Count == 0)
        {
            throw InvalidResult();
        }
        if (tokens.Any(static token => token.Kind == TokenKind.UsingKeyword))
        {
            throw InvalidResult();
        }
        if (SyntaxTree.Parse(lexical).HasErrors)
        {
            throw InvalidResult();
        }
        var declarations = ImmutableArray.CreateBuilder<ProgramDeclaration>();
        var names = new HashSet<string>(StringComparer.Ordinal);
        var aliases = new ScalarAliasEnvironment([]);
        var at = 0;
        var scopeEnd = 0;
        foreach (var scope in scopes)
        {
            aliases = InventoryScalarAliasesForScope(tokens, scopes, scope);
            at = scope.TokenStart;
            scopeEnd = scope.TokenEnd;
            while (at < scopeEnd)
            {
                if (TrySkipAlias(tokens, ref at, scopeEnd))
                {
                    continue;
                }
                var start = at;
                var effects = 0;
                while (at < scopeEnd && tokens[at].Kind == TokenKind.OpenBracket)
                {
                    if (!Take(TokenKind.OpenBracket) || at >= scopeEnd)
                    {
                        throw InvalidResult();
                    }
                    var effect = tokens[at].Text switch {
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
                while (at < scopeEnd && tokens[at].Kind is TokenKind.PublicKeyword or TokenKind.InternalKeyword
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
                if (at >= scopeEnd)
                {
                    throw InvalidResult();
                }
                var returnType = ScalarCode(tokens[at].Name, allowVoid: true, aliases);
                if (!Take(returnType == 0 ? TokenKind.VoidKeyword : TokenKind.Identifier)
                    || at >= scopeEnd || tokens[at].Kind != TokenKind.Identifier)
                {
                    throw InvalidResult();
                }
                var name = tokens[at++];
                var qualifiedName = scope.NamespaceName.Length == 0
                    ? name.Name : scope.NamespaceName + "." + name.Name;
                if (!names.Add(qualifiedName) || !Take(TokenKind.OpenParen)
                    || name.Span.End != tokens[at - 1].Span.Start)
                {
                    throw InvalidResult();
                }
                var parameterTypes = ImmutableArray.CreateBuilder<int>();
                var parameterNames = new HashSet<string>(StringComparer.Ordinal);
                var parameterNameList = ImmutableArray.CreateBuilder<string>();
                var outParameterMask = 0;
                var initRefParameterMask = 0;
                while (at < scopeEnd && tokens[at].Kind != TokenKind.CloseParen)
                {
                    if (parameterTypes.Count == 4 || (parameterTypes.Count != 0 && !Take(TokenKind.Comma)))
                    {
                        throw InvalidResult();
                    }
                    var isOut = tokens[at].Kind == TokenKind.OutKeyword;
                    var isInitRef = tokens[at].Kind == TokenKind.Identifier
                        && tokens[at].Name == "init" && tokens[at].Span.Length == 4;
                    if (isOut)
                    {
                        at++;
                    }
                    if (isInitRef
                        && (!Take(TokenKind.Identifier) || tokens[at - 1].Name != "init"
                            || !Take(TokenKind.RefKeyword) || !Take(TokenKind.Less)))
                    {
                        throw InvalidResult();
                    }
                    if (at >= scopeEnd)
                    {
                        throw InvalidResult();
                    }
                    var type = ScalarCode(tokens[at].Name, allowVoid: false, aliases);
                    if (!Take(TokenKind.Identifier)
                        || (isInitRef && !Take(TokenKind.Greater))
                        || at >= scopeEnd
                        || tokens[at].Kind != TokenKind.Identifier || !parameterNames.Add(tokens[at].Name))
                    {
                        throw InvalidResult();
                    }
                    parameterNameList.Add(tokens[at].Name);
                    at++;
                    if (isOut)
                    {
                        outParameterMask |= 1 << parameterTypes.Count;
                    }
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
                TextSpan bodySpan;
                if (Take(TokenKind.Arrow))
                {
                    var bodyStart = tokens[at - 1].Span.Start;
                    while (at < scopeEnd && tokens[at].Kind != TokenKind.Semicolon)
                    {
                        at++;
                    }
                    if (!Take(TokenKind.Semicolon))
                    {
                        throw InvalidResult();
                    }
                    bodySpan = new TextSpan(bodyStart, tokens[at - 1].Span.End - bodyStart);
                }
                else if (Take(TokenKind.OpenBrace))
                {
                    var bodyStart = tokens[at - 1].Span.Start;
                    var depth = 1;
                    while (at < scopeEnd && depth != 0)
                    {
                        if (tokens[at].Kind == TokenKind.OpenBrace)
                        {
                            depth++;
                        }
                        if (tokens[at].Kind == TokenKind.CloseBrace)
                        {
                            depth--;
                        }
                        at++;
                    }
                    if (depth != 0)
                    {
                        throw InvalidResult();
                    }
                    bodySpan = new TextSpan(bodyStart, tokens[at - 1].Span.End - bodyStart);
                }
                else
                {
                    throw InvalidResult();
                }
                if (declarations.Count == MaximumFunctions)
                {
                    throw InvalidResult();
                }
                declarations.Add(new ProgramDeclaration(name.Name, qualifiedName, scope.NamespaceNameSpan,
                    returnType, effects, outParameterMask, initRefParameterMask,
                    parameterTypes.ToImmutable(), parameterNameList.ToImmutable(),
                    start, at, new TextSpan(tokens[start].Span.Start,
                        tokens[at - 1].Span.End - tokens[start].Span.Start), bodySpan,
                    aliases));
            }
            if (at != scopeEnd)
            {
                throw InvalidResult();
            }
        }
        if (tokens[endToken].Kind != TokenKind.EndOfFile)
        {
            throw InvalidResult();
        }
        return new ProgramInventory(declarations.ToImmutable());

        bool Take(TokenKind kind)
        {
            if (at >= scopeEnd || tokens[at].Kind != kind)
            {
                return false;
            }
            at++;
            return true;
        }
    }

    private static int ReadNamespaceBody(ImmutableArray<SyntaxToken> tokens, int openingIndex,
        int endToken, string namespaceIdentity, TextSpan namespaceNameSpan, List<ProgramScope> scopes,
        int nestingDepth, out string resolvedNamespace)
    {
        if (nestingDepth > 8)
        {
            throw InvalidResult();
        }
        var namespaceParts = new List<string>(namespaceIdentity.Split('.'));
        resolvedNamespace = namespaceIdentity;
        var cursor = openingIndex + 1;
        var segmentStart = cursor;
        var declarationDepth = 0;
        while (cursor < endToken)
        {
            if (declarationDepth == 0
                && tokens[cursor].Kind == TokenKind.CloseBrace)
            {
                AddScope(segmentStart, cursor);
                return cursor + 1;
            }
            if (declarationDepth == 0
                && tokens[cursor].Kind == TokenKind.NamespaceKeyword)
            {
                AddScope(segmentStart, cursor);
                var previousPartCount = namespaceParts.Count;
                var (childOpening, childNameSpan) = ReadNestedNamespaceHeader(
                    tokens, cursor, endToken, namespaceParts);
                cursor = ReadNamespaceBody(tokens, childOpening, endToken,
                    string.Join(".", namespaceParts), childNameSpan, scopes,
                    nestingDepth + 1, out _);
                namespaceParts.RemoveRange(previousPartCount,
                    namespaceParts.Count - previousPartCount);
                segmentStart = cursor;
                continue;
            }
            if (tokens[cursor].Kind == TokenKind.OpenBrace)
            {
                declarationDepth++;
            }
            else if (tokens[cursor].Kind == TokenKind.CloseBrace)
            {
                if (declarationDepth == 0)
                {
                    throw InvalidResult();
                }
                declarationDepth--;
            }
            cursor++;
        }
        throw InvalidResult();

        void AddScope(int start, int end)
        {
            if (start < end)
            {
                scopes.Add(new ProgramScope(namespaceIdentity, namespaceNameSpan,
                    start, end));
            }
        }
    }

    private static (int OpeningIndex, TextSpan NameSpan) ReadNestedNamespaceHeader(
        ImmutableArray<SyntaxToken> tokens, int namespaceKeywordIndex, int endToken,
        List<string> namespaceParts)
    {
        var cursor = namespaceKeywordIndex + 1;
        if (cursor >= endToken || tokens[cursor].Kind != TokenKind.Identifier)
        {
            throw InvalidResult();
        }
        var firstName = tokens[cursor];
        var lastName = firstName;
        namespaceParts.Add(firstName.Name);
        cursor++;
        while (cursor < endToken && tokens[cursor].Kind == TokenKind.Dot)
        {
            cursor++;
            if (cursor >= endToken || tokens[cursor].Kind != TokenKind.Identifier)
            {
                throw InvalidResult();
            }
            lastName = tokens[cursor];
            namespaceParts.Add(lastName.Name);
            cursor++;
        }
        if (cursor >= endToken || tokens[cursor].Kind != TokenKind.OpenBrace)
        {
            throw InvalidResult();
        }
        var namespaceNameSpan = new TextSpan(firstName.Span.Start,
            lastName.Span.End - firstName.Span.Start);
        return (cursor, namespaceNameSpan);
    }

    private static bool TrySkipAlias(ImmutableArray<SyntaxToken> tokens, ref int at, int end)
    {
        var start = at;
        while (at < end && tokens[at].Kind is TokenKind.PublicKeyword or TokenKind.InternalKeyword)
        {
            at++;
        }
        if (at >= end || tokens[at].Kind != TokenKind.AliasKeyword)
        {
            at = start;
            return false;
        }
        while (at < end && tokens[at].Kind != TokenKind.Semicolon)
        {
            at++;
        }
        if (at >= end)
        {
            throw InvalidResult();
        }
        at++;
        return true;
    }

    private static ScalarAliasEnvironment InventoryScalarAliasesForScope(
        ImmutableArray<SyntaxToken> tokens, List<ProgramScope> scopes,
        ProgramScope target)
    {
        var declarations = new Dictionary<string, AliasDeclaration>(
            StringComparer.Ordinal);
        foreach (var scope in scopes)
        {
            if (!IsNamespaceAncestor(scope.NamespaceName,
                target.NamespaceName))
            {
                continue;
            }
            foreach (var (name, declaration) in
                InventoryScalarAliasDeclarations(tokens, scope.TokenStart,
                    scope.TokenEnd))
            {
                if (declarations.Count == MaximumAliases
                    || !declarations.TryAdd(name, declaration))
                {
                    throw InvalidResult();
                }
            }
        }
        return ResolveScalarAliases(declarations);

        static bool IsNamespaceAncestor(string ancestor, string current)
        {
            return ancestor.Length == 0
                || string.Equals(ancestor, current, StringComparison.Ordinal)
                || current.StartsWith(ancestor + ".", StringComparison.Ordinal);
        }
    }

    private static ScalarAliasEnvironment InventoryScalarAliases(
        ImmutableArray<SyntaxToken> tokens,
        int initialTokenIndex = 0, int endTokenIndex = -1)
        => ResolveScalarAliases(InventoryScalarAliasDeclarations(tokens,
            initialTokenIndex, endTokenIndex));

    private static Dictionary<string, AliasDeclaration>
        InventoryScalarAliasDeclarations(ImmutableArray<SyntaxToken> tokens,
            int initialTokenIndex = 0, int endTokenIndex = -1)
    {
        var declarations = new Dictionary<string, AliasDeclaration>(StringComparer.Ordinal);
        var end = endTokenIndex < 0 ? tokens.Length - 1 : endTokenIndex;
        var tokenLimit = endTokenIndex < 0 ? tokens.Length : endTokenIndex;
        var at = initialTokenIndex;
        while (at < end)
        {
            var start = at;
            var hasAttributes = false;
            while (at < tokenLimit && tokens[at].Kind == TokenKind.OpenBracket)
            {
                hasAttributes = true;
                while (at < tokenLimit && tokens[at++].Kind != TokenKind.CloseBracket)
                {
                }
            }
            var visibility = 0;
            while (at < tokenLimit && tokens[at].Kind is TokenKind.PublicKeyword or TokenKind.InternalKeyword)
            {
                var currentVisibility = tokens[at++].Kind == TokenKind.PublicKeyword ? 1 : 2;
                if (visibility != 0)
                {
                    throw InvalidResult();
                }
                visibility = currentVisibility;
            }
            if (at < tokenLimit && tokens[at].Kind == TokenKind.AliasKeyword)
            {
                if (hasAttributes || visibility == 1)
                {
                    throw InvalidResult();
                }
                at++;
                if (at + 3 >= tokenLimit || tokens[at].Kind != TokenKind.Identifier
                    || tokens[at + 1].Kind != TokenKind.Equals
                    || tokens[at + 2].Kind != TokenKind.Identifier
                    || tokens[at + 3].Kind != TokenKind.Semicolon
                    || declarations.Count == MaximumAliases
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
            for (var index = at; index < end; index++)
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
                while (at < end && tokens[at].Kind != TokenKind.Semicolon)
                {
                    at++;
                }
                if (at >= end)
                {
                    throw InvalidResult();
                }
                at++;
            }
            else
            {
                var depth = 1;
                at = bodyStart + 1;
                while (at < end && depth != 0)
                {
                    if (tokens[at].Kind == TokenKind.OpenBrace)
                    {
                        depth++;
                    }
                    if (tokens[at].Kind == TokenKind.CloseBrace)
                    {
                        depth--;
                    }
                    at++;
                }
                if (depth != 0)
                {
                    throw InvalidResult();
                }
            }
        }

        return declarations;
    }

    private static ScalarAliasEnvironment ResolveScalarAliases(
        Dictionary<string, AliasDeclaration> declarations)
    {
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
