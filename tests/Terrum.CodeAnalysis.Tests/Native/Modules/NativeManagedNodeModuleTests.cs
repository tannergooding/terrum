// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests.Native.Modules;

public sealed class NativeManagedNodeModuleTests
{
    private const string Original = "public int32 Leaf() { return 40; }";
    private const string Middle = "public int32 Middle() { return Leaf() + 1; }";
    private const string Root = "int32 Main() { return Middle() + 1; }";
    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public async Task PrivateNodeLeafReplacesAnUnchangedRootAndIntermediateAsync(NativeRuntimePolicy policy)
    {
        var original = NativeModuleCompiler.CreateProducer(Compile(Original, policy), ["Leaf"], "NodeB", policy);
        var middle = NativeModuleCompiler.CreateGraphProducer(Compile(Middle, policy,
            NativeModuleCompiler.CreateGraphReference([original])), ["Middle"], "NodeA", [original], policy);
        var root = NativeModuleCompiler.CreateGraphConsumer(Compile(Root, policy,
            NativeModuleCompiler.CreateGraphReference([middle]), library: false), [middle], policy);
        var rootHash = SHA256.HashData(root);
        var middleHash = SHA256.HashData(middle);
        var replacement = NativeModuleCompiler.CreateProducer(Compile(Sample(), policy),
            ["Leaf"], "NodeB", policy);
        Assert.That(NativeModuleCompiler.Inspect(replacement).NativeProfileVersion, Is.EqualTo("native-18"));
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
        Assert.That(SHA256.HashData(root), Is.EqualTo(rootHash));
        Assert.That(SHA256.HashData(middle), Is.EqualTo(middleHash));
    }
    [TestCase("public ManagedNode Leak() { require ManagedNode.TryCreate(42, out var node); return node; }","Leak")]
    [TestCase("public bool Leak(out write ManagedNode node) { return ManagedNode.TryCreate(42, out node); }","Leak")]
    public void ManagedHandlesCannotCrossNativePackageExports(string source, string export)
    {
        var program = Compile(source, NativeRuntimePolicy.FailFast);
        _ = Assert.Throws<ModuleContractException>(() =>
            NativeModuleCompiler.CreateProducer(program, [export], "NodeB", NativeRuntimePolicy.FailFast))!;
    }

    [Test]
    public void PrivateSourceClassSelectsItsOwnNativeProfile()
    {
        var program = Compile("""
            class Leaf {
                public int32 Value;
                public Leaf(int32 value) { Value = value; }
            }
            public int32 Proof() {
                require Leaf.TryCreate(42, out var leaf);
                return leaf.Value;
            }
            """, NativeRuntimePolicy.FailFast);
        var package = NativeModuleCompiler.CreateProducer(program, ["Proof"], "NodeB",
            NativeRuntimePolicy.FailFast);
        Assert.That(NativeModuleCompiler.Inspect(package).NativeProfileVersion, Is.EqualTo("native-19"));
    }

    [Test]
    public void NodeProfileAndLexicalRootMetadataAreCheckedOnReload()
    {
        var program = Compile(Sample(), NativeRuntimePolicy.FailFast);
        var contract = ModuleCompiler.CreateProducer(program, ["Leaf"]);
        Assert.That(BinaryPrimitives.ReadInt32LittleEndian(contract.AsSpan(4, 4)), Is.EqualTo(76));
        Assert.That(ModuleCompiler.GetNativeProfileVersion(contract, false), Is.EqualTo("native-18"));
        var profile = (byte[])contract.Clone();
        var label = Encoding.UTF8.GetBytes("native-18");
        var profileOffset = Find(profile, label);
        profile[profileOffset + label.Length - 1] = (byte)'7';
        _ = Assert.Throws<ModuleFormatException>(() => ModuleCompiler.CreateReference(profile))!;
        BinaryPrimitives.WriteInt32LittleEndian(profile.AsSpan(4), 17);
        _ = Assert.Throws<ModuleFormatException>(() => ModuleCompiler.CreateReference(profile))!;

        var make = global::System.Linq.Enumerable.Single(program.Functions, function => function.Name == "MakeValue");
        Assert.That(make.Body.IsManagedLexicalScope, Is.True);
        Assert.That(make.Body.RetiredManagedLocals.Length, Is.EqualTo(2));
        var marker = new byte[14 + (4 * make.Body.RetiredManagedLocals.Length)];
        marker[0] = 1;
        BinaryPrimitives.WriteInt32LittleEndian(marker.AsSpan(1), make.Body.Span.Start);
        BinaryPrimitives.WriteInt32LittleEndian(marker.AsSpan(5), make.Body.Span.Length);
        marker[9] = 1;
        BinaryPrimitives.WriteInt32LittleEndian(marker.AsSpan(10), make.Body.RetiredManagedLocals.Length);
        for (var i = 0; i < make.Body.RetiredManagedLocals.Length; i++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(marker.AsSpan(14 + (4 * i)),
                make.Body.RetiredManagedLocals[i]);
        }
        var scopeOffset = Find(contract, marker) + 9;
        var noncanonical = (byte[])contract.Clone();
        noncanonical[scopeOffset] = 2;
        _ = Assert.Throws<ModuleFormatException>(() => ModuleCompiler.CreateReference(noncanonical))!;
        var retired = (byte[])contract.Clone();
        BinaryPrimitives.WriteInt32LittleEndian(retired.AsSpan(scopeOffset + 1), 257);
        _ = Assert.Throws<ModuleFormatException>(() => ModuleCompiler.CreateReference(retired))!;
        var wrongSlot = (byte[])contract.Clone();
        BinaryPrimitives.WriteInt32LittleEndian(wrongSlot.AsSpan(scopeOffset + 5), 250);
        _ = Assert.Throws<ModuleFormatException>(() => ModuleCompiler.CreateReference(wrongSlot))!;
    }

    [Test]
    public void NativePackageRejectsProfileDowngradeAndUnlistedHeapImport()
    {
        var package = NativeModuleCompiler.CreateProducer(Compile(Sample(), NativeRuntimePolicy.FailFast),
            ["Leaf"], "NodeB", NativeRuntimePolicy.FailFast);
        var downgraded = (byte[])package.Clone();
        var profileOffset = 18 + downgraded[7];
        Assert.That(Encoding.ASCII.GetString(downgraded, profileOffset, downgraded[8]), Is.EqualTo("native-18"));
        downgraded[profileOffset + downgraded[8] - 1] = (byte)'7';
        Rehash(downgraded);
        _ = Assert.Throws<ModuleFormatException>(() => NativeModuleCompiler.Inspect(downgraded))!;

        var import = (byte[])package.Clone();
        var imageOffset = 18 + import[7] + import[8] + 2
            + BinaryPrimitives.ReadInt32LittleEndian(import.AsSpan(10, 4));
        var heapName = import.AsSpan(imageOffset,
            BinaryPrimitives.ReadInt32LittleEndian(import.AsSpan(14, 4))).IndexOf("HeapAlloc\0"u8);
        Assert.That(heapName >= 0, Is.True);
        import[imageOffset + heapName + 4] = (byte)'X';
        Rehash(import);
        _ = Assert.Throws<ModuleFormatException>(() => NativeModuleCompiler.Inspect(import))!;
    }

    private static void Rehash(byte[] package) =>
        SHA256.HashData(package.AsSpan(0, package.Length - 32)).CopyTo(package, package.Length - 32);

    private static int Find(byte[] bytes, byte[] pattern)
    {
        var index = bytes.AsSpan().IndexOf(pattern);
        Assert.That(index >= 0, Is.True);
        Assert.That(bytes.AsSpan(index + 1).IndexOf(pattern), Is.EqualTo(-1));
        return index;
    }

    private static BoundProgram Compile(string source, NativeRuntimePolicy policy,
        CompilationReference? reference = null, bool library = true)
    {
        var syntax = SyntaxTree.Parse(source);
        var boundPolicy = (BoundRuntimePolicy)policy;
        var compilation = library
            ? reference is null ? Compilation.CreateLibrary(syntax, boundPolicy)
                : Compilation.CreateLibrary(syntax, reference, boundPolicy)
            : Compilation.Create(syntax, ((global::System.Func<CompilationReference>)(() => { var __nunitActual0 = reference; Assert.That(__nunitActual0, Is.TypeOf<CompilationReference>()); return (CompilationReference)__nunitActual0!; }))(), boundPolicy);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        return ((global::System.Func<BoundProgram>)(() => { var __nunitActual1 = compilation.Program; Assert.That(__nunitActual1, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual1!; }))();
    }

    private static string Sample()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        var repository = Path.GetFullPath(Path.Combine(directory.FullName,
            "..", "..", "..", "..", "..", ".."));
        return File.ReadAllText(Path.Combine(repository,
            "samples", "DependencyGraphServicing", "managed-node-leaf.terrum"));
    }
}
