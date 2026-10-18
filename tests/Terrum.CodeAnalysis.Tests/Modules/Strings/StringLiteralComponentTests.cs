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
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Terrum.CodeAnalysis.Text;
using NUnit.Framework;

using Terrum.CodeAnalysis.Tests.Infrastructure;

namespace Terrum.CodeAnalysis.Tests.Modules.Strings;

public sealed class StringLiteralComponentTests
{
    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public async Task CompiledLiteralValidationAndScalarReadsMatchSharedFixturesAsync(NativeRuntimePolicy policy)
    {
        var source = new StringBuilder(" ");
        var literals = ImmutableArray.CreateBuilder<int>();
        var scalars = ImmutableArray.CreateBuilder<int>();
        var utf8Records = ImmutableArray.CreateBuilder<int>();
        var utf8Bytes = ImmutableArray.CreateBuilder<int>();
        var utf16Records = ImmutableArray.CreateBuilder<int>();
        var utf16Units = ImmutableArray.CreateBuilder<int>();
        var utf16ByteRecords = ImmutableArray.CreateBuilder<int>();
        var utf16Bytes = ImmutableArray.CreateBuilder<int>();
        var utf16BeRecords = ImmutableArray.CreateBuilder<int>();
        var utf16BeBytes = ImmutableArray.CreateBuilder<int>();
        var asciiRecords = ImmutableArray.CreateBuilder<int>();
        var asciiBytes = ImmutableArray.CreateBuilder<int>();
        var latin1Records = ImmutableArray.CreateBuilder<int>();
        var latin1Bytes = ImmutableArray.CreateBuilder<int>();
        var utf32Records = ImmutableArray.CreateBuilder<int>();
        var utf32Bytes = ImmutableArray.CreateBuilder<int>();
        var utf32BeRecords = ImmutableArray.CreateBuilder<int>();
        var utf32BeBytes = ImmutableArray.CreateBuilder<int>();
        var utf16Encoder = new UnicodeEncoding(false, false, true);
        var utf32Encoder = new UTF32Encoding(false, false, true);
        var utf16BeEncoder = new UnicodeEncoding(true, false, true);
        var utf32BeEncoder = new UTF32Encoding(true, false, true);
        foreach (var fixture in Fixtures())
        {
            var lexed = Lexer.Lex(SourceText.From(fixture.Text));
            var isSingleValidLiteral = lexed.Diagnostics.IsEmpty && lexed.Tokens.Length == 2
                && lexed.Tokens[0].Kind == TokenKind.StringLiteral
                && lexed.Tokens[0].Span == new TextSpan(0, fixture.Text.Length);
            Assert.That(isSingleValidLiteral, Is.EqualTo(fixture.Scalars is not null));
            literals.Add(source.Length);
            literals.Add(fixture.Text.Length);
            literals.Add(fixture.Scalars?.Length ?? -1);
            literals.Add(scalars.Count / 3);
            utf8Records.Add(utf8Bytes.Count);
            utf8Records.Add(fixture.Scalars is null ? -1 : 0);
            utf16Records.Add(utf16Units.Count);
            utf16Records.Add(fixture.Scalars is null ? -1 : 0);
            utf16ByteRecords.Add(utf16Bytes.Count);
            utf16ByteRecords.Add(fixture.Scalars is null ? -1 : 0);
            utf16BeRecords.Add(utf16BeBytes.Count);
            utf16BeRecords.Add(fixture.Scalars is null ? -1 : 0);
            asciiRecords.Add(asciiBytes.Count);
            asciiRecords.Add(-1);
            latin1Records.Add(latin1Bytes.Count);
            latin1Records.Add(-1);
            utf32Records.Add(utf32Bytes.Count);
            utf32Records.Add(fixture.Scalars is null ? -1 : 0);
            utf32BeRecords.Add(utf32BeBytes.Count);
            utf32BeRecords.Add(fixture.Scalars is null ? -1 : 0);
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
                var encoded16 = utf16Encoder.GetBytes(decoded);
                utf16ByteRecords[^1] = encoded16.Length;
                utf16Bytes.AddRange(encoded16.Select(static value => (int)value));
                var encoded16Be = utf16BeEncoder.GetBytes(decoded);
                utf16BeRecords[^1] = encoded16Be.Length;
                utf16BeBytes.AddRange(encoded16Be.Select(static value => (int)value));
                if (values.All(static value => value.Value < 128))
                {
                    asciiRecords[^1] = values.Length;
                    asciiBytes.AddRange(values.Select(static value => value.Value));
                }
                if (values.All(static value => value.Value < 256))
                {
                    latin1Records[^1] = values.Length;
                    latin1Bytes.AddRange(values.Select(static value => value.Value));
                }
                var encoded32 = utf32Encoder.GetBytes(decoded);
                utf32Records[^1] = encoded32.Length;
                utf32Bytes.AddRange(encoded32.Select(static value => (int)value));
                var encoded32Be = utf32BeEncoder.GetBytes(decoded);
                utf32BeRecords[^1] = encoded32Be.Length;
                utf32BeBytes.AddRange(encoded32Be.Select(static value => (int)value));
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
            new HostBuffer("ExpectedUtf16ByteRecords", PrimitiveType.Int32, utf16ByteRecords.ToImmutable(), false),
            new HostBuffer("ExpectedUtf16Bytes", PrimitiveType.Int32, utf16Bytes.ToImmutable(), false),
            new HostBuffer("ExpectedUtf16BeRecords", PrimitiveType.Int32, utf16BeRecords.ToImmutable(), false),
            new HostBuffer("ExpectedUtf16BeBytes", PrimitiveType.Int32, utf16BeBytes.ToImmutable(), false),
            new HostBuffer("ExpectedAsciiRecords", PrimitiveType.Int32, asciiRecords.ToImmutable(), false),
            new HostBuffer("ExpectedAsciiBytes", PrimitiveType.Int32, asciiBytes.ToImmutable(), false),
            new HostBuffer("ExpectedLatin1Records", PrimitiveType.Int32, latin1Records.ToImmutable(), false),
            new HostBuffer("ExpectedLatin1Bytes", PrimitiveType.Int32, latin1Bytes.ToImmutable(), false),
            new HostBuffer("ExpectedUtf32Records", PrimitiveType.Int32, utf32Records.ToImmutable(), false),
            new HostBuffer("ExpectedUtf32Bytes", PrimitiveType.Int32, utf32Bytes.ToImmutable(), false),
            new HostBuffer("ExpectedUtf32BeRecords", PrimitiveType.Int32, utf32BeRecords.ToImmutable(), false),
            new HostBuffer("ExpectedUtf32BeBytes", PrimitiveType.Int32, utf32BeBytes.ToImmutable(), false),
        ], policy == NativeRuntimePolicy.FailFast ? BoundRuntimePolicy.FailFast : BoundRuntimePolicy.CheckedExceptions);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual0 = compilation.Program; Assert.That(__nunitActual0, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual0!; }))();
        Assert.That(await ExecuteAotAsync(program, policy).ConfigureAwait(true), Is.EqualTo(0));
        Assert.That(NativeJit.Execute(program, policy), Is.EqualTo(0));
    }
    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public async Task LongAsciiLiteralReadsEveryScalarAsync(NativeRuntimePolicy policy)
    {
        const int ScalarCount = 8192;
        var spelling = "\"" + new string('A', ScalarCount) + "\"";
        var trees = await ReadComponentsAsync(includeTests: false).ConfigureAwait(true);
        var entry = SyntaxTree.Parse("""
            namespace Terrum.CodeAnalysis;
            int32 Main() {
                require SourceRange.TryCreate(0, Host.Source.Length, init var spelling);
                require StringLiteral.TryCreate(spelling, init var literal);
                mut StringLiteralCursor cursor = StringLiteralCursor.Create(literal);
                mut int32 count = 0;
                while (cursor.TryRead(init var scalar, init var next)) {
                    if (scalar != 65) { return 1; }
                    count = count + 1;
                    cursor = next;
                }
                if (count != 8192) { return 2; }
                return 0;
            }
            """, "long-ascii-string-literal-entry.terrum");
        var compilation = Compilation.Create(trees.Add(entry),
            [new HostBuffer("Source", PrimitiveType.Char16, [.. spelling.Select(unit => (int)unit)], false)],
            (BoundRuntimePolicy)policy);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual1 = compilation.Program; Assert.That(__nunitActual1, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual1!; }))();
        Assert.That(NativeJit.Execute(program, policy), Is.EqualTo(0));
        Assert.That(await ExecuteAotAsync(program, policy).ConfigureAwait(true), Is.EqualTo(0));
    }
    [TestCase("TryGetUtf8Length","EncodeUtf8","\"\u00E9\"",new[] { 195, 169 })]
    [TestCase("TryGetUtf16Length","EncodeUtf16","\"\uD83D\uDE00\"",new[] { 0xD83D, 0xDE00 })]
    [TestCase("TryGetUtf16ByteLength","EncodeUtf16LeBytes","\"\uD83D\uDE00\"",new[] { 0x3D, 0xD8, 0, 0xDE })]
    [TestCase("TryGetUtf16ByteLength","EncodeUtf16BeBytes","\"\uD83D\uDE00\"",new[] { 0xD8, 0x3D, 0xDE, 0 })]
    [TestCase("TryGetUtf32ByteLength","EncodeUtf32LeBytes","\"\u00E9\"",new[] { 233, 0, 0, 0 })]
    [TestCase("TryGetUtf32ByteLength","EncodeUtf32BeBytes","\"\uD83D\uDE00\"",new[] { 0, 1, 246, 0 })]
    [TestCase("TryGetLatin1Length","EncodeLatin1","\"\u00E9\"",new[] { 233 })]
    public async Task LiteralEncodingRejectsUndersizedStorageAndReleasesItOnCheckedFailureAsync(
        string measure, string encode, string spelling, int[] expected)
    {
        var trees = await ReadComponentsAsync(includeTests: false).ConfigureAwait(true);
        var checks = string.Join(Environment.NewLine, expected.Select((value, index) =>
            FormattableString.Invariant($"if (encoded.Read({index}) != {value}) {{ return 3; }}")));
        var entry = SyntaxTree.Parse($$"""
            namespace Terrum.CodeAnalysis;
            int32 Main() {
                require SourceRange.TryCreate(0, Host.Source.Length, init var spelling);
                require StringLiteral.TryCreate(spelling, init var literal);
                require literal.{{measure}}(init var unitLength);
                if (unitLength != {{expected.Length}}) { return 1; }
                require OwnedInt32Buffer.TryCreate(unitLength - 1, out var small);
                unsafe {
                    try {
                        OwnedInt32Buffer partial = literal.{{encode}}(move small);
                        return 2;
                    } catch {
                        require OwnedInt32Buffer.TryCreate(unitLength, out var exact);
                        OwnedInt32Buffer encoded = literal.{{encode}}(move exact);
                        if (encoded.Count != unitLength || encoded.Capacity != unitLength) { return 3; }
                        {{checks}}
                        return 0;
                    }
                }
            }
            """, "literal-undersized-entry.terrum");
        var compilation = Compilation.Create(trees.Add(entry),
            [new HostBuffer("Source", PrimitiveType.Char16, [.. spelling.Select(unit => (int)unit)], false)],
            BoundRuntimePolicy.CheckedExceptions);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual2 = compilation.Program; Assert.That(__nunitActual2, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual2!; }))();
        Assert.That(NativeJit.Execute(program, NativeRuntimePolicy.CheckedExceptions), Is.EqualTo(0));
        Assert.That(await ExecuteAotAsync(program, NativeRuntimePolicy.CheckedExceptions).ConfigureAwait(true), Is.EqualTo(0));
    }
    [TestCase("TryGetAsciiLength","EncodeAscii",233)]
    [TestCase("TryGetLatin1Length","EncodeLatin1",256)]
    [TestCase("TryGetLatin1Length","EncodeLatin1",0x1F600)]
    public async Task SingleByteEncodingRejectsUnrepresentableScalarWithoutPublishingPartialOutputAsync(
        string measure, string encode, int scalar)
    {
        var trees = await ReadComponentsAsync(includeTests: false).ConfigureAwait(true);
        var entry = SyntaxTree.Parse($$"""
            namespace Terrum.CodeAnalysis;
            int32 Main() {
                require SourceRange.TryCreate(0, Host.Source.Length, init var spelling);
                require StringLiteral.TryCreate(spelling, init var literal);
                if (literal.{{measure}}(init var length)) { return 1; }
                require OwnedInt32Buffer.TryCreate(2, out var workspace);
                unsafe {
                    try {
                        OwnedInt32Buffer invalid = literal.{{encode}}(move workspace);
                        return 2;
                    } catch {
                        require OwnedInt32Buffer.TryCreate(1, out var created);
                        mut OwnedInt32Buffer recovered = move created;
                        if (!recovered.TryAppend(42) || recovered.Read(0) != 42) { return 3; }
                        return 0;
                    }
                }
            }
            """, "single-byte-unrepresentable-entry.terrum");
        var compilation = Compilation.Create(trees.Add(entry),
            [new HostBuffer("Source", PrimitiveType.Char16,
                [34, 65, .. char.ConvertFromUtf32(scalar).Select(unit => (int)unit), 34], false)],
            BoundRuntimePolicy.CheckedExceptions);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual3 = compilation.Program; Assert.That(__nunitActual3, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual3!; }))();
        Assert.That(NativeJit.Execute(program, NativeRuntimePolicy.CheckedExceptions), Is.EqualTo(0));
        Assert.That(await ExecuteAotAsync(program, NativeRuntimePolicy.CheckedExceptions).ConfigureAwait(true), Is.EqualTo(0));
    }
    [TestCase(NativeRuntimePolicy.FailFast,0,"\"A\"",new[] { 0, 1, 0, 0, 65, 0, 0, 0 })]
    [TestCase(NativeRuntimePolicy.CheckedExceptions,0,"\"A\"",new[] { 0, 1, 0, 0, 65, 0, 0, 0 })]
    [TestCase(NativeRuntimePolicy.FailFast,1,"\"\u00E9\"",new[] { 1, 2, 0, 0, 195, 169, 0, 0 })]
    [TestCase(NativeRuntimePolicy.CheckedExceptions,1,"\"\u00E9\"",new[] { 1, 2, 0, 0, 195, 169, 0, 0 })]
    [TestCase(NativeRuntimePolicy.FailFast,2,"\"\uD83D\uDE00\"",new[] { 2, 2, 0, 0, 61, 216, 0, 222, 0, 0, 0, 0 })]
    [TestCase(NativeRuntimePolicy.CheckedExceptions,2,"\"\uD83D\uDE00\"",new[] { 2, 2, 0, 0, 61, 216, 0, 222, 0, 0, 0, 0 })]
    [TestCase(NativeRuntimePolicy.FailFast,3,"\"\uD83D\uDE00\"",new[] { 3, 1, 0, 0, 0, 246, 1, 0, 0, 0, 0, 0 })]
    [TestCase(NativeRuntimePolicy.CheckedExceptions,3,"\"\uD83D\uDE00\"",new[] { 3, 1, 0, 0, 0, 246, 1, 0, 0, 0, 0, 0 })]
    [TestCase(NativeRuntimePolicy.FailFast,0,"\"\"",new[] { 0, 0, 0, 0, 0, 0, 0, 0 })]
    [TestCase(NativeRuntimePolicy.CheckedExceptions,2,"\"\\0\"",new[] { 2, 1, 0, 0, 0, 0, 0, 0 })]
    public async Task EncodedStringRowsHaveExactCountTerminationAndPaddingAsync(
        NativeRuntimePolicy policy, int flags, string spelling, int[] expected)
    {
        var trees = await ReadComponentsAsync(includeTests: false).ConfigureAwait(true);
        var checks = string.Join(Environment.NewLine, expected.Select((value, index) =>
            FormattableString.Invariant($"if (row.Read({index}) != {value}) {{ return 3; }}")));
        var entry = SyntaxTree.Parse($$"""
            namespace Terrum.CodeAnalysis;
            int32 Main() {
                require SourceRange.TryCreate(0, Host.Source.Length, init var spelling);
                require StringLiteral.TryCreate(spelling, init var literal);
                require TryGetStringRowByteLength(literal, {{flags}}, init var byteLength);
                if (byteLength != {{expected.Length}}) { return 1; }
                require OwnedInt32Buffer.TryCreate(byteLength, out var workspace);
                mut OwnedInt32Buffer row = EncodeStringRow(literal, {{flags}}, move workspace);
                if (row.Count != byteLength || row.Capacity != byteLength) { return 2; }
                {{checks}}
                row = EncodeStringRow(literal, {{flags}}, move row);
                if (row.Count != byteLength || row.Capacity != byteLength) { return 4; }
                return 0;
            }
            """, "string-row-entry.terrum");
        var compilation = Compilation.Create(trees.Add(entry),
            [new HostBuffer("Source", PrimitiveType.Char16, [.. spelling.Select(unit => (int)unit)], false)],
            (BoundRuntimePolicy)policy);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual4 = compilation.Program; Assert.That(__nunitActual4, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual4!; }))();
        Assert.That(NativeJit.Execute(program, policy), Is.EqualTo(0));
        Assert.That(await ExecuteAotAsync(program, policy).ConfigureAwait(true), Is.EqualTo(0));
    }
    [TestCase(NativeRuntimePolicy.FailFast,0,"Name","\"\\u004Eame\"")]
    [TestCase(NativeRuntimePolicy.CheckedExceptions,0,"","\"\"")]
    [TestCase(NativeRuntimePolicy.FailFast,1,"\u00E9\U0001F600","\"\\u00E9\\U0001F600\"")]
    [TestCase(NativeRuntimePolicy.CheckedExceptions,1,"A\u0301","\"A\u0301\"")]
    [TestCase(NativeRuntimePolicy.FailFast,2,"\U0001F600","\"\\U0001F600\"")]
    [TestCase(NativeRuntimePolicy.CheckedExceptions,2,"\u00E9","\"\u00E9\"")]
    [TestCase(NativeRuntimePolicy.FailFast,3,"\u00E9\U0001F600","\"\u00E9\U0001F600\"")]
    [TestCase(NativeRuntimePolicy.CheckedExceptions,3,"","\"\"")]
    public async Task CompiledStringRowsMatchHostWireEncodingAsync(
        NativeRuntimePolicy policy, int encoding, string text, string spelling)
    {
        var section = TrRmStringTableWriter.EncodeTextRows([(text, encoding)]);
        var expected = new TrRmVariableTableIndex(section, 1, 8, 4).GetRow(1).ToArray();
        var checks = string.Join(Environment.NewLine, expected.Select((value, index) =>
            FormattableString.Invariant($"if (row.Read({index}) != {value}) {{ return 2; }}")));
        var trees = await ReadComponentsAsync(includeTests: false).ConfigureAwait(true);
        var entry = SyntaxTree.Parse($$"""
            namespace Terrum.CodeAnalysis;
            int32 Main() {
                require SourceRange.TryCreate(0, Host.Source.Length, init var spelling);
                require StringLiteral.TryCreate(spelling, init var literal);
                require TryGetStringNamespaceRowByteLength(literal, {{encoding}}, init var length);
                if (length != {{expected.Length}}) { return 1; }
                require OwnedInt32Buffer.TryCreate(length, out var workspace);
                OwnedInt32Buffer row = EncodeStringNamespaceRow(literal, {{encoding}}, move workspace);
                {{checks}}
                return 0;
            }
            """, "string-row-host-parity-entry.terrum");
        var compilation = Compilation.Create(trees.Add(entry),
            [new HostBuffer("Source", PrimitiveType.Char16, [.. spelling.Select(unit => (int)unit)], false)],
            (BoundRuntimePolicy)policy);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual5 = compilation.Program; Assert.That(__nunitActual5, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual5!; }))();
        Assert.That(NativeJit.Execute(program, policy), Is.EqualTo(0));
        Assert.That(await ExecuteAotAsync(program, policy).ConfigureAwait(true), Is.EqualTo(0));
    }
    [TestCase(NativeRuntimePolicy.FailFast,0,"\"Name\"",new[] { 0, 4, 0, 0, 78, 97, 109, 101, 0, 0, 0, 0 })]
    [TestCase(NativeRuntimePolicy.CheckedExceptions,0,"\"Name\"",new[] { 0, 4, 0, 0, 78, 97, 109, 101, 0, 0, 0, 0 })]
    [TestCase(NativeRuntimePolicy.FailFast,1,"\"\u00E9\"",new[] { 1, 2, 0, 0, 195, 169, 0, 0 })]
    [TestCase(NativeRuntimePolicy.CheckedExceptions,1,"\"\u00E9\"",new[] { 1, 2, 0, 0, 195, 169, 0, 0 })]
    [TestCase(NativeRuntimePolicy.FailFast,2,"\"\uD83D\uDE00\"",new[] { 2, 2, 0, 0, 0x3D, 0xD8, 0, 0xDE, 0, 0, 0, 0 })]
    [TestCase(NativeRuntimePolicy.CheckedExceptions,2,"\"\uD83D\uDE00\"",new[] { 2, 2, 0, 0, 0x3D, 0xD8, 0, 0xDE, 0, 0, 0, 0 })]
    [TestCase(NativeRuntimePolicy.FailFast,3,"\"\u00E9\"",new[] { 3, 1, 0, 0, 233, 0, 0, 0, 0, 0, 0, 0 })]
    [TestCase(NativeRuntimePolicy.CheckedExceptions,3,"\"\u00E9\"",new[] { 3, 1, 0, 0, 233, 0, 0, 0, 0, 0, 0, 0 })]
    public async Task MetadataNameRowsRequireNonemptyScalarTextAndPreserveEncodingAsync(
        NativeRuntimePolicy policy, int flags, string spelling, int[] expected)
    {
        var trees = await ReadComponentsAsync(includeTests: false).ConfigureAwait(true);
        var checks = string.Join(Environment.NewLine, expected.Select((value, index) =>
            FormattableString.Invariant($"if (row.Read({index}) != {value}) {{ return 3; }}")));
        var entry = SyntaxTree.Parse($$"""
            namespace Terrum.CodeAnalysis;
            int32 Main() {
                require SourceRange.TryCreate(0, Host.Source.Length, init var spelling);
                require StringLiteral.TryCreate(spelling, init var literal);
                require TryGetStringNameRowByteLength(literal, {{flags}}, init var byteLength);
                if (byteLength != {{expected.Length}}) { return 1; }
                require OwnedInt32Buffer.TryCreate(byteLength, out var workspace);
                OwnedInt32Buffer row = EncodeStringNameRow(literal, {{flags}}, move workspace);
                if (row.Count != byteLength) { return 2; }
                {{checks}}
                return 0;
            }
            """, "string-name-row-entry.terrum");
        var compilation = Compilation.Create(trees.Add(entry),
            [new HostBuffer("Source", PrimitiveType.Char16, [.. spelling.Select(unit => (int)unit)], false)],
            (BoundRuntimePolicy)policy);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual6 = compilation.Program; Assert.That(__nunitActual6, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual6!; }))();
        Assert.That(NativeJit.Execute(program, policy), Is.EqualTo(0));
        Assert.That(await ExecuteAotAsync(program, policy).ConfigureAwait(true), Is.EqualTo(0));
    }
    [TestCase(NativeRuntimePolicy.FailFast,false)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions,false)]
    [TestCase(NativeRuntimePolicy.FailFast,true)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions,true)]
    public async Task Utf8StringRowBuildersPublishOnlyCompleteRowsAsync(
        NativeRuntimePolicy policy, bool namespaceRow)
    {
        var trees = await ReadComponentsAsync(includeTests: false).ConfigureAwait(true);
        (string Spelling, int Capacity, int[]? Expected)[] cases = [
            ("\"A\"", 8, [1, 1, 0, 0, 65, 0, 0, 0]),
            ("\"\u00E9\"", 8, [1, 2, 0, 0, 195, 169, 0, 0]),
            ("\"\U0001F600\"", 12, [1, 4, 0, 0, 240, 159, 152, 128, 0, 0, 0, 0]),
            ("\"\"", 8, namespaceRow ? [1, 0, 0, 0, 0, 0, 0, 0] : null),
            ("\"A\\0B\"", 12, null),
            ("\"A\\q\"", 8, null),
            ("A", 8, null),
            ("\"A\"", 7, [1, 1, 0, 0, 65, 0, 0, 0]),
        ];

        var builder = namespaceRow ? "TryBuildStringNamespaceRowUtf8" : "TryBuildStringNameRowUtf8";
        foreach (var (spelling, capacity, expected) in cases)
        {
            var checks = expected is null
                ? string.Empty
                : string.Join(Environment.NewLine, expected.Select((value, index) =>
                    FormattableString.Invariant($"if (row.Read({index}) != {value}) {{ return 3; }}")));
            var expectedLength = expected?.Length ?? 0;
            var verifyRow = $$"""
                    mut OwnedInt32Buffer completed = move row;
                    if (completed.Count != {{expectedLength}} || completed.Capacity != {{expectedLength}}) { return 2; }
                    {{checks.Replace("row.", "completed.")}}
                    """;
            var resultCheck = expected is null
                ? $"if ({builder}(spelling, move workspace, init var row)) {{ return 1; }}"
                : capacity < expectedLength
                    ? $$"""
                        if ({{builder}}(spelling, move workspace, init var row)) { return 1; }
                        require OwnedInt32Buffer.TryCreate({{expectedLength}}, out var retryWorkspace);
                        if (!{{builder}}(spelling, move retryWorkspace, init row)) { return 2; }
                        {{verifyRow}}
                        """
                    : $$"""
                        if (!{{builder}}(spelling, move workspace, init var row)) { return 1; }
                        {{verifyRow}}
                        """;
            var entry = SyntaxTree.Parse($$"""
                namespace Terrum.CodeAnalysis;
                int32 Main() {
                    require SourceRange.TryCreate(0, Host.Source.Length, init var spelling);
                    require OwnedInt32Buffer.TryCreate({{capacity}}, out var workspace);
                    {{resultCheck}}
                    return 0;
                }
                """, "string-name-row-builder-entry.terrum");
            var compilation = Compilation.Create(trees.Add(entry),
                [new HostBuffer("Source", PrimitiveType.Char16, [.. spelling.Select(unit => (int)unit)], false)],
                (BoundRuntimePolicy)policy);
            Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
            var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual7 = compilation.Program; Assert.That(__nunitActual7, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual7!; }))();
            Assert.That(NativeJit.Execute(program, policy), Is.EqualTo(0));
            Assert.That(await ExecuteAotAsync(program, policy).ConfigureAwait(true), Is.EqualTo(0));
        }
    }
    [TestCase(NativeRuntimePolicy.FailFast,false)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions,false)]
    [TestCase(NativeRuntimePolicy.FailFast,true)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions,true)]
    public async Task Utf8StringRowBuildersHandleMaximumExactCapacityAsync(
        NativeRuntimePolicy policy, bool namespaceRow)
    {
        const int maximumSourceLength = 16_384;
        const int expectedRowLength = 49_152;
        var text = new string('\u0800', maximumSourceLength - 2);
        var spelling = "\"" + text + "\"";
        var expectedSection = namespaceRow
            ? TrRmStringTableWriter.EncodeTextRows([(text, 1)])
            : TrRmStringTableWriter.EncodeNames([(text, 1)]);
        var expectedRow = new TrRmVariableTableIndex(expectedSection, 1, 8, 4).GetRow(1).ToArray();
        Assert.That(spelling.Length, Is.EqualTo(maximumSourceLength));
        Assert.That(expectedRow.Length, Is.EqualTo(expectedRowLength));

        var trees = await ReadComponentsAsync(includeTests: false).ConfigureAwait(true);
        var builder = namespaceRow ? "TryBuildStringNamespaceRowUtf8" : "TryBuildStringNameRowUtf8";
        var entry = SyntaxTree.Parse($$"""
            namespace Terrum.CodeAnalysis;
            int32 Main() {
                if (Host.Expected.Length != {{expectedRowLength}}) { return 1; }
                require SourceRange.TryCreate(0, Host.Source.Length, init var spelling);
                require OwnedInt32Buffer.TryCreate({{expectedRowLength - 1}}, out var undersized);
                if ({{builder}}(spelling, move undersized, init var row)) { return 2; }
                require OwnedInt32Buffer.TryCreate({{expectedRowLength}}, out var workspace);
                if (!{{builder}}(spelling, move workspace, init row)) { return 3; }
                if (row.Count != {{expectedRowLength}} || row.Capacity != {{expectedRowLength}}) { return 4; }
                for (mut int32 index = 0; index < row.Count; index = index + 1) {
                    if (row.Read(index) != Host.Expected[index]) { return 5; }
                }
                return 0;
            }
            """, "maximum-utf8-string-row-builder-entry.terrum");
        var compilation = Compilation.Create(trees.Add(entry),
        [
            new HostBuffer("Source", PrimitiveType.Char16, [.. spelling.Select(unit => (int)unit)], false),
            new HostBuffer("Expected", PrimitiveType.Int32, [.. expectedRow.Select(static value => (int)value)], false),
        ], (BoundRuntimePolicy)policy);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual8 = compilation.Program; Assert.That(__nunitActual8, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual8!; }))();
        Assert.That(NativeJit.Execute(program, policy), Is.EqualTo(0));
        Assert.That(await ExecuteAotAsync(program, policy).ConfigureAwait(true), Is.EqualTo(0));
    }
    [TestCase("\"\"")]
    [TestCase("\"\\0\"")]
    [TestCase("\"A\\0B\"")]
    public async Task MetadataNameRowsRejectEmptyAndEmbeddedZeroScalarsAsync(string spelling)
    {
        var trees = await ReadComponentsAsync(includeTests: false).ConfigureAwait(true);
        var entry = SyntaxTree.Parse("""
            namespace Terrum.CodeAnalysis;
            int32 Main() {
                require SourceRange.TryCreate(0, Host.Source.Length, init var spelling);
                require StringLiteral.TryCreate(spelling, init var literal);
                if (TryGetStringNameRowByteLength(literal, 0, init var ascii)
                    || TryGetStringNameRowByteLength(literal, 1, init var utf8)
                    || TryGetStringNameRowByteLength(literal, 2, init var utf16)
                    || TryGetStringNameRowByteLength(literal, 3, init var utf32)) { return 1; }
                return 0;
            }
            """, "invalid-name-row-entry.terrum");
        var compilation = Compilation.Create(trees.Add(entry),
            [new HostBuffer("Source", PrimitiveType.Char16, [.. spelling.Select(unit => (int)unit)], false)],
            BoundRuntimePolicy.CheckedExceptions);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual9 = compilation.Program; Assert.That(__nunitActual9, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual9!; }))();
        Assert.That(NativeJit.Execute(program, NativeRuntimePolicy.CheckedExceptions), Is.EqualTo(0));
        Assert.That(await ExecuteAotAsync(program, NativeRuntimePolicy.CheckedExceptions).ConfigureAwait(true), Is.EqualTo(0));
    }
    [TestCase(NativeRuntimePolicy.FailFast,0)]
    [TestCase(NativeRuntimePolicy.FailFast,1)]
    [TestCase(NativeRuntimePolicy.FailFast,2)]
    [TestCase(NativeRuntimePolicy.FailFast,3)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions,0)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions,1)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions,2)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions,3)]
    public async Task EmptyNamespaceRowRetainsEncodingAndZeroUnitsAsync(
        NativeRuntimePolicy policy, int flags)
    {
        var trees = await ReadComponentsAsync(includeTests: false).ConfigureAwait(true);
        var entry = SyntaxTree.Parse($$"""
            namespace Terrum.CodeAnalysis;
            int32 Main() {
                require SourceRange.TryCreate(0, Host.Source.Length, init var spelling);
                require StringLiteral.TryCreate(spelling, init var literal);
                if (TryGetStringNameRowByteLength(literal, {{flags}}, init var nameLength)) { return 1; }
                require TryGetStringNamespaceRowByteLength(literal, {{flags}}, init var length);
                if (length != 8) { return 2; }
                require OwnedInt32Buffer.TryCreate(length, out var workspace);
                OwnedInt32Buffer row = EncodeStringNamespaceRow(literal, {{flags}}, move workspace);
                if (row.Count != 8 || row.Read(0) != {{flags}}) { return 3; }
                mut int32 index = 1;
                while (index < 8) {
                    if (row.Read(index) != 0) { return 4; }
                    index = index + 1;
                }
                return 0;
            }
            """, "empty-namespace-row-entry.terrum");
        var compilation = Compilation.Create(trees.Add(entry),
            [new HostBuffer("Source", PrimitiveType.Char16, [.. "\"\"".Select(unit => (int)unit)], false)],
            (BoundRuntimePolicy)policy);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual10 = compilation.Program; Assert.That(__nunitActual10, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual10!; }))();
        Assert.That(NativeJit.Execute(program, policy), Is.EqualTo(0));
        Assert.That(await ExecuteAotAsync(program, policy).ConfigureAwait(true), Is.EqualTo(0));
    }

    [Test]
    public async Task NamespaceRowRejectsEmbeddedZeroScalarAsync()
    {
        var trees = await ReadComponentsAsync(includeTests: false).ConfigureAwait(true);
        var entry = SyntaxTree.Parse("""
            namespace Terrum.CodeAnalysis;
            int32 Main() {
                require SourceRange.TryCreate(0, Host.Source.Length, init var spelling);
                require StringLiteral.TryCreate(spelling, init var literal);
                if (TryGetStringNamespaceRowByteLength(literal, 0, init var ascii)
                    || TryGetStringNamespaceRowByteLength(literal, 1, init var utf8)
                    || TryGetStringNamespaceRowByteLength(literal, 2, init var utf16)
                    || TryGetStringNamespaceRowByteLength(literal, 3, init var utf32)) { return 1; }
                return 0;
            }
            """, "invalid-namespace-row-entry.terrum");
        var compilation = Compilation.Create(trees.Add(entry),
            [new HostBuffer("Source", PrimitiveType.Char16, [.. "\"A\\0B\"".Select(unit => (int)unit)], false)],
            BoundRuntimePolicy.CheckedExceptions);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual11 = compilation.Program; Assert.That(__nunitActual11, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual11!; }))();
        Assert.That(NativeJit.Execute(program, NativeRuntimePolicy.CheckedExceptions), Is.EqualTo(0));
        Assert.That(await ExecuteAotAsync(program, NativeRuntimePolicy.CheckedExceptions).ConfigureAwait(true), Is.EqualTo(0));
    }
    [TestCase("\"A\"","\"\\u0041\"",true)]
    [TestCase("\"\u00E9\"","\"\\u00E9\"",true)]
    [TestCase("\"\uD83D\uDE00\"","\"\\U0001F600\"",true)]
    [TestCase("\"A\"","\"a\"",false)]
    [TestCase("\"A\"","\"AB\"",false)]
    [TestCase("\"AB\"","\"A\"",false)]
    [TestCase("\"A\"","\"B\\0\"",null)]
    [TestCase("\"A\\0\"","\"B\"",null)]
    [TestCase("\"A\u0301\"","\"\u00C1\"",false)]
    [TestCase("\"\"","\"A\"",null)]
    [TestCase("\"A\"","\"\\0\"",null)]
    [TestCase("\"A\\u0000\"","\"A\\u0000\"",null)]
    public async Task MetadataNameEqualityComparesDecodedScalarsWithoutNormalizationAsync(
        string left, string right, bool? expected)
    {
        var trees = await ReadComponentsAsync(includeTests: false).ConfigureAwait(true);
        var assertion = expected is bool equal
            ? $"require TryCompareStringNames(first, second, init var same); if (same != {equal.ToString().ToLowerInvariant()}) {{ return 1; }}"
            : "if (TryCompareStringNames(first, second, init var same)) { return 1; }";
        var entry = SyntaxTree.Parse($$"""
            namespace Terrum.CodeAnalysis;
            int32 Main() {
                require SourceRange.TryCreate(0, {{left.Length}}, init var firstSpelling);
                require SourceRange.TryCreate({{left.Length}}, {{right.Length}}, init var secondSpelling);
                require StringLiteral.TryCreate(firstSpelling, init var first);
                require StringLiteral.TryCreate(secondSpelling, init var second);
                {{assertion}}
                return 0;
            }
            """, "string-name-equality-entry.terrum");
        var compilation = Compilation.Create(trees.Add(entry),
            [new HostBuffer("Source", PrimitiveType.Char16, [.. (left + right).Select(unit => (int)unit)], false)],
            BoundRuntimePolicy.CheckedExceptions);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual12 = compilation.Program; Assert.That(__nunitActual12, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual12!; }))();
        Assert.That(NativeJit.Execute(program, NativeRuntimePolicy.CheckedExceptions), Is.EqualTo(0));
        Assert.That(await ExecuteAotAsync(program, NativeRuntimePolicy.CheckedExceptions).ConfigureAwait(true), Is.EqualTo(0));
    }
    [TestCase(NativeRuntimePolicy.FailFast,"\"\"","\"\"",true)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions,"\"\"","\"\"",true)]
    [TestCase(NativeRuntimePolicy.FailFast,"\"\"","\"A\"",false)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions,"\"A\"","\"\"",false)]
    [TestCase(NativeRuntimePolicy.FailFast,"\"\u00E9\"","\"\\u00E9\"",true)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions,"\"\uD83D\uDE00\"","\"\\U0001F600\"",true)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions,"\"A\u0301\"","\"\u00C1\"",false)]
    [TestCase(NativeRuntimePolicy.FailFast,"\"A\"","\"B\\0\"",null)]
    [TestCase(NativeRuntimePolicy.FailFast,"\"\"","\"\\0\"",null)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions,"\"A\"","\"A\\u0000\"",null)]
    public async Task MetadataNamespaceEqualityAllowsEmptyWithoutNormalizingAsync(
        NativeRuntimePolicy policy, string left, string right, bool? expected)
    {
        var trees = await ReadComponentsAsync(includeTests: false).ConfigureAwait(true);
        var assertion = expected is bool equal
            ? $"require TryCompareStringNamespaces(first, second, init var same); if (same != {equal.ToString().ToLowerInvariant()}) {{ return 1; }}"
            : "if (TryCompareStringNamespaces(first, second, init var same)) { return 1; }";
        var entry = SyntaxTree.Parse($$"""
            namespace Terrum.CodeAnalysis;
            int32 Main() {
                require SourceRange.TryCreate(0, {{left.Length}}, init var firstSpelling);
                require SourceRange.TryCreate({{left.Length}}, {{right.Length}}, init var secondSpelling);
                require StringLiteral.TryCreate(firstSpelling, init var first);
                require StringLiteral.TryCreate(secondSpelling, init var second);
                {{assertion}}
                return 0;
            }
            """, "string-namespace-equality-entry.terrum");
        var compilation = Compilation.Create(trees.Add(entry),
            [new HostBuffer("Source", PrimitiveType.Char16, [.. (left + right).Select(unit => (int)unit)], false)],
            (BoundRuntimePolicy)policy);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual13 = compilation.Program; Assert.That(__nunitActual13, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual13!; }))();
        Assert.That(NativeJit.Execute(program, policy), Is.EqualTo(0));
        Assert.That(await ExecuteAotAsync(program, policy).ConfigureAwait(true), Is.EqualTo(0));
    }
    [TestCase("\"\"")]
    [TestCase("\"\\0\"")]
    [TestCase("\"A\\0B\"")]
    public async Task InvalidMetadataNameEmissionReleasesWorkspaceOnCheckedFailureAsync(string invalid)
    {
        var trees = await ReadComponentsAsync(includeTests: false).ConfigureAwait(true);
        var entry = SyntaxTree.Parse($$"""
            namespace Terrum.CodeAnalysis;
            int32 Main() {
                require SourceRange.TryCreate(0, {{invalid.Length}}, init var badSpelling);
                require StringLiteral.TryCreate(badSpelling, init var bad);
                require SourceRange.TryCreate({{invalid.Length}}, 4, init var goodSpelling);
                require StringLiteral.TryCreate(goodSpelling, init var good);
                require OwnedInt32Buffer.TryCreate(8, out var workspace);
                unsafe {
                    try {
                        OwnedInt32Buffer invalidRow = EncodeStringNameRow(bad, 0, move workspace);
                        return 1;
                    } catch {
                        require OwnedInt32Buffer.TryCreate(8, out var nextWorkspace);
                        OwnedInt32Buffer row = EncodeStringNameRow(good, 0, move nextWorkspace);
                        if (row.Count != 8 || row.Read(0) != 0 || row.Read(1) != 2
                            || row.Read(4) != 79 || row.Read(5) != 107
                            || row.Read(6) != 0 || row.Read(7) != 0) { return 2; }
                        return 0;
                    }
                }
            }
            """, "invalid-name-emission-entry.terrum");
        var compilation = Compilation.Create(trees.Add(entry),
            [new HostBuffer("Source", PrimitiveType.Char16, [.. (invalid + "\"Ok\"").Select(unit => (int)unit)], false)],
            BoundRuntimePolicy.CheckedExceptions);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual14 = compilation.Program; Assert.That(__nunitActual14, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual14!; }))();
        Assert.That(NativeJit.Execute(program, NativeRuntimePolicy.CheckedExceptions), Is.EqualTo(0));
        Assert.That(await ExecuteAotAsync(program, NativeRuntimePolicy.CheckedExceptions).ConfigureAwait(true), Is.EqualTo(0));
    }
    [TestCase(NativeRuntimePolicy.FailFast,257,1,1,0)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions,65536,0,0,1)]
    public async Task StringRowUnitCountUsesThreeLittleEndianBytesAsync(
        NativeRuntimePolicy policy, int unitCount, int low, int middle, int high)
    {
        var trees = await ReadComponentsAsync(includeTests: false).ConfigureAwait(true);
        var contentEnd = unitCount + 4;
        var extent = contentEnd + 1;
        var rowLength = extent + (4 - extent % 4) % 4;
        var entry = SyntaxTree.Parse($$"""
            namespace Terrum.CodeAnalysis;
            int32 Main() {
                require SourceRange.TryCreate(0, Host.Source.Length, init var spelling);
                require StringLiteral.TryCreate(spelling, init var literal);
                require TryGetStringRowByteLength(literal, 0, init var length);
                if (length != {{rowLength}}) { return 1; }
                require OwnedInt32Buffer.TryCreate(length, out var workspace);
                OwnedInt32Buffer row = EncodeStringRow(literal, 0, move workspace);
                if (row.Count != length || row.Read(0) != 0
                    || row.Read(1) != {{low}} || row.Read(2) != {{middle}}
                    || row.Read(3) != {{high}}) { return 2; }
                mut int32 index = 4;
                while (index < {{contentEnd}}) {
                    if (row.Read(index) != 65) { return 3; }
                    index = index + 1;
                }
                while (index < row.Count) {
                    if (row.Read(index) != 0) { return 4; }
                    index = index + 1;
                }
                return 0;
            }
            """, "string-row-count-entry.terrum");
        var source = ImmutableArray.CreateBuilder<int>(unitCount + 2);
        source.Add(34);
        source.AddRange(Enumerable.Repeat(65, unitCount));
        source.Add(34);
        var compilation = Compilation.Create(trees.Add(entry),
            [new HostBuffer("Source", PrimitiveType.Char16, source.ToImmutable(), false)],
            (BoundRuntimePolicy)policy);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual15 = compilation.Program; Assert.That(__nunitActual15, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual15!; }))();
        Assert.That(NativeJit.Execute(program, policy), Is.EqualTo(0));
        Assert.That(await ExecuteAotAsync(program, policy).ConfigureAwait(true), Is.EqualTo(0));
    }

    [Test]
    public async Task StringRowsRejectReservedFlagsAndUndersizedWorkspaceAsync()
    {
        var trees = await ReadComponentsAsync(includeTests: false).ConfigureAwait(true);
        var entry = SyntaxTree.Parse("""
            namespace Terrum.CodeAnalysis;
            int32 Main() {
                require SourceRange.TryCreate(0, Host.Source.Length, init var spelling);
                require StringLiteral.TryCreate(spelling, init var literal);
                if (TryGetStringRowByteLength(literal, -1, init var negative)
                    || TryGetStringRowByteLength(literal, 4, init var reserved)
                    || TryGetStringRowByteLength(literal, 255, init var extended)
                    || TryGetStringRowByteLength(literal, 0, init var nonAscii)) { return 1; }
                require TryGetStringRowByteLength(literal, 1, init var exactLength);
                require OwnedInt32Buffer.TryCreate(exactLength - 1, out var small);
                unsafe {
                    try {
                        OwnedInt32Buffer partial = EncodeStringRow(literal, 1, move small);
                        return 2;
                    } catch {
                        require OwnedInt32Buffer.TryCreate(exactLength, out var exact);
                        OwnedInt32Buffer row = EncodeStringRow(literal, 1, move exact);
                        if (row.Count != exactLength || row.Read(0) != 1) { return 3; }
                        return 0;
                    }
                }
            }
            """, "string-row-failure-entry.terrum");
        var compilation = Compilation.Create(trees.Add(entry),
            [new HostBuffer("Source", PrimitiveType.Char16, [34, 233, 34], false)],
            BoundRuntimePolicy.CheckedExceptions);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual16 = compilation.Program; Assert.That(__nunitActual16, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual16!; }))();
        Assert.That(NativeJit.Execute(program, NativeRuntimePolicy.CheckedExceptions), Is.EqualTo(0));
        Assert.That(await ExecuteAotAsync(program, NativeRuntimePolicy.CheckedExceptions).ConfigureAwait(true), Is.EqualTo(0));
    }

    private static async Task<int> ExecuteAotAsync(BoundProgram program, NativeRuntimePolicy policy)
    {
        var path = Path.Combine(TestArtifactPaths.DirectoryPath, $"string-literals-{Guid.NewGuid():N}.exe");
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
            await TestFileCleanup.DeleteWithRetryAsync(path).ConfigureAwait(true);
        }
    }
    [TestCase("bool success = StringLiteral.TryCreate(range, init var literal); return literal.ScalarCount;")]
    [TestCase("require StringLiteral.TryCreate(range, init var literal); bool success = literal.TryReadScalar(0, init var scalar, init var next); return scalar;")]
    [TestCase("require StringLiteral.TryCreate(range, init var literal); bool success = literal.TryReadScalar(0, init var scalar, init var next); return next;")]
    [TestCase("require StringLiteral.TryCreate(range, init var literal); StringLiteralCursor cursor = StringLiteralCursor.Create(literal); bool success = cursor.TryRead(init var scalar, init var next); return next.Position;")]
    [TestCase("require StringLiteral.TryCreate(range, init var literal); bool success = literal.TryGetAsciiLength(init var length); return length;")]
    public async Task FailedValidationAndReadsDoNotPublishOutputsAsync(string body)
    {
        var trees = await ReadComponentsAsync(includeTests: false).ConfigureAwait(true);
        var entry = SyntaxTree.Parse($$"""
            namespace Terrum.CodeAnalysis;
            int32 Main() {
                require SourceRange.TryCreate(0, 2, init var range);
                {{body}}
            }
            """, "entry.terrum");
        var compilation = Compilation.Create(trees.Add(entry),
            [new HostBuffer("Source", PrimitiveType.Char16, [34, 34], false)]);
        Assert.That(compilation.HasErrors, Is.True);
        Assert.That(compilation.Program, Is.Null);
        Assert.That(global::System.Linq.Enumerable.Any(compilation.SourceDiagnostics,diagnostic => diagnostic.SyntaxTree == entry && diagnostic.Diagnostic.Code == "TRM3016"),Is.True);
    }

    private static IEnumerable<LiteralFixture> Fixtures()
    {
        yield return Valid();
        yield return Valid(("a", 97), ("\0", 0), ("z", 122));
        yield return Valid(("\t", 9), ("\v", 11), ("\f", 12), ("\u001C", 28), ("\u0661", 1633));
        yield return Valid(("\\n", 10), ("\\r", 13), ("\\t", 9), ("\\\\", 92), ("\\\"", 34), ("\\0", 0));
        yield return Valid(("e", 101), ("\u0301", 769), ("\u00E9", 233));
        yield return Valid(("\\u00FF", 255), ("\u00E9", 233), ("\\U00000080", 128));
        yield return Valid(("\\u0100", 256));
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
        var names = new[] {
            "sources/Terrum.CodeAnalysis.Terrum/Text/SourceRange.terrum", "sources/Terrum.CodeAnalysis.Terrum/Lexing/CharacterFacts.terrum", "sources/Terrum.CodeAnalysis.Terrum/Text/Utf16CodeUnits.terrum", "sources/Terrum.CodeAnalysis.Terrum/Text/Utf16Scalar.terrum", "sources/Terrum.CodeAnalysis.Terrum/Lexing/StringEscape.terrum",
            "sources/Terrum.CodeAnalysis.Terrum/Lexing/StringLiteral.terrum", "sources/Terrum.CodeAnalysis.Terrum/Modules/StringRow.terrum",
        };
        foreach (var name in includeTests
            ? [.. names, "tests/Terrum.CodeAnalysis.Terrum.Tests/Lexing/StringLiteralTests.terrum"]
            : names)
        {
            var path = TerrumProjectPaths.RepositoryFile(RepositoryRoot(), name);
            var text = await File.ReadAllTextAsync(path).ConfigureAwait(true);
            var tree = SyntaxTree.Parse(text, path);
            Assert.That(tree.HasErrors, Is.False, string.Join(Environment.NewLine, tree.Diagnostics));
            Assert.That(tree.Root.ToFullString(), Is.EqualTo(text));
            trees.Add(tree);
        }
        return trees.ToImmutable();
    }

    private static string RepositoryRoot() =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));

    private sealed record LiteralFixture(string Text, ScalarFixture[]? Scalars);
    private sealed record ScalarFixture(int Start, int Value, int End);
}
