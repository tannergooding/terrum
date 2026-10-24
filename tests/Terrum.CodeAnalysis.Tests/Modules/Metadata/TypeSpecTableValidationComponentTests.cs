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

using Terrum.CodeAnalysis.Tests.Infrastructure;

namespace Terrum.CodeAnalysis.Tests.Modules.Metadata;

public sealed class TypeSpecTableValidationComponentTests
{
    private const string VerificationSource = """
        namespace Terrum.CodeAnalysis;
        int32 Main() {
            bool valid = TryValidateTypeSpecTable(
                Host.Expected[0], Host.Expected[1], Host.Expected[2],
                Host.Expected[3]);
            return valid == (Host.Expected[4] != 0) ? 0 : 1;
        }
        """;

    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
    public async Task TypeSpecTableValidationMatchesHostedStructuralReader(BoundRuntimePolicy policy)
    {
        const int typeDefCount = 1;
        const int importCount = 1;
        const int genericParamCount = 1;
        var cases = new List<TableCase>();

        // This structural pass does not infer formal arity or bound typed arguments.
        ReadOnlyMemory<byte>[] validRows = [
            TrRmTypeSpecTableWriter.Builtin(8),
            TrRmTypeSpecTableWriter.Reference(0x02, 1),
            TrRmTypeSpecTableWriter.Reference(0x03, 1),
            TrRmTypeSpecTableWriter.Reference(0x04, 1),
            TrRmTypeSpecTableWriter.Constructed(2,
                [0xF2000008u, 0x00FF_FFFFu, 0xF3008B08u]),
            new byte[] { 0x0F, 0 },
            TrRmTypeSpecTableWriter.Wrapped(0x06, 1),
            TrRmTypeSpecTableWriter.Wrapped(0x06, 0xF2000008),
            TrRmTypeSpecTableWriter.Wrapped(0x06, 0xF2000000),
            TrRmTypeSpecTableWriter.Wrapped(0x06, 0xF200001A),
            TrRmTypeSpecTableWriter.Wrapped(0x06, 0xF3008100),
            TrRmTypeSpecTableWriter.Wrapped(0x06, 0xF3008200),
            TrRmTypeSpecTableWriter.Wrapped(0x06, 0xF3008300),
            TrRmTypeSpecTableWriter.Wrapped(0x07, 0xF3018008),
            TrRmTypeSpecTableWriter.Wrapped(0x08, 0xF3028B08),
            TrRmTypeSpecTableWriter.Wrapped(0x09, 0xF3028D08),
            TrRmTypeSpecTableWriter.Wrapped(0x0A, 0xF3008C08),
            TrRmTypeSpecTableWriter.Wrapped(0x0B, 0xF3008E19),
            TrRmTypeSpecTableWriter.Wrapped(0x06, 0xF3008F19),
            TrRmTypeSpecTableWriter.IndexedFamily(0x80, 0xF2000008),
            TrRmTypeSpecTableWriter.IndexedFamily(0x84, 0xF3008419),
            TrRmTypeSpecTableWriter.IndexedFamily(0x8A, 0xF3048A08),
            new byte[] { 0x0E, 1, 0x08, 0, 0, 0xF2 },
            TrRmTypeSpecTableWriter.InlineFamily(0x8A, 8, 4),
            TrRmTypeSpecTableWriter.IndexedFamily(0x8A, 24),
            TrRmTypeSpecTableWriter.InlineFamily(0x8B, 8, 2),
            TrRmTypeSpecTableWriter.InlineFamily(0x8D, 8, 2),
        ];
        var validTable = TrRmTypeSpecTableWriter.Encode(validRows,
            typeDefCount, importCount, genericParamCount);
        cases.Add(Valid(validTable, validRows.Length, typeDefCount, importCount,
            genericParamCount));
        cases.Add(Valid(validTable, validRows.Length, 0xFF_FFFF, 0xFF_FFFF,
            0xFF_FFFF));

        var repeatedConstructedRows = TrRmVariableTableWriter.EncodeBlobs([
            TrRmTypeSpecTableWriter.Builtin(8),
            TrRmTypeSpecTableWriter.Reference(0x02, 1),
            TrRmTypeSpecTableWriter.Constructed(2, [0xF2000008u, 0x00FF_FFFFu]),
            TrRmTypeSpecTableWriter.Constructed(2, [0xF2000008u, 0x00FF_FFFFu]),
        ]);
        cases.Add(Valid(repeatedConstructedRows, 4, typeDefCount, importCount,
            genericParamCount));

        var importedConstructedType = TrRmTypeSpecTableWriter.Encode([
            TrRmTypeSpecTableWriter.Reference(0x03, 1),
            TrRmTypeSpecTableWriter.Constructed(1, [0xF2000008u]),
        ], 0, 1, 0);
        cases.Add(Valid(importedConstructedType, 2, 0, 1, 0));

        AddRejectedTableCase(cases, WithOffset(validTable, 0, 1), validRows.Length,
            typeDefCount, importCount, genericParamCount);
        AddRejectedTableCase(cases, WithOffset(validTable, 1, 0), validRows.Length,
            typeDefCount, importCount, genericParamCount);
        AddRejectedTableCase(cases, [.. validTable, 0], validRows.Length,
            typeDefCount, importCount, genericParamCount);

        cases.Add(new TableCase([], 0, typeDefCount, importCount, genericParamCount,
            false, true));
        AddRejectedTableCase(cases, validTable, 0, typeDefCount, importCount,
            genericParamCount);
        AddRejectedTableCase(cases, validTable, 0x1000000, typeDefCount, importCount,
            genericParamCount);
        foreach (var invalidCount in new[] { -1, 0x1000000 })
        {
            AddRejectedTableCase(cases, validTable, validRows.Length, invalidCount,
                importCount, genericParamCount);
            AddRejectedTableCase(cases, validTable, validRows.Length, typeDefCount,
                invalidCount, genericParamCount);
            AddRejectedTableCase(cases, validTable, validRows.Length, typeDefCount,
                importCount, invalidCount);
        }

        AddInvalid(cases, [Array.Empty<byte>()], 1,
            typeDefCount, importCount, genericParamCount);
        AddInvalid(cases, [[1, 2, 8]], 1,
            typeDefCount, importCount, genericParamCount);
        AddInvalid(cases, [[1, 0, 0x1A]], 1,
            typeDefCount, importCount, genericParamCount);
        AddInvalid(cases, [[1, 0, 8, 0]], 1,
            typeDefCount, importCount, genericParamCount);
        AddInvalid(cases, [[1, 0, 0x80]], 1,
            typeDefCount, importCount, genericParamCount);
        AddInvalid(cases, [[1, 0, 0x85, 8]], 1,
            typeDefCount, importCount, genericParamCount);
        AddInvalid(cases, [[1, 0, 0x89, 8]], 1,
            typeDefCount, importCount, genericParamCount);
        AddInvalid(cases, [[1, 0, 0x80, 0x1A]], 1,
            typeDefCount, importCount, genericParamCount);
        AddInvalid(cases, [[1, 8, 8]], 1,
            typeDefCount, importCount, genericParamCount);
        AddInvalid(cases, [[1, 1, 8]], 1,
            typeDefCount, importCount, genericParamCount);
        AddInvalid(cases, [[1, 2, 0x80, 8]], 1,
            typeDefCount, importCount, genericParamCount);
        AddInvalid(cases, [[1, 2, 0x8C, 8]], 1,
            typeDefCount, importCount, genericParamCount);

        AddInvalid(cases, [[2, 0, 1, 0, 0, 0]], 1, 0, importCount, genericParamCount);
        AddInvalid(cases, [[2, 0, 0, 0, 0, 0]], 1, typeDefCount, importCount, genericParamCount);
        AddInvalid(cases, [[2, 0, 2, 0, 0, 0]], 1, typeDefCount, importCount, genericParamCount);
        AddInvalid(cases, [[2, 0, 1]], 1, typeDefCount, importCount, genericParamCount);
        AddInvalid(cases, [[5, 0, 1, 0, 0, 0, 1, 0, 0]], 1,
            typeDefCount, importCount, genericParamCount);
        AddInvalid(cases, [[5, 0, 1, 0, 0, 0, 1, 0, 0, 0, 0]], 1,
            typeDefCount, importCount, genericParamCount);
        AddInvalid(cases, [[5, 0, 1, 0, 0, 0]], 1,
            typeDefCount, importCount, genericParamCount);
        AddInvalid(cases, [
            [1, 0, 8],
            [5, 0, 1, 0, 0, 0, 1, 0, 0, 0],
        ], 2, typeDefCount, importCount, genericParamCount);
        AddInvalid(cases, [
            [5, 0, 2, 0, 0, 0, 1, 0, 0, 0],
            [2, 0, 1, 0, 0, 0],
        ], 2, typeDefCount, importCount, genericParamCount);
        AddInvalid(cases, [
            [2, 0, 1, 0, 0, 0],
            [5, 0, 1, 0, 0, 0, 0, 0, 0, 0],
        ], 2, typeDefCount, importCount, genericParamCount);
        foreach (var invalidArgument in new uint[] {
            0xF2010008,
            0xF200001B,
            0xF3008508,
            0xF3008B1A,
            0xF3088B08,
            0xF3028C08,
            0xF3048A08,
            0xF0000001,
            0xF1000001,
            0xF4000001,
            0xFF000001,
            0x01000001,
        })
        {
            AddInvalidConstructedArgument(cases, invalidArgument, typeDefCount,
                importCount, genericParamCount);
        }

        AddInvalid(cases, [[6, 0, 0, 0, 0, 0]], 1,
            typeDefCount, importCount, genericParamCount);
        AddInvalid(cases, [[6, 0, 8, 0, 1, 0xF2]], 1,
            typeDefCount, importCount, genericParamCount);
        AddInvalid(cases, [[6, 0, 0x1B, 0, 0, 0xF2]], 1,
            typeDefCount, importCount, genericParamCount);
        AddInvalid(cases, [[6, 0, 0x08, 0x8B, 0x08, 0xF3]], 1,
            typeDefCount, importCount, genericParamCount);
        AddInvalid(cases, [[6, 0, 0, 0x85, 0, 0xF3]], 1,
            typeDefCount, importCount, genericParamCount);
        AddInvalid(cases, [[6, 0, 0, 0x89, 0, 0xF3]], 1,
            typeDefCount, importCount, genericParamCount);
        AddInvalid(cases, [[6, 0, 0x1A, 0x8B, 0, 0xF3]], 1,
            typeDefCount, importCount, genericParamCount);
        AddInvalid(cases, [[6, 0, 0x08, 0x8B, 8, 0xF3]], 1,
            typeDefCount, importCount, genericParamCount);
        AddInvalid(cases, [[6, 0, 0x08, 0x8C, 2, 0xF3]], 1,
            typeDefCount, importCount, genericParamCount);

        AddInvalid(cases, [[7, 0, 0x08, 0x80, 4, 0xF3]], 1,
            typeDefCount, importCount, genericParamCount);
        AddInvalid(cases, [
            [1, 0, 8],
            [7, 0, 1, 0, 0, 0],
        ], 2, typeDefCount, importCount, genericParamCount);
        AddInvalid(cases, [
            [7, 0, 2, 0, 0, 0],
            [0x0F, 0],
        ], 2, typeDefCount, importCount, genericParamCount);
        AddInvalid(cases, [[0x0C, 0, 0x8B, 1, 0, 0, 0]], 1,
            typeDefCount, importCount, genericParamCount);
        AddInvalid(cases, [[0x0C, 0, 0x80, 0x08, 0x80, 4, 0xF3]], 1,
            typeDefCount, importCount, genericParamCount);
        AddInvalid(cases, [[0x0C, 0, 0x80, 1, 0, 0, 0]], 1,
            typeDefCount, importCount, genericParamCount);

        AddInvalid(cases, [[0x0F, 0, 0]], 1,
            typeDefCount, importCount, genericParamCount);
        AddInvalid(cases, [[0x0F, 1]], 1,
            typeDefCount, importCount, genericParamCount);
        AddInvalid(cases, [[0x0E, 2, 8, 0, 0, 0]], 1,
            typeDefCount, importCount, genericParamCount);
        AddInvalid(cases, [[0x0E, 0, 8, 0x8A, 4, 0xF3]], 1,
            typeDefCount, importCount, genericParamCount);
        AddInvalid(cases, [[0x0E, 0, 0, 0, 0, 0]], 1,
            typeDefCount, importCount, genericParamCount);

        var invalidByteWords = validTable.Select(static value => (int)value).ToArray();
        invalidByteWords[(validRows.Length + 1) * sizeof(uint) + 1] = 256;
        cases.Add(new TableCase(invalidByteWords, validRows.Length, typeDefCount,
            importCount, genericParamCount, false, false));

        var root = RepositoryRoot();
        var componentPaths = new[] {
            "tests/Terrum.CodeAnalysis.Terrum.Tests/Modules/TypeSpecTableValidation.terrum",
        };
        var components = new List<SyntaxTree>();
        foreach (var name in componentPaths)
        {
            var path = TerrumProjectPaths.RepositoryFile(root, name);
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
                    testCase.TypeDefCount, testCase.ImportCount, testCase.GenericParamCount);
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
        int importCount, int genericParamCount)
    {
        var valid = ValidateHosted(section, recordCount, typeDefCount, importCount,
            genericParamCount);
        Assert.That(valid, Is.True);
        return new TableCase([.. section.Select(static value => (int)value)], recordCount,
            typeDefCount, importCount, genericParamCount, true, true);
    }

    private static void AddRejectedTableCase(List<TableCase> cases, byte[] section, int recordCount,
        int typeDefCount, int importCount, int genericParamCount)
    {
        Assert.That(ValidateHosted(section, recordCount, typeDefCount, importCount,
            genericParamCount), Is.False);
        cases.Add(new TableCase([.. section.Select(static value => (int)value)], recordCount,
            typeDefCount, importCount, genericParamCount, false, true));
    }

    private static byte[] WithOffset(byte[] section, int index, uint value)
    {
        var copy = section.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(copy.AsSpan(index * sizeof(uint)), value);
        return copy;
    }

    private static void AddInvalid(List<TableCase> cases, byte[][] rows, int recordCount,
        int typeDefCount, int importCount, int genericParamCount)
    {
        var section = TrRmVariableTableWriter.EncodeBlobs(
            [.. rows.Select(static row => (ReadOnlyMemory<byte>)row)]);
        Assert.That(ValidateHosted(section, recordCount, typeDefCount, importCount,
            genericParamCount), Is.False);
        cases.Add(new TableCase([.. section.Select(static value => (int)value)], recordCount,
            typeDefCount, importCount, genericParamCount, false, true));
    }

    private static void AddInvalidConstructedArgument(List<TableCase> cases, uint argument,
        int typeDefCount, int importCount, int genericParamCount)
    {
        var constructedRow = new byte[10];
        constructedRow[0] = 0x05;
        constructedRow[2] = 1;
        BinaryPrimitives.WriteUInt32LittleEndian(constructedRow.AsSpan(6), argument);
        AddInvalid(cases, [[2, 0, 1, 0, 0, 0], constructedRow], 2,
            typeDefCount, importCount, genericParamCount);
    }

    private static bool ValidateHosted(byte[] section, int recordCount, int typeDefCount,
        int importCount, int genericParamCount)
    {
        try
        {
            _ = new TrRmTypeSpecTableReader(section, recordCount,
                typeDefCount, importCount, genericParamCount);
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
        int ImportCount, int GenericParamCount, bool ExpectedValid, bool HasHostedOracle);
}
