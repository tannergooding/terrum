using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using Terrum.CodeAnalysis.Lexing;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Terrum.CodeAnalysis.Text;

namespace Terrum.CodeAnalysis.Tests.Semantics.Frontend;

internal sealed partial class ScalarFrontendInvocationProfileTests
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
    public async Task PrivateSpanModulesRejectSerializationAsync(BoundRuntimePolicy policy)
    {
        var trees = await ReadComponentsAsync(s_scannerComponents, PrivateSpanProvider).ConfigureAwait(true);
        var compilation = CreateWithLayoutCore(trees,
            ScannerHost.CreateInvocationProfileBuffers(
                SourceText.From("seed")), policy);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => {
            var __nunitActual0 = compilation.Program;
            Assert.That(__nunitActual0, Is.TypeOf<BoundProgram>());
            return (BoundProgram)__nunitActual0!;
        }))();
        _ = Assert.Throws<ModuleContractException>(() => ModuleCompiler.CreateProducer(
            program, ["Terrum.CodeAnalysis.InspectPrivateSpan"]));

        var scalarCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse("public int32 Value() { return 42; }"), policy);
        Assert.That(scalarCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, scalarCompilation.Diagnostics));
        var scalarProgram = ((global::System.Func<BoundProgram>)(() => {
            var __nunitActual1 = scalarCompilation.Program;
            Assert.That(__nunitActual1, Is.TypeOf<BoundProgram>());
            return (BoundProgram)__nunitActual1!;
        }))();
        var ordinaryModule = ModuleCompiler.CreateProducer(scalarProgram, ["Value"]);
        foreach (var version in new[] { 77, 81 })
        {
            var forged = ordinaryModule.ToArray();
            BinaryPrimitives.WriteInt32LittleEndian(forged.AsSpan(4), version);
            _ = Assert.Throws<ModuleFormatException>(() => ModuleCompiler.CreateReference(forged));
        }

        const string scalarSource = "public int32 Value() { return 42; }";
        var literalStart = scalarSource.IndexOf("42", StringComparison.Ordinal);
        var expression = new byte[21];
        expression[0] = 1;
        BinaryPrimitives.WriteInt32LittleEndian(expression.AsSpan(1), (int)PrimitiveType.Int32);
        BinaryPrimitives.WriteInt32LittleEndian(expression.AsSpan(5), literalStart);
        BinaryPrimitives.WriteInt32LittleEndian(expression.AsSpan(9), 2);
        BinaryPrimitives.WriteInt64LittleEndian(expression.AsSpan(13), 42);
        var expressionOffset = ordinaryModule.AsSpan().IndexOf(expression);
        Assert.That(expressionOffset, Is.GreaterThanOrEqualTo(0));
        Assert.That(ordinaryModule.AsSpan().LastIndexOf(expression), Is.EqualTo(expressionOffset));
        var forgedExpression = ordinaryModule.ToArray();
        forgedExpression[expressionOffset] = 32;
        BinaryPrimitives.WriteInt32LittleEndian(forgedExpression.AsSpan(expressionOffset + 1),
            (int)PrimitiveType.TokenSpan);
        _ = Assert.Throws<ModuleFormatException>(() => ModuleCompiler.CreateReference(forgedExpression));

        var structureCompilation = CreateLibraryWithLayoutCore(SyntaxTree.Parse("""
            [Blittable] [Layout(LayoutKind.Sequential)] public struct Data { public int32 Field; }
            public int32 ReadValue(Data value) { return value.Field; }
            """), policy);
        Assert.That(structureCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, structureCompilation.Diagnostics));
        var structureProgram = ((global::System.Func<BoundProgram>)(() => {
            var __nunitActual2 = structureCompilation.Program;
            Assert.That(__nunitActual2, Is.TypeOf<BoundProgram>());
            return (BoundProgram)__nunitActual2!;
        }))();
        var structureModule = ModuleCompiler.CreateProducer(structureProgram, ["ReadValue"]);
        var roleOffset = FirstStructureRoleOffset(structureModule);
        var forgedRole = structureModule.ToArray();
        forgedRole[roleOffset] = 1;
        _ = Assert.Throws<ModuleFormatException>(() => ModuleCompiler.CreateReference(forgedRole));
    }
    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
    public async Task PrivateSpanModulesRejectPublicContractsAsync(BoundRuntimePolicy policy)
    {
        var trees = await ReadComponentsAsync(s_scannerComponents,
            PrivateSpanProvider.Replace("internal int32 SpanCount", "public int32 SpanCount",
                StringComparison.Ordinal)).ConfigureAwait(true);
        var compilation = CreateWithLayoutCore(trees,
            ScannerHost.CreateInvocationProfileBuffers(
                SourceText.From("seed")), policy);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        _ = Assert.Throws<ModuleContractException>(() => ModuleCompiler.CreateProducer(
            ((global::System.Func<BoundProgram>)(() => {
                var __nunitActual2 = compilation.Program;
                Assert.That(__nunitActual2, Is.TypeOf<BoundProgram>());
                return (BoundProgram)__nunitActual2!;
            }))(), ["Terrum.CodeAnalysis.SpanCount"]));
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
            (s_scannerComponents.Append("Lexing/TokenCursor").ToArray(), PrivateSpanProvider),
        })
        {
            var trees = await ReadComponentsAsync(components, entry).ConfigureAwait(true);
            var compilation = CreateWithLayoutCore(trees,
                ScannerHost.CreateInvocationProfileBuffers(
                    SourceText.From("seed")), policy);
            Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
            _ = Assert.Throws<ModuleContractException>(() => ModuleCompiler.CreateProducer(
                ((global::System.Func<BoundProgram>)(() => {
                    var __nunitActual6 = compilation.Program;
                    Assert.That(__nunitActual6, Is.TypeOf<BoundProgram>());
                    return (BoundProgram)__nunitActual6!;
                }))(), ["Terrum.CodeAnalysis.InspectPrivateSpan"]));
        }
        var sourceFreeTrees = await ReadComponentsAsync(s_scannerComponents, PrivateSpanProvider).ConfigureAwait(true);
        var sourceFreeCompilation = CreateWithLayoutCore(sourceFreeTrees,
            ScannerHost.CreateBuffers(SourceText.From("seed")), policy);
        Assert.That(sourceFreeCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, sourceFreeCompilation.Diagnostics));
        _ = Assert.Throws<ModuleContractException>(() => ModuleCompiler.CreateProducer(
            ((global::System.Func<BoundProgram>)(() => {
                var __nunitActual7 = sourceFreeCompilation.Program;
                Assert.That(__nunitActual7, Is.TypeOf<BoundProgram>());
                return (BoundProgram)__nunitActual7!;
            }))(), ["Terrum.CodeAnalysis.InspectPrivateSpan"]));
    }
    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
    public async Task PrivateSpanStagedBackingCannotBeSerializedAsync(BoundRuntimePolicy policy)
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
        var compilation = CreateWithLayoutCore(trees,
            ScannerHost.CreateInvocationProfileBuffers(
                SourceText.From("seed")), policy);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => {
            var __nunitActual8 = compilation.Program;
            Assert.That(__nunitActual8, Is.TypeOf<BoundProgram>());
            return (BoundProgram)__nunitActual8!;
        }))();
        _ = Assert.Throws<ModuleContractException>(() => ModuleCompiler.CreateProducer(
            program, ["Terrum.CodeAnalysis.Stage"]));
    }
    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
    public async Task PrivateSpanConditionalCleanupCannotBeSerializedAsync(BoundRuntimePolicy policy)
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
        var compilation = CreateWithLayoutCore(trees,
            ScannerHost.CreateInvocationProfileBuffers(
                SourceText.From("seed")), policy);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => {
            var __nunitActual11 = compilation.Program;
            Assert.That(__nunitActual11, Is.TypeOf<BoundProgram>());
            return (BoundProgram)__nunitActual11!;
        }))();
        _ = Assert.Throws<ModuleContractException>(() => ModuleCompiler.CreateProducer(
            program, ["Terrum.CodeAnalysis.CountWithCleanup"]));
    }

    private static int FirstStructureRoleOffset(byte[] artifact)
    {
        using var stream = new MemoryStream(artifact, writable: false);
        using var reader = new BinaryReader(stream, Encoding.UTF8);
        _ = reader.ReadInt32();
        _ = reader.ReadInt32();
        string ReadString()
        {
            return Encoding.UTF8.GetString(reader.ReadBytes(reader.ReadInt32()));
        }

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
        Assert.That(reader.ReadInt32(), Is.GreaterThan(0));
        _ = ReadString();
        return checked((int)stream.Position);
    }
}
