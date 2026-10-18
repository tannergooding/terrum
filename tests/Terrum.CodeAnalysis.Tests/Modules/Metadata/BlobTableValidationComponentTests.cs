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

public sealed class BlobTableValidationComponentTests
{
    private const string BatchVerificationSource = """
        namespace Terrum.CodeAnalysis;
        int32 Main() {
            int32 recordCount = Host.Expected[0];
            if (!TryValidateBlobTableIndex(recordCount)) {
                return 1;
            }
            for (mut int32 row = 1; row <= recordCount; row = row + 1) {
                int32 expectedRecord = 1 + (row - 1) * 2;
                if (!TryGetValidatedBlobTableRowExtent(recordCount, row,
                    init var blobStart, init var blobLength)
                    || blobStart != Host.Expected[expectedRecord]
                    || blobLength != Host.Expected[expectedRecord + 1]) {
                    return 2;
                }
            }

            init int32 vacantStart;
            init int32 vacantLength;
            if (TryGetValidatedBlobTableRowExtent(recordCount, 0,
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
            if (!TryGetBlobTableRowExtent(Host.Expected[0], Host.Expected[1],
                init var blobStart, init var blobLength)) {
                return Host.Expected[2] == 0 ? 0 : 1;
            }
            if (Host.Expected[2] == 0) {
                return 2;
            }
            if (blobStart != Host.Expected[3] || blobLength != Host.Expected[4]) {
                return 3;
            }
            return 0;
        }
        """;
    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
    public async Task BlobTableValidationMatchesHostedReader(BoundRuntimePolicy policy)
    {
        var blobs = TrRmVariableTableWriter.EncodeBlobs([
            ReadOnlyMemory<byte>.Empty, new byte[] { 0x16, 0, 0, 1 },
            ReadOnlyMemory<byte>.Empty, new byte[] { 0xFF, 0, 0x16, 0 },
        ]);
        var cases = new List<TableCase> {
            Valid(blobs, 4, 1),
            Valid(blobs, 4, 2),
            Valid(blobs, 4, 3),
            Valid(blobs, 4, 4),
            Rejected(blobs, 4, 0),
            Rejected(blobs, 4, 5),
            Rejected(blobs, 3, 1),
            Rejected([], 0, 1),
            Rejected([], -1, 1),
            Rejected([], 0x1000000, 1),
        };

        var nonzeroStart = blobs.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(nonzeroStart, 1);
        cases.Add(Rejected(nonzeroStart, 4, 1));

        var descendingOffsets = blobs.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(descendingOffsets.AsSpan(sizeof(uint)), 2);
        BinaryPrimitives.WriteUInt32LittleEndian(descendingOffsets.AsSpan(2 * sizeof(uint)), 1);
        cases.Add(Rejected(descendingOffsets, 4, 1));

        cases.Add(Rejected(blobs[..^sizeof(uint)], 4, 1));

        var pastEnd = blobs.ToArray();
        int finalOffset = 4 * sizeof(uint);
        BinaryPrimitives.WriteUInt32LittleEndian(pastEnd.AsSpan(finalOffset),
            BinaryPrimitives.ReadUInt32LittleEndian(pastEnd.AsSpan(finalOffset)) + 1);
        cases.Add(Rejected(pastEnd, 4, 4));

        var invalidOffsetByte = blobs.Select(static value => (int)value).ToArray();
        invalidOffsetByte[0] = 256;
        cases.Add(new TableCase(invalidOffsetByte, 4, 1, false, -1, -1, false));

        var root = RepositoryRoot();
        var components = new List<SyntaxTree>();
        foreach (var name in new[] { "sources/Terrum.CodeAnalysis.Terrum/Modules/VariableTableIndex.terrum", "tests/Terrum.CodeAnalysis.Terrum.Tests/Modules/BlobTableValidation.terrum" })
        {
            var path = TerrumProjectPaths.RepositoryFile(root, name);
            var tree = SyntaxTree.Parse(await File.ReadAllTextAsync(path).ConfigureAwait(true), path);
            Assert.That(tree.HasErrors, Is.False, string.Join(Environment.NewLine, tree.Diagnostics));
            components.Add(tree);
        }
        components.Add(SyntaxTree.Parse(VerificationSource, "blob-table-validation.terrum"));

        for (var caseIndex = 0; caseIndex < cases.Count; caseIndex++)
        {
            var testCase = cases[caseIndex];
            if (testCase.HasHostedOracle)
            {
                Assert.That(ValidateHosted(testCase), Is.EqualTo(testCase.ExpectedValid));
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
            Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
            var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual0 = compilation.Program; Assert.That(__nunitActual0, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual0!; }))();
            Assert.That(NativeJit.Execute(program, (NativeRuntimePolicy)policy), Is.EqualTo(0));
            Assert.That(await ExecuteAotAsync(program, (NativeRuntimePolicy)policy)
                .ConfigureAwait(true), Is.EqualTo(0));
        }
    }
    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
    public async Task ValidatedBlobIndexSupportsBatchRowReadsAsync(BoundRuntimePolicy policy)
    {
        var rows = Enumerable.Range(0, 512)
            .Select(static index => index % 3 == 0
                ? ReadOnlyMemory<byte>.Empty
                : new ReadOnlyMemory<byte>(new byte[] { (byte)(index & 0xFF), (byte)(index >> 8) }))
            .ToArray();
        var section = TrRmVariableTableWriter.EncodeBlobs(rows);
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
        foreach (var name in new[] { "sources/Terrum.CodeAnalysis.Terrum/Modules/VariableTableIndex.terrum", "tests/Terrum.CodeAnalysis.Terrum.Tests/Modules/BlobTableValidation.terrum" })
        {
            var path = TerrumProjectPaths.RepositoryFile(root, name);
            var tree = SyntaxTree.Parse(await File.ReadAllTextAsync(path).ConfigureAwait(true), path);
            Assert.That(tree.HasErrors, Is.False, string.Join(Environment.NewLine, tree.Diagnostics));
            components.Add(tree);
        }
        components.Add(SyntaxTree.Parse(BatchVerificationSource, "blob-table-batch-validation.terrum"));

        var compilation = Compilation.Create(components,
        [
            new HostBuffer("Rows", PrimitiveType.Int32, [.. section.Select(static value => (int)value)], false),
            new HostBuffer("Expected", PrimitiveType.Int32, [.. expected], false),
        ], policy);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual1 = compilation.Program; Assert.That(__nunitActual1, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual1!; }))();
        Assert.That(NativeJit.Execute(program, (NativeRuntimePolicy)policy), Is.EqualTo(0));
        Assert.That(await ExecuteAotAsync(program, (NativeRuntimePolicy)policy).ConfigureAwait(true), Is.EqualTo(0));
    }

    private static TableCase Valid(byte[] section, int count, int row)
    {
        var reader = new TrRmBlobTableReader(section, count);
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
            var reader = new TrRmBlobTableReader(ToBytes(testCase.Section), testCase.RecordCount);
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
        var path = Path.Combine(TestArtifactPaths.DirectoryPath, $"blob-table-validation-{Guid.NewGuid():N}.exe");
        try
        {
            await File.WriteAllBytesAsync(path, NativeEmitter.Emit(program, policy))
                .ConfigureAwait(true);
            using var process = Process.Start(new ProcessStartInfo(path) {
                UseShellExecute = false,
                CreateNoWindow = true,
            }) ?? throw new InvalidOperationException("Could not start compiled Blob-table validator.");
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
