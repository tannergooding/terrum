// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Globalization;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Lexing;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Terrum.CodeAnalysis.Text;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed class ScannerTests
{
    [Theory]
    [InlineData(PrimitiveType.Int64)]
    [InlineData(PrimitiveType.UInt64)]
    public void WideValuesDoNotImplyWideHostBufferStorage(PrimitiveType type)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("int32 Main() { return 0; }"),
            [new HostBuffer("Wide", type, [0], true)]);
        Assert.True(compilation.HasErrors);
        Assert.Null(compilation.Program);
    }

    [Theory]
    [InlineData("void Main() { Host.Source[0] = (char16)65; }")]
    [InlineData("int32 Main() { return Host.Missing[0]; }")]
    [InlineData("int32 Main() { return (int32)Host.Source[true]; }")]
    [InlineData("void Main() { var borrowed = Host.Source; }")]
    [InlineData("int32 Main() { Host.Tokens.Length = 1; return 0; }")]
    [InlineData("int32 Main() { Host.Tokens[0] = true; return 0; }")]
    public void BufferCapabilitiesRejectInvalidAccessAndEscape(string source)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(source), ScannerHost.CreateBuffers(SourceText.From("x")));
        Assert.True(compilation.HasErrors);
        Assert.Null(compilation.Program);
    }

    [Fact]
    public void NamedBuffersRequireExplicitHostGrants()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("int32 Main() { return Host.Source.Length; }"));
        Assert.True(compilation.HasErrors);
        Assert.Null(compilation.Program);
    }

    [Theory]
    [InlineData("int32 Main() { return Host.State[-1]; }", unchecked((int)0xE0540004))]
    [InlineData("int32 Main() { return Host.State[2]; }", unchecked((int)0xE0540004))]
    [InlineData("int32 Main() { return Host.State[^0]; }", unchecked((int)0xE0540004))]
    [InlineData("int32 Main() { return Host.State[^3]; }", unchecked((int)0xE0540004))]
    [InlineData("int32 Main() { return Host.State[^(-1)]; }", unchecked((int)0xE0540004))]
    [InlineData("int32 Main() { Host.State[^1] = 42; return Host.State[^1]; }", 42)]
    [InlineData("int32 Main() { const int32 last = 1; Host.State[^last] = 42; return Host.State[1]; }", 42)]
    [InlineData("int32 Offset() { Host.State[0] = Host.State[0] + 1; return 1; } int32 Value() { Host.State[0] = Host.State[0] + 1; return 40; } int32 Main() { Host.State[^Offset()] = Value(); return Host.State[^Offset()] + Host.State[0]; }", 43)]
    [InlineData("int32 Offset() { return 0; } int32 Main() { Host.State[1] = 42; return Host.State[^(Offset() + 1)]; }", 42)]
    [InlineData("int32 Offset() { Host.State[0] = Host.State[0] + 1; return 1; } int32 Main() { Host.State[1] = 40; Host.State[^Offset()] += 2; return Host.State[1] + Host.State[0]; }", 43)]
    [InlineData("int32 Main() { Host.State[2] = Fail(); return 0; } int32 Fail() { require false; return 0; }", unchecked((int)0xE0540004))]
    [InlineData("int32 Offset() { return -1; } int32 Main() { return Host.State[^Offset()]; }", unchecked((int)0xE0540004))]
    [InlineData("int32 Offset() { return 0; } int32 Main() { return Host.State[^Offset()]; }", unchecked((int)0xE0540004))]
    [InlineData("int32 Offset() { return 2; } int32 Main() { return Host.State[^Offset()]; }", unchecked((int)0xE0540004))]
    [InlineData("int32 Main() { Host.State[Index()] = Value(); return Host.State[0] * 10 + Host.State[1]; } int32 Index() { Host.State[0] = Host.State[0] + 1; return 1; } int32 Value() { return Host.State[0] + 6; }", 17)]
    public async Task BufferAccessChecksBoundsAndPreservesEvaluationOrderAsync(string source, int expected)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(source),
            [new HostBuffer("State", PrimitiveType.Int32, [0, 0], true)]);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        await ExecuteAsync(Assert.IsType<BoundProgram>(compilation.Program), expected).ConfigureAwait(true);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" \t\r\n\u0085\u2028\u2029\u00A0\u3000")]
    [InlineData("alpha _value 0123 + (beta != 7) >> >= && & || | == =")]
    [InlineData("a??b a??=b T?? T??? ? ?: ??= ??")]
    [InlineData("++count count-- count++ + ++count count-- - --count")]
    [InlineData("int32 F(int32 x) => x == 0; => = > ==> /* arrow */ =>")]
    [InlineData("_ _name __ _123 _/* trivia */=int32(42);")]
    [InlineData("\U00010400_var \u2118 \u212E \u2122 \u0301")]
    [InlineData("a\u200Cb a\u200Db \u200C \u200D \u0661 a\u06612")]
    [InlineData("a\u00E9 ae\u0301 \U0001F600x")]
    [InlineData("0x1.fp+2 0x_1_f.0_fP+1_0 0x1p-4 0x1__p2 0x1._p2 0x1p2__")]
    [InlineData("if while require int32 2147483648")]
    [InlineData("abstract operator where abstractly operator_ where2 G<T( ;")]
    [InlineData("// trailing comment")]
    [InlineData("alpha/* inner // ignored */+// line\r\n\"quoted\\ntext\"")]
    [InlineData("1.25e-10f64 2e+3 3i128 4u8 0.5 6. 7efoo 8i32extra")]
    [InlineData("0i8 0i16 0i32 0i64 0i128 0u8 0u16 0u32 0u64 0u128 0f16 0f32 0f64 0f128")]
    [InlineData("1f8 2u128x 3i64\u200D 4f128\uD800 5E+6f32 7e+8 9e-0 10e\u00E9")]
    [InlineData("1e 2e+ 3E-; 4e. 5e_ 6e\uD800 7e+f64")]
    [InlineData("\"\" \"\\u0041\" \"\\U0001F600\" \"\\\"\" \"\\\\\"")]
    [InlineData("\"\\uD800\" \"\\U00110000\" \"\\u12x\" \"\\q\"")]
    [InlineData("\"\\UFFFFFFFF\" \"\\U0000DFFF\" \"\\U0010FFFF\" \"\\uFFFF\" \"\\u0000\"")]
    [InlineData("\"\\U1234567\" \"\\u\" \"\\z\uD800\" \"\uDC00\"")]
    [InlineData("\"bad\uD800\uDC00\uD800\" //\uDC00\r\n/*\uD800*/")]
    [InlineData("\"unterminated\nsecond \"ok\"")]
    [InlineData("\"incomplete\\\r\nnext")]
    [InlineData("/* unterminated \uD800")]
    [InlineData("/*a*/ / /*b*/ * / //\U0001F600\uDC00\nx")]
    public async Task CompiledScannerAgreesWithHostOnTokenBoundariesAndDiagnosticsAsync(string source)
    {
        await CheckScannerAsync(source).ConfigureAwait(true);
    }

    [Fact]
    public async Task MalformedSurrogatesRetainExactCodeUnitBoundariesAsync()
    {
        await CheckScannerAsync("\uD800x \uDC00 \uD800\uD800 \uDC00\uDC00 \U00010400").ConfigureAwait(true);
    }

    [Fact]
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

    [Fact]
    public async Task CharacterLiteralsRetainTokenKindsAndDiagnosticParityAsync()
    {
        await CheckScannerAsync("'A' '\\'' '\\u0041' '\\U0001F600' '😀' '' 'ab' '\\q' '\\uD800' 'unterminated\nnext")
            .ConfigureAwait(true);
    }

    [Fact]
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

    [Fact]
    public async Task StringEscapeDiagnosticsMatchHostedBoundariesAsync()
    {
        string[] literals = [
            "\"\\n\"", "\"\\r\"", "\"\\t\"", "\"\\\\\"", "\"\\\"\"", "\"\\0\"",
            "\"\\u0000\"", "\"\\u0041\"", "\"\\uD7FF\"", "\"\\uE000\"", "\"\\uFFFF\"",
            "\"\\U00000000\"", "\"\\U0001F600\"", "\"\\U0010FFFF\"",
            "\"\\u\"", "\"\\u004G\"", "\"\\uD800\"", "\"\\U0000D800\"",
            "\"\\U00110000\"", "\"\\U1234567\"", "\"\\q\"", "\"\\u0041\\q\"",
            "\"\\q\\U00110000\"", "\"\\q\uD800\"",
            "\"\uD800\"", "\"\uDC00\"",
        ];
        await CheckScannerAsync(string.Join(' ', literals) + " \"unterminated\\")
            .ConfigureAwait(true);
    }

    [Fact]
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
        Assert.Equal(keywords.Select(keyword => keyword.Kind).Append(TokenKind.Identifier).Append(TokenKind.Identifier)
            .Append(TokenKind.EndOfFile), lexed.Tokens.Select(token => token.Kind));
        await CheckScannerAsync(source).ConfigureAwait(true);
    }

    [Fact]
    public void TokenKindsAppendWithoutRenumberingPublishedKinds()
    {
        Assert.Equal(24, (int)TokenKind.InterfaceKeyword);
        Assert.Equal(76, (int)TokenKind.UnsafeKeyword);
        Assert.Equal(77, (int)TokenKind.AbstractKeyword);
        Assert.Equal(78, (int)TokenKind.OperatorKeyword);
        Assert.Equal(79, (int)TokenKind.WhereKeyword);
        Assert.Equal(102, (int)TokenKind.AliasKeyword);
        Assert.Equal(103, (int)TokenKind.CharacterLiteral);
        Assert.Equal(104, (int)TokenKind.DotDot);
        Assert.Equal(105, (int)TokenKind.DotDotCaret);
        Assert.Equal(106, (int)TokenKind.QuestionDot);
    }

    [Fact]
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
            Assert.Contains(kind, actual);
        }

        await CheckScannerAsync(Operators).ConfigureAwait(true);
        await CheckScannerAsync(" \t\u00A0\u1680\u2000\u202F\u205F\u3000\n\r\r\n\u0085\u2028\u2029// line\n/* block */"
            + "x \t// eof", repeat: true).ConfigureAwait(true);
        await CheckScannerAsync("/* unterminated \uDC00").ConfigureAwait(true);
    }

    [Fact]
    public async Task ExtendedStaticProtocolSampleAgreesWithCompiledScannerAsync()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
        var source = await File.ReadAllTextAsync(Path.Combine(root, "samples", "native",
            "static-protocol-binary.terrum")).ConfigureAwait(true);
        await CheckScannerAsync(source).ConfigureAwait(true);
    }

    [Fact]
    public Task SelfBoundStaticProtocolAgreesWithCompiledScannerAsync() =>
        CheckScannerAsync(StaticProtocolSelfTests.Source);

    [Fact]
    public async Task MultipleTypeParameterSampleAgreesWithCompiledScannerAsync()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
        var source = await File.ReadAllTextAsync(Path.Combine(root, "samples", "native",
            "multi-type-generics.terrum")).ConfigureAwait(true);
        await CheckScannerAsync(source).ConfigureAwait(true);
    }

    [Fact]
    public async Task ManagedGenericSampleAgreesWithCompiledScannerAsync()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
        var source = await File.ReadAllTextAsync(Path.Combine(root, "samples", "native",
            "managed-generic-selection.terrum")).ConfigureAwait(true);
        await CheckScannerAsync(source).ConfigureAwait(true);
    }

    [Fact]
    public async Task ComparisonProtocolSampleAgreesWithCompiledScannerAsync()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
        var source = await File.ReadAllTextAsync(Path.Combine(root, "samples", "native",
            "static-protocol-comparison.terrum")).ConfigureAwait(true);
        await CheckScannerAsync(source).ConfigureAwait(true);
    }

    [Fact]
    public async Task DiagnosticAndTokenBuffersHandleDenseInvalidInputAsync()
    {
        await CheckScannerAsync(new string('\uDC00', 256)).ConfigureAwait(true);
    }

    [Fact]
    public async Task TriviaBufferHandlesDenseAlternatingRunsAsync()
    {
        await CheckScannerAsync(string.Concat(Enumerable.Repeat(" \n", 512))).ConfigureAwait(true);
    }

    [Theory]
    [InlineData("0 00 000000000000000000000000000000000000000001 256u8")]
    [InlineData("9223372036854775807 9223372036854775808i64 -9223372036854775808i64")]
    [InlineData("18446744073709551615 18446744073709551615u64 18446744073709551616")]
    [InlineData("0x0 0X2A 0x1f32 0XFFFFFFFFFFFFFFFF 0x10000000000000000")]
    [InlineData("0x 0X 0xG 0x1g 0x1u32 0x1f32 0x1_2 0x1.2 0x1p2")]
    [InlineData("0b0 0B1 0b101010 0B11111111111111111111111111111111 0b000000000000000000000000000000000000000000000000000000000000000001")]
    [InlineData("0b 0B2 0b102 0b1u32 0b1f32 0b1_0 0b1.1 0b1p2")]
    [InlineData("1_000 1__2 0x_FFFF_FFFF_FFFF_FFFF 0b___1010 0b_11111111_11111111_11111111_11111111")]
    [InlineData("1_ 0x___ 0b___ 1_.2 1.2_3e+4_5 1.2_ 1e+2_3 1e+2_ 0b1_ 0xF__ 1_000i32 0b_1f32 0x_F_Fi32")]
    [InlineData("0x_1_0000_0000_0000_0000 0b_1_0000000000000000000000000000000000000000000000000000000000000000")]
    [InlineData("18_446_744_073_709_551_615 18_446_744_073_709_551_616 1_foo 0xF_tail 0b1_tail")]
    [InlineData("18446744073709551616i128 340282366920938463463374607431768211455u128")]
    [InlineData("1i8 2i16 3i32 4i64 5i128 6u8 7u16 8u32 9u64 10u128")]
    [InlineData("1.25 2e+3 3E- 4e 5f32 6f128 7efoo 8i64extra 9i128\u200D")]
    [InlineData("12345678901234567890123456789012345678901234567890u128 // trailing")]
    public async Task CompiledIntegerDecoderMatchesBigIntegerOracleAsync(string source)
    {
        await CheckScannerAsync(source, repeat: true).ConfigureAwait(true);
    }

    [Fact]
    public async Task RepeatedScanReplacesPublishedDiagnosticsAsync()
    {
        await CheckScannerAsync("\"\\q\" /*\uD800*/ 1e+ \uDC00", repeat: true).ConfigureAwait(true);
    }

    [Fact]
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
    }

    [Fact]
    public void HostBufferInputPreservesMalformedUnitsAndRejectsResourceExcess()
    {
        var buffers = ScannerHost.CreateBuffers(SourceText.From("\uD800\0\uDC00"));
        Assert.Equal<int>([0xD800, 0, 0xDC00], buffers[0].Values);
        Assert.Equal(UnicodeIdentifierProfile.StartRanges.ToArray(), buffers[1].Values.ToArray());
        Assert.Equal(UnicodeIdentifierProfile.ContinueRanges.ToArray(), buffers[2].Values.ToArray());
        Assert.False(buffers[0].IsWritable);
        Assert.True(buffers[3].IsWritable);
        Assert.True(buffers[4].IsWritable);
        Assert.True(buffers[5].IsWritable);
        Assert.False(buffers[6].IsWritable);
        Assert.False(buffers[7].IsWritable);
        Assert.True(buffers[8].IsWritable);
        Assert.Equal(1 + (4 * 3), buffers[5].Values.Length);
        Assert.Equal(1 + (5 * 3), buffers[8].Values.Length);
        _ = Assert.Throws<ArgumentException>(() => ScannerHost.CreateBuffers(
            SourceText.From(new string('x', ScannerHost.MaximumSourceLength + 1))));
    }

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(int.MaxValue, 0, 1)]
    [InlineData(1, -1, 1)]
    [InlineData(1, 0, 2)]
    [InlineData(1, 0, 0)]
    public void ResultAdapterRejectsInvalidPublishedRecords(int kind, int start, int length)
    {
        var bytes = new byte[24];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, kind);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), start);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8), length);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(16), 1);
        _ = Assert.Throws<ArgumentException>(() => ScannerHost.ReadTokens(SourceText.From("x"), 2, bytes));
    }

    [Theory]
    [InlineData(2, 0, 0, 1, 0)]
    [InlineData(1, 6, 0, 1, 0)]
    [InlineData(1, 0, 1, 0, 0)]
    [InlineData(1, 0, 0, 3, 0)]
    [InlineData(1, 0, 0, 2, 0)]
    [InlineData(1, 0, 0, 1, 1)]
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
        _ = Assert.Throws<ArgumentException>(() => ScannerHost.ReadTrivia(source, tokens, bytes));
    }

    [Theory]
    [InlineData(2, 1, 0, 1)]
    [InlineData(1, 0, 0, 1)]
    [InlineData(1, 13, 0, 1)]
    [InlineData(1, 1, -1, 1)]
    [InlineData(1, 1, 0, 0)]
    [InlineData(1, 1, 0, 2)]
    public void ResultAdapterRejectsInvalidDiagnosticRecords(int count, int code, int start, int length)
    {
        var bytes = new byte[16];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, count);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), code);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8), start);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(12), length);
        _ = Assert.Throws<ArgumentException>(() => ScannerHost.ReadDiagnostics(SourceText.From("x"), bytes));
    }

    [Fact]
    public void ResultAdapterAcceptsStringRecordsAndLexicalDiagnostics()
    {
        var bytes = new byte[24];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, (int)TokenKind.StringLiteral);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8), 1);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(16), 1);
        Assert.Equal(TokenKind.StringLiteral, ScannerHost.ReadTokens(SourceText.From("\""), 2, bytes)[0].Kind);

        var diagnostics = new byte[16];
        BinaryPrimitives.WriteInt32LittleEndian(diagnostics, 1);
        BinaryPrimitives.WriteInt32LittleEndian(diagnostics.AsSpan(4), 2);
        BinaryPrimitives.WriteInt32LittleEndian(diagnostics.AsSpan(12), 1);
        Assert.Equal(new ScannerDiagnostic("TRM0002", new TextSpan(0, 1)),
            ScannerHost.ReadDiagnostics(SourceText.From("\""), diagnostics)[0]);
    }

    [Fact]
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
        Assert.Equal<ScannerTrivia>(
            [new ScannerTrivia(TriviaKind.Whitespace, new TextSpan(1, 1), 1),
                new ScannerTrivia(TriviaKind.MultiLineComment, new TextSpan(2, 7), 1)],
            ScannerHost.ReadTrivia(source, tokens, bytes));
    }

    [Theory]
    [InlineData(-1, 0, 0, 0, 1)]
    [InlineData(1, 0, 0, 0, 1)]
    [InlineData(0, 2, 0, 0, 1)]
    [InlineData(0, 1, 1, 0, 1)]
    [InlineData(0, 0, 0, 0, 6)]
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
        _ = Assert.Throws<ArgumentException>(() => ScannerHost.ReadIntegerValues(source, tokens, bytes));
    }

    [Theory]
    [InlineData("2147483648", int.MinValue, 0, 2147483648UL)]
    [InlineData("9223372036854775808", 0, int.MinValue, 9223372036854775808UL)]
    [InlineData("18446744073709551615", -1, -1, ulong.MaxValue)]
    [InlineData("0xFFFFFFFFFFFFFFFF", -1, -1, ulong.MaxValue)]
    [InlineData("0X8000000000000000", 0, int.MinValue, 9223372036854775808UL)]
    [InlineData("0b10000000000000000000000000000000", int.MinValue, 0, 2147483648UL)]
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
        Assert.Equal(new ScannerIntegerValue(0, "", false, expected),
            Assert.Single(ScannerHost.ReadIntegerValues(source, tokens, bytes)));
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
        Assert.Equal(source, reconstructed.ToString());

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
        Assert.Equal<ScannerIntegerValue>(integerValues,
            ScannerHost.ReadIntegerValues(lexed.Source, records, integerBytes));
        Assert.Equal<ScannerIntegerValue>(integerValues,
            ScannerHost.ReadIntegerValues(lexed.Source, lexed.Tokens, integerBytes));

        var adapted = ScannerHost.ReadLexResult(lexed.Source, lexed.Tokens.Length,
            EncodeWords(expected.ToImmutable()),
            EncodeWords(expectedTrivia.ToImmutable().Insert(0, expectedTrivia.Count / 4)),
            EncodeWords(expectedDiagnostics.ToImmutable().Insert(0, expectedDiagnostics.Count / 3)));
        var hostTree = SyntaxTree.Parse(lexed);
        var adaptedTree = SyntaxTree.Parse(adapted);
        Assert.Same(lexed.Source, adaptedTree.Source);
        Assert.Equal(source, adaptedTree.Root.ToFullString());
        Assert.Equal(hostTree.ContentId, adaptedTree.ContentId);
        Assert.Equal(hostTree.Root.DescendantNodes().Select(node => (node.Kind, node.Span)),
            adaptedTree.Root.DescendantNodes().Select(node => (node.Kind, node.Span)));
        Assert.Equal(hostTree.Diagnostics.Select(diagnostic => (diagnostic.Code, diagnostic.Severity, diagnostic.Span)),
            adaptedTree.Diagnostics.Select(diagnostic => (diagnostic.Code, diagnostic.Severity, diagnostic.Span)));

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
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        await ExecuteAsync(Assert.IsType<BoundProgram>(compilation.Program), 0).ConfigureAwait(true);
    }

    internal static async Task<string> ReadScannerSourceAsync(string root)
    {
        var selfHostDirectory = Path.Combine(root, "self-host");
        var sourceRange = await File.ReadAllTextAsync(Path.Combine(selfHostDirectory, "SourceRange.terrum"))
            .ConfigureAwait(true);
        var utf16Scalar = await File.ReadAllTextAsync(Path.Combine(selfHostDirectory, "Utf16Scalar.terrum"))
            .ConfigureAwait(true);
        var characterLiteral = await File.ReadAllTextAsync(Path.Combine(selfHostDirectory, "CharacterLiteral.terrum"))
            .ConfigureAwait(true);
        var stringEscape = await File.ReadAllTextAsync(Path.Combine(selfHostDirectory, "StringEscape.terrum"))
            .ConfigureAwait(true);
        var scanner = await File.ReadAllTextAsync(Path.Combine(selfHostDirectory, "Scanner.terrum"))
            .ConfigureAwait(true);
        return sourceRange + RemoveFileScopedNamespace(utf16Scalar)
            + RemoveFileScopedNamespace(characterLiteral)
            + RemoveFileScopedNamespace(stringEscape) + RemoveFileScopedNamespace(scanner);
    }

    private static string RemoveFileScopedNamespace(string source)
    {
        const string NamespaceDeclaration = "namespace Terrum.CodeAnalysis;";
        Assert.StartsWith(NamespaceDeclaration, source, StringComparison.Ordinal);
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
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
        var directory = Path.Combine(root, "artifacts", "scanner-tests");
        _ = Directory.CreateDirectory(directory);
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

            Assert.Equal(expected, process.ExitCode);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
