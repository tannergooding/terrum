using System;
using System.IO;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed class CopyAlgorithmsModuleTests
{
    [Theory]
    [InlineData(BoundRuntimePolicy.FailFast)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions)]
    public async Task CopyAlgorithmSpecializationsPersistAcrossSemanticModuleConsumersAsync(
        BoundRuntimePolicy policy)
    {
        var root = RepositoryRoot();
        var componentNames = new[] { "SourceRange", "Token", "CopyAlgorithms" };
        var trees = new SyntaxTree[componentNames.Length + 1];
        for (var index = 0; index < componentNames.Length; index++)
        {
            var path = Path.Combine(root, "self-host", componentNames[index] + ".terrum");
            var text = await File.ReadAllTextAsync(path).ConfigureAwait(true);
            trees[index] = SyntaxTree.Parse(text, path);
            Assert.False(trees[index].HasErrors, string.Join(Environment.NewLine, trees[index].Diagnostics));
        }

        trees[^1] = SyntaxTree.Parse("""
            using Terrum;
            using Terrum.CodeAnalysis;

            public int32 VerifyCopyAlgorithms() {
                int64 selected = SelectCopy(true, int64.MinValue, int64.MaxValue);
                if (selected != int64.MinValue) { return 1; }
                bool selectedBoolean = SelectCopy(false, true, false);
                if (selectedBoolean) { return 2; }
                char16 selectedCodeUnit = SelectCopy(true, char16(55296), char16(65));
                if (selectedCodeUnit != char16(55296)) { return 3; }

                if (TrySelectCopyInit(2, int64(40), int64(42), init var initialized)) { return 4; }
                require TrySelectCopyInit(1, int64(40), int64(42), init initialized);
                if (initialized != int64(42)) { return 5; }
                if (TrySelectCopy(-1, int64(40), int64(42), out var absentCopy)) { return 6; }
                require TrySelectCopy(0, int64(40), int64(42), out var copied);
                if (copied != int64(40)) { return 7; }

                require SourceRange.TryCreate(12, 5, init var firstRange);
                require SourceRange.TryCreate(42, 7, init var secondRange);
                Token first = new Token(1, firstRange);
                Token second = new Token(2, secondRange);
                Token selectedToken = SelectCopy(false, first, second);
                if (selectedToken.Kind != 2 || selectedToken.Range.Start != 42
                    || selectedToken.Range.Length != 7) { return 8; }
                if (TrySelectCopyInit(2, first, second, init var initializedToken)) { return 9; }
                require TrySelectCopyInit(0, first, second, init initializedToken);
                if (initializedToken.Kind != 1 || initializedToken.Range.Start != 12
                    || initializedToken.Range.Length != 5) { return 10; }
                return 0;
            }

            int32 Main() { return VerifyCopyAlgorithms(); }
            """, "copy-algorithms-module.terrum");
        Assert.False(trees[^1].HasErrors, string.Join(Environment.NewLine, trees[^1].Diagnostics));

        var producerCompilation = Compilation.Create(trees, policy);
        Assert.False(producerCompilation.HasErrors,
            string.Join(Environment.NewLine, producerCompilation.Diagnostics));
        var producer = ModuleCompiler.CreateProducer(
            Assert.IsType<BoundProgram>(producerCompilation.Program), ["VerifyCopyAlgorithms"]);

        var consumerTree = SyntaxTree.Parse("""
            int32 Main() { return VerifyCopyAlgorithms(); }
            """, "copy-algorithms-consumer.terrum");
        var consumerCompilation = Compilation.Create(consumerTree, ModuleCompiler.CreateReference(producer), policy);
        Assert.False(consumerCompilation.HasErrors,
            string.Join(Environment.NewLine, consumerCompilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(
            Assert.IsType<BoundProgram>(consumerCompilation.Program), producer);
        var linked = ModuleCompiler.Link(consumer, producer);

        Assert.Equal(0, NativeJit.Execute(linked, (NativeRuntimePolicy)policy));
    }

    private static string RepositoryRoot() =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
}
