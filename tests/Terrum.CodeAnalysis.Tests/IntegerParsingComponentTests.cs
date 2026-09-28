// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed class IntegerParsingComponentTests
{
    [Theory]
    [InlineData(NativeRuntimePolicy.FailFast)]
    [InlineData(NativeRuntimePolicy.CheckedExceptions)]
    public async Task RadixParsingMatchesBoundariesAcrossNativeBackendsAsync(NativeRuntimePolicy policy)
    {
        var cases = new List<ParseCase>();
        for (var radix = 2; radix <= 36; radix++)
        {
            var maximum = ToBase(ulong.MaxValue, radix);
            var signedMaximum = ToBase(long.MaxValue, radix);
            var signedMinimum = ToBase(1UL << 63, radix);
            cases.Add(new(maximum, radix, false, ulong.MaxValue.ToString(CultureInfo.InvariantCulture)));
            cases.Add(new(signedMaximum, radix, true, "int64.MaxValue"));
            cases.Add(new("-" + signedMinimum, radix, true, "int64.MinValue"));
            if (radix is 2 or 3 or 10 or 16 or 36)
            {
                cases.Add(new(maximum + "0", radix, false, null));
                cases.Add(new("-" + ToBase((1UL << 63) + 1, radix), radix, true, null));
                cases.Add(new(signedMinimum, radix, true, null));
            }
        }
        cases.AddRange([
            new("0", 10, false, "0"),
            new("-0", 10, true, "0"),
            new("Z", 36, false, "35"),
            new("z", 36, true, "35"),
            new("Ff", 16, false, "255"),
            new("02", 2, false, null),
            new("3", 2, false, null),
            new("12_3", 10, false, null),
            new("0x1", 16, false, null),
            new(" 3", 10, true, null),
            new("+3", 10, true, null),
            new("-", 10, true, null),
            new("-1", 10, false, null),
            new("\u0661", 10, false, null),
            new("1", 1, false, null),
            new("1", 37, true, null),
            new("", 10, false, null),
        ]);

        var source = new StringBuilder("!");
        var checks = new StringBuilder();
        for (var index = 0; index < cases.Count; index++)
        {
            var test = cases[index];
            var start = source.Length;
            _ = source.Append(test.Text).Append('!');
            _ = checks.AppendLine("{");
            var method = test.IsSigned ? "TryParseInt64Base" : "TryParseUInt64Base";
            if (test.Expected is not null)
            {
                _ = checks.AppendLine($"if (!{method}({start}, {test.Text.Length}, {test.Radix}, out var parsed)"
                    + $" || parsed != {test.Expected}) {{ return {index + 1}; }}");
            }
            else
            {
                _ = checks.AppendLine($"if ({method}({start}, {test.Text.Length}, {test.Radix}, out var ignored))"
                    + $" {{ return {index + 1}; }}");
            }
            _ = checks.AppendLine("}");
        }
        _ = checks.AppendLine("if (TryParseUInt64Base(-1, 1, 10, out var negativeStart)"
            + " || TryParseUInt64Base(int32.MaxValue, 1, 10, out var hugeStart)"
            + " || TryParseInt64Base(0, int32.MaxValue, 10, out var hugeLength)"
            + " || TryParseInt64Base(0, -1, 10, out var negativeLength)) { return 100; }");
        _ = checks.AppendLine("return 0;");

        var root = RepositoryRoot();
        var programSource = SyntaxTree.Parse(
            "namespace Terrum.CodeAnalysis.Numerics64; int32 Main() {\n" + checks + "\n}", "parse-entry.terrum");
        var compilation = Compilation.Create([
            ReadComponent(root, "WideIntegerFacts.terrum"),
            ReadComponent(root, "IntegerParsing.terrum"),
            programSource,
        ], [new HostBuffer("Source", PrimitiveType.Char16,
            [.. source.ToString().Select(unit => (int)unit)], false)], (BoundRuntimePolicy)policy);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = Assert.IsType<BoundProgram>(compilation.Program);
        Assert.Equal(0, NativeJit.Execute(program, policy));

        var path = Path.Combine(root, "artifacts", $"integer-parsing-{Guid.NewGuid():N}.exe");
        try
        {
            await File.WriteAllBytesAsync(path, NativeEmitter.Emit(program, policy)).ConfigureAwait(true);
            using var process = Process.Start(new ProcessStartInfo(path) {
                UseShellExecute = false,
                CreateNoWindow = true,
            }) ?? throw new InvalidOperationException("Could not start compiled integer parser.");
            try
            {
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(true);
            }
            catch (TimeoutException)
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync().ConfigureAwait(true);
                }
                throw;
            }
            Assert.Equal(0, process.ExitCode);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData("TryParseUInt64Base(1, 1, 10, out var value); return int32(value);")]
    [InlineData("TryParseInt64Base(1, 1, 10, out var value); return int32(value);")]
    public void FailedParseCannotPublishOutput(string body)
    {
        ArgumentNullException.ThrowIfNull(body);
        var root = RepositoryRoot();
        var entry = SyntaxTree.Parse("namespace Terrum.CodeAnalysis.Numerics64; int32 Main() { "
            + body + " }", "parse-invalid.terrum");
        var compilation = Compilation.Create([
            ReadComponent(root, "WideIntegerFacts.terrum"),
            ReadComponent(root, "IntegerParsing.terrum"),
            entry,
        ], [new HostBuffer("Source", PrimitiveType.Char16, [49], false)]);
        Assert.True(compilation.HasErrors);
        Assert.Null(compilation.Program);
        Assert.Contains(compilation.SourceDiagnostics, diagnostic => diagnostic.SyntaxTree == entry
            && diagnostic.Diagnostic.Code == "TRM3016");
    }

    private static SyntaxTree ReadComponent(string root, string name) =>
        SyntaxTree.Parse(File.ReadAllText(Path.Combine(root, "self-host", name)), name);

    private static string RepositoryRoot() =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));

    private static string ToBase(ulong number, int radix)
    {
        var value = new BigInteger(number);
        var characters = new char[64];
        var start = characters.Length;
        do
        {
            value = BigInteger.DivRem(value, radix, out var remainder);
            characters[--start] = "0123456789abcdefghijklmnopqrstuvwxyz"[(int)remainder];
        }
        while (value > 0);
        return new string(characters, start, characters.Length - start);
    }

    private sealed record ParseCase(string Text, int Radix, bool IsSigned, string? Expected);
}
