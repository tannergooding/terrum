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
}
