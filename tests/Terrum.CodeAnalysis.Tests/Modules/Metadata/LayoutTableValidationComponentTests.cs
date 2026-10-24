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

using Terrum.CodeAnalysis.Tests.Infrastructure;

namespace Terrum.CodeAnalysis.Tests.Modules.Metadata;

public sealed class LayoutTableValidationComponentTests
{
    private const string VerificationSource = """
        namespace Terrum.CodeAnalysis;
        int32 Main() {
            bool valid = TryValidateLayoutTable(
                Host.Expected[0], Host.Expected[1], Host.Expected[2],
                Host.Expected[3], Host.Expected[4], Host.Expected[5],
                Host.Expected[6], Host.Expected[7], Host.Expected[8],
                Host.Expected[9], Host.Expected[10], Host.Expected[11],
                Host.Expected[12]);
            if (valid != (Host.Expected[13] != 0)) { return 1; }
            if (!valid) { return 0; }
            if (Host.LayoutOwners.Length != Host.Expected[14]
                || Host.LayoutFieldOwners.Length != Host.Expected[15]) { return 1; }
            for (mut int32 row = 0; row < Host.LayoutOwners.Length; row = row + 1)
            {
                if (Host.LayoutOwners[row] != Host.Expected[16 + row]) { return 1; }
            }
            int32 fieldExpectedStart = 16 + Host.Expected[14];
            for (mut int32 row = 0; row < Host.LayoutFieldOwners.Length; row = row + 1)
            {
                if (Host.LayoutFieldOwners[row]
                    != Host.Expected[fieldExpectedStart + row]) { return 1; }
            }
            return 0;
        }
        """;

    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
    public async Task LayoutValidationMatchesHostedReader(BoundRuntimePolicy policy)
    {
        var cases = new List<TableCase>
        {
            DefaultLayout(),
            BlittableProfileVariant(),
            EmptyTables(),
            InvalidLayout(BlittableProfileVariant(), 0, 2),
            InvalidLayout(BlittableProfileVariant(), 9, 2),
            InvalidLayout(BlittableProfileVariant(), 10, 1),
            InvalidLayout(BlittableProfileVariant(), 12, 3),
            InvalidLayout(BlittableProfileVariant(), 16, 3),
            InvalidLayout(MissingDefaultVariant(), -1, 0),
            InvalidLayout(IncompleteFieldList(), -1, 0),
            InvalidLayout(InvalidFieldOrdinal(), -1, 0),
            InvalidLayout(InvalidFieldOwner(), -1, 0),
            InvalidLayout(DuplicateProfileVariant(), -1, 0),
        };

        var root = RepositoryRoot();
        var components = new List<SyntaxTree>();
        foreach (var name in new[] {
            "sources/Terrum.CodeAnalysis.Terrum/Modules/VariableTableIndex.terrum",
            "sources/Terrum.CodeAnalysis.Terrum/Modules/StringRowLayout.terrum",
            "sources/Terrum.CodeAnalysis.Terrum/Modules/StringRowText.terrum",
            "sources/Terrum.CodeAnalysis.Terrum/Modules/StringTableText.terrum",
            "tests/Terrum.CodeAnalysis.Terrum.Tests/Modules/ModuleTableValidation.terrum",
            "tests/Terrum.CodeAnalysis.Terrum.Tests/Modules/TypeDefTableValidation.terrum",
            "tests/Terrum.CodeAnalysis.Terrum.Tests/Modules/LayoutTableValidation.terrum",
        })
        {
            var path = TerrumProjectPaths.RepositoryFile(root, name);
            var tree = SyntaxTree.Parse(await File.ReadAllTextAsync(path).ConfigureAwait(true), path);
            Assert.That(tree.HasErrors, Is.False, string.Join(Environment.NewLine, tree.Diagnostics));
            components.Add(tree);
        }
        components.Add(SyntaxTree.Parse(VerificationSource, "layout-table-validation.terrum"));

        for (var caseIndex = 0; caseIndex < cases.Count; caseIndex++)
        {
            var testCase = cases[caseIndex];
            Assert.That(ValidateHosted(testCase), Is.EqualTo(testCase.ExpectedValid),
                $"Hosted differential case {caseIndex}");

            var expected = Expected(testCase);
            var compilation = Compilation.Create(components,
            [
                new HostBuffer("Rows", PrimitiveType.Int32, [.. testCase.Strings], false),
                new HostBuffer("Modules", PrimitiveType.Int32, [.. testCase.Modules], false),
                new HostBuffer("TypeDefs", PrimitiveType.Int32, [.. testCase.TypeDefs], false),
                new HostBuffer("FieldDefs", PrimitiveType.Int32, [.. testCase.FieldDefs], false),
                new HostBuffer("Layouts", PrimitiveType.Int32, [.. testCase.Layouts], false),
                new HostBuffer("LayoutFields", PrimitiveType.Int32, [.. testCase.LayoutFields], false),
                new HostBuffer("TargetProfiles", PrimitiveType.Int32, [.. testCase.TargetProfiles], false),
                new HostBuffer("IdentitySlots", PrimitiveType.Int32, [123, 123], true),
                new HostBuffer("FieldOwners", PrimitiveType.Int32,
                    [.. Enumerable.Repeat(123, testCase.FieldCount + 1)], true),
                new HostBuffer("LayoutOwners", PrimitiveType.Int32,
                    [.. Enumerable.Repeat(123, testCase.LayoutCount + 1)], true),
                new HostBuffer("LayoutFieldOwners", PrimitiveType.Int32,
                    [.. Enumerable.Repeat(123, testCase.LayoutFieldCount + 1)], true),
                new HostBuffer("Expected", PrimitiveType.Int32, [.. expected], false),
            ], policy);
            Assert.That(compilation.HasErrors, Is.False,
                string.Join(Environment.NewLine, compilation.SourceDiagnostics.Select(
                    static item => $"{item.SyntaxTree.Source.Path}: {item.Diagnostic}")));
            var program = ((global::System.Func<BoundProgram>)(() => {
                var __nunitActual0 = compilation.Program;
                Assert.That(__nunitActual0, Is.TypeOf<BoundProgram>());
                return (BoundProgram)__nunitActual0!;
            }))();
            Assert.That(NativeJit.Execute(program, (NativeRuntimePolicy)policy) == 0,
                Is.True, $"JIT differential case {caseIndex}.");
            Assert.That(await ExecuteAotAsync(program, (NativeRuntimePolicy)policy)
                    .ConfigureAwait(true) == 0,
                Is.True, $"AOT differential case {caseIndex}.");
        }
    }

    private static TableCase DefaultLayout()
        => CreateCase(typeDefCount: 1, fieldCount: 0, Layouts(
            new LayoutRow(1, 0, 0, 0, 0, 0, 0, 0)), [], [], true,
            [0, 1], [0]);

    private static TableCase BlittableProfileVariant()
        => CreateCase(typeDefCount: 1, fieldCount: 1, Layouts(
            new LayoutRow(1, 0, 1, 0, 0, 16, 1, 1),
            new LayoutRow(1, 1, 1, 1, 0, 16, 2, 1)),
            LayoutFields((1, 0, 1), (2, 0, 1)), TargetProfile(),
            true, [0, 1, 1], [0, 1, 2]);

    private static TableCase EmptyTables()
        => CreateCase(typeDefCount: 0, fieldCount: 0, [], [], [], true, [0], [0]);

    private static TableCase MissingDefaultVariant()
        => CreateCase(typeDefCount: 1, fieldCount: 0, Layouts(
            new LayoutRow(1, 1, 1, 1, 0, 16, 0, 0)), [], TargetProfile(),
            false, [0], [0]);

    private static TableCase IncompleteFieldList()
        => CreateCase(typeDefCount: 1, fieldCount: 1, Layouts(
            new LayoutRow(1, 0, 0, 0, 0, 0, 0, 0)), [], [], false, [0], [0]);

    private static TableCase InvalidFieldOrdinal()
        => CreateCase(typeDefCount: 1, fieldCount: 1, Layouts(
            new LayoutRow(1, 0, 1, 0, 0, 16, 1, 1)),
            LayoutFields((1, 1, 1)), [], false, [0], [0]);

    private static TableCase InvalidFieldOwner()
        => CreateCase(typeDefCount: 1, fieldCount: 1, Layouts(
            new LayoutRow(1, 0, 1, 0, 0, 16, 1, 1)),
            LayoutFields((1, 0, 2)), [], false, [0], [0]);

    private static TableCase DuplicateProfileVariant()
        => CreateCase(typeDefCount: 1, fieldCount: 0, Layouts(
            new LayoutRow(1, 0, 0, 0, 0, 0, 0, 0),
            new LayoutRow(1, 1, 1, 0, 0, 16, 0, 0),
            new LayoutRow(1, 1, 1, 0, 0, 16, 0, 0)),
            [], TargetProfile(), false, [0], [0]);

    private static TableCase InvalidLayout(TableCase source, int offset, byte value)
    {
        var layouts = (int[])source.Layouts.Clone();
        var index = offset < 0 ? layouts.Length - 1 : 28 + offset;
        layouts[index] = value;
        return source with { Layouts = layouts, ExpectedValid = false };
    }

    private static TableCase CreateCase(int typeDefCount, int fieldCount,
        byte[] layoutBytes, byte[] layoutFieldBytes, byte[] targetProfileBytes,
        bool expectedValid, int[] expectedLayoutOwners, int[] expectedLayoutFieldOwners)
    {
        var stringsBytes = TrRmStringTableWriter.EncodeTextRows([
            ("", 0), ("Module", 0), ("Widget", 0), ("Value", 0),
        ]);
        var strings = new TrRmStringTableReader(stringsBytes, 4);
        var moduleBytes = TrRmModuleTableWriter.Encode([
            new TrRmModuleRow(2, 1, 0, 0, 1, 1, 1, 1),
        ],
            strings, 0, typeDefCount, 0, 0);
        var typeDefs = typeDefCount == 0 ? []
            : TrRmTypeDefTableWriter.Encode([
                new TrRmTypeDefRow(1, 3, 4, fieldCount == 0 ? 0 : 1,
                    fieldCount, 0, 0, 1, layoutBytes.Length / 28, 0, 0),
            ], strings,
                new TrRmModuleTableReader(moduleBytes, 1, strings,
                    0, typeDefCount, 0, 0),
                fieldCount, 0, layoutBytes.Length / 28, 0);
        var fieldDefs = fieldCount == 0 ? []
            : FieldDefs(fieldCount);
        return new TableCase(
            [.. stringsBytes.Select(static value => (int)value)],
            [.. moduleBytes.Select(static value => (int)value)],
            [.. typeDefs.Select(static value => (int)value)],
            [.. fieldDefs.Select(static value => (int)value)],
            [.. layoutBytes.Select(static value => (int)value)],
            [.. layoutFieldBytes.Select(static value => (int)value)],
            [.. targetProfileBytes.Select(static value => (int)value)],
            typeDefCount, fieldCount, layoutBytes.Length / 28,
            layoutFieldBytes.Length / 12, targetProfileBytes.Length / 8,
            expectedValid, expectedLayoutOwners, expectedLayoutFieldOwners);
    }

    private static int[] Expected(TableCase testCase)
    {
        var expected = new int[16 + testCase.ExpectedLayoutOwners.Length
            + testCase.ExpectedLayoutFieldOwners.Length];
        expected[0] = 1;
        expected[1] = 4;
        expected[2] = 0;
        expected[3] = testCase.TypeDefCount;
        expected[4] = 0;
        expected[5] = 0;
        expected[6] = 0;
        expected[7] = testCase.FieldCount;
        expected[8] = 0;
        expected[9] = testCase.LayoutCount;
        expected[10] = 0;
        expected[11] = testCase.TargetProfileCount;
        expected[12] = testCase.LayoutFieldCount;
        expected[13] = testCase.ExpectedValid ? 1 : 0;
        expected[14] = testCase.ExpectedLayoutOwners.Length;
        expected[15] = testCase.ExpectedLayoutFieldOwners.Length;
        testCase.ExpectedLayoutOwners.CopyTo(expected, 16);
        testCase.ExpectedLayoutFieldOwners.CopyTo(expected,
            16 + testCase.ExpectedLayoutOwners.Length);
        return expected;
    }

    private static bool ValidateHosted(TableCase testCase)
    {
        try
        {
            var stringsBytes = testCase.Strings.Select(static value => checked((byte)value)).ToArray();
            var strings = new TrRmStringTableReader(stringsBytes, 4);
            var moduleBytes = testCase.Modules.Select(static value => checked((byte)value)).ToArray();
            var modules = new TrRmModuleTableReader(moduleBytes, 1, strings,
                0, testCase.TypeDefCount, 0, 0);
            var typeDefBytes = testCase.TypeDefs.Select(static value => checked((byte)value)).ToArray();
            var types = new TrRmTypeDefTableReader(typeDefBytes, testCase.TypeDefCount,
                strings, modules, testCase.FieldCount, 0, testCase.LayoutCount, 0);
            var profileBytes = testCase.TargetProfiles
                .Select(static value => checked((byte)value)).ToArray();
            var profiles = new TrRmTargetProfileTableReader(profileBytes,
                testCase.TargetProfileCount);
            var typeSpecs = testCase.FieldCount == 0 ? default
                : new TrRmTypeSpecTableReader(
                    [0, 0, 0, 0, 3, 0, 0, 0, 1, 0, 8],
                    1, testCase.TypeDefCount, 0, 0, 0);
            var fieldBytes = testCase.FieldDefs
                .Select(static value => checked((byte)value)).ToArray();
            var fields = testCase.FieldCount == 0 ? default
                : new TrRmFieldDefTableReader(fieldBytes, testCase.FieldCount,
                    strings, types, typeSpecs);
            var layoutBytes = testCase.Layouts.Select(static value => checked((byte)value)).ToArray();
            var layouts = new TrRmLayoutTableReader(layoutBytes, testCase.LayoutCount,
                types, profiles, testCase.LayoutFieldCount);
            var layoutFieldBytes = testCase.LayoutFields
                .Select(static value => checked((byte)value)).ToArray();
            _ = new TrRmLayoutFieldTableReader(layoutFieldBytes,
                testCase.LayoutFieldCount, layouts, types, fields);
            return true;
        }
        catch (InvalidDataException)
        {
            return false;
        }
    }

    private static byte[] FieldDefs(int count)
    {
        var bytes = new byte[count * 16];
        for (var row = 0; row < count; row++)
        {
            WriteWord(bytes, row * 16, 1);
            WriteWord(bytes, row * 16 + 4, 4);
            WriteWord(bytes, row * 16 + 8, 1);
        }
        return bytes;
    }

    private static byte[] Layouts(params LayoutRow[] rows)
    {
        var bytes = new byte[rows.Length * 28];
        for (var row = 0; row < rows.Length; row++)
        {
            var offset = row * 28;
            WriteWord(bytes, offset, (uint)rows[row].Owner);
            WriteWord(bytes, offset + 4, (uint)rows[row].TargetProfile);
            bytes[offset + 8] = rows[row].Kind;
            bytes[offset + 9] = rows[row].Flags;
            WriteWord(bytes, offset + 12, (uint)rows[row].Alignment);
            WriteWord(bytes, offset + 16, (uint)rows[row].Pack);
            WriteWord(bytes, offset + 20, (uint)rows[row].FieldFirst);
            WriteWord(bytes, offset + 24, (uint)rows[row].FieldCount);
        }
        return bytes;
    }

    private static byte[] LayoutFields(params (int Owner, int Ordinal, int Field)[] rows)
    {
        var bytes = new byte[rows.Length * 12];
        for (var row = 0; row < rows.Length; row++)
        {
            var offset = row * 12;
            WriteWord(bytes, offset, (uint)rows[row].Owner);
            WriteWord(bytes, offset + 4, (uint)rows[row].Ordinal);
            WriteWord(bytes, offset + 8, (uint)rows[row].Field);
        }
        return bytes;
    }

    private static byte[] TargetProfile()
        => TrRmTargetProfileTableWriter.Encode([new TrRmTargetProfileRow(1, 1)]);

    private static void WriteWord(byte[] bytes, int offset, uint value)
        => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);

    private static async Task<int> ExecuteAotAsync(BoundProgram program, NativeRuntimePolicy policy)
    {
        var path = Path.Combine(TestArtifactPaths.DirectoryPath, $"layout-table-validation-{Guid.NewGuid():N}.exe");
        try
        {
            await File.WriteAllBytesAsync(path, NativeEmitter.Emit(program, policy)).ConfigureAwait(true);
            using var process = Process.Start(new ProcessStartInfo(path) {
                UseShellExecute = false,
                CreateNoWindow = true,
            }) ?? throw new InvalidOperationException("Could not start compiled Layout table validator.");
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

    private readonly record struct LayoutRow(
        int Owner, int TargetProfile, byte Kind, byte Flags,
        int Alignment, int Pack, int FieldFirst, int FieldCount);

    private sealed record TableCase(int[] Strings, int[] Modules, int[] TypeDefs,
        int[] FieldDefs, int[] Layouts, int[] LayoutFields, int[] TargetProfiles,
        int TypeDefCount, int FieldCount, int LayoutCount, int LayoutFieldCount,
        int TargetProfileCount, bool ExpectedValid, int[] ExpectedLayoutOwners,
        int[] ExpectedLayoutFieldOwners);
}
