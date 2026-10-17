// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.IO;
using System.Linq;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests;

public sealed partial class NativeSourceImportTests
{
    private static string DirectHardwarePipelineSource => File.ReadAllText(Path.Combine(
        FindRepositoryRoot(), "samples", "design", "win32-dx11-triangle", "window-device-hardware-pipeline-stage.terrum"))
        .Replace("\r\n", "\n", StringComparison.Ordinal);

    private static BoundProgram CompileDirectHardwarePipeline()
    {
        var compilation = Compilation.CreateWithNativeImports([SyntaxTree.Parse(DirectHardwarePipelineSource)],
            runtimePolicy: BoundRuntimePolicy.CheckedExceptions);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var __nunitAssertValue0 = compilation.Program;
        Assert.That(__nunitAssertValue0, Is.TypeOf<BoundProgram>());
        var program = ((BoundProgram)__nunitAssertValue0);
        Assert.That(NativeDx11HardwareAcquisitionContract.MatchesProgram(program), Is.True);
        return program;
    }

    [Test]
    public void DirectHardwarePipelineEmitsOnlyDriverArgumentChange()
    {
        var hardware = CompileDirectHardwarePipeline();
        Assert.That(hardware.Structures.Take(3), Is.EqualTo(NativeDx11WarpAcquisitionContract.Structures));
        Assert.That(hardware.Functions[0].Intrinsic, Is.EqualTo(BoundIntrinsic.Dx11HardwareTryCreate));
        Assert.That(hardware.Functions[0].Name, Is.EqualTo(NativeDx11RuntimeSessionContract.TypeName + "." + NativeDx11HardwareAcquisitionContract.AcquisitionName));
        Assert.That(hardware.Functions, Has.None.Matches(static function => function.Intrinsic == BoundIntrinsic.Dx11WarpTryCreate));

        var hardwareCode = DirectWindowDeviceShaderCode(hardware);
        var __nunitAssertValue1 = global::System.Linq.Enumerable.ToArray(global::System.Linq.Enumerable.Where(hardwareCode.Functions, range => range.Start == hardwareCode.SourceFunctionOffsets[0]));
        Assert.That(__nunitAssertValue1, Has.Exactly(1).Items);
        var range = __nunitAssertValue1[0];
        byte[] driver = [0xB8, 1, 0, 0, 0, 0x89, 0xC2];
        var offset = hardwareCode.Code.AsSpan(range.Start, range.End - range.Start).IndexOf(driver);
        Assert.That(offset >= 0, Is.True, "Hardware must pass signed four-byte driver value 1 in EDX.");
        byte[] warpDriver = [0xB8, 5, 0, 0, 0, 0x89, 0xC2];
        Assert.That(hardwareCode.Code.AsSpan(range.Start, range.End - range.Start).IndexOf(warpDriver) < 0, Is.True, "Hardware must not pass the WARP driver value 5.");
        Assert.That(range.StackSize & 15, Is.EqualTo(8));
        Assert.That(hardwareCode.NativeImports, Has.None.Matches(static import =>
            import.ExportName == "D3D11CreateDeviceAndSwapChain" || import.DllName.Contains("d3d11", StringComparison.OrdinalIgnoreCase)));
        Assert.That(hardwareCode.NativeImports, Has.Some.Matches(static import => import.ExportName == "GetClientRect"));
        Assert.That(hardwareCode.NativeImports, Has.None.Matches(static import =>
            import.ExportName.StartsWith("NativeFfi", StringComparison.Ordinal)
            || import.ExportName is "LoadLibraryA" or "LoadLibraryW"));
        Assert.That(hardwareCode.NativeImports, Has.Some.Matches(static import => import.ExportName == "GetProcAddress"));
        Assert.That(hardwareCode.NativeImports, Has.None.Matches(static import =>
            import.DllName.Contains("D3DCompiler", StringComparison.OrdinalIgnoreCase)
            || import.ExportName == "D3DCompileFromFile"));
        var image = NativeEmitter.EmitWithNativeImports(hardware, NativeRuntimePolicy.CheckedExceptions);
        Assert.That(image.AsSpan().IndexOf("NativeFfi"u8) < 0, Is.True);
        Assert.That(image.AsSpan().IndexOf("D3D11CreateDeviceAndSwapChain\0"u8) < 0, Is.True);
        Assert.That(image.AsSpan().IndexOf("LoadLibraryExW\0"u8) >= 0, Is.True);
    }

    [TestCase("trycreatehardwareswapchain(Handle,", "trycreatehardwareswapchain(Terrum.Interop.Windows.HWND.NoOwner,")]
    [TestCase("out var device, out var swapchain", "out var swapchain, out var device")]
    [TestCase("out var device", "out device")]
    [TestCase("InlineArray<Vertex, 3> vertices = [", "InlineArray<Vertex, 2> vertices = [")]
    [TestCase("Vertex first = vertices[0];", "Vertex first = vertices[1];")]
    [TestCase("Vertex second = vertices[1];", "Vertex second = vertices[0];")]
    [TestCase("context.Draw(3, 0);", "context.Draw(4, 0);")]
    [TestCase("context.Draw(3, 0);", "context.Draw(3, 0); context.Draw(3, 0);")]
    [TestCase("context.ClearState();", "")]
    [TestCase("context.ClearState();", "pixelShader.Close(); context.ClearState();")]
    [TestCase("GetClientRect(Handle, &clientRectangle)",
        "GetClientRect(Terrum.Interop.Windows.HWND.NoOwner, &clientRectangle)")]
    [TestCase("if (GetClientRect(Handle, &clientRectangle) == 0)",
        "if (GetClientRect(Handle, &clientRectangle) != 0)")]
    [TestCase("context.IASetVertexBuffers(buffer, 12, 0);", "context.IASetVertexBuffers(move buffer, 12, 0);")]
    [TestCase("if (swapchain.Present())", "if (!swapchain.Present())")]
    [TestCase("return window.Run();", "return 0;")]
    [TestCase("return 1;", "return 0;")]
    [TestCase("context.Draw(3, 0);", "uint64 raw = device; context.Draw(3, 0);")]
    [TestCase("context.Draw(3, 0);", "trycreatewarpswapchain(Handle, out var d, out var s, out var c); context.Draw(3, 0);")]
    [TestCase("context.Draw(3, 0);", "trycreatehardwareswapchain(Handle, out var d, out var s, out var c); context.Draw(3, 0);")]
    public void DirectHardwarePipelineRejectsSourceEscapeAndLifecycleMutations(string original, string replacement)
    {
        Assert.That(DirectHardwarePipelineSource.Contains(original, StringComparison.Ordinal), Is.True);
        var source = DirectHardwarePipelineSource.Replace(original, replacement, StringComparison.Ordinal);
        var compilation = Compilation.CreateWithNativeImports([SyntaxTree.Parse(source)],
            runtimePolicy: BoundRuntimePolicy.CheckedExceptions);
        Assert.That(compilation.Program, Is.Null);
        Assert.That(compilation.HasErrors, Is.True, string.Join(Environment.NewLine, compilation.Diagnostics));
    }

    [Test]
    public void DirectHardwarePipelineRequiresCheckedSystemCatalog()
    {
        var tree = SyntaxTree.Parse(DirectHardwarePipelineSource);
        foreach (var compilation in new[] {
            Compilation.Create(tree, BoundRuntimePolicy.CheckedExceptions),
            Compilation.CreateWithNativeImports(tree, runtimePolicy: BoundRuntimePolicy.FailFast),
            Compilation.CreateWithNativeImports(SyntaxTree.Parse(DirectWarpAcquisitionSource.Replace(
                "trycreatewarpswapchain", "trycreatehardwareswapchain", StringComparison.Ordinal)),
                runtimePolicy: BoundRuntimePolicy.FailFast),
            Compilation.CreateWithNativeImports(SyntaxTree.Parse(DirectWindowDeviceShaderSource.Replace(
                "trycreatewarpswapchain", "trycreatehardwareswapchain", StringComparison.Ordinal)),
                runtimePolicy: BoundRuntimePolicy.CheckedExceptions),
        })
        {
            Assert.That(compilation.Program, Is.Null);
            Assert.That(compilation.HasErrors, Is.True, string.Join(Environment.NewLine, compilation.Diagnostics));
        }
    }

    [Test]
    public void DirectHardwarePipelineRejectsAcquisitionDeclarationAndPredecessorForgeries()
    {
        var program = CompileDirectHardwarePipeline();
        var acquisition = program.Functions[0];
        foreach (var function in new[] {
            acquisition with { Name = NativeDx11WarpAcquisitionContract.AcquisitionName },
            acquisition with { Intrinsic = BoundIntrinsic.Dx11WarpTryCreate },
            acquisition with { Intrinsic = BoundIntrinsic.None },
            acquisition with { ReturnType = PrimitiveType.Int32 },
            acquisition with { IsNoThrow = false },
            acquisition with { IsInternal = false },
            acquisition with { ParameterCount = 3 },
            acquisition with { RequiresUnsafeCaller = true },
            acquisition with { Body = new BoundBlock([new BoundReturn(null, default)], default) },
            acquisition with { Variables = acquisition.Variables.SetItem(2, acquisition.Variables[2] with { IsOut = false }) },
            acquisition with { Variables = acquisition.Variables.SetItem(3, acquisition.Variables[3] with { Type = (PrimitiveType)100 }) },
        })
        {
            RejectHardwareForgery(program with { Functions = program.Functions.SetItem(0, function) });
        }
        foreach (var forgery in new[] {
            program with { RuntimePolicy = BoundRuntimePolicy.FailFast },
            program with { EntryPoint = 0 },
            program with { NativeImportCatalog = null },
            program with { NativeImportCatalogId = "unapproved.other" },
            program with { Functions = program.Functions.SetItem(1, acquisition) },
            program with { Structures = program.Structures.SetItem(0, program.Structures[0] with { IsUnique = false }) },
            program with { Structures = program.Structures.SetItem(1, program.Structures[1] with { Size = 16 }) },
            program with { Structures = program.Structures.SetItem(2, program.Structures[2] with { CleanupMethod = program.Structures[0].CleanupMethod }) },
            program with { Structures = program.Structures.SetItem(4, program.Structures[0]) },
            program with { NativeUtf8ZLiterals = program.NativeUtf8ZLiterals.SetItem(2, "COLOR") },
            program with { Functions = program.Functions.SetItem(19, program.Functions[19] with {
                NativeDx11DirectShaderMethod = NativeDx11DirectShaderContract.CreateInputLayout with { Slot = 12 },
            }) },
            program with { Functions = program.Functions.SetItem(28, program.Functions[28] with { ReturnType = PrimitiveType.Int32 }) },
            program with { Functions = program.Functions.SetItem(29, program.Functions[29] with { IsNoThrow = false }) },
        })
        {
            RejectHardwareForgery(forgery);
        }
        var warp = CompileDirectWindowDevicePipeline();
        RejectHardwareForgery(warp with {
            Functions = warp.Functions.SetItem(0, warp.Functions[0] with {
                Intrinsic = BoundIntrinsic.Dx11HardwareTryCreate,
            })
        });
    }

    [Test]
    public void DirectHardwarePipelineRejectsBoundCallerOutputsDrawAndCleanupForgeries()
    {
        var program = CompileDirectHardwarePipeline();
        var runIndex = NativeDx11DirectPipelineContract.RunIndex;
        var run = program.Functions[runIndex];
        var __nunitAssertValue2 = run.Body.Statements[2];
        Assert.That(__nunitAssertValue2, Is.TypeOf<BoundTryFinally>());
        var runtimeScope = ((BoundTryFinally)__nunitAssertValue2);
        var __nunitAssertValue3 = runtimeScope.Body.Statements[2];
        Assert.That(__nunitAssertValue3, Is.TypeOf<BoundTryFinally>());
        var deviceScope = ((BoundTryFinally)__nunitAssertValue3);
        var __nunitAssertValue4 = deviceScope.Body.Statements[1];
        Assert.That(__nunitAssertValue4, Is.TypeOf<BoundTryFinally>());
        var swapScope = ((BoundTryFinally)__nunitAssertValue4);
        var __nunitAssertValue5 = swapScope.Body.Statements[1];
        Assert.That(__nunitAssertValue5, Is.TypeOf<BoundTryFinally>());
        var contextScope = ((BoundTryFinally)__nunitAssertValue5);
        var acquisitionScope = contextScope.Body;
        var __nunitAssertValue6 = acquisitionScope.Statements[0];
        Assert.That(__nunitAssertValue6, Is.TypeOf<BoundIf>());
        var branch = ((BoundIf)__nunitAssertValue6);
        var __nunitAssertValue7 = branch.Condition;
        Assert.That(__nunitAssertValue7, Is.TypeOf<BoundCall>());
        var call = ((BoundCall)__nunitAssertValue7);
        Assert.That(call.Function, Is.EqualTo(0));
        BoundProgram ReplaceCall(BoundCall replacement) => program with {
            Functions = program.Functions.SetItem(runIndex, run with {
                Body = NativeDx11StageProjection.ReplaceBlock(run.Body, acquisitionScope,
                    acquisitionScope with {
                        Statements = acquisitionScope.Statements.SetItem(0, branch with { Condition = replacement }),
                    }),
            }),
        };
        foreach (var replacement in new[] {
            call with { Function = 1 },
            call with { Type = PrimitiveType.Int32 },
            call with { IsUnsafeAuthorized = true },
            call with { ArgumentParameterIndices = [0, 1, 2, 3] },
            call with { Arguments = call.Arguments.SetItem(0, new BoundNativeNoOwner(default)) },
            call with { Arguments = call.Arguments.RemoveAt(3) },
        })
        {
            RejectHardwareForgery(ReplaceCall(replacement));
        }
        for (var argument = 2; argument <= 4; argument++)
        {
            var __nunitAssertValue8 = call.Arguments[argument];
            Assert.That(__nunitAssertValue8, Is.TypeOf<BoundOutArgument>());
            var output = ((BoundOutArgument)__nunitAssertValue8);
            foreach (var replacement in new[] {
                output with { DeclaresVariable = false },
                output with { IsInit = true },
                output with { Variable = output.Variable + 1 },
                output with { Type = (PrimitiveType)103 },
            })
            {
                RejectHardwareForgery(ReplaceCall(call with {
                    Arguments = call.Arguments.SetItem(argument, replacement),
                }));
            }
        }
        RejectHardwareForgery(program with {
            Functions = program.Functions.SetItem(runIndex, run with {
                Name = "Other.Run",
            })
        });
        var pipelineBlock = FindPipelineScope(run.Body);
        var __nunitAssertValue9 = global::System.Linq.Enumerable.ToArray(pipelineBlock.Statements);
        Assert.That(__nunitAssertValue9, Has.Exactly(1).Items);
        var __nunitAssertValue10 = __nunitAssertValue9[0];
        Assert.That(__nunitAssertValue10, Is.TypeOf<BoundTryFinally>());
        var scope = ((BoundTryFinally)__nunitAssertValue10);
        foreach (var replacement in new[] {
            scope with { Finally = scope.Finally with { Statements = [] } },
            scope with { Body = scope.Body with { Statements = scope.Body.Statements.RemoveAt(9) } },
            scope with { Body = scope.Body with { Statements = scope.Body.Statements.Insert(0, scope.Finally.Statements[0]) } },
            scope with { Body = scope.Body with { Statements = scope.Body.Statements.Add(scope.Body.Statements[9]) } },
        })
        {
            RejectHardwareForgery(program with {
                Functions = program.Functions.SetItem(runIndex, run with {
                    Body = NativeDx11StageProjection.ReplaceBlock(run.Body, pipelineBlock,
                        pipelineBlock with { Statements = [replacement] }),
                }),
            });
        }
    }

    private static void RejectHardwareForgery(BoundProgram program)
    {
        Assert.That(NativeDx11HardwareAcquisitionContract.MatchesProgram(program), Is.False);
        Assert.Throws<ArgumentException>(() => SemanticVerifier.VerifyReferences(program));
        Assert.Throws<ArgumentException>(() => NativeEmitter.EmitWithNativeImports(program, NativeRuntimePolicy.CheckedExceptions));
    }
}
