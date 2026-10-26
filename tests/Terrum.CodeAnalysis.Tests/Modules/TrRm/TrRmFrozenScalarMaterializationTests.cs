// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Linq;
using NUnit.Framework;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;

namespace Terrum.CodeAnalysis.Tests.Modules.TrRm;

public sealed class TrRmFrozenScalarMaterializationTests
{
    [Test]
    public void MaterializesWindowsX64ScalarsWithoutChangingDeclaredBits()
    {
        var values = new (byte TypeCode, PrimitiveType Type, byte[] Payload, long Value)[]
        {
            (0x00, PrimitiveType.Void, [], 0),
            (0x01, PrimitiveType.Bool, [1], 1),
            (0x02, PrimitiveType.Char8, [0xE9], 0xE9),
            (0x03, PrimitiveType.Char16, [0, 0xD8], 0xD800),
            (0x04, PrimitiveType.Char32, [0x00, 0xD8, 0, 0], 0xD800),
            (0x05, PrimitiveType.Rune, [0, 0xF6, 1, 0], 0x1F600),
            (0x06, PrimitiveType.Int8, [0x80], -128),
            (0x07, PrimitiveType.Int16, [0, 0x80], -32768),
            (0x08, PrimitiveType.Int32, [0xFF, 0xFF, 0xFF, 0xFF], -1),
            (0x09, PrimitiveType.Int64, [0, 0, 0, 0, 0, 0, 0, 0x80], long.MinValue),
            (0x0B, PrimitiveType.UInt8, [0xFF], byte.MaxValue),
            (0x0C, PrimitiveType.UInt16, [0xFF, 0xFF], ushort.MaxValue),
            (0x0D, PrimitiveType.UInt32, [0xFF, 0xFF, 0xFF, 0xFF],
                unchecked((int)uint.MaxValue)),
            (0x0E, PrimitiveType.UInt64,
                [0x10, 0x32, 0x54, 0x76, 0x98, 0xBA, 0xDC, 0xFE],
                unchecked((long)0xFEDC_BA98_7654_3210)),
            (0x14, PrimitiveType.Float32, [1, 0, 0x80, 0x7F], 0x7F80_0001),
            (0x15, PrimitiveType.Float64, [1, 0, 0, 0, 0, 0, 0xF0, 0xFF],
                unchecked((long)0xFFF0_0000_0000_0001)),
        };

        foreach (var value in values)
        {
            var typeSpecRef = 0xF200_0000u | value.TypeCode;
            var artifact = TrRmRegisterBodyTestData.StandardScalarConstantEntryArtifact(
                value.TypeCode, value.Payload);
            var tables = TrRmStructuralMetadataValidator.ValidateAndGetExecutionTables(
                new TrRmArtifactIndex(artifact));
            tables.ValidateRegisterBodyMapsForMethod(1);

            var constant = TrRmConstantSourceValueValidator.MaterializeFrozenScalar(
                1, typeSpecRef, 1, FixedLayoutStorage.WindowsX64TargetRequirement,
                tables.ConstantSources, tables.FrozenValueNodes, tables.Blobs,
                tables.TypeSpecs, tables.Features, tables.Modules);

            Assert.That(constant.Type, Is.EqualTo(value.Type));
            Assert.That(constant.Value, Is.EqualTo(value.Value));
        }
    }

    [Test]
    public void MaterializesWideScalarPayloadsWithoutLosingHighBits()
    {
        var values = new (byte TypeCode, PrimitiveType Type, byte[] Payload, long LegacyValue)[]
        {
            (0x0A, PrimitiveType.Int128,
                [0x10, 0x32, 0x54, 0x76, 0x98, 0xBA, 0xDC, 0xFE,
                    0xEF, 0xCD, 0xAB, 0x89, 0x67, 0x45, 0x23, 0x81], 0),
            (0x0F, PrimitiveType.UInt128,
                [0x21, 0x43, 0x65, 0x87, 0xA9, 0xCB, 0xED, 0x0F,
                    0xFE, 0xDC, 0xBA, 0x98, 0x76, 0x54, 0x32, 0x10], 0),
            (0x12, PrimitiveType.Float16, [0x01, 0x7C], 0x7C01),
            (0x13, PrimitiveType.BFloat16, [0x81, 0x7F], 0x7F81),
            (0x16, PrimitiveType.Float128,
                [1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0xFF, 0x7F], 0),
            (0x17, PrimitiveType.Decimal32, [0x26, 0x02, 0x80, 0x31], 0x3180_0226),
            (0x18, PrimitiveType.Decimal64,
                [0x26, 0x02, 0x80, 0x31, 0x88, 0x77, 0x66, 0x55],
                0x5566_7788_3180_0226),
            (0x19, PrimitiveType.Decimal128,
                [0x26, 0x02, 0x80, 0x31, 0x88, 0x77, 0x66, 0x55,
                    0x10, 0x32, 0x54, 0x76, 0x98, 0xBA, 0xDC, 0xFE], 0),
        };

        foreach (var value in values)
        {
            var artifact = TrRmRegisterBodyTestData.StandardScalarConstantEntryArtifact(
                value.TypeCode, value.Payload, isEntryPoint: false, methodName: "Value");
            var loaded = TrRmExecutableCompiler.LoadStandardScalarReturn(artifact);
            var constant = (BoundConstant)((BoundReturn)loaded.Functions[0].Body.Statements.Single()).Value!;

            Assert.That(constant.Type, Is.EqualTo(value.Type));
            if (value.Payload.Length > sizeof(ulong))
            {
                Assert.Throws<InvalidOperationException>(() => _ = constant.Value);
                var changedHighBits = (byte[])value.Payload.Clone();
                changedHighBits[^1] ^= 0x80;
                var changedArtifact = TrRmRegisterBodyTestData.StandardScalarConstantEntryArtifact(
                    value.TypeCode, changedHighBits, isEntryPoint: false, methodName: "Value");
                var changed = (BoundConstant)((BoundReturn)TrRmExecutableCompiler
                    .LoadStandardScalarReturn(changedArtifact).Functions[0].Body.Statements.Single()).Value!;
                Assert.That(constant, Is.Not.EqualTo(changed));
            }
            else
            {
                Assert.That(constant.Value, Is.EqualTo(value.LegacyValue));
            }
        }
    }

    [Test]
    public void ConstEvalChar16WideningPreservesTheComplete128BitPayload()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            [ConstEval] int128 WidenSigned(char16 value) { return int128(value); }
            [ConstEval] uint128 WidenUnsigned(char16 value) { return uint128(value); }
            [ConstEval] int128 WidenInt64(int64 value) { return int128(value); }
            [ConstEval] uint128 WidenUInt64(uint64 value) { return uint128(value); }
            int128 SignedValue() {
                const int128 result = WidenSigned(char16(65535));
                return result;
            }
            uint128 UnsignedValue() {
                const uint128 result = WidenUnsigned(char16(65535));
                return result;
            }
            int128 SignedLongValue() {
                const int128 result = WidenInt64(int64(-1));
                return result;
            }
            uint128 UnsignedLongValue() {
                const uint128 result = WidenUInt64(uint64.MaxValue);
                return result;
            }
            int32 Main() { return 42; }
            """));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program!;

        var payloads = new[] {
            ("SignedValue", (byte)0x0A, PrimitiveType.Int128,
                new byte[] { 0xFF, 0xFF, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }),
            ("UnsignedValue", (byte)0x0F, PrimitiveType.UInt128,
                new byte[] { 0xFF, 0xFF, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }),
            ("SignedLongValue", (byte)0x0A, PrimitiveType.Int128,
                new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,
                    0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF }),
            ("UnsignedLongValue", (byte)0x0F, PrimitiveType.UInt128,
                new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,
                    0, 0, 0, 0, 0, 0, 0, 0 }),
        };
        foreach (var (name, typeCode, type, expectedPayload) in payloads)
        {
            var function = program.Functions.Single(candidate => candidate.Name == name);
            var actual = (BoundConstant)((BoundLocal)function.Body.Statements[0]).Initializer;

            var expectedArtifact = TrRmRegisterBodyTestData.StandardScalarConstantEntryArtifact(
                typeCode, expectedPayload, isEntryPoint: false, methodName: "Expected");
            var expectedProgram = TrRmExecutableCompiler.LoadStandardScalarReturn(expectedArtifact);
            var expected = (BoundConstant)((BoundReturn)expectedProgram.Functions[0].Body.Statements.Single())
                .Value!;

            Assert.That(actual.Type, Is.EqualTo(type));
            Assert.That(actual with { Span = default }, Is.EqualTo(expected));
        }
    }

    [TestCase("char8", "255")]
    [TestCase("char8", "-0")]
    [TestCase("char32", "0xD800")]
    [TestCase("rune", "0x10FFFF")]
    [TestCase("int8", "-128")]
    [TestCase("int8", "0x80")]
    [TestCase("int8", "0xFF")]
    [TestCase("int16", "-32768")]
    [TestCase("uint8", "255")]
    [TestCase("uint16", "65535")]
    public void BindsNewFixedScalarLiteralTypes(string type, string literal)
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(
            $"public {type} Value() {{ return {literal}; }}"));

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
    }

    [TestCase("int8", "128")]
    [TestCase("int8", "+0x80")]
    [TestCase("int8", "-0xFF")]
    [TestCase("uint8", "256")]
    [TestCase("char8", "'\\u00E9'")]
    [TestCase("rune", "0xD800")]
    public void RejectsOutOfRangeFixedScalarLiterals(string type, string literal)
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(
            $"public {type} Value() {{ return {literal}; }}"));

        Assert.That(compilation.Diagnostics.Any(static diagnostic => diagnostic.Code == "TRM3013"),
            Is.True, string.Join(Environment.NewLine, compilation.Diagnostics));
    }

    [TestCase("char8", "255")]
    [TestCase("char32", "0xD800")]
    [TestCase("rune", "0x10FFFF")]
    [TestCase("int8", "-128")]
    [TestCase("int16", "-32768")]
    [TestCase("uint8", "255")]
    [TestCase("uint16", "65535")]
    public void NativeExecutionAcceptsNewFixedScalarReturnTypes(string type, string literal)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(
            $"{type} Value() {{ return {literal}; }} int32 Main() {{ _ = Value(); return 42; }}"));

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(NativeJit.Execute(compilation.Program!, NativeRuntimePolicy.FailFast),
            Is.EqualTo(42));
        Assert.That(NativeEmitter.Emit(compilation.Program!, NativeRuntimePolicy.FailFast), Is.Not.Empty);
    }

    [Test]
    public void MaterializedConstantMapValueLowersAndExecutes()
    {
        var artifact = TrRmRegisterBodyTestData.StandardScalarConstantEntryArtifact(
            0x08, [42, 0, 0, 0]);
        var loaded = TrRmExecutableCompiler.LoadStandardScalarReturn(artifact);

        Assert.That(NativeJit.Execute(loaded, NativeRuntimePolicy.FailFast), Is.EqualTo(42));
        Assert.That(NativeEmitter.Emit(loaded, NativeRuntimePolicy.FailFast), Is.Not.Empty);
    }

    [Test]
    public void MaterializedSmallScalarFunctionsSurviveSourceFreeLinkingAndExecute()
    {
        var values = new (byte TypeCode, byte[] Payload)[]
        {
            (0x02, [0xE9]),
            (0x04, [0, 0xD8, 0, 0]),
            (0x05, [0, 0xF6, 1, 0]),
            (0x06, [0x80]),
            (0x07, [0, 0x80]),
            (0x0B, [0xFF]),
            (0x0C, [0xFF, 0xFF]),
            (0x0D, [0xFF, 0xFF, 0xFF, 0xFF]),
        };

        foreach (var value in values)
        {
            var artifact = TrRmRegisterBodyTestData.StandardScalarConstantEntryArtifact(
                value.TypeCode, value.Payload, isEntryPoint: false, methodName: "Value");
            var loaded = TrRmExecutableCompiler.LoadStandardScalarReturn(artifact);
            Assert.That(loaded.EntryPoint, Is.EqualTo(-1));
            var producer = ModuleCompiler.CreateProducer(loaded, ["Value"]);
            var consumerCompilation = Compilation.Create(SyntaxTree.Parse(
                "int32 Main() { _ = Value(); return 42; }"),
                ModuleCompiler.CreateReference(producer));
            Assert.That(consumerCompilation.HasErrors, Is.False,
                string.Join(Environment.NewLine, consumerCompilation.Diagnostics));
            var consumer = ModuleCompiler.CreateConsumer(consumerCompilation.Program!, producer);
            var linked = ModuleCompiler.Link(consumer, producer);

            Assert.That(NativeJit.Execute(linked, NativeRuntimePolicy.FailFast), Is.EqualTo(42));
            Assert.That(NativeEmitter.Emit(linked, NativeRuntimePolicy.FailFast), Is.Not.Empty);
        }
    }

    [TestCase(0x01, 0x0F, PrimitiveType.Bool, 1)]
    [TestCase(0x02, 0x0F, PrimitiveType.Char8, 255)]
    [TestCase(0x03, 0x0F, PrimitiveType.Char16, 65535)]
    [TestCase(0x04, 0x0F, PrimitiveType.Char32, 4294967295L)]
    [TestCase(0x05, 0x0E, PrimitiveType.Rune, 0)]
    [TestCase(0x06, 0x0F, PrimitiveType.Int8, -1)]
    [TestCase(0x07, 0x0F, PrimitiveType.Int16, -1)]
    [TestCase(0x0B, 0x0F, PrimitiveType.UInt8, 255)]
    [TestCase(0x0C, 0x0F, PrimitiveType.UInt16, 65535)]
    [TestCase(0x0D, 0x0F, PrimitiveType.UInt32, -1)]
    public void MaterializesTypedRegisterConstantsForScalarLibraryReturns(
        byte typeCode, byte register, PrimitiveType expectedType, long expectedValue)
    {
        var artifact = TrRmRegisterBodyTestData.StandardScalarReturnArtifact(
            register, typeCode, isEntryPoint: false, methodName: "Value");
        var program = TrRmExecutableCompiler.LoadStandardScalarReturn(artifact);
        var function = program.Functions.Single();
        var result = (BoundConstant)((BoundReturn)function.Body.Statements.Single()).Value!;

        Assert.That(program.EntryPoint, Is.EqualTo(-1));
        Assert.That(function.ReturnType, Is.EqualTo(expectedType));
        Assert.That(result.Type, Is.EqualTo(expectedType));
        Assert.That(result.Value, Is.EqualTo(expectedValue));
    }

    [Test]
    public void R15DoesNotMaterializeAnInvalidRuneReturn()
    {
        var artifact = TrRmRegisterBodyTestData.StandardScalarReturnArtifact(
            0x0F, 0x05, isEntryPoint: false, methodName: "Value");

        Assert.Throws<NotSupportedException>(
            () => TrRmExecutableCompiler.LoadStandardScalarReturn(artifact));
    }

    [Test]
    public void RejectsScalarMaterializationForAnUnselectedTarget()
    {
        var artifact = TrRmRegisterBodyTestData.StandardScalarConstantEntryArtifact(
            0x08, [42, 0, 0, 0]);
        var tables = TrRmStructuralMetadataValidator.ValidateAndGetExecutionTables(
            new TrRmArtifactIndex(artifact));
        tables.ValidateRegisterBodyMapsForMethod(1);

        var rejected = false;
        try
        {
            _ = TrRmConstantSourceValueValidator.MaterializeFrozenScalar(
                1, 0xF200_0008, 1, 0, tables.ConstantSources,
                tables.FrozenValueNodes, tables.Blobs, tables.TypeSpecs,
                tables.Features, tables.Modules);
        }
        catch (NotSupportedException)
        {
            rejected = true;
        }
        Assert.That(rejected, Is.True);
    }

    [Test]
    public void MaterializesInt128WithoutNarrowingItsHighWord()
    {
        var artifact = TrRmRegisterBodyTestData.StandardScalarConstantEntryArtifact(
            0x0A, [0x80, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0]);
        var tables = TrRmStructuralMetadataValidator.ValidateAndGetExecutionTables(
            new TrRmArtifactIndex(artifact));
        tables.ValidateRegisterBodyMapsForMethod(1);

        var constant = TrRmConstantSourceValueValidator.MaterializeFrozenScalar(
            1, 0xF200_000A, 1, FixedLayoutStorage.WindowsX64TargetRequirement,
            tables.ConstantSources, tables.FrozenValueNodes, tables.Blobs,
            tables.TypeSpecs, tables.Features, tables.Modules);

        Assert.That(constant.Type, Is.EqualTo(PrimitiveType.Int128));
        Assert.Throws<InvalidOperationException>(() => _ = constant.Value);
    }

    [Test]
    public void NativeLoweringPreservesUnsigned32ConstantBits()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            uint32 Max() { return 4294967295; }
            int32 Main() {
                if (Max() == 4294967295) { return 42; }
                return 0;
            }
            """));

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(NativeJit.Execute(compilation.Program!, NativeRuntimePolicy.FailFast),
            Is.EqualTo(42));
        Assert.That(NativeEmitter.Emit(compilation.Program!, NativeRuntimePolicy.FailFast),
            Is.Not.Empty);
    }
}
