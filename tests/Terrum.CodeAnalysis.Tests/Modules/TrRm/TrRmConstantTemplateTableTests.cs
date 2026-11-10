// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.IO;
using NUnit.Framework;
using Terrum.CodeAnalysis.Modules;

namespace Terrum.CodeAnalysis.Tests.Modules.TrRm;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "NUnit instantiates this fixture through reflection.")]
internal sealed class TrRmConstantTemplateTableTests
{
    [Test]
    public void ConstantTemplateUsesExactZeroAndOneActualExtents()
    {
        var noActuals = TrRmConstantTemplateTableWriter.Encode([
            new TrRmConstantTemplateRow(0x1600_0001, 1, 0, []),
        ], TrRmArtifactSchema.GenericConstantMethodsDraft);
        var oneActual = TrRmConstantTemplateTableWriter.Encode([
            new TrRmConstantTemplateRow(0x1600_0001, 1, 0,
                [new TrRmConstantTemplateActual(2, 1)]),
        ], TrRmArtifactSchema.GenericConstantMethodsDraft);

        Assert.That(new TrRmVariableTableIndex(noActuals, 1, 16, 1)
            .GetRow(1).Length, Is.EqualTo(16));
        Assert.That(new TrRmVariableTableIndex(oneActual, 1, 16, 1)
            .GetRow(1).Length, Is.EqualTo(24));
        Assert.That(Read(oneActual), Is.EqualTo(
            (0x1600_0001u, 1u, 0u, 1,
                new TrRmConstantTemplateActual(2, 1))));

        var importedHelper = TrRmConstantTemplateTableWriter.Encode([
            new TrRmConstantTemplateRow(0x1C00_0001, 1, 0,
                [new TrRmConstantTemplateActual(2, 1)]),
        ], TrRmArtifactSchema.GenericConstantMethodsDraft);
        Assert.That(Read(importedHelper, importedHelper: true).SelectedMethodReference,
            Is.EqualTo(0x1C00_0001u));

        var invalidImport = TrRmConstantTemplateTableWriter.Encode([
            new TrRmConstantTemplateRow(0x1C00_0002, 1, 0,
                [new TrRmConstantTemplateActual(2, 1)]),
        ], TrRmArtifactSchema.GenericConstantMethodsDraft);
        _ = Assert.Throws<InvalidDataException>(() =>
            Read(invalidImport, importedHelper: true));
    }

    [Test]
    public void ConstantTemplateRequiresCg1AndExactActualRows()
    {
        var bytes = TrRmConstantTemplateTableWriter.Encode([
            new TrRmConstantTemplateRow(0x1600_0001, 1, 0,
                [new TrRmConstantTemplateActual(2, 1)]),
        ], TrRmArtifactSchema.GenericConstantMethodsDraft);
        _ = Assert.Throws<InvalidDataException>(() =>
            Read(bytes, TrRmArtifactSchema.GenericMethodCallsDraft));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() =>
            TrRmConstantTemplateTableWriter.Encode([
                new TrRmConstantTemplateRow(0x1600_0001, 1, 0, []),
            ], TrRmArtifactSchema.GenericMethodCallsDraft));

        var trailingByte = new byte[bytes.Length + 1];
        bytes.CopyTo(trailingByte, 0);
        _ = Assert.Throws<InvalidDataException>(() => Read(trailingByte));

        var invalidFlags = (byte[])bytes.Clone();
        invalidFlags[25] = 1;
        _ = Assert.Throws<InvalidDataException>(() => Read(invalidFlags));

        var invalidReserved = (byte[])bytes.Clone();
        invalidReserved[26] = 1;
        _ = Assert.Throws<InvalidDataException>(() => Read(invalidReserved));
    }

    [Test]
    public void ConstantTemplateChecksLocalHelperAndActualKinds()
    {
        var wrongKind = TrRmConstantTemplateTableWriter.Encode([
            new TrRmConstantTemplateRow(0x1600_0001, 1, 0,
                [new TrRmConstantTemplateActual(1, 1)]),
        ], TrRmArtifactSchema.GenericConstantMethodsDraft);
        _ = Assert.Throws<InvalidDataException>(() => Read(wrongKind));

        var wrongSource = TrRmConstantTemplateTableWriter.Encode([
            new TrRmConstantTemplateRow(0x1600_0001, 1, 0,
                [new TrRmConstantTemplateActual(2, 2)]),
        ], TrRmArtifactSchema.GenericConstantMethodsDraft);
        _ = Assert.Throws<InvalidDataException>(() => Read(wrongSource));

        var wrongTarget = TrRmConstantTemplateTableWriter.Encode([
            new TrRmConstantTemplateRow(0x1600_0001, 1, 1,
                [new TrRmConstantTemplateActual(2, 1)]),
        ], TrRmArtifactSchema.GenericConstantMethodsDraft);
        _ = Assert.Throws<InvalidDataException>(() => Read(wrongTarget));

        var parameterizedHelper = TrRmConstantTemplateTableWriter.Encode([
            new TrRmConstantTemplateRow(0x1C00_0001, 1, 0,
                [new TrRmConstantTemplateActual(2, 1)]),
        ], TrRmArtifactSchema.GenericConstantMethodsDraft);
        _ = Assert.Throws<InvalidDataException>(() =>
            Read(parameterizedHelper, parameterCount: 1, importedHelper: true));

        var invalidMethod = TrRmConstantTemplateTableWriter.Encode([
            new TrRmConstantTemplateRow(0x1400_0001, 1, 0, []),
        ], TrRmArtifactSchema.GenericConstantMethodsDraft);
        _ = Assert.Throws<InvalidDataException>(() => Read(invalidMethod));
    }

    private static (
        uint SelectedMethodReference,
        uint ResultTypeSpecRef,
        uint TargetProfileRef,
        int ArgumentCount,
        TrRmConstantTemplateActual Actual) Read(byte[] section,
        TrRmArtifactSchema schema = TrRmArtifactSchema.GenericConstantMethodsDraft,
        int parameterCount = 0, bool importedHelper = false)
    {
        var stringBytes = TrRmStringTableWriter.EncodeNames([
            ("Only", 0), ("Run", 0), ("P", 0),
            ("Producer", 0), ("RunExport", 0),
        ]);
        var strings = new TrRmStringTableReader(stringBytes, 5);
        var moduleBytes = TrRmModuleTableWriter.Encode([
            new TrRmModuleRow(1, 0, 0, 0, 1, 1, 1, 1),
        ], strings, importedHelper ? 1 : 0, 0, 1, 0);
        var modules = new TrRmModuleTableReader(moduleBytes, 1, strings,
            importedHelper ? 1 : 0, 0, 1, 0);
        var typeDefBytes = Array.Empty<byte>();
        var types = new TrRmTypeDefTableReader(typeDefBytes, 0, strings,
            modules, 0, 0, 0, 0);
        var typeSpecBytes = TrRmTypeSpecTableWriter.Encode([
            TrRmTypeSpecTableWriter.Builtin(8),
        ], 0, 0, 1, 0, schema, methodDefCount: 1);
        var typeSpecs = new TrRmTypeSpecTableReader(typeSpecBytes, 1,
            0, 0, 1, 0, schema, methodDefCount: 1);
        var signatureBytes = TrRmMethodSigTableWriter.Encode([
            new TrRmMethodSigRow(1, 0,
                parameterCount == 0 ? 0 : 1, parameterCount,
                1, 1),
        ], typeSpecs, parameterCount, 1);
        var signatures = new TrRmMethodSigTableReader(signatureBytes, 1,
            typeSpecs, parameterCount, 1);
        var methodBytes = TrRmMethodDefTableWriter.Encode([
            new TrRmMethodDefRow(0, 2, 1, 0, 0, 0,
                importedHelper ? 0 : 1),
        ], strings, modules, types, signatures, 0,
            importedHelper ? 0 : 1);
        var methods = new TrRmMethodDefTableReader(methodBytes, 1, strings,
            modules, types, signatures, 0, importedHelper ? 0 : 1);
        var imports = default(TrRmImportTableReader);
        if (importedHelper)
        {
            var moduleRefBytes = TrRmModuleRefTableWriter.Encode([
                new TrRmModuleRefRow(4, 0, 0),
            ], strings, modules);
            var moduleRefs = new TrRmModuleRefTableReader(moduleRefBytes, 1,
                strings, modules);
            var importBytes = TrRmImportTableWriter.Encode([
                new TrRmImportRow(1, 5, 0x1600_0001),
            ], strings, moduleRefs, types, methods, default);
            imports = new TrRmImportTableReader(importBytes, 1,
                strings, moduleRefs, types, methods, default);
        }
        var genericParamBytes = TrRmGenericParamTableWriter.Encode([
            new TrRmGenericParamRow(0x1600_0001, 0, 3, 0, 0,
                2, 0, 0, 1, 0),
        ], strings, types, methods, signatures, typeSpecs, 0);
        var genericParams = new TrRmGenericParamTableReader(genericParamBytes,
            1, strings, types, methods, signatures, typeSpecs, 0, 1);
        var sourceBytes = TrRmConstantSourceTableWriter.Encode([
            new TrRmConstantSourceRow(1, 1),
        ], 1, 0, 0, 1, 0, schema);
        var constantSources = new TrRmConstantSourceTableReader(sourceBytes,
            1, 1, 0, 0, 1, 0, schema);
        var targetProfiles = new TrRmTargetProfileTableReader([], 0, schema);
        var templates = new TrRmConstantTemplateTableReader(section, 1,
            methods, imports, signatures, genericParams, typeSpecs,
            constantSources, targetProfiles, schema);
        return (templates.GetSelectedMethodReference(1),
            templates.GetResultTypeSpecRef(1),
            templates.GetTargetProfileRef(1),
            templates.GetArgumentCount(1),
            templates.GetActual(1, 0));
    }
}
