using System;
using System.Collections.Immutable;
using System.IO;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed class CharacterFactsModuleTests
{
    private const int BatchSize = 16_384;

    [Theory]
    [InlineData(BoundRuntimePolicy.FailFast)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions)]
    public async Task CharacterFactsModuleClassifiesEveryUtf16CodeUnit(BoundRuntimePolicy policy)
    {
        var root = RepositoryRoot();
        var componentPath = Path.Combine(root, "self-host", "CharacterFacts.terrum");
        var componentText = await File.ReadAllTextAsync(componentPath).ConfigureAwait(true);
        var componentTree = SyntaxTree.Parse(componentText, componentPath);
        Assert.False(componentTree.HasErrors, string.Join(Environment.NewLine, componentTree.Diagnostics));
        var verifierTree = SyntaxTree.Parse("""
            namespace Terrum.CodeAnalysis;
            int32 Main() { return VerifyBatch(); }

            int32 VerifyHexDigit(char16 value, int32 expected) {
                bool success = TryGetHexDigit(value, init var digit);
                if (success != (expected >= 0)) { return 1; }
                if (success) {
                    if (!(digit is init)) { return 2; }
                    if (digit != expected) { return 3; }
                } else {
                    if (digit is init) { return 4; }
                    if (!TryGetHexDigit(char16(48), init digit)) { return 5; }
                    if (!(digit is init)) { return 6; }
                    if (digit != 0) { return 7; }
                }
                return 0;
            }

            public int32 VerifyBatch() {
                mut int32 index = 0;
                while (index < Host.Source.Length) {
                    char16 value = Host.Source[index];
                    bool isDigit = value >= 48 && value <= 57;
                    if (IsAsciiDigit(value) != isDigit) { return index + 1; }
                    if (isDigit && GetDecimalDigit(value) != int32(value) - 48) { return index + 1; }
                    bool expectedLineBreak = value == 10 || value == 13 || value == 133
                        || value == 8232 || value == 8233;
                    if (IsLineBreak(value) != expectedLineBreak) { return index + 1; }
                    mut int32 expectedHexDigit = -1;
                    if (value >= 48 && value <= 57) { expectedHexDigit = int32(value) - 48; }
                    else if (value >= 65 && value <= 70) { expectedHexDigit = int32(value) - 55; }
                    else if (value >= 97 && value <= 102) { expectedHexDigit = int32(value) - 87; }
                    if (VerifyHexDigit(value, expectedHexDigit) != 0) { return index + 1; }
                    index = index + 1;
                }
                return 0;
            }
            """, "character-facts-module.terrum");
        var producerCompilation = Compilation.Create([componentTree, verifierTree],
        [
            new HostBuffer("Source", PrimitiveType.Char16, [0], false) {
                IsInvocationScoped = true,
            },
        ], policy);
        Assert.False(producerCompilation.HasErrors,
            string.Join(Environment.NewLine, producerCompilation.Diagnostics));
        var producer = ModuleCompiler.CreateProducer(
            Assert.IsType<BoundProgram>(producerCompilation.Program),
            ["Terrum.CodeAnalysis.VerifyBatch"]);

        var consumerCompilation = Compilation.Create(SyntaxTree.Parse("""
            namespace Terrum.CodeAnalysis;
            int32 Main() { return VerifyBatch(); }
            """, "character-facts-consumer.terrum"), ModuleCompiler.CreateReference(producer), policy);
        Assert.False(consumerCompilation.HasErrors,
            string.Join(Environment.NewLine, consumerCompilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(
            Assert.IsType<BoundProgram>(consumerCompilation.Program), producer);
        var linked = ModuleCompiler.Link(consumer, producer);

        for (var start = 0; start < char.MaxValue + 1; start += BatchSize)
        {
            var values = ImmutableArray.CreateBuilder<int>(BatchSize);
            for (var value = start; value < start + BatchSize; value++)
            {
                values.Add(value);
            }
            var grant = new HostBuffer("Source", PrimitiveType.Char16, values.MoveToImmutable(), false) {
                IsInvocationScoped = true,
            };
            Assert.Equal(0, NativeJit.ExecuteWithSourceGrant(linked, [grant], (NativeRuntimePolicy)policy));
        }
    }

    private static string RepositoryRoot() =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
}
