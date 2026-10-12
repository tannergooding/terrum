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
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed class CompactBodyOperationIndexComponentTests
{
    private const string SourceMapConsumerVerificationSource = """
        namespace Terrum.CodeAnalysis;
        int32 Main() {
            if (!TryValidateSourceMapTable(1, 1, 3, 0, 0, 1, 1,
                    0, 0, 0, 0, 0, 1, 1, 1)) {
                return 1;
            }
            if (Host.BodyOwners[1] != 1) {
                return 4;
            }
            if (!TryBuildCompactBodySourceMapOperationIndex(1, 1, 1)
                || Host.SourceMapOperationCounts[0] != 1) {
                return 2;
            }
            bool locationsValid = TryValidateCompactSourceMapLocations(1, 1);
            return locationsValid == (Host.Expected[0] != 0) ? 0 : 3;
        }
        """;

    private const string VerificationSource = """
        namespace Terrum.CodeAnalysis;
        int32 Main() {
            int32 bodyCount = Host.Expected[0];
            int32 methodDefCount = Host.Expected[1];
            int32 typeSpecCount = Host.Expected[2];
            bool succeeded = TryBuildCompactBodySourceMapOperationIndex(
                bodyCount, methodDefCount, typeSpecCount);
            if (succeeded != (Host.Expected[3] != 0)) {
                return 1;
            }
            if (!succeeded) {
                return Host.SourceMapOperationCounts[0] == 0 ? 0 : 2;
            }
            if (Host.SourceMapOperationCounts[0] != 1) {
                return 3;
            }

            int32 methodArrayLength = methodDefCount + 1;
            int32 expectedBlocksOffset = 4 + methodArrayLength;
            int32 expectedOperationsOffset = expectedBlocksOffset + methodArrayLength;
            for (mut int32 method = 0; method <= methodDefCount; method = method + 1) {
                if (Host.SourceMapOperationBlockStarts[method]
                        != Host.Expected[4 + method]
                    || Host.SourceMapOperationBlockCounts[method]
                        != Host.Expected[expectedBlocksOffset + method]) {
                    return 4;
                }
            }
            for (mut int32 index = 0; index < Host.SourceMapOperationCounts.Length;
                    index = index + 1) {
                if (Host.SourceMapOperationCounts[index]
                    != Host.Expected[expectedOperationsOffset + index]) {
                    return 5;
                }
            }
            return 0;
        }
        """;

    [Theory]
    [InlineData(BoundRuntimePolicy.FailFast)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions)]
    public async Task SourceMapOperationIndexMatchesBodyOwnersAsync(BoundRuntimePolicy policy)
    {
        var testCase = CreateValidCase();
        await VerifyAsync(testCase, policy).ConfigureAwait(true);
        await VerifyAsync(testCase with { ScratchCapacity = 6 }, policy).ConfigureAwait(true);
    }

    [Theory]
    [InlineData(BoundRuntimePolicy.FailFast)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions)]
    public async Task SourceMapOperationIndexHandlesManyReverseOwnedLinearBodiesAsync(
        BoundRuntimePolicy policy)
    {
        await VerifyAsync(CreateManyLinearCase(512), policy).ConfigureAwait(true);
    }

    [Theory]
    [InlineData(BoundRuntimePolicy.FailFast)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions)]
    public async Task SourceMapOperationIndexFramesLocalAndPackedCallWidthsAsync(
        BoundRuntimePolicy policy)
    {
        int[] widths = [1, 2, 4];
        var bodyRows = new byte[widths.Length * widths.Length * 2][];
        var body = 0;
        foreach (var referenceWidth in widths)
        {
            foreach (var countWidth in widths)
            {
                bodyRows[body++] =
                    CompactLinearBodyFramingComponentTests.LocalCallBody(
                        referenceWidth, countWidth);
                bodyRows[body++] =
                    CompactLinearBodyFramingComponentTests.PackedCallBody(
                        referenceWidth, countWidth);
            }
        }

        int methodCount = bodyRows.Length;
        var bodyOwners = new int[methodCount + 1];
        var expectedStarts = new int[methodCount + 1];
        var expectedBlockCounts = new int[methodCount + 1];
        var expectedOperationCounts = new int[methodCount + 1];
        expectedOperationCounts[0] = 1;
        for (var method = 1; method <= methodCount; method++)
        {
            bodyOwners[method] = methodCount - method + 1;
            expectedStarts[method] = method;
            expectedBlockCounts[method] = 1;
            expectedOperationCounts[method] = 1;
        }

        await VerifyAsync(new IndexCase(
            bodyRows,
            bodyOwners,
            methodCount,
            1,
            expectedStarts,
            expectedBlockCounts,
            expectedOperationCounts,
            expectedOperationCounts.Length,
            1,
            true), policy).ConfigureAwait(true);
    }

    [Theory]
    [InlineData(BoundRuntimePolicy.FailFast)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions)]
    public async Task SourceMapOperationIndexHandlesManyReverseOwnedIndexedBodiesAsync(
        BoundRuntimePolicy policy)
    {
        await VerifyAsync(CreateManyIndexedCase(512), policy).ConfigureAwait(true);
    }

    [Theory]
    [InlineData(BoundRuntimePolicy.FailFast)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions)]
    public async Task SourceMapOperationIndexHandlesEmptyBodyTablesAsync(
        BoundRuntimePolicy policy)
    {
        await VerifyAsync(CreateEmptyCase(0), policy).ConfigureAwait(true);
        await VerifyAsync(CreateEmptyCase(3), policy).ConfigureAwait(true);
        await VerifyAsync(CreateEmptyCase(3) with {
            BodyRows = [new byte[] { 0 }],
            ExpectedValid = false,
        }, policy).ConfigureAwait(true);
    }

    [Theory]
    [InlineData(BoundRuntimePolicy.FailFast)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions)]
    public async Task SourceMapOperationIndexFeedsCompactLocationValidationAsync(
        BoundRuntimePolicy policy)
    {
        await VerifySourceMapConsumerAsync(ordinal: 0, expectedValid: true, policy)
            .ConfigureAwait(true);
        await VerifySourceMapConsumerAsync(ordinal: 1, expectedValid: true, policy)
            .ConfigureAwait(true);
        await VerifySourceMapConsumerAsync(ordinal: 2, expectedValid: false, policy)
            .ConfigureAwait(true);
        await VerifySourceMapConsumerAsync(ordinal: 0, expectedValid: true, policy,
            hasOperation: false).ConfigureAwait(true);
        await VerifySourceMapConsumerAsync(ordinal: 1, expectedValid: false, policy,
            hasOperation: false).ConfigureAwait(true);
        await VerifySourceMapConsumerAsync(ordinal: 1, expectedValid: true, policy,
            indexedBody: true).ConfigureAwait(true);
        await VerifySourceMapConsumerAsync(ordinal: 2, expectedValid: false, policy,
            indexedBody: true).ConfigureAwait(true);
        await VerifySourceMapConsumerAsync(ordinal: 0, expectedValid: true, policy,
            indexedBody: true, block: 2).ConfigureAwait(true);
        await VerifySourceMapConsumerAsync(ordinal: 1, expectedValid: false, policy,
            indexedBody: true, block: 2).ConfigureAwait(true);
        await VerifySourceMapConsumerAsync(ordinal: 0, expectedValid: false, policy,
            indexedBody: true, block: 3).ConfigureAwait(true);
    }

    [Theory]
    [InlineData(BoundRuntimePolicy.FailFast)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions)]
    public async Task SourceMapOperationIndexDoesNotPublishMalformedOrOversizedRowsAsync(
        BoundRuntimePolicy policy)
    {
        var malformed = CreateValidCase();
        var badLastBody = (byte[])malformed.BodyRows[^1].ToArray();
        badLastBody[^1] = 2;
        await VerifyAsync(malformed with {
            BodyRows = [.. malformed.BodyRows[..^1], badLastBody],
            ExpectedValid = false,
        }, policy).ConfigureAwait(true);

        await VerifyAsync(CreateValidCase() with {
            BodyOwners = [0, 3, 3, 1],
            ExpectedValid = false,
        }, policy).ConfigureAwait(true);

        await VerifyAsync(CreateValidCase() with {
            OperationCapacity = 7,
            ExpectedValid = false,
        }, policy).ConfigureAwait(true);

        await VerifyAsync(CreateValidCase() with {
            ScratchCapacity = 5,
            ExpectedValid = false,
        }, policy).ConfigureAwait(true);
    }

    private static async Task VerifyAsync(IndexCase testCase, BoundRuntimePolicy policy)
    {
        var section = testCase.BodyRows.Length == 0
            ? new byte[4]
            : TrRmVariableTableWriter.EncodeBodies(testCase.BodyRows
                .Select(static row => new ReadOnlyMemory<byte>(row)).ToArray());
        var root = RepositoryRoot();
        var components = await LoadComponentsAsync(root).ConfigureAwait(true);
        components.Add(SyntaxTree.Parse(VerificationSource,
            "compact-body-operation-index.terrum"));

        var expected = new List<int> {
            testCase.BodyOwners.Length - 1,
            testCase.MethodDefCount,
            testCase.TypeSpecCount,
            testCase.ExpectedValid ? 1 : 0,
        };
        if (testCase.ExpectedValid)
        {
            expected.AddRange(testCase.ExpectedStarts);
            expected.AddRange(testCase.ExpectedBlockCounts);
            expected.AddRange(testCase.ExpectedOperationCounts);
        }

        var compilation = Compilation.Create(components,
        [
            new HostBuffer("Rows", PrimitiveType.Int32, [], false),
            new HostBuffer("BodyRows", PrimitiveType.Int32,
                [.. section.Select(static value => (int)value)], false),
            new HostBuffer("BodyOwners", PrimitiveType.Int32,
                [.. testCase.BodyOwners], false),
            new HostBuffer("SourceMapOperationBlockStarts", PrimitiveType.Int32,
                [.. Enumerable.Repeat(-1, testCase.MethodDefCount + 1)], true),
            new HostBuffer("SourceMapOperationBlockCounts", PrimitiveType.Int32,
                [.. Enumerable.Repeat(-1, testCase.MethodDefCount + 1)], true),
            new HostBuffer("SourceMapOperationCounts", PrimitiveType.Int32,
                [.. Enumerable.Repeat(-1, testCase.OperationCapacity)], true),
            new HostBuffer("BodyBlockOperationCounts", PrimitiveType.Int32,
                [.. Enumerable.Repeat(-1, testCase.ScratchCapacity)], true),
            new HostBuffer("Expected", PrimitiveType.Int32, [.. expected], false),
        ], policy);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = Assert.IsType<BoundProgram>(compilation.Program);
        Assert.Equal(0, NativeJit.Execute(program, (NativeRuntimePolicy)policy));
        Assert.Equal(0, await ExecuteAotAsync(program, (NativeRuntimePolicy)policy)
            .ConfigureAwait(true));
    }

    private static IndexCase CreateValidCase()
    {
        var loopSection = TrRmBodyExampleTests.IndexedScalarLoop();
        var loopRow = loopSection.AsSpan(8).ToArray();
        var loopCounts = TrRmBodyExampleTests.GetIndexedBlockOperationCounts(
            loopSection, parameterCount: 1, resultTypeSpec: 2, parameterTypeIds: [2]);
        byte[] linearRow = [
            0x06, 1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0,
            0xF0, 0x02, 0x12,
            2, 0, 0, 0, 2, 0, 0, 0, 1, 1, 0, 0, 0,
            0x83, 2,
        ];
        var twoBlockRow = CompactIndexedBodyFramingComponentTests.TwoBlockIndexedBody();
        int[] expectedCounts = [1, 0, 1, .. loopCounts];
        int[] expectedSourceMapOperationCounts = [0, .. expectedCounts];
        expectedSourceMapOperationCounts[0] = 1;
        return new IndexCase(
            [loopRow, linearRow, twoBlockRow],
            [0, 3, 2, 1],
            3,
            2,
            [0, 1, 3, 4],
            [0, 2, 1, loopCounts.Length],
            expectedSourceMapOperationCounts,
            expectedSourceMapOperationCounts.Length,
            16,
            true);
    }

    private static IndexCase CreateManyLinearCase(int methodCount)
    {
        byte[] zeroOperationRow = [0x04, 0, 0, 0x83, 1];
        byte[] oneOperationRow = [0x04, 0, 1, 1, 1, 1, 42, 0x83, 1];
        var bodyRows = new byte[methodCount][];
        var bodyOwners = new int[methodCount + 1];
        for (var body = 1; body <= methodCount; body++)
        {
            bodyRows[body - 1] = body % 2 == 0 ? oneOperationRow : zeroOperationRow;
            bodyOwners[body] = methodCount - body + 1;
        }

        var expectedStarts = new int[methodCount + 1];
        var expectedBlockCounts = new int[methodCount + 1];
        var expectedOperationCounts = new int[methodCount + 1];
        expectedOperationCounts[0] = 1;
        for (var method = 1; method <= methodCount; method++)
        {
            expectedStarts[method] = method;
            expectedBlockCounts[method] = 1;
            int body = methodCount - method + 1;
            expectedOperationCounts[method] = body % 2 == 0 ? 1 : 0;
        }

        return new IndexCase(
            bodyRows,
            bodyOwners,
            methodCount,
            0,
            expectedStarts,
            expectedBlockCounts,
            expectedOperationCounts,
            expectedOperationCounts.Length,
            1,
            true);
    }

    private static IndexCase CreateManyIndexedCase(int methodCount)
    {
        byte[] body = CompactIndexedBodyFramingComponentTests.TwoBlockIndexedBody();
        var bodyRows = Enumerable.Range(0, methodCount)
            .Select(_ => (byte[])body.Clone())
            .ToArray();
        var bodyOwners = new int[methodCount + 1];
        var expectedStarts = new int[methodCount + 1];
        var expectedBlockCounts = new int[methodCount + 1];
        var expectedOperationCounts = new int[1 + methodCount * 2];
        expectedOperationCounts[0] = 1;
        for (var bodyRow = 1; bodyRow <= methodCount; bodyRow++)
        {
            int method = methodCount - bodyRow + 1;
            bodyOwners[bodyRow] = method;
        }
        for (var method = 1; method <= methodCount; method++)
        {
            int start = 1 + (method - 1) * 2;
            expectedStarts[method] = start;
            expectedBlockCounts[method] = 2;
            expectedOperationCounts[start] = 1;
            expectedOperationCounts[start + 1] = 0;
        }

        return new IndexCase(
            bodyRows,
            bodyOwners,
            methodCount,
            1,
            expectedStarts,
            expectedBlockCounts,
            expectedOperationCounts,
            expectedOperationCounts.Length,
            4,
            true);
    }

    private static IndexCase CreateEmptyCase(int methodDefCount)
        => new(
            [],
            [0],
            methodDefCount,
            0,
            new int[methodDefCount + 1],
            new int[methodDefCount + 1],
            [1],
            1,
            0,
            true);

    private static async Task VerifySourceMapConsumerAsync(
        int ordinal, bool expectedValid, BoundRuntimePolicy policy, bool hasOperation = true,
        bool indexedBody = false, int block = 1)
    {
        var root = RepositoryRoot();
        var components = await LoadComponentsAsync(root, includeSourceMapValidation: true)
            .ConfigureAwait(true);
        components.Add(SyntaxTree.Parse(SourceMapConsumerVerificationSource,
            "compact-body-operation-index-source-map-consumer.terrum"));

        byte[] body = indexedBody
            ? CompactIndexedBodyFramingComponentTests.TwoBlockIndexedBody()
            : hasOperation
                ? [0x04, 0, 1, 1, 1, 1, 42, 0x83, 1]
                : [0x04, 0, 0, 0x83, 1];
        var bodySection = TrRmVariableTableWriter.EncodeBodies([
            new ReadOnlyMemory<byte>(body),
        ]);
        byte[] strings = TrRmStringTableWriter.EncodeNames([
            ("Module", 0), ("Run", 0), ("src/A", 0),
        ]);
        var modules = new byte[44];
        WriteWord(modules, 0, 1);
        WriteWord(modules, 4, 1);
        WriteWord(modules, 28, 1);
        WriteWord(modules, 32, 1);
        WriteWord(modules, 36, 1);
        WriteWord(modules, 40, 1);
        var methods = new byte[28];
        WriteWord(methods, 4, 2);
        WriteWord(methods, 8, 1);
        WriteWord(methods, 24, 1);
        var signatures = new byte[24];
        WriteWord(signatures, 0, 1);
        WriteWord(signatures, 12, 1);
        var sourceFiles = new byte[8];
        WriteWord(sourceFiles, 0, 3);
        var sourceMaps = new byte[52];
        WriteWord(sourceMaps, 0, 2);
        WriteHalfWord(sourceMaps, 4, 0x0020);
        WriteHalfWord(sourceMaps, 6, 1);
        WriteWord(sourceMaps, 8, 1);
        WriteWord(sourceMaps, 12, (uint)block);
        WriteWord(sourceMaps, 16, (uint)ordinal);
        WriteHalfWord(sourceMaps, 20, 0x001F);
        WriteWord(sourceMaps, 24, 1);
        WriteWord(sourceMaps, 36, 1);
        WriteWord(sourceMaps, 40, 2);
        WriteWord(sourceMaps, 48, 2);
        var compilation = Compilation.Create(components,
        [
            new HostBuffer("Rows", PrimitiveType.Int32,
                [.. strings.Select(static value => (int)value)], false),
            new HostBuffer("BodyRows", PrimitiveType.Int32,
                [.. bodySection.Select(static value => (int)value)], false),
            new HostBuffer("Modules", PrimitiveType.Int32,
                [.. modules.Select(static value => (int)value)], false),
            new HostBuffer("TypeDefs", PrimitiveType.Int32, [], false),
            new HostBuffer("Methods", PrimitiveType.Int32,
                [.. methods.Select(static value => (int)value)], false),
            new HostBuffer("Signatures", PrimitiveType.Int32,
                [.. signatures.Select(static value => (int)value)], false),
            new HostBuffer("SourceFiles", PrimitiveType.Int32,
                [.. sourceFiles.Select(static value => (int)value)], false),
            new HostBuffer("SourceMaps", PrimitiveType.Int32,
                [.. sourceMaps.Select(static value => (int)value)], false),
            new HostBuffer("IdentitySlots", PrimitiveType.Int32, [.. new int[2]], true),
            new HostBuffer("FieldOwners", PrimitiveType.Int32, [0], true),
            new HostBuffer("BodyOwners", PrimitiveType.Int32, [0, 0], true),
            new HostBuffer("SourceMapOperationBlockStarts", PrimitiveType.Int32, [0, 0], true),
            new HostBuffer("SourceMapOperationBlockCounts", PrimitiveType.Int32, [0, 0], true),
            new HostBuffer("SourceMapOperationCounts", PrimitiveType.Int32,
                [.. new int[indexedBody ? 3 : 2]], true),
            new HostBuffer("BodyBlockOperationCounts", PrimitiveType.Int32,
                [.. new int[indexedBody ? 4 : 0]], true),
            new HostBuffer("GenericSignatureOwners", PrimitiveType.Int32, [0, 0], true),
            new HostBuffer("Expected", PrimitiveType.Int32, [expectedValid ? 1 : 0], false),
        ], policy);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = Assert.IsType<BoundProgram>(compilation.Program);
        Assert.Equal(0, NativeJit.Execute(program, (NativeRuntimePolicy)policy));
        Assert.Equal(0, await ExecuteAotAsync(program, (NativeRuntimePolicy)policy)
            .ConfigureAwait(true));
    }

    private static async Task<List<SyntaxTree>> LoadComponentsAsync(string root,
        bool includeSourceMapValidation = false)
    {
        var components = new List<SyntaxTree>();
        var names = new List<string> {
            "VariableTableIndex.terrum",
            "BodyTableValidation.terrum",
            "CompactLinearBodyFraming.terrum",
            "CompactIndexedBodyFraming.terrum",
            "CompactBodyOperationIndex.terrum",
        };
        if (includeSourceMapValidation)
        {
            names.AddRange([
                "StringRowLayout.terrum",
                "StringRowText.terrum",
                "ModuleTableValidation.terrum",
                "TypeDefTableValidation.terrum",
                "MethodDefTableValidation.terrum",
                "SourceFileTableValidation.terrum",
                "SourceMapTableValidation.terrum",
            ]);
        }

        foreach (var name in names)
        {
            var path = Path.Combine(root, "self-host", name);
            var tree = SyntaxTree.Parse(await File.ReadAllTextAsync(path).ConfigureAwait(true), path);
            Assert.False(tree.HasErrors, string.Join(Environment.NewLine, tree.Diagnostics));
            components.Add(tree);
        }

        return components;
    }

    private static void WriteWord(byte[] bytes, int offset, uint value)
        => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);

    private static void WriteHalfWord(byte[] bytes, int offset, ushort value)
        => BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(offset), value);

    private static async Task<int> ExecuteAotAsync(BoundProgram program, NativeRuntimePolicy policy)
    {
        var path = Path.Combine(Path.GetTempPath(),
            $"compact-body-operation-index-{Guid.NewGuid():N}.exe");
        try
        {
            await File.WriteAllBytesAsync(path, NativeEmitter.Emit(program, policy))
                .ConfigureAwait(true);
            using var process = Process.Start(new ProcessStartInfo(path) {
                UseShellExecute = false,
                CreateNoWindow = true,
            }) ?? throw new InvalidOperationException("Could not start Body operation-index fixture.");
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

    private sealed record IndexCase(byte[][] BodyRows, int[] BodyOwners,
        int MethodDefCount, int TypeSpecCount, int[] ExpectedStarts, int[] ExpectedBlockCounts,
        int[] ExpectedOperationCounts, int OperationCapacity, int ScratchCapacity,
        bool ExpectedValid);
}
