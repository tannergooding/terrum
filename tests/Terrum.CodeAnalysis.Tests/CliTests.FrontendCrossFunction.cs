// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
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
    [Fact]
    public async Task ScalarFrontendCrossFunctionVoidStatementCliUsesProjection75Async()
    {
        const string text = "public int32 Main() { Step(); return 42; } void Step() { }";
        var directory = Path.Combine(RepositoryRoot(), "self-host");
        var input = Encoding.UTF8.GetBytes(text);
        var (exit, output, error) = await RunAsync(input, "frontend-component",
            directory, "-", "--runtime", "fail-fast", "--json");
        Assert.True(exit == 0, $"{exit}: {output}{error}");
        using (var document = JsonDocument.Parse(output))
        {
            var result = document.RootElement.GetProperty("result");
            Assert.Equal(75, result.GetProperty("projectionVersion").GetInt32());
            var main = result.GetProperty("functions")[0];
            Assert.Contains(main.GetProperty("nodes").EnumerateArray(),
                static node => node.GetProperty("operation").GetInt32() == 105
                    && node.GetProperty("type").GetInt32() == 0);
        }
        var (runExit, runOutput, runError) = await RunAsync(input, "frontend-run",
            directory, "-", "--runtime", "fail-fast", "--json");
        Assert.Equal(42, runExit);
        Assert.Empty(runOutput);
        Assert.Empty(runError);
    }

    [Fact]
    public async Task ScalarFrontendCrossFunctionDoesNotReturnCliUsesProjection76Async()
    {
        const string text = "[DoesNotReturn] void Halt() { require false; } public int32 Main() { if (false) { Halt(); } return 42; }";
        var directory = Path.Combine(RepositoryRoot(), "self-host");
        var input = Encoding.UTF8.GetBytes(text);
        foreach (var runtime in new[] { "fail-fast", "checked-exceptions" })
        {
            var (exit, output, error) = await RunAsync(input, "frontend-component",
                directory, "-", "--runtime", runtime, "--json");
            Assert.True(exit == 0, $"{exit}: {output}{error}");
            using (var document = JsonDocument.Parse(output))
            {
                var result = document.RootElement.GetProperty("result");
                Assert.Equal(76, result.GetProperty("projectionVersion").GetInt32());
                Assert.Equal(2, result.GetProperty("functions").GetArrayLength());
            }
            var (runExit, runOutput, runError) = await RunAsync(input, "frontend-run",
                directory, "-", "--runtime", runtime, "--json");
            Assert.Equal(42, runExit);
            Assert.Empty(runOutput);
            Assert.Empty(runError);
        }
    }

    [Fact]
    public async Task ScalarFrontendCrossFunctionOutputArgumentsCliUsesProjection77Async()
    {
        const string text = "public int32 Main() { if (Set(40, out var value, out var extra)) { return value + extra; } return 0; } bool Set(int32 seed, out int32 value, out int32 extra) { value = seed + 1; extra = 1; return true; }";
        var directory = Path.Combine(RepositoryRoot(), "self-host");
        var input = Encoding.UTF8.GetBytes(text);
        foreach (var runtime in new[] { "fail-fast", "checked-exceptions" })
        {
            var (exit, output, error) = await RunAsync(input, "frontend-component",
                directory, "-", "--runtime", runtime, "--json");
            Assert.True(exit == 0, $"{exit}: {output}{error}");
            using (var document = JsonDocument.Parse(output))
            {
                var result = document.RootElement.GetProperty("result");
                Assert.Equal(77, result.GetProperty("projectionVersion").GetInt32());
                var main = result.GetProperty("functions")[0];
                Assert.Equal(0, main.GetProperty("outParameterMask").GetInt32());
                Assert.Equal(6, result.GetProperty("functions")[1]
                    .GetProperty("outParameterMask").GetInt32());
                Assert.Contains(main.GetProperty("nodes").EnumerateArray(),
                    static node => node.GetProperty("operation").GetInt32() == 104);
            }
            var (runExit, runOutput, runError) = await RunAsync(input, "frontend-run",
                directory, "-", "--runtime", runtime, "--json");
            Assert.Equal(42, runExit);
            Assert.Empty(runOutput);
            Assert.Empty(runError);
        }
    }

    [Fact]
    public async Task ScalarFrontendCrossFunctionInitReferencesCliUsesProjection78Async()
    {
        const string text = "public int32 Main() { init int32 second; if (Fill(init var first)) { require Fill(init second); return first + second; } return 0; } bool Fill(init ref<int32> value) { value = 42; return true; }";
        var directory = Path.Combine(RepositoryRoot(), "self-host");
        var input = Encoding.UTF8.GetBytes(text);
        foreach (var runtime in new[] { "fail-fast", "checked-exceptions" })
        {
            var (exit, output, error) = await RunAsync(input, "frontend-component",
                directory, "-", "--runtime", runtime, "--json");
            Assert.True(exit == 0, $"{exit}: {output}{error}");
            using (var document = JsonDocument.Parse(output))
            {
                var result = document.RootElement.GetProperty("result");
                Assert.Equal(78, result.GetProperty("projectionVersion").GetInt32());
                Assert.Equal(0, result.GetProperty("functions")[0]
                    .GetProperty("initRefParameterMask").GetInt32());
                Assert.Equal(1, result.GetProperty("functions")[1]
                    .GetProperty("initRefParameterMask").GetInt32());
                Assert.Contains(result.GetProperty("functions")[0].GetProperty("nodes")
                    .EnumerateArray(), static node =>
                    node.GetProperty("operation").GetInt32() == 107);
                Assert.Contains(result.GetProperty("functions")[0].GetProperty("nodes")
                    .EnumerateArray(), static node =>
                    node.GetProperty("operation").GetInt32() == 108);
            }
            var (runExit, runOutput, runError) = await RunAsync(input, "frontend-run",
                directory, "-", "--runtime", runtime, "--json");
            Assert.Equal(84, runExit);
            Assert.Empty(runOutput);
            Assert.Empty(runError);
        }
    }

    [Fact]
    public async Task ScalarFrontendCrossFunctionCliSelectsEntryAndExportsInDeclarationOrderAsync()
    {
        const string text = "public int32 Next(int32 value) => value + 1; public int32 Main() => Next(41);";
        var directory = Path.Combine(RepositoryRoot(), "self-host");
        var outputDirectory = Path.Combine(RepositoryRoot(), "artifacts", "scanner-tests");
        Directory.CreateDirectory(outputDirectory);
        foreach (var runtime in new[] { "fail-fast", "checked-exceptions" })
        {
            var input = Encoding.UTF8.GetBytes(text);
            var (exit, output, error) = await RunAsync(input, "frontend-component",
                directory, "-", "--runtime", runtime, "--json");
            Assert.True(exit == 0, $"{exit}: {output}{error}");
            using (var document = JsonDocument.Parse(output))
            {
                var result = document.RootElement.GetProperty("result");
                Assert.Equal(74, result.GetProperty("projectionVersion").GetInt32());
                var functions = result.GetProperty("functions");
                Assert.Equal(2, functions.GetArrayLength());
                Assert.Equal(0, functions[0].GetProperty("index").GetInt32());
                Assert.Equal(1, functions[1].GetProperty("index").GetInt32());
                Assert.Equal(0, functions[1].GetProperty("nodes").EnumerateArray()
                    .Single(node => node.GetProperty("operation").GetInt32() == 105)
                    .GetProperty("function").GetInt32());
            }
            var (runExit, runOutput, runError) = await RunAsync(input, "frontend-run",
                directory, "-", "--runtime", runtime, "--json");
            Assert.Equal(42, runExit);
            Assert.Empty(runOutput);
            Assert.Empty(runError);

            var path = Path.Combine(outputDirectory, $"v74-library-{Guid.NewGuid():N}.dll");
            var executablePath = Path.ChangeExtension(path, ".exe");
            try
            {
                var (buildExit, buildOutput, buildError) = await RunAsync(input, "frontend-build-library",
                    directory, "-", "-o", path, "--runtime", runtime, "--json");
                Assert.True(buildExit == 0, $"{buildExit}: {buildOutput}{buildError}");
                var policy = runtime == "fail-fast"
                    ? BoundRuntimePolicy.FailFast : BoundRuntimePolicy.CheckedExceptions;
                var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(text), policy);
                Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
                var hosted = Assert.IsType<BoundProgram>(compilation.Program);
                Assert.Equal(NativeEmitter.EmitLibrary(hosted, ["Next", "Main"], (NativeRuntimePolicy)policy),
                    await File.ReadAllBytesAsync(path));
                var (executableExit, executableOutput, executableError) = await RunAsync(input, "frontend-build",
                    directory, "-", "-o", executablePath, "--runtime", runtime, "--json");
                Assert.True(executableExit == 0, $"{executableExit}: {executableOutput}{executableError}");
                Assert.Equal(NativeEmitter.Emit(hosted with { EntryPoint = 1 }, (NativeRuntimePolicy)policy),
                    await File.ReadAllBytesAsync(executablePath));
                using var process = Process.Start(new ProcessStartInfo(executablePath) {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                }) ?? throw new InvalidOperationException("Could not execute v74 frontend output.");
                await process.WaitForExitAsync();
                Assert.Equal(42, process.ExitCode);
            }
            finally
            {
                File.Delete(path);
                File.Delete(executablePath);
            }
        }
    }
}
