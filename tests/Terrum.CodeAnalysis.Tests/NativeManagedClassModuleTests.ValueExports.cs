// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Globalization;
using System.IO;
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

public sealed partial class NativeManagedClassModuleTests
{
    private const string ValueExportSource = """
        public struct PublicPair {
            public int64 First;
            public int64 Second;
            public PublicPair(int64 first, int64 second) { First = first; Second = second; }
        }
        class Leaf {
            public PublicPair Data;
            public Leaf(PublicPair value) { Data = value; }
        }
        public PublicPair Echo(PublicPair input) {
            require Leaf.TryCreate(input, out var leaf);
            return leaf.Data;
        }
        public PublicPair Make(int64 first, int64 second) {
            return new PublicPair(first, second);
        }
        public int32 CollectGarbage() { return ManagedHeap.Collect(); }
        """;

    private const string ConditionalValueSource = """
        public struct Pair {
            public int64 First;
            public int64 Second;
            public Pair(int64 first, int64 second) { First = first; Second = second; }
        }
        class Box {
            public Pair Data;
            public Box(Pair value) { Data = value; }
        }
        public Pair Make(int64 first, int64 second) { return new Pair(first, second); }
        public bool TryPair(int64 first, int64 second, int32 mode, out Pair value, out int32 count) {
            require Box.TryCreate(new Pair(first, second), out var box);
            value = box.Data;
            count = 2;
            if (mode == 2) { require false; }
            return mode == 1;
        }
        public bool TryScalar(int32 value, bool publish, out int32 result) {
            result = value;
            return publish;
        }
        public int32 FillCopy(int64 first, out Pair value) {
            require Box.TryCreate(new Pair(first, 22), out var box);
            value = box.Data;
            return 7;
        }
        public void FillVoid(out Pair value) {
            require Box.TryCreate(new Pair(18, 24), out var box);
            value = box.Data;
        }
        public int32 CollectGarbage() { return ManagedHeap.Collect(); }
        """;

    [Fact]
    public void ManagedLeafExportsExistingCompactCopyValues()
    {
        var program = Compile(ValueExportSource, NativeRuntimePolicy.FailFast);
        var producer = ModuleCompiler.CreateProducer(program, ["Echo"]);
        Assert.Equal(68,BinaryPrimitives.ReadInt32LittleEndian(producer.AsSpan(4, 4)));
        Assert.Equal("native-19", ModuleCompiler.GetNativeProfileVersion(producer, false));
        Assert.Equal("Echo", Assert.Single(ModuleCompiler.CreateReference(producer).Functions).Name);
        var native = NativeModuleCompiler.CreateProducer(program, ["Echo"], "NodeB",
            NativeRuntimePolicy.FailFast);
        Assert.Equal("native-19", NativeModuleCompiler.Inspect(native).NativeProfileVersion);
    }

    [Fact]
    public void CurrentSemanticRevisionSupportsScalarOnlyManagedLeaf()
    {
        var program = Compile("""
            class Box {
                public int32 Value;
                public Box(int32 value) { Value = value; }
            }
            public int32 Read() {
                require Box.TryCreate(42, out var box);
                return box.Value;
            }
            """, NativeRuntimePolicy.FailFast);
        var original = ModuleCompiler.CreateProducer(program, ["Read"]);
        Assert.Equal(68,BinaryPrimitives.ReadInt32LittleEndian(original.AsSpan(4, 4)));
        _ = ModuleCompiler.CreateReference(original);
        var forged = (byte[])original.Clone();
        BinaryPrimitives.WriteInt32LittleEndian(forged.AsSpan(4, 4), 31);
        _ = Assert.Throws<ModuleFormatException>(() => ModuleCompiler.CreateReference(forged));
    }

    [Theory]
    [InlineData(false, NativeRuntimePolicy.FailFast)]
    [InlineData(false, NativeRuntimePolicy.CheckedExceptions)]
    [InlineData(true, NativeRuntimePolicy.FailFast)]
    [InlineData(true, NativeRuntimePolicy.CheckedExceptions)]
    public async Task ManagedStaticScalarOwnerPreservesCurrentArtifactAndNativeGraphAsync(
        bool unique, NativeRuntimePolicy policy)
    {
        const string Source = """
            public struct Vault {
                private int32 Hidden;
                public Vault(int32 hidden) { Hidden = hidden; }
                public static int32 Read() {
                    require Leaf.TryCreate(40, out var leaf);
                    return leaf.Value;
                }
            }
            class Leaf {
                public int32 Value;
                public Leaf(int32 value) { Value = value; }
            }
            """;
        const string Consumer = "int32 Main() { return Vault.Read(); }";
        var originalSource = unique
            ? Source.Replace("public struct Vault", "public unique struct Vault", StringComparison.Ordinal)
            : Source;
        var replacementSource = originalSource.Replace("Leaf.TryCreate(40", "Leaf.TryCreate(42",
            StringComparison.Ordinal);
        var originalProgram = Compile(originalSource, policy);
        var replacementProgram = Compile(replacementSource, policy);
        var original = ModuleCompiler.CreateProducer(originalProgram, ["Vault.Read"]);
        Assert.Equal(68,BinaryPrimitives.ReadInt32LittleEndian(original.AsSpan(4, 4)));
        var reference = ModuleCompiler.CreateReference(original);
        Assert.Equal("Vault", Assert.Single(reference.Structures).Name);
        var consumer = ModuleCompiler.CreateConsumer(Compile(Consumer, policy, reference,
            library: false), original);
        var consumerHash = SHA256.HashData(consumer);
        var replacement = ModuleCompiler.CreateProducer(replacementProgram, ["Vault.Read"]);
        Assert.Equal(BinaryPrimitives.ReadInt32LittleEndian(original.AsSpan(4, 4)),
            BinaryPrimitives.ReadInt32LittleEndian(replacement.AsSpan(4, 4)));
        Assert.Equal(40, await ExecuteAsync(NativeEmitter.Emit(ModuleCompiler.Link(consumer, original), policy))
            .ConfigureAwait(true));
        Assert.Equal(42, await ExecuteAsync(NativeEmitter.Emit(ModuleCompiler.Link(consumer, replacement), policy))
            .ConfigureAwait(true));
        Assert.Equal(consumerHash, SHA256.HashData(consumer));

        var nativeOriginal = NativeModuleCompiler.CreateProducer(originalProgram, ["Vault.Read"],
            "NodeB", policy);
        var nativeConsumer = NativeModuleCompiler.CreateGraphConsumer(Compile(Consumer, policy,
            NativeModuleCompiler.CreateGraphReference([nativeOriginal]), library: false),
            [nativeOriginal], policy);
        var nativeHash = SHA256.HashData(nativeConsumer);
        var nativeReplacement = NativeModuleCompiler.CreateProducer(replacementProgram,
            ["Vault.Read"], "NodeB", policy);
        Assert.Equal(40, await NativeModuleCompiler.RunGraphAsync(nativeConsumer, [nativeOriginal],
            TimeSpan.FromSeconds(10), policy).ConfigureAwait(true));
        Assert.Equal(42, await NativeModuleCompiler.RunGraphAsync(nativeConsumer, [nativeReplacement],
            TimeSpan.FromSeconds(10), policy).ConfigureAwait(true));
        var framework = new DirectoryInfo(AppContext.BaseDirectory);
        var configuration = framework.Parent?.Name
            ?? throw new InvalidOperationException("Missing test configuration directory.");
        var apphost = Path.Combine(RepositoryRoot(), "artifacts", "bin", "sources", "terrumc",
            configuration, framework.Name, "terrumc.exe");
        Assert.Equal(42, await NativeModuleCompiler.RunGraphJitAsync(nativeConsumer, [nativeReplacement],
            apphost, TimeSpan.FromSeconds(30), policy).ConfigureAwait(true));
        Assert.Equal(nativeHash, SHA256.HashData(nativeConsumer));
    }

    [Theory]
    [InlineData(NativeRuntimePolicy.FailFast)]
    [InlineData(NativeRuntimePolicy.CheckedExceptions)]
    public async Task DeepCompactCopyValuesCrossManagedModuleAndNativeGraphAsync(NativeRuntimePolicy policy)
    {
        var source = new StringBuilder("""
            public struct Wrap0 {
                public int32 Number;
                public Wrap0(int32 number) { Number = number; }
            }

            """);
        var constructor = "new Wrap0(value)";
        for (var depth = 1; depth <= 20; depth++)
        {
            _ = source.Append("public struct Wrap").Append(depth).Append(" { public Wrap")
                .Append(depth - 1).Append(" Inner; public Wrap").Append(depth)
                .Append("(Wrap").Append(depth - 1).Append(" inner) { Inner = inner; } }")
                .AppendLine();
            constructor = $"new Wrap{depth}({constructor})";
        }
        _ = source.Append(CultureInfo.InvariantCulture, $$"""
            class Box {
                public Wrap20 Value;
                public Box(Wrap20 value) { Value = value; }
            }
            public Wrap20 Make(int32 value) { return {{constructor}}; }
            public Wrap20 Echo(Wrap20 value) {
                require Box.TryCreate(value, out var box);
                return box.Value;
            }
            public int32 CollectGarbage() { return ManagedHeap.Collect(); }
            """);
        var fieldPath = string.Concat(Enumerable.Repeat(".Inner", 20));
        var consumerSource = $$"""
            int32 Main() {
                Wrap20 value = Echo(Make(42));
                require CollectGarbage() == 1;
                return value{{fieldPath}}.Number;
            }
            """;
        var program = Compile(source.ToString(), policy);
        string[] exports = ["Make", "Echo", "CollectGarbage"];
        var producer = ModuleCompiler.CreateProducer(program, exports);
        Assert.Equal(68,BinaryPrimitives.ReadInt32LittleEndian(producer.AsSpan(4, 4)));
        var reference = ModuleCompiler.CreateReference(producer);
        Assert.Equal(21, reference.Structures.Length);
        Assert.All(reference.Structures, static structure => Assert.Equal(4, structure.Size));
        var consumer = ModuleCompiler.CreateConsumer(Compile(consumerSource, policy,
            reference, library: false), producer);
        Assert.Equal(42, await ExecuteAsync(NativeEmitter.Emit(ModuleCompiler.Link(consumer, producer), policy))
            .ConfigureAwait(true));
        var downgraded = (byte[])producer.Clone();
        BinaryPrimitives.WriteInt32LittleEndian(downgraded.AsSpan(4, 4), 30);
        _ = Assert.Throws<ModuleFormatException>(() => ModuleCompiler.CreateReference(downgraded));

        var nativeProducer = NativeModuleCompiler.CreateProducer(program, exports, "NodeB", policy);
        var nativeConsumer = NativeModuleCompiler.CreateGraphConsumer(Compile(consumerSource, policy,
            NativeModuleCompiler.CreateGraphReference([nativeProducer]), library: false),
            [nativeProducer], policy);
        Assert.Equal(42, await NativeModuleCompiler.RunGraphAsync(nativeConsumer, [nativeProducer],
            TimeSpan.FromSeconds(10), policy).ConfigureAwait(true));
        var framework = new DirectoryInfo(AppContext.BaseDirectory);
        var configuration = framework.Parent?.Name
            ?? throw new InvalidOperationException("Missing test configuration directory.");
        var apphost = Path.Combine(RepositoryRoot(), "artifacts", "bin", "sources", "terrumc",
            configuration, framework.Name, "terrumc.exe");
        Assert.Equal(42, await NativeModuleCompiler.RunGraphJitAsync(nativeConsumer, [nativeProducer],
            apphost, TimeSpan.FromSeconds(30), policy).ConfigureAwait(true));
    }

    [Theory]
    [InlineData(NativeRuntimePolicy.FailFast)]
    [InlineData(NativeRuntimePolicy.CheckedExceptions)]
    public async Task ManagedCompactValueReplacementKeepsPublicOriginAndPrivateClassStateAsync(
        NativeRuntimePolicy policy)
    {
        var replacementSource = ValueExportSource
            .Replace("class Leaf {", """
                struct Stamp {
                    public int64 Amount;
                    public Stamp(int64 amount) { Amount = amount; }
                }
                class Leaf {
                """, StringComparison.Ordinal)
            .Replace("public PublicPair Data;", "public PublicPair Data; public Stamp Marker;",
                StringComparison.Ordinal)
            .Replace("Data = value;", "Data = value; Marker = new Stamp(1);", StringComparison.Ordinal)
            .Replace("return leaf.Data;", """
                require ManagedNode.TryCreate(1, out var node);
                node.SetNext(node);
                return new PublicPair(leaf.Data.First + leaf.Marker.Amount, leaf.Data.Second);
                """,
                StringComparison.Ordinal);
        const string Consumer = """
            struct Stamp {
                public int32 Unrelated;
                public Stamp(int32 value) { Unrelated = value; }
            }
            int32 Main() {
                Stamp unrelated = new Stamp(0);
                PublicPair value = Echo(Make(39, 2));
                return int32(value.First + value.Second) + unrelated.Unrelated + CollectGarbage();
            }
            """;
        var originalProgram = Compile(ValueExportSource, policy);
        string[] exports = ["Echo", "Make", "CollectGarbage"];
        var original = ModuleCompiler.CreateProducer(originalProgram, exports);
        var consumer = ModuleCompiler.CreateConsumer(Compile(Consumer, policy,
            ModuleCompiler.CreateReference(original), library: false), original);
        var consumerHash = SHA256.HashData(consumer);
        var replacementProgram = Compile(replacementSource, policy);
        var replacement = ModuleCompiler.CreateProducer(replacementProgram, exports);
        Assert.Equal(68,BinaryPrimitives.ReadInt32LittleEndian(replacement.AsSpan(4, 4)));
        var reference = ModuleCompiler.CreateReference(replacement);
        Assert.Equal("PublicPair", Assert.Single(reference.Structures).Name);
        var downgraded = (byte[])replacement.Clone();
        BinaryPrimitives.WriteInt32LittleEndian(downgraded.AsSpan(4, 4), 30);
        _ = Assert.Throws<ModuleFormatException>(() => ModuleCompiler.CreateReference(downgraded));
        Assert.Equal(42, await ExecuteAsync(NativeEmitter.Emit(ModuleCompiler.Link(consumer, original), policy))
            .ConfigureAwait(true));
        var linked = ModuleCompiler.Link(consumer, replacement);
        Assert.Contains(linked.Structures, structure => structure.Name == "$producer.Stamp");
        Assert.Contains(linked.Structures, structure => structure.Name == "Stamp");
        Assert.Equal(44, await ExecuteAsync(NativeEmitter.Emit(linked, policy)).ConfigureAwait(true));
        Assert.Equal(consumerHash, SHA256.HashData(consumer));

        var nativeOriginal = NativeModuleCompiler.CreateProducer(originalProgram, exports, "NodeB", policy);
        var nativeConsumer = NativeModuleCompiler.CreateGraphConsumer(Compile(Consumer, policy,
            NativeModuleCompiler.CreateGraphReference([nativeOriginal]), library: false),
            [nativeOriginal], policy);
        var nativeHash = SHA256.HashData(nativeConsumer);
        var nativeReplacement = NativeModuleCompiler.CreateProducer(replacementProgram,
            exports, "NodeB", policy);
        Assert.Equal(42, await NativeModuleCompiler.RunGraphAsync(nativeConsumer, [nativeOriginal],
            TimeSpan.FromSeconds(10), policy).ConfigureAwait(true));
        Assert.Equal(44, await NativeModuleCompiler.RunGraphAsync(nativeConsumer, [nativeReplacement],
            TimeSpan.FromSeconds(10), policy).ConfigureAwait(true));
        var framework = new DirectoryInfo(AppContext.BaseDirectory);
        var configuration = framework.Parent?.Name
            ?? throw new InvalidOperationException("Missing test configuration directory.");
        var apphost = Path.Combine(RepositoryRoot(), "artifacts", "bin", "sources", "terrumc",
            configuration, framework.Name, "terrumc.exe");
        Assert.Equal(44, await NativeModuleCompiler.RunGraphJitAsync(nativeConsumer, [nativeReplacement],
            apphost, TimeSpan.FromSeconds(30), policy).ConfigureAwait(true));
        Assert.Equal(nativeHash, SHA256.HashData(nativeConsumer));
    }

    [Theory]
    [InlineData(NativeRuntimePolicy.FailFast)]
    [InlineData(NativeRuntimePolicy.CheckedExceptions)]
    public async Task ManagedCompactConditionalOutputsRespectPublicationAcrossNativeImportsAsync(
        NativeRuntimePolicy policy)
    {
        const string originalSource = """
            public struct Pair {
                public int64 First;
                public int64 Second;
                public Pair(int64 first, int64 second) { First = first; Second = second; }
            }
            public Pair Make(int64 first, int64 second) { return new Pair(first, second); }
            public bool TryPair(int64 first, int64 second, int32 mode, out Pair value, out int32 count) {
                if (mode != 1) { return false; }
                value = new Pair(first, second);
                count = 2;
                return true;
            }
            public bool TryScalar(int32 value, bool publish, out int32 result) {
                if (!publish) { return false; }
                result = value;
                return true;
            }
            public int32 FillCopy(int64 first, out Pair value) {
                value = new Pair(first, 22);
                return 7;
            }
            public void FillVoid(out Pair value) { value = new Pair(18, 24); }
            public int32 CollectGarbage() { return 0; }
            """;
        var consumerSource = """
            int32 Main() {
                mut Pair pair = Make(7, 9);
                mut int32 count = 7;
                if (TryPair(1, 2, 0, out pair, out count)) { return 1; }
                if (CollectGarbage() != 1) { return 3; }
                require TryPair(40, 2, 1, out pair, out count);
                if (pair.First != 40 || pair.Second != 2 || count != 2) { return 4; }
                if (CollectGarbage() != 1) { return 5; }
                mut int32 number = 5;
                if (TryScalar(42, false, out number)) { return 6; }
                require TryScalar(42, true, out number);
            """ + (policy == NativeRuntimePolicy.CheckedExceptions ? """
                unsafe {
                    try { TryPair(1, 2, 2, out pair, out count); return 7; }
                    catch {
                        if (CollectGarbage() != 1) { return 9; }
                    }
                }
            """ : "") + """
                int32 status = FillCopy(20, out var copy);
                FillVoid(out var final);
                if (status != 7 || copy.First != 20 || copy.Second != 22
                    || final.First != 18 || final.Second != 24) { return 10; }
                return number;
            }
            """;
        string[] exports = ["Make", "TryPair", "TryScalar", "FillCopy", "FillVoid", "CollectGarbage"];
        var originalProgram = Compile(originalSource, policy);
        var originalProducer = ModuleCompiler.CreateProducer(originalProgram, exports);
        var program = Compile(ConditionalValueSource, policy);
        var producer = ModuleCompiler.CreateProducer(program, exports);
        Assert.Equal(68,BinaryPrimitives.ReadInt32LittleEndian(producer.AsSpan(4, 4)));
        var consumer = ModuleCompiler.CreateConsumer(Compile(consumerSource, policy,
            ModuleCompiler.CreateReference(originalProducer), library: false), originalProducer);
        var consumerHash = SHA256.HashData(consumer);
        Assert.Equal(42, await ExecuteAsync(NativeEmitter.Emit(ModuleCompiler.Link(consumer, producer), policy))
            .ConfigureAwait(true));
        Assert.Equal(consumerHash, SHA256.HashData(consumer));

        var originalNativeProducer = NativeModuleCompiler.CreateProducer(originalProgram, exports, "NodeB", policy);
        var nativeConsumer = NativeModuleCompiler.CreateGraphConsumer(Compile(consumerSource, policy,
            NativeModuleCompiler.CreateGraphReference([originalNativeProducer]), library: false),
            [originalNativeProducer], policy);
        var nativeConsumerHash = SHA256.HashData(nativeConsumer);
        var nativeProducer = NativeModuleCompiler.CreateProducer(program, exports, "NodeB", policy);
        Assert.Equal(42, await NativeModuleCompiler.RunGraphAsync(nativeConsumer, [nativeProducer],
            TimeSpan.FromSeconds(10), policy).ConfigureAwait(true));
        var framework = new DirectoryInfo(AppContext.BaseDirectory);
        var configuration = framework.Parent?.Name
            ?? throw new InvalidOperationException("Missing test configuration directory.");
        var apphost = Path.Combine(RepositoryRoot(), "artifacts", "bin", "sources", "terrumc",
            configuration, framework.Name, "terrumc.exe");
        Assert.Equal(42, await NativeModuleCompiler.RunGraphJitAsync(nativeConsumer, [nativeProducer],
            apphost, TimeSpan.FromSeconds(30), policy).ConfigureAwait(true));
        Assert.Equal(nativeConsumerHash, SHA256.HashData(nativeConsumer));
    }

    [Fact]
    public async Task NonBooleanCopyOutputsRemainUnpublishedOnCheckedExceptionAsync()
    {
        const string providerSource = """
            public struct Pair {
                public int64 First;
                public int64 Second;
                public Pair(int64 first, int64 second) { First = first; Second = second; }
            }
            public int32 FillCopyThenFail(bool fail, out Pair result) {
                result = new Pair(40, 2);
                if (fail) { require false; }
                return 7;
            }
            public void FillVoidThenFail(bool fail, out Pair result) {
                result = new Pair(18, 24);
                if (fail) { require false; }
            }
            """;
        const string consumerSource = """
            int32 CatchCopy(out Pair result) {
                mut int32 collected = 0;
                unsafe {
                    try {
                        try {
                            require ManagedNode.TryCreate(40, out var departed);
                            FillCopyThenFail(true, out var failed);
                        }
                        finally {
                            collected = ManagedHeap.Collect();
                        }
                    }
                    catch {
                        if (collected != 1) {
                            FillCopyThenFail(false, out result);
                            return 1;
                        }
                        if (ManagedHeap.Collect() != 0) {
                            FillCopyThenFail(false, out result);
                            return 2;
                        }
                        if (ManagedHeap.Collect() != 0) {
                            FillCopyThenFail(false, out result);
                            return 3;
                        }
                        FillCopyThenFail(false, out result);
                        return 4;
                    }
                }
                FillCopyThenFail(false, out result);
                return 5;
            }
            int32 CatchVoid(out Pair result) {
                mut int32 collected = 0;
                unsafe {
                    try {
                        try {
                            require ManagedNode.TryCreate(18, out var departed);
                            FillVoidThenFail(true, out var failed);
                        }
                        finally {
                            collected = ManagedHeap.Collect();
                        }
                    }
                    catch {
                        if (collected != 1) {
                            FillVoidThenFail(false, out result);
                            return 11;
                        }
                        if (ManagedHeap.Collect() != 0) {
                            FillVoidThenFail(false, out result);
                            return 12;
                        }
                        if (ManagedHeap.Collect() != 0) {
                            FillVoidThenFail(false, out result);
                            return 13;
                        }
                        FillVoidThenFail(false, out result);
                        return 14;
                    }
                }
                FillVoidThenFail(false, out result);
                return 15;
            }
            int32 Main() {
                unsafe {
                    try {
                        int32 copyStatus = CatchCopy(out var copy);
                        int32 voidStatus = CatchVoid(out var voidResult);
                        if (copyStatus != 4) { return copyStatus; }
                        if (copy.First != 40 || copy.Second != 2) { return 5; }
                        if (voidStatus != 14) { return voidStatus; }
                        if (voidResult.First != 18 || voidResult.Second != 24) { return 6; }
                        return 42;
                    }
                    catch { return 3; }
                }
            }
            """;
        const NativeRuntimePolicy policy = NativeRuntimePolicy.CheckedExceptions;
        string[] exports = ["FillCopyThenFail", "FillVoidThenFail"];
        var providerProgram = Compile(providerSource, policy);
        var producer = ModuleCompiler.CreateProducer(providerProgram, exports);
        var invalidCopyConsumer = Compilation.Create(SyntaxTree.Parse("""
            int32 ReadCopyAfterFailure(out Pair copy) {
                unsafe {
                    try { FillCopyThenFail(true, out copy); }
                    catch { return int32(copy.First); }
                }
                FillCopyThenFail(false, out copy);
                return 0;
            }
            int32 Main() { return 0; }
            """), ModuleCompiler.CreateReference(producer), (BoundRuntimePolicy)policy);
        Assert.Contains(invalidCopyConsumer.Diagnostics, static diagnostic => diagnostic.Code == "TRM3016");
        var invalidVoidConsumer = Compilation.Create(SyntaxTree.Parse("""
            int32 ReadVoidAfterFailure(out Pair value) {
                unsafe {
                    try { FillVoidThenFail(true, out value); }
                    catch { return int32(value.First); }
                }
                FillVoidThenFail(false, out value);
                return 0;
            }
            int32 Main() { return 0; }
            """), ModuleCompiler.CreateReference(producer), (BoundRuntimePolicy)policy);
        Assert.Contains(invalidVoidConsumer.Diagnostics, static diagnostic => diagnostic.Code == "TRM3016");

        var consumer = ModuleCompiler.CreateConsumer(Compile(consumerSource, policy,
            ModuleCompiler.CreateReference(producer), library: false), producer);
        var linked = ModuleCompiler.Link(consumer, producer);
        Assert.Equal(42, NativeJit.Execute(linked, policy));
        Assert.Equal(42, await ExecuteAsync(NativeEmitter.Emit(linked, policy)).ConfigureAwait(true));

        var nativeProducer = NativeModuleCompiler.CreateProducer(providerProgram, exports,
            "CopyOutputFailureProvider", policy);
        var nativeConsumer = NativeModuleCompiler.CreateGraphConsumer(Compile(consumerSource, policy,
            NativeModuleCompiler.CreateGraphReference([nativeProducer]), library: false),
            [nativeProducer], policy);
        Assert.Equal(42, await NativeModuleCompiler.RunGraphAsync(nativeConsumer, [nativeProducer],
            TimeSpan.FromSeconds(10), policy).ConfigureAwait(true));
        var framework = new DirectoryInfo(AppContext.BaseDirectory);
        var configuration = framework.Parent?.Name
            ?? throw new InvalidOperationException("Missing test configuration directory.");
        var apphost = Path.Combine(RepositoryRoot(), "artifacts", "bin", "sources", "terrumc",
            configuration, framework.Name, "terrumc.exe");
        Assert.Equal(42, await NativeModuleCompiler.RunGraphJitAsync(nativeConsumer, [nativeProducer],
            apphost, TimeSpan.FromSeconds(30), policy).ConfigureAwait(true));
    }

    [Theory]
    [InlineData(NativeRuntimePolicy.FailFast)]
    [InlineData(NativeRuntimePolicy.CheckedExceptions)]
    public async Task NamespacedNestedCopyValuesCrossManagedNativeGraphWithoutPrivateTypeLeakAsync(
        NativeRuntimePolicy policy)
    {
        const string Source = """
            namespace Values;
            public struct Word {
                public int32 Number;
                public Word(int32 number) { Number = number; }
            }
            public struct Nested {
                public Word First;
                public int64 Last;
                public Nested(Word first, int64 last) { First = first; Last = last; }
            }
            class Cell {
                public Nested Data;
                public Cell(Nested value) { Data = value; }
            }
            public Nested Make(int32 first, int64 last) {
                return new Nested(new Word(first), last);
            }
            public Nested Echo(Nested input) {
                require Cell.TryCreate(input, out var cell);
                return cell.Data;
            }
            """;
        const string Consumer = """
            using Values;
            struct Cell {
                public int32 Unrelated;
                public Cell(int32 value) { Unrelated = value; }
            }
            int32 Main() {
                Cell unrelated = new Cell(0);
                Nested value = Echo(Make(40, 2));
                return value.First.Number + int32(value.Last) + unrelated.Unrelated;
            }
            """;
        string[] exports = ["Values.Make", "Values.Echo"];
        var program = Compile(Source, policy);
        var producer = ModuleCompiler.CreateProducer(program, exports);
        Assert.Equal(["Values.Word", "Values.Nested"],
            ModuleCompiler.CreateReference(producer).Structures.Select(static structure => structure.Name));
        var consumer = ModuleCompiler.CreateConsumer(Compile(Consumer, policy,
            ModuleCompiler.CreateReference(producer), library: false), producer);
        Assert.Equal(42, await ExecuteAsync(NativeEmitter.Emit(ModuleCompiler.Link(consumer, producer), policy))
            .ConfigureAwait(true));
        var nativeProducer = NativeModuleCompiler.CreateProducer(program, exports, "NodeB", policy);
        var nativeConsumer = NativeModuleCompiler.CreateGraphConsumer(Compile(Consumer, policy,
            NativeModuleCompiler.CreateGraphReference([nativeProducer]), library: false),
            [nativeProducer], policy);
        Assert.Equal(42, await NativeModuleCompiler.RunGraphAsync(nativeConsumer, [nativeProducer],
            TimeSpan.FromSeconds(10), policy).ConfigureAwait(true));
        var framework = new DirectoryInfo(AppContext.BaseDirectory);
        var configuration = framework.Parent?.Name
            ?? throw new InvalidOperationException("Missing test configuration directory.");
        var apphost = Path.Combine(RepositoryRoot(), "artifacts", "bin", "sources", "terrumc",
            configuration, framework.Name, "terrumc.exe");
        Assert.Equal(42, await NativeModuleCompiler.RunGraphJitAsync(nativeConsumer, [nativeProducer],
            apphost, TimeSpan.FromSeconds(30), policy).ConfigureAwait(true));
    }

    [Theory]
    [InlineData(NativeRuntimePolicy.FailFast)]
    [InlineData(NativeRuntimePolicy.CheckedExceptions)]
    public async Task ManagedCopyContractSampleReplacesUnchangedConsumerAsync(NativeRuntimePolicy policy)
    {
        var directory = Path.Combine(RepositoryRoot(), "samples", "native", "graph");
        var originalSource = await File.ReadAllTextAsync(Path.Combine(directory,
            "copy-contract-original.terrum")).ConfigureAwait(true);
        var managedSource = await File.ReadAllTextAsync(Path.Combine(directory,
            "copy-contract-managed.terrum")).ConfigureAwait(true);
        var consumerSource = await File.ReadAllTextAsync(Path.Combine(directory,
            "copy-contract-consumer.terrum")).ConfigureAwait(true);
        string[] exports = ["ReadSnapshot", "TryReadSnapshot", "ReadTotal"];
        var originalProgram = Compile(originalSource, policy);
        var managedProgram = Compile(managedSource, policy);
        var original = ModuleCompiler.CreateProducer(originalProgram, exports);
        var consumer = ModuleCompiler.CreateConsumer(Compile(consumerSource, policy,
            ModuleCompiler.CreateReference(original), library: false), original);
        var hash = SHA256.HashData(consumer);
        var replacement = ModuleCompiler.CreateProducer(managedProgram, exports);
        Assert.Equal(68,BinaryPrimitives.ReadInt32LittleEndian(replacement.AsSpan(4, 4)));
        var publicStructure = Assert.Single(ModuleCompiler.CreateReference(replacement).Structures);
        Assert.Equal("Snapshot", publicStructure.Name);
        Assert.Equal(12, publicStructure.Size);
        Assert.Equal(40, await ExecuteAsync(NativeEmitter.Emit(ModuleCompiler.Link(consumer, original), policy))
            .ConfigureAwait(true));
        Assert.Equal(42, await ExecuteAsync(NativeEmitter.Emit(ModuleCompiler.Link(consumer, replacement), policy))
            .ConfigureAwait(true));
        Assert.Equal(hash, SHA256.HashData(consumer));

        var nativeOriginal = NativeModuleCompiler.CreateProducer(originalProgram, exports, "NodeB", policy);
        var nativeConsumer = NativeModuleCompiler.CreateGraphConsumer(Compile(consumerSource, policy,
            NativeModuleCompiler.CreateGraphReference([nativeOriginal]), library: false),
            [nativeOriginal], policy);
        var nativeHash = SHA256.HashData(nativeConsumer);
        var nativeReplacement = NativeModuleCompiler.CreateProducer(managedProgram, exports, "NodeB", policy);
        Assert.Equal(40, await NativeModuleCompiler.RunGraphAsync(nativeConsumer, [nativeOriginal],
            TimeSpan.FromSeconds(10), policy).ConfigureAwait(true));
        Assert.Equal(42, await NativeModuleCompiler.RunGraphAsync(nativeConsumer, [nativeReplacement],
            TimeSpan.FromSeconds(10), policy).ConfigureAwait(true));
        var framework = new DirectoryInfo(AppContext.BaseDirectory);
        var configuration = framework.Parent?.Name
            ?? throw new InvalidOperationException("Missing test configuration directory.");
        var apphost = Path.Combine(RepositoryRoot(), "artifacts", "bin", "sources", "terrumc",
            configuration, framework.Name, "terrumc.exe");
        Assert.Equal(42, await NativeModuleCompiler.RunGraphJitAsync(nativeConsumer, [nativeReplacement],
            apphost, TimeSpan.FromSeconds(30), policy).ConfigureAwait(true));
        Assert.Equal(nativeHash, SHA256.HashData(nativeConsumer));
    }

    [Theory]
    [InlineData("""
        public unique struct Owned {
            public int32 Number;
            public Owned(int32 number) { Number = number; }
        }
        public Owned Leak(Owned value) { return move value; }
        """)]
    [InlineData("""
        public unique struct Owned {
            public OwnedInt32Buffer Words;
            public Owned(OwnedInt32Buffer words) { Words = move words; }
        }
        public Owned Leak(Owned value) { return move value; }
        """)]
    [InlineData("""
        public struct Hidden {
            private int32 Number;
            public Hidden(int32 number) { Number = number; }
        }
        public Hidden Leak(Hidden value) { return value; }
        """)]
    public void ManagedValueExportsRejectUniqueAndPrivateLayouts(string declaration)
    {
        var program = Compile(ValueExportSource + declaration, NativeRuntimePolicy.FailFast);
        _ = Assert.Throws<ModuleContractException>(() => ModuleCompiler.CreateProducer(program, ["Leak"]));
        _ = Assert.Throws<ModuleContractException>(() => NativeModuleCompiler.CreateProducer(program,
            ["Leak"], "NodeB", NativeRuntimePolicy.FailFast));
    }
}
