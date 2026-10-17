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

public sealed class TypeSpecTableValidationComponentTests
{
    private const string VerificationSource = """
        namespace Terrum.CodeAnalysis;
        int32 Main() {
            bool valid = TryValidateTypeSpecTable(
                Host.Expected[0], Host.Expected[1], Host.Expected[2],
                Host.Expected[3], Host.Expected[4]);
            return valid == (Host.Expected[5] != 0) ? 0 : 1;
        }
        """;
    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
    public async Task TypeSpecTableValidationMatchesHostedStructuralReader(BoundRuntimePolicy policy)
    {
        const int typeDefCount = 1;
        const int importCount = 1;
        const int genericParamCount = 1;
        const int typeArgCount = 2;
        var cases = new List<TableCase>();

        ReadOnlyMemory<byte>[] validRows = [
            TrRmTypeSpecTableWriter.Builtin(8),
            TrRmTypeSpecTableWriter.Reference(0x02, 1),
            TrRmTypeSpecTableWriter.Reference(0x03, 1),
            TrRmTypeSpecTableWriter.Reference(0x04, 1),
            TrRmTypeSpecTableWriter.Constructed(2, 1, 2),
            TrRmTypeSpecTableWriter.Wrapped(0x06, 1),
            TrRmTypeSpecTableWriter.Wrapped(0x07, 2),
            TrRmTypeSpecTableWriter.Wrapped(0x08, 2),
            TrRmTypeSpecTableWriter.Wrapped(0x09, 5),
            TrRmTypeSpecTableWriter.Wrapped(0x0A, 2),
            TrRmTypeSpecTableWriter.Wrapped(0x0B, 5),
            TrRmTypeSpecTableWriter.IndexedFamily(0x80, 2),
            TrRmTypeSpecTableWriter.InlineFamily(0x8F, 0x19),
            TrRmTypeSpecTableWriter.IndexedFamily(0x8A, 2),
        ];
        var validTable = TrRmTypeSpecTableWriter.Encode(validRows,
            typeDefCount, importCount, genericParamCount, typeArgCount);
        cases.Add(Valid(validTable, validRows.Length, typeDefCount, importCount,
            genericParamCount, typeArgCount));
        cases.Add(Valid(validTable, validRows.Length, 0xFF_FFFF, 0xFF_FFFF,
            0xFF_FFFF, typeArgCount));
        var overlappingRanges = TrRmVariableTableWriter.EncodeBlobs([
            TrRmTypeSpecTableWriter.Builtin(8),
            TrRmTypeSpecTableWriter.Reference(0x02, 1),
            TrRmTypeSpecTableWriter.Constructed(2, 1, 2),
            TrRmTypeSpecTableWriter.Constructed(2, 1, 2),
        ]);
        cases.Add(Valid(overlappingRanges, 4, typeDefCount, importCount,
            genericParamCount, typeArgCount));
        AddRejectedTableCase(cases, WithOffset(validTable, 0, 1), validRows.Length,
            typeDefCount, importCount, genericParamCount, typeArgCount);
        AddRejectedTableCase(cases, WithOffset(validTable, 1, 0), validRows.Length,
            typeDefCount, importCount, genericParamCount, typeArgCount);
        AddRejectedTableCase(cases, [.. validTable, 0], validRows.Length,
            typeDefCount, importCount, genericParamCount, typeArgCount);

        cases.Add(new TableCase([], 0, typeDefCount, importCount, genericParamCount,
            typeArgCount, false, true));
        AddRejectedTableCase(cases, validTable, 0, typeDefCount, importCount,
            genericParamCount, typeArgCount);
        AddRejectedTableCase(cases, validTable, 0x1000000, typeDefCount, importCount,
            genericParamCount, typeArgCount);
        foreach (var invalidCount in new[] { -1, 0x1000000 })
        {
            AddRejectedTableCase(cases, validTable, validRows.Length, invalidCount,
                importCount, genericParamCount, typeArgCount);
            AddRejectedTableCase(cases, validTable, validRows.Length, typeDefCount,
                invalidCount, genericParamCount, typeArgCount);
            AddRejectedTableCase(cases, validTable, validRows.Length, typeDefCount,
                importCount, invalidCount, typeArgCount);
            AddRejectedTableCase(cases, validTable, validRows.Length, typeDefCount,
                importCount, genericParamCount, invalidCount);
        }
        AddInvalid(cases, [Array.Empty<byte>()], 1,
            typeDefCount, importCount, genericParamCount, typeArgCount);
        AddInvalid(cases, [[1, 2, 8]], 1,
            typeDefCount, importCount, genericParamCount, typeArgCount);
        AddInvalid(cases, [[1, 0, 0x1A]], 1,
            typeDefCount, importCount, genericParamCount, typeArgCount);
        AddInvalid(cases, [[1, 0, 8, 0]], 1,
            typeDefCount, importCount, genericParamCount, typeArgCount);
        AddInvalid(cases, [[1, 0, 0x80]], 1,
            typeDefCount, importCount, genericParamCount, typeArgCount);
        AddInvalid(cases, [[1, 0, 0x90, 8]], 1,
            typeDefCount, importCount, genericParamCount, typeArgCount);
        AddInvalid(cases, [[1, 0, 0x80, 0x1A]], 1,
            typeDefCount, importCount, genericParamCount, typeArgCount);
        AddInvalid(cases, [[2, 0, 1, 0, 0, 0]], 1, 0, importCount, genericParamCount, typeArgCount);
        AddInvalid(cases, [[2, 0, 0, 0, 0, 0]], 1, typeDefCount, importCount, genericParamCount, typeArgCount);
        AddInvalid(cases, [[2, 0, 2, 0, 0, 0]], 1, typeDefCount, importCount, genericParamCount, typeArgCount);
        AddInvalid(cases, [[2, 0, 1]], 1, typeDefCount, importCount, genericParamCount, typeArgCount);
        AddInvalid(cases, [[5, 0, 1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0]], 1,
            typeDefCount, importCount, genericParamCount, typeArgCount);
        AddInvalid(cases, [
            [1, 0, 8],
            [5, 0, 2, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0],
        ], 2, typeDefCount, importCount, genericParamCount, typeArgCount);
        AddInvalid(cases, [
            [1, 0, 8],
            [5, 0, 1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0],
        ], 2, typeDefCount, importCount, genericParamCount, typeArgCount);
        AddInvalid(cases, [
            [2, 0, 1, 0, 0, 0],
            [5, 0, 1, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0],
        ], 2, typeDefCount, importCount, genericParamCount, typeArgCount);
        AddInvalid(cases, [
            [2, 0, 1, 0, 0, 0],
            [5, 0, 1, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0],
        ], 2, typeDefCount, importCount, genericParamCount, typeArgCount);
        AddInvalid(cases, [
            [2, 0, 1, 0, 0, 0],
            [5, 0, 1, 0, 0, 0, 2, 0, 0, 0, 2, 0, 0, 0],
        ], 2, typeDefCount, importCount, genericParamCount, typeArgCount);
        AddInvalid(cases, [[7, 0, 1, 0, 0, 0]], 1,
            typeDefCount, importCount, genericParamCount, typeArgCount);
        AddInvalid(cases, [
            [6, 0, 2, 0, 0, 0],
            [1, 0, 8],
        ], 2, typeDefCount, importCount, genericParamCount, typeArgCount);
        AddInvalid(cases, [
            [1, 0, 8],
            [7, 0, 1, 0, 0, 0],
        ], 2, typeDefCount, importCount, genericParamCount, typeArgCount);
        AddInvalid(cases, [
            [1, 0, 8],
            [7, 0, 1, 0, 0, 0, 0],
        ], 2, typeDefCount, importCount, genericParamCount, typeArgCount);
        AddInvalid(cases, [
            [1, 0, 8],
            [0x0C, 0, 0x8B, 1, 0, 0, 0],
        ], 2, typeDefCount, importCount, genericParamCount, typeArgCount);
        AddInvalid(cases, [
            [0x0C, 0, 0x80, 2, 0, 0, 0],
            [1, 0, 8],
        ], 2, typeDefCount, importCount, genericParamCount, typeArgCount);
        AddInvalid(cases, [
            [1, 0, 8],
            [0x0C, 0, 0x80, 1, 0, 0, 0],
        ], 2, typeDefCount, importCount, genericParamCount, typeArgCount);

        var invalidByteWords = validTable.Select(static value => (int)value).ToArray();
        invalidByteWords[(validRows.Length + 1) * sizeof(uint) + 1] = 256;
        cases.Add(new TableCase(invalidByteWords, validRows.Length, typeDefCount,
            importCount, genericParamCount, typeArgCount, false, false));

        var root = RepositoryRoot();
        var componentPaths = new[] {
            "TypeSpecTableValidation.terrum",
        };
        var components = new List<SyntaxTree>();
        foreach (var name in componentPaths)
        {
            var path = SelfHostPaths.ResolveFile(root, name);
            var tree = SyntaxTree.Parse(await File.ReadAllTextAsync(path).ConfigureAwait(true), path);
            Assert.That(tree.HasErrors, Is.False, string.Join(Environment.NewLine, tree.Diagnostics));
            components.Add(tree);
        }
        var entry = SyntaxTree.Parse(VerificationSource, "type-spec-table-validation-component.terrum");
        components.Add(entry);

        foreach (var testCase in cases)
        {
            if (testCase.HasHostedOracle)
            {
                var hostedSection = testCase.Section.Select(static value => checked((byte)value)).ToArray();
                var hostedValid = ValidateHosted(hostedSection, testCase.RecordCount,
                    testCase.TypeDefCount, testCase.ImportCount,
                    testCase.GenericParamCount, testCase.TypeArgCount);
                Assert.That(hostedValid, Is.EqualTo(testCase.ExpectedValid));
            }

            var compilation = Compilation.Create(components,
            [
                new HostBuffer("TypeSpecs", PrimitiveType.Int32, [.. testCase.Section], false),
                new HostBuffer("Expected", PrimitiveType.Int32,
                [
                    testCase.RecordCount,
                    testCase.TypeDefCount,
                    testCase.ImportCount,
                    testCase.GenericParamCount,
                    testCase.TypeArgCount,
                    testCase.ExpectedValid ? 1 : 0,
                ], false),
            ], policy);
            Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
            var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual0 = compilation.Program; Assert.That(__nunitActual0, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual0!; }))();
            Assert.That(NativeJit.Execute(program, (NativeRuntimePolicy)policy), Is.EqualTo(0));
            Assert.That(await ExecuteAotAsync(program, (NativeRuntimePolicy)policy).ConfigureAwait(true), Is.EqualTo(0));
        }
    }

    private static TableCase Valid(byte[] section, int recordCount, int typeDefCount,
        int importCount, int genericParamCount, int typeArgCount)
    {
        var valid = ValidateHosted(section, recordCount, typeDefCount, importCount,
            genericParamCount, typeArgCount);
        Assert.That(valid, Is.True);
        return new TableCase([.. section.Select(static value => (int)value)], recordCount,
            typeDefCount, importCount, genericParamCount, typeArgCount, true, true);
    }

    private static void AddRejectedTableCase(List<TableCase> cases, byte[] section, int recordCount,
        int typeDefCount, int importCount, int genericParamCount, int typeArgCount)
    {
        Assert.That(ValidateHosted(section, recordCount, typeDefCount, importCount,
            genericParamCount, typeArgCount), Is.False);
        cases.Add(new TableCase([.. section.Select(static value => (int)value)], recordCount,
            typeDefCount, importCount, genericParamCount, typeArgCount, false, true));
    }

    private static byte[] WithOffset(byte[] section, int index, uint value)
    {
        var copy = section.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(copy.AsSpan(index * sizeof(uint)), value);
        return copy;
    }

    private static void AddInvalid(List<TableCase> cases, byte[][] rows, int recordCount,
        int typeDefCount, int importCount, int genericParamCount, int typeArgCount)
    {
        var section = TrRmVariableTableWriter.EncodeBlobs(
            [.. rows.Select(static row => (ReadOnlyMemory<byte>)row)]);
        Assert.That(ValidateHosted(section, recordCount, typeDefCount, importCount,
            genericParamCount, typeArgCount), Is.False);
        cases.Add(new TableCase([.. section.Select(static value => (int)value)], recordCount,
            typeDefCount, importCount, genericParamCount, typeArgCount, false, true));
    }

    private static bool ValidateHosted(byte[] section, int recordCount, int typeDefCount,
        int importCount, int genericParamCount, int typeArgCount)
    {
        try
        {
            _ = new TrRmTypeSpecTableReader(section, recordCount,
                typeDefCount, importCount, genericParamCount, typeArgCount);
            return true;
        }
        catch (InvalidDataException)
        {
            return false;
        }
    }

    private static async Task<int> ExecuteAotAsync(BoundProgram program, NativeRuntimePolicy policy)
    {
        var path = Path.Combine(TestArtifactPaths.DirectoryPath, $"typespec-table-validation-{Guid.NewGuid():N}.exe");
        try
        {
            await File.WriteAllBytesAsync(path, NativeEmitter.Emit(program, policy)).ConfigureAwait(true);
            using var process = Process.Start(new ProcessStartInfo(path) {
                UseShellExecute = false,
                CreateNoWindow = true,
            }) ?? throw new InvalidOperationException("Could not start compiled TypeSpec table validator.");
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

    private sealed record TableCase(int[] Section, int RecordCount, int TypeDefCount,
        int ImportCount, int GenericParamCount, int TypeArgCount, bool ExpectedValid, bool HasHostedOracle);
}
