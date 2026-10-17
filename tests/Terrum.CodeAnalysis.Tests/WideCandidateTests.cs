// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests;

public sealed class WideCandidateTests
{
    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public async Task WideCompilerCandidatesExecuteFromIndependentModulesAsync(NativeRuntimePolicy policy)
    {
        var producer = CreateCandidateLibrary((BoundRuntimePolicy)policy);
        var reference = ModuleCompiler.CreateReference(producer);
        var root = RepositoryRoot();
        var tests = ReadTree(SelfHostPaths.ResolveFile(root, "WideIntegerTests.terrum"));
        var entry = ReadTree(Path.Combine(root, "samples", "native", "modules", "wide-entry.terrum"));
        var compilation = Compilation.Create([tests, entry], reference, (BoundRuntimePolicy)policy);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(((global::System.Func<BoundProgram>)(() => { var __nunitActual0 = compilation.Program; Assert.That(__nunitActual0, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual0!; }))(), producer);
        Assert.That(NativeJit.Execute(ModuleCompiler.Link(consumer, producer), policy), Is.EqualTo(0));
        await ExecuteBothAsync(consumer, producer, 0, policy).ConfigureAwait(true);
    }
    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public async Task RadixLengthsPublishOnlyForValidBasesAcrossModulesAsync(NativeRuntimePolicy policy)
    {
        var producer = CreateIntegerFormattingLibrary(policy,
            "Terrum.CodeAnalysis.Numerics64.TryGetUInt64BaseLength",
            "Terrum.CodeAnalysis.Numerics64.TryGetInt64BaseLength",
            "Terrum.CodeAnalysis.Numerics64.TryGetUInt32BaseLength",
            "Terrum.CodeAnalysis.Numerics64.TryGetInt32BaseLength");
        var reference = ModuleCompiler.CreateReference(producer);
        var consumerSource = SyntaxTree.Parse("""
            using Terrum.CodeAnalysis.Numerics64;
            int32 Main() {
                if (TryGetUInt64BaseLength(0, 1, init var low)
                    || TryGetInt64BaseLength(-1, 37, init var high)
                    || TryGetUInt32BaseLength(0, 0, init var low32)
                    || TryGetInt32BaseLength(0, 37, init var high32)) { return 1; }
                require TryGetUInt64BaseLength(uint64.MaxValue, 2, init var unsigned64);
                require TryGetInt64BaseLength(int64.MinValue, 2, init var signed64);
                require TryGetUInt32BaseLength(uint32.MaxValue, 2, init var unsigned32);
                require TryGetInt32BaseLength(int32.MinValue, 2, init var signed32);
                require TryGetInt64BaseLength(0, 36, init var zero);
                if (unsigned64 != 64 || signed64 != 65 || unsigned32 != 32
                    || signed32 != 33 || zero != 1) { return 2; }
                return 42;
            }
            """);
        var compilation = Compilation.Create(consumerSource, reference, (BoundRuntimePolicy)policy);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(((global::System.Func<BoundProgram>)(() => { var __nunitActual1 = compilation.Program; Assert.That(__nunitActual1, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual1!; }))(), producer);
        Assert.That(NativeJit.Execute(ModuleCompiler.Link(consumer, producer), policy), Is.EqualTo(42));
        await ExecuteBothAsync(consumer, producer, 42, policy).ConfigureAwait(true);
    }

    [Test]
    public void InvalidRadixLengthCannotBeReadAfterFailure()
    {
        var producer = CreateIntegerFormattingLibrary(NativeRuntimePolicy.FailFast,
            "Terrum.CodeAnalysis.Numerics64.TryGetUInt64BaseLength");
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            using Terrum.CodeAnalysis.Numerics64;
            int32 Main() {
                bool valid = TryGetUInt64BaseLength(0, 1, init var length);
                return length;
            }
            """), ModuleCompiler.CreateReference(producer));
        Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,static diagnostic => diagnostic.Code == "TRM3016"),Is.True);
        Assert.That(compilation.Program, Is.Null);
    }
    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public async Task BitOperationsMatchHostedReferenceAcrossModulesAsync(NativeRuntimePolicy policy)
    {
        var producer = CreateBitOperationsLibrary(policy);
        var compilation = Compilation.Create(SyntaxTree.Parse(BuildBitOperationsConsumer()),
            ModuleCompiler.CreateReference(producer), (BoundRuntimePolicy)policy);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(((global::System.Func<BoundProgram>)(() => { var __nunitActual2 = compilation.Program; Assert.That(__nunitActual2, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual2!; }))(), producer);
        Assert.That(NativeJit.Execute(ModuleCompiler.Link(consumer, producer), policy), Is.EqualTo(42));
        await ExecuteBothAsync(consumer, producer, 42, policy).ConfigureAwait(true);
    }
    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public Task BitOperationsUInt32MatchEveryBitPositionAcrossModulesAsync(NativeRuntimePolicy policy) =>
        ExecuteBitOperationsPositionConsumerAsync(policy, includeUInt64: false);
    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public Task BitOperationsUInt64MatchEveryBitPositionAcrossModulesAsync(NativeRuntimePolicy policy) =>
        ExecuteBitOperationsPositionConsumerAsync(policy, includeUInt64: true);
    [TestCase(BoundRuntimePolicy.FailFast,"UInt32","uint32.MaxValue")]
    [TestCase(BoundRuntimePolicy.CheckedExceptions,"UInt32","uint32.MaxValue")]
    [TestCase(BoundRuntimePolicy.FailFast,"UInt64","uint64.MaxValue")]
    [TestCase(BoundRuntimePolicy.CheckedExceptions,"UInt64","uint64.MaxValue")]
    public void BitOperationsRoundUpCannotReadFailureOutput(
        BoundRuntimePolicy policy, string suffix, string input)
    {
        var producer = CreateNumericsLibrary("BitOperations.terrum", (NativeRuntimePolicy)policy,
            $"Terrum.CodeAnalysis.Numerics64.TryRoundUpToPowerOfTwo{suffix}");
        var invalidConsumer = SyntaxTree.Parse($$"""
            using Terrum.CodeAnalysis.Numerics64;
            int32 Main() {
                if (!TryRoundUpToPowerOfTwo{{suffix}}({{input}}, init var rounded)) {
                    return int32(rounded);
                }
                return 0;
            }
            """);
        var compilation = Compilation.Create(invalidConsumer, ModuleCompiler.CreateReference(producer), policy);
        Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,static diagnostic => diagnostic.Code == "TRM3016"),Is.True);
        Assert.That(compilation.Program, Is.Null);
    }
    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public async Task Float32MathSelectionMatchesCandidateContractAcrossModulesAsync(NativeRuntimePolicy policy)
    {
        var producer = CreateNumericsLibrary("Float32MathSelection.terrum", policy,
            "Terrum.CodeAnalysis.Numerics64.AbsFloat32",
            "Terrum.CodeAnalysis.Numerics64.MinFloat32",
            "Terrum.CodeAnalysis.Numerics64.MaxFloat32",
            "Terrum.CodeAnalysis.Numerics64.ClampFloat32");
        var compilation = Compilation.Create(SyntaxTree.Parse(BuildFloat32MathSelectionConsumer()),
            ModuleCompiler.CreateReference(producer), (BoundRuntimePolicy)policy);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(((global::System.Func<BoundProgram>)(() => { var __nunitActual3 = compilation.Program; Assert.That(__nunitActual3, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual3!; }))(), producer);
        Assert.That(NativeJit.Execute(ModuleCompiler.Link(consumer, producer), policy), Is.EqualTo(42));
        await ExecuteBothAsync(consumer, producer, 42, policy).ConfigureAwait(true);
    }
    [TestCase(NativeRuntimePolicy.FailFast,"ClampFloat32(0.0, 2.0, 1.0)")]
    [TestCase(NativeRuntimePolicy.CheckedExceptions,"ClampFloat32(0.0, 2.0, 1.0)")]
    [TestCase(NativeRuntimePolicy.FailFast,"ClampFloat32(0.0, notANumber, 1.0)")]
    [TestCase(NativeRuntimePolicy.CheckedExceptions,"ClampFloat32(0.0, notANumber, 1.0)")]
    [TestCase(NativeRuntimePolicy.FailFast,"ClampFloat32(0.0, -1.0, notANumber)")]
    [TestCase(NativeRuntimePolicy.CheckedExceptions,"ClampFloat32(0.0, -1.0, notANumber)")]
    [TestCase(NativeRuntimePolicy.FailFast,"ClampFloat32(notANumber, 1.0, -1.0)")]
    [TestCase(NativeRuntimePolicy.CheckedExceptions,"ClampFloat32(notANumber, 1.0, -1.0)")]
    public async Task Float32ClampRejectsInvalidBoundsBeforeSelectingNaNAsync(
        NativeRuntimePolicy policy, string expression)
    {
        var producer = CreateNumericsLibrary("Float32MathSelection.terrum", policy,
            "Terrum.CodeAnalysis.Numerics64.ClampFloat32");
        var consumerSource = SyntaxTree.Parse($$"""
            using Terrum.CodeAnalysis.Numerics64;
            int32 Main() {
                float32 notANumber = 0.0 / 0.0;
                return int32({{expression}});
            }
            """);
        var compilation = Compilation.Create(consumerSource, ModuleCompiler.CreateReference(producer),
            (BoundRuntimePolicy)policy);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(((global::System.Func<BoundProgram>)(() => { var __nunitActual4 = compilation.Program; Assert.That(__nunitActual4, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual4!; }))(), producer);
        var program = ModuleCompiler.Link(consumer, producer);
        var image = Path.Combine(TestArtifactPaths.DirectoryPath, $"float32-math-failure-{Guid.NewGuid():N}.exe");
        try
        {
            await File.WriteAllBytesAsync(image, NativeEmitter.Emit(program, policy)).ConfigureAwait(true);
            await RunAsync(image, [], unchecked((int)0xE0540003)).ConfigureAwait(true);
        }
        finally
        {
            await TestFileCleanup.DeleteWithRetryAsync(image).ConfigureAwait(true);
        }
    }
    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public async Task Float64MathSelectionMatchesCandidateContractAcrossModulesAsync(NativeRuntimePolicy policy)
    {
        var producer = CreateNumericsLibrary("Float64MathSelection.terrum", policy,
            "Terrum.CodeAnalysis.Numerics64.AbsFloat64",
            "Terrum.CodeAnalysis.Numerics64.MinFloat64",
            "Terrum.CodeAnalysis.Numerics64.MaxFloat64",
            "Terrum.CodeAnalysis.Numerics64.ClampFloat64");
        var compilation = Compilation.Create(SyntaxTree.Parse(BuildFloat64MathSelectionConsumer()),
            ModuleCompiler.CreateReference(producer), (BoundRuntimePolicy)policy);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(((global::System.Func<BoundProgram>)(() => { var __nunitActual5 = compilation.Program; Assert.That(__nunitActual5, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual5!; }))(), producer);
        Assert.That(NativeJit.Execute(ModuleCompiler.Link(consumer, producer), policy), Is.EqualTo(42));
        await ExecuteBothAsync(consumer, producer, 42, policy).ConfigureAwait(true);
    }
    [TestCase(NativeRuntimePolicy.FailFast,"ClampFloat64(0.0, 2.0, 1.0)")]
    [TestCase(NativeRuntimePolicy.CheckedExceptions,"ClampFloat64(0.0, 2.0, 1.0)")]
    [TestCase(NativeRuntimePolicy.FailFast,"ClampFloat64(0.0, notANumber, 1.0)")]
    [TestCase(NativeRuntimePolicy.CheckedExceptions,"ClampFloat64(0.0, notANumber, 1.0)")]
    [TestCase(NativeRuntimePolicy.FailFast,"ClampFloat64(0.0, -1.0, notANumber)")]
    [TestCase(NativeRuntimePolicy.CheckedExceptions,"ClampFloat64(0.0, -1.0, notANumber)")]
    [TestCase(NativeRuntimePolicy.FailFast,"ClampFloat64(notANumber, 1.0, -1.0)")]
    [TestCase(NativeRuntimePolicy.CheckedExceptions,"ClampFloat64(notANumber, 1.0, -1.0)")]
    public async Task Float64ClampRejectsInvalidBoundsBeforeSelectingNaNAsync(
        NativeRuntimePolicy policy, string expression)
    {
        var producer = CreateNumericsLibrary("Float64MathSelection.terrum", policy,
            "Terrum.CodeAnalysis.Numerics64.ClampFloat64");
        var consumerSource = SyntaxTree.Parse($$"""
            using Terrum.CodeAnalysis.Numerics64;
            int32 Main() {
                float64 notANumber = 0.0 / 0.0;
                return int32({{expression}});
            }
            """);
        var compilation = Compilation.Create(consumerSource, ModuleCompiler.CreateReference(producer),
            (BoundRuntimePolicy)policy);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(((global::System.Func<BoundProgram>)(() => { var __nunitActual6 = compilation.Program; Assert.That(__nunitActual6, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual6!; }))(), producer);
        var program = ModuleCompiler.Link(consumer, producer);
        var image = Path.Combine(TestArtifactPaths.DirectoryPath, $"float64-math-failure-{Guid.NewGuid():N}.exe");
        try
        {
            await File.WriteAllBytesAsync(image, NativeEmitter.Emit(program, policy)).ConfigureAwait(true);
            await RunAsync(image, [], unchecked((int)0xE0540003)).ConfigureAwait(true);
        }
        finally
        {
            await TestFileCleanup.DeleteWithRetryAsync(image).ConfigureAwait(true);
        }
    }
    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public async Task IntegerMathCandidatesMatchHostedReferenceAcrossModulesAsync(NativeRuntimePolicy policy)
    {
        var producer = CreateNumericsLibrary("IntegerMath.terrum", policy,
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
            "Terrum.CodeAnalysis.Numerics64.GreatestCommonDivisorInt64");
        var compilation = Compilation.Create(SyntaxTree.Parse(BuildIntegerMathConsumer()),
            ModuleCompiler.CreateReference(producer), (BoundRuntimePolicy)policy);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(((global::System.Func<BoundProgram>)(() => { var __nunitActual7 = compilation.Program; Assert.That(__nunitActual7, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual7!; }))(), producer);
        Assert.That(NativeJit.Execute(ModuleCompiler.Link(consumer, producer), policy), Is.EqualTo(42));
        await ExecuteBothAsync(consumer, producer, 42, policy).ConfigureAwait(true);
    }
    [TestCase(NativeRuntimePolicy.FailFast,"AbsInt32(int32.MinValue)",unchecked((int)0xE0540001))]
    [TestCase(NativeRuntimePolicy.CheckedExceptions,"AbsInt32(int32.MinValue)",unchecked((int)0xE0540001))]
    [TestCase(NativeRuntimePolicy.FailFast,"int32(AbsInt64(int64.MinValue))",unchecked((int)0xE0540001))]
    [TestCase(NativeRuntimePolicy.CheckedExceptions,"int32(AbsInt64(int64.MinValue))",unchecked((int)0xE0540001))]
    [TestCase(NativeRuntimePolicy.FailFast,"ClampInt32(0, 2, 1)",unchecked((int)0xE0540003))]
    [TestCase(NativeRuntimePolicy.CheckedExceptions,"ClampInt32(0, 2, 1)",unchecked((int)0xE0540003))]
    [TestCase(NativeRuntimePolicy.FailFast,"int32(ClampInt64(0, 2, 1))",unchecked((int)0xE0540003))]
    [TestCase(NativeRuntimePolicy.CheckedExceptions,"int32(ClampInt64(0, 2, 1))",unchecked((int)0xE0540003))]
    public async Task IntegerMathCandidatesPreserveCheckedFailureSemanticsAsync(
        NativeRuntimePolicy policy, string expression, int expected)
    {
        var component = File.ReadAllText(SelfHostPaths.ResolveFile(RepositoryRoot(), "IntegerMath.terrum"));
        var compilation = Compilation.Create(SyntaxTree.Parse(
            component + $"\nint32 Main() {{ return {expression}; }}"), (BoundRuntimePolicy)policy);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual8 = compilation.Program; Assert.That(__nunitActual8, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual8!; }))();
        var image = Path.Combine(TestArtifactPaths.DirectoryPath, $"integer-math-failure-{Guid.NewGuid():N}.exe");
        try
        {
            await File.WriteAllBytesAsync(image, NativeEmitter.Emit(program, policy)).ConfigureAwait(true);
            await RunAsync(image, [], expected).ConfigureAwait(true);
        }
        finally
        {
            await TestFileCleanup.DeleteWithRetryAsync(image).ConfigureAwait(true);
        }
    }
    [TestCase(NativeRuntimePolicy.FailFast,"Int32")]
    [TestCase(NativeRuntimePolicy.FailFast,"Int64")]
    [TestCase(NativeRuntimePolicy.CheckedExceptions,"Int32")]
    [TestCase(NativeRuntimePolicy.CheckedExceptions,"Int64")]
    public async Task CheckedSignedMagnitudePublishesOnlyRepresentableResultsAcrossModulesAsync(
        NativeRuntimePolicy policy, string suffix)
    {
        var library = suffix == "Int32" ? "IntegerCheckedArithmetic.terrum" : "WideIntegerFacts.terrum";
        var producer = CreateNumericsLibrary(library, policy,
            $"Terrum.CodeAnalysis.Numerics64.TryNegate{suffix}",
            $"Terrum.CodeAnalysis.Numerics64.TryAbsolute{suffix}");
        var type = suffix.ToLowerInvariant();
        var consumerSource = SyntaxTree.Parse($$"""
            using Terrum.CodeAnalysis.Numerics64;
            int32 Main() {
                if (TryNegate{{suffix}}({{type}}.MinValue, init var negationOverflow)
                    || TryAbsolute{{suffix}}({{type}}.MinValue, init var absoluteOverflow)) { return 1; }
                require TryNegate{{suffix}}({{type}}.MaxValue, init var negativeMaximum);
                require TryNegate{{suffix}}(-1, init var positiveOne);
                require TryNegate{{suffix}}(0, init var zero);
                require TryAbsolute{{suffix}}(-42, init var positive);
                require TryAbsolute{{suffix}}(42, init var unchanged);
                require TryAbsolute{{suffix}}(0, init var absoluteZero);
                if (negativeMaximum != -{{type}}.MaxValue || positiveOne != 1
                    || zero != 0 || positive != 42 || unchanged != 42
                    || absoluteZero != 0) { return 2; }
                return 42;
            }
            """);
        var compilation = Compilation.Create(consumerSource, ModuleCompiler.CreateReference(producer),
            (BoundRuntimePolicy)policy);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(((global::System.Func<BoundProgram>)(() => { var __nunitActual9 = compilation.Program; Assert.That(__nunitActual9, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual9!; }))(), producer);
        Assert.That(NativeJit.Execute(ModuleCompiler.Link(consumer, producer), policy), Is.EqualTo(42));
        await ExecuteBothAsync(consumer, producer, 42, policy).ConfigureAwait(true);
    }
    [TestCase("Int32")]
    [TestCase("Int64")]
    public void FailedSignedMagnitudeCannotPublishVacantResult(string suffix)
    {
        var library = suffix == "Int32" ? "IntegerCheckedArithmetic.terrum" : "WideIntegerFacts.terrum";
        var producer = CreateNumericsLibrary(library, NativeRuntimePolicy.FailFast,
            $"Terrum.CodeAnalysis.Numerics64.TryAbsolute{suffix}");
        var type = suffix.ToLowerInvariant();
        var compilation = Compilation.Create(SyntaxTree.Parse($$"""
            using Terrum.CodeAnalysis.Numerics64;
            int32 Main() {
                bool valid = TryAbsolute{{suffix}}({{type}}.MinValue, init var result);
                return int32(result);
            }
            """), ModuleCompiler.CreateReference(producer));
        Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,static diagnostic => diagnostic.Code == "TRM3016"),Is.True);
        Assert.That(compilation.Program, Is.Null);
    }
    [TestCase(NativeRuntimePolicy.FailFast,"Int32")]
    [TestCase(NativeRuntimePolicy.FailFast,"Int64")]
    [TestCase(NativeRuntimePolicy.CheckedExceptions,"Int32")]
    [TestCase(NativeRuntimePolicy.CheckedExceptions,"Int64")]
    public async Task SignedMagnitudeIncludesMinimumAcrossModulesAsync(NativeRuntimePolicy policy, string suffix)
    {
        var library = suffix == "Int32" ? "IntegerCheckedArithmetic.terrum" : "WideIntegerFacts.terrum";
        var producer = CreateNumericsLibrary(library, policy,
            $"Terrum.CodeAnalysis.Numerics64.Magnitude{suffix}");
        var type = suffix.ToLowerInvariant();
        var unsignedType = "u" + type;
        var consumerSource = SyntaxTree.Parse($$"""
            using Terrum.CodeAnalysis.Numerics64;
            int32 Main() {
                if (Magnitude{{suffix}}({{type}}.MinValue)
                        != {{unsignedType}}({{type}}.MaxValue) + 1
                    || Magnitude{{suffix}}({{type}}.MaxValue)
                        != {{unsignedType}}({{type}}.MaxValue)
                    || Magnitude{{suffix}}(-42) != 42
                    || Magnitude{{suffix}}(42) != 42
                    || Magnitude{{suffix}}(0) != 0
                    || Magnitude{{suffix}}(-1) != 1) { return 1; }
                return 42;
            }
            """);
        var compilation = Compilation.Create(consumerSource, ModuleCompiler.CreateReference(producer),
            (BoundRuntimePolicy)policy);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(((global::System.Func<BoundProgram>)(() => { var __nunitActual10 = compilation.Program; Assert.That(__nunitActual10, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual10!; }))(), producer);
        Assert.That(NativeJit.Execute(ModuleCompiler.Link(consumer, producer), policy), Is.EqualTo(42));
        await ExecuteBothAsync(consumer, producer, 42, policy).ConfigureAwait(true);
    }
    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public async Task CheckedInt32ArithmeticPublishesOnlyInRangeAcrossModulesAsync(NativeRuntimePolicy policy)
    {
        var producer = CreateNumericsLibrary("IntegerCheckedArithmetic.terrum", policy,
            "Terrum.CodeAnalysis.Numerics64.TryAddInt32",
            "Terrum.CodeAnalysis.Numerics64.TrySubtractInt32",
            "Terrum.CodeAnalysis.Numerics64.TryMultiplyInt32");
        var consumerSource = SyntaxTree.Parse("""
            using Terrum.CodeAnalysis.Numerics64;
            int32 Main() {
                if (TryAddInt32(int32.MaxValue, 1, init var addOverflow)
                    || TryAddInt32(int32.MinValue, -1, init var addUnderflow)
                    || TrySubtractInt32(int32.MaxValue, -1, init var subOverflow)
                    || TrySubtractInt32(int32.MinValue, 1, init var subUnderflow)
                    || TryMultiplyInt32(int32.MaxValue, 2, init var mulOverflow)
                    || TryMultiplyInt32(int32.MinValue, -1, init var mulNegOverflow)
                    || TryMultiplyInt32(int32.MinValue, 2, init var mulUnderflow)) { return 1; }
                require TryAddInt32(int32.MinValue, int32.MaxValue, init var negativeOne);
                require TryAddInt32(40, 2, init var fortyTwo);
                require TrySubtractInt32(-1, int32.MinValue, init var maximum);
                require TrySubtractInt32(int32.MinValue, int32.MinValue, init var zero);
                require TryMultiplyInt32(int32.MinValue, 1, init var minimum);
                require TryMultiplyInt32(-1, int32.MaxValue, init var negativeMaximum);
                require TryMultiplyInt32(46340, 46340, init var square);
                if (negativeOne != -1 || fortyTwo != 42 || maximum != int32.MaxValue
                    || zero != 0 || minimum != int32.MinValue
                    || negativeMaximum != -int32.MaxValue || square != 2147395600) { return 2; }
                return 42;
            }
            """);
        var compilation = Compilation.Create(consumerSource, ModuleCompiler.CreateReference(producer),
            (BoundRuntimePolicy)policy);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(((global::System.Func<BoundProgram>)(() => { var __nunitActual11 = compilation.Program; Assert.That(__nunitActual11, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual11!; }))(), producer);
        Assert.That(NativeJit.Execute(ModuleCompiler.Link(consumer, producer), policy), Is.EqualTo(42));
        await ExecuteBothAsync(consumer, producer, 42, policy).ConfigureAwait(true);
    }
    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public async Task CheckedUInt32ArithmeticPublishesOnlyInRangeAcrossModulesAsync(NativeRuntimePolicy policy)
    {
        var producer = CreateNumericsLibrary("IntegerCheckedArithmetic.terrum", policy,
            "Terrum.CodeAnalysis.Numerics64.TryAddUInt32",
            "Terrum.CodeAnalysis.Numerics64.TrySubtractUInt32",
            "Terrum.CodeAnalysis.Numerics64.TryMultiplyUInt32");
        var consumerSource = SyntaxTree.Parse("""
            using Terrum.CodeAnalysis.Numerics64;
            int32 Main() {
                if (TryAddUInt32(uint32.MaxValue, 1, init var addOverflow)
                    || TrySubtractUInt32(0, 1, init var subUnderflow)
                    || TryMultiplyUInt32(uint32.MaxValue, 2, init var mulOverflow)) { return 1; }
                require TryAddUInt32(uint32.MaxValue - 1, 1, init var maximum);
                require TryAddUInt32(0, 0, init var zero);
                require TrySubtractUInt32(uint32.MaxValue, uint32.MaxValue, init var difference);
                require TrySubtractUInt32(uint32.MaxValue, 1, init var almostMaximum);
                require TryMultiplyUInt32(65535, 65537, init var product);
                require TryMultiplyUInt32(uint32.MaxValue, 0, init var zeroProduct);
                if (maximum != uint32.MaxValue || zero != 0 || difference != 0
                    || almostMaximum != uint32.MaxValue - 1 || product != uint32.MaxValue
                    || zeroProduct != 0) { return 2; }
                return 42;
            }
            """);
        var compilation = Compilation.Create(consumerSource, ModuleCompiler.CreateReference(producer),
            (BoundRuntimePolicy)policy);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(((global::System.Func<BoundProgram>)(() => { var __nunitActual12 = compilation.Program; Assert.That(__nunitActual12, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual12!; }))(), producer);
        Assert.That(NativeJit.Execute(ModuleCompiler.Link(consumer, producer), policy), Is.EqualTo(42));
        await ExecuteBothAsync(consumer, producer, 42, policy).ConfigureAwait(true);
    }
    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public async Task CheckedWideArithmeticPublishesOnlyInRangeAcrossModulesAsync(NativeRuntimePolicy policy)
    {
        var producer = CreateNumericsLibrary("WideIntegerFacts.terrum", policy,
            "Terrum.CodeAnalysis.Numerics64.TryAddInt64",
            "Terrum.CodeAnalysis.Numerics64.TrySubtractInt64",
            "Terrum.CodeAnalysis.Numerics64.TryMultiplyInt64",
            "Terrum.CodeAnalysis.Numerics64.TryAddUInt64",
            "Terrum.CodeAnalysis.Numerics64.TrySubtractUInt64",
            "Terrum.CodeAnalysis.Numerics64.TryMultiplyUInt64");
        var consumerSource = SyntaxTree.Parse("""
            using Terrum.CodeAnalysis.Numerics64;
            int32 Main() {
                if (TryAddInt64(int64.MaxValue, 1, init var addOverflow)
                    || TryAddInt64(int64.MinValue, -1, init var addUnderflow)
                    || TrySubtractInt64(int64.MaxValue, -1, init var subOverflow)
                    || TrySubtractInt64(int64.MinValue, 1, init var subUnderflow)
                    || TryMultiplyInt64(int64.MinValue, -1, init var mulOverflow)
                    || TryMultiplyInt64(int64.MaxValue, 2, init var mulOverflow2)
                    || TryAddUInt64(uint64.MaxValue, 1, init var unsignedAddOverflow)
                    || TrySubtractUInt64(0, 1, init var unsignedSubUnderflow)
                    || TryMultiplyUInt64(uint64.MaxValue, 2, init var unsignedMulOverflow)) { return 1; }
                require TryAddInt64(int64.MinValue, int64.MaxValue, init var negativeOne);
                require TrySubtractInt64(-1, int64.MinValue, init var maximum);
                require TryMultiplyInt64(int64.MinValue, 1, init var minimum);
                require TryMultiplyInt64(3037000499, 3037000499, init var signedSquare);
                require TryAddUInt64(uint64.MaxValue - 1, 1, init var unsignedMaximum);
                require TrySubtractUInt64(uint64.MaxValue, uint64.MaxValue, init var unsignedZero);
                require TryMultiplyUInt64(uint64.MaxValue, 0, init var zeroProduct);
                require TryMultiplyUInt64(4294967295, 4294967297, init var unsignedProduct);
                if (negativeOne != -1 || maximum != int64.MaxValue
                    || minimum != int64.MinValue || signedSquare != 9223372030926249001
                    || unsignedMaximum != uint64.MaxValue || unsignedZero != 0
                    || zeroProduct != 0 || unsignedProduct != uint64.MaxValue) { return 2; }
                return 42;
            }
            """);
        var compilation = Compilation.Create(consumerSource, ModuleCompiler.CreateReference(producer),
            (BoundRuntimePolicy)policy);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(((global::System.Func<BoundProgram>)(() => { var __nunitActual13 = compilation.Program; Assert.That(__nunitActual13, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual13!; }))(), producer);
        Assert.That(NativeJit.Execute(ModuleCompiler.Link(consumer, producer), policy), Is.EqualTo(42));
        await ExecuteBothAsync(consumer, producer, 42, policy).ConfigureAwait(true);
    }
    [TestCase(NativeRuntimePolicy.FailFast,"Int32")]
    [TestCase(NativeRuntimePolicy.FailFast,"UInt32")]
    [TestCase(NativeRuntimePolicy.FailFast,"Int64")]
    [TestCase(NativeRuntimePolicy.FailFast,"UInt64")]
    [TestCase(NativeRuntimePolicy.CheckedExceptions,"Int32")]
    [TestCase(NativeRuntimePolicy.CheckedExceptions,"UInt32")]
    [TestCase(NativeRuntimePolicy.CheckedExceptions,"Int64")]
    [TestCase(NativeRuntimePolicy.CheckedExceptions,"UInt64")]
    public async Task CheckedDivisionAndRemainderPreserveBoundarySemanticsAcrossModulesAsync(
        NativeRuntimePolicy policy, string suffix)
    {
        string type = suffix.ToLowerInvariant();
        string library = suffix.EndsWith("32", StringComparison.Ordinal)
            ? "IntegerCheckedArithmetic.terrum" : "WideIntegerFacts.terrum";
        string signedCases = suffix.StartsWith("Int", StringComparison.Ordinal)
            ? $$"""
                if (TryDivide{{suffix}}({{type}}.MinValue, -1, init var overflow)) { return 3; }
                require TryRemainder{{suffix}}({{type}}.MinValue, -1, init var minRemainder);
                require TryDivide{{suffix}}(-7, 3, init var negativeQuotient);
                require TryRemainder{{suffix}}(-7, 3, init var negativeRemainder);
                if (minRemainder != 0 || negativeQuotient != -2 || negativeRemainder != -1) { return 4; }
                """
            : $$"""
                require TryDivide{{suffix}}({{type}}.MaxValue, 2, init var half);
                if (half != {{(suffix == "UInt32" ? "2147483647" : "9223372036854775807")}}) { return 4; }
                """;
        var producer = CreateNumericsLibrary(library, policy,
            $"Terrum.CodeAnalysis.Numerics64.TryDivide{suffix}",
            $"Terrum.CodeAnalysis.Numerics64.TryRemainder{suffix}");
        var consumerSource = SyntaxTree.Parse($$"""
            using Terrum.CodeAnalysis.Numerics64;
            int32 Main() {
                if (TryDivide{{suffix}}(42, 0, init var divideByZero)
                    || TryRemainder{{suffix}}(42, 0, init var remainderByZero)) { return 1; }
                require TryDivide{{suffix}}(84, 2, init var quotient);
                require TryRemainder{{suffix}}(85, 2, init var remainder);
                require TryDivide{{suffix}}({{type}}.MaxValue, {{type}}.MaxValue, init var one);
                require TryRemainder{{suffix}}({{type}}.MaxValue, {{type}}.MaxValue, init var zero);
                if (quotient != 42 || remainder != 1 || one != 1 || zero != 0) { return 2; }
                {{signedCases}}
                return 42;
            }
            """);
        var compilation = Compilation.Create(consumerSource, ModuleCompiler.CreateReference(producer),
            (BoundRuntimePolicy)policy);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(((global::System.Func<BoundProgram>)(() => { var __nunitActual14 = compilation.Program; Assert.That(__nunitActual14, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual14!; }))(), producer);
        Assert.That(NativeJit.Execute(ModuleCompiler.Link(consumer, producer), policy), Is.EqualTo(42));
        await ExecuteBothAsync(consumer, producer, 42, policy).ConfigureAwait(true);
    }
    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public async Task CheckedConversionsPublishOnlyRepresentableValuesAcrossModulesAsync(NativeRuntimePolicy policy)
    {
        var producer = CreateNumericsLibrary("IntegerCheckedConversions.terrum", policy,
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
            "Terrum.CodeAnalysis.Numerics64.TryConvertInt64ToUInt64");
        var consumerSource = SyntaxTree.Parse("""
            using Terrum.CodeAnalysis.Numerics64;
            int32 Main() {
                if (TryConvertInt32ToUInt64(-1, init var unsigned64FromNegative32)
                    || TryConvertInt32ToUInt32(-1, init var unsigned32FromNegative32)
                    || TryConvertUInt32ToInt32(uint32.MaxValue, init var int32FromLarge32)
                    || TryConvertInt64ToInt32(int64.MinValue, init var int32FromSmall64)
                    || TryConvertInt64ToInt32(int64.MaxValue, init var int32FromLarge64)
                    || TryConvertInt64ToUInt32(-1, init var unsigned32FromNegative64)
                    || TryConvertInt64ToUInt32(int64.MaxValue, init var unsigned32FromLarge64)
                    || TryConvertUInt64ToInt32(uint64.MaxValue, init var int32FromLargeUnsigned)
                    || TryConvertUInt64ToUInt32(uint64.MaxValue, init var unsigned32FromLargeUnsigned)
                    || TryConvertUInt64ToInt64(uint64.MaxValue, init var int64FromLargeUnsigned)
                    || TryConvertInt64ToUInt64(-1, init var unsigned64FromNegative64)) { return 1; }
                require TryConvertInt32ToInt64(int32.MinValue, init var minInt64);
                require TryConvertInt32ToUInt64(int32.MaxValue, init var unsigned64From32);
                require TryConvertUInt32ToInt64(uint32.MaxValue, init var int64FromUnsigned32);
                require TryConvertUInt32ToUInt64(uint32.MaxValue, init var unsigned64FromUInt32);
                require TryConvertUInt32ToInt32(uint32(int32.MaxValue), init var int32From32);
                require TryConvertInt32ToUInt32(int32.MaxValue, init var unsigned32From32);
                require TryConvertInt64ToInt32(int64(int32.MinValue), init var int32From64);
                require TryConvertInt64ToUInt32(int64(uint32.MaxValue), init unsigned32FromNegative64);
                require TryConvertUInt64ToInt32(uint64(int32.MaxValue), init var int32FromUnsigned64);
                require TryConvertUInt64ToUInt32(uint64(uint32.MaxValue), init var unsigned32FromUnsigned64);
                require TryConvertUInt64ToInt64(uint64(int64.MaxValue), init var int64FromUnsigned64);
                require TryConvertInt64ToUInt64(int64.MaxValue, init var unsigned64From64);
                if (minInt64 != int64(int32.MinValue)
                    || unsigned64From32 != uint64(int32.MaxValue)
                    || int64FromUnsigned32 != int64(uint32.MaxValue)
                    || unsigned64FromUInt32 != uint64(uint32.MaxValue)
                    || int32From32 != int32.MaxValue
                    || unsigned32From32 != uint32(int32.MaxValue)
                    || int32From64 != int32.MinValue
                    || unsigned32FromNegative64 != uint32.MaxValue
                    || int32FromUnsigned64 != int32.MaxValue
                    || unsigned32FromUnsigned64 != uint32.MaxValue
                    || int64FromUnsigned64 != int64.MaxValue
                    || unsigned64From64 != uint64(int64.MaxValue)) { return 2; }
                return 42;
            }
            """);
        var compilation = Compilation.Create(consumerSource, ModuleCompiler.CreateReference(producer),
            (BoundRuntimePolicy)policy);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(((global::System.Func<BoundProgram>)(() => { var __nunitActual15 = compilation.Program; Assert.That(__nunitActual15, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual15!; }))(), producer);
        Assert.That(NativeJit.Execute(ModuleCompiler.Link(consumer, producer), policy), Is.EqualTo(42));
        await ExecuteBothAsync(consumer, producer, 42, policy).ConfigureAwait(true);
    }
    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public async Task CheckedChar16ConversionsPreserveCodeUnitsAcrossModulesAsync(NativeRuntimePolicy policy)
    {
        var producer = CreateNumericsLibrary("IntegerCheckedConversions.terrum", policy,
            "Terrum.CodeAnalysis.Numerics64.TryConvertInt32ToChar16",
            "Terrum.CodeAnalysis.Numerics64.TryConvertUInt32ToChar16",
            "Terrum.CodeAnalysis.Numerics64.TryConvertInt64ToChar16",
            "Terrum.CodeAnalysis.Numerics64.TryConvertUInt64ToChar16",
            "Terrum.CodeAnalysis.Numerics64.TryConvertChar16ToInt32",
            "Terrum.CodeAnalysis.Numerics64.TryConvertChar16ToUInt32",
            "Terrum.CodeAnalysis.Numerics64.TryConvertChar16ToInt64",
            "Terrum.CodeAnalysis.Numerics64.TryConvertChar16ToUInt64");
        var source = SyntaxTree.Parse("""
            using Terrum.CodeAnalysis.Numerics64;
            int32 Main() {
                if (TryConvertInt32ToChar16(-1, init var negative32)
                    || TryConvertInt32ToChar16(65536, init var large32)
                    || TryConvertUInt32ToChar16(uint32.MaxValue, init var largeUnsigned32)
                    || TryConvertInt64ToChar16(int64.MinValue, init var negative64)
                    || TryConvertInt64ToChar16(int64.MaxValue, init var large64)
                    || TryConvertUInt64ToChar16(uint64.MaxValue, init var largeUnsigned64)) { return 1; }
                require TryConvertInt32ToChar16(0, init var zero);
                require TryConvertInt32ToChar16(65535, init var max32);
                require TryConvertUInt32ToChar16(55296, init var surrogate);
                require TryConvertInt64ToChar16(65535, init var max64);
                require TryConvertUInt64ToChar16(65535, init var maxUnsigned64);
                if (zero != char16(0) || max32 != char16(65535)
                    || surrogate != char16(55296) || max64 != char16(65535)
                    || maxUnsigned64 != char16(65535)) { return 2; }
                require TryConvertChar16ToInt32(zero, init var widenedZero);
                require TryConvertChar16ToInt32(surrogate, init var widenedSurrogate);
                require TryConvertChar16ToUInt32(max32, init var widenedUnsigned32);
                require TryConvertChar16ToInt64(surrogate, init var widenedSigned64);
                require TryConvertChar16ToUInt64(maxUnsigned64, init var widenedUnsigned64);
                if (widenedZero != 0 || widenedSurrogate != 55296
                    || widenedUnsigned32 != 65535 || widenedSigned64 != 55296
                    || widenedUnsigned64 != 65535) { return 3; }
                return 42;
            }
            """);
        var reference = ModuleCompiler.CreateReference(producer);
        var compilation = Compilation.Create(source, reference, (BoundRuntimePolicy)policy);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(((global::System.Func<BoundProgram>)(() => { var __nunitActual16 = compilation.Program; Assert.That(__nunitActual16, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual16!; }))(), producer);
        Assert.That(NativeJit.Execute(ModuleCompiler.Link(consumer, producer), policy), Is.EqualTo(42));
        await ExecuteBothAsync(consumer, producer, 42, policy).ConfigureAwait(true);
    }

    [Test]
    public void FailedCheckedChar16ConversionDoesNotPublishAnInitResult()
    {
        var producer = CreateNumericsLibrary("IntegerCheckedConversions.terrum", NativeRuntimePolicy.FailFast,
            "Terrum.CodeAnalysis.Numerics64.TryConvertInt32ToChar16");
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            using Terrum.CodeAnalysis.Numerics64;
            int32 Main() {
                bool valid = TryConvertInt32ToChar16(-1, init var result);
                return int32(result);
            }
            """), ModuleCompiler.CreateReference(producer));
        Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,static diagnostic => diagnostic.Code == "TRM3016"),Is.True);
        Assert.That(compilation.Program, Is.Null);
    }
    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public async Task SaturatingConversionsExecuteFromIndependentModuleAsync(NativeRuntimePolicy policy)
    {
        var producer = CreateNumericsLibrary("IntegerSaturation.terrum", policy,
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
            "Terrum.CodeAnalysis.Numerics64.SaturateInt64ToUInt64");
        var consumerSource = SyntaxTree.Parse("""
            using Terrum.CodeAnalysis.Numerics64;
            int32 Main() {
                if (SaturateInt32ToInt64(int32.MinValue) != int64(int32.MinValue)
                    || SaturateInt32ToUInt64(-1) != 0
                    || SaturateUInt32ToInt64(uint32.MaxValue) != int64(uint32.MaxValue)
                    || SaturateUInt32ToUInt64(uint32.MaxValue) != uint64(uint32.MaxValue)) { return 9; }
                if (SaturateUInt32ToInt32(uint32.MaxValue) != int32.MaxValue
                    || SaturateUInt32ToInt32(42) != 42) { return 7; }
                if (SaturateInt32ToUInt32(int32.MinValue) != 0
                    || SaturateInt32ToUInt32(42) != 42) { return 8; }
                if (SaturateInt64ToInt32(int64.MinValue) != int32.MinValue
                    || SaturateInt64ToInt32(int64.MaxValue) != int32.MaxValue
                    || SaturateInt64ToInt32(42) != 42) { return 1; }
                if (SaturateInt64ToUInt32(-1) != 0
                    || SaturateInt64ToUInt32(int64.MaxValue) != uint32.MaxValue) { return 2; }
                if (SaturateUInt64ToInt32(uint64.MaxValue) != int32.MaxValue
                    || SaturateUInt64ToInt32(42) != 42) { return 3; }
                if (SaturateUInt64ToUInt32(uint64.MaxValue) != uint32.MaxValue
                    || SaturateUInt64ToUInt32(42) != 42) { return 4; }
                if (SaturateUInt64ToInt64(uint64.MaxValue) != int64.MaxValue
                    || SaturateUInt64ToInt64(42) != 42) { return 5; }
                if (SaturateInt64ToUInt64(int64.MinValue) != 0
                    || SaturateInt64ToUInt64(int64.MaxValue) != uint64(int64.MaxValue)) { return 6; }
                return 42;
            }
            """);
        var compilation = Compilation.Create(consumerSource, ModuleCompiler.CreateReference(producer),
            (BoundRuntimePolicy)policy);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(((global::System.Func<BoundProgram>)(() => { var __nunitActual17 = compilation.Program; Assert.That(__nunitActual17, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual17!; }))(), producer);
        Assert.That(NativeJit.Execute(ModuleCompiler.Link(consumer, producer), policy), Is.EqualTo(42));
        await ExecuteBothAsync(consumer, producer, 42, policy).ConfigureAwait(true);
    }
    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public async Task SaturatingChar16ConversionsClampOnlyOutsideCodeUnitRangeAsync(NativeRuntimePolicy policy)
    {
        var producer = CreateNumericsLibrary("IntegerSaturation.terrum", policy,
            "Terrum.CodeAnalysis.Numerics64.SaturateInt32ToChar16",
            "Terrum.CodeAnalysis.Numerics64.SaturateUInt32ToChar16",
            "Terrum.CodeAnalysis.Numerics64.SaturateInt64ToChar16",
            "Terrum.CodeAnalysis.Numerics64.SaturateUInt64ToChar16",
            "Terrum.CodeAnalysis.Numerics64.SaturateChar16ToInt32",
            "Terrum.CodeAnalysis.Numerics64.SaturateChar16ToUInt32",
            "Terrum.CodeAnalysis.Numerics64.SaturateChar16ToInt64",
            "Terrum.CodeAnalysis.Numerics64.SaturateChar16ToUInt64");
        var source = SyntaxTree.Parse("""
            using Terrum.CodeAnalysis.Numerics64;
            int32 Main() {
                if (SaturateInt32ToChar16(int32.MinValue) != char16(0)
                    || SaturateInt32ToChar16(-1) != char16(0)
                    || SaturateInt32ToChar16(0) != char16(0)
                    || SaturateInt32ToChar16(55296) != char16(55296)
                    || SaturateInt32ToChar16(65535) != char16(65535)
                    || SaturateInt32ToChar16(65536) != char16(65535)
                    || SaturateUInt32ToChar16(55296) != char16(55296)
                    || SaturateUInt32ToChar16(uint32.MaxValue) != char16(65535)
                    || SaturateInt64ToChar16(int64.MinValue) != char16(0)
                    || SaturateInt64ToChar16(65535) != char16(65535)
                    || SaturateInt64ToChar16(int64.MaxValue) != char16(65535)
                    || SaturateUInt64ToChar16(0) != char16(0)
                    || SaturateUInt64ToChar16(uint64.MaxValue) != char16(65535)) { return 0; }
                if (SaturateChar16ToInt32(char16(0)) != 0
                    || SaturateChar16ToInt32(char16(55296)) != 55296
                    || SaturateChar16ToUInt32(char16(65535)) != 65535
                    || SaturateChar16ToInt64(char16(55296)) != 55296
                    || SaturateChar16ToUInt64(char16(65535)) != 65535) { return 1; }
                return 42;
            }
            """);
        var compilation = Compilation.Create(source, ModuleCompiler.CreateReference(producer),
            (BoundRuntimePolicy)policy);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(((global::System.Func<BoundProgram>)(() => { var __nunitActual18 = compilation.Program; Assert.That(__nunitActual18, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual18!; }))(), producer);
        Assert.That(NativeJit.Execute(ModuleCompiler.Link(consumer, producer), policy), Is.EqualTo(42));
        await ExecuteBothAsync(consumer, producer, 42, policy).ConfigureAwait(true);
    }
    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public async Task WrappingConversionsExecuteFromIndependentModuleAsync(NativeRuntimePolicy policy)
    {
        var producer = CreateNumericsLibrary("IntegerWrapping.terrum", policy,
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
            "Terrum.CodeAnalysis.Numerics64.WrapUInt64ToInt64");
        var consumerSource = SyntaxTree.Parse("""
            using Terrum.CodeAnalysis.Numerics64;
            int32 Main() {
                if (WrapInt32ToInt64(int32.MinValue) != int64(int32.MinValue)
                    || WrapInt32ToUInt64(-1) != uint64.MaxValue
                    || WrapUInt32ToInt64(uint32.MaxValue) != int64(uint32.MaxValue)
                    || WrapUInt32ToUInt64(uint32.MaxValue) != uint64(uint32.MaxValue)) { return 5; }
                if (WrapInt32ToUInt32(-1) != uint32.MaxValue
                    || WrapUInt32ToInt32(uint32.MaxValue) != -1) { return 1; }
                if (WrapInt64ToUInt32(-4294967297) != uint32.MaxValue
                    || WrapInt64ToInt32(2147483648) != int32.MinValue) { return 2; }
                if (WrapUInt64ToUInt32(uint64.MaxValue) != uint32.MaxValue
                    || WrapUInt64ToInt32(4294967295) != -1) { return 3; }
                if (WrapInt64ToUInt64(-1) != uint64.MaxValue
                    || WrapUInt64ToInt64(uint64.MaxValue) != -1) { return 4; }
                return 42;
            }
            """);
        var compilation = Compilation.Create(consumerSource, ModuleCompiler.CreateReference(producer),
            (BoundRuntimePolicy)policy);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(((global::System.Func<BoundProgram>)(() => { var __nunitActual19 = compilation.Program; Assert.That(__nunitActual19, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual19!; }))(), producer);
        Assert.That(NativeJit.Execute(ModuleCompiler.Link(consumer, producer), policy), Is.EqualTo(42));
        await ExecuteBothAsync(consumer, producer, 42, policy).ConfigureAwait(true);
    }
    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public async Task WrappingChar16ConversionsKeepLowSixteenBitsAsync(NativeRuntimePolicy policy)
    {
        var producer = CreateNumericsLibrary("IntegerWrapping.terrum", policy,
            "Terrum.CodeAnalysis.Numerics64.WrapInt32ToChar16",
            "Terrum.CodeAnalysis.Numerics64.WrapUInt32ToChar16",
            "Terrum.CodeAnalysis.Numerics64.WrapInt64ToChar16",
            "Terrum.CodeAnalysis.Numerics64.WrapUInt64ToChar16",
            "Terrum.CodeAnalysis.Numerics64.WrapChar16ToInt32",
            "Terrum.CodeAnalysis.Numerics64.WrapChar16ToUInt32",
            "Terrum.CodeAnalysis.Numerics64.WrapChar16ToInt64",
            "Terrum.CodeAnalysis.Numerics64.WrapChar16ToUInt64");
        var source = SyntaxTree.Parse("""
            using Terrum.CodeAnalysis.Numerics64;
            int32 Main() {
                if (WrapInt32ToChar16(-1) != char16(65535)
                    || WrapInt32ToChar16(-65535) != char16(1)
                    || WrapInt32ToChar16(int32.MinValue) != char16(0)
                    || WrapInt32ToChar16(65536) != char16(0)
                    || WrapUInt32ToChar16(55296) != char16(55296)
                    || WrapUInt32ToChar16(uint32.MaxValue) != char16(65535)
                    || WrapInt64ToChar16(-4294967297) != char16(65535)
                    || WrapInt64ToChar16(int64.MinValue) != char16(0)
                    || WrapInt64ToChar16(65536) != char16(0)
                    || WrapUInt64ToChar16(uint64.MaxValue) != char16(65535)
                    || WrapUInt64ToChar16(65536) != char16(0)) { return 0; }
                if (WrapChar16ToInt32(char16(0)) != 0
                    || WrapChar16ToInt32(char16(55296)) != 55296
                    || WrapChar16ToUInt32(char16(65535)) != 65535
                    || WrapChar16ToInt64(char16(55296)) != 55296
                    || WrapChar16ToUInt64(char16(65535)) != 65535) { return 1; }
                return 42;
            }
            """);
        var compilation = Compilation.Create(source, ModuleCompiler.CreateReference(producer),
            (BoundRuntimePolicy)policy);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(((global::System.Func<BoundProgram>)(() => { var __nunitActual20 = compilation.Program; Assert.That(__nunitActual20, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual20!; }))(), producer);
        Assert.That(NativeJit.Execute(ModuleCompiler.Link(consumer, producer), policy), Is.EqualTo(42));
        await ExecuteBothAsync(consumer, producer, 42, policy).ConfigureAwait(true);
    }
    [TestCase("TryAddInt64(int64.MaxValue, 1, init var value); return int32(value);")]
    [TestCase("if (!TryAddInt64(int64.MaxValue, 1, init var value)) { return int32(value); } return 0;")]
    [TestCase("uint32 value = 1; require TryAddInt64(value, 2, init var sum); return int32(sum);")]
    [TestCase("TryAccumulateDecimal(1, 10, uint64.MaxValue, init var value); return int32(value);")]
    [TestCase("TryAccumulateBase(1, 2, 2, uint64.MaxValue, init var value); return int32(value);")]
    public void WideCandidateContractsRejectInvalidUse(string body)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(
            "using Terrum.CodeAnalysis.Numerics64; int32 Main() { " + body + " }"),
            ModuleCompiler.CreateReference(CreateCandidateLibrary()));
        Assert.That(compilation.HasErrors, Is.True);
        Assert.That(compilation.Program, Is.Null);
    }

    [Test]
    public async Task WideModuleContractsPermitImplementationReplacementButNotSignednessChangesAsync()
    {
        var original = Compilation.CreateLibrary(SyntaxTree.Parse("public int64 Echo(int64 value) { return value; }"));
        Assert.That(original.HasErrors, Is.False, string.Join(Environment.NewLine, original.Diagnostics));
        var producer = ModuleCompiler.CreateProducer(((global::System.Func<BoundProgram>)(() => { var __nunitActual21 = original.Program; Assert.That(__nunitActual21, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual21!; }))(), ["Echo"]);
        var compilation = Compilation.Create(SyntaxTree.Parse("int32 Main() { return int32(Echo(42)); }"),
            ModuleCompiler.CreateReference(producer));
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(((global::System.Func<BoundProgram>)(() => { var __nunitActual22 = compilation.Program; Assert.That(__nunitActual22, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual22!; }))(), producer);
        var preserved = (byte[])consumer.Clone();
        await ExecuteBothAsync(consumer, producer, 42).ConfigureAwait(true);
        var changed = Compilation.CreateLibrary(SyntaxTree.Parse("public int64 Echo(int64 value) { return value + 1; }"));
        Assert.That(changed.HasErrors, Is.False, string.Join(Environment.NewLine, changed.Diagnostics));
        var replacement = ModuleCompiler.CreateProducer(((global::System.Func<BoundProgram>)(() => { var __nunitActual23 = changed.Program; Assert.That(__nunitActual23, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual23!; }))(), ["Echo"]);
        await ExecuteBothAsync(consumer, replacement, 43).ConfigureAwait(true);
        Assert.That(consumer, Is.EqualTo(preserved));

        var incompatible = Compilation.CreateLibrary(SyntaxTree.Parse("public uint64 Echo(uint64 value) { return value; }"));
        Assert.That(incompatible.HasErrors, Is.False, string.Join(Environment.NewLine, incompatible.Diagnostics));
        _ = Assert.Throws<ModuleContractException>(() => ModuleCompiler.Link(consumer,
            ModuleCompiler.CreateProducer(((global::System.Func<BoundProgram>)(() => { var __nunitActual24 = incompatible.Program; Assert.That(__nunitActual24, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual24!; }))(), ["Echo"])))!;
    }

    private static byte[] CreateCandidateLibrary(BoundRuntimePolicy policy = BoundRuntimePolicy.FailFast)
    {
        var path = SelfHostPaths.ResolveFile(RepositoryRoot(), "WideIntegerFacts.terrum");
        var compilation = Compilation.CreateLibrary(ReadTree(path), policy);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        return ModuleCompiler.CreateProducer(((global::System.Func<BoundProgram>)(() => { var __nunitActual25 = compilation.Program; Assert.That(__nunitActual25, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual25!; }))(), [
            "Terrum.CodeAnalysis.Numerics64.TryAddInt64",
            "Terrum.CodeAnalysis.Numerics64.TrySubtractInt64",
            "Terrum.CodeAnalysis.Numerics64.TryMultiplyInt64",
            "Terrum.CodeAnalysis.Numerics64.TryDivideInt64",
            "Terrum.CodeAnalysis.Numerics64.TryRemainderInt64",
            "Terrum.CodeAnalysis.Numerics64.TryDivideUInt64",
            "Terrum.CodeAnalysis.Numerics64.TryRemainderUInt64",
            "Terrum.CodeAnalysis.Numerics64.TryAccumulateDecimal",
            "Terrum.CodeAnalysis.Numerics64.TryAccumulateBase",
        ]);
    }

    private static byte[] CreateNumericsLibrary(string name, NativeRuntimePolicy policy, params string[] exports)
    {
        var source = ReadTree(SelfHostPaths.ResolveFile(RepositoryRoot(), name));
        var compilation = Compilation.CreateLibrary(source, (BoundRuntimePolicy)policy);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        return ModuleCompiler.CreateProducer(((global::System.Func<BoundProgram>)(() => { var __nunitActual26 = compilation.Program; Assert.That(__nunitActual26, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual26!; }))(), exports);
    }

    private static byte[] CreateIntegerFormattingLibrary(NativeRuntimePolicy policy, params string[] exports)
    {
        var root = RepositoryRoot();
        var trees = new[] {
            ReadTree(SelfHostPaths.ResolveFile(root, "WideIntegerFacts.terrum")),
            ReadTree(SelfHostPaths.ResolveFile(root, "IntegerFormatting.terrum")),
        };
        var compilation = Compilation.CreateLibrary(trees, (BoundRuntimePolicy)policy);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        return ModuleCompiler.CreateProducer(((global::System.Func<BoundProgram>)(() => { var __nunitActual27 = compilation.Program; Assert.That(__nunitActual27, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual27!; }))(), exports);
    }

    private static byte[] CreateBitOperationsLibrary(NativeRuntimePolicy policy) =>
        CreateNumericsLibrary("BitOperations.terrum", policy,
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
            "Terrum.CodeAnalysis.Numerics64.TryRoundUpToPowerOfTwoUInt64");

    private static async Task ExecuteBitOperationsPositionConsumerAsync(
        NativeRuntimePolicy policy, bool includeUInt64)
    {
        var producer = CreateBitOperationsLibrary(policy);
        var compilation = Compilation.Create(
            SyntaxTree.Parse(BuildBitOperationsPositionConsumer(includeUInt64)),
            ModuleCompiler.CreateReference(producer), (BoundRuntimePolicy)policy);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(((global::System.Func<BoundProgram>)(() => { var __nunitActual28 = compilation.Program; Assert.That(__nunitActual28, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual28!; }))(), producer);
        Assert.That(NativeJit.Execute(ModuleCompiler.Link(consumer, producer), policy), Is.EqualTo(42));
        await ExecuteBothAsync(consumer, producer, 42, policy).ConfigureAwait(true);
    }

    private static string BuildBitOperationsPositionConsumer(bool includeUInt64)
    {
        var source = new StringBuilder("using Terrum.CodeAnalysis.Numerics64;\nint32 Main() {\n");
        var failureCode = 1;
        if (!includeUInt64)
        {
            int[] counts32 = [int.MinValue, -1, 1, int.MaxValue];
            for (var bit = 0; bit < 32; bit++)
            {
                var oneBit = 1U << bit;
                foreach (var value in new uint[] { oneBit, ~oneBit })
                {
                    var literal = Hex(value);
                    AppendBitCheck(source, $"LeadingZeroCountUInt32({literal})",
                        BitOperations.LeadingZeroCount(value).ToString(CultureInfo.InvariantCulture), ref failureCode);
                    AppendBitCheck(source, $"TrailingZeroCountUInt32({literal})",
                        BitOperations.TrailingZeroCount(value).ToString(CultureInfo.InvariantCulture), ref failureCode);
                    AppendBitCheck(source, $"PopCountUInt32({literal})",
                        BitOperations.PopCount(value).ToString(CultureInfo.InvariantCulture), ref failureCode);
                    AppendBitCheck(source, $"ReverseBitsUInt32({literal})", Hex(ReverseBits(value)), ref failureCode);
                    AppendBitCheck(source, $"ReverseEndiannessUInt32({literal})",
                        Hex(BinaryPrimitives.ReverseEndianness(value)), ref failureCode);
                    AppendBitCheck(source, $"IsPow2UInt32({literal})",
                        BitOperations.IsPow2(value) ? "true" : "false", ref failureCode);
                    foreach (var count in counts32)
                    {
                        var countLiteral = count.ToString(CultureInfo.InvariantCulture);
                        AppendBitCheck(source, $"RotateLeftUInt32({literal}, {countLiteral})",
                            Hex(BitOperations.RotateLeft(value, count)), ref failureCode);
                        AppendBitCheck(source, $"RotateRightUInt32({literal}, {countLiteral})",
                            Hex(BitOperations.RotateRight(value, count)), ref failureCode);
                    }
                }

                var power = 1U << bit;
                foreach (var value in new uint[] { power - 1, power, power + 1 })
                {
                    var expected = BitOperations.RoundUpToPowerOf2(value);
                    AppendRoundUpCheck(source, "TryRoundUpToPowerOfTwoUInt32", Hex(value), Hex(expected),
                        expected != 0, ref failureCode);
                }
            }
            AppendRoundUpCheck(source, "TryRoundUpToPowerOfTwoUInt32", Hex(uint.MaxValue),
                Hex(BitOperations.RoundUpToPowerOf2(uint.MaxValue)), false, ref failureCode);
        }
        else
        {
            int[] counts64 = [int.MinValue, -1, 1, int.MaxValue];
            for (var bit = 0; bit < 64; bit++)
            {
                var oneBit = 1UL << bit;
                foreach (var value in new ulong[] { oneBit, ~oneBit })
                {
                    var literal = Hex(value);
                    AppendBitCheck(source, $"LeadingZeroCountUInt64({literal})",
                        BitOperations.LeadingZeroCount(value).ToString(CultureInfo.InvariantCulture), ref failureCode);
                    AppendBitCheck(source, $"TrailingZeroCountUInt64({literal})",
                        BitOperations.TrailingZeroCount(value).ToString(CultureInfo.InvariantCulture), ref failureCode);
                    AppendBitCheck(source, $"PopCountUInt64({literal})",
                        BitOperations.PopCount(value).ToString(CultureInfo.InvariantCulture), ref failureCode);
                    AppendBitCheck(source, $"ReverseBitsUInt64({literal})", Hex(ReverseBits(value)), ref failureCode);
                    AppendBitCheck(source, $"ReverseEndiannessUInt64({literal})",
                        Hex(BinaryPrimitives.ReverseEndianness(value)), ref failureCode);
                    AppendBitCheck(source, $"IsPow2UInt64({literal})",
                        BitOperations.IsPow2(value) ? "true" : "false", ref failureCode);
                    foreach (var count in counts64)
                    {
                        var countLiteral = count.ToString(CultureInfo.InvariantCulture);
                        AppendBitCheck(source, $"RotateLeftUInt64({literal}, {countLiteral})",
                            Hex(BitOperations.RotateLeft(value, count)), ref failureCode);
                        AppendBitCheck(source, $"RotateRightUInt64({literal}, {countLiteral})",
                            Hex(BitOperations.RotateRight(value, count)), ref failureCode);
                    }
                }

                var power = 1UL << bit;
                foreach (var value in new ulong[] { power - 1, power, power + 1 })
                {
                    var expected = BitOperations.RoundUpToPowerOf2(value);
                    AppendRoundUpCheck(source, "TryRoundUpToPowerOfTwoUInt64", Hex(value), Hex(expected),
                        expected != 0, ref failureCode);
                }
            }
            AppendRoundUpCheck(source, "TryRoundUpToPowerOfTwoUInt64", Hex(ulong.MaxValue),
                Hex(BitOperations.RoundUpToPowerOf2(ulong.MaxValue)), false, ref failureCode);
        }

        source.AppendLine("return 42;").AppendLine("}");
        return source.ToString();
    }

    private static string BuildBitOperationsConsumer()
    {
        var source = new StringBuilder("using Terrum.CodeAnalysis.Numerics64;\nint32 Main() {\n");
        var failureCode = 1;
        uint[] values32 = [0, 1, 2, 3, 0x80000000, 0x80000001, 0x01234567, 0xAAAAAAAA, 0x55555555, uint.MaxValue];
        ulong[] values64 = [0, 1, 2, 3, 0x8000000000000000, 0x8000000000000001,
            0x0123456789ABCDEF, 0xAAAAAAAAAAAAAAAA, 0x5555555555555555, ulong.MaxValue];
        int[] counts = [int.MinValue, -65, -64, -33, -32, -1, 0, 1, 31, 32, 33, 63, 64, 65, int.MaxValue];

        foreach (uint value in values32)
        {
            var literal = Hex(value);
            AppendBitCheck(source, $"LeadingZeroCountUInt32({literal})",
                BitOperations.LeadingZeroCount(value).ToString(CultureInfo.InvariantCulture), ref failureCode);
            AppendBitCheck(source, $"TrailingZeroCountUInt32({literal})",
                BitOperations.TrailingZeroCount(value).ToString(CultureInfo.InvariantCulture), ref failureCode);
            AppendBitCheck(source, $"PopCountUInt32({literal})",
                BitOperations.PopCount(value).ToString(CultureInfo.InvariantCulture), ref failureCode);
            AppendBitCheck(source, $"ReverseBitsUInt32({literal})", Hex(ReverseBits(value)), ref failureCode);
            AppendBitCheck(source, $"ReverseEndiannessUInt32({literal})",
                Hex(BinaryPrimitives.ReverseEndianness(value)), ref failureCode);
            AppendBitCheck(source, $"IsPow2UInt32({literal})",
                BitOperations.IsPow2(value) ? "true" : "false", ref failureCode);
            foreach (int count in counts)
            {
                var countLiteral = count.ToString(CultureInfo.InvariantCulture);
                AppendBitCheck(source, $"RotateLeftUInt32({literal}, {countLiteral})",
                    Hex(BitOperations.RotateLeft(value, count)), ref failureCode);
                AppendBitCheck(source, $"RotateRightUInt32({literal}, {countLiteral})",
                    Hex(BitOperations.RotateRight(value, count)), ref failureCode);
            }
        }

        foreach (ulong value in values64)
        {
            var literal = Hex(value);
            AppendBitCheck(source, $"LeadingZeroCountUInt64({literal})",
                BitOperations.LeadingZeroCount(value).ToString(CultureInfo.InvariantCulture), ref failureCode);
            AppendBitCheck(source, $"TrailingZeroCountUInt64({literal})",
                BitOperations.TrailingZeroCount(value).ToString(CultureInfo.InvariantCulture), ref failureCode);
            AppendBitCheck(source, $"PopCountUInt64({literal})",
                BitOperations.PopCount(value).ToString(CultureInfo.InvariantCulture), ref failureCode);
            AppendBitCheck(source, $"ReverseBitsUInt64({literal})", Hex(ReverseBits(value)), ref failureCode);
            AppendBitCheck(source, $"ReverseEndiannessUInt64({literal})",
                Hex(BinaryPrimitives.ReverseEndianness(value)), ref failureCode);
            AppendBitCheck(source, $"IsPow2UInt64({literal})",
                BitOperations.IsPow2(value) ? "true" : "false", ref failureCode);
            foreach (int count in counts)
            {
                var countLiteral = count.ToString(CultureInfo.InvariantCulture);
                AppendBitCheck(source, $"RotateLeftUInt64({literal}, {countLiteral})",
                    Hex(BitOperations.RotateLeft(value, count)), ref failureCode);
                AppendBitCheck(source, $"RotateRightUInt64({literal}, {countLiteral})",
                    Hex(BitOperations.RotateRight(value, count)), ref failureCode);
            }
        }

        uint[] round32 = [0, 1, 2, 3, 5, 0x40000000, 0x40000001, 0x7FFFFFFF, 0x80000000, 0x80000001, uint.MaxValue];
        foreach (uint value in round32)
        {
            var expected = BitOperations.RoundUpToPowerOf2(value);
            AppendRoundUpCheck(source, "TryRoundUpToPowerOfTwoUInt32", Hex(value), Hex(expected),
                expected != 0, ref failureCode);
        }

        ulong[] round64 = [0, 1, 2, 3, 5, 0x4000000000000000, 0x4000000000000001,
            0x7FFFFFFFFFFFFFFF, 0x8000000000000000, 0x8000000000000001, ulong.MaxValue];
        foreach (ulong value in round64)
        {
            var expected = BitOperations.RoundUpToPowerOf2(value);
            AppendRoundUpCheck(source, "TryRoundUpToPowerOfTwoUInt64", Hex(value), Hex(expected),
                expected != 0, ref failureCode);
        }

        AppendBitCheck(source, "ReverseBitsUInt32(0x01234567)", "0xE6A2C480", ref failureCode);
        AppendBitCheck(source, "ReverseBitsUInt64(0x0123456789ABCDEF)", "0xF7B3D591E6A2C480",
            ref failureCode);
        source.AppendLine("return 42;").AppendLine("}");
        return source.ToString();
    }

    private static string BuildIntegerMathConsumer()
    {
        var source = new StringBuilder("using Terrum.CodeAnalysis.Numerics64;\nint32 Main() {\n");
        var failureCode = 1;
        int[] values32 = [int.MinValue, int.MinValue + 1, -17, -1, 0, 1, 17, int.MaxValue];
        long[] values64 = [long.MinValue, long.MinValue + 1, -17, -1, 0, 1, 17, long.MaxValue];
        foreach (int value in values32)
        {
            var literal = SignedLiteral(value);
            if (value != int.MinValue)
            {
                AppendBitCheck(source, $"AbsInt32({literal})", SignedLiteral(Math.Abs(value)), ref failureCode);
            }
            AppendTryAbsCheck(source, "TryAbsInt32", literal,
                value == int.MinValue ? "0" : SignedLiteral(Math.Abs(value)), value != int.MinValue, ref failureCode);
            AppendBitCheck(source, $"SignInt32({literal})", Math.Sign(value).ToString(CultureInfo.InvariantCulture),
                ref failureCode);
        }
        foreach (int left in values32)
        {
            foreach (int right in values32)
            {
                var leftLiteral = SignedLiteral(left);
                var rightLiteral = SignedLiteral(right);
                AppendBitCheck(source, $"MinInt32({leftLiteral}, {rightLiteral})",
                    SignedLiteral(Math.Min(left, right)), ref failureCode);
                AppendBitCheck(source, $"MaxInt32({leftLiteral}, {rightLiteral})",
                    SignedLiteral(Math.Max(left, right)), ref failureCode);
                AppendBitCheck(source, $"BigMultiplyInt32({leftLiteral}, {rightLiteral})",
                    ((long)left * right).ToString(CultureInfo.InvariantCulture), ref failureCode);
                AppendBitCheck(source, $"GreatestCommonDivisorInt32({leftLiteral}, {rightLiteral})",
                    Hex(GreatestCommonDivisor(Magnitude(left), Magnitude(right))), ref failureCode);
            }
        }

        foreach (int value in values32)
        {
            AppendBitCheck(source, $"ClampInt32({SignedLiteral(value)}, -5, 5)",
                SignedLiteral(Math.Clamp(value, -5, 5)), ref failureCode);
        }

        foreach (long value in values64)
        {
            var literal = SignedLiteral(value);
            if (value != long.MinValue)
            {
                AppendBitCheck(source, $"AbsInt64({literal})", SignedLiteral(Math.Abs(value)), ref failureCode);
            }
            AppendTryAbsCheck(source, "TryAbsInt64", literal,
                value == long.MinValue ? "0" : SignedLiteral(Math.Abs(value)), value != long.MinValue, ref failureCode);
            AppendBitCheck(source, $"SignInt64({literal})", Math.Sign(value).ToString(CultureInfo.InvariantCulture),
                ref failureCode);
        }
        foreach (long left in values64)
        {
            foreach (long right in values64)
            {
                var leftLiteral = SignedLiteral(left);
                var rightLiteral = SignedLiteral(right);
                AppendBitCheck(source, $"MinInt64({leftLiteral}, {rightLiteral})",
                    SignedLiteral(Math.Min(left, right)), ref failureCode);
                AppendBitCheck(source, $"MaxInt64({leftLiteral}, {rightLiteral})",
                    SignedLiteral(Math.Max(left, right)), ref failureCode);
                AppendBitCheck(source, $"GreatestCommonDivisorInt64({leftLiteral}, {rightLiteral})",
                    Hex(GreatestCommonDivisor(Magnitude(left), Magnitude(right))), ref failureCode);
            }
        }

        foreach (long value in values64)
        {
            AppendBitCheck(source, $"ClampInt64({SignedLiteral(value)}, -5, 5)",
                SignedLiteral(Math.Clamp(value, -5, 5)), ref failureCode);
        }

        source.AppendLine("return 42;").AppendLine("}");
        return source.ToString();
    }

    private static string BuildFloat32MathSelectionConsumer()
    {
        var source = new StringBuilder("""
            using Terrum.CodeAnalysis.Numerics64;
            int32 Main() {
                float32 positiveInfinity = 1.0 / 0.0;
                float32 negativeInfinity = -positiveInfinity;
                float32 notANumber = 0.0 / 0.0;
                float32 negativeZero = -0.0;
                float32 positiveZero = 0.0;
            """);
        var failureCode = 1;

        AppendBitCheck(source, "AbsFloat32(-1.5)", "1.5", ref failureCode);
        AppendBitCheck(source, "AbsFloat32(negativeInfinity)", "positiveInfinity", ref failureCode);
        AppendBitCheck(source, "AbsFloat32(notANumber) == AbsFloat32(notANumber)", "false", ref failureCode);
        AppendBitCheck(source, "AbsFloat32(negativeZero) != positiveZero", "false", ref failureCode);
        AppendBitCheck(source, "1.0 / AbsFloat32(negativeZero) < 0.0", "false", ref failureCode);

        AppendBitCheck(source, "MinFloat32(positiveInfinity, negativeInfinity)", "negativeInfinity",
            ref failureCode);
        AppendBitCheck(source, "MinFloat32(notANumber, 1.0) == MinFloat32(notANumber, 1.0)", "false",
            ref failureCode);
        AppendBitCheck(source, "MinFloat32(1.0, notANumber) == MinFloat32(1.0, notANumber)", "false",
            ref failureCode);
        AppendBitCheck(source, "MinFloat32(positiveZero, negativeZero) != positiveZero", "false",
            ref failureCode);
        AppendBitCheck(source, "1.0 / MinFloat32(positiveZero, negativeZero) < 0.0", "true",
            ref failureCode);
        AppendBitCheck(source, "MinFloat32(negativeZero, positiveZero) != positiveZero", "false",
            ref failureCode);

        AppendBitCheck(source, "MaxFloat32(negativeInfinity, positiveInfinity)", "positiveInfinity",
            ref failureCode);
        AppendBitCheck(source, "MaxFloat32(notANumber, 1.0) == MaxFloat32(notANumber, 1.0)", "false",
            ref failureCode);
        AppendBitCheck(source, "MaxFloat32(1.0, notANumber) == MaxFloat32(1.0, notANumber)", "false",
            ref failureCode);
        AppendBitCheck(source, "1.0 / MaxFloat32(negativeZero, positiveZero) < 0.0", "false",
            ref failureCode);
        AppendBitCheck(source, "1.0 / MaxFloat32(positiveZero, negativeZero) < 0.0", "false",
            ref failureCode);
        AppendBitCheck(source, "MaxFloat32(negativeZero, negativeZero) != negativeZero", "false",
            ref failureCode);
        AppendBitCheck(source, "1.0 / MaxFloat32(negativeZero, negativeZero) < 0.0", "true",
            ref failureCode);

        AppendBitCheck(source, "ClampFloat32(-2.0, -1.0, 1.0)", "-1.0", ref failureCode);
        AppendBitCheck(source, "ClampFloat32(2.0, -1.0, 1.0)", "1.0", ref failureCode);
        AppendBitCheck(source, "ClampFloat32(notANumber, -1.0, 1.0) == notANumber", "false",
            ref failureCode);
        AppendBitCheck(source, "ClampFloat32(negativeZero, positiveZero, 1.0) != positiveZero", "false",
            ref failureCode);
        AppendBitCheck(source, "1.0 / ClampFloat32(negativeZero, positiveZero, 1.0) < 0.0", "true",
            ref failureCode);
        AppendBitCheck(source, "ClampFloat32(negativeZero, positiveZero, positiveZero) != positiveZero",
            "false", ref failureCode);
        AppendBitCheck(source, "1.0 / ClampFloat32(negativeZero, positiveZero, positiveZero) < 0.0",
            "true", ref failureCode);
        AppendBitCheck(source, "ClampFloat32(1.5, negativeInfinity, positiveInfinity)", "1.5",
            ref failureCode);

        float[] values = [-123.5f, -2.25f, -1.0f, 0.125f, 2.25f, 123.5f];
        foreach (float value in values)
        {
            var literal = FloatLiteral(value);
            AppendBitCheck(source, $"AbsFloat32({literal})", FloatLiteral(MathF.Abs(value)), ref failureCode);
            AppendBitCheck(source, $"ClampFloat32({literal}, -2.25, 2.25)",
                FloatLiteral(Math.Clamp(value, -2.25f, 2.25f)), ref failureCode);
            foreach (float other in values)
            {
                var otherLiteral = FloatLiteral(other);
                AppendBitCheck(source, $"MinFloat32({literal}, {otherLiteral})",
                    FloatLiteral(MathF.Min(value, other)), ref failureCode);
                AppendBitCheck(source, $"MaxFloat32({literal}, {otherLiteral})",
                    FloatLiteral(MathF.Max(value, other)), ref failureCode);
            }
        }

        source.AppendLine("return 42;").AppendLine("}");
        return source.ToString();
    }

    private static string BuildFloat64MathSelectionConsumer()
    {
        var source = new StringBuilder("""
            using Terrum.CodeAnalysis.Numerics64;
            int32 Main() {
                float64 positiveInfinity = 1.0 / 0.0;
                float64 negativeInfinity = -positiveInfinity;
                float64 notANumber = 0.0 / 0.0;
                float64 negativeZero = -0.0;
                float64 positiveZero = 0.0;
            """);
        var failureCode = 1;

        AppendBitCheck(source, "AbsFloat64(-1.5)", "1.5", ref failureCode);
        AppendBitCheck(source, "AbsFloat64(negativeInfinity)", "positiveInfinity", ref failureCode);
        AppendBitCheck(source, "AbsFloat64(notANumber) == AbsFloat64(notANumber)", "false", ref failureCode);
        AppendBitCheck(source, "AbsFloat64(negativeZero) != positiveZero", "false", ref failureCode);
        AppendBitCheck(source, "1.0 / AbsFloat64(negativeZero) < 0.0", "false", ref failureCode);

        AppendBitCheck(source, "MinFloat64(positiveInfinity, negativeInfinity)", "negativeInfinity",
            ref failureCode);
        AppendBitCheck(source, "MinFloat64(notANumber, 1.0) == MinFloat64(notANumber, 1.0)", "false",
            ref failureCode);
        AppendBitCheck(source, "MinFloat64(1.0, notANumber) == MinFloat64(1.0, notANumber)", "false",
            ref failureCode);
        AppendBitCheck(source, "MinFloat64(positiveZero, negativeZero) != positiveZero", "false",
            ref failureCode);
        AppendBitCheck(source, "1.0 / MinFloat64(positiveZero, negativeZero) < 0.0", "true",
            ref failureCode);
        AppendBitCheck(source, "MinFloat64(negativeZero, positiveZero) != positiveZero", "false",
            ref failureCode);

        AppendBitCheck(source, "MaxFloat64(negativeInfinity, positiveInfinity)", "positiveInfinity",
            ref failureCode);
        AppendBitCheck(source, "MaxFloat64(notANumber, 1.0) == MaxFloat64(notANumber, 1.0)", "false",
            ref failureCode);
        AppendBitCheck(source, "MaxFloat64(1.0, notANumber) == MaxFloat64(1.0, notANumber)", "false",
            ref failureCode);
        AppendBitCheck(source, "1.0 / MaxFloat64(negativeZero, positiveZero) < 0.0", "false",
            ref failureCode);
        AppendBitCheck(source, "1.0 / MaxFloat64(positiveZero, negativeZero) < 0.0", "false",
            ref failureCode);
        AppendBitCheck(source, "MaxFloat64(negativeZero, negativeZero) != negativeZero", "false",
            ref failureCode);
        AppendBitCheck(source, "1.0 / MaxFloat64(negativeZero, negativeZero) < 0.0", "true",
            ref failureCode);

        AppendBitCheck(source, "ClampFloat64(-2.0, -1.0, 1.0)", "-1.0", ref failureCode);
        AppendBitCheck(source, "ClampFloat64(2.0, -1.0, 1.0)", "1.0", ref failureCode);
        AppendBitCheck(source, "ClampFloat64(notANumber, -1.0, 1.0) == notANumber", "false",
            ref failureCode);
        AppendBitCheck(source, "ClampFloat64(negativeZero, positiveZero, 1.0) != positiveZero", "false",
            ref failureCode);
        AppendBitCheck(source, "1.0 / ClampFloat64(negativeZero, positiveZero, 1.0) < 0.0", "true",
            ref failureCode);
        AppendBitCheck(source, "ClampFloat64(negativeZero, positiveZero, positiveZero) != positiveZero",
            "false", ref failureCode);
        AppendBitCheck(source, "1.0 / ClampFloat64(negativeZero, positiveZero, positiveZero) < 0.0",
            "true", ref failureCode);
        AppendBitCheck(source, "ClampFloat64(1.5, negativeInfinity, positiveInfinity)", "1.5",
            ref failureCode);

        double[] values = [-123.5, -2.25, -1.0, 0.125, 2.25, 123.5];
        foreach (double value in values)
        {
            var literal = DoubleLiteral(value);
            AppendBitCheck(source, $"AbsFloat64({literal})", DoubleLiteral(Math.Abs(value)), ref failureCode);
            AppendBitCheck(source, $"ClampFloat64({literal}, -2.25, 2.25)",
                DoubleLiteral(Math.Clamp(value, -2.25, 2.25)), ref failureCode);
            foreach (double other in values)
            {
                var otherLiteral = DoubleLiteral(other);
                AppendBitCheck(source, $"MinFloat64({literal}, {otherLiteral})",
                    DoubleLiteral(Math.Min(value, other)), ref failureCode);
                AppendBitCheck(source, $"MaxFloat64({literal}, {otherLiteral})",
                    DoubleLiteral(Math.Max(value, other)), ref failureCode);
            }
        }

        source.AppendLine("return 42;").AppendLine("}");
        return source.ToString();
    }

    private static void AppendBitCheck(StringBuilder source, string expression, string expected,
        ref int failureCode)
    {
        source.Append("if (").Append(expression).Append(" != ").Append(expected)
            .Append(") { return ").Append(failureCode++).AppendLine("; }");
    }

    private static void AppendTryAbsCheck(StringBuilder source, string method, string value,
        string expected, bool succeeds, ref int failureCode)
    {
        var code = failureCode++;
        var output = $"absolute{code}";
        var call = $"{method}({value}, init var {output})";
        if (!succeeds)
        {
            source.Append("if (").Append(call).Append(") { return ").Append(code).AppendLine("; }");
            source.Append("require ").Append(method).Append("(1, init ").Append(output).AppendLine(");");
            AppendBitCheck(source, output, "1", ref failureCode);
            return;
        }

        source.Append("require ").Append(call).AppendLine(";");
        AppendBitCheck(source, output, expected, ref failureCode);
    }

    private static void AppendRoundUpCheck(StringBuilder source, string method, string value,
        string expected, bool succeeds, ref int failureCode)
    {
        var code = failureCode++;
        var output = $"rounded{code}";
        var call = $"{method}({value}, init var {output})";
        if (!succeeds)
        {
            source.Append("if (").Append(call).Append(") { return ").Append(code).AppendLine("; }");
            source.Append("require ").Append(method).Append("(1, init ").Append(output).AppendLine(");");
            AppendBitCheck(source, output, "1", ref failureCode);
            return;
        }

        source.Append("require ").Append(call).AppendLine(";");
        AppendBitCheck(source, output, expected, ref failureCode);
    }

    private static string Hex(uint value) => $"0x{value:X8}";

    private static string Hex(ulong value) => $"0x{value:X16}";

    private static string FloatLiteral(float value) => value.ToString("R", CultureInfo.InvariantCulture);

    private static string DoubleLiteral(double value) => value.ToString("R", CultureInfo.InvariantCulture);

    private static string SignedLiteral(int value) =>
        value == int.MinValue ? "int32.MinValue" : value.ToString(CultureInfo.InvariantCulture);

    private static string SignedLiteral(long value) =>
        value == long.MinValue ? "int64.MinValue" : value.ToString(CultureInfo.InvariantCulture);

    private static uint Magnitude(int value) => value < 0 ? (uint)-((long)value) : (uint)value;

    private static ulong Magnitude(long value) => value < 0 ? (ulong)(-(value + 1)) + 1 : (ulong)value;

    private static uint GreatestCommonDivisor(uint left, uint right)
    {
        while (right != 0)
        {
            (left, right) = (right, left % right);
        }
        return left;
    }

    private static ulong GreatestCommonDivisor(ulong left, ulong right)
    {
        while (right != 0)
        {
            (left, right) = (right, left % right);
        }
        return left;
    }

    private static uint ReverseBits(uint value)
    {
        uint reversed = 0;
        for (var index = 0; index < 32; index++)
        {
            reversed = (reversed << 1) | (value & 1);
            value >>>= 1;
        }
        return reversed;
    }

    private static ulong ReverseBits(ulong value)
    {
        ulong reversed = 0;
        for (var index = 0; index < 64; index++)
        {
            reversed = (reversed << 1) | (value & 1);
            value >>>= 1;
        }
        return reversed;
    }

    private static SyntaxTree ReadTree(string path) => SyntaxTree.Parse(File.ReadAllText(path), path);

    private static string RepositoryRoot() =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));

    private static async Task ExecuteBothAsync(byte[] consumer, byte[] producer, int expected,
        NativeRuntimePolicy policy = NativeRuntimePolicy.FailFast)
    {
        var prefix = Path.Combine(TestArtifactPaths.DirectoryPath, $"wide-candidate-{Guid.NewGuid():N}");
        var image = prefix + ".exe";
        var consumerPath = prefix + "-consumer.trm";
        var producerPath = prefix + "-producer.trm";
        try
        {
            var program = ModuleCompiler.Link(consumer, producer);
            await File.WriteAllBytesAsync(image, NativeEmitter.Emit(program, policy)).ConfigureAwait(true);
            await RunAsync(image, [], expected).ConfigureAwait(true);
            await File.WriteAllBytesAsync(consumerPath, consumer).ConfigureAwait(true);
            await File.WriteAllBytesAsync(producerPath, producer).ConfigureAwait(true);
            var framework = new DirectoryInfo(AppContext.BaseDirectory);
            var configuration = framework.Parent?.Name ?? throw new InvalidOperationException("Missing configuration.");
            var cli = Path.Combine(RepositoryRoot(), "artifacts", "bin", "sources", "terrumc", configuration, framework.Name, "terrumc.dll");
            await RunAsync("dotnet", [cli, "run-module", consumerPath, producerPath, "--runtime",
                policy == NativeRuntimePolicy.FailFast ? "fail-fast" : "checked-exceptions"], expected).ConfigureAwait(true);
            Assert.That(await File.ReadAllBytesAsync(consumerPath).ConfigureAwait(true), Is.EqualTo(consumer));
        }
        finally
        {
            await TestFileCleanup.DeleteWithRetryAsync(image).ConfigureAwait(true);
            File.Delete(consumerPath);
            File.Delete(producerPath);
        }
    }

    private static async Task RunAsync(string path, ImmutableArray<string> arguments, int expected)
    {
        var start = new ProcessStartInfo(path) {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
        };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start wide-integer worker.");
        var error = process.StandardError.ReadToEndAsync();
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
        Assert.That(process.ExitCode, Is.EqualTo(expected));
        Assert.That(await error.ConfigureAwait(true), Is.Empty);
    }
}
