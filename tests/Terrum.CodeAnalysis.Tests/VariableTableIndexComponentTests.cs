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

public sealed class VariableTableIndexComponentTests
{
    private const string VerificationSource = """
        namespace Terrum.CodeAnalysis;
        int32 Main() {
            int32 caseCount = Host.Expected.Length / 7;
            for (mut int32 index = 0; index < caseCount; index = index + 1) {
                int32 record = index * 7;
                int32 count = Host.Expected[record];
                int32 minimumRowBytes = Host.Expected[record + 1];
                int32 rowAlignment = Host.Expected[record + 2];
                int32 row = Host.Expected[record + 3];
                bool expectedSuccess = Host.Expected[record + 4] != 0;
                if (expectedSuccess) {
                    if (!TryGetVariableTableRowExtent(count, minimumRowBytes, rowAlignment, row,
                        init var rowStart, init var rowLength)) {
                        return 1;
                    }
                    if (rowStart != Host.Expected[record + 5]
                        || rowLength != Host.Expected[record + 6]) {
                        return 2;
                    }
                } else if (TryGetVariableTableRowExtent(count, minimumRowBytes, rowAlignment, row,
                    init var rejectedStart, init var rejectedLength)) {
                    return 3;
                }
            }
            return 0;
        }
        """;

    [Theory]
    [InlineData(BoundRuntimePolicy.FailFast)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions)]
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
        cases.Add(Rejected(blobs, 4, 0, 1, 0));
        cases.Add(Rejected(blobs, 4, 0, 1, 5));
        cases.Add(Rejected(blobs, 0, 0, 1, 1));
        cases.Add(Rejected(blobs, 0x1000000, 0, 1, 1));
        cases.Add(Rejected(blobs, 4, -1, 1, 1));
        cases.Add(Rejected(blobs, 4, 0, 2, 1));

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
        cases.Add(Rejected(bodies, 2, 1, 1, 0));

        var emptyBody = bodies.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(emptyBody.AsSpan(4), 0);
        cases.Add(Rejected(emptyBody, 2, 1, 1, 1));

        var root = RepositoryRoot();
        var componentPath = Path.Combine(root, "self-host", "VariableTableIndex.terrum");
        var component = SyntaxTree.Parse(await File.ReadAllTextAsync(componentPath).ConfigureAwait(true), componentPath);
        Assert.False(component.HasErrors, string.Join(Environment.NewLine, component.Diagnostics));
        var entry = SyntaxTree.Parse(VerificationSource, "variable-table-index-component.terrum");

        foreach (var testCase in cases)
        {
            var compilation = Compilation.Create([component, entry],
            [
                new HostBuffer("Rows", PrimitiveType.Int32, [.. testCase.Section], false),
                new HostBuffer("Expected", PrimitiveType.Int32, [.. testCase.Expected.SelectMany(static item => item)], false),
            ], policy);
            Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
            var program = Assert.IsType<BoundProgram>(compilation.Program);
            Assert.Equal(0, NativeJit.Execute(program, (NativeRuntimePolicy)policy));
            Assert.Equal(0, await ExecuteAotAsync(program, (NativeRuntimePolicy)policy).ConfigureAwait(true));
        }
    }

    private static TableCase Success(byte[] section, int count, int minimumRowBytes, int rowAlignment, int row)
    {
        var reader = new TrRmVariableTableIndex(section, count, minimumRowBytes, rowAlignment);
        var start = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(section.AsSpan((row - 1) * sizeof(uint))));
        var length = reader.GetRow(row).Length;
        return new TableCase([.. section.Select(static value => (int)value)],
            [[count, minimumRowBytes, rowAlignment, row, 1, start, length]]);
    }

    private static TableCase Rejected(byte[] section, int count, int minimumRowBytes, int rowAlignment, int row)
        => Rejected([.. section.Select(static value => (int)value)], count, minimumRowBytes, rowAlignment, row);

    private static TableCase Rejected(int[] section, int count, int minimumRowBytes, int rowAlignment, int row)
        => new(section, [[count, minimumRowBytes, rowAlignment, row, 0, -1, -1]]);

    private static async Task<int> ExecuteAotAsync(BoundProgram program, NativeRuntimePolicy policy)
    {
        var path = Path.Combine(Path.GetTempPath(), $"variable-table-index-{Guid.NewGuid():N}.exe");
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
                File.Delete(path);
            }
        }
    }

    private static string RepositoryRoot() =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));

    private sealed record TableCase(int[] Section, int[][] Expected);
}
