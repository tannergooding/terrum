using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Terrum.CodeAnalysis.Tests.Infrastructure;
using Terrum.CodeAnalysis.Tests.Modules.TrRm;

namespace Terrum.CodeAnalysis.Tests.Modules.Bodies;

public sealed class CompactIndexedBodyFramingComponentTests
{
    private const string BlockOperationCountVerificationSource = """
        namespace Terrum.CodeAnalysis;
        int32 Main() {
            int32 recordCount = Host.Expected[0];
            int32 typeSpecCount = Host.Expected[1];
            if (!TryValidateBodyTableIndex(recordCount)
                || !TryGetValidatedCompactIndexedBodyFrame(recordCount, 1, typeSpecCount,
                    init var blockCount, init var operationCount)
                || blockCount != 2 || operationCount != 1) {
                return 1;
            }

            if (!TryGetCompactIndexedBodyBlockOperationCount(recordCount, 1,
                    typeSpecCount, 1, init var firstBlockOperations)
                || firstBlockOperations != 1
                || !TryGetValidatedCompactIndexedBodyBlockOperationCount(recordCount,
                    1, typeSpecCount, 2, init var secondBlockOperations)
                || secondBlockOperations != 0) {
                return 2;
            }

            init int32 zeroBlockOperations;
            init int32 outOfRangeOperations;
            if (TryGetValidatedCompactIndexedBodyBlockOperationCount(recordCount,
                    1, typeSpecCount, 0, init zeroBlockOperations)
                || zeroBlockOperations is init
                || TryGetCompactIndexedBodyBlockOperationCount(recordCount, 1,
                    typeSpecCount, 3, init outOfRangeOperations)
                || outOfRangeOperations is init) {
                return 3;
            }

            if (!TryWriteCompactIndexedBodyOperationCounts(recordCount, 1,
                    typeSpecCount, 1, init var writtenBlockCount,
                    init var writtenOperationCount)
                || writtenBlockCount != 2 || writtenOperationCount != 1
                || Host.BodyBlockOperationCounts[1] != writtenBlockCount
                || Host.BodyBlockOperationCounts[2] != 1
                || Host.BodyBlockOperationCounts[3] != 0) {
                return 4;
            }

            init int32 malformedBodyOperations;
            if (TryGetValidatedCompactIndexedBodyBlockOperationCount(recordCount,
                    2, typeSpecCount, 1, init malformedBodyOperations)
                || malformedBodyOperations is init) {
                return 5;
            }

            init int32 malformedBlocks;
            init int32 malformedOperations;
            if (TryWriteCompactIndexedBodyOperationCounts(recordCount, 2,
                    typeSpecCount, 4, init malformedBlocks, init malformedOperations)
                || malformedBlocks is init || malformedOperations is init
                || Host.BodyBlockOperationCounts[4] != 0) {
                return 6;
            }

            init int32 shortBufferBlocks;
            init int32 shortBufferOperations;
            if (TryWriteCompactIndexedBodyOperationCounts(recordCount, 1,
                    typeSpecCount, Host.BodyBlockOperationCounts.Length - 1,
                    init shortBufferBlocks, init shortBufferOperations)
                || shortBufferBlocks is init || shortBufferOperations is init
                || Host.BodyBlockOperationCounts[Host.BodyBlockOperationCounts.Length - 1] != 0) {
                return 7;
            }
            return 0;
        }
        """;

    private const string LoopBlockOperationCountVerificationSource = """
        namespace Terrum.CodeAnalysis;
        int32 Main() {
            int32 recordCount = Host.Expected[0];
            int32 typeSpecCount = Host.Expected[1];
            if (!TryValidateBodyTableIndex(recordCount)
                || !TryGetValidatedCompactIndexedBodyFrame(recordCount, 1, typeSpecCount,
                    init var blockCount, init var operationCount)
                || blockCount != Host.Expected[2]
                || operationCount != Host.Expected[3]) {
                return 1;
            }

            for (mut int32 block = 1; block <= blockCount; block = block + 1) {
                if (!TryGetValidatedCompactIndexedBodyBlockOperationCount(recordCount,
                        1, typeSpecCount, block, init var blockOperations)
                    || blockOperations != Host.Expected[3 + block]) {
                    return 2;
                }
            }

            if (!TryWriteCompactIndexedBodyOperationCounts(recordCount, 1,
                    typeSpecCount, 1, init var writtenBlockCount,
                    init var writtenOperationCount)
                || writtenBlockCount != blockCount
                || writtenOperationCount != operationCount
                || Host.BodyBlockOperationCounts[1] != blockCount) {
                return 3;
            }
            for (mut int32 block = 1; block <= blockCount; block = block + 1) {
                if (Host.BodyBlockOperationCounts[1 + block]
                    != Host.Expected[3 + block]) {
                    return 4;
                }
            }
            return 0;
        }
        """;

    private const string ShortBatchBufferVerificationSource = """
        namespace Terrum.CodeAnalysis;
        int32 Main() {
            init int32 blocks;
            init int32 operations;
            if (TryWriteCompactIndexedBodyOperationCounts(1, 1, 1, 1,
                    init blocks, init operations)
                || blocks is init || operations is init
                || Host.BodyBlockOperationCounts[1] != 0) {
                return 1;
            }
            return 0;
        }
        """;

    private const string BatchVerificationSource = """
        namespace Terrum.CodeAnalysis;
        int32 Main() {
            int32 recordCount = Host.Expected[0];
            if (!TryValidateBodyTableIndex(recordCount)) {
                return 1;
            }
            for (mut int32 row = 1; row <= recordCount; row = row + 1) {
                if (!TryGetValidatedCompactIndexedBodyFrame(recordCount, row, 0,
                    init var blockCount, init var operationCount)
                    || blockCount != 1 || operationCount != 0) {
                    return 2;
                }
            }

            init int32 vacantBlocks;
            init int32 vacantOperations;
            if (TryGetValidatedCompactIndexedBodyFrame(recordCount, 0, 0,
                init vacantBlocks, init vacantOperations)
                || vacantBlocks is init || vacantOperations is init) {
                return 3;
            }
            return 0;
        }
        """;

    private const string VerificationSource = """
        namespace Terrum.CodeAnalysis;
        int32 Main() {
            if (!TryGetCompactIndexedBodyFrame(
                Host.Expected[0], Host.Expected[1], Host.Expected[2],
                init var blockCount, init var operationCount)) {
                if (Host.Expected[3] != 0) {
                    return 1;
                }
                return !(blockCount is init) && !(operationCount is init) ? 0 : 2;
            }
            if (Host.Expected[3] == 0) {
                return 3;
            }
            if (blockCount != Host.Expected[4] || operationCount != Host.Expected[5]) {
                return 4;
            }
            return 0;
        }
        """;

    private const string SchemaVerificationSource = """
        namespace Terrum.CodeAnalysis;
        int32 Main() {
            int32 recordCount = Host.Expected[0];
            if (!TryValidateBodyTableIndex(recordCount)) {
                return 1;
            }
            for (mut int32 row = 1; row <= recordCount; row = row + 1) {
                int32 expectedOffset = 1 + (row - 1) * 2;
                if (!TryGetValidatedCompactIndexedBodyFrame(recordCount, row, 1,
                    init var blockCount, init var operationCount)
                    || blockCount != Host.Expected[expectedOffset]
                    || operationCount != Host.Expected[expectedOffset + 1]) {
                    return 2;
                }
            }
            return 0;
        }
        """;
    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
    public async Task CompactIndexedBodyFramingMatchesHostedReader(BoundRuntimePolicy policy)
    {
        var indexedConstant = TrRmBodyExampleTests.IndexedScalarOperation(
            [1, 1, 2, 42], [], 2);
        var indexedLoop = TrRmBodyExampleTests.IndexedScalarLoop();
        var cases = new List<TableCase> {
            Valid(EncodeBody(FailureBody(0)), 0, 1, 0),
            Valid(EncodeBody(FailureBody(1)), 0, 1, 0),
            Valid(EncodeBody(FailureBody(2)), 0, 1, 0),
            HostedValid(indexedConstant, 2, 1, 1, 0),
            Rejected(ChangeSectionByte(indexedConstant, 13, 0), 2),
            HostedValid(indexedLoop, 2, 4,
                TrRmBodyExampleTests.CountIndexedOperations(indexedLoop,
                    parameterCount: 1, resultTypeSpec: 2, parameterTypeIds: [2]),
                parameterCount: 1, parameterTypeIds: [2]),
            HostedValid(TrRmBodyExampleTests.ReadExample(
                "#### Compact indexed conditional merge"), 2, 4, 0),
            Rejected(FailureBody(0)[..^1]),
            Rejected(IndexedBody([0x19, 1, 1, 1], 1, Return(1))),
            Rejected(IndexedBody([0xF0, 0x03, 1, 1, 1, 42], 1, Return(1))),
            Rejected(ChangeBodyByte(FailureBody(0), 0, 0x04)),
            Rejected(ChangeBodyByte(FailureBody(0), 1, 0)),
            Rejected(ChangeBodyByte(FailureBody(0), 8, 3)),
            Rejected(ChangeBodyByte(FailureBody(0), 9, 3)),
            Rejected(ChangeBodyByte(FailureBody(0), 10, 0x20)),
            Rejected(ChangeBodyByte(FailureBody(0), 10, 4)),
            Rejected(ChangeBodyByte(FailureBody(0), 11, 1)),
            Rejected(ChangeBodyByte(FailureBody(0), 12, 0x82)),
            Rejected(ChangeBodyByte(FailureBody(0), 13, 5)),
        };

        var root = RepositoryRoot();
        var components = await LoadComponentsAsync(root).ConfigureAwait(true);
        components.Add(SyntaxTree.Parse(VerificationSource,
            "compact-indexed-body-framing.terrum"));

        foreach (var testCase in cases)
        {
            if (testCase.HasHostedOracle)
            {
                Assert.That(TrRmBodyExampleTests.InspectIndexed(testCase.Section,
                        parameterCount: testCase.ParameterCount,
                        parameterTypeIds: testCase.ParameterTypeIds).Blocks, Is.EqualTo(testCase.ExpectedBlockCount));
                Assert.That(TrRmBodyExampleTests.CountIndexedOperations(testCase.Section,
                        testCase.ParameterCount, parameterTypeIds: testCase.ParameterTypeIds), Is.EqualTo(testCase.ExpectedOperationCount));
            }

            var compilation = Compilation.Create(components,
            [
                new HostBuffer("Rows", PrimitiveType.Int32, [], false),
                new HostBuffer("BodyRows", PrimitiveType.Int32,
                    [.. testCase.Section.Select(static value => (int)value)], false),
                new HostBuffer("Expected", PrimitiveType.Int32,
                [
                    1, 1, testCase.TypeSpecCount,
                    testCase.ExpectedValid ? 1 : 0,
                    testCase.ExpectedBlockCount,
                    testCase.ExpectedOperationCount,
                ], false),
                new HostBuffer("BodyBlockOperationCounts", PrimitiveType.Int32,
                    [.. Enumerable.Repeat(-1, 16)], true),
            ], policy);
            Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
            var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual0 = compilation.Program; Assert.That(__nunitActual0, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual0!; }))();
            var jitResult = NativeJit.Execute(program, (NativeRuntimePolicy)policy);
            Assert.That(jitResult == 0, Is.True, $"JIT result {jitResult} for Body {Convert.ToHexString(testCase.Section.AsSpan(8))}.");
            var aotResult = await ExecuteAotAsync(program, (NativeRuntimePolicy)policy)
                .ConfigureAwait(true);
            Assert.That(aotResult == 0, Is.True, $"AOT result {aotResult} for Body {Convert.ToHexString(testCase.Section.AsSpan(8))}.");
        }
    }
    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
    public async Task CompactIndexedBodyBlockOperationCountsAreFramedPerBlockAsync(
        BoundRuntimePolicy policy)
    {
        var malformedBody = TwoBlockIndexedBody();
        malformedBody[^1] = 2;
        var section = TrRmVariableTableWriter.EncodeBodies([
            new ReadOnlyMemory<byte>(TwoBlockIndexedBody()),
            new ReadOnlyMemory<byte>(malformedBody),
        ]);
        var root = RepositoryRoot();
        var components = await LoadComponentsAsync(root).ConfigureAwait(true);
        components.Add(SyntaxTree.Parse(BlockOperationCountVerificationSource,
            "compact-indexed-body-block-operation-count.terrum"));

        var compilation = Compilation.Create(components,
        [
            new HostBuffer("Rows", PrimitiveType.Int32, [], false),
            new HostBuffer("BodyRows", PrimitiveType.Int32,
                [.. section.Select(static value => (int)value)], false),
            new HostBuffer("Expected", PrimitiveType.Int32, [2, 1], false),
            new HostBuffer("BodyBlockOperationCounts", PrimitiveType.Int32,
                [.. Enumerable.Repeat(-1, 16)], true),
        ], policy);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual1 = compilation.Program; Assert.That(__nunitActual1, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual1!; }))();
        Assert.That(NativeJit.Execute(program, (NativeRuntimePolicy)policy), Is.EqualTo(0));
        Assert.That(await ExecuteAotAsync(program, (NativeRuntimePolicy)policy)
            .ConfigureAwait(true), Is.EqualTo(0));
    }
    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
    public async Task CompactIndexedBodyBlockOperationCountsMatchHostedLoopAsync(
        BoundRuntimePolicy policy)
    {
        var section = TrRmBodyExampleTests.IndexedScalarLoop();
        var expectedOperationCounts = TrRmBodyExampleTests.GetIndexedBlockOperationCounts(
            section, parameterCount: 1, resultTypeSpec: 2, parameterTypeIds: [2]);
        var root = RepositoryRoot();
        var components = await LoadComponentsAsync(root).ConfigureAwait(true);
        components.Add(SyntaxTree.Parse(LoopBlockOperationCountVerificationSource,
            "compact-indexed-body-loop-block-operation-count.terrum"));

        var compilation = Compilation.Create(components,
        [
            new HostBuffer("Rows", PrimitiveType.Int32, [], false),
            new HostBuffer("BodyRows", PrimitiveType.Int32,
                [.. section.Select(static value => (int)value)], false),
            new HostBuffer("Expected", PrimitiveType.Int32,
                [1, 2, expectedOperationCounts.Length,
                    expectedOperationCounts.Sum(), .. expectedOperationCounts], false),
            new HostBuffer("BodyBlockOperationCounts", PrimitiveType.Int32,
                [.. Enumerable.Repeat(-1, 16)], true),
        ], policy);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual2 = compilation.Program; Assert.That(__nunitActual2, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual2!; }))();
        Assert.That(NativeJit.Execute(program, (NativeRuntimePolicy)policy), Is.EqualTo(0));
        Assert.That(await ExecuteAotAsync(program, (NativeRuntimePolicy)policy)
            .ConfigureAwait(true), Is.EqualTo(0));
    }
    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
    public async Task CompactIndexedBodyBlockOperationCountsRejectOneMissingSlotAsync(
        BoundRuntimePolicy policy)
    {
        var section = TrRmVariableTableWriter.EncodeBodies([
            new ReadOnlyMemory<byte>(TwoBlockIndexedBody()),
        ]);
        var root = RepositoryRoot();
        var components = await LoadComponentsAsync(root).ConfigureAwait(true);
        components.Add(SyntaxTree.Parse(ShortBatchBufferVerificationSource,
            "compact-indexed-body-short-batch-buffer.terrum"));

        var compilation = Compilation.Create(components,
        [
            new HostBuffer("Rows", PrimitiveType.Int32, [], false),
            new HostBuffer("BodyRows", PrimitiveType.Int32,
                [.. section.Select(static value => (int)value)], false),
            new HostBuffer("BodyBlockOperationCounts", PrimitiveType.Int32,
                [.. Enumerable.Repeat(-1, 3)], true),
        ], policy);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual3 = compilation.Program; Assert.That(__nunitActual3, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual3!; }))();
        Assert.That(NativeJit.Execute(program, (NativeRuntimePolicy)policy), Is.EqualTo(0));
        Assert.That(await ExecuteAotAsync(program, (NativeRuntimePolicy)policy)
            .ConfigureAwait(true), Is.EqualTo(0));
    }
    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
    public async Task ValidatedBodyIndexSupportsBatchFramingAsync(BoundRuntimePolicy policy)
    {
        const int recordCount = 512;
        var body = new ReadOnlyMemory<byte>(FailureBody(0));
        var section = TrRmVariableTableWriter.EncodeBodies(
            Enumerable.Repeat(body, recordCount).ToArray());
        var root = RepositoryRoot();
        var components = await LoadComponentsAsync(root).ConfigureAwait(true);
        components.Add(SyntaxTree.Parse(BatchVerificationSource,
            "compact-indexed-body-batch-framing.terrum"));

        var compilation = Compilation.Create(components,
        [
            new HostBuffer("Rows", PrimitiveType.Int32, [], false),
            new HostBuffer("BodyRows", PrimitiveType.Int32,
                [.. section.Select(static value => (int)value)], false),
            new HostBuffer("Expected", PrimitiveType.Int32, [recordCount], false),
            new HostBuffer("BodyBlockOperationCounts", PrimitiveType.Int32,
                [.. Enumerable.Repeat(-1, 16)], true),
        ], policy);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual4 = compilation.Program; Assert.That(__nunitActual4, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual4!; }))();
        Assert.That(NativeJit.Execute(program, (NativeRuntimePolicy)policy), Is.EqualTo(0));
        Assert.That(await ExecuteAotAsync(program, (NativeRuntimePolicy)policy)
            .ConfigureAwait(true), Is.EqualTo(0));
    }
    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
    public async Task CompactIndexedBodyFramingCoversInstructionAndTerminatorSchemasAsync(
        BoundRuntimePolicy policy)
    {
        var rows = new List<byte[]>();
        var expected = new List<int>();
        foreach (var (operation, resultCount) in IndexedOperations())
        {
            rows.Add(IndexedBody(operation, resultCount, Return(resultCount == 0 ? 0u : 1u)));
            expected.Add(1);
            expected.Add(1);
        }

        var constant = new byte[] { 1, 1, 1, 42 };
        foreach (var terminator in new[] {
            Return(1),
            new byte[] { 0x80, 1, 0 },
            new byte[] { 0x81, 1, 1, 0, 1, 0 },
            new byte[] { 0x82, 1, 0, 1, 0 },
            [0xF0, 0x01, 0x83, 1, 0],
            [0xF0, 0x01, 0x80, 1, 0, 0],
            [0xF0, 0x01, 0x81, 1, 0, 1, 0, 0, 1, 0, 0],
            [0xF0, 0x11, 0x82, 1, 0, 0, 1, 0, 0],
        })
        {
            rows.Add(IndexedBody(constant, 1, terminator));
            expected.Add(1);
            expected.Add(1);
        }

        var voidBody = IndexedBody([], 0, Return(0));
        rows.Add(voidBody);
        expected.Add(1);
        expected.Add(0);

        var section = TrRmVariableTableWriter.EncodeBodies(
            rows.Select(static row => new ReadOnlyMemory<byte>(row)).ToArray());
        var root = RepositoryRoot();
        var components = await LoadComponentsAsync(root).ConfigureAwait(true);
        components.Add(SyntaxTree.Parse(SchemaVerificationSource,
            "compact-indexed-body-schema-framing.terrum"));

        var compilation = Compilation.Create(components,
        [
            new HostBuffer("Rows", PrimitiveType.Int32, [], false),
            new HostBuffer("BodyRows", PrimitiveType.Int32,
                [.. section.Select(static value => (int)value)], false),
            new HostBuffer("Expected", PrimitiveType.Int32,
                [rows.Count, .. expected], false),
            new HostBuffer("BodyBlockOperationCounts", PrimitiveType.Int32,
                [.. Enumerable.Repeat(-1, 16)], true),
        ], policy);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual5 = compilation.Program; Assert.That(__nunitActual5, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual5!; }))();
        Assert.That(NativeJit.Execute(program, (NativeRuntimePolicy)policy), Is.EqualTo(0));
        Assert.That(await ExecuteAotAsync(program, (NativeRuntimePolicy)policy)
            .ConfigureAwait(true), Is.EqualTo(0));
    }

    private static async Task<List<SyntaxTree>> LoadComponentsAsync(string root)
    {
        var components = new List<SyntaxTree>();
        foreach (var name in new[] {
            "sources/Terrum.CodeAnalysis.Terrum/Modules/VariableTableIndex.terrum",
            "tests/Terrum.CodeAnalysis.Terrum.Tests/Modules/BodyTableValidation.terrum",
            "sources/Terrum.CodeAnalysis.Terrum/Modules/CompactIndexedBodyFraming.terrum",
        })
        {
            var path = TerrumProjectPaths.RepositoryFile(root, name);
            var tree = SyntaxTree.Parse(await File.ReadAllTextAsync(path).ConfigureAwait(true), path);
            Assert.That(tree.HasErrors, Is.False, string.Join(Environment.NewLine, tree.Diagnostics));
            components.Add(tree);
        }

        return components;
    }

    private static TableCase HostedValid(byte[] section, int typeSpecCount,
        int blockCount, int operationCount, int parameterCount = 3,
        byte[]? parameterTypeIds = null)
        => new(section, typeSpecCount, true, blockCount, operationCount,
            parameterCount, parameterTypeIds, true);

    private static TableCase Valid(byte[] section, int typeSpecCount,
        int blockCount, int operationCount)
        => new(section, typeSpecCount, true, blockCount, operationCount, 0, null, false);

    private static TableCase Rejected(byte[] body)
        => new(EncodeBody(body), 0, false, -1, -1, 0, null, false);

    private static TableCase Rejected(byte[] section, int typeSpecCount)
        => new(section, typeSpecCount, false, -1, -1, 0, null, false);

    private static byte[] EncodeBody(byte[] body)
        => TrRmVariableTableWriter.EncodeBodies([new ReadOnlyMemory<byte>(body)]);

    private static byte[] ChangeSectionByte(byte[] section, int sectionOffset, byte value)
    {
        var changed = (byte[])section.Clone();
        changed[sectionOffset] = value;
        return changed;
    }

    private static byte[] ChangeBodyByte(byte[] body, int bodyOffset, byte value)
    {
        var section = EncodeBody(body);
        section[8 + bodyOffset] = value;
        return section;
    }

    private static byte[] FailureBody(int widthCode)
    {
        var width = widthCode switch { 0 => 1, 1 => 2, _ => 4 };
        var body = new List<byte> { (byte)(0x08 | widthCode) };
        AppendLocal(body, width, 1);
        AppendLocal(body, width, 0);
        AppendLocal(body, width, 0);
        AppendLocal(body, width, 0);
        AppendLocal(body, width, 0);
        AppendLocal(body, width, 0);
        AppendLocal(body, width, 0);
        AppendLocal(body, width, (uint)(1 + width + 2));
        AppendLocal(body, width, (uint)(1 + width));
        body.Add(0);
        AppendLocal(body, width, 0);
        body.AddRange([0x87, 1]);
        return body.ToArray();
    }

    private static IEnumerable<(byte[] Operation, int ResultCount)> IndexedOperations()
    {
        yield return ([1, 1, 1, 42], 1);
        yield return ([2, 1, 1, 42], 1);
        yield return ([3, 1, 1], 1);
        yield return ([4, 1, 1, 1], 1);
        yield return ([5, 1, 1, 1, 1], 1);
        yield return ([6, 1, 1, 1, 1], 1);
        yield return ([7, 1, 1, 1, 1], 1);
        yield return ([8, 1, 1, 1], 1);
        yield return ([9, 1, 1], 1);
        yield return ([0x0A, 1, 1], 1);
        yield return ([0x0B, 1, 1], 1);
        yield return ([0x0C, 1, 1, 1], 1);
        yield return ([0x0D, 1, 1], 1);
        yield return ([0x0E, 1, 1, 1], 1);
        yield return ([0x0F, 1, 1, 1], 1);
        yield return ([0x10, 1, 1], 1);
        yield return ([0x11, 1, 1], 1);
        yield return ([0x12, 1, 1, 0], 1);
        yield return ([0x13, 1, 1, 0], 1);
        yield return ([0x14, 1, 1, 1], 1);
        yield return ([0x15, 1, 1, 1, 1], 1);
        yield return ([0x16, 1, 1, 1], 1);
        yield return ([0x17, 1, 1], 1);
        yield return ([0x18, 1, 2, 1, 1], 2);
        yield return ([0xF0, 0x11, 1, 1, 0, 1, 0, 42, 0], 1);
        yield return ([0xF0, 0x01, 3, 1, 0, 1, 0], 1);
        yield return ([0xF0, 0x04, 0x12, 1, 1, 0, 0], 1);
        yield return ([0xF0, 0x04, 0x13, 1, 1, 0, 0], 1);
        yield return ([0x12, 0, 1, 0], 0);
    }

    private static byte[] IndexedBody(byte[] operation, int resultCount, byte[] terminator)
    {
        var row = new List<byte> { 0x08, 1, (byte)resultCount, 0, 1, 1, 0, 0, 0 };
        for (var value = 0; value < resultCount; value++)
        {
            row.Add(1);
            row.Add(0);
        }

        row.Add(0);
        row.Add((byte)resultCount);
        var definitionsStart = row.Count;
        row.AddRange(Enumerable.Repeat((byte)0, resultCount));
        var firstOffset = row.Count;
        row.Add(0);
        var finalOffset = row.Count;
        row.Add(0);
        var terminatorOffset = row.Count;
        row.Add(0);
        var blockStart = row.Count;
        row.Add(0);
        row.Add((byte)(operation.Length == 0 ? 0 : 1));
        var operationStart = row.Count;
        row.AddRange(operation);
        var relativeTerminatorOffset = row.Count - blockStart;
        row.AddRange(terminator);
        row[firstOffset] = 0;
        row[finalOffset] = (byte)(row.Count - blockStart);
        row[terminatorOffset] = (byte)relativeTerminatorOffset;
        for (var value = 0; value < resultCount; value++)
        {
            row[definitionsStart + value] = (byte)(operationStart - blockStart);
        }

        return row.ToArray();
    }

    internal static byte[] TwoBlockIndexedBody() =>
    [
        0x08, 2, 1, 0, 1,
        1, 0, 0, 0,
        1, 0,
        0, 1, 1,
        2,
        0, 9, 13,
        6, 2,
        0, 1, 1, 1, 1, 42, 0x80, 2, 0,
        0, 0, 0x83, 1,
    ];

    private static byte[] Return(uint value)
        => [0x83, (byte)value];

    private static void AppendLocal(List<byte> body, int width, uint value)
    {
        for (var index = 0; index < width; index++)
        {
            body.Add((byte)(value >> (index * 8)));
        }
    }

    private static async Task<int> ExecuteAotAsync(BoundProgram program, NativeRuntimePolicy policy)
    {
        var path = Path.Combine(TestArtifactPaths.DirectoryPath,
            $"compact-indexed-body-framing-{Guid.NewGuid():N}.exe");
        try
        {
            await File.WriteAllBytesAsync(path, NativeEmitter.Emit(program, policy))
                .ConfigureAwait(true);
            using var process = Process.Start(new ProcessStartInfo(path) {
                UseShellExecute = false,
                CreateNoWindow = true,
            }) ?? throw new InvalidOperationException("Could not start compact-indexed Body framer.");
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

    private sealed record TableCase(byte[] Section, int TypeSpecCount,
        bool ExpectedValid, int ExpectedBlockCount, int ExpectedOperationCount,
        int ParameterCount, byte[]? ParameterTypeIds, bool HasHostedOracle);
}
