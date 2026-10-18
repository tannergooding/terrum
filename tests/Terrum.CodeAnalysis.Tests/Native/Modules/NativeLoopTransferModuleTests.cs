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
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests.Native.Modules;

public sealed class NativeLoopTransferModuleTests
{
    private const string StructuredLeaf = """
        public int32 Leaf() {
            mut int32 sum = 0;
            outer: for (mut int32 i = 0; i < 5; i = i + 1) {
                do {
                    if (i == 0) { continue outer; }
                    if (i == 3) { break outer; }
                    sum = sum + 20;
                    break;
                } while (true);
            }
            return sum + 2;
        }
        """;
    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public async Task CurrentSemanticFormatRoundTripsPostTestedNamedLoopsAsync(NativeRuntimePolicy policy)
    {
        var previous = NativeModuleCompiler.CreateProducer(Compile("public int32 Leaf() { return 1; }", policy),
            ["Leaf"], "NodeB", policy);
        var replacementProgram = Compile(StructuredLeaf, policy);
        var semantic = ModuleCompiler.CreateProducer(replacementProgram, ["Leaf"]);
        Assert.That(VersionOf(semantic), Is.EqualTo(76));
        var reference = ModuleCompiler.CreateReference(semantic);
        var consumer = ModuleCompiler.CreateConsumer(Compile("""
            int32 Main() {
                mut int32 answer = 0;
                do { answer = Leaf(); } while (false);
                return answer;
            }
            """, policy, reference, library: false), semantic);
        Assert.That(VersionOf(consumer), Is.EqualTo(76));
        SemanticVerifier.Verify(ModuleCompiler.Link(consumer, semantic));
        var downgraded = (byte[])semantic.Clone();
        BinaryPrimitives.WriteInt32LittleEndian(downgraded.AsSpan(4, 4), 50);
        _ = Assert.Throws<ModuleFormatException>(() => ModuleCompiler.CreateReference(downgraded))!;
        var replacement = NativeModuleCompiler.CreateProducer(replacementProgram, ["Leaf"], "NodeB", policy);
        var middle = NativeModuleCompiler.CreateGraphProducer(Compile(
            "public int32 Middle() { return Leaf(); }", policy,
            NativeModuleCompiler.CreateGraphReference([previous])), ["Middle"], "NodeA", [previous], policy);
        var root = NativeModuleCompiler.CreateGraphConsumer(Compile(
            "int32 Main() { return Middle(); }", policy,
            NativeModuleCompiler.CreateGraphReference([middle]), library: false), [middle], policy);
        NativeModuleCompiler.ValidateGraph(root, [middle, replacement]);
        Assert.That(await NativeModuleCompiler.RunGraphAsync(root, [middle, previous],
            TimeSpan.FromSeconds(10), policy).ConfigureAwait(true), Is.EqualTo(1));
        Assert.That(await NativeModuleCompiler.RunGraphAsync(root, [middle, replacement],
            TimeSpan.FromSeconds(10), policy).ConfigureAwait(true), Is.EqualTo(42));
        Assert.That(await NativeModuleCompiler.RunGraphJitAsync(root, [middle, replacement],
            Apphost(), TimeSpan.FromSeconds(30), policy).ConfigureAwait(true), Is.EqualTo(42));
    }

    private const string Leaf = """
        int32 Advance(int32 value) { return value + 1; }
        public int32 Leaf() {
            mut int32 result = 0;
            for (mut int32 i = 0; i < 5; i = Advance(i)) {
                if (i == 1) { continue; }
                mut int32 j = 0;
                while (j < 3) {
                    j = j + 1;
                    if (j == 1) { continue; }
                    if (j == 3) { break; }
                    result = result + 1;
                }
                if (i == 3) { break; }
                result = result + 10;
            }
            return result;
        }
        """;

    private const string ClassLeaf = """
        class Box {
            public int32 Value;
            public Box(int32 value) { Value = value; }
        }
        public int32 Leaf() {
            require Box.TryCreate(42, out var box);
            while (true) { break; }
            return box.Value;
        }
        """;
    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public void CurrentSemanticFormatRoundTripsNearestLoopAndForIterator(NativeRuntimePolicy policy)
    {
        var producer = ModuleCompiler.CreateProducer(Compile(Leaf, policy), ["Leaf"]);
        Assert.That(VersionOf(producer), Is.EqualTo(76));
        Assert.That(ModuleCompiler.GetNativeProfileVersion(producer, false), Is.EqualTo(policy == NativeRuntimePolicy.FailFast ? "native-10" : "native-11"));
        _ = global::System.Linq.Enumerable.Single(ModuleCompiler.CreateReference(producer).Functions);
        var consumer = ModuleCompiler.CreateConsumer(Compile("""
            int32 Main() {
                mut int32 result = Leaf();
                while (true) { result = result + 1; break; }
                return result;
            }
            """, policy, ModuleCompiler.CreateReference(producer), library: false), producer);
        Assert.That(VersionOf(consumer), Is.EqualTo(76));
        var linked = ModuleCompiler.Link(consumer, producer);
        Assert.That(global::System.Linq.Enumerable.Any(linked.Functions,static function => function.Name == "Leaf" && !function.IsExternal),Is.True);
        Assert.That(global::System.Linq.Enumerable.Any(linked.Functions,static function => function.Name == "$producer.Advance"),Is.True);
        var old = ModuleCompiler.CreateProducer(Compile("""
            public int32 Leaf() {
                mut int32 sum = 0;
                for (mut int32 i = 0; i < 2; i = i + 1) { sum = sum + i; }
                return sum;
            }
            """, policy), ["Leaf"]);
        Assert.That(VersionOf(old), Is.EqualTo(76));
        _ = ModuleCompiler.CreateReference(old);
    }
    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public async Task CurrentSemanticFormatLeafReplacesUnchangedNativeGraphAsync(NativeRuntimePolicy policy)
    {
        var original = NativeModuleCompiler.CreateProducer(Compile("public int32 Leaf() { return 20; }", policy),
            ["Leaf"], "NodeB", policy);
        var middle = NativeModuleCompiler.CreateGraphProducer(Compile(
            "public int32 Middle() { return Leaf() + 1; }", policy,
            NativeModuleCompiler.CreateGraphReference([original])), ["Middle"], "NodeA", [original], policy);
        var root = NativeModuleCompiler.CreateGraphConsumer(Compile(
            "int32 Main() { return Middle() + 1; }", policy,
            NativeModuleCompiler.CreateGraphReference([middle]), library: false), [middle], policy);
        var hashes = (SHA256.HashData(root), SHA256.HashData(middle));
        var replacement = NativeModuleCompiler.CreateProducer(Compile(Leaf, policy), ["Leaf"], "NodeB", policy);
        Assert.That(NativeModuleCompiler.Inspect(replacement).NativeProfileVersion, Is.EqualTo(policy == NativeRuntimePolicy.FailFast ? "native-10" : "native-11"));
        NativeModuleCompiler.ValidateGraph(root, [middle, replacement]);
        Assert.That(await NativeModuleCompiler.RunGraphAsync(root, [original, middle],
            TimeSpan.FromSeconds(10), policy).ConfigureAwait(true), Is.EqualTo(22));
        Assert.That(await NativeModuleCompiler.RunGraphAsync(root, [middle, replacement],
            TimeSpan.FromSeconds(10), policy).ConfigureAwait(true), Is.EqualTo(25));
        Assert.That(await NativeModuleCompiler.RunGraphJitAsync(root, [replacement, middle],
            Apphost(), TimeSpan.FromSeconds(30), policy).ConfigureAwait(true), Is.EqualTo(25));
        Assert.That(SHA256.HashData(root), Is.EqualTo(hashes.Item1));
        Assert.That(SHA256.HashData(middle), Is.EqualTo(hashes.Item2));
    }
    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public async Task CurrentSemanticFormatClassLeafAndClassFreeGraphMembersRetainTheirShapesAsync(
        NativeRuntimePolicy policy)
    {
        var original = NativeModuleCompiler.CreateProducer(Compile("public int32 Leaf() { return 40; }", policy),
            ["Leaf"], "NodeB", policy);
        var middle = NativeModuleCompiler.CreateGraphProducer(Compile("""
            public int32 Middle() {
                while (true) { break; }
                return Leaf() + 1;
            }
            """, policy, NativeModuleCompiler.CreateGraphReference([original])),
            ["Middle"], "NodeA", [original], policy);
        var root = NativeModuleCompiler.CreateGraphConsumer(Compile("""
            int32 Main() {
                mut int32 count = 0;
                for (mut int32 i = 0; i < 3; i = i + 1) {
                    if (i == 0) { continue; }
                    count = count + 1;
                    break;
                }
                return Middle() + count;
            }
            """, policy, NativeModuleCompiler.CreateGraphReference([middle]), library: false),
            [middle], policy);
        var copyLeaf = (await File.ReadAllTextAsync(Path.Combine(RepositoryRoot(),
            "samples", "DependencyGraphServicing", "managed-copy-class-leaf.terrum")).ConfigureAwait(true))
            .Replace("return result;", "while (true) { break; } return result;", StringComparison.Ordinal);
        var replacement = NativeModuleCompiler.CreateProducer(Compile(copyLeaf, policy), ["Leaf"], "NodeB", policy);
        Assert.That(NativeModuleCompiler.Inspect(replacement).NativeProfileVersion, Is.EqualTo("native-20"));
        NativeModuleCompiler.ValidateGraph(root, [middle, replacement]);
        Assert.That(await NativeModuleCompiler.RunGraphAsync(root, [middle, original],
            TimeSpan.FromSeconds(10), policy).ConfigureAwait(true), Is.EqualTo(42));
        Assert.That(await NativeModuleCompiler.RunGraphAsync(root, [replacement, middle],
            TimeSpan.FromSeconds(10), policy).ConfigureAwait(true), Is.EqualTo(44));
        Assert.That(await NativeModuleCompiler.RunGraphJitAsync(root, [middle, replacement],
            Apphost(), TimeSpan.FromSeconds(30), policy).ConfigureAwait(true), Is.EqualTo(44));
    }

    [Test]
    public async Task CurrentSemanticFormatModuleSetKeepsBothLeafImportsIndependentAsync()
    {
        var alpha = NativeModuleCompiler.CreateProducer(Compile(Leaf.Replace("Leaf()", "Alpha()",
            StringComparison.Ordinal), NativeRuntimePolicy.FailFast), ["Alpha"], "First",
            NativeRuntimePolicy.FailFast);
        var beta = NativeModuleCompiler.CreateProducer(Compile(
            "public int32 Beta() { return 19; }", NativeRuntimePolicy.FailFast),
            ["Beta"], "Second", NativeRuntimePolicy.FailFast);
        var consumer = NativeModuleCompiler.CreateConsumerSet(Compile("""
            int32 Main() {
                while (true) { break; }
                return Alpha() + Beta();
            }
            """, NativeRuntimePolicy.FailFast, NativeModuleCompiler.CreateReferenceSet([alpha, beta]),
            library: false), [alpha, beta], NativeRuntimePolicy.FailFast);
        NativeModuleCompiler.ValidateConsumerSet(consumer, [beta, alpha]);
        Assert.That(await NativeModuleCompiler.RunConsumerSetAsync(consumer, [beta, alpha],
            TimeSpan.FromSeconds(10), NativeRuntimePolicy.FailFast).ConfigureAwait(true), Is.EqualTo(42));
    }

    [Test]
    public void CurrentSemanticFormatRejectsForgedTags()
    {
        var policy = NativeRuntimePolicy.FailFast;
        var oldProgram = Compile("public int32 Leaf() { return 42; }", policy);
        var old = ModuleCompiler.CreateProducer(oldProgram, ["Leaf"]);
        Assert.That(VersionOf(old), Is.EqualTo(76));
        var classProgram = Compile("""
            class Box {
                public int32 Value;
                public Box() { Value = 42; }
            }
            public int32 Leaf() { require Box.TryCreate(out var box); return box.Value; }
            """, policy);
        var classOnly = ModuleCompiler.CreateProducer(classProgram, ["Leaf"]);
        Assert.That(VersionOf(classOnly), Is.EqualTo(76));
        _ = ModuleCompiler.CreateReference(classOnly);
        var copyProgram = Compile(File.ReadAllText(Path.Combine(RepositoryRoot(),
            "samples", "DependencyGraphServicing", "managed-copy-class-leaf.terrum")), policy);
        var copy = ModuleCompiler.CreateProducer(copyProgram, ["Leaf"]);
        Assert.That(VersionOf(copy), Is.EqualTo(76));
        _ = ModuleCompiler.CreateReference(copy);
        foreach (var (legacy, program) in new[] {
            (old, oldProgram), (classOnly, classProgram), (copy, copyProgram),
        })
        {
            var invalid = (byte[])legacy.Clone();
            var bodyTag = FindReturnTag(invalid, program);
            invalid[bodyTag] = 18;
            _ = Assert.Throws<ModuleFormatException>(() => ModuleCompiler.CreateReference(invalid))!;
        }
        var transferred = ModuleCompiler.CreateProducer(Compile(ClassLeaf, policy), ["Leaf"]);
        Assert.That(VersionOf(transferred), Is.EqualTo(76));
        var downgraded = (byte[])transferred.Clone();
        BinaryPrimitives.WriteInt32LittleEndian(downgraded.AsSpan(4, 4), 19);
        _ = Assert.Throws<ModuleFormatException>(() => ModuleCompiler.CreateReference(downgraded))!;
    }

    private static int FindReturnTag(byte[] bytes, BoundProgram program)
    {
        var function = global::System.Linq.Enumerable.Single(program.Functions, static item => item.Name == "Leaf");
        var statement = global::System.Linq.Enumerable.Single(function.Body.Statements.OfType<BoundReturn>());
        var marker = new byte[9];
        marker[0] = 8;
        BinaryPrimitives.WriteInt32LittleEndian(marker.AsSpan(1), statement.Span.Start);
        BinaryPrimitives.WriteInt32LittleEndian(marker.AsSpan(5), statement.Span.Length);
        var index = bytes.AsSpan().IndexOf(marker);
        Assert.That(index >= 0, Is.True);
        Assert.That(bytes.AsSpan(index + 1).IndexOf(marker), Is.EqualTo(-1));
        return index;
    }

    private static int VersionOf(byte[] module) => BinaryPrimitives.ReadInt32LittleEndian(module.AsSpan(4, 4));

    private static BoundProgram Compile(string source, NativeRuntimePolicy policy,
        CompilationReference? reference = null, bool library = true)
    {
        var syntax = SyntaxTree.Parse(source);
        var compilation = library
            ? reference is null ? Compilation.CreateLibrary(syntax, (BoundRuntimePolicy)policy)
                : Compilation.CreateLibrary(syntax, reference, (BoundRuntimePolicy)policy)
            : Compilation.Create(syntax, ((global::System.Func<CompilationReference>)(() => { var __nunitActual0 = reference; Assert.That(__nunitActual0, Is.TypeOf<CompilationReference>()); return (CompilationReference)__nunitActual0!; }))(), (BoundRuntimePolicy)policy);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        return ((global::System.Func<BoundProgram>)(() => { var __nunitActual1 = compilation.Program; Assert.That(__nunitActual1, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual1!; }))();
    }

    private static string Apphost()
    {
        var frameworkDirectory = new DirectoryInfo(AppContext.BaseDirectory);
        var configuration = frameworkDirectory.Parent?.Name
            ?? throw new InvalidOperationException("Missing test configuration directory.");
        return Path.Combine(RepositoryRoot(), "artifacts", "bin", "sources", "terrumc",
            configuration, frameworkDirectory.Name, "terrumc.exe");
    }

    private static string RepositoryRoot() => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
        "..", "..", "..", "..", "..", ".."));
}
