// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Terrum.CodeAnalysis.Text;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests;

public sealed class ModuleTests
{
    [Test]
    public async Task InternalAliasesEraseBeforeModulePublicationAsync()
    {
        var library = Compilation.CreateLibrary(SyntaxTree.Parse(
            "alias Count = int32; public Count Get() { return 42; }"));
        Assert.That(library.HasErrors, Is.False, string.Join(Environment.NewLine, library.Diagnostics));
        var producer = ModuleCompiler.CreateProducer(((global::System.Func<BoundProgram>)(() => { var __nunitActual0 = library.Program; Assert.That(__nunitActual0, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual0!; }))(), ["Get"]);

        var compilation = Compilation.Create(SyntaxTree.Parse(
            "int32 Main() { return Get(); }"), ModuleCompiler.CreateReference(producer));
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(((global::System.Func<BoundProgram>)(() => { var __nunitActual1 = compilation.Program; Assert.That(__nunitActual1, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual1!; }))(), producer);
        Assert.That(await ExecuteAsync(NativeEmitter.Emit(
            ModuleCompiler.Link(consumer, producer), NativeRuntimePolicy.FailFast)).ConfigureAwait(true), Is.EqualTo(42));
    }

    [Test]
    public async Task InternalGenericAliasesEraseBeforeModulePublicationAsync()
    {
        var library = Compilation.CreateLibrary(SyntaxTree.Parse(
            "alias Identity<T> = T; public Identity<int32> Get() { return 42; }"));
        Assert.That(library.HasErrors, Is.False, string.Join(Environment.NewLine, library.Diagnostics));
        var producer = ModuleCompiler.CreateProducer(((global::System.Func<BoundProgram>)(() => { var __nunitActual2 = library.Program; Assert.That(__nunitActual2, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual2!; }))(), ["Get"]);

        var compilation = Compilation.Create(SyntaxTree.Parse(
            "int32 Main() { return Get(); }"), ModuleCompiler.CreateReference(producer));
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(((global::System.Func<BoundProgram>)(() => { var __nunitActual3 = compilation.Program; Assert.That(__nunitActual3, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual3!; }))(), producer);
        Assert.That(await ExecuteAsync(NativeEmitter.Emit(
            ModuleCompiler.Link(consumer, producer), NativeRuntimePolicy.FailFast)).ConfigureAwait(true), Is.EqualTo(42));
    }
    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
    public async Task NamedArgumentsRetainParameterMappingAcrossModuleArtifactsAsync(BoundRuntimePolicy policy)
    {
        var library = Compilation.CreateLibrary(SyntaxTree.Parse("""
            public int32 Combine(int32 first, int32 second, int32 third,
                int32 fourth, int32 fifth, int32 sixth) {
                return fifth * 10 + sixth;
            }
            """), policy);
        Assert.That(library.HasErrors, Is.False, string.Join(Environment.NewLine, library.Diagnostics));
        var producer = ModuleCompiler.CreateProducer(((global::System.Func<BoundProgram>)(() => { var __nunitActual4 = library.Program; Assert.That(__nunitActual4, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual4!; }))(), ["Combine"]);
        Assert.That(BinaryPrimitives.ReadInt32LittleEndian(producer.AsSpan(4, 4)), Is.EqualTo(76));

        var compilation = Compilation.Create(SyntaxTree.Parse(
            "int32 Main() { return Combine(sixth: 2, fifth: 4, first: 0, second: 0, third: 0, fourth: 0); }"),
            ModuleCompiler.CreateReference(producer), policy);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(((global::System.Func<BoundProgram>)(() => { var __nunitActual5 = compilation.Program; Assert.That(__nunitActual5, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual5!; }))(), producer);
        Assert.That(BinaryPrimitives.ReadInt32LittleEndian(consumer.AsSpan(4, 4)), Is.EqualTo(76));
        var nativePolicy = policy == BoundRuntimePolicy.FailFast
            ? NativeRuntimePolicy.FailFast : NativeRuntimePolicy.CheckedExceptions;
        Assert.That(await ExecuteAsync(NativeEmitter.Emit(
            ModuleCompiler.Link(consumer, producer), nativePolicy)).ConfigureAwait(true), Is.EqualTo(42));
    }
    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
    public async Task NamedOutputArgumentsRetainParameterMappingAcrossModuleArtifactsAsync(
        BoundRuntimePolicy policy)
    {
        var library = Compilation.CreateLibrary(SyntaxTree.Parse("""
            public bool Populate(int32 seed, out int32 value) {
                value = seed;
                return true;
            }
            public int32 Initialize(int32 value, init ref<int32> result) {
                result = value;
                return 0;
            }
            """), policy);
        Assert.That(library.HasErrors, Is.False, string.Join(Environment.NewLine, library.Diagnostics));
        var producer = ModuleCompiler.CreateProducer(((global::System.Func<BoundProgram>)(() => { var __nunitActual6 = library.Program; Assert.That(__nunitActual6, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual6!; }))(),
            ["Populate", "Initialize"]);

        var compilation = Compilation.Create(SyntaxTree.Parse("""
            int32 Main() {
                if (!Populate(value: out var output, seed: 42)) {
                    return 1;
                }
                init int32 initialized;
                int32 status = Initialize(result: init initialized, value: output);
                return initialized + status;
            }
            """), ModuleCompiler.CreateReference(producer), policy);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(((global::System.Func<BoundProgram>)(() => { var __nunitActual7 = compilation.Program; Assert.That(__nunitActual7, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual7!; }))(), producer);
        var nativePolicy = policy == BoundRuntimePolicy.FailFast
            ? NativeRuntimePolicy.FailFast : NativeRuntimePolicy.CheckedExceptions;
        Assert.That(await ExecuteAsync(NativeEmitter.Emit(
            ModuleCompiler.Link(consumer, producer), nativePolicy)).ConfigureAwait(true), Is.EqualTo(42));
    }
    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
    public async Task FloatingOutputsBindAndExecuteAcrossModuleArtifactsAsync(
        BoundRuntimePolicy policy)
    {
        var library = Compilation.CreateLibrary(SyntaxTree.Parse("""
            public bool TryGet(out float32 value) {
                value = 3.5;
                return true;
            }
            public bool Initialize(float64 input, init ref<float64> result) {
                result = input;
                return true;
            }
            """), policy);
        Assert.That(library.HasErrors, Is.False, string.Join(Environment.NewLine, library.Diagnostics));
        var producer = ModuleCompiler.CreateProducer(((global::System.Func<BoundProgram>)(() => { var __nunitActual8 = library.Program; Assert.That(__nunitActual8, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual8!; }))(),
            ["TryGet", "Initialize"]);

        var compilation = Compilation.Create(SyntaxTree.Parse("""
            int32 Main() {
                init float64 initialized;
                if (!Initialize(7.75, init initialized)) { return 1; }
                if (!TryGet(out var output)) { return 2; }
                return initialized == 7.75 && output == 3.5 ? 42 : 0;
            }
            """), ModuleCompiler.CreateReference(producer), policy);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(((global::System.Func<BoundProgram>)(() => { var __nunitActual9 = compilation.Program; Assert.That(__nunitActual9, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual9!; }))(), producer);
        var nativePolicy = policy == BoundRuntimePolicy.FailFast
            ? NativeRuntimePolicy.FailFast : NativeRuntimePolicy.CheckedExceptions;
        Assert.That(await ExecuteAsync(NativeEmitter.Emit(
            ModuleCompiler.Link(consumer, producer), nativePolicy)).ConfigureAwait(true), Is.EqualTo(42));
    }
    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
    public async Task NonBooleanCopyOutputsBindAndExecuteAcrossModuleArtifactsAsync(
        BoundRuntimePolicy policy)
    {
        var library = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Blittable] [Layout(LayoutKind.Sequential)] public struct Pair {
                public int32 First;
                public int32 Second;
                public Pair(int32 first, int32 second) {
                    First = first;
                    Second = second;
                }
            }
            public int32 Fill(int32 first, int32 second, out Pair result) {
                result = new Pair(first, second);
                return first;
            }
            public void FillVoid(out Pair result) {
                result = new Pair(20, 22);
            }
            """), policy);
        Assert.That(library.HasErrors, Is.False, string.Join(Environment.NewLine, library.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual10 = library.Program; Assert.That(__nunitActual10, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual10!; }))();
        var producer = ModuleCompiler.CreateProducer(program, ["Fill", "FillVoid"]);

        const string ConsumerSource = """
            int32 Main() {
                int32 zeroStatus = Fill(0, 1, out var zero);
                int32 nonzeroStatus = Fill(40, 41, out var nonzero);
                FillVoid(out var voidResult);
                if (zeroStatus != 0 || zero.First != 0 || zero.Second != 1) { return 1; }
                if (nonzeroStatus != 40 || nonzero.First != 40 || nonzero.Second != 41) { return 2; }
                if (voidResult.First != 20 || voidResult.Second != 22) { return 3; }
                return 42;
            }
            """;
        var compilation = Compilation.Create(SyntaxTree.Parse(ConsumerSource),
            ModuleCompiler.CreateReference(producer), policy);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(
            ((global::System.Func<BoundProgram>)(() => { var __nunitActual11 = compilation.Program; Assert.That(__nunitActual11, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual11!; }))(), producer);
        var nativePolicy = policy == BoundRuntimePolicy.FailFast
            ? NativeRuntimePolicy.FailFast : NativeRuntimePolicy.CheckedExceptions;
        Assert.That(await ExecuteAsync(NativeEmitter.Emit(
            ModuleCompiler.Link(consumer, producer), nativePolicy)).ConfigureAwait(true), Is.EqualTo(42));

        var nativeProducer = NativeModuleCompiler.CreateProducer(program,
            ["Fill", "FillVoid"], "NonBooleanCopyOutputProvider", nativePolicy);
        var nativeCompilation = Compilation.Create(SyntaxTree.Parse(ConsumerSource),
            NativeModuleCompiler.CreateGraphReference([nativeProducer]), runtimePolicy: policy);
        Assert.That(nativeCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, nativeCompilation.Diagnostics));
        var nativeConsumer = NativeModuleCompiler.CreateGraphConsumer(
            ((global::System.Func<BoundProgram>)(() => { var __nunitActual12 = nativeCompilation.Program; Assert.That(__nunitActual12, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual12!; }))(), [nativeProducer], nativePolicy);
        Assert.That(await NativeModuleCompiler.RunGraphAsync(nativeConsumer, [nativeProducer],
            TimeSpan.FromSeconds(10), nativePolicy).ConfigureAwait(true), Is.EqualTo(42));
    }
    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
    public async Task GenericCopyOutputSpecializationsRemainPrivateAcrossModuleReplacementAsync(
        BoundRuntimePolicy policy)
    {
        const string LibrarySource = """
            [Blittable] [Layout(LayoutKind.Sequential)] public struct Pair {
                public int32 First;
                public int32 Second;
                public Pair(int32 first, int32 second) {
                    First = first;
                    Second = second;
                }
            }
            int32 Fill<T>(T value, out T result) where T : [Copy] {
                result = value;
                return 7;
            }
            public int32 Read() {
                int32 status = Fill(new Pair(20, 22), out var result);
                return status + result.First + result.Second;
            }
            """;
        const string ConsumerSource = "int32 Main() { return Read(); }";
        var library = Compilation.CreateLibrary(SyntaxTree.Parse(LibrarySource), policy);
        Assert.That(library.HasErrors, Is.False, string.Join(Environment.NewLine, library.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual13 = library.Program; Assert.That(__nunitActual13, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual13!; }))();
        var producer = ModuleCompiler.CreateProducer(program, ["Read"]);
        var compilation = Compilation.Create(SyntaxTree.Parse(ConsumerSource),
            ModuleCompiler.CreateReference(producer), policy);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(
            ((global::System.Func<BoundProgram>)(() => { var __nunitActual14 = compilation.Program; Assert.That(__nunitActual14, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual14!; }))(), producer);
        var nativePolicy = policy == BoundRuntimePolicy.FailFast
            ? NativeRuntimePolicy.FailFast : NativeRuntimePolicy.CheckedExceptions;
        Assert.That(await ExecuteAsync(NativeEmitter.Emit(
            ModuleCompiler.Link(consumer, producer), nativePolicy)).ConfigureAwait(true), Is.EqualTo(49));

        var replacementLibrary = Compilation.CreateLibrary(SyntaxTree.Parse(
            LibrarySource.Replace("return 7;", "return 8;", StringComparison.Ordinal)), policy);
        Assert.That(replacementLibrary.HasErrors, Is.False, string.Join(Environment.NewLine, replacementLibrary.Diagnostics));
        var replacementProgram = ((global::System.Func<BoundProgram>)(() => { var __nunitActual15 = replacementLibrary.Program; Assert.That(__nunitActual15, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual15!; }))();
        var replacement = ModuleCompiler.CreateProducer(replacementProgram, ["Read"]);
        Assert.That(await ExecuteAsync(NativeEmitter.Emit(
            ModuleCompiler.Link(consumer, replacement), nativePolicy)).ConfigureAwait(true), Is.EqualTo(50));

        var nativeProducer = NativeModuleCompiler.CreateProducer(
            program, ["Read"], "GenericCopyOutputProvider", nativePolicy);
        var nativeCompilation = Compilation.Create(SyntaxTree.Parse(ConsumerSource),
            NativeModuleCompiler.CreateGraphReference([nativeProducer]), runtimePolicy: policy);
        Assert.That(nativeCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, nativeCompilation.Diagnostics));
        var nativeConsumer = NativeModuleCompiler.CreateGraphConsumer(
            ((global::System.Func<BoundProgram>)(() => { var __nunitActual16 = nativeCompilation.Program; Assert.That(__nunitActual16, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual16!; }))(), [nativeProducer], nativePolicy);
        Assert.That(await NativeModuleCompiler.RunGraphAsync(nativeConsumer, [nativeProducer],
            TimeSpan.FromSeconds(10), nativePolicy).ConfigureAwait(true), Is.EqualTo(49));

        var nativeReplacement = NativeModuleCompiler.CreateProducer(
            replacementProgram, ["Read"], "GenericCopyOutputProvider", nativePolicy);
        NativeModuleCompiler.ValidateGraph(nativeConsumer, [nativeReplacement]);
        Assert.That(await NativeModuleCompiler.RunGraphAsync(nativeConsumer,
            [nativeReplacement], TimeSpan.FromSeconds(10), nativePolicy).ConfigureAwait(true), Is.EqualTo(50));
    }
    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
    public async Task GenericCopyInitSpecializationsRemainPrivateAcrossModuleReplacementAsync(
        BoundRuntimePolicy policy)
    {
        const string LibrarySource = """
            [Blittable] [Layout(LayoutKind.Sequential)] public struct Pair {
                public int32 First;
                public int32 Second;
                public Pair(int32 first, int32 second) {
                    First = first;
                    Second = second;
                }
            }
            int32 Fill<T>(T value, init ref<T> result) where T : [Copy] {
                result = value;
                return 7;
            }
            void FillVoid<T>(T value, init ref<T> result) where T : [Copy] {
                result = value;
            }
            public int32 Read() {
                int32 status = Fill(new Pair(20, 22), init var result);
                FillVoid(new Pair(5, 2), init var voidResult);
                return status + result.First + result.Second + voidResult.First + voidResult.Second;
            }
            """;
        const string ConsumerSource = "int32 Main() { return Read(); }";
        var library = Compilation.CreateLibrary(SyntaxTree.Parse(LibrarySource), policy);
        Assert.That(library.HasErrors, Is.False, string.Join(Environment.NewLine, library.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual17 = library.Program; Assert.That(__nunitActual17, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual17!; }))();
        var producer = ModuleCompiler.CreateProducer(program, ["Read"]);
        var compilation = Compilation.Create(SyntaxTree.Parse(ConsumerSource),
            ModuleCompiler.CreateReference(producer), policy);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(
            ((global::System.Func<BoundProgram>)(() => { var __nunitActual18 = compilation.Program; Assert.That(__nunitActual18, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual18!; }))(), producer);
        var nativePolicy = policy == BoundRuntimePolicy.FailFast
            ? NativeRuntimePolicy.FailFast : NativeRuntimePolicy.CheckedExceptions;
        Assert.That(await ExecuteAsync(NativeEmitter.Emit(
            ModuleCompiler.Link(consumer, producer), nativePolicy)).ConfigureAwait(true), Is.EqualTo(56));

        var replacementLibrary = Compilation.CreateLibrary(SyntaxTree.Parse(
            LibrarySource.Replace("return 7;", "return 8;", StringComparison.Ordinal)), policy);
        Assert.That(replacementLibrary.HasErrors, Is.False, string.Join(Environment.NewLine, replacementLibrary.Diagnostics));
        var replacementProgram = ((global::System.Func<BoundProgram>)(() => { var __nunitActual19 = replacementLibrary.Program; Assert.That(__nunitActual19, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual19!; }))();
        var replacement = ModuleCompiler.CreateProducer(replacementProgram, ["Read"]);
        Assert.That(await ExecuteAsync(NativeEmitter.Emit(
            ModuleCompiler.Link(consumer, replacement), nativePolicy)).ConfigureAwait(true), Is.EqualTo(57));

        var nativeProducer = NativeModuleCompiler.CreateProducer(
            program, ["Read"], "GenericCopyInitProvider", nativePolicy);
        var nativeCompilation = Compilation.Create(SyntaxTree.Parse(ConsumerSource),
            NativeModuleCompiler.CreateGraphReference([nativeProducer]), runtimePolicy: policy);
        Assert.That(nativeCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, nativeCompilation.Diagnostics));
        var nativeConsumer = NativeModuleCompiler.CreateGraphConsumer(
            ((global::System.Func<BoundProgram>)(() => { var __nunitActual20 = nativeCompilation.Program; Assert.That(__nunitActual20, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual20!; }))(), [nativeProducer], nativePolicy);
        Assert.That(await NativeModuleCompiler.RunGraphAsync(nativeConsumer, [nativeProducer],
            TimeSpan.FromSeconds(10), nativePolicy).ConfigureAwait(true), Is.EqualTo(56));

        var nativeReplacement = NativeModuleCompiler.CreateProducer(
            replacementProgram, ["Read"], "GenericCopyInitProvider", nativePolicy);
        NativeModuleCompiler.ValidateGraph(nativeConsumer, [nativeReplacement]);
        Assert.That(await NativeModuleCompiler.RunGraphAsync(nativeConsumer,
            [nativeReplacement], TimeSpan.FromSeconds(10), nativePolicy).ConfigureAwait(true), Is.EqualTo(57));
    }
    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
    public async Task GenericFloatingResultsReplaceSingleModeSpecializationsAcrossModulesAsync(
        BoundRuntimePolicy policy)
    {
        const string LibrarySource = """
            struct Pair {
                public int32 First;
                public int32 Second;
                public Pair(int32 first, int32 second) {
                    First = first;
                    Second = second;
                }
            }
            float32 FillOut<T>(T value, out T result) where T : [Copy] {
                result = value;
                return float32(1.5);
            }
            float64 FillInit<T>(T value, init ref<T> result) where T : [Copy] {
                result = value;
                return float64(2.5);
            }
            public int32 Read() {
                float32 outResult = FillOut(new Pair(20, 22), out var copied);
                float64 initResult = FillInit(new Pair(5, 2), init var initialized);
                if (outResult != float32(1.5) || copied.First != 20 || copied.Second != 22
                    || initResult != float64(2.5) || initialized.First != 5 || initialized.Second != 2) {
                    return 1;
                }
                return 42;
            }
            """;
        const string ConsumerSource = "int32 Main() { return Read(); }";
        var library = Compilation.CreateLibrary(SyntaxTree.Parse(LibrarySource), policy);
        Assert.That(library.HasErrors, Is.False, string.Join(Environment.NewLine, library.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual21 = library.Program; Assert.That(__nunitActual21, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual21!; }))();
        var producer = ModuleCompiler.CreateProducer(program, ["Read"]);
        var compilation = Compilation.Create(SyntaxTree.Parse(ConsumerSource),
            ModuleCompiler.CreateReference(producer), policy);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(
            ((global::System.Func<BoundProgram>)(() => { var __nunitActual22 = compilation.Program; Assert.That(__nunitActual22, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual22!; }))(), producer);
        var nativePolicy = policy == BoundRuntimePolicy.FailFast
            ? NativeRuntimePolicy.FailFast : NativeRuntimePolicy.CheckedExceptions;
        Assert.That(await ExecuteAsync(NativeEmitter.Emit(
            ModuleCompiler.Link(consumer, producer), nativePolicy)).ConfigureAwait(true), Is.EqualTo(42));

        var replacementSource = LibrarySource
            .Replace("float32(1.5)", "float32(3.5)", StringComparison.Ordinal)
            .Replace("float64(2.5)", "float64(4.5)", StringComparison.Ordinal)
            .Replace("return 42;", "return 43;", StringComparison.Ordinal);
        var replacementLibrary = Compilation.CreateLibrary(
            SyntaxTree.Parse(replacementSource), policy);
        Assert.That(replacementLibrary.HasErrors, Is.False, string.Join(Environment.NewLine, replacementLibrary.Diagnostics));
        var replacementProgram = ((global::System.Func<BoundProgram>)(() => { var __nunitActual23 = replacementLibrary.Program; Assert.That(__nunitActual23, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual23!; }))();
        var replacement = ModuleCompiler.CreateProducer(replacementProgram, ["Read"]);
        Assert.That(await ExecuteAsync(NativeEmitter.Emit(
            ModuleCompiler.Link(consumer, replacement), nativePolicy)).ConfigureAwait(true), Is.EqualTo(43));

        var nativeProducer = NativeModuleCompiler.CreateProducer(
            program, ["Read"], "GenericFloatingOutputProvider", nativePolicy);
        var nativeCompilation = Compilation.Create(SyntaxTree.Parse(ConsumerSource),
            NativeModuleCompiler.CreateGraphReference([nativeProducer]), runtimePolicy: policy);
        Assert.That(nativeCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, nativeCompilation.Diagnostics));
        var nativeConsumer = NativeModuleCompiler.CreateGraphConsumer(
            ((global::System.Func<BoundProgram>)(() => { var __nunitActual24 = nativeCompilation.Program; Assert.That(__nunitActual24, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual24!; }))(), [nativeProducer], nativePolicy);
        Assert.That(await NativeModuleCompiler.RunGraphAsync(nativeConsumer, [nativeProducer],
            TimeSpan.FromSeconds(10), nativePolicy).ConfigureAwait(true), Is.EqualTo(42));

        var nativeReplacement = NativeModuleCompiler.CreateProducer(
            replacementProgram, ["Read"], "GenericFloatingOutputProvider", nativePolicy);
        NativeModuleCompiler.ValidateGraph(nativeConsumer, [nativeReplacement]);
        Assert.That(await NativeModuleCompiler.RunGraphAsync(nativeConsumer,
            [nativeReplacement], TimeSpan.FromSeconds(10), nativePolicy).ConfigureAwait(true), Is.EqualTo(43));
    }
    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
    public async Task CopyOutputsRetainConditionalPublicationAcrossModuleArtifactsAsync(
        BoundRuntimePolicy policy)
    {
        const string LibrarySource = """
            [Blittable] [Layout(LayoutKind.Sequential)] public struct Pair {
                public int32 First;
                public int32 Second;
                public Pair(int32 first, int32 second) {
                    First = first;
                    Second = second;
                }
            }
            public bool TryGetPair(int32 key, out Pair result) {
                if (key == 0) { return false; }
                result = new Pair(key, 2);
                return true;
            }
            public bool TryInitializePair(int32 key, init ref<Pair> result) {
                if (key == 0) { return false; }
                result = new Pair(key, 3);
                return true;
            }
            """;
        var library = Compilation.CreateLibrary(SyntaxTree.Parse(LibrarySource), policy);
        Assert.That(library.HasErrors, Is.False, string.Join(Environment.NewLine, library.Diagnostics));
        var producer = ModuleCompiler.CreateProducer(((global::System.Func<BoundProgram>)(() => { var __nunitActual25 = library.Program; Assert.That(__nunitActual25, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual25!; }))(),
            ["TryGetPair", "TryInitializePair"]);

        const string ConsumerSource = """
            int32 Main() {
                if (!TryGetPair(40, out var original)) { return 1; }
                mut Pair copied = original;
                if (TryGetPair(0, out copied) || copied is init) { return 2; }
                if (!TryGetPair(40, out copied)) { return 2; }
                init Pair initialized;
                if (TryInitializePair(0, init initialized)) { return 3; }
                if (!TryInitializePair(40, init initialized)) { return 4; }
                return copied.First + copied.Second + initialized.First + initialized.Second;
            }
            """;
        var compilation = Compilation.Create(SyntaxTree.Parse(ConsumerSource),
            ModuleCompiler.CreateReference(producer), policy);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(((global::System.Func<BoundProgram>)(() => { var __nunitActual26 = compilation.Program; Assert.That(__nunitActual26, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual26!; }))(), producer);
        var nativePolicy = policy == BoundRuntimePolicy.FailFast
            ? NativeRuntimePolicy.FailFast : NativeRuntimePolicy.CheckedExceptions;
        Assert.That(await ExecuteAsync(NativeEmitter.Emit(
            ModuleCompiler.Link(consumer, producer), nativePolicy)).ConfigureAwait(true), Is.EqualTo(85));

        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual27 = library.Program; Assert.That(__nunitActual27, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual27!; }))();
        var nativeProvider = NativeModuleCompiler.CreateProducer(program,
            ["TryGetPair", "TryInitializePair"], "CopyOutputProvider", nativePolicy);
        var nativeCompilation = Compilation.Create(SyntaxTree.Parse(ConsumerSource),
            NativeModuleCompiler.CreateGraphReference([nativeProvider]), runtimePolicy: policy);
        Assert.That(nativeCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, nativeCompilation.Diagnostics));
        var nativeConsumer = NativeModuleCompiler.CreateGraphConsumer(
            ((global::System.Func<BoundProgram>)(() => { var __nunitActual28 = nativeCompilation.Program; Assert.That(__nunitActual28, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual28!; }))(), [nativeProvider], nativePolicy);
        Assert.That(await NativeModuleCompiler.RunGraphAsync(nativeConsumer, [nativeProvider],
            TimeSpan.FromSeconds(10), nativePolicy).ConfigureAwait(true), Is.EqualTo(85));

        var replacementLibrary = Compilation.CreateLibrary(SyntaxTree.Parse(
            LibrarySource.Replace("new Pair(key, 2)", "new Pair(key, 4)", StringComparison.Ordinal)
                .Replace("new Pair(key, 3)", "new Pair(key, 5)", StringComparison.Ordinal)), policy);
        Assert.That(replacementLibrary.HasErrors, Is.False, string.Join(Environment.NewLine, replacementLibrary.Diagnostics));
        var replacementProvider = NativeModuleCompiler.CreateProducer(
            ((global::System.Func<BoundProgram>)(() => { var __nunitActual29 = replacementLibrary.Program; Assert.That(__nunitActual29, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual29!; }))(),
            ["TryGetPair", "TryInitializePair"], "CopyOutputProvider", nativePolicy);
        NativeModuleCompiler.ValidateGraph(nativeConsumer, [replacementProvider]);
        Assert.That(await NativeModuleCompiler.RunGraphAsync(nativeConsumer, [replacementProvider],
            TimeSpan.FromSeconds(10), nativePolicy).ConfigureAwait(true), Is.EqualTo(89));
    }

    [Test]
    public async Task NativeGraphDoesNotPublishCopyInitializationOutputAfterImportedFailureAsync()
    {
        const string librarySource = """
            [Blittable] [Layout(LayoutKind.Sequential)] public struct Pair {
                public int32 First;
                public int32 Second;
                public Pair(int32 first, int32 second) {
                    First = first;
                    Second = second;
                }
            }
            public void FailAfterInitializePair(int32 key, init ref<Pair> result) {
                result = new Pair(key, 3);
                if (key == 0) { require false; }
            }
            public bool TryInitializePair(int32 key, init ref<Pair> result) {
                if (key == 0) { return false; }
                result = new Pair(key, 3);
                return true;
            }
            """;
        var library = Compilation.CreateLibrary(SyntaxTree.Parse(librarySource),
            BoundRuntimePolicy.CheckedExceptions);
        Assert.That(library.HasErrors, Is.False, string.Join(Environment.NewLine, library.Diagnostics));
        var producer = NativeModuleCompiler.CreateProducer(
            ((global::System.Func<BoundProgram>)(() => { var __nunitActual30 = library.Program; Assert.That(__nunitActual30, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual30!; }))(),
            ["FailAfterInitializePair", "TryInitializePair"], "CopyOutputFailure",
            NativeRuntimePolicy.CheckedExceptions);
        var consumerCompilation = Compilation.Create(SyntaxTree.Parse("""
            int32 Main() {
                init Pair initialized;
                unsafe {
                    try {
                        FailAfterInitializePair(0, init initialized);
                        return 1;
                    } catch {
                        if (initialized is init) { return 2; }
                        if (!TryInitializePair(40, init initialized)) { return 3; }
                    }
                }
                return initialized.First + initialized.Second;
            }
            """), NativeModuleCompiler.CreateGraphReference([producer]),
            runtimePolicy: BoundRuntimePolicy.CheckedExceptions);
        Assert.That(consumerCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, consumerCompilation.Diagnostics));
        var consumer = NativeModuleCompiler.CreateGraphConsumer(
            ((global::System.Func<BoundProgram>)(() => { var __nunitActual31 = consumerCompilation.Program; Assert.That(__nunitActual31, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual31!; }))(), [producer],
            NativeRuntimePolicy.CheckedExceptions);
        Assert.That(await NativeModuleCompiler.RunGraphAsync(consumer, [producer],
            TimeSpan.FromSeconds(10), NativeRuntimePolicy.CheckedExceptions).ConfigureAwait(true), Is.EqualTo(43));
    }
    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
    public async Task NamedArgumentExpressionsEvaluateInSourceOrderAcrossModuleArtifactsAsync(
        BoundRuntimePolicy policy)
    {
        const string functionSource = """
            public int32 Initialize(int32 first, int32 second, init ref<int32> result) {
                result = first * 10 + second;
                return 0;
            }
            """;
        const string mainSource = """
            int32 Main() {
                mut int32 trace = 0;
                init int32 initialized;
                int32 status = Initialize(
                    second: trace++,
                    result: init initialized,
                    first: trace++);
                return initialized + trace + status;
            }
            """;
        const int expected = 12;

        var hosted = Compilation.Create(SyntaxTree.Parse(functionSource + mainSource), policy);
        Assert.That(hosted.HasErrors, Is.False, string.Join(Environment.NewLine, hosted.Diagnostics));
        Assert.That(NativeJit.Execute(
            ((global::System.Func<BoundProgram>)(() => { var __nunitActual32 = hosted.Program; Assert.That(__nunitActual32, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual32!; }))(),
            policy == BoundRuntimePolicy.FailFast
                ? NativeRuntimePolicy.FailFast : NativeRuntimePolicy.CheckedExceptions), Is.EqualTo(expected));

        var library = Compilation.CreateLibrary(SyntaxTree.Parse(functionSource), policy);
        Assert.That(library.HasErrors, Is.False, string.Join(Environment.NewLine, library.Diagnostics));
        var producer = ModuleCompiler.CreateProducer(
            ((global::System.Func<BoundProgram>)(() => { var __nunitActual33 = library.Program; Assert.That(__nunitActual33, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual33!; }))(), ["Initialize"]);

        var compilation = Compilation.Create(SyntaxTree.Parse(mainSource),
            ModuleCompiler.CreateReference(producer), policy);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(((global::System.Func<BoundProgram>)(() => { var __nunitActual34 = compilation.Program; Assert.That(__nunitActual34, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual34!; }))(), producer);
        var nativePolicy = policy == BoundRuntimePolicy.FailFast
            ? NativeRuntimePolicy.FailFast : NativeRuntimePolicy.CheckedExceptions;
        Assert.That(await ExecuteAsync(NativeEmitter.Emit(
            ModuleCompiler.Link(consumer, producer), nativePolicy)).ConfigureAwait(true), Is.EqualTo(expected));
    }
    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
    public async Task PositionalArgumentExpressionsEvaluateInSourceOrderAcrossModuleArtifactsAsync(
        BoundRuntimePolicy policy)
    {
        const string functionSource = """
            public int32 Combine(int32 first, int32 second) {
                return first * 10 + second;
            }
            """;
        const string mainSource = """
            int32 Main() {
                mut int32 trace = 0;
                int32 result = Combine(trace++ + 1, trace++ + 1);
                return result * 10 + trace;
            }
            """;
        const int expected = 122;

        var hosted = Compilation.Create(SyntaxTree.Parse(functionSource + mainSource), policy);
        Assert.That(hosted.HasErrors, Is.False, string.Join(Environment.NewLine, hosted.Diagnostics));
        Assert.That(NativeJit.Execute(
            ((global::System.Func<BoundProgram>)(() => { var __nunitActual35 = hosted.Program; Assert.That(__nunitActual35, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual35!; }))(),
            policy == BoundRuntimePolicy.FailFast
                ? NativeRuntimePolicy.FailFast : NativeRuntimePolicy.CheckedExceptions), Is.EqualTo(expected));

        var library = Compilation.CreateLibrary(SyntaxTree.Parse(functionSource), policy);
        Assert.That(library.HasErrors, Is.False, string.Join(Environment.NewLine, library.Diagnostics));
        var producer = ModuleCompiler.CreateProducer(
            ((global::System.Func<BoundProgram>)(() => { var __nunitActual36 = library.Program; Assert.That(__nunitActual36, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual36!; }))(), ["Combine"]);

        var compilation = Compilation.Create(SyntaxTree.Parse(mainSource),
            ModuleCompiler.CreateReference(producer), policy);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(((global::System.Func<BoundProgram>)(() => { var __nunitActual37 = compilation.Program; Assert.That(__nunitActual37, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual37!; }))(), producer);
        var nativePolicy = policy == BoundRuntimePolicy.FailFast
            ? NativeRuntimePolicy.FailFast : NativeRuntimePolicy.CheckedExceptions;
        Assert.That(await ExecuteAsync(NativeEmitter.Emit(
            ModuleCompiler.Link(consumer, producer), nativePolicy)).ConfigureAwait(true), Is.EqualTo(expected));
    }
    [TestCase(BoundRuntimePolicy.FailFast,@"samples\native\graph\managed-class-leaf.terrum","Leaf")]
    [TestCase(BoundRuntimePolicy.CheckedExceptions,@"samples\native\graph\managed-class-leaf.terrum","Leaf")]
    [TestCase(BoundRuntimePolicy.FailFast,@"samples\native\graph\managed-copy-class-leaf.terrum","Leaf")]
    [TestCase(BoundRuntimePolicy.CheckedExceptions,@"samples\native\graph\managed-copy-class-leaf.terrum","Leaf")]
    [TestCase(BoundRuntimePolicy.FailFast,@"tests\native\managed-loop-transfer-abi.terrum","LoopTransfers")]
    [TestCase(BoundRuntimePolicy.CheckedExceptions,@"tests\native\managed-loop-transfer-abi.terrum","LoopTransfers")]
    public async Task SemanticLeafExecutesAfterConstructorAndOwnerRemappingAsync(
        BoundRuntimePolicy policy, string sample, string export)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
        var source = await File.ReadAllTextAsync(Path.Combine(root, sample)).ConfigureAwait(true);
        var library = Compilation.CreateLibrary(SyntaxTree.Parse(source), policy);
        Assert.That(library.HasErrors, Is.False, string.Join(Environment.NewLine, library.Diagnostics));
        var producer = ModuleCompiler.CreateProducer(((global::System.Func<BoundProgram>)(() => { var __nunitActual38 = library.Program; Assert.That(__nunitActual38, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual38!; }))(), [export]);
        var compilation = Compilation.Create(SyntaxTree.Parse($$"""
            struct Cell {
                public int64 Value;
                public Cell(int64 value) { Value = value; }
            }
            struct Stamp {
                public int32 Value;
                public Stamp(int32 value) { Value = value; }
            }
            int32 Main() {
                Cell first = new Cell(1);
                Stamp second = new Stamp(2);
                return {{export}}() + int32(first.Value) + second.Value - 1;
            }
            """),
            ModuleCompiler.CreateReference(producer), runtimePolicy: policy);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(((global::System.Func<BoundProgram>)(() => { var __nunitActual39 = compilation.Program; Assert.That(__nunitActual39, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual39!; }))(), producer);
        var consumerPath = Path.Combine(AppContext.BaseDirectory, $"class-module-{Guid.NewGuid():N}.trm");
        var producerPath = Path.ChangeExtension(consumerPath, ".producer.trm");
        try
        {
            await File.WriteAllBytesAsync(consumerPath, consumer).ConfigureAwait(true);
            await File.WriteAllBytesAsync(producerPath, producer).ConfigureAwait(true);
            var (exit, error) = await RunCliAsync("run-module", consumerPath, producerPath, "--runtime",
                policy == BoundRuntimePolicy.FailFast ? "fail-fast" : "checked-exceptions").ConfigureAwait(true);
            Assert.That(exit, Is.EqualTo(44));
            Assert.That(error, Is.Empty);
            Assert.That(await ExecuteAsync(NativeEmitter.Emit(ModuleCompiler.Link(consumer, producer),
                (NativeRuntimePolicy)policy)).ConfigureAwait(true), Is.EqualTo(44));
            Assert.That(await File.ReadAllBytesAsync(consumerPath).ConfigureAwait(true), Is.EqualTo(consumer));
        }
        finally
        {
            File.Delete(consumerPath);
            File.Delete(producerPath);
        }
    }
    [TestCase(7,BoundRuntimePolicy.FailFast,"native-10")]
    [TestCase(8,BoundRuntimePolicy.FailFast,"native-10")]
    [TestCase(7,BoundRuntimePolicy.CheckedExceptions,"native-11")]
    [TestCase(8,BoundRuntimePolicy.CheckedExceptions,"native-11")]
    public async Task ScalarParameterArityDoesNotDeclareNativeBufferOrViewAsync(
        int count, BoundRuntimePolicy policy, string profile)
    {
        var parameters = string.Join(", ", Enumerable.Range(0, count).Select(index => $"int32 arg{index}"));
        var arguments = string.Join(", ", Enumerable.Range(0, count).Select(index =>
            index == 0 ? "41" : index == count - 1 ? "1" : "0"));
        var library = Compilation.CreateLibrary(SyntaxTree.Parse(
            $"public int32 Sum({parameters}) {{ return arg0 + arg{count - 1}; }}"), policy);
        Assert.That(library.HasErrors, Is.False, string.Join(Environment.NewLine, library.Diagnostics));
        var producer = ModuleCompiler.CreateProducer(((global::System.Func<BoundProgram>)(() => { var __nunitActual40 = library.Program; Assert.That(__nunitActual40, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual40!; }))(), ["Sum"]);
        Assert.That(ModuleCompiler.GetNativeProfileVersion(producer, isConsumer: false), Is.EqualTo(profile));

        var compilation = Compilation.Create(SyntaxTree.Parse(
            $"int32 Main() {{ return Sum({arguments}); }}"), ModuleCompiler.CreateReference(producer),
            runtimePolicy: policy);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(((global::System.Func<BoundProgram>)(() => { var __nunitActual41 = compilation.Program; Assert.That(__nunitActual41, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual41!; }))(), producer);
        Assert.That(ModuleCompiler.GetNativeProfileVersion(consumer, isConsumer: true), Is.EqualTo(profile));
        var nativePolicy = policy == BoundRuntimePolicy.FailFast
            ? NativeRuntimePolicy.FailFast : NativeRuntimePolicy.CheckedExceptions;
        Assert.That(await ExecuteAsync(NativeEmitter.Emit(
            ModuleCompiler.Link(consumer, producer), nativePolicy)).ConfigureAwait(true), Is.EqualTo(42));
    }
    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
    public async Task SelfHostComponentTestRunnerExecutesAgainstAnIndependentMultiSourceLibraryAsync(
        BoundRuntimePolicy policy)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
        var rangePath = Path.Combine(root, "self-host", "SourceRange.terrum");
        var factsPath = Path.Combine(root, "self-host", "CharacterFacts.terrum");
        var factsTestsPath = Path.Combine(root, "self-host", "CharacterFactsTests.terrum");
        var utf16CodeUnitsPath = Path.Combine(root, "self-host", "Utf16CodeUnits.terrum");
        var integerFactsPath = Path.Combine(root, "self-host", "IntegerFacts.terrum");
        var integerFactsExportsPath = Path.Combine(root, "self-host", "IntegerFactsTestExports.terrum");
        var tokenPath = Path.Combine(root, "self-host", "Token.terrum");
        var managedTokenChainPath = Path.Combine(root, "self-host", "ManagedTokenChain.terrum");
        var managedTokenChainTestsPath = Path.Combine(root, "self-host", "ManagedTokenChainTests.terrum");
        var copyAlgorithmsPath = Path.Combine(root, "self-host", "CopyAlgorithms.terrum");
        var copyAlgorithmsTestsPath = Path.Combine(root, "self-host", "CopyAlgorithmsTests.terrum");
        var float32MathSelectionPath = Path.Combine(root, "self-host", "Float32MathSelection.terrum");
        var float64MathSelectionPath = Path.Combine(root, "self-host", "Float64MathSelection.terrum");
        var floatMathSelectionTestsPath = Path.Combine(root, "self-host", "FloatMathSelectionTests.terrum");
        var wideFactsPath = Path.Combine(root, "self-host", "WideIntegerFacts.terrum");
        var integerFormattingPath = Path.Combine(root, "self-host", "IntegerFormatting.terrum");
        var integerFormattingTestsPath = Path.Combine(root, "self-host", "IntegerFormattingTests.terrum");
        var integerFloatConversionPath = Path.Combine(root, "self-host", "IntegerFloatConversion.terrum");
        var integerFloatConversionTestsPath = Path.Combine(root, "self-host", "IntegerFloatConversionTests.terrum");
        var floatingIntegerConversionPath = Path.Combine(root, "self-host", "FloatingIntegerConversion.terrum");
        var floatingIntegerConversionTestsPath = Path.Combine(root, "self-host", "FloatingIntegerConversionTests.terrum");
        var floatingFormatConversionPath = Path.Combine(root, "self-host", "FloatingFormatConversion.terrum");
        var floatingFormatConversionTestsPath = Path.Combine(root, "self-host", "FloatingFormatConversionTests.terrum");
        var floatingNegationPath = Path.Combine(root, "self-host", "FloatingNegation.terrum");
        var floatingNegationTestsPath = Path.Combine(root, "self-host", "FloatingNegationTests.terrum");
        var char16FloatConversionPath = Path.Combine(root, "self-host", "Char16FloatConversion.terrum");
        var char16FloatConversionTestsPath = Path.Combine(root, "self-host", "Char16FloatConversionTests.terrum");
        var floatingComparisonPath = Path.Combine(root, "self-host", "FloatingComparison.terrum");
        var floatingComparisonTestsPath = Path.Combine(root, "self-host", "FloatingComparisonTests.terrum");
        var floatingArithmeticPath = Path.Combine(root, "self-host", "FloatingArithmetic.terrum");
        var floatingArithmeticTestsPath = Path.Combine(root, "self-host", "FloatingArithmeticTests.terrum");
        var checkedFactsPath = Path.Combine(root, "self-host", "IntegerCheckedArithmetic.terrum");
        var integerMathPath = Path.Combine(root, "self-host", "IntegerMath.terrum");
        var integerSaturationPath = Path.Combine(root, "self-host", "IntegerSaturation.terrum");
        var integerWrappingPath = Path.Combine(root, "self-host", "IntegerWrapping.terrum");
        var checkedConversionsPath = Path.Combine(root, "self-host", "IntegerCheckedConversions.terrum");
        var bitOperationsPath = Path.Combine(root, "self-host", "BitOperations.terrum");
        var rangeTestsPath = Path.Combine(root, "self-host", "RangeTests.terrum");
        var tokenTestsPath = Path.Combine(root, "self-host", "TokenTests.terrum");
        var wideTestsPath = Path.Combine(root, "self-host", "WideIntegerTests.terrum");
        var checkedTestsPath = Path.Combine(root, "self-host", "IntegerCheckedArithmeticTests.terrum");
        var integerMathTestsPath = Path.Combine(root, "self-host", "IntegerMathTests.terrum");
        var integerSaturationTestsPath = Path.Combine(root, "self-host", "IntegerSaturationTests.terrum");
        var integerWrappingTestsPath = Path.Combine(root, "self-host", "IntegerWrappingTests.terrum");
        var checkedConversionsTestsPath = Path.Combine(root, "self-host", "IntegerCheckedConversionsTests.terrum");
        var bitOperationsTestsPath = Path.Combine(root, "self-host", "BitOperationsTests.terrum");
        var utf16CodeUnitsTestsPath = Path.Combine(root, "self-host", "Utf16CodeUnitsTests.terrum");
        var integerFactsTestsPath = Path.Combine(root, "self-host", "IntegerFactsTests.terrum");
        var entryPath = Path.Combine(root, "self-host", "ComponentTestRunner.terrum");
        var range = SyntaxTree.Parse(await File.ReadAllTextAsync(rangePath).ConfigureAwait(true), rangePath);
        var facts = SyntaxTree.Parse(await File.ReadAllTextAsync(factsPath).ConfigureAwait(true), factsPath);
        var utf16CodeUnits = SyntaxTree.Parse(await File.ReadAllTextAsync(utf16CodeUnitsPath).ConfigureAwait(true),
            utf16CodeUnitsPath);
        var integerFacts = SyntaxTree.Parse(await File.ReadAllTextAsync(integerFactsPath).ConfigureAwait(true),
            integerFactsPath);
        var integerFactsExports = SyntaxTree.Parse(
            await File.ReadAllTextAsync(integerFactsExportsPath).ConfigureAwait(true),
            integerFactsExportsPath);
        var token = SyntaxTree.Parse(await File.ReadAllTextAsync(tokenPath).ConfigureAwait(true), tokenPath);
        var managedTokenChain = SyntaxTree.Parse(
            await File.ReadAllTextAsync(managedTokenChainPath).ConfigureAwait(true),
            managedTokenChainPath);
        var managedTokenChainTests = SyntaxTree.Parse(
            await File.ReadAllTextAsync(managedTokenChainTestsPath).ConfigureAwait(true),
            managedTokenChainTestsPath);
        var copyAlgorithms = SyntaxTree.Parse(
            await File.ReadAllTextAsync(copyAlgorithmsPath).ConfigureAwait(true),
            copyAlgorithmsPath);
        var copyAlgorithmsTests = SyntaxTree.Parse(
            await File.ReadAllTextAsync(copyAlgorithmsTestsPath).ConfigureAwait(true),
            copyAlgorithmsTestsPath);
        var float32MathSelection = SyntaxTree.Parse(
            await File.ReadAllTextAsync(float32MathSelectionPath).ConfigureAwait(true),
            float32MathSelectionPath);
        var float64MathSelection = SyntaxTree.Parse(
            await File.ReadAllTextAsync(float64MathSelectionPath).ConfigureAwait(true),
            float64MathSelectionPath);
        var floatMathSelectionTests = SyntaxTree.Parse(
            await File.ReadAllTextAsync(floatMathSelectionTestsPath).ConfigureAwait(true),
            floatMathSelectionTestsPath);
        var wideFacts = SyntaxTree.Parse(await File.ReadAllTextAsync(wideFactsPath).ConfigureAwait(true), wideFactsPath);
        var integerFormatting = SyntaxTree.Parse(
            await File.ReadAllTextAsync(integerFormattingPath).ConfigureAwait(true),
            integerFormattingPath);
        var integerFormattingTests = SyntaxTree.Parse(
            await File.ReadAllTextAsync(integerFormattingTestsPath).ConfigureAwait(true),
            integerFormattingTestsPath);
        var integerFloatConversion = SyntaxTree.Parse(
            await File.ReadAllTextAsync(integerFloatConversionPath).ConfigureAwait(true),
            integerFloatConversionPath);
        var integerFloatConversionTests = SyntaxTree.Parse(
            await File.ReadAllTextAsync(integerFloatConversionTestsPath).ConfigureAwait(true),
            integerFloatConversionTestsPath);
        var floatingIntegerConversion = SyntaxTree.Parse(
            await File.ReadAllTextAsync(floatingIntegerConversionPath).ConfigureAwait(true),
            floatingIntegerConversionPath);
        var floatingIntegerConversionTests = SyntaxTree.Parse(
            await File.ReadAllTextAsync(floatingIntegerConversionTestsPath).ConfigureAwait(true),
            floatingIntegerConversionTestsPath);
        var floatingFormatConversion = SyntaxTree.Parse(
            await File.ReadAllTextAsync(floatingFormatConversionPath).ConfigureAwait(true),
            floatingFormatConversionPath);
        var floatingFormatConversionTests = SyntaxTree.Parse(
            await File.ReadAllTextAsync(floatingFormatConversionTestsPath).ConfigureAwait(true),
            floatingFormatConversionTestsPath);
        var floatingNegation = SyntaxTree.Parse(
            await File.ReadAllTextAsync(floatingNegationPath).ConfigureAwait(true),
            floatingNegationPath);
        var floatingNegationTests = SyntaxTree.Parse(
            await File.ReadAllTextAsync(floatingNegationTestsPath).ConfigureAwait(true),
            floatingNegationTestsPath);
        var char16FloatConversion = SyntaxTree.Parse(
            await File.ReadAllTextAsync(char16FloatConversionPath).ConfigureAwait(true),
            char16FloatConversionPath);
        var char16FloatConversionTests = SyntaxTree.Parse(
            await File.ReadAllTextAsync(char16FloatConversionTestsPath).ConfigureAwait(true),
            char16FloatConversionTestsPath);
        var floatingComparison = SyntaxTree.Parse(
            await File.ReadAllTextAsync(floatingComparisonPath).ConfigureAwait(true),
            floatingComparisonPath);
        var floatingComparisonTests = SyntaxTree.Parse(
            await File.ReadAllTextAsync(floatingComparisonTestsPath).ConfigureAwait(true),
            floatingComparisonTestsPath);
        var floatingArithmetic = SyntaxTree.Parse(
            await File.ReadAllTextAsync(floatingArithmeticPath).ConfigureAwait(true),
            floatingArithmeticPath);
        var floatingArithmeticTests = SyntaxTree.Parse(
            await File.ReadAllTextAsync(floatingArithmeticTestsPath).ConfigureAwait(true),
            floatingArithmeticTestsPath);
        var checkedFacts = SyntaxTree.Parse(await File.ReadAllTextAsync(checkedFactsPath).ConfigureAwait(true),
            checkedFactsPath);
        var integerMath = SyntaxTree.Parse(await File.ReadAllTextAsync(integerMathPath).ConfigureAwait(true),
            integerMathPath);
        var integerSaturation = SyntaxTree.Parse(await File.ReadAllTextAsync(integerSaturationPath).ConfigureAwait(true),
            integerSaturationPath);
        var integerWrapping = SyntaxTree.Parse(await File.ReadAllTextAsync(integerWrappingPath).ConfigureAwait(true),
            integerWrappingPath);
        var checkedConversions = SyntaxTree.Parse(
            await File.ReadAllTextAsync(checkedConversionsPath).ConfigureAwait(true),
            checkedConversionsPath);
        var bitOperations = SyntaxTree.Parse(await File.ReadAllTextAsync(bitOperationsPath).ConfigureAwait(true),
            bitOperationsPath);
        var library = Compilation.CreateLibrary([range, facts, utf16CodeUnits, integerFacts, integerFactsExports,
            token, managedTokenChain, managedTokenChainTests, copyAlgorithms, copyAlgorithmsTests,
            float32MathSelection, float64MathSelection, floatMathSelectionTests, wideFacts,
            integerFormatting, integerFormattingTests, integerFloatConversion, integerFloatConversionTests,
            floatingIntegerConversion, floatingIntegerConversionTests, floatingFormatConversion,
            floatingFormatConversionTests, floatingNegation, floatingNegationTests, checkedFacts,
            char16FloatConversion, char16FloatConversionTests, floatingComparison, floatingComparisonTests,
            floatingArithmetic, floatingArithmeticTests,
            integerMath, integerSaturation, integerWrapping, checkedConversions, bitOperations], policy);
        Assert.That(library.HasErrors, Is.False, string.Join(Environment.NewLine, library.Diagnostics));
        var producer = ModuleCompiler.CreateProducer(((global::System.Func<BoundProgram>)(() => { var __nunitActual42 = library.Program; Assert.That(__nunitActual42, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual42!; }))(), [
            "Terrum.CodeAnalysis.SourceRange.TryCreate",
            "Terrum.CodeAnalysis.SourceRange.GetEnd",
            "Terrum.CodeAnalysis.SourceRange.#get:End",
            "Terrum.CodeAnalysis.IsAsciiDigit",
            "Terrum.CodeAnalysis.IsLineBreak",
            "Terrum.CodeAnalysis.GetDecimalDigit",
            "Terrum.CodeAnalysis.TryGetHexDigit",
            "Terrum.CodeAnalysis.DecodeUtf16Scalar",
            "Terrum.CodeAnalysis.Utf16ScalarWidth",
            "IntegerFactsInt32Mask",
            "IntegerFactsUInt32Mask",
            "IntegerFactsInt64Mask",
            "IntegerFactsUInt64Mask",
            "Terrum.CodeAnalysis.Tests.CheckIntegerFormatting",
            "Terrum.CodeAnalysis.Tests.CheckIntegerFloatConversion",
            "Terrum.CodeAnalysis.Tests.CheckChar16FloatConversion",
            "Terrum.CodeAnalysis.Tests.CheckFloatingComparison",
            "Terrum.CodeAnalysis.Tests.CheckFloatingArithmetic",
            "Terrum.CodeAnalysis.Tests.CheckFloatingNegation",
            "Terrum.CodeAnalysis.Tests.CheckFloatingFormatConversion",
            "Terrum.CodeAnalysis.Tests.CheckFloatingIntegerConversion",
            "Terrum.CodeAnalysis.Tests.CheckCopyAlgorithms",
            "Terrum.CodeAnalysis.Tests.CheckFiniteFloatMathSelections",
            "Terrum.CodeAnalysis.CheckManagedTokenChain",
            "Terrum.CodeAnalysis.Token.#ctor",
            "Terrum.CodeAnalysis.Numerics64.TryAddInt64",
            "Terrum.CodeAnalysis.Numerics64.TrySubtractInt64",
            "Terrum.CodeAnalysis.Numerics64.TryMultiplyInt64",
            "Terrum.CodeAnalysis.Numerics64.TryDivideInt64",
            "Terrum.CodeAnalysis.Numerics64.TryRemainderInt64",
            "Terrum.CodeAnalysis.Numerics64.TryDivideUInt64",
            "Terrum.CodeAnalysis.Numerics64.TryRemainderUInt64",
            "Terrum.CodeAnalysis.Numerics64.TryAccumulateDecimal",
            "Terrum.CodeAnalysis.Numerics64.TryAccumulateBase",
            "Terrum.CodeAnalysis.Numerics64.TryAddInt32",
            "Terrum.CodeAnalysis.Numerics64.TrySubtractInt32",
            "Terrum.CodeAnalysis.Numerics64.TryMultiplyInt32",
            "Terrum.CodeAnalysis.Numerics64.TryDivideInt32",
            "Terrum.CodeAnalysis.Numerics64.TryRemainderInt32",
            "Terrum.CodeAnalysis.Numerics64.TryNegateInt32",
            "Terrum.CodeAnalysis.Numerics64.TryAbsoluteInt32",
            "Terrum.CodeAnalysis.Numerics64.MagnitudeInt32",
            "Terrum.CodeAnalysis.Numerics64.TryAddUInt32",
            "Terrum.CodeAnalysis.Numerics64.TrySubtractUInt32",
            "Terrum.CodeAnalysis.Numerics64.TryMultiplyUInt32",
            "Terrum.CodeAnalysis.Numerics64.TryDivideUInt32",
            "Terrum.CodeAnalysis.Numerics64.TryRemainderUInt32",
            "Terrum.CodeAnalysis.Numerics64.AbsInt32",
            "Terrum.CodeAnalysis.Numerics64.TryAbsInt32",
            "Terrum.CodeAnalysis.Numerics64.SignInt32",
            "Terrum.CodeAnalysis.Numerics64.MinInt32",
            "Terrum.CodeAnalysis.Numerics64.MaxInt32",
            "Terrum.CodeAnalysis.Numerics64.ClampInt32",
            "Terrum.CodeAnalysis.Numerics64.BigMultiplyInt32",
            "Terrum.CodeAnalysis.Numerics64.GreatestCommonDivisorInt32",
            "Terrum.CodeAnalysis.Numerics64.AbsInt64",
            "Terrum.CodeAnalysis.Numerics64.TryAbsInt64",
            "Terrum.CodeAnalysis.Numerics64.SignInt64",
            "Terrum.CodeAnalysis.Numerics64.MinInt64",
            "Terrum.CodeAnalysis.Numerics64.MaxInt64",
            "Terrum.CodeAnalysis.Numerics64.ClampInt64",
            "Terrum.CodeAnalysis.Numerics64.GreatestCommonDivisorInt64",
            "Terrum.CodeAnalysis.Numerics64.SaturateInt32ToInt64",
            "Terrum.CodeAnalysis.Numerics64.SaturateInt32ToUInt64",
            "Terrum.CodeAnalysis.Numerics64.SaturateUInt32ToInt64",
            "Terrum.CodeAnalysis.Numerics64.SaturateUInt32ToUInt64",
            "Terrum.CodeAnalysis.Numerics64.SaturateUInt32ToInt32",
            "Terrum.CodeAnalysis.Numerics64.SaturateInt32ToUInt32",
            "Terrum.CodeAnalysis.Numerics64.SaturateInt64ToInt32",
            "Terrum.CodeAnalysis.Numerics64.SaturateInt64ToUInt32",
            "Terrum.CodeAnalysis.Numerics64.SaturateUInt64ToInt32",
            "Terrum.CodeAnalysis.Numerics64.SaturateUInt64ToUInt32",
            "Terrum.CodeAnalysis.Numerics64.SaturateUInt64ToInt64",
            "Terrum.CodeAnalysis.Numerics64.SaturateInt64ToUInt64",
            "Terrum.CodeAnalysis.Numerics64.SaturateInt32ToChar16",
            "Terrum.CodeAnalysis.Numerics64.SaturateUInt32ToChar16",
            "Terrum.CodeAnalysis.Numerics64.SaturateInt64ToChar16",
            "Terrum.CodeAnalysis.Numerics64.SaturateUInt64ToChar16",
            "Terrum.CodeAnalysis.Numerics64.SaturateChar16ToInt32",
            "Terrum.CodeAnalysis.Numerics64.SaturateChar16ToUInt32",
            "Terrum.CodeAnalysis.Numerics64.SaturateChar16ToInt64",
            "Terrum.CodeAnalysis.Numerics64.SaturateChar16ToUInt64",
            "Terrum.CodeAnalysis.Numerics64.WrapInt32ToInt64",
            "Terrum.CodeAnalysis.Numerics64.WrapInt32ToUInt64",
            "Terrum.CodeAnalysis.Numerics64.WrapUInt32ToInt64",
            "Terrum.CodeAnalysis.Numerics64.WrapUInt32ToUInt64",
            "Terrum.CodeAnalysis.Numerics64.WrapInt32ToUInt32",
            "Terrum.CodeAnalysis.Numerics64.WrapUInt32ToInt32",
            "Terrum.CodeAnalysis.Numerics64.WrapInt64ToUInt32",
            "Terrum.CodeAnalysis.Numerics64.WrapInt64ToInt32",
            "Terrum.CodeAnalysis.Numerics64.WrapUInt64ToUInt32",
            "Terrum.CodeAnalysis.Numerics64.WrapUInt64ToInt32",
            "Terrum.CodeAnalysis.Numerics64.WrapInt64ToUInt64",
            "Terrum.CodeAnalysis.Numerics64.WrapUInt64ToInt64",
            "Terrum.CodeAnalysis.Numerics64.WrapInt32ToChar16",
            "Terrum.CodeAnalysis.Numerics64.WrapUInt32ToChar16",
            "Terrum.CodeAnalysis.Numerics64.WrapInt64ToChar16",
            "Terrum.CodeAnalysis.Numerics64.WrapUInt64ToChar16",
            "Terrum.CodeAnalysis.Numerics64.WrapChar16ToInt32",
            "Terrum.CodeAnalysis.Numerics64.WrapChar16ToUInt32",
            "Terrum.CodeAnalysis.Numerics64.WrapChar16ToInt64",
            "Terrum.CodeAnalysis.Numerics64.WrapChar16ToUInt64",
            "Terrum.CodeAnalysis.Numerics64.TryConvertInt32ToInt64",
            "Terrum.CodeAnalysis.Numerics64.TryConvertInt32ToUInt64",
            "Terrum.CodeAnalysis.Numerics64.TryConvertUInt32ToInt64",
            "Terrum.CodeAnalysis.Numerics64.TryConvertUInt32ToUInt64",
            "Terrum.CodeAnalysis.Numerics64.TryConvertUInt32ToInt32",
            "Terrum.CodeAnalysis.Numerics64.TryConvertInt32ToUInt32",
            "Terrum.CodeAnalysis.Numerics64.TryConvertInt64ToInt32",
            "Terrum.CodeAnalysis.Numerics64.TryConvertInt64ToUInt32",
            "Terrum.CodeAnalysis.Numerics64.TryConvertUInt64ToInt32",
            "Terrum.CodeAnalysis.Numerics64.TryConvertUInt64ToUInt32",
            "Terrum.CodeAnalysis.Numerics64.TryConvertUInt64ToInt64",
            "Terrum.CodeAnalysis.Numerics64.TryConvertInt64ToUInt64",
            "Terrum.CodeAnalysis.Numerics64.TryConvertInt32ToChar16",
            "Terrum.CodeAnalysis.Numerics64.TryConvertUInt32ToChar16",
            "Terrum.CodeAnalysis.Numerics64.TryConvertInt64ToChar16",
            "Terrum.CodeAnalysis.Numerics64.TryConvertUInt64ToChar16",
            "Terrum.CodeAnalysis.Numerics64.TryConvertChar16ToInt32",
            "Terrum.CodeAnalysis.Numerics64.TryConvertChar16ToUInt32",
            "Terrum.CodeAnalysis.Numerics64.TryConvertChar16ToInt64",
            "Terrum.CodeAnalysis.Numerics64.TryConvertChar16ToUInt64",
            "Terrum.CodeAnalysis.Numerics64.LeadingZeroCountUInt32",
            "Terrum.CodeAnalysis.Numerics64.TrailingZeroCountUInt32",
            "Terrum.CodeAnalysis.Numerics64.PopCountUInt32",
            "Terrum.CodeAnalysis.Numerics64.RotateLeftUInt32",
            "Terrum.CodeAnalysis.Numerics64.RotateRightUInt32",
            "Terrum.CodeAnalysis.Numerics64.ReverseBitsUInt32",
            "Terrum.CodeAnalysis.Numerics64.ReverseEndiannessUInt32",
            "Terrum.CodeAnalysis.Numerics64.IsPow2UInt32",
            "Terrum.CodeAnalysis.Numerics64.TryRoundUpToPowerOfTwoUInt32",
            "Terrum.CodeAnalysis.Numerics64.LeadingZeroCountUInt64",
            "Terrum.CodeAnalysis.Numerics64.TrailingZeroCountUInt64",
            "Terrum.CodeAnalysis.Numerics64.PopCountUInt64",
            "Terrum.CodeAnalysis.Numerics64.RotateLeftUInt64",
            "Terrum.CodeAnalysis.Numerics64.RotateRightUInt64",
            "Terrum.CodeAnalysis.Numerics64.ReverseBitsUInt64",
            "Terrum.CodeAnalysis.Numerics64.ReverseEndiannessUInt64",
            "Terrum.CodeAnalysis.Numerics64.IsPow2UInt64",
            "Terrum.CodeAnalysis.Numerics64.TryRoundUpToPowerOfTwoUInt64",
        ]);
        var rangeTests = SyntaxTree.Parse(await File.ReadAllTextAsync(rangeTestsPath).ConfigureAwait(true), rangeTestsPath);
        var factsTests = SyntaxTree.Parse(await File.ReadAllTextAsync(factsTestsPath).ConfigureAwait(true), factsTestsPath);
        var utf16CodeUnitsTests = SyntaxTree.Parse(
            await File.ReadAllTextAsync(utf16CodeUnitsTestsPath).ConfigureAwait(true),
            utf16CodeUnitsTestsPath);
        var integerFactsTests = SyntaxTree.Parse(
            await File.ReadAllTextAsync(integerFactsTestsPath).ConfigureAwait(true),
            integerFactsTestsPath);
        var tokenTests = SyntaxTree.Parse(await File.ReadAllTextAsync(tokenTestsPath).ConfigureAwait(true), tokenTestsPath);
        var wideTests = SyntaxTree.Parse(await File.ReadAllTextAsync(wideTestsPath).ConfigureAwait(true), wideTestsPath);
        var checkedTests = SyntaxTree.Parse(await File.ReadAllTextAsync(checkedTestsPath).ConfigureAwait(true),
            checkedTestsPath);
        var integerMathTests = SyntaxTree.Parse(await File.ReadAllTextAsync(integerMathTestsPath).ConfigureAwait(true),
            integerMathTestsPath);
        var integerSaturationTests = SyntaxTree.Parse(
            await File.ReadAllTextAsync(integerSaturationTestsPath).ConfigureAwait(true),
            integerSaturationTestsPath);
        var integerWrappingTests = SyntaxTree.Parse(
            await File.ReadAllTextAsync(integerWrappingTestsPath).ConfigureAwait(true),
            integerWrappingTestsPath);
        var checkedConversionsTests = SyntaxTree.Parse(
            await File.ReadAllTextAsync(checkedConversionsTestsPath).ConfigureAwait(true),
            checkedConversionsTestsPath);
        var bitOperationsTests = SyntaxTree.Parse(await File.ReadAllTextAsync(bitOperationsTestsPath).ConfigureAwait(true),
            bitOperationsTestsPath);
        var entry = SyntaxTree.Parse(await File.ReadAllTextAsync(entryPath).ConfigureAwait(true), entryPath);
        var compilation = Compilation.Create([rangeTests, factsTests, utf16CodeUnitsTests, integerFactsTests,
            tokenTests, wideTests, checkedTests, integerMathTests, integerSaturationTests, integerWrappingTests,
            checkedConversionsTests, bitOperationsTests, entry],
            ModuleCompiler.CreateReference(producer), policy);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        { var __nunitItems43 = global::System.Linq.Enumerable.ToArray(compilation.SyntaxTrees); Assert.That(__nunitItems43, Has.Length.EqualTo(13)); Assert.That(__nunitItems43[0], Is.SameAs(rangeTests)); Assert.That(__nunitItems43[1], Is.SameAs(factsTests)); Assert.That(__nunitItems43[2], Is.SameAs(utf16CodeUnitsTests)); Assert.That(__nunitItems43[3], Is.SameAs(integerFactsTests)); Assert.That(__nunitItems43[4], Is.SameAs(tokenTests)); Assert.That(__nunitItems43[5], Is.SameAs(wideTests)); Assert.That(__nunitItems43[6], Is.SameAs(checkedTests)); Assert.That(__nunitItems43[7], Is.SameAs(integerMathTests)); Assert.That(__nunitItems43[8], Is.SameAs(integerSaturationTests)); Assert.That(__nunitItems43[9], Is.SameAs(integerWrappingTests)); Assert.That(__nunitItems43[10], Is.SameAs(checkedConversionsTests)); Assert.That(__nunitItems43[11], Is.SameAs(bitOperationsTests)); Assert.That(__nunitItems43[12], Is.SameAs(entry)); }
        var consumer = ModuleCompiler.CreateConsumer(((global::System.Func<BoundProgram>)(() => { var __nunitActual44 = compilation.Program; Assert.That(__nunitActual44, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual44!; }))(), producer);
        var consumerPath = Path.Combine(AppContext.BaseDirectory, $"self-host-module-{Guid.NewGuid():N}.trm");
        var producerPath = Path.ChangeExtension(consumerPath, ".producer.trm");
        try
        {
            await File.WriteAllBytesAsync(consumerPath, consumer).ConfigureAwait(true);
            await File.WriteAllBytesAsync(producerPath, producer).ConfigureAwait(true);
            string runtime = policy == BoundRuntimePolicy.FailFast ? "fail-fast" : "checked-exceptions";
            var (exit, error) = await RunCliAsync(
                "run-module", consumerPath, producerPath, "--runtime", runtime).ConfigureAwait(true);
            Assert.That(exit, Is.EqualTo(0));
            Assert.That(error, Is.Empty);
            Assert.That(await ExecuteAsync(NativeEmitter.Emit(ModuleCompiler.Link(consumer, producer),
                (NativeRuntimePolicy)policy)).ConfigureAwait(true), Is.EqualTo(0));
            Assert.That(await File.ReadAllBytesAsync(consumerPath).ConfigureAwait(true), Is.EqualTo(consumer));
        }
        finally
        {
            File.Delete(consumerPath);
            File.Delete(producerPath);
        }
    }
    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
    public async Task SelfHostSourceLineIndexComponentRunnerExecutesWithFreshSourceGrantAsync(
        BoundRuntimePolicy policy)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
        var componentNames = new[] {
            "SourceRange",
            "CharacterFacts",
            "SourceText",
            "SourcePosition",
            "SourceLineIndex",
            "SourceDiagnosticLocation",
            "SourceLineIndexTests",
            "SourceDiagnosticLocationTests",
        };
        var components = new SyntaxTree[componentNames.Length];
        for (var index = 0; index < componentNames.Length; index++)
        {
            var path = Path.Combine(root, "self-host", componentNames[index] + ".terrum");
            components[index] = SyntaxTree.Parse(await File.ReadAllTextAsync(path).ConfigureAwait(true), path);
            Assert.That(components[index].HasErrors, Is.False, string.Join(Environment.NewLine, components[index].Diagnostics));
        }

        const string sourceText = "A\U0001F600\r\nb\n";
        ImmutableArray<HostBuffer> CreateSourceGrant(string text)
        {
            return [
                new HostBuffer("Source", PrimitiveType.Char16,
                    [.. text.Select(static codeUnit => (int)codeUnit)], false) {
                    IsInvocationScoped = true,
                },
            ];
        }

        var library = Compilation.CreateLibrary(components, CreateSourceGrant(sourceText), policy);
        Assert.That(library.HasErrors, Is.False, string.Join(Environment.NewLine, library.Diagnostics));
        var producer = ModuleCompiler.CreateProducer(((global::System.Func<BoundProgram>)(() => { var __nunitActual45 = library.Program; Assert.That(__nunitActual45, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual45!; }))(),
            ["Terrum.CodeAnalysis.Tests.CheckSourceLineIndex",
                "Terrum.CodeAnalysis.Tests.CheckSourceDiagnosticLocation",
                "Terrum.CodeAnalysis.TryGetSourceDiagnosticLocation"]);

        var runnerPath = Path.Combine(root, "self-host", "SourceLineIndexComponentTestRunner.terrum");
        var runner = SyntaxTree.Parse(await File.ReadAllTextAsync(runnerPath).ConfigureAwait(true), runnerPath);
        var compilation = Compilation.Create(runner, ModuleCompiler.CreateReference(producer), policy);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(((global::System.Func<BoundProgram>)(() => { var __nunitActual46 = compilation.Program; Assert.That(__nunitActual46, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual46!; }))(), producer);
        var linked = ModuleCompiler.Link(consumer, producer);
        Assert.That(linked.RequiresSourceGrant, Is.True);
        foreach (var sourceFixture in new[] { sourceText, "", "\r\n\n" })
        {
            Assert.That(NativeJit.ExecuteWithSourceGrant(linked,
                CreateSourceGrant(sourceFixture), (NativeRuntimePolicy)policy), Is.EqualTo(0));
        }

        var diagnosticRunner = SyntaxTree.Parse("""
            using Terrum.CodeAnalysis.Tests;
            int32 Main() {
                require CheckSourceDiagnosticLocation();
                return 0;
            }
            """, "source-diagnostic-location-component-runner.terrum");
        var diagnosticCompilation = Compilation.Create(diagnosticRunner,
            ModuleCompiler.CreateReference(producer), policy);
        Assert.That(diagnosticCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, diagnosticCompilation.Diagnostics));
        var diagnosticConsumer = ModuleCompiler.CreateConsumer(
            ((global::System.Func<BoundProgram>)(() => { var __nunitActual47 = diagnosticCompilation.Program; Assert.That(__nunitActual47, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual47!; }))(), producer);
        var diagnosticLinked = ModuleCompiler.Link(diagnosticConsumer, producer);
        var diagnosticFixtures = new[] {
            "",
            "\n",
            "\r",
            "\r\n",
            "a\n",
            "a\r",
            "a\r\n",
            "A\U0001F600\r\nb\n",
        };
        var diagnosticConsumers = new BoundProgram[diagnosticFixtures.Max(static fixture => fixture.Length) + 1];
        for (var position = 0; position < diagnosticConsumers.Length; position++)
        {
            var positionConsumer = SyntaxTree.Parse($$"""
                using Terrum.CodeAnalysis;
                int32 Main() {
                    if (!TryGetSourceDiagnosticLocation({{position}},
                        init var line, init var column)) { return -1; }
                    return line * 256 + column;
                }
                """, "source-diagnostic-location-position-consumer.terrum");
            var positionCompilation = Compilation.Create(positionConsumer,
                ModuleCompiler.CreateReference(producer), policy);
            Assert.That(positionCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, positionCompilation.Diagnostics));
            var consumerProgram = ModuleCompiler.CreateConsumer(
                ((global::System.Func<BoundProgram>)(() => { var __nunitActual48 = positionCompilation.Program; Assert.That(__nunitActual48, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual48!; }))(), producer);
            diagnosticConsumers[position] = ModuleCompiler.Link(consumerProgram, producer);
        }

        foreach (var sourceFixture in diagnosticFixtures)
        {
            var source = SourceText.From(sourceFixture);
            var grant = CreateSourceGrant(sourceFixture);
            Assert.That(NativeJit.ExecuteWithSourceGrant(diagnosticLinked, grant, (NativeRuntimePolicy)policy), Is.EqualTo(0));
            for (var position = 0; position <= source.Length; position++)
            {
                var location = source.GetLinePosition(position);
                var expected = (location.Line + 1) * 256 + location.Character + 1;
                Assert.That(NativeJit.ExecuteWithSourceGrant(diagnosticConsumers[position],
                    grant, (NativeRuntimePolicy)policy), Is.EqualTo(expected));
            }
        }
    }
    [TestCase("range.Start")]
    [TestCase("range.End")]
    public async Task FailedRangeInitCannotPublishFieldsAsync(string read)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
        var source = Path.Combine(root, "self-host", "SourceRange.terrum");
        var range = SyntaxTree.Parse(await File.ReadAllTextAsync(source).ConfigureAwait(true), source);
        var entry = SyntaxTree.Parse($$"""
            namespace Terrum.CodeAnalysis;
            int32 Main() {
                bool created = SourceRange.TryCreate(-1, 0, init var range);
                return {{read}};
            }
            """);
        var compilation = Compilation.Create([range, entry]);
        Assert.That(compilation.HasErrors, Is.True);
        Assert.That(compilation.Program, Is.Null);
        Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,static diagnostic => diagnostic.Code == "TRM3016"),Is.True);
    }
    [TestCase(false)]
    [TestCase(true)]
    public async Task CliBuildsMultiSourceSelfHostLibraryAndConsumerAsync(bool tokens)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
        var prefix = Path.Combine(AppContext.BaseDirectory, $"self-host-cli-{Guid.NewGuid():N}");
        var producer = prefix + "-producer.trm";
        var consumer = prefix + "-consumer.trm";
        var image = prefix + ".exe";
        try
        {
            var arguments = new List<string> { "compile-module", "producer",
                Path.Combine(root, "self-host", "SourceRange.terrum"), "Terrum.CodeAnalysis.SourceRange.TryCreate",
                "--source", Path.Combine(root, "self-host", tokens ? "Token.terrum" : "CharacterFacts.terrum"),
                "--export", "Terrum.CodeAnalysis.SourceRange.GetEnd",
                "--export", "Terrum.CodeAnalysis.SourceRange.#get:End",
            };
            arguments.AddRange(tokens ? ["--export", "Terrum.CodeAnalysis.Token.#ctor"]
                : ["--export", "Terrum.CodeAnalysis.IsAsciiDigit", "--export", "Terrum.CodeAnalysis.IsLineBreak",
                    "--export", "Terrum.CodeAnalysis.GetDecimalDigit"]);
            arguments.AddRange(["-o", producer]);
            var (libraryExit, libraryError) = await RunCliAsync([.. arguments]).ConfigureAwait(true);
            Assert.That(libraryExit, Is.EqualTo(0));
            Assert.That(libraryError, Is.Empty);
            var (consumerExit, consumerError) = await RunCliAsync("compile-module", "consumer",
                Path.Combine(root, "self-host", tokens ? "TokenTests.terrum" : "RangeTests.terrum"), producer,
                "--source", Path.Combine(root, "samples", "native", "modules", tokens ? "token-entry.terrum" : "range-entry.terrum"),
                "-o", consumer).ConfigureAwait(true);
            Assert.That(consumerExit, Is.EqualTo(0));
            Assert.That(consumerError, Is.Empty);
            Assert.That((await RunJitWorkerAsync(consumer, producer).ConfigureAwait(true)).ExitCode, Is.EqualTo(0));
            Assert.That((await RunCliAsync("link-module", consumer, producer, "-o", image, "--runtime", "fail-fast")
                .ConfigureAwait(true)).ExitCode, Is.EqualTo(0));
            Assert.That(await ExecuteAsync(await File.ReadAllBytesAsync(image).ConfigureAwait(true)).ConfigureAwait(true), Is.EqualTo(0));
        }
        finally
        {
            File.Delete(producer);
            File.Delete(consumer);
            File.Delete(image);
        }
    }

    [Test]
    public async Task CliMultiSourceDiagnosticsIdentifySnapshotsAndPreserveArtifactsAsync()
    {
        var prefix = Path.Combine(AppContext.BaseDirectory, $"module-diagnostics-{Guid.NewGuid():N}");
        var first = prefix + "-first.terrum";
        var second = prefix + "-second.terrum";
        var artifact = prefix + ".trm";
        try
        {
            await File.WriteAllTextAsync(first, "public int32 A() { return B(); }").ConfigureAwait(true);
            await File.WriteAllTextAsync(second, "int32 B() { return 42; }").ConfigureAwait(true);
            Assert.That((await RunCliAsync("compile-module", "producer", first, "A",
                "--source", second, "-o", artifact).ConfigureAwait(true)).ExitCode, Is.EqualTo(0));
            var original = await File.ReadAllBytesAsync(artifact).ConfigureAwait(true);
            const string InvalidSource = "// \U00010400\nint32 B() { return Missing; }";
            await File.WriteAllTextAsync(second, InvalidSource).ConfigureAwait(true);
            var (exit, error, output) = await RunCliWithOutputAsync("compile-module", "producer", first, "A",
                "--source", second, "-o", artifact, "--json").ConfigureAwait(true);
            Assert.That(exit, Is.EqualTo(1));
            Assert.That(error, Is.Empty);
            using var document = JsonDocument.Parse(output);
            Assert.That(document.RootElement.GetProperty("schemaVersion").GetInt32(), Is.EqualTo(2));
            Assert.That(document.RootElement.GetProperty("success").GetBoolean(), Is.False);
            var sources = document.RootElement.GetProperty("sources");
            Assert.That(sources[0].GetProperty("path").GetString(), Is.EqualTo(first));
            Assert.That(sources[1].GetProperty("path").GetString(), Is.EqualTo(second));
            Assert.That(sources[1].GetProperty("sourceLength").GetInt32(), Is.EqualTo(InvalidSource.Length));
            Assert.That(sources[1].GetProperty("contentId").GetString(), Is.EqualTo(SyntaxTree.Parse(InvalidSource).ContentId));
            var diagnostic = global::System.Linq.Enumerable.Single(document.RootElement.GetProperty("diagnostics").EnumerateArray());
            Assert.That(diagnostic.GetProperty("sourceIndex").GetInt32(), Is.EqualTo(1));
            Assert.That(diagnostic.GetProperty("span").GetProperty("start").GetInt32(), Is.EqualTo(InvalidSource.IndexOf("Missing", StringComparison.Ordinal)));
            Assert.That(await File.ReadAllBytesAsync(artifact).ConfigureAwait(true), Is.EqualTo(original));
            Assert.That((await RunCliAsync("compile-module", "producer", first, "A",
                "--source", first, "-o", artifact).ConfigureAwait(true)).ExitCode, Is.EqualTo(2));
            Assert.That((await RunCliAsync("compile-module", "producer", first, "A",
                "--export", "A", "-o", artifact).ConfigureAwait(true)).ExitCode, Is.EqualTo(2));
            Assert.That(await File.ReadAllBytesAsync(artifact).ConfigureAwait(true), Is.EqualTo(original));
        }
        finally
        {
            File.Delete(first);
            File.Delete(second);
            File.Delete(artifact);
        }
    }

    [Test]
    public async Task CliRejectsCombinedUtf8SourceBudgetBeforePublishingAsync()
    {
        var prefix = Path.Combine(AppContext.BaseDirectory, $"module-source-limit-{Guid.NewGuid():N}");
        var first = prefix + "-first.terrum";
        var second = prefix + "-second.terrum";
        var artifact = prefix + ".trm";
        try
        {
            var source = "/*" + new string('\u00E9', 1_048_576) + "*/ public int32 A() { return 1; }";
            Assert.That(Encoding.UTF8.GetByteCount(source) <= 4 * 1024 * 1024, Is.True);
            Assert.That(Encoding.UTF8.GetByteCount(source) * 2 > 4 * 1024 * 1024, Is.True);
            await File.WriteAllTextAsync(first, source).ConfigureAwait(true);
            await File.WriteAllTextAsync(second, source).ConfigureAwait(true);
            var original = Produce("public int32 A() { return 1; }", "A");
            await File.WriteAllBytesAsync(artifact, original).ConfigureAwait(true);
            var (exit, error, output) = await RunCliWithOutputAsync("compile-module", "producer", first, "A",
                "--source", second, "-o", artifact, "--json").ConfigureAwait(true);
            Assert.That(exit, Is.EqualTo(1));
            Assert.That(error, Is.Empty);
            using var document = JsonDocument.Parse(output);
            Assert.That(document.RootElement.GetProperty("diagnostics")[0].GetProperty("code").GetString(), Is.EqualTo("TRM5001"));
            Assert.That(await File.ReadAllBytesAsync(artifact).ConfigureAwait(true), Is.EqualTo(original));
        }
        finally
        {
            File.Delete(first);
            File.Delete(second);
            File.Delete(artifact);
        }
    }
    [TestCase("--source",256)]
    [TestCase("--export",1024)]
    public async Task CliRejectsExcessiveModuleInputCountsBeforeReadingFilesAsync(string option, int maximum)
    {
        var arguments = new List<string> { "compile-module", "producer", "absent.terrum", "A" };
        for (var index = 0; index < maximum; index++)
        {
            arguments.Add(option);
            arguments.Add("V" + index.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        arguments.AddRange(["-o", "unused.trm", "--json"]);
        var (exit, error, output) = await RunCliWithOutputAsync([.. arguments]).ConfigureAwait(true);
        Assert.That(exit, Is.EqualTo(2));
        Assert.That(error, Is.Empty);
        using var document = JsonDocument.Parse(output);
        Assert.That(document.RootElement.GetProperty("diagnostics")[0].GetProperty("code").GetString(), Is.EqualTo("TRM2000"));
    }

    [Test]
    public void MultiSourceReferencesPreserveDiagnosticSnapshotOwnership()
    {
        var reference = ModuleCompiler.CreateReference(Produce("public int32 Value() { return 9; }", "Value"));
        var entry = SyntaxTree.Parse("int32 Main() { return Bad(); }", "entry.terrum");
        var invalid = SyntaxTree.Parse("int32 Bad() { return Value(true); }", "invalid.terrum");
        var compilation = Compilation.Create([entry, invalid], reference);
        Assert.That(compilation.HasErrors, Is.True);
        Assert.That(compilation.Program, Is.Null);
        Assert.That(compilation.SourceDiagnostics, Is.Not.Empty);
        {var __nunitItems0 = compilation.SourceDiagnostics;        Assert.Multiple(() => { foreach (var __nunitItem49 in __nunitItems0) { Assert.That(__nunitItem49.SyntaxTree, Is.SameAs(invalid)); } });}
        var library = Compilation.CreateLibrary([entry, invalid]);
        Assert.That(library.HasErrors, Is.True);
        {var __nunitItems1 = library.SourceDiagnostics;        Assert.Multiple(() => { foreach (var __nunitItem50 in __nunitItems1) { Assert.That(__nunitItem50.SyntaxTree, Is.SameAs(invalid)); } });}
        _ = Assert.Throws<ArgumentException>(() => Compilation.CreateLibrary(Array.Empty<SyntaxTree>()))!;
        _ = Assert.Throws<ArgumentException>(() => Compilation.Create(Array.Empty<SyntaxTree>(), reference))!;
    }

    [Test]
    public async Task ConsumerBindsOnlyImportedDeclarationsAndExecutesValuesAndOutputsAsync()
    {
        const string Library = """
            namespace Values;
            [Blittable] [Layout(LayoutKind.Sequential)] public struct Value {
                public int32 Number;
                public Value(int32 number) { this.Number = number; }
                public int32 Add(int32 amount) { return this.Number + amount; }
                public static bool Try(int32 number, out Value value) {
                    if (number < 0) { return false; }
                    value = new Value(number);
                    return true;
                }
            }
            """;
        var producer = Produce(Library, "Values.Value.#ctor", "Values.Value.Add", "Values.Value.Try");
        var reference = ModuleCompiler.CreateReference(producer);
        {var __nunitItems2 = reference.Functions;        Assert.Multiple(() => {
            foreach (var __nunitItem51 in __nunitItems2)
            {
                {
                    Assert.That(__nunitItem51.IsExternal, Is.True);
                    Assert.That(__nunitItem51.Body.Statements, Is.Empty);
                }
            }
        });}
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            using Values;
            namespace Consumer;
            struct Local { public int32 Number; public Local() { this.Number = 1; } }
            int32 Main() {
                Local local = new Local();
                Value initial = new Value(20);
                require Value.Try(initial.Add(local.Number), out var result);
                if (Value.Try(-1, out var unused)) { return 99; }
                return result.Number;
            }
            """), reference);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual52 = compilation.Program; Assert.That(__nunitActual52, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual52!; }))();
        _ = Assert.Throws<ArgumentException>(() => SemanticVerifier.Verify(program))!;
        _ = Assert.Throws<ArgumentException>(() => NativeEmitter.EmitCode(program, NativeRuntimePolicy.FailFast))!;
        _ = Assert.Throws<ArgumentException>(() => ModuleCompiler.CreateProducer(program, ["Consumer.Main"]))!;
        var consumer = ModuleCompiler.CreateConsumer(program, producer);
        var linked = ModuleCompiler.Link(consumer, producer);
        Assert.That(global::System.Linq.Enumerable.Any(linked.Functions,static function => function.IsExternal),Is.False);
        Assert.That(await ExecuteAsync(NativeEmitter.Emit(linked, NativeRuntimePolicy.FailFast)).ConfigureAwait(true), Is.EqualTo(21));
    }
    [TestCase("int32 Main() { return Private(); }")]
    [TestCase("int32 Main() { return Try(1); }")]
    [TestCase("int32 Main() { Try(out var value); return value; }")]
    [TestCase("int32 Main() { if (!Try(out var value)) { return value; } return 0; }")]
    [TestCase("int32 Main() { return Try(out var value); }")]
    [TestCase("public bool Try(out int32 value) { value = 0; return true; } int32 Main() { return 0; }")]
    public void ImportedDeclarationsEnforceSourceContracts(string source)
    {
        var producer = Produce("public bool Try(out int32 value) { value = Private(); return true; } "
            + "int32 Private() { return 7; }", "Try");
        var compilation = Compilation.Create(SyntaxTree.Parse(source), ModuleCompiler.CreateReference(producer));
        Assert.That(compilation.HasErrors, Is.True);
        Assert.That(compilation.Program, Is.Null);
    }

    [Test]
    public async Task PersistedConsumerAllowsPrivateLayoutsNamesAndExportAdditionsToEvolveAsync()
    {
        const string First = """
            struct State { public int32 Value; public State() { this.Value = 11; } }
            int32 Compute() { State state = new State(); return state.Value; }
            public int32 API() { return Compute(); }
            """;
        const string Second = """
            struct State { public char16 Value; public bool Valid;
                public State() { this.Value = char16(22); this.Valid = true; } }
            struct Extra { public uint32 Value; public Extra() { this.Value = 0; } }
            int32 Calculate() { State state = new State(); Extra extra = new Extra();
                require state.Valid; return int32(state.Value) + int32(extra.Value); }
            public int32 API() { return Calculate(); }
            public int32 NewAPI() { return 33; }
            """;
        var first = Produce(First, "API");
        var second = Produce(Second, "API", "NewAPI");
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            struct State { public bool Valid; public State() { this.Valid = true; } }
            int32 Compute() { return 0; }
            int32 Main() { State state = new State(); require state.Valid; return API() + Compute(); }
            """), ModuleCompiler.CreateReference(first));
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(((global::System.Func<BoundProgram>)(() => { var __nunitActual53 = compilation.Program; Assert.That(__nunitActual53, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual53!; }))(), first);
        var consumerPath = Path.Combine(AppContext.BaseDirectory, $"independent-{Guid.NewGuid():N}.trm");
        var producerPath = Path.ChangeExtension(consumerPath, ".producer.trm");
        try
        {
            await File.WriteAllBytesAsync(consumerPath, consumer).ConfigureAwait(true);
            foreach (var (producer, expected) in new[] { (first, 11), (second, 22) })
            {
                await File.WriteAllBytesAsync(producerPath, producer).ConfigureAwait(true);
                var (exit, error) = await RunJitWorkerAsync(consumerPath, producerPath).ConfigureAwait(true);
                Assert.That(exit, Is.EqualTo(expected));
                Assert.That(error, Is.Empty);
                Assert.That(await ExecuteAsync(NativeEmitter.Emit(ModuleCompiler.Link(consumer, producer),
                    NativeRuntimePolicy.FailFast)).ConfigureAwait(true), Is.EqualTo(expected));
                Assert.That(await File.ReadAllBytesAsync(consumerPath).ConfigureAwait(true), Is.EqualTo(consumer));
            }
        }
        finally
        {
            File.Delete(consumerPath);
            File.Delete(producerPath);
        }
    }

    private static byte[] Produce(string source, params string[] exports)
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source));
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        return ModuleCompiler.CreateProducer(((global::System.Func<BoundProgram>)(() => { var __nunitActual54 = compilation.Program; Assert.That(__nunitActual54, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual54!; }))(), exports);
    }

    [Test]
    public async Task ProducerLibraryNeedsNoSyntheticEntryPointAsync()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("public int32 Helper() { return 29; }"));
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var library = ((global::System.Func<BoundProgram>)(() => { var __nunitActual55 = compilation.Program; Assert.That(__nunitActual55, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual55!; }))();
        Assert.That(library.EntryPoint, Is.EqualTo(-1));
        SemanticVerifier.VerifyLibrary(library);
        _ = Assert.Throws<ArgumentException>(() => NativeEmitter.Emit(library, NativeRuntimePolicy.FailFast))!;
        var producer = ModuleCompiler.CreateProducer(library, ["Helper"]);
        var reference = Compile("public int32 Helper() { return 0; } int32 Main() { return Helper(); }");
        var consumer = ModuleCompiler.CreateConsumer(reference, producer);
        Assert.That(await ExecuteAsync(NativeEmitter.Emit(
            ModuleCompiler.Link(consumer, producer), NativeRuntimePolicy.FailFast)).ConfigureAwait(true), Is.EqualTo(29));
    }
    [TestCase("")]
    [TestCase("public int32 Helper() { return missing; }")]
    [TestCase("public bool Helper(out int32 value) { return true; }")]
    [TestCase("struct S { public int32 X; public S() { } }")]
    public void InvalidLibrariesProduceSourceDiagnostics(string source)
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source));
        Assert.That(compilation.HasErrors, Is.True);
        Assert.That(compilation.Program, Is.Null);
    }

    [Test]
    public async Task StaticLinksProduceSeparateImagesFromIndependentProducersAsync()
    {
        var first = Compile("int32 Main() { return Helper(); } public int32 Helper() { return 11; }");
        var second = Compile("int32 Main() { return 0; } public int32 Helper() { return 22; }");
        var producerOne = ModuleCompiler.CreateProducer(first, ["Helper"]);
        var consumer = ModuleCompiler.CreateConsumer(first, producerOne);
        var producerTwo = ModuleCompiler.CreateProducer(second, ["Helper"]);
        var consumerFile = Path.Combine(AppContext.BaseDirectory, $"consumer-{Guid.NewGuid():N}.trm");
        var producerFile = Path.Combine(AppContext.BaseDirectory, $"producer-{Guid.NewGuid():N}.trm");

        try
        {
            await File.WriteAllBytesAsync(consumerFile, consumer).ConfigureAwait(true);
            await File.WriteAllBytesAsync(producerFile, producerOne).ConfigureAwait(true);
            var firstProgram = ModuleCompiler.Link(await File.ReadAllBytesAsync(consumerFile).ConfigureAwait(true),
                await File.ReadAllBytesAsync(producerFile).ConfigureAwait(true));
            var originalImage = NativeEmitter.Emit(firstProgram, NativeRuntimePolicy.FailFast);
            var originalImageHash = SHA256.HashData(originalImage);
            Assert.That(await ExecuteAsync(originalImage).ConfigureAwait(true), Is.EqualTo(11));

            await File.WriteAllBytesAsync(producerFile, producerTwo).ConfigureAwait(true);
            var replacement = ModuleCompiler.Link(await File.ReadAllBytesAsync(consumerFile).ConfigureAwait(true),
                await File.ReadAllBytesAsync(producerFile).ConfigureAwait(true));
            Assert.That(await ExecuteAsync(NativeEmitter.Emit(replacement, NativeRuntimePolicy.FailFast)).ConfigureAwait(true), Is.EqualTo(22));
            Assert.That(await ExecuteAsync(originalImage).ConfigureAwait(true), Is.EqualTo(11));
            Assert.That(SHA256.HashData(originalImage), Is.EqualTo(originalImageHash));
        }
        finally
        {
            File.Delete(consumerFile);
            File.Delete(producerFile);
        }
    }

    [Test]
    public async Task PersistedConsumerLoadsCompatibleReplacementInChildJitWithoutRewritingArtifactAsync()
    {
        var first = Compile("int32 Main() { return Helper(); } public int32 Helper() { return 11; }");
        var replacement = Compile("int32 Main() { return 0; } public int32 Helper() { return 22; }");
        var producer = ModuleCompiler.CreateProducer(first, ["Helper"]);
        var consumerPath = Path.Combine(AppContext.BaseDirectory, $"consumer-{Guid.NewGuid():N}.trm");
        var producerPath = Path.Combine(AppContext.BaseDirectory, $"producer-{Guid.NewGuid():N}.trm");
        try
        {
            await File.WriteAllBytesAsync(consumerPath, ModuleCompiler.CreateConsumer(first, producer)).ConfigureAwait(true);
            var originalConsumerBytes = await File.ReadAllBytesAsync(consumerPath).ConfigureAwait(true);
            var originalConsumerHash = SHA256.HashData(originalConsumerBytes);
            await File.WriteAllBytesAsync(producerPath, producer).ConfigureAwait(true);
            var (missingPolicyExit, missingPolicyError) =
                await RunJitWorkerAsync(consumerPath, producerPath, explicitPolicy: false).ConfigureAwait(true);
            Assert.That(missingPolicyExit, Is.EqualTo(2));
            Assert.That(missingPolicyError, Does.Contain("TRM2000"));
            var (firstExit, firstError) = await RunJitWorkerAsync(consumerPath, producerPath).ConfigureAwait(true);
            Assert.That(firstExit, Is.EqualTo(11));
            Assert.That(firstError, Is.Empty);
            var (jsonExit, jsonError, jsonOutput) = await RunCliWithOutputAsync(
                "run-module", consumerPath, producerPath, "--runtime", "fail-fast", "--json").ConfigureAwait(true);
            Assert.That(jsonExit, Is.EqualTo(11));
            Assert.That(jsonError, Is.Empty);
            Assert.That(jsonOutput, Is.Empty);

            await File.WriteAllBytesAsync(producerPath, ModuleCompiler.CreateProducer(replacement, ["Helper"])).ConfigureAwait(true);
            var (replacementExit, replacementError) = await RunJitWorkerAsync(consumerPath, producerPath).ConfigureAwait(true);
            Assert.That(replacementExit, Is.EqualTo(22));
            Assert.That(replacementError, Is.Empty);
            Assert.That(await File.ReadAllBytesAsync(consumerPath).ConfigureAwait(true), Is.EqualTo(originalConsumerBytes));
            Assert.That(SHA256.HashData(await File.ReadAllBytesAsync(consumerPath).ConfigureAwait(true)), Is.EqualTo(originalConsumerHash));

            var incompatible = Compile("int32 Main() { return 0; } public bool Helper() { return true; }");
            await File.WriteAllBytesAsync(producerPath, ModuleCompiler.CreateProducer(incompatible, ["Helper"])).ConfigureAwait(true);
            var (invalidExit, invalidError) = await RunJitWorkerAsync(consumerPath, producerPath).ConfigureAwait(true);
            Assert.That(invalidExit, Is.EqualTo(1));
            Assert.That(invalidError, Does.Contain("TRM5002"));
            Assert.That(SHA256.HashData(await File.ReadAllBytesAsync(consumerPath).ConfigureAwait(true)), Is.EqualTo(originalConsumerHash));
        }
        finally
        {
            File.Delete(consumerPath);
            File.Delete(producerPath);
        }
    }

    [Test]
    public async Task CliLoadsPersistedConsumerAfterBothSourceFilesAreRemovedAsync()
    {
        var prefix = Path.Combine(AppContext.BaseDirectory, $"module-cli-{Guid.NewGuid():N}");
        var referenceSource = prefix + "-reference.terrum";
        var consumerSource = prefix + "-consumer.terrum";
        var replacementSource = prefix + "-replacement.terrum";
        var consumer = prefix + "-consumer.trm";
        var original = prefix + "-producer-v1.trm";
        var replacement = prefix + "-producer-v2.trm";
        var linked = prefix + "-linked.exe";
        try
        {
            await File.WriteAllTextAsync(referenceSource,
                "public int32 Helper() { return 11; }").ConfigureAwait(true);
            await File.WriteAllTextAsync(consumerSource, "int32 Main() { return Helper(); }").ConfigureAwait(true);
            await File.WriteAllTextAsync(replacementSource,
                "public int32 Helper() { return 22; }").ConfigureAwait(true);
            Assert.That((await RunCliAsync("compile-module", "producer", referenceSource, "Helper", "-o", original)
                .ConfigureAwait(true)).ExitCode, Is.EqualTo(0));
            Assert.That((await RunCliAsync("compile-module", "consumer", consumerSource, original, "-o", consumer)
                .ConfigureAwait(true)).ExitCode, Is.EqualTo(0));
            var (compileExit, compileError, compileOutput) = await RunCliWithOutputAsync(
                "compile-module", "producer", replacementSource, "Helper", "-o", replacement, "--json").ConfigureAwait(true);
            Assert.That(compileExit, Is.EqualTo(0));
            Assert.That(compileError, Is.Empty);
            using (var document = JsonDocument.Parse(compileOutput))
            {
                Assert.That(document.RootElement.GetProperty("schemaVersion").GetInt32(), Is.EqualTo(1));
                Assert.That(document.RootElement.GetProperty("success").GetBoolean(), Is.True);
                Assert.That(document.RootElement.GetProperty("artifact").GetProperty("kind").GetString(), Is.EqualTo("semantic-module"));
                Assert.That(document.RootElement.GetProperty("artifact").GetProperty("sha256").GetString(), Is.EqualTo(Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(replacement).ConfigureAwait(true)))));
            }
            var consumerBytes = await File.ReadAllBytesAsync(consumer).ConfigureAwait(true);
            await File.WriteAllTextAsync(consumerSource, "int32 Main() { return Helper(true); }").ConfigureAwait(true);
            Assert.That((await RunCliAsync("compile-module", "consumer", consumerSource, original, "-o", consumer)
                .ConfigureAwait(true)).ExitCode, Is.EqualTo(1));
            Assert.That(await File.ReadAllBytesAsync(consumer).ConfigureAwait(true), Is.EqualTo(consumerBytes));
            File.Delete(referenceSource);
            File.Delete(consumerSource);
            File.Delete(replacementSource);
            Assert.That((await RunJitWorkerAsync(consumer, original).ConfigureAwait(true)).ExitCode, Is.EqualTo(11));
            Assert.That((await RunJitWorkerAsync(consumer, replacement).ConfigureAwait(true)).ExitCode, Is.EqualTo(22));
            var (usageExit, usageError, usageOutput) = await RunCliWithOutputAsync(
                "link-module", consumer, original, "-o", linked, "--json").ConfigureAwait(true);
            Assert.That(usageExit, Is.EqualTo(2));
            Assert.That(usageError, Is.Empty);
            using (var document = JsonDocument.Parse(usageOutput))
            {
                Assert.That(document.RootElement.GetProperty("diagnostics")[0].GetProperty("code").GetString(), Is.EqualTo("TRM2000"));
            }
            Assert.That(File.Exists(linked), Is.False);
            Assert.That((await RunCliAsync("link-module", consumer, original, "-o", linked, "--runtime", "fail-fast")
                .ConfigureAwait(true)).ExitCode, Is.EqualTo(0));
            var linkedBytes = await File.ReadAllBytesAsync(linked).ConfigureAwait(true);
            Assert.That(await ExecuteAsync(linkedBytes).ConfigureAwait(true), Is.EqualTo(11));
            var incompatible = ModuleCompiler.CreateProducer(
                Compile("int32 Main() { return 0; } public bool Helper() { return true; }"), ["Helper"]);
            await File.WriteAllBytesAsync(replacement, incompatible).ConfigureAwait(true);
            var (failedExit, failedError, failedOutput) = await RunCliWithOutputAsync(
                "link-module", consumer, replacement, "-o", linked, "--runtime", "fail-fast", "--json").ConfigureAwait(true);
            Assert.That(failedExit, Is.EqualTo(1));
            Assert.That(failedError, Is.Empty);
            using (var document = JsonDocument.Parse(failedOutput))
            {
                Assert.That(document.RootElement.GetProperty("success").GetBoolean(), Is.False);
                Assert.That(document.RootElement.GetProperty("diagnostics")[0].GetProperty("code").GetString(), Is.EqualTo("TRM5002"));
            }
            Assert.That(await File.ReadAllBytesAsync(linked).ConfigureAwait(true), Is.EqualTo(linkedBytes));
            Assert.That(await File.ReadAllBytesAsync(consumer).ConfigureAwait(true), Is.EqualTo(consumerBytes));
        }
        finally
        {
            File.Delete(referenceSource);
            File.Delete(consumerSource);
            File.Delete(replacementSource);
            File.Delete(consumer);
            File.Delete(original);
            File.Delete(replacement);
            File.Delete(linked);
        }
    }

    [Test]
    public async Task CompileModuleReportsSourceDiagnosticsAsSchemaOneJsonAsync()
    {
        var source = Path.Combine(AppContext.BaseDirectory, $"module-source-{Guid.NewGuid():N}.terrum");
        var artifact = Path.ChangeExtension(source, ".trm");
        try
        {
            await File.WriteAllTextAsync(source, "int32 Main() { return Missing; }").ConfigureAwait(true);
            var (exitCode, error, output) = await RunCliWithOutputAsync(
                "compile-module", "producer", source, "Helper", "-o", artifact, "--json").ConfigureAwait(true);
            Assert.That(exitCode, Is.EqualTo(1));
            Assert.That(error, Is.Empty);
            using var document = JsonDocument.Parse(output);
            Assert.That(document.RootElement.GetProperty("schemaVersion").GetInt32(), Is.EqualTo(1));
            Assert.That(document.RootElement.GetProperty("command").GetString(), Is.EqualTo("compile-module"));
            Assert.That(document.RootElement.GetProperty("success").GetBoolean(), Is.False);
            Assert.That(document.RootElement.GetProperty("diagnostics").GetArrayLength() > 0, Is.True);
            Assert.That(File.Exists(artifact), Is.False);
        }
        finally
        {
            File.Delete(source);
            File.Delete(artifact);
        }
    }
    [TestCase("public bool Helper() { return true; }")]
    [TestCase("public int32 Helper(int32 value) { return value; }")]
    [TestCase("public void Helper() { }")]
    public void RejectsChangedImportedSignatureBeforeEmission(string changed)
    {
        var first = Compile("int32 Main() { return Helper(); } public int32 Helper() { return 11; }");
        var initial = ModuleCompiler.CreateProducer(first, ["Helper"]);
        var consumer = ModuleCompiler.CreateConsumer(first, initial);
        var replacement = Compile("int32 Main() { return 0; } " + changed);
        var producer = ModuleCompiler.CreateProducer(replacement, ["Helper"]);
        _ = Assert.Throws<ModuleContractException>(() => ModuleCompiler.Link(consumer, producer))!;
    }

    [Test]
    public void RejectsChangedConditionalOutputContract()
    {
        var initial = Compile("int32 Main() { if (Try(out var value)) { return value; } return 0; } "
            + "public bool Try(out int32 value) { value = 11; return true; }");
        var producer = ModuleCompiler.CreateProducer(initial, ["Try"]);
        var consumer = ModuleCompiler.CreateConsumer(initial, producer);
        var changed = Compile("int32 Main() { return 0; } public bool Try(int32 value) { return true; }");
        _ = Assert.Throws<ModuleContractException>(() =>
            ModuleCompiler.Link(consumer, ModuleCompiler.CreateProducer(changed, ["Try"])))!;
    }
    [TestCase(BoundRuntimePolicy.FailFast,false)]
    [TestCase(BoundRuntimePolicy.FailFast,true)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions,false)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions,true)]
    public void RejectsOutAndInitModeChangesAcrossSemanticAndNativeModules(
        BoundRuntimePolicy policy,
        bool initialUsesInit)
    {
        var initialDeclaration = initialUsesInit
            ? "public void Set(init ref<int32> result) { result = 42; }"
            : "public void Set(out int32 result) { result = 42; }";
        var replacementDeclaration = initialUsesInit
            ? "public void Set(out int32 result) { result = 42; }"
            : "public void Set(init ref<int32> result) { result = 42; }";
        var consumerSource = initialUsesInit
            ? "int32 Main() { init int32 result; Set(init result); return result; }"
            : "int32 Main() { Set(out var result); return result; }";

        var initialCompilation = Compilation.CreateLibrary(SyntaxTree.Parse(initialDeclaration), policy);
        Assert.That(initialCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, initialCompilation.Diagnostics));
        var initialProgram = ((global::System.Func<BoundProgram>)(() => { var __nunitActual56 = initialCompilation.Program; Assert.That(__nunitActual56, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual56!; }))();
        var semanticProvider = ModuleCompiler.CreateProducer(initialProgram, ["Set"]);
        var semanticCompilation = Compilation.Create(SyntaxTree.Parse(consumerSource),
            ModuleCompiler.CreateReference(semanticProvider), policy);
        Assert.That(semanticCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, semanticCompilation.Diagnostics));
        var semanticConsumer = ModuleCompiler.CreateConsumer(
            ((global::System.Func<BoundProgram>)(() => { var __nunitActual57 = semanticCompilation.Program; Assert.That(__nunitActual57, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual57!; }))(), semanticProvider);
        var replacementCompilation = Compilation.CreateLibrary(SyntaxTree.Parse(replacementDeclaration), policy);
        Assert.That(replacementCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, replacementCompilation.Diagnostics));
        var replacementProgram = ((global::System.Func<BoundProgram>)(() => { var __nunitActual58 = replacementCompilation.Program; Assert.That(__nunitActual58, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual58!; }))();
        var semanticReplacement = ModuleCompiler.CreateProducer(replacementProgram, ["Set"]);
        _ = Assert.Throws<ModuleContractException>(() =>
            ModuleCompiler.Link(semanticConsumer, semanticReplacement))!;

        var nativePolicy = policy == BoundRuntimePolicy.FailFast
            ? NativeRuntimePolicy.FailFast : NativeRuntimePolicy.CheckedExceptions;
        var nativeProvider = NativeModuleCompiler.CreateProducer(
            initialProgram, ["Set"], "OutputProvider", nativePolicy);
        var nativeCompilation = Compilation.Create(SyntaxTree.Parse(consumerSource),
            NativeModuleCompiler.CreateReference(nativeProvider), policy);
        Assert.That(nativeCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, nativeCompilation.Diagnostics));
        var nativeConsumer = NativeModuleCompiler.CreateConsumer(
            ((global::System.Func<BoundProgram>)(() => { var __nunitActual59 = nativeCompilation.Program; Assert.That(__nunitActual59, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual59!; }))(), nativeProvider, nativePolicy);
        var nativeReplacement = NativeModuleCompiler.CreateProducer(
            replacementProgram, ["Set"], "OutputProvider", nativePolicy);
        _ = Assert.Throws<ModuleContractException>(() =>
            NativeModuleCompiler.Validate(nativeConsumer, nativeReplacement))!;
    }
    [TestCase(
        BoundRuntimePolicy.FailFast,        "[NoThrow] public int32 Read() { return 42; }",        "public int32 Read() { return 42; }",        "int32 Main() { return Read(); }",        "Read")]
    [TestCase(
        BoundRuntimePolicy.CheckedExceptions,        "[NoThrow] public int32 Read() { return 42; }",        "public int32 Read() { return 42; }",        "int32 Main() { return Read(); }",        "Read")]
    [TestCase(
        BoundRuntimePolicy.FailFast,        "[DoesNotReturn] public void Stop() { require false; }",        "public void Stop() { }",        "void Main() { Stop(); }",        "Stop")]
    [TestCase(
        BoundRuntimePolicy.CheckedExceptions,        "[DoesNotReturn] public void Stop() { require false; }",        "public void Stop() { }",        "void Main() { Stop(); }",        "Stop")]
    public void RejectsRemovedEffectPromisesAcrossSemanticAndNativeModules(
        BoundRuntimePolicy policy,
        string initialDeclaration,
        string replacementDeclaration,
        string consumerSource,
        string export)
    {
        var initialCompilation = Compilation.CreateLibrary(SyntaxTree.Parse(initialDeclaration),
            policy);
        Assert.That(initialCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, initialCompilation.Diagnostics));
        var initialProgram = ((global::System.Func<BoundProgram>)(() => { var __nunitActual60 = initialCompilation.Program; Assert.That(__nunitActual60, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual60!; }))();
        var semanticProvider = ModuleCompiler.CreateProducer(initialProgram, [export]);
        var semanticCompilation = Compilation.Create(SyntaxTree.Parse(consumerSource),
            ModuleCompiler.CreateReference(semanticProvider), policy);
        Assert.That(semanticCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, semanticCompilation.Diagnostics));
        var semanticConsumer = ModuleCompiler.CreateConsumer(
            ((global::System.Func<BoundProgram>)(() => { var __nunitActual61 = semanticCompilation.Program; Assert.That(__nunitActual61, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual61!; }))(), semanticProvider);

        var replacementCompilation = Compilation.CreateLibrary(SyntaxTree.Parse(replacementDeclaration),
            policy);
        Assert.That(replacementCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, replacementCompilation.Diagnostics));
        var replacementProgram = ((global::System.Func<BoundProgram>)(() => { var __nunitActual62 = replacementCompilation.Program; Assert.That(__nunitActual62, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual62!; }))();
        var semanticReplacement = ModuleCompiler.CreateProducer(replacementProgram, [export]);
        _ = Assert.Throws<ModuleContractException>(() =>
            ModuleCompiler.Link(semanticConsumer, semanticReplacement))!;

        var nativePolicy = policy == BoundRuntimePolicy.FailFast
            ? NativeRuntimePolicy.FailFast : NativeRuntimePolicy.CheckedExceptions;
        var nativeProvider = NativeModuleCompiler.CreateProducer(
            initialProgram, [export], "EffectProvider", nativePolicy);
        var nativeCompilation = Compilation.Create(SyntaxTree.Parse(consumerSource),
            NativeModuleCompiler.CreateReference(nativeProvider), policy);
        Assert.That(nativeCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, nativeCompilation.Diagnostics));
        var nativeConsumer = NativeModuleCompiler.CreateConsumer(
            ((global::System.Func<BoundProgram>)(() => { var __nunitActual63 = nativeCompilation.Program; Assert.That(__nunitActual63, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual63!; }))(), nativeProvider, nativePolicy);
        var nativeReplacement = NativeModuleCompiler.CreateProducer(replacementProgram, [export],
            "EffectProvider", nativePolicy);
        _ = Assert.Throws<ModuleContractException>(() =>
            NativeModuleCompiler.Validate(nativeConsumer, nativeReplacement))!;
    }

    [Test]
    public async Task ConstructorContractsUseExplicitArgumentsRatherThanReceiverSlotsAsync()
    {
        const string Consumer = "[Blittable] [Layout(LayoutKind.Sequential)] public struct Box { public int32 Value; "
            + "public Box(int32 value) { this.Value = value; } } "
            + "int32 Main() { Box box = new Box(11); return box.Value; }";
        var first = Compile(Consumer);
        var consumer = ModuleCompiler.CreateConsumer(first, ModuleCompiler.CreateProducer(first, ["Box.#ctor"]));
        var compatible = Compile("[Blittable] [Layout(LayoutKind.Sequential)] public struct Box { public int32 Value; "
            + "public Box(int32 value) { this.Value = 22; } } int32 Main() { return 0; }");
        var linked = ModuleCompiler.Link(consumer, ModuleCompiler.CreateProducer(compatible, ["Box.#ctor"]));
        Assert.That(await ExecuteAsync(NativeEmitter.Emit(linked, NativeRuntimePolicy.FailFast)).ConfigureAwait(true), Is.EqualTo(22));

        var changed = Compile("[Blittable] [Layout(LayoutKind.Sequential)] public struct Box { public int32 Value; "
            + "public Box(uint32 value) { this.Value = int32(value); } } int32 Main() { return 0; }");
        _ = Assert.Throws<ModuleContractException>(() =>
            ModuleCompiler.Link(consumer, ModuleCompiler.CreateProducer(changed, ["Box.#ctor"])))!;
    }
    [TestCase(-1)]
    [TestCase(2)]
    public void RejectsMalformedConstructorParameterCountBeforeContractParsing(int parameterCount)
    {
        var initial = Compile("[Blittable] [Layout(LayoutKind.Sequential)] public struct Box { public int32 Value; "
            + "public Box(int32 value) { this.Value = value; } } "
            + "int32 Main() { Box box = new Box(11); return box.Value; }");
        var producer = ModuleCompiler.CreateProducer(initial, ["Box.#ctor"]);
        var consumer = ModuleCompiler.CreateConsumer(initial, producer);
        var modified = (byte[])producer.Clone();
        var name = Encoding.UTF8.GetBytes("Box.#ctor");
        var functionName = modified.AsSpan().LastIndexOf(name);
        Assert.That(functionName >= sizeof(int), Is.True);
        using var reader = new BinaryReader(new MemoryStream(modified));
        reader.BaseStream.Position = functionName + name.Length;
        _ = reader.ReadInt32();
        Assert.That(reader.ReadBoolean(), Is.True);
        for (var flag = 0; flag < 8; flag++)
        {
            _ = reader.ReadBoolean();
        }
        Assert.That(reader.ReadBoolean(), Is.False);
        Assert.That(reader.ReadByte(), Is.EqualTo((byte)BoundIntrinsic.None));
        Assert.That(reader.ReadBoolean(), Is.False);
        _ = reader.ReadInt32();
        Assert.That(reader.ReadBoolean(), Is.True);
        var ownerLength = reader.ReadInt32();
        Assert.That(Encoding.UTF8.GetString(reader.ReadBytes(ownerLength)), Is.EqualTo("Box"));
        var parameterOffset = (int)reader.BaseStream.Position;
        Assert.That(reader.ReadInt32(), Is.EqualTo(1));
        BinaryPrimitives.WriteInt32LittleEndian(modified.AsSpan(parameterOffset, sizeof(int)), parameterCount);
        _ = Assert.Throws<ModuleFormatException>(() => ModuleCompiler.Link(consumer, modified))!;
    }

    [Test]
    public async Task PersistedConsumerRejectsChangedOutputLayoutAndFieldAccessInWorkerAsync()
    {
        const string PairSource = "[Blittable] [Layout(LayoutKind.Sequential)] public struct Pair { public int32 A; public int32 B; "
            + "public Pair() { this.A = 11; this.B = 1; } "
            + "public static int32 Value() { Pair p = new Pair(); return p.A; } } "
            + "int32 Main() { return Pair.Value(); }";
        const string PairReplacement = "public Pair() { this.A = 11; this.B = 1; } "
            + "public static int32 Value() { Pair p = new Pair(); return p.A; } } int32 Main() { return 0; }";
        var cases = new (string Initial, string Export, string Changed)[] {
            ("int32 Main() { if (Try(out var value)) { return value; } return 0; } "
                + "public bool Try(out int32 value) { value = 11; return true; }",
                "Try", "int32 Main() { return 0; } public bool Try(int32 value) { return true; }"),
            (PairSource, "Pair.Value", "[Blittable] [Layout(LayoutKind.Sequential)] public struct Pair { public int32 B; public int32 A; " + PairReplacement),
            (PairSource, "Pair.Value", "[Blittable] [Layout(LayoutKind.Sequential)] public struct Pair { public int32 A; private int32 B; " + PairReplacement),
            (PairSource, "Pair.Value", "[Blittable] [Layout(LayoutKind.Sequential)] public struct Pair { public int32 A; internal int32 B; " + PairReplacement),
        };

        foreach (var (initialSource, export, changedSource) in cases)
        {
            var initial = Compile(initialSource);
            var original = ModuleCompiler.CreateProducer(initial, [export]);
            var consumerBytes = ModuleCompiler.CreateConsumer(initial, original);
            var replacement = ModuleCompiler.CreateProducer(Compile(changedSource), [export]);
            var consumerPath = Path.Combine(AppContext.BaseDirectory, $"consumer-{Guid.NewGuid():N}.trm");
            var producerPath = Path.Combine(AppContext.BaseDirectory, $"producer-{Guid.NewGuid():N}.trm");
            try
            {
                await File.WriteAllBytesAsync(consumerPath, consumerBytes).ConfigureAwait(true);
                await File.WriteAllBytesAsync(producerPath, original).ConfigureAwait(true);
                Assert.That((await RunJitWorkerAsync(consumerPath, producerPath).ConfigureAwait(true)).ExitCode, Is.EqualTo(11));
                await File.WriteAllBytesAsync(producerPath, replacement).ConfigureAwait(true);
                var (exitCode, error) = await RunJitWorkerAsync(consumerPath, producerPath).ConfigureAwait(true);
                Assert.That(exitCode, Is.EqualTo(1));
                Assert.That(error, Does.Contain("TRM5002"));
                Assert.That(await File.ReadAllBytesAsync(consumerPath).ConfigureAwait(true), Is.EqualTo(consumerBytes));
            }
            finally
            {
                File.Delete(consumerPath);
                File.Delete(producerPath);
            }
        }
    }

    [Test]
    public void PrivateFunctionCannotBecomeAnImportedGrant()
    {
        var program = Compile("[Blittable] [Layout(LayoutKind.Sequential)] public struct Secret { private int32 Value; private Secret() { this.Value = 7; } } "
            + "int32 Main() { return 0; }");
        _ = Assert.Throws<ModuleContractException>(() => ModuleCompiler.CreateProducer(program, ["Secret.#ctor"]))!;
    }

    [Test]
    public void InternalFunctionCannotBecomeAnImportedGrantFromCombinedCompilation()
    {
        var program = Compile("int32 Main() { return Helper(); } "
            + "internal int32 Helper() { return 11; }");
        Assert.That(global::System.Linq.Enumerable.Any(program.Functions,static function => function.Name == "Helper" && function.IsInternal),Is.True);
        _ = Assert.Throws<ModuleContractException>(() => ModuleCompiler.CreateProducer(program, ["Helper"]))!;

        var implicitInternal = Compile("int32 Main() { return Helper(); } int32 Helper() { return 11; }");
        Assert.That(global::System.Linq.Enumerable.Any(implicitInternal.Functions,static function => function.Name == "Helper" && function.IsInternal),Is.True);
        _ = Assert.Throws<ModuleContractException>(() => ModuleCompiler.CreateProducer(implicitInternal, ["Helper"]))!;
    }

    [Test]
    public void RejectsInaccessibleImportedTypeAndValueFieldSurface()
    {
        var internalType = Compile("internal struct Hidden { public int32 Value; "
            + "public static int32 Get() { return 11; } } int32 Main() { return 0; }");
        _ = Assert.Throws<ModuleContractException>(() => ModuleCompiler.CreateProducer(internalType, ["Hidden.Get"]))!;
        var implicitInternalType = Compile("struct Hidden { public int32 Value; "
            + "public static int32 Get() { return 11; } } int32 Main() { return 0; }");
        _ = Assert.Throws<ModuleContractException>(() => ModuleCompiler.CreateProducer(implicitInternalType, ["Hidden.Get"]))!;

        var privateField = Compile("[Blittable] [Layout(LayoutKind.Sequential)] public struct Pair { public int32 A; private int32 B; "
            + "public Pair() { this.A = 11; this.B = 1; } } "
            + "int32 Main() { return 0; } public Pair Make() { return new Pair(); }");
        _ = Assert.Throws<ModuleContractException>(() => ModuleCompiler.CreateProducer(privateField, ["Make"]))!;

        var staticField = Compilation.Create(SyntaxTree.Parse("struct S { static int32 Value; } int32 Main() { return 0; }"));
        Assert.That(global::System.Linq.Enumerable.Any(staticField.Diagnostics,static diagnostic => diagnostic.Code == "TRM3001"),Is.True);
    }

    [Test]
    public void HostBufferCapabilityCannotSilentlyEnterModuleArtifact()
    {
        var program = Compile("int32 Main() { return 0; } public int32 Helper() { return 11; }")
            with { Buffers = [new HostBuffer("Input", PrimitiveType.Int32, [1], false)] };
        _ = Assert.Throws<ModuleFormatException>(() => ModuleCompiler.CreateProducer(program, ["Helper"]))!;
    }

    [Test]
    public async Task PrivateProducerDependencyIsNotPinnedOrGrantedToConsumerAsync()
    {
        const string Main = "int32 Main() { return Secret.Get(); }";
        static string Producer(int value)
        {
            return "[Blittable] [Layout(LayoutKind.Sequential)] public struct Secret { private int32 Value; "
            + "private Secret() { this.Value = " + value.ToString(System.Globalization.CultureInfo.InvariantCulture)
            + "; } public static int32 Get() { Secret secret = new Secret(); return secret.Value; } }";
        }

        var initial = Compile(Producer(7) + Main);
        var reference = ModuleCompiler.CreateProducer(initial, ["Secret.Get"]);
        var consumer = ModuleCompiler.CreateConsumer(initial, reference);
        Assert.That(Encoding.UTF8.GetString(consumer), Does.Not.Contain("#ctor"));
        var changed = Compile(Producer(9) + "int32 Main() { return 0; }");
        var replacement = ModuleCompiler.CreateProducer(changed, ["Secret.Get"]);
        var linked = ModuleCompiler.Link(consumer, replacement);
        Assert.That(await ExecuteAsync(NativeEmitter.Emit(linked, NativeRuntimePolicy.FailFast)).ConfigureAwait(true), Is.EqualTo(9));
    }

    [Test]
    public async Task PublicButUnexportedProducerHelperIsNotPinnedInConsumerAsync()
    {
        const string Consumer = "int32 Main() { return API(); } ";
        const string ProducerOne = "public int32 API() { return Helper(); } "
            + "public int32 Helper() { return 11; }";
        const string ProducerTwo = "public int32 API() { return Helper(); } "
            + "public int32 Helper() { return 22; }";
        var first = Compile(Consumer + ProducerOne);
        var reference = ModuleCompiler.CreateProducer(first, ["API"]);
        var consumer = ModuleCompiler.CreateConsumer(first, reference);
        Assert.That(Encoding.UTF8.GetString(consumer), Does.Not.Contain("Helper"));

        var second = Compile("int32 Main() { return 0; } " + ProducerTwo);
        var replacement = ModuleCompiler.CreateProducer(second, ["API"]);
        var linked = ModuleCompiler.Link(consumer, replacement);
        Assert.That(await ExecuteAsync(NativeEmitter.Emit(linked, NativeRuntimePolicy.FailFast)).ConfigureAwait(true), Is.EqualTo(22));
    }

    [Test]
    public void RetainedBufferClearPreservesUnexportedProducerCallDiagnostic()
    {
        var producer = Produce("public int32 API() { Helper(); return 42; } void Helper() { }", "API");
        var consumer = Compile("""
            int32 Main() {
                require OwnedInt32Buffer.TryCreate(1, out var raw);
                mut OwnedInt32Buffer buffer = move raw;
                buffer.Clear();
                Helper();
                return 0;
            }
            void Helper() { }
            """);
        Assert.That(global::System.Linq.Enumerable.Any(consumer.Functions,static function => function.Intrinsic == BoundIntrinsic.BufferClear),Is.True);

        var exception = Assert.Throws<ModuleContractException>(() =>
            ModuleCompiler.CreateConsumer(consumer, producer))!;
        Assert.That(exception.Message, Is.EqualTo("Consumer cannot call an unexported producer implementation."));
    }

    [Test]
    public void UnreferencedBufferClearIsOmittedWithoutCheckingSkippedProducerImplementations()
    {
        var producer = Produce("""
            public int32 API() { Hidden(); return 42; }
            void Hidden() { Helper(); }
            void Helper() { }
            """, "API");
        var consumer = Compile("""
            int32 Main() {
                require OwnedInt32Buffer.TryCreate(1, out var buffer);
                return buffer.Count;
            }
            void Hidden() { Helper(); }
            void Helper() { }
            """);
        Assert.That(global::System.Linq.Enumerable.Any(consumer.Functions,static function => function.Intrinsic == BoundIntrinsic.BufferClear),Is.True);

        var artifact = ModuleCompiler.CreateConsumer(consumer, producer);

        Assert.That(Encoding.UTF8.GetString(artifact), Does.Not.Contain("OwnedInt32Buffer.Clear"));
    }

    [Test]
    public void TamperedProducerCannotExportPrivateDependency()
    {
        const string Source = "[Blittable] [Layout(LayoutKind.Sequential)] public struct Secret { private int32 Value; "
            + "private Secret() { this.Value = 7; } "
            + "public static int32 Get() { Secret secret = new Secret(); return secret.Value; } } "
            + "int32 Main() { return Secret.Get(); }";
        var program = Compile(Source);
        var producer = ModuleCompiler.CreateProducer(program, ["Secret.Get"]);
        var consumer = ModuleCompiler.CreateConsumer(program, producer);
        var modified = (byte[])producer.Clone();
        var privateName = Encoding.UTF8.GetBytes("Secret.#ctor");
        var offset = modified.AsSpan().IndexOf(privateName);
        Assert.That(offset >= sizeof(int), Is.True, "Private function record is missing.");
        Assert.That(BinaryPrimitives.ReadInt32LittleEndian(modified.AsSpan(offset - sizeof(int), sizeof(int))), Is.EqualTo(privateName.Length));
        var contractLengthOffset = offset + privateName.Length;
        var contractLength = BinaryPrimitives.ReadInt32LittleEndian(modified.AsSpan(contractLengthOffset, sizeof(int)));
        var exportFlagOffset = contractLengthOffset + sizeof(int) + contractLength;
        Assert.That(modified[exportFlagOffset], Is.EqualTo(0));
        modified[exportFlagOffset] = 1;
        _ = Assert.Throws<ModuleFormatException>(() => ModuleCompiler.Link(consumer, modified))!;
    }

    [Test]
    public void SameDeclaringTypeDoesNotGrantConsumerAccessToProducerPrivateBody()
    {
        const string Source = "[Blittable] [Layout(LayoutKind.Sequential)] public struct Secret { private int32 Value; "
            + "private Secret() { this.Value = 7; } "
            + "public static int32 Get() { Secret s = new Secret(); return s.Value; } "
            + "public static int32 Consumer() { Secret s = new Secret(); return s.Value; } } "
            + "int32 Main() { return Secret.Consumer(); }";
        var program = Compile(Source);
        var producer = ModuleCompiler.CreateProducer(program, ["Secret.Get"]);
        Assert.That(global::System.Linq.Enumerable.Any(program.Functions,static function => function.Name == "Secret.#ctor" && function.IsPrivate),Is.True);
        Assert.That(Encoding.UTF8.GetString(producer), Does.Contain("Secret.#ctor"));
        Assert.That(Encoding.UTF8.GetString(producer), Does.Not.Contain("Secret.Consumer"));
        foreach (var function in program.Functions)
        {
            if (function.Name == "Secret.Consumer")
            {
                var local = ((global::System.Func<BoundLocal>)(() => { var __nunitActual64 = function.Body.Statements[0]; Assert.That(__nunitActual64, Is.TypeOf<BoundLocal>()); return (BoundLocal)__nunitActual64!; }))();
                var call = ((global::System.Func<BoundCall>)(() => { var __nunitActual65 = local.Initializer; Assert.That(__nunitActual65, Is.TypeOf<BoundCall>()); return (BoundCall)__nunitActual65!; }))();
                Assert.That(program.Functions[call.Function].Name, Is.EqualTo("Secret.#ctor"));
            }
        }
        _ = Assert.Throws<ModuleContractException>(() => ModuleCompiler.CreateConsumer(program, producer))!;
    }

    [Test]
    public void ForgedConsumerCallCannotReachProducerPrivateSymbol()
    {
        const string Source = "[Blittable] [Layout(LayoutKind.Sequential)] public struct S { public int32 Field; "
            + "private static int32 Hide() { return 7; } "
            + "public static int32 Show() { return Hide(); } } "
            + "int32 Main() { return S.Show(); }";
        var program = Compile(Source);
        var producer = ModuleCompiler.CreateProducer(program, ["S.Show"]);
        var consumer = ModuleCompiler.CreateConsumer(program, producer);
        var forged = (byte[])consumer.Clone();
        var publicCall = Encoding.UTF8.GetBytes("S.Show");
        var privateCall = Encoding.UTF8.GetBytes("S.Hide");
        var callOffset = forged.AsSpan().LastIndexOf(publicCall);
        Assert.That(callOffset >= 0, Is.True);
        privateCall.AsSpan().CopyTo(forged.AsSpan(callOffset, privateCall.Length));
        _ = Assert.Throws<ModuleContractException>(() => ModuleCompiler.Link(forged, producer))!;
    }
    [TestCase("public int32 B; public int32 A;")]
    [TestCase("public int32 A; private int32 B;")]
    [TestCase("public int32 A; internal int32 B;")]
    public void RejectsChangedFieldOrderOrAccessBeforeExecutingConsumer(string fields)
    {
        var initial = Compile("[Blittable] [Layout(LayoutKind.Sequential)] public struct Pair { public int32 A; public int32 B; "
            + "public Pair() { this.A = 11; this.B = 1; } } "
            + "int32 Main() { return Value(); } public int32 Value() { Pair p = new Pair(); return p.A; }");
        var original = ModuleCompiler.CreateProducer(initial, ["Value", "Pair.#ctor"]);
        var consumer = ModuleCompiler.CreateConsumer(initial, original);
        var changed = Compile("[Blittable] [Layout(LayoutKind.Sequential)] public struct Pair { " + fields + " "
            + "public Pair() { this.A = 11; this.B = 1; } } "
            + "int32 Main() { return 0; } public int32 Value() { Pair p = new Pair(); return p.A; }");
        _ = Assert.Throws<ModuleContractException>(() =>
            ModuleCompiler.Link(consumer, ModuleCompiler.CreateProducer(changed, ["Value", "Pair.#ctor"])))!;
    }

    [Test]
    public async Task RemapsReorderedTypeIdentifiersWithoutRebindingConsumerAsync()
    {
        const string A = "[Blittable] [Layout(LayoutKind.Sequential)] public struct A { public int32 Value; public A() { this.Value = 5; } }";
        const string B = "struct B { public int32 Number; public B() { this.Number = 2; } }";
        const string Functions = "int32 Main() { return Helper(); } "
            + "public int32 Helper() { A value = new A(); return value.Value; }";
        var original = Compile(A + B + Functions);
        var consumer = ModuleCompiler.CreateConsumer(original, ModuleCompiler.CreateProducer(original, ["Helper", "A.#ctor"]));
        var reordered = Compile(B + A + Functions);
        var replacement = ModuleCompiler.CreateProducer(reordered, ["Helper", "A.#ctor"]);
        var linked = ModuleCompiler.Link(consumer, replacement);
        Assert.That(await ExecuteAsync(NativeEmitter.Emit(linked, NativeRuntimePolicy.FailFast)).ConfigureAwait(true), Is.EqualTo(5));
    }

    [Test]
    public void RejectsTamperedVersionBoundsAndUnknownNodes()
    {
        var initial = Compile("int32 Main() { return Helper(); } public int32 Helper() { return 11; }");
        var producer = ModuleCompiler.CreateProducer(initial, ["Helper"]);
        var consumer = ModuleCompiler.CreateConsumer(initial, producer);
        var version = (byte[])consumer.Clone();
        for (var unsupported = 0; unsupported < 10; unsupported++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(version.AsSpan(4, 4), unsupported);
            _ = Assert.Throws<ModuleFormatException>(() => ModuleCompiler.Link(version, producer))!;
        }
        BinaryPrimitives.WriteInt32LittleEndian(version.AsSpan(4, 4), int.MaxValue);
        _ = Assert.Throws<ModuleFormatException>(() => ModuleCompiler.Link(version, producer))!;
        _ = Assert.Throws<ModuleFormatException>(() => ModuleCompiler.Link(consumer[..^1], producer))!;
        _ = Assert.Throws<ModuleFormatException>(() => ModuleCompiler.Link(new byte[(4 * 1024 * 1024) + 1], producer))!;
        _ = Assert.Throws<ModuleFormatException>(() => ModuleCompiler.Link([.. consumer, 0], producer))!;
    }

    [Test]
    public void RejectsCompositeContractsInRetiredSemanticRevision()
    {
        var program = Compile("""
            [Blittable] [Layout(LayoutKind.Sequential)]
            public unique struct Child {
                public int32 Value;
                public Child() { Value = 42; }
            }
            [Blittable] [Layout(LayoutKind.Sequential)]
            public unique struct Owner {
                public Child Item;
                public Owner() { Item = new Child(); }
            }
            public Owner Make() { return new Owner(); }
            int32 Main() { Owner item = Make(); return 42; }
            """);
        var producer = ModuleCompiler.CreateProducer(program, ["Make"]);
        var consumer = ModuleCompiler.CreateConsumer(program, producer);
        var downgradedProducer = (byte[])producer.Clone();
        BinaryPrimitives.WriteInt32LittleEndian(downgradedProducer.AsSpan(4, 4), 13);
        _ = Assert.Throws<ModuleFormatException>(() => ModuleCompiler.CreateReference(downgradedProducer))!;
        BinaryPrimitives.WriteInt32LittleEndian(consumer.AsSpan(4, 4), 13);
        _ = Assert.Throws<ModuleFormatException>(() => ModuleCompiler.Link(consumer, producer))!;
    }

    private static BoundProgram Compile(string source)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        return ((global::System.Func<BoundProgram>)(() => { var __nunitActual66 = compilation.Program; Assert.That(__nunitActual66, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual66!; }))();
    }

    private static async Task<int> ExecuteAsync(byte[] image)
    {
        Assert.That(OperatingSystem.IsWindows() && RuntimeInformation.ProcessArchitecture == Architecture.X64, Is.True);
        var path = Path.Combine(AppContext.BaseDirectory, $"module-{Guid.NewGuid():N}.exe");
        try
        {
            await File.WriteAllBytesAsync(path, image).ConfigureAwait(true);
            using var process = Process.Start(new ProcessStartInfo(path) { UseShellExecute = false, CreateNoWindow = true })
                ?? throw new InvalidOperationException("Failed to start linked native program.");
            try
            {
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(true);
            }
            catch (TimeoutException)
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync().ConfigureAwait(true);
                }
                throw;
            }
            return process.ExitCode;
        }
        finally
        {
            await TestFileCleanup.DeleteWithRetryAsync(path).ConfigureAwait(true);
        }
    }

    private static async Task<(int ExitCode, string Error)> RunJitWorkerAsync(string consumer, string producer,
        bool explicitPolicy = true)
    {
        return explicitPolicy
            ? await RunCliAsync("run-module", consumer, producer, "--runtime", "fail-fast").ConfigureAwait(true)
            : await RunCliAsync("run-module", consumer, producer).ConfigureAwait(true);
    }

    private static async Task<(int ExitCode, string Error)> RunCliAsync(params string[] arguments)
    {
        var (exitCode, error, _) = await RunCliWithOutputAsync(arguments).ConfigureAwait(true);
        return (exitCode, error);
    }

    private static async Task<(int ExitCode, string Error, string Output)> RunCliWithOutputAsync(params string[] arguments)
    {
        var framework = new DirectoryInfo(AppContext.BaseDirectory);
        var configuration = framework.Parent?.Name ?? throw new InvalidOperationException("Missing configuration directory.");
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
        var cli = Path.Combine(root, "artifacts", "bin", "sources", "terrumc", configuration, framework.Name, "terrumc.dll");
        Assert.That(File.Exists(cli), Is.True, $"CLI was not built: {cli}");
        var start = new ProcessStartInfo("dotnet") {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        };
        start.ArgumentList.Add(cli);
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Failed to start module JIT worker.");
        var stderr = process.StandardError.ReadToEndAsync();
        var stdout = process.StandardOutput.ReadToEndAsync();
        try
        {
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(true);
        }
        catch (TimeoutException)
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync().ConfigureAwait(true);
            }
            throw;
        }
        return (process.ExitCode, await stderr.ConfigureAwait(true), await stdout.ConfigureAwait(true));
    }
}
