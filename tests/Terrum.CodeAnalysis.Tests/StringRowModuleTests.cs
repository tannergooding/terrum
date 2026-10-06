using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Terrum.CodeAnalysis.Text;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed class StringRowModuleTests
{
    private const string VerificationSource = """
        namespace Terrum.CodeAnalysis;
        int32 Main() { return 0; }

        bool TryGetLiteral(init ref<StringLiteral> literal) {
            if (Host.Source.Length < 3) { return false; }
            int32 expectedLength = int32(Host.Source[2]);
            if (expectedLength != 65535 && expectedLength > Host.Source.Length - 3) { return false; }
            mut int32 literalStart = 3;
            if (expectedLength != 65535) { literalStart = literalStart + expectedLength; }
            if (!SourceRange.TryCreate(literalStart, Host.Source.Length - literalStart, init var spelling)) {
                return false;
            }
            return StringLiteral.TryCreate(spelling, init literal);
        }

        bool TryMeasure(StringLiteral literal, int32 kind, int32 encoding, init ref<int32> length) {
            if (kind == 0) { return TryGetStringNameRowByteLength(literal, encoding, init length); }
            return TryGetStringNamespaceRowByteLength(literal, encoding, init length);
        }

        OwnedInt32Buffer Encode(StringLiteral literal, int32 kind, int32 encoding, OwnedInt32Buffer workspace) {
            if (kind == 0) { return EncodeStringNameRow(literal, encoding, move workspace); }
            return EncodeStringNamespaceRow(literal, encoding, move workspace);
        }

        int32 VerifyRow(int32 kind, int32 encoding) {
            if (Host.Source.Length < 3 || int32(Host.Source[0]) != kind
                || int32(Host.Source[1]) != encoding) { return -1; }
            if (!TryGetLiteral(init var literal)) { return -2; }

            int32 expectedLength = int32(Host.Source[2]);
            if (expectedLength == 65535) {
                if (TryMeasure(literal, kind, encoding, init var rejectedLength)) { return -3; }
                return 0;
            }

            if (!TryMeasure(literal, kind, encoding, init var measuredLength)) { return -4; }
            if (measuredLength != expectedLength) { return -5; }
            if (!OwnedInt32Buffer.TryCreate(expectedLength, out var workspace)) { return -6; }
            mut OwnedInt32Buffer row = Encode(literal, kind, encoding, move workspace);
            if (row.Count != expectedLength || row.Capacity != expectedLength) { return -7; }
            mut int32 index = 0;
            while (index < expectedLength) {
                if (row.Read(index) != int32(Host.Source[index + 3])) { return -8; }
                index = index + 1;
            }
            return 0;
        }

        public int32 VerifyNameAscii() { return VerifyRow(0, 0); }
        public int32 VerifyNameUtf8() { return VerifyRow(0, 1); }
        public int32 VerifyNameUtf16Le() { return VerifyRow(0, 2); }
        public int32 VerifyNameUtf32Le() { return VerifyRow(0, 3); }
        public int32 VerifyNamespaceAscii() { return VerifyRow(1, 0); }
        public int32 VerifyNamespaceUtf8() { return VerifyRow(1, 1); }
        public int32 VerifyNamespaceUtf16Le() { return VerifyRow(1, 2); }
        public int32 VerifyNamespaceUtf32Le() { return VerifyRow(1, 3); }
        """;

    [Theory]
    [InlineData(BoundRuntimePolicy.FailFast)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions)]
    public async Task StringRowModuleMatchesAllFormatsAcrossFreshSourceGrants(BoundRuntimePolicy policy)
    {
        var root = RepositoryRoot();
        var componentNames = new[] {
            "SourceRange", "CharacterFacts", "Utf16CodeUnits", "Utf16Scalar", "StringEscape", "StringLiteral", "StringRow",
        };
        var trees = new SyntaxTree[componentNames.Length + 1];
        for (var index = 0; index < componentNames.Length; index++)
        {
            var path = Path.Combine(root, "self-host", componentNames[index] + ".terrum");
            var text = await File.ReadAllTextAsync(path).ConfigureAwait(true);
            trees[index] = SyntaxTree.Parse(text, path);
            Assert.False(trees[index].HasErrors, string.Join(Environment.NewLine, trees[index].Diagnostics));
        }
        trees[^1] = SyntaxTree.Parse(VerificationSource, "string-row-module.terrum");

        var producerCompilation = Compilation.Create(trees,
        [
            new HostBuffer("Source", PrimitiveType.Char16, [0, 0, 65535, 34, 34], false) {
                IsInvocationScoped = true,
            },
        ], policy);
        Assert.False(producerCompilation.HasErrors,
            string.Join(Environment.NewLine, producerCompilation.Diagnostics));

        var exports = new[] {
            "VerifyNameAscii",
            "VerifyNameUtf8",
            "VerifyNameUtf16Le",
            "VerifyNameUtf32Le",
            "VerifyNamespaceAscii",
            "VerifyNamespaceUtf8",
            "VerifyNamespaceUtf16Le",
            "VerifyNamespaceUtf32Le",
        };
        var producer = ModuleCompiler.CreateProducer(Assert.IsType<BoundProgram>(producerCompilation.Program),
            [.. exports.Select(static name => "Terrum.CodeAnalysis." + name)]);
        var consumers = new BoundProgram[exports.Length];
        for (var index = 0; index < exports.Length; index++)
        {
            var consumerTree = SyntaxTree.Parse(
                $"namespace Terrum.CodeAnalysis; int32 Main() {{ return {exports[index]}(); }}",
                "string-row-consumer.terrum");
            var consumerCompilation = Compilation.Create(consumerTree, ModuleCompiler.CreateReference(producer), policy);
            Assert.False(consumerCompilation.HasErrors,
                string.Join(Environment.NewLine, consumerCompilation.Diagnostics));
            var consumer = ModuleCompiler.CreateConsumer(
                Assert.IsType<BoundProgram>(consumerCompilation.Program), producer);
            consumers[index] = ModuleCompiler.Link(consumer, producer);
        }

        var fixtures = new[] {
            new RowFixture(true, 0, "Name", "\"Name\"", true),
            new RowFixture(true, 1, "é😀", "\"\\u00E9\\U0001F600\"", true),
            new RowFixture(true, 2, "😀", "\"\\U0001F600\"", true),
            new RowFixture(true, 3, "Aé😀", "\"A\\u00E9\\U0001F600\"", true),
            new RowFixture(true, 1, "\U00010000\U0010FFFF", "\"\\U00010000\\U0010FFFF\"", true),
            new RowFixture(true, 2, "\U00010000\U0010FFFF", "\"\\U00010000\\U0010FFFF\"", true),
            new RowFixture(true, 3, "\U00010000\U0010FFFF", "\"\\U00010000\\U0010FFFF\"", true),
            new RowFixture(false, 0, "Name", "\"Name\"", true),
            new RowFixture(false, 1, "é😀", "\"\\u00E9\\U0001F600\"", true),
            new RowFixture(false, 2, "😀", "\"\\U0001F600\"", true),
            new RowFixture(false, 3, "Aé😀", "\"A\\u00E9\\U0001F600\"", true),
            new RowFixture(false, 1, "\U00010000\U0010FFFF", "\"\\U00010000\\U0010FFFF\"", true),
            new RowFixture(false, 2, "\U00010000\U0010FFFF", "\"\\U00010000\\U0010FFFF\"", true),
            new RowFixture(false, 3, "\U00010000\U0010FFFF", "\"\\U00010000\\U0010FFFF\"", true),
            new RowFixture(false, 0, "", "\"\"", true),
            new RowFixture(false, 1, "", "\"\"", true),
            new RowFixture(false, 2, "", "\"\"", true),
            new RowFixture(false, 3, "", "\"\"", true),
            new RowFixture(true, 0, "", "\"\"", false),
            new RowFixture(true, 1, "", "\"\"", false),
            new RowFixture(true, 2, "", "\"\"", false),
            new RowFixture(true, 3, "", "\"\"", false),
            new RowFixture(true, 0, "é", "\"\\u00E9\"", false),
            new RowFixture(false, 0, "é", "\"\\u00E9\"", false),
            new RowFixture(true, 0, "A\0B", "\"A\\0B\"", false),
            new RowFixture(false, 0, "A\0B", "\"A\\0B\"", false),
            new RowFixture(true, 1, "A\0B", "\"A\\0B\"", false),
            new RowFixture(false, 1, "A\0B", "\"A\\0B\"", false),
            new RowFixture(true, 2, "A\0B", "\"A\\0B\"", false),
            new RowFixture(false, 2, "A\0B", "\"A\\0B\"", false),
            new RowFixture(true, 3, "A\0B", "\"A\\0B\"", false),
            new RowFixture(false, 3, "A\0B", "\"A\\0B\"", false),
        };

        void VerifyFixture(RowFixture fixture)
        {
            int[]? expectedBytes = fixture.IsValid
                ? GetExpectedRow(fixture)
                : null;
            var grant = CreateSourceGrant(fixture, expectedBytes);
            var consumerIndex = (fixture.IsName ? 0 : 4) + fixture.Encoding;
            var result = NativeJit.ExecuteWithSourceGrant(consumers[consumerIndex], [grant],
                (NativeRuntimePolicy)policy);
            Assert.True(result == 0,
                $"{exports[consumerIndex]} failed for {fixture.Spelling}: verification returned {result}.");
        }

        foreach (var fixture in fixtures)
        {
            VerifyFixture(fixture);
        }

        const int maximumScalarsPerBatch = 1_100;
        var textBuilder = new StringBuilder(maximumScalarsPerBatch * 2);
        var spellingBuilder = new StringBuilder(maximumScalarsPerBatch * 10);
        var scalarCount = 0;
        void VerifyBatch()
        {
            var text = textBuilder.ToString();
            var spelling = $"\"{spellingBuilder}\"";
            Assert.True(scalarCount * 14 + 13 <= 16_384);
            for (var encoding = 0; encoding < 4; encoding++)
            {
                var valid = encoding != 0 || text.All(static unit => unit <= 0x7F);
                foreach (var isName in new[] { true, false })
                {
                    VerifyFixture(new RowFixture(
                        isName, encoding, text, spelling, valid));
                }
            }

            textBuilder.Clear();
            spellingBuilder.Clear();
            scalarCount = 0;
        }

        for (var scalar = 1; scalar < 0x110000; scalar++)
        {
            if (scalar is >= 0xD800 and <= 0xDFFF)
            {
                continue;
            }

            textBuilder.Append(new Rune(scalar).ToString());
            spellingBuilder.Append($"\\U{scalar:X8}");
            scalarCount++;
            if (scalarCount == maximumScalarsPerBatch || scalar == 0x7F)
            {
                VerifyBatch();
            }
        }
        if (scalarCount != 0)
        {
            VerifyBatch();
        }
    }

    private static int[] GetExpectedRow(RowFixture fixture)
    {
        var section = fixture.IsName
            ? TrRmStringTableWriter.EncodeNames([(fixture.Text, fixture.Encoding)])
            : TrRmStringTableWriter.EncodeTextRows([(fixture.Text, fixture.Encoding)]);
        return new TrRmVariableTableIndex(section, 1, 8, 4).GetRow(1).ToArray()
            .Select(static value => (int)value)
            .ToArray();
    }

    private static HostBuffer CreateSourceGrant(RowFixture fixture, int[]? expectedBytes)
    {
        var values = ImmutableArray.CreateBuilder<int>();
        values.Add(fixture.IsName ? 0 : 1);
        values.Add(fixture.Encoding);
        values.Add(expectedBytes?.Length ?? ushort.MaxValue);
        if (expectedBytes is not null)
        {
            values.AddRange(expectedBytes);
        }
        values.AddRange(fixture.Spelling.Select(static unit => (int)unit));
        Assert.True(values.Count <= 16_384);
        return new HostBuffer("Source", PrimitiveType.Char16, values.ToImmutable(), false) {
            IsInvocationScoped = true,
        };
    }

    private static string RepositoryRoot() =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));

    private sealed record RowFixture(bool IsName, int Encoding, string Text, string Spelling, bool IsValid);
}
