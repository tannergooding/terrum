// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Terrum.CodeAnalysis.Text;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests;

public sealed partial class ImportTableValidationComponentTests
{
    private const string ExportVerificationSource = """
        namespace Terrum.CodeAnalysis;
        int32 Main() {
            bool importsValid = TryValidateImportTable(
                Host.Expected[7], Host.Expected[1], Host.Expected[2],
                Host.Expected[3], Host.Expected[4], Host.Expected[5],
                Host.Expected[6]);
            bool exportsValid = TryValidateExportTable(
                Host.Expected[0], Host.Expected[1], Host.Expected[2],
                Host.Expected[3], Host.Expected[4], Host.Expected[5],
                Host.Expected[6], Host.Expected[7]);
            if (!importsValid || exportsValid != (Host.Expected[8] != 0)) {
                return 1;
            }
            return 0;
        }
        """;
    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
    public async Task ExportValidationMatchesHostedReader(BoundRuntimePolicy policy)
    {
        var valid = CreateValidCase();
        valid = valid with { Methods = SetWord(valid.Methods, 12, 2) };
        var property = CreatePropertyExportCase(valid);
        var cases = new List<ExportCase> {
            NewCase(valid, ExportRows()),
            NewCase(valid, ExportRows((0x1300_0001, 5))),
            NewCase(valid, ExportRows((0x1600_0001, 6))),
            NewCase(valid, ExportRows((0x1300_0001, 5), (0x1300_0002, 5))),
            NewCase(valid, ExportRows((0x1300_0001, 5), (0x1600_0001, 6))),
            NewCase(valid, ExportRows((0x1300_0001, 5), (0x1300_0001, 6))),
            CreateLargeExportTableCase(valid),
            NewCase(valid with { Strings = LongUnicodeExportKeyStrings() },
                ExportRows((0x1300_0001, 5))),
            NewCase(valid, ExportRows((0x1300_0001, 5), (0x1300_0002, 8))),
            NewCase(valid, ExportRows((0x1300_0001, 5), (0x1600_0001, 8)), false),
            NewCase(valid, ExportRows((0x1600_0002, 5)), false),
            NewCase(valid, ExportRows((0x1400_0001, 5)), false),
            NewCase(valid, ExportRows((0x1300_0003, 5)), false),
            NewCase(valid with { Methods = SetWord(valid.Methods, 12, 0) },
                ExportRows((0x1600_0001, 5)), false),
            NewCase(valid with {
                TypeDefs = SetWord(valid.TypeDefs, 8, 5),
                Methods = SetWord(SetWord(valid.Methods, 0, 1), 12, 2),
            }, ExportRows((0x1600_0001, 5)), false),
            NewCase(property, ExportRows((0x1E00_0001, 5))),
            NewCase(property with {
                Properties = SetWord(property.Properties, 16, 0),
            }, ExportRows((0x1E00_0001, 5)), false),
            NewCase(valid, ExportRows((0x1300_0001, 1)), false),
            NewCase(valid, ExportRows((0x1300_0001, 99)), false),
            NewCase(valid, ExportRows((0x1300_0001, 5), (0x1600_0001, 5)), false),
            NewCase(valid, ExportRows((0x1300_0001, 5), (0x1300_0002, 8))),
            NewCase(valid with {
                Strings = SetStringByte(valid.Strings, 8, 8, 0x80),
            }, ExportRows((0x1300_0001, 8)), false, hasHostedOracle: false),
        };

        var root = RepositoryRoot();
        var components = new List<SyntaxTree>();
        foreach (var name in new[] {
            "VariableTableIndex.terrum",
            "StringRowLayout.terrum",
            "StringRowText.terrum",
            "ModuleTableValidation.terrum",
            "ModuleRefTableValidation.terrum",
            "TypeDefTableValidation.terrum",
            "MethodDefTableValidation.terrum",
            "PropertyDefTableValidation.terrum",
            "ImportTableValidation.terrum",
            "ExportTableValidation.terrum",
        })
        {
            var path = Path.Combine(root, "self-host", name);
            var tree = SyntaxTree.Parse(await File.ReadAllTextAsync(path).ConfigureAwait(true), path);
            Assert.That(tree.HasErrors, Is.False, string.Join(Environment.NewLine, tree.Diagnostics));
            components.Add(tree);
        }
        components.Add(SyntaxTree.Parse(ExportVerificationSource, "export-table-validation.terrum"));

        for (var caseIndex = 0; caseIndex < cases.Count; caseIndex++)
        {
            var testCase = cases[caseIndex];
            if (testCase.HasHostedOracle)
            {
                Assert.That(ValidateHosted(testCase), Is.EqualTo(testCase.ExpectedValid));
            }

            int outputCapacity = testCase.ExportCount + 1;
            var compilation = Compilation.Create(components,
            [
                new HostBuffer("Rows", PrimitiveType.Int32, [.. testCase.Tables.Strings], false),
                new HostBuffer("Modules", PrimitiveType.Int32, [.. testCase.Tables.Modules], false),
                new HostBuffer("ModuleRefs", PrimitiveType.Int32,
                    [.. testCase.Tables.ModuleRefs], false),
                new HostBuffer("TypeDefs", PrimitiveType.Int32, [.. testCase.Tables.TypeDefs], false),
                new HostBuffer("Methods", PrimitiveType.Int32, [.. testCase.Tables.Methods], false),
                new HostBuffer("Properties", PrimitiveType.Int32,
                    [.. testCase.Tables.Properties], false),
                new HostBuffer("Signatures", PrimitiveType.Int32,
                    [.. testCase.Tables.Signatures], false),
                new HostBuffer("Imports", PrimitiveType.Int32,
                    [.. testCase.Tables.Imports], false),
                new HostBuffer("Exports", PrimitiveType.Int32, [.. testCase.Exports], false),
                new HostBuffer("IdentitySlots", PrimitiveType.Int32, [0, 0, 0], true),
                new HostBuffer("FieldOwners", PrimitiveType.Int32, [0], true),
                new HostBuffer("BodyOwners", PrimitiveType.Int32,
                    [.. testCase.Tables.BodyOwners], true),
                new HostBuffer("GenericSignatureOwners", PrimitiveType.Int32, [0, 0], true),
                new HostBuffer("ImportExpectedDefinitions", PrimitiveType.Int32,
                    [.. new int[testCase.Tables.ImportCount + 1]], true),
                new HostBuffer("ImportExpectedTargets", PrimitiveType.Int32,
                    [.. new int[testCase.Tables.ImportCount + 1]], true),
                new HostBuffer("ImportExpectedPolicies", PrimitiveType.Int32,
                    [.. new int[testCase.Tables.ImportCount + 1]], true),
                new HostBuffer("ExportModules", PrimitiveType.Int32, [.. new int[outputCapacity]], true),
                new HostBuffer("ExportKeyStarts", PrimitiveType.Int32, [.. new int[outputCapacity]], true),
                new HostBuffer("ExportKeyLengths", PrimitiveType.Int32, [.. new int[outputCapacity]], true),
                new HostBuffer("ExportKeyEncodings", PrimitiveType.Int32,
                    [.. new int[outputCapacity]], true),
                new HostBuffer("ExportKeyContentLengths", PrimitiveType.Int32,
                    [.. new int[outputCapacity]], true),
                new HostBuffer("Expected", PrimitiveType.Int32,
                [
                    testCase.ExportCount,
                    2,
                    testCase.Tables.StringCount,
                    testCase.Tables.ModuleRefCount,
                    2,
                    2,
                    testCase.Tables.PropertyCount,
                    testCase.Tables.ImportCount,
                    testCase.ExpectedValid ? 1 : 0,
                ], false),
            ], policy);
            Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
            var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual0 = compilation.Program; Assert.That(__nunitActual0, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual0!; }))();
            int jitResult = NativeJit.Execute(program, (NativeRuntimePolicy)policy);
            Assert.That(jitResult == 0, Is.True, $"JIT differential case {caseIndex} expected {testCase.ExpectedValid}, returned {jitResult}.");
            int aotResult = await ExecuteAotAsync(program, (NativeRuntimePolicy)policy)
                .ConfigureAwait(true);
            Assert.That(aotResult == 0, Is.True, $"AOT differential case {caseIndex} expected {testCase.ExpectedValid}, returned {aotResult}.");
        }
    }

    private static ExportCase NewCase(TableCase tables,
        int[] exports, bool expectedValid = true, bool hasHostedOracle = true)
        => new(tables, exports, exports.Length / 8, expectedValid, hasHostedOracle);

    private static bool ValidateHosted(ExportCase testCase)
    {
        try
        {
            var tables = testCase.Tables;
            var strings = new TrRmStringTableReader(ToBytes(tables.Strings), tables.StringCount);
            var modules = new TrRmModuleTableReader(ToBytes(tables.Modules), 2, strings,
                tables.ModuleRefCount, 2, 2, 0);
            var types = new TrRmTypeDefTableReader(ToBytes(tables.TypeDefs), 2,
                strings, modules, 0, 0, 0, 0);
            var typeSpecs = new TrRmTypeSpecTableReader(ToBytes(tables.TypeSpecs), 1, 2, 0, 0, 0);
            var signatures = new TrRmMethodSigTableReader(ToBytes(tables.Signatures),
                1, typeSpecs, 0, 0);
            var methods = new TrRmMethodDefTableReader(ToBytes(tables.Methods), 2,
                strings, modules, types, signatures, 0, tables.BodyCount);
            var properties = tables.PropertyCount == 0
                ? default
                : new TrRmPropertyDefTableReader(ToBytes(tables.Properties),
                    tables.PropertyCount, strings, types, typeSpecs, methods, signatures);
            var references = new TrRmModuleRefTableReader(ToBytes(tables.ModuleRefs),
                tables.ModuleRefCount, strings, modules);
            var imports = new TrRmImportTableReader(ToBytes(tables.Imports),
                tables.ImportCount, strings, references, types, methods, properties);
            _ = new TrRmExportTableReader(ToBytes(testCase.Exports),
                testCase.ExportCount, strings, types, methods, properties, imports);
            return true;
        }
        catch (InvalidDataException)
        {
            return false;
        }
    }

    private static TableCase CreatePropertyExportCase(TableCase tables)
    {
        int[] methods = SetWord(SetWord(tables.Methods, 0, 1), 24, 0);
        var properties = new byte[20];
        WriteWord(properties, 0, 1);
        WriteWord(properties, 4, 7);
        WriteWord(properties, 8, 1);
        WriteWord(properties, 12, 1);
        WriteWord(properties, 16, 2);
        return tables with {
            Modules = [.. Modules(true).Select(static value => (int)value)],
            Methods = methods,
            Properties = [.. properties.Select(static value => (int)value)],
            Imports = [],
            BodyOwners = [0],
            BodyCount = 0,
            ImportCount = 0,
            PropertyCount = 1,
        };
    }

    private static int[] ExportRows(params (uint Definition, uint Key)[] rows)
    {
        var bytes = new byte[rows.Length * 8];
        for (var index = 0; index < rows.Length; index++)
        {
            WriteWord(bytes, index * 8, rows[index].Definition);
            WriteWord(bytes, index * 8 + 4, rows[index].Key);
        }
        return [.. bytes.Select(static value => (int)value)];
    }

    private static int[] LongUnicodeExportKeyStrings()
    {
        var longName = string.Concat(Enumerable.Repeat("\uD83D\uDE80", 512));
        var strings = TrRmStringTableWriter.EncodeTextRows([
            ("", 0), ("Producer", 0), ("Consumer", 0), ("Widget", 0),
            (longName, 1), ("Execute", 0), ("Value", 0), ("Key", 1),
        ]);
        return [.. strings.Select(static value => (int)value)];
    }

    private static ExportCase CreateLargeExportTableCase(TableCase tables)
    {
        const int exportCount = 512;
        var names = new (string Text, int Encoding)[8 + exportCount];
        names[0] = ("", 0);
        names[1] = ("Producer", 0);
        names[2] = ("Consumer", 0);
        names[3] = ("Widget", 0);
        names[4] = ("Key", 0);
        names[5] = ("Execute", 0);
        names[6] = ("Value", 0);
        names[7] = ("Key", 1);

        var exports = new (uint Definition, uint Key)[exportCount];
        for (var index = 0; index < exportCount; index++)
        {
            names[8 + index] = ($"Export-{index:D4}", 0);
            exports[index] = (0x1300_0001, (uint)(9 + index));
        }

        var strings = TrRmStringTableWriter.EncodeTextRows(names);
        return NewCase(tables with {
            Strings = [.. strings.Select(static value => (int)value)],
            StringCount = names.Length,
        }, ExportRows(exports));
    }


    private sealed record ExportCase(
        TableCase Tables,
        int[] Exports,
        int ExportCount,
        bool ExpectedValid,
        bool HasHostedOracle);
}
