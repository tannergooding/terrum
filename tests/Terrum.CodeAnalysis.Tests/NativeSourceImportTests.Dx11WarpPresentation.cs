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
    private const string WarpPresentationDeclaration = """
        [Terrum.Interop.NativeImport("test.application.dx11-triangle.PresentWarp")]
        [NoThrow] extern int32 PresentTriangleWarp();
        """;

    private static NativeImportCatalog CreateWarpPresentationCatalog() =>
        NativeImportCatalog.CreateForTesting(NativeDx11WarpPresentationContract.CatalogIdentity,
            [NativeDx11WarpPresentationContract.Import]);

    private static Compilation CompileWarpPresentation(
        string body = "return PresentTriangleWarp();",
        string declaration = WarpPresentationDeclaration, NativeImportCatalog? catalog = null,
        BoundRuntimePolicy policy = BoundRuntimePolicy.FailFast) =>
        Compilation.CreateWithNativeImports([SyntaxTree.Parse(declaration + "\nint32 Main() {" + body + "}")],
            catalog ?? CreateWarpPresentationCatalog(), policy);

    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
    public void Dx11WarpPresentationUsesOnlyItsSealedZeroArgumentScalarImport(BoundRuntimePolicy policy)
    {
        var compilation = CompileWarpPresentation(policy: policy);
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
        Assert.That(image.AsSpan().IndexOf("native-dx11-warp-presentation.dll\0"u8) >= 0, Is.True);
        Assert.That(image.AsSpan().IndexOf("NativeFfiPresentTriangleWarp\0"u8) >= 0, Is.True);
        foreach (var forbidden in new[] {
            NativeDx11WarpOutputContract.DllName, NativeDx11WarpTriangleContract.DllName,
            "D3D11CreateDeviceAndSwapChain", "DwmFlush", "IDXGISwapChain", "GetBuffer",
            "RegisterClassExW", "CreateWindowExW", "PeekMessageW", "DestroyWindow",
        })
        {
            Assert.That(image.AsSpan().IndexOf(Encoding.ASCII.GetBytes(forbidden)) < 0, Is.True);
        }
        foreach (var catalog in new[] {
            NativeImportCatalog.WindowsX64, CreateWarpOutputCatalog(), CreateWarpTriangleCatalog(),
        })
        {
            Assert.That(CompileWarpPresentation(catalog: catalog).Program, Is.Null);
        }
        Assert.That(CompileWarpOutput(catalog: CreateWarpPresentationCatalog()).Program, Is.Null);
        Assert.That(CompileWarpTriangle(catalog: CreateWarpPresentationCatalog()).Program, Is.Null);
        _ = Assert.Throws<ArgumentException>(() => NativeEmitter.EmitWithNativeImports(program, runtimePolicy));
        _ = Assert.Throws<NotSupportedException>(() => NativeJit.Execute(program, runtimePolicy));
        _ = Assert.Throws<NotSupportedException>(() => NativeEmitter.EmitLibrary(
            program with { EntryPoint = -1 }, ["Main"], runtimePolicy));
        _ = Assert.Throws<NotSupportedException>(() => ModuleCompiler.CreateProducer(
            program with { EntryPoint = -1 }, ["Main"]));
        _ = Assert.Throws<NotSupportedException>(() => TrRmExecutableCompiler.Emit(
            program with { EntryPoint = -1 }, "warp-presentation"));
    }

    [TestCase("return PresentTriangleWarp(0);")]
    [TestCase("mut uint64 output = 0; return PresentTriangleWarp(out output);")]
    [TestCase("return PresentTriangleWarp().Release();")]
    public void Dx11WarpPresentationDoesNotExposeArgumentsOrNativeResources(string body)
    {
        var compilation = CompileWarpPresentation(body);
        Assert.That(compilation.Program, Is.Null);
        Assert.That(compilation.Diagnostics, Is.Not.Empty);
    }

    [TestCase("uint64", "")]
    [TestCase("int32", "int32 mode")]
    [TestCase("int32", "out uint64 output")]
    public void Dx11WarpPresentationRejectsChangedImportSignatures(string result, string parameters)
    {
        var declaration = $$"""
            [Terrum.Interop.NativeImport("{{NativeDx11WarpPresentationContract.ImportKey}}")]
            [NoThrow] extern {{result}} PresentTriangleWarp({{parameters}});
            """;
        Assert.That(CompileWarpPresentation("return 0;", declaration).Program, Is.Null);
    }

    [Test]
    public void Dx11WarpPresentationSealsItsCatalogAndRejectsForgedBoundSignatures()
    {
        var original = NativeDx11WarpPresentationContract.Import;
        foreach (var invalid in new[] {
            original with { Key = "test.alias" },
            original with { DllName = original.DllName.ToUpperInvariant() },
            original with { ExportName = NativeDx11WarpOutputContract.ExportName },
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
                NativeDx11WarpPresentationContract.CatalogIdentity, [invalid]));
        }
        _ = Assert.Throws<ArgumentException>(() => new NativeImportCatalog(
            NativeDx11WarpPresentationContract.CatalogIdentity, [original]));
        foreach (var identity in new[] {
            "test.other", NativeDx11WarpOutputContract.CatalogIdentity, NativeDx11WarpTriangleContract.CatalogIdentity,
        })
        {
            _ = Assert.Throws<ArgumentException>(() => NativeImportCatalog.CreateForTesting(identity, [original]));
        }
        var extra = new NativeImportDefinition("test.extra", "fixture.dll", "Extra", [],
            PrimitiveType.Int32, true, true, false, NativeImportDeploymentPolicy.ApplicationDirectory);
        _ = Assert.Throws<ArgumentException>(() => NativeImportCatalog.CreateForTesting(
            NativeDx11WarpPresentationContract.CatalogIdentity, [original, extra]));
        _ = Assert.Throws<ArgumentException>(() => NativeImportCatalog.CreateForTesting(
            NativeDx11WarpPresentationContract.CatalogIdentity, [extra]));
        _ = Assert.Throws<ArgumentException>(() => new NativeImportCatalog(
            NativeDx11WarpPresentationContract.CatalogIdentity, [extra]));
        var __nunitAssertValue5 = CompileWarpPresentation().Program;
        Assert.That(__nunitAssertValue5, Is.TypeOf<BoundProgram>());
        var program = ((BoundProgram)__nunitAssertValue5);
        var __nunitAssertValue6 = global::System.Linq.Enumerable.ToArray(global::System.Linq.Enumerable.Where(program.Functions, static function => function.IsExternal));
        Assert.That(__nunitAssertValue6, Has.Exactly(1).Items);
        var external = __nunitAssertValue6[0];
        foreach (var forged in new[] {
            program with { NativeImportCatalog = null },
            program with { NativeImportCatalogId = NativeDx11WarpOutputContract.CatalogIdentity },
            program with { NativeImportCatalog = CreateWarpOutputCatalog() },
            program with { Functions = program.Functions.Replace(external,
                external with { NativeImportKey = NativeDx11WarpOutputContract.ImportKey }) },
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

    [InteractiveWindowTest]
    [Category("InteractiveWindow")]
    public async Task Dx11WarpPresentationCloseAndQuitDeferDestructionUntilCleanupAsync()
    {
        Assert.That(OperatingSystem.IsWindows() && RuntimeInformation.ProcessArchitecture == Architecture.X64, Is.True);
        var root = FindRepositoryRoot();
        var environment = await FindNativeFixtureEnvironmentAsync(root).ConfigureAwait(false);
        var directory = Directory.CreateTempSubdirectory("terrum-dx11-warp-close-quit-").FullName;
        var native = Path.Combine(root, "tests", "native");
        var comspec = Environment.GetEnvironmentVariable("ComSpec")
            ?? throw new InvalidOperationException("ComSpec is unset.");
        try
        {
            _ = await RunAsync(new ProcessStartInfo(comspec) {
                Arguments = $"/d /s /c \"call \"{environment}\" >nul && cl /nologo /W4 /WX /wd4201 /O2 /MT /TC /Fo\"{directory}\\harness.obj\" /Fe\"{directory}\\harness.exe\" \"{native}\\native-dx11-warp-presentation-test.c\" /link /incremental:no d3d11.lib dxguid.lib user32.lib dwmapi.lib && cl /nologo /W4 /WX /wd4201 /Od /TC /c /Fo\"{directory}\\sdk.obj\" \"{native}\\dx11-warp-presentation-sdk-probe.c\"\"",
                WorkingDirectory = directory,
            }, TimeSpan.FromMinutes(2)).ConfigureAwait(false);
            File.Copy(Path.Combine(root, "samples", "design", "win32-dx11-triangle", "triangle.hlsl"),
                Path.Combine(directory, "triangle.hlsl"));
            var output = await RunAsync(new ProcessStartInfo(Path.Combine(directory, "harness.exe")) {
                Arguments = "close-quit",
                WorkingDirectory = directory,
            }, TimeSpan.FromSeconds(30)).ConfigureAwait(false);
            Assert.That(output.Contains("case early-close[0]: 0x800704C7; events=rnhxNz;", StringComparison.Ordinal), Is.True);
            Assert.That(output.Contains("case early-quit[0]: 0x800704C7; events=rnhxNz;", StringComparison.Ordinal), Is.True);
            Assert.That(output.Contains("case repeated-close[0]: 0x800704C7; events=rnhxNz;", StringComparison.Ordinal), Is.True);
            Assert.That(output.Contains("case readback-close[0]: 0x800704C7; events=rnhvpdgaVLBPTqRScopviatwDXMUCfsexNz;", StringComparison.Ordinal), Is.True);
            Assert.That(output.Contains("case readback-quit[0]: 0x800704C7; events=rnhvpdgaVLBPTqRScopviatwDXMUCfsexNz;", StringComparison.Ordinal), Is.True);
            Assert.That(output.Contains("close=1; consumed-quit=0; posted-quit=1", StringComparison.Ordinal), Is.True);
            Assert.That(output.Contains("close=0; consumed-quit=1; posted-quit=1", StringComparison.Ordinal), Is.True);
            Assert.That(output.Contains("5 close/quit cases verified: unfiltered pump, no quit translation/dispatch, "
                + "deferred destruction and cleanup quit", StringComparison.Ordinal), Is.True);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [InteractiveWindowTest]
    [Category("InteractiveWindow")]
    public async Task Dx11WarpPresentationPresentsRealBackbufferAndChecksCancellationAndCleanupAsync()
    {
        Assert.That(OperatingSystem.IsWindows() && RuntimeInformation.ProcessArchitecture == Architecture.X64, Is.True);
        var root = FindRepositoryRoot();
        var environment = await FindNativeFixtureEnvironmentAsync(root).ConfigureAwait(false);
        var directory = Directory.CreateTempSubdirectory("terrum-dx11-warp-presentation-").FullName;
        var native = Path.Combine(root, "tests", "native");
        var comspec = Environment.GetEnvironmentVariable("ComSpec")
            ?? throw new InvalidOperationException("ComSpec is unset.");
        try
        {
            var importsPath = Path.Combine(directory, "presentation-imports.txt");
            var exportsPath = Path.Combine(directory, "presentation-exports.txt");
            _ = await RunAsync(new ProcessStartInfo(comspec) {
                Arguments = $"/d /s /c \"call \"{environment}\" >nul && cl /nologo /W4 /WX /wd4201 /O2 /MT /LD /TC /Fo\"{directory}\\bridge.obj\" /Fe\"{directory}\\{NativeDx11WarpPresentationContract.DllName}\" \"{native}\\native-dx11-warp-presentation.c\" /link /incremental:no d3d11.lib dxguid.lib user32.lib dwmapi.lib && cl /nologo /W4 /WX /wd4201 /O2 /MT /TC /Fo\"{directory}\\harness.obj\" /Fe\"{directory}\\harness.exe\" \"{native}\\native-dx11-warp-presentation-test.c\" /link /incremental:no d3d11.lib dxguid.lib user32.lib dwmapi.lib && cl /nologo /W4 /WX /wd4201 /Od /TC /c /showIncludes /Fo\"{directory}\\sdk.obj\" \"{native}\\dx11-warp-presentation-sdk-probe.c\" && dumpbin /imports \"{directory}\\{NativeDx11WarpPresentationContract.DllName}\" > \"{importsPath}\" && dumpbin /exports \"{directory}\\{NativeDx11WarpPresentationContract.DllName}\" > \"{exportsPath}\"\"",
                WorkingDirectory = directory,
            }, TimeSpan.FromMinutes(2)).ConfigureAwait(false);
            var imports = await File.ReadAllTextAsync(importsPath).ConfigureAwait(false);
            Assert.That(imports.Contains("D3D11CreateDeviceAndSwapChain", StringComparison.Ordinal), Is.True);
            Assert.That(imports.Contains("DwmFlush", StringComparison.Ordinal), Is.True);
            Assert.That(imports.Contains("USER32.dll", StringComparison.OrdinalIgnoreCase), Is.True);
            Assert.That(imports.Contains("D3DCompiler", StringComparison.OrdinalIgnoreCase), Is.False);
            var exports = await File.ReadAllTextAsync(exportsPath).ConfigureAwait(false);
            Assert.That(exports.Contains(NativeDx11WarpPresentationContract.ExportName, StringComparison.Ordinal), Is.True);
            Assert.That(exports.Contains(NativeDx11WarpOutputContract.ExportName, StringComparison.Ordinal), Is.False);
            File.Copy(Path.Combine(root, "samples", "design", "win32-dx11-triangle", "triangle.hlsl"),
                Path.Combine(directory, "triangle.hlsl"));
            var harnessOutput = await RunAsync(new ProcessStartInfo(Path.Combine(directory, "harness.exe")) {
                WorkingDirectory = directory,
            }, TimeSpan.FromSeconds(90)).ConfigureAwait(false);
            Assert.That(harnessOutput.Contains("real pixels: (160,160)=", StringComparison.Ordinal), Is.True);
            Assert.That(harnessOutput.Contains("; (0,0)=(0,0,0,255)", StringComparison.Ordinal), Is.True);
            Assert.That(harnessOutput.Contains("case real-presentation[0]: 0x00000000; events=rnhvpdgaVLBPTqRScopviatwDXMUFWCfsexNz", StringComparison.Ordinal), Is.True);
            Assert.That(harnessOutput.Contains("case occluded[0]: 0x80004005;", StringComparison.Ordinal), Is.True);
            Assert.That(harnessOutput.Contains("case device-removed[0]: 0x887A0020;", StringComparison.Ordinal), Is.True);
            Assert.That(harnessOutput.Contains("111 presentation cases verified: SDK proxies, actual back-buffer pixels, presentation/composition and teardown", StringComparison.Ordinal), Is.True);

            var destroyFailure = await RunAsync(new ProcessStartInfo(Path.Combine(directory, "harness.exe")) {
                Arguments = "destroy-failure",
                WorkingDirectory = directory,
            }, TimeSpan.FromSeconds(90)).ConfigureAwait(false);
            Assert.That(destroyFailure.Contains("case destroy-failed[0]: 0x80070005; events=rnhvpdgaVLBPTqRScopviatwDXMUFWCfsexz", StringComparison.Ordinal), Is.True);
            Assert.That(destroyFailure.Contains("destroy-failure state verified: live callback state rejected reentry; harness cleanup succeeded", StringComparison.Ordinal), Is.True);

            var unregisterFailure = await RunAsync(new ProcessStartInfo(Path.Combine(directory, "harness.exe")) {
                Arguments = "unregister-failure",
                WorkingDirectory = directory,
            }, TimeSpan.FromSeconds(90)).ConfigureAwait(false);
            Assert.That(unregisterFailure.Contains("case unregister-failed[0]: 0x80070005; events=rnhvpdgaVLBPTqRScopviatwDXMUFWCfsexNz", StringComparison.Ordinal), Is.True);
            Assert.That(unregisterFailure.Contains("unregister-failure state verified: registered class rejected reentry; harness cleanup succeeded", StringComparison.Ordinal), Is.True);

            var working = Directory.CreateDirectory(Path.Combine(directory, "unrelated-working-directory")).FullName;
            await File.WriteAllTextAsync(Path.Combine(working, "triangle.hlsl"), "not HLSL").ConfigureAwait(false);
            var shaderPath = Path.Combine(directory, "triangle.hlsl");
            var shader = await File.ReadAllTextAsync(shaderPath).ConfigureAwait(false);
            foreach (var policy in new[] { BoundRuntimePolicy.FailFast, BoundRuntimePolicy.CheckedExceptions })
            {
                var runtimePolicy = policy == BoundRuntimePolicy.FailFast
                    ? NativeRuntimePolicy.FailFast : NativeRuntimePolicy.CheckedExceptions;
                var __nunitAssertValue7 = CompileWarpPresentation(policy: policy).Program;
                Assert.That(__nunitAssertValue7, Is.TypeOf<BoundProgram>());
                var program = ((BoundProgram)__nunitAssertValue7);
                var executable = Path.Combine(directory, $"warp-presentation-{policy}.exe");
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
                Assert.That(mismatch.Error.Contains("pixel output failed", StringComparison.Ordinal), Is.True);

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
