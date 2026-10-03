// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.IO;
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
    [InlineData("public int32 Main() { init int32 value; if (value is init) { return 0; } value = 42; return value is init ? value : 0; }", 80, 42, 2)]
    [InlineData("public int32 Main() { init uint32 value; return value is init ? 42 : 0; }", 80, 0, 1)]
    [InlineData("public int32 Main() { init int64 value; return value is init ? 42 : 0; }", 80, 0, 1)]
    [InlineData("public int32 Main() { init uint64 value; return value is init ? 42 : 0; }", 80, 0, 1)]
    [InlineData("public int32 Main() { init bool value; return value is init ? 42 : 0; }", 80, 0, 1)]
    [InlineData("public int32 Main() { init char16 value; return value is init ? 42 : 0; }", 80, 0, 1)]
    [InlineData("""
        public int32 Main() => Ready(42) ? 42 : 0;
        bool Ready(int32 value)
        {
            init int32 local;
            if (local is init) { return false; }
            local = value;
            return local is init && value is init;
        }
        """, 81, 42, 3)]
    public async Task ScalarFrontendNamedInitializationTestsUseCurrentProjectionAsync(
        string text, int projectionVersion, int expected, int expectedInitializationTests)
    {
        var directory = Path.Combine(RepositoryRoot(), "self-host");
        var input = Encoding.UTF8.GetBytes(text);
        foreach (var runtime in new[] { "fail-fast", "checked-exceptions" })
        {
            var (componentExit, componentOutput, componentError) = await RunAsync(input,
                "frontend-component", directory, "-", "--runtime", runtime, "--json");
            Assert.True(componentExit == 0, $"{runtime}: {componentOutput}{componentError}");
            using (var document = JsonDocument.Parse(componentOutput))
            {
                var result = document.RootElement.GetProperty("result");
                Assert.Equal(projectionVersion, result.GetProperty("projectionVersion").GetInt32());
                var initializationTestCount = 0;
                if (result.TryGetProperty("functions", out var functions))
                {
                    foreach (var function in functions.EnumerateArray())
                    {
                        foreach (var node in function.GetProperty("nodes").EnumerateArray())
                        {
                            if (node.GetProperty("operation").GetInt32() == 109)
                            {
                                initializationTestCount++;
                            }
                        }
                    }
                }
                else
                {
                    foreach (var node in result.GetProperty("nodes").EnumerateArray())
                    {
                        if (node.GetProperty("operation").GetInt32() == 109)
                        {
                            initializationTestCount++;
                        }
                    }
                }
                Assert.Equal(expectedInitializationTests, initializationTestCount);
            }

            var (runExit, runOutput, runError) = await RunAsync(input, "frontend-run",
                directory, "-", "--runtime", runtime, "--json");
            Assert.Equal(expected, runExit);
            Assert.Empty(runOutput);
            Assert.Empty(runError);

            var policy = runtime == "fail-fast"
                ? BoundRuntimePolicy.FailFast : BoundRuntimePolicy.CheckedExceptions;
            var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(text), policy);
            Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
            var hosted = Assert.IsType<BoundProgram>(compilation.Program);
            var outputPath = Path.Combine(Path.GetTempPath(), $"terrum-frontend-init-{Guid.NewGuid():N}.dll");
            try
            {
                var (buildExit, buildOutput, buildError) = await RunAsync(input,
                    "frontend-build-library", directory, "-", "-o", outputPath,
                    "--runtime", runtime, "--json");
                Assert.True(buildExit == 0, $"{runtime}: {buildExit}: {buildOutput}{buildError}");
                Assert.Equal(NativeEmitter.EmitLibrary(hosted, ["Main"], (NativeRuntimePolicy)policy),
                    await File.ReadAllBytesAsync(outputPath));
            }
            finally
            {
                File.Delete(outputPath);
            }
        }
    }
}
