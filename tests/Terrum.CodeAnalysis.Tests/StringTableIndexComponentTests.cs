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

public sealed class StringTableIndexComponentTests
{
    private const string VerificationSource = """
        namespace Terrum.CodeAnalysis;
        int32 Main() {
            int32 caseCount = Host.Expected.Length / 7;
            for (mut int32 index = 0; index < caseCount; index = index + 1) {
                int32 record = index * 7;
                int32 count = Host.Expected[record];
                int32 row = Host.Expected[record + 1];
                bool expectedSuccess = Host.Expected[record + 2] != 0;
                if (expectedSuccess) {
                    if (!TryGetStringTableRowLayout(count, row,
                        init var rowStart, init var rowLength,
                        init var encoding, init var contentByteLength)) {
                        return 1;
                    }
                    if (rowStart != Host.Expected[record + 3]
                        || rowLength != Host.Expected[record + 4]
                        || encoding != Host.Expected[record + 5]
                        || contentByteLength != Host.Expected[record + 6]) {
                        return 2;
                    }
                } else if (TryGetStringTableRowLayout(count, row,
                    init var rejectedStart, init var rejectedLength,
                    init var rejectedEncoding, init var rejectedContentLength)) {
                    return 3;
                }
            }
            return 0;
        }
        """;

    [Theory]
    [InlineData(BoundRuntimePolicy.FailFast)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions)]
    public async Task StringTableIndexMatchesHostedVariableRows(BoundRuntimePolicy policy)
    {
        byte[] valid = TrRmStringTableWriter.EncodeTextRows([
            ("A", 0), ("\u00E9", 1), ("\U0001F600", 2), ("Z", 3), ("", 1),
        ]);
        const int recordCount = 5;
        var validIndex = new TrRmVariableTableIndex(valid, recordCount, 8, 4);
        var validCases = new List<TableCase>();
        for (var row = 1; row <= recordCount; row++)
        {
            var rowBytes = validIndex.GetRow(row);
            Assert.Equal(TrRmStringRowReader.DecodeNamespace(rowBytes),
                row switch {
                    1 => "A",
                    2 => "\u00E9",
                    3 => "\U0001F600",
                    4 => "Z",
                    _ => "",
                });
            var rowStart = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(valid.AsSpan((row - 1) * 4)));
            var width = rowBytes[0] switch {
                0 or 1 => 1,
                2 => 2,
                3 => 4,
                _ => throw new InvalidDataException("The hosted writer emitted an unassigned encoding."),
            };
            var units = rowBytes[1] | (rowBytes[2] << 8) | (rowBytes[3] << 16);
            validCases.Add(new TableCase([.. valid.Select(static value => (int)value)],
                [[recordCount, row, 1, rowStart, rowBytes.Length, rowBytes[0], units * width]]));
        }
        var validWords = valid.Select(static value => (int)value).ToArray();
        validCases.Add(new TableCase(validWords,
        [
            [recordCount, 0, 0, -1, -1, -1, -1],
            [recordCount, recordCount + 1, 0, -1, -1, -1, -1],
        ]));
        validCases.Add(new TableCase(validWords, [[0, 1, 0, -1, -1, -1, -1]]));
        validCases.Add(new TableCase(validWords, [[-1, 1, 0, -1, -1, -1, -1]]));
        validCases.Add(new TableCase(validWords, [[0x1000000, 1, 0, -1, -1, -1, -1]]));

        var malformedText = valid.ToArray();
        var secondRowStart = BinaryPrimitives.ReadUInt32LittleEndian(malformedText.AsSpan(4));
        var dataStart = (recordCount + 1) * sizeof(uint);
        malformedText[dataStart + checked((int)secondRowStart) + 4] = 0xC0;
        Assert.Throws<InvalidDataException>(() =>
            TrRmStringRowReader.DecodeNamespace(new TrRmVariableTableIndex(
                malformedText, recordCount, 8, 4).GetRow(2)));
        var malformedTextStart = checked((int)secondRowStart);
        validCases.Add(new TableCase([.. malformedText.Select(static value => (int)value)],
        [
            [recordCount, 2, 1, malformedTextStart,
                checked((int)(BinaryPrimitives.ReadUInt32LittleEndian(malformedText.AsSpan(8)) - secondRowStart)),
                1, 2],
        ]));

        var badFirstOffset = valid.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(badFirstOffset, 1);
        validCases.Add(InvalidTable(badFirstOffset, recordCount));

        var tooShortFirstRow = valid.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(tooShortFirstRow.AsSpan(4), 4);
        validCases.Add(InvalidTable(tooShortFirstRow, recordCount));

        var decreasingOffsets = valid.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(decreasingOffsets.AsSpan(8), 4);
        validCases.Add(InvalidTable(decreasingOffsets, recordCount));

        var unalignedOffset = valid.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(unalignedOffset.AsSpan(4), 9);
        validCases.Add(InvalidTable(unalignedOffset, recordCount));

        var wrongFinalOffset = valid.ToArray();
        var finalOffsetIndex = recordCount * sizeof(uint);
        var finalOffset = BinaryPrimitives.ReadUInt32LittleEndian(wrongFinalOffset.AsSpan(finalOffsetIndex));
        BinaryPrimitives.WriteUInt32LittleEndian(wrongFinalOffset.AsSpan(finalOffsetIndex), finalOffset - 4);
        validCases.Add(InvalidTable(wrongFinalOffset, recordCount));

        var oversizedFinalOffset = valid.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(oversizedFinalOffset.AsSpan(finalOffsetIndex), finalOffset + 4);
        validCases.Add(InvalidTable(oversizedFinalOffset, recordCount));

        validCases.Add(InvalidTable(valid[..^sizeof(uint)], recordCount));

        var invalidSecondRowFlags = valid.ToArray();
        invalidSecondRowFlags[dataStart + checked((int)secondRowStart)] = 4;
        validCases.Add(InvalidTable(invalidSecondRowFlags, recordCount));

        var nonzeroSecondRowTerminator = valid.ToArray();
        nonzeroSecondRowTerminator[dataStart + checked((int)secondRowStart) + 6] = 1;
        validCases.Add(InvalidTable(nonzeroSecondRowTerminator, recordCount));

        var nonzeroSecondRowPadding = valid.ToArray();
        nonzeroSecondRowPadding[dataStart + checked((int)secondRowStart) + 7] = 1;
        validCases.Add(InvalidTable(nonzeroSecondRowPadding, recordCount));

        var invalidByte = valid.Select(static value => (int)value).ToArray();
        invalidByte[0] = 256;
        validCases.Add(InvalidTable(invalidByte, recordCount));

        var root = RepositoryRoot();
        var componentPath = Path.Combine(root, "self-host", "StringRowLayout.terrum");
        var layout = SyntaxTree.Parse(await File.ReadAllTextAsync(componentPath).ConfigureAwait(true), componentPath);
        Assert.False(layout.HasErrors, string.Join(Environment.NewLine, layout.Diagnostics));
        componentPath = Path.Combine(root, "self-host", "StringTableIndex.terrum");
        var indexTree = SyntaxTree.Parse(await File.ReadAllTextAsync(componentPath).ConfigureAwait(true), componentPath);
        Assert.False(indexTree.HasErrors, string.Join(Environment.NewLine, indexTree.Diagnostics));
        var entry = SyntaxTree.Parse(VerificationSource, "string-table-index-component.terrum");

        foreach (var testCase in validCases)
        {
            var compilation = Compilation.Create([layout, indexTree, entry],
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

    private static TableCase InvalidTable(int[] section, int recordCount)
        => new(section, [[recordCount, 1, 0, -1, -1, -1, -1]]);

    private static TableCase InvalidTable(byte[] section, int recordCount)
        => InvalidTable([.. section.Select(static value => (int)value)], recordCount);

    private static async Task<int> ExecuteAotAsync(BoundProgram program, NativeRuntimePolicy policy)
    {
        var path = Path.Combine(Path.GetTempPath(), $"string-table-index-{Guid.NewGuid():N}.exe");
        try
        {
            await File.WriteAllBytesAsync(path, NativeEmitter.Emit(program, policy)).ConfigureAwait(true);
            using var process = Process.Start(new ProcessStartInfo(path) {
                UseShellExecute = false,
                CreateNoWindow = true,
            }) ?? throw new InvalidOperationException("Could not start compiled String table index component.");
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
