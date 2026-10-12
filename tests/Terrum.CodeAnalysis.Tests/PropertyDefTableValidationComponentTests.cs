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
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed class PropertyDefTableValidationComponentTests
{
    private const string VerificationSource = """
        namespace Terrum.CodeAnalysis;
        int32 Main() {
            bool valid = TryValidateMethodSigParamTables(
                Host.Expected[5], 0, 0, Host.Expected[3],
                Host.Expected[2], 0, 0)
                && TryValidatePropertyDefTable(
                    Host.Expected[0], Host.Expected[1], Host.Expected[2],
                    Host.Expected[3], Host.Expected[4], Host.Expected[5]);
            return valid == (Host.Expected[6] != 0) ? 0 : 1;
        }
        """;

    [Theory]
    [InlineData(BoundRuntimePolicy.FailFast)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions)]
    public async Task PropertyDefValidationMatchesHostedReader(BoundRuntimePolicy policy)
    {
        var valid = CreateValidCase();
        var cases = new List<TableCase> {
            valid,
            WithPropertyWord(valid, 0, 0),
            WithPropertyWord(valid, 0, 2),
            WithPropertyWord(valid, 4, 0),
            WithPropertyWord(valid, 4, 6),
            WithPropertyWord(valid, 4, 1),
            WithPropertyWord(valid, 8, 0),
            WithPropertyWord(valid, 8, 3),
            WithPropertyWord(valid, 12, 0),
            WithPropertyWord(valid, 12, 2),
            WithPropertyWord(valid, 16, 1) with { ExpectedValid = true },
            WithPropertyWord(valid, 16, 2) with { ExpectedValid = true },
            WithPropertyWord(valid, 16, 3),
            valid with { Properties = [.. valid.Properties[..^1]], ExpectedValid = false },
            valid with { PropertyCount = 0, Properties = [], ExpectedValid = false },
            valid with { TypeDefCount = 0, TypeDefs = [], ExpectedValid = false },
            valid with { MethodDefCount = 0, Methods = [], ExpectedValid = false },
            valid with { MethodSigCount = 0, Signatures = [], ExpectedValid = false },
        };

        byte[] oneTypeSpec = TrRmTypeSpecTableWriter.Encode([
            TrRmTypeSpecTableWriter.Builtin(8),
        ], 1, 0, 0, 0);
        var outOfRangeType = valid with {
            TypeSpecs = [.. oneTypeSpec.Select(static value => (int)value)],
            TypeSpecCount = 1,
            Properties = [.. WithPropertyType(valid, 2)],
            ExpectedValid = false,
        };
        cases.Add(outOfRangeType);

        var ownerlessMethod = Method(owner: 0);
        var ownerlessSignature = Signature(receiver: 0);
        cases.Add(valid with {
            Methods = [.. ownerlessMethod.Select(static value => (int)value)],
            Signatures = [.. ownerlessSignature.Select(static value => (int)value)],
            ExpectedValid = false,
        });

        var wrongResultSignature = Signature(result: 2);
        cases.Add(valid with {
            Signatures = [.. wrongResultSignature.Select(static value => (int)value)],
            ExpectedValid = false,
        });

        var emptyName = Property();
        WriteWord(emptyName, 4, 1);
        cases.Add(valid with {
            Properties = [.. emptyName.Select(static value => (int)value)],
            ExpectedValid = false,
        });

        var invalidPropertyByte = valid.Properties.ToArray();
        invalidPropertyByte[0] = 256;
        cases.Add(valid with {
            Properties = invalidPropertyByte,
            ExpectedValid = false,
            HasHostedOracle = false,
        });

        var root = RepositoryRoot();
        var components = new List<SyntaxTree>();
        foreach (var name in new[] {
            "VariableTableIndex.terrum",
            "StringRowLayout.terrum",
            "StringRowText.terrum",
            "TypeSpecTableValidation.terrum",
            "MethodSigParamTableValidation.terrum",
            "PropertyDefTableValidation.terrum",
        })
        {
            var path = Path.Combine(root, "self-host", name);
            var tree = SyntaxTree.Parse(await File.ReadAllTextAsync(path).ConfigureAwait(true), path);
            Assert.False(tree.HasErrors, string.Join(Environment.NewLine, tree.Diagnostics));
            components.Add(tree);
        }
        components.Add(SyntaxTree.Parse(VerificationSource, "property-def-table-validation.terrum"));

        for (var caseIndex = 0; caseIndex < cases.Count; caseIndex++)
        {
            var testCase = cases[caseIndex];
            if (testCase.HasHostedOracle)
            {
                Assert.Equal(testCase.ExpectedValid, ValidateHosted(testCase));
            }

            var compilation = Compilation.Create(components,
            [
                new HostBuffer("Rows", PrimitiveType.Int32, [.. testCase.Strings], false),
                new HostBuffer("TypeSpecs", PrimitiveType.Int32, [.. testCase.TypeSpecs], false),
                new HostBuffer("Properties", PrimitiveType.Int32, [.. testCase.Properties], false),
                new HostBuffer("TypeDefs", PrimitiveType.Int32, [.. testCase.TypeDefs], false),
                new HostBuffer("Methods", PrimitiveType.Int32, [.. testCase.Methods], false),
                new HostBuffer("Signatures", PrimitiveType.Int32, [.. testCase.Signatures], false),
                new HostBuffer("Params", PrimitiveType.Int32, [], false),
                new HostBuffer("Expected", PrimitiveType.Int32,
                [
                    testCase.PropertyCount, testCase.StringCount, testCase.TypeDefCount,
                    testCase.TypeSpecCount, testCase.MethodDefCount, testCase.MethodSigCount,
                    testCase.ExpectedValid ? 1 : 0,
                ], false),
            ], policy);
            Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
            var program = Assert.IsType<BoundProgram>(compilation.Program);
            Assert.Equal(0, NativeJit.Execute(program, (NativeRuntimePolicy)policy));
            Assert.Equal(0, await ExecuteAotAsync(program, (NativeRuntimePolicy)policy)
                .ConfigureAwait(true));
        }
    }

    private static TableCase CreateValidCase()
    {
        byte[] strings = TrRmStringTableWriter.EncodeTextRows([
            ("", 0), ("Module", 0), ("Widget", 0), ("Read", 0), ("Value", 0),
        ]);
        byte[] typeSpecs = TrRmTypeSpecTableWriter.Encode([
            TrRmTypeSpecTableWriter.Builtin(8),
            TrRmTypeSpecTableWriter.Builtin(8),
        ], 1, 0, 0, 0);
        var testCase = new TableCase(
            [.. strings.Select(static value => (int)value)],
            [.. typeSpecs.Select(static value => (int)value)],
            [.. Type().Select(static value => (int)value)],
            [.. Method().Select(static value => (int)value)],
            [.. Signature().Select(static value => (int)value)],
            [.. Property().Select(static value => (int)value)],
            5, 1, 2, 1, 1, 1, true, true);
        Assert.True(ValidateHosted(testCase));
        return testCase;
    }

    private static TableCase WithPropertyWord(TableCase testCase, int offset, uint value)
    {
        var bytes = ToBytes(testCase.Properties);
        WriteWord(bytes, offset, value);
        return testCase with {
            Properties = [.. bytes.Select(static item => (int)item)],
            ExpectedValid = false,
        };
    }

    private static byte[] WithPropertyType(TableCase testCase, uint value)
    {
        var bytes = ToBytes(testCase.Properties);
        WriteWord(bytes, 8, value);
        return bytes;
    }

    private static bool ValidateHosted(TableCase testCase)
    {
        try
        {
            var strings = new TrRmStringTableReader(ToBytes(testCase.Strings), testCase.StringCount);
            var modules = new TrRmModuleTableReader(Module(), 1, strings, 0,
                testCase.TypeDefCount, testCase.MethodDefCount, 0);
            var types = new TrRmTypeDefTableReader(ToBytes(testCase.TypeDefs),
                testCase.TypeDefCount, strings, modules, 0, 0, 0, 0);
            var typeSpecs = new TrRmTypeSpecTableReader(ToBytes(testCase.TypeSpecs),
                testCase.TypeSpecCount, testCase.TypeDefCount, 0, 0, 0);
            var signatures = new TrRmMethodSigTableReader(ToBytes(testCase.Signatures),
                testCase.MethodSigCount, typeSpecs, 0, 0);
            var methods = new TrRmMethodDefTableReader(ToBytes(testCase.Methods),
                testCase.MethodDefCount, strings, modules, types, signatures, 0, 0);
            _ = new TrRmPropertyDefTableReader(ToBytes(testCase.Properties),
                testCase.PropertyCount, strings, types, typeSpecs, methods, signatures);
            return true;
        }
        catch (InvalidDataException)
        {
            return false;
        }
    }

    private static byte[] Module()
    {
        var row = new byte[44];
        WriteWord(row, 0, 2);
        WriteWord(row, 4, 1);
        WriteWord(row, 28, 1);
        WriteWord(row, 32, 1);
        WriteWord(row, 36, 1);
        WriteWord(row, 40, 1);
        return row;
    }

    private static byte[] Type()
    {
        var row = new byte[40];
        WriteWord(row, 0, 1);
        WriteWord(row, 4, 3);
        WriteWord(row, 8, 4);
        return row;
    }

    private static byte[] Method(uint owner = 1)
    {
        var row = new byte[28];
        WriteWord(row, 0, owner);
        WriteWord(row, 4, 4);
        WriteWord(row, 8, 1);
        return row;
    }

    private static byte[] Signature(uint result = 1, uint receiver = 2)
    {
        var row = new byte[24];
        WriteWord(row, 0, result);
        WriteWord(row, 4, receiver);
        return row;
    }

    private static byte[] Property()
    {
        var row = new byte[20];
        WriteWord(row, 0, 1);
        WriteWord(row, 4, 5);
        WriteWord(row, 8, 1);
        WriteWord(row, 12, 1);
        return row;
    }

    private static byte[] ToBytes(int[] values)
        => values.Select(static value => checked((byte)value)).ToArray();

    private static void WriteWord(byte[] bytes, int offset, uint value)
        => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);

    private static async Task<int> ExecuteAotAsync(BoundProgram program, NativeRuntimePolicy policy)
    {
        var path = Path.Combine(Path.GetTempPath(),
            $"property-def-table-validation-{Guid.NewGuid():N}.exe");
        try
        {
            await File.WriteAllBytesAsync(path, NativeEmitter.Emit(program, policy))
                .ConfigureAwait(true);
            using var process = Process.Start(new ProcessStartInfo(path) {
                UseShellExecute = false,
                CreateNoWindow = true,
            }) ?? throw new InvalidOperationException("Could not start compiled PropertyDef validator.");
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

    private sealed record TableCase(int[] Strings, int[] TypeSpecs, int[] TypeDefs, int[] Methods,
        int[] Signatures, int[] Properties, int StringCount, int TypeDefCount, int TypeSpecCount,
        int MethodDefCount, int MethodSigCount, int PropertyCount, bool ExpectedValid, bool HasHostedOracle);
}
