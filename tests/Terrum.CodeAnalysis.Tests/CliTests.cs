// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed partial class CliTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LibraryBuildPublishesSelectedExportsWithoutAnEntryPointAsync(bool multipleSources)
    {
        const string Entry = "public int32 Value() { return Helper(); }";
        const string Helper = "int32 Helper() { return 42; }";
        var prefix = Path.Combine(Path.GetTempPath(), $"terrum-library-cli-{Guid.NewGuid():N}");
        var outputPath = prefix + ".dll";
        var helperPath = prefix + ".terrum";
        try
        {
            List<string> arguments = ["build-library", "-", "--export", "Value", "-o", outputPath, "--runtime", "fail-fast"];
            if (multipleSources)
            {
                await File.WriteAllTextAsync(helperPath, Helper);
                arguments.AddRange(["--source", helperPath]);
            }
            arguments.Add("--json");
            var input = multipleSources ? Entry : Entry + Helper;
            var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(input), [.. arguments]);
            Assert.Equal(0, exit);
            Assert.Empty(error);
            using var document = JsonDocument.Parse(output);
            Assert.Equal(multipleSources ? 2 : 1, document.RootElement.GetProperty("schemaVersion").GetInt32());
            Assert.Equal("dynamic-library", document.RootElement.GetProperty("artifact").GetProperty("kind").GetString());
            var bytes = await File.ReadAllBytesAsync(outputPath);
            var compilation = multipleSources
                ? Compilation.CreateLibrary([SyntaxTree.Parse(Entry, "<stdin>"), SyntaxTree.Parse(Helper, helperPath)])
                : Compilation.CreateLibrary(SyntaxTree.Parse(input, "<stdin>"));
            Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
            Assert.Equal(NativeEmitter.EmitLibrary(Assert.IsType<BoundProgram>(compilation.Program),
                ["Value"], NativeRuntimePolicy.FailFast), bytes);
            using var stream = new MemoryStream(bytes);
            using var reader = new PEReader(stream);
            var header = Assert.IsType<PEHeader>(reader.PEHeaders.PEHeader);
            Assert.Equal(0, header.AddressOfEntryPoint);
            Assert.True(reader.PEHeaders.IsDll);
            Assert.NotEqual(0, header.ExportTableDirectory.Size);
        }
        finally
        {
            File.Delete(outputPath);
            File.Delete(helperPath);
        }
    }

    [Theory]
    [InlineData("Unknown")]
    [InlineData("Hidden")]
    [InlineData("\u03B1")]
    public async Task InvalidLibraryExportsPreservePriorArtifactsAsync(string export)
    {
        var path = Path.Combine(Path.GetTempPath(), $"terrum-library-export-{Guid.NewGuid():N}.dll");
        const string Source = "public int32 Value() { return 42; } int32 Hidden() { return 1; } public int32 \u03B1() { return 2; }";
        try
        {
            var (exit, _, _) = await RunAsync(Encoding.UTF8.GetBytes(Source), "build-library", "-", "--export", "Value",
                "-o", path, "--runtime", "fail-fast", "--json");
            Assert.Equal(0, exit);
            var original = await File.ReadAllBytesAsync(path);
            var (failed, output, error) = await RunAsync(Encoding.UTF8.GetBytes(Source), "build-library", "-", "--export", export,
                "-o", path, "--runtime", "fail-fast", "--json");
            Assert.Equal(1, failed);
            Assert.Empty(error);
            using var document = JsonDocument.Parse(output);
            Assert.Equal("TRM4002", document.RootElement.GetProperty("diagnostics")[0].GetProperty("code").GetString());
            Assert.False(document.RootElement.TryGetProperty("artifact", out _));
            Assert.Equal(original, await File.ReadAllBytesAsync(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData("analyze")]
    [InlineData("build")]
    [InlineData("jit")]
    public async Task SourceSetsResolveNamespacesAndValueMembersAcrossFilesAsync(string command)
    {
        const string Entry = "using Parts; int32 Main() { Box value = new Box(10); return value.Total(32); }";
        const string Library = "namespace Parts; public struct Box { public int32 Value; public Box(int32 value) { Value = value; } public int32 Total(int32 extra) { return Value + extra; } }";
        var prefix = Path.Combine(Path.GetTempPath(), $"terrum-source-set-{Guid.NewGuid():N}");
        var library = prefix + ".terrum";
        var image = prefix + ".exe";
        try
        {
            await File.WriteAllTextAsync(library, Library);
            List<string> arguments = [command, "-", "--source", library];
            if (command == "build")
            {
                arguments.AddRange(["-o", image]);
            }
            if (command != "analyze")
            {
                arguments.AddRange(["--runtime", "fail-fast"]);
            }
            arguments.Add("--json");
            var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(Entry), [.. arguments]);
            Assert.Empty(error);
            Assert.Equal(command == "jit" ? 42 : 0, exit);
            if (command == "jit")
            {
                Assert.Empty(output);
                return;
            }

            using var json = JsonDocument.Parse(output);
            Assert.Equal(2, json.RootElement.GetProperty("schemaVersion").GetInt32());
            Assert.True(json.RootElement.GetProperty("success").GetBoolean());
            Assert.Equal(command, json.RootElement.GetProperty("command").GetString());
            var sources = json.RootElement.GetProperty("sources");
            Assert.Equal(2, sources.GetArrayLength());
            Assert.Equal("<stdin>", sources[0].GetProperty("path").GetString());
            Assert.Equal(library, sources[1].GetProperty("path").GetString());
            Assert.Equal(Entry.Length, sources[0].GetProperty("sourceLength").GetInt32());
            Assert.Equal(Library.Length, sources[1].GetProperty("sourceLength").GetInt32());
            Assert.False(json.RootElement.TryGetProperty("contentId", out _));
            if (command == "build")
            {
                var bytes = await File.ReadAllBytesAsync(image);
                Assert.Equal(Convert.ToHexString(SHA256.HashData(bytes)),
                    json.RootElement.GetProperty("artifact").GetProperty("sha256").GetString());
                using var process = Process.Start(new ProcessStartInfo(image) { UseShellExecute = false, CreateNoWindow = true })
                    ?? throw new InvalidOperationException("Could not start source-set artifact.");
                try
                {
                    await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
                }
                catch (TimeoutException)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync();
                    throw;
                }
                Assert.Equal(42, process.ExitCode);
            }
        }
        finally
        {
            File.Delete(library);
            File.Delete(image);
        }
    }

    [Theory]
    [InlineData("analyze", true)]
    [InlineData("build", true)]
    [InlineData("jit", true)]
    [InlineData("analyze", false)]
    public async Task SourceSetErrorsKeepTheirOwningSnapshotAndPreserveOutputAsync(string command, bool json)
    {
        const string Invalid = "int32 Helper() { return missing; }";
        var prefix = Path.Combine(Path.GetTempPath(), $"terrum-source-errors-{Guid.NewGuid():N}");
        var helper = prefix + ".terrum";
        var image = prefix + ".exe";
        byte[] original = [1, 2, 3];
        try
        {
            await File.WriteAllTextAsync(helper, Invalid);
            await File.WriteAllBytesAsync(image, original);
            List<string> arguments = [command, "-", "--source", helper];
            if (command == "build")
            {
                arguments.AddRange(["-o", image]);
            }
            if (command != "analyze")
            {
                arguments.AddRange(["--runtime", "fail-fast"]);
            }
            if (json)
            {
                arguments.Add("--json");
            }
            var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes("int32 Main() { return Helper(); }"), [.. arguments]);
            Assert.Equal(1, exit);
            Assert.Equal(original, await File.ReadAllBytesAsync(image));
            if (json)
            {
                Assert.Empty(error);
                using var document = JsonDocument.Parse(output);
                Assert.Equal(2, document.RootElement.GetProperty("schemaVersion").GetInt32());
                Assert.False(document.RootElement.GetProperty("success").GetBoolean());
                Assert.Equal(helper, document.RootElement.GetProperty("sources")[1].GetProperty("path").GetString());
                var diagnostic = Assert.Single(document.RootElement.GetProperty("diagnostics").EnumerateArray());
                Assert.Equal(1, diagnostic.GetProperty("sourceIndex").GetInt32());
                Assert.Equal(Invalid.IndexOf("missing", StringComparison.Ordinal),
                    diagnostic.GetProperty("span").GetProperty("start").GetInt32());
                Assert.False(document.RootElement.TryGetProperty("artifact", out _));
            }
            else
            {
                Assert.Empty(output);
                Assert.Contains(helper + "(1,", error, StringComparison.Ordinal);
                Assert.Contains("missing", error, StringComparison.Ordinal);
            }
        }
        finally
        {
            File.Delete(helper);
            File.Delete(image);
        }
    }

    [Theory]
    [InlineData("build", ".exe")]
    [InlineData("build-library", ".dll")]
    public async Task SourceSetOptionsRejectDuplicatesAndProtectEveryInputAsync(string command, string extension)
    {
        var prefix = Path.Combine(Path.GetTempPath(), $"terrum-source-input-{Guid.NewGuid():N}");
        var entry = prefix + ".terrum";
        var helper = prefix + extension;
        const string Helper = "int32 Helper() { return 42; }";
        try
        {
            await File.WriteAllTextAsync(entry, "int32 Main() { return Helper(); }");
            await File.WriteAllTextAsync(helper, Helper);
            List<string> arguments = [command, entry, "--source", helper, "-o", helper, "--runtime", "fail-fast"];
            if (command == "build-library")
            {
                arguments.AddRange(["--export", "Helper"]);
            }
            arguments.Add("--json");
            var (exit, output, _) = await RunAsync([], [.. arguments]);
            Assert.Equal(2, exit);
            Assert.Equal(Helper, await File.ReadAllTextAsync(helper));
            using var document = JsonDocument.Parse(output);
            Assert.Equal("TRM2002", document.RootElement.GetProperty("diagnostics")[0].GetProperty("code").GetString());

            var alias = Path.Combine(Path.GetDirectoryName(entry) ?? throw new InvalidOperationException(), ".", Path.GetFileName(entry));
            var (duplicate, duplicateOutput, _) = await RunAsync([], "analyze", entry, "--source", alias, "--json");
            Assert.Equal(2, duplicate);
            using var duplicateDocument = JsonDocument.Parse(duplicateOutput);
            Assert.Equal("TRM2000", duplicateDocument.RootElement.GetProperty("diagnostics")[0].GetProperty("code").GetString());
        }
        finally
        {
            File.Delete(entry);
            File.Delete(helper);
        }
    }

    [Theory]
    [InlineData("analyze - --source - --json")]
    [InlineData("analyze - --source --json")]
    [InlineData("analyze - --json --json")]
    [InlineData("analyze - --runtime unknown --json")]
    [InlineData("jit - --source another.terrum --json")]
    [InlineData("jit - -o output.exe --runtime fail-fast --json")]
    [InlineData("build - --runtime fail-fast --json")]
    [InlineData("build - -o output.exe --runtime fail-fast --runtime fail-fast --json")]
    [InlineData("build-library - --export Value --runtime fail-fast --json")]
    [InlineData("build-library - -o output.dll --runtime fail-fast --json")]
    [InlineData("build-library - -o output.dll --export Value --json")]
    [InlineData("build-library - -o output.dll --export Value --export Value --runtime fail-fast --json")]
    [InlineData("build - -o output.exe --export Value --runtime fail-fast --json")]
    public async Task SourceSetCommandsRejectMalformedOptionsBeforeReadingInputsAsync(string arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        var (exit, output, error) = await RunAsync([], arguments.Split(' '));
        Assert.Equal(2, exit);
        Assert.Empty(error);
        using var document = JsonDocument.Parse(output);
        Assert.Equal("TRM2000", document.RootElement.GetProperty("diagnostics")[0].GetProperty("code").GetString());
    }

    [Fact]
    public async Task SourceSetsBoundInputCountBeforeReadingFilesAsync()
    {
        List<string> arguments = ["analyze", "-"];
        for (var index = 0; index < 256; index++)
        {
            arguments.AddRange(["--source", $"missing-{index}.terrum"]);
        }
        arguments.Add("--json");
        var (exit, output, _) = await RunAsync([], [.. arguments]);
        Assert.Equal(2, exit);
        using var document = JsonDocument.Parse(output);
        Assert.Equal("TRM2000", document.RootElement.GetProperty("diagnostics")[0].GetProperty("code").GetString());
    }

    [Fact]
    public async Task ScannerPublishesExactIntegerMagnitudesWithoutDefaultTypesOrPrecisionLossAsync()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
        const string Source = "0 9223372036854775808 18446744073709551615 18446744073709551616 256 1.0 2e3 3.0";
        var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(Source), "scan-component",
            Path.Combine(root, "self-host", "Scanner.terrum"), "-", "--runtime", "fail-fast", "--json");
        Assert.Equal(0, exit);
        Assert.Empty(error);
        using var document = JsonDocument.Parse(output);
        Assert.Equal(5, document.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("scanner-5", document.RootElement.GetProperty("component").GetString());
        var integers = document.RootElement.GetProperty("integerValues");
        Assert.Equal(5, integers.GetArrayLength());
        string[] tags = ["", "", "", "", ""];
        string?[] magnitudes = ["0", "9223372036854775808", "18446744073709551615", null, "256"];
        for (var index = 0; index < tags.Length; index++)
        {
            Assert.Equal(index, integers[index].GetProperty("tokenIndex").GetInt32());
            Assert.Equal(tags[index], integers[index].GetProperty("tag").GetString());
            Assert.Equal(magnitudes[index] is null ? "overflow" : "exact", integers[index].GetProperty("status").GetString());
            if (magnitudes[index] is string magnitude)
            {
                Assert.Equal(JsonValueKind.String, integers[index].GetProperty("magnitude").ValueKind);
                Assert.Equal(magnitude, integers[index].GetProperty("magnitude").GetString());
            }
            else
            {
                Assert.False(integers[index].TryGetProperty("magnitude", out _));
            }
        }
    }

    [Fact]
    public async Task ScannerWorkerPublishesCompiledTokenRecordsAsync()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
        var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes("\u03B12 + 42 \U00010400 \U0001F600"),
            "scan-component", Path.Combine(root, "self-host", "Scanner.terrum"), "-", "--runtime", "fail-fast", "--json");
        Assert.Equal(1, exit);
        Assert.Equal("", error);
        using var document = JsonDocument.Parse(output);
        Assert.Equal(5, document.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("scanner-5", document.RootElement.GetProperty("component").GetString());
        Assert.False(document.RootElement.GetProperty("success").GetBoolean());
        var diagnostic = Assert.Single(document.RootElement.GetProperty("diagnostics").EnumerateArray());
        Assert.Equal("TRM0001", diagnostic.GetProperty("code").GetString());
        Assert.Equal(11, diagnostic.GetProperty("start").GetInt32());
        Assert.Equal(2, diagnostic.GetProperty("length").GetInt32());
        var tokens = document.RootElement.GetProperty("tokens").EnumerateArray().ToArray();
        string[] kinds = ["Identifier", "Plus", "NumericLiteral", "Identifier", "Bad", "EndOfFile"];
        int[] starts = [0, 3, 5, 8, 11, 13];
        int[] lengths = [2, 1, 2, 2, 2, 0];
        Assert.Equal(kinds, tokens.Select(static token => token.GetProperty("kind").GetString()));
        Assert.Equal(starts, tokens.Select(static token => token.GetProperty("start").GetInt32()));
        Assert.Equal(lengths, tokens.Select(static token => token.GetProperty("length").GetInt32()));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BuildPublishesNativeArtifactAndFailedCompilationPreservesItAsync(bool nativeScanner)
    {
        var path = Path.Combine(Path.GetTempPath(), $"terrum-cli-{Guid.NewGuid():N}.exe");
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
        string[] arguments = nativeScanner
            ? ["build-component", Path.Combine(root, "self-host", "Scanner.terrum"), "-", "-o", path, "--runtime", "fail-fast", "--json"]
            : ["build", "-", "-o", path, "--runtime", "fail-fast", "--json"];

        try
        {
            var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes("int32 Main() { require true; return 23; }"),
                arguments);
            Assert.Equal(0, exit);
            Assert.Equal("", error);
            using var document = JsonDocument.Parse(output);
            var artifact = document.RootElement.GetProperty("artifact");
            Assert.Equal("windows-x64", artifact.GetProperty("target").GetString());
            Assert.Equal("fail-fast", artifact.GetProperty("runtimePolicy").GetString());
            var original = await File.ReadAllBytesAsync(path);
            Assert.Equal(original.Length, artifact.GetProperty("length").GetInt32());
            Assert.Equal(Convert.ToHexString(SHA256.HashData(original)), artifact.GetProperty("sha256").GetString());

            var (failure, failedOutput, _) = await RunAsync(Encoding.UTF8.GetBytes("int32 Main() { return unknown; }"),
                arguments);
            Assert.Equal(1, failure);
            using var failedDocument = JsonDocument.Parse(failedOutput);
            Assert.False(failedDocument.RootElement.GetProperty("success").GetBoolean());
            Assert.False(failedDocument.RootElement.TryGetProperty("artifact", out _));
            Assert.Equal(original, await File.ReadAllBytesAsync(path));

            var oversized = "int32 Main() {" + string.Concat(Enumerable.Range(0, 257)
                .Select(static index => FormattableString.Invariant($"int32 v{index} = 0;"))) + "return 0;}";
            var (limitExit, limitOutput, _) = await RunAsync(Encoding.UTF8.GetBytes(oversized),
                arguments);
            Assert.Equal(1, limitExit);
            using var limitDocument = JsonDocument.Parse(limitOutput);
            Assert.Equal("TRM3016", limitDocument.RootElement.GetProperty("diagnostics")[0].GetProperty("code").GetString());
            Assert.False(limitDocument.RootElement.TryGetProperty("artifact", out _));
            Assert.Equal(original, await File.ReadAllBytesAsync(path));

            using var process = Process.Start(new ProcessStartInfo(path) { UseShellExecute = false, CreateNoWindow = true })
                ?? throw new InvalidOperationException("Could not start native artifact.");

            try
            {
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            }
            catch (TimeoutException)
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync();
                }

                throw;
            }

            Assert.Equal(23, process.ExitCode);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData("/* first */ int32 Main() { int32 \u03B1 = 17; return \u03B1 + 25; } // end")]
    [InlineData("int32 F(int32 value) { if (value == 0) { return 1; } return value * F(value - 1); } int32 Main() { return F(5); }")]
    [InlineData("struct S { public int32 Value; public S(int32 value) { Value = value; } } int32 Main() { S value = new S(42); return value.Value; }")]
    [InlineData("int32 Main() { uint64 value = 18446744073709551615; return int32(value % 1000); }")]
    [InlineData("int32 Main() { int64 minimum = -9223372036854775808; return int32(minimum % -1); }")]
    [InlineData("int32 Main() { mut int32 sum = 0; for (mut int32 i = 0; i < 10; i = i + 1) { sum = sum + i; } return sum; }")]
    public async Task NativeScannerFeedsTheOfficialCompilerWithoutChangingEmissionAsync(string source)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
        var original = Path.Combine(Path.GetTempPath(), $"terrum-host-{Guid.NewGuid():N}.exe");
        var scanned = original + ".scanned.exe";
        try
        {
            var bytes = Encoding.UTF8.GetBytes(source);
            var (hostExit, hostOutput, hostError) = await RunAsync(bytes, "build", "-", "-o", original,
                "--runtime", "fail-fast", "--json");
            Assert.Equal(0, hostExit);
            Assert.Empty(hostError);
            var (nativeExit, nativeOutput, nativeError) = await RunAsync(bytes, "build-component",
                Path.Combine(root, "self-host", "Scanner.terrum"), "-", "-o", scanned, "--runtime", "fail-fast", "--json");
            Assert.Equal(0, nativeExit);
            Assert.Empty(nativeError);
            using var host = JsonDocument.Parse(hostOutput);
            using var native = JsonDocument.Parse(nativeOutput);
            Assert.Equal(host.RootElement.GetProperty("contentId").GetString(), native.RootElement.GetProperty("contentId").GetString());
            Assert.Equal("build-component", native.RootElement.GetProperty("command").GetString());
            Assert.Equal(await File.ReadAllBytesAsync(original), await File.ReadAllBytesAsync(scanned));
        }
        finally
        {
            File.Delete(original);
            File.Delete(scanned);
        }
    }

    [Theory]
    [InlineData("int32 Main() { return 0; } /*", "TRM0003")]
    [InlineData("int32 Main() { return 0; }\0", "TRM0001")]
    [InlineData("int32 Main() { return 1e+; }", "TRM0004")]
    [InlineData("int32 Main() { return 0 }", "TRM1001")]
    public async Task NativeScannerBuildPreservesLexicalAndParserDiagnosticsAsync(string source, string code)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
        var artifact = Path.Combine(Path.GetTempPath(), $"terrum-scanner-failure-{Guid.NewGuid():N}.exe");
        byte[] previous = [1, 2, 3, 4];
        try
        {
            await File.WriteAllBytesAsync(artifact, previous);
            var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(source), "build-component",
                Path.Combine(root, "self-host", "Scanner.terrum"), "-", "-o", artifact, "--runtime", "fail-fast", "--json");
            Assert.Equal(1, exit);
            Assert.Empty(error);
            using var document = JsonDocument.Parse(output);
            Assert.False(document.RootElement.GetProperty("success").GetBoolean());
            Assert.Contains(document.RootElement.GetProperty("diagnostics").EnumerateArray(),
                diagnostic => diagnostic.GetProperty("code").GetString() == code);
            Assert.Equal(previous, await File.ReadAllBytesAsync(artifact));
        }
        finally
        {
            File.Delete(artifact);
        }
    }

    [Theory]
    [InlineData("int32 Scan() { return 0; }", 1)]
    [InlineData("int32 Scan() { require false; return 0; }", unchecked((int)0xE0540003))]
    public async Task ScannerFailureNeverFallsBackToTheHostLexerAsync(string scanner, int expected)
    {
        var prefix = Path.Combine(Path.GetTempPath(), $"terrum-scanner-worker-{Guid.NewGuid():N}");
        var component = prefix + ".terrum";
        var artifact = prefix + ".exe";
        byte[] previous = [1, 2, 3, 4];
        try
        {
            await File.WriteAllTextAsync(component, scanner);
            await File.WriteAllBytesAsync(artifact, previous);
            var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes("int32 Main() { return 42; }"),
                "build-component", component, "-", "-o", artifact, "--runtime", "fail-fast", "--json");
            Assert.Equal(expected, exit);
            Assert.Empty(error);
            if (exit == 1)
            {
                using var document = JsonDocument.Parse(output);
                Assert.Equal("TRM5001", document.RootElement.GetProperty("diagnostics")[0].GetProperty("code").GetString());
            }
            Assert.Equal(previous, await File.ReadAllBytesAsync(artifact));
        }
        finally
        {
            File.Delete(component);
            File.Delete(artifact);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ComponentBuildCannotOverwriteEitherSourceAsync(bool scannerIsDestination)
    {
        var prefix = Path.Combine(Path.GetTempPath(), $"terrum-component-input-{Guid.NewGuid():N}");
        var scanner = prefix + "-scanner.exe";
        var input = prefix + "-source.exe";
        const string Scanner = "int32 Scan() { require false; return 0; }";
        const string Source = "int32 Main() { return 42; }";
        try
        {
            await File.WriteAllTextAsync(scanner, Scanner);
            await File.WriteAllTextAsync(input, Source);
            var (exit, output, error) = await RunAsync([], "build-component", scanner, input,
                "-o", scannerIsDestination ? scanner : input, "--runtime", "fail-fast", "--json");
            Assert.Equal(2, exit);
            Assert.Empty(error);
            using var document = JsonDocument.Parse(output);
            Assert.Equal("TRM2002", document.RootElement.GetProperty("diagnostics")[0].GetProperty("code").GetString());
            Assert.Equal(Scanner, await File.ReadAllTextAsync(scanner));
            Assert.Equal(Source, await File.ReadAllTextAsync(input));
        }
        finally
        {
            File.Delete(scanner);
            File.Delete(input);
        }
    }

    [Theory]
    [InlineData("build", "-", "-o", "unused.exe", "--json")]
    [InlineData("build", "-", "-o", "unused.exe", "--runtime", "implicit", "--json")]
    [InlineData("build-component", "scanner.terrum", "-", "-o", "unused.exe", "--json")]
    [InlineData("build-component", "scanner.terrum", "-", "-o", "unused.exe", "--runtime", "implicit", "--json")]
    [InlineData("build-component", "-", "-", "-o", "unused.exe", "--runtime", "fail-fast", "--json")]
    [SuppressMessage("Design", "CA1062:Validate arguments of public methods", Justification = "The xUnit data rows supply non-null argument arrays.")]
    public async Task BuildRequiresExplicitRuntimePolicyAsync(params string[] arguments)
    {
        var (exit, output, _) = await RunAsync([], arguments);
        Assert.Equal(2, exit);
        using var document = JsonDocument.Parse(output);
        Assert.Equal("TRM2000", document.RootElement.GetProperty("diagnostics")[0].GetProperty("code").GetString());
    }

    [Fact]
    public async Task AnalyzeRunsBindingRatherThanOnlyParsingAsync()
    {
        var source = Encoding.UTF8.GetBytes("int32 Main() { var x = 1; return x; }");
        var (syntaxExit, _, _) = await RunAsync(source, "check", "-", "--json");
        Assert.Equal(0, syntaxExit);
        var (semanticExit, output, _) = await RunAsync(source, "analyze", "-", "--json");
        Assert.Equal(1, semanticExit);
        using var document = JsonDocument.Parse(output);
        Assert.Equal("native-10", document.RootElement.GetProperty("profile").GetString());
        Assert.False(document.RootElement.GetProperty("success").GetBoolean());
    }

    [Theory]
    [InlineData("check")]
    [InlineData("analyze")]
    public async Task CliAcceptsPostTestedAndNamedLoopsAsync(string command)
    {
        const string source = """
            int32 Main() {
                mut int32 value = 0;
                outer: do {
                    do { value = value + 42; break outer; } while (true);
                } while (false);
                return value;
            }
            """;
        var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(source), command, "-", "--json");
        Assert.Equal(0, exit);
        Assert.Empty(error);
        using var document = JsonDocument.Parse(output);
        Assert.True(document.RootElement.GetProperty("success").GetBoolean());
        if (command == "check")
        {
            Assert.Equal(SyntaxTree.LanguageVersion, document.RootElement.GetProperty("languageVersion").GetString());
        }
    }

    [Theory]
    [InlineData("check")]
    [InlineData("tree")]
    public async Task RepeatedOptionalReportsPreciseStructuredDiagnosticAsync(string command)
    {
        const string Source = "void F(T? /* gap */\n? value) {}";
        var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(Source), command, "-", "--json");
        Assert.Equal(1, exit);
        Assert.Equal("", error);
        using var document = JsonDocument.Parse(output);
        var root = document.RootElement;
        Assert.False(root.GetProperty("success").GetBoolean());
        Assert.Equal(SyntaxTree.LanguageVersion, root.GetProperty("languageVersion").GetString());
        var diagnostic = Assert.Single(root.GetProperty("diagnostics").EnumerateArray());
        Assert.Equal("TRM1005", diagnostic.GetProperty("code").GetString());
        Assert.Equal(Source.LastIndexOf('?', StringComparison.Ordinal), diagnostic.GetProperty("span").GetProperty("start").GetInt32());
        Assert.Equal(1, diagnostic.GetProperty("span").GetProperty("length").GetInt32());

        if (command == "tree")
        {
            var reconstructed = string.Concat(root.GetProperty("tokens").EnumerateArray().Select(token =>
                string.Concat(token.GetProperty("leadingTrivia").EnumerateArray().Select(trivia => trivia.GetProperty("text").GetString())) + token.GetProperty("text").GetString()));
            Assert.Equal(Source, reconstructed);
        }
    }

    [Theory]
    [InlineData("name")]
    [InlineData("\"quote\\\" and \\\\ slash\"")]
    [InlineData("\"<tag> \u03B1 \U0001F600\"")]
    public async Task HumanTokensPreserveJsonEscapingAsync(string source)
    {
        var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(source), "tokens", "-");
        Assert.Equal(0, exit);
        Assert.Equal("", error);
        Assert.Contains(JsonSerializer.Serialize(source), output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("check", "void Main() { return; }", 0)]
    [InlineData("tree", "void Main() { return 1 }", 1)]
    [InlineData("tokens", " \r\n\u03B1 + 12 // end", 0)]
    [InlineData("tokens", "1 \u0661", 1)]
    [SuppressMessage("Design", "CA1062:Validate arguments of public methods", Justification = "The xUnit data rows supply non-null source strings.")]
    public async Task EmitsStructuredResultsWithExpectedExitAsync(string command, string source, int exitCode)
    {
        (var exit, var output, var error) = await RunAsync(Encoding.UTF8.GetBytes(source), command, "-", "--json");
        Assert.Equal(exitCode, exit);
        Assert.Equal("", error);
        using var document = JsonDocument.Parse(output);
        var root = document.RootElement;
        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("utf-16", root.GetProperty("coordinateSystem").GetString());
        Assert.Equal(exitCode == 0, root.GetProperty("success").GetBoolean());
        Assert.Equal(source.Length, root.GetProperty("sourceLength").GetInt32());

        if (command != "check")
        {
            var reconstructed = string.Concat(root.GetProperty("tokens").EnumerateArray().Select(token =>
                string.Concat(token.GetProperty("leadingTrivia").EnumerateArray().Select(trivia => trivia.GetProperty("text").GetString())) + token.GetProperty("text").GetString()));
            Assert.Equal(source, reconstructed);
        }

        if (command == "tree")
        {
            Assert.Equal("CompilationUnit", root.GetProperty("nodes")[0].GetProperty("kind").GetString());
            Assert.Equal(64, Assert.IsType<string>(root.GetProperty("contentId").GetString()).Length);
        }
    }

    [Theory]
    [InlineData("void Main() {\nreturn 1\n}", "<stdin>(3,1): error TRM1001")]
    [InlineData("void F(T? /* gap */\n? value) {}", "<stdin>(2,1): error TRM1005")]
    public async Task HumanErrorsUseOneBasedLocationsAndStderrAsync(string source, string diagnostic)
    {
        (var exit, var output, var error) = await RunAsync(Encoding.UTF8.GetBytes(source), "check", "-");
        Assert.Equal(1, exit);
        Assert.Equal("", output);
        Assert.Contains(diagnostic, error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InputFailuresRemainStructuredAndDoNotClaimToParseAsync()
    {
        foreach (var arguments in new[] { new[] { "bad", "-", "--json" }, ["check", Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".terrum"), "--json"] })
        {
            (var exit, var output, var error) = await RunAsync([], arguments);
            Assert.Equal(2, exit);
            Assert.Equal("", error);
            using var document = JsonDocument.Parse(output);
            Assert.False(document.RootElement.GetProperty("success").GetBoolean());
            Assert.StartsWith("TRM200", document.RootElement.GetProperty("diagnostics")[0].GetProperty("code").GetString(), StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task StrictUtf8RejectsInvalidBytesAndAcceptsAnOptionalBomAsync()
    {
        (var exit, var output, _) = await RunAsync([0xFF, 0xFE, 0x00, 0xD8], "check", "-", "--json");
        Assert.Equal(2, exit);
        using var document = JsonDocument.Parse(output);
        Assert.Equal("TRM2001", document.RootElement.GetProperty("diagnostics")[0].GetProperty("code").GetString());
        (var validExit, _, _) = await RunAsync([0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes("void Main() {}")], "check", "-");
        Assert.Equal(0, validExit);
    }

    [Fact]
    public async Task FlatTreeProtocolPreservesReferencesAndDeepTreesAsync()
    {
        var source = "void F() { return " + string.Join("+", Enumerable.Repeat("1", 500)) + "; }";
        (var exit, var output, _) = await RunAsync(Encoding.UTF8.GetBytes(source), "tree", "-", "--json");
        Assert.Equal(0, exit);
        using var document = JsonDocument.Parse(output);
        var nodes = document.RootElement.GetProperty("nodes");
        var tokens = document.RootElement.GetProperty("tokens");
        var nodeReferences = new int[nodes.GetArrayLength()];
        var tokenReferences = new int[tokens.GetArrayLength()];

        foreach (var node in nodes.EnumerateArray())
        {
            foreach (var child in node.GetProperty("children").EnumerateArray())
            {
                if (child.TryGetProperty("node", out var nodeId))
                {
                    nodeReferences[nodeId.GetInt32()]++;
                }
                else
                {
                    tokenReferences[child.GetProperty("token").GetInt32()]++;
                }
            }
        }

        Assert.Equal(0, nodeReferences[0]);
        Assert.All(nodeReferences.Skip(1), count => Assert.Equal(1, count));
        Assert.All(tokenReferences, count => Assert.Equal(1, count));
        (var secondExit, var secondOutput, _) = await RunAsync(Encoding.UTF8.GetBytes(source), "tree", "-", "--json");
        Assert.Equal(exit, secondExit);
        Assert.Equal(output, secondOutput);
    }

    private static Task<(int ExitCode, string Output, string Error)> RunAsync(byte[] input, params string[] arguments) =>
        RunAsync(input, arguments, workingDirectory: null);

    private static async Task<(int ExitCode, string Output, string Error)> RunAsync(byte[] input, string[] arguments,
        string? workingDirectory)
    {
        var frameworkDirectory = new DirectoryInfo(AppContext.BaseDirectory);
        var configuration = frameworkDirectory.Parent?.Name ?? throw new InvalidOperationException("Missing configuration directory.");
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
        var cli = Path.Combine(root, "artifacts", "bin", "sources", "terrumc", configuration, frameworkDirectory.Name, "terrumc.dll");
        Assert.True(File.Exists(cli), $"CLI was not built: {cli}");
        var start = new ProcessStartInfo("dotnet") {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        if (workingDirectory is not null)
        {
            start.WorkingDirectory = workingDirectory;
        }
        start.ArgumentList.Add(cli);

        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start CLI.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.StandardInput.BaseStream.WriteAsync(input).ConfigureAwait(true);
        process.StandardInput.Close();

        try
        {
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20)).ConfigureAwait(true);
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
