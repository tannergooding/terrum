// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.IO;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed class SourceRangeModuleTests
{
    [Theory]
    [InlineData(BoundRuntimePolicy.FailFast)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions)]
    public async Task SourceRangeExecutesAcrossPersistedSemanticModulesAsync(BoundRuntimePolicy policy)
    {
        var root = RepositoryRoot();
        var componentPath = Path.Combine(root, "self-host", "SourceRange.terrum");
        var componentText = await File.ReadAllTextAsync(componentPath).ConfigureAwait(true);
        var producerTree = SyntaxTree.Parse(componentText + """

            public int32 VerifySourceRange() {
                require SourceRange.TryCreate(0, 0, init var empty);
                if (empty.Start != 0 || empty.Length != 0 || empty.End != 0
                    || empty.GetEnd() != 0) { return 1; }

                require SourceRange.TryCreate(int32.MaxValue, 0, init var atEnd);
                if (atEnd.Start != int32.MaxValue || atEnd.Length != 0
                    || atEnd.End != int32.MaxValue || atEnd.GetEnd() != int32.MaxValue) { return 2; }

                require SourceRange.TryCreate(int32.MaxValue - 1, 1, init var finalCodeUnit);
                if (finalCodeUnit.Start != int32.MaxValue - 1 || finalCodeUnit.Length != 1
                    || finalCodeUnit.End != int32.MaxValue
                    || finalCodeUnit.GetEnd() != int32.MaxValue) { return 3; }

                require SourceRange.TryCreate(0, int32.MaxValue, init var fullExtent);
                if (fullExtent.Start != 0 || fullExtent.Length != int32.MaxValue
                    || fullExtent.End != int32.MaxValue) { return 4; }

                if (SourceRange.TryCreate(-1, 0, init var negativeStart)
                    || SourceRange.TryCreate(0, -1, init var negativeLength)
                    || SourceRange.TryCreate(int32.MaxValue, 1, init var overflowingEnd)
                    || SourceRange.TryCreate(1, int32.MaxValue, init var overflowingLength)) { return 5; }

                return 42;
            }

            int32 Main() { return 0; }
            """, componentPath);
        Assert.False(producerTree.HasErrors, string.Join(Environment.NewLine, producerTree.Diagnostics));
        var producerCompilation = Compilation.Create(producerTree, policy);
        Assert.False(producerCompilation.HasErrors,
            string.Join(Environment.NewLine, producerCompilation.Diagnostics));
        var producer = ModuleCompiler.CreateProducer(
            Assert.IsType<BoundProgram>(producerCompilation.Program),
            ["Terrum.CodeAnalysis.VerifySourceRange"]);

        var consumerTree = SyntaxTree.Parse("""
            namespace Terrum.CodeAnalysis;
            int32 Main() { return VerifySourceRange(); }
            """, "source-range-consumer.terrum");
        var consumerCompilation = Compilation.Create(
            consumerTree, ModuleCompiler.CreateReference(producer), policy);
        Assert.False(consumerCompilation.HasErrors,
            string.Join(Environment.NewLine, consumerCompilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(
            Assert.IsType<BoundProgram>(consumerCompilation.Program), producer);

        var consumerPath = Path.Combine(AppContext.BaseDirectory, $"source-range-{Guid.NewGuid():N}.trm");
        var producerPath = Path.ChangeExtension(consumerPath, ".producer.trm");
        try
        {
            await File.WriteAllBytesAsync(consumerPath, consumer).ConfigureAwait(true);
            await File.WriteAllBytesAsync(producerPath, producer).ConfigureAwait(true);
            var persistedConsumer = await File.ReadAllBytesAsync(consumerPath).ConfigureAwait(true);
            var persistedProducer = await File.ReadAllBytesAsync(producerPath).ConfigureAwait(true);
            var linked = ModuleCompiler.Link(persistedConsumer, persistedProducer);

            Assert.Equal(42, NativeJit.Execute(linked, (NativeRuntimePolicy)policy));
        }
        finally
        {
            File.Delete(consumerPath);
            File.Delete(producerPath);
        }
    }

    private static string RepositoryRoot() =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
}
