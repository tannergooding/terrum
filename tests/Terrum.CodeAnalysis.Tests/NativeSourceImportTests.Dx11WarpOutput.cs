// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests;

public sealed partial class NativeSourceImportTests
{
    private const string WarpOutputDeclaration = """
        [Terrum.Interop.NativeImport("test.application.dx11-triangle.RenderWarp")]
        [NoThrow] extern int32 RenderTriangleWarp();
        """;

    private static NativeImportCatalog CreateWarpOutputCatalog() =>
        NativeImportCatalog.CreateForTesting(NativeDx11WarpOutputContract.CatalogIdentity,
            [NativeDx11WarpOutputContract.Import]);

    private static Compilation CompileWarpOutput(
        string body = "return RenderTriangleWarp();",
        string declaration = WarpOutputDeclaration, NativeImportCatalog? catalog = null,
        BoundRuntimePolicy policy = BoundRuntimePolicy.FailFast) =>
        Compilation.CreateWithNativeImports([SyntaxTree.Parse(declaration + "\nint32 Main() {" + body + "}")],
            catalog ?? CreateWarpOutputCatalog(), policy);

    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
    public void Dx11WarpOutputUsesOnlyItsSealedZeroArgumentScalarImport(BoundRuntimePolicy policy)
    {
        var compilation = CompileWarpOutput(policy: policy);
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
        Assert.That(image.AsSpan().IndexOf("native-dx11-warp-output.dll\0"u8) >= 0, Is.True);
        Assert.That(image.AsSpan().IndexOf("NativeFfiRenderTriangleWarp\0"u8) >= 0, Is.True);
        foreach (var forbidden in new[] {
            NativeDx11WarpTriangleContract.DllName, NativeDx11WarpTriangleContract.ExportName,
            "D3DCompileFromFile", "D3DCompiler_47", "D3D11CreateDevice", "CreatePixelShader",
            "ID3D11DeviceContext", "CreateTexture2D", "CopyResource", "ClearRenderTargetView",
        })
        {
            Assert.That(image.AsSpan().IndexOf(Encoding.ASCII.GetBytes(forbidden)) < 0, Is.True);
        }
        foreach (var catalog in new[] {
            NativeImportCatalog.WindowsX64, CreateWarpTriangleCatalog(), CreateShaderCompileCatalog(),
        })
        {
            Assert.That(CompileWarpOutput(catalog: catalog).Program, Is.Null);
        }
        Assert.That(CompileWarpTriangle(catalog: CreateWarpOutputCatalog()).Program, Is.Null);
        _ = Assert.Throws<ArgumentException>(() => NativeEmitter.EmitWithNativeImports(program, runtimePolicy));
        _ = Assert.Throws<NotSupportedException>(() => NativeJit.Execute(program, runtimePolicy));
        _ = Assert.Throws<NotSupportedException>(() => NativeEmitter.EmitLibrary(
            program with { EntryPoint = -1 }, ["Main"], runtimePolicy));
        _ = Assert.Throws<NotSupportedException>(() => ModuleCompiler.CreateProducer(
            program with { EntryPoint = -1 }, ["Main"]));
        _ = Assert.Throws<NotSupportedException>(() => TrRmExecutableCompiler.Emit(
            program with { EntryPoint = -1 }, "warp-output"));
    }

    [TestCase("return RenderTriangleWarp(0);")]
    [TestCase("mut uint64 output = 0; return RenderTriangleWarp(out output);")]
    [TestCase("return RenderTriangleWarp().Release();")]
    public void Dx11WarpOutputDoesNotExposeArgumentsOrNativeResources(string body)
    {
        var compilation = CompileWarpOutput(body);
        Assert.That(compilation.Program, Is.Null);
        Assert.That(compilation.Diagnostics, Is.Not.Empty);
    }

    [TestCase("uint64", "")]
    [TestCase("int32", "int32 mode")]
    [TestCase("int32", "out uint64 output")]
    public void Dx11WarpOutputRejectsChangedImportSignatures(string result, string parameters)
    {
        var declaration = $$"""
            [Terrum.Interop.NativeImport("{{NativeDx11WarpOutputContract.ImportKey}}")]
            [NoThrow] extern {{result}} RenderTriangleWarp({{parameters}});
            """;
        Assert.That(CompileWarpOutput("return 0;", declaration).Program, Is.Null);
    }

    [Test]
    public void Dx11WarpOutputSealsItsCatalogAndRejectsForgedBoundSignatures()
    {
        var original = NativeDx11WarpOutputContract.Import;
        foreach (var invalid in new[] {
            original with { Key = "test.alias" },
            original with { DllName = original.DllName.ToUpperInvariant() },
            original with { ExportName = NativeDx11WarpTriangleContract.ExportName },
            original with { ReturnType = PrimitiveType.UInt64 },
            original with { ParameterTypes = [PrimitiveType.UInt64] },
            original with { ParameterTypes = default },
            original with { ParameterContracts = [NativeImportParameterContract.Value] },
            original with { ResultContract = (NativeImportResultContract)int.MaxValue },
            original with { IsNoThrow = false },
            original with { RequiresUnsafeCaller = true },
            original with { DoesNotReturn = true },
            original with { DeploymentPolicy = NativeImportDeploymentPolicy.System32 },
        })
        {
            _ = Assert.Throws<ArgumentException>(() => NativeImportCatalog.CreateForTesting(
                NativeDx11WarpOutputContract.CatalogIdentity, [invalid]));
        }
        _ = Assert.Throws<ArgumentException>(() => new NativeImportCatalog(
            NativeDx11WarpOutputContract.CatalogIdentity, [original]));
        _ = Assert.Throws<ArgumentException>(() => NativeImportCatalog.CreateForTesting("test.other", [original]));
        _ = Assert.Throws<ArgumentException>(() => NativeImportCatalog.CreateForTesting(
            NativeDx11WarpTriangleContract.CatalogIdentity, [original]));
        var extra = new NativeImportDefinition("test.extra", "fixture.dll", "Extra", [],
            PrimitiveType.Int32, true, true, false, NativeImportDeploymentPolicy.ApplicationDirectory);
        _ = Assert.Throws<ArgumentException>(() => NativeImportCatalog.CreateForTesting(
            NativeDx11WarpOutputContract.CatalogIdentity, [original, extra]));
        _ = Assert.Throws<ArgumentException>(() => NativeImportCatalog.CreateForTesting(
            NativeDx11WarpOutputContract.CatalogIdentity, [extra]));
        _ = Assert.Throws<ArgumentException>(() => new NativeImportCatalog(
            NativeDx11WarpOutputContract.CatalogIdentity, [extra]));
        var __nunitAssertValue5 = CompileWarpOutput().Program;
        Assert.That(__nunitAssertValue5, Is.TypeOf<BoundProgram>());
        var program = ((BoundProgram)__nunitAssertValue5);
        var __nunitAssertValue6 = global::System.Linq.Enumerable.ToArray(global::System.Linq.Enumerable.Where(program.Functions, static function => function.IsExternal));
        Assert.That(__nunitAssertValue6, Has.Exactly(1).Items);
        var external = __nunitAssertValue6[0];
        foreach (var forged in new[] {
            program with { NativeImportCatalog = null },
            program with { NativeImportCatalogId = NativeDx11WarpTriangleContract.CatalogIdentity },
            program with { NativeImportCatalog = CreateWarpTriangleCatalog() },
            program with { Functions = program.Functions.Replace(external,
                external with { NativeImportKey = NativeDx11WarpTriangleContract.ImportKey }) },
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
    public async Task Dx11WarpOutputReadsRealPixelsAndChecksCleanupAndErrorsAsync()
    {
        Assert.That(OperatingSystem.IsWindows() && RuntimeInformation.ProcessArchitecture == Architecture.X64, Is.True);
        var root = FindRepositoryRoot();
        var environment = await FindNativeFixtureEnvironmentAsync(root).ConfigureAwait(false);
        var directory = TestArtifactPaths.CreateDirectory("terrum-dx11-warp-output-");
        var native = Path.Combine(root, "tests", "native");
        var comspec = Environment.GetEnvironmentVariable("ComSpec")
            ?? throw new InvalidOperationException("ComSpec is unset.");
        try
        {
            var importsPath = Path.Combine(directory, "warp-output-imports.txt");
            _ = await RunAsync(new ProcessStartInfo(comspec) {
                Arguments = $"/d /s /c \"call \"{environment}\" >nul && cl /nologo /W4 /WX /wd4201 /O2 /MT /LD /TC /Fo\"{directory}\\bridge.obj\" /Fe\"{directory}\\{NativeDx11WarpOutputContract.DllName}\" \"{native}\\native-dx11-warp-output.c\" /link /incremental:no d3d11.lib && cl /nologo /W4 /WX /wd4201 /O2 /MT /TC /Fo\"{directory}\\harness.obj\" /Fe\"{directory}\\harness.exe\" \"{native}\\native-dx11-warp-output-test.c\" /link /incremental:no d3d11.lib && cl /nologo /W4 /WX /wd4201 /Od /TC /c /Fo\"{directory}\\sdk.obj\" \"{native}\\dx11-warp-output-sdk-probe.c\" && cl /nologo /W4 /WX /wd4201 /O2 /MT /LD /TC /Fo\"{directory}\\shadow.obj\" /Fe\"{directory}\\D3DCompiler_47.dll\" \"{native}\\native-dx11-compiler-shadow.c\" /link /incremental:no && dumpbin /imports \"{directory}\\{NativeDx11WarpOutputContract.DllName}\" > \"{importsPath}\"\"",
                WorkingDirectory = directory,
            }, TimeSpan.FromMinutes(2)).ConfigureAwait(false);
            var imports = await File.ReadAllTextAsync(importsPath).ConfigureAwait(false);
            Assert.That(imports.Contains("d3d11.dll", StringComparison.OrdinalIgnoreCase), Is.True);
            Assert.That(imports.Contains("D3DCompiler", StringComparison.OrdinalIgnoreCase), Is.False);
            Assert.That(imports.Contains("LoadLibraryExW", StringComparison.Ordinal), Is.True);
            Assert.That(imports.Contains("GetProcAddress", StringComparison.Ordinal), Is.True);

            var shaderPath = Path.Combine(directory, "triangle.hlsl");
            var shader = await File.ReadAllTextAsync(Path.Combine(root, "samples", "design",
                "win32-dx11-triangle", "triangle.hlsl")).ConfigureAwait(false);
            await File.WriteAllTextAsync(shaderPath, shader).ConfigureAwait(false);
            var harness = Path.Combine(directory, "harness.exe");
            var harnessOutput = await RunAsync(new ProcessStartInfo(harness) {
                WorkingDirectory = directory,
            }, TimeSpan.FromSeconds(90)).ConfigureAwait(false);
            Assert.That(harnessOutput.Contains("real pixels: (16,16)=", StringComparison.Ordinal), Is.True);
            Assert.That(harnessOutput.Contains("; (0,0)=(0,0,0,255)", StringComparison.Ordinal), Is.True);
            Assert.That(harnessOutput.Contains("case real-output[0]: 0x00000000; events=vpdVLBPTRScopviatwDXMUC; maps=1; unmaps=1", StringComparison.Ordinal), Is.True);
            Assert.That(harnessOutput.Contains("case map-failed[0]: 0x80070057; events=vpdVLBPTRScopviatwDXMC; maps=1; unmaps=0", StringComparison.Ordinal), Is.True);
            Assert.That(harnessOutput.Contains("case map-null[0]: 0x8000FFFF;", StringComparison.Ordinal), Is.True);
            Assert.That(harnessOutput.Contains("case padded-rows[0]: 0x00000000;", StringComparison.Ordinal), Is.True);
            Assert.That(harnessOutput.Contains("case pixels-and-unload[0]: 0x80004005;", StringComparison.Ordinal), Is.True);
            Assert.That(harnessOutput.Contains("62 wrapper cases verified: real WARP pixels, exact calls, RowPitch, Map/Unmap, and cleanup", StringComparison.Ordinal), Is.True);
            var shadowOutput = await RunAsync(new ProcessStartInfo(harness) {
                Arguments = "preloaded-shadow",
                WorkingDirectory = directory,
            }, TimeSpan.FromSeconds(30)).ConfigureAwait(false);
            Assert.That(shadowOutput.Contains("rejected before export resolution", StringComparison.Ordinal), Is.True);

            var working = Directory.CreateDirectory(Path.Combine(directory, "unrelated-working-directory")).FullName;
            await File.WriteAllTextAsync(Path.Combine(working, "triangle.hlsl"), "not HLSL").ConfigureAwait(false);
            File.Copy(Path.Combine(directory, "D3DCompiler_47.dll"),
                Path.Combine(working, "D3DCompiler_47.dll"));
            foreach (var policy in new[] { BoundRuntimePolicy.FailFast, BoundRuntimePolicy.CheckedExceptions })
            {
                var runtimePolicy = policy == BoundRuntimePolicy.FailFast
                    ? NativeRuntimePolicy.FailFast : NativeRuntimePolicy.CheckedExceptions;
                var __nunitAssertValue7 = CompileWarpOutput(policy: policy).Program;
                Assert.That(__nunitAssertValue7, Is.TypeOf<BoundProgram>());
                var program = ((BoundProgram)__nunitAssertValue7);
                var executable = Path.Combine(directory, $"warp-output-{policy}.exe");
                await File.WriteAllBytesAsync(executable, NativeEmitter.EmitWithNativeImportsForTesting(
                    program, runtimePolicy)).ConfigureAwait(false);
                var success = await RunWarpTriangleAsync(executable, working).ConfigureAwait(false);
                Assert.That(success.ExitCode, Is.EqualTo(0));
                Assert.That(success.Output, Is.Empty);
                Assert.That(success.Error, Is.Empty);

                await File.WriteAllTextAsync(shaderPath,
                    shader.Replace("0.1f, 0.8f, 0.9f", "1.0f, 0.0f, 0.0f", StringComparison.Ordinal))
                    .ConfigureAwait(false);
                var mismatch = await RunWarpTriangleAsync(executable, working).ConfigureAwait(false);
                Assert.That(mismatch.ExitCode, Is.EqualTo(unchecked((int)0x80004005)));
                Assert.That(mismatch.Error.Contains("pixel output failed (0x80004005)", StringComparison.Ordinal), Is.True);

                await File.WriteAllTextAsync(shaderPath,
                    shader.Replace("PixelMain", "MissingPixelMain", StringComparison.Ordinal))
                    .ConfigureAwait(false);
                var missingEntry = await RunWarpTriangleAsync(executable, working).ConfigureAwait(false);
                Assert.That(missingEntry.ExitCode < 0, Is.True);
                Assert.That(missingEntry.Error.Contains("PixelMain failed", StringComparison.Ordinal), Is.True);

                File.Delete(shaderPath);
                var missingAsset = await RunWarpTriangleAsync(executable, working).ConfigureAwait(false);
                Assert.That(missingAsset.ExitCode < 0, Is.True);
                Assert.That(missingAsset.Error.Contains("VertexMain failed", StringComparison.Ordinal), Is.True);
                await File.WriteAllTextAsync(shaderPath, shader).ConfigureAwait(false);
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
