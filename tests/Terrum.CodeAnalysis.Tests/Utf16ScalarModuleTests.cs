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
using NUnit.Framework;

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

        public int32 VerifyBoundedBmpCodeUnits() {
            mut int32 position = 0;
            while (position < Host.Source.Length) {
                int32 codeUnit = int32(Host.Source[position]);
                int32 scalar = ReadUtf16Scalar(position, position + 1);
                if (codeUnit >= 55296 && codeUnit <= 57343) {
                    if (scalar != -1) { return position + 1; }
                } else if (scalar != codeUnit) {
                    return position + 1;
                }
                position = position + 1;
            }
            return 0;
        }

        public int32 VerifySupplementaryPairs() {
            if (Host.Source.Length < 2 || (Host.Source.Length - 2) % 2 != 0) { return -1; }
            mut int32 expected = int32(Host.Source[0]) | (int32(Host.Source[1]) << 16);
            mut int32 position = 2;
            mut int32 index = 0;
            while (position < Host.Source.Length) {
                int32 scalar = ReadUtf16Scalar(position, position + 2);
                if (scalar != expected || Utf16ScalarWidth(scalar) != 2) { return index + 1; }
                position = position + 2;
                expected = expected + 1;
                index = index + 1;
            }
            return 0;
        }
        """;
    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
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
            Assert.That(trees[index].HasErrors, Is.False, string.Join(Environment.NewLine, trees[index].Diagnostics));
        }
        trees[^1] = SyntaxTree.Parse(VerificationSource, "utf16-scalar-module.terrum");

        var producerCompilation = Compilation.Create(trees,
        [
            CreateSourceGrant("A", 0),
        ], policy);
        Assert.That(producerCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, producerCompilation.Diagnostics));
        var producer = ModuleCompiler.CreateProducer(
            ((global::System.Func<BoundProgram>)(() => { var __nunitActual0 = producerCompilation.Program; Assert.That(__nunitActual0, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual0!; }))(),
            [
                "Terrum.CodeAnalysis.DecodeRequestedScalar",
                "Terrum.CodeAnalysis.VerifyBoundedBmpCodeUnits",
                "Terrum.CodeAnalysis.VerifySupplementaryPairs",
            ]);

        var consumerCompilation = Compilation.Create(SyntaxTree.Parse("""
            namespace Terrum.CodeAnalysis;
            int32 Main() { return DecodeRequestedScalar(0); }
            """, "utf16-scalar-consumer.terrum"), ModuleCompiler.CreateReference(producer), policy);
        Assert.That(consumerCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, consumerCompilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(
            ((global::System.Func<BoundProgram>)(() => { var __nunitActual1 = consumerCompilation.Program; Assert.That(__nunitActual1, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual1!; }))(), producer);
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
            Assert.That(actual, Is.EqualTo(expected));
        }

        const string boundedText = "A\uD800\uDC00";
        var boundedConsumerCompilation = Compilation.Create(SyntaxTree.Parse("""
            namespace Terrum.CodeAnalysis;
            int32 Main() { return DecodeRequestedScalar(1); }
            """, "utf16-scalar-bounded-consumer.terrum"), ModuleCompiler.CreateReference(producer), policy);
        Assert.That(boundedConsumerCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, boundedConsumerCompilation.Diagnostics));
        var boundedConsumer = ModuleCompiler.CreateConsumer(
            ((global::System.Func<BoundProgram>)(() => { var __nunitActual2 = boundedConsumerCompilation.Program; Assert.That(__nunitActual2, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual2!; }))(), producer);
        var boundedLinked = ModuleCompiler.Link(boundedConsumer, producer);
        var boundedGrant = CreateSourceGrant(boundedText, 1);
        Assert.That(NativeJit.ExecuteWithSourceGrant(boundedLinked, [boundedGrant], (NativeRuntimePolicy)policy), Is.EqualTo(ExpectedResult(boundedText, 1, endOffset: 1)));

        var bmpConsumerCompilation = Compilation.Create(SyntaxTree.Parse("""
            namespace Terrum.CodeAnalysis;
            int32 Main() { return VerifyBoundedBmpCodeUnits(); }
            """, "utf16-scalar-bmp-consumer.terrum"), ModuleCompiler.CreateReference(producer), policy);
        Assert.That(bmpConsumerCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, bmpConsumerCompilation.Diagnostics));
        var bmpConsumer = ModuleCompiler.CreateConsumer(
            ((global::System.Func<BoundProgram>)(() => { var __nunitActual3 = bmpConsumerCompilation.Program; Assert.That(__nunitActual3, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual3!; }))(), producer);
        var bmpLinked = ModuleCompiler.Link(bmpConsumer, producer);

        for (var start = 0; start <= char.MaxValue; start += 16_384)
        {
            var count = Math.Min(16_384, char.MaxValue + 1 - start);
            var source = ImmutableArray.CreateBuilder<int>(count);
            for (var offset = 0; offset < count; offset++)
            {
                source.Add(start + offset);
            }

            var grant = new HostBuffer("Source", PrimitiveType.Char16, source.ToImmutable(), false) {
                IsInvocationScoped = true,
            };
            Assert.That(NativeJit.ExecuteWithSourceGrant(
                bmpLinked, [grant], (NativeRuntimePolicy)policy), Is.EqualTo(0));
        }

        var supplementaryConsumerCompilation = Compilation.Create(SyntaxTree.Parse("""
            namespace Terrum.CodeAnalysis;
            int32 Main() { return VerifySupplementaryPairs(); }
            """, "utf16-scalar-supplementary-consumer.terrum"),
            ModuleCompiler.CreateReference(producer), policy);
        Assert.That(supplementaryConsumerCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, supplementaryConsumerCompilation.Diagnostics));
        var supplementaryConsumer = ModuleCompiler.CreateConsumer(
            ((global::System.Func<BoundProgram>)(() => { var __nunitActual4 = supplementaryConsumerCompilation.Program; Assert.That(__nunitActual4, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual4!; }))(), producer);
        var supplementaryLinked = ModuleCompiler.Link(supplementaryConsumer, producer);

        const int firstSupplementaryScalar = 0x10000;
        const int supplementaryScalarLimit = 0x110000;
        const int maximumPairsPerGrant = 8_191;
        var pairBuffer = new char[2];
        for (var scalarStart = firstSupplementaryScalar;
            scalarStart < supplementaryScalarLimit;
            scalarStart += maximumPairsPerGrant)
        {
            var count = Math.Min(maximumPairsPerGrant, supplementaryScalarLimit - scalarStart);
            var source = ImmutableArray.CreateBuilder<int>(2 + count * 2);
            source.Add(scalarStart & 0xFFFF);
            source.Add(scalarStart >> 16);
            for (var index = 0; index < count; index++)
            {
                var rune = new Rune(scalarStart + index);
                _ = rune.EncodeToUtf16(pairBuffer);
                source.Add(pairBuffer[0]);
                source.Add(pairBuffer[1]);
            }

            var grant = new HostBuffer("Source", PrimitiveType.Char16, source.ToImmutable(), false) {
                IsInvocationScoped = true,
            };
            Assert.That(NativeJit.ExecuteWithSourceGrant(
                supplementaryLinked, [grant], (NativeRuntimePolicy)policy), Is.EqualTo(0));
        }
    }
    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
    public async Task Utf16CodeUnitsModuleDecodesScalarsAndWidthsWithoutHostSource(BoundRuntimePolicy policy)
    {
        var root = RepositoryRoot();
        var componentPath = Path.Combine(root, "self-host", "Utf16CodeUnits.terrum");
        var componentText = await File.ReadAllTextAsync(componentPath).ConfigureAwait(true);
        var componentTree = SyntaxTree.Parse(componentText, componentPath);
        Assert.That(componentTree.HasErrors, Is.False, string.Join(Environment.NewLine, componentTree.Diagnostics));
        var producerEntry = SyntaxTree.Parse("""
            namespace Terrum.CodeAnalysis;
            int32 Main() { return 0; }
            """, "utf16-code-units-module.terrum");
        var producerCompilation = Compilation.Create([componentTree, producerEntry], policy);
        Assert.That(producerCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, producerCompilation.Diagnostics));
        var producer = ModuleCompiler.CreateProducer(
            ((global::System.Func<BoundProgram>)(() => { var __nunitActual5 = producerCompilation.Program; Assert.That(__nunitActual5, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual5!; }))(),
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
        Assert.That(consumerTree.HasErrors, Is.False, string.Join(Environment.NewLine, consumerTree.Diagnostics));
        var consumerCompilation = Compilation.Create(consumerTree, ModuleCompiler.CreateReference(producer), policy);
        Assert.That(consumerCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, consumerCompilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(((global::System.Func<BoundProgram>)(() => { var __nunitActual6 = consumerCompilation.Program; Assert.That(__nunitActual6, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual6!; }))(), producer);
        var linked = ModuleCompiler.Link(consumer, producer);
        var actual = NativeJit.Execute(linked, (NativeRuntimePolicy)policy);
        Assert.That(actual == 0, Is.True, actual > 0 && actual <= fixtures.Length * 2
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
        Assert.That(values.Count <= 16_384, Is.True);
        return new HostBuffer("Source", PrimitiveType.Char16, values.ToImmutable(), false) {
            IsInvocationScoped = true,
        };
    }

    private static string RepositoryRoot() =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
}
