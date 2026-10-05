// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using System.Diagnostics;
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

public sealed partial class SourceLineComponentTests
{
    [Theory]
    [InlineData("")]
    [InlineData("plain")]
    [InlineData("\r")]
    [InlineData("\n")]
    [InlineData("\r\n")]
    [InlineData("\r\r\n\n\r")]
    [InlineData("a\r\nb\rc\nd\u0085e\u2028f\u2029")]
    [InlineData("\uD800\r\n\uDC00\0\uD800\uDC00")]
    [InlineData("\u000B\u000C\u001C\u001D\u001E\u001F\u00A0")]
    public async Task CompiledLinesAndEveryPositionAgreeWithSourceSnapshotAsync(string text)
    {
        await CheckAsync(text).ConfigureAwait(true);
    }

    [Fact]
    public async Task AlternatingBreaksAndSurrogatesPreserveAllOffsetsAsync()
    {
        await CheckAsync(string.Concat(Enumerable.Repeat("a\r\n\uD800\u2028\uDC00\n", 64))).ConfigureAwait(true);
    }

    [Fact]
    public async Task SourceFacadeModulesBindFreshSnapshotsAndRejectInvalidGrantsAsync()
    {
        var trees = await ReadComponentsAsync(includeTests: false).ConfigureAwait(true);
        var entry = SyntaxTree.Parse("""
            namespace Terrum.CodeAnalysis;
            int32 Main() { return 0; }
            public int32 GetLastSourceCodeUnitOrMinusOne() {
                if (Host.Source.Length == 0) { return -1; }
                return int32(Host.Source[^1]);
            }
            int32 SourceIndex() { return 0; }
            public int32 GetFirstSourceCodeUnitOrMinusOne() {
                if (Host.Source.Length == 0) { return -1; }
                return int32(Host.Source[SourceIndex()]);
            }
            """, "source-grant-producer-entry.terrum");
        var consumerTree = SyntaxTree.Parse("""
            namespace Terrum.CodeAnalysis;
            int32 Main() {
                int32 length = GetSourceTextLength();
                if (TryReadSourceCodeUnit(length, init var outOfRange)) { return -7; }
                if (!TryGetSourceRange(0, length, init var whole)) { return -2; }
                int32 last = GetLastSourceCodeUnitOrMinusOne();
                int32 first = GetFirstSourceCodeUnitOrMinusOne();
                mut int32 checksum = (length + whole.Start + whole.Length + last + first + 2 * 65521) % 65521;
                mut int32 at = 0;
                while (at < length) {
                    if (!TryReadSourceCodeUnit(at, init var unit)) { return -3; }
                    checksum = (checksum + (int32(unit) * ((at % 97) + 1)) % 65521) % 65521;
                    at = at + 1;
                }
                at = 0;
                while (at <= length) {
                    if (!TryGetSourcePosition(at, init var line, init var character)) { return -4; }
                    if (!TryGetSourceOffset(line, character, init var offset)) { return -5; }
                    if (offset != at) { return -6; }
                    checksum = (checksum + (line * 31 + character) % 65521) % 65521;
                    at = at + 1;
                }
                mut int32 lineIndex = 0;
                mut bool done = false;
                while (!done) {
                    if (TryGetSourceLineExtent(lineIndex, init var extent)) {
                        checksum = (checksum + extent.Start + extent.Length) % 65521;
                        lineIndex = lineIndex + 1;
                    } else {
                        done = true;
                    }
                }
                return checksum;
            }
            """, "source-grant-consumer.terrum");
        var snapshots = new[] {
            "",
            "plain",
            "\r",
            "\n",
            "\r\n",
            "a\r\nb\rc\nd\u0085e\u2028f\u2029",
            "\uD800\r\n\uDC00\0\uD800\uDC00",
        };

        foreach (var policy in new[] { BoundRuntimePolicy.FailFast, BoundRuntimePolicy.CheckedExceptions })
        {
            var producerCompilation = Compilation.Create(trees.Add(entry),
                [new HostBuffer("Source", PrimitiveType.Char16, [0x1357, 0x2468, 0x369A], false) {
                    IsInvocationScoped = true,
                }], policy);
            Assert.False(producerCompilation.HasErrors,
                string.Join(Environment.NewLine, producerCompilation.Diagnostics));
            var producer = ModuleCompiler.CreateProducer(Assert.IsType<BoundProgram>(producerCompilation.Program),
                ["Terrum.CodeAnalysis.GetSourceTextLength", "Terrum.CodeAnalysis.TryReadSourceCodeUnit",
                    "Terrum.CodeAnalysis.TryGetSourceRange", "Terrum.CodeAnalysis.TryReadSourceLine",
                    "Terrum.CodeAnalysis.TryGetSourcePosition", "Terrum.CodeAnalysis.TryGetSourceOffset",
                    "Terrum.CodeAnalysis.TryGetSourceLineExtent",
                    "Terrum.CodeAnalysis.GetLastSourceCodeUnitOrMinusOne",
                    "Terrum.CodeAnalysis.GetFirstSourceCodeUnitOrMinusOne"]);
            Assert.Equal(-1, producer.AsSpan().IndexOf(new byte[] { 0x57, 0x13, 0x68, 0x24, 0x9A, 0x36 }));
            Assert.True(ModuleCompiler.CreateReference(producer).RequiresSourceGrant);

            var consumerCompilation = Compilation.Create(consumerTree, ModuleCompiler.CreateReference(producer), policy);
            Assert.False(consumerCompilation.HasErrors,
                string.Join(Environment.NewLine, consumerCompilation.Diagnostics));
            var consumerProgram = Assert.IsType<BoundProgram>(consumerCompilation.Program);
            Assert.True(consumerProgram.RequiresSourceGrant);
            Assert.Throws<NotSupportedException>(() =>
                NativeEmitter.EmitWithImports(consumerProgram, [], (NativeRuntimePolicy)policy));
            var consumer = ModuleCompiler.CreateConsumer(consumerProgram, producer);
            var linked = ModuleCompiler.Link(consumer, producer);
            Assert.True(linked.RequiresSourceGrant);
            Assert.Throws<ArgumentException>(() => NativeJit.Execute(linked, (NativeRuntimePolicy)policy));
            Assert.Throws<NotSupportedException>(() => NativeEmitter.Emit(linked, (NativeRuntimePolicy)policy));

            var neutralCompilation = Compilation.Create(SyntaxTree.Parse("""
                namespace Terrum.CodeAnalysis;
                public int32 Different() { return 0; }
                int32 Main() { return 0; }
                """, "source-grant-neutral-producer.terrum"), policy);
            Assert.False(neutralCompilation.HasErrors,
                string.Join(Environment.NewLine, neutralCompilation.Diagnostics));
            var neutralProducer = ModuleCompiler.CreateProducer(Assert.IsType<BoundProgram>(neutralCompilation.Program),
                ["Terrum.CodeAnalysis.Different"]);
            Assert.Throws<ModuleContractException>(() => ModuleCompiler.Link(consumer, neutralProducer));
            Assert.Throws<ModuleContractException>(() => ModuleCompiler.CreateReferenceSet([producer, neutralProducer]));
            var neutralLengthCompilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
                namespace Terrum.CodeAnalysis;
                public int32 GetSourceTextLength() { return 0; }
                """, "source-grant-neutral-length.terrum"), policy);
            Assert.False(neutralLengthCompilation.HasErrors,
                string.Join(Environment.NewLine, neutralLengthCompilation.Diagnostics));
            var neutralLengthProducer = ModuleCompiler.CreateProducer(
                Assert.IsType<BoundProgram>(neutralLengthCompilation.Program),
                ["Terrum.CodeAnalysis.GetSourceTextLength"]);
            Assert.Throws<ModuleContractException>(() =>
                ModuleCompiler.CreateConsumer(consumerProgram, neutralLengthProducer));

            foreach (var text in snapshots)
            {
                var expected = ComputeSourceFacadeChecksum(SourceText.From(text));
                var actual = NativeJit.ExecuteWithSourceGrant(linked, [CreateSourceGrant(text)],
                    (NativeRuntimePolicy)policy);
                Assert.True(expected == actual, $"Source snapshot '{text}': expected {expected}, got {actual}.");
            }

            var vacantOutputCompilation = Compilation.Create(SyntaxTree.Parse("""
                namespace Terrum.CodeAnalysis;
                int32 Main() {
                    if (TryReadSourceCodeUnit(-1, init var codeUnit)) { return 1; }
                    return int32(codeUnit);
                }
                """, "source-grant-vacant-output.terrum"), ModuleCompiler.CreateReference(producer), policy);
            Assert.True(vacantOutputCompilation.HasErrors);
            Assert.Contains(vacantOutputCompilation.Diagnostics, diagnostic => diagnostic.Code == "TRM3016");

            var lengthConsumerCompilation = Compilation.Create(SyntaxTree.Parse("""
                namespace Terrum.CodeAnalysis;
                int32 Main() { return GetSourceTextLength(); }
                """, "source-grant-length-consumer.terrum"), ModuleCompiler.CreateReference(producer), policy);
            Assert.False(lengthConsumerCompilation.HasErrors,
                string.Join(Environment.NewLine, lengthConsumerCompilation.Diagnostics));
            var lengthConsumer = ModuleCompiler.CreateConsumer(
                Assert.IsType<BoundProgram>(lengthConsumerCompilation.Program), producer);
            var lengthLinked = ModuleCompiler.Link(lengthConsumer, producer);
            Assert.Equal(16_384, NativeJit.ExecuteWithSourceGrant(lengthLinked,
                [CreateSourceGrant(new string('x', 16_384))], (NativeRuntimePolicy)policy));

            var valid = CreateSourceGrant("");
            Assert.Throws<ArgumentException>(() => NativeJit.ExecuteWithSourceGrant(linked, [], (NativeRuntimePolicy)policy));
            Assert.Throws<ArgumentException>(() => NativeJit.ExecuteWithSourceGrant(linked, [valid, valid],
                (NativeRuntimePolicy)policy));
            Assert.Throws<ArgumentException>(() => NativeJit.ExecuteWithSourceGrant(linked,
                [valid with { IsWritable = true }], (NativeRuntimePolicy)policy));
            Assert.Throws<ArgumentException>(() => NativeJit.ExecuteWithSourceGrant(linked,
                [new HostBuffer("Source", PrimitiveType.Int32, [1], false) { IsInvocationScoped = true }],
                (NativeRuntimePolicy)policy));
            Assert.Throws<ArgumentException>(() => NativeJit.ExecuteWithSourceGrant(linked,
                [new HostBuffer("Other", PrimitiveType.Char16, [65], false) { IsInvocationScoped = true }],
                (NativeRuntimePolicy)policy));
            Assert.Throws<ArgumentException>(() => NativeJit.ExecuteWithSourceGrant(linked,
                [new HostBuffer("Source", PrimitiveType.Char16, [-1], false) { IsInvocationScoped = true }],
                (NativeRuntimePolicy)policy));
            Assert.Throws<ArgumentException>(() => NativeJit.ExecuteWithSourceGrant(linked,
                [new HostBuffer("Source", PrimitiveType.Char16, [ushort.MaxValue + 1], false) {
                    IsInvocationScoped = true,
                }], (NativeRuntimePolicy)policy));
            Assert.Throws<ArgumentException>(() => NativeJit.ExecuteWithSourceGrant(lengthLinked,
                [CreateSourceGrant(new string('x', 16_385))], (NativeRuntimePolicy)policy));
        }
    }

    [Fact]
    public void PersistedSourceReadsUseInvocationLengthAndResolveCallsInIndexes()
    {
        var sourceTree = SyntaxTree.Parse("""
            namespace Terrum.CodeAnalysis;
            int32 Main() { return 0; }
            int32 SourceIndex() { return 0; }
            public int32 GetLastSourceCodeUnitOrMinusOne() {
                if (Host.Source.Length == 0) { return -1; }
                return int32(Host.Source[^1]);
            }
            public int32 GetFirstSourceCodeUnitOrMinusOne() {
                if (Host.Source.Length == 0) { return -1; }
                return int32(Host.Source[SourceIndex()]);
            }
            """, "source-grant-index-producer.terrum");
        var producerCompilation = Compilation.Create(sourceTree,
            [new HostBuffer("Source", PrimitiveType.Char16, [65, 66, 67], false) {
                IsInvocationScoped = true,
            }]);
        Assert.False(producerCompilation.HasErrors,
            string.Join(Environment.NewLine, producerCompilation.Diagnostics));
        var producer = ModuleCompiler.CreateProducer(Assert.IsType<BoundProgram>(producerCompilation.Program),
            ["Terrum.CodeAnalysis.GetLastSourceCodeUnitOrMinusOne",
                "Terrum.CodeAnalysis.GetFirstSourceCodeUnitOrMinusOne"]);
        var consumerCompilation = Compilation.Create(SyntaxTree.Parse("""
            namespace Terrum.CodeAnalysis;
            int32 Main() { return GetLastSourceCodeUnitOrMinusOne(); }
            """, "source-grant-index-consumer.terrum"), ModuleCompiler.CreateReference(producer));
        Assert.False(consumerCompilation.HasErrors,
            string.Join(Environment.NewLine, consumerCompilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(Assert.IsType<BoundProgram>(consumerCompilation.Program),
            producer);
        var linked = ModuleCompiler.Link(consumer, producer);

        foreach (var text in new[] { "", "plain", "\r", "\uD83D\uDE00", "\uD800" })
        {
            var source = SourceText.From(text);
            var last = source.Length == 0 ? -1 : source[source.Length - 1];
            Assert.Equal(last, NativeJit.ExecuteWithSourceGrant(linked, [CreateSourceGrant(text)],
                NativeRuntimePolicy.FailFast));
        }
    }

    [Theory]
    [InlineData("bool ok = TryReadSourceLine(-1, init var content, init var including); return content.Start;")]
    [InlineData("bool ok = TryReadSourceLine(-1, init var content, init var including); return including.Length;")]
    [InlineData("bool ok = TryGetSourcePosition(-1, init var line, init var character); return line;")]
    [InlineData("bool ok = TryGetSourcePosition(-1, init var line, init var character); return character;")]
    [InlineData("bool ok = TryGetSourceOffset(-1, 0, init var offset); return offset;")]
    [InlineData("bool ok = TryGetSourceOffset(0, 2, init var offset); return offset;")]
    [InlineData("bool ok = TryGetSourceLineExtent(-1, init var extent); return extent.Start;")]
    [InlineData("bool ok = TryGetSourceLineExtent(1, init var extent); return extent.Length;")]
    [InlineData("bool ok = TryReadSourceCodeUnit(-1, init var codeUnit); return int32(codeUnit);")]
    [InlineData("bool ok = TryGetSourceRange(-1, 0, init var range); return range.Start;")]
    public async Task FailedQueriesCannotPublishReadableOutputsAsync(string body)
    {
        var trees = await ReadComponentsAsync(includeTests: false).ConfigureAwait(true);
        var entry = SyntaxTree.Parse($"namespace Terrum.CodeAnalysis; int32 Main() {{ {body} }}", "entry.terrum");
        var compilation = Compilation.Create(trees.Add(entry),
            [new HostBuffer("Source", PrimitiveType.Char16, [65], false)]);
        Assert.True(compilation.HasErrors);
        Assert.Null(compilation.Program);
        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Code == "TRM3016");
        Assert.Contains(compilation.SourceDiagnostics,
            diagnostic => diagnostic.SyntaxTree == entry && diagnostic.Diagnostic.Code == "TRM3016");
    }

    [Fact]
    public void MultipleSnapshotsPreserveHostGrantRestrictionsAndDiagnosticOwnership()
    {
        var entry = SyntaxTree.Parse("int32 Main() { Write(); return 0; }", "entry.terrum");
        var write = SyntaxTree.Parse("void Write() { Host.Source[0] = char16(66); }", "write.terrum");
        var compilation = Compilation.Create([entry, write],
            [new HostBuffer("Source", PrimitiveType.Char16, [65], false)]);
        Assert.True(compilation.HasErrors);
        Assert.Null(compilation.Program);
        Assert.Collection(compilation.SyntaxTrees, tree => Assert.Same(entry, tree), tree => Assert.Same(write, tree));
        Assert.NotEmpty(compilation.SourceDiagnostics);
        Assert.All(compilation.SourceDiagnostics, diagnostic => Assert.Same(write, diagnostic.SyntaxTree));
    }

    private static async Task CheckAsync(string text)
    {
        var source = SourceText.From(text);
        var lines = ImmutableArray.CreateBuilder<int>();
        foreach (var line in source.Lines)
        {
            lines.Add(line.Span.Start);
            lines.Add(line.Span.Length);
            lines.Add(line.SpanIncludingLineBreak.Length);
        }

        var positions = ImmutableArray.CreateBuilder<int>();
        for (var position = 0; position <= source.Length; position++)
        {
            var location = source.GetLinePosition(position);
            positions.Add(location.Line);
            positions.Add(location.Character);
        }

        var trees = await ReadComponentsAsync(includeTests: true).ConfigureAwait(true);
        const string Harness = """
            namespace Terrum.CodeAnalysis;
            int32 Main() {
                int32 first = CheckSourceLines();
                if (first != 0) { return first; }
                return CheckSourceLines();
            }
            """;
        foreach (var policy in new[] { NativeRuntimePolicy.FailFast, NativeRuntimePolicy.CheckedExceptions })
        {
            var compilation = Compilation.Create(trees.Add(SyntaxTree.Parse(Harness, "entry.terrum")),
            [
                new HostBuffer("Source", PrimitiveType.Char16, [.. text.Select(unit => (int)unit)], false),
                new HostBuffer("ExpectedLines", PrimitiveType.Int32, lines.ToImmutable(), false),
                new HostBuffer("ExpectedPositions", PrimitiveType.Int32, positions.ToImmutable(), false),
            ], (BoundRuntimePolicy)policy);
            Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
            var program = Assert.IsType<BoundProgram>(compilation.Program);
            Assert.Equal(0, NativeJit.Execute(program, policy));
            await ExecuteAsync(program, policy).ConfigureAwait(true);
        }
    }

    private static async Task ExecuteAsync(BoundProgram program, NativeRuntimePolicy policy, int expectedExit = 0)
    {
        var path = Path.Combine(RepositoryRoot(), "artifacts", $"source-lines-{Guid.NewGuid():N}.exe");
        try
        {
            await File.WriteAllBytesAsync(path, NativeEmitter.Emit(program, policy)).ConfigureAwait(true);
            using var process = Process.Start(new ProcessStartInfo(path) { UseShellExecute = false, CreateNoWindow = true })
                ?? throw new InvalidOperationException("Could not start compiled source-line component.");
            try
            {
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(true);
            }
            catch (TimeoutException)
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync().ConfigureAwait(true);
                }
                throw;
            }
            Assert.Equal(expectedExit, process.ExitCode);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static async Task<ImmutableArray<SyntaxTree>> ReadComponentsAsync(bool includeTests,
        bool includeLines = true)
    {
        var trees = ImmutableArray.CreateBuilder<SyntaxTree>();
        var names = includeLines || includeTests
            ? new[] { "SourceRange.terrum", "CharacterFacts.terrum", "SourceText.terrum", "SourceLines.terrum" }
            : new[] { "SourceRange.terrum", "CharacterFacts.terrum", "SourceText.terrum" };
        foreach (var name in includeTests ? [.. names, "SourceLineTests.terrum"] : names)
        {
            var path = Path.Combine(RepositoryRoot(), "self-host", name);
            var text = await File.ReadAllTextAsync(path).ConfigureAwait(true);
            var tree = SyntaxTree.Parse(text, path);
            Assert.False(tree.HasErrors, string.Join(Environment.NewLine, tree.Diagnostics));
            Assert.Equal(text, tree.Root.ToFullString());
            trees.Add(tree);
        }
        return trees.ToImmutable();
    }

    private static HostBuffer CreateSourceGrant(string text) =>
        new("Source", PrimitiveType.Char16, [.. text.Select(static unit => (int)unit)], false) {
            IsInvocationScoped = true,
        };

    private static int ComputeSourceFacadeChecksum(SourceText source)
    {
        var last = source.Length == 0 ? -1 : source[source.Length - 1];
        var first = source.Length == 0 ? -1 : source[0];
        var checksum = (source.Length * 2 + last + first + 2 * 65_521) % 65_521;
        for (var index = 0; index < source.Length; index++)
        {
            checksum = (checksum + (source[index] * ((index % 97) + 1)) % 65_521) % 65_521;
        }
        for (var position = 0; position <= source.Length; position++)
        {
            var line = source.GetLinePosition(position);
            checksum = (checksum + (line.Line * 31 + line.Character) % 65_521) % 65_521;
        }
        foreach (var line in source.Lines)
        {
            checksum = (checksum + line.SpanIncludingLineBreak.Start
                + line.SpanIncludingLineBreak.Length) % 65_521;
        }
        return checksum;
    }

    private static string RepositoryRoot() => Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
}
