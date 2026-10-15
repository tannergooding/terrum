using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Lexing;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Terrum.CodeAnalysis.Text;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests;

public sealed partial class ScalarFrontendInvocationProfileTests
{
    private const string PrivateSpanProvider = """
        namespace Terrum.CodeAnalysis;
        int32 Main() { return 0; }
        internal int32 SpanCount(Span<Token> view) { return int32(view.Length); }
        internal Token ReadSpan(Span<Token> view, uint64 index) { return view[index]; }
        internal int32 VerifySpan(Span<Token> view, int32 count) {
            Span<Token> copy = view;
            mut int32 index = 0;
            while (index < count) {
                Token token = ReadSpan(index: uint64(index), view: copy);
                require token.Kind == Host.Tokens[index * 3];
                require token.Range.Start == Host.Tokens[index * 3 + 1];
                require token.Range.Length == Host.Tokens[index * 3 + 2];
                index = index + 1;
            }
            return SpanCount(copy);
        }
        public int32 InspectPrivateSpan() {
            int32 count = Scan();
            require TokenSnapshot.TryBuild(count, init var owner);
            Span<Token> view = owner.AsSpan();
            return VerifySpan(count: count, view: view);
        }
        """;
    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
    public async Task PrivateSpanModuleRoundTripsAndReplacesWithRemappedTypeRolesAsync(BoundRuntimePolicy policy)
    {
        var trees = await ReadComponentsAsync(s_scannerComponents, PrivateSpanProvider).ConfigureAwait(true);
        var compilation = Compilation.Create(trees,
            ScannerHost.CreateInvocationProfileBuffers(SourceText.From("seed")), policy);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var provider = ModuleCompiler.CreateProducer(((global::System.Func<BoundProgram>)(() => { var __nunitActual0 = compilation.Program; Assert.That(__nunitActual0, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual0!; }))(),
            ["Terrum.CodeAnalysis.InspectPrivateSpan"]);
        Assert.That(BinaryPrimitives.ReadInt32LittleEndian(provider.AsSpan(4)), Is.EqualTo(81));
        var consumerCompilation = Compilation.Create(SyntaxTree.Parse("""
            namespace Terrum.CodeAnalysis;
            struct ConsumerLocal {
                public int32 Value;
                public ConsumerLocal(int32 value) { Value = value; }
            }
            int32 Main() {
                var padding = new ConsumerLocal(0);
                return InspectPrivateSpan() + padding.Value;
            }
            """), ModuleCompiler.CreateReference(provider), policy);
        Assert.That(consumerCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, consumerCompilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(
            ((global::System.Func<BoundProgram>)(() => { var __nunitActual1 = consumerCompilation.Program; Assert.That(__nunitActual1, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual1!; }))(), provider);
        Assert.That(BinaryPrimitives.ReadInt32LittleEndian(consumer.AsSpan(4)), Is.EqualTo(76));

        var replacementTrees = await ReadComponentsAsync(
            s_scannerComponents.Reverse().ToArray(),
            PrivateSpanProvider.Replace("SpanCount(copy)",
                "int32(copy.Length)", StringComparison.Ordinal)).ConfigureAwait(true);
        var replacementCompilation = Compilation.Create(replacementTrees,
            ScannerHost.CreateInvocationProfileBuffers(SourceText.From("different seed")), policy);
        Assert.That(replacementCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, replacementCompilation.Diagnostics));
        var replacement = ModuleCompiler.CreateProducer(
            ((global::System.Func<BoundProgram>)(() => { var __nunitActual2 = replacementCompilation.Program; Assert.That(__nunitActual2, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual2!; }))(),
            ["Terrum.CodeAnalysis.InspectPrivateSpan"]);

        foreach (var artifact in new[] { provider, replacement })
        {
            var linked = ModuleCompiler.Link(consumer, artifact);
            Assert.That(global::System.Linq.Enumerable.Any(linked.Structures,static structure =>
                structure.Name == "$producer.Terrum.CodeAnalysis.Token"),Is.True);
            foreach (var text in new[] { "", "alpha 42", TenFunctions, "void A() { } void B() { }" })
            {
                var source = SourceText.From(text);
                var actual = NativeJit.ExecuteWithScannerProfile(linked, source, (NativeRuntimePolicy)policy);
                var expected = Lexer.Lex(source);
                Assert.That(actual.TokenCount, Is.EqualTo(expected.Tokens.Length));
                Assert.That(actual.LexResult.Tokens.Select(static token => (token.Kind, token.Span)), Is.EqualTo(expected.Tokens.Select(static token => (token.Kind, token.Span))));
            }
        }
        foreach (var version in new[] { 76, 69, 70, 71, 72, 73, 74, 75, 78, 80 })
        {
            var forged = provider.ToArray();
            BinaryPrimitives.WriteInt32LittleEndian(forged.AsSpan(4), version);
            Assert.Throws<ModuleFormatException>(() => ModuleCompiler.CreateReference(forged));
        }
    }
    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
    public async Task PrivateSpanModuleRejectsForgedRolesProvenanceIndexAndRetirementAsync(BoundRuntimePolicy policy)
    {
        var trees = await ReadComponentsAsync(s_scannerComponents, PrivateSpanProvider).ConfigureAwait(true);
        var compilation = Compilation.Create(trees,
            ScannerHost.CreateInvocationProfileBuffers(SourceText.From("seed")), policy);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual3 = compilation.Program; Assert.That(__nunitActual3, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual3!; }))();
        var provider = ModuleCompiler.CreateProducer(program, ["Terrum.CodeAnalysis.InspectPrivateSpan"]);
        var roles = ReadRoleOffsets(provider);
        var roleOffsets = new HashSet<int>(roles.Values);
        var revision76 = provider.Where((_, index) => !roleOffsets.Contains(index)).ToArray();
        BinaryPrimitives.WriteInt32LittleEndian(revision76.AsSpan(4), 76);
        Assert.Throws<ModuleFormatException>(() => ModuleCompiler.CreateReference(revision76));
        foreach (var (name, role) in new[] {
            ("Terrum.CodeAnalysis.Token", (byte)255),
            ("Terrum.CodeAnalysis.Token", (byte)2),
            ("Terrum.CodeAnalysis.TokenSnapshot", (byte)0),
        })
        {
            var forged = provider.ToArray();
            forged[roles[name]] = role;
            Assert.Throws<ModuleFormatException>(() => ModuleCompiler.CreateReference(forged));
        }
        var forgedStride = provider.ToArray();
        BinaryPrimitives.WriteInt32LittleEndian(forgedStride.AsSpan(roles["Terrum.CodeAnalysis.Token"] + 1), 16);
        Assert.Throws<ModuleFormatException>(() => ModuleCompiler.CreateReference(forgedStride));

        var inspect = global::System.Linq.Enumerable.Single(program.Functions, static function => function.Name == "Terrum.CodeAnalysis.InspectPrivateSpan");
        var snapshot = global::System.Linq.Enumerable.Single(SpanNodes(inspect.Body).OfType<BoundSnapshotSpan>());
        var drop = global::System.Linq.Enumerable.Single(SpanNodes(inspect.Body).OfType<BoundDropSpan>());
        var snapshotOffset = UniqueNodeOffset(provider, writer => {
            writer.Write((byte)32);
            writer.Write((int)PrimitiveType.TokenSpan);
            writer.Write(snapshot.Span.Start);
            writer.Write(snapshot.Span.Length);
            writer.Write(snapshot.Variable);
            writer.Write(snapshot.SourceBuffer);
        });
        var forgedSource = provider.ToArray();
        BinaryPrimitives.WriteInt32LittleEndian(forgedSource.AsSpan(snapshotOffset + 17), 1);
        Assert.Throws<ModuleFormatException>(() => ModuleCompiler.CreateReference(forgedSource));

        var dropOffset = UniqueNodeOffset(provider, writer => {
            writer.Write((byte)28);
            writer.Write(drop.Span.Start);
            writer.Write(drop.Span.Length);
            writer.Write(drop.Variable);
        });
        var forgedRetirement = provider.ToArray();
        BinaryPrimitives.WriteInt32LittleEndian(forgedRetirement.AsSpan(dropOffset + 9), snapshot.Variable);
        Assert.Throws<ModuleFormatException>(() => ModuleCompiler.CreateReference(forgedRetirement));

        var readFunction = global::System.Linq.Enumerable.Single(program.Functions, static function => function.Name == "Terrum.CodeAnalysis.ReadSpan");
        var read = global::System.Linq.Enumerable.Single(SpanNodes(readFunction.Body).OfType<BoundSpanRead>());
        var index = ((global::System.Func<BoundRead>)(() => { var __nunitActual4 = read.Index; Assert.That(__nunitActual4, Is.TypeOf<BoundRead>()); return (BoundRead)__nunitActual4!; }))();
        var indexOffset = UniqueNodeOffset(provider, writer => {
            writer.Write((byte)2);
            writer.Write((int)PrimitiveType.UInt64);
            writer.Write(index.Span.Start);
            writer.Write(index.Span.Length);
            writer.Write(index.Variable);
        });
        var forgedIndex = provider.ToArray();
        BinaryPrimitives.WriteInt32LittleEndian(forgedIndex.AsSpan(indexOffset + 1), (int)PrimitiveType.Int32);
        Assert.Throws<ModuleFormatException>(() => ModuleCompiler.CreateReference(forgedIndex));

        var publicTrees = await ReadComponentsAsync(s_scannerComponents,
            PrivateSpanProvider.Replace("internal int32 SpanCount", "public int32 SpanCount",
                StringComparison.Ordinal)).ConfigureAwait(true);
        var publicCompilation = Compilation.Create(publicTrees,
            ScannerHost.CreateInvocationProfileBuffers(SourceText.From("seed")), policy);
        Assert.That(publicCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, publicCompilation.Diagnostics));
        Assert.Throws<ModuleContractException>(() => ModuleCompiler.CreateProducer(
            ((global::System.Func<BoundProgram>)(() => { var __nunitActual5 = publicCompilation.Program; Assert.That(__nunitActual5, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual5!; }))(), ["Terrum.CodeAnalysis.SpanCount"]));
    }
    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
    public async Task PrivateSpanModuleKeepsReturnedViewsCapturesAndSourceFreeArtifactsClosedAsync(
        BoundRuntimePolicy policy)
    {
        var forwarded = PrivateSpanProvider.Replace("owner.AsSpan()", "Forward(owner.AsSpan())",
            StringComparison.Ordinal) + """

            [return: DependsOn(nameof(view))]
            internal Span<Token> Forward(Span<Token> view) { return view; }
            """;
        foreach (var (components, entry) in new[] {
            (s_scannerComponents, forwarded),
            (s_scannerComponents.Append("TokenCursor").ToArray(), PrivateSpanProvider),
        })
        {
            var trees = await ReadComponentsAsync(components, entry).ConfigureAwait(true);
            var compilation = Compilation.Create(trees,
                ScannerHost.CreateInvocationProfileBuffers(SourceText.From("seed")), policy);
            Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
            Assert.Throws<ModuleContractException>(() => ModuleCompiler.CreateProducer(
                ((global::System.Func<BoundProgram>)(() => { var __nunitActual6 = compilation.Program; Assert.That(__nunitActual6, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual6!; }))(), ["Terrum.CodeAnalysis.InspectPrivateSpan"]));
        }
        var sourceFreeTrees = await ReadComponentsAsync(s_scannerComponents, PrivateSpanProvider).ConfigureAwait(true);
        var sourceFreeCompilation = Compilation.Create(sourceFreeTrees,
            ScannerHost.CreateBuffers(SourceText.From("seed")), policy);
        Assert.That(sourceFreeCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, sourceFreeCompilation.Diagnostics));
        Assert.Throws<ModuleContractException>(() => ModuleCompiler.CreateProducer(
            ((global::System.Func<BoundProgram>)(() => { var __nunitActual7 = sourceFreeCompilation.Program; Assert.That(__nunitActual7, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual7!; }))(), ["Terrum.CodeAnalysis.InspectPrivateSpan"]));
    }
    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
    public async Task PrivateSpanModuleRechecksStagedBackingBeforeLaterOutputArgumentsAsync(BoundRuntimePolicy policy)
    {
        const string entry = """
            namespace Terrum.CodeAnalysis;
            int32 Main() { return 0; }
            internal bool Replace(out TokenSnapshot owner, int32 count) {
                require TokenSnapshot.TryBuild(count, init var fresh);
                owner = move fresh;
                return true;
            }
            internal int32 Observe(Span<Token> view, bool ignored) { return int32(view.Length); }
            public int32 Stage() {
                int32 count = Scan();
                require TokenSnapshot.TryBuild(count, init var scanned);
                mut TokenSnapshot owner = move scanned;
                require Replace(out owner, count);
                return Observe(owner.AsSpan(), false);
            }
            """;
        var trees = await ReadComponentsAsync(s_scannerComponents, entry).ConfigureAwait(true);
        var compilation = Compilation.Create(trees,
            ScannerHost.CreateInvocationProfileBuffers(SourceText.From("seed")), policy);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var producer = ModuleCompiler.CreateProducer(((global::System.Func<BoundProgram>)(() => { var __nunitActual8 = compilation.Program; Assert.That(__nunitActual8, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual8!; }))(),
            ["Terrum.CodeAnalysis.Stage"]);
        var consumerCompilation = Compilation.Create(
            SyntaxTree.Parse("namespace Terrum.CodeAnalysis; int32 Main() { return Stage(); }"),
            ModuleCompiler.CreateReference(producer), policy);
        Assert.That(consumerCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, consumerCompilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(
            ((global::System.Func<BoundProgram>)(() => { var __nunitActual9 = consumerCompilation.Program; Assert.That(__nunitActual9, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual9!; }))(), producer);
        var linked = ModuleCompiler.Link(consumer, producer);
        Assert.That(NativeJit.ExecuteWithScannerProfile(linked, SourceText.From("alpha 42"),
            (NativeRuntimePolicy)policy).TokenCount, Is.EqualTo(3));

        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual10 = compilation.Program; Assert.That(__nunitActual10, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual10!; }))();
        var stage = global::System.Linq.Enumerable.Single(program.Functions, static function => function.Name == "Terrum.CodeAnalysis.Stage");
        var owner = stage.Variables.IndexOf(global::System.Linq.Enumerable.Single(stage.Variables, static variable => variable.Name == "owner"));
        var count = stage.Variables.IndexOf(global::System.Linq.Enumerable.Single(stage.Variables, static variable => variable.Name == "count"));
        var snapshotType = 100 + ReadRoleOffsets(producer).Keys.ToList()
            .IndexOf("Terrum.CodeAnalysis.TokenSnapshot");
        var span = new TextSpan(entry.IndexOf("false", StringComparison.Ordinal), 5);
        var offset = UniqueNodeOffset(producer, writer => {
            writer.Write((byte)1);
            writer.Write((int)PrimitiveType.Bool);
            writer.Write(span.Start);
            writer.Write(span.Length);
            writer.Write(0L);
        });
        using var stream = new MemoryStream();
        using var artifactWriter = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        artifactWriter.Write((byte)5);
        artifactWriter.Write((int)PrimitiveType.Bool);
        artifactWriter.Write(span.Start);
        artifactWriter.Write(span.Length);
        var name = Encoding.UTF8.GetBytes("Terrum.CodeAnalysis.Replace");
        artifactWriter.Write(name.Length);
        artifactWriter.Write(name);
        artifactWriter.Write(2);
        artifactWriter.Write(false);
        artifactWriter.Write((byte)8);
        artifactWriter.Write(snapshotType);
        artifactWriter.Write(span.Start);
        artifactWriter.Write(span.Length);
        artifactWriter.Write(owner);
        artifactWriter.Write(false);
        artifactWriter.Write((byte)2);
        artifactWriter.Write((int)PrimitiveType.Int32);
        artifactWriter.Write(span.Start);
        artifactWriter.Write(span.Length);
        artifactWriter.Write(count);
        var forged = producer.AsSpan(0, offset).ToArray()
            .Concat(stream.ToArray()).Concat(producer.AsSpan(offset + 21).ToArray()).ToArray();
        Assert.Throws<ModuleFormatException>(() => ModuleCompiler.CreateReference(forged));
        Assert.Throws<ModuleFormatException>(() => ModuleCompiler.Link(consumer, forged));
    }
    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
    public async Task PrivateSpanModuleRebuildsConditionalBackingAndCleanupFactsAsync(BoundRuntimePolicy policy)
    {
        var cleanup = policy == BoundRuntimePolicy.CheckedExceptions ? """
            unsafe {
                try {
                    Span<Token> temporary = first.AsSpan();
                    require temporary.Length == uint64(count);
                    require false;
                } catch { }
            }
            """ : """
            {
                Span<Token> temporary = first.AsSpan();
                require temporary.Length == uint64(count);
            }
            """;
        var entry = $$"""
            namespace Terrum.CodeAnalysis;
            int32 Main() { return 0; }
            internal int32 SelectCount(Span<Token> left, Span<Token> right, bool choose) {
                Span<Token> selected = choose ? left : right;
                return int32(selected.Length);
            }
            public int32 CountWithCleanup() {
                int32 count = Scan();
                require TokenSnapshot.TryBuild(count, init var firstScanned);
                require TokenSnapshot.TryBuild(count, init var secondScanned);
                mut TokenSnapshot first = move firstScanned;
                mut TokenSnapshot second = move secondScanned;
                {{cleanup}}
                int32 observed = SelectCount(choose: true, right: second.AsSpan(), left: first.AsSpan());
                first = _;
                second = _;
                return observed;
            }
            """;
        var trees = await ReadComponentsAsync(s_scannerComponents, entry).ConfigureAwait(true);
        var compilation = Compilation.Create(trees,
            ScannerHost.CreateInvocationProfileBuffers(SourceText.From("seed")), policy);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var producer = ModuleCompiler.CreateProducer(((global::System.Func<BoundProgram>)(() => { var __nunitActual11 = compilation.Program; Assert.That(__nunitActual11, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual11!; }))(),
            ["Terrum.CodeAnalysis.CountWithCleanup"]);
        var consumerCompilation = Compilation.Create(SyntaxTree.Parse("""
            namespace Terrum.CodeAnalysis;
            int32 Main() { return CountWithCleanup(); }
            """), ModuleCompiler.CreateReference(producer), policy);
        Assert.That(consumerCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, consumerCompilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(
            ((global::System.Func<BoundProgram>)(() => { var __nunitActual12 = consumerCompilation.Program; Assert.That(__nunitActual12, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual12!; }))(), producer);
        var linked = ModuleCompiler.Link(consumer, producer);
        foreach (var text in new[] { "", "alpha 42", TenFunctions })
        {
            var source = SourceText.From(text);
            Assert.That(NativeJit.ExecuteWithScannerProfile(
                linked, source, (NativeRuntimePolicy)policy).TokenCount, Is.EqualTo(Lexer.Lex(source).Tokens.Length));
        }
    }

    private static Dictionary<string, int> ReadRoleOffsets(byte[] provider)
    {
        using var stream = new MemoryStream(provider, writable: false);
        using var reader = new BinaryReader(stream, Encoding.UTF8);
        _ = reader.ReadInt32();
        var version = reader.ReadInt32();
        Assert.That(version, Is.EqualTo(81));
        string ReadString() => Encoding.UTF8.GetString(reader.ReadBytes(reader.ReadInt32()));
        _ = ReadString();
        _ = reader.ReadBoolean();
        _ = reader.ReadByte();
        _ = reader.ReadBoolean();
        _ = ReadString();
        var bufferCount = reader.ReadInt32();
        for (var index = 0; index < bufferCount; index++)
        {
            _ = ReadString();
            _ = reader.ReadInt32();
            _ = reader.ReadBoolean();
        }
        _ = reader.ReadInt32();
        var count = reader.ReadInt32();
        var offsets = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var index = 0; index < count; index++)
        {
            var name = ReadString();
            offsets.Add(name, checked((int)stream.Position));
            _ = reader.ReadByte();
            _ = reader.ReadInt32();
            _ = reader.ReadBytes(4);
            if (version >= 78)
            {
                _ = reader.ReadInt32();
                _ = reader.ReadBoolean();
            }
            if (reader.ReadBoolean())
            {
                _ = ReadString();
            }
            var fieldCount = reader.ReadInt32();
            for (var field = 0; field < fieldCount; field++)
            {
                _ = ReadString();
                _ = reader.ReadBytes(16);
            }
        }
        return offsets;
    }

    private static int UniqueNodeOffset(byte[] provider, Action<BinaryWriter> write)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        write(writer);
        var bytes = stream.ToArray();
        var offset = provider.AsSpan().IndexOf(bytes);
        Assert.That(offset, Is.InRange(0, provider.Length - bytes.Length));
        Assert.That(provider.AsSpan().LastIndexOf(bytes), Is.EqualTo(offset));
        return offset;
    }

    private static IEnumerable<object> SpanNodes(object node)
    {
        var pending = new Stack<object>();
        pending.Push(node);
        while (pending.TryPop(out var current))
        {
            switch (current)
            {
                case BoundSnapshotSpan or BoundDropSpan:
                    yield return current;
                    break;
                case BoundSpanLength length:
                    yield return length;
                    pending.Push(length.View);
                    break;
                case BoundSpanRead read:
                    yield return read;
                    pending.Push(read.View);
                    pending.Push(read.Index);
                    break;
                case BoundBlock block:
                    foreach (var child in block.Statements)
                    {
                        pending.Push(child);
                    }
                    break;
                case BoundTryFinally region:
                    pending.Push(region.Body);
                    pending.Push(region.Finally);
                    break;
                case BoundLocal local:
                    pending.Push(local.Initializer);
                    break;
                case BoundReturn { Value: BoundExpression value }:
                    pending.Push(value);
                    break;
                case BoundCast cast:
                    pending.Push(cast.Operand);
                    break;
                case BoundCall call:
                    foreach (var argument in call.Arguments)
                    {
                        pending.Push(argument);
                    }
                    break;
                case BoundWhile loop:
                    pending.Push(loop.Body);
                    break;
            }
        }
    }
}
