// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed class TransparentLayoutTests
{
    private const string Library = """
        [Layout(Transparent)] public struct Number {
            public int32 Value;
            public Number(int32 value) { Value = value; }
        }
        public Number Wrap(int32 value) { return new Number(value); }
        public int32 Unwrap(Number value) { return value.Value; }
        """;

    [Theory]
    [InlineData(NativeRuntimePolicy.FailFast)]
    [InlineData(NativeRuntimePolicy.CheckedExceptions)]
    public async Task PublicScalarWrapperServicesUnchangedSemanticAndNativeConsumersAsync(NativeRuntimePolicy policy)
    {
        const string Consumer = "int32 Main() { return Unwrap(Wrap(42)); }";
        var program = CompileLibrary(Library, policy);
        var structure = Assert.Single(program.Structures);
        Assert.True(structure.IsTransparent);
        Assert.Equal(4, structure.Size);
        var producer = ModuleCompiler.CreateProducer(program, ["Wrap", "Unwrap"]);
        Assert.Equal(60,BinaryPrimitives.ReadInt32LittleEndian(producer.AsSpan(4, 4)));
        var reference = ModuleCompiler.CreateReference(producer);
        Assert.True(Assert.Single(reference.Structures).IsTransparent);
        var consumer = ModuleCompiler.CreateConsumer(CompileConsumer(Consumer, reference, policy), producer);
        var snapshot = SHA256.HashData(consumer);
        Assert.Equal(42, NativeJit.Execute(ModuleCompiler.Link(consumer, producer), policy));
        var replacementProgram = CompileLibrary(Library.Replace("return value.Value;", "return value.Value + 1;",
            StringComparison.Ordinal), policy);
        var replacement = ModuleCompiler.CreateProducer(replacementProgram, ["Wrap", "Unwrap"]);
        Assert.Equal(43, NativeJit.Execute(ModuleCompiler.Link(consumer, replacement), policy));
        Assert.Equal(snapshot, SHA256.HashData(consumer));

        var nativeProducer = NativeModuleCompiler.CreateProducer(program, ["Wrap", "Unwrap"], "NodeB", policy);
        var nativeConsumer = NativeModuleCompiler.CreateGraphConsumer(CompileConsumer(Consumer,
            NativeModuleCompiler.CreateGraphReference([nativeProducer]), policy), [nativeProducer], policy);
        var nativeSnapshot = SHA256.HashData(nativeConsumer);
        Assert.Equal(42, await NativeModuleCompiler.RunGraphAsync(nativeConsumer, [nativeProducer],
            TimeSpan.FromSeconds(10), policy).ConfigureAwait(true));
        var nativeReplacement = NativeModuleCompiler.CreateProducer(replacementProgram,
            ["Wrap", "Unwrap"], "NodeB", policy);
        NativeModuleCompiler.ValidateGraph(nativeConsumer, [nativeReplacement]);
        Assert.Equal(43, await NativeModuleCompiler.RunGraphAsync(nativeConsumer, [nativeReplacement],
            TimeSpan.FromSeconds(10), policy).ConfigureAwait(true));
        var framework = new DirectoryInfo(AppContext.BaseDirectory);
        var configuration = framework.Parent?.Name
            ?? throw new InvalidOperationException("Missing test configuration directory.");
        var apphost = Path.Combine(RepositoryRoot(), "artifacts", "bin", "sources", "terrumc",
            configuration, framework.Name, "terrumc.exe");
        Assert.Equal(43, await NativeModuleCompiler.RunGraphJitAsync(nativeConsumer, [nativeReplacement],
            apphost, TimeSpan.FromSeconds(30), policy).ConfigureAwait(true));
        Assert.Equal(nativeSnapshot, SHA256.HashData(nativeConsumer));

        var autoProgram = CompileLibrary(Library.Replace("[Layout(Transparent)] ", "", StringComparison.Ordinal), policy);
        var autoProducer = ModuleCompiler.CreateProducer(autoProgram, ["Wrap", "Unwrap"]);
        Assert.Equal(60,BinaryPrimitives.ReadInt32LittleEndian(autoProducer.AsSpan(4, 4)));
        _ = Assert.Throws<ModuleContractException>(() => ModuleCompiler.Link(consumer, autoProducer));
        var nativeAuto = NativeModuleCompiler.CreateProducer(autoProgram, ["Wrap", "Unwrap"], "NodeB", policy);
        _ = Assert.Throws<ModuleContractException>(() => NativeModuleCompiler.ValidateGraph(nativeConsumer, [nativeAuto]));
    }

    [Theory]
    [InlineData("[Layout(Transparent)] struct Empty { }")]
    [InlineData("[Layout(Transparent)] struct Pair { int32 A; int32 B; }")]
    [InlineData("[Layout(Transparent)] unique struct Owner { int32 Value; }")]
    [InlineData("[Layout(Transparent)] struct Inner { int32 Value; } [Layout(Transparent)] struct Outer { Inner Value; }")]
    [InlineData("[Layout(Transparent)] struct Unsupported { OwnedInt32Buffer Value; }")]
    [InlineData("[Layout(Transparent)] [Layout(Transparent)] struct Repeat { int32 Value; }")]
    public void UnsupportedTransparentDeclarationsDiagnose(string declaration)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(declaration + "int32 Main() { return 0; }"));
        Assert.True(compilation.HasErrors);
        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Code is "TRM3001" or "TRM3005");
    }

    [Theory]
    [InlineData("Number value = int32(42); return value.Value;", "TRM3007")]
    [InlineData("return Wrap(42);", "TRM3007")]
    public void TransparentValuesDoNotImplicitlyConvertToOrFromCarriers(string body, string diagnosticCode)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(Library + "int32 Main() { " + body + " }"));
        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Code == diagnosticCode);
    }

    [Fact]
    public void MalformedTransparentShapeAndWireFlagAreRejected()
    {
        var program = CompileLibrary(Library, NativeRuntimePolicy.FailFast);
        var structure = Assert.Single(program.Structures);
        _ = Assert.Throws<ArgumentException>(() => SemanticVerifier.VerifyLibrary(program with {
            Structures = [structure with { Fields = [] }],
        }));
        var producer = ModuleCompiler.CreateProducer(CompileLibrary(
            "public struct Pair { public int32 Left; public int32 Right; }"
            + " public int32 Read(Pair value) { return value.Left; }", NativeRuntimePolicy.FailFast), ["Read"]);
        var flag = new byte[] { 4, 0, 0, 0, (byte)'P', (byte)'a', (byte)'i', (byte)'r',
            8, 0, 0, 0, 0, 0, 0, 0 };
        var location = producer.AsSpan().IndexOf(flag);
        Assert.True(location >= 0);
        producer[location + flag.Length - 1] = 1;
        _ = Assert.Throws<ModuleFormatException>(() => ModuleCompiler.CreateReference(producer));
    }

    [Fact]
    public void PrivateTransparentDependencySelectsItsMetadataWithoutExportingItsType()
    {
        const string Source = """
            [Layout(Transparent)] struct Hidden {
                int64 Value;
                public Hidden(int64 value) { Value = value; }
                public int32 Read() { return int32(Value); }
            }
            public int32 Read() { Hidden value = new Hidden(42); return value.Read(); }
            """;
        var producer = ModuleCompiler.CreateProducer(CompileLibrary(Source, NativeRuntimePolicy.FailFast), ["Read"]);
        Assert.Equal(60,BinaryPrimitives.ReadInt32LittleEndian(producer.AsSpan(4, 4)));
        Assert.Empty(ModuleCompiler.CreateReference(producer).Structures);
        var consumer = ModuleCompiler.CreateConsumer(CompileConsumer("int32 Main() { return Read(); }",
            ModuleCompiler.CreateReference(producer), NativeRuntimePolicy.FailFast), producer);
        Assert.Equal(42, NativeJit.Execute(ModuleCompiler.Link(consumer, producer), NativeRuntimePolicy.FailFast));
    }

    [Theory]
    [InlineData(NativeRuntimePolicy.FailFast)]
    [InlineData(NativeRuntimePolicy.CheckedExceptions)]
    public async Task ManagedGraphIntermediateRetainsImportedTransparentValueAsync(NativeRuntimePolicy policy)
    {
        const string Middle = """
            class Box {
                public Number Value;
                public Box(Number value) { Value = value; }
            }
            public int32 Combine() {
                require Box.TryCreate(Wrap(42), out var box);
                return Unwrap(box.Value);
            }
            """;
        const string Root = "int32 Main() { return Combine(); }";
        var leaf = NativeModuleCompiler.CreateProducer(CompileLibrary(Library, policy),
            ["Wrap", "Unwrap"], "NodeB", policy);
        var middle = NativeModuleCompiler.CreateGraphProducer(CompileLibrary(Middle,
            NativeModuleCompiler.CreateGraphReference([leaf]), policy),
            ["Combine"], "NodeA", [leaf], policy);
        var root = NativeModuleCompiler.CreateGraphConsumer(CompileConsumer(Root,
            NativeModuleCompiler.CreateGraphReference([middle, leaf]), policy), [middle, leaf], policy);
        var snapshot = SHA256.HashData(root);
        Assert.Equal(42, await NativeModuleCompiler.RunGraphAsync(root, [middle, leaf],
            TimeSpan.FromSeconds(10), policy).ConfigureAwait(true));
        var replacement = NativeModuleCompiler.CreateProducer(CompileLibrary(
            Library.Replace("return value.Value;", "return value.Value + 1;", StringComparison.Ordinal), policy),
            ["Wrap", "Unwrap"], "NodeB", policy);
        NativeModuleCompiler.ValidateGraph(root, [middle, replacement]);
        Assert.Equal(43, await NativeModuleCompiler.RunGraphAsync(root, [replacement, middle],
            TimeSpan.FromSeconds(10), policy).ConfigureAwait(true));
        Assert.Equal(snapshot, SHA256.HashData(root));
    }

    private static BoundProgram CompileLibrary(string source, NativeRuntimePolicy policy)
        => CompileLibrary(source, null, policy);

    private static BoundProgram CompileLibrary(string source, CompilationReference? reference,
        NativeRuntimePolicy policy)
    {
        var tree = SyntaxTree.Parse(source);
        var compilation = reference is null
            ? Compilation.CreateLibrary(tree, (BoundRuntimePolicy)policy)
            : Compilation.CreateLibrary(tree, reference, (BoundRuntimePolicy)policy);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        return Assert.IsType<BoundProgram>(compilation.Program);
    }

    private static BoundProgram CompileConsumer(string source, CompilationReference reference,
        NativeRuntimePolicy policy)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(source), reference, (BoundRuntimePolicy)policy);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        return Assert.IsType<BoundProgram>(compilation.Program);
    }

    private static string RepositoryRoot() => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
        "..", "..", "..", "..", "..", ".."));
}
