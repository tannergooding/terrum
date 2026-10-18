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
using NUnit.Framework;

using Terrum.CodeAnalysis.Tests.Infrastructure;

namespace Terrum.CodeAnalysis.Tests.Modules.Metadata;

public sealed class BodyTableValidationComponentTests
{
    private const string BatchVerificationSource = """
        namespace Terrum.CodeAnalysis;
        int32 Main() {
            int32 recordCount = Host.Expected[0];
            if (!TryValidateBodyTableIndex(recordCount)) {
                return 1;
            }
            for (mut int32 row = 1; row <= recordCount; row = row + 1) {
                int32 expectedRecord = 1 + (row - 1) * 2;
                if (!TryGetValidatedBodyTableRowExtent(recordCount, row,
                    init var bodyStart, init var bodyLength)
                    || bodyStart != Host.Expected[expectedRecord]
                    || bodyLength != Host.Expected[expectedRecord + 1]) {
                    return 2;
                }
            }

            init int32 vacantStart;
            init int32 vacantLength;
            if (TryGetValidatedBodyTableRowExtent(recordCount, 0,
                init vacantStart, init vacantLength)
                || vacantStart is init || vacantLength is init) {
                return 3;
            }
            return 0;
        }
        """;

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
    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
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
        foreach (var name in new[] { "sources/Terrum.CodeAnalysis.Terrum/Modules/VariableTableIndex.terrum", "tests/Terrum.CodeAnalysis.Terrum.Tests/Modules/BodyTableValidation.terrum" })
        {
            var path = TerrumProjectPaths.RepositoryFile(root, name);
            var tree = SyntaxTree.Parse(await File.ReadAllTextAsync(path).ConfigureAwait(true), path);
            Assert.That(tree.HasErrors, Is.False, string.Join(Environment.NewLine, tree.Diagnostics));
            components.Add(tree);
        }
        components.Add(SyntaxTree.Parse(VerificationSource, "body-table-validation.terrum"));

        for (var caseIndex = 0; caseIndex < cases.Count; caseIndex++)
        {
            var testCase = cases[caseIndex];
            if (testCase.HasHostedOracle)
            {
                Assert.That(ValidateHosted(testCase), Is.EqualTo(testCase.ExpectedValid));
            }

            var compilation = Compilation.Create(components,
            [
                new HostBuffer("Rows", PrimitiveType.Int32, [], false),
                new HostBuffer("BodyRows", PrimitiveType.Int32, [.. testCase.Section], false),
                new HostBuffer("Expected", PrimitiveType.Int32,
                [
                    testCase.RecordCount, testCase.Row,
                    testCase.ExpectedValid ? 1 : 0,
                    testCase.ExpectedStart, testCase.ExpectedLength,
                ], false),
            ], policy);
            Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
            var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual0 = compilation.Program; Assert.That(__nunitActual0, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual0!; }))();
            Assert.That(NativeJit.Execute(program, (NativeRuntimePolicy)policy), Is.EqualTo(0));
            Assert.That(await ExecuteAotAsync(program, (NativeRuntimePolicy)policy)
                .ConfigureAwait(true), Is.EqualTo(0));
        }
    }
    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
    public async Task ValidatedBodyIndexSupportsBatchRowReadsAsync(BoundRuntimePolicy policy)
    {
        var rows = Enumerable.Range(0, 512)
            .Select(static index => new ReadOnlyMemory<byte>(
                Enumerable.Range(0, 1 + index % 7)
                    .Select(value => (byte)((index + value) & 0xFF)).ToArray()))
            .ToArray();
        var section = TrRmVariableTableWriter.EncodeBodies(rows);
        var expected = new List<int> { rows.Length };
        for (var row = 1; row <= rows.Length; row++)
        {
            var start = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(
                section.AsSpan((row - 1) * sizeof(uint))));
            var end = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(
                section.AsSpan(row * sizeof(uint))));
            expected.Add(start);
            expected.Add(end - start);
        }

        var root = RepositoryRoot();
        var components = new List<SyntaxTree>();
        foreach (var name in new[] { "sources/Terrum.CodeAnalysis.Terrum/Modules/VariableTableIndex.terrum", "tests/Terrum.CodeAnalysis.Terrum.Tests/Modules/BodyTableValidation.terrum" })
        {
            var path = TerrumProjectPaths.RepositoryFile(root, name);
            var tree = SyntaxTree.Parse(await File.ReadAllTextAsync(path).ConfigureAwait(true), path);
            Assert.That(tree.HasErrors, Is.False, string.Join(Environment.NewLine, tree.Diagnostics));
            components.Add(tree);
        }
        components.Add(SyntaxTree.Parse(BatchVerificationSource, "body-table-batch-validation.terrum"));

        var compilation = Compilation.Create(components,
        [
            new HostBuffer("Rows", PrimitiveType.Int32, [], false),
            new HostBuffer("BodyRows", PrimitiveType.Int32, [.. section.Select(static value => (int)value)], false),
            new HostBuffer("Expected", PrimitiveType.Int32, [.. expected], false),
        ], policy);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual1 = compilation.Program; Assert.That(__nunitActual1, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual1!; }))();
        Assert.That(NativeJit.Execute(program, (NativeRuntimePolicy)policy), Is.EqualTo(0));
        Assert.That(await ExecuteAotAsync(program, (NativeRuntimePolicy)policy).ConfigureAwait(true), Is.EqualTo(0));
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
        var path = Path.Combine(TestArtifactPaths.DirectoryPath, $"body-table-validation-{Guid.NewGuid():N}.exe");
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
            await TestFileCleanup.DeleteWithRetryAsync(path).ConfigureAwait(true);
        }
    }

    private static string RepositoryRoot() =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));

    private sealed record TableCase(int[] Section, int RecordCount, int Row,
        bool ExpectedValid, int ExpectedStart, int ExpectedLength, bool HasHostedOracle);
}
