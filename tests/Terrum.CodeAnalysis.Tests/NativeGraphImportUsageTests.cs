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

public sealed class NativeGraphImportUsageTests
{
    private const string ProducerSource = """
        public unique struct Child {
            public int32 Value;
            [NoThrow] public Child(int32 value) { Value = value; }
            [NoThrow] [Cleanup] public void Close() {}
        }
        public unique struct Parent {
            public Child Owned;
            [NoThrow] public Parent(int32 value) { Owned = new Child(value); }
        }
        public Parent Make() { return new Parent(40); }
        """;

    private static BoundProgram Compile(string source, NativeRuntimePolicy policy,
        CompilationReference? reference = null, bool library = true)
    {
        var syntax = SyntaxTree.Parse(source);
        var runtime = (BoundRuntimePolicy)policy;
        var compilation = library
            ? reference is null ? Compilation.CreateLibrary(syntax, runtime)
                : Compilation.CreateLibrary(syntax, reference, runtime)
            : Compilation.Create(syntax, Assert.IsType<CompilationReference>(reference), runtime);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        return Assert.IsType<BoundProgram>(compilation.Program);
    }

    [Theory]
    [InlineData(NativeRuntimePolicy.FailFast)]
    [InlineData(NativeRuntimePolicy.CheckedExceptions)]
    public async Task PairAndSetValidateGeneratedChildCleanupImportsAsync(NativeRuntimePolicy policy)
    {
        var leaf = NativeModuleCompiler.CreateProducer(Compile(ProducerSource, policy),
            ["Make", "Child.Close"], "Leaf", policy);
        var pair = NativeModuleCompiler.CreateConsumer(Compile("""
            int32 Main() {
                Parent value = Make();
                return value.Owned.Value + 2;
            }
            """, policy, NativeModuleCompiler.CreateReference(leaf), library: false), leaf, policy);
        NativeModuleCompiler.Validate(pair, leaf);
        Assert.Equal(42, await NativeModuleCompiler.RunAsync(pair, leaf, TimeSpan.FromSeconds(10), policy));

        var addition = NativeModuleCompiler.CreateProducer(Compile(
            "public int32 Add() { return 2; }", policy), ["Add"], "Addition", policy);
        var set = NativeModuleCompiler.CreateConsumerSet(Compile("""
            int32 Main() {
                Parent value = Make();
                return value.Owned.Value + Add();
            }
            """, policy, NativeModuleCompiler.CreateReferenceSet([leaf, addition]), library: false),
            [leaf, addition], policy);
        NativeModuleCompiler.ValidateConsumerSet(set, [addition, leaf]);
        Assert.Equal(42, await NativeModuleCompiler.RunConsumerSetAsync(set, [leaf, addition],
            TimeSpan.FromSeconds(10), policy));
        var originalPair = pair.ToArray();
        var originalSet = set.ToArray();
        var replacement = NativeModuleCompiler.CreateProducer(Compile(
            ProducerSource.Replace("new Parent(40)", "new Parent(41)", StringComparison.Ordinal), policy),
            ["Make", "Child.Close"], "Leaf", policy);
        Assert.Equal(43, await NativeModuleCompiler.RunAsync(pair, replacement, TimeSpan.FromSeconds(10), policy));
        Assert.Equal(43, await NativeModuleCompiler.RunConsumerSetAsync(set, [addition, replacement],
            TimeSpan.FromSeconds(10), policy));
        Assert.Equal(originalPair, pair);
        Assert.Equal(originalSet, set);
    }

    [Fact]
    public void RecalculatedPairAndSetImportMappingTamperingIsRejected()
    {
        const NativeRuntimePolicy Policy = NativeRuntimePolicy.FailFast;
        var leaf = NativeModuleCompiler.CreateProducer(Compile(ProducerSource, Policy),
            ["Make", "Child.Close"], "Leaf", Policy);
        var pair = NativeModuleCompiler.CreateConsumer(Compile(
            "int32 Main() { Parent value = Make(); return value.Owned.Value; }", Policy,
            NativeModuleCompiler.CreateReference(leaf), library: false), leaf, Policy);
        Assert.Equal(2, pair[4]);
        var damagedPair = pair.ToArray();
        var offset = 18 + damagedPair[7] + damagedPair[8];
        var count = ReadCount(damagedPair, ref offset);
        var replaced = false;
        for (var index = 0; index < count; index++)
        {
            var name = ReadName(damagedPair, ref offset, out var start);
            if (name == "Make")
            {
                "Fake"u8.CopyTo(damagedPair.AsSpan(start));
                replaced = true;
            }
        }
        Assert.True(replaced);
        Rehash(damagedPair);
        _ = Assert.Throws<ModuleFormatException>(() => NativeModuleCompiler.Validate(damagedPair, leaf));

        var addition = NativeModuleCompiler.CreateProducer(Compile(
            "public int32 Add() { return 2; }", Policy), ["Add"], "Addition", Policy);
        var set = NativeModuleCompiler.CreateConsumerSet(Compile(
            "int32 Main() { Parent value = Make(); return value.Owned.Value + Add(); }", Policy,
            NativeModuleCompiler.CreateReferenceSet([leaf, addition]), library: false),
            [leaf, addition], Policy);
        Assert.Equal(2, set[4]);
        var damagedSet = set.ToArray();
        offset = 16;
        for (var dependency = 0; dependency < 2; dependency++)
        {
            var length = damagedSet[offset++];
            offset += length;
            var declarations = ReadCount(damagedSet, ref offset);
            for (var index = 0; index < declarations; index++)
            {
                _ = ReadName(damagedSet, ref offset, out _);
            }
            var imports = ReadCount(damagedSet, ref offset);
            for (var index = 0; index < imports; index++)
            {
                var name = ReadName(damagedSet, ref offset, out var start);
                if (name == "Add")
                {
                    "Bad"u8.CopyTo(damagedSet.AsSpan(start));
                }
            }
        }
        Assert.True(damagedSet.AsSpan().IndexOf("Bad"u8) >= 0);
        Rehash(damagedSet);
        _ = Assert.Throws<ModuleFormatException>(() => NativeModuleCompiler.ValidateConsumerSet(
            damagedSet, [leaf, addition]));
    }

    private static int ReadCount(byte[] bytes, ref int offset)
    {
        var result = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset));
        offset += 2;
        return result;
    }

    private static string ReadName(byte[] bytes, ref int offset, out int start)
    {
        var length = ReadCount(bytes, ref offset);
        start = offset;
        offset += length;
        return Encoding.UTF8.GetString(bytes, start, length);
    }

    private static void Rehash(byte[] bytes) =>
        SHA256.HashData(bytes.AsSpan(0, bytes.Length - 32)).CopyTo(bytes, bytes.Length - 32);
}
