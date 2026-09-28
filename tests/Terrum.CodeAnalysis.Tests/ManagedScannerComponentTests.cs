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
            _ = source.Append(CultureInfo.InvariantCulture,
                $"if (!cursor.{method}({mode} var token{index}) || token{index}.Kind != {(int)expected[index].Kind} || token{index}.Range.Start != {expected[index].Span.Start} || token{index}.Range.Length != {expected[index].Span.Length}) {{ return 1; }}");
        }
        _ = source.Append("""
                if (cursor.TryRead(out var pastEnd) || !cursor.IsCurrent) { return 2; }
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
                require chain.TryCreateCursor(out var stale);
                require {{mutation}};
                if (cursor.IsCurrent || stale.IsCurrent
                    || cursor.TryReadInit(init var absent) || stale.TryRead(out var missed)) { return 1; }
                require chain.TryCreateCursor(out var fresh);
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
                require chain.TryCreateCursor(out var cursor);
                result = cursor;
                return true;
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
                if (stale.TryRead(out var absent) || stale.IsCurrent) { return 3; }
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
        string[] names = ["SourceRange", "Token", "ManagedTokenChain", "Scanner", "ManagedScanner"];
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
