// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
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
    private static NativeImportCatalog CreateShaderCompileCatalog() =>
        NativeImportCatalog.CreateForTesting(NativeShaderCompileFixtureContract.CatalogIdentity,
            NativeShaderCompileFixtureContract.Imports);

    private static Compilation CompileShaderFixture(string body = "return shadercompilefixture(0);",
        string declarations = "", NativeImportCatalog? catalog = null,
        BoundRuntimePolicy policy = BoundRuntimePolicy.FailFast) =>
        Compilation.CreateWithNativeImports(
            [SyntaxTree.Parse(declarations + "\nint32 Main() {" + body + "}")],
            catalog ?? CreateShaderCompileCatalog(), policy);

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    [TestCase(4)]
    public void ShaderCompileFixtureBindsOnlyScalarResultAndPrivateAbiAndLifetime(int mode)
    {
        var compilation = CompileShaderFixture($"return shadercompilefixture({mode});");
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var __nunitAssertValue0 = compilation.Program;
        Assert.That(__nunitAssertValue0, Is.TypeOf<BoundProgram>());
        var program = ((BoundProgram)__nunitAssertValue0);
        var main = program.Functions[program.EntryPoint];
        var __nunitAssertValue1 = global::System.Linq.Enumerable.ToArray(main.Body.Statements);
        Assert.That(__nunitAssertValue1, Has.Exactly(1).Items);
        var __nunitAssertValue2 = __nunitAssertValue1[0];
        Assert.That(__nunitAssertValue2, Is.TypeOf<BoundReturn>());
        var __nunitAssertValue3 = (((BoundReturn)__nunitAssertValue2)).Value;
        Assert.That(__nunitAssertValue3, Is.TypeOf<BoundNativeShaderCompileFixture>());
        var shader = ((BoundNativeShaderCompileFixture)__nunitAssertValue3);
        Assert.That(main.Variables, Is.Empty);
        Assert.That(program.Functions, Has.None.Matches(static function => function.IsExternal));
        Assert.That(NativeShaderCompileFixtureContract.MatchesGate(shader, program), Is.True);
        Assert.That(shader.Type, Is.EqualTo(PrimitiveType.Int32));
        Assert.That(shader.Mode, Is.EqualTo(mode));
        Assert.That(shader.PointerGetter.Slot, Is.EqualTo(3));
        Assert.That(shader.SizeGetter.Slot, Is.EqualTo(4));
        Assert.That(shader.Release.Slot, Is.EqualTo(2));
        Assert.That(shader.Release.ReturnType, Is.EqualTo(NativeShaderBlobAbiType.UInt32));
        Assert.That(shader.Release.Effect, Is.EqualTo(NativeShaderBlobMethodEffect.ConsumeOneOutputReference));
        Assert.That(CreateShaderCompileCatalog().SupportsBlobGetterFixture, Is.False);
        var image = NativeEmitter.EmitWithNativeImportsForTesting(program, NativeRuntimePolicy.FailFast);
        foreach (var definition in NativeShaderCompileFixtureContract.Imports)
        {
            Assert.That(image.AsSpan().IndexOf(System.Text.Encoding.ASCII.GetBytes(definition.ExportName + "\0")) >= 0, Is.True);
        }
        foreach (var slot in new byte[] { 0x10, 0x18, 0x20 })
        {
            Assert.That(CountShaderSequence(image, [0x48, 0x89, 0xC1, 0x48, 0x8B, 0x01, 0xFF, 0x50, slot]), Is.EqualTo(2));
        }
        Assert.That(image.AsSpan().IndexOf("D3DCompiler"u8) < 0, Is.True);
        Assert.That(image.AsSpan().IndexOf("D3DCompileFromFile\0"u8) < 0, Is.True);
        Assert.That(image.AsSpan().IndexOf("NativeFfiAcquireBlob"u8) < 0, Is.True);
    }

    private static int CountShaderSequence(byte[] image, byte[] sequence)
    {
        var count = 0;
        var remaining = image.AsSpan();
        while (remaining.IndexOf(sequence) is var index && index >= 0)
        {
            count++;
            remaining = remaining[(index + sequence.Length)..];
        }
        return count;
    }

    [TestCase("return shadercompilefixture();")]
    [TestCase("return shadercompilefixture(5);")]
    [TestCase("return shadercompilefixture(-1);")]
    [TestCase("return shadercompilefixture(true);")]
    [TestCase("return shadercompilefixture(mode: 0);")]
    [TestCase("int32 mode = 0; return shadercompilefixture(mode);")]
    [TestCase("return shadercompilefixture(0 + 0);")]
    [TestCase("return shadercompilefixture((0));")]
    [TestCase("return shadercompilefixture(0, 0);")]
    [TestCase("return shadercompilefixture(utf16z(\"fixture.hlsl\"), null, null, utf8z(\"main\"), utf8z(\"vs_5_0\"), 0, 0, 0, 0);")]
    [TestCase("mut uint64 code = 0; mut uint64 diagnostics = 0; return shadercompilefixture(0, &code, &diagnostics);")]
    [TestCase("return shadercompilefixture(0, &Main);")]
    [TestCase("return shadercompilefixture(0).GetBufferPointer();")]
    [TestCase("return shadercompilefixture(0).GetBufferSize();")]
    [TestCase("return shadercompilefixture(0).Release();")]
    [TestCase("return shadercompilefixture(0).QueryInterface();")]
    [TestCase("ptr<uint32> code = shadercompilefixture(0); return 0;")]
    [TestCase("Terrum.Interop.Dx11.BlobFixtureObject code = shadercompilefixture(0); return 0;")]
    [TestCase("return blobgetters(shadercompilefixture(0));")]
    [TestCase("return shadercompilefixture(0, 2, 3, 4);")]
    public void ShaderCompileFixtureRejectsArbitraryArgumentsCellsObjectsDispatchAndEscape(string body)
    {
        var compilation = CompileShaderFixture(body);
        Assert.That(compilation.Program, Is.Null);
        Assert.That(compilation.Diagnostics, Is.Not.Empty);
    }

    [TestCase("ptr<uint32> Forward() { return shadercompilefixture(0); }")]
    [TestCase("namespace Terrum.Interop.Dx11 { struct ShaderBlobOutputCell { uint64 Address; } }")]
    [TestCase("[ConstEval] int32 Forward() { return shadercompilefixture(0); }")]
    public void ShaderCompileFixtureDoesNotProvideRawOutputTypesOrConstEvalEffects(string declaration)
    {
        var body = declaration.Contains("ShaderBlobOutputCell", StringComparison.Ordinal)
            ? "Terrum.Interop.Dx11.ShaderBlobOutputCell code = shadercompilefixture(0); return 0;"
            : "return 0;";
        var compilation = CompileShaderFixture(body, declaration);
        Assert.That(compilation.Program, Is.Null);
        Assert.That(compilation.Diagnostics, Is.Not.Empty);
    }

    [TestCase("NativeFfiShaderMode", "void", "uint32 mode")]
    [TestCase("NativeFfiCompileFromFile", "int32",
        "ptr<char16> file, uint64 defines, uint64 include, ptr<char8> entry, ptr<char8> target, uint32 flags1, uint32 flags2, uint64 code, uint64 diagnostics")]
    [TestCase("NativeFfiObserveShaderOutputs", "int32",
        "int32 result, uint64 code, uint64 pointer, uint64 size, uint64 diagnostics, uint64 diagnosticsPointer, uint64 diagnosticsSize")]
    [TestCase("NativeFfiFinishShader", "int32", "int32 observation")]
    public void ShaderCompileFixturePrivateImportsCannotBeDeclaredOrCalledFromSource(
        string export, string result, string parameters)
    {
        var __nunitAssertValue4 = global::System.Linq.Enumerable.ToArray(global::System.Linq.Enumerable.Where(NativeShaderCompileFixtureContract.Imports, definition => definition.ExportName == export));
        Assert.That(__nunitAssertValue4, Has.Exactly(1).Items);
        var definition = __nunitAssertValue4[0];
        var compilation = CompileShaderFixture(declarations: $$"""
            [Terrum.Interop.NativeImport("{{definition.Key}}")]
            [NoThrow] extern {{result}} PrivateImport({{parameters}});
            """);
        Assert.That(compilation.Program, Is.Null);
        Assert.That(compilation.Diagnostics, Has.Some.Matches(static diagnostic => diagnostic.Code == "TRM3030"));
    }

    [Test]
    public void ShaderCompileFixtureCatalogRejectsAlteredIdentitySignaturesEffectsAndOwnership()
    {
        var originals = NativeShaderCompileFixtureContract.Imports;
        foreach (var definition in originals)
        {
            foreach (var invalid in new[] {
                definition with { Key = "test.other" },
                definition with { DllName = "other.dll" },
                definition with { DllName = "NATIVE-SHADER-COMPILE-PROBE.DLL" },
                definition with { DllName = "D3DCompiler_47.dll", ExportName = "D3DCompileFromFile" },
                definition with { ExportName = "OtherExport" },
                definition with { ReturnType = PrimitiveType.Int64 },
                definition with { ParameterTypes = default },
                definition with { ParameterTypes = [PrimitiveType.UInt64] },
                definition with { ParameterContracts = default },
                definition with { ParameterContracts = [NativeImportParameterContract.CompilerInternalShaderFixture] },
                definition with { ResultContract = NativeImportResultContract.FixtureOwnedBlob },
                definition with { ResultContract = (NativeImportResultContract)99 },
                definition with { IsNoThrow = false },
                definition with { RequiresUnsafeCaller = true },
                definition with { DoesNotReturn = true },
                definition with { DeploymentPolicy = NativeImportDeploymentPolicy.System32 },
            })
            {
                _ = Assert.Throws<ArgumentException>(() => NativeImportCatalog.CreateForTesting(
                    NativeShaderCompileFixtureContract.CatalogIdentity, originals.Replace(definition, invalid)));
            }
            _ = Assert.Throws<ArgumentException>(() => NativeImportCatalog.CreateForTesting(
                NativeShaderCompileFixtureContract.CatalogIdentity, originals.Remove(definition)));
            foreach (var dll in new[] { definition.DllName, definition.DllName.ToUpperInvariant() })
            {
                var alias = new NativeImportDefinition("test.alias", dll, definition.ExportName,
                    [], PrimitiveType.Int32, true, true, false, NativeImportDeploymentPolicy.ApplicationDirectory);
                _ = Assert.Throws<ArgumentException>(() => NativeImportCatalog.CreateForTesting(
                    NativeShaderCompileFixtureContract.CatalogIdentity, [alias]));
            }
        }
        var compiler = originals[1];
        var parameters = compiler.ParameterTypes;
        for (var index = 0; index < parameters.Length; index++)
        {
            var invalid = compiler with {
                ParameterTypes = parameters.SetItem(index, parameters[index] == PrimitiveType.UInt64
                    ? PrimitiveType.UInt32 : PrimitiveType.UInt64),
            };
            _ = Assert.Throws<ArgumentException>(() => NativeImportCatalog.CreateForTesting(
                NativeShaderCompileFixtureContract.CatalogIdentity, originals.Replace(compiler, invalid)));
        }
        _ = Assert.Throws<ArgumentException>(() => NativeImportCatalog.CreateForTesting(
            NativeShaderCompileFixtureContract.CatalogIdentity, originals.Replace(compiler, compiler with {
                ResultContract = NativeImportResultContract.Value,
            })));
        _ = Assert.Throws<ArgumentException>(() => NativeImportCatalog.CreateForTesting("test.other", originals));
        _ = Assert.Throws<ArgumentException>(() => new NativeImportCatalog(
            NativeShaderCompileFixtureContract.CatalogIdentity, originals));
    }

    [TestCase("negative-mode")]
    [TestCase("unknown-mode")]
    [TestCase("file-index")]
    [TestCase("entry-index")]
    [TestCase("target-index")]
    [TestCase("file-value")]
    [TestCase("entry-value")]
    [TestCase("target-value")]
    [TestCase("file-pool")]
    [TestCase("utf8-pool")]
    [TestCase("import-keys-default")]
    [TestCase("import-key")]
    [TestCase("import-key-missing")]
    [TestCase("import-key-production")]
    [TestCase("pointer-slot")]
    [TestCase("size-slot")]
    [TestCase("release-query-slot")]
    [TestCase("release-addref-slot")]
    [TestCase("release-getter-slot")]
    [TestCase("release-outside-slot")]
    [TestCase("pointer-parameters")]
    [TestCase("size-parameters")]
    [TestCase("release-parameters-default")]
    [TestCase("release-parameters-missing")]
    [TestCase("release-parameters-extra")]
    [TestCase("release-parameters-raw")]
    [TestCase("pointer-return")]
    [TestCase("size-return")]
    [TestCase("release-return-wide")]
    [TestCase("release-return-pointer")]
    [TestCase("pointer-effect")]
    [TestCase("size-effect")]
    [TestCase("release-effect")]
    [TestCase("pointer-abi")]
    [TestCase("size-abi")]
    [TestCase("release-abi")]
    [TestCase("raw-return")]
    [TestCase("raw-main-return")]
    [TestCase("cell-escape")]
    [TestCase("catalog-removed")]
    [TestCase("catalog-id")]
    [TestCase("catalog-production")]
    [TestCase("catalog-other-fixture")]
    [TestCase("checked-policy")]
    public void ShaderCompileFixtureMandatoryVerifierAndEmitterRejectForgedContracts(string variant)
    {
        var compilation = CompileShaderFixture();
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var __nunitAssertValue5 = compilation.Program;
        Assert.That(__nunitAssertValue5, Is.TypeOf<BoundProgram>());
        var program = ((BoundProgram)__nunitAssertValue5);
        var main = program.Functions[program.EntryPoint];
        var __nunitAssertValue6 = main.Body.Statements[0];
        Assert.That(__nunitAssertValue6, Is.TypeOf<BoundReturn>());
        var result = ((BoundReturn)__nunitAssertValue6);
        var __nunitAssertValue7 = result.Value;
        Assert.That(__nunitAssertValue7, Is.TypeOf<BoundNativeShaderCompileFixture>());
        var gate = ((BoundNativeShaderCompileFixture)__nunitAssertValue7);
        gate = variant switch {
            "negative-mode" => gate with { Mode = -1 },
            "unknown-mode" => gate with { Mode = 5 },
            "file-index" => gate with { FileLiteral = -1 },
            "entry-index" => gate with { EntryLiteral = int.MaxValue },
            "target-index" => gate with { TargetLiteral = gate.EntryLiteral },
            "import-keys-default" => gate with { ImportKeys = default },
            "import-key" => gate with { ImportKeys = gate.ImportKeys.SetItem(1, "test.other") },
            "import-key-missing" => gate with { ImportKeys = gate.ImportKeys.RemoveAt(3) },
            "import-key-production" => gate with {
                ImportKeys = gate.ImportKeys.SetItem(1,
                "windows-x64.system32.D3DCompiler_47.D3DCompileFromFile")
            },
            "pointer-slot" => gate with { PointerGetter = gate.PointerGetter with { Slot = 2 } },
            "size-slot" => gate with { SizeGetter = gate.SizeGetter with { Slot = 3 } },
            "release-query-slot" => gate with { Release = gate.Release with { Slot = 0 } },
            "release-addref-slot" => gate with { Release = gate.Release with { Slot = 1 } },
            "release-getter-slot" => gate with { Release = gate.Release with { Slot = 4 } },
            "release-outside-slot" => gate with { Release = gate.Release with { Slot = 5 } },
            "pointer-parameters" => gate with { PointerGetter = gate.PointerGetter with { ParameterTypes = [] } },
            "size-parameters" => gate with { SizeGetter = gate.SizeGetter with { ParameterTypes = default } },
            "release-parameters-default" => gate with { Release = gate.Release with { ParameterTypes = default } },
            "release-parameters-missing" => gate with { Release = gate.Release with { ParameterTypes = [] } },
            "release-parameters-extra" => gate with {
                Release = gate.Release with {
                    ParameterTypes = [
                NativeShaderBlobAbiType.OwnedOutputReference, NativeShaderBlobAbiType.UInt64,
            ]
                }
            },
            "release-parameters-raw" => gate with {
                Release = gate.Release with {
                    ParameterTypes = [
                NativeShaderBlobAbiType.UInt64,
            ]
                }
            },
            "pointer-return" => gate with {
                PointerGetter = gate.PointerGetter with {
                    ReturnType = NativeShaderBlobAbiType.UInt64,
                }
            },
            "size-return" => gate with { SizeGetter = gate.SizeGetter with { ReturnType = NativeShaderBlobAbiType.UInt32 } },
            "release-return-wide" => gate with { Release = gate.Release with { ReturnType = NativeShaderBlobAbiType.UInt64 } },
            "release-return-pointer" => gate with {
                Release = gate.Release with {
                    ReturnType = NativeShaderBlobAbiType.BorrowedBytesWhileLive,
                }
            },
            "pointer-effect" => gate with {
                PointerGetter = gate.PointerGetter with {
                    Effect = NativeShaderBlobMethodEffect.ConsumeOneOutputReference,
                }
            },
            "size-effect" => gate with {
                SizeGetter = gate.SizeGetter with {
                    Effect = NativeShaderBlobMethodEffect.ConsumeOneOutputReference,
                }
            },
            "release-effect" => gate with { Release = gate.Release with { Effect = NativeShaderBlobMethodEffect.BorrowWhileLive } },
            "pointer-abi" => gate with { PointerGetter = gate.PointerGetter with { Abi = "x86-stdcall" } },
            "size-abi" => gate with { SizeGetter = gate.SizeGetter with { Abi = "x86-stdcall" } },
            "release-abi" => gate with { Release = gate.Release with { Abi = "x86-stdcall" } },
            "raw-return" => gate with { Type = PrimitiveType.UInt64 },
            _ => gate,
        };
        program = variant switch {
            "file-value" => program with { NativeUtf16ZLiterals = ["other.hlsl"] },
            "entry-value" => program with { NativeUtf8ZLiterals = ["other", "vs_5_0"] },
            "target-value" => program with { NativeUtf8ZLiterals = ["main", "ps_5_0"] },
            "file-pool" => program with { NativeUtf16ZLiterals = [] },
            "utf8-pool" => program with { NativeUtf8ZLiterals = [] },
            "catalog-removed" => program with { NativeImportCatalog = null, NativeImportCatalogId = null },
            "catalog-id" => program with { NativeImportCatalogId = "test.other" },
            "catalog-production" => program with {
                NativeImportCatalog = NativeImportCatalog.WindowsX64,
                NativeImportCatalogId = NativeImportCatalog.WindowsX64.Identity
            },
            "catalog-other-fixture" => program with {
                NativeImportCatalog = CreateBlobCatalog(),
                NativeImportCatalogId = CreateBlobCatalog().Identity
            },
            "checked-policy" => program with { RuntimePolicy = BoundRuntimePolicy.CheckedExceptions },
            _ => program,
        };
        main = main with {
            ReturnType = variant == "raw-main-return" ? PrimitiveType.UInt64 : main.ReturnType,
            Body = main.Body with {
                Statements = [result with {
                Value = variant == "cell-escape" ? new BoundRead(0, PrimitiveType.UInt64, gate.Span) : gate,
            }]
            },
        };
        program = program with { Functions = program.Functions.SetItem(program.EntryPoint, main) };
        _ = Assert.Throws<ArgumentException>(() => SemanticVerifier.VerifyReferences(program));
        _ = Assert.Throws<ArgumentException>(() =>
            NativeEmitter.EmitWithNativeImportsForTesting(program, NativeRuntimePolicy.FailFast));
        if (variant is not ("raw-main-return" or "cell-escape"))
        {
            _ = Assert.Throws<InvalidOperationException>(() => new PeCompiler(program, default, managedNodes: false).Compile());
        }
    }

    [Test]
    public void ShaderCompileFixtureRemainsTestOnlyFailFastPeAot()
    {
        var compilation = CompileShaderFixture();
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var __nunitAssertValue8 = compilation.Program;
        Assert.That(__nunitAssertValue8, Is.TypeOf<BoundProgram>());
        var program = ((BoundProgram)__nunitAssertValue8);
        Assert.That(CompileShaderFixture(catalog: NativeImportCatalog.WindowsX64).Program, Is.Null);
        Assert.That(CompileShaderFixture(catalog: CreateBlobCatalog()).Program, Is.Null);
        Assert.That(CompileShaderFixture(policy: BoundRuntimePolicy.CheckedExceptions).Program, Is.Null);
        _ = Assert.Throws<ArgumentException>(() => NativeEmitter.EmitWithNativeImports(program, NativeRuntimePolicy.FailFast));
        _ = Assert.Throws<NotSupportedException>(() => NativeEmitter.EmitCode(program, NativeRuntimePolicy.FailFast));
        _ = Assert.Throws<NotSupportedException>(() => NativeJit.Execute(program, NativeRuntimePolicy.FailFast));
        _ = Assert.Throws<NotSupportedException>(() => ModuleCompiler.CreateProducer(program with { EntryPoint = -1 }, ["Main"]));
        _ = Assert.Throws<NotSupportedException>(() => NativeModuleCompiler.CreateProducer(
            program with { EntryPoint = -1 }, ["Main"], "shader", NativeRuntimePolicy.FailFast));
        _ = Assert.Throws<NotSupportedException>(() => TrRmExecutableCompiler.Emit(program with { EntryPoint = -1 }, "shader"));
        _ = Assert.Throws<NotSupportedException>(() => NativeEmitter.EmitLibrary(
            program with { EntryPoint = -1 }, ["Main"], NativeRuntimePolicy.FailFast));
        _ = Assert.Throws<NotSupportedException>(() => SemanticVerifier.RejectSourceNativeImports(program with {
            NativeImportCatalog = null,
            NativeImportCatalogId = null,
        }, "JIT"));
    }

    [Test]
    public void ShaderCompileFixtureDoesNotHideOrdinaryOrGenericFunctions()
    {
        foreach (var declaration in new[] {
            "[NoThrow] int32 shadercompilefixture(int32 value) { return value; }",
            "[NoThrow] T shadercompilefixture<T>(T value) where T : [Copy] { return value; }",
        })
        {
            var compilation = CompileShaderFixture("return shadercompilefixture(42);", declaration);
            Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
            var __nunitAssertValue9 = compilation.Program;
            Assert.That(__nunitAssertValue9, Is.TypeOf<BoundProgram>());
            var program = ((BoundProgram)__nunitAssertValue9);
            var __nunitAssertValue10 = program.Functions[program.EntryPoint].Body.Statements[0];
            Assert.That(__nunitAssertValue10, Is.TypeOf<BoundReturn>());
            Assert.That((((BoundReturn)__nunitAssertValue10)).Value, Is.TypeOf<BoundCall>());
        }
    }

    [Test]
    public async Task ShaderCompileFixtureProvesIndependentNineArgumentAbiAndOwnedOutputsAsync()
    {
        Assert.That(OperatingSystem.IsWindows() && RuntimeInformation.ProcessArchitecture == Architecture.X64, Is.True);
        var root = FindRepositoryRoot();
        var environment = await FindNativeFixtureEnvironmentAsync(root).ConfigureAwait(false);
        var directory = Directory.CreateTempSubdirectory("terrum-shader-compile-fixture-").FullName;
        var fixture = Path.Combine(root, "tests", "native", "native-shader-compile-probe.c");
        var sdk = Path.Combine(root, "tests", "native", "dx11-sdk-layout-probe.c");
        var assembly = Path.Combine(directory, "shader-sdk.asm");
        var comspec = Environment.GetEnvironmentVariable("ComSpec")
            ?? throw new InvalidOperationException("ComSpec is unset.");
        try
        {
            _ = await RunAsync(new ProcessStartInfo(comspec) {
                Arguments = $"/d /s /c \"call \"{environment}\" >nul && cl /nologo /W4 /WX /O2 /MT /LD /TC /Fo\"{directory}\\fixture.obj\" /Fe\"{directory}\\{NativeShaderCompileFixtureContract.DllName}\" \"{fixture}\" /link /incremental:no && cl /nologo /W4 /WX /O2 /MT /TC /DNATIVE_SHADER_FIXTURE_SELF_TEST /Fo\"{directory}\\selftest.obj\" /Fe\"{directory}\\selftest.exe\" \"{fixture}\" /link /incremental:no && cl /nologo /W4 /WX /wd4201 /Od /TC /c /FAs /Fa\"{assembly}\" /Fo\"{directory}\\sdk.obj\" \"{sdk}\"\"",
                WorkingDirectory = directory,
            }, TimeSpan.FromMinutes(2)).ConfigureAwait(false);
            _ = await RunAsync(new ProcessStartInfo(Path.Combine(directory, "selftest.exe")) {
                WorkingDirectory = directory,
            }, TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            var listing = await File.ReadAllTextAsync(assembly).ConfigureAwait(false);
            var start = listing.IndexOf("NativeFfiCompileSdkStackProbe PROC", StringComparison.Ordinal);
            Assert.That(start >= 0, Is.True, listing);
            var end = listing.IndexOf("NativeFfiCompileSdkStackProbe ENDP", start, StringComparison.Ordinal);
            Assert.That(end > start, Is.True, listing);
            var call = listing[start..end];
            // Windows x64: four register args, then target/flags/flags/code/errors after the 32-byte home area.
            foreach (var offset in new[] { 32, 56, 64 })
            {
                Assert.Matches($@"mov\s+QWORD PTR \[rsp\+{offset}\], rax", call);
            }
            foreach (var offset in new[] { 40, 48 })
            {
                Assert.Matches($@"mov\s+DWORD PTR \[rsp\+{offset}\], 0", call);
            }

            var compilation = CompileShaderFixture("""
                mut int32 attempt = 0;
                while (attempt < 32) {
                    if (CodeOnly() != 0) { return 1; }
                    if (SuccessDiagnostics() != 0) { return 2; }
                    if (FailureDiagnostics() != 0) { return 3; }
                    if (FailureEmpty() != 0) { return 4; }
                    if (Alias() != 0) { return 5; }
                    if (41 + Alias() != 41) { return 6; }
                    if (CodeOnly() + FailureDiagnostics() != 0) { return 7; }
                    attempt++;
                }
                return 0;
                """, """
                [NoThrow] int32 CodeOnly() { return shadercompilefixture(0); }
                [NoThrow] int32 SuccessDiagnostics() { return shadercompilefixture(1); }
                [NoThrow] int32 FailureDiagnostics() { return shadercompilefixture(2); }
                [NoThrow] int32 FailureEmpty() { return shadercompilefixture(3); }
                [NoThrow] int32 Alias() { return shadercompilefixture(4); }
                """);
            Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
            var path = Path.Combine(directory, "shader-compile-fixture.exe");
            var __nunitAssertValue11 = compilation.Program;
            Assert.That(__nunitAssertValue11, Is.TypeOf<BoundProgram>());
            await File.WriteAllBytesAsync(path, NativeEmitter.EmitWithNativeImportsForTesting(
                            ((BoundProgram)__nunitAssertValue11), NativeRuntimePolicy.FailFast)).ConfigureAwait(false);
            _ = await RunAsync(new ProcessStartInfo(path) {
                WorkingDirectory = directory,
            }, TimeSpan.FromSeconds(10)).ConfigureAwait(false);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
