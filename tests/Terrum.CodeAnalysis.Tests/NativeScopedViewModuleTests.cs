// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed class NativeScopedViewModuleTests
{
    private static string Fixture(string name)
    {
        var repository = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", ".."));
        return File.ReadAllText(Path.Combine(repository, "samples", "native", "graph", name));
    }

    private static BoundProgram Compile(string source, NativeRuntimePolicy policy,
        CompilationReference? reference = null, bool library = true)
    {
        var tree = SyntaxTree.Parse(source);
        var compilation = library
            ? reference is null ? Compilation.CreateLibrary(tree, (BoundRuntimePolicy)policy)
                : Compilation.CreateLibrary(tree, reference, (BoundRuntimePolicy)policy)
            : reference is null ? Compilation.Create(tree, (BoundRuntimePolicy)policy)
                : Compilation.Create(tree, reference, (BoundRuntimePolicy)policy);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        return Assert.IsType<BoundProgram>(compilation.Program);
    }

    [Theory]
    [InlineData(NativeRuntimePolicy.FailFast, "native-10")]
    [InlineData(NativeRuntimePolicy.CheckedExceptions, "native-11")]
    public async Task InternalViewReplacementServicesUnchangedSingleAndGraphConsumersAsync(
        NativeRuntimePolicy policy, string originalProfile)
    {
        var original = NativeModuleCompiler.CreateProducer(
            Compile(Fixture("scoped-view-leaf.terrum"), policy), ["Leaf"], "NodeB", policy);
        var replacement = NativeModuleCompiler.CreateProducer(
            Compile(Fixture("scoped-view-leaf-replacement.terrum"), policy), ["Leaf"], "NodeB", policy);
        Assert.Equal(originalProfile, NativeModuleCompiler.Inspect(original).NativeProfileVersion);
        Assert.Equal("native-16", NativeModuleCompiler.Inspect(replacement).NativeProfileVersion);

        var single = NativeModuleCompiler.CreateConsumer(
            Compile("int32 Main() { return Leaf() + 2; }", policy,
                NativeModuleCompiler.CreateReference(original), library: false), original, policy);
        var persistedSingle = (byte[])single.Clone();
        NativeModuleCompiler.Validate(single, original);
        NativeModuleCompiler.Validate(single, replacement);
        Assert.Equal(42, await NativeModuleCompiler.RunAsync(single, original, TimeSpan.FromSeconds(10)));
        Assert.Equal(44, await NativeModuleCompiler.RunAsync(single, replacement, TimeSpan.FromSeconds(10)));
        Assert.Equal(persistedSingle, single);

        var middle = NativeModuleCompiler.CreateGraphProducer(
            Compile(Fixture("middle.terrum"), policy, NativeModuleCompiler.CreateGraphReference([original])),
            ["Middle"], "NodeA", [original], policy);
        var root = NativeModuleCompiler.CreateGraphConsumer(
            Compile(Fixture("consumer.terrum"), policy, NativeModuleCompiler.CreateGraphReference([middle]),
                library: false), [middle], policy);
        var persistedMiddle = (byte[])middle.Clone();
        var persistedRoot = (byte[])root.Clone();
        Assert.Equal(originalProfile, NativeModuleCompiler.InspectGraphArtifact(middle).NativeProfileVersion);
        Assert.Equal(originalProfile, NativeModuleCompiler.InspectGraphArtifact(root).NativeProfileVersion);
        NativeModuleCompiler.ValidateGraph(root, [middle, original]);
        NativeModuleCompiler.ValidateGraph(root, [replacement, middle]);
        Assert.Equal(42, await NativeModuleCompiler.RunGraphAsync(root, [original, middle],
            TimeSpan.FromSeconds(10), policy));
        Assert.Equal(44, await NativeModuleCompiler.RunGraphAsync(root, [middle, replacement],
            TimeSpan.FromSeconds(10), policy));
        var frameworkDirectory = new DirectoryInfo(AppContext.BaseDirectory);
        var configuration = frameworkDirectory.Parent?.Name
            ?? throw new InvalidOperationException("Missing test configuration directory.");
        var repository = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", ".."));
        var apphost = Path.Combine(repository, "artifacts", "bin", "sources", "terrumc",
            configuration, frameworkDirectory.Name, "terrumc.exe");
        Assert.Equal(42, await NativeModuleCompiler.RunGraphJitAsync(root, [middle, original], apphost,
            TimeSpan.FromSeconds(30), policy));
        Assert.Equal(44, await NativeModuleCompiler.RunGraphJitAsync(root, [replacement, middle], apphost,
            TimeSpan.FromSeconds(30), policy));
        Assert.Equal(persistedRoot, root);
        Assert.Equal(persistedMiddle, middle);

        var incompatible = NativeModuleCompiler.CreateProducer(
            Compile("public int64 Leaf() { return 42; }", policy), ["Leaf"], "NodeB", policy);
        _ = Assert.Throws<ModuleContractException>(() => NativeModuleCompiler.Validate(single, incompatible));
        _ = Assert.Throws<ModuleContractException>(() => NativeModuleCompiler.ValidateGraph(root, [middle, incompatible]));

        var unknown = (byte[])replacement.Clone();
        var at = unknown.AsSpan().IndexOf("native-16"u8);
        Assert.True(at >= 0);
        "native-99"u8.CopyTo(unknown.AsSpan(at, 9));
        SHA256.HashData(unknown.AsSpan(0, unknown.Length - 32)).CopyTo(unknown, unknown.Length - 32);
        _ = Assert.Throws<ModuleFormatException>(() => NativeModuleCompiler.Inspect(unknown));
        _ = Assert.Throws<ModuleFormatException>(() => NativeModuleCompiler.ValidateGraph(root, [middle, unknown]));

        var escapedContract = (byte[])replacement.Clone();
        var signature = escapedContract.AsSpan().IndexOf("Leaf|2|"u8);
        Assert.True(signature >= 0);
        "Leaf|8|"u8.CopyTo(escapedContract.AsSpan(signature, 7));
        SHA256.HashData(escapedContract.AsSpan(0, escapedContract.Length - 32))
            .CopyTo(escapedContract, escapedContract.Length - 32);
        _ = Assert.Throws<ModuleFormatException>(() => NativeModuleCompiler.Inspect(escapedContract));
        _ = Assert.Throws<ModuleFormatException>(() => NativeModuleCompiler.ValidateGraph(root, [middle, escapedContract]));

        var escaping = Compilation.CreateLibrary(SyntaxTree.Parse(
            "public int32 Leak(ReadonlyInt32BufferView view) { return view.Length; }"),
            (BoundRuntimePolicy)policy);
        Assert.True(escaping.HasErrors);
    }

    [Theory]
    [InlineData(NativeRuntimePolicy.FailFast)]
    [InlineData(NativeRuntimePolicy.CheckedExceptions)]
    public async Task WritableViewReplacementServicesPersistedNative15And16ConsumersAsync(NativeRuntimePolicy policy)
    {
        var original = NativeModuleCompiler.CreateProducer(
            Compile(Fixture("writable-view-leaf-original.terrum"), policy), ["Leaf"], "NodeB", policy);
        var readonlyReplacement = NativeModuleCompiler.CreateProducer(
            Compile(Fixture("scoped-view-leaf-replacement.terrum"), policy), ["Leaf"], "NodeB", policy);
        var writableReplacement = NativeModuleCompiler.CreateProducer(
            Compile(Fixture("writable-view-leaf.terrum"), policy), ["Leaf"], "NodeB", policy);
        Assert.Equal("native-15", NativeModuleCompiler.Inspect(original).NativeProfileVersion);
        Assert.Equal("native-16", NativeModuleCompiler.Inspect(readonlyReplacement).NativeProfileVersion);
        Assert.Equal("native-17", NativeModuleCompiler.Inspect(writableReplacement).NativeProfileVersion);

        var single = NativeModuleCompiler.CreateConsumer(
            Compile("int32 Main() { return Leaf() + 2; }", policy,
                NativeModuleCompiler.CreateReference(original), library: false), original, policy);
        var persistedSingle = (byte[])single.Clone();
        var middle = NativeModuleCompiler.CreateGraphProducer(
            Compile(Fixture("middle.terrum"), policy, NativeModuleCompiler.CreateGraphReference([original])),
            ["Middle"], "NodeA", [original], policy);
        var root = NativeModuleCompiler.CreateGraphConsumer(
            Compile(Fixture("consumer.terrum"), policy, NativeModuleCompiler.CreateGraphReference([middle]),
                library: false), [middle], policy);
        var persistedMiddle = (byte[])middle.Clone();
        var persistedRoot = (byte[])root.Clone();
        var originalGraphProfile = policy == NativeRuntimePolicy.FailFast ? "native-10" : "native-11";
        Assert.Equal(originalGraphProfile, NativeModuleCompiler.InspectGraphArtifact(middle).NativeProfileVersion);
        Assert.Equal(originalGraphProfile, NativeModuleCompiler.InspectGraphArtifact(root).NativeProfileVersion);

        var frameworkDirectory = new DirectoryInfo(AppContext.BaseDirectory);
        var configuration = frameworkDirectory.Parent?.Name
            ?? throw new InvalidOperationException("Missing test configuration directory.");
        var repository = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", ".."));
        var apphost = Path.Combine(repository, "artifacts", "bin", "sources", "terrumc",
            configuration, frameworkDirectory.Name, "terrumc.exe");
        foreach (var (provider, expected) in new[] {
            (original, 42),
            (readonlyReplacement, 44),
            (writableReplacement, 46),
        })
        {
            NativeModuleCompiler.Validate(single, provider);
            Assert.Equal(expected, await NativeModuleCompiler.RunAsync(single, provider, TimeSpan.FromSeconds(10)));
            NativeModuleCompiler.ValidateGraph(root, [middle, provider]);
            Assert.Equal(expected, await NativeModuleCompiler.RunGraphAsync(root, [provider, middle],
                TimeSpan.FromSeconds(10), policy));
            Assert.Equal(expected, await NativeModuleCompiler.RunGraphJitAsync(root, [middle, provider], apphost,
                TimeSpan.FromSeconds(30), policy));
        }

        var writableMiddle = NativeModuleCompiler.CreateGraphProducer(
            Compile(Fixture("writable-view-middle.terrum"), policy,
                NativeModuleCompiler.CreateGraphReference([original])),
            ["Middle"], "NodeA", [original], policy);
        Assert.Equal("native-17", NativeModuleCompiler.InspectGraphArtifact(writableMiddle).NativeProfileVersion);
        NativeModuleCompiler.ValidateGraph(root, [original, writableMiddle]);
        Assert.Equal(42, await NativeModuleCompiler.RunGraphAsync(root, [writableMiddle, original],
            TimeSpan.FromSeconds(10), policy));
        NativeModuleCompiler.ValidateGraph(root, [writableReplacement, writableMiddle]);
        Assert.Equal(46, await NativeModuleCompiler.RunGraphAsync(root, [writableMiddle, writableReplacement],
            TimeSpan.FromSeconds(10), policy));
        Assert.Equal(46, await NativeModuleCompiler.RunGraphJitAsync(root,
            [writableReplacement, writableMiddle], apphost, TimeSpan.FromSeconds(30), policy));
        Assert.Equal(persistedSingle, single);
        Assert.Equal(persistedMiddle, middle);
        Assert.Equal(persistedRoot, root);

        var incompatible = NativeModuleCompiler.CreateProducer(
            Compile("public int64 Leaf() { return 44; }", policy), ["Leaf"], "NodeB", policy);
        _ = Assert.Throws<ModuleContractException>(() => NativeModuleCompiler.Validate(single, incompatible));
        _ = Assert.Throws<ModuleContractException>(() => NativeModuleCompiler.ValidateGraph(root, [middle, incompatible]));

        var unknown = (byte[])writableReplacement.Clone();
        var at = unknown.AsSpan().IndexOf("native-17"u8);
        Assert.True(at >= 0);
        "native-99"u8.CopyTo(unknown.AsSpan(at, 9));
        SHA256.HashData(unknown.AsSpan(0, unknown.Length - 32)).CopyTo(unknown, unknown.Length - 32);
        _ = Assert.Throws<ModuleFormatException>(() => NativeModuleCompiler.Inspect(unknown));
        _ = Assert.Throws<ModuleFormatException>(() => NativeModuleCompiler.ValidateGraph(root, [middle, unknown]));

        var unknownGraph = (byte[])writableMiddle.Clone();
        var graphProfile = unknownGraph.AsSpan().IndexOf("native-17"u8);
        Assert.True(graphProfile >= 0);
        "native-99"u8.CopyTo(unknownGraph.AsSpan(graphProfile, 9));
        SHA256.HashData(unknownGraph.AsSpan(0, unknownGraph.Length - 32))
            .CopyTo(unknownGraph, unknownGraph.Length - 32);
        _ = Assert.Throws<ModuleFormatException>(() => NativeModuleCompiler.InspectGraphArtifact(unknownGraph));
        _ = Assert.Throws<ModuleFormatException>(() => NativeModuleCompiler.ValidateGraph(root, [original, unknownGraph]));

        var downgraded = (byte[])writableReplacement.Clone();
        var packageProfile = downgraded.AsSpan().IndexOf("native-17"u8);
        Assert.True(packageProfile >= 0);
        var semanticProfile = downgraded.AsSpan(packageProfile + 9).IndexOf("native-17"u8);
        Assert.True(semanticProfile >= 0);
        "native-16"u8.CopyTo(downgraded.AsSpan(packageProfile, 9));
        "native-16"u8.CopyTo(downgraded.AsSpan(packageProfile + 9 + semanticProfile, 9));
        SHA256.HashData(downgraded.AsSpan(0, downgraded.Length - 32))
            .CopyTo(downgraded, downgraded.Length - 32);
        _ = Assert.Throws<ModuleFormatException>(() => NativeModuleCompiler.Inspect(downgraded));
        _ = Assert.Throws<ModuleFormatException>(() => NativeModuleCompiler.ValidateGraph(root, [middle, downgraded]));

        var escaped = (byte[])writableReplacement.Clone();
        var signature = escaped.AsSpan().IndexOf("Leaf|2|"u8);
        Assert.True(signature >= 0);
        "Leaf|9|"u8.CopyTo(escaped.AsSpan(signature, 7));
        SHA256.HashData(escaped.AsSpan(0, escaped.Length - 32)).CopyTo(escaped, escaped.Length - 32);
        _ = Assert.Throws<ModuleFormatException>(() => NativeModuleCompiler.Inspect(escaped));
        _ = Assert.Throws<ModuleFormatException>(() => NativeModuleCompiler.ValidateGraph(root, [middle, escaped]));
    }

    [Theory]
    [InlineData("public int32 Leak(MutableInt32BufferView view) { return view.Length; }")]
    [InlineData("public MutableInt32BufferView Leak() { return 0; }")]
    [InlineData("public struct Leaky { public MutableInt32BufferView Value; } public int32 Leaf() { return 0; }")]
    public void MutableViewCannotAppearInPublicContracts(string source)
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source),
            BoundRuntimePolicy.FailFast);
        Assert.True(compilation.HasErrors);
    }
}
