// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

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
using NUnit.Framework;

using Terrum.CodeAnalysis.Tests.Infrastructure;

namespace Terrum.CodeAnalysis.Tests.Modules.Metadata;

public sealed class GenericParamConstraintTableValidationComponentTests
{
    private const string VerificationSource = """
        namespace Terrum.CodeAnalysis;
        int32 Main() {
            bool valid = TryValidateMethodSigParamTables(
                Host.Expected[12], 0, Host.Expected[9], Host.Expected[13],
                Host.Expected[4], 0, 0)
                && TryValidateConstraintTable(
                Host.Expected[0], Host.Expected[1], Host.Expected[2],
                Host.Expected[3], Host.Expected[4], Host.Expected[5],
                Host.Expected[6], Host.Expected[7], Host.Expected[8],
                Host.Expected[9], Host.Expected[10], Host.Expected[11],
                Host.Expected[12], Host.Expected[13], Host.Expected[14],
                Host.Expected[15]);
            return valid == (Host.Expected[16] != 0) ? 0 : 1;
        }
        """;
    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
    public async Task GenericParamAndConstraintValidationMatchesHostedReaders(
        BoundRuntimePolicy policy)
    {
        var valid = CreateValidCase();
        var cases = new List<TableCase> {
            valid,
            CreateGroupedFormalCase(valid),
            CreateNoFormalCase(valid),
            WithFormalWord(valid, 0, 0x1700_0001),
            WithFormalWord(valid, 0, 0x1300_0002),
            WithFormalWord(valid, 4, 1),
            WithFormalWord(valid, 8, 0),
            WithFormalWord(valid, 8, 1),
            WithFormalWord(valid, 8, 7),
            WithFormalWord(valid, 12, 0),
            WithFormalWord(valid, 16, 0),
            WithFormalWord(valid, 32, 1),
            WithFormalWord(valid, 36, 0),
            WithFormalBytes(valid, ToBytes(valid.GenericParams[..^1])),
            WithConstraintWord(valid, 0, 0),
            WithConstraintWord(valid, 0, 3),
            WithConstraintHalfWord(valid, 6, 1),
            WithConstraintWord(valid, 8, 0),
            WithConstraintWord(valid, 8, 2),
            WithConstraintBytes(valid, ToBytes(valid.Constraints[..^1])),
            valid with { TypeSpecCount = -1, ExpectedValid = false },
            valid with { TypeSpecCount = 0x1000000, ExpectedValid = false },
            valid with { ConstraintCount = 1, ExpectedValid = false },
        };

        var overlappingRanges = WithFormalWord(valid, 32, 1);
        cases.Add(overlappingRanges);
        var unownedRange = WithFormalWord(valid, 32, 0);
        cases.Add(WithFormalWord(unownedRange, 36, 0));

        var invalidConstraintByte = valid.Constraints.ToArray();
        invalidConstraintByte[15] = 256;
        cases.Add(valid with {
            Constraints = invalidConstraintByte,
            ExpectedValid = false,
            HasHostedOracle = false,
        });

        var root = RepositoryRoot();
        var components = new List<SyntaxTree>();
        foreach (var name in new[] {
            "sources/Terrum.CodeAnalysis.Terrum/Modules/VariableTableIndex.terrum",
            "sources/Terrum.CodeAnalysis.Terrum/Modules/StringRowLayout.terrum",
            "sources/Terrum.CodeAnalysis.Terrum/Modules/StringRowText.terrum",
            "tests/Terrum.CodeAnalysis.Terrum.Tests/Modules/ModuleTableValidation.terrum",
            "tests/Terrum.CodeAnalysis.Terrum.Tests/Modules/TypeDefTableValidation.terrum",
            "tests/Terrum.CodeAnalysis.Terrum.Tests/Modules/MethodDefTableValidation.terrum",
            "tests/Terrum.CodeAnalysis.Terrum.Tests/Modules/MethodSigParamTableValidation.terrum",
            "tests/Terrum.CodeAnalysis.Terrum.Tests/Modules/GenericParamTableValidation.terrum",
            "tests/Terrum.CodeAnalysis.Terrum.Tests/Modules/ConstraintTableValidation.terrum",
            "tests/Terrum.CodeAnalysis.Terrum.Tests/Modules/TypeSpecTableValidation.terrum",
        })
        {
            var path = TerrumProjectPaths.RepositoryFile(root, name);
            var tree = SyntaxTree.Parse(await File.ReadAllTextAsync(path).ConfigureAwait(true), path);
            Assert.That(tree.HasErrors, Is.False, string.Join(Environment.NewLine, tree.Diagnostics));
            components.Add(tree);
        }
        components.Add(SyntaxTree.Parse(VerificationSource,
            "generic-param-constraint-table-validation.terrum"));

        for (var caseIndex = 0; caseIndex < cases.Count; caseIndex++)
        {
            var testCase = cases[caseIndex];
            if (testCase.HasHostedOracle)
            {
                Assert.That(testCase.ExpectedValid == ValidateHosted(testCase), Is.True, $"Hosted differential case {caseIndex} expected {testCase.ExpectedValid}.");
            }

            var compilation = Compilation.Create(components,
            [
                new HostBuffer("Rows", PrimitiveType.Int32, [.. testCase.Strings], false),
                new HostBuffer("TypeSpecs", PrimitiveType.Int32,
                    [.. TrRmTypeSpecTableWriter.Encode([
                        TrRmTypeSpecTableWriter.Builtin(8),
                    ], testCase.TypeDefCount, 0, testCase.GenericParamCount, 0)
                        .Select(static value => (int)value)], false),
                new HostBuffer("Modules", PrimitiveType.Int32, [.. testCase.Modules], false),
                new HostBuffer("TypeDefs", PrimitiveType.Int32, [.. testCase.TypeDefs], false),
                new HostBuffer("Methods", PrimitiveType.Int32, [.. testCase.Methods], false),
                new HostBuffer("Signatures", PrimitiveType.Int32, [.. testCase.Signatures], false),
                new HostBuffer("Params", PrimitiveType.Int32, [], false),
                new HostBuffer("GenericParams", PrimitiveType.Int32, [.. testCase.GenericParams], false),
                new HostBuffer("Constraints", PrimitiveType.Int32, [.. testCase.Constraints], false),
                new HostBuffer("IdentitySlots", PrimitiveType.Int32,
                    [.. Enumerable.Repeat(123, testCase.ModuleCount + 1)], true),
                new HostBuffer("FieldOwners", PrimitiveType.Int32, [123], true),
                new HostBuffer("BodyOwners", PrimitiveType.Int32, [123], true),
                new HostBuffer("GenericSignatureOwners", PrimitiveType.Int32, [123, 123], true),
                new HostBuffer("ConstraintOwners", PrimitiveType.Int32,
                    [.. Enumerable.Repeat(123, testCase.ConstraintCount + 1)], true),
                new HostBuffer("Expected", PrimitiveType.Int32,
                [
                    testCase.ConstraintCount,
                    testCase.ModuleCount,
                    testCase.StringCount,
                    0,
                    testCase.TypeDefCount,
                    testCase.MethodDefCount,
                    0,
                    0,
                    0,
                    testCase.GenericParamCount,
                    0,
                    0,
                    testCase.SignatureCount,
                    testCase.TypeSpecCount,
                    testCase.ConstraintCount,
                    0,
                    testCase.ExpectedValid ? 1 : 0,
                ], false),
            ], policy);
            Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
            var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual0 = compilation.Program; Assert.That(__nunitActual0, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual0!; }))();
            Assert.That(NativeJit.Execute(program, (NativeRuntimePolicy)policy), Is.EqualTo(0));
            Assert.That(await ExecuteAotAsync(program, (NativeRuntimePolicy)policy)
                .ConfigureAwait(true), Is.EqualTo(0));
        }
    }

    private static TableCase CreateValidCase()
    {
        byte[] strings = TrRmStringTableWriter.EncodeTextRows([
            ("", 0), ("Module", 0), ("Widget", 0), ("Build", 0), ("T", 0), ("U", 0),
        ]);
        var testCase = new TableCase(
            [.. strings.Select(static value => (int)value)],
            [.. Module().Select(static value => (int)value)],
            [.. Type().Select(static value => (int)value)],
            [.. Method().Select(static value => (int)value)],
            [.. Signature().Select(static value => (int)value)],
            [.. GenericParams().Select(static value => (int)value)],
            [.. Constraints().Select(static value => (int)value)],
            1, 6, 1, 1, 1, 2, 2, 1, true, true);
        Assert.That(ValidateHosted(testCase), Is.True);
        return testCase;
    }

    private static TableCase CreateNoFormalCase(TableCase testCase)
    {
        var types = Type();
        WriteWord(types, 20, 0);
        WriteWord(types, 24, 0);
        var signature = Signature();
        WriteWord(signature, 16, 0);
        WriteWord(signature, 20, 0);
        return testCase with {
            TypeDefs = [.. types.Select(static value => (int)value)],
            Signatures = [.. signature.Select(static value => (int)value)],
            GenericParams = [],
            Constraints = [],
            GenericParamCount = 0,
            ConstraintCount = 0,
        };
    }

    private static TableCase CreateGroupedFormalCase(TableCase testCase)
    {
        const int genericParamCount = 512;
        var type = Type();
        WriteWord(type, 24, genericParamCount);
        var signature = Signature();
        WriteWord(signature, 16, 0);
        WriteWord(signature, 20, 0);
        var genericParams = new byte[genericParamCount * 20];
        for (var index = 0; index < genericParamCount; index++)
        {
            var offset = index * 20;
            WriteWord(genericParams, offset, 0x1300_0001);
            WriteWord(genericParams, offset + 4, (uint)index);
            WriteWord(genericParams, offset + 8, 5);
        }

        var groupedCase = testCase with {
            TypeDefs = [.. type.Select(static value => (int)value)],
            Signatures = [.. signature.Select(static value => (int)value)],
            GenericParams = [.. genericParams.Select(static value => (int)value)],
            GenericParamCount = genericParamCount,
            Constraints = [],
            ConstraintCount = 0,
        };
        Assert.That(ValidateHosted(groupedCase), Is.True);
        return groupedCase;
    }

    private static TableCase WithFormalWord(TableCase testCase, int offset, uint value)
    {
        var bytes = ToBytes(testCase.GenericParams);
        WriteWord(bytes, offset, value);
        return testCase with {
            GenericParams = [.. bytes.Select(static item => (int)item)],
            ExpectedValid = false,
        };
    }

    private static TableCase WithFormalBytes(TableCase testCase, byte[] bytes)
        => testCase with {
            GenericParams = [.. bytes.Select(static item => (int)item)],
            ExpectedValid = false,
        };

    private static TableCase WithConstraintWord(TableCase testCase, int offset, uint value)
    {
        var bytes = ToBytes(testCase.Constraints);
        WriteWord(bytes, offset, value);
        return testCase with {
            Constraints = [.. bytes.Select(static item => (int)item)],
            ExpectedValid = false,
        };
    }

    private static TableCase WithConstraintHalfWord(TableCase testCase, int offset, ushort value)
    {
        var bytes = ToBytes(testCase.Constraints);
        WriteHalfWord(bytes, offset, value);
        return testCase with {
            Constraints = [.. bytes.Select(static item => (int)item)],
            ExpectedValid = false,
        };
    }

    private static TableCase WithConstraintBytes(TableCase testCase, byte[] bytes)
        => testCase with {
            Constraints = [.. bytes.Select(static item => (int)item)],
            ExpectedValid = false,
        };

    private static bool ValidateHosted(TableCase testCase)
    {
        try
        {
            var strings = new TrRmStringTableReader(ToBytes(testCase.Strings), testCase.StringCount);
            var modules = new TrRmModuleTableReader(ToBytes(testCase.Modules),
                testCase.ModuleCount, strings, 0, testCase.TypeDefCount,
                testCase.MethodDefCount, 0, 0);
            var types = new TrRmTypeDefTableReader(ToBytes(testCase.TypeDefs),
                testCase.TypeDefCount, strings, modules, 0,
                testCase.GenericParamCount, 0, 0);
            byte[] typeSpecRows = TrRmTypeSpecTableWriter.Encode([
                TrRmTypeSpecTableWriter.Builtin(8),
            ], testCase.TypeDefCount, 0, testCase.GenericParamCount, 0);
            var typeSpecs = new TrRmTypeSpecTableReader(typeSpecRows,
                testCase.TypeSpecCount, testCase.TypeDefCount, 0,
                testCase.GenericParamCount, 0);
            var signatures = new TrRmMethodSigTableReader(ToBytes(testCase.Signatures),
                testCase.SignatureCount, typeSpecs, 0, testCase.GenericParamCount);
            var methods = new TrRmMethodDefTableReader(ToBytes(testCase.Methods),
                testCase.MethodDefCount, strings, modules, types, signatures, 0, 0);
            var formals = new TrRmGenericParamTableReader(ToBytes(testCase.GenericParams),
                testCase.GenericParamCount, strings, types, methods, signatures,
                testCase.ConstraintCount);
            _ = new TrRmConstraintTableReader(ToBytes(testCase.Constraints),
                testCase.ConstraintCount, formals, typeSpecs);
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
        WriteWord(row, 20, 1);
        WriteWord(row, 24, 1);
        return row;
    }

    private static byte[] Method()
    {
        var row = new byte[28];
        WriteWord(row, 4, 4);
        WriteWord(row, 8, 1);
        return row;
    }

    private static byte[] Signature()
    {
        var row = new byte[24];
        WriteWord(row, 0, 1);
        WriteWord(row, 16, 2);
        WriteWord(row, 20, 1);
        return row;
    }

    private static byte[] GenericParams()
    {
        var rows = new byte[40];
        WriteWord(rows, 0, 0x1300_0001);
        WriteWord(rows, 8, 5);
        WriteWord(rows, 12, 1);
        WriteWord(rows, 16, 1);
        WriteWord(rows, 20, 0x1600_0001);
        WriteWord(rows, 28, 6);
        WriteWord(rows, 32, 2);
        WriteWord(rows, 36, 1);
        return rows;
    }

    private static byte[] Constraints()
    {
        var rows = new byte[32];
        WriteWord(rows, 0, 1);
        WriteHalfWord(rows, 4, ushort.MaxValue);
        WriteWord(rows, 8, 1);
        WriteWord(rows, 12, 0xDEAD_BEEF);
        WriteWord(rows, 16, 2);
        WriteHalfWord(rows, 20, 0xFFFE);
        WriteWord(rows, 24, 1);
        WriteWord(rows, 28, uint.MaxValue);
        return rows;
    }

    private static byte[] ToBytes(int[] values)
        => values.Select(static value => checked((byte)value)).ToArray();

    private static void WriteWord(byte[] bytes, int offset, uint value)
        => System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);

    private static void WriteHalfWord(byte[] bytes, int offset, ushort value)
        => System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(offset), value);

    private static async Task<int> ExecuteAotAsync(BoundProgram program, NativeRuntimePolicy policy)
    {
        var path = Path.Combine(TestArtifactPaths.DirectoryPath,
            $"generic-constraint-table-validation-{Guid.NewGuid():N}.exe");
        try
        {
            await File.WriteAllBytesAsync(path, NativeEmitter.Emit(program, policy))
                .ConfigureAwait(true);
            using var process = Process.Start(new ProcessStartInfo(path) {
                UseShellExecute = false,
                CreateNoWindow = true,
            }) ?? throw new InvalidOperationException("Could not start compiled GenericParam/Constraint validator.");
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(true);
            return process.ExitCode;
        }
        finally
        {
            if (File.Exists(path))
            {
                await TestFileCleanup.DeleteWithRetryAsync(path).ConfigureAwait(true);
            }
        }
    }

    private static string RepositoryRoot() =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));

    private sealed record TableCase(int[] Strings, int[] Modules, int[] TypeDefs, int[] Methods,
        int[] Signatures, int[] GenericParams, int[] Constraints, int ModuleCount, int StringCount,
        int TypeDefCount, int MethodDefCount, int SignatureCount, int GenericParamCount,
        int ConstraintCount, int TypeSpecCount, bool ExpectedValid, bool HasHostedOracle);
}
