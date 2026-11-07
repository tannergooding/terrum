// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using Terrum.CodeAnalysis.Lexing;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Terrum.CodeAnalysis.Tests.Native.Jit;

namespace Terrum.CodeAnalysis.Tests.Modules.TrRm;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "NUnit instantiates this fixture through reflection.")]
internal sealed class TrRmFrozenScalarMaterializationTests
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
                value.TypeCode, value.Payload,
                schema: TrRmArtifactSchema.ScalarFrozenValuesDraft);
            var tables = TrRmStructuralMetadataValidator
                .ValidateScalarFrozenValuesDraftAndGetExecutionTables(
                    TrRmArtifactIndex.ForScalarFrozenValuesDraft(artifact));
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
                value.TypeCode, value.Payload, isEntryPoint: false, methodName: "Value",
                schema: TrRmArtifactSchema.ScalarFrozenValuesDraft);
            var loaded = TrRmExecutableCompiler.LoadScalarFrozenValuesDraft(artifact);
            var constant = (BoundConstant)((BoundReturn)loaded.Functions[0].Body.Statements.Single()).Value!;

            Assert.That(constant.Type, Is.EqualTo(value.Type));
            if (value.Payload.Length > sizeof(ulong))
            {
                _ = Assert.Throws<InvalidOperationException>(() => _ = constant.Value);
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
    public void ScalarFrozenValuesDraftMaterializesWithoutNominalUnionFeature()
    {
        var artifact = TrRmRegisterBodyTestData.StandardScalarConstantEntryArtifact(
            0x08, [42, 0, 0, 0], schema: TrRmArtifactSchema.ScalarFrozenValuesDraft);
        var index = TrRmArtifactIndex.ForScalarFrozenValuesDraft(artifact);
        var tables = TrRmStructuralMetadataValidator
            .ValidateScalarFrozenValuesDraftAndGetExecutionTables(index);

        Assert.That(TrRmFeatureManifestValidator.Requires(tables.Features,
            tables.Modules, 1, TrRmFeatureTableReader.TypedFrozenValuesFeatureId,
            index.Schema), Is.True);
        Assert.That(TrRmFeatureManifestValidator.Requires(tables.Features,
            tables.Modules, 1, TrRmFeatureTableReader.NominalUnionMetadataFeatureId,
            index.Schema), Is.False);

        var loaded = TrRmExecutableCompiler.LoadScalarFrozenValuesDraft(artifact);
        Assert.That(NativeJit.Execute(loaded, NativeRuntimePolicy.FailFast), Is.EqualTo(42));
    }

    [Test]
    public void ScalarFrozenValuesDraftProducerWritesAndExecutesConstantReturn()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(
            "int32 Main() { return 42; }"));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var artifact = TrRmExecutableCompiler.EmitScalarFrozenValuesDraftConstantReturn(
            compilation.Program!, "scalar-producer");
        var loaded = TrRmExecutableCompiler.LoadScalarFrozenValuesDraft(artifact);

        Assert.That(NativeJit.Execute(loaded, NativeRuntimePolicy.FailFast), Is.EqualTo(42));
        Assert.That(NativeEmitter.Emit(loaded, NativeRuntimePolicy.FailFast), Is.Not.Empty);
    }

    [Test]
    public void ScalarFrozenValuesDraftInternsAcrossMethodsAndReemitsSourceFree()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            int32 SharedA() { return 42; }
            int32 Main() { return 42; }
            int32 SharedB() { return 42; }
            """));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var artifact = TrRmExecutableCompiler.EmitScalarFrozenValuesDraftConstantReturn(
            compilation.Program!, "scalar-producer");
        var tables = TrRmStructuralMetadataValidator
            .ValidateScalarFrozenValuesDraftAndGetExecutionTables(
                TrRmArtifactIndex.ForScalarFrozenValuesDraft(artifact));
        var loaded = TrRmExecutableCompiler.LoadScalarFrozenValuesDraft(artifact);
        var reemitted = TrRmExecutableCompiler.EmitScalarFrozenValuesDraftConstantReturn(
            loaded, "scalar-producer");

        Assert.That(tables.Methods.Count, Is.EqualTo(3));
        Assert.That(tables.ConstantSources.Count, Is.EqualTo(1));
        Assert.That(tables.FrozenValueNodes.Count, Is.EqualTo(1));
        Assert.That(tables.Blobs.Count, Is.EqualTo(1));
        Assert.That(loaded.EntryPoint, Is.EqualTo(1));
        Assert.That(reemitted, Is.EqualTo(artifact));
        Assert.That(NativeJit.Execute(loaded, NativeRuntimePolicy.FailFast), Is.EqualTo(42));
        Assert.That(NativeEmitter.Emit(loaded, NativeRuntimePolicy.FailFast), Is.Not.Empty);
    }

    [Test]
    public void ScalarFrozenValuesDraftRemapsDuplicateRowsThroughSourceFreeRoundTrip()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            int32 SharedA() { return 42; }
            int32 Main() { return 42; }
            """));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var canonical = TrRmExecutableCompiler.EmitScalarFrozenValuesDraftConstantReturn(
            compilation.Program!, "scalar-producer");
        var duplicateRows = DuplicateScalarValueRows(canonical);
        var duplicateTables = TrRmStructuralMetadataValidator
            .ValidateScalarFrozenValuesDraftAndGetExecutionTables(
                TrRmArtifactIndex.ForScalarFrozenValuesDraft(duplicateRows));
        var loaded = TrRmExecutableCompiler.LoadScalarFrozenValuesDraft(duplicateRows);
        var reemitted = TrRmExecutableCompiler.EmitScalarFrozenValuesDraftConstantReturn(
            loaded, "scalar-producer");

        Assert.That(duplicateTables.ConstantSources.Count, Is.EqualTo(2));
        Assert.That(duplicateTables.FrozenValueNodes.Count, Is.EqualTo(2));
        Assert.That(duplicateTables.Blobs.Count, Is.EqualTo(2));
        Assert.That(reemitted, Is.EqualTo(canonical));
        Assert.That(NativeJit.Execute(loaded, NativeRuntimePolicy.FailFast), Is.EqualTo(42));
    }

    [Test]
    public void ScalarFrozenValuesDraftProducerRoundTripsAllNonUnitScalarTypes()
    {
        var functions = ImmutableArray.CreateBuilder<BoundFunction>();
        var expected = new List<(PrimitiveType Type, byte[] Payload)>();
        for (var candidate = 0; candidate <= 0x19; candidate++)
        {
            var typeCode = (byte)candidate;
            if (!TrRmConstantSourceValueValidator.TryGetBoundScalarType(
                typeCode, out var type) || type == PrimitiveType.Void)
            {
                continue;
            }

            var payload = new byte[PrimitiveTypeFacts.ScalarWidth(type)];
            for (var index = 0; index < payload.Length; index++)
            {
                payload[index] = unchecked((byte)((typeCode * 17) + index + 1));
            }
            if (type == PrimitiveType.Bool)
            {
                payload[0] = 1;
            }
            else if (type == PrimitiveType.Rune)
            {
                payload = [0x00, 0xF6, 0x01, 0x00];
            }

            var constant = new BoundConstant(
                ScalarBits.FromPayload(type, payload), type, default);
            functions.Add(new BoundFunction($"Value{typeCode:X2}", type, [], 0,
                new BoundBlock([new BoundReturn(constant, default)], default), default));
            expected.Add((type, payload));
        }

        var program = new BoundProgram(functions.ToImmutable(), -1);
        var artifact = TrRmExecutableCompiler.EmitScalarFrozenValuesDraftConstantReturn(
            program, "scalar-producer");
        var loaded = TrRmExecutableCompiler.LoadScalarFrozenValuesDraft(artifact);

        Assert.That(loaded.Functions.Length, Is.EqualTo(expected.Count));
        for (var index = 0; index < expected.Count; index++)
        {
            var actual = (BoundConstant)((BoundReturn)loaded.Functions[index]
                .Body.Statements.Single()).Value!;
            var expectedBits = ScalarBits.FromPayload(expected[index].Type,
                expected[index].Payload);
            Assert.That(actual.Type, Is.EqualTo(expected[index].Type));
            Assert.That(actual.Bits, Is.EqualTo(expectedBits));
        }
    }

    [Test]
    public async Task FixedScalarConstantsExecuteAcrossNativeCallCarriersAsync()
    {
        var values = new (byte TypeCode, PrimitiveType Type, byte[] Payload)[]
        {
            (0x01, PrimitiveType.Bool, [1]),
            (0x02, PrimitiveType.Char8, [0xE9]),
            (0x03, PrimitiveType.Char16, [0, 0xD8]),
            (0x04, PrimitiveType.Char32, [0xFF, 0xFF, 0x10, 0]),
            (0x05, PrimitiveType.Rune, [0, 0xF6, 1, 0]),
            (0x06, PrimitiveType.Int8, [0x80]),
            (0x07, PrimitiveType.Int16, [0, 0x80]),
            (0x08, PrimitiveType.Int32, [0xFF, 0xFF, 0xFF, 0xFF]),
            (0x09, PrimitiveType.Int64, [0, 0, 0, 0, 0, 0, 0, 0x80]),
            (0x0A, PrimitiveType.Int128,
                [0x10, 0x32, 0x54, 0x76, 0x98, 0xBA, 0xDC, 0xFE,
                    0xEF, 0xCD, 0xAB, 0x89, 0x67, 0x45, 0x23, 0x81]),
            (0x0B, PrimitiveType.UInt8, [0xFF]),
            (0x0C, PrimitiveType.UInt16, [0xFF, 0xFF]),
            (0x0D, PrimitiveType.UInt32, [0xFF, 0xFF, 0xFF, 0xFF]),
            (0x0E, PrimitiveType.UInt64,
                [0x10, 0x32, 0x54, 0x76, 0x98, 0xBA, 0xDC, 0xFE]),
            (0x0F, PrimitiveType.UInt128,
                [0x21, 0x43, 0x65, 0x87, 0xA9, 0xCB, 0xED, 0x0F,
                    0xFE, 0xDC, 0xBA, 0x98, 0x76, 0x54, 0x32, 0x10]),
            (0x12, PrimitiveType.Float16, [0x00, 0x3C]),
            (0x13, PrimitiveType.BFloat16, [0x80, 0x3F]),
            (0x16, PrimitiveType.Float128,
                [0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0xFF, 0x3F]),
            (0x17, PrimitiveType.Decimal32, [0x78, 0x00, 0x80, 0x31]),
            (0x18, PrimitiveType.Decimal64,
                [0x78, 0x00, 0, 0, 0, 0, 0x80, 0x31]),
            (0x19, PrimitiveType.Decimal128,
                [0x78, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0x3C, 0x30]),
        };

        foreach (var value in values)
        {
            var artifact = TrRmRegisterBodyTestData.StandardScalarConstantEntryArtifact(
                value.TypeCode, value.Payload, isEntryPoint: false, methodName: "Value");
            var program = TrRmExecutableCompiler.LoadStandardScalarReturn(artifact);
            var result = (BoundConstant)((BoundReturn)program.Functions[0]
                .Body.Statements.Single()).Value!;
            var identity = new BoundFunction("Identity", value.Type,
                [new BoundVariable("value", value.Type, false, default)], 1,
                new BoundBlock(
                    [new BoundReturn(new BoundRead(0, value.Type, default), default)],
                    default),
                default);
            var valueCall = new BoundCall(0, [], value.Type, default);
            var identityCall = new BoundCall(1, [valueCall], value.Type, default);
            var matchesValue = new BoundBinary(TokenKind.EqualsEquals, identityCall,
                result, PrimitiveType.Bool, default);
            var main = new BoundFunction("Main", PrimitiveType.Int32, [], 0,
                new BoundBlock(
                    [
                        new BoundIf(matchesValue,
                            new BoundReturn(new BoundConstant(42, PrimitiveType.Int32, default), default),
                            new BoundReturn(new BoundConstant(0, PrimitiveType.Int32, default), default),
                            default),
                    ],
                    default),
                default);
            var executable = program with
            {
                Functions = program.Functions.Add(identity).Add(main),
                EntryPoint = 2,
            };

            await JitTests.AssertProgramExecutionAsync(
                executable, 42, NativeRuntimePolicy.FailFast).ConfigureAwait(true);
            await JitTests.AssertProgramExecutionAsync(
                executable, 42, NativeRuntimePolicy.CheckedExceptions).ConfigureAwait(true);
        }
    }

    [Test]
    public async Task Float128NegativeZeroSignSurvivesFrozenLoadAndNativeCallsAsync()
    {
        var negativeZeroPayload = new byte[16];
        negativeZeroPayload[^1] = 0x80;
        var artifact = TrRmRegisterBodyTestData.StandardScalarConstantEntryArtifact(
            0x16, negativeZeroPayload, isEntryPoint: false, methodName: "Value",
            schema: TrRmArtifactSchema.ScalarFrozenValuesDraft);
        var program = TrRmExecutableCompiler.LoadScalarFrozenValuesDraft(artifact);
        var identity = new BoundFunction("Identity", PrimitiveType.Float128,
            [new BoundVariable("value", PrimitiveType.Float128, false, default)], 1,
            new BoundBlock([
                new BoundReturn(new BoundRead(0, PrimitiveType.Float128, default), default),
            ], default), default);
        var valueCall = new BoundCall(0, [], PrimitiveType.Float128, default);
        var identityCall = new BoundCall(1, [valueCall], PrimitiveType.Float128, default);
        var onePayload = new byte[]
        {
            0, 0, 0, 0, 0, 0, 0, 0,
            0, 0, 0, 0, 0, 0, 0xFF, 0x3F,
        };
        var one = new BoundConstant(ScalarBits.FromPayload(PrimitiveType.Float128,
            onePayload), PrimitiveType.Float128, default);
        var zero = new BoundConstant(ScalarBits.FromPayload(PrimitiveType.Float128,
            new byte[16]), PrimitiveType.Float128, default);
        var reciprocal = new BoundBinary(TokenKind.Slash, one, identityCall,
            PrimitiveType.Float128, default)
        {
            OverflowBehavior = BoundIntegerOverflowBehavior.Checked,
        };
        var negativeInfinity = new BoundBinary(TokenKind.Less, reciprocal, zero,
            PrimitiveType.Bool, default)
        {
            OverflowBehavior = BoundIntegerOverflowBehavior.Checked,
        };
        var main = new BoundFunction("Main", PrimitiveType.Int32, [], 0,
            new BoundBlock([
                new BoundIf(negativeInfinity,
                    new BoundReturn(new BoundConstant(42, PrimitiveType.Int32, default), default),
                    new BoundReturn(new BoundConstant(0, PrimitiveType.Int32, default), default),
                    default),
            ], default), default);
        var executable = program with
        {
            Functions = program.Functions.Add(identity).Add(main),
            EntryPoint = 2,
        };

        await JitTests.AssertProgramExecutionAsync(executable, 42,
            NativeRuntimePolicy.FailFast).ConfigureAwait(true);
        await JitTests.AssertProgramExecutionAsync(executable, 42,
            NativeRuntimePolicy.CheckedExceptions).ConfigureAwait(true);
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

        _ = Assert.Throws<NotSupportedException>(
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
        _ = Assert.Throws<InvalidOperationException>(() => _ = constant.Value);
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

    private static byte[] DuplicateScalarValueRows(byte[] artifact)
    {
        var index = TrRmArtifactIndex.ForScalarFrozenValuesDraft(artifact);
        if (!index.TryGetSection(0x0003, out var originalBlobSection, out var originalBlobCount)
            || originalBlobCount != 1
            || !index.TryGetSection(0x0020, out var originalBodySection, out var bodyCount)
            || bodyCount != 2)
        {
            throw new InvalidOperationException("The producer artifact does not contain the expected scalar rows.");
        }

        var originalBlobs = new TrRmBlobTableReader(originalBlobSection, originalBlobCount);
        var payload = originalBlobs.GetRow(1).ToArray();
        var blobSection = TrRmVariableTableWriter.EncodeBlobs([
            new ReadOnlyMemory<byte>(payload),
            new ReadOnlyMemory<byte>(payload),
        ]);
        var blobs = new TrRmBlobTableReader(blobSection, 2);
        var typeSpecs = default(TrRmTypeSpecTableReader);
        var frozenValueNodeSection = TrRmFrozenValueNodeTableWriter.Encode([
            new TrRmFrozenValueNodeRow(0xF200_0008, 1, 1, 0, 0, 0),
            new TrRmFrozenValueNodeRow(0xF200_0008, 1, 2, 0, 0, 0),
        ], typeSpecs, blobs, 0, 0);
        var constantSourceSection = TrRmConstantSourceTableWriter.Encode([
            new TrRmConstantSourceRow(1, 1),
            new TrRmConstantSourceRow(1, 2),
        ], 2, 0, 0);

        var bodyIndex = new TrRmVariableTableIndex(originalBodySection,
            bodyCount, 1, 1);
        var firstBody = bodyIndex.GetRow(1).ToArray();
        var secondBody = bodyIndex.GetRow(2).ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(secondBody.AsSpan(25), 2);
        var bodySection = TrRmVariableTableWriter.EncodeBodies([
            new ReadOnlyMemory<byte>(firstBody),
            new ReadOnlyMemory<byte>(secondBody),
        ]);

        var sections = new List<TrRmSection>(index.SectionCount);
        for (var sectionIndex = 0; sectionIndex < index.SectionCount; sectionIndex++)
        {
            index.GetSection(sectionIndex, out var kind, out var flags,
                out var bytes, out var recordCount);
            var replacementBytes = kind switch
            {
                0x0003 => blobSection,
                0x0020 => bodySection,
                0x0030 => constantSourceSection,
                0x003F => frozenValueNodeSection,
                _ => bytes.ToArray(),
            };
            var replacementCount = kind is 0x0003 or 0x0030 or 0x003F
                ? 2u : checked((uint)recordCount);
            sections.Add(new TrRmSection(kind, flags, replacementCount,
                replacementBytes));
        }

        return TrRmArtifactWriter.Encode(sections,
            TrRmArtifactSchema.ScalarFrozenValuesDraft);
    }
}
