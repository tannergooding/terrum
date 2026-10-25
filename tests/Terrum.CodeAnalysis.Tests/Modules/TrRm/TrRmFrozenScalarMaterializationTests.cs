// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
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
            (0x03, PrimitiveType.Char16, [0, 0xD8], 0xD800),
            (0x08, PrimitiveType.Int32, [0xFF, 0xFF, 0xFF, 0xFF], -1),
            (0x09, PrimitiveType.Int64, [0, 0, 0, 0, 0, 0, 0, 0x80], long.MinValue),
            (0x0D, PrimitiveType.UInt32, [0xFF, 0xFF, 0xFF, 0xFF], uint.MaxValue),
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
    public void MaterializedConstantMapValueLowersAndExecutes()
    {
        var artifact = TrRmRegisterBodyTestData.StandardScalarConstantEntryArtifact(
            0x08, [42, 0, 0, 0]);
        var loaded = TrRmExecutableCompiler.LoadStandardScalarReturn(artifact);

        Assert.That(NativeJit.Execute(loaded, NativeRuntimePolicy.FailFast), Is.EqualTo(42));
        Assert.That(NativeEmitter.Emit(loaded, NativeRuntimePolicy.FailFast), Is.Not.Empty);
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
    public void RejectsScalarsWithoutAnExactBoundRepresentation()
    {
        var artifact = TrRmRegisterBodyTestData.StandardScalarConstantEntryArtifact(
            0x06, [0x80]);
        var tables = TrRmStructuralMetadataValidator.ValidateAndGetExecutionTables(
            new TrRmArtifactIndex(artifact));
        tables.ValidateRegisterBodyMapsForMethod(1);

        var rejected = false;
        try
        {
            _ = TrRmConstantSourceValueValidator.MaterializeFrozenScalar(
                1, 0xF200_0006, 1, FixedLayoutStorage.WindowsX64TargetRequirement,
                tables.ConstantSources, tables.FrozenValueNodes, tables.Blobs,
                tables.TypeSpecs, tables.Features, tables.Modules);
        }
        catch (NotSupportedException)
        {
            rejected = true;
        }
        Assert.That(rejected, Is.True);
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
