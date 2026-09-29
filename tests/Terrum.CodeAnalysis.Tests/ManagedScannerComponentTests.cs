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
    [InlineData("alpha 1i32")]
    [InlineData("0x")]
    [InlineData("\uD800")]
    [InlineData("\"unterminated\n\"\\q\"")]
    public async Task OwnedDiagnosticsRemainAvailableOnLexicalFailureAsync(string text)
    {
        var expected = Lexer.Lex(SourceText.From(text)).Diagnostics;
        var entry = new StringBuilder("""
            using Terrum.CodeAnalysis;
            int32 Main() {
                require TokenDiagnosticSnapshot.TryScan(init var diagnostics);
            """);
        _ = entry.Append(CultureInfo.InvariantCulture,
            $"if (diagnostics.GetCount() != {expected.Length} || diagnostics.TryGet(-1, out var invalidCode, out var invalidSpan) || diagnostics.TryGet({expected.Length}, out var beyondCode, out var beyondSpan)) {{ return 1; }}");
        if (expected.Length != 0)
        {
            _ = entry.Append("if (TokenSnapshot.TryBuild(1, init var invalidTokens)) { return 3; } Host.Diagnostics[1] = -1;");
        }
        for (var index = 0; index < expected.Length; index++)
        {
            var diagnostic = expected[index];
            var code = int.Parse(diagnostic.Code.AsSpan(3), CultureInfo.InvariantCulture);
            _ = entry.Append(CultureInfo.InvariantCulture,
                $"require diagnostics.TryGet({index}, out var code{index}, out var span{index});"
                + $" if (code{index} != {code} || span{index}.Start != {diagnostic.Span.Start}"
                + $" || span{index}.Length != {diagnostic.Span.Length}) {{ return 2; }}");
        }
        _ = entry.Append("return 42; }");

        foreach (var policy in new[] { NativeRuntimePolicy.FailFast, NativeRuntimePolicy.CheckedExceptions })
        {
            var program = await CompileAsync(text, entry.ToString(), (BoundRuntimePolicy)policy).ConfigureAwait(true);
            Assert.Equal(42, NativeJit.Execute(program, policy));
            await ExecuteAotAsync(program, policy).ConfigureAwait(true);
        }
    }

    [Theory]
    [InlineData(0, -1)]
    [InlineData(0, int.MaxValue)]
    [InlineData(1, 0)]
    [InlineData(1, 10)]
    [InlineData(2, -1)]
    [InlineData(2, 2)]
    [InlineData(3, -1)]
    [InlineData(3, 2)]
    public async Task InvalidDiagnosticRecordsDoNotPublishSnapshotAsync(int word, int value)
    {
        var entry = $$"""
            using Terrum.CodeAnalysis;
            int32 Main() {
                int32 tokenCount = Scan();
                Host.Diagnostics[{{word}}] = {{value}};
                if (TokenDiagnosticSnapshot.TryBuild(init var diagnostics)) { return 1; }
                return 42;
            }
            """;
        foreach (var policy in new[] { NativeRuntimePolicy.FailFast, NativeRuntimePolicy.CheckedExceptions })
        {
            var program = await CompileAsync("\uD800", entry, (BoundRuntimePolicy)policy).ConfigureAwait(true);
            Assert.Equal(42, NativeJit.Execute(program, policy));
            await ExecuteAotAsync(program, policy).ConfigureAwait(true);
        }
    }

    [Theory]
    [InlineData(0, 3, 3, 0, true)]
    [InlineData(1, 3, 3, 0, false)]
    [InlineData(0, 4, 3, 1, true)]
    [InlineData(1, 4, 3, 1, false)]
    [InlineData(0, 5, 4, 1, true)]
    [InlineData(0, 6, 5, 1, true)]
    [InlineData(715827881, int.MaxValue, 3, 0, true)]
    [InlineData(715827882, int.MaxValue, 3, 0, false)]
    [InlineData(536870910, int.MaxValue, 4, 1, true)]
    [InlineData(536870911, int.MaxValue, 4, 1, false)]
    [InlineData(429496728, int.MaxValue, 5, 1, true)]
    [InlineData(429496729, int.MaxValue, 5, 1, false)]
    [InlineData(-1, int.MaxValue, 3, 0, false)]
    [InlineData(0, 0, 3, 1, false)]
    public async Task ScannerRecordCapacityChecksCannotOverflowAsync(
        int count, int capacity, int width, int prefix, bool expected)
    {
        var result = expected ? "true" : "false";
        var entry = $$"""
            using Terrum.CodeAnalysis;
            int32 Main() {
                if (CanAppendRecord({{count}}, {{capacity}}, {{width}}, {{prefix}}) != {{result}})
                {
                    return 1;
                }
                return 42;
            }
            """;
        foreach (var policy in new[] { NativeRuntimePolicy.FailFast, NativeRuntimePolicy.CheckedExceptions })
        {
            var program = await CompileAsync("", entry, (BoundRuntimePolicy)policy).ConfigureAwait(true);
            Assert.Equal(42, NativeJit.Execute(program, policy));
            await ExecuteAotAsync(program, policy).ConfigureAwait(true);
        }
    }

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
    [InlineData("")]
    [InlineData("  // leading\r\nalpha /* block */ beta\t")]
    [InlineData("alpha\u2028beta // trailing")]
    public async Task OwnedTriviaSnapshotMatchesHostedLeadingTriviaAsync(string text)
    {
        var expected = Lexer.Lex(SourceText.From(text)).Tokens
            .SelectMany((token, owner) => token.LeadingTrivia.Select(trivia =>
                ((int)trivia.Kind, trivia.Span.Start, trivia.Span.Length, owner)))
            .ToArray();
        var entry = new StringBuilder("""
            using Terrum.CodeAnalysis;
            int32 Main() {
                require TryScanLossless(init var tokens, init var trivia);
            """);
        _ = entry.Append(CultureInfo.InvariantCulture,
            $"if (trivia.GetCount() != {expected.Length} || trivia.TryGet(-1, out var negative, out var negativeOwner) || trivia.TryGet({expected.Length}, out var beyond, out var beyondOwner)) {{ return 1; }}");
        if (expected.Length > 0)
        {
            _ = entry.Append("Host.Trivia[1] = -1;");
        }
        for (var index = 0; index < expected.Length; index++)
        {
            var (kind, start, length, owner) = expected[index];
            _ = entry.Append(CultureInfo.InvariantCulture,
                $"require trivia.TryGet({index}, out var part{index}, out var owner{index});"
                + $" if (part{index}.Kind != {kind} || part{index}.Range.Start != {start}"
                + $" || part{index}.Range.Length != {length} || owner{index} != {owner}) {{ return 2; }}");
        }
        _ = entry.Append("return 42; }");

        foreach (var policy in new[] { NativeRuntimePolicy.FailFast, NativeRuntimePolicy.CheckedExceptions })
        {
            var program = await CompileAsync(text, entry.ToString(), (BoundRuntimePolicy)policy).ConfigureAwait(true);
            Assert.Equal(42, NativeJit.Execute(program, policy));
            await ExecuteAotAsync(program, policy).ConfigureAwait(true);
        }
    }

    [Theory]
    [InlineData(NativeRuntimePolicy.FailFast)]
    [InlineData(NativeRuntimePolicy.CheckedExceptions)]
    public async Task OwnedTriviaSnapshotSurvivesTheManagedChainLimitAsync(NativeRuntimePolicy policy)
    {
        const string Entry = """
            using Terrum.CodeAnalysis;
            int32 Main() {
                require TryScanLossless(init var tokens, init var trivia);
                if (tokens.GetCount() != 129 || trivia.GetCount() != 128) { return 1; }
                require trivia.TryGet(0, out var first, out var firstOwner);
                require trivia.TryGet(63, out var middle, out var middleOwner);
                require trivia.TryGet(127, out var last, out var lastOwner);
                if (first.Kind != 0 || first.Range.Start != 1 || first.Range.Length != 1
                    || firstOwner != 1 || middle.Range.Start != 127 || middleOwner != 64
                    || last.Range.Start != 255 || lastOwner != 128) { return 2; }
                return 42;
            }
            """;
        var input = string.Concat(Enumerable.Repeat("a ", 128));
        var program = await CompileAsync(input, Entry, (BoundRuntimePolicy)policy).ConfigureAwait(true);
        Assert.Equal(42, NativeJit.Execute(program, policy));
        await ExecuteAotAsync(program, policy).ConfigureAwait(true);
    }

    [Theory]
    [InlineData(NativeRuntimePolicy.FailFast)]
    [InlineData(NativeRuntimePolicy.CheckedExceptions)]
    public async Task LexicalFailureLeavesBothLosslessSnapshotsVacantAsync(NativeRuntimePolicy policy)
    {
        const string Entry = """
            using Terrum.CodeAnalysis;
            int32 Main() {
                if (TryScanLossless(init var tokens, init var trivia)) { return 1; }
                return 42;
            }
            """;
        var program = await CompileAsync("\uD800", Entry, (BoundRuntimePolicy)policy).ConfigureAwait(true);
        Assert.Equal(42, NativeJit.Execute(program, policy));
        await ExecuteAotAsync(program, policy).ConfigureAwait(true);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(0, int.MaxValue)]
    [InlineData(1, -1)]
    [InlineData(2, 1)]
    [InlineData(3, 0)]
    [InlineData(4, 0)]
    [InlineData(4, 3)]
    public async Task InvalidTriviaDoesNotPublishOwnedSnapshotAsync(int word, int value)
    {
        var entry = $$"""
            using Terrum.CodeAnalysis;
            int32 Main() {
                int32 count = Scan();
                Host.Trivia[{{word}}] = {{value}};
                if (TryBuildLossless(count, init var tokens, init var trivia)) { return 1; }
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

    [Theory]
    [InlineData(NativeRuntimePolicy.FailFast)]
    [InlineData(NativeRuntimePolicy.CheckedExceptions)]
    public async Task OwnedIntegerRecordsRetainFullUnsignedMagnitudeAndOverflowAsync(
        NativeRuntimePolicy policy)
    {
        const string Entry = """
            using Terrum.CodeAnalysis;
            int32 Main() {
                require TryScanCompilerInput(init var tokens, init var trivia, init var integers);
                if (tokens.GetCount() != 5 || trivia.GetCount() != 3
                    || integers.GetCount() != 4 || integers.TryGet(-1, out var negativeIndex,
                        out var negativeOverflow, out var negativeLow, out var negativeHigh)
                    || integers.TryGet(4, out var beyondIndex, out var beyondOverflow,
                        out var beyondLow, out var beyondHigh)) { return 1; }
                Host.IntegerValues[3] = 0;
                require integers.TryGet(0, out var first, out var firstOverflow,
                    out var firstLow, out var firstHigh);
                require integers.TryGet(1, out var second, out var secondOverflow,
                    out var secondLow, out var secondHigh);
                require integers.TryGet(2, out var third, out var thirdOverflow,
                    out var thirdLow, out var thirdHigh);
                require integers.TryGet(3, out var fourth, out var fourthOverflow,
                    out var fourthLow, out var fourthHigh);
                if (first != 0 || firstOverflow || firstLow != -1 || firstHigh != -1
                    || second != 1 || !secondOverflow || secondLow != 0 || secondHigh != 0
                    || third != 2 || thirdOverflow || thirdLow != 2 || thirdHigh != 0
                    || fourth != 3 || fourthOverflow || fourthLow != 42 || fourthHigh != 0) {
                    return 2;
                }
                return 42;
            }
            """;
        const string Text = "0xFFFF_FFFF_FFFF_FFFF 18446744073709551616 0b1_0 42";
        var program = await CompileAsync(Text, Entry, (BoundRuntimePolicy)policy).ConfigureAwait(true);
        Assert.Equal(42, NativeJit.Execute(program, policy));
        await ExecuteAotAsync(program, policy).ConfigureAwait(true);
    }

    [Theory]
    [InlineData(NativeRuntimePolicy.FailFast)]
    [InlineData(NativeRuntimePolicy.CheckedExceptions)]
    public async Task CompilerInputPermitsNoDecodedIntegersAsync(NativeRuntimePolicy policy)
    {
        const string Entry = """
            using Terrum.CodeAnalysis;
            int32 Main() {
                require TryScanCompilerInput(init var tokens, init var trivia, init var integers);
                if (tokens.GetCount() != 2 || trivia.GetCount() != 0
                    || integers.GetCount() != 0
                    || integers.TryFind(0, out var overflow, out var low, out var high)) {
                    return 1;
                }
                return 42;
            }
            """;
        var program = await CompileAsync("alpha", Entry, (BoundRuntimePolicy)policy).ConfigureAwait(true);
        Assert.Equal(42, NativeJit.Execute(program, policy));
        await ExecuteAotAsync(program, policy).ConfigureAwait(true);
    }

    [Theory]
    [InlineData(NativeRuntimePolicy.FailFast)]
    [InlineData(NativeRuntimePolicy.CheckedExceptions)]
    public async Task FindsDecodedIntegerByTokenIndexAndSkipsNonIntegerNumericsAsync(
        NativeRuntimePolicy policy)
    {
        const string Entry = """
            using Terrum.CodeAnalysis;
            int32 Main() {
                require TryScanCompilerInput(init var tokens, init var trivia, init var integers);
                if (tokens.GetCount() != 4 || integers.GetCount() != 2
                    || integers.TryFind(-1, out var negativeOverflow, out var negativeLow,
                        out var negativeHigh)
                    || integers.TryFind(1, out var floatingOverflow, out var floatingLow,
                        out var floatingHigh)
                    || integers.TryFind(3, out var eofOverflow, out var eofLow,
                        out var eofHigh)) { return 1; }
                require integers.TryFind(0, out var firstOverflow, out var firstLow,
                    out var firstHigh);
                require integers.TryFind(2, out var lastOverflow, out var lastLow,
                    out var lastHigh);
                if (firstOverflow || firstLow != 1 || firstHigh != 0
                    || lastOverflow || lastLow != 42 || lastHigh != 0) { return 2; }
                return 42;
            }
            """;
        var program = await CompileAsync("1 1.5 42", Entry, (BoundRuntimePolicy)policy).ConfigureAwait(true);
        Assert.Equal(42, NativeJit.Execute(program, policy));
        await ExecuteAotAsync(program, policy).ConfigureAwait(true);
    }

    [Theory]
    [InlineData(NativeRuntimePolicy.FailFast)]
    [InlineData(NativeRuntimePolicy.CheckedExceptions)]
    public async Task IndexedIntegerLookupExceedsManagedChainLimitAsync(NativeRuntimePolicy policy)
    {
        const string Entry = """
            using Terrum.CodeAnalysis;
            int32 Main() {
                require TryScanCompilerInput(init var tokens, init var trivia, init var integers);
                if (tokens.GetCount() != 129 || integers.GetCount() != 128
                    || integers.TryFind(128, out var missingOverflow, out var missingLow,
                        out var missingHigh)) { return 1; }
                require integers.TryFind(0, out var firstOverflow, out var firstLow,
                    out var firstHigh);
                require integers.TryFind(64, out var middleOverflow, out var middleLow,
                    out var middleHigh);
                require integers.TryFind(127, out var lastOverflow, out var lastLow,
                    out var lastHigh);
                if (firstOverflow || middleOverflow || lastOverflow
                    || firstLow != 1 || middleLow != 1 || lastLow != 1
                    || firstHigh != 0 || middleHigh != 0 || lastHigh != 0) { return 2; }
                return 42;
            }
            """;
        var program = await CompileAsync(string.Concat(Enumerable.Repeat("1 ", 128)),
            Entry, (BoundRuntimePolicy)policy).ConfigureAwait(true);
        Assert.Equal(42, NativeJit.Execute(program, policy));
        await ExecuteAotAsync(program, policy).ConfigureAwait(true);
    }

    [Theory]
    [InlineData(0, 3)]
    [InlineData(1, -1)]
    [InlineData(2, 2)]
    [InlineData(5, 1)]
    [InlineData(6, 0)]
    public async Task InvalidIntegerRecordsDoNotPublishCompilerInputAsync(int word, int value)
    {
        var entry = $$"""
            using Terrum.CodeAnalysis;
            int32 Main() {
                int32 count = Scan();
                Host.IntegerValues[{{word}}] = {{value}};
                if (TryBuildCompilerInput(count, init var tokens, init var trivia,
                    init var integers)) { return 1; }
                return 42;
            }
            """;
        foreach (var policy in new[] { NativeRuntimePolicy.FailFast, NativeRuntimePolicy.CheckedExceptions })
        {
            var program = await CompileAsync("42 3", entry, (BoundRuntimePolicy)policy).ConfigureAwait(true);
            Assert.Equal(42, NativeJit.Execute(program, policy));
            await ExecuteAotAsync(program, policy).ConfigureAwait(true);
        }
    }

    [Theory]
    [InlineData(NativeRuntimePolicy.FailFast)]
    [InlineData(NativeRuntimePolicy.CheckedExceptions)]
    public async Task OverflowIntegerRecordRequiresZeroPayloadAsync(NativeRuntimePolicy policy)
    {
        const string Entry = """
            using Terrum.CodeAnalysis;
            int32 Main() {
                int32 count = Scan();
                Host.IntegerValues[2] = 1;
                Host.IntegerValues[3] = 1;
                if (TryBuildCompilerInput(count, init var tokens, init var trivia,
                    init var integers)) { return 1; }
                return 42;
            }
            """;
        var program = await CompileAsync("42", Entry, (BoundRuntimePolicy)policy).ConfigureAwait(true);
        Assert.Equal(42, NativeJit.Execute(program, policy));
        await ExecuteAotAsync(program, policy).ConfigureAwait(true);
    }

    [Theory]
    [InlineData("int32 Answer() { return 42; }", true)]
    [InlineData("int32 Answer() { return -42; }", true)]
    [InlineData("int32 Answer() { return +42; }", true)]
    [InlineData("int32 Answer() { return ~42; }", true)]
    [InlineData("uint32 Answer() { return 42; }", false)]
    [InlineData("int32 Answer() { return (42); }", true)]
    [InlineData("int32 Answer() { return (-42); }", true)]
    [InlineData("int32 Answer() { return -(42); }", true)]
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
        Assert.Equal(57, (int)TokenKind.Plus);
        Assert.Equal(58, (int)TokenKind.Minus);
        Assert.Equal(62, (int)TokenKind.Bang);
        Assert.Equal(63, (int)TokenKind.Tilde);

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
            var program = await CompileParserAsync(text, entry, (BoundRuntimePolicy)policy).ConfigureAwait(true);
            Assert.Equal(42, NativeJit.Execute(program, policy));
            await ExecuteAotAsync(program, policy).ConfigureAwait(true);
        }
    }

    [Theory]
    [InlineData("int32 Answer() { return 42; }", 1)]
    [InlineData("uint32 Answer() { return 42; }", 2)]
    [InlineData("uint32 Answer() { return +42; }", 2)]
    [InlineData("int64 Answer() { return -42; }", 3)]
    [InlineData("uint64 Answer() { return 42; }", 4)]
    [InlineData("int64 Answer() { return (42); }", 3)]
    [InlineData("uint64 Answer() { return (+42); }", 4)]
    [InlineData("bool Answer() { return true; }", 5)]
    [InlineData("bool Answer() { return (false); }", 5)]
    [InlineData("bool Answer() { return !true; }", 5)]
    [InlineData("bool Answer() { return !(false); }", 5)]
    [InlineData("uint64 Answer() { return +(42); }", 4)]
    [InlineData("uint64 Answer() { return (~42); }", 4)]
    [InlineData("float32 Answer() { return 42; }", 0)]
    public async Task TypedScalarParserMatchesHostedDeclarationSpansAsync(string text, int type)
    {
        var hosted = SyntaxTree.Parse(text);
        var name = hosted.Tokens[1].Span;
        var result = hosted.Root.DescendantNodes().Single(node => node.Kind == SyntaxKind.ReturnStatement);
        var expression = ((SyntaxNode)result.Children[1]).Span;
        var assertion = type != 0
            ? FormattableString.Invariant($$"""
                require TryParseScalarReturnFunctionTyped(move tokens, out var kind,
                    out var name, out var expression);
                if (kind != {{type}} || name.Start != {{name.Start}} || name.Length != {{name.Length}}
                    || expression.Start != {{expression.Start}}
                    || expression.Length != {{expression.Length}}) { return 1; }
                """)
            : """
                if (TryParseScalarReturnFunctionTyped(move tokens, out var kind,
                    out var name, out var expression)) { return 1; }
                """;
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
            var program = await CompileParserAsync(text, entry, (BoundRuntimePolicy)policy).ConfigureAwait(true);
            Assert.Equal(42, NativeJit.Execute(program, policy));
            await ExecuteAotAsync(program, policy).ConfigureAwait(true);
        }
    }

    [Theory]
    [InlineData("bool Value() { return true; }", true, true)]
    [InlineData("bool Value() { return false; }", true, false)]
    [InlineData("bool Value() { return (true); }", true, true)]
    [InlineData("bool Value() { return (false); }", true, false)]
    [InlineData("bool Value() { return !true; }", true, false)]
    [InlineData("bool Value() { return !false; }", true, true)]
    [InlineData("bool Value() { return (!true); }", true, false)]
    [InlineData("bool Value() { return !(false); }", true, true)]
    [InlineData("bool Value() { return +true; }", false, false)]
    [InlineData("bool Value() { return +(true); }", false, false)]
    [InlineData("int32 Value() { return 1; }", false, false)]
    public async Task BoundedNativeBoolEvaluationMatchesHostedExecutedValuesAsync(
        string text, bool evaluable, bool value)
    {
        var expected = value ? "true" : "false";
        var assertion = evaluable
            ? $$"""
                require TryEvaluateBoolScalarReturn(move tokens, out var name, out var bound);
                if (name.Start != 5 || name.Length != 5 || bound != {{expected}}) { return 1; }
                """
            : """
                if (TryEvaluateBoolScalarReturn(move tokens, out var name, out var bound)) { return 1; }
                """;
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
            if (evaluable)
            {
                var hostedSource = text + $$"""
                     int32 Main() {
                         if (Value() == {{expected}}) { return 42; }
                         return 1;
                     }
                    """;
                var hosted = Compilation.Create(SyntaxTree.Parse(hostedSource), (BoundRuntimePolicy)policy);
                Assert.False(hosted.HasErrors, string.Join(Environment.NewLine, hosted.Diagnostics));
                Assert.Equal(42, NativeJit.Execute(Assert.IsType<BoundProgram>(hosted.Program), policy));
            }
            var program = await CompileParserAsync(text, entry, (BoundRuntimePolicy)policy).ConfigureAwait(true);
            Assert.Equal(42, NativeJit.Execute(program, policy));
            await ExecuteAotAsync(program, policy).ConfigureAwait(true);
        }
    }

    [Theory]
    [InlineData("int32(1) == int32(1)", true, true)]
    [InlineData("int32(1) != int32(2)", true, true)]
    [InlineData("int32(0xFFFF_FFFF) < int32(0)", true, true)]
    [InlineData("int32(1) <= int32(1)", true, true)]
    [InlineData("uint32(4294967295) > uint32(1)", true, true)]
    [InlineData("uint32(1) >= uint32(2)", true, false)]
    [InlineData("int64(0x8000_0000_0000_0000) < int64(0)", true, true)]
    [InlineData("uint64(18446744073709551615) > uint64(0)", true, true)]
    [InlineData("int64(0xFFFF_FFFF_FFFF_FFFF) == int64(0)", true, false)]
    [InlineData("char16(65535) > char16(1)", true, true)]
    [InlineData("char16(0xFFFF) <= char16(65535)", true, true)]
    [InlineData("char16(0) != char16(0)", true, false)]
    [InlineData("int32(2147483648) == int32(0)", false, false)]
    [InlineData("uint32(4294967296) == uint32(0)", false, false)]
    [InlineData("int64(9223372036854775808) < int64(0)", false, false)]
    [InlineData("uint64(18446744073709551616) > uint64(0)", false, false)]
    [InlineData("char16(65536) > char16(0)", false, false)]
    [InlineData("char16(1) < uint32(2)", false, false)]
    [InlineData("int32(1) < int64(2)", false, false)]
    [InlineData("int32(1) + int32(2)", false, false)]
    public async Task BoundedNativeTypedScalarComparisonUsesOwnedRecordsAsync(
        string expression, bool evaluable, bool expected)
    {
        var text = $"bool Value() {{ return {expression}; }}";
        var assertion = evaluable
            ? $$"""
                require TryEvaluateTypedScalarComparisonReturn(move tokens, move integers,
                    out var name, out var bound);
                if (name.Start != 5 || name.Length != 5 || bound != {{(expected ? "true" : "false")}})
                {
                    return 1;
                }
                """
            : """
                if (TryEvaluateTypedScalarComparisonReturn(move tokens, move integers,
                    out var name, out var bound))
                {
                    return 1;
                }
                """;
        var entry = $$"""
            using Terrum.CodeAnalysis;
            int32 Main() {
                require TryScanCompilerInput(init var tokens, init var trivia, init var integers);
                {{assertion}}
                return 42;
            }
            """;
        foreach (var policy in new[] { NativeRuntimePolicy.FailFast, NativeRuntimePolicy.CheckedExceptions })
        {
            if (evaluable)
            {
                var hosted = Compilation.Create(SyntaxTree.Parse(
                    text + " int32 Main() { if (Value() == true) { return 42; } return 1; }"),
                    (BoundRuntimePolicy)policy);
                Assert.False(hosted.HasErrors, string.Join(Environment.NewLine, hosted.Diagnostics));
                Assert.Equal(expected ? 42 : 1,
                    NativeJit.Execute(Assert.IsType<BoundProgram>(hosted.Program), policy));
            }
            var program = await CompileParserAsync(text, entry, (BoundRuntimePolicy)policy).ConfigureAwait(true);
            Assert.Equal(42, NativeJit.Execute(program, policy));
            await ExecuteAotAsync(program, policy).ConfigureAwait(true);
        }
    }

    [Theory]
    [InlineData("int32", "37", "x + 5", true, "42")]
    [InlineData("int32", "1", "x + 0xFFFF_FFFF", true, "0")]
    [InlineData("int32", "int32.MaxValue", "x + 0x8000_0000", true, "-1")]
    [InlineData("int32", "int32.MaxValue", "x + 1", false, "0")]
    [InlineData("int32", "int32.MinValue", "x + 0xFFFF_FFFF", false, "0")]
    [InlineData("int32", "0", "x + 2147483648", false, "0")]
    [InlineData("int32", "1", "y + 5", false, "0")]
    [InlineData("uint32", "0", "x + 0xFFFF_FFFF", true, "uint32.MaxValue")]
    [InlineData("uint32", "uint32.MaxValue", "x + 1", false, "0")]
    [InlineData("uint32", "0", "x + 4294967296", false, "0")]
    [InlineData("int64", "int64.MaxValue", "x + 0x8000_0000_0000_0000", true, "-1")]
    [InlineData("int64", "int64.MaxValue", "x + 1", false, "0")]
    [InlineData("int64", "int64.MinValue", "x + 0xFFFF_FFFF_FFFF_FFFF", false, "0")]
    [InlineData("int64", "0", "x + 9223372036854775808", false, "0")]
    [InlineData("uint64", "0", "x + 18446744073709551615", true, "uint64.MaxValue")]
    [InlineData("uint64", "uint64.MaxValue", "x + 1", false, "0")]
    [InlineData("uint64", "0", "x + 18446744073709551616", false, "0")]
    public async Task BoundedNativeNamedParameterAdditionUsesOwnedRecordsAsync(
        string typeName, string parameter, string expression, bool evaluable, string expected)
    {
        var evaluator = typeName switch
        {
            "int32" => "TryEvaluateInt32ParameterAddReturn",
            "uint32" => "TryEvaluateUInt32ParameterAddReturn",
            "int64" => "TryEvaluateInt64ParameterAddReturn",
            "uint64" => "TryEvaluateUInt64ParameterAddReturn",
            _ => throw new ArgumentOutOfRangeException(nameof(typeName)),
        };
        var text = $"{typeName} Value({typeName} x) {{ return {expression}; }}";
        var assertion = evaluable
            ? $$"""
                require {{evaluator}}(move tokens, move integers, {{parameter}},
                    out var name, out var bound);
                if (name.Start != {{typeName.Length + 1}} || name.Length != 5
                    || bound != {{expected}}) { return 1; }
                """
            : $$"""
                if ({{evaluator}}(move tokens, move integers, {{parameter}},
                    out var name, out var bound)) { return 1; }
                """;
        var entry = $$"""
            using Terrum.CodeAnalysis;
            int32 Main() {
                require TryScanCompilerInput(init var tokens, init var trivia, init var integers);
                {{assertion}}
                return 42;
            }
            """;
        foreach (var policy in new[] { NativeRuntimePolicy.FailFast, NativeRuntimePolicy.CheckedExceptions })
        {
            if (evaluable)
            {
                var source = text + $$"""
                     int32 Main() {
                         {{typeName}} actual = Value({{parameter}});
                         if (actual == {{expected}}) { return 42; }
                         return 1;
                     }
                    """;
                var hosted = Compilation.Create(SyntaxTree.Parse(source), (BoundRuntimePolicy)policy);
                Assert.False(hosted.HasErrors, string.Join(Environment.NewLine, hosted.Diagnostics));
                Assert.Equal(42, NativeJit.Execute(Assert.IsType<BoundProgram>(hosted.Program), policy));
            }
            var program = await CompileParserAsync(text, entry, (BoundRuntimePolicy)policy).ConfigureAwait(true);
            Assert.Equal(42, NativeJit.Execute(program, policy));
            await ExecuteAotAsync(program, policy).ConfigureAwait(true);
        }
    }

    [Theory]
    [InlineData("int32", "21", "21", "x + y", true, "42")]
    [InlineData("int32", "int32.MinValue", "int32.MaxValue", "x + y", true, "-1")]
    [InlineData("int32", "int32.MaxValue", "1", "x + y", false, "0")]
    [InlineData("int32", "int32.MinValue", "-1", "x + y", false, "0")]
    [InlineData("int32", "1", "2", "x + x", false, "0")]
    [InlineData("int32", "1", "2", "y + x", false, "0")]
    [InlineData("uint32", "uint32.MaxValue", "0", "x + y", true, "uint32.MaxValue")]
    [InlineData("uint32", "uint32.MaxValue", "1", "x + y", false, "0")]
    [InlineData("int64", "int64.MinValue", "int64.MaxValue", "x + y", true, "-1")]
    [InlineData("int64", "int64.MaxValue", "1", "x + y", false, "0")]
    [InlineData("int64", "int64.MinValue", "-1", "x + y", false, "0")]
    [InlineData("uint64", "uint64.MaxValue", "0", "x + y", true, "uint64.MaxValue")]
    [InlineData("uint64", "uint64.MaxValue", "1", "x + y", false, "0")]
    public async Task BoundedNativeTwoParameterAdditionBindsBothNamesAsync(
        string typeName, string first, string second, string expression,
        bool evaluable, string expected)
    {
        var evaluator = typeName switch
        {
            "int32" => "TryEvaluateInt32TwoParameterAddReturn",
            "uint32" => "TryEvaluateUInt32TwoParameterAddReturn",
            "int64" => "TryEvaluateInt64TwoParameterAddReturn",
            "uint64" => "TryEvaluateUInt64TwoParameterAddReturn",
            _ => throw new ArgumentOutOfRangeException(nameof(typeName)),
        };
        var text = $"{typeName} Value({typeName} x, {typeName} y) {{ return {expression}; }}";
        var assertion = evaluable
            ? $$"""
                require {{evaluator}}(move tokens, {{first}}, {{second}},
                    out var name, out var bound);
                if (name.Start != {{typeName.Length + 1}} || name.Length != 5
                    || bound != {{expected}})
                {
                    return 1;
                }
                """
            : $$"""
                if ({{evaluator}}(move tokens, {{first}}, {{second}},
                    out var name, out var bound))
                {
                    return 1;
                }
                """;
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
            if (evaluable)
            {
                var source = text + $$"""
                     int32 Main() {
                         {{typeName}} actual = Value({{first}}, {{second}});
                         if (actual == {{expected}}) { return 42; }
                         return 1;
                     }
                    """;
                var hosted = Compilation.Create(SyntaxTree.Parse(source), (BoundRuntimePolicy)policy);
                Assert.False(hosted.HasErrors, string.Join(Environment.NewLine, hosted.Diagnostics));
                Assert.Equal(42, NativeJit.Execute(Assert.IsType<BoundProgram>(hosted.Program), policy));
            }
            var program = await CompilePairParserAsync(text, entry, (BoundRuntimePolicy)policy).ConfigureAwait(true);
            Assert.Equal(42, NativeJit.Execute(program, policy));
            await ExecuteAotAsync(program, policy).ConfigureAwait(true);
        }
    }

    [Theory]
    [InlineData("int32 Value(int32 x, int32 x) { return x + x; }")]
    [InlineData("int32 Value(int32 x, uint32 y) { return x + y; }")]
    [InlineData("int32 Value(int32 x int32 y) { return x + y; }")]
    [InlineData("char16 Value(char16 x, char16 y) { return x + y; }")]
    public async Task BoundedNativeTwoParameterAdditionRejectsInvalidDeclarationsAsync(string text)
    {
        const string Entry = """
            using Terrum.CodeAnalysis;
            int32 Main() {
                require TokenSnapshot.TryScan(init var tokens);
                if (TryEvaluateInt32TwoParameterAddReturn(move tokens, 1, 2,
                    out var name, out var value)) { return 1; }
                return 42;
            }
            """;
        foreach (var policy in new[] { NativeRuntimePolicy.FailFast, NativeRuntimePolicy.CheckedExceptions })
        {
            var program = await CompilePairParserAsync(text, Entry, (BoundRuntimePolicy)policy).ConfigureAwait(true);
            Assert.Equal(42, NativeJit.Execute(program, policy));
            await ExecuteAotAsync(program, policy).ConfigureAwait(true);
        }
    }

    [Theory]
    [InlineData("18", "21", "x + y + 3", true, "42")]
    [InlineData("int32.MaxValue", "-1", "x + y + 1", true, "int32.MaxValue")]
    [InlineData("int32.MinValue", "int32.MaxValue", "x + y + 1", true, "0")]
    [InlineData("0", "0", "x + y + 0xFFFF_FFFF", true, "-1")]
    [InlineData("int32.MaxValue", "1", "x + y + 0xFFFF_FFFF", false, "0")]
    [InlineData("int32.MinValue", "-1", "x + y + 1", false, "0")]
    [InlineData("int32.MaxValue", "0", "x + y + 1", false, "0")]
    [InlineData("int32.MinValue", "0", "x + y + 0xFFFF_FFFF", false, "0")]
    [InlineData("0", "0", "x + y + 2147483648", false, "0")]
    [InlineData("0", "0", "x + y + 4294967296", false, "0")]
    [InlineData("0", "0", "x + z + 3", false, "0")]
    [InlineData("0", "0", "x + y + 3 + 4", false, "0")]
    public async Task BoundedNativeTwoParameterChainedAdditionChecksEachStepAsync(
        string first, string second, string expression, bool evaluable, string expected)
    {
        var text = $"int32 Value(int32 x, int32 y) {{ return {expression}; }}";
        var assertion = evaluable
            ? $$"""
                require TryEvaluateInt32TwoParameterAddAndLiteralReturn(
                    move tokens, move integers, {{first}}, {{second}},
                    out var name, out var bound);
                if (name.Start != 6 || name.Length != 5 || bound != {{expected}})
                {
                    return 1;
                }
                """
            : $$"""
                if (TryEvaluateInt32TwoParameterAddAndLiteralReturn(
                    move tokens, move integers, {{first}}, {{second}},
                    out var name, out var bound))
                {
                    return 1;
                }
                """;
        var entry = $$"""
            using Terrum.CodeAnalysis;
            int32 Main() {
                require TryScanCompilerInput(init var tokens, init var trivia, init var integers);
                {{assertion}}
                return 42;
            }
            """;
        foreach (var policy in new[] { NativeRuntimePolicy.FailFast, NativeRuntimePolicy.CheckedExceptions })
        {
            if (evaluable)
            {
                var source = text + $$"""
                     int32 Main() {
                         if (Value({{first}}, {{second}}) == {{expected}}) { return 42; }
                         return 1;
                     }
                    """;
                var hosted = Compilation.Create(SyntaxTree.Parse(source), (BoundRuntimePolicy)policy);
                Assert.False(hosted.HasErrors, string.Join(Environment.NewLine, hosted.Diagnostics));
                Assert.Equal(42, NativeJit.Execute(Assert.IsType<BoundProgram>(hosted.Program), policy));
            }
            var program = await CompilePairParserAsync(text, entry, (BoundRuntimePolicy)policy).ConfigureAwait(true);
            Assert.Equal(42, NativeJit.Execute(program, policy));
            await ExecuteAotAsync(program, policy).ConfigureAwait(true);
        }
    }

    [Theory]
    [InlineData("int32", "-1", "x < 0", true, true)]
    [InlineData("int32", "-1", "x == 0xFFFF_FFFF", true, true)]
    [InlineData("int32", "int32.MaxValue", "x <= 2147483647", true, true)]
    [InlineData("int32", "0", "x < 2147483648", false, false)]
    [InlineData("uint32", "uint32.MaxValue", "x >= 0xFFFF_FFFF", true, true)]
    [InlineData("uint32", "0", "x < 4294967296", false, false)]
    [InlineData("int64", "int64.MinValue", "x < 0", true, true)]
    [InlineData("int64", "-1", "x == 0xFFFF_FFFF_FFFF_FFFF", true, true)]
    [InlineData("int64", "0", "x < 9223372036854775808", false, false)]
    [InlineData("uint64", "uint64.MaxValue", "x > 18446744073709551614", true, true)]
    [InlineData("uint64", "0", "x > 18446744073709551616", false, false)]
    [InlineData("char16", "char16(65535)", "x >= 65535", true, true)]
    [InlineData("char16", "char16(0)", "x != 0", true, false)]
    [InlineData("char16", "char16(0)", "x != 65536", false, false)]
    [InlineData("int32", "1", "y == 1", false, false)]
    public async Task BoundedNativeNamedParameterComparisonUsesOwnedRecordsAsync(
        string typeName, string parameter, string expression, bool evaluable, bool expected)
    {
        var evaluator = typeName switch
        {
            "int32" => "TryEvaluateInt32ParameterComparisonReturn",
            "uint32" => "TryEvaluateUInt32ParameterComparisonReturn",
            "int64" => "TryEvaluateInt64ParameterComparisonReturn",
            "uint64" => "TryEvaluateUInt64ParameterComparisonReturn",
            "char16" => "TryEvaluateChar16ParameterComparisonReturn",
            _ => throw new ArgumentOutOfRangeException(nameof(typeName)),
        };
        var text = $"bool Value({typeName} x) {{ return {expression}; }}";
        var expectedValue = expected ? "true" : "false";
        var assertion = evaluable
            ? $$"""
                require {{evaluator}}(move tokens, move integers, {{parameter}},
                    out var name, out var bound);
                if (name.Start != 5 || name.Length != 5 || bound != {{expectedValue}})
                {
                    return 1;
                }
                """
            : $$"""
                if ({{evaluator}}(move tokens, move integers, {{parameter}},
                    out var name, out var bound))
                {
                    return 1;
                }
                """;
        var entry = $$"""
            using Terrum.CodeAnalysis;
            int32 Main() {
                require TryScanCompilerInput(init var tokens, init var trivia, init var integers);
                {{assertion}}
                return 42;
            }
            """;
        foreach (var policy in new[] { NativeRuntimePolicy.FailFast, NativeRuntimePolicy.CheckedExceptions })
        {
            if (evaluable)
            {
                var source = text + $$"""
                     int32 Main() {
                         if (Value({{parameter}}) == {{expectedValue}}) { return 42; }
                         return 1;
                     }
                    """;
                var hosted = Compilation.Create(SyntaxTree.Parse(source), (BoundRuntimePolicy)policy);
                Assert.False(hosted.HasErrors, string.Join(Environment.NewLine, hosted.Diagnostics));
                Assert.Equal(42, NativeJit.Execute(Assert.IsType<BoundProgram>(hosted.Program), policy));
            }
            var program = await CompileParserAsync(text, entry, (BoundRuntimePolicy)policy).ConfigureAwait(true);
            Assert.Equal(42, NativeJit.Execute(program, policy));
            await ExecuteAotAsync(program, policy).ConfigureAwait(true);
        }
    }

    [Theory]
    [InlineData("int32 Main() { return 1 + 2; }", true, 3)]
    [InlineData("int32 Main() { return 2147483647 + 0; }", true, int.MaxValue)]
    [InlineData("int32 Main() { return 2147483647 + 1; }", false, 0)]
    [InlineData("int32 Main() { return 0xFFFF_FFFF + 1; }", true, 0)]
    [InlineData("int32 Main() { return 0x8000_0000 + 0; }", true, int.MinValue)]
    [InlineData("int32 Main() { return 2147483648 + 0; }", false, 0)]
    [InlineData("int32 Main() { return 1 - 2; }", false, 0)]
    [InlineData("uint32 Main() { return 1 + 2; }", false, 0)]
    public async Task BoundedNativeInt32AdditionUsesOwnedIntegerRecordsAsync(
        string text, bool evaluable, int value)
    {
        var assertion = evaluable
            ? FormattableString.Invariant($$"""
                require TryEvaluateInt32AddReturn(move tokens, move integers, out var name, out var bound);
                if (name.Start != 6 || name.Length != 4 || bound != {{value}}) { return 1; }
                """)
            : """
                if (TryEvaluateInt32AddReturn(move tokens, move integers,
                    out var name, out var bound)) { return 1; }
                """;
        var entry = $$"""
            using Terrum.CodeAnalysis;
            int32 Main() {
                require TryScanCompilerInput(init var tokens, init var trivia, init var integers);
                {{assertion}}
                return 42;
            }
            """;
        foreach (var policy in new[] { NativeRuntimePolicy.FailFast, NativeRuntimePolicy.CheckedExceptions })
        {
            if (evaluable)
            {
                var hosted = Compilation.Create(SyntaxTree.Parse(text), (BoundRuntimePolicy)policy);
                Assert.False(hosted.HasErrors, string.Join(Environment.NewLine, hosted.Diagnostics));
                Assert.Equal(value, NativeJit.Execute(Assert.IsType<BoundProgram>(hosted.Program), policy));
            }
            var program = await CompileParserAsync(text, entry, (BoundRuntimePolicy)policy).ConfigureAwait(true);
            Assert.Equal(42, NativeJit.Execute(program, policy));
            await ExecuteAotAsync(program, policy).ConfigureAwait(true);
        }
    }

    [Theory]
    [InlineData("uint32 Value() { return 1 + 2; }", true, 3u)]
    [InlineData("uint32 Value() { return 4294967295 + 0; }", true, uint.MaxValue)]
    [InlineData("uint32 Value() { return 0xFFFF_FFFF + 0; }", true, uint.MaxValue)]
    [InlineData("uint32 Value() { return 4294967295 + 1; }", false, 0u)]
    [InlineData("uint32 Value() { return 4294967296 + 0; }", false, 0u)]
    [InlineData("int32 Value() { return 1 + 2; }", false, 0u)]
    public async Task BoundedNativeUInt32AdditionUsesOwnedIntegerRecordsAsync(
        string text, bool evaluable, uint value)
    {
        var assertion = evaluable
            ? FormattableString.Invariant($$"""
                require TryEvaluateUInt32AddReturn(move tokens, move integers, out var name, out var bound);
                if (name.Start != 7 || name.Length != 5 || bound != {{value}}) { return 1; }
                """)
            : """
                if (TryEvaluateUInt32AddReturn(move tokens, move integers,
                    out var name, out var bound)) { return 1; }
                """;
        var entry = $$"""
            using Terrum.CodeAnalysis;
            int32 Main() {
                require TryScanCompilerInput(init var tokens, init var trivia, init var integers);
                {{assertion}}
                return 42;
            }
            """;
        foreach (var policy in new[] { NativeRuntimePolicy.FailFast, NativeRuntimePolicy.CheckedExceptions })
        {
            if (evaluable)
            {
                var source = text + FormattableString.Invariant(
                    $" int32 Main() {{ uint32 actual = Value(); if (actual == {value}) {{ return 42; }} return 1; }}");
                var hosted = Compilation.Create(SyntaxTree.Parse(source), (BoundRuntimePolicy)policy);
                Assert.False(hosted.HasErrors, string.Join(Environment.NewLine, hosted.Diagnostics));
                Assert.Equal(42, NativeJit.Execute(Assert.IsType<BoundProgram>(hosted.Program), policy));
            }
            var program = await CompileParserAsync(text, entry, (BoundRuntimePolicy)policy).ConfigureAwait(true);
            Assert.Equal(42, NativeJit.Execute(program, policy));
            await ExecuteAotAsync(program, policy).ConfigureAwait(true);
        }
    }

    [Theory]
    [InlineData("int64 Value() { return 4294967296 + 1; }", true, 4294967297L)]
    [InlineData("int64 Value() { return 9223372036854775807 + 0; }", true, long.MaxValue)]
    [InlineData("int64 Value() { return 0x8000_0000_0000_0000 + 0; }", true, long.MinValue)]
    [InlineData("int64 Value() { return 0xFFFF_FFFF_FFFF_FFFF + 1; }", true, 0L)]
    [InlineData("int64 Value() { return 9223372036854775807 + 1; }", false, 0L)]
    [InlineData("int64 Value() { return 0x8000_0000_0000_0000 + 0xFFFF_FFFF_FFFF_FFFF; }", false, 0L)]
    [InlineData("int64 Value() { return 9223372036854775808 + 0; }", false, 0L)]
    public async Task BoundedNativeInt64AdditionUsesOwnedIntegerRecordsAsync(
        string text, bool evaluable, long value)
    {
        var expected = value == long.MinValue ? "int64.MinValue"
            : value.ToString(CultureInfo.InvariantCulture);
        var assertion = evaluable
            ? $$"""
                require TryEvaluateInt64AddReturn(move tokens, move integers, out var name, out var bound);
                if (name.Start != 6 || name.Length != 5 || bound != {{expected}}) { return 1; }
                """
            : """
                if (TryEvaluateInt64AddReturn(move tokens, move integers,
                    out var name, out var bound)) { return 1; }
                """;
        var entry = $$"""
            using Terrum.CodeAnalysis;
            int32 Main() {
                require TryScanCompilerInput(init var tokens, init var trivia, init var integers);
                {{assertion}}
                return 42;
            }
            """;
        foreach (var policy in new[] { NativeRuntimePolicy.FailFast, NativeRuntimePolicy.CheckedExceptions })
        {
            if (evaluable)
            {
                var source = text + $$"""
                     int32 Main() {
                         int64 actual = Value();
                         if (actual == {{expected}}) { return 42; }
                         return 1;
                     }
                    """;
                var hosted = Compilation.Create(SyntaxTree.Parse(source), (BoundRuntimePolicy)policy);
                Assert.False(hosted.HasErrors, string.Join(Environment.NewLine, hosted.Diagnostics));
                Assert.Equal(42, NativeJit.Execute(Assert.IsType<BoundProgram>(hosted.Program), policy));
            }
            var program = await CompileParserAsync(text, entry, (BoundRuntimePolicy)policy).ConfigureAwait(true);
            Assert.Equal(42, NativeJit.Execute(program, policy));
            await ExecuteAotAsync(program, policy).ConfigureAwait(true);
        }
    }

    [Theory]
    [InlineData("uint64 Value() { return 4294967296 + 1; }", true, 4294967297ul)]
    [InlineData("uint64 Value() { return 18446744073709551615 + 0; }", true, ulong.MaxValue)]
    [InlineData("uint64 Value() { return 0xFFFF_FFFF_FFFF_FFFF + 0; }", true, ulong.MaxValue)]
    [InlineData("uint64 Value() { return 18446744073709551615 + 1; }", false, 0ul)]
    [InlineData("uint64 Value() { return 18446744073709551616 + 0; }", false, 0ul)]
    public async Task BoundedNativeUInt64AdditionUsesOwnedIntegerRecordsAsync(
        string text, bool evaluable, ulong value)
    {
        var assertion = evaluable
            ? FormattableString.Invariant($$"""
                require TryEvaluateUInt64AddReturn(move tokens, move integers, out var name, out var bound);
                if (name.Start != 7 || name.Length != 5 || bound != {{value}}) { return 1; }
                """)
            : """
                if (TryEvaluateUInt64AddReturn(move tokens, move integers,
                    out var name, out var bound)) { return 1; }
                """;
        var entry = $$"""
            using Terrum.CodeAnalysis;
            int32 Main() {
                require TryScanCompilerInput(init var tokens, init var trivia, init var integers);
                {{assertion}}
                return 42;
            }
            """;
        foreach (var policy in new[] { NativeRuntimePolicy.FailFast, NativeRuntimePolicy.CheckedExceptions })
        {
            if (evaluable)
            {
                var source = text + FormattableString.Invariant(
                    $" int32 Main() {{ uint64 actual = Value(); if (actual == {value}) {{ return 42; }} return 1; }}");
                var hosted = Compilation.Create(SyntaxTree.Parse(source), (BoundRuntimePolicy)policy);
                Assert.False(hosted.HasErrors, string.Join(Environment.NewLine, hosted.Diagnostics));
                Assert.Equal(42, NativeJit.Execute(Assert.IsType<BoundProgram>(hosted.Program), policy));
            }
            var program = await CompileParserAsync(text, entry, (BoundRuntimePolicy)policy).ConfigureAwait(true);
            Assert.Equal(42, NativeJit.Execute(program, policy));
            await ExecuteAotAsync(program, policy).ConfigureAwait(true);
        }
    }

    [Theory]
    [InlineData("int32", "8 - 3", true, "5")]
    [InlineData("int32", "0 - 0xFFFF_FFFF", true, "1")]
    [InlineData("int32", "0x8000_0000 - 0x8000_0000", true, "0")]
    [InlineData("int32", "0x8000_0000 - 1", false, "0")]
    [InlineData("int32", "2147483647 - 0xFFFF_FFFF", false, "0")]
    [InlineData("int32", "2147483648 - 0", false, "0")]
    [InlineData("uint32", "8 - 3", true, "5")]
    [InlineData("uint32", "0xFFFF_FFFF - 1", true, "4294967294")]
    [InlineData("uint32", "0 - 1", false, "0")]
    [InlineData("uint32", "4294967296 - 0", false, "0")]
    [InlineData("int64", "4294967296 - 1", true, "4294967295")]
    [InlineData("int64", "0x8000_0000_0000_0000 - 0xFFFF_FFFF_FFFF_FFFF", true, "-9223372036854775807")]
    [InlineData("int64", "0x8000_0000_0000_0000 - 1", false, "0")]
    [InlineData("int64", "9223372036854775807 - 0xFFFF_FFFF_FFFF_FFFF", false, "0")]
    [InlineData("int64", "9223372036854775808 - 0", false, "0")]
    [InlineData("uint64", "4294967296 - 1", true, "4294967295")]
    [InlineData("uint64", "18446744073709551615 - 1", true, "18446744073709551614")]
    [InlineData("uint64", "0 - 1", false, "0")]
    [InlineData("uint64", "18446744073709551616 - 0", false, "0")]
    public async Task BoundedNativeSubtractionUsesOwnedIntegerRecordsAsync(
        string typeName, string expression, bool evaluable, string expected)
    {
        var evaluator = typeName switch
        {
            "int32" => "TryEvaluateInt32SubtractReturn",
            "uint32" => "TryEvaluateUInt32SubtractReturn",
            "int64" => "TryEvaluateInt64SubtractReturn",
            "uint64" => "TryEvaluateUInt64SubtractReturn",
            _ => throw new ArgumentOutOfRangeException(nameof(typeName)),
        };
        var text = $"{typeName} Value() {{ return {expression}; }}";
        var assertion = evaluable
            ? $$"""
                require {{evaluator}}(move tokens, move integers, out var name, out var bound);
                if (name.Start != {{typeName.Length + 1}} || name.Length != 5
                    || bound != {{expected}}) { return 1; }
                """
            : $$"""
                if ({{evaluator}}(move tokens, move integers,
                    out var name, out var bound)) { return 1; }
                """;
        var entry = $$"""
            using Terrum.CodeAnalysis;
            int32 Main() {
                require TryScanCompilerInput(init var tokens, init var trivia, init var integers);
                {{assertion}}
                return 42;
            }
            """;
        foreach (var policy in new[] { NativeRuntimePolicy.FailFast, NativeRuntimePolicy.CheckedExceptions })
        {
            if (evaluable)
            {
                var source = text + $$"""
                     int32 Main() {
                         {{typeName}} actual = Value();
                         if (actual == {{expected}}) { return 42; }
                         return 1;
                     }
                    """;
                var hosted = Compilation.Create(SyntaxTree.Parse(source), (BoundRuntimePolicy)policy);
                Assert.False(hosted.HasErrors, string.Join(Environment.NewLine, hosted.Diagnostics));
                Assert.Equal(42, NativeJit.Execute(Assert.IsType<BoundProgram>(hosted.Program), policy));
            }
            var program = await CompileParserAsync(text, entry, (BoundRuntimePolicy)policy).ConfigureAwait(true);
            Assert.Equal(42, NativeJit.Execute(program, policy));
            await ExecuteAotAsync(program, policy).ConfigureAwait(true);
        }
    }

    [Theory]
    [InlineData("int32", "0xFFFF_FFFF * 2", true, "-2")]
    [InlineData("int32", "46340 * 46340", true, "2147395600")]
    [InlineData("int32", "46341 * 46341", false, "0")]
    [InlineData("int32", "2147483648 * 0", false, "0")]
    [InlineData("uint32", "65535 * 65537", true, "4294967295")]
    [InlineData("uint32", "65536 * 65536", false, "0")]
    [InlineData("uint32", "4294967295 * 0", true, "0")]
    [InlineData("int64", "0x8000_0000_0000_0000 * 1", true, "-9223372036854775808")]
    [InlineData("int64", "0x8000_0000_0000_0000 * 0xFFFF_FFFF_FFFF_FFFF", false, "0")]
    [InlineData("int64", "0xFFFF_FFFF_FFFF_FFFF * 2", true, "-2")]
    [InlineData("int64", "3037000499 * 3037000499", true, "9223372030926249001")]
    [InlineData("int64", "3037000500 * 3037000500", false, "0")]
    [InlineData("uint64", "4294967295 * 4294967297", true, "18446744073709551615")]
    [InlineData("uint64", "4294967296 * 4294967296", false, "0")]
    [InlineData("uint64", "18446744073709551615 * 0", true, "0")]
    [InlineData("uint64", "18446744073709551616 * 0", false, "0")]
    public async Task BoundedNativeMultiplicationUsesOwnedIntegerRecordsAsync(
        string typeName, string expression, bool evaluable, string expected)
    {
        var evaluator = typeName switch
        {
            "int32" => "TryEvaluateInt32MultiplyReturn",
            "uint32" => "TryEvaluateUInt32MultiplyReturn",
            "int64" => "TryEvaluateInt64MultiplyReturn",
            "uint64" => "TryEvaluateUInt64MultiplyReturn",
            _ => throw new ArgumentOutOfRangeException(nameof(typeName)),
        };
        var text = $"{typeName} Value() {{ return {expression}; }}";
        var assertion = evaluable
            ? $$"""
                require {{evaluator}}(move tokens, move integers, out var name, out var bound);
                if (name.Start != {{typeName.Length + 1}} || name.Length != 5
                    || bound != {{expected}}) { return 1; }
                """
            : $$"""
                if ({{evaluator}}(move tokens, move integers,
                    out var name, out var bound)) { return 1; }
                """;
        var entry = $$"""
            using Terrum.CodeAnalysis;
            int32 Main() {
                require TryScanCompilerInput(init var tokens, init var trivia, init var integers);
                {{assertion}}
                return 42;
            }
            """;
        foreach (var policy in new[] { NativeRuntimePolicy.FailFast, NativeRuntimePolicy.CheckedExceptions })
        {
            if (evaluable)
            {
                var source = text + $$"""
                     int32 Main() {
                         {{typeName}} actual = Value();
                         if (actual == {{expected}}) { return 42; }
                         return 1;
                     }
                    """;
                var hosted = Compilation.Create(SyntaxTree.Parse(source), (BoundRuntimePolicy)policy);
                Assert.False(hosted.HasErrors, string.Join(Environment.NewLine, hosted.Diagnostics));
                Assert.Equal(42, NativeJit.Execute(Assert.IsType<BoundProgram>(hosted.Program), policy));
            }
            var program = await CompileParserAsync(text, entry, (BoundRuntimePolicy)policy).ConfigureAwait(true);
            Assert.Equal(42, NativeJit.Execute(program, policy));
            await ExecuteAotAsync(program, policy).ConfigureAwait(true);
        }
    }

    [Theory]
    [InlineData("int32", "7 / 2", true, "3")]
    [InlineData("int32", "0xFFFF_FFFF / 2", true, "0")]
    [InlineData("int32", "7 % 2", true, "1")]
    [InlineData("int32", "0xFFFF_FFFF % 2", true, "-1")]
    [InlineData("int32", "0x8000_0000 / 0xFFFF_FFFF", false, "0")]
    [InlineData("int32", "0x8000_0000 % 0xFFFF_FFFF", true, "0")]
    [InlineData("uint32", "4294967295 / 2", true, "2147483647")]
    [InlineData("uint32", "4294967295 % 2", true, "1")]
    [InlineData("uint32", "7 / 0", false, "0")]
    [InlineData("int64", "0x8000_0000_0000_0000 / 2", true, "-4611686018427387904")]
    [InlineData("int64", "0xFFFF_FFFF_FFFF_FFFF % 2", true, "-1")]
    [InlineData("int64", "0x8000_0000_0000_0000 / 0xFFFF_FFFF_FFFF_FFFF", false, "0")]
    [InlineData("int64", "0x8000_0000_0000_0000 % 0xFFFF_FFFF_FFFF_FFFF", true, "0")]
    [InlineData("uint64", "18446744073709551615 / 2", true, "9223372036854775807")]
    [InlineData("uint64", "18446744073709551615 % 2", true, "1")]
    [InlineData("uint64", "18446744073709551616 / 2", false, "0")]
    [InlineData("uint64", "7 % 0", false, "0")]
    public async Task BoundedNativeDivisionAndRemainderUseOwnedIntegerRecordsAsync(
        string typeName, string expression, bool evaluable, string expected)
    {
        var width = typeName switch
        {
            "int32" => "Int32",
            "uint32" => "UInt32",
            "int64" => "Int64",
            "uint64" => "UInt64",
            _ => throw new ArgumentOutOfRangeException(nameof(typeName)),
        };
        var operation = expression.Contains('/') ? "Divide" : "Remainder";
        var evaluator = $"TryEvaluate{width}{operation}Return";
        var text = $"{typeName} Value() {{ return {expression}; }}";
        var assertion = evaluable
            ? $$"""
                require {{evaluator}}(move tokens, move integers, out var name, out var bound);
                if (name.Start != {{typeName.Length + 1}} || name.Length != 5
                    || bound != {{expected}}) { return 1; }
                """
            : $$"""
                if ({{evaluator}}(move tokens, move integers,
                    out var name, out var bound)) { return 1; }
                """;
        var entry = $$"""
            using Terrum.CodeAnalysis;
            int32 Main() {
                require TryScanCompilerInput(init var tokens, init var trivia, init var integers);
                {{assertion}}
                return 42;
            }
            """;
        foreach (var policy in new[] { NativeRuntimePolicy.FailFast, NativeRuntimePolicy.CheckedExceptions })
        {
            if (evaluable)
            {
                var source = text + $$"""
                     int32 Main() {
                         {{typeName}} actual = Value();
                         if (actual == {{expected}}) { return 42; }
                         return 1;
                     }
                    """;
                var hosted = Compilation.Create(SyntaxTree.Parse(source), (BoundRuntimePolicy)policy);
                Assert.False(hosted.HasErrors, string.Join(Environment.NewLine, hosted.Diagnostics));
                Assert.Equal(42, NativeJit.Execute(Assert.IsType<BoundProgram>(hosted.Program), policy));
            }
            var program = await CompileParserAsync(text, entry, (BoundRuntimePolicy)policy).ConfigureAwait(true);
            Assert.Equal(42, NativeJit.Execute(program, policy));
            await ExecuteAotAsync(program, policy).ConfigureAwait(true);
        }
    }

    [Theory]
    [InlineData("int32", "0xFFFF_FFFF & 0xAAAA_AAAA", "-1431655766")]
    [InlineData("int32", "0x8000_0000 | 1", "-2147483647")]
    [InlineData("int32", "0xFFFF_FFFF ^ 0xAAAA_AAAA", "1431655765")]
    [InlineData("uint32", "0xFFFF_FFFF & 0xAAAA_AAAA", "2863311530")]
    [InlineData("uint32", "0x8000_0000 | 1", "2147483649")]
    [InlineData("uint32", "0xFFFF_FFFF ^ 0xAAAA_AAAA", "1431655765")]
    [InlineData("int64", "0xFFFF_FFFF_FFFF_FFFF & 0xAAAA_AAAA_AAAA_AAAA", "-6148914691236517206")]
    [InlineData("int64", "0x8000_0000_0000_0000 | 1", "-9223372036854775807")]
    [InlineData("int64", "0xFFFF_FFFF_FFFF_FFFF ^ 0xAAAA_AAAA_AAAA_AAAA", "6148914691236517205")]
    [InlineData("uint64", "0xFFFF_FFFF_FFFF_FFFF & 0xAAAA_AAAA_AAAA_AAAA", "12297829382473034410")]
    [InlineData("uint64", "0x8000_0000_0000_0000 | 1", "9223372036854775809")]
    [InlineData("uint64", "0xFFFF_FFFF_FFFF_FFFF ^ 0xAAAA_AAAA_AAAA_AAAA", "6148914691236517205")]
    public async Task BoundedNativeBitwiseOperationsUseOwnedIntegerRecordsAsync(
        string typeName, string expression, string expected)
    {
        var width = typeName switch
        {
            "int32" => "Int32",
            "uint32" => "UInt32",
            "int64" => "Int64",
            "uint64" => "UInt64",
            _ => throw new ArgumentOutOfRangeException(nameof(typeName)),
        };
        var operation = expression.Contains('&') ? "And"
            : expression.Contains('|') ? "Or" : "Xor";
        var evaluator = $"TryEvaluate{width}{operation}Return";
        var text = $"{typeName} Value() {{ return {expression}; }}";
        var entry = $$"""
            using Terrum.CodeAnalysis;
            int32 Main() {
                require TryScanCompilerInput(init var tokens, init var trivia, init var integers);
                require {{evaluator}}(move tokens, move integers, out var name, out var bound);
                if (name.Start != {{typeName.Length + 1}} || name.Length != 5
                    || bound != {{expected}}) { return 1; }
                return 42;
            }
            """;
        foreach (var policy in new[] { NativeRuntimePolicy.FailFast, NativeRuntimePolicy.CheckedExceptions })
        {
            var source = text + $$"""
                 int32 Main() {
                     {{typeName}} actual = Value();
                     if (actual == {{expected}}) { return 42; }
                     return 1;
                 }
                """;
            var hosted = Compilation.Create(SyntaxTree.Parse(source), (BoundRuntimePolicy)policy);
            Assert.False(hosted.HasErrors, string.Join(Environment.NewLine, hosted.Diagnostics));
            Assert.Equal(42, NativeJit.Execute(Assert.IsType<BoundProgram>(hosted.Program), policy));
            var program = await CompileParserAsync(text, entry, (BoundRuntimePolicy)policy).ConfigureAwait(true);
            Assert.Equal(42, NativeJit.Execute(program, policy));
            await ExecuteAotAsync(program, policy).ConfigureAwait(true);
        }
    }

    [Theory]
    [InlineData("int32 Answer() { return 42; }", true, 42, 0)]
    [InlineData("int32 Answer() { return -42; }", true, 42, 0)]
    [InlineData("int32 Answer() { return +42; }", true, 42, 0)]
    [InlineData("int32 Answer() { return ~42; }", true, 42, 0)]
    [InlineData("int32 Answer() { return 0xFFFF_FFFF; }", true, -1, 0)]
    [InlineData("int32 Answer() { return 18446744073709551616; }", false, 0, 0)]
    [InlineData("int32 Answer() { return 1.5; }", false, 0, 0)]
    [InlineData("int32 Answer() { return (42); }", true, 42, 0)]
    [InlineData("int32 Answer() { return (-42); }", true, 42, 0)]
    [InlineData("int32 Answer() { return -(42); }", true, 42, 0)]
    public async Task BoundedParserSelectsOwnedIntegerRecordsAsync(
        string text, bool accepted, int low, int high)
    {
        var hosted = SyntaxTree.Parse(text);
        var name = hosted.Tokens[1].Span;
        var result = hosted.Root.DescendantNodes().Single(node => node.Kind == SyntaxKind.ReturnStatement);
        var expression = ((SyntaxNode)result.Children[1]).Span;
        var assertion = accepted
            ? FormattableString.Invariant($$"""
                require TryParseScalarReturnInteger(move tokens, move integers,
                    out var name, out var expression, out var low, out var high);
                if (name.Start != {{name.Start}} || name.Length != {{name.Length}}
                    || expression.Start != {{expression.Start}} || expression.Length != {{expression.Length}}
                    || low != {{low}} || high != {{high}}) { return 1; }
                """)
            : """
                if (TryParseScalarReturnInteger(move tokens, move integers,
                    out var name, out var expression, out var low, out var high)) { return 1; }
                """;
        var entry = $$"""
            using Terrum.CodeAnalysis;
            int32 Main() {
                require TryScanCompilerInput(init var tokens, init var trivia, init var integers);
                {{assertion}}
                return 42;
            }
            """;
        foreach (var policy in new[] { NativeRuntimePolicy.FailFast, NativeRuntimePolicy.CheckedExceptions })
        {
            var program = await CompileParserAsync(text, entry, (BoundRuntimePolicy)policy).ConfigureAwait(true);
            Assert.Equal(42, NativeJit.Execute(program, policy));
            await ExecuteAotAsync(program, policy).ConfigureAwait(true);
        }
    }

    [Theory]
    [InlineData("int32 Main() { return 2147483647; }", true, int.MaxValue)]
    [InlineData("int32 Main() { return +2147483647; }", true, int.MaxValue)]
    [InlineData("int32 Main() { return (2147483647); }", true, int.MaxValue)]
    [InlineData("int32 Main() { return (-2147483648); }", true, int.MinValue)]
    [InlineData("int32 Main() { return -(42); }", true, -42)]
    [InlineData("int32 Main() { return -(2147483648); }", false, 0)]
    [InlineData("int32 Main() { return +0xFFFF_FFFF; }", true, -1)]
    [InlineData("int32 Main() { return ~0; }", true, -1)]
    [InlineData("int32 Main() { return ~(0x8000_0000); }", true, int.MaxValue)]
    [InlineData("int32 Main() { return -2147483648; }", true, int.MinValue)]
    [InlineData("int32 Main() { return - 2_147_483_648; }", true, int.MinValue)]
    [InlineData("int32 Main() { return 2147483648; }", false, 0)]
    [InlineData("int32 Main() { return +2147483648; }", false, 0)]
    [InlineData("int32 Main() { return -2147483649; }", false, 0)]
    [InlineData("int32 Main() { return 0xFFFF_FFFF; }", true, -1)]
    [InlineData("int32 Main() { return 0x0000_0000_FFFF_FFFF; }", true, -1)]
    [InlineData("int32 Main() { return 0b1000_0000_0000_0000_0000_0000_0000_0000; }", true, int.MinValue)]
    [InlineData("int32 Main() { return -0x8000_0000; }", false, 0)]
    [InlineData("int32 Main() { return -0xFFFF_FFFF; }", true, 1)]
    [InlineData("int32 Main() { return 0x1_0000_0000; }", false, 0)]
    [InlineData("int32 Main() { return 1.5; }", false, 0)]
    public async Task BoundedNativeInt32EvaluationMatchesHostedExecutedValuesAsync(
        string text, bool evaluable, int value)
    {
        var assertion = evaluable
            ? FormattableString.Invariant($$"""
                require TryEvaluateInt32ScalarReturn(move tokens, move integers, out var name, out var bound);
                if (name.Start != 6 || name.Length != 4 || bound != {{value}}) { return 1; }
                """)
            : """
                if (TryEvaluateInt32ScalarReturn(move tokens, move integers,
                    out var name, out var bound)) { return 1; }
                """;
        var entry = $$"""
            using Terrum.CodeAnalysis;
            int32 Main() {
                require TryScanCompilerInput(init var tokens, init var trivia, init var integers);
                {{assertion}}
                return 42;
            }
            """;
        foreach (var policy in new[] { NativeRuntimePolicy.FailFast, NativeRuntimePolicy.CheckedExceptions })
        {
            if (evaluable)
            {
                var hosted = Compilation.Create(SyntaxTree.Parse(text), (BoundRuntimePolicy)policy);
                Assert.False(hosted.HasErrors, string.Join(Environment.NewLine, hosted.Diagnostics));
                Assert.Equal(value, NativeJit.Execute(Assert.IsType<BoundProgram>(hosted.Program), policy));
            }
            var program = await CompileParserAsync(text, entry, (BoundRuntimePolicy)policy).ConfigureAwait(true);
            Assert.Equal(42, NativeJit.Execute(program, policy));
            await ExecuteAotAsync(program, policy).ConfigureAwait(true);
        }
    }

    [Theory]
    [InlineData("uint32 Value() { return 0; }", true, 0u)]
    [InlineData("uint32 Value() { return 4294967295; }", true, uint.MaxValue)]
    [InlineData("uint32 Value() { return +4294967295; }", true, uint.MaxValue)]
    [InlineData("uint32 Value() { return (4294967295); }", true, uint.MaxValue)]
    [InlineData("uint32 Value() { return (+4294967295); }", true, uint.MaxValue)]
    [InlineData("uint32 Value() { return +(4294967295); }", true, uint.MaxValue)]
    [InlineData("uint32 Value() { return ~0; }", true, uint.MaxValue)]
    [InlineData("uint32 Value() { return (~4294967295); }", true, 0u)]
    [InlineData("uint32 Value() { return (-1); }", false, 0u)]
    [InlineData("uint32 Value() { return 0xFFFF_FFFF; }", true, uint.MaxValue)]
    [InlineData("uint32 Value() { return 0x0000_0000_FFFF_FFFF; }", true, uint.MaxValue)]
    [InlineData("uint32 Value() { return 4294967296; }", false, 0u)]
    [InlineData("uint32 Value() { return 0x1_0000_0000; }", false, 0u)]
    [InlineData("uint32 Value() { return -1; }", false, 0u)]
    [InlineData("uint32 Value() { return 1.5; }", false, 0u)]
    public async Task BoundedNativeUInt32EvaluationMatchesHostedExecutedValuesAsync(
        string text, bool evaluable, uint value)
    {
        var assertion = evaluable
            ? FormattableString.Invariant($$"""
                require TryEvaluateUInt32ScalarReturn(move tokens, move integers,
                    out var name, out var bound);
                if (name.Start != 7 || name.Length != 5 || bound != {{value}}) { return 1; }
                """)
            : """
                if (TryEvaluateUInt32ScalarReturn(move tokens, move integers,
                    out var name, out var bound)) { return 1; }
                """;
        var entry = $$"""
            using Terrum.CodeAnalysis;
            int32 Main() {
                require TryScanCompilerInput(init var tokens, init var trivia, init var integers);
                {{assertion}}
                return 42;
            }
            """;
        foreach (var policy in new[] { NativeRuntimePolicy.FailFast, NativeRuntimePolicy.CheckedExceptions })
        {
            if (evaluable)
            {
                var source = text + FormattableString.Invariant(
                    $" int32 Main() {{ uint32 actual = Value(); if (actual == {value}) {{ return 42; }} return 1; }}");
                var hosted = Compilation.Create(SyntaxTree.Parse(source), (BoundRuntimePolicy)policy);
                Assert.False(hosted.HasErrors, string.Join(Environment.NewLine, hosted.Diagnostics));
                Assert.Equal(42, NativeJit.Execute(Assert.IsType<BoundProgram>(hosted.Program), policy));
            }
            var program = await CompileParserAsync(text, entry, (BoundRuntimePolicy)policy).ConfigureAwait(true);
            Assert.Equal(42, NativeJit.Execute(program, policy));
            await ExecuteAotAsync(program, policy).ConfigureAwait(true);
        }
    }

    [Theory]
    [InlineData("uint64 Value() { return 0; }", true, 0ul)]
    [InlineData("uint64 Value() { return 4294967296; }", true, 4294967296ul)]
    [InlineData("uint64 Value() { return 9223372036854775808; }", true, 9223372036854775808ul)]
    [InlineData("uint64 Value() { return 18446744073709551615; }", true, ulong.MaxValue)]
    [InlineData("uint64 Value() { return +18446744073709551615; }", true, ulong.MaxValue)]
    [InlineData("uint64 Value() { return (18446744073709551615); }", true, ulong.MaxValue)]
    [InlineData("uint64 Value() { return (+18446744073709551615); }", true, ulong.MaxValue)]
    [InlineData("uint64 Value() { return +(18446744073709551615); }", true, ulong.MaxValue)]
    [InlineData("uint64 Value() { return ~0; }", true, ulong.MaxValue)]
    [InlineData("uint64 Value() { return ~(18446744073709551615); }", true, 0ul)]
    [InlineData("uint64 Value() { return (-1); }", false, 0ul)]
    [InlineData("uint64 Value() { return 0xFFFF_FFFF_FFFF_FFFF; }", true, ulong.MaxValue)]
    [InlineData("uint64 Value() { return 18446744073709551616; }", false, 0ul)]
    [InlineData("uint64 Value() { return -1; }", false, 0ul)]
    public async Task BoundedNativeUInt64EvaluationMatchesHostedExecutedValuesAsync(
        string text, bool evaluable, ulong value)
    {
        var assertion = evaluable
            ? FormattableString.Invariant($$"""
                require TryEvaluateUInt64ScalarReturn(move tokens, move integers,
                    out var name, out var bound);
                if (name.Start != 7 || name.Length != 5 || bound != {{value}}) { return 1; }
                """)
            : """
                if (TryEvaluateUInt64ScalarReturn(move tokens, move integers,
                    out var name, out var bound)) { return 1; }
                """;
        var entry = $$"""
            using Terrum.CodeAnalysis;
            int32 Main() {
                require TryScanCompilerInput(init var tokens, init var trivia, init var integers);
                {{assertion}}
                return 42;
            }
            """;
        foreach (var policy in new[] { NativeRuntimePolicy.FailFast, NativeRuntimePolicy.CheckedExceptions })
        {
            if (evaluable)
            {
                var source = text + FormattableString.Invariant(
                    $" int32 Main() {{ uint64 actual = Value(); if (actual == {value}) {{ return 42; }} return 1; }}");
                var hosted = Compilation.Create(SyntaxTree.Parse(source), (BoundRuntimePolicy)policy);
                Assert.False(hosted.HasErrors, string.Join(Environment.NewLine, hosted.Diagnostics));
                Assert.Equal(42, NativeJit.Execute(Assert.IsType<BoundProgram>(hosted.Program), policy));
            }
            var program = await CompileParserAsync(text, entry, (BoundRuntimePolicy)policy).ConfigureAwait(true);
            Assert.Equal(42, NativeJit.Execute(program, policy));
            await ExecuteAotAsync(program, policy).ConfigureAwait(true);
        }
    }

    [Theory]
    [InlineData("int64 Value() { return 0; }", true, 0L)]
    [InlineData("int64 Value() { return 9223372036854775807; }", true, long.MaxValue)]
    [InlineData("int64 Value() { return +9223372036854775807; }", true, long.MaxValue)]
    [InlineData("int64 Value() { return (9223372036854775807); }", true, long.MaxValue)]
    [InlineData("int64 Value() { return (-9223372036854775808); }", true, long.MinValue)]
    [InlineData("int64 Value() { return -(9223372036854775807); }", true, -long.MaxValue)]
    [InlineData("int64 Value() { return -(9223372036854775808); }", false, 0L)]
    [InlineData("int64 Value() { return +0xFFFF_FFFF_FFFF_FFFF; }", true, -1L)]
    [InlineData("int64 Value() { return ~0; }", true, -1L)]
    [InlineData("int64 Value() { return (~0x8000_0000_0000_0000); }", true, long.MaxValue)]
    [InlineData("int64 Value() { return -9223372036854775808; }", true, long.MinValue)]
    [InlineData("int64 Value() { return - 9_223_372_036_854_775_808; }", true, long.MinValue)]
    [InlineData("int64 Value() { return 9223372036854775808; }", false, 0L)]
    [InlineData("int64 Value() { return +9223372036854775808; }", false, 0L)]
    [InlineData("int64 Value() { return -9223372036854775809; }", false, 0L)]
    [InlineData("int64 Value() { return 0x8000_0000_0000_0000; }", true, long.MinValue)]
    [InlineData("int64 Value() { return 0xFFFF_FFFF_FFFF_FFFF; }", true, -1L)]
    [InlineData("int64 Value() { return -0xFFFF_FFFF_FFFF_FFFF; }", true, 1L)]
    [InlineData("int64 Value() { return -0x8000_0000_0000_0000; }", false, 0L)]
    [InlineData("int64 Value() { return 18446744073709551616; }", false, 0L)]
    [InlineData("int64 Value() { return 0x1_0000_0000_0000_0000; }", false, 0L)]
    public async Task BoundedNativeInt64EvaluationMatchesHostedExecutedValuesAsync(
        string text, bool evaluable, long value)
    {
        var expected = value == long.MinValue ? "int64.MinValue"
            : value.ToString(CultureInfo.InvariantCulture);
        var assertion = evaluable
            ? $$"""
                require TryEvaluateInt64ScalarReturn(move tokens, move integers,
                    out var name, out var bound);
                if (name.Start != 6 || name.Length != 5 || bound != {{expected}}) { return 1; }
                """
            : """
                if (TryEvaluateInt64ScalarReturn(move tokens, move integers,
                    out var name, out var bound)) { return 1; }
                """;
        var entry = $$"""
            using Terrum.CodeAnalysis;
            int32 Main() {
                require TryScanCompilerInput(init var tokens, init var trivia, init var integers);
                {{assertion}}
                return 42;
            }
            """;
        foreach (var policy in new[] { NativeRuntimePolicy.FailFast, NativeRuntimePolicy.CheckedExceptions })
        {
            if (evaluable)
            {
                var source = text + $$"""
                     int32 Main() {
                         int64 actual = Value();
                         if (actual == {{expected}}) { return 42; }
                         return 1;
                     }
                    """;
                var hosted = Compilation.Create(SyntaxTree.Parse(source), (BoundRuntimePolicy)policy);
                Assert.False(hosted.HasErrors, string.Join(Environment.NewLine, hosted.Diagnostics));
                Assert.Equal(42, NativeJit.Execute(Assert.IsType<BoundProgram>(hosted.Program), policy));
            }
            var program = await CompileParserAsync(text, entry, (BoundRuntimePolicy)policy).ConfigureAwait(true);
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

    private static Task<BoundProgram> CompileParserAsync(string text, string entry, BoundRuntimePolicy policy) =>
        CompileAsync(text, entry, policy, "ScalarFunctionParser");

    private static Task<BoundProgram> CompilePairParserAsync(string text, string entry, BoundRuntimePolicy policy) =>
        CompileAsync(text, entry, policy, "ScalarParameterPairParser");

    private static async Task<BoundProgram> CompileAsync(string text, string entry,
        BoundRuntimePolicy policy, string? parserName = null)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
        string[] names = ["SourceRange", "Token", "ManagedTokenChain", "Scanner", "ManagedScanner",
            "TokenSnapshot", "TokenTriviaSnapshot", "TokenIntegerSnapshot",
            "TokenDiagnosticSnapshot"];
        if (parserName is not null)
        {
            names = [.. names, "ScalarParserTypes", parserName];
        }
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
