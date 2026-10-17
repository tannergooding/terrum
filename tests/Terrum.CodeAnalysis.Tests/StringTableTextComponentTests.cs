using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Terrum.CodeAnalysis.Text;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests;

public sealed class StringTableTextComponentTests
{
    private const string BatchVerificationSource = """
        namespace Terrum.CodeAnalysis;
        int32 Main() {
            int32 recordCount = Host.Expected[0];
            if (!TryValidateStringTableTextIndex(recordCount)) {
                return 1;
            }
            mut int32 expectedRecord = 1;
            for (mut int32 row = 1; row <= recordCount; row = row + 1) {
                if (!TryGetValidatedStringTableTextRow(recordCount, row,
                    init var rowStart, init var rowLength)) {
                    return 2;
                }

                int32 expectedCount = Host.Expected[expectedRecord];
                int32 scalarStart = Host.Expected[expectedRecord + 1];
                if (!StringRowTextCursor.TryCreateName(rowStart, rowLength, init var created)) {
                    return 3;
                }
                mut StringRowTextCursor cursor = move created;
                mut int32 scalarIndex = 0;
                while (cursor.TryRead(init var scalar)) {
                    if (scalarIndex >= expectedCount
                        || scalar != Host.Expected[scalarStart + scalarIndex]) {
                        return 4;
                    }
                    scalarIndex = scalarIndex + 1;
                }
                if (scalarIndex != expectedCount) {
                    return 5;
                }
                cursor = _;
                expectedRecord = expectedRecord + 2 + expectedCount;
            }

            init int32 vacantStart;
            init int32 vacantLength;
            if (TryGetValidatedStringTableTextRow(recordCount, 0,
                init vacantStart, init vacantLength)
                || vacantStart is init || vacantLength is init) {
                return 6;
            }
            return 0;
        }
        """;

    private const string VerificationSource = """
        namespace Terrum.CodeAnalysis;
        int32 Main() {
            int32 caseCount = Host.Expected.Length / 8;
            for (mut int32 index = 0; index < caseCount; index = index + 1) {
                int32 record = index * 8;
                int32 recordCount = Host.Expected[record];
                int32 row = Host.Expected[record + 1];
                bool isName = Host.Expected[record + 2] != 0;
                int32 scalarIndex = Host.Expected[record + 3];
                bool expectedCountSuccess = Host.Expected[record + 4] != 0;
                int32 expectedCount = Host.Expected[record + 5];
                bool expectedReadSuccess = Host.Expected[record + 6] != 0;
                int32 expectedScalar = Host.Expected[record + 7];
                if (isName) {
                    if (expectedCountSuccess) {
                        if (!TryGetStringTableNameScalarCount(recordCount, row, init var count)
                            || count != expectedCount) {
                            return 1;
                        }
                    } else {
                        if (TryGetStringTableNameScalarCount(recordCount, row, init var rejectedCount)) {
                            return 2;
                        }
                        if (!TryGetStringTableNameScalarCount(recordCount, 1, init rejectedCount)
                            || rejectedCount != 1) {
                            return 3;
                        }
                    }

                    if (expectedReadSuccess) {
                        if (!TryReadStringTableNameScalarAt(recordCount, row, scalarIndex, init var scalar)
                            || scalar != expectedScalar) {
                            return 4;
                        }
                    } else {
                        if (TryReadStringTableNameScalarAt(recordCount, row, scalarIndex, init var rejectedScalar)) {
                            return 5;
                        }
                        if (!TryReadStringTableNameScalarAt(recordCount, 1, 0, init rejectedScalar)
                            || rejectedScalar != 65) {
                            return 6;
                        }
                    }
                } else {
                    if (expectedCountSuccess) {
                        if (!TryGetStringTableNamespaceScalarCount(recordCount, row, init var count)
                            || count != expectedCount) {
                            return 7;
                        }
                    } else {
                        if (TryGetStringTableNamespaceScalarCount(recordCount, row, init var rejectedCount)) {
                            return 8;
                        }
                        if (!TryGetStringTableNamespaceScalarCount(recordCount, 1, init rejectedCount)
                            || rejectedCount != 1) {
                            return 9;
                        }
                    }

                    if (expectedReadSuccess) {
                        if (!TryReadStringTableNamespaceScalarAt(recordCount, row, scalarIndex, init var scalar)
                            || scalar != expectedScalar) {
                            return 10;
                        }
                    } else {
                        if (TryReadStringTableNamespaceScalarAt(recordCount, row, scalarIndex, init var rejectedScalar)) {
                            return 11;
                        }
                        if (!TryReadStringTableNamespaceScalarAt(recordCount, 1, 0, init rejectedScalar)
                            || rejectedScalar != 65) {
                            return 12;
                        }
                    }
                }
            }
            return 0;
        }
        """;
    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
    public async Task StringTableTextMatchesSelectedHostedRowDecoding(BoundRuntimePolicy policy)
    {
        const int recordCount = 5;
        byte[] table = TrRmStringTableWriter.EncodeTextRows([
            ("A", 0), ("", 1), ("B", 2), ("\U0001F600", 3), ("\u00E9", 1),
        ]);
        var dataStart = (recordCount + 1) * sizeof(uint);

        var thirdRowStart = ReadRowStart(table, 3);
        table[dataStart + thirdRowStart + 4 + 2] = 1;

        var fifthRowStart = ReadRowStart(table, 5);
        table[dataStart + fifthRowStart + 4] = 0xC0;

        var cases = new List<int[]>();
        AddValidRow(cases, table, recordCount, 1, true, "A");
        AddInvalidRow(cases, table, recordCount, 2, true);
        AddValidRow(cases, table, recordCount, 2, false, "");
        AddInvalidRow(cases, table, recordCount, 3, true);
        AddValidRow(cases, table, recordCount, 4, true, "\U0001F600");
        AddInvalidRow(cases, table, recordCount, 5, false);
        AddInvalidRow(cases, table, recordCount, 0, true);
        AddInvalidRow(cases, table, recordCount, recordCount + 1, false);

        var root = RepositoryRoot();
        var componentPaths = new[] {
            "VariableTableIndex.terrum",
            "StringRowLayout.terrum",
            "StringRowText.terrum",
            "StringTableText.terrum",
        };
        var components = new List<SyntaxTree>();
        foreach (var name in componentPaths)
        {
            var path = SelfHostPaths.ResolveFile(root, name);
            var tree = SyntaxTree.Parse(await File.ReadAllTextAsync(path).ConfigureAwait(true), path);
            Assert.That(tree.HasErrors, Is.False, string.Join(Environment.NewLine, tree.Diagnostics));
            components.Add(tree);
        }
        var entry = SyntaxTree.Parse(VerificationSource, "string-table-text-component.terrum");
        components.Add(entry);

        var compilation = Compilation.Create(components,
        [
            new HostBuffer("Rows", PrimitiveType.Int32, [.. table.Select(static value => (int)value)], false),
            new HostBuffer("Expected", PrimitiveType.Int32, [.. cases.SelectMany(static item => item)], false),
        ], policy);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual0 = compilation.Program; Assert.That(__nunitActual0, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual0!; }))();
        Assert.That(NativeJit.Execute(program, (NativeRuntimePolicy)policy), Is.EqualTo(0));
        Assert.That(await ExecuteAotAsync(program, (NativeRuntimePolicy)policy).ConfigureAwait(true), Is.EqualTo(0));
    }
    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
    public async Task ValidatedStringTableIndexSupportsBatchTextReadsAsync(BoundRuntimePolicy policy)
    {
        var rows = Enumerable.Range(1, 512)
            .Select(static index => (Text: $"m-row-{index}", Encoding: index % 4))
            .ToArray();
        var table = TrRmStringTableWriter.EncodeTextRows(rows);
        var expected = new List<int> { rows.Length };
        foreach (var row in rows)
        {
            var scalars = row.Text.EnumerateRunes().Select(static rune => rune.Value).ToArray();
            expected.Add(scalars.Length);
            expected.Add(expected.Count + 1);
            expected.AddRange(scalars);
        }
        var program = await CompileAsync(table, [.. expected], BatchVerificationSource, policy)
            .ConfigureAwait(true);

        Assert.That(NativeJit.Execute(program, (NativeRuntimePolicy)policy), Is.EqualTo(0));
        Assert.That(await ExecuteAotAsync(program, (NativeRuntimePolicy)policy).ConfigureAwait(true), Is.EqualTo(0));
    }

    private static void AddValidRow(List<int[]> cases, byte[] table, int recordCount, int row, bool isName,
        string expectedText)
    {
        var decoded = isName
            ? DecodeName(table, recordCount, row)
            : DecodeNamespace(table, recordCount, row);
        Assert.That(decoded, Is.EqualTo(expectedText));
        var scalars = new List<int>();
        foreach (var rune in decoded.EnumerateRunes())
        {
            scalars.Add(rune.Value);
        }

        cases.Add([recordCount, row, isName ? 1 : 0, -1, 1, scalars.Count, 0, -1]);
        for (var index = 0; index < scalars.Count; index++)
        {
            cases.Add([recordCount, row, isName ? 1 : 0, index, 1, scalars.Count, 1, scalars[index]]);
        }
        cases.Add([recordCount, row, isName ? 1 : 0, scalars.Count, 1, scalars.Count, 0, -1]);
        cases.Add([recordCount, row, isName ? 1 : 0, -1, 1, scalars.Count, 0, -1]);
    }

    private static void AddInvalidRow(List<int[]> cases, byte[] table, int recordCount, int row, bool isName)
    {
        Assert.Throws<InvalidDataException>(() => {
            if (isName)
            {
                DecodeName(table, recordCount, row);
            }
            else
            {
                DecodeNamespace(table, recordCount, row);
            }
        });

        cases.Add([recordCount, row, isName ? 1 : 0, -1, 0, -1, 0, -1]);
        cases.Add([recordCount, row, isName ? 1 : 0, 0, 0, -1, 0, -1]);
    }

    private static string DecodeName(byte[] table, int recordCount, int row)
        => new TrRmStringTableReader(table, recordCount).DecodeName(row);

    private static string DecodeNamespace(byte[] table, int recordCount, int row)
        => new TrRmStringTableReader(table, recordCount).DecodeNamespace(row);

    private static int ReadRowStart(byte[] table, int row)
        => checked((int)BinaryPrimitives.ReadUInt32LittleEndian(table.AsSpan((row - 1) * sizeof(uint))));

    private static async Task<BoundProgram> CompileAsync(byte[] table, int[] expected,
        string source, BoundRuntimePolicy policy)
    {
        var root = RepositoryRoot();
        var componentPaths = new[] {
            "VariableTableIndex.terrum",
            "StringRowLayout.terrum",
            "StringRowText.terrum",
            "StringRowTextCursor.terrum",
            "StringTableText.terrum",
        };
        var components = new List<SyntaxTree>();
        foreach (var name in componentPaths)
        {
            var path = SelfHostPaths.ResolveFile(root, name);
            var tree = SyntaxTree.Parse(await File.ReadAllTextAsync(path).ConfigureAwait(true), path);
            Assert.That(tree.HasErrors, Is.False, string.Join(Environment.NewLine, tree.Diagnostics));
            components.Add(tree);
        }
        components.Add(SyntaxTree.Parse(source, "string-table-text-batch-component.terrum"));

        var compilation = Compilation.Create(components,
        [
            new HostBuffer("Rows", PrimitiveType.Int32, [.. table.Select(static value => (int)value)], false),
            new HostBuffer("Expected", PrimitiveType.Int32, [.. expected], false),
        ], policy);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        return ((global::System.Func<BoundProgram>)(() => { var __nunitActual1 = compilation.Program; Assert.That(__nunitActual1, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual1!; }))();
    }

    private static async Task<int> ExecuteAotAsync(BoundProgram program, NativeRuntimePolicy policy)
    {
        var path = Path.Combine(TestArtifactPaths.DirectoryPath, $"string-table-text-{Guid.NewGuid():N}.exe");
        try
        {
            await File.WriteAllBytesAsync(path, NativeEmitter.Emit(program, policy)).ConfigureAwait(true);
            using var process = Process.Start(new ProcessStartInfo(path) {
                UseShellExecute = false,
                CreateNoWindow = true,
            }) ?? throw new InvalidOperationException("Could not start compiled String table text component.");
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(true);
            return process.ExitCode;
        }
        finally
        {
            await TestFileCleanup.DeleteWithRetryAsync(path).ConfigureAwait(true);
        }
    }

    private static string RepositoryRoot() =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
}
