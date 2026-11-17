using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Terrum.CodeAnalysis.Tests.Infrastructure;

namespace Terrum.CodeAnalysis.Tests.Lexing;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "NUnit instantiates this fixture through reflection.")]
internal sealed class CharacterFactsModuleTests
{
    private const int BatchSize = 16_384;
    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
    public async Task CharacterFactsModuleClassifiesEveryUtf16CodeUnit(BoundRuntimePolicy policy)
    {
        var root = RepositoryRoot();
        var componentPath = TerrumProjectPaths.RepositoryFile(root, "sources/Terrum.CodeAnalysis.Terrum/Lexing/CharacterFacts.terrum");
        var componentText = await File.ReadAllTextAsync(componentPath).ConfigureAwait(true);
        var componentTree = SyntaxTree.Parse(componentText, componentPath);
        Assert.That(componentTree.HasErrors, Is.False, string.Join(Environment.NewLine, componentTree.Diagnostics));
        var verifierTree = SyntaxTree.Parse("""
            namespace Terrum.CodeAnalysis;
            int32 Main() { return VerifyBatch(); }

            int32 VerifyHexDigit(char16 value, int32 expected) {
                init int32 digit;
                bool success = TryGetHexDigit(value, ref init digit);
                if (success != (expected >= 0)) { return 1; }
                if (success) {
                    if (!(digit is init)) { return 2; }
                    if (digit != expected) { return 3; }
                } else {
                    if (digit is init) { return 4; }
                    if (!TryGetHexDigit(char16(48), ref init digit)) { return 5; }
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
                    if (isDigit && GetDecimalDigit(value) != int32(value) - 48) { return 100_000 + index + 1; }
                    bool expectedLineBreak = value == 10 || value == 13 || value == 133
                        || value == 8232 || value == 8233;
                    if (IsLineBreak(value) != expectedLineBreak) { return 200_000 + index + 1; }
                    mut int32 expectedHexDigit = -1;
                    if (value >= 48 && value <= 57) { expectedHexDigit = int32(value) - 48; }
                    else if (value >= 65 && value <= 70) { expectedHexDigit = int32(value) - 55; }
                    else if (value >= 97 && value <= 102) { expectedHexDigit = int32(value) - 87; }
                    int32 hexResult = VerifyHexDigit(value, expectedHexDigit);
                    if (hexResult != 0) { return 300_000 + hexResult * 100_000 + index + 1; }
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
        Assert.That(producerCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, producerCompilation.Diagnostics));
        var producer = ModuleCompiler.CreateProducer(
            ((global::System.Func<BoundProgram>)(() => {
                var __nunitActual0 = producerCompilation.Program;
                Assert.That(__nunitActual0, Is.TypeOf<BoundProgram>());
                return (BoundProgram)__nunitActual0!;
            }))(),
            ["Terrum.CodeAnalysis.VerifyBatch"]);

        var consumerCompilation = Compilation.Create(SyntaxTree.Parse("""
            namespace Terrum.CodeAnalysis;
            int32 Main() { return VerifyBatch(); }
            """, "character-facts-consumer.terrum"), ModuleCompiler.CreateReference(producer), policy);
        Assert.That(consumerCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, consumerCompilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(
            ((global::System.Func<BoundProgram>)(() => {
                var __nunitActual1 = consumerCompilation.Program;
                Assert.That(__nunitActual1, Is.TypeOf<BoundProgram>());
                return (BoundProgram)__nunitActual1!;
            }))(), producer);
        var linked = ModuleCompiler.Link(consumer, producer);
        var linkedHexDigit = linked.Functions.Single(static function =>
            function.Name.EndsWith("TryGetHexDigit", StringComparison.Ordinal));
        var linkedOutput = linkedHexDigit.Variables[1];
        Assert.That(linkedOutput.IsInit, Is.True);
        Assert.That(linkedOutput.IsOut, Is.False);
        Assert.That(CheckedReferenceTypes.IsType(linkedOutput.Type), Is.True);
        Assert.That(linked.CheckedReferenceTypes[
            CheckedReferenceTypes.Index(linkedOutput.Type)].IsInitialization,
            Is.True);
        var linkedVerifier = linked.Functions.Single(static function =>
            function.Name.EndsWith("VerifyHexDigit", StringComparison.Ordinal));
        Assert.That(linkedVerifier.Variables.Single(static variable =>
            variable.Name == "digit").IsInitializationLocal, Is.True);

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
            Assert.That(NativeJit.ExecuteWithSourceGrant(
                producerCompilation.Program!, [grant],
                (NativeRuntimePolicy)policy), Is.EqualTo(0));
            Assert.That(NativeJit.ExecuteWithSourceGrant(linked, [grant], (NativeRuntimePolicy)policy), Is.EqualTo(0));
        }
    }

    private static string RepositoryRoot() =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
}
