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
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests;

public sealed class IntegerParsingComponentTests
{
    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
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
            cases.Add(new(ToBase(uint.MaxValue, radix), radix, false, "uint32.MaxValue", true));
            cases.Add(new(ToBase((ulong)uint.MaxValue + 1, radix), radix, false, null, true));
            cases.Add(new(ToBase((ulong)int.MaxValue, radix), radix, true, "int32.MaxValue", true));
            cases.Add(new("-" + ToBase(1UL << 31, radix), radix, true, "int32.MinValue", true));
            cases.Add(new(ToBase(1UL << 31, radix), radix, true, null, true));
            cases.Add(new("-" + ToBase((1UL << 31) + 1, radix), radix, true, null, true));
            cases.Add(new(ToBase(65535, radix), radix, false, "char16.MaxValue", IsChar: true));
            cases.Add(new(ToBase(65536, radix), radix, false, null, IsChar: true));
            cases.Add(new(ToBase(55296, radix), radix, false, "char16(55296)", IsChar: true));
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
            new("0", 10, false, "0", true),
            new("-0", 10, true, "0", true),
            new("0", 10, false, "char16(0)", IsChar: true),
            new("-1", 10, false, null, IsChar: true),
            new("\u0661", 10, false, null, IsChar: true),
            new("", 10, false, null, IsChar: true),
            new("Ff", 16, false, "255", true),
            new("-1", 10, false, null, true),
            new("\u0661", 10, false, null, true),
            new("1", 37, true, null, true),
            new("", 10, true, null, true),
            new("Z", 36, false, "35"),
            new("z", 36, true, "35"),
            new("Ff", 16, false, "255"),
            new("1__2", 10, false, "12"),
            new("__F_F", 16, false, "255"),
            new("___1__0", 2, false, "2"),
            new("-1__2", 10, true, "-12"),
            new("1_", 10, false, null),
            new("_1", 10, false, null),
            new("1_0", 3, false, null),
            new("02", 2, false, null),
            new("3", 2, false, null),
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

        foreach (var batch in cases.Chunk(64))
        {
            await AssertParseBatchAsync(batch, policy).ConfigureAwait(true);
        }
    }

    private static async Task AssertParseBatchAsync(IReadOnlyList<ParseCase> cases, NativeRuntimePolicy policy)
    {
        var source = new StringBuilder("!");
        var checks = new StringBuilder();
        for (var index = 0; index < cases.Count; index++)
        {
            var test = cases[index];
            var start = source.Length;
            _ = source.Append(test.Text).Append('!');
            _ = checks.AppendLine("{");
            var method = (test.IsSigned, test.IsNarrow, test.IsChar) switch {
                (false, false, false) => "TryParseUInt64Base",
                (true, false, false) => "TryParseInt64Base",
                (false, true, false) => "TryParseUInt32Base",
                (true, true, false) => "TryParseInt32Base",
                (false, false, true) => "TryParseChar16Base",
                _ => throw new InvalidOperationException("Unsupported parse fixture."),
            };
            if (test.Expected is not null)
            {
                _ = checks.AppendLine($"if (!{method}({start}, {test.Text.Length}, {test.Radix}, init var parsed)"
                    + $" || parsed != {test.Expected}) {{ return {index + 1}; }}");
            }
            else
            {
                _ = checks.AppendLine($"if ({method}({start}, {test.Text.Length}, {test.Radix}, init var ignored))"
                    + $" {{ return {index + 1}; }}");
            }
            _ = checks.AppendLine("}");
        }
        var zeroStart = source.Length;
        _ = source.Append('0');
        var oversizedNegativeStart = source.Length;
        _ = source.Append('-');
        var sourceEnd = source.Length;
        _ = checks.AppendLine($"if (TryParseUInt64Base(0, 1, 10, init var reusable)) {{ return 102; }}"
            + $" require TryParseUInt64Base({zeroStart}, 1, 10, init reusable);"
            + " if (reusable != 0) { return 103; }");
        _ = checks.AppendLine("if (TryParseUInt64Base(-1, 1, 10, init var negativeStart)"
            + $" || TryParseInt64Base({oversizedNegativeStart}, 2, 10, init var oversizedNegative)"
            + $" || TryParseInt64Base({sourceEnd}, 1, 10, init var sourceEndStart)"
            + " || TryParseUInt64Base(int32.MaxValue, 1, 10, init var hugeStart)"
            + " || TryParseUInt32Base(int32.MaxValue, 1, 10, init var initHugeStart)"
            + " || TryParseInt64Base(0, int32.MaxValue, 10, init var hugeLength)"
            + " || TryParseInt64Base(0, -1, 10, init var negativeLength)"
            + " || TryParseInt32Base(0, -1, 10, init var initNegativeLength)"
            + " || TryParseChar16Base(0, -1, 10, init var negativeCharLength)) { return 100; }");
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
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual0 = compilation.Program; Assert.That(__nunitActual0, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual0!; }))();
        Assert.That(NativeJit.Execute(program, policy), Is.EqualTo(0));

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
            Assert.That(process.ExitCode, Is.EqualTo(0));
        }
        finally
        {
            await TestFileCleanup.DeleteWithRetryAsync(path).ConfigureAwait(true);
        }
    }
    [TestCase("TryParseUInt64Base(1, 1, 10, init var value); return int32(value);")]
    [TestCase("TryParseInt64Base(1, 1, 10, init var value); return int32(value);")]
    [TestCase("TryParseUInt32Base(1, 1, 10, init var value); return int32(value);")]
    [TestCase("TryParseInt32Base(1, 1, 10, init var value); return value;")]
    [TestCase("TryParseChar16Base(1, 1, 10, init var value); return int32(value);")]
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
        Assert.That(compilation.HasErrors, Is.True);
        Assert.That(compilation.Program, Is.Null);
        Assert.That(global::System.Linq.Enumerable.Any(compilation.SourceDiagnostics,diagnostic => diagnostic.SyntaxTree == entry
            && diagnostic.Diagnostic.Code == "TRM3016"),Is.True);
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

    private sealed record ParseCase(string Text, int Radix, bool IsSigned, string? Expected,
        bool IsNarrow = false, bool IsChar = false);
}
