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

public sealed class ModuleTableValidationComponentTests
{
    private const string VerificationSource = """
        namespace Terrum.CodeAnalysis;
        int32 Main() {
            bool valid = TryValidateModuleTable(
                Host.Expected[0], Host.Expected[1], Host.Expected[2],
                Host.Expected[3], Host.Expected[4], Host.Expected[5], Host.Expected[6]);
            return valid == (Host.Expected[7] != 0) ? 0 : 1;
        }
        """;

    [Theory]
    [InlineData(BoundRuntimePolicy.FailFast)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions)]
    public async Task ModuleTableValidationMatchesHostedReader(BoundRuntimePolicy policy)
    {
        var names = TrRmStringTableWriter.EncodeNames([
            ("A", 0),
            ("AA", 1),
            ("A\U00010400", 2),
            ("B", 3),
            ("\U00010000", 1),
            ("\u00E9", 2),
            ("\u00E9A", 3),
            ("Z", 0),
        ]);
        var cases = new List<TableCase> {
            Valid(names, [4, 1, 8, 2, 5, 7, 3, 6], 8),
            Valid(names, [1, 1], 8, expectedValid: false),
            Valid(TrRmStringTableWriter.EncodeNames([("\u00E9", 1), ("\u00E9", 2)]),
                [1, 2], 2, expectedValid: false),
            Valid(TrRmStringTableWriter.EncodeNames([("\U00010400", 1), ("\U00010400", 2)]),
                [1, 2], 2, expectedValid: false),
        };

        AddRejectedWithModuleWord(cases, Valid(names, [1, 2], 8), 4, 2);
        AddRejectedWithModuleWord(cases, Valid(names, [1, 2], 8), 8, 2);
        AddRejectedWithModuleWord(cases, Valid(names, [1, 2], 8), 24, 1);
        AddRejectedWithModuleWord(cases,
            Valid(names, [1, 2], 8, moduleRefCount: 1), 44 + 28, 0);
        AddRejectedWithModuleWord(cases,
            Valid(names, [1, 2], 8, moduleRefCount: 1), 44 + 28, 3);
        AddRejectedWithModuleWord(cases,
            Valid(names, [1, 2], 8, typeDefCount: 1), 44 + 32, 3);
        AddRejectedWithModuleWord(cases,
            Valid(names, [1, 2], 8, methodDefCount: 2), 44 + 36, 0);
        AddRejectedWithModuleWord(cases,
            Valid(names, [1, 2], 8, sourceFileCount: 1), 44 + 40, 3);

        AddRejected(cases, names, [0]);
        AddRejected(cases, names, [9]);
        AddRejected(cases, names, [1, 2], methodDefCount: -1);
        AddRejected(cases, names, [1, 2], moduleRefCount: 0x1000000);
        AddRejected(cases, names, [1, 2], featureCount: 0x1000000);
        cases.Add(Valid(names, [1, 2], 8, featureCount: 2, firstFeature: 1, firstFeatureCount: 1));
        AddRejected(cases, names, [1, 2], featureCount: 1, firstFeature: 2, firstFeatureCount: 1);
        AddRejected(cases, names, [1, 2], firstFeature: 1, firstFeatureCount: 0);
        AddRejected(cases, names, [1, 2], firstFeature: 0, firstFeatureCount: 1);
        AddRejected(cases, names, [1, 2], firstFeature: 0xFFFFFF, firstFeatureCount: 2);
        AddRejected(cases, names, [1, 2], entryMethod: 1, methodDefCount: 0);
        cases.Add(Valid(names, [1, 2], 8, entryMethod: 1, methodDefCount: 2));
        AddRejected(cases, names, [1, 2], moduleRefCount: 1, secondModuleRefStart: 0);
        AddRejected(cases, names, [1, 2], moduleRefCount: 1, secondModuleRefStart: 3);
        cases.Add(Valid(names, [1, 2], 8, moduleRefCount: 1, secondModuleRefStart: 2));
        cases.Add(new TableCase(
            [.. EncodeStringSection(
                [0, 1, 0, 0, 65, 0, 0, 0],
                [1, 1, 0, 0, 192, 128, 0, 0]).Select(static value => (int)value)],
            [.. EncodeModules([1]).Select(static value => (int)value)],
            1, 2, 0, 0, 0, 0, -1, true, true, 2));
        cases.Add(new TableCase(
            [.. EncodeStringSection(
                [0, 1, 0, 0, 65, 0, 0, 0],
                [4, 1, 0, 0, 65, 0, 0, 0]).Select(static value => (int)value)],
            [.. EncodeModules([1]).Select(static value => (int)value)],
            1, 2, 0, 0, 0, 0, -1, true, true, 2));
        AddRejected(cases, EncodeStringSection([0, 0, 0, 0, 0, 0, 0, 0]),
            [1], 1);
        AddRejected(cases, EncodeStringSection([2, 1, 0, 0, 0, 0, 0, 0]),
            [1], 1);
        AddRejected(cases, EncodeStringSection([1, 1, 0, 0, 192, 128, 0, 0]),
            [1], 1, hasHostedOracle: true);
        cases.Add(Valid(names, [1], 8) with {
            ExpectedValid = false, HasHostedOracle = false, IdentitySlotCount = 1,
        });
        cases.Add(Valid(names, [1], 8) with {
            ExpectedValid = false, HasHostedOracle = false, IdentitySlotCount = 3,
        });

        var invalidStringByte = names.Select(static value => (int)value).ToArray();
        invalidStringByte[9 * sizeof(uint)] = 256;
        cases.Add(new TableCase(invalidStringByte,
            [.. EncodeModules([1]).Select(static value => (int)value)],
            1, 8, 0, 0, 0, 0, -1, false, false, 2));

        var invalidModuleByte = EncodeModules([1]).Select(static value => (int)value).ToArray();
        invalidModuleByte[4] = 256;
        cases.Add(new TableCase([.. names.Select(static value => (int)value)], invalidModuleByte,
            1, 8, 0, 0, 0, 0, -1, false, false, 2));
        cases.Add(Valid(names, [1], 8) with {
            Modules = [.. EncodeModules([1])[..^1].Select(static value => (int)value)],
            ExpectedValid = false,
        });
        var invalidIndex = names.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(invalidIndex, 4);
        cases.Add(Valid(names, [1], 8) with {
            Strings = [.. invalidIndex.Select(static value => (int)value)],
            ExpectedValid = false,
        });

        var root = RepositoryRoot();
        var components = new List<SyntaxTree>();
        foreach (var name in new[] {
            "VariableTableIndex.terrum",
            "StringRowLayout.terrum",
            "StringRowText.terrum",
            "ModuleTableValidation.terrum",
        })
        {
            var path = Path.Combine(root, "self-host", name);
            var tree = SyntaxTree.Parse(await File.ReadAllTextAsync(path).ConfigureAwait(true), path);
            Assert.False(tree.HasErrors, string.Join(Environment.NewLine, tree.Diagnostics));
            components.Add(tree);
        }
        components.Add(SyntaxTree.Parse(VerificationSource, "module-table-validation.terrum"));

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
                new HostBuffer("IdentitySlots", PrimitiveType.Int32,
                    [.. Enumerable.Repeat(123, testCase.IdentitySlotCount)], true),
                new HostBuffer("Expected", PrimitiveType.Int32,
                [
                    testCase.RecordCount,
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

    private static TableCase Valid(byte[] strings, int[] identities, int stringCount,
        bool expectedValid = true,
        int moduleRefCount = 0, int typeDefCount = 0, int methodDefCount = 0,
        int sourceFileCount = 0, int featureCount = -1,
        int firstFeature = 0, int firstFeatureCount = 0, int entryMethod = 0,
        int secondModuleRefStart = 1)
    {
        var modules = EncodeModules(identities, moduleRefCount, typeDefCount, methodDefCount,
            sourceFileCount, firstFeature, firstFeatureCount, entryMethod, secondModuleRefStart);
        return new TableCase([.. strings.Select(static value => (int)value)],
            [.. modules.Select(static value => (int)value)], identities.Length, stringCount,
            moduleRefCount, typeDefCount, methodDefCount, sourceFileCount, featureCount, expectedValid, true,
            identities.Length + 1);
    }

    private static void AddRejected(List<TableCase> cases, byte[] strings, int[] identities,
        int stringCount = 8,
        bool hasHostedOracle = true, int moduleRefCount = 0, int methodDefCount = 0,
        int typeDefCount = 0, int sourceFileCount = 0,
        int featureCount = -1, int firstFeature = 0, int firstFeatureCount = 0,
        int entryMethod = 0, int secondModuleRefStart = 1)
    {
        var testCase = Valid(strings, identities, stringCount, expectedValid: false,
            moduleRefCount: moduleRefCount, typeDefCount: typeDefCount,
            methodDefCount: methodDefCount, sourceFileCount: sourceFileCount,
            featureCount: featureCount, firstFeature: firstFeature,
            firstFeatureCount: firstFeatureCount, entryMethod: entryMethod,
            secondModuleRefStart: secondModuleRefStart) with { HasHostedOracle = hasHostedOracle };
        cases.Add(testCase);
    }

    private static void AddRejectedWithModuleWord(List<TableCase> cases, TableCase source,
        int byteOffset, uint value)
    {
        var modules = source.Modules.Select(static part => checked((byte)part)).ToArray();
        WriteWord(modules, byteOffset, value);
        cases.Add(source with {
            Modules = [.. modules.Select(static part => (int)part)],
            ExpectedValid = false,
        });
    }

    private static bool ValidateHosted(TableCase testCase)
    {
        try
        {
            var strings = new TrRmStringTableReader(
                testCase.Strings.Select(static value => checked((byte)value)).ToArray(),
                testCase.StringCount);
            _ = new TrRmModuleTableReader(
                testCase.Modules.Select(static value => checked((byte)value)).ToArray(),
                testCase.RecordCount, strings, testCase.ModuleRefCount, testCase.TypeDefCount,
                testCase.MethodDefCount, testCase.SourceFileCount, testCase.FeatureCount);
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
        var dataLength = rows.Sum(static row => row.Length);
        var section = new byte[indexLength + dataLength];
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

    private static byte[] EncodeModules(int[] identities, int moduleRefCount = 0,
        int typeDefCount = 0, int methodDefCount = 0, int sourceFileCount = 0,
        int firstFeature = 0, int firstFeatureCount = 0,
        int entryMethod = 0, int secondModuleRefStart = 1)
    {
        var modules = new byte[identities.Length * 44];
        for (var row = 0; row < identities.Length; row++)
        {
            var offset = row * 44;
            WriteWord(modules, offset, checked((uint)identities[row]));
            WriteWord(modules, offset + 12, row == 0 ? checked((uint)entryMethod) : 0);
            WriteWord(modules, offset + 16, row == 0 ? checked((uint)firstFeature) : 0);
            WriteWord(modules, offset + 20, row == 0 ? checked((uint)firstFeatureCount) : 0);
            WriteWord(modules, offset + 28,
                row == 0 ? 1u : checked((uint)secondModuleRefStart));
            WriteWord(modules, offset + 32, row == 0 || typeDefCount == 0 ? 1u : 2u);
            WriteWord(modules, offset + 36, row == 0 || methodDefCount == 0 ? 1u : 2u);
            WriteWord(modules, offset + 40, row == 0 || sourceFileCount == 0 ? 1u : 2u);
        }
        return modules;
    }

    private static void WriteWord(byte[] bytes, int offset, uint value)
        => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);

    private static async Task<int> ExecuteAotAsync(BoundProgram program, NativeRuntimePolicy policy)
    {
        var path = Path.Combine(Path.GetTempPath(), $"module-table-validation-{Guid.NewGuid():N}.exe");
        try
        {
            await File.WriteAllBytesAsync(path, NativeEmitter.Emit(program, policy)).ConfigureAwait(true);
            using var process = Process.Start(new ProcessStartInfo(path) {
                UseShellExecute = false,
                CreateNoWindow = true,
            }) ?? throw new InvalidOperationException("Could not start compiled Module table validator.");
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

    private sealed record TableCase(int[] Strings, int[] Modules, int RecordCount,
        int StringCount, int ModuleRefCount, int TypeDefCount, int MethodDefCount,
        int SourceFileCount, int FeatureCount, bool ExpectedValid, bool HasHostedOracle,
        int IdentitySlotCount);
}
