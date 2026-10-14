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
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests;

public sealed class CompactIndexedBodyValueIndexComponentTests
{
    private const string VerificationSource = """
        namespace Terrum.CodeAnalysis;
        int32 Main() {
            int32 stride = 3 + Host.BodyBlockValueStarts.Length
                + 3 * Host.BodyValueDefinitions.Length;
            for (mut int32 row = 1; row <= Host.Expected[0]; row = row + 1) {
                int32 expected = 1 + (row - 1) * stride;
                Host.BodyValueIndexComplete[0] = 37;
                bool indexed = TryBuildCompactIndexedBodyValueIndex(Host.Expected[0],
                    row, Host.Expected[expected], Host.Expected[expected + 1]);
                if (indexed != (Host.Expected[expected + 2] != 0)) {
                    return 1;
                }
                if (!indexed) {
                    if (Host.BodyValueIndexComplete[0] != 0) {
                        return 2;
                    }
                    continue;
                }
                if (Host.BodyValueIndexComplete[0] != 1) {
                    return 3;
                }
                mut int32 position = expected + 3;
                for (mut int32 block = 0; block < Host.BodyBlockValueStarts.Length;
                        block = block + 1) {
                    if (Host.BodyBlockValueStarts[block] != Host.Expected[position]) {
                        return 4;
                    }
                    position = position + 1;
                }
                for (mut int32 value = 0; value < Host.BodyValueDefinitions.Length;
                        value = value + 1) {
                    if (Host.BodyValueDefinitions[value] != Host.Expected[position + value]
                        || Host.BodyValueOwners[value] != Host.Expected[position
                            + Host.BodyValueDefinitions.Length + value]
                        || Host.BodyValueUseBlocks[value] != Host.Expected[position
                            + 2 * Host.BodyValueDefinitions.Length + value]) {
                        return 5;
                    }
                }
                for (mut int32 block = 0; block < Host.BodyBlockRegions.Length;
                        block = block + 1) {
                    if (Host.BodyBlockRegions[block] < 0
                        || Host.BodyBlockRegions[block] >= Host.BodyRegionBackedges.Length) {
                        return 6;
                    }
                    if (block == 0) {
                        if (Host.BodyBlockImmediateDominators[block] != 0
                            || Host.BodyBlockDominatorDepths[block] != 0) {
                            return 10;
                        }
                    } else if (block == 1) {
                        if (Host.BodyBlockImmediateDominators[block] != 1
                            || Host.BodyBlockDominatorDepths[block] != 0) {
                            return 10;
                        }
                    } else if (Host.BodyBlockImmediateDominators[block] <= 0
                        || Host.BodyBlockImmediateDominators[block] >= block
                        || Host.BodyBlockDominatorDepths[block]
                            != Host.BodyBlockDominatorDepths[
                                Host.BodyBlockImmediateDominators[block]] + 1) {
                        return 10;
                    }
                    if (block == 0
                        ? Host.BodyBlockReachable[block] != 0
                        : Host.BodyBlockReachable[block] != 1) {
                        return 9;
                    }
                }
                for (mut int32 region = 0; region < Host.BodyRegionBackedges.Length;
                        region = region + 1) {
                    if (Host.BodyRegionBackedges[region] != 0
                        && Host.BodyRegionBackedges[region] != 1) {
                        return 7;
                    }
                    if (Host.BodyRegionDepths[region] < 0
                        || Host.BodyRegionDepths[region] >= Host.BodyRegionDepths.Length) {
                        return 8;
                    }
                }
            }
            return 0;
        }
        """;

    private const string ValidatedBatchVerificationSource = """
        namespace Terrum.CodeAnalysis;
        int32 Main() {
            int32 recordCount = Host.Expected[0];
            if (!TryValidateBodyTableIndex(recordCount)) {
                return 1;
            }
            int32 stride = 3 + Host.BodyBlockValueStarts.Length
                + 3 * Host.BodyValueDefinitions.Length;
            for (mut int32 row = 1; row <= recordCount; row = row + 1) {
                int32 expected = 1 + (row - 1) * stride;
                if (!TryBuildValidatedCompactIndexedBodyValueIndex(recordCount,
                        row, Host.Expected[expected], Host.Expected[expected + 1])
                    || Host.BodyValueIndexComplete[0] != 1) {
                    return 2;
                }
                mut int32 position = expected + 3;
                for (mut int32 block = 0; block < Host.BodyBlockValueStarts.Length;
                        block = block + 1) {
                    if (Host.BodyBlockValueStarts[block] != Host.Expected[position]) {
                        return 3;
                    }
                    position = position + 1;
                }
                for (mut int32 value = 0; value < Host.BodyValueDefinitions.Length;
                        value = value + 1) {
                    if (Host.BodyValueDefinitions[value] != Host.Expected[position + value]
                        || Host.BodyValueOwners[value] != Host.Expected[position
                            + Host.BodyValueDefinitions.Length + value]
                        || Host.BodyValueUseBlocks[value] != Host.Expected[position
                            + 2 * Host.BodyValueDefinitions.Length + value]) {
                        return 4;
                    }
                }
                if (row == 1
                    && (TryBuildValidatedCompactIndexedBodyValueIndex(recordCount, row,
                            -1, Host.Expected[expected + 1])
                        || Host.BodyValueIndexComplete[0] != 0)) {
                    return 5;
                }
            }
            return 0;
        }
        """;
    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
    public async Task CompactIndexedValueIndexMatchesHostedReader(BoundRuntimePolicy policy)
    {
        var cases = new List<ValueIndexCase>();
        cases.Add(DominatedCrossBlockValueUse());
        cases.Add(DominatedCrossBlockValueUseAcrossBranchMerge());
        foreach (var width in new[] { 1, 2, 4 })
        {
            cases.Add(DivRemBlocks(width));
            cases.Add(DivRemBlocks(width, repeatedOperand: true));
            cases.Add(ConditionalBlocks(width, useSwitch: false));
            cases.Add(ConditionalBlocks(width, useSwitch: true));
            cases.Add(ConditionalBlocks(width, useSwitch: true, wideTerminator: true));
            cases.Add(EmptyBlocks(width, voidCall: false));
            cases.Add(EmptyBlocks(width, voidCall: true));
        }
        foreach (var referenceWidth in new[] { 1, 2, 4 })
        {
            foreach (var countWidth in new[] { 1, 2, 4 })
            {
                cases.Add(CallBlocks(referenceWidth, countWidth, packed: false));
                cases.Add(CallBlocks(referenceWidth, countWidth, packed: true));
                cases.Add(EmptyBlocks(4, voidCall: true, referenceWidth, countWidth));
            }
        }
        cases.Add(new(TrRmBodyExampleTests.IndexedScalarLoop(), 1, [2]));
        cases.Add(new(TrRmBodyExampleTests.IndexedScalarLoop(entryHeader: true), 1, [2]));
        cases.Add(new(TrRmBodyExampleTests.IndexedScalarLoop(siblingLoop: true), 1, [2]));
        cases.Add(new(TrRmBodyExampleTests.IndexedNestedLoop(), 1, [1],
            ResultTypeSpec: 1, TypeSpecCount: 1));
        cases.Add(new(TrRmBodyExampleTests.ReadExample(
            "#### Compact indexed conditional merge"), 3, [1, 2, 2]));
        cases.Add(DominatedCrossBlockValueUse());
        foreach (var (operation, parameters, result) in new (byte[], byte[], byte)[] {
            ([1, 1, 2, 42], [], 2),
            ([2, 1, 2, 0xFF], [], 2),
            ([3, 2, 1], [2], 2),
            ([4, 2, 2, 1], [2], 2),
            ([5, 3, 1, 1, 2], [2, 2], 2),
            ([6, 2, 2, 1, 1], [2], 2),
            ([7, 4, 1, 2, 3], [1, 2, 2], 2),
            ([8, 2, 3, 1], [2], 3),
            ([9, 1, 3], [], 3),
            ([0x0A, 2, 1], [3], 1),
            ([0x15, 3, 1, 1, 2], [2, 2], 2),
            ([0x16, 2, 3, 1], [2], 3),
            ([0x17, 2, 1], [2], 2),
            ([0xF0, 0x30, 1, 1, 2, 42, 0, 0, 0, 0, 0, 0, 0], [], 2),
            ([0xF0, 2, 3, 2, 0, 0, 0, 1, 0, 0, 0], [2], 2),
        })
        {
            var optional = operation[0] is 8 or 9 or 0x0A;
            cases.Add(new(TrRmBodyExampleTests.IndexedScalarOperation(
                operation, parameters, result), parameters.Length, parameters,
                ResultTypeSpec: result, TypeSpecCount: result == 3 || parameters.Contains((byte)3)
                    ? 3 : 2, ThirdTypeCode: optional ? null : (byte)0x0D,
                ThirdTypeOptional: optional));
        }

        await ExecuteCasesAsync(cases, policy).ConfigureAwait(true);
    }
    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
    public async Task CompactIndexedValueIndexRejectsValueUsesNotDominatedByTheirOwner(
        BoundRuntimePolicy policy)
    {
        var bypassedOwner = new ValueIndexCase(
            BuildBody(1, [1], [1, 1], [0, 1, 2, 2, 2],
            [
                new([1], [], [2], [(1, 0)], [],
                    [0x81, 1, 2, 1, 1, 3, 0]),
                new([2], [], [2], [], [], [0x80, 4, 0]),
                new([], [2], [2], [], [], [0x80, 4, 0]),
                new([], [2], [], [(2, 0)], [], [0x83, 2]),
            ]),
            1, [1], ResultTypeSpec: 1, TypeSpecCount: 1, ExpectedValid: false);

        await ExecuteCasesAsync([bypassedOwner], policy).ConfigureAwait(true);
    }
    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
    public async Task CompactIndexedValueIndexRejectsExternalUsesMissingLiveInDeclarations(
        BoundRuntimePolicy policy)
    {
        await ExecuteCasesAsync(
            [DominatedCrossBlockValueUse(declareLiveIn: false)], policy).ConfigureAwait(true);
    }
    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
    public async Task CompactIndexedValueIndexRejectsSelfSupportingDeadLoopTransit(
        BoundRuntimePolicy policy)
    {
        var cases = new List<ValueIndexCase>();
        foreach (var width in new[] { 1, 2, 4 })
        {
            var valid = LoopTransit(width, includeDeadTransit: false,
                useValueOnIndependentBranch: true);
            cases.Add(valid);
            cases.Add(LoopTransit(width, includeDeadTransit: true,
                useValueOnIndependentBranch: false));
            cases.Add(LoopTransit(width, includeDeadTransit: true,
                useValueOnIndependentBranch: true));
            cases.Add(valid);
        }

        await ExecuteCasesAsync(cases, policy).ConfigureAwait(true);
    }
    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
    public async Task CompactIndexedValueIndexRejectsUnavailableOrUnsortedLiveInsAndLiveOuts(
        BoundRuntimePolicy policy)
    {
        var unavailableLiveIn = new ValueIndexCase(
            BuildBody(1, [1], [1, 1], [0, 1, 2, 2],
            [
                new([1], [], [1], [(1, 0)], [], [0x81, 1, 2, 1, 1, 3, 0]),
                new([2], [], [], [(2, 0)], [], [0x83, 2]),
                new([], [2], [], [(1, 0)], [], [0x83, 1]),
            ]),
            1, [1], ResultTypeSpec: 1, TypeSpecCount: 1, ExpectedValid: false);
        var unsortedLiveIns = new ValueIndexCase(
            BuildBody(1, [1], [1, 1], [0, 2, 2],
            [
                new([1, 2], [], [1, 2], [], [], [0x80, 2, 0]),
                new([], [2, 1], [], [(1, 0)], [], [0x83, 1]),
            ]),
            2, [1, 1], ResultTypeSpec: 1, TypeSpecCount: 1, ExpectedValid: false);
        var unavailableLiveOut = new ValueIndexCase(
            BuildBody(1, [1], [1, 1], [0, 1, 2, 2],
            [
                new([1], [], [1], [(1, 0)], [], [0x81, 1, 2, 1, 1, 3, 0]),
                new([2], [], [], [(2, 0)], [], [0x83, 2]),
                new([], [], [2], [(1, 0)], [], [0x83, 1]),
            ]),
            1, [1], ResultTypeSpec: 1, TypeSpecCount: 1, ExpectedValid: false);
        var unsortedLiveOuts = new ValueIndexCase(
            BuildBody(1, [1], [1, 1], [0, 2, 2],
            [
                new([1, 2], [], [2, 1], [], [], [0x80, 2, 0]),
                new([], [1, 2], [], [(1, 0)], [], [0x83, 1]),
            ]),
            2, [1, 1], ResultTypeSpec: 1, TypeSpecCount: 1, ExpectedValid: false);
        var returnWithLiveOut = new ValueIndexCase(
            BuildBody(1, [1], [1], [0, 1],
            [
                new([1], [], [1], [(1, 0)], [], [0x83, 1]),
            ]),
            1, [1], ResultTypeSpec: 1, TypeSpecCount: 1, ExpectedValid: false);
        var failWithLiveOut = new ValueIndexCase(
            BuildBody(1, [1], [1], [0, 1],
            [
                new([1], [], [1], [], [], [0x87, 1]),
            ]),
            1, [1], ResultTypeSpec: 1, TypeSpecCount: 1, ExpectedValid: false);
        var missingEdgeLiveOut = new ValueIndexCase(
            BuildBody(1, [1], [1, 1], [0, 1, 2],
            [
                new([1], [], [], [(1, 0)], [], [0x80, 2, 1, 1]),
                new([2], [], [], [(2, 0)], [], [0x83, 2]),
            ]),
            1, [1], ResultTypeSpec: 1, TypeSpecCount: 1, ExpectedValid: false);
        var extraEdgeLiveOut = new ValueIndexCase(
            BuildBody(1, [1], [1], [0, 1, 1],
            [
                new([1], [], [1], [], [], [0x80, 2, 0]),
                new([], [], [], [], [], [0x87, 1]),
            ]),
            1, [1], ResultTypeSpec: 1, TypeSpecCount: 1, ExpectedValid: false);
        var missingTransitLiveIn = new ValueIndexCase(
            BuildBody(1, [1], [1], [0, 1, 1, 1],
            [
                new([1], [], [], [], [], [0x80, 2, 0]),
                new([], [], [1], [], [], [0x80, 3, 0]),
                new([], [1], [], [(1, 0)], [], [0x83, 1]),
            ]),
            1, [1], ResultTypeSpec: 1, TypeSpecCount: 1, ExpectedValid: false);
        var unusedLiveIn = new ValueIndexCase(
            BuildBody(1, [1], [1], [0, 1, 1],
            [
                new([1], [], [1], [], [], [0x80, 2, 0]),
                new([], [1], [], [], [], [0x87, 1]),
            ]),
            1, [1], ResultTypeSpec: 1, TypeSpecCount: 1, ExpectedValid: false);

        await ExecuteCasesAsync(
            [unavailableLiveIn, unsortedLiveIns, unavailableLiveOut, unsortedLiveOuts,
                returnWithLiveOut, failWithLiveOut, missingEdgeLiveOut, extraEdgeLiveOut,
                missingTransitLiveIn, unusedLiveIn],
            policy).ConfigureAwait(true);
    }
    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
    public async Task CompactIndexedValueIndexReusesValidatedBodyIndexForBatch(
        BoundRuntimePolicy policy)
    {
        const int recordCount = 512;
        var fixture = DivRemBlocks(1);
        var facts = DecodeFacts(fixture.Section);
        var rowBytes = fixture.Section.AsMemory(8).ToArray();
        var section = TrRmVariableTableWriter.EncodeBodies(
            Enumerable.Repeat(rowBytes, recordCount)
                .Select(static row => new ReadOnlyMemory<byte>(row)).ToArray());
        var expected = new List<int> { recordCount };
        for (var row = 0; row < recordCount; row++)
        {
            expected.AddRange([fixture.ParameterCount, fixture.TypeSpecCount, 1]);
            expected.AddRange(facts.Starts);
            expected.AddRange(facts.Definitions);
            expected.AddRange(facts.Owners);
            expected.AddRange(facts.UseBlocks);
        }

        var sources = await LoadComponentsAsync().ConfigureAwait(true);
        sources.Add(SyntaxTree.Parse(ValidatedBatchVerificationSource,
            "indexed-value-validated-batch-verification.terrum"));
        await ExecuteAsync(sources,
            CreateBuffers(section, expected, facts, fixture), policy).ConfigureAwait(true);
    }
    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
    public async Task CompactIndexedValueIndexRejectsHostedValueRuleViolations(
        BoundRuntimePolicy policy)
    {
        var valid = DivRemBlocks(1);
        var facts = DecodeFacts(valid.Section);
        var firstBlock = facts.BlockDataStart;
        var secondBlock = firstBlock + facts.Offsets[1];
        var operation = firstBlock + facts.Definitions[2];
        var secondOperation = secondBlock + facts.Definitions[5];
        var cases = new List<ValueIndexCase> { valid };
        void Reject(int position, byte value)
        {
            var section = (byte[])valid.Section.Clone();
            section[position] = value;
            cases.Add(valid with { Section = section, ExpectedValid = false });
        }

        Reject(operation + 1, 4); // Nonsequential first result.
        Reject(operation + 2, 5); // DivRem's second result is in another block.
        Reject(operation + 3, 3); // Use of this operation's own result.
        Reject(operation + 3, 5); // Use owned by a later block.
        Reject(operation + 3, 0); // Vacant operand.
        Reject(secondOperation + 2, 6); // Later-block self use.
        Reject(secondOperation + 2, 7); // Out-of-range operand.
        Reject(facts.DescriptorsStart + 1, 0); // Descriptor undercounts used blocks.
        Reject(facts.DescriptorsStart + 2 * 2 + 1, 1); // Unused result claims a use.
        Reject(facts.DescriptorsStart, 0); // Vacant descriptor type.
        Reject(facts.DescriptorsStart, 3); // Descriptor type outside the map.
        Reject(facts.DefinitionsStart, (byte)facts.Definitions[2]); // Parameter not sentinel.
        Reject(facts.DefinitionsStart + 2, 0xFF); // Result claims to be a parameter.
        Reject(facts.DefinitionsStart + 3, (byte)(facts.Definitions[3] + 1));
        Reject(firstBlock + 2, 2); // Parameter list no longer starts at value one.
        // Header: presence, parameter count/list, live-out count/list, last-use count/pairs.
        Reject(firstBlock + 8, 1); // Incorrect observed last-use ordinal.
        Reject(firstBlock + 9, 1); // Duplicate/unsorted last-use key.
        Reject(firstBlock + 9, 3); // Missing actual use and a ghost declared use.
        Reject(secondBlock + 5, 1); // Failure after the first block was indexed.

        var truncated = valid.Section[..^1];
        BinaryPrimitives.WriteUInt32LittleEndian(truncated.AsSpan(4),
            (uint)(truncated.Length - 8));
        cases.Add(valid with { Section = truncated, ExpectedValid = false });
        var badIndex = (byte[])valid.Section.Clone();
        badIndex[0] = 1;
        cases.Add(valid with { Section = badIndex, ExpectedValid = false });
        cases.Add(valid); // Reuse after failed/partially written attempts.
        cases.Add(ConditionalBlocks(1, useSwitch: false, missingLastUse: true)
            with { ExpectedValid = false });
        cases.Add(UnsupportedOperation());
        var call = CallBlocks(4, 4, packed: false);
        var callFacts = DecodeFacts(call.Section);
        var badCall = (byte[])call.Section.Clone();
        var callArgument = callFacts.BlockDataStart + callFacts.Definitions[1]
            + 3 + 4 + 4 + 4;
        BinaryPrimitives.WriteUInt32LittleEndian(badCall.AsSpan(callArgument), 2);
        cases.Add(call with { Section = (byte[])badCall.Clone(), ExpectedValid = false });
        BinaryPrimitives.WriteUInt32LittleEndian(badCall.AsSpan(callArgument), uint.MaxValue);
        cases.Add(call with { Section = (byte[])badCall.Clone(), ExpectedValid = false });

        await ExecuteCasesAsync(cases, policy).ConfigureAwait(true);
    }
    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
    public async Task CompactIndexedValueIndexChecksAcceptedRegionBoundaries(
        BoundRuntimePolicy policy)
    {
        var cases = new List<ValueIndexCase>();
        foreach (var useSwitch in new[] { false, true })
        {
            var valid = RegionControlFlow(useSwitch);
            var facts = DecodeFacts(valid.Section);
            cases.Add(valid);

            var skippedEntry = (byte[])valid.Section.Clone();
            var firstTerminator = facts.BlockDataStart + facts.Offsets[0]
                + facts.Terminators[0];
            skippedEntry[firstTerminator + (useSwitch ? 4 : 2)] = 3;
            cases.Add(valid with { Section = skippedEntry, ExpectedValid = false });

            var unreachableRegion = (byte[])valid.Section.Clone();
            unreachableRegion[firstTerminator + (useSwitch ? 4 : 2)] = 4;
            cases.Add(valid with { Section = unreachableRegion, ExpectedValid = false });

            var skippedExit = (byte[])valid.Section.Clone();
            var thirdTerminator = facts.BlockDataStart + facts.Offsets[2]
                + facts.Terminators[2];
            skippedExit[thirdTerminator + 1] = 1;
            cases.Add(valid with { Section = skippedExit, ExpectedValid = false });

            var wrongHeader = (byte[])valid.Section.Clone();
            wrongHeader[facts.RegionStart + 2] = 3;
            cases.Add(valid with { Section = wrongHeader, ExpectedValid = false });

            var unsupportedKind = (byte[])valid.Section.Clone();
            unsupportedKind[facts.RegionStart] = 2;
            cases.Add(valid with { Section = unsupportedKind, ExpectedValid = false });

            var cleanup = (byte[])valid.Section.Clone();
            cleanup[facts.RegionStart + 4] = 4;
            cases.Add(valid with { Section = cleanup, ExpectedValid = false });
        }

        var loop = new ValueIndexCase(TrRmBodyExampleTests.IndexedScalarLoop(),
            1, [2]);
        var loopFacts = DecodeFacts(loop.Section);
        cases.Add(loop);
        var missingBackedge = (byte[])loop.Section.Clone();
        var lastTerminator = loopFacts.BlockDataStart + loopFacts.Offsets[2]
            + loopFacts.Terminators[2];
        missingBackedge[lastTerminator + 1] = 4;
        cases.Add(loop with { Section = missingBackedge, ExpectedValid = false });

        var conditional = ConditionalBlocks(1, useSwitch: false);
        var conditionalFacts = DecodeFacts(conditional.Section);
        var conditionalTerminator = conditionalFacts.BlockDataStart
            + conditionalFacts.Offsets[0] + conditionalFacts.Terminators[0];
        var unreachableBlock = (byte[])conditional.Section.Clone();
        unreachableBlock[conditionalTerminator + 4] = 2;
        cases.Add(conditional with { Section = unreachableBlock, ExpectedValid = false });

        await ExecuteCasesAsync(cases, policy).ConfigureAwait(true);
    }
    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
    public async Task CompactIndexedValueIndexRequiresExactBuffersAndValidArguments(
        BoundRuntimePolicy policy)
    {
        var testCase = DivRemBlocks(4);
        Assert.That(InspectHosted(testCase), Is.EqualTo((2, 6)));
        var facts = DecodeFacts(testCase.Section);
        var exact = CreateBuffers(testCase.Section, [], facts, testCase);
        var sources = await LoadComponentsAsync().ConfigureAwait(true);
        var rejection = SyntaxTree.Parse("""
            namespace Terrum.CodeAnalysis;
            int32 Main() {
                if (TryBuildCompactIndexedBodyValueIndex(1, 1, 2, 2)) {
                    return 1;
                }
                for (mut int32 marker = 0; marker < Host.BodyValueIndexComplete.Length;
                        marker = marker + 1) {
                    if (Host.BodyValueIndexComplete[marker]
                            != (Host.BodyValueIndexComplete.Length == 1 ? 0 : 37)) {
                        return 2;
                    }
                }
                for (mut int32 value = 0; value < Host.BodyValueDefinitions.Length;
                        value = value + 1) {
                    if (Host.BodyValueDefinitions[value] != 37) {
                        return 3;
                    }
                }
                return 0;
            }
            """, "indexed-value-buffer-rejection.terrum");
        foreach (var name in new[] {
            "BodyValueIndexComplete", "BodyBlockRegions", "BodyBlockReachable",
            "BodyBlockImmediateDominators", "BodyBlockDominatorDepths",
            "BodyRegionDepths",
            "BodyRegionBackedges",
            "BodyBlockValueStarts", "BodyValueDefinitions",
            "BodyValueOwners", "BodyValueUseBlocks", "BodyValueLiveInBlocks",
            "BodyValueLastUses",
            "BodyValueDeclaredLastUses",
        })
        {
            foreach (var adjustment in new[] { -1, 1 })
            {
                var buffers = exact.Select(buffer => buffer.Name == name
                    ? new HostBuffer(name, PrimitiveType.Int32,
                        [.. Enumerable.Repeat(37, buffer.Values.Length + adjustment)], true)
                    : buffer).ToArray();
                await ExecuteAsync([.. sources, rejection], buffers, policy).ConfigureAwait(true);
            }
        }
        var invalidArguments = SyntaxTree.Parse("""
            namespace Terrum.CodeAnalysis;
            int32 Main() {
                for (mut int32 test = 0; test < 12; test = test + 1) {
                    Host.BodyValueIndexComplete[0] = 37;
                    int32 count = test == 0 ? -1 : test == 1 ? 16777216 : test == 2 ? 2 : 1;
                    int32 row = test == 3 ? 0 : test == 4 ? 2 : test == 5 ? -1 : 1;
                    int32 parameters = test == 6 ? -1 : test == 7 ? 16777216
                        : test == 8 ? 7 : test == 9 ? 1 : 2;
                    int32 types = test == 10 ? -1 : test == 11 ? 1 : 2;
                    if (TryBuildCompactIndexedBodyValueIndex(count, row, parameters, types)
                        || Host.BodyValueIndexComplete[0] != 0) {
                        return 1;
                    }
                }
                return 0;
            }
            """, "indexed-value-argument-rejection.terrum");
        await ExecuteAsync([.. sources, invalidArguments], exact, policy).ConfigureAwait(true);
    }
    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
    public async Task CompactIndexedValueIndexChecksSelectedOperationTypeRelationsButNotGeneralSemantics(
        BoundRuntimePolicy policy)
    {
        var typedResultMismatch = new ValueIndexCase(
            TrRmBodyExampleTests.IndexedScalarOperation([1, 1, 2, 42], [], 1),
            0, [], ResultTypeSpec: 1, ExpectedValid: false);
        Assert.Throws<InvalidDataException>(() => InspectHosted(typedResultMismatch));

        var unaryResultMismatch = new ValueIndexCase(
            TrRmBodyExampleTests.IndexedScalarOperation([4, 2, 3, 1], [2], 1),
            1, [2], ResultTypeSpec: 1, ExpectedValid: false);
        Assert.Throws<InvalidDataException>(() => InspectHosted(unaryResultMismatch));

        var bitNotBoolean = new ValueIndexCase(
            TrRmBodyExampleTests.IndexedScalarOperation([4, 2, 2, 1], [1], 1),
            1, [1], ResultTypeSpec: 1, ExpectedValid: false);
        var validBooleanNot = new ValueIndexCase(
            TrRmBodyExampleTests.IndexedScalarOperation([4, 2, 1, 1], [1], 1),
            1, [1], ResultTypeSpec: 1);
        var boolNotInteger = new ValueIndexCase(
            TrRmBodyExampleTests.IndexedScalarOperation([4, 2, 1, 1], [2], 2),
            1, [2], ResultTypeSpec: 2, ExpectedValid: false);
        var checkedNegateUnsigned = new ValueIndexCase(
            TrRmBodyExampleTests.IndexedScalarOperation([0x17, 2, 1], [2], 2),
            1, [2], ResultTypeSpec: 2, ExpectedValid: false, SecondTypeCode: 0x0D);
        Assert.Throws<InvalidDataException>(() => InspectHosted(bitNotBoolean));
        Assert.That(InspectHosted(validBooleanNot), Is.EqualTo((1, 2)));
        Assert.Throws<InvalidDataException>(() => InspectHosted(boolNotInteger));
        Assert.Throws<InvalidDataException>(() => InspectHosted(checkedNegateUnsigned));

        var binaryOperandMismatch = new ValueIndexCase(
            TrRmBodyExampleTests.IndexedScalarOperation([5, 3, 10, 1, 2], [2, 3], 2),
            2, [2, 3], ResultTypeSpec: 2, TypeSpecCount: 3,
            ExpectedValid: false, ThirdTypeCode: 0x0D);
        Assert.Throws<InvalidDataException>(() => InspectHosted(binaryOperandMismatch));

        var binaryResultMismatch = new ValueIndexCase(
            TrRmBodyExampleTests.IndexedScalarOperation([5, 3, 10, 1, 2], [2, 2], 3),
            2, [2, 2], ResultTypeSpec: 2, TypeSpecCount: 3,
            ExpectedValid: false, ThirdTypeCode: 0x0D);
        Assert.Throws<InvalidDataException>(() => InspectHosted(binaryResultMismatch));

        var selectAlternativeMismatch = new ValueIndexCase(
            TrRmBodyExampleTests.IndexedScalarOperation([7, 4, 1, 2, 3], [1, 2, 3], 2),
            3, [1, 2, 3], ResultTypeSpec: 2, TypeSpecCount: 3,
            ExpectedValid: false, ThirdTypeCode: 0x0D);
        Assert.Throws<InvalidDataException>(() => InspectHosted(selectAlternativeMismatch));

        var checkedBinaryOperandMismatch = new ValueIndexCase(
            TrRmBodyExampleTests.IndexedScalarOperation([0x15, 3, 1, 1, 2], [2, 3], 2),
            2, [2, 3], ResultTypeSpec: 2, TypeSpecCount: 3,
            ExpectedValid: false, ThirdTypeCode: 0x0D);
        Assert.Throws<InvalidDataException>(() => InspectHosted(checkedBinaryOperandMismatch));

        var checkedBinaryResultMismatch = new ValueIndexCase(
            TrRmBodyExampleTests.IndexedScalarOperation([0x15, 3, 1, 1, 2], [2, 2], 3),
            2, [2, 2], ResultTypeSpec: 2, TypeSpecCount: 3,
            ExpectedValid: false, ThirdTypeCode: 0x0D);
        Assert.Throws<InvalidDataException>(() => InspectHosted(checkedBinaryResultMismatch));

        var checkedShiftResultMismatch = new ValueIndexCase(
            TrRmBodyExampleTests.IndexedScalarOperation([0x15, 3, 6, 1, 2], [2, 3], 3),
            2, [2, 3], ResultTypeSpec: 2, TypeSpecCount: 3,
            ExpectedValid: false, ThirdTypeCode: 0x0D);
        Assert.Throws<InvalidDataException>(() => InspectHosted(checkedShiftResultMismatch));

        var validComparison = new ValueIndexCase(
            TrRmBodyExampleTests.IndexedScalarOperation([5, 3, 4, 1, 2], [2, 2], 1),
            2, [2, 2], ResultTypeSpec: 1);
        Assert.That(InspectHosted(validComparison), Is.EqualTo((1, 3)));

        var validCheckedShift = new ValueIndexCase(
            TrRmBodyExampleTests.IndexedScalarOperation([0x15, 3, 6, 1, 2], [2, 3], 2),
            2, [2, 3], ResultTypeSpec: 2, TypeSpecCount: 3, ThirdTypeCode: 0x0D);
        Assert.That(InspectHosted(validCheckedShift), Is.EqualTo((1, 3)));

        var comparisonResultMismatch = new ValueIndexCase(
            TrRmBodyExampleTests.IndexedScalarOperation([5, 3, 4, 1, 2], [2, 2], 2),
            2, [2, 2], ResultTypeSpec: 2, ExpectedValid: false);
        var invalidBooleanConstant = new ValueIndexCase(
            TrRmBodyExampleTests.IndexedScalarOperation([1, 1, 1, 2], [], 1),
            0, [], ResultTypeSpec: 1, ExpectedValid: false);
        var invalidSignedBooleanConstant = new ValueIndexCase(
            TrRmBodyExampleTests.IndexedScalarOperation([2, 1, 1, 1], [], 1),
            0, [], ResultTypeSpec: 1, ExpectedValid: false);
        var invalidUnsignedNegativeConstant = new ValueIndexCase(
            TrRmBodyExampleTests.IndexedScalarOperation([2, 1, 2, 0xFF], [], 2),
            0, [], ResultTypeSpec: 2, ExpectedValid: false, SecondTypeCode: 0x0D);
        var validUnsignedSignedConstant = new ValueIndexCase(
            TrRmBodyExampleTests.IndexedScalarOperation([2, 1, 2, 127], [], 2),
            0, [], ResultTypeSpec: 2, SecondTypeCode: 0x0D);
        var wideInt32Minimum = new ValueIndexCase(
            TrRmBodyExampleTests.IndexedScalarOperation(
                [0xF0, 0x30, 2, 1, 2, 0, 0, 0, 0x80, 0xFF, 0xFF, 0xFF, 0xFF],
                [], 2), 0, [], ResultTypeSpec: 2);
        var wideInt32AboveMaximum = new ValueIndexCase(
            TrRmBodyExampleTests.IndexedScalarOperation(
                [0xF0, 0x30, 2, 1, 2, 0, 0, 0, 0x80, 0, 0, 0, 0],
                [], 2), 0, [], ResultTypeSpec: 2, ExpectedValid: false);
        var wideInt32BelowMinimum = new ValueIndexCase(
            TrRmBodyExampleTests.IndexedScalarOperation(
                [0xF0, 0x30, 2, 1, 2, 0xFF, 0xFF, 0xFF, 0x7F, 0xFF, 0xFF, 0xFF, 0xFF],
                [], 2), 0, [], ResultTypeSpec: 2, ExpectedValid: false);
        var wideUnsignedMaximum = new ValueIndexCase(
            TrRmBodyExampleTests.IndexedScalarOperation(
                [0xF0, 0x30, 2, 1, 2, 0xFF, 0xFF, 0xFF, 0xFF, 0, 0, 0, 0],
                [], 2), 0, [], ResultTypeSpec: 2, SecondTypeCode: 0x0D);
        var wideUnsignedAboveMaximum = new ValueIndexCase(
            TrRmBodyExampleTests.IndexedScalarOperation(
                [0xF0, 0x30, 2, 1, 2, 0, 0, 0, 0, 1, 0, 0, 0],
                [], 2), 0, [], ResultTypeSpec: 2, ExpectedValid: false,
            SecondTypeCode: 0x0D);
        var float32Bits = new ValueIndexCase(
            TrRmBodyExampleTests.IndexedScalarOperation(
                [0xF0, 0x20, 1, 1, 1, 0, 0, 0xC0, 0x7F], [], 1),
            0, [], ResultTypeSpec: 1, TypeSpecCount: 2, FirstTypeCode: 0x14);
        var float32HighBits = new ValueIndexCase(
            TrRmBodyExampleTests.IndexedScalarOperation(
                [0xF0, 0x30, 1, 1, 1, 0, 0, 0, 0, 1, 0, 0, 0], [], 1),
            0, [], ResultTypeSpec: 1, TypeSpecCount: 2,
            FirstTypeCode: 0x14, ExpectedValid: false);
        var float64Bits = new ValueIndexCase(
            TrRmBodyExampleTests.IndexedScalarOperation(
                [0xF0, 0x30, 1, 1, 1, 0xFF, 0xFF, 0xFF, 0xFF,
                    0xFF, 0xFF, 0xFF, 0xFF], [], 1),
            0, [], ResultTypeSpec: 1, TypeSpecCount: 2, FirstTypeCode: 0x15);
        var selectConditionMismatch = new ValueIndexCase(
            TrRmBodyExampleTests.IndexedScalarOperation([7, 4, 1, 2, 3], [1, 2, 2], 2),
            3, [1, 2, 2], ResultTypeSpec: 2, TypeSpecCount: 3,
            ExpectedValid: false, FirstTypeCode: 8, ThirdTypeCode: 0x0D);
        var checkedShiftCountMismatch = new ValueIndexCase(
            TrRmBodyExampleTests.IndexedScalarOperation([0x15, 3, 6, 1, 2], [2, 3], 2),
            2, [2, 3], ResultTypeSpec: 2, TypeSpecCount: 3,
            ExpectedValid: false, ThirdTypeCode: 9);
        Assert.Throws<InvalidDataException>(() => InspectHosted(comparisonResultMismatch));
        Assert.Throws<InvalidDataException>(() => InspectHosted(invalidBooleanConstant));
        Assert.Throws<InvalidDataException>(() => InspectHosted(invalidSignedBooleanConstant));
        Assert.Throws<InvalidDataException>(() => InspectHosted(invalidUnsignedNegativeConstant));
        Assert.That(InspectHosted(validUnsignedSignedConstant), Is.EqualTo((1, 1)));
        Assert.That(InspectHosted(wideInt32Minimum), Is.EqualTo((1, 1)));
        Assert.Throws<InvalidDataException>(() => InspectHosted(wideInt32AboveMaximum));
        Assert.Throws<InvalidDataException>(() => InspectHosted(wideInt32BelowMinimum));
        Assert.That(InspectHosted(wideUnsignedMaximum), Is.EqualTo((1, 1)));
        Assert.Throws<InvalidDataException>(() => InspectHosted(wideUnsignedAboveMaximum));
        Assert.That(InspectHosted(float32Bits), Is.EqualTo((1, 1)));
        Assert.Throws<InvalidDataException>(() => InspectHosted(float32HighBits));
        Assert.That(InspectHosted(float64Bits), Is.EqualTo((1, 1)));
        Assert.Throws<InvalidDataException>(() => InspectHosted(selectConditionMismatch));
        Assert.Throws<InvalidDataException>(() => InspectHosted(checkedShiftCountMismatch));

        var branchConditionMismatch = ConditionalBlocks(1, useSwitch: false)
            with { FirstTypeCode = 8, ExpectedValid = false };
        var switchSelectorMismatch = ConditionalBlocks(1, useSwitch: true)
            with { FirstTypeCode = 0x14, ExpectedValid = false };
        var switchPatternMismatch = ConditionalBlocks(1, useSwitch: true, switchPattern: 2)
            with { ExpectedValid = false };
        Assert.Throws<InvalidDataException>(() => InspectHosted(branchConditionMismatch));
        Assert.Throws<InvalidDataException>(() => InspectHosted(switchSelectorMismatch));
        Assert.Throws<InvalidDataException>(() => InspectHosted(switchPatternMismatch));

        var checkedBooleanOperand = new ValueIndexCase(
            TrRmBodyExampleTests.IndexedScalarOperation([0x15, 3, 1, 1, 2], [2, 2], 2),
            2, [2, 2], ResultTypeSpec: 2, ExpectedValid: false,
            FirstTypeCode: 8, SecondTypeCode: 1);
        Assert.Throws<InvalidDataException>(() => InspectHosted(checkedBooleanOperand));

        var validDivRem = DivRemBlocks(1);
        var divRemFacts = DecodeFacts(validDivRem.Section);
        ValueIndexCase DivRemWithMismatchedType(int value)
        {
            var section = (byte[])validDivRem.Section.Clone();
            section[divRemFacts.DescriptorsStart + (value - 1) * 2] = 1;
            return validDivRem with { Section = section, ExpectedValid = false };
        }
        var divRemOperandMismatch = DivRemWithMismatchedType(2);
        var divRemQuotientMismatch = DivRemWithMismatchedType(3);
        var divRemRemainderMismatch = DivRemWithMismatchedType(4);
        Assert.Throws<InvalidDataException>(() => InspectHosted(divRemOperandMismatch));
        Assert.Throws<InvalidDataException>(() => InspectHosted(divRemQuotientMismatch));
        Assert.Throws<InvalidDataException>(() => InspectHosted(divRemRemainderMismatch));
        var divRemBooleanOperand = validDivRem with {
            FirstTypeCode = 8,
            SecondTypeCode = 1,
            ExpectedValid = false,
        };
        Assert.Throws<InvalidDataException>(() => InspectHosted(divRemBooleanOperand));

        var typed = TrRmBodyExampleTests.IndexedScalarOperation([3, 2, 1], [2], 1);
        var copyTypeMismatch = new ValueIndexCase(
            typed, 1, [2], ResultTypeSpec: 1, ExpectedValid: false);
        Assert.Throws<InvalidDataException>(() => InspectHosted(copyTypeMismatch));

        var loop = TrRmBodyExampleTests.IndexedScalarLoop(ghostLiveness: true);
        var ghostLiveness = new ValueIndexCase(loop, 1, [2], ExpectedValid: false);
        Assert.Throws<InvalidDataException>(() => InspectHosted(ghostLiveness));
        var call = CallBlocks(1, 1, packed: false);
        var facts = DecodeFacts(call.Section);
        var badTarget = (byte[])call.Section.Clone();
        badTarget[facts.BlockDataStart + facts.Definitions[1] + 2] = 3;
        var targetMismatch = call with { Section = badTarget };
        Assert.Throws<InvalidDataException>(() => InspectHosted(targetMismatch));
        await ExecuteCasesAsync(
            [typedResultMismatch, copyTypeMismatch, unaryResultMismatch,
                bitNotBoolean, validBooleanNot, boolNotInteger, checkedNegateUnsigned,
                binaryOperandMismatch, binaryResultMismatch,
                selectAlternativeMismatch, checkedBinaryOperandMismatch,
                checkedBinaryResultMismatch, checkedShiftResultMismatch,
                validComparison, validCheckedShift, comparisonResultMismatch,
                invalidBooleanConstant, invalidSignedBooleanConstant,
                invalidUnsignedNegativeConstant, validUnsignedSignedConstant,
                wideInt32Minimum, wideInt32AboveMaximum, wideInt32BelowMinimum,
                wideUnsignedMaximum, wideUnsignedAboveMaximum,
                float32Bits, float32HighBits, float64Bits,
                selectConditionMismatch, checkedShiftCountMismatch,
                branchConditionMismatch, switchSelectorMismatch, switchPatternMismatch,
                checkedBooleanOperand, divRemOperandMismatch,
                divRemQuotientMismatch, divRemRemainderMismatch, divRemBooleanOperand,
                ghostLiveness, targetMismatch],
            policy, validateHosted: false).ConfigureAwait(true);
    }
    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
    public async Task CompactIndexedValueIndexChecksConversionsAndOptionalScalarTypes(
        BoundRuntimePolicy policy)
    {
        var cases = new ValueIndexCase[] {
            new(TrRmBodyExampleTests.IndexedScalarOperation(
                [6, 2, 3, 1, 1], [2], 3), 1, [2],
                ResultTypeSpec: 3, TypeSpecCount: 3, ThirdTypeCode: 0x0D),
            new(TrRmBodyExampleTests.IndexedScalarOperation(
                [6, 2, 3, 2, 1], [2], 3), 1, [2],
                ResultTypeSpec: 3, TypeSpecCount: 3, ThirdTypeCode: 0x0D),
            new(TrRmBodyExampleTests.IndexedScalarOperation(
                [6, 2, 3, 3, 1], [2], 3), 1, [2],
                ResultTypeSpec: 3, TypeSpecCount: 3, ThirdTypeCode: 0x0D),
            new(TrRmBodyExampleTests.IndexedScalarOperation(
                [6, 2, 3, 3, 1], [2], 3), 1, [2],
                ResultTypeSpec: 3, TypeSpecCount: 3, ThirdTypeCode: 0x0D,
                SecondTypeCode: 9, ExpectedValid: false),
            new(TrRmBodyExampleTests.IndexedScalarOperation(
                [6, 2, 3, 1, 1], [2], 3), 1, [2],
                ResultTypeSpec: 3, TypeSpecCount: 3, ThirdTypeOptional: true,
                ExpectedValid: false),
            new(TrRmBodyExampleTests.IndexedScalarOperation(
                [6, 2, 2, 1, 1], [3], 2), 1, [3],
                ResultTypeSpec: 2, TypeSpecCount: 3, ThirdTypeOptional: true,
                ExpectedValid: false),
            new(TrRmBodyExampleTests.IndexedScalarOperation(
                [0x16, 2, 3, 1], [2], 3), 1, [2],
                ResultTypeSpec: 3, TypeSpecCount: 3, ThirdTypeCode: 3),
            new(TrRmBodyExampleTests.IndexedScalarOperation(
                [0x16, 2, 2, 1], [1], 2), 1, [1],
                ResultTypeSpec: 2, TypeSpecCount: 2, ExpectedValid: false),
            new(TrRmBodyExampleTests.IndexedScalarOperation(
                [0x16, 2, 2, 1], [3], 2), 1, [3],
                ResultTypeSpec: 2, TypeSpecCount: 3, ThirdTypeOptional: true,
                ExpectedValid: false),
            new(TrRmBodyExampleTests.IndexedScalarOperation(
                [8, 2, 3, 1], [2], 3), 1, [2],
                ResultTypeSpec: 3, TypeSpecCount: 3, ThirdTypeOptional: true),
            new(TrRmBodyExampleTests.IndexedScalarOperation(
                [8, 2, 3, 1], [1], 3), 1, [1],
                ResultTypeSpec: 3, TypeSpecCount: 3, ThirdTypeOptional: true,
                ExpectedValid: false),
            new(TrRmBodyExampleTests.IndexedScalarOperation(
                [8, 2, 2, 1], [2], 2), 1, [2],
                ResultTypeSpec: 2, TypeSpecCount: 2, ExpectedValid: false),
            new(TrRmBodyExampleTests.IndexedScalarOperation(
                [9, 1, 3], [], 3), 0, [],
                ResultTypeSpec: 3, TypeSpecCount: 3, ThirdTypeOptional: true),
            new(TrRmBodyExampleTests.IndexedScalarOperation(
                [9, 1, 2], [], 2), 0, [],
                ResultTypeSpec: 2, TypeSpecCount: 2, ExpectedValid: false),
            new(TrRmBodyExampleTests.IndexedScalarOperation(
                [0x0A, 2, 1], [3], 1), 1, [3],
                ResultTypeSpec: 1, TypeSpecCount: 3, ThirdTypeOptional: true),
            new(TrRmBodyExampleTests.IndexedScalarOperation(
                [0x0A, 2, 1], [2], 1), 1, [2],
                ResultTypeSpec: 1, TypeSpecCount: 2, ExpectedValid: false),
            new(TrRmBodyExampleTests.IndexedScalarOperation(
                [0x0A, 2, 1], [3], 2), 1, [3],
                ResultTypeSpec: 2, TypeSpecCount: 3, ThirdTypeOptional: true,
                ExpectedValid: false),
            new(TrRmBodyExampleTests.IndexedScalarOperation(
                [0x0A, 2, 1], [3], 1), 1, [3],
                ResultTypeSpec: 1, TypeSpecCount: 3, ThirdTypeOptional: true,
                SecondTypeCode: 0x14, ExpectedValid: false),
        };

        await ExecuteCasesAsync(cases, policy).ConfigureAwait(true);
    }

    private static async Task ExecuteCasesAsync(IEnumerable<ValueIndexCase> cases,
        BoundRuntimePolicy policy, bool validateHosted = true)
    {
        var sources = await LoadComponentsAsync().ConfigureAwait(true);
        sources.Add(SyntaxTree.Parse(VerificationSource, "indexed-value-verification.terrum"));
        foreach (var group in cases.GroupBy(testCase => {
            var facts = DecodeFacts(testCase.Section);
            return (facts.Starts.Length, facts.Definitions.Length, testCase.TypeSpecCount,
                testCase.FirstTypeCode, testCase.SecondTypeCode, testCase.ThirdTypeCode,
                testCase.ThirdTypeOptional);
        }))
        {
            var fixtures = group.ToArray();
            var expected = new List<int> { fixtures.Length };
            foreach (var fixture in fixtures)
            {
                var facts = DecodeFacts(fixture.Section);
                if (validateHosted)
                {
                    if (fixture.ExpectedValid)
                    {
                        Assert.That(InspectHosted(fixture), Is.EqualTo((facts.Starts.Length - 1, facts.Definitions.Length)));
                    }
                    else
                    {
                        Assert.Throws<InvalidDataException>(() => InspectHosted(fixture));
                    }
                }
                expected.AddRange([fixture.ParameterCount, fixture.TypeSpecCount,
                    fixture.ExpectedValid ? 1 : 0]);
                expected.AddRange(facts.Starts);
                expected.AddRange(facts.Definitions);
                expected.AddRange(facts.Owners);
                expected.AddRange(facts.UseBlocks);
            }
            var section = TrRmVariableTableWriter.EncodeBodies(
                fixtures.Select(static fixture => new ReadOnlyMemory<byte>(
                    fixture.Section.AsMemory(8).ToArray())).ToArray());
            // A malformed section index must remain malformed after batching.
            if (fixtures.Any(static fixture => fixture.Section[0] != 0))
            {
                // Run the malformed table separately; it invalidates every row.
                var bad = fixtures.Single(static fixture => fixture.Section[0] != 0);
                var facts = DecodeFacts(bad.Section);
                var rejected = new List<int> { 1, bad.ParameterCount, bad.TypeSpecCount, 0 };
                rejected.AddRange(facts.Starts);
                rejected.AddRange(facts.Definitions);
                rejected.AddRange(facts.Owners);
                rejected.AddRange(facts.UseBlocks);
                await ExecuteAsync(sources, CreateBuffers(bad.Section, rejected, facts, bad),
                    policy).ConfigureAwait(true);
                await ExecuteCasesAsync(fixtures.Where(static fixture => fixture.Section[0] == 0),
                    policy, validateHosted).ConfigureAwait(true);
                continue;
            }
            await ExecuteAsync(sources, CreateBuffers(section, expected,
                DecodeFacts(fixtures[0].Section), fixtures[0]), policy).ConfigureAwait(true);
        }
    }

    private static (int Blocks, int Values) InspectHosted(ValueIndexCase fixture)
        => TrRmBodyExampleTests.InspectIndexed(fixture.Section,
            parameterCount: fixture.ParameterCount, parameterTypeIds: fixture.ParameterTypes,
            resultTypeSpec: fixture.ResultTypeSpec, firstTypeCode: fixture.FirstTypeCode,
            secondTypeCode: fixture.SecondTypeCode,
            thirdTypeCode: fixture.ThirdTypeCode, thirdTypeOptional: fixture.ThirdTypeOptional,
            secondMethod: fixture.SecondMethod, packedTarget: fixture.PackedTarget);

    private static HostBuffer[] CreateBuffers(byte[] section, IEnumerable<int> expected,
        IndexFacts facts, ValueIndexCase fixture)
        =>
        [
            new("Rows", PrimitiveType.Int32, [], false),
            new("BodyRows", PrimitiveType.Int32, [.. section.Select(static value => (int)value)], false),
            new("TypeSpecs", PrimitiveType.Int32, [.. EncodeTypeSpecs(fixture)], false),
            new("Expected", PrimitiveType.Int32, [.. expected], false),
            new("BodyBlockOperationCounts", PrimitiveType.Int32, [], true),
            new("BodyValueIndexComplete", PrimitiveType.Int32, [37], true),
            new("BodyBlockRegions", PrimitiveType.Int32,
                [.. Enumerable.Repeat(37, facts.Starts.Length)], true),
            new("BodyBlockReachable", PrimitiveType.Int32,
                [.. Enumerable.Repeat(37, facts.Starts.Length)], true),
            new("BodyBlockImmediateDominators", PrimitiveType.Int32,
                [.. Enumerable.Repeat(37, facts.Starts.Length)], true),
            new("BodyBlockDominatorDepths", PrimitiveType.Int32,
                [.. Enumerable.Repeat(37, facts.Starts.Length)], true),
            new("BodyRegionDepths", PrimitiveType.Int32,
                [.. Enumerable.Repeat(37, facts.Regions + 1)], true),
            new("BodyRegionBackedges", PrimitiveType.Int32,
                [.. Enumerable.Repeat(37, facts.Regions + 1)], true),
            new("BodyBlockValueStarts", PrimitiveType.Int32,
                [.. Enumerable.Repeat(37, facts.Starts.Length)], true),
            new("BodyValueDefinitions", PrimitiveType.Int32,
                [.. Enumerable.Repeat(37, facts.Definitions.Length)], true),
            new("BodyValueOwners", PrimitiveType.Int32,
                [.. Enumerable.Repeat(37, facts.Definitions.Length)], true),
            new("BodyValueUseBlocks", PrimitiveType.Int32,
                [.. Enumerable.Repeat(37, facts.Definitions.Length)], true),
            new("BodyValueLiveInBlocks", PrimitiveType.Int32,
                [.. Enumerable.Repeat(37, facts.Definitions.Length)], true),
            new("BodyValueLastUses", PrimitiveType.Int32,
                [.. Enumerable.Repeat(37, facts.Definitions.Length)], true),
            new("BodyValueDeclaredLastUses", PrimitiveType.Int32,
                [.. Enumerable.Repeat(37, facts.Definitions.Length)], true),
        ];

    private static byte[] EncodeTypeSpecs(ValueIndexCase fixture)
    {
        if (fixture.TypeSpecCount == 0)
        {
            return [];
        }

        var rows = new List<ReadOnlyMemory<byte>>(fixture.TypeSpecCount) {
            TrRmTypeSpecTableWriter.Builtin(fixture.FirstTypeCode),
        };
        if (fixture.TypeSpecCount >= 2)
        {
            rows.Add(TrRmTypeSpecTableWriter.Builtin(fixture.SecondTypeCode));
        }
        if (fixture.TypeSpecCount >= 3)
        {
            rows.Add(fixture.ThirdTypeOptional
                ? TrRmTypeSpecTableWriter.Wrapped(0x06, 2)
                : TrRmTypeSpecTableWriter.Builtin(fixture.ThirdTypeCode ?? 0x0D));
        }

        return TrRmTypeSpecTableWriter.Encode(rows, 0, 0, 0, 0);
    }

    // The hosted API retains counts, not its temporary definition/use arrays.
    // On hosted success its exact checks certify these encoded value facts;
    // decode the certified indexes independently instead of changing the reader.
    private static IndexFacts DecodeFacts(byte[] section)
    {
        var cursor = 9;
        var width = (section[8] & 3) switch { 0 => 1, 1 => 2, _ => 4 };
        var blocks = (int)ReadWord(section, ref cursor, width);
        var values = (int)ReadWord(section, ref cursor, width);
        var regions = (int)ReadWord(section, ref cursor, width);
        var types = (int)ReadWord(section, ref cursor, width);
        var regionStart = cursor + types * 4;
        cursor += types * 4 + regions * (1 + 4 * width);
        var descriptorsStart = cursor;
        var uses = new int[values];
        for (var value = 0; value < values; value++)
        {
            cursor += width;
            uses[value] = (int)ReadWord(section, ref cursor, width);
        }
        var starts = new int[blocks + 1];
        for (var block = 0; block <= blocks; block++)
        {
            starts[block] = (int)ReadWord(section, ref cursor, width);
        }
        var definitionsStart = cursor;
        var definitions = new int[values];
        var sentinel = width == 4 ? uint.MaxValue : (1u << (8 * width)) - 1;
        for (var value = 0; value < values; value++)
        {
            var definition = ReadWord(section, ref cursor, width);
            definitions[value] = definition == sentinel ? -1 : (int)definition;
        }
        var offsets = new int[blocks + 1];
        for (var block = 0; block <= blocks; block++)
        {
            offsets[block] = (int)ReadWord(section, ref cursor, width);
        }
        var terminators = new int[blocks];
        for (var block = 0; block < blocks; block++)
        {
            terminators[block] = (int)ReadWord(section, ref cursor, width);
        }
        var owners = new int[values];
        for (var block = 1; block <= blocks; block++)
        {
            Array.Fill(owners, block, starts[block - 1], starts[block] - starts[block - 1]);
        }
        return new(starts, definitions, owners, uses, offsets, terminators,
            regions, regionStart, descriptorsStart, definitionsStart, cursor);
    }

    private static ValueIndexCase DivRemBlocks(int width, bool repeatedOperand = false)
    {
        var firstUses = repeatedOperand ? new (int, int)[] { (1, 0), (4, 1) }
            : [(1, 0), (2, 0), (4, 1)];
        var section = BuildBody(width, [1, 2], [2, 2, 2, 2, 2, 2], [0, 4, 6],
        [
            new([1, 2], [], [4], firstUses,
                [new([0x18, 3, 4, 1, repeatedOperand ? (byte)1 : (byte)2], [3, 4])],
                [0x80, 2, 1, 4]),
            new([5], [], [], [(5, 0), (6, 1)], [new([3, 6, 5], [6])], [0x83, 6]),
        ]);
        return new(section, 2, [2, 2]);
    }

    private static ValueIndexCase ConditionalBlocks(int width, bool useSwitch,
        bool wideTerminator = false, bool missingLastUse = false,
        uint switchPattern = 0)
    {
        var terminator = new List<byte>();
        var referenceWidth = wideTerminator ? 4 : 1;
        var countWidth = wideTerminator ? 2 : 1;
        if (wideTerminator)
        {
            terminator.AddRange([0xF0, 0x26, 0x82]);
        }
        else
        {
            terminator.Add(useSwitch ? (byte)0x82 : (byte)0x81);
        }
        AppendWord(terminator, 1, referenceWidth);
        if (useSwitch)
        {
            AppendWord(terminator, 1, countWidth);
            AppendWord(terminator, switchPattern, wideTerminator ? 4 : 1);
        }
        AppendWord(terminator, 2, referenceWidth);
        AppendWord(terminator, 0, countWidth);
        AppendWord(terminator, 3, referenceWidth);
        AppendWord(terminator, 0, countWidth);
        var section = BuildBody(width, [1], [1], [0, 1, 1, 1],
        [
            new([1], [], [1], [(1, 0)], [], [.. terminator]),
            new([], [1], [], [(1, 0)], [], [0x83, 1]),
            new([], [1], [], missingLastUse ? [] : [(1, 0)], [], [0x83, 1]),
        ]);
        return new(section, 1, [1], ResultTypeSpec: 1);
    }

    private static ValueIndexCase EmptyBlocks(int width, bool voidCall,
        int referenceWidth = 1, int countWidth = 1)
    {
        var call = new List<byte>();
        AppendInstruction(call, 0x12, referenceWidth, countWidth);
        AppendWord(call, 0, referenceWidth);
        AppendWord(call, 1, referenceWidth);
        AppendWord(call, 0, countWidth);
        var section = BuildBody(width, [], [], [0, 0, 0],
        [
            new([], [], [], [], voidCall ? [new([.. call], [])] : [],
                [0x80, 2, 0]),
            new([], [], [], [], [], voidCall ? [0x83, 0] : [0x87, 1]),
        ]);
        return new(section, 0, [], ResultTypeSpec: 1, TypeSpecCount: 0, FirstTypeCode: 0);
    }

    private static ValueIndexCase UnsupportedOperation()
    {
        var section = BuildBody(1, [2], [1, 1], [0, 2, 2],
        [
            new([1], [], [], [(1, 0)], [new([0x0B, 2, 1], [2])], [0x80, 2, 0]),
            new([], [], [], [], [], [0x87, 1]),
        ]);
        return new(section, 1, [2], ExpectedValid: false);
    }

    private static ValueIndexCase CallBlocks(int referenceWidth, int countWidth, bool packed)
    {
        var operation = new List<byte>();
        AppendInstruction(operation, 0x12, referenceWidth, countWidth);
        AppendWord(operation, 2, referenceWidth);
        AppendWord(operation, packed ? 0u : 1u, referenceWidth);
        if (packed)
        {
            AppendWord(operation, 0x1600_0002, 4);
        }
        AppendWord(operation, 1, countWidth);
        AppendWord(operation, 1, referenceWidth);
        var branch = new List<byte>();
        AppendInstruction(branch, 0x80, referenceWidth, countWidth);
        AppendWord(branch, 2, referenceWidth);
        AppendWord(branch, 1, countWidth);
        AppendWord(branch, 2, referenceWidth);
        var section = BuildBody(2, [2], [1, 1, 1], [0, 2, 3],
        [
            new([1], [], [2], [(1, 0), (2, 1)], [new([.. operation], [2])], [.. branch]),
            new([3], [], [], [(3, 0)], [], [0x83, 3]),
        ]);
        return new(section, 1, [2], TypeSpecCount: 2,
            SecondMethod: packed, PackedTarget: packed);
    }

    private static ValueIndexCase RegionControlFlow(bool useSwitch)
    {
        var parameterTypeCode = useSwitch ? (byte)8 : (byte)1;
        var terminator = useSwitch
            ? new byte[] { 0x82, 1, 1, 0, 2, 0, 4, 0 }
            : new byte[] { 0x81, 1, 2, 0, 4, 0 };
        var section = BuildBody(1, [1], [1], [0, 1, 1, 1, 1],
        [
            new([1], [], [], [(1, 0)], [], terminator),
            new([], [], [], [], [], [0x80, 3, 0], Region: 1),
            new([], [], [], [], [], [0x80, 4, 0], Region: 1),
            new([], [], [], [], [], [0x83, 0]),
        ], [(6, 0, 2, 4, 0)]);
        return new(section, 1, [1], ResultTypeSpec: 2,
            TypeSpecCount: 2, FirstTypeCode: parameterTypeCode, SecondTypeCode: 0);
    }

    private static ValueIndexCase DominatedCrossBlockValueUse(bool declareLiveIn = true)
    {
        var section = BuildBody(1, [1], [1, 1], [0, 1, 2, 2],
        [
            new([1], [], [1], [(1, 0)], [], [0x80, 2, 1, 1]),
            new([2], [], [2], [], [], [0x80, 3, 0]),
            new([], declareLiveIn ? [2] : [], [], [(2, 0)], [], [0x83, 2]),
        ]);
        return new(section, 1, [1], ResultTypeSpec: 1, TypeSpecCount: 1,
            ExpectedValid: declareLiveIn);
    }

    private static ValueIndexCase LoopTransit(
        int width, bool includeDeadTransit, bool useValueOnIndependentBranch)
    {
        int[] loopValues = includeDeadTransit ? [1, 2] : [1];
        var returnValue = useValueOnIndependentBranch ? 2 : 1;
        var section = BuildBody(width, [1], [1, 1], [0, 2, 2, 2, 2, 2],
        [
            new([1, 2], [], [1, 2], [(1, 0)], [], [0x81, 1, 2, 0, 5, 0]),
            new([], loopValues, loopValues, [(1, 0)], [],
                [0x81, 1, 3, 0, 4, 0], Region: 1),
            new([], loopValues, loopValues, [], [], [0x80, 2, 0], Region: 1),
            new([], [1], [], [(1, 0)], [], [0x83, 1]),
            new([], [returnValue], [], [(returnValue, 0)], [], [0x83, (byte)returnValue]),
        ], [(1, 0, 2, 4, 0)]);
        return new(section, 2, [1, 1], ResultTypeSpec: 1, TypeSpecCount: 1,
            ExpectedValid: !includeDeadTransit);
    }

    private static ValueIndexCase DominatedCrossBlockValueUseAcrossBranchMerge()
    {
        var section = BuildBody(1, [1], [1, 1], [0, 1, 2, 2, 2, 2],
        [
            new([1], [], [1], [(1, 0)], [], [0x80, 2, 1, 1]),
            new([2], [], [2], [(2, 0)], [], [0x81, 2, 3, 0, 4, 0]),
            new([], [2], [2], [], [], [0x80, 5, 0]),
            new([], [2], [2], [], [], [0x80, 5, 0]),
            new([], [2], [], [(2, 0)], [], [0x83, 2]),
        ]);
        return new(section, 1, [1], ResultTypeSpec: 1, TypeSpecCount: 1);
    }

    private static byte[] BuildBody(int width, uint[] mappedTypes, int[] valueTypes,
        int[] starts, Block[] blocks,
        (byte Kind, int Parent, int Entry, int Exit, int Cleanup)[]? regions = null)
    {
        var data = new List<byte>();
        var offsets = new List<uint> { 0 };
        var terminators = new List<uint>();
        var sentinel = width == 4 ? uint.MaxValue : (1u << (8 * width)) - 1;
        var definitions = Enumerable.Repeat(sentinel, valueTypes.Length).ToArray();
        var uses = new uint[valueTypes.Length];
        foreach (var block in blocks)
        {
            var blockStart = data.Count;
            data.Add((byte)((block.Region != 0 ? 1 : 0)
                | (block.Parameters.Length != 0 ? 2 : 0)
                | (block.LiveIns.Length != 0 ? 4 : 0)
                | (block.LiveOuts.Length != 0 ? 8 : 0)
                | (block.LastUses.Length != 0 ? 16 : 0)));
            if (block.Region != 0)
            {
                AppendWord(data, (uint)block.Region, width);
            }
            foreach (var list in new[] { block.Parameters, block.LiveIns, block.LiveOuts })
            {
                if (list.Length != 0)
                {
                    AppendWord(data, (uint)list.Length, width);
                    foreach (var value in list)
                    {
                        AppendWord(data, (uint)value, width);
                    }
                }
            }
            if (block.LastUses.Length != 0)
            {
                AppendWord(data, (uint)block.LastUses.Length, width);
                foreach (var (value, ordinal) in block.LastUses)
                {
                    AppendWord(data, (uint)value, width);
                    AppendWord(data, (uint)ordinal, width);
                    uses[value - 1]++;
                }
            }
            AppendWord(data, (uint)block.Operations.Length, width);
            foreach (var operation in block.Operations)
            {
                foreach (var value in operation.Results)
                {
                    definitions[value - 1] = (uint)(data.Count - blockStart);
                }
                data.AddRange(operation.Bytes);
            }
            terminators.Add((uint)(data.Count - blockStart));
            data.AddRange(block.Terminator);
            offsets.Add((uint)data.Count);
        }
        var body = new List<byte> { (byte)(0x08 | WidthCode(width)) };
        AppendWord(body, (uint)blocks.Length, width);
        AppendWord(body, (uint)valueTypes.Length, width);
        AppendWord(body, (uint)(regions?.Length ?? 0), width);
        AppendWord(body, (uint)mappedTypes.Length, width);
        foreach (var type in mappedTypes)
        {
            AppendWord(body, type, 4);
        }
        foreach (var (kind, parent, entry, exit, cleanup) in regions ?? [])
        {
            body.Add(kind);
            AppendWord(body, (uint)parent, width);
            AppendWord(body, (uint)entry, width);
            AppendWord(body, (uint)exit, width);
            AppendWord(body, (uint)cleanup, width);
        }
        for (var value = 0; value < valueTypes.Length; value++)
        {
            AppendWord(body, (uint)valueTypes[value], width);
            AppendWord(body, uses[value], width);
        }
        foreach (var start in starts)
        {
            AppendWord(body, (uint)start, width);
        }
        foreach (var index in definitions.Concat(offsets).Concat(terminators))
        {
            AppendWord(body, index, width);
        }
        body.AddRange(data);
        return TrRmVariableTableWriter.EncodeBodies([body.ToArray()]);
    }

    private static void AppendInstruction(List<byte> bytes, byte opcode,
        int referenceWidth, int countWidth)
    {
        if (referenceWidth != 1 || countWidth != 1)
        {
            bytes.Add(0xF0);
            bytes.Add((byte)(WidthCode(referenceWidth) | (WidthCode(countWidth) << 2)));
        }
        bytes.Add(opcode);
    }

    private static int WidthCode(int width)
        => width switch { 1 => 0, 2 => 1, 4 => 2, _ => throw new ArgumentOutOfRangeException(nameof(width)) };

    private static void AppendWord(List<byte> bytes, uint value, int width)
    {
        for (var index = 0; index < width; index++)
        {
            bytes.Add((byte)((value >> (index * 8)) & 0xFF));
        }
    }

    private static uint ReadWord(byte[] bytes, ref int cursor, int width)
    {
        uint value = 0;
        for (var index = 0; index < width; index++)
        {
            value |= (uint)bytes[cursor++] << (index * 8);
        }
        return value;
    }

    private static async Task<List<SyntaxTree>> LoadComponentsAsync()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", ".."));
        var sources = new List<SyntaxTree>();
        foreach (var name in new[] {
            "VariableTableIndex.terrum", "BodyTableValidation.terrum",
            "TypeSpecTableValidation.terrum",
            "CompactIndexedBodyFraming.terrum", "CompactIndexedBodyValueIndex.terrum",
        })
        {
            var path = Path.Combine(root, "self-host", name);
            var source = SyntaxTree.Parse(await File.ReadAllTextAsync(path).ConfigureAwait(true), path);
            Assert.That(source.HasErrors, Is.False, string.Join(Environment.NewLine, source.Diagnostics));
            sources.Add(source);
        }
        return sources;
    }

    private static async Task ExecuteAsync(IEnumerable<SyntaxTree> sources,
        HostBuffer[] buffers, BoundRuntimePolicy policy)
    {
        var compilation = Compilation.Create(sources, [.. buffers], policy);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual0 = compilation.Program; Assert.That(__nunitActual0, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual0!; }))();
        Assert.That(NativeJit.Execute(program, (NativeRuntimePolicy)policy), Is.EqualTo(0));
        var path = Path.Combine(Path.GetTempPath(), $"indexed-value-index-{Guid.NewGuid():N}.exe");
        try
        {
            await File.WriteAllBytesAsync(path, NativeEmitter.Emit(program, (NativeRuntimePolicy)policy))
                .ConfigureAwait(true);
            using var process = Process.Start(new ProcessStartInfo(path) {
                UseShellExecute = false,
                CreateNoWindow = true,
            }) ?? throw new InvalidOperationException("Failed to start native executable.");
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(true);
            Assert.That(process.ExitCode, Is.EqualTo(0));
        }
        finally
        {
            await TestFileCleanup.DeleteWithRetryAsync(path).ConfigureAwait(true);
        }
    }

    private sealed record ValueIndexCase(byte[] Section, int ParameterCount,
        byte[] ParameterTypes, int ResultTypeSpec = 2, int TypeSpecCount = 2,
        bool ExpectedValid = true, byte FirstTypeCode = 1, byte SecondTypeCode = 8,
        byte? ThirdTypeCode = null, bool ThirdTypeOptional = false,
        bool SecondMethod = false, bool PackedTarget = false);

    private sealed record IndexFacts(int[] Starts, int[] Definitions, int[] Owners,
        int[] UseBlocks, int[] Offsets, int[] Terminators, int Regions, int RegionStart,
        int DescriptorsStart, int DefinitionsStart, int BlockDataStart);

    private sealed record Operation(byte[] Bytes, int[] Results);

    private sealed record Block(int[] Parameters, int[] LiveIns, int[] LiveOuts,
        (int Value, int Ordinal)[] LastUses, Operation[] Operations, byte[] Terminator,
        int Region = 0);
}
