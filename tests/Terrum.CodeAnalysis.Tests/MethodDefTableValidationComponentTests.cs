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

public sealed class MethodDefTableValidationComponentTests
{
    private const string VerificationSource = """
        namespace Terrum.CodeAnalysis;
        int32 Main() {
            bool valid = TryValidateMethodSigParamTables(
                Host.Expected[12], 0, Host.Expected[9], 1,
                Host.Expected[4], Host.Expected[3], 0)
                && TryValidateMethodDefTable(
                    Host.Expected[0], Host.Expected[1], Host.Expected[2],
                    Host.Expected[3], Host.Expected[4], Host.Expected[5],
                    Host.Expected[6], Host.Expected[7], Host.Expected[8],
                    Host.Expected[9], Host.Expected[10], Host.Expected[11],
                    Host.Expected[12], Host.Expected[13]);
            if (valid != (Host.Expected[14] != 0)) { return 1; }
            if (!valid) { return 0; }

            mut int32 index = 0;
            if (Host.Expected[15] != Host.BodyOwners.Length) { return 1; }
            while (index < Host.BodyOwners.Length) {
                if (Host.BodyOwners[index] != Host.Expected[16 + index]) { return 1; }
                index = index + 1;
            }

            int32 signatureLengthIndex = 16 + Host.BodyOwners.Length;
            if (Host.Expected[signatureLengthIndex] != Host.GenericSignatureOwners.Length) {
                return 1;
            }
            index = 0;
            while (index < Host.GenericSignatureOwners.Length) {
                if (Host.GenericSignatureOwners[index]
                    != Host.Expected[signatureLengthIndex + 1 + index]) { return 1; }
                index = index + 1;
            }
            return 0;
        }
        """;
    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
    public async Task MethodDefValidationMatchesHostedReader(BoundRuntimePolicy policy)
    {
        var valid = CreateValidCase();
        var twoModules = CreatePopulatedModuleRangesCase();
        var emptyFirstModule = CreateEmptyModuleRangeCase();
        var cases = new List<TableCase> {
            valid,
            CreateZeroMethodCase(),
            emptyFirstModule,
            Reject(valid with { Methods = ChangeMethodWord(valid.Methods, 1, 0, 2) }),
            Reject(valid with { Methods = ChangeMethodWord(valid.Methods, 1, 4, 0) }),
            Reject(valid with { Methods = ChangeMethodWord(valid.Methods, 1, 4, 2) }),
            Reject(valid with { Methods = ChangeMethodWord(valid.Methods, 1, 4, 6) }),
            Reject(valid with { Methods = ChangeMethodWord(valid.Methods, 1, 8, 0) }),
            Reject(valid with { Methods = ChangeMethodWord(valid.Methods, 1, 8, 4) }),
            Reject(valid with { Methods = ChangeMethodWord(valid.Methods, 1, 12, 3) }),
            Reject(valid with { Methods = ChangeMethodWord(valid.Methods, 1, 16, 0) }),
            Reject(valid with { Methods = ChangeMethodWord(valid.Methods, 1, 16, 3) }),
            Reject(valid with { Methods = ChangeMethodWord(valid.Methods, 1, 20, 0) }),
            Reject(valid with { Methods = ChangeMethodWord(valid.Methods, 1, 24, 3) }),
            Reject(valid with { Methods = ChangeMethodWord(valid.Methods, 1, 24, 1) }),
            Reject(valid with { BodyCount = 3 }),
            Reject(valid with { RecordCount = 1 }),
            Reject(valid with { Methods = valid.Methods[..^1] }),
            Reject(valid with { Signatures = ChangeSignatureWord(valid.Signatures, 1, 4, 1) }),
            Reject(valid with {
                Methods = ChangeMethodWord(
                    ChangeMethodWord(valid.Methods, 1, 0, 1), 1, 8, 2),
            }),
            twoModules,
            Reject(twoModules with { Methods = ChangeMethodWord(twoModules.Methods, 1, 0, 2) }),
            Reject(twoModules with { Methods = ChangeMethodWord(twoModules.Methods, 2, 0, 1) }),
            Reject(valid with {
                Strings = [.. EncodeStringSection(
                    NameRow("ModuleA"), NameRow(""), NameRow("Widget"),
                    NameRow("odd-name"), InvalidUtf8NameRow())
                    .Select(static value => (int)value)],
            }),
            AcceptWithUnreferencedMalformedName(),
            valid with { BodyOwnerLength = 2, ExpectedValid = false, HasHostedOracle = false },
            valid with {
                GenericSignatureOwnerLength = 3,
                ExpectedValid = false,
                HasHostedOracle = false,
            },
            valid with {
                Methods = [.. valid.Methods],
                ExpectedValid = false,
                HasHostedOracle = false,
            },
        };
        cases[^1].Methods[0] = 256;

        var root = RepositoryRoot();
        var components = new List<SyntaxTree>();
        foreach (var name in new[] {
            "VariableTableIndex.terrum",
            "StringRowLayout.terrum",
            "StringRowText.terrum",
            "ModuleTableValidation.terrum",
            "TypeDefTableValidation.terrum",
            "TypeSpecTableValidation.terrum",
            "MethodSigParamTableValidation.terrum",
            "MethodDefTableValidation.terrum",
        })
        {
            var path = Path.Combine(root, "self-host", name);
            var tree = SyntaxTree.Parse(await File.ReadAllTextAsync(path).ConfigureAwait(true), path);
            Assert.That(tree.HasErrors, Is.False, string.Join(Environment.NewLine, tree.Diagnostics));
            components.Add(tree);
        }
        components.Add(SyntaxTree.Parse(VerificationSource, "method-def-table-validation.terrum"));

        foreach (var testCase in cases)
        {
            if (testCase.HasHostedOracle)
            {
                Assert.That(ValidateHosted(testCase), Is.EqualTo(testCase.ExpectedValid));
            }

            var typeSpecs = TrRmTypeSpecTableWriter.Encode(
                [TrRmTypeSpecTableWriter.Builtin(8)], testCase.TypeDefCount,
                testCase.ModuleRefCount, testCase.GenericParamCount, 0);
            var compilation = Compilation.Create(components,
            [
                new HostBuffer("Rows", PrimitiveType.Int32, [.. testCase.Strings], false),
                new HostBuffer("Modules", PrimitiveType.Int32, [.. testCase.Modules], false),
                new HostBuffer("TypeDefs", PrimitiveType.Int32, [.. testCase.TypeDefs], false),
                new HostBuffer("Methods", PrimitiveType.Int32, [.. testCase.Methods], false),
                new HostBuffer("Signatures", PrimitiveType.Int32, [.. testCase.Signatures], false),
                new HostBuffer("Params", PrimitiveType.Int32, [], false),
                new HostBuffer("TypeSpecs", PrimitiveType.Int32,
                    [.. typeSpecs.Select(static value => (int)value)], false),
                new HostBuffer("IdentitySlots", PrimitiveType.Int32,
                    [.. Enumerable.Repeat(123, testCase.ModuleCount + 1)], true),
                new HostBuffer("FieldOwners", PrimitiveType.Int32,
                    [.. Enumerable.Repeat(123, testCase.FieldCount + 1)], true),
                new HostBuffer("BodyOwners", PrimitiveType.Int32,
                    [.. Enumerable.Repeat(123, testCase.BodyOwnerLength ?? testCase.BodyCount + 1)], true),
                new HostBuffer("GenericSignatureOwners", PrimitiveType.Int32,
                    [.. Enumerable.Repeat(123,
                        testCase.GenericSignatureOwnerLength ?? testCase.MethodSigCount + 1)], true),
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
                    testCase.MethodSigCount,
                    testCase.BodyCount,
                    testCase.ExpectedValid ? 1 : 0,
                    testCase.ExpectedBodyOwners.Length,
                    .. testCase.ExpectedBodyOwners,
                    testCase.ExpectedGenericSignatureOwners.Length,
                    .. testCase.ExpectedGenericSignatureOwners,
                ], false),
            ], policy);
            Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
            var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual0 = compilation.Program; Assert.That(__nunitActual0, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual0!; }))();
            Assert.That(NativeJit.Execute(program, (NativeRuntimePolicy)policy), Is.EqualTo(0));
            Assert.That(await ExecuteAotAsync(program, (NativeRuntimePolicy)policy).ConfigureAwait(true), Is.EqualTo(0));
        }
    }

    private static TableCase CreateValidCase()
    {
        var strings = TrRmStringTableWriter.EncodeTextRows([
            ("ModuleA", 0), ("", 0), ("Widget", 0), ("odd-name", 0), ("Run", 0),
        ]);
        var stringReader = new TrRmStringTableReader(strings, 5);
        var moduleBytes = TrRmModuleTableWriter.Encode([
            new TrRmModuleRow(1, 0, 0, 1, 1, 1, 1, 1),
        ], stringReader, 0, 1, 2, 0);
        var modules = new TrRmModuleTableReader(moduleBytes, 1, stringReader, 0, 1, 2, 0);
        var typeBytes = TrRmTypeDefTableWriter.Encode([
            new TrRmTypeDefRow(2, 3, 4, 0, 0, 0, 0, 0, 0, 0),
        ], stringReader, modules, 0, 1, 0, 2);
        var types = new TrRmTypeDefTableReader(typeBytes, 1, stringReader,
            modules, 0, 1, 0, 2);
        var typeSpecs = CreateTypeSpecs(1, 1);
        var signaturesBytes = TrRmMethodSigTableWriter.Encode([
            new TrRmMethodSigRow(1, 0, 0, 0, 0, 0),
            new TrRmMethodSigRow(1, 1, 0, 0, 1, 1),
            new TrRmMethodSigRow(1, 1, 0, 0, 1, 1),
        ], typeSpecs, 0, 1);
        var signatures = new TrRmMethodSigTableReader(signaturesBytes, 3,
            typeSpecs, 0, 1);
        var methodBytes = TrRmMethodDefTableWriter.Encode([
            new TrRmMethodDefRow(0, 4, 1, 2, 1, 1, 2),
            new TrRmMethodDefRow(1, 5, 2, 0, 2, 1, 1),
        ], stringReader, modules, types, signatures, 2, 2);
        return CreateCase(strings, moduleBytes, typeBytes, methodBytes, signaturesBytes,
            recordCount: 2, moduleCount: 1, stringCount: 5, typeDefCount: 1,
            methodDefCount: 2, genericParamCount: 1, contractCount: 2,
            methodSigCount: 3, bodyCount: 2,
            expectedBodyOwners: [0, 2, 1],
            expectedGenericSignatureOwners: [0, 0, 2, 0]);
    }

    private static TableCase CreateZeroMethodCase()
    {
        var testCase = CreateValidCase();
        var strings = testCase.Strings.Select(static value => checked((byte)value)).ToArray();
        var stringReader = new TrRmStringTableReader(strings, testCase.StringCount);
        var moduleBytes = TrRmModuleTableWriter.Encode([
            new TrRmModuleRow(1, 0, 0, 0, 1, 1, 1, 1),
        ], stringReader, 0, testCase.TypeDefCount, 0, 0);
        var modules = new TrRmModuleTableReader(moduleBytes, 1, stringReader, 0,
            testCase.TypeDefCount, 0, 0);
        var typeBytes = EncodeTypeDefs(stringReader, modules, 1, testCase.ContractCount);
        return testCase with {
            Modules = [.. moduleBytes.Select(static value => (int)value)],
            TypeDefs = [.. typeBytes.Select(static value => (int)value)],
            Methods = [],
            RecordCount = 0,
            MethodDefCount = 0,
            BodyCount = 0,
            ExpectedBodyOwners = [0],
            ExpectedGenericSignatureOwners = [0, 0, 0, 0],
        };
    }

    private static TableCase CreateEmptyModuleRangeCase()
    {
        var strings = TrRmStringTableWriter.EncodeTextRows([
            ("ModuleA", 0), ("ModuleB", 0), ("", 0), ("Widget", 0), ("Gadget", 0),
            ("Run", 0),
        ]);
        var stringReader = new TrRmStringTableReader(strings, 6);
        var moduleBytes = TrRmModuleTableWriter.Encode([
            new TrRmModuleRow(1, 0, 0, 0, 1, 1, 1, 1),
            new TrRmModuleRow(2, 0, 0, 0, 1, 2, 1, 1),
        ], stringReader, 0, 2, 2, 0);
        var modules = new TrRmModuleTableReader(moduleBytes, 2, stringReader, 0, 2, 2, 0);
        var typeBytes = TrRmTypeDefTableWriter.Encode([
            new TrRmTypeDefRow(3, 4, 4, 0, 0, 0, 0, 0, 0, 0),
            new TrRmTypeDefRow(3, 5, 4, 0, 0, 0, 0, 0, 0, 0),
        ], stringReader, modules, 0, 0, 0, 0);
        var types = new TrRmTypeDefTableReader(typeBytes, 2, stringReader,
            modules, 0, 0, 0, 0);
        var typeSpecs = CreateTypeSpecs(2, 0);
        var signaturesBytes = TrRmMethodSigTableWriter.Encode([
            new TrRmMethodSigRow(1, 1, 0, 0, 0, 0),
            new TrRmMethodSigRow(1, 0, 0, 0, 0, 0),
        ], typeSpecs, 0, 0);
        var signatures = new TrRmMethodSigTableReader(signaturesBytes, 2,
            typeSpecs, 0, 0);
        var methodBytes = TrRmMethodDefTableWriter.Encode([
            new TrRmMethodDefRow(2, 6, 1, 0, 0, 0, 1),
            new TrRmMethodDefRow(0, 6, 2, 0, 0, 0, 2),
        ], stringReader, modules, types, signatures, 0, 2);
        return CreateCase(strings, moduleBytes, typeBytes, methodBytes, signaturesBytes,
            recordCount: 2, moduleCount: 2, stringCount: 6, typeDefCount: 2,
            methodDefCount: 2, methodSigCount: 2, bodyCount: 2,
            expectedBodyOwners: [0, 1, 2],
            expectedGenericSignatureOwners: [0, 0, 0]);
    }

    private static TableCase CreatePopulatedModuleRangesCase()
    {
        var strings = TrRmStringTableWriter.EncodeTextRows([
            ("ModuleA", 0), ("ModuleB", 0), ("", 0), ("Widget", 0), ("Gadget", 0),
            ("Run", 0),
        ]);
        var stringReader = new TrRmStringTableReader(strings, 6);
        var moduleBytes = TrRmModuleTableWriter.Encode([
            new TrRmModuleRow(1, 0, 0, 0, 1, 1, 1, 1),
            new TrRmModuleRow(2, 0, 0, 0, 1, 2, 2, 1),
        ], stringReader, 0, 2, 2, 0);
        var modules = new TrRmModuleTableReader(moduleBytes, 2, stringReader, 0, 2, 2, 0);
        var typeBytes = TrRmTypeDefTableWriter.Encode([
            new TrRmTypeDefRow(3, 4, 4, 0, 0, 0, 0, 0, 0, 0),
            new TrRmTypeDefRow(3, 5, 4, 0, 0, 0, 0, 0, 0, 0),
        ], stringReader, modules, 0, 0, 0, 0);
        var types = new TrRmTypeDefTableReader(typeBytes, 2, stringReader,
            modules, 0, 0, 0, 0);
        var typeSpecs = CreateTypeSpecs(2, 0);
        var signaturesBytes = TrRmMethodSigTableWriter.Encode([
            new TrRmMethodSigRow(1, 1, 0, 0, 0, 0),
            new TrRmMethodSigRow(1, 1, 0, 0, 0, 0),
        ], typeSpecs, 0, 0);
        var signatures = new TrRmMethodSigTableReader(signaturesBytes, 2,
            typeSpecs, 0, 0);
        var methodBytes = TrRmMethodDefTableWriter.Encode([
            new TrRmMethodDefRow(1, 6, 1, 0, 0, 0, 1),
            new TrRmMethodDefRow(2, 6, 2, 0, 0, 0, 2),
        ], stringReader, modules, types, signatures, 0, 2);
        return CreateCase(strings, moduleBytes, typeBytes, methodBytes, signaturesBytes,
            recordCount: 2, moduleCount: 2, stringCount: 6, typeDefCount: 2,
            methodDefCount: 2, methodSigCount: 2, bodyCount: 2,
            expectedBodyOwners: [0, 1, 2],
            expectedGenericSignatureOwners: [0, 0, 0]);
    }

    private static TableCase AcceptWithUnreferencedMalformedName()
    {
        var testCase = CreateValidCase();
        var strings = EncodeStringSection(
            NameRow("ModuleA"), NameRow(""), NameRow("Widget"),
            NameRow("odd-name"), InvalidUtf8NameRow());
        var methods = ChangeMethodWord(testCase.Methods, 2, 4, 4);
        return testCase with {
            Strings = [.. strings.Select(static value => (int)value)],
            Methods = methods,
        };
    }

    private static TableCase CreateCase(byte[] strings, byte[] modules,
        byte[] typeDefs, byte[] methods, byte[] signatures,
        int recordCount, int moduleCount, int stringCount, int typeDefCount,
        int methodDefCount, int genericParamCount = 0, int contractCount = 0,
        int methodSigCount = 1, int bodyCount = 0,
        int[]? expectedBodyOwners = null, int[]? expectedGenericSignatureOwners = null)
        => new([.. strings.Select(static value => (int)value)],
            [.. modules.Select(static value => (int)value)],
            [.. typeDefs.Select(static value => (int)value)],
            [.. methods.Select(static value => (int)value)],
            [.. signatures.Select(static value => (int)value)],
            recordCount, moduleCount, stringCount, 0, typeDefCount, methodDefCount,
            0, -1, 0, genericParamCount, 0, contractCount, methodSigCount, bodyCount,
            true, true, null, null,
            expectedBodyOwners ?? [.. Enumerable.Repeat(0, bodyCount + 1)],
            expectedGenericSignatureOwners ?? [.. Enumerable.Repeat(0, methodSigCount + 1)]);

    private static TableCase Reject(TableCase testCase)
        => testCase with { ExpectedValid = false };

    private static TrRmTypeSpecTableReader CreateTypeSpecs(int typeDefCount, int genericParamCount)
    {
        var rows = new ReadOnlyMemory<byte>[] { TrRmTypeSpecTableWriter.Builtin(8) };
        var section = TrRmTypeSpecTableWriter.Encode(rows,
            typeDefCount, 0, genericParamCount, 0);
        return new TrRmTypeSpecTableReader(section, 1, typeDefCount, 0, genericParamCount, 0);
    }

    private static byte[] EncodeTypeDefs(TrRmStringTableReader strings,
        TrRmModuleTableReader modules, int typeDefCount, int contractCount)
    {
        if (typeDefCount == 0)
        {
            return [];
        }

        return TrRmTypeDefTableWriter.Encode([
            new TrRmTypeDefRow(2, 3, 4, 0, 0, 0, 0, 0, 0, 0),
        ], strings, modules, 0, 1, 0, contractCount);
    }

    private static bool ValidateHosted(TableCase testCase)
    {
        try
        {
            var strings = new TrRmStringTableReader(ToBytes(testCase.Strings), testCase.StringCount);
            var modules = new TrRmModuleTableReader(ToBytes(testCase.Modules), testCase.ModuleCount,
                strings, testCase.ModuleRefCount, testCase.TypeDefCount,
                testCase.MethodDefCount, testCase.SourceFileCount, testCase.FeatureCount);
            var types = new TrRmTypeDefTableReader(ToBytes(testCase.TypeDefs),
                testCase.TypeDefCount, strings, modules, testCase.FieldCount,
                testCase.GenericParamCount, testCase.LayoutCount, testCase.ContractCount);
            var typeSpecs = new TrRmTypeSpecTableReader(
                TrRmTypeSpecTableWriter.Encode(
                    [TrRmTypeSpecTableWriter.Builtin(8)],
                    testCase.TypeDefCount, testCase.ModuleRefCount,
                    testCase.GenericParamCount, 0),
                1, testCase.TypeDefCount, 0, testCase.GenericParamCount, 0);
            var signatures = new TrRmMethodSigTableReader(ToBytes(testCase.Signatures),
                testCase.MethodSigCount, typeSpecs, 0, testCase.GenericParamCount);
            _ = new TrRmMethodDefTableReader(ToBytes(testCase.Methods),
                testCase.RecordCount, strings, modules, types, signatures,
                testCase.ContractCount, testCase.BodyCount);
            return true;
        }
        catch (InvalidDataException)
        {
            return false;
        }
    }

    private static byte[] ToBytes(int[] values)
        => values.Select(static value => checked((byte)value)).ToArray();

    private static int[] ChangeMethodWord(int[] data, int row, int offset, uint value)
        => ChangeWord(data, (row - 1) * 28 + offset, value);

    private static int[] ChangeSignatureWord(int[] data, int row, int offset, uint value)
        => ChangeWord(data, (row - 1) * 24 + offset, value);

    private static int[] ChangeWord(int[] data, int offset, uint value)
    {
        var result = (int[])data.Clone();
        for (var index = 0; index < sizeof(uint); index++)
        {
            result[offset + index] = (int)((value >> (index * 8)) & 0xFF);
        }
        return result;
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

    private static byte[] NameRow(string text)
    {
        var content = System.Text.Encoding.ASCII.GetBytes(text);
        var length = Math.Max(8, (4 + content.Length + 1 + 3) & ~3);
        var row = new byte[length];
        row[1] = (byte)content.Length;
        content.CopyTo(row, 4);
        return row;
    }

    private static byte[] InvalidUtf8NameRow()
        => [1, 1, 0, 0, 0x80, 0, 0, 0];

    private static string RepositoryRoot()
        => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));

    private static async Task<int> ExecuteAotAsync(BoundProgram program, NativeRuntimePolicy policy)
    {
        var path = Path.Combine(Path.GetTempPath(), $"method-def-table-validation-{Guid.NewGuid():N}.exe");
        try
        {
            await File.WriteAllBytesAsync(path, NativeEmitter.Emit(program, policy)).ConfigureAwait(true);
            using var process = Process.Start(new ProcessStartInfo(path) {
                UseShellExecute = false,
                CreateNoWindow = true,
            }) ?? throw new InvalidOperationException("Could not start compiled MethodDef table validator.");
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

    private sealed record TableCase(int[] Strings, int[] Modules, int[] TypeDefs,
        int[] Methods, int[] Signatures,
        int RecordCount, int ModuleCount, int StringCount, int ModuleRefCount,
        int TypeDefCount, int MethodDefCount, int SourceFileCount, int FeatureCount,
        int FieldCount, int GenericParamCount, int LayoutCount, int ContractCount,
        int MethodSigCount, int BodyCount, bool ExpectedValid, bool HasHostedOracle,
        int? BodyOwnerLength, int? GenericSignatureOwnerLength,
        int[] ExpectedBodyOwners, int[] ExpectedGenericSignatureOwners);
}
