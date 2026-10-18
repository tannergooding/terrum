// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests.Native.Modules;

public sealed class NativeScopedViewModuleTests
{
    private static string Fixture(string name)
    {
        var repository = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", ".."));
        return File.ReadAllText(Path.Combine(repository, "samples", "DependencyGraphServicing", name));
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
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        return ((global::System.Func<BoundProgram>)(() => { var __nunitActual0 = compilation.Program; Assert.That(__nunitActual0, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual0!; }))();
    }
    [TestCase(NativeRuntimePolicy.FailFast,"native-10")]
    [TestCase(NativeRuntimePolicy.CheckedExceptions,"native-11")]
    public async Task InternalViewReplacementServicesUnchangedSingleAndGraphConsumersAsync(
        NativeRuntimePolicy policy, string originalProfile)
    {
        var original = NativeModuleCompiler.CreateProducer(
            Compile(Fixture("scoped-view-leaf.terrum"), policy), ["Leaf"], "NodeB", policy);
        var replacement = NativeModuleCompiler.CreateProducer(
            Compile(Fixture("scoped-view-leaf-replacement.terrum"), policy), ["Leaf"], "NodeB", policy);
        Assert.That(NativeModuleCompiler.Inspect(original).NativeProfileVersion, Is.EqualTo(originalProfile));
        Assert.That(NativeModuleCompiler.Inspect(replacement).NativeProfileVersion, Is.EqualTo("native-16"));

        var single = NativeModuleCompiler.CreateConsumer(
            Compile("int32 Main() { return Leaf() + 2; }", policy,
                NativeModuleCompiler.CreateReference(original), library: false), original, policy);
        var persistedSingle = (byte[])single.Clone();
        NativeModuleCompiler.Validate(single, original);
        NativeModuleCompiler.Validate(single, replacement);
        Assert.That(await NativeModuleCompiler.RunAsync(single, original, TimeSpan.FromSeconds(10)), Is.EqualTo(42));
        Assert.That(await NativeModuleCompiler.RunAsync(single, replacement, TimeSpan.FromSeconds(10)), Is.EqualTo(44));
        Assert.That(single, Is.EqualTo(persistedSingle));

        var middle = NativeModuleCompiler.CreateGraphProducer(
            Compile(Fixture("middle.terrum"), policy, NativeModuleCompiler.CreateGraphReference([original])),
            ["Middle"], "NodeA", [original], policy);
        var root = NativeModuleCompiler.CreateGraphConsumer(
            Compile(Fixture("consumer.terrum"), policy, NativeModuleCompiler.CreateGraphReference([middle]),
                library: false), [middle], policy);
        var persistedMiddle = (byte[])middle.Clone();
        var persistedRoot = (byte[])root.Clone();
        Assert.That(NativeModuleCompiler.InspectGraphArtifact(middle).NativeProfileVersion, Is.EqualTo(originalProfile));
        Assert.That(NativeModuleCompiler.InspectGraphArtifact(root).NativeProfileVersion, Is.EqualTo(originalProfile));
        NativeModuleCompiler.ValidateGraph(root, [middle, original]);
        NativeModuleCompiler.ValidateGraph(root, [replacement, middle]);
        Assert.That(await NativeModuleCompiler.RunGraphAsync(root, [original, middle],
            TimeSpan.FromSeconds(10), policy), Is.EqualTo(42));
        Assert.That(await NativeModuleCompiler.RunGraphAsync(root, [middle, replacement],
            TimeSpan.FromSeconds(10), policy), Is.EqualTo(44));
        var frameworkDirectory = new DirectoryInfo(AppContext.BaseDirectory);
        var configuration = frameworkDirectory.Parent?.Name
            ?? throw new InvalidOperationException("Missing test configuration directory.");
        var repository = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", ".."));
        var apphost = Path.Combine(repository, "artifacts", "bin", "sources", "terrumc",
            configuration, frameworkDirectory.Name, "terrumc.exe");
        Assert.That(await NativeModuleCompiler.RunGraphJitAsync(root, [middle, original], apphost,
            TimeSpan.FromSeconds(30), policy), Is.EqualTo(42));
        Assert.That(await NativeModuleCompiler.RunGraphJitAsync(root, [replacement, middle], apphost,
            TimeSpan.FromSeconds(30), policy), Is.EqualTo(44));
        Assert.That(root, Is.EqualTo(persistedRoot));
        Assert.That(middle, Is.EqualTo(persistedMiddle));

        var incompatible = NativeModuleCompiler.CreateProducer(
            Compile("public int64 Leaf() { return 42; }", policy), ["Leaf"], "NodeB", policy);
        _ = Assert.Throws<ModuleContractException>(() => NativeModuleCompiler.Validate(single, incompatible))!;
        _ = Assert.Throws<ModuleContractException>(() => NativeModuleCompiler.ValidateGraph(root, [middle, incompatible]))!;

        var unknown = (byte[])replacement.Clone();
        var at = unknown.AsSpan().IndexOf("native-16"u8);
        Assert.That(at >= 0, Is.True);
        "native-99"u8.CopyTo(unknown.AsSpan(at, 9));
        SHA256.HashData(unknown.AsSpan(0, unknown.Length - 32)).CopyTo(unknown, unknown.Length - 32);
        _ = Assert.Throws<ModuleFormatException>(() => NativeModuleCompiler.Inspect(unknown))!;
        _ = Assert.Throws<ModuleFormatException>(() => NativeModuleCompiler.ValidateGraph(root, [middle, unknown]))!;

        var escapedContract = (byte[])replacement.Clone();
        var signature = escapedContract.AsSpan().IndexOf("Leaf|2|"u8);
        Assert.That(signature >= 0, Is.True);
        "Leaf|8|"u8.CopyTo(escapedContract.AsSpan(signature, 7));
        SHA256.HashData(escapedContract.AsSpan(0, escapedContract.Length - 32))
            .CopyTo(escapedContract, escapedContract.Length - 32);
        _ = Assert.Throws<ModuleFormatException>(() => NativeModuleCompiler.Inspect(escapedContract))!;
        _ = Assert.Throws<ModuleFormatException>(() => NativeModuleCompiler.ValidateGraph(root, [middle, escapedContract]))!;

        var escaping = Compilation.CreateLibrary(SyntaxTree.Parse(
            "public int32 Leak(ReadonlyInt32BufferView view) { return view.Length; }"),
            (BoundRuntimePolicy)policy);
        Assert.That(escaping.HasErrors, Is.True);
    }
    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public async Task WritableViewReplacementServicesPersistedNative15And16ConsumersAsync(NativeRuntimePolicy policy)
    {
        var original = NativeModuleCompiler.CreateProducer(
            Compile(Fixture("writable-view-leaf-original.terrum"), policy), ["Leaf"], "NodeB", policy);
        var readonlyReplacement = NativeModuleCompiler.CreateProducer(
            Compile(Fixture("scoped-view-leaf-replacement.terrum"), policy), ["Leaf"], "NodeB", policy);
        var writableReplacement = NativeModuleCompiler.CreateProducer(
            Compile(Fixture("writable-view-leaf.terrum"), policy), ["Leaf"], "NodeB", policy);
        Assert.That(NativeModuleCompiler.Inspect(original).NativeProfileVersion, Is.EqualTo("native-15"));
        Assert.That(NativeModuleCompiler.Inspect(readonlyReplacement).NativeProfileVersion, Is.EqualTo("native-16"));
        Assert.That(NativeModuleCompiler.Inspect(writableReplacement).NativeProfileVersion, Is.EqualTo("native-17"));

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
        Assert.That(NativeModuleCompiler.InspectGraphArtifact(middle).NativeProfileVersion, Is.EqualTo(originalGraphProfile));
        Assert.That(NativeModuleCompiler.InspectGraphArtifact(root).NativeProfileVersion, Is.EqualTo(originalGraphProfile));

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
            Assert.That(await NativeModuleCompiler.RunAsync(single, provider, TimeSpan.FromSeconds(10)), Is.EqualTo(expected));
            NativeModuleCompiler.ValidateGraph(root, [middle, provider]);
            Assert.That(await NativeModuleCompiler.RunGraphAsync(root, [provider, middle],
                TimeSpan.FromSeconds(10), policy), Is.EqualTo(expected));
            Assert.That(await NativeModuleCompiler.RunGraphJitAsync(root, [middle, provider], apphost,
                TimeSpan.FromSeconds(30), policy), Is.EqualTo(expected));
        }

        var writableMiddle = NativeModuleCompiler.CreateGraphProducer(
            Compile(Fixture("writable-view-middle.terrum"), policy,
                NativeModuleCompiler.CreateGraphReference([original])),
            ["Middle"], "NodeA", [original], policy);
        Assert.That(NativeModuleCompiler.InspectGraphArtifact(writableMiddle).NativeProfileVersion, Is.EqualTo("native-17"));
        NativeModuleCompiler.ValidateGraph(root, [original, writableMiddle]);
        Assert.That(await NativeModuleCompiler.RunGraphAsync(root, [writableMiddle, original],
            TimeSpan.FromSeconds(10), policy), Is.EqualTo(42));
        NativeModuleCompiler.ValidateGraph(root, [writableReplacement, writableMiddle]);
        Assert.That(await NativeModuleCompiler.RunGraphAsync(root, [writableMiddle, writableReplacement],
            TimeSpan.FromSeconds(10), policy), Is.EqualTo(46));
        Assert.That(await NativeModuleCompiler.RunGraphJitAsync(root,
            [writableReplacement, writableMiddle], apphost, TimeSpan.FromSeconds(30), policy), Is.EqualTo(46));
        Assert.That(single, Is.EqualTo(persistedSingle));
        Assert.That(middle, Is.EqualTo(persistedMiddle));
        Assert.That(root, Is.EqualTo(persistedRoot));

        var incompatible = NativeModuleCompiler.CreateProducer(
            Compile("public int64 Leaf() { return 44; }", policy), ["Leaf"], "NodeB", policy);
        _ = Assert.Throws<ModuleContractException>(() => NativeModuleCompiler.Validate(single, incompatible))!;
        _ = Assert.Throws<ModuleContractException>(() => NativeModuleCompiler.ValidateGraph(root, [middle, incompatible]))!;

        var unknown = (byte[])writableReplacement.Clone();
        var at = unknown.AsSpan().IndexOf("native-17"u8);
        Assert.That(at >= 0, Is.True);
        "native-99"u8.CopyTo(unknown.AsSpan(at, 9));
        SHA256.HashData(unknown.AsSpan(0, unknown.Length - 32)).CopyTo(unknown, unknown.Length - 32);
        _ = Assert.Throws<ModuleFormatException>(() => NativeModuleCompiler.Inspect(unknown))!;
        _ = Assert.Throws<ModuleFormatException>(() => NativeModuleCompiler.ValidateGraph(root, [middle, unknown]))!;

        var unknownGraph = (byte[])writableMiddle.Clone();
        var graphProfile = unknownGraph.AsSpan().IndexOf("native-17"u8);
        Assert.That(graphProfile >= 0, Is.True);
        "native-99"u8.CopyTo(unknownGraph.AsSpan(graphProfile, 9));
        SHA256.HashData(unknownGraph.AsSpan(0, unknownGraph.Length - 32))
            .CopyTo(unknownGraph, unknownGraph.Length - 32);
        _ = Assert.Throws<ModuleFormatException>(() => NativeModuleCompiler.InspectGraphArtifact(unknownGraph))!;
        _ = Assert.Throws<ModuleFormatException>(() => NativeModuleCompiler.ValidateGraph(root, [original, unknownGraph]))!;

        var downgraded = (byte[])writableReplacement.Clone();
        var packageProfile = downgraded.AsSpan().IndexOf("native-17"u8);
        Assert.That(packageProfile >= 0, Is.True);
        var semanticProfile = downgraded.AsSpan(packageProfile + 9).IndexOf("native-17"u8);
        Assert.That(semanticProfile >= 0, Is.True);
        "native-16"u8.CopyTo(downgraded.AsSpan(packageProfile, 9));
        "native-16"u8.CopyTo(downgraded.AsSpan(packageProfile + 9 + semanticProfile, 9));
        SHA256.HashData(downgraded.AsSpan(0, downgraded.Length - 32))
            .CopyTo(downgraded, downgraded.Length - 32);
        _ = Assert.Throws<ModuleFormatException>(() => NativeModuleCompiler.Inspect(downgraded))!;
        _ = Assert.Throws<ModuleFormatException>(() => NativeModuleCompiler.ValidateGraph(root, [middle, downgraded]))!;

        var escaped = (byte[])writableReplacement.Clone();
        var signature = escaped.AsSpan().IndexOf("Leaf|2|"u8);
        Assert.That(signature >= 0, Is.True);
        "Leaf|9|"u8.CopyTo(escaped.AsSpan(signature, 7));
        SHA256.HashData(escaped.AsSpan(0, escaped.Length - 32)).CopyTo(escaped, escaped.Length - 32);
        _ = Assert.Throws<ModuleFormatException>(() => NativeModuleCompiler.Inspect(escaped))!;
        _ = Assert.Throws<ModuleFormatException>(() => NativeModuleCompiler.ValidateGraph(root, [middle, escaped]))!;
    }
    [TestCase("public int32 Leak(MutableInt32BufferView view) { return view.Length; }")]
    [TestCase("public MutableInt32BufferView Leak() { return 0; }")]
    [TestCase("public struct Leaky { public MutableInt32BufferView Value; } public int32 Leaf() { return 0; }")]
    public void MutableViewCannotAppearInPublicContracts(string source)
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source),
            BoundRuntimePolicy.FailFast);
        Assert.That(compilation.HasErrors, Is.True);
    }
}
