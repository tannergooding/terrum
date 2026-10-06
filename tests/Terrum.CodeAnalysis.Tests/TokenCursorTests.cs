// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Lexing;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Terrum.CodeAnalysis.Text;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed class TokenCursorTests
{
    private const string ForwardingDeclarations = """
        using Terrum.CodeAnalysis;
        [return: DependsOn(nameof(tokens))]
        internal Span<Token> Identity(Span<Token> tokens) { return tokens; }
        [return: DependsOn(nameof(left), nameof(right))]
        internal Span<Token> Choose(Span<Token> left, Span<Token> right, bool useLeft) {
            return useLeft ? left : right;
        }
        [return: DependsOn(nameof(first), nameof(second))]
        internal Span<Token> Forward(Span<Token> first, Span<Token> second, bool useFirst) {
            return Choose(right: second, left: first, useLeft: useFirst);
        }
        [return: DependsOn(nameof(tokens))]
        internal Span<Token> ForwardWithCleanup(Span<Token> tokens, bool failCleanup) {
            try {
                require !failCleanup;
                return tokens;
            } finally { }
        }
        """;

    [Fact]
    public void SpanMetadataInspectionPreservesFunctionShortCircuitOrder()
    {
        var malformed = new BoundFunction("Malformed", PrimitiveType.Int32, [], 0,
            new BoundBlock(default, default), default);
        var captured = new BoundFunction("Captured", PrimitiveType.Int32, [], 0,
            new BoundBlock(default, default), default) { CapturedParameters = [0] };
        Assert.True(InspectSpanMetadata(new BoundProgram([malformed, captured], -1)));

        var spanBody = new BoundBlock([
            new BoundReturn(new BoundSnapshotSpan(0, 0, default), default),
        ], default);
        var spanFunction = new BoundFunction("SpanNode", PrimitiveType.Int32, [], 0, spanBody, default);
        var error = Assert.Throws<TargetInvocationException>(
            () => InspectSpanMetadata(new BoundProgram([malformed, spanFunction], -1)));
        Assert.Equal("A span-inspected block requires initialized statements.",
            Assert.IsType<ArgumentException>(error.InnerException).Message);

        Assert.True(InspectSpanMetadata(new BoundProgram([spanFunction], -1)));
    }

    private static bool InspectSpanMetadata(BoundProgram program)
    {
        var contractType = Assert.IsAssignableFrom<Type>(typeof(SemanticVerifier).Assembly.GetType(
            "Terrum.CodeAnalysis.Semantics.TokenSpanContract", throwOnError: true));
        var method = Assert.IsAssignableFrom<MethodInfo>(contractType.GetMethod("HasMetadata",
            BindingFlags.NonPublic | BindingFlags.Static));
        return Assert.IsType<bool>(method.Invoke(null, [program]));
    }

    private const string EntryPrefix = """
        using Terrum.CodeAnalysis;
        bool Pick() { return false; }
        bool Replace(out TokenSnapshot value) {
            require TokenSnapshot.TryScan(init var fresh);
            value = move fresh;
            return true;
        }
        uint64 OwnerReplaceIndex(bool ignored) { return 0; }
        int32 Observe(Span<Token> tokens, bool ignored) { return 42; }
        int32 Consume(TokenCursor cursor, bool ignored) { return 42; }
        int32 Main() {
            require TokenSnapshot.TryScan(init var scanned);
            mut TokenSnapshot owner = move scanned;
        """;

    [Theory]
    [InlineData("")]
    [InlineData("alpha 42")]
    [InlineData(".. ..^ ?.")]
    [InlineData("@await")]
    public async Task CursorReadsTheSnapshotAndRetainsOnlyItsDescriptorAsync(string text)
    {
        const string Entry = EntryPrefix + """
            Span<Token> tokens = owner.AsSpan();
            require tokens.Length == uint64(owner.GetCount());
            mut TokenCursor cursor = new TokenCursor(tokens);
            mut int32 index = 0;
            while (cursor.TryRead(out var token)) {
                require token.Kind == Host.Tokens[index * 3];
                require token.Range.Start == Host.Tokens[index * 3 + 1];
                require token.Range.Length == Host.Tokens[index * 3 + 2];
                index = index + 1;
            }
            require index == owner.GetCount();
            require !cursor.TryRead(out var exhausted);
            cursor = _;
            tokens = _;
            owner = _;
            return 42;
        }
        """;
        foreach (var policy in new[] { BoundRuntimePolicy.FailFast, BoundRuntimePolicy.CheckedExceptions })
        {
            var program = Program(await CompileAsync(text, Entry, policy));
            var cursor = Assert.Single(program.Structures, static structure => structure.Name == "Terrum.CodeAnalysis.TokenCursor");
            Assert.Equal(16, cursor.Size);
            Assert.Equal(16, Assert.Single(cursor.Fields).Size);
            Assert.Equal(PrimitiveType.TokenSpan, Assert.Single(cursor.Fields).Type);
            var constructor = Assert.Single(program.Functions,
                static function => function.Name == "Terrum.CodeAnalysis.TokenCursor.#ctor");
            Assert.Equal(1, Assert.Single(constructor.CapturedParameters));
            await ExecuteAsync(program, policy);
        }
    }

    [Theory]
    [InlineData(BoundRuntimePolicy.FailFast)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions)]
    public async Task CursorTraversesMaximumScannerTokenSnapshotAsync(BoundRuntimePolicy policy)
    {
        var identifierCount = ScannerHost.MaximumSourceLength / 2;
        var expectedTokenCount = identifierCount + 1;
        var input = string.Concat(Enumerable.Repeat("a ", identifierCount));
        Assert.Equal(ScannerHost.MaximumSourceLength, input.Length);
        var entry = $$"""
            using Terrum.CodeAnalysis;
            int32 Main() {
                require TokenSnapshot.TryScan(init var scanned);
                mut TokenSnapshot owner = move scanned;
                mut TokenCursor cursor = new TokenCursor(owner.AsSpan());
                mut int32 index = 0;
                while (cursor.TryRead(out var token)) {
                    if (index < {{identifierCount}}) {
                        if (token.Kind != 2 || token.Range.Start != index * 2
                            || token.Range.Length != 1) { return 1; }
                    } else if (index == {{identifierCount}}) {
                        if (token.Kind != 0 || token.Range.Start != {{ScannerHost.MaximumSourceLength}}
                            || token.Range.Length != 0) { return 2; }
                    } else {
                        return 3;
                    }
                    index = index + 1;
                }
                if (index != {{expectedTokenCount}}
                    || cursor.TryRead(out var exhausted)) { return 4; }
                cursor = _;
                owner = _;
                return 42;
            }
            """;
        var compilation = await CompileAsync(input, entry, policy).ConfigureAwait(true);
        await ExecuteAsync(Program(compilation), policy).ConfigureAwait(true);
    }

    [Theory]
    [InlineData(BoundRuntimePolicy.FailFast)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions)]
    public async Task ForwardedSpanResultsRetainDeclaredBackingThroughTransitiveCallsAsync(BoundRuntimePolicy policy)
    {
        var entry = ForwardingDeclarations + """
            int32 Main() {
                require TokenSnapshot.TryScan(init var firstScanned);
                require TokenSnapshot.TryScan(init var secondScanned);
                mut TokenSnapshot first = move firstScanned;
                mut TokenSnapshot second = move secondScanned;
                Span<Token> result = Identity(Forward(second: second.AsSpan(), first: first.AsSpan(),
                    useFirst: false));
                require result.Length == uint64(first.GetCount());
                require result[0].Kind == 2;
                result = _;
                first = _;
                second = _;
                return 42;
            }
            """;
        var program = Program(await CompileAsync("alpha 42", entry, policy).ConfigureAwait(true));
        await ExecuteAsync(program, policy).ConfigureAwait(true);
        _ = Assert.Throws<ModuleContractException>(() => ModuleCompiler.CreateProducer(program, ["Main"]));
        _ = Assert.Throws<ArgumentException>(() => NativeEmitter.EmitLibrary(program, ["Main"], (NativeRuntimePolicy)policy));
        _ = Assert.Throws<NotSupportedException>(() => TrRmExecutableCompiler.Emit(program, "forwarded-span"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DirectCursorConstructorArgumentsRetainForwardedBackingAsync(bool throughForwarder)
    {
        var view = throughForwarder ? "Identity(owner.AsSpan())" : "owner.AsSpan()";
        var entry = ForwardingDeclarations + $$"""
            int32 Consume(TokenCursor cursor, bool ignored) { return 42; }
            int32 Main() {
                require TokenSnapshot.TryScan(init var scanned);
                mut TokenSnapshot owner = move scanned;
                int32 result = Consume(new TokenCursor({{view}}), false);
                owner = _;
                if (result != 42) { return 1; }
                return 42;
            }
            """;
        var program = Program(await CompileAsync("alpha 42", entry,
            BoundRuntimePolicy.CheckedExceptions).ConfigureAwait(true));
        await ExecuteAsync(program, BoundRuntimePolicy.CheckedExceptions).ConfigureAwait(true);
    }

    [Fact]
    public async Task ForwardedSpanResultRetainsEveryDeclaredSourceUntilRetiredAsync()
    {
        var entry = ForwardingDeclarations + """
            int32 Main() {
                require TokenSnapshot.TryScan(init var firstScanned);
                require TokenSnapshot.TryScan(init var secondScanned);
                mut TokenSnapshot first = move firstScanned;
                mut TokenSnapshot second = move secondScanned;
                Span<Token> result = Choose(first.AsSpan(), second.AsSpan(), false);
                first = _;
                result = _;
                second = _;
                return 42;
            }
            """;
        var compilation = await CompileAsync("alpha 42", entry, BoundRuntimePolicy.FailFast).ConfigureAwait(true);
        Assert.Contains(compilation.Diagnostics, static diagnostic => diagnostic.Code == "TRM3023");
        Assert.True(compilation.HasErrors);
        Assert.Null(compilation.Program);
    }

    [Fact]
    public async Task ForwardedSpanCannotOutliveASourceOwnersLexicalScopeAsync()
    {
        var entry = ForwardingDeclarations + """
            int32 Main() {
                require TokenSnapshot.TryScan(init var firstScanned);
                mut TokenSnapshot first = move firstScanned;
                mut TokenCursor result = new TokenCursor(first.AsSpan());
                {
                    require TokenSnapshot.TryScan(init var scopedScanned);
                    mut TokenSnapshot scoped = move scopedScanned;
                    result = new TokenCursor(Forward(scoped.AsSpan(), scoped.AsSpan(), true));
                }
                result = _;
                first = _;
                return 42;
            }
            """;
        var compilation = await CompileAsync("alpha 42", entry, BoundRuntimePolicy.FailFast).ConfigureAwait(true);
        Assert.Contains(compilation.Diagnostics, static diagnostic => diagnostic.Code == "TRM3023");
        Assert.True(compilation.HasErrors);
        Assert.Null(compilation.Program);
    }

    [Fact]
    public async Task ForwardedSpanOwnershipFailureKeepsItsDeclaringSourceContextAsync()
    {
        var entry = ForwardingDeclarations + """
            [return: DependsOn(nameof(left))]
            internal Span<Token> Invalid(Span<Token> left, Span<Token> right) { return right; }
            int32 Main() { return 42; }
            """;
        var compilation = await CompileAsync("alpha 42", entry, BoundRuntimePolicy.FailFast).ConfigureAwait(true);
        var diagnostic = Assert.Single(compilation.Diagnostics,
            static diagnostic => diagnostic.Code == "TRM3023");
        Assert.InRange(diagnostic.Span.End, 1, entry.Length);
        Assert.Null(compilation.Program);
    }

    [Fact]
    public async Task MandatoryVerifierRejectsForgedForwardedSpanMetadataAndMalformedCallsAsync()
    {
        var entry = ForwardingDeclarations + """
            int32 Main() {
                require TokenSnapshot.TryScan(init var scanned);
                mut TokenSnapshot owner = move scanned;
                Span<Token> result = Identity(Forward(owner.AsSpan(), owner.AsSpan(), false));
                result = _;
                owner = _;
                return 42;
            }
            """;
        var program = Program(await CompileAsync("alpha 42", entry, BoundRuntimePolicy.FailFast).ConfigureAwait(true));
        var identity = Assert.Single(program.Functions, static function => function.Name == "Identity");
        var identityIndex = program.Functions.IndexOf(identity);
        var forward = Assert.Single(program.Functions, static function => function.Name == "Forward");
        var forwardIndex = program.Functions.IndexOf(forward);
        var mainIndex = program.EntryPoint;
        var identityReturn = Assert.IsType<BoundReturn>(Assert.Single(identity.Body.Statements));
        var malformedConditional = new BoundConditional(
            new BoundConstant(1, PrimitiveType.Bool, identity.Span),
            new BoundRead(0, PrimitiveType.TokenSpan, identity.Span), null!,
            PrimitiveType.TokenSpan, identity.Span);
        Reject(program with {
            Functions = program.Functions.SetItem(identityIndex, identity with {
                Body = identity.Body with {
                    Statements = [identityReturn with { Value = malformedConditional }],
                },
            }),
        });
        var cursorConstructor = Assert.Single(program.Functions,
            static function => function.Name == "Terrum.CodeAnalysis.TokenCursor.#ctor");
        var cursorConstructorIndex = program.Functions.IndexOf(cursorConstructor);
        Reject(program with {
            Functions = program.Functions.SetItem(identityIndex, identity with {
                Body = identity.Body with {
                    Statements = [identityReturn with {
                        Value = new BoundCall(cursorConstructorIndex, [], PrimitiveType.TokenSpan, identity.Span),
                    }],
                },
            }),
        });
        Reject(program with {
            Functions = program.Functions.SetItem(identityIndex, identity with { IsInternal = false }),
        });

        BoundExpression RewriteExpression(BoundExpression expression, int targetFunction) => expression switch {
            BoundCall call when call.Function == targetFunction
                => call with { Arguments = [] },
            BoundCall call => call with {
                Arguments = [.. call.Arguments.Select(argument => RewriteExpression(argument, targetFunction))],
            },
            BoundConditional conditional => conditional with {
                Condition = RewriteExpression(conditional.Condition, targetFunction),
                WhenTrue = RewriteExpression(conditional.WhenTrue, targetFunction),
                WhenFalse = RewriteExpression(conditional.WhenFalse, targetFunction),
            },
            BoundFieldRead field => field with { Receiver = RewriteExpression(field.Receiver, targetFunction) },
            BoundSpanAdvance advance => advance with { View = RewriteExpression(advance.View, targetFunction) },
            _ => expression,
        };
        BoundStatement RewriteStatement(BoundStatement statement, int targetFunction) => statement switch {
            BoundBlock block => block with {
                Statements = [.. block.Statements.Select(child => RewriteStatement(child, targetFunction))],
            },
            BoundTryFinally region => region with {
                Body = (BoundBlock)RewriteStatement(region.Body, targetFunction),
                Finally = (BoundBlock)RewriteStatement(region.Finally, targetFunction),
            },
            BoundLocal { Initializer: BoundExpression initializer } local => local with {
                Initializer = RewriteExpression(initializer, targetFunction),
            },
            BoundReturn { Value: BoundExpression value } result => result with {
                Value = RewriteExpression(value, targetFunction),
            },
            BoundRequire requirement => requirement with {
                Condition = RewriteExpression(requirement.Condition, targetFunction),
            },
            _ => statement,
        };
        foreach (var targetFunction in new[] { identityIndex, forwardIndex })
        {
            var malformedMain = program.Functions[mainIndex] with {
                Body = (BoundBlock)RewriteStatement(program.Functions[mainIndex].Body, targetFunction),
            };
            Reject(program with { Functions = program.Functions.SetItem(mainIndex, malformedMain) });
        }
    }

    [Fact]
    public async Task ForwardedSpanArgumentIsStagedBeforeLaterArgumentEvaluationAsync()
    {
        var entry = ForwardingDeclarations + """
            bool Replace(out TokenSnapshot value) {
                require TokenSnapshot.TryScan(init var fresh);
                value = move fresh;
                return true;
            }
            int32 Observe(Span<Token> tokens, bool ignored) { return 42; }
            int32 Main() {
                require TokenSnapshot.TryScan(init var scanned);
                mut TokenSnapshot owner = move scanned;
                return Observe(Identity(owner.AsSpan()), Replace(out owner));
            }
            """;
        var compilation = await CompileAsync("alpha 42", entry, BoundRuntimePolicy.FailFast).ConfigureAwait(true);
        Assert.True(compilation.HasErrors);
        Assert.Null(compilation.Program);
    }

    [Fact]
    public async Task CheckedCleanupFailureDoesNotPublishAForwardedSpanResultAsync()
    {
        var entry = ForwardingDeclarations + """
            int32 Main() {
                require TokenSnapshot.TryScan(init var scanned);
                mut TokenSnapshot owner = move scanned;
                unsafe {
                    try {
                        Span<Token> failed = ForwardWithCleanup(owner.AsSpan(), true);
                        return 1;
                    } catch { }
                }
                Span<Token> result = ForwardWithCleanup(owner.AsSpan(), false);
                require result.Length == uint64(owner.GetCount());
                require result[0].Kind == 2;
                result = _;
                owner = _;
                return 42;
            }
            """;
        var program = Program(await CompileAsync("alpha 42", entry,
            BoundRuntimePolicy.CheckedExceptions).ConfigureAwait(true));
        await ExecuteAsync(program, BoundRuntimePolicy.CheckedExceptions).ConfigureAwait(true);
    }

    [Theory]
    [InlineData("""
        internal Span<Token> MissingSources(Span<Token> tokens) { return tokens; }
        """)]
    [InlineData("""
        [return: DependsOn(nameof(left))]
        internal Span<Token> UndeclaredSource(Span<Token> left, Span<Token> right, bool selectRight) {
            return selectRight ? right : left;
        }
        """)]
    [InlineData("""
        [return: DependsOn(nameof(tokens), nameof(tokens))]
        internal Span<Token> DuplicateSource(Span<Token> tokens) { return tokens; }
        """)]
    [InlineData("""
        [return: DependsOn(nameof(tokens))]
        [return: DependsOn(nameof(tokens))]
        internal Span<Token> DuplicateDependencies(Span<Token> tokens) { return tokens; }
        """)]
    [InlineData("""
        [return: DependsOn(nameof(tokens))]
        public Span<Token> NonInternal(Span<Token> tokens) { return tokens; }
        """)]
    [InlineData("""
        [DependsOn(nameof(tokens))]
        internal Span<Token> WrongDependencyTarget(Span<Token> tokens) { return tokens; }
        """)]
    [InlineData("""
        [return: DependsOn(nameof(select))]
        internal Span<Token> NonSpanSource(bool select, Span<Token> tokens) { return tokens; }
        """)]
    public async Task InvalidForwardedSpanContractsAreRejectedEvenWhenUncalledAsync(string declaration)
    {
        var entry = "using Terrum.CodeAnalysis; " + declaration + " int32 Main() { return 42; }";
        var compilation = await CompileAsync("", entry, BoundRuntimePolicy.FailFast).ConfigureAwait(true);
        Assert.True(compilation.HasErrors);
        Assert.Null(compilation.Program);
    }

    [Fact]
    public async Task ForwardedSpanCannotEscapeALocalOwnerAsync()
    {
        var entry = """
            using Terrum.CodeAnalysis;
            [return: DependsOn(nameof(tokens))]
            internal Span<Token> Escape(Span<Token> tokens) {
                require TokenSnapshot.TryScan(init var scanned);
                TokenSnapshot owner = move scanned;
                Span<Token> local = owner.AsSpan();
                return local;
            }
            int32 Main() { return 42; }
            """;
        var compilation = await CompileAsync("alpha 42", entry, BoundRuntimePolicy.FailFast).ConfigureAwait(true);
        Assert.True(compilation.HasErrors);
        Assert.Null(compilation.Program);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SnapshotConstructionPathsBothProduceAValidatedViewAsync(bool lossless)
    {
        var build = lossless ? "require TryScanLossless(init var scanned, init var trivia);"
            : "require TokenSnapshot.TryScan(init var scanned);";
        var entry = $$"""
            using Terrum.CodeAnalysis;
            uint64 Length(Span<Token> tokens) { return tokens.Length; }
            int32 Main() {
                {{build}}
                mut TokenSnapshot owner = move scanned;
                Span<Token> tokens = owner.AsSpan();
                require Length(tokens) == 3;
                Host.Tokens[0] = 0;
                Host.Tokens[1] = 0;
                require tokens[0].Kind == 2 && tokens[1].Kind == 3;
                mut TokenCursor first = new TokenCursor(tokens);
                mut TokenCursor second = move first;
                require second.TryRead(out var token);
                require token.Range.Start == 0 && token.Range.Length == 5;
                second = _;
                tokens = _;
                owner = _;
                return 42;
            }
            """;
        foreach (var policy in new[] { BoundRuntimePolicy.FailFast, BoundRuntimePolicy.CheckedExceptions })
        {
            await ExecuteAsync(Program(await CompileAsync("alpha 42", entry, policy)), policy);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("alpha 42")]
    public async Task IndexedReadsAreRepeatableAndDoNotAdvanceAsync(string text)
    {
        const string Entry = """
            using Terrum.CodeAnalysis;
            int32 Main() {
                require TokenSnapshot.TryScan(init var scanned);
                mut TokenSnapshot owner = move scanned;
                mut TokenCursor cursor = new TokenCursor(owner.AsSpan());
                int32 count = owner.GetCount();
                if (!cursor.TryGet(0, init var first)
                    || !cursor.TryGet(0, init var repeated)
                    || first.Kind != repeated.Kind
                    || first.Range.Start != repeated.Range.Start
                    || first.Range.Length != repeated.Range.Length) { return 1; }
                if (!cursor.TryGet(count - 1, init var last) || last.Kind != 0) { return 2; }
                if (cursor.TryGet(-1, init var negative)
                    || cursor.TryGet(count, init var beyond)) { return 3; }
                if (!cursor.TryRead(out var next)
                    || next.Kind != first.Kind
                    || next.Range.Start != first.Range.Start
                    || next.Range.Length != first.Range.Length) { return 4; }
                cursor = _;
                owner = _;
                return 42;
            }
            """;

        foreach (var policy in new[] { BoundRuntimePolicy.FailFast, BoundRuntimePolicy.CheckedExceptions })
        {
            await ExecuteAsync(Program(await CompileAsync(text, Entry, policy)), policy);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("alpha 42")]
    public async Task ManagedReaderCanUseBorrowedSpanAsAMethodParameterAsync(string text)
    {
        const string Entry = """
            using Terrum.CodeAnalysis;
            mut class TokenViewReader {
                private mut int32 _initialized;

                public TokenViewReader() {
                    _initialized = 1;
                }

                public bool TryGet(Span<Token> tokens, int32 index, init ref<Token> token) {
                    if (index < 0) { return false; }
                    uint64 unsignedIndex = uint64(index);
                    if (unsignedIndex >= tokens.Length) { return false; }
                    token = tokens[unsignedIndex];
                    return true;
                }
            }
            int32 Main() {
                require TokenSnapshot.TryScan(init var scanned);
                mut TokenSnapshot owner = move scanned;
                Span<Token> tokens = owner.AsSpan();
                require TokenViewReader.TryCreate(out var reader);
                if (!reader.TryGet(tokens, 0, init var first)
                    || !reader.TryGet(tokens, 0, init var repeated)
                    || first.Kind != repeated.Kind
                    || first.Range.Start != repeated.Range.Start
                    || first.Range.Length != repeated.Range.Length) { return 1; }
                if (reader.TryGet(tokens, -1, init var negative)
                    || reader.TryGet(tokens, owner.GetCount(), init var beyond)) { return 2; }
                tokens = _;
                owner = _;
                return 42;
            }
            """;

        foreach (var policy in new[] { BoundRuntimePolicy.FailFast, BoundRuntimePolicy.CheckedExceptions })
        {
            await ExecuteAsync(Program(await CompileAsync(text, Entry, policy)), policy);
        }
    }

    [Theory]
    [InlineData("Span<Token> tokens = owner.AsSpan(); owner = _; return 42;")]
    [InlineData("Span<Token> tokens = owner.AsSpan(); TokenSnapshot moved = move owner; return 42;")]
    [InlineData("Span<Token> tokens = owner.AsSpan(); require Replace(out owner); return 42;")]
    [InlineData("Span<Token> tokens = owner.AsSpan(); require TokenSnapshot.TryScan(init var fresh); owner = move fresh; return 42;")]
    [InlineData("mut TokenCursor cursor = new TokenCursor(owner.AsSpan()); owner = _; return 42;")]
    [InlineData("mut TokenCursor cursor = new TokenCursor(owner.AsSpan()); mut TokenCursor moved = move cursor; owner = _; return 42;")]
    [InlineData("mut TokenCursor cursor = new TokenCursor(owner.AsSpan()); while (cursor.TryRead(out var token)) { } owner = _; return 42;")]
    [InlineData("mut TokenCursor cursor = new TokenCursor(owner.AsSpan()); if (Pick()) { cursor = _; } owner = _; return 42;")]
    [InlineData("return Observe(owner.AsSpan(), Replace(out owner));")]
    [InlineData("mut TokenCursor cursor = new TokenCursor(owner.AsSpan()); return Consume(move cursor, Replace(out owner));")]
    [InlineData("mut TokenCursor cursor = new TokenCursor(owner.AsSpan()); return Consume(cursor: move cursor, ignored: Replace(out owner));")]
    [InlineData("Token token = owner.AsSpan()[OwnerReplaceIndex(Replace(out owner))]; return 42;")]
    [InlineData("Span<Token> tokens = owner.AsSpan(); unsafe { try { require false; } finally { owner = _; } } return 42;")]
    public async Task LiveAndStagedDependenciesPreventOwnerInvalidationAsync(string body)
    {
        var compilation = await CompileAsync("", EntryPrefix + body + "}", BoundRuntimePolicy.CheckedExceptions);
        Assert.True(compilation.HasErrors);
        Assert.Null(compilation.Program);
        Assert.Contains(compilation.Diagnostics, static diagnostic => diagnostic.Code == "TRM3023");
    }

    [Theory]
    [InlineData("{ Span<Token> tokens = owner.AsSpan(); require tokens.Length == 1; } owner = _; return 42;")]
    [InlineData("Span<Token> tokens = owner.AsSpan(); Span<Token> copied = tokens; tokens = _; require copied.Length == 1; copied = _; owner = _; return 42;")]
    [InlineData("mut TokenCursor cursor = new TokenCursor(owner.AsSpan()); if (Pick()) { cursor = _; } else { cursor = _; } owner = _; return 42;")]
    [InlineData("mut TokenCursor cursor = new TokenCursor(owner.AsSpan()); while (cursor.TryRead(out var token)) { } cursor = _; owner = _; return 42;")]
    [InlineData("unsafe { try { Span<Token> tokens = owner.AsSpan(); Token token = tokens[uint64.MaxValue]; } catch { owner = _; } } return 42;")]
    public async Task DependenciesRetireAtTheirOwnEndsJoinsAndUnwindScopesAsync(string body)
    {
        var program = Program(await CompileAsync("", EntryPrefix + body + "}", BoundRuntimePolicy.CheckedExceptions));
        await ExecuteAsync(program, BoundRuntimePolicy.CheckedExceptions);
    }

    [Fact]
    public async Task LiveSpanRejectsAConflictingMutableOwnerReceiverAsync()
    {
        var compilation = await CompileAsync("", EntryPrefix
            + "Span<Token> tokens = owner.AsSpan(); owner.Touch(); return 42;}", BoundRuntimePolicy.FailFast,
            (name, source) => name == "TokenSnapshot" ? source.Replace("public int32 GetCount()", """
                public mut void Touch() { }
                public int32 GetCount()
                """, StringComparison.Ordinal) : source);
        Assert.True(compilation.HasErrors);
        Assert.Null(compilation.Program);
        Assert.Contains(compilation.Diagnostics, static diagnostic => diagnostic.Code == "TRM3023");
    }

    [Fact]
    public async Task AJoinPreservesBothPossibleBackingOwnersAsync()
    {
        var compilation = await CompileAsync("", EntryPrefix + """
            require TokenSnapshot.TryScan(init var other);
            mut TokenCursor cursor = new TokenCursor(owner.AsSpan());
            if (Pick()) { cursor = _; cursor = new TokenCursor(other.AsSpan()); }
            owner = _;
            return 42;
        }
        """, BoundRuntimePolicy.FailFast);
        Assert.True(compilation.HasErrors);
        Assert.Null(compilation.Program);
        Assert.Contains(compilation.Diagnostics, static diagnostic => diagnostic.Code == "TRM3023");
    }

    [Theory]
    [InlineData("4294967296")]
    [InlineData("9223372036854775808")]
    [InlineData("0x1_0000_0000")]
    [InlineData("0x8000_0000_0000_0000")]
    [InlineData("uint64.MaxValue")]
    public async Task BoundsChecksDoNotNarrowUnsignedNativeWidthIndexesAsync(string index)
    {
        var entry = EntryPrefix + $$"""
            Span<Token> tokens = owner.AsSpan();
            unsafe {
                try { Token bad = tokens[{{index}}]; return 1; }
                catch { require tokens[0].Kind == 0; }
            }
            return 42;
        }
        """;
        await ExecuteAsync(Program(await CompileAsync("", entry, BoundRuntimePolicy.CheckedExceptions)),
            BoundRuntimePolicy.CheckedExceptions);
    }

    [Theory]
    [InlineData("")]
    [InlineData("alpha 42")]
    public async Task FirstLastAndExhaustedReadsUseTheFullDescriptorAsync(string text)
    {
        const string Entry = EntryPrefix + """
            Span<Token> tokens = owner.AsSpan();
            Token first = tokens[0];
            Token last = tokens[tokens.Length - 1];
            require first.Kind == Host.Tokens[0];
            require last.Kind == 0 && last.Range.Start == Host.Source.Length && last.Range.Length == 0;
            mut TokenCursor cursor = new TokenCursor(tokens);
            mut int32 count = 0;
            while (cursor.TryRead(out var token)) { count = count + 1; }
            require count == owner.GetCount();
            require !cursor.TryRead(out var firstExhausted) && !cursor.TryRead(out var secondExhausted);
            require tokens.Length == uint64(count);
            return 42;
        }
        """;
        foreach (var policy in new[] { BoundRuntimePolicy.FailFast, BoundRuntimePolicy.CheckedExceptions })
        {
            await ExecuteAsync(Program(await CompileAsync(text, Entry, policy)), policy);
        }
    }

    [Theory]
    [InlineData("0x1_0000_0000")]
    [InlineData("0x8000_0000_0000_0000")]
    [InlineData("uint64.MaxValue")]
    public async Task SuccessfulAndFailedIndexesAreEvaluatedOnceAsync(string oversized)
    {
        var entry = EntryPrefix + $$"""
            Span<Token> tokens = owner.AsSpan();
            Token first = tokens[Index(0)];
            require first.Kind == 2 && Host.Trace[0] == 1;
            Token last = tokens[Index(tokens.Length - 1)];
            require last.Kind == 0 && Host.Trace[0] == 2;
            unsafe {
                try { Token bad = tokens[Index({{oversized}})]; return 1; }
                catch { require Host.Trace[0] == 3; }
            }
            return 42;
        }
        uint64 Index(uint64 value) { Host.Trace[0] = Host.Trace[0] + 1; return value; }
        """;
        await ExecuteAsync(Program(await CompileAsync("alpha 42", entry, BoundRuntimePolicy.CheckedExceptions,
            trace: true)), BoundRuntimePolicy.CheckedExceptions);
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("-18446744073709551616")]
    [InlineData("18446744073709551616")]
    [InlineData("0x1_0000_0000_0000_0000")]
    public async Task NegativeAndOversizedIndexLiteralsAreRejectedAsync(string literal)
    {
        var compilation = await CompileAsync("", EntryPrefix
            + $"Span<Token> tokens = owner.AsSpan(); Token bad = tokens[{literal}]; return 42;}}",
            BoundRuntimePolicy.FailFast);
        Assert.True(compilation.HasErrors);
        Assert.Null(compilation.Program);
    }

    [Theory]
    [InlineData("int32", "-1")]
    [InlineData("int32", "-2147483648")]
    [InlineData("int64", "-1")]
    [InlineData("int64", "-9223372036854775808")]
    public async Task SignedConversionsAreCheckedBeforeElementAccessAsync(string type, string value)
    {
        var entry = EntryPrefix + $$"""
            Span<Token> tokens = owner.AsSpan();
            {{type}} signedIndex = {{value}};
            unsafe {
                try { Token bad = tokens[uint64(signedIndex)]; return 1; }
                catch { require tokens[0].Kind == 0; }
            }
            return 42;
        }
        """;
        await ExecuteAsync(Program(await CompileAsync("", entry, BoundRuntimePolicy.CheckedExceptions)),
            BoundRuntimePolicy.CheckedExceptions);
    }

    [Fact]
    public async Task NonnegativeSignedIndexesRequireAnExplicitCheckedCarrierConversionAsync()
    {
        var entry = EntryPrefix + """
            Span<Token> tokens = owner.AsSpan();
            int32 firstIndex = 0;
            int64 lastIndex = 2;
            require tokens[uint64(firstIndex)].Kind == 2;
            require tokens[uint64(lastIndex)].Kind == 0;
            return 42;
        }
        """;
        foreach (var policy in new[] { BoundRuntimePolicy.FailFast, BoundRuntimePolicy.CheckedExceptions })
        {
            await ExecuteAsync(Program(await CompileAsync("alpha 42", entry, policy)), policy);
        }
    }

    [Fact]
    public async Task BoundsChecksPrecedeScalingAndAddressFormationAsync()
    {
        var entry = EntryPrefix + """
            return Read(owner.AsSpan(), 0);
        }
        int32 Read(Span<Token> tokens, uint64 index) { Token token = tokens[index]; return 42; }
        """;
        var program = Program(await CompileAsync("", entry, BoundRuntimePolicy.FailFast));
        var code = NativeEmitter.EmitCode(program, NativeRuntimePolicy.FailFast);
        var body = SourceFunctionCode(code, program, "Read");
        var comparison = body.IndexOf(new byte[] { 0x48, 0x39, 0xC1, 0x0F, 0x82, 0x05, 0x00, 0x00, 0x00 });
        Assert.True(comparison >= 0);
        Assert.Equal(0xE8, body[comparison + 9]);
        var start = code.SourceFunctionOffsets[program.Functions.IndexOf(Assert.Single(program.Functions,
            static function => function.Name == "Read"))];
        var boundsTarget = start + comparison + 14
            + BinaryPrimitives.ReadInt32LittleEndian(body.Slice(comparison + 10, 4));
        Assert.Equal(code.Functions[4].Start, boundsTarget);
        Assert.True(body.Slice(comparison + 14).StartsWith(new byte[] {
            0x48, 0x89, 0xC8, 0xB9, 0x0C, 0x00, 0x00, 0x00, 0x48, 0xF7, 0xE1,
        }));
        Assert.True(body.IndexOf(new byte[] { 0x48, 0x01, 0xD0 }) > comparison + 22);
    }

    [Fact]
    public async Task ProjectionAndAdvanceInitializeBothFullDescriptorWordsAsync()
    {
        var entry = EntryPrefix + """
            mut TokenCursor cursor = new TokenCursor(owner.AsSpan());
            require cursor.TryRead(out var token);
            cursor = _;
            require Measure(move owner) == 1;
            return 42;
        }
        uint64 Measure(TokenSnapshot owner) { return owner.AsSpan().Length; }
        """;
        var program = Program(await CompileAsync("", entry, BoundRuntimePolicy.FailFast));
        var code = NativeEmitter.EmitCode(program, NativeRuntimePolicy.FailFast);
        var body = SourceFunctionCode(code, program, "Measure");
        var countMultiply = body.IndexOf(new byte[] { 0x48, 0x6B, 0xC0, 0x03 });
        Assert.True(countMultiply >= 14);
        var countLoad = body.IndexOf(new byte[] { 0x8B, 0x80, 0x08, 0x00, 0x00, 0x00 });
        Assert.True(countLoad >= 0 && countLoad < countMultiply - 14);
        Assert.True(body.Slice(countMultiply - 14, 3).SequenceEqual(new byte[] { 0x48, 0x89, 0x85 }));
        Assert.True(body.Slice(countMultiply - 7, 3).SequenceEqual(new byte[] { 0x48, 0x89, 0x85 }));
        var countSlot = BinaryPrimitives.ReadInt32LittleEndian(body.Slice(countMultiply - 11, 4));
        var dataStore = new byte[] { 0x48, 0x89, 0x85, 0, 0, 0, 0 };
        BinaryPrimitives.WriteInt32LittleEndian(dataStore.AsSpan(3), countSlot - 8);
        Assert.True(body.IndexOf(dataStore) >= 0);
        Assert.True(body.IndexOf(new byte[] { 0x48, 0x8B, 0x80, 0x08, 0x00, 0x00, 0x00 }) >= 0);

        var advance = SourceFunctionCode(code, program, "Terrum.CodeAnalysis.TokenCursor.TryRead");
        var decrement = advance.IndexOf(new byte[] { 0x48, 0x83, 0xE8, 0x01 });
        Assert.True(decrement >= 0);
        Assert.True(advance.Slice(decrement + 4, 3).SequenceEqual(new byte[] { 0x48, 0x89, 0x85 }));
        var advanceCountSlot = BinaryPrimitives.ReadInt32LittleEndian(advance.Slice(decrement + 7, 4));
        BinaryPrimitives.WriteInt32LittleEndian(dataStore.AsSpan(3), advanceCountSlot - 8);
        Assert.True(advance.IndexOf(dataStore) >= 0);

        var image = NativeEmitter.Emit(program, NativeRuntimePolicy.FailFast);
        var pe = BinaryPrimitives.ReadInt32LittleEndian(image.AsSpan(0x3C, 4));
        Assert.Equal(0x8664, BinaryPrimitives.ReadUInt16LittleEndian(image.AsSpan(pe + 4, 2)));
        Assert.Equal(0x20B, BinaryPrimitives.ReadUInt16LittleEndian(image.AsSpan(pe + 24, 2)));
        await ExecuteAsync(program, BoundRuntimePolicy.FailFast);
    }

    [Theory]
    [InlineData("[Captures(tokens)]")]
    [InlineData("[Captures(nameof(missing))]")]
    [InlineData("[Captures(other(tokens))]")]
    [InlineData("[Captures(nameof(tokens))] [Captures(nameof(tokens))]")]
    [InlineData("")]
    public async Task CapturesSelectorsAreCheckedAndRequiredAsync(string attribute)
    {
        var compilation = await CompileAsync("", EntryPrefix + "return 42;}", BoundRuntimePolicy.FailFast,
            (name, source) => name == "TokenCursor"
                ? source.Replace("[Captures(nameof(tokens))]", attribute, StringComparison.Ordinal) : source);
        Assert.True(compilation.HasErrors);
        Assert.Null(compilation.Program);
    }

    [Theory]
    [InlineData("Span<int32> tokens = owner.AsSpan(); return 42;")]
    [InlineData("write Span<Token> tokens = owner.AsSpan(); return 42;")]
    [InlineData("Span<Token>? tokens = owner.AsSpan(); return 42;")]
    [InlineData("Span<Token>?? tokens = owner.AsSpan(); return 42;")]
    [InlineData("mut Span<Token> tokens = owner.AsSpan(); return 42;")]
    [InlineData("nuint index = 0; return 42;")]
    [InlineData("nint index = 0; return 42;")]
    [InlineData("Span<Token> tokens = owner.AsSpan(); Token token = tokens[nuint(0)]; return 42;")]
    [InlineData("Span<Token> tokens = owner.AsSpan(); Token token = tokens[nint(0)]; return 42;")]
    [InlineData("Span<Token> tokens = owner.AsSpan(); tokens[0] = tokens[0]; return 42;")]
    [InlineData("Span<Token> tokens = owner.AsSpan(); Span<Token> tail = tokens.AdvanceOne(); return 42;")]
    [InlineData("Span<Token> tokens = owner.AsSpan(); Span<Token> tail = tokens[..]; return 42;")]
    [InlineData("Span<Token> tokens = new Span<Token>(); return 42;")]
    [InlineData("Span<Token> tokens = owner.AsSpan(); int32 index = 0; Token token = tokens[index]; return 42;")]
    [InlineData("Span<Token> tokens = owner.AsSpan(); int64 index = 0; Token token = tokens[index]; return 42;")]
    [InlineData("Span<Token> tokens = owner.AsSpan(); uint32 index = 0; Token token = tokens[index]; return 42;")]
    public async Task BroaderSpanAndNativeIntegerSurfacesRemainUnsupportedAsync(string body)
    {
        var compilation = await CompileAsync("", EntryPrefix + body + "}", BoundRuntimePolicy.FailFast);
        Assert.True(compilation.HasErrors);
        Assert.Null(compilation.Program);
    }

    [Theory]
    [InlineData("unique struct Stored { private Span<Token> Value; public Stored(Span<Token> value) { Value = value; } }")]
    [InlineData("class Stored { private Span<Token> Value; public Stored(Span<Token> value) { Value = value; } }")]
    [InlineData("Span<Token> Escape(Span<Token> value) { return value; }")]
    [InlineData("TokenCursor Escape(TokenCursor value) { return move value; }")]
    [InlineData("[Captures(nameof(value))] void Capture(Span<Token> value) { }")]
    public async Task StorageEscapesAndMisplacedCapturesRemainUnsupportedAsync(string declaration)
    {
        var compilation = await CompileAsync("", "using Terrum.CodeAnalysis; " + declaration
            + " int32 Main() { return 42; }", BoundRuntimePolicy.FailFast);
        Assert.True(compilation.HasErrors);
        Assert.Null(compilation.Program);
    }

    [Fact]
    public async Task NestedAttributeArgumentsRemainSpecificToCapturesAsync()
    {
        var compilation = await CompileAsync("", """
            using Terrum.CodeAnalysis;
            [NoThrow(nameof(tokens))] void Observe(Span<Token> tokens) { }
            int32 Main() { return 42; }
            """, BoundRuntimePolicy.FailFast);
        Assert.True(compilation.HasErrors);
        Assert.Null(compilation.Program);
    }

    [Fact]
    public async Task MandatoryVerifierRejectsRemovedCapturesAndForgedTokenLayoutAsync()
    {
        var program = Program(await CompileAsync("", EntryPrefix
            + "mut TokenCursor cursor = new TokenCursor(owner.AsSpan()); return 42;}", BoundRuntimePolicy.FailFast));
        var constructor = program.Functions.IndexOf(Assert.Single(program.Functions,
            static function => function.Name == "Terrum.CodeAnalysis.TokenCursor.#ctor"));
        Reject(program with {
            Functions = program.Functions.SetItem(constructor, program.Functions[constructor] with { CapturedParameters = [] }),
        });
        Reject(program with {
            Functions = program.Functions.SetItem(constructor,
                program.Functions[constructor] with { CapturedParameters = [0] }),
        });
        var token = program.Structures.IndexOf(Assert.Single(program.Structures,
            static structure => structure.Name == "Terrum.CodeAnalysis.Token"));
        var layout = program.Structures[token];
        Reject(program with {
            Structures = program.Structures.SetItem(token, layout with {
                Fields = layout.Fields.SetItem(1, layout.Fields[1] with { Offset = 8 }),
            }),
        });
    }

    [Fact]
    public async Task MandatoryVerifierRejectsForgedProjectionProvenanceAsync()
    {
        var program = Program(await CompileAsync("", EntryPrefix
            + "Span<Token> tokens = owner.AsSpan(); return 42;}", BoundRuntimePolicy.FailFast));
        var entry = program.Functions[program.EntryPoint];
        BoundStatement Rewrite(BoundStatement statement) => statement switch {
            BoundBlock block => block with { Statements = [.. block.Statements.Select(Rewrite)] },
            BoundTryFinally region => region with { Body = (BoundBlock)Rewrite(region.Body) },
            BoundLocal { Initializer: BoundSnapshotSpan snapshot } local => local with {
                Initializer = snapshot with { SourceBuffer = 1 },
            },
            _ => statement,
        };
        Reject(program with {
            Functions = program.Functions.SetItem(program.EntryPoint, entry with { Body = (BoundBlock)Rewrite(entry.Body) }),
        });
    }

    [Fact]
    public async Task MandatoryVerifierRejectsForgedReleaseAndIndexWidthAsync()
    {
        var program = Program(await CompileAsync("", EntryPrefix + """
            Span<Token> tokens = owner.AsSpan();
            Token token = tokens[0];
            tokens = _;
            owner = _;
            return 42;
        }
        """, BoundRuntimePolicy.FailFast));
        var entry = program.Functions[program.EntryPoint];

        BoundStatement Rewrite(BoundStatement statement, bool eraseEnd, PrimitiveType indexType)
        {
            return statement switch {
                BoundBlock block => block with { Statements = [.. block.Statements.Select(child => Rewrite(child, eraseEnd, indexType))] },
                BoundTryFinally region => region with {
                    Body = (BoundBlock)Rewrite(region.Body, eraseEnd, indexType),
                    Finally = (BoundBlock)Rewrite(region.Finally, eraseEnd, indexType),
                },
                BoundLifetimeEnd end when eraseEnd && entry.Variables[end.Variable].Type == PrimitiveType.TokenSpan
                    => new BoundBlock([], end.Span),
                BoundLocal { Initializer: BoundSpanRead read } local when !eraseEnd
                    => local with { Initializer = read with { Index = new BoundConstant(0, indexType, read.Span) } },
                _ => statement,
            };
        }

        foreach (var indexType in new[] { PrimitiveType.UInt32, PrimitiveType.Int32, PrimitiveType.Int64, PrimitiveType.Float64 })
        {
            Reject(program with {
                Functions = program.Functions.SetItem(program.EntryPoint,
                    entry with { Body = (BoundBlock)Rewrite(entry.Body, false, indexType) }),
            });
        }
        Reject(program with {
            Functions = program.Functions.SetItem(program.EntryPoint,
                entry with { Body = (BoundBlock)Rewrite(entry.Body, true, PrimitiveType.UInt64) }),
        });
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task MandatoryVerifierRejectsForgedProjectionAndLengthResultTypesAsync(bool projection)
    {
        var program = Program(await CompileAsync("", EntryPrefix
            + "uint64 count = owner.AsSpan().Length; return 42;}", BoundRuntimePolicy.FailFast));
        var entry = program.Functions[program.EntryPoint];
        var variable = -1;
        var forgedType = projection ? PrimitiveType.UInt64 : PrimitiveType.Int32;
        BoundStatement Rewrite(BoundStatement statement)
        {
            if (statement is BoundLocal { Initializer: BoundSpanLength { View: BoundSnapshotSpan snapshot } length } local)
            {
                Assert.Equal(-1, variable);
                variable = local.Variable;
                BoundExpression initializer = projection
                    ? snapshot with { Type = forgedType } : length with { Type = forgedType };
                return local with { Initializer = initializer };
            }
            return statement switch {
                BoundBlock block => block with { Statements = [.. block.Statements.Select(Rewrite)] },
                BoundTryFinally region => region with { Body = (BoundBlock)Rewrite(region.Body) },
                _ => statement,
            };
        }
        var body = (BoundBlock)Rewrite(entry.Body);
        Assert.InRange(variable, entry.ParameterCount, entry.Variables.Length - 1);
        Reject(program with {
            Functions = program.Functions.SetItem(program.EntryPoint, entry with {
                Body = body,
                Variables = entry.Variables.SetItem(variable, entry.Variables[variable] with { Type = forgedType }),
            }),
        });
    }

    [Theory]
    [InlineData(false, BoundRuntimePolicy.FailFast)]
    [InlineData(true, BoundRuntimePolicy.FailFast)]
    [InlineData(false, BoundRuntimePolicy.CheckedExceptions)]
    [InlineData(true, BoundRuntimePolicy.CheckedExceptions)]
    public async Task MandatoryVerifierRejectsOwnerCleanupBeforeItsCapturedCursorAsync(bool lifetimeEnd, BoundRuntimePolicy policy)
    {
        var program = Program(await CompileAsync("", EntryPrefix
            + "TokenCursor cursor = new TokenCursor(owner.AsSpan()); return 42;}", policy));
        var entry = program.Functions[program.EntryPoint];
        var owner = entry.Variables.IndexOf(Assert.Single(entry.Variables, static variable => variable.Name == "owner"));
        var cursor = entry.Variables.IndexOf(Assert.Single(entry.Variables, static variable => variable.Name == "cursor"));
        BoundDrop? ownerCleanup = null;
        var replacements = 0;
        BoundStatement Rewrite(BoundStatement statement)
        {
            if (statement is BoundTryFinally region)
            {
                if (region.Finally.Statements is [BoundDrop ownerDrop] && ownerDrop.Variable == owner)
                {
                    Assert.Null(ownerCleanup);
                    ownerCleanup = ownerDrop;
                }
                var body = (BoundBlock)Rewrite(region.Body);
                if (region.Finally.Statements is [BoundDrop cursorDrop] && cursorDrop.Variable == cursor)
                {
                    var backingCleanup = Assert.IsType<BoundDrop>(ownerCleanup);
                    BoundStatement invalidation = lifetimeEnd
                        ? new BoundLifetimeEnd(owner, backingCleanup.CleanupFunction, cursorDrop.Span)
                        : backingCleanup with { Span = cursorDrop.Span };
                    replacements++;
                    return region with {
                        Body = body,
                        Finally = region.Finally with { Statements = [invalidation, cursorDrop] },
                    };
                }
                return region with { Body = body };
            }
            return statement switch {
                BoundBlock block => block with { Statements = [.. block.Statements.Select(Rewrite)] },
                _ => statement,
            };
        }
        var forgedBody = (BoundBlock)Rewrite(entry.Body);
        Assert.Equal(1, replacements);
        Reject(program with {
            Functions = program.Functions.SetItem(program.EntryPoint, entry with { Body = forgedBody }),
        });
    }

    [Fact]
    public async Task MandatoryVerifierRejectsForgedAdvanceResultTypeAsync()
    {
        var program = Program(await CompileAsync("", EntryPrefix
            + "mut TokenCursor cursor = new TokenCursor(owner.AsSpan()); return 42;}", BoundRuntimePolicy.FailFast));
        var method = Assert.Single(program.Functions,
            static function => function.Name == "Terrum.CodeAnalysis.TokenCursor.TryRead");
        var function = program.Functions.IndexOf(method);
        var variable = method.Variables.Length;
        var replacements = 0;
        BoundStatement Rewrite(BoundStatement statement)
        {
            if (statement is BoundFieldAssignment { Value: BoundSpanAdvance advance } assignment)
            {
                replacements++;
                return new BoundLocal(variable, advance with { Type = PrimitiveType.UInt64 }, assignment.Span);
            }
            return statement switch {
                BoundBlock block => block with { Statements = [.. block.Statements.Select(Rewrite)] },
                BoundIf conditional => conditional with {
                    Then = (BoundBlock)Rewrite(conditional.Then),
                    Else = conditional.Else is null ? null : (BoundBlock)Rewrite(conditional.Else),
                },
                BoundTryFinally region => region with { Body = (BoundBlock)Rewrite(region.Body) },
                _ => statement,
            };
        }
        var body = (BoundBlock)Rewrite(method.Body);
        Assert.Equal(1, replacements);
        Reject(program with {
            Functions = program.Functions.SetItem(function, method with {
                Body = body,
                Variables = method.Variables.Add(new BoundVariable("forgedAdvance", PrimitiveType.UInt64, false, body.Span)),
            }),
        });
    }

    [Theory]
    [InlineData(nameof(BoundSnapshotSpan))]
    [InlineData(nameof(BoundSpanAdvance))]
    public void SpanNodeKindsCannotHidePersistenceMetadataBehindForgedResultTypes(string kind)
    {
        var program = Program(Compilation.CreateLibrary([SyntaxTree.Parse("public uint64 Probe() { return 1; }")]));
        Assert.Empty(program.Structures);
        var function = Assert.Single(program.Functions);
        Assert.Empty(function.Variables);
        Assert.True(function.CapturedParameters.IsDefaultOrEmpty);
        var span = function.Body.Span;
        BoundExpression expression = kind switch {
            nameof(BoundSnapshotSpan) => new BoundSnapshotSpan(0, 0, span) with { Type = PrimitiveType.UInt64 },
            nameof(BoundSpanAdvance) => new BoundSpanAdvance(new BoundConstant(0, PrimitiveType.UInt64, span), span)
                with { Type = PrimitiveType.UInt64 },
            _ => throw new ArgumentException(nameof(kind)),
        };
        var forged = program with {
            Functions = program.Functions.SetItem(0, function with {
                Body = new BoundBlock([new BoundReturn(expression, span)], span),
            }),
        };
        var error = Assert.Throws<ModuleContractException>(() => ModuleCompiler.CreateProducer(forged, ["Probe"]));
        Assert.Equal("Compilation-local span and capture metadata cannot be persisted.", error.Message);
    }

    [Fact]
    public async Task MandatoryVerifierRejectsAStagedOwnerMoveAfterTheCursorMovesAsync()
    {
        var program = Program(await CompileAsync("", EntryPrefix + """
            mut TokenCursor cursor = new TokenCursor(owner.AsSpan());
            return Consume(move cursor, true);
        }
        """, BoundRuntimePolicy.FailFast));
        var entry = program.Functions[program.EntryPoint];
        var owner = entry.Variables.IndexOf(Assert.Single(entry.Variables, static variable => variable.Name == "owner"));
        var replace = program.Functions.IndexOf(Assert.Single(program.Functions, static function => function.Name == "Replace"));
        BoundStatement Rewrite(BoundStatement statement) => statement switch {
            BoundBlock block => block with { Statements = [.. block.Statements.Select(Rewrite)] },
            BoundTryFinally region => region with { Body = (BoundBlock)Rewrite(region.Body) },
            BoundReturn { Value: BoundCall call } result => result with {
                Value = call with { Arguments = call.Arguments.SetItem(1,
                    new BoundCall(replace, [new BoundOutArgument(owner, false, entry.Variables[owner].Type, call.Span)],
                        PrimitiveType.Bool, call.Span)) },
            },
            _ => statement,
        };
        Reject(program with {
            Functions = program.Functions.SetItem(program.EntryPoint, entry with { Body = (BoundBlock)Rewrite(entry.Body) }),
        });
    }

    [Fact]
    public async Task ConstructionFailureAndArgumentUnwindDoNotTransferOwnerCleanupAsync()
    {
        const string Entry = EntryPrefix + """
            unsafe {
                try { mut TokenCursor cursor = new TokenCursor(owner.AsSpan()); return 1; }
                catch { require Host.Trace[0] == 0; }
            }
            owner = _;
            require Host.Trace[0] == 1;
            return 42;
        }
        """;
        var program = Program(await CompileAsync("", Entry, BoundRuntimePolicy.CheckedExceptions, (name, source) => name switch {
            "TokenCursor" => source.Replace("_tokens = tokens;", "_tokens = tokens; require false;", StringComparison.Ordinal),
            "TokenSnapshot" => source.Replace("public int32 GetCount()", """
                [NoThrow] [Cleanup] public void Close() { Host.Trace[0] = Host.Trace[0] ^ 1; }
                public int32 GetCount()
                """, StringComparison.Ordinal),
            _ => source,
        }, trace: true));
        await ExecuteAsync(program, BoundRuntimePolicy.CheckedExceptions);

        const string Staged = """
            using Terrum.CodeAnalysis;
            bool Fail() { require false; return true; }
            int32 Consume(TokenCursor cursor, bool ignored) { return 1; }
            int32 Main() {
                require TokenSnapshot.TryScan(init var owner);
                mut TokenCursor cursor = new TokenCursor(owner.AsSpan());
                unsafe { try { return Consume(move cursor, Fail()); } catch { require Host.Trace[0] == 0; } }
                owner = _;
                require Host.Trace[0] == 1;
                return 42;
            }
            """;
        var staged = Program(await CompileAsync("", Staged, BoundRuntimePolicy.CheckedExceptions,
            (name, source) => name == "TokenSnapshot" ? source.Replace("public int32 GetCount()", """
                [NoThrow] [Cleanup] public void Close() { Host.Trace[0] = Host.Trace[0] ^ 1; }
                public int32 GetCount()
                """, StringComparison.Ordinal) : source, trace: true));
        await ExecuteAsync(staged, BoundRuntimePolicy.CheckedExceptions);
    }

    [Theory]
    [InlineData(0, "")]
    [InlineData(2, "require records.TryAppend(0); require records.TryAppend(0); require records.TryAppend(0);")]
    [InlineData(1, "require records.TryAppend(0); require records.TryAppend(1); require records.TryAppend(0);")]
    [InlineData(1, "require records.TryAppend(107); require records.TryAppend(0); require records.TryAppend(0);")]
    [InlineData(2147483647, "")]
    public async Task SnapshotFactoryValidatesBeforePublishingAsync(int count, string append)
    {
        var entry = $$"""
            using Terrum.CodeAnalysis;
            int32 Main() {
                require OwnedInt32Buffer.TryCreate(3, out var raw);
                mut OwnedInt32Buffer records = move raw;
                {{append}}
                unsafe {
                    try {
                        require TokenSnapshot.TryCreateValidated(move records, {{count}}, init var bad);
                        return 1;
                    } catch { }
                }
                return 42;
            }
            """;
        await ExecuteAsync(Program(await CompileAsync("", entry, BoundRuntimePolicy.CheckedExceptions)),
            BoundRuntimePolicy.CheckedExceptions);
    }

    [Theory]
    [InlineData(0, "")]
    [InlineData(1, "")]
    [InlineData(2, "0,0,0")]
    [InlineData(1, "0,0,0")]
    [InlineData(2, "107,0,1,0,1,0")]
    [InlineData(2, "2,0,2,0,1,0")]
    [InlineData(2, "2,0,1,0,0,0")]
    [InlineData(2, "2,0,0,0,1,0")]
    public async Task ProjectionIndependentlyChecksRecordsBeforeCursorPublicationAsync(int count, string words)
    {
        var append = string.Join(Environment.NewLine, words.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(static word => $"require records.TryAppend({word});"));
        var entry = $$"""
            using Terrum.CodeAnalysis;
            TokenSnapshot Build(OwnedInt32Buffer records) {
                require TokenSnapshot.TryCreateValidated(move records, {{count}}, init var result);
                return move result;
            }
            int32 Main() {
                require OwnedInt32Buffer.TryCreate(6, out var raw);
                mut OwnedInt32Buffer records = move raw;
                {{append}}
                unsafe {
                    try {
                        TokenSnapshot owner = Build(move records);
                        mut TokenCursor cursor = new TokenCursor(owner.AsSpan());
                        return 1;
                    }
                    catch { require Host.Trace[0] == 1; }
                }
                return 42;
            }
            """;
        var program = Program(await CompileAsync("a", entry, BoundRuntimePolicy.CheckedExceptions,
            (name, source) => {
                if (name != "TokenSnapshot")
                {
                    return source;
                }
                var start = source.IndexOf("    internal static bool TryCreateValidated(", StringComparison.Ordinal);
                var end = source.IndexOf("    public int32 GetCount()", start, StringComparison.Ordinal);
                Assert.True(start >= 0 && end > start);
                var uncheckedFactory = """
                    internal static bool TryCreateValidated(OwnedInt32Buffer records, int32 count,
                        init ref<TokenSnapshot> result)
                    {
                        result = new TokenSnapshot(move records, count);
                        return true;
                    }

                    [NoThrow] [Cleanup] public void Close() { Host.Trace[0] = Host.Trace[0] ^ 1; }
                    """;
                return source.Remove(start, end - start).Insert(start, uncheckedFactory);
            }, trace: true));
        await ExecuteAsync(program, BoundRuntimePolicy.CheckedExceptions);
    }

    [Fact]
    public async Task SpanAndCaptureMetadataCannotCrossPersistenceOrExportBoundariesAsync()
    {
        var program = Program(await CompileAsync("", EntryPrefix
            + "Span<Token> tokens = owner.AsSpan(); return 42;}", BoundRuntimePolicy.FailFast));
        _ = Assert.Throws<ModuleContractException>(() => ModuleCompiler.CreateProducer(program, ["Observe"]));
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
        var trees = new List<SyntaxTree>();
        foreach (var name in new[] { "SourceRange", "Token", "TokenCursor" })
        {
            var path = Path.Combine(root, "self-host", name + ".terrum");
            trees.Add(SyntaxTree.Parse(await File.ReadAllTextAsync(path).ConfigureAwait(true), path));
        }
        trees.Add(SyntaxTree.Parse("""
            using Terrum.CodeAnalysis;
            public int32 Observe(Span<Token> tokens) { return 42; }
            """));
        var library = Program(Compilation.CreateLibrary(trees));
        _ = Assert.Throws<ModuleContractException>(() => ModuleCompiler.CreateProducer(library, ["Observe"]));
        _ = Assert.Throws<ArgumentException>(() => NativeEmitter.EmitLibrary(library, ["Observe"], NativeRuntimePolicy.FailFast));
    }

    private static BoundProgram Program(Compilation compilation)
    {
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        return Assert.IsType<BoundProgram>(compilation.Program);
    }

    private static ReadOnlySpan<byte> SourceFunctionCode(NativeCode code, BoundProgram program, string name)
    {
        var index = program.Functions.IndexOf(Assert.Single(program.Functions, function => function.Name == name));
        var start = code.SourceFunctionOffsets[index];
        var range = Assert.Single(code.Functions, function => function.Start == start);
        return code.Code.AsSpan(range.Start, range.End - range.Start);
    }

    private static void Reject(BoundProgram program)
    {
        _ = Assert.Throws<ArgumentException>(() => SemanticVerifier.Verify(program));
        _ = Assert.Throws<ArgumentException>(() => NativeEmitter.EmitCode(program, NativeRuntimePolicy.FailFast));
    }

    private static async Task<Compilation> CompileAsync(string text, string entry, BoundRuntimePolicy policy,
        Func<string, string, string>? rewrite = null, bool trace = false)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
        string[] names = ["SourceRange", "CharacterFacts", "Utf16CodeUnits", "Utf16Scalar", "CharacterLiteral", "StringEscape", "Token",
            "ManagedTokenChain", "Scanner", "ManagedScanner", "TokenSnapshot", "TokenTriviaSnapshot",
            "TokenIntegerSnapshot", "TokenDiagnosticSnapshot", "TokenCursor"];
        var trees = new List<SyntaxTree>(names.Length + 1);
        foreach (var name in names)
        {
            var path = Path.Combine(root, "self-host", name + ".terrum");
            var source = await File.ReadAllTextAsync(path).ConfigureAwait(true);
            trees.Add(SyntaxTree.Parse(rewrite is null ? source : rewrite(name, source), path));
        }
        trees.Add(SyntaxTree.Parse(entry, "token-cursor-entry.terrum"));
        var buffers = ScannerHost.CreateBuffers(SourceText.From(text));
        if (trace)
        {
            buffers = buffers.Add(new HostBuffer("Trace", PrimitiveType.Int32, [0], true));
        }
        return Compilation.Create(trees, buffers, policy);
    }

    private static async Task ExecuteAsync(BoundProgram program, BoundRuntimePolicy policy)
    {
        var nativePolicy = (NativeRuntimePolicy)policy;
        Assert.Equal(42, NativeJit.Execute(program, nativePolicy));
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
        var directory = Path.Combine(root, "artifacts", "scanner-tests");
        _ = Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"terrum-token-cursor-{Guid.NewGuid():N}.exe");
        try
        {
            await File.WriteAllBytesAsync(path, NativeEmitter.Emit(program, nativePolicy)).ConfigureAwait(true);
            using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) {
                UseShellExecute = false,
                CreateNoWindow = true,
            }) ?? throw new InvalidOperationException("Could not start compiled token cursor.");
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(true);
            Assert.Equal(42, process.ExitCode);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
