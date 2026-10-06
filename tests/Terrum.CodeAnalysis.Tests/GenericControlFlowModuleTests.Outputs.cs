// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed partial class GenericControlFlowModuleTests
{
    private const string ScalarProducer = """
        [NoThrow] T Choose<T>(T left, T right, bool first) where T : [Copy] {
            if (first) { return left; }
            return right;
        }
        [NoThrow] bool TryCopy<T>(T value, out T result) where T : [Copy] {
            result = value;
            return true;
        }
        public int32 PowerProbe() {
            int64 first = Choose<int64>(int64(40), int64(0), true);
            require TryCopy(first, out var result);
            return int32(Choose(result, int64(2), true) + 2);
        }
        """;

    private const string OutputProducer = """
        struct Pair {
            public int64 First;
            public int64 Second;
            public Pair(int64 first, int64 second) { First = first; Second = second; }
        }
        [NoThrow] bool TryCopy<T>(T value, bool publish, out T result) where T : [Copy] {
            result = value;
            return publish;
        }
        [NoThrow] int32 Fill<T>(T value, int32 marker, out T result, out T duplicate) where T : [Copy] {
            result = value;
            duplicate = value;
            if (marker != 42) { return 0; }
            return 7;
        }
        [NoThrow] void FillVoid<T>(T value, out T result) where T : [Copy] {
            result = value;
        }
        [NoThrow] int32 FillInit<T>(T value, int32 marker, init ref<T> result) where T : [Copy] {
            result = value;
            if (marker != 42) { return 0; }
            return 7;
        }
        [NoThrow] int32 FillFloating<T>(T value, int32 status, out T result,
            init ref<T> initialized) where T : [Copy] {
            result = value;
            initialized = value;
            return status;
        }
        [NoThrow] void FillInitVoid<T>(T value, init ref<T> result) where T : [Copy] {
            result = value;
        }
        [NoThrow] int32 FillMixed<T, U>(T value, U second, int32 status, out T result,
            init ref<U> initialized, out int32 count, init ref<float64> score)
            where T : [Copy] where U : [Copy] {
            result = value;
            initialized = second;
            count = 2;
            score = float64(3.5);
            return status;
        }
        [NoThrow] int32 Count<T>(T value, int32 count) where T : [Copy] { return count; }
        public int32 PowerProbe() {
            if (TryCopy(new Pair(1, 2), false, out var missing)) { return 1; }
            require TryCopy(new Pair(40, 2), true, out var result);
            int32 status = Fill(result, 42, out var copied, out var duplicate);
            FillVoid(copied, out var copiedAgain);
            int32 initStatus = FillInit(result, 42, init var initialized);
            FillInitVoid(copied, init var initializedAgain);
            int32 floatingStatus = FillFloating(float32(40.5), 0,
                out var floatingCopy, init var floatingInitialized);
            mut Pair mixed = new Pair(0, 0);
            int32 mixedStatus = FillMixed(result, int64(42), 7, out mixed,
                init var initializedMixed, out var mixedCount, init var mixedScore);
            if (status != 7 || duplicate.First != 40 || duplicate.Second != 2
                || copiedAgain.First != 40 || copiedAgain.Second != 2
                || initStatus != 7 || initialized.First != 40 || initialized.Second != 2
                || initializedAgain.First != 40 || initializedAgain.Second != 2
                || floatingStatus != 0 || floatingCopy != float32(40.5)
                || floatingInitialized != float32(40.5)
                || mixedStatus != 7 || mixed.First != 40 || mixed.Second != 2
                || initializedMixed != 42 || mixedCount != 2 || mixedScore != float64(3.5)) { return 2; }
            return int32(result.First + result.Second) + Count(result, 0);
        }
        """;

    [Theory]
    [InlineData(NativeRuntimePolicy.FailFast, false)]
    [InlineData(NativeRuntimePolicy.FailFast, true)]
    [InlineData(NativeRuntimePolicy.CheckedExceptions, false)]
    [InlineData(NativeRuntimePolicy.CheckedExceptions, true)]
    public async Task GenericOutputSpecializationsPersistWithoutPublicWitnessesOrNewWireFormatAsync(
        NativeRuntimePolicy policy, bool infer)
    {
        var source = infer ? OutputProducer : OutputProducer
            .Replace("TryCopy(", "TryCopy<Pair>(", StringComparison.Ordinal)
            .Replace("status = Fill(", "status = Fill<Pair>(", StringComparison.Ordinal)
            .Replace("FillVoid(", "FillVoid<Pair>(", StringComparison.Ordinal)
            .Replace("FillInit(", "FillInit<Pair>(", StringComparison.Ordinal)
            .Replace("FillFloating(", "FillFloating<float32>(", StringComparison.Ordinal)
            .Replace("FillInitVoid(", "FillInitVoid<Pair>(", StringComparison.Ordinal)
            .Replace("FillMixed(", "FillMixed<Pair, int64>(", StringComparison.Ordinal)
            .Replace("Count(", "Count<Pair>(", StringComparison.Ordinal);
        var original = ModuleCompiler.CreateProducer(Compile(Stub, policy), ["PowerProbe"]);
        var consumer = ModuleCompiler.CreateConsumer(Compile("""
            struct Pair {
                public int32 Other;
                public Pair(int32 other) { Other = other; }
            }
            int32 Main() {
                Pair unrelated = new Pair(1);
                return PowerProbe() + unrelated.Other - 1;
            }
            """, policy, ModuleCompiler.CreateReference(original), library: false), original);
        var hash = SHA256.HashData(consumer);
        var replacement = ModuleCompiler.CreateProducer(Compile(source, policy), ["PowerProbe"]);
        Assert.Equal(59, BinaryPrimitives.ReadInt32LittleEndian(replacement.AsSpan(4, 4)));
        var reference = ModuleCompiler.CreateReference(replacement);
        Assert.Empty(reference.Structures);
        Assert.Equal("PowerProbe", Assert.Single(reference.Functions).Name);
        var linked = ModuleCompiler.Link(consumer, replacement);
        Assert.Equal(["Pair", "$producer.Pair"], linked.Structures.Select(static structure => structure.Name));
        var fill = Assert.Single(linked.Functions,
            static function => function.Name == "$producer.Fill<Pair>");
        Assert.Equal(PrimitiveType.Int32, fill.ReturnType);
        Assert.True(fill.Variables[2].IsOut);
        Assert.True(fill.Variables[3].IsOut);
        var fillVoid = Assert.Single(linked.Functions,
            static function => function.Name == "$producer.FillVoid<Pair>");
        Assert.Equal(PrimitiveType.Void, fillVoid.ReturnType);
        Assert.True(fillVoid.Variables[1].IsOut);
        var fillInit = Assert.Single(linked.Functions,
            static function => function.Name == "$producer.FillInit<Pair>");
        Assert.Equal(PrimitiveType.Int32, fillInit.ReturnType);
        Assert.True(fillInit.Variables[2].IsInit);
        var fillFloating = Assert.Single(linked.Functions,
            static function => function.Name == "$producer.FillFloating<float32>");
        Assert.Equal(PrimitiveType.Int32, fillFloating.ReturnType);
        Assert.True(fillFloating.Variables[2].IsOut);
        Assert.False(fillFloating.Variables[2].IsInit);
        Assert.True(fillFloating.Variables[3].IsInit);
        var fillInitVoid = Assert.Single(linked.Functions,
            static function => function.Name == "$producer.FillInitVoid<Pair>");
        Assert.Equal(PrimitiveType.Void, fillInitVoid.ReturnType);
        Assert.True(fillInitVoid.Variables[1].IsInit);
        var fillMixed = Assert.Single(linked.Functions,
            static function => function.Name == "$producer.FillMixed<Pair, int64>");
        Assert.Equal(PrimitiveType.Int32, fillMixed.ReturnType);
        Assert.True(fillMixed.Variables[3].IsOut);
        Assert.False(fillMixed.Variables[3].IsInit);
        Assert.True(fillMixed.Variables[4].IsInit);
        Assert.True(fillMixed.Variables[5].IsOut);
        Assert.False(fillMixed.Variables[5].IsInit);
        Assert.True(fillMixed.Variables[6].IsOut);
        Assert.True(fillMixed.Variables[6].IsInit);
        var helper = Assert.Single(linked.Functions,
            static function => function.Name == "$producer.TryCopy<Pair>");
        Assert.Equal(PrimitiveType.Bool, helper.ReturnType);
        Assert.True(helper.Variables[2].IsOut);
        Assert.Equal((PrimitiveType)101, helper.Variables[2].Type);
        Assert.Equal(42, await ExecuteAsync(NativeEmitter.Emit(linked, policy)).ConfigureAwait(true));
        Assert.Equal(hash, SHA256.HashData(consumer));

        var nativeOriginal = NativeModuleCompiler.CreateProducer(Compile(Stub, policy), ["PowerProbe"], "NodeB", policy);
        var middle = NativeModuleCompiler.CreateGraphProducer(Compile(Middle, policy,
            NativeModuleCompiler.CreateGraphReference([nativeOriginal])), ["Middle"], "NodeA", [nativeOriginal], policy);
        var root = NativeModuleCompiler.CreateGraphConsumer(Compile(Root, policy,
            NativeModuleCompiler.CreateGraphReference([middle]), library: false), [middle], policy);
        var rootHash = SHA256.HashData(root);
        var middleHash = SHA256.HashData(middle);
        var nativeReplacement = NativeModuleCompiler.CreateProducer(Compile(source, policy),
            ["PowerProbe"], "NodeB", policy);
        Assert.Equal(44, await NativeModuleCompiler.RunGraphAsync(root, [nativeReplacement, middle],
            TimeSpan.FromSeconds(10), policy).ConfigureAwait(true));
        Assert.Equal(44, await NativeModuleCompiler.RunGraphJitAsync(root, [middle, nativeReplacement],
            Apphost(), TimeSpan.FromSeconds(30), policy).ConfigureAwait(true));
        Assert.Equal(rootHash, SHA256.HashData(root));
        Assert.Equal(middleHash, SHA256.HashData(middle));
    }

    [Theory]
    [InlineData(NativeRuntimePolicy.FailFast)]
    [InlineData(NativeRuntimePolicy.CheckedExceptions)]
    public async Task CopyOutVarDestinationsPublishForNonBooleanAndVoidCallsAsync(
        NativeRuntimePolicy policy)
    {
        const string providerSource = """
            public struct Pair {
                public int64 First;
                public int64 Second;
                public Pair(int64 first, int64 second) { First = first; Second = second; }
            }
            public int32 Fill(out Pair result) {
                result = new Pair(20, 22);
                return 7;
            }
            public void FillVoid(out Pair result) {
                result = new Pair(18, 24);
            }
            """;
        const string originalProviderSource = """
            public struct Pair {
                public int64 First;
                public int64 Second;
                public Pair(int64 first, int64 second) { First = first; Second = second; }
            }
            public int32 Fill(out Pair result) {
                result = new Pair(0, 0);
                return 0;
            }
            public void FillVoid(out Pair result) { result = new Pair(0, 0); }
            """;
        const string consumerSource = """
            int32 Main() {
                int32 status = Fill(out var first);
                FillVoid(out var second);
                return int32(first.First + first.Second + second.First + second.Second) + status - 7;
            }
            """;
        var originalProviderProgram = Compile(originalProviderSource, policy);
        var originalProducer = ModuleCompiler.CreateProducer(originalProviderProgram, ["Fill", "FillVoid"]);
        var consumerProgram = Compile(consumerSource, policy,
            ModuleCompiler.CreateReference(originalProducer), library: false);
        var consumer = ModuleCompiler.CreateConsumer(consumerProgram, originalProducer);
        var consumerHash = SHA256.HashData(consumer);
        var providerProgram = Compile(providerSource, policy);
        var producer = ModuleCompiler.CreateProducer(providerProgram, ["Fill", "FillVoid"]);
        var linked = ModuleCompiler.Link(consumer, producer);
        Assert.Equal(84, NativeJit.Execute(linked, policy));
        Assert.Equal(84, await ExecuteAsync(NativeEmitter.Emit(linked, policy)).ConfigureAwait(true));
        Assert.Equal(consumerHash, SHA256.HashData(consumer));

        var originalNativeProducer = NativeModuleCompiler.CreateProducer(originalProviderProgram,
            ["Fill", "FillVoid"], "CopyOutputProvider", policy);
        var nativeConsumerProgram = Compile(consumerSource, policy,
            NativeModuleCompiler.CreateReference(originalNativeProducer), library: false);
        var nativeConsumer = NativeModuleCompiler.CreateConsumer(nativeConsumerProgram, originalNativeProducer, policy);
        var nativeConsumerHash = SHA256.HashData(nativeConsumer);
        var nativeProducer = NativeModuleCompiler.CreateProducer(providerProgram,
            ["Fill", "FillVoid"], "CopyOutputProvider", policy);
        Assert.Equal(84, await NativeModuleCompiler.RunAsync(nativeConsumer, nativeProducer,
            TimeSpan.FromSeconds(10)).ConfigureAwait(true));
        Assert.Equal(nativeConsumerHash, SHA256.HashData(nativeConsumer));
    }

    [Theory]
    [InlineData(NativeRuntimePolicy.FailFast)]
    [InlineData(NativeRuntimePolicy.CheckedExceptions)]
    public async Task ScalarCopySpecializationsPersistPrivatelyAcrossSemanticAndNativeModulesAsync(
        NativeRuntimePolicy policy)
    {
        var original = ModuleCompiler.CreateProducer(Compile(Stub, policy), ["PowerProbe"]);
        var reference = ModuleCompiler.CreateReference(original);
        var consumer = ModuleCompiler.CreateConsumer(Compile("int32 Main() { return PowerProbe(); }",
            policy, reference, library: false), original);
        var consumerHash = SHA256.HashData(consumer);
        var replacement = ModuleCompiler.CreateProducer(Compile(ScalarProducer, policy), ["PowerProbe"]);
        var scalarReference = ModuleCompiler.CreateReference(replacement);
        Assert.Empty(scalarReference.Structures);
        Assert.Equal("PowerProbe", Assert.Single(scalarReference.Functions).Name);
        var linked = ModuleCompiler.Link(consumer, replacement);
        Assert.Contains(linked.Functions, function => function.Name == "$producer.Choose<int64>");
        Assert.Contains(linked.Functions, function => function.Name == "$producer.TryCopy<int64>");
        Assert.Equal(42, await ExecuteAsync(NativeEmitter.Emit(linked, policy)).ConfigureAwait(true));
        Assert.Equal(consumerHash, SHA256.HashData(consumer));

        var nativeOriginal = NativeModuleCompiler.CreateProducer(Compile(Stub, policy), ["PowerProbe"], "NodeB", policy);
        var nativeReference = NativeModuleCompiler.CreateGraphReference([nativeOriginal]);
        var nativeConsumer = NativeModuleCompiler.CreateGraphConsumer(Compile(
            "int32 Main() { return PowerProbe(); }", policy, nativeReference, library: false),
            [nativeOriginal], policy);
        var nativeHash = SHA256.HashData(nativeConsumer);
        var nativeReplacement = NativeModuleCompiler.CreateProducer(Compile(ScalarProducer, policy),
            ["PowerProbe"], "NodeB", policy);
        Assert.Equal(42, await NativeModuleCompiler.RunGraphAsync(nativeConsumer, [nativeReplacement],
            TimeSpan.FromSeconds(10), policy).ConfigureAwait(true));
        Assert.Equal(42, await NativeModuleCompiler.RunGraphJitAsync(nativeConsumer, [nativeReplacement],
            Apphost(), TimeSpan.FromSeconds(30), policy).ConfigureAwait(true));
        Assert.Equal(nativeHash, SHA256.HashData(nativeConsumer));
    }

    [Theory]
    [InlineData(NativeRuntimePolicy.FailFast)]
    [InlineData(NativeRuntimePolicy.CheckedExceptions)]
    public async Task FloatingCopySpecializationsPersistPrivatelyAcrossSemanticAndNativeModulesAsync(
        NativeRuntimePolicy policy)
    {
        const string FloatingProducer = """
            struct Inner {
                public float32 Single;
                public int32 Marker;
                [NoThrow] public Inner(float32 single, int32 marker) { Single = single; Marker = marker; }
            }
            struct Pair {
                public Inner Nested;
                public float64 Wide;
                [NoThrow] public Pair(Inner nested, float64 wide) { Nested = nested; Wide = wide; }
                [NoThrow] public float32 ReadSingle() { return Nested.Single; }
            }
            [NoThrow] T Identity<T>(T value) where T : [Copy] { return value; }
            [NoThrow] bool TryCreatePair(init ref<Pair> result) {
                result = new Pair(new Inner(float32(1), 42), float64(2));
                return true;
            }
            [NoThrow] float32 CopyFloat(float32 value) { return value; }
            [NoThrow] T Forward<T>(T value) where T : [Copy] {
                if (!TryCreatePair(init var pair)) { return Identity(value); }
                if (CopyFloat(pair.ReadSingle()) != float32(1) || pair.Wide != float64(2)) {
                    return Identity(value);
                }
                return Identity(value);
            }
            [NoThrow] bool TryCopy<T>(T value, bool publish, out T result) where T : [Copy] {
                result = Forward(value);
                return publish;
            }
            [NoThrow] bool TryCopyInit<T>(T value, bool publish, init ref<T> result) where T : [Copy] {
                if (!publish) { return false; }
                result = Forward(value);
                return true;
            }
            public int32 PowerProbe() {
                float32 single = Identity<float32>(float32(40));
                float64 wide = Forward(float64(2));
                if (TryCopy(single, false, out var absent)) { return 1; }
                require TryCopy<float32>(single, true, out var copied);
                if (TryCopyInit(wide, false, init var deferred)) { return 2; }
                require TryCopyInit<float64>(wide, true, init deferred);
                if (copied != float32(40) || deferred != float64(2)) { return 3; }
                return 42;
            }
            """;

        var original = ModuleCompiler.CreateProducer(Compile(Stub, policy), ["PowerProbe"]);
        var consumer = ModuleCompiler.CreateConsumer(Compile("int32 Main() { return PowerProbe(); }",
            policy, ModuleCompiler.CreateReference(original), library: false), original);
        var consumerHash = SHA256.HashData(consumer);
        var replacement = ModuleCompiler.CreateProducer(Compile(FloatingProducer, policy), ["PowerProbe"]);
        var linked = ModuleCompiler.Link(consumer, replacement);
        Assert.Contains(linked.Functions, static function => function.Name == "$producer.Identity<float32>");
        Assert.Contains(linked.Functions, static function => function.Name == "$producer.Forward<float64>");
        Assert.Contains(linked.Functions, static function =>
            function.Name == "$producer.TryCopy<float32>" && function.Variables[2].IsOut);
        Assert.Contains(linked.Functions, static function =>
            function.Name == "$producer.TryCopyInit<float64>" && function.Variables[2].IsInit);
        Assert.Equal(42, NativeJit.Execute(linked, policy));
        Assert.Equal(42, await ExecuteAsync(NativeEmitter.Emit(linked, policy)).ConfigureAwait(true));
        Assert.Equal(consumerHash, SHA256.HashData(consumer));

        var nativeOriginal = NativeModuleCompiler.CreateProducer(Compile(Stub, policy),
            ["PowerProbe"], "NodeB", policy);
        var nativeConsumer = NativeModuleCompiler.CreateGraphConsumer(Compile(
            "int32 Main() { return PowerProbe(); }", policy,
            NativeModuleCompiler.CreateGraphReference([nativeOriginal]), library: false),
            [nativeOriginal], policy);
        var nativeHash = SHA256.HashData(nativeConsumer);
        var nativeReplacement = NativeModuleCompiler.CreateProducer(Compile(FloatingProducer, policy),
            ["PowerProbe"], "NodeB", policy);
        Assert.Equal(42, await NativeModuleCompiler.RunGraphAsync(nativeConsumer, [nativeReplacement],
            TimeSpan.FromSeconds(10), policy).ConfigureAwait(true));
        Assert.Equal(42, await NativeModuleCompiler.RunGraphJitAsync(nativeConsumer, [nativeReplacement],
            Apphost(), TimeSpan.FromSeconds(30), policy).ConfigureAwait(true));
        Assert.Equal(nativeHash, SHA256.HashData(nativeConsumer));
    }
}
