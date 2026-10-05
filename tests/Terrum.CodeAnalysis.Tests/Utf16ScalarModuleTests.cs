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

public sealed class Utf16ScalarModuleTests
{
    private const string VerificationSource = """
        namespace Terrum.CodeAnalysis;
        int32 Main() { return DecodeRequestedScalar(); }

        public int32 DecodeRequestedScalar() {
            if (Host.Source.Length < 1) { return -2; }
            int32 position = int32(Host.Source[0]) + 1;
            int32 scalar = ReadUtf16Scalar(position, Host.Source.Length);
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
        var componentNames = new[] { "Utf16Scalar" };
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
            int32 Main() { return DecodeRequestedScalar(); }
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
    }

    private static int ExpectedResult(string text, int position)
    {
        var first = text[position];
        if (char.IsHighSurrogate(first))
        {
            if (position + 1 >= text.Length || !char.IsLowSurrogate(text[position + 1]))
            {
                return -1;
            }
            return char.ConvertToUtf32(first, text[position + 1]) * 2 + 2;
        }
        if (char.IsLowSurrogate(first))
        {
            return -1;
        }
        return first * 2 + 1;
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
