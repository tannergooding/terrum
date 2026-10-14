using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Terrum.CodeAnalysis.Text;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests;

public sealed class StringRowTextComponentTests
{
    private const string VerificationSource = """
        namespace Terrum.CodeAnalysis;
        int32 Main() {
            int32 caseCount = Host.Expected.Length / 8;
            for (mut int32 index = 0; index < caseCount; index = index + 1) {
                int32 record = index * 8;
                int32 rowStart = Host.Expected[record];
                int32 rowLength = Host.Expected[record + 1];
                bool isName = Host.Expected[record + 2] != 0;
                int32 scalarIndex = Host.Expected[record + 3];
                bool expectedCountSuccess = Host.Expected[record + 4] != 0;
                int32 expectedCount = Host.Expected[record + 5];
                bool expectedReadSuccess = Host.Expected[record + 6] != 0;
                int32 expectedScalar = Host.Expected[record + 7];
                if (isName) {
                    if (expectedCountSuccess) {
                        if (!TryGetStringNameScalarCount(rowStart, rowLength, init var count)
                            || count != expectedCount) {
                            return 1;
                        }
                    } else {
                        if (TryGetStringNameScalarCount(rowStart, rowLength, init var rejectedCount)) {
                            return 2;
                        }
                        if (!TryGetStringNamespaceScalarCount(0, 8, init rejectedCount) || rejectedCount != 3) {
                            return 9;
                        }
                    }

                    if (expectedReadSuccess) {
                        if (!TryReadStringNameScalarAt(rowStart, rowLength, scalarIndex, init var scalar)
                            || scalar != expectedScalar) {
                            return 3;
                        }
                    } else {
                        if (TryReadStringNameScalarAt(rowStart, rowLength, scalarIndex, init var rejectedScalar)) {
                            return 4;
                        }
                        if (!TryReadStringNamespaceScalarAt(0, 8, 0, init rejectedScalar) || rejectedScalar != 1) {
                            return 10;
                        }
                    }
                } else {
                    if (expectedCountSuccess) {
                        if (!TryGetStringNamespaceScalarCount(rowStart, rowLength, init var count)
                            || count != expectedCount) {
                            return 5;
                        }
                    } else {
                        if (TryGetStringNamespaceScalarCount(rowStart, rowLength, init var rejectedCount)) {
                            return 6;
                        }
                        if (!TryGetStringNamespaceScalarCount(0, 8, init rejectedCount) || rejectedCount != 3) {
                            return 11;
                        }
                    }

                    if (expectedReadSuccess) {
                        if (!TryReadStringNamespaceScalarAt(rowStart, rowLength, scalarIndex, init var scalar)
                            || scalar != expectedScalar) {
                            return 7;
                        }
                    } else {
                        if (TryReadStringNamespaceScalarAt(rowStart, rowLength, scalarIndex, init var rejectedScalar)) {
                            return 8;
                        }
                        if (!TryReadStringNamespaceScalarAt(0, 8, 0, init rejectedScalar) || rejectedScalar != 1) {
                            return 12;
                        }
                    }
                }
            }
            return 0;
        }
        """;
    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
    public async Task StringRowTextMatchesHostedStrictDecoding(BoundRuntimePolicy policy)
    {
        var fixtures = new FixtureSet();
        AddValidText(fixtures, EncodeText("\u0001A\u007F", 0), true, "\u0001A\u007F");
        const string unicodeText = "\u0080\u07FF\u0800\uD7FF\uE000\uFFFF\U00010000\U0001F600\U0010FFFF\uFEFF";
        AddValidText(fixtures, EncodeText(unicodeText, 1), true, unicodeText);
        AddValidText(fixtures, EncodeText(unicodeText, 2), true, unicodeText);
        AddValidText(fixtures, EncodeText(unicodeText, 3), true, unicodeText);

        for (var encoding = 0; encoding <= 3; encoding++)
        {
            AddValidText(fixtures, EncodeText("", encoding), false, "");
            AddInvalidText(fixtures, EncodeRow(encoding, []), true);
        }

        AddInvalidText(fixtures, EncodeRow(0, [0]), true);
        AddInvalidText(fixtures, EncodeRow(0, [0x80]), false);
        AddInvalidText(fixtures, EncodeRow(1, [0]), false);
        AddInvalidText(fixtures, EncodeRow(1, [0xC0, 0xAF]), false);
        AddInvalidText(fixtures, EncodeRow(1, [0xE0, 0x80, 0x80]), false);
        AddInvalidText(fixtures, EncodeRow(1, [0xF0, 0x80, 0x80, 0x80]), false);
        AddInvalidText(fixtures, EncodeRow(1, [0xC2]), false);
        AddInvalidText(fixtures, EncodeRow(1, [0xE2, 0x82]), false);
        AddInvalidText(fixtures, EncodeRow(1, [0xF0, 0x9F, 0x92]), false);
        AddInvalidText(fixtures, EncodeRow(1, [0xE2, 0x28, 0xA1]), false);
        AddInvalidText(fixtures, EncodeRow(1, [0xED, 0xA0, 0x80]), false);
        AddInvalidText(fixtures, EncodeRow(1, [0xF4, 0x90, 0x80, 0x80]), false);
        AddInvalidText(fixtures, EncodeRow(1, [(byte)'A', 0xC2]), false);
        AddInvalidText(fixtures, EncodeRow(2, [0, 0]), false);
        AddInvalidText(fixtures, EncodeRow(2, [0, 0xD8]), false);
        AddInvalidText(fixtures, EncodeRow(2, [0, 0xDC]), false);
        AddInvalidText(fixtures, EncodeRow(2, [0, 0xD8, (byte)'A', 0]), false);
        AddInvalidText(fixtures, EncodeRow(2, [(byte)'A', 0, 0, 0xD8]), false);
        AddInvalidText(fixtures, EncodeRow(3, [0, 0, 0, 0]), false);
        AddInvalidText(fixtures, EncodeRow(3, [0, 0xD8, 0, 0]), false);
        AddInvalidText(fixtures, EncodeRow(3, [0, 0, 0x11, 0]), false);
        AddInvalidText(fixtures, EncodeRow(3, [(byte)'A', 0, 0, 0, 0, 0xD8, 0, 0]), false);

        var badTerminator = EncodeRow(0, [(byte)'A']);
        badTerminator[5] = 1;
        AddInvalidText(fixtures, badTerminator, false);

        var badPadding = EncodeRow(0, [(byte)'A']);
        badPadding[6] = 1;
        AddInvalidText(fixtures, badPadding, false);

        var badFlags = EncodeRow(1, [(byte)'A']);
        badFlags[0] = 4;
        AddInvalidText(fixtures, badFlags, false);

        var wrongUnitCount = EncodeRow(1, [(byte)'A']);
        wrongUnitCount[1] = 4;
        AddInvalidText(fixtures, wrongUnitCount, false);

        var root = RepositoryRoot();
        var layoutPath = Path.Combine(root, "self-host", "StringRowLayout.terrum");
        var layout = SyntaxTree.Parse(await File.ReadAllTextAsync(layoutPath).ConfigureAwait(true), layoutPath);
        Assert.That(layout.HasErrors, Is.False, string.Join(Environment.NewLine, layout.Diagnostics));
        var textPath = Path.Combine(root, "self-host", "StringRowText.terrum");
        var text = SyntaxTree.Parse(await File.ReadAllTextAsync(textPath).ConfigureAwait(true), textPath);
        Assert.That(text.HasErrors, Is.False, string.Join(Environment.NewLine, text.Diagnostics));
        var entry = SyntaxTree.Parse(VerificationSource, "string-row-text-component.terrum");
        var compilation = Compilation.Create([layout, text, entry],
        [
            new HostBuffer("Rows", PrimitiveType.Int32, [.. fixtures.Rows], false),
            new HostBuffer("Expected", PrimitiveType.Int32, [.. fixtures.Expected.SelectMany(static item => item)], false),
        ], policy);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual0 = compilation.Program; Assert.That(__nunitActual0, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual0!; }))();
        Assert.That(NativeJit.Execute(program, (NativeRuntimePolicy)policy), Is.EqualTo(0));
        Assert.That(await ExecuteAotAsync(program, (NativeRuntimePolicy)policy).ConfigureAwait(true), Is.EqualTo(0));
    }

    private static byte[] EncodeText(string value, int encoding)
    {
        byte[] table = TrRmStringTableWriter.EncodeTextRows([(value, encoding)]);
        var index = new TrRmVariableTableIndex(table, 1, 8, 4);
        return index.GetRow(1).ToArray();
    }

    private static byte[] EncodeRow(int encoding, byte[] content)
    {
        var width = encoding switch {
            0 or 1 => 1,
            2 => 2,
            3 => 4,
            _ => throw new ArgumentOutOfRangeException(nameof(encoding)),
        };
        if (content.Length % width != 0)
        {
            throw new ArgumentException("The row content must contain complete encoding units.", nameof(content));
        }

        var unitCount = content.Length / width;
        var unpaddedLength = 4 + content.Length + width;
        var row = new byte[(unpaddedLength + 3) & ~3];
        row[0] = checked((byte)encoding);
        row[1] = (byte)unitCount;
        row[2] = (byte)(unitCount >> 8);
        row[3] = (byte)(unitCount >> 16);
        content.CopyTo(row, 4);
        return row;
    }

    private static void AddValidText(FixtureSet fixtures, byte[] row, bool isName, string expectedText)
    {
        var decoded = isName
            ? TrRmStringRowReader.DecodeName(row)
            : TrRmStringRowReader.DecodeNamespace(row);
        Assert.That(decoded, Is.EqualTo(expectedText));
        var scalars = new List<int>();
        foreach (var rune in decoded.EnumerateRunes())
        {
            scalars.Add(rune.Value);
        }

        var rowStart = fixtures.AppendRow(row);
        fixtures.Expected.Add([rowStart, row.Length, isName ? 1 : 0, -1, 1, scalars.Count, 0, -1]);
        for (var index = 0; index < scalars.Count; index++)
        {
            fixtures.Expected.Add([rowStart, row.Length, isName ? 1 : 0, index, 1, scalars.Count, 1, scalars[index]]);
        }
        fixtures.Expected.Add([rowStart, row.Length, isName ? 1 : 0, scalars.Count, 1, scalars.Count, 0, -1]);
        fixtures.Expected.Add([rowStart, row.Length, isName ? 1 : 0, -1, 1, scalars.Count, 0, -1]);
    }

    private static void AddInvalidText(FixtureSet fixtures, byte[] row, bool isName)
    {
        Assert.Throws<InvalidDataException>(() => {
            if (isName)
            {
                TrRmStringRowReader.DecodeName(row);
            }
            else
            {
                TrRmStringRowReader.DecodeNamespace(row);
            }
        });

        var rowStart = fixtures.AppendRow(row);
        fixtures.Expected.Add([rowStart, row.Length, isName ? 1 : 0, -1, 0, -1, 0, -1]);
        fixtures.Expected.Add([rowStart, row.Length, isName ? 1 : 0, 0, 0, -1, 0, -1]);
    }

    private static async Task<int> ExecuteAotAsync(BoundProgram program, NativeRuntimePolicy policy)
    {
        var path = Path.Combine(Path.GetTempPath(), $"string-row-text-{Guid.NewGuid():N}.exe");
        try
        {
            await File.WriteAllBytesAsync(path, NativeEmitter.Emit(program, policy)).ConfigureAwait(true);
            using var process = Process.Start(new ProcessStartInfo(path) {
                UseShellExecute = false,
                CreateNoWindow = true,
            }) ?? throw new InvalidOperationException("Could not start compiled String row text component.");
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

    private sealed class FixtureSet
    {
        public List<int> Rows { get; } = [];
        public List<int[]> Expected { get; } = [];

        public int AppendRow(byte[] row)
        {
            var start = Rows.Count;
            foreach (var value in row)
            {
                Rows.Add(value);
            }

            return start;
        }
    }
}
