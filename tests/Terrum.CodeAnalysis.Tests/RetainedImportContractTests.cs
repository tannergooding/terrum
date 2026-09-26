// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Text;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed class RetainedImportContractTests
{
    [Theory]
    [InlineData("[NoThrow] public int32 Leaf() { return 42; }",
        "[NoThrow] int32 Main() { return Leaf(); }", "|NoThrow|MayReturn", "|MayThrow|MayReturn")]
    [InlineData("[DoesNotReturn] public void Leaf() { while (true) {} }",
        "int32 Main() { Leaf(); }", "|MayThrow|DoesNotReturn", "|MayThrow|MayReturn")]
    public void LinkingDoesNotUpgradeTheConsumersRecordedPromises(
        string leafSource, string consumerSource, string originalPromise, string weakenedPromise)
    {
        ArgumentNullException.ThrowIfNull(originalPromise);
        var producer = Producer(leafSource);
        var compilation = Compilation.Create(SyntaxTree.Parse(consumerSource),
            ModuleCompiler.CreateReference(producer));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(Assert.IsType<BoundProgram>(compilation.Program), producer);
        _ = ModuleCompiler.Link(consumer, producer);

        var corrupted = WeakenLeafImport(consumer, originalPromise, weakenedPromise);
        _ = Assert.Throws<ModuleFormatException>(() => ModuleCompiler.Link(corrupted, producer));
    }

    [Fact]
    public void StrongerReplacementRemainsCompatibleWithAnUnchangedValidConsumer()
    {
        var producer = Producer("public int32 Leaf() { return 42; }");
        var compilation = Compilation.Create(SyntaxTree.Parse("int32 Main() { return Leaf(); }"),
            ModuleCompiler.CreateReference(producer));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(Assert.IsType<BoundProgram>(compilation.Program), producer);
        var original = (byte[])consumer.Clone();
        var replacement = Producer("[NoThrow] public int32 Leaf() { return 43; }");
        _ = ModuleCompiler.Link(consumer, replacement);
        Assert.Equal(original, consumer);
    }

    private static byte[] Producer(string source)
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        return ModuleCompiler.CreateProducer(Assert.IsType<BoundProgram>(compilation.Program), ["Leaf"]);
    }

    private static byte[] WeakenLeafImport(byte[] consumer, string originalPromise, string weakenedPromise)
    {
        var position = consumer.AsSpan().IndexOf("Leaf|"u8);
        Assert.True(position >= 4);
        Assert.Equal(-1, consumer.AsSpan(position + 5).IndexOf("Leaf|"u8));
        var length = BinaryPrimitives.ReadInt32LittleEndian(consumer.AsSpan(position - 4, 4));
        var contract = Encoding.UTF8.GetString(consumer, position, length);
        Assert.EndsWith(originalPromise, contract, StringComparison.Ordinal);
        var replacement = Encoding.UTF8.GetBytes(contract[..^originalPromise.Length] + weakenedPromise);
        var result = new byte[consumer.Length - length + replacement.Length];
        consumer.AsSpan(0, position - 4).CopyTo(result);
        BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(position - 4, 4), replacement.Length);
        replacement.CopyTo(result, position);
        consumer.AsSpan(position + length).CopyTo(result.AsSpan(position + replacement.Length));
        return result;
    }
}
