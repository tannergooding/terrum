// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.IO;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using NUnit.Framework;

using Terrum.CodeAnalysis.Tests.Infrastructure;

namespace Terrum.CodeAnalysis.Tests.Lexing;

public sealed class ManagedTokenChainModuleTests
{
    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
    public async Task ManagedTokenChainExecutesAcrossPersistedSemanticModulesAsync(BoundRuntimePolicy policy)
    {
        var root = RepositoryRoot();
        var componentNames = new[] { "sources/Terrum.CodeAnalysis.Terrum/Text/SourceRange", "sources/Terrum.CodeAnalysis.Terrum/Lexing/Token", "sources/Terrum.CodeAnalysis.Terrum/Lexing/ManagedTokenChain" };
        var trees = new SyntaxTree[componentNames.Length + 1];
        for (var index = 0; index < componentNames.Length; index++)
        {
            var path = TerrumProjectPaths.RepositoryFile(root, componentNames[index] + ".terrum");
            trees[index] = SyntaxTree.Parse(await File.ReadAllTextAsync(path).ConfigureAwait(true), path);
            Assert.That(trees[index].HasErrors, Is.False, string.Join(Environment.NewLine, trees[index].Diagnostics));
        }

        trees[^1] = SyntaxTree.Parse("""
            namespace Terrum.CodeAnalysis;
            int32 Main() { return 0; }

            public int32 VerifyManagedTokenChain() {
                require ManagedTokenChain.TryCreate(out var chain);
                if (chain.Count != 0 || chain.Version != 0 || chain.TryClear()) { return 1; }
                if (!chain.TryAppend(7, 10, 2) || !chain.TryAppend(8, 12, 1)) { return 2; }
                if (chain.Count != 2 || chain.Version != 2) { return 3; }
                if (chain.TryAppend(-1, 13, 1) || chain.TryAppend(9, 11, 1)
                    || chain.TryAppend(9, 13, -1) || chain.TryAppend(9, int32.MaxValue, 1)) { return 4; }
                if (chain.Count != 2 || chain.Version != 2) { return 5; }
                if (chain.TryGet(-1, init var negative) || chain.TryGet(2, init var pastEnd)) { return 6; }
                require chain.TryGet(0, init var firstCopy);
                require chain.TryGet(1, init var secondCopy);
                if (firstCopy.Kind != 7 || firstCopy.Range.Start != 10
                    || firstCopy.Range.Length != 2 || firstCopy.Range.End != 12) { return 7; }
                if (secondCopy.Kind != 8 || secondCopy.Range.Start != 12
                    || secondCopy.Range.Length != 1 || secondCopy.Range.End != 13) { return 8; }
                require chain.TryClear();
                if (chain.Count != 0 || chain.Version != 3 || chain.TryClear()) { return 9; }
                if (ManagedHeap.Collect() != 2) { return 10; }
                if (firstCopy.Kind != 7 || firstCopy.Range.Start != 10
                    || firstCopy.Range.Length != 2 || secondCopy.Kind != 8
                    || secondCopy.Range.Start != 12 || secondCopy.Range.Length != 1) { return 11; }

                require ManagedTokenChain.TryCreate(out var cursorChain);
                require cursorChain.TryAppend(7, 0, 1);
                require cursorChain.TryAppend(8, 1, 1);
                require cursorChain.TryAppend(9, 2, 1);
                {
                    require cursorChain.TryCreateCursor(out var cursor);
                    if (cursor.TryPeekAhead(-1, init var negativeDistance)
                        || cursor.TryPeekAhead(3, init var outside)) { return 12; }
                    require cursor.TryPeekAhead(2, init var third);
                    require cursor.TryPeekAhead(1, init var second);
                    if (third.Kind != 9 || second.Kind != 8) { return 13; }
                    if (cursor.TryReadKind(8, init var mismatch)) { return 14; }
                    require cursor.TryReadKind(7, init var first);
                    if (first.Kind != 7 || first.Range.Start != 0) { return 15; }
                    require cursor.TryRead(init var next);
                    if (next.Kind != 8 || next.Range.Start != 1) { return 16; }
                    require cursor.TryPeek(init var remaining);
                    if (remaining.Kind != 9 || cursor.TryPeekAhead(2, init var beyond)) { return 17; }
                    require cursor.TryReadKind(9, init var lastRead);
                    if (lastRead.Kind != 9 || cursor.TryRead(init var atEnd) || !cursor.IsCurrent) { return 18; }

                    require cursorChain.TryCreateCursor(out var staleByAppend);
                    require cursorChain.TryAppend(10, 3, 1);
                    if (staleByAppend.IsCurrent || staleByAppend.TryPeek(init var invalidatedByAppend)) { return 19; }

                    require cursorChain.TryCreateCursor(out var staleByClear);
                    require cursorChain.TryClear();
                    if (cursorChain.Count != 0 || cursorChain.Version != 5
                        || staleByClear.IsCurrent || staleByClear.TryRead(init var invalidatedByClear)) { return 20; }
                }
                for (mut int32 index = 0; index < 64; index = index + 1) {
                    require cursorChain.TryAppend(index, index, 1);
                }
                if (cursorChain.Count != 64 || cursorChain.Version != 69
                    || cursorChain.TryAppend(64, 64, 1)) { return 21; }
                return 42;
            }
            """, "managed-token-chain-verifier.terrum");
        Assert.That(trees[^1].HasErrors, Is.False, string.Join(Environment.NewLine, trees[^1].Diagnostics));

        var producerCompilation = Compilation.Create(trees, policy);
        Assert.That(producerCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, producerCompilation.Diagnostics));
        var producer = ModuleCompiler.CreateProducer(
            ((global::System.Func<BoundProgram>)(() => { var __nunitActual0 = producerCompilation.Program; Assert.That(__nunitActual0, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual0!; }))(),
            ["Terrum.CodeAnalysis.VerifyManagedTokenChain"]);

        var consumerTree = SyntaxTree.Parse("""
            namespace Terrum.CodeAnalysis;
            int32 Main() { return VerifyManagedTokenChain(); }
            """, "managed-token-chain-consumer.terrum");
        var consumerCompilation = Compilation.Create(
            consumerTree, ModuleCompiler.CreateReference(producer), policy);
        Assert.That(consumerCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, consumerCompilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(
            ((global::System.Func<BoundProgram>)(() => { var __nunitActual1 = consumerCompilation.Program; Assert.That(__nunitActual1, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual1!; }))(), producer);

        var consumerPath = Path.Combine(TestArtifactPaths.DirectoryPath, $"managed-token-chain-{Guid.NewGuid():N}.trm");
        var producerPath = Path.ChangeExtension(consumerPath, ".producer.trm");
        try
        {
            await File.WriteAllBytesAsync(consumerPath, consumer).ConfigureAwait(true);
            await File.WriteAllBytesAsync(producerPath, producer).ConfigureAwait(true);
            var persistedConsumer = await File.ReadAllBytesAsync(consumerPath).ConfigureAwait(true);
            var persistedProducer = await File.ReadAllBytesAsync(producerPath).ConfigureAwait(true);
            var linked = ModuleCompiler.Link(persistedConsumer, persistedProducer);

            Assert.That(NativeJit.Execute(linked, (NativeRuntimePolicy)policy), Is.EqualTo(42));
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
