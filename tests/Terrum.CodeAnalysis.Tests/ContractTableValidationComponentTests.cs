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

public sealed class ContractTableValidationComponentTests
{
    private const string VerificationSource = """
        namespace Terrum.CodeAnalysis;
        int32 Main() {
            bool valid = TryValidateMethodSigParamTables(
                Host.Expected[3], Host.Expected[4], 0, Host.Expected[5],
                Host.Expected[1], 0, 0)
                && TryValidateContractTable(
                    Host.Expected[0], Host.Expected[1], Host.Expected[2],
                    Host.Expected[3], Host.Expected[4], Host.Expected[5]);
            return valid == (Host.Expected[6] != 0) ? 0 : 1;
        }
        """;

    [Theory]
    [InlineData(BoundRuntimePolicy.FailFast)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions)]
    public async Task ContractTableValidationMatchesHostedReader(BoundRuntimePolicy policy)
    {
        var empty = CreateFixture([]);
        var cleanupThenEffect = CreateFixture([
            new Clause(0x1300_0001, 5, Word(1)),
            new Clause(0x1600_0001, 1, Word(1)),
        ]);
        var largeSingletons = CreateLargeSingletonFixture(512);
        var outputState = CreateFixture([
            new Clause(0x1600_0001, 2, OutputState()),
        ], hasOutputParam: true);
        var cases = new List<TestCase> {
            empty,
            cleanupThenEffect,
            outputState,
            CreateFixture([
                new Clause(0x1600_0001, 2, OutputState()),
            ], hasOutputParam: true, paramMode: 2),
            CreateFixture([
                new Clause(0x1600_0001, 1, Word(7)),
            ]),
            CreateFixture([
                new Clause(0x1600_0001, 3, new byte[8]),
            ]),
            CreateFixture([
                new Clause(0x1600_0001, 4, new byte[12]),
            ]),
            Invalid(CreateFixture([
                new Clause(0x1600_0001, 6, []),
            ])),
            Invalid(CreateFixture([
                new Clause(0x1600_0001, 1, Word(8)),
            ])),
            Invalid(CreateFixture([
                new Clause(0x1600_0001, 1, Word(1)),
                new Clause(0x1600_0001, 1, Word(2)),
            ])),
            Invalid(CreateFixture([
                new Clause(0x1300_0001, 5, Word(1)),
                new Clause(0x1300_0001, 5, Word(1)),
            ])),
            Invalid(CreateFixture([
                new Clause(0x1300_0002, 5, Word(1)),
            ])),
            Invalid(CreateFixture([
                new Clause(0x1600_0001, 1, new byte[8]),
            ])),
            Invalid(WithContractByte(CreateFixture([
                new Clause(0x1600_0001, 1, Word(1)),
            ]), 1, 6, 1)),
            Invalid(CreateFixture([
                new Clause(0x1600_0001, 2, OutputState()),
            ], hasOutputParam: true, booleanResult: false)),
            Invalid(CreateFixture([
                new Clause(0x1600_0001, 2, OutputState()),
            ])),
            Invalid(CreateFixture([
                new Clause(0x1600_0001, 2, OutputState()),
            ], hasOutputParam: true, paramMode: 0)),
            Invalid(CreateFixture([
                new Clause(0x1600_0001, 2, OutputState()),
            ], hasOutputParam: true, paramMode: 3)),
            Invalid(CreateFixture([
                new Clause(0x1600_0001, 2, OutputState(parameterOrdinal: 1)),
            ], hasOutputParam: true)),
            Invalid(CreateFixture([
                new Clause(0x1600_0001, 2, OutputState(onTrue: 2)),
            ], hasOutputParam: true)),
            Invalid(CreateFixture([
                new Clause(0x1600_0001, 2, OutputState(reserved: 1)),
            ], hasOutputParam: true)),
            Invalid(CreateFixture([
                new Clause(0x1600_0001, 3, [0, 0, 0, 0, 0, 0, 1, 0]),
            ])),
            Invalid(CreateFixture([
                new Clause(0x1600_0001, 4, new byte[8]),
            ])),
            Invalid(CreateFixture([
                new Clause(0x1300_0001, 5, Word(1)),
            ])),
            Invalid(CreateFixture([
                new Clause(0x1300_0001, 5, Word(2)),
                new Clause(0x1600_0001, 1, Word(1)),
            ])),
            Invalid(cleanupThenEffect with {
                Tables = cleanupThenEffect.Tables with {
                    Methods = SetWord(cleanupThenEffect.Tables.Methods, 0, 2),
                },
            }),
            Invalid(cleanupThenEffect with {
                Tables = cleanupThenEffect.Tables with {
                    Methods = SetWord(cleanupThenEffect.Tables.Methods, 16, 1),
                },
            }),
            Invalid(cleanupThenEffect with {
                Tables = cleanupThenEffect.Tables with {
                    Methods = SetWord(SetWord(cleanupThenEffect.Tables.Methods, 16, 0), 20, 0),
                },
            }),
            Invalid(cleanupThenEffect with {
                Tables = cleanupThenEffect.Tables with { RecordCount = 3 },
            }),
            Invalid(cleanupThenEffect with {
                Tables = cleanupThenEffect.Tables with {
                    Contracts = SetContractOffset(cleanupThenEffect.Tables.Contracts, 0, 1),
                },
            }),
            Invalid(cleanupThenEffect with {
                Tables = cleanupThenEffect.Tables with {
                    Contracts = SetContractOffset(cleanupThenEffect.Tables.Contracts, 2, 99),
                },
            }),
            Invalid(cleanupThenEffect with {
                Tables = cleanupThenEffect.Tables with {
                    Contracts = cleanupThenEffect.Tables.Contracts[..^1],
                },
            }),
            Invalid(empty with {
                Tables = empty.Tables with { Contracts = [0, 0, 0, 0] },
            }),
            InvalidContractByte(cleanupThenEffect, 1, 8, 256),
            largeSingletons,
            Invalid(CreateLargeSingletonFixture(512, duplicateFirst: true)),
        };

        var root = RepositoryRoot();
        var components = new List<SyntaxTree>();
        foreach (var name in new[] {
            "TypeSpecTableValidation.terrum",
            "MethodSigParamTableValidation.terrum",
            "ContractTableValidation.terrum",
        })
        {
            var path = Path.Combine(root, "self-host", name);
            var tree = SyntaxTree.Parse(await File.ReadAllTextAsync(path).ConfigureAwait(true), path);
            Assert.False(tree.HasErrors, string.Join(Environment.NewLine, tree.Diagnostics));
            components.Add(tree);
        }
        components.Add(SyntaxTree.Parse(VerificationSource, "contract-table-validation.terrum"));

        for (var caseIndex = 0; caseIndex < cases.Count; caseIndex++)
        {
            var testCase = cases[caseIndex];
            if (testCase.HasHostedOracle)
            {
                Assert.True(testCase.ExpectedValid == ValidateHosted(testCase.Tables),
                    $"Hosted reader case {caseIndex}");
            }

            var tables = testCase.Tables;
            var contractValues = testCase.ContractHostValues
                ?? [.. tables.Contracts.Select(static value => (int)value)];
            var compilation = Compilation.Create(components,
            [
                new HostBuffer("Contracts", PrimitiveType.Int32,
                    [.. contractValues], false),
                new HostBuffer("TypeDefs", PrimitiveType.Int32,
                    [.. tables.TypeDefs.Select(static value => (int)value)], false),
                new HostBuffer("Methods", PrimitiveType.Int32,
                    [.. tables.Methods.Select(static value => (int)value)], false),
                new HostBuffer("Signatures", PrimitiveType.Int32,
                    [.. tables.Signatures.Select(static value => (int)value)], false),
                new HostBuffer("Params", PrimitiveType.Int32,
                    [.. tables.Params.Select(static value => (int)value)], false),
                new HostBuffer("TypeSpecs", PrimitiveType.Int32,
                    [.. tables.TypeSpecs.Select(static value => (int)value)], false),
                new HostBuffer("ContractOwners", PrimitiveType.Int32,
                    [.. new int[tables.RecordCount + 1]], true),
                new HostBuffer("SingletonKeys", PrimitiveType.Int32,
                    [.. new int[tables.RecordCount + 1]], true),
                new HostBuffer("CleanupMethods", PrimitiveType.Int32,
                    [.. new int[tables.RecordCount + 1]], true),
                new HostBuffer("NoThrowMethods", PrimitiveType.Int32,
                    [.. new int[tables.MethodDefCount + 1]], true),
                new HostBuffer("Expected", PrimitiveType.Int32,
                [
                    tables.RecordCount,
                    tables.TypeDefCount,
                    tables.MethodDefCount,
                    tables.SignatureCount,
                    tables.ParamCount,
                    tables.TypeSpecCount,
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

    private static TestCase CreateFixture(Clause[] clauses, bool hasOutputParam = false,
        byte paramMode = 1, bool booleanResult = true)
    {
        var typeRows = clauses.Select((clause, index) => (clause, index))
            .Where(static item => item.clause.Owner >> 24 == 0x13)
            .Select(static item => item.index + 1).ToArray();
        var methodRows = clauses.Select((clause, index) => (clause, index))
            .Where(static item => item.clause.Owner >> 24 == 0x16)
            .Select(static item => item.index + 1).ToArray();
        byte[] typeDefs = new byte[40];
        WriteWord(typeDefs, 0, 1);
        WriteWord(typeDefs, 4, 3);
        WriteWord(typeDefs, 8, 4);
        WriteWord(typeDefs, 32, typeRows.Length == 0 ? 0u : (uint)typeRows[0]);
        WriteWord(typeDefs, 36, (uint)typeRows.Length);
        byte[] methods = new byte[28];
        WriteWord(methods, 0, 1);
        WriteWord(methods, 4, 4);
        WriteWord(methods, 8, 1);
        WriteWord(methods, 16, methodRows.Length == 0 ? 0u : (uint)methodRows[0]);
        WriteWord(methods, 20, (uint)methodRows.Length);
        byte[] typeSpecs = [
            0, 0, 0, 0,
            3, 0, 0, 0,
            6, 0, 0, 0,
            1, 0, (byte)(booleanResult ? 1 : 8),
            1, 0, 8,
        ];
        byte[] signatures = new byte[24];
        WriteWord(signatures, 0, booleanResult ? 1u : 2u);
        if (hasOutputParam)
        {
            WriteWord(signatures, 8, 1);
            WriteWord(signatures, 12, 1);
        }
        byte[] parameters = hasOutputParam ? new byte[16] : [];
        if (hasOutputParam)
        {
            WriteWord(parameters, 0, 1);
            WriteWord(parameters, 8, 2);
            parameters[12] = paramMode;
        }

        byte[][] rows = clauses.Select(static clause =>
            Contract(clause.Owner, clause.Kind, clause.Payload)).ToArray();
        var contracts = EncodeContractSection(rows);
        return new TestCase(new TableData(contracts, typeDefs, methods, signatures,
            parameters, typeSpecs, clauses.Length, 1, 1, 1,
            hasOutputParam ? 1 : 0, 2), true, true);
    }

    private static TestCase CreateLargeSingletonFixture(int methodCount,
        bool duplicateFirst = false)
    {
        var methods = new byte[methodCount * 28];
        var rows = new List<byte[]>();
        uint firstContract = 1;
        for (var method = 0; method < methodCount; method++)
        {
            var contractCount = duplicateFirst && method == 0 ? 2 : 1;
            var methodOffset = method * 28;
            WriteWord(methods, methodOffset + 4, 4);
            WriteWord(methods, methodOffset + 8, 1);
            WriteWord(methods, methodOffset + 16, firstContract);
            WriteWord(methods, methodOffset + 20, (uint)contractCount);
            for (var index = 0; index < contractCount; index++)
            {
                rows.Add(Contract(0x1600_0000u | (uint)(method + 1), 1, Word(1)));
            }
            firstContract += (uint)contractCount;
        }

        var signatures = new byte[24];
        WriteWord(signatures, 0, 1);
        byte[] typeSpecs = [
            0, 0, 0, 0,
            3, 0, 0, 0,
            1, 0, 1,
        ];
        byte[] contracts = EncodeContractSection([.. rows]);
        var tables = new TableData(contracts, [], methods, signatures, [], typeSpecs,
            rows.Count, 0, methodCount, 1, 0, 1);
        return new TestCase(tables, true, true);
    }

    private static bool ValidateHosted(TableData tables)
    {
        try
        {
            byte[] stringBytes = TrRmStringTableWriter.EncodeTextRows(
                [("", 0), ("Module", 0), ("Widget", 0), ("Run", 0)]);
            var strings = new TrRmStringTableReader(stringBytes, 4);
            byte[] moduleBytes = new byte[44];
            WriteWord(moduleBytes, 0, 2);
            WriteWord(moduleBytes, 4, 1);
            WriteWord(moduleBytes, 28, 1);
            WriteWord(moduleBytes, 32, 1);
            WriteWord(moduleBytes, 36, 1);
            WriteWord(moduleBytes, 40, 1);
            var modules = new TrRmModuleTableReader(moduleBytes, 1, strings,
                0, tables.TypeDefCount, tables.MethodDefCount, 0);
            var types = new TrRmTypeDefTableReader(tables.TypeDefs, tables.TypeDefCount,
                strings, modules, 0, 0, 0, tables.RecordCount);
            var typeSpecs = new TrRmTypeSpecTableReader(tables.TypeSpecs,
                tables.TypeSpecCount, tables.TypeDefCount, 0, 0, 0);
            var signatures = new TrRmMethodSigTableReader(tables.Signatures,
                tables.SignatureCount, typeSpecs, tables.ParamCount, 0);
            var parameters = tables.ParamCount == 0
                ? default
                : new TrRmParamTableReader(tables.Params, tables.ParamCount, signatures, typeSpecs);
            var methods = new TrRmMethodDefTableReader(tables.Methods, tables.MethodDefCount,
                strings, modules, types, signatures, tables.RecordCount, 0);
            _ = new TrRmContractTableReader(tables.Contracts, tables.RecordCount,
                types, methods, signatures, parameters, typeSpecs);
            return true;
        }
        catch (InvalidDataException)
        {
            return false;
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    private static byte[] Contract(uint owner, ushort kind, byte[] payload)
    {
        var row = new byte[8 + payload.Length];
        WriteWord(row, 0, owner);
        BinaryPrimitives.WriteUInt16LittleEndian(row.AsSpan(4), kind);
        payload.CopyTo(row.AsSpan(8));
        return row;
    }

    private static byte[] EncodeContractSection(byte[][] rows)
    {
        if (rows.Length == 0)
        {
            return [];
        }

        int indexLength = (rows.Length + 1) * sizeof(uint);
        int dataLength = rows.Sum(static row => row.Length);
        var section = new byte[indexLength + dataLength];
        int offset = 0;
        for (var row = 0; row < rows.Length; row++)
        {
            WriteWord(section, row * sizeof(uint), (uint)offset);
            rows[row].CopyTo(section.AsSpan(indexLength + offset));
            offset += rows[row].Length;
        }
        WriteWord(section, rows.Length * sizeof(uint), (uint)offset);
        return section;
    }

    private static byte[] Word(uint value)
    {
        var bytes = new byte[4];
        WriteWord(bytes, 0, value);
        return bytes;
    }

    private static byte[] OutputState(uint parameterOrdinal = 0,
        byte onTrue = 1, byte onFalse = 0, byte onExceptional = 0, byte reserved = 0)
    {
        var payload = new byte[8];
        WriteWord(payload, 0, parameterOrdinal);
        payload[4] = onTrue;
        payload[5] = onFalse;
        payload[6] = onExceptional;
        payload[7] = reserved;
        return payload;
    }

    private static byte[] SetWord(byte[] bytes, int offset, uint value)
    {
        byte[] result = bytes.ToArray();
        WriteWord(result, offset, value);
        return result;
    }

    private static byte[] SetContractOffset(byte[] section, int row, uint value)
    {
        byte[] result = section.ToArray();
        WriteWord(result, row * sizeof(uint), value);
        return result;
    }

    private static byte[] SetContractByte(TableData tables, int row, int rowOffset, int value)
    {
        byte[] result = tables.Contracts.ToArray();
        int indexLength = (tables.RecordCount + 1) * sizeof(uint);
        int dataOffset = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(
            result.AsSpan((row - 1) * sizeof(uint))));
        result[indexLength + dataOffset + rowOffset] = checked((byte)value);
        return result;
    }

    private static TestCase WithContractByte(TestCase testCase, int row, int rowOffset, int value)
        => testCase with {
            Tables = testCase.Tables with {
                Contracts = SetContractByte(testCase.Tables, row, rowOffset, value),
            },
        };

    private static TestCase InvalidContractByte(TestCase testCase,
        int row, int rowOffset, int value)
    {
        int[] values = [.. testCase.Tables.Contracts.Select(static item => (int)item)];
        int indexLength = (testCase.Tables.RecordCount + 1) * sizeof(uint);
        int dataOffset = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(
            testCase.Tables.Contracts.AsSpan((row - 1) * sizeof(uint))));
        values[indexLength + dataOffset + rowOffset] = value;
        return new TestCase(testCase.Tables, false, false, values);
    }

    private static void WriteWord(byte[] bytes, int offset, uint value)
        => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);

    private static async Task<int> ExecuteAotAsync(BoundProgram program, NativeRuntimePolicy policy)
    {
        var path = Path.Combine(Path.GetTempPath(), $"contract-table-validation-{Guid.NewGuid():N}.exe");
        try
        {
            await File.WriteAllBytesAsync(path, NativeEmitter.Emit(program, policy))
                .ConfigureAwait(true);
            using var process = Process.Start(new ProcessStartInfo(path) {
                UseShellExecute = false,
                CreateNoWindow = true,
            }) ?? throw new InvalidOperationException("Could not start compiled Contract-table validator.");
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

    private static TestCase Invalid(TestCase testCase)
        => testCase with { ExpectedValid = false };

    private sealed record Clause(uint Owner, ushort Kind, byte[] Payload);

    private sealed record TableData(byte[] Contracts, byte[] TypeDefs, byte[] Methods,
        byte[] Signatures, byte[] Params, byte[] TypeSpecs, int RecordCount,
        int TypeDefCount, int MethodDefCount, int SignatureCount, int ParamCount,
        int TypeSpecCount);

    private sealed record TestCase(TableData Tables, bool ExpectedValid,
        bool HasHostedOracle, int[]? ContractHostValues = null);
}
