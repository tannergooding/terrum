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

namespace Terrum.CodeAnalysis.Tests.Modules;

public sealed class VariableTableIndexComponentTests
{
    private const string VerificationSource = """
        namespace Terrum.CodeAnalysis;
        int32 Main() {
            int32 caseCount = Host.Expected.Length / 8;
            for (mut int32 index = 0; index < caseCount; index = index + 1) {
                int32 record = index * 8;
                int32 count = Host.Expected[record];
                int32 minimumRowBytes = Host.Expected[record + 1];
                int32 rowAlignment = Host.Expected[record + 2];
                int32 row = Host.Expected[record + 3];
                bool expectedSuccess = Host.Expected[record + 4] != 0;
                bool validIndex = TryValidateVariableTableIndex(
                    count, minimumRowBytes, rowAlignment);
                if (validIndex != (Host.Expected[record + 7] != 0)) {
                    return 8;
                }
                if (validIndex) {
                    for (mut int32 indexedRow = 1; indexedRow <= count; indexedRow = indexedRow + 1) {
                        if (!TryGetValidatedVariableTableRowExtent(
                            count, minimumRowBytes, rowAlignment, indexedRow,
                            init var indexedRowStart, init var indexedRowLength)) {
                            return 7;
                        }
                        if (!TryGetVariableTableRowExtent(
                            count, minimumRowBytes, rowAlignment, indexedRow,
                            init var checkedRowStart, init var checkedRowLength)
                            || indexedRowStart != checkedRowStart
                            || indexedRowLength != checkedRowLength) {
                            return 9;
                        }
                    }
                }
                if (expectedSuccess) {
                    if (!validIndex) {
                        return 4;
                    }
                    if (!TryGetVariableTableRowExtent(count, minimumRowBytes, rowAlignment, row,
                        init var rowStart, init var rowLength)) {
                        return 1;
                    }
                    if (rowStart != Host.Expected[record + 5]
                        || rowLength != Host.Expected[record + 6]) {
                        return 2;
                    }
                    if (!TryGetValidatedVariableTableRowExtent(
                        count, minimumRowBytes, rowAlignment, row,
                        init var validatedRowStart, init var validatedRowLength)
                        || validatedRowStart != rowStart
                        || validatedRowLength != rowLength) {
                        return 5;
                    }
                } else if (TryGetVariableTableRowExtent(count, minimumRowBytes, rowAlignment, row,
                    init var rejectedStart, init var rejectedLength)) {
                    return 3;
                } else if (validIndex
                    && TryGetValidatedVariableTableRowExtent(
                        count, minimumRowBytes, rowAlignment, row,
                        init var unusedStart, init var unusedLength)) {
                    return 6;
                }
            }
            return 0;
        }
        """;
    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
    public async Task VariableTableIndexMatchesHostedBlobAndBodyReaders(BoundRuntimePolicy policy)
    {
        var cases = new List<TableCase>();
        byte[] blobs = TrRmVariableTableWriter.EncodeBlobs([
            ReadOnlyMemory<byte>.Empty, new byte[] { 0, 255 }, ReadOnlyMemory<byte>.Empty, new byte[] { 7 },
        ]);
        for (var row = 1; row <= 4; row++)
        {
            cases.Add(Success(blobs, 4, 0, 1, row));
        }
        cases.Add(Rejected(blobs, 4, 0, 1, 0, validIndex: true));
        cases.Add(Rejected(blobs, 4, 0, 1, 5, validIndex: true));
        cases.Add(Rejected(blobs, 0, 0, 1, 1));
        cases.Add(Rejected(blobs, 0x1000000, 0, 1, 1));
        cases.Add(Rejected(blobs, 4, -1, 1, 1));
        cases.Add(Rejected(blobs, 4, 0, 2, 1));
        cases.Add(Success(TrRmVariableTableWriter.EncodeBlobs(
            Enumerable.Repeat<ReadOnlyMemory<byte>>(new byte[] { 7 }, 256).ToArray()),
            256, 0, 1, 256));

        var truncatedIndex = blobs[..^sizeof(uint)];
        cases.Add(Rejected(truncatedIndex, 4, 0, 1, 1));

        var nonzeroFirstOffset = blobs.ToArray();
        nonzeroFirstOffset[0] = 1;
        cases.Add(Rejected(nonzeroFirstOffset, 4, 0, 1, 1));

        var descendingOffsets = blobs.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(descendingOffsets.AsSpan(4), 2);
        BinaryPrimitives.WriteUInt32LittleEndian(descendingOffsets.AsSpan(8), 1);
        cases.Add(Rejected(descendingOffsets, 4, 0, 1, 1));

        var pastEnd = blobs.ToArray();
        var finalOffsetIndex = 4 * sizeof(uint);
        var finalOffset = BinaryPrimitives.ReadUInt32LittleEndian(pastEnd.AsSpan(finalOffsetIndex));
        BinaryPrimitives.WriteUInt32LittleEndian(pastEnd.AsSpan(finalOffsetIndex), finalOffset + 1);
        cases.Add(Rejected(pastEnd, 4, 0, 1, 1));

        var invalidByte = blobs.Select(static value => (int)value).ToArray();
        invalidByte[0] = 256;
        cases.Add(Rejected(invalidByte, 4, 0, 1, 1));

        byte[] bodies = TrRmVariableTableWriter.EncodeBodies([
            new byte[] { 0xFF }, new byte[] { 1, 2 },
        ]);
        for (var row = 1; row <= 2; row++)
        {
            cases.Add(Success(bodies, 2, 1, 1, row));
        }
        cases.Add(Rejected(bodies, 2, 1, 1, 0, validIndex: true));

        var emptyBody = bodies.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(emptyBody.AsSpan(4), 0);
        cases.Add(Rejected(emptyBody, 2, 1, 1, 1));

        var root = RepositoryRoot();
        var componentPath = TerrumProjectPaths.RepositoryFile(root, "sources/Terrum.CodeAnalysis.Terrum/Modules/VariableTableIndex.terrum");
        var component = SyntaxTree.Parse(await File.ReadAllTextAsync(componentPath).ConfigureAwait(true), componentPath);
        Assert.That(component.HasErrors, Is.False, string.Join(Environment.NewLine, component.Diagnostics));
        var entry = SyntaxTree.Parse(VerificationSource, "variable-table-index-component.terrum");

        foreach (var testCase in cases)
        {
            var compilation = Compilation.Create([component, entry],
            [
                new HostBuffer("Rows", PrimitiveType.Int32, [.. testCase.Section], false),
                new HostBuffer("Expected", PrimitiveType.Int32, [.. testCase.Expected.SelectMany(static item => item)], false),
            ], policy);
            Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
            var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual0 = compilation.Program; Assert.That(__nunitActual0, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual0!; }))();
            Assert.That(NativeJit.Execute(program, (NativeRuntimePolicy)policy), Is.EqualTo(0));
            Assert.That(await ExecuteAotAsync(program, (NativeRuntimePolicy)policy).ConfigureAwait(true), Is.EqualTo(0));
        }
    }

    private static TableCase Success(byte[] section, int count, int minimumRowBytes, int rowAlignment, int row)
    {
        var reader = new TrRmVariableTableIndex(section, count, minimumRowBytes, rowAlignment);
        var start = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(section.AsSpan((row - 1) * sizeof(uint))));
        var length = reader.GetRow(row).Length;
        return new TableCase([.. section.Select(static value => (int)value)],
            [[count, minimumRowBytes, rowAlignment, row, 1, start, length, 1]]);
    }

    private static TableCase Rejected(byte[] section, int count, int minimumRowBytes, int rowAlignment,
        int row, bool validIndex = false) =>
        Rejected([.. section.Select(static value => (int)value)], count, minimumRowBytes,
            rowAlignment, row, validIndex);

    private static TableCase Rejected(int[] section, int count, int minimumRowBytes, int rowAlignment,
        int row, bool validIndex = false) =>
        new(section, [[count, minimumRowBytes, rowAlignment, row, 0, -1, -1, validIndex ? 1 : 0]]);

    private static async Task<int> ExecuteAotAsync(BoundProgram program, NativeRuntimePolicy policy)
    {
        var path = Path.Combine(TestArtifactPaths.DirectoryPath, $"variable-table-index-{Guid.NewGuid():N}.exe");
        try
        {
            await File.WriteAllBytesAsync(path, NativeEmitter.Emit(program, policy)).ConfigureAwait(true);
            using var process = Process.Start(new ProcessStartInfo(path) {
                UseShellExecute = false,
                CreateNoWindow = true,
            }) ?? throw new InvalidOperationException("Could not start compiled variable-table index component.");
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

    private sealed record TableCase(int[] Section, int[][] Expected);
}
