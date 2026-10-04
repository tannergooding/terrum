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

public sealed partial class NativeManagedClassModuleTests
{
    [Theory]
    [InlineData(NativeRuntimePolicy.FailFast)]
    [InlineData(NativeRuntimePolicy.CheckedExceptions)]
    public async Task LifetimeEndGraphLibraryRetainsPrivateClassRootsThroughDependenciesAsync(NativeRuntimePolicy policy)
    {
        var leaf = NativeModuleCompiler.CreateProducer(Compile(
            "public int32 Seed() { return 42; }", policy), ["Seed"], "EndSeed", policy);
        var middle = NativeModuleCompiler.CreateGraphProducer(Compile("""
            class Box {
                public int32 Value;
                public Box(int32 value) { Value = value; }
            }
            Box Create() {
                require Box.TryCreate(Seed(), out var box);
                return box;
            }
            public int32 Middle() {
                init Box value;
                value = Create();
                Box duplicate = value;
                value = _;
                if (ManagedHeap.Collect() != 0 || duplicate.Value != 42) { return 1; }
                duplicate = _;
                if (ManagedHeap.Collect() != 1) { return 2; }
                value = Create();
                if (ManagedHeap.Collect() != 0 || value.Value != 42) { return 3; }
                int32 result = value.Value;
                value = _;
                if (ManagedHeap.Collect() != 1) { return 4; }
                return result;
            }
            """, policy, NativeModuleCompiler.CreateGraphReference([leaf])),
            ["Middle"], "EndMiddle", [leaf], policy);
        Assert.Equal(53, GraphSemanticVersion(middle));
        Assert.Equal("native-19", NativeModuleCompiler.InspectGraphArtifact(middle).NativeProfileVersion);
        foreach (var version in new[] { 32, 40, 41, 42, 43, 44, 45, 46 })
        {
            var old = SetGraphSemanticVersion(middle, version);
            _ = Assert.Throws<ModuleFormatException>(() => NativeModuleCompiler.CreateGraphReference([old]));
        }
        var root = NativeModuleCompiler.CreateGraphConsumer(Compile(
            "int32 Main() { return Middle(); }", policy,
            NativeModuleCompiler.CreateGraphReference([middle]), library: false), [middle], policy);
        NativeModuleCompiler.ValidateGraph(root, [middle, leaf]);
        Assert.Equal(42, await NativeModuleCompiler.RunGraphAsync(root, [middle, leaf],
            TimeSpan.FromSeconds(10), policy).ConfigureAwait(true));
        var framework = new DirectoryInfo(AppContext.BaseDirectory);
        var configuration = framework.Parent?.Name
            ?? throw new InvalidOperationException("Missing test configuration directory.");
        var apphost = Path.Combine(RepositoryRoot(), "artifacts", "bin", "sources", "terrumc",
            configuration, framework.Name, "terrumc.exe");
        Assert.Equal(42, await NativeModuleCompiler.RunGraphJitAsync(root, [leaf, middle],
            apphost, TimeSpan.FromSeconds(30), policy).ConfigureAwait(true));

        var noEnd = NativeModuleCompiler.CreateGraphProducer(Compile("""
            public int32 NoEnd() {
                init int32 adjustment;
                adjustment = 1;
                return Seed() + adjustment - 1;
            }
            """, policy, NativeModuleCompiler.CreateGraphReference([leaf])),
            ["NoEnd"], "NoEndMiddle", [leaf], policy);
        Assert.Equal(53, GraphSemanticVersion(noEnd));
        _ = NativeModuleCompiler.CreateGraphReference([noEnd]);
    }

    private const string ManagedGraphLeaf = """
        namespace Values;
        public struct Payload {
            public int64 First;
            public int64 Second;
            public Payload(int64 first, int64 second) { First = first; Second = second; }
        }
        class Cell {
            public Payload Data;
            public Cell(Payload data) { Data = data; }
        }
        public Payload Make(int64 first, int64 second) { return new Payload(first, second); }
        public Payload Forward(Payload input) {
            require Cell.TryCreate(input, out var box);
            return new Payload(box.Data.First + 0, box.Data.Second);
        }
        public bool Fail(Payload input, out Payload output) {
            require Cell.TryCreate(input, out var box);
            output = box.Data;
            require false;
            return true;
        }
        public int32 CollectLeaf() { return ManagedHeap.Collect(); }
        """;

    private const string ManagedGraphBias = """
        namespace Source;
        public int32 Bias() { return 0; }
        """;

    private static string ManagedMiddle(NativeRuntimePolicy policy) => $$"""
        using Values;
        using Source;
        class Cell {
            public Payload Data;
            public Cell(Payload data) { Data = data; }
        }
        public Payload Middle(Payload input) {
            Payload value = Forward(input);
            require Cell.TryCreate(value, out var box);
            require Bias() == 0;
            {{(policy == NativeRuntimePolicy.CheckedExceptions ? """
            unsafe {
                try { Fail(value, out var ignored); return Make(0, 0); }
                catch { require box.Data.First == value.First; }
            }
            """ : "")}}
            require CollectLeaf() == {{(policy == NativeRuntimePolicy.CheckedExceptions ? 2 : 1)}};
            return box.Data;
        }
        public bool TryMiddle(bool available, Payload input, out Payload output) {
            if (!available) { return false; }
            output = Middle(input);
            return true;
        }
        public int32 CollectMiddle() { return ManagedHeap.Collect(); }
        """;

    private const string ManagedGraphRoot = """
        using Values;
        using Source;
        class Cell {
            public Payload Data;
            public Cell(Payload data) { Data = data; }
        }
        int32 Main() {
            Payload input = Make(39, 2);
            require !TryMiddle(false, input, out var absent);
            require TryMiddle(true, input, out var result);
            require Cell.TryCreate(result, out var root);
            require CollectMiddle() == 1;
            require CollectLeaf() == 0;
            require ManagedHeap.Collect() == 0;
            return int32(root.Data.First + root.Data.Second) + Bias();
        }
        """;

    [Theory]
    [InlineData(NativeRuntimePolicy.FailFast)]
    [InlineData(NativeRuntimePolicy.CheckedExceptions)]
    public async Task PrivateInitInManagedGraphMiddlePreservesDownstreamImagesAsync(NativeRuntimePolicy policy)
    {
        const string Leaf = """
            namespace Source;
            public int32 Bias() { return 2; }
            """;
        const string Middle = """
            using Source;
            class Cell {
                public int32 Value;
                public Cell(int32 value) { Value = value; }
            }
            bool Fill(init ref<int32> value) {
                value = Bias();
                return true;
            }
            public int32 Middle() {
                require Cell.TryCreate(40, out var cell);
                require Fill(init var offset);
                require ManagedHeap.Collect() == 0;
                return cell.Value + offset;
            }
            """;
        const string Root = "int32 Main() { return Middle(); }";
        var leaf = NativeModuleCompiler.CreateProducer(Compile(Leaf, policy), ["Source.Bias"], "NodeB", policy);
        var middle = NativeModuleCompiler.CreateGraphProducer(Compile(Middle, policy,
            NativeModuleCompiler.CreateGraphReference([leaf])), ["Middle"], "NodeA", [leaf], policy);
        Assert.Equal(53, GraphSemanticVersion(middle));
        var middleHash = SHA256.HashData(middle);
        var root = NativeModuleCompiler.CreateGraphConsumer(Compile(Root, policy,
            NativeModuleCompiler.CreateGraphReference([middle]), library: false), [middle], policy);
        var rootHash = SHA256.HashData(root);
        NativeModuleCompiler.ValidateGraph(root, [leaf, middle]);
        Assert.Equal(42, await NativeModuleCompiler.RunGraphAsync(root, [middle, leaf],
            TimeSpan.FromSeconds(10), policy).ConfigureAwait(true));

        var replacement = NativeModuleCompiler.CreateProducer(Compile(
            Leaf.Replace("return 2", "return 3", StringComparison.Ordinal), policy),
            ["Source.Bias"], "NodeB", policy);
        NativeModuleCompiler.ValidateGraph(root, [replacement, middle]);
        Assert.Equal(43, await NativeModuleCompiler.RunGraphAsync(root, [replacement, middle],
            TimeSpan.FromSeconds(10), policy).ConfigureAwait(true));
        var framework = new DirectoryInfo(AppContext.BaseDirectory);
        var configuration = framework.Parent?.Name
            ?? throw new InvalidOperationException("Missing test configuration directory.");
        var apphost = Path.Combine(RepositoryRoot(), "artifacts", "bin", "sources", "terrumc",
            configuration, framework.Name, "terrumc.exe");
        Assert.Equal(43, await NativeModuleCompiler.RunGraphJitAsync(root, [middle, replacement],
            apphost, TimeSpan.FromSeconds(30), policy).ConfigureAwait(true));
        Assert.Equal(rootHash, SHA256.HashData(root));
        Assert.Equal(middleHash, SHA256.HashData(middle));
    }

    [Theory]
    [InlineData(NativeRuntimePolicy.FailFast)]
    [InlineData(NativeRuntimePolicy.CheckedExceptions)]
    public async Task ManagedRootAndMiddleKeepSeparateHeapsAcrossGraphReplacementAsync(NativeRuntimePolicy policy)
    {
        var originalProgram = Compile(ManagedGraphLeaf, policy);
        string[] leafExports = ["Values.Make", "Values.Forward", "Values.Fail", "Values.CollectLeaf"];
        var original = NativeModuleCompiler.CreateProducer(originalProgram, leafExports, "NodeB", policy);
        Assert.Equal(53, GraphSemanticVersion(original));
        var bias = NativeModuleCompiler.CreateProducer(Compile(ManagedGraphBias, policy),
            ["Source.Bias"], "NodeC", policy);
        var middleProgram = Compile(ManagedMiddle(policy), policy,
            NativeModuleCompiler.CreateGraphReference([original, bias]));
        string[] middleExports = ["Middle", "TryMiddle", "CollectMiddle"];
        var middle = NativeModuleCompiler.CreateGraphProducer(middleProgram, middleExports, "NodeA",
            [original, bias], policy);
        Assert.Equal(53, GraphSemanticVersion(middle));
        var middleHash = SHA256.HashData(middle);
        var reference = NativeModuleCompiler.CreateGraphReference([middle, original, bias]);
        Assert.Equal("Values.Payload", Assert.Single(reference.Structures).Name);
        var rootProgram = Compile(ManagedGraphRoot, policy,
            reference, library: false);
        var root = NativeModuleCompiler.CreateGraphConsumer(rootProgram, [middle, original, bias], policy);
        Assert.Equal(53, GraphSemanticVersion(root));
        var rootHash = SHA256.HashData(root);
        Assert.Equal("native-19", NativeModuleCompiler.InspectGraphArtifact(middle).NativeProfileVersion);
        Assert.Equal("native-19", NativeModuleCompiler.InspectGraphArtifact(root).NativeProfileVersion);

        NativeModuleCompiler.ValidateGraph(root, [original, bias, middle]);
        Assert.Equal(41, await NativeModuleCompiler.RunGraphAsync(root, [middle, original, bias],
            TimeSpan.FromSeconds(10), policy).ConfigureAwait(true));
        var replacementProgram = Compile(ManagedGraphLeaf.Replace("box.Data.First + 0", "box.Data.First + 1",
            StringComparison.Ordinal), policy);
        var replacement = NativeModuleCompiler.CreateProducer(replacementProgram, leafExports, "NodeB", policy);
        NativeModuleCompiler.ValidateGraph(root, [middle, bias, replacement]);
        Assert.Equal(42, await NativeModuleCompiler.RunGraphAsync(root, [replacement, bias, middle],
            TimeSpan.FromSeconds(10), policy).ConfigureAwait(true));
        var framework = new DirectoryInfo(AppContext.BaseDirectory);
        var configuration = framework.Parent?.Name
            ?? throw new InvalidOperationException("Missing test configuration directory.");
        var apphost = Path.Combine(RepositoryRoot(), "artifacts", "bin", "sources", "terrumc",
            configuration, framework.Name, "terrumc.exe");
        Assert.Equal(42, await NativeModuleCompiler.RunGraphJitAsync(root, [middle, replacement, bias],
            apphost, TimeSpan.FromSeconds(30), policy).ConfigureAwait(true));
        Assert.Equal(rootHash, SHA256.HashData(root));
        Assert.Equal(middleHash, SHA256.HashData(middle));

        var downgradedMiddle = SetGraphSemanticVersion(middle, 31);
        var downgradedRoot = SetGraphSemanticVersion(root, 31);
        _ = Assert.Throws<ModuleFormatException>(() => NativeModuleCompiler.InspectGraphArtifact(downgradedMiddle));
        _ = Assert.Throws<ModuleFormatException>(() => NativeModuleCompiler.InspectGraphArtifact(downgradedRoot));
        _ = Assert.Throws<ModuleFormatException>(() => NativeModuleCompiler.ValidateGraph(downgradedRoot,
            [original, middle, bias]));
    }

    [Theory]
    [InlineData(NativeRuntimePolicy.FailFast)]
    [InlineData(NativeRuntimePolicy.CheckedExceptions)]
    public async Task ManagedGraphSampleKeepsMiddleAndRootUnchangedAcrossLeafReplacementAsync(
        NativeRuntimePolicy policy)
    {
        var directory = Path.Combine(RepositoryRoot(), "samples", "native", "graph");
        var originalSource = await File.ReadAllTextAsync(Path.Combine(directory,
            "copy-contract-original.terrum")).ConfigureAwait(true);
        var managedSource = await File.ReadAllTextAsync(Path.Combine(directory,
            "copy-contract-managed.terrum")).ConfigureAwait(true);
        var middleSource = await File.ReadAllTextAsync(Path.Combine(directory,
            "managed-copy-middle.terrum")).ConfigureAwait(true);
        var rootSource = await File.ReadAllTextAsync(Path.Combine(directory,
            "managed-copy-root.terrum")).ConfigureAwait(true);
        string[] leafExports = ["ReadSnapshot", "TryReadSnapshot", "ReadTotal"];
        var original = NativeModuleCompiler.CreateProducer(Compile(originalSource, policy),
            leafExports, "NodeB", policy);
        var middle = NativeModuleCompiler.CreateGraphProducer(Compile(middleSource, policy,
            NativeModuleCompiler.CreateGraphReference([original])), ["ForwardSnapshot", "MiddleValue"],
            "NodeA", [original], policy);
        Assert.Equal(53, GraphSemanticVersion(middle));
        var root = NativeModuleCompiler.CreateGraphConsumer(Compile(rootSource, policy,
            NativeModuleCompiler.CreateGraphReference([middle, original]), library: false),
            [middle, original], policy);
        Assert.Equal(53, GraphSemanticVersion(root));
        var rootHash = SHA256.HashData(root);
        var middleHash = SHA256.HashData(middle);
        Assert.Equal(42, await NativeModuleCompiler.RunGraphAsync(root, [middle, original],
            TimeSpan.FromSeconds(10), policy).ConfigureAwait(true));
        var replacement = NativeModuleCompiler.CreateProducer(Compile(managedSource, policy),
            leafExports, "NodeB", policy);
        Assert.Equal(53, GraphSemanticVersion(replacement));
        Assert.Equal(44, await NativeModuleCompiler.RunGraphAsync(root, [replacement, middle],
            TimeSpan.FromSeconds(10), policy).ConfigureAwait(true));
        var framework = new DirectoryInfo(AppContext.BaseDirectory);
        var configuration = framework.Parent?.Name
            ?? throw new InvalidOperationException("Missing test configuration directory.");
        var apphost = Path.Combine(RepositoryRoot(), "artifacts", "bin", "sources", "terrumc",
            configuration, framework.Name, "terrumc.exe");
        Assert.Equal(42, await NativeModuleCompiler.RunGraphJitAsync(root, [original, middle],
            apphost, TimeSpan.FromSeconds(30), policy).ConfigureAwait(true));
        Assert.Equal(44, await NativeModuleCompiler.RunGraphJitAsync(root, [middle, replacement],
            apphost, TimeSpan.FromSeconds(30), policy).ConfigureAwait(true));
        Assert.Equal(rootHash, SHA256.HashData(root));
        Assert.Equal(middleHash, SHA256.HashData(middle));
    }

    [Theory]
    [InlineData(NativeRuntimePolicy.FailFast)]
    [InlineData(NativeRuntimePolicy.CheckedExceptions)]
    public void UnmanagedGraphsRejectRetiredSemanticModuleRevisions(NativeRuntimePolicy policy)
    {
        var leaf = NativeModuleCompiler.CreateProducer(Compile("public int32 Leaf() { return 40; }", policy),
            ["Leaf"], "NodeB", policy);
        var middle = NativeModuleCompiler.CreateGraphProducer(Compile(
            "public int32 Middle() { return Leaf() + 2; }", policy,
            NativeModuleCompiler.CreateGraphReference([leaf])), ["Middle"], "NodeA", [leaf], policy);
        var root = NativeModuleCompiler.CreateGraphConsumer(Compile(
            "int32 Main() { return Middle(); }", policy,
            NativeModuleCompiler.CreateGraphReference([middle]), library: false), [middle], policy);
        Assert.Equal(53, GraphSemanticVersion(middle));
        Assert.Equal(53, GraphSemanticVersion(root));
        NativeModuleCompiler.ValidateGraph(root, [leaf, middle]);
        _ = Assert.Throws<ModuleFormatException>(() =>
            NativeModuleCompiler.InspectGraphArtifact(SetGraphSemanticVersion(middle, 32)));
        _ = Assert.Throws<ModuleFormatException>(() =>
            NativeModuleCompiler.InspectGraphArtifact(SetGraphSemanticVersion(root, 32)));
    }

    [Theory]
    [InlineData(NativeRuntimePolicy.FailFast)]
    [InlineData(NativeRuntimePolicy.CheckedExceptions)]
    public async Task NodeOnlyGraphParticipantsRetainImageLocalRootsAsync(NativeRuntimePolicy policy)
    {
        var leaf = NativeModuleCompiler.CreateProducer(Compile("public int32 Value() { return 42; }", policy),
            ["Value"], "NodeB", policy);
        var middle = NativeModuleCompiler.CreateGraphProducer(Compile("""
            public int32 Middle() {
                require ManagedNode.TryCreate(Value(), out var node);
                require ManagedHeap.Collect() == 0;
                return node.Value;
            }
            public int32 MiddleCollect() { return ManagedHeap.Collect(); }
            """, policy, NativeModuleCompiler.CreateGraphReference([leaf])),
            ["Middle", "MiddleCollect"], "NodeA", [leaf], policy);
        var root = NativeModuleCompiler.CreateGraphConsumer(Compile("""
            int32 Main() {
                require ManagedNode.TryCreate(Middle(), out var node);
                require MiddleCollect() == 1;
                return node.Value;
            }
            """, policy, NativeModuleCompiler.CreateGraphReference([middle]), library: false),
            [middle], policy);
        Assert.Equal(53, GraphSemanticVersion(middle));
        Assert.Equal(53, GraphSemanticVersion(root));
        Assert.Equal("native-18", NativeModuleCompiler.InspectGraphArtifact(root).NativeProfileVersion);
        Assert.Equal(42, await NativeModuleCompiler.RunGraphAsync(root, [leaf, middle],
            TimeSpan.FromSeconds(10), policy).ConfigureAwait(true));
        var framework = new DirectoryInfo(AppContext.BaseDirectory);
        var configuration = framework.Parent?.Name
            ?? throw new InvalidOperationException("Missing test configuration directory.");
        var apphost = Path.Combine(RepositoryRoot(), "artifacts", "bin", "sources", "terrumc",
            configuration, framework.Name, "terrumc.exe");
        Assert.Equal(42, await NativeModuleCompiler.RunGraphJitAsync(root, [middle, leaf],
            apphost, TimeSpan.FromSeconds(30), policy).ConfigureAwait(true));
    }

    [Theory]
    [InlineData(NativeRuntimePolicy.FailFast)]
    [InlineData(NativeRuntimePolicy.CheckedExceptions)]
    public void ManagedGraphParticipantsRejectOwningImportsAndExports(NativeRuntimePolicy policy)
    {
        const string OwningProvider = """
            public unique struct Token {
                public int32 Value;
                public Token(int32 value) { Value = value; }
            }
            public Token Pass(Token value) { return move value; }
            """;
        var provider = NativeModuleCompiler.CreateProducer(Compile(OwningProvider, policy),
            ["Pass"], "NodeB", policy);
        var reference = NativeModuleCompiler.CreateGraphReference([provider]);
        const string Managed = """
            class Cell {
                public int32 Value;
                public Cell(int32 value) { Value = value; }
            }
            public int32 Read() {
                require Cell.TryCreate(42, out var cell);
                return cell.Value;
            }
            """;
        var imported = Compilation.CreateLibrary(SyntaxTree.Parse(Managed), reference,
            (BoundRuntimePolicy)policy);
        Assert.Contains(imported.Diagnostics, static diagnostic => diagnostic.Code == "TRM3023");
        var bias = NativeModuleCompiler.CreateProducer(Compile(ManagedGraphBias, policy),
            ["Source.Bias"], "NodeC", policy);
        var export = Compile(Managed + """
            public unique struct Owned {
                public int32 Value;
                public Owned(int32 value) { Value = value; }
            }
            public int32 Leak(Owned value) { return value.Value; }
            """, policy, NativeModuleCompiler.CreateGraphReference([bias]));
        _ = Assert.Throws<ModuleContractException>(() => NativeModuleCompiler.CreateGraphProducer(export,
            ["Leak"], "NodeA", [bias], policy));
        var handle = Compile(Managed + """
            public ManagedNode Leak() {
                require ManagedNode.TryCreate(42, out var node);
                return node;
            }
            """, policy, NativeModuleCompiler.CreateGraphReference([bias]));
        _ = Assert.Throws<ModuleContractException>(() => NativeModuleCompiler.CreateGraphProducer(handle,
            ["Leak"], "NodeA", [bias], policy));
        var view = Compilation.CreateLibrary(SyntaxTree.Parse(Managed + """
            public int32 Leak(ReadonlyInt32BufferView value) { return value.Length; }
            """), NativeModuleCompiler.CreateGraphReference([bias]), (BoundRuntimePolicy)policy);
        Assert.True(view.HasErrors);
        var root = Compilation.Create(SyntaxTree.Parse(Managed + "int32 Main() { return Read(); }"),
            reference, (BoundRuntimePolicy)policy);
        Assert.Contains(root.Diagnostics, static diagnostic => diagnostic.Code == "TRM3023");
    }

    private static int GraphSemanticVersion(byte[] package)
    {
        var graph = package.AsSpan().StartsWith("TNGP"u8);
        var contractLength = BinaryPrimitives.ReadInt32LittleEndian(package.AsSpan(graph ? 11 : 10, 4));
        var imageLength = BinaryPrimitives.ReadInt32LittleEndian(package.AsSpan(graph ? 15 : 14, 4));
        var start = package.Length - 32 - imageLength - contractLength;
        Assert.True(package.AsSpan(start, 4).SequenceEqual("TRRM"u8));
        return BinaryPrimitives.ReadInt32LittleEndian(package.AsSpan(start + 4, 4));
    }

    private static byte[] SetGraphSemanticVersion(byte[] package, int version)
    {
        var changed = (byte[])package.Clone();
        var contractLength = BinaryPrimitives.ReadInt32LittleEndian(changed.AsSpan(11, 4));
        var imageLength = BinaryPrimitives.ReadInt32LittleEndian(changed.AsSpan(15, 4));
        var start = changed.Length - 32 - imageLength - contractLength;
        BinaryPrimitives.WriteInt32LittleEndian(changed.AsSpan(start + 4, 4), version);
        SHA256.HashData(changed.AsSpan(0, changed.Length - 32)).CopyTo(changed, changed.Length - 32);
        return changed;
    }
}
