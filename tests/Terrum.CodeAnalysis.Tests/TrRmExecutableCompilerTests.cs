// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed class TrRmExecutableCompilerTests
{
    private static BoundProgram Compile(string source, bool library = false)
    {
        var syntax = SyntaxTree.Parse(source);
        var compilation = library ? Compilation.CreateLibrary(syntax) : Compilation.Create(syntax);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        return Assert.IsType<BoundProgram>(compilation.Program);
    }

    [Theory]
    [InlineData("int32 Main() { return 42; }", 42)]
    [InlineData("int32 Main() { mut int32 x = 10; x = x + 20; x = x ^ 12; return x; }", 18)]
    [InlineData("int32 Main() { int32 x = int32(uint64.MaxValue % uint64(100)); return x; }", 15)]
    [InlineData("int32 Main() { return int32(char16(55296)); }", 55296)]
    public void ScalarExecutableRoundTripsToNativeImageAndJitStatus(string source, int expected)
    {
        var original = Compile(source);
        var artifact = TrRmExecutableCompiler.Emit(original, "sample");
        TrRmStructuralMetadataValidator.ValidateCompactLinear(artifact);
        var loaded = TrRmExecutableCompiler.Load(artifact);
        Assert.Equal(expected, NativeJit.Execute(loaded, NativeRuntimePolicy.FailFast));
        Assert.Equal(expected, NativeJit.Execute(original, NativeRuntimePolicy.FailFast));
        Assert.NotEmpty(NativeEmitter.Emit(loaded, NativeRuntimePolicy.FailFast));
    }

    [Theory]
    [InlineData("float32", "1.5", "<", "2.0")]
    [InlineData("float32", "2.0", "<=", "2.0")]
    [InlineData("float32", "2.0", ">", "1.5")]
    [InlineData("float32", "2.0", ">=", "2.0")]
    [InlineData("float32", "1.5", "==", "1.5")]
    [InlineData("float32", "1.5", "!=", "2.0")]
    [InlineData("float64", "1.5", "<", "2.0")]
    [InlineData("float64", "2.0", "<=", "2.0")]
    [InlineData("float64", "2.0", ">", "1.5")]
    [InlineData("float64", "2.0", ">=", "2.0")]
    [InlineData("float64", "1.0", "==", "1.0")]
    [InlineData("float64", "1.5", "!=", "2.0")]
    public void FloatingComparisonsRoundTripThroughLinearBodies(
        string type, string left, string operation, string right)
    {
        var source = $$"""
            int32 Main() { return Compare({{left}}, {{right}}) ? 42 : 0; }
            bool Compare({{type}} left, {{type}} right) { return left {{operation}} right; }
            """;
        var original = Compile(source);
        var artifact = TrRmExecutableCompiler.Emit(original, "float-comparison");
        var loaded = TrRmExecutableCompiler.Load(artifact);

        Assert.Equal(42, NativeJit.Execute(original, NativeRuntimePolicy.FailFast));
        Assert.Equal(42, NativeJit.Execute(loaded, NativeRuntimePolicy.FailFast));
        Assert.NotEmpty(NativeEmitter.Emit(loaded, NativeRuntimePolicy.FailFast));
    }

    [Theory]
    [InlineData("float32")]
    [InlineData("float64")]
    public void FloatingComparisonRoundTripsThroughIndexedBodies(string type)
    {
        var source = $$"""
            int32 Main() {
                {{type}} negativeZero = -0.0;
                {{type}} positiveZero = 0.0;
                if (negativeZero == positiveZero) { return 42; }
                return 0;
            }
            """;
        var original = Compile(source);
        var artifact = TrRmExecutableCompiler.Emit(original, "indexed-float-comparison");
        var loaded = TrRmExecutableCompiler.Load(artifact);

        Assert.Equal(42, NativeJit.Execute(original, NativeRuntimePolicy.FailFast));
        Assert.Equal(42, NativeJit.Execute(loaded, NativeRuntimePolicy.FailFast));
    }

    [Theory]
    [InlineData("float32", "==", 0)]
    [InlineData("float32", "!=", 42)]
    [InlineData("float32", "<", 0)]
    [InlineData("float32", "<=", 0)]
    [InlineData("float32", ">", 0)]
    [InlineData("float32", ">=", 0)]
    [InlineData("float64", "==", 0)]
    [InlineData("float64", "!=", 42)]
    [InlineData("float64", "<", 0)]
    [InlineData("float64", "<=", 0)]
    [InlineData("float64", ">", 0)]
    [InlineData("float64", ">=", 0)]
    public void FloatingNaNComparisonsFollowIeee(string type, string operation, int expected)
    {
        var source = $$"""
            int32 Main() {
                {{type}} value = 1.0;
                if (value {{operation}} value) { return 42; }
                return 0;
            }
            """;
        var oneBits = type == "float32"
            ? new byte[] { 0x00, 0x00, 0x80, 0x3F }
            : new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0xF0, 0x3F };
        var nanBits = type == "float32"
            ? new byte[] { 0x01, 0x00, 0xC0, 0x7F }
            : new byte[] { 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0xF8, 0x7F };
        var artifact = TrRmExecutableCompiler.Emit(Compile(source), "float-nan");
        var loaded = TrRmExecutableCompiler.Load(
            ReplaceBodyConstant(artifact, 1, oneBits, nanBits));

        Assert.Equal(expected, NativeJit.Execute(loaded, NativeRuntimePolicy.FailFast));
    }

    [Fact]
    public void FloatingArithmeticRemainsOutsideExecutableTrRm()
    {
        var program = Compile("int32 Main() { float32 left = 1.0; float32 right = 2.0; "
            + "return int32(left + right); }");

        Assert.Throws<NotSupportedException>(() => TrRmExecutableCompiler.Emit(program, "float-arithmetic"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    public void InvalidFloatingComparisonKindsAreRejected(byte kind)
    {
        const string source = """
            int32 Main() { return Compare(1.5, 2.0) ? 42 : 0; }
            bool Compare(float32 left, float32 right) { return left < right; }
            """;
        var artifact = TrRmExecutableCompiler.Emit(Compile(source), "invalid-float-compare");
        var index = new TrRmArtifactIndex(artifact);
        Assert.True(index.TryGetSection(0x20, out var bodyBytes, out var bodyCount));
        var bodies = new TrRmBodyTableReader(bodyBytes, bodyCount);
        var body = bodies.GetRow(2).ToArray();
        var opcodeOffset = body.AsSpan().IndexOf((byte)0x19);
        Assert.True(opcodeOffset >= 0);
        body[opcodeOffset + 2] = kind;

        var bodyRows = new ReadOnlyMemory<byte>[bodyCount];
        for (var row = 1; row <= bodyCount; row++)
        {
            bodyRows[row - 1] = row == 2 ? body : bodies.GetRow(row).ToArray();
        }
        var rewrittenBodies = TrRmVariableTableWriter.EncodeBodies(bodyRows);
        var sections = new List<TrRmSection>(index.SectionCount);
        for (var section = 0; section < index.SectionCount; section++)
        {
            index.GetSection(section, out var sectionKind, out var flags, out var bytes, out var count);
            sections.Add(new TrRmSection(sectionKind, flags, (uint)count,
                sectionKind == 0x20 ? rewrittenBodies : bytes.ToArray()));
        }
        var malformed = TrRmArtifactWriter.Encode(sections);

        Assert.Throws<InvalidDataException>(() => TrRmExecutableCompiler.Load(malformed));
    }

    [Fact]
    public void OptionalFloatingTypesRemainOutsideExecutableTrRm()
    {
        var artifact = OptionalScalarArtifact(
            IndexedOptionalPresenceBody(8, true, 9),
            optionalChar16: false,
            optionalInt32: false,
            optionalFloat32: true);

        Assert.Throws<InvalidDataException>(() => TrRmExecutableCompiler.Load(artifact));
    }

    [Fact]
    public void IntegerBodyBytesRemainStableInMixedFloatModules()
    {
        var integerOnly = TrRmExecutableCompiler.Emit(
            Compile("int32 Main() { return 42; }"), "integer-only");
        const string mixedSource = """
            int32 Main() { return Integer(); }
            int32 Integer() { return 42; }
            float32 Identity(float32 value) { return value; }
            """;
        var mixed = TrRmExecutableCompiler.Emit(Compile(mixedSource), "mixed-float");

        var integerOnlyIndex = new TrRmArtifactIndex(integerOnly);
        Assert.True(integerOnlyIndex.TryGetSection(0x20, out var integerOnlyBodies, out var integerOnlyCount));
        var integerOnlyBody = new TrRmBodyTableReader(integerOnlyBodies, integerOnlyCount).GetRow(1);
        var mixedIndex = new TrRmArtifactIndex(mixed);
        Assert.True(mixedIndex.TryGetSection(0x20, out var mixedBodies, out var mixedCount));
        var integerMethodBody = new TrRmBodyTableReader(mixedBodies, mixedCount).GetRow(2);

        Assert.Equal(integerOnlyBody.ToArray(), integerMethodBody.ToArray());
    }

    [Fact]
    public void LibraryPreservesScalarSignaturesVisibilityAndExportIdentity()
    {
        var source = """
            public bool B(bool value) { return !value; }
            public char16 C(char16 value) { return value; }
            public int32 I(int32 value) { return value + 1; }
            public uint32 U(uint32 value) { return value ^ uint32.MaxValue; }
            public int64 L(int64 value) { return value + int64.MinValue; }
            public uint64 W(uint64 value) { return value - 1; }
            int32 Hidden(int32 value) { return value; }
            """;
        var artifact = TrRmExecutableCompiler.Emit(Compile(source, true), "scalar-library");
        TrRmStructuralMetadataValidator.ValidateCompactLinear(artifact);
        var program = TrRmExecutableCompiler.Load(artifact);
        Assert.Equal(-1, program.EntryPoint);
        Assert.Equal(7, program.Functions.Length);
        Assert.Equal(PrimitiveType.Bool, program.Functions[0].ReturnType);
        Assert.Equal(PrimitiveType.Char16, program.Functions[1].Variables[0].Type);
        Assert.Equal(PrimitiveType.UInt64, program.Functions[5].ReturnType);
        Assert.NotEmpty(NativeEmitter.EmitLibrary(program,
            ["B", "C", "I", "U", "L", "W"], NativeRuntimePolicy.FailFast));
        Assert.Throws<ArgumentException>(() => NativeEmitter.EmitLibrary(program,
            ["Hidden"], NativeRuntimePolicy.FailFast));
    }

    [Theory]
    [InlineData("int32 Main() { return Helper(); } int32 Helper() { return 42; }", 42)]
    [InlineData("int32 Main() { mut int32 x = 1; x += Helper(); return x; } int32 Helper() { return 41; }", 42)]
    [InlineData("int32 Main() { return Factorial(5); } int32 Factorial(int32 n) { if (n == 0) { return 1; } else { return n * Factorial(n - 1); } }", 120)]
    [InlineData("int32 Main() { return Even(6); } int32 Even(int32 n) { if (n == 0) { return 1; } else { return Odd(n - 1); } } int32 Odd(int32 n) { if (n == 0) { return 0; } else { return Even(n - 1); } }", 1)]
    [InlineData("int32 Main() { Observe(); return 42; } void Observe() { }", 42)]
    [InlineData("int32 Main() { return Mix(true, char16(66), -3, uint32(4), int64(5), uint64(6)); } int32 Mix(bool b, char16 c, int32 i, uint32 u, int64 l, uint64 w) { if (b) { return int32(c) + i + int32(u) + int32(l) + int32(w); } else { return 0; } }", 78)]
    [InlineData("int32 Main() { mut int32 n = 0; while (n < 4) { n = Step(n); } return n + Final(); } int32 Step(int32 n) { return n + 1; } int32 Final() { return 38; }", 42)]
    public void LocalCallsIncludeForwardNestedAndFiniteRecursion(string source, int expected)
    {
        var program = Compile(source);
        var artifact = TrRmExecutableCompiler.Emit(program, "calls");
        var loaded = TrRmExecutableCompiler.Load(artifact);
        Assert.Equal(expected, NativeJit.Execute(loaded, NativeRuntimePolicy.FailFast));
        Assert.Equal(expected, NativeJit.Execute(program, NativeRuntimePolicy.FailFast));
        Assert.NotEmpty(NativeEmitter.Emit(loaded, NativeRuntimePolicy.FailFast));
        Assert.Throws<NotSupportedException>(() => TrRmExecutableCompiler.Emit(loaded, "source-free"));
    }

    [Fact]
    public void VoidCallsAndArgumentSnapshotsSurviveMutationAndLazyFlow()
    {
        const string source = """
            void Observe(int32 value) { }
            int32 Sum(int32 a, int32 b, int32 c, int32 d, int32 e, int32 f) {
                return a * 10 + b + c + d + e + f;
            }
            int32 Main() {
                mut int32 x = 1;
                Observe(x++);
                if (false && (Sum(1, 2, 3, 4, 5, 6) == 0)) { Observe(9); }
                Observe(x);
                return Sum(x++, ++x, 3, 4, 5, 6);
            }
            """;
        var program = Compile(source);
        var loaded = TrRmExecutableCompiler.Load(TrRmExecutableCompiler.Emit(program, "void-calls"));
        Assert.Equal(42, NativeJit.Execute(program, NativeRuntimePolicy.FailFast));
        Assert.Equal(42, NativeJit.Execute(loaded, NativeRuntimePolicy.FailFast));
        Assert.Empty(loaded.Functions[2].Body.Statements);
        Assert.Throws<NotSupportedException>(() => TrRmExecutableCompiler.Emit(loaded, "not-source"));
    }

    [Fact]
    public void VoidOperationOrdinalsDoNotExhaustScalarValueInventory()
    {
        var source = "int32 Main() { "
            + string.Concat(Enumerable.Repeat("Observe(); ", 260))
            + "return 42; } void Observe() { }";
        var artifact = TrRmExecutableCompiler.Emit(Compile(source), "void-ordinals");
        var loaded = TrRmExecutableCompiler.Load(artifact);
        Assert.Single(loaded.Functions[0].Variables);
        Assert.Equal(42, NativeJit.Execute(loaded, NativeRuntimePolicy.FailFast));
    }

    [Fact]
    public void NoThrowEffectRoundTripsWithoutFabricatedProof()
    {
        var source = "[NoThrow] int32 Main() { return 42; }";
        var program = TrRmExecutableCompiler.Load(
            TrRmExecutableCompiler.Emit(Compile(source), "no-throw"));
        Assert.True(program.Functions[0].IsNoThrow);
    }

    [Theory]
    [InlineData("int32 Main() { return 1 << 2; }", 4)]
    [InlineData("int32 Main() { return int32.MaxValue >>> 1; }", 1073741823)]
    [InlineData("int32 Main() { mut int32 x = 20; int32 old = x; x = x + 2; return old + x; }", 42)]
    [InlineData("int32 Main() { return int32.MinValue % -1; }", 0)]
    [InlineData("int32 Main() { return int32(int64.MinValue % -1); }", 0)]
    public void FoldedConstantsAndRetainedScalarOperationsMatchBothExistingPolicies(string source, int expected)
    {
        foreach (var policy in new[] { BoundRuntimePolicy.FailFast, BoundRuntimePolicy.CheckedExceptions })
        {
            var compilation = Compilation.Create(SyntaxTree.Parse(source), policy);
            Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
            var program = Assert.IsType<BoundProgram>(compilation.Program);
            Assert.Equal(expected, NativeJit.Execute(program, (NativeRuntimePolicy)policy));
            if (policy == BoundRuntimePolicy.FailFast)
            {
                Assert.Equal(expected, NativeJit.Execute(
                    TrRmExecutableCompiler.Load(TrRmExecutableCompiler.Emit(program, "policies")),
                    NativeRuntimePolicy.FailFast));
            }
            else
            {
                Assert.Throws<NotSupportedException>(() => TrRmExecutableCompiler.Emit(program, "policies"));
            }
        }
    }

    [Fact]
    public void ScalarLibraryMatchesDirectNativeBytesAndIndependentExportCalls()
    {
        const string source = """
            public bool B(bool value) { bool result = !value; return result; }
            public char16 C(char16 value) { return value; }
            public int32 I(int32 left, int32 right) { int32 result = left + right; return result; }
            public uint32 U(uint32 left, uint32 right) { uint32 result = left ^ right; return result; }
            public int64 L(int64 left, int64 right) { int64 result = left % right; return result; }
            public uint64 W(uint64 left, uint64 right) { uint64 result = left / right; return result; }
            public float32 F(float32 value) { return value; }
            public float64 D(float64 value) { return value; }
            public void V() { }
            """;
        var original = Compile(source, true);
        var loaded = TrRmExecutableCompiler.Load(TrRmExecutableCompiler.Emit(original, "native-parity"));
        string[] names = ["B", "C", "I", "U", "L", "W", "F", "D", "V"];
        var expected = NativeEmitter.EmitLibrary(original, names, NativeRuntimePolicy.FailFast);
        var actual = NativeEmitter.EmitLibrary(loaded, names, NativeRuntimePolicy.FailFast);
        Assert.Equal(expected, actual);
        var path = Path.Combine(Directory.GetCurrentDirectory(), $"trrm-scalar-{Guid.NewGuid():N}.dll");
        try
        {
            File.WriteAllBytes(path, actual);
            var handle = NativeLibrary.Load(path);
            try
            {
                var boolean = Marshal.GetDelegateForFunctionPointer<BoolIdentity>(NativeLibrary.GetExport(handle, "B"));
                var character = Marshal.GetDelegateForFunctionPointer<CharIdentity>(NativeLibrary.GetExport(handle, "C"));
                var signed = Marshal.GetDelegateForFunctionPointer<IntBinary>(NativeLibrary.GetExport(handle, "I"));
                var unsigned = Marshal.GetDelegateForFunctionPointer<UIntBinary>(NativeLibrary.GetExport(handle, "U"));
                var wideSigned = Marshal.GetDelegateForFunctionPointer<LongBinary>(NativeLibrary.GetExport(handle, "L"));
                var wideUnsigned = Marshal.GetDelegateForFunctionPointer<ULongBinary>(NativeLibrary.GetExport(handle, "W"));
                var noResult = Marshal.GetDelegateForFunctionPointer<VoidAction>(NativeLibrary.GetExport(handle, "V"));
                Assert.Equal((byte)1, boolean(0));
                Assert.Equal((byte)0, boolean(1));
                Assert.Equal((ushort)0xD800, character(0xD800));
                Assert.Equal(42, signed(20, 22));
                Assert.Equal(uint.MaxValue, unsigned(0, uint.MaxValue));
                Assert.Equal(0, wideSigned(long.MinValue, -1));
                Assert.Equal(ulong.MaxValue, wideUnsigned(ulong.MaxValue, 1));
                noResult();
            }
            finally
            {
                NativeLibrary.Free(handle);
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate byte BoolIdentity(byte value);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate ushort CharIdentity(ushort value);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int IntBinary(int left, int right);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate uint UIntBinary(uint left, uint right);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate long LongBinary(long left, long right);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate ulong ULongBinary(ulong left, ulong right);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void VoidAction();

    [Theory]
    [InlineData("public void V() { }")]
    [InlineData("public void V() { return; }")]
    public void VoidBodyHasOneTerminatorAfterBinderNormalization(string source)
    {
        var loaded = TrRmExecutableCompiler.Load(TrRmExecutableCompiler.Emit(Compile(source, true), "void"));
        Assert.Single(loaded.Functions[0].Body.Statements);
        Assert.Null(Assert.IsType<BoundReturn>(loaded.Functions[0].Body.Statements[0]).Value);
    }

    [Fact]
    public void RequiredSourceProofsAreDischargedBeforeEmission()
    {
        const string source = """
            [ConstEval] int32 Identity(int32 value) { return value; }
            int32 Main() { static require true; return const Identity(42); }
            """;
        var loaded = TrRmExecutableCompiler.Load(TrRmExecutableCompiler.Emit(Compile(source), "proven"));
        Assert.Equal(42, NativeJit.Execute(loaded, NativeRuntimePolicy.FailFast));
    }

    [Fact]
    public void SourceFreeIndexedCallsPreserveNamedEvaluationAndFormalOrder()
    {
        const string source = """
            int32 Pair(int32 first, int32 second) { return first * 10 + second; }
            int32 Main() { mut int32 trace = 0; return Pair(second: trace++, first: trace); }
            """;
        var original = Compile(source);
        var artifact = TrRmExecutableCompiler.Emit(original, "named-arguments");
        var loaded = TrRmExecutableCompiler.Load(artifact);
        Assert.Equal(10, NativeJit.Execute(original, NativeRuntimePolicy.FailFast));
        Assert.Equal(10, NativeJit.Execute(loaded, NativeRuntimePolicy.FailFast));
        Assert.NotEmpty(NativeEmitter.Emit(loaded, NativeRuntimePolicy.FailFast));
    }

    [Fact]
    public async Task SourceFreeOptionalScalarBodyExecutesPresenceCopySelectAndAot()
    {
        var body = OptionalLinearBody(
            [2, 4, 6, 8],
            [
                1, 1, 3, 0,
                8, 2, 4, 1,
                9, 3, 4,
                3, 4, 2,
                0x0A, 5, 4,
                0x0A, 6, 3,
                7, 7, 5, 4, 3,
                0x0A, 8, 7,
                1, 9, 2, 42,
                1, 10, 2, 0,
                7, 11, 8, 9, 10,
                7, 12, 6, 4, 3,
                0x0A, 13, 12,
                7, 14, 13, 9, 10,
                0x15, 15, 1, 11, 14,
            ],
            15, 15);
        var loaded = TrRmExecutableCompiler.Load(OptionalScalarArtifact(body,
            optionalChar16: false, optionalInt32: false, optionalInt64: true));

        Assert.Equal(42, NativeJit.Execute(loaded, NativeRuntimePolicy.FailFast));
        Assert.Equal(42, await StaticProtocolTests.RunAsync(
            NativeEmitter.Emit(loaded, NativeRuntimePolicy.FailFast)).ConfigureAwait(true));
    }

    [Theory]
    [InlineData(2, true, 42)]
    [InlineData(2, false, 0)]
    [InlineData(4, true, 42)]
    [InlineData(4, false, 0)]
    [InlineData(5, true, 42)]
    [InlineData(5, false, 0)]
    [InlineData(7, true, 42)]
    [InlineData(7, false, 0)]
    public async Task SourceFreeOptionalScalarPayloadTypesPreservePresenceInAotAndJit(
        byte payloadTypeCode, bool present, int expected)
    {
        var typeRows = new[] { 2, 4, (int)payloadTypeCode, 8 }
            .Distinct().ToArray();
        var payloadTypeId = Array.IndexOf(typeRows, payloadTypeCode) + 1;
        var int32TypeId = Array.IndexOf(typeRows, 4) + 1;
        var optionalTypeId = Array.IndexOf(typeRows, 8) + 1;
        var operations = new List<byte> { 1, 1, (byte)payloadTypeId, 0 };
        if (present)
        {
            operations.AddRange([8, 2, (byte)optionalTypeId, 1]);
        }
        else
        {
            operations.AddRange([9, 2, (byte)optionalTypeId]);
        }
        operations.AddRange([
            3, 3, 2,
            0x0A, 4, 3,
            1, 5, (byte)int32TypeId, 42,
            1, 6, (byte)int32TypeId, 0,
            7, 7, 4, 5, 6,
        ]);
        var body = OptionalLinearBody(typeRows, [.. operations], 7, 7);
        var loaded = TrRmExecutableCompiler.Load(OptionalScalarArtifact(body,
            optionalChar16: false,
            optionalInt32: payloadTypeCode == 4,
            optionalInt64: false,
            optionalBool: payloadTypeCode == 2,
            optionalUInt32: payloadTypeCode == 5,
            optionalUInt64: payloadTypeCode == 7));

        Assert.Equal(expected, NativeJit.Execute(loaded, NativeRuntimePolicy.FailFast));
        Assert.Equal(expected, await StaticProtocolTests.RunAsync(
            NativeEmitter.Emit(loaded, NativeRuntimePolicy.FailFast)).ConfigureAwait(true));
    }

    [Fact]
    public async Task SourceFreeOptionalChar16EqualityDistinguishesPresenceAndPayload()
    {
        var body = OptionalLinearBody(
            [2, 4, 3, 8],
            [
                1, 1, 3, 65,
                1, 2, 3, 66,
                8, 3, 4, 1,
                8, 4, 4, 1,
                8, 5, 4, 2,
                5, 6, 4, 3, 4,
                5, 7, 5, 3, 5,
                9, 8, 4,
                5, 9, 4, 8, 8,
                5, 10, 5, 3, 8,
                1, 11, 2, 42,
                1, 12, 2, 0,
                7, 13, 6, 11, 12,
                7, 14, 7, 11, 12,
                7, 15, 9, 11, 12,
                7, 16, 10, 11, 12,
                0x15, 17, 1, 13, 14,
                0x15, 18, 1, 17, 15,
                0x15, 19, 1, 18, 16,
            ],
            19, 19);
        var loaded = TrRmExecutableCompiler.Load(OptionalScalarArtifact(body,
            optionalChar16: true, optionalInt32: false));

        Assert.Equal(168, NativeJit.Execute(loaded, NativeRuntimePolicy.FailFast));
        Assert.Equal(168, await StaticProtocolTests.RunAsync(
            NativeEmitter.Emit(loaded, NativeRuntimePolicy.FailFast)).ConfigureAwait(true));
    }

    [Theory]
    [InlineData(false, 168)]
    [InlineData(true, 169)]
    public async Task IndexedOptionalChar16EqualityAndSelectExecuteThroughNativeGraph(
        bool selectPresent, int expected)
    {
        var loaded = TrRmExecutableCompiler.Load(OptionalScalarArtifact(
            IndexedOptionalChar16Body(selectPresent),
            optionalChar16: true, optionalInt32: false));

        Assert.Equal(expected, NativeJit.Execute(loaded, NativeRuntimePolicy.FailFast));
        Assert.Equal(expected, await StaticProtocolTests.RunAsync(
            NativeEmitter.Emit(loaded, NativeRuntimePolicy.FailFast)).ConfigureAwait(true));
    }

    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 1)]
    public async Task IndexedOptionalChar16ComparisonResultsAreCanonicalInSelect(
        bool selectFirst, int expected)
    {
        var loaded = TrRmExecutableCompiler.Load(OptionalScalarArtifact(
            IndexedOptionalChar16SelectionBody(selectFirst),
            optionalChar16: true, optionalInt32: false));

        Assert.Equal(expected, NativeJit.Execute(loaded, NativeRuntimePolicy.FailFast));
        Assert.Equal(expected, await StaticProtocolTests.RunAsync(
            NativeEmitter.Emit(loaded, NativeRuntimePolicy.FailFast)).ConfigureAwait(true));
    }

    [Theory]
    [InlineData(2, false, 0)]
    [InlineData(2, true, 42)]
    [InlineData(3, false, 0)]
    [InlineData(3, true, 42)]
    [InlineData(4, false, 0)]
    [InlineData(4, true, 42)]
    [InlineData(5, false, 0)]
    [InlineData(5, true, 42)]
    [InlineData(6, false, 0)]
    [InlineData(6, true, 42)]
    [InlineData(7, false, 0)]
    [InlineData(7, true, 42)]
    public async Task IndexedOptionalSelectionPreservesPresenceForEveryPayload(
        int payloadTypeSpecRow, bool selectPresent, int expected)
    {
        var body = IndexedOptionalPresenceBody(payloadTypeSpecRow, selectPresent);
        var artifact = payloadTypeSpecRow switch
        {
            2 => OptionalScalarArtifact(body, optionalChar16: false, optionalInt32: false,
                optionalBool: true),
            3 => OptionalScalarArtifact(body, optionalChar16: true, optionalInt32: false),
            4 => OptionalScalarArtifact(body, optionalChar16: false, optionalInt32: true),
            5 => OptionalScalarArtifact(body, optionalChar16: false, optionalInt32: false,
                optionalUInt32: true),
            6 => OptionalScalarArtifact(body, optionalChar16: false, optionalInt32: false,
                optionalInt64: true),
            7 => OptionalScalarArtifact(body, optionalChar16: false, optionalInt32: false,
                optionalUInt64: true),
            _ => throw new ArgumentOutOfRangeException(nameof(payloadTypeSpecRow)),
        };
        var loaded = TrRmExecutableCompiler.Load(artifact);

        Assert.Equal(expected, NativeJit.Execute(loaded, NativeRuntimePolicy.FailFast));
        Assert.Equal(expected, await StaticProtocolTests.RunAsync(
            NativeEmitter.Emit(loaded, NativeRuntimePolicy.FailFast)).ConfigureAwait(true));
    }

    [Fact]
    public async Task IndexedOptionalEdgePreservesIndirectPayloadAndPresence()
    {
        var loaded = TrRmExecutableCompiler.Load(OptionalScalarArtifact(
            IndexedOptionalTransferBody(), optionalChar16: false,
            optionalInt32: false, optionalInt64: true));

        Assert.Equal(42, NativeJit.Execute(loaded, NativeRuntimePolicy.FailFast));
        Assert.Equal(42, await StaticProtocolTests.RunAsync(
            NativeEmitter.Emit(loaded, NativeRuntimePolicy.FailFast)).ConfigureAwait(true));
    }

    [Fact]
    public void MaterializedValueLimitRejectsWithoutDroppingOperations()
    {
        var source = "int32 Main() { mut int32 x = 0; "
            + string.Concat(Enumerable.Repeat("x = x + 1; ", 128)) + "return x; }";
        Assert.Throws<NotSupportedException>(() => TrRmExecutableCompiler.Emit(Compile(source), "too-many-values"));
    }

    [Fact]
    public void BodyLocalWidthAndInstructionReferenceWidthWidenIndependently()
    {
        var source = new StringBuilder("int32 Main() { mut int32 x = 0; ");
        for (var i = 0; i < 127; i++)
        {
            source.Append("x = x + 1; ");
        }
        source.Append("x = +x; return x; }");
        var artifact = TrRmExecutableCompiler.Emit(Compile(source.ToString()), "wide-values");
        var index = new TrRmArtifactIndex(artifact);
        Assert.True(index.TryGetSection(0x20, out var bodyBytes, out var count));
        var bodies = new TrRmBodyTableReader(bodyBytes, count);
        Assert.Equal((byte)0x05, bodies.GetRow(1)[0]);
        Assert.Equal(127, NativeJit.Execute(TrRmExecutableCompiler.Load(artifact),
            NativeRuntimePolicy.FailFast));
    }

    [Fact]
    public void TamperedPolicyAndUnknownSectionsNeverBecomeExecutable()
    {
        var bytes = TrRmExecutableCompiler.Emit(Compile("int32 Main() { return 42; }"), "tamper");
        var index = new TrRmArtifactIndex(bytes);
        for (var i = 0; i < index.SectionCount; i++)
        {
            index.GetSection(i, out var kind, out _, out _, out _);
            if (kind != 1)
            {
                continue;
            }
            var offset = (int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(32 + i * 16 + 8));
            bytes[offset + 8] = 1;
            Assert.Throws<InvalidDataException>(() => TrRmExecutableCompiler.Load(bytes));
            bytes[offset + 8] = 0;
            bytes[offset + 4] = 0;
            Assert.Throws<InvalidDataException>(() => TrRmExecutableCompiler.Load(bytes));
            return;
        }
        throw new InvalidOperationException("Module section missing.");
    }

    private static byte[] OptionalLinearBody(int[] typeRows, byte[] operations,
        int operationCount, int returnValue)
    {
        var body = new List<byte> { 0x04, (byte)typeRows.Length };
        foreach (var row in typeRows)
        {
            var bytes = new byte[sizeof(uint)];
            BinaryPrimitives.WriteUInt32LittleEndian(bytes, (uint)row);
            body.AddRange(bytes);
        }
        body.Add((byte)operationCount);
        body.AddRange(operations);
        body.Add(0x83);
        body.Add((byte)returnValue);
        return body.ToArray();
    }

    private static byte[] IndexedOptionalTransferBody()
    {
        var firstBlock = new byte[]
        {
            0x18, 1, 2, 2, 1, 1, 2, 2, 2,
            1, 1, 1, 0,
            8, 2, 2, 1,
            0x80, 2, 1, 2,
        };
        var secondBlock = new byte[]
        {
            0x12, 1, 3, 5, 3, 0, 4, 3, 5, 3, 6, 3, 7, 4, 4,
            0x0A, 4, 3,
            1, 5, 4, 42,
            1, 6, 4, 0,
            7, 7, 4, 5, 6,
            0x83, 7,
        };
        var body = new List<byte> { 0x08, 2, 7, 0, 4 };
        foreach (var row in new[] { 6, 8, 2, 4 })
        {
            var bytes = new byte[sizeof(uint)];
            BinaryPrimitives.WriteUInt32LittleEndian(bytes, (uint)row);
            body.AddRange(bytes);
        }
        body.AddRange([1, 1, 2, 1, 2, 1, 3, 1, 4, 1, 4, 1, 4, 1]);
        body.AddRange([0, 2, 7]);
        body.AddRange([9, 13, 0xFF, 15, 18, 22, 26]);
        body.AddRange([0, (byte)firstBlock.Length,
            (byte)(firstBlock.Length + secondBlock.Length)]);
        body.AddRange([17, 31]);
        body.AddRange(firstBlock);
        body.AddRange(secondBlock);
        return body.ToArray();
    }

    private static byte[] IndexedOptionalChar16Body(bool selectPresent)
    {
        byte[] valueTypeIds =
        [
            3, 3, 4, 4, 4, 1, 1, 4, 1, 1,
            2, 2, 2, 2, 2, 2, 2, 2, 2, 1,
            4, 4, 1, 2, 2, 2, 2,
        ];
        byte[] lastUseOrdinals =
        [
            3, 4, 22, 5, 20, 12, 13, 9, 14, 15,
            15, 15, 16, 16, 17, 18, 17, 18, 26, 20,
            21, 22, 25, 25, 25, 26, 27,
        ];
        byte[][] operations =
        [
            [1, 1, 3, 65],
            [1, 2, 3, 66],
            [8, 3, 4, 1],
            [8, 4, 4, 1],
            [8, 5, 4, 2],
            [5, 6, 4, 3, 4],
            [5, 7, 5, 3, 5],
            [9, 8, 4],
            [5, 9, 4, 8, 8],
            [5, 10, 5, 3, 8],
            [1, 11, 2, 42],
            [1, 12, 2, 0],
            [7, 13, 6, 11, 12],
            [7, 14, 7, 11, 12],
            [7, 15, 9, 11, 12],
            [7, 16, 10, 11, 12],
            [0x15, 17, 1, 13, 14],
            [0x15, 18, 1, 17, 15],
            [0x15, 19, 1, 18, 16],
            [1, 20, 1, (byte)(selectPresent ? 1 : 0)],
            [7, 21, 20, 3, 5],
            [3, 22, 21],
            [5, 23, 4, 22, 3],
            [1, 24, 2, 1],
            [1, 25, 2, 0],
            [7, 26, 23, 24, 25],
            [0x15, 27, 1, 19, 26],
        ];
        var block = new List<byte> { 16, (byte)valueTypeIds.Length };
        for (var value = 0; value < valueTypeIds.Length; value++)
        {
            block.Add((byte)(value + 1));
            block.Add(lastUseOrdinals[value]);
        }
        block.Add((byte)operations.Length);
        var operationOffsets = new byte[operations.Length];
        for (var operation = 0; operation < operations.Length; operation++)
        {
            operationOffsets[operation] = (byte)block.Count;
            block.AddRange(operations[operation]);
        }
        var terminatorOffset = (byte)block.Count;
        block.Add(0x83);
        block.Add(27);

        var body = new List<byte> { 0x08, 1, (byte)valueTypeIds.Length, 0, 4 };
        foreach (var row in new[] { 2, 4, 3, 8 })
        {
            var bytes = new byte[sizeof(uint)];
            BinaryPrimitives.WriteUInt32LittleEndian(bytes, (uint)row);
            body.AddRange(bytes);
        }
        foreach (var typeId in valueTypeIds)
        {
            body.Add(typeId);
            body.Add(1);
        }
        body.AddRange([0, (byte)valueTypeIds.Length]);
        body.AddRange(operationOffsets);
        body.AddRange([0, (byte)block.Count, terminatorOffset]);
        body.AddRange(block);
        return body.ToArray();
    }

    private static byte[] IndexedOptionalChar16SelectionBody(bool selectFirst)
    {
        byte[] valueTypeIds = [3, 3, 4, 4, 1, 4, 1, 1, 2, 2, 2, 2, 2];
        byte[] lastUseOrdinals = [2, 3, 7, 5, 5, 7, 10, 11, 11, 11, 12, 12, 13];
        byte[][] operations =
        [
            [1, 1, 3, 65],
            [1, 2, 3, 66],
            [8, 3, 4, 1],
            [8, 4, 4, 2],
            [1, 5, 1, (byte)(selectFirst ? 1 : 0)],
            [7, 6, 5, 3, 4],
            [5, 7, 4, 6, 3],
            [5, 8, 5, 6, 3],
            [1, 9, 2, 1],
            [1, 10, 2, 0],
            [7, 11, 7, 9, 10],
            [7, 12, 8, 9, 10],
            [0x15, 13, 1, 11, 12],
        ];
        var block = new List<byte> { 16, (byte)valueTypeIds.Length };
        for (var value = 0; value < valueTypeIds.Length; value++)
        {
            block.Add((byte)(value + 1));
            block.Add(lastUseOrdinals[value]);
        }
        block.Add((byte)operations.Length);
        var operationOffsets = new byte[operations.Length];
        for (var operation = 0; operation < operations.Length; operation++)
        {
            operationOffsets[operation] = (byte)block.Count;
            block.AddRange(operations[operation]);
        }
        var terminatorOffset = (byte)block.Count;
        block.Add(0x83);
        block.Add(13);

        var body = new List<byte> { 0x08, 1, (byte)valueTypeIds.Length, 0, 4 };
        foreach (var row in new[] { 2, 4, 3, 8 })
        {
            var bytes = new byte[sizeof(uint)];
            BinaryPrimitives.WriteUInt32LittleEndian(bytes, (uint)row);
            body.AddRange(bytes);
        }
        foreach (var typeId in valueTypeIds)
        {
            body.Add(typeId);
            body.Add(1);
        }
        body.AddRange([0, (byte)valueTypeIds.Length]);
        body.AddRange(operationOffsets);
        body.AddRange([0, (byte)block.Count, terminatorOffset]);
        body.AddRange(block);
        return body.ToArray();
    }

    private static byte[] IndexedOptionalPresenceBody(int payloadTypeSpecRow,
        bool selectPresent, int optionalTypeSpecRow = 8)
    {
        var typeRows = new SortedSet<int> { 2, 4, payloadTypeSpecRow, optionalTypeSpecRow }.ToArray();
        var booleanTypeId = (byte)(Array.IndexOf(typeRows, 2) + 1);
        var integerTypeId = (byte)(Array.IndexOf(typeRows, 4) + 1);
        var payloadTypeId = (byte)(Array.IndexOf(typeRows, payloadTypeSpecRow) + 1);
        var optionalTypeId = (byte)(Array.IndexOf(typeRows, optionalTypeSpecRow) + 1);
        byte[] valueTypeIds =
        [
            payloadTypeId, optionalTypeId, optionalTypeId, booleanTypeId, optionalTypeId,
            optionalTypeId, booleanTypeId, integerTypeId, integerTypeId, integerTypeId,
        ];
        byte[] lastUseOrdinals = [1, 4, 4, 4, 5, 6, 9, 9, 9, 10];
        byte[][] operations =
        [
            [1, 1, payloadTypeId, 0],
            [8, 2, optionalTypeId, 1],
            [9, 3, optionalTypeId],
            [1, 4, booleanTypeId, (byte)(selectPresent ? 1 : 0)],
            [7, 5, 4, 2, 3],
            [3, 6, 5],
            [0x0A, 7, 6],
            [1, 8, integerTypeId, 42],
            [1, 9, integerTypeId, 0],
            [7, 10, 7, 8, 9],
        ];
        var block = new List<byte> { 16, (byte)valueTypeIds.Length };
        for (var value = 0; value < valueTypeIds.Length; value++)
        {
            block.Add((byte)(value + 1));
            block.Add(lastUseOrdinals[value]);
        }
        block.Add((byte)operations.Length);
        var operationOffsets = new byte[operations.Length];
        for (var operation = 0; operation < operations.Length; operation++)
        {
            operationOffsets[operation] = (byte)block.Count;
            block.AddRange(operations[operation]);
        }
        var terminatorOffset = (byte)block.Count;
        block.Add(0x83);
        block.Add(10);

        var body = new List<byte> { 0x08, 1, (byte)valueTypeIds.Length, 0, (byte)typeRows.Length };
        foreach (var row in typeRows)
        {
            var bytes = new byte[sizeof(uint)];
            BinaryPrimitives.WriteUInt32LittleEndian(bytes, (uint)row);
            body.AddRange(bytes);
        }
        foreach (var typeId in valueTypeIds)
        {
            body.Add(typeId);
            body.Add(1);
        }
        body.AddRange([0, (byte)valueTypeIds.Length]);
        body.AddRange(operationOffsets);
        body.AddRange([0, (byte)block.Count, terminatorOffset]);
        body.AddRange(block);
        return body.ToArray();
    }

    private static byte[] OptionalScalarArtifact(byte[] body, bool optionalChar16,
        bool optionalInt32, bool optionalInt64 = false, bool optionalBool = false,
        bool optionalUInt32 = false, bool optionalUInt64 = false, bool optionalFloat32 = false)
    {
        var original = TrRmExecutableCompiler.Emit(
            Compile("int32 Main() { return 0; }"), "optional-body");
        var index = new TrRmArtifactIndex(original);
        var rows = new List<ReadOnlyMemory<byte>>();
        index.TryGetSection(0x11, out var typeBytes, out var typeCount);
        var existingTypes = new TrRmTypeSpecTableReader(typeBytes, typeCount, 0, 0, 0, 0);
        for (var row = 1; row <= typeCount; row++)
        {
            rows.Add(existingTypes.GetRow(row).ToArray());
        }
        if (optionalChar16)
        {
            rows.Add(TrRmTypeSpecTableWriter.Wrapped(0x06, 3));
        }
        if (optionalInt32)
        {
            rows.Add(TrRmTypeSpecTableWriter.Wrapped(0x06, 4));
        }
        if (optionalInt64)
        {
            rows.Add(TrRmTypeSpecTableWriter.Wrapped(0x06, 6));
        }
        if (optionalBool)
        {
            rows.Add(TrRmTypeSpecTableWriter.Wrapped(0x06, 2));
        }
        if (optionalUInt32)
        {
            rows.Add(TrRmTypeSpecTableWriter.Wrapped(0x06, 5));
        }
        if (optionalUInt64)
        {
            rows.Add(TrRmTypeSpecTableWriter.Wrapped(0x06, 7));
        }
        if (optionalFloat32)
        {
            rows.Add(TrRmTypeSpecTableWriter.Builtin(0x14));
            rows.Add(TrRmTypeSpecTableWriter.Wrapped(0x06, (uint)rows.Count));
        }
        var rewrittenTypes = TrRmTypeSpecTableWriter.Encode(rows, 0, 0, 0, 0);
        var rewrittenBody = TrRmVariableTableWriter.EncodeBodies([body]);
        var sections = new List<TrRmSection>(index.SectionCount);
        for (var section = 0; section < index.SectionCount; section++)
        {
            index.GetSection(section, out var kind, out var flags, out var bytes, out var count);
            sections.Add(new TrRmSection(kind, flags, (uint)(kind switch
            {
                0x11 => rows.Count,
                _ => count,
            }), kind switch
            {
                0x11 => rewrittenTypes,
                0x20 => rewrittenBody,
                _ => bytes.ToArray(),
            }));
        }
        return TrRmArtifactWriter.Encode(sections);
    }

    private static byte[] ReplaceBodyConstant(byte[] artifact, int bodyRow,
        byte[] originalBits, byte[] replacementBits)
    {
        Assert.Equal(originalBits.Length, replacementBits.Length);
        var index = new TrRmArtifactIndex(artifact);
        Assert.True(index.TryGetSection(0x20, out var bodyBytes, out var bodyCount));
        var bodies = new TrRmBodyTableReader(bodyBytes, bodyCount);
        var bodyRows = new ReadOnlyMemory<byte>[bodyCount];
        for (var row = 1; row <= bodyCount; row++)
        {
            bodyRows[row - 1] = bodies.GetRow(row).ToArray();
        }

        var body = bodyRows[bodyRow - 1].ToArray();
        var offset = body.AsSpan().IndexOf(originalBits);
        Assert.True(offset >= 0);
        replacementBits.CopyTo(body.AsSpan(offset));
        bodyRows[bodyRow - 1] = body;
        var rewrittenBodies = TrRmVariableTableWriter.EncodeBodies(bodyRows);
        var sections = new List<TrRmSection>(index.SectionCount);
        for (var section = 0; section < index.SectionCount; section++)
        {
            index.GetSection(section, out var kind, out var flags, out var bytes, out var count);
            sections.Add(new TrRmSection(kind, flags, (uint)count,
                kind == 0x20 ? rewrittenBodies : bytes.ToArray()));
        }
        return TrRmArtifactWriter.Encode(sections);
    }
}
