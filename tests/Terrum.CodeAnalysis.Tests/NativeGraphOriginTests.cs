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
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed class NativeGraphOriginTests
{
    private const string LeafSource = """
        public unique struct Ticket {
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
            : Compilation.Create(syntax, Assert.IsType<CompilationReference>(reference), boundPolicy);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        return Assert.IsType<BoundProgram>(compilation.Program);
    }

    private static byte[] Leaf(string source, NativeRuntimePolicy policy) =>
        NativeModuleCompiler.CreateProducer(Compile(source, policy), ["Make", "Ticket.Close"],
            "Leaf", policy);

    [Theory]
    [InlineData(NativeRuntimePolicy.FailFast)]
    [InlineData(NativeRuntimePolicy.CheckedExceptions)]
    public async Task UniqueReturnAndOutputUseOriginCleanupWithUnchangedImagesAsync(NativeRuntimePolicy policy)
    {
        var leaf = Leaf(LeafSource, policy);
        var middle = NativeModuleCompiler.CreateGraphProducer(Compile("""
            public Ticket Forward() { return Make(); }
            public bool TryForward(out Ticket ticket) { ticket = Make(); return true; }
            """, policy, NativeModuleCompiler.CreateGraphReference([leaf])),
            ["Forward", "TryForward"], "Middle", [leaf], policy);
        _ = Assert.Throws<ModuleContractException>(() => NativeModuleCompiler.CreateGraphReference([middle]));
        var root = NativeModuleCompiler.CreateGraphConsumer(Compile("""
            int32 Main() {
                Ticket first = Forward();
                require TryForward(out var second);
                return first.Value + second.Value + 2;
            }
            """, policy, NativeModuleCompiler.CreateGraphReference([middle, leaf]), library: false),
            [middle, leaf], policy);
        var (declarations, imports) = Mapping(middle, "Leaf");
        Assert.Contains("Ticket.Close", declarations);
        Assert.Contains("Ticket.Close", imports);
        Assert.Contains("Ticket.Close", Mapping(root, "Leaf").Imports);
        var originalRoot = root.ToArray();
        var originalMiddle = middle.ToArray();
        NativeModuleCompiler.ValidateGraph(root, [leaf, middle]);
        Assert.Equal(42, await NativeModuleCompiler.RunGraphAsync(root, [middle, leaf],
            TimeSpan.FromSeconds(10), policy));
        var replacement = Leaf(LeafSource.Replace("new Ticket(20)", "new Ticket(21)",
            StringComparison.Ordinal), policy);
        NativeModuleCompiler.ValidateGraph(root, [replacement, middle]);
        Assert.Equal(44, await NativeModuleCompiler.RunGraphAsync(root, [replacement, middle],
            TimeSpan.FromSeconds(10), policy));
        Assert.Equal(originalRoot, root);
        Assert.Equal(originalMiddle, middle);
        _ = Assert.Throws<ModuleContractException>(() => NativeModuleCompiler.ValidateGraph(root, [
            Leaf(LeafSource.Replace("public int32 Value;", "public int64 Value;", StringComparison.Ordinal)
                .Replace("Value = value;", "Value = int64(value);", StringComparison.Ordinal), policy), middle]));
    }

    [Theory]
    [InlineData(NativeRuntimePolicy.FailFast)]
    [InlineData(NativeRuntimePolicy.CheckedExceptions)]
    public void DirectForwardingRetainsCleanupDeclarationWithoutMachineImport(NativeRuntimePolicy policy)
    {
        var leaf = Leaf(LeafSource, policy);
        var middle = NativeModuleCompiler.CreateGraphProducer(Compile(
            "public Ticket Forward() { return Make(); }", policy,
            NativeModuleCompiler.CreateGraphReference([leaf])),
            ["Forward"], "Middle", [leaf], policy);
        var (declarations, imports) = Mapping(middle, "Leaf");
        Assert.Contains("Ticket.Close", declarations);
        Assert.DoesNotContain("Ticket.Close", imports);
        Assert.Contains("Make", imports);
    }

    [Theory]
    [InlineData(NativeRuntimePolicy.FailFast)]
    [InlineData(NativeRuntimePolicy.CheckedExceptions)]
    public async Task CopyTypeSharedOriginMergesInDiamondAsync(NativeRuntimePolicy policy)
    {
        const string Source = """
            public struct Shared {
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
        Assert.Equal(42, await NativeModuleCompiler.RunGraphAsync(root, [leaf, middle],
            TimeSpan.FromSeconds(10), policy));
        var replacement = NativeModuleCompiler.CreateProducer(Compile(
            Source.Replace("new Shared(40)", "new Shared(41)", StringComparison.Ordinal), policy),
            ["Make"], "Leaf", policy);
        Assert.Equal(43, await NativeModuleCompiler.RunGraphAsync(root, [middle, replacement],
            TimeSpan.FromSeconds(10), policy));
    }

    [Theory]
    [InlineData(NativeRuntimePolicy.FailFast)]
    [InlineData(NativeRuntimePolicy.CheckedExceptions)]
    public async Task UniqueWithoutDeclaredCleanupRetainsItsOriginAsync(NativeRuntimePolicy policy)
    {
        const string Source = """
            public unique struct Plain {
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
        Assert.Equal(42, await NativeModuleCompiler.RunGraphAsync(root, [middle, leaf],
            TimeSpan.FromSeconds(10), policy));
        var replacement = NativeModuleCompiler.CreateProducer(Compile(
            Source.Replace("new Plain(40)", "new Plain(41)", StringComparison.Ordinal), policy),
            ["Make"], "Leaf", policy);
        Assert.Equal(43, await NativeModuleCompiler.RunGraphAsync(root, [replacement, middle],
            TimeSpan.FromSeconds(10), policy));
    }

    [Fact]
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
            public struct Shared {
                public int32 Value;
                public Shared(int32 value) { Value = value; }
            }
            public Shared Make() { return new Shared(40); }
            """, Policy), ["Make"], "Leaf", Policy);
        var root = NativeModuleCompiler.CreateGraphConsumer(Compile(
            "int32 Main() { Shared value = Make(); return value.Value + Middle() + 1; }", Policy,
            NativeModuleCompiler.CreateGraphReference([middle, leaf]), library: false),
            [middle, leaf], Policy);
        Assert.Equal(42, await NativeModuleCompiler.RunGraphAsync(root, [seed, leaf, middle],
            TimeSpan.FromSeconds(10), Policy));
    }

    [Fact]
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
        Assert.Equal(42, await NativeModuleCompiler.RunGraphAsync(root, [seed, middle],
            TimeSpan.FromSeconds(10), Policy));
    }

    [Fact]
    public void EqualLayoutsFromDistinctOriginsDoNotMerge()
    {
        const string First = """
            public struct Shared {
                public int32 Value;
                public Shared(int32 value) { Value = value; }
            }
            public Shared First() { return new Shared(1); }
            """;
        const string Second = """
            public struct Shared {
                public int32 Value;
                public Shared(int32 value) { Value = value; }
            }
            public Shared Second() { return new Shared(2); }
            """;
        var left = NativeModuleCompiler.CreateProducer(Compile(First, NativeRuntimePolicy.FailFast),
            ["First"], "Left", NativeRuntimePolicy.FailFast);
        var right = NativeModuleCompiler.CreateProducer(Compile(Second, NativeRuntimePolicy.FailFast),
            ["Second"], "Right", NativeRuntimePolicy.FailFast);
        _ = Assert.Throws<ModuleContractException>(() => NativeModuleCompiler.CreateGraphReference([left, right]));
    }

    [Fact]
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
        _ = Assert.Throws<ModuleFormatException>(() => NativeModuleCompiler.ValidateGraph(root, [leaf, wrongOwner]));
        var wrongCleanup = middle.ToArray();
        "Evil"u8.CopyTo(wrongCleanup.AsSpan(cleanup));
        Rehash(wrongCleanup);
        _ = Assert.Throws<ModuleFormatException>(() => NativeModuleCompiler.CreateGraphReference([wrongCleanup, leaf]));
    }

    private static (int Owner, int Cleanup) OriginOffsets(byte[] package, string type)
    {
        Assert.Equal(2, package[4]);
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
                Assert.Equal(4, ReadCount());
                return (owner, cleanup);
            }
            _ = ReadString();
            var length = ReadCount();
            at += length;
        }
        throw new Xunit.Sdk.XunitException("Missing retained type origin.");

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
        Assert.Equal(2, package[4]);
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
        throw new Xunit.Sdk.XunitException("Missing graph dependency mapping.");

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
