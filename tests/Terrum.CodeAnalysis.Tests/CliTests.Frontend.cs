// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Lexing;
using Terrum.CodeAnalysis.Text;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests;

public sealed partial class CliTests
{
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int ScalarBinary(int left, int right);
    [TestCase("int32 Main() { return 42; }",42)]
    [TestCase("int32 Main() { ; return 42; }",42)]
    [TestCase("int32 Main() { return -0xFFFFFFD6; }",42)]
    [TestCase("int32 Main() { if (true) { return 42; } else { return 0; } }",42)]
    [TestCase("int32 Main() { return ((42)); }",42)]
    [TestCase("int32 Main() { if (!false) { return 42; } else { return 0; } }",42)]
    [TestCase("int32 Main() { return 0; }",0)]
    [TestCase("int32 Main() { return 20 + 22; }",42)]
    [TestCase("int32 Main() { return (20) + ((22)); }",42)]
    [TestCase("int32 Main() { if ((true) && (false)) { return 0; } else { return 42; } }",42)]
    [TestCase("int32 Main() { return 84 / 2; }",42)]
    [TestCase("int32 Main() { return 0x2A & 0x3F; }",42)]
    [TestCase("int32 Main() { return 40 ^ 2; }",42)]
    [TestCase("int32 Main() { return +42; }",42)]
    [TestCase("int32 Main() { return -(0xFFFFFFD6); }",42)]
    [TestCase("int32 Main() { if (true) { return 42; } return 0; }",42)]
    [TestCase("int32 Main() { if (false) { return 0; } return 42; }",42)]
    [TestCase("internal int32 Main() { return 42; }",42)]
    [TestCase("int32 Main() { return ~(-43); }",42)]
    [TestCase("int32 Main() { return ~(+(0xFFFFFFD5 + 0)); }",42)]
    [TestCase("int32 Main() { return -(~(41 + 0)); }",42)]
    [TestCase("int32 Main() { return ~(~(40 + 2)); }",42)]
    [TestCase("int32 Main() { if (true || false) { return 42; } return 0; }",42)]
    [TestCase("int32 Main() { if (false && (true || false)) { return 0; } return 6 * (3 + 4); }",42)]
    [TestCase("int32 Main() { if (true == true) { return 42; } return 0; }",42)]
    [TestCase("int32 Main() { char16 value = 'A'; return int32(value); }",65)]
    [TestCase("int32 Main() { char16 value = '\\u0041'; return int32(value); }",65)]
    [TestCase("int32 Main() { char16 value = '\\n'; return int32(value); }",10)]
    [TestCase("int32 Main() { if ('A' == char16(65)) { return 42; } return 0; }",42)]
    [TestCase("int32 Main() { return Select('A'); } int32 Select(char16 value) { return int32(value); }",65)]
    public async Task FrontendRunMatchesHostedJitAsync(string source, int expected)
    {
        var directory = Path.Combine(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", "..")), "self-host");
        foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
        {
            var input = Encoding.UTF8.GetBytes(source);
            var (nativeExit, nativeOutput, nativeError) = await RunAsync(input,
                "frontend-run", directory, "-", "--runtime", policy, "--json");
            var (hostedExit, hostedOutput, hostedError) = await RunAsync(input,
                "jit", "-", "--runtime", policy, "--json");
            Assert.That(hostedExit, Is.EqualTo(expected));
            Assert.That(nativeExit, Is.EqualTo(hostedExit));
            Assert.That(nativeOutput, Is.Empty);
            Assert.That(hostedOutput, Is.Empty);
            Assert.That(nativeError, Is.Empty);
            Assert.That(hostedError, Is.Empty);
        }
    }

    [Test]
    public async Task FrontendRunRejectsUnsupportedEntryAsync()
    {
        var directory = Path.Combine(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", "..")), "self-host");
        var (exit, output, error) = await RunAsync(
            Encoding.UTF8.GetBytes("int32 Other() { return 42; }"),
            "frontend-run", directory, "-", "--runtime", "fail-fast", "--json");
        Assert.That(exit, Is.EqualTo(1));
        Assert.That(error, Is.Empty);
        using var result = JsonDocument.Parse(output);
        Assert.That(result.RootElement.GetProperty("success").GetBoolean(), Is.False);
        Assert.That(result.RootElement.GetProperty("diagnostics")[0]
            .GetProperty("code").GetString(), Is.EqualTo("TRM4002"));
    }

    [Test]
    public async Task FrontendComponentReportsBundledSourceDiagnosticsAsync()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", ".."));
        var sourceDirectory = Path.Combine(root, "self-host");
        var directory = Path.Combine(Path.GetTempPath(), $"terrum-frontend-sources-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            foreach (var file in Directory.GetFiles(sourceDirectory, "*.terrum"))
            {
                File.Copy(file, Path.Combine(directory, Path.GetFileName(file)));
            }
            var frontend = Path.Combine(directory, "ScalarFrontend.terrum");
            File.AppendAllText(frontend, "\nint32 Broken() { return missing; }\n");
            var (exit, output, error) = await RunAsync(
                Encoding.UTF8.GetBytes("int32 Main() { return 42; }"),
                "frontend-component", directory, "-", "--runtime", "fail-fast", "--json");
            Assert.That(exit, Is.EqualTo(1));
            Assert.That(error, Is.Empty);
            using var document = JsonDocument.Parse(output);
            var result = document.RootElement;
            Assert.That(result.GetProperty("schemaVersion").GetInt32(), Is.EqualTo(2));
            Assert.That(result.GetProperty("success").GetBoolean(), Is.False);
            var diagnostics = result.GetProperty("diagnostics");
            Assert.That(diagnostics.EnumerateArray(), Is.Not.Empty);
            var diagnostic = diagnostics[0];
            var index = diagnostic.GetProperty("sourceIndex").GetInt32();
            var source = result.GetProperty("sources")[index];
            Assert.That(source.GetProperty("path").GetString(), Is.EqualTo(frontend));
            Assert.That(diagnostic.GetProperty("span").GetProperty("start").GetInt32()
                > File.ReadAllText(Path.Combine(directory, "SourceRange.terrum")).Length, Is.True);
            Assert.That(diagnostic.GetProperty("range").GetProperty("start")
                .GetProperty("line").GetInt32() > 0, Is.True);
            var (plainExit, plainOutput, plainError) = await RunAsync(
                Encoding.UTF8.GetBytes("int32 Main() { return 42; }"),
                "frontend-component", directory, "-", "--runtime", "fail-fast");
            Assert.That(plainExit, Is.EqualTo(1));
            Assert.That(plainOutput, Is.Empty);
            Assert.That(plainError, Does.Contain(frontend));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
    [TestCase("int32 Main() { return 42; }")]
    [TestCase("public int32 Main() { return 0x2A; }")]
    [TestCase("int32 Main() { if (true) { return 42; } else { return 0; } }")]
    [TestCase("int32 Main() { return -0xFFFFFFD6; }")]
    [TestCase("int32 Main() { return ((42)); }")]
    [TestCase("int32 Main() { if (!false) { return 42; } else { return 0; } }")]
    [TestCase("int32 Main() { return 20 + 22; }")]
    [TestCase("int32 Main() { return (20) + ((22)); }")]
    [TestCase("int32 Main() { if ((true) && (false)) { return 0; } else { return 42; } }")]
    [TestCase("int32 Main() { return 6 * 7; }")]
    [TestCase("int32 Main() { return 84 / 2; }")]
    [TestCase("int32 Main() { return 85 % 43; }")]
    [TestCase("int32 Main() { return 0x2A & 0x3F; }")]
    [TestCase("int32 Main() { return 0 | 42; }")]
    [TestCase("int32 Main() { return 40 ^ 2; }")]
    [TestCase("int32 Main() { return 50 - 8; }")]
    [TestCase("int32 Main() { return +0x2A; }")]
    [TestCase("int32 Main() { return -(0xFFFFFFD6); }")]
    [TestCase("int32 Main() { if (true) { return 42; } return 0; }")]
    [TestCase("int32 Main() { if (false) { return 0; } return 42; }")]
    [TestCase("internal int32 Main() { return 42; }")]
    [TestCase("int32 Main() { if (true && false) { return 0; } else { return 42; } }")]
    [TestCase("int32 Main() { if (true || false) { return 42; } return 0; }")]
    [TestCase("int32 Main() { if (false != true) { return 42; } return 0; }")]
    [TestCase("int32 Main() { if (!((false))) { return 42; } return 0; }")]
    [TestCase("int32 Main() { return ~(0xFFFFFFD5); }")]
    [TestCase("int32 Main() { return +((42)); }")]
    [TestCase("int32 Main() { return ~(-43); }")]
    [TestCase("int32 Main() { return ~(+(0xFFFFFFD5 + 0)); }")]
    [TestCase("int32 Main() { return -(~(41 + 0)); }")]
    [TestCase("int32 Main() { return ~(~(40 + 2)); }")]
    [TestCase("int32 Main() => 6 * (3 + 4);")]
    public async Task FrontendBuiltExecutableMatchesHostedAndRunsAsync(string source)
    {
        await AssertFrontendBuiltExecutableMatchesHostedAndRunsAsync(source);
    }
    [TestCase("int32 Main() { return (42); }","(42)",42)]
    [TestCase("int32 Main() { return ((20 + 22)); }","((20 + 22))",42)]
    [TestCase("int32 Main() { return 6 * (3 + 4); }","(3 + 4)",42)]
    [TestCase("int32 Main() { return 6 * 3 + 4; }","",22)]
    public async Task FrontendGroupedExpressionsPreserveSourceSpansAndHostedEmissionAsync(
        string source, string groupedExpression, int expected)
    {
        var directory = Path.Combine(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", "..")), "self-host");
        var (componentExit, componentOutput, componentError) = await RunAsync(
            Encoding.UTF8.GetBytes(source), "frontend-component", directory, "-",
            "--runtime", "fail-fast", "--json");
        Assert.That(componentExit == 0, Is.True, $"{componentOutput}{componentError}");
        Assert.That(componentError, Is.Empty);
        using (var document = JsonDocument.Parse(componentOutput))
        {
            var result = document.RootElement.GetProperty("result");
            if (groupedExpression.Length == 0)
            {
                if (result.TryGetProperty("nodes", out var ungroupedNodes))
                {
                    Assert.That(global::System.Linq.Enumerable.Any(ungroupedNodes.EnumerateArray(),static node =>
                        node.GetProperty("operation").GetInt32() == 46),Is.False);
                }
            }
            else
            {
                var start = source.IndexOf(groupedExpression, StringComparison.Ordinal);
                Assert.That(start >= 0, Is.True);
                if (result.TryGetProperty("nodes", out var nodes))
                {
                    Assert.That(global::System.Linq.Enumerable.Any(nodes.EnumerateArray(),node =>
                        node.GetProperty("operation").GetInt32() == 46
                        && node.GetProperty("span").GetProperty("start").GetInt32() == start
                        && node.GetProperty("span").GetProperty("length").GetInt32()
                            == groupedExpression.Length),Is.True);
                }
                else
                {
                    var span = result.GetProperty("whenTrue").GetProperty("span");
                    Assert.That(span.GetProperty("start").GetInt32(), Is.EqualTo(start));
                    Assert.That(span.GetProperty("length").GetInt32(), Is.EqualTo(groupedExpression.Length));
                }
            }
        }

        await AssertFrontendBuiltExecutableMatchesHostedAndRunsAsync(source, expected);
    }

    [Test]
    public async Task FrontendBuildMatchesHostedAtMaximumSourceLengthAsync()
    {
        const string program = "int32 Main() { return 42; }";
        var source = program + new string(' ', ScannerHost.MaximumSourceLength - program.Length);
        Assert.That(source.Length, Is.EqualTo(ScannerHost.MaximumSourceLength));
        await AssertFrontendBuiltExecutableMatchesHostedAndRunsAsync(source);
    }

    [Test]
    public async Task FrontendBuildMatchesHostedForMaximumFunctionGraphAtSourceLimitAsync()
    {
        var functions = new string[ScalarFrontendHost.MaximumFunctions];
        for (var index = 0; index < functions.Length - 1; index++)
        {
            var body = index == 0
                ? "Main() => F1()"
                : $"F{index}() => F{index + 1}()";
            functions[index] = $"int32 {body};";
        }
        functions[^1] = $"int32 F{functions.Length - 1}() => 42;";
        var program = string.Join(' ', functions);
        var source = program + new string(' ', ScannerHost.MaximumSourceLength - program.Length);
        Assert.That(Lexer.Lex(SourceText.From(program)).Tokens.Length
            > ScalarFrontendHost.MaximumInvocationTokens, Is.True);
        Assert.That(source.Length, Is.EqualTo(ScannerHost.MaximumSourceLength));

        await AssertFrontendBuiltExecutableMatchesHostedAndRunsAsync(source);
    }
    [TestCase("fail-fast")]
    [TestCase("checked-exceptions")]
    public async Task FrontendBuildRejectsSourceBeyondMaximumLengthAsync(string policy)
    {
        var directory = Path.Combine(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", "..")), "self-host");
        var outputPath = Path.Combine(Path.GetTempPath(), $"terrum-frontend-overlong-{Guid.NewGuid():N}.exe");
        var source = new string(' ', ScannerHost.MaximumSourceLength + 1);
        try
        {
            var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(source), "frontend-build",
                directory, "-", "-o", outputPath, "--runtime", policy, "--json");
            Assert.That(exit, Is.EqualTo(2));
            Assert.That(error, Is.Empty);
            using var document = JsonDocument.Parse(output);
            Assert.That(document.RootElement.GetProperty("diagnostics")[0]
                .GetProperty("code").GetString(), Is.EqualTo("TRM2001"));
            Assert.That(File.Exists(outputPath), Is.False);
        }
        finally
        {
            File.Delete(outputPath);
        }
    }

    private static async Task AssertFrontendBuiltExecutableMatchesHostedAndRunsAsync(
        string source, int expectedExitCode = 42)
    {
        var directory = Path.Combine(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", "..")), "self-host");
        var prefix = Path.Combine(Path.GetTempPath(), $"terrum-native-executable-{Guid.NewGuid():N}");
        var nativePath = prefix + ".native.exe";
        var hostedPath = prefix + ".hosted.exe";
        try
        {
            foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
            {
                var input = Encoding.UTF8.GetBytes(source);
                var (nativeExit, nativeOutput, nativeError) = await RunAsync(input, "frontend-build",
                    directory, "-", "-o", nativePath, "--runtime", policy, "--json");
                var (hostedExit, hostedOutput, hostedError) = await RunAsync(input, "build",
                    "-", "-o", hostedPath, "--runtime", policy, "--json");
                Assert.That(nativeExit == 0, Is.True, $"{policy} frontend build exited {nativeExit}: {nativeOutput}{nativeError}");
                Assert.That(hostedExit == 0, Is.True, $"{policy} hosted build exited {hostedExit}: {hostedOutput}{hostedError}");
                Assert.That(nativeError, Is.Empty);
                Assert.That(hostedError, Is.Empty);
                using (var document = JsonDocument.Parse(nativeOutput))
                {
                    Assert.That(document.RootElement.GetProperty("command").GetString(), Is.EqualTo("frontend-build"));
                    Assert.That(document.RootElement.GetProperty("output").GetString(), Is.EqualTo(nativePath));
                }
                Assert.That(await File.ReadAllBytesAsync(nativePath), Is.EqualTo(await File.ReadAllBytesAsync(hostedPath)));
                using var process = Process.Start(new ProcessStartInfo(nativePath) {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                }) ?? throw new InvalidOperationException("Could not start native frontend executable.");
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20));
                Assert.That(process.ExitCode, Is.EqualTo(expectedExitCode));
            }
        }
        finally
        {
            await TestFileCleanup.DeleteWithRetryAsync(nativePath).ConfigureAwait(true);
            File.Delete(hostedPath);
        }
    }
    [TestCase("int32 Other() { return 42; }")]
    [TestCase("int32 Main(int32 x) { return x; }")]
    [TestCase("uint32 Main() { return 42; }")]
    public async Task FrontendExecutableRejectsUnsupportedEntryWithoutReplacingOutputAsync(
        string source)
    {
        var directory = Path.Combine(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", "..")), "self-host");
        var outputPath = Path.Combine(Path.GetTempPath(), $"terrum-frontend-entry-{Guid.NewGuid():N}.exe");
        try
        {
            var (initial, _, initialError) = await RunAsync(
                Encoding.UTF8.GetBytes("int32 Main() { return 42; }"),
                "frontend-build", directory, "-", "-o", outputPath, "--runtime", "fail-fast", "--json");
            Assert.That(initial, Is.EqualTo(0));
            Assert.That(initialError, Is.Empty);
            var image = await File.ReadAllBytesAsync(outputPath);

            var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(source),
                "frontend-build", directory, "-", "-o", outputPath, "--runtime", "fail-fast", "--json");
            Assert.That(exit, Is.EqualTo(1));
            Assert.That(error, Is.Empty);
            using var document = JsonDocument.Parse(output);
            Assert.That(document.RootElement.GetProperty("success").GetBoolean(), Is.False);
            Assert.That(document.RootElement.GetProperty("diagnostics")[0]
                .GetProperty("code").GetString(), Is.EqualTo("TRM4002"));
            Assert.That(await File.ReadAllBytesAsync(outputPath), Is.EqualTo(image));
        }
        finally
        {
            File.Delete(outputPath);
        }
    }
    [TestCase("public int32 Value(int32 x, int32 y) { return x + y; }")]
    [TestCase("public int32 Value(int32 x, int32 y) { if (x < y) { return x + y; } else { return x - y; } }")]
    [TestCase("public bool Value(int32 x, int32 y) { return x >= y; }")]
    [TestCase("public char16 Value(char16 x, char16 y) { if (x != y) { return y; } else { return x; } }")]
    [TestCase("public bool Value() { return true; }")]
    [TestCase("public bool Value() { return false; }")]
    [TestCase("public bool Value() { if (true) { return false; } else { return true; } }")]
    [TestCase("public int32 Value() { return 42; }")]
    [TestCase("public int32 Value() { return 0xFFFFFFFF; }")]
    [TestCase("public uint32 Value() { return 4294967295; }")]
    [TestCase("public int64 Value() { return 9223372036854775807; }")]
    [TestCase("public uint64 Value() { return 18446744073709551615; }")]
    [TestCase("public char16 Value() { return 0xFFFF; }")]
    [TestCase("public int32 Value() { if (true) { return 0b101010; } else { return 0; } }")]
    [TestCase("public int32 Value() { return -42; }")]
    [TestCase("public int32 Value() { return /* whitespace */ -42; }")]
    [TestCase("public int32 Value() { return -2147483648; }")]
    [TestCase("public int32 Value() { return -0x80000000; }")]
    [TestCase("public int32 Value() { return -(42); }")]
    [TestCase("public int32 Value() { return -(0xFFFFFFD6); }")]
    [TestCase("public int32 Value() { return -(0x80000000); }")]
    [TestCase("public int64 Value() { return -(9223372036854775807); }")]
    [TestCase("public int64 Value() { return -(0xFFFFFFFFFFFFFFD6); }")]
    [TestCase("public int64 Value() { return -9223372036854775808; }")]
    [TestCase("public int64 Value() { return -0xFFFFFFFFFFFFFFFF; }")]
    [TestCase("public int32 Value(int32 x) { return x + 5; }")]
    [TestCase("public int32 Value(int32 x) { return (x) + ((5)); }")]
    [TestCase("public uint32 Value(uint32 x) { return ((7)) * (x); }")]
    [TestCase("public bool Value(char16 x) { return ((x)) != (0xFFFF); }")]
    [TestCase("public bool Value(uint64 x) { return (18446744073709551615) == ((x)); }")]
    [TestCase("public int32 Value() { return (20) + ((22)); }")]
    [TestCase("public bool Value(bool x) { return (x) && ((true)); }")]
    [TestCase("public bool Value(bool x) { return ((false)) != (x); }")]
    [TestCase("public bool Value() { return (true) == ((false)); }")]
    [TestCase("public int32 Value(bool x) { if ((x) || (false)) { return 42; } else { return 0; } }")]
    [TestCase("public int32 Value() { if ((true) && (false)) { return 0; } else { return 42; } }")]
    [TestCase("public uint32 Value() { return (((0xFFFFFFFF) & (42))); }")]
    [TestCase("public int32 Value(int32 x) { return 42 - /* trivia */ ((x)); }")]
    [TestCase("public uint32 Value(uint32 x) { return x - 0xFFFFFFFF; }")]
    [TestCase("public int64 Value(int64 x) { return x + 9223372036854775807; }")]
    [TestCase("public uint64 Value(uint64 x) { return x + 18446744073709551615; }")]
    [TestCase("public int32 Value(int32 x, int32 y) { return y - /* gap */ 0b101; }")]
    [TestCase("public int32 Value(int32 x) { if (x < x) { return x + 42; } else { return x - 5; } }")]
    [TestCase("public int32 Value(int32 x) { return 5 + x; }")]
    [TestCase("public int32 Value(int32 x) { return x + -5; }")]
    [TestCase("public int32 Value(int32 x) { return (-5) + x; }")]
    [TestCase("public int32 Value(int32 x) { return x - ((-5)); }")]
    [TestCase("public bool Value(int32 x) { return x < -5; }")]
    [TestCase("public bool Value(int32 x) { return (-5) < x; }")]
    [TestCase("public bool Value(int32 x) { return x == -2147483648; }")]
    [TestCase("public bool Value(int32 x) { return x == -0xFFFFFFFF; }")]
    [TestCase("public int32 Value(int32 x) { return x + +5; }")]
    [TestCase("public int32 Value(int32 x) { return (+5) + x; }")]
    [TestCase("public int32 Value(int32 x) { return +(5) + x; }")]
    [TestCase("public int32 Value(int32 x) { return ((+(5))) + x; }")]
    [TestCase("public int32 Value(int32 x) { return -(5) + x; }")]
    [TestCase("public int32 Value(int32 x) { return x - (+5); }")]
    [TestCase("public int32 Value(int32 x) { return x - +((5)); }")]
    [TestCase("public int32 Value(int32 x) { return x - -(5); }")]
    [TestCase("public bool Value(int32 x) { return x < +42; }")]
    [TestCase("public bool Value(int32 x) { return (+42) < x; }")]
    [TestCase("public bool Value(int32 x) { return +(42) < x; }")]
    [TestCase("public bool Value(int32 x) { return -(42) < x; }")]
    [TestCase("public bool Value(uint64 x) { return x == +42; }")]
    [TestCase("public bool Value(int32 x) { return x == -(0x80000000); }")]
    [TestCase("public bool Value(int32 x) { return -(0x80000000) == x; }")]
    [TestCase("public bool Value(int64 x) { return x == -9223372036854775808; }")]
    [TestCase("public uint32 Value(uint32 x) { return 0xFFFFFFFF - x; }")]
    [TestCase("public int64 Value(int64 x) { return 9223372036854775807 - x; }")]
    [TestCase("public uint64 Value(uint64 x) { return 18446744073709551615 - x; }")]
    [TestCase("public int32 Value(int32 x, int32 y) { return 47 - /* gap */ y; }")]
    [TestCase("public int32 Value(int32 x) { return ((x + 5)); }")]
    [TestCase("public bool Value() { if (((true))) { return (false); } else { return true; } }")]
    [TestCase("public bool Value() { return !false; }")]
    [TestCase("public bool Value(bool x) { return !x; }")]
    [TestCase("public bool Value(bool x) { return !((x)); }")]
    [TestCase("public bool Value(int32 x, int32 y) { return !(x < y); }")]
    [TestCase("public bool Value(int32 x, int32 y) { return !((x) < ((y))); }")]
    [TestCase("public bool Value(bool x) { return !(x && true); }")]
    [TestCase("public bool Value(bool x) { return !((x) && (true)); }")]
    [TestCase("public bool Value(bool x) { return !(false || x); }")]
    [TestCase("public bool Value() { return !(true == false); }")]
    [TestCase("public int32 Value(bool x) { if (!(x == false)) { return 42; } return 0; }")]
    [TestCase("public bool Value(int32 x) { return !(x < 42); }")]
    [TestCase("public bool Value(int32 x) { return !((x) < (42)); }")]
    [TestCase("public bool Value(int32 x) { return !(42 >= x); }")]
    [TestCase("public bool Value(int32 x) { return !(-42 < x); }")]
    [TestCase("public bool Value(int32 x) { return !(+42 <= x); }")]
    [TestCase("public bool Value(int32 x) { return !(x >= -1); }")]
    [TestCase("public bool Value(uint64 x) { return !(x != 0xFFFFFFFFFFFFFFFF); }")]
    [TestCase("public int32 Value(int32 x, int32 y) { return ~(x + y); }")]
    [TestCase("public int32 Value(int32 x, int32 y) { return ~((x) & (y)); }")]
    [TestCase("public int32 Value(int32 x) { return ~(x + 5); }")]
    [TestCase("public int32 Value(int32 x) { return ~(5 | x); }")]
    [TestCase("public int32 Value() { return ~(0xF ^ 0xA); }")]
    [TestCase("public uint64 Value(uint64 x) { return ~(x & 0xFFFFFFFFFFFFFFFF); }")]
    [TestCase("public int32 Value(int32 x, int32 y) { return +(x + y); }")]
    [TestCase("public int32 Value(int32 x, int32 y) { return -((x) + (y)); }")]
    [TestCase("public int64 Value(int64 x, int64 y) { return -(x & y); }")]
    [TestCase("public uint32 Value(uint32 x) { return +(x | 5); }")]
    [TestCase("public int32 Value(int32 x) { return -(5 ^ x); }")]
    [TestCase("public int32 Value() { return +(42 + 1); }")]
    [TestCase("public int32 Value() { return -(0xF ^ 0xA); }")]
    [TestCase("public int32 Value(int32 x) { return ~(-x); }")]
    [TestCase("public uint32 Value(uint32 x) { return ~(+((x))); }")]
    [TestCase("public int64 Value(int64 x) { return ~(-(x)); }")]
    [TestCase("public int32 Value(int32 x) { return -(~x); }")]
    [TestCase("public int32 Value(int32 x) { return +((~x)); }")]
    [TestCase("public uint64 Value(uint64 x) { return +(~(x)); }")]
    [TestCase("public int32 Value() { return -(~42); }")]
    [TestCase("public int32 Value() { return +((~42)); }")]
    [TestCase("public uint32 Value() { return +(~0xFFFFFFFF); }")]
    [TestCase("public int32 Value(int32 x, int32 y) { return ~(+(x + y)); }")]
    [TestCase("public int64 Value(int64 x, int64 y) { return ~(-(x & y)); }")]
    [TestCase("public uint32 Value(uint32 x) { return ~(+(x | 5)); }")]
    [TestCase("public int32 Value(int32 x) { return ~(-(5 ^ x)); }")]
    [TestCase("public int32 Value() { return ~(+(42 + 1)); }")]
    [TestCase("public int32 Value(int32 x, int32 y) { return +(~(x + y)); }")]
    [TestCase("public int32 Value(int32 x, int32 y) { return -(~(x + y)); }")]
    [TestCase("public uint32 Value(uint32 x) { return +(~(5 | x)); }")]
    [TestCase("public int64 Value(int64 x, int64 y) { return -(~(x & y)); }")]
    [TestCase("public int32 Value() { return +(~(0xF ^ 0xA)); }")]
    [TestCase("public int32 Value(int32 x, int32 y) { return ~(~(x + y)); }")]
    [TestCase("public int64 Value(int64 x, int64 y) { return ~(~((x) & (y))); }")]
    [TestCase("public uint64 Value(uint64 x) { return ~(~(x & 0xFFFFFFFFFFFFFFFF)); }")]
    [TestCase("public int32 Value() { return ~(~(0xF ^ 0xA)); }")]
    [TestCase("public int32 Value() { return ~(-2147483648); }")]
    [TestCase("public int64 Value() { return ~(-9223372036854775808); }")]
    [TestCase("public int32 Value() { return ~(-0xFFFFFFFF); }")]
    [TestCase("public int32 Value() { return ~(-(42)); }")]
    [TestCase("public int32 Value() { return ~(+((42))); }")]
    [TestCase("public bool Value(bool x, bool y) { return !((x && y)); }")]
    [TestCase("public bool Value(bool x, bool y) { return !(((x) || (y))); }")]
    [TestCase("public bool Value(char16 x, char16 y) { return !(x != y); }")]
    [TestCase("public bool Value(bool x) { return !(!x); }")]
    [TestCase("public bool Value(bool x) { return !((!x)); }")]
    [TestCase("public bool Value() { return !(!true); }")]
    [TestCase("public bool Value(int32 x, int32 y) { return !(!(x < y)); }")]
    [TestCase("public bool Value(bool x) { return !(!(x && true)); }")]
    [TestCase("public bool Value(bool x) { return !(!(false || x)); }")]
    [TestCase("public bool Value() { return !(false); }")]
    [TestCase("public bool Value(bool x, bool y) { return !y; }")]
    [TestCase("public int32 Value(bool x) { if (!x) { return 42; } else { return 0; } }")]
    [TestCase("public int32 Value(int32 x, int32 y) { if (!(x >= y)) { return x; } else { return y; } }")]
    [TestCase("public int32 Value(int32 x, int32 y) { if (!((x) >= (y))) { return x; } else { return y; } }")]
    [TestCase("public int32 Value(bool x, bool y) { if (!((x || y))) { return 42; } else { return 0; } }")]
    [TestCase("public bool Value(int32 x) { return x >= 42; }")]
    [TestCase("public bool Value(uint64 x) { return x == 18446744073709551615; }")]
    [TestCase("public bool Value(char16 x) { return x != 0xFFFF; }")]
    [TestCase("public bool Value(int32 x) { return 42 < x; }")]
    [TestCase("public bool Value(char16 x) { return 0xFFFF != x; }")]
    [TestCase("public int32 Value(int32 x) { if (x < 42) { return x; } else { return 42; } }")]
    [TestCase("public int32 Value(int32 x, int32 y) { return x * y; }")]
    [TestCase("public int32 Value(int32 x, int32 y) { return x + ((y)); }")]
    [TestCase("public int32 Value(int32 x, int32 y) { return ((x)) + y; }")]
    [TestCase("public int32 Value(int32 x, int32 y) { return (((x) + (y))); }")]
    [TestCase("public bool Value(bool x, bool y) { return ((x)) == ((y)); }")]
    [TestCase("public int32 Value(bool x, bool y) { if ((x) && (y)) { return 42; } else { return 0; } }")]
    [TestCase("public uint64 Value(uint64 x, uint64 y) { return x ^ /* gap */ ((y)); }")]
    [TestCase("public bool Value(int32 x, int32 y) { return x >= (y); }")]
    [TestCase("public bool Value(bool x, bool y) { return x && ((y)); }")]
    [TestCase("public int32 Value(bool x, bool y) { if (x || (y)) { return 42; } else { return 0; } }")]
    [TestCase("public uint64 Value(uint64 x, uint64 y) { return x / y; }")]
    [TestCase("public int32 Value(int32 x, int32 y) { return x % y; }")]
    [TestCase("public int32 Value(int32 x) { return x * 7; }")]
    [TestCase("public uint32 Value(uint32 x) { return x / 0xFFFF; }")]
    [TestCase("public int64 Value(int64 x) { return x % 9223372036854775807; }")]
    [TestCase("public uint64 Value(uint64 x) { return 7 * x; }")]
    [TestCase("public int32 Value(int32 x) { return 42 / x; }")]
    [TestCase("public uint64 Value(uint64 x) { return 18446744073709551615 % x; }")]
    [TestCase("public int32 Value(int32 x, int32 y) { return x & y; }")]
    [TestCase("public uint32 Value(uint32 x, uint32 y) { return x | y; }")]
    [TestCase("public int64 Value(int64 x, int64 y) { return x ^ y; }")]
    [TestCase("public uint64 Value(uint64 x) { return x & 0xFF; }")]
    [TestCase("public int32 Value(int32 x) { return x | 0xFFFFFFFF; }")]
    [TestCase("public uint64 Value(uint64 x) { return x ^ 0xFFFFFFFFFFFFFFFF; }")]
    [TestCase("public int32 Value(int32 x) { return 0xFF & x; }")]
    [TestCase("public uint64 Value(uint64 x) { return 18446744073709551615 | x; }")]
    [TestCase("public uint32 Value(uint32 x) { return 0b101010 ^ x; }")]
    [TestCase("public int32 Value(int32 x) { return ~x; }")]
    [TestCase("public uint32 Value(uint32 x, uint32 y) { return ~y; }")]
    [TestCase("public int64 Value(int64 x) { return ~x; }")]
    [TestCase("public uint64 Value(uint64 x) { return ~x; }")]
    [TestCase("public int32 Value() { return ~0xFFFFFFD5; }")]
    [TestCase("public uint32 Value() { return ~4294967295; }")]
    [TestCase("public int32 Value(int32 x) { return ~((x)); }")]
    [TestCase("public int32 Value() { return ~(0xFFFFFFD5); }")]
    [TestCase("public int64 Value() { return ~0xFFFFFFFFFFFFFFFF; }")]
    [TestCase("public uint64 Value() { return ~0xFFFFFFFFFFFFFFFF; }")]
    [TestCase("public bool Value(bool x, bool y) { return x && y; }")]
    [TestCase("public bool Value(bool x, bool y) { return x || y; }")]
    [TestCase("public bool Value(bool x, bool y) { return x & y; }")]
    [TestCase("public bool Value(bool x, bool y) { return x | y; }")]
    [TestCase("public bool Value(bool x, bool y) { return x ^ y; }")]
    [TestCase("public bool Value(bool x, bool y) { return x == y; }")]
    [TestCase("public bool Value(bool x, bool y) { return x != y; }")]
    [TestCase("public bool Value(bool x) { return x && true; }")]
    [TestCase("public bool Value(bool x) { return false && x; }")]
    [TestCase("public bool Value(bool x) { return x || false; }")]
    [TestCase("public bool Value(bool x) { return true || x; }")]
    [TestCase("public bool Value(bool x) { return x & false; }")]
    [TestCase("public bool Value(bool x) { return true | x; }")]
    [TestCase("public bool Value(bool x) { return x ^ true; }")]
    [TestCase("public bool Value(bool x) { return false ^ x; }")]
    [TestCase("public bool Value() { return true && false; }")]
    [TestCase("public bool Value() { return false || true; }")]
    [TestCase("public bool Value() { return true & true; }")]
    [TestCase("public bool Value() { return false | false; }")]
    [TestCase("public bool Value() { return true ^ false; }")]
    [TestCase("public bool Value(bool x) { return x == true; }")]
    [TestCase("public bool Value(bool x) { return false != x; }")]
    [TestCase("public bool Value() { return true == false; }")]
    [TestCase("public bool Value() { return false != true; }")]
    [TestCase("public int32 Value(bool x) { if (x || false) { return 42; } else { return 0; } }")]
    [TestCase("public int32 Value(bool x) { if (true && x) { return 42; } return 0; }")]
    [TestCase("public int32 Value(bool x, bool y) { if (x && y) { return 42; } else { return 0; } }")]
    [TestCase("public int32 Value(int32 x) { return -x; }")]
    [TestCase("public int64 Value(int64 x, int64 y) { return -y; }")]
    [TestCase("public int32 Value(int32 x) { return -(x); }")]
    [TestCase("public int32 Value(int32 x) { return +((x)); }")]
    [TestCase("public int32 Value(int32 x) { return +x; }")]
    [TestCase("public uint32 Value(uint32 x) { return +x; }")]
    [TestCase("public int64 Value(int64 x) { return +x; }")]
    [TestCase("public uint64 Value(uint64 x) { return +x; }")]
    [TestCase("public int32 Value() { return 20 + 22; }")]
    [TestCase("public uint32 Value() { return 0xFFFFFFFF & 42; }")]
    [TestCase("public int32 Value() { return 84 / 2; }")]
    [TestCase("public uint32 Value() { return 0b101010 ^ 0; }")]
    [TestCase("public int32 Value() { return +42; }")]
    [TestCase("public int32 Value() { return +((42)); }")]
    [TestCase("public uint32 Value() { return +(4294967295); }")]
    [TestCase("public int32 Value() { return +0xFFFFFFFF; }")]
    [TestCase("public uint32 Value() { return +4294967295; }")]
    [TestCase("public int64 Value() { return +9223372036854775807; }")]
    [TestCase("public uint64 Value() { return +18446744073709551615; }")]
    [TestCase("public int32 Value(bool x) { if (x) { return 42; } return 0; }")]
    [TestCase("public int32 Value(int32 x) { if (x < 42) { return x; } return 42; }")]
    [TestCase("public int32 Value(int32 x, int32 y, int32 z) { return z; }")]
    [TestCase("public int32 Value(int32 x, int32 y, int32 z) { return x + z; }")]
    [TestCase("public int32 Value(int32 x, int32 y, int32 z) { return z - y; }")]
    [TestCase("public int32 Value(int32 x, int32 y, int32 z) { return -((z)); }")]
    [TestCase("public uint32 Value(uint32 x, uint32 y, uint32 z) { return ~z; }")]
    [TestCase("public bool Value(bool x, bool y, bool z) { return !z; }")]
    [TestCase("public int32 Value(int32 x, int32 y, int32 z) { return z + 5; }")]
    [TestCase("public int32 Value(int32 x, int32 y, int32 z) { return (-5) + z; }")]
    [TestCase("public int32 Value(int32 x, int32 y, int32 z) { return ~(z + 5); }")]
    [TestCase("public int32 Value(int32 x, int32 y, int32 z) { return -(~(z + 5)); }")]
    [TestCase("public int32 Value(int32 x, int32 y, int32 z) { return ~(~(z + 5)); }")]
    [TestCase("public int32 Value(int32 x, int32 y, int32 z) { return ~(-(z + 5)); }")]
    [TestCase("public int32 Value(int32 x, int32 y, int32 z) { return +(~(z + 5)); }")]
    [TestCase("public bool Value(int32 x, int32 y, int32 z) { return !(z < -5); }")]
    [TestCase("public bool Value(bool x, bool y, bool z) { return z && x; }")]
    [TestCase("public bool Value(bool x, bool y, bool z) { return !((false || z)); }")]
    [TestCase("public int32 Value(bool x, int32 y, int32 z) { if (x) { return z + 5; } return y; }")]
    [TestCase("public int64 Value(int64 x, int64 y, int64 z) { return -9223372036854775808 + z; }")]
    [TestCase("public char16 Value(char16 x, char16 y, char16 z) { return z; }")]
    [TestCase("public bool Value(char16 x, char16 y, char16 z) { return z == 42; }")]
    [TestCase("public bool Value(bool x, bool y, bool z) { return z && true; }")]
    [TestCase("public bool Value(bool x, bool y, bool z) { return false || z; }")]
    [TestCase("public bool Value(bool x, bool y, bool z) { return z == true; }")]
    [TestCase("public bool Value(bool x, bool y, bool z) { return !((z && false)); }")]
    [TestCase("public uint64 Value(uint64 x, uint64 y, uint64 z) { return 0xFFFFFFFFFFFFFFFF ^ z; }")]
    [TestCase("public int32 Value(int32 x, int32 y, int32 z) { return z + -2147483648; }")]
    [TestCase("public bool Value(int64 x, int64 y, int64 z) { return z == -9223372036854775808; }")]
    [TestCase("public int32 Value() { for (;;) { break; } return 42; }")]
    public async Task FrontendBuiltLibraryMatchesHostedNativeBytesAsync(string source)
    {
        var directory = Path.Combine(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", "..")), "self-host");
        var prefix = Path.Combine(Path.GetTempPath(), $"terrum-native-parity-{Guid.NewGuid():N}");
        var nativePath = prefix + ".native.dll";
        var hostedPath = prefix + ".hosted.dll";
        try
        {
            foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
            {
                var input = Encoding.UTF8.GetBytes(source);
                var (nativeExit, _, nativeError) = await RunAsync(input, "frontend-build-library",
                    directory, "-", "-o", nativePath, "--runtime", policy, "--json");
                var (hostedExit, _, hostedError) = await RunAsync(input, "build-library",
                    "-", "--export", "Value", "-o", hostedPath, "--runtime", policy, "--json");
                Assert.That(nativeExit, Is.EqualTo(0));
                Assert.That(hostedExit, Is.EqualTo(0));
                Assert.That(nativeError, Is.Empty);
                Assert.That(hostedError, Is.Empty);
                Assert.That(await File.ReadAllBytesAsync(nativePath), Is.EqualTo(await File.ReadAllBytesAsync(hostedPath)));
            }
        }
        finally
        {
            File.Delete(nativePath);
            File.Delete(hostedPath);
        }
    }
    [TestCase("fail-fast")]
    [TestCase("checked-exceptions")]
    public async Task FrontendBuiltLibraryExportsPublicScalarAndPreservesOutputOnFailureAsync(
        string policy)
    {
        var directory = Path.Combine(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", "..")), "self-host");
        var outputPath = Path.Combine(Path.GetTempPath(), $"terrum-native-frontend-{Guid.NewGuid():N}.dll");
        try
        {
            var (exit, output, error) = await RunAsync(
                Encoding.UTF8.GetBytes("public int32 Value(int32 x, int32 y) { return x + y; }"),
                "frontend-build-library", directory, "-", "-o", outputPath, "--runtime", policy, "--json");
            Assert.That(exit, Is.EqualTo(0));
            Assert.That(error, Is.Empty);
            using (var document = JsonDocument.Parse(output))
            {
                Assert.That(document.RootElement.GetProperty("success").GetBoolean(), Is.True);
                Assert.That(document.RootElement.GetProperty("result").GetProperty("public").GetBoolean(), Is.True);
                Assert.That(document.RootElement.GetProperty("output").GetString(), Is.EqualTo(outputPath));
            }
            var image = await File.ReadAllBytesAsync(outputPath);
            var module = NativeLibrary.Load(outputPath);
            try
            {
                var function = Marshal.GetDelegateForFunctionPointer<ScalarBinary>(
                    NativeLibrary.GetExport(module, "Value"));
                Assert.That(function(20, 22), Is.EqualTo(42));
            }
            finally
            {
                NativeLibrary.Free(module);
            }

            var (privateExit, privateOutput, privateError) = await RunAsync(
                Encoding.UTF8.GetBytes("int32 Value(int32 x, int32 y) { return x + y; }"),
                "frontend-build-library", directory, "-", "-o", outputPath, "--runtime", policy, "--json");
            Assert.That(privateExit, Is.EqualTo(1));
            Assert.That(privateError, Is.Empty);
            using (var document = JsonDocument.Parse(privateOutput))
            {
                Assert.That(document.RootElement.GetProperty("success").GetBoolean(), Is.False);
            }
            Assert.That(await File.ReadAllBytesAsync(outputPath), Is.EqualTo(image));

            var (internalExit, internalOutput, internalError) = await RunAsync(
                Encoding.UTF8.GetBytes("internal int32 Value(int32 x, int32 y) { return x + y; }"),
                "frontend-build-library", directory, "-", "-o", outputPath, "--runtime", policy, "--json");
            Assert.That(internalExit, Is.EqualTo(1));
            Assert.That(internalError, Is.Empty);
            using (var document = JsonDocument.Parse(internalOutput))
            {
                Assert.That(document.RootElement.GetProperty("success").GetBoolean(), Is.False);
                Assert.That(document.RootElement.GetProperty("diagnostics")[0]
                    .GetProperty("code").GetString(), Is.EqualTo("TRM4002"));
            }
            Assert.That(await File.ReadAllBytesAsync(outputPath), Is.EqualTo(image));

            var (lexExit, lexOutput, lexError) = await RunAsync(
                Encoding.UTF8.GetBytes("public int32 Value(int32 x) { return @; }"),
                "frontend-build-library", directory, "-", "-o", outputPath, "--runtime", policy, "--json");
            Assert.That(lexExit, Is.EqualTo(1));
            Assert.That(lexError, Is.Empty);
            using (var document = JsonDocument.Parse(lexOutput))
            {
                Assert.That(document.RootElement.GetProperty("success").GetBoolean(), Is.False);
                Assert.That(document.RootElement.GetProperty("diagnostics")[0]
                    .GetProperty("code").GetString(), Is.EqualTo("TRM0001"));
            }
            Assert.That(await File.ReadAllBytesAsync(outputPath), Is.EqualTo(image));
        }
        finally
        {
            File.Delete(outputPath);
        }
    }
    [TestCase("fail-fast")]
    [TestCase("checked-exceptions")]
    public async Task FrontendComponentPublishesTypedBranchesAndRejectsInvalidShapesAsync(string policy)
    {
        var directory = Path.Combine(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", "..")), "self-host");
        const string Conditional =
            "int32 Value(int32 x, int32 y) { if (x < y) { return x + y; } else { return x - y; } }";
        var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(Conditional),
            "frontend-component", directory, "-", "--runtime", policy, "--json");
        Assert.That(exit, Is.EqualTo(0));
        Assert.That(error, Is.Empty);
        using (var document = JsonDocument.Parse(output))
        {
            var result = document.RootElement;
            Assert.That(result.GetProperty("schema").GetInt32(), Is.EqualTo(1));
            Assert.That(result.GetProperty("command").GetString(), Is.EqualTo("frontend-component"));
            Assert.That(result.GetProperty("success").GetBoolean(), Is.True);
            Assert.That(result.GetProperty("result").GetProperty("parameterCount").GetInt32(), Is.EqualTo(2));
            Assert.That(result.GetProperty("result").TryGetProperty("hasFallthrough", out _), Is.False);
            Assert.That(result.GetProperty("result").GetProperty("condition")
                .GetProperty("operation").GetInt32(), Is.EqualTo(67));
            Assert.That(result.GetProperty("result").GetProperty("whenTrue")
                .GetProperty("operation").GetInt32(), Is.EqualTo(57));
            Assert.That(result.GetProperty("result").GetProperty("whenFalse")
                .GetProperty("operation").GetInt32(), Is.EqualTo(58));
            Assert.That(result.GetProperty("diagnostics").EnumerateArray(), Is.Empty);
        }

        var (invalidExit, invalidOutput, invalidError) = await RunAsync(
            Encoding.UTF8.GetBytes("int32 Value(int32 x) { return y; }"),
            "frontend-component", directory, "-", "--runtime", policy, "--json");
        Assert.That(invalidExit, Is.EqualTo(1));
        Assert.That(invalidError, Is.Empty);
        using (var document = JsonDocument.Parse(invalidOutput))
        {
            var result = document.RootElement;
            Assert.That(result.GetProperty("success").GetBoolean(), Is.False);
            Assert.That(result.GetProperty("result").GetProperty("errorCode").GetInt32(), Is.EqualTo(3));
            Assert.That(result.GetProperty("result").GetProperty("errorSpan")
                .GetProperty("start").GetInt32(), Is.EqualTo(30));
            Assert.That(result.GetProperty("diagnostics").EnumerateArray(), Is.Empty);
        }

        const string LocatedInvalid = "int32 Value()\r\n{\r\n return /* \U0001F600 */ missing;\r\n}";
        var (locatedExit, locatedOutput, locatedError) = await RunAsync(
            Encoding.UTF8.GetBytes(LocatedInvalid),
            "frontend-component", directory, "-", "--runtime", policy, "--json");
        Assert.That(locatedExit, Is.EqualTo(1));
        Assert.That(locatedError, Is.Empty);
        using (var document = JsonDocument.Parse(locatedOutput))
        {
            var result = document.RootElement;
            var range = result.GetProperty("result").GetProperty("errorRange");
            Assert.That(range.GetProperty("start").GetProperty("line").GetInt32(), Is.EqualTo(2));
            Assert.That(range.GetProperty("start").GetProperty("character").GetInt32(), Is.EqualTo(17));
            Assert.That(range.GetProperty("end").GetProperty("line").GetInt32(), Is.EqualTo(2));
            Assert.That(range.GetProperty("end").GetProperty("character").GetInt32(), Is.EqualTo(24));
        }

        var (consoleExit, consoleOutput, consoleError) = await RunAsync(
            Encoding.UTF8.GetBytes(LocatedInvalid),
            "frontend-component", directory, "-", "--runtime", policy);
        Assert.That(consoleExit, Is.EqualTo(1));
        Assert.That(consoleOutput, Is.Empty);
        Assert.That(consoleError, Does.Contain("<stdin>(3,18): Frontend 3 at [35..42)."));

        const string LocatedLexicalError = "int32 Value()\r\n{\r\n @\r\n}";
        var (lexExit, lexOutput, lexError) = await RunAsync(Encoding.UTF8.GetBytes(LocatedLexicalError),
            "frontend-component", directory, "-", "--runtime", policy, "--json");
        Assert.That(lexExit, Is.EqualTo(1));
        Assert.That(lexError, Is.Empty);
        using (var document = JsonDocument.Parse(lexOutput))
        {
            var result = document.RootElement;
            Assert.That(result.GetProperty("success").GetBoolean(), Is.False);
            Assert.That(result.TryGetProperty("result", out _), Is.False);
            var diagnostic = result.GetProperty("diagnostics")[0];
            Assert.That(diagnostic.GetProperty("code").GetString(), Is.EqualTo("TRM0001"));
            Assert.That(diagnostic.GetProperty("range").GetProperty("start")
                .GetProperty("line").GetInt32(), Is.EqualTo(2));
            Assert.That(diagnostic.GetProperty("range").GetProperty("start")
                .GetProperty("character").GetInt32(), Is.EqualTo(1));
            Assert.That(diagnostic.GetProperty("range").GetProperty("end")
                .GetProperty("line").GetInt32(), Is.EqualTo(2));
            Assert.That(diagnostic.GetProperty("range").GetProperty("end")
                .GetProperty("character").GetInt32(), Is.EqualTo(2));
        }
    }
    [TestCase("fail-fast")]
    [TestCase("checked-exceptions")]
    public async Task FrontendComponentPublishesTrailingReturnShapeAsync(string policy)
    {
        var directory = Path.Combine(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", "..")), "self-host");
        var (exit, output, error) = await RunAsync(
            Encoding.UTF8.GetBytes("int32 Value(bool x) { if (x) { return 42; } return 0; }"),
            "frontend-component", directory, "-", "--runtime", policy, "--json");
        Assert.That(exit, Is.EqualTo(0));
        Assert.That(error, Is.Empty);
        using var document = JsonDocument.Parse(output);
        var result = document.RootElement.GetProperty("result");
        Assert.That(result.GetProperty("hasCondition").GetBoolean(), Is.True);
        Assert.That(result.GetProperty("hasFallthrough").GetBoolean(), Is.True);
        Assert.That(result.GetProperty("condition").GetProperty("operation").GetInt32(), Is.EqualTo(0));
        Assert.That(result.GetProperty("whenTrue").GetProperty("operation").GetInt32(), Is.EqualTo(3));
        Assert.That(result.GetProperty("whenFalse").GetProperty("operation").GetInt32(), Is.EqualTo(3));
    }
    [TestCase("fail-fast")]
    [TestCase("checked-exceptions")]
    public async Task FrontendComponentReportsExplicitInternalVisibilityAsync(string policy)
    {
        var directory = Path.Combine(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", "..")), "self-host");
        var (exit, output, error) = await RunAsync(
            Encoding.UTF8.GetBytes("internal bool Value(bool x) { return x; }"),
            "frontend-component", directory, "-", "--runtime", policy, "--json");
        Assert.That(exit, Is.EqualTo(0));
        Assert.That(error, Is.Empty);
        using var document = JsonDocument.Parse(output);
        var result = document.RootElement.GetProperty("result");
        Assert.That(result.GetProperty("valid").GetBoolean(), Is.True);
        Assert.That(result.GetProperty("public").GetBoolean(), Is.False);
    }
    [TestCase("fail-fast")]
    [TestCase("checked-exceptions")]
    public async Task FrontendComponentRejectsConditionalWithoutFallbackAsync(string policy)
    {
        var directory = Path.Combine(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", "..")), "self-host");
        var (exit, output, error) = await RunAsync(
            Encoding.UTF8.GetBytes("int32 Value(bool x) { if (x) { return 42; } }"),
            "frontend-component", directory, "-", "--runtime", policy, "--json");
        Assert.That(exit, Is.EqualTo(1));
        Assert.That(error, Is.Empty);
        using var document = JsonDocument.Parse(output);
        var result = document.RootElement.GetProperty("result");
        Assert.That(result.GetProperty("valid").GetBoolean(), Is.False);
        Assert.That(result.GetProperty("errorCode").GetInt32(), Is.EqualTo(1));
    }

    [Test]
    public async Task FrontendComponentRejectsMissingSourcesWithoutPublishingAResultAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"terrum-missing-frontend-{Guid.NewGuid():N}");
        var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes("int32 Value(int32 x) { return x; }"),
            "frontend-component", directory, "-", "--runtime", "fail-fast", "--json");
        Assert.That(exit, Is.EqualTo(2));
        Assert.That(error, Is.Empty);
        using var document = JsonDocument.Parse(output);
        Assert.That(document.RootElement.GetProperty("success").GetBoolean(), Is.False);
    }

    [Test]
    public async Task FrontendComponentRejectsBooleanLiteralWithIncompatibleReturnTypeAsync()
    {
        var directory = Path.Combine(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", "..")), "self-host");
        var (exit, output, error) = await RunAsync(
            Encoding.UTF8.GetBytes("public int32 Value() { return true; }"),
            "frontend-component", directory, "-", "--runtime", "fail-fast", "--json");
        Assert.That(exit, Is.EqualTo(1));
        Assert.That(error, Is.Empty);
        using var document = JsonDocument.Parse(output);
        var result = document.RootElement.GetProperty("result");
        Assert.That(result.GetProperty("valid").GetBoolean(), Is.False);
        Assert.That(result.GetProperty("public").GetBoolean(), Is.False);
        Assert.That(result.GetProperty("errorCode").GetInt32(), Is.EqualTo(2));
        Assert.That(result.GetProperty("errorSpan").GetProperty("start").GetInt32(), Is.EqualTo(30));
    }
    [TestCase("int32 Main() { int32 value = 'A'; return value; }")]
    [TestCase("int32 Main() { var value = 'A'; return 0; }")]
    [TestCase("int32 Main() { char16 value = '\\U0001F600'; return int32(value); }")]
    [TestCase("int32 Main() { char16 value = '\U0001F600'; return int32(value); }")]
    public async Task FrontendComponentRejectsCharacterLiteralsWithoutChar16ContextAsync(
        string source)
    {
        var directory = Path.Combine(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", "..")), "self-host");
        var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(source),
            "frontend-component", directory, "-", "--runtime", "fail-fast", "--json");
        Assert.That(exit, Is.EqualTo(1));
        Assert.That(error, Is.Empty);
        using var document = JsonDocument.Parse(output);
        var result = document.RootElement.GetProperty("result");
        Assert.That(result.GetProperty("valid").GetBoolean(), Is.False);
        Assert.That(result.GetProperty("errorCode").GetInt32(), Is.EqualTo(2));
    }
    [TestCase("'é'",233)]
    [TestCase("'\\u00E9'",233)]
    [TestCase("'\\U000000E9'",233)]
    [TestCase("'\\''",39)]
    [TestCase("'\\\"'",34)]
    [TestCase("'\\\\'",92)]
    [TestCase("'\\0'",0)]
    public async Task FrontendExecutesRepresentableCharacterLiteralsAsync(string literal, int expected)
    {
        var source = $"int32 Main() {{ char16 value = {literal}; return int32(value); }}";
        var directory = Path.Combine(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", "..")), "self-host");
        var input = Encoding.UTF8.GetBytes(source);

        foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
        {
            var (frontendExit, frontendOutput, frontendError) = await RunAsync(input,
                "frontend-run", directory, "-", "--runtime", policy);
            Assert.That(frontendExit, Is.EqualTo(expected));
            Assert.That(frontendOutput, Is.Empty);
            Assert.That(frontendError, Is.Empty);

            var (hostedExit, hostedOutput, hostedError) = await RunAsync(input,
                "jit", "-", "--runtime", policy, "--json");
            Assert.That(hostedExit, Is.EqualTo(expected));
            Assert.That(hostedOutput, Is.Empty);
            Assert.That(hostedError, Is.Empty);
        }
    }
    [TestCase("true",233)]
    [TestCase("false",65)]
    public async Task FrontendContextuallyTypesCharacterLiteralsInConditionalCallsAsync(
        string condition, int expected)
    {
        var source = $$"""
            char16 Choose(bool condition) {
                return condition ? 'é' : '\u0041';
            }
            int32 Main() {
                return int32(Choose({{condition}}));
            }
            """;
        var directory = Path.Combine(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", "..")), "self-host");
        var input = Encoding.UTF8.GetBytes(source);

        foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
        {
            var (frontendExit, frontendOutput, frontendError) = await RunAsync(input,
                "frontend-run", directory, "-", "--runtime", policy);
            Assert.That(frontendExit, Is.EqualTo(expected));
            Assert.That(frontendOutput, Is.Empty);
            Assert.That(frontendError, Is.Empty);

            var (hostedExit, hostedOutput, hostedError) = await RunAsync(input,
                "jit", "-", "--runtime", policy, "--json");
            Assert.That(hostedExit, Is.EqualTo(expected));
            Assert.That(hostedOutput, Is.Empty);
            Assert.That(hostedError, Is.Empty);
        }
    }
    [TestCase("int32","2147483648")]
    [TestCase("int32","0x100000000")]
    [TestCase("uint32","4294967296")]
    [TestCase("int64","9223372036854775808")]
    [TestCase("uint64","18446744073709551616")]
    [TestCase("char16","65536")]
    [TestCase("bool","1")]
    [TestCase("int32","-2147483649")]
    [TestCase("int64","-9223372036854775809")]
    [TestCase("uint32","-1")]
    public async Task FrontendComponentRejectsOutOfRangeIntegerAsync(string type, string literal)
    {
        var directory = Path.Combine(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", "..")), "self-host");
        var source = $"public {type} Value() {{ return {literal}; }}";
        var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(source),
            "frontend-component", directory, "-", "--runtime", "fail-fast", "--json");
        Assert.That(exit, Is.EqualTo(1));
        Assert.That(error, Is.Empty);
        using var document = JsonDocument.Parse(output);
        var result = document.RootElement.GetProperty("result");
        Assert.That(result.GetProperty("valid").GetBoolean(), Is.False);
        Assert.That(result.GetProperty("errorCode").GetInt32(), Is.EqualTo(2));
        Assert.That(result.GetProperty("errorSpan").GetProperty("start").GetInt32(), Is.EqualTo(source.IndexOf(literal, StringComparison.Ordinal)));
    }
    [TestCase("int32 Value(int32 x) { return x + 2147483648; }","2147483648")]
    [TestCase("int32 Value(int32 x) { return 2147483648 + x; }","2147483648")]
    [TestCase("bool Value(int32 x) { return x < 2147483648; }","2147483648")]
    [TestCase("bool Value(int32 x) { return 2147483648 < x; }","2147483648")]
    [TestCase("int32 Value(int32 x) { return x * 2147483648; }","2147483648")]
    [TestCase("int32 Value(int32 x) { return 2147483648 / x; }","2147483648")]
    [TestCase("uint32 Value(uint32 x) { return x & 4294967296; }","4294967296")]
    [TestCase("uint32 Value(uint32 x) { return 4294967296 | x; }","4294967296")]
    [TestCase("int32 Value() { return ~2147483648; }","2147483648")]
    [TestCase("int32 Value() { return 1 + 2147483648; }","2147483648")]
    [TestCase("int32 Value() { return +2147483648; }","2147483648")]
    [TestCase("int32 Value(int32 x) { return x + -2147483649; }","2147483649")]
    [TestCase("bool Value(int64 x) { return -9223372036854775809 < x; }","9223372036854775809")]
    [TestCase("uint32 Value(uint32 x) { return x + -1; }","1")]
    [TestCase("int32 Value(int32 x) { return x + +2147483648; }","2147483648")]
    [TestCase("bool Value(int32 x) { return x == -(2147483648); }","2147483648")]
    [TestCase("int64 Value() { return -(9223372036854775808); }","9223372036854775808")]
    [TestCase("uint32 Value() { return +(4294967296); }","4294967296")]
    public async Task FrontendComponentRejectsOutOfRangeBinaryLiteralAsync(string source,
        string invalidLiteral)
    {
        var directory = Path.Combine(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", "..")), "self-host");
        var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(source),
            "frontend-component", directory, "-", "--runtime", "fail-fast", "--json");
        Assert.That(exit, Is.EqualTo(1));
        Assert.That(error, Is.Empty);
        using var document = JsonDocument.Parse(output);
        var result = document.RootElement.GetProperty("result");
        Assert.That(result.GetProperty("valid").GetBoolean(), Is.False);
        Assert.That(result.GetProperty("errorCode").GetInt32(), Is.EqualTo(2));
        Assert.That(result.GetProperty("errorSpan").GetProperty("start").GetInt32(), Is.EqualTo(source.IndexOf(invalidLiteral, StringComparison.Ordinal)));
    }
    [TestCase("char16 Value(char16 x) { return x & 42; }")]
    [TestCase("char16 Value(char16 x) { return 42 | x; }")]
    [TestCase("char16 Value(char16 x) { return x + 42; }")]
    [TestCase("char16 Value(char16 x) { return ~x; }")]
    [TestCase("char16 Value(char16 x, char16 y) { return x & y; }")]
    [TestCase("int32 Value(int32 x, int32 y) { return x && y; }")]
    [TestCase("bool Value(int32 x) { return x && true; }")]
    [TestCase("bool Value(int32 x) { return false | x; }")]
    [TestCase("bool Value(int32 x) { return x == true; }")]
    [TestCase("bool Value(int32 x) { return false != x; }")]
    [TestCase("uint32 Value(uint32 x) { return -x; }")]
    [TestCase("bool Value(bool x) { return +x; }")]
    [TestCase("char16 Value() { return +42; }")]
    [TestCase("uint32 Value() { return -(5); }")]
    [TestCase("uint64 Value() { return -(5); }")]
    public async Task FrontendComponentRejectsUnsupportedScalarLiteralOperatorAsync(string source)
    {
        var directory = Path.Combine(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", "..")), "self-host");
        var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(source),
            "frontend-component", directory, "-", "--runtime", "fail-fast", "--json");
        Assert.That(exit, Is.EqualTo(1));
        Assert.That(error, Is.Empty);
        using var document = JsonDocument.Parse(output);
        var result = document.RootElement.GetProperty("result");
        Assert.That(result.GetProperty("valid").GetBoolean(), Is.False);
        Assert.That(result.GetProperty("errorCode").GetInt32(), Is.EqualTo(2));
    }
    [TestCase("int32 Main() { return (42; }",";")]
    [TestCase("int32 Main() { return ((42); }",";")]
    [TestCase("int32 Main() { return (42)); }",")")]
    [TestCase("int32 Main() { return (20 + 22; }",";")]
    [TestCase("int32 Main() { return ((20 + 22); }",";")]
    [TestCase("int32 Main() { return (20 + 22)); }",")")]
    public async Task FrontendComponentRejectsUnbalancedExpressionGroupAsync(
        string source, string offending)
    {
        var directory = Path.Combine(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", "..")), "self-host");
        var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(source),
            "frontend-component", directory, "-", "--runtime", "fail-fast", "--json");
        Assert.That(exit, Is.EqualTo(1));
        Assert.That(error, Is.Empty);
        using var document = JsonDocument.Parse(output);
        var result = document.RootElement.GetProperty("result");
        Assert.That(result.GetProperty("valid").GetBoolean(), Is.False);
        Assert.That(result.GetProperty("errorCode").GetInt32(), Is.EqualTo(1));
        var errorSpan = result.GetProperty("errorSpan");
        Assert.That(errorSpan.GetProperty("start").GetInt32(), Is.EqualTo(source.LastIndexOf(offending, StringComparison.Ordinal)));
        Assert.That(errorSpan.GetProperty("length").GetInt32(), Is.EqualTo(offending.Length));
    }
    [TestCase("bool Value(int32 x) { return !x; }",2)]
    [TestCase("bool Value(bool x) { return !missing; }",3)]
    [TestCase("bool Value(bool x) { return !(missing); }",3)]
    public async Task FrontendComponentRejectsInvalidBooleanNegationAsync(
        string source, int expectedError)
    {
        var directory = Path.Combine(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", "..")), "self-host");
        var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(source),
            "frontend-component", directory, "-", "--runtime", "fail-fast", "--json");
        Assert.That(exit, Is.EqualTo(1));
        Assert.That(error, Is.Empty);
        using var document = JsonDocument.Parse(output);
        var result = document.RootElement.GetProperty("result");
        Assert.That(result.GetProperty("valid").GetBoolean(), Is.False);
        Assert.That(result.GetProperty("errorCode").GetInt32(), Is.EqualTo(expectedError));
    }
}
