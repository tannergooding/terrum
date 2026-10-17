// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Immutable;
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
    private const string ShaderLayoutBridgeDeclaration = """
        [Terrum.Interop.NativeImport("test.application.dx11-shader-layout.CompileTriangle")]
        [NoThrow] extern int32 CompileTriangleInputLayout();
        """;

    private static NativeImportCatalog CreateShaderLayoutBridgeCatalog() =>
        NativeImportCatalog.CreateForTesting(NativeDx11ShaderLayoutBridgeContract.CatalogIdentity,
            [NativeDx11ShaderLayoutBridgeContract.Import]);

    private static Compilation CompileShaderLayoutBridge(
        string body = "return CompileTriangleInputLayout();",
        string declaration = ShaderLayoutBridgeDeclaration, NativeImportCatalog? catalog = null,
        BoundRuntimePolicy policy = BoundRuntimePolicy.FailFast) =>
        Compilation.CreateWithNativeImports([SyntaxTree.Parse(declaration + "\nint32 Main() {" + body + "}")],
            catalog ?? CreateShaderLayoutBridgeCatalog(), policy);

    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
    public void Dx11ShaderLayoutBridgeUsesOnlyExistingClosedScalarImportPath(BoundRuntimePolicy policy)
    {
        var compilation = CompileShaderLayoutBridge(policy: policy);
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
        Assert.That(image.AsSpan().IndexOf("NativeFfiCompileTriangleInputLayout\0"u8) >= 0, Is.True);
        foreach (var forbidden in new[] {
            "D3DCompileFromFile", "D3DCompiler_47", "LoadLibrary", "GetProcAddress",
            "NativeFfiInputLayoutOwnerSignature", "NativeFfiAcquireBlob",
        })
        {
            Assert.That(image.AsSpan().IndexOf(System.Text.Encoding.ASCII.GetBytes(forbidden)) < 0, Is.True);
        }
        Assert.That(CompileShaderLayoutBridge(catalog: NativeImportCatalog.WindowsX64).Program, Is.Null);
        Assert.That(CompileShaderLayoutBridge(catalog: CreateShaderCompileCatalog()).Program, Is.Null);
        _ = Assert.Throws<ArgumentException>(() => NativeEmitter.EmitWithNativeImports(program, runtimePolicy));
        _ = Assert.Throws<NotSupportedException>(() => NativeJit.Execute(program, runtimePolicy));
        _ = Assert.Throws<NotSupportedException>(() => NativeEmitter.EmitLibrary(
            program with { EntryPoint = -1 }, ["Main"], runtimePolicy));
        _ = Assert.Throws<NotSupportedException>(() => ModuleCompiler.CreateProducer(
            program with { EntryPoint = -1 }, ["Main"]));
        _ = Assert.Throws<NotSupportedException>(() => TrRmExecutableCompiler.Emit(
            program with { EntryPoint = -1 }, "shader-layout"));
    }

    [TestCase("return CompileTriangleInputLayout(0);")]
    [TestCase("return CompileTriangleInputLayout(utf16z(\"triangle.hlsl\"));")]
    [TestCase("return CompileTriangleInputLayout(utf8z(\"VertexMain\"), utf8z(\"vs_5_0\"));")]
    [TestCase("mut uint64 blob = 0; return CompileTriangleInputLayout(out blob);")]
    [TestCase("return CompileTriangleInputLayout().GetBufferPointer();")]
    [TestCase("return CompileTriangleInputLayout().GetBufferSize();")]
    [TestCase("return CompileTriangleInputLayout().Release();")]
    [TestCase("return LoadLibraryExW(utf16z(\"D3DCompiler_47.dll\"), 0, 2048);")]
    [TestCase("return GetProcAddress(0, utf8z(\"D3DCompileFromFile\"));")]
    public void Dx11ShaderLayoutBridgeRejectsSourceConfigurationAndNativeResourceAccess(string body)
    {
        var compilation = CompileShaderLayoutBridge(body);
        Assert.That(compilation.Program, Is.Null);
        Assert.That(compilation.Diagnostics, Is.Not.Empty);
    }

    [TestCase("uint64", "")]
    [TestCase("int32", "uint64 pointer")]
    [TestCase("int32", "out uint64 code")]
    [TestCase("int32", "ptr<char16> path")]
    public void Dx11ShaderLayoutBridgeRejectsChangedSourceSignatures(string result, string parameters)
    {
        var compilation = CompileShaderLayoutBridge("return 0;", $$"""
            [Terrum.Interop.NativeImport("{{NativeDx11ShaderLayoutBridgeContract.ImportKey}}")]
            [NoThrow] extern {{result}} CompileTriangleInputLayout({{parameters}});
            """);
        Assert.That(compilation.Program, Is.Null);
    }

    [Test]
    public void Dx11ShaderLayoutBridgeSealsCatalogAndRejectsForgedExternalSignatures()
    {
        var original = NativeDx11ShaderLayoutBridgeContract.Import;
        foreach (var invalid in new[] {
            original with { Key = "test.alias" },
            original with { DllName = original.DllName.ToUpperInvariant() },
            original with { ExportName = "D3DCompileFromFile" },
            original with { ReturnType = PrimitiveType.UInt64 },
            original with { ParameterTypes = [PrimitiveType.UInt64] },
            original with { ParameterTypes = default },
            original with { ParameterContracts = [NativeImportParameterContract.Value] },
            original with { ResultContract = NativeImportResultContract.FixtureOwnedBlob },
            original with { IsNoThrow = false },
            original with { RequiresUnsafeCaller = true },
            original with { DoesNotReturn = true },
            original with { DeploymentPolicy = NativeImportDeploymentPolicy.System32 },
        })
        {
            _ = Assert.Throws<ArgumentException>(() => NativeImportCatalog.CreateForTesting(
                NativeDx11ShaderLayoutBridgeContract.CatalogIdentity, [invalid]));
        }
        _ = Assert.Throws<ArgumentException>(() => new NativeImportCatalog(
            NativeDx11ShaderLayoutBridgeContract.CatalogIdentity, [original]));
        _ = Assert.Throws<ArgumentException>(() => NativeImportCatalog.CreateForTesting("test.other", [original]));
        _ = Assert.Throws<ArgumentException>(() => NativeImportCatalog.CreateForTesting(
            NativeDx11ShaderLayoutBridgeContract.CatalogIdentity,
            [original, original with { Key = "test.extra", DllName = "other.dll" }]));
        var __nunitAssertValue5 = CompileShaderLayoutBridge().Program;
        Assert.That(__nunitAssertValue5, Is.TypeOf<BoundProgram>());
        var program = ((BoundProgram)__nunitAssertValue5);
        var __nunitAssertValue6 = global::System.Linq.Enumerable.ToArray(global::System.Linq.Enumerable.Where(program.Functions, static function => function.IsExternal));
        Assert.That(__nunitAssertValue6, Has.Exactly(1).Items);
        var external = __nunitAssertValue6[0];
        foreach (var forged in new[] {
            program with { NativeImportCatalog = null },
            program with { NativeImportCatalogId = "test.other" },
            program with { Functions = program.Functions.Replace(external, external with { ReturnType = PrimitiveType.UInt64 }) },
            program with { Functions = program.Functions.Replace(external, external with { ParameterCount = 1 }) },
            program with { Functions = program.Functions.Replace(external, external with { IsNoThrow = false }) },
        })
        {
            _ = Assert.Throws<ArgumentException>(() => SemanticVerifier.Verify(forged));
            _ = Assert.Throws<ArgumentException>(() =>
                NativeEmitter.EmitWithNativeImportsForTesting(forged, NativeRuntimePolicy.FailFast));
        }
    }

    [Test]
    public async Task Dx11ShaderLayoutBridgeExecutesRealSampleOnWarpAndProvesFailureCleanupAndSystemLoadingAsync()
    {
        Assert.That(OperatingSystem.IsWindows() && RuntimeInformation.ProcessArchitecture == Architecture.X64, Is.True);
        var root = FindRepositoryRoot();
        var environment = await FindNativeFixtureEnvironmentAsync(root).ConfigureAwait(false);
        var directory = TestArtifactPaths.CreateDirectory("terrum-real-shader-layout-");
        var native = Path.Combine(root, "tests", "native");
        var comspec = Environment.GetEnvironmentVariable("ComSpec")
            ?? throw new InvalidOperationException("ComSpec is unset.");
        try
        {
            var importsPath = Path.Combine(directory, "bridge-imports.txt");
            _ = await RunAsync(new ProcessStartInfo(comspec) {
                Arguments = $"/d /s /c \"call \"{environment}\" >nul && cl /nologo /W4 /WX /wd4201 /O2 /MT /LD /TC /Fo\"{directory}\\bridge.obj\" /Fe\"{directory}\\{NativeDx11ShaderLayoutBridgeContract.DllName}\" \"{native}\\native-dx11-shader-layout-bridge.c\" /link /incremental:no d3d11.lib && cl /nologo /W4 /WX /wd4201 /O2 /MT /TC /Fo\"{directory}\\harness.obj\" /Fe\"{directory}\\harness.exe\" \"{native}\\native-dx11-shader-layout-bridge-test.c\" /link /incremental:no d3d11.lib && cl /nologo /W4 /WX /wd4201 /O2 /MT /LD /TC /Fo\"{directory}\\shadow.obj\" /Fe\"{directory}\\D3DCompiler_47.dll\" \"{native}\\native-dx11-compiler-shadow.c\" /link /incremental:no && dumpbin /imports \"{directory}\\{NativeDx11ShaderLayoutBridgeContract.DllName}\" > \"{importsPath}\"\"",
                WorkingDirectory = directory,
            }, TimeSpan.FromMinutes(2)).ConfigureAwait(false);
            var imports = await File.ReadAllTextAsync(importsPath).ConfigureAwait(false);
            Assert.That(imports.Contains("d3d11.dll", StringComparison.OrdinalIgnoreCase), Is.True);
            Assert.That(imports.Contains("D3DCompiler", StringComparison.OrdinalIgnoreCase), Is.False);
            Assert.That(imports.Contains("LoadLibraryExW", StringComparison.Ordinal), Is.True);
            Assert.That(imports.Contains("GetProcAddress", StringComparison.Ordinal), Is.True);
            var shader = Path.Combine(directory, "triangle.hlsl");
            File.Copy(Path.Combine(root, "samples", "design", "win32-dx11-triangle", "triangle.hlsl"), shader);
            var harness = Path.Combine(directory, "harness.exe");
            var harnessOutput = await RunAsync(new ProcessStartInfo(harness) {
                WorkingDirectory = directory,
            }, TimeSpan.FromSeconds(90)).ConfigureAwait(false);
            Assert.That(harnessOutput.Contains("11 lifetime/failure cases passed", StringComparison.Ordinal), Is.True);
            Assert.That(harnessOutput.Contains("case 0: 0x00000000; cleanup=LDCU", StringComparison.Ordinal), Is.True);
            Assert.That(harnessOutput.Contains("case 3: 0x80004005; cleanup=ECU", StringComparison.Ordinal), Is.True);
            Assert.That(harnessOutput.Contains("case 4: 0x80004005; cleanup=ECU", StringComparison.Ordinal), Is.True);
            var shadowOutput = await RunAsync(new ProcessStartInfo(harness) {
                Arguments = "preloaded-shadow",
                WorkingDirectory = directory,
            }, TimeSpan.FromSeconds(30)).ConfigureAwait(false);
            Assert.That(shadowOutput.Contains("rejected before export resolution", StringComparison.Ordinal), Is.True);

            var executable = Path.Combine(directory, "shader-layout.exe");
            var __nunitAssertValue7 = CompileShaderLayoutBridge().Program;
            Assert.That(__nunitAssertValue7, Is.TypeOf<BoundProgram>());
            var program = ((BoundProgram)__nunitAssertValue7);
            await File.WriteAllBytesAsync(executable, NativeEmitter.EmitWithNativeImportsForTesting(
                program, NativeRuntimePolicy.FailFast)).ConfigureAwait(false);
            var working = Directory.CreateDirectory(Path.Combine(directory, "unrelated-working-directory")).FullName;
            await File.WriteAllTextAsync(Path.Combine(working, "triangle.hlsl"), "not HLSL").ConfigureAwait(false);
            File.Copy(Path.Combine(directory, "D3DCompiler_47.dll"), Path.Combine(working, "D3DCompiler_47.dll"));
            var success = await RunShaderLayoutBridgeAsync(executable, working).ConfigureAwait(false);
            Assert.That(success.ExitCode, Is.EqualTo(0));
            Assert.That(success.Error, Is.Empty);
            var __nunitAssertValue8 = CompileShaderLayoutBridge(
                            policy: BoundRuntimePolicy.CheckedExceptions).Program;
            Assert.That(__nunitAssertValue8, Is.TypeOf<BoundProgram>());
            var checkedProgram = ((BoundProgram)__nunitAssertValue8);
            var checkedPath = Path.Combine(directory, "shader-layout-checked.exe");
            await File.WriteAllBytesAsync(checkedPath, NativeEmitter.EmitWithNativeImportsForTesting(
                checkedProgram, NativeRuntimePolicy.CheckedExceptions)).ConfigureAwait(false);
            var checkedSuccess = await RunShaderLayoutBridgeAsync(checkedPath, working).ConfigureAwait(false);
            Assert.That(checkedSuccess.ExitCode, Is.EqualTo(0));
            Assert.That(checkedSuccess.Error, Is.Empty);
            var __nunitAssertValue9 = CompileShaderLayoutBridge("""
                mut int32 attempt = 0;
                while (attempt < 4) {
                    int32 result = CompileTriangleInputLayout();
                    if (result != 0) { return result; }
                    attempt++;
                }
                return 0;
                """).Program;
            Assert.That(__nunitAssertValue9, Is.TypeOf<BoundProgram>());
            var repeat = ((BoundProgram)__nunitAssertValue9);
            var repeatedPath = Path.Combine(directory, "shader-layout-repeat.exe");
            await File.WriteAllBytesAsync(repeatedPath, NativeEmitter.EmitWithNativeImportsForTesting(
                repeat, NativeRuntimePolicy.FailFast)).ConfigureAwait(false);
            var repeated = await RunShaderLayoutBridgeAsync(repeatedPath, working).ConfigureAwait(false);
            Assert.That(repeated.ExitCode, Is.EqualTo(0));
            Assert.That(repeated.Error, Is.Empty);

            await File.WriteAllTextAsync(shader, "float4 VertexMain( { invalid shader").ConfigureAwait(false);
            var badShader = await RunShaderLayoutBridgeAsync(executable, working).ConfigureAwait(false);
            Assert.That(badShader.ExitCode < 0, Is.True, badShader.Error);
            Assert.That(badShader.Error.Contains("compiler diagnostics:", StringComparison.Ordinal), Is.True);
            Assert.That(badShader.Error.Contains("D3DCompileFromFile failed", StringComparison.Ordinal), Is.True);
            Assert.That(badShader.Error.Contains("APP_LOCAL_COMPILER_EXECUTED", StringComparison.Ordinal), Is.False);
            await File.WriteAllTextAsync(shader,
                "float4 VertexMain(float3 color : COLOR) : SV_POSITION { return float4(color, 1); }")
                .ConfigureAwait(false);
            var badLayout = await RunShaderLayoutBridgeAsync(executable, working).ConfigureAwait(false);
            Assert.That(badLayout.ExitCode, Is.EqualTo(unchecked((int)0x80070057)));
            Assert.That(badLayout.Error.Contains("CreateInputLayout failed", StringComparison.Ordinal), Is.True);
            File.Delete(shader);
            var missing = await RunShaderLayoutBridgeAsync(executable, working).ConfigureAwait(false);
            Assert.That(missing.ExitCode < 0, Is.True, missing.Error);
            Assert.That(missing.Error.Contains("D3DCompileFromFile failed", StringComparison.Ordinal), Is.True);
            Assert.That(missing.Error.Contains("APP_LOCAL_COMPILER_EXECUTED", StringComparison.Ordinal), Is.False);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static async Task<(int ExitCode, string Output, string Error)> RunShaderLayoutBridgeAsync(
        string executable, string workingDirectory)
    {
        using var process = Process.Start(new ProcessStartInfo(executable) {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        }) ?? throw new InvalidOperationException("Could not start the real shader/layout bridge.");
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
