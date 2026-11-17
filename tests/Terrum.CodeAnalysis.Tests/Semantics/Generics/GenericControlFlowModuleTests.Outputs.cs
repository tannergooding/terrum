// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using NUnit.Framework;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;

namespace Terrum.CodeAnalysis.Tests.Semantics.Generics;

internal sealed partial class GenericControlFlowModuleTests
{
    private static PrivateModuleIdentityInputs CreateGenericOutputIdentityInputs(
        BoundProgram program, string moduleIdentity)
    {
        var definitions = program.Structures.Select((structure, index) =>
                new PrivateNominalDefinition((PrimitiveType)(100 + index),
                    $"types.{structure.Name}",
                    !structure.IsPrivate && !structure.IsInternal))
            .Concat(program.Classes.Select((@class, index) =>
                new PrivateNominalDefinition(
                    (PrimitiveType)(ManagedClassTypes.First + index),
                    $"classes.{@class.Name}", !@class.IsInternal)))
            .ToArray();
        return PrivateModuleIdentityInputs.Create(program, moduleIdentity,
            definitions, []);
    }

    private const string ScalarProducer = """
         T Choose<T>(T left, T right, bool first)  {
            if (first) { return left; }
            return right;
        }
         bool TryCopy<T>(T value, out T result)  {
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
         bool TryCopy<T>(T value, bool publish, out T result)  {
            result = value;
            return publish;
        }
         int32 Fill<T>(T value, int32 marker, out T result, out T duplicate)  {
            result = value;
            duplicate = value;
            if (marker != 42) { return 0; }
            return 7;
        }
         void FillVoid<T>(T value, out T result)  {
            result = value;
        }
         int32 FillInit<T>(T value, int32 marker, ref<init T> result)  {
        *result = value;
            if (marker != 42) { return 0; }
            return 7;
        }
         int32 FillFloating<T>(T value, int32 status, out T result,
        ref<init T> initialized)  {
            result = value;
        *initialized = value;
            return status;
        }
         float32 FillFloating32<T>(T value, out T result, ref<init T> initialized)
             {
            result = value;
        *initialized = value;
            return float32(-0.0);
        }
         float64 FillFloating64<T>(T value, out T result, ref<init T> initialized)
             {
            result = value;
        *initialized = value;
            return float64(-0.0);
        }
         void FillInitVoid<T>(T value, ref<init T> result)  {
        *result = value;
        }
         int32 FillMixed<T, U>(T value, U second, int32 status, out T result,
        ref<init U> initialized, out int32 count, ref<init float64> score)
              {
            result = value;
        *initialized = second;
            count = 2;
        *score = float64(3.5);
            return status;
        }
         int32 Count<T>(T value, int32 count)  { return count; }
        public int32 PowerProbe() {
            if (TryCopy(new Pair(1, 2), false, out var missing)) { return 1; }
            require TryCopy(new Pair(40, 2), true, out var result);
            int32 status = Fill(result, 42, out var copied, out var duplicate);
            FillVoid(copied, out var copiedAgain);
            init Pair initialized;
            int32 initStatus = FillInit(result, 42, ref init initialized);
            init Pair initializedAgain;
            FillInitVoid(copied, ref init initializedAgain);
            init float32 floatingInitialized;
            int32 floatingStatus = FillFloating(float32(40.5), 0,
                out var floatingCopy, ref init floatingInitialized);
            init int32 floatingInitialized32;
            float32 floatingResult32 = FillFloating32(int32(42), out var floatingCopy32,
                ref init floatingInitialized32);
            init int32 floatingInitialized64;
            float64 floatingResult64 = FillFloating64<int32>(int32(43), out var floatingCopy64,
                ref init floatingInitialized64);
            mut Pair mixed = new Pair(0, 0);
            init int64 initializedMixed;
            init float64 mixedScore;
            int32 mixedStatus = FillMixed(result, int64(42), 7, out mixed,
                ref init initializedMixed, out var mixedCount, ref init mixedScore);
            if (status != 7 || duplicate.First != 40 || duplicate.Second != 2
                || copiedAgain.First != 40 || copiedAgain.Second != 2
                || initStatus != 7 || initialized.First != 40 || initialized.Second != 2
                || initializedAgain.First != 40 || initializedAgain.Second != 2
                || floatingStatus != 0 || floatingCopy != float32(40.5)
                || floatingInitialized != float32(40.5)
                || floatingResult32 != float32(0.0)
                || float32(1.0) / floatingResult32 >= float32(0.0)
                || floatingCopy32 != 42 || floatingInitialized32 != 42
                || floatingResult64 != float64(0.0)
                || float64(1.0) / floatingResult64 >= float64(0.0)
                || floatingCopy64 != 43 || floatingInitialized64 != 43
                || mixedStatus != 7 || mixed.First != 40 || mixed.Second != 2
                || initializedMixed != 42 || mixedCount != 2 || mixedScore != float64(3.5)) { return 2; }
            return int32(result.First + result.Second) + Count(result, 0);
        }
        """;
    [TestCase(NativeRuntimePolicy.FailFast,false)]
    [TestCase(NativeRuntimePolicy.FailFast,true)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions,false)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions,true)]
    public async Task GenericOutputSpecializationsPersistWithoutPublicWitnessesOrNewWireFormatAsync(
        NativeRuntimePolicy policy, bool infer)
    {
        var source = infer ? OutputProducer : OutputProducer
            .Replace("TryCopy(", "TryCopy<Pair>(", StringComparison.Ordinal)
            .Replace("status = Fill(", "status = Fill<Pair>(", StringComparison.Ordinal)
            .Replace("FillVoid(", "FillVoid<Pair>(", StringComparison.Ordinal)
            .Replace("FillInit(", "FillInit<Pair>(", StringComparison.Ordinal)
            .Replace("FillFloating(", "FillFloating<float32>(", StringComparison.Ordinal)
            .Replace("FillFloating32(", "FillFloating32<int32>(", StringComparison.Ordinal)
            .Replace("FillFloating64(", "FillFloating64<int32>(", StringComparison.Ordinal)
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
        var replacementProgram = Compile(source, policy);
        var replacement = ModuleCompiler.CreateProducer(replacementProgram,
            ["PowerProbe"], CreateGenericOutputIdentityInputs(
                replacementProgram, "GenericControlFlowOutputProducer"));
        Assert.That(BinaryPrimitives.ReadInt32LittleEndian(replacement.AsSpan(4, 4)), Is.EqualTo(106));
        var reference = ModuleCompiler.CreateReference(replacement);
        Assert.That(reference.Structures, Is.Empty);
        Assert.That(global::System.Linq.Enumerable.Single(reference.Functions).Name, Is.EqualTo("PowerProbe"));
        var linked = ModuleCompiler.Link(consumer, replacement);
        Assert.That(linked.Structures.Select(static structure => structure.Name), Is.EqualTo(["Pair", "$producer.Pair"]));
        var fill = global::System.Linq.Enumerable.Single(linked.Functions, static function => function.Name == "$producer.Fill<Pair>");
        Assert.That(fill.ReturnType, Is.EqualTo(PrimitiveType.Int32));
        Assert.That(fill.Variables[2].IsOut, Is.True);
        Assert.That(fill.Variables[3].IsOut, Is.True);
        var fillVoid = global::System.Linq.Enumerable.Single(linked.Functions, static function => function.Name == "$producer.FillVoid<Pair>");
        Assert.That(fillVoid.ReturnType, Is.EqualTo(PrimitiveType.Void));
        Assert.That(fillVoid.Variables[1].IsOut, Is.True);
        var fillInit = global::System.Linq.Enumerable.Single(linked.Functions, static function => function.Name == "$producer.FillInit<Pair>");
        Assert.That(fillInit.ReturnType, Is.EqualTo(PrimitiveType.Int32));
        Assert.That(fillInit.Variables[2].IsOut, Is.False);
        Assert.That(fillInit.Variables[2].IsInit, Is.True);
        var fillFloating = global::System.Linq.Enumerable.Single(linked.Functions, static function => function.Name == "$producer.FillFloating<float32>");
        Assert.That(fillFloating.ReturnType, Is.EqualTo(PrimitiveType.Int32));
        Assert.That(fillFloating.Variables[2].IsOut, Is.True);
        Assert.That(fillFloating.Variables[2].IsInit, Is.False);
        Assert.That(fillFloating.Variables[3].IsOut, Is.False);
        Assert.That(fillFloating.Variables[3].IsInit, Is.True);
        var fillFloating32 = global::System.Linq.Enumerable.Single(linked.Functions, static function => function.Name == "$producer.FillFloating32<int32>");
        Assert.That(fillFloating32.ReturnType, Is.EqualTo(PrimitiveType.Float32));
        Assert.That(fillFloating32.Variables[1].IsOut, Is.True);
        Assert.That(fillFloating32.Variables[2].IsOut, Is.False);
        Assert.That(fillFloating32.Variables[2].IsInit, Is.True);
        var fillFloating64 = global::System.Linq.Enumerable.Single(linked.Functions, static function => function.Name == "$producer.FillFloating64<int32>");
        Assert.That(fillFloating64.ReturnType, Is.EqualTo(PrimitiveType.Float64));
        Assert.That(fillFloating64.Variables[1].IsOut, Is.True);
        Assert.That(fillFloating64.Variables[2].IsOut, Is.False);
        Assert.That(fillFloating64.Variables[2].IsInit, Is.True);
        var fillInitVoid = global::System.Linq.Enumerable.Single(linked.Functions, static function => function.Name == "$producer.FillInitVoid<Pair>");
        Assert.That(fillInitVoid.ReturnType, Is.EqualTo(PrimitiveType.Void));
        Assert.That(fillInitVoid.Variables[1].IsOut, Is.False);
        Assert.That(fillInitVoid.Variables[1].IsInit, Is.True);
        var fillMixed = global::System.Linq.Enumerable.Single(linked.Functions, static function => function.Name == "$producer.FillMixed<Pair, int64>");
        Assert.That(fillMixed.ReturnType, Is.EqualTo(PrimitiveType.Int32));
        Assert.That(fillMixed.Variables[3].IsOut, Is.True);
        Assert.That(fillMixed.Variables[3].IsInit, Is.False);
        Assert.That(fillMixed.Variables[4].IsOut, Is.False);
        Assert.That(fillMixed.Variables[4].IsInit, Is.True);
        Assert.That(fillMixed.Variables[5].IsOut, Is.True);
        Assert.That(fillMixed.Variables[5].IsInit, Is.False);
        Assert.That(fillMixed.Variables[6].IsOut, Is.False);
        Assert.That(fillMixed.Variables[6].IsInit, Is.True);
        var helper = global::System.Linq.Enumerable.Single(linked.Functions, static function => function.Name == "$producer.TryCopy<Pair>");
        Assert.That(helper.ReturnType, Is.EqualTo(PrimitiveType.Bool));
        Assert.That(helper.Variables[2].IsOut, Is.True);
        Assert.That(helper.Variables[2].Type, Is.EqualTo((PrimitiveType)101));
        Assert.That(await ExecuteAsync(NativeEmitter.Emit(linked, policy)).ConfigureAwait(true), Is.EqualTo(42));
        Assert.That(SHA256.HashData(consumer), Is.EqualTo(hash));

        var nativeOriginal = NativeModuleCompiler.CreateProducer(Compile(Stub, policy), ["PowerProbe"], "NodeB", policy);
        var middle = NativeModuleCompiler.CreateGraphProducer(Compile(Middle, policy,
            NativeModuleCompiler.CreateGraphReference([nativeOriginal])), ["Middle"], "NodeA", [nativeOriginal], policy);
        var root = NativeModuleCompiler.CreateGraphConsumer(Compile(Root, policy,
            NativeModuleCompiler.CreateGraphReference([middle]), library: false), [middle], policy);
        var rootHash = SHA256.HashData(root);
        var middleHash = SHA256.HashData(middle);
        var nativeReplacement = NativeModuleCompiler.CreateProducer(replacementProgram,
            ["PowerProbe"], "NodeB", policy,
            CreateGenericOutputIdentityInputs(replacementProgram, "NodeB"));
        Assert.That(await NativeModuleCompiler.RunGraphAsync(root, [nativeReplacement, middle],
            TimeSpan.FromSeconds(10), policy).ConfigureAwait(true), Is.EqualTo(44));
        Assert.That(await NativeModuleCompiler.RunGraphJitAsync(root, [middle, nativeReplacement],
            Apphost(), TimeSpan.FromSeconds(30), policy).ConfigureAwait(true), Is.EqualTo(44));
        Assert.That(SHA256.HashData(root), Is.EqualTo(rootHash));
        Assert.That(SHA256.HashData(middle), Is.EqualTo(middleHash));
    }
    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public async Task MixedGenericOutputsReplacePrivateSpecializationsAcrossModulesAsync(
        NativeRuntimePolicy policy)
    {
        const string ReplacementTemplate = """
            struct Pair {
                public int64 First;
                public int64 Second;
                public Pair(int64 first, int64 second) { First = first; Second = second; }
            }
            int32 CopyMixed<T, U>(T value, U second, int32 status, bool complete,
                out T result, ref<init U> initialized)   {
                result = value;
                *initialized = second;
                require complete;
                return status;
            }
             void CopyMixedVoid<T, U>(T value, U second, out T result,
                ref<init U> initialized)   {
                result = value;
                *initialized = second;
            }
            int32 FillManaged(bool complete, out Pair result, ref<init write ManagedNode> initialized) {
                require ManagedNode.TryCreate(42, out var created);
                return CopyMixed(new Pair(40, 2), created, 7, complete, out result,
                    ref init *initialized);
            }
            public int32 PowerProbe() {
                init int64 initialized;
                int32 status = CopyMixed<Pair, int64>(new Pair(42, 2), int64(5), 7, true,
                    out var copied, ref init initialized);
                init int64 voidInitialized;
                CopyMixedVoid(new Pair(18, 24), int64(6), out var voidCopy,
                    ref init voidInitialized);
                if (status != 7 || copied.First != 42 || initialized != 5
                    || voidCopy.First != 18 || voidInitialized != 6) { return 1; }
                MANAGED_PATH
            }
            """;
        var managedPath = policy == NativeRuntimePolicy.CheckedExceptions
            ? """
                mut Pair result = new Pair(0, 0);
                init mut write ManagedNode node;
                if (ManagedHeap.Collect() != 0) { return 1; }
                unsafe {
                    try {
                        FillManaged(false, out result, ref init node);
                        return 2;
                    } catch {
                        if (node is init) { node = _; }
                        if (ManagedHeap.Collect() != 1) { return 3; }
                        int32 managedStatus = FillManaged(true, out result,
                            ref init node);
                        if (ManagedHeap.Collect() != 0 || result.First != 40 || result.Second != 2) {
                            return 4;
                        }
                        return managedStatus + node.Value;
                    }
                }
                return 0;
                """
            : "return 42;";
        var replacementSource = ReplacementTemplate.Replace("MANAGED_PATH", managedPath,
            StringComparison.Ordinal);

        var original = ModuleCompiler.CreateProducer(Compile(Stub, policy), ["PowerProbe"]);
        var consumer = ModuleCompiler.CreateConsumer(Compile("int32 Main() { return PowerProbe(); }",
            policy, ModuleCompiler.CreateReference(original), library: false), original);
        var consumerHash = SHA256.HashData(consumer);
        var replacementProgram = Compile(replacementSource, policy);
        var replacement = ModuleCompiler.CreateProducer(replacementProgram,
            ["PowerProbe"], CreateGenericOutputIdentityInputs(
                replacementProgram, "GenericControlFlowMixedOutputs"));
        var linked = ModuleCompiler.Link(consumer, replacement);
        Assert.That(global::System.Linq.Enumerable.Single(linked.Functions, static function => function.Name.StartsWith("$producer.CopyMixed<Pair, int64>",
                StringComparison.Ordinal)).ReturnType, Is.EqualTo(PrimitiveType.Int32));
        var mixed = global::System.Linq.Enumerable.Single(linked.Functions, static function => function.Name.StartsWith("$producer.CopyMixed<Pair, int64>",
                StringComparison.Ordinal));
        Assert.That(mixed.Variables[4].IsOut, Is.True);
        Assert.That(mixed.Variables[4].IsInit, Is.False);
        Assert.That(mixed.Variables[5].IsOut, Is.False);
        Assert.That(mixed.Variables[5].IsInit, Is.True);
        var mixedVoid = global::System.Linq.Enumerable.Single(linked.Functions, static function => function.Name.StartsWith("$producer.CopyMixedVoid<Pair, int64>",
                StringComparison.Ordinal));
        Assert.That(mixedVoid.ReturnType, Is.EqualTo(PrimitiveType.Void));
        Assert.That(mixedVoid.Variables[2].IsOut, Is.True);
        Assert.That(mixedVoid.Variables[2].IsInit, Is.False);
        Assert.That(mixedVoid.Variables[3].IsOut, Is.False);
        Assert.That(mixedVoid.Variables[3].IsInit, Is.True);
        var expected = policy == NativeRuntimePolicy.CheckedExceptions ? 49 : 42;
        Assert.That(NativeJit.Execute(linked, policy), Is.EqualTo(expected));
        Assert.That(await ExecuteAsync(NativeEmitter.Emit(linked, policy)).ConfigureAwait(true), Is.EqualTo(expected));
        Assert.That(SHA256.HashData(consumer), Is.EqualTo(consumerHash));

        var nativeOriginal = NativeModuleCompiler.CreateProducer(Compile(Stub, policy),
            ["PowerProbe"], "NodeB", policy);
        var nativeConsumer = NativeModuleCompiler.CreateGraphConsumer(Compile(
            "int32 Main() { return PowerProbe(); }", policy,
            NativeModuleCompiler.CreateGraphReference([nativeOriginal]), library: false),
            [nativeOriginal], policy);
        var nativeHash = SHA256.HashData(nativeConsumer);
        var nativeReplacement = NativeModuleCompiler.CreateProducer(
            replacementProgram, ["PowerProbe"], "NodeB", policy);
        Assert.That(await NativeModuleCompiler.RunGraphAsync(nativeConsumer,
            [nativeReplacement], TimeSpan.FromSeconds(10), policy).ConfigureAwait(true), Is.EqualTo(expected));
        Assert.That(await NativeModuleCompiler.RunGraphJitAsync(nativeConsumer,
            [nativeReplacement], Apphost(), TimeSpan.FromSeconds(30), policy).ConfigureAwait(true), Is.EqualTo(expected));
        Assert.That(SHA256.HashData(nativeConsumer), Is.EqualTo(nativeHash));
    }
    [TestCase(NativeRuntimePolicy.FailFast,false)]
    [TestCase(NativeRuntimePolicy.FailFast,true)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions,false)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions,true)]
    public async Task FixedSideOutputsReplacePrivateSpecializationsAcrossModulesAsync(
        NativeRuntimePolicy policy, bool explicitType)
    {
        const string ReplacementTemplate = """
            struct Pair {
                public int64 First;
                public int64 Second;
                public Pair(int64 first, int64 second) { First = first; Second = second; }
            }
            int32 Fill<T>(T value, bool complete, out T copied, out int32 count,
                ref<init float64> score)  {
                copied = value;
                count = 2;
                *score = float64(3.5);
                require complete;
                return 7;
            }
            void FillInit<U>(U value, bool complete, ref<init U> initialized, out int32 count,
                ref<init float64> score)  {
                *initialized = value;
                count = 3;
                *score = float64(4.5);
                require complete;
            }
            public int32 PowerProbe() {
                mut Pair copied = new Pair(0, 0);
                mut int32 count = 0;
                init float64 score;
                int32 status = FILL(new Pair(42, 2), true, out copied, out count, ref init score);
                init Pair initialized;
                mut int32 initCount = 0;
                init float64 initScore;
                FILLINIT(new Pair(40, 2), true, ref init initialized, out initCount,
                    ref init initScore);
                if (status != 7 || copied.First != 42 || copied.Second != 2 || count != 2
                    || score != float64(3.5) || initialized.First != 40 || initialized.Second != 2
                    || initCount != 3 || initScore != float64(4.5)) { return 1; }
                FAILURE_PATH
                return 42;
            }
            """;
        var failurePath = policy == NativeRuntimePolicy.CheckedExceptions
            ? """
                mut Pair failedCopy = new Pair(0, 0);
                mut int32 failedCount = 0;
                init mut float64 failedScore;
                init mut Pair failedInitialized;
                mut int32 failedInitCount = 0;
                init mut float64 failedInitScore;
                unsafe {
                    try {
                        Fill(new Pair(1, 2), false, out failedCopy, out failedCount,
                            ref init failedScore);
                        return 2;
                    } catch {
                        if (failedScore is init) { failedScore = _; }
                        int32 retryStatus = Fill(new Pair(44, 2), true,
                            out failedCopy, out failedCount, ref init failedScore);
                        if (retryStatus != 7 || failedCopy.First != 44 || failedCount != 2
                            || failedScore != float64(3.5)) { return 4; }
                    }
                    try {
                        FillInit(new Pair(1, 2), false, ref init failedInitialized,
                            out failedInitCount, ref init failedInitScore);
                        return 5;
                    } catch {
                        if (failedInitialized is init) { failedInitialized = _; }
                        if (failedInitScore is init) { failedInitScore = _; }
                        FillInit(new Pair(45, 2), true, ref init failedInitialized,
                            out failedInitCount, ref init failedInitScore);
                        if (failedInitialized.First != 45 || failedInitCount != 3
                            || failedInitScore != float64(4.5)) { return 7; }
                    }
                }
                """
            : string.Empty;
        var fillCall = explicitType ? "Fill<Pair>(" : "Fill(";
        var fillInitCall = explicitType ? "FillInit<Pair>(" : "FillInit(";
        var replacementSource = ReplacementTemplate
            .Replace("FILL(", fillCall, StringComparison.Ordinal)
            .Replace("FILLINIT(", fillInitCall, StringComparison.Ordinal)
            .Replace("FAILURE_PATH", failurePath, StringComparison.Ordinal);

        var original = ModuleCompiler.CreateProducer(Compile(Stub, policy), ["PowerProbe"]);
        var consumer = ModuleCompiler.CreateConsumer(Compile("int32 Main() { return PowerProbe(); }",
            policy, ModuleCompiler.CreateReference(original), library: false), original);
        var consumerHash = SHA256.HashData(consumer);
        var replacementProgram = Compile(replacementSource, policy);
        var replacement = ModuleCompiler.CreateProducer(replacementProgram,
            ["PowerProbe"], CreateGenericOutputIdentityInputs(
                replacementProgram, "GenericControlFlowFixedSideOutputs"));
        var linked = ModuleCompiler.Link(consumer, replacement);
        var fill = global::System.Linq.Enumerable.Single(linked.Functions, static function => function.Name == "$producer.Fill<Pair>");
        Assert.That(fill.ReturnType, Is.EqualTo(PrimitiveType.Int32));
        Assert.That(fill.Variables[3].Type, Is.EqualTo(PrimitiveType.Int32));
        Assert.That(fill.Variables[2].IsOut, Is.True);
        Assert.That(fill.Variables[2].IsInit, Is.False);
        Assert.That(fill.Variables[3].IsOut, Is.True);
        Assert.That(fill.Variables[3].IsInit, Is.False);
        var fillScore = linked.CheckedReferenceTypes[
            CheckedReferenceTypes.Index(fill.Variables[4].Type)];
        Assert.That(fillScore.ReferentType, Is.EqualTo(PrimitiveType.Float64));
        Assert.That(fillScore.IsInitialization, Is.True);
        Assert.That(fill.Variables[4].IsOut, Is.False);
        Assert.That(fill.Variables[4].IsInit, Is.True);
        var fillInit = global::System.Linq.Enumerable.Single(linked.Functions, static function => function.Name == "$producer.FillInit<Pair>");
        Assert.That(fillInit.ReturnType, Is.EqualTo(PrimitiveType.Void));
        Assert.That(fillInit.Variables[2].IsOut, Is.False);
        Assert.That(fillInit.Variables[2].IsInit, Is.True);
        Assert.That(fillInit.Variables[3].Type, Is.EqualTo(PrimitiveType.Int32));
        Assert.That(fillInit.Variables[3].IsOut, Is.True);
        Assert.That(fillInit.Variables[3].IsInit, Is.False);
        var fillInitScore = linked.CheckedReferenceTypes[
            CheckedReferenceTypes.Index(fillInit.Variables[4].Type)];
        Assert.That(fillInitScore.ReferentType, Is.EqualTo(PrimitiveType.Float64));
        Assert.That(fillInitScore.IsInitialization, Is.True);
        Assert.That(fillInit.Variables[4].IsOut, Is.False);
        Assert.That(fillInit.Variables[4].IsInit, Is.True);
        Assert.That(NativeJit.Execute(linked, policy), Is.EqualTo(42));
        Assert.That(await ExecuteAsync(NativeEmitter.Emit(linked, policy)).ConfigureAwait(true), Is.EqualTo(42));
        Assert.That(SHA256.HashData(consumer), Is.EqualTo(consumerHash));

        var nativeOriginal = NativeModuleCompiler.CreateProducer(Compile(Stub, policy),
            ["PowerProbe"], "NodeB", policy);
        var nativeConsumer = NativeModuleCompiler.CreateGraphConsumer(Compile(
            "int32 Main() { return PowerProbe(); }", policy,
            NativeModuleCompiler.CreateGraphReference([nativeOriginal]), library: false),
            [nativeOriginal], policy);
        var nativeHash = SHA256.HashData(nativeConsumer);
        var nativeReplacement = NativeModuleCompiler.CreateProducer(
            replacementProgram, ["PowerProbe"], "NodeB", policy,
            CreateGenericOutputIdentityInputs(replacementProgram, "NodeB"));
        Assert.That(await NativeModuleCompiler.RunGraphAsync(nativeConsumer,
            [nativeReplacement], TimeSpan.FromSeconds(10), policy).ConfigureAwait(true), Is.EqualTo(42));
        Assert.That(await NativeModuleCompiler.RunGraphJitAsync(nativeConsumer,
            [nativeReplacement], Apphost(), TimeSpan.FromSeconds(30), policy).ConfigureAwait(true), Is.EqualTo(42));
        Assert.That(SHA256.HashData(nativeConsumer), Is.EqualTo(nativeHash));
    }
    [TestCase(NativeRuntimePolicy.FailFast,false)]
    [TestCase(NativeRuntimePolicy.FailFast,true)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions,false)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions,true)]
    public async Task FloatingResultFixedSideOutputsReplacePrivateSpecializationsAcrossModulesAsync(
        NativeRuntimePolicy policy, bool explicitType)
    {
        const string ReplacementTemplate = """
            struct Pair {
                public int64 First;
                public int64 Second;
                public Pair(int64 first, int64 second) { First = first; Second = second; }
            }
            float32 Fill<T>(T value, bool complete, out T copied, ref<init T> initialized,
                out int32 count,
                ref<init float64> score)  {
                copied = value;
                *initialized = value;
                count = 2;
                *score = float64(3.5);
                require complete;
                return float32(-0.0);
            }
            float64 FillInit<U>(U value, bool complete, out U copied, ref<init U> initialized,
                out int32 count,
                ref<init float32> score)  {
                copied = value;
                *initialized = value;
                count = 3;
                *score = float32(4.5);
                require complete;
                return float64(4.9406564584124654e-324);
            }
            public int32 PowerProbe() {
                mut Pair copied = new Pair(0, 0);
                mut int32 count = 0;
                init float64 score;
                init Pair filled;
                float32 negative = FILL(new Pair(42, 2), true, out copied,
                    ref init filled, out count, ref init score);
                init Pair initialized;
                mut Pair initCopy = new Pair(0, 0);
                mut int32 initCount = 0;
                init float32 initScore;
                float64 tiny = FILLINIT(new Pair(40, 2), true, out initCopy,
                    ref init initialized, out initCount, ref init initScore);
                if (negative != float32(0.0) || float32(1.0) / negative >= float32(0.0)
                    || copied.First != 42 || copied.Second != 2 || filled.First != 42
                    || filled.Second != 2 || count != 2 || score != float64(3.5)
                    || initCopy.First != 40 || initialized.First != 40 || initialized.Second != 2
                    || initCount != 3 || initScore != float32(4.5)
                    || tiny != float64(4.9406564584124654e-324)) { return 1; }
                FAILURE_PATH
                return 42;
            }
            """;
        var failurePath = policy == NativeRuntimePolicy.CheckedExceptions
            ? """
                mut Pair failedCopy = new Pair(0, 0);
                mut int32 failedCount = 0;
                init mut float64 failedScore;
                init mut Pair failedInitializedForOut;
                mut Pair failedInitCopy = new Pair(0, 0);
                init mut Pair failedInitialized;
                mut int32 failedInitCount = 0;
                init mut float32 failedInitScore;
                unsafe {
                    try {
                        Fill(new Pair(1, 2), false, out failedCopy,
                            ref init failedInitializedForOut, out failedCount,
                            ref init failedScore);
                        return 2;
                    } catch {
                        if (failedInitializedForOut is init) {
                            failedInitializedForOut = _;
                        }
                        if (failedScore is init) { failedScore = _; }
                        float32 retryNegative = Fill(new Pair(44, 2), true,
                            out failedCopy, ref init failedInitializedForOut,
                            out failedCount, ref init failedScore);
                        if (retryNegative != float32(0.0)
                            || float32(1.0) / retryNegative >= float32(0.0)
                            || failedCopy.First != 44 || failedInitializedForOut.First != 44
                            || failedCount != 2
                            || failedScore != float64(3.5)) { return 4; }
                    }
                    try {
                        FillInit(new Pair(1, 2), false, out failedInitCopy,
                            ref init failedInitialized, out failedInitCount,
                            ref init failedInitScore);
                        return 5;
                    } catch {
                        if (failedInitialized is init) {
                            failedInitialized = _;
                        }
                        if (failedInitScore is init) { failedInitScore = _; }
                        float64 retryTiny = FillInit(new Pair(45, 2), true,
                            out failedInitCopy, ref init failedInitialized,
                            out failedInitCount, ref init failedInitScore);
                        if (retryTiny != float64(4.9406564584124654e-324)
                            || failedInitCopy.First != 45 || failedInitialized.First != 45
                            || failedInitCount != 3
                            || failedInitScore != float32(4.5)) { return 7; }
                    }
                }
                """
            : string.Empty;
        var fillCall = explicitType ? "Fill<Pair>(" : "Fill(";
        var fillInitCall = explicitType ? "FillInit<Pair>(" : "FillInit(";
        var replacementSource = ReplacementTemplate
            .Replace("FILL(", fillCall, StringComparison.Ordinal)
            .Replace("FILLINIT(", fillInitCall, StringComparison.Ordinal)
            .Replace("FAILURE_PATH", failurePath, StringComparison.Ordinal);

        var original = ModuleCompiler.CreateProducer(Compile(Stub, policy), ["PowerProbe"]);
        var consumer = ModuleCompiler.CreateConsumer(Compile("int32 Main() { return PowerProbe(); }",
            policy, ModuleCompiler.CreateReference(original), library: false), original);
        var consumerHash = SHA256.HashData(consumer);
        var replacementProgram = Compile(replacementSource, policy);
        var replacement = ModuleCompiler.CreateProducer(replacementProgram,
            ["PowerProbe"], CreateGenericOutputIdentityInputs(
                replacementProgram, "GenericControlFlowFloatingResultOutputs"));
        var linked = ModuleCompiler.Link(consumer, replacement);
        var fill = global::System.Linq.Enumerable.Single(linked.Functions, static function => function.Name == "$producer.Fill<Pair>");
        Assert.That(fill.ReturnType, Is.EqualTo(PrimitiveType.Float32));
        Assert.That(fill.Variables[4].Type, Is.EqualTo(PrimitiveType.Int32));
        Assert.That(fill.Variables[2].IsOut, Is.True);
        Assert.That(fill.Variables[2].IsInit, Is.False);
        Assert.That(fill.Variables[3].IsOut, Is.False);
        Assert.That(fill.Variables[3].IsInit, Is.True);
        Assert.That(fill.Variables[4].IsOut, Is.True);
        Assert.That(fill.Variables[4].IsInit, Is.False);
        var fillScore = linked.CheckedReferenceTypes[
            CheckedReferenceTypes.Index(fill.Variables[5].Type)];
        Assert.That(fillScore.ReferentType, Is.EqualTo(PrimitiveType.Float64));
        Assert.That(fillScore.IsInitialization, Is.True);
        Assert.That(fill.Variables[5].IsOut, Is.False);
        Assert.That(fill.Variables[5].IsInit, Is.True);
        var fillInit = global::System.Linq.Enumerable.Single(linked.Functions, static function => function.Name == "$producer.FillInit<Pair>");
        Assert.That(fillInit.ReturnType, Is.EqualTo(PrimitiveType.Float64));
        Assert.That(fillInit.Variables[2].IsOut, Is.True);
        Assert.That(fillInit.Variables[2].IsInit, Is.False);
        Assert.That(fillInit.Variables[3].IsOut, Is.False);
        Assert.That(fillInit.Variables[3].IsInit, Is.True);
        Assert.That(fillInit.Variables[4].Type, Is.EqualTo(PrimitiveType.Int32));
        Assert.That(fillInit.Variables[4].IsOut, Is.True);
        Assert.That(fillInit.Variables[4].IsInit, Is.False);
        var fillInitScore = linked.CheckedReferenceTypes[
            CheckedReferenceTypes.Index(fillInit.Variables[5].Type)];
        Assert.That(fillInitScore.ReferentType, Is.EqualTo(PrimitiveType.Float32));
        Assert.That(fillInitScore.IsInitialization, Is.True);
        Assert.That(fillInit.Variables[5].IsOut, Is.False);
        Assert.That(fillInit.Variables[5].IsInit, Is.True);
        Assert.That(NativeJit.Execute(linked, policy), Is.EqualTo(42));
        Assert.That(await ExecuteAsync(NativeEmitter.Emit(linked, policy)).ConfigureAwait(true), Is.EqualTo(42));
        Assert.That(SHA256.HashData(consumer), Is.EqualTo(consumerHash));

        var nativeOriginal = NativeModuleCompiler.CreateProducer(Compile(Stub, policy),
            ["PowerProbe"], "NodeB", policy);
        var nativeConsumer = NativeModuleCompiler.CreateGraphConsumer(Compile(
            "int32 Main() { return PowerProbe(); }", policy,
            NativeModuleCompiler.CreateGraphReference([nativeOriginal]), library: false),
            [nativeOriginal], policy);
        var nativeHash = SHA256.HashData(nativeConsumer);
        var nativeReplacement = NativeModuleCompiler.CreateProducer(
            replacementProgram, ["PowerProbe"], "NodeB", policy,
            CreateGenericOutputIdentityInputs(replacementProgram, "NodeB"));
        Assert.That(await NativeModuleCompiler.RunGraphAsync(nativeConsumer,
            [nativeReplacement], TimeSpan.FromSeconds(10), policy).ConfigureAwait(true), Is.EqualTo(42));
        Assert.That(await NativeModuleCompiler.RunGraphJitAsync(nativeConsumer,
            [nativeReplacement], Apphost(), TimeSpan.FromSeconds(30), policy).ConfigureAwait(true), Is.EqualTo(42));
        Assert.That(SHA256.HashData(nativeConsumer), Is.EqualTo(nativeHash));
    }
    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public async Task CopyOutVarDestinationsPublishForNonBooleanAndVoidCallsAsync(
        NativeRuntimePolicy policy)
    {
        const string providerSource = """
            [Blittable] [Layout(LayoutKind.Sequential)] public struct Pair {
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
            [Blittable] [Layout(LayoutKind.Sequential)] public struct Pair {
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
        Assert.That(NativeJit.Execute(linked, policy), Is.EqualTo(84));
        Assert.That(await ExecuteAsync(NativeEmitter.Emit(linked, policy)).ConfigureAwait(true), Is.EqualTo(84));
        Assert.That(SHA256.HashData(consumer), Is.EqualTo(consumerHash));

        var originalNativeProducer = NativeModuleCompiler.CreateProducer(originalProviderProgram,
            ["Fill", "FillVoid"], "CopyOutputProvider", policy);
        var nativeConsumerProgram = Compile(consumerSource, policy,
            NativeModuleCompiler.CreateReference(originalNativeProducer), library: false);
        var nativeConsumer = NativeModuleCompiler.CreateConsumer(nativeConsumerProgram, originalNativeProducer, policy);
        var nativeConsumerHash = SHA256.HashData(nativeConsumer);
        var nativeProducer = NativeModuleCompiler.CreateProducer(providerProgram,
            ["Fill", "FillVoid"], "CopyOutputProvider", policy);
        Assert.That(await NativeModuleCompiler.RunAsync(nativeConsumer, nativeProducer,
            TimeSpan.FromSeconds(10)).ConfigureAwait(true), Is.EqualTo(84));
        Assert.That(SHA256.HashData(nativeConsumer), Is.EqualTo(nativeConsumerHash));
    }
    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
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
        Assert.That(scalarReference.Structures, Is.Empty);
        Assert.That(global::System.Linq.Enumerable.Single(scalarReference.Functions).Name, Is.EqualTo("PowerProbe"));
        var linked = ModuleCompiler.Link(consumer, replacement);
        Assert.That(global::System.Linq.Enumerable.Any(linked.Functions,function => function.Name == "$producer.Choose<int64>"),Is.True);
        Assert.That(global::System.Linq.Enumerable.Any(linked.Functions,function => function.Name == "$producer.TryCopy<int64>"),Is.True);
        Assert.That(await ExecuteAsync(NativeEmitter.Emit(linked, policy)).ConfigureAwait(true), Is.EqualTo(42));
        Assert.That(SHA256.HashData(consumer), Is.EqualTo(consumerHash));

        var nativeOriginal = NativeModuleCompiler.CreateProducer(Compile(Stub, policy), ["PowerProbe"], "NodeB", policy);
        var nativeReference = NativeModuleCompiler.CreateGraphReference([nativeOriginal]);
        var nativeConsumer = NativeModuleCompiler.CreateGraphConsumer(Compile(
            "int32 Main() { return PowerProbe(); }", policy, nativeReference, library: false),
            [nativeOriginal], policy);
        var nativeHash = SHA256.HashData(nativeConsumer);
        var nativeReplacement = NativeModuleCompiler.CreateProducer(Compile(ScalarProducer, policy),
            ["PowerProbe"], "NodeB", policy);
        Assert.That(await NativeModuleCompiler.RunGraphAsync(nativeConsumer, [nativeReplacement],
            TimeSpan.FromSeconds(10), policy).ConfigureAwait(true), Is.EqualTo(42));
        Assert.That(await NativeModuleCompiler.RunGraphJitAsync(nativeConsumer, [nativeReplacement],
            Apphost(), TimeSpan.FromSeconds(30), policy).ConfigureAwait(true), Is.EqualTo(42));
        Assert.That(SHA256.HashData(nativeConsumer), Is.EqualTo(nativeHash));
    }
    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public async Task FloatingCopyArgumentsPersistPrivatelyAcrossSemanticAndNativeModulesAsync(
        NativeRuntimePolicy policy)
    {
        const string FloatingProducer = """
             T Identity<T>(T value)  { return value; }
             bool TryCopy<T>(T value, out T result)  {
                result = Identity(value);
                return true;
            }
             bool TryInitialize<T>(T value, ref<init T> result)  {
                *result = Identity(value);
                return true;
            }
            public int32 PowerProbe() {
                float32 single = Identity<float32>(float32(40));
                float64 wide = Identity(float64(2));
                require TryCopy(single, out var copied);
                init float64 initialized;
                require TryInitialize(wide, ref init initialized);
                if (copied != float32(40) || initialized != float64(2)) { return 1; }
                return 42;
            }
            """;

        var original = ModuleCompiler.CreateProducer(Compile(Stub, policy), ["PowerProbe"]);
        var consumer = ModuleCompiler.CreateConsumer(Compile("int32 Main() { return PowerProbe(); }",
            policy, ModuleCompiler.CreateReference(original), library: false), original);
        var consumerHash = SHA256.HashData(consumer);
        var replacementProgram = Compile(FloatingProducer, policy);
        var replacement = ModuleCompiler.CreateProducer(replacementProgram,
            ["PowerProbe"]);
        var replacementReference = ModuleCompiler.CreateReference(replacement);
        Assert.That(replacementReference.Structures, Is.Empty);
        Assert.That(global::System.Linq.Enumerable.Single(replacementReference.Functions).Name, Is.EqualTo("PowerProbe"));

        var linked = ModuleCompiler.Link(consumer, replacement);
        Assert.That(global::System.Linq.Enumerable.Any(linked.Functions,static function => function.Name == "$producer.Identity<float32>"),Is.True);
        Assert.That(global::System.Linq.Enumerable.Any(linked.Functions,static function => function.Name == "$producer.Identity<float64>"),Is.True);
        Assert.That(global::System.Linq.Enumerable.Any(linked.Functions,static function =>
            function.Name == "$producer.TryCopy<float32>" && function.Variables[1].IsOut),Is.True);
        Assert.That(global::System.Linq.Enumerable.Any(linked.Functions,static function =>
            function.Name == "$producer.TryInitialize<float64>" && function.Variables[1].IsInit),Is.True);
        Assert.That(NativeJit.Execute(linked, policy), Is.EqualTo(42));
        Assert.That(await ExecuteAsync(NativeEmitter.Emit(linked, policy)).ConfigureAwait(true), Is.EqualTo(42));
        Assert.That(SHA256.HashData(consumer), Is.EqualTo(consumerHash));

        var nativeOriginal = NativeModuleCompiler.CreateProducer(Compile(Stub, policy),
            ["PowerProbe"], "NodeB", policy);
        var nativeConsumer = NativeModuleCompiler.CreateGraphConsumer(Compile(
            "int32 Main() { return PowerProbe(); }", policy,
            NativeModuleCompiler.CreateGraphReference([nativeOriginal]), library: false),
            [nativeOriginal], policy);
        var nativeHash = SHA256.HashData(nativeConsumer);
        var nativeReplacement = NativeModuleCompiler.CreateProducer(
            replacementProgram, ["PowerProbe"], "NodeB", policy,
            CreateGenericOutputIdentityInputs(replacementProgram, "NodeB"));
        Assert.That(await NativeModuleCompiler.RunGraphAsync(nativeConsumer, [nativeReplacement],
            TimeSpan.FromSeconds(10), policy).ConfigureAwait(true), Is.EqualTo(42));
        Assert.That(await NativeModuleCompiler.RunGraphJitAsync(nativeConsumer, [nativeReplacement],
            Apphost(), TimeSpan.FromSeconds(30), policy).ConfigureAwait(true), Is.EqualTo(42));
        Assert.That(SHA256.HashData(nativeConsumer), Is.EqualTo(nativeHash));
    }
    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public async Task FloatingCopySpecializationsPersistPrivatelyAcrossSemanticAndNativeModulesAsync(
        NativeRuntimePolicy policy)
    {
        const string FloatingProducer = """
            struct Inner {
                public float32 Single;
                public int32 Marker;
                 public Inner(float32 single, int32 marker) { Single = single; Marker = marker; }
            }
            struct Pair {
                public Inner Nested;
                public float64 Wide;
                 public Pair(Inner nested, float64 wide) { Nested = nested; Wide = wide; }
                 public float32 ReadSingle() { return Nested.Single; }
            }
             T Identity<T>(T value)  { return value; }
             bool TryCreatePair(ref<init Pair> result) {
                *result = new Pair(new Inner(float32(1), 42), float64(2));
                return true;
            }
             float32 CopyFloat(float32 value) { return value; }
             T Forward<T>(T value)  {
                init Pair pair;
                if (!TryCreatePair(ref init pair)) { return Identity(value); }
                if (CopyFloat(pair.ReadSingle()) != float32(1) || pair.Wide != float64(2)) {
                    return Identity(value);
                }
                return Identity(value);
            }
             bool TryCopy<T>(T value, bool publish, out T result)  {
                result = Forward(value);
                return publish;
            }
             bool TryCopyInit<T>(T value, bool publish, ref<init T> result)  {
                if (!publish) { return false; }
                *result = Forward(value);
                return true;
            }
            public int32 PowerProbe() {
                float32 single = Identity<float32>(float32(40));
                float64 wide = Forward(float64(2));
                if (TryCopy(single, false, out var absent)) { return 1; }
                require TryCopy<float32>(single, true, out var copied);
                init float64 deferred;
                if (TryCopyInit(wide, false, ref init deferred)) { return 2; }
                require TryCopyInit<float64>(wide, true, ref init deferred);
                if (copied != float32(40) || deferred != float64(2)) { return 3; }
                return 42;
            }
            """;

        var original = ModuleCompiler.CreateProducer(Compile(Stub, policy), ["PowerProbe"]);
        var consumer = ModuleCompiler.CreateConsumer(Compile("int32 Main() { return PowerProbe(); }",
            policy, ModuleCompiler.CreateReference(original), library: false), original);
        var consumerHash = SHA256.HashData(consumer);
        var replacementProgram = Compile(FloatingProducer, policy);
        var replacement = ModuleCompiler.CreateProducer(replacementProgram,
            ["PowerProbe"], CreateGenericOutputIdentityInputs(
                replacementProgram, "GenericControlFlowFloatingCopy"));
        var linked = ModuleCompiler.Link(consumer, replacement);
        Assert.That(global::System.Linq.Enumerable.Any(linked.Functions,static function => function.Name == "$producer.Identity<float32>"),Is.True);
        Assert.That(global::System.Linq.Enumerable.Any(linked.Functions,static function => function.Name == "$producer.Forward<float64>"),Is.True);
        Assert.That(global::System.Linq.Enumerable.Any(linked.Functions,static function =>
            function.Name == "$producer.TryCopy<float32>" && function.Variables[2].IsOut),Is.True);
        Assert.That(global::System.Linq.Enumerable.Any(linked.Functions,static function =>
            function.Name == "$producer.TryCopyInit<float64>" && function.Variables[2].IsInit),Is.True);
        Assert.That(NativeJit.Execute(linked, policy), Is.EqualTo(42));
        Assert.That(await ExecuteAsync(NativeEmitter.Emit(linked, policy)).ConfigureAwait(true), Is.EqualTo(42));
        Assert.That(SHA256.HashData(consumer), Is.EqualTo(consumerHash));

        var nativeOriginal = NativeModuleCompiler.CreateProducer(Compile(Stub, policy),
            ["PowerProbe"], "NodeB", policy);
        var nativeConsumer = NativeModuleCompiler.CreateGraphConsumer(Compile(
            "int32 Main() { return PowerProbe(); }", policy,
            NativeModuleCompiler.CreateGraphReference([nativeOriginal]), library: false),
            [nativeOriginal], policy);
        var nativeHash = SHA256.HashData(nativeConsumer);
        var nativeReplacement = NativeModuleCompiler.CreateProducer(
            replacementProgram, ["PowerProbe"], "NodeB", policy,
            CreateGenericOutputIdentityInputs(replacementProgram, "NodeB"));
        Assert.That(await NativeModuleCompiler.RunGraphAsync(nativeConsumer, [nativeReplacement],
            TimeSpan.FromSeconds(10), policy).ConfigureAwait(true), Is.EqualTo(42));
        Assert.That(await NativeModuleCompiler.RunGraphJitAsync(nativeConsumer, [nativeReplacement],
            Apphost(), TimeSpan.FromSeconds(30), policy).ConfigureAwait(true), Is.EqualTo(42));
        Assert.That(SHA256.HashData(nativeConsumer), Is.EqualTo(nativeHash));
    }
}
