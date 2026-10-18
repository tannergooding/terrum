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
using NUnit.Framework;

using Terrum.CodeAnalysis.Tests.Infrastructure;

namespace Terrum.CodeAnalysis.Tests.Cli;

public sealed partial class CliTests
{
    [Test]
    public async Task ScalarFrontendProjectsOutParametersThroughHostedVerificationAsync()
    {
        const string accepted = "public bool Try(out int32 value) { value = 41; value++; return true; }";
        const string unassigned = "public bool Try(out int32 value) { return true; }";
        var frontend = TerrumProjectPaths.SourceDirectory(RepositoryRoot());
        foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
        {
            var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(accepted),
                "frontend-component", frontend, "-", "--runtime", policy, "--json");
            Assert.That(exit == 0, Is.True, $"{policy}: {exit}: {output}{error}");
            using (var document = JsonDocument.Parse(output))
            {
                Assert.That(document.RootElement.GetProperty("result")
                    .GetProperty("projectionVersion").GetInt32(), Is.EqualTo(87));
            }

            var (invalidExit, invalidOutput, invalidError) = await RunAsync(Encoding.UTF8.GetBytes(unassigned),
                "frontend-component", frontend, "-", "--runtime", policy, "--json");
            Assert.That(invalidExit == 1, Is.True, $"{policy}: {invalidExit}: {invalidOutput}{invalidError}");
        }
    }

    [Test]
    public async Task ScalarFrontendProjectsRecursiveOutputCallsAsync()
    {
        const string recursive = "public bool Try(int32 value, out int32 result) { if (value == 0) { result = 1; return true; } mut int32 previous = 0; if (Try(value - 1, out previous)) { result = previous * value; return true; } return false; }";
        const string readOnlyOutput = "public bool Try(int32 value, out int32 result) { return Try(value, out value); }";
        var frontend = TerrumProjectPaths.SourceDirectory(RepositoryRoot());
        var source = SourceText.From(recursive);
        foreach (var runtimeName in new[] { "fail-fast", "checked-exceptions" })
        {
            var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(recursive),
                "frontend-component", frontend, "-", "--runtime", runtimeName, "--json");
            Assert.That(exit == 0, Is.True, $"{runtimeName}: {exit}: {output}{error}");
            using var document = JsonDocument.Parse(output);
            var result = document.RootElement.GetProperty("result");
            Assert.That(result.GetProperty("projectionVersion").GetInt32(), Is.EqualTo(87));
            Assert.That(global::System.Linq.Enumerable.Any(result.GetProperty("nodes").EnumerateArray(),node => node.GetProperty("operation").GetInt32() == 102),Is.True);
            Assert.That(global::System.Linq.Enumerable.Any(result.GetProperty("nodes").EnumerateArray(),node => node.GetProperty("operation").GetInt32() == 103),Is.True);

            var (invalidExit, invalidOutput, invalidError) = await RunAsync(
                Encoding.UTF8.GetBytes(readOnlyOutput), "frontend-component", frontend, "-",
                "--runtime", runtimeName, "--json");
            Assert.That(invalidExit == 1, Is.True, $"{runtimeName}: {invalidExit}: {invalidOutput}{invalidError}");

            var policy = runtimeName == "fail-fast"
                ? BoundRuntimePolicy.FailFast : BoundRuntimePolicy.CheckedExceptions;
            var hostedCompilation = Compilation.CreateLibrary(SyntaxTree.Parse(source), policy);
            Assert.That(hostedCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, hostedCompilation.Diagnostics));
            var hosted = ((global::System.Func<BoundProgram>)(() => { var __nunitActual0 = hostedCompilation.Program; Assert.That(__nunitActual0, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual0!; }))();
            var outputPath = Path.Combine(TestArtifactPaths.DirectoryPath, $"terrum-frontend-out-{Guid.NewGuid():N}.dll");
            try
            {
                var (buildExit, buildOutput, buildError) = await RunAsync(Encoding.UTF8.GetBytes(recursive),
                    "frontend-build-library", frontend, "-", "-o", outputPath, "--runtime", runtimeName, "--json");
                Assert.That(buildExit == 0, Is.True, $"{runtimeName}: {buildExit}: {buildOutput}{buildError}");
                Assert.That(await File.ReadAllBytesAsync(outputPath), Is.EqualTo(NativeEmitter.EmitLibrary(hosted, ["Try"], (NativeRuntimePolicy)policy)));
                var handle = NativeLibrary.Load(outputPath);
                try
                {
                    var invoke = Marshal.GetDelegateForFunctionPointer<RecursiveTry>(
                        NativeLibrary.GetExport(handle, "Try"));
                    Assert.That(invoke(5, out var factorial), Is.True);
                    Assert.That(factorial, Is.EqualTo(120));
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

    [Test]
    public async Task ScalarFrontendProjectsRecursiveOutVariableCallsAsync()
    {
        const string recursive = "public bool Try(int32 value, out int32 result) { if (value == 0) { result = 1; return true; } if (Try(value - 1, out var previous)) { result = previous * value; return true; } return false; }";
        const string readsUnassigned = "public bool Try(int32 value, out int32 result) { if (Try(value - 1, out var previous)) { result = previous * value; return true; } result = previous; return false; }";
        var frontend = TerrumProjectPaths.SourceDirectory(RepositoryRoot());
        foreach (var runtimeName in new[] { "fail-fast", "checked-exceptions" })
        {
            var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(recursive),
                "frontend-component", frontend, "-", "--runtime", runtimeName, "--json");
            Assert.That(exit == 0, Is.True, $"{runtimeName}: {exit}: {output}{error}");
            using (var document = JsonDocument.Parse(output))
            {
                var result = document.RootElement.GetProperty("result");
                Assert.That(result.GetProperty("projectionVersion").GetInt32(), Is.EqualTo(87));
                Assert.That(global::System.Linq.Enumerable.Any(result.GetProperty("nodes").EnumerateArray(),node => node.GetProperty("operation").GetInt32() == 104),Is.True);
            }

            var invalid = await RunAsync(Encoding.UTF8.GetBytes(readsUnassigned),
                "frontend-component", frontend, "-", "--runtime", runtimeName, "--json");
            Assert.That(invalid.ExitCode == 1, Is.True, $"{runtimeName}: {invalid.ExitCode}: {invalid.Output}{invalid.Error}");
            Assert.That(invalid.Error, Is.Empty);
            using (var invalidDocument = JsonDocument.Parse(invalid.Output))
            {
                Assert.That(invalidDocument.RootElement
                    .GetProperty("diagnostics")[0].GetProperty("code").GetString(), Is.EqualTo("TRM5001"));
            }

            var policy = runtimeName == "fail-fast"
                ? BoundRuntimePolicy.FailFast : BoundRuntimePolicy.CheckedExceptions;
            var hostedCompilation = Compilation.CreateLibrary(SyntaxTree.Parse(SourceText.From(recursive)), policy);
            Assert.That(hostedCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, hostedCompilation.Diagnostics));
            var hosted = ((global::System.Func<BoundProgram>)(() => { var __nunitActual1 = hostedCompilation.Program; Assert.That(__nunitActual1, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual1!; }))();
            var outputPath = Path.Combine(TestArtifactPaths.DirectoryPath, $"terrum-frontend-outvar-{Guid.NewGuid():N}.dll");
            try
            {
                var (buildExit, buildOutput, buildError) = await RunAsync(Encoding.UTF8.GetBytes(recursive),
                    "frontend-build-library", frontend, "-", "-o", outputPath, "--runtime", runtimeName, "--json");
                Assert.That(buildExit == 0, Is.True, $"{runtimeName}: {buildExit}: {buildOutput}{buildError}");
                Assert.That(await File.ReadAllBytesAsync(outputPath), Is.EqualTo(NativeEmitter.EmitLibrary(hosted, ["Try"], (NativeRuntimePolicy)policy)));
                var handle = NativeLibrary.Load(outputPath);
                try
                {
                    var invoke = Marshal.GetDelegateForFunctionPointer<RecursiveTry>(
                        NativeLibrary.GetExport(handle, "Try"));
                    Assert.That(invoke(5, out var factorial), Is.True);
                    Assert.That(factorial, Is.EqualTo(120));
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

    [Test]
    public async Task ScalarFrontendProjectsRecursiveValueCallsAsync()
    {
        const string recursive = "public int32 Sum(int32 value) { if (value == 0) { return 0; } return Sum(value - 1) + value; }";
        var frontend = TerrumProjectPaths.SourceDirectory(RepositoryRoot());
        foreach (var runtimeName in new[] { "fail-fast", "checked-exceptions" })
        {
            var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(recursive),
                "frontend-component", frontend, "-", "--runtime", runtimeName, "--json");
            Assert.That(exit == 0, Is.True, $"{runtimeName}: {exit}: {output}{error}");
            using (var document = JsonDocument.Parse(output))
            {
                var result = document.RootElement.GetProperty("result");
                Assert.That(result.GetProperty("projectionVersion").GetInt32(), Is.EqualTo(87));
                Assert.That(global::System.Linq.Enumerable.Any(result.GetProperty("nodes").EnumerateArray(),node => node.GetProperty("operation").GetInt32() == 102),Is.True);
            }

            var policy = runtimeName == "fail-fast"
                ? BoundRuntimePolicy.FailFast : BoundRuntimePolicy.CheckedExceptions;
            var hostedCompilation = Compilation.CreateLibrary(SyntaxTree.Parse(SourceText.From(recursive)), policy);
            Assert.That(hostedCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, hostedCompilation.Diagnostics));
            var hosted = ((global::System.Func<BoundProgram>)(() => { var __nunitActual2 = hostedCompilation.Program; Assert.That(__nunitActual2, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual2!; }))();
            var outputPath = Path.Combine(TestArtifactPaths.DirectoryPath, $"terrum-frontend-sum-{Guid.NewGuid():N}.dll");
            try
            {
                var (buildExit, buildOutput, buildError) = await RunAsync(Encoding.UTF8.GetBytes(recursive),
                    "frontend-build-library", frontend, "-", "-o", outputPath, "--runtime", runtimeName, "--json");
                Assert.That(buildExit == 0, Is.True, $"{runtimeName}: {buildExit}: {buildOutput}{buildError}");
                Assert.That(await File.ReadAllBytesAsync(outputPath), Is.EqualTo(NativeEmitter.EmitLibrary(hosted, ["Sum"], (NativeRuntimePolicy)policy)));
                var handle = NativeLibrary.Load(outputPath);
                try
                {
                    var invoke = Marshal.GetDelegateForFunctionPointer<RecursiveSum>(
                        NativeLibrary.GetExport(handle, "Sum"));
                    Assert.That(invoke(5), Is.EqualTo(15));
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

    [Test]
    public async Task ScalarFrontendProjectsRecursiveCallStatementsAsync()
    {
        const string recursive = "public int32 Sum(int32 value) { mut int32 remaining = value; if (value > 0) { Sum(value - 1); for (Sum(0); remaining > 0; Sum(0)) { remaining = remaining - 1; } } return value + 42; }";
        var frontend = TerrumProjectPaths.SourceDirectory(RepositoryRoot());
        foreach (var runtimeName in new[] { "fail-fast", "checked-exceptions" })
        {
            var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(recursive),
                "frontend-component", frontend, "-", "--runtime", runtimeName, "--json");
            Assert.That(exit == 0, Is.True, $"{runtimeName}: {exit}: {output}{error}");
            using (var document = JsonDocument.Parse(output))
            {
                var result = document.RootElement.GetProperty("result");
                Assert.That(result.GetProperty("projectionVersion").GetInt32(), Is.EqualTo(87));
                Assert.That(global::System.Linq.Enumerable.Any(result.GetProperty("nodes").EnumerateArray(),node => node.GetProperty("operation").GetInt32() == 102),Is.True);
                Assert.That(global::System.Linq.Enumerable.Any(result.GetProperty("statements").EnumerateArray(),statement => statement.GetProperty("kind").GetInt32() == 37),Is.True);
            }

            var policy = runtimeName == "fail-fast"
                ? BoundRuntimePolicy.FailFast : BoundRuntimePolicy.CheckedExceptions;
            var hostedCompilation = Compilation.CreateLibrary(SyntaxTree.Parse(SourceText.From(recursive)), policy);
            Assert.That(hostedCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, hostedCompilation.Diagnostics));
            var hosted = ((global::System.Func<BoundProgram>)(() => { var __nunitActual3 = hostedCompilation.Program; Assert.That(__nunitActual3, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual3!; }))();
            var outputPath = Path.Combine(TestArtifactPaths.DirectoryPath, $"terrum-frontend-call-statement-{Guid.NewGuid():N}.dll");
            try
            {
                var (buildExit, buildOutput, buildError) = await RunAsync(Encoding.UTF8.GetBytes(recursive),
                    "frontend-build-library", frontend, "-", "-o", outputPath, "--runtime", runtimeName, "--json");
                Assert.That(buildExit == 0, Is.True, $"{runtimeName}: {buildExit}: {buildOutput}{buildError}");
                Assert.That(await File.ReadAllBytesAsync(outputPath), Is.EqualTo(NativeEmitter.EmitLibrary(hosted, ["Sum"], (NativeRuntimePolicy)policy)));
                var handle = NativeLibrary.Load(outputPath);
                try
                {
                    var invoke = Marshal.GetDelegateForFunctionPointer<RecursiveSum>(
                        NativeLibrary.GetExport(handle, "Sum"));
                    Assert.That(invoke(5), Is.EqualTo(47));
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

        foreach (var unsupported in new[]
        {
            "public int32 Sum(int32 value) { Other(value); return value; }",
            "public void Stop(int32 value) { int32 result = Stop(value); }",
        })
        {
            var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(unsupported),
                "frontend-component", frontend, "-", "--runtime", "fail-fast", "--json");
            Assert.That(exit, Is.EqualTo(1));
            Assert.That(output, Is.Not.Empty);
            Assert.That(error, Is.Empty);
        }
    }

    [Test]
    public async Task ScalarFrontendProjectsRecursiveVoidCallStatementsAsync()
    {
        const string recursive = "public void Stop(int32 value) { if (value > 0) { Stop(value - 1); } }";
        var frontend = TerrumProjectPaths.SourceDirectory(RepositoryRoot());
        foreach (var runtimeName in new[] { "fail-fast", "checked-exceptions" })
        {
            var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(recursive),
                "frontend-component", frontend, "-", "--runtime", runtimeName, "--json");
            Assert.That(exit == 0, Is.True, $"{runtimeName}: {exit}: {output}{error}");
            using (var document = JsonDocument.Parse(output))
            {
                var result = document.RootElement.GetProperty("result");
                Assert.That(result.GetProperty("projectionVersion").GetInt32(), Is.EqualTo(87));
                Assert.That(global::System.Linq.Enumerable.Any(result.GetProperty("nodes").EnumerateArray(),node => node.GetProperty("operation").GetInt32() == 102
                        && node.GetProperty("type").GetInt32() == 0),Is.True);
                Assert.That(global::System.Linq.Enumerable.Any(result.GetProperty("statements").EnumerateArray(),statement => statement.GetProperty("kind").GetInt32() == 37),Is.True);
            }

            var policy = runtimeName == "fail-fast"
                ? BoundRuntimePolicy.FailFast : BoundRuntimePolicy.CheckedExceptions;
            var hostedCompilation = Compilation.CreateLibrary(SyntaxTree.Parse(SourceText.From(recursive)), policy);
            Assert.That(hostedCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, hostedCompilation.Diagnostics));
            var hosted = ((global::System.Func<BoundProgram>)(() => { var __nunitActual4 = hostedCompilation.Program; Assert.That(__nunitActual4, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual4!; }))();
            var outputPath = Path.Combine(TestArtifactPaths.DirectoryPath, $"terrum-frontend-void-call-statement-{Guid.NewGuid():N}.dll");
            try
            {
                var (buildExit, buildOutput, buildError) = await RunAsync(Encoding.UTF8.GetBytes(recursive),
                    "frontend-build-library", frontend, "-", "-o", outputPath, "--runtime", runtimeName, "--json");
                Assert.That(buildExit == 0, Is.True, $"{runtimeName}: {buildExit}: {buildOutput}{buildError}");
                Assert.That(await File.ReadAllBytesAsync(outputPath), Is.EqualTo(NativeEmitter.EmitLibrary(hosted, ["Stop"], (NativeRuntimePolicy)policy)));
                var handle = NativeLibrary.Load(outputPath);
                try
                {
                    var invoke = Marshal.GetDelegateForFunctionPointer<RecursiveStop>(
                        NativeLibrary.GetExport(handle, "Stop"));
                    invoke(5);
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

    [Test]
    public async Task ScalarFrontendProjectsRecursiveOutVariableCallStatementsAsync()
    {
        const string recursive = "public bool Try(int32 value, out int32 result) { if (value == 0) { result = 1; return true; } Try(value - 1, out var previous); mut int32 ignored = 0; Try(value - 1, out ignored); result = value; return true; }";
        const string readsUnpublished = "public bool Try(int32 value, out int32 result) { if (value == 0) { result = 1; return true; } Try(value - 1, out var previous); result = previous; return true; }";
        var frontend = TerrumProjectPaths.SourceDirectory(RepositoryRoot());
        foreach (var runtimeName in new[] { "fail-fast", "checked-exceptions" })
        {
            var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(recursive),
                "frontend-component", frontend, "-", "--runtime", runtimeName, "--json");
            Assert.That(exit == 0, Is.True, $"{runtimeName}: {exit}: {output}{error}");
            using (var document = JsonDocument.Parse(output))
            {
                var result = document.RootElement.GetProperty("result");
                Assert.That(result.GetProperty("projectionVersion").GetInt32(), Is.EqualTo(87));
                Assert.That(global::System.Linq.Enumerable.Any(result.GetProperty("nodes").EnumerateArray(),node => node.GetProperty("operation").GetInt32() == 104),Is.True);
                Assert.That(global::System.Linq.Enumerable.Any(result.GetProperty("nodes").EnumerateArray(),node => node.GetProperty("operation").GetInt32() == 103),Is.True);
                Assert.That(global::System.Linq.Enumerable.Any(result.GetProperty("statements").EnumerateArray(),statement => statement.GetProperty("kind").GetInt32() == 37),Is.True);
            }

            var policy = runtimeName == "fail-fast"
                ? BoundRuntimePolicy.FailFast : BoundRuntimePolicy.CheckedExceptions;
            var hostedCompilation = Compilation.CreateLibrary(SyntaxTree.Parse(SourceText.From(recursive)), policy);
            Assert.That(hostedCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, hostedCompilation.Diagnostics));
            var hosted = ((global::System.Func<BoundProgram>)(() => { var __nunitActual5 = hostedCompilation.Program; Assert.That(__nunitActual5, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual5!; }))();
            var outputPath = Path.Combine(TestArtifactPaths.DirectoryPath, $"terrum-frontend-outvar-call-statement-{Guid.NewGuid():N}.dll");
            try
            {
                var (buildExit, buildOutput, buildError) = await RunAsync(Encoding.UTF8.GetBytes(recursive),
                    "frontend-build-library", frontend, "-", "-o", outputPath, "--runtime", runtimeName, "--json");
                Assert.That(buildExit == 0, Is.True, $"{runtimeName}: {buildExit}: {buildOutput}{buildError}");
                Assert.That(await File.ReadAllBytesAsync(outputPath), Is.EqualTo(NativeEmitter.EmitLibrary(hosted, ["Try"], (NativeRuntimePolicy)policy)));
                var handle = NativeLibrary.Load(outputPath);
                try
                {
                    var invoke = Marshal.GetDelegateForFunctionPointer<RecursiveTry>(
                        NativeLibrary.GetExport(handle, "Try"));
                    Assert.That(invoke(5, out var result), Is.True);
                    Assert.That(result, Is.EqualTo(5));
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

        var (invalidExit, invalidOutput, invalidError) = await RunAsync(
            Encoding.UTF8.GetBytes(readsUnpublished), "frontend-component", frontend, "-",
            "--runtime", "fail-fast", "--json");
        Assert.That(invalidExit, Is.EqualTo(1));
        Assert.That(invalidError, Is.Empty);
        using var invalidDocument = JsonDocument.Parse(invalidOutput);
        Assert.That(invalidDocument.RootElement.GetProperty("diagnostics")[0]
            .GetProperty("code").GetString(), Is.EqualTo("TRM5001"));
    }

    [Test]
    public async Task ScalarFrontendProjectsMultipleRecursiveOutCallStatementsAsync()
    {
        const string recursive = "public bool Pair(int32 value, out int32 first, out int32 second) { if (false) { Pair(value, out var previousFirst, out var previousSecond); } first = value; second = value + 1; return true; }";
        var frontend = TerrumProjectPaths.SourceDirectory(RepositoryRoot());
        foreach (var runtimeName in new[] { "fail-fast", "checked-exceptions" })
        {
            var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(recursive),
                "frontend-component", frontend, "-", "--runtime", runtimeName, "--json");
            Assert.That(exit == 0, Is.True, $"{runtimeName}: {exit}: {output}{error}");
            using (var document = JsonDocument.Parse(output))
            {
                var result = document.RootElement.GetProperty("result");
                Assert.That(result.GetProperty("projectionVersion").GetInt32(), Is.EqualTo(87));
                Assert.That(System.Linq.Enumerable.Count(result.GetProperty("nodes").EnumerateArray(),
                    node => node.GetProperty("operation").GetInt32() == 104), Is.EqualTo(2));
                Assert.That(global::System.Linq.Enumerable.Any(result.GetProperty("statements").EnumerateArray(),statement => statement.GetProperty("kind").GetInt32() == 37),Is.True);
            }

            var policy = runtimeName == "fail-fast"
                ? BoundRuntimePolicy.FailFast : BoundRuntimePolicy.CheckedExceptions;
            var hostedCompilation = Compilation.CreateLibrary(SyntaxTree.Parse(SourceText.From(recursive)), policy);
            Assert.That(hostedCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, hostedCompilation.Diagnostics));
            var hosted = ((global::System.Func<BoundProgram>)(() => { var __nunitActual6 = hostedCompilation.Program; Assert.That(__nunitActual6, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual6!; }))();
            var outputPath = Path.Combine(TestArtifactPaths.DirectoryPath, $"terrum-frontend-pair-call-statement-{Guid.NewGuid():N}.dll");
            try
            {
                var (buildExit, buildOutput, buildError) = await RunAsync(Encoding.UTF8.GetBytes(recursive),
                    "frontend-build-library", frontend, "-", "-o", outputPath, "--runtime", runtimeName, "--json");
                Assert.That(buildExit == 0, Is.True, $"{runtimeName}: {buildExit}: {buildOutput}{buildError}");
                Assert.That(await File.ReadAllBytesAsync(outputPath), Is.EqualTo(NativeEmitter.EmitLibrary(hosted, ["Pair"], (NativeRuntimePolicy)policy)));
                var handle = NativeLibrary.Load(outputPath);
                try
                {
                    var invoke = Marshal.GetDelegateForFunctionPointer<RecursivePair>(
                        NativeLibrary.GetExport(handle, "Pair"));
                    Assert.That(invoke(5, out var left, out var right), Is.True);
                    Assert.That(left, Is.EqualTo(5));
                    Assert.That(right, Is.EqualTo(6));
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

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int RecursiveSum(int value);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void RecursiveStop(int value);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate bool RecursivePair(int value, out int left, out int right);
}
