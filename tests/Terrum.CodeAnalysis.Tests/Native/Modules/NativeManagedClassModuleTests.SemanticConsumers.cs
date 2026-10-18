// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using NUnit.Framework;

using Terrum.CodeAnalysis.Tests.Infrastructure;

namespace Terrum.CodeAnalysis.Tests.Native.Modules;

public sealed partial class NativeManagedClassModuleTests
{
    private const string ConsumerProducerSource = """
        [Blittable] [Layout(LayoutKind.Sequential)] public struct Snapshot {
            public int64 First;
            public int64 Second;
            public Snapshot(int64 first, int64 second) { First = first; Second = second; }
        }
        mut class Cell {
            public Snapshot Data;
            public mut write Cell? Next;
            public Cell(Snapshot data) { Data = data; Next = null; }
        }
        public Snapshot Read() {
            require Cell.TryCreate(new Snapshot(39, 2), out var box);
            box.Next = box;
            return new Snapshot(box.Data.First + 0, box.Data.Second);
        }
        public bool TryRead(bool available, out Snapshot result) {
            if (!available) { return false; }
            result = Read();
            return true;
        }
        public bool Fail(out Snapshot result) {
            result = Read();
            require false;
            return true;
        }
        public int32 CollectProvider() { return ManagedHeap.Collect(); }
        """;

    private static string ManagedConsumerSource(NativeRuntimePolicy policy, bool linked, bool bias) => $$"""
        mut class Cell {
            public int32 Data;
            public mut write Cell? Next;
            public Cell(int32 data) { Data = data; Next = null; }
        }
        int32 Main() {
            {
                require Cell.TryCreate(1, out var abandoned);
                abandoned.Next = abandoned;
            }
            require !TryRead(false, out var vacant);
            require TryRead(true, out var snapshot);
            require Cell.TryCreate(int32(snapshot.First + snapshot.Second), out var root);
            root.Next = root;
            {{(policy == NativeRuntimePolicy.CheckedExceptions ? """
            unsafe {
                try { Fail(out var ignored); return 99; }
                catch { require root.Data == int32(snapshot.First + snapshot.Second); }
            }
            """ : "")}}
            require CollectProvider() == {{(linked
                ? policy == NativeRuntimePolicy.CheckedExceptions ? 3 : 2
                : policy == NativeRuntimePolicy.CheckedExceptions ? 2 : 1)}};
            require ManagedHeap.Collect() == {{(linked ? 0 : 1)}};
            return root.Data{{(bias ? " + Bias()" : "")}};
        }
        """;
    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public async Task ManagedSingleConsumerLinksPrivateClassesAndServesReplacementsAsync(NativeRuntimePolicy policy)
    {
        string[] exports = ["Read", "TryRead", "Fail", "CollectProvider"];
        var originalProgram = Compile(ConsumerProducerSource, policy);
        var replacementProgram = Compile(ConsumerProducerSource.Replace("box.Data.First + 0",
            "box.Data.First + 1", StringComparison.Ordinal), policy);
        var original = ModuleCompiler.CreateProducer(originalProgram, exports);
        var replacement = ModuleCompiler.CreateProducer(replacementProgram, exports);
        Assert.That(BinaryPrimitives.ReadInt32LittleEndian(original.AsSpan(4, 4)), Is.EqualTo(78));
        var source = ManagedConsumerSource(policy, linked: true, bias: false);
        var consumer = ModuleCompiler.CreateConsumer(Compile(source, policy,
            ModuleCompiler.CreateReference(original), library: false), original);
        Assert.That(BinaryPrimitives.ReadInt32LittleEndian(consumer.AsSpan(4, 4)), Is.EqualTo(78));
        var consumerHash = SHA256.HashData(consumer);
        var linked = ModuleCompiler.Link(consumer, replacement);
        Assert.That(linked.Classes.Select(static @class => @class.Name), Is.EqualTo(["Cell", "$producer.Cell"]));
        Assert.That(await ExecuteAsync(NativeEmitter.Emit(ModuleCompiler.Link(consumer, original), policy))
            .ConfigureAwait(true), Is.EqualTo(41));
        Assert.That(await ExecuteAsync(NativeEmitter.Emit(linked, policy)).ConfigureAwait(true), Is.EqualTo(42));
        Assert.That(await RunSemanticModuleJitAsync(consumer, replacement, policy).ConfigureAwait(true), Is.EqualTo(42));
        Assert.That(SHA256.HashData(consumer), Is.EqualTo(consumerHash));

        var nativeOriginal = NativeModuleCompiler.CreateProducer(originalProgram, exports, "NodeB", policy);
        var nativeReplacement = NativeModuleCompiler.CreateProducer(replacementProgram, exports, "NodeB", policy);
        var nativeSource = ManagedConsumerSource(policy, linked: false, bias: false);
        var nativeConsumer = NativeModuleCompiler.CreateConsumer(Compile(nativeSource, policy,
            NativeModuleCompiler.CreateReference(nativeOriginal), library: false), nativeOriginal, policy);
        var nativeHash = SHA256.HashData(nativeConsumer);
        Assert.That(await NativeModuleCompiler.RunAsync(nativeConsumer, nativeOriginal,
            TimeSpan.FromSeconds(10), policy).ConfigureAwait(true), Is.EqualTo(41));
        Assert.That(await NativeModuleCompiler.RunAsync(nativeConsumer, nativeReplacement,
            TimeSpan.FromSeconds(10), policy).ConfigureAwait(true), Is.EqualTo(42));
        Assert.That(SHA256.HashData(nativeConsumer), Is.EqualTo(nativeHash));
    }
    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public async Task ManagedCopyConsumerSampleServesUnchangedSemanticArtifactAsync(NativeRuntimePolicy policy)
    {
        var samples = Path.Combine(RepositoryRoot(), "samples");
        var consumerSource = await File.ReadAllTextAsync(Path.Combine(samples, "ModuleReplacement",
            "managed-copy-consumer.terrum")).ConfigureAwait(true);
        var originalSource = await File.ReadAllTextAsync(Path.Combine(samples, "DependencyGraphServicing",
            "copy-contract-original.terrum")).ConfigureAwait(true);
        var replacementSource = await File.ReadAllTextAsync(Path.Combine(samples, "DependencyGraphServicing",
            "copy-contract-managed.terrum")).ConfigureAwait(true);
        string[] exports = ["ReadSnapshot", "TryReadSnapshot", "ReadTotal"];
        var original = ModuleCompiler.CreateProducer(Compile(originalSource, policy), exports);
        var replacement = ModuleCompiler.CreateProducer(Compile(replacementSource, policy), exports);
        var consumer = ModuleCompiler.CreateConsumer(Compile(consumerSource, policy,
            ModuleCompiler.CreateReference(original), library: false), original);
        Assert.That(BinaryPrimitives.ReadInt32LittleEndian(consumer.AsSpan(4, 4)), Is.EqualTo(78));
        var hash = SHA256.HashData(consumer);
        var linkedOriginal = ModuleCompiler.Link(consumer, original);
        var linkedReplacement = ModuleCompiler.Link(consumer, replacement);
        Assert.That(linkedReplacement.Classes.Select(static @class => @class.Name), Is.EqualTo(["Record", "$producer.Record"]));
        Assert.That(await ExecuteAsync(NativeEmitter.Emit(linkedOriginal, policy)).ConfigureAwait(true), Is.EqualTo(42));
        Assert.That(await ExecuteAsync(NativeEmitter.Emit(linkedReplacement, policy)).ConfigureAwait(true), Is.EqualTo(44));
        Assert.That(await RunSemanticModuleJitAsync(consumer, original, policy).ConfigureAwait(true), Is.EqualTo(42));
        Assert.That(await RunSemanticModuleJitAsync(consumer, replacement, policy).ConfigureAwait(true), Is.EqualTo(44));
        Assert.That(SHA256.HashData(consumer), Is.EqualTo(hash));
    }
    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public async Task ManagedConsumerSetUsesIndependentHeapsAndUnchangedProvidersAsync(NativeRuntimePolicy policy)
    {
        string[] exports = ["Read", "TryRead", "Fail", "CollectProvider"];
        var original = NativeModuleCompiler.CreateProducer(Compile(ConsumerProducerSource, policy),
            exports, "NodeB", policy);
        var replacement = NativeModuleCompiler.CreateProducer(Compile(ConsumerProducerSource.Replace(
            "box.Data.First + 0", "box.Data.First + 1", StringComparison.Ordinal), policy),
            exports, "NodeB", policy);
        var bias = NativeModuleCompiler.CreateProducer(Compile("public int32 Bias() { return 0; }", policy),
            ["Bias"], "NodeA", policy);
        var source = ManagedConsumerSource(policy, linked: false, bias: true);
        var program = Compile(source, policy, NativeModuleCompiler.CreateReferenceSet([original, bias]),
            library: false);
        var consumer = NativeModuleCompiler.CreateConsumerSet(program, [original, bias], policy);
        var hash = SHA256.HashData(consumer);
        Assert.That(NativeModuleCompiler.InspectConsumerSet(consumer).NativeProfileVersion, Is.EqualTo("native-20"));
        Assert.That(await NativeModuleCompiler.RunConsumerSetAsync(consumer, [bias, original],
            TimeSpan.FromSeconds(10), policy).ConfigureAwait(true), Is.EqualTo(41));
        Assert.That(await NativeModuleCompiler.RunConsumerSetAsync(consumer, [replacement, bias],
            TimeSpan.FromSeconds(10), policy).ConfigureAwait(true), Is.EqualTo(42));
        var graph = NativeModuleCompiler.CreateGraphConsumer(Compile(source, policy,
            NativeModuleCompiler.CreateGraphReference([original, bias]), library: false),
            [original, bias], policy);
        var framework = new DirectoryInfo(AppContext.BaseDirectory);
        var configuration = framework.Parent?.Name
            ?? throw new InvalidOperationException("Missing test configuration directory.");
        var apphost = Path.Combine(RepositoryRoot(), "artifacts", "bin", "sources", "terrumc",
            configuration, framework.Name, "terrumc.exe");
        Assert.That(await NativeModuleCompiler.RunGraphJitAsync(graph, [bias, replacement],
            apphost, TimeSpan.FromSeconds(30), policy).ConfigureAwait(true), Is.EqualTo(42));
        Assert.That(SHA256.HashData(consumer), Is.EqualTo(hash));
    }
    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public async Task NodeOnlySemanticConsumerKeepsRootAcrossUnmanagedImportAsync(NativeRuntimePolicy policy)
    {
        const string ConsumerSource = """
            int32 Main() {
                require ManagedNode.TryCreate(Value(), out var node);
                require ManagedHeap.Collect() == 0;
                return node.Value;
            }
            """;
        var original = ModuleCompiler.CreateProducer(Compile("public int32 Value() { return 40; }", policy),
            ["Value"]);
        var replacement = ModuleCompiler.CreateProducer(Compile("public int32 Value() { return 42; }", policy),
            ["Value"]);
        var consumer = ModuleCompiler.CreateConsumer(Compile(ConsumerSource, policy,
            ModuleCompiler.CreateReference(original), library: false), original);
        Assert.That(BinaryPrimitives.ReadInt32LittleEndian(consumer.AsSpan(4, 4)), Is.EqualTo(76));
        Assert.That(ModuleCompiler.Link(consumer, replacement).Classes, Is.Empty);
        Assert.That(await ExecuteAsync(NativeEmitter.Emit(ModuleCompiler.Link(consumer, original), policy))
            .ConfigureAwait(true), Is.EqualTo(40));
        Assert.That(await ExecuteAsync(NativeEmitter.Emit(ModuleCompiler.Link(consumer, replacement), policy))
            .ConfigureAwait(true), Is.EqualTo(42));
    }
    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public void CurrentSemanticConsumerRetainsCopyOnlyBoundary(NativeRuntimePolicy policy)
    {
        var producer = ModuleCompiler.CreateProducer(Compile(ConsumerProducerSource, policy),
            ["Read", "TryRead", "Fail", "CollectProvider"]);
        var consumer = ModuleCompiler.CreateConsumer(Compile(ManagedConsumerSource(policy, linked: true,
            bias: false), policy, ModuleCompiler.CreateReference(producer), library: false), producer);
        var oldConsumer = (byte[])consumer.Clone();
        BinaryPrimitives.WriteInt32LittleEndian(oldConsumer.AsSpan(4, 4), 31);
        _ = Assert.Throws<ModuleFormatException>(() => ModuleCompiler.Link(oldConsumer, producer))!;
        using (var reader = new BinaryReader(new MemoryStream(consumer)))
        {
            reader.BaseStream.Position = sizeof(int);
            var formatVersion = reader.ReadInt32();
            reader.BaseStream.Position = 8;
            SkipString(reader);
            _ = reader.ReadByte();
            _ = reader.ReadByte();
            _ = reader.ReadBoolean();
            _ = reader.ReadInt32();
            _ = reader.ReadInt32();
            _ = reader.ReadInt32();
            Assert.That(reader.ReadInt32(), Is.EqualTo(1));
            SkipString(reader);
            _ = reader.ReadInt32();
            _ = reader.ReadBoolean();
            _ = reader.ReadBoolean();
            _ = reader.ReadBoolean();
            _ = reader.ReadBoolean();
            if (formatVersion >= 78)
            {
                _ = reader.ReadInt32();
                _ = reader.ReadBoolean();
            }
            if (reader.ReadBoolean())
            {
                SkipString(reader);
            }
            var structureFields = reader.ReadInt32();
            for (var i = 0; i < structureFields; i++)
            {
                SkipString(reader);
                reader.BaseStream.Position += 4 + 4 + 4 + 4;
            }
            Assert.That(reader.ReadInt32(), Is.EqualTo(1));
            SkipString(reader);
            reader.BaseStream.Position += 4 + 2;
            Assert.That(reader.ReadInt32(), Is.EqualTo(2));
            SkipString(reader);
            reader.BaseStream.Position += 4 + 4 + 4 + 3;
            SkipString(reader);
            var classTypeOffset = (int)reader.BaseStream.Position;
            Assert.That(ManagedClassTypes.IsManaged((PrimitiveType)reader.ReadInt32()), Is.True);
            var invalidClass = (byte[])consumer.Clone();
            BinaryPrimitives.WriteInt32LittleEndian(invalidClass.AsSpan(classTypeOffset, 4), int.MaxValue);
            _ = Assert.Throws<ModuleFormatException>(() => ModuleCompiler.Link(invalidClass, producer))!;
        }

        var scalarProducer = ModuleCompiler.CreateProducer(Compile(
            "public int32 Value() { return 42; }", policy), ["Value"]);
        Assert.That(BinaryPrimitives.ReadInt32LittleEndian(scalarProducer.AsSpan(4, 4)), Is.EqualTo(76));
        var scalarConsumer = ModuleCompiler.CreateConsumer(Compile(
            "int32 Main() { return Value(); }", policy,
            ModuleCompiler.CreateReference(scalarProducer), library: false), scalarProducer);
        Assert.That(BinaryPrimitives.ReadInt32LittleEndian(scalarConsumer.AsSpan(4, 4)), Is.EqualTo(76));
        _ = ModuleCompiler.Link(scalarConsumer, scalarProducer);
        var forged = (byte[])scalarConsumer.Clone();
        BinaryPrimitives.WriteInt32LittleEndian(forged.AsSpan(4, 4), 32);
        _ = Assert.Throws<ModuleFormatException>(() => ModuleCompiler.Link(forged, scalarProducer))!;

        const string OwningProducer = """
            [Blittable] [Layout(LayoutKind.Sequential)]
            public unique struct Token {
                public int32 Value;
                public Token(int32 value) { Value = value; }
            }
            public Token Pass(Token token) { return move token; }
            """;
        var unique = ModuleCompiler.CreateProducer(Compile(OwningProducer, policy), ["Pass"]);
        const string ManagedConsumer = """
            class Cell {
                public int32 Value;
                public Cell(int32 value) { Value = value; }
            }
            int32 Main() { return 42; }
            """;
        var invalid = Compilation.Create(SyntaxTree.Parse(ManagedConsumer),
            ModuleCompiler.CreateReference(unique), (BoundRuntimePolicy)policy);
        Assert.That(global::System.Linq.Enumerable.Any(invalid.Diagnostics,static diagnostic => diagnostic.Code == "TRM3023"),Is.True);
        var nativeUnique = NativeModuleCompiler.CreateProducer(Compile(OwningProducer, policy),
            ["Pass"], "NodeB", policy);
        var nativeBias = NativeModuleCompiler.CreateProducer(Compile(
            "public int32 Bias() { return 0; }", policy), ["Bias"], "NodeA", policy);
        var invalidSet = Compilation.Create(SyntaxTree.Parse(ManagedConsumer),
            NativeModuleCompiler.CreateReferenceSet([nativeUnique, nativeBias]), (BoundRuntimePolicy)policy);
        Assert.That(global::System.Linq.Enumerable.Any(invalidSet.Diagnostics,static diagnostic => diagnostic.Code == "TRM3023"),Is.True);
    }

    private static void SkipString(BinaryReader reader)
    {
        var length = reader.ReadInt32();
        reader.BaseStream.Position += length;
    }

    private static async Task<int> RunSemanticModuleJitAsync(byte[] consumer, byte[] producer,
        NativeRuntimePolicy policy)
    {
        var framework = new DirectoryInfo(AppContext.BaseDirectory);
        var configuration = framework.Parent?.Name
            ?? throw new InvalidOperationException("Missing test configuration directory.");
        var cli = Path.Combine(RepositoryRoot(), "artifacts", "bin", "sources", "terrumc",
            configuration, framework.Name, "terrumc.dll");
        var consumerPath = Path.Combine(TestArtifactPaths.DirectoryPath, $"semantic-consumer-{Guid.NewGuid():N}.trm");
        var producerPath = Path.Combine(TestArtifactPaths.DirectoryPath, $"semantic-producer-{Guid.NewGuid():N}.trm");
        try
        {
            await File.WriteAllBytesAsync(consumerPath, consumer).ConfigureAwait(true);
            await File.WriteAllBytesAsync(producerPath, producer).ConfigureAwait(true);
            var start = new ProcessStartInfo("dotnet") {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
            };
            foreach (var argument in new[] {
                cli, "run-module", consumerPath, producerPath, "--runtime",
                policy == NativeRuntimePolicy.FailFast ? "fail-fast" : "checked-exceptions",
            })
            {
                start.ArgumentList.Add(argument);
            }
            using var process = Process.Start(start)
                ?? throw new InvalidOperationException("Failed to start semantic module JIT.");
            var stderr = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(true);
            Assert.That(process.ExitCode is >= 0 and <= 255, Is.True, await stderr.ConfigureAwait(true));
            return process.ExitCode;
        }
        finally
        {
            File.Delete(consumerPath);
            File.Delete(producerPath);
        }
    }
}
