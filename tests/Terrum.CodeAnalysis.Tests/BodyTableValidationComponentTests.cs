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

public sealed class BodyTableValidationComponentTests
{
    private const string VerificationSource = """
        namespace Terrum.CodeAnalysis;
        int32 Main() {
            if (!TryGetBodyTableRowExtent(Host.Expected[0], Host.Expected[1],
                init var bodyStart, init var bodyLength)) {
                return Host.Expected[2] == 0 ? 0 : 1;
            }
            if (Host.Expected[2] == 0) {
                return 2;
            }
            if (bodyStart != Host.Expected[3] || bodyLength != Host.Expected[4]) {
                return 3;
            }
            return 0;
        }
        """;

    [Theory]
    [InlineData(BoundRuntimePolicy.FailFast)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions)]
    public async Task BodyTableValidationMatchesHostedReader(BoundRuntimePolicy policy)
    {
        var bodies = TrRmVariableTableWriter.EncodeBodies([
            new byte[] { 0xFF }, new byte[] { 0, 0x10, 0x80 },
        ]);
        var cases = new List<TableCase> {
            Valid(bodies, 2, 1),
            Valid(bodies, 2, 2),
            Rejected(bodies, 2, 0),
            Rejected(bodies, 2, 3),
            Rejected(bodies, 1, 1),
            Rejected([], 0, 1),
            Rejected([], -1, 1),
            Rejected([], 0x1000000, 1),
        };

        var emptyRow = bodies.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(emptyRow.AsSpan(sizeof(uint)), 0);
        cases.Add(Rejected(emptyRow, 2, 2));

        var nonzeroStart = bodies.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(nonzeroStart, 1);
        cases.Add(Rejected(nonzeroStart, 2, 1));

        var descendingOffsets = bodies.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(descendingOffsets.AsSpan(sizeof(uint)), 3);
        BinaryPrimitives.WriteUInt32LittleEndian(descendingOffsets.AsSpan(2 * sizeof(uint)), 1);
        cases.Add(Rejected(descendingOffsets, 2, 1));

        cases.Add(Rejected(bodies[..^sizeof(uint)], 2, 1));

        var pastEnd = bodies.ToArray();
        var finalOffset = 2 * sizeof(uint);
        BinaryPrimitives.WriteUInt32LittleEndian(pastEnd.AsSpan(finalOffset),
            BinaryPrimitives.ReadUInt32LittleEndian(pastEnd.AsSpan(finalOffset)) + 1);
        cases.Add(Rejected(pastEnd, 2, 1));

        var invalidOffsetByte = bodies.Select(static value => (int)value).ToArray();
        invalidOffsetByte[0] = 256;
        cases.Add(new TableCase(invalidOffsetByte, 2, 1, false, -1, -1, false));

        var root = RepositoryRoot();
        var components = new List<SyntaxTree>();
        foreach (var name in new[] { "VariableTableIndex.terrum", "BodyTableValidation.terrum" })
        {
            var path = Path.Combine(root, "self-host", name);
            var tree = SyntaxTree.Parse(await File.ReadAllTextAsync(path).ConfigureAwait(true), path);
            Assert.False(tree.HasErrors, string.Join(Environment.NewLine, tree.Diagnostics));
            components.Add(tree);
        }
        components.Add(SyntaxTree.Parse(VerificationSource, "body-table-validation.terrum"));

        for (var caseIndex = 0; caseIndex < cases.Count; caseIndex++)
        {
            var testCase = cases[caseIndex];
            if (testCase.HasHostedOracle)
            {
                Assert.Equal(testCase.ExpectedValid, ValidateHosted(testCase));
            }

            var compilation = Compilation.Create(components,
            [
                new HostBuffer("Rows", PrimitiveType.Int32, [.. testCase.Section], false),
                new HostBuffer("Expected", PrimitiveType.Int32,
                [
                    testCase.RecordCount, testCase.Row,
                    testCase.ExpectedValid ? 1 : 0,
                    testCase.ExpectedStart, testCase.ExpectedLength,
                ], false),
            ], policy);
            Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
            var program = Assert.IsType<BoundProgram>(compilation.Program);
            Assert.Equal(0, NativeJit.Execute(program, (NativeRuntimePolicy)policy));
            Assert.Equal(0, await ExecuteAotAsync(program, (NativeRuntimePolicy)policy)
                .ConfigureAwait(true));
        }
    }

    private static TableCase Valid(byte[] section, int count, int row)
    {
        var reader = new TrRmBodyTableReader(section, count);
        var start = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(
            section.AsSpan((row - 1) * sizeof(uint))));
        return new TableCase([.. section.Select(static value => (int)value)],
            count, row, true, start, reader.GetRow(row).Length, true);
    }

    private static TableCase Rejected(byte[] section, int count, int row)
        => new([.. section.Select(static value => (int)value)], count, row, false, -1, -1, true);

    private static bool ValidateHosted(TableCase testCase)
    {
        try
        {
            var reader = new TrRmBodyTableReader(ToBytes(testCase.Section), testCase.RecordCount);
            _ = reader.GetRow(testCase.Row);
            return true;
        }
        catch (InvalidDataException)
        {
            return false;
        }
    }

    private static byte[] ToBytes(int[] values)
        => values.Select(static value => checked((byte)value)).ToArray();

    private static async Task<int> ExecuteAotAsync(BoundProgram program, NativeRuntimePolicy policy)
    {
        var path = Path.Combine(Path.GetTempPath(), $"body-table-validation-{Guid.NewGuid():N}.exe");
        try
        {
            await File.WriteAllBytesAsync(path, NativeEmitter.Emit(program, policy))
                .ConfigureAwait(true);
            using var process = Process.Start(new ProcessStartInfo(path) {
                UseShellExecute = false,
                CreateNoWindow = true,
            }) ?? throw new InvalidOperationException("Could not start compiled Body-table validator.");
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

    private sealed record TableCase(int[] Section, int RecordCount, int Row,
        bool ExpectedValid, int ExpectedStart, int ExpectedLength, bool HasHostedOracle);
}
