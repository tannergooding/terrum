using System.Buffers.Binary;
using System;
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
    private static string DirectWindowDevicePresentSource => File.ReadAllText(Path.Combine(
        FindRepositoryRoot(), "samples", "design", "win32-dx11-triangle", "window-device-present-stage.terrum"))
        .Replace("\r\n", "\n", StringComparison.Ordinal);

    private static BoundProgram CompileDirectWindowDevicePresent()
    {
        var compilation = Compilation.CreateWithNativeImports([SyntaxTree.Parse(DirectWindowDevicePresentSource)],
            runtimePolicy: BoundRuntimePolicy.CheckedExceptions);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var __nunitAssertValue0 = compilation.Program;
        Assert.That(__nunitAssertValue0, Is.TypeOf<BoundProgram>());
        var program = ((BoundProgram)__nunitAssertValue0);
        Assert.That(NativeDx11DirectPresentationContract.MatchesProgram(program), Is.True, "The direct presentation program must match its exact source-derived bound seal.");
        return program;
    }

    [Test]
    public void OrdinaryPresentMemberDoesNotRequireDirectPresentationGate()
    {
        const string source = """
            struct Display {
                int32 Value;
                public static int32 Present() { return 42; }
            }
            int32 Main() { return Display.Present(); }
            """;
        var tree = SyntaxTree.Parse(source);
        var compilations = new[] {
            Compilation.Create(tree, BoundRuntimePolicy.CheckedExceptions),
            Compilation.CreateWithNativeImports(tree, runtimePolicy: BoundRuntimePolicy.CheckedExceptions),
        };

        foreach (var compilation in compilations)
        {
            Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
            Assert.That(compilation.Program, Is.TypeOf<BoundProgram>());
        }
    }

    [Test]
    public void DirectWindowDevicePresentStageSealsSourceBoundProgramAndComAbi()
    {
        Assert.That(NativeDx11DirectPresentationContract.MatchesCatalog(NativeImportCatalog.WindowsX64), Is.True);
        var program = CompileDirectWindowDevicePresent();
        Assert.That(NativeDx11DirectPresentationContract.MatchesProgram(program), Is.True);
        Assert.That(NativeDx11WindowBufferIntegrationContract.MatchesProgram(program), Is.False);
        Assert.That(NativeDx11DirectBufferContract.MatchesProgram(program), Is.False);
        SemanticVerifier.VerifyReferences(program);

        Assert.That(NativeDx11DirectPresentationContract.GetBuffer.Slot, Is.EqualTo(9));
        Assert.That(NativeDx11DirectPresentationContract.CreateRenderTargetView.Slot, Is.EqualTo(9));
        Assert.That(NativeDx11DirectPresentationContract.Clear.Slot, Is.EqualTo(50));
        Assert.That(NativeDx11DirectPresentationContract.PresentFrame.Slot, Is.EqualTo(8));
        Assert.That(NativeDx11DirectPresentationContract.ReleaseTexture.Slot, Is.EqualTo(2));
        Assert.That(NativeDx11DirectPresentationContract.ReleaseRenderTargetView.Slot, Is.EqualTo(2));
        Assert.That(NativeDx11ComCatalog.Interfaces.Length, Is.EqualTo(16));
        Assert.That(NativeDx11ComCatalog.Interface(NativeDx11ComCatalog.SwapChain).FlattenedMethods.Length, Is.EqualTo(18));
        Assert.That(NativeDx11ComCatalog.Interface(NativeDx11ComCatalog.Device).FlattenedMethods.Length, Is.EqualTo(43));
        Assert.That(NativeDx11ComCatalog.Interface(NativeDx11ComCatalog.DeviceContext).FlattenedMethods.Length, Is.EqualTo(111));
        Assert.That(NativeDx11ComCatalog.Interface(NativeDx11ComCatalog.Texture2D).FlattenedMethods.Length, Is.EqualTo(11));
        Assert.That(NativeDx11ComCatalog.Interface(NativeDx11ComCatalog.RenderTargetView).FlattenedMethods.Length, Is.EqualTo(9));
        Assert.That(NativeDx11ComCatalog.Interface(NativeDx11ComCatalog.InputLayout).FlattenedMethods.Length, Is.EqualTo(7));
        Assert.That(NativeDx11ComCatalog.Interface(NativeDx11ComCatalog.InputLayout).SdkIid, Is.EqualTo(Guid.Parse("E4819DDC-4CF0-4025-BD26-5DE82A3E07B7")));
        Assert.That(NativeDx11ComCatalog.Interface(NativeDx11ComCatalog.InputLayout).ImmediateBaseIdentity, Is.EqualTo(NativeDx11ComCatalog.DeviceChild));
        Assert.That(NativeDx11ComCatalog.Interface(NativeDx11ComCatalog.InputLayout).FlattenedMethods[2], Is.EqualTo("IUnknown::Release"));
        Assert.That(NativeDx11ComCatalog.Interface(NativeDx11ComCatalog.VertexShader).FlattenedMethods.Length, Is.EqualTo(7));
        Assert.That(NativeDx11ComCatalog.Interface(NativeDx11ComCatalog.VertexShader).SdkIid, Is.EqualTo(Guid.Parse("3B301D64-D678-4289-8897-22F8928B72F3")));
        Assert.That(NativeDx11ComCatalog.Interface(NativeDx11ComCatalog.VertexShader).ImmediateBaseIdentity, Is.EqualTo(NativeDx11ComCatalog.DeviceChild));
        Assert.That(NativeDx11ComCatalog.Interface(NativeDx11ComCatalog.PixelShader).FlattenedMethods.Length, Is.EqualTo(7));
        Assert.That(NativeDx11ComCatalog.Interface(NativeDx11ComCatalog.PixelShader).SdkIid, Is.EqualTo(Guid.Parse("EA82E40D-51DC-4F33-93D4-DB7C9125AE8C")));
        Assert.That(NativeDx11ComCatalog.Interface(NativeDx11ComCatalog.PixelShader).ImmediateBaseIdentity, Is.EqualTo(NativeDx11ComCatalog.DeviceChild));
        Assert.That(NativeDx11ComCatalog.Interface(NativeDx11ComCatalog.Buffer).FlattenedMethods.Length, Is.EqualTo(11));
        Assert.That(NativeDx11ComCatalog.Interface(NativeDx11ComCatalog.Buffer).SdkIid, Is.EqualTo(Guid.Parse("48570B85-D1EE-4FCD-A250-EB350722B037")));
        Assert.That(NativeDx11ComCatalog.Interface(NativeDx11ComCatalog.Buffer).ImmediateBaseIdentity, Is.EqualTo(NativeDx11ComCatalog.Resource));
        Assert.That(NativeDx11ComCatalog.Interface(NativeDx11ComCatalog.Buffer).FlattenedMethods[10], Is.EqualTo("ID3D11Buffer::GetDesc"));
        Assert.That(NativeDx11ComCatalog.Interface(NativeDx11ComCatalog.Blob).FlattenedMethods.Length, Is.EqualTo(5));
        Assert.That(NativeDx11ComCatalog.Interface(NativeDx11ComCatalog.Blob).SdkIid, Is.EqualTo(Guid.Parse("8BA5FB08-5195-40E2-AC58-0D989C3A0102")));
        Assert.That(NativeDx11ComCatalog.Interface(NativeDx11ComCatalog.Blob).ImmediateBaseIdentity, Is.EqualTo(NativeDx11ComCatalog.Unknown));
        Assert.That(NativeDx11ComCatalog.Interface(NativeDx11ComCatalog.Blob).FlattenedMethods[3], Is.EqualTo(NativeDx11ComCatalog.BlobGetBufferPointerIdentity));
        Assert.That(NativeDx11ComCatalog.Interface(NativeDx11ComCatalog.Blob).FlattenedMethods[4], Is.EqualTo(NativeDx11ComCatalog.BlobGetBufferSizeIdentity));
        Assert.That(NativeDx11ComCatalog.Interface(NativeDx11ComCatalog.SwapChain).FlattenedMethods[9], Is.EqualTo(NativeDx11ComCatalog.GetBufferIdentity));
        Assert.That(NativeDx11ComCatalog.Interface(NativeDx11ComCatalog.SwapChain).FlattenedMethods[13], Is.EqualTo(NativeDx11ComCatalog.ResizeBuffersIdentity));
        Assert.That(NativeDx11ComCatalog.Interface(NativeDx11ComCatalog.Device).FlattenedMethods[9], Is.EqualTo(NativeDx11ComCatalog.CreateRenderTargetViewIdentity));
        Assert.That(NativeDx11ComCatalog.Interface(NativeDx11ComCatalog.DeviceContext).FlattenedMethods[50], Is.EqualTo(NativeDx11ComCatalog.ClearRenderTargetViewIdentity));
        Assert.That(NativeDx11ComCatalog.Interface(NativeDx11ComCatalog.DeviceContext).FlattenedMethods[33], Is.EqualTo(NativeDx11ComCatalog.OMSetRenderTargetsIdentity));
        Assert.That(NativeDx11ComCatalog.Interface(NativeDx11ComCatalog.DeviceContext).FlattenedMethods[24], Is.EqualTo(NativeDx11ComCatalog.IASetPrimitiveTopologyIdentity));
        Assert.That(NativeDx11ComCatalog.Interface(NativeDx11ComCatalog.DeviceContext).FlattenedMethods[17], Is.EqualTo(NativeDx11ComCatalog.IASetInputLayoutIdentity));
        Assert.That(NativeDx11ComCatalog.Interface(NativeDx11ComCatalog.DeviceContext).FlattenedMethods[18], Is.EqualTo(NativeDx11ComCatalog.IASetVertexBuffersIdentity));
        Assert.That(NativeDx11ComCatalog.Interface(NativeDx11ComCatalog.DeviceContext).FlattenedMethods[11], Is.EqualTo(NativeDx11ComCatalog.VSSetShaderIdentity));
        Assert.That(NativeDx11ComCatalog.Interface(NativeDx11ComCatalog.DeviceContext).FlattenedMethods[9], Is.EqualTo(NativeDx11ComCatalog.PSSetShaderIdentity));
        Assert.That(NativeDx11ComCatalog.Interface(NativeDx11ComCatalog.DeviceContext).FlattenedMethods[13], Is.EqualTo(NativeDx11ComCatalog.DrawIdentity));
        Assert.That(NativeDx11ComCatalog.Interface(NativeDx11ComCatalog.DeviceContext).FlattenedMethods[44], Is.EqualTo(NativeDx11ComCatalog.RSSetViewportsIdentity));
        Assert.That(NativeDx11ComCatalog.Interface(NativeDx11ComCatalog.DeviceContext).FlattenedMethods[110], Is.EqualTo(NativeDx11ComCatalog.ClearStateIdentity));
        Assert.That(NativeDx11ComCatalog.IsPermittedBaseEdge(
            NativeDx11ComCatalog.Texture2D, NativeDx11ComCatalog.Resource), Is.True);
        Assert.That(NativeDx11ComCatalog.Methods.Count(static method => method.IsCallable), Is.EqualTo(37));
        foreach (var (identity, signature) in new[] {
            (NativeDx11ComCatalog.ReleaseDeviceIdentity, "ULONG(ID3D11Device*)"),
            (NativeDx11ComCatalog.ReleaseSwapChainIdentity, "ULONG(IDXGISwapChain*)"),
            (NativeDx11ComCatalog.ReleaseContextIdentity, "ULONG(ID3D11DeviceContext*)"),
        })
        {
            var release = NativeDx11ComCatalog.Method(identity);
            Assert.That(release.Slot, Is.EqualTo(2));
            Assert.That(release.Signature, Is.EqualTo(signature));
            Assert.That(release.Effects, Is.EqualTo(NativeComEffects.ReleasesReference));
        }
        var resizeBuffers = NativeDx11ComCatalog.Method(NativeDx11ComCatalog.ResizeBuffersIdentity);
        Assert.That(resizeBuffers.Slot, Is.EqualTo(13));
        Assert.That(resizeBuffers.Signature, Is.EqualTo("HRESULT(IDXGISwapChain*,UINT,UINT,UINT,DXGI_FORMAT,UINT)"));
        Assert.That(resizeBuffers.Abi, Is.EqualTo("windows-x64-rcx-edx-r8d-r9d-stack32-stack40-eax"));
        Assert.That(resizeBuffers.Effects, Is.EqualTo(NativeComEffects.SignedHResult));
        var bufferOwner = NativeDx11ComCatalog.Owner(NativeDx11DirectBufferContract.TypeName);
        Assert.That(bufferOwner.InterfaceIdentity, Is.EqualTo(NativeDx11ComCatalog.Buffer));
        Assert.That(bufferOwner.AcquisitionKind, Is.EqualTo(NativeComOwnerAcquisitionKind.ComMethodOutput));
        Assert.That(bufferOwner.AcquisitionIdentity, Is.EqualTo(NativeDx11ComCatalog.CreateBufferIdentity));
        Assert.That(bufferOwner.OutputParameterIndex, Is.EqualTo(3));
        Assert.That(bufferOwner.IsUnique, Is.True);
        Assert.That(bufferOwner.CellSize, Is.EqualTo(8));
        Assert.That(bufferOwner.CellAlignment, Is.EqualTo(8));
        Assert.That(NativeDx11DirectBufferContract.CreateBufferCom.Slot, Is.EqualTo(3));
        Assert.That(NativeDx11DirectBufferContract.CreateBufferCom.Signature, Is.EqualTo("HRESULT(ID3D11Device*,const D3D11_BUFFER_DESC*,const D3D11_SUBRESOURCE_DATA*,ID3D11Buffer**)"));
        Assert.That(NativeDx11DirectBufferContract.CreateBufferCom.Abi, Is.EqualTo("windows-x64-rcx-rdx-r8-r9-eax"));
        Assert.That(NativeDx11DirectBufferContract.ReleaseCom.Slot, Is.EqualTo(2));
        var inputLayoutOwner = NativeDx11ComCatalog.Owner(NativeDx11DirectShaderContract.InputLayoutName);
        Assert.That(inputLayoutOwner.InterfaceIdentity, Is.EqualTo(NativeDx11ComCatalog.InputLayout));
        Assert.That(inputLayoutOwner.AcquisitionKind, Is.EqualTo(NativeComOwnerAcquisitionKind.ComMethodOutput));
        Assert.That(inputLayoutOwner.AcquisitionIdentity, Is.EqualTo(NativeDx11ComCatalog.CreateInputLayoutIdentity));
        Assert.That(inputLayoutOwner.OutputParameterIndex, Is.EqualTo(5));
        Assert.That(inputLayoutOwner.IsUnique, Is.True);
        Assert.That(inputLayoutOwner.CellSize, Is.EqualTo(8));
        Assert.That(inputLayoutOwner.CellAlignment, Is.EqualTo(8));
        Assert.That(NativeDx11DirectShaderContract.CreateInputLayoutCom.Signature, Is.EqualTo("HRESULT(ID3D11Device*,const D3D11_INPUT_ELEMENT_DESC*,UINT,const void*,SIZE_T,ID3D11InputLayout**)"));
        Assert.That(NativeDx11DirectShaderContract.CreateInputLayoutCom.Abi, Is.EqualTo("windows-x64-rcx-rdx-r8d-r9-stack32-stack40-eax"));
        Assert.That(NativeDx11DirectShaderContract.CreateInputLayoutCom.Effects, Is.EqualTo(NativeComEffects.SignedHResult | NativeComEffects.AcquiresReference
            | NativeComEffects.OutputMayBeWrittenOnFailure));
        Assert.That(NativeDx11DirectShaderContract.CreateInputLayoutCom.Slot, Is.EqualTo(11));
        Assert.That(NativeDx11DirectShaderContract.CreateInputLayoutCom.OutputParameterIndex, Is.EqualTo(5));
        Assert.That(NativeDx11DirectShaderContract.CreateInputLayoutCom.OutputInterfaceIdentity, Is.EqualTo(NativeDx11ComCatalog.InputLayout));
        Assert.That(NativeDx11DirectShaderContract.ReleaseInputLayoutCom.Slot, Is.EqualTo(2));
        foreach (var (ownerName, interfaceName, identity) in new[] {
            (NativeDx11DirectShaderContract.VertexShaderName, NativeDx11ComCatalog.VertexShader,
                NativeDx11ComCatalog.CreateVertexShaderIdentity),
            (NativeDx11DirectShaderContract.PixelShaderName, NativeDx11ComCatalog.PixelShader,
                NativeDx11ComCatalog.CreatePixelShaderIdentity),
        })
        {
            var shaderOwner = NativeDx11ComCatalog.Owner(ownerName);
            Assert.That(shaderOwner.InterfaceIdentity, Is.EqualTo(interfaceName));
            Assert.That(shaderOwner.AcquisitionKind, Is.EqualTo(NativeComOwnerAcquisitionKind.ComMethodOutput));
            Assert.That(shaderOwner.AcquisitionIdentity, Is.EqualTo(identity));
            Assert.That(shaderOwner.OutputParameterIndex, Is.EqualTo(4));
            Assert.That(shaderOwner.IsUnique, Is.True);
            Assert.That(shaderOwner.CellSize, Is.EqualTo(8));
            Assert.That(shaderOwner.CellAlignment, Is.EqualTo(8));
        }
        Assert.That(NativeDx11DirectShaderContract.CreateVertexCom.Signature, Is.EqualTo("HRESULT(ID3D11Device*,const void*,SIZE_T,ID3D11ClassLinkage*,ID3D11VertexShader**)"));
        Assert.That(NativeDx11DirectShaderContract.CreateVertexCom.Slot, Is.EqualTo(12));
        Assert.That(NativeDx11DirectShaderContract.CreatePixelCom.Signature, Is.EqualTo("HRESULT(ID3D11Device*,const void*,SIZE_T,ID3D11ClassLinkage*,ID3D11PixelShader**)"));
        Assert.That(NativeDx11DirectShaderContract.CreatePixelCom.Slot, Is.EqualTo(15));
        Assert.That(NativeDx11DirectShaderContract.ReleaseVertexCom.Slot, Is.EqualTo(2));
        Assert.That(NativeDx11DirectShaderContract.ReleasePixelCom.Slot, Is.EqualTo(2));
        var codeOwner = NativeDx11ComCatalog.Owner(NativeDx11DirectShaderContract.CodeName);
        Assert.That(codeOwner.InterfaceIdentity, Is.EqualTo(NativeDx11ComCatalog.Blob));
        Assert.That(codeOwner.AcquisitionKind, Is.EqualTo(NativeComOwnerAcquisitionKind.NativeImportOutput));
        Assert.That(codeOwner.AcquisitionIdentity, Is.EqualTo(NativeDx11DirectShaderContract.CompileKey));
        Assert.That(codeOwner.OutputParameterIndex, Is.EqualTo(7));
        Assert.That(codeOwner.IsUnique, Is.True);
        Assert.That(codeOwner.CellSize, Is.EqualTo(8));
        Assert.That(codeOwner.CellAlignment, Is.EqualTo(8));
        Assert.That(NativeDx11DirectShaderContract.AddRefCodeCom.Slot, Is.EqualTo(1));
        Assert.That(NativeDx11DirectShaderContract.ReleaseCodeCom.Slot, Is.EqualTo(2));
        Assert.That(NativeDx11DirectShaderContract.GetCodePointerCom.Slot, Is.EqualTo(3));
        Assert.That(NativeDx11DirectShaderContract.GetCodeSizeCom.Slot, Is.EqualTo(4));
        var contextOwner = NativeDx11ComCatalog.Owner(NativeDx11WarpAcquisitionContract.ContextName);
        Assert.That(contextOwner.InterfaceIdentity, Is.EqualTo(NativeDx11ComCatalog.DeviceContext));
        Assert.That(contextOwner.AcquisitionKind, Is.EqualTo(NativeComOwnerAcquisitionKind.NativeImportOutput));
        Assert.That(contextOwner.AcquisitionIdentity, Is.EqualTo(NativeDx11WarpAcquisitionContract.ImportKey));
        Assert.That(contextOwner.OutputParameterIndex, Is.EqualTo(11));
        Assert.That(contextOwner.IsUnique, Is.True);
        Assert.That(contextOwner.CellSize, Is.EqualTo(8));
        Assert.That(contextOwner.CellAlignment, Is.EqualTo(8));
        Assert.That(NativeDx11DirectPresentationContract.GetBuffer.OutputInterfaceIdentity, Is.EqualTo(NativeDx11ComCatalog.Texture2D));
        Assert.That(NativeDx11DirectPresentationContract.GetBuffer.Signature, Is.EqualTo("HRESULT(IDXGISwapChain*,UINT,REFIID,void**)"));
        Assert.That(NativeDx11DirectPresentationContract.CreateRenderTargetView.Signature, Is.EqualTo("HRESULT(ID3D11Device*,ID3D11Resource*,const D3D11_RENDER_TARGET_VIEW_DESC*,ID3D11RenderTargetView**)"));
        Assert.That(NativeDx11DirectPresentationContract.ReleaseTexture.Signature, Is.EqualTo("ULONG(ID3D11Texture2D*)"));
        Assert.That(NativeDx11DirectPresentationContract.ReleaseRenderTargetView.Signature, Is.EqualTo("ULONG(ID3D11RenderTargetView*)"));
        Assert.That(NativeDx11DirectPresentationContract.PresentFrame.Identity, Is.EqualTo(NativeDx11ComCatalog.PresentIdentity));
        Assert.That(NativeDx11DirectPresentationContract.PresentFrame.Signature, Is.EqualTo("HRESULT(IDXGISwapChain*,UINT,UINT)"));
        Assert.That(NativeDx11DirectPresentationContract.PresentFrame.Abi, Is.EqualTo("windows-x64-rcx-edx-r8d-eax"));
        Assert.That(NativeDx11DirectPresentationContract.PresentFrame.Effects, Is.EqualTo(NativeComEffects.SignedHResult));
        Assert.That(NativeDx11DirectPresentationContract.PresentFrame.OutputParameterIndex, Is.EqualTo(-1));
        Assert.That(NativeDx11DirectPresentationContract.PresentFrame.OutputInterfaceIdentity, Is.Null);
        Assert.That(NativeDx11DirectPresentationContract.PresentFrame.ParameterAdaptations.IsDefaultOrEmpty, Is.True);
        Assert.That(NativeDx11DirectPresentationContract.Clear.Identity, Is.EqualTo("ID3D11DeviceContext::ClearRenderTargetView"));
        Assert.That(NativeDx11DirectPresentationContract.Clear.OwnerInterfaceIdentity, Is.EqualTo("ID3D11DeviceContext"));
        Assert.That(NativeDx11DirectPresentationContract.Clear.DeclaringInterfaceIdentity, Is.EqualTo("ID3D11DeviceContext"));
        Assert.That(NativeDx11DirectPresentationContract.Clear.Signature, Is.EqualTo("void(ID3D11DeviceContext*,ID3D11RenderTargetView*,const FLOAT[4])"));
        Assert.That(NativeDx11DirectPresentationContract.Clear.Abi, Is.EqualTo("windows-x64-rcx-rdx-r8-disp32"));
        Assert.That(NativeDx11DirectPresentationContract.Clear.Effects, Is.EqualTo(NativeComEffects.None));
        var __nunitAssertValue1 = global::System.Linq.Enumerable.ToArray(global::System.Linq.Enumerable.Where(program.Functions, static function => function.NativeComMethod?.Identity == NativeDx11ComCatalog.GetBufferIdentity));
        Assert.That(__nunitAssertValue1, Has.Exactly(1).Items);
        var getBufferFunction = __nunitAssertValue1[0];
        Assert.That(getBufferFunction.Intrinsic, Is.EqualTo(BoundIntrinsic.NativeComVTableCall));
        Assert.That(getBufferFunction.NativeComMethod, Is.SameAs(NativeDx11DirectPresentationContract.GetBuffer));
        Assert.That(getBufferFunction.NativeComHiddenParameters.ToArray(), Is.EqualTo([new NativeComHiddenParameter(1, NativeComHiddenParameterKind.UInt32),
                new NativeComHiddenParameter(2, NativeComHiddenParameterKind.InterfaceIdAddress,
                    InterfaceIdentity: NativeDx11ComCatalog.Texture2D)]));
        var __nunitAssertValue2 = global::System.Linq.Enumerable.ToArray(global::System.Linq.Enumerable.Where(program.Functions, static function => function.NativeComMethod?.Identity
                        == NativeDx11ComCatalog.CreateRenderTargetViewIdentity));
        Assert.That(__nunitAssertValue2, Has.Exactly(1).Items);
        var createViewFunction = __nunitAssertValue2[0];
        Assert.That(createViewFunction.Intrinsic, Is.EqualTo(BoundIntrinsic.NativeComVTableCall));
        Assert.That(createViewFunction.NativeComMethod, Is.SameAs(NativeDx11DirectPresentationContract.CreateRenderTargetView));
        Assert.That(createViewFunction.NativeComHiddenParameters.ToArray(), Is.EqualTo([new NativeComHiddenParameter(2, NativeComHiddenParameterKind.NullPointer)]));
        var __nunitAssertValue3 = global::System.Linq.Enumerable.ToArray(global::System.Linq.Enumerable.Where(program.Functions, static function => function.NativeComMethod?.Identity == NativeDx11ComCatalog.PresentIdentity));
        Assert.That(__nunitAssertValue3, Has.Exactly(1).Items);
        var presentFunction = __nunitAssertValue3[0];
        Assert.That(presentFunction.Intrinsic, Is.EqualTo(BoundIntrinsic.NativeComVTableCall));
        Assert.That(presentFunction.NativeComMethod, Is.SameAs(NativeDx11DirectPresentationContract.PresentFrame));
        Assert.That(presentFunction.NativeComHiddenParameters.ToArray(), Is.EqualTo([
            new NativeComHiddenParameter(1, NativeComHiddenParameterKind.UInt32, 1),
            new NativeComHiddenParameter(2, NativeComHiddenParameterKind.UInt32),
        ]));
        var __nunitAssertValue4 = global::System.Linq.Enumerable.ToArray(global::System.Linq.Enumerable.Where(program.Functions, static function => function.NativeComMethod?.Identity
                        == NativeDx11ComCatalog.ClearRenderTargetViewIdentity));
        Assert.That(__nunitAssertValue4, Has.Exactly(1).Items);
        var clearFunction = __nunitAssertValue4[0];
        Assert.That(clearFunction.Intrinsic, Is.EqualTo(BoundIntrinsic.NativeComVTableCall));
        Assert.That(clearFunction.NativeComMethod, Is.SameAs(NativeDx11DirectPresentationContract.Clear));
        Assert.That(clearFunction.NativeComParameterMappings.ToArray(), Is.EqualTo([
            new NativeComBoundParameterMapping(2, 2),
            new NativeComBoundParameterMapping(3, 2),
            new NativeComBoundParameterMapping(4, 2),
            new NativeComBoundParameterMapping(5, 2),
        ]));

        var image = NativeEmitter.EmitWithNativeImports(program, NativeRuntimePolicy.CheckedExceptions);
        Assert.That(image.AsSpan().IndexOf("D3D11CreateDeviceAndSwapChain\0"u8) >= 0, Is.True);
        Assert.That(image.AsSpan().IndexOf("GetClientRect\0"u8) >= 0, Is.True);
        Assert.That(image.AsSpan().IndexOf("NativeFfi"u8) < 0, Is.True);
        Assert.That(ContainsSequence(image, [0xFF, 0x90, 0x90, 0x01, 0x00, 0x00]), Is.True, "The catalogued ClearRenderTargetView slot 50 must use byte offset 400.");
        Assert.That(ContainsSequence(image, [0xFF, 0x50, 0x48]), Is.True, "GetBuffer and CreateRenderTargetView slot 9 must use byte offset 72.");
        Assert.That(ContainsSequence(image, [0xFF, 0x50, 0x40]), Is.True, "The catalogued Present slot 8 must use byte offset 64.");
        Assert.That(ContainsStoredImmediateSequence(image,
            [0x6F15AAF2, 0x4E89D208, 0x9548B49A, 0x9C4FD335]), Is.True, "GetBuffer must request IID_ID3D11Texture2D in Windows GUID byte order.");
        Assert.That(ContainsInstructionSequence(image,
            ([0x8B, 0x85], 4), ([0x85, 0xC0], 0), ([0x0F, 0x8E], 4), ([0x89, 0x85], 4),
            ([0x8B, 0x85], 4), ([0x85, 0xC0], 0), ([0x0F, 0x8E], 4), ([0x89, 0x85], 4)), Is.True, "The validated RECT right/bottom dimensions must be copied to the swap-chain descriptor.");
    }

    [TestCase("trycreatewarpswapchain(Handle, out var device, out var swapchain, out var context)",
        "trycreatewarpswapchain(Handle, out var device, out var context, out var swapchain)")]
    [TestCase("ShowWindow(Handle, 5);", "ShowWindow(Handle, 0);")]
    [TestCase("context.ClearRenderTargetView(renderTarget, 0.08, 0.12, 0.18, 1.0);",
        "context.ClearRenderTargetView(renderTarget, 0.18, 0.12, 0.08, 1.0);")]
    [TestCase("if (swapchain.Present())", "if (!swapchain.Present())")]
    [TestCase("private Terrum.Interop.Windows.HWND Handle;",
        "public Terrum.Interop.Windows.HWND Handle;")]
    public void DirectWindowDevicePresentStageRejectsSourceMutation(string original, string replacement)
    {
        var source = DirectWindowDevicePresentSource;
        Assert.That(source.Contains(original, StringComparison.Ordinal), Is.True);
        var mutated = source.Replace(original, replacement, StringComparison.Ordinal);
        var compilation = Compilation.CreateWithNativeImports([SyntaxTree.Parse(mutated)],
            runtimePolicy: BoundRuntimePolicy.CheckedExceptions);
        Assert.That(compilation.Program, Is.Null);
        Assert.That(compilation.HasErrors, Is.True, string.Join(Environment.NewLine, compilation.Diagnostics));
    }

    [Test]
    public void DirectWindowDevicePresentStageRejectsForgedBoundSlotAndOwnerContract()
    {
        var program = CompileDirectWindowDevicePresent();
        var index = Enumerable.Range(0, program.Functions.Length).Single(
            item => program.Functions[item].NativeComMethod?.Identity
                == NativeDx11ComCatalog.ClearRenderTargetViewIdentity);
        var clear = program.Functions[index];
        var forged = program with {
            Functions = program.Functions.SetItem(index, clear with {
                NativeComMethod = NativeDx11DirectPresentationContract.Clear with { Slot = 49 },
            }),
        };
        Assert.That(NativeDx11DirectPresentationContract.MatchesProgram(forged), Is.False);
        Assert.Throws<ArgumentException>(() => SemanticVerifier.VerifyReferences(forged));

        var forgedOwner = program with {
            Structures = program.Structures.SetItem(7, program.Structures[7] with { IsUnique = false }),
        };
        Assert.That(NativeDx11DirectPresentationContract.MatchesProgram(forgedOwner), Is.False);
        Assert.Throws<ArgumentException>(() => SemanticVerifier.VerifyReferences(forgedOwner));

        foreach (var forgedClear in new[] {
            program with { Functions = program.Functions.SetItem(index, clear with { NativeComMethod = null }) },
            program with { Functions = program.Functions.SetItem(index, clear with {
                NativeComMethod = NativeDx11DirectPresentationContract.Clear with { Effects = NativeComEffects.SignedHResult },
            }) },
            program with { Functions = program.Functions.SetItem(index, clear with {
                NativeComMethod = NativeDx11DirectPresentationContract.Clear with {
                    Signature = "void(ID3D11DeviceContext*,void*,const FLOAT*)",
                },
            }) },
        })
        {
            Assert.That(NativeDx11DirectPresentationContract.MatchesProgram(forgedClear), Is.False);
            Assert.Throws<ArgumentException>(() => SemanticVerifier.VerifyReferences(forgedClear));
            Assert.Throws<ArgumentException>(() =>
                NativeEmitter.EmitWithNativeImports(forgedClear, NativeRuntimePolicy.CheckedExceptions));
        }

        var releaseIndex = Enumerable.Range(0, program.Functions.Length).Single(
            item => program.Functions[item].Name == NativeDx11DirectPresentationContract.TextureTypeName + ".Close"
                && program.Functions[item].Intrinsic == BoundIntrinsic.NativeComOwnerClose);
        var release = program.Functions[releaseIndex];
        var forgedRelease = program with {
            Functions = program.Functions.SetItem(releaseIndex, release with {
                NativeComMethod =
                    NativeDx11DirectPresentationContract.ReleaseTexture with { Slot = 3 },
            }),
        };
        Assert.That(NativeDx11DirectPresentationContract.MatchesProgram(forgedRelease), Is.False);
        Assert.Throws<ArgumentException>(() => SemanticVerifier.VerifyReferences(forgedRelease));

        var presentIndex = Enumerable.Range(0, program.Functions.Length).Single(
            item => program.Functions[item].NativeComMethod?.Identity == NativeDx11ComCatalog.PresentIdentity);
        var present = program.Functions[presentIndex];
        foreach (var forgedPresent in new[] {
            program with { Functions = program.Functions.SetItem(presentIndex, present with { NativeComMethod = null }) },
            program with { Functions = program.Functions.SetItem(presentIndex, present with {
                NativeComMethod = NativeDx11DirectPresentationContract.PresentFrame with { Slot = 7 },
            }) },
            program with { Functions = program.Functions.SetItem(presentIndex, present with {
                NativeComMethod = NativeDx11DirectPresentationContract.PresentFrame with {
                    Effects = NativeComEffects.None,
                },
            }) },
            program with { Functions = program.Functions.SetItem(presentIndex, present with {
                NativeComMethod = NativeDx11DirectPresentationContract.PresentFrame with {
                    Signature = "HRESULT(void*)",
                },
            }) },
        })
        {
            Assert.That(NativeDx11DirectPresentationContract.MatchesProgram(forgedPresent), Is.False);
            Assert.Throws<ArgumentException>(() => SemanticVerifier.VerifyReferences(forgedPresent));
            Assert.Throws<ArgumentException>(() =>
                NativeEmitter.EmitWithNativeImports(forgedPresent, NativeRuntimePolicy.CheckedExceptions));
        }
    }

    [Test]
    public void DirectWindowDevicePresentStageRejectsForgedComIdentitySignatureProvenanceAndLoans()
    {
        var program = CompileDirectWindowDevicePresent();
        var getIndex = Enumerable.Range(0, program.Functions.Length).Single(
            item => program.Functions[item].NativeComMethod?.Identity
                == NativeDx11ComCatalog.GetBufferIdentity);
        var get = program.Functions[getIndex];
        var rejectedPrograms = new[] {
            program with { Functions = program.Functions.SetItem(getIndex, get with { NativeComMethod = null }) },
            program with { Functions = program.Functions.SetItem(getIndex, get with {
                NativeComMethod = NativeDx11DirectPresentationContract.CreateRenderTargetView,
            }) },
            program with { Functions = program.Functions.SetItem(getIndex, get with {
                NativeComMethod = NativeDx11DirectPresentationContract.GetBuffer with {
                    Identity = "IDXGIObject::GetBuffer",
                },
            }) },
            program with { Functions = program.Functions.SetItem(getIndex, get with {
                NativeComMethod = NativeDx11DirectPresentationContract.GetBuffer with {
                    Signature = "HRESULT(void**)",
                },
            }) },
            program with { Functions = program.Functions.SetItem(getIndex, get with {
                NativeComMethod = NativeDx11DirectPresentationContract.GetBuffer with {
                    OutputInterfaceIdentity = NativeDx11ComCatalog.Resource,
                },
            }) },
            program with { Functions = program.Functions.SetItem(getIndex, get with {
                NativeComMethod = NativeDx11DirectPresentationContract.GetBuffer with {
                    Effects = NativeComEffects.None,
                },
            }) },
        };
        foreach (var forged in rejectedPrograms)
        {
            Assert.That(NativeDx11DirectPresentationContract.MatchesProgram(forged), Is.False);
            Assert.Throws<ArgumentException>(() => SemanticVerifier.VerifyReferences(forged));
            Assert.Throws<ArgumentException>(() =>
                NativeEmitter.EmitWithNativeImports(forged, NativeRuntimePolicy.CheckedExceptions));
        }

        var createIndex = Enumerable.Range(0, program.Functions.Length).Single(
            item => program.Functions[item].NativeComMethod?.Identity
                == NativeDx11ComCatalog.CreateRenderTargetViewIdentity);
        var create = program.Functions[createIndex];
        foreach (var forged in new[] {
            program with { Functions = program.Functions.SetItem(createIndex, create with { NativeComMethod = null }) },
            program with { Functions = program.Functions.SetItem(createIndex, create with {
                NativeComMethod = NativeDx11DirectPresentationContract.CreateRenderTargetView with {
                    ParameterAdaptations = [],
                },
            }) },
        })
        {
            Assert.That(NativeDx11DirectPresentationContract.MatchesProgram(forged), Is.False);
            Assert.Throws<ArgumentException>(() => SemanticVerifier.VerifyReferences(forged));
        }

        static NativeImportCatalog CreateComCatalog(
            System.Collections.Immutable.ImmutableArray<NativeComInterfaceDefinition> interfaces,
            System.Collections.Immutable.ImmutableArray<NativeComOwnerDefinition> owners,
            System.Collections.Immutable.ImmutableArray<NativeComMethodDefinition> methods,
            System.Collections.Immutable.ImmutableArray<(string Source, string Target)> edges,
            string identity = NativeWindowCreationContract.CatalogIdentity,
            bool isForTesting = false)
        {
            return new NativeImportCatalog(identity, NativeImportCatalog.WindowsX64.ImportDefinitions,
                isForTesting, comInterfaces: interfaces, comOwners: owners, comMethods: methods,
                comBaseEdges: edges, comSignatureTypes: NativeDx11ComCatalog.SignatureTypes,
                nativeStorageRecordLayouts: NativeDx11BufferContract.SampleArrayStorageRecordLayouts);
        }

        var productionComCatalog = CreateComCatalog(NativeDx11ComCatalog.Interfaces,
            NativeDx11ComCatalog.Owners, NativeDx11ComCatalog.Methods,
            NativeDx11ComCatalog.PermittedBaseEdges);
        Assert.That(NativeDx11ComCatalog.MatchesCatalog(productionComCatalog), Is.True);
        Assert.That(productionComCatalog.Identity, Is.EqualTo(NativeWindowCreationContract.CatalogIdentity));
        Assert.That(productionComCatalog.ComInterfaces, Is.EqualTo(NativeDx11ComCatalog.Interfaces));
        Assert.That(productionComCatalog.ComOwners, Is.EqualTo(NativeDx11ComCatalog.Owners));
        Assert.That(productionComCatalog.ComMethods, Is.EqualTo(NativeDx11ComCatalog.Methods));
        Assert.That(productionComCatalog.ComBaseEdges, Is.EqualTo(NativeDx11ComCatalog.PermittedBaseEdges));
        Assert.That(productionComCatalog.ComSignatureTypes, Is.EqualTo(NativeDx11ComCatalog.SignatureTypes));

        _ = Assert.Throws<ArgumentException>(() => CreateComCatalog(NativeDx11ComCatalog.Interfaces,
            NativeDx11ComCatalog.Owners, NativeDx11ComCatalog.Methods,
            NativeDx11ComCatalog.PermittedBaseEdges, isForTesting: true));
        _ = Assert.Throws<ArgumentException>(() => CreateComCatalog(NativeDx11ComCatalog.Interfaces,
            NativeDx11ComCatalog.Owners, NativeDx11ComCatalog.Methods,
            NativeDx11ComCatalog.PermittedBaseEdges,
            identity: NativeDx11WarpOutputContract.CatalogIdentity));

        static void RejectComCatalog(
            System.Collections.Immutable.ImmutableArray<NativeComInterfaceDefinition> interfaces,
            System.Collections.Immutable.ImmutableArray<NativeComOwnerDefinition> owners,
            System.Collections.Immutable.ImmutableArray<NativeComMethodDefinition> methods,
            System.Collections.Immutable.ImmutableArray<(string Source, string Target)> edges)
        {
            _ = Assert.Throws<ArgumentException>(() => CreateComCatalog(interfaces, owners, methods, edges));
        }

        RejectComCatalog(NativeDx11ComCatalog.Interfaces.SetItem(0,
            NativeDx11ComCatalog.Interfaces[0] with { SdkIid = Guid.Empty }),
            NativeDx11ComCatalog.Owners, NativeDx11ComCatalog.Methods,
            NativeDx11ComCatalog.PermittedBaseEdges);
        RejectComCatalog(NativeDx11ComCatalog.Interfaces.SetItem(2,
            NativeDx11ComCatalog.Interfaces[2] with { ImmediateBaseIdentity = NativeDx11ComCatalog.Unknown }),
            NativeDx11ComCatalog.Owners, NativeDx11ComCatalog.Methods,
            NativeDx11ComCatalog.PermittedBaseEdges);
        RejectComCatalog(NativeDx11ComCatalog.Interfaces, NativeDx11ComCatalog.Owners,
            NativeDx11ComCatalog.Methods.SetItem(0,
                NativeDx11ComCatalog.Methods[0] with { Slot = 8 }),
            NativeDx11ComCatalog.PermittedBaseEdges);
        RejectComCatalog(NativeDx11ComCatalog.Interfaces, NativeDx11ComCatalog.Owners,
            NativeDx11ComCatalog.Methods.SetItem(0,
                NativeDx11ComCatalog.Methods[0] with { Signature = "HRESULT(void**)" }),
            NativeDx11ComCatalog.PermittedBaseEdges);
        RejectComCatalog(NativeDx11ComCatalog.Interfaces,
            NativeDx11ComCatalog.Owners.SetItem(2,
                NativeDx11ComCatalog.Owners[2] with { AcquisitionIdentity = "IDXGISwapChain::Present" }),
            NativeDx11ComCatalog.Methods, NativeDx11ComCatalog.PermittedBaseEdges);
        RejectComCatalog(NativeDx11ComCatalog.Interfaces, NativeDx11ComCatalog.Owners,
            NativeDx11ComCatalog.Methods,
            [("ID3D11Texture2D", "ID3D11DeviceChild")]);

        var createLoanForgery = RewriteDirectPresentationCall(program, createIndex, call =>
            call with { Arguments = call.Arguments.SetItem(1, new BoundReceiverLoan(2, (PrimitiveType)101, default)) });
        Assert.That(NativeDx11DirectPresentationContract.MatchesProgram(createLoanForgery), Is.False);
        Assert.Throws<ArgumentException>(() => SemanticVerifier.VerifyReferences(createLoanForgery));
        var outputForgery = RewriteDirectPresentationCall(program, getIndex, call => {
            var output = call.Arguments[1];
            Assert.That(output, Is.TypeOf<BoundOutArgument>());
            return call with {
                Arguments = call.Arguments.SetItem(1,
                    ((BoundOutArgument)output) with { Variable = 17 }),
            };
        });
        Assert.That(NativeDx11DirectPresentationContract.MatchesProgram(outputForgery), Is.False);
        Assert.Throws<ArgumentException>(() => SemanticVerifier.VerifyReferences(outputForgery));
    }

    [Test]
    public void DirectWindowDevicePresentStageIsUnsupportedByJitTrRmAndModules()
    {
        var program = CompileDirectWindowDevicePresent();
        Assert.Throws<NotSupportedException>(() => NativeEmitter.EmitCode(
            program, NativeRuntimePolicy.CheckedExceptions));
        if (OperatingSystem.IsWindows() && RuntimeInformation.ProcessArchitecture == Architecture.X64)
        {
            Assert.Throws<NotSupportedException>(() =>
                NativeJit.Execute(program, NativeRuntimePolicy.CheckedExceptions));
        }
        else
        {
            Assert.Throws<PlatformNotSupportedException>(() =>
                NativeJit.Execute(program, NativeRuntimePolicy.CheckedExceptions));
        }
        Assert.Throws<NotSupportedException>(() => TrRmExecutableCompiler.Emit(program, "dx11-com-test"));
        Assert.Throws<NotSupportedException>(() => NativeModuleCompiler.CreateProducer(program,
            ["Triangle.Interop.Main"], "dx11_com_test", NativeRuntimePolicy.CheckedExceptions));
    }

    private static BoundProgram RewriteDirectPresentationCall(
        BoundProgram program, int targetFunction, Func<BoundCall, BoundCall> rewrite)
    {
        var runIndex = Enumerable.Range(0, program.Functions.Length).Single(index =>
            program.Functions[index].Name == NativeWindowOwnerContract.RunName);
        var run = program.Functions[runIndex];
        var replaced = false;
        var result = program with {
            Functions = program.Functions.SetItem(runIndex, run with {
                Body = (BoundBlock)RewriteDirectPresentationStatement(run.Body),
            }),
        };
        Assert.That(replaced, Is.True, $"No Run call targeted function {targetFunction}.");
        return result;

        BoundStatement RewriteDirectPresentationStatement(BoundStatement statement) => statement switch {
            BoundBlock block => block with {
                Statements = [.. block.Statements.Select(RewriteDirectPresentationStatement)],
            },
            BoundIf branch => branch with {
                Condition = RewriteDirectPresentationExpression(branch.Condition),
                Then = RewriteDirectPresentationStatement(branch.Then),
                Else = branch.Else is null ? null : RewriteDirectPresentationStatement(branch.Else),
            },
            BoundTryFinally cleanup => cleanup with {
                Body = (BoundBlock)RewriteDirectPresentationStatement(cleanup.Body),
                Finally = (BoundBlock)RewriteDirectPresentationStatement(cleanup.Finally),
            },
            BoundWhile loop => loop with {
                Condition = RewriteDirectPresentationExpression(loop.Condition),
                Body = (BoundBlock)RewriteDirectPresentationStatement(loop.Body),
                Iterator = loop.Iterator is null
                    ? null : RewriteDirectPresentationStatement(loop.Iterator),
            },
            BoundLocal local => local with {
                Initializer = RewriteDirectPresentationExpression(local.Initializer),
            },
            BoundExpressionStatement expression => expression with {
                Expression = RewriteDirectPresentationExpression(expression.Expression),
            },
            BoundAssignment assignment => assignment with {
                Value = RewriteDirectPresentationExpression(assignment.Value),
            },
            BoundReturn result => result with {
                Value = result.Value is null ? null : RewriteDirectPresentationExpression(result.Value),
            },
            _ => statement,
        };

        BoundExpression RewriteDirectPresentationExpression(BoundExpression expression) => expression switch {
            BoundCall call => RewriteCall(call),
            BoundBinary binary => binary with {
                Left = RewriteDirectPresentationExpression(binary.Left),
                Right = RewriteDirectPresentationExpression(binary.Right),
            },
            BoundConditional conditional => conditional with {
                Condition = RewriteDirectPresentationExpression(conditional.Condition),
                WhenTrue = RewriteDirectPresentationExpression(conditional.WhenTrue),
                WhenFalse = RewriteDirectPresentationExpression(conditional.WhenFalse),
            },
            _ => expression,
        };

        BoundCall RewriteCall(BoundCall call)
        {
            if (call.Function == targetFunction)
            {
                replaced = true;
                call = rewrite(call);
            }
            return call with {
                Arguments = [.. call.Arguments.Select(RewriteDirectPresentationExpression)],
            };
        }
    }

    [InteractiveWindowTest]
    [Category("InteractiveWindow")]
    public async Task DirectWindowDevicePresentStageShowsAndPresentsOnRealWarp()
    {
        Assert.That(OperatingSystem.IsWindows() && RuntimeInformation.ProcessArchitecture == Architecture.X64, Is.True);
        var program = CompileDirectWindowDevicePresent();
        Assert.That(NativeDx11DirectPresentationContract.MatchesProgram(program), Is.True);
        var image = NativeEmitter.EmitWithNativeImports(program, NativeRuntimePolicy.CheckedExceptions);
        var directory = Directory.CreateTempSubdirectory("terrum-window-device-present-").FullName;
        var executablePath = Path.Combine(directory, "window-device-present.exe");
        try
        {
            await File.WriteAllBytesAsync(executablePath, image).ConfigureAwait(false);
            using var process = Process.Start(new ProcessStartInfo(executablePath) {
                UseShellExecute = false,
                CreateNoWindow = false,
                WorkingDirectory = directory,
            }) ?? throw new InvalidOperationException("Direct window-device-present executable did not start.");
            try
            {
                var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
                var window = IntPtr.Zero;
                while (DateTime.UtcNow < deadline && !process.HasExited)
                {
                    process.Refresh();
                    window = process.MainWindowHandle;
                    if (window != IntPtr.Zero
                        && process.MainWindowTitle == "Terrum DX11 Triangle"
                        && IsWindowVisible(window))
                    {
                        break;
                    }
                    await Task.Delay(50).ConfigureAwait(false);
                }

                Assert.That(process.HasExited, Is.False, "Window-sized WARP acquisition, back-buffer acquisition, clear, or present failed.");
                Assert.That(window, Is.Not.EqualTo(IntPtr.Zero));
                Assert.That(process.MainWindowTitle, Is.EqualTo("Terrum DX11 Triangle"));
                Assert.That(IsWindowVisible(window), Is.True, "The Terrum clear/present window was not visible.");
                Assert.That(PostMessageW(window, WindowCloseMessage, IntPtr.Zero, IntPtr.Zero), Is.True, "The Terrum clear/present window did not accept WM_CLOSE.");
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                Assert.That(process.ExitCode, Is.EqualTo(0));
            }
            catch
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync().ConfigureAwait(false);
                }
                throw;
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static bool ContainsInstructionSequence(
        byte[] bytes,
        params (byte[] Opcode, int OperandSize)[] instructions)
    {
        for (var start = 0; start < bytes.Length; start++)
        {
            var offset = start;
            var found = true;
            foreach (var (opcode, operandSize) in instructions)
            {
                if (offset > bytes.Length - opcode.Length - operandSize
                    || !bytes.AsSpan(offset, opcode.Length).SequenceEqual(opcode))
                {
                    found = false;
                    break;
                }
                offset += opcode.Length + operandSize;
            }
            if (found)
            {
                return true;
            }
        }
        return false;
    }

    private static bool ContainsSequence(byte[] bytes, byte[] sequence) =>
        bytes.AsSpan().IndexOf(sequence) >= 0;

    private static bool ContainsStoredImmediateSequence(byte[] bytes, uint[] immediates)
    {
        for (var start = 0; start < bytes.Length; start++)
        {
            var offset = start;
            var found = true;
            foreach (var immediate in immediates)
            {
                if (offset > bytes.Length - 5 || bytes[offset] != 0xB8
                    || BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset + 1, 4)) != immediate)
                {
                    found = false;
                    break;
                }
                offset += 5;
                if (offset > bytes.Length - 2 || bytes[offset] != 0x89)
                {
                    found = false;
                    break;
                }
                if (bytes[offset + 1] == 0x85)
                {
                    offset += 6;
                }
                else if (offset <= bytes.Length - 3 && bytes[offset + 1] == 0x84 && bytes[offset + 2] == 0x24)
                {
                    offset += 7;
                }
                else
                {
                    found = false;
                    break;
                }
                if (offset > bytes.Length)
                {
                    found = false;
                    break;
                }
            }
            if (found)
            {
                return true;
            }
        }
        return false;
    }
}
