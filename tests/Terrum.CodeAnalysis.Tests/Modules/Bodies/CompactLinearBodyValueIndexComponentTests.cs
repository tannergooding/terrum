using System;
using System.Collections.Generic;
using System.Collections.Immutable;
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

public sealed class CompactLinearBodyValueIndexComponentTests
{
    private const string VerificationSource = """
        namespace Terrum.CodeAnalysis;
        int32 Main() {
            int32 count = Host.Expected[0];
            for (mut int32 row = 1; row <= count; row = row + 1) {
                int32 expected = 1 + (row - 1) * 14;
                bool expectedValid = Host.Expected[expected + 2] != 0;
                bool indexed = TryBuildCompactLinearBodyValueIndex(count, row,
                    Host.Expected[expected], Host.Expected[expected + 1]);
                if (indexed != expectedValid) {
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
                int32 valueCount = Host.Expected[expected + 4];
                if (Host.BodyValueDefinitions.Length != valueCount
                    || Host.BodyValueLastUses.Length != valueCount
                    || Host.BodyValueUseBlocks.Length != valueCount) {
                    return 4;
                }
                for (mut int32 value = 0; value < valueCount; value = value + 1) {
                    if (Host.BodyValueDefinitions[value] != Host.Expected[expected + 5 + value]
                        || Host.BodyValueLastUses[value] != Host.Expected[expected + 5 + valueCount + value]
                        || Host.BodyValueUseBlocks[value] != Host.Expected[expected + 5 + 2 * valueCount + value]) {
                        return 5;
                    }
                }
            }
            return 0;
        }
        """;

    private const string ValidatedBatchVerificationSource = """
        namespace Terrum.CodeAnalysis;
        int32 Main() {
            int32 count = Host.Expected[0];
            if (!TryValidateBodyTableIndex(count)) {
                return 1;
            }
            for (mut int32 row = 1; row <= count; row = row + 1) {
                int32 expected = 1 + (row - 1) * 14;
                if (!TryBuildValidatedCompactLinearBodyValueIndex(count, row,
                        Host.Expected[expected], Host.Expected[expected + 1])
                    || Host.BodyValueIndexComplete[0] != 1) {
                    return 2;
                }
                int32 valueCount = Host.Expected[expected + 4];
                if (Host.BodyValueDefinitions.Length != valueCount
                    || Host.BodyValueLastUses.Length != valueCount
                    || Host.BodyValueUseBlocks.Length != valueCount) {
                    return 3;
                }
                for (mut int32 value = 0; value < valueCount; value = value + 1) {
                    if (Host.BodyValueDefinitions[value] != Host.Expected[expected + 5 + value]
                        || Host.BodyValueLastUses[value]
                            != Host.Expected[expected + 5 + valueCount + value]
                        || Host.BodyValueUseBlocks[value]
                            != Host.Expected[expected + 5 + 2 * valueCount + value]) {
                        return 4;
                    }
                }
                if (row == 1
                    && (TryBuildValidatedCompactLinearBodyValueIndex(count, row,
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
    public async Task CompactLinearValueIndexMatchesHostedReader(BoundRuntimePolicy policy)
    {
        var cases = new List<ValueIndexCase>
        {
            new([0x04, 1, 1, 0, 0, 0, 1, 1, 3, 1, 42, 0x83, 3],
                ParameterCount: 2, ExpectedCandidateValid: true, HostedValid: true),
            new([0x04, 1, 1, 0, 0, 0, 1, 3, 3, 1, 0x83, 3],
                ParameterCount: 2, ExpectedCandidateValid: true, HostedValid: true),
            new([0x04, 1, 1, 0, 0, 0, 2, 1, 2, 1, 42, 3, 3, 2, 0x83, 3],
                ParameterCount: 1, ExpectedCandidateValid: true, HostedValid: true),
            new([0x04, 1, 1, 0, 0, 0, 2, 3, 2, 1, 3, 3, 1, 0x83, 3],
                ParameterCount: 1, ExpectedCandidateValid: true, HostedValid: true),
            new([0x04, 1, 1, 0, 0, 0, 1, 4, 3, 1, 1, 0x83, 3],
                ParameterCount: 2, ExpectedCandidateValid: true, HostedValid: true,
                TypeCode: 1),
            new([0x04, 1, 1, 0, 0, 0, 1, 7, 3, 1, 1, 2, 0x83, 3],
                ParameterCount: 2, ExpectedCandidateValid: true, HostedValid: true,
                TypeCode: 1),
            new([0x04, 2, 1, 0, 0, 0, 2, 0, 0, 0, 1, 6, 3, 2, 1, 1, 0x83, 3],
                ParameterCount: 2, ExpectedCandidateValid: true, HostedValid: true,
                TypeSpecCount: 2, TypeCode: 8, SecondTypeCode: 0x0D,
                ResultUsesSecondType: true),
            new([0x04, 2, 1, 0, 0, 0, 2, 0, 0, 0, 1, 8, 3, 2, 1, 0x83, 3],
                ParameterCount: 2, ExpectedCandidateValid: true, HostedValid: true,
                TypeSpecCount: 2, TypeCode: 8, SecondTypeCode: 8,
                ResultUsesSecondType: true, SecondTypeOptional: true),
            new([0x04, 2, 1, 0, 0, 0, 2, 0, 0, 0, 1, 9, 3, 2, 0x83, 3],
                ParameterCount: 2, ExpectedCandidateValid: true, HostedValid: true,
                TypeSpecCount: 2, TypeCode: 1, SecondTypeCode: 1,
                ResultUsesSecondType: true, SecondTypeOptional: true),
            new([0x04, 2, 1, 0, 0, 0, 2, 0, 0, 0, 1, 0x0A, 3, 1, 0x83, 3],
                ParameterCount: 2, ExpectedCandidateValid: true, HostedValid: true,
                TypeSpecCount: 2, TypeCode: 1, SecondTypeCode: 1,
                SecondTypeOptional: true, ParameterUsesSecondType: true),
            new([0x04, 2, 1, 0, 0, 0, 2, 0, 0, 0, 1, 0x16, 3, 2, 1, 0x83, 3],
                ParameterCount: 2, ExpectedCandidateValid: true, HostedValid: true,
                TypeSpecCount: 2, TypeCode: 8, SecondTypeCode: 0x0D,
                ResultUsesSecondType: true),
            new([0x04, 2, 1, 0, 0, 0, 2, 0, 0, 0, 1, 0x1A, 3, 2, 1, 0x83, 3],
                ParameterCount: 2, ExpectedCandidateValid: true, HostedValid: true,
                TypeSpecCount: 2, TypeCode: 0x14, SecondTypeCode: 0x15,
                ResultUsesSecondType: true),
            new([0x04, 2, 1, 0, 0, 0, 2, 0, 0, 0, 1, 0x1B, 3, 2, 1, 0x83, 3],
                ParameterCount: 2, ExpectedCandidateValid: true, HostedValid: true,
                TypeSpecCount: 2, TypeCode: 8, SecondTypeCode: 0x14,
                ResultUsesSecondType: true),
            new([0x04, 1, 1, 0, 0, 0, 1, 0x17, 3, 1, 0x83, 3],
                ParameterCount: 2, ExpectedCandidateValid: true, HostedValid: true),
            new([0x04, 2, 1, 0, 0, 0, 2, 0, 0, 0, 1, 0x19, 3, 1, 1, 2, 0x83, 3],
                ParameterCount: 2, ExpectedCandidateValid: true, HostedValid: true,
                TypeSpecCount: 2, TypeCode: 0x14, SecondTypeCode: 1,
                ResultUsesSecondType: true),
            new([0x04, 1, 1, 0, 0, 0, 1, 0x1C, 3, 1, 0x83, 3],
                ParameterCount: 2, ExpectedCandidateValid: true, HostedValid: true,
                TypeCode: 0x14),
            new([0x04, 1, 1, 0, 0, 0, 1, 0x1D, 3, 1, 1, 2, 0x83, 3],
                ParameterCount: 2, ExpectedCandidateValid: true, HostedValid: true,
                TypeCode: 0x14),
            new([0x04, 2, 1, 0, 0, 0, 2, 0, 0, 0, 1, 0x1E, 3, 2, 1, 0x83, 3],
                ParameterCount: 2, ExpectedCandidateValid: true, HostedValid: true,
                TypeSpecCount: 2, TypeCode: 3, SecondTypeCode: 0x14,
                ResultUsesSecondType: true),
            new([0x04, 2, 1, 0, 0, 0, 2, 0, 0, 0, 1, 0x1F, 3, 2, 1, 0x83, 3],
                ParameterCount: 2, ExpectedCandidateValid: true, HostedValid: true,
                TypeSpecCount: 2, TypeCode: 0x14, SecondTypeCode: 8,
                ResultUsesSecondType: true),
            new([0x04, 1, 1, 0, 0, 0, 0, 0x87, 1],
                ParameterCount: 3, ExpectedCandidateValid: true, HostedValid: true),
            new([0x04, 1, 1, 0, 0, 0, 1, 0xF0, 0x10, 2, 3, 1, 200, 0, 0x83, 3],
                ParameterCount: 2, ExpectedCandidateValid: true, HostedValid: true),
            new([0x04, 1, 1, 0, 0, 0, 1, 0xF0, 1, 3, 3, 0, 1, 0, 0x83, 3],
                ParameterCount: 2, ExpectedCandidateValid: true, HostedValid: true),
            new([0x04, 1, 1, 0, 0, 0, 1, 5, 3, 1, 1, 2, 0x83, 3],
                ParameterCount: 2, ExpectedCandidateValid: true, HostedValid: true),
            new([0x04, 1, 1, 0, 0, 0, 1, 0x15, 3, 1, 1, 2, 0x83, 3],
                ParameterCount: 2, ExpectedCandidateValid: true, HostedValid: true),
            new([0x04, 1, 1, 0, 0, 0, 1, 0x18, 2, 3, 1, 1, 0x83, 3],
                ParameterCount: 1, ExpectedCandidateValid: true, HostedValid: true),
            new([0x04, 1, 1, 0, 0, 0, 1, 0x12, 3, 0, 2, 0, 0, 0x16, 2, 1, 2, 0x83, 3],
                ParameterCount: 2, ExpectedCandidateValid: true, HostedValid: true,
                SecondMethod: true, PackedTarget: true),
            new([0x04, 1, 1, 0, 0, 0, 1,
                    0xF0, 0x0A, 0x12, 3, 0, 0, 0, 0, 0, 0, 0,
                    2, 0, 0, 0x16, 2, 0, 0, 0,
                    1, 0, 0, 0, 2, 0, 0, 0,
                    0xF0, 0x02, 0x83, 3, 0, 0, 0],
                ParameterCount: 2, ExpectedCandidateValid: true, HostedValid: true,
                SecondMethod: true, PackedTarget: true),
            new([0x04, 1, 1, 0, 0, 0, 1, 3, 3, 3, 0x83, 3],
                ParameterCount: 2, ExpectedCandidateValid: false, HostedValid: false),
            new([0x04, 1, 1, 0, 0, 0, 1, 0x18, 2, 4, 1, 1, 0x83, 4],
                ParameterCount: 1, ExpectedCandidateValid: false, HostedValid: false),
            new([0x04, 1, 1, 0, 0, 0, 1, 0x18, 2, 3, 2, 1, 0x83, 3],
                ParameterCount: 1, ExpectedCandidateValid: false, HostedValid: false),
            new([0x04, 1, 1, 0, 0, 0, 1, 3, 4, 1, 0x83, 4],
                ParameterCount: 2, ExpectedCandidateValid: false, HostedValid: false),
            new([0x04, 1, 1, 0, 0, 0, 1, 0x12, 3, 2, 1, 3, 0x83, 3],
                ParameterCount: 2, ExpectedCandidateValid: false, HostedValid: false),
        };

        foreach (var referenceWidth in new[] { 1, 2, 4 })
        {
            foreach (var countWidth in new[] { 1, 2, 4 })
            {
                cases.Add(new(BuildLocalCallBody(referenceWidth, countWidth),
                    ParameterCount: 2, ExpectedCandidateValid: true, HostedValid: true,
                    SecondMethod: true));
            }
        }

        var bodyRows = TrRmVariableTableWriter.EncodeBodies(cases
            .Select(static testCase => new ReadOnlyMemory<byte>(testCase.Row)).ToArray());
        var expected = new List<int> { cases.Count };
        foreach (var testCase in cases)
        {
            expected.Add(testCase.ParameterCount);
            expected.Add(testCase.TypeSpecCount);
            expected.Add(testCase.ExpectedCandidateValid ? 1 : 0);
            expected.Add(testCase.HostedValid ? 1 : 0);
            expected.Add(3);

            if (testCase.HostedValid)
            {
                var index = InspectHosted(testCase);
                Assert.That(index.ValueCount, Is.EqualTo(3));
                expected.AddRange(index.Definitions);
                expected.AddRange(index.LastUses);
                expected.AddRange(index.UseBlocks);
            }
            else
            {
                Assert.Throws<InvalidDataException>(() =>
                    TrRmCompactLinearBodyReaderTests.Inspect(
                        testCase.Row, testCase.ParameterCount, typeCode: testCase.TypeCode,
                        secondTypeCode: testCase.SecondTypeCode,
                        resultUsesSecondType: testCase.ResultUsesSecondType,
                        secondTypeOptional: testCase.SecondTypeOptional,
                        parameterUsesSecondType: testCase.ParameterUsesSecondType));
                expected.AddRange(Enumerable.Repeat(0, 9));
            }
        }

        var compilation = await CreateCompilationAsync(
        [
            new HostBuffer("Rows", PrimitiveType.Int32, [], false),
            new HostBuffer("BodyRows", PrimitiveType.Int32,
                [.. bodyRows.Select(static value => (int)value)], false),
            new HostBuffer("Expected", PrimitiveType.Int32, [.. expected], false),
            new HostBuffer("BodyValueIndexComplete", PrimitiveType.Int32, [0], true),
            new HostBuffer("BodyValueDefinitions", PrimitiveType.Int32, [0, 0, 0], true),
            new HostBuffer("BodyValueLastUses", PrimitiveType.Int32, [0, 0, 0], true),
            new HostBuffer("BodyValueUseBlocks", PrimitiveType.Int32, [0, 0, 0], true),
        ], policy).ConfigureAwait(true);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual0 = compilation.Program; Assert.That(__nunitActual0, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual0!; }))();
        Assert.That(NativeJit.Execute(program, (NativeRuntimePolicy)policy), Is.EqualTo(0));
        Assert.That(await ExecuteAotAsync(program, (NativeRuntimePolicy)policy)
            .ConfigureAwait(true), Is.EqualTo(0));
    }
    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
    public async Task CompactLinearValueIndexReusesValidatedBodyIndexForBatch(
        BoundRuntimePolicy policy)
    {
        const int recordCount = 512;
        var fixture = new ValueIndexCase(
            [0x04, 1, 1, 0, 0, 0, 1, 1, 3, 1, 42, 0x83, 3],
            ParameterCount: 2, ExpectedCandidateValid: true, HostedValid: true);
        var hosted = InspectHosted(fixture);
        var bodyRows = TrRmVariableTableWriter.EncodeBodies(Enumerable.Repeat(
            new ReadOnlyMemory<byte>(fixture.Row), recordCount).ToArray());
        var expected = new List<int> { recordCount };
        for (var row = 0; row < recordCount; row++)
        {
            expected.AddRange([
                fixture.ParameterCount, fixture.TypeSpecCount, 1, 1, hosted.ValueCount,
            ]);
            expected.AddRange(hosted.Definitions);
            expected.AddRange(hosted.LastUses);
            expected.AddRange(hosted.UseBlocks);
        }

        var buffers = ImmutableArray.Create(
            new HostBuffer("Rows", PrimitiveType.Int32, [], false),
            new HostBuffer("BodyRows", PrimitiveType.Int32,
                [.. bodyRows.Select(static value => (int)value)], false),
            new HostBuffer("Expected", PrimitiveType.Int32, [.. expected], false),
            new HostBuffer("BodyValueIndexComplete", PrimitiveType.Int32, [0], true),
            new HostBuffer("BodyValueDefinitions", PrimitiveType.Int32,
                [.. Enumerable.Repeat(0, hosted.ValueCount)], true),
            new HostBuffer("BodyValueLastUses", PrimitiveType.Int32,
                [.. Enumerable.Repeat(0, hosted.ValueCount)], true),
            new HostBuffer("BodyValueUseBlocks", PrimitiveType.Int32,
                [.. Enumerable.Repeat(0, hosted.ValueCount)], true));
        var compilation = await CreateCompilationAsync(
            buffers, policy, ValidatedBatchVerificationSource).ConfigureAwait(true);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual1 = compilation.Program; Assert.That(__nunitActual1, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual1!; }))();
        Assert.That(NativeJit.Execute(program, (NativeRuntimePolicy)policy), Is.EqualTo(0));
        Assert.That(await ExecuteAotAsync(program, (NativeRuntimePolicy)policy)
            .ConfigureAwait(true), Is.EqualTo(0));
    }
    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
    public async Task CompactLinearVoidCallDoesNotDefineAValue(BoundRuntimePolicy policy)
    {
        byte[] row = [0x04, 0, 1, 0x12, 0, 2, 0, 0x83, 0];
        var testCase = new ValueIndexCase(row, ParameterCount: 0,
            ExpectedCandidateValid: true, HostedValid: true,
            TypeSpecCount: 0, TypeCode: 0, SecondMethod: true);
        var hosted = InspectHosted(testCase);
        Assert.That(hosted.Operations, Is.EqualTo(1));
        Assert.That(hosted.Definitions, Is.Empty);

        var bodyRows = TrRmVariableTableWriter.EncodeBodies([
            new ReadOnlyMemory<byte>(row),
        ]);
        var compilation = await CreateCompilationAsync(
        [
            new HostBuffer("Rows", PrimitiveType.Int32, [], false),
            new HostBuffer("BodyRows", PrimitiveType.Int32,
                [.. bodyRows.Select(static value => (int)value)], false),
            new HostBuffer("Expected", PrimitiveType.Int32, [1, 0, 0, 1, 1, 0], false),
            new HostBuffer("BodyValueIndexComplete", PrimitiveType.Int32, [0], true),
            new HostBuffer("BodyValueDefinitions", PrimitiveType.Int32, [], true),
            new HostBuffer("BodyValueLastUses", PrimitiveType.Int32, [], true),
            new HostBuffer("BodyValueUseBlocks", PrimitiveType.Int32, [], true),
        ], policy).ConfigureAwait(true);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual2 = compilation.Program; Assert.That(__nunitActual2, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual2!; }))();
        Assert.That(NativeJit.Execute(program, (NativeRuntimePolicy)policy), Is.EqualTo(0));
        Assert.That(await ExecuteAotAsync(program, (NativeRuntimePolicy)policy)
            .ConfigureAwait(true), Is.EqualTo(0));
    }
    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
    public async Task CompactLinearMultipleDivRemResultsRemainSequential(
        BoundRuntimePolicy policy)
    {
        byte[] row =
        [
            0x04, 1, 1, 0, 0, 0, 2,
            0x18, 3, 4, 1, 2,
            0x18, 5, 6, 3, 4,
            0x83, 6,
        ];
        var hosted = InspectHosted(new ValueIndexCase(row, ParameterCount: 2,
            ExpectedCandidateValid: true, HostedValid: true));
        Assert.That(hosted.ValueCount, Is.EqualTo(6));

        var bodyRows = TrRmVariableTableWriter.EncodeBodies([
            new ReadOnlyMemory<byte>(row),
        ]);
        var expected = new List<int> { 1, 2, 1, 1, 1, hosted.ValueCount };
        expected.AddRange(hosted.Definitions);
        expected.AddRange(hosted.LastUses);
        expected.AddRange(hosted.UseBlocks);
        var compilation = await CreateCompilationAsync(
        [
            new HostBuffer("Rows", PrimitiveType.Int32, [], false),
            new HostBuffer("BodyRows", PrimitiveType.Int32,
                [.. bodyRows.Select(static value => (int)value)], false),
            new HostBuffer("Expected", PrimitiveType.Int32, [.. expected], false),
            new HostBuffer("BodyValueIndexComplete", PrimitiveType.Int32, [0], true),
            new HostBuffer("BodyValueDefinitions", PrimitiveType.Int32,
                [0, 0, 0, 0, 0, 0], true),
            new HostBuffer("BodyValueLastUses", PrimitiveType.Int32,
                [0, 0, 0, 0, 0, 0], true),
            new HostBuffer("BodyValueUseBlocks", PrimitiveType.Int32,
                [0, 0, 0, 0, 0, 0], true),
        ], policy).ConfigureAwait(true);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual3 = compilation.Program; Assert.That(__nunitActual3, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual3!; }))();
        Assert.That(NativeJit.Execute(program, (NativeRuntimePolicy)policy), Is.EqualTo(0));
        Assert.That(await ExecuteAotAsync(program, (NativeRuntimePolicy)policy)
            .ConfigureAwait(true), Is.EqualTo(0));
    }

    private static (int Operations, int ValueCount, int[] Definitions, int[] LastUses,
        int[] UseBlocks, byte[] TypeCodes, bool[] Optional) InspectHosted(ValueIndexCase testCase)
    {
        try
        {
            return TrRmCompactLinearBodyReaderTests.Inspect(
                testCase.Row, testCase.ParameterCount, typeCode: testCase.TypeCode,
                secondTypeCode: testCase.SecondTypeCode,
                resultUsesSecondType: testCase.ResultUsesSecondType,
                secondTypeOptional: testCase.SecondTypeOptional,
                parameterUsesSecondType: testCase.ParameterUsesSecondType,
                secondMethod: testCase.SecondMethod, packedTarget: testCase.PackedTarget,
                expectedTarget: testCase.ExpectedTarget);
        }
        catch (InvalidDataException exception)
        {
            throw new InvalidDataException(
                $"Hosted reader rejected Body {Convert.ToHexString(testCase.Row)}.", exception);
        }
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Terrum.slnx")))
        {
            directory = directory.Parent;
        }
        return directory?.FullName ?? throw new DirectoryNotFoundException(
            "Could not locate Terrum.slnx.");
    }

    private static byte[] BuildLocalCallBody(int referenceWidth, int countWidth)
    {
        var row = new List<byte> { 0x04, 1, 1, 0, 0, 0, 1 };
        if (referenceWidth == 1 && countWidth == 1)
        {
            row.Add(0x12);
        }
        else
        {
            row.Add(0xF0);
            row.Add((byte)(WidthCode(referenceWidth) | (WidthCode(countWidth) << 2)));
            row.Add(0x12);
        }

        AppendWord(row, 3, referenceWidth);
        AppendWord(row, 2, referenceWidth);
        AppendWord(row, 2, countWidth);
        AppendWord(row, 1, referenceWidth);
        AppendWord(row, 2, referenceWidth);
        if (referenceWidth == 1)
        {
            row.Add(0x83);
        }
        else
        {
            row.Add(0xF0);
            row.Add((byte)WidthCode(referenceWidth));
            row.Add(0x83);
        }
        AppendWord(row, 3, referenceWidth);
        return row.ToArray();
    }

    private static void AppendWord(List<byte> bytes, uint value, int width)
    {
        for (var index = 0; index < width; index++)
        {
            bytes.Add((byte)(value >> (index * 8)));
        }
    }

    private static int WidthCode(int width)
        => width switch {
            1 => 0,
            2 => 1,
            4 => 2,
            _ => throw new ArgumentOutOfRangeException(nameof(width)),
        };

    private static async Task<Compilation> CreateCompilationAsync(
        ImmutableArray<HostBuffer> hostBuffers, BoundRuntimePolicy policy,
        string verificationSource = VerificationSource)
    {
        var root = RepositoryRoot();
        var components = new List<SyntaxTree>();
        foreach (var name in new[] {
            "sources/Terrum.CodeAnalysis.Terrum/Modules/VariableTableIndex.terrum",
            "tests/Terrum.CodeAnalysis.Terrum.Tests/Modules/BodyTableValidation.terrum",
            "sources/Terrum.CodeAnalysis.Terrum/Modules/CompactLinearBodyFraming.terrum",
            "sources/Terrum.CodeAnalysis.Terrum/Modules/CompactLinearBodyValueIndex.terrum",
        })
        {
            var path = TerrumProjectPaths.RepositoryFile(root, name);
            var tree = SyntaxTree.Parse(await File.ReadAllTextAsync(path).ConfigureAwait(true), path);
            Assert.That(tree.HasErrors, Is.False, string.Join(Environment.NewLine, tree.Diagnostics));
            components.Add(tree);
        }
        components.Add(SyntaxTree.Parse(verificationSource,
            "compact-linear-body-value-index.terrum"));
        return Compilation.Create(components, hostBuffers, policy);
    }

    private static async Task<int> ExecuteAotAsync(
        BoundProgram program, NativeRuntimePolicy policy)
    {
        var path = Path.Combine(TestArtifactPaths.DirectoryPath,
            $"compact-linear-body-value-index-{Guid.NewGuid():N}.exe");
        try
        {
            await File.WriteAllBytesAsync(path, NativeEmitter.Emit(program, policy))
                .ConfigureAwait(true);
            using var process = Process.Start(new ProcessStartInfo(path) {
                UseShellExecute = false,
                CreateNoWindow = true,
            }) ?? throw new InvalidOperationException("Failed to start native executable.");
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15))
                .ConfigureAwait(true);
            return process.ExitCode;
        }
        finally
        {
            await TestFileCleanup.DeleteWithRetryAsync(path).ConfigureAwait(true);
        }
    }

    private sealed record ValueIndexCase(
        byte[] Row, int ParameterCount, bool ExpectedCandidateValid, bool HostedValid,
        int TypeSpecCount = 1, byte TypeCode = 8, byte? SecondTypeCode = null,
        bool ResultUsesSecondType = false, bool SecondTypeOptional = false,
        bool ParameterUsesSecondType = false, bool SecondMethod = false,
        bool PackedTarget = false, bool ExpectedTarget = false);
}
