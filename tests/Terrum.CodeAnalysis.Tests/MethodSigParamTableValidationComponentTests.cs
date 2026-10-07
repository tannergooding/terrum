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
using Terrum.CodeAnalysis.Text;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed class MethodSigParamTableValidationComponentTests
{
    private const string VerificationSource = """
        namespace Terrum.CodeAnalysis;
        int32 Main() {
            bool valid = TryValidateMethodSigParamTables(
                Host.Expected[0], Host.Expected[1], Host.Expected[2],
                Host.Expected[3], Host.Expected[4], Host.Expected[5], Host.Expected[6]);
            return valid == (Host.Expected[7] != 0) ? 0 : 1;
        }
        """;

    [Theory]
    [InlineData(BoundRuntimePolicy.FailFast)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions)]
    public async Task MethodSigParamValidationMatchesHostedReaders(BoundRuntimePolicy policy)
    {
        var valid = CreateValidCase();
        var cases = new List<TableCase> {
            valid,
            valid with { GenericParamCount = 0xFF_FFFF },
            valid with { SignatureCount = 0, Signatures = [], ExpectedValid = false },
            valid with { ParamCount = 0, Parameters = [], ExpectedValid = false },
        };

        var noParameterSignature = EncodeWords(1, 0, 0, 0, 0, 0);
        AddValid(cases, valid with {
            Signatures = [.. noParameterSignature.Select(static value => (int)value)],
            SignatureCount = 1,
            ParamCount = 0,
            GenericParamCount = 0,
            Parameters = [],
        });

        var overlappingSignatures = valid.Signatures.ToArray();
        WriteWord(overlappingSignatures, 24 + 8, 2);
        WriteWord(overlappingSignatures, 24 + 12, 2);
        var overlappingParameters = EncodeParameters((1, 0, 1, 0),
            (1, 1, 2, 0), (2, 1, 1, 0));
        AddRejected(cases, valid with {
            Signatures = overlappingSignatures,
            Parameters = [.. overlappingParameters.Select(static value => (int)value)],
            ExpectedValid = false,
        });

        var gappedSignatures = valid.Signatures.ToArray();
        WriteWord(gappedSignatures, 24 + 8, 2);
        WriteWord(gappedSignatures, 24 + 12, 1);
        var gappedParameters = EncodeParameters((1, 0, 1, 0),
            (1, 1, 2, 0), (2, 0, 1, 0));
        AddRejected(cases, valid with {
            Signatures = gappedSignatures,
            Parameters = [.. gappedParameters.Select(static value => (int)value)],
            ExpectedValid = false,
        });

        var outOfOrderSignatures = EncodeWords(1, 0, 2, 1, 1, 1,
            2, 1, 1, 1, 2, 1);
        var outOfOrderParameters = EncodeParameters((2, 0, 1, 0), (1, 0, 2, 0));
        AddValid(cases, valid with {
            Signatures = [.. outOfOrderSignatures.Select(static value => (int)value)],
            Parameters = [.. outOfOrderParameters.Select(static value => (int)value)],
            ParamCount = 2,
        });

        var sharedGenericRange = valid.Signatures.ToArray();
        WriteWord(sharedGenericRange, 24 + 16, 1);
        AddValid(cases, valid with {
            Signatures = sharedGenericRange,
            GenericParamCount = 1,
        });

        AddInvalidSignatures(cases, valid, signatures => WriteWord(signatures, 0, 0));
        AddInvalidSignatures(cases, valid, signatures => WriteWord(signatures, 0, 3));
        AddInvalidSignatures(cases, valid, signatures => WriteWord(signatures, 4, 3));
        AddInvalidSignatures(cases, valid, signatures => WriteWord(signatures, 8, 0));
        AddInvalidSignatures(cases, valid, signatures => WriteWord(signatures, 8, 4));
        AddInvalidSignatures(cases, valid, signatures => WriteWord(signatures, 12, 0));
        AddInvalidSignatures(cases, valid, signatures => WriteWord(signatures, 12, 3));
        AddInvalidSignatures(cases, valid, signatures => WriteWord(signatures, 16, 0));
        AddInvalidSignatures(cases, valid, signatures => WriteWord(signatures, 16, 3));
        AddInvalidSignatures(cases, valid, signatures => WriteWord(signatures, 20, 0));
        AddInvalidSignatures(cases, valid, signatures => WriteWord(signatures, 20, 3));
        AddInvalidParameters(cases, valid, parameters => WriteWord(parameters, 0, 0));
        AddInvalidParameters(cases, valid, parameters => WriteWord(parameters, 0, 3));
        AddInvalidParameters(cases, valid, parameters => WriteWord(parameters, 4, 1));
        AddInvalidParameters(cases, valid, parameters => WriteWord(parameters, 8, 0));
        AddInvalidParameters(cases, valid, parameters => WriteWord(parameters, 8, 3));
        AddInvalidParameters(cases, valid, parameters => parameters[12] = 5);
        AddInvalidParameters(cases, valid, parameters => parameters[13] = 1);
        AddInvalidParameters(cases, valid, parameters => parameters[14] = 1);
        AddInvalidParameters(cases, valid, parameters => parameters[15] = 1);
        cases.Add(valid with {
            Signatures = [.. valid.Signatures[..^1]],
            ExpectedValid = false,
        });
        cases.Add(valid with {
            Parameters = [.. valid.Parameters[..^1]],
            ExpectedValid = false,
        });
        cases.Add(valid with {
            ParamCount = -1,
            ExpectedValid = false,
        });
        cases.Add(valid with {
            GenericParamCount = 0x1000000,
            ExpectedValid = false,
        });

        var invalidParameterByte = valid.Parameters.ToArray();
        invalidParameterByte[0] = 256;
        cases.Add(valid with {
            Parameters = invalidParameterByte,
            ExpectedValid = false,
            HasHostedOracle = false,
        });

        var root = RepositoryRoot();
        var components = new List<SyntaxTree>();
        foreach (var name in new[] {
            "VariableTableIndex.terrum",
            "TypeSpecTableValidation.terrum",
            "MethodSigParamTableValidation.terrum",
        })
        {
            var path = Path.Combine(root, "self-host", name);
            var tree = SyntaxTree.Parse(await File.ReadAllTextAsync(path).ConfigureAwait(true), path);
            Assert.False(tree.HasErrors, string.Join(Environment.NewLine, tree.Diagnostics));
            components.Add(tree);
        }
        components.Add(SyntaxTree.Parse(VerificationSource, "method-sig-param-table-validation.terrum"));

        foreach (var testCase in cases)
        {
            if (testCase.HasHostedOracle)
            {
                var hostedValid = ValidateHosted(testCase);
                Assert.Equal(testCase.ExpectedValid, hostedValid);
            }

            var compilation = Compilation.Create(components,
            [
                new HostBuffer("Rows", PrimitiveType.Int32, [.. testCase.Types], false),
                new HostBuffer("Signatures", PrimitiveType.Int32, [.. testCase.Signatures], false),
                new HostBuffer("Params", PrimitiveType.Int32, [.. testCase.Parameters], false),
                new HostBuffer("Expected", PrimitiveType.Int32,
                [
                    testCase.SignatureCount,
                    testCase.ParamCount,
                    testCase.GenericParamCount,
                    testCase.TypeSpecCount,
                    testCase.TypeDefCount,
                    testCase.ImportCount,
                    testCase.TypeArgCount,
                    testCase.ExpectedValid ? 1 : 0,
                ], false),
            ], policy);
            Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
            var program = Assert.IsType<BoundProgram>(compilation.Program);
            Assert.Equal(0, NativeJit.Execute(program, (NativeRuntimePolicy)policy));
            Assert.Equal(0, await ExecuteAotAsync(program, (NativeRuntimePolicy)policy).ConfigureAwait(true));
        }
    }

    private static TableCase CreateValidCase()
    {
        byte[] types = TrRmTypeSpecTableWriter.Encode([
            TrRmTypeSpecTableWriter.Builtin(8),
            TrRmTypeSpecTableWriter.Builtin(0),
        ], 0, 0, 2, 0);
        byte[] signatures = EncodeWords(1, 0, 1, 2, 1, 1,
            2, 1, 3, 1, 2, 1);
        byte[] parameters = EncodeParameters((1, 0, 1, 0),
            (1, 1, 2, 1), (2, 0, 1, 4));
        var testCase = new TableCase([.. types.Select(static value => (int)value)],
            [.. signatures.Select(static value => (int)value)],
            [.. parameters.Select(static value => (int)value)], 2, 3, 2, 2, 0, 0, 0, true, true);
        Assert.True(ValidateHosted(testCase));
        return testCase;
    }

    private static void AddInvalidSignatures(List<TableCase> cases, TableCase valid,
        Action<byte[]> mutate)
    {
        var signatures = valid.Signatures.Select(static value => checked((byte)value)).ToArray();
        mutate(signatures);
        AddRejected(cases, valid with {
            Signatures = [.. signatures.Select(static value => (int)value)],
            ExpectedValid = false,
        });
    }

    private static void AddInvalidParameters(List<TableCase> cases, TableCase valid,
        Action<byte[]> mutate)
    {
        var parameters = valid.Parameters.Select(static value => checked((byte)value)).ToArray();
        mutate(parameters);
        AddRejected(cases, valid with {
            Parameters = [.. parameters.Select(static value => (int)value)],
            ExpectedValid = false,
        });
    }

    private static void AddValid(List<TableCase> cases, TableCase testCase)
    {
        Assert.True(ValidateHosted(testCase));
        cases.Add(testCase);
    }

    private static void AddRejected(List<TableCase> cases, TableCase testCase)
    {
        Assert.False(ValidateHosted(testCase));
        cases.Add(testCase);
    }

    private static bool ValidateHosted(TableCase testCase)
    {
        try
        {
            var types = testCase.Types.Select(static value => checked((byte)value)).ToArray();
            var signatures = testCase.Signatures.Select(static value => checked((byte)value)).ToArray();
            var parameters = testCase.Parameters.Select(static value => checked((byte)value)).ToArray();
            var typeSpecs = new TrRmTypeSpecTableReader(types, testCase.TypeSpecCount,
                testCase.TypeDefCount, testCase.ImportCount,
                testCase.GenericParamCount, testCase.TypeArgCount);
            var signatureReader = new TrRmMethodSigTableReader(signatures, testCase.SignatureCount,
                typeSpecs, testCase.ParamCount, testCase.GenericParamCount);
            if (testCase.ParamCount > 0)
            {
                _ = new TrRmParamTableReader(parameters, testCase.ParamCount,
                    signatureReader, typeSpecs);
            }

            return true;
        }
        catch (InvalidDataException)
        {
            return false;
        }
    }

    private static byte[] EncodeWords(params uint[] words)
    {
        var bytes = new byte[words.Length * sizeof(uint)];
        for (var index = 0; index < words.Length; index++)
        {
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(
                bytes.AsSpan(index * sizeof(uint)), words[index]);
        }

        return bytes;
    }

    private static byte[] EncodeParameters(params (uint Owner, uint Ordinal, uint Type, byte Mode)[] rows)
    {
        var bytes = new byte[rows.Length * 16];
        for (var index = 0; index < rows.Length; index++)
        {
            var row = rows[index];
            var offset = index * 16;
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), row.Owner);
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset + 4), row.Ordinal);
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset + 8), row.Type);
            bytes[offset + 12] = row.Mode;
        }

        return bytes;
    }

    private static void WriteWord(byte[] bytes, int offset, uint value)
        => System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);

    private static void WriteWord(int[] bytes, int offset, uint value)
    {
        bytes[offset] = (int)(value & 0xFF);
        bytes[offset + 1] = (int)((value >> 8) & 0xFF);
        bytes[offset + 2] = (int)((value >> 16) & 0xFF);
        bytes[offset + 3] = (int)((value >> 24) & 0xFF);
    }

    private static async Task<int> ExecuteAotAsync(BoundProgram program, NativeRuntimePolicy policy)
    {
        var path = Path.Combine(Path.GetTempPath(), $"method-sig-param-validation-{Guid.NewGuid():N}.exe");
        try
        {
            await File.WriteAllBytesAsync(path, NativeEmitter.Emit(program, policy)).ConfigureAwait(true);
            using var process = Process.Start(new ProcessStartInfo(path) {
                UseShellExecute = false,
                CreateNoWindow = true,
            }) ?? throw new InvalidOperationException("Could not start compiled MethodSig/Param validator.");
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

    private sealed record TableCase(int[] Types, int[] Signatures, int[] Parameters,
        int SignatureCount, int ParamCount, int GenericParamCount, int TypeSpecCount,
        int TypeDefCount, int ImportCount, int TypeArgCount, bool ExpectedValid, bool HasHostedOracle = true);
}
