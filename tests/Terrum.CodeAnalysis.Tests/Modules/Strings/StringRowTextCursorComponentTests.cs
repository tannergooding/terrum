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

using Terrum.CodeAnalysis.Tests.Infrastructure;

namespace Terrum.CodeAnalysis.Tests.Modules.Strings;

public sealed class StringRowTextCursorComponentTests
{
    private const string ReadAllSource = """
        namespace Terrum.CodeAnalysis;
        int32 Main() {
            if (!StringRowTextCursor.TryCreateName(0, Host.Rows.Length, init var created)) {
                return 1;
            }
            mut StringRowTextCursor cursor = move created;
            mut int32 index = 0;
            mut bool passed = true;
            while (cursor.TryRead(init var scalar)) {
                if (index >= Host.Expected.Length || scalar != Host.Expected[index]) {
                    passed = false;
                }
                index = index + 1;
            }
            if (index != Host.Expected.Length) {
                passed = false;
            }
            init int32 exhausted;
            if (cursor.TryRead(init exhausted) || exhausted is init) {
                passed = false;
            }
            cursor = _;
            return passed ? 0 : 2;
        }
        """;

    private const string EdgeVerificationSource = """
        namespace Terrum.CodeAnalysis;
        int32 Main() {
            if (Host.Expected[0] == 0) {
                if (!StringRowTextCursor.TryCreateNamespace(0, Host.Rows.Length, init var created)) {
                    return 1;
                }
                mut StringRowTextCursor cursor = move created;
                init int32 empty;
                if (cursor.TryRead(init empty) || empty is init) {
                    cursor = _;
                    return 2;
                }
                cursor = _;
                init StringRowTextCursor rejectedName;
                if (StringRowTextCursor.TryCreateName(0, Host.Rows.Length, init rejectedName)
                    || rejectedName is init) {
                    return 3;
                }
            } else {
                init StringRowTextCursor rejectedName;
                if (StringRowTextCursor.TryCreateName(0, Host.Rows.Length, init rejectedName)
                    || rejectedName is init) {
                    return 4;
                }
                init StringRowTextCursor rejectedNamespace;
                if (StringRowTextCursor.TryCreateNamespace(0, Host.Rows.Length, init rejectedNamespace)
                    || rejectedNamespace is init) {
                    return 5;
                }
            }
            return 0;
        }
        """;

    private const string RejectedRowVerificationSource = """
        namespace Terrum.CodeAnalysis;
        int32 Main() {
            int32 caseCount = Host.Expected[0];
            for (mut int32 index = 0; index < caseCount; index = index + 1) {
                int32 expected = 1 + index * 2;
                int32 rowStart = Host.Expected[expected];
                int32 rowLength = Host.Expected[expected + 1];
                init StringRowTextCursor rejectedName;
                if (StringRowTextCursor.TryCreateName(
                        rowStart, rowLength, init rejectedName)
                    || rejectedName is init) {
                    return 1;
                }
                init StringRowTextCursor rejectedNamespace;
                if (StringRowTextCursor.TryCreateNamespace(
                        rowStart, rowLength, init rejectedNamespace)
                    || rejectedNamespace is init) {
                    return 2;
                }
            }
            return 0;
        }
        """;
    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
    public async Task CursorReadsLongRowsSequentiallyAcrossEncodingsAsync(BoundRuntimePolicy policy)
    {
        const string unicodeBlock = "\u0001A\u007F\u0080\u07FF\u0800\uD7FF\uE000\uFFFF\U00010000\U0001F600\U0010FFFF\uFEFF";
        for (var encoding = 0; encoding <= 3; encoding++)
        {
            var text = encoding == 0
                ? new string('A', 4096)
                : string.Concat(Enumerable.Repeat(unicodeBlock, 256));
            var row = EncodeRow(text, encoding);
            var expected = text.EnumerateRunes().Select(static rune => rune.Value).ToArray();
            var program = await CompileAsync(row, expected, ReadAllSource, policy).ConfigureAwait(true);
            Assert.That(NativeJit.Execute(program, (NativeRuntimePolicy)policy), Is.EqualTo(0));
            Assert.That(await ExecuteAotAsync(program, (NativeRuntimePolicy)policy).ConfigureAwait(true), Is.EqualTo(0));
        }
    }
    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
    public async Task CursorRetainsEmptyNamespaceAndCompleteRowValidationAsync(BoundRuntimePolicy policy)
    {
        var emptyNamespace = EncodeRow("", 1);
        var emptyProgram = await CompileAsync(emptyNamespace, [0], EdgeVerificationSource, policy)
            .ConfigureAwait(true);
        Assert.That(NativeJit.Execute(emptyProgram, (NativeRuntimePolicy)policy), Is.EqualTo(0));
        Assert.That(await ExecuteAotAsync(emptyProgram, (NativeRuntimePolicy)policy).ConfigureAwait(true), Is.EqualTo(0));

        byte[] malformedUtf8 = [1, 2, 0, 0, 0x41, 0xC2, 0, 0];
        var malformedProgram = await CompileAsync(malformedUtf8, [1], EdgeVerificationSource, policy)
            .ConfigureAwait(true);
        Assert.That(NativeJit.Execute(malformedProgram, (NativeRuntimePolicy)policy), Is.EqualTo(0));
        Assert.That(await ExecuteAotAsync(malformedProgram, (NativeRuntimePolicy)policy).ConfigureAwait(true), Is.EqualTo(0));
    }
    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
    public async Task CursorRejectsMalformedPhysicalRowsWithoutPublishingAsync(
        BoundRuntimePolicy policy)
    {
        byte[][] malformedRows = [
            [0, 0, 0, 0],
            [4, 0, 0, 0, 0, 0, 0, 0],
            [0, 0, 0, 0, 0, 0, 0, 1],
        ];
        var hostRows = new List<int>();
        var expected = new List<int> { malformedRows.Length + 4 };
        foreach (var row in malformedRows)
        {
            expected.Add(hostRows.Count);
            expected.Add(row.Length);
            hostRows.AddRange(row.Select(static value => (int)value));
        }

        expected.Add(0);
        expected.Add(8);
        expected.Add(hostRows.Count + 1);
        expected.Add(8);
        expected.Add(0);
        expected.Add(int.MaxValue);
        var invalidByteStart = hostRows.Count;
        hostRows.AddRange([0, 0, 0, 0, 0, 0, 0, 0]);
        hostRows[invalidByteStart + 6] = 256;
        expected.Add(invalidByteStart);
        expected.Add(8);

        var program = await CompileRowsAsync(hostRows, expected,
            RejectedRowVerificationSource, policy).ConfigureAwait(true);
        Assert.That(NativeJit.Execute(program, (NativeRuntimePolicy)policy), Is.EqualTo(0));
        Assert.That(await ExecuteAotAsync(program, (NativeRuntimePolicy)policy)
            .ConfigureAwait(true), Is.EqualTo(0));
    }
    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
    public async Task CursorRejectsMalformedTextAcrossEncodingsWithoutPublishingAsync(
        BoundRuntimePolicy policy)
    {
        byte[][] malformedRows = [
            [0, 1, 0, 0, 0x80, 0, 0, 0],
            [1, 1, 0, 0, 0xC2, 0, 0, 0],
            [2, 1, 0, 0, 0, 0xD8, 0, 0],
            [3, 1, 0, 0, 0, 0, 0x11, 0, 0, 0, 0, 0],
        ];
        var hostRows = new List<int>();
        var expected = new List<int> { malformedRows.Length };
        foreach (var row in malformedRows)
        {
            expected.Add(hostRows.Count);
            expected.Add(row.Length);
            hostRows.AddRange(row.Select(static value => (int)value));
        }

        var program = await CompileRowsAsync(hostRows, expected,
            RejectedRowVerificationSource, policy).ConfigureAwait(true);
        Assert.That(NativeJit.Execute(program, (NativeRuntimePolicy)policy), Is.EqualTo(0));
        Assert.That(await ExecuteAotAsync(program, (NativeRuntimePolicy)policy)
            .ConfigureAwait(true), Is.EqualTo(0));
    }

    private static byte[] EncodeRow(string text, int encoding)
    {
        var table = TrRmStringTableWriter.EncodeTextRows([(text, encoding)]);
        return new TrRmVariableTableIndex(table, 1, 8, 4).GetRow(1).ToArray();
    }

    private static async Task<BoundProgram> CompileAsync(byte[] row, int[] expected,
        string source, BoundRuntimePolicy policy)
        => await CompileRowsAsync(row.Select(static value => (int)value).ToArray(),
            expected, source, policy).ConfigureAwait(true);

    private static async Task<BoundProgram> CompileRowsAsync(
        IReadOnlyList<int> rows,
        IReadOnlyList<int> expected,
        string source, BoundRuntimePolicy policy)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
        var names = new[] {
            "sources/Terrum.CodeAnalysis.Terrum/Modules/StringRowLayout.terrum",
            "sources/Terrum.CodeAnalysis.Terrum/Modules/StringRowText.terrum",
            "sources/Terrum.CodeAnalysis.Terrum/Modules/StringRowTextCursor.terrum",
        };
        var components = new SyntaxTree[names.Length];
        for (var index = 0; index < names.Length; index++)
        {
            var path = TerrumProjectPaths.RepositoryFile(root, names[index]);
            components[index] = SyntaxTree.Parse(await File.ReadAllTextAsync(path).ConfigureAwait(true), path);
            Assert.That(components[index].HasErrors, Is.False, string.Join(Environment.NewLine, components[index].Diagnostics));
        }
        var entry = SyntaxTree.Parse(source, "string-row-text-cursor-component.terrum");
        var compilation = Compilation.Create([.. components, entry],
        [
            new HostBuffer("Rows", PrimitiveType.Int32, [.. rows], false),
            new HostBuffer("Expected", PrimitiveType.Int32, [.. expected], false),
        ], policy);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        return ((global::System.Func<BoundProgram>)(() => { var __nunitActual0 = compilation.Program; Assert.That(__nunitActual0, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual0!; }))();
    }

    private static async Task<int> ExecuteAotAsync(BoundProgram program, NativeRuntimePolicy policy)
    {
        var path = Path.Combine(TestArtifactPaths.DirectoryPath, $"string-row-text-cursor-{Guid.NewGuid():N}.exe");
        try
        {
            await File.WriteAllBytesAsync(path, NativeEmitter.Emit(program, policy)).ConfigureAwait(true);
            using var process = Process.Start(new ProcessStartInfo(path) {
                UseShellExecute = false,
                CreateNoWindow = true,
            }) ?? throw new InvalidOperationException("Could not start compiled String-row text cursor component.");
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(true);
            return process.ExitCode;
        }
        finally
        {
            await TestFileCleanup.DeleteWithRetryAsync(path).ConfigureAwait(true);
        }
    }
}
