// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Globalization;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Lexing;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Terrum.CodeAnalysis.Text;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests;

public sealed class ScannerTests
{
    [TestCase(PrimitiveType.Int64)]
    [TestCase(PrimitiveType.UInt64)]
    public void WideValuesDoNotImplyWideHostBufferStorage(PrimitiveType type)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("int32 Main() { return 0; }"),
            [new HostBuffer("Wide", type, [0], true)]);
        Assert.That(compilation.HasErrors, Is.True);
        Assert.That(compilation.Program, Is.Null);
    }
    [TestCase("void Main() { Host.Source[0] = (char16)65; }")]
    [TestCase("int32 Main() { return Host.Missing[0]; }")]
    [TestCase("int32 Main() { return (int32)Host.Source[true]; }")]
    [TestCase("void Main() { var borrowed = Host.Source; }")]
    [TestCase("int32 Main() { Host.Tokens.Length = 1; return 0; }")]
    [TestCase("int32 Main() { Host.Tokens[0] = true; return 0; }")]
    public void BufferCapabilitiesRejectInvalidAccessAndEscape(string source)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(source), ScannerHost.CreateBuffers(SourceText.From("x")));
        Assert.That(compilation.HasErrors, Is.True);
        Assert.That(compilation.Program, Is.Null);
    }

    [Test]
    public void NamedBuffersRequireExplicitHostGrants()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("int32 Main() { return Host.Source.Length; }"));
        Assert.That(compilation.HasErrors, Is.True);
        Assert.That(compilation.Program, Is.Null);
    }
    [TestCase("int32 Main() { return Host.State[-1]; }",unchecked((int)0xE0540004))]
    [TestCase("int32 Main() { return Host.State[2]; }",unchecked((int)0xE0540004))]
    [TestCase("int32 Main() { return Host.State[^0]; }",unchecked((int)0xE0540004))]
    [TestCase("int32 Main() { return Host.State[^3]; }",unchecked((int)0xE0540004))]
    [TestCase("int32 Main() { return Host.State[^(-1)]; }",unchecked((int)0xE0540004))]
    [TestCase("int32 Main() { Host.State[^1] = 42; return Host.State[^1]; }",42)]
    [TestCase("int32 Main() { const int32 last = 1; Host.State[^last] = 42; return Host.State[1]; }",42)]
    [TestCase("int32 Offset() { Host.State[0] = Host.State[0] + 1; return 1; } int32 Value() { Host.State[0] = Host.State[0] + 1; return 40; } int32 Main() { Host.State[^Offset()] = Value(); return Host.State[^Offset()] + Host.State[0]; }",43)]
    [TestCase("int32 Offset() { return 0; } int32 Main() { Host.State[1] = 42; return Host.State[^(Offset() + 1)]; }",42)]
    [TestCase("int32 Offset() { Host.State[0] = Host.State[0] + 1; return 1; } int32 Main() { Host.State[1] = 40; Host.State[^Offset()] += 2; return Host.State[1] + Host.State[0]; }",43)]
    [TestCase("int32 Main() { Host.State[2] = Fail(); return 0; } int32 Fail() { require false; return 0; }",unchecked((int)0xE0540004))]
    [TestCase("int32 Offset() { return -1; } int32 Main() { return Host.State[^Offset()]; }",unchecked((int)0xE0540004))]
    [TestCase("int32 Offset() { return 0; } int32 Main() { return Host.State[^Offset()]; }",unchecked((int)0xE0540004))]
    [TestCase("int32 Offset() { return 2; } int32 Main() { Host.State[0] = 42; return Host.State[^Offset()]; }",42)]
    [TestCase("int32 Main() { Host.State[Index()] = Value(); return Host.State[0] * 10 + Host.State[1]; } int32 Index() { Host.State[0] = Host.State[0] + 1; return 1; } int32 Value() { return Host.State[0] + 6; }",17)]
    public async Task BufferAccessChecksBoundsAndPreservesEvaluationOrderAsync(string source, int expected)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(source),
            [new HostBuffer("State", PrimitiveType.Int32, [0, 0], true)]);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        await ExecuteAsync(((global::System.Func<BoundProgram>)(() => { var __nunitActual0 = compilation.Program; Assert.That(__nunitActual0, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual0!; }))(), expected).ConfigureAwait(true);
    }
    [TestCase("")]
    [TestCase(" \t\r\n\u0085\u2028\u2029\u00A0\u3000")]
    [TestCase("alpha _value 0123 + (beta != 7) >> >= && & || | == =")]
    [TestCase("a??b a??=b T?? T??? ? ?: ??= ??")]
    [TestCase("++count count-- count++ + ++count count-- - --count")]
    [TestCase("int32 F(int32 x) => x == 0; => = > ==> /* arrow */ =>")]
    [TestCase("_ _name __ _123 _/* trivia */=int32(42);")]
    [TestCase("\U00010400_var \u2118 \u212E \u2122 \u0301")]
    [TestCase("a\u200Cb a\u200Db \u200C \u200D \u0661 a\u06612")]
    [TestCase("a\u00E9 ae\u0301 \U0001F600x")]
    [TestCase("0x1.fp+2 0x_1_f.0_fP+1_0 0x1p-4 0x1p2 0x1__p2 0x1._p2 0x1p2__")]
    [TestCase("0x1p2 0X.8P-1 0x1.p2 0x.p2 0x1._p2 0x1__p2 0x1.2__p2")]
    [TestCase("0x1p 0x1p+ 0x1P- 0x1p2")]
    [TestCase("0xi32")]
    [TestCase("if while require int32 2147483648")]
    [TestCase("abstract operator where abstractly operator_ where2 G<T( ;")]
    [TestCase("// trailing comment")]
    [TestCase("alpha/* inner // ignored */+// line\r\n\"quoted\\ntext\"")]
    [TestCase("1.25e-10f64 2e+3 3i128 4u8 0.5 6. 7efoo 8i32extra")]
    [TestCase("0i8 0i16 0i32 0i64 0i128 0u8 0u16 0u32 0u64 0u128 0f16 0f32 0f64 0f128")]
    [TestCase("1f8 2u128x 3i64\u200D 4f128\uD800 5E+6f32 7e+8 9e-0 10e\u00E9")]
    [TestCase("1e 2e+ 3E-; 4e. 5e_ 6e\uD800 7e+f64")]
    [TestCase("\u0661 \uFF11 \U0001D7D9 1\u0661 1\uFF11 1\U0001D7D9")]
    [TestCase("0x_FF 0xFF_FF 0xFF_ 0b_1 0b1__0")]
    [TestCase("1_000 1__2 1.2_3 1.2__3 1e4_5 1e4__5")]
    [TestCase("\"\" \"\\u0041\" \"\\U0001F600\" \"\\\"\" \"\\\\\"")]
    [TestCase("\"\\uD800\" \"\\U00110000\" \"\\u12x\" \"\\q\"")]
    [TestCase("\"\\UFFFFFFFF\" \"\\U0000DFFF\" \"\\U0010FFFF\" \"\\uFFFF\" \"\\u0000\"")]
    [TestCase("\"\\U1234567\" \"\\u\" \"\\z\uD800\" \"\uDC00\"")]
    [TestCase("\"bad\uD800\uDC00\uD800\" //\uDC00\r\n/*\uD800*/")]
    [TestCase("\"unterminated\nsecond \"ok\"")]
    [TestCase("\"incomplete\\\r\nnext")]
    [TestCase("/* unterminated \uD800")]
    [TestCase("/*a*/ / /*b*/ * / //\U0001F600\uDC00\nx")]
    public async Task CompiledScannerAgreesWithHostOnTokenBoundariesAndDiagnosticsAsync(string source)
    {
        await CheckScannerAsync(source).ConfigureAwait(true);
    }

    [Test]
    public async Task MalformedSurrogatesRetainExactCodeUnitBoundariesAsync()
    {
        await CheckScannerAsync("\uD800x \uDC00 \uD800\uD800 \uDC00\uDC00 \U00010400").ConfigureAwait(true);
    }

    [Test]
    public async Task IntegerMagnitudeOverflowBoundariesMatchAcrossRadicesAsync()
    {
        string binaryMaximum = "0b" + new string('1', 64);
        string binaryOverflow = "0b1" + new string('0', 64);
        string source = string.Join(' ',
            "18446744073709551615",
            "18446744073709551616",
            "0xFFFFFFFFFFFFFFFF",
            "0x10000000000000000",
            binaryMaximum,
            binaryOverflow);

        await CheckScannerAsync(source).ConfigureAwait(true);
    }

    [Test]
    public async Task NumericScannerMatchesHostedLexerAcrossDeterministicTokenCorpusAsync()
    {
        const string decimalCharacters = "0123456789_.eE+-iIuUfF";
        const string binaryCharacters = "01_23456789xXbB.pPeE+-iuf";
        const string hexadecimalCharacters = "0123456789abcdefABCDEF_xXbB.pPeE+-iuf";
        var random = new Random(0x5EED);
        var source = new StringBuilder();
        for (var index = 0; index < 384; index++)
        {
            var form = index % 3;
            _ = source.Append(form switch {
                0 => (char)('1' + random.Next(9)),
                1 or 2 => '0',
                _ => throw new InvalidOperationException(),
            });
            if (form == 1)
            {
                _ = source.Append('x');
            }
            else if (form == 2)
            {
                _ = source.Append('b');
            }

            var characters = form switch {
                0 => decimalCharacters,
                1 => hexadecimalCharacters,
                2 => binaryCharacters,
                _ => throw new InvalidOperationException(),
            };
            var length = random.Next(1, 25);
            for (var characterIndex = 0; characterIndex < length; characterIndex++)
            {
                _ = source.Append(characters[random.Next(characters.Length)]);
            }
            _ = source.Append(index % 16 == 15 ? "\r\n" : " ");
        }

        await CheckScannerAsync(source.ToString()).ConfigureAwait(true);
    }

    [Test]
    public async Task LongHexFloatMantissaKeepsScannerParityAsync()
    {
        var source = "0x" + new string('A', 2048) + "." + new string('F', 2048) + "p+10";
        await CheckScannerAsync(source).ConfigureAwait(true);
    }

    [Test]
    public async Task CharacterLiteralsRetainTokenKindsAndDiagnosticParityAsync()
    {
        await CheckScannerAsync("'A' '\\'' '\\u0041' '\\U0001F600' '😀' '' 'ab' '\\q' '\\uD800' 'unterminated\nnext")
            .ConfigureAwait(true);
    }

    [Test]
    public async Task LongAsciiStringContentsPreserveScannerParityAsync()
    {
        await CheckScannerAsync("\"" + new string('a', 8_192) + "\"").ConfigureAwait(true);
    }

    [Test]
    public async Task LongAsciiCommentsPreserveScannerParityAsync()
    {
        var source = "//" + new string('a', 8_000) + "\r\n/*" + new string('b', 8_000) + "*/value";
        await CheckScannerAsync(source).ConfigureAwait(true);
    }

    [Test]
    public async Task LongAsciiIdentifierPreservesScannerParityAsync()
    {
        await CheckScannerAsync(new string('a', 8_000)).ConfigureAwait(true);
    }

    [Test]
    public async Task CharacterLiteralEscapeDiagnosticsMatchHostedBoundariesAsync()
    {
        string[] literals = [
            "'\\n'", "'\\r'", "'\\t'", "'\\\\'", "'\\\"'", "'\\0'", "'\\''",
            "'\\u0000'", "'\\u0041'", "'\\uD7FF'", "'\\uE000'", "'\\uFFFF'",
            "'\\u000'", "'\\u004G'", "'\\uD800'", "'\\uDC00'", "'\\u00000'",
            "'\\U00000000'", "'\\U0001F600'", "'\\U0010FFFF'", "'\\U00110000'",
            "'\\U0000D800'", "'\\U12G45678'", "''", "'ab'", "'😀x'", "'\\qA'",
            "'\\u0041\\q'",
        ];
        await CheckScannerAsync(string.Join(' ', literals) + " '\uD800' '\uDC00'")
            .ConfigureAwait(true);
    }

    [Test]
    public async Task StringEscapeDiagnosticsMatchHostedBoundariesAsync()
    {
        string[] literals = [
            "\"\\n\"", "\"\\r\"", "\"\\t\"", "\"\\\\\"", "\"\\\"\"", "\"\\0\"",
            "\"\\u0000\"", "\"\\u0041\"", "\"\\uD7FF\"", "\"\\uE000\"", "\"\\uFFFF\"",
            "\"\\U00000000\"", "\"\\U0001F600\"", "\"\\U0010FFFF\"",
            "\"\\u\"", "\"\\u004G\"", "\"\\uD800\"", "\"\\U0000D800\"",
            "\"\\U00110000\"", "\"\\U1234567\"", "\"\\q\"", "\"\\u0041\\q\"",
            "\"\\q\\U00110000\"", "\"\\q\uD800\"", "\"\\😀\"",
            "\"\\\uD800\"", "\"\\\uDC00\"",
            "\"\uD800\"", "\"\uDC00\"",
        ];
        await CheckScannerAsync(string.Join(' ', literals) + " \"unterminated\\")
            .ConfigureAwait(true);
    }

    [Test]
    public async Task EveryKeywordClassifiesNativelyAsync()
    {
        var keywords = Enum.GetValues<TokenKind>()
            .Where(kind => kind != TokenKind.AbstractKeyword
                && kind.ToString().EndsWith("Keyword", StringComparison.Ordinal))
            .Select(kind => {
                var name = kind.ToString();
                return (Kind: kind, Text: char.ToLowerInvariant(name[0]) + name[1..^"Keyword".Length]);
            }).ToArray();
        var source = string.Join(' ', keywords.Select(keyword => keyword.Text)) + " namespace_extra Namespace";
        var lexed = Lexer.Lex(SourceText.From(source));
        Assert.That(lexed.Tokens.Select(token => token.Kind), Is.EqualTo(keywords.Select(keyword => keyword.Kind).Append(TokenKind.Identifier).Append(TokenKind.Identifier)
            .Append(TokenKind.EndOfFile)));
        await CheckScannerAsync(source).ConfigureAwait(true);
    }

    [Test]
    public void TokenKindsAppendWithoutRenumberingPublishedKinds()
    {
        Assert.That((int)TokenKind.InterfaceKeyword, Is.EqualTo(24));
        Assert.That((int)TokenKind.UnsafeKeyword, Is.EqualTo(76));
        Assert.That((int)TokenKind.AbstractKeyword, Is.EqualTo(77));
        Assert.That((int)TokenKind.OperatorKeyword, Is.EqualTo(78));
        Assert.That((int)TokenKind.WhereKeyword, Is.EqualTo(79));
        Assert.That((int)TokenKind.AliasKeyword, Is.EqualTo(102));
        Assert.That((int)TokenKind.CharacterLiteral, Is.EqualTo(103));
        Assert.That((int)TokenKind.DotDot, Is.EqualTo(104));
        Assert.That((int)TokenKind.DotDotCaret, Is.EqualTo(105));
        Assert.That((int)TokenKind.QuestionDot, Is.EqualTo(106));
    }

    [Test]
    public async Task EveryOperatorAndTriviaFormExecutesNativelyAsync()
    {
        const string Operators = "_ ( ) { } [ ] ; , . .. ..^ : ? ?. ?? ??= + - ++ -- * / % ! ~ = == => != < <= > >= & && | || ^"
            + " << >> >>> += -= *= /= %= &= |= ^= <<= >>= >>>=";
        var lexed = Lexer.Lex(SourceText.From(Operators));
        var actual = lexed.Tokens.Select(token => token.Kind).ToHashSet();
        foreach (var kind in Enum.GetValues<TokenKind>())
        {
            if (kind is TokenKind.Bad or TokenKind.Identifier or TokenKind.NumericLiteral or TokenKind.StringLiteral
                or TokenKind.CharacterLiteral
                || kind.ToString().EndsWith("Keyword", StringComparison.Ordinal))
            {
                continue;
            }
            Assert.That(actual, Does.Contain(kind));
        }

        await CheckScannerAsync(Operators).ConfigureAwait(true);
        await CheckScannerAsync(" \t\u00A0\u1680\u2000\u202F\u205F\u3000\n\r\r\n\u0085\u2028\u2029// line\n/* block */"
            + "x \t// eof", repeat: true).ConfigureAwait(true);
        await CheckScannerAsync("/* unterminated \uDC00").ConfigureAwait(true);
    }

    [Test]
    public async Task ExtendedStaticProtocolSampleAgreesWithCompiledScannerAsync()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
        var source = await File.ReadAllTextAsync(Path.Combine(root, "samples", "native",
            "static-protocol-binary.terrum")).ConfigureAwait(true);
        await CheckScannerAsync(source).ConfigureAwait(true);
    }

    [Test]
    public Task SelfBoundStaticProtocolAgreesWithCompiledScannerAsync() =>
        CheckScannerAsync(StaticProtocolSelfTests.Source);

    [Test]
    public async Task MultipleTypeParameterSampleAgreesWithCompiledScannerAsync()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
        var source = await File.ReadAllTextAsync(Path.Combine(root, "samples", "native",
            "multi-type-generics.terrum")).ConfigureAwait(true);
        await CheckScannerAsync(source).ConfigureAwait(true);
    }

    [Test]
    public async Task ManagedGenericSampleAgreesWithCompiledScannerAsync()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
        var source = await File.ReadAllTextAsync(Path.Combine(root, "samples", "native",
            "managed-generic-selection.terrum")).ConfigureAwait(true);
        await CheckScannerAsync(source).ConfigureAwait(true);
    }

    [Test]
    public async Task ComparisonProtocolSampleAgreesWithCompiledScannerAsync()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
        var source = await File.ReadAllTextAsync(Path.Combine(root, "samples", "native",
            "static-protocol-comparison.terrum")).ConfigureAwait(true);
        await CheckScannerAsync(source).ConfigureAwait(true);
    }

    [Test]
    public async Task DiagnosticAndTokenBuffersHandleDenseInvalidInputAsync()
    {
        await CheckScannerAsync(new string('\uDC00', 256)).ConfigureAwait(true);
    }

    [Test]
    public async Task TriviaBufferHandlesDenseAlternatingRunsAsync()
    {
        await CheckScannerAsync(string.Concat(Enumerable.Repeat(" \n", 512))).ConfigureAwait(true);
    }
    [TestCase("0 00 000000000000000000000000000000000000000001 256u8")]
    [TestCase("9223372036854775807 9223372036854775808i64 -9223372036854775808i64")]
    [TestCase("18446744073709551615 18446744073709551615u64 18446744073709551616")]
    [TestCase("0x0 0X2A 0x1f32 0XFFFFFFFFFFFFFFFF 0x10000000000000000")]
    [TestCase("0x 0X 0xG 0x1g 0x1u32 0x1f32 0x1_2 0x1.2 0x1p2")]
    [TestCase("0b0 0B1 0b101010 0B11111111111111111111111111111111 0b000000000000000000000000000000000000000000000000000000000000000001")]
    [TestCase("0b 0B2 0b102 0b1u32 0b1f32 0b1_0 0b1.1 0b1p2")]
    [TestCase("1_000 1__2 0x_FFFF_FFFF_FFFF_FFFF 0b___1010 0b_11111111_11111111_11111111_11111111")]
    [TestCase("1_ 0x___ 0b___ 1_.2 1.2_3e+4_5 1.2_ 1e+2_3 1e+2_ 0b1_ 0xF__ 1_000i32 0b_1f32 0x_F_Fi32")]
    [TestCase("0x_1_0000_0000_0000_0000 0b_1_0000000000000000000000000000000000000000000000000000000000000000")]
    [TestCase("18_446_744_073_709_551_615 18_446_744_073_709_551_616 1_foo 0xF_tail 0b1_tail")]
    [TestCase("18446744073709551616i128 340282366920938463463374607431768211455u128")]
    [TestCase("1i8 2i16 3i32 4i64 5i128 6u8 7u16 8u32 9u64 10u128")]
    [TestCase("1.25 2e+3 3E- 4e 5f32 6f128 7efoo 8i64extra 9i128\u200D")]
    [TestCase("12345678901234567890123456789012345678901234567890u128 // trailing")]
    public async Task CompiledIntegerDecoderMatchesBigIntegerOracleAsync(string source)
    {
        await CheckScannerAsync(source, repeat: true).ConfigureAwait(true);
    }

    [Test]
    public async Task RepeatedScanReplacesPublishedDiagnosticsAsync()
    {
        await CheckScannerAsync("\"\\q\" /*\uD800*/ 1e+ \uDC00", repeat: true).ConfigureAwait(true);
    }

    [Test]
    public async Task PinnedIdentifierRangeEdgesExecuteInTerrumAsync()
    {
        var source = new StringBuilder();
        foreach (var pair in UnicodeIdentifierProfile.StartRanges.ToArray().Chunk(2).Where((_, index) => index % 31 == 0))
        {
            foreach (var scalar in new[] { pair[0] - 1, pair[0], pair[1], pair[1] + 1 })
            {
                if (scalar is >= 0 and <= 0x10FFFF and not (>= 0xD800 and <= 0xDFFF))
                {
                    _ = source.Append(char.ConvertFromUtf32(scalar)).Append(' ');
                }
            }
        }

        await CheckScannerAsync(source.ToString()).ConfigureAwait(true);
        await CheckScannerAsync("@\U00010400\U00010428").ConfigureAwait(true);
    }

    [Test]
    public void HostBufferInputPreservesMalformedUnitsAndRejectsResourceExcess()
    {
        var buffers = ScannerHost.CreateBuffers(SourceText.From("\uD800\0\uDC00"));
        Assert.That(buffers[0].Values, Is.EqualTo([0xD800, 0, 0xDC00]));
        Assert.That(buffers[1].Values.ToArray(), Is.EqualTo(UnicodeIdentifierProfile.StartRanges.ToArray()));
        Assert.That(buffers[2].Values.ToArray(), Is.EqualTo(UnicodeIdentifierProfile.ContinueRanges.ToArray()));
        Assert.That(buffers[0].IsWritable, Is.False);
        Assert.That(buffers[3].IsWritable, Is.True);
        Assert.That(buffers[4].IsWritable, Is.True);
        Assert.That(buffers[5].IsWritable, Is.True);
        Assert.That(buffers[6].IsWritable, Is.False);
        Assert.That(buffers[7].IsWritable, Is.False);
        Assert.That(buffers[8].IsWritable, Is.True);
        Assert.That(buffers[5].Values.Length, Is.EqualTo(1 + (4 * 3)));
        Assert.That(buffers[8].Values.Length, Is.EqualTo(1 + (5 * 3)));
        _ = Assert.Throws<ArgumentException>(() => ScannerHost.CreateBuffers(
            SourceText.From(new string('x', ScannerHost.MaximumSourceLength + 1))))!;
    }
    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
    public async Task PersistedScannerProfileRebindsFreshBuffersForEachInvocationAsync(BoundRuntimePolicy policy)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
        var scanner = await ReadScannerSourceAsync(root).ConfigureAwait(true);
        scanner = scanner.Replace("int32 Scan()", "public int32 Scan()", StringComparison.Ordinal);
        var seed = SourceText.From("seed 1");
        var providerCompilation = Compilation.Create(SyntaxTree.Parse(scanner + "\nint32 Main() { return 0; }"),
            ScannerHost.CreateInvocationProfileBuffers(seed), policy);
        Assert.That(providerCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, providerCompilation.Diagnostics));
        var provider = ModuleCompiler.CreateProducer(((global::System.Func<BoundProgram>)(() => { var __nunitActual1 = providerCompilation.Program; Assert.That(__nunitActual1, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual1!; }))(),
            ["Terrum.CodeAnalysis.Scan"]);

        var alternateCompilation = Compilation.Create(
            SyntaxTree.Parse(scanner + "\nint32 Main() { return 0; }"),
            ScannerHost.CreateInvocationProfileBuffers(SourceText.From("a differently sized seed")), policy);
        Assert.That(alternateCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, alternateCompilation.Diagnostics));
        var alternateProvider = ModuleCompiler.CreateProducer(
            ((global::System.Func<BoundProgram>)(() => { var __nunitActual2 = alternateCompilation.Program; Assert.That(__nunitActual2, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual2!; }))(), ["Terrum.CodeAnalysis.Scan"]);
        Assert.That(alternateProvider, Is.EqualTo(provider));

        var invalidProvider = provider.ToArray();
        var sourceDescriptor = Encoding.UTF8.GetBytes("Source");
        var sourceDescriptorOffset = invalidProvider.AsSpan().IndexOf(sourceDescriptor);
        Assert.That(sourceDescriptorOffset >= 0, Is.True);
        invalidProvider[sourceDescriptorOffset] = (byte)'X';
        _ = Assert.Throws<ModuleFormatException>(() => ModuleCompiler.CreateReference(invalidProvider))!;

        var reference = ModuleCompiler.CreateReference(provider);
        Assert.That(reference.InvocationProfileId, Is.EqualTo(ScannerHost.InvocationProfileIdentity));
        var consumerCompilation = Compilation.Create(
            SyntaxTree.Parse("namespace Terrum.CodeAnalysis; int32 Main() { return Scan(); }"),
            reference, policy);
        Assert.That(consumerCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, consumerCompilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(
            ((global::System.Func<BoundProgram>)(() => { var __nunitActual3 = consumerCompilation.Program; Assert.That(__nunitActual3, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual3!; }))(), provider);
        var linked = ModuleCompiler.Link(consumer, provider);

        var sources = new List<string> {
            "if 12i8 18446744073709551616u64 // trailing trivia\r\n\"\\q\"",
            "\uD800 0x_ \uDC00",
            new string('x', ScannerHost.MaximumSourceLength),
            "",
        };
        var maximumTokenAndTriviaSource = new StringBuilder(ScannerHost.MaximumSourceLength);
        for (var index = 0; index < ScannerHost.MaximumSourceLength / 2; index++)
        {
            maximumTokenAndTriviaSource.Append("a ");
        }
        Assert.That(maximumTokenAndTriviaSource.Length, Is.EqualTo(ScannerHost.MaximumSourceLength));
        sources.Add(maximumTokenAndTriviaSource.ToString());

        var maximumTriviaSource = new string('\n', ScannerHost.MaximumSourceLength);
        sources.Add(maximumTriviaSource);

        var maximumIntegerRecordSource = new StringBuilder(ScannerHost.MaximumSourceLength);
        for (var index = 0; index < ScannerHost.MaximumSourceLength / 2; index++)
        {
            maximumIntegerRecordSource.Append("1 ");
        }
        Assert.That(maximumIntegerRecordSource.Length, Is.EqualTo(ScannerHost.MaximumSourceLength));
        var maximumIntegerRecordText = maximumIntegerRecordSource.ToString();
        sources.Add(maximumIntegerRecordText);

        var maximumDiagnosticSource = new string('\uD800', ScannerHost.MaximumSourceLength);
        sources.Add(maximumDiagnosticSource);

        var binaryMaximum = "0b" + new string('1', 64);
        var binaryOverflow = "0b1" + new string('0', 64);
        var integerBoundarySource = string.Join(' ',
            "0xFFFFFFFFFFFFFFFF",
            "0x10000000000000000",
            binaryMaximum,
            binaryOverflow,
            "18446744073709551615",
            "18446744073709551616");
        sources.Add(integerBoundarySource);
        const int maximumBmpEscapesPerSource = 2_700;
        for (var scalarStart = (int)char.MinValue;
            scalarStart <= char.MaxValue;
            scalarStart += maximumBmpEscapesPerSource)
        {
            var scalarCount = Math.Min(maximumBmpEscapesPerSource,
                char.MaxValue + 1 - scalarStart);
            var text = new StringBuilder(scalarCount * 6 + 2).Append('"');
            for (var index = 0; index < scalarCount; index++)
            {
                text.Append("\\u").Append((scalarStart + index).ToString("X4",
                    CultureInfo.InvariantCulture));
            }
            text.Append('"');
            sources.Add(text.ToString());
        }

        var supplementaryEscapes = new List<int> { 0, 0xD7FF, 0xD800, 0xDFFF, 0xE000, 0xFFFF };
        for (var plane = 1; plane <= 16; plane++)
        {
            var planeStart = plane << 16;
            supplementaryEscapes.Add(planeStart);
            supplementaryEscapes.Add(planeStart + 1);
            supplementaryEscapes.Add(planeStart + 0xFFFE);
            supplementaryEscapes.Add(planeStart + 0xFFFF);
        }
        supplementaryEscapes.Add(0x110000);
        var supplementaryEscapeSource = new StringBuilder();
        foreach (var scalar in supplementaryEscapes)
        {
            supplementaryEscapeSource.Append("\"\\U")
                .Append(scalar.ToString("X8", CultureInfo.InvariantCulture))
                .Append("\" ");
        }
        sources.Add(supplementaryEscapeSource.ToString());
        sources.Add("""
            "\u" "\u0" "\u000" "\u000G" "\u00000"
            "\U1234567" "\U0000000G" "\U0000000" "\UFFFFFFFFF" "\U00110000"
            """);
        sources.Add("\"\\U0000000");

        const int maximumSurrogateEscapesPerSource = 1_024;
        for (var scalarStart = 0xD800; scalarStart <= 0xDFFF;
            scalarStart += maximumSurrogateEscapesPerSource)
        {
            var scalarCount = Math.Min(maximumSurrogateEscapesPerSource, 0xE000 - scalarStart);
            var text = new StringBuilder(scalarCount * 10 + 2).Append('"');
            for (var index = 0; index < scalarCount; index++)
            {
                text.Append("\\U").Append((scalarStart + index).ToString("X8",
                    CultureInfo.InvariantCulture));
            }
            text.Append('"');
            Assert.That(text.Length, Is.InRange(1, ScannerHost.MaximumSourceLength));
            sources.Add(text.ToString());
        }

        var rawSurrogateSource = new StringBuilder(0x8000);
        for (var codeUnit = 0xD800; codeUnit <= 0xDFFF; codeUnit++)
        {
            rawSurrogateSource.Append('"').Append((char)codeUnit).Append("\" ");
        }
        Assert.That(rawSurrogateSource.Length, Is.InRange(1, ScannerHost.MaximumSourceLength));
        sources.Add(rawSurrogateSource.ToString());

        const int maximumSupplementaryEscapesPerSource = 1_500;
        for (var scalarStart = 0x10000; scalarStart <= 0x10FFFF;
            scalarStart += maximumSupplementaryEscapesPerSource)
        {
            var scalarCount = Math.Min(maximumSupplementaryEscapesPerSource, 0x110000 - scalarStart);
            var text = new StringBuilder(scalarCount * 10 + 2).Append('"');
            for (var index = 0; index < scalarCount; index++)
            {
                text.Append("\\U").Append((scalarStart + index).ToString("X8",
                    CultureInfo.InvariantCulture));
            }
            text.Append('"');
            Assert.That(text.Length, Is.InRange(1, ScannerHost.MaximumSourceLength));
            sources.Add(text.ToString());
        }

        var supplementarySource = new StringBuilder();
        foreach (var scalar in supplementaryEscapes)
        {
            if (scalar == 0 || scalar > 0x10FFFF || scalar is >= 0xD800 and <= 0xDFFF)
            {
                continue;
            }
            supplementarySource.Append('"')
                .Append(char.ConvertFromUtf32(scalar))
                .Append("\" ");
        }
        sources.Add(supplementarySource.ToString());

        const int maximumRawSupplementaryScalarsPerSource = 8_000;
        for (var scalarStart = 0x10000; scalarStart <= 0x10FFFF;
            scalarStart += maximumRawSupplementaryScalarsPerSource)
        {
            var scalarCount = Math.Min(maximumRawSupplementaryScalarsPerSource, 0x110000 - scalarStart);
            var text = new StringBuilder(scalarCount * 2 + 2).Append('"');
            for (var index = 0; index < scalarCount; index++)
            {
                text.Append(char.ConvertFromUtf32(scalarStart + index));
            }
            text.Append('"');
            Assert.That(text.Length, Is.InRange(1, ScannerHost.MaximumSourceLength));
            sources.Add(text.ToString());
        }

        const int maximumSupplementaryIdentifierScalarsPerSource = 1_700;
        for (var scalarStart = 0x10000; scalarStart <= 0x10FFFF;
            scalarStart += maximumSupplementaryIdentifierScalarsPerSource)
        {
            var scalarCount = Math.Min(maximumSupplementaryIdentifierScalarsPerSource,
                0x110000 - scalarStart);
            var text = new StringBuilder(scalarCount * 9);
            for (var index = 0; index < scalarCount; index++)
            {
                var scalar = char.ConvertFromUtf32(scalarStart + index);
                text.Append(scalar).Append("x A").Append(scalar).Append("x ");
            }
            Assert.That(text.Length, Is.InRange(1, ScannerHost.MaximumSourceLength));
            sources.Add(text.ToString());
        }

        var maximumLengthSupplementarySource = new StringBuilder(ScannerHost.MaximumSourceLength)
            .Append('"')
            .Append('a', ScannerHost.MaximumSourceLength - 4)
            .Append(char.ConvertFromUtf32(0x10FFFF))
            .Append('"')
            .ToString();
        Assert.That(maximumLengthSupplementarySource.Length, Is.EqualTo(ScannerHost.MaximumSourceLength));
        sources.Add(maximumLengthSupplementarySource);

        foreach (var trailingSurrogate in new[] { '\uD800', '\uDC00' })
        {
            var maximumLengthMalformedSource = new string('x', ScannerHost.MaximumSourceLength - 1)
                + trailingSurrogate;
            Assert.That(maximumLengthMalformedSource.Length, Is.EqualTo(ScannerHost.MaximumSourceLength));
            sources.Add(maximumLengthMalformedSource);
        }

        ScannerInvocationResult? retained = null;
        foreach (var text in sources)
        {
            var source = SourceText.From(text);
            var expected = Lexer.Lex(source);
            var actual = NativeJit.ExecuteWithScannerProfile(linked, source, (NativeRuntimePolicy)policy);
            Assert.That(actual.LexResult.Tokens.Select(static token => (token.Kind, token.Span)), Is.EqualTo(expected.Tokens.Select(static token => (token.Kind, token.Span))));
            Assert.That(actual.LexResult.Diagnostics.Select(static diagnostic => (diagnostic.Code, diagnostic.Span)), Is.EqualTo(expected.Diagnostics.Select(static diagnostic => (diagnostic.Code, diagnostic.Span))));

            var expectedTrivia = ImmutableArray.CreateBuilder<ScannerTrivia>();
            for (var tokenIndex = 0; tokenIndex < expected.Tokens.Length; tokenIndex++)
            {
                foreach (var trivia in expected.Tokens[tokenIndex].LeadingTrivia)
                {
                    expectedTrivia.Add(new ScannerTrivia(trivia.Kind, trivia.Span, tokenIndex));
                }
            }
            Assert.That(actual.LexResult.Tokens
                .SelectMany(static (token, tokenIndex) => token.LeadingTrivia.Select(trivia =>
                    new ScannerTrivia(trivia.Kind, trivia.Span, tokenIndex))), Is.EqualTo(expectedTrivia));

            if (text.StartsWith("if ", StringComparison.Ordinal))
            {
                Assert.That(actual.IntegerValues,
                    Is.EqualTo(new ScannerIntegerValue[] { new(1, "i8", false, 12), new(2, "u64", true, null) }));
                retained = actual;
            }
            else if (text == integerBoundarySource)
            {
                Assert.That(actual.IntegerValues, Is.EqualTo(new ScannerIntegerValue[]
                {
                    new(0, "", false, ulong.MaxValue),
                    new(1, "", true, null),
                    new(2, "", false, ulong.MaxValue),
                    new(3, "", true, null),
                    new(4, "", false, ulong.MaxValue),
                    new(5, "", true, null),
                }));
            }
            else if (text.Length == 0)
            {
                Assert.That(actual.LexResult.Diagnostics, Is.Empty);
                Assert.That(actual.IntegerValues, Is.Empty);
            }
            else if (text == maximumDiagnosticSource)
            {
                Assert.That(actual.LexResult.Diagnostics.Length, Is.EqualTo(ScannerHost.MaximumSourceLength));
                Assert.That(actual.TokenCount, Is.EqualTo(ScannerHost.MaximumSourceLength + 1));
            }
            else if (text == maximumTriviaSource)
            {
                Assert.That(actual.TokenCount, Is.EqualTo(1));
                Assert.That(actual.LexResult.Tokens
                    .SelectMany(static token => token.LeadingTrivia).Count(), Is.EqualTo(ScannerHost.MaximumSourceLength));
            }
            else if (text == maximumIntegerRecordText)
            {
                Assert.That(actual.IntegerValues.Length, Is.EqualTo(ScannerHost.MaximumSourceLength / 2));
                for (var index = 0; index < actual.IntegerValues.Length; index++)
                {
                    Assert.That(actual.IntegerValues[index], Is.EqualTo(new ScannerIntegerValue(index, "", false, 1)));
                }
            }
        }

        TestAssertions.NotNull(retained);
        Assert.That(retained.TokenCount, Is.EqualTo(5));
        Assert.That(retained.LexResult.Diagnostics.Length, Is.EqualTo(3));
        Assert.That(retained.IntegerValues,
            Is.EqualTo(new ScannerIntegerValue[] { new(1, "i8", false, 12), new(2, "u64", true, null) }));
    }
    [TestCase(0,0,0)]
    [TestCase(int.MaxValue,0,1)]
    [TestCase(1,-1,1)]
    [TestCase(1,0,2)]
    [TestCase(1,0,0)]
    public void ResultAdapterRejectsInvalidPublishedRecords(int kind, int start, int length)
    {
        var bytes = new byte[24];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, kind);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), start);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8), length);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(16), 1);
        _ = Assert.Throws<ArgumentException>(() => ScannerHost.ReadTokens(SourceText.From("x"), 2, bytes))!;
    }
    [TestCase(2,0,0,1,0)]
    [TestCase(1,6,0,1,0)]
    [TestCase(1,0,1,0,0)]
    [TestCase(1,0,0,3,0)]
    [TestCase(1,0,0,2,0)]
    [TestCase(1,0,0,1,1)]
    public void ResultAdapterRejectsInvalidTriviaRecords(int count, int kind, int start, int length, int owner)
    {
        var source = SourceText.From(" x");
        ImmutableArray<ScannerToken> tokens =
        [
            new(TokenKind.Identifier, new TextSpan(1, 1)),
            new(TokenKind.EndOfFile, new TextSpan(2, 0)),
        ];
        var bytes = new byte[20];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, count);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), kind);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8), start);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(12), length);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(16), owner);
        _ = Assert.Throws<ArgumentException>(() => ScannerHost.ReadTrivia(source, tokens, bytes))!;
    }
    [TestCase(2,1,0,1)]
    [TestCase(1,0,0,1)]
    [TestCase(1,13,0,1)]
    [TestCase(1,1,-1,1)]
    [TestCase(1,1,0,0)]
    [TestCase(1,1,0,2)]
    public void ResultAdapterRejectsInvalidDiagnosticRecords(int count, int code, int start, int length)
    {
        var bytes = new byte[16];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, count);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), code);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8), start);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(12), length);
        _ = Assert.Throws<ArgumentException>(() => ScannerHost.ReadDiagnostics(SourceText.From("x"), bytes))!;
    }

    [Test]
    public void ResultAdapterAcceptsStringRecordsAndLexicalDiagnostics()
    {
        var bytes = new byte[24];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, (int)TokenKind.StringLiteral);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8), 1);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(16), 1);
        Assert.That(ScannerHost.ReadTokens(SourceText.From("\""), 2, bytes)[0].Kind, Is.EqualTo(TokenKind.StringLiteral));

        var diagnostics = new byte[16];
        BinaryPrimitives.WriteInt32LittleEndian(diagnostics, 1);
        BinaryPrimitives.WriteInt32LittleEndian(diagnostics.AsSpan(4), 2);
        BinaryPrimitives.WriteInt32LittleEndian(diagnostics.AsSpan(12), 1);
        Assert.That(ScannerHost.ReadDiagnostics(SourceText.From("\""), diagnostics)[0], Is.EqualTo(new ScannerDiagnostic("TRM0002", new TextSpan(0, 1))));
    }

    [Test]
    public void ResultAdapterAcceptsTrailingTriviaOwnedByEof()
    {
        var source = SourceText.From("x /*end*/");
        ImmutableArray<ScannerToken> tokens =
        [
            new(TokenKind.Identifier, new TextSpan(0, 1)),
            new(TokenKind.EndOfFile, new TextSpan(source.Length, 0)),
        ];
        var bytes = new byte[36];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, 2);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), (int)TriviaKind.Whitespace);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8), 1);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(12), 1);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(16), 1);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(20), (int)TriviaKind.MultiLineComment);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(24), 2);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(28), 7);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(32), 1);
        Assert.That(ScannerHost.ReadTrivia(source, tokens, bytes), Is.EqualTo([new ScannerTrivia(TriviaKind.Whitespace, new TextSpan(1, 1), 1),
                new ScannerTrivia(TriviaKind.MultiLineComment, new TextSpan(2, 7), 1)]));
    }
    [TestCase(-1,0,0,0,1)]
    [TestCase(1,0,0,0,1)]
    [TestCase(0,2,0,0,1)]
    [TestCase(0,1,1,0,1)]
    [TestCase(0,0,0,0,6)]
    public void IntegerResultAdapterRejectsMalformedRecords(int tokenIndex, int status, int low, int high, int tag)
    {
        var source = SourceText.From("12i8");
        ImmutableArray<ScannerToken> tokens =
        [
            new(TokenKind.NumericLiteral, new TextSpan(0, source.Length)),
            new(TokenKind.EndOfFile, new TextSpan(source.Length, 0)),
        ];
        var bytes = new byte[24];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, 1);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), tokenIndex);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8), status);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(12), low);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(16), high);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(20), tag);
        _ = Assert.Throws<ArgumentException>(() => ScannerHost.ReadIntegerValues(source, tokens, bytes))!;
    }
    [TestCase("2147483648",int.MinValue,0,2147483648UL)]
    [TestCase("9223372036854775808",0,int.MinValue,9223372036854775808UL)]
    [TestCase("18446744073709551615",-1,-1,ulong.MaxValue)]
    [TestCase("0xFFFFFFFFFFFFFFFF",-1,-1,ulong.MaxValue)]
    [TestCase("0X8000000000000000",0,int.MinValue,9223372036854775808UL)]
    [TestCase("0b10000000000000000000000000000000",int.MinValue,0,2147483648UL)]
    public void IntegerResultAdapterPreservesAll64MagnitudeBits(string text, int low, int high, ulong expected)
    {
        var source = SourceText.From(text);
        ImmutableArray<ScannerToken> tokens =
        [
            new(TokenKind.NumericLiteral, new TextSpan(0, source.Length)),
            new(TokenKind.EndOfFile, new TextSpan(source.Length, 0)),
        ];
        var bytes = new byte[24];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, 1);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(12), low);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(16), high);
        Assert.That(global::System.Linq.Enumerable.Single(ScannerHost.ReadIntegerValues(source, tokens, bytes)), Is.EqualTo(new ScannerIntegerValue(0, "", false, expected)));
    }

    internal static async Task CheckScannerAsync(string source, bool repeat = false)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
        var scanner = await ReadScannerSourceAsync(root).ConfigureAwait(true);
        var lexed = Lexer.Lex(SourceText.From(source));
        var expected = ImmutableArray.CreateBuilder<int>();
        var expectedTrivia = ImmutableArray.CreateBuilder<int>();
        var reconstructed = new StringBuilder();
        var tokenIndex = 0;
        foreach (var token in lexed.Tokens)
        {
            foreach (var trivia in token.LeadingTrivia)
            {
                expectedTrivia.Add((int)trivia.Kind);
                expectedTrivia.Add(trivia.Span.Start);
                expectedTrivia.Add(trivia.Span.Length);
                expectedTrivia.Add(tokenIndex);
                _ = reconstructed.Append(trivia.Text);
            }

            expected.Add((int)token.Kind);
            expected.Add(token.Span.Start);
            expected.Add(token.Span.Length);
            _ = reconstructed.Append(token.Text);
            tokenIndex++;
        }
        Assert.That(reconstructed.ToString(), Is.EqualTo(source));

        var expectedDiagnostics = ImmutableArray.CreateBuilder<int>();
        foreach (var diagnostic in lexed.Diagnostics)
        {
            expectedDiagnostics.Add(int.Parse(diagnostic.Code.AsSpan(3), System.Globalization.CultureInfo.InvariantCulture));
            expectedDiagnostics.Add(diagnostic.Span.Start);
            expectedDiagnostics.Add(diagnostic.Span.Length);
        }

        var expectedIntegers = ImmutableArray.CreateBuilder<int>();
        var integerValues = ImmutableArray.CreateBuilder<ScannerIntegerValue>();
        for (var index = 0; index < lexed.Tokens.Length - 1; index++)
        {
            var token = lexed.Tokens[index];
            if (token.Kind != TokenKind.NumericLiteral)
            {
                continue;
            }

            var text = token.Text;
            var tag = ScannerIntegerTag.None;
            var tagText = "";
            foreach (var candidate in Enum.GetValues<ScannerIntegerTag>().Skip(1))
            {
                var name = candidate.ToString();
                var suffix = char.ToLowerInvariant(name[0]) + name[1..];
                if (text.EndsWith(suffix, StringComparison.Ordinal))
                {
                    tag = candidate;
                    tagText = suffix;
                    text = text[..^suffix.Length];
                    break;
                }
            }

            var hex = text.StartsWith("0x", StringComparison.OrdinalIgnoreCase);
            var binary = text.StartsWith("0b", StringComparison.OrdinalIgnoreCase);
            var normalized = (hex || binary ? text[2..] : text).Replace("_", "", StringComparison.Ordinal);
            if (normalized.Length == 0 || (hex
                ? !normalized.All(ch =>
                    ch is (>= '0' and <= '9') or (>= 'a' and <= 'f') or (>= 'A' and <= 'F'))
                : binary ? !normalized.All(ch => ch is '0' or '1')
                    : !normalized.All(ch => ch is >= '0' and <= '9')))
            {
                continue;
            }

            var value = hex
                ? BigInteger.Parse("0" + normalized, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture)
                : binary ? BigInteger.Parse("0" + normalized, NumberStyles.AllowBinarySpecifier, CultureInfo.InvariantCulture)
                    : BigInteger.Parse(normalized, NumberStyles.None, CultureInfo.InvariantCulture);
            var overflow = value > ulong.MaxValue;
            var magnitude = overflow ? 0UL : (ulong)value;
            var status = overflow ? ScannerIntegerStatus.MagnitudeOverflow : ScannerIntegerStatus.Decoded;
            integerValues.Add(new ScannerIntegerValue(index, tagText, overflow, overflow ? null : magnitude));
            expectedIntegers.Add(index);
            expectedIntegers.Add((int)status);
            expectedIntegers.Add(unchecked((int)(uint)magnitude));
            expectedIntegers.Add(unchecked((int)(uint)(magnitude >> 32)));
            expectedIntegers.Add((int)tag);
        }
        var records = lexed.Tokens.Select(token => new ScannerToken(token.Kind, token.Span)).ToImmutableArray();
        var integerBytes = EncodeWords(expectedIntegers.ToImmutable().Insert(0, integerValues.Count));
        Assert.That(ScannerHost.ReadIntegerValues(lexed.Source, records, integerBytes), Is.EqualTo(integerValues));
        Assert.That(ScannerHost.ReadIntegerValues(lexed.Source, lexed.Tokens, integerBytes), Is.EqualTo(integerValues));

        var adapted = ScannerHost.ReadLexResult(lexed.Source, lexed.Tokens.Length,
            EncodeWords(expected.ToImmutable()),
            EncodeWords(expectedTrivia.ToImmutable().Insert(0, expectedTrivia.Count / 4)),
            EncodeWords(expectedDiagnostics.ToImmutable().Insert(0, expectedDiagnostics.Count / 3)));
        var hostTree = SyntaxTree.Parse(lexed);
        var adaptedTree = SyntaxTree.Parse(adapted);
        Assert.That(adaptedTree.Source, Is.SameAs(lexed.Source));
        Assert.That(adaptedTree.Root.ToFullString(), Is.EqualTo(source));
        Assert.That(adaptedTree.ContentId, Is.EqualTo(hostTree.ContentId));
        Assert.That(adaptedTree.Root.DescendantNodes().Select(node => (node.Kind, node.Span)), Is.EqualTo(hostTree.Root.DescendantNodes().Select(node => (node.Kind, node.Span))));
        Assert.That(adaptedTree.Diagnostics.Select(diagnostic => (diagnostic.Code, diagnostic.Severity, diagnostic.Span)), Is.EqualTo(hostTree.Diagnostics.Select(diagnostic => (diagnostic.Code, diagnostic.Severity, diagnostic.Span))));

        var buffers = ScannerHost.CreateBuffers(SourceText.From(source))
            .Add(new HostBuffer("Expected", PrimitiveType.Int32, expected.ToImmutable(), false))
            .Add(new HostBuffer("ExpectedTrivia", PrimitiveType.Int32, expectedTrivia.ToImmutable(), false))
            .Add(new HostBuffer("ExpectedDiagnostics", PrimitiveType.Int32, expectedDiagnostics.ToImmutable(), false))
            .Add(new HostBuffer("ExpectedIntegers", PrimitiveType.Int32, expectedIntegers.ToImmutable(), false));
        const string Harness = """

            int32 CheckScan()
            {
                int32 count = Scan();
                if (count * 3 != Host.Expected.Length)
                {
                    return 100;
                }
                mut int32 index = 0;
                while (index < count * 3)
                {
                    if (Host.Tokens[index] != Host.Expected[index])
                    {
                        return 101;
                    }
                    index = index + 1;
                }
                if (Host.Trivia[0] * 4 != Host.ExpectedTrivia.Length)
                {
                    return 104;
                }
                index = 0;
                while (index < Host.ExpectedTrivia.Length)
                {
                    if (Host.Trivia[index + 1] != Host.ExpectedTrivia[index])
                    {
                        return 105;
                    }
                    index = index + 1;
                }
                if (Host.IntegerValues[0] * 5 != Host.ExpectedIntegers.Length)
                {
                    return 106;
                }
                index = 0;
                while (index < Host.ExpectedIntegers.Length)
                {
                    if (Host.IntegerValues[index + 1] != Host.ExpectedIntegers[index])
                    {
                        return 107;
                    }
                    index = index + 1;
                }
                if (Host.Diagnostics[0] * 3 != Host.ExpectedDiagnostics.Length)
                {
                    return 102;
                }
                index = 0;
                while (index < Host.ExpectedDiagnostics.Length)
                {
                    if (Host.Diagnostics[index + 1] != Host.ExpectedDiagnostics[index])
                    {
                        return 103;
                    }
                    index = index + 1;
                }
                return 0;
            }
            """;
        const string Once = "int32 Main() { return CheckScan(); }";
        const string Twice = """

            int32 Main()
            {
                int32 first = CheckScan();
                if (first != 0)
                {
                    return first;
                }

                return CheckScan();
            }
            """;
        var compilation = Compilation.Create(SyntaxTree.Parse(scanner + Harness + (repeat ? Twice : Once)), buffers);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        await ExecuteAsync(((global::System.Func<BoundProgram>)(() => { var __nunitActual4 = compilation.Program; Assert.That(__nunitActual4, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual4!; }))(), 0).ConfigureAwait(true);
    }

    internal static async Task<string> ReadScannerSourceAsync(string root)
    {
        var selfHostDirectory = SelfHostPaths.SourceDirectory(root);
        var sourceRange = await File.ReadAllTextAsync(Path.Combine(selfHostDirectory, "SourceRange.terrum"))
            .ConfigureAwait(true);
        var characterFacts = await File.ReadAllTextAsync(Path.Combine(selfHostDirectory, "CharacterFacts.terrum"))
            .ConfigureAwait(true);
        var utf16CodeUnits = await File.ReadAllTextAsync(Path.Combine(selfHostDirectory, "Utf16CodeUnits.terrum"))
            .ConfigureAwait(true);
        var utf16Scalar = await File.ReadAllTextAsync(Path.Combine(selfHostDirectory, "Utf16Scalar.terrum"))
            .ConfigureAwait(true);
        var characterLiteral = await File.ReadAllTextAsync(Path.Combine(selfHostDirectory, "CharacterLiteral.terrum"))
            .ConfigureAwait(true);
        var stringEscape = await File.ReadAllTextAsync(Path.Combine(selfHostDirectory, "StringEscape.terrum"))
            .ConfigureAwait(true);
        var scanner = await File.ReadAllTextAsync(Path.Combine(selfHostDirectory, "Scanner.terrum"))
            .ConfigureAwait(true);
        return sourceRange + RemoveFileScopedNamespace(characterFacts)
            + RemoveFileScopedNamespace(utf16CodeUnits)
            + RemoveFileScopedNamespace(utf16Scalar)
            + RemoveFileScopedNamespace(characterLiteral)
            + RemoveFileScopedNamespace(stringEscape) + RemoveFileScopedNamespace(scanner);
    }

    private static string RemoveFileScopedNamespace(string source)
    {
        const string NamespaceDeclaration = "namespace Terrum.CodeAnalysis;";
        Assert.That(source, Does.StartWith(NamespaceDeclaration));
        var lineEnd = source.IndexOfAny(['\r', '\n']);
        if (lineEnd < 0)
        {
            return string.Empty;
        }

        if (source[lineEnd] == '\r' && lineEnd + 1 < source.Length && source[lineEnd + 1] == '\n')
        {
            lineEnd++;
        }
        return source[(lineEnd + 1)..];
    }

    private static byte[] EncodeWords(ImmutableArray<int> words)
    {
        var bytes = new byte[words.Length * 4];
        for (var index = 0; index < words.Length; index++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(index * 4), words[index]);
        }
        return bytes;
    }

    private static async Task ExecuteAsync(BoundProgram program, int expected)
    {
        var image = NativeEmitter.Emit(program, NativeRuntimePolicy.FailFast);
        var directory = TestArtifactPaths.CreateDirectory("scanner-tests");
        var path = Path.Combine(directory, $"terrum-scanner-{Guid.NewGuid():N}.exe");
        try
        {
            await File.WriteAllBytesAsync(path, image).ConfigureAwait(true);
            using var process = Process.Start(new ProcessStartInfo(path) { UseShellExecute = false, CreateNoWindow = true })
                ?? throw new InvalidOperationException("Could not start compiled scanner.");
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

            Assert.That(process.ExitCode, Is.EqualTo(expected));
        }
        finally
        {
            await TestFileCleanup.DeleteWithRetryAsync(path).ConfigureAwait(true);
        }
    }
}
