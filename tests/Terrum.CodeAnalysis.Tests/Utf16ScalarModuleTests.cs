using System;
using System.Buffers;
using System.Collections.Immutable;
using System.Globalization;
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

public sealed class Utf16ScalarModuleTests
{
    private const string VerificationSource = """
        namespace Terrum.CodeAnalysis;
        int32 Main() { return DecodeRequestedScalar(0); }

        public int32 DecodeRequestedScalar(int32 endOffset) {
            if (Host.Source.Length < 1) { return -2; }
            int32 position = int32(Host.Source[0]) + 1;
            int32 scalar = ReadUtf16Scalar(position, Host.Source.Length - endOffset);
            if (scalar < 0) { return -1; }
            return scalar * 2 + Utf16ScalarWidth(scalar);
        }
        """;

    [Theory]
    [InlineData(BoundRuntimePolicy.FailFast)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions)]
    public async Task Utf16ScalarModuleDecodesFreshSourceGrants(BoundRuntimePolicy policy)
    {
        var root = RepositoryRoot();
        var componentNames = new[] { "Utf16CodeUnits", "Utf16Scalar" };
        var trees = new SyntaxTree[componentNames.Length + 1];
        for (var index = 0; index < componentNames.Length; index++)
        {
            var path = Path.Combine(root, "self-host", componentNames[index] + ".terrum");
            var text = await File.ReadAllTextAsync(path).ConfigureAwait(true);
            trees[index] = SyntaxTree.Parse(text, path);
            Assert.False(trees[index].HasErrors, string.Join(Environment.NewLine, trees[index].Diagnostics));
        }
        trees[^1] = SyntaxTree.Parse(VerificationSource, "utf16-scalar-module.terrum");

        var producerCompilation = Compilation.Create(trees,
        [
            CreateSourceGrant("A", 0),
        ], policy);
        Assert.False(producerCompilation.HasErrors,
            string.Join(Environment.NewLine, producerCompilation.Diagnostics));
        var producer = ModuleCompiler.CreateProducer(
            Assert.IsType<BoundProgram>(producerCompilation.Program),
            ["Terrum.CodeAnalysis.DecodeRequestedScalar"]);

        var consumerCompilation = Compilation.Create(SyntaxTree.Parse("""
            namespace Terrum.CodeAnalysis;
            int32 Main() { return DecodeRequestedScalar(0); }
            """, "utf16-scalar-consumer.terrum"), ModuleCompiler.CreateReference(producer), policy);
        Assert.False(consumerCompilation.HasErrors,
            string.Join(Environment.NewLine, consumerCompilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(
            Assert.IsType<BoundProgram>(consumerCompilation.Program), producer);
        var linked = ModuleCompiler.Link(consumer, producer);

        foreach (var fixture in new[] {
            ("A", 0),
            ("é", 0),
            ("\0", 0),
            ("😀", 0),
            ("😀", 1),
            ("\uD800", 0),
            ("\uDC00", 0),
            ("A\uD800B", 1),
            ("A\uDC00B", 1),
            ("A😀B", 1),
            ("A😀B", 2),
        })
        {
            var expected = ExpectedResult(fixture.Item1, fixture.Item2);
            var grant = CreateSourceGrant(fixture.Item1, fixture.Item2);
            var actual = NativeJit.ExecuteWithSourceGrant(linked, [grant], (NativeRuntimePolicy)policy);
            Assert.Equal(expected, actual);
        }

        const string boundedText = "A\uD800\uDC00";
        var boundedConsumerCompilation = Compilation.Create(SyntaxTree.Parse("""
            namespace Terrum.CodeAnalysis;
            int32 Main() { return DecodeRequestedScalar(1); }
            """, "utf16-scalar-bounded-consumer.terrum"), ModuleCompiler.CreateReference(producer), policy);
        Assert.False(boundedConsumerCompilation.HasErrors,
            string.Join(Environment.NewLine, boundedConsumerCompilation.Diagnostics));
        var boundedConsumer = ModuleCompiler.CreateConsumer(
            Assert.IsType<BoundProgram>(boundedConsumerCompilation.Program), producer);
        var boundedLinked = ModuleCompiler.Link(boundedConsumer, producer);
        var boundedGrant = CreateSourceGrant(boundedText, 1);
        Assert.Equal(ExpectedResult(boundedText, 1, endOffset: 1),
            NativeJit.ExecuteWithSourceGrant(boundedLinked, [boundedGrant], (NativeRuntimePolicy)policy));
    }

    [Theory]
    [InlineData(BoundRuntimePolicy.FailFast)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions)]
    public async Task Utf16CodeUnitsModuleDecodesScalarsAndWidthsWithoutHostSource(BoundRuntimePolicy policy)
    {
        var root = RepositoryRoot();
        var componentPath = Path.Combine(root, "self-host", "Utf16CodeUnits.terrum");
        var componentText = await File.ReadAllTextAsync(componentPath).ConfigureAwait(true);
        var componentTree = SyntaxTree.Parse(componentText, componentPath);
        Assert.False(componentTree.HasErrors, string.Join(Environment.NewLine, componentTree.Diagnostics));
        var producerEntry = SyntaxTree.Parse("""
            namespace Terrum.CodeAnalysis;
            int32 Main() { return 0; }
            """, "utf16-code-units-module.terrum");
        var producerCompilation = Compilation.Create([componentTree, producerEntry], policy);
        Assert.False(producerCompilation.HasErrors,
            string.Join(Environment.NewLine, producerCompilation.Diagnostics));
        var producer = ModuleCompiler.CreateProducer(
            Assert.IsType<BoundProgram>(producerCompilation.Program),
            ["Terrum.CodeAnalysis.DecodeUtf16Scalar", "Terrum.CodeAnalysis.Utf16ScalarWidth"]);

        var fixtures = new (char First, char Second, bool HasSecond)[] {
            ('\0', '\uDC00', true),
            ('\uFDD0', '\0', false),
            ('\uFFFF', '\0', false),
            ('\uD800', '\uDC00', true),
            ('\uDBFF', '\uDFFF', true),
            ('\uD800', '\0', false),
            ('\uDC00', '\0', false),
            ('\uD800', 'A', true),
            ('\uD800', '\uDBFF', true),
        };
        var expectedResults = fixtures.Select(static fixture =>
            ExpectedDecode(fixture.First, fixture.Second, fixture.HasSecond)).ToArray();
        var checks = new StringBuilder();
        for (var index = 0; index < fixtures.Length; index++)
        {
            var fixture = fixtures[index];
            var expected = expectedResults[index];
            checks.Append("int32 scalar").Append(index).Append(" = DecodeUtf16Scalar(char16(")
                .Append(((int)fixture.First).ToString(CultureInfo.InvariantCulture)).Append("), char16(")
                .Append(((int)fixture.Second).ToString(CultureInfo.InvariantCulture)).AppendLine("));");
            checks.Append("if (scalar").Append(index).Append(" != ")
                .Append(expected.Scalar.ToString(CultureInfo.InvariantCulture))
                .Append(") { return ").Append((index + 1).ToString(CultureInfo.InvariantCulture)).AppendLine("; }");
            checks.Append("if (Utf16ScalarWidth(scalar").Append(index).Append(") != ")
                .Append(expected.Width.ToString(CultureInfo.InvariantCulture)).Append(") { return ")
                .Append((fixtures.Length + index + 1).ToString(CultureInfo.InvariantCulture)).AppendLine("; }");
        }
        var consumerTree = SyntaxTree.Parse(
            "namespace Terrum.CodeAnalysis;\nint32 Main() {\n" + checks + "return 0;\n}",
            "utf16-code-units-consumer.terrum");
        Assert.False(consumerTree.HasErrors, string.Join(Environment.NewLine, consumerTree.Diagnostics));
        var consumerCompilation = Compilation.Create(consumerTree, ModuleCompiler.CreateReference(producer), policy);
        Assert.False(consumerCompilation.HasErrors,
            string.Join(Environment.NewLine, consumerCompilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(Assert.IsType<BoundProgram>(consumerCompilation.Program), producer);
        var linked = ModuleCompiler.Link(consumer, producer);
        var actual = NativeJit.Execute(linked, (NativeRuntimePolicy)policy);
        Assert.True(actual == 0,
            actual > 0 && actual <= fixtures.Length * 2
                ? $"UTF-16 fixture {((actual - 1) % fixtures.Length) + 1} failed: "
                    + $"expected {expectedResults[(actual - 1) % fixtures.Length].Status} "
                    + $"with scalar {expectedResults[(actual - 1) % fixtures.Length].Scalar}."
                : $"Expected no failed UTF-16 checks, got consumer result {actual}.");
    }

    private static int ExpectedResult(string text, int position, int endOffset = 0)
    {
        var (scalar, width, _) = ExpectedDecode(text[position], position + 1 < text.Length - endOffset
            ? text[position + 1]
            : '\0', position + 1 < text.Length - endOffset);
        return scalar < 0 ? -1 : scalar * 2 + width;
    }

    private static (int Scalar, int Width, OperationStatus Status) ExpectedDecode(
        char first, char second, bool hasSecond)
    {
        Span<char> units = stackalloc char[2];
        units[0] = first;
        var length = 1;
        if (hasSecond)
        {
            units[1] = second;
            length = 2;
        }
        var status = Rune.DecodeFromUtf16(units[..length], out var rune, out _);
        var scalar = status == OperationStatus.Done ? rune.Value : -1;
        return (scalar, scalar >= 65536 ? 2 : 1, status);
    }

    private static HostBuffer CreateSourceGrant(string text, int position)
    {
        var values = ImmutableArray.CreateBuilder<int>(text.Length + 1);
        values.Add(position);
        values.AddRange(text.Select(static unit => (int)unit));
        Assert.True(values.Count <= 16_384);
        return new HostBuffer("Source", PrimitiveType.Char16, values.ToImmutable(), false) {
            IsInvocationScoped = true,
        };
    }

    private static string RepositoryRoot() =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
}
