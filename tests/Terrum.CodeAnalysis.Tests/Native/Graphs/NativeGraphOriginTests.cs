// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests.Native.Graphs;

public sealed class NativeGraphOriginTests
{
    private const string LeafSource = """
        [Blittable] [Layout(LayoutKind.Sequential)] public unique struct Ticket {
            public int32 Value;
            [NoThrow] public Ticket(int32 value) { Value = value; }
            [NoThrow] [Cleanup] public void Close() {}
        }
        public Ticket Make() { return new Ticket(20); }
        """;

    private static BoundProgram Compile(string source, NativeRuntimePolicy policy,
        CompilationReference? reference = null, bool library = true)
    {
        var boundPolicy = (BoundRuntimePolicy)policy;
        var syntax = SyntaxTree.Parse(source);
        var compilation = library
            ? reference is null ? Compilation.CreateLibrary(syntax, boundPolicy)
                : Compilation.CreateLibrary(syntax, reference, boundPolicy)
            : Compilation.Create(syntax, ((global::System.Func<CompilationReference>)(() => { var __nunitActual0 = reference; Assert.That(__nunitActual0, Is.TypeOf<CompilationReference>()); return (CompilationReference)__nunitActual0!; }))(), boundPolicy);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        return ((global::System.Func<BoundProgram>)(() => { var __nunitActual1 = compilation.Program; Assert.That(__nunitActual1, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual1!; }))();
    }

    private static byte[] Leaf(string source, NativeRuntimePolicy policy) =>
        NativeModuleCompiler.CreateProducer(Compile(source, policy), ["Make", "Ticket.Close"],
            "Leaf", policy);

    private static byte[] CopyLeaf(string type, string factory, string identity, int value,
        NativeRuntimePolicy policy)
    {
        var source = $$"""
            [Blittable] [Layout(LayoutKind.Sequential)] public struct {{type}} {
                public int32 Value;
                public {{type}}(int32 value) { Value = value; }
            }
            public {{type}} {{factory}}() { return new {{type}}({{value}}); }
            """;
        return NativeModuleCompiler.CreateProducer(Compile(source, policy), [factory], identity, policy);
    }
    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public async Task UniqueReturnAndOutputUseOriginCleanupWithUnchangedImagesAsync(NativeRuntimePolicy policy)
    {
        var leaf = Leaf(LeafSource, policy);
        var middle = NativeModuleCompiler.CreateGraphProducer(Compile("""
            public Ticket Forward() { return Make(); }
            public bool TryForward(out Ticket ticket) { ticket = Make(); return true; }
            """, policy, NativeModuleCompiler.CreateGraphReference([leaf])),
            ["Forward", "TryForward"], "Middle", [leaf], policy);
        _ = Assert.Throws<ModuleContractException>(() => NativeModuleCompiler.CreateGraphReference([middle]))!;
        var root = NativeModuleCompiler.CreateGraphConsumer(Compile("""
            int32 Main() {
                Ticket first = Forward();
                require TryForward(out var second);
                return first.Value + second.Value + 2;
            }
            """, policy, NativeModuleCompiler.CreateGraphReference([middle, leaf]), library: false),
            [middle, leaf], policy);
        var (declarations, imports) = Mapping(middle, "Leaf");
        Assert.That(declarations, Does.Contain("Ticket.Close"));
        Assert.That(imports, Does.Contain("Ticket.Close"));
        Assert.That(Mapping(root, "Leaf").Imports, Does.Contain("Ticket.Close"));
        var originalRoot = root.ToArray();
        var originalMiddle = middle.ToArray();
        NativeModuleCompiler.ValidateGraph(root, [leaf, middle]);
        Assert.That(await NativeModuleCompiler.RunGraphAsync(root, [middle, leaf],
            TimeSpan.FromSeconds(10), policy), Is.EqualTo(42));
        var replacement = Leaf(LeafSource.Replace("new Ticket(20)", "new Ticket(21)",
            StringComparison.Ordinal), policy);
        NativeModuleCompiler.ValidateGraph(root, [replacement, middle]);
        Assert.That(await NativeModuleCompiler.RunGraphAsync(root, [replacement, middle],
            TimeSpan.FromSeconds(10), policy), Is.EqualTo(44));
        Assert.That(root, Is.EqualTo(originalRoot));
        Assert.That(middle, Is.EqualTo(originalMiddle));
        _ = Assert.Throws<ModuleContractException>(() => NativeModuleCompiler.ValidateGraph(root, [
            Leaf(LeafSource.Replace("public int32 Value;", "public int64 Value;", StringComparison.Ordinal)
                .Replace("Value = value;", "Value = int64(value);", StringComparison.Ordinal), policy), middle]))!;
    }
    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public void DirectForwardingRetainsCleanupDeclarationWithoutMachineImport(NativeRuntimePolicy policy)
    {
        var leaf = Leaf(LeafSource, policy);
        var middle = NativeModuleCompiler.CreateGraphProducer(Compile(
            "public Ticket Forward() { return Make(); }", policy,
            NativeModuleCompiler.CreateGraphReference([leaf])),
            ["Forward"], "Middle", [leaf], policy);
        var (declarations, imports) = Mapping(middle, "Leaf");
        Assert.That(declarations, Does.Contain("Ticket.Close"));
        Assert.That(imports, Does.Not.Contain("Ticket.Close"));
        Assert.That(imports, Does.Contain("Make"));
    }
    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public async Task CopyTypeSharedOriginMergesInDiamondAsync(NativeRuntimePolicy policy)
    {
        const string Source = """
            [Blittable] [Layout(LayoutKind.Sequential)] public struct Shared {
                public int32 Value;
                public Shared(int32 value) { Value = value; }
            }
            public Shared Make() { return new Shared(40); }
            """;
        var leaf = NativeModuleCompiler.CreateProducer(Compile(Source, policy), ["Make"], "Leaf", policy);
        var middle = NativeModuleCompiler.CreateGraphProducer(Compile(
            "public Shared Forward() { return Make(); }", policy,
            NativeModuleCompiler.CreateGraphReference([leaf])),
            ["Forward"], "Middle", [leaf], policy);
        var root = NativeModuleCompiler.CreateGraphConsumer(Compile(
            "int32 Main() { Shared value = Forward(); return value.Value + 2; }", policy,
            NativeModuleCompiler.CreateGraphReference([leaf, middle]), library: false),
            [middle, leaf], policy);
        Assert.That(await NativeModuleCompiler.RunGraphAsync(root, [leaf, middle],
            TimeSpan.FromSeconds(10), policy), Is.EqualTo(42));
        var replacement = NativeModuleCompiler.CreateProducer(Compile(
            Source.Replace("new Shared(40)", "new Shared(41)", StringComparison.Ordinal), policy),
            ["Make"], "Leaf", policy);
        Assert.That(await NativeModuleCompiler.RunGraphAsync(root, [middle, replacement],
            TimeSpan.FromSeconds(10), policy), Is.EqualTo(43));
    }
    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public async Task SparseSharedTypeOriginsMergeAcrossProvidersAsync(NativeRuntimePolicy policy)
    {
        const string SharedSource = """
            [Blittable] [Layout(LayoutKind.Sequential)] public struct Shared {
                public int32 Value;
                public Shared(int32 value) { Value = value; }
            }
            public Shared MakeShared() { return new Shared(40); }
            """;
        var leaf = NativeModuleCompiler.CreateProducer(Compile(SharedSource, policy),
            ["MakeShared"], "Leaf", policy);
        var middle = NativeModuleCompiler.CreateGraphProducer(Compile(
            "public Shared Forward() { return MakeShared(); }", policy,
            NativeModuleCompiler.CreateGraphReference([leaf])),
            ["Forward"], "Middle", [leaf], policy);
        var alpha = CopyLeaf("Alpha", "MakeAlpha", "Alpha", 0, policy);
        var beta = CopyLeaf("Beta", "MakeBeta", "Beta", 0, policy);
        var gamma = CopyLeaf("Gamma", "MakeGamma", "Gamma", 0, policy);
        var dependencies = new[] { leaf, middle, alpha, beta, gamma };
        var root = NativeModuleCompiler.CreateGraphConsumer(Compile("""
            int32 Main() {
                Shared shared = Forward();
                Alpha alpha = MakeAlpha();
                Beta beta = MakeBeta();
                Gamma gamma = MakeGamma();
                return shared.Value + alpha.Value + beta.Value + gamma.Value + 2;
            }
            """, policy, NativeModuleCompiler.CreateGraphReference(dependencies), library: false),
            dependencies, policy);
        NativeModuleCompiler.ValidateGraph(root, dependencies);
        Assert.That(await NativeModuleCompiler.RunGraphAsync(root,
            [gamma, middle, leaf, beta, alpha], TimeSpan.FromSeconds(10), policy), Is.EqualTo(42));
    }
    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public async Task UniqueWithoutDeclaredCleanupRetainsItsOriginAsync(NativeRuntimePolicy policy)
    {
        const string Source = """
            [Blittable] [Layout(LayoutKind.Sequential)] public unique struct Plain {
                public int32 Value;
                [NoThrow] public Plain(int32 value) { Value = value; }
            }
            public Plain Make() { return new Plain(40); }
            """;
        var leaf = NativeModuleCompiler.CreateProducer(Compile(Source, policy), ["Make"], "Leaf", policy);
        var middle = NativeModuleCompiler.CreateGraphProducer(Compile(
            "public Plain Forward() { return Make(); }", policy,
            NativeModuleCompiler.CreateGraphReference([leaf])),
            ["Forward"], "Middle", [leaf], policy);
        var root = NativeModuleCompiler.CreateGraphConsumer(Compile(
            "int32 Main() { Plain value = Forward(); return value.Value + 2; }", policy,
            NativeModuleCompiler.CreateGraphReference([middle, leaf]), library: false),
            [middle, leaf], policy);
        Assert.That(await NativeModuleCompiler.RunGraphAsync(root, [middle, leaf],
            TimeSpan.FromSeconds(10), policy), Is.EqualTo(42));
        var replacement = NativeModuleCompiler.CreateProducer(Compile(
            Source.Replace("new Plain(40)", "new Plain(41)", StringComparison.Ordinal), policy),
            ["Make"], "Leaf", policy);
        Assert.That(await NativeModuleCompiler.RunGraphAsync(root, [replacement, middle],
            TimeSpan.FromSeconds(10), policy), Is.EqualTo(43));
    }

    [Test]
    public async Task PrivateGraphImplementationTypeDoesNotShadowAnotherPublicTypeAsync()
    {
        const NativeRuntimePolicy Policy = NativeRuntimePolicy.FailFast;
        var seed = NativeModuleCompiler.CreateProducer(Compile(
            "public int32 Seed() { return 0; }", Policy), ["Seed"], "Seed", Policy);
        var middle = NativeModuleCompiler.CreateGraphProducer(Compile("""
            internal struct Shared {
                public int32 Value;
                public Shared(int32 value) { Value = value; }
            }
            public int32 Middle() {
                Shared hidden = new Shared(1);
                return Seed() + hidden.Value;
            }
            """, Policy, NativeModuleCompiler.CreateGraphReference([seed])),
            ["Middle"], "Middle", [seed], Policy);
        var leaf = NativeModuleCompiler.CreateProducer(Compile("""
            [Blittable] [Layout(LayoutKind.Sequential)] public struct Shared {
                public int32 Value;
                public Shared(int32 value) { Value = value; }
            }
            public Shared Make() { return new Shared(40); }
            """, Policy), ["Make"], "Leaf", Policy);
        var root = NativeModuleCompiler.CreateGraphConsumer(Compile(
            "int32 Main() { Shared value = Make(); return value.Value + Middle() + 1; }", Policy,
            NativeModuleCompiler.CreateGraphReference([middle, leaf]), library: false),
            [middle, leaf], Policy);
        Assert.That(await NativeModuleCompiler.RunGraphAsync(root, [seed, leaf, middle],
            TimeSpan.FromSeconds(10), Policy), Is.EqualTo(42));
    }

    [Test]
    public async Task PrivateUniqueLocalNeedsNoPublicCleanupExportAsync()
    {
        const NativeRuntimePolicy Policy = NativeRuntimePolicy.FailFast;
        var seed = NativeModuleCompiler.CreateProducer(Compile(
            "public int32 Seed() { return 40; }", Policy), ["Seed"], "Seed", Policy);
        var middle = NativeModuleCompiler.CreateGraphProducer(Compile("""
            internal unique struct Local {
                public int32 Value;
                [NoThrow] public Local(int32 value) { Value = value; }
                [NoThrow] [Cleanup] public void Close() {}
            }
            public int32 Middle() {
                Local local = new Local(2);
                return Seed() + local.Value;
            }
            """, Policy, NativeModuleCompiler.CreateGraphReference([seed])),
            ["Middle"], "Middle", [seed], Policy);
        var root = NativeModuleCompiler.CreateGraphConsumer(Compile(
            "int32 Main() { return Middle(); }", Policy,
            NativeModuleCompiler.CreateGraphReference([middle]), library: false),
            [middle], Policy);
        Assert.That(await NativeModuleCompiler.RunGraphAsync(root, [seed, middle],
            TimeSpan.FromSeconds(10), Policy), Is.EqualTo(42));
    }

    [Test]
    public void EqualLayoutsFromDistinctOriginsDoNotMerge()
    {
        const string First = """
            [Blittable] [Layout(LayoutKind.Sequential)] public struct Shared {
                public int32 Value;
                public Shared(int32 value) { Value = value; }
            }
            public Shared First() { return new Shared(1); }
            """;
        const string Second = """
            [Blittable] [Layout(LayoutKind.Sequential)] public struct Shared {
                public int32 Value;
                public Shared(int32 value) { Value = value; }
            }
            public Shared Second() { return new Shared(2); }
            """;
        var left = NativeModuleCompiler.CreateProducer(Compile(First, NativeRuntimePolicy.FailFast),
            ["First"], "Left", NativeRuntimePolicy.FailFast);
        var right = NativeModuleCompiler.CreateProducer(Compile(Second, NativeRuntimePolicy.FailFast),
            ["Second"], "Right", NativeRuntimePolicy.FailFast);
        _ = Assert.Throws<ModuleContractException>(() => NativeModuleCompiler.CreateGraphReference([left, right]))!;
    }

    [Test]
    public void RecalculatedOriginAndCleanupTamperingIsRejected()
    {
        var leaf = Leaf(LeafSource, NativeRuntimePolicy.FailFast);
        var middle = NativeModuleCompiler.CreateGraphProducer(Compile(
            "public Ticket Forward() { return Make(); }", NativeRuntimePolicy.FailFast,
            NativeModuleCompiler.CreateGraphReference([leaf])),
            ["Forward"], "Middle", [leaf], NativeRuntimePolicy.FailFast);
        var root = NativeModuleCompiler.CreateGraphConsumer(Compile(
            "int32 Main() { Ticket ticket = Forward(); return ticket.Value; }",
            NativeRuntimePolicy.FailFast, NativeModuleCompiler.CreateGraphReference([middle, leaf]),
            library: false), [middle, leaf], NativeRuntimePolicy.FailFast);
        var (owner, cleanup) = OriginOffsets(middle, "Ticket");
        var wrongOwner = middle.ToArray();
        "Evil"u8.CopyTo(wrongOwner.AsSpan(owner));
        Rehash(wrongOwner);
        _ = Assert.Throws<ModuleFormatException>(() => NativeModuleCompiler.ValidateGraph(root, [leaf, wrongOwner]))!;
        var wrongCleanup = middle.ToArray();
        "Evil"u8.CopyTo(wrongCleanup.AsSpan(cleanup));
        Rehash(wrongCleanup);
        _ = Assert.Throws<ModuleFormatException>(() => NativeModuleCompiler.CreateGraphReference([wrongCleanup, leaf]))!;
    }

    private static (int Owner, int Cleanup) OriginOffsets(byte[] package, string type)
    {
        Assert.That(package[4], Is.EqualTo(2));
        var at = 19 + package[7];
        for (var i = 0; i < package[8]; i++)
        {
            _ = ReadString();
            var declarations = ReadCount();
            for (var j = 0; j < declarations; j++)
            {
                _ = ReadString();
            }
            var imports = ReadCount();
            for (var j = 0; j < imports; j++)
            {
                _ = ReadString();
            }
        }
        var exports = ReadCountAt(9);
        for (var i = 0; i < exports; i++)
        {
            _ = ReadString();
        }
        var origins = ReadCount();
        for (var i = 0; i < origins; i++)
        {
            if (ReadString() == type)
            {
                var owner = at + 2;
                _ = ReadString();
                var cleanup = at + 2;
                Assert.That(ReadCount(), Is.EqualTo(4));
                return (owner, cleanup);
            }
            _ = ReadString();
            var length = ReadCount();
            at += length;
        }
        throw new NUnit.Framework.AssertionException("Missing retained type origin.");

        int ReadCount()
        {
            var count = ReadCountAt(at);
            at += 2;
            return count;
        }
        int ReadCountAt(int offset)
        {
            return BinaryPrimitives.ReadUInt16LittleEndian(package.AsSpan(offset));
        }
        string ReadString()
        {
            var length = ReadCount();
            var result = Encoding.UTF8.GetString(package, at, length);
            at += length;
            return result;
        }
    }

    private static (string[] Declarations, string[] Imports) Mapping(byte[] package, string identity)
    {
        Assert.That(package[4], Is.EqualTo(2));
        var at = 19 + package[7];
        for (var i = 0; i < package[8]; i++)
        {
            var name = ReadString();
            var declarations = ReadNames();
            var imports = ReadNames();
            if (name == identity)
            {
                return (declarations, imports);
            }
        }
        throw new NUnit.Framework.AssertionException("Missing graph dependency mapping.");

        string[] ReadNames()
        {
            var count = ReadCount();
            var result = new string[count];
            for (var i = 0; i < result.Length; i++)
            {
                result[i] = ReadString();
            }
            return result;
        }
        int ReadCount()
        {
            var result = BinaryPrimitives.ReadUInt16LittleEndian(package.AsSpan(at));
            at += 2;
            return result;
        }
        string ReadString()
        {
            var length = ReadCount();
            var result = Encoding.UTF8.GetString(package, at, length);
            at += length;
            return result;
        }
    }

    private static void Rehash(byte[] package) =>
        SHA256.HashData(package.AsSpan(0, package.Length - 32)).CopyTo(package, package.Length - 32);
}
