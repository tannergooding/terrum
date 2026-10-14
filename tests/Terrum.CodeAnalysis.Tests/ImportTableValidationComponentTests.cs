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
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests;

public sealed partial class ImportTableValidationComponentTests
{
    private const string VerificationSource = """
        namespace Terrum.CodeAnalysis;
        int32 Main() {
            bool validModuleRefs = TryValidateModuleRefTable(
                Host.Expected[3], Host.Expected[1], Host.Expected[2],
                Host.Expected[3], Host.Expected[4], Host.Expected[5],
                Host.Expected[14], Host.Expected[15]);
            mut bool validDefinitions = false;
            if (Host.Expected[5] > 0) {
                validDefinitions = TryValidateMethodDefTable(
                    Host.Expected[5], Host.Expected[1], Host.Expected[2],
                    Host.Expected[3], Host.Expected[4], Host.Expected[5],
                    Host.Expected[14], Host.Expected[15], Host.Expected[8],
                    Host.Expected[9], Host.Expected[10], Host.Expected[11],
                    Host.Expected[12], Host.Expected[13]);
            } else {
                validDefinitions = TryValidateTypeDefTable(
                    Host.Expected[4], Host.Expected[1], Host.Expected[2],
                    Host.Expected[3], Host.Expected[4], Host.Expected[5],
                    Host.Expected[14], Host.Expected[15], Host.Expected[8],
                    Host.Expected[9], Host.Expected[10], Host.Expected[11]);
            }
            bool validProperties = Host.Expected[6] == 0
                || TryValidatePropertyDefTable(
                    Host.Expected[6], Host.Expected[2], Host.Expected[4],
                    Host.Expected[7], Host.Expected[5], Host.Expected[12]);
            bool validImports = TryValidateImportTable(
                Host.Expected[0], Host.Expected[1], Host.Expected[2],
                Host.Expected[3], Host.Expected[4], Host.Expected[5],
                Host.Expected[6]);
            if (Host.Expected[16] != 0) {
                if (!validModuleRefs) { return 10; }
                if (!validDefinitions) { return 11; }
                if (!validProperties) { return 12; }
                if (!validImports) { return 13; }
            }
            bool valid = validModuleRefs && validDefinitions && validProperties && validImports;
            if (valid != (Host.Expected[16] != 0)) {
                return 1;
            }
            if (!valid) {
                return 0;
            }

            uint32 reference = uint32(Host.Expected[17]);
            bool found = IsExpectedImportDefinition(reference, Host.Expected[0]);
            if (found != (Host.Expected[20] != 0)) {
                return 2;
            }

            if (Host.Expected[20] != 0) {
                if (!TryGetExpectedImportRequirements(reference, Host.Expected[0],
                    init var target, init var policy)) {
                    return 3;
                }
                if (target != uint32(Host.Expected[18])
                    || policy != uint32(Host.Expected[19])) {
                    return 4;
                }
            } else if (TryGetExpectedImportRequirements(reference, Host.Expected[0],
                init var unusedTarget, init var unusedPolicy)) {
                return 3;
            }

            return 0;
        }
        """;
    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
    public async Task ImportValidationMatchesHostedReader(BoundRuntimePolicy policy)
    {
        var valid = CreateValidCase();
        var empty = valid with {
            Imports = [],
            ImportCount = 0,
            ExpectedFound = false,
        };
        var requirements = valid with {
            Modules = SetWord(valid.Modules, 8, 1),
            ModuleRefs = SetWord(valid.ModuleRefs, 8, 1),
            ExpectedTarget = 1,
            ExpectedPolicy = 1,
        };
        var externalRequirements = CreateExternalRequirementsCase(valid);
        var property = CreatePropertyCase(valid);
        var twoDefinitions = valid with {
            Imports = Join(Import(0x1600_0002, 6), Import(0x1300_0002, 8)),
            ImportCount = 2,
            QueryReference = 0x1300_0002,
            ExpectedFound = true,
        };
        var cases = new List<TableCase> {
            valid,
            valid with {
                Imports = Import(0x1300_0002),
                QueryReference = 0x1300_0002,
            },
            property,
            empty,
            requirements,
            externalRequirements with {
                QueryReference = 0x1300_0002,
                ExpectedTarget = 0,
                ExpectedPolicy = 0,
            },
            externalRequirements with {
                QueryReference = 0x1600_0002,
                ExpectedTarget = 1,
                ExpectedPolicy = 1,
            },
            twoDefinitions,
            valid with { Imports = Import(0), ExpectedValid = false },
            valid with { Imports = Import(0x1400_0001), ExpectedValid = false },
            valid with { Imports = Import(0x1300_0003), ExpectedValid = false },
            valid with { Imports = Import(0x1600_0003), ExpectedValid = false },
            valid with { Imports = Import(0x1E00_0001), ExpectedValid = false },
            valid with { Imports = Import(0x1300_0001), ExpectedValid = false },
            valid with {
                Imports = Import(0x1600_0001),
                Modules = [.. Modules(producerOwnsReference: true)
                    .Select(static value => (int)value)],
                ExpectedValid = false,
            },
            property with {
                Imports = Import(0x1E00_0001),
                Methods = SetWord(property.Methods, 24, 1),
                BodyCount = 1,
                BodyOwners = [0, 0],
                ExpectedValid = false,
            },
            valid with {
                Imports = Join(Import(), Import()),
                ImportCount = 2,
                ExpectedValid = false,
            },
        };
        cases.Add(valid with {
            Imports = Import(0x1600_0002, key: 0),
            ExpectedValid = false,
        });
        cases.Add(valid with {
            Imports = Import(0x1600_0002, key: 1),
            ExpectedValid = false,
        });
        cases.Add(valid with {
            Imports = Import(0x1600_0002, key: 99),
            ExpectedValid = false,
        });
        cases.Add(valid with {
            Strings = SetStringByte(valid.Strings, 8, 5, 0x80),
            ExpectedValid = false,
        });
        cases.Add(valid with {
            Imports = Import()[..^1],
            ExpectedValid = false,
            HasHostedOracle = false,
        });
        var invalidByte = Import();
        invalidByte[0] = 256;
        cases.Add(valid with {
            Imports = invalidByte,
            ExpectedValid = false,
            HasHostedOracle = false,
        });
        cases.Add(valid with {
            ModuleRefs = SetWord(valid.ModuleRefs, 4, 2),
            ExpectedValid = false,
        });
        cases.Add(valid with {
            OutputCapacity = 1,
            ExpectedValid = false,
            HasHostedOracle = false,
        });
        cases.Add(valid with {
            DeclaredImportCount = 0,
            ExpectedValid = false,
        });
        cases.Add(valid with {
            TypeDefs = SetWord(valid.TypeDefs, 8, 3),
            ExpectedValid = false,
        });
        cases.Add(property with {
            Properties = SetWord(property.Properties, 16, 3),
            ExpectedValid = false,
        });

        var root = RepositoryRoot();
        var components = new List<SyntaxTree>();
        foreach (var name in new[] {
            "VariableTableIndex.terrum",
            "StringRowLayout.terrum",
            "StringRowText.terrum",
            "ModuleTableValidation.terrum",
            "ModuleRefTableValidation.terrum",
            "TypeDefTableValidation.terrum",
            "MethodDefTableValidation.terrum",
            "PropertyDefTableValidation.terrum",
            "ImportTableValidation.terrum",
        })
        {
            var path = Path.Combine(root, "self-host", name);
            var tree = SyntaxTree.Parse(await File.ReadAllTextAsync(path).ConfigureAwait(true), path);
            Assert.That(tree.HasErrors, Is.False, string.Join(Environment.NewLine, tree.Diagnostics));
            components.Add(tree);
        }
        components.Add(SyntaxTree.Parse(VerificationSource, "import-table-validation.terrum"));

        for (var caseIndex = 0; caseIndex < cases.Count; caseIndex++)
        {
            var testCase = cases[caseIndex];
            if (testCase.HasHostedOracle)
            {
                var hosted = ValidateHosted(testCase);
                Assert.That(hosted.Valid, Is.EqualTo(testCase.ExpectedValid));
                if (hosted.Valid)
                {
                    Assert.That(hosted.Found, Is.EqualTo(testCase.ExpectedFound));
                    if (hosted.Found)
                    {
                        Assert.That(hosted.Target, Is.EqualTo(testCase.ExpectedTarget));
                        Assert.That(hosted.Policy, Is.EqualTo(testCase.ExpectedPolicy));
                    }
                }
            }

            int outputCapacity = testCase.OutputCapacity < 0
                ? testCase.ImportCount + 1
                : testCase.OutputCapacity;
            var compilation = Compilation.Create(components,
            [
                new HostBuffer("Rows", PrimitiveType.Int32, [.. testCase.Strings], false),
                new HostBuffer("Modules", PrimitiveType.Int32, [.. testCase.Modules], false),
                new HostBuffer("ModuleRefs", PrimitiveType.Int32, [.. testCase.ModuleRefs], false),
                new HostBuffer("TypeDefs", PrimitiveType.Int32, [.. testCase.TypeDefs], false),
                new HostBuffer("Methods", PrimitiveType.Int32, [.. testCase.Methods], false),
                new HostBuffer("Properties", PrimitiveType.Int32, [.. testCase.Properties], false),
                new HostBuffer("Signatures", PrimitiveType.Int32, [.. testCase.Signatures], false),
                new HostBuffer("Imports", PrimitiveType.Int32, [.. testCase.Imports], false),
                new HostBuffer("IdentitySlots", PrimitiveType.Int32, [0, 0, 0], true),
                new HostBuffer("FieldOwners", PrimitiveType.Int32, [0], true),
                new HostBuffer("BodyOwners", PrimitiveType.Int32, [.. testCase.BodyOwners], true),
                new HostBuffer("GenericSignatureOwners", PrimitiveType.Int32, [0, 0], true),
                new HostBuffer("ImportExpectedDefinitions", PrimitiveType.Int32,
                    [.. new int[outputCapacity]], true),
                new HostBuffer("ImportExpectedTargets", PrimitiveType.Int32,
                    [.. new int[outputCapacity]], true),
                new HostBuffer("ImportExpectedPolicies", PrimitiveType.Int32,
                    [.. new int[outputCapacity]], true),
                new HostBuffer("Expected", PrimitiveType.Int32,
                [
                    testCase.DeclaredImportCount < 0
                        ? testCase.ImportCount
                        : testCase.DeclaredImportCount,
                    2,
                    testCase.StringCount,
                    testCase.ModuleRefCount,
                    2,
                    2,
                    testCase.PropertyCount,
                    1,
                    0,
                    0,
                    0,
                    0,
                    1,
                    testCase.BodyCount,
                    0,
                    -1,
                    testCase.ExpectedValid ? 1 : 0,
                    checked((int)testCase.QueryReference),
                    checked((int)testCase.ExpectedTarget),
                    checked((int)testCase.ExpectedPolicy),
                    testCase.ExpectedFound ? 1 : 0,
                ], false),
            ], policy);
            Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
            var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual0 = compilation.Program; Assert.That(__nunitActual0, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual0!; }))();
            int jitResult = NativeJit.Execute(program, (NativeRuntimePolicy)policy);
            Assert.That(jitResult == 0, Is.True, $"JIT differential case {caseIndex} expected {testCase.ExpectedValid}, returned {jitResult}.");
            int aotResult = await ExecuteAotAsync(program, (NativeRuntimePolicy)policy)
                .ConfigureAwait(true);
            Assert.That(aotResult == 0, Is.True, $"AOT differential case {caseIndex} expected {testCase.ExpectedValid}, returned {aotResult}.");
        }
    }

    private static TableCase CreateValidCase()
    {
        byte[] strings = TrRmStringTableWriter.EncodeTextRows([
            ("", 0), ("Producer", 0), ("Consumer", 0), ("Widget", 0),
            ("Key", 0), ("Execute", 0), ("Value", 0), ("Key", 1),
        ]);
        var methods = new byte[56];
        WriteWord(methods, 4, 6);
        WriteWord(methods, 8, 1);
        WriteWord(methods, 24, 1);
        WriteWord(methods, 32, 6);
        WriteWord(methods, 36, 1);

        var typeDefs = new byte[80];
        for (var row = 0; row < 2; row++)
        {
            int offset = row * 40;
            WriteWord(typeDefs, offset, 1);
            WriteWord(typeDefs, offset + 4, 4);
            WriteWord(typeDefs, offset + 8, 6);
        }

        var signatures = new byte[24];
        WriteWord(signatures, 0, 1);
        byte[] typeSpecs = [0, 0, 0, 0, 3, 0, 0, 0, 1, 0, 8];

        return new TableCase(
            [.. strings.Select(static value => (int)value)],
            [.. Modules(producerOwnsReference: false).Select(static value => (int)value)],
            [.. ModuleRef().Select(static value => (int)value)],
            [.. typeDefs.Select(static value => (int)value)],
            [.. methods.Select(static value => (int)value)],
            [],
            [.. typeSpecs.Select(static value => (int)value)],
            [.. signatures.Select(static value => (int)value)],
            Import(),
            [0, 0],
            1,
            0,
            1,
            -1,
            0x1600_0002,
            1,
            0,
            true,
            true);
    }

    private static TableCase CreatePropertyCase(TableCase testCase)
    {
        int[] methods = SetWord(SetWord(testCase.Methods, 0, 1), 24, 0);
        var properties = new byte[20];
        WriteWord(properties, 0, 1);
        WriteWord(properties, 4, 7);
        WriteWord(properties, 8, 1);
        WriteWord(properties, 12, 1);
        return testCase with {
            Modules = [.. Modules(producerOwnsReference: true).Select(static value => (int)value)],
            Methods = methods,
            Properties = [.. properties.Select(static value => (int)value)],
            Imports = Import(0x1E00_0001),
            BodyOwners = [0],
            BodyCount = 0,
            PropertyCount = 1,
            QueryReference = 0x1E00_0001,
        };
    }

    private static TableCase CreateExternalRequirementsCase(TableCase testCase)
    {
        byte[] strings = TrRmStringTableWriter.EncodeTextRows([
            ("", 0), ("Producer", 0), ("Consumer", 0), ("Widget", 0),
            ("Key", 0), ("Execute", 0), ("Value", 0), ("Key", 1),
            ("ExternalA", 0), ("ExternalB", 0),
        ]);
        var moduleRefs = new byte[24];
        WriteWord(moduleRefs, 0, 9);
        WriteWord(moduleRefs, 12, 10);
        WriteWord(moduleRefs, 16, 1);
        WriteWord(moduleRefs, 20, 1);
        return testCase with {
            Strings = [.. strings.Select(static value => (int)value)],
            ModuleRefs = [.. moduleRefs.Select(static value => (int)value)],
            Imports = Join(
                Import(0x1600_0002, key: 6, moduleRef: 2),
                Import(0x1300_0002, key: 5, moduleRef: 1)),
            ImportCount = 2,
            ExpectedFound = true,
            StringCount = 10,
            ModuleRefCount = 2,
        };
    }

    private static (bool Valid, bool Found, uint Target, uint Policy) ValidateHosted(
        TableCase testCase)
    {
        try
        {
            var strings = new TrRmStringTableReader(
                ToBytes(testCase.Strings), testCase.StringCount);
            var modules = new TrRmModuleTableReader(ToBytes(testCase.Modules), 2,
                strings, testCase.ModuleRefCount, 2, 2, 0);
            var types = new TrRmTypeDefTableReader(ToBytes(testCase.TypeDefs), 2,
                strings, modules, 0, 0, 0, 0);
            var typeSpecs = new TrRmTypeSpecTableReader(ToBytes(testCase.TypeSpecs),
                1, 2, 0, 0, 0);
            var signatures = new TrRmMethodSigTableReader(ToBytes(testCase.Signatures),
                1, typeSpecs, 0, 0);
            var methods = new TrRmMethodDefTableReader(ToBytes(testCase.Methods), 2,
                strings, modules, types, signatures, 0, testCase.BodyCount);
            var properties = testCase.PropertyCount == 0
                ? default
                : new TrRmPropertyDefTableReader(ToBytes(testCase.Properties),
                    testCase.PropertyCount, strings, types, typeSpecs, methods, signatures);
            var references = new TrRmModuleRefTableReader(ToBytes(testCase.ModuleRefs),
                testCase.ModuleRefCount, strings, modules);
            var imports = new TrRmImportTableReader(ToBytes(testCase.Imports),
                testCase.DeclaredImportCount < 0
                    ? testCase.ImportCount
                    : testCase.DeclaredImportCount,
                strings, references, types, methods, properties);
            bool found = imports.TryGetExpectedRequirements(testCase.QueryReference,
                out var requirements);
            return (true, found, requirements.TargetRequirement, requirements.RuntimePolicy);
        }
        catch (InvalidDataException)
        {
            return (false, false, 0, 0);
        }
        catch (OverflowException)
        {
            return (false, false, 0, 0);
        }
    }

    private static byte[] Modules(bool producerOwnsReference)
    {
        var rows = new byte[88];
        WriteWord(rows, 0, 2);
        WriteWord(rows, 4, 1);
        WriteWord(rows, 28, 1);
        WriteWord(rows, 32, 1);
        WriteWord(rows, 36, 1);
        WriteWord(rows, 40, 1);
        WriteWord(rows, 44, 3);
        WriteWord(rows, 48, 1);
        WriteWord(rows, 72, producerOwnsReference ? 2u : 1u);
        WriteWord(rows, 76, 2);
        WriteWord(rows, 80, 2);
        WriteWord(rows, 84, 1);
        return rows;
    }

    private static byte[] ModuleRef()
    {
        var row = new byte[12];
        WriteWord(row, 0, 2);
        WriteWord(row, 4, 1);
        return row;
    }

    private static int[] Import(uint reference = 0x1600_0002, uint key = 5, uint moduleRef = 1)
    {
        var row = new byte[12];
        WriteWord(row, 0, moduleRef);
        WriteWord(row, 4, key);
        WriteWord(row, 8, reference);
        return [.. row.Select(static value => (int)value)];
    }

    private static int[] SetWord(int[] bytes, int offset, uint value)
    {
        byte[] result = ToBytes(bytes);
        WriteWord(result, offset, value);
        return [.. result.Select(static item => (int)item)];
    }

    private static int[] SetStringByte(int[] bytes, int stringCount, int row, byte value)
    {
        byte[] result = ToBytes(bytes);
        uint rowOffset = BinaryPrimitives.ReadUInt32LittleEndian(
            result.AsSpan((row - 1) * sizeof(uint)));
        int rowStart = (stringCount + 1) * sizeof(uint) + checked((int)rowOffset);
        result[rowStart + 4] = value;
        return [.. result.Select(static item => (int)item)];
    }

    private static int[] Join(int[] first, int[] second)
        => [.. first, .. second];

    private static byte[] ToBytes(int[] values)
        => values.Select(static value => checked((byte)value)).ToArray();

    private static void WriteWord(byte[] bytes, int offset, uint value)
        => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);

    private static async Task<int> ExecuteAotAsync(BoundProgram program, NativeRuntimePolicy policy)
    {
        var path = Path.Combine(Path.GetTempPath(), $"import-table-validation-{Guid.NewGuid():N}.exe");
        try
        {
            await File.WriteAllBytesAsync(path, NativeEmitter.Emit(program, policy)).ConfigureAwait(true);
            using var process = Process.Start(new ProcessStartInfo(path) {
                UseShellExecute = false,
                CreateNoWindow = true,
            }) ?? throw new InvalidOperationException("Could not start compiled Import table validator.");
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

    private sealed record TableCase(
        int[] Strings,
        int[] Modules,
        int[] ModuleRefs,
        int[] TypeDefs,
        int[] Methods,
        int[] Properties,
        int[] TypeSpecs,
        int[] Signatures,
        int[] Imports,
        int[] BodyOwners,
        int ImportCount,
        int PropertyCount,
        int BodyCount,
        int DeclaredImportCount,
        uint QueryReference,
        uint ExpectedTarget,
        uint ExpectedPolicy,
        bool ExpectedValid,
        bool ExpectedFound,
        int OutputCapacity = -1,
        bool HasHostedOracle = true,
        int StringCount = 8,
        int ModuleRefCount = 1);
}
