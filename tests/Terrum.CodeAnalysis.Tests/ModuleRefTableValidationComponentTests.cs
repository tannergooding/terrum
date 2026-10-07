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

public sealed class ModuleRefTableValidationComponentTests
{
    private const string VerificationSource = """
        namespace Terrum.CodeAnalysis;
        int32 Main() {
            bool valid = TryValidateModuleRefTable(
                Host.Expected[0], Host.Expected[1], Host.Expected[2],
                Host.Expected[3], Host.Expected[4], Host.Expected[5],
                Host.Expected[6], Host.Expected[7]);
            return valid == (Host.Expected[8] != 0) ? 0 : 1;
        }
        """;

    [Theory]
    [InlineData(BoundRuntimePolicy.FailFast)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions)]
    public async Task ModuleRefValidationMatchesHostedReader(BoundRuntimePolicy policy)
    {
        var strings = TrRmStringTableWriter.EncodeNames([
            ("A", 0),
            ("B", 2),
            ("X", 1),
        ]);
        var validModules = EncodeModules(2);
        var cases = new List<TableCase> {
            Valid(strings, validModules, EncodeModuleRefs((2, 0, 1), (3, 1, 0))),
            Valid(strings, SwapModuleIdentities(validModules), EncodeModuleRefs((1, 0, 1), (2, 1, 0))),
            Valid(strings, EncodeModules(0), [], recordCount: 0, moduleRefCount: 0),
            Rejected(strings, validModules, EncodeModuleRefs((2, 1, 1), (3, 1, 0))),
            Rejected(strings, validModules, EncodeModuleRefs((2, 0, 0), (3, 1, 0))),
            Rejected(strings, validModules, EncodeModuleRefs((0, 0, 1), (3, 1, 0))),
            Rejected(strings, validModules, EncodeModuleRefs((4, 0, 1), (3, 1, 0))),
            Rejected(strings, validModules, EncodeModuleRefs((2, 2, 1), (3, 1, 0))),
            Rejected(strings, validModules, EncodeModuleRefs((2, 0, 1), (3, 1, 2))),
            Rejected(strings, validModules, EncodeModuleRefs((2, 0, 1), (2, 0, 0))),
            Rejected(strings, validModules, EncodeModuleRefs((1, 0, 0), (3, 1, 0))),
        };

        var malformedProducer = EncodeStringSection(
            [0, 1, 0, 0, 65, 0, 0, 0],
            [2, 1, 0, 0, 66, 0, 0, 0],
            [0, 1, 0, 0, 128, 0, 0, 0]);
        cases.Add(Rejected(malformedProducer, validModules,
            EncodeModuleRefs((2, 0, 1), (3, 1, 0)), stringCount: 3));

        var invalidModuleRefByte = EncodeModuleRefs((2, 0, 1), (3, 1, 0))
            .Select(static value => (int)value).ToArray();
        invalidModuleRefByte[4] = 256;
        cases.Add(new TableCase(
            [.. strings.Select(static value => (int)value)],
            [.. validModules.Select(static value => (int)value)],
            invalidModuleRefByte, 2, 2, 3, 2, 0, 0, 0, -1, false, false, 3));
        cases.Add(Valid(strings, validModules, EncodeModuleRefs((2, 0, 1), (3, 1, 0))[..^1])
            with { ExpectedValid = false, HasHostedOracle = false });
        cases.Add(Valid(strings, validModules, EncodeModuleRefs((2, 0, 1), (3, 1, 0)),
            recordCount: 1) with { ExpectedValid = false });
        cases.Add(Valid(strings, validModules, EncodeModuleRefs((2, 0, 1), (3, 1, 0)))
            with { IdentitySlotCount = 2, ExpectedValid = false, HasHostedOracle = false });

        var root = RepositoryRoot();
        var components = new List<SyntaxTree>();
        foreach (var name in new[] {
            "VariableTableIndex.terrum",
            "StringRowLayout.terrum",
            "StringRowText.terrum",
            "ModuleTableValidation.terrum",
            "ModuleRefTableValidation.terrum",
        })
        {
            var path = Path.Combine(root, "self-host", name);
            var tree = SyntaxTree.Parse(await File.ReadAllTextAsync(path).ConfigureAwait(true), path);
            Assert.False(tree.HasErrors, string.Join(Environment.NewLine, tree.Diagnostics));
            components.Add(tree);
        }
        components.Add(SyntaxTree.Parse(VerificationSource, "module-ref-table-validation.terrum"));

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
                new HostBuffer("ModuleRefs", PrimitiveType.Int32, [.. testCase.ModuleRefs], false),
                new HostBuffer("IdentitySlots", PrimitiveType.Int32,
                    [.. Enumerable.Repeat(123, testCase.IdentitySlotCount)], true),
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

    private static TableCase Valid(byte[] strings, byte[] modules, byte[] moduleRefs,
        int recordCount = 2, int moduleCount = 2, int stringCount = 3,
        int moduleRefCount = 2, int typeDefCount = 0, int methodDefCount = 0,
        int sourceFileCount = 0, int featureCount = -1)
        => new([.. strings.Select(static value => (int)value)],
            [.. modules.Select(static value => (int)value)],
            [.. moduleRefs.Select(static value => (int)value)],
            recordCount, moduleCount, stringCount, moduleRefCount,
            typeDefCount, methodDefCount, sourceFileCount, featureCount,
            true, true, moduleCount + 1);

    private static TableCase Rejected(byte[] strings, byte[] modules, byte[] moduleRefs,
        int stringCount = 3)
        => Valid(strings, modules, moduleRefs, stringCount: stringCount) with {
            ExpectedValid = false,
        };

    private static bool ValidateHosted(TableCase testCase)
    {
        try
        {
            var stringReader = new TrRmStringTableReader(
                testCase.Strings.Select(static value => checked((byte)value)).ToArray(),
                testCase.StringCount);
            var moduleReader = new TrRmModuleTableReader(
                testCase.Modules.Select(static value => checked((byte)value)).ToArray(),
                testCase.ModuleCount, stringReader, testCase.ModuleRefCount,
                testCase.TypeDefCount, testCase.MethodDefCount, testCase.SourceFileCount,
                testCase.FeatureCount);
            _ = new TrRmModuleRefTableReader(
                testCase.ModuleRefs.Select(static value => checked((byte)value)).ToArray(),
                testCase.RecordCount, stringReader, moduleReader);
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

    private static byte[] EncodeModules(int moduleRefCount)
    {
        var modules = new byte[2 * 44];
        WriteWord(modules, 0, 1);
        WriteWord(modules, 4, 1);
        WriteWord(modules, 28, 1);
        WriteWord(modules, 32, 1);
        WriteWord(modules, 36, 1);
        WriteWord(modules, 40, 1);
        WriteWord(modules, 44, 2);
        WriteWord(modules, 44 + 4, 0);
        WriteWord(modules, 44 + 8, 1);
        WriteWord(modules, 44 + 28, moduleRefCount == 0 ? 1u : 2u);
        WriteWord(modules, 44 + 32, 1);
        WriteWord(modules, 44 + 36, 1);
        WriteWord(modules, 44 + 40, 1);
        return modules;
    }

    private static byte[] SwapModuleIdentities(byte[] modules)
    {
        var result = modules.ToArray();
        WriteWord(result, 0, 2);
        WriteWord(result, 44, 1);
        return result;
    }

    private static byte[] EncodeModuleRefs(params (uint Identity, uint Target, uint Policy)[] rows)
    {
        var section = new byte[rows.Length * 12];
        for (var row = 0; row < rows.Length; row++)
        {
            var offset = row * 12;
            WriteWord(section, offset, rows[row].Identity);
            WriteWord(section, offset + 4, rows[row].Target);
            WriteWord(section, offset + 8, rows[row].Policy);
        }
        return section;
    }

    private static void WriteWord(byte[] bytes, int offset, uint value)
        => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);

    private static async Task<int> ExecuteAotAsync(BoundProgram program, NativeRuntimePolicy policy)
    {
        var path = Path.Combine(Path.GetTempPath(), $"module-ref-table-validation-{Guid.NewGuid():N}.exe");
        try
        {
            await File.WriteAllBytesAsync(path, NativeEmitter.Emit(program, policy)).ConfigureAwait(true);
            using var process = Process.Start(new ProcessStartInfo(path) {
                UseShellExecute = false,
                CreateNoWindow = true,
            }) ?? throw new InvalidOperationException("Could not start compiled ModuleRef table validator.");
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

    private sealed record TableCase(int[] Strings, int[] Modules, int[] ModuleRefs,
        int RecordCount, int ModuleCount, int StringCount, int ModuleRefCount,
        int TypeDefCount, int MethodDefCount, int SourceFileCount, int FeatureCount,
        bool ExpectedValid, bool HasHostedOracle, int IdentitySlotCount);
}
