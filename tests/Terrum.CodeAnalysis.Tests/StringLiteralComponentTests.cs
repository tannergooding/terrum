// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Generic;
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

public sealed class StringLiteralComponentTests
{
    [Theory]
    [InlineData(NativeRuntimePolicy.FailFast)]
    [InlineData(NativeRuntimePolicy.CheckedExceptions)]
    public async Task CompiledLiteralValidationAndScalarReadsMatchSharedFixturesAsync(NativeRuntimePolicy policy)
    {
        var source = new StringBuilder(" ");
        var literals = ImmutableArray.CreateBuilder<int>();
        var scalars = ImmutableArray.CreateBuilder<int>();
        var utf8Records = ImmutableArray.CreateBuilder<int>();
        var utf8Bytes = ImmutableArray.CreateBuilder<int>();
        var utf16Records = ImmutableArray.CreateBuilder<int>();
        var utf16Units = ImmutableArray.CreateBuilder<int>();
        foreach (var fixture in Fixtures())
        {
            var lexed = Lexer.Lex(SourceText.From(fixture.Text));
            var isSingleValidLiteral = lexed.Diagnostics.IsEmpty && lexed.Tokens.Length == 2
                && lexed.Tokens[0].Kind == TokenKind.StringLiteral
                && lexed.Tokens[0].Span == new TextSpan(0, fixture.Text.Length);
            Assert.Equal(fixture.Scalars is not null, isSingleValidLiteral);
            literals.Add(source.Length);
            literals.Add(fixture.Text.Length);
            literals.Add(fixture.Scalars?.Length ?? -1);
            literals.Add(scalars.Count / 3);
            utf8Records.Add(utf8Bytes.Count);
            utf8Records.Add(fixture.Scalars is null ? -1 : 0);
            utf16Records.Add(utf16Units.Count);
            utf16Records.Add(fixture.Scalars is null ? -1 : 0);
            if (fixture.Scalars is ScalarFixture[] values)
            {
                foreach (var value in values)
                {
                    scalars.Add(source.Length + value.Start);
                    scalars.Add(value.Value);
                    scalars.Add(source.Length + value.End);
                }
                var decoded = string.Concat(values.Select(static value => new Rune(value.Value).ToString()));
                var encoded = Encoding.UTF8.GetBytes(decoded);
                utf8Records[^1] = encoded.Length;
                utf8Bytes.AddRange(encoded.Select(static value => (int)value));
                utf16Records[^1] = decoded.Length;
                utf16Units.AddRange(decoded.Select(static unit => (int)unit));
            }
            _ = source.Append(fixture.Text).Append('\n');
        }

        var trees = await ReadComponentsAsync(includeTests: true).ConfigureAwait(true);
        const string Harness = """
            namespace Terrum.CodeAnalysis;
            int32 Main() {
                int32 first = CheckStringLiterals();
                if (first != 0) { return first; }
                return CheckStringLiterals();
            }
            """;
        var compilation = Compilation.Create(trees.Add(SyntaxTree.Parse(Harness, "entry.terrum")),
        [
            new HostBuffer("Source", PrimitiveType.Char16, [.. source.ToString().Select(unit => (int)unit)], false),
            new HostBuffer("ExpectedLiterals", PrimitiveType.Int32, literals.ToImmutable(), false),
            new HostBuffer("ExpectedScalars", PrimitiveType.Int32, scalars.ToImmutable(), false),
            new HostBuffer("ExpectedUtf8Records", PrimitiveType.Int32, utf8Records.ToImmutable(), false),
            new HostBuffer("ExpectedUtf8Bytes", PrimitiveType.Int32, utf8Bytes.ToImmutable(), false),
            new HostBuffer("ExpectedUtf16Records", PrimitiveType.Int32, utf16Records.ToImmutable(), false),
            new HostBuffer("ExpectedUtf16Units", PrimitiveType.Int32, utf16Units.ToImmutable(), false),
        ], policy == NativeRuntimePolicy.FailFast ? BoundRuntimePolicy.FailFast : BoundRuntimePolicy.CheckedExceptions);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = Assert.IsType<BoundProgram>(compilation.Program);
        Assert.Equal(0, await ExecuteAotAsync(program, policy).ConfigureAwait(true));
        Assert.Equal(0, NativeJit.Execute(program, policy));
    }

    [Theory]
    [InlineData("Utf8", "\"\u00E9\"", 195, 169)]
    [InlineData("Utf16", "\"\uD83D\uDE00\"", 0xD83D, 0xDE00)]
    public async Task LiteralEncodingRejectsUndersizedStorageAndReleasesItOnCheckedFailureAsync(
        string encoding, string spelling, int first, int second)
    {
        var trees = await ReadComponentsAsync(includeTests: false).ConfigureAwait(true);
        var entry = SyntaxTree.Parse($$"""
            namespace Terrum.CodeAnalysis;
            int32 Main() {
                require SourceRange.TryCreate(0, Host.Source.Length, out var spelling);
                require StringLiteral.TryCreate(spelling, out var literal);
                require literal.TryGet{{encoding}}Length(init var unitLength);
                if (unitLength != 2) { return 1; }
                require OwnedInt32Buffer.TryCreate(1, out var small);
                unsafe {
                    try {
                        OwnedInt32Buffer partial = literal.Encode{{encoding}}(move small);
                        return 2;
                    } catch {
                        require OwnedInt32Buffer.TryCreate(unitLength, out var exact);
                        OwnedInt32Buffer encoded = literal.Encode{{encoding}}(move exact);
                        if (encoded.Count != 2 || encoded.Capacity != 2
                            || encoded.Read(0) != {{first}} || encoded.Read(1) != {{second}}) { return 3; }
                        return 0;
                    }
                }
            }
            """, "literal-undersized-entry.terrum");
        var compilation = Compilation.Create(trees.Add(entry),
            [new HostBuffer("Source", PrimitiveType.Char16, [.. spelling.Select(unit => (int)unit)], false)],
            BoundRuntimePolicy.CheckedExceptions);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = Assert.IsType<BoundProgram>(compilation.Program);
        Assert.Equal(0, NativeJit.Execute(program, NativeRuntimePolicy.CheckedExceptions));
        Assert.Equal(0, await ExecuteAotAsync(program, NativeRuntimePolicy.CheckedExceptions).ConfigureAwait(true));
    }

    private static async Task<int> ExecuteAotAsync(BoundProgram program, NativeRuntimePolicy policy)
    {
        var path = Path.Combine(RepositoryRoot(), "artifacts", $"string-literals-{Guid.NewGuid():N}.exe");
        try
        {
            await File.WriteAllBytesAsync(path, NativeEmitter.Emit(program, policy)).ConfigureAwait(true);
            using var process = Process.Start(new ProcessStartInfo(path) { UseShellExecute = false, CreateNoWindow = true })
                ?? throw new InvalidOperationException("Could not start compiled string-literal component.");
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
            return process.ExitCode;
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData("bool success = StringLiteral.TryCreate(range, out var literal); return literal.ScalarCount;")]
    [InlineData("require StringLiteral.TryCreate(range, out var literal); bool success = literal.TryReadScalar(0, out var scalar, out var next); return scalar;")]
    [InlineData("require StringLiteral.TryCreate(range, out var literal); bool success = literal.TryReadScalar(0, out var scalar, out var next); return next;")]
    [InlineData("require StringLiteral.TryCreate(range, out var literal); StringLiteralCursor cursor = StringLiteralCursor.Create(literal); bool success = cursor.TryRead(out var scalar, out var next); return next.Position;")]
    [InlineData("bool success = StringLiteral.TryCreateInit(range, init var literal); return literal.ScalarCount;")]
    [InlineData("require StringLiteral.TryCreate(range, out var literal); bool success = literal.TryReadScalarInit(0, init var scalar, init var next); return scalar;")]
    [InlineData("require StringLiteral.TryCreate(range, out var literal); bool success = literal.TryReadScalarInit(0, init var scalar, init var next); return next;")]
    [InlineData("require StringLiteral.TryCreate(range, out var literal); StringLiteralCursor cursor = StringLiteralCursor.Create(literal); bool success = cursor.TryReadInit(init var scalar, init var next); return next.Position;")]
    public async Task FailedValidationAndReadsDoNotPublishOutputsAsync(string body)
    {
        var trees = await ReadComponentsAsync(includeTests: false).ConfigureAwait(true);
        var entry = SyntaxTree.Parse($$"""
            namespace Terrum.CodeAnalysis;
            int32 Main() {
                require SourceRange.TryCreate(0, 2, out var range);
                {{body}}
            }
            """, "entry.terrum");
        var compilation = Compilation.Create(trees.Add(entry),
            [new HostBuffer("Source", PrimitiveType.Char16, [34, 34], false)]);
        Assert.True(compilation.HasErrors);
        Assert.Null(compilation.Program);
        Assert.Contains(compilation.SourceDiagnostics,
            diagnostic => diagnostic.SyntaxTree == entry && diagnostic.Diagnostic.Code == "TRM3016");
    }

    private static IEnumerable<LiteralFixture> Fixtures()
    {
        yield return Valid();
        yield return Valid(("a", 97), ("\0", 0), ("z", 122));
        yield return Valid(("\t", 9), ("\v", 11), ("\f", 12), ("\u001C", 28), ("\u0661", 1633));
        yield return Valid(("\\n", 10), ("\\r", 13), ("\\t", 9), ("\\\\", 92), ("\\\"", 34), ("\\0", 0));
        yield return Valid(("e", 101), ("\u0301", 769), ("\u00E9", 233));
        yield return Valid(("\uD7FF", 0xD7FF), ("\uE000", 0xE000), ("\uFFFF", 0xFFFF));
        yield return Valid(("\uD800\uDC00", 0x10000), ("\uDBFF\uDFFF", 0x10FFFF));
        yield return Valid(("\u007F", 0x7F), ("\u0080", 0x80), ("\u07FF", 0x7FF),
            ("\u0800", 0x800), ("\uFFFF", 0xFFFF));
        yield return Valid(("\\u0000", 0), ("\\u000A", 10), ("\\u0085", 133), ("\\u2028", 8232));
        yield return Valid(("\\uD7FF", 0xD7FF), ("\\ue000", 0xE000), ("\\uffFf", 0xFFFF));
        yield return Valid(("\\U00000041", 65), ("\\U00010000", 0x10000), ("\\U0010FFFF", 0x10FFFF));
        yield return Valid(("\\u1234", 0x1234), ("5", 53), ("\\U00000001", 1), ("F", 70));
        yield return Valid([.. Enumerable.Repeat(("\\U0001F600", 0x1F600), 64)]);

        foreach (var text in new[]
        {
            "", "\"", "\"unclosed", "plain", "\"a\" \"b\"", "\"a\"suffix",
            "\"\\\"", "\"\\q\"", "\"\\a\"", "\"\\u\"", "\"\\u123\"", "\"\\u12xz\"",
            "\"\\uD800\"", "\"\\uDC00\"", "\"\\uDFFF\"", "\"\\uD800\\uDC00\"",
            "\"\\U00110000\"", "\"\\UFFFFFFFF\"", "\"\\U80000000\"", "\"\\U0000D800\"",
            "\"\\U1234567\"", "\"\\u066\u0661\"", "\"\\\uD800\"", "\"\\\uD800\uDC00\"",
            "\"\uD800\"", "\"\uDC00\"", "\"\uD800x\"", "\"\uDBFF\uDBFF\"",
            "\"\n\"", "\"\r\"", "\"\r\n\"", "\"\u0085\"", "\"\u2028\"", "\"\u2029\"",
        })
        {
            yield return new LiteralFixture(text, null);
        }
    }

    private static LiteralFixture Valid(params (string Text, int Scalar)[] segments)
    {
        var text = new StringBuilder("\"");
        var scalars = new ScalarFixture[segments.Length];
        for (var index = 0; index < segments.Length; index++)
        {
            var (spelling, scalar) = segments[index];
            var start = text.Length;
            _ = text.Append(spelling);
            scalars[index] = new ScalarFixture(start, scalar, text.Length);
        }
        return new LiteralFixture(text.Append('"').ToString(), scalars);
    }

    private static async Task<ImmutableArray<SyntaxTree>> ReadComponentsAsync(bool includeTests)
    {
        var trees = ImmutableArray.CreateBuilder<SyntaxTree>();
        var names = new[] { "SourceRange.terrum", "CharacterFacts.terrum", "StringLiteral.terrum" };
        foreach (var name in includeTests ? [.. names, "StringLiteralTests.terrum"] : names)
        {
            var path = Path.Combine(RepositoryRoot(), "self-host", name);
            var text = await File.ReadAllTextAsync(path).ConfigureAwait(true);
            var tree = SyntaxTree.Parse(text, path);
            Assert.False(tree.HasErrors, string.Join(Environment.NewLine, tree.Diagnostics));
            Assert.Equal(text, tree.Root.ToFullString());
            trees.Add(tree);
        }
        return trees.ToImmutable();
    }

    private static string RepositoryRoot() =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));

    private sealed record LiteralFixture(string Text, ScalarFixture[]? Scalars);
    private sealed record ScalarFixture(int Start, int Value, int End);
}
