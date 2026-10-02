// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Terrum.CodeAnalysis.Text;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed partial class CliTests
{
    [Fact]
    public async Task ScalarFrontendProjectsOutParametersThroughHostedVerificationAsync()
    {
        const string accepted = "public bool Try(out int32 value) { value = 41; value++; return true; }";
        const string unassigned = "public bool Try(out int32 value) { return true; }";
        var frontend = Path.Combine(RepositoryRoot(), "self-host");
        foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
        {
            var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(accepted),
                "frontend-component", frontend, "-", "--runtime", policy, "--json");
            Assert.True(exit == 0, $"{policy}: {exit}: {output}{error}");
            using (var document = JsonDocument.Parse(output))
            {
                Assert.Equal(69, document.RootElement.GetProperty("result")
                    .GetProperty("projectionVersion").GetInt32());
            }

            var (invalidExit, invalidOutput, invalidError) = await RunAsync(Encoding.UTF8.GetBytes(unassigned),
                "frontend-component", frontend, "-", "--runtime", policy, "--json");
            Assert.True(invalidExit == 1, $"{policy}: {invalidExit}: {invalidOutput}{invalidError}");
        }
    }

    [Fact]
    public async Task ScalarFrontendProjectsRecursiveOutputCallsAsync()
    {
        const string recursive = "public bool Try(int32 value, out int32 result) { if (value == 0) { result = 1; return true; } mut int32 previous = 0; if (Try(value - 1, out previous)) { result = previous * value; return true; } return false; }";
        const string readOnlyOutput = "public bool Try(int32 value, out int32 result) { return Try(value, out value); }";
        var frontend = Path.Combine(RepositoryRoot(), "self-host");
        var source = SourceText.From(recursive);
        foreach (var runtimeName in new[] { "fail-fast", "checked-exceptions" })
        {
            var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(recursive),
                "frontend-component", frontend, "-", "--runtime", runtimeName, "--json");
            Assert.True(exit == 0, $"{runtimeName}: {exit}: {output}{error}");
            using var document = JsonDocument.Parse(output);
            var result = document.RootElement.GetProperty("result");
            Assert.Equal(70, result.GetProperty("projectionVersion").GetInt32());
            Assert.Contains(result.GetProperty("nodes").EnumerateArray(),
                node => node.GetProperty("operation").GetInt32() == 102);
            Assert.Contains(result.GetProperty("nodes").EnumerateArray(),
                node => node.GetProperty("operation").GetInt32() == 103);

            var (invalidExit, invalidOutput, invalidError) = await RunAsync(
                Encoding.UTF8.GetBytes(readOnlyOutput), "frontend-component", frontend, "-",
                "--runtime", runtimeName, "--json");
            Assert.True(invalidExit == 1, $"{runtimeName}: {invalidExit}: {invalidOutput}{invalidError}");

            var policy = runtimeName == "fail-fast"
                ? BoundRuntimePolicy.FailFast : BoundRuntimePolicy.CheckedExceptions;
            var hostedCompilation = Compilation.CreateLibrary(SyntaxTree.Parse(source), policy);
            Assert.False(hostedCompilation.HasErrors,
                string.Join(Environment.NewLine, hostedCompilation.Diagnostics));
            var hosted = Assert.IsType<BoundProgram>(hostedCompilation.Program);
            var outputPath = Path.Combine(Path.GetTempPath(), $"terrum-frontend-out-{Guid.NewGuid():N}.dll");
            try
            {
                var (buildExit, buildOutput, buildError) = await RunAsync(Encoding.UTF8.GetBytes(recursive),
                    "frontend-build-library", frontend, "-", "-o", outputPath, "--runtime", runtimeName, "--json");
                Assert.True(buildExit == 0, $"{runtimeName}: {buildExit}: {buildOutput}{buildError}");
                Assert.Equal(NativeEmitter.EmitLibrary(hosted, ["Try"], (NativeRuntimePolicy)policy),
                    await File.ReadAllBytesAsync(outputPath));
                var handle = NativeLibrary.Load(outputPath);
                try
                {
                    var invoke = Marshal.GetDelegateForFunctionPointer<RecursiveTry>(
                        NativeLibrary.GetExport(handle, "Try"));
                    Assert.True(invoke(5, out var factorial));
                    Assert.Equal(120, factorial);
                }
                finally
                {
                    NativeLibrary.Free(handle);
                }
            }
            finally
            {
                File.Delete(outputPath);
            }
        }
    }

    [Fact]
    public async Task ScalarFrontendProjectsRecursiveOutVariableCallsAsync()
    {
        const string recursive = "public bool Try(int32 value, out int32 result) { if (value == 0) { result = 1; return true; } if (Try(value - 1, out var previous)) { result = previous * value; return true; } return false; }";
        const string readsUnassigned = "public bool Try(int32 value, out int32 result) { if (Try(value - 1, out var previous)) { result = previous * value; return true; } result = previous; return false; }";
        var frontend = Path.Combine(RepositoryRoot(), "self-host");
        foreach (var runtimeName in new[] { "fail-fast", "checked-exceptions" })
        {
            var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(recursive),
                "frontend-component", frontend, "-", "--runtime", runtimeName, "--json");
            Assert.True(exit == 0, $"{runtimeName}: {exit}: {output}{error}");
            using (var document = JsonDocument.Parse(output))
            {
                var result = document.RootElement.GetProperty("result");
                Assert.Equal(71, result.GetProperty("projectionVersion").GetInt32());
                Assert.Contains(result.GetProperty("nodes").EnumerateArray(),
                    node => node.GetProperty("operation").GetInt32() == 104);
            }

            var invalid = await RunAsync(Encoding.UTF8.GetBytes(readsUnassigned),
                "frontend-component", frontend, "-", "--runtime", runtimeName, "--json");
            Assert.True(invalid.ExitCode == 1, $"{runtimeName}: {invalid.ExitCode}: {invalid.Output}{invalid.Error}");
            Assert.Empty(invalid.Error);
            using (var invalidDocument = JsonDocument.Parse(invalid.Output))
            {
                Assert.Equal("TRM5001", invalidDocument.RootElement
                    .GetProperty("diagnostics")[0].GetProperty("code").GetString());
            }

            var policy = runtimeName == "fail-fast"
                ? BoundRuntimePolicy.FailFast : BoundRuntimePolicy.CheckedExceptions;
            var hostedCompilation = Compilation.CreateLibrary(SyntaxTree.Parse(SourceText.From(recursive)), policy);
            Assert.False(hostedCompilation.HasErrors,
                string.Join(Environment.NewLine, hostedCompilation.Diagnostics));
            var hosted = Assert.IsType<BoundProgram>(hostedCompilation.Program);
            var outputPath = Path.Combine(Path.GetTempPath(), $"terrum-frontend-outvar-{Guid.NewGuid():N}.dll");
            try
            {
                var (buildExit, buildOutput, buildError) = await RunAsync(Encoding.UTF8.GetBytes(recursive),
                    "frontend-build-library", frontend, "-", "-o", outputPath, "--runtime", runtimeName, "--json");
                Assert.True(buildExit == 0, $"{runtimeName}: {buildExit}: {buildOutput}{buildError}");
                Assert.Equal(NativeEmitter.EmitLibrary(hosted, ["Try"], (NativeRuntimePolicy)policy),
                    await File.ReadAllBytesAsync(outputPath));
                var handle = NativeLibrary.Load(outputPath);
                try
                {
                    var invoke = Marshal.GetDelegateForFunctionPointer<RecursiveTry>(
                        NativeLibrary.GetExport(handle, "Try"));
                    Assert.True(invoke(5, out var factorial));
                    Assert.Equal(120, factorial);
                }
                finally
                {
                    NativeLibrary.Free(handle);
                }
            }
            finally
            {
                File.Delete(outputPath);
            }
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate bool RecursiveTry(int value, out int result);
}
