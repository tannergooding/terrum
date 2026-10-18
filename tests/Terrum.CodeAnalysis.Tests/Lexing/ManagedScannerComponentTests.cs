// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Collections.Immutable;
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
using NUnit.Framework;

using Terrum.CodeAnalysis.Tests.Infrastructure;

namespace Terrum.CodeAnalysis.Tests.Lexing;

public sealed partial class ManagedScannerComponentTests
{
    [TestCase("@await",true)]
    [TestCase("@union",true)]
    [TestCase("@ordinaryName",false)]
    [TestCase("@int32",false)]
    [TestCase("@out",false)]
    [TestCase("x@await",false)]
    [TestCase("@await_",false)]
    [TestCase("@ await",false)]
    [TestCase("@_",false)]
    [TestCase("_",false)]
    public async Task KeywordEscapeSnapshotsAuditHandAuthoredRecordsAsync(string text, bool valid)
    {
        var entry = $$"""
            using Terrum.CodeAnalysis;
            int32 Main() {
                Host.Diagnostics[0] = 0;
                Host.Tokens[0] = 2;
                Host.Tokens[1] = 0;
                Host.Tokens[2] = {{text.Length}};
                Host.Tokens[3] = 0;
                Host.Tokens[4] = {{text.Length}};
                Host.Tokens[5] = 0;
                if (TokenSnapshot.TryBuild(2, init var owned) != {{(valid ? "true" : "false")}}) { return 1; }
                if (TryBuildManagedTokenChain(2, init var managed) != {{(valid ? "true" : "false")}}) { return 2; }
                return 42;
            }
            """;
        foreach (var policy in new[] { NativeRuntimePolicy.FailFast, NativeRuntimePolicy.CheckedExceptions })
        {
            var program = await CompileAsync(text, entry, (BoundRuntimePolicy)policy);
            Assert.That(NativeJit.Execute(program, policy), Is.EqualTo(42));
            await ExecuteAotAsync(program, policy);
        }
    }
    [TestCase("",false)]
    [TestCase("// trivia\r\nalpha 42",false)]
    [TestCase("//* *// /// heading\nalpha",false)]
    [TestCase("//* */",true)]
    [TestCase("/* **/ */",true)]
    [TestCase(".. ..^ ?.",false)]
    [TestCase("\U00010400 42",false)]
    [TestCase("\uD800",true)]
    [TestCase("a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a",true)]
    public async Task PublishedTokensMatchHostAndFailedScansDoNotPublishAsync(string text, bool fails)
    {
        var lexical = Lexer.Lex(SourceText.From(text));
        var expected = lexical.Tokens.ToArray();
        Assert.That(fails, Is.EqualTo(lexical.Diagnostics.Length != 0 || expected.Length > 64));
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
                    $"if (!chain.TryGet({index}, init var token{index}) || token{index}.Kind != {(int)expected[index].Kind} || token{index}.Range.Start != {expected[index].Span.Start} || token{index}.Range.Length != {expected[index].Span.Length}) {{ return 4; }}");
            }
            _ = source.Append(CultureInfo.InvariantCulture,
                $"if (chain.TryGet({expected.Length}, init var copied)) {{ return 9; }}"
                + $" require chain.TryGet(0, init copied);"
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
            Assert.That(NativeJit.Execute(program, policy), Is.EqualTo(42));
            await ExecuteAotAsync(program, policy).ConfigureAwait(true);
        }
    }
    [TestCase(63,false)]
    [TestCase(64,true)]
    public Task CapacityCountsTheEndOfFileTokenAsync(int identifiers, bool fails) =>
        PublishedTokensMatchHostAndFailedScansDoNotPublishAsync(
            string.Join(" ", Enumerable.Repeat("a", identifiers)), fails);
    [TestCase("")]
    [TestCase("alpha 1i32")]
    [TestCase("0x")]
    [TestCase("\uD800")]
    [TestCase("\"unterminated\n\"\\q\"")]
    [TestCase("/* **/ */")]
    [TestCase("!!")]
    [TestCase("\U00010400 +++")]
    public async Task OwnedDiagnosticsRemainAvailableOnLexicalFailureAsync(string text)
    {
        var expected = Lexer.Lex(SourceText.From(text)).Diagnostics;
        var entry = new StringBuilder("""
            using Terrum.CodeAnalysis;
            int32 Main() {
                require TokenDiagnosticSnapshot.TryScan(init var diagnostics);
            """);
        _ = entry.Append(CultureInfo.InvariantCulture,
            $"if (diagnostics.GetCount() != {expected.Length} || diagnostics.TryGet(-1, init var invalidCode, init var invalidSpan) || diagnostics.TryGet({expected.Length}, init var beyondCode, init var beyondSpan)) {{ return 1; }}");
        if (expected.Length != 0)
        {
            _ = entry.Append("if (TokenSnapshot.TryBuild(1, init var invalidTokens)) { return 3; } Host.Diagnostics[1] = -1;");
        }
        for (var index = 0; index < expected.Length; index++)
        {
            var diagnostic = expected[index];
            var code = int.Parse(diagnostic.Code.AsSpan(3), CultureInfo.InvariantCulture);
            _ = entry.Append(CultureInfo.InvariantCulture,
                $"require diagnostics.TryGet({index}, init var code{index}, init var span{index});"
                + $" if (code{index} != {code} || span{index}.Start != {diagnostic.Span.Start}"
                + $" || span{index}.Length != {diagnostic.Span.Length}) {{ return 2; }}");
        }
        _ = entry.Append("return 42; }");

        foreach (var policy in new[] { NativeRuntimePolicy.FailFast, NativeRuntimePolicy.CheckedExceptions })
        {
            var program = await CompileAsync(text, entry.ToString(), (BoundRuntimePolicy)policy).ConfigureAwait(true);
            Assert.That(NativeJit.Execute(program, policy), Is.EqualTo(42));
            await ExecuteAotAsync(program, policy).ConfigureAwait(true);
        }
    }
    [TestCase("\uD800\r\uD800")]
    [TestCase("a\r\n\uD800\n\uD800")]
    [TestCase("a\uD800\rx\uD800")]
    [TestCase("\uD800\r\n\"unterminated")]
    public async Task DiagnosticLocationSnapshotMapsHostDiagnosticsAcrossLineBreaksAsync(string text)
    {
        var expected = Lexer.Lex(SourceText.From(text)).Diagnostics;
        Assert.That(expected, Is.Not.Empty);

        var entry = new StringBuilder("""
            using Terrum.CodeAnalysis;
            int32 Main() {
                if (!TokenDiagnosticLocationSnapshot.TryScan(init var diagnostics)
                    || diagnostics.GetCount() !=
            """);
        _ = entry.Append(CultureInfo.InvariantCulture, $" {expected.Length}) {{ return 1; }}\n");
        for (var index = 0; index < expected.Length; index++)
        {
            var diagnostic = expected[index];
            var code = int.Parse(diagnostic.Code.AsSpan(3), CultureInfo.InvariantCulture);
            var location = SourceText.From(text).GetLinePosition(diagnostic.Span.Start);
            _ = entry.Append(FormattableString.Invariant($$"""
                require diagnostics.TryGet({{index}}, init var code{{index}}, init var span{{index}},
                    init var line{{index}}, init var column{{index}});
                if (code{{index}} != {{code}} || span{{index}}.Start != {{diagnostic.Span.Start}}
                    || span{{index}}.Length != {{diagnostic.Span.Length}}
                    || line{{index}} != {{location.Line + 1}}
                    || column{{index}} != {{location.Character + 1}}) { return 2; }
                """));
        }
        _ = entry.Append("""
                if (diagnostics.TryGet(-1, init var invalidCode, init var invalidSpan,
                    init var invalidLine, init var invalidColumn)
                    || invalidCode is init || invalidSpan is init
                    || invalidLine is init || invalidColumn is init) { return 3; }
                if (diagnostics.TryGet(diagnostics.GetCount(), init var beyondCode,
                    init var beyondSpan, init var beyondLine, init var beyondColumn)
                    || beyondCode is init || beyondSpan is init
                    || beyondLine is init || beyondColumn is init) { return 4; }
                return 42;
            }
            """);

        foreach (var policy in new[] { NativeRuntimePolicy.FailFast, NativeRuntimePolicy.CheckedExceptions })
        {
            var program = await CompileAsync(text, entry.ToString(), (BoundRuntimePolicy)policy,
                includeSourceLines: true).ConfigureAwait(true);
            Assert.That(NativeJit.Execute(program, policy), Is.EqualTo(42));
            await ExecuteAotAsync(program, policy).ConfigureAwait(true);
        }
    }
    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public async Task DiagnosticLocationSnapshotHandlesMaximumDenseLineSourceAsync(NativeRuntimePolicy policy)
    {
        var diagnosticCount = ScannerHost.MaximumSourceLength / 2;
        var text = string.Concat(Enumerable.Repeat("\uD800\r", diagnosticCount));
        Assert.That(text.Length, Is.EqualTo(ScannerHost.MaximumSourceLength));
        var entry = $$"""
            using Terrum.CodeAnalysis;
            int32 Main() {
                if (!TokenDiagnosticLocationSnapshot.TryScan(init var diagnostics)
                    || diagnostics.GetCount() != {{diagnosticCount}}) { return 1; }
                mut int32 index = 0;
                while (index < {{diagnosticCount}}) {
                    if (!diagnostics.TryGet(index, init var code, init var span,
                        init var line, init var column)
                        || code != 1 || span.Start != index * 2 || span.Length != 1
                        || line != index + 1 || column != 1) { return 2; }
                    index = index + 1;
                }
                return 42;
            }
            """;

        var program = await CompileAsync(text, entry, (BoundRuntimePolicy)policy,
            includeSourceLines: true).ConfigureAwait(true);
        Assert.That(NativeJit.Execute(program, policy), Is.EqualTo(42));
        await ExecuteAotAsync(program, policy).ConfigureAwait(true);
    }
    [TestCase(0,-1)]
    [TestCase(0,int.MaxValue)]
    [TestCase(1,0)]
    [TestCase(1,13)]
    [TestCase(2,-1)]
    [TestCase(2,2)]
    [TestCase(3,-1)]
    [TestCase(3,0)]
    [TestCase(3,2)]
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
            Assert.That(NativeJit.Execute(program, policy), Is.EqualTo(42));
            await ExecuteAotAsync(program, policy).ConfigureAwait(true);
        }
    }
    [TestCase(0,3,3,0,true)]
    [TestCase(1,3,3,0,false)]
    [TestCase(0,4,3,1,true)]
    [TestCase(1,4,3,1,false)]
    [TestCase(0,5,4,1,true)]
    [TestCase(0,6,5,1,true)]
    [TestCase(715827881,int.MaxValue,3,0,true)]
    [TestCase(715827882,int.MaxValue,3,0,false)]
    [TestCase(536870910,int.MaxValue,4,1,true)]
    [TestCase(536870911,int.MaxValue,4,1,false)]
    [TestCase(429496728,int.MaxValue,5,1,true)]
    [TestCase(429496729,int.MaxValue,5,1,false)]
    [TestCase(-1,int.MaxValue,3,0,false)]
    [TestCase(0,0,3,1,false)]
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
            Assert.That(NativeJit.Execute(program, policy), Is.EqualTo(42));
            await ExecuteAotAsync(program, policy).ConfigureAwait(true);
        }
    }
    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public async Task OwnedTokenSnapshotSupportsIndexedReadsBeyondManagedChainLimitAsync(
        NativeRuntimePolicy policy)
    {
        const string Entry = """
            using Terrum.CodeAnalysis;
            int32 Main() {
                require TokenSnapshot.TryScan(init var tokens);
                if (tokens.GetCount() != 129 || tokens.TryGet(-1, init var negative)
                    || tokens.TryGet(129, init var beyond)) { return 1; }
                mut int32 index = 0;
                while (index < 128) {
                    require tokens.TryGet(index, init var token);
                    if (token.Kind != 2 || token.Range.Start != index * 2
                        || token.Range.Length != 1) { return 2; }
                    index = index + 1;
                }
                require tokens.TryGet(128, init var eof);
                if (eof.Kind != 0 || eof.Range.Start != 255
                    || eof.Range.Length != 0) { return 3; }
                return 42;
            }
            """;
        var input = string.Join(" ", Enumerable.Repeat("a", 128));
        var program = await CompileAsync(input, Entry, (BoundRuntimePolicy)policy).ConfigureAwait(true);
        Assert.That(NativeJit.Execute(program, policy), Is.EqualTo(42));
        await ExecuteAotAsync(program, policy).ConfigureAwait(true);
    }
    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public async Task OwnedTokenSnapshotScalesToMaximumScannerSourceAsync(NativeRuntimePolicy policy)
    {
        var identifierCount = ScannerHost.MaximumSourceLength / 2;
        var expectedTokenCount = identifierCount + 1;
        var input = string.Concat(Enumerable.Repeat("a ", identifierCount));
        Assert.That(input.Length, Is.EqualTo(ScannerHost.MaximumSourceLength));
        var entry = $$"""
            using Terrum.CodeAnalysis;
            int32 Main() {
                require TokenSnapshot.TryScan(init var tokens);
                if (tokens.GetCount() != {{expectedTokenCount}}
                    || tokens.TryGet(-1, init var negative)
                    || tokens.TryGet({{expectedTokenCount}}, init var beyond)) { return 1; }
                mut int32 index = 0;
                while (index < {{identifierCount}}) {
                    require tokens.TryGet(index, init var token);
                    if (token.Kind != 2 || token.Range.Start != index * 2
                        || token.Range.Length != 1) { return 2; }
                    index = index + 1;
                }
                require tokens.TryGet({{identifierCount}}, init var eof);
                if (eof.Kind != 0 || eof.Range.Start != {{ScannerHost.MaximumSourceLength}}
                    || eof.Range.Length != 0) { return 3; }
                return 42;
            }
            """;
        var program = await CompileAsync(input, entry, (BoundRuntimePolicy)policy).ConfigureAwait(true);
        Assert.That(NativeJit.Execute(program, policy), Is.EqualTo(42));
        await ExecuteAotAsync(program, policy).ConfigureAwait(true);
    }
    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public async Task CompilerInputSnapshotsPublishMaximumDensityRecordsAsync(NativeRuntimePolicy policy)
    {
        var integerCount = ScannerHost.MaximumSourceLength / 2;
        var tokenCount = integerCount + 1;
        var input = string.Concat(Enumerable.Repeat("1 ", integerCount));
        Assert.That(input.Length, Is.EqualTo(ScannerHost.MaximumSourceLength));
        var entry = $$"""
            using Terrum.CodeAnalysis;
            int32 Main() {
                if (!TryScanCompilerInput(init var tokens, init var trivia, init var integers)) {
                    return 1;
                }
                if (tokens.GetCount() != {{tokenCount}}
                    || trivia.GetCount() != {{integerCount}}
                    || integers.GetCount() != {{integerCount}}) { return 2; }
                mut int32 index = 0;
                while (index < {{integerCount}}) {
                    if (!tokens.TryGet(index, init var token)
                        || token.Kind != 3 || token.Range.Start != index * 2
                        || token.Range.Length != 1) { return 3; }
                    if (!trivia.TryGet(index, init var whitespace, init var owner)
                        || whitespace.Kind != 0 || whitespace.Range.Start != index * 2 + 1
                        || whitespace.Range.Length != 1 || owner != index + 1) { return 4; }
                    if (!integers.TryGet(index, init var tokenIndex, init var overflow,
                        init var low, init var high)
                        || tokenIndex != index || overflow || low != 1 || high != 0) { return 5; }
                    if (!integers.TryFind(index, init var foundOverflow,
                        init var foundLow, init var foundHigh)
                        || foundOverflow || foundLow != 1 || foundHigh != 0) { return 6; }
                    index = index + 1;
                }
                if (!tokens.TryGet({{integerCount}}, init var eof)
                    || eof.Kind != 0 || eof.Range.Start != {{ScannerHost.MaximumSourceLength}}
                    || eof.Range.Length != 0) { return 7; }
                return 42;
            }
            """;
        var program = await CompileAsync(input, entry, (BoundRuntimePolicy)policy).ConfigureAwait(true);
        Assert.That(NativeJit.Execute(program, policy), Is.EqualTo(42));
        await ExecuteAotAsync(program, policy).ConfigureAwait(true);
    }
    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
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
                require snapshot.TryGet(0, init var first);
                if (first.Kind != 2 || first.Range.Start != 0) { return 1; }
                Host.Tokens[0] = 2;
                Host.Tokens[3] = -1;
                if (TokenSnapshot.TryBuild(count, init var invalid)) { return 2; }
                if (TryBuildLossless(count, init var invalidTokens, init var invalidTrivia)) {
                    return 4;
                }
                if (!snapshot.TryGet(1, init var second) || second.Kind != 2
                    || second.Range.Start != 6) { return 3; }
                return 42;
            }
            """;
        var program = await CompileAsync("alpha beta", Entry, (BoundRuntimePolicy)policy).ConfigureAwait(true);
        Assert.That(NativeJit.Execute(program, policy), Is.EqualTo(42));
        await ExecuteAotAsync(program, policy).ConfigureAwait(true);
    }
    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public async Task StandaloneTriviaBuilderRejectsInvalidTokenRecordsAsync(NativeRuntimePolicy policy)
    {
        const string Entry = """
            using Terrum.CodeAnalysis;
            int32 Main() {
                int32 count = Scan();
                Host.Tokens[0] = -1;
                if (TokenTriviaSnapshot.TryBuild(count, init var invalid)) { return 1; }
                return 42;
            }
            """;
        var program = await CompileAsync("alpha", Entry, (BoundRuntimePolicy)policy).ConfigureAwait(true);
        Assert.That(NativeJit.Execute(program, policy), Is.EqualTo(42));
        await ExecuteAotAsync(program, policy).ConfigureAwait(true);
    }
    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
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
        Assert.That(NativeJit.Execute(program, policy), Is.EqualTo(42));
        await ExecuteAotAsync(program, policy).ConfigureAwait(true);
    }
    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public async Task DiagnosticSnapshotRetainsMaximumMalformedSourceRecordsAsync(NativeRuntimePolicy policy)
    {
        var entry = $$"""
            using Terrum.CodeAnalysis;
            int32 Main() {
                if (TokenSnapshot.TryScan(init var tokens)) { return 1; }
                if (!TokenDiagnosticSnapshot.TryScan(init var diagnostics)
                    || diagnostics.GetCount() != {{ScannerHost.MaximumSourceLength}}
                    || diagnostics.TryGet(-1, init var negativeCode, init var negativeSpan)
                    || diagnostics.TryGet({{ScannerHost.MaximumSourceLength}},
                        init var beyondCode, init var beyondSpan)) { return 2; }
                mut int32 index = 0;
                while (index < {{ScannerHost.MaximumSourceLength}}) {
                    if (!diagnostics.TryGet(index, init var code, init var span)
                        || code != 1 || span.Start != index || span.Length != 1) { return 3; }
                    index = index + 1;
                }
                return 42;
            }
            """;
        var input = new string('\uD800', ScannerHost.MaximumSourceLength);
        var program = await CompileAsync(input, entry, (BoundRuntimePolicy)policy).ConfigureAwait(true);
        Assert.That(NativeJit.Execute(program, policy), Is.EqualTo(42));
        await ExecuteAotAsync(program, policy).ConfigureAwait(true);
    }
    [TestCase("")]
    [TestCase("  // leading\r\nalpha /* block */ beta\t")]
    [TestCase("alpha\u2028beta // trailing")]
    [TestCase("/// heading\nalpha //** body **// beta /** tail **/")]
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
            $"if (trivia.GetCount() != {expected.Length} || trivia.TryGet(-1, init var negative, init var negativeOwner) || trivia.TryGet({expected.Length}, init var beyond, init var beyondOwner)) {{ return 1; }}");
        if (expected.Length > 0)
        {
            _ = entry.Append("Host.Trivia[1] = -1;");
        }
        for (var index = 0; index < expected.Length; index++)
        {
            var (kind, start, length, owner) = expected[index];
            _ = entry.Append(CultureInfo.InvariantCulture,
                $"require trivia.TryGet({index}, init var part{index}, init var owner{index});"
                + $" if (part{index}.Kind != {kind} || part{index}.Range.Start != {start}"
                + $" || part{index}.Range.Length != {length} || owner{index} != {owner}) {{ return 2; }}");
        }
        _ = entry.Append("return 42; }");

        foreach (var policy in new[] { NativeRuntimePolicy.FailFast, NativeRuntimePolicy.CheckedExceptions })
        {
            var program = await CompileAsync(text, entry.ToString(), (BoundRuntimePolicy)policy).ConfigureAwait(true);
            Assert.That(NativeJit.Execute(program, policy), Is.EqualTo(42));
            await ExecuteAotAsync(program, policy).ConfigureAwait(true);
        }
    }
    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public async Task LosslessTokenSnapshotMatchesStandaloneBuilderAsync(NativeRuntimePolicy policy)
    {
        const string Entry = """
            using Terrum.CodeAnalysis;
            int32 Main() {
                int32 count = Scan();
                require TokenSnapshot.TryBuild(count, init var standalone);
                require TryBuildLossless(count, init var tokens, init var trivia);
                if (tokens.GetCount() != standalone.GetCount()) { return 1; }
                for (mut int32 index = 0; index < count; index = index + 1) {
                    require standalone.TryGet(index, init var expected);
                    require tokens.TryGet(index, init var actual);
                    if (actual.Kind != expected.Kind || actual.Range.Start != expected.Range.Start
                        || actual.Range.Length != expected.Range.Length) { return 2; }
                }
                Host.Tokens[0] = -1;
                Host.Trivia[1] = -1;
                require tokens.TryGet(0, init var first);
                require trivia.TryGet(0, init var firstTrivia, init var firstOwner);
                if (first.Kind != 2 || first.Range.Start != 0
                    || firstTrivia.Range.Start != 5 || firstOwner != 1) { return 3; }
                return 42;
            }
            """;
        var program = await CompileAsync("alpha beta", Entry, (BoundRuntimePolicy)policy).ConfigureAwait(true);
        Assert.That(NativeJit.Execute(program, policy), Is.EqualTo(42));
        await ExecuteAotAsync(program, policy).ConfigureAwait(true);
    }
    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public async Task OwnedTriviaSnapshotSurvivesTheManagedChainLimitAsync(NativeRuntimePolicy policy)
    {
        const string Entry = """
            using Terrum.CodeAnalysis;
            int32 Main() {
                require TryScanLossless(init var tokens, init var trivia);
                if (tokens.GetCount() != 129 || trivia.GetCount() != 128) { return 1; }
                require trivia.TryGet(0, init var first, init var firstOwner);
                require trivia.TryGet(63, init var middle, init var middleOwner);
                require trivia.TryGet(127, init var last, init var lastOwner);
                if (first.Kind != 0 || first.Range.Start != 1 || first.Range.Length != 1
                    || firstOwner != 1 || middle.Range.Start != 127 || middleOwner != 64
                    || last.Range.Start != 255 || lastOwner != 128) { return 2; }
                return 42;
            }
            """;
        var input = string.Concat(Enumerable.Repeat("a ", 128));
        var program = await CompileAsync(input, Entry, (BoundRuntimePolicy)policy).ConfigureAwait(true);
        Assert.That(NativeJit.Execute(program, policy), Is.EqualTo(42));
        await ExecuteAotAsync(program, policy).ConfigureAwait(true);
    }
    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
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
        Assert.That(NativeJit.Execute(program, policy), Is.EqualTo(42));
        await ExecuteAotAsync(program, policy).ConfigureAwait(true);
    }
    [TestCase(0,0)]
    [TestCase(0,int.MaxValue)]
    [TestCase(1,-1)]
    [TestCase(2,1)]
    [TestCase(3,0)]
    [TestCase(4,0)]
    [TestCase(4,3)]
    public async Task InvalidTriviaDoesNotPublishOwnedSnapshotAsync(int word, int value)
    {
        var entry = $$"""
            using Terrum.CodeAnalysis;
            int32 Main() {
                int32 count = Scan();
                require TokenSnapshot.TryBuild(count, init var standalone);
                Host.Trivia[{{word}}] = {{value}};
                if (TryBuildLossless(count, init var tokens, init var trivia)) { return 1; }
                if (standalone.GetCount() != count) { return 2; }
                return 42;
            }
            """;
        foreach (var policy in new[] { NativeRuntimePolicy.FailFast, NativeRuntimePolicy.CheckedExceptions })
        {
            var program = await CompileAsync("alpha beta", entry, (BoundRuntimePolicy)policy).ConfigureAwait(true);
            Assert.That(NativeJit.Execute(program, policy), Is.EqualTo(42));
            await ExecuteAotAsync(program, policy).ConfigureAwait(true);
        }
    }
    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public async Task OwnedIntegerRecordsRetainFullUnsignedMagnitudeAndOverflowAsync(
        NativeRuntimePolicy policy)
    {
        const string Entry = """
            using Terrum.CodeAnalysis;
            int32 Main() {
                require TryScanCompilerInput(init var tokens, init var trivia, init var integers);
                if (tokens.GetCount() != 5 || trivia.GetCount() != 3
                    || integers.GetCount() != 4 || integers.TryGet(-1, init var negativeIndex,
                        init var negativeOverflow, init var negativeLow, init var negativeHigh)
                    || integers.TryGet(4, init var beyondIndex, init var beyondOverflow,
                        init var beyondLow, init var beyondHigh)) { return 1; }
                Host.IntegerValues[3] = 0;
                require integers.TryGet(0, init var first, init var firstOverflow,
                    init var firstLow, init var firstHigh);
                require integers.TryGet(1, init var second, init var secondOverflow,
                    init var secondLow, init var secondHigh);
                require integers.TryGet(2, init var third, init var thirdOverflow,
                    init var thirdLow, init var thirdHigh);
                require integers.TryGet(3, init var fourth, init var fourthOverflow,
                    init var fourthLow, init var fourthHigh);
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
        Assert.That(NativeJit.Execute(program, policy), Is.EqualTo(42));
        await ExecuteAotAsync(program, policy).ConfigureAwait(true);
    }
    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public async Task CompilerInputSnapshotsOwnEveryRecordBufferAsync(NativeRuntimePolicy policy)
    {
        const string Entry = """
            using Terrum.CodeAnalysis;
            int32 Main() {
                int32 count = Scan();
                require TryBuildCompilerInput(count, init var tokens, init var trivia,
                    init var integers);
                Host.Tokens[0] = -1;
                Host.Trivia[1] = -1;
                Host.IntegerValues[1] = -1;
                require tokens.TryGet(0, init var firstToken);
                require trivia.TryGet(0, init var firstTrivia, init var firstOwner);
                require integers.TryGet(0, init var integerIndex, init var overflow,
                    init var low, init var high);
                if (firstToken.Kind != 2 || firstToken.Range.Start != 0
                    || firstTrivia.Range.Start != 5 || firstOwner != 1
                    || integerIndex != 1 || overflow || low != 42 || high != 0) {
                    return 1;
                }
                return 42;
            }
            """;
        var program = await CompileAsync("alpha 42", Entry, (BoundRuntimePolicy)policy).ConfigureAwait(true);
        Assert.That(NativeJit.Execute(program, policy), Is.EqualTo(42));
        await ExecuteAotAsync(program, policy).ConfigureAwait(true);
    }
    [TestCase("alpha",NativeRuntimePolicy.FailFast)]
    [TestCase("alpha",NativeRuntimePolicy.CheckedExceptions)]
    [TestCase("1.5",NativeRuntimePolicy.FailFast)]
    [TestCase("1.5",NativeRuntimePolicy.CheckedExceptions)]
    public async Task CompilerInputPermitsNoDecodedIntegersAsync(string text, NativeRuntimePolicy policy)
    {
        const string Entry = """
            using Terrum.CodeAnalysis;
            int32 Main() {
                require TryScanCompilerInput(init var tokens, init var trivia, init var integers);
                if (tokens.GetCount() != 2 || trivia.GetCount() != 0
                    || integers.GetCount() != 0
                    || integers.TryFind(0, init var overflow, init var low, init var high)) {
                    return 1;
                }
                return 42;
            }
            """;
        var program = await CompileAsync(text, Entry, (BoundRuntimePolicy)policy).ConfigureAwait(true);
        Assert.That(NativeJit.Execute(program, policy), Is.EqualTo(42));
        await ExecuteAotAsync(program, policy).ConfigureAwait(true);
    }
    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public async Task FindsDecodedIntegerByTokenIndexAndSkipsNonIntegerNumericsAsync(
        NativeRuntimePolicy policy)
    {
        const string Entry = """
            using Terrum.CodeAnalysis;
            int32 Main() {
                require TryScanCompilerInput(init var tokens, init var trivia, init var integers);
                if (tokens.GetCount() != 8 || integers.GetCount() != 4
                    || integers.TryFind(-1, init var negativeOverflow, init var negativeLow,
                        init var negativeHigh)
                    || integers.TryFind(1, init var floatingOverflow, init var floatingLow,
                        init var floatingHigh)
                    || integers.TryFind(2, init var exponentOverflow, init var exponentLow,
                        init var exponentHigh)
                    || integers.TryFind(3, init var hexFloatOverflow, init var hexFloatLow,
                        init var hexFloatHigh)
                    || integers.TryFind(7, init var eofOverflow, init var eofLow,
                        init var eofHigh)) { return 1; }
                require integers.TryFind(0, init var firstOverflow, init var firstLow,
                    init var firstHigh);
                require integers.TryFind(4, init var hexIntegerOverflow,
                    init var hexIntegerLow, init var hexIntegerHigh);
                require integers.TryFind(5, init var binaryIntegerOverflow,
                    init var binaryIntegerLow, init var binaryIntegerHigh);
                require integers.TryFind(6, init var lastOverflow, init var lastLow,
                    init var lastHigh);
                if (firstOverflow || firstLow != 1 || firstHigh != 0
                    || hexIntegerOverflow || hexIntegerLow != 0x1E2 || hexIntegerHigh != 0
                    || binaryIntegerOverflow || binaryIntegerLow != 2 || binaryIntegerHigh != 0
                    || lastOverflow || lastLow != 42 || lastHigh != 0) { return 2; }
                return 42;
            }
            """;
        var program = await CompileAsync("1 1.5 1e2 0x1p1 0x1e2 0b10 42", Entry,
            (BoundRuntimePolicy)policy).ConfigureAwait(true);
        Assert.That(NativeJit.Execute(program, policy), Is.EqualTo(42));
        await ExecuteAotAsync(program, policy).ConfigureAwait(true);
    }
    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public async Task IndexedIntegerLookupExceedsManagedChainLimitAsync(NativeRuntimePolicy policy)
    {
        const string Entry = """
            using Terrum.CodeAnalysis;
            int32 Main() {
                require TryScanCompilerInput(init var tokens, init var trivia, init var integers);
                if (tokens.GetCount() != 129 || integers.GetCount() != 128
                    || integers.TryFind(128, init var missingOverflow, init var missingLow,
                        init var missingHigh)) { return 1; }
                require integers.TryFind(0, init var firstOverflow, init var firstLow,
                    init var firstHigh);
                require integers.TryFind(64, init var middleOverflow, init var middleLow,
                    init var middleHigh);
                require integers.TryFind(127, init var lastOverflow, init var lastLow,
                    init var lastHigh);
                if (firstOverflow || middleOverflow || lastOverflow
                    || firstLow != 1 || middleLow != 1 || lastLow != 1
                    || firstHigh != 0 || middleHigh != 0 || lastHigh != 0) { return 2; }
                return 42;
            }
            """;
        var program = await CompileAsync(string.Concat(Enumerable.Repeat("1 ", 128)),
            Entry, (BoundRuntimePolicy)policy).ConfigureAwait(true);
        Assert.That(NativeJit.Execute(program, policy), Is.EqualTo(42));
        await ExecuteAotAsync(program, policy).ConfigureAwait(true);
    }
    [TestCase(0,3)]
    [TestCase(1,-1)]
    [TestCase(2,2)]
    [TestCase(5,1)]
    [TestCase(6,0)]
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
            Assert.That(NativeJit.Execute(program, policy), Is.EqualTo(42));
            await ExecuteAotAsync(program, policy).ConfigureAwait(true);
        }
    }
    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public async Task MissingIntegerRecordsDoNotPublishCompilerInputAsync(NativeRuntimePolicy policy)
    {
        const string Entry = """
            using Terrum.CodeAnalysis;
            int32 Main() {
                int32 count = Scan();
                Host.IntegerValues[0] = 1;
                if (TryBuildCompilerInput(count, init var tokens, init var trivia,
                    init var integers)) { return 1; }
                return 42;
            }
            """;
        var program = await CompileAsync("42 3", Entry, (BoundRuntimePolicy)policy).ConfigureAwait(true);
        Assert.That(NativeJit.Execute(program, policy), Is.EqualTo(42));
        await ExecuteAotAsync(program, policy).ConfigureAwait(true);
    }
    [TestCase("42",0,43,0)]
    [TestCase("42",1,0,0)]
    [TestCase("18446744073709551616",0,0,0)]
    [TestCase("18446744073709551615",0,-1,0)]
    public async Task CompilerInputRejectsIntegerMagnitudeRecordsThatDisagreeWithSourceAsync(
        string text, int status, int low, int high)
    {
        var entry = $$"""
            using Terrum.CodeAnalysis;
            int32 Main() {
                int32 count = Scan();
                Host.IntegerValues[2] = {{status}};
                Host.IntegerValues[3] = {{low}};
                Host.IntegerValues[4] = {{high}};
                if (TryBuildCompilerInput(count, init var tokens, init var trivia,
                    init var integers)) { return 1; }
                return 42;
            }
            """;
        foreach (var policy in new[] { NativeRuntimePolicy.FailFast, NativeRuntimePolicy.CheckedExceptions })
        {
            var program = await CompileAsync(text, entry, (BoundRuntimePolicy)policy).ConfigureAwait(true);
            Assert.That(NativeJit.Execute(program, policy), Is.EqualTo(42));
            await ExecuteAotAsync(program, policy).ConfigureAwait(true);
        }
    }
    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public async Task ScalarFrontendRejectsMismatchedScannerMagnitudeAsync(NativeRuntimePolicy policy)
    {
        const string Text = "int32 Value() { return 42; }";
        const string Entry = """
            using Terrum.CodeAnalysis;
            int32 Main() {
                require TokenSnapshot.TryScan(init var tokens);
                Host.IntegerValues[3] = 43;
                if (TryParseScalarFrontend(move tokens, init var result)) { return 1; }
                return 42;
            }
            """;
        var program = await CompileAsync(Text, Entry, (BoundRuntimePolicy)policy, "ScalarFrontend")
            .ConfigureAwait(true);
        Assert.That(NativeJit.Execute(program, policy), Is.EqualTo(42));
        await ExecuteAotAsync(program, policy).ConfigureAwait(true);
    }
    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public async Task ScalarProgramRejectsMismatchedScannerMagnitudeAsync(NativeRuntimePolicy policy)
    {
        const string Text = "int32 First() { return 42; } int32 Second() { return First(); }";
        const string Entry = """
            using Terrum.CodeAnalysis;
            int32 Main() {
                require TokenSnapshot.TryScan(init var tokens);
                Host.IntegerValues[3] = 43;
                int32 count = tokens.GetCount();
                Span<Token> tokenView = tokens.AsSpan();
                require SourceRange.TryCreate(0, 0, init var spacing);
                if (TryPublishScalarFrontendProgram(tokenView, count, spacing, init var handled)) {
                    return 1;
                }
                return 42;
            }
            """;
        var program = await CompileAsync(Text, Entry, (BoundRuntimePolicy)policy, "ScalarFrontend")
            .ConfigureAwait(true);
        Assert.That(NativeJit.Execute(program, policy), Is.EqualTo(42));
        await ExecuteAotAsync(program, policy).ConfigureAwait(true);
    }
    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public async Task ScalarFrontendRejectsMissingScannerIntegerRecordAsync(NativeRuntimePolicy policy)
    {
        const string Text = "int32 Value() { return 42; }";
        const string Entry = """
            using Terrum.CodeAnalysis;
            int32 Main() {
                require TokenSnapshot.TryScan(init var tokens);
                Host.IntegerValues[0] = 0;
                if (TryParseScalarFrontend(move tokens, init var result)) { return 1; }
                return 42;
            }
            """;
        var program = await CompileAsync(Text, Entry, (BoundRuntimePolicy)policy, "ScalarFrontend")
            .ConfigureAwait(true);
        Assert.That(NativeJit.Execute(program, policy), Is.EqualTo(42));
        await ExecuteAotAsync(program, policy).ConfigureAwait(true);
    }
    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public async Task ScalarProgramRejectsMissingScannerIntegerRecordAsync(NativeRuntimePolicy policy)
    {
        const string Text = "int32 First() { return 42; } int32 Second() { return First(); }";
        const string Entry = """
            using Terrum.CodeAnalysis;
            int32 Main() {
                require TokenSnapshot.TryScan(init var tokens);
                Host.IntegerValues[0] = 0;
                int32 count = tokens.GetCount();
                Span<Token> tokenView = tokens.AsSpan();
                require SourceRange.TryCreate(0, 0, init var spacing);
                if (TryPublishScalarFrontendProgram(tokenView, count, spacing, init var handled)) {
                    return 1;
                }
                return 42;
            }
            """;
        var program = await CompileAsync(Text, Entry, (BoundRuntimePolicy)policy, "ScalarFrontend")
            .ConfigureAwait(true);
        Assert.That(NativeJit.Execute(program, policy), Is.EqualTo(42));
        await ExecuteAotAsync(program, policy).ConfigureAwait(true);
    }
    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
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
        Assert.That(NativeJit.Execute(program, policy), Is.EqualTo(42));
        await ExecuteAotAsync(program, policy).ConfigureAwait(true);
    }
    [TestCase("int32 Answer() { return 42; }",true)]
    [TestCase("int32 Answer() { return -42; }",true)]
    [TestCase("int32 Answer() { return +42; }",true)]
    [TestCase("int32 Answer() { return ~42; }",true)]
    [TestCase("uint32 Answer() { return 42; }",false)]
    [TestCase("int32 Answer() { return (42); }",true)]
    [TestCase("int32 Answer() { return (-42); }",true)]
    [TestCase("int32 Answer() { return -(42); }",true)]
    [TestCase("int32 Answer() { return true; }",false)]
    [TestCase("int32 Answer() { return 42 }",false)]
    public async Task BoundedScalarParserAgreesWithHostedSyntaxForItsAcceptedShapeAsync(
        string text, bool accepted)
    {
        Assert.That((int)TokenKind.EndOfFile, Is.EqualTo(0));
        Assert.That((int)TokenKind.Identifier, Is.EqualTo(2));
        Assert.That((int)TokenKind.NumericLiteral, Is.EqualTo(3));
        Assert.That((int)TokenKind.ReturnKeyword, Is.EqualTo(19));
        Assert.That((int)TokenKind.OpenParen, Is.EqualTo(46));
        Assert.That((int)TokenKind.CloseParen, Is.EqualTo(47));
        Assert.That((int)TokenKind.OpenBrace, Is.EqualTo(48));
        Assert.That((int)TokenKind.CloseBrace, Is.EqualTo(49));
        Assert.That((int)TokenKind.Semicolon, Is.EqualTo(52));
        Assert.That((int)TokenKind.Plus, Is.EqualTo(57));
        Assert.That((int)TokenKind.Minus, Is.EqualTo(58));
        Assert.That((int)TokenKind.Bang, Is.EqualTo(62));
        Assert.That((int)TokenKind.Tilde, Is.EqualTo(63));

        var hosted = SyntaxTree.Parse(text);
        var name = hosted.Tokens[1].Span;
        var result = hosted.Root.DescendantNodes().Single(node => node.Kind == SyntaxKind.ReturnStatement);
        var expression = ((SyntaxNode)result.Children[1]).Span;
        var assertion = accepted
            ? FormattableString.Invariant($$"""
                require TryParseScalarReturnFunction(move tokens, init var name, init var value);
                if (name.Start != {{name.Start}} || name.Length != {{name.Length}}
                    || value.Start != {{expression.Start}} || value.Length != {{expression.Length}}) { return 1; }
                """)
            : "if (TryParseScalarReturnFunction(move tokens, init var name, init var value)) { return 1; }";
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
            Assert.That(NativeJit.Execute(program, policy), Is.EqualTo(42));
            await ExecuteAotAsync(program, policy).ConfigureAwait(true);
        }
    }
    [TestCase("int32 Answer() { return 42; }",1)]
    [TestCase("uint32 Answer() { return 42; }",2)]
    [TestCase("uint32 Answer() { return +42; }",2)]
    [TestCase("int64 Answer() { return -42; }",3)]
    [TestCase("uint64 Answer() { return 42; }",4)]
    [TestCase("int64 Answer() { return (42); }",3)]
    [TestCase("uint64 Answer() { return (+42); }",4)]
    [TestCase("bool Answer() { return true; }",5)]
    [TestCase("bool Answer() { return (false); }",5)]
    [TestCase("bool Answer() { return !true; }",5)]
    [TestCase("bool Answer() { return !(false); }",5)]
    [TestCase("uint64 Answer() { return +(42); }",4)]
    [TestCase("uint64 Answer() { return (~42); }",4)]
    [TestCase("float32 Answer() { return 42; }",0)]
    [TestCase("float64 Answer() { return 42; }",0)]
    [TestCase("int32 Answer () { return 42; }",0)]
    [TestCase("int32 Answer\n() { return 42; }",0)]
    [TestCase("int32 Answer/* boundary */() { return 42; }",0)]
    [TestCase("int32 Answer/// boundary\n() { return 42; }",0)]
    public async Task TypedScalarParserMatchesHostedDeclarationSpansAsync(string text, int type)
    {
        var hosted = SyntaxTree.Parse(text);
        var name = hosted.Tokens[1].Span;
        var result = hosted.Root.DescendantNodes().Single(node => node.Kind == SyntaxKind.ReturnStatement);
        var expression = ((SyntaxNode)result.Children[1]).Span;
        var assertion = type != 0
            ? FormattableString.Invariant($$"""
                require TryParseScalarReturnFunctionTyped(move tokens, init var kind,
                    init var name, init var expression);
                if (kind != {{type}} || name.Start != {{name.Start}} || name.Length != {{name.Length}}
                    || expression.Start != {{expression.Start}}
                    || expression.Length != {{expression.Length}}) { return 1; }
                """)
            : """
                if (TryParseScalarReturnFunctionTyped(move tokens, init var kind,
                    init var name, init var expression)) { return 1; }
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
            Assert.That(NativeJit.Execute(program, policy), Is.EqualTo(42));
            await ExecuteAotAsync(program, policy).ConfigureAwait(true);
        }
    }

    [Test]
    public async Task TypedScalarParserKeepsOutputsVacantWhenTypeFilterRejectsAsync()
    {
        const string Text = "uint32 Value() { return 42; }";
        const string Entry = """
            using Terrum.CodeAnalysis;
            int32 Main() {
                require TokenSnapshot.TryScan(init var tokens);
                if (TryParseScalarReturnFunction(move tokens, init var name, init var expression))
                {
                    return 1;
                }
                require TokenSnapshot.TryScan(init var retryTokens);
                require TryParseScalarReturnFunctionTyped(
                    move retryTokens, init var scalarType, init name, init expression);
                if (scalarType != 2 || name.Start != 7 || name.Length != 5
                    || expression.Length != 2)
                {
                    return 2;
                }
                return 42;
            }
            """;
        foreach (var policy in new[] { NativeRuntimePolicy.FailFast, NativeRuntimePolicy.CheckedExceptions })
        {
            var program = await CompileParserAsync(Text, Entry, (BoundRuntimePolicy)policy)
                .ConfigureAwait(true);
            Assert.That(NativeJit.Execute(program, policy), Is.EqualTo(42));
            await ExecuteAotAsync(program, policy).ConfigureAwait(true);
        }
    }
    [TestCase("bool Value() { return true; }",true,true)]
    [TestCase("bool Value() { return false; }",true,false)]
    [TestCase("bool Value() { return (true); }",true,true)]
    [TestCase("bool Value() { return (false); }",true,false)]
    [TestCase("bool Value() { return !true; }",true,false)]
    [TestCase("bool Value() { return !false; }",true,true)]
    [TestCase("bool Value() { return (!true); }",true,false)]
    [TestCase("bool Value() { return !(false); }",true,true)]
    [TestCase("bool Value() { return +true; }",false,false)]
    [TestCase("bool Value() { return +(true); }",false,false)]
    [TestCase("int32 Value() { return 1; }",false,false)]
    public async Task BoundedNativeBoolEvaluationMatchesHostedExecutedValuesAsync(
        string text, bool evaluable, bool value)
    {
        var expected = value ? "true" : "false";
        var assertion = evaluable
            ? $$"""
                require TryEvaluateBoolScalarReturn(move tokens, init var name, init var bound);
                if (name.Start != 5 || name.Length != 5 || bound != {{expected}}) { return 1; }
                """
            : """
                if (TryEvaluateBoolScalarReturn(move tokens, init var name, init var bound)) { return 1; }
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
                Assert.That(hosted.HasErrors, Is.False, string.Join(Environment.NewLine, hosted.Diagnostics));
                Assert.That(NativeJit.Execute(((global::System.Func<BoundProgram>)(() => { var __nunitActual0 = hosted.Program; Assert.That(__nunitActual0, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual0!; }))(), policy), Is.EqualTo(42));
            }
            var program = await CompileParserAsync(text, entry, (BoundRuntimePolicy)policy).ConfigureAwait(true);
            Assert.That(NativeJit.Execute(program, policy), Is.EqualTo(42));
            await ExecuteAotAsync(program, policy).ConfigureAwait(true);
        }
    }
    [TestCase("int32(1) == int32(1)",true,true)]
    [TestCase("int32(1) != int32(2)",true,true)]
    [TestCase("int32(0xFFFF_FFFF) < int32(0)",true,true)]
    [TestCase("int32(1) <= int32(1)",true,true)]
    [TestCase("uint32(4294967295) > uint32(1)",true,true)]
    [TestCase("uint32(1) >= uint32(2)",true,false)]
    [TestCase("int64(0x8000_0000_0000_0000) < int64(0)",true,true)]
    [TestCase("uint64(18446744073709551615) > uint64(0)",true,true)]
    [TestCase("int64(0xFFFF_FFFF_FFFF_FFFF) == int64(0)",true,false)]
    [TestCase("char16(65535) > char16(1)",true,true)]
    [TestCase("char16(0xFFFF) <= char16(65535)",true,true)]
    [TestCase("char16(0) != char16(0)",true,false)]
    [TestCase("int32(2147483648) == int32(0)",false,false)]
    [TestCase("uint32(4294967296) == uint32(0)",false,false)]
    [TestCase("int64(9223372036854775808) < int64(0)",false,false)]
    [TestCase("uint64(18446744073709551616) > uint64(0)",false,false)]
    [TestCase("char16(65536) > char16(0)",false,false)]
    [TestCase("char16(1) < uint32(2)",false,false)]
    [TestCase("int32(1) < int64(2)",false,false)]
    [TestCase("int32(1) + int32(2)",false,false)]
    [TestCase("int32 (1) == int32(1)",false,false)]
    [TestCase("int32(1) == int32\n(1)",false,false)]
    [TestCase("int32/* boundary */(1) == int32(1)",false,false)]
    [TestCase("int32(1) == int32/// boundary\n(1)",false,false)]
    public async Task BoundedNativeTypedScalarComparisonUsesOwnedRecordsAsync(
        string expression, bool evaluable, bool expected)
    {
        var text = $"bool Value() {{ return {expression}; }}";
        var assertion = evaluable
            ? $$"""
                require TryEvaluateTypedScalarComparisonReturn(move tokens, move integers,
                    init var name, init var bound);
                if (name.Start != 5 || name.Length != 5 || bound != {{(expected ? "true" : "false")}})
                {
                    return 1;
                }
                """
            : """
                if (TryEvaluateTypedScalarComparisonReturn(move tokens, move integers,
                    init var name, init var bound))
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
                Assert.That(hosted.HasErrors, Is.False, string.Join(Environment.NewLine, hosted.Diagnostics));
                Assert.That(NativeJit.Execute(((global::System.Func<BoundProgram>)(() => { var __nunitActual1 = hosted.Program; Assert.That(__nunitActual1, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual1!; }))(), policy), Is.EqualTo(expected ? 42 : 1));
            }
            var program = await CompileParserAsync(text, entry, (BoundRuntimePolicy)policy).ConfigureAwait(true);
            Assert.That(NativeJit.Execute(program, policy), Is.EqualTo(42));
            await ExecuteAotAsync(program, policy).ConfigureAwait(true);
        }
    }
    [TestCase("int32","37","x + 5",true,"42")]
    [TestCase("int32","1","x + 0xFFFF_FFFF",true,"0")]
    [TestCase("int32","int32.MaxValue","x + 0x8000_0000",true,"-1")]
    [TestCase("int32","int32.MaxValue","x + 1",false,"0")]
    [TestCase("int32","int32.MinValue","x + 0xFFFF_FFFF",false,"0")]
    [TestCase("int32","0","x + 2147483648",false,"0")]
    [TestCase("int32","1","y + 5",false,"0")]
    [TestCase("uint32","0","x + 0xFFFF_FFFF",true,"uint32.MaxValue")]
    [TestCase("uint32","uint32.MaxValue","x + 1",false,"0")]
    [TestCase("uint32","0","x + 4294967296",false,"0")]
    [TestCase("int64","int64.MaxValue","x + 0x8000_0000_0000_0000",true,"-1")]
    [TestCase("int64","int64.MaxValue","x + 1",false,"0")]
    [TestCase("int64","int64.MinValue","x + 0xFFFF_FFFF_FFFF_FFFF",false,"0")]
    [TestCase("int64","0","x + 9223372036854775808",false,"0")]
    [TestCase("uint64","0","x + 18446744073709551615",true,"uint64.MaxValue")]
    [TestCase("uint64","uint64.MaxValue","x + 1",false,"0")]
    [TestCase("uint64","0","x + 18446744073709551616",false,"0")]
    public async Task BoundedNativeNamedParameterAdditionUsesOwnedRecordsAsync(
        string typeName, string parameter, string expression, bool evaluable, string expected)
    {
        var evaluator = typeName switch {
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
                    init var name, init var bound);
                if (name.Start != {{typeName.Length + 1}} || name.Length != 5
                    || bound != {{expected}}) { return 1; }
                """
            : $$"""
                if ({{evaluator}}(move tokens, move integers, {{parameter}},
                    init var name, init var bound)) { return 1; }
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
                Assert.That(hosted.HasErrors, Is.False, string.Join(Environment.NewLine, hosted.Diagnostics));
                Assert.That(NativeJit.Execute(((global::System.Func<BoundProgram>)(() => { var __nunitActual2 = hosted.Program; Assert.That(__nunitActual2, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual2!; }))(), policy), Is.EqualTo(42));
            }
            var program = await CompileParserAsync(text, entry, (BoundRuntimePolicy)policy).ConfigureAwait(true);
            Assert.That(NativeJit.Execute(program, policy), Is.EqualTo(42));
            await ExecuteAotAsync(program, policy).ConfigureAwait(true);
        }
    }

    [Test]
    public async Task BoundedNativeNamedParameterAdditionKeepsOutputsVacantOnFailureAsync()
    {
        const string Text = "int32 Value(int32 x) { return x + 22; }";
        const string Entry = """
            using Terrum.CodeAnalysis;
            int32 Main() {
                require TryScanCompilerInput(init var tokens, init var trivia, init var integers);
                if (TryEvaluateInt32ParameterAddReturn(
                    move tokens, move integers, int32.MaxValue, init var name, init var value))
                {
                    return 1;
                }
                require TryScanCompilerInput(init var retryTokens, init var retryTrivia,
                    init var retryIntegers);
                require TryEvaluateInt32ParameterAddReturn(
                    move retryTokens, move retryIntegers, 20, init name, init value);
                if (name.Start != 6 || name.Length != 5 || value != 42) { return 2; }
                return 42;
            }
            """;
        foreach (var policy in new[] { NativeRuntimePolicy.FailFast, NativeRuntimePolicy.CheckedExceptions })
        {
            var program = await CompileParserAsync(Text, Entry, (BoundRuntimePolicy)policy)
                .ConfigureAwait(true);
            Assert.That(NativeJit.Execute(program, policy), Is.EqualTo(42));
            await ExecuteAotAsync(program, policy).ConfigureAwait(true);
        }
    }
    [TestCase("int32","21","21","x + y",true,"42")]
    [TestCase("int32","int32.MinValue","int32.MaxValue","x + y",true,"-1")]
    [TestCase("int32","int32.MaxValue","1","x + y",false,"0")]
    [TestCase("int32","int32.MinValue","-1","x + y",false,"0")]
    [TestCase("int32","1","2","x + x",false,"0")]
    [TestCase("int32","1","2","y + x",false,"0")]
    [TestCase("uint32","uint32.MaxValue","0","x + y",true,"uint32.MaxValue")]
    [TestCase("uint32","uint32.MaxValue","1","x + y",false,"0")]
    [TestCase("int64","int64.MinValue","int64.MaxValue","x + y",true,"-1")]
    [TestCase("int64","int64.MaxValue","1","x + y",false,"0")]
    [TestCase("int64","int64.MinValue","-1","x + y",false,"0")]
    [TestCase("uint64","uint64.MaxValue","0","x + y",true,"uint64.MaxValue")]
    [TestCase("uint64","uint64.MaxValue","1","x + y",false,"0")]
    public async Task BoundedNativeTwoParameterAdditionBindsBothNamesAsync(
        string typeName, string first, string second, string expression,
        bool evaluable, string expected)
    {
        var evaluator = typeName switch {
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
                    init var name, init var bound);
                if (name.Start != {{typeName.Length + 1}} || name.Length != 5
                    || bound != {{expected}})
                {
                    return 1;
                }
                """
            : $$"""
                if ({{evaluator}}(move tokens, {{first}}, {{second}},
                    init var name, init var bound))
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
                Assert.That(hosted.HasErrors, Is.False, string.Join(Environment.NewLine, hosted.Diagnostics));
                Assert.That(NativeJit.Execute(((global::System.Func<BoundProgram>)(() => { var __nunitActual3 = hosted.Program; Assert.That(__nunitActual3, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual3!; }))(), policy), Is.EqualTo(42));
            }
            var program = await CompilePairParserAsync(text, entry, (BoundRuntimePolicy)policy).ConfigureAwait(true);
            Assert.That(NativeJit.Execute(program, policy), Is.EqualTo(42));
            await ExecuteAotAsync(program, policy).ConfigureAwait(true);
        }
    }
    [TestCase("int32 Value(int32 x, int32 x) { return x + x; }")]
    [TestCase("int32 Value(int32 x, uint32 y) { return x + y; }")]
    [TestCase("int32 Value(int32 x int32 y) { return x + y; }")]
    [TestCase("char16 Value(char16 x, char16 y) { return x + y; }")]
    [TestCase("int32 Value (int32 x, int32 y) { return x + y; }")]
    [TestCase("int32 Value\n(int32 x, int32 y) { return x + y; }")]
    [TestCase("int32 Value/* boundary */(int32 x, int32 y) { return x + y; }")]
    [TestCase("int32 Value/// boundary\n(int32 x, int32 y) { return x + y; }")]
    public async Task BoundedNativeTwoParameterAdditionRejectsInvalidDeclarationsAsync(string text)
    {
        const string Entry = """
            using Terrum.CodeAnalysis;
            int32 Main() {
                require TokenSnapshot.TryScan(init var tokens);
                if (TryEvaluateInt32TwoParameterAddReturn(move tokens, 1, 2,
                    init var name, init var value)) { return 1; }
                return 42;
            }
            """;
        foreach (var policy in new[] { NativeRuntimePolicy.FailFast, NativeRuntimePolicy.CheckedExceptions })
        {
            var program = await CompilePairParserAsync(text, Entry, (BoundRuntimePolicy)policy).ConfigureAwait(true);
            Assert.That(NativeJit.Execute(program, policy), Is.EqualTo(42));
            await ExecuteAotAsync(program, policy).ConfigureAwait(true);
        }
    }

    [Test]
    public async Task BoundedNativeTwoParameterAdditionKeepsOutputsVacantOnFailureAsync()
    {
        const string Text = "int32 Value(int32 x, int32 y) { return x + y; }";
        const string Entry = """
            using Terrum.CodeAnalysis;
            int32 Main() {
                require TokenSnapshot.TryScan(init var tokens);
                if (TryEvaluateInt32TwoParameterAddReturn(
                    move tokens, int32.MaxValue, 1, init var name, init var value))
                {
                    return 1;
                }
                require TokenSnapshot.TryScan(init var retryTokens);
                require TryEvaluateInt32TwoParameterAddReturn(
                    move retryTokens, 20, 22, init name, init value);
                if (name.Start != 6 || name.Length != 5 || value != 42) { return 2; }
                return 42;
            }
            """;
        foreach (var policy in new[] { NativeRuntimePolicy.FailFast, NativeRuntimePolicy.CheckedExceptions })
        {
            var program = await CompilePairParserAsync(Text, Entry, (BoundRuntimePolicy)policy)
                .ConfigureAwait(true);
            Assert.That(NativeJit.Execute(program, policy), Is.EqualTo(42));
            await ExecuteAotAsync(program, policy).ConfigureAwait(true);
        }
    }
    [TestCase("int32","43","1","x - y",true,"42")]
    [TestCase("int32","int32.MinValue","int32.MinValue","x - y",true,"0")]
    [TestCase("int32","1","2","x - y",true,"-1")]
    [TestCase("int32","int32.MaxValue","-1","x - y",false,"0")]
    [TestCase("int32","int32.MinValue","1","x - y",false,"0")]
    [TestCase("int32","1","2","y - x",false,"0")]
    [TestCase("int32","1","2","x - x",false,"0")]
    [TestCase("int32","1","2","x + y",false,"0")]
    [TestCase("uint32","uint32.MaxValue","0","x - y",true,"uint32.MaxValue")]
    [TestCase("uint32","uint32.MaxValue","uint32.MaxValue","x - y",true,"0")]
    [TestCase("uint32","0","1","x - y",false,"0")]
    [TestCase("int64","int64.MinValue","int64.MinValue","x - y",true,"0")]
    [TestCase("int64","int64.MinValue","-1","x - y",true,"int64.MinValue + 1")]
    [TestCase("int64","int64.MaxValue","-1","x - y",false,"0")]
    [TestCase("int64","int64.MinValue","1","x - y",false,"0")]
    [TestCase("uint64","uint64.MaxValue","0","x - y",true,"uint64.MaxValue")]
    [TestCase("uint64","uint64.MaxValue","uint64.MaxValue","x - y",true,"0")]
    [TestCase("uint64","0","1","x - y",false,"0")]
    public async Task BoundedNativeTwoParameterSubtractionChecksRangeAndOrderAsync(
        string typeName, string first, string second, string expression,
        bool evaluable, string expected)
    {
        var evaluator = typeName switch {
            "int32" => "TryEvaluateInt32TwoParameterSubtractReturn",
            "uint32" => "TryEvaluateUInt32TwoParameterSubtractReturn",
            "int64" => "TryEvaluateInt64TwoParameterSubtractReturn",
            "uint64" => "TryEvaluateUInt64TwoParameterSubtractReturn",
            _ => throw new ArgumentOutOfRangeException(nameof(typeName)),
        };
        var text = $"{typeName} Value({typeName} x, {typeName} y) {{ return {expression}; }}";
        var assertion = evaluable
            ? $$"""
                require {{evaluator}}(move tokens, {{first}}, {{second}},
                    init var name, init var bound);
                if (name.Start != {{typeName.Length + 1}} || name.Length != 5
                    || bound != {{expected}})
                {
                    return 1;
                }
                """
            : $$"""
                if ({{evaluator}}(move tokens, {{first}}, {{second}},
                    init var name, init var bound))
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
                Assert.That(hosted.HasErrors, Is.False, string.Join(Environment.NewLine, hosted.Diagnostics));
                Assert.That(NativeJit.Execute(((global::System.Func<BoundProgram>)(() => { var __nunitActual4 = hosted.Program; Assert.That(__nunitActual4, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual4!; }))(), policy), Is.EqualTo(42));
            }
            var program = await CompilePairParserAsync(text, entry, (BoundRuntimePolicy)policy).ConfigureAwait(true);
            Assert.That(NativeJit.Execute(program, policy), Is.EqualTo(42));
            await ExecuteAotAsync(program, policy).ConfigureAwait(true);
        }
    }
    [TestCase("int32","true","int32.MinValue","int32.MaxValue","if (flag) { return x; } else { return y; }",true,"int32.MinValue")]
    [TestCase("int32","false","int32.MinValue","int32.MaxValue","if (flag) { return x; } else { return y; }",true,"int32.MaxValue")]
    [TestCase("uint32","true","uint32.MaxValue","0","if (flag) { return x; } else { return y; }",true,"uint32.MaxValue")]
    [TestCase("uint32","false","uint32.MaxValue","0","if (flag) { return x; } else { return y; }",true,"0")]
    [TestCase("int64","true","int64.MinValue","int64.MaxValue","if (flag) { return x; } else { return y; }",true,"int64.MinValue")]
    [TestCase("int64","false","int64.MinValue","int64.MaxValue","if (flag) { return x; } else { return y; }",true,"int64.MaxValue")]
    [TestCase("uint64","true","uint64.MaxValue","0","if (flag) { return x; } else { return y; }",true,"uint64.MaxValue")]
    [TestCase("uint64","false","uint64.MaxValue","0","if (flag) { return x; } else { return y; }",true,"0")]
    [TestCase("bool","true","false","true","if (flag) { return x; } else { return y; }",true,"false")]
    [TestCase("bool","false","false","true","if (flag) { return x; } else { return y; }",true,"true")]
    [TestCase("char16","true","char16(65535)","char16(0)","if (flag) { return x; } else { return y; }",true,"char16(65535)")]
    [TestCase("char16","false","char16(65535)","char16(0)","if (flag) { return x; } else { return y; }",true,"char16(0)")]
    [TestCase("int32","true","1","2","if (wrong) { return x; } else { return y; }",false,"0")]
    [TestCase("int32","true","1","2","if (flag) { return y; } else { return x; }",false,"0")]
    [TestCase("int32","true","1","2","if (flag) { return x; } else { return x; }",false,"0")]
    [TestCase("int32","true","1","2","if (flag) { return x; }",false,"0")]
    public async Task BoundedNativeConditionalReturnBindsBothBranchValuesAsync(
        string typeName, string condition, string first, string second, string body,
        bool evaluable, string expected)
    {
        var evaluator = typeName switch {
            "int32" => "TryEvaluateInt32ScalarConditionalReturn",
            "uint32" => "TryEvaluateUInt32ScalarConditionalReturn",
            "int64" => "TryEvaluateInt64ScalarConditionalReturn",
            "uint64" => "TryEvaluateUInt64ScalarConditionalReturn",
            "bool" => "TryEvaluateBoolScalarConditionalReturn",
            "char16" => "TryEvaluateChar16ScalarConditionalReturn",
            _ => throw new ArgumentOutOfRangeException(nameof(typeName)),
        };
        var text = $"{typeName} Value(bool flag, {typeName} x, {typeName} y) {{ {body} }}";
        var assertion = evaluable
            ? $$"""
                require {{evaluator}}(move tokens, {{condition}}, {{first}}, {{second}},
                    init var name, init var bound);
                if (name.Start != {{typeName.Length + 1}} || name.Length != 5
                    || bound != {{expected}})
                {
                    return 1;
                }
                """
            : $$"""
                if ({{evaluator}}(move tokens, {{condition}}, {{first}}, {{second}},
                    init var name, init var bound))
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
                         {{typeName}} actual = Value({{condition}}, {{first}}, {{second}});
                         if (actual == {{expected}}) { return 42; }
                         return 1;
                     }
                    """;
                var hosted = Compilation.Create(SyntaxTree.Parse(source), (BoundRuntimePolicy)policy);
                Assert.That(hosted.HasErrors, Is.False, string.Join(Environment.NewLine, hosted.Diagnostics));
                Assert.That(NativeJit.Execute(((global::System.Func<BoundProgram>)(() => { var __nunitActual5 = hosted.Program; Assert.That(__nunitActual5, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual5!; }))(), policy), Is.EqualTo(42));
            }
            var program = await CompileAsync(text, entry, (BoundRuntimePolicy)policy,
                "ScalarConditionalParser").ConfigureAwait(true);
            Assert.That(NativeJit.Execute(program, policy), Is.EqualTo(42));
            await ExecuteAotAsync(program, policy).ConfigureAwait(true);
        }
    }
    [TestCase("int32","true","1","2","if (flag) { return x + y; } else { return x; }",true,"3")]
    [TestCase("int32","false","int32.MaxValue","1","if (flag) { return x + y; } else { return x; }",true,"int32.MaxValue")]
    [TestCase("int32","true","int32.MaxValue","1","if (flag) { return x + y; } else { return x; }",false,"0")]
    [TestCase("int32","true","int32.MinValue","-1","if (flag) { return x + y; } else { return x; }",false,"0")]
    [TestCase("uint32","true","uint32.MaxValue","1","if (flag) { return x + y; } else { return x; }",false,"0")]
    [TestCase("uint32","false","uint32.MaxValue","1","if (flag) { return x + y; } else { return x; }",true,"uint32.MaxValue")]
    [TestCase("int64","true","int64.MaxValue","-1","if (flag) { return x + y; } else { return x; }",true,"int64.MaxValue - 1")]
    [TestCase("int64","true","int64.MaxValue","1","if (flag) { return x + y; } else { return x; }",false,"0")]
    [TestCase("uint64","true","uint64.MaxValue","1","if (flag) { return x + y; } else { return x; }",false,"0")]
    [TestCase("uint64","false","uint64.MaxValue","1","if (flag) { return x + y; } else { return x; }",true,"uint64.MaxValue")]
    [TestCase("int32","true","1","2","if (missing) { return x + y; } else { return x; }",false,"0")]
    [TestCase("int32","true","1","2","if (flag) { return y + x; } else { return x; }",false,"0")]
    [TestCase("int32","true","1","2","if (flag) { return x + x; } else { return x; }",false,"0")]
    [TestCase("int32","true","1","2","if (flag) { return x + y; } else { return y; }",false,"0")]
    [TestCase("int32","true","1","2","if (flag) { return x - y; } else { return x; }",false,"0")]
    public async Task BoundedNativeConditionalAddSkipsUnselectedCheckedOperationAsync(
        string typeName, string condition, string first, string second, string body,
        bool evaluable, string expected)
    {
        var evaluator = typeName switch {
            "int32" => "TryEvaluateInt32ScalarConditionalAddReturn",
            "uint32" => "TryEvaluateUInt32ScalarConditionalAddReturn",
            "int64" => "TryEvaluateInt64ScalarConditionalAddReturn",
            "uint64" => "TryEvaluateUInt64ScalarConditionalAddReturn",
            _ => throw new ArgumentOutOfRangeException(nameof(typeName)),
        };
        var text = $"{typeName} Value(bool flag, {typeName} x, {typeName} y) {{ {body} }}";
        var assertion = evaluable
            ? $$"""
                require {{evaluator}}(move tokens, {{condition}}, {{first}}, {{second}},
                    init var name, init var bound);
                if (name.Start != {{typeName.Length + 1}} || name.Length != 5
                    || bound != {{expected}})
                {
                    return 1;
                }
                """
            : $$"""
                if ({{evaluator}}(move tokens, {{condition}}, {{first}}, {{second}},
                    init var name, init var bound))
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
                         {{typeName}} actual = Value({{condition}}, {{first}}, {{second}});
                         if (actual == {{expected}}) { return 42; }
                         return 1;
                     }
                    """;
                var hosted = Compilation.Create(SyntaxTree.Parse(source), (BoundRuntimePolicy)policy);
                Assert.That(hosted.HasErrors, Is.False, string.Join(Environment.NewLine, hosted.Diagnostics));
                Assert.That(NativeJit.Execute(((global::System.Func<BoundProgram>)(() => { var __nunitActual6 = hosted.Program; Assert.That(__nunitActual6, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual6!; }))(), policy), Is.EqualTo(42));
            }
            var program = await CompileAsync(text, entry, (BoundRuntimePolicy)policy,
                "ScalarConditionalAddParser").ConfigureAwait(true);
            Assert.That(NativeJit.Execute(program, policy), Is.EqualTo(42));
            await ExecuteAotAsync(program, policy).ConfigureAwait(true);
        }
    }
    [TestCase("int32","int32.MinValue","int32.MaxValue","x < y",true,"int32.MinValue")]
    [TestCase("int32","int32.MinValue","int32.MaxValue","x > y",true,"int32.MaxValue")]
    [TestCase("int32","1","1","x <= y",true,"1")]
    [TestCase("int32","1","2","x == y",true,"2")]
    [TestCase("int32","1","2","x != y",true,"1")]
    [TestCase("int32","1","2","x >= y",true,"2")]
    [TestCase("uint32","uint32.MaxValue","0","x > y",true,"uint32.MaxValue")]
    [TestCase("uint32","uint32.MaxValue","0","x < y",true,"0")]
    [TestCase("int64","int64.MinValue","int64.MaxValue","x < y",true,"int64.MinValue")]
    [TestCase("uint64","0","uint64.MaxValue","x >= y",true,"uint64.MaxValue")]
    [TestCase("char16","char16(65535)","char16(0)","x != y",true,"char16(65535)")]
    [TestCase("char16","char16(0)","char16(65535)","x > y",true,"char16(65535)")]
    [TestCase("int32","1","2","y < x",false,"0")]
    [TestCase("int32","1","2","x < x",false,"0")]
    [TestCase("int32","1","2","x + y",false,"0")]
    public async Task BoundedNativeComparisonConditionalBindsPredicateAndReturnsAsync(
        string typeName, string first, string second, string comparison,
        bool evaluable, string expected)
    {
        var evaluator = typeName switch {
            "int32" => "TryEvaluateInt32ComparisonConditionalReturn",
            "uint32" => "TryEvaluateUInt32ComparisonConditionalReturn",
            "int64" => "TryEvaluateInt64ComparisonConditionalReturn",
            "uint64" => "TryEvaluateUInt64ComparisonConditionalReturn",
            "char16" => "TryEvaluateChar16ComparisonConditionalReturn",
            _ => throw new ArgumentOutOfRangeException(nameof(typeName)),
        };
        var text = $$"""
            {{typeName}} Value({{typeName}} x, {{typeName}} y) {
                if ({{comparison}}) { return x; } else { return y; }
            }
            """;
        var assertion = evaluable
            ? $$"""
                require {{evaluator}}(move tokens, {{first}}, {{second}},
                    init var name, init var bound);
                if (name.Start != {{typeName.Length + 1}} || name.Length != 5
                    || bound != {{expected}})
                {
                    return 1;
                }
                """
            : $$"""
                if ({{evaluator}}(move tokens, {{first}}, {{second}},
                    init var name, init var bound))
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
                Assert.That(hosted.HasErrors, Is.False, string.Join(Environment.NewLine, hosted.Diagnostics));
                Assert.That(NativeJit.Execute(((global::System.Func<BoundProgram>)(() => { var __nunitActual7 = hosted.Program; Assert.That(__nunitActual7, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual7!; }))(), policy), Is.EqualTo(42));
            }
            var program = await CompileAsync(text, entry, (BoundRuntimePolicy)policy,
                "ScalarComparisonConditionalParser").ConfigureAwait(true);
            Assert.That(NativeJit.Execute(program, policy), Is.EqualTo(42));
            await ExecuteAotAsync(program, policy).ConfigureAwait(true);
        }
    }
    [TestCase("int32","-1","0","x < y",true,true)]
    [TestCase("int32","-1","0","x <= y",true,true)]
    [TestCase("int32","-1","0","x > y",true,false)]
    [TestCase("int32","-1","0","x >= y",true,false)]
    [TestCase("int32","-1","-1","x == y",true,true)]
    [TestCase("int32","-1","-1","x != y",true,false)]
    [TestCase("int32","1","2","y < x",false,false)]
    [TestCase("int32","1","2","x < x",false,false)]
    [TestCase("int32","1","2","x + y",false,false)]
    [TestCase("uint32","uint32.MaxValue","0","x > y",true,true)]
    [TestCase("uint32","uint32.MaxValue","uint32.MaxValue","x >= y",true,true)]
    [TestCase("int64","int64.MinValue","int64.MaxValue","x < y",true,true)]
    [TestCase("int64","int64.MaxValue","int64.MinValue","x <= y",true,false)]
    [TestCase("uint64","uint64.MaxValue","0","x != y",true,true)]
    [TestCase("uint64","0","uint64.MaxValue","x > y",true,false)]
    [TestCase("char16","char16(65535)","char16(0)","x > y",true,true)]
    [TestCase("char16","char16(0)","char16(65535)","x == y",true,false)]
    public async Task BoundedNativeTwoParameterComparisonsBindBothNamesAsync(
        string typeName, string first, string second, string expression,
        bool evaluable, bool expected)
    {
        var evaluator = typeName switch {
            "int32" => "TryEvaluateInt32TwoParameterComparisonReturn",
            "uint32" => "TryEvaluateUInt32TwoParameterComparisonReturn",
            "int64" => "TryEvaluateInt64TwoParameterComparisonReturn",
            "uint64" => "TryEvaluateUInt64TwoParameterComparisonReturn",
            "char16" => "TryEvaluateChar16TwoParameterComparisonReturn",
            _ => throw new ArgumentOutOfRangeException(nameof(typeName)),
        };
        var text = $"bool Value({typeName} x, {typeName} y) {{ return {expression}; }}";
        var expectedValue = expected ? "true" : "false";
        var assertion = evaluable
            ? $$"""
                require {{evaluator}}(move tokens, {{first}}, {{second}},
                    init var name, init var bound);
                if (name.Start != 5 || name.Length != 5 || bound != {{expectedValue}})
                {
                    return 1;
                }
                """
            : $$"""
                if ({{evaluator}}(move tokens, {{first}}, {{second}},
                    init var name, init var bound))
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
                         if (Value({{first}}, {{second}}) == {{expectedValue}}) { return 42; }
                         return 1;
                     }
                    """;
                var hosted = Compilation.Create(SyntaxTree.Parse(source), (BoundRuntimePolicy)policy);
                Assert.That(hosted.HasErrors, Is.False, string.Join(Environment.NewLine, hosted.Diagnostics));
                Assert.That(NativeJit.Execute(((global::System.Func<BoundProgram>)(() => { var __nunitActual8 = hosted.Program; Assert.That(__nunitActual8, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual8!; }))(), policy), Is.EqualTo(42));
            }
            var program = await CompilePairParserAsync(text, entry, (BoundRuntimePolicy)policy).ConfigureAwait(true);
            Assert.That(NativeJit.Execute(program, policy), Is.EqualTo(42));
            await ExecuteAotAsync(program, policy).ConfigureAwait(true);
        }
    }
    [TestCase("int32","18","21","x + y + 3",true,"42")]
    [TestCase("int32","int32.MaxValue","-1","x + y + 1",true,"int32.MaxValue")]
    [TestCase("int32","int32.MinValue","int32.MaxValue","x + y + 1",true,"0")]
    [TestCase("int32","0","0","x + y + 0xFFFF_FFFF",true,"-1")]
    [TestCase("int32","int32.MaxValue","1","x + y + 0xFFFF_FFFF",false,"0")]
    [TestCase("int32","int32.MinValue","-1","x + y + 1",false,"0")]
    [TestCase("int32","int32.MaxValue","0","x + y + 1",false,"0")]
    [TestCase("int32","int32.MinValue","0","x + y + 0xFFFF_FFFF",false,"0")]
    [TestCase("int32","0","0","x + y + 2147483648",false,"0")]
    [TestCase("int32","0","0","x + y + 4294967296",false,"0")]
    [TestCase("int32","0","0","x + z + 3",false,"0")]
    [TestCase("int32","0","0","x + y + 3 + 4",false,"0")]
    [TestCase("uint32","uint32.MaxValue","0","x + y + 0",true,"uint32.MaxValue")]
    [TestCase("uint32","0","0","x + y + 0xFFFF_FFFF",true,"uint32.MaxValue")]
    [TestCase("uint32","uint32.MaxValue","1","x + y + 0xFFFF_FFFF",false,"0")]
    [TestCase("uint32","uint32.MaxValue","0","x + y + 1",false,"0")]
    [TestCase("uint32","0","0","x + y + 4294967296",false,"0")]
    [TestCase("int64","int64.MinValue","int64.MaxValue","x + y + 1",true,"0")]
    [TestCase("int64","int64.MaxValue","0","x + y + 0xFFFF_FFFF_FFFF_FFFF",true,"int64.MaxValue - 1")]
    [TestCase("int64","int64.MaxValue","1","x + y + 0xFFFF_FFFF_FFFF_FFFF",false,"0")]
    [TestCase("int64","int64.MinValue","0","x + y + 0xFFFF_FFFF_FFFF_FFFF",false,"0")]
    [TestCase("int64","int64.MaxValue","0","x + y + 1",false,"0")]
    [TestCase("int64","0","0","x + y + 9223372036854775808",false,"0")]
    [TestCase("uint64","uint64.MaxValue","0","x + y + 0",true,"uint64.MaxValue")]
    [TestCase("uint64","0","0","x + y + 18446744073709551615",true,"uint64.MaxValue")]
    [TestCase("uint64","uint64.MaxValue","1","x + y + 0",false,"0")]
    [TestCase("uint64","uint64.MaxValue","0","x + y + 1",false,"0")]
    [TestCase("uint64","0","0","x + y + 18446744073709551616",false,"0")]
    public async Task BoundedNativeTwoParameterChainedAdditionChecksEachStepAsync(
        string typeName, string first, string second, string expression,
        bool evaluable, string expected)
    {
        var evaluator = typeName switch {
            "int32" => "TryEvaluateInt32TwoParameterAddAndLiteralReturn",
            "uint32" => "TryEvaluateUInt32TwoParameterAddAndLiteralReturn",
            "int64" => "TryEvaluateInt64TwoParameterAddAndLiteralReturn",
            "uint64" => "TryEvaluateUInt64TwoParameterAddAndLiteralReturn",
            _ => throw new ArgumentOutOfRangeException(nameof(typeName)),
        };
        var text = $"{typeName} Value({typeName} x, {typeName} y) {{ return {expression}; }}";
        var assertion = evaluable
            ? $$"""
                require {{evaluator}}(
                    move tokens, move integers, {{first}}, {{second}},
                    init var name, init var bound);
                if (name.Start != {{typeName.Length + 1}} || name.Length != 5
                    || bound != {{expected}})
                {
                    return 1;
                }
                """
            : $$"""
                if ({{evaluator}}(
                    move tokens, move integers, {{first}}, {{second}},
                    init var name, init var bound))
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
                         {{typeName}} actual = Value({{first}}, {{second}});
                         if (actual == {{expected}}) { return 42; }
                         return 1;
                     }
                    """;
                var hosted = Compilation.Create(SyntaxTree.Parse(source), (BoundRuntimePolicy)policy);
                Assert.That(hosted.HasErrors, Is.False, string.Join(Environment.NewLine, hosted.Diagnostics));
                Assert.That(NativeJit.Execute(((global::System.Func<BoundProgram>)(() => { var __nunitActual9 = hosted.Program; Assert.That(__nunitActual9, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual9!; }))(), policy), Is.EqualTo(42));
            }
            var program = await CompilePairParserAsync(text, entry, (BoundRuntimePolicy)policy).ConfigureAwait(true);
            Assert.That(NativeJit.Execute(program, policy), Is.EqualTo(42));
            await ExecuteAotAsync(program, policy).ConfigureAwait(true);
        }
    }
    [TestCase("int32","-1","x < 0",true,true)]
    [TestCase("int32","-1","x == 0xFFFF_FFFF",true,true)]
    [TestCase("int32","int32.MaxValue","x <= 2147483647",true,true)]
    [TestCase("int32","0","x < 2147483648",false,false)]
    [TestCase("uint32","uint32.MaxValue","x >= 0xFFFF_FFFF",true,true)]
    [TestCase("uint32","0","x < 4294967296",false,false)]
    [TestCase("int64","int64.MinValue","x < 0",true,true)]
    [TestCase("int64","-1","x == 0xFFFF_FFFF_FFFF_FFFF",true,true)]
    [TestCase("int64","0","x < 9223372036854775808",false,false)]
    [TestCase("uint64","uint64.MaxValue","x > 18446744073709551614",true,true)]
    [TestCase("uint64","0","x > 18446744073709551616",false,false)]
    [TestCase("char16","char16(65535)","x >= 65535",true,true)]
    [TestCase("char16","char16(0)","x != 0",true,false)]
    [TestCase("char16","char16(0)","x != 65536",false,false)]
    [TestCase("int32","1","y == 1",false,false)]
    public async Task BoundedNativeNamedParameterComparisonUsesOwnedRecordsAsync(
        string typeName, string parameter, string expression, bool evaluable, bool expected)
    {
        var evaluator = typeName switch {
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
                    init var name, init var bound);
                if (name.Start != 5 || name.Length != 5 || bound != {{expectedValue}})
                {
                    return 1;
                }
                """
            : $$"""
                if ({{evaluator}}(move tokens, move integers, {{parameter}},
                    init var name, init var bound))
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
                Assert.That(hosted.HasErrors, Is.False, string.Join(Environment.NewLine, hosted.Diagnostics));
                Assert.That(NativeJit.Execute(((global::System.Func<BoundProgram>)(() => { var __nunitActual10 = hosted.Program; Assert.That(__nunitActual10, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual10!; }))(), policy), Is.EqualTo(42));
            }
            var program = await CompileParserAsync(text, entry, (BoundRuntimePolicy)policy).ConfigureAwait(true);
            Assert.That(NativeJit.Execute(program, policy), Is.EqualTo(42));
            await ExecuteAotAsync(program, policy).ConfigureAwait(true);
        }
    }
    [TestCase("int32 Main() { return 1 + 2; }",true,3)]
    [TestCase("int32 Main() { return 2147483647 + 0; }",true,int.MaxValue)]
    [TestCase("int32 Main() { return 2147483647 + 1; }",false,0)]
    [TestCase("int32 Main() { return 0xFFFF_FFFF + 1; }",true,0)]
    [TestCase("int32 Main() { return 0x8000_0000 + 0; }",true,int.MinValue)]
    [TestCase("int32 Main() { return 2147483648 + 0; }",false,0)]
    [TestCase("int32 Main() { return 1 - 2; }",false,0)]
    [TestCase("uint32 Main() { return 1 + 2; }",false,0)]
    public async Task BoundedNativeInt32AdditionUsesOwnedIntegerRecordsAsync(
        string text, bool evaluable, int value)
    {
        var assertion = evaluable
            ? FormattableString.Invariant($$"""
                require TryEvaluateInt32AddReturn(move tokens, move integers, init var name, init var bound);
                if (name.Start != 6 || name.Length != 4 || bound != {{value}}) { return 1; }
                """)
            : """
                if (TryEvaluateInt32AddReturn(move tokens, move integers,
                    init var name, init var bound)) { return 1; }
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
                Assert.That(hosted.HasErrors, Is.False, string.Join(Environment.NewLine, hosted.Diagnostics));
                Assert.That(NativeJit.Execute(((global::System.Func<BoundProgram>)(() => { var __nunitActual11 = hosted.Program; Assert.That(__nunitActual11, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual11!; }))(), policy), Is.EqualTo(value));
            }
            var program = await CompileParserAsync(text, entry, (BoundRuntimePolicy)policy).ConfigureAwait(true);
            Assert.That(NativeJit.Execute(program, policy), Is.EqualTo(42));
            await ExecuteAotAsync(program, policy).ConfigureAwait(true);
        }
    }
    [TestCase("uint32 Value() { return 1 + 2; }",true,3u)]
    [TestCase("uint32 Value() { return 4294967295 + 0; }",true,uint.MaxValue)]
    [TestCase("uint32 Value() { return 0xFFFF_FFFF + 0; }",true,uint.MaxValue)]
    [TestCase("uint32 Value() { return 4294967295 + 1; }",false,0u)]
    [TestCase("uint32 Value() { return 4294967296 + 0; }",false,0u)]
    [TestCase("int32 Value() { return 1 + 2; }",false,0u)]
    public async Task BoundedNativeUInt32AdditionUsesOwnedIntegerRecordsAsync(
        string text, bool evaluable, uint value)
    {
        var assertion = evaluable
            ? FormattableString.Invariant($$"""
                require TryEvaluateUInt32AddReturn(move tokens, move integers, init var name, init var bound);
                if (name.Start != 7 || name.Length != 5 || bound != {{value}}) { return 1; }
                """)
            : """
                if (TryEvaluateUInt32AddReturn(move tokens, move integers,
                    init var name, init var bound)) { return 1; }
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
                Assert.That(hosted.HasErrors, Is.False, string.Join(Environment.NewLine, hosted.Diagnostics));
                Assert.That(NativeJit.Execute(((global::System.Func<BoundProgram>)(() => { var __nunitActual12 = hosted.Program; Assert.That(__nunitActual12, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual12!; }))(), policy), Is.EqualTo(42));
            }
            var program = await CompileParserAsync(text, entry, (BoundRuntimePolicy)policy).ConfigureAwait(true);
            Assert.That(NativeJit.Execute(program, policy), Is.EqualTo(42));
            await ExecuteAotAsync(program, policy).ConfigureAwait(true);
        }
    }
    [TestCase("int64 Value() { return 4294967296 + 1; }",true,4294967297L)]
    [TestCase("int64 Value() { return 9223372036854775807 + 0; }",true,long.MaxValue)]
    [TestCase("int64 Value() { return 0x8000_0000_0000_0000 + 0; }",true,long.MinValue)]
    [TestCase("int64 Value() { return 0xFFFF_FFFF_FFFF_FFFF + 1; }",true,0L)]
    [TestCase("int64 Value() { return 9223372036854775807 + 1; }",false,0L)]
    [TestCase("int64 Value() { return 0x8000_0000_0000_0000 + 0xFFFF_FFFF_FFFF_FFFF; }",false,0L)]
    [TestCase("int64 Value() { return 9223372036854775808 + 0; }",false,0L)]
    public async Task BoundedNativeInt64AdditionUsesOwnedIntegerRecordsAsync(
        string text, bool evaluable, long value)
    {
        var expected = value == long.MinValue ? "int64.MinValue"
            : value.ToString(CultureInfo.InvariantCulture);
        var assertion = evaluable
            ? $$"""
                require TryEvaluateInt64AddReturn(move tokens, move integers, init var name, init var bound);
                if (name.Start != 6 || name.Length != 5 || bound != {{expected}}) { return 1; }
                """
            : """
                if (TryEvaluateInt64AddReturn(move tokens, move integers,
                    init var name, init var bound)) { return 1; }
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
                Assert.That(hosted.HasErrors, Is.False, string.Join(Environment.NewLine, hosted.Diagnostics));
                Assert.That(NativeJit.Execute(((global::System.Func<BoundProgram>)(() => { var __nunitActual13 = hosted.Program; Assert.That(__nunitActual13, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual13!; }))(), policy), Is.EqualTo(42));
            }
            var program = await CompileParserAsync(text, entry, (BoundRuntimePolicy)policy).ConfigureAwait(true);
            Assert.That(NativeJit.Execute(program, policy), Is.EqualTo(42));
            await ExecuteAotAsync(program, policy).ConfigureAwait(true);
        }
    }
    [TestCase("uint64 Value() { return 4294967296 + 1; }",true,4294967297ul)]
    [TestCase("uint64 Value() { return 18446744073709551615 + 0; }",true,ulong.MaxValue)]
    [TestCase("uint64 Value() { return 0xFFFF_FFFF_FFFF_FFFF + 0; }",true,ulong.MaxValue)]
    [TestCase("uint64 Value() { return 18446744073709551615 + 1; }",false,0ul)]
    [TestCase("uint64 Value() { return 18446744073709551616 + 0; }",false,0ul)]
    public async Task BoundedNativeUInt64AdditionUsesOwnedIntegerRecordsAsync(
        string text, bool evaluable, ulong value)
    {
        var assertion = evaluable
            ? FormattableString.Invariant($$"""
                require TryEvaluateUInt64AddReturn(move tokens, move integers, init var name, init var bound);
                if (name.Start != 7 || name.Length != 5 || bound != {{value}}) { return 1; }
                """)
            : """
                if (TryEvaluateUInt64AddReturn(move tokens, move integers,
                    init var name, init var bound)) { return 1; }
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
                Assert.That(hosted.HasErrors, Is.False, string.Join(Environment.NewLine, hosted.Diagnostics));
                Assert.That(NativeJit.Execute(((global::System.Func<BoundProgram>)(() => { var __nunitActual14 = hosted.Program; Assert.That(__nunitActual14, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual14!; }))(), policy), Is.EqualTo(42));
            }
            var program = await CompileParserAsync(text, entry, (BoundRuntimePolicy)policy).ConfigureAwait(true);
            Assert.That(NativeJit.Execute(program, policy), Is.EqualTo(42));
            await ExecuteAotAsync(program, policy).ConfigureAwait(true);
        }
    }
    [TestCase("int32","8 - 3",true,"5")]
    [TestCase("int32","0 - 0xFFFF_FFFF",true,"1")]
    [TestCase("int32","0x8000_0000 - 0x8000_0000",true,"0")]
    [TestCase("int32","0x8000_0000 - 1",false,"0")]
    [TestCase("int32","2147483647 - 0xFFFF_FFFF",false,"0")]
    [TestCase("int32","2147483648 - 0",false,"0")]
    [TestCase("uint32","8 - 3",true,"5")]
    [TestCase("uint32","0xFFFF_FFFF - 1",true,"4294967294")]
    [TestCase("uint32","0 - 1",false,"0")]
    [TestCase("uint32","4294967296 - 0",false,"0")]
    [TestCase("int64","4294967296 - 1",true,"4294967295")]
    [TestCase("int64","0x8000_0000_0000_0000 - 0xFFFF_FFFF_FFFF_FFFF",true,"-9223372036854775807")]
    [TestCase("int64","0x8000_0000_0000_0000 - 1",false,"0")]
    [TestCase("int64","9223372036854775807 - 0xFFFF_FFFF_FFFF_FFFF",false,"0")]
    [TestCase("int64","9223372036854775808 - 0",false,"0")]
    [TestCase("uint64","4294967296 - 1",true,"4294967295")]
    [TestCase("uint64","18446744073709551615 - 1",true,"18446744073709551614")]
    [TestCase("uint64","0 - 1",false,"0")]
    [TestCase("uint64","18446744073709551616 - 0",false,"0")]
    public async Task BoundedNativeSubtractionUsesOwnedIntegerRecordsAsync(
        string typeName, string expression, bool evaluable, string expected)
    {
        var evaluator = typeName switch {
            "int32" => "TryEvaluateInt32SubtractReturn",
            "uint32" => "TryEvaluateUInt32SubtractReturn",
            "int64" => "TryEvaluateInt64SubtractReturn",
            "uint64" => "TryEvaluateUInt64SubtractReturn",
            _ => throw new ArgumentOutOfRangeException(nameof(typeName)),
        };
        var text = $"{typeName} Value() {{ return {expression}; }}";
        var assertion = evaluable
            ? $$"""
                require {{evaluator}}(move tokens, move integers, init var name, init var bound);
                if (name.Start != {{typeName.Length + 1}} || name.Length != 5
                    || bound != {{expected}}) { return 1; }
                """
            : $$"""
                if ({{evaluator}}(move tokens, move integers,
                    init var name, init var bound)) { return 1; }
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
                Assert.That(hosted.HasErrors, Is.False, string.Join(Environment.NewLine, hosted.Diagnostics));
                Assert.That(NativeJit.Execute(((global::System.Func<BoundProgram>)(() => { var __nunitActual15 = hosted.Program; Assert.That(__nunitActual15, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual15!; }))(), policy), Is.EqualTo(42));
            }
            var program = await CompileParserAsync(text, entry, (BoundRuntimePolicy)policy).ConfigureAwait(true);
            Assert.That(NativeJit.Execute(program, policy), Is.EqualTo(42));
            await ExecuteAotAsync(program, policy).ConfigureAwait(true);
        }
    }
    [TestCase("int32","0xFFFF_FFFF * 2",true,"-2")]
    [TestCase("int32","46340 * 46340",true,"2147395600")]
    [TestCase("int32","46341 * 46341",false,"0")]
    [TestCase("int32","2147483648 * 0",false,"0")]
    [TestCase("uint32","65535 * 65537",true,"4294967295")]
    [TestCase("uint32","65536 * 65536",false,"0")]
    [TestCase("uint32","4294967295 * 0",true,"0")]
    [TestCase("int64","0x8000_0000_0000_0000 * 1",true,"-9223372036854775808")]
    [TestCase("int64","0x8000_0000_0000_0000 * 0xFFFF_FFFF_FFFF_FFFF",false,"0")]
    [TestCase("int64","0xFFFF_FFFF_FFFF_FFFF * 2",true,"-2")]
    [TestCase("int64","3037000499 * 3037000499",true,"9223372030926249001")]
    [TestCase("int64","3037000500 * 3037000500",false,"0")]
    [TestCase("uint64","4294967295 * 4294967297",true,"18446744073709551615")]
    [TestCase("uint64","4294967296 * 4294967296",false,"0")]
    [TestCase("uint64","18446744073709551615 * 0",true,"0")]
    [TestCase("uint64","18446744073709551616 * 0",false,"0")]
    public async Task BoundedNativeMultiplicationUsesOwnedIntegerRecordsAsync(
        string typeName, string expression, bool evaluable, string expected)
    {
        var evaluator = typeName switch {
            "int32" => "TryEvaluateInt32MultiplyReturn",
            "uint32" => "TryEvaluateUInt32MultiplyReturn",
            "int64" => "TryEvaluateInt64MultiplyReturn",
            "uint64" => "TryEvaluateUInt64MultiplyReturn",
            _ => throw new ArgumentOutOfRangeException(nameof(typeName)),
        };
        var text = $"{typeName} Value() {{ return {expression}; }}";
        var assertion = evaluable
            ? $$"""
                require {{evaluator}}(move tokens, move integers, init var name, init var bound);
                if (name.Start != {{typeName.Length + 1}} || name.Length != 5
                    || bound != {{expected}}) { return 1; }
                """
            : $$"""
                if ({{evaluator}}(move tokens, move integers,
                    init var name, init var bound)) { return 1; }
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
                Assert.That(hosted.HasErrors, Is.False, string.Join(Environment.NewLine, hosted.Diagnostics));
                Assert.That(NativeJit.Execute(((global::System.Func<BoundProgram>)(() => { var __nunitActual16 = hosted.Program; Assert.That(__nunitActual16, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual16!; }))(), policy), Is.EqualTo(42));
            }
            var program = await CompileParserAsync(text, entry, (BoundRuntimePolicy)policy).ConfigureAwait(true);
            Assert.That(NativeJit.Execute(program, policy), Is.EqualTo(42));
            await ExecuteAotAsync(program, policy).ConfigureAwait(true);
        }
    }
    [TestCase("int32","7 / 2",true,"3")]
    [TestCase("int32","0xFFFF_FFFF / 2",true,"0")]
    [TestCase("int32","7 % 2",true,"1")]
    [TestCase("int32","0xFFFF_FFFF % 2",true,"-1")]
    [TestCase("int32","0x8000_0000 / 0xFFFF_FFFF",false,"0")]
    [TestCase("int32","0x8000_0000 % 0xFFFF_FFFF",true,"0")]
    [TestCase("uint32","4294967295 / 2",true,"2147483647")]
    [TestCase("uint32","4294967295 % 2",true,"1")]
    [TestCase("uint32","7 / 0",false,"0")]
    [TestCase("int64","0x8000_0000_0000_0000 / 2",true,"-4611686018427387904")]
    [TestCase("int64","0xFFFF_FFFF_FFFF_FFFF % 2",true,"-1")]
    [TestCase("int64","0x8000_0000_0000_0000 / 0xFFFF_FFFF_FFFF_FFFF",false,"0")]
    [TestCase("int64","0x8000_0000_0000_0000 % 0xFFFF_FFFF_FFFF_FFFF",true,"0")]
    [TestCase("uint64","18446744073709551615 / 2",true,"9223372036854775807")]
    [TestCase("uint64","18446744073709551615 % 2",true,"1")]
    [TestCase("uint64","18446744073709551616 / 2",false,"0")]
    [TestCase("uint64","7 % 0",false,"0")]
    public async Task BoundedNativeDivisionAndRemainderUseOwnedIntegerRecordsAsync(
        string typeName, string expression, bool evaluable, string expected)
    {
        var width = typeName switch {
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
                require {{evaluator}}(move tokens, move integers, init var name, init var bound);
                if (name.Start != {{typeName.Length + 1}} || name.Length != 5
                    || bound != {{expected}}) { return 1; }
                """
            : $$"""
                if ({{evaluator}}(move tokens, move integers,
                    init var name, init var bound)) { return 1; }
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
                Assert.That(hosted.HasErrors, Is.False, string.Join(Environment.NewLine, hosted.Diagnostics));
                Assert.That(NativeJit.Execute(((global::System.Func<BoundProgram>)(() => { var __nunitActual17 = hosted.Program; Assert.That(__nunitActual17, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual17!; }))(), policy), Is.EqualTo(42));
            }
            var program = await CompileParserAsync(text, entry, (BoundRuntimePolicy)policy).ConfigureAwait(true);
            Assert.That(NativeJit.Execute(program, policy), Is.EqualTo(42));
            await ExecuteAotAsync(program, policy).ConfigureAwait(true);
        }
    }
    [TestCase("int32","0xFFFF_FFFF & 0xAAAA_AAAA","-1431655766")]
    [TestCase("int32","0x8000_0000 | 1","-2147483647")]
    [TestCase("int32","0xFFFF_FFFF ^ 0xAAAA_AAAA","1431655765")]
    [TestCase("uint32","0xFFFF_FFFF & 0xAAAA_AAAA","2863311530")]
    [TestCase("uint32","0x8000_0000 | 1","2147483649")]
    [TestCase("uint32","0xFFFF_FFFF ^ 0xAAAA_AAAA","1431655765")]
    [TestCase("int64","0xFFFF_FFFF_FFFF_FFFF & 0xAAAA_AAAA_AAAA_AAAA","-6148914691236517206")]
    [TestCase("int64","0x8000_0000_0000_0000 | 1","-9223372036854775807")]
    [TestCase("int64","0xFFFF_FFFF_FFFF_FFFF ^ 0xAAAA_AAAA_AAAA_AAAA","6148914691236517205")]
    [TestCase("uint64","0xFFFF_FFFF_FFFF_FFFF & 0xAAAA_AAAA_AAAA_AAAA","12297829382473034410")]
    [TestCase("uint64","0x8000_0000_0000_0000 | 1","9223372036854775809")]
    [TestCase("uint64","0xFFFF_FFFF_FFFF_FFFF ^ 0xAAAA_AAAA_AAAA_AAAA","6148914691236517205")]
    public async Task BoundedNativeBitwiseOperationsUseOwnedIntegerRecordsAsync(
        string typeName, string expression, string expected)
    {
        var width = typeName switch {
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
                require {{evaluator}}(move tokens, move integers, init var name, init var bound);
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
            Assert.That(hosted.HasErrors, Is.False, string.Join(Environment.NewLine, hosted.Diagnostics));
            Assert.That(NativeJit.Execute(((global::System.Func<BoundProgram>)(() => { var __nunitActual18 = hosted.Program; Assert.That(__nunitActual18, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual18!; }))(), policy), Is.EqualTo(42));
            var program = await CompileParserAsync(text, entry, (BoundRuntimePolicy)policy).ConfigureAwait(true);
            Assert.That(NativeJit.Execute(program, policy), Is.EqualTo(42));
            await ExecuteAotAsync(program, policy).ConfigureAwait(true);
        }
    }

    [Test]
    public async Task BoundedNativeBinaryEvaluatorKeepsOutputsVacantOnFailureAsync()
    {
        const string Text = "int32 Value() { return 20 + 22; }";
        const string Entry = """
            using Terrum.CodeAnalysis;
            int32 Main() {
                require TryScanCompilerInput(init var tokens, init var trivia, init var integers);
                if (TryEvaluateInt32SubtractReturn(
                    move tokens, move integers, init var name, init var value))
                {
                    return 1;
                }
                require TryScanCompilerInput(init var retryTokens, init var retryTrivia,
                    init var retryIntegers);
                require TryEvaluateInt32AddReturn(
                    move retryTokens, move retryIntegers, init name, init value);
                if (name.Start != 6 || name.Length != 5 || value != 42) { return 2; }
                return 42;
            }
            """;
        foreach (var policy in new[] { NativeRuntimePolicy.FailFast, NativeRuntimePolicy.CheckedExceptions })
        {
            var program = await CompileParserAsync(Text, Entry, (BoundRuntimePolicy)policy)
                .ConfigureAwait(true);
            Assert.That(NativeJit.Execute(program, policy), Is.EqualTo(42));
            await ExecuteAotAsync(program, policy).ConfigureAwait(true);
        }
    }
    [TestCase("int32 Answer() { return 42; }",true,42,0)]
    [TestCase("int32 Answer() { return -42; }",true,42,0)]
    [TestCase("int32 Answer() { return +42; }",true,42,0)]
    [TestCase("int32 Answer() { return ~42; }",true,42,0)]
    [TestCase("int32 Answer() { return 0xFFFF_FFFF; }",true,-1,0)]
    [TestCase("int32 Answer() { return 18446744073709551616; }",false,0,0)]
    [TestCase("int32 Answer() { return 1.5; }",false,0,0)]
    [TestCase("int32 Answer() { return (42); }",true,42,0)]
    [TestCase("int32 Answer() { return (-42); }",true,42,0)]
    [TestCase("int32 Answer() { return -(42); }",true,42,0)]
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
                    init var name, init var expression, init var low, init var high);
                if (name.Start != {{name.Start}} || name.Length != {{name.Length}}
                    || expression.Start != {{expression.Start}} || expression.Length != {{expression.Length}}
                    || low != {{low}} || high != {{high}}) { return 1; }
                """)
            : """
                if (TryParseScalarReturnInteger(move tokens, move integers,
                    init var name, init var expression, init var low, init var high)) { return 1; }
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
            Assert.That(NativeJit.Execute(program, policy), Is.EqualTo(42));
            await ExecuteAotAsync(program, policy).ConfigureAwait(true);
        }
    }
    [TestCase("int32 Main() { return 2147483647; }",true,int.MaxValue)]
    [TestCase("int32 Main() { return +2147483647; }",true,int.MaxValue)]
    [TestCase("int32 Main() { return (2147483647); }",true,int.MaxValue)]
    [TestCase("int32 Main() { return (-2147483648); }",true,int.MinValue)]
    [TestCase("int32 Main() { return -(42); }",true,-42)]
    [TestCase("int32 Main() { return -(2147483648); }",false,0)]
    [TestCase("int32 Main() { return +0xFFFF_FFFF; }",true,-1)]
    [TestCase("int32 Main() { return ~0; }",true,-1)]
    [TestCase("int32 Main() { return ~(0x8000_0000); }",true,int.MaxValue)]
    [TestCase("int32 Main() { return -2147483648; }",true,int.MinValue)]
    [TestCase("int32 Main() { return -2_147_483_648; }",true,int.MinValue)]
    [TestCase("int32 Main() { return 2147483648; }",false,0)]
    [TestCase("int32 Main() { return +2147483648; }",false,0)]
    [TestCase("int32 Main() { return -2147483649; }",false,0)]
    [TestCase("int32 Main() { return 0xFFFF_FFFF; }",true,-1)]
    [TestCase("int32 Main() { return 0x0000_0000_FFFF_FFFF; }",true,-1)]
    [TestCase("int32 Main() { return 0b1000_0000_0000_0000_0000_0000_0000_0000; }",true,int.MinValue)]
    [TestCase("int32 Main() { return -0x8000_0000; }",false,0)]
    [TestCase("int32 Main() { return -0xFFFF_FFFF; }",true,1)]
    [TestCase("int32 Main() { return 0x1_0000_0000; }",false,0)]
    [TestCase("int32 Main() { return 1.5; }",false,0)]
    public async Task BoundedNativeInt32EvaluationMatchesHostedExecutedValuesAsync(
        string text, bool evaluable, int value)
    {
        var assertion = evaluable
            ? FormattableString.Invariant($$"""
                require TryEvaluateInt32ScalarReturn(move tokens, move integers, init var name, init var bound);
                if (name.Start != 6 || name.Length != 4 || bound != {{value}}) { return 1; }
                """)
            : """
                if (TryEvaluateInt32ScalarReturn(move tokens, move integers,
                    init var name, init var bound)) { return 1; }
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
                Assert.That(hosted.HasErrors, Is.False, string.Join(Environment.NewLine, hosted.Diagnostics));
                Assert.That(NativeJit.Execute(((global::System.Func<BoundProgram>)(() => { var __nunitActual19 = hosted.Program; Assert.That(__nunitActual19, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual19!; }))(), policy), Is.EqualTo(value));
            }
            var program = await CompileParserAsync(text, entry, (BoundRuntimePolicy)policy).ConfigureAwait(true);
            Assert.That(NativeJit.Execute(program, policy), Is.EqualTo(42));
            await ExecuteAotAsync(program, policy).ConfigureAwait(true);
        }
    }
    [TestCase("uint32 Value() { return 0; }",true,0u)]
    [TestCase("uint32 Value() { return 4294967295; }",true,uint.MaxValue)]
    [TestCase("uint32 Value() { return +4294967295; }",true,uint.MaxValue)]
    [TestCase("uint32 Value() { return (4294967295); }",true,uint.MaxValue)]
    [TestCase("uint32 Value() { return (+4294967295); }",true,uint.MaxValue)]
    [TestCase("uint32 Value() { return +(4294967295); }",true,uint.MaxValue)]
    [TestCase("uint32 Value() { return ~0; }",true,uint.MaxValue)]
    [TestCase("uint32 Value() { return (~4294967295); }",true,0u)]
    [TestCase("uint32 Value() { return (-1); }",false,0u)]
    [TestCase("uint32 Value() { return 0xFFFF_FFFF; }",true,uint.MaxValue)]
    [TestCase("uint32 Value() { return 0x0000_0000_FFFF_FFFF; }",true,uint.MaxValue)]
    [TestCase("uint32 Value() { return 4294967296; }",false,0u)]
    [TestCase("uint32 Value() { return 0x1_0000_0000; }",false,0u)]
    [TestCase("uint32 Value() { return -1; }",false,0u)]
    [TestCase("uint32 Value() { return 1.5; }",false,0u)]
    public async Task BoundedNativeUInt32EvaluationMatchesHostedExecutedValuesAsync(
        string text, bool evaluable, uint value)
    {
        var assertion = evaluable
            ? FormattableString.Invariant($$"""
                require TryEvaluateUInt32ScalarReturn(move tokens, move integers,
                    init var name, init var bound);
                if (name.Start != 7 || name.Length != 5 || bound != {{value}}) { return 1; }
                """)
            : """
                if (TryEvaluateUInt32ScalarReturn(move tokens, move integers,
                    init var name, init var bound)) { return 1; }
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
                Assert.That(hosted.HasErrors, Is.False, string.Join(Environment.NewLine, hosted.Diagnostics));
                Assert.That(NativeJit.Execute(((global::System.Func<BoundProgram>)(() => { var __nunitActual20 = hosted.Program; Assert.That(__nunitActual20, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual20!; }))(), policy), Is.EqualTo(42));
            }
            var program = await CompileParserAsync(text, entry, (BoundRuntimePolicy)policy).ConfigureAwait(true);
            Assert.That(NativeJit.Execute(program, policy), Is.EqualTo(42));
            await ExecuteAotAsync(program, policy).ConfigureAwait(true);
        }
    }
    [TestCase("uint64 Value() { return 0; }",true,0ul)]
    [TestCase("uint64 Value() { return 4294967296; }",true,4294967296ul)]
    [TestCase("uint64 Value() { return 9223372036854775808; }",true,9223372036854775808ul)]
    [TestCase("uint64 Value() { return 18446744073709551615; }",true,ulong.MaxValue)]
    [TestCase("uint64 Value() { return +18446744073709551615; }",true,ulong.MaxValue)]
    [TestCase("uint64 Value() { return (18446744073709551615); }",true,ulong.MaxValue)]
    [TestCase("uint64 Value() { return (+18446744073709551615); }",true,ulong.MaxValue)]
    [TestCase("uint64 Value() { return +(18446744073709551615); }",true,ulong.MaxValue)]
    [TestCase("uint64 Value() { return ~0; }",true,ulong.MaxValue)]
    [TestCase("uint64 Value() { return ~(18446744073709551615); }",true,0ul)]
    [TestCase("uint64 Value() { return (-1); }",false,0ul)]
    [TestCase("uint64 Value() { return 0xFFFF_FFFF_FFFF_FFFF; }",true,ulong.MaxValue)]
    [TestCase("uint64 Value() { return 18446744073709551616; }",false,0ul)]
    [TestCase("uint64 Value() { return -1; }",false,0ul)]
    public async Task BoundedNativeUInt64EvaluationMatchesHostedExecutedValuesAsync(
        string text, bool evaluable, ulong value)
    {
        var assertion = evaluable
            ? FormattableString.Invariant($$"""
                require TryEvaluateUInt64ScalarReturn(move tokens, move integers,
                    init var name, init var bound);
                if (name.Start != 7 || name.Length != 5 || bound != {{value}}) { return 1; }
                """)
            : """
                if (TryEvaluateUInt64ScalarReturn(move tokens, move integers,
                    init var name, init var bound)) { return 1; }
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
                Assert.That(hosted.HasErrors, Is.False, string.Join(Environment.NewLine, hosted.Diagnostics));
                Assert.That(NativeJit.Execute(((global::System.Func<BoundProgram>)(() => { var __nunitActual21 = hosted.Program; Assert.That(__nunitActual21, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual21!; }))(), policy), Is.EqualTo(42));
            }
            var program = await CompileParserAsync(text, entry, (BoundRuntimePolicy)policy).ConfigureAwait(true);
            Assert.That(NativeJit.Execute(program, policy), Is.EqualTo(42));
            await ExecuteAotAsync(program, policy).ConfigureAwait(true);
        }
    }
    [TestCase("int64 Value() { return 0; }",true,0L)]
    [TestCase("int64 Value() { return 9223372036854775807; }",true,long.MaxValue)]
    [TestCase("int64 Value() { return +9223372036854775807; }",true,long.MaxValue)]
    [TestCase("int64 Value() { return (9223372036854775807); }",true,long.MaxValue)]
    [TestCase("int64 Value() { return (-9223372036854775808); }",true,long.MinValue)]
    [TestCase("int64 Value() { return -(9223372036854775807); }",true,-long.MaxValue)]
    [TestCase("int64 Value() { return -(9223372036854775808); }",false,0L)]
    [TestCase("int64 Value() { return +0xFFFF_FFFF_FFFF_FFFF; }",true,-1L)]
    [TestCase("int64 Value() { return ~0; }",true,-1L)]
    [TestCase("int64 Value() { return (~0x8000_0000_0000_0000); }",true,long.MaxValue)]
    [TestCase("int64 Value() { return -9223372036854775808; }",true,long.MinValue)]
    [TestCase("int64 Value() { return -9_223_372_036_854_775_808; }",true,long.MinValue)]
    [TestCase("int64 Value() { return 9223372036854775808; }",false,0L)]
    [TestCase("int64 Value() { return +9223372036854775808; }",false,0L)]
    [TestCase("int64 Value() { return -9223372036854775809; }",false,0L)]
    [TestCase("int64 Value() { return 0x8000_0000_0000_0000; }",true,long.MinValue)]
    [TestCase("int64 Value() { return 0xFFFF_FFFF_FFFF_FFFF; }",true,-1L)]
    [TestCase("int64 Value() { return -0xFFFF_FFFF_FFFF_FFFF; }",true,1L)]
    [TestCase("int64 Value() { return -0x8000_0000_0000_0000; }",false,0L)]
    [TestCase("int64 Value() { return 18446744073709551616; }",false,0L)]
    [TestCase("int64 Value() { return 0x1_0000_0000_0000_0000; }",false,0L)]
    public async Task BoundedNativeInt64EvaluationMatchesHostedExecutedValuesAsync(
        string text, bool evaluable, long value)
    {
        var expected = value == long.MinValue ? "int64.MinValue"
            : value.ToString(CultureInfo.InvariantCulture);
        var assertion = evaluable
            ? $$"""
                require TryEvaluateInt64ScalarReturn(move tokens, move integers,
                    init var name, init var bound);
                if (name.Start != 6 || name.Length != 5 || bound != {{expected}}) { return 1; }
                """
            : """
                if (TryEvaluateInt64ScalarReturn(move tokens, move integers,
                    init var name, init var bound)) { return 1; }
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
                Assert.That(hosted.HasErrors, Is.False, string.Join(Environment.NewLine, hosted.Diagnostics));
                Assert.That(NativeJit.Execute(((global::System.Func<BoundProgram>)(() => { var __nunitActual22 = hosted.Program; Assert.That(__nunitActual22, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual22!; }))(), policy), Is.EqualTo(42));
            }
            var program = await CompileParserAsync(text, entry, (BoundRuntimePolicy)policy).ConfigureAwait(true);
            Assert.That(NativeJit.Execute(program, policy), Is.EqualTo(42));
            await ExecuteAotAsync(program, policy).ConfigureAwait(true);
        }
    }
    [TestCase("")]
    [TestCase("alpha 42 // trailing\r\n\uD801\uDC00")]
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
            if (index == 0 || index == expected.Length / 2 || index == expected.Length - 1)
            {
                _ = source.Append(CultureInfo.InvariantCulture,
                    $"if (!cursor.TryPeek(init var peek{index}) || !cursor.TryPeek(init var repeated{index}) || peek{index}.Kind != {(int)expected[index].Kind} || repeated{index}.Range.Start != {expected[index].Span.Start}) {{ return 4; }}");
            }
            _ = source.Append(CultureInfo.InvariantCulture,
                $"if (!cursor.TryRead(init var token{index}) || token{index}.Kind != {(int)expected[index].Kind} || token{index}.Range.Start != {expected[index].Span.Start} || token{index}.Range.Length != {expected[index].Span.Length}) {{ return 1; }}");
        }
        _ = source.Append("""
                if (cursor.TryPeek(init var pastPeek) || cursor.TryRead(init var pastEnd)
                    || !cursor.IsCurrent) { return 2; }
                if (cursor.TryRead(init var again)) { return 3; }
                return 42;
            }
            """);

        foreach (var policy in new[] { NativeRuntimePolicy.FailFast, NativeRuntimePolicy.CheckedExceptions })
        {
            var program = await CompileAsync(text, source.ToString(), (BoundRuntimePolicy)policy).ConfigureAwait(true);
            Assert.That(NativeJit.Execute(program, policy), Is.EqualTo(42));
            await ExecuteAotAsync(program, policy).ConfigureAwait(true);
        }
    }

    [Test]
    public Task CursorReadsFullCapacityAsync() =>
        CursorReadsPublishedTokensAndStopsAtEndAsync(string.Join(" ", Enumerable.Repeat("a", 63)));
    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
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
                if (cursor.TryPeekAhead(-1, init var negative)
                    || cursor.TryPeekAhead(3, init var outside)
                    || cursor.TryPeekAhead(3, init var beyond)) { return 1; }
                require cursor.TryPeekAhead(2, init var last) && last.Kind == 9;
                require cursor.TryPeekAhead(1, init var middle) && middle.Kind == 8;
                require cursor.TryRead(init var first) && first.Kind == 7;
                require cursor.TryPeekAhead(1, init var remaining) && remaining.Kind == 9;
                require cursor.TryRead(init var second) && second.Kind == 8;
                require cursor.TryRead(init var third) && third.Kind == 9;
                if (cursor.TryPeekAhead(0, init var afterEnd) || !cursor.IsCurrent) { return 2; }
                require chain.TryCreateCursor(out var stale);
                require chain.TryClear();
                if (stale.TryPeekAhead(1, init var invalidated) || stale.IsCurrent) { return 3; }
                return 42;
            }
            """;
        var program = await CompileAsync("abc", Entry, (BoundRuntimePolicy)policy).ConfigureAwait(true);
        Assert.That(NativeJit.Execute(program, policy), Is.EqualTo(42));
        await ExecuteAotAsync(program, policy).ConfigureAwait(true);
    }
    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public async Task CursorKindMatchConsumesOnlyTheSelectedTokenAsync(NativeRuntimePolicy policy)
    {
        const string Entry = """
            using Terrum.CodeAnalysis;
            int32 Main() {
                require ManagedTokenChain.TryCreate(out var chain);
                require chain.TryAppend(7, 0, 1);
                require chain.TryAppend(8, 1, 1);
                require chain.TryCreateCursorInit(init var cursor);
                if (cursor.TryReadKind(8, init var wrong)
                    || cursor.TryReadKind(-1, init var invalid)) { return 1; }
                require cursor.TryReadKind(7, init var first);
                if (first.Kind != 7 || first.Range.Start != 0) { return 2; }
                if (cursor.TryReadKind(7, init var repeated)) { return 3; }
                require cursor.TryReadKind(8, init var second);
                if (second.Kind != 8 || second.Range.Start != 1
                    || cursor.TryReadKind(8, init var afterEnd)) { return 4; }
                require chain.TryCreateCursorInit(init var stale);
                require chain.TryClear();
                if (stale.TryReadKind(7, init var afterMutation) || stale.IsCurrent) { return 5; }
                return 42;
            }
            """;
        var program = await CompileAsync("ab", Entry, (BoundRuntimePolicy)policy).ConfigureAwait(true);
        Assert.That(NativeJit.Execute(program, policy), Is.EqualTo(42));
        await ExecuteAotAsync(program, policy).ConfigureAwait(true);
    }
    [TestCase("\uD800")]
    [TestCase("a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a")]
    public async Task FailedManagedScansLeaveOutputsVacantAsync(string text)
    {
        var lexical = Lexer.Lex(SourceText.From(text));
        Assert.That(lexical.Diagnostics.Length != 0 || lexical.Tokens.Length > 64, Is.True);
        const string Entry = """
            using Terrum.CodeAnalysis;
            int32 Main() {
                if (TryScanManaged(init var chain)) { return 1; }
                if (chain is init) { return 2; }
                if (TryScanManagedCursor(init var cursor)) { return 3; }
                if (cursor is init) { return 4; }
                return 42;
            }
            """;
        foreach (var policy in new[] { NativeRuntimePolicy.FailFast, NativeRuntimePolicy.CheckedExceptions })
        {
            var program = await CompileAsync(text, Entry, (BoundRuntimePolicy)policy).ConfigureAwait(true);
            Assert.That(NativeJit.Execute(program, policy), Is.EqualTo(42));
            await ExecuteAotAsync(program, policy).ConfigureAwait(true);
        }
    }
    [TestCase(false)]
    [TestCase(true)]
    public async Task CursorRejectsMutationsWithoutPublishingOutputAsync(bool clear)
    {
        var mutation = clear ? "chain.TryClear()" : "chain.TryAppend(2, 1, 0)";
        var entry = $$"""
            using Terrum.CodeAnalysis;
            int32 Main() {
                require ManagedTokenChain.TryCreate(out var chain);
                require chain.TryAppend(1, 0, 1);
                require chain.TryCreateCursorInit(init var cursor);
                require cursor.TryRead(init var first) && first.Kind == 1;
                require chain.TryCreateCursorInit(init var stale);
                require {{mutation}};
                if (cursor.IsCurrent || stale.IsCurrent
                    || cursor.TryPeek(init var absent) || stale.TryPeek(init var missed)
                    || cursor.TryRead(init var unread) || stale.TryRead(init var ignored)) { return 1; }
                require chain.TryCreateCursorInit(init var fresh);
                if (!fresh.IsCurrent) { return 2; }
                if ({{(clear ? "fresh.TryRead(init var cleared)" : "!fresh.TryRead(init var appended) || appended.Kind != 1")}}) { return 3; }
                return 42;
            }
            """;
        foreach (var policy in new[] { NativeRuntimePolicy.FailFast, NativeRuntimePolicy.CheckedExceptions })
        {
            var program = await CompileAsync("a", entry, (BoundRuntimePolicy)policy).ConfigureAwait(true);
            Assert.That(NativeJit.Execute(program, policy), Is.EqualTo(42));
            await ExecuteAotAsync(program, policy).ConfigureAwait(true);
        }
    }
    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
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
                if (!cursor.TryRead(init var token) || token.Kind != 7) { return 2; }
                if (cursor.TryRead(init var pastEnd) || !cursor.IsCurrent) { return 3; }
                if (ManagedHeap.Collect() != 0) { return 4; }
                return 42;
            }
            """;
        var program = await CompileAsync("a", Entry, (BoundRuntimePolicy)policy).ConfigureAwait(true);
        Assert.That(NativeJit.Execute(program, policy), Is.EqualTo(42));
        await ExecuteAotAsync(program, policy).ConfigureAwait(true);
    }
    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public async Task FailedMutationKeepsCursorCurrentAndInvalidatedReadReleasesLinksAsync(NativeRuntimePolicy policy)
    {
        const string Entry = """
            using Terrum.CodeAnalysis;
            int32 Main() {
                require ManagedTokenChain.TryCreate(out var chain);
                require chain.TryAppend(7, 0, 1);
                require chain.TryCreateCursorInit(init var cursor);
                if (chain.TryAppend(-1, 1, 0) || !cursor.IsCurrent) { return 1; }
                if (!cursor.TryRead(init var token) || token.Kind != 7) { return 2; }
                require chain.TryCreateCursorInit(init var stale);
                require chain.TryClear();
                if (stale.TryRead(init var absent) || stale.IsCurrent) { return 3; }
                if (ManagedHeap.Collect() != 1) { return 4; }
                if (cursor.TryRead(init var afterClear) || cursor.IsCurrent) { return 5; }
                return 42;
            }
            """;
        var program = await CompileAsync("a", Entry, (BoundRuntimePolicy)policy).ConfigureAwait(true);
        Assert.That(NativeJit.Execute(program, policy), Is.EqualTo(42));
        await ExecuteAotAsync(program, policy).ConfigureAwait(true);
    }
    [TestCase(3,-1,2)]
    [TestCase(5,100,2)]
    [TestCase(6,1,3)]
    public async Task InvalidHostRecordDoesNotPublishPartiallyBuiltChainAsync(int word, int value, int collected)
    {
        var entry = $$"""
            using Terrum.CodeAnalysis;
            int32 Main() {
                int32 count = Scan();
                Host.Tokens[{{word}}] = {{value}};
                if (TryBuildManagedTokenChain(count, init var chain)) { return 1; }
                if (chain is init) { return 3; }
                if (ManagedHeap.Collect() != {{collected}}) { return 2; }
                return 42;
            }
            """;
        foreach (var policy in new[] { NativeRuntimePolicy.FailFast, NativeRuntimePolicy.CheckedExceptions })
        {
            var program = await CompileAsync("alpha beta", entry, (BoundRuntimePolicy)policy).ConfigureAwait(true);
            Assert.That(NativeJit.Execute(program, policy), Is.EqualTo(42));
            await ExecuteAotAsync(program, policy).ConfigureAwait(true);
        }
    }
    [TestCase(3,107)]
    [TestCase(5,0)]
    public async Task UndefinedKindAndZeroLengthNonEofRecordDoNotPublishManagedChainAsync(int word, int value)
    {
        var entry = $$"""
            using Terrum.CodeAnalysis;
            int32 Main() {
                int32 count = Scan();
                Host.Tokens[{{word}}] = {{value}};
                if (TryBuildManagedTokenChain(count, init var chain)) { return 1; }
                if (chain is init) { return 3; }
                if (ManagedHeap.Collect() != 2) { return 2; }
                return 42;
            }
            """;
        foreach (var policy in new[] { NativeRuntimePolicy.FailFast, NativeRuntimePolicy.CheckedExceptions })
        {
            var program = await CompileAsync("alpha beta", entry, (BoundRuntimePolicy)policy).ConfigureAwait(true);
            Assert.That(NativeJit.Execute(program, policy), Is.EqualTo(42));
            await ExecuteAotAsync(program, policy).ConfigureAwait(true);
        }
    }

    private static Task<BoundProgram> CompileParserAsync(string text, string entry, BoundRuntimePolicy policy) =>
        CompileAsync(text, entry, policy, "ScalarFunctionParser");
    [TestCase("int32 Value(int32 x) { return x; }","Value(42)","42",1,1,0,1,0,false)]
    [TestCase("uint64 Value(uint64 x, uint64 y) { return x + y; }","Value(uint64(40), uint64(2))","uint64(42)",4,2,57,1,2,false)]
    [TestCase("int32 Value(int32 x, int32 y) { return x - y; }","Value(50, 8)","42",1,2,58,1,2,false)]
    [TestCase("int32 Value(int32 x, int32 y) { return y - x; }","Value(8, 50)","42",1,2,58,2,1,false)]
    [TestCase("int32 Value(int32 x, int32 y) { return x + y; }","Value(int32.MaxValue - 1, 1)","int32.MaxValue",1,2,57,1,2,false)]
    [TestCase("bool Value(int32 x, int32 y) { return x < y; }","Value(1, 2)","true",5,2,67,1,2,false)]
    [TestCase("int32 Value(int32 x, int32 y) { if (x < y) { return x + y; } else { return x - y; } }","Value(20, 22)","42",1,2,57,1,2,true)]
    [TestCase("int32 Value(int32 x, int32 y) { if (x >= y) { return x; } else { return y; } }","Value(42, 3)","42",1,2,0,1,0,true)]
    [TestCase("uint32 Value(uint32 x) { return x - x; }","Value(uint32.MaxValue)","uint32(0)",2,1,58,1,1,false)]
    [TestCase("bool Value(bool x) { return x; }","Value(true)","true",5,1,0,1,0,false)]
    [TestCase("char16 Value(char16 x, char16 y) { if (x != y) { return y; } else { return x; } }","Value(char16(1), char16(42))","char16(42)",6,2,0,2,0,true)]
    public async Task ScannerOwnedScalarFrontendPublishesTypedShapesAsync(
        string text, string call, string expected, int returnType, int count,
        int operation, int left, int right, bool conditional)
    {
        var syntax = SyntaxTree.Parse(text);
        Assert.That(syntax.Diagnostics, Is.Empty);
        var functionName = syntax.Tokens[1].Span;
        var valueStart = text.IndexOf("return ", StringComparison.Ordinal) + "return ".Length;
        var valueLength = text.IndexOf(';', valueStart) - valueStart;
        var conditionOperation = conditional
            ? text.Contains(" < ", StringComparison.Ordinal) ? 67
                : text.Contains(" >= ", StringComparison.Ordinal) ? 70 : 66
            : 0;
        var conditionStart = conditional ? text.IndexOf("if (", StringComparison.Ordinal) + 4 : 0;
        var conditionLength = conditional ? text.IndexOf(')', conditionStart) - conditionStart : 0;
        var secondReturnStart = conditional
            ? text.IndexOf("return ", valueStart, StringComparison.Ordinal) + "return ".Length : 0;
        var secondReturnLength = conditional ? text.IndexOf(';', secondReturnStart) - secondReturnStart : 0;
        var firstParameter = syntax.Tokens[4].Span;
        var secondParameter = count == 2 ? syntax.Tokens[7].Span : default;
        var firstTypeText = text.Substring(syntax.Tokens[3].Span.Start, syntax.Tokens[3].Span.Length);
        var firstType = firstTypeText switch {
            "int32" => 1,
            "uint32" => 2,
            "int64" => 3,
            "uint64" => 4,
            "bool" => 5,
            "char16" => 6,
            _ => throw new InvalidOperationException(firstTypeText),
        };
        var assertion = $$"""
            require TryScanScalarFrontend(init var result);
            if (!result.IsValid || result.ErrorCode != 0
                || result.ReturnType != {{returnType}} || result.ParameterCount != {{count}}
                || result.Name.Start != {{functionName.Start}}
                || result.Name.Length != {{functionName.Length}}
                || result.FirstType != {{firstType}}
                || result.SecondType != {{(count == 2 ? firstType : 0)}}
                || result.FirstName.Start != {{firstParameter.Start}}
                || result.FirstName.Length != {{firstParameter.Length}}
                || result.SecondName.Start != {{secondParameter.Start}}
                || result.SecondName.Length != {{secondParameter.Length}}
                || result.WhenTrue.Operation != {{operation}}
                || result.WhenTrue.Type != {{returnType}}
                || result.WhenTrue.LeftParameter != {{left}}
                || result.WhenTrue.RightParameter != {{right}}
                || result.WhenTrue.Span.Start != {{valueStart}}
                || result.WhenTrue.Span.Length != {{valueLength}}
                || result.HasCondition != {{conditional.ToString().ToLowerInvariant()}}
                || result.Condition.Operation != {{conditionOperation}}
                || result.Condition.Type != {{(conditional ? 5 : 0)}}
                || result.Condition.Span.Start != {{conditionStart}}
                || result.Condition.Span.Length != {{conditionLength}}
                || result.WhenFalse.Type != {{(conditional ? returnType : 0)}}
                || result.WhenFalse.Span.Start != {{secondReturnStart}}
                || result.WhenFalse.Span.Length != {{secondReturnLength}})
            {
                return 1;
            }
            return 42;
            """;
        // Independent projection checks stay within the verifier's expression work bound.
        assertion = assertion.Replace(" || ", ") { return 1; } if (",
            StringComparison.Ordinal);
        var entry = "using Terrum.CodeAnalysis; int32 Main() { " + assertion + " }";
        foreach (var policy in new[] { NativeRuntimePolicy.FailFast, NativeRuntimePolicy.CheckedExceptions })
        {
            var hostedSource = text + $" int32 Main() {{ if (ValueResult()) {{ return 42; }} return 1; }} bool ValueResult() {{ return {call} == {expected}; }}";
            var hosted = Compilation.Create(SyntaxTree.Parse(hostedSource), (BoundRuntimePolicy)policy);
            Assert.That(hosted.HasErrors, Is.False, string.Join(Environment.NewLine, hosted.Diagnostics));
            Assert.That(NativeJit.Execute(((global::System.Func<BoundProgram>)(() => { var __nunitActual23 = hosted.Program; Assert.That(__nunitActual23, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual23!; }))(), policy), Is.EqualTo(42));
            var program = await CompileAsync(text, entry, (BoundRuntimePolicy)policy,
                "ScalarFrontend").ConfigureAwait(true);
            Assert.That(NativeJit.Execute(program, policy), Is.EqualTo(42));
            await ExecuteAotAsync(program, policy).ConfigureAwait(true);
        }
    }
    [TestCase("int32 Value() { return -#42; }",true,"ScalarFunctionParser",        "TryParseScalarReturnFunctionTyped(move tokens, init var scalarType, init var name, init var expression)")]
    [TestCase("int32 Value(int32 x, int32 y) { return x#+#y; }",false,"ScalarParameterPairParser",        "TryEvaluateInt32TwoParameterAddReturn(move tokens, 20, 22, init var name, init var value)")]
    [TestCase("int32 Value(bool c, int32 x, int32 y) { if (c) { return x#+#y; } else { return x; } }",        false,"ScalarConditionalAddParser",        "TryEvaluateInt32ScalarConditionalAddReturn(move tokens, true, 20, 22, init var name, init var value)")]
    [TestCase("int32 Value(int32 x, int32 y) { if (x#<#y) { return x; } else { return y; } }",        false,"ScalarComparisonConditionalParser",        "TryEvaluateInt32ComparisonConditionalReturn(move tokens, 20, 22, init var name, init var value)")]
    public async Task MaintainedScalarProbesRejectOperatorSpacingBeforeLegacyFallbackAsync(
        string form, bool prefix, string module, string invocation)
    {
        foreach (var gap in new[] { "", " ", "\r\n", "/* gap */", "//** gap **//", "/// gap\n" })
        {
            var text = form.Replace("#", gap, StringComparison.Ordinal);
            var entry = $$"""
                using Terrum.CodeAnalysis;
                int32 Main() {
                    require TokenSnapshot.TryScan(init var tokens);
                    bool valid = {{invocation}};
                    if (valid != {{(prefix == (gap.Length == 0) ? "true" : "false")}}) { return 1; }
                    return 42;
                }
                """;
            foreach (var policy in new[] { NativeRuntimePolicy.FailFast, NativeRuntimePolicy.CheckedExceptions })
            {
                var program = await CompileAsync(text, entry, (BoundRuntimePolicy)policy, module).ConfigureAwait(true);
                Assert.That(NativeJit.Execute(program, policy), Is.EqualTo(42));
                await ExecuteAotAsync(program, policy).ConfigureAwait(true);
            }
        }
    }
    [TestCase("int32 Value#(bool c, int32 x, int32 y) { if (c) { return x; } else { return y; } }",        "ScalarConditionalParser","TryEvaluateInt32ScalarConditionalReturn(move tokens, true, 20, 22, init var name, init var value)")]
    [TestCase("int32 Value#(bool c, int32 x, int32 y) { if (c) { return x + y; } else { return x; } }",        "ScalarConditionalAddParser","TryEvaluateInt32ScalarConditionalAddReturn(move tokens, true, 20, 22, init var name, init var value)")]
    [TestCase("int32 Value#(int32 x, int32 y) { if (x < y) { return x; } else { return y; } }",        "ScalarComparisonConditionalParser","TryEvaluateInt32ComparisonConditionalReturn(move tokens, 20, 22, init var name, init var value)")]
    public async Task MaintainedScalarProbesEnforceAttachedHeadersAsync(string form, string module, string invocation)
    {
        foreach (var gap in new[] { "", " ", "\n", "/* boundary */", "/// boundary\n" })
        {
            var text = form.Replace("#", gap, StringComparison.Ordinal);
            var entry = $$"""
                using Terrum.CodeAnalysis;
                int32 Main() {
                    require TokenSnapshot.TryScan(init var tokens);
                    bool valid = {{invocation}};
                    if (valid != {{(gap.Length == 0 ? "true" : "false")}}) { return 1; }
                    return 42;
                }
                """;
            foreach (var policy in new[] { NativeRuntimePolicy.FailFast, NativeRuntimePolicy.CheckedExceptions })
            {
                var program = await CompileAsync(text, entry, (BoundRuntimePolicy)policy, module).ConfigureAwait(true);
                Assert.That(NativeJit.Execute(program, policy), Is.EqualTo(42));
                await ExecuteAotAsync(program, policy).ConfigureAwait(true);
            }
        }
    }
    [TestCase("!!",TokenKind.Bang,TokenKind.Bang)]
    [TestCase("+++",TokenKind.PlusPlus,TokenKind.Plus)]
    [TestCase(">?>",TokenKind.Greater,TokenKind.Question,TokenKind.Greater)]
    [TestCase(">>>>",TokenKind.ShiftRightUnsigned,TokenKind.Greater)]
    [TestCase("+-*",TokenKind.Plus,TokenKind.Minus,TokenKind.Star)]
    [TestCase("!!",TokenKind.Identifier)]
    [TestCase("==",TokenKind.Equals,TokenKind.Equals)]
    public async Task NativeScalarRunGateRejectsIndependentHistoricAndForgedRecordsAsync(
        string text, params TokenKind[] kinds)
    {
        var entry = new StringBuilder("""
            using Terrum.CodeAnalysis;
            int32 Main() {
                Host.Diagnostics[0] = 0;
            """);
        var start = 0;
        for (var index = 0; index < kinds.Length; index++)
        {
            var width = kinds[index] switch {
                TokenKind.Identifier => text.Length,
                TokenKind.PlusPlus => 2,
                TokenKind.ShiftRightUnsigned => 3,
                _ => 1,
            };
            entry.Append(CultureInfo.InvariantCulture,
                $"Host.Tokens[{index * 3}] = {(int)kinds[index]};"
                + $" Host.Tokens[{index * 3 + 1}] = {start};"
                + $" Host.Tokens[{index * 3 + 2}] = {width};");
            start += width;
        }
        Assert.That(start, Is.EqualTo(text.Length));
        entry.Append(
            $"Host.Tokens[{kinds.Length * 3}] = 0;"
            + $" Host.Tokens[{kinds.Length * 3 + 1}] = {text.Length};"
            + $" Host.Tokens[{kinds.Length * 3 + 2}] = 0;"
            + $" require TokenSnapshot.TryBuild({kinds.Length + 1}, init var tokens);"
            + " var error = tokens.GetScalarOperatorSpacingError();"
            + $" if (error.Start != 0 || error.Length != {text.Length}) {{ return 1; }}"
            + " require TryParseScalarFrontend(move tokens, init var result);"
            + $" if (result.IsValid || result.ErrorSpan.Start != 0 || result.ErrorSpan.Length != {text.Length}) {{ return 2; }}"
            + " return 42; }");
        foreach (var policy in new[] { NativeRuntimePolicy.FailFast, NativeRuntimePolicy.CheckedExceptions })
        {
            var program = await CompileAsync(text, entry.ToString(), (BoundRuntimePolicy)policy, "ScalarFrontend");
            Assert.That(NativeJit.Execute(program, policy), Is.EqualTo(42));
            await ExecuteAotAsync(program, policy);
        }
    }
    [TestCase("b ? 1 : 2","")]
    [TestCase("b/* left */?/// right\n1//** left **//:\r\n2","")]
    [TestCase("b? 1 : 2","?")]
    [TestCase("b ?1 : 2","?")]
    [TestCase("b ? 1: 2",":")]
    [TestCase("b ? 1 :2",":")]
    [TestCase("b?1:2","?")]
    public async Task NativeSnapshotAndFrontendRejectConditionalSpacingWithoutHostParseAuditAsync(
        string expression, string offending)
    {
        var text = "int32 Value(bool b) { return " + expression + "; }";
        var expectedStart = offending.Length == 0 ? 0 : text.IndexOf(offending, StringComparison.Ordinal);
        var entry = $$"""
            using Terrum.CodeAnalysis;
            int32 Main() {
                require TokenSnapshot.TryScan(init var tokens);
                var spacing = tokens.GetScalarOperatorSpacingError();
                if (spacing.Start != {{expectedStart}}) { return 1; }
                if (spacing.Length != {{offending.Length}}) { return 2; }
                require TryParseScalarFrontend(move tokens, init var result);
                if (result.IsValid != {{(offending.Length == 0 ? "true" : "false")}}) { return 3; }
                if (result.ErrorSpan.Start != {{expectedStart}}) { return 4; }
                if (result.ErrorSpan.Length != {{offending.Length}}) { return 5; }
                return 42;
            }
            """;
        foreach (var policy in new[] { NativeRuntimePolicy.FailFast, NativeRuntimePolicy.CheckedExceptions })
        {
            var program = await CompileAsync(text, entry, (BoundRuntimePolicy)policy,
                "ScalarFrontend").ConfigureAwait(true);
            Assert.That(NativeJit.Execute(program, policy), Is.EqualTo(42));
            await ExecuteAotAsync(program, policy).ConfigureAwait(true);
        }
    }
    [TestCase("int32 Value(int32 a, int32 b, int32 c, int32 d) { return a + b * c - d; }","")]
    [TestCase("int32 Value(int32 a, int32 b, int32 c) { return -a + b * c; }","")]
    [TestCase("int32 Value(int32 a, int32 b, int32 c) { return a + b *c; }","*")]
    [TestCase("int32 Value(int32 a, int32 b, int32 c, int32 d) { return a + b * c- d; }","-")]
    public async Task NativeSnapshotPreservesMultiTokenOperatorSpacingDecisionsAsync(
        string text, string offending)
    {
        var expectedStart = offending.Length == 0 ? 0 : text.IndexOf(offending, StringComparison.Ordinal);
        var expectedLength = offending.Length;
        var entry = $$"""
            using Terrum.CodeAnalysis;
            int32 Main() {
                require TokenSnapshot.TryScan(init var tokens);
                var spacing = tokens.GetScalarOperatorSpacingError();
                if (spacing.Start != {{expectedStart}} || spacing.Length != {{expectedLength}}) {
                    return 1;
                }
                return 42;
            }
            """;
        foreach (var policy in new[] { NativeRuntimePolicy.FailFast, NativeRuntimePolicy.CheckedExceptions })
        {
            var program = await CompileAsync(text, entry, (BoundRuntimePolicy)policy,
                "ScalarFrontend").ConfigureAwait(true);
            Assert.That(NativeJit.Execute(program, policy), Is.EqualTo(42));
            await ExecuteAotAsync(program, policy).ConfigureAwait(true);
        }
    }
    [TestCase("int32 Value(int32 x, int32 y) { return x+y; }",1,"+")]
    [TestCase("int32 Value(int32 x, int32 y) { return x+ y; }",1,"+")]
    [TestCase("int32 Value(int32 x, int32 y) { return x +y; }",1,"+")]
    [TestCase("int32 Value(int32 x) { return - x; }",1,"-")]
    [TestCase("int32 Value(int32 x) { return ~/* gap */x; }",1,"~")]
    [TestCase("bool Value(bool x) { return !/// gap\nx; }",1,"!")]
    [TestCase("int32 Value(int32 x) { return +\r\n(x); }",1,"+")]
    [TestCase("int32 Value() { mut int32 x= 4; return x; }",1,"=")]
    [TestCase("int32 Value() { mut int32 x =4; return x; }",1,"=")]
    [TestCase("int32 Value() { mut int32 x = 4; x+= 2; return x; }",1,"+=")]
    [TestCase("int32 Value() { mut int32 x = 4; x +=2; return x; }",1,"+=")]
    [TestCase("int32 Value() { mut int32 x = 4; return x /* gap */++; }",1,"++")]
    [TestCase("int32 Value() { mut int32 x = 4; return ++\nx; }",1,"++")]
    [TestCase("int32 Value()=> 42;",1,"=>")]
    [TestCase("int32 Value() =>42;",1,"=>")]
    [TestCase("int32 Value() { return - 2147483648; }",1,"-")]
    [TestCase("int32 Value () { return 42; }",1,"()")]
    [TestCase("int32 Value\n() { return 42; }",1,"()")]
    [TestCase("int32 Value/* boundary */() { return 42; }",1,"()")]
    [TestCase("int32 Value/// boundary\n() { return 42; }",1,"()")]
    [TestCase("int32 @await /* boundary */() { return 42; }",1,"()")]
    [TestCase("int32 Value() { return int32 (42); }",1,"(42)")]
    [TestCase("int32 Value() { return int32\n(42); }",1,"(42)")]
    [TestCase("int32 Value() { return int32/* boundary */(42); }",1,"(42)")]
    [TestCase("int32 Value() { return int32/// boundary\n(42); }",1,"(42)")]
    [TestCase("int32 Value(int32 x, int32 x) { return x; }",4,"x)")]
    [TestCase("int32 Value(int32 x) { return y; }",3,"y;")]
    [TestCase("int32 Value(uint32 x) { return x; }",2,"x;")]
    [TestCase("int32 Value(int32 x, uint32 y) { return x + y; }",2,"+ y")]
    [TestCase("bool Value(bool x, bool y) { return x < y; }",2,"< y")]
    [TestCase("int32 Value(int32 x) { return x + unknown; }",3,"unknown")]
    [TestCase("int32 Value(int32 x) { return x }",1,"}")]
    [TestCase("int32 Value(int32 x) { if (x) { return x; } else { return x; } }",2,"x) { return x")]
    [TestCase("int32 Value(int32 x) { return x; } int32 Other() { return x; }",1,"int32 Other")]
    public async Task ScannerOwnedScalarFrontendRejectsInvalidOrUnsupportedShapesAsync(
        string text, int code, string offending)
    {
        var expectedStart = text.IndexOf(offending, StringComparison.Ordinal);
        Assert.That(expectedStart >= 0, Is.True);
        var expectedLength = SyntaxTree.Parse(text).Tokens
            .Single(token => token.Span.Start == expectedStart).Span.Length;
        var entry = $$"""
            using Terrum.CodeAnalysis;
            int32 Main() {
                require TokenSnapshot.TryScan(init var tokens);
                require TryParseScalarFrontend(move tokens, init var result);
                if (result.IsValid || result.ErrorCode != {{code}}
                    || result.ErrorSpan.Start != {{expectedStart}}
                    || result.ErrorSpan.Length != {{expectedLength}}
                    || result.ReturnType != 0 || result.ParameterCount != 0)
                {
                    return 1;
                }
                return 42;
            }
            """;
        foreach (var policy in new[] { NativeRuntimePolicy.FailFast, NativeRuntimePolicy.CheckedExceptions })
        {
            var program = await CompileAsync(text, entry, (BoundRuntimePolicy)policy,
                "ScalarFrontend").ConfigureAwait(true);
            Assert.That(NativeJit.Execute(program, policy), Is.EqualTo(42));
            await ExecuteAotAsync(program, policy).ConfigureAwait(true);
        }
    }
    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public async Task ScannerOwnedScalarFrontendRejectsTrailingTokensAsync(NativeRuntimePolicy policy)
    {
        var text = "int32 Value(int32 x) { return x; } " + string.Join(' ', Enumerable.Repeat("extra", 65));
        var excess = SyntaxTree.Parse(text).Tokens.First(token => token.Text == "extra").Span;
        var entry = $$"""
            using Terrum.CodeAnalysis;
            int32 Main() {
                require TokenSnapshot.TryScan(init var tokens);
                require TryParseScalarFrontend(move tokens, init var result);
                if (result.IsValid) { return 1; }
                if (result.ErrorCode != 1) { return 2; }
                if (result.ErrorSpan.Start != {{excess.Start}}) { return 3; }
                if (result.ErrorSpan.Length != {{excess.Length}}) { return 4; }
                if (result.ReturnType != 0) { return 5; }
                if (result.ParameterCount != 0) { return 6; }
                return 42;
            }
            """;
        var program = await CompileAsync(text, entry, (BoundRuntimePolicy)policy,
            "ScalarFrontend").ConfigureAwait(true);
        Assert.That(NativeJit.Execute(program, policy), Is.EqualTo(42));
        await ExecuteAotAsync(program, policy).ConfigureAwait(true);
    }
    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public async Task ScannerOwnedScalarFrontendLeavesLexicalErrorsWithScannerAsync(
        NativeRuntimePolicy policy)
    {
        const string Entry = """
            using Terrum.CodeAnalysis;
            int32 Main() {
                if (TryScanScalarFrontend(init var result)) { return 1; }
                require TokenDiagnosticSnapshot.TryBuild(init var diagnostics);
                if (diagnostics.GetCount() != 1) { return 2; }
                require diagnostics.TryGet(0, init var code, init var span);
                if (code == 0 || span.Start != 0 || span.Length != 1) { return 3; }
                return 42;
            }
            """;
        var program = await CompileAsync("\uD800", Entry, (BoundRuntimePolicy)policy,
            "ScalarFrontend").ConfigureAwait(true);
        Assert.That(NativeJit.Execute(program, policy), Is.EqualTo(42));
        await ExecuteAotAsync(program, policy).ConfigureAwait(true);
    }
    [TestCase("'é'",true)]
    [TestCase("'\\u00E9'",true)]
    [TestCase("'\\U000000E9'",true)]
    [TestCase("'\\''",true)]
    [TestCase("'\\\"'",true)]
    [TestCase("'\\\\'",true)]
    [TestCase("'\\0'",true)]
    [TestCase("'😀'",false)]
    [TestCase("'\\U0001F600'",false)]
    public async Task ScalarFrontendDecodesCharacterLiteralScalarsAsync(
        string literal, bool valid)
    {
        var text = "char16 Value() { return " + literal + "; }";
        var validLiteral = valid ? "true" : "false";
        var entry = $$"""
            using Terrum.CodeAnalysis;
            int32 Main() {
                if (!TokenSnapshot.TryScan(init var tokens)) { return 1; }
                if (!TryParseScalarFrontend(move tokens, init var result)) { return 2; }
                if (result.IsValid != {{validLiteral}}) { return 3; }
                return 42;
            }
            """;
        foreach (var policy in new[] { NativeRuntimePolicy.FailFast, NativeRuntimePolicy.CheckedExceptions })
        {
            var program = await CompileAsync(text, entry, (BoundRuntimePolicy)policy, "ScalarFrontend")
                .ConfigureAwait(true);
            Assert.That(NativeJit.Execute(program, policy), Is.EqualTo(42));
            await ExecuteAotAsync(program, policy).ConfigureAwait(true);
        }
    }
    [TestCase("int32 Value(int32 x) { return x; }",true,0,1,0)]
    [TestCase("int32 Value() { return 42; }",true,0,0,3)]
    [TestCase("int32 Value(int32 x, int32 y) { return x + ((y)); }",true,0,2,57)]
    [TestCase("int32 Value(int32 x, int32 y) { return ((x)) + y; }",true,0,2,57)]
    [TestCase("int32 Value(int32 x, int32 y) { if (x < y) { return x + y; } else { return x - y; } }",true,0,2,57)]
    [TestCase("int32 Value(int32 x) { return y; }",false,3,0,0)]
    public async Task ScalarFrontendProjectionPublishesValidatedTypedResultAsync(
        string source, bool valid, int error, int parameters, int operation)
    {
        foreach (var policy in new[] { NativeRuntimePolicy.FailFast, NativeRuntimePolicy.CheckedExceptions })
        {
            var program = await CompileAsync(source,
                "using Terrum.CodeAnalysis; int32 Main() { require TryPublishScalarFrontend(); return 42; }",
                (BoundRuntimePolicy)policy, "ScalarFrontendProjection").ConfigureAwait(true);
            var native = NativeJit.ExecuteWithData(program, policy);
            Assert.That(native.ExitCode, Is.EqualTo(42));
            var projected = ScalarFrontendHost.ReadResult(SourceText.From(source),
                native.Data.AsSpan(native.BufferOffsets[ScalarFrontendHost.BufferIndex],
                    ScalarFrontendHost.WordCount * sizeof(int)));
            Assert.That(projected.IsValid, Is.EqualTo(valid));
            Assert.That(projected.ErrorCode, Is.EqualTo(error));
            Assert.That(projected.ParameterCount, Is.EqualTo(parameters));
            Assert.That(projected.WhenTrue.Operation, Is.EqualTo(operation));
            if (valid)
            {
                Assert.That(source.Substring(projected.Name.Start, projected.Name.Length), Is.EqualTo("Value"));
                Assert.That(projected.HasCondition, Is.EqualTo(source.Contains("if (", StringComparison.Ordinal)));
                if (projected.HasCondition)
                {
                    Assert.That(projected.Condition.Operation, Is.EqualTo(67));
                    Assert.That(projected.Condition.LeftParameter, Is.EqualTo(1));
                    Assert.That(projected.Condition.RightParameter, Is.EqualTo(2));
                    Assert.That(projected.WhenFalse.Operation, Is.EqualTo(58));
                }
            }
            else
            {
                Assert.That(projected.Name, Is.EqualTo(default(TextSpan)));
                Assert.That(projected.ErrorSpan.Start, Is.EqualTo(source.IndexOf("y;", StringComparison.Ordinal)));
                Assert.That(projected.ErrorSpan.Length, Is.EqualTo(1));
                Assert.Throws<ArgumentException>(() => ScalarFrontendHost.BindLibrary(
                    SourceText.From(source),
                    native.Data.AsSpan(native.BufferOffsets[ScalarFrontendHost.BufferIndex],
                        ScalarFrontendHost.WordCount * sizeof(int)), (BoundRuntimePolicy)policy));
            }
            await ExecuteAotAsync(program, policy).ConfigureAwait(true);
        }
    }
    [TestCase(0,4)]
    [TestCase(1,0)]
    [TestCase(2,1)]
    [TestCase(3,-1)]
    [TestCase(6,3)]
    [TestCase(9,0)]
    [TestCase(22,7)]
    [TestCase(27,int.MaxValue)]
    [TestCase(0,19)]
    [TestCase(34,1)]
    [TestCase(35,1)]
    [TestCase(36,1)]
    public void ScalarFrontendProjectionRejectsMalformedHostWords(int index, int value)
    {
        var source = SourceText.From("int32 Value(int32 x) { return x; }");
        var words = new int[ScalarFrontendHost.WordCount];
        words[0] = 87;
        words[1] = 1;
        words[5] = 1;
        words[6] = 1;
        words[7] = 1;
        words[9] = 6;
        words[10] = 5;
        words[11] = 18;
        words[12] = 1;
        words[22] = 1;
        words[24] = 1;
        words[26] = 30;
        words[27] = 1;
        var bytes = new byte[words.Length * sizeof(int)];
        for (var at = 0; at < words.Length; at++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(at * 4), words[at]);
        }
        Assert.That(ScalarFrontendHost.ReadResult(source, bytes).IsValid, Is.True);
        BinaryPrimitives.WriteInt32LittleEndian(bytes, 2);
        Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(source, bytes));
        BinaryPrimitives.WriteInt32LittleEndian(bytes, 87);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(index * 4), value);
        Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(source, bytes));
    }
    [TestCase("int32 Value(int32 x, int32 y, int32 z) { return z + 5; }",40157)]
    [TestCase("int32 Value(int32 x, int32 y, int32 z) { return (-5) + z; }",41557)]
    [TestCase("int32 Value(int32 x, int32 y, int32 z) { return ~(z + 5); }",44157)]
    [TestCase("int32 Value(int32 x, int32 y, int32 z) { return -(~(z + 5)); }",72157)]
    [TestCase("bool Value(int32 x, int32 y, int32 z) { return !(z < -5); }",43367)]
    public async Task ScalarFrontendThirdParameterRejectsForgedProjectionAsync(
        string text, int operation)
    {
        var source = SourceText.From(text);
        var stage = await CompileAsync(text,
            "using Terrum.CodeAnalysis; int32 Main() { require TryPublishScalarFrontend(); return 42; }",
            BoundRuntimePolicy.FailFast, "ScalarFrontendProjection").ConfigureAwait(true);
        var native = NativeJit.ExecuteWithData(stage, NativeRuntimePolicy.FailFast);
        Assert.That(native.ExitCode, Is.EqualTo(42));
        var bytes = native.Data.AsSpan(native.BufferOffsets[ScalarFrontendHost.BufferIndex],
            ScalarFrontendHost.WordCount * sizeof(int)).ToArray();
        Assert.That(BinaryPrimitives.ReadInt32LittleEndian(bytes), Is.EqualTo(87));
        var expressionWords = ScalarFrontendExpressionWordOffsets(bytes);
        var result = ScalarFrontendHost.ReadResult(source, bytes);
        Assert.That(result.ParameterCount, Is.EqualTo(3));
        Assert.That(result.WhenTrue.Operation, Is.EqualTo(ExpectedScalarFrontendOperation(source, bytes, operation)));
        Assert.That(text.Substring(result.ThirdName.Start, result.ThirdName.Length), Is.EqualTo("z"));
        foreach (var word in new[] { 0, 6, expressionWords.Operation, 34, 35, 36 })
        {
            var offset = word * sizeof(int);
            var original = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset));
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset),
                word == 0 ? 18 : original ^ 1);
            Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(source, bytes));
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset), original);
        }
        int operationWord = expressionWords.Operation;
        int operationOffset = operationWord * sizeof(int);
        int actualOperation = result.WhenTrue.Operation;
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(operationOffset), actualOperation - 40000);
        Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(source, bytes));
        foreach (var forged in new[] { actualOperation + 100, actualOperation + 200, actualOperation + 40000 })
        {
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(operationOffset), forged);
            Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(source, bytes));
        }
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(operationOffset), actualOperation);
        var changed = text.ToCharArray();
        changed[text.LastIndexOf('z')] = 'x';
        Assert.Throws<ArgumentException>(() =>
            ScalarFrontendHost.ReadResult(SourceText.From(new string(changed)), bytes));
    }
    [TestCase("int32 Value(int32 x, int32 y, int32 x) { return x; }",4)]
    [TestCase("int32 Value(int32 x, int32 y, int32 y) { return x; }",4)]
    [TestCase("int32 Value(int32 x, int32 y, int32 z) { return missing; }",3)]
    [TestCase("int32 Value(int32 x, int32 y, bool z) { return z; }",2)]
    public async Task ScalarFrontendThirdParameterRejectsInvalidSourceAsync(
        string text, int error)
    {
        var stage = await CompileAsync(text,
            "using Terrum.CodeAnalysis; int32 Main() { require TryPublishScalarFrontend(); return 42; }",
            BoundRuntimePolicy.FailFast, "ScalarFrontendProjection").ConfigureAwait(true);
        var native = NativeJit.ExecuteWithData(stage, NativeRuntimePolicy.FailFast);
        Assert.That(native.ExitCode, Is.EqualTo(42));
        var result = ScalarFrontendHost.ReadResult(SourceText.From(text),
            native.Data.AsSpan(native.BufferOffsets[ScalarFrontendHost.BufferIndex],
                ScalarFrontendHost.WordCount * sizeof(int)));
        Assert.That(result.IsValid, Is.False);
        Assert.That(result.ErrorCode, Is.EqualTo(error));
    }
    [TestCase("int32 Value(int32 x, int32 y, int32 z) { return z; }",1,2,42)]
    [TestCase("int32 Value(int32 x, int32 y, int32 z) { return z + 5; }",1,2,37)]
    [TestCase("int32 Value(int32 x, int32 y, int32 z) { return (-5) + z; }",1,2,47)]
    [TestCase("int32 Value(int32 x, int32 y, int32 z) { return -(~(z + 5)); }",1,2,36)]
    [TestCase("int32 Value(int32 x, int32 y, int32 z) { if (x < z) { return x + z; } return y; }",0,7,42)]
    [TestCase("int32 Value(int32 x, int32 y, int32 z) { if (x < z) { return x + z; } return y; }",43,42,42)]
    [TestCase("int32 Value(int32 x, int32 y, int32 z) { if (x < z) { return x + z; } return y; }",int.MaxValue,42,int.MaxValue)]
    public async Task NativeScalarFrontendThirdParameterExecutesAsync(
        string text, int x, int y, int z)
    {
        var source = SourceText.From(text);
        foreach (var policy in new[] { NativeRuntimePolicy.FailFast, NativeRuntimePolicy.CheckedExceptions })
        {
            var stage = await CompileAsync(text,
                "using Terrum.CodeAnalysis; int32 Main() { require TryPublishScalarFrontend(); return 42; }",
                (BoundRuntimePolicy)policy, "ScalarFrontendProjection").ConfigureAwait(true);
            var native = NativeJit.ExecuteWithData(stage, policy);
            Assert.That(native.ExitCode, Is.EqualTo(42));
            var bytes = native.Data.AsSpan(native.BufferOffsets[ScalarFrontendHost.BufferIndex],
                ScalarFrontendHost.WordCount * sizeof(int));
            var library = ScalarFrontendHost.BindLibrary(source, bytes, (BoundRuntimePolicy)policy);
            var call = new BoundCall(0, [
                new BoundConstant(x, PrimitiveType.Int32, default),
                new BoundConstant(y, PrimitiveType.Int32, default),
                new BoundConstant(z, PrimitiveType.Int32, default),
            ], PrimitiveType.Int32, default);
            var main = new BoundFunction("Main", PrimitiveType.Int32, [], 0,
                new BoundBlock([new BoundReturn(call, default)], default), default);
            var executable = library with { Functions = [.. library.Functions, main], EntryPoint = 1 };
            SemanticVerifier.Verify(executable);
            Assert.That(NativeJit.Execute(executable, policy), Is.EqualTo(42));
            await ExecuteAotAsync(executable, policy).ConfigureAwait(true);
        }
    }
    [TestCase("public bool Fill(bool publish, init ref<int32> value) { if (publish) { value = 42; return true; } return Fill(true, init value); }",107)]
    [TestCase("public bool Fill(bool publish, init ref<int32> value) { if (publish) { value = 42; return true; } if (Fill(true, init var nested)) { value = nested; return true; } return false; }",108)]
    public async Task NativeScalarFrontendSingleInitReferenceUsesCurrentProjectionAsync(
        string text, int outputOperation)
    {
        var source = SourceText.From(text);
        foreach (var policy in new[] { NativeRuntimePolicy.FailFast, NativeRuntimePolicy.CheckedExceptions })
        {
            var stage = await CompileAsync(text,
                "using Terrum.CodeAnalysis; int32 Main() { require TryPublishScalarFrontend(); return 42; }",
                (BoundRuntimePolicy)policy, "ScalarFrontendProjection").ConfigureAwait(true);
            var native = NativeJit.ExecuteWithData(stage, policy);
            Assert.That(native.ExitCode, Is.EqualTo(42));
            var bytes = native.Data.AsSpan(native.BufferOffsets[ScalarFrontendHost.BufferIndex],
                ScalarFrontendHost.WordCount * sizeof(int)).ToArray();
            Assert.That(BinaryPrimitives.ReadInt32LittleEndian(bytes) == 87, Is.True, $"Status {BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(sizeof(int)))}, error {BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(2 * sizeof(int)))} at {BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(3 * sizeof(int)))}+{BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(4 * sizeof(int)))}");
            Assert.That(BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(17 * sizeof(int))), Is.EqualTo(2));
            var result = ScalarFrontendHost.ReadResult(source, bytes, (BoundRuntimePolicy)policy);
            Assert.That(result.IsValid, Is.True, $"Error {result.ErrorCode} at {result.ErrorSpan}");
            Assert.That(result.InitRefParameterMask, Is.EqualTo(2));
            Assert.That(global::System.Linq.Enumerable.Any(result.Nodes,node => node.Operation == outputOperation),Is.True);

            var actual = ScalarFrontendHost.BindLibrary(source, bytes, (BoundRuntimePolicy)policy);
            var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source), (BoundRuntimePolicy)policy);
            Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
            var hosted = ((global::System.Func<BoundProgram>)(() => { var __nunitActual24 = compilation.Program; Assert.That(__nunitActual24, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual24!; }))();
            Assert.That(NativeEmitter.EmitLibrary(actual, ["Fill"], policy), Is.EqualTo(NativeEmitter.EmitLibrary(hosted, ["Fill"], policy)));

            foreach (var version in new[] { 73, 74, 75, 76, 77, 78 })
            {
                BinaryPrimitives.WriteInt32LittleEndian(bytes, version);
                Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(source,
                    bytes, (BoundRuntimePolicy)policy));
            }
            BinaryPrimitives.WriteInt32LittleEndian(bytes, 87);
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(17 * sizeof(int)), 1);
            Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(source,
                bytes, (BoundRuntimePolicy)policy));
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(17 * sizeof(int)), 0);
            Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(source,
                bytes, (BoundRuntimePolicy)policy));
        }
    }

    [Test]
    public async Task NativeScalarFrontendSingleNonBooleanOutUsesCurrentProjectionAsync()
    {
        const string text = "public int32 Fill(int32 seed, out int32 value) { if (seed == 0) { value = 40; return 2; } return Fill(seed - 1, out value); }";
        var source = SourceText.From(text);
        foreach (var policy in new[] { NativeRuntimePolicy.FailFast, NativeRuntimePolicy.CheckedExceptions })
        {
            var stage = await CompileAsync(text,
                "using Terrum.CodeAnalysis; int32 Main() { require TryPublishScalarFrontend(); return 42; }",
                (BoundRuntimePolicy)policy, "ScalarFrontendProjection").ConfigureAwait(true);
            var native = NativeJit.ExecuteWithData(stage, policy);
            Assert.That(native.ExitCode, Is.EqualTo(42));
            var bytes = native.Data.AsSpan(native.BufferOffsets[ScalarFrontendHost.BufferIndex],
                ScalarFrontendHost.WordCount * sizeof(int)).ToArray();
            Assert.That(BinaryPrimitives.ReadInt32LittleEndian(bytes), Is.EqualTo(87));
            Assert.That(BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(16 * sizeof(int))), Is.EqualTo(2));

            var result = ScalarFrontendHost.ReadResult(source, bytes, (BoundRuntimePolicy)policy);
            Assert.That(result.IsValid, Is.True, $"Error {result.ErrorCode} at {result.ErrorSpan}");
            Assert.That(result.OutParameterMask, Is.EqualTo(2));
            Assert.That(global::System.Linq.Enumerable.Any(result.Nodes,static node => node.Operation == 102),Is.True);
            Assert.That(global::System.Linq.Enumerable.Any(result.Nodes,static node => node.Operation == 103),Is.True);

            var actual = ScalarFrontendHost.BindLibrary(source, bytes, (BoundRuntimePolicy)policy);
            var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source), (BoundRuntimePolicy)policy);
            Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
            var hosted = ((global::System.Func<BoundProgram>)(() => { var __nunitActual25 = compilation.Program; Assert.That(__nunitActual25, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual25!; }))();
            Assert.That(NativeEmitter.EmitLibrary(actual, ["Fill"], policy), Is.EqualTo(NativeEmitter.EmitLibrary(hosted, ["Fill"], policy)));

            foreach (var version in new[] { 73, 82 })
            {
                BinaryPrimitives.WriteInt32LittleEndian(bytes, version);
                Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(source,
                    bytes, (BoundRuntimePolicy)policy));
            }
        }
    }

    [Test]
    public async Task NativeScalarFrontendSingleNonBooleanInitRefUsesCurrentProjectionAsync()
    {
        const string text = "public int32 Fill(int32 seed, init ref<int32> value) { if (seed == 0) { value = 40; return 0; } return Fill(seed - 1, init value); }";
        var source = SourceText.From(text);
        foreach (var policy in new[] { NativeRuntimePolicy.FailFast, NativeRuntimePolicy.CheckedExceptions })
        {
            var stage = await CompileAsync(text,
                "using Terrum.CodeAnalysis; int32 Main() { require TryPublishScalarFrontend(); return 42; }",
                (BoundRuntimePolicy)policy, "ScalarFrontendProjection").ConfigureAwait(true);
            var native = NativeJit.ExecuteWithData(stage, policy);
            Assert.That(native.ExitCode, Is.EqualTo(42));
            var bytes = native.Data.AsSpan(native.BufferOffsets[ScalarFrontendHost.BufferIndex],
                ScalarFrontendHost.WordCount * sizeof(int)).ToArray();
            Assert.That(BinaryPrimitives.ReadInt32LittleEndian(bytes), Is.EqualTo(87));
            Assert.That(BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(17 * sizeof(int))), Is.EqualTo(2));

            var result = ScalarFrontendHost.ReadResult(source, bytes, (BoundRuntimePolicy)policy);
            Assert.That(result.IsValid, Is.True, $"Error {result.ErrorCode} at {result.ErrorSpan}");
            Assert.That(result.InitRefParameterMask, Is.EqualTo(2));
            Assert.That(global::System.Linq.Enumerable.Any(result.Nodes,static node => node.Operation == 102),Is.True);
            Assert.That(global::System.Linq.Enumerable.Any(result.Nodes,static node => node.Operation == 107),Is.True);

            var actual = ScalarFrontendHost.BindLibrary(source, bytes, (BoundRuntimePolicy)policy);
            var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source), (BoundRuntimePolicy)policy);
            Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
            var hosted = ((global::System.Func<BoundProgram>)(() => { var __nunitActual26 = compilation.Program; Assert.That(__nunitActual26, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual26!; }))();
            Assert.That(NativeEmitter.EmitLibrary(actual, ["Fill"], policy), Is.EqualTo(NativeEmitter.EmitLibrary(hosted, ["Fill"], policy)));

            foreach (var version in new[] { 73, 79, 80, 82 })
            {
                BinaryPrimitives.WriteInt32LittleEndian(bytes, version);
                Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(source,
                    bytes, (BoundRuntimePolicy)policy));
            }
        }
    }

    [Test]
    public async Task NativeScalarFrontendSingleNonBooleanOutVarUsesCurrentProjectionAsync()
    {
        const string text = "public int32 Fill(int32 seed, out int32 value) { if (seed == 0) { value = 40; return 2; } int32 status = Fill(seed - 1, out var nested); value = nested; return status; }";
        var source = SourceText.From(text);
        foreach (var policy in new[] { NativeRuntimePolicy.FailFast, NativeRuntimePolicy.CheckedExceptions })
        {
            var stage = await CompileAsync(text,
                "using Terrum.CodeAnalysis; int32 Main() { require TryPublishScalarFrontend(); return 42; }",
                (BoundRuntimePolicy)policy, "ScalarFrontendProjection").ConfigureAwait(true);
            var native = NativeJit.ExecuteWithData(stage, policy);
            Assert.That(native.ExitCode, Is.EqualTo(42));
            var bytes = native.Data.AsSpan(native.BufferOffsets[ScalarFrontendHost.BufferIndex],
                ScalarFrontendHost.WordCount * sizeof(int)).ToArray();
            Assert.That(BinaryPrimitives.ReadInt32LittleEndian(bytes), Is.EqualTo(87));
            var result = ScalarFrontendHost.ReadResult(source, bytes, (BoundRuntimePolicy)policy);
            Assert.That(result.IsValid, Is.True, $"Error {result.ErrorCode} at {result.ErrorSpan}");
            Assert.That(global::System.Linq.Enumerable.Any(result.Nodes,static node => node.Operation == 104),Is.True);
            Assert.That(global::System.Linq.Enumerable.Any(result.Nodes,static node => node.Operation == 102),Is.True);

            var actual = ScalarFrontendHost.BindLibrary(source, bytes, (BoundRuntimePolicy)policy);
            var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source), (BoundRuntimePolicy)policy);
            Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
            var hosted = ((global::System.Func<BoundProgram>)(() => { var __nunitActual27 = compilation.Program; Assert.That(__nunitActual27, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual27!; }))();
            Assert.That(NativeEmitter.EmitLibrary(actual, ["Fill"], policy), Is.EqualTo(NativeEmitter.EmitLibrary(hosted, ["Fill"], policy)));
        }
    }
    [TestCase("int32 Value(bool x, bool y, bool z) { if (z && x) { return 42; } return 0; }",true,false,true)]
    [TestCase("int32 Value(bool x, bool y, bool z) { if (!z) { return 42; } return 0; }",false,true,false)]
    [TestCase("int32 Value(bool x, bool y, bool z) { if (z || x) { return 42; } return 0; }",true,false,false)]
    [TestCase("int32 Value(bool x, bool y, bool z) { if (z && false) { return 0; } return 42; }",true,true,true)]
    public async Task NativeScalarFrontendThirdBooleanParameterExecutesAsync(
        string text, bool x, bool y, bool z)
    {
        var source = SourceText.From(text);
        foreach (var policy in new[] { NativeRuntimePolicy.FailFast, NativeRuntimePolicy.CheckedExceptions })
        {
            var stage = await CompileAsync(text,
                "using Terrum.CodeAnalysis; int32 Main() { require TryPublishScalarFrontend(); return 42; }",
                (BoundRuntimePolicy)policy, "ScalarFrontendProjection").ConfigureAwait(true);
            var native = NativeJit.ExecuteWithData(stage, policy);
            Assert.That(native.ExitCode, Is.EqualTo(42));
            var library = ScalarFrontendHost.BindLibrary(source,
                native.Data.AsSpan(native.BufferOffsets[ScalarFrontendHost.BufferIndex],
                    ScalarFrontendHost.WordCount * sizeof(int)), (BoundRuntimePolicy)policy);
            var call = new BoundCall(0, [
                new BoundConstant(x ? 1 : 0, PrimitiveType.Bool, default),
                new BoundConstant(y ? 1 : 0, PrimitiveType.Bool, default),
                new BoundConstant(z ? 1 : 0, PrimitiveType.Bool, default),
            ], PrimitiveType.Int32, default);
            var main = new BoundFunction("Main", PrimitiveType.Int32, [], 0,
                new BoundBlock([new BoundReturn(call, default)], default), default);
            var executable = library with { Functions = [.. library.Functions, main], EntryPoint = 1 };
            SemanticVerifier.Verify(executable);
            Assert.That(NativeJit.Execute(executable, policy), Is.EqualTo(42));
            await ExecuteAotAsync(executable, policy).ConfigureAwait(true);
        }
    }
    [TestCase("int32 Value(int32 x) { return x; return x; }")]
    [TestCase("int32 Value(int32 x) { return x + x; }")]
    [TestCase("int32 Value(int32 x) { if (true) { return x; } }")]
    [TestCase("int32 Value(int32 x) { return x; } int32 Other() { return 1; }")]
    public void ScalarFrontendProjectionRejectsForgedBodyShape(string text)
    {
        var words = new int[ScalarFrontendHost.WordCount];
        words[0] = 87;
        words[1] = 1;
        words[5] = 1;
        words[6] = 1;
        words[7] = 1;
        words[9] = 6;
        words[10] = 5;
        words[11] = 18;
        words[12] = 1;
        words[22] = 1;
        words[24] = 1;
        words[26] = 30;
        words[27] = 1;
        var bytes = new byte[words.Length * sizeof(int)];
        for (var at = 0; at < words.Length; at++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(at * sizeof(int)), words[at]);
        }
        Assert.That(ScalarFrontendHost.ReadResult(
            SourceText.From("int32 Value(int32 x) { return x; }"), bytes).IsValid, Is.True);
        Assert.Throws<ArgumentException>(() =>
            ScalarFrontendHost.ReadResult(SourceText.From(text), bytes));
    }
    [TestCase("int32 Value() { return 42; }")]
    [TestCase("uint64 Value() { return 18446744073709551615; }")]
    public async Task ScalarFrontendProjectionRejectsForgedIntegerBitsAsync(string text)
    {
        var source = SourceText.From(text);
        var program = await CompileAsync(text,
            "using Terrum.CodeAnalysis; int32 Main() { require TryPublishScalarFrontend(); return 42; }",
            BoundRuntimePolicy.FailFast, "ScalarFrontendProjection").ConfigureAwait(true);
        var native = NativeJit.ExecuteWithData(program, NativeRuntimePolicy.FailFast);
        Assert.That(native.ExitCode, Is.EqualTo(42));
        var bytes = native.Data.AsSpan(native.BufferOffsets[ScalarFrontendHost.BufferIndex],
            ScalarFrontendHost.WordCount * sizeof(int)).ToArray();
        var original = ScalarFrontendHost.ReadResult(source, bytes);
        Assert.That(original.WhenTrue.Operation, Is.EqualTo(3));
        foreach (var word in new[] { 24, 25 })
        {
            var offset = word * sizeof(int);
            var value = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset));
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset), value ^ 1);
            Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(source, bytes));
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset), value);
        }
    }
    [TestCase("int64 Value(int64 x, int64 y) { return y + 0xFFFFFFFFFFFFFFFF; }",257)]
    [TestCase("int64 Value(int64 x, int64 y) { return 0xFFFFFFFFFFFFFFFF + y; }",457)]
    public async Task ScalarFrontendProjectionRejectsForgedParameterLiteralWordsAsync(
        string text, int expectedOperation)
    {
        var source = SourceText.From(text);
        var stage = await CompileAsync(text,
            "using Terrum.CodeAnalysis; int32 Main() { require TryPublishScalarFrontend(); return 42; }",
            BoundRuntimePolicy.FailFast, "ScalarFrontendProjection").ConfigureAwait(true);
        var native = NativeJit.ExecuteWithData(stage, NativeRuntimePolicy.FailFast);
        Assert.That(native.ExitCode, Is.EqualTo(42));
        var bytes = native.Data.AsSpan(native.BufferOffsets[ScalarFrontendHost.BufferIndex],
            ScalarFrontendHost.WordCount * sizeof(int)).ToArray();
        Assert.That(ScalarFrontendHost.ReadResult(source, bytes).WhenTrue.Operation, Is.EqualTo(expectedOperation));
        foreach (var (word, replacement) in new[] { (0, 2), (23, 158), (24, 0), (25, 0) })
        {
            var offset = word * sizeof(int);
            var original = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset));
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset), replacement);
            Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(source, bytes));
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset), original);
        }
    }
    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public async Task ScalarFrontendProjectionDoesNotPublishLexicalFailuresAsync(
        NativeRuntimePolicy policy)
    {
        const string Source = "\uD800";
        var program = await CompileAsync(Source,
            "using Terrum.CodeAnalysis; int32 Main() { if (TryPublishScalarFrontend()) { return 1; } return 42; }",
            (BoundRuntimePolicy)policy, "ScalarFrontendProjection").ConfigureAwait(true);
        var native = NativeJit.ExecuteWithData(program, policy);
        Assert.That(native.ExitCode, Is.EqualTo(42));
        Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(SourceText.From(Source),
            native.Data.AsSpan(native.BufferOffsets[ScalarFrontendHost.BufferIndex],
                ScalarFrontendHost.WordCount * sizeof(int))));
        await ExecuteAotAsync(program, policy).ConfigureAwait(true);
    }
    [TestCase(NativeRuntimePolicy.FailFast,20,22,false)]
    [TestCase(NativeRuntimePolicy.FailFast,50,8,false)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions,20,22,false)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions,50,8,false)]
    [TestCase(NativeRuntimePolicy.FailFast,20,22,true)]
    public async Task NativeScalarFrontendFactsBindAndExecuteWithoutHostedParsingAsync(
        NativeRuntimePolicy policy, int first, int second, bool comments)
    {
        var text = comments
            ? "int32 Value(int32 x, int32 y) { if ((x)/* test */< (y)) { return (x)/* test */+ ((y)); } else { return x - y; } }"
            : "int32 Value(int32 x, int32 y) { if (x < y) { return x + y; } else { return x - y; } }";
        var source = SourceText.From(text);
        var stage = await CompileAsync(text,
            "using Terrum.CodeAnalysis; int32 Main() { require TryPublishScalarFrontend(); return 42; }",
            (BoundRuntimePolicy)policy, "ScalarFrontendProjection").ConfigureAwait(true);
        var native = NativeJit.ExecuteWithData(stage, policy);
        Assert.That(native.ExitCode, Is.EqualTo(42));
        Assert.That(BinaryPrimitives.ReadInt32LittleEndian(
            native.Data.AsSpan(native.BufferOffsets[ScalarFrontendHost.BufferIndex])), Is.EqualTo(87));
        if (comments)
        {
            var projected = native.Data.AsSpan(native.BufferOffsets[ScalarFrontendHost.BufferIndex],
                ScalarFrontendHost.WordCount * sizeof(int)).ToArray();
            var right = BinaryPrimitives.ReadInt32LittleEndian(projected.AsSpan(25 * sizeof(int)));
            BinaryPrimitives.WriteInt32LittleEndian(projected.AsSpan(25 * sizeof(int)), 1);
            Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(source, projected));
            BinaryPrimitives.WriteInt32LittleEndian(projected.AsSpan(25 * sizeof(int)), right);
            var length = BinaryPrimitives.ReadInt32LittleEndian(projected.AsSpan(27 * sizeof(int)));
            BinaryPrimitives.WriteInt32LittleEndian(projected.AsSpan(27 * sizeof(int)), length - 1);
            Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(source, projected));
            BinaryPrimitives.WriteInt32LittleEndian(projected.AsSpan(27 * sizeof(int)), length);
            var start = BinaryPrimitives.ReadInt32LittleEndian(projected.AsSpan(26 * sizeof(int)));
            BinaryPrimitives.WriteInt32LittleEndian(projected.AsSpan(26 * sizeof(int)), start + 1);
            Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(source, projected));
        }
        var library = ScalarFrontendHost.BindLibrary(source,
            native.Data.AsSpan(native.BufferOffsets[ScalarFrontendHost.BufferIndex],
                ScalarFrontendHost.WordCount * sizeof(int)), (BoundRuntimePolicy)policy);
        Assert.That(library.Functions, Has.Exactly(1).Items);
        Assert.That(library.Functions[0].IsInternal, Is.True);
        Assert.That(library.Buffers, Is.Empty);
        Assert.Throws<ArgumentException>(() => NativeEmitter.EmitLibrary(library, ["Value"], policy));

        var result = new BoundCall(0, [
            new BoundConstant(first, PrimitiveType.Int32, default),
            new BoundConstant(second, PrimitiveType.Int32, default),
        ], PrimitiveType.Int32, default);
        var main = new BoundFunction("Main", PrimitiveType.Int32, [], 0,
            new BoundBlock([new BoundReturn(result, default)], default), default);
        var executable = library with { Functions = [.. library.Functions, main], EntryPoint = 1 };
        SemanticVerifier.Verify(executable);
        Assert.That(NativeJit.Execute(executable, policy), Is.EqualTo(42));
        await ExecuteAotAsync(executable, policy).ConfigureAwait(true);
    }
    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public async Task NativeScalarFrontendIntegerFactsExecuteWithoutHostedBindingAsync(
        NativeRuntimePolicy policy)
    {
        const string Text = "int32 Value() { return 0x2A; }";
        var source = SourceText.From(Text);
        var stage = await CompileAsync(Text,
            "using Terrum.CodeAnalysis; int32 Main() { require TryPublishScalarFrontend(); return 42; }",
            (BoundRuntimePolicy)policy, "ScalarFrontendProjection").ConfigureAwait(true);
        var native = NativeJit.ExecuteWithData(stage, policy);
        Assert.That(native.ExitCode, Is.EqualTo(42));
        var library = ScalarFrontendHost.BindLibrary(source,
            native.Data.AsSpan(native.BufferOffsets[ScalarFrontendHost.BufferIndex],
                ScalarFrontendHost.WordCount * sizeof(int)), (BoundRuntimePolicy)policy);
        var result = new BoundCall(0, [], PrimitiveType.Int32, default);
        var main = new BoundFunction("Main", PrimitiveType.Int32, [], 0,
            new BoundBlock([new BoundReturn(result, default)], default), default);
        var executable = library with { Functions = [.. library.Functions, main], EntryPoint = 1 };
        SemanticVerifier.Verify(executable);
        Assert.That(NativeJit.Execute(executable, policy), Is.EqualTo(42));
        await ExecuteAotAsync(executable, policy).ConfigureAwait(true);
    }
    [TestCase(NativeRuntimePolicy.FailFast,"int32 Value(int32 x, int32 y) { return y + 5; }",0,37)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions,"int32 Value(int32 x, int32 y) { return y + 5; }",0,37)]
    [TestCase(NativeRuntimePolicy.FailFast,"int32 Value(int32 x, int32 y) { return 47 - x; }",5,0)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions,"int32 Value(int32 x, int32 y) { return 47 - x; }",5,0)]
    [TestCase(NativeRuntimePolicy.FailFast,"int32 Value(int32 x, int32 y) { return x * 7; }",6,0)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions,"int32 Value(int32 x, int32 y) { return x * 7; }",6,0)]
    [TestCase(NativeRuntimePolicy.FailFast,"int32 Value(int32 x, int32 y) { return y / 2; }",0,84)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions,"int32 Value(int32 x, int32 y) { return y / 2; }",0,84)]
    [TestCase(NativeRuntimePolicy.FailFast,"int32 Value(int32 x, int32 y) { return 42 % x; }",43,0)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions,"int32 Value(int32 x, int32 y) { return 42 % x; }",43,0)]
    [TestCase(NativeRuntimePolicy.FailFast,"int32 Value(int32 x, int32 y) { return x & 63; }",42,0)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions,"int32 Value(int32 x, int32 y) { return x & 63; }",42,0)]
    [TestCase(NativeRuntimePolicy.FailFast,"int32 Value(int32 x, int32 y) { return y | 42; }",0,0)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions,"int32 Value(int32 x, int32 y) { return y | 42; }",0,0)]
    [TestCase(NativeRuntimePolicy.FailFast,"int32 Value(int32 x, int32 y) { return 42 ^ x; }",0,0)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions,"int32 Value(int32 x, int32 y) { return 42 ^ x; }",0,0)]
    [TestCase(NativeRuntimePolicy.FailFast,"int32 Value(int32 x, int32 y) { return ((x)) + ((5)); }",37,0)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions,"int32 Value(int32 x, int32 y) { return ((47)) - (x); }",5,0)]
    [TestCase(NativeRuntimePolicy.FailFast,"int32 Value(int32 x, int32 y) { return x + -5; }",47,0)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions,"int32 Value(int32 x, int32 y) { return (-5) + x; }",47,0)]
    [TestCase(NativeRuntimePolicy.FailFast,"int32 Value(int32 x, int32 y) { return ((x)) + ((-5)); }",47,0)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions,"int32 Value(int32 x, int32 y) { return x - (-5); }",37,0)]
    [TestCase(NativeRuntimePolicy.FailFast,"int32 Value(int32 x, int32 y) { return x + +5; }",37,0)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions,"int32 Value(int32 x, int32 y) { return (+5) + x; }",37,0)]
    [TestCase(NativeRuntimePolicy.FailFast,"int32 Value(int32 x, int32 y) { return x - (+5); }",47,0)]
    public async Task NativeScalarFrontendParameterLiteralFactsExecuteAsync(
        NativeRuntimePolicy policy, string text, int first, int second)
    {
        var source = SourceText.From(text);
        var stage = await CompileAsync(text,
            "using Terrum.CodeAnalysis; int32 Main() { require TryPublishScalarFrontend(); return 42; }",
            (BoundRuntimePolicy)policy, "ScalarFrontendProjection").ConfigureAwait(true);
        var native = NativeJit.ExecuteWithData(stage, policy);
        Assert.That(native.ExitCode, Is.EqualTo(42));
        var library = ScalarFrontendHost.BindLibrary(source,
            native.Data.AsSpan(native.BufferOffsets[ScalarFrontendHost.BufferIndex],
                ScalarFrontendHost.WordCount * sizeof(int)), (BoundRuntimePolicy)policy);
        var call = new BoundCall(0, [
            new BoundConstant(first, PrimitiveType.Int32, default),
            new BoundConstant(second, PrimitiveType.Int32, default),
        ], PrimitiveType.Int32, default);
        var main = new BoundFunction("Main", PrimitiveType.Int32, [], 0,
            new BoundBlock([new BoundReturn(call, default)], default), default);
        var executable = library with { Functions = [.. library.Functions, main], EntryPoint = 1 };
        SemanticVerifier.Verify(executable);
        Assert.That(NativeJit.Execute(executable, policy), Is.EqualTo(42));
        await ExecuteAotAsync(executable, policy).ConfigureAwait(true);
    }
    [TestCase(NativeRuntimePolicy.FailFast,"!x",3,62,0)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions,"!x",3,62,0)]
    [TestCase(NativeRuntimePolicy.FailFast,"!((x))",3,62,0)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions,"!((x))",3,62,0)]
    [TestCase(NativeRuntimePolicy.FailFast,"!(!x)",15,16062,1)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions,"!((!x))",15,16062,1)]
    [TestCase(NativeRuntimePolicy.FailFast,"!(!true)",15,16062,0)]
    public async Task NativeScalarFrontendBooleanNegationExecutesAsync(
        NativeRuntimePolicy policy, string expression, int version, int operation,
        int argument)
    {
        var text = $"bool Value(bool x) {{ return {expression}; }}";
        var source = SourceText.From(text);
        var stage = await CompileAsync(text,
            "using Terrum.CodeAnalysis; int32 Main() { require TryPublishScalarFrontend(); return 42; }",
            (BoundRuntimePolicy)policy, "ScalarFrontendProjection").ConfigureAwait(true);
        var native = NativeJit.ExecuteWithData(stage, policy);
        Assert.That(native.ExitCode, Is.EqualTo(42));
        var bytes = native.Data.AsSpan(native.BufferOffsets[ScalarFrontendHost.BufferIndex],
            ScalarFrontendHost.WordCount * sizeof(int)).ToArray();
        Assert.That(BinaryPrimitives.ReadInt32LittleEndian(bytes), Is.EqualTo(87));
        Assert.That(ScalarFrontendHost.ReadResult(source, bytes).WhenTrue.Operation, Is.EqualTo(ExpectedScalarFrontendOperation(source, bytes, operation)));
        foreach (var word in new[] { 0, 23, 24, 25 })
        {
            var offset = word * sizeof(int);
            var original = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset));
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset),
                word == 0 ? version - 1 : original ^ 1);
            Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(source, bytes));
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset), original);
        }
        var library = ScalarFrontendHost.BindLibrary(source, bytes, (BoundRuntimePolicy)policy);
        var call = new BoundCall(0, [new BoundConstant(argument, PrimitiveType.Bool, default)],
            PrimitiveType.Bool, default);
        var branch = new BoundIf(call,
            new BoundBlock([new BoundReturn(new BoundConstant(42, PrimitiveType.Int32, default),
                default)], default),
            new BoundBlock([new BoundReturn(new BoundConstant(0, PrimitiveType.Int32, default),
                default)], default), default);
        var main = new BoundFunction("Main", PrimitiveType.Int32, [], 0,
            new BoundBlock([branch], default), default);
        var executable = library with { Functions = [.. library.Functions, main], EntryPoint = 1 };
        SemanticVerifier.Verify(executable);
        Assert.That(NativeJit.Execute(executable, policy), Is.EqualTo(42));
        await ExecuteAotAsync(executable, policy).ConfigureAwait(true);
    }
    [TestCase(NativeRuntimePolicy.FailFast,"bool Value(int32 x) { return x >= 42; }",        PrimitiveType.Int32,42L,170)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions,"bool Value(int32 x) { return x >= 42; }",        PrimitiveType.Int32,42L,170)]
    [TestCase(NativeRuntimePolicy.FailFast,"bool Value(int32 x) { return 42 < x; }",        PrimitiveType.Int32,43L,367)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions,"bool Value(int32 x) { return 42 < x; }",        PrimitiveType.Int32,43L,367)]
    [TestCase(NativeRuntimePolicy.FailFast,        "bool Value(uint64 x) { return x == 18446744073709551615; }",        PrimitiveType.UInt64,-1L,165)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions,        "bool Value(uint64 x) { return x == 18446744073709551615; }",        PrimitiveType.UInt64,-1L,165)]
    [TestCase(NativeRuntimePolicy.FailFast,"bool Value(char16 x) { return x != 0xFFFF; }",        PrimitiveType.Char16,1L,166)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions,"bool Value(char16 x) { return x != 0xFFFF; }",        PrimitiveType.Char16,1L,166)]
    [TestCase(NativeRuntimePolicy.FailFast,"bool Value(char16 x) { return 0xFFFF != x; }",        PrimitiveType.Char16,1L,366)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions,"bool Value(char16 x) { return 0xFFFF != x; }",        PrimitiveType.Char16,1L,366)]
    [TestCase(NativeRuntimePolicy.FailFast,"bool Value(int32 x) { return ((x)) >= (42); }",        PrimitiveType.Int32,42L,70)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions,"bool Value(int32 x) { return (42) < ((x)); }",        PrimitiveType.Int32,43L,67)]
    [TestCase(NativeRuntimePolicy.FailFast,"bool Value(int32 x) { return x < -5; }",        PrimitiveType.Int32,-6L,1367)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions,"bool Value(int32 x) { return (-5) < x; }",        PrimitiveType.Int32,42L,67)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions,"bool Value(int32 x) { return x == -2147483648; }",        PrimitiveType.Int32,int.MinValue,1365)]
    [TestCase(NativeRuntimePolicy.FailFast,"bool Value(int32 x) { return x == -0xFFFFFFFF; }",        PrimitiveType.Int32,1L,1365)]
    [TestCase(NativeRuntimePolicy.FailFast,"bool Value(int32 x) { return x < +42; }",        PrimitiveType.Int32,41L,1767)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions,"bool Value(int32 x) { return (+42) < x; }",        PrimitiveType.Int32,43L,67)]
    [TestCase(NativeRuntimePolicy.FailFast,"bool Value(uint64 x) { return x == +42; }",        PrimitiveType.UInt64,42L,1765)]
    [TestCase(NativeRuntimePolicy.FailFast,"bool Value(int64 x) { return x != -9223372036854775808; }",        PrimitiveType.Int64,0L,1366)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions,"bool Value(int64 x) { return -9223372036854775808 == x; }",        PrimitiveType.Int64,long.MinValue,1565)]
    public async Task NativeScalarFrontendLiteralComparisonExecutesAsync(
        NativeRuntimePolicy policy, string text, PrimitiveType parameterType, long argument,
        int operation)
    {
        var source = SourceText.From(text);
        var stage = await CompileAsync(text,
            "using Terrum.CodeAnalysis; int32 Main() { require TryPublishScalarFrontend(); return 42; }",
            (BoundRuntimePolicy)policy, "ScalarFrontendProjection").ConfigureAwait(true);
        var native = NativeJit.ExecuteWithData(stage, policy);
        Assert.That(native.ExitCode, Is.EqualTo(42));
        var bytes = native.Data.AsSpan(native.BufferOffsets[ScalarFrontendHost.BufferIndex],
            ScalarFrontendHost.WordCount * sizeof(int)).ToArray();
        Assert.That(ScalarFrontendHost.ReadResult(source, bytes).WhenTrue.Operation, Is.EqualTo(ExpectedScalarFrontendOperation(source, bytes, operation)));
        if (operation >= 1200)
        {
            BinaryPrimitives.WriteInt32LittleEndian(bytes, 3);
            Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(source, bytes));
            BinaryPrimitives.WriteInt32LittleEndian(bytes, 87);
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(23 * sizeof(int)),
                operation - (operation >= 1600 ? 1600 : 1200));
            Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(source, bytes));
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(23 * sizeof(int)), operation);
        }
        foreach (var word in new[] { 22, 23, 24, 25 })
        {
            var offset = word * sizeof(int);
            var original = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset));
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset), original ^ 1);
            Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(source, bytes));
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset), original);
        }
        var library = ScalarFrontendHost.BindLibrary(source, bytes, (BoundRuntimePolicy)policy);
        var call = new BoundCall(0, [new BoundConstant(argument, parameterType, default)],
            PrimitiveType.Bool, default);
        var branch = new BoundIf(call,
            new BoundBlock([new BoundReturn(new BoundConstant(42, PrimitiveType.Int32, default),
                default)], default),
            new BoundBlock([new BoundReturn(new BoundConstant(0, PrimitiveType.Int32, default),
                default)], default), default);
        var main = new BoundFunction("Main", PrimitiveType.Int32, [], 0,
            new BoundBlock([branch], default), default);
        var executable = library with { Functions = [.. library.Functions, main], EntryPoint = 1 };
        SemanticVerifier.Verify(executable);
        Assert.That(NativeJit.Execute(executable, policy), Is.EqualTo(42));
        await ExecuteAotAsync(executable, policy).ConfigureAwait(true);
    }
    [TestCase(NativeRuntimePolicy.FailFast,"int32 Value(int32 x) { return ~x; }",        -43L,63)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions,"int32 Value(int32 x) { return ~x; }",        -43L,63)]
    [TestCase(NativeRuntimePolicy.FailFast,"int32 Value() { return ~0xFFFFFFD5; }",        0L,163)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions,"int32 Value() { return ~0xFFFFFFD5; }",        0L,163)]
    [TestCase(NativeRuntimePolicy.FailFast,"int32 Value() { return +42; }",        0L,657)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions,"int32 Value() { return +42; }",        0L,657)]
    [TestCase(NativeRuntimePolicy.FailFast,"int32 Value(int32 x) { return ~((x)); }",        -43L,63)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions,"int32 Value() { return ~(0xFFFFFFD5); }",        0L,163)]
    [TestCase(NativeRuntimePolicy.FailFast,"int32 Value() { return +((42)); }",        0L,657)]
    [TestCase(NativeRuntimePolicy.FailFast,"int32 Value() { return -(0xFFFFFFD6); }",        0L,658)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions,"int32 Value() { return -(0xFFFFFFD6); }",        0L,658)]
    public async Task NativeScalarFrontendIntegerUnaryExecutesAsync(
        NativeRuntimePolicy policy, string text, long argument, int operation)
    {
        var source = SourceText.From(text);
        var stage = await CompileAsync(text,
            "using Terrum.CodeAnalysis; int32 Main() { require TryPublishScalarFrontend(); return 42; }",
            (BoundRuntimePolicy)policy, "ScalarFrontendProjection").ConfigureAwait(true);
        var native = NativeJit.ExecuteWithData(stage, policy);
        Assert.That(native.ExitCode, Is.EqualTo(42));
        var bytes = native.Data.AsSpan(native.BufferOffsets[ScalarFrontendHost.BufferIndex],
            ScalarFrontendHost.WordCount * sizeof(int)).ToArray();
        if (operation == 658)
        {
            Assert.That(BinaryPrimitives.ReadInt32LittleEndian(bytes), Is.EqualTo(87));
            BinaryPrimitives.WriteInt32LittleEndian(bytes, 6);
            Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(source, bytes));
            BinaryPrimitives.WriteInt32LittleEndian(bytes, 87);
        }
        Assert.That(ScalarFrontendHost.ReadResult(source, bytes).WhenTrue.Operation, Is.EqualTo(ExpectedScalarFrontendOperation(source, bytes, operation)));
        foreach (var word in new[] { 22, 23, 24, 25 })
        {
            var offset = word * sizeof(int);
            var original = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset));
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset), original ^ 1);
            Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(source, bytes));
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset), original);
        }
        var library = ScalarFrontendHost.BindLibrary(source, bytes, (BoundRuntimePolicy)policy);
        var call = new BoundCall(0, operation == 63
            ? [new BoundConstant(argument, PrimitiveType.Int32, default)] : [],
            PrimitiveType.Int32, default);
        var main = new BoundFunction("Main", PrimitiveType.Int32, [], 0,
            new BoundBlock([new BoundReturn(call, default)], default), default);
        var executable = library with { Functions = [.. library.Functions, main], EntryPoint = 1 };
        SemanticVerifier.Verify(executable);
        Assert.That(NativeJit.Execute(executable, policy), Is.EqualTo(42));
        await ExecuteAotAsync(executable, policy).ConfigureAwait(true);
    }
    [TestCase(NativeRuntimePolicy.FailFast,"int32 Value(int32 x, int32 y) { return ~(x + y); }",11,4057,"int32",10L,32L,2,-43L)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions,"int32 Value(int32 x, int32 y) { return ~((x) & (y)); }",11,4071,"int32",7L,3L,2,-4L)]
    [TestCase(NativeRuntimePolicy.FailFast,"int32 Value(int32 x) { return ~(5 | x); }",11,4373,"int32",2L,0L,1,-8L)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions,"int32 Value() { return ~(0xF ^ 0xA); }",11,4575,"int32",0L,0L,0,-6L)]
    [TestCase(NativeRuntimePolicy.FailFast,"uint64 Value(uint64 x) { return ~(x & 0xFFFFFFFFFFFFFFFF); }",11,4171,"uint64",15L,0L,1,-16L)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions,"int32 Value(int32 x, int32 y) { return +(x + y); }",12,6057,"int32",10L,32L,2,42L)]
    [TestCase(NativeRuntimePolicy.FailFast,"int32 Value(int32 x, int32 y) { return -((x) + (y)); }",12,8057,"int32",10L,32L,2,-42L)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions,"int64 Value(int64 x, int64 y) { return -(x & y); }",12,8071,"int64",7L,3L,2,-3L)]
    [TestCase(NativeRuntimePolicy.FailFast,"uint32 Value(uint32 x) { return +(x | 5); }",12,6173,"uint32",2L,0L,1,7L)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions,"int32 Value(int32 x) { return -(5 ^ x); }",12,8375,"int32",2L,0L,1,-7L)]
    [TestCase(NativeRuntimePolicy.FailFast,"int32 Value() { return +(42 + 1); }",12,6557,"int32",0L,0L,0,43L)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions,"int32 Value() { return -(0xF ^ 0xA); }",12,8575,"int32",0L,0L,0,-5L)]
    [TestCase(NativeRuntimePolicy.FailFast,"int32 Value(int32 x) { return ~(-x); }",13,10058,"int32",42L,0L,1,41L)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions,"uint32 Value(uint32 x) { return ~(+((x))); }",13,10057,"uint32",0L,0L,1,-1L)]
    [TestCase(NativeRuntimePolicy.FailFast,"int64 Value(int64 x) { return ~(-(x)); }",13,10058,"int64",-1L,0L,1,-2L)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions,"int32 Value() { return ~(-2147483648); }",13,10003,"int32",0L,0L,0,2147483647L)]
    [TestCase(NativeRuntimePolicy.FailFast,"int64 Value() { return ~(-9223372036854775808); }",13,10003,"int64",0L,0L,0,9223372036854775807L)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions,"int32 Value() { return ~(-0xFFFFFFFF); }",13,10003,"int32",0L,0L,0,-2L)]
    [TestCase(NativeRuntimePolicy.FailFast,"int32 Value() { return ~(-(42)); }",13,10658,"int32",0L,0L,0,41L)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions,"int32 Value() { return ~(+((42))); }",13,10657,"int32",0L,0L,0,-43L)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions,"int32 Value(int32 x) { return -(~x); }",14,14063,"int32",42L,0L,1,43L)]
    [TestCase(NativeRuntimePolicy.FailFast,"int32 Value(int32 x) { return +((~x)); }",14,12063,"int32",42L,0L,1,-43L)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions,"uint64 Value(uint64 x) { return +(~(x)); }",14,12063,"uint64",0L,0L,1,-1L)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions,"int32 Value() { return -(~42); }",14,14163,"int32",0L,0L,0,43L)]
    [TestCase(NativeRuntimePolicy.FailFast,"int32 Value() { return +((~42)); }",14,12163,"int32",0L,0L,0,-43L)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions,"uint32 Value() { return +(~0xFFFFFFFF); }",14,12163,"uint32",0L,0L,0,0L)]
    [TestCase(NativeRuntimePolicy.FailFast,"int32 Value(int32 x, int32 y) { return ~(+(x + y)); }",16,22057,"int32",10L,32L,2,-43L)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions,"int64 Value(int64 x, int64 y) { return ~(-(x & y)); }",16,24071,"int64",7L,3L,2,2L)]
    [TestCase(NativeRuntimePolicy.FailFast,"uint32 Value(uint32 x) { return ~(+(x | 5)); }",16,22173,"uint32",2L,0L,1,-8L)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions,"int32 Value(int32 x) { return ~(-(5 ^ x)); }",16,24375,"int32",2L,0L,1,6L)]
    [TestCase(NativeRuntimePolicy.FailFast,"int32 Value() { return ~(+(42 + 1)); }",16,22557,"int32",0L,0L,0,-44L)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions,"int32 Value(int32 x, int32 y) { return +(~(x + y)); }",17,30057,"int32",10L,32L,2,-43L)]
    [TestCase(NativeRuntimePolicy.FailFast,"int32 Value(int32 x, int32 y) { return -(~(x + y)); }",17,32057,"int32",10L,32L,2,43L)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions,"uint32 Value(uint32 x) { return +(~(5 | x)); }",17,30373,"uint32",2L,0L,1,-8L)]
    [TestCase(NativeRuntimePolicy.FailFast,"int64 Value(int64 x, int64 y) { return -(~(x & y)); }",17,32071,"int64",7L,3L,2,4L)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions,"int32 Value() { return +(~(0xF ^ 0xA)); }",17,30575,"int32",0L,0L,0,-6L)]
    [TestCase(NativeRuntimePolicy.FailFast,"int32 Value(int32 x, int32 y) { return ~(~(x + y)); }",18,38057,"int32",10L,32L,2,42L)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions,"int64 Value(int64 x, int64 y) { return ~(~((x) & (y))); }",18,38071,"int64",7L,3L,2,3L)]
    [TestCase(NativeRuntimePolicy.FailFast,"uint64 Value(uint64 x) { return ~(~(x & 0xFFFFFFFFFFFFFFFF)); }",18,38171,"uint64",15L,0L,1,15L)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions,"int32 Value() { return ~(~(0xF ^ 0xA)); }",18,38575,"int32",0L,0L,0,5L)]
    public async Task NativeScalarFrontendGroupedIntegerUnaryExecutesAsync(
        NativeRuntimePolicy policy, string text, int version, int operation, string typeName,
        long first, long second, int parameterCount, long expected)
    {
        var source = SourceText.From(text);
        var stage = await CompileAsync(text,
            "using Terrum.CodeAnalysis; int32 Main() { require TryPublishScalarFrontend(); return 42; }",
            (BoundRuntimePolicy)policy, "ScalarFrontendProjection").ConfigureAwait(true);
        var native = NativeJit.ExecuteWithData(stage, policy);
        Assert.That(native.ExitCode, Is.EqualTo(42));
        var bytes = native.Data.AsSpan(native.BufferOffsets[ScalarFrontendHost.BufferIndex],
            ScalarFrontendHost.WordCount * sizeof(int)).ToArray();
        Assert.That(BinaryPrimitives.ReadInt32LittleEndian(bytes), Is.EqualTo(87));
        Assert.That(ScalarFrontendHost.ReadResult(source, bytes).WhenTrue.Operation, Is.EqualTo(ExpectedScalarFrontendOperation(source, bytes, operation)));
        BinaryPrimitives.WriteInt32LittleEndian(bytes, version - 1);
        Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(source, bytes));
        BinaryPrimitives.WriteInt32LittleEndian(bytes, 87);
        var expressionWords = ScalarFrontendExpressionWordOffsets(bytes);
        var operationOffset = expressionWords.Operation * sizeof(int);
        var originalOperation = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(operationOffset));
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(operationOffset), originalOperation + 1);
        Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(source, bytes));
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(operationOffset), originalOperation);
        var operandOffset = expressionWords.Left * sizeof(int);
        var original = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(operandOffset));
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(operandOffset), original ^ 1);
        Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(source, bytes));
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(operandOffset), original);
        if (operation is 4057 or 6057 or 22057)
        {
            Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(
                SourceText.From(text.Replace("x + y", "x - y", StringComparison.Ordinal)), bytes));
        }
        if (operation is 10003 or 10058 or 10658)
        {
            Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(
                SourceText.From(text.Replace("-", "+", StringComparison.Ordinal)), bytes));
        }
        if (operation is 12063 or 12163 or 14063 or 14163 or 30057 or 32057 or 38057)
        {
            Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(
                SourceText.From(text.Replace("~", "-", StringComparison.Ordinal)), bytes));
        }
        var type = typeName switch {
            "int64" => PrimitiveType.Int64,
            "uint32" => PrimitiveType.UInt32,
            "uint64" => PrimitiveType.UInt64,
            _ => PrimitiveType.Int32,
        };
        ImmutableArray<BoundExpression> arguments = parameterCount switch {
            0 => [],
            1 => [new BoundConstant(first, type, default)],
            _ => [new BoundConstant(first, type, default),
                new BoundConstant(second, type, default)],
        };
        var library = ScalarFrontendHost.BindLibrary(source, bytes, (BoundRuntimePolicy)policy);
        var call = new BoundCall(0, arguments, type, default);
        var matches = new BoundBinary(TokenKind.EqualsEquals, call,
            new BoundConstant(expected, type, default), PrimitiveType.Bool, default);
        var main = new BoundFunction("Main", PrimitiveType.Int32, [], 0,
            new BoundBlock([new BoundIf(matches,
                new BoundBlock([new BoundReturn(new BoundConstant(42, PrimitiveType.Int32,
                    default), default)], default),
                new BoundBlock([new BoundReturn(new BoundConstant(0, PrimitiveType.Int32,
                    default), default)], default), default)], default), default);
        var executable = library with { Functions = [.. library.Functions, main], EntryPoint = 1 };
        SemanticVerifier.Verify(executable);
        Assert.That(NativeJit.Execute(executable, policy), Is.EqualTo(42));
        await ExecuteAotAsync(executable, policy).ConfigureAwait(true);
    }
    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public async Task NativeScalarFrontendGroupedInt64MinusExecutesAsync(NativeRuntimePolicy policy)
    {
        const string text = "int64 Value() { return -(0xFFFFFFFFFFFFFFD6); }";
        var source = SourceText.From(text);
        var stage = await CompileAsync(text,
            "using Terrum.CodeAnalysis; int32 Main() { require TryPublishScalarFrontend(); return 42; }",
            (BoundRuntimePolicy)policy, "ScalarFrontendProjection").ConfigureAwait(true);
        var native = NativeJit.ExecuteWithData(stage, policy);
        Assert.That(native.ExitCode, Is.EqualTo(42));
        var bytes = native.Data.AsSpan(native.BufferOffsets[ScalarFrontendHost.BufferIndex],
            ScalarFrontendHost.WordCount * sizeof(int)).ToArray();
        Assert.That(BinaryPrimitives.ReadInt32LittleEndian(bytes), Is.EqualTo(87));
        Assert.That(ScalarFrontendHost.ReadResult(source, bytes).WhenTrue.Operation, Is.EqualTo(ExpectedScalarFrontendOperation(source, bytes, 658)));
        var library = ScalarFrontendHost.BindLibrary(source, bytes, (BoundRuntimePolicy)policy);
        var call = new BoundCall(0, [], PrimitiveType.Int64, default);
        var matches = new BoundBinary(TokenKind.EqualsEquals, call,
            new BoundConstant(42L, PrimitiveType.Int64, default), PrimitiveType.Bool, default);
        var main = new BoundFunction("Main", PrimitiveType.Int32, [], 0,
            new BoundBlock([new BoundIf(matches,
                new BoundBlock([new BoundReturn(new BoundConstant(42, PrimitiveType.Int32,
                    default), default)], default),
                new BoundBlock([new BoundReturn(new BoundConstant(0, PrimitiveType.Int32,
                    default), default)], default), default)], default), default);
        var executable = library with { Functions = [.. library.Functions, main], EntryPoint = 1 };
        SemanticVerifier.Verify(executable);
        Assert.That(NativeJit.Execute(executable, policy), Is.EqualTo(42));
        await ExecuteAotAsync(executable, policy).ConfigureAwait(true);
    }
    [TestCase(NativeRuntimePolicy.FailFast,"bool Value(int32 x, int32 y) { return !(x < y); }",8,2067,2,1)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions,"bool Value(int32 x, int32 y) { return !(x < y); }",8,2067,2,1)]
    [TestCase(NativeRuntimePolicy.FailFast,"bool Value(int32 x, int32 y) { return !(!(x < y)); }",15,18067,1,2)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions,"bool Value(int32 x, int32 y) { return !(!(x < y)); }",15,18067,1,2)]
    public async Task NativeScalarFrontendNegatedComparisonExecutesAsync(
        NativeRuntimePolicy policy, string text, int version, int operation,
        int first, int second)
    {
        var source = SourceText.From(text);
        var stage = await CompileAsync(text,
            "using Terrum.CodeAnalysis; int32 Main() { require TryPublishScalarFrontend(); return 42; }",
            (BoundRuntimePolicy)policy, "ScalarFrontendProjection").ConfigureAwait(true);
        var native = NativeJit.ExecuteWithData(stage, policy);
        Assert.That(native.ExitCode, Is.EqualTo(42));
        var bytes = native.Data.AsSpan(native.BufferOffsets[ScalarFrontendHost.BufferIndex],
            ScalarFrontendHost.WordCount * sizeof(int)).ToArray();
        Assert.That(BinaryPrimitives.ReadInt32LittleEndian(bytes), Is.EqualTo(87));
        var expressionWords = ScalarFrontendExpressionWordOffsets(bytes);
        var actualOperation = ExpectedScalarFrontendOperation(source, bytes, operation);
        Assert.That(ScalarFrontendHost.ReadResult(source, bytes).WhenTrue.Operation, Is.EqualTo(ExpectedScalarFrontendOperation(source, bytes, operation)));
        BinaryPrimitives.WriteInt32LittleEndian(bytes, version - 1);
        Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(source, bytes));
        BinaryPrimitives.WriteInt32LittleEndian(bytes, 87);
        var operationOffset = expressionWords.Operation * sizeof(int);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(operationOffset), actualOperation + 1);
        Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(source, bytes));
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(operationOffset), actualOperation);
        if (version == 15)
        {
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(operationOffset), 4067);
            BinaryPrimitives.WriteInt32LittleEndian(bytes, 87);
            Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(source, bytes));
            BinaryPrimitives.WriteInt32LittleEndian(bytes, 87);
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(operationOffset), actualOperation);
        }
        Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(
            SourceText.From(text.Replace("x < y", "y < x", StringComparison.Ordinal)), bytes));
        var library = ScalarFrontendHost.BindLibrary(source, bytes, (BoundRuntimePolicy)policy);
        var call = new BoundCall(0,
            [new BoundConstant(first, PrimitiveType.Int32, default),
                new BoundConstant(second, PrimitiveType.Int32, default)],
            PrimitiveType.Bool, default);
        var main = new BoundFunction("Main", PrimitiveType.Int32, [], 0,
            new BoundBlock([new BoundIf(call,
                new BoundBlock([new BoundReturn(new BoundConstant(42, PrimitiveType.Int32,
                    default), default)], default),
                new BoundBlock([new BoundReturn(new BoundConstant(0, PrimitiveType.Int32,
                    default), default)], default), default)], default), default);
        var executable = library with { Functions = [.. library.Functions, main], EntryPoint = 1 };
        SemanticVerifier.Verify(executable);
        Assert.That(NativeJit.Execute(executable, policy), Is.EqualTo(42));
        await ExecuteAotAsync(executable, policy).ConfigureAwait(true);
    }
    [TestCase(NativeRuntimePolicy.FailFast,"bool Value(bool x) { return !(x && true); }",2772,true)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions,"bool Value(bool x) { return !((x) && (true)); }",2772,true)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions,"bool Value(bool x) { return !(false || x); }",2974,true)]
    [TestCase(NativeRuntimePolicy.FailFast,"bool Value() { return !(true == false); }",3165,false)]
    public async Task NativeScalarFrontendNegatedBooleanLiteralExecutesAsync(
        NativeRuntimePolicy policy, string text, int operation, bool hasParameter)
    {
        var source = SourceText.From(text);
        var stage = await CompileAsync(text,
            "using Terrum.CodeAnalysis; int32 Main() { require TryPublishScalarFrontend(); return 42; }",
            (BoundRuntimePolicy)policy, "ScalarFrontendProjection").ConfigureAwait(true);
        var native = NativeJit.ExecuteWithData(stage, policy);
        Assert.That(native.ExitCode, Is.EqualTo(42));
        var bytes = native.Data.AsSpan(native.BufferOffsets[ScalarFrontendHost.BufferIndex],
            ScalarFrontendHost.WordCount * sizeof(int)).ToArray();
        Assert.That(BinaryPrimitives.ReadInt32LittleEndian(bytes), Is.EqualTo(87));
        Assert.That(ScalarFrontendHost.ReadResult(source, bytes).WhenTrue.Operation, Is.EqualTo(ExpectedScalarFrontendOperation(source, bytes, operation)));
        BinaryPrimitives.WriteInt32LittleEndian(bytes, 8);
        Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(source, bytes));
        BinaryPrimitives.WriteInt32LittleEndian(bytes, 87);
        var expressionWords = ScalarFrontendExpressionWordOffsets(bytes);
        var operationOffset = expressionWords.Operation * sizeof(int);
        var originalOperation = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(operationOffset));
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(operationOffset), originalOperation + 1);
        Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(source, bytes));
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(operationOffset), originalOperation);
        foreach (var word in new[] { 24, 25 })
        {
            var offset = word * sizeof(int);
            var original = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset));
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset), original ^ 1);
            Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(source, bytes));
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset), original);
        }
        var library = ScalarFrontendHost.BindLibrary(source, bytes, (BoundRuntimePolicy)policy);
        var call = new BoundCall(0, hasParameter
            ? [new BoundConstant(0, PrimitiveType.Bool, default)] : [],
            PrimitiveType.Bool, default);
        var main = new BoundFunction("Main", PrimitiveType.Int32, [], 0,
            new BoundBlock([new BoundIf(call,
                new BoundBlock([new BoundReturn(new BoundConstant(42, PrimitiveType.Int32,
                    default), default)], default),
                new BoundBlock([new BoundReturn(new BoundConstant(0, PrimitiveType.Int32,
                    default), default)], default), default)], default), default);
        var executable = library with { Functions = [.. library.Functions, main], EntryPoint = 1 };
        SemanticVerifier.Verify(executable);
        Assert.That(NativeJit.Execute(executable, policy), Is.EqualTo(42));
        await ExecuteAotAsync(executable, policy).ConfigureAwait(true);
    }
    [TestCase(NativeRuntimePolicy.FailFast,"bool Value(int32 x) { return !(x < 42); }",2167,"int32",42L)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions,"bool Value(int32 x) { return !(42 >= x); }",2370,"int32",43L)]
    [TestCase(NativeRuntimePolicy.FailFast,"bool Value(int32 x) { return !(x >= -1); }",3370,"int32",-2L)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions,"bool Value(int32 x) { return !(-42 < x); }",3567,"int32",-43L)]
    [TestCase(NativeRuntimePolicy.FailFast,"bool Value(int32 x) { return !(+42 <= x); }",3968,"int32",41L)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions,"bool Value(uint64 x) { return !(x != 0xFFFFFFFFFFFFFFFF); }",2166,"uint64",-1L)]
    public async Task NativeScalarFrontendNegatedNumericLiteralExecutesAsync(
        NativeRuntimePolicy policy, string text, int operation, string typeName, long value)
    {
        var source = SourceText.From(text);
        var stage = await CompileAsync(text,
            "using Terrum.CodeAnalysis; int32 Main() { require TryPublishScalarFrontend(); return 42; }",
            (BoundRuntimePolicy)policy, "ScalarFrontendProjection").ConfigureAwait(true);
        var native = NativeJit.ExecuteWithData(stage, policy);
        Assert.That(native.ExitCode, Is.EqualTo(42));
        var bytes = native.Data.AsSpan(native.BufferOffsets[ScalarFrontendHost.BufferIndex],
            ScalarFrontendHost.WordCount * sizeof(int)).ToArray();
        Assert.That(BinaryPrimitives.ReadInt32LittleEndian(bytes), Is.EqualTo(87));
        Assert.That(ScalarFrontendHost.ReadResult(source, bytes).WhenTrue.Operation, Is.EqualTo(ExpectedScalarFrontendOperation(source, bytes, operation)));
        BinaryPrimitives.WriteInt32LittleEndian(bytes, 9);
        Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(source, bytes));
        BinaryPrimitives.WriteInt32LittleEndian(bytes, 87);
        var offset = 24 * sizeof(int);
        var original = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset));
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset), original ^ 1);
        Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(source, bytes));
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset), original);
        var library = ScalarFrontendHost.BindLibrary(source, bytes, (BoundRuntimePolicy)policy);
        var parameterType = typeName == "uint64" ? PrimitiveType.UInt64 : PrimitiveType.Int32;
        var call = new BoundCall(0, [new BoundConstant(value, parameterType, default)],
            PrimitiveType.Bool, default);
        var main = new BoundFunction("Main", PrimitiveType.Int32, [], 0,
            new BoundBlock([new BoundIf(call,
                new BoundBlock([new BoundReturn(new BoundConstant(42, PrimitiveType.Int32,
                    default), default)], default),
                new BoundBlock([new BoundReturn(new BoundConstant(0, PrimitiveType.Int32,
                    default), default)], default), default)], default), default);
        var executable = library with { Functions = [.. library.Functions, main], EntryPoint = 1 };
        SemanticVerifier.Verify(executable);
        Assert.That(NativeJit.Execute(executable, policy), Is.EqualTo(42));
        await ExecuteAotAsync(executable, policy).ConfigureAwait(true);
    }
    [TestCase(NativeRuntimePolicy.FailFast,"&&",true,true,true)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions,"&&",true,false,false)]
    [TestCase(NativeRuntimePolicy.FailFast,"||",false,true,true)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions,"||",false,false,false)]
    [TestCase(NativeRuntimePolicy.FailFast,"&",true,false,false)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions,"&",true,false,false)]
    [TestCase(NativeRuntimePolicy.FailFast,"|",false,true,true)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions,"|",false,true,true)]
    [TestCase(NativeRuntimePolicy.FailFast,"^",true,false,true)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions,"^",true,true,false)]
    [TestCase(NativeRuntimePolicy.FailFast,"==",true,true,true)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions,"!=",true,false,true)]
    public async Task NativeScalarFrontendBooleanPairExecutesAsync(
        NativeRuntimePolicy policy, string op, bool first, bool second, bool expected)
    {
        var text = $"bool Value(bool x, bool y) {{ return x {op} y; }}";
        var source = SourceText.From(text);
        var stage = await CompileAsync(text,
            "using Terrum.CodeAnalysis; int32 Main() { require TryPublishScalarFrontend(); return 42; }",
            (BoundRuntimePolicy)policy, "ScalarFrontendProjection").ConfigureAwait(true);
        var native = NativeJit.ExecuteWithData(stage, policy);
        Assert.That(native.ExitCode, Is.EqualTo(42));
        var bytes = native.Data.AsSpan(native.BufferOffsets[ScalarFrontendHost.BufferIndex],
            ScalarFrontendHost.WordCount * sizeof(int)).ToArray();
        var result = ScalarFrontendHost.ReadResult(source, bytes);
        Assert.That(result.WhenTrue.Type, Is.EqualTo(5));
        foreach (var word in new[] { 22, 23, 24, 25 })
        {
            var offset = word * sizeof(int);
            var original = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset));
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset), original ^ 1);
            Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(source, bytes));
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset), original);
        }
        var library = ScalarFrontendHost.BindLibrary(source, bytes, (BoundRuntimePolicy)policy);
        var call = new BoundCall(0, [
            new BoundConstant(first ? 1 : 0, PrimitiveType.Bool, default),
            new BoundConstant(second ? 1 : 0, PrimitiveType.Bool, default),
        ], PrimitiveType.Bool, default);
        var branch = new BoundIf(call,
            new BoundBlock([new BoundReturn(new BoundConstant(expected ? 42 : 0,
                PrimitiveType.Int32, default), default)], default),
            new BoundBlock([new BoundReturn(new BoundConstant(expected ? 0 : 42,
                PrimitiveType.Int32, default), default)], default), default);
        var main = new BoundFunction("Main", PrimitiveType.Int32, [], 0,
            new BoundBlock([branch], default), default);
        var executable = library with { Functions = [.. library.Functions, main], EntryPoint = 1 };
        SemanticVerifier.Verify(executable);
        Assert.That(NativeJit.Execute(executable, policy), Is.EqualTo(42));
        await ExecuteAotAsync(executable, policy).ConfigureAwait(true);
    }
    [TestCase(NativeRuntimePolicy.FailFast,"x && true",true,true,772)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions,"false && x",true,false,972)]
    [TestCase(NativeRuntimePolicy.FailFast,"x || false",false,false,774)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions,"true || x",false,true,974)]
    [TestCase(NativeRuntimePolicy.FailFast,"x & false",true,false,771)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions,"true | x",false,true,973)]
    [TestCase(NativeRuntimePolicy.FailFast,"x ^ true",true,false,775)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions,"false ^ x",true,true,975)]
    [TestCase(NativeRuntimePolicy.FailFast,"x == true",true,true,765)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions,"false != x",true,true,966)]
    [TestCase(NativeRuntimePolicy.FailFast,"(x) && ((true))",true,true,772)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions,"((false)) != (x)",true,true,966)]
    public async Task NativeScalarFrontendBooleanLiteralPairExecutesAsync(
        NativeRuntimePolicy policy, string expression, bool input, bool expected, int operation)
    {
        var text = $"bool Value(bool x) {{ return {expression}; }}";
        var source = SourceText.From(text);
        var stage = await CompileAsync(text,
            "using Terrum.CodeAnalysis; int32 Main() { require TryPublishScalarFrontend(); return 42; }",
            (BoundRuntimePolicy)policy, "ScalarFrontendProjection").ConfigureAwait(true);
        var native = NativeJit.ExecuteWithData(stage, policy);
        Assert.That(native.ExitCode, Is.EqualTo(42));
        var bytes = native.Data.AsSpan(native.BufferOffsets[ScalarFrontendHost.BufferIndex],
            ScalarFrontendHost.WordCount * sizeof(int)).ToArray();
        Assert.That(ScalarFrontendHost.ReadResult(source, bytes).WhenTrue.Operation, Is.EqualTo(ExpectedScalarFrontendOperation(source, bytes, operation)));
        foreach (var (word, value) in new[] { (0, 2), (24, 2), (25, 22) })
        {
            var offset = word * sizeof(int);
            var original = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset));
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset), value);
            Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(source, bytes));
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset), original);
        }
        var library = ScalarFrontendHost.BindLibrary(source, bytes, (BoundRuntimePolicy)policy);
        var call = new BoundCall(0,
            [new BoundConstant(input ? 1 : 0, PrimitiveType.Bool, default)],
            PrimitiveType.Bool, default);
        var matches = new BoundBinary(TokenKind.EqualsEquals, call,
            new BoundConstant(expected ? 1 : 0, PrimitiveType.Bool, default),
            PrimitiveType.Bool, default);
        var main = new BoundFunction("Main", PrimitiveType.Int32, [], 0,
            new BoundBlock([new BoundIf(matches,
                new BoundBlock([new BoundReturn(new BoundConstant(42, PrimitiveType.Int32,
                    default), default)], default),
                new BoundBlock([new BoundReturn(new BoundConstant(0, PrimitiveType.Int32,
                    default), default)], default), default)], default), default);
        var executable = library with { Functions = [.. library.Functions, main], EntryPoint = 1 };
        SemanticVerifier.Verify(executable);
        Assert.That(NativeJit.Execute(executable, policy), Is.EqualTo(42));
        await ExecuteAotAsync(executable, policy).ConfigureAwait(true);
    }
    [TestCase("true && false",1172)]
    [TestCase("true == false",1165)]
    [TestCase("(true) == ((false))",1165)]
    public async Task ScalarFrontendBooleanLiteralPairRejectsForgedWordsAsync(
        string expression, int operation)
    {
        var text = $"bool Value() {{ return {expression}; }}";
        var source = SourceText.From(text);
        var stage = await CompileAsync(text,
            "using Terrum.CodeAnalysis; int32 Main() { require TryPublishScalarFrontend(); return 42; }",
            BoundRuntimePolicy.FailFast, "ScalarFrontendProjection").ConfigureAwait(true);
        var native = NativeJit.ExecuteWithData(stage, NativeRuntimePolicy.FailFast);
        Assert.That(native.ExitCode, Is.EqualTo(42));
        var bytes = native.Data.AsSpan(native.BufferOffsets[ScalarFrontendHost.BufferIndex],
            ScalarFrontendHost.WordCount * sizeof(int)).ToArray();
        Assert.That(ScalarFrontendHost.ReadResult(source, bytes).WhenTrue.Operation, Is.EqualTo(ExpectedScalarFrontendOperation(source, bytes, operation)));
        foreach (var (word, value) in new[] { (0, 2), (23, operation == 1172 ? 1174 : 1166),
            (24, 22), (25, 22) })
        {
            var offset = word * sizeof(int);
            var original = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset));
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset), value);
            Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(source, bytes));
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset), original);
        }
        await ExecuteAotAsync(stage, NativeRuntimePolicy.FailFast).ConfigureAwait(true);
    }
    [TestCase(NativeRuntimePolicy.FailFast,"-x",-42,58)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions,"-x",-42,58)]
    [TestCase(NativeRuntimePolicy.FailFast,"+x",42,57)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions,"+x",42,57)]
    [TestCase(NativeRuntimePolicy.FailFast,"-(x)",-42,58)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions,"+((x))",42,57)]
    public async Task NativeScalarFrontendNamedUnaryIntegerExecutesAsync(
        NativeRuntimePolicy policy, string expression, int input, int operation)
    {
        var text = $"int32 Value(int32 x) {{ return {expression}; }}";
        var source = SourceText.From(text);
        var stage = await CompileAsync(text,
            "using Terrum.CodeAnalysis; int32 Main() { require TryPublishScalarFrontend(); return 42; }",
            (BoundRuntimePolicy)policy, "ScalarFrontendProjection").ConfigureAwait(true);
        var native = NativeJit.ExecuteWithData(stage, policy);
        Assert.That(native.ExitCode, Is.EqualTo(42));
        var bytes = native.Data.AsSpan(native.BufferOffsets[ScalarFrontendHost.BufferIndex],
            ScalarFrontendHost.WordCount * sizeof(int)).ToArray();
        Assert.That(ScalarFrontendHost.ReadResult(source, bytes).WhenTrue.Operation, Is.EqualTo(ExpectedScalarFrontendOperation(source, bytes, operation)));
        foreach (var word in new[] { 22, 23, 24, 25 })
        {
            var offset = word * sizeof(int);
            var original = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset));
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset), original ^ 1);
            Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(source, bytes));
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset), original);
        }
        var library = ScalarFrontendHost.BindLibrary(source, bytes, (BoundRuntimePolicy)policy);
        var call = new BoundCall(0, [new BoundConstant(input, PrimitiveType.Int32, default)],
            PrimitiveType.Int32, default);
        var main = new BoundFunction("Main", PrimitiveType.Int32, [], 0,
            new BoundBlock([new BoundReturn(call, default)], default), default);
        var executable = library with { Functions = [.. library.Functions, main], EntryPoint = 1 };
        SemanticVerifier.Verify(executable);
        Assert.That(NativeJit.Execute(executable, policy), Is.EqualTo(42));
        await ExecuteAotAsync(executable, policy).ConfigureAwait(true);
    }
    [TestCase(NativeRuntimePolicy.FailFast,"int32 Value() { return 20 + 22; }",557)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions,"int32 Value() { return 20 + 22; }",557)]
    [TestCase(NativeRuntimePolicy.FailFast,"int32 Value() { return 0x2A & 0x3F; }",571)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions,"int32 Value() { return 0x2A & 0x3F; }",571)]
    [TestCase(NativeRuntimePolicy.FailFast,"int32 Value() { return (20) + ((22)); }",557)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions,"int32 Value() { return (20) + ((22)); }",557)]
    public async Task NativeScalarFrontendIntegerLiteralPairExecutesAsync(
        NativeRuntimePolicy policy, string text, int operation)
    {
        var source = SourceText.From(text);
        var stage = await CompileAsync(text,
            "using Terrum.CodeAnalysis; int32 Main() { require TryPublishScalarFrontend(); return 42; }",
            (BoundRuntimePolicy)policy, "ScalarFrontendProjection").ConfigureAwait(true);
        var native = NativeJit.ExecuteWithData(stage, policy);
        Assert.That(native.ExitCode, Is.EqualTo(42));
        var bytes = native.Data.AsSpan(native.BufferOffsets[ScalarFrontendHost.BufferIndex],
            ScalarFrontendHost.WordCount * sizeof(int)).ToArray();
        Assert.That(ScalarFrontendHost.ReadResult(source, bytes).WhenTrue.Operation, Is.EqualTo(ExpectedScalarFrontendOperation(source, bytes, operation)));
        foreach (var word in new[] { 22, 23, 24, 25 })
        {
            var offset = word * sizeof(int);
            var original = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset));
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset), original ^ 1);
            Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(source, bytes));
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset), original);
        }
        var library = ScalarFrontendHost.BindLibrary(source, bytes, (BoundRuntimePolicy)policy);
        var call = new BoundCall(0, [], PrimitiveType.Int32, default);
        var main = new BoundFunction("Main", PrimitiveType.Int32, [], 0,
            new BoundBlock([new BoundReturn(call, default)], default), default);
        var executable = library with { Functions = [.. library.Functions, main], EntryPoint = 1 };
        SemanticVerifier.Verify(executable);
        Assert.That(NativeJit.Execute(executable, policy), Is.EqualTo(42));
        await ExecuteAotAsync(executable, policy).ConfigureAwait(true);
    }
    [TestCase(NativeRuntimePolicy.FailFast,true)]
    [TestCase(NativeRuntimePolicy.FailFast,false)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions,true)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions,false)]
    public async Task NativeScalarFrontendTrailingReturnExecutesAsync(
        NativeRuntimePolicy policy, bool input)
    {
        const string Text = "int32 Value(bool x) { if (x) { return 42; } return 0; }";
        var source = SourceText.From(Text);
        var stage = await CompileAsync(Text,
            "using Terrum.CodeAnalysis; int32 Main() { require TryPublishScalarFrontend(); return 42; }",
            (BoundRuntimePolicy)policy, "ScalarFrontendProjection").ConfigureAwait(true);
        var native = NativeJit.ExecuteWithData(stage, policy);
        Assert.That(native.ExitCode, Is.EqualTo(42));
        var bytes = native.Data.AsSpan(native.BufferOffsets[ScalarFrontendHost.BufferIndex],
            ScalarFrontendHost.WordCount * sizeof(int)).ToArray();
        var result = ScalarFrontendHost.ReadResult(source, bytes);
        Assert.That(result.HasCondition, Is.True);
        Assert.That(result.HasFallthrough, Is.True);
        Assert.That(BinaryPrimitives.ReadInt32LittleEndian(bytes), Is.EqualTo(87));
        foreach (var (word, value) in new[] { (0, 3), (15, 0), (15, 1) })
        {
            var offset = word * sizeof(int);
            var original = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset));
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset), value);
            Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(source, bytes));
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset), original);
        }
        BinaryPrimitives.WriteInt32LittleEndian(bytes, 3);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(15 * sizeof(int)), 1);
        Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(source, bytes));
        BinaryPrimitives.WriteInt32LittleEndian(bytes, 87);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(15 * sizeof(int)), 2);
        var library = ScalarFrontendHost.BindLibrary(source, bytes, (BoundRuntimePolicy)policy);
        var call = new BoundCall(0, [new BoundConstant(input ? 1 : 0, PrimitiveType.Bool, default)],
            PrimitiveType.Int32, default);
        var matches = new BoundBinary(TokenKind.EqualsEquals, call,
            new BoundConstant(input ? 42 : 0, PrimitiveType.Int32, default),
            PrimitiveType.Bool, default);
        var branch = new BoundIf(matches,
            new BoundBlock([new BoundReturn(new BoundConstant(42, PrimitiveType.Int32, default),
                default)], default),
            new BoundBlock([new BoundReturn(new BoundConstant(0, PrimitiveType.Int32, default),
                default)], default), default);
        var main = new BoundFunction("Main", PrimitiveType.Int32, [], 0,
            new BoundBlock([branch], default), default);
        var executable = library with { Functions = [.. library.Functions, main], EntryPoint = 1 };
        SemanticVerifier.Verify(executable);
        Assert.That(NativeJit.Execute(executable, policy), Is.EqualTo(42));
        await ExecuteAotAsync(executable, policy).ConfigureAwait(true);
    }
    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public async Task NativeScalarStandaloneBlocksPublishAndExecuteAsync(NativeRuntimePolicy policy)
    {
        const string text = "int32 Value() { { mut int32 x = 1; x++; } { int32 x = 42; return x; } }";
        var source = SourceText.From(text);
        var stage = await CompileAsync(text,
            "using Terrum.CodeAnalysis; int32 Main() { require TryPublishScalarFrontend(); return 42; }",
            (BoundRuntimePolicy)policy, "ScalarFrontendProjection").ConfigureAwait(true);
        var native = NativeJit.ExecuteWithData(stage, policy);
        Assert.That(native.ExitCode, Is.EqualTo(42));
        var bytes = native.Data.AsSpan(native.BufferOffsets[ScalarFrontendHost.BufferIndex],
            ScalarFrontendHost.WordCount * sizeof(int)).ToArray();
        Assert.That(BinaryPrimitives.ReadInt32LittleEndian(bytes), Is.EqualTo(87));
        var result = ScalarFrontendHost.ReadResult(source, bytes);
        Assert.That(result.Locals.Length, Is.EqualTo(2));
        Assert.That(result.Statements.Length, Is.EqualTo(6));
        Assert.That(result.Statements[0].Kind, Is.EqualTo(14));
        Assert.That(result.Statements[3].Kind, Is.EqualTo(14));
        await ExecuteAotAsync(stage, policy).ConfigureAwait(true);
        var library = ScalarFrontendHost.BindLibrary(source, bytes, (BoundRuntimePolicy)policy);
        var call = new BoundCall(0, [], PrimitiveType.Int32, default);
        var main = new BoundFunction("Main", PrimitiveType.Int32, [], 0,
            new BoundBlock([new BoundReturn(call, default)], default), default);
        var executable = library with { Functions = [.. library.Functions, main], EntryPoint = 1 };
        SemanticVerifier.Verify(executable);
        Assert.That(NativeJit.Execute(executable, policy), Is.EqualTo(42));
        await ExecuteAotAsync(executable, policy).ConfigureAwait(true);
    }
    [TestCase(NativeRuntimePolicy.FailFast,"int32 Value() { { int32 x = 1; } return x; }",3,"x;")]
    [TestCase(NativeRuntimePolicy.CheckedExceptions,"int32 Value() { { int32 x = 1; } return x; }",3,"x;")]
    [TestCase(NativeRuntimePolicy.FailFast,"int32 Value() { int32 x = 1; { int32 x = 2; } return x; }",4,"x = 2")]
    [TestCase(NativeRuntimePolicy.CheckedExceptions,"int32 Value() { int32 x = 1; { int32 x = 2; } return x; }",4,"x = 2")]
    [TestCase(NativeRuntimePolicy.FailFast,"int32 Value() { { var x = x; } return 42; }",3,"x;")]
    [TestCase(NativeRuntimePolicy.CheckedExceptions,"int32 Value() { { var x = x; } return 42; }",3,"x;")]
    public async Task NativeScalarStandaloneBlocksRejectInvalidNamesAtTheirRawSpanAsync(
        NativeRuntimePolicy policy, string text, int code, string marker)
    {
        var stage = await CompileAsync(text,
            "using Terrum.CodeAnalysis; int32 Main() { require TryPublishScalarFrontend(); return 42; }",
            (BoundRuntimePolicy)policy, "ScalarFrontendProjection").ConfigureAwait(true);
        var native = NativeJit.ExecuteWithData(stage, policy);
        Assert.That(native.ExitCode, Is.EqualTo(42));
        var bytes = native.Data.AsSpan(native.BufferOffsets[ScalarFrontendHost.BufferIndex],
            ScalarFrontendHost.WordCount * sizeof(int)).ToArray();
        var result = ScalarFrontendHost.ReadResult(SourceText.From(text), bytes);
        Assert.That(result.IsValid, Is.False);
        Assert.That(result.ErrorCode, Is.EqualTo(code));
        Assert.That(result.ErrorSpan, Is.EqualTo(new TextSpan(text.IndexOf(marker, StringComparison.Ordinal), 1)));
        {var __nunitItems0 = bytes.AsSpan(37 * sizeof(int)).ToArray();        Assert.Multiple(() => { foreach (var __nunitItem28 in __nunitItems0) { Assert.That(__nunitItem28, Is.EqualTo(0)); } });}
        await ExecuteAotAsync(stage, policy).ConfigureAwait(true);
    }

    private static Task<BoundProgram> CompilePairParserAsync(string text, string entry, BoundRuntimePolicy policy) =>
        CompileAsync(text, entry, policy, "ScalarParameterPairParser");

    private static int ExpectedScalarFrontendOperation(SourceText source, byte[] bytes,
        int compactOperation)
    {
        var offsets = ScalarFrontendExpressionWordOffsets(bytes);
        return offsets.IsTree ? TreeRootOperation(source) : compactOperation;
    }

    private static (bool IsTree, int Type, int Operation, int Left, int Right)
        ScalarFrontendExpressionWordOffsets(byte[] bytes)
    {
        var nodeCount = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(37 * sizeof(int)));
        if (nodeCount == 0)
        {
            return (false, 22, 23, 24, 25);
        }
        if (nodeCount is < 1 or > 64)
        {
            throw new InvalidOperationException("The scalar frontend tree node count is invalid.");
        }

        var root = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(39 * sizeof(int)));
        if (root < 0 || root >= nodeCount)
        {
            throw new InvalidOperationException("The scalar frontend true-expression root is invalid.");
        }

        var offset = root < 32 ? 42 + root * 8 : 392 + (root - 32) * 8;
        return (true, offset + 1, offset, offset + 4, offset + 5);
    }

    private static int TreeRootOperation(SourceText source)
    {
        var tokens = Lexer.Lex(source).Tokens;
        var returnIndex = 0;
        while (returnIndex < tokens.Length && tokens[returnIndex].Kind != TokenKind.ReturnKeyword)
        {
            returnIndex++;
        }
        if (returnIndex + 1 >= tokens.Length)
        {
            throw new InvalidOperationException("The scalar frontend test source has no return expression.");
        }

        var start = returnIndex + 1;
        var end = start;
        var depth = 0;
        while (end < tokens.Length)
        {
            var kind = tokens[end].Kind;
            if (kind == TokenKind.OpenParen)
            {
                depth++;
            }
            else if (kind == TokenKind.CloseParen)
            {
                depth--;
            }
            else if (kind == TokenKind.Semicolon && depth == 0)
            {
                break;
            }
            end++;
        }
        if (end == tokens.Length)
        {
            throw new InvalidOperationException("The scalar frontend test return expression is unterminated.");
        }

        while (start < end && tokens[start].Kind == TokenKind.OpenParen
            && tokens[end - 1].Kind == TokenKind.CloseParen)
        {
            var enclosingDepth = 0;
            var enclosesExpression = true;
            for (var index = start; index < end; index++)
            {
                if (tokens[index].Kind == TokenKind.OpenParen)
                {
                    enclosingDepth++;
                }
                else if (tokens[index].Kind == TokenKind.CloseParen)
                {
                    enclosingDepth--;
                }
                if (enclosingDepth == 0 && index < end - 1)
                {
                    enclosesExpression = false;
                    break;
                }
            }
            if (!enclosesExpression)
            {
                break;
            }
            start++;
            end--;
        }

        var first = tokens[start].Kind;
        if (first is TokenKind.Bang or TokenKind.Minus or TokenKind.Plus or TokenKind.Tilde)
        {
            return (int)first;
        }

        depth = 0;
        for (var index = start; index < end; index++)
        {
            var kind = tokens[index].Kind;
            if (kind == TokenKind.OpenParen)
            {
                depth++;
            }
            else if (kind == TokenKind.CloseParen)
            {
                depth--;
            }
            else if (depth == 0 && IsBinaryScalarOperator(kind))
            {
                return (int)kind;
            }
        }
        return first switch {
            TokenKind.Identifier => 0,
            TokenKind.NumericLiteral => 3,
            TokenKind.TrueKeyword or TokenKind.FalseKeyword => (int)first,
            _ => throw new InvalidOperationException("The scalar frontend tree root has no operator."),
        };

        static bool IsBinaryScalarOperator(TokenKind kind) => kind is
            TokenKind.Plus or TokenKind.Minus or TokenKind.Star or TokenKind.Slash or TokenKind.Percent
            or TokenKind.EqualsEquals or TokenKind.BangEquals or TokenKind.Less or TokenKind.LessEquals
            or TokenKind.Greater or TokenKind.GreaterEquals or TokenKind.Ampersand
            or TokenKind.AmpersandAmpersand or TokenKind.Pipe or TokenKind.PipePipe or TokenKind.Caret
            or TokenKind.ShiftLeft or TokenKind.ShiftRight or TokenKind.ShiftRightUnsigned;
    }

    private static async Task<BoundProgram> CompileAsync(string text, string entry,
        BoundRuntimePolicy policy, string? parserName = null, bool includeSourceLines = false)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
        string[] names = [
            Path.Combine("Text", "SourceRange"),
            Path.Combine("Lexing", "CharacterFacts"),
            Path.Combine("Text", "Utf16CodeUnits"),
            Path.Combine("Text", "Utf16Scalar"),
            Path.Combine("Lexing", "CharacterLiteral"),
            Path.Combine("Lexing", "StringEscape"),
            Path.Combine("Lexing", "Token"),
            Path.Combine("Lexing", "ManagedTokenChain"),
            Path.Combine("Lexing", "Scanner"),
            Path.Combine("Lexing", "ManagedScanner"),
            Path.Combine("Lexing", "TokenSnapshot"),
            Path.Combine("Lexing", "TokenTriviaSnapshot"),
            Path.Combine("Utilities", "Numerics", "WideIntegerFacts"),
            Path.Combine("Utilities", "Numerics", "IntegerParsing"),
            Path.Combine("Lexing", "TokenIntegerSnapshot"),
            Path.Combine("Lexing", "TokenDiagnosticSnapshot"),
        ];
        if (parserName is not null)
        {
            names = parserName == "ScalarFrontendProjection"
                ? [.. names,
                    Path.Combine("Lexing", "HexDigitValue"),
                    Path.Combine("Lexing", "BasedIntegerFloatLiteral"),
                    Path.Combine("Lexing", "HexFloatLiteral"),
                    Path.Combine("Lexing", "DecimalFloatLiteral"),
                    Path.Combine("Utilities", "Numerics", "IntegerFloatConversion"),
                    Path.Combine("Utilities", "Numerics", "Char16FloatConversion"),
                    Path.Combine("Syntax", "ScalarParserTypes"),
                    Path.Combine("Semantics", "ScalarFrontend"),
                    Path.Combine("Lexing", "TokenCursor"),
                    Path.Combine("Semantics", "ScalarFrontendProgram"),
                    Path.Combine("Semantics", parserName),
                    Path.Combine("Semantics", "ScalarFrontendProgramReader")]
                : parserName == "ScalarFrontend"
                    ? [.. names,
                        Path.Combine("Lexing", "HexDigitValue"),
                        Path.Combine("Lexing", "BasedIntegerFloatLiteral"),
                        Path.Combine("Lexing", "HexFloatLiteral"),
                        Path.Combine("Lexing", "DecimalFloatLiteral"),
                        Path.Combine("Utilities", "Numerics", "IntegerFloatConversion"),
                        Path.Combine("Utilities", "Numerics", "Char16FloatConversion"),
                        Path.Combine("Syntax", "ScalarParserTypes"),
                        Path.Combine("Semantics", parserName),
                        Path.Combine("Lexing", "TokenCursor"),
                        Path.Combine("Semantics", "ScalarFrontendProgram"),
                        Path.Combine("Semantics", "ScalarFrontendProjection")]
                    : [.. names, Path.Combine("Syntax", "ScalarParserTypes"), Path.Combine("Syntax", parserName)];
        }
        if (includeSourceLines)
        {
            names = [.. names,
                Path.Combine("Text", "SourceText"),
                Path.Combine("Text", "SourcePosition"),
                Path.Combine("Text", "SourceLineIndex"),
                Path.Combine("Lexing", "TokenDiagnosticLocationSnapshot")];
        }
        var trees = new List<SyntaxTree>(names.Length + 1);
        foreach (var name in names)
        {
            var path = TerrumProjectPaths.SourceFile(root, name + ".terrum");
            trees.Add(SyntaxTree.Parse(await File.ReadAllTextAsync(path).ConfigureAwait(true), path));
        }
        trees.Add(SyntaxTree.Parse(entry, "managed-scanner-entry.terrum"));
        var buffers = parserName is not null
            ? ScalarFrontendHost.CreateBuffers(SourceText.From(text))
            : ScannerHost.CreateBuffers(SourceText.From(text));
        var compilation = Compilation.Create(trees, buffers, policy);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        return ((global::System.Func<BoundProgram>)(() => { var __nunitActual29 = compilation.Program; Assert.That(__nunitActual29, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual29!; }))();
    }

    private static async Task ExecuteAotAsync(BoundProgram program, NativeRuntimePolicy policy)
    {
        var directory = TestArtifactPaths.CreateDirectory("scanner-tests");
        var path = Path.Combine(directory, $"terrum-managed-scanner-{Guid.NewGuid():N}.exe");
        try
        {
            await File.WriteAllBytesAsync(path, NativeEmitter.Emit(program, policy)).ConfigureAwait(true);
            using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) {
                UseShellExecute = false,
                CreateNoWindow = true,
            }) ?? throw new InvalidOperationException("Could not start compiled managed scanner.");
            try
            {
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(true);
            }
            catch (TimeoutException)
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync().ConfigureAwait(true);
                }

                throw;
            }
            Assert.That(process.ExitCode, Is.EqualTo(42));
        }
        finally
        {
            await TestFileCleanup.DeleteWithRetryAsync(path).ConfigureAwait(true);
        }
    }
}
