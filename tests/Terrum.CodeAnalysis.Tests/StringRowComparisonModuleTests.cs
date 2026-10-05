using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Terrum.CodeAnalysis.Text;
using Xunit;

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

    [Theory]
    [InlineData(BoundRuntimePolicy.FailFast)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions)]
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
            Assert.False(trees[index].HasErrors, string.Join(Environment.NewLine, trees[index].Diagnostics));
        }
        trees[^1] = SyntaxTree.Parse(VerificationSource, "string-row-comparison-module.terrum");

        var producerCompilation = Compilation.Create(trees,
        [
            CreateSourceGrant("\"A\"", "\"A\""),
        ], policy);
        Assert.False(producerCompilation.HasErrors,
            string.Join(Environment.NewLine, producerCompilation.Diagnostics));

        var exports = new[] { "CompareStringNames", "CompareStringNamespaces" };
        var producer = ModuleCompiler.CreateProducer(Assert.IsType<BoundProgram>(producerCompilation.Program),
            [.. exports.Select(static name => "Terrum.CodeAnalysis." + name)]);
        var consumers = new BoundProgram[exports.Length];
        for (var index = 0; index < exports.Length; index++)
        {
            var consumerTree = SyntaxTree.Parse(
                $"namespace Terrum.CodeAnalysis; int32 Main() {{ return {exports[index]}(); }}",
                "string-row-comparison-consumer.terrum");
            var consumerCompilation = Compilation.Create(
                consumerTree, ModuleCompiler.CreateReference(producer), policy);
            Assert.False(consumerCompilation.HasErrors,
                string.Join(Environment.NewLine, consumerCompilation.Diagnostics));
            var consumer = ModuleCompiler.CreateConsumer(
                Assert.IsType<BoundProgram>(consumerCompilation.Program), producer);
            consumers[index] = ModuleCompiler.Link(consumer, producer);
        }

        var fixtures = new[] {
            new ComparisonFixture("\"A\"", "\"\\u0041\"", "A", "A", true, true),
            new ComparisonFixture("\"\u00E9\"", "\"\\u00E9\"", "\u00E9", "\u00E9", true, true),
            new ComparisonFixture("\"\uD83D\uDE00\"", "\"\\U0001F600\"", "\U0001F600", "\U0001F600", true, true),
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
                    Assert.Equal(expectedEquality,
                        string.Equals(fixture.LeftText, fixture.RightText, StringComparison.Ordinal));
                }

                var grant = CreateSourceGrant(fixture.LeftSpelling, fixture.RightSpelling);
                var result = NativeJit.ExecuteWithSourceGrant(consumers[apiIndex], [grant],
                    (NativeRuntimePolicy)policy);
                var expectedStatus = expected switch {
                    bool equality => equality ? 1 : 0,
                    null => InvalidComparison,
                };
                Assert.Equal(expectedStatus, result);
            }
        }
    }

    private static HostBuffer CreateSourceGrant(string leftSpelling, string rightSpelling)
    {
        var values = ImmutableArray.CreateBuilder<int>();
        values.Add(leftSpelling.Length);
        values.Add(rightSpelling.Length);
        values.AddRange(leftSpelling.Select(static unit => (int)unit));
        values.AddRange(rightSpelling.Select(static unit => (int)unit));
        Assert.True(values.Count <= 16_384);
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
