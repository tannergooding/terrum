// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Lexing;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Terrum.CodeAnalysis.Text;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed class JitTests
{
    [Theory]
    [InlineData("int32 Main() { return 42; }", 42)]
    [InlineData("void Main() { }", 0)]
    [InlineData("int32 Main() { mut int32 y = 9; bool result = true || Try(out y); return y; } bool Try(out int32 y) { return false; }", 9)]
    [InlineData("int32 Main() { require Fill(out var old); require Take(out var flag); if (flag == true) { return 0; } return 1; } bool Fill(out int32 value) { value = 2147483647; return true; } bool Take(out bool value) { value = true; return true; }", 0)]
    [InlineData("int32 Main() { require Fill(out var old); require Take(out var unit); return (int32)unit; } bool Fill(out int32 value) { value = 2147483647; return true; } bool Take(out char16 value) { value = (char16)42; return true; }", 42)]
    [InlineData("int32 Main() { return Factorial(6); } int32 Factorial(int32 n) { if (n <= 1) { return 1; } return n * Factorial(n - 1); }", 720)]
    [InlineData("int32 Main() { return Mix(1, 2, 3, 4, 5, 6); } int32 Mix(int32 a, int32 b, int32 c, int32 d, int32 e, int32 f) { return a + b * 2 + c * 3 + d * 4 + e * 5 + f * 6; }", 91)]
    [InlineData("int32 Main() { return 2147483647 + 1; }", unchecked((int)0xE0540001))]
    [InlineData("int32 Main() { return 1 / 0; }", unchecked((int)0xE0540002))]
    [InlineData("int32 Main() { require false; return 1; }", unchecked((int)0xE0540003))]
    [InlineData("int32 Main() { uint32 n = 4000000000u32; if (n > 2147483647u32 && n / 1000000000u32 == 4u32) { return (int32)(n - 3999999958u32); } return 0; }", 42)]
    [InlineData("int32 Main() { return (int32)uint32.MaxValue; }", unchecked((int)0xE0540001))]
    [InlineData("int32 Main() { return (int32)(uint32.MaxValue + 1u32); }", unchecked((int)0xE0540001))]
    [InlineData("int32 Main() { char16 high = (char16)55296u32; return (int32)high; }", 55296)]
    [InlineData("int32 Main() { return (int32)(char16)65536u32; }", unchecked((int)0xE0540001))]
    [InlineData("""
        struct Tag {
            public char16 Unit;
            public bool Valid;
            public Tag(char16 unit, bool valid) {
                this.Unit = unit;
                this.Valid = valid;
            }
        }
        Tag CreateTag() { return new Tag((char16)55296u32, true); }
        int32 ReadTag(Tag tag) { if (tag.Valid) { return (int32)tag.Unit; } return 0; }
        int32 Main() { return ReadTag(CreateTag()); }
        """, 55296)]
    [InlineData("int32 Main() { require Try(out var x) && x == 7; return x; } bool Try(out int32 x) { x = 7; return true; }", 7)]
    [InlineData("int32 Main() { require Try(out var x); return x; } bool Try(out int32 x) { x = 7; return false; }", unchecked((int)0xE0540003))]
    [InlineData("int32 Main() { if (Try(out var x) || false) { return x; } return 0; } bool Try(out int32 x) { x = 7; return true; }", 7)]
    [InlineData("int32 Main() { mut int32 sum = 0; for (mut int32 i = 0; i < 10; i = i + 1) { sum = sum + i; } return sum; }", 45)]
    [InlineData("int32 Main() { mut int32 i = 0; for (; i < 7;) { i = i + 1; } return i; }", 7)]
    [InlineData("int32 Main() { for (;;) { return 42; } return 0; }", 42)]
    [InlineData("int32 Main() { mut int32 i = 3; for (i = 9; false; i = i / 0) { require false; } return i; }", 9)]
    [InlineData("int32 Main() { for (mut int32 i = 0; i < 1; i = i / 0) { return 42; } return 0; }", 42)]
    [InlineData("int32 Main() { mut int32 sum = 0; for (mut int32 i = 1; i < 4; i = i + 1) { for (mut int32 j = 1; j < 4; j = j + 1) { sum = sum + i * j; } } return sum; }", 36)]
    [InlineData("int32 Main() { for (mut int32 i = 0; i < 3; i = i + 1) { } int32 i = 42; return i; }", 42)]
    [InlineData("int32 Main() { mut int32 sum = 0; for (mut int32 i = 0; Next(i, out var value); i = i + 1) { sum = sum + value; } return sum; } bool Next(int32 i, out int32 value) { if (i == 4) { return false; } value = i; return true; }", 6)]
    [InlineData("int32 Main() { for (mut int32 i = 0; i < 1; i = i / 0) { } return 0; }", unchecked((int)0xE0540002))]
    [InlineData("int32 Main() { for (mut int32 i = int32.MaxValue; i > 0; i = i + 1) { } return 0; }", unchecked((int)0xE0540001))]
    public async Task WorkerAndNativeExecutableAgreeAsync(string source, int expected)
    {
        AssertWindowsX64();
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = Assert.IsType<BoundProgram>(compilation.Program);
        var root = RepositoryRoot();
        var directory = Path.Combine(root, "artifacts", "jit-tests");
        _ = Directory.CreateDirectory(directory);
        var image = Path.Combine(directory, $"jit-{Guid.NewGuid():N}.exe");

        try
        {
            await File.WriteAllBytesAsync(image, NativeEmitter.Emit(program, NativeRuntimePolicy.FailFast)).ConfigureAwait(true);
            (var nativeExit, _, _) = await RunAsync(new ProcessStartInfo(image) { UseShellExecute = false, CreateNoWindow = true }, []).ConfigureAwait(true);
            (var jitExit, var jitOutput, var jitError) = await RunAsync(CliStart("jit", "-", "--runtime", "fail-fast"), Encoding.UTF8.GetBytes(source)).ConfigureAwait(true);
            Assert.Equal(expected, nativeExit);
            Assert.Equal(nativeExit, jitExit);
            Assert.Equal("", jitOutput);
            Assert.Equal("", jitError);
        }
        finally
        {
            File.Delete(image);
        }
    }

    [Theory]
    [InlineData("int32 Main() { return unknown; }", "TRM")]
    [InlineData("int32 Main() { mut int32 x = 9; if (Try(out x) && x == 5) { return 1; } return x; } bool Try(out int32 x) { x = 5; return false; }", "TRM3016")]
    [InlineData("int32 Main() { mut int32 y = 9; bool result = false || Try(out y); return y; } bool Try(out int32 y) { return false; }", "TRM")]
    [InlineData("int32 Main() { mut int32 y = 9; Try(out y); return y; } bool Try(out int32 y) { return false; }", "TRM")]
    [InlineData("int32 Main() { mut int32 y = 9; while (y > 0) { Try(out y); } return 0; } bool Try(out int32 y) { return false; }", "TRM")]
    [InlineData("int32 Main() { return 1", "TRM")]
    public async Task InvalidSourceDoesNotExecuteAsync(string source, string diagnosticPrefix)
    {
        (var exit, var output, _) = await RunAsync(CliStart("jit", "-", "--runtime", "fail-fast", "--json"), Encoding.UTF8.GetBytes(source)).ConfigureAwait(true);
        Assert.Equal(1, exit);
        using var document = JsonDocument.Parse(output);
        Assert.False(document.RootElement.GetProperty("success").GetBoolean());
        Assert.StartsWith(diagnosticPrefix, document.RootElement.GetProperty("diagnostics")[0].GetProperty("code").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task InvalidUtf8AndMissingRuntimePolicyAreDiagnosedAsync()
    {
        (var invalidExit, var invalidOutput, _) = await RunAsync(CliStart("jit", "-", "--runtime", "fail-fast", "--json"), [0xFF]).ConfigureAwait(true);
        Assert.Equal(2, invalidExit);
        using (var document = JsonDocument.Parse(invalidOutput))
        {
            Assert.Equal("TRM2001", document.RootElement.GetProperty("diagnostics")[0].GetProperty("code").GetString());
        }

        (var missingExit, var missingOutput, _) = await RunAsync(CliStart("jit", "-", "--json"), []).ConfigureAwait(true);
        Assert.Equal(2, missingExit);
        using var missingDocument = JsonDocument.Parse(missingOutput);
        Assert.Equal("TRM2000", missingDocument.RootElement.GetProperty("diagnostics")[0].GetProperty("code").GetString());
    }

    [Theory]
    [InlineData("alpha 42", 5, 6, 8)]
    [InlineData("\U00010400 42", 2, 3, 5)]
    public async Task ScannerWorkerReturnsCopiedWritableTokenDataAsync(string source, int identifierLength, int integerStart, int sourceLength)
    {
        AssertWindowsX64();
        var scanner = Path.Combine(RepositoryRoot(), "self-host", "Scanner.terrum");
        (var exit, var output, var error) = await RunAsync(
            CliStart("scan-component", scanner, "-", "--runtime", "fail-fast", "--json"),
            Encoding.UTF8.GetBytes(source)).ConfigureAwait(true);
        Assert.Equal(0, exit);
        Assert.Equal("", error);
        using var document = JsonDocument.Parse(output);
        Assert.Equal(4, document.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("scanner-4", document.RootElement.GetProperty("component").GetString());
        Assert.True(document.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal(0, document.RootElement.GetProperty("diagnostics").GetArrayLength());
        var tokens = document.RootElement.GetProperty("tokens");
        Assert.Equal(3, tokens.GetArrayLength());
        Assert.Equal("Identifier", tokens[0].GetProperty("kind").GetString());
        Assert.Equal(0, tokens[0].GetProperty("start").GetInt32());
        Assert.Equal(identifierLength, tokens[0].GetProperty("length").GetInt32());
        Assert.Equal("NumericLiteral", tokens[1].GetProperty("kind").GetString());
        Assert.Equal(integerStart, tokens[1].GetProperty("start").GetInt32());
        Assert.Equal(2, tokens[1].GetProperty("length").GetInt32());
        Assert.Equal("EndOfFile", tokens[2].GetProperty("kind").GetString());
        Assert.Equal(sourceLength, tokens[2].GetProperty("start").GetInt32());
    }

    [Theory]
    [InlineData("/* comment */ 1.25e+3f64 \"a\\U00010400\" // end")]
    [InlineData("1e+ \"\\q\"")]
    [InlineData("/* unterminated")]
    [InlineData("\"unterminated")]
    [InlineData("name \0 42")]
    [InlineData("public bool Scan() { /* first */ return true && false; }\r\n// eof")]
    [InlineData(" \t// only trivia\r\n/* end */")]
    public async Task ScannerWorkerReportsLexicalDiagnosticsAndTokenSpansAsync(string source)
    {
        var expected = Lexer.Lex(SourceText.From(source));
        var scanner = Path.Combine(RepositoryRoot(), "self-host", "Scanner.terrum");
        var (exit, output, error) = await RunAsync(
            CliStart("scan-component", scanner, "-", "--runtime", "fail-fast", "--json"),
            Encoding.UTF8.GetBytes(source)).ConfigureAwait(true);
        Assert.Equal(expected.Diagnostics.IsEmpty ? 0 : 1, exit);
        Assert.Empty(error);
        using var document = JsonDocument.Parse(output);
        Assert.Equal(4, document.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(expected.Diagnostics.IsEmpty, document.RootElement.GetProperty("success").GetBoolean());
        var diagnostics = document.RootElement.GetProperty("diagnostics");
        Assert.Equal(expected.Diagnostics.Length, diagnostics.GetArrayLength());
        for (var index = 0; index < expected.Diagnostics.Length; index++)
        {
            Assert.Equal(expected.Diagnostics[index].Code, diagnostics[index].GetProperty("code").GetString());
            Assert.Equal(expected.Diagnostics[index].Span.Start, diagnostics[index].GetProperty("start").GetInt32());
            Assert.Equal(expected.Diagnostics[index].Span.Length, diagnostics[index].GetProperty("length").GetInt32());
        }
        var tokens = document.RootElement.GetProperty("tokens");
        Assert.Equal(expected.Tokens.Length, tokens.GetArrayLength());
        for (var index = 0; index < expected.Tokens.Length; index++)
        {
            Assert.Equal(expected.Tokens[index].Kind.ToString(), tokens[index].GetProperty("kind").GetString());
            Assert.Equal(expected.Tokens[index].Span.Start, tokens[index].GetProperty("start").GetInt32());
            Assert.Equal(expected.Tokens[index].Span.Length, tokens[index].GetProperty("length").GetInt32());
            var leading = tokens[index].GetProperty("leadingTrivia");
            Assert.Equal(expected.Tokens[index].LeadingTrivia.Length, leading.GetArrayLength());
            for (var triviaIndex = 0; triviaIndex < expected.Tokens[index].LeadingTrivia.Length; triviaIndex++)
            {
                var trivia = expected.Tokens[index].LeadingTrivia[triviaIndex];
                Assert.Equal(trivia.Kind.ToString(), leading[triviaIndex].GetProperty("kind").GetString());
                Assert.Equal(trivia.Span.Start, leading[triviaIndex].GetProperty("start").GetInt32());
                Assert.Equal(trivia.Span.Length, leading[triviaIndex].GetProperty("length").GetInt32());
            }
        }
    }

    private static void AssertWindowsX64()
    {
        Assert.True(OperatingSystem.IsWindows() && RuntimeInformation.ProcessArchitecture == Architecture.X64,
            "JIT execution requires Windows x64.");
    }

    private static string RepositoryRoot() =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));

    private static ProcessStartInfo CliStart(params string[] args)
    {
        var framework = new DirectoryInfo(AppContext.BaseDirectory);
        var configuration = framework.Parent?.Name ?? throw new InvalidOperationException("Missing build configuration.");
        var cli = Path.Combine(RepositoryRoot(), "artifacts", "bin", "sources", "terrumc", configuration, framework.Name, "terrumc.dll");
        Assert.True(File.Exists(cli), $"CLI was not built: {cli}");
        var start = new ProcessStartInfo("dotnet") {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        start.ArgumentList.Add(cli);
        foreach (var arg in args)
        {
            start.ArgumentList.Add(arg);
        }
        return start;
    }

    private static async Task<(int ExitCode, string Output, string Error)> RunAsync(ProcessStartInfo start, byte[] input)
    {
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start test process.");
        var output = start.RedirectStandardOutput ? process.StandardOutput.ReadToEndAsync() : Task.FromResult("");
        var error = start.RedirectStandardError ? process.StandardError.ReadToEndAsync() : Task.FromResult("");
        if (start.RedirectStandardInput)
        {
            await process.StandardInput.BaseStream.WriteAsync(input).ConfigureAwait(true);
            process.StandardInput.Close();
        }
        try
        {
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(true);
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
        return (process.ExitCode, await output.ConfigureAwait(true), await error.ConfigureAwait(true));
    }
}
