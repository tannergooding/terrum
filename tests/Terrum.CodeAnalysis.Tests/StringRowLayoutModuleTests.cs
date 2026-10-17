using System;
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

public sealed class StringRowLayoutModuleTests
{
    private const string VerificationSource = """
        namespace Terrum.CodeAnalysis;
        int32 Main() {
            int32 caseCount = Host.Expected.Length / 4;
            for (mut int32 index = 0; index < caseCount; index = index + 1) {
                int32 record = index * 4;
                int32 start = Host.Expected[record];
                int32 length = Host.Expected[record + 1];
                int32 expectedEncoding = Host.Expected[record + 2];
                if (expectedEncoding < 0) {
                    if (TryGetStringRowLayout(start, length, init var encoding, init var contentByteLength)) {
                        return 1;
                    }
                } else {
                    if (!TryGetStringRowLayout(start, length, init var encoding, init var contentByteLength)) {
                        return 2;
                    }
                    if (encoding != expectedEncoding
                        || contentByteLength != Host.Expected[record + 3]) {
                        return 3;
                    }
                }
            }
            return 0;
        }
        """;
    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
    public async Task StringRowLayoutMatchesHostedStructuralValidation(BoundRuntimePolicy policy)
    {
        var fixtures = new[] {
            new RowFixture([0, 6, 0, 0, 65, 110, 115, 119, 101, 114, 0, 0], true, 0, 6, "Answer"),
            new RowFixture([0, 1, 0, 0, 65, 0, 0, 0], true, 0, 1, "A"),
            new RowFixture([1, 2, 0, 0, 195, 169, 0, 0], true, 1, 2, "\u00E9"),
            new RowFixture([2, 2, 0, 0, 61, 216, 0, 222, 0, 0, 0, 0], true, 2, 4, "\uD83D\uDE00"),
            new RowFixture([3, 1, 0, 0, 233, 0, 0, 0, 0, 0, 0, 0], true, 3, 4, "\u00E9"),
            new RowFixture([0, 0, 0, 0, 0, 0, 0, 0], true, 0, 0, null),
            new RowFixture([0, 1, 0, 0, 0, 0, 0, 0], true, 0, 1, null),
            new RowFixture([0, 1, 0, 0, 128, 0, 0, 0], true, 0, 1, null),
            new RowFixture([1, 2, 0, 0, 192, 128, 0, 0], true, 1, 2, null),
            new RowFixture([2, 1, 0, 0, 0, 216, 0, 0], true, 2, 2, null),
            new RowFixture([3, 1, 0, 0, 0, 0, 17, 0, 0, 0, 0, 0], true, 3, 4, null),
            new RowFixture([0, 1, 0, 0, 65, 0, 0], false, -1, -1, null),
            new RowFixture([4, 1, 0, 0, 65, 0, 0, 0], false, -1, -1, null),
            new RowFixture([0, 255, 255, 255, 65, 0, 0, 0], false, -1, -1, null),
            new RowFixture([0, 1, 0, 0, 65, 0, 0, 0, 0, 0, 0, 0], false, -1, -1, null),
            new RowFixture([0, 1, 0, 0, 65, 1, 0, 0], false, -1, -1, null),
            new RowFixture([0, 1, 0, 0, 65, 0, 1, 0], false, -1, -1, null),
            new RowFixture([0, 1, 0, 0, 65, 0, 0, 256], false, -1, -1, null),
            new RowFixture([0, 1, 0, 0, 65, 0, 0, -1], false, -1, -1, null),
        };
        var rowValues = new System.Collections.Generic.List<int>();
        var expected = new System.Collections.Generic.List<int>();
        foreach (var fixture in fixtures)
        {
            var start = rowValues.Count;
            rowValues.AddRange(fixture.Values);
            Assert.That(IsValidShape(fixture.Values), Is.EqualTo(fixture.IsShapeValid));
            if (fixture.Values.All(static value => value is >= 0 and <= byte.MaxValue))
            {
                var hostedRow = fixture.Values.Select(static value => (byte)value).ToArray();
                if (fixture.ExpectedDecodedText is string expectedText)
                {
                    Assert.That(TrRmStringRowReader.DecodeName(hostedRow), Is.EqualTo(expectedText));
                }
                else
                {
                    Assert.Throws<InvalidDataException>(() => TrRmStringRowReader.DecodeName(hostedRow));
                }
            }
            expected.AddRange([
                start,
                fixture.Values.Length,
                fixture.IsShapeValid ? fixture.Encoding : -1,
                fixture.IsShapeValid ? fixture.ContentByteLength : -1,
            ]);
        }
        expected.AddRange([
            -1, 8, -1, -1,
            rowValues.Count, 8, -1, -1,
            0, 7, -1, -1,
            0, int.MaxValue, -1, -1,
            int.MaxValue, 8, -1, -1,
        ]);

        var root = RepositoryRoot();
        var componentPath = SelfHostPaths.ResolveFile(root, "StringRowLayout.terrum");
        var component = SyntaxTree.Parse(await File.ReadAllTextAsync(componentPath).ConfigureAwait(true), componentPath);
        Assert.That(component.HasErrors, Is.False, string.Join(System.Environment.NewLine, component.Diagnostics));
        var entry = SyntaxTree.Parse(VerificationSource, "string-row-layout-module.terrum");
        var compilation = Compilation.Create([component, entry],
        [
            new HostBuffer("Rows", PrimitiveType.Int32, [.. rowValues], false),
            new HostBuffer("Expected", PrimitiveType.Int32, [.. expected], false),
        ], policy);
        Assert.That(compilation.HasErrors, Is.False, string.Join(System.Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual0 = compilation.Program; Assert.That(__nunitActual0, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual0!; }))();
        Assert.That(NativeJit.Execute(program, (NativeRuntimePolicy)policy), Is.EqualTo(0));
        Assert.That(await ExecuteAotAsync(program, (NativeRuntimePolicy)policy).ConfigureAwait(true), Is.EqualTo(0));
    }

    private static string RepositoryRoot() =>
        Path.GetFullPath(Path.Combine(System.AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));

    private static async Task<int> ExecuteAotAsync(BoundProgram program, NativeRuntimePolicy policy)
    {
        var path = Path.Combine(TestArtifactPaths.DirectoryPath, $"string-row-layout-{Guid.NewGuid():N}.exe");
        try
        {
            await File.WriteAllBytesAsync(path, NativeEmitter.Emit(program, policy)).ConfigureAwait(true);
            using var process = Process.Start(new ProcessStartInfo(path) {
                UseShellExecute = false,
                CreateNoWindow = true,
            }) ?? throw new InvalidOperationException("Could not start compiled string-row layout component.");
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

    private static bool IsValidShape(int[] row)
    {
        if (row.Length < 8 || row.Any(static value => value is < 0 or > byte.MaxValue)
            || row[0] > 3)
        {
            return false;
        }

        var width = row[0] switch {
            0 or 1 => 1,
            2 => 2,
            _ => 4,
        };
        var units = row[1] | (row[2] << 8) | (row[3] << 16);
        var contentByteLength = units * width;
        var terminatorEnd = 4 + contentByteLength + width;
        var paddedLength = terminatorEnd + ((4 - (terminatorEnd & 3)) & 3);
        return paddedLength == row.Length && row.Skip(4 + contentByteLength).All(static value => value == 0);
    }

    private sealed record RowFixture(
        int[] Values, bool IsShapeValid, int Encoding, int ContentByteLength, string? ExpectedDecodedText);
}
