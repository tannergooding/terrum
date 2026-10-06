// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed class NativeGraphTests
{
    private static string CompilerApphost()
    {
        var framework = new DirectoryInfo(AppContext.BaseDirectory);
        var configuration = framework.Parent?.Name
            ?? throw new InvalidOperationException("Missing test configuration directory.");
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
        return Path.Combine(root, "artifacts", "bin", "sources", "terrumc",
            configuration, framework.Name, "terrumc.exe");
    }

    private static byte[] Leaf(string source, NativeRuntimePolicy policy = NativeRuntimePolicy.FailFast)
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source), (BoundRuntimePolicy)policy);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        return NativeModuleCompiler.CreateProducer(Assert.IsType<BoundProgram>(compilation.Program),
            ["Leaf"], "NodeB", policy);
    }

    private static byte[] Middle(string source, byte[] leaf,
        NativeRuntimePolicy policy = NativeRuntimePolicy.FailFast)
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source),
            NativeModuleCompiler.CreateGraphReference([leaf]), (BoundRuntimePolicy)policy);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        return NativeModuleCompiler.CreateGraphProducer(Assert.IsType<BoundProgram>(compilation.Program),
            ["Middle"], "NodeA", [leaf], policy);
    }

    private static byte[] Consumer(byte[] middle, NativeRuntimePolicy policy = NativeRuntimePolicy.FailFast)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("int32 Main() { return Middle() + 1; }"),
            NativeModuleCompiler.CreateGraphReference([middle]), (BoundRuntimePolicy)policy);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        return NativeModuleCompiler.CreateGraphConsumer(Assert.IsType<BoundProgram>(compilation.Program),
            [middle], policy);
    }

    private static byte[] WeakenRecordedImport(byte[] artifact, string name, string oldSuffix, string newSuffix)
    {
        var anchor = Encoding.ASCII.GetBytes(name + "|");
        var at = artifact.AsSpan().IndexOf(anchor);
        Assert.True(at >= 4);
        Assert.Equal(-1, artifact.AsSpan(at + 1).IndexOf(anchor));
        var length = BinaryPrimitives.ReadInt32LittleEndian(artifact.AsSpan(at - 4));
        var saved = Encoding.ASCII.GetString(artifact, at, length);
        Assert.EndsWith(oldSuffix, saved, StringComparison.Ordinal);
        var suffixStart = at + length - oldSuffix.Length;
        var difference = newSuffix.Length - oldSuffix.Length;
        var result = new byte[artifact.Length + difference];
        artifact.AsSpan(0, suffixStart).CopyTo(result);
        Encoding.ASCII.GetBytes(newSuffix).CopyTo(result, suffixStart);
        artifact.AsSpan(at + length).CopyTo(result.AsSpan(at + length + difference));
        BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(at - 4), checked(length + difference));
        BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(11),
            checked(BinaryPrimitives.ReadInt32LittleEndian(artifact.AsSpan(11)) + difference));
        SHA256.HashData(result.AsSpan(0, result.Length - 32)).CopyTo(result, result.Length - 32);
        return result;
    }

    private static byte[] ReplaceRecordedGraphImport(byte[] artifact, string dependencyIdentity,
        string oldName, string newName)
    {
        Assert.Equal(oldName.Length, newName.Length);
        var result = (byte[])artifact.Clone();
        var at = 19 + result[7];
        var replaced = false;
        for (var i = 0; i < result[8]; i++)
        {
            var identityLength = BinaryPrimitives.ReadUInt16LittleEndian(result.AsSpan(at));
            at += 2;
            var identity = Encoding.UTF8.GetString(result, at, identityLength);
            at += identityLength;
            var declarationCount = BinaryPrimitives.ReadUInt16LittleEndian(result.AsSpan(at));
            at += 2;
            for (var j = 0; j < declarationCount; j++)
            {
                var length = BinaryPrimitives.ReadUInt16LittleEndian(result.AsSpan(at));
                at += 2 + length;
            }
            var importCount = BinaryPrimitives.ReadUInt16LittleEndian(result.AsSpan(at));
            at += 2;
            for (var j = 0; j < importCount; j++)
            {
                var length = BinaryPrimitives.ReadUInt16LittleEndian(result.AsSpan(at));
                at += 2;
                if (identity == dependencyIdentity
                    && Encoding.UTF8.GetString(result, at, length) == oldName)
                {
                    Assert.False(replaced);
                    Encoding.UTF8.GetBytes(newName).CopyTo(result, at);
                    replaced = true;
                }
                at += length;
            }
        }
        Assert.True(replaced);
        SHA256.HashData(result.AsSpan(0, result.Length - 32)).CopyTo(result, result.Length - 32);
        return result;
    }

    [Fact]
    public void GraphPackagesUseCurrentVersionAndRejectPreviousVersion()
    {
        var leaf = Leaf("[NoThrow] public int32 Leaf() { return 40; }");
        var middle = Middle("public int32 Middle() { return Leaf() + 1; }", leaf);
        var root = Consumer(middle);
        Assert.Equal(2, middle[4]);
        Assert.Equal(2, root[4]);

        foreach (var artifact in new[] { middle, root })
        {
            var previousVersion = (byte[])artifact.Clone();
            previousVersion[4] = 1;
            SHA256.HashData(previousVersion.AsSpan(0, previousVersion.Length - 32))
                .CopyTo(previousVersion, previousVersion.Length - 32);
            _ = Assert.Throws<ModuleFormatException>(() => NativeModuleCompiler.InspectGraphArtifact(previousVersion));
        }
    }

    [Fact]
    public void InspectingLeafGraphArtifactPreservesNativeProfile()
    {
        var leaf = Leaf("[NoThrow] public int32 Leaf() { return 40; }");

        Assert.Equal(NativeModuleCompiler.Inspect(leaf).NativeProfileVersion,
            NativeModuleCompiler.InspectGraphArtifact(leaf).NativeProfileVersion);
    }

    [Fact]
    public void GraphProvidersAreOrderedByIdentity()
    {
        static byte[] CreateStructuredProvider(string structure, string identity)
        {
            var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(
                $"public struct {structure} {{ public int32 Value; }} "
                + $"public int32 Read{structure}({structure} value) {{ return value.Value; }}"));
            Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
            return NativeModuleCompiler.CreateProducer(Assert.IsType<BoundProgram>(compilation.Program),
                [$"Read{structure}"], identity, NativeRuntimePolicy.FailFast);
        }

        var reference = NativeModuleCompiler.CreateGraphReference([
            CreateStructuredProvider("Zulu", "NodeZ"),
            CreateStructuredProvider("Alpha", "NodeA"),
        ]);

        Assert.Equal(["Alpha", "Zulu"], reference.Structures.Select(static item => item.Name));
    }

    [Fact]
    public void ProducerValidationAcceptsUnsortedExportNames()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            public int32 Zebra() { return 2; }
            public int32 Alpha() { return 1; }
            """));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var producer = NativeModuleCompiler.CreateProducer(Assert.IsType<BoundProgram>(compilation.Program),
            ["Zebra", "Alpha"], "UnsortedExports", NativeRuntimePolicy.FailFast);

        Assert.False(NativeModuleCompiler.Inspect(producer).IsConsumer);
    }

    [Fact]
    public void ConsumerValidationAcceptsMultipleImports()
    {
        var providerCompilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            public int32 Zebra() { return 2; }
            public int32 Alpha() { return 1; }
            """));
        Assert.False(providerCompilation.HasErrors,
            string.Join(Environment.NewLine, providerCompilation.Diagnostics));
        var provider = NativeModuleCompiler.CreateProducer(
            Assert.IsType<BoundProgram>(providerCompilation.Program), ["Zebra", "Alpha"],
            "MultipleImports", NativeRuntimePolicy.FailFast);
        var consumerCompilation = Compilation.Create(SyntaxTree.Parse("""
            int32 Main() { return Zebra() + Alpha(); }
            """), NativeModuleCompiler.CreateReference(provider));
        Assert.False(consumerCompilation.HasErrors,
            string.Join(Environment.NewLine, consumerCompilation.Diagnostics));

        var consumer = NativeModuleCompiler.CreateConsumer(
            Assert.IsType<BoundProgram>(consumerCompilation.Program), provider, NativeRuntimePolicy.FailFast);

        Assert.NotEmpty(consumer);
    }

    [Fact]
    public void ConsumerValidationAcceptsNoImports()
    {
        var provider = Leaf("public int32 Leaf() { return 1; }");
        var compilation = Compilation.Create(SyntaxTree.Parse("int32 Main() { return 42; }"),
            NativeModuleCompiler.CreateReference(provider));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));

        var consumer = NativeModuleCompiler.CreateConsumer(
            Assert.IsType<BoundProgram>(compilation.Program), provider, NativeRuntimePolicy.FailFast);

        Assert.NotEmpty(consumer);
    }

    [Fact]
    public void GraphProviderValidationAcceptsNoImportsWithDependencies()
    {
        var dependencyCompilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            public struct Node { public int32 Value; }
            public Node GetNode(Node value) { return value; }
            """));
        Assert.False(dependencyCompilation.HasErrors,
            string.Join(Environment.NewLine, dependencyCompilation.Diagnostics));
        var dependency = NativeModuleCompiler.CreateProducer(
            Assert.IsType<BoundProgram>(dependencyCompilation.Program), ["GetNode"], "NodeB",
            NativeRuntimePolicy.FailFast);
        var dependencyReference = NativeModuleCompiler.CreateGraphReference([dependency]);
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(
            "public Node Identity(Node value) { return value; }"), dependencyReference);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var provider = NativeModuleCompiler.CreateGraphProducer(
            Assert.IsType<BoundProgram>(compilation.Program), ["Identity"], "NodeA", [dependency],
            NativeRuntimePolicy.FailFast);

        Assert.NotEmpty(provider);
    }

    [Fact]
    public void GraphProvidersRejectCaseOnlyIdentityDuplicates()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(
            "[NoThrow] public int32 Leaf() { return 40; }"));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = Assert.IsType<BoundProgram>(compilation.Program);
        var provider = NativeModuleCompiler.CreateProducer(program, ["Leaf"], "NodeB",
            NativeRuntimePolicy.FailFast);
        var caseVariant = NativeModuleCompiler.CreateProducer(program, ["Leaf"], "nodeb",
            NativeRuntimePolicy.FailFast);
        var consumerCompilation = Compilation.Create(SyntaxTree.Parse(
            "int32 Main() { return Leaf() + 1; }"), NativeModuleCompiler.CreateGraphReference([provider]));
        Assert.False(consumerCompilation.HasErrors,
            string.Join(Environment.NewLine, consumerCompilation.Diagnostics));
        var consumer = NativeModuleCompiler.CreateGraphConsumer(
            Assert.IsType<BoundProgram>(consumerCompilation.Program), [provider], NativeRuntimePolicy.FailFast);

        var error = Assert.Throws<ModuleContractException>(() =>
            NativeModuleCompiler.ValidateGraph(consumer, [provider, caseVariant]));

        Assert.Equal("Duplicate, ambiguous or consumer graph dependency.", error.Message);
    }

    [Theory]
    [InlineData(NativeRuntimePolicy.FailFast)]
    [InlineData(NativeRuntimePolicy.CheckedExceptions)]
    public async Task UnchangedRootAndMiddleExecuteCompatibleLeafReplacementAsync(NativeRuntimePolicy policy)
    {
        var original = Leaf("[NoThrow] public int32 Leaf() { return 40; }", policy);
        var middle = Middle("public int32 Middle() { return Leaf() + 1; }", original, policy);
        var root = Consumer(middle, policy);
        var originalRoot = root.ToArray();
        var originalMiddle = middle.ToArray();
        NativeModuleCompiler.ValidateGraph(root, [original, middle]);
        Assert.Equal(42, await NativeModuleCompiler.RunGraphAsync(root, [middle, original],
            TimeSpan.FromSeconds(10), policy));
        var replacement = Leaf("[NoThrow] public int32 Leaf() { return 41; }", policy);
        NativeModuleCompiler.ValidateGraph(root, [replacement, middle]);
        Assert.Equal(43, await NativeModuleCompiler.RunGraphAsync(root, [replacement, middle],
            TimeSpan.FromSeconds(10), policy));
        Assert.Equal(originalRoot, root);
        Assert.Equal(originalMiddle, middle);
        Assert.Equal(["NodeA"], NativeModuleCompiler.InspectGraphArtifact(root).Dependencies);
        Assert.Equal(["NodeB"], NativeModuleCompiler.InspectGraphArtifact(middle).Dependencies);
    }

    [Fact]
    public void RejectsMissingExtraIncompatiblePolicyAndCyclicGraphs()
    {
        var leaf = Leaf("[NoThrow] public int32 Leaf() { return 40; }");
        var middle = Middle("public int32 Middle() { return Leaf() + 1; }", leaf);
        var root = Consumer(middle);
        _ = Assert.Throws<ModuleContractException>(() => NativeModuleCompiler.ValidateGraph(root, [middle]));
        _ = Assert.Throws<ModuleContractException>(() => NativeModuleCompiler.ValidateGraph(root, [leaf, middle, leaf]));
        _ = Assert.Throws<ModuleContractException>(() => NativeModuleCompiler.ValidateGraph(root, [
            leaf, middle, NativeModuleCompiler.CreateProducer(
                Assert.IsType<BoundProgram>(Compilation.CreateLibrary(
                    SyntaxTree.Parse("public int32 Extra() { return 1; }")).Program),
                ["Extra"], "Extra", NativeRuntimePolicy.FailFast)]));
        _ = Assert.Throws<ModuleContractException>(() => NativeModuleCompiler.ValidateGraph(root, [
            Leaf("public int32 Leaf() { return 40; }"), middle]));
        _ = Assert.Throws<ModuleContractException>(() => NativeModuleCompiler.ValidateGraph(root, [
            Leaf("[NoThrow] public int32 Leaf() { return 40; }", NativeRuntimePolicy.CheckedExceptions), middle]));
        var damaged = middle.ToArray();
        damaged[^1] ^= 1;
        _ = Assert.Throws<ModuleFormatException>(() => NativeModuleCompiler.ValidateGraph(root, [leaf, damaged]));
        var tamperedImport = middle.ToArray();
        "NodeC"u8.CopyTo(tamperedImport.AsSpan(19 + "NodeA".Length + 2));
        SHA256.HashData(tamperedImport.AsSpan(0, tamperedImport.Length - 32))
            .CopyTo(tamperedImport, tamperedImport.Length - 32);
        _ = Assert.Throws<ModuleFormatException>(() => NativeModuleCompiler.ValidateGraph(root,
            [leaf, tamperedImport]));
        var badCount = middle.ToArray();
        badCount[8] = 0;
        SHA256.HashData(badCount.AsSpan(0, badCount.Length - 32))
            .CopyTo(badCount, badCount.Length - 32);
        _ = Assert.Throws<ModuleFormatException>(() => NativeModuleCompiler.ValidateGraph(root, [leaf, badCount]));

        var cycle = middle.ToArray();
        "NodeB"u8.CopyTo(cycle.AsSpan(19));
        SHA256.HashData(cycle.AsSpan(0, cycle.Length - 32)).CopyTo(cycle, cycle.Length - 32);
        var error = Assert.Throws<ModuleContractException>(() => NativeModuleCompiler.ValidateGraph(root,
            [middle, cycle]));
        Assert.Contains("cycle", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task OwnedTransferAcrossIntermediatePreservesReplacementContractsAsync()
    {
        const string LeafSource = """
            public unique struct Token {
                public int32 Value;
                public Token(int32 value) { Value = value; }
                [NoThrow] [Cleanup] public void Close() { }
            }
            public Token New() { return new Token(20); }
            [NoThrow] public int32 Take(Token value) { return value.Value; }
            """;
        static byte[] CompileLeaf(string source)
        {
            var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source));
            Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
            return NativeModuleCompiler.CreateProducer(Assert.IsType<BoundProgram>(compilation.Program),
                ["New", "Take", "Token.Close"], "NodeB", NativeRuntimePolicy.FailFast);
        }
        var leaf = CompileLeaf(LeafSource);
        var middleCompilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            public int32 Middle() {
                Token token = New();
                return Take(move token) + 22;
            }
            """), NativeModuleCompiler.CreateGraphReference([leaf]));
        Assert.False(middleCompilation.HasErrors, string.Join(Environment.NewLine, middleCompilation.Diagnostics));
        var middle = NativeModuleCompiler.CreateGraphProducer(
            Assert.IsType<BoundProgram>(middleCompilation.Program), ["Middle"], "NodeA",
            [leaf], NativeRuntimePolicy.FailFast);
        var rootCompilation = Compilation.Create(SyntaxTree.Parse("int32 Main() { return Middle(); }"),
            NativeModuleCompiler.CreateGraphReference([middle]));
        Assert.False(rootCompilation.HasErrors, string.Join(Environment.NewLine, rootCompilation.Diagnostics));
        var root = NativeModuleCompiler.CreateGraphConsumer(Assert.IsType<BoundProgram>(rootCompilation.Program),
            [middle], NativeRuntimePolicy.FailFast);
        Assert.Equal("native-13", NativeModuleCompiler.InspectGraphArtifact(middle).NativeProfileVersion);
        var replacement = CompileLeaf(LeafSource.Replace("new Token(20)", "new Token(21)", StringComparison.Ordinal));
        Assert.Equal(42, await NativeModuleCompiler.RunGraphAsync(root, [middle, leaf],
            TimeSpan.FromSeconds(10), NativeRuntimePolicy.FailFast));
        Assert.Equal(43, await NativeModuleCompiler.RunGraphAsync(root, [replacement, middle],
            TimeSpan.FromSeconds(10), NativeRuntimePolicy.FailFast));
        _ = Assert.Throws<ModuleContractException>(() => NativeModuleCompiler.ValidateGraph(root, [
            CompileLeaf(LeafSource.Replace("[NoThrow] public int32 Take", "public int32 Take",
                StringComparison.Ordinal)), middle]));
        _ = Assert.Throws<ModuleContractException>(() => NativeModuleCompiler.ValidateGraph(root, [
            CompileLeaf(LeafSource.Replace("public int32 Value;", "public int32 Other;", StringComparison.Ordinal)
                .Replace("Value = value;", "Other = value;", StringComparison.Ordinal)
                .Replace("value.Value", "value.Other", StringComparison.Ordinal)), middle]));
    }

    [Theory]
    [InlineData(NativeRuntimePolicy.FailFast, false)]
    [InlineData(NativeRuntimePolicy.FailFast, true)]
    [InlineData(NativeRuntimePolicy.CheckedExceptions, false)]
    [InlineData(NativeRuntimePolicy.CheckedExceptions, true)]
    public async Task ScalarOutputExecutesAcrossGraphAndRejectsModeReplacementAsync(
        NativeRuntimePolicy policy, bool initialUsesInit)
    {
        var initialSource = initialUsesInit
            ? "public void Leaf(init ref<int32> result) { result = 42; }"
            : "public void Leaf(out int32 result) { result = 42; }";
        var replacementSource = initialUsesInit
            ? "public void Leaf(out int32 result) { result = 43; }"
            : "public void Leaf(init ref<int32> result) { result = 43; }";
        var consumerSource = initialUsesInit
            ? "int32 Main() { init int32 result; Leaf(init result); return result; }"
            : "int32 Main() { Leaf(out var result); return result; }";

        var leaf = Leaf(initialSource, policy);
        var rootCompilation = Compilation.Create(SyntaxTree.Parse(consumerSource),
            NativeModuleCompiler.CreateGraphReference([leaf]), (BoundRuntimePolicy)policy);
        Assert.False(rootCompilation.HasErrors, string.Join(Environment.NewLine, rootCompilation.Diagnostics));
        var root = NativeModuleCompiler.CreateGraphConsumer(Assert.IsType<BoundProgram>(rootCompilation.Program),
            [leaf], policy);

        NativeModuleCompiler.ValidateGraph(root, [leaf]);
        Assert.Equal(42, await NativeModuleCompiler.RunGraphAsync(root, [leaf],
            TimeSpan.FromSeconds(10), policy).ConfigureAwait(true));
        Assert.Equal(42, await NativeModuleCompiler.RunGraphJitAsync(root, [leaf],
            CompilerApphost(), TimeSpan.FromSeconds(30), policy).ConfigureAwait(true));

        var replacement = Leaf(replacementSource, policy);
        _ = Assert.Throws<ModuleContractException>(() =>
            NativeModuleCompiler.ValidateGraph(root, [replacement]));
    }

    [Theory]
    [InlineData(NativeRuntimePolicy.FailFast, false)]
    [InlineData(NativeRuntimePolicy.FailFast, true)]
    [InlineData(NativeRuntimePolicy.CheckedExceptions, false)]
    [InlineData(NativeRuntimePolicy.CheckedExceptions, true)]
    public async Task CopyStructOutputExecutesAcrossGraphAsync(
        NativeRuntimePolicy policy, bool usesInit)
    {
        var outputMode = usesInit ? "init ref<Pair>" : "out Pair";
        const string ProviderTemplate = """
            public struct Pair {
                public int64 First;
                public int64 Second;
                public Pair(int64 first, int64 second) { First = first; Second = second; }
            }
            """;
        var providerSource = $"{ProviderTemplate}public void Leaf({outputMode} result) "
            + "{ result = new Pair(20, 22); }";
        var consumerSource = usesInit
            ? """
                int32 Main() {
                    init Pair result;
                    Leaf(init result);
                    return int32(result.First + result.Second);
                }
                """
            : """
            int32 Main() {
                Leaf(out var result);
                return int32(result.First + result.Second);
            }
            """;

        var leaf = Leaf(providerSource, policy);
        var rootCompilation = Compilation.Create(SyntaxTree.Parse(consumerSource),
            NativeModuleCompiler.CreateGraphReference([leaf]), (BoundRuntimePolicy)policy);
        Assert.False(rootCompilation.HasErrors, string.Join(Environment.NewLine, rootCompilation.Diagnostics));
        var root = NativeModuleCompiler.CreateGraphConsumer(Assert.IsType<BoundProgram>(rootCompilation.Program),
            [leaf], policy);

        NativeModuleCompiler.ValidateGraph(root, [leaf]);
        Assert.Equal(42, await NativeModuleCompiler.RunGraphAsync(root, [leaf],
            TimeSpan.FromSeconds(10), policy).ConfigureAwait(true));
        Assert.Equal(42, await NativeModuleCompiler.RunGraphJitAsync(root, [leaf],
            CompilerApphost(), TimeSpan.FromSeconds(30), policy).ConfigureAwait(true));
    }

    [Fact]
    public async Task CheckedGraphFailureLeavesInitRefOutputVacantForRetryAsync()
    {
        const NativeRuntimePolicy policy = NativeRuntimePolicy.CheckedExceptions;
        const string ProviderSource = """
            public void Fail(init ref<int32> result) { result = 42; require false; }
            public void Complete(init ref<int32> result) { result = 43; }
            """;
        const string ConsumerSource = """
            int32 Main() {
                init int32 result;
                unsafe {
                    try {
                        Fail(init result);
                        return 1;
                    } catch { }
                }
                Complete(init result);
                return result;
            }
            """;

        var providerCompilation = Compilation.CreateLibrary(SyntaxTree.Parse(ProviderSource),
            (BoundRuntimePolicy)policy);
        Assert.False(providerCompilation.HasErrors,
            string.Join(Environment.NewLine, providerCompilation.Diagnostics));
        var provider = NativeModuleCompiler.CreateProducer(
            Assert.IsType<BoundProgram>(providerCompilation.Program), ["Fail", "Complete"], "NodeB", policy);
        var rootCompilation = Compilation.Create(SyntaxTree.Parse(ConsumerSource),
            NativeModuleCompiler.CreateGraphReference([provider]), (BoundRuntimePolicy)policy);
        Assert.False(rootCompilation.HasErrors, string.Join(Environment.NewLine, rootCompilation.Diagnostics));
        var root = NativeModuleCompiler.CreateGraphConsumer(Assert.IsType<BoundProgram>(rootCompilation.Program),
            [provider], policy);

        NativeModuleCompiler.ValidateGraph(root, [provider]);
        Assert.Equal(43, await NativeModuleCompiler.RunGraphAsync(root, [provider],
            TimeSpan.FromSeconds(10), policy).ConfigureAwait(true));
        Assert.Equal(43, await NativeModuleCompiler.RunGraphJitAsync(root, [provider],
            CompilerApphost(), TimeSpan.FromSeconds(30), policy).ConfigureAwait(true));
    }

    [Theory]
    [InlineData(NativeRuntimePolicy.FailFast, false)]
    [InlineData(NativeRuntimePolicy.FailFast, true)]
    [InlineData(NativeRuntimePolicy.CheckedExceptions, false)]
    [InlineData(NativeRuntimePolicy.CheckedExceptions, true)]
    public async Task ConditionalInitRefOutputPublishesAcrossGraphOnlyOnTrueAsync(
        NativeRuntimePolicy policy, bool usesCopyStruct)
    {
        var providerSource = usesCopyStruct
            ? """
                public struct Pair {
                    public int64 First;
                    public int64 Second;
                    public Pair(int64 first, int64 second) { First = first; Second = second; }
                }
                public bool TryPublish(bool publish, init ref<Pair> result) {
                    result = publish ? new Pair(20, 22) : new Pair(20, 21);
                    return publish;
                }
                """
            : """
            public bool TryPublish(bool publish, init ref<int32> result) {
                result = publish ? 42 : 41;
                return publish;
            }
            """;
        var consumerSource = usesCopyStruct
            ? """
                int32 Main() {
                    init Pair result;
                    if (TryPublish(false, init result)) { return 1; }
                    if (!TryPublish(true, init result)) { return 2; }
                    return int32(result.First + result.Second);
                }
                """
            : """
            int32 Main() {
                init int32 result;
                if (TryPublish(false, init result)) { return 1; }
                if (!TryPublish(true, init result)) { return 2; }
                return result;
            }
            """;

        var providerCompilation = Compilation.CreateLibrary(SyntaxTree.Parse(providerSource),
            (BoundRuntimePolicy)policy);
        Assert.False(providerCompilation.HasErrors,
            string.Join(Environment.NewLine, providerCompilation.Diagnostics));
        var provider = NativeModuleCompiler.CreateProducer(
            Assert.IsType<BoundProgram>(providerCompilation.Program), ["TryPublish"], "NodeB", policy);
        var rootCompilation = Compilation.Create(SyntaxTree.Parse(consumerSource),
            NativeModuleCompiler.CreateGraphReference([provider]), (BoundRuntimePolicy)policy);
        Assert.False(rootCompilation.HasErrors, string.Join(Environment.NewLine, rootCompilation.Diagnostics));
        var root = NativeModuleCompiler.CreateGraphConsumer(Assert.IsType<BoundProgram>(rootCompilation.Program),
            [provider], policy);

        NativeModuleCompiler.ValidateGraph(root, [provider]);
        Assert.Equal(42, await NativeModuleCompiler.RunGraphAsync(root, [provider],
            TimeSpan.FromSeconds(10), policy).ConfigureAwait(true));
        Assert.Equal(42, await NativeModuleCompiler.RunGraphJitAsync(root, [provider],
            CompilerApphost(), TimeSpan.FromSeconds(30), policy).ConfigureAwait(true));
    }

    [Theory]
    [InlineData(NativeRuntimePolicy.FailFast, false)]
    [InlineData(NativeRuntimePolicy.FailFast, true)]
    [InlineData(NativeRuntimePolicy.CheckedExceptions, false)]
    [InlineData(NativeRuntimePolicy.CheckedExceptions, true)]
    public async Task ConditionalOutOutputRemainsVacantOnFalseAcrossGraphAsync(
        NativeRuntimePolicy policy, bool usesCopyStruct)
    {
        var providerSource = usesCopyStruct
            ? """
                public struct Pair {
                    public int64 First;
                    public int64 Second;
                    public Pair(int64 first, int64 second) { First = first; Second = second; }
                }
                public bool TryPublish(bool publish, out Pair result) {
                    result = publish ? new Pair(20, 22) : new Pair(20, 21);
                    return publish;
                }
                public void Complete(init ref<Pair> result) { result = new Pair(30, 13); }
                """
            : """
            public bool TryPublish(bool publish, out int32 result) {
                result = publish ? 42 : 41;
                return publish;
            }
            public void Complete(init ref<int32> result) { result = 43; }
            """;
        var consumerSource = usesCopyStruct
            ? """
                int32 Main() {
                    if (TryPublish(false, out var failed)) { return 1; }
                    Complete(init failed);
                    if (failed.First != 30 || failed.Second != 13) { return 2; }
                    if (!TryPublish(true, out var published)) { return 3; }
                    return int32(published.First + published.Second);
                }
                """
            : """
            int32 Main() {
                if (TryPublish(false, out var failed)) { return 1; }
                Complete(init failed);
                if (failed != 43) { return 2; }
                if (!TryPublish(true, out var published)) { return 3; }
                return published;
            }
            """;

        var providerCompilation = Compilation.CreateLibrary(SyntaxTree.Parse(providerSource),
            (BoundRuntimePolicy)policy);
        Assert.False(providerCompilation.HasErrors,
            string.Join(Environment.NewLine, providerCompilation.Diagnostics));
        var provider = NativeModuleCompiler.CreateProducer(
            Assert.IsType<BoundProgram>(providerCompilation.Program), ["TryPublish", "Complete"],
            "NodeB", policy);
        var rootCompilation = Compilation.Create(SyntaxTree.Parse(consumerSource),
            NativeModuleCompiler.CreateGraphReference([provider]), (BoundRuntimePolicy)policy);
        Assert.False(rootCompilation.HasErrors, string.Join(Environment.NewLine, rootCompilation.Diagnostics));
        var root = NativeModuleCompiler.CreateGraphConsumer(Assert.IsType<BoundProgram>(rootCompilation.Program),
            [provider], policy);

        NativeModuleCompiler.ValidateGraph(root, [provider]);
        Assert.Equal(42, await NativeModuleCompiler.RunGraphAsync(root, [provider],
            TimeSpan.FromSeconds(10), policy).ConfigureAwait(true));
        Assert.Equal(42, await NativeModuleCompiler.RunGraphJitAsync(root, [provider],
            CompilerApphost(), TimeSpan.FromSeconds(30), policy).ConfigureAwait(true));
    }

    [Fact]
    public async Task SharedLeafDiamondIsAcyclicAndBindsEveryImportToItsOwnerAsync()
    {
        var leaf = Leaf("public int32 Leaf() { return 40; }");
        var middle = Middle("public int32 Middle() { return Leaf() + 1; }", leaf);
        var topCompilation = Compilation.CreateLibrary(SyntaxTree.Parse(
            "public int32 Top() { return Middle() + Leaf(); }"),
            NativeModuleCompiler.CreateGraphReference([middle, leaf]));
        Assert.False(topCompilation.HasErrors, string.Join(Environment.NewLine, topCompilation.Diagnostics));
        var top = NativeModuleCompiler.CreateGraphProducer(Assert.IsType<BoundProgram>(topCompilation.Program),
            ["Top"], "NodeC", [middle, leaf], NativeRuntimePolicy.FailFast);
        var rootCompilation = Compilation.Create(SyntaxTree.Parse("int32 Main() { return Top(); }"),
            NativeModuleCompiler.CreateGraphReference([top]));
        Assert.False(rootCompilation.HasErrors, string.Join(Environment.NewLine, rootCompilation.Diagnostics));
        var root = NativeModuleCompiler.CreateGraphConsumer(Assert.IsType<BoundProgram>(rootCompilation.Program),
            [top], NativeRuntimePolicy.FailFast);
        NativeModuleCompiler.ValidateGraph(root, [top, leaf, middle]);
        Assert.Equal(81, await NativeModuleCompiler.RunGraphAsync(root, [middle, leaf, top],
            TimeSpan.FromSeconds(10), NativeRuntimePolicy.FailFast));
        var replacement = Leaf("public int32 Leaf() { return 41; }");
        Assert.Equal(83, await NativeModuleCompiler.RunGraphAsync(root, [replacement, top, middle],
            TimeSpan.FromSeconds(10), NativeRuntimePolicy.FailFast));
    }

    [Fact]
    public void GraphImportsMustBeDeclaredByTheirOwnDependency()
    {
        var leafCompilation = Compilation.CreateLibrary(SyntaxTree.Parse(
            "public int32 Bravo() { return 40; }"));
        Assert.False(leafCompilation.HasErrors, string.Join(Environment.NewLine, leafCompilation.Diagnostics));
        var leaf = NativeModuleCompiler.CreateProducer(Assert.IsType<BoundProgram>(leafCompilation.Program),
            ["Bravo"], "NodeB", NativeRuntimePolicy.FailFast);
        var middleCompilation = Compilation.CreateLibrary(SyntaxTree.Parse(
            "public int32 Alpha() { return Bravo(); }"), NativeModuleCompiler.CreateGraphReference([leaf]));
        Assert.False(middleCompilation.HasErrors, string.Join(Environment.NewLine, middleCompilation.Diagnostics));
        var middle = NativeModuleCompiler.CreateGraphProducer(Assert.IsType<BoundProgram>(middleCompilation.Program),
            ["Alpha"], "NodeA", [leaf], NativeRuntimePolicy.FailFast);
        var topCompilation = Compilation.CreateLibrary(SyntaxTree.Parse(
            "public int32 Top() { return Alpha() + Bravo(); }"),
            NativeModuleCompiler.CreateGraphReference([leaf, middle]));
        Assert.False(topCompilation.HasErrors, string.Join(Environment.NewLine, topCompilation.Diagnostics));
        var top = NativeModuleCompiler.CreateGraphProducer(Assert.IsType<BoundProgram>(topCompilation.Program),
            ["Top"], "NodeC", [middle, leaf], NativeRuntimePolicy.FailFast);
        var rootCompilation = Compilation.Create(SyntaxTree.Parse("int32 Main() { return Top(); }"),
            NativeModuleCompiler.CreateGraphReference([top]));
        Assert.False(rootCompilation.HasErrors, string.Join(Environment.NewLine, rootCompilation.Diagnostics));
        var root = NativeModuleCompiler.CreateGraphConsumer(Assert.IsType<BoundProgram>(rootCompilation.Program),
            [top], NativeRuntimePolicy.FailFast);

        NativeModuleCompiler.ValidateGraph(root, [top, middle, leaf]);
        var crossDependencyImport = ReplaceRecordedGraphImport(top, "NodeA", "Alpha", "Bravo");
        _ = Assert.Throws<ModuleFormatException>(() =>
            NativeModuleCompiler.ValidateGraph(root, [crossDependencyImport, middle, leaf]));
    }

    [Fact]
    public void PersistedLibraryAndRootBodiesKeepRecordedNoThrowPromise()
    {
        var leaf = Leaf("[NoThrow] public int32 Leaf() { return 40; }");
        var middle = Middle("[NoThrow] public int32 Middle() { return Leaf(); }", leaf);
        var rootCompilation = Compilation.Create(SyntaxTree.Parse(
            "[NoThrow] int32 Main() { return Middle(); }"),
            NativeModuleCompiler.CreateGraphReference([middle]));
        Assert.False(rootCompilation.HasErrors, string.Join(Environment.NewLine, rootCompilation.Diagnostics));
        var root = NativeModuleCompiler.CreateGraphConsumer(Assert.IsType<BoundProgram>(rootCompilation.Program),
            [middle], NativeRuntimePolicy.FailFast);
        NativeModuleCompiler.ValidateGraph(root, [leaf, middle]);
        var weakenedMiddle = WeakenRecordedImport(middle, "Leaf", "|NoThrow|MayReturn", "|MayThrow|MayReturn");
        _ = Assert.Throws<ModuleFormatException>(() => NativeModuleCompiler.ValidateGraph(root,
            [leaf, weakenedMiddle]));
        var weakenedRoot = WeakenRecordedImport(root, "Middle", "|NoThrow|MayReturn", "|MayThrow|MayReturn");
        _ = Assert.Throws<ModuleFormatException>(() => NativeModuleCompiler.ValidateGraph(weakenedRoot,
            [leaf, middle]));
    }

    [Fact]
    public void PersistedLibraryCannotWeakenRequiredNoReturnPromise()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(
            "[DoesNotReturn] public void Halt() { while (true) { } }"));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var leaf = NativeModuleCompiler.CreateProducer(Assert.IsType<BoundProgram>(compilation.Program),
            ["Halt"], "NodeB", NativeRuntimePolicy.FailFast);
        var middleCompilation = Compilation.CreateLibrary(SyntaxTree.Parse(
            "public int32 Middle() { Halt(); }"), NativeModuleCompiler.CreateGraphReference([leaf]));
        Assert.False(middleCompilation.HasErrors, string.Join(Environment.NewLine, middleCompilation.Diagnostics));
        var middle = NativeModuleCompiler.CreateGraphProducer(Assert.IsType<BoundProgram>(middleCompilation.Program),
            ["Middle"], "NodeA", [leaf], NativeRuntimePolicy.FailFast);
        var root = Consumer(middle);
        NativeModuleCompiler.ValidateGraph(root, [leaf, middle]);
        var weakened = WeakenRecordedImport(middle, "Halt", "|MayThrow|DoesNotReturn", "|MayThrow|MayReturn");
        _ = Assert.Throws<ModuleFormatException>(() => NativeModuleCompiler.ValidateGraph(root,
            [leaf, weakened]));
    }

    [Theory]
    [InlineData(NativeRuntimePolicy.FailFast)]
    [InlineData(NativeRuntimePolicy.CheckedExceptions)]
    public void GraphReplacementCannotWeakenRequiredNoReturnPromise(NativeRuntimePolicy policy)
    {
        var leaf = Leaf("[DoesNotReturn] public void Leaf() { while (true) { } }", policy);
        var rootCompilation = Compilation.Create(SyntaxTree.Parse(
            "int32 Main() { Leaf(); return 42; }"),
            NativeModuleCompiler.CreateGraphReference([leaf]), (BoundRuntimePolicy)policy);
        Assert.False(rootCompilation.HasErrors, string.Join(Environment.NewLine, rootCompilation.Diagnostics));
        var root = NativeModuleCompiler.CreateGraphConsumer(Assert.IsType<BoundProgram>(rootCompilation.Program),
            [leaf], policy);

        NativeModuleCompiler.ValidateGraph(root, [leaf]);
        var replacement = Leaf("public void Leaf() { }", policy);
        _ = Assert.Throws<ModuleContractException>(() => NativeModuleCompiler.ValidateGraph(root, [replacement]));
    }

    [Theory]
    [InlineData(NativeRuntimePolicy.FailFast)]
    [InlineData(NativeRuntimePolicy.CheckedExceptions)]
    public void GraphReplacementCannotWeakenNoThrowPromise(NativeRuntimePolicy policy)
    {
        var leaf = Leaf("[NoThrow] public int32 Leaf() { return 40; }", policy);
        var middle = Middle("public int32 Middle() { return Leaf() + 1; }", leaf, policy);
        var root = Consumer(middle, policy);

        NativeModuleCompiler.ValidateGraph(root, [leaf, middle]);
        var replacement = Leaf("public int32 Leaf() { return 40; }", policy);
        _ = Assert.Throws<ModuleContractException>(() =>
            NativeModuleCompiler.ValidateGraph(root, [replacement, middle]));
    }

    [Fact]
    public void LibraryMayExportOrdinaryMainWithoutCreatingAnEntryPoint()
    {
        var leaf = Leaf("public int32 Leaf() { return 40; }");
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(
            "public int32 Main() { return Leaf(); }"),
            NativeModuleCompiler.CreateGraphReference([leaf]));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = Assert.IsType<BoundProgram>(compilation.Program);
        Assert.Equal(-1, program.EntryPoint);
        var graph = NativeModuleCompiler.CreateGraphProducer(program, ["Main"], "NodeA",
            [leaf], NativeRuntimePolicy.FailFast);
        Assert.Equal(["Main"], NativeModuleCompiler.InspectGraphArtifact(graph).Exports);
    }
}
