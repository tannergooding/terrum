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

public sealed class Integer64Tests
{
    [Theory]
    [InlineData("return 9223372036854775807 == int64.MaxValue && -9223372036854775808 == int64.MinValue;", true)]
    [InlineData("return 18446744073709551615 == uint64.MaxValue && 0 == uint64.MinValue;", true)]
    [InlineData("return int64(9223372036854775807) > -1 && uint64(18446744073709551615) > 9223372036854775807;", true)]
    [InlineData("return (9223372036854775806 + 1) == int64.MaxValue;", true)]
    [InlineData("return (uint64.MaxValue - 1) == 18446744073709551614;", true)]
    [InlineData("return (int64(-3000000000) * 3) / 3 == -3000000000;", true)]
    [InlineData("return (uint64.MaxValue / 3) * 3 == uint64.MaxValue;", true)]
    [InlineData("return int64(-9223372036854775808) % -1 == 0;", true)]
    [InlineData("return int64(-11) / 3 == -3 && int64(-11) % 3 == -2;", true)]
    [InlineData("return (~uint64(0) ^ uint64.MaxValue) == 0 && (uint64(1) | 2) == 3;", true)]
    [InlineData("return int64(uint32(2147483648)) == 2147483648;", true)]
    [InlineData("return int64(int32(-2147483648)) == -2147483648;", true)]
    [InlineData("return uint64(uint32.MaxValue) == 4294967295;", true)]
    [InlineData("return int32(int64(2147483647)) == int32.MaxValue;", true)]
    [InlineData("return uint32(uint64(4294967295)) == uint32.MaxValue;", true)]
    [InlineData("return char16(uint64(65535)) == char16.MaxValue;", true)]
    [InlineData("return uint64(0xFFFFFFFFFFFFFFFF) == uint64.MaxValue && 0X2A == uint64(42);", true)]
    [InlineData("return int64(0x8000000000000000) == int64.MinValue && int32(0x80000000) == int32.MinValue;", true)]
    [InlineData("return uint64(0XFFFFFFFFFFFFFFFF) / uint64(0xF) == uint64.MaxValue / 15;", true)]
    [InlineData("return int32(0x1f32) == 7986 && char16(0xFFFF) == char16.MaxValue;", true)]
    [InlineData("int64 value = (0X7FFFFFFF + 0x1); return value == 2147483648;", true)]
    [InlineData("return int64(0x80000000) == 2147483648 && uint32(0XFFFFFFFF) == uint32.MaxValue;", true)]
    [InlineData("return int32(0xFFFFFFFF) == -1 && int32(0x80000000) == int32.MinValue;", true)]
    [InlineData("return int32(0B11111111111111111111111111111111) == -1 && uint32(0b101010) == 42;", true)]
    [InlineData("return int64(0xFFFFFFFF) == 4294967295 && int64(int32(0xFFFFFFFF)) == -1;", true)]
    [InlineData("return int64(0xFFFFFFFFFFFFFFFF) == -1 && uint64(0xFFFFFFFFFFFFFFFF) == uint64.MaxValue;", true)]
    [InlineData("return int32(0x000000000000000000FFFFFFFF) == -1 && int32(0b000000000000000000000000000000001) == 1;", true)]
    [InlineData("return int32(-0xFFFFFFFF) == 1 && ~int32(0b0) == -1;", true)]
    [InlineData("return ~int32(0x80000000) == int32.MaxValue && -int32(0b1) == -1;", true)]
    [InlineData("return int32(0x_8000_0000) == int32.MinValue && int32(0b_11111111_11111111_11111111_11111111) == -1;", true)]
    [InlineData("return int64(9_223_372_036_854_775_807) == int64.MaxValue && uint32(1__0_00) == 1000;", true)]
    [InlineData("return int32(-2_147_483_648) == int32.MinValue && int64(-9_223_372_036_854_775_808) == int64.MinValue;", true)]
    [InlineData("return int32(0x_0000_0000_FFFF_FFFF) == -1 && char16(0x_F_F_F_F) == char16.MaxValue;", true)]
    [InlineData("return char16(0b1111111111111111) == char16.MaxValue && uint64(0B1) == 1;", true)]
    [InlineData("return uint32(0B11111111111111111111111111111111) == uint32.MaxValue && int32(0b0) == 0;", true)]
    [InlineData("int32 bits = (0b100000 | 0b001010); return bits == 42;", true)]
    [InlineData("int64 value = 50000 * 50000; return value == 2500000000;", true)]
    [InlineData("int64 value = (50000 + 1) * (50000 - 1); return value == 2499999999;", true)]
    [InlineData("uint64 value = 65536 * 65536; return value == 4294967296;", true)]
    [InlineData("int32 value = 50000; int64 product = int64(value) * int64(value); return product == 2500000000;", true)]
    public async Task WideValuesAgreeAcrossAotAndCliJitAsync(string body, bool expected)
    {
        var source = "int32 Main() { if (Check()) { return 42; } return 1; } bool Check() { " + body + " }";
        await ExecuteBothAsync(Compile(source), expected ? 42 : 1).ConfigureAwait(true);
    }

    [Fact]
    public async Task FullWidthBinaryBitsAndArbitraryLeadingZeroesAgreeAcrossAotAndCliJitAsync()
    {
        var signedMinimum = "0b_1" + new string('0', 63);
        var unsignedMaximum = "0B___" + new string('1', 64);
        var leadingZeroes = "0b___" + new string('0', 80) + "__1";
        var source = $$"""
            int32 Main() {
                int64 signed = {{signedMinimum}};
                uint64 unsigned = {{unsignedMaximum}};
                int32 small = {{leadingZeroes}};
                if (signed == int64.MinValue && unsigned == uint64.MaxValue && small == 1) {
                    return 42;
                }
                return 0;
            }
            """;
        await ExecuteBothAsync(Compile(source), 42).ConfigureAwait(true);
    }

    [Theory]
    [InlineData("int32 Main() { int64 x = int64.MaxValue + 1; return 0; }", unchecked((int)0xE0540001))]
    [InlineData("int32 Main() { uint64 x = uint64.MaxValue + 1; return 0; }", unchecked((int)0xE0540001))]
    [InlineData("int32 Main() { int64 x = int64.MinValue - 1; return 0; }", unchecked((int)0xE0540001))]
    [InlineData("int32 Main() { uint64 x = 0 - 1; return 0; }", unchecked((int)0xE0540001))]
    [InlineData("int32 Main() { int64 x = 3037000500 * 3037000500; return 0; }", unchecked((int)0xE0540001))]
    [InlineData("int32 Main() { uint64 x = uint64.MaxValue * 2; return 0; }", unchecked((int)0xE0540001))]
    [InlineData("int32 Main() { int64 x = int64.MinValue / -1; return 0; }", unchecked((int)0xE0540001))]
    [InlineData("int32 Main() { uint64 x = 1 / 0; return 0; }", unchecked((int)0xE0540002))]
    [InlineData("int32 Main() { int64 x = -int64.MinValue; return 0; }", unchecked((int)0xE0540001))]
    [InlineData("int32 Main() { int32 x = int32(int64(2147483648)); return 0; }", unchecked((int)0xE0540001))]
    [InlineData("int32 Main() { int64 x = int64(uint64(9223372036854775808)); return 0; }", unchecked((int)0xE0540001))]
    [InlineData("int32 Main() { uint64 x = uint64(int64(-1)); return 0; }", unchecked((int)0xE0540001))]
    [InlineData("int32 Main() { uint32 x = uint32(uint64(4294967296)); return 0; }", unchecked((int)0xE0540001))]
    [InlineData("int32 Main() { char16 x = char16(uint64(65536)); return 0; }", unchecked((int)0xE0540001))]
    [InlineData("int32 Main() { int32 x = int32(uint32(0xFFFFFFFF)); return 0; }", unchecked((int)0xE0540001))]
    [InlineData("int32 Main() { int32 x = -0x80000000; return 0; }", unchecked((int)0xE0540001))]
    [InlineData("int32 Main() { int32 x = -0b10000000000000000000000000000000; return 0; }", unchecked((int)0xE0540001))]
    [InlineData("int32 Main() { int64 x = -0x8000000000000000; return 0; }", unchecked((int)0xE0540001))]
    public async Task WideFailuresUseCheckedRuntimePolicyAsync(string source, int expected)
    {
        await ExecuteBothAsync(Compile(source), expected).ConfigureAwait(true);
    }

    [Theory]
    [InlineData("uint32", "0", "1")]
    [InlineData("uint32", "4294967295", "1")]
    [InlineData("uint32", "4294967295", "2")]
    [InlineData("uint32", "2147483648", "2147483648")]
    [InlineData("uint32", "4294967295", "2147483648")]
    [InlineData("uint64", "0", "1")]
    [InlineData("uint64", "18446744073709551615", "1")]
    [InlineData("uint64", "18446744073709551615", "2")]
    [InlineData("uint64", "18446744073709551615", "4294967296")]
    [InlineData("uint64", "9223372036854775808", "9223372036854775808")]
    [InlineData("uint64", "18446744073709551615", "9223372036854775808")]
    public async Task UnsignedPowerOfTwoDivisionsPreserveExactBitsAsync(string type, string numerator, string divisor)
    {
        var value = ulong.Parse(numerator, System.Globalization.CultureInfo.InvariantCulture);
        var power = ulong.Parse(divisor, System.Globalization.CultureInfo.InvariantCulture);
        var program = Compile($$"""
            {{type}} Quot({{type}} n) { return n / {{type}}({{divisor}}); }
            {{type}} Rem({{type}} n) { return n % {{type}}({{divisor}}); }
            int32 Main() {
                {{type}} n = {{type}}({{numerator}});
                if (Quot(n) != {{type}}({{value / power}})
                    || Rem(n) != {{type}}({{value % power}})) { return 1; }
                return 42;
            }
            """);
        var code = NativeEmitter.EmitCode(program, NativeRuntimePolicy.FailFast);
        for (var index = 0; index < 2; index++)
        {
            var start = code.SourceFunctionOffsets[index];
            var length = code.SourceFunctionOffsets[index + 1] - start;
            var bytes = code.Code.AsSpan(start, length);
            ReadOnlySpan<byte> division = type == "uint32" ? [0xF7, 0xF1] : [0x48, 0xF7, 0xF1];
            Assert.True(bytes.IndexOf(division) < 0);
        }
        await ExecuteBothAsync(program, 42).ConfigureAwait(true);
    }

    [Theory]
    [InlineData("uint32", "4294967295", "3")]
    [InlineData("uint64", "18446744073709551615", "3")]
    public async Task OtherUnsignedDivisorsRetainGeneralDivisionAsync(string type, string numerator, string divisor)
    {
        var value = ulong.Parse(numerator, System.Globalization.CultureInfo.InvariantCulture);
        var other = ulong.Parse(divisor, System.Globalization.CultureInfo.InvariantCulture);
        var program = Compile($$"""
            {{type}} Quot({{type}} n) { return n / {{type}}({{divisor}}); }
            {{type}} Rem({{type}} n) { return n % {{type}}({{divisor}}); }
            int32 Main() {
                {{type}} n = {{type}}({{numerator}});
                if (Quot(n) != {{type}}({{value / other}})
                    || Rem(n) != {{type}}({{value % other}})) { return 1; }
                return 42;
            }
            """);
        var code = NativeEmitter.EmitCode(program, NativeRuntimePolicy.FailFast);
        for (var index = 0; index < 2; index++)
        {
            var start = code.SourceFunctionOffsets[index];
            var length = code.SourceFunctionOffsets[index + 1] - start;
            var bytes = code.Code.AsSpan(start, length);
            ReadOnlySpan<byte> division = type == "uint32" ? [0xF7, 0xF1] : [0x48, 0xF7, 0xF1];
            Assert.True(bytes.IndexOf(division) >= 0);
        }
        await ExecuteBothAsync(program, 42).ConfigureAwait(true);
    }

    [Theory]
    [InlineData("uint32", "/", "1")]
    [InlineData("uint32", "%", "2147483648")]
    [InlineData("uint64", "/", "9223372036854775808")]
    [InlineData("uint64", "%", "1")]
    public async Task UnsignedPowerOfTwoDivisionsEvaluateNumeratorOnceAsync(string type, string operation, string divisor)
    {
        await ExecuteBothAsync(Compile($$"""
            struct Counter {
                public mut int32 Calls;
                public Counter() { Calls = 0; }
                public mut {{type}} Read() { Calls = Calls + 1; return {{type}}.MaxValue; }
            }
            int32 Main() {
                mut Counter counter = new Counter();
                {{type}} result = counter.Read() {{operation}} {{type}}({{divisor}});
                if (counter.Calls != 1) { return 1; }
                return 42;
            }
            """), 42).ConfigureAwait(true);
        await ExecuteBothAsync(Compile($$"""
            {{type}} Fail() { require false; return {{type}}(1); }
            int32 Main() {
                {{type}} result = Fail() {{operation}} {{type}}({{divisor}});
                return 1;
            }
            """), unchecked((int)0xE0540003)).ConfigureAwait(true);
    }

    [Theory]
    [InlineData("uint32", "/", "0")]
    [InlineData("uint64", "%", "0")]
    [InlineData("int32", "/", "-1")]
    [InlineData("int64", "%", "-1")]
    public async Task UnspecializedDivisionsRetainFailureAndSignedSemanticsAsync(string type, string operation, string divisor)
    {
        var source = $$"""
            int32 Main() {
                {{type}} n = {{type}}.MinValue;
                {{type}} result = n {{operation}} {{type}}({{divisor}});
                return 42;
            }
            """;
        var expected = divisor == "0" ? unchecked((int)0xE0540002)
            : operation == "/" ? unchecked((int)0xE0540001) : 42;
        await ExecuteBothAsync(Compile(source), expected).ConfigureAwait(true);
    }

    [Theory]
    [InlineData("int32", "/")]
    [InlineData("int32", "%")]
    [InlineData("int64", "/")]
    [InlineData("int64", "%")]
    public Task SignedPowerOfTwoDivisorsPreserveNegativeResultsAsync(string type, string operation) =>
        ExecuteBothAsync(Compile($$"""
            {{type}} Calculate({{type}} n) { return n {{operation}} {{type}}(2); }
            int32 Main() {
                if (Calculate({{type}}(-3)) != {{type}}(-1)) { return 1; }
                return 42;
            }
            """), 42);

    [Theory]
    [InlineData("int32", "1")]
    [InlineData("int32", "2")]
    [InlineData("int32", "1073741824")]
    [InlineData("int64", "1")]
    [InlineData("int64", "2")]
    [InlineData("int64", "4611686018427387904")]
    public void SignedPowerOfTwoDivisionsAvoidGeneralDivision(string type, string divisor)
    {
        var program = Compile($$"""
            {{type}} Quot({{type}} n) { return n / {{type}}({{divisor}}); }
            {{type}} Rem({{type}} n) { return n % {{type}}({{divisor}}); }
            int32 Main() { return 42; }
            """);
        var code = NativeEmitter.EmitCode(program, NativeRuntimePolicy.FailFast);
        var general = NativeEmitter.EmitCode(Compile($$"""
            {{type}} Quot({{type}} n) { return n / {{type}}(3); }
            {{type}} Rem({{type}} n) { return n % {{type}}(3); }
            int32 Main() { return 42; }
            """), NativeRuntimePolicy.FailFast);
        ReadOnlySpan<byte> signedDivision = type == "int32" ? [0x99, 0xF7, 0xF9] : [0x48, 0x99, 0x48, 0xF7, 0xF9];
        for (var index = 0; index < 2; index++)
        {
            var start = code.SourceFunctionOffsets[index];
            var length = code.SourceFunctionOffsets[index + 1] - start;
            var bytes = code.Code.AsSpan(start, length);
            Assert.True(bytes.IndexOf(signedDivision) < 0);
            var generalStart = general.SourceFunctionOffsets[index];
            var generalLength = general.SourceFunctionOffsets[index + 1] - generalStart;
            Assert.True(general.Code.AsSpan(generalStart, generalLength).IndexOf(signedDivision) >= 0);
            Assert.True(length < generalLength);
        }
    }

    [Theory]
    [InlineData("int32", "-1")]
    [InlineData("int32", "-2")]
    [InlineData("int32", "0")]
    [InlineData("int32", "3")]
    [InlineData("int64", "-1")]
    [InlineData("int64", "-2")]
    [InlineData("int64", "0")]
    [InlineData("int64", "3")]
    public void OtherSignedDivisorsRetainGeneralDivision(string type, string divisor)
    {
        var program = Compile($$"""
            {{type}} Quot({{type}} n) { return n / {{type}}({{divisor}}); }
            {{type}} Rem({{type}} n) { return n % {{type}}({{divisor}}); }
            int32 Main() { return 42; }
            """);
        var code = NativeEmitter.EmitCode(program, NativeRuntimePolicy.FailFast);
        ReadOnlySpan<byte> signedDivision = type == "int32" ? [0x99, 0xF7, 0xF9] : [0x48, 0x99, 0x48, 0xF7, 0xF9];
        for (var index = 0; index < 2; index++)
        {
            var start = code.SourceFunctionOffsets[index];
            var length = code.SourceFunctionOffsets[index + 1] - start;
            Assert.True(code.Code.AsSpan(start, length).IndexOf(signedDivision) >= 0);
        }
    }

    [Theory]
    [InlineData("int32", "/", "1")]
    [InlineData("int32", "%", "2")]
    [InlineData("int64", "/", "4611686018427387904")]
    [InlineData("int64", "%", "1")]
    public void SignedPowerOfTwoSelectionDoesNotRelaxNoThrowProof(string type, string operation, string divisor)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse($$"""
            [NoThrow] {{type}} Calculate({{type}} n) { return n {{operation}} {{type}}({{divisor}}); }
            int32 Main() { return 42; }
            """));
        Assert.Contains(compilation.Diagnostics, static diagnostic => diagnostic.Code == "TRM3017");
    }

    [Theory]
    [InlineData("uint32", "/", "2")]
    [InlineData("uint32", "%", "2147483648")]
    [InlineData("uint64", "/", "9223372036854775808")]
    [InlineData("uint64", "%", "1")]
    public void NativeUnsignedStrengthReductionDoesNotRelaxNoThrowProof(string type, string operation, string divisor)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse($$"""
            [NoThrow] {{type}} Calculate({{type}} n) { return n {{operation}} {{type}}({{divisor}}); }
            int32 Main() { return 42; }
            """));
        Assert.Contains(compilation.Diagnostics, static diagnostic => diagnostic.Code == "TRM3017");
    }

    [Fact]
    public async Task WideArgumentsNestedFieldsOutputsAndRecursionPreserveBitsAsync()
    {
        var program = Compile("""
            struct Inner {
                public mut int64 Value;
                public Inner(int64 value) { Value = value; }
            }
            struct Pair {
                public mut Inner Signed;
                public uint64 Unsigned;
                public Pair(int64 signed, uint64 unsigned) {
                    Signed = new Inner(signed); Unsigned = unsigned;
                }
            }
            int64 Sum(int64 a, int64 b, int64 c, int64 d, int64 e, int64 f) {
                return a + b + c + d + e + f;
            }
            int64 Recurse(int64 n) {
                if (n == 0) { return 32; }
                return Recurse(n - 1);
            }
            bool Try(bool success, out Pair value) {
                if (!success) { return false; }
                value = new Pair(1, uint64.MaxValue);
                value.Signed.Value = 10;
                return true;
            }
            bool Forward(bool success, out Pair value) { return Try(success, out value); }
            int32 Main() {
                require Forward(true, out var value);
                if (Forward(false, out var ignored)) { return 1; }
                Pair copy = value;
                mut Pair replacement = value;
                replacement.Signed.Value = 2;
                if (copy.Signed.Value != 10 || replacement.Signed.Value != 2
                    || value.Unsigned != uint64.MaxValue) { return 2; }
                if (Sum(1, 2, 3, 4, 0, Recurse(3)) != 42) { return 3; }
                return 42;
            }
            """);
        Assert.Equal(16, program.Structures[1].Size);
        await ExecuteBothAsync(program, 42).ConfigureAwait(true);
    }

    [Fact]
    public async Task ScalarWideOutputForwardingPublishesOnlyOnSuccessAsync()
    {
        await ExecuteBothAsync(Compile("""
            bool Try(bool success, out int64 signed, out uint64 unsigned) {
                if (!success) { return false; }
                signed = int64.MinValue;
                unsigned = uint64.MaxValue;
                return true;
            }
            bool Forward(bool success, out int64 signed, out uint64 unsigned) {
                return Try(success, out signed, out unsigned);
            }
            int32 Main() {
                mut int64 signed = 0;
                mut uint64 unsigned = 0;
                require Forward(true, out signed, out unsigned);
                if (signed != int64.MinValue || unsigned != uint64.MaxValue) { return 2; }
                if (Forward(false, out signed, out unsigned)) { return 1; }
                require Forward(true, out signed, out unsigned);
                return 42;
            }
            """), 42).ConfigureAwait(true);
    }

    [Fact]
    public async Task PublishedWideIntegerSampleExecutesAsync()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
        var source = await File.ReadAllTextAsync(Path.Combine(root, "samples", "native", "integers64.terrum")).ConfigureAwait(true);
        await ExecuteBothAsync(Compile(source), 42).ConfigureAwait(true);
    }

    [Fact]
    public async Task WideLiteralPayloadsAndFieldContractsSurviveModuleLinkingAsync()
    {
        var library = Compilation.CreateLibrary(SyntaxTree.Parse("""
            public struct Wide {
                public int64 Signed;
                public uint64 Unsigned;
                public Wide() { Signed = int64.MinValue; Unsigned = uint64.MaxValue; }
            }
            public Wide Make() { return new Wide(); }
            """));
        var producer = ModuleCompiler.CreateProducer(CompileLibrary(library), ["Make"]);
        var reference = ModuleCompiler.CreateReference(producer);
        var consumer = Compilation.Create(SyntaxTree.Parse("""
            int32 Main() {
                Wide value = Make();
                if (value.Signed != -9223372036854775808
                    || value.Unsigned != 18446744073709551615) { return 1; }
                return 42;
            }
            """), reference);
        Assert.False(consumer.HasErrors, string.Join(Environment.NewLine, consumer.Diagnostics));
        var artifact = ModuleCompiler.CreateConsumer(Assert.IsType<BoundProgram>(consumer.Program), producer);
        await ExecuteBothAsync(ModuleCompiler.Link(artifact, producer), 42).ConfigureAwait(true);
    }

    [Theory]
    [InlineData("int64 Main() { return 0; }")]
    [InlineData("int32 Main() { int64 value = int32(1); return 0; }")]
    [InlineData("int32 Main() { uint64 value = int64(-1); return 0; }")]
    [InlineData("int32 Main() { int64 value = 9223372036854775808; return 0; }")]
    [InlineData("int32 Main() { uint64 value = 18446744073709551616; return 0; }")]
    [InlineData("int32 Main() { int64 value = 18446744073709551615; return 0; }")]
    [InlineData("int32 Main() { bool value = bool(int64(1)); return 0; }")]
    [InlineData("int32 Main() { int64 value = 1.0; return 0; }")]
    [InlineData("int32 Main() { var value = 20 * 20; return 0; }")]
    [InlineData("int32 Main() { var value = -(20 * 20); return 0; }")]
    [InlineData("int32 Main() { int32 value = 50000; int64 product = value * value; return 0; }")]
    [InlineData("int32 Main() { int64 value = int32(20) * 20; return 0; }")]
    [InlineData("int32 Main() { var value = 0x2A; return 0; }")]
    [InlineData("int32 Main() { int32 value = 0x100000000; return 0; }")]
    [InlineData("int32 Main() { int32 value = 0x_1_0000_0000; return 0; }")]
    [InlineData("int32 Main() { int32 value = 0b100000000000000000000000000000000; return 0; }")]
    [InlineData("int32 Main() { int32 value = 4294967295; return 0; }")]
    [InlineData("int32 Main() { var value = 0b101010; return 0; }")]
    [InlineData("int32 Main() { uint32 value = 0x100000000; return 0; }")]
    [InlineData("int32 Main() { char16 value = 0x10000; return 0; }")]
    [InlineData("int32 Main() { int64 value = -0x10000000000000000; return 0; }")]
    [InlineData("int32 Main() { uint64 value = 0x10000000000000000; return 0; }")]
    [InlineData("int32 Main() { uint64 value = 0x_1_0000_0000_0000_0000; return 0; }")]
    [InlineData("int32 Main() { int32 value = 2_147_483_648; return 0; }")]
    [InlineData("int32 Main() { uint64 value = 18_446_744_073_709_551_616; return 0; }")]
    [InlineData("int32 Main() { uint64 value = 0b10000000000000000000000000000000000000000000000000000000000000000; return 0; }")]
    public void WideInvalidSourcesAreDiagnosed(string source)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        Assert.True(compilation.HasErrors);
        Assert.Null(compilation.Program);
    }

    [Fact]
    public void ForgedNarrowLiteralPayloadIsRejectedBeforeEmission()
    {
        var program = Compile("int32 Main() { return 42; }");
        var function = program.Functions[0];
        var result = Assert.IsType<BoundReturn>(function.Body.Statements[0]);
        BoundExpression[] expressions = [
            new BoundConstant((long)int.MaxValue + 1, PrimitiveType.Int32, result.Span),
            new BoundCast(new BoundConstant(1, PrimitiveType.Bool, result.Span), PrimitiveType.Int32, result.Span),
        ];
        foreach (var expression in expressions)
        {
            var forged = function with {
                Body = function.Body with {
                    Statements = function.Body.Statements.SetItem(0, result with { Value = expression }),
                },
            };
            _ = Assert.Throws<ArgumentException>(() =>
                NativeEmitter.EmitCode(program with { Functions = program.Functions.SetItem(0, forged) },
                    NativeRuntimePolicy.FailFast));
        }
    }

    [Fact]
    public void UnsignedLiteralPayloadsRetainTheirExactBits()
    {
        var program = Compile("""
            int32 Main() {
                uint64 large = 18446744073709551615;
                int64 negative = -9223372036854775808;
                uint32 previous = 4294967295;
                return 0;
            }
            """);
        var statements = program.Functions[0].Body.Statements;
        Assert.Equal(-1L, Assert.IsType<BoundConstant>(Assert.IsType<BoundLocal>(statements[0]).Initializer).Value);
        Assert.Equal(long.MinValue, Assert.IsType<BoundConstant>(Assert.IsType<BoundLocal>(statements[1]).Initializer).Value);
        Assert.Equal(-1L, Assert.IsType<BoundConstant>(Assert.IsType<BoundLocal>(statements[2]).Initializer).Value);
    }

    [Theory]
    [InlineData(PrimitiveType.Int64)]
    [InlineData(PrimitiveType.UInt64)]
    public void WideHostBufferElementsRemainOutsideTheFourByteHostBufferContract(PrimitiveType elementType)
    {
        var program = Compile("int32 Main() { return 0; }");
        var malformed = program with {
            Buffers = [new HostBuffer("wide", elementType, [0], IsWritable: true)],
        };
        _ = Assert.Throws<ArgumentException>(() => NativeEmitter.EmitCode(malformed, NativeRuntimePolicy.FailFast));
    }

    private static BoundProgram Compile(string source)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        return Assert.IsType<BoundProgram>(compilation.Program);
    }

    private static async Task ExecuteBothAsync(BoundProgram program, int expected)
    {
        var prefix = Path.Combine(AppContext.BaseDirectory, $"integer64-{Guid.NewGuid():N}");
        var executable = prefix + ".exe";
        var consumer = prefix + ".consumer.trm";
        var producer = prefix + ".producer.trm";
        try
        {
            await File.WriteAllBytesAsync(executable, NativeEmitter.Emit(program, NativeRuntimePolicy.FailFast)).ConfigureAwait(true);
            await RunAsync(executable, [], expected).ConfigureAwait(true);
            var unused = Compilation.CreateLibrary(SyntaxTree.Parse("public int32 Unused() { return 0; }"));
            var producerBytes = ModuleCompiler.CreateProducer(CompileLibrary(unused), ["Unused"]);
            await File.WriteAllBytesAsync(producer, producerBytes).ConfigureAwait(true);
            await File.WriteAllBytesAsync(consumer, ModuleCompiler.CreateConsumer(program, producerBytes)).ConfigureAwait(true);
            var framework = new DirectoryInfo(AppContext.BaseDirectory);
            var configuration = framework.Parent?.Name ?? throw new InvalidOperationException("Missing configuration.");
            var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
            var cli = Path.Combine(root, "artifacts", "bin", "sources", "terrumc", configuration, framework.Name, "terrumc.dll");
            await RunAsync("dotnet", [cli, "run-module", consumer, producer, "--runtime", "fail-fast"], expected).ConfigureAwait(true);
        }
        finally
        {
            File.Delete(executable);
            File.Delete(consumer);
            File.Delete(producer);
        }
    }

    private static BoundProgram CompileLibrary(Compilation compilation)
    {
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        return Assert.IsType<BoundProgram>(compilation.Program);
    }

    private static async Task RunAsync(string executable, ImmutableArray<string> arguments, int expected)
    {
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Failed to start native worker.");
        var error = process.StandardError.ReadToEndAsync();
        try
        {
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(true);
        }
        catch (TimeoutException)
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync().ConfigureAwait(true);
            throw;
        }
        Assert.Equal(expected, process.ExitCode);
        Assert.Empty(await error.ConfigureAwait(true));
    }
}
