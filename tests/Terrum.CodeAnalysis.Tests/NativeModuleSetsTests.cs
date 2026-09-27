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

public sealed class NativeModuleSetsTests
{
    private const string Alpha = "public int32 Alpha() { return 20; }";
    private const string Beta = "public int32 Beta() { return 22; }";
    private const string Consumer = "int32 Main() { return Alpha() + Beta(); }";

    private static byte[] Producer(string source, string export, string identity)
        => Producer(source, [export], identity);

    private static byte[] Producer(string source, string[] exports, string identity)
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        return NativeModuleCompiler.CreateProducer(Assert.IsType<BoundProgram>(compilation.Program),
            exports, identity, NativeRuntimePolicy.FailFast);
    }

    private static byte[] MakeConsumer(params byte[][] producers) => MakeConsumerSource(Consumer, producers);

    private static byte[] MakeConsumerSource(string source, params byte[][] producers)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(source),
            NativeModuleCompiler.CreateReferenceSet(producers));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        return NativeModuleCompiler.CreateConsumerSet(Assert.IsType<BoundProgram>(compilation.Program),
            producers, NativeRuntimePolicy.FailFast);
    }

    private static byte[] WithUpdatedSetChecksum(byte[] artifact, Action<byte[]> mutate)
    {
        var result = artifact.ToArray();
        mutate(result);
        SHA256.HashData(result.AsSpan(0, result.Length - 32)).CopyTo(result, result.Length - 32);
        return result;
    }

    private static int UniqueOffset(byte[] bytes, string text)
    {
        var value = Encoding.ASCII.GetBytes(text);
        var offset = bytes.AsSpan().IndexOf(value);
        Assert.True(offset >= 0, $"Missing expected serialized value '{text}'.");
        Assert.Equal(-1, bytes.AsSpan(offset + 1).IndexOf(value));
        return offset;
    }

    [Fact]
    public async Task PersistedConsumerRunsIndependentReplacementProvidersAsync()
    {
        var alpha = Producer(Alpha, "Alpha", "First");
        var beta = Producer(Beta, "Beta", "Second");
        var bytes = MakeConsumer(beta, alpha);
        var unchanged = bytes.ToArray();
        NativeModuleCompiler.ValidateConsumerSet(bytes, [alpha, beta]);
        Assert.Equal(["First", "Second"], NativeModuleCompiler.InspectConsumerSet(bytes).Dependencies);
        Assert.Equal(42, await NativeModuleCompiler.RunConsumerSetAsync(bytes, [beta, alpha],
            TimeSpan.FromSeconds(10), NativeRuntimePolicy.FailFast));
        var alphaReplacement = Producer(Alpha.Replace("20", "21", StringComparison.Ordinal), "Alpha", "First");
        var betaReplacement = Producer(Beta.Replace("22", "23", StringComparison.Ordinal), "Beta", "Second");
        Assert.Equal(43, await NativeModuleCompiler.RunConsumerSetAsync(bytes, [alphaReplacement, beta],
            TimeSpan.FromSeconds(10), NativeRuntimePolicy.FailFast));
        Assert.Equal(43, await NativeModuleCompiler.RunConsumerSetAsync(bytes, [betaReplacement, alpha],
            TimeSpan.FromSeconds(10), NativeRuntimePolicy.FailFast));
        Assert.Equal(unchanged, bytes);
    }

    [Fact]
    public void RejectsMissingDuplicateAmbiguousAndIncompatibleDependencies()
    {
        var alpha = Producer(Alpha, "Alpha", "First");
        var beta = Producer(Beta, "Beta", "Second");
        var consumer = MakeConsumer(alpha, beta);
        _ = Assert.Throws<ModuleContractException>(() => NativeModuleCompiler.ValidateConsumerSet(consumer, [alpha]));
        _ = Assert.Throws<ModuleContractException>(() => NativeModuleCompiler.ValidateConsumerSet(consumer, [alpha, alpha]));
        _ = Assert.Throws<ModuleContractException>(() => NativeModuleCompiler.CreateReferenceSet([
            alpha, Producer("public int32 Alpha() { return 1; }", "Alpha", "Other")]));
        _ = Assert.Throws<ModuleContractException>(() => NativeModuleCompiler.ValidateConsumerSet(consumer, [
            Producer("public int64 Alpha() { return 20; }", "Alpha", "First"), beta]));
        _ = Assert.Throws<ModuleContractException>(() => NativeModuleCompiler.ValidateConsumerSet(consumer, [
            alpha, Producer("public int32 Beta() { return 22; }", "Beta", "Third")]));
        _ = Assert.Throws<ModuleContractException>(() => NativeModuleCompiler.ValidateConsumerSet(consumer, [
            alpha, beta, Producer("public int32 Gamma() { return 1; }", "Gamma", "Third")]));
        _ = Assert.Throws<ModuleContractException>(() => NativeModuleCompiler.ValidateConsumerSet(consumer, [
            alpha, Producer(Beta, "Beta", "second")]));
        var checkedCompilation = Compilation.CreateLibrary(SyntaxTree.Parse(Beta),
            BoundRuntimePolicy.CheckedExceptions);
        Assert.False(checkedCompilation.HasErrors);
        var checkedBeta = NativeModuleCompiler.CreateProducer(Assert.IsType<BoundProgram>(checkedCompilation.Program),
            ["Beta"], "Second", NativeRuntimePolicy.CheckedExceptions);
        _ = Assert.Throws<ModuleContractException>(() => NativeModuleCompiler.ValidateConsumerSet(consumer,
            [alpha, checkedBeta]));
        var tampered = consumer.ToArray();
        tampered[^1] ^= 1;
        _ = Assert.Throws<ModuleFormatException>(() => NativeModuleCompiler.ValidateConsumerSet(tampered, [alpha, beta]));
        var version = WithUpdatedSetChecksum(consumer, bytes => bytes[4] = 3);
        _ = Assert.Throws<ModuleFormatException>(() => NativeModuleCompiler.InspectConsumerSet(version));
        var mapping = WithUpdatedSetChecksum(consumer, bytes => {
            var firstImportOffset = 16 + 1 + "First".Length + 2 + 2;
            bytes[firstImportOffset] = (byte)'G';
        });
        _ = Assert.Throws<ModuleFormatException>(() => NativeModuleCompiler.ValidateConsumerSet(mapping, [alpha, beta]));
        var bounds = WithUpdatedSetChecksum(consumer, bytes =>
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8), int.MaxValue));
        _ = Assert.Throws<ModuleFormatException>(() => NativeModuleCompiler.InspectConsumerSet(bounds));
    }

    [Fact]
    public void ReplacementChecksRetainedEffectsAndValueLayouts()
    {
        var beta = Producer(Beta, "Beta", "Second");
        var noThrow = Producer("[NoThrow] public int32 Alpha() { return 20; }", "Alpha", "First");
        var effectConsumer = MakeConsumer(noThrow, beta);
        _ = Assert.Throws<ModuleContractException>(() => NativeModuleCompiler.ValidateConsumerSet(effectConsumer,
            [Producer(Alpha, "Alpha", "First"), beta]));

        const string Value = """
            public struct Box {
                public int32 Value;
                public Box(int32 value) { Value = value; }
            }
            public Box Alpha() { return new Box(20); }
            """;
        var boxed = Producer(Value, "Alpha", "First");
        var boxedConsumer = MakeConsumerSource(
            "int32 Main() { Box value = Alpha(); return value.Value + Beta(); }", boxed, beta);
        NativeModuleCompiler.ValidateConsumerSet(boxedConsumer, [boxed, beta]);
        _ = Assert.Throws<ModuleContractException>(() => NativeModuleCompiler.ValidateConsumerSet(boxedConsumer, [
            Producer(Value.Replace("public int32 Value;", "public uint32 Value;", StringComparison.Ordinal)
                .Replace("Box(int32 value)", "Box(uint32 value)", StringComparison.Ordinal)
                .Replace("new Box(20)", "new Box(uint32(20))", StringComparison.Ordinal),
                "Alpha", "First"), beta]));

        const string Output = "public bool Alpha(out int32 value) { value = 20; return true; }";
        var output = Producer(Output, "Alpha", "First");
        var outputConsumer = MakeConsumerSource(
            "int32 Main() { require Alpha(out var value); return value + Beta(); }", output, beta);
        _ = Assert.Throws<ModuleContractException>(() => NativeModuleCompiler.ValidateConsumerSet(outputConsumer, [
            Producer("public int32 Alpha() { return 20; }", "Alpha", "First"), beta]));

        const string Owner = """
            public unique struct Lease {
                public int32 Value;
                public Lease(int32 value) { Value = value; }
                [NoThrow] [Cleanup] public void Close() { }
            }
            public Lease Alpha() { return new Lease(20); }
            """;
        var owned = Producer(Owner, ["Alpha", "Lease.Close"], "First");
        var ownedConsumer = MakeConsumerSource(
            "int32 Main() { Lease value = Alpha(); return value.Value + Beta(); }", owned, beta);
        NativeModuleCompiler.ValidateConsumerSet(ownedConsumer, [owned, beta]);
        Assert.NotEqual(Compilation.ProfileVersion,
            NativeModuleCompiler.InspectConsumerSet(ownedConsumer).NativeProfileVersion);
        _ = Assert.Throws<ModuleContractException>(() => NativeModuleCompiler.ValidateConsumerSet(ownedConsumer, [
            Producer(Owner.Replace("public int32 Value;", "public int32 Identifier;", StringComparison.Ordinal)
                .Replace("Value = value;", "Identifier = value;", StringComparison.Ordinal),
                ["Alpha", "Lease.Close"], "First"), beta]));

        var unusedOwner = Producer(Alpha + """
             unique struct Unused {
                 public int32 Value;
                 public Unused(int32 value) { Value = value; }
                 [NoThrow] [Cleanup] public void Close() { }
             }
             """, "Alpha", "First");
        var unownedConsumer = MakeConsumer(unusedOwner, beta);
        Assert.Equal(Compilation.ProfileVersion,
            NativeModuleCompiler.InspectConsumerSet(unownedConsumer).NativeProfileVersion);
    }

    [Fact]
    public async Task CheckedExceptionPolicyBindsBothProvidersAsync()
    {
        static byte[] CheckedProducer(string source, string name, string identity)
        {
            var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source),
                BoundRuntimePolicy.CheckedExceptions);
            Assert.False(compilation.HasErrors);
            return NativeModuleCompiler.CreateProducer(Assert.IsType<BoundProgram>(compilation.Program),
                [name], identity, NativeRuntimePolicy.CheckedExceptions);
        }

        var first = CheckedProducer(Alpha, "Alpha", "First");
        var second = CheckedProducer(Beta, "Beta", "Second");
        var sources = Compilation.Create(SyntaxTree.Parse(Consumer),
            NativeModuleCompiler.CreateReferenceSet([first, second]), BoundRuntimePolicy.CheckedExceptions);
        Assert.False(sources.HasErrors);
        var consumer = NativeModuleCompiler.CreateConsumerSet(Assert.IsType<BoundProgram>(sources.Program),
            [second, first], NativeRuntimePolicy.CheckedExceptions);
        Assert.Equal(42, await NativeModuleCompiler.RunConsumerSetAsync(consumer, [first, second],
            TimeSpan.FromSeconds(10), NativeRuntimePolicy.CheckedExceptions));
        _ = Assert.Throws<ModuleContractException>(() => NativeModuleCompiler.ValidateConsumerSet(consumer,
            [Producer(Alpha, "Alpha", "First"), second]));
    }

    [Theory]
    [InlineData("int64", "22")]
    [InlineData("uint32", "22")]
    public async Task BothIndependentNestedValueTablesBindAndExecuteAsync(string secondType, string secondValue)
    {
        const string First = """
            public struct FirstInner {
                public int32 Value;
                public FirstInner(int32 value) { Value = value; }
            }
            public struct FirstOuter {
                public FirstInner Child;
                public FirstOuter(FirstInner child) { Child = child; }
            }
            public FirstOuter Alpha() { return new FirstOuter(new FirstInner(20)); }
            """;
        var second = $$"""
            public struct SecondInner {
                public {{secondType}} Value;
                public SecondInner({{secondType}} value) { Value = value; }
            }
            public struct SecondOuter {
                public SecondInner Child;
                public SecondOuter(SecondInner child) { Child = child; }
            }
            public SecondOuter Beta() { return new SecondOuter(new SecondInner({{secondValue}})); }
            """;
        var first = Producer(First, "Alpha", "First");
        var provider = Producer(second, "Beta", "Second");
        var reference = NativeModuleCompiler.CreateReferenceSet([provider, first]);
        foreach (var (outer, inner) in new[] { ("FirstOuter", "FirstInner"), ("SecondOuter", "SecondInner") })
        {
            var structure = Assert.Single(reference.Structures, item => item.Name == outer);
            Assert.Equal(inner, reference.Structures[(int)structure.Fields[0].Type - 100].Name);
        }
        const string Source = """
            struct Unused { public int64 Marker; }
            int32 Main() {
                FirstOuter first = Alpha();
                SecondOuter second = Beta();
                return first.Child.Value + int32(second.Child.Value);
            }
            """;
        var executable = MakeConsumerSource(Source, provider, first);
        Assert.Equal(42, await NativeModuleCompiler.RunConsumerSetAsync(executable, [first, provider],
            TimeSpan.FromSeconds(10), NativeRuntimePolicy.FailFast));
    }

    [Fact]
    public void PublicIrCannotAddConsumerOwnedMemberToImportedType()
    {
        const string Provider = """
            public struct Box { public int32 Value; public Box(int32 value) { Value = value; } }
            public Box Alpha() { return new Box(20); }
            """;
        var first = Producer(Provider, "Alpha", "First");
        var second = Producer(Beta, "Beta", "Second");
        var reference = NativeModuleCompiler.CreateReferenceSet([first, second]);
        var result = Compilation.Create(SyntaxTree.Parse(
            "int32 Main() { Box value = Alpha(); return value.Value + Beta(); }"), reference);
        Assert.False(result.HasErrors);
        var program = Assert.IsType<BoundProgram>(result.Program);
        var probe = new BoundFunction("Box.Probe", PrimitiveType.Void, [], 0,
            new BoundBlock([new BoundReturn(null, default)], default), default) {
            DeclaringStruct = "Box",
            IsStatic = true,
        };
        var forged = program with { Functions = program.Functions.Add(probe) };
        var exception = Assert.Throws<ModuleContractException>(() => NativeModuleCompiler.CreateConsumerSet(forged,
            [first, second], NativeRuntimePolicy.FailFast));
        Assert.Equal("Consumer cannot define members of an imported value type.", exception.Message);
    }

    [Fact]
    public void RetainedImportOwnershipCannotBeLoweredByRehashingSet()
    {
        const string Owner = """
            public unique struct Token {
                public int32 Value;
                public Token(int32 value) { Value = value; }
                [NoThrow] [Cleanup] public void Close() { }
            }
            public Token New() { return new Token(20); }
            public int32 Take(Token value) { return value.Value; }
            """;
        var first = Producer(Owner, ["New", "Take", "Token.Close"], "First");
        var second = Producer(Beta, "Beta", "Second");
        var consumer = MakeConsumerSource(
            "int32 Main() { Token token = New(); return Take(move token) + Beta(); }", first, second);
        Assert.Equal("native-13", NativeModuleCompiler.InspectConsumerSet(consumer).NativeProfileVersion);
        var tampered = WithUpdatedSetChecksum(consumer, bytes => {
            var profile = UniqueOffset(bytes, "native-13");
            bytes[profile + "native-1".Length] = (byte)'2';
            var contract = UniqueOffset(bytes, "Take|");
            var end = bytes.AsSpan(contract).IndexOf(Encoding.ASCII.GetBytes("|MayReturn"));
            Assert.True(end >= 0);
            var marker = contract + end + "|MayReturn".Length;
            Assert.Equal((byte)1, bytes[marker]);
            Assert.Equal((byte)0, bytes[marker + 1]);
            Assert.Equal((byte)1, bytes[marker + 2]);
            bytes[marker + 2] = 0;
        });
        _ = Assert.Throws<ModuleContractException>(() => NativeModuleCompiler.ValidateConsumerSet(tampered,
            [first, second]));
    }
}
