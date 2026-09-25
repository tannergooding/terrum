// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Diagnostics;
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

public sealed class ScannerTests
{
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
    [InlineData("int32 Main() { Host.State[2] = Fail(); return 0; } int32 Fail() { require false; return 0; }", unchecked((int)0xE0540004))]
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
    [InlineData("\U00010400_var \u2118 \u212E \u2122 \u0301")]
    [InlineData("a\u200Cb a\u200Db \u200C \u200D \u0661 a\u06612")]
    [InlineData("a\u00E9 ae\u0301 \U0001F600x")]
    [InlineData("if while require int32 2147483648")]
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
    public async Task EveryKeywordClassifiesNativelyAsync()
    {
        var keywords = Enum.GetValues<TokenKind>()
            .Where(kind => kind.ToString().EndsWith("Keyword", StringComparison.Ordinal))
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
    public async Task EveryOperatorAndTriviaFormExecutesNativelyAsync()
    {
        const string Operators = "( ) { } [ ] ; , . : ? + - * / % ! ~ = == != < <= > >= & && | || ^";
        var lexed = Lexer.Lex(SourceText.From(Operators));
        var actual = lexed.Tokens.Select(token => token.Kind).ToHashSet();
        foreach (var kind in Enum.GetValues<TokenKind>())
        {
            if (kind is TokenKind.Bad or TokenKind.Identifier or TokenKind.NumericLiteral or TokenKind.StringLiteral
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
    public async Task DiagnosticAndTokenBuffersHandleDenseInvalidInputAsync()
    {
        await CheckScannerAsync(new string('\uDC00', 256)).ConfigureAwait(true);
    }

    [Fact]
    public async Task TriviaBufferHandlesDenseAlternatingRunsAsync()
    {
        await CheckScannerAsync(string.Concat(Enumerable.Repeat(" \n", 512))).ConfigureAwait(true);
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
        Assert.Equal(1 + (4 * 3), buffers[5].Values.Length);
        _ = Assert.Throws<ArgumentException>(() => ScannerHost.CreateBuffers(
            SourceText.From(new string('x', ScannerHost.MaximumSourceLength + 1))));
    }

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(100, 0, 1)]
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
    [InlineData(1, 4, 0, 1, 0)]
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
    [InlineData(1, 6, 0, 1)]
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

    private static async Task CheckScannerAsync(string source, bool repeat = false)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
        var scanner = await File.ReadAllTextAsync(Path.Combine(root, "self-host", "Scanner.terrum")).ConfigureAwait(true);
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

        var buffers = ScannerHost.CreateBuffers(SourceText.From(source))
            .Add(new HostBuffer("Expected", PrimitiveType.Int32, expected.ToImmutable(), false))
            .Add(new HostBuffer("ExpectedTrivia", PrimitiveType.Int32, expectedTrivia.ToImmutable(), false))
            .Add(new HostBuffer("ExpectedDiagnostics", PrimitiveType.Int32, expectedDiagnostics.ToImmutable(), false));
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
