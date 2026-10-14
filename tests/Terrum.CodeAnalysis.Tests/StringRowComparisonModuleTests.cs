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
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests;

public sealed class StringRowComparisonModuleTests
{
    private const int InvalidComparison = -1;

    private const string VerificationSource = """
        namespace Terrum.CodeAnalysis;
        int32 Main() { return 0; }

        int32 CompareStringText(bool allowEmpty) {
            if (Host.Source.Length < 2) { return -2; }
            int32 leftLength = int32(Host.Source[0]);
            int32 rightLength = int32(Host.Source[1]);
            if (leftLength > Host.Source.Length - 2) { return -2; }
            int32 rightStart = 2 + leftLength;
            if (rightLength != Host.Source.Length - rightStart) { return -2; }
            if (!SourceRange.TryCreate(2, leftLength, init var leftSpelling)) { return -2; }
            if (!SourceRange.TryCreate(rightStart, rightLength, init var rightSpelling)) { return -2; }
            if (!StringLiteral.TryCreate(leftSpelling, init var left)) { return -2; }
            if (!StringLiteral.TryCreate(rightSpelling, init var right)) { return -2; }
            if (allowEmpty) {
                if (!TryCompareStringNamespaces(left, right, init var equal)) { return -1; }
                if (equal) { return 1; }
                return 0;
            }
            if (!TryCompareStringNames(left, right, init var equal)) { return -1; }
            if (equal) { return 1; }
            return 0;
        }

        public int32 CompareStringNames() { return CompareStringText(false); }
        public int32 CompareStringNamespaces() { return CompareStringText(true); }
        """;
    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
    public async Task StringRowComparisonsUseFreshSourceGrantsAcrossModuleJit(BoundRuntimePolicy policy)
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
            Assert.That(trees[index].HasErrors, Is.False, string.Join(Environment.NewLine, trees[index].Diagnostics));
        }
        trees[^1] = SyntaxTree.Parse(VerificationSource, "string-row-comparison-module.terrum");

        var producerCompilation = Compilation.Create(trees,
        [
            CreateSourceGrant("\"A\"", "\"A\""),
        ], policy);
        Assert.That(producerCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, producerCompilation.Diagnostics));

        var exports = new[] { "CompareStringNames", "CompareStringNamespaces" };
        var producer = ModuleCompiler.CreateProducer(((global::System.Func<BoundProgram>)(() => { var __nunitActual0 = producerCompilation.Program; Assert.That(__nunitActual0, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual0!; }))(),
            [.. exports.Select(static name => "Terrum.CodeAnalysis." + name)]);
        var consumers = new BoundProgram[exports.Length];
        for (var index = 0; index < exports.Length; index++)
        {
            var consumerTree = SyntaxTree.Parse(
                $"namespace Terrum.CodeAnalysis; int32 Main() {{ return {exports[index]}(); }}",
                "string-row-comparison-consumer.terrum");
            var consumerCompilation = Compilation.Create(
                consumerTree, ModuleCompiler.CreateReference(producer), policy);
            Assert.That(consumerCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, consumerCompilation.Diagnostics));
            var consumer = ModuleCompiler.CreateConsumer(
                ((global::System.Func<BoundProgram>)(() => { var __nunitActual1 = consumerCompilation.Program; Assert.That(__nunitActual1, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual1!; }))(), producer);
            consumers[index] = ModuleCompiler.Link(consumer, producer);
        }

        var fixtures = new[] {
            new ComparisonFixture("\"A\"", "\"\\u0041\"", "A", "A", true, true),
            new ComparisonFixture("\"\u00E9\"", "\"\\u00E9\"", "\u00E9", "\u00E9", true, true),
            new ComparisonFixture("\"\uD83D\uDE00\"", "\"\\U0001F600\"", "\U0001F600", "\U0001F600", true, true),
            new ComparisonFixture("\"\U00010000\"", "\"\\U00010000\"", "\U00010000", "\U00010000", true, true),
            new ComparisonFixture("\"\U0010FFFF\"", "\"\\U0010FFFF\"", "\U0010FFFF", "\U0010FFFF", true, true),
            new ComparisonFixture("\"\\U00010000\"", "\"\\U0010FFFF\"",
                "\U00010000", "\U0010FFFF", false, false),
            new ComparisonFixture("\"A\"", "\"a\"", "A", "a", false, false),
            new ComparisonFixture("\"A\"", "\"AB\"", "A", "AB", false, false),
            new ComparisonFixture("\"AB\"", "\"A\"", "AB", "A", false, false),
            new ComparisonFixture("\"A\u0301\"", "\"\u00C1\"", "A\u0301", "\u00C1", false, false),
            new ComparisonFixture("\"\"", "\"\"", "", "", null, true),
            new ComparisonFixture("\"\"", "\"A\"", "", "A", null, false),
            new ComparisonFixture("\"A\"", "\"B\\0\"", "A", "B\0", null, null),
            new ComparisonFixture("\"A\\0\"", "\"B\"", "A\0", "B", null, null),
            new ComparisonFixture("\"A\"", "\"\\0\"", "A", "\0", null, null),
            new ComparisonFixture("\"A\\u0000\"", "\"A\\u0000\"", "A\0", "A\0", null, null),
            new ComparisonFixture("\"A\\0B\"", "\"A\\0B\"", "A\0B", "A\0B", null, null),
        };

        foreach (var fixture in fixtures)
        {
            for (var apiIndex = 0; apiIndex < exports.Length; apiIndex++)
            {
                var expected = apiIndex == 0 ? fixture.NameEquality : fixture.NamespaceEquality;
                if (expected is bool expectedEquality)
                {
                    Assert.That(string.Equals(fixture.LeftText, fixture.RightText, StringComparison.Ordinal), Is.EqualTo(expectedEquality));
                }

                var grant = CreateSourceGrant(fixture.LeftSpelling, fixture.RightSpelling);
                var result = NativeJit.ExecuteWithSourceGrant(consumers[apiIndex], [grant],
                    (NativeRuntimePolicy)policy);
                var expectedStatus = expected switch {
                    bool equality => equality ? 1 : 0,
                    null => InvalidComparison,
                };
                Assert.That(result, Is.EqualTo(expectedStatus));
            }
        }

        const int maximumScalarsPerBatch = 818;
        var leftBuilder = new StringBuilder(maximumScalarsPerBatch * 10 + 2);
        var rightBuilder = new StringBuilder(maximumScalarsPerBatch * 10 + 2);
        var expectedLeftBuilder = new StringBuilder(maximumScalarsPerBatch * 2);
        var expectedRightBuilder = new StringBuilder(maximumScalarsPerBatch * 2);
        var scalarCount = 0;
        var batchStart = 0;
        void VerifyBatch()
        {
            leftBuilder.Append('"');
            rightBuilder.Append('"');
            var leftSpelling = leftBuilder.ToString();
            var rightSpelling = rightBuilder.ToString();
            var expectedLeft = expectedLeftBuilder.ToString();
            var expectedRight = expectedRightBuilder.ToString();
            var expectedEqual = string.Equals(expectedLeft, expectedRight, StringComparison.Ordinal);
            Assert.That(scalarCount * 20 + 6 <= 16_384, Is.True);
            var grant = CreateSourceGrant(leftSpelling, rightSpelling);
            for (var apiIndex = 0; apiIndex < exports.Length; apiIndex++)
            {
                var result = NativeJit.ExecuteWithSourceGrant(consumers[apiIndex], [grant],
                    (NativeRuntimePolicy)policy);
                Assert.That(result == (expectedEqual ? 1 : 0), Is.True, $"{exports[apiIndex]} failed for batch starting at U+{batchStart:X}: {result}.");
            }

            leftBuilder.Clear();
            rightBuilder.Clear();
            expectedLeftBuilder.Clear();
            expectedRightBuilder.Clear();
            scalarCount = 0;
        }

        for (var scalar = 1; scalar < 0x110000; scalar++)
        {
            if (scalar is >= 0xD800 and <= 0xDFFF)
            {
                continue;
            }

            if (scalarCount == 0)
            {
                batchStart = scalar;
                leftBuilder.Append('"');
                rightBuilder.Append('"');
            }
            leftBuilder.Append($"\\U{scalar:X8}");
            rightBuilder.Append($"\\U{scalar:x8}");
            expectedLeftBuilder.Append(new Rune(scalar).ToString());
            expectedRightBuilder.Append(char.ConvertFromUtf32(scalar));
            scalarCount++;
            if (scalarCount == maximumScalarsPerBatch)
            {
                VerifyBatch();
            }
        }
        if (scalarCount != 0)
        {
            VerifyBatch();
        }
    }

    private static HostBuffer CreateSourceGrant(string leftSpelling, string rightSpelling)
    {
        var values = ImmutableArray.CreateBuilder<int>();
        values.Add(leftSpelling.Length);
        values.Add(rightSpelling.Length);
        values.AddRange(leftSpelling.Select(static unit => (int)unit));
        values.AddRange(rightSpelling.Select(static unit => (int)unit));
        Assert.That(values.Count <= 16_384, Is.True);
        return new HostBuffer("Source", PrimitiveType.Char16, values.ToImmutable(), false) {
            IsInvocationScoped = true,
        };
    }

    private static string RepositoryRoot() =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));

    private sealed record ComparisonFixture(
        string LeftSpelling,
        string RightSpelling,
        string LeftText,
        string RightText,
        bool? NameEquality,
        bool? NamespaceEquality);
}
