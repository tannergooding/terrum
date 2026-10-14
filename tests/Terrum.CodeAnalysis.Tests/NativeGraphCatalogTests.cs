// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests;

public sealed class NativeGraphCatalogTests
{
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

    private static byte[] Producer(string name, int value, NativeRuntimePolicy policy, bool incompatible = false,
        string? identity = null) =>
        NativeModuleCompiler.CreateProducer(Compile($"public {(incompatible ? "int64" : "int32")} {name}() "
            + $"{{ return {value}; }}", policy), [name], identity ?? name, policy);

    private static (byte[] Root, byte[] Middle, byte[] Original, byte[] Replacement, byte[] Incompatible,
        byte[] Unrelated) Graph(NativeRuntimePolicy policy)
    {
        var original = Producer("Leaf", 40, policy, identity: "NodeB");
        var replacement = Producer("Leaf", 42, policy, identity: "NodeB");
        var incompatible = Producer("Leaf", 42, policy, incompatible: true, identity: "NodeB");
        var unrelated = Producer("Unrelated", 7, policy);
        var middle = NativeModuleCompiler.CreateGraphProducer(
            Compile("public int32 Middle() { return Leaf() + 1; }", policy,
                NativeModuleCompiler.CreateGraphReference([original])),
            ["Middle"], "NodeA", [original], policy);
        var root = NativeModuleCompiler.CreateGraphConsumer(
            Compile("int32 Main() { return Middle() + 1; }", policy,
                NativeModuleCompiler.CreateGraphReference([middle]), library: false),
            [middle], policy);
        return (root, middle, original, replacement, incompatible, unrelated);
    }
    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public async Task UniqueCompatibleCatalogServicesUnchangedRootAndIntermediateAsync(NativeRuntimePolicy policy)
    {
        var (root, middle, _, replacement, incompatible, unrelated) = Graph(policy);
        var persistedRoot = (byte[])root.Clone();
        var persistedMiddle = (byte[])middle.Clone();
        var catalog = new[] { unrelated, incompatible, replacement, middle, (byte[])replacement.Clone() };
        var selection = NativeModuleCompiler.ResolveGraphCatalog(root, catalog);
        int[] expectedIndexes = [2, 3];
        string[] expectedIdentities = ["NodeA", "NodeB"];
        var selectedRoot = selection.Consumer.ToArray();
        var selectedProviders = selection.Producers.Select(static bytes => bytes.ToArray()).ToArray();
        Assert.That(selection.CandidateIndexes.Order(), Is.EqualTo(expectedIndexes));
        Assert.That(selectedProviders
            .Select(item => NativeModuleCompiler.InspectGraphArtifact(item).Identity
                ?? throw new InvalidOperationException("Selected graph producer has no identity."))
            .Order(StringComparer.Ordinal), Is.EqualTo(expectedIdentities));
        root[0] ^= 1;
        replacement[0] ^= 1;
        middle[0] ^= 1;
        Assert.That(selectedRoot, Is.EqualTo(persistedRoot));
        NativeModuleCompiler.ValidateGraph(selectedRoot, selectedProviders);
        Assert.That(await NativeModuleCompiler.RunGraphAsync(selectedRoot, selectedProviders,
            TimeSpan.FromSeconds(10), policy), Is.EqualTo(44));
        var frameworkDirectory = new DirectoryInfo(AppContext.BaseDirectory);
        var configuration = frameworkDirectory.Parent?.Name
            ?? throw new InvalidOperationException("Missing test configuration directory.");
        var repository = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", ".."));
        var apphost = Path.Combine(repository, "artifacts", "bin", "sources", "terrumc",
            configuration, frameworkDirectory.Name, "terrumc.exe");
        Assert.That(await NativeModuleCompiler.RunGraphJitAsync(selectedRoot, selectedProviders, apphost,
            TimeSpan.FromSeconds(30), policy), Is.EqualTo(44));
        selectedRoot[0] ^= 1;
        selectedProviders[0][0] ^= 1;
        Assert.That(selection.Consumer.ToArray(), Is.EqualTo(persistedRoot));
        Assert.That(selection.Producers[0].ToArray(), Is.EqualTo(persistedMiddle));
    }

    [Test]
    public void CatalogRejectsMissingIncompatibleAmbiguousAndMalformedChoices()
    {
        var (root, middle, original, replacement, incompatible, unrelated) = Graph(NativeRuntimePolicy.FailFast);
        Assert.That(Assert.Throws<ModuleContractException>(() =>
            NativeModuleCompiler.ResolveGraphCatalog(root, [middle]))!.Message, Does.Contain("Missing"));
        Assert.That(Assert.Throws<ModuleContractException>(() =>
            NativeModuleCompiler.ResolveGraphCatalog(root, [middle, incompatible]))!.Message, Does.Contain("no compatible"));
        Assert.That(Assert.Throws<ModuleContractException>(() =>
            NativeModuleCompiler.ResolveGraphCatalog(root, [middle, original, replacement]))!.Message, Does.Contain("multiple compatible"));
        var malformed = (byte[])unrelated.Clone();
        malformed[^1] ^= 1;
        _ = Assert.Throws<ModuleFormatException>(() =>
            NativeModuleCompiler.ResolveGraphCatalog(root, [middle, replacement, malformed]))!;
        var caseVariant = NativeModuleCompiler.CreateProducer(
            Compile("public int32 Leaf() { return 42; }", NativeRuntimePolicy.FailFast), ["Leaf"],
            "nodeb", NativeRuntimePolicy.FailFast);
        Assert.That(Assert.Throws<ModuleContractException>(() =>
            NativeModuleCompiler.ResolveGraphCatalog(root, [middle, caseVariant]))!.Message, Does.Contain("no compatible"));
        Assert.That(Assert.Throws<ModuleContractException>(() =>
            NativeModuleCompiler.ResolveGraphCatalog(root, [middle, replacement, caseVariant]))!.Message, Does.Contain("Case-ambiguous"));
        var wrongPolicy = Producer("Leaf", 42, NativeRuntimePolicy.CheckedExceptions, identity: "NodeB");
        Assert.That(Assert.Throws<ModuleContractException>(() =>
            NativeModuleCompiler.ResolveGraphCatalog(root, [middle, wrongPolicy]))!.Message, Does.Contain("no compatible"));
    }

    [Test]
    public void CatalogRejectsCyclicSelectedClosure()
    {
        var (root, middle, _, _, _, _) = Graph(NativeRuntimePolicy.FailFast);
        var cycle = (byte[])middle.Clone();
        Assert.That(cycle.AsSpan(19, 5).SequenceEqual("NodeA"u8), Is.True);
        "NodeB"u8.CopyTo(cycle.AsSpan(19, 5));
        for (var index = 24; index <= cycle.Length - 32 - 5; index++)
        {
            if (cycle.AsSpan(index, 5).SequenceEqual("NodeB"u8))
            {
                "NodeA"u8.CopyTo(cycle.AsSpan(index, 5));
            }
        }
        SHA256.HashData(cycle.AsSpan(0, cycle.Length - 32)).CopyTo(cycle, cycle.Length - 32);
        Assert.That(NativeModuleCompiler.InspectGraphArtifact(cycle).Identity, Is.EqualTo("NodeB"));
        Assert.That(Assert.Throws<ModuleContractException>(() =>
            NativeModuleCompiler.ResolveGraphCatalog(root, [middle, cycle]))!.Message, Does.Contain("cycle"));
    }

    [Test]
    public void CatalogCandidateAndAggregateByteBoundsAreExact()
    {
        var (root, middle, _, replacement, _, unrelated) = Graph(NativeRuntimePolicy.FailFast);
        var catalog = new byte[32][];
        catalog[0] = middle;
        catalog[1] = replacement;
        for (var index = 2; index < catalog.Length; index++)
        {
            catalog[index] = unrelated;
        }
        Assert.That(NativeModuleCompiler.ResolveGraphCatalog(root, catalog).Producers.Length, Is.EqualTo(2));
        Assert.That(Assert.Throws<ModuleContractException>(() =>
            NativeModuleCompiler.ResolveGraphCatalog(root, [.. catalog, unrelated]))!.Message, Does.Contain("1 to 32"));

        const int MaximumPackageBytes = 16 * 1024 * 1024;
        var large = new byte[MaximumPackageBytes];
        var final = new byte[MaximumPackageBytes - root.Length];
        var atLimit = new[] { large, large, large, final };
        _ = Assert.Throws<ModuleFormatException>(() =>
            NativeModuleCompiler.ResolveGraphCatalog(root, atLimit))!;
        atLimit[3] = new byte[final.Length + 1];
        Assert.That(Assert.Throws<ModuleContractException>(() =>
            NativeModuleCompiler.ResolveGraphCatalog(root, atLimit))!.Message, Does.Contain("64 MiB"));
    }

    [Test]
    public void CatalogCannotReturnFirstSolutionBeforeAssignmentBudgetProvesUniqueness()
    {
        const int Pairs = 12;
        var originals = new byte[Pairs][];
        var catalog = new List<byte[]>(Pairs * 2);
        var calls = new StringBuilder("int32 Main() { return ");
        for (var index = 0; index < Pairs; index++)
        {
            var name = $"Part{index:D2}";
            originals[index] = Producer(name, 1, NativeRuntimePolicy.FailFast);
            catalog.Add(originals[index]);
            catalog.Add(Producer(name, 1, NativeRuntimePolicy.FailFast, incompatible: true));
            if (index != 0)
            {
                _ = calls.Append(" + ");
            }
            _ = calls.Append(name).Append("()");
        }
        _ = calls.Append("; }");
        var root = NativeModuleCompiler.CreateGraphConsumer(
            Compile(calls.ToString(), NativeRuntimePolicy.FailFast,
                NativeModuleCompiler.CreateGraphReference(originals), library: false),
            originals, NativeRuntimePolicy.FailFast);
        Assert.That(Assert.Throws<ModuleContractException>(() =>
            NativeModuleCompiler.ResolveGraphCatalog(root, catalog))!.Message, Does.Contain("4096 candidate assignments"));
    }
}
