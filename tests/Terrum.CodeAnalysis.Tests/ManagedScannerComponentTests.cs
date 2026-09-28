// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Lexing;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Terrum.CodeAnalysis.Text;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed class ManagedScannerComponentTests
{
    [Theory]
    [InlineData("", false)]
    [InlineData("// trivia\r\nalpha 42", false)]
    [InlineData("\U00010400 42", false)]
    [InlineData("\uD800", true)]
    [InlineData("a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a", true)]
    public async Task PublishedTokensMatchHostAndFailedScansDoNotPublishAsync(string text, bool fails)
    {
        var lexical = Lexer.Lex(SourceText.From(text));
        var expected = lexical.Tokens.ToArray();
        Assert.Equal(lexical.Diagnostics.Length != 0 || expected.Length > 64, fails);
        var source = new StringBuilder("""
            using Terrum.CodeAnalysis;
            int32 Main() {
                if (!TryScanManaged(init var chain)) {
                    return EXPECTED_FAILURE;
                }
            """.Replace("EXPECTED_FAILURE", fails ? "42" : "1", StringComparison.Ordinal));
        if (fails)
        {
            _ = source.Append("return 2; }");
        }
        else
        {
            _ = source.Append(CultureInfo.InvariantCulture, $"if (chain.Count != {expected.Length}) {{ return 3; }}");
            for (var index = 0; index < expected.Length; index++)
            {
                _ = source.Append(CultureInfo.InvariantCulture,
                    $"if (!chain.TryGet({index}, out var token{index}) || token{index}.Kind != {(int)expected[index].Kind} || token{index}.Range.Start != {expected[index].Span.Start} || token{index}.Range.Length != {expected[index].Span.Length}) {{ return 4; }}");
            }
            _ = source.Append(CultureInfo.InvariantCulture,
                $"if (chain.TryGetInit({expected.Length}, init var copied)) {{ return 9; }}"
                + $" require chain.TryGetInit(0, init copied);"
                + $" if (copied.Kind != {(int)expected[0].Kind} || copied.Range.Start != {expected[0].Span.Start}) {{ return 10; }}");
            _ = source.Append(CultureInfo.InvariantCulture,
                $"if (!chain.TryClear() || ManagedHeap.Collect() != {expected.Length}) {{ return 5; }}");
            for (var index = 0; index < expected.Length; index++)
            {
                _ = source.Append(CultureInfo.InvariantCulture,
                    $"if (token{index}.Kind != {(int)expected[index].Kind} || token{index}.Range.Start != {expected[index].Span.Start}) {{ return 6; }}");
            }
            _ = source.Append(CultureInfo.InvariantCulture,
                $"if (copied.Kind != {(int)expected[0].Kind} || copied.Range.Start != {expected[0].Span.Start}) {{ return 11; }}");
            _ = source.Append(CultureInfo.InvariantCulture,
                $"if (!TryScanManaged(init var again) || again.Count != {expected.Length}) {{ return 7; }}");
            _ = source.Append(CultureInfo.InvariantCulture,
                $"if (!again.TryClear() || ManagedHeap.Collect() != {expected.Length}) {{ return 8; }}");
            _ = source.Append("return 42; }");
        }

        foreach (var policy in new[] { NativeRuntimePolicy.FailFast, NativeRuntimePolicy.CheckedExceptions })
        {
            var program = await CompileAsync(text, source.ToString(), (BoundRuntimePolicy)policy).ConfigureAwait(true);
            Assert.Equal(42, NativeJit.Execute(program, policy));
            await ExecuteAotAsync(program, policy).ConfigureAwait(true);
        }
    }

    [Theory]
    [InlineData(63, false)]
    [InlineData(64, true)]
    public Task CapacityCountsTheEndOfFileTokenAsync(int identifiers, bool fails) =>
        PublishedTokensMatchHostAndFailedScansDoNotPublishAsync(
            string.Join(" ", Enumerable.Repeat("a", identifiers)), fails);

    [Theory]
    [InlineData(NativeRuntimePolicy.FailFast)]
    [InlineData(NativeRuntimePolicy.CheckedExceptions)]
    public async Task OwnedTokenSnapshotSupportsIndexedReadsBeyondManagedChainLimitAsync(
        NativeRuntimePolicy policy)
    {
        const string Entry = """
            using Terrum.CodeAnalysis;
            int32 Main() {
                require TokenSnapshot.TryScan(init var tokens);
                if (tokens.GetCount() != 129 || tokens.TryGet(-1, out var negative)
                    || tokens.TryGet(129, out var beyond)) { return 1; }
                mut int32 index = 0;
                while (index < 128) {
                    require tokens.TryGetInit(index, init var token);
                    if (token.Kind != 2 || token.Range.Start != index * 2
                        || token.Range.Length != 1) { return 2; }
                    index = index + 1;
                }
                require tokens.TryGet(128, out var eof);
                if (eof.Kind != 0 || eof.Range.Start != 255
                    || eof.Range.Length != 0) { return 3; }
                return 42;
            }
            """;
        var input = string.Join(" ", Enumerable.Repeat("a", 128));
        var program = await CompileAsync(input, Entry, (BoundRuntimePolicy)policy).ConfigureAwait(true);
        Assert.Equal(42, NativeJit.Execute(program, policy));
        await ExecuteAotAsync(program, policy).ConfigureAwait(true);
    }

    [Theory]
    [InlineData(NativeRuntimePolicy.FailFast)]
    [InlineData(NativeRuntimePolicy.CheckedExceptions)]
    public async Task TokenSnapshotOwnsValidatedRecordsAndRejectsPartialCopiesAsync(
        NativeRuntimePolicy policy)
    {
        const string Entry = """
            using Terrum.CodeAnalysis;
            int32 Main() {
                int32 count = Scan();
                if (TokenSnapshot.TryBuild(int32.MaxValue, init var oversized)) { return 4; }
                require TokenSnapshot.TryBuild(count, init var snapshot);
                Host.Tokens[0] = -1;
                require snapshot.TryGet(0, out var first);
                if (first.Kind != 2 || first.Range.Start != 0) { return 1; }
                Host.Tokens[0] = 2;
                Host.Tokens[3] = -1;
                if (TokenSnapshot.TryBuild(count, init var invalid)) { return 2; }
                if (!snapshot.TryGet(1, out var second) || second.Kind != 2
                    || second.Range.Start != 6) { return 3; }
                return 42;
            }
            """;
        var program = await CompileAsync("alpha beta", Entry, (BoundRuntimePolicy)policy).ConfigureAwait(true);
        Assert.Equal(42, NativeJit.Execute(program, policy));
        await ExecuteAotAsync(program, policy).ConfigureAwait(true);
    }

    [Theory]
    [InlineData(NativeRuntimePolicy.FailFast)]
    [InlineData(NativeRuntimePolicy.CheckedExceptions)]
    public async Task LexicalFailureLeavesOwnedTokenSnapshotVacantAsync(NativeRuntimePolicy policy)
    {
        const string Entry = """
            using Terrum.CodeAnalysis;
            int32 Main() {
                if (TokenSnapshot.TryScan(init var tokens)) { return 1; }
                return 42;
            }
            """;
        var program = await CompileAsync("\uD800", Entry, (BoundRuntimePolicy)policy).ConfigureAwait(true);
        Assert.Equal(42, NativeJit.Execute(program, policy));
        await ExecuteAotAsync(program, policy).ConfigureAwait(true);
    }

    [Theory]
    [InlineData("int32 Answer() { return 42; }", true)]
    [InlineData("int32 Answer() { return -42; }", true)]
    [InlineData("int32 Answer() { return (42); }", false)]
    [InlineData("int32 Answer() { return true; }", false)]
    [InlineData("int32 Answer() { return 42 }", false)]
    public async Task BoundedScalarParserAgreesWithHostedSyntaxForItsAcceptedShapeAsync(
        string text, bool accepted)
    {
        Assert.Equal(0, (int)TokenKind.EndOfFile);
        Assert.Equal(2, (int)TokenKind.Identifier);
        Assert.Equal(3, (int)TokenKind.NumericLiteral);
        Assert.Equal(19, (int)TokenKind.ReturnKeyword);
        Assert.Equal(46, (int)TokenKind.OpenParen);
        Assert.Equal(47, (int)TokenKind.CloseParen);
        Assert.Equal(48, (int)TokenKind.OpenBrace);
        Assert.Equal(49, (int)TokenKind.CloseBrace);
        Assert.Equal(52, (int)TokenKind.Semicolon);
        Assert.Equal(58, (int)TokenKind.Minus);

        var hosted = SyntaxTree.Parse(text);
        var name = hosted.Tokens[1].Span;
        var result = hosted.Root.DescendantNodes().Single(node => node.Kind == SyntaxKind.ReturnStatement);
        var expression = ((SyntaxNode)result.Children[1]).Span;
        var assertion = accepted
            ? FormattableString.Invariant($$"""
                require TryParseScalarReturnFunction(move tokens, out var name, out var value);
                if (name.Start != {{name.Start}} || name.Length != {{name.Length}}
                    || value.Start != {{expression.Start}} || value.Length != {{expression.Length}}) { return 1; }
                """)
            : "if (TryParseScalarReturnFunction(move tokens, out var name, out var value)) { return 1; }";
        var entry = $$"""
            using Terrum.CodeAnalysis;
            int32 Main() {
                require TokenSnapshot.TryScan(init var tokens);
                {{assertion}}
                return 42;
            }
            """;
        foreach (var policy in new[] { NativeRuntimePolicy.FailFast, NativeRuntimePolicy.CheckedExceptions })
        {
            var program = await CompileAsync(text, entry, (BoundRuntimePolicy)policy).ConfigureAwait(true);
            Assert.Equal(42, NativeJit.Execute(program, policy));
            await ExecuteAotAsync(program, policy).ConfigureAwait(true);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("alpha 42 // trailing\r\n\uD801\uDC00")]
    public async Task CursorReadsPublishedTokensAndStopsAtEndAsync(string text)
    {
        var expected = Lexer.Lex(SourceText.From(text)).Tokens.ToArray();
        var source = new StringBuilder("""
            using Terrum.CodeAnalysis;
            int32 Main() {
                require TryScanManagedCursor(init var cursor);
                require cursor.IsCurrent;
            """);
        for (var index = 0; index < expected.Length; index++)
        {
            var method = index % 2 == 0 ? "TryReadInit" : "TryRead";
            var mode = index % 2 == 0 ? "init" : "out";
            if (index == 0 || index == expected.Length / 2 || index == expected.Length - 1)
            {
                _ = source.Append(CultureInfo.InvariantCulture,
                    $"if (!cursor.TryPeek(out var peek{index}) || !cursor.TryPeekInit(init var repeated{index}) || peek{index}.Kind != {(int)expected[index].Kind} || repeated{index}.Range.Start != {expected[index].Span.Start}) {{ return 4; }}");
            }
            _ = source.Append(CultureInfo.InvariantCulture,
                $"if (!cursor.{method}({mode} var token{index}) || token{index}.Kind != {(int)expected[index].Kind} || token{index}.Range.Start != {expected[index].Span.Start} || token{index}.Range.Length != {expected[index].Span.Length}) {{ return 1; }}");
        }
        _ = source.Append("""
                if (cursor.TryPeek(out var pastPeek) || cursor.TryPeekInit(init var againPeek)
                    || cursor.TryRead(out var pastEnd) || !cursor.IsCurrent) { return 2; }
                if (cursor.TryReadInit(init var again)) { return 3; }
                return 42;
            }
            """);

        foreach (var policy in new[] { NativeRuntimePolicy.FailFast, NativeRuntimePolicy.CheckedExceptions })
        {
            var program = await CompileAsync(text, source.ToString(), (BoundRuntimePolicy)policy).ConfigureAwait(true);
            Assert.Equal(42, NativeJit.Execute(program, policy));
            await ExecuteAotAsync(program, policy).ConfigureAwait(true);
        }
    }

    [Fact]
    public Task CursorReadsFullCapacityAsync() =>
        CursorReadsPublishedTokensAndStopsAtEndAsync(string.Join(" ", Enumerable.Repeat("a", 63)));

    [Theory]
    [InlineData(NativeRuntimePolicy.FailFast)]
    [InlineData(NativeRuntimePolicy.CheckedExceptions)]
    public async Task CursorLookaheadDoesNotConsumeAndRejectsInvalidDistancesAsync(
        NativeRuntimePolicy policy)
    {
        const string Entry = """
            using Terrum.CodeAnalysis;
            int32 Main() {
                require ManagedTokenChain.TryCreate(out var chain);
                require chain.TryAppend(7, 0, 1);
                require chain.TryAppend(8, 1, 1);
                require chain.TryAppend(9, 2, 1);
                require chain.TryCreateCursor(out var cursor);
                if (cursor.TryPeekAhead(-1, out var negative)
                    || cursor.TryPeekAhead(3, out var outside)
                    || cursor.TryPeekAheadInit(3, init var beyond)) { return 1; }
                require cursor.TryPeekAhead(2, out var last) && last.Kind == 9;
                require cursor.TryPeekAheadInit(1, init var middle) && middle.Kind == 8;
                require cursor.TryRead(out var first) && first.Kind == 7;
                require cursor.TryPeekAhead(1, out var remaining) && remaining.Kind == 9;
                require cursor.TryRead(out var second) && second.Kind == 8;
                require cursor.TryRead(out var third) && third.Kind == 9;
                if (cursor.TryPeekAhead(0, out var afterEnd) || !cursor.IsCurrent) { return 2; }
                require chain.TryCreateCursor(out var stale);
                require chain.TryClear();
                if (stale.TryPeekAhead(1, out var invalidated) || stale.IsCurrent) { return 3; }
                return 42;
            }
            """;
        var program = await CompileAsync("abc", Entry, (BoundRuntimePolicy)policy).ConfigureAwait(true);
        Assert.Equal(42, NativeJit.Execute(program, policy));
        await ExecuteAotAsync(program, policy).ConfigureAwait(true);
    }

    [Theory]
    [InlineData(NativeRuntimePolicy.FailFast)]
    [InlineData(NativeRuntimePolicy.CheckedExceptions)]
    public async Task CursorKindMatchConsumesOnlyTheSelectedTokenAsync(NativeRuntimePolicy policy)
    {
        const string Entry = """
            using Terrum.CodeAnalysis;
            int32 Main() {
                require ManagedTokenChain.TryCreate(out var chain);
                require chain.TryAppend(7, 0, 1);
                require chain.TryAppend(8, 1, 1);
                require chain.TryCreateCursor(out var cursor);
                if (cursor.TryReadKind(8, out var wrong)
                    || cursor.TryReadKindInit(-1, init var invalid)) { return 1; }
                require cursor.TryReadKindInit(7, init var first);
                if (first.Kind != 7 || first.Range.Start != 0) { return 2; }
                if (cursor.TryReadKind(7, out var repeated)) { return 3; }
                require cursor.TryReadKind(8, out var second);
                if (second.Kind != 8 || second.Range.Start != 1
                    || cursor.TryReadKind(8, out var afterEnd)) { return 4; }
                require chain.TryCreateCursor(out var stale);
                require chain.TryClear();
                if (stale.TryReadKindInit(7, init var afterMutation) || stale.IsCurrent) { return 5; }
                return 42;
            }
            """;
        var program = await CompileAsync("ab", Entry, (BoundRuntimePolicy)policy).ConfigureAwait(true);
        Assert.Equal(42, NativeJit.Execute(program, policy));
        await ExecuteAotAsync(program, policy).ConfigureAwait(true);
    }

    [Theory]
    [InlineData("\uD800")]
    [InlineData("a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a")]
    public async Task FailedCursorScanDoesNotPublishAsync(string text)
    {
        var lexical = Lexer.Lex(SourceText.From(text));
        Assert.True(lexical.Diagnostics.Length != 0 || lexical.Tokens.Length > 64);
        const string Entry = """
            using Terrum.CodeAnalysis;
            int32 Main() {
                if (TryScanManagedCursor(init var cursor)) { return 1; }
                return 42;
            }
            """;
        foreach (var policy in new[] { NativeRuntimePolicy.FailFast, NativeRuntimePolicy.CheckedExceptions })
        {
            var program = await CompileAsync(text, Entry, (BoundRuntimePolicy)policy).ConfigureAwait(true);
            Assert.Equal(42, NativeJit.Execute(program, policy));
            await ExecuteAotAsync(program, policy).ConfigureAwait(true);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CursorRejectsMutationsWithoutPublishingOutputAsync(bool clear)
    {
        var mutation = clear ? "chain.TryClear()" : "chain.TryAppend(2, 1, 0)";
        var entry = $$"""
            using Terrum.CodeAnalysis;
            int32 Main() {
                require ManagedTokenChain.TryCreate(out var chain);
                require chain.TryAppend(1, 0, 1);
                require chain.TryCreateCursor(out var cursor);
                require cursor.TryRead(out var first) && first.Kind == 1;
                require chain.TryCreateCursorInit(init var stale);
                require {{mutation}};
                if (cursor.IsCurrent || stale.IsCurrent
                    || cursor.TryPeekInit(init var absent) || stale.TryPeek(out var missed)
                    || cursor.TryReadInit(init var unread) || stale.TryRead(out var ignored)) { return 1; }
                require chain.TryCreateCursorInit(init var fresh);
                if (!fresh.IsCurrent) { return 2; }
                if ({{(clear ? "fresh.TryRead(out var cleared)" : "!fresh.TryRead(out var appended) || appended.Kind != 1")}}) { return 3; }
                return 42;
            }
            """;
        foreach (var policy in new[] { NativeRuntimePolicy.FailFast, NativeRuntimePolicy.CheckedExceptions })
        {
            var program = await CompileAsync("a", entry, (BoundRuntimePolicy)policy).ConfigureAwait(true);
            Assert.Equal(42, NativeJit.Execute(program, policy));
            await ExecuteAotAsync(program, policy).ConfigureAwait(true);
        }
    }

    [Theory]
    [InlineData(NativeRuntimePolicy.FailFast)]
    [InlineData(NativeRuntimePolicy.CheckedExceptions)]
    public async Task CursorKeepsSourceGraphAliveAfterFactoryReturnsAsync(NativeRuntimePolicy policy)
    {
        const string Entry = """
            using Terrum.CodeAnalysis;
            bool MakeCursor(init ref<write ManagedTokenCursor> result) {
                require ManagedTokenChain.TryCreate(out var chain);
                require chain.TryAppend(7, 0, 1);
                return chain.TryCreateCursorInit(init result);
            }
            int32 Main() {
                require MakeCursor(init var cursor);
                if (ManagedHeap.Collect() != 0) { return 1; }
                if (!cursor.TryRead(out var token) || token.Kind != 7) { return 2; }
                if (cursor.TryRead(out var pastEnd) || !cursor.IsCurrent) { return 3; }
                if (ManagedHeap.Collect() != 0) { return 4; }
                return 42;
            }
            """;
        var program = await CompileAsync("a", Entry, (BoundRuntimePolicy)policy).ConfigureAwait(true);
        Assert.Equal(42, NativeJit.Execute(program, policy));
        await ExecuteAotAsync(program, policy).ConfigureAwait(true);
    }

    [Theory]
    [InlineData(NativeRuntimePolicy.FailFast)]
    [InlineData(NativeRuntimePolicy.CheckedExceptions)]
    public async Task FailedMutationKeepsCursorCurrentAndInvalidatedReadReleasesLinksAsync(NativeRuntimePolicy policy)
    {
        const string Entry = """
            using Terrum.CodeAnalysis;
            int32 Main() {
                require ManagedTokenChain.TryCreate(out var chain);
                require chain.TryAppend(7, 0, 1);
                require chain.TryCreateCursor(out var cursor);
                if (chain.TryAppend(-1, 1, 0) || !cursor.IsCurrent) { return 1; }
                if (!cursor.TryRead(out var token) || token.Kind != 7) { return 2; }
                require chain.TryCreateCursor(out var stale);
                require chain.TryClear();
                if (stale.TryReadInit(init var absent) || stale.IsCurrent) { return 3; }
                if (ManagedHeap.Collect() != 1) { return 4; }
                if (cursor.TryRead(out var afterClear) || cursor.IsCurrent) { return 5; }
                return 42;
            }
            """;
        var program = await CompileAsync("a", Entry, (BoundRuntimePolicy)policy).ConfigureAwait(true);
        Assert.Equal(42, NativeJit.Execute(program, policy));
        await ExecuteAotAsync(program, policy).ConfigureAwait(true);
    }

    [Theory]
    [InlineData(3, -1, 2)]
    [InlineData(5, 100, 2)]
    [InlineData(6, 1, 3)]
    public async Task InvalidHostRecordDoesNotPublishPartiallyBuiltChainAsync(int word, int value, int collected)
    {
        var entry = $$"""
            using Terrum.CodeAnalysis;
            int32 Main() {
                int32 count = Scan();
                Host.Tokens[{{word}}] = {{value}};
                if (TryBuildManagedTokenChain(count, init var chain)) { return 1; }
                if (ManagedHeap.Collect() != {{collected}}) { return 2; }
                return 42;
            }
            """;
        foreach (var policy in new[] { NativeRuntimePolicy.FailFast, NativeRuntimePolicy.CheckedExceptions })
        {
            var program = await CompileAsync("alpha beta", entry, (BoundRuntimePolicy)policy).ConfigureAwait(true);
            Assert.Equal(42, NativeJit.Execute(program, policy));
            await ExecuteAotAsync(program, policy).ConfigureAwait(true);
        }
    }

    private static async Task<BoundProgram> CompileAsync(string text, string entry, BoundRuntimePolicy policy)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
        string[] names = ["SourceRange", "Token", "ManagedTokenChain", "Scanner", "ManagedScanner",
            "TokenSnapshot", "ScalarFunctionParser"];
        var trees = new List<SyntaxTree>(names.Length + 1);
        foreach (var name in names)
        {
            var path = Path.Combine(root, "self-host", name + ".terrum");
            trees.Add(SyntaxTree.Parse(await File.ReadAllTextAsync(path).ConfigureAwait(true), path));
        }
        trees.Add(SyntaxTree.Parse(entry, "managed-scanner-entry.terrum"));
        var compilation = Compilation.Create(trees, ScannerHost.CreateBuffers(SourceText.From(text)), policy);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        return Assert.IsType<BoundProgram>(compilation.Program);
    }

    private static async Task ExecuteAotAsync(BoundProgram program, NativeRuntimePolicy policy)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
        var directory = Path.Combine(root, "artifacts", "scanner-tests");
        _ = Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"terrum-managed-scanner-{Guid.NewGuid():N}.exe");
        try
        {
            await File.WriteAllBytesAsync(path, NativeEmitter.Emit(program, policy)).ConfigureAwait(true);
            using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) {
                UseShellExecute = false,
                CreateNoWindow = true,
            }) ?? throw new InvalidOperationException("Could not start compiled managed scanner.");
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(true);
            Assert.Equal(42, process.ExitCode);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
