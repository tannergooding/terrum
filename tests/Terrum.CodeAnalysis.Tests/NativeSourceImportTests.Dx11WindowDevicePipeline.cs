// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests;

public sealed partial class NativeSourceImportTests
{
    private static string DirectWindowDevicePipelineSource => File.ReadAllText(Path.Combine(
        FindRepositoryRoot(), "samples", "design", "win32-dx11-triangle", "window-device-pipeline-stage.terrum"))
        .Replace("\r\n", "\n", StringComparison.Ordinal);

    private static BoundProgram CompileDirectWindowDevicePipeline()
    {
        Assert.That(NativeWindowsModulePathContract.MatchesImport(
            NativeWindowsModulePathContract.GetModuleFileNameImport), Is.True);
        Assert.That(NativeWindowsModulePathContract.MatchesImport(
            NativeWindowsModulePathContract.GetLastErrorImport), Is.True);
        Assert.That(NativeWindowsModulePathContract.MatchesCatalog(NativeImportCatalog.WindowsX64), Is.True);
        var compilation = Compilation.CreateWithNativeImports([SyntaxTree.Parse(DirectWindowDevicePipelineSource)],
            runtimePolicy: BoundRuntimePolicy.CheckedExceptions);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var __nunitAssertValue0 = compilation.Program;
        Assert.That(__nunitAssertValue0, Is.TypeOf<BoundProgram>());
        var program = ((BoundProgram)__nunitAssertValue0);
        Assert.That(NativeDx11DirectPipelineContract.MatchesProgram(program), Is.True);
        return program;
    }

    [Test]
    public void DirectWindowDevicePipelineStageSealsBoundProgramAndExactSdkAbi()
    {
        var program = CompileDirectWindowDevicePipeline();
        Assert.That(NativeDx11DirectShaderContract.MatchesProgram(program), Is.False);
        Assert.That(program.Functions[48].Variables[5].Name, Is.EqualTo("shaderPath"));
        Assert.That(program.Functions[48].Variables[5].Type, Is.EqualTo(PrimitiveType.NativeFixedArray));
        Assert.That(program.Functions[48].Variables[10].Name, Is.EqualTo("vertices"));
        Assert.That(program.Functions[48].Variables[10].Type, Is.EqualTo(PrimitiveType.NativeFixedArray));
        Assert.That(NativeDx11DirectShaderContract.MatchesProgram(CompileDirectWindowDeviceShader()), Is.True);
        Assert.That(ValueLayout.MaximumSize, Is.EqualTo(16));
        Assert.That(NativeDx11DirectPipelineContract.ViewportSize, Is.EqualTo(24));
        Assert.That(NativeDx11DirectPipelineContract.ViewportAlignment, Is.EqualTo(4));
        Assert.That(NativeDx11DirectPipelineContract.ClearState.Slot, Is.EqualTo(110));
        Assert.That(NativeDx11DirectPipelineContract.ClearState.Signature, Is.EqualTo("void(ID3D11DeviceContext*)"));
        Assert.That(NativeDx11DirectPipelineContract.ClearState.Abi, Is.EqualTo("windows-x64-rcx-void"));
        Assert.That(NativeDx11DirectPipelineContract.ClearState.Effects, Is.EqualTo(NativeComEffects.None));
        Assert.That(NativeDx11DirectPipelineContract.OMSetRenderTargets.Slot, Is.EqualTo(33));
        Assert.That(NativeDx11DirectPipelineContract.OMSetRenderTargets.Signature, Is.EqualTo("void(ID3D11DeviceContext*,UINT,ID3D11RenderTargetView*const*,ID3D11DepthStencilView*)"));
        Assert.That(NativeDx11DirectPipelineContract.OMSetRenderTargets.Abi, Is.EqualTo("windows-x64-rcx-edx-r8-r9-void"));
        Assert.That(NativeDx11DirectPipelineContract.OMSetRenderTargets.Effects, Is.EqualTo(NativeComEffects.None));
        Assert.That(NativeDx11DirectPipelineContract.IASetPrimitiveTopology.Slot, Is.EqualTo(24));
        Assert.That(NativeDx11DirectPipelineContract.IASetPrimitiveTopology.Signature, Is.EqualTo("void(ID3D11DeviceContext*,D3D11_PRIMITIVE_TOPOLOGY)"));
        Assert.That(NativeDx11DirectPipelineContract.IASetPrimitiveTopology.Abi, Is.EqualTo("windows-x64-rcx-edx-void"));
        Assert.That(NativeDx11DirectPipelineContract.IASetPrimitiveTopology.Effects, Is.EqualTo(NativeComEffects.None));
        Assert.That(NativeDx11DirectPipelineContract.IASetInputLayout.Slot, Is.EqualTo(17));
        Assert.That(NativeDx11DirectPipelineContract.IASetInputLayout.Signature, Is.EqualTo("void(ID3D11DeviceContext*,ID3D11InputLayout*)"));
        Assert.That(NativeDx11DirectPipelineContract.IASetInputLayout.Abi, Is.EqualTo("windows-x64-rcx-rdx-void"));
        Assert.That(NativeDx11DirectPipelineContract.IASetInputLayout.Effects, Is.EqualTo(NativeComEffects.None));
        Assert.That(NativeDx11DirectPipelineContract.IASetVertexBuffers.Slot, Is.EqualTo(18));
        Assert.That(NativeDx11DirectPipelineContract.IASetVertexBuffers.Signature, Is.EqualTo("void(ID3D11DeviceContext*,UINT,UINT,ID3D11Buffer*const*,const UINT*,const UINT*)"));
        Assert.That(NativeDx11DirectPipelineContract.IASetVertexBuffers.Abi, Is.EqualTo("windows-x64-rcx-edx-r8d-r9-stack32-stack40-void"));
        Assert.That(NativeDx11DirectPipelineContract.IASetVertexBuffers.Effects, Is.EqualTo(NativeComEffects.None));
        Assert.That(NativeDx11DirectPipelineContract.VSSetShader.Slot, Is.EqualTo(11));
        Assert.That(NativeDx11DirectPipelineContract.VSSetShader.Signature, Is.EqualTo("void(ID3D11DeviceContext*,ID3D11VertexShader*,ID3D11ClassInstance*const*,UINT)"));
        Assert.That(NativeDx11DirectPipelineContract.VSSetShader.Abi, Is.EqualTo("windows-x64-rcx-rdx-r8-r9d-void"));
        Assert.That(NativeDx11DirectPipelineContract.VSSetShader.Effects, Is.EqualTo(NativeComEffects.None));
        Assert.That(NativeDx11DirectPipelineContract.PSSetShader.Slot, Is.EqualTo(9));
        Assert.That(NativeDx11DirectPipelineContract.PSSetShader.Signature, Is.EqualTo("void(ID3D11DeviceContext*,ID3D11PixelShader*,ID3D11ClassInstance*const*,UINT)"));
        Assert.That(NativeDx11DirectPipelineContract.PSSetShader.Abi, Is.EqualTo("windows-x64-rcx-rdx-r8-r9d-void"));
        Assert.That(NativeDx11DirectPipelineContract.PSSetShader.Effects, Is.EqualTo(NativeComEffects.None));
        Assert.That(NativeDx11DirectPipelineContract.Draw.Slot, Is.EqualTo(13));
        Assert.That(NativeDx11DirectPipelineContract.Draw.Signature, Is.EqualTo("void(ID3D11DeviceContext*,UINT,UINT)"));
        Assert.That(NativeDx11DirectPipelineContract.Draw.Abi, Is.EqualTo("windows-x64-rcx-edx-r8d-void"));
        Assert.That(NativeDx11DirectPipelineContract.Draw.Effects, Is.EqualTo(NativeComEffects.None));
        Assert.That(NativeDx11DirectPipelineContract.RSSetViewports.Slot, Is.EqualTo(44));
        Assert.That(NativeDx11DirectPipelineContract.RSSetViewports.Signature, Is.EqualTo("void(ID3D11DeviceContext*,UINT,const FLOAT[6])"));
        Assert.That(NativeDx11DirectPipelineContract.RSSetViewports.Abi, Is.EqualTo("windows-x64-rcx-edx-r8-void"));
        Assert.That(NativeDx11DirectPipelineContract.RSSetViewports.Effects, Is.EqualTo(NativeComEffects.None));
        foreach (var expected in NativeDx11DirectBufferContract.Functions(program.Structures))
        {
            Assert.That(program.Functions.Count(function => function.Name == expected.Name
                && function.Intrinsic == expected.Intrinsic), Is.EqualTo(1));
        }
        var code = DirectWindowDeviceShaderCode(program);
        Assert.That(code.NativeImports, Has.None.Matches(static import =>
            import.ExportName.StartsWith("NativeFfi", StringComparison.Ordinal)));
        var modulePathImports = code.NativeImports.Where(
            static import => import.ExportName == "GetModuleFileNameW").ToArray();
        Assert.That(modulePathImports.Length, Is.EqualTo(3));
        Assert.Multiple(() => {
            foreach (var import in modulePathImports)
                Assert.That(import.DllName, Is.EqualTo("kernel32.dll"));
        });
        Assert.That(NativeImportCatalog.WindowsX64.TryGetImport(
            NativeWindowsModulePathContract.GetModuleFileNameKey, out var modulePathDefinition), Is.True);
        Assert.That(modulePathDefinition, Is.EqualTo(NativeWindowsModulePathContract.GetModuleFileNameImport));
        Assert.That(modulePathDefinition!.ParameterTypes, Is.EqualTo(new[] {
            PrimitiveType.NativeNullPointer, PrimitiveType.NativeStorageAddress,
            PrimitiveType.UInt32,
        }));
        Assert.That(modulePathDefinition.ReturnType, Is.EqualTo(PrimitiveType.UInt32));
        var __nunitAssertValue1 = global::System.Linq.Enumerable.ToArray(global::System.Linq.Enumerable.Where(code.NativeImports, static import => import.ExportName == "GetLastError"));
        Assert.That(__nunitAssertValue1, Has.Exactly(1).Items);
        var lastErrorImport = __nunitAssertValue1[0];
        Assert.That(lastErrorImport.DllName, Is.EqualTo("kernel32.dll"));
        Assert.That(NativeImportCatalog.WindowsX64.TryGetImport(
            NativeWindowsModulePathContract.GetLastErrorKey, out var lastErrorDefinition), Is.True);
        Assert.That(lastErrorDefinition, Is.EqualTo(NativeWindowsModulePathContract.GetLastErrorImport));
        var __nunitAssertValue2 = global::System.Linq.Enumerable.ToArray(global::System.Linq.Enumerable.Where(code.Functions, range => range.Start == code.SourceFunctionOffsets[NativeDx11DirectPipelineContract.RunIndex]));
        Assert.That(__nunitAssertValue2, Has.Exactly(1).Items);
        var runRange = __nunitAssertValue2[0];
        Assert.That(runRange.StackSize, Is.InRange(0, 3840));
        Assert.That(program.Functions.SelectMany(static function => function.Variables)
            .Count(variable => variable.NativeFixedArrayLayout is BoundNativeFixedArrayLayout layout
                && NativeWindowsModulePathContract.IsPathBufferLayout(
                    layout, program.Structures, program.NativeImportCatalog)), Is.EqualTo(1));
        for (var index = 0; index < NativeDx11DirectPipelineContract.FunctionCount; index++)
        {
            var function = program.Functions[21 + index];
            Assert.That(function.ReturnType, Is.EqualTo(PrimitiveType.Void));
            Assert.That(function.IsNoThrow, Is.True);
            var __nunitAssertValue3 = global::System.Linq.Enumerable.ToArray(global::System.Linq.Enumerable.Where(code.Functions, range => range.Start == code.SourceFunctionOffsets[21 + index]));
            Assert.That(__nunitAssertValue3, Has.Exactly(1).Items);
            var range = __nunitAssertValue3[0];
            var bytes = code.Code.AsSpan(range.Start, range.End - range.Start).ToArray();
            Assert.That(range.StackSize & 15, Is.EqualTo(8));
            var __nunitAssertValue4 = function.NativeComMethod;
            Assert.That(__nunitAssertValue4, Is.TypeOf<NativeComMethodDefinition>());
            var methodSlot = (((NativeComMethodDefinition)__nunitAssertValue4)).Slot;
            var offset = methodSlot * 8;
            byte[] call = offset <= 127 ? [0xFF, 0x50, (byte)offset]
                : [0xFF, 0x90, (byte)(offset & 255), (byte)(offset >> 8), 0, 0];
            Assert.That(ContainsSequence(bytes, call), Is.True);
            if (index == 1)
            {
                Assert.That(function.ParameterCount, Is.EqualTo(7));
                Assert.Multiple(() => {
                    foreach (var variable in function.Variables.Skip(1))
                        Assert.That(variable.Type, Is.EqualTo(PrimitiveType.Float32));
                });
                Assert.That(NativeComCallContract.IsValidVTableCallFunction(function, program), Is.True);
            }
            if (index is 5 or 6)
            {
                Assert.That(ContainsSequence(bytes, [
                    0x31, 0xC0, 0x49, 0x89, 0xC0,
                    0xB8, 0, 0, 0, 0, 0x41, 0x89, 0xC1,
                ]), Is.True, "Shader binding must pass a null class-instance array and zero count through the generic COM ABI.");
            }
            if (index == 0)
            {
                Assert.That(ContainsSequence(bytes, [0xB8, 1, 0, 0, 0, 0x89, 0xC2]), Is.True, "Render-target binding must pass a count of one through the generic COM ABI.");
                Assert.That(ContainsSequence(bytes, [0x31, 0xC0, 0x49, 0x89, 0xC1]), Is.True, "Render-target binding must pass a null depth-stencil pointer through the generic COM ABI.");
            }
            if (index == 3)
            {
                int[] stackOffsets = [32, 40];
                foreach (var argument in stackOffsets)
                {
                    Assert.That(ContainsSequence(bytes, [0x48, 0x89, 0x84, 0x24, (byte)argument, 0, 0, 0]), Is.True, "Both IASetVertexBuffers stack arguments must store full-width cell addresses.");
                }
            }
        }
        var image = NativeEmitter.EmitWithNativeImports(program, NativeRuntimePolicy.CheckedExceptions);
        Assert.That(image.AsSpan().IndexOf("NativeFfi"u8) < 0, Is.True);
        Assert.That(image.AsSpan().IndexOf("GetModuleFileNameW\0"u8) >= 0, Is.True);
        Assert.That(image.AsSpan().IndexOf("GetLastError\0"u8) >= 0, Is.True);
        Assert.That(code.NativeImports, Has.Some.Matches(static import => import.ExportName == "GetClientRect"));
    }

    [TestCase("InlineArray<Vertex, 3> vertices = [", "InlineArray<Vertex, 2> vertices = [")]
    [TestCase("D3DCompileFromFile(&shaderPath[0]", "D3DCompileFromFile(&shaderPath[1]")]
    [TestCase("context.Draw(3, 0);", "context.Draw(4, 0);")]
    [TestCase("context.Draw(3, 0);", "context.Draw(3, 1);")]
    [TestCase("context.Draw(3, 0);", "context.Draw(3, 0); context.Draw(3, 0);")]
    [TestCase("context.Draw(3, 0);", "")]
    [TestCase("context.ClearState();", "")]
    [TestCase("context.ClearState();", "pixelShader.Close(); context.ClearState();")]
    [TestCase("GetClientRect(Handle, &clientRectangle)",
        "GetClientRect(Terrum.Interop.Windows.HWND.NoOwner, &clientRectangle)")]
    [TestCase("GetClientRect(Handle, &clientRectangle)",
        "GetClientRect(window.Handle, &clientRectangle)")]
    [TestCase("context.IASetVertexBuffers(buffer, 12, 0);", "context.IASetVertexBuffers(move buffer, 12, 0);")]
    [TestCase("context.IASetVertexBuffers(buffer, 12, 0);", "context.IASetVertexBuffers(buffer, 36, 0);")]
    [TestCase("context.IASetPrimitiveTopology(4);", "context.IASetPrimitiveTopology(5);")]
    [TestCase("context.VSSetShader(vertexShader);", "context.VSSetShader(pixelShader);")]
    [TestCase("context.OMSetRenderTargets(renderTarget);", "context.OMSetRenderTargets(backBuffer);")]
    [TestCase("context.IASetInputLayout(inputLayout);", "")]
    [TestCase("if (!swapchain.Present())", "if (swapchain.Present())")]
    public void DirectWindowDevicePipelineStageRejectsSourceMutations(string original, string replacement)
    {
        Assert.That(DirectWindowDevicePipelineSource.Contains(original, StringComparison.Ordinal), Is.True);
        var source = DirectWindowDevicePipelineSource.Replace(original, replacement, StringComparison.Ordinal);
        var compilation = Compilation.CreateWithNativeImports([SyntaxTree.Parse(source)],
            runtimePolicy: BoundRuntimePolicy.CheckedExceptions);
        Assert.That(compilation.Program, Is.Null);
        Assert.That(compilation.HasErrors, Is.True, string.Join(Environment.NewLine, compilation.Diagnostics));
    }

    [TestCase("GetModuleFileNameW(null, &shaderPath[0], 260)",
        "GetModuleFileNameW(null, &shaderPath[0], 259)")]
    [TestCase("if (pathLength == 0)", "if (pathLength != 0)")]
    [TestCase("if (pathLength >= 260)", "if (pathLength > 260)")]
    [TestCase("while (scan > 0 && prefixLength == 0)", "while (scan > 0 && prefixLength != 0)")]
    [TestCase("shaderPath[scan] == 92 || shaderPath[scan] == 47",
        "shaderPath[scan] == 92 && shaderPath[scan] == 47")]
    [TestCase("prefixLength + 14 > 260", "prefixLength + 13 > 260")]
    [TestCase("shaderPath[prefixLength + 12] = 108", "shaderPath[prefixLength + 12] = 109")]
    public void DirectWindowDevicePipelinePathBehaviorRemainsSourceAuthored(
        string original, string replacement)
    {
        Assert.That(DirectWindowDevicePipelineSource.Contains(original, StringComparison.Ordinal), Is.True);
        var source = DirectWindowDevicePipelineSource.Replace(original, replacement, StringComparison.Ordinal);
        var compilation = Compilation.CreateWithNativeImports([SyntaxTree.Parse(source)],
            runtimePolicy: BoundRuntimePolicy.CheckedExceptions);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(compilation.Program, Is.TypeOf<BoundProgram>());
    }

    [TestCase("Vertex second = vertices[1];", "Vertex second = vertices[0];")]
    [TestCase("Vertex first = vertices[0];", "Vertex first = vertices[1];")]
    public void DirectWindowDevicePipelineStageAcceptsVertexDataMutations(string original, string replacement)
    {
        var source = DirectWindowDevicePipelineSource;
        Assert.That(source.Contains(original, StringComparison.Ordinal), Is.True);
        var mutated = source.Replace(original, replacement, StringComparison.Ordinal);
        var compilation = Compilation.CreateWithNativeImports([SyntaxTree.Parse(mutated)],
            runtimePolicy: BoundRuntimePolicy.CheckedExceptions);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var __nunitAssertValue5 = compilation.Program;
        Assert.That(__nunitAssertValue5, Is.TypeOf<BoundProgram>());
        Assert.That(NativeDx11DirectPipelineContract.MatchesProgram(((BoundProgram)__nunitAssertValue5)), Is.True);
    }

    [Test]
    public void DirectPipelineSampleArrayStorageIsNotAvailableToOrdinaryWindowsX64Source()
    {
        const string source = """
            [Layout(Sequential)]
            struct Vertex {
                float32 X;
                float32 Y;
                float32 Z;
                public Vertex(float32 x, float32 y, float32 z) {
                    X = x;
                    Y = y;
                    Z = z;
                }
            }

            int32 Main() {
                InlineArray<Vertex, 3> vertices = [
                    new Vertex(0, 0.6, 0.5),
                    new Vertex(0.6, -0.5, 0.5),
                    new Vertex(-0.6, -0.5, 0.5)];
                return vertices[0].X == 0.0 ? 0 : 1;
            }
            """;
        var compilation = Compilation.CreateWithNativeImports(
            SyntaxTree.Parse(source), BoundRuntimePolicy.CheckedExceptions);

        Assert.That(compilation.Program, Is.Null);
        Assert.That(compilation.Diagnostics, Is.Not.Empty);
    }

    [Test]
    public void DirectWindowDevicePipelineStageRequiresCheckedSystemCatalog()
    {
        var tree = SyntaxTree.Parse(DirectWindowDevicePipelineSource);
        foreach (var compilation in new[] {
            Compilation.Create(tree, BoundRuntimePolicy.CheckedExceptions),
            Compilation.CreateWithNativeImports(tree, runtimePolicy: BoundRuntimePolicy.FailFast),
        })
        {
            Assert.That(compilation.Program, Is.Null);
            Assert.That(compilation.Diagnostics, Has.Some.Matches(static diagnostic => diagnostic.Code == "TRM3030"));
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public void DirectPipelineStagesAcceptMultipleSourceTrees(bool hardware)
    {
        var source = hardware ? DirectHardwarePipelineSource : DirectWindowDevicePipelineSource;
        var entryPointStart = source.LastIndexOf("int32 Main()", StringComparison.Ordinal);
        Assert.That(entryPointStart > 0, Is.True);
        var compilation = Compilation.CreateWithNativeImports([
            SyntaxTree.Parse(source[..entryPointStart]),
            SyntaxTree.Parse("namespace Triangle.Interop;\n" + source[entryPointStart..]),
        ], runtimePolicy: BoundRuntimePolicy.CheckedExceptions);

        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var __nunitAssertValue6 = compilation.Program;
        Assert.That(__nunitAssertValue6, Is.TypeOf<BoundProgram>());
        var program = ((BoundProgram)__nunitAssertValue6);
        Assert.That(hardware ? NativeDx11HardwareAcquisitionContract.MatchesProgram(program)
            : NativeDx11DirectPipelineContract.MatchesProgram(program), Is.True);
    }

    [Test]
    public void DirectWindowDevicePipelineStageDoesNotReserveOrdinaryContextMethodNames()
    {
        const string Source = """
            struct Other {
                int32 Value;
                public static int32 Draw() { return 3; }
                public static int32 ClearState() { return 0; }
                public static int32 RSSetViewports() { return 32; }
                public static int32 IASetVertexBuffers() { return 12; }
            }
            int32 Main() {
                return Other.Draw() + Other.ClearState() + Other.RSSetViewports() + Other.IASetVertexBuffers();
            }
            """;
        foreach (var compilation in new[] {
            Compilation.Create(SyntaxTree.Parse(Source), BoundRuntimePolicy.CheckedExceptions),
            Compilation.CreateWithNativeImports(SyntaxTree.Parse(Source), runtimePolicy: BoundRuntimePolicy.CheckedExceptions),
        })
        {
            Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
            var __nunitAssertValue7 = compilation.Program;
            Assert.That(__nunitAssertValue7, Is.TypeOf<BoundProgram>());
            var program = ((BoundProgram)__nunitAssertValue7);
            Assert.That(program.Functions, Has.None.Matches(static function =>
                function.Name == NativeDx11WarpAcquisitionContract.ContextName + ".RSSetViewports"));
            SemanticVerifier.VerifyReferences(program);
        }
    }

    [Test]
    public void DirectWindowDevicePipelineStageRejectsMethodAndPredecessorForgeries()
    {
        var program = CompileDirectWindowDevicePipeline();
        foreach (var index in Enumerable.Range(21, 9))
        {
            var function = program.Functions[index];
            var forgedFunctions = new List<BoundFunction>();
            if (function.Intrinsic == BoundIntrinsic.NativeComVTableCall)
            {
                var __nunitAssertValue8 = function.NativeComMethod;
                Assert.That(__nunitAssertValue8, Is.TypeOf<NativeComMethodDefinition>());
                var method = ((NativeComMethodDefinition)__nunitAssertValue8);
                forgedFunctions.AddRange([
                    function with { NativeComMethod = method with { Slot = method.Slot + 1 } },
                    function with { NativeComMethod = method with { Signature = "HRESULT(void*)" } },
                    function with { NativeComMethod = method with { Abi = "x86-stdcall" } },
                    function with { NativeComMethod = null },
                ]);
            }
            forgedFunctions.AddRange([
                function with { Intrinsic = BoundIntrinsic.None },
                function with { ReturnType = PrimitiveType.Int32 },
                function with { IsNoThrow = false },
                function with { IsCleanup = true },
                function with { Variables = function.Variables.SetItem(0,
                    function.Variables[0] with { IsMutable = true }) },
            ]);
            foreach (var forgedFunction in forgedFunctions)
            {
                RejectPipelineForgery(program with {
                    Functions = program.Functions.SetItem(index, forgedFunction),
                });
            }
        }
        RejectPipelineForgery(program with { RuntimePolicy = BoundRuntimePolicy.FailFast });
        RejectPipelineForgery(program with { EntryPoint = 40 });
        RejectPipelineForgery(program with { NativeImportCatalog = null });
        RejectPipelineForgery(program with { NativeUtf8ZLiterals = program.NativeUtf8ZLiterals.SetItem(2, "COLOR") });
        RejectPipelineForgery(program with {
            Structures = program.Structures.SetItem(12, program.Structures[12] with { IsUnique = false }),
        });
        Assert.That(NativeDx11DirectPipelineContract.MatchesProgram(program with {
            Structures = program.Structures.SetItem(4, program.Structures[4] with {
                Name = NativeDx11DirectShaderContract.CodeName,
            }),
        }), Is.False);
        RejectPipelineForgery(program with {
            Functions = program.Functions.SetItem(19,
            program.Functions[19] with {
                NativeDx11DirectShaderMethod = NativeDx11DirectShaderContract.CreateInputLayout with { Slot = 12 },
            })
        });
    }

    [Test]
    public void DirectWindowDevicePipelineStageRejectsBoundDrawBindingsAndCleanupForgeries()
    {
        var program = CompileDirectWindowDevicePipeline();
        var run = program.Functions[48];
        var block = FindPipelineScope(run.Body);
        var __nunitAssertValue9 = global::System.Linq.Enumerable.ToArray(block.Statements);
        Assert.That(__nunitAssertValue9, Has.Exactly(1).Items);
        var __nunitAssertValue10 = __nunitAssertValue9[0];
        Assert.That(__nunitAssertValue10, Is.TypeOf<BoundTryFinally>());
        var scope = ((BoundTryFinally)__nunitAssertValue10);
        BoundProgram Replace(BoundTryFinally replacement)
        {
            return program with {
                Functions = program.Functions.SetItem(48, run with {
                    Body = NativeDx11StageProjection.ReplaceBlock(run.Body, block,
                        block with { Statements = [replacement] }),
                }),
            };
        }
        foreach (var index in Enumerable.Range(2, 8))
        {
            var statement = scope.Body.Statements[index];
            var call = statement switch {
                BoundExpressionStatement { Expression: BoundCall expression } => expression,
                BoundBlock { Statements: [BoundExpressionStatement { Expression: BoundCall expression }] } => expression,
                BoundIf { Condition: BoundUnary { Operand: BoundCall expression } } => expression,
                _ => throw new InvalidOperationException("Unexpected pipeline call statement."),
            };
            var __nunitAssertValue11 = call.Arguments[0];
            Assert.That(__nunitAssertValue11, Is.TypeOf<BoundReceiverLoan>());
            var receiver = ((BoundReceiverLoan)__nunitAssertValue11);
            BoundStatement ReplaceCall(BoundCall replacement) => statement switch {
                BoundExpressionStatement expressionStatement =>
                    expressionStatement with { Expression = replacement },
                BoundBlock block => block with {
                    Statements = [
                        ((BoundExpressionStatement)block.Statements[0]) with { Expression = replacement },
                    ],
                },
                BoundIf conditional => conditional with {
                    Condition = conditional.Condition is BoundUnary unary
                        ? unary with { Operand = replacement }
                        : throw new InvalidOperationException("Unexpected viewport condition."),
                },
                _ => throw new InvalidOperationException("Unexpected pipeline call statement."),
            };
            foreach (var forgedCall in new[] {
                call with { Function = call.Function + 1 },
                call with { Type = PrimitiveType.Int32 },
                call with { IsUnsafeAuthorized = true },
                call with { ArgumentParameterIndices = [.. Enumerable.Range(0, call.Arguments.Length)] },
                call with { Arguments = call.Arguments.SetItem(0, receiver with { Variable = 1 }) },
                call with { Arguments = call.Arguments.SetItem(0, new BoundRead(3, (PrimitiveType)102, default)) },
            })
            {
                RejectPipelineForgery(Replace(scope with {
                    Body = scope.Body with {
                        Statements = scope.Body.Statements.SetItem(index,
                        ReplaceCall(forgedCall))
                    },
                }));
            }
            for (var argument = 1; argument < call.Arguments.Length; argument++)
            {
                var value = call.Arguments[argument];
                BoundExpression forgedValue = value switch {
                    BoundConstant constant => constant with { Value = constant.Value + 1 },
                    BoundFieldRead field => field with { Field = field.Field + 1 },
                    BoundReceiverLoan loan => loan with { Variable = loan.Variable + 1 },
                    BoundCast cast => cast with { Type = PrimitiveType.Int32 },
                    _ => throw new InvalidOperationException("Unexpected pipeline argument."),
                };
                RejectPipelineForgery(Replace(scope with {
                    Body = scope.Body with {
                        Statements = scope.Body.Statements.SetItem(index,
                        ReplaceCall(call with { Arguments = call.Arguments.SetItem(argument, forgedValue) }))
                    },
                }));
            }
        }
        foreach (var replacement in new[] {
            scope with { Finally = scope.Finally with { Statements = [] } },
            scope with { Body = scope.Body with { Statements = scope.Body.Statements.Add(scope.Body.Statements[9]) } },
            scope with { Body = scope.Body with { Statements = scope.Body.Statements.RemoveAt(9) } },
            scope with { Body = scope.Body with { Statements = scope.Body.Statements.Insert(0, scope.Finally.Statements[0]) } },
            scope with { Body = scope.Body with { IsManagedLexicalScope = false } },
            scope with { Finally = scope.Finally with { RetiredManagedLocals = [24] } },
            scope with { Finally = scope.Finally with { Statements = [
                new BoundDrop(24, 9, default), scope.Finally.Statements[0],
            ] } },
            scope with { Body = scope.Body with { Statements = scope.Body.Statements.SetItem(2, scope.Body.Statements[9]) } },
        })
        {
            RejectPipelineForgery(Replace(replacement));
        }
        var __nunitAssertValue12 = global::System.Linq.Enumerable.ToArray(scope.Finally.Statements);
        Assert.That(__nunitAssertValue12, Has.Exactly(1).Items);
        var __nunitAssertValue13 = __nunitAssertValue12[0];
        Assert.That(__nunitAssertValue13, Is.TypeOf<BoundExpressionStatement>());
        var clearStatement = ((BoundExpressionStatement)__nunitAssertValue13);
        var __nunitAssertValue14 = clearStatement.Expression;
        Assert.That(__nunitAssertValue14, Is.TypeOf<BoundCall>());
        var clear = ((BoundCall)__nunitAssertValue14);
        RejectPipelineForgery(Replace(scope with {
            Finally = scope.Finally with {
                Statements = [
            clearStatement with { Expression = clear with { Arguments = [
                new BoundReceiverLoan(1, (PrimitiveType)100, default),
            ] } },
        ]
            }
        }));
        var deep = scope.Body;
        for (var depth = 0; depth <= SemanticVerifier.MaximumDepth; depth++)
        {
            deep = new BoundBlock([deep], default);
        }
        RejectPipelineForgery(Replace(scope with { Body = deep }));
    }

    [Test]
    public void DirectWindowDevicePipelinePathUsesGenericBoundedStorageLoans()
    {
        var program = CompileDirectWindowDevicePipeline();
        var run = program.Functions[NativeDx11DirectPipelineContract.RunIndex];
        var (pathBlock, start) = FindShaderPathSetup(run.Body);
        var __nunitAssertValue15 = pathBlock.Statements[start + 1];
        Assert.That(__nunitAssertValue15, Is.TypeOf<BoundLocal>());
        var queryLocal = ((BoundLocal)__nunitAssertValue15);
        var __nunitAssertValue16 = queryLocal.Initializer;
        Assert.That(__nunitAssertValue16, Is.TypeOf<BoundCall>());
        var query = ((BoundCall)__nunitAssertValue16);
        var __nunitAssertValue17 = query.Arguments[1];
        Assert.That(__nunitAssertValue17, Is.TypeOf<BoundNativeStorageAddress>());
        var queryAddress = ((BoundNativeStorageAddress)__nunitAssertValue17);
        Assert.That(queryAddress.CarrierType, Is.EqualTo(PrimitiveType.NativeFixedArray));
        Assert.That(queryAddress.ElementType, Is.EqualTo(PrimitiveType.Char16));
        Assert.That(queryAddress.ElementCount, Is.EqualTo(NativeWindowsModulePathContract.Capacity));
        Assert.That(queryAddress.ElementStride, Is.EqualTo(sizeof(char)));
        Assert.That(queryAddress.ByteExtent, Is.EqualTo(NativeWindowsModulePathContract.StorageBytes));
        Assert.That(queryAddress.IsReadonly, Is.False);
        Assert.That(NativeStorageParameterContract.TryGetParameter(
            NativeWindowsModulePathContract.GetModuleFileNameImport, 1, out var queryStorageParameter), Is.True);
        Assert.That(queryStorageParameter, Is.Not.Null);
        Assert.That(NativeStorageParameterContract.MatchesAddress(queryAddress, queryStorageParameter), Is.True);

        var compileCalls = OwnerFixtureCalls(run.Body).Where(static call => call.Function == 12).ToArray();
        Assert.That(compileCalls, Is.Not.Empty);
        foreach (var call in compileCalls)
        {
            Assert.That(NativeStorageParameterContract.TryGetParameter(
                program.Functions[call.Function], 1, program, out var compileStorageParameter), Is.True);
            Assert.That(compileStorageParameter, Is.Not.Null);
            var __nunitAssertValue18 = call.Arguments[1];
            Assert.That(__nunitAssertValue18, Is.TypeOf<BoundNativeStorageAddress>());
            Assert.That(NativeStorageParameterContract.MatchesAddress(
                            ((BoundNativeStorageAddress)__nunitAssertValue18), compileStorageParameter), Is.True);
        }

        BoundProgram ReplacePathBlock(BoundBlock replacement) => program with {
            Functions = program.Functions.SetItem(NativeDx11DirectPipelineContract.RunIndex, run with {
                Body = NativeDx11StageProjection.ReplaceBlock(run.Body, pathBlock, replacement),
            }),
        };
        var __nunitAssertValue19 = pathBlock.Statements[start + 8];
        Assert.That(__nunitAssertValue19, Is.TypeOf<BoundExpressionStatement>());
        var firstWriteStatement = ((BoundExpressionStatement)__nunitAssertValue19);
        var __nunitAssertValue20 = firstWriteStatement.Expression;
        Assert.That(__nunitAssertValue20, Is.TypeOf<BoundNativeFixedArrayWrite>());
        var firstWrite = ((BoundNativeFixedArrayWrite)__nunitAssertValue20);
        var __nunitAssertValue21 = firstWrite.Value;
        Assert.That(__nunitAssertValue21, Is.TypeOf<BoundConstant>());
        var firstValue = ((BoundConstant)__nunitAssertValue21);
        AcceptPipelineForgery(ReplacePathBlock(pathBlock with {
            Statements = pathBlock.Statements.SetItem(start + 8, firstWriteStatement with {
                Expression = firstWrite with { Value = firstValue with { Value = firstValue.Value + 1 } },
            }),
        }));
        var __nunitAssertValue22 = firstWrite.Index;
        Assert.That(__nunitAssertValue22, Is.TypeOf<BoundBinary>());
        var firstOffset = ((BoundBinary)__nunitAssertValue22);
        var __nunitAssertValue23 = firstOffset.Right;
        Assert.That(__nunitAssertValue23, Is.TypeOf<BoundConstant>());
        var offset = ((BoundConstant)__nunitAssertValue23);
        AcceptPipelineForgery(ReplacePathBlock(pathBlock with {
            Statements = pathBlock.Statements.SetItem(start + 8, firstWriteStatement with {
                Expression = firstWrite with {
                    Index = firstOffset with { Right = offset with { Value = offset.Value + 1 } },
                },
            }),
        }));

        RejectSemanticForgery(ReplacePathBlock(pathBlock with {
            Statements = pathBlock.Statements.SetItem(start + 1, queryLocal with {
                Initializer = query with {
                    Arguments = query.Arguments.SetItem(1,
                        queryAddress with { Type = PrimitiveType.NativeUtf16ZPointer }),
                },
            }),
        }));

        var readonlyForgery = RewriteOwnerFixtureCalls(run.Body, call =>
            call.Function == 12 && call.Arguments[1] is BoundNativeStorageAddress {
                CarrierType: PrimitiveType.NativeFixedArray,
            } address
                ? call with {
                    Arguments = call.Arguments.SetItem(1, address with {
                        Type = PrimitiveType.NativeStorageAddress,
                    }),
                }
                : call);
        RejectSemanticForgery(program with {
            Functions = program.Functions.SetItem(NativeDx11DirectPipelineContract.RunIndex,
                run with { Body = readonlyForgery }),
        });
        RejectSemanticForgery(program with {
            NativeImportCatalog = NativeImportCatalog.CreateForTesting("forged-dx11-path", [
                new NativeImportDefinition("forged-dx11-path.kernel32.GetTickCount", "kernel32.dll",
                    "GetTickCount", [], PrimitiveType.UInt32, true, true, false,
                    NativeImportDeploymentPolicy.System32),
            ]),
        });
    }

    private static void RejectPipelineForgery(BoundProgram program, string? message = null)
    {
        Assert.That(NativeDx11DirectPipelineContract.MatchesProgram(program), Is.False, message);
        _ = Assert.Throws<ArgumentException>(() => SemanticVerifier.VerifyReferences(program));
        _ = Assert.Throws<ArgumentException>(() => NativeEmitter.EmitWithNativeImports(
            program, NativeRuntimePolicy.CheckedExceptions));
    }

    private static void RejectSemanticForgery(BoundProgram program)
    {
        _ = Assert.Throws<ArgumentException>(() => SemanticVerifier.VerifyReferences(program));
        _ = Assert.Throws<ArgumentException>(() => NativeEmitter.EmitWithNativeImports(
            program, NativeRuntimePolicy.CheckedExceptions));
    }

    private static void AcceptPipelineForgery(BoundProgram program)
    {
        Assert.That(NativeDx11DirectPipelineContract.MatchesProgram(program), Is.True);
        SemanticVerifier.VerifyReferences(program);
        _ = NativeEmitter.EmitWithNativeImports(program, NativeRuntimePolicy.CheckedExceptions);
    }

    private static BoundBlock FindPipelineScope(BoundStatement statement) =>
        FindPipelineScopeOrNull(statement) ?? throw new InvalidOperationException("Sealed pipeline scope was not found.");

    private static (BoundBlock Block, int Index) FindShaderPathSetup(BoundStatement statement)
    {
        if (TryFindShaderPathSetup(statement, out var block, out var index))
        {
            return (block!, index);
        }
        throw new InvalidOperationException("Sealed shader-path setup was not found.");
    }

    private static bool TryFindShaderPathSetup(BoundStatement statement, out BoundBlock? block, out int index)
    {
        if (statement is BoundBlock candidate)
        {
            for (var current = 0; current < candidate.Statements.Length; current++)
            {
                if (candidate.Statements[current] is BoundLocal {
                    Initializer: BoundNativeFixedArrayInitialization {
                        Layout.ElementType: PrimitiveType.Char16,
                        Layout.ElementCount: NativeWindowsModulePathContract.Capacity,
                    },
                })
                {
                    block = candidate;
                    index = current;
                    return true;
                }
                if (TryFindShaderPathSetup(candidate.Statements[current], out block, out index))
                {
                    return true;
                }
            }
        }
        else if (statement is BoundIf branch)
        {
            if (TryFindShaderPathSetup(branch.Then, out block, out index)
                || (branch.Else is not null && TryFindShaderPathSetup(branch.Else, out block, out index)))
            {
                return true;
            }
        }
        else if (statement is BoundTryFinally cleanup)
        {
            if (TryFindShaderPathSetup(cleanup.Body, out block, out index)
                || TryFindShaderPathSetup(cleanup.Finally, out block, out index))
            {
                return true;
            }
        }
        else if (statement is BoundWhile loop
            && TryFindShaderPathSetup(loop.Body, out block, out index))
        {
            return true;
        }

        block = null;
        index = -1;
        return false;
    }

    private static BoundBlock? FindPipelineScopeOrNull(BoundStatement statement)
    {
        if (statement is BoundBlock { Statements: [BoundTryFinally scope] } block
            && scope.Finally.Statements is [BoundExpressionStatement { Expression: BoundCall { Function: 29 } }])
        {
            return block;
        }
        switch (statement)
        {
            case BoundBlock nested:
                foreach (var child in nested.Statements)
                {
                    if (FindPipelineScopeOrNull(child) is BoundBlock found)
                    {
                        return found;
                    }
                }
                break;
            case BoundTryFinally cleanup:
                return FindPipelineScopeOrNull(cleanup.Body) ?? FindPipelineScopeOrNull(cleanup.Finally);
            case BoundIf branch:
                return FindPipelineScopeOrNull(branch.Then);
            case BoundWhile loop:
                return FindPipelineScopeOrNull(loop.Body);
            default:
                break;
        }

        return null;
    }
}
