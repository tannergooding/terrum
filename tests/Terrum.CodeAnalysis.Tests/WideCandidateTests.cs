// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed class WideCandidateTests
{
    [Theory]
    [InlineData(NativeRuntimePolicy.FailFast)]
    [InlineData(NativeRuntimePolicy.CheckedExceptions)]
    public async Task WideCompilerCandidatesExecuteFromIndependentModulesAsync(NativeRuntimePolicy policy)
    {
        var producer = CreateCandidateLibrary((BoundRuntimePolicy)policy);
        var reference = ModuleCompiler.CreateReference(producer);
        var root = RepositoryRoot();
        var tests = ReadTree(Path.Combine(root, "self-host", "WideIntegerTests.terrum"));
        var entry = ReadTree(Path.Combine(root, "samples", "native", "modules", "wide-entry.terrum"));
        var compilation = Compilation.Create([tests, entry], reference, (BoundRuntimePolicy)policy);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(Assert.IsType<BoundProgram>(compilation.Program), producer);
        Assert.Equal(0, NativeJit.Execute(ModuleCompiler.Link(consumer, producer), policy));
        await ExecuteBothAsync(consumer, producer, 0, policy).ConfigureAwait(true);
    }

    [Theory]
    [InlineData(NativeRuntimePolicy.FailFast)]
    [InlineData(NativeRuntimePolicy.CheckedExceptions)]
    public async Task RadixLengthsPublishOnlyForValidBasesAcrossModulesAsync(NativeRuntimePolicy policy)
    {
        var producer = CreateNumericsLibrary("IntegerFormatting.terrum", policy,
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
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(Assert.IsType<BoundProgram>(compilation.Program), producer);
        Assert.Equal(42, NativeJit.Execute(ModuleCompiler.Link(consumer, producer), policy));
        await ExecuteBothAsync(consumer, producer, 42, policy).ConfigureAwait(true);
    }

    [Fact]
    public void InvalidRadixLengthCannotBeReadAfterFailure()
    {
        var producer = CreateNumericsLibrary("IntegerFormatting.terrum", NativeRuntimePolicy.FailFast,
            "Terrum.CodeAnalysis.Numerics64.TryGetUInt64BaseLength");
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            using Terrum.CodeAnalysis.Numerics64;
            int32 Main() {
                bool valid = TryGetUInt64BaseLength(0, 1, init var length);
                return length;
            }
            """), ModuleCompiler.CreateReference(producer));
        Assert.Contains(compilation.Diagnostics, static diagnostic => diagnostic.Code == "TRM3016");
        Assert.Null(compilation.Program);
    }

    [Theory]
    [InlineData(NativeRuntimePolicy.FailFast, "Int32")]
    [InlineData(NativeRuntimePolicy.FailFast, "Int64")]
    [InlineData(NativeRuntimePolicy.CheckedExceptions, "Int32")]
    [InlineData(NativeRuntimePolicy.CheckedExceptions, "Int64")]
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
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(Assert.IsType<BoundProgram>(compilation.Program), producer);
        Assert.Equal(42, NativeJit.Execute(ModuleCompiler.Link(consumer, producer), policy));
        await ExecuteBothAsync(consumer, producer, 42, policy).ConfigureAwait(true);
    }

    [Theory]
    [InlineData("Int32")]
    [InlineData("Int64")]
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
        Assert.Contains(compilation.Diagnostics, static diagnostic => diagnostic.Code == "TRM3016");
        Assert.Null(compilation.Program);
    }

    [Theory]
    [InlineData(NativeRuntimePolicy.FailFast, "Int32")]
    [InlineData(NativeRuntimePolicy.FailFast, "Int64")]
    [InlineData(NativeRuntimePolicy.CheckedExceptions, "Int32")]
    [InlineData(NativeRuntimePolicy.CheckedExceptions, "Int64")]
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
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(Assert.IsType<BoundProgram>(compilation.Program), producer);
        Assert.Equal(42, NativeJit.Execute(ModuleCompiler.Link(consumer, producer), policy));
        await ExecuteBothAsync(consumer, producer, 42, policy).ConfigureAwait(true);
    }

    [Theory]
    [InlineData(NativeRuntimePolicy.FailFast)]
    [InlineData(NativeRuntimePolicy.CheckedExceptions)]
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
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(Assert.IsType<BoundProgram>(compilation.Program), producer);
        Assert.Equal(42, NativeJit.Execute(ModuleCompiler.Link(consumer, producer), policy));
        await ExecuteBothAsync(consumer, producer, 42, policy).ConfigureAwait(true);
    }

    [Theory]
    [InlineData(NativeRuntimePolicy.FailFast)]
    [InlineData(NativeRuntimePolicy.CheckedExceptions)]
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
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(Assert.IsType<BoundProgram>(compilation.Program), producer);
        Assert.Equal(42, NativeJit.Execute(ModuleCompiler.Link(consumer, producer), policy));
        await ExecuteBothAsync(consumer, producer, 42, policy).ConfigureAwait(true);
    }

    [Theory]
    [InlineData(NativeRuntimePolicy.FailFast)]
    [InlineData(NativeRuntimePolicy.CheckedExceptions)]
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
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(Assert.IsType<BoundProgram>(compilation.Program), producer);
        Assert.Equal(42, NativeJit.Execute(ModuleCompiler.Link(consumer, producer), policy));
        await ExecuteBothAsync(consumer, producer, 42, policy).ConfigureAwait(true);
    }

    [Theory]
    [InlineData(NativeRuntimePolicy.FailFast, "Int32")]
    [InlineData(NativeRuntimePolicy.FailFast, "UInt32")]
    [InlineData(NativeRuntimePolicy.FailFast, "Int64")]
    [InlineData(NativeRuntimePolicy.FailFast, "UInt64")]
    [InlineData(NativeRuntimePolicy.CheckedExceptions, "Int32")]
    [InlineData(NativeRuntimePolicy.CheckedExceptions, "UInt32")]
    [InlineData(NativeRuntimePolicy.CheckedExceptions, "Int64")]
    [InlineData(NativeRuntimePolicy.CheckedExceptions, "UInt64")]
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
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(Assert.IsType<BoundProgram>(compilation.Program), producer);
        Assert.Equal(42, NativeJit.Execute(ModuleCompiler.Link(consumer, producer), policy));
        await ExecuteBothAsync(consumer, producer, 42, policy).ConfigureAwait(true);
    }

    [Theory]
    [InlineData(NativeRuntimePolicy.FailFast)]
    [InlineData(NativeRuntimePolicy.CheckedExceptions)]
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
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(Assert.IsType<BoundProgram>(compilation.Program), producer);
        Assert.Equal(42, NativeJit.Execute(ModuleCompiler.Link(consumer, producer), policy));
        await ExecuteBothAsync(consumer, producer, 42, policy).ConfigureAwait(true);
    }

    [Theory]
    [InlineData(NativeRuntimePolicy.FailFast)]
    [InlineData(NativeRuntimePolicy.CheckedExceptions)]
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
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(Assert.IsType<BoundProgram>(compilation.Program), producer);
        Assert.Equal(42, NativeJit.Execute(ModuleCompiler.Link(consumer, producer), policy));
        await ExecuteBothAsync(consumer, producer, 42, policy).ConfigureAwait(true);
    }

    [Fact]
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
        Assert.Contains(compilation.Diagnostics, static diagnostic => diagnostic.Code == "TRM3016");
        Assert.Null(compilation.Program);
    }

    [Theory]
    [InlineData(NativeRuntimePolicy.FailFast)]
    [InlineData(NativeRuntimePolicy.CheckedExceptions)]
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
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(Assert.IsType<BoundProgram>(compilation.Program), producer);
        Assert.Equal(42, NativeJit.Execute(ModuleCompiler.Link(consumer, producer), policy));
        await ExecuteBothAsync(consumer, producer, 42, policy).ConfigureAwait(true);
    }

    [Theory]
    [InlineData(NativeRuntimePolicy.FailFast)]
    [InlineData(NativeRuntimePolicy.CheckedExceptions)]
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
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(Assert.IsType<BoundProgram>(compilation.Program), producer);
        Assert.Equal(42, NativeJit.Execute(ModuleCompiler.Link(consumer, producer), policy));
        await ExecuteBothAsync(consumer, producer, 42, policy).ConfigureAwait(true);
    }

    [Theory]
    [InlineData(NativeRuntimePolicy.FailFast)]
    [InlineData(NativeRuntimePolicy.CheckedExceptions)]
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
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(Assert.IsType<BoundProgram>(compilation.Program), producer);
        Assert.Equal(42, NativeJit.Execute(ModuleCompiler.Link(consumer, producer), policy));
        await ExecuteBothAsync(consumer, producer, 42, policy).ConfigureAwait(true);
    }

    [Theory]
    [InlineData(NativeRuntimePolicy.FailFast)]
    [InlineData(NativeRuntimePolicy.CheckedExceptions)]
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
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(Assert.IsType<BoundProgram>(compilation.Program), producer);
        Assert.Equal(42, NativeJit.Execute(ModuleCompiler.Link(consumer, producer), policy));
        await ExecuteBothAsync(consumer, producer, 42, policy).ConfigureAwait(true);
    }

    [Theory]
    [InlineData("TryAddInt64(int64.MaxValue, 1, init var value); return int32(value);")]
    [InlineData("if (!TryAddInt64(int64.MaxValue, 1, init var value)) { return int32(value); } return 0;")]
    [InlineData("uint32 value = 1; require TryAddInt64(value, 2, init var sum); return int32(sum);")]
    [InlineData("TryAccumulateDecimal(1, 10, uint64.MaxValue, init var value); return int32(value);")]
    [InlineData("TryAccumulateBase(1, 2, 2, uint64.MaxValue, init var value); return int32(value);")]
    public void WideCandidateContractsRejectInvalidUse(string body)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(
            "using Terrum.CodeAnalysis.Numerics64; int32 Main() { " + body + " }"),
            ModuleCompiler.CreateReference(CreateCandidateLibrary()));
        Assert.True(compilation.HasErrors);
        Assert.Null(compilation.Program);
    }

    [Fact]
    public async Task WideModuleContractsPermitImplementationReplacementButNotSignednessChangesAsync()
    {
        var original = Compilation.CreateLibrary(SyntaxTree.Parse("public int64 Echo(int64 value) { return value; }"));
        Assert.False(original.HasErrors, string.Join(Environment.NewLine, original.Diagnostics));
        var producer = ModuleCompiler.CreateProducer(Assert.IsType<BoundProgram>(original.Program), ["Echo"]);
        var compilation = Compilation.Create(SyntaxTree.Parse("int32 Main() { return int32(Echo(42)); }"),
            ModuleCompiler.CreateReference(producer));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(Assert.IsType<BoundProgram>(compilation.Program), producer);
        var preserved = (byte[])consumer.Clone();
        await ExecuteBothAsync(consumer, producer, 42).ConfigureAwait(true);
        var changed = Compilation.CreateLibrary(SyntaxTree.Parse("public int64 Echo(int64 value) { return value + 1; }"));
        Assert.False(changed.HasErrors, string.Join(Environment.NewLine, changed.Diagnostics));
        var replacement = ModuleCompiler.CreateProducer(Assert.IsType<BoundProgram>(changed.Program), ["Echo"]);
        await ExecuteBothAsync(consumer, replacement, 43).ConfigureAwait(true);
        Assert.Equal(preserved, consumer);

        var incompatible = Compilation.CreateLibrary(SyntaxTree.Parse("public uint64 Echo(uint64 value) { return value; }"));
        Assert.False(incompatible.HasErrors, string.Join(Environment.NewLine, incompatible.Diagnostics));
        _ = Assert.Throws<ModuleContractException>(() => ModuleCompiler.Link(consumer,
            ModuleCompiler.CreateProducer(Assert.IsType<BoundProgram>(incompatible.Program), ["Echo"])));
    }

    private static byte[] CreateCandidateLibrary(BoundRuntimePolicy policy = BoundRuntimePolicy.FailFast)
    {
        var path = Path.Combine(RepositoryRoot(), "self-host", "WideIntegerFacts.terrum");
        var compilation = Compilation.CreateLibrary(ReadTree(path), policy);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        return ModuleCompiler.CreateProducer(Assert.IsType<BoundProgram>(compilation.Program), [
            "Terrum.CodeAnalysis.Numerics64.TryAddInt64",
            "Terrum.CodeAnalysis.Numerics64.TrySubtractInt64",
            "Terrum.CodeAnalysis.Numerics64.TryMultiplyInt64",
            "Terrum.CodeAnalysis.Numerics64.TryAccumulateDecimal",
            "Terrum.CodeAnalysis.Numerics64.TryAccumulateBase",
        ]);
    }

    private static byte[] CreateNumericsLibrary(string name, NativeRuntimePolicy policy, params string[] exports)
    {
        var source = ReadTree(Path.Combine(RepositoryRoot(), "self-host", name));
        var compilation = Compilation.CreateLibrary(source, (BoundRuntimePolicy)policy);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        return ModuleCompiler.CreateProducer(Assert.IsType<BoundProgram>(compilation.Program), exports);
    }

    private static SyntaxTree ReadTree(string path) => SyntaxTree.Parse(File.ReadAllText(path), path);

    private static string RepositoryRoot() =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));

    private static async Task ExecuteBothAsync(byte[] consumer, byte[] producer, int expected,
        NativeRuntimePolicy policy = NativeRuntimePolicy.FailFast)
    {
        var prefix = Path.Combine(AppContext.BaseDirectory, $"wide-candidate-{Guid.NewGuid():N}");
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
            Assert.Equal(consumer, await File.ReadAllBytesAsync(consumerPath).ConfigureAwait(true));
        }
        finally
        {
            File.Delete(image);
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
        Assert.Equal(expected, process.ExitCode);
        Assert.Empty(await error.ConfigureAwait(true));
    }
}
