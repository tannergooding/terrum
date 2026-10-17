// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests;

public sealed partial class NativeSourceImportTests
{
    private const string WarpTriangleDeclaration = """
        [Terrum.Interop.NativeImport("test.application.dx11-triangle.DrawWarp")]
        [NoThrow] extern int32 DrawTriangleWarp();
        """;

    private static NativeImportCatalog CreateWarpTriangleCatalog() =>
        NativeImportCatalog.CreateForTesting(NativeDx11WarpTriangleContract.CatalogIdentity,
            [NativeDx11WarpTriangleContract.Import]);

    private static Compilation CompileWarpTriangle(
        string body = "return DrawTriangleWarp();",
        string declaration = WarpTriangleDeclaration, NativeImportCatalog? catalog = null,
        BoundRuntimePolicy policy = BoundRuntimePolicy.FailFast) =>
        Compilation.CreateWithNativeImports([SyntaxTree.Parse(declaration + "\nint32 Main() {" + body + "}")],
            catalog ?? CreateWarpTriangleCatalog(), policy);

    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
    public void Dx11WarpTriangleUsesOnlyTheSealedZeroArgumentScalarImport(BoundRuntimePolicy policy)
    {
        var compilation = CompileWarpTriangle(policy: policy);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var __nunitAssertValue0 = compilation.Program;
        Assert.That(__nunitAssertValue0, Is.TypeOf<BoundProgram>());
        var program = ((BoundProgram)__nunitAssertValue0);
        var __nunitAssertValue1 = global::System.Linq.Enumerable.ToArray(global::System.Linq.Enumerable.Where(program.Functions, static function => function.IsExternal));
        Assert.That(__nunitAssertValue1, Has.Exactly(1).Items);
        var external = __nunitAssertValue1[0];
        Assert.That(external.ReturnType, Is.EqualTo(PrimitiveType.Int32));
        Assert.That(external.ParameterCount, Is.EqualTo(0));
        Assert.That(external.Variables, Is.Empty);
        Assert.That(program.Structures, Is.Empty);
        Assert.That(program.NativeUtf8ZLiterals, Is.Empty);
        Assert.That(program.NativeUtf16ZLiterals, Is.Empty);
        var __nunitAssertValue2 = global::System.Linq.Enumerable.ToArray(program.Functions[program.EntryPoint].Body.Statements);
        Assert.That(__nunitAssertValue2, Has.Exactly(1).Items);
        var __nunitAssertValue3 = __nunitAssertValue2[0];
        Assert.That(__nunitAssertValue3, Is.TypeOf<BoundReturn>());
        var __nunitAssertValue4 = (((BoundReturn)__nunitAssertValue3)).Value;
        Assert.That(__nunitAssertValue4, Is.TypeOf<BoundCall>());
        var call = ((BoundCall)__nunitAssertValue4);
        Assert.That(call.Arguments, Is.Empty);
        Assert.That(call.Type, Is.EqualTo(PrimitiveType.Int32));
        var runtimePolicy = policy == BoundRuntimePolicy.FailFast
            ? NativeRuntimePolicy.FailFast : NativeRuntimePolicy.CheckedExceptions;
        var image = NativeEmitter.EmitWithNativeImportsForTesting(program, runtimePolicy);
        Assert.That(image.AsSpan().IndexOf("native-dx11-warp-triangle.dll\0"u8) >= 0, Is.True);
        Assert.That(image.AsSpan().IndexOf("NativeFfiDrawTriangleWarp\0"u8) >= 0, Is.True);
        foreach (var forbidden in new[] {
            "D3DCompileFromFile", "D3DCompiler_47", "D3D11CreateDevice", "CreateVertexShader",
            "ID3D11DeviceContext", "IASetVertexBuffers", "RSSetViewports",
        })
        {
            Assert.That(image.AsSpan().IndexOf(System.Text.Encoding.ASCII.GetBytes(forbidden)) < 0, Is.True);
        }
        Assert.That(CompileWarpTriangle(catalog: NativeImportCatalog.WindowsX64).Program, Is.Null);
        Assert.That(CompileWarpTriangle(catalog: CreateShaderCompileCatalog()).Program, Is.Null);
        _ = Assert.Throws<ArgumentException>(() => NativeEmitter.EmitWithNativeImports(program, runtimePolicy));
        _ = Assert.Throws<NotSupportedException>(() => NativeJit.Execute(program, runtimePolicy));
        _ = Assert.Throws<NotSupportedException>(() => NativeEmitter.EmitLibrary(
            program with { EntryPoint = -1 }, ["Main"], runtimePolicy));
        _ = Assert.Throws<NotSupportedException>(() => ModuleCompiler.CreateProducer(
            program with { EntryPoint = -1 }, ["Main"]));
        _ = Assert.Throws<NotSupportedException>(() => TrRmExecutableCompiler.Emit(
            program with { EntryPoint = -1 }, "warp-triangle"));
    }

    [TestCase("return DrawTriangleWarp(0);")]
    [TestCase("mut uint64 output = 0; return DrawTriangleWarp(out output);")]
    [TestCase("return DrawTriangleWarp().Release();")]
    public void Dx11WarpTriangleDoesNotExposeArgumentsOrNativeResources(string body)
    {
        var compilation = CompileWarpTriangle(body);
        Assert.That(compilation.Program, Is.Null);
        Assert.That(compilation.Diagnostics, Is.Not.Empty);
    }

    [TestCase("uint64", "")]
    [TestCase("int32", "int32 mode")]
    [TestCase("int32", "out uint64 output")]
    public void Dx11WarpTriangleRejectsChangedImportSignatures(string result, string parameters)
    {
        var declaration = $$"""
            [Terrum.Interop.NativeImport("{{NativeDx11WarpTriangleContract.ImportKey}}")]
            [NoThrow] extern {{result}} DrawTriangleWarp({{parameters}});
            """;
        Assert.That(CompileWarpTriangle("return 0;", declaration).Program, Is.Null);
    }

    [Test]
    public void Dx11WarpTriangleSealsItsCatalogAndRejectsForgedBoundSignatures()
    {
        var original = NativeDx11WarpTriangleContract.Import;
        foreach (var invalid in new[] {
            original with { Key = "test.alias" },
            original with { DllName = original.DllName.ToUpperInvariant() },
            original with { ExportName = "D3DCompileFromFile" },
            original with { ReturnType = PrimitiveType.UInt64 },
            original with { ParameterTypes = [PrimitiveType.UInt64] },
            original with { ParameterTypes = default },
            original with { ParameterContracts = [NativeImportParameterContract.Value] },
            original with { IsNoThrow = false },
            original with { RequiresUnsafeCaller = true },
            original with { DoesNotReturn = true },
            original with { DeploymentPolicy = NativeImportDeploymentPolicy.System32 },
        })
        {
            _ = Assert.Throws<ArgumentException>(() => NativeImportCatalog.CreateForTesting(
                NativeDx11WarpTriangleContract.CatalogIdentity, [invalid]));
        }
        _ = Assert.Throws<ArgumentException>(() => new NativeImportCatalog(
            NativeDx11WarpTriangleContract.CatalogIdentity, [original]));
        _ = Assert.Throws<ArgumentException>(() => NativeImportCatalog.CreateForTesting("test.other", [original]));
        var extra = new NativeImportDefinition("test.extra", "fixture.dll", "Extra", [],
            PrimitiveType.Int32, true, true, false, NativeImportDeploymentPolicy.ApplicationDirectory);
        _ = Assert.Throws<ArgumentException>(() => NativeImportCatalog.CreateForTesting(
            NativeDx11WarpTriangleContract.CatalogIdentity, [original, extra]));
        var __nunitAssertValue5 = CompileWarpTriangle().Program;
        Assert.That(__nunitAssertValue5, Is.TypeOf<BoundProgram>());
        var program = ((BoundProgram)__nunitAssertValue5);
        var __nunitAssertValue6 = global::System.Linq.Enumerable.ToArray(global::System.Linq.Enumerable.Where(program.Functions, static function => function.IsExternal));
        Assert.That(__nunitAssertValue6, Has.Exactly(1).Items);
        var external = __nunitAssertValue6[0];
        foreach (var forged in new[] {
            program with { NativeImportCatalog = null },
            program with { NativeImportCatalogId = "test.other" },
            program with { Functions = program.Functions.Replace(external,
                external with { ReturnType = PrimitiveType.UInt64 }) },
            program with { Functions = program.Functions.Replace(external,
                external with { ParameterCount = 1 }) },
            program with { Functions = program.Functions.Replace(external,
                external with { IsNoThrow = false }) },
        })
        {
            _ = Assert.Throws<ArgumentException>(() => SemanticVerifier.Verify(forged));
            _ = Assert.Throws<ArgumentException>(() =>
                NativeEmitter.EmitWithNativeImportsForTesting(forged, NativeRuntimePolicy.FailFast));
        }
    }

    [Test]
    public async Task Dx11WarpTriangleSubmitsOneRealWarpDrawAndCleansPartialOutputsAsync()
    {
        Assert.That(OperatingSystem.IsWindows() && RuntimeInformation.ProcessArchitecture == Architecture.X64, Is.True);
        var root = FindRepositoryRoot();
        var environment = await FindNativeFixtureEnvironmentAsync(root).ConfigureAwait(false);
        var directory = TestArtifactPaths.CreateDirectory("terrum-dx11-warp-triangle-");
        var native = Path.Combine(root, "tests", "native");
        var comspec = Environment.GetEnvironmentVariable("ComSpec")
            ?? throw new InvalidOperationException("ComSpec is unset.");
        try
        {
            var importsPath = Path.Combine(directory, "warp-triangle-imports.txt");
            var sdk = Path.Combine(native, "dx11-sdk-layout-probe.c");
            _ = await RunAsync(new ProcessStartInfo(comspec) {
                Arguments = $"/d /s /c \"call \"{environment}\" >nul && cl /nologo /W4 /WX /wd4201 /O2 /MT /LD /TC /Fo\"{directory}\\bridge.obj\" /Fe\"{directory}\\{NativeDx11WarpTriangleContract.DllName}\" \"{native}\\native-dx11-warp-triangle.c\" /link /incremental:no d3d11.lib && cl /nologo /W4 /WX /wd4201 /O2 /MT /TC /Fo\"{directory}\\harness.obj\" /Fe\"{directory}\\harness.exe\" \"{native}\\native-dx11-warp-triangle-test.c\" /link /incremental:no d3d11.lib && cl /nologo /W4 /WX /wd4201 /Od /TC /c /Fo\"{directory}\\sdk.obj\" \"{sdk}\" && cl /nologo /W4 /WX /wd4201 /O2 /MT /LD /TC /Fo\"{directory}\\shadow.obj\" /Fe\"{directory}\\D3DCompiler_47.dll\" \"{native}\\native-dx11-compiler-shadow.c\" /link /incremental:no && dumpbin /imports \"{directory}\\{NativeDx11WarpTriangleContract.DllName}\" > \"{importsPath}\"\"",
                WorkingDirectory = directory,
            }, TimeSpan.FromMinutes(2)).ConfigureAwait(false);
            var imports = await File.ReadAllTextAsync(importsPath).ConfigureAwait(false);
            Assert.That(imports.Contains("d3d11.dll", StringComparison.OrdinalIgnoreCase), Is.True);
            Assert.That(imports.Contains("D3DCompiler", StringComparison.OrdinalIgnoreCase), Is.False);
            Assert.That(imports.Contains("LoadLibraryExW", StringComparison.Ordinal), Is.True);
            Assert.That(imports.Contains("GetProcAddress", StringComparison.Ordinal), Is.True);

            File.Copy(Path.Combine(root, "samples", "design", "win32-dx11-triangle", "triangle.hlsl"),
                Path.Combine(directory, "triangle.hlsl"));
            var harness = Path.Combine(directory, "harness.exe");
            var harnessOutput = await RunAsync(new ProcessStartInfo(harness) {
                WorkingDirectory = directory,
            }, TimeSpan.FromSeconds(90)).ConfigureAwait(false);
            Assert.That(harnessOutput.Contains("case 0: 0x00000000; setup=VLBSIATRDC", StringComparison.Ordinal), Is.True);
            Assert.That(harnessOutput.Contains("case 1: 0x80004005; setup=C", StringComparison.Ordinal), Is.True);
            Assert.That(harnessOutput.Contains("case 4: 0x80004005; setup=VC", StringComparison.Ordinal), Is.True);
            Assert.That(harnessOutput.Contains("case 5: 0x80004005; setup=VLC", StringComparison.Ordinal), Is.True);
            Assert.That(harnessOutput.Contains("case 6: 0x80004005; setup=VLBC", StringComparison.Ordinal), Is.True);
            Assert.That(harnessOutput.Contains("case 10: 0x800700AA; setup=VLBSIATRDC", StringComparison.Ordinal), Is.True);
            Assert.That(harnessOutput.Contains("case 11: 0x80004005; setup=VC", StringComparison.Ordinal), Is.True);
            Assert.That(harnessOutput.Contains("case 12: 0x80070057; setup=", StringComparison.Ordinal), Is.True);
            Assert.That(harnessOutput.Contains("case 13: 0x80004005; setup=", StringComparison.Ordinal), Is.True);
            Assert.That(harnessOutput.Contains("case 14: 0x8000FFFF; setup=", StringComparison.Ordinal), Is.True);
            Assert.That(harnessOutput.Contains("case 15: 0x8000FFFF; setup=", StringComparison.Ordinal), Is.True);
            Assert.That(harnessOutput.Contains("one draw, 12 existing cases, and 4 compiler-output cases verified", StringComparison.Ordinal), Is.True);
            var shadowOutput = await RunAsync(new ProcessStartInfo(harness) {
                Arguments = "preloaded-shadow",
                WorkingDirectory = directory,
            }, TimeSpan.FromSeconds(30)).ConfigureAwait(false);
            Assert.That(shadowOutput.Contains("rejected before export resolution", StringComparison.Ordinal), Is.True);
            var __nunitAssertValue7 = CompileWarpTriangle().Program;
            Assert.That(__nunitAssertValue7, Is.TypeOf<BoundProgram>());
            var program = ((BoundProgram)__nunitAssertValue7);
            var executable = Path.Combine(directory, "warp-triangle.exe");
            await File.WriteAllBytesAsync(executable, NativeEmitter.EmitWithNativeImportsForTesting(
                program, NativeRuntimePolicy.FailFast)).ConfigureAwait(false);
            var working = Directory.CreateDirectory(Path.Combine(directory, "unrelated-working-directory")).FullName;
            await File.WriteAllTextAsync(Path.Combine(working, "triangle.hlsl"), "not HLSL").ConfigureAwait(false);
            File.Copy(Path.Combine(directory, "D3DCompiler_47.dll"),
                Path.Combine(working, "D3DCompiler_47.dll"));
            var success = await RunWarpTriangleAsync(executable, working).ConfigureAwait(false);
            Assert.That(success.ExitCode, Is.EqualTo(0));
            Assert.That(success.Error, Is.Empty);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static async Task<(int ExitCode, string Output, string Error)> RunWarpTriangleAsync(
        string executable, string workingDirectory)
    {
        using var process = Process.Start(new ProcessStartInfo(executable) {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        }) ?? throw new InvalidOperationException("Could not start the WARP triangle gate.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        try
        {
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(60)).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
            await process.WaitForExitAsync().ConfigureAwait(false);
            throw;
        }
        return (process.ExitCode, await output.ConfigureAwait(false), await error.ConfigureAwait(false));
    }
}
