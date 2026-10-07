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
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed class CompactLinearBodyFramingComponentTests
{
    private const string VerificationSource = """
        namespace Terrum.CodeAnalysis;
        int32 Main() {
            if (!TryGetCompactLinearBodyFrame(
                Host.Expected[0], Host.Expected[1], Host.Expected[2],
                init var blockCount, init var operationCount)) {
                return Host.Expected[3] == 0 ? 0 : 1;
            }
            if (Host.Expected[3] == 0) {
                return 2;
            }
            if (blockCount != Host.Expected[4] || operationCount != Host.Expected[5]) {
                return 3;
            }
            return 0;
        }
        """;

    [Theory]
    [InlineData(BoundRuntimePolicy.FailFast)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions)]
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
            Valid([0x04, 1, 1, 0, 0, 0, 1, 1, 1, 1, 42, 0x83, 1],
                parameterCount: 0),
            Valid([0x04, 1, 1, 0, 0, 0, 1, 0xF0, 0x10, 2, 1, 1, 200, 0,
                0x83, 1], parameterCount: 0),
            Valid([0x04, 0, 0, 0x87, 1], parameterCount: 0),
            Valid([0x04, 1, 1, 0, 0, 0, 0, 0x83, 1], parameterCount: 1),
            Valid(ReturnParameterBody(0x05, 2), parameterCount: 1),
            Valid(ReturnParameterBody(0x06, 4), parameterCount: 1),
            Valid([0x04, 2, 1, 0, 0, 0, 2, 0, 0, 0, 1,
                0x16, 2, 2, 1, 0x83, 2], parameterCount: 1,
                typeSpecCount: 2, secondTypeCode: 0x0D, resultUsesSecondType: true),
            Rejected([0x08, 0, 0, 0x83, 0]),
            Rejected([0x07, 0, 0, 0x87, 1]),
            Rejected([0x04, 1, 0, 0, 0, 0, 0, 0x87, 1]),
            Rejected([0x04, 1, 2, 0, 0, 0, 0, 0x87, 1]),
            Rejected([0x04, 0, 1, 0x0B, 1, 1, 0x83, 0]),
            Rejected([0x04, 1, 1, 0, 0, 0, 0, 1, 1, 1, 42, 0x83, 1]),
            Rejected([0x04, 1, 1, 0, 0, 0, 2, 1, 1, 1, 42, 0x83, 1]),
            Rejected([0x04, 1, 1, 0, 0, 0, 1, 1, 1]),
            Rejected([0x04, 0, 0, 0x87, 1, 0]),
            Rejected([0x04, 0, 0, 0x87, 5]),
            Rejected([0x04, 0, 0, 0xF0, 0x01, 0x87, 1]),
            Rejected([0x04, 1, 1, 0, 0, 0, 1, 0xF0, 0x04, 0x01,
                1, 1, 1, 42, 0x83, 1]),
        };

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
            Assert.False(tree.HasErrors, string.Join(Environment.NewLine, tree.Diagnostics));
            components.Add(tree);
        }
        components.Add(SyntaxTree.Parse(VerificationSource, "compact-linear-body-framing.terrum"));

        foreach (var testCase in cases)
        {
            if (testCase.HasHostedOracle)
            {
                Assert.Equal(testCase.ExpectedValid, ValidateHosted(testCase));
            }

            var section = TrRmVariableTableWriter.EncodeBodies([testCase.Row]);
            var compilation = Compilation.Create(components,
            [
                new HostBuffer("Rows", PrimitiveType.Int32,
                    [.. section.Select(static value => (int)value)], false),
                new HostBuffer("Expected", PrimitiveType.Int32,
                [
                    1, 1, testCase.TypeSpecCount,
                    testCase.ExpectedValid ? 1 : 0,
                    testCase.ExpectedBlockCount,
                    testCase.ExpectedOperationCount,
                ], false),
            ], policy);
            Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
            var program = Assert.IsType<BoundProgram>(compilation.Program);
            Assert.Equal(0, NativeJit.Execute(program, (NativeRuntimePolicy)policy));
            Assert.Equal(0, await ExecuteAotAsync(program, (NativeRuntimePolicy)policy)
                .ConfigureAwait(true));
        }
    }

    private static TableCase Valid(byte[] row, int parameterCount, int typeSpecCount = 1,
        byte typeCode = 8, byte? secondTypeCode = null, bool resultUsesSecondType = false)
        => new(row, parameterCount, typeSpecCount, typeCode, secondTypeCode,
            resultUsesSecondType, true, 1,
            TrRmCompactLinearBodyReaderTests.GetOperationCount(row, parameterCount,
                typeCode, secondTypeCode, resultUsesSecondType), true);

    private static TableCase Rejected(byte[] row)
        => new(row, 0, 1, 8, null, false, false, -1, -1, true);

    private static bool ValidateHosted(TableCase testCase)
    {
        try
        {
            _ = TrRmCompactLinearBodyReaderTests.GetOperationCount(testCase.Row,
                testCase.ParameterCount, testCase.TypeCode, testCase.SecondTypeCode,
                testCase.ResultUsesSecondType);
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
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    private static string RepositoryRoot() =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));

    private sealed record TableCase(byte[] Row, int ParameterCount, int TypeSpecCount,
        byte TypeCode, byte? SecondTypeCode, bool ResultUsesSecondType, bool ExpectedValid,
        int ExpectedBlockCount, int ExpectedOperationCount, bool HasHostedOracle);
}
