// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
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
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed class ModuleTests
{
    [Fact]
    public async Task InternalAliasesEraseBeforeModulePublicationAsync()
    {
        var library = Compilation.CreateLibrary(SyntaxTree.Parse(
            "alias Count = int32; public Count Get() { return 42; }"));
        Assert.False(library.HasErrors, string.Join(Environment.NewLine, library.Diagnostics));
        var producer = ModuleCompiler.CreateProducer(Assert.IsType<BoundProgram>(library.Program), ["Get"]);

        var compilation = Compilation.Create(SyntaxTree.Parse(
            "int32 Main() { return Get(); }"), ModuleCompiler.CreateReference(producer));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(Assert.IsType<BoundProgram>(compilation.Program), producer);
        Assert.Equal(42, await ExecuteAsync(NativeEmitter.Emit(
            ModuleCompiler.Link(consumer, producer), NativeRuntimePolicy.FailFast)).ConfigureAwait(true));
    }

    [Fact]
    public async Task InternalGenericAliasesEraseBeforeModulePublicationAsync()
    {
        var library = Compilation.CreateLibrary(SyntaxTree.Parse(
            "alias Identity<T> = T; public Identity<int32> Get() { return 42; }"));
        Assert.False(library.HasErrors, string.Join(Environment.NewLine, library.Diagnostics));
        var producer = ModuleCompiler.CreateProducer(Assert.IsType<BoundProgram>(library.Program), ["Get"]);

        var compilation = Compilation.Create(SyntaxTree.Parse(
            "int32 Main() { return Get(); }"), ModuleCompiler.CreateReference(producer));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(Assert.IsType<BoundProgram>(compilation.Program), producer);
        Assert.Equal(42, await ExecuteAsync(NativeEmitter.Emit(
            ModuleCompiler.Link(consumer, producer), NativeRuntimePolicy.FailFast)).ConfigureAwait(true));
    }

    [Theory]
    [InlineData(BoundRuntimePolicy.FailFast)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions)]
    public async Task NamedArgumentsRetainParameterMappingAcrossModuleArtifactsAsync(BoundRuntimePolicy policy)
    {
        var library = Compilation.CreateLibrary(SyntaxTree.Parse("""
            public int32 Combine(int32 first, int32 second, int32 third,
                int32 fourth, int32 fifth, int32 sixth) {
                return fifth * 10 + sixth;
            }
            """), policy);
        Assert.False(library.HasErrors, string.Join(Environment.NewLine, library.Diagnostics));
        var producer = ModuleCompiler.CreateProducer(Assert.IsType<BoundProgram>(library.Program), ["Combine"]);
        Assert.Equal(56, BinaryPrimitives.ReadInt32LittleEndian(producer.AsSpan(4, 4)));

        var compilation = Compilation.Create(SyntaxTree.Parse(
            "int32 Main() { return Combine(sixth: 2, fifth: 4, first: 0, second: 0, third: 0, fourth: 0); }"),
            ModuleCompiler.CreateReference(producer), policy);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(Assert.IsType<BoundProgram>(compilation.Program), producer);
        Assert.Equal(56, BinaryPrimitives.ReadInt32LittleEndian(consumer.AsSpan(4, 4)));
        var nativePolicy = policy == BoundRuntimePolicy.FailFast
            ? NativeRuntimePolicy.FailFast : NativeRuntimePolicy.CheckedExceptions;
        Assert.Equal(42, await ExecuteAsync(NativeEmitter.Emit(
            ModuleCompiler.Link(consumer, producer), nativePolicy)).ConfigureAwait(true));
    }

    [Theory]
    [InlineData(BoundRuntimePolicy.FailFast)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions)]
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
        Assert.False(library.HasErrors, string.Join(Environment.NewLine, library.Diagnostics));
        var producer = ModuleCompiler.CreateProducer(Assert.IsType<BoundProgram>(library.Program),
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
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(Assert.IsType<BoundProgram>(compilation.Program), producer);
        var nativePolicy = policy == BoundRuntimePolicy.FailFast
            ? NativeRuntimePolicy.FailFast : NativeRuntimePolicy.CheckedExceptions;
        Assert.Equal(42, await ExecuteAsync(NativeEmitter.Emit(
            ModuleCompiler.Link(consumer, producer), nativePolicy)).ConfigureAwait(true));
    }

    [Theory]
    [InlineData(BoundRuntimePolicy.FailFast, @"samples\native\graph\managed-class-leaf.terrum", "Leaf")]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, @"samples\native\graph\managed-class-leaf.terrum", "Leaf")]
    [InlineData(BoundRuntimePolicy.FailFast, @"samples\native\graph\managed-copy-class-leaf.terrum", "Leaf")]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, @"samples\native\graph\managed-copy-class-leaf.terrum", "Leaf")]
    [InlineData(BoundRuntimePolicy.FailFast, @"tests\native\managed-loop-transfer-abi.terrum", "LoopTransfers")]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, @"tests\native\managed-loop-transfer-abi.terrum", "LoopTransfers")]
    public async Task SemanticLeafExecutesAfterConstructorAndOwnerRemappingAsync(
        BoundRuntimePolicy policy, string sample, string export)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
        var source = await File.ReadAllTextAsync(Path.Combine(root, sample)).ConfigureAwait(true);
        var library = Compilation.CreateLibrary(SyntaxTree.Parse(source), policy);
        Assert.False(library.HasErrors, string.Join(Environment.NewLine, library.Diagnostics));
        var producer = ModuleCompiler.CreateProducer(Assert.IsType<BoundProgram>(library.Program), [export]);
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
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(Assert.IsType<BoundProgram>(compilation.Program), producer);
        var consumerPath = Path.Combine(AppContext.BaseDirectory, $"class-module-{Guid.NewGuid():N}.trm");
        var producerPath = Path.ChangeExtension(consumerPath, ".producer.trm");
        try
        {
            await File.WriteAllBytesAsync(consumerPath, consumer).ConfigureAwait(true);
            await File.WriteAllBytesAsync(producerPath, producer).ConfigureAwait(true);
            var (exit, error) = await RunCliAsync("run-module", consumerPath, producerPath, "--runtime",
                policy == BoundRuntimePolicy.FailFast ? "fail-fast" : "checked-exceptions").ConfigureAwait(true);
            Assert.Equal(44, exit);
            Assert.Empty(error);
            Assert.Equal(44, await ExecuteAsync(NativeEmitter.Emit(ModuleCompiler.Link(consumer, producer),
                (NativeRuntimePolicy)policy)).ConfigureAwait(true));
            Assert.Equal(consumer, await File.ReadAllBytesAsync(consumerPath).ConfigureAwait(true));
        }
        finally
        {
            File.Delete(consumerPath);
            File.Delete(producerPath);
        }
    }

    [Theory]
    [InlineData(7, BoundRuntimePolicy.FailFast, "native-10")]
    [InlineData(8, BoundRuntimePolicy.FailFast, "native-10")]
    [InlineData(7, BoundRuntimePolicy.CheckedExceptions, "native-11")]
    [InlineData(8, BoundRuntimePolicy.CheckedExceptions, "native-11")]
    public async Task ScalarParameterArityDoesNotDeclareNativeBufferOrViewAsync(
        int count, BoundRuntimePolicy policy, string profile)
    {
        var parameters = string.Join(", ", Enumerable.Range(0, count).Select(index => $"int32 arg{index}"));
        var arguments = string.Join(", ", Enumerable.Range(0, count).Select(index =>
            index == 0 ? "41" : index == count - 1 ? "1" : "0"));
        var library = Compilation.CreateLibrary(SyntaxTree.Parse(
            $"public int32 Sum({parameters}) {{ return arg0 + arg{count - 1}; }}"), policy);
        Assert.False(library.HasErrors, string.Join(Environment.NewLine, library.Diagnostics));
        var producer = ModuleCompiler.CreateProducer(Assert.IsType<BoundProgram>(library.Program), ["Sum"]);
        Assert.Equal(profile, ModuleCompiler.GetNativeProfileVersion(producer, isConsumer: false));

        var compilation = Compilation.Create(SyntaxTree.Parse(
            $"int32 Main() {{ return Sum({arguments}); }}"), ModuleCompiler.CreateReference(producer),
            runtimePolicy: policy);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(Assert.IsType<BoundProgram>(compilation.Program), producer);
        Assert.Equal(profile, ModuleCompiler.GetNativeProfileVersion(consumer, isConsumer: true));
        var nativePolicy = policy == BoundRuntimePolicy.FailFast
            ? NativeRuntimePolicy.FailFast : NativeRuntimePolicy.CheckedExceptions;
        Assert.Equal(42, await ExecuteAsync(NativeEmitter.Emit(
            ModuleCompiler.Link(consumer, producer), nativePolicy)).ConfigureAwait(true));
    }

    [Fact]
    public async Task SelfHostComponentTestRunnerExecutesAgainstAnIndependentMultiSourceLibraryAsync()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
        var rangePath = Path.Combine(root, "self-host", "SourceRange.terrum");
        var factsPath = Path.Combine(root, "self-host", "CharacterFacts.terrum");
        var factsTestsPath = Path.Combine(root, "self-host", "CharacterFactsTests.terrum");
        var utf16CodeUnitsPath = Path.Combine(root, "self-host", "Utf16CodeUnits.terrum");
        var integerFactsPath = Path.Combine(root, "self-host", "IntegerFacts.terrum");
        var integerFactsExportsPath = Path.Combine(root, "self-host", "IntegerFactsTestExports.terrum");
        var tokenPath = Path.Combine(root, "self-host", "Token.terrum");
        var wideFactsPath = Path.Combine(root, "self-host", "WideIntegerFacts.terrum");
        var integerFormattingPath = Path.Combine(root, "self-host", "IntegerFormatting.terrum");
        var integerFormattingTestsPath = Path.Combine(root, "self-host", "IntegerFormattingTests.terrum");
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
        var wideFacts = SyntaxTree.Parse(await File.ReadAllTextAsync(wideFactsPath).ConfigureAwait(true), wideFactsPath);
        var integerFormatting = SyntaxTree.Parse(
            await File.ReadAllTextAsync(integerFormattingPath).ConfigureAwait(true),
            integerFormattingPath);
        var integerFormattingTests = SyntaxTree.Parse(
            await File.ReadAllTextAsync(integerFormattingTestsPath).ConfigureAwait(true),
            integerFormattingTestsPath);
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
            token, wideFacts, integerFormatting, integerFormattingTests, checkedFacts, integerMath,
            integerSaturation, integerWrapping, checkedConversions, bitOperations]);
        Assert.False(library.HasErrors, string.Join(Environment.NewLine, library.Diagnostics));
        var producer = ModuleCompiler.CreateProducer(Assert.IsType<BoundProgram>(library.Program), [
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
            "Terrum.CodeAnalysis.Token.#ctor",
            "Terrum.CodeAnalysis.Numerics64.TryAddInt64",
            "Terrum.CodeAnalysis.Numerics64.TrySubtractInt64",
            "Terrum.CodeAnalysis.Numerics64.TryMultiplyInt64",
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
            ModuleCompiler.CreateReference(producer));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.Collection(compilation.SyntaxTrees, tree => Assert.Same(rangeTests, tree),
            tree => Assert.Same(factsTests, tree), tree => Assert.Same(utf16CodeUnitsTests, tree),
            tree => Assert.Same(integerFactsTests, tree), tree => Assert.Same(tokenTests, tree),
            tree => Assert.Same(wideTests, tree),
            tree => Assert.Same(checkedTests, tree), tree => Assert.Same(integerMathTests, tree),
            tree => Assert.Same(integerSaturationTests, tree), tree => Assert.Same(integerWrappingTests, tree),
            tree => Assert.Same(checkedConversionsTests, tree), tree => Assert.Same(bitOperationsTests, tree),
            tree => Assert.Same(entry, tree));
        var consumer = ModuleCompiler.CreateConsumer(Assert.IsType<BoundProgram>(compilation.Program), producer);
        var consumerPath = Path.Combine(AppContext.BaseDirectory, $"self-host-module-{Guid.NewGuid():N}.trm");
        var producerPath = Path.ChangeExtension(consumerPath, ".producer.trm");
        try
        {
            await File.WriteAllBytesAsync(consumerPath, consumer).ConfigureAwait(true);
            await File.WriteAllBytesAsync(producerPath, producer).ConfigureAwait(true);
            var (exit, error) = await RunJitWorkerAsync(consumerPath, producerPath).ConfigureAwait(true);
            Assert.Equal(0, exit);
            Assert.Empty(error);
            Assert.Equal(0, await ExecuteAsync(NativeEmitter.Emit(ModuleCompiler.Link(consumer, producer),
                NativeRuntimePolicy.FailFast)).ConfigureAwait(true));
            Assert.Equal(consumer, await File.ReadAllBytesAsync(consumerPath).ConfigureAwait(true));
        }
        finally
        {
            File.Delete(consumerPath);
            File.Delete(producerPath);
        }
    }

    [Theory]
    [InlineData("range.Start")]
    [InlineData("range.End")]
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
        Assert.True(compilation.HasErrors);
        Assert.Null(compilation.Program);
        Assert.Contains(compilation.Diagnostics, static diagnostic => diagnostic.Code == "TRM3016");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
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
            Assert.Equal(0, libraryExit);
            Assert.Empty(libraryError);
            var (consumerExit, consumerError) = await RunCliAsync("compile-module", "consumer",
                Path.Combine(root, "self-host", tokens ? "TokenTests.terrum" : "RangeTests.terrum"), producer,
                "--source", Path.Combine(root, "samples", "native", "modules", tokens ? "token-entry.terrum" : "range-entry.terrum"),
                "-o", consumer).ConfigureAwait(true);
            Assert.Equal(0, consumerExit);
            Assert.Empty(consumerError);
            Assert.Equal(0, (await RunJitWorkerAsync(consumer, producer).ConfigureAwait(true)).ExitCode);
            Assert.Equal(0, (await RunCliAsync("link-module", consumer, producer, "-o", image, "--runtime", "fail-fast")
                .ConfigureAwait(true)).ExitCode);
            Assert.Equal(0, await ExecuteAsync(await File.ReadAllBytesAsync(image).ConfigureAwait(true)).ConfigureAwait(true));
        }
        finally
        {
            File.Delete(producer);
            File.Delete(consumer);
            File.Delete(image);
        }
    }

    [Fact]
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
            Assert.Equal(0, (await RunCliAsync("compile-module", "producer", first, "A",
                "--source", second, "-o", artifact).ConfigureAwait(true)).ExitCode);
            var original = await File.ReadAllBytesAsync(artifact).ConfigureAwait(true);
            const string InvalidSource = "// \U00010400\nint32 B() { return Missing; }";
            await File.WriteAllTextAsync(second, InvalidSource).ConfigureAwait(true);
            var (exit, error, output) = await RunCliWithOutputAsync("compile-module", "producer", first, "A",
                "--source", second, "-o", artifact, "--json").ConfigureAwait(true);
            Assert.Equal(1, exit);
            Assert.Empty(error);
            using var document = JsonDocument.Parse(output);
            Assert.Equal(2, document.RootElement.GetProperty("schemaVersion").GetInt32());
            Assert.False(document.RootElement.GetProperty("success").GetBoolean());
            var sources = document.RootElement.GetProperty("sources");
            Assert.Equal(first, sources[0].GetProperty("path").GetString());
            Assert.Equal(second, sources[1].GetProperty("path").GetString());
            Assert.Equal(InvalidSource.Length, sources[1].GetProperty("sourceLength").GetInt32());
            Assert.Equal(SyntaxTree.Parse(InvalidSource).ContentId, sources[1].GetProperty("contentId").GetString());
            var diagnostic = Assert.Single(document.RootElement.GetProperty("diagnostics").EnumerateArray());
            Assert.Equal(1, diagnostic.GetProperty("sourceIndex").GetInt32());
            Assert.Equal(InvalidSource.IndexOf("Missing", StringComparison.Ordinal),
                diagnostic.GetProperty("span").GetProperty("start").GetInt32());
            Assert.Equal(original, await File.ReadAllBytesAsync(artifact).ConfigureAwait(true));
            Assert.Equal(2, (await RunCliAsync("compile-module", "producer", first, "A",
                "--source", first, "-o", artifact).ConfigureAwait(true)).ExitCode);
            Assert.Equal(2, (await RunCliAsync("compile-module", "producer", first, "A",
                "--export", "A", "-o", artifact).ConfigureAwait(true)).ExitCode);
            Assert.Equal(original, await File.ReadAllBytesAsync(artifact).ConfigureAwait(true));
        }
        finally
        {
            File.Delete(first);
            File.Delete(second);
            File.Delete(artifact);
        }
    }

    [Fact]
    public async Task CliRejectsCombinedUtf8SourceBudgetBeforePublishingAsync()
    {
        var prefix = Path.Combine(AppContext.BaseDirectory, $"module-source-limit-{Guid.NewGuid():N}");
        var first = prefix + "-first.terrum";
        var second = prefix + "-second.terrum";
        var artifact = prefix + ".trm";
        try
        {
            var source = "/*" + new string('\u00E9', 1_048_576) + "*/ public int32 A() { return 1; }";
            Assert.True(Encoding.UTF8.GetByteCount(source) <= 4 * 1024 * 1024);
            Assert.True(Encoding.UTF8.GetByteCount(source) * 2 > 4 * 1024 * 1024);
            await File.WriteAllTextAsync(first, source).ConfigureAwait(true);
            await File.WriteAllTextAsync(second, source).ConfigureAwait(true);
            var original = Produce("public int32 A() { return 1; }", "A");
            await File.WriteAllBytesAsync(artifact, original).ConfigureAwait(true);
            var (exit, error, output) = await RunCliWithOutputAsync("compile-module", "producer", first, "A",
                "--source", second, "-o", artifact, "--json").ConfigureAwait(true);
            Assert.Equal(1, exit);
            Assert.Empty(error);
            using var document = JsonDocument.Parse(output);
            Assert.Equal("TRM5001", document.RootElement.GetProperty("diagnostics")[0].GetProperty("code").GetString());
            Assert.Equal(original, await File.ReadAllBytesAsync(artifact).ConfigureAwait(true));
        }
        finally
        {
            File.Delete(first);
            File.Delete(second);
            File.Delete(artifact);
        }
    }

    [Theory]
    [InlineData("--source", 256)]
    [InlineData("--export", 1024)]
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
        Assert.Equal(2, exit);
        Assert.Empty(error);
        using var document = JsonDocument.Parse(output);
        Assert.Equal("TRM2000", document.RootElement.GetProperty("diagnostics")[0].GetProperty("code").GetString());
    }

    [Fact]
    public void MultiSourceReferencesPreserveDiagnosticSnapshotOwnership()
    {
        var reference = ModuleCompiler.CreateReference(Produce("public int32 Value() { return 9; }", "Value"));
        var entry = SyntaxTree.Parse("int32 Main() { return Bad(); }", "entry.terrum");
        var invalid = SyntaxTree.Parse("int32 Bad() { return Value(true); }", "invalid.terrum");
        var compilation = Compilation.Create([entry, invalid], reference);
        Assert.True(compilation.HasErrors);
        Assert.Null(compilation.Program);
        Assert.NotEmpty(compilation.SourceDiagnostics);
        Assert.All(compilation.SourceDiagnostics, located => Assert.Same(invalid, located.SyntaxTree));
        var library = Compilation.CreateLibrary([entry, invalid]);
        Assert.True(library.HasErrors);
        Assert.All(library.SourceDiagnostics, located => Assert.Same(invalid, located.SyntaxTree));
        _ = Assert.Throws<ArgumentException>(() => Compilation.CreateLibrary(Array.Empty<SyntaxTree>()));
        _ = Assert.Throws<ArgumentException>(() => Compilation.Create(Array.Empty<SyntaxTree>(), reference));
    }

    [Fact]
    public async Task ConsumerBindsOnlyImportedDeclarationsAndExecutesValuesAndOutputsAsync()
    {
        const string Library = """
            namespace Values;
            public struct Value {
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
        Assert.All(reference.Functions, static function => {
            Assert.True(function.IsExternal);
            Assert.Empty(function.Body.Statements);
        });
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
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = Assert.IsType<BoundProgram>(compilation.Program);
        _ = Assert.Throws<ArgumentException>(() => SemanticVerifier.Verify(program));
        _ = Assert.Throws<ArgumentException>(() => NativeEmitter.EmitCode(program, NativeRuntimePolicy.FailFast));
        _ = Assert.Throws<ArgumentException>(() => ModuleCompiler.CreateProducer(program, ["Consumer.Main"]));
        var consumer = ModuleCompiler.CreateConsumer(program, producer);
        var linked = ModuleCompiler.Link(consumer, producer);
        Assert.DoesNotContain(linked.Functions, static function => function.IsExternal);
        Assert.Equal(21, await ExecuteAsync(NativeEmitter.Emit(linked, NativeRuntimePolicy.FailFast)).ConfigureAwait(true));
    }

    [Theory]
    [InlineData("int32 Main() { return Private(); }")]
    [InlineData("int32 Main() { return Try(1); }")]
    [InlineData("int32 Main() { Try(out var value); return value; }")]
    [InlineData("int32 Main() { if (!Try(out var value)) { return value; } return 0; }")]
    [InlineData("int32 Main() { return Try(out var value); }")]
    [InlineData("public bool Try(out int32 value) { value = 0; return true; } int32 Main() { return 0; }")]
    public void ImportedDeclarationsEnforceSourceContracts(string source)
    {
        var producer = Produce("public bool Try(out int32 value) { value = Private(); return true; } "
            + "int32 Private() { return 7; }", "Try");
        var compilation = Compilation.Create(SyntaxTree.Parse(source), ModuleCompiler.CreateReference(producer));
        Assert.True(compilation.HasErrors);
        Assert.Null(compilation.Program);
    }

    [Fact]
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
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(Assert.IsType<BoundProgram>(compilation.Program), first);
        var consumerPath = Path.Combine(AppContext.BaseDirectory, $"independent-{Guid.NewGuid():N}.trm");
        var producerPath = Path.ChangeExtension(consumerPath, ".producer.trm");
        try
        {
            await File.WriteAllBytesAsync(consumerPath, consumer).ConfigureAwait(true);
            foreach (var (producer, expected) in new[] { (first, 11), (second, 22) })
            {
                await File.WriteAllBytesAsync(producerPath, producer).ConfigureAwait(true);
                var (exit, error) = await RunJitWorkerAsync(consumerPath, producerPath).ConfigureAwait(true);
                Assert.Equal(expected, exit);
                Assert.Empty(error);
                Assert.Equal(expected, await ExecuteAsync(NativeEmitter.Emit(ModuleCompiler.Link(consumer, producer),
                    NativeRuntimePolicy.FailFast)).ConfigureAwait(true));
                Assert.Equal(consumer, await File.ReadAllBytesAsync(consumerPath).ConfigureAwait(true));
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
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        return ModuleCompiler.CreateProducer(Assert.IsType<BoundProgram>(compilation.Program), exports);
    }

    [Fact]
    public async Task ProducerLibraryNeedsNoSyntheticEntryPointAsync()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("public int32 Helper() { return 29; }"));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var library = Assert.IsType<BoundProgram>(compilation.Program);
        Assert.Equal(-1, library.EntryPoint);
        SemanticVerifier.VerifyLibrary(library);
        _ = Assert.Throws<ArgumentException>(() => NativeEmitter.Emit(library, NativeRuntimePolicy.FailFast));
        var producer = ModuleCompiler.CreateProducer(library, ["Helper"]);
        var reference = Compile("public int32 Helper() { return 0; } int32 Main() { return Helper(); }");
        var consumer = ModuleCompiler.CreateConsumer(reference, producer);
        Assert.Equal(29, await ExecuteAsync(NativeEmitter.Emit(
            ModuleCompiler.Link(consumer, producer), NativeRuntimePolicy.FailFast)).ConfigureAwait(true));
    }

    [Theory]
    [InlineData("")]
    [InlineData("public int32 Helper() { return missing; }")]
    [InlineData("public bool Helper(out int32 value) { return true; }")]
    [InlineData("struct S { public int32 X; public S() { } }")]
    public void InvalidLibrariesProduceSourceDiagnostics(string source)
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source));
        Assert.True(compilation.HasErrors);
        Assert.Null(compilation.Program);
    }

    [Fact]
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
            Assert.Equal(11, await ExecuteAsync(originalImage).ConfigureAwait(true));

            await File.WriteAllBytesAsync(producerFile, producerTwo).ConfigureAwait(true);
            var replacement = ModuleCompiler.Link(await File.ReadAllBytesAsync(consumerFile).ConfigureAwait(true),
                await File.ReadAllBytesAsync(producerFile).ConfigureAwait(true));
            Assert.Equal(22, await ExecuteAsync(NativeEmitter.Emit(replacement, NativeRuntimePolicy.FailFast)).ConfigureAwait(true));
            Assert.Equal(11, await ExecuteAsync(originalImage).ConfigureAwait(true));
            Assert.Equal(originalImageHash, SHA256.HashData(originalImage));
        }
        finally
        {
            File.Delete(consumerFile);
            File.Delete(producerFile);
        }
    }

    [Fact]
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
            Assert.Equal(2, missingPolicyExit);
            Assert.Contains("TRM2000", missingPolicyError, StringComparison.Ordinal);
            var (firstExit, firstError) = await RunJitWorkerAsync(consumerPath, producerPath).ConfigureAwait(true);
            Assert.Equal(11, firstExit);
            Assert.Empty(firstError);
            var (jsonExit, jsonError, jsonOutput) = await RunCliWithOutputAsync(
                "run-module", consumerPath, producerPath, "--runtime", "fail-fast", "--json").ConfigureAwait(true);
            Assert.Equal(11, jsonExit);
            Assert.Empty(jsonError);
            Assert.Empty(jsonOutput);

            await File.WriteAllBytesAsync(producerPath, ModuleCompiler.CreateProducer(replacement, ["Helper"])).ConfigureAwait(true);
            var (replacementExit, replacementError) = await RunJitWorkerAsync(consumerPath, producerPath).ConfigureAwait(true);
            Assert.Equal(22, replacementExit);
            Assert.Empty(replacementError);
            Assert.Equal(originalConsumerBytes, await File.ReadAllBytesAsync(consumerPath).ConfigureAwait(true));
            Assert.Equal(originalConsumerHash, SHA256.HashData(await File.ReadAllBytesAsync(consumerPath).ConfigureAwait(true)));

            var incompatible = Compile("int32 Main() { return 0; } public bool Helper() { return true; }");
            await File.WriteAllBytesAsync(producerPath, ModuleCompiler.CreateProducer(incompatible, ["Helper"])).ConfigureAwait(true);
            var (invalidExit, invalidError) = await RunJitWorkerAsync(consumerPath, producerPath).ConfigureAwait(true);
            Assert.Equal(1, invalidExit);
            Assert.Contains("TRM5002", invalidError, StringComparison.Ordinal);
            Assert.Equal(originalConsumerHash, SHA256.HashData(await File.ReadAllBytesAsync(consumerPath).ConfigureAwait(true)));
        }
        finally
        {
            File.Delete(consumerPath);
            File.Delete(producerPath);
        }
    }

    [Fact]
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
            Assert.Equal(0, (await RunCliAsync("compile-module", "producer", referenceSource, "Helper", "-o", original)
                .ConfigureAwait(true)).ExitCode);
            Assert.Equal(0, (await RunCliAsync("compile-module", "consumer", consumerSource, original, "-o", consumer)
                .ConfigureAwait(true)).ExitCode);
            var (compileExit, compileError, compileOutput) = await RunCliWithOutputAsync(
                "compile-module", "producer", replacementSource, "Helper", "-o", replacement, "--json").ConfigureAwait(true);
            Assert.Equal(0, compileExit);
            Assert.Empty(compileError);
            using (var document = JsonDocument.Parse(compileOutput))
            {
                Assert.Equal(1, document.RootElement.GetProperty("schemaVersion").GetInt32());
                Assert.True(document.RootElement.GetProperty("success").GetBoolean());
                Assert.Equal("semantic-module", document.RootElement.GetProperty("artifact").GetProperty("kind").GetString());
                Assert.Equal(Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(replacement).ConfigureAwait(true))),
                    document.RootElement.GetProperty("artifact").GetProperty("sha256").GetString());
            }
            var consumerBytes = await File.ReadAllBytesAsync(consumer).ConfigureAwait(true);
            await File.WriteAllTextAsync(consumerSource, "int32 Main() { return Helper(true); }").ConfigureAwait(true);
            Assert.Equal(1, (await RunCliAsync("compile-module", "consumer", consumerSource, original, "-o", consumer)
                .ConfigureAwait(true)).ExitCode);
            Assert.Equal(consumerBytes, await File.ReadAllBytesAsync(consumer).ConfigureAwait(true));
            File.Delete(referenceSource);
            File.Delete(consumerSource);
            File.Delete(replacementSource);
            Assert.Equal(11, (await RunJitWorkerAsync(consumer, original).ConfigureAwait(true)).ExitCode);
            Assert.Equal(22, (await RunJitWorkerAsync(consumer, replacement).ConfigureAwait(true)).ExitCode);
            var (usageExit, usageError, usageOutput) = await RunCliWithOutputAsync(
                "link-module", consumer, original, "-o", linked, "--json").ConfigureAwait(true);
            Assert.Equal(2, usageExit);
            Assert.Empty(usageError);
            using (var document = JsonDocument.Parse(usageOutput))
            {
                Assert.Equal("TRM2000", document.RootElement.GetProperty("diagnostics")[0].GetProperty("code").GetString());
            }
            Assert.False(File.Exists(linked));
            Assert.Equal(0, (await RunCliAsync("link-module", consumer, original, "-o", linked, "--runtime", "fail-fast")
                .ConfigureAwait(true)).ExitCode);
            var linkedBytes = await File.ReadAllBytesAsync(linked).ConfigureAwait(true);
            Assert.Equal(11, await ExecuteAsync(linkedBytes).ConfigureAwait(true));
            var incompatible = ModuleCompiler.CreateProducer(
                Compile("int32 Main() { return 0; } public bool Helper() { return true; }"), ["Helper"]);
            await File.WriteAllBytesAsync(replacement, incompatible).ConfigureAwait(true);
            var (failedExit, failedError, failedOutput) = await RunCliWithOutputAsync(
                "link-module", consumer, replacement, "-o", linked, "--runtime", "fail-fast", "--json").ConfigureAwait(true);
            Assert.Equal(1, failedExit);
            Assert.Empty(failedError);
            using (var document = JsonDocument.Parse(failedOutput))
            {
                Assert.False(document.RootElement.GetProperty("success").GetBoolean());
                Assert.Equal("TRM5002", document.RootElement.GetProperty("diagnostics")[0].GetProperty("code").GetString());
            }
            Assert.Equal(linkedBytes, await File.ReadAllBytesAsync(linked).ConfigureAwait(true));
            Assert.Equal(consumerBytes, await File.ReadAllBytesAsync(consumer).ConfigureAwait(true));
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

    [Fact]
    public async Task CompileModuleReportsSourceDiagnosticsAsSchemaOneJsonAsync()
    {
        var source = Path.Combine(AppContext.BaseDirectory, $"module-source-{Guid.NewGuid():N}.terrum");
        var artifact = Path.ChangeExtension(source, ".trm");
        try
        {
            await File.WriteAllTextAsync(source, "int32 Main() { return Missing; }").ConfigureAwait(true);
            var (exitCode, error, output) = await RunCliWithOutputAsync(
                "compile-module", "producer", source, "Helper", "-o", artifact, "--json").ConfigureAwait(true);
            Assert.Equal(1, exitCode);
            Assert.Empty(error);
            using var document = JsonDocument.Parse(output);
            Assert.Equal(1, document.RootElement.GetProperty("schemaVersion").GetInt32());
            Assert.Equal("compile-module", document.RootElement.GetProperty("command").GetString());
            Assert.False(document.RootElement.GetProperty("success").GetBoolean());
            Assert.True(document.RootElement.GetProperty("diagnostics").GetArrayLength() > 0);
            Assert.False(File.Exists(artifact));
        }
        finally
        {
            File.Delete(source);
            File.Delete(artifact);
        }
    }

    [Theory]
    [InlineData("public bool Helper() { return true; }")]
    [InlineData("public int32 Helper(int32 value) { return value; }")]
    [InlineData("public void Helper() { }")]
    public void RejectsChangedImportedSignatureBeforeEmission(string changed)
    {
        var first = Compile("int32 Main() { return Helper(); } public int32 Helper() { return 11; }");
        var initial = ModuleCompiler.CreateProducer(first, ["Helper"]);
        var consumer = ModuleCompiler.CreateConsumer(first, initial);
        var replacement = Compile("int32 Main() { return 0; } " + changed);
        var producer = ModuleCompiler.CreateProducer(replacement, ["Helper"]);
        _ = Assert.Throws<ModuleContractException>(() => ModuleCompiler.Link(consumer, producer));
    }

    [Fact]
    public void RejectsChangedConditionalOutputContract()
    {
        var initial = Compile("int32 Main() { if (Try(out var value)) { return value; } return 0; } "
            + "public bool Try(out int32 value) { value = 11; return true; }");
        var producer = ModuleCompiler.CreateProducer(initial, ["Try"]);
        var consumer = ModuleCompiler.CreateConsumer(initial, producer);
        var changed = Compile("int32 Main() { return 0; } public bool Try(int32 value) { return true; }");
        _ = Assert.Throws<ModuleContractException>(() =>
            ModuleCompiler.Link(consumer, ModuleCompiler.CreateProducer(changed, ["Try"])));
    }

    [Fact]
    public async Task ConstructorContractsUseExplicitArgumentsRatherThanReceiverSlotsAsync()
    {
        const string Consumer = "public struct Box { public int32 Value; "
            + "public Box(int32 value) { this.Value = value; } } "
            + "int32 Main() { Box box = new Box(11); return box.Value; }";
        var first = Compile(Consumer);
        var consumer = ModuleCompiler.CreateConsumer(first, ModuleCompiler.CreateProducer(first, ["Box.#ctor"]));
        var compatible = Compile("public struct Box { public int32 Value; "
            + "public Box(int32 value) { this.Value = 22; } } int32 Main() { return 0; }");
        var linked = ModuleCompiler.Link(consumer, ModuleCompiler.CreateProducer(compatible, ["Box.#ctor"]));
        Assert.Equal(22, await ExecuteAsync(NativeEmitter.Emit(linked, NativeRuntimePolicy.FailFast)).ConfigureAwait(true));

        var changed = Compile("public struct Box { public int32 Value; "
            + "public Box(uint32 value) { this.Value = int32(value); } } int32 Main() { return 0; }");
        _ = Assert.Throws<ModuleContractException>(() =>
            ModuleCompiler.Link(consumer, ModuleCompiler.CreateProducer(changed, ["Box.#ctor"])));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(2)]
    public void RejectsMalformedConstructorParameterCountBeforeContractParsing(int parameterCount)
    {
        var initial = Compile("public struct Box { public int32 Value; "
            + "public Box(int32 value) { this.Value = value; } } "
            + "int32 Main() { Box box = new Box(11); return box.Value; }");
        var producer = ModuleCompiler.CreateProducer(initial, ["Box.#ctor"]);
        var consumer = ModuleCompiler.CreateConsumer(initial, producer);
        var modified = (byte[])producer.Clone();
        var name = Encoding.UTF8.GetBytes("Box.#ctor");
        var functionName = modified.AsSpan().LastIndexOf(name);
        Assert.True(functionName >= sizeof(int));
        using var reader = new BinaryReader(new MemoryStream(modified));
        reader.BaseStream.Position = functionName + name.Length;
        _ = reader.ReadInt32();
        Assert.True(reader.ReadBoolean());
        for (var flag = 0; flag < 7; flag++)
        {
            _ = reader.ReadBoolean();
        }
        Assert.False(reader.ReadBoolean());
        Assert.Equal((byte)BoundIntrinsic.None, reader.ReadByte());
        Assert.False(reader.ReadBoolean());
        _ = reader.ReadInt32();
        Assert.True(reader.ReadBoolean());
        var ownerLength = reader.ReadInt32();
        Assert.Equal("Box", Encoding.UTF8.GetString(reader.ReadBytes(ownerLength)));
        var parameterOffset = (int)reader.BaseStream.Position;
        Assert.Equal(1, reader.ReadInt32());
        BinaryPrimitives.WriteInt32LittleEndian(modified.AsSpan(parameterOffset, sizeof(int)), parameterCount);
        _ = Assert.Throws<ModuleFormatException>(() => ModuleCompiler.Link(consumer, modified));
    }

    [Fact]
    public async Task PersistedConsumerRejectsChangedOutputLayoutAndFieldAccessInWorkerAsync()
    {
        const string PairSource = "public struct Pair { public int32 A; public int32 B; "
            + "public Pair() { this.A = 11; this.B = 1; } "
            + "public static int32 Value() { Pair p = new Pair(); return p.A; } } "
            + "int32 Main() { return Pair.Value(); }";
        const string PairReplacement = "public Pair() { this.A = 11; this.B = 1; } "
            + "public static int32 Value() { Pair p = new Pair(); return p.A; } } int32 Main() { return 0; }";
        var cases = new (string Initial, string Export, string Changed)[] {
            ("int32 Main() { if (Try(out var value)) { return value; } return 0; } "
                + "public bool Try(out int32 value) { value = 11; return true; }",
                "Try", "int32 Main() { return 0; } public bool Try(int32 value) { return true; }"),
            (PairSource, "Pair.Value", "public struct Pair { public int32 B; public int32 A; " + PairReplacement),
            (PairSource, "Pair.Value", "public struct Pair { public int32 A; private int32 B; " + PairReplacement),
            (PairSource, "Pair.Value", "public struct Pair { public int32 A; internal int32 B; " + PairReplacement),
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
                Assert.Equal(11, (await RunJitWorkerAsync(consumerPath, producerPath).ConfigureAwait(true)).ExitCode);
                await File.WriteAllBytesAsync(producerPath, replacement).ConfigureAwait(true);
                var (exitCode, error) = await RunJitWorkerAsync(consumerPath, producerPath).ConfigureAwait(true);
                Assert.Equal(1, exitCode);
                Assert.Contains("TRM5002", error, StringComparison.Ordinal);
                Assert.Equal(consumerBytes, await File.ReadAllBytesAsync(consumerPath).ConfigureAwait(true));
            }
            finally
            {
                File.Delete(consumerPath);
                File.Delete(producerPath);
            }
        }
    }

    [Fact]
    public void PrivateFunctionCannotBecomeAnImportedGrant()
    {
        var program = Compile("public struct Secret { private int32 Value; private Secret() { this.Value = 7; } } "
            + "int32 Main() { return 0; }");
        _ = Assert.Throws<ModuleContractException>(() => ModuleCompiler.CreateProducer(program, ["Secret.#ctor"]));
    }

    [Fact]
    public void InternalFunctionCannotBecomeAnImportedGrantFromCombinedCompilation()
    {
        var program = Compile("int32 Main() { return Helper(); } "
            + "internal int32 Helper() { return 11; }");
        Assert.Contains(program.Functions, static function => function.Name == "Helper" && function.IsInternal);
        _ = Assert.Throws<ModuleContractException>(() => ModuleCompiler.CreateProducer(program, ["Helper"]));

        var implicitInternal = Compile("int32 Main() { return Helper(); } int32 Helper() { return 11; }");
        Assert.Contains(implicitInternal.Functions, static function => function.Name == "Helper" && function.IsInternal);
        _ = Assert.Throws<ModuleContractException>(() => ModuleCompiler.CreateProducer(implicitInternal, ["Helper"]));
    }

    [Fact]
    public void RejectsInaccessibleImportedTypeAndValueFieldSurface()
    {
        var internalType = Compile("internal struct Hidden { public int32 Value; "
            + "public static int32 Get() { return 11; } } int32 Main() { return 0; }");
        _ = Assert.Throws<ModuleContractException>(() => ModuleCompiler.CreateProducer(internalType, ["Hidden.Get"]));
        var implicitInternalType = Compile("struct Hidden { public int32 Value; "
            + "public static int32 Get() { return 11; } } int32 Main() { return 0; }");
        _ = Assert.Throws<ModuleContractException>(() => ModuleCompiler.CreateProducer(implicitInternalType, ["Hidden.Get"]));

        var privateField = Compile("public struct Pair { public int32 A; private int32 B; "
            + "public Pair() { this.A = 11; this.B = 1; } } "
            + "int32 Main() { return 0; } public Pair Make() { return new Pair(); }");
        _ = Assert.Throws<ModuleContractException>(() => ModuleCompiler.CreateProducer(privateField, ["Make"]));

        var staticField = Compilation.Create(SyntaxTree.Parse("struct S { static int32 Value; } int32 Main() { return 0; }"));
        Assert.Contains(staticField.Diagnostics, static diagnostic => diagnostic.Code == "TRM3001");
    }

    [Fact]
    public void HostBufferCapabilityCannotSilentlyEnterModuleArtifact()
    {
        var program = Compile("int32 Main() { return 0; } public int32 Helper() { return 11; }")
            with { Buffers = [new HostBuffer("Input", PrimitiveType.Int32, [1], false)] };
        _ = Assert.Throws<ModuleFormatException>(() => ModuleCompiler.CreateProducer(program, ["Helper"]));
    }

    [Fact]
    public async Task PrivateProducerDependencyIsNotPinnedOrGrantedToConsumerAsync()
    {
        const string Main = "int32 Main() { return Secret.Get(); }";
        static string Producer(int value)
        {
            return "public struct Secret { private int32 Value; "
            + "private Secret() { this.Value = " + value.ToString(System.Globalization.CultureInfo.InvariantCulture)
            + "; } public static int32 Get() { Secret secret = new Secret(); return secret.Value; } }";
        }

        var initial = Compile(Producer(7) + Main);
        var reference = ModuleCompiler.CreateProducer(initial, ["Secret.Get"]);
        var consumer = ModuleCompiler.CreateConsumer(initial, reference);
        Assert.DoesNotContain("#ctor", Encoding.UTF8.GetString(consumer), StringComparison.Ordinal);
        var changed = Compile(Producer(9) + "int32 Main() { return 0; }");
        var replacement = ModuleCompiler.CreateProducer(changed, ["Secret.Get"]);
        var linked = ModuleCompiler.Link(consumer, replacement);
        Assert.Equal(9, await ExecuteAsync(NativeEmitter.Emit(linked, NativeRuntimePolicy.FailFast)).ConfigureAwait(true));
    }

    [Fact]
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
        Assert.DoesNotContain("Helper", Encoding.UTF8.GetString(consumer), StringComparison.Ordinal);

        var second = Compile("int32 Main() { return 0; } " + ProducerTwo);
        var replacement = ModuleCompiler.CreateProducer(second, ["API"]);
        var linked = ModuleCompiler.Link(consumer, replacement);
        Assert.Equal(22, await ExecuteAsync(NativeEmitter.Emit(linked, NativeRuntimePolicy.FailFast)).ConfigureAwait(true));
    }

    [Fact]
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
        Assert.Contains(consumer.Functions, static function => function.Intrinsic == BoundIntrinsic.BufferClear);

        var exception = Assert.Throws<ModuleContractException>(() =>
            ModuleCompiler.CreateConsumer(consumer, producer));
        Assert.Equal("Consumer cannot call an unexported producer implementation.", exception.Message);
    }

    [Fact]
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
        Assert.Contains(consumer.Functions, static function => function.Intrinsic == BoundIntrinsic.BufferClear);

        var artifact = ModuleCompiler.CreateConsumer(consumer, producer);

        Assert.DoesNotContain("OwnedInt32Buffer.Clear", Encoding.UTF8.GetString(artifact), StringComparison.Ordinal);
    }

    [Fact]
    public void TamperedProducerCannotExportPrivateDependency()
    {
        const string Source = "public struct Secret { private int32 Value; "
            + "private Secret() { this.Value = 7; } "
            + "public static int32 Get() { Secret secret = new Secret(); return secret.Value; } } "
            + "int32 Main() { return Secret.Get(); }";
        var program = Compile(Source);
        var producer = ModuleCompiler.CreateProducer(program, ["Secret.Get"]);
        var consumer = ModuleCompiler.CreateConsumer(program, producer);
        var modified = (byte[])producer.Clone();
        var privateName = Encoding.UTF8.GetBytes("Secret.#ctor");
        var offset = modified.AsSpan().IndexOf(privateName);
        Assert.True(offset >= sizeof(int), "Private function record is missing.");
        Assert.Equal(privateName.Length, BinaryPrimitives.ReadInt32LittleEndian(modified.AsSpan(offset - sizeof(int), sizeof(int))));
        var contractLengthOffset = offset + privateName.Length;
        var contractLength = BinaryPrimitives.ReadInt32LittleEndian(modified.AsSpan(contractLengthOffset, sizeof(int)));
        var exportFlagOffset = contractLengthOffset + sizeof(int) + contractLength;
        Assert.Equal(0, modified[exportFlagOffset]);
        modified[exportFlagOffset] = 1;
        _ = Assert.Throws<ModuleFormatException>(() => ModuleCompiler.Link(consumer, modified));
    }

    [Fact]
    public void SameDeclaringTypeDoesNotGrantConsumerAccessToProducerPrivateBody()
    {
        const string Source = "public struct Secret { private int32 Value; "
            + "private Secret() { this.Value = 7; } "
            + "public static int32 Get() { Secret s = new Secret(); return s.Value; } "
            + "public static int32 Consumer() { Secret s = new Secret(); return s.Value; } } "
            + "int32 Main() { return Secret.Consumer(); }";
        var program = Compile(Source);
        var producer = ModuleCompiler.CreateProducer(program, ["Secret.Get"]);
        Assert.Contains(program.Functions, static function => function.Name == "Secret.#ctor" && function.IsPrivate);
        Assert.Contains("Secret.#ctor", Encoding.UTF8.GetString(producer), StringComparison.Ordinal);
        Assert.DoesNotContain("Secret.Consumer", Encoding.UTF8.GetString(producer), StringComparison.Ordinal);
        foreach (var function in program.Functions)
        {
            if (function.Name == "Secret.Consumer")
            {
                var local = Assert.IsType<BoundLocal>(function.Body.Statements[0]);
                var call = Assert.IsType<BoundCall>(local.Initializer);
                Assert.Equal("Secret.#ctor", program.Functions[call.Function].Name);
            }
        }
        _ = Assert.Throws<ModuleContractException>(() => ModuleCompiler.CreateConsumer(program, producer));
    }

    [Fact]
    public void ForgedConsumerCallCannotReachProducerPrivateSymbol()
    {
        const string Source = "public struct S { public int32 Field; "
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
        Assert.True(callOffset >= 0);
        privateCall.AsSpan().CopyTo(forged.AsSpan(callOffset, privateCall.Length));
        _ = Assert.Throws<ModuleContractException>(() => ModuleCompiler.Link(forged, producer));
    }

    [Theory]
    [InlineData("public int32 B; public int32 A;")]
    [InlineData("public int32 A; private int32 B;")]
    [InlineData("public int32 A; internal int32 B;")]
    public void RejectsChangedFieldOrderOrAccessBeforeExecutingConsumer(string fields)
    {
        var initial = Compile("public struct Pair { public int32 A; public int32 B; "
            + "public Pair() { this.A = 11; this.B = 1; } } "
            + "int32 Main() { return Value(); } public int32 Value() { Pair p = new Pair(); return p.A; }");
        var original = ModuleCompiler.CreateProducer(initial, ["Value", "Pair.#ctor"]);
        var consumer = ModuleCompiler.CreateConsumer(initial, original);
        var changed = Compile("public struct Pair { " + fields + " "
            + "public Pair() { this.A = 11; this.B = 1; } } "
            + "int32 Main() { return 0; } public int32 Value() { Pair p = new Pair(); return p.A; }");
        _ = Assert.Throws<ModuleContractException>(() =>
            ModuleCompiler.Link(consumer, ModuleCompiler.CreateProducer(changed, ["Value", "Pair.#ctor"])));
    }

    [Fact]
    public async Task RemapsReorderedTypeIdentifiersWithoutRebindingConsumerAsync()
    {
        const string A = "public struct A { public int32 Value; public A() { this.Value = 5; } }";
        const string B = "struct B { public int32 Number; public B() { this.Number = 2; } }";
        const string Functions = "int32 Main() { return Helper(); } "
            + "public int32 Helper() { A value = new A(); return value.Value; }";
        var original = Compile(A + B + Functions);
        var consumer = ModuleCompiler.CreateConsumer(original, ModuleCompiler.CreateProducer(original, ["Helper", "A.#ctor"]));
        var reordered = Compile(B + A + Functions);
        var replacement = ModuleCompiler.CreateProducer(reordered, ["Helper", "A.#ctor"]);
        var linked = ModuleCompiler.Link(consumer, replacement);
        Assert.Equal(5, await ExecuteAsync(NativeEmitter.Emit(linked, NativeRuntimePolicy.FailFast)).ConfigureAwait(true));
    }

    [Fact]
    public void RejectsTamperedVersionBoundsAndUnknownNodes()
    {
        var initial = Compile("int32 Main() { return Helper(); } public int32 Helper() { return 11; }");
        var producer = ModuleCompiler.CreateProducer(initial, ["Helper"]);
        var consumer = ModuleCompiler.CreateConsumer(initial, producer);
        var version = (byte[])consumer.Clone();
        for (var unsupported = 0; unsupported < 10; unsupported++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(version.AsSpan(4, 4), unsupported);
            _ = Assert.Throws<ModuleFormatException>(() => ModuleCompiler.Link(version, producer));
        }
        BinaryPrimitives.WriteInt32LittleEndian(version.AsSpan(4, 4), int.MaxValue);
        _ = Assert.Throws<ModuleFormatException>(() => ModuleCompiler.Link(version, producer));
        _ = Assert.Throws<ModuleFormatException>(() => ModuleCompiler.Link(consumer[..^1], producer));
        _ = Assert.Throws<ModuleFormatException>(() => ModuleCompiler.Link(new byte[(4 * 1024 * 1024) + 1], producer));
        _ = Assert.Throws<ModuleFormatException>(() => ModuleCompiler.Link([.. consumer, 0], producer));
    }

    [Fact]
    public void RejectsCompositeContractsInRetiredSemanticRevision()
    {
        var program = Compile("""
            public unique struct Child {
                public int32 Value;
                public Child() { Value = 42; }
            }
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
        _ = Assert.Throws<ModuleFormatException>(() => ModuleCompiler.CreateReference(downgradedProducer));
        BinaryPrimitives.WriteInt32LittleEndian(consumer.AsSpan(4, 4), 13);
        _ = Assert.Throws<ModuleFormatException>(() => ModuleCompiler.Link(consumer, producer));
    }

    private static BoundProgram Compile(string source)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        return Assert.IsType<BoundProgram>(compilation.Program);
    }

    private static async Task<int> ExecuteAsync(byte[] image)
    {
        Assert.True(OperatingSystem.IsWindows() && RuntimeInformation.ProcessArchitecture == Architecture.X64);
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
            File.Delete(path);
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
        Assert.True(File.Exists(cli), $"CLI was not built: {cli}");
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
