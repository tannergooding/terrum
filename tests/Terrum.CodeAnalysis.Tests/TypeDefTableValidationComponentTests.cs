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
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests;

public sealed class TypeDefTableValidationComponentTests
{
    private const string VerificationSource = """
        namespace Terrum.CodeAnalysis;
        int32 Main() {
            bool valid = TryValidateTypeDefTable(
                Host.Expected[0], Host.Expected[1], Host.Expected[2],
                Host.Expected[3], Host.Expected[4], Host.Expected[5],
                Host.Expected[6], Host.Expected[7], Host.Expected[8],
                Host.Expected[9], Host.Expected[10], Host.Expected[11]);
            if (valid != (Host.Expected[12] != 0)) { return 1; }
            if (!valid) { return 0; }
            if (Host.Expected[13] != Host.FieldOwners.Length) { return 1; }
            for (mut int32 field = 0; field < Host.FieldOwners.Length; field = field + 1)
            {
                if (Host.FieldOwners[field] != Host.Expected[14 + field]) { return 1; }
            }
            return 0;
        }
        """;
    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
    public async Task TypeDefValidationMatchesHostedReader(BoundRuntimePolicy policy)
    {
        var strings = TrRmStringTableWriter.EncodeTextRows([
            ("", 0), ("ModuleA", 0), ("ModuleB", 0), ("Widget", 0), ("Gadget", 0),
        ]);
        var modules = EncodeModules(2, 2);
        var types = EncodeTypeDefs(
            (1, 4, 4, 0, 0, 0, 0, 0, 0, 0),
            (1, 5, 4, 1, 1, 0, 0, 0, 0, 0));
        var cases = new List<TableCase> {
            Valid(strings, modules, types, fieldCount: 1, expectedFieldOwners: [0, 2]),
            Valid(strings, modules, EncodeTypeDefs(
                (1, 4, 4, 1, 2, 0, 0, 0, 0, 0),
                (1, 5, 4, 0, 0, 0, 0, 0, 0, 0)),
                fieldCount: 2, expectedFieldOwners: [0, 1, 1]),
            Valid(strings, modules, EncodeTypeDefs(
                (1, 4, 5, 0, 0, 0, 0, 1, 0, 0),
                (1, 5, 4, 0, 0, 0, 0, 0, 0, 0)),
                layoutCount: 1),
            Valid(strings, modules, EncodeTypeDefs(
                (1, 4, 4, 0, 0, 1, 1, 0, 1, 1),
                (1, 5, 4, 0, 0, 0, 0, 0, 0, 0)),
                genericParamCount: 1, contractCount: 1),
            Valid(strings, EncodeModules(1, 0), [], recordCount: 0,
                moduleCount: 1, typeDefCount: 0),
            Rejected(strings, modules, types[..^1], fieldCount: 1),
            Rejected(strings, modules, types, recordCount: 1, fieldCount: 1),
            Rejected(strings, modules, EncodeTypeDefs(
                (0, 4, 4, 0, 0, 0, 0, 0, 0, 0),
                (1, 5, 4, 1, 1, 0, 0, 0, 0, 0)), fieldCount: 1),
            Rejected(strings, modules, EncodeTypeDefs(
                (1, 0, 4, 0, 0, 0, 0, 0, 0, 0),
                (1, 5, 4, 1, 1, 0, 0, 0, 0, 0)), fieldCount: 1),
            Rejected(strings, modules, EncodeTypeDefs(
                (1, 4, 0, 0, 0, 0, 0, 0, 0, 0),
                (1, 5, 4, 1, 1, 0, 0, 0, 0, 0)), fieldCount: 1),
            Rejected(strings, modules, EncodeTypeDefs(
                (1, 4, 7, 0, 0, 0, 0, 0, 0, 0),
                (1, 5, 4, 1, 1, 0, 0, 0, 0, 0)), fieldCount: 1),
            Rejected(strings, modules, EncodeTypeDefs(
                (1, 4, 4, 1, 0, 0, 0, 0, 0, 0),
                (1, 5, 4, 1, 1, 0, 0, 0, 0, 0)), fieldCount: 1),
            Rejected(strings, modules, EncodeTypeDefs(
                (1, 4, 4, 2, 1, 0, 0, 0, 0, 0),
                (1, 5, 4, 1, 1, 0, 0, 0, 0, 0)), fieldCount: 1),
            Rejected(strings, modules, EncodeTypeDefs(
                (1, 4, 4, 0, 1, 0, 0, 0, 0, 0),
                (1, 5, 4, 1, 1, 0, 0, 0, 0, 0)), fieldCount: 1),
            Rejected(strings, modules, EncodeTypeDefs(
                (1, 4, 4, 1, 1, 0, 0, 0, 0, 0),
                (1, 5, 4, 1, 1, 0, 0, 0, 0, 0)), fieldCount: 1),
            Rejected(strings, modules, EncodeTypeDefs(
                (1, 4, 4, 0, 0, 0, 0, 0, 0, 0),
                (1, 5, 4, 0, 0, 0, 0, 0, 0, 0)), fieldCount: 1),
            Rejected(strings, modules, EncodeTypeDefs(
                (1, 4, 4, 0, 0, 1, 0, 0, 0, 0),
                (1, 5, 4, 1, 1, 0, 0, 0, 0, 0)), fieldCount: 1),
            Rejected(strings, modules, EncodeTypeDefs(
                (1, 4, 4, 0, 0, 0, 0, 0, 1, 0),
                (1, 5, 4, 1, 1, 0, 0, 0, 0, 0)), fieldCount: 1),
            Rejected(strings, modules, EncodeTypeDefs(
                (1, 4, 4, 0, 0, 0, 0, 1, 0, 0),
                (1, 5, 4, 1, 1, 0, 0, 0, 0, 0)), fieldCount: 1, layoutCount: 1),
        };

        var malformedNameStrings = EncodeStringSection(
            [0, 0, 0, 0],
            [0, 1, 0, 0, 77, 0, 0, 0],
            [0, 1, 0, 0, 78, 0, 0, 0],
            [0, 1, 0, 0, 128, 0, 0, 0],
            [0, 1, 0, 0, 71, 0, 0, 0]);
        cases.Add(Rejected(malformedNameStrings, EncodeModules(2, 2),
            EncodeTypeDefs((1, 4, 4, 0, 0, 0, 0, 0, 0, 0),
                (1, 5, 4, 0, 0, 0, 0, 0, 0, 0)), stringCount: 5, fieldCount: 0));
        cases.Add(Rejected(malformedNameStrings, EncodeModules(2, 2),
            EncodeTypeDefs((4, 5, 4, 0, 0, 0, 0, 0, 0, 0),
                (1, 5, 4, 0, 0, 0, 0, 0, 0, 0)), stringCount: 5));

        var invalidByteWords = types.Select(static value => (int)value).ToArray();
        invalidByteWords[0] = 256;
        cases.Add(new TableCase(
            [.. strings.Select(static value => (int)value)],
            [.. modules.Select(static value => (int)value)],
            invalidByteWords, 2, 2, 5, 0, 2, 0, 0, -1, 1, 0, 0, 0,
            false, false, 3, 2, []));
        cases.Add(Valid(strings, modules, types, fieldCount: 1) with {
            FieldOwnerCount = 1,
            ExpectedValid = false,
            HasHostedOracle = false,
        });
        cases.Add(Valid(strings, modules, types, fieldCount: 1) with {
            TypeDefs = [.. types[..^1].Select(static value => (int)value)],
            ExpectedValid = false,
        });

        var root = RepositoryRoot();
        var components = new List<SyntaxTree>();
        foreach (var name in new[] {
            "VariableTableIndex.terrum",
            "StringRowLayout.terrum",
            "StringRowText.terrum",
            "StringTableText.terrum",
            "ModuleTableValidation.terrum",
            "TypeDefTableValidation.terrum",
        })
        {
            var path = SelfHostPaths.ResolveFile(root, name);
            var tree = SyntaxTree.Parse(await File.ReadAllTextAsync(path).ConfigureAwait(true), path);
            Assert.That(tree.HasErrors, Is.False, string.Join(Environment.NewLine, tree.Diagnostics));
            components.Add(tree);
        }
        components.Add(SyntaxTree.Parse(VerificationSource, "type-def-table-validation.terrum"));

        for (var caseIndex = 0; caseIndex < cases.Count; caseIndex++)
        {
            var testCase = cases[caseIndex];
            if (testCase.HasHostedOracle)
            {
                Assert.That(testCase.ExpectedValid == ValidateHosted(testCase), Is.True, $"Hosted differential case {caseIndex} expected {testCase.ExpectedValid}.");
            }

            var compilation = Compilation.Create(components,
            [
                new HostBuffer("Rows", PrimitiveType.Int32, [.. testCase.Strings], false),
                new HostBuffer("Modules", PrimitiveType.Int32, [.. testCase.Modules], false),
                new HostBuffer("TypeDefs", PrimitiveType.Int32, [.. testCase.TypeDefs], false),
                new HostBuffer("IdentitySlots", PrimitiveType.Int32,
                    [.. Enumerable.Repeat(123, testCase.ModuleCount + 1)], true),
                new HostBuffer("FieldOwners", PrimitiveType.Int32,
                    [.. Enumerable.Repeat(123, testCase.FieldOwnerCount)], true),
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
                    testCase.ExpectedValid ? 1 : 0,
                    testCase.ExpectedFieldOwners.Length,
                    .. testCase.ExpectedFieldOwners,
                ], false),
            ], policy);
            Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
            var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual0 = compilation.Program; Assert.That(__nunitActual0, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual0!; }))();
            Assert.That(NativeJit.Execute(program, (NativeRuntimePolicy)policy) == 0, Is.True, $"JIT differential case {caseIndex} expected {testCase.ExpectedValid}.");
            Assert.That(await ExecuteAotAsync(program, (NativeRuntimePolicy)policy).ConfigureAwait(true) == 0, Is.True, $"AOT differential case {caseIndex} expected {testCase.ExpectedValid}.");
        }
    }
    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
    public async Task TypeDefFieldAndTypeSpecValidationCompose(BoundRuntimePolicy policy)
    {
        var strings = TrRmStringTableWriter.EncodeTextRows([
            ("", 0), ("Module", 0), ("Widget", 0), ("Value", 0), ("Gadget", 0),
        ]);
        var modules = EncodeModules(1, 1);
        var typeDefs = EncodeTypeDefs((1, 3, 4, 1, 1, 0, 0, 0, 0, 0));
        var fieldDefs = EncodeFieldDef(1, 4, 1, 0, uint.MaxValue);
        var typeSpecs = TrRmTypeSpecTableWriter.Encode(
            [TrRmTypeSpecTableWriter.Builtin(8)], 1, 0, 0, 0);
        var recursiveTypeSpecs = TrRmTypeSpecTableWriter.Encode(
            [TrRmTypeSpecTableWriter.Reference(0x02, 1)], 1, 0, 0, 0);
        var missingTypeSpecs = TrRmVariableTableWriter.EncodeBlobs(
            [TrRmTypeSpecTableWriter.Reference(0x02, 2)]);
        var invalidTypeSpecs = (byte[])typeSpecs.Clone();
        var typeSpecRowStart = sizeof(uint) * 2
            + (int)BinaryPrimitives.ReadUInt32LittleEndian(typeSpecs.AsSpan(0));
        invalidTypeSpecs[typeSpecRowStart + 2] = 0x1A;
        var cases = new[] {
            (TypeSpecs: typeSpecs, ExpectedValid: true),
            (TypeSpecs: recursiveTypeSpecs, ExpectedValid: true),
            (TypeSpecs: missingTypeSpecs, ExpectedValid: false),
            (TypeSpecs: invalidTypeSpecs, ExpectedValid: false),
        };

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
            "TypeSpecTableValidation.terrum",
            "FieldDefTableValidation.terrum",
        })
        {
            var path = SelfHostPaths.ResolveFile(root, name);
            var tree = SyntaxTree.Parse(await File.ReadAllTextAsync(path).ConfigureAwait(true), path);
            Assert.That(tree.HasErrors, Is.False, string.Join(Environment.NewLine, tree.Diagnostics));
            components.Add(tree);
        }
        components.Add(SyntaxTree.Parse("""
            namespace Terrum.CodeAnalysis;
            int32 Main() {
                bool typeSpecValid = TryValidateTypeSpecTable(
                    Host.Expected[12], Host.Expected[4], 0, Host.Expected[9], 0);
                bool typeDefValid = TryValidateTypeDefTable(
                    Host.Expected[0], Host.Expected[1], Host.Expected[2],
                    Host.Expected[3], Host.Expected[4], Host.Expected[5],
                    Host.Expected[6], Host.Expected[7], Host.Expected[8],
                    Host.Expected[9], Host.Expected[10], Host.Expected[11]);
                bool fieldDefValid = TryValidateFieldDefTable(
                    Host.Expected[0], Host.Expected[1], Host.Expected[2],
                    Host.Expected[3], Host.Expected[4], Host.Expected[5],
                    Host.Expected[6], Host.Expected[7], Host.Expected[8],
                    Host.Expected[9], Host.Expected[10], Host.Expected[11],
                    Host.Expected[12]);
                bool valid = typeSpecValid && typeDefValid && fieldDefValid;
                if (valid != (Host.Expected[13] != 0)) { return 1; }
                if (!valid) { return 0; }
                if (Host.FieldOwners.Length != 2
                    || Host.FieldOwners[0] != 0 || Host.FieldOwners[1] != 1) { return 1; }
                return 0;
            }
            """, "type-def-field-typespec-validation.terrum"));

        foreach (var testCase in cases)
        {
            Assert.That(ValidateComposedHosted(
                strings, modules, typeDefs, fieldDefs, testCase.TypeSpecs), Is.EqualTo(testCase.ExpectedValid));

            var compilation = Compilation.Create(components,
            [
                new HostBuffer("Rows", PrimitiveType.Int32,
                    [.. strings.Select(static value => (int)value)], false),
                new HostBuffer("Modules", PrimitiveType.Int32,
                    [.. modules.Select(static value => (int)value)], false),
                new HostBuffer("TypeDefs", PrimitiveType.Int32,
                    [.. typeDefs.Select(static value => (int)value)], false),
                new HostBuffer("Layouts", PrimitiveType.Int32, [], false),
                new HostBuffer("FieldDefs", PrimitiveType.Int32,
                    [.. fieldDefs.Select(static value => (int)value)], false),
                new HostBuffer("TypeSpecs", PrimitiveType.Int32,
                    [.. testCase.TypeSpecs.Select(static value => (int)value)], false),
                new HostBuffer("IdentitySlots", PrimitiveType.Int32,
                    [.. Enumerable.Repeat(123, 2)], true),
                new HostBuffer("FieldOwners", PrimitiveType.Int32,
                    [.. Enumerable.Repeat(123, 2)], true),
                new HostBuffer("LayoutOwners", PrimitiveType.Int32,
                    [.. Enumerable.Repeat(123, 1)], true),
                new HostBuffer("Expected", PrimitiveType.Int32,
                [
                    1, 1, 5, 0, 1, 0, 0, -1, 1, 0, 0, 0, 1,
                    testCase.ExpectedValid ? 1 : 0,
                ], false),
            ], policy);
            Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
            var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual1 = compilation.Program; Assert.That(__nunitActual1, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual1!; }))();
            Assert.That(NativeJit.Execute(program, (NativeRuntimePolicy)policy) == 0, Is.True, $"JIT composition case expected {testCase.ExpectedValid}.");
            Assert.That(await ExecuteAotAsync(program, (NativeRuntimePolicy)policy).ConfigureAwait(true) == 0, Is.True, $"AOT composition case expected {testCase.ExpectedValid}.");
        }
    }

    private static TableCase Valid(byte[] strings, byte[] modules, byte[] typeDefs,
        int recordCount = 2, int moduleCount = 2, int stringCount = 5,
        int moduleRefCount = 0, int typeDefCount = 2, int methodDefCount = 0,
        int sourceFileCount = 0, int featureCount = -1, int fieldCount = 0,
        int genericParamCount = 0, int layoutCount = 0, int contractCount = 0,
        int[]? expectedFieldOwners = null)
        => new([.. strings.Select(static value => (int)value)],
            [.. modules.Select(static value => (int)value)],
            [.. typeDefs.Select(static value => (int)value)],
            recordCount, moduleCount, stringCount, moduleRefCount, typeDefCount,
            methodDefCount, sourceFileCount, featureCount, fieldCount,
            genericParamCount, layoutCount, contractCount,
            true, true, moduleCount + 1, fieldCount + 1,
            expectedFieldOwners ?? [.. Enumerable.Repeat(0, fieldCount + 1)]);

    private static bool ValidateComposedHosted(byte[] stringBytes, byte[] moduleBytes,
        byte[] typeDefBytes, byte[] fieldDefBytes, byte[] typeSpecBytes)
    {
        try
        {
            var strings = new TrRmStringTableReader(stringBytes, 5);
            var modules = new TrRmModuleTableReader(moduleBytes, 1, strings,
                0, 1, 0, 0, -1);
            var typeSpecs = new TrRmTypeSpecTableReader(typeSpecBytes, 1, 1, 0, 0, 0);
            var typeDefs = new TrRmTypeDefTableReader(typeDefBytes, 1, strings, modules,
                1, 0, 0, 0);
            _ = new TrRmFieldDefTableReader(fieldDefBytes, 1, strings, typeDefs, typeSpecs);
            return true;
        }
        catch (InvalidDataException)
        {
            return false;
        }
    }

    private static TableCase Rejected(byte[] strings, byte[] modules, byte[] typeDefs,
        int stringCount = 5, bool hasHostedOracle = true, int recordCount = 2,
        int fieldCount = 0, int layoutCount = 0)
        => Valid(strings, modules, typeDefs, recordCount: recordCount,
            stringCount: stringCount, fieldCount: fieldCount, layoutCount: layoutCount) with {
            ExpectedValid = false,
            HasHostedOracle = hasHostedOracle,
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
            _ = new TrRmTypeDefTableReader(
                testCase.TypeDefs.Select(static value => checked((byte)value)).ToArray(),
                testCase.RecordCount, strings, modules, testCase.FieldCount,
                testCase.GenericParamCount, testCase.LayoutCount, testCase.ContractCount);
            return true;
        }
        catch (InvalidDataException)
        {
            return false;
        }
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

    private static byte[] EncodeFieldDef(uint owner, uint name, uint typeSpec,
        uint flags, uint byteOffset)
    {
        var field = new byte[20];
        WriteWord(field, 0, owner);
        WriteWord(field, 4, name);
        WriteWord(field, 8, typeSpec);
        WriteWord(field, 12, flags);
        WriteWord(field, 16, byteOffset);
        return field;
    }

    private static byte[] EncodeModules(int moduleCount, int typeDefCount)
    {
        var modules = new byte[moduleCount * 44];
        for (var row = 0; row < moduleCount; row++)
        {
            var offset = row * 44;
            WriteWord(modules, offset, (uint)(row + 2));
            WriteWord(modules, offset + 28, 1);
            WriteWord(modules, offset + 32, (uint)Math.Min(row + 1, typeDefCount + 1));
            WriteWord(modules, offset + 36, 1);
            WriteWord(modules, offset + 40, 1);
        }
        return modules;
    }

    private static byte[] EncodeTypeDefs(params (uint Namespace, uint Name, uint Flags,
        uint FieldFirst, uint FieldCount, uint GenericFirst, uint GenericCount,
        uint Layout, uint ContractFirst, uint ContractCount)[] rows)
    {
        var section = new byte[rows.Length * 40];
        for (var row = 0; row < rows.Length; row++)
        {
            var offset = row * 40;
            WriteWord(section, offset, rows[row].Namespace);
            WriteWord(section, offset + 4, rows[row].Name);
            WriteWord(section, offset + 8, rows[row].Flags);
            WriteWord(section, offset + 12, rows[row].FieldFirst);
            WriteWord(section, offset + 16, rows[row].FieldCount);
            WriteWord(section, offset + 20, rows[row].GenericFirst);
            WriteWord(section, offset + 24, rows[row].GenericCount);
            WriteWord(section, offset + 28, rows[row].Layout);
            WriteWord(section, offset + 32, rows[row].ContractFirst);
            WriteWord(section, offset + 36, rows[row].ContractCount);
        }
        return section;
    }

    private static void WriteWord(byte[] bytes, int offset, uint value)
        => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);

    private static async Task<int> ExecuteAotAsync(BoundProgram program, NativeRuntimePolicy policy)
    {
        var path = Path.Combine(TestArtifactPaths.DirectoryPath, $"type-def-table-validation-{Guid.NewGuid():N}.exe");
        try
        {
            await File.WriteAllBytesAsync(path, NativeEmitter.Emit(program, policy)).ConfigureAwait(true);
            using var process = Process.Start(new ProcessStartInfo(path) {
                UseShellExecute = false,
                CreateNoWindow = true,
            }) ?? throw new InvalidOperationException("Could not start compiled TypeDef table validator.");
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(true);
            return process.ExitCode;
        }
        finally
        {
            if (File.Exists(path))
            {
                await TestFileCleanup.DeleteWithRetryAsync(path).ConfigureAwait(true);
            }
        }
    }

    private static string RepositoryRoot() =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));

    private sealed record TableCase(int[] Strings, int[] Modules, int[] TypeDefs,
        int RecordCount, int ModuleCount, int StringCount, int ModuleRefCount,
        int TypeDefCount, int MethodDefCount, int SourceFileCount, int FeatureCount,
        int FieldCount, int GenericParamCount, int LayoutCount, int ContractCount,
        bool ExpectedValid, bool HasHostedOracle, int IdentitySlotCount, int FieldOwnerCount,
        int[] ExpectedFieldOwners);
}
