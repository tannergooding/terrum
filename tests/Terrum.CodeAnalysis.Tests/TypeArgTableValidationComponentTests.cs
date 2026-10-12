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

public sealed class TypeArgTableValidationComponentTests
{
    private const string VerificationSource = """
        namespace Terrum.CodeAnalysis;
        int32 Main() {
            bool valid = TryValidateTypeArgTable(
                Host.Expected[0], Host.Expected[1], Host.Expected[2],
                Host.Expected[3], Host.Expected[4], Host.Expected[5]);
            return valid == (Host.Expected[6] != 0) ? 0 : 1;
        }
        """;

    [Theory]
    [InlineData(BoundRuntimePolicy.FailFast)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions)]
    public async Task TypeArgTableValidationMatchesHostedOwnershipReader(BoundRuntimePolicy policy)
    {
        var valid = CreateValidCase();
        var cases = new List<TableCase> {
            valid,
            CreateLargeCase(),
            CreateGroupedCase(),
            valid with {
                TypeDefCount = 0xFF_FFFF,
                ImportCount = 0xFF_FFFF,
                GenericParamCount = 0xFF_FFFF,
            },
            valid with { RecordCount = 2, ExpectedValid = false },
        };

        AddInvalidArguments(cases, valid, arguments => WriteWord(arguments, 0, 0));
        AddInvalidArguments(cases, valid, arguments => WriteWord(arguments, 0, 1));
        AddInvalidArguments(cases, valid, arguments => WriteWord(arguments, 0, 2));
        AddInvalidArguments(cases, valid, arguments => WriteWord(arguments, 0, 6));
        AddInvalidArguments(cases, valid, arguments => WriteWord(arguments, 4, 1));
        AddInvalidArguments(cases, valid, arguments => WriteWord(arguments, 4, 0x1000000));
        AddInvalidArguments(cases, valid, arguments => WriteWord(arguments, 8, 0));
        AddInvalidArguments(cases, valid, arguments => WriteWord(arguments, 8, 4));
        AddInvalidArguments(cases, valid, arguments => WriteWord(arguments, 32, 5));
        cases.Add(valid with { Arguments = [.. valid.Arguments[..^1]], ExpectedValid = false });
        cases.Add(valid with { Arguments = [.. valid.Arguments, 0], ExpectedValid = false });
        cases.Add(valid with { TypeArgCount = 2, ExpectedValid = false });
        cases.Add(valid with { TypeArgCount = -1, ExpectedValid = false });
        cases.Add(valid with { TypeArgCount = 0x1000000, ExpectedValid = false });
        cases.Add(valid with { RecordCount = 0, Arguments = [], ExpectedValid = false });

        byte[] overlappingTypes = EncodeTypes(3,
            TrRmTypeSpecTableWriter.Builtin(8),
            TrRmTypeSpecTableWriter.Reference(0x02, 1),
            TrRmTypeSpecTableWriter.Reference(0x03, 1),
            TrRmTypeSpecTableWriter.Constructed(2, 1, 2),
            TrRmTypeSpecTableWriter.Constructed(3, 2, 2));
        byte[] overlappingArguments = EncodeArguments(4, 0, 1, 4, 1, 2, 5, 1, 3);
        AddRejected(cases, new TableCase([.. overlappingTypes.Select(static value => (int)value)],
            [.. overlappingArguments.Select(static value => (int)value)], 3, 5, 1, 1, 0, 3, false, true));

        byte[] unownedTypes = EncodeTypes(1, TrRmTypeSpecTableWriter.Builtin(8));
        byte[] unownedArguments = EncodeArguments(1, 0, 1);
        AddRejected(cases, new TableCase([.. unownedTypes.Select(static value => (int)value)],
            [.. unownedArguments.Select(static value => (int)value)], 1, 1, 1, 0, 0, 1, false, true));

        var invalidByteWords = valid.Arguments.ToArray();
        invalidByteWords[0] = 256;
        cases.Add(valid with { Arguments = invalidByteWords, ExpectedValid = false, HasHostedOracle = false });

        var root = RepositoryRoot();
        var components = new List<SyntaxTree>();
        foreach (var name in new[] {
            "TypeSpecTableValidation.terrum",
            "TypeArgTableValidation.terrum",
        })
        {
            var path = Path.Combine(root, "self-host", name);
            var tree = SyntaxTree.Parse(await File.ReadAllTextAsync(path).ConfigureAwait(true), path);
            Assert.False(tree.HasErrors, string.Join(Environment.NewLine, tree.Diagnostics));
            components.Add(tree);
        }
        components.Add(SyntaxTree.Parse(VerificationSource, "type-arg-table-validation-component.terrum"));

        foreach (var testCase in cases)
        {
            if (testCase.HasHostedOracle)
            {
                var hostedValid = ValidateHosted(testCase);
                Assert.Equal(testCase.ExpectedValid, hostedValid);
            }

            var compilation = Compilation.Create(components,
            [
                new HostBuffer("TypeSpecs", PrimitiveType.Int32, [.. testCase.Types], false),
                new HostBuffer("Args", PrimitiveType.Int32, [.. testCase.Arguments], false),
                new HostBuffer("Expected", PrimitiveType.Int32,
                [
                    testCase.RecordCount,
                    testCase.TypeSpecCount,
                    testCase.TypeDefCount,
                    testCase.ImportCount,
                    testCase.GenericParamCount,
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
        byte[] types = EncodeTypes(3,
            TrRmTypeSpecTableWriter.Builtin(8),
            TrRmTypeSpecTableWriter.Reference(0x02, 1),
            TrRmTypeSpecTableWriter.Reference(0x03, 1),
            TrRmTypeSpecTableWriter.Constructed(2, 1, 2),
            TrRmTypeSpecTableWriter.Constructed(3, 3, 1));
        var typeReader = new TrRmTypeSpecTableReader(types, 5, 1, 1, 0, 3);
        byte[] arguments = TrRmTypeArgTableWriter.Encode([
            new TrRmTypeArgRow(4, 0, 1),
            new TrRmTypeArgRow(4, 1, 2),
            new TrRmTypeArgRow(5, 0, 3),
        ], typeReader);
        var testCase = new TableCase([.. types.Select(static value => (int)value)],
            [.. arguments.Select(static value => (int)value)], 3, 5, 1, 1, 0, 3, true, true);
        Assert.True(ValidateHosted(testCase));
        return testCase;
    }

    private static TableCase CreateLargeCase()
    {
        const int typeArgCount = 512;
        var rows = new ReadOnlyMemory<byte>[typeArgCount + 2];
        rows[0] = TrRmTypeSpecTableWriter.Builtin(8);
        rows[1] = TrRmTypeSpecTableWriter.Reference(0x02, 1);
        var argumentWords = new uint[typeArgCount * 3];
        for (var index = 0; index < typeArgCount; index++)
        {
            rows[index + 2] = TrRmTypeSpecTableWriter.Constructed(2, (uint)index + 1, 1);
            argumentWords[index * 3] = (uint)index + 3;
            argumentWords[index * 3 + 2] = 1;
        }

        var types = EncodeTypes(typeArgCount, rows);
        var arguments = EncodeArguments(argumentWords);
        return new TableCase(
            [.. types.Select(static value => (int)value)],
            [.. arguments.Select(static value => (int)value)],
            typeArgCount,
            typeArgCount + 2,
            1,
            1,
            0,
            typeArgCount,
            true,
            true);
    }

    private static TableCase CreateGroupedCase()
    {
        const int typeArgCount = 512;
        var types = EncodeTypes(typeArgCount,
            TrRmTypeSpecTableWriter.Builtin(8),
            TrRmTypeSpecTableWriter.Reference(0x02, 1),
            TrRmTypeSpecTableWriter.Constructed(2, 1, typeArgCount));
        var argumentWords = new uint[typeArgCount * 3];
        for (var index = 0; index < typeArgCount; index++)
        {
            argumentWords[index * 3] = 3;
            argumentWords[index * 3 + 1] = (uint)index;
            argumentWords[index * 3 + 2] = 1;
        }

        var arguments = EncodeArguments(argumentWords);
        return new TableCase(
            [.. types.Select(static value => (int)value)],
            [.. arguments.Select(static value => (int)value)],
            typeArgCount,
            3,
            1,
            1,
            0,
            typeArgCount,
            true,
            true);
    }

    private static void AddInvalidArguments(List<TableCase> cases, TableCase valid,
        Action<byte[]> mutate)
    {
        var arguments = valid.Arguments.Select(static value => checked((byte)value)).ToArray();
        mutate(arguments);
        AddRejected(cases, valid with {
            Arguments = [.. arguments.Select(static value => (int)value)],
            ExpectedValid = false,
        });
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
            var arguments = testCase.Arguments.Select(static value => checked((byte)value)).ToArray();
            var typeReader = new TrRmTypeSpecTableReader(types, testCase.TypeSpecCount,
                testCase.TypeDefCount, testCase.ImportCount,
                testCase.GenericParamCount, testCase.TypeArgCount);
            _ = new TrRmTypeArgTableReader(arguments, testCase.RecordCount, typeReader);
            return true;
        }
        catch (InvalidDataException)
        {
            return false;
        }
    }

    private static byte[] EncodeTypes(int typeArgCount, params ReadOnlyMemory<byte>[] rows)
        => TrRmTypeSpecTableWriter.Encode(rows, 1, 1, 0, typeArgCount);

    private static byte[] EncodeArguments(params uint[] words)
    {
        var bytes = new byte[words.Length * sizeof(uint)];
        for (var index = 0; index < words.Length; index++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(index * sizeof(uint)), words[index]);
        }

        return bytes;
    }

    private static void WriteWord(byte[] bytes, int offset, uint value)
        => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);

    private static async Task<int> ExecuteAotAsync(BoundProgram program, NativeRuntimePolicy policy)
    {
        var path = Path.Combine(Path.GetTempPath(), $"typearg-table-validation-{Guid.NewGuid():N}.exe");
        try
        {
            await File.WriteAllBytesAsync(path, NativeEmitter.Emit(program, policy)).ConfigureAwait(true);
            using var process = Process.Start(new ProcessStartInfo(path) {
                UseShellExecute = false,
                CreateNoWindow = true,
            }) ?? throw new InvalidOperationException("Could not start compiled TypeArg table validator.");
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

    private sealed record TableCase(int[] Types, int[] Arguments, int RecordCount, int TypeSpecCount,
        int TypeDefCount, int ImportCount, int GenericParamCount, int TypeArgCount,
        bool ExpectedValid, bool HasHostedOracle);
}
