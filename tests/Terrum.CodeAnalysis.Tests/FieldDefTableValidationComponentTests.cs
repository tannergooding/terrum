// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Terrum.CodeAnalysis.Text;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed class FieldDefTableValidationComponentTests
{
    private const string VerificationSource = """
        namespace Terrum.CodeAnalysis;
        int32 Main() {
            bool valid = TryValidateFieldDefTable(
                Host.Expected[0], Host.Expected[1], Host.Expected[2],
                Host.Expected[3], Host.Expected[4], Host.Expected[5],
                Host.Expected[6], Host.Expected[7], Host.Expected[8],
                Host.Expected[9], Host.Expected[10], Host.Expected[11],
                Host.Expected[12]);
            return valid == (Host.Expected[13] != 0) ? 0 : 1;
        }
        """;

    [Theory]
    [InlineData(BoundRuntimePolicy.FailFast)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions)]
    public async Task FieldDefValidationMatchesHostedReader(BoundRuntimePolicy policy)
    {
        var strings = TrRmStringTableWriter.EncodeTextRows([
            ("", 0), ("Module", 0), ("Widget", 0), ("Value", 0), ("Gadget", 0),
        ]);
        var typeSpecs = TrRmTypeSpecTableWriter.Encode(
            [TrRmTypeSpecTableWriter.Builtin(8)], 1, 0, 0, 0);
        var multiTypeSpecs = TrRmTypeSpecTableWriter.Encode(
            [TrRmTypeSpecTableWriter.Builtin(8)], 2, 0, 0, 0);
        var modules = EncodeModules();
        var noLayoutType = EncodeTypeDefs(0);
        var laidOutType = EncodeTypeDefs(1);
        var layouts = EncodeLayouts(4);
        var fields = EncodeFields(1, 4, 1, 0, uint.MaxValue);
        var cases = new List<TableCase> {
            Valid(strings, modules, noLayoutType, [], fields, typeSpecs),
            Valid(strings, modules, laidOutType, layouts,
                EncodeFields(1, 4, 1, 0, 0), typeSpecs, layoutCount: 1),
            Valid(strings, modules, laidOutType, layouts,
                EncodeFields(1, 4, 1, 0, 3), typeSpecs, layoutCount: 1),
            Valid(strings, modules, EncodeTypeDefs(
                    (3, 0x14, 0, 0, 2), (5, 0x14, 1, 1, 1)),
                EncodeLayouts((2, 1, 4, 4, 0, 0), (1, 0, 0, 4, 0, 0)),
                EncodeFields(2, 4, 1, 0, 3), multiTypeSpecs,
                typeDefCount: 2, layoutCount: 2),
            Rejected(strings, modules, laidOutType, layouts,
                EncodeFields(1, 4, 1, 0, 4), typeSpecs, layoutCount: 1),
            Rejected(strings, modules, noLayoutType, [],
                EncodeFields(1, 4, 1, 0, 0), typeSpecs),
            Rejected(strings, modules, noLayoutType, [],
                EncodeFields(2, 4, 1, 0, uint.MaxValue), typeSpecs),
            Rejected(strings, modules, EncodeTypeDefs(
                    (3, 4, 0, 0, 0), (5, 4, 1, 1, 0)),
                [], EncodeFields(1, 4, 1, 0, uint.MaxValue),
                multiTypeSpecs, typeDefCount: 2),
            Rejected(strings, modules, EncodeTypeDefs((3, 4, 0, 0, 0)), [],
                EncodeFields(1, 4, 1, 0, uint.MaxValue), typeSpecs, fieldCount: 0),
            Rejected(strings, modules, noLayoutType, [],
                EncodeFields(1, 0, 1, 0, uint.MaxValue), typeSpecs),
            Rejected(strings, modules, noLayoutType, [],
                EncodeFields(1, 6, 1, 0, uint.MaxValue), typeSpecs),
            Rejected(strings, modules, noLayoutType, [],
                EncodeFields(1, 4, 0, 0, uint.MaxValue), typeSpecs),
            Rejected(strings, modules, noLayoutType, [],
                EncodeFields(1, 4, 2, 0, uint.MaxValue), typeSpecs),
            Rejected(strings, modules, noLayoutType, [],
                EncodeFields(1, 4, 1, 3, uint.MaxValue), typeSpecs),
            Rejected(strings, modules, noLayoutType, [],
                EncodeFields(1, 4, 1, 8, uint.MaxValue), typeSpecs),
            Rejected(strings, modules, noLayoutType, [],
                EncodeFields(1, 4, 1, 0, uint.MaxValue)[..^1], typeSpecs),
            Rejected(strings, modules, noLayoutType, [],
                EncodeFields(1, 4, 1, 0, uint.MaxValue), typeSpecs, typeSpecCount: 0),
            Rejected(strings, modules, laidOutType, EncodeLayouts(0),
                EncodeFields(1, 4, 1, 0, 0), typeSpecs, layoutCount: 1),
        };

        var malformedNameStrings = EncodeStringSection(
            [0, 0, 0, 0],
            [0, 1, 0, 0, 77, 0, 0, 0],
            [0, 1, 0, 0, 84, 0, 0, 0],
            [0, 1, 0, 0, 70, 0, 0, 0],
            [0, 1, 0, 0, 128, 0, 0, 0]);
        cases.Add(Rejected(malformedNameStrings, EncodeModules(), noLayoutType, [],
            EncodeFields(1, 5, 1, 0, uint.MaxValue), typeSpecs, stringCount: 5));

        var invalidByteWords = fields.Select(static value => (int)value).ToArray();
        invalidByteWords[0] = 256;
        cases.Add(new TableCase(
            [.. strings.Select(static value => (int)value)],
            [.. modules.Select(static value => (int)value)],
            [.. noLayoutType.Select(static value => (int)value)],
            [], invalidByteWords, [.. typeSpecs.Select(static value => (int)value)],
            1, 1, 5, 0, 1, 0, 0, -1, 1, 0, 0, 0, 1, false, false));

        var root = RepositoryRoot();
        var components = new List<SyntaxTree>();
        foreach (var name in new[] {
            "VariableTableIndex.terrum",
            "StringRowLayout.terrum",
            "StringRowText.terrum",
            "StringTableText.terrum",
            "ModuleTableValidation.terrum",
            "TypeDefTableValidation.terrum",
            "LayoutTableValidation.terrum",
            "FieldDefTableValidation.terrum",
        })
        {
            var path = Path.Combine(root, "self-host", name);
            var tree = SyntaxTree.Parse(await File.ReadAllTextAsync(path).ConfigureAwait(true), path);
            Assert.False(tree.HasErrors, string.Join(Environment.NewLine, tree.Diagnostics));
            components.Add(tree);
        }
        components.Add(SyntaxTree.Parse(VerificationSource, "field-def-table-validation.terrum"));

        for (var caseIndex = 0; caseIndex < cases.Count; caseIndex++)
        {
            var testCase = cases[caseIndex];
            if (testCase.HasHostedOracle)
            {
                Assert.True(testCase.ExpectedValid == ValidateHosted(testCase),
                    $"Hosted differential case {caseIndex} expected {testCase.ExpectedValid}.");
            }

            var compilation = Compilation.Create(components,
            [
                new HostBuffer("Rows", PrimitiveType.Int32, [.. testCase.Strings], false),
                new HostBuffer("Modules", PrimitiveType.Int32, [.. testCase.Modules], false),
                new HostBuffer("TypeDefs", PrimitiveType.Int32, [.. testCase.TypeDefs], false),
                new HostBuffer("Layouts", PrimitiveType.Int32, [.. testCase.Layouts], false),
                new HostBuffer("FieldDefs", PrimitiveType.Int32, [.. testCase.FieldDefs], false),
                new HostBuffer("IdentitySlots", PrimitiveType.Int32,
                    [.. Enumerable.Repeat(123, testCase.ModuleCount + 1)], true),
                new HostBuffer("FieldOwners", PrimitiveType.Int32,
                    [.. Enumerable.Repeat(123, testCase.FieldCount + 1)], true),
                new HostBuffer("LayoutOwners", PrimitiveType.Int32,
                    [.. Enumerable.Repeat(123, testCase.LayoutCount + 1)], true),
                new HostBuffer("Expected", PrimitiveType.Int32,
                [
                    testCase.RecordCount,
                    testCase.ModuleCount,
                    testCase.StringCount,
                    testCase.ModuleRefCount,
                    testCase.TypeDefCount,
                    testCase.MethodDefCount,
                    testCase.SourceFileCount,
                    testCase.FeatureCount,
                    testCase.FieldCount,
                    testCase.GenericParamCount,
                    testCase.LayoutCount,
                    testCase.ContractCount,
                    testCase.TypeSpecCount,
                    testCase.ExpectedValid ? 1 : 0,
                ], false),
            ], policy);
            Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
            var program = Assert.IsType<BoundProgram>(compilation.Program);
            Assert.True(NativeJit.Execute(program, (NativeRuntimePolicy)policy) == 0,
                $"JIT differential case {caseIndex} expected {testCase.ExpectedValid}.");
            Assert.True(await ExecuteAotAsync(program, (NativeRuntimePolicy)policy).ConfigureAwait(true) == 0,
                $"AOT differential case {caseIndex} expected {testCase.ExpectedValid}.");
        }
    }

    private static TableCase Valid(byte[] strings, byte[] modules, byte[] typeDefs,
        byte[] layouts, byte[] fields, byte[] typeSpecs,
        int recordCount = 1, int moduleCount = 1, int stringCount = 5,
        int moduleRefCount = 0, int typeDefCount = 1, int methodDefCount = 0,
        int sourceFileCount = 0, int featureCount = -1, int fieldCount = 1,
        int genericParamCount = 0, int layoutCount = 0, int contractCount = 0,
        int typeSpecCount = 1)
        => new([.. strings.Select(static value => (int)value)],
            [.. modules.Select(static value => (int)value)],
            [.. typeDefs.Select(static value => (int)value)],
            [.. layouts.Select(static value => (int)value)],
            [.. fields.Select(static value => (int)value)],
            [.. typeSpecs.Select(static value => (int)value)],
            recordCount, moduleCount, stringCount, moduleRefCount, typeDefCount,
            methodDefCount, sourceFileCount, featureCount, fieldCount,
            genericParamCount, layoutCount, contractCount, typeSpecCount, true, true);

    private static TableCase Rejected(byte[] strings, byte[] modules, byte[] typeDefs,
        byte[] layouts, byte[] fields, byte[] typeSpecs,
        int stringCount = 5, bool hasHostedOracle = true, int recordCount = 1,
        int moduleCount = 1, int typeDefCount = 1, int fieldCount = 1,
        int layoutCount = 0, int typeSpecCount = 1)
        => Valid(strings, modules, typeDefs, layouts, fields, typeSpecs,
            recordCount: recordCount, moduleCount: moduleCount,
            stringCount: stringCount, typeDefCount: typeDefCount,
            fieldCount: fieldCount, layoutCount: layoutCount,
            typeSpecCount: typeSpecCount) with {
            ExpectedValid = false, HasHostedOracle = hasHostedOracle,
        };

    private static bool ValidateHosted(TableCase testCase)
    {
        try
        {
            var strings = new TrRmStringTableReader(
                testCase.Strings.Select(static value => checked((byte)value)).ToArray(),
                testCase.StringCount);
            var modules = new TrRmModuleTableReader(
                testCase.Modules.Select(static value => checked((byte)value)).ToArray(),
                testCase.ModuleCount, strings, testCase.ModuleRefCount,
                testCase.TypeDefCount, testCase.MethodDefCount, testCase.SourceFileCount,
                testCase.FeatureCount);
            var typeSpecs = new TrRmTypeSpecTableReader(
                testCase.TypeSpecs.Select(static value => checked((byte)value)).ToArray(),
                testCase.TypeSpecCount, testCase.TypeDefCount, 0,
                testCase.GenericParamCount, 0);
            var types = new TrRmTypeDefTableReader(
                testCase.TypeDefs.Select(static value => checked((byte)value)).ToArray(),
                testCase.TypeDefCount, strings, modules, testCase.FieldCount,
                testCase.GenericParamCount, testCase.LayoutCount, testCase.ContractCount);
            var layouts = testCase.LayoutCount == 0 ? default
                : new TrRmLayoutTableReader(
                    testCase.Layouts.Select(static value => checked((byte)value)).ToArray(),
                    testCase.LayoutCount, types);
            _ = new TrRmFieldDefTableReader(
                testCase.FieldDefs.Select(static value => checked((byte)value)).ToArray(),
                testCase.RecordCount, strings, types, typeSpecs, layouts);
            return true;
        }
        catch (InvalidDataException)
        {
            return false;
        }
    }

    private static byte[] EncodeModules()
    {
        var module = new byte[44];
        WriteWord(module, 0, 2);
        WriteWord(module, 28, 1);
        WriteWord(module, 32, 1);
        WriteWord(module, 36, 1);
        WriteWord(module, 40, 1);
        return module;
    }

    private static byte[] EncodeTypeDefs(uint layoutRow)
        => EncodeTypeDefs((3, layoutRow == 0 ? 4u : 0x14u, 1u, 1u, layoutRow));

    private static byte[] EncodeTypeDefs(params (uint Name, uint Flags, uint FieldFirst,
        uint FieldCount, uint Layout)[] rows)
    {
        var typeDefs = new byte[rows.Length * 40];
        for (var row = 0; row < rows.Length; row++)
        {
            var offset = row * 40;
            WriteWord(typeDefs, offset, 1);
            WriteWord(typeDefs, offset + 4, rows[row].Name);
            WriteWord(typeDefs, offset + 8, rows[row].Flags);
            WriteWord(typeDefs, offset + 12, rows[row].FieldFirst);
            WriteWord(typeDefs, offset + 16, rows[row].FieldCount);
            WriteWord(typeDefs, offset + 28, rows[row].Layout);
        }
        return typeDefs;
    }

    private static byte[] EncodeLayouts(uint size)
        => EncodeLayouts((1, 1, size, 4u, 0u, 0u));

    private static byte[] EncodeLayouts(params (uint Owner, uint Target, uint Size,
        uint Alignment, uint Pack, uint Flags)[] rows)
    {
        var layouts = new byte[rows.Length * 24];
        for (var row = 0; row < rows.Length; row++)
        {
            var offset = row * 24;
            WriteWord(layouts, offset, rows[row].Owner);
            WriteWord(layouts, offset + 4, rows[row].Target);
            WriteWord(layouts, offset + 8, rows[row].Size);
            WriteWord(layouts, offset + 12, rows[row].Alignment);
            WriteWord(layouts, offset + 16, rows[row].Pack);
            WriteWord(layouts, offset + 20, rows[row].Flags);
        }
        return layouts;
    }

    private static byte[] EncodeFields(uint owner, uint name, uint typeSpec, uint flags, uint offset)
    {
        var field = new byte[20];
        WriteWord(field, 0, owner);
        WriteWord(field, 4, name);
        WriteWord(field, 8, typeSpec);
        WriteWord(field, 12, flags);
        WriteWord(field, 16, offset);
        return field;
    }

    private static byte[] EncodeStringSection(params byte[][] rows)
    {
        var indexLength = (rows.Length + 1) * sizeof(uint);
        var section = new byte[indexLength + rows.Sum(static row => row.Length)];
        var offset = 0;
        for (var index = 0; index < rows.Length; index++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(section.AsSpan(index * sizeof(uint)), (uint)offset);
            rows[index].CopyTo(section, indexLength + offset);
            offset += rows[index].Length;
        }
        BinaryPrimitives.WriteUInt32LittleEndian(section.AsSpan(rows.Length * sizeof(uint)), (uint)offset);
        return section;
    }

    private static void WriteWord(byte[] bytes, int offset, uint value)
        => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);

    private static async Task<int> ExecuteAotAsync(BoundProgram program, NativeRuntimePolicy policy)
    {
        var path = Path.Combine(Path.GetTempPath(), $"field-def-table-validation-{Guid.NewGuid():N}.exe");
        try
        {
            await File.WriteAllBytesAsync(path, NativeEmitter.Emit(program, policy)).ConfigureAwait(true);
            using var process = Process.Start(new ProcessStartInfo(path) {
                UseShellExecute = false,
                CreateNoWindow = true,
            }) ?? throw new InvalidOperationException("Could not start compiled FieldDef table validator.");
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(true);
            return process.ExitCode;
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    private static string RepositoryRoot() =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));

    private sealed record TableCase(int[] Strings, int[] Modules, int[] TypeDefs, int[] Layouts,
        int[] FieldDefs, int[] TypeSpecs, int RecordCount, int ModuleCount, int StringCount,
        int ModuleRefCount, int TypeDefCount, int MethodDefCount, int SourceFileCount,
        int FeatureCount, int FieldCount, int GenericParamCount, int LayoutCount, int ContractCount,
        int TypeSpecCount, bool ExpectedValid, bool HasHostedOracle);
}
