// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
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
    [InlineData("""
        int32 Fill(int32 prefix, init ref<int32> result, int32 suffix) {
            result = prefix + suffix;
            return suffix;
        }
        int32 Main() {
            init int32 result;
            int32 status = Fill(20, init result, 2);
            return result + status;
        }
        """, 24)]
    [InlineData("""
        void Fill(init ref<int32> first, init ref<int32> second) {
            first = 40;
            second = 2;
        }
        int32 Main() {
            init int32 first;
            init int32 second;
            Fill(init first, init second);
            return first + second;
        }
        """, 42)]
    [InlineData("""
        void Store(bool flag, char16 character, uint32 unsigned32, int64 signed64,
            uint64 unsigned64, float32 single, float64 wide, init ref<int32> result) {
            result = 42;
        }
        bool GetFlag() { return true; }
        char16 GetCharacter() { return char16(65); }
        uint32 GetUnsigned32() { return uint32(3); }
        int64 GetSigned64() { return int64(4); }
        uint64 GetUnsigned64() { return uint64(5); }
        float32 GetSingle() { return float32(1.5); }
        float64 GetWide() { return float64(2.5); }
        int32 Main() {
            init int32 result;
            Store(GetFlag(), GetCharacter(), GetUnsigned32(), GetSigned64(),
                GetUnsigned64(), GetSingle(), GetWide(), init result);
            return result;
        }
        """, 42)]
    public void InitOutputCallsRoundTripWithFeatureAndPublishOnNormalReturn(string source, int expected)
    {
        var original = Compile(source);
        var artifact = TrRmExecutableCompiler.Emit(original, "init-output");
        var index = new TrRmArtifactIndex(artifact);

        Assert.True(index.TryGetSection(4, out var featureBytes, out var featureCount));
        Assert.Equal(1, featureCount);
        var features = new TrRmFeatureTableReader(featureBytes, featureCount);
        Assert.Equal(TrRmFeatureRow.ScalarInitOutput, features.GetFeature(1));

        var loaded = TrRmExecutableCompiler.Load(artifact);
        Assert.Equal(expected, NativeJit.Execute(original, NativeRuntimePolicy.FailFast));
        Assert.Equal(expected, NativeJit.Execute(loaded, NativeRuntimePolicy.FailFast));
        Assert.NotEmpty(NativeEmitter.Emit(loaded, NativeRuntimePolicy.FailFast));
    }

    [Theory]
    [InlineData("TrueValue() == TrueValue()", 42)]
    [InlineData("TrueValue() != FalseValue()", 42)]
    [InlineData("FalseValue() == TrueValue()", 0)]
    [InlineData("CharacterA() == CharacterA()", 42)]
    [InlineData("CharacterA() != CharacterB()", 42)]
    [InlineData("Signed32One() == Signed32One()", 42)]
    [InlineData("Signed32One() != Signed32Two()", 42)]
    [InlineData("Signed32One() < Signed32Two()", 42)]
    [InlineData("Signed32Two() <= Signed32Two()", 42)]
    [InlineData("Signed32Two() > Signed32One()", 42)]
    [InlineData("Signed32Two() >= Signed32Two()", 42)]
    [InlineData("Signed32Two() < Signed32One()", 0)]
    [InlineData("Unsigned32One() < Unsigned32Two()", 42)]
    [InlineData("Unsigned32Two() >= Unsigned32Two()", 42)]
    [InlineData("Signed64Two() > Signed64One()", 42)]
    [InlineData("Signed64One() <= Signed64One()", 42)]
    [InlineData("Unsigned64Two() > Unsigned64One()", 42)]
    [InlineData("SingleOne() == SingleOne()", 42)]
    [InlineData("SingleOne() != SingleTwo()", 42)]
    [InlineData("SingleOne() < SingleTwo()", 42)]
    [InlineData("SingleOne() <= SingleOne()", 42)]
    [InlineData("SingleTwo() > SingleOne()", 42)]
    [InlineData("SingleOne() >= SingleOne()", 42)]
    [InlineData("WideOne() == WideOne()", 42)]
    [InlineData("WideOne() != WideTwo()", 42)]
    [InlineData("WideOne() < WideTwo()", 42)]
    [InlineData("WideOne() <= WideOne()", 42)]
    [InlineData("WideTwo() > WideOne()", 42)]
    [InlineData("WideOne() >= WideOne()", 42)]
    public void ScalarComparisonsRoundTripInOutputBodies(string comparison, int expected)
    {
        var source = $$"""
            void Fill(int32 value, init ref<int32> result) { result = value; }
            int32 Observe(bool value) { return value ? 42 : 0; }
            bool TrueValue() { return true; }
            bool FalseValue() { return false; }
            char16 CharacterA() { return char16(65); }
            char16 CharacterB() { return char16(66); }
            int32 Signed32One() { return 1; }
            int32 Signed32Two() { return 2; }
            uint32 Unsigned32One() { return uint32(1); }
            uint32 Unsigned32Two() { return uint32(2); }
            int64 Signed64One() { return int64(1); }
            int64 Signed64Two() { return int64(2); }
            uint64 Unsigned64One() { return uint64(1); }
            uint64 Unsigned64Two() { return uint64(2); }
            float32 SingleOne() { return float32(1.0); }
            float32 SingleTwo() { return float32(2.0); }
            float64 WideOne() { return float64(1.0); }
            float64 WideTwo() { return float64(2.0); }
            int32 Main() {
                init int32 result;
                Fill(Observe({{comparison}}), init result);
                return result;
            }
            """;
        var original = Compile(source);
        var loaded = TrRmExecutableCompiler.Load(
            TrRmExecutableCompiler.Emit(original, "output-comparisons"));

        Assert.Equal(expected, NativeJit.Execute(original, NativeRuntimePolicy.FailFast));
        Assert.Equal(expected, NativeJit.Execute(loaded, NativeRuntimePolicy.FailFast));
        Assert.NotEmpty(NativeEmitter.Emit(loaded, NativeRuntimePolicy.FailFast));
    }

    [Fact]
    public void OutputBodyCallsEncodeWideMethodReferenceSeparatelyFromArgumentCount()
    {
        var fillers = string.Join(Environment.NewLine,
            Enumerable.Range(0, 255).Select(static index => $"void Helper{index}() {{ return; }}"));
        var source = $$"""
            {{fillers}}
            void Target(int32 value) { return; }
            int32 Main() {
                init int32 result;
                int32 value = 1;
                Target(value);
                result = 42;
                return result;
            }
            """;
        var loaded = TrRmExecutableCompiler.Load(
            TrRmExecutableCompiler.Emit(Compile(source), "wide-output-method-reference"));

        Assert.Equal(42, NativeJit.Execute(loaded, NativeRuntimePolicy.FailFast));
    }

    [Fact]
    public void InitOutputParameterRequiresModuleFeatureAssignment()
    {
        var artifact = TrRmExecutableCompiler.Emit(Compile("""
            void Fill(init ref<int32> result) { result = 42; }
            int32 Main() {
                init int32 result;
                Fill(init result);
                return result;
            }
            """), "missing-feature");
        var index = new TrRmArtifactIndex(artifact);
        for (var section = 0; section < index.SectionCount; section++)
        {
            index.GetSection(section, out var kind, out _, out _, out _);
            if (kind != 1)
            {
                continue;
            }

            var offset = (int)BinaryPrimitives.ReadUInt32LittleEndian(
                artifact.AsSpan(32 + section * 16 + 8));
            BinaryPrimitives.WriteUInt32LittleEndian(artifact.AsSpan(offset + 16), 0);
            BinaryPrimitives.WriteUInt32LittleEndian(artifact.AsSpan(offset + 20), 0);
            Assert.Throws<InvalidDataException>(() => TrRmExecutableCompiler.Load(artifact));
            return;
        }

        throw new InvalidOperationException("Module section missing.");
    }

    [Fact]
    public void ExportedInitOutputFunctionRemainsOutsideTheProvisionalProfile()
    {
        const string source = """
            public void Fill(init ref<int32> result) { result = 42; }
            int32 Main() {
                init int32 result;
                Fill(init result);
                return result;
            }
            """;

        Assert.Throws<NotSupportedException>(() =>
            TrRmExecutableCompiler.Emit(Compile(source), "exported-output"));
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

    [Fact]
    public void FloatingExpressionOnlyHelperKeepsFloatingTypesInIndexedModule()
    {
        const string source = """
            int32 Main() { return IsLess() ? 42 : 0; }
            bool IsLess() { return Less(1.5, 2.0); }
            bool Less(float64 left, float64 right) { return left < right; }
            """;
        var original = Compile(source);
        var artifact = TrRmExecutableCompiler.Emit(original, "indexed-float-expression");
        var loaded = TrRmExecutableCompiler.Load(artifact);

        Assert.Equal(42, NativeJit.Execute(original, NativeRuntimePolicy.FailFast));
        Assert.Equal(42, NativeJit.Execute(loaded, NativeRuntimePolicy.FailFast));
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
    public void FloatingRemainderRemainsUnsupported()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(
            "public float32 Remainder(float32 left, float32 right) { return left % right; }"));

        Assert.True(compilation.HasErrors);
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
        var malformed = ReplaceBodyOperationByte(artifact, 2, 0x19, 2, kind);

        Assert.Throws<InvalidDataException>(() => TrRmExecutableCompiler.Load(malformed));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(7)]
    public void FloatConvertRejectsInvalidDestinationTypeSpecs(byte destinationTypeId)
    {
        const string source = "public float64 Widen(float32 value) { return float64(value); }";
        var artifact = TrRmExecutableCompiler.Emit(Compile(source, true), "invalid-float-convert");
        var malformed = ReplaceBodyOperationByte(artifact, 1, 0x1A, 2, destinationTypeId);

        Assert.Throws<InvalidDataException>(() => TrRmExecutableCompiler.Load(malformed));
    }

    [Fact]
    public void FloatConvertRejectsConstructedDestinationTypeSpecs()
    {
        const string source = "public float64 Widen(float32 value) { return float64(value); }";
        var artifact = TrRmExecutableCompiler.Emit(Compile(source, true), "constructed-float-convert");
        var index = new TrRmArtifactIndex(artifact);
        Assert.True(index.TryGetSection(0x11, out var typeBytes, out var typeCount));
        var originalTypes = new TrRmTypeSpecTableReader(typeBytes, typeCount, 0, 0, 0, 0);
        var typeRows = new List<ReadOnlyMemory<byte>>(typeCount + 1);
        var int32Row = 0;
        var float64Row = 0;
        var int32Type = TrRmTypeSpecTableWriter.Builtin(0x08);
        var float64Type = TrRmTypeSpecTableWriter.Builtin(0x15);
        for (var row = 1; row <= typeCount; row++)
        {
            var type = originalTypes.GetRow(row);
            typeRows.Add(type.ToArray());
            if (type.SequenceEqual(int32Type))
            {
                int32Row = row;
            }
            if (type.SequenceEqual(float64Type))
            {
                float64Row = row;
            }
        }
        Assert.NotEqual(0, int32Row);
        Assert.NotEqual(0, float64Row);
        typeRows.Add(TrRmTypeSpecTableWriter.Wrapped(0x06, (uint)int32Row));
        var constructedTypeRow = typeRows.Count;
        var rewrittenTypes = TrRmTypeSpecTableWriter.Encode(typeRows, 0, 0, 0, 0);

        Assert.True(index.TryGetSection(0x20, out var bodyBytes, out var bodyCount));
        var originalBodies = new TrRmBodyTableReader(bodyBytes, bodyCount);
        var bodyRows = new ReadOnlyMemory<byte>[bodyCount];
        for (var row = 1; row <= bodyCount; row++)
        {
            bodyRows[row - 1] = originalBodies.GetRow(row).ToArray();
        }
        var body = bodyRows[0].ToArray();
        var localWidth = 1 << (body[0] & 3);
        Assert.Equal(1, localWidth);
        var typeMapCount = body[1];
        var conversionOffset = body.AsSpan().IndexOf((byte)0x1A);
        Assert.True(conversionOffset >= 0);
        var destinationTypeId = body[conversionOffset + 2];
        Assert.InRange(destinationTypeId, 1, typeMapCount);
        var typeMapOffset = 1 + localWidth + (destinationTypeId - 1) * sizeof(int);
        Assert.Equal(float64Row, BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(typeMapOffset)));
        BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(typeMapOffset), constructedTypeRow);
        bodyRows[0] = body;
        var rewrittenBodies = TrRmVariableTableWriter.EncodeBodies(bodyRows);

        var sections = new List<TrRmSection>(index.SectionCount);
        for (var section = 0; section < index.SectionCount; section++)
        {
            index.GetSection(section, out var kind, out var flags, out var bytes, out var count);
            sections.Add(new TrRmSection(kind, flags, (uint)(kind == 0x11 ? typeRows.Count : count),
                kind switch
                {
                    0x11 => rewrittenTypes,
                    0x20 => rewrittenBodies,
                    _ => bytes.ToArray(),
                }));
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
    public void FloatingFormatConversionsExecuteThroughLinearAndIndexedBodies()
    {
        const string source = """
            public float64 Widen(float32 value) { return float64(value); }
            public float32 Narrow(float64 value) { return float32(value); }
            public float64 WidenIf(float32 value) {
                if (value < 0.0) { return float64(value); }
                return float64(value);
            }
            public float32 NarrowIf(float64 value) {
                if (value < 0.0) { return float32(value); }
                return float32(value);
            }
            """;
        var original = Compile(source, true);
        var artifact = TrRmExecutableCompiler.Emit(original, "float-conversions");
        var loaded = TrRmExecutableCompiler.Load(artifact);
        string[] names = ["Widen", "Narrow", "WidenIf", "NarrowIf"];
        var images = new[]
        {
            NativeEmitter.EmitLibrary(original, names, NativeRuntimePolicy.FailFast),
            NativeEmitter.EmitLibrary(loaded, names, NativeRuntimePolicy.FailFast),
        };
        var paths = new string[images.Length];
        var handles = new IntPtr[images.Length];
        try
        {
            for (var index = 0; index < images.Length; index++)
            {
                paths[index] = Path.Combine(Directory.GetCurrentDirectory(),
                    $"trrm-float-conversion-{Guid.NewGuid():N}-{index}.dll");
                File.WriteAllBytes(paths[index], images[index]);
                handles[index] = NativeLibrary.Load(paths[index]);
            }

            foreach (var handle in handles)
            {
                var widen = Marshal.GetDelegateForFunctionPointer<Float32ToFloat64>(
                    NativeLibrary.GetExport(handle, "Widen"));
                var narrow = Marshal.GetDelegateForFunctionPointer<Float64ToFloat32>(
                    NativeLibrary.GetExport(handle, "Narrow"));
                var widenIf = Marshal.GetDelegateForFunctionPointer<Float32ToFloat64>(
                    NativeLibrary.GetExport(handle, "WidenIf"));
                var narrowIf = Marshal.GetDelegateForFunctionPointer<Float64ToFloat32>(
                    NativeLibrary.GetExport(handle, "NarrowIf"));

                Assert.Equal(0x3FB99999A0000000L, widen(0x3DCCCCCD));
                Assert.Equal(long.MinValue, widen(int.MinValue));
                Assert.Equal(long.MinValue, widenIf(int.MinValue));
                Assert.True(double.IsPositiveInfinity(BitConverter.Int64BitsToDouble(widenIf(0x7F800000))));
                Assert.True(double.IsNegativeInfinity(BitConverter.Int64BitsToDouble(
                    widenIf(unchecked((int)0xFF800000)))));
                Assert.True(double.IsNaN(BitConverter.Int64BitsToDouble(widenIf(0x7FC00001))));
                Assert.Equal(0x3F800000, narrow(0x3FF0000010000000L));
                Assert.Equal(0x7F800000, narrow(BitConverter.DoubleToInt64Bits(double.MaxValue)));
                Assert.Equal(0, narrow(BitConverter.DoubleToInt64Bits(double.Epsilon)));
                Assert.Equal(int.MinValue, narrow(long.MinValue | 1));
                Assert.True(float.IsNaN(BitConverter.Int32BitsToSingle(narrow(
                    BitConverter.DoubleToInt64Bits(double.NaN)))));
                Assert.Equal(0x7F800000,
                    narrow(BitConverter.DoubleToInt64Bits(double.PositiveInfinity)));
                Assert.Equal(0x3FB99999A0000000L, widenIf(0x3DCCCCCD));
                Assert.Equal(unchecked((int)0xFF800000),
                    narrowIf(BitConverter.DoubleToInt64Bits(double.NegativeInfinity)));
                Assert.Equal(0x7F800000,
                    narrowIf(BitConverter.DoubleToInt64Bits(double.PositiveInfinity)));
                Assert.Equal(unchecked((int)0x7F800000),
                    narrowIf(BitConverter.DoubleToInt64Bits(double.MaxValue)));
                Assert.Equal(0, narrowIf(BitConverter.DoubleToInt64Bits(double.Epsilon)));
                Assert.Equal(int.MinValue, narrowIf(long.MinValue | 1));
                Assert.True(float.IsNaN(BitConverter.Int32BitsToSingle(
                    narrowIf(BitConverter.DoubleToInt64Bits(double.NaN)))));
                Assert.Equal(0x3F800000, narrowIf(0x3FF0000010000000L));
                Assert.Equal(0x40000000, narrowIf(0x4000000000000000L));
            }
        }
        finally
        {
            foreach (var handle in handles)
            {
                if (handle != IntPtr.Zero)
                {
                    NativeLibrary.Free(handle);
                }
            }
            foreach (var path in paths)
            {
                if (path is not null)
                {
                    File.Delete(path);
                }
            }
        }
    }

    [Fact]
    public void IntegerToFloatingConversionsExecuteThroughLinearAndIndexedBodies()
    {
        const string source = """
            public float32 Int32ToFloat32(int32 value) { return float32(value); }
            public float32 UInt32ToFloat32(uint32 value) { return float32(value); }
            public float32 Int64ToFloat32(int64 value) { return float32(value); }
            public float32 UInt64ToFloat32(uint64 value) { return float32(value); }
            public float64 Int32ToFloat64(int32 value) { return float64(value); }
            public float64 UInt32ToFloat64(uint32 value) { return float64(value); }
            public float64 Int64ToFloat64(int64 value) { return float64(value); }
            public float64 UInt64ToFloat64(uint64 value) { return float64(value); }
            public float32 Int64ToFloat32If(int64 value) {
                float32 converted = float32(value);
                if (converted == 16777216.0) { return converted; }
                return 0.0;
            }
            int32 Main() {
                if (Int32ToFloat32(16777217) != 16777216.0) { return 1; }
                if (UInt32ToFloat32(4294967295) != 4294967296.0) { return 2; }
                if (Int64ToFloat32(16777219) != 16777220.0) { return 3; }
                if (UInt64ToFloat32(18446744073709551615) != 18446744073709551616.0) { return 4; }
                if (Int32ToFloat64(-2147483648) != -2147483648.0) { return 5; }
                if (UInt32ToFloat64(4294967295) != 4294967295.0) { return 6; }
                if (Int64ToFloat64(-9223372036854775808) != -9223372036854775808.0) { return 7; }
                if (UInt64ToFloat64(18446744073709551615) != 18446744073709551616.0) { return 8; }
                if (Int64ToFloat32If(16777217) != 16777216.0) { return 9; }
                return 42;
            }
            """;
        var original = Compile(source);
        var artifact = TrRmExecutableCompiler.Emit(original, "integer-float-conversions");
        var loaded = TrRmExecutableCompiler.Load(artifact);

        Assert.Equal(42, NativeJit.Execute(original, NativeRuntimePolicy.FailFast));
        Assert.Equal(42, NativeJit.Execute(loaded, NativeRuntimePolicy.FailFast));
    }

    [Fact]
    public void FloatingToIntegerConversionsExecuteThroughLinearAndIndexedBodies()
    {
        const string source = """
            public int32 Float64ToInt32(float64 value) { return int32(value); }
            public uint32 Float64ToUInt32(float64 value) { return uint32(value); }
            public int64 Float64ToInt64(float64 value) { return int64(value); }
            public uint64 Float64ToUInt64(float64 value) { return uint64(value); }
            public int32 Float32ToInt32(float32 value) { return int32(value); }
            public uint32 Float32ToUInt32(float32 value) { return uint32(value); }
            public int64 Float32ToInt64(float32 value) { return int64(value); }
            public uint64 Float32ToUInt64(float32 value) { return uint64(value); }
            public int32 Float64ToInt32IfZero(float64 value) {
                if (value == 0.0) { return int32(value); }
                return 42;
            }
            public uint32 Float64ToUInt32IfZero(float64 value) {
                if (value == 0.0) { return uint32(value); }
                return 42;
            }
            public int64 Float64ToInt64IfZero(float64 value) {
                if (value == 0.0) { return int64(value); }
                return 42;
            }
            public uint64 Float64ToUInt64IfZero(float64 value) {
                if (value == 0.0) { return uint64(value); }
                return 42;
            }
            public int32 Float32ToInt32IfZero(float32 value) {
                if (value == 0.0) { return int32(value); }
                return 42;
            }
            public uint32 Float32ToUInt32IfZero(float32 value) {
                if (value == 0.0) { return uint32(value); }
                return 42;
            }
            public int64 Float32ToInt64IfZero(float32 value) {
                if (value == 0.0) { return int64(value); }
                return 42;
            }
            public uint64 Float32ToUInt64IfZero(float32 value) {
                if (value == 0.0) { return uint64(value); }
                return 42;
            }
            """;
        var original = Compile(source, true);
        var artifact = TrRmExecutableCompiler.Emit(original, "float-integer-conversions");
        var loaded = TrRmExecutableCompiler.Load(artifact);
        string[] names =
        [
            "Float64ToInt32", "Float64ToUInt32", "Float64ToInt64", "Float64ToUInt64",
            "Float32ToInt32", "Float32ToUInt32", "Float32ToInt64", "Float32ToUInt64",
            "Float64ToInt32IfZero", "Float64ToUInt32IfZero", "Float64ToInt64IfZero",
            "Float64ToUInt64IfZero", "Float32ToInt32IfZero", "Float32ToUInt32IfZero",
            "Float32ToInt64IfZero", "Float32ToUInt64IfZero",
        ];
        var images = new[]
        {
            NativeEmitter.EmitLibrary(original, names, NativeRuntimePolicy.FailFast),
            NativeEmitter.EmitLibrary(loaded, names, NativeRuntimePolicy.FailFast),
        };
        var paths = new string[images.Length];
        var handles = new IntPtr[images.Length];
        try
        {
            for (var index = 0; index < images.Length; index++)
            {
                paths[index] = Path.Combine(Directory.GetCurrentDirectory(),
                    $"trrm-float-to-integer-{Guid.NewGuid():N}-{index}.dll");
                File.WriteAllBytes(paths[index], images[index]);
                handles[index] = NativeLibrary.Load(paths[index]);
            }

            foreach (var handle in handles)
            {
                var float64ToInt32 = Marshal.GetDelegateForFunctionPointer<Float64ToInt32>(
                    NativeLibrary.GetExport(handle, "Float64ToInt32"));
                var float64ToUInt32 = Marshal.GetDelegateForFunctionPointer<Float64ToUInt32>(
                    NativeLibrary.GetExport(handle, "Float64ToUInt32"));
                var float64ToInt64 = Marshal.GetDelegateForFunctionPointer<Float64ToInt64>(
                    NativeLibrary.GetExport(handle, "Float64ToInt64"));
                var float64ToUInt64 = Marshal.GetDelegateForFunctionPointer<Float64ToUInt64>(
                    NativeLibrary.GetExport(handle, "Float64ToUInt64"));
                var float32ToInt32 = Marshal.GetDelegateForFunctionPointer<Float32ToInt32>(
                    NativeLibrary.GetExport(handle, "Float32ToInt32"));
                var float32ToUInt32 = Marshal.GetDelegateForFunctionPointer<Float32ToUInt32>(
                    NativeLibrary.GetExport(handle, "Float32ToUInt32"));
                var float32ToInt64 = Marshal.GetDelegateForFunctionPointer<Float32ToInt64>(
                    NativeLibrary.GetExport(handle, "Float32ToInt64"));
                var float32ToUInt64 = Marshal.GetDelegateForFunctionPointer<Float32ToUInt64>(
                    NativeLibrary.GetExport(handle, "Float32ToUInt64"));
                var float64ToInt32IfZero = Marshal.GetDelegateForFunctionPointer<Float64ToInt32>(
                    NativeLibrary.GetExport(handle, "Float64ToInt32IfZero"));
                var float64ToUInt32IfZero = Marshal.GetDelegateForFunctionPointer<Float64ToUInt32>(
                    NativeLibrary.GetExport(handle, "Float64ToUInt32IfZero"));
                var float64ToInt64IfZero = Marshal.GetDelegateForFunctionPointer<Float64ToInt64>(
                    NativeLibrary.GetExport(handle, "Float64ToInt64IfZero"));
                var float64ToUInt64IfZero = Marshal.GetDelegateForFunctionPointer<Float64ToUInt64>(
                    NativeLibrary.GetExport(handle, "Float64ToUInt64IfZero"));
                var float32ToInt32IfZero = Marshal.GetDelegateForFunctionPointer<Float32ToInt32>(
                    NativeLibrary.GetExport(handle, "Float32ToInt32IfZero"));
                var float32ToUInt32IfZero = Marshal.GetDelegateForFunctionPointer<Float32ToUInt32>(
                    NativeLibrary.GetExport(handle, "Float32ToUInt32IfZero"));
                var float32ToInt64IfZero = Marshal.GetDelegateForFunctionPointer<Float32ToInt64>(
                    NativeLibrary.GetExport(handle, "Float32ToInt64IfZero"));
                var float32ToUInt64IfZero = Marshal.GetDelegateForFunctionPointer<Float32ToUInt64>(
                    NativeLibrary.GetExport(handle, "Float32ToUInt64IfZero"));

                Assert.Equal(1, float64ToInt32(BitConverter.DoubleToInt64Bits(1.9)));
                Assert.Equal(-1, float64ToInt32(BitConverter.DoubleToInt64Bits(-1.9)));
                Assert.Equal(0, float64ToInt32(BitConverter.DoubleToInt64Bits(-0.0)));
                Assert.Equal(int.MinValue,
                    float64ToInt32(BitConverter.DoubleToInt64Bits(-2147483648.9)));
                Assert.Equal(int.MaxValue,
                    float64ToInt32(BitConverter.DoubleToInt64Bits(2147483647.9)));
                Assert.Equal(0u, float64ToUInt32(BitConverter.DoubleToInt64Bits(-0.5)));
                Assert.Equal(0u, float64ToUInt32(BitConverter.DoubleToInt64Bits(-0.0)));
                Assert.Equal(uint.MaxValue,
                    float64ToUInt32(BitConverter.DoubleToInt64Bits(4294967295.75)));
                Assert.Equal(long.MinValue,
                    float64ToInt64(BitConverter.DoubleToInt64Bits(long.MinValue)));
                Assert.Equal(9_223_372_036_854_774_784L,
                    float64ToInt64(BitConverter.DoubleToInt64Bits(
                        Math.BitDecrement(9223372036854775808.0))));
                Assert.Equal(18_446_744_073_709_549_568UL,
                    float64ToUInt64(BitConverter.DoubleToInt64Bits(
                        Math.BitDecrement(18446744073709551616.0))));
                Assert.Equal(0UL, float64ToUInt64(BitConverter.DoubleToInt64Bits(-0.5)));
                Assert.Equal(0UL, float64ToUInt64(BitConverter.DoubleToInt64Bits(-0.0)));
                Assert.Equal(3, float32ToInt32(BitConverter.SingleToInt32Bits(3.75F)));
                Assert.Equal(0, float32ToInt32(BitConverter.SingleToInt32Bits(-0.0F)));
                Assert.Equal(0U, float32ToUInt32(BitConverter.SingleToInt32Bits(-0.5F)));
                Assert.Equal(4_294_967_040U, float32ToUInt32(unchecked((int)0x4F7FFFFF)));
                Assert.Equal(long.MinValue, float32ToInt64(unchecked((int)0xDF000000)));
                Assert.Equal(9_223_372_036_854_775_808UL, float32ToUInt64(0x5F000000));
                Assert.Equal(1UL,
                    float32ToUInt64(BitConverter.SingleToInt32Bits(1.9F)));
                Assert.Equal(0, float64ToInt32IfZero(BitConverter.DoubleToInt64Bits(-0.0)));
                Assert.Equal(42, float64ToInt32IfZero(BitConverter.DoubleToInt64Bits(1.0)));
                Assert.Equal(0U, float64ToUInt32IfZero(BitConverter.DoubleToInt64Bits(-0.0)));
                Assert.Equal(42U, float64ToUInt32IfZero(BitConverter.DoubleToInt64Bits(1.0)));
                Assert.Equal(0L, float64ToInt64IfZero(BitConverter.DoubleToInt64Bits(-0.0)));
                Assert.Equal(42L, float64ToInt64IfZero(BitConverter.DoubleToInt64Bits(1.0)));
                Assert.Equal(0UL, float64ToUInt64IfZero(BitConverter.DoubleToInt64Bits(-0.0)));
                Assert.Equal(42UL, float64ToUInt64IfZero(BitConverter.DoubleToInt64Bits(1.0)));
                Assert.Equal(0, float32ToInt32IfZero(BitConverter.SingleToInt32Bits(-0.0F)));
                Assert.Equal(42, float32ToInt32IfZero(BitConverter.SingleToInt32Bits(1.0F)));
                Assert.Equal(0U, float32ToUInt32IfZero(BitConverter.SingleToInt32Bits(-0.0F)));
                Assert.Equal(42U, float32ToUInt32IfZero(BitConverter.SingleToInt32Bits(1.0F)));
                Assert.Equal(0L, float32ToInt64IfZero(BitConverter.SingleToInt32Bits(-0.0F)));
                Assert.Equal(42L, float32ToInt64IfZero(BitConverter.SingleToInt32Bits(1.0F)));
                Assert.Equal(0UL, float32ToUInt64IfZero(BitConverter.SingleToInt32Bits(-0.0F)));
                Assert.Equal(42UL, float32ToUInt64IfZero(BitConverter.SingleToInt32Bits(1.0F)));
            }
        }
        finally
        {
            foreach (var handle in handles)
            {
                if (handle != IntPtr.Zero)
                {
                    NativeLibrary.Free(handle);
                }
            }
            foreach (var path in paths)
            {
                if (path is not null)
                {
                    File.Delete(path);
                }
            }
        }
    }

    [Theory]
    [InlineData(0x7FF8000000000001UL)]
    [InlineData(0x7FF0000000000000UL)]
    [InlineData(0xFFF0000000000000UL)]
    [InlineData(0x41E0000000000000UL)]
    [InlineData(0xC1E0000000200000UL)]
    public async Task FloatingToIntegerFailuresUseTheOverflowFailFastStatusAsync(ulong replacementBits)
    {
        const string source = """
            int32 Main() { return int32(Value()); }
            float64 Value() { return 1.0; }
            """;
        var artifact = TrRmExecutableCompiler.Emit(Compile(source), "float-to-integer-failure");
        var oneBits = new byte[sizeof(ulong)];
        var failureBits = new byte[sizeof(ulong)];
        BinaryPrimitives.WriteUInt64LittleEndian(oneBits,
            unchecked((ulong)BitConverter.DoubleToInt64Bits(1.0)));
        BinaryPrimitives.WriteUInt64LittleEndian(failureBits, replacementBits);
        var malformed = ReplaceBodyConstant(artifact, 2, oneBits, failureBits);
        var path = Path.Combine(Directory.GetCurrentDirectory(), $"trrm-float-to-integer-{Guid.NewGuid():N}.trrm");
        try
        {
            File.WriteAllBytes(path, malformed);
            var (exitCode, output, error) = await RunTrRmAsync(path);

            Assert.Equal(unchecked((int)0xE0540001), exitCode);
            Assert.Empty(output);
            Assert.Empty(error);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void FloatToIntegerRejectsAnInvalidDestinationTypeSpec()
    {
        const string source = "public int32 Convert(float64 value) { return int32(value); }";
        var artifact = TrRmExecutableCompiler.Emit(Compile(source, true), "invalid-float-integer");
        var malformed = ReplaceBodyOperationByte(artifact, 1, 0x1F, 2, 0);

        Assert.Throws<InvalidDataException>(() => TrRmExecutableCompiler.Load(malformed));
    }

    [Fact]
    public void FloatingToIntegerConversionsCannotPromiseNoThrow()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(
            "[NoThrow] public int32 Convert(float64 value) { return int32(value); }"));

        Assert.True(compilation.HasErrors);
    }

    [Fact]
    public void NoThrowArtifactsCannotRetainFloatToIntegerOperations()
    {
        const string source = """
            [NoThrow] public int32 Convert(float64 value) { return 0; }
            public int32 ConvertChecked(float64 value) { return int32(value); }
            [NoThrow] public int32 ConvertIf(float64 value) {
                if (value == 0.0) { return 0; }
                return 42;
            }
            public int32 ConvertIfChecked(float64 value) {
                if (value == 0.0) { return int32(value); }
                return 42;
            }
            """;
        var artifact = TrRmExecutableCompiler.Emit(Compile(source, true), "float-to-integer-no-throw");

        Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.Load(ReplaceBodyRow(artifact, 1, 2)));
        Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.Load(ReplaceBodyRow(artifact, 3, 4)));
    }

    [Fact]
    public void IntegerToFloatRejectsAnIntegerDestinationTypeSpec()
    {
        const string source = "public float32 Convert(int32 value) { return float32(value); }";
        var artifact = TrRmExecutableCompiler.Emit(Compile(source, true), "invalid-integer-float");
        var malformed = ReplaceBodyOperationByte(artifact, 1, 0x1B, 2, 1);

        Assert.Throws<InvalidDataException>(() => TrRmExecutableCompiler.Load(malformed));
    }

    [Fact]
    public void Char16ToFloatingConversionsPreserveCodeUnitValues()
    {
        const string source = """
            [NoThrow] public float32 ToFloat32(char16 value) { return float32(value); }
            [NoThrow] public float64 ToFloat64(char16 value) { return float64(value); }
            public float32 ToFloat32IfBoundary(char16 value) {
                if (value == char16(0) || value == char16(55296) || value == char16(65535)) {
                    return float32(value);
                }
                return 0.0;
            }
            public float64 ToFloat64IfBoundary(char16 value) {
                if (value == char16(0) || value == char16(55296) || value == char16(65535)) {
                    return float64(value);
                }
                return 0.0;
            }
            """;
        var original = Compile(source, true);
        var artifact = TrRmExecutableCompiler.Emit(original, "char16-float-conversions");
        var loaded = TrRmExecutableCompiler.Load(artifact);
        string[] names = ["ToFloat32", "ToFloat64", "ToFloat32IfBoundary", "ToFloat64IfBoundary"];
        var images = new[]
        {
            NativeEmitter.EmitLibrary(original, names, NativeRuntimePolicy.FailFast),
            NativeEmitter.EmitLibrary(loaded, names, NativeRuntimePolicy.FailFast),
        };
        var paths = new string[images.Length];
        var handles = new IntPtr[images.Length];
        try
        {
            for (var index = 0; index < images.Length; index++)
            {
                paths[index] = Path.Combine(Directory.GetCurrentDirectory(),
                    $"trrm-char16-float-{Guid.NewGuid():N}-{index}.dll");
                File.WriteAllBytes(paths[index], images[index]);
                handles[index] = NativeLibrary.Load(paths[index]);
            }

            foreach (var handle in handles)
            {
                var toFloat32 = Marshal.GetDelegateForFunctionPointer<Char16ToFloat32>(
                    NativeLibrary.GetExport(handle, "ToFloat32"));
                var toFloat64 = Marshal.GetDelegateForFunctionPointer<Char16ToFloat64>(
                    NativeLibrary.GetExport(handle, "ToFloat64"));
                var toFloat32If = Marshal.GetDelegateForFunctionPointer<Char16ToFloat32>(
                    NativeLibrary.GetExport(handle, "ToFloat32IfBoundary"));
                var toFloat64If = Marshal.GetDelegateForFunctionPointer<Char16ToFloat64>(
                    NativeLibrary.GetExport(handle, "ToFloat64IfBoundary"));

                Assert.Equal(0, toFloat32(0));
                Assert.Equal(BitConverter.SingleToInt32Bits(55296.0F), toFloat32(0xD800));
                Assert.Equal(BitConverter.SingleToInt32Bits(65535.0F), toFloat32(ushort.MaxValue));
                Assert.Equal(0L, toFloat64(0));
                Assert.Equal(BitConverter.DoubleToInt64Bits(55296.0), toFloat64(0xD800));
                Assert.Equal(BitConverter.DoubleToInt64Bits(65535.0), toFloat64(ushort.MaxValue));
                Assert.Equal(BitConverter.SingleToInt32Bits(0.0F), toFloat32If(0));
                Assert.Equal(BitConverter.SingleToInt32Bits(55296.0F), toFloat32If(0xD800));
                Assert.Equal(BitConverter.SingleToInt32Bits(65535.0F), toFloat32If(0xFFFF));
                Assert.Equal(0L, toFloat64If(0));
                Assert.Equal(BitConverter.DoubleToInt64Bits(55296.0), toFloat64If(0xD800));
                Assert.Equal(BitConverter.DoubleToInt64Bits(65535.0), toFloat64If(0xFFFF));
            }
        }
        finally
        {
            foreach (var handle in handles)
            {
                if (handle != IntPtr.Zero)
                {
                    NativeLibrary.Free(handle);
                }
            }
            foreach (var path in paths)
            {
                if (path is not null)
                {
                    File.Delete(path);
                }
            }
        }
    }

    [Fact]
    public void Char16ToFloatRejectsAnIntegerDestinationTypeSpec()
    {
        const string source = "public float32 Convert(char16 value) { return float32(value); }";
        var artifact = TrRmExecutableCompiler.Emit(Compile(source, true), "invalid-char16-float");
        var malformed = ReplaceBodyOperationByte(artifact, 1, 0x1E, 2, 1);

        Assert.Throws<InvalidDataException>(() => TrRmExecutableCompiler.Load(malformed));
    }

    [Fact]
    public void FloatingNegationPreservesPayloadBitsThroughLinearAndIndexedBodies()
    {
        const string source = """
            public float32 Negate32(float32 value) { return -value; }
            public float64 Negate64(float64 value) { return -value; }
            public float32 Negate32If(float32 value) {
                if (value == 0.0) { return -value; }
                return -1.0;
            }
            public float64 Negate64If(float64 value) {
                if (value == 0.0) { return -value; }
                return -1.0;
            }
            """;
        var original = Compile(source, true);
        var artifact = TrRmExecutableCompiler.Emit(original, "float-negation");
        var loaded = TrRmExecutableCompiler.Load(artifact);
        string[] names = ["Negate32", "Negate64", "Negate32If", "Negate64If"];
        var images = new[]
        {
            NativeEmitter.EmitLibrary(original, names, NativeRuntimePolicy.FailFast),
            NativeEmitter.EmitLibrary(loaded, names, NativeRuntimePolicy.FailFast),
        };
        var paths = new string[images.Length];
        var handles = new IntPtr[images.Length];
        try
        {
            for (var index = 0; index < images.Length; index++)
            {
                paths[index] = Path.Combine(Directory.GetCurrentDirectory(),
                    $"trrm-float-negation-{Guid.NewGuid():N}-{index}.dll");
                File.WriteAllBytes(paths[index], images[index]);
                handles[index] = NativeLibrary.Load(paths[index]);
            }

            foreach (var handle in handles)
            {
                var negate32 = Marshal.GetDelegateForFunctionPointer<Float32Unary>(
                    NativeLibrary.GetExport(handle, "Negate32"));
                var negate64 = Marshal.GetDelegateForFunctionPointer<Float64Unary>(
                    NativeLibrary.GetExport(handle, "Negate64"));
                var negate32If = Marshal.GetDelegateForFunctionPointer<Float32Unary>(
                    NativeLibrary.GetExport(handle, "Negate32If"));
                var negate64If = Marshal.GetDelegateForFunctionPointer<Float64Unary>(
                    NativeLibrary.GetExport(handle, "Negate64If"));

                Assert.Equal(unchecked((int)0x80000000), negate32(0));
                Assert.Equal(0, negate32(unchecked((int)0x80000000)));
                Assert.Equal(unchecked((int)0xFF800000), negate32(0x7F800000));
                Assert.Equal(0x7F800000, negate32(unchecked((int)0xFF800000)));
                Assert.Equal(unchecked((int)0xFFC12345), negate32(unchecked((int)0x7FC12345)));
                Assert.Equal(unchecked((int)0x7FC54321), negate32(unchecked((int)0xFFC54321)));
                Assert.Equal(long.MinValue, negate64(0));
                Assert.Equal(0L, negate64(long.MinValue));
                Assert.Equal(unchecked((long)0xFFF0000000000000UL),
                    negate64(unchecked((long)0x7FF0000000000000UL)));
                Assert.Equal(0x7FF0000000000000L,
                    negate64(unchecked((long)0xFFF0000000000000UL)));
                Assert.Equal(unchecked((long)0xFFF8123456789ABCUL),
                    negate64(unchecked((long)0x7FF8123456789ABCUL)));
                Assert.Equal(unchecked((long)0x7FF8ABCDEF012345UL),
                    negate64(unchecked((long)0xFFF8ABCDEF012345UL)));
                Assert.Equal(unchecked((int)0x80000000), negate32If(0));
                Assert.Equal(unchecked((int)0xBF800000), negate32If(0x3F800000));
                Assert.Equal(long.MinValue, negate64If(0));
                Assert.Equal(unchecked((long)0xBFF0000000000000UL),
                    negate64If(0x3FF0000000000000L));
            }
        }
        finally
        {
            foreach (var handle in handles)
            {
                if (handle != IntPtr.Zero)
                {
                    NativeLibrary.Free(handle);
                }
            }
            foreach (var path in paths)
            {
                if (path is not null)
                {
                    File.Delete(path);
                }
            }
        }
    }

    [Fact]
    public void FloatNegateRejectsIntegerOperands()
    {
        const string source = "public int32 Negate(int32 value) { return -value; }";
        var artifact = TrRmExecutableCompiler.Emit(Compile(source, true), "invalid-float-negate");
        var malformed = ReplaceBodyOperationByte(artifact, 1, 0x17, 0, 0x1C);

        Assert.Throws<InvalidDataException>(() => TrRmExecutableCompiler.Load(malformed));
    }

    [Fact]
    public void FloatingArithmeticExecutesThroughLinearAndIndexedBodies()
    {
        const string source = """
            [NoThrow] public float32 Add32(float32 left, float32 right) { return left + right; }
            [NoThrow] public float32 Subtract32(float32 left, float32 right) { return left - right; }
            [NoThrow] public float32 Multiply32(float32 left, float32 right) { return left * right; }
            [NoThrow] public float32 Divide32(float32 left, float32 right) { return left / right; }
            [NoThrow] public float64 Add64(float64 left, float64 right) { return left + right; }
            [NoThrow] public float64 Subtract64(float64 left, float64 right) { return left - right; }
            [NoThrow] public float64 Multiply64(float64 left, float64 right) { return left * right; }
            [NoThrow] public float64 Divide64(float64 left, float64 right) { return left / right; }
            public float32 Add32IfZero(float32 left, float32 right) {
                if (left == 0.0) { return left + right; }
                return 0.0;
            }
            public float64 Add64IfZero(float64 left, float64 right) {
                if (left == 0.0) { return left + right; }
                return 0.0;
            }
            """;
        var original = Compile(source, true);
        var artifact = TrRmExecutableCompiler.Emit(original, "float-arithmetic");
        var loaded = TrRmExecutableCompiler.Load(artifact);
        string[] names = [
            "Add32", "Subtract32", "Multiply32", "Divide32",
            "Add64", "Subtract64", "Multiply64", "Divide64",
            "Add32IfZero", "Add64IfZero",
        ];
        var images = new[]
        {
            NativeEmitter.EmitLibrary(original, names, NativeRuntimePolicy.FailFast),
            NativeEmitter.EmitLibrary(loaded, names, NativeRuntimePolicy.FailFast),
        };
        var paths = new string[images.Length];
        var handles = new IntPtr[images.Length];
        try
        {
            for (var index = 0; index < images.Length; index++)
            {
                paths[index] = Path.Combine(Directory.GetCurrentDirectory(),
                    $"trrm-float-arithmetic-{Guid.NewGuid():N}-{index}.dll");
                File.WriteAllBytes(paths[index], images[index]);
                handles[index] = NativeLibrary.Load(paths[index]);
            }

            foreach (var handle in handles)
            {
                var add32 = Marshal.GetDelegateForFunctionPointer<Float32Binary>(
                    NativeLibrary.GetExport(handle, "Add32"));
                var subtract32 = Marshal.GetDelegateForFunctionPointer<Float32Binary>(
                    NativeLibrary.GetExport(handle, "Subtract32"));
                var multiply32 = Marshal.GetDelegateForFunctionPointer<Float32Binary>(
                    NativeLibrary.GetExport(handle, "Multiply32"));
                var divide32 = Marshal.GetDelegateForFunctionPointer<Float32Binary>(
                    NativeLibrary.GetExport(handle, "Divide32"));
                var add64 = Marshal.GetDelegateForFunctionPointer<Float64Binary>(
                    NativeLibrary.GetExport(handle, "Add64"));
                var subtract64 = Marshal.GetDelegateForFunctionPointer<Float64Binary>(
                    NativeLibrary.GetExport(handle, "Subtract64"));
                var multiply64 = Marshal.GetDelegateForFunctionPointer<Float64Binary>(
                    NativeLibrary.GetExport(handle, "Multiply64"));
                var divide64 = Marshal.GetDelegateForFunctionPointer<Float64Binary>(
                    NativeLibrary.GetExport(handle, "Divide64"));
                var add32IfZero = Marshal.GetDelegateForFunctionPointer<Float32Binary>(
                    NativeLibrary.GetExport(handle, "Add32IfZero"));
                var add64IfZero = Marshal.GetDelegateForFunctionPointer<Float64Binary>(
                    NativeLibrary.GetExport(handle, "Add64IfZero"));

                Assert.Equal(0x40700000, add32(0x3FC00000, 0x40100000));
                Assert.Equal(0x40200000, subtract32(0x40800000, 0x3FC00000));
                Assert.Equal(0x40C00000, multiply32(0x40000000, 0x40400000));
                Assert.Equal(0x3FC00000, divide32(0x40400000, 0x40000000));
                Assert.Equal(0x7FC00000, add32(0x7F800000, unchecked((int)0xFF800000)));
                Assert.Equal(0x7FC00000, subtract32(0x7F800000, 0x7F800000));
                Assert.Equal(0x7FC00000, multiply32(0, 0x7F800000));
                Assert.Equal(0x7FC00000, divide32(0, 0));
                Assert.Equal(unchecked((int)0x7FC00000),
                    add32(unchecked((int)0xFFC12345), 0x3F800000));
                Assert.Equal(unchecked((int)0x80000000),
                    multiply32(unchecked((int)0x80000000), 0x3F800000));

                Assert.Equal(0x400E000000000000L, add64(0x3FF8000000000000L, 0x4002000000000000L));
                Assert.Equal(0x4004000000000000L, subtract64(0x4010000000000000L, 0x3FF8000000000000L));
                Assert.Equal(0x4018000000000000L, multiply64(0x4000000000000000L, 0x4008000000000000L));
                Assert.Equal(0x3FF8000000000000L, divide64(0x4008000000000000L, 0x4000000000000000L));
                Assert.Equal(0x7FF8000000000000L,
                    add64(0x7FF0000000000000L, unchecked((long)0xFFF0000000000000UL)));
                Assert.Equal(0x7FF8000000000000L,
                    subtract64(0x7FF0000000000000L, 0x7FF0000000000000L));
                Assert.Equal(0x7FF8000000000000L,
                    multiply64(0, 0x7FF0000000000000L));
                Assert.Equal(0x7FF8000000000000L, divide64(0, 0));
                Assert.Equal(unchecked((long)0x7FF8000000000000UL),
                    add64(unchecked((long)0xFFF8123456789ABCUL), 0x3FF0000000000000L));
                Assert.Equal(long.MinValue, multiply64(long.MinValue, 0x3FF0000000000000L));
                Assert.Equal(unchecked((int)0x80000000),
                    add32IfZero(unchecked((int)0x80000000), unchecked((int)0x80000000)));
                Assert.Equal(0, add32IfZero(0x3F800000, 0x40000000));
                Assert.Equal(long.MinValue,
                    add64IfZero(long.MinValue, long.MinValue));
                Assert.Equal(0L, add64IfZero(0x3FF0000000000000L, 0x4000000000000000L));
            }
        }
        finally
        {
            foreach (var handle in handles)
            {
                if (handle != IntPtr.Zero)
                {
                    NativeLibrary.Free(handle);
                }
            }
            foreach (var path in paths)
            {
                if (path is not null)
                {
                    File.Delete(path);
                }
            }
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    public void InvalidFloatingArithmeticKindsAreRejected(byte kind)
    {
        const string source = "public float32 Add(float32 left, float32 right) { return left + right; }";
        var artifact = TrRmExecutableCompiler.Emit(Compile(source, true), "invalid-float-binary");
        var malformed = ReplaceBodyOperationByte(artifact, 1, 0x1D, 2, kind);

        Assert.Throws<InvalidDataException>(() => TrRmExecutableCompiler.Load(malformed));
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

    [Theory]
    [InlineData("[NoThrow] ", true, false)]
    [InlineData("[DoesNotReturn] ", false, true)]
    [InlineData("[NoThrow][DoesNotReturn] ", true, true)]
    public void EffectsRoundTripWithoutFabricatedProof(string attributes, bool noThrow, bool doesNotReturn)
    {
        var haltBody = doesNotReturn ? "while (true) { }" : string.Empty;
        var source = attributes + $"void Halt() {{ {haltBody} }} int32 Main() {{ return 42; }}";
        var program = TrRmExecutableCompiler.Load(
            TrRmExecutableCompiler.Emit(Compile(source), "effects"));
        var function = Assert.Single(program.Functions, static function => function.Name == "Halt");
        Assert.Equal(noThrow, function.IsNoThrow);
        Assert.Equal(doesNotReturn, function.DoesNotReturn);
    }

    [Theory]
    [InlineData("out int32 value")]
    public void ExecutableWriterRejectsOutputParameters(string parameter)
    {
        var source = $"int32 Output({parameter}) {{ value = 42; return 0; }} "
            + "int32 Main() { return 42; }";
        var program = Compile(source);

        Assert.Throws<NotSupportedException>(() =>
            TrRmExecutableCompiler.Emit(program, "output-parameters"));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void ExecutableLoaderRejectsOutputModesInUncalledIndexedMethods(byte mode)
    {
        const string source = """
            int32 Helper(int32 value) {
                if (value == 0) { return 1; }
                return value;
            }
            int32 Main() { return 42; }
            """;
        var artifact = TrRmExecutableCompiler.Emit(Compile(source), "output-mode");
        var malformed = ReplaceParameterMode(artifact, mode);

        Assert.Throws<InvalidDataException>(() => TrRmExecutableCompiler.Load(malformed));
    }

    [Fact]
    public void LoadsMultipleEffectContractsFromOneTableIndex()
    {
        const string source = "[NoThrow] void Safe() { } "
            + "[DoesNotReturn] void Halt() { while (true) { } } "
            + "int32 Main() { Safe(); return 42; }";
        var program = TrRmExecutableCompiler.Load(
            TrRmExecutableCompiler.Emit(Compile(source), "multiple-effects"));
        var safe = Assert.Single(program.Functions, static function => function.Name == "Safe");
        var halt = Assert.Single(program.Functions, static function => function.Name == "Halt");
        Assert.True(safe.IsNoThrow);
        Assert.False(safe.DoesNotReturn);
        Assert.False(halt.IsNoThrow);
        Assert.True(halt.DoesNotReturn);
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
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate long Float32ToFloat64(int value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int Float64ToFloat32(long value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int Float32Unary(int value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate long Float64Unary(long value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int Float32Binary(int left, int right);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate long Float64Binary(long left, long right);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int Char16ToFloat32(ushort value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate long Char16ToFloat64(ushort value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int Float64ToInt32(long value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate uint Float64ToUInt32(long value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate long Float64ToInt64(long value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate ulong Float64ToUInt64(long value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int Float32ToInt32(int value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate uint Float32ToUInt32(int value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate long Float32ToInt64(int value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate ulong Float32ToUInt64(int value);
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

    [Theory]
    [InlineData(2147483648L)]
    [InlineData(-2147483649L)]
    public void CompactLinearReaderRejectsOutOfRangeInt32ConstSignedPayloads(long value)
    {
        var artifact = OptionalScalarArtifact(SignedInt32LinearBody(value),
            optionalChar16: false, optionalInt32: false);

        Assert.Throws<InvalidDataException>(() => TrRmExecutableCompiler.Load(artifact));
    }

    [Theory]
    [InlineData(int.MinValue)]
    [InlineData(int.MaxValue)]
    public void CompactLinearReaderAcceptsInt32ConstSignedExtrema(int value)
    {
        var artifact = OptionalScalarArtifact(SignedInt32LinearBody(value),
            optionalChar16: false, optionalInt32: false);
        var loaded = TrRmExecutableCompiler.Load(artifact);

        Assert.Equal(value, NativeJit.Execute(loaded, NativeRuntimePolicy.FailFast));
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

    private static byte[] SignedInt32LinearBody(long value)
    {
        var dataWidth = value is >= sbyte.MinValue and <= sbyte.MaxValue ? 1
            : value is >= short.MinValue and <= short.MaxValue ? 2
            : value is >= int.MinValue and <= int.MaxValue ? 4 : 8;
        var operations = new List<byte>();
        if (dataWidth == 1)
        {
            operations.Add(2);
        }
        else
        {
            operations.Add(0xF0);
            operations.Add((byte)(dataWidth switch { 2 => 0x10, 4 => 0x20, _ => 0x30 }));
            operations.Add(2);
        }
        operations.Add(1);
        operations.Add(1);
        var payload = new byte[dataWidth];
        switch (dataWidth)
        {
            case 1:
                payload[0] = unchecked((byte)value);
                break;
            case 2:
                BinaryPrimitives.WriteUInt16LittleEndian(payload, unchecked((ushort)value));
                break;
            case 4:
                BinaryPrimitives.WriteUInt32LittleEndian(payload, unchecked((uint)value));
                break;
            default:
                BinaryPrimitives.WriteUInt64LittleEndian(payload, unchecked((ulong)value));
                break;
        }
        operations.AddRange(payload);
        return OptionalLinearBody([4], [.. operations], 1, 1);
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

    private static byte[] ReplaceBodyRow(byte[] artifact, int destinationRow, int sourceRow)
    {
        var index = new TrRmArtifactIndex(artifact);
        Assert.True(index.TryGetSection(0x20, out var bodyBytes, out var bodyCount));
        var bodies = new TrRmBodyTableReader(bodyBytes, bodyCount);
        var bodyRows = new ReadOnlyMemory<byte>[bodyCount];
        for (var row = 1; row <= bodyCount; row++)
        {
            bodyRows[row - 1] = bodies.GetRow(row).ToArray();
        }
        bodyRows[destinationRow - 1] = bodies.GetRow(sourceRow).ToArray();
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

    private static byte[] ReplaceParameterMode(byte[] artifact, byte mode)
    {
        var index = new TrRmArtifactIndex(artifact);
        Assert.True(index.TryGetSection(0x18, out var parameterBytes, out var parameterCount));
        Assert.Equal(1, parameterCount);
        var parameters = parameterBytes.ToArray();
        parameters[12] = mode;
        var sections = new List<TrRmSection>(index.SectionCount);
        for (var section = 0; section < index.SectionCount; section++)
        {
            index.GetSection(section, out var kind, out var flags, out var bytes, out var count);
            sections.Add(new TrRmSection(kind, flags, (uint)count,
                kind == 0x18 ? parameters : bytes.ToArray()));
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

    private static byte[] ReplaceBodyOperationByte(byte[] artifact, int bodyRow,
        byte opcode, int fieldOffset, byte replacement)
    {
        var index = new TrRmArtifactIndex(artifact);
        Assert.True(index.TryGetSection(0x20, out var bodyBytes, out var bodyCount));
        var bodies = new TrRmBodyTableReader(bodyBytes, bodyCount);
        var bodyRows = new ReadOnlyMemory<byte>[bodyCount];
        for (var row = 1; row <= bodyCount; row++)
        {
            bodyRows[row - 1] = bodies.GetRow(row).ToArray();
        }

        var body = bodyRows[bodyRow - 1].ToArray();
        var opcodeOffset = body.AsSpan().IndexOf(opcode);
        Assert.True(opcodeOffset >= 0);
        body[opcodeOffset + fieldOffset] = replacement;
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

    private static async Task<(int ExitCode, string Output, string Error)> RunTrRmAsync(string artifact)
    {
        var frameworkDirectory = new DirectoryInfo(AppContext.BaseDirectory);
        var configuration = frameworkDirectory.Parent?.Name
            ?? throw new InvalidOperationException("Test configuration directory is missing.");
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", ".."));
        var cli = Path.Combine(root, "artifacts", "bin", "sources", "terrumc",
            configuration, frameworkDirectory.Name, "terrumc.dll");
        var start = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        start.ArgumentList.Add(cli);
        start.ArgumentList.Add("run-trrm");
        start.ArgumentList.Add(artifact);
        start.ArgumentList.Add("--runtime");
        start.ArgumentList.Add("fail-fast");
        start.ArgumentList.Add("--json");
        using var process = Process.Start(start)
            ?? throw new InvalidOperationException("TRRM runner process was not started.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
        return (process.ExitCode, await output, await error);
    }
}
