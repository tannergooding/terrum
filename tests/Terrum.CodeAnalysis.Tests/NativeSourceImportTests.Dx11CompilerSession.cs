// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests;

public sealed partial class NativeSourceImportTests
{
    [TestCase("struct")]
    [TestCase("class")]
    [TestCase("alias")]
    public void DirectDx11CompilerSessionReservesOpaqueOwnerIdentity(string declaration)
    {
        const string name = "ProvisionalCompilerSession";
        var source = "namespace Terrum.Interop.Dx11;\n" + (declaration == "alias"
            ? "alias " + name + " = uint64;" : declaration + " " + name + " { uint64 Raw; }")
            + "\nint32 Main() { return 0; }";
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        Assert.That(compilation.Program, Is.Null);
        Assert.That(compilation.Diagnostics, Has.Some.Matches(static diagnostic => diagnostic.Code == "TRM3003"));
    }

    [Test]
    public void DirectDx11CompilerSessionHasPrivateRuntimeImportsAndExactCompileAbi()
    {
        var windowProgram = CompileDirectWindowDevicePipeline();
        Assert.That(NativeDx11RuntimeSessionContract.TryProjectProgram(windowProgram, out var compilerSessionProgram), Is.True);
        Assert.That(NativeDx11CompilerSessionContract.TryProjectProgram(compilerSessionProgram, out _), Is.True);
        foreach (var (program, hardware) in new[] {
            (windowProgram, false), (CompileDirectHardwarePipeline(), true),
        })
        {
            Assert.That(NativeDx11CompilerSessionContract.TryGetFunctionIndices(program.Functions,
                BoundIntrinsic.Dx11CompilerSessionTryOpen, BoundIntrinsic.Dx11CompilerSessionClose,
                out var compilerOpen, out var compilerClose), Is.True);
            Assert.That(NativeDx11CompilerSessionContract.TryGetFunctionIndices(program.Functions,
                BoundIntrinsic.Dx11RuntimeSessionTryOpen, BoundIntrinsic.Dx11RuntimeSessionClose,
                out var runtimeOpen, out var runtimeClose), Is.True);
            Assert.That(compilerClose, Is.EqualTo(compilerOpen + 1));
            Assert.That(runtimeOpen, Is.EqualTo(compilerClose + 1));
            Assert.That(runtimeClose, Is.EqualTo(runtimeOpen + 1));
            Assert.That(program.Structures[14], Is.EqualTo(NativeDx11CompilerSessionContract.Structure));
            Assert.That(ValueLayout.Alignment(NativeDx11CompilerSessionContract.SessionType,
                program.Structures, program.NativeImportCatalog), Is.EqualTo(8));
            Assert.That(NativeDx11CompilerSessionContract.MatchesCatalog(program.NativeImportCatalog), Is.True);
            var code = DirectWindowDeviceShaderCode(program);
            Assert.That(code.NativeImports, Has.None.Matches(static import =>
                import.DllName.Contains("D3DCompiler", StringComparison.OrdinalIgnoreCase)
                || import.ExportName is "D3DCompileFromFile" or "LoadLibraryW" or "LoadLibraryA"));
            foreach (var import in NativeDx11CompilerSessionContract.Imports)
            {
                Assert.That(code.NativeImports, Has.Some.Matches(item =>
                    item.DllName == "kernel32.dll" && item.ExportName == import.ExportName));
            }
            var __nunitAssertValue0 = global::System.Linq.Enumerable.ToArray(global::System.Linq.Enumerable.Where(code.Functions, item => item.Start == code.SourceFunctionOffsets[12]));
            Assert.That(__nunitAssertValue0, Has.Exactly(1).Items);
            var range = __nunitAssertValue0[0];
            var bytes = code.Code.AsSpan(range.Start, range.End - range.Start).ToArray();
            Assert.That(range.StackSize & 15, Is.EqualTo(8));
            Assert.That(ContainsSequence(bytes, [0xFF, 0xD0]), Is.True);
            foreach (var offset in new byte[] { 32, 40, 48, 56, 64 })
            {
                Assert.That(ContainsSequence(bytes, [0x48, 0x89, 0x84, 0x24, offset, 0, 0, 0]), Is.True);
            }
            var __nunitAssertValue1 = global::System.Linq.Enumerable.ToArray(global::System.Linq.Enumerable.Where(code.Functions, item =>
                            item.Start == code.SourceFunctionOffsets[compilerOpen]));
            Assert.That(__nunitAssertValue1, Has.Exactly(1).Items);
            var loader = __nunitAssertValue1[0];
            Assert.That(loader.StackSize & 15, Is.EqualTo(8));
            Assert.That(loader.StackSize, Is.InRange(1100, 2000));
            Assert.That(ContainsSequence(code.Code.AsSpan(loader.Start, loader.End - loader.Start).ToArray(),
                [0xB8, 0, 8, 0, 0, 0x41, 0x89, 0xC0]), Is.True);
        }
        var legacy = DirectWindowDeviceShaderCode(CompileDirectWindowDeviceShader());
        Assert.That(legacy.NativeImports, Has.Some.Matches(static import => import.ExportName == "D3DCompileFromFile"
            && Path.IsPathFullyQualified(import.DllName)));
        Assert.That(legacy.NativeImports, Has.None.Matches(static import => import.ExportName == "LoadLibraryExW"));
    }

    [TestCase("tryopenshadercompiler(out var compiler)", "!tryopenshadercompiler(out var compiler)")]
    [TestCase("compiler.D3DCompileFromFile", "D3DCompileFromFile")]
    [TestCase("compiler.D3DCompileFromFile", "compiler.GetProcAddress")]
    [TestCase("if (compiler.D3DCompileFromFile", "compiler.Close(); if (compiler.D3DCompileFromFile")]
    [TestCase("context.Draw(3, 0);", "uint64 address = compiler; context.Draw(3, 0);")]
    [TestCase("context.Draw(3, 0);", "tryopenshadercompiler(out var other); context.Draw(3, 0);")]
    public void DirectDx11CompilerSessionRejectsSourceLifetimeAndExportEscapes(string original, string replacement)
    {
        foreach (var source in new[] { DirectWindowDevicePipelineSource, DirectHardwarePipelineSource })
        {
            Assert.That(source.Contains(original, StringComparison.Ordinal), Is.True);
            var compilation = Compilation.CreateWithNativeImports(
                SyntaxTree.Parse(source.Replace(original, replacement, StringComparison.Ordinal)),
                BoundRuntimePolicy.CheckedExceptions);
            Assert.That(compilation.Program, Is.Null);
            Assert.That(compilation.Diagnostics, Is.Not.Empty);
        }
    }

    [Test]
    public void DirectDx11CompilerSessionRejectsBoundSessionAndLifetimeMutations()
    {
        foreach (var (program, hardware) in new[] {
            (CompileDirectWindowDevicePipeline(), false), (CompileDirectHardwarePipeline(), true),
        })
        {
            var forgeryIndex = 0;
            void Reject(BoundProgram forged)
            {
                if (hardware)
                {
                    RejectHardwareForgery(forged);
                }
                else
                {
                    RejectPipelineForgery(forged, $"Compiler session forgery {forgeryIndex}.");
                }
                forgeryIndex++;
            }
            var runIndex = NativeDx11DirectPipelineContract.RunIndex;
            var run = program.Functions[runIndex];
            Assert.That(NativeDx11CompilerSessionContract.TryGetFunctionIndices(program.Functions,
                BoundIntrinsic.Dx11CompilerSessionTryOpen, BoundIntrinsic.Dx11CompilerSessionClose,
                out var compilerOpen, out var compilerClose), Is.True);
            var session = FindCompilerSessionScope(run.Body, compilerClose);
            var __nunitAssertValue2 = session.Statements[1];
            Assert.That(__nunitAssertValue2, Is.TypeOf<BoundTryFinally>());
            var lifetime = ((BoundTryFinally)__nunitAssertValue2);
            var __nunitAssertValue3 = global::System.Linq.Enumerable.ToArray(lifetime.Finally.Statements);
            Assert.That(__nunitAssertValue3, Has.Exactly(1).Items);
            var __nunitAssertValue4 = __nunitAssertValue3[0];
            Assert.That(__nunitAssertValue4, Is.TypeOf<BoundDrop>());
            var drop = ((BoundDrop)__nunitAssertValue4);
            foreach (var replacement in new[] {
                session with { Statements = session.Statements.RemoveAt(0) },
                session with { IsManagedLexicalScope = false },
                session with { RetiredManagedLocals = default },
                session with { Statements = session.Statements.SetItem(1, lifetime with {
                    Finally = lifetime.Finally with { Statements = [] },
                }) },
                session with { Statements = session.Statements.SetItem(1, lifetime with {
                    Finally = lifetime.Finally with { Statements = [drop with { CleanupFunction = 13 }] },
                }) },
                session with { Statements = session.Statements.SetItem(1, lifetime with {
                    Body = lifetime.Body with { Statements = lifetime.Body.Statements.Insert(0, drop) },
                }) },
            })
            {
                Reject(program with {
                    Functions = program.Functions.SetItem(runIndex, run with {
                        Body = NativeDx11StageProjection.ReplaceBlock(run.Body, session, replacement),
                    })
                });
            }
            foreach (var index in new[] { 12, compilerOpen, compilerClose })
            {
                Assert.That(index < program.Functions.Length, Is.True, $"Function index {index} must exist in a {program.Functions.Length}-function program.");
                var function = program.Functions[index];
                foreach (var replacement in new[] {
                    function with { IsNoThrow = false },
                    function with { Intrinsic = BoundIntrinsic.None },
                    function with { ParameterCount = function.ParameterCount + 1 },
                    function with { Body = new BoundBlock([new BoundReturn(null, default)], default) },
                })
                {
                    Reject(program with { Functions = program.Functions.SetItem(index, replacement) });
                }
            }
            Reject(program with {
                Structures = program.Structures.SetItem(14,
                program.Structures[14] with { Size = 8 })
            });
            Reject(program with {
                Functions = program.Functions.SetItem(runIndex,
                BoundTreeTraversal.RewriteCalls(run, call => call.Function == 12
                    ? call with {
                        Arguments = call.Arguments.SetItem(0,
                        new BoundReceiverLoan(1, NativeDx11CompilerSessionContract.SessionType, default))
                    }
                    : call))
            });
            Reject(program with {
                Functions = program.Functions.SetItem(runIndex,
                BoundTreeTraversal.RewriteCalls(run, call => call.Function == 12
                    ? call with { Arguments = [] } : call))
            });
        }
    }

    [Test]
    public void DirectDx11CompilerSessionPrivateLoaderCatalogRejectsImportMutations()
    {
        foreach (var import in NativeDx11CompilerSessionContract.Imports)
        {
            foreach (var forged in new[] {
                import with { DllName = "D3DCompiler_47.dll" },
                import with { ExportName = import.ExportName + "A" },
                import with { ReturnType = PrimitiveType.Void },
                import with { ParameterTypes = [] },
                import with { IsNoThrow = false },
                import with { DeploymentPolicy = NativeImportDeploymentPolicy.ApplicationDirectory },
            })
            {
                Assert.That(NativeDx11CompilerSessionContract.MatchesImport(forged), Is.False);
                Assert.Throws<ArgumentException>(() => NativeImportCatalog.CreateForTesting("forged-session", [forged]));
            }
        }
        const string source = """
            [Terrum.Interop.NativeImport("windows-x64.dx11-compiler-session.kernel32.GetProcAddress")]
            [NoThrow]
            extern uint64 GetProcAddress(uint64 module, uint64 export);
            int32 Main() { return GetProcAddress(0, 0) == 0 ? 0 : 1; }
            """;
        var compilation = Compilation.CreateWithNativeImports(SyntaxTree.Parse(source), BoundRuntimePolicy.CheckedExceptions);
        Assert.That(compilation.Program, Is.Null);
        Assert.That(compilation.Diagnostics, Has.Some.Matches(static diagnostic => diagnostic.Code == "TRM3030"));
    }

    [Test]
    public async Task DirectDx11CompilerSessionLoaderExportAndBlobLifetimeAreDeterministicAsync()
    {
        var root = FindRepositoryRoot();
        var environment = await FindNativeFixtureEnvironmentAsync(root).ConfigureAwait(true);
        var directory = TestArtifactPaths.CreateDirectory("terrum-compiler-session-");
        try
        {
            var dll = Path.Combine(directory, "native-dx11-compiler-session-probe.dll");
            var shadow = Path.Combine(directory, "D3DCompiler_47.dll");
            var probe = Path.Combine(root, "tests", "native", "native-dx11-compiler-session-probe.c");
            var shadowSource = Path.Combine(root, "tests", "native", "native-dx11-compiler-shadow.c");
            var build = new ProcessStartInfo(Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe") {
                WorkingDirectory = directory,
                Arguments = $"/d /s /c \"call \"{environment}\" >nul && cl /nologo /W4 /WX /wd4201 /wd4191 /TC /LD /Focompiler.obj \"{probe}\" /link /OUT:\"{dll}\" user32.lib d3d11.lib && cl /nologo /W4 /WX /wd4201 /TC /LD /Foshadow.obj \"{shadowSource}\" /link /OUT:\"{shadow}\"\"",
            };
            build.Environment["PATH"] = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                "Microsoft Visual Studio", "Installer") + ";" + build.Environment["PATH"];
            _ = await RunAsync(build, TimeSpan.FromSeconds(60)).ConfigureAwait(true);
            File.Copy(Path.Combine(root, "samples", "design", "win32-dx11-triangle", "triangle.hlsl"),
                Path.Combine(directory, "triangle.hlsl"));
            (string Mode, string Trace, int Exit)[] cases = [
                ("valid", "LSMCEVARRVRF", 0), ("case-fold", "LSMCEVARRVRF", 0),
                ("positive-success", "LSMCEVARRVRF", 0),
                ("directory-boundary", "LSMCEVARRVRF", 0),
                ("load-failure", "L", 1), ("directory-zero", "LSF", 1),
                ("directory-truncated", "LSF", 1), ("directory-long", "LSF", 1),
                ("directory-required", "LSF", 1), ("directory-max", "LSF", 1),
                ("module-zero", "LSMF", 1), ("module-truncated", "LSMF", 1),
                ("module-required", "LSMF", 1), ("module-max", "LSMF", 1),
                ("wrong-path", "LSMCF", 1), ("preloaded", "LSMCF", 1),
                ("compare-failure", "LSMCF", 1), ("export-missing", "LSMCEF", 1),
                ("compile-failure", "LSMCEVRF", 1), ("aliased-failure", "LSMCEVARRF", 1),
                ("null-success", "LSMCEVRF", 1), ("diagnostics", "LSMCEVRF", 1),
                ("pixel-failure", "LSMCEVARRVRF", 1),
                ("unload-failure", "LSMCEVARRVRF", 87), ("reject-unload-failure", "LSMCF", 87),
            ];
            foreach (var (program, variant) in new[] {
                (CompileDirectWindowDevicePipeline(), "warp"), (CompileDirectHardwarePipeline(), "hardware"),
            })
            {
                var code = DirectWindowDeviceShaderCode(program);
                var exports = NativeDx11CompilerSessionContract.Imports.Select(static import => import.ExportName)
                    .ToHashSet(StringComparer.Ordinal);
                var injected = code with {
                    NativeImports = [.. code.NativeImports.Select(import =>
                    exports.Contains(import.ExportName) ? import with {
                        DllName = Path.GetFileName(dll), ExportName = "NativeFfiCompiler" + import.ExportName,
                    } : import.ExportName switch {
                        "GetMessageW" => import with {
                            DllName = Path.GetFileName(dll), ExportName = "NativeFfiCompilerGetMessageW",
                            },
                        _ => import,
                    })]
                };
                var executable = Path.Combine(directory, variant + ".exe");
                await File.WriteAllBytesAsync(executable, PeImage.Build(injected)).ConfigureAwait(true);
                foreach (var (mode, trace, exit) in cases)
                {
                    var report = Path.Combine(directory, variant + "-" + mode + ".txt");
                    var start = new ProcessStartInfo(executable) {
                        WorkingDirectory = directory,
                        UseShellExecute = false,
                        RedirectStandardError = true,
                    };
                    start.Environment["TERRUM_DX11_COMPILER_MODE"] = mode;
                    start.Environment["TERRUM_DX11_COMPILER_TRACE"] = report;
                    start.Environment["TERRUM_DX11_COMPILER_SHADOW"] = shadow;
                    start.Environment["TERRUM_DX11_COMPILER_DRIVER"] = variant == "hardware" ? "1" : "5";
                    using var process = Process.Start(start) ?? throw new InvalidOperationException("Compiler session probe did not start.");
                    var stderr = process.StandardError.ReadToEndAsync();
                    try
                    {
                        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20)).ConfigureAwait(true);
                        var errors = await stderr.ConfigureAwait(true);
                        Assert.That(process.ExitCode == exit, Is.True, $"{variant}/{mode}: exit {process.ExitCode}, expected {exit}: {errors}");
                        Assert.That(await File.ReadAllTextAsync(report).ConfigureAwait(true), Is.EqualTo(trace));
                        Assert.That(errors.Contains("APP_LOCAL_COMPILER_EXECUTED", StringComparison.Ordinal), Is.False);
                        Assert.That(errors, Is.Empty);
                    }
                    finally
                    {
                        if (!process.HasExited)
                        {
                            process.Kill(entireProcessTree: true);
                            await process.WaitForExitAsync().ConfigureAwait(true);
                        }
                    }
                }
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static BoundBlock FindCompilerSessionScope(BoundStatement statement, int closeIndex)
    {
        if (statement is BoundBlock {
            Statements: [BoundOwnerDeclaration { Variable: var variable }, BoundTryFinally scope],
        } candidate && scope.Finally.Statements is [
            BoundDrop { Variable: var dropped, CleanupFunction: var cleanupFunction },
        ] && cleanupFunction == closeIndex && variable == dropped)
        {
            return candidate;
        }
        if (statement is BoundBlock block)
        {
            foreach (var child in block.Statements)
            {
                if (child is BoundBlock or BoundTryFinally or BoundIf)
                {
                    var found = FindCompilerSessionScope(child, closeIndex);
                    if (found.Statements.Length != 0)
                    {
                        return found;
                    }
                }
            }
        }
        else if (statement is BoundTryFinally lifetime)
        {
            return FindCompilerSessionScope(lifetime.Body, closeIndex);
        }
        else if (statement is BoundIf branch)
        {
            return FindCompilerSessionScope(branch.Then, closeIndex);
        }
        return new BoundBlock([], default);
    }
}
