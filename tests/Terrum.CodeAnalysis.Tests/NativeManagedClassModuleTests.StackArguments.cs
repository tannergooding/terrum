// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed partial class NativeManagedClassModuleTests
{
    [Theory]
    [InlineData(NativeRuntimePolicy.FailFast, false)]
    [InlineData(NativeRuntimePolicy.FailFast, true)]
    [InlineData(NativeRuntimePolicy.CheckedExceptions, false)]
    [InlineData(NativeRuntimePolicy.CheckedExceptions, true)]
    public void ClassStackArgumentsRetainVersionedManagedAccessAlongsideCopyAndLoopFeatures(
        NativeRuntimePolicy policy, bool copyArgument)
    {
        var source = StackArgumentLeaf(copyArgument) + "int32 Loop() { while (true) { break; } return 0; }";
        var producer = ModuleCompiler.CreateProducer(Compile(source, policy), ["Leaf"]);
        Assert.Equal(49, BinaryPrimitives.ReadInt32LittleEndian(producer.AsSpan(4, 4)));
        Assert.Equal("native-20", ModuleCompiler.GetNativeProfileVersion(producer, false));
        var reference = ModuleCompiler.CreateReference(producer);
        Assert.Empty(reference.Structures);
        Assert.Equal("Leaf", Assert.Single(reference.Functions).Name);
        foreach (var version in new[] { 19, 20, 21, 22, 23 })
        {
            var downgraded = (byte[])producer.Clone();
            BinaryPrimitives.WriteInt32LittleEndian(downgraded.AsSpan(4, 4), version);
            _ = Assert.Throws<ModuleFormatException>(() => ModuleCompiler.CreateReference(downgraded));
        }
    }

    [Theory]
    [InlineData(NativeRuntimePolicy.FailFast)]
    [InlineData(NativeRuntimePolicy.CheckedExceptions)]
    public async Task ClassStackArgumentsReplaceScalarLeafWithoutChangingConsumersAsync(NativeRuntimePolicy policy)
    {
        var stub = Compile("public int32 Leaf() { return 40; }", policy);
        var original = ModuleCompiler.CreateProducer(stub, ["Leaf"]);
        var consumer = ModuleCompiler.CreateConsumer(Compile("int32 Main() { return Leaf() + 2; }",
            policy, ModuleCompiler.CreateReference(original), library: false), original);
        var consumerHash = SHA256.HashData(consumer);
        var program = Compile(StackArgumentLeaf(copyArgument: true), policy);
        var replacement = ModuleCompiler.CreateProducer(program, ["Leaf"]);
        Assert.Equal(49, BinaryPrimitives.ReadInt32LittleEndian(replacement.AsSpan(4, 4)));
        var linked = ModuleCompiler.Link(consumer, replacement);
        Assert.Equal(44, await ExecuteAsync(NativeEmitter.Emit(linked, policy)).ConfigureAwait(true));
        Assert.Equal(consumerHash, SHA256.HashData(consumer));

        var nativeOriginal = NativeModuleCompiler.CreateProducer(stub, ["Leaf"], "NodeB", policy);
        var middle = NativeModuleCompiler.CreateGraphProducer(Compile(Middle, policy,
            NativeModuleCompiler.CreateGraphReference([nativeOriginal])), ["Middle"], "NodeA", [nativeOriginal], policy);
        var root = NativeModuleCompiler.CreateGraphConsumer(Compile(Root, policy,
            NativeModuleCompiler.CreateGraphReference([middle]), library: false), [middle], policy);
        var rootHash = SHA256.HashData(root);
        var middleHash = SHA256.HashData(middle);
        var nativeReplacement = NativeModuleCompiler.CreateProducer(program, ["Leaf"], "NodeB", policy);
        Assert.Equal("native-20", NativeModuleCompiler.Inspect(nativeReplacement).NativeProfileVersion);
        Assert.Equal(44, await NativeModuleCompiler.RunGraphAsync(root, [middle, nativeReplacement],
            TimeSpan.FromSeconds(10), policy).ConfigureAwait(true));
        var framework = new DirectoryInfo(AppContext.BaseDirectory);
        var configuration = framework.Parent?.Name
            ?? throw new InvalidOperationException("Missing test configuration directory.");
        var apphost = Path.Combine(RepositoryRoot(), "artifacts", "bin", "sources", "terrumc",
            configuration, framework.Name, "terrumc.exe");
        Assert.Equal(44, await NativeModuleCompiler.RunGraphJitAsync(root, [nativeReplacement, middle],
            apphost, TimeSpan.FromSeconds(30), policy).ConfigureAwait(true));
        Assert.Equal(rootHash, SHA256.HashData(root));
        Assert.Equal(middleHash, SHA256.HashData(middle));
    }

    private static string StackArgumentLeaf(bool copyArgument)
    {
        var source = copyArgument ? CopyArgumentLeaf() : Sample("managed-copy-class-leaf");
        var parameter = copyArgument ? "Stamp value" : "int32 value";
        var first = copyArgument ? "new Stamp(20)" : "20";
        var second = copyArgument ? "new Stamp(22)" : "22";
        return source.Replace("public Cycle(" + parameter + ")",
            "public Cycle(int32 prefix, bool ready, uint64 marker, " + parameter + ")", StringComparison.Ordinal)
            .Replace("Ready = true;", "Ready = ready; require prefix == 1 && marker == 18446744073709551615;",
                StringComparison.Ordinal)
            .Replace("Cycle.TryCreate(" + first + ",",
                "Cycle.TryCreate(1, true, 18446744073709551615, " + first + ",", StringComparison.Ordinal)
            .Replace("Cycle.TryCreate(" + second + ",",
                "Cycle.TryCreate(1, true, 18446744073709551615, " + second + ",", StringComparison.Ordinal);
    }
}
