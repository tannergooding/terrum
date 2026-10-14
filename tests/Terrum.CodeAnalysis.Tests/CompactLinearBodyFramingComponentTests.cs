using System;
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

public sealed class CompactLinearBodyFramingComponentTests
{
    private const string BatchVerificationSource = """
        namespace Terrum.CodeAnalysis;
        int32 Main() {
            int32 recordCount = Host.Expected[0];
            int32 typeSpecCount = Host.Expected[1];
            if (!TryValidateBodyTableIndex(recordCount)) {
                return 1;
            }
            for (mut int32 row = 1; row <= recordCount; row = row + 1) {
                if (!TryGetValidatedCompactLinearBodyFrame(recordCount, row, typeSpecCount,
                    init var blockCount, init var operationCount)
                    || blockCount != 1 || operationCount != 0) {
                    return 2;
                }
            }

            init int32 vacantBlocks;
            init int32 vacantOperations;
            if (TryGetValidatedCompactLinearBodyFrame(recordCount, 0, typeSpecCount,
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
            int32 bodyCount = Host.Expected[0];
            for (mut int32 row = 1; row <= bodyCount; row = row + 1) {
                int32 expected = 1 + (row - 1) * 4;
                bool expectedValid = Host.Expected[expected + 1] != 0;
                if (TryGetCompactLinearBodyFrame(bodyCount, row,
                    Host.Expected[expected], init var blockCount, init var operationCount)) {
                    if (!expectedValid) {
                        return 1;
                    }
                    if (blockCount != Host.Expected[expected + 2]
                        || operationCount != Host.Expected[expected + 3]) {
                        return 2;
                    }
                } else if (expectedValid || blockCount is init || operationCount is init) {
                    return 3;
                }
            }

            init int32 vacantBlocks;
            init int32 vacantOperations;
            if (TryGetCompactLinearBodyFrame(bodyCount, 0, 1,
                init vacantBlocks, init vacantOperations)
                || vacantBlocks is init || vacantOperations is init) {
                return 4;
            }
            return 0;
        }
        """;
    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
    public async Task CompactLinearBodyFramingMatchesHostedReader(BoundRuntimePolicy policy)
    {
        var validCheckedAdd = new byte[] {
            0x04, 1, 1, 0, 0, 0, 3,
            0x15, 3, 1, 1, 2,
            0x02, 4, 1, 3,
            0x15, 5, 1, 3, 4,
            0x83, 5,
        };
        var cases = new List<TableCase> {
            Valid(validCheckedAdd, parameterCount: 2),
            Valid([0x04, 1, 1, 0, 0, 0, 1, 5, 3, 1, 1, 2, 0x83, 3],
                parameterCount: 2),
            Valid([0x04, 1, 1, 0, 0, 0, 1, 1, 1, 1, 42, 0x83, 1],
                parameterCount: 0),
            Valid([0x04, 1, 1, 0, 0, 0, 1, 0xF0, 0x10, 2, 1, 1, 200, 0,
                0x83, 1], parameterCount: 0),
            Valid([0x04, 0, 0, 0x87, 1], parameterCount: 0),
            Valid([0x04, 1, 1, 0, 0, 0, 0, 0x83, 1], parameterCount: 1),
            Valid(ReturnParameterBody(0x05, 2), parameterCount: 1),
            Valid(ReturnParameterBody(0x06, 4), parameterCount: 1),
            Valid([0x04, 1, 1, 0, 0, 0, 1,
                0x12, 2, 2, 1, 1, 0x83, 2],
                parameterCount: 1, secondMethod: true),
            Valid([0x04, 1, 1, 0, 0, 0, 1,
                0xF0, 0x04, 0x12, 2, 2, 1, 0, 1, 0x83, 2],
                parameterCount: 1, secondMethod: true),
            Valid([0x04, 1, 1, 0, 0, 0, 1,
                0xF0, 0x08, 0x12, 2, 2, 1, 0, 0, 0, 1, 0x83, 2],
                parameterCount: 1, secondMethod: true),
            Valid([0x05, 1, 0, 1, 0, 0, 0, 1, 0,
                0xF0, 0x05, 0x12, 2, 0, 2, 0, 1, 0, 1, 0, 0x83, 2],
                parameterCount: 1, secondMethod: true),
            Valid([0x06, 1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0,
                0xF0, 0x02, 0x12,
                2, 0, 0, 0, 2, 0, 0, 0, 1, 1, 0, 0, 0, 0x83, 2],
                parameterCount: 1, secondMethod: true),
            Valid([0x04, 1, 1, 0, 0, 0, 1,
                0x12, 2, 0, 2, 0, 0, 0x16, 1, 1, 0x83, 2],
                parameterCount: 1, secondMethod: true, packedTarget: true),
            Valid([0x05, 1, 0, 1, 0, 0, 0, 1, 0,
                0xF0, 0x05, 0x12, 2, 0, 0, 0, 2, 0, 0, 0x16, 1, 0, 1, 0,
                0x83, 2],
                parameterCount: 1, secondMethod: true, packedTarget: true),
            Valid([0x06, 1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0,
                0xF0, 0x02, 0x12,
                2, 0, 0, 0, 0, 0, 0, 0, 2, 0, 0, 0x16, 1, 1, 0, 0, 0, 0x83, 2],
                parameterCount: 1, secondMethod: true, packedTarget: true),
            Valid([0x04, 2, 1, 0, 0, 0, 2, 0, 0, 0, 1,
                0x16, 2, 2, 1, 0x83, 2], parameterCount: 1,
                typeSpecCount: 2, secondTypeCode: 0x0D, resultUsesSecondType: true),
            Valid([0x04, 2, 1, 0, 0, 0, 2, 0, 0, 0, 1,
                0x19, 3, 1, 1, 2, 0x83, 3], parameterCount: 2,
                typeSpecCount: 2, typeCode: 0x14, secondTypeCode: 1,
                resultUsesSecondType: true),
            Valid([0x04, 2, 1, 0, 0, 0, 2, 0, 0, 0, 1,
                0x1A, 2, 2, 1, 0x83, 2], parameterCount: 1,
                typeSpecCount: 2, typeCode: 0x14, secondTypeCode: 0x15,
                resultUsesSecondType: true),
            Valid([0x04, 2, 1, 0, 0, 0, 2, 0, 0, 0, 1,
                0x1B, 2, 2, 1, 0x83, 2], parameterCount: 1,
                typeSpecCount: 2, typeCode: 8, secondTypeCode: 0x14,
                resultUsesSecondType: true),
            Valid([0x04, 1, 1, 0, 0, 0, 1,
                0x1C, 2, 1, 0x83, 2], parameterCount: 1,
                typeCode: 0x14),
            Valid([0x04, 1, 1, 0, 0, 0, 1,
                0x1D, 3, 1, 1, 2, 0x83, 3], parameterCount: 2,
                typeCode: 0x14),
            Valid([0x05, 1, 0, 1, 0, 0, 0, 1, 0,
                0xF0, 1, 0x1D, 3, 0, 1, 1, 0, 2, 0, 0x83, 3], parameterCount: 2,
                typeCode: 0x14),
            Valid([0x06, 1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0,
                0xF0, 0x02, 0x1D,
                3, 0, 0, 0, 1, 1, 0, 0, 0, 2, 0, 0, 0,
                0x83, 3], parameterCount: 2,
                typeCode: 0x14),
            Valid([0x04, 2, 1, 0, 0, 0, 2, 0, 0, 0, 1,
                0x1E, 2, 2, 1, 0x83, 2], parameterCount: 1,
                typeSpecCount: 2, typeCode: 3, secondTypeCode: 0x14,
                resultUsesSecondType: true),
            Valid([0x04, 2, 1, 0, 0, 0, 2, 0, 0, 0, 1,
                0x1F, 2, 2, 1, 0x83, 2], parameterCount: 1,
                typeSpecCount: 2, typeCode: 0x14, secondTypeCode: 8,
                resultUsesSecondType: true),
            Rejected([0x08, 0, 0, 0x83, 0]),
            Rejected([0x07, 0, 0, 0x87, 1]),
            Rejected([0x06, 255, 255, 255, 255]),
            Rejected([0x06, 0, 0, 0, 0, 255, 255, 255, 255]),
            Rejected([0x04, 1, 0, 0, 0, 0, 0, 0x87, 1]),
            Rejected([0x04, 1, 2, 0, 0, 0, 0, 0x87, 1]),
            Rejected([0x04, 0, 1, 0x0B, 1, 1, 0x83, 0]),
            Rejected([0x04, 1, 1, 0, 0, 0, 0, 1, 1, 1, 42, 0x83, 1]),
            Rejected([0x04, 1, 1, 0, 0, 0, 2, 1, 1, 1, 42, 0x83, 1]),
            Rejected([0x04, 1, 1, 0, 0, 0, 1, 1, 1]),
            Rejected([0x04, 0, 0, 0x87, 1, 0]),
            Rejected([0x04, 0, 0, 0x87, 5]),
            Rejected([0x04, 0, 0, 0xF0, 0x01, 0x87, 1]),
            Rejected([0x04, 0, 0, 0xF0, 0x04, 0x87, 1]),
            Rejected([0x04, 1, 1, 0, 0, 0, 1, 0xF0, 0x04, 0x01,
                1, 1, 1, 42, 0x83, 1]),
            Rejected([0x04, 1, 1, 0, 0, 0, 1, 0xF0, 0x00, 0x12]),
            Rejected([0x04, 1, 1, 0, 0, 0, 1, 0xF0, 0x03, 0x12]),
            Rejected([0x04, 1, 1, 0, 0, 0, 1, 0xF0, 0x10, 0x12]),
            Rejected([0x04, 1, 1, 0, 0, 0, 1, 0xF0, 0x40, 0x12]),
            Rejected([0x04, 1, 1, 0, 0, 0, 1, 0xF0, 0x80, 0x12]),
            Rejected([0x04, 1, 1, 0, 0, 0, 1, 0x1A, 2, 1, 0x83, 2]),
            Rejected([0x04, 1, 1, 0, 0, 0, 1, 0x1C, 2, 0x83, 2]),
            Rejected([0x04, 1, 1, 0, 0, 0, 1, 0x1D, 2, 1, 0x83, 2]),
            Rejected([0x04, 1, 1, 0, 0, 0, 1, 0x12, 2]),
            Rejected([0x04, 1, 1, 0, 0, 0, 1,
                0xF0, 0x04, 0x12, 2, 2, 1]),
            Rejected([0x04, 1, 1, 0, 0, 0, 1,
                0xF0, 0x08, 0x12, 2, 2, 255, 255, 255, 255, 0x83, 2]),
            Rejected([0x04, 1, 1, 0, 0, 0, 1,
                0xF0, 0x0C, 0x12, 2, 2, 1, 1]),
            Rejected([0x04, 1, 1, 0, 0, 0, 1,
                0x12, 2, 2, 255, 1]),
        };

        foreach (var referenceWidth in new[] { 1, 2, 4 })
        {
            foreach (var countWidth in new[] { 1, 2, 4 })
            {
                cases.Add(Valid(LocalCallBody(referenceWidth, countWidth),
                    parameterCount: 1, secondMethod: true));
                cases.Add(Valid(PackedCallBody(referenceWidth, countWidth),
                    parameterCount: 1, secondMethod: true, packedTarget: true));
            }
        }
        cases.Add(Valid(LocalCallBody(4, 4, argumentCount: 0),
            parameterCount: 0, secondMethod: true));

        var root = RepositoryRoot();
        var components = new List<SyntaxTree>();
        foreach (var name in new[] {
            "VariableTableIndex.terrum",
            "BodyTableValidation.terrum",
            "CompactLinearBodyFraming.terrum",
        })
        {
            var path = Path.Combine(root, "self-host", name);
            var tree = SyntaxTree.Parse(await File.ReadAllTextAsync(path).ConfigureAwait(true), path);
            Assert.That(tree.HasErrors, Is.False, string.Join(Environment.NewLine, tree.Diagnostics));
            components.Add(tree);
        }
        components.Add(SyntaxTree.Parse(VerificationSource,
            "compact-linear-body-framing.terrum"));

        foreach (var testCase in cases)
        {
            if (testCase.HasHostedOracle)
            {
                Assert.That(ValidateHosted(testCase), Is.EqualTo(testCase.ExpectedValid));
            }
        }

        var section = TrRmVariableTableWriter.EncodeBodies(cases
            .Select(static testCase => new ReadOnlyMemory<byte>(testCase.Row)).ToArray());
        var expected = new List<int> { cases.Count };
        foreach (var testCase in cases)
        {
            expected.Add(testCase.TypeSpecCount);
            expected.Add(testCase.ExpectedValid ? 1 : 0);
            expected.Add(testCase.ExpectedBlockCount);
            expected.Add(testCase.ExpectedOperationCount);
        }

        var compilation = Compilation.Create(components,
        [
            new HostBuffer("Rows", PrimitiveType.Int32, [], false),
            new HostBuffer("BodyRows", PrimitiveType.Int32,
                [.. section.Select(static value => (int)value)], false),
            new HostBuffer("Expected", PrimitiveType.Int32, [.. expected], false),
        ], policy);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual0 = compilation.Program; Assert.That(__nunitActual0, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual0!; }))();
        Assert.That(NativeJit.Execute(program, (NativeRuntimePolicy)policy), Is.EqualTo(0));
        Assert.That(await ExecuteAotAsync(program, (NativeRuntimePolicy)policy)
            .ConfigureAwait(true), Is.EqualTo(0));
    }
    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
    public async Task ValidatedBodyIndexSupportsBatchFramingAsync(BoundRuntimePolicy policy)
    {
        const int recordCount = 512;
        var body = new ReadOnlyMemory<byte>([0x04, 0, 0, 0x83, 1]);
        var section = TrRmVariableTableWriter.EncodeBodies(
            Enumerable.Repeat(body, recordCount).ToArray());
        var root = RepositoryRoot();
        var components = new List<SyntaxTree>();
        foreach (var name in new[] {
            "VariableTableIndex.terrum",
            "BodyTableValidation.terrum",
            "CompactLinearBodyFraming.terrum",
        })
        {
            var path = Path.Combine(root, "self-host", name);
            var tree = SyntaxTree.Parse(await File.ReadAllTextAsync(path).ConfigureAwait(true), path);
            Assert.That(tree.HasErrors, Is.False, string.Join(Environment.NewLine, tree.Diagnostics));
            components.Add(tree);
        }
        components.Add(SyntaxTree.Parse(BatchVerificationSource,
            "compact-linear-body-batch-framing.terrum"));

        var compilation = Compilation.Create(components,
        [
            new HostBuffer("Rows", PrimitiveType.Int32, [], false),
            new HostBuffer("BodyRows", PrimitiveType.Int32,
                [.. section.Select(static value => (int)value)], false),
            new HostBuffer("Expected", PrimitiveType.Int32, [recordCount, 1], false),
        ], policy);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual1 = compilation.Program; Assert.That(__nunitActual1, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual1!; }))();
        Assert.That(NativeJit.Execute(program, (NativeRuntimePolicy)policy), Is.EqualTo(0));
        Assert.That(await ExecuteAotAsync(program, (NativeRuntimePolicy)policy)
            .ConfigureAwait(true), Is.EqualTo(0));
    }

    private static TableCase Valid(byte[] row, int parameterCount, int typeSpecCount = 1,
        byte typeCode = 8, byte? secondTypeCode = null, bool resultUsesSecondType = false,
        bool secondMethod = false, bool packedTarget = false, bool expectedTarget = false)
    {
        int operationCount;
        try
        {
            operationCount = TrRmCompactLinearBodyReaderTests.GetOperationCount(row,
                parameterCount, typeCode, secondTypeCode, resultUsesSecondType,
                secondMethod, packedTarget, expectedTarget);
        }
        catch (InvalidDataException exception)
        {
            throw new InvalidDataException(
                $"Hosted reader rejected Body row {Convert.ToHexString(row)}.", exception);
        }

        return new TableCase(row, parameterCount, typeSpecCount, typeCode, secondTypeCode,
            resultUsesSecondType, secondMethod, packedTarget, expectedTarget,
            true, 1, operationCount, true);
    }

    private static TableCase Rejected(byte[] row)
        => new(row, 0, 1, 8, null, false, false, false, false, false, -1, -1, true);

    internal static byte[] LocalCallBody(
        int referenceWidth, int countWidth, int argumentCount = 1)
    {
        if (argumentCount < 0 || argumentCount > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(argumentCount));
        }

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

        int result = argumentCount == 0 ? 1 : 2;
        AppendWord(row, result, referenceWidth);
        AppendWord(row, 2, referenceWidth);
        AppendWord(row, argumentCount, countWidth);
        if (argumentCount != 0)
        {
            AppendWord(row, 1, referenceWidth);
        }
        row.Add(0x83);
        row.Add((byte)result);
        return [.. row];
    }

    internal static byte[] PackedCallBody(int referenceWidth, int countWidth)
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

        AppendWord(row, 2, referenceWidth);
        AppendWord(row, 0, referenceWidth);
        row.AddRange([2, 0, 0, 0x16]);
        AppendWord(row, 1, countWidth);
        AppendWord(row, 1, referenceWidth);
        row.Add(0x83);
        row.Add(2);
        return [.. row];
    }

    private static int WidthCode(int width)
        => width switch {
            1 => 0,
            2 => 1,
            4 => 2,
            _ => throw new ArgumentOutOfRangeException(nameof(width)),
        };

    private static void AppendWord(List<byte> bytes, int value, int width)
    {
        for (var index = 0; index < width; index++)
        {
            bytes.Add((byte)(value >> (index * 8)));
        }
    }

    private static bool ValidateHosted(TableCase testCase)
    {
        try
        {
            _ = TrRmCompactLinearBodyReaderTests.GetOperationCount(testCase.Row,
                testCase.ParameterCount, testCase.TypeCode, testCase.SecondTypeCode,
                testCase.ResultUsesSecondType, testCase.SecondMethod,
                testCase.PackedTarget, testCase.ExpectedTarget);
            return true;
        }
        catch (InvalidDataException)
        {
            return false;
        }
    }

    private static byte[] ReturnParameterBody(byte bodyLayout, int width)
    {
        var row = new byte[1 + width + 4 + width + 2];
        row[0] = bodyLayout;
        row[1] = 1;
        row[1 + width] = 1;
        var returnOffset = 1 + width + 4 + width;
        row[returnOffset] = 0x83;
        row[returnOffset + 1] = 1;
        return row;
    }

    private static async Task<int> ExecuteAotAsync(BoundProgram program, NativeRuntimePolicy policy)
    {
        var path = Path.Combine(Path.GetTempPath(),
            $"compact-linear-body-framing-{Guid.NewGuid():N}.exe");
        try
        {
            await File.WriteAllBytesAsync(path, NativeEmitter.Emit(program, policy))
                .ConfigureAwait(true);
            using var process = Process.Start(new ProcessStartInfo(path) {
                UseShellExecute = false,
                CreateNoWindow = true,
            }) ?? throw new InvalidOperationException("Could not start compact-linear Body framer.");
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

    private sealed record TableCase(byte[] Row, int ParameterCount, int TypeSpecCount,
        byte TypeCode, byte? SecondTypeCode, bool ResultUsesSecondType, bool SecondMethod,
        bool PackedTarget, bool ExpectedTarget, bool ExpectedValid, int ExpectedBlockCount,
        int ExpectedOperationCount, bool HasHostedOracle);
}
