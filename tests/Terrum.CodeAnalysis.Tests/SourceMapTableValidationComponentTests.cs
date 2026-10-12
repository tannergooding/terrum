// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
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

public sealed class SourceMapTableValidationComponentTests
{
    private const string VerificationSource = """
        namespace Terrum.CodeAnalysis;
        int32 Main() {
            bool valid = TryValidateSourceMapTable(
                Host.Expected[0], Host.Expected[1], Host.Expected[2],
                Host.Expected[3], Host.Expected[4], Host.Expected[5],
                Host.Expected[6], Host.Expected[7], Host.Expected[8],
                Host.Expected[9], Host.Expected[10], Host.Expected[11],
                Host.Expected[12], Host.Expected[13], Host.Expected[14])
                && TryValidateCompactSourceMapLocations(
                    Host.Expected[5], Host.Expected[13]);
            return valid == (Host.Expected[15] != 0) ? 0 : 1;
        }
        """;

    [Theory]
    [InlineData(BoundRuntimePolicy.FailFast)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions)]
    public async Task SourceMapValidationMatchesHostedReader(BoundRuntimePolicy policy)
    {
        var valid = CreateCase();
        var manyModules = CreateManyModuleCase();
        var twoInstructions = CreateCase(twoMethods: true);
        var twoModuleTransitions = CreateCase(twoModules: true, reverseBodyOwners: true);
        var twoBlockBody = valid with {
            SourceMapOperationBlockStarts = [0, 1],
            SourceMapOperationBlockCounts = [0, 2],
            SourceMapOperationCounts = [0, 1, 2],
        };
        var secondBlock = WithWord(twoBlockBody, 12, 2);
        var unusedSlots = WithSourceMap(valid, MapWithUnusedSlots());
        var cases = new List<TableCase> {
            valid,
            manyModules,
            WithWord(valid, 16, 1),
            secondBlock,
            WithWord(secondBlock, 16, 2),
            ChangeWord(secondBlock, 12, 3),
            ChangeWord(secondBlock, 16, 3),
            twoInstructions,
            twoModuleTransitions,
            ChangeWord(valid, 16, uint.MaxValue),
            ChangeWord(valid, 12, uint.MaxValue),
            ChangeWord(valid, 12, 2),
            ChangeWord(valid, 16, 2),
            WithWord(valid, 44, uint.MaxValue - 2),
            WithWord(WithWord(valid, 44, uint.MaxValue), 48, 0),
            unusedSlots,
            Reject(ChangeHalfWord(unusedSlots, 38, 0)),
            Reject(ChangeWord(unusedSlots, 40, 0)),
            Reject(ChangeWord(unusedSlots, 44, 0)),
            Reject(ChangeHalfWord(unusedSlots, 54, 1)),
            Reject(ChangeWord(unusedSlots, 60, 1)),
            Reject(valid with { RecordCount = 0 }),
            Reject(valid with { RecordCount = 2 }),
            Reject(valid with { SourceMaps = valid.SourceMaps[..^1] }),
            Reject(ChangeWord(valid, 0, 0)),
            Reject(ChangeWord(valid, 0, uint.MaxValue)),
            Reject(ChangeHalfWord(valid, 4, 0x003F)),
            Reject(ChangeHalfWord(valid, 6, 0)),
            Reject(ChangeWord(valid, 8, 0)),
            Reject(ChangeWord(valid, 12, 0)),
            Reject(ChangeHalfWord(valid, 22, 1)),
            Reject(ChangeWord(valid, 28, 1)),
            Reject(ChangeWord(valid, 32, 1)),
            Reject(ChangeWord(valid, 8, 2)),
            Reject(ChangeWord(valid, 24, 2)),
            Reject(ChangeWord(valid, 36, 0)),
            Reject(ChangeWord(valid, 36, 2)),
            Reject(ChangeWord(valid, 36, 3)),
            Reject(ChangeWord(valid, 36, uint.MaxValue)),
            Reject(ChangeWord(valid, 40, 0)),
            Reject(ChangeWord(valid, 40, 1)),
            Reject(ChangeWord(valid, 40, 3)),
            Reject(ChangeWord(valid, 40, uint.MaxValue)),
            Reject(ChangeWord(valid, 44, uint.MaxValue)),
            Reject(ChangeWord(twoInstructions, 68, 1)),
            Reject(CreateCase(twoModules: true, fileRow: 2)),
            Reject(valid with { SourceFileCount = 2 }),
        };

        var invalidSourceFilePath = valid.SourceFiles.ToArray();
        invalidSourceFilePath[0] = 4;
        cases.Add(Reject(valid with { SourceFiles = invalidSourceFilePath }));

        var invalidByte = valid.SourceMaps.ToArray();
        invalidByte[0] = 256;
        cases.Add(valid with {
            SourceMaps = invalidByte,
            ExpectedValid = false,
            HasHostedOracle = false,
        });

        var root = RepositoryRoot();
        var components = new List<SyntaxTree>();
        foreach (var name in new[] {
            "VariableTableIndex.terrum",
            "StringRowLayout.terrum",
            "StringRowText.terrum",
            "ModuleTableValidation.terrum",
            "TypeDefTableValidation.terrum",
            "MethodDefTableValidation.terrum",
            "SourceFileTableValidation.terrum",
            "SourceMapTableValidation.terrum",
        })
        {
            var path = Path.Combine(root, "self-host", name);
            var tree = SyntaxTree.Parse(await File.ReadAllTextAsync(path).ConfigureAwait(true), path);
            Assert.False(tree.HasErrors, string.Join(Environment.NewLine, tree.Diagnostics));
            components.Add(tree);
        }
        components.Add(SyntaxTree.Parse(VerificationSource, "source-map-table-validation.terrum"));

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
                new HostBuffer("Methods", PrimitiveType.Int32, [.. testCase.Methods], false),
                new HostBuffer("Signatures", PrimitiveType.Int32, [.. testCase.Signatures], false),
                new HostBuffer("SourceFiles", PrimitiveType.Int32, [.. testCase.SourceFiles], false),
                new HostBuffer("SourceMaps", PrimitiveType.Int32, [.. testCase.SourceMaps], false),
                new HostBuffer("IdentitySlots", PrimitiveType.Int32,
                    [.. Enumerable.Repeat(123, testCase.ModuleCount + 1)], true),
                new HostBuffer("FieldOwners", PrimitiveType.Int32, [123], true),
                new HostBuffer("BodyOwners", PrimitiveType.Int32,
                    [.. Enumerable.Repeat(123, testCase.BodyCount + 1)], true),
                new HostBuffer("SourceMapOperationBlockStarts", PrimitiveType.Int32,
                    [.. testCase.SourceMapOperationBlockStarts], false),
                new HostBuffer("SourceMapOperationBlockCounts", PrimitiveType.Int32,
                    [.. testCase.SourceMapOperationBlockCounts], false),
                new HostBuffer("SourceMapOperationCounts", PrimitiveType.Int32,
                    [.. testCase.SourceMapOperationCounts], false),
                new HostBuffer("GenericSignatureOwners", PrimitiveType.Int32, [123, 123], true),
                new HostBuffer("Expected", PrimitiveType.Int32,
                [
                    testCase.RecordCount,
                    testCase.ModuleCount,
                    testCase.StringCount,
                    0,
                    0,
                    testCase.MethodCount,
                    testCase.SourceFileCount,
                    0,
                    0,
                    0,
                    0,
                    0,
                    1,
                    testCase.BodyCount,
                    testCase.SourceFileCount,
                    testCase.ExpectedValid ? 1 : 0,
                ], false),
            ], policy);
            Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
            var program = Assert.IsType<BoundProgram>(compilation.Program);
            Assert.Equal(0, NativeJit.Execute(program, (NativeRuntimePolicy)policy));
            Assert.Equal(0, await ExecuteAotAsync(program, (NativeRuntimePolicy)policy).ConfigureAwait(true));
        }
    }

    private static TableCase CreateCase(bool twoMethods = false,
        bool twoModules = false, uint fileRow = 1, bool reverseBodyOwners = false)
    {
        var names = twoModules
            ? TrRmStringTableWriter.EncodeNames([
                ("ModuleA", 0), ("ModuleB", 0), ("Run", 0), ("src/A", 0), ("src/B", 0),
            ])
            : TrRmStringTableWriter.EncodeNames([
                ("Module", 0), ("Run", 0), ("src/A", 0),
            ]);
        var moduleCount = twoModules ? 2 : 1;
        var methodCount = twoMethods || twoModules ? 2 : 1;
        var sourceFileCount = twoModules ? 2 : 1;
        var methodName = twoModules ? 3u : 2u;
        var sourceFileBytes = new byte[sourceFileCount * 8];
        Write(sourceFileBytes, 0, twoModules ? 4u : 3u);
        if (twoModules)
        {
            Write(sourceFileBytes, 8, 5);
        }

        var methodBytes = new byte[methodCount * 28];
        var operationBlockStarts = new int[methodCount + 1];
        var operationBlockCounts = new int[methodCount + 1];
        var operationCounts = new int[methodCount + 1];
        for (var row = 0; row < methodCount; row++)
        {
            Write(methodBytes, row * 28 + 4, methodName);
            Write(methodBytes, row * 28 + 8, 1);
            var bodyRow = reverseBodyOwners ? (uint)(methodCount - row) : (uint)row + 1;
            Write(methodBytes, row * 28 + 24, bodyRow);
            operationBlockStarts[row + 1] = row + 1;
            operationBlockCounts[row + 1] = 1;
            operationCounts[row + 1] = 1;
        }

        var sourceMap = twoModules && reverseBodyOwners
            ? TwoModuleMap()
            : twoMethods ? TwoInstructionMap() : Map(fileRow);
        return new TableCase(
            [.. names.Select(static value => (int)value)],
            [.. EncodeModules(moduleCount, methodCount, sourceFileCount)
                .Select(static value => (int)value)],
            [],
            [.. methodBytes.Select(static value => (int)value)],
            [.. EncodeSignature().Select(static value => (int)value)],
            [.. sourceFileBytes.Select(static value => (int)value)],
            [.. sourceMap.Select(static value => (int)value)],
            moduleCount,
            twoModules ? 5 : 3,
            methodCount,
            sourceFileCount,
            methodCount,
            operationBlockStarts,
            operationBlockCounts,
            operationCounts,
            twoMethods || reverseBodyOwners ? 2 : 1,
            true,
            true);
    }

    private static TableCase CreateManyModuleCase()
    {
        const int moduleCount = 512;
        var names = TrRmStringTableWriter.EncodeNames(Enumerable.Range(0, moduleCount)
            .Select(static index => ("Module" + index.ToString("D4", CultureInfo.InvariantCulture), 0))
            .Append(("Run", 0))
            .Append(("src/A", 0))
            .ToArray());
        var modules = new byte[moduleCount * 44];
        for (var row = 0; row < moduleCount; row++)
        {
            var offset = row * 44;
            Write(modules, offset, (uint)row + 1);
            Write(modules, offset + 4, 1);
            Write(modules, offset + 28, 1);
            Write(modules, offset + 32, 1);
            Write(modules, offset + 36, 1);
            Write(modules, offset + 40, 1);
        }

        var method = new byte[28];
        Write(method, 4, moduleCount + 1);
        Write(method, 8, 1);
        Write(method, 24, 1);
        var sourceFile = new byte[8];
        Write(sourceFile, 0, moduleCount + 2);
        return new TableCase(
            [.. names.Select(static value => (int)value)],
            [.. modules.Select(static value => (int)value)],
            [],
            [.. method.Select(static value => (int)value)],
            [.. EncodeSignature().Select(static value => (int)value)],
            [.. sourceFile.Select(static value => (int)value)],
            [.. Map(1).Select(static value => (int)value)],
            moduleCount,
            moduleCount + 2,
            1,
            1,
            1,
            [0, 1],
            [0, 1],
            [0, 1],
            1,
            true,
            true);
    }

    private static TableCase Reject(TableCase testCase)
        => testCase with { ExpectedValid = false };

    private static TableCase ChangeWord(TableCase testCase, int offset, uint value)
        => Reject(WithWord(testCase, offset, value));

    private static TableCase WithSourceMap(TableCase testCase, byte[] sourceMap)
        => testCase with { SourceMaps = [.. sourceMap.Select(static item => (int)item)] };

    private static TableCase WithWord(TableCase testCase, int offset, uint value)
    {
        var bytes = ToBytes(testCase.SourceMaps);
        Write(bytes, offset, value);
        return testCase with { SourceMaps = [.. bytes.Select(static item => (int)item)] };
    }

    private static TableCase ChangeHalfWord(TableCase testCase, int offset, ushort value)
    {
        var bytes = ToBytes(testCase.SourceMaps);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(offset), value);
        return Reject(testCase with { SourceMaps = [.. bytes.Select(static item => (int)item)] });
    }

    private static bool ValidateHosted(TableCase testCase)
    {
        try
        {
            var stringReader = new TrRmStringTableReader(ToBytes(testCase.Strings), testCase.StringCount);
            var modules = new TrRmModuleTableReader(ToBytes(testCase.Modules),
                testCase.ModuleCount, stringReader, 0, 0, testCase.MethodCount,
                testCase.SourceFileCount, 0);
            var types = new TrRmTypeDefTableReader([], 0, stringReader, modules, 0, 0, 0, 0);
            byte[] typeSpec = [0, 0, 0, 0, 3, 0, 0, 0, 1, 0, 8];
            var typeSpecs = new TrRmTypeSpecTableReader(typeSpec, 1, 0, 0, 0, 0);
            var signatures = new TrRmMethodSigTableReader(ToBytes(testCase.Signatures),
                1, typeSpecs, 0, 0);
            var methods = new TrRmMethodDefTableReader(ToBytes(testCase.Methods),
                testCase.MethodCount, stringReader, modules, types, signatures, 0,
                testCase.BodyCount);
            var files = new TrRmSourceFileTableReader(ToBytes(testCase.SourceFiles),
                testCase.SourceFileCount, stringReader, modules);
            var sourceMaps = new TrRmSourceMapTableReader(ToBytes(testCase.SourceMaps),
                testCase.RecordCount, methods, files);
            var operationCounts = new int[methods.Count + 1][];
            for (var method = 1; method <= methods.Count; method++)
            {
                var blockCount = testCase.SourceMapOperationBlockCounts[method];
                var counts = new int[blockCount + 1];
                var firstCount = testCase.SourceMapOperationBlockStarts[method];
                Array.Copy(testCase.SourceMapOperationCounts, firstCount, counts, 1, blockCount);
                operationCounts[method] = counts;
            }
            sourceMaps.ValidateCompactLocations(methods, operationCounts);
            return true;
        }
        catch (InvalidDataException)
        {
            return false;
        }
    }

    private static byte[] EncodeModules(int moduleCount, int methodCount, int sourceFileCount)
    {
        var modules = new byte[moduleCount * 44];
        for (var row = 0; row < moduleCount; row++)
        {
            var offset = row * 44;
            Write(modules, offset, (uint)row + 1);
            Write(modules, offset + 4, 1);
            Write(modules, offset + 28, 1);
            Write(modules, offset + 32, 1);
            Write(modules, offset + 36, row == 0 ? 1u : (uint)methodCount);
            Write(modules, offset + 40, row == 0 ? 1u : (uint)sourceFileCount);
        }
        return modules;
    }

    private static byte[] EncodeSignature()
    {
        var signature = new byte[24];
        Write(signature, 0, 1);
        return signature;
    }

    private static byte[] Map(uint fileRow)
    {
        var bytes = new byte[52];
        Write(bytes, 0, 2);
        WriteHalfWord(bytes, 4, 0x0020);
        WriteHalfWord(bytes, 6, 1);
        Write(bytes, 8, 1);
        Write(bytes, 12, 1);
        WriteHalfWord(bytes, 20, 0x001F);
        Write(bytes, 24, fileRow);
        Write(bytes, 36, 1);
        Write(bytes, 40, 2);
        Write(bytes, 48, 2);
        return bytes;
    }

    private static byte[] TwoInstructionMap()
    {
        var bytes = new byte[84];
        Write(bytes, 0, 3);
        WriteHalfWord(bytes, 4, 0x0020);
        WriteHalfWord(bytes, 6, 1);
        Write(bytes, 8, 1);
        Write(bytes, 12, 1);
        WriteHalfWord(bytes, 20, 0x0020);
        WriteHalfWord(bytes, 22, 1);
        Write(bytes, 24, 2);
        Write(bytes, 28, 1);
        WriteHalfWord(bytes, 36, 0x001F);
        Write(bytes, 40, 1);
        Write(bytes, 52, 1);
        Write(bytes, 56, 3);
        Write(bytes, 60, 0);
        Write(bytes, 64, 2);
        Write(bytes, 68, 2);
        Write(bytes, 72, 3);
        Write(bytes, 76, 2);
        Write(bytes, 80, 3);
        return bytes;
    }

    private static byte[] TwoModuleMap()
    {
        var bytes = new byte[100];
        Write(bytes, 0, 4);
        WriteHalfWord(bytes, 4, 0x0020);
        WriteHalfWord(bytes, 6, 1);
        Write(bytes, 8, 2);
        Write(bytes, 12, 1);
        WriteHalfWord(bytes, 20, 0x0020);
        WriteHalfWord(bytes, 22, 1);
        Write(bytes, 24, 1);
        Write(bytes, 28, 1);
        WriteHalfWord(bytes, 36, 0x001F);
        Write(bytes, 40, 1);
        WriteHalfWord(bytes, 52, 0x001F);
        Write(bytes, 56, 2);
        Write(bytes, 68, 1);
        Write(bytes, 72, 3);
        Write(bytes, 76, 0);
        Write(bytes, 80, 2);
        Write(bytes, 84, 2);
        Write(bytes, 88, 4);
        Write(bytes, 92, 2);
        Write(bytes, 96, 3);
        return bytes;
    }

    private static byte[] MapWithUnusedSlots()
    {
        var original = Map(1);
        var bytes = new byte[84];
        Write(bytes, 0, 4);
        original.AsSpan(4, 32).CopyTo(bytes.AsSpan(4));
        WriteHalfWord(bytes, 36, 0x0020);
        WriteHalfWord(bytes, 38, 1);
        Write(bytes, 40, 1);
        Write(bytes, 44, 1);
        WriteHalfWord(bytes, 52, 0x001F);
        Write(bytes, 56, 1);
        original.AsSpan(36, 16).CopyTo(bytes.AsSpan(68));
        return bytes;
    }

    private static byte[] ToBytes(int[] values)
        => values.Select(static value => checked((byte)value)).ToArray();

    private static void Write(byte[] bytes, int offset, uint value)
        => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);

    private static void WriteHalfWord(byte[] bytes, int offset, ushort value)
        => BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(offset), value);

    private static async Task<int> ExecuteAotAsync(BoundProgram program, NativeRuntimePolicy policy)
    {
        var path = Path.Combine(Path.GetTempPath(), $"source-map-table-validation-{Guid.NewGuid():N}.exe");
        try
        {
            await File.WriteAllBytesAsync(path, NativeEmitter.Emit(program, policy)).ConfigureAwait(true);
            using var process = Process.Start(new ProcessStartInfo(path) {
                UseShellExecute = false,
                CreateNoWindow = true,
            }) ?? throw new InvalidOperationException("Could not start compiled SourceMap table validator.");
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

    private sealed record TableCase(int[] Strings, int[] Modules, int[] TypeDefs,
        int[] Methods, int[] Signatures, int[] SourceFiles, int[] SourceMaps,
        int ModuleCount, int StringCount, int MethodCount, int SourceFileCount,
        int BodyCount, int[] SourceMapOperationBlockStarts, int[] SourceMapOperationBlockCounts,
        int[] SourceMapOperationCounts, int RecordCount, bool ExpectedValid, bool HasHostedOracle);
}
