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
    public void DirectDx11RuntimeSessionReservesOpaqueOwnerIdentity(string declaration)
    {
        const string name = "ProvisionalD3D11Session";
        var source = "namespace Terrum.Interop.Dx11;\n" + (declaration == "alias"
            ? "alias " + name + " = uint64;" : declaration + " " + name + " { uint64 Raw; }")
            + "\nint32 Main() { return 0; }";
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        Assert.That(compilation.Program, Is.Null);
        Assert.That(compilation.Diagnostics, Has.Some.Matches(static diagnostic => diagnostic.Code == "TRM3003"));
    }

    [Test]
    public void DirectDx11RuntimeSessionHasNoD3D11ImportAndPreservesTwelveArgumentAbi()
    {
        foreach (var program in new[] { CompileDirectWindowDevicePipeline(), CompileDirectHardwarePipeline() })
        {
            Assert.That(NativeDx11CompilerSessionContract.TryGetFunctionIndices(program.Functions,
                BoundIntrinsic.Dx11RuntimeSessionTryOpen, BoundIntrinsic.Dx11RuntimeSessionClose,
                out var runtimeOpen, out var runtimeClose), Is.True);
            Assert.That(runtimeClose, Is.EqualTo(runtimeOpen + 1));
            Assert.That(program.Structures[15], Is.EqualTo(NativeDx11RuntimeSessionContract.Structure));
            Assert.That(ValueLayout.Alignment(NativeDx11RuntimeSessionContract.SessionType,
                program.Structures, program.NativeImportCatalog), Is.EqualTo(8));
            Assert.That(program.Functions[0].DeclaringStruct, Is.EqualTo(NativeDx11RuntimeSessionContract.TypeName));
            Assert.That(program.Functions[0].ParameterCount, Is.EqualTo(5));
            Assert.That(program.Functions[0].Variables[0].Type, Is.EqualTo(NativeDx11RuntimeSessionContract.SessionType));
            var code = DirectWindowDeviceShaderCode(program);
            Assert.That(code.NativeImports, Has.None.Matches(static import =>
                import.DllName.Contains("d3d11", StringComparison.OrdinalIgnoreCase)
                || import.ExportName is "D3D11CreateDeviceAndSwapChain" or "LoadLibraryW" or "LoadLibraryA"));
            var __nunitAssertValue0 = global::System.Linq.Enumerable.ToArray(global::System.Linq.Enumerable.Where(code.Functions, item => item.Start == code.SourceFunctionOffsets[0]));
            Assert.That(__nunitAssertValue0, Has.Exactly(1).Items);
            var range = __nunitAssertValue0[0];
            var bytes = code.Code.AsSpan(range.Start, range.End - range.Start).ToArray();
            Assert.That(range.StackSize & 15, Is.EqualTo(8));
            Assert.That(ContainsSequence(bytes, [0xFF, 0xD0]), Is.True);
            Assert.That(ContainsSequence(bytes, [0x85, 0xC0, 0x0F, 0x88]), Is.True);
            foreach (var offset in new byte[] { 32, 40, 48, 56, 64, 72, 80, 88 })
            {
                Assert.That(ContainsSequence(bytes, [0x48, 0x89, 0x84, 0x24, offset, 0, 0, 0]), Is.True);
            }
            var __nunitAssertValue1 = global::System.Linq.Enumerable.ToArray(global::System.Linq.Enumerable.Where(code.Functions, item =>
                            item.Start == code.SourceFunctionOffsets[runtimeOpen]));
            Assert.That(__nunitAssertValue1, Has.Exactly(1).Items);
            var loader = __nunitAssertValue1[0];
            Assert.That(loader.StackSize & 15, Is.EqualTo(8));
            Assert.That(loader.StackSize, Is.InRange(1100, 2000));
            Assert.That(ContainsSequence(code.Code.AsSpan(loader.Start, loader.End - loader.Start).ToArray(),
                [0xB8, 0, 8, 0, 0, 0x41, 0x89, 0xC0]), Is.True);
        }
        var predecessor = DirectWindowDeviceShaderCode(CompileDirectWindowDeviceShader());
        Assert.That(predecessor.NativeImports, Has.Some.Matches(static import =>
            import.ExportName == "D3D11CreateDeviceAndSwapChain" && Path.IsPathFullyQualified(import.DllName)));
    }

    [TestCase("!tryopend3d11(out var d3d11)", "tryopend3d11(out var d3d11)")]
    [TestCase("d3d11.trycreate", "trycreate")]
    [TestCase("d3d11.trycreate", "d3d11.GetProcAddress")]
    [TestCase("if (d3d11.trycreate", "d3d11.Close(); if (d3d11.trycreate")]
    [TestCase("context.Draw(3, 0);", "uint64 address = d3d11; context.Draw(3, 0);")]
    [TestCase("context.Draw(3, 0);", "tryopend3d11(out var other); context.Draw(3, 0);")]
    [TestCase("context.ClearState();", "d3d11.Close(); context.ClearState();")]
    public void DirectDx11RuntimeSessionRejectsSourceLifetimeAndExportEscapes(string original, string replacement)
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
    public void DirectDx11RuntimeSessionRejectsBoundSessionAndLifetimeForgeries()
    {
        foreach (var (program, hardware) in new[] {
            (CompileDirectWindowDevicePipeline(), false), (CompileDirectHardwarePipeline(), true),
        })
        {
            var forgeryIndex = 0;
            void Reject(BoundProgram forged)
            {
                if (hardware)
                { RejectHardwareForgery(forged); }
                else
                { RejectPipelineForgery(forged, $"Runtime session forgery {forgeryIndex}."); }
                forgeryIndex++;
            }
            var runIndex = NativeDx11DirectPipelineContract.RunIndex;
            var run = program.Functions[runIndex];
            Assert.That(NativeDx11CompilerSessionContract.TryGetFunctionIndices(program.Functions,
                BoundIntrinsic.Dx11RuntimeSessionTryOpen, BoundIntrinsic.Dx11RuntimeSessionClose,
                out var runtimeOpen, out var runtimeClose), Is.True);
            var __nunitAssertValue2 = run.Body.Statements[2];
            Assert.That(__nunitAssertValue2, Is.TypeOf<BoundTryFinally>());
            var lifetime = ((BoundTryFinally)__nunitAssertValue2);
            var __nunitAssertValue3 = global::System.Linq.Enumerable.ToArray(lifetime.Finally.Statements);
            Assert.That(__nunitAssertValue3, Has.Exactly(1).Items);
            var __nunitAssertValue4 = __nunitAssertValue3[0];
            Assert.That(__nunitAssertValue4, Is.TypeOf<BoundDrop>());
            var drop = ((BoundDrop)__nunitAssertValue4);
            var __nunitAssertValue5 = lifetime.Body.Statements[0];
            Assert.That(__nunitAssertValue5, Is.TypeOf<BoundIf>());
            var failure = ((BoundIf)__nunitAssertValue5);
            var __nunitAssertValue6 = failure.Condition;
            Assert.That(__nunitAssertValue6, Is.TypeOf<BoundUnary>());
            var __nunitAssertValue7 = (((BoundUnary)__nunitAssertValue6)).Operand;
            Assert.That(__nunitAssertValue7, Is.TypeOf<BoundCall>());
            var open = ((BoundCall)__nunitAssertValue7);
            foreach (var forged in new[] {
                run.Body with { Statements = run.Body.Statements.RemoveAt(1) },
                run.Body with { IsManagedLexicalScope = false },
                run.Body with { RetiredManagedLocals = default },
                run.Body with { Statements = run.Body.Statements.SetItem(2, lifetime with {
                    Finally = lifetime.Finally with { Statements = [] },
                }) },
                run.Body with { Statements = run.Body.Statements.SetItem(2, lifetime with {
                    Finally = lifetime.Finally with { Statements = [drop with { CleanupFunction = 1 }] },
                }) },
                run.Body with { Statements = run.Body.Statements.SetItem(2, lifetime with {
                    Body = lifetime.Body with { Statements = lifetime.Body.Statements.Insert(1, drop) },
                }) },
                run.Body with { Statements = run.Body.Statements.SetItem(2, lifetime with {
                    Body = lifetime.Body with { Statements = lifetime.Body.Statements.RemoveAt(0) },
                }) },
                run.Body with { Statements = run.Body.Statements.SetItem(2, lifetime with {
                    Body = lifetime.Body with { Statements = lifetime.Body.Statements.SetItem(0, failure with {
                        Condition = open,
                    }) },
                }) },
            })
            {
                Reject(program with { Functions = program.Functions.SetItem(runIndex, run with { Body = forged }) });
            }
            foreach (var index in new[] { 0, runtimeOpen, runtimeClose })
            {
                var function = program.Functions[index];
                foreach (var forged in new[] {
                    function with { IsNoThrow = false },
                    function with { Intrinsic = BoundIntrinsic.None },
                    function with { ParameterCount = function.ParameterCount + 1 },
                    function with { DeclaringStruct = null },
                    function with { Body = new BoundBlock([new BoundReturn(null, default)], default) },
                }.Where(forged => forged != function))
                {
                    Reject(program with { Functions = program.Functions.SetItem(index, forged) });
                }
            }
            foreach (var forged in new[] {
                program.Structures[15] with { Size = 8 },
                program.Structures[15] with { IsUnique = false },
                program.Structures[15] with { CleanupMethod = NativeDx11WarpAcquisitionContract.DeviceName + ".Close" },
            })
            {
                Reject(program with { Structures = program.Structures.SetItem(15, forged) });
            }
            foreach (var index in Enumerable.Range(0, 3))
            {
                Reject(program with {
                    Structures = program.Structures.SetItem(index,
                    program.Structures[index] with { Name = "ForgedOwner" })
                });
            }
            foreach (var loan in new BoundExpression[] {
                new BoundReceiverLoan(2, NativeDx11RuntimeSessionContract.SessionType, default),
                new BoundRead(1, NativeDx11RuntimeSessionContract.SessionType, default),
                new BoundMove(1, NativeDx11RuntimeSessionContract.SessionType, default),
            })
            {
                Reject(program with {
                    Functions = program.Functions.SetItem(runIndex,
                    BoundTreeTraversal.RewriteCalls(run, call => call.Function == 0
                        ? call with { Arguments = call.Arguments.SetItem(0, loan) } : call))
                });
            }
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task DirectDx11RuntimeSessionLoaderAbiAndObjectLifetimeAreDeterministicAsync(bool hardware)
    {
        var root = FindRepositoryRoot();
        var environment = await FindNativeFixtureEnvironmentAsync(root).ConfigureAwait(true);
        var directory = Directory.CreateTempSubdirectory("terrum-d3d11-session-").FullName;
        try
        {
            var dll = Path.Combine(directory, "native-dx11-runtime-session-probe.dll");
            var shadow = Path.Combine(directory, "d3d11.dll");
            var probe = Path.Combine(root, "tests", "native", "native-dx11-runtime-session-probe.c");
            var shadowSource = Path.Combine(root, "tests", "native", "native-dx11-runtime-shadow.c");
            var build = new ProcessStartInfo(Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe") {
                WorkingDirectory = directory,
                Arguments = $"/d /s /c \"call \"{environment}\" >nul && cl /nologo /W4 /WX /wd4201 /wd4191 /Od /TC /LD /Fofixture.obj \"{probe}\" /link /OUT:\"{dll}\" user32.lib && cl /nologo /W4 /WX /wd4201 /TC /LD /Foshadow.obj \"{shadowSource}\" /link /OUT:\"{shadow}\"\"",
            };
            build.Environment["PATH"] = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                "Microsoft Visual Studio", "Installer") + ";" + build.Environment["PATH"];
            _ = await RunAsync(build, TimeSpan.FromSeconds(60)).ConfigureAwait(true);
            File.Copy(Path.Combine(root, "samples", "design", "win32-dx11-triangle", "triangle.hlsl"),
                Path.Combine(directory, "triangle.hlsl"));
            var code = DirectWindowDeviceShaderCode(hardware ? CompileDirectHardwarePipeline() : CompileDirectWindowDevicePipeline());
            var loaders = NativeDx11CompilerSessionContract.Imports.Select(static import => import.ExportName)
                .ToHashSet(StringComparer.Ordinal);
            var injected = code with {
                NativeImports = [.. code.NativeImports.Select(import => {
                var export = loaders.Contains(import.ExportName) ? "NativeFfiRuntime" + import.ExportName
                    : import.ExportName == "DestroyWindow" ? "NativeFfiRuntimeDestroyWindow"
                    : import.ExportName is "GetMessageW" or "ShowWindow" or "TranslateMessage"
                        or "DispatchMessageW" or "PostQuitMessage" or "UnregisterClassW"
                        ? "NativeFfiDirectPipeline" + import.ExportName : null;
                return export is null ? import : import with { DllName = Path.GetFileName(dll), ExportName = export };
            })]
            };
            var executable = Path.Combine(directory, "pipeline.exe");
            await File.WriteAllBytesAsync(executable, PeImage.Build(injected)).ConfigureAwait(true);
            var pipeline = hardware
                ? "ABLVPGRcovlbtxydOpmTFQeKrgYXIBCSD"
                : "ABLVPKGRKcovlbtxydOpKrgmTFQeYXIBCSD";
            (string Mode, string Trace, int Exit)[] cases = [
                ("valid", "12345" + pipeline + "6WUZ", 0),
                ("case-fold", "12345" + pipeline + "6WUZ", 0),
                ("directory-boundary", "12345" + pipeline + "6WUZ", 0),
                ("positive-success", "12345" + pipeline + "6WUZ", 0),
                ("maximum-success", "12345" + pipeline + "6WUZ", 0),
                ("load-failure", "1WUZ", 1), ("directory-zero", "126WUZ", 1),
                ("directory-truncated", "126WUZ", 1), ("directory-required", "126WUZ", 1),
                ("directory-long", "126WUZ", 1), ("directory-max", "126WUZ", 1),
                ("module-zero", "1236WUZ", 1), ("module-truncated", "1236WUZ", 1),
                ("module-required", "1236WUZ", 1), ("module-max", "1236WUZ", 1),
                ("wrong-path", "12346WUZ", 1), ("suffix", "12346WUZ", 1),
                ("preloaded", "12346WUZ", 1), ("compare-failure", "12346WUZ", 1),
                ("export-missing", "123456WUZ", 1), ("acquire-failure", "12345ACSD6WUZ", 1),
                ("alias-failure", "12345Aaaa6WUZ", 1), ("alias-success", "12345Abaaa6WUZ", 1),
                ("buffer-failure", "12345ABCSD6WUZ", 1),
                ("vertex-failure", "12345ABLVIBCSD6WUZ", 1),
                ("pixel-failure", "12345ABLVPXIBCSD6WUZ", 1),
                ("unload-failure", "12345" + pipeline + "6", 87),
                ("reject-unload-failure", "12346", 87),
            ];
            cases = [.. cases, .. Enumerable.Range(0, 8).Select(mask =>
                ("partial-" + mask, "12345A" + ((mask & 4) != 0 ? "c" : "")
                    + ((mask & 2) != 0 ? "s" : "") + ((mask & 1) != 0 ? "d" : "") + "6WUZ", 1)),
                .. Enumerable.Range(0, 7).Select(mask =>
                    ("null-" + mask, "12345A" + ((mask & 4) != 0 ? "c" : "")
                        + ((mask & 2) != 0 ? "s" : "") + ((mask & 1) != 0 ? "d" : ""),
                        NativeDx11WarpAcquisitionContract.NullSuccessError))];
            foreach (var (mode, trace, exit) in cases)
            {
                var report = Path.Combine(directory, mode + ".txt");
                var start = new ProcessStartInfo(executable) {
                    WorkingDirectory = directory,
                    UseShellExecute = false,
                    RedirectStandardError = true,
                };
                start.Environment["TERRUM_DX11_RUNTIME_MODE"] = mode;
                start.Environment["TERRUM_DX11_RUNTIME_SHADOW"] = shadow;
                start.Environment["TERRUM_DX11_PIPELINE_MODE"] = "0";
                start.Environment["TERRUM_DX11_PIPELINE_REPORT"] = report;
                if (hardware)
                { start.Environment["TERRUM_DX11_HARDWARE"] = "1"; }
                using var process = Process.Start(start) ?? throw new InvalidOperationException("Runtime session probe did not start.");
                var stderr = process.StandardError.ReadToEndAsync();
                try
                {
                    await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20)).ConfigureAwait(true);
                    var errors = await stderr.ConfigureAwait(true);
                    Assert.That(process.ExitCode == exit, Is.True, $"{hardware}/{mode}: exit {process.ExitCode}, expected {exit}: {errors}");
                    Assert.That(await File.ReadAllTextAsync(report).ConfigureAwait(true), Is.EqualTo(trace));
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
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
